using System;
using System.Collections.Generic;
using System.Reflection;

namespace GameModeForge
{
    // A sibling mod's SETTINGS ROWS, drawn in this tab under that mod's own
    // on/off row. New Game Plus is the first (its carry switches and its two
    // headline sliders, CONCEPT.md 4); Enemy Traits will be the next.
    //
    // ★ THIS MOD DRAWS THE ROWS AND KNOWS NOTHING ABOUT THEM. The sibling
    // owns every number and every file (section 1: "each DLL owns its own
    // numbers") and exposes one public static class, found by TYPE NAME:
    //
    //   string[] Rows()                    "id|toggle|caption|offText|onText"
    //                                      "id|slider|caption|steps|maxShown"
    //   int      Get(string id)            toggle 0 / 1; slider a step
    //   bool     Set(string id, int value) false = not saved (the row goes back)
    //   string   Label(string id, int v)   the caption for that value
    //
    // No reference, no shared type - strings and ints only, so either side can
    // be rebuilt alone. With the sibling absent there are simply no rows.
    //
    // ★ EVERY CALL IS WRAPPED. A sibling that throws must never break the
    // options screen; a failed Set reads as "not saved", which puts the row
    // back where the file is - the rule the mod switches already follow.
    public sealed class ForgeTabRows
    {
        public sealed class Row
        {
            public string id;
            public bool slider;
            public string caption;
            public string offText = "Off";
            public string onText = "On";
            public int steps;
            public int maxShown;
        }

        private readonly string _owner;
        private readonly MethodInfo _rows;
        private readonly MethodInfo _get;
        private readonly MethodInfo _set;
        private readonly MethodInfo _label;

        private readonly HashSet<string> _warned = new HashSet<string>();

        private ForgeTabRows(string owner, MethodInfo rows, MethodInfo get,
                             MethodInfo set, MethodInfo label)
        {
            _owner = owner;
            _rows = rows;
            _get = get;
            _set = set;
            _label = label;
        }

        // The sibling's rows class, or null when it is not installed or does
        // not speak the contract. Quietly: an absent mod is a normal state.
        public static ForgeTabRows Find(string typeName, string owner)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            Type t = ForgeInterop.FindTypeQuietly(typeName);

            if (t == null)
                return null;

            const BindingFlags S = BindingFlags.Public | BindingFlags.Static;

            MethodInfo rows = t.GetMethod("Rows", S, null, Type.EmptyTypes, null);
            MethodInfo get = t.GetMethod("Get", S, null, new[] { typeof(string) }, null);
            MethodInfo set = t.GetMethod("Set", S, null, new[] { typeof(string), typeof(int) }, null);
            MethodInfo label = t.GetMethod("Label", S, null, new[] { typeof(string), typeof(int) }, null);

            if (rows == null || rows.ReturnType != typeof(string[]) ||
                get == null || get.ReturnType != typeof(int) ||
                set == null || set.ReturnType != typeof(bool))
            {
                GameModeForgePlugin.Log.LogWarning(
                    owner + " is installed but its settings rows (" + typeName + ") do not " +
                    "match what this build of Game Mode Forge reads - they are not shown. Its " +
                    "settings still work from its own files.");
                return null;
            }

            return new ForgeTabRows(owner, rows, get, set, label);
        }

        public List<Row> Rows()
        {
            var list = new List<Row>();
            string[] raw;

            try
            {
                raw = (string[])_rows.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Warn("rows", "could not read " + _owner + "'s settings rows (" + Inner(ex) +
                             ") - none are shown.");
                return list;
            }

            if (raw == null)
                return list;

            foreach (string line in raw)
            {
                string[] bits = (line ?? "").Split('|');

                if (bits.Length < 3 || bits[0].Length == 0)
                    continue;

                var row = new Row { id = bits[0], caption = bits[2] };

                if (bits[1] == "slider")
                {
                    int steps, max;

                    if (bits.Length < 5 || !int.TryParse(bits[3], out steps) ||
                        !int.TryParse(bits[4], out max) || steps < 1)
                    {
                        continue;
                    }

                    row.slider = true;
                    row.steps = steps;
                    row.maxShown = max;
                }
                else if (bits[1] == "toggle")
                {
                    if (bits.Length > 3 && bits[3].Length > 0) row.offText = bits[3];
                    if (bits.Length > 4 && bits[4].Length > 0) row.onText = bits[4];
                }
                else
                {
                    continue;
                }

                list.Add(row);
            }

            return list;
        }

        public int Get(string id)
        {
            try
            {
                return (int)_get.Invoke(null, new object[] { id });
            }
            catch (Exception ex)
            {
                Warn("get:" + id, "could not read " + _owner + "'s '" + id + "' (" + Inner(ex) + ").");
                return 0;
            }
        }

        public bool Set(string id, int value)
        {
            try
            {
                return (bool)_set.Invoke(null, new object[] { id, value });
            }
            catch (Exception ex)
            {
                GameModeForgePlugin.Log.LogError(
                    "COULD NOT SAVE " + _owner + "'s '" + id + "' (" + Inner(ex) + ") - the row " +
                    "is put back where the file is.");
                return false;
            }
        }

        public string Label(string id, int value, string fallback)
        {
            if (_label == null)
                return fallback;

            try
            {
                return (_label.Invoke(null, new object[] { id, value }) as string) ?? fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private void Warn(string key, string text)
        {
            if (_warned.Add(key))
                GameModeForgePlugin.Log.LogWarning(text);
        }

        private static string Inner(Exception ex)
        {
            Exception e = ex is TargetInvocationException && ex.InnerException != null
                ? ex.InnerException : ex;

            return e.GetType().Name + ": " + e.Message;
        }
    }
}
