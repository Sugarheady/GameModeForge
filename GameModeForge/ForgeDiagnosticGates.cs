using System;
using System.Collections.Generic;
using System.Reflection;

namespace GameModeForge
{
    // A deliberate copy of ForgeDiagnosticGates / ModuleForgeDiagnosticGates,
    // and the duplicate-by-design rule is why it is a copy rather than a
    // borrow: resetting your own diagnostics is not a decoder worth a
    // reflection bridge, and this mod has to work with the other two absent.
    //
    // WHAT IT IS FOR. A once-only log gate is per SESSION, but almost
    // everything it describes is per RUN. Weapon Forge had 26 such gates
    // across 19 files and THREE had a working reset - so the second run of a
    // session reported nothing, and the run where you go looking for a
    // warning is exactly the run that cannot produce it.
    //
    // It sweeps BY NAMING CONVENTION rather than from a list, because the
    // hand-written version is what drifted, and a list nobody rereads is not
    // a mechanism. Any static field named `_said*`, `_warned*` or `_told*` in
    // this assembly is cleared.
    //
    // ★ TWO THINGS CARRIED ACROSS FROM THE TWIN RATHER THAN REDISCOVERED:
    //
    // 1. `HashSet<T>` does NOT implement the non-generic
    //    System.Collections.ICollection. Weapon Forge's first cut filtered on
    //    exactly that and silently dropped every keyed gate while reporting a
    //    plausible total - 20 of 26. So test the CAPABILITY about to be used,
    //    a public parameterless Clear(), which is also the method the reset
    //    then calls; acceptance and action cannot disagree.
    //
    // 2. The log line prints the SPLIT, not one total, because a count is
    //    only evidence when it can be checked against a count derived another
    //    way. One number looks like confirmation and confirms nothing.
    //
    // It deliberately over-resets: a few gates describe a session-invariant
    // reflection failure, and one repeated line beats losing a diagnostic at
    // the moment it is wanted.
    public static class ForgeDiagnosticGates
    {
        private static readonly string[] Prefixes = { "_said", "_warned", "_told" };

        public static void Reset()
        {
            int bools = 0;
            int keyed = 0;

            try
            {
                foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
                {
                    FieldInfo[] fields = type.GetFields(
                        BindingFlags.Static | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                    foreach (FieldInfo field in fields)
                    {
                        if (!IsGateName(field.Name))
                            continue;

                        if (field.FieldType == typeof(bool))
                        {
                            if (field.IsInitOnly)
                                continue;

                            field.SetValue(null, false);
                            bools++;
                            continue;
                        }

                        if (TryClear(field))
                            keyed++;
                    }
                }
            }
            catch (Exception)
            {
                // A diagnostic sweep must never be the thing that breaks a
                // run. Weapon Forge lost five test rounds to a warning-only
                // read that threw.
                return;
            }

            if (bools + keyed == 0)
                return;

            GameModeForgePlugin.Log.LogInfo(
                "diagnostic gates reset: " + bools + " one-shot, " +
                keyed + " keyed");
        }

        private static bool IsGateName(string name)
        {
            for (int i = 0; i < Prefixes.Length; i++)
                if (name.StartsWith(Prefixes[i], StringComparison.Ordinal))
                    return true;

            return false;
        }

        // Ask for the thing we are about to do, not for an interface that
        // implies it. See note 1 in the header - this is the whole fix.
        private static bool TryClear(FieldInfo field)
        {
            object value = field.GetValue(null);

            if (value == null)
                return false;

            MethodInfo clear = value.GetType().GetMethod(
                "Clear", BindingFlags.Public | BindingFlags.Instance,
                null, Type.EmptyTypes, null);

            if (clear == null)
                return false;

            clear.Invoke(value, null);
            return true;
        }
    }
}
