using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GameModeForge.SalvageShop
{
    // WHERE EACH UPGRADE IS APPLIED (CONCEPT 19, "Where it is applied").
    //
    //   weapons         on the gun the game builds from that one module, right
    //                   where it builds it: a postfix on WeaponFactory.Create
    //                   while the game is building for THAT module. That is
    //                   where the game applies its own weapon modules
    //                   (IWeaponModifier.Modify), and it runs before Weapon
    //                   Forge captures any base - so to everything downstream an
    //                   upgraded gun is a normal one (2.3).
    //   weapon gadgets  the same, plus the instance's own Cooldown and
    //                   ActivationCost (12.1: the gadget's cooldown is its
    //                   DATA's, never its gun's fire rate).
    //   ship upgrades   the game's own ModifyResourceCapacity /
    //                   ResourceAutoChargeEffect, added to that one Module
    //                   instance's Effects (each Module clones its own, 14.2).
    //   levels          BaseLevel (the grid computes Level = BaseLevel + boost).
    //
    // ★ EVERY ONE IS IDEMPOTENT AND RE-APPLIED, never accumulated: a gun is a
    // fresh build on every grid change, and a level / an effect is tracked so
    // a second apply replaces the first. A load wipes all of it (2.4) and the
    // save's record puts it back through these same calls.
    internal static class SalvageApply
    {
        private static readonly ManualLogSourceHolder Say = new ManualLogSourceHolder();

        // ---- which module the game is building a gun for -------------------

        private static readonly List<Module> _building = new List<Module>();
        private static readonly List<bool> _done = new List<bool>();

        private static void Push(Module m)
        {
            _building.Add(m);
            _done.Add(false);
        }

        private static void Pop()
        {
            int last = _building.Count - 1;

            if (last >= 0)
            {
                _building.RemoveAt(last);
                _done.RemoveAt(last);
            }
        }

        private static readonly FieldInfo HolderCluster =
            AccessTools.Field(typeof(ModuleSlotWeaponHolder), "moduleCluster");

        // A ship's weapon slot: RefreshWeapon reads the cluster's MainModule
        // (Weapon Forge's rank may have swapped its WeaponData in a prefix of
        // its own - the build is matched against the module's LIVE WeaponData,
        // so the order of the two prefixes does not matter).
        [HarmonyPatch(typeof(ModuleSlotWeaponHolder), "RecreateWeapon")]
        public class OnHolderBuild
        {
            static void Prefix(ModuleSlotWeaponHolder __instance, out bool __state)
            {
                __state = false;

                if (!SalvageShopPlugin.Active)
                    return;

                try
                {
                    var cluster = HolderCluster != null ? HolderCluster.GetValue(__instance) as IModuleCluster : null;

                    if (cluster != null && cluster.HasMainModule && cluster.MainModule != null)
                    {
                        Push(cluster.MainModule);
                        __state = true;
                    }
                }
                catch (Exception)
                {
                }
            }

            static Exception Finalizer(Exception __exception, bool __state)
            {
                if (__state)
                    Pop();

                return __exception;
            }
        }

        // A weapon gadget builds its gun on itself, on every cluster refresh.
        [HarmonyPatch(typeof(WeaponBasedActiveModule), "OnContainingClusterRefreshed")]
        public class OnGadgetBuild
        {
            static void Prefix(WeaponBasedActiveModule __instance, out bool __state)
            {
                __state = false;

                if (!SalvageShopPlugin.Active)
                    return;

                Push(__instance);
                __state = true;
            }

            static void Postfix(WeaponBasedActiveModule __instance)
            {
                if (SalvageShopPlugin.Active)
                    GadgetNumbers(__instance);
            }

            static Exception Finalizer(Exception __exception, bool __state)
            {
                if (__state)
                    Pop();

                return __exception;
            }
        }

        [HarmonyPatch(typeof(WeaponFactory), "Create",
                      new Type[] { typeof(WeaponData), typeof(IEnumerable<Module>) })]
        public class OnCreate
        {
            static void Postfix(WeaponData weaponData, WeaponBase __result)
            {
                int top = _building.Count - 1;

                if (top < 0 || __result == null || weaponData == null || _done[top])
                    return;

                Module m = _building[top];

                // ★ ONLY THE GUN OF THAT MODULE. A sub-emitter is built by a
                // nested Create with ITS data (and no modules - never upgraded,
                // 13.3), and must not take the parent's upgrade.
                if (!ReferenceEquals(SalvageCatalog.WeaponOf(m), weaponData))
                    return;

                _done[top] = true;

                SalvageUpgrade u = SalvageRecords.Get(m);

                if (u == null || !u.AnyWeaponStat)
                    return;

                try
                {
                    ApplyWeapon(__result, weaponData, m, u, SalvageRules.Current);
                }
                catch (Exception ex)
                {
                    Say.Once("weapon:" + ex.GetType().Name,
                             "could not put the salvage upgrades on " + Name(m) + " (" + ex.GetType().Name + ": " +
                             ex.Message + ") - it fires as stock until its next rebuild.");
                }
            }
        }

        // ---- the gun -------------------------------------------------------

        // ★ PROJECTILE ROCKETS: `HomingData` has no setter, so the engine's
        // thrust is written through its backing field (16.1).
        private static readonly AccessTools.FieldRef<ProjectileWeapon, ProjectileHomingData> Homing =
            SafeHoming();

        private static AccessTools.FieldRef<ProjectileWeapon, ProjectileHomingData> SafeHoming()
        {
            try
            {
                return AccessTools.FieldRefAccess<ProjectileWeapon, ProjectileHomingData>("<HomingData>k__BackingField");
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static void ApplyWeapon(WeaponBase w, WeaponData t, Module m, SalvageUpgrade u, SalvageRules r)
        {
            bool gadget = m is ActiveModule;
            var pw = w as ProjectileWeapon;
            var hw = w as HitscanWeapon;
            var phw = w as PhysicsWeapon;
            var pd = t as ProjectileWeaponData;

            int n;

            // DAMAGE - the hit only (13.2), +10% of the gun's own hit a step.
            if ((n = u.Count("damage")) > 0)
            {
                float amount = SalvageMath.AddPercentOf(w.Damage.amount, t.damage.amount, r.Amount("damage"), n);
                w.Damage = new Damage(amount, w.Damage.damageType);
            }

            // FIRE RATE - a gun's own; a gadget's is its cooldown (GadgetNumbers).
            if (!gadget && (n = u.Count("fireRate")) > 0 && t.fireRate > 0f)
                w.FireRate = SalvageMath.AddPercentOf(w.FireRate, t.fireRate, r.Amount("fireRate"), n);

            // RANGE - lifetime on a timed shot (lobbed, rockets), range on a
            // plain one, the beam's length on a beam (12.2).
            if ((n = u.Count("range")) > 0)
            {
                float pct = r.Amount("range");

                if (pw != null && pd != null)
                {
                    if (SalvageCatalog.Timed(pd))
                    {
                        ProjectileLifetimeData ld = pw.LifetimeData;
                        ld.time = SalvageMath.AddPercentOf(ld.time, pd.lifetimeData.time, pct, n);
                        pw.LifetimeData = ld;
                    }
                    else
                    {
                        ProjectileRangeData rd = pw.RangeData;
                        rd.range = SalvageMath.AddPercentOf(rd.range, pd.rangeData.range, pct, n);
                        pw.RangeData = rd;
                    }
                }
                else if (hw != null && t is HitscanWeaponData)
                {
                    hw.Range = SalvageMath.AddPercentOf(hw.Range, ((HitscanWeaponData)t).range, pct, n);
                }
            }

            // PROJECTILE SPEED - the launch, as the game's ProjSpeed module
            // does it (14.4); and on a ROCKET the engine too (16.1), because a
            // rocket settles at acceleration / (mass x damping) whatever it
            // was launched at.
            if ((n = u.Count("speed")) > 0)
            {
                float pct = r.Amount("speed");

                if (pw != null && pd != null)
                {
                    pw.ProjectileSpeed = SalvageMath.AddPercentOf(pw.ProjectileSpeed, pd.projectileSpeed, pct, n);

                    if (pw.UsePhysics && Homing != null &&
                        pd.homingData.acceleration > 0f && pd.homingData.maxSpeed > 0f)
                    {
                        ProjectileHomingData h = Homing(pw);
                        h.acceleration = SalvageMath.AddPercentOf(h.acceleration, pd.homingData.acceleration, pct, n);
                        h.maxSpeed = SalvageMath.AddPercentOf(h.maxSpeed, pd.homingData.maxSpeed, pct, n);
                        Homing(pw) = h;
                    }
                }
                else if (phw != null && t is PhysicsWeaponData)
                {
                    phw.ProjectileSpeed = SalvageMath.AddPercentOf(phw.ProjectileSpeed,
                                                                  ((PhysicsWeaponData)t).projectileSpeed, pct, n);
                }
            }

            // SPREAD - -10% of the gun's own spread a step, never below 0.
            if ((n = u.Count("spread")) > 0 && t.spread > 0f && t.spread < 360f)
                w.Spread = SalvageMath.CutPercentOf(w.Spread, t.spread, r.Amount("spread"), n);

            // +1 PROJECTILE. The game sizes the muzzle flashes (and a beam its
            // visuals) in InitializeVisuals, which runs right after this build.
            if ((n = u.Count("projectiles")) > 0)
                w.ProjectileCount = w.ProjectileCount + r.Amount("projectiles") * n;

            // REDUCE COST - the gun's own cost (a gadget's is its activation
            // cost, GadgetNumbers), and every stock add-on that charges per
            // shot on this one gun (18.2).
            int costSteps = u.Count("cost");
            float costPct = r.Amount("cost");

            if (costSteps > 0)
            {
                if (!gadget)
                    w.Cost = SalvageMath.CostAfter(w.Cost, costPct, costSteps);

                CheaperAddOns(w, costPct, costSteps, m);
            }

            // EXPLOSION and SPARK - one more add-on on this gun (14.3). Only a
            // projectile gun applies add-ons at all.
            if (pw != null)
            {
                if ((n = u.Count("explosion")) > 0)
                    AddExplosion(pw, m, n, r, costPct, costSteps);

                if ((n = u.Count("spark")) > 0)
                    AddSpark(pw, m, n, r, costPct, costSteps);
            }
        }

        private static void CheaperAddOns(WeaponBase w, float pct, int steps, Module m)
        {
            if (SalvageAddOns.Safe == null)
            {
                Say.Once("safe", "the game's Health resource could not be found, so reduce cost does not reach " +
                                 "the add-on modules (the gun's own cost is still cut).");
                return;
            }

            List<WeaponAugmentation> list = w.augmentations;

            for (int i = 0; i < list.Count; i++)
            {
                WeaponAugmentation a = list[i];

                // The game's own three, exactly - never one of ours (already
                // cut) or another mod's (its cost is its business).
                if (a == null || a.GetType() != typeof(AddExplosionEffect) &&
                    a.GetType() != typeof(AddDischargeEffect) && a.GetType() != typeof(AddBurnEffect))
                {
                    continue;
                }

                var e = a as AddExplosionEffect;
                var d = a as AddDischargeEffect;
                var b = a as AddBurnEffect;

                if (e != null && e.costPerProjectile > 0f)
                    list[i] = SalvageExplosion.CopyOf(e, SalvageMath.CostAfter(e.costPerProjectile, pct, steps));
                else if (d != null && d.costPerProjectile > 0)
                    list[i] = SalvageSpark.CopyOf(d, SalvageMath.IntCostAfter(d.costPerProjectile, pct, steps));
                else if (b != null && b.costPerProjectile > 0f)
                    list[i] = SalvageBurn.CopyOf(b, SalvageMath.CostAfter(b.costPerProjectile, pct, steps));
            }
        }

        // The gun's own damage type (16.2): its hit's, else its explosion's,
        // else the resource it fires.
        private static global::Resource DamageTypeOf(WeaponBase w)
        {
            if (w.Damage.damageType != null)
                return w.Damage.damageType;

            List<Damage> ds = w.Explosion.damages;

            if (ds != null)
                foreach (Damage d in ds)
                    if (d.damageType != null)
                        return d.damageType;

            return w.ResourceUsed;
        }

        private static void AddExplosion(ProjectileWeapon w, Module m, int n, SalvageRules r,
                                         float costPct, int costSteps)
        {
            global::Resource type = DamageTypeOf(w);

            if (SalvageAddOns.Safe == null || type == null)
            {
                Say.Once("explosion:" + Name(m),
                         Name(m) + ": the salvage explosion is not added - " +
                         (type == null ? "the gun has no damage type to explode in" : "the game's Health resource could not be found") + ".");
                return;
            }

            SalvageStatRule s = r.Stat("explosion");

            // ★ A FLAT SERIES (change 0): the add-on's damage reads
            // GetElement(Module.Level - 1), so it is the same at every level of
            // the weapon module it rides on.
            w.AddAugmentation(new SalvageExplosion
            {
                damageType = type,
                damageAmount = new FloatSeries { baseValue = s.amount * n, change = 0f },
                burn = new FloatSeries { baseValue = 0f, change = 0f },
                explosionRadiusIncrement = s.amount2 * n,
                addImpactExplosion = true,      // on hit ...
                addTimeoutExplosion = true,     // ... and when a timed shot expires (16.3)
                atRange = r.explodeAtRange,     // and, with the setting, at a plain shot's range (18.1)
                realCost = SalvageMath.CostAfter(r.explosionCost, costPct, costSteps),
                realResource = SalvageCatalog.ResourceFor(r.explosionResource),
                costResource = SalvageCatalog.ResourceFor(r.explosionResource),
                Module = m
            });
        }

        private static void AddSpark(ProjectileWeapon w, Module m, int n, SalvageRules r,
                                     float costPct, int costSteps)
        {
            if (SalvageAddOns.Safe == null)
            {
                Say.Once("safe", "the game's Health resource could not be found, so the salvage spark is not added.");
                return;
            }

            // Every player gun ships its spark wiring switched off - subSystem
            // 1 (the player's), layer mask 128 (Entities) - so adding a spark
            // is giving it damage and switching it on (14.3). A gun whose
            // wiring was never set gets the player's.
            DischargeData dd = w.DischargeData;

            if (dd.layerMask.value == 0 || dd.subSystem == ElectricityManager.SubSystemType.None)
            {
                if (dd.layerMask.value == 0)
                    dd.layerMask = 128;

                if (dd.subSystem == ElectricityManager.SubSystemType.None)
                    dd.subSystem = ElectricityManager.SubSystemType.Player;

                w.DischargeData = dd;
            }

            SalvageStatRule s = r.Stat("spark");

            w.AddAugmentation(new SalvageSpark
            {
                damageIncrement = new FloatSeries { baseValue = s.amount * n, change = 0f },
                chainLengthIncrement = Mathf.RoundToInt(s.amount2 * n),
                impact = true,
                timeout = true,
                realCost = SalvageMath.IntCostAfter(r.sparkCost, costPct, costSteps),
                realResource = SalvageCatalog.ResourceFor(r.sparkResource),
                costResource = SalvageCatalog.ResourceFor(r.sparkResource),
                Module = m
            });
        }

        // ---- a weapon gadget's own numbers (12.1) ----------------------------

        private static readonly HashSet<ActiveModule> _gadgetTouched = new HashSet<ActiveModule>();

        internal static void GadgetNumbers(ActiveModule am)
        {
            if (!(am is WeaponBasedActiveModule))
                return;

            try
            {
                SalvageUpgrade u = SalvageRecords.Get(am);
                int nf = u != null ? u.Count("fireRate") : 0;
                int nc = u != null ? u.Count("cost") : 0;

                if (nf == 0 && nc == 0 && !_gadgetTouched.Contains(am))
                    return;

                SalvageRules r = SalvageRules.Current;

                // From the gadget's DATA every time, so a second apply is the
                // same as the first, and an upgrade taken away (a refund
                // cannot, but a record can be dropped) puts the stock back.
                am.Cooldown = SalvageMath.CooldownAfter(SalvageCatalog.CooldownOf(am), r.Amount("fireRate"), nf);
                // Never exactly 0: the gadget cost trap (SalvageMath.ActivationCostAfter).
                am.ActivationCost = SalvageMath.ActivationCostAfter(SalvageCatalog.ActivationCostOf(am), r.Amount("cost"), nc);

                _gadgetTouched.Add(am);
            }
            catch (Exception ex)
            {
                Say.Once("gadget", "could not set a gadget's cooldown / cost (" + ex.Message + ").");
            }
        }

        // ---- levels ------------------------------------------------------------

        private static readonly Dictionary<Module, int> _levels = new Dictionary<Module, int>();

        internal static void ApplyLevel(Module m)
        {
            SalvageUpgrade u = SalvageRecords.Get(m);
            int target = u != null ? Mathf.RoundToInt(u.Count("level") * SalvageRules.Current.Amount("level")) : 0;

            int had;
            _levels.TryGetValue(m, out had);

            int delta = target - had;

            if (delta == 0)
                return;

            // ★ BOTH. The grid recomputes Level = BaseLevel + boost on any
            // change, but only for a boostable module and only on a change; so
            // Level moves now as well, and the next recompute agrees.
            m.BaseLevel += delta;
            m.Level += delta;

            if (target == 0)
                _levels.Remove(m);
            else
                _levels[m] = target;
        }

        // ---- ship resource stats (14.2, 16.5) -----------------------------------

        private static readonly Dictionary<Module, List<ModuleEffect>> _effects =
            new Dictionary<Module, List<ModuleEffect>>();

        internal static void ApplyEffects(Module m)
        {
            List<ModuleEffect> had;

            if (_effects.TryGetValue(m, out had))
            {
                foreach (ModuleEffect e in had)
                    m.Effects.Remove(e);

                _effects.Remove(m);
            }

            SalvageUpgrade u = SalvageRecords.Get(m);

            if (u == null)
                return;

            SalvageRules r = SalvageRules.Current;
            var added = new List<ModuleEffect>();

            foreach (string res in SalvageRules.Resources)
            {
                int cap = u.Count("cap." + res);
                int regen = u.Count("regen." + res);

                if (cap <= 0 && regen <= 0)
                    continue;

                global::Resource resource = SalvageCatalog.ResourceFor(res);

                if (resource == null)
                {
                    Say.Once("res:" + res, "the game's " + SalvageCatalog.ResourceId(res) +
                                           " could not be found, so " + res + " upgrades do nothing.");
                    continue;
                }

                // The game's own two effects (Module Forge builds its capacity
                // and regen the same way). Flat series: the amount is the
                // upgrade's, not the module's level's.
                if (cap > 0)
                {
                    added.Add(new ModifyResourceCapacity
                    {
                        resource = resource,
                        delta = new FloatSeries { baseValue = r.Amount("cap." + res) * cap, change = 0f },
                        Module = m
                    });
                }

                if (regen > 0)
                {
                    added.Add(new ResourceAutoChargeEffect
                    {
                        resource = resource,
                        rechargeRate = new FloatSeries { baseValue = r.Amount("regen." + res) * regen, change = 0f },
                        Module = m
                    });
                }
            }

            if (added.Count == 0)
                return;

            m.Effects.AddRange(added);
            _effects[m] = added;
        }

        // ---- the vault card's numbers ------------------------------------------

        private static readonly FieldInfo BaseWeapon = AccessTools.Field(typeof(WeaponModule), "baseWeapon");

        // A weapon in the VAULT shows its stats off a gun it built in its own
        // constructor. Rebuilt here, through the same build hook, so the card
        // shows the upgraded numbers before the gun is equipped.
        internal static void VaultCardGun(WeaponModule wm)
        {
            if (wm == null || BaseWeapon == null || wm.WeaponData == null)
                return;

            WeaponFactory factory;

            if (!ServiceLocator.TryGet<WeaponFactory>(out factory) || factory == null)
                return;

            Push(wm);

            try
            {
                WeaponBase fresh = factory.Create(wm.WeaponData, null);
                var old = BaseWeapon.GetValue(wm) as WeaponBase;

                BaseWeapon.SetValue(wm, fresh);

                if (old != null && !ReferenceEquals(old, fresh))
                    old.Dispose();
            }
            catch (Exception ex)
            {
                Say.Once("card", "could not rebuild a vault card's numbers (" + ex.Message + ").");
            }
            finally
            {
                Pop();
            }
        }

        // ---- everything on one module, and the rebuild that shows it -----------

        // Levels, ship stats, a gadget's numbers and the vault card - the parts
        // that live on the MODULE. The gun's own stats are put on at its next
        // build (Refresh asks for one).
        internal static void OnModule(Module m)
        {
            if (m == null)
                return;

            try
            {
                ApplyLevel(m);
                ApplyEffects(m);

                var am = m as ActiveModule;

                if (am != null)
                    GadgetNumbers(am);

                var wm = m as WeaponModule;

                if (wm != null)
                    VaultCardGun(wm);
            }
            catch (Exception ex)
            {
                Say.Once("module:" + ex.GetType().Name,
                         "could not apply the salvage upgrades to " + Name(m) + " (" + ex.Message + ").");
            }
        }

        private static readonly MethodInfo GridChanged = AccessTools.Method(typeof(ModuleGrid), "OnModulesChanged");

        private static readonly AccessTools.FieldRef<ModuleGridOwner.Data, bool> StatsDirty = SafeDirty();

        private static AccessTools.FieldRef<ModuleGridOwner.Data, bool> SafeDirty()
        {
            try
            {
                return AccessTools.FieldRefAccess<ModuleGridOwner.Data, bool>("modulesChanged");
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ★ THE GAME'S OWN "THE GRID CHANGED": the private ModuleGrid
        // .OnModulesChanged that Install and Uninstall end with. It refreshes
        // every cluster (so every gun and gadget is rebuilt through the build
        // hook above), recomputes the levels from BaseLevel, and tells the grid
        // widget. The ship's stats (capacity, regen, shields) are recalculated
        // on the owner's next update, the way an install does it.
        internal static void Rebuild(ModuleGrid grid, ModuleGridOwner.Data owner)
        {
            try
            {
                if (grid != null && GridChanged != null)
                    GridChanged.Invoke(grid, null);
            }
            catch (Exception ex)
            {
                Exception e = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Say.Once("rebuild", "could not rebuild a ship grid after an upgrade (" + e.GetType().Name + ": " +
                                    e.Message + ") - it shows on the next grid change.");
            }

            try
            {
                if (owner != null && StatsDirty != null)
                    StatsDirty(owner) = true;
            }
            catch (Exception)
            {
            }
        }

        // Every ship grid, with its owner.
        internal static IEnumerable<KeyValuePair<ModuleGrid, ModuleGridOwner.Data>> ShipGrids()
        {
            var list = new List<KeyValuePair<ModuleGrid, ModuleGridOwner.Data>>();
            EntityManager em;

            if (!ServiceLocator.TryGet<EntityManager>(out em) || em == null)
                return list;

            try
            {
                foreach (EntityData ship in em.GetShips())
                {
                    ModuleGridOwner.Data data;

                    if (ship == null || !ship.TryGetComponent<ModuleGridOwner.Data>(out data) || data == null)
                        continue;

                    var grid = data.ModuleGrid as ModuleGrid;

                    if (grid != null)
                        list.Add(new KeyValuePair<ModuleGrid, ModuleGridOwner.Data>(grid, data));
                }
            }
            catch (Exception)
            {
            }

            return list;
        }

        // After a purchase: put it on, and rebuild whatever shows it.
        internal static void Refresh(Module m)
        {
            OnModule(m);

            foreach (KeyValuePair<ModuleGrid, ModuleGridOwner.Data> g in ShipGrids())
            {
                if (g.Key.Contains(m))
                {
                    Rebuild(g.Key, g.Value);
                    return;
                }
            }
        }

        internal static string Name(Module m)
        {
            try
            {
                return m != null && m.Data != null
                    ? (string.IsNullOrEmpty(m.DisplayName) ? m.Data.name : m.DisplayName.ToUpperInvariant())
                    : "?";
            }
            catch (Exception)
            {
                return "?";
            }
        }

        // Run entry: every module of the last run is gone, and so is what was
        // tracked about it.
        internal static void Reset()
        {
            _building.Clear();
            _done.Clear();
            _levels.Clear();
            _effects.Clear();
            _gadgetTouched.Clear();
            Say.Clear();
        }
    }

    // A once-per-run warning gate (the house rule: a gate that is per session
    // goes quiet on the run where you go looking for it).
    internal sealed class ManualLogSourceHolder
    {
        private readonly HashSet<string> _said = new HashSet<string>();

        public void Once(string key, string text)
        {
            if (_said.Add(key))
                SalvageShopPlugin.Log.LogWarning(text);
        }

        public void Clear()
        {
            _said.Clear();
        }
    }
}
