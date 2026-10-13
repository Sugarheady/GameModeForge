using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MyBox;
using UnityEngine;

namespace GameModeForge.NewGamePlus
{
    // ENEMY WEAPON STATS - stage 2's second piece (built 2026-10-05). The rules
    // are NgWeaponRules (NgRules.cs); this applies them.
    //
    // ---- WHO -----------------------------------------------------------------
    //
    // Each enemy is decided where its health is: two frames after it appears
    // (NgBuffs' queue - the owner trap), so a drone of yours is never touched,
    // and every stat rolls on its own: covered = roll(NG+ seed, instance id,
    // the stat's salt) < the stat's share this world. Nothing is stored per
    // enemy in the save: a Continue rolls the same ids and gets the same guns.
    //
    // ---- WHERE ---------------------------------------------------------------
    //
    // ★ AN ENEMY'S GUN IS BUILT BEFORE THE ENEMY IS DECIDED. Measured: a
    // `ModuleSlotWeaponHolder` builds it as the body binds to its data
    // (`OnGridOwnerBound` -> `RefreshWeapon` -> `RecreateWeapon` ->
    // `WeaponFactory.Create`), and 16 enemies (the Queen and the cross and
    // officer variants) carry a `StaticWeaponHolder` too, which builds its gun
    // ONCE, in `Start`. Both happen as the body appears - before the two-frame
    // decision. So the decision changes the LIVE guns in place (every holder
    // on the body), and from then on every rebuild - a cluster refresh, the
    // body coming back when its stretch of the world reloads - is changed as
    // `WeaponFactory.Create` returns, inside a bracket that says which
    // enemy's holder is building. Each gun object is changed once.
    //
    // ★ ONLY THE HOLDER'S OWN GUN, never a sub-emitter's: `Create` builds a
    // sub-emitter by calling itself, so only the outermost Create in the
    // bracket counts.
    //
    // ---- WHAT -----------------------------------------------------------------
    //
    //   fire rate  shots a second x; the gap inside a burst / the same; and on a
    //              gun that runs on AMMO the cost a shot / the same - measured,
    //              the AI holds the trigger until the tank is empty and then
    //              waits for it to refill, so otherwise a faster gun would only
    //              empty sooner (10 enemy guns use ammo).
    //              ★ STAGE 3 (2026-10-12, CONCEPT.md 3.7): on a BEAM, the
    //              damage gap / the same. A beam fires 20-60 times a second but
    //              hits a target at most once a `DamageRepeatDelay` (0.1-0.5 s
    //              on all 11 enemy beams), so x FireRate alone changed no beam's
    //              damage - his answer: "Shorten the damage gap". And a scripted
    //              VOLLEY (`ShootComplexAction`: the Queen's bullet patterns, 12
    //              crosses, the Fly Zapper) presses Shooter.Shoot at its OWN
    //              pace, so its interval x and burst delay / the same too, with
    //              `enemyWeapons.volleys` on (the default).
    //   reach      whichever limit the gun has, both if both: a shot's range, a
    //              shot's lifetime, a beam's length. The AI still opens fire at
    //              its own distance (`ShootAction.shootingDistance`).
    //   speed      a shot's launch speed, and a rocket's engine too (it settles
    //              at acceleration / (mass x damping) whatever it left at).
    //   push       the shove a hit gives (`pushForce`). Not `knockbackForce`:
    //              that is recoil on the shooter.
    //   burn       a gun that burns burns hotter (x).
    //   ignite     a gun that does not burn gets a burn of its own a hit.
    //   projectiles  + whole bullets; on a gun with NO spread they fan out
    //              FanDegrees apart, or the extra bullet would sit exactly on
    //              the first (about two dozen enemy guns).
    //   burst      + whole shots a burst.
    //
    // Never a minion or mine launcher (`MinionSpawnerWeapon`, his "Leave them
    // alone"): what they launch is an enemy, buffed on its own.
    internal static class NgWeapons
    {
        // One salt per stat: each rolls on its own (DECIDED: "each stat is
        // its own buff with its own roll"). Health is 1.
        private const int SaltFireRate = 11, SaltReach = 12, SaltSpeed = 13, SaltPush = 14,
                          SaltBurn = 15, SaltIgnite = 16, SaltProjectiles = 17, SaltBurst = 18;

        internal const float FanDegrees = 8f;

        // What one enemy's guns get.
        internal sealed class Buffs
        {
            internal float fireRate, reach, speed, push, burn, ignite;
            internal int projectiles, burst;
            internal bool rolled;              // got a stat that not every enemy gets (the marker)

            internal bool Any
            {
                get
                {
                    return fireRate > 0f || reach > 0f || speed > 0f || push > 0f || burn > 0f ||
                           ignite > 0f || projectiles > 0 || burst > 0;
                }
            }
        }

        private static readonly Dictionary<int, Buffs> _byId = new Dictionary<int, Buffs>();

        // Each gun object is changed once (a rebuild is a NEW object).
        private static ConditionalWeakTable<WeaponBase, object> _done = new ConditionalWeakTable<WeaponBase, object>();
        private static readonly object Marked = new object();

        // Which enemy's holder is building right now (-1: none), and how deep
        // in WeaponFactory.Create we are (1 = the holder's own gun).
        private static int _buildingFor = -1;
        private static int _createDepth;

        private static int _decided, _withBuffs, _guns, _launchers, _volleys, _sinceReport;

        // Each volley pattern is sped once (a body that reloads is a NEW one).
        private static ConditionalWeakTable<ShootComplexAction, object> _paced =
            new ConditionalWeakTable<ShootComplexAction, object>();
        private static float _reportAfter = -1f;
        private static bool _saidFirst;

        internal static void Reset()
        {
            _byId.Clear();
            _done = new ConditionalWeakTable<WeaponBase, object>();
            _paced = new ConditionalWeakTable<ShootComplexAction, object>();
            _buildingFor = -1;
            _createDepth = 0;
            _decided = _withBuffs = _guns = _launchers = _volleys = _sinceReport = 0;
            _reportAfter = -1f;
            _saidFirst = false;
        }

        // Any weapon stat in force this world?
        internal static bool Wanted()
        {
            return NgRun.Live && NgRun.Rules.enemyWeapons.AnyAt(NgRun.RulesPlus);
        }

        internal static bool IsMarked(int instanceId)
        {
            Buffs b;
            return _byId.TryGetValue(instanceId, out b) && b.rolled;
        }

        internal static int ChangedGuns
        {
            get { return _guns; }
        }

        internal static int PacedVolleys
        {
            get { return _volleys; }
        }

        // ---- the decision (NgBuffs' queue, two frames after the enemy appears)

        internal static void Decide(EntityData e)
        {
            if (e == null || !Wanted())
                return;

            NgWeaponRules r = NgRun.Rules.enemyWeapons;
            int plus = NgRun.RulesPlus, seed = NgRun.Seed, id = e.instanceId;
            var b = new Buffs();

            b.fireRate = Roll(r, "fireRate", SaltFireRate, seed, id, plus, b);
            b.reach = Roll(r, "reach", SaltReach, seed, id, plus, b);
            b.speed = Roll(r, "speed", SaltSpeed, seed, id, plus, b);
            b.push = Roll(r, "push", SaltPush, seed, id, plus, b);
            b.burn = Roll(r, "burn", SaltBurn, seed, id, plus, b);
            b.ignite = Roll(r, "ignite", SaltIgnite, seed, id, plus, b);
            b.projectiles = RollStep(r, "projectiles", SaltProjectiles, seed, id, plus, b);
            b.burst = RollStep(r, "burst", SaltBurst, seed, id, plus, b);

            _decided++;
            _sinceReport++;
            _reportAfter = Time.unscaledTime + 2f;

            if (!b.Any)
                return;

            _byId[id] = b;
            _withBuffs++;

            ChangeLiveGuns(id, e.entityId);

            if (b.rolled)
                NgMarker.MaybeAttach(id);
        }

        private static float Roll(NgWeaponRules r, string stat, int salt, int seed, int id, int plus, Buffs b)
        {
            float cover = r.CoverageOf(stat, plus);

            if (cover <= 0f || !NgMath.Covered(seed, id, salt, cover))
                return 0f;

            if (cover < 100f)
                b.rolled = true;

            return r.ValueOf(stat, plus);
        }

        private static int RollStep(NgWeaponRules r, string stat, int salt, int seed, int id, int plus, Buffs b)
        {
            float cover = r.CoverageOf(stat, plus);

            if (cover <= 0f || !NgMath.Covered(seed, id, salt, cover))
                return 0;

            if (cover < 100f)
                b.rolled = true;

            return r.Step(stat).ValueAt(plus);
        }

        // The guns the body already carries (built before the decision).
        private static void ChangeLiveGuns(int id, string kind)
        {
            EntityGameObjectManager manager;
            SavableEntity se;

            if (!ServiceLocator.TryGet<EntityGameObjectManager>(out manager) || manager == null ||
                !manager.TryGetSavableEntity(id, out se) || se == null)
            {
                return;     // no body yet: its holders build through the bracket later
            }

            foreach (WeaponHolder holder in se.GetComponentsInChildren<WeaponHolder>(true))
            {
                WeaponBase w = holder != null ? holder.Weapon : null;

                if (w != null && Change(w, _byId[id], kind))
                    w.InitializeVisuals();      // a beam's beams and the muzzle flashes follow the count
            }

            PaceVolleys(se, _byId[id]);
        }

        // EntityGameObjectManager.SpawnObjectForEntity, postfix (NgSpawnPatch):
        // a decided enemy's body appears after it was decided - its stretch of
        // the world loaded, and every body that loads is a NEW object. Its guns
        // are changed through the holder bracket; its volley patterns here.
        internal static void OnSpawned(SavableEntity se)
        {
            Buffs b;

            if (se == null || se.EntityData == null || _byId.Count == 0 ||
                !_byId.TryGetValue(se.EntityData.instanceId, out b))
            {
                return;
            }

            PaceVolleys(se, b);
        }

        // ★ THE VOLLEY REACH (stage 3). Each pattern's interval x the fire-rate
        // multiplier and its burst delay / it - once per pattern object. A
        // Continuous pattern holds its shooters' triggers, so the gun's own
        // fire rate already reaches it. Enemy Traits moves the same two fields
        // by a RATIO of what it last applied, so this one-time change survives
        // whichever runs first.
        private static void PaceVolleys(SavableEntity se, Buffs b)
        {
            if (b == null || b.fireRate <= 0f || !NgRun.Rules.enemyWeapons.volleys ||
                VolleyRate == null || VolleyBurstDelay == null || VolleyBehaviour == null)
            {
                return;
            }

            float m = NgMath.Multiplier(b.fireRate);

            foreach (ShootComplexAction a in se.GetComponentsInChildren<ShootComplexAction>(true))
            {
                object seen;

                if (a == null || _paced.TryGetValue(a, out seen))
                    continue;

                _paced.Add(a, Marked);

                if (VolleyBehaviour(a) == ShootComplexAction.Behaviour.Continous)
                    continue;

                if (VolleyRate(a) > 0f)
                    VolleyRate(a) *= m;

                if (VolleyBurstDelay(a) > 0f)
                    VolleyBurstDelay(a) /= m;

                _volleys++;
            }
        }

        // The pattern's private numbers, made safely (a throwing static
        // initializer takes its whole class down).
        private static readonly AccessTools.FieldRef<ShootComplexAction, float> VolleyRate =
            SafeVolley<float>("fireRate");
        private static readonly AccessTools.FieldRef<ShootComplexAction, float> VolleyBurstDelay =
            SafeVolley<float>("burstDelay");
        private static readonly AccessTools.FieldRef<ShootComplexAction, ShootComplexAction.Behaviour> VolleyBehaviour =
            SafeVolley<ShootComplexAction.Behaviour>("behaviour");

        private static AccessTools.FieldRef<ShootComplexAction, T> SafeVolley<T>(string name)
        {
            try
            {
                return AccessTools.FieldRefAccess<ShootComplexAction, T>(name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---- every later build -------------------------------------------------

        internal static void EnterHolder(Component holder, out int previous)
        {
            previous = _buildingFor;
            _buildingFor = -1;

            if (_byId.Count == 0 || holder == null)
                return;

            SavableEntity se = holder.GetComponentInParent<SavableEntity>();

            if (se != null && se.EntityData != null && _byId.ContainsKey(se.EntityData.instanceId))
                _buildingFor = se.EntityData.instanceId;
        }

        internal static void ExitHolder(int previous)
        {
            _buildingFor = previous;
        }

        internal static void CreateEntered()
        {
            _createDepth++;
        }

        internal static void CreateLeft()
        {
            if (_createDepth > 0)
                _createDepth--;
        }

        // WeaponFactory.Create, postfix. The holder bracket says whose gun it
        // is; depth 1 is the holder's own gun (a sub-emitter is depth 2).
        internal static void Built(WeaponBase w)
        {
            Buffs b;

            if (_buildingFor < 0 || _createDepth != 1 || w == null || !_byId.TryGetValue(_buildingFor, out b))
                return;

            // The holder calls InitializeVisuals itself, right after Create.
            Change(w, b, null);
        }

        // ---- the change ------------------------------------------------------------

        private static bool Change(WeaponBase w, Buffs b, string kind)
        {
            object seen;

            if (_done.TryGetValue(w, out seen))
                return false;

            _done.Add(w, Marked);

            // His "Leave them alone": a minion or mine launcher keeps its own numbers.
            if (w is MinionSpawnerWeapon)
            {
                _launchers++;
                return false;
            }

            string before = _saidFirst ? null : Describe(w);
            float m;

            if (b.fireRate > 0f)
            {
                m = NgMath.Multiplier(b.fireRate);
                w.FireRate *= m;

                if (w.BurstDelay > 0f)
                    w.BurstDelay /= m;

                // An ammo gun: the same bullets a minute only if each costs less.
                if (w.Cost > 0f)
                    w.Cost /= m;

                // ★ A beam hits a target once a damage gap, whatever its fire
                // rate (stage 3, his "Shorten the damage gap").
                var beam = w as HitscanWeapon;

                if (beam != null && beam.DamageRepeatDelay > 0f)
                    beam.DamageRepeatDelay /= m;
            }

            var pw = w as ProjectileWeapon;
            var hw = w as HitscanWeapon;

            if (b.reach > 0f)
            {
                m = NgMath.Multiplier(b.reach);

                if (pw != null)
                {
                    ProjectileRangeData rd = pw.RangeData;

                    if (rd.enabled && rd.range > 0f)
                    {
                        rd.range *= m;
                        pw.RangeData = rd;
                    }

                    ProjectileLifetimeData ld = pw.LifetimeData;

                    if (ld.enabled && ld.time > 0f)
                    {
                        ld.time *= m;
                        pw.LifetimeData = ld;
                    }
                }
                else if (hw != null)
                {
                    hw.Range *= m;
                }
            }

            if (b.speed > 0f)
            {
                m = NgMath.Multiplier(b.speed);

                if (pw != null)
                {
                    pw.ProjectileSpeed *= m;

                    if (pw.UsePhysics && Homing != null)
                    {
                        ProjectileHomingData h = Homing(pw);

                        if (h.acceleration > 0f && h.maxSpeed > 0f)
                        {
                            h.acceleration *= m;
                            h.maxSpeed *= m;
                            Homing(pw) = h;
                        }
                    }
                }
                else
                {
                    var phw = w as PhysicsWeapon;

                    if (phw != null)
                        phw.ProjectileSpeed *= m;
                }
            }

            if (b.push > 0f && w.PushForce > 0f)
                w.PushForce *= NgMath.Multiplier(b.push);

            if (b.burn > 0f && w.Burn.Max > 0f)
            {
                m = NgMath.Multiplier(b.burn);
                w.Burn = new MinMaxFloat(w.Burn.Min * m, w.Burn.Max * m);
            }
            else if (b.ignite > 0f && w.Burn.Max <= 0f)
            {
                w.Burn = new MinMaxFloat(b.ignite, b.ignite);
            }

            if (b.projectiles > 0)
            {
                w.ProjectileCount = Mathf.Round(w.ProjectileCount) + b.projectiles;

                // A gun with no spread fans its bullets out (the game spaces
                // them Spread / (count - 1) apart), or they would overlap.
                if (w.Spread <= 0.01f)
                    w.Spread = FanDegrees * (w.ProjectileCount - 1f);
            }

            if (b.burst > 0)
                w.BurstSize += b.burst;

            _guns++;

            if (before != null)
            {
                _saidFirst = true;
                NewGamePlusPlugin.Log.LogInfo(
                    "enemy weapons: the first gun changed this world - " +
                    (w.TemplateData != null ? w.TemplateData.name : "?") +
                    (kind != null ? " on " + kind : "") + ": " + before + "  ->  " + Describe(w) + ".");
            }

            return true;
        }

        private static string Describe(WeaponBase w)
        {
            try
            {
                string s = "fire rate " + Math.Round(w.FireRate, 2) + "/s" +
                           (w.BurstSize > 1 ? ", burst " + w.BurstSize : "") +
                           ", " + Math.Round(w.ProjectileCount) + " projectile(s)" +
                           (w.Spread > 0f ? " over " + Math.Round(w.Spread) + " deg" : "") +
                           (w.Burn.Max > 0f ? ", burn " + Math.Round(w.Burn.Max, 1) : "") +
                           (w.PushForce > 0f ? ", push " + Math.Round(w.PushForce, 1) : "") +
                           (w.Cost > 0f ? ", ammo " + Math.Round(w.Cost, 2) + " a shot" : "");

                var pw = w as ProjectileWeapon;

                if (pw != null)
                {
                    s += ", speed " + Math.Round(pw.ProjectileSpeed, 1);

                    if (pw.RangeData.enabled)
                        s += ", range " + Math.Round(pw.RangeData.range, 1);

                    if (pw.LifetimeData.enabled)
                        s += ", lifetime " + Math.Round(pw.LifetimeData.time, 1) + "s";
                }

                var hw = w as HitscanWeapon;

                if (hw != null)
                    s += ", beam " + Math.Round(hw.Range, 1) + ", hits every " + Math.Round(hw.DamageRepeatDelay, 3) + "s";

                return s;
            }
            catch (Exception)
            {
                return "?";
            }
        }

        // ★ A ROCKET'S ENGINE: `HomingData` has no setter, so it is written
        // through its backing field (the Salvage Shop's measured route).
        private static readonly AccessTools.FieldRef<ProjectileWeapon, ProjectileHomingData> Homing = SafeHoming();

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

        // ---- the report ----------------------------------------------------------

        internal static void Tick()
        {
            if (_reportAfter < 0f || Time.unscaledTime < _reportAfter || _sinceReport == 0)
                return;

            _reportAfter = -1f;
            _sinceReport = 0;

            NewGamePlusPlugin.Log.LogInfo(
                "enemy weapons (" + NgMath.WorldName(NgRun.Plus) + ": " +
                NgRun.Rules.Summary(NgRun.RulesPlus) + "): " + _decided + " enemies decided so far, " +
                _withBuffs + " with at least one weapon stat, " + _guns + " guns changed" +
                (_volleys > 0 ? ", " + _volleys + " volley patterns sped up" : "") +
                (_launchers > 0 ? ", " + _launchers + " minion / mine launchers left alone" : "") + ".");
        }
    }

    // ---- the patches ------------------------------------------------------------

    [HarmonyPatch(typeof(ModuleSlotWeaponHolder), "RecreateWeapon")]
    internal static class NgWeaponsSlotPatch
    {
        static void Prefix(ModuleSlotWeaponHolder __instance, out int __state)
        {
            __state = -1;

            try
            {
                NgWeapons.EnterHolder(__instance, out __state);
            }
            catch (Exception)
            {
            }
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgWeapons.ExitHolder(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(StaticWeaponHolder), "Start")]
    internal static class NgWeaponsStaticPatch
    {
        static void Prefix(StaticWeaponHolder __instance, out int __state)
        {
            __state = -1;

            try
            {
                NgWeapons.EnterHolder(__instance, out __state);
            }
            catch (Exception)
            {
            }
        }

        static Exception Finalizer(Exception __exception, int __state)
        {
            NgWeapons.ExitHolder(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(WeaponFactory), "Create", new Type[] { typeof(WeaponData), typeof(IEnumerable<Module>) })]
    internal static class NgWeaponsCreatePatch
    {
        static void Prefix()
        {
            NgWeapons.CreateEntered();
        }

        static void Postfix(WeaponBase __result)
        {
            try
            {
                NgWeapons.Built(__result);
            }
            catch (Exception)
            {
                // The game's own gun, unchanged.
            }
        }

        static Exception Finalizer(Exception __exception)
        {
            NgWeapons.CreateLeft();
            return __exception;
        }
    }
}
