using UnityEngine;

namespace GameModeForge.EnemyTraits
{
    // ONE REWARD SET, the numbers of one world - a deliberate copy of Module
    // Forge's ModuleForgeRewards (duplicate-by-default), resolved from the
    // rules at the world's plus so every per-plus box is already applied.
    //
    // What a trait still owns itself: its ramp, its clock, the visuals
    // rebuild that `pellets` forces, the beam gap and the volley pace (EtPace)
    // and the ammo cost that follows the fire rate (decided 3.7).
    public sealed class EtRewards
    {
        public bool rate, accuracy, damage, pellets, cheaper, speed, range, size, burst, burn,
                    explosion, push, warmup, lifetime;

        public float fireRateAtFull = 1f, accuracyAtFull, spreadAtFull = 1f, damageAtFull = 1f,
                     pelletsAtFull = 1f, costAtFull = 1f, speedAtFull = 1f, rangeAtFull = 1f,
                     sizeAtFull = 1f, burstAtFull = 1f, burstDelayAtFull = 1f, burnAtFull = 1f,
                     explosionAtFull = 1f, pushAtFull = 1f, knockbackAtFull = 1f, warmupAtFull = 1f,
                     lifetimeAtFull = 1f;

        public static EtRewards Resolve(EtRewardRules r, int fromPlus, int plus)
        {
            return new EtRewards
            {
                rate = r.On("rate"),
                accuracy = r.On("accuracy"),
                damage = r.On("damage"),
                pellets = r.On("pellets"),
                cheaper = r.On("cheaper"),
                speed = r.On("speed"),
                range = r.On("range"),
                size = r.On("size"),
                burst = r.On("burst"),
                burn = r.On("burn"),
                explosion = r.On("explosion"),
                push = r.On("push"),
                warmup = r.On("warmup"),
                lifetime = r.On("lifetime"),

                // A multiplier below this would stop a gun dead (1/0) or run
                // it backwards; the cap is the honest floor.
                fireRateAtFull = Mathf.Max(0.05f, r.At("fireRateAtFull", fromPlus, plus)),
                accuracyAtFull = Mathf.Clamp01(r.At("accuracyAtFull", fromPlus, plus)),
                spreadAtFull = Mathf.Max(0f, r.At("spreadAtFull", fromPlus, plus)),
                damageAtFull = Mathf.Max(0f, r.At("damageAtFull", fromPlus, plus)),
                pelletsAtFull = Mathf.Max(0f, r.At("pelletsAtFull", fromPlus, plus)),
                costAtFull = Mathf.Max(0f, r.At("costAtFull", fromPlus, plus)),
                speedAtFull = Mathf.Max(0f, r.At("speedAtFull", fromPlus, plus)),
                rangeAtFull = Mathf.Max(0f, r.At("rangeAtFull", fromPlus, plus)),
                sizeAtFull = Mathf.Max(0f, r.At("sizeAtFull", fromPlus, plus)),
                burstAtFull = Mathf.Max(0f, r.At("burstAtFull", fromPlus, plus)),
                burstDelayAtFull = Mathf.Max(0f, r.At("burstDelayAtFull", fromPlus, plus)),
                burnAtFull = Mathf.Max(0f, r.At("burnAtFull", fromPlus, plus)),
                explosionAtFull = Mathf.Max(0f, r.At("explosionAtFull", fromPlus, plus)),
                pushAtFull = Mathf.Max(0f, r.At("pushAtFull", fromPlus, plus)),
                knockbackAtFull = Mathf.Max(0f, r.At("knockbackAtFull", fromPlus, plus)),
                warmupAtFull = Mathf.Max(0f, r.At("warmupAtFull", fromPlus, plus)),
                lifetimeAtFull = Mathf.Max(0f, r.At("lifetimeAtFull", fromPlus, plus)),
            };
        }

        // The fire-rate multiplier at ramp position f (1 when the reward is
        // off) - what the volleys, the beam gap and the ammo cost follow.
        public float RateAt(float f)
        {
            return rate ? EtMath.Lerp1(fireRateAtFull, f) : 1f;
        }

        // Publish every reward at ramp position f (0..1). Returns true when
        // the projectile count is among them - the caller's cue to rebuild a
        // beam's visuals when the ROUNDED count moves (HitscanWeapon indexes
        // its visuals with no guard).
        public bool Publish(WeaponBase w, string key, float f)
        {
            if (w == null)
                return false;

            if (rate)
            {
                One(w, key, EtWeaponStats.FireRate, fireRateAtFull, f);

                // DECIDED 3.7: a gun that runs on AMMO spends the same ammo a
                // second, or a faster gun only empties sooner (the AI empties
                // a tank, then waits for it). A key of its own, so `cheaper`
                // can stack on it.
                if (w.Cost > 0f)
                    Raw(w, key + ".ammo", EtWeaponStats.Cost, 1f / RateAt(f));
            }

            if (accuracy)
            {
                One(w, key, EtWeaponStats.AngleVariance, Mathf.Max(0f, 1f - accuracyAtFull), f);
                One(w, key, EtWeaponStats.Spread, spreadAtFull, f);
            }

            if (damage) One(w, key, EtWeaponStats.Damage, damageAtFull, f);
            if (pellets) One(w, key, EtWeaponStats.Pellets, pelletsAtFull, f);
            if (cheaper) One(w, key, EtWeaponStats.Cost, costAtFull, f);
            if (speed) One(w, key, EtWeaponStats.Speed, speedAtFull, f);
            if (range) One(w, key, EtWeaponStats.Range, rangeAtFull, f);
            if (size) One(w, key, EtWeaponStats.Size, sizeAtFull, f);

            if (burst)
            {
                One(w, key, EtWeaponStats.BurstSize, burstAtFull, f);
                One(w, key, EtWeaponStats.BurstDelay, burstDelayAtFull, f);
            }

            if (burn) One(w, key, EtWeaponStats.Burn, burnAtFull, f);

            if (explosion)
            {
                One(w, key, EtWeaponStats.ExplosionRadius, explosionAtFull, f);
                One(w, key, EtWeaponStats.ExplosionDamage, explosionAtFull, f);
            }

            if (push)
            {
                One(w, key, EtWeaponStats.Push, pushAtFull, f);
                One(w, key, EtWeaponStats.Knockback, knockbackAtFull, f);
            }

            if (warmup) One(w, key, EtWeaponStats.Warmup, warmupAtFull, f);
            if (lifetime) One(w, key, EtWeaponStats.Lifetime, lifetimeAtFull, f);

            return pellets;
        }

        // Everything this set published on `w`, withdrawn.
        public static void Withdraw(WeaponBase w, string key)
        {
            if (w == null)
                return;

            EtWeaponStats.Clear(w, key);
            EtWeaponStats.Clear(w, key + ".ammo");
        }

        // A multiplier of 1 WITHDRAWS rather than pinning a captured base for
        // nothing (Module Forge's rule).
        private static void One(WeaponBase w, string key, int stat, float atFull, float f)
        {
            Raw(w, key, stat, EtMath.Lerp1(atFull, f));
        }

        private static void Raw(WeaponBase w, string key, int stat, float m)
        {
            if (Mathf.Abs(m - 1f) > 0.0001f)
                EtWeaponStats.Set(w, stat, key, m);
            else
                EtWeaponStats.Clear(w, stat, key);
        }

        public bool PaysAnything
        {
            get
            {
                return rate || accuracy || damage || pellets || cheaper || speed || range || size ||
                       burst || burn || explosion || push || warmup || lifetime;
            }
        }
    }
}
