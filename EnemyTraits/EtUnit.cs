using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GameModeForge.EnemyTraits
{
    // ONE ENEMY'S TRAITS, ON ITS BODY. Added when the enemy is decided (or
    // when its body appears later), and gone with the body.
    //
    // ---- THE GUN TRAITS: wind-up and overload --------------------------------
    //
    // ★ ONE METER PER ENEMY, NOT PER GUN (3.6). The Queen has 32+ shooters;
    // every gun on an enemy feeds its wind-up and its overload and takes the
    // reward, so an enemy overloads as one.
    //
    // ★ WHAT "FIRING" IS (3.7, his answer "Pattern = firing"):
    //   - a held trigger, as the game itself defines it: a Shooter whose
    //     trigger is down, whose owner can pay, and that nothing blocks (the AI
    //     lets go when its ammo runs out and waits for it - the meter rests);
    //   - OR a running VOLLEY PATTERN: a `ShootComplexAction` that is enabled
    //     and past its own start delay, gaps included. Volleys press
    //     `Shooter.Shoot()` themselves at their own pace (0.12 to 32 a second,
    //     measured), so a held-trigger meter would never fill on the Queen,
    //     the 12 crosses or the Fly Zapper. With volleys "off" only held fire
    //     counts.
    //
    // ★ THE REWARDS ARE PUBLISHED, NEVER WRITTEN (EtWeaponStats): into Weapon
    // Forge's or Module Forge's table when either is installed, so they
    // multiply with a `slow` instead of fighting it.
    //
    // ★ THE TWO PACES NO TABLE KNOWS (3.7), moved by RATIO (EtMath.Retarget)
    // so New Game Plus's one-time change to the same field survives in any
    // order:
    //   - a BEAM's damage gap (`HitscanWeapon.DamageRepeatDelay`) is divided
    //     by the fire-rate reward. A beam fires 20-60 times a second but hits
    //     a target at most once a gap, so fire rate alone did nothing to it;
    //   - a VOLLEY's interval and burst delay, with volleys on "speed" (the
    //     default): the pattern's fireRate x the reward, burstDelay / it.
    internal sealed class EtUnit : MonoBehaviour
    {
        internal const string KeyWindup = "EnemyTraits.Windup";
        internal const string KeyOverload = "EnemyTraits.Overload";

        internal int instanceId;
        internal EtDecide.Picked picked;
        internal EtNow now;

        private Shooter[] _shooters = new Shooter[0];
        private ShootComplexAction[] _actions = new ShootComplexAction[0];

        // The meters.
        private float _spin;
        private float _meter;
        private float _windowLeft;
        private float _lastFiringAt = -999f;

        // ★ Nothing is published for the first moments of a body: New Game
        // Plus decides the same enemy's guns two frames after it appears and
        // writes its fire-rate buff then, and a stat table that captured its
        // base before that write would put the gun back on the next frame.
        private float _armedAt;

        private sealed class Pub
        {
            public float windup = -1f;
            public float overload = -1f;
            public int pellets = -1;
        }

        private readonly Dictionary<WeaponBase, Pub> _pub = new Dictionary<WeaponBase, Pub>();
        private readonly Dictionary<Shooter, WeaponBase> _held = new Dictionary<Shooter, WeaponBase>();
        private readonly Dictionary<HitscanWeapon, float> _gap = new Dictionary<HitscanWeapon, float>();
        private readonly Dictionary<ShootComplexAction, float> _pace = new Dictionary<ShootComplexAction, float>();

        private bool _broken;
        private bool _hot;
        private EtGauge _gauge;

        internal bool Has(int trait)
        {
            return picked != null && picked.has[trait];
        }

        internal float WindupFraction
        {
            get { return Has(EtMath.Windup) ? EtMath.Fraction(_spin, now.stepped, now.steps) : 0f; }
        }

        internal float OverloadFraction
        {
            get { return Has(EtMath.Overload) ? EtMath.WindowFraction(_windowLeft, now.window, now.flat) : 0f; }
        }

        internal bool Overloaded
        {
            get { return _windowLeft > 0f; }
        }

        internal float Meter
        {
            get { return _meter; }
        }

        // The enemy that owns `unit`'s shots, or null.
        internal static EtUnit Of(Component c)
        {
            return c != null ? c.GetComponentInParent<EtUnit>() : null;
        }

        internal void Set(int id, EtDecide.Picked p)
        {
            instanceId = id;
            picked = p;
            now = EtRun.Now;
            _armedAt = Time.time + 0.3f;
            EtReflect.SayIfMissing();

            try
            {
                _shooters = GetComponentsInChildren<Shooter>(true);
                _actions = GetComponentsInChildren<ShootComplexAction>(true);
            }
            catch (Exception)
            {
                _shooters = new Shooter[0];
                _actions = new ShootComplexAction[0];
            }
        }

        // ---- the per-frame work ---------------------------------------------------

        private void Update()
        {
            if (_broken || picked == null || now == null)
                return;

            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                // Latch off rather than throw every frame for every enemy.
                _broken = true;
                EnemyTraitsPlugin.Log.LogError(
                    "an enemy's wind-up / overload stopped working (it keeps what it had): " + ex);
            }
        }

        private void Tick()
        {
            bool wu = Has(EtMath.Windup), ol = Has(EtMath.Overload);

            if (!wu && !ol)
                return;

            float t = Time.time, dt = Time.deltaTime;
            bool firing = HeldFiring() || (now.volleys != EtMath.VolleysOff && VolleyFiring());

            if (firing)
                _lastFiringAt = t;

            float since = t - _lastFiringAt;

            if (wu)
                _spin = EtMath.Spin(_spin, firing, dt, since, now.windUpTime, now.windDownTime, now.grace);

            if (ol)
            {
                if (_windowLeft > 0f)
                {
                    _windowLeft -= dt;

                    // The window spends the meter: empty when it closes.
                    if (_windowLeft <= 0f)
                    {
                        _windowLeft = 0f;
                        _meter = 0f;
                    }
                }
                else
                {
                    float fill = now.fill == "fire" ? 1f / now.fillTime : 0f;

                    if (EtMath.Heat(ref _meter, firing, dt, since, fill, now.coolDelay, now.coolPerSecond))
                        Blow();
                }
            }

            // The opt-in gauge (off by default, read live).
            if (ol && _gauge == null && EtSettings.OverloadGauge != null && EtSettings.OverloadGauge.Value)
                _gauge = EtGauge.Attach(this);

            if (t < _armedAt)
                return;

            float fw = WindupFraction, fo = OverloadFraction;

            // A cold enemy does nothing at all. `_hot` is still true on the
            // frame both meters reach zero, which is what lets the rewards be
            // withdrawn and the paces put back before this starts skipping.
            bool hot = fw > 0.0001f || fo > 0.0001f;

            if (!hot && !_hot)
                return;

            _hot = hot;

            Publish(fw, fo);

            float rate = (wu ? now.windupRewards.RateAt(fw) : 1f) * (ol ? now.overloadRewards.RateAt(fo) : 1f);

            Pace(rate);
        }

        private void Blow()
        {
            _windowLeft = now.window;
            _meter = 1f;
        }

        // A shot fired (Shooter.Shoot, really fired): the "shots" fill.
        internal void NoteShot()
        {
            if (!Has(EtMath.Overload) || _windowLeft > 0f || now.fill != "shots")
                return;

            if (EtMath.AddHeat(ref _meter, 1f / now.shots))
                Blow();
        }

        // A hit on one of your ships: the "hits" fill.
        internal void NoteHit()
        {
            if (!Has(EtMath.Overload) || _windowLeft > 0f || now.fill != "hits")
                return;

            if (EtMath.AddHeat(ref _meter, 1f / now.hits))
                Blow();
        }

        // ---- firing ---------------------------------------------------------------

        private bool HeldFiring()
        {
            for (int i = 0; i < _shooters.Length; i++)
            {
                Shooter s = _shooters[i];

                if (s == null || !s.isActiveAndEnabled || !s.IsShooting || s.Weapon == null)
                    continue;

                if (s.OwnerHasResource && !EtReflect.Blocked(s))
                    return true;
            }

            return false;
        }

        private bool VolleyFiring()
        {
            float t = Time.time;

            for (int i = 0; i < _actions.Length; i++)
            {
                ShootComplexAction a = _actions[i];

                if (a == null || !a.isActiveAndEnabled)
                    continue;

                // A Continuous pattern holds its shooters' triggers - it is
                // already held fire above.
                if (!EtReflect.IsVolley(a))
                    continue;

                if (t - EtReflect.EnterTime(a) > EtReflect.Delay(a))
                    return true;
            }

            return false;
        }

        // ---- the rewards ------------------------------------------------------------

        private void Publish(float fw, float fo)
        {
            for (int i = 0; i < _shooters.Length; i++)
            {
                Shooter s = _shooters[i];

                if (s == null)
                    continue;

                WeaponBase w = s.Weapon;
                WeaponBase was;

                // A rebuilt gun is a NEW object: hand the old one back so the
                // owner's table forgets it.
                if (_held.TryGetValue(s, out was) && !ReferenceEquals(was, w))
                {
                    Drop(was);
                    _held.Remove(s);
                }

                if (w == null || w is MinionSpawnerWeapon)
                    continue;

                _held[s] = w;

                Pub p;

                if (!_pub.TryGetValue(w, out p))
                {
                    p = new Pub();
                    _pub[w] = p;
                }

                bool pellets = false;

                if (Has(EtMath.Windup) && Moved(fw, p.windup))
                {
                    if (fw <= 0.0001f)
                        EtRewards.Withdraw(w, KeyWindup);
                    else
                        pellets |= now.windupRewards.Publish(w, KeyWindup, fw);

                    p.windup = fw;
                }

                if (Has(EtMath.Overload) && Moved(fo, p.overload))
                {
                    if (fo <= 0.0001f)
                        EtRewards.Withdraw(w, KeyOverload);
                    else
                        pellets |= now.overloadRewards.Publish(w, KeyOverload, fo);

                    p.overload = fo;
                }

                if (pellets || p.pellets >= 0)
                    Redress(w, p);
            }
        }

        // Publish only when the fraction really moved - the Queen has 32
        // shooters, and a stat table write is a reflection call.
        private static bool Moved(float f, float last)
        {
            if (last < 0f)
                return f > 0.0001f;

            if (f <= 0.0001f)
                return last > 0.0001f;

            return Mathf.Abs(f - last) > 0.004f;
        }

        // A beam whose count rose without its visuals being rebuilt throws
        // every frame (HitscanWeapon indexes them unguarded) - Module Forge's
        // Redress, on the ROUNDED count, both ways.
        private static void Redress(WeaponBase w, Pub p)
        {
            int n = Mathf.RoundToInt(w.ProjectileCount);

            if (n == p.pellets)
                return;

            p.pellets = n;

            try
            {
                w.InitializeVisuals();
            }
            catch (Exception)
            {
            }
        }

        private void Drop(WeaponBase w)
        {
            if (w == null)
                return;

            EtRewards.Withdraw(w, KeyWindup);
            EtRewards.Withdraw(w, KeyOverload);
            _pub.Remove(w);

            var hw = w as HitscanWeapon;

            if (hw != null)
                _gap.Remove(hw);
        }

        // ---- the paces (3.7) ----------------------------------------------------------

        private void Pace(float rate)
        {
            // A beam's damage gap follows the fire rate, whatever the volleys say.
            foreach (KeyValuePair<Shooter, WeaponBase> kv in _held)
            {
                var hw = kv.Value as HitscanWeapon;

                if (hw == null)
                    continue;

                float target = 1f / Mathf.Max(0.05f, rate);
                float last;

                if (!_gap.TryGetValue(hw, out last))
                    last = 1f;

                if (Mathf.Abs(target - last) < 0.0005f)
                    continue;

                hw.DamageRepeatDelay = Mathf.Max(0.01f, EtMath.Retarget(hw.DamageRepeatDelay, last, target));
                _gap[hw] = target;
            }

            // A volley's pace, on "speed" only.
            float m = now.volleys == EtMath.VolleysSpeed ? rate : 1f;

            for (int i = 0; i < _actions.Length; i++)
            {
                ShootComplexAction a = _actions[i];

                if (a == null || !EtReflect.IsVolley(a))
                    continue;

                float last;

                if (!_pace.TryGetValue(a, out last))
                    last = 1f;

                if (Mathf.Abs(m - last) < 0.0005f)
                    continue;

                float fr = EtReflect.FireRate(a);

                if (fr > 0f)
                    EtReflect.SetFireRate(a, Mathf.Max(0.01f, EtMath.Retarget(fr, last, m)));

                // The delay between bursts shrinks the other way round.
                float bd = EtReflect.BurstDelay(a);

                if (bd > 0f)
                    EtReflect.SetBurstDelay(a, Mathf.Max(0f, EtMath.Retarget(bd, 1f / last, 1f / m)));

                _pace[a] = m;
            }
        }

        // The body is going (killed, or its stretch of the world unloaded):
        // hand every gun back. Its paces die with it.
        private void OnDestroy()
        {
            try
            {
                foreach (KeyValuePair<Shooter, WeaponBase> kv in _held)
                {
                    EtRewards.Withdraw(kv.Value, KeyWindup);
                    EtRewards.Withdraw(kv.Value, KeyOverload);
                }
            }
            catch (Exception)
            {
            }

            _held.Clear();
            _pub.Clear();
        }
    }

    // THE PRIVATE FIELDS ENEMY TRAITS READS, in one place, each made safely
    // (a static initializer that throws takes its whole class down - New Game
    // Plus's `_missingFields` lesson). A field that cannot be reached reads as
    // "not a volley" / "not blocked", said once.
    internal static class EtReflect
    {
        private static readonly AccessTools.FieldRef<ShootComplexAction, float> _fireRate =
            Make<ShootComplexAction, float>("fireRate");
        private static readonly AccessTools.FieldRef<ShootComplexAction, float> _burstDelay =
            Make<ShootComplexAction, float>("burstDelay");
        private static readonly AccessTools.FieldRef<ShootComplexAction, float> _delay =
            Make<ShootComplexAction, float>("delay");
        private static readonly AccessTools.FieldRef<ShootComplexAction, float> _enterTime =
            Make<ShootComplexAction, float>("enterTime");
        private static readonly AccessTools.FieldRef<ShootComplexAction, ShootComplexAction.Behaviour> _behaviour =
            Make<ShootComplexAction, ShootComplexAction.Behaviour>("behaviour");
        private static readonly AccessTools.FieldRef<Shooter, HashSet<object>> _blockers =
            Make<Shooter, HashSet<object>>("blockers");

        private static bool _said;

        private static AccessTools.FieldRef<T, F> Make<T, F>(string name)
        {
            try
            {
                return AccessTools.FieldRefAccess<T, F>(name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static bool Ready
        {
            get
            {
                return _fireRate != null && _burstDelay != null && _delay != null && _enterTime != null &&
                       _behaviour != null && _blockers != null;
            }
        }

        internal static void SayIfMissing()
        {
            if (Ready || _said)
                return;

            _said = true;
            EnemyTraitsPlugin.Log.LogWarning(
                "the game's volley pattern (ShootComplexAction) or Shooter has changed shape - volleys do not " +
                "count toward wind-up and overload and are not sped up, until Enemy Traits is updated.");
        }

        // A pattern that presses Shooter.Shoot itself (Grouped or Once). A
        // Continuous one holds its shooters' triggers instead.
        internal static bool IsVolley(ShootComplexAction a)
        {
            return _behaviour != null && _behaviour(a) != ShootComplexAction.Behaviour.Continous;
        }

        internal static float EnterTime(ShootComplexAction a) { return _enterTime != null ? _enterTime(a) : float.MaxValue; }
        internal static float Delay(ShootComplexAction a) { return _delay != null ? _delay(a) : 0f; }
        internal static float FireRate(ShootComplexAction a) { return _fireRate != null ? _fireRate(a) : 0f; }
        internal static float BurstDelay(ShootComplexAction a) { return _burstDelay != null ? _burstDelay(a) : 0f; }

        internal static void SetFireRate(ShootComplexAction a, float v)
        {
            if (_fireRate != null)
                _fireRate(a) = v;
        }

        internal static void SetBurstDelay(ShootComplexAction a, float v)
        {
            if (_burstDelay != null)
                _burstDelay(a) = v;
        }

        internal static bool Blocked(Shooter s)
        {
            HashSet<object> b = _blockers != null ? _blockers(s) : null;
            return b != null && b.Count > 0;
        }
    }
}
