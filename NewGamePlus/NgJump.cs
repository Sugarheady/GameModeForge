using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Sirenix.Serialization;
using UnityEngine;

namespace GameModeForge.NewGamePlus
{
    // THE JUMP - leave a cleared world for a new one and keep what you own
    // (CONCEPT.md 2.2, 2.3).
    //
    // A new world is a new Game scene, and the game already builds everything
    // fresh for one: a new Seed and RunData (RunDataInstaller), a new Vault
    // (GameSystemsInstaller), a level generated from the seed, and ships from
    // `ShipManager.PlaceShipEntity`. So the jump is:
    //
    //   1. capture, in the OLD world: each ship's grid memento and tank values,
    //      the vault memento and the run record - through the save's own
    //      serializer, so what crosses is a detached copy exactly as a save
    //      would store it, never a live object of a scene being unloaded;
    //   2. tell the other mods (NgCarry.Leaving) - Weapon Forge takes its ranks;
    //   3. `GameScene.GoToGameScene` with a NEW seed, `isContinue = false`, the
    //      same co-op and devices, and no daily challenge;
    //   4. in the NEW world, where the game would install the starting loadout,
    //      restore the captured grid instead - ★ which matters more than it
    //      looks: every ship's slot LAYOUT is random (the grid's constructor
    //      calls RandomizeSlots), so a fresh ship would not fit the build.
    //      `ModuleGrid.RestoreFromMemento` restores the slot types AND the
    //      modules; it is the game's own Continue path;
    //   5. the vault onto the FRESH vault only (its restore ADDS, and an
    //      ingredient twice throws), the run-record carries the switches ask
    //      for, then NgCarry.Arrived - Weapon Forge gives the ranks back;
    //   6. save the moment the world is running (NgRun), so there is no window
    //      in which quitting lands you back in the old world.
    //
    // No save-file surgery at all - that was the 2026-09-21 plan, and this
    // replaced it.
    //
    // ★ NEVER CARRY RunData WHOLESALE because it happens to be one object: that
    // silently decides every carry switch at once (2.9). Each field is copied
    // on its own switch in `MergeRunData`.
    internal static class NgJump
    {
        internal sealed class ShipCarry
        {
            public int oldId;
            public byte[] grid;
            public Dictionary<string, float> tanks = new Dictionary<string, float>();
        }

        internal sealed class Stashed
        {
            public EntityData data;
            public Vector3 position;
            public LoadoutTemplate loadout;
        }

        internal sealed class Payload
        {
            public int toPlus;
            public int seed;
            public int queensBefore;
            public NgRules rules;

            public readonly List<ShipCarry> ships = new List<ShipCarry>();
            public byte[] vault;
            public byte[] runData;

            // The switches, as they stood when you left.
            public bool grid, vaultOn, money, shop, prices, stations, ingredients, totals, keep;

            // Filled in the new world.
            public int expected = 1;
            public readonly List<Stashed> stash = new List<Stashed>();
            public bool flushed;
        }

        // Set when you leave; taken by the new world's RunData.Initialize.
        internal static Payload Pending;

        // The payload the world being built right now is arriving with.
        internal static Payload Arriving;

        private static bool _leaving;

        internal static bool InProgress
        {
            get { return _leaving || Pending != null || Arriving != null; }
        }

        // ---- 1. leaving ---------------------------------------------------

        internal static void Begin(string from)
        {
            if (InProgress)
                return;

            if (!NgRun.Live)
            {
                NewGamePlusPlugin.Log.LogWarning("NEW GAME PLUS was chosen with no run in progress - ignored.");
                return;
            }

            _leaving = true;

            try
            {
                Payload p = Capture();

                if (p == null)
                {
                    _leaving = false;
                    return;
                }

                NewGamePlusPlugin.Log.LogInfo(
                    "NEW GAME PLUS chosen (" + from + "): leaving " + NgMath.WorldName(NgRun.Plus) +
                    " for " + NgMath.WorldName(p.toPlus) + " - " + p.rules.Summary(p.toPlus) +
                    ". Carrying " + CarriedList(p) + ".");

                // Weapon Forge takes its rank records now, while the old
                // modules still exist.
                NgCarry.RaiseLeaving();

                Pending = p;

                RunArguments args = GameScene.arguments;
                args.seed = NgRun.NewSeed();
                args.isContinue = false;
                args.saveFolder = null;
                args.dailyChallengeData = null;

                GameScene.GoToGameScene(args);
            }
            catch (Exception ex)
            {
                Pending = null;
                NgCarry.Abandon();

                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT START NEW GAME PLUS (" + ex.GetType().Name + ": " + ex.Message +
                    ") - you are still in this world, nothing was changed. " + ex);
            }
            finally
            {
                _leaving = false;
            }
        }

        private static Payload Capture()
        {
            EntityManager em;
            Vault vault;
            RunData run;

            if (!ServiceLocator.TryGet<EntityManager>(out em) || em == null ||
                !ServiceLocator.TryGet<Vault>(out vault) || vault == null ||
                !ServiceLocator.TryGet<RunData>(out run) || run == null)
            {
                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT START NEW GAME PLUS - the world's systems are not all there. Nothing changed.");
                return null;
            }

            var p = new Payload
            {
                toPlus = NgRun.Plus + 1,
                seed = NgRun.NewSeed(),
                queensBefore = NgRun.QueensBefore + run.KilledBossCount,

                // ★ LOCKED HERE: the next world's rules are the file as it is
                // at the jump. An edit made after this applies to the jump
                // after.
                rules = NgRules.LoadFile(),

                grid = NgSettings.CarryGrid.Value,
                vaultOn = NgSettings.CarryVault.Value,
                money = NgSettings.CarryMoney.Value,
                shop = NgSettings.CarryShop.Value,
                prices = NgSettings.CarryPrices.Value,
                stations = NgSettings.CarryStations.Value,
                ingredients = NgSettings.CarryIngredients.Value,
                totals = NgSettings.CarryTotals.Value,
                keep = NgSettings.KeepResources.Value,
            };

            // ★ PAIRED BY ORDER, NEVER BY ID (2.2): the game gives player 1 the
            // ship with the LOWER id, and the new ships get new random ids.
            foreach (EntityData ship in em.GetShips().OrderBy(s => s.instanceId))
            {
                var c = new ShipCarry { oldId = ship.instanceId };

                ModuleGridOwner.Data grid;

                if (ship.TryGetComponent<ModuleGridOwner.Data>(out grid) && grid != null)
                    c.grid = SerializationUtility.SerializeValue(grid.CreateMemento(), DataFormat.Binary);

                Unit.Data unit;

                if (ship.TryGetComponent<Unit.Data>(out unit) && unit != null)
                {
                    foreach (ResourceTank t in unit.GetNotSharedTanks())
                        if (t != null && t.resource != null)
                            c.tanks[t.resource.Id] = t.Value;
                }

                p.ships.Add(c);
            }

            p.vault = SerializationUtility.SerializeValue(vault.CreateMemento(), DataFormat.Binary);
            p.runData = SerializationUtility.SerializeValue(run.CreateMemento(), DataFormat.Binary);

            if (p.ships.Count == 0)
            {
                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT START NEW GAME PLUS - no ship was found to carry. Nothing changed.");
                return null;
            }

            return p;
        }

        private static string CarriedList(Payload p)
        {
            var on = new List<string>();

            if (p.grid) on.Add("ship grid");
            if (p.vaultOn) on.Add("vault");
            if (p.money) on.Add("money");
            if (p.shop) on.Add("shop stock" + (p.prices ? "" : " (base prices)"));
            if (p.stations) on.Add("stations count");
            if (p.ingredients) on.Add("ingredients ever owned");
            if (p.totals) on.Add("run time + kills");

            return (on.Count == 0 ? "nothing" : string.Join(", ", on.ToArray())) +
                   (p.keep ? "; you keep your health, fuel and ammo" : "; you arrive on full health, fuel and ammo");
        }

        // ---- 4. arriving: the ships ---------------------------------------

        // ShipManager.PlaceShipEntitiesToStartPosition, prefix: how many ships
        // this world places (both are placed in the same frame, one after the
        // other, before anything else can look at them).
        internal static void OnPlacingShips(bool isCoop)
        {
            Payload a = Arriving;

            if (a == null)
                return;

            a.expected = (Application.platform == RuntimePlatform.Android || !isCoop) ? 1 : 2;
            a.stash.Clear();
            a.flushed = false;
        }

        // ShipManager.PlaceShipEntity, prefix. False replaces the original for
        // an arriving world: each ship's data is made as the original makes it
        // and set aside, and when the last one is made they are finished and
        // added TOGETHER - because which new ship is player 1 is only known
        // once both have ids.
        internal static bool OnPlacingShip(ShipManager manager, Vector3 position, LoadoutTemplate loadout)
        {
            Payload a = Arriving;

            if (a == null || a.flushed)
                return true;

            ShipsConfig cfg;
            EntityManager em;

            if (!ServiceLocator.TryGet<ShipsConfig>(out cfg) || cfg == null ||
                !ServiceLocator.TryGet<EntityManager>(out em) || em == null)
            {
                NewGamePlusPlugin.Log.LogError(
                    "the new world's ship systems are missing - the ships start on the starting " +
                    "loadout and nothing is carried.");
                Abandon();
                return true;
            }

            EntityData data = cfg.AutoSwichShipPrefab.SavableEntity.CreateData();
            data.instanceId = em.CreateInstanceId();

            a.stash.Add(new Stashed { data = data, position = position, loadout = loadout });

            if (a.stash.Count >= a.expected)
                Flush(manager, em, a);

            return false;
        }

        private static void Flush(ShipManager manager, EntityManager em, Payload a)
        {
            a.flushed = true;

            List<Stashed> ships = a.stash.OrderBy(s => s.data.instanceId).ToList();
            var map = new Dictionary<int, int>();
            var added = new HashSet<EntityData>();

            try
            {
                // The vault and the run record first: nothing about them
                // depends on a ship, and Weapon Forge's rank restore reads the
                // vault when the ships arrive.
                if (a.vaultOn)
                    RestoreVault(a);

                MergeRunData(a);

                for (int i = 0; i < ships.Count; i++)
                {
                    Stashed s = ships[i];
                    ShipCarry carry = i < a.ships.Count ? a.ships[i] : null;

                    Finish(manager, s, carry, a);
                    em.Add(s.data, s.position);
                    added.Add(s.data);

                    if (carry != null)
                        map[carry.oldId] = s.data.instanceId;
                }

                NewGamePlusPlugin.Log.LogInfo(
                    "the new world's " + ships.Count + " ship(s) are placed" +
                    (a.grid ? " with the carried grid(s) - every slot and module where you left it" : " on the starting loadout") +
                    ".");
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT FINISH CARRYING into the new world (" + ex.GetType().Name + ": " +
                    ex.Message + ") - any ship not yet placed starts on the starting loadout. " + ex);

                // ★ EVERY SHIP MUST STILL EXIST. A ship left out is an index
                // error in the game's own SpawnShipGameObjects and a broken run.
                foreach (Stashed s in ships)
                {
                    if (added.Contains(s.data))
                        continue;

                    try
                    {
                        AddStock(manager, em, s);
                    }
                    catch (Exception ex2)
                    {
                        NewGamePlusPlugin.Log.LogError("could not place a ship even on its loadout: " + ex2);
                    }
                }
            }
            finally
            {
                Arriving = null;

                // Weapon Forge gives the ranks back now: the carried modules are
                // installed and no gun has been built from them yet.
                NgCarry.RaiseArrived(map);
            }
        }

        // What the original PlaceShipEntity does, with the carried grid in
        // place of the loadout, and the carried tanks when asked for.
        private static void Finish(ShipManager manager, Stashed s, ShipCarry carry, Payload a)
        {
            ModuleGridOwner.Data grid;
            Unit.Data unit;

            if (!s.data.TryGetComponent<ModuleGridOwner.Data>(out grid) || grid == null)
                throw new InvalidOperationException("the new ship has no module grid");

            bool restored = false;

            if (a.grid && carry != null && carry.grid != null)
            {
                var memento = SerializationUtility.DeserializeValue<ModuleGridOwner.Data.Memento>(
                    carry.grid, DataFormat.Binary);

                if (memento != null)
                {
                    grid.RestoreFromMemento(memento);
                    restored = true;
                }
            }

            if (!restored && s.loadout != null)
                s.loadout.Apply(grid);

            if (s.data.TryGetComponent<Unit.Data>(out unit) && unit != null)
            {
                unit.RecalculateStats(grid.ModuleGrid);
                manager.FillEveryResourceExceptFuel(unit);

                // DECIDED: a new world starts on full resources. The switch
                // keeps what you had instead.
                if (a.keep && carry != null)
                {
                    Resource health = ShipHealth();

                    foreach (ResourceTank t in unit.GetNotSharedTanks())
                    {
                        float v;

                        if (t == null || t.resource == null || !carry.tanks.TryGetValue(t.resource.Id, out v))
                            continue;

                        // A co-op ship that was DEAD when you left would
                        // arrive dead. Its health comes back full instead;
                        // an empty fuel tank is carried as it is.
                        if (health != null && t.resource == health && v <= 0f)
                            continue;

                        t.Value = Mathf.Min(v, t.Capacity);
                    }
                }
            }
        }

        // The resource the ship takes damage through (its health), off the
        // ship prefab the game places.
        private static Resource ShipHealth()
        {
            try
            {
                ShipsConfig cfg;

                if (!ServiceLocator.TryGet<ShipsConfig>(out cfg) || cfg == null || cfg.AutoSwichShipPrefab == null)
                    return null;

                DamagableResource d = cfg.AutoSwichShipPrefab.GetComponentInChildren<DamagableResource>(true);
                return d != null ? d.resource : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void AddStock(ShipManager manager, EntityManager em, Stashed s)
        {
            ModuleGridOwner.Data grid;
            Unit.Data unit;

            if (s.data.TryGetComponent<ModuleGridOwner.Data>(out grid) && grid != null && s.loadout != null)
                s.loadout.Apply(grid);

            if (s.data.TryGetComponent<Unit.Data>(out unit) && unit != null && grid != null)
            {
                unit.RecalculateStats(grid.ModuleGrid);
                manager.FillEveryResourceExceptFuel(unit);
            }

            em.Add(s.data, s.position);
        }

        private static void RestoreVault(Payload a)
        {
            Vault vault;

            if (a.vault == null || !ServiceLocator.TryGet<Vault>(out vault) || vault == null)
                return;

            var memento = SerializationUtility.DeserializeValue<Vault.Memento>(a.vault, DataFormat.Binary);

            if (memento == null)
                return;

            // ★ Onto the FRESH vault only: RestoreFromMemento adds its modules
            // and `ingredients.Add`s each ingredient, which throws on a key
            // that is already there. A new world's vault is empty.
            vault.RestoreFromMemento(memento);

            NewGamePlusPlugin.Log.LogInfo(
                "vault carried: " + vault.ModuleCount + " module(s), " + vault.Ingredients.Count +
                " kind(s) of ingredient.");
        }

        // ---- 5. the run record, one switch at a time -----------------------

        private static void MergeRunData(Payload a)
        {
            RunData run;

            if (a.runData == null || !ServiceLocator.TryGet<RunData>(out run) || run == null)
                return;

            RunData.Memento old = SerializationUtility.DeserializeValue<RunData.Memento>(
                a.runData, DataFormat.Binary);

            if (old == null)
                return;

            RunData.Memento fresh = run.CreateMemento();
            RunData.Memento m = run.CreateMemento();

            if (a.money && old.sharedResources != null)
            {
                // Money is the game's only shared resource (ShipsConfig), and
                // it is the score. Any other shared one follows the same switch.
                foreach (var kv in old.sharedResources)
                    if (m.sharedResources.ContainsKey(kv.Key))
                        m.sharedResources[kv.Key] = kv.Value;
            }

            if (a.shop)
            {
                m.shopItems = old.shopItems ?? m.shopItems;
                m.consumableShopItems = old.consumableShopItems ?? m.consumableShopItems;

                // What the shop has already offered goes with its stock, or it
                // would offer the same modules again.
                m.moduleIdsAddedToShop = Union(old.moduleIdsAddedToShop, fresh.moduleIdsAddedToShop);
            }

            if (a.stations)
                m.unlockedShopCount = NgMath.MergeStationCount(old.unlockedShopCount, fresh.unlockedShopCount);

            if (a.ingredients)
                m.ingredientsEverOwned = Union(old.ingredientsEverOwned, fresh.ingredientsEverOwned);

            // What you have picked up and what has dropped for you follows
            // what you OWN: a carried non-repeating module must not come back
            // in the shop, and the loot tables' repeat damping keeps counting.
            if (a.grid || a.vaultOn)
            {
                m.moduleIdsPickedUp = Union(old.moduleIdsPickedUp, fresh.moduleIdsPickedUp);
                m.droppedModuleIds = Concat(old.droppedModuleIds, fresh.droppedModuleIds);
            }

            if (a.totals)
            {
                m.totalRunTime = old.totalRunTime + fresh.totalRunTime;
                m.killedEnemyCount = old.killedEnemyCount + fresh.killedEnemyCount;
            }

            // A new world has four new Queens. NG+ keeps its own running total.
            m.killedBossCount = 0;

            run.RestoreFromMemento(m);

            if (a.shop && !a.prices)
                ResetPrices(run);

            NewGamePlusPlugin.Log.LogInfo(
                "run record carried - money " + (a.money ? "kept" : "reset") + ", shop " +
                (a.shop ? (a.prices ? "kept with its prices" : "kept at base prices") : "reset") +
                ", stations count " + (a.stations ? m.unlockedShopCount.ToString() : "reset") +
                (a.totals ? ", run time and " + m.killedEnemyCount + " kill(s) carried" : "") + ".");
        }

        // "Off = rebuild the items at base price" (DECIDED, 2.3): every carried
        // item goes back to the price the shop's own table gives it.
        private static void ResetPrices(RunData run)
        {
            ShopItemsConfig config;

            if (!ServiceLocator.TryGet<ShopItemsConfig>(out config) || config == null)
                return;

            int reset = 0;

            foreach (ShopItem item in run.GeneralShopItemList.Items)
            {
                ShopItemConfig c = item != null && item.ModuleTemplate != null
                    ? config.Get(item.ModuleTemplate.Id) : null;

                if (c != null && c.price != null)
                {
                    item.price = new List<Price>(c.price);
                    reset++;
                }
            }

            foreach (ConsumableShopItem item in run.ConsumableShopItems)
            {
                ShopItemConfig c = item != null && item.consumable != null
                    ? config.Get(item.consumable.Id) : null;

                if (c != null && c.price != null)
                {
                    item.price = new List<Price>(c.price);
                    reset++;
                }
            }

            NewGamePlusPlugin.Log.LogInfo("shop prices reset to base on " + reset + " item(s).");
        }

        private static List<string> Union(List<string> a, List<string> b)
        {
            var seen = new HashSet<string>();
            var list = new List<string>();

            foreach (List<string> src in new[] { a, b })
            {
                if (src == null)
                    continue;

                foreach (string s in src)
                    if (s != null && seen.Add(s))
                        list.Add(s);
            }

            return list;
        }

        private static List<string> Concat(List<string> a, List<string> b)
        {
            var list = new List<string>();

            if (a != null)
                list.AddRange(a);

            if (b != null)
                list.AddRange(b);

            return list;
        }

        internal static void Abandon()
        {
            Arriving = null;
            Pending = null;
            NgCarry.Abandon();
        }
    }

    [HarmonyPatch(typeof(ShipManager), "PlaceShipEntitiesToStartPosition")]
    internal static class NgPlaceShipsPatch
    {
        static void Prefix(bool isCoop)
        {
            try
            {
                NgJump.OnPlacingShips(isCoop);
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError("New Game Plus could not prepare the new ships: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(ShipManager), "PlaceShipEntity")]
    internal static class NgPlaceShipPatch
    {
        static bool Prefix(ShipManager __instance, Vector3 position, LoadoutTemplate loadoutTemplate)
        {
            try
            {
                return NgJump.OnPlacingShip(__instance, position, loadoutTemplate);
            }
            catch (Exception ex)
            {
                // Before anything was stashed for this ship: the game's own
                // placement is always the safe answer.
                NewGamePlusPlugin.Log.LogError(
                    "New Game Plus could not place a carried ship - it starts on its loadout: " + ex);
                return true;
            }
        }
    }
}
