using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GameModeForge.SalvageShop
{
    // What a module IS to the salvage shop, which stats it is offered, and how
    // each stat reads on screen (CONCEPT 2.3, 3.4, 12.1, 13.2, 14.2).
    internal enum SalvageKind
    {
        None,          // not upgradeable (BoosterCore, the ship itself, flat augmentations)
        Weapon,        // a weapon module: a stat a step
        WeaponGadget,  // a weapon gadget: a stat a step; fire rate is its cooldown
        SpawnerGadget, // Generator Fuel, Teardrop: they fire a UNIT - cooldown, count and cost only
        DroneGadget,   // its level IS its drone cap: +1 level = +1 drone
        ShipUpgrade,   // Passive Add / Regen / Shield: any of the 14 resource stats (shields: + level)
        LevelOnly      // the PowerCore and the scaling augmentations: +1 level
    }

    internal static class SalvageCatalog
    {
        // ---- the seven resources, by the names the game shows (13.4) ------

        // "gel" is Resource Purple and "stamina" Resource White - the screen
        // names and the asset names differ, and the screen name is the one
        // anybody types.
        public static string ResourceId(string key)
        {
            switch ((key ?? "").Trim().ToLowerInvariant())
            {
                case "health": return "Resource Health";
                case "fuel": return "Resource Fuel";
                case "gel":
                case "purple": return "Resource Purple";
                case "caps": return "Resource Caps";
                case "stamina":
                case "white": return "Resource White";
                case "tech": return "Resource Tech";
                case "electron": return "Resource Electron";
            }

            return null;
        }

        public static string ResourceName(string key)
        {
            return (key ?? "").ToUpperInvariant();
        }

        private static readonly Dictionary<string, Resource> _resources =
            new Dictionary<string, Resource>();

        // The game's own Resource asset, through its registry (the save uses
        // the same one), with a scan of the loaded assets as the fallback.
        // Latched on success only.
        public static Resource ResourceFor(string key)
        {
            string id = ResourceId(key);

            if (id == null)
                return null;

            Resource r;

            if (_resources.TryGetValue(id, out r) && r != null)
                return r;

            r = null;

            try
            {
                IRegistry<Resource, string> reg;

                if (ServiceLocator.TryGet<IRegistry<Resource, string>>(out reg) && reg != null)
                    r = reg.Get(id);
            }
            catch (Exception)
            {
            }

            if (r == null)
            {
                foreach (Resource res in UnityEngine.Resources.FindObjectsOfTypeAll<Resource>())
                {
                    if (res != null && (res.Id == id || res.name == id))
                    {
                        r = res;
                        break;
                    }
                }
            }

            if (r != null)
                _resources[id] = r;

            return r;
        }

        // ---- the kinds --------------------------------------------------------

        public static WeaponData WeaponOf(Module m)
        {
            var wm = m as WeaponModule;

            if (wm != null)
                return wm.WeaponData;

            var wb = m as WeaponBasedActiveModule;
            return wb != null ? wb.WeaponData : null;
        }

        private static string TypeName(Module m)
        {
            try
            {
                return m != null && m.ModuleType != null ? m.ModuleType.name ?? "" : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string TypeLabel(Module m)
        {
            try
            {
                return m != null && m.ModuleType != null ? m.ModuleType.displayName ?? "" : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        // By the module's ModuleType asset (its name, or the word the card
        // prints at the top), never by the module's own name.
        private static bool IsType(Module m, string asset, string label)
        {
            string n = TypeName(m);
            return n == "ModuleType " + asset ||
                   string.Equals(TypeLabel(m), label, StringComparison.OrdinalIgnoreCase);
        }

        public static SalvageKind KindOf(Module m)
        {
            if (m == null || m.Data == null)
                return SalvageKind.None;

            if (m is WeaponModule)
                return WeaponOf(m) != null ? SalvageKind.Weapon : SalvageKind.None;

            if (m is WeaponBasedActiveModule)
            {
                WeaponData wd = WeaponOf(m);

                if (wd == null)
                    return SalvageKind.None;

                return wd is MinionSpawnerWeaponData ? SalvageKind.SpawnerGadget : SalvageKind.WeaponGadget;
            }

            if (m is SpawnMinionModule)
                return SalvageKind.DroneGadget;

            if (IsType(m, "Passive", "UPGRADES"))
                return SalvageKind.ShipUpgrade;

            if ((IsType(m, "PowerCore", "POWER") || IsType(m, "WeaponAugmentation", "WEAPON MODS")) &&
                m.Data.canBeBoosted && Scales(m))
            {
                return SalvageKind.LevelOnly;
            }

            return SalvageKind.None;
        }

        public static bool IsShield(Module m)
        {
            if (m == null || m.Effects == null)
                return false;

            foreach (ModuleEffect e in m.Effects)
                if (e is AddShieldEffect)
                    return true;

            return false;
        }

        // 3.2: weapons and gadgets, from the vault only.
        public static bool Scrappable(Module m)
        {
            switch (KindOf(m))
            {
                case SalvageKind.Weapon:
                case SalvageKind.WeaponGadget:
                case SalvageKind.SpawnerGadget:
                case SalvageKind.DroneGadget:
                    return true;
            }

            return false;
        }

        // ★ A LEVEL ONLY MOVES A STAT THAT SCALES. Every effect reads its
        // strength as `series.GetElement(Module.Level - 1)`, and a series with
        // no change is the same at every level - so a level step on a flat
        // module (Burst, ExtraProjectile) would be scrap spent on nothing.
        // Read off the module's own effect instances, cached per asset.
        private static readonly Dictionary<ModuleData, bool> _scales = new Dictionary<ModuleData, bool>();

        public static bool Scales(Module m)
        {
            if (m == null || m.Data == null)
                return false;

            bool known;

            if (_scales.TryGetValue(m.Data, out known))
                return known;

            bool any = false;

            try
            {
                if (m.Effects != null)
                {
                    foreach (ModuleEffect e in m.Effects)
                    {
                        if (e != null && EffectScales(e))
                        {
                            any = true;
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
                any = false;
            }

            _scales[m.Data] = any;
            return any;
        }

        private static bool EffectScales(ModuleEffect e)
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (Type t = e.GetType(); t != null && t != typeof(ModuleEffect); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(F))
                {
                    if (f.FieldType != typeof(FloatSeries))
                        continue;

                    var s = (FloatSeries)f.GetValue(e);

                    if (SalvageMath.Scales((int)s.increaseMethod, s.baseValue, s.change))
                        return true;
                }
            }

            return false;
        }

        // ---- what a module is offered (19: "Only stats that mean something on
        // that gun are offered") -------------------------------------------------

        public static List<string> Offers(Module m, SalvageRules r)
        {
            var list = new List<string>();

            switch (KindOf(m))
            {
                case SalvageKind.Weapon:
                case SalvageKind.WeaponGadget:
                case SalvageKind.SpawnerGadget:
                    AddWeaponOffers(m, r, list);
                    break;

                case SalvageKind.DroneGadget:
                case SalvageKind.LevelOnly:
                    Offer(list, r, "level");
                    break;

                case SalvageKind.ShipUpgrade:
                    if (IsShield(m))
                        Offer(list, r, "level");

                    foreach (string res in SalvageRules.Resources)
                        Offer(list, r, "cap." + res);

                    foreach (string res in SalvageRules.Resources)
                        Offer(list, r, "regen." + res);
                    break;
            }

            return list;
        }

        private static void Offer(List<string> list, SalvageRules r, string id)
        {
            if (r.Enabled(id))
                list.Add(id);
        }

        private static void AddWeaponOffers(Module m, SalvageRules r, List<string> list)
        {
            WeaponData t = WeaponOf(m);

            if (t == null)
                return;

            bool gadget = m is ActiveModule;
            bool spawner = t is MinionSpawnerWeaponData;
            var pd = t as ProjectileWeaponData;
            var hd = t as HitscanWeaponData;
            var phd = t as PhysicsWeaponData;

            // damage is the HIT only (13.2), so a gun with no hit gets the
            // explosion stat instead.
            if (!spawner && t.damage.amount > 0f)
                Offer(list, r, "damage");

            // ★ A beam has no explosion or spark code at all (HitscanWeapon),
            // and a spawner fires a unit, not a shot (14.3). Only a projectile
            // gun applies the game's add-ons (ProjectileWeapon.FireSingle).
            if (pd != null)
            {
                Offer(list, r, "explosion");
                Offer(list, r, "spark");
            }

            if (gadget ? CooldownOf(m) > 0f : t.fireRate > 0f)
                Offer(list, r, "fireRate");

            if ((pd != null && RangeOrLifetime(pd) > 0f) || (hd != null && hd.range > 0f))
                Offer(list, r, "range");

            if ((pd != null && pd.projectileSpeed > 0f) || (phd != null && phd.projectileSpeed > 0f))
                Offer(list, r, "speed");

            // ★ A 360-degree ring is spaced by `spread / count`; cutting it
            // turns an even ring into a fan, which is not "tighter".
            if (!spawner && t.spread > 0f && t.spread < 360f)
                Offer(list, r, "spread");

            Offer(list, r, "projectiles");

            if (gadget ? ActivationCostOf(m) > 0f : t.cost > 0f)
                Offer(list, r, "cost");
        }

        // ★ On a lobbed or rocket shot `rangeData` is inert - the game never
        // enforces it - so "range" means its lifetime there (12.2, 13.2).
        public static bool Timed(ProjectileWeaponData pd)
        {
            if (pd == null)
                return false;

            if (pd.usePhysics)
                return true;

            return !(pd.rangeData.enabled && pd.rangeData.range > 0f) && pd.lifetimeData.enabled;
        }

        public static float RangeOrLifetime(ProjectileWeaponData pd)
        {
            if (pd == null)
                return 0f;

            if (Timed(pd))
                return pd.lifetimeData.enabled ? pd.lifetimeData.time : 0f;

            return pd.rangeData.range;
        }

        public static float CooldownOf(Module m)
        {
            var d = m != null ? m.Data as ActiveModuleData : null;

            try
            {
                float c = d != null ? d.Cooldown : 0f;
                return float.IsNaN(c) || float.IsInfinity(c) ? 0f : c;
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        public static float ActivationCostOf(Module m)
        {
            var d = m != null ? m.Data as ActiveModuleData : null;

            try
            {
                return d != null ? d.ActivationCost : 0f;
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        // ---- words ---------------------------------------------------------

        public static string StatName(string id, Module m)
        {
            switch (id)
            {
                case "damage": return "DAMAGE";
                case "explosion": return "EXPLOSION";
                case "spark": return "SPARK";
                case "fireRate": return m is ActiveModule ? "COOLDOWN" : "FIRE RATE";
                case "range":
                    return Timed(WeaponOf(m) as ProjectileWeaponData) ? "LIFETIME" : "RANGE";
                case "speed": return "PROJECTILE SPEED";
                case "spread": return "SPREAD";
                case "projectiles": return "PROJECTILES";
                case "cost": return m is ActiveModule ? "ACTIVATION COST" : "REDUCE COST";
                case "level": return KindOf(m) == SalvageKind.DroneGadget ? "LEVEL (+1 DRONE)" : "LEVEL";
            }

            if (id != null && id.StartsWith("cap.", StringComparison.Ordinal))
                return ResourceName(id.Substring(4)) + " CAPACITY";

            if (id != null && id.StartsWith("regen.", StringComparison.Ordinal))
                return ResourceName(id.Substring(6)) + " REGEN";

            return (id ?? "?").ToUpperInvariant();
        }

        // What `steps` steps of a stat give, as the card and the picker say it:
        // "+20%", "+2 DMG +1 RADIUS", "-20%", "+0.1/S".
        public static string Gives(string id, int steps, SalvageRules r, Module m)
        {
            SalvageStatRule s = r.Stat(id);
            float a = s != null ? s.amount * steps : 0f;
            float b = s != null ? s.amount2 * steps : 0f;

            switch (id)
            {
                case "damage":
                case "range":
                case "speed":
                    return "+" + SalvageMath.Num(a) + "%";
                case "fireRate":
                    return m is ActiveModule ? "-" + CooldownCutPercent(s, steps) + "%" : "+" + SalvageMath.Num(a) + "%";
                case "spread":
                    return "-" + SalvageMath.Num(Math.Min(100f, a)) + "%";
                case "cost":
                    return "-" + SalvageMath.Num(Math.Min(100f, a)) + "%";
                case "explosion":
                    return "+" + SalvageMath.Num(a) + " DMG +" + SalvageMath.Num(b) + " RADIUS";
                case "spark":
                    return "+" + SalvageMath.Num(a) + " DMG +" + SalvageMath.Num(b) + " CHAIN";
                case "projectiles":
                case "level":
                    return "+" + SalvageMath.Num(a);
            }

            if (id != null && id.StartsWith("regen.", StringComparison.Ordinal))
                return "+" + SalvageMath.Num(a) + "/S";

            return "+" + SalvageMath.Num(a);
        }

        // A gadget's rate +10% a step is a cooldown cut of 1 - 1/(1 + 0.1n).
        private static string CooldownCutPercent(SalvageStatRule s, int steps)
        {
            float pct = s != null ? s.amount : 0f;
            float after = SalvageMath.CooldownAfter(1f, pct, steps);
            return SalvageMath.Num((float)Math.Round((1f - after) * 100f, 1));
        }
    }
}
