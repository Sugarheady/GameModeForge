using System;

namespace GameModeForge.SalvageShop
{
    // THE ADD-ONS SALVAGE PUTS ON A GUN - its own explosion and spark (14.3),
    // and the cheaper copies of the game's own add-ons that REDUCE COST swaps
    // in (18.2). Each one IS the game's own add-on class, so the game applies
    // it to every shot itself (ProjectileWeapon.FireSingle walks
    // WeaponBase.augmentations), the card's explosion and discharge lines count
    // it (WeaponBase.GetExplosionDamageWithAllAugmentations looks for
    // AddExplosionEffect), and nothing about how a shot is fired changes.
    //
    // ★ THE ONE THING EACH CHANGES IS HOW THE COST IS PAID, and that is a crash
    // fix rather than a style choice. FireSingle's check is
    //     GetResource(CostResource) >= CostPerProjectile
    // followed by GetTank(CostResource).Value -= CostPerProjectile. A ship with
    // NO tank of that resource answers 0 for GetResource, so a cost of 0 passes
    // the check - and GetTank then hands back null, inside the game's own fire
    // code, on every shot. A salvage add-on costs 0 by default (16.4), and
    // reduce cost can take a stock add-on to 0 (ten steps). Either would crash
    // a ship that does not carry that resource.
    //
    // So each of these tells the game "no cost, on HEALTH" (every ship has a
    // health tank, so the game's own check and subtraction are harmless), and
    // pays its REAL cost itself in ModifyProjectile, with the tank checked
    // first. An add-on the ship cannot pay for is skipped for that shot -
    // exactly what the game's own add-on does when its tank runs dry.
    //
    // These live only on a BUILT gun (WeaponBase.augmentations). They are never
    // on a module and never saved, so a save never names a type from this DLL.
    internal static class SalvageAddOns
    {
        // Resource Health, resolved through the game's registry. Null means the
        // add-ons cannot be made safe, so none are added (and it is said).
        public static global::Resource Safe
        {
            get { return SalvageCatalog.ResourceFor("health"); }
        }

        // Pay `cost` of `res` from the shot's owner, or say it cannot be paid.
        public static bool Pay(IProjectile shot, float cost, global::Resource res)
        {
            if (cost <= 0f)
                return true;

            try
            {
                Unit owner = shot != null ? shot.Owner : null;

                if (owner == null || res == null || !owner.HasTank(res) || owner.GetResource(res) < cost)
                    return false;

                owner.GetTank(res).Value -= cost;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    // An explosion: salvage's own (14.3), or a stock AddExplosion with its cost
    // cut (18.2).
    internal sealed class SalvageExplosion : AddExplosionEffect, IProjectileModifier, IHasPerProjectileCost
    {
        public float realCost;
        public global::Resource realResource;

        // 18.1: a plain shot that reaches its range explodes too - salvage's
        // own explosion only, and only with the setting on.
        public bool atRange;

        float IHasPerProjectileCost.CostPerProjectile
        {
            get { return 0f; }
        }

        global::Resource IHasPerProjectileCost.CostResource
        {
            get { return SalvageAddOns.Safe; }
        }

        void IProjectileModifier.ModifyProjectile(IProjectile projectile)
        {
            if (!SalvageAddOns.Pay(projectile, realCost, realResource))
                return;

            base.ModifyProjectile(projectile);

            if (atRange)
            {
                ProjectileRangeData rd = projectile.RangeData;

                if (rd.enabled && rd.destroyWhenReached)
                {
                    rd.spawnExplosion = true;
                    projectile.RangeData = rd;
                }
            }
        }

        public override ModuleEffect Clone()
        {
            return new SalvageExplosion
            {
                damageType = damageType,
                damageAmount = damageAmount,
                costPerProjectile = costPerProjectile,
                costResource = costResource,
                addImpactExplosion = addImpactExplosion,
                addTimeoutExplosion = addTimeoutExplosion,
                explosionRadiusIncrement = explosionRadiusIncrement,
                burn = burn,
                realCost = realCost,
                realResource = realResource,
                atRange = atRange,
                Module = Module
            };
        }

        // A stock add-on's copy, with its cost cut. ★ The copy is given the
        // ORIGINAL's module: the game's own Clone() drops `Module`, and every
        // add-on's damage reads `Module.Level` - a copy without it throws on
        // its first shot (18.2).
        public static SalvageExplosion CopyOf(AddExplosionEffect a, float cost)
        {
            return new SalvageExplosion
            {
                damageType = a.damageType,
                damageAmount = a.damageAmount,
                costPerProjectile = a.costPerProjectile,
                costResource = a.costResource,
                addImpactExplosion = a.addImpactExplosion,
                addTimeoutExplosion = a.addTimeoutExplosion,
                explosionRadiusIncrement = a.explosionRadiusIncrement,
                burn = a.burn,
                realCost = cost,
                realResource = a.costResource,
                Module = a.Module
            };
        }
    }

    // A spark (a discharge): salvage's own, or a stock AddSpark with its cost
    // cut. The stock cost is an int, so the cut is rounded (18.2).
    internal sealed class SalvageSpark : AddDischargeEffect, IProjectileModifier, IHasPerProjectileCost
    {
        public int realCost;
        public global::Resource realResource;

        float IHasPerProjectileCost.CostPerProjectile
        {
            get { return 0f; }
        }

        global::Resource IHasPerProjectileCost.CostResource
        {
            get { return SalvageAddOns.Safe; }
        }

        void IProjectileModifier.ModifyProjectile(IProjectile projectile)
        {
            if (!SalvageAddOns.Pay(projectile, realCost, realResource))
                return;

            base.ModifyProjectile(projectile);
        }

        public override ModuleEffect Clone()
        {
            return new SalvageSpark
            {
                chainLengthIncrement = chainLengthIncrement,
                damageIncrement = damageIncrement,
                impact = impact,
                timeout = timeout,
                costPerProjectile = costPerProjectile,
                costResource = costResource,
                realCost = realCost,
                realResource = realResource,
                Module = Module
            };
        }

        public static SalvageSpark CopyOf(AddDischargeEffect a, int cost)
        {
            return new SalvageSpark
            {
                chainLengthIncrement = a.chainLengthIncrement,
                damageIncrement = a.damageIncrement,
                impact = a.impact,
                timeout = a.timeout,
                costPerProjectile = a.costPerProjectile,
                costResource = a.costResource,
                realCost = cost,
                realResource = a.costResource,
                Module = a.Module
            };
        }
    }

    // A stock AddBurn with its cost cut (reduce cost covers every add-on that
    // charges per shot, 18.2).
    internal sealed class SalvageBurn : AddBurnEffect, IProjectileModifier, IHasPerProjectileCost
    {
        public float realCost;
        public global::Resource realResource;

        float IHasPerProjectileCost.CostPerProjectile
        {
            get { return 0f; }
        }

        global::Resource IHasPerProjectileCost.CostResource
        {
            get { return SalvageAddOns.Safe; }
        }

        void IProjectileModifier.ModifyProjectile(IProjectile projectile)
        {
            if (!SalvageAddOns.Pay(projectile, realCost, realResource))
                return;

            base.ModifyProjectile(projectile);
        }

        public override ModuleEffect Clone()
        {
            return new SalvageBurn
            {
                amount = amount,
                costPerProjectile = costPerProjectile,
                costResource = costResource,
                realCost = realCost,
                realResource = realResource,
                Module = Module
            };
        }

        public static SalvageBurn CopyOf(AddBurnEffect a, float cost)
        {
            return new SalvageBurn
            {
                amount = a.amount,
                costPerProjectile = a.costPerProjectile,
                costResource = a.costResource,
                realCost = cost,
                realResource = a.costResource,
                Module = a.Module
            };
        }
    }
}
