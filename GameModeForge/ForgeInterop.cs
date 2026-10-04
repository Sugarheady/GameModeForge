using System;

namespace GameModeForge
{
    // The one-way bridge to the other Forge mods, and to anyone else's
    // plugin.
    //
    // ★ THE RULES, inherited from the other two repos and not negotiable:
    //
    //   - TYPE NAME ONLY. Never an assembly reference, never a
    //     [BepInDependency], never a plugin GUID. Each mod must build and run
    //     with the others absent.
    //   - FIND TYPES QUIETLY. AccessTools.TypeByName LOGS A WARNING when the
    //     type is absent, and "the other mod is not installed" is a
    //     completely normal state that must be silent. Scan the loaded
    //     assemblies instead.
    //   - LATCH ON SUCCESS ONLY. Plugin load order is not guaranteed, so "the
    //     type is not there" has a different answer depending on when it is
    //     asked. Caching the first failure forever is exactly the trap.
    //     A failure is retried once per run, via ResetLookups below.
    //   - IF THE REFLECTION FAILS, DO NOTHING. Never risk two mods fighting
    //     over the same patch.
    //
    // ★ AND THE ONE THAT IS NEW HERE, because this mod is the first with a
    //   reason to break it: exactly ONE OWNER PER SHARED MECHANIC is harder
    //   for a game mode than for the existing pair. A mode that changes how
    //   weapons behave has to do so WITHOUT KNOWING whether Weapon Forge is
    //   installed. Until the concept settles, the safe reading is that this
    //   mod reads the others and never writes into them - one-way, as the
    //   class name says.
    public static class ForgeInterop
    {
        private static Type _weaponForge;
        private static Type _moduleForge;
        private static bool _weaponForgeFound;
        private static bool _moduleForgeFound;

        // True when Weapon Forge is loaded. Probe types chosen because they
        // are long-lived owners rather than feature classes - a feature can
        // be renamed, and a probe that goes stale reads as "the mod is not
        // installed", which is the wrong answer arriving silently.
        public static bool HasWeaponForge
        {
            get { return WeaponForgeType != null; }
        }

        public static bool HasModuleForge
        {
            get { return ModuleForgeType != null; }
        }

        private static Type WeaponForgeType
        {
            get
            {
                if (_weaponForgeFound)
                    return _weaponForge;

                _weaponForge = FindTypeQuietly("WeaponForge.ForgeWeaponStats");

                if (_weaponForge != null)
                    _weaponForgeFound = true;   // latch on SUCCESS only

                return _weaponForge;
            }
        }

        private static Type ModuleForgeType
        {
            get
            {
                if (_moduleForgeFound)
                    return _moduleForge;

                _moduleForge = FindTypeQuietly("ModuleForge.ModuleForgeRegistry");

                if (_moduleForge != null)
                    _moduleForgeFound = true;

                return _moduleForge;
            }
        }

        // Only a FAILED lookup is forgotten, so the question is asked again
        // next run rather than answered once and forever from whenever it
        // first happened to be asked. A SUCCESSFUL one is kept: a type that
        // was there does not leave.
        public static void ResetLookups()
        {
            if (!_weaponForgeFound)
                _weaponForge = null;

            if (!_moduleForgeFound)
                _moduleForge = null;
        }

        // Found by scanning loaded assemblies rather than
        // AccessTools.TypeByName, which logs a warning when the type is
        // absent. See the rules in the header.
        public static Type FindTypeQuietly(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;

                try
                {
                    t = asm.GetType(fullName, false);
                }
                catch (Exception)
                {
                    continue;
                }

                if (t != null)
                    return t;
            }

            return null;
        }
    }
}
