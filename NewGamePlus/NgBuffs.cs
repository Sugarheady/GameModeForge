using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace GameModeForge.NewGamePlus
{
    // THE TWO NUMBER BUFFS OF STAGE 1: enemy health and enemy count
    // (CONCEPT.md 2.5), bosses included (DECIDED: "the player is going to be
    // strong in the new game, might as well make everything strong too").
    //
    // ---- ENEMY COUNT - the spawn budget -------------------------------------
    //
    // `EnemyGenerator.PlaceBasedOnEcosystem` computes
    // `remainingPowerLevel = radius * difficulty * difficultyMultiplier` and
    // spends it drawing enemies, so scaling `difficultyMultiplier` means more
    // enemies per room (and bigger ones, since a draw may not exceed what is
    // left). World-wide, so no coverage. Two measured limits, said in the docs:
    //   - it can never add a Queen: the boss rooms' PoI has multiplier 0, and
    //     `PlaceEnemies` skips a room whose multiplier is not above 0;
    //   - a room stops at the first enemy it cannot find a free spot for in 5
    //     tries, so +30% is a budget, not a head count.
    //
    // ---- ENEMY HEALTH - the game's own dial ---------------------------------
    //
    // An enemy's health is a `ModifyResourceCapacity` on its embedded module.
    // Co-op already multiplies it (`Enemy.Initialize`, and again on load in
    // `ModuleGridOwner.Data.RestoreFromMemento`); NG+ multiplies on top.
    //
    // ★ THE ID TRAP (measured 2026-09-29). `Enemy.Initialize` runs inside
    // `CreateData()`, BEFORE the generator gives the enemy its instance id - so
    // a coverage roll there would give every enemy the same answer. NG+ acts
    // once the entity is ADDED (its id is set by then).
    //
    // ★ AND NOT EVEN THEN: A FRAME OR TWO LATER. The player's own drones and
    // minions ARE enemy prefabs (Enemy + AIAgent), and every path that makes
    // one - the game's gadget, Weapon Forge's swarm and wingman bodies - calls
    // `CreateEntity` (which Adds it) BEFORE `SetOwner`. At Add a drone looks
    // exactly like a wild enemy. So an added enemy is queued, and decided two
    // frames later, when its owner is known: anything owned (at any depth) by
    // a ship is left alone. A wild enemy is out of reach that long anyway.
    //
    // ★ HEALTH IS WHAT THE ENEMY TAKES DAMAGE THROUGH - not "Resource Health".
    // Measured on the embedded modules: the drones take damage through Caps,
    // Electron or Purple, the Crawler through Tech, and several enemies also
    // carry White / Fuel / Caps tanks that are ammo. Co-op scales every one of
    // them; NG+ scales only the resource the prefab's `DamagableResource` uses.
    //
    // ★ CRATES ARE NOT ENEMIES. Boxes and crates carry `Enemy` too; the one
    // thing none of them has is an `AIAgent` (Weapon Forge measured 60 of 60
    // combatants with one, 0 of 13 containers). Both are required here.
    //
    // ★ THE SAME ENEMY KEEPS THE SAME BUFF, with nothing stored per enemy
    // (2.6): covered = roll(NG+ seed, instance id) < coverage. A save keeps the
    // ids, NG+'s record keeps the seed.
    internal static class NgBuffs
    {
        private const int SaltHealth = 1;

        private struct Pending
        {
            public EntityData entity;
            public bool fresh;      // made this world (fill the new health) vs restored by a load
            public int frame;
        }

        private static readonly Queue<Pending> _queue = new Queue<Pending>();

        // Which enemies carry a buff this world - the marker and WORLD INFO.
        private static readonly HashSet<int> _buffed = new HashSet<int>();

        // The module instances already multiplied. A restored enemy gets a NEW
        // module instance (Module.Memento.Restore deep-copies from the asset),
        // which is not in here, so it is multiplied again - exactly once.
        private static ConditionalWeakTable<Module, object> _done =
            new ConditionalWeakTable<Module, object>();

        private static readonly object Marked = new object();

        // entityId -> the resource that enemy takes damage through (null =
        // none found, which is said once and then skipped).
        private static readonly Dictionary<string, Resource> _damageResource =
            new Dictionary<string, Resource>();

        private static readonly HashSet<string> _saidNoHealth = new HashSet<string>();

        // Counted per world, reported once the first wave has been decided.
        private static int _applied, _uncovered, _owned, _noHealth;
        private static int _sinceReport;
        private static float _reportAfter = -1f;
        private static bool _saidCount;

        internal static void Reset()
        {
            _queue.Clear();
            _buffed.Clear();
            _done = new ConditionalWeakTable<Module, object>();
            _saidNoHealth.Clear();
            _applied = _uncovered = _owned = _noHealth = _sinceReport = 0;
            _reportAfter = -1f;
            _saidCount = false;

            // The prefab lookup is a fact about the game's assets, not about
            // a run, but it holds Resource references from a registry that is
            // rebuilt with the scene - cheap to redo.
            _damageResource.Clear();
        }

        internal static bool IsBuffed(int instanceId)
        {
            return _buffed.Contains(instanceId);
        }

        internal static int BuffedCount
        {
            get { return _buffed.Count; }
        }

        // ---- enemy count ----------------------------------------------------

        internal static void ScaleBudget(ref float difficultyMultiplier)
        {
            float pct = NgRun.CountPercent;

            if (pct <= 0f)
                return;

            difficultyMultiplier *= NgMath.Multiplier(pct);

            if (!_saidCount)
            {
                _saidCount = true;
                NewGamePlusPlugin.Log.LogInfo(
                    "enemy count " + NgMath.Pct(pct) + ": every room's spawn budget is x" +
                    NgMath.Multiplier(pct).ToString("0.##") + " in " + NgMath.WorldName(NgRun.Plus) +
                    " (a budget, not a head count - a crowded room stops early; boss rooms are " +
                    "never given ecosystem enemies, so there are still four Queens).");
            }
        }

        // ---- enemy health ---------------------------------------------------

        // EntityManager.Add, postfix: a NEW entity of this world.
        internal static void OnAdded(EntityData e)
        {
            if (e == null || NgRun.Loading || !Wanted())
                return;

            if (IsEnemy(e))
                _queue.Enqueue(new Pending { entity = e, fresh = true, frame = Time.frameCount });
        }

        // GameSaver.Load, postfix: every enemy the save brought back. Their
        // modules were rebuilt from the save, so each is multiplied again -
        // and keeps the health it was saved with.
        internal static void OnLoaded()
        {
            if (!Wanted())
                return;

            EntityManager em;

            if (!ServiceLocator.TryGet<EntityManager>(out em) || em == null)
                return;

            int n = 0;

            foreach (EntityData e in em.GetAllEntities())
            {
                if (e != null && IsEnemy(e))
                {
                    _queue.Enqueue(new Pending { entity = e, fresh = false, frame = Time.frameCount });
                    n++;
                }
            }

            NewGamePlusPlugin.Log.LogInfo(
                "enemy health " + NgMath.Pct(NgRun.HealthPercent) + ": " + n +
                " saved enemies are being re-buffed from the save's rules (the same ones as before).");
        }

        private static bool Wanted()
        {
            return NgRun.HealthPercent > 0f && NgRun.HealthCoverage > 0f;
        }

        internal static void Tick()
        {
            if (_queue.Count == 0)
            {
                if (_reportAfter > 0f && Time.unscaledTime >= _reportAfter)
                    Report();

                return;
            }

            int now = Time.frameCount;
            int budget = 400;

            while (_queue.Count > 0 && budget-- > 0)
            {
                Pending p = _queue.Peek();

                // Two frames: late enough for SetOwner, whatever made the unit.
                if (p.frame > now - 2)
                    break;

                _queue.Dequeue();

                try
                {
                    Apply(p);
                }
                catch (Exception ex)
                {
                    // One enemy that cannot be buffed stays as the game made it.
                    NewGamePlusPlugin.Log.LogWarning(
                        "could not buff enemy " + (p.entity != null ? p.entity.entityId : "?") +
                        " (" + ex.GetType().Name + ": " + ex.Message + ") - it keeps its normal health.");
                }
            }

            _reportAfter = Time.unscaledTime + 2f;
        }

        private static void Apply(Pending p)
        {
            EntityData e = p.entity;

            Unit.Data unit;
            ModuleGridOwner.Data grid;

            if (!e.TryGetComponent<Unit.Data>(out unit) || unit == null ||
                !e.TryGetComponent<ModuleGridOwner.Data>(out grid) || grid == null ||
                grid.ModuleGrid == null)
            {
                return;
            }

            if (OwnedByAShip(unit))
            {
                _owned++;
                _sinceReport++;
                return;
            }

            if (!NgMath.Covered(NgRun.Seed, e.instanceId, SaltHealth, NgRun.HealthCoverage))
            {
                _uncovered++;
                _sinceReport++;
                return;
            }

            Resource res = DamageResourceOf(e.entityId);

            if (res == null)
            {
                _noHealth++;
                _sinceReport++;

                if (_saidNoHealth.Add(e.entityId ?? "?"))
                {
                    NewGamePlusPlugin.Log.LogWarning(
                        "enemy '" + e.entityId + "' has no resource it takes damage through that " +
                        "New Game Plus can find - it is left at normal health.");
                }

                return;
            }

            IModuleCluster passive = grid.ModuleGrid.GetCluster(ClusterType.Passive);

            if (passive == null || !passive.HasMainModule || passive.MainModule == null)
                return;

            Module module = passive.MainModule;
            object seen;

            if (_done.TryGetValue(module, out seen))
            {
                _buffed.Add(e.instanceId);
                return;
            }

            float mult = NgMath.Multiplier(NgRun.HealthPercent);
            bool touched = false;

            foreach (ModuleEffect fx in module.Effects)
            {
                ModifyResourceCapacity cap = fx as ModifyResourceCapacity;

                if (cap == null || cap.resource != res)
                    continue;

                // The whole series, not just its first term - a level-2 enemy
                // must be as much tougher as a level-1 one. (`delta` is a
                // struct field of a class, so this writes the real value.)
                cap.delta.baseValue *= mult;

                if (cap.delta.increaseMethod == FloatSeries.IncreaseMethod.Add)
                    cap.delta.change *= mult;

                touched = true;
            }

            if (!touched)
            {
                _noHealth++;
                _sinceReport++;
                return;
            }

            _done.Add(module, Marked);

            ResourceTank tank = unit.GetTank(res);
            float oldCap = tank != null ? tank.Capacity : 0f;
            float oldValue = tank != null ? tank.Value : 0f;

            unit.RecalculateStats(grid.ModuleGrid);

            tank = unit.GetTank(res);

            // A new enemy was made full, so it stays full at the new size. A
            // restored one keeps the health it was saved on - which, for an
            // enemy already buffed last session, only fits under the new cap.
            if (p.fresh && tank != null && oldValue >= oldCap - 0.01f)
                tank.Value = tank.Capacity;

            _buffed.Add(e.instanceId);
            _applied++;
            _sinceReport++;

            NgMarker.MaybeAttach(e.instanceId);
        }

        private static void Report()
        {
            _reportAfter = -1f;

            if (_sinceReport == 0)
                return;

            _sinceReport = 0;

            NewGamePlusPlugin.Log.LogInfo(
                "enemy health " + NgMath.Pct(NgRun.HealthPercent) + " on " +
                Math.Round(NgRun.HealthCoverage) + "% of enemies (" + NgMath.WorldName(NgRun.Plus) +
                "): " + _applied + " buffed so far, " + _uncovered + " not in the covered share, " +
                _owned + " yours (drones and minions are never buffed)" +
                (_noHealth > 0 ? ", " + _noHealth + " with no health New Game Plus could find" : "") + ".");
        }

        private static bool IsEnemy(EntityData e)
        {
            Enemy.Data enemy;
            AIAgent.Data agent;

            return e.TryGetComponent<Enemy.Data>(out enemy) &&
                   e.TryGetComponent<AIAgent.Data>(out agent);
        }

        // Owned by a player ship at any depth: a drone, a drone's drone.
        // (NgDamage asks the same question of an attacker.)
        internal static bool OwnedByAShip(Unit.Data unit)
        {
            EntityData owner = unit.Owner;

            for (int depth = 0; owner != null && depth < 8; depth++)
            {
                if (owner.entityId == "Ship")
                    return true;

                Unit.Data next;

                if (!owner.TryGetComponent<Unit.Data>(out next) || next == null)
                    return false;

                owner = next.Owner;
            }

            return false;
        }

        private static Resource DamageResourceOf(string entityId)
        {
            Resource found;

            if (entityId == null)
                return null;

            if (_damageResource.TryGetValue(entityId, out found))
                return found;

            found = null;

            try
            {
                SavablesCollection all;

                if (ServiceLocator.TryGet<SavablesCollection>(out all) && all != null)
                {
                    foreach (SavablesCollection.EntityPrefab info in all.savableObjectInfos)
                    {
                        if (info.entityId != entityId || info.prefab == null)
                            continue;

                        DamagableResource d = info.prefab.GetComponentInChildren<DamagableResource>(true);

                        if (d != null)
                            found = d.resource;

                        break;
                    }
                }
            }
            catch (Exception)
            {
                found = null;
            }

            _damageResource[entityId] = found;
            return found;
        }
    }

    // The opt-in marker (DECIDED 2026-09-29: OFF by default). A tiny amber
    // square above a buffed enemy - never a colour tint on the enemy itself,
    // because the game's hit flash owns a unit's colour (Weapon Forge's tint
    // contract). It follows the enemy without turning with it.
    internal sealed class NgMarker : MonoBehaviour
    {
        private static Sprite _sprite;

        private Transform _target;
        private float _height;

        internal static void MaybeAttach(int instanceId)
        {
            if (NgSettings.EnemyMarker == null || !NgSettings.EnemyMarker.Value)
                return;

            EntityGameObjectManager manager;
            SavableEntity se;

            if (!ServiceLocator.TryGet<EntityGameObjectManager>(out manager) || manager == null ||
                !manager.TryGetSavableEntity(instanceId, out se) || se == null)
            {
                return;
            }

            Attach(se);
        }

        // EntityGameObjectManager.SpawnPrefabForEntity, postfix: an enemy's
        // GameObject appears (its segment loaded) after it was buffed.
        internal static void OnSpawned(SavableEntity se)
        {
            if (se == null || se.EntityData == null)
                return;

            if (NgSettings.EnemyMarker == null || !NgSettings.EnemyMarker.Value)
                return;

            if (NgBuffs.IsBuffed(se.EntityData.instanceId))
                Attach(se);
        }

        private static void Attach(SavableEntity se)
        {
            try
            {
                if (se.GetComponentInChildren<NgMarker>(true) != null)
                    return;

                if (_sprite == null)
                {
                    var tex = new Texture2D(3, 3, TextureFormat.RGBA32, false);
                    tex.filterMode = FilterMode.Point;

                    var px = new Color32[9];
                    for (int i = 0; i < px.Length; i++)
                        px[i] = new Color32(255, 255, 255, 255);

                    tex.SetPixels32(px);
                    tex.Apply();

                    // PPU 20 is the game's own: 3 pixels = 0.15 units.
                    _sprite = Sprite.Create(tex, new Rect(0, 0, 3, 3), new Vector2(0.5f, 0.5f), 20f);
                }

                var go = new GameObject("NgMarker");
                go.transform.SetParent(se.transform, false);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _sprite;
                sr.color = new Color(1f, 0.6f, 0.15f, 0.95f);

                // Draw where the enemy draws, just above it.
                SpriteRenderer body = se.GetComponentInChildren<SpriteRenderer>();

                if (body != null && body != sr)
                {
                    sr.sortingLayerID = body.sortingLayerID;
                    sr.sortingOrder = body.sortingOrder + 50;
                }

                float height = 1f;
                Collider2D col = se.GetComponentInChildren<Collider2D>();

                if (col != null)
                    height = col.bounds.extents.y + 0.45f;

                var m = go.AddComponent<NgMarker>();
                m._target = se.transform;
                m._height = height;
                m.LateUpdate();
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }

        private void LateUpdate()
        {
            if (_target == null)
                return;

            transform.position = _target.position + new Vector3(0f, _height, -0.01f);
            transform.rotation = Quaternion.identity;
        }
    }

    // ---- the patches ------------------------------------------------------

    [HarmonyPatch(typeof(EnemyGenerator), "PlaceBasedOnEcosystem")]
    internal static class NgCountPatch
    {
        static void Prefix(ref float difficultyMultiplier)
        {
            try
            {
                NgBuffs.ScaleBudget(ref difficultyMultiplier);
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError("New Game Plus could not scale a room's spawn budget: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(EntityManager), "Add")]
    internal static class NgAddPatch
    {
        static void Postfix(EntityData entity)
        {
            try
            {
                NgBuffs.OnAdded(entity);
            }
            catch (Exception)
            {
                // Called for every entity in the game; it must never throw.
            }
        }
    }

    [HarmonyPatch(typeof(EntityGameObjectManager), "SpawnPrefabForEntity")]
    internal static class NgSpawnPatch
    {
        static void Postfix(SavableEntity __result)
        {
            try
            {
                NgMarker.OnSpawned(__result);
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }
    }
}
