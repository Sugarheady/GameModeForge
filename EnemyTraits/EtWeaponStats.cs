using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using MyBox;          // MinMaxFloat, which is what WeaponBase.Burn is
using UnityEngine;

namespace GameModeForge.EnemyTraits
{
    // ONE OWNER FOR A LIVE ENEMY GUN'S TUNING - a deliberate copy of Module
    // Forge's ModuleForgeWeaponStats (duplicate-by-default), with one more
    // bridge.
    //
    // ★ AN ENEMY GUN'S FIRE RATE ALREADY HAS AN OWNER WHEN WEAPON FORGE OR
    // MODULE FORGE IS INSTALLED (CONCEPT.md 3.6): their `slow` publishes a
    // slowed enemy's fire rate into their table, and that table rewrites
    // `WeaponBase.FireRate` from ONE captured base times every published
    // multiplier. A second table writing the same field from its own base is
    // the ratchet Module Forge's file describes - order-dependent, compounding,
    // invisible in every log. So wind-up and overload PUBLISH:
    //
    //   1. into Weapon Forge's `ForgeWeaponStats` when it is installed;
    //   2. else into Module Forge's `ModuleForgeWeaponStats` (which owns the
    //      field when Weapon Forge is absent);
    //   3. else into this identical local table.
    //
    // Each under its own key, so they multiply with a slow rather than
    // overwrite it. The stat ids are resolved from the other mod BY NAME,
    // never assumed; a stat it cannot name is NOT published (said once) rather
    // than written here while that mod owns the field.
    public static class EtWeaponStats
    {
        private static readonly ManualLogSource Log = EnemyTraitsPlugin.Log;

        public const int FireRate = 0;
        public const int Damage = 1;
        public const int AngleVariance = 2;
        public const int Spread = 3;
        public const int Pellets = 4;
        public const int Cost = 5;
        public const int Range = 6;
        public const int Speed = 7;
        public const int BurstSize = 8;
        public const int BurstDelay = 9;
        public const int Burn = 10;
        public const int Push = 11;
        public const int Knockback = 12;
        public const int Size = 13;
        public const int ExplosionRadius = 14;
        public const int ExplosionDamage = 15;
        public const int Warmup = 16;
        public const int Lifetime = 17;

        private const int Count = 18;

        // The names each mod gives these ids - the same eighteen in both.
        private static readonly string[] IdNames =
        {
            "FireRate", "Damage", "AngleVariance", "Spread", "Pellets", "Cost", "Range", "Speed",
            "BurstSize", "BurstDelay", "Burn", "Push", "Knockback", "Size", "ExplosionRadius",
            "ExplosionDamage", "Warmup", "Lifetime",
        };

        private static readonly string[] Names =
        {
            "fire rate", "damage", "angle variance", "spread", "pellets",
            "cost", "range", "projectile speed", "burst size", "burst delay",
            "burn", "push force", "knockback", "projectile size",
            "explosion radius", "explosion damage", "warmup", "lifetime",
        };

        public static string NameOf(int stat)
        {
            return (stat >= 0 && stat < Count) ? Names[stat] : "?";
        }

        // ------------------------------------------------------------------
        // The bridge
        // ------------------------------------------------------------------

        private static bool _init;
        private static string _owner;        // "Weapon Forge" / "Module Forge" / null = ours
        private static MethodInfo _set;      // (WeaponBase, int, string, float)
        private static MethodInfo _clearOne; // (WeaponBase, int, string)
        private static MethodInfo _clearAll; // (WeaponBase, string)
        private static readonly int[] _theirIds = new int[Count];
        private static bool _announced;

        // Once per run: the other mods are loaded long before any enemy
        // fires, so the first answer in a run is the real one.
        private static void EnsureInit()
        {
            if (_init)
                return;

            _init = true;
            _owner = null;

            if (TryBridge("WeaponForge.ForgeWeaponStats", "Weapon Forge"))
                return;

            TryBridge("ModuleForge.ModuleForgeWeaponStats", "Module Forge");
        }

        private static bool TryBridge(string typeName, string owner)
        {
            try
            {
                Type t = EtInterop.FindTypeQuietly(typeName);

                if (t == null)
                    return false;

                MethodInfo set = t.GetMethod("Set", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(WeaponBase), typeof(int), typeof(string), typeof(float) }, null);
                MethodInfo one = t.GetMethod("Clear", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(WeaponBase), typeof(int), typeof(string) }, null);
                MethodInfo all = t.GetMethod("Clear", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(WeaponBase), typeof(string) }, null);

                bool core = TryId(t, FireRate) & TryId(t, Damage);

                for (int i = 2; i < Count; i++)
                    TryId(t, i);

                if (set == null || one == null || all == null || !core)
                {
                    // ★ Present but unreachable: refuse to publish at all
                    // rather than fall back to our own table while that mod
                    // writes the same field. Losing the reward beats a ratchet.
                    Log.LogWarning(
                        owner + " is installed but its weapon-stat table could not be reached, so the " +
                        "wind-up and overload rewards are OFF this run (writing them from a second table " +
                        "would fight that mod over the same gun). Update the two mods together.");
                    _owner = owner;
                    _set = null;
                    return true;
                }

                _owner = owner;
                _set = set;
                _clearOne = one;
                _clearAll = all;
                return true;
            }
            catch (Exception e)
            {
                Log.LogError("Resolving " + owner + "'s weapon-stat table failed: " + e);
                return false;
            }
        }

        private static bool TryId(Type t, int mine)
        {
            _theirIds[mine] = -1;

            FieldInfo f = t.GetField(IdNames[mine], BindingFlags.Public | BindingFlags.Static);

            if (f == null || f.FieldType != typeof(int))
                return false;

            _theirIds[mine] = (int)f.GetValue(null);
            return true;
        }

        private static readonly HashSet<int> _saidUnbridged = new HashSet<int>();

        private static void SayUnbridged(int stat)
        {
            if (!_saidUnbridged.Add(stat))
                return;

            Log.LogWarning(
                _owner + " owns an enemy gun's live tuning and its table has no \"" + NameOf(stat) +
                "\", so that wind-up / overload reward does nothing while it is installed. Update the " +
                "mods together.");
        }

        private static void Announce()
        {
            if (_announced || _owner == null)
                return;

            _announced = true;

            Log.LogInfo(
                _owner + " is installed, so IT owns an enemy gun's live fire rate (its `slow`). " +
                "Wind-up and overload are published into its table and multiply with a slow rather " +
                "than overwrite it.");
        }

        // ------------------------------------------------------------------
        // Publishing
        // ------------------------------------------------------------------

        public static void Set(WeaponBase weapon, int stat, string key, float multiplier)
        {
            if (weapon == null || string.IsNullOrEmpty(key) || stat < 0 || stat >= Count)
                return;

            EnsureInit();

            if (_owner != null)
            {
                Announce();

                if (_set == null)
                    return;

                if (_theirIds[stat] < 0)
                {
                    SayUnbridged(stat);
                    return;
                }

                try
                {
                    _set.Invoke(null, new object[] { weapon, _theirIds[stat], key, multiplier });
                }
                catch (Exception e)
                {
                    Log.LogError("Publishing across to " + _owner + " failed: " + e);
                }

                return;
            }

            Local(weapon, stat, key, multiplier);
        }

        public static void Clear(WeaponBase weapon, int stat, string key)
        {
            if (weapon == null || string.IsNullOrEmpty(key) || stat < 0 || stat >= Count)
                return;

            EnsureInit();

            if (_owner != null)
            {
                if (_clearOne == null || _theirIds[stat] < 0)
                    return;

                try
                {
                    _clearOne.Invoke(null, new object[] { weapon, _theirIds[stat], key });
                }
                catch (Exception e)
                {
                    Log.LogError("Withdrawing across to " + _owner + " failed: " + e);
                }

                return;
            }

            LocalClear(weapon, stat, key);
        }

        public static void Clear(WeaponBase weapon, string key)
        {
            if (weapon == null || string.IsNullOrEmpty(key))
                return;

            EnsureInit();

            if (_owner != null)
            {
                if (_clearAll == null)
                    return;

                try
                {
                    _clearAll.Invoke(null, new object[] { weapon, key });
                }
                catch (Exception e)
                {
                    Log.LogError("Withdrawing across to " + _owner + " failed: " + e);
                }

                return;
            }

            for (int i = 0; i < Count; i++)
                LocalClear(weapon, i, key);
        }

        // Run entry. The other mods reset their own tables.
        public static void Reset()
        {
            _local.Clear();
            _init = false;
            _announced = false;
            _saidUnbridged.Clear();
        }

        // ------------------------------------------------------------------
        // The local table - identical semantics, used with neither mod there.
        // ------------------------------------------------------------------

        private class Stat
        {
            public bool captured;
            public float baseValue;
            public float baseValue2;
            public float[] baseList;
            public float lastProduct = float.NaN;
            public readonly List<string> keys = new List<string>();
            public readonly List<float> mults = new List<float>();

            public float Product()
            {
                float p = 1f;

                for (int i = 0; i < mults.Count; i++)
                    p *= mults[i];

                return p;
            }
        }

        private class Entry
        {
            public readonly Stat[] stats = new Stat[Count];

            public Stat Of(int stat)
            {
                if (stats[stat] == null)
                    stats[stat] = new Stat();

                return stats[stat];
            }

            public bool Idle()
            {
                for (int i = 0; i < Count; i++)
                    if (stats[i] != null && stats[i].keys.Count > 0)
                        return false;

                return true;
            }
        }

        private static readonly Dictionary<WeaponBase, Entry> _local = new Dictionary<WeaponBase, Entry>();

        private static void Local(WeaponBase weapon, int stat, string key, float multiplier)
        {
            try
            {
                Entry e;

                if (!_local.TryGetValue(weapon, out e))
                {
                    e = new Entry();
                    _local[weapon] = e;
                }

                Stat s = e.Of(stat);

                if (!s.captured)
                {
                    s.captured = true;
                    s.baseValue = Read(weapon, stat);
                    s.baseValue2 = Read2(weapon, stat);
                    s.baseList = ReadList(weapon, stat);
                }

                int at = s.keys.IndexOf(key);

                if (at < 0)
                {
                    s.keys.Add(key);
                    s.mults.Add(multiplier);
                }
                else
                {
                    s.mults[at] = multiplier;
                }

                Write(weapon, stat, s, s.Product());
            }
            catch (Exception e)
            {
                Log.LogError("Publishing an enemy gun stat failed: " + e);
            }
        }

        private static void LocalClear(WeaponBase weapon, int stat, string key)
        {
            try
            {
                Entry e;

                if (!_local.TryGetValue(weapon, out e))
                    return;

                Stat s = e.stats[stat];

                if (s == null)
                    return;

                int at = s.keys.IndexOf(key);

                if (at < 0)
                    return;

                s.keys.RemoveAt(at);
                s.mults.RemoveAt(at);

                if (s.keys.Count > 0)
                {
                    Write(weapon, stat, s, s.Product());
                }
                else if (s.captured)
                {
                    // Withdrawing the last contributor drops the captured base,
                    // so a later change to the gun is picked up, not pinned.
                    Write(weapon, stat, s, 1f);
                    s.captured = false;
                    s.baseList = null;
                    s.lastProduct = float.NaN;
                }

                if (e.Idle())
                    _local.Remove(weapon);
            }
            catch (Exception ex)
            {
                Log.LogError("Withdrawing an enemy gun stat failed: " + ex);
            }
        }

        private static float Read(WeaponBase w, int stat)
        {
            var pw = w as ProjectileWeapon;

            switch (stat)
            {
                case FireRate: return w.FireRate;
                case Damage: return w.Damage.amount;
                case AngleVariance: return w.AngleVariance;
                case Spread: return w.Spread;
                case Pellets: return w.ProjectileCount;
                case Cost: return w.Cost;
                case Range:
                {
                    var r = w as IHasRangeProperty;
                    return r != null ? r.Range : 0f;
                }
                case Speed:
                {
                    var s = w as IHasSpeedProperty;
                    return s != null ? s.Speed : 0f;
                }
                case BurstSize: return w.BurstSize;
                case BurstDelay: return w.BurstDelay;
                case Burn: return w.Burn.Max;
                case Push: return w.PushForce;
                case Knockback: return w.KnockbackForce;
                case Size:
                {
                    if (pw != null)
                        return pw.ProjectileRadius;

                    var hw = w as HitscanWeapon;
                    return hw != null ? hw.RayWidth : 0f;
                }
                case ExplosionRadius: return w.Explosion.radius;
                case ExplosionDamage:
                {
                    var d = w.Explosion.damages;
                    return d != null && d.Count > 0 ? d[0].amount : 0f;
                }
                case Warmup: return w.WarmupTime;
                case Lifetime: return pw != null ? pw.LifetimeData.time : 0f;
                default: return 0f;
            }
        }

        private static float Read2(WeaponBase w, int stat)
        {
            return stat == Burn ? w.Burn.Min : 0f;
        }

        private static float[] ReadList(WeaponBase w, int stat)
        {
            if (stat != ExplosionDamage)
                return null;

            List<Damage> d = w.Explosion.damages;

            if (d == null || d.Count == 0)
                return null;

            var baseline = new float[d.Count];

            for (int i = 0; i < d.Count; i++)
                baseline[i] = d[i].amount;

            return baseline;
        }

        private static void Write(WeaponBase w, int stat, Stat s, float k)
        {
            if (!float.IsNaN(s.lastProduct) && Mathf.Abs(k - s.lastProduct) < 0.000001f)
                return;

            s.lastProduct = k;

            float value = s.baseValue * k;
            var pw = w as ProjectileWeapon;

            switch (stat)
            {
                case FireRate:
                    // 0 is Infinity in Shooter.FireRatePassed: a gun that never fires again.
                    w.FireRate = Mathf.Max(0.01f, value);
                    break;
                case Damage:
                {
                    Damage d = w.Damage;
                    d.amount = Mathf.Max(0f, value);
                    w.Damage = d;
                    break;
                }
                case AngleVariance:
                    w.AngleVariance = Mathf.Max(0f, value);
                    break;
                case Spread:
                    w.Spread = Mathf.Clamp(value, 0f, 360f);
                    break;
                case Pellets:
                    if (s.baseValue > 0f)
                        w.ProjectileCount = Mathf.Max(1f, Mathf.Round(value));
                    break;
                case Cost:
                    w.Cost = Mathf.Max(0f, value);
                    break;
                case Range:
                {
                    var r = w as IHasRangeProperty;
                    if (r != null)
                        r.Range = Mathf.Max(0f, value);
                    break;
                }
                case Speed:
                {
                    var sp = w as IHasSpeedProperty;
                    if (sp != null)
                        sp.Speed = Mathf.Max(0f, value);
                    break;
                }
                case BurstSize:
                    w.BurstSize = Mathf.Max(1, Mathf.RoundToInt(value));
                    break;
                case BurstDelay:
                    w.BurstDelay = Mathf.Max(0f, value);
                    break;
                case Burn:
                    w.Burn = new MinMaxFloat(Mathf.Max(0f, s.baseValue2 * k), Mathf.Max(0f, value));
                    break;
                case Push:
                    w.PushForce = value;
                    break;
                case Knockback:
                    w.KnockbackForce = value;
                    break;
                case Size:
                {
                    if (pw != null)
                    {
                        pw.ProjectileRadius = Mathf.Max(0.001f, value);
                        break;
                    }

                    var hw = w as HitscanWeapon;
                    if (hw != null)
                        hw.RayWidth = Mathf.Max(0.001f, value);
                    break;
                }
                case ExplosionRadius:
                {
                    Explosion ex = w.Explosion;
                    ex.radius = Mathf.Max(0f, value);
                    w.Explosion = ex;
                    break;
                }
                case ExplosionDamage:
                {
                    if (s.baseList == null)
                        break;

                    // DUPLICATE, never edit in place: a shot in the air holds
                    // the list by reference.
                    Explosion ex = w.Explosion.Duplicate();

                    if (ex.damages != null && ex.damages.Count == s.baseList.Length)
                    {
                        for (int i = 0; i < ex.damages.Count; i++)
                            ex.damages[i] = new Damage(Mathf.Max(0f, s.baseList[i] * k), ex.damages[i].damageType);
                    }

                    w.Explosion = ex;
                    break;
                }
                case Warmup:
                    w.WarmupTime = Mathf.Max(0f, value);
                    break;
                case Lifetime:
                {
                    if (pw == null)
                        break;

                    ProjectileLifetimeData lt = pw.LifetimeData;
                    lt.time = Mathf.Max(0.01f, value);
                    pw.LifetimeData = lt;
                    break;
                }
            }
        }
    }
}
