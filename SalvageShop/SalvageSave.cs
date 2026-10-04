using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace GameModeForge.SalvageShop
{
    // THE SAVE'S SALVAGE RECORD - `salvage.txt` in the save folder (2.4, 3.8).
    //
    // ★ THE SAVE KEEPS NONE OF IT. Module.Memento holds the data id, the four
    // connections, the power core, the level field and the power level - no
    // Level, no BaseLevel - and RestoreFromMemento rebuilds the effect list
    // from the asset. So every upgrade would vanish on a Continue with nothing
    // in the log. This record puts each one back.
    //
    // ★ KEYED BY WHERE EACH ITEM IS, which the save itself restores exactly
    // (Weapon Forge's rank record is the precedent):
    //
    //   grid  <ship id>  x,y   on that ship's grid, at that cell (the grid
    //                          memento stores modules by position)
    //   vault 0          i     the i-th module of the vault (its list order is
    //                          what the vault memento restores)
    //
    // and checked against the module's data id, so a record never lands on a
    // different item. A module lying on the ground is nowhere a save keeps a
    // module, so its upgrades end there (said in the log).
    //
    // ★ THE FILE LIVES IN THE SAVE FOLDER, so it is slot-correct for free and is
    // deleted with the save. A save with no upgrades DELETES it, or yesterday's
    // file would hand the upgrades back tonight.
    //
    // ★ WITH THE MOD OFF THE SAVE STILL LOADS: Ex and Bond are stock
    // ingredients, and this file is simply not read (3.8). Switched back on,
    // the upgrades return.
    internal static class SalvageSave
    {
        private const string FileName = "salvage.txt";
        private const string Version = "salvage 1";

        // ---- where every module is -----------------------------------------

        internal struct Place
        {
            public int shipId;     // 0 for the vault
            public string where;   // "grid:x,y" or "vault:i"
        }

        internal static List<KeyValuePair<Module, Place>> Locate()
        {
            var list = new List<KeyValuePair<Module, Place>>();
            var seen = new HashSet<Module>();
            EntityManager em;

            if (ServiceLocator.TryGet<EntityManager>(out em) && em != null)
            {
                try
                {
                    foreach (EntityData ship in em.GetShips())
                    {
                        ModuleGridOwner.Data data;

                        if (ship == null || !ship.TryGetComponent<ModuleGridOwner.Data>(out data) ||
                            data == null)
                        {
                            continue;
                        }

                        var grid = data.ModuleGrid as ModuleGrid;

                        if (grid == null)
                            continue;

                        foreach (KeyValuePair<Vector2Int, Module> kv in grid.Modules)
                        {
                            if (kv.Value == null || !seen.Add(kv.Value))
                                continue;

                            list.Add(new KeyValuePair<Module, Place>(kv.Value, new Place
                            {
                                shipId = ship.instanceId,
                                where = "grid:" + kv.Key.x.ToString(CultureInfo.InvariantCulture) + "," +
                                        kv.Key.y.ToString(CultureInfo.InvariantCulture)
                            }));
                        }
                    }
                }
                catch (Exception)
                {
                }
            }

            Vault vault;

            if (ServiceLocator.TryGet<Vault>(out vault) && vault != null)
            {
                int i = 0;

                foreach (Module m in vault.Modules)
                {
                    if (m != null && seen.Add(m))
                    {
                        list.Add(new KeyValuePair<Module, Place>(m, new Place
                        {
                            shipId = 0,
                            where = "vault:" + i.ToString(CultureInfo.InvariantCulture)
                        }));
                    }

                    i++;
                }
            }

            return list;
        }

        // ---- the lines --------------------------------------------------------

        // One line per upgraded item: ship id, where, data id, steps, Ex spent,
        // Bond spent, the stat counts. The save writes these; a New Game Plus
        // jump holds them in memory across the new world's run-entry reset.
        // One builder, so the two can never write different records.
        internal static List<string> Lines(out int unplaced)
        {
            var lines = new List<string>();
            unplaced = 0;

            var where = new Dictionary<Module, Place>();

            foreach (KeyValuePair<Module, Place> kv in Locate())
                where[kv.Key] = kv.Value;

            foreach (KeyValuePair<Module, SalvageUpgrade> kv in SalvageRecords.All)
            {
                if (kv.Key == null || kv.Value == null || !kv.Value.Any)
                    continue;

                Place p;

                if (!where.TryGetValue(kv.Key, out p))
                {
                    unplaced++;
                    continue;
                }

                lines.Add(string.Join("\t", new[]
                {
                    p.shipId.ToString(CultureInfo.InvariantCulture),
                    p.where,
                    (kv.Key.Data != null ? kv.Key.Data.Id : "?").Replace('\t', ' '),
                    kv.Value.steps.ToString(CultureInfo.InvariantCulture),
                    kv.Value.spentEx.ToString(CultureInfo.InvariantCulture),
                    kv.Value.spentBond.ToString(CultureInfo.InvariantCulture),
                    SalvageMath.EncodeCounts(kv.Value.counts)
                }));
            }

            return lines;
        }

        internal sealed class Record
        {
            public int shipId;
            public string where;
            public string dataId;
            public SalvageUpgrade u;
        }

        internal static Record Parse(string line)
        {
            string[] b = (line ?? "").Split('\t');

            if (b.Length < 7)
                return null;

            int ship, steps, ex, bond;

            if (!int.TryParse(b[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out ship) ||
                !int.TryParse(b[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out steps) ||
                !int.TryParse(b[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out ex) ||
                !int.TryParse(b[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out bond))
            {
                return null;
            }

            var u = new SalvageUpgrade
            {
                steps = Math.Max(0, steps),
                spentEx = Math.Max(0, ex),
                spentBond = Math.Max(0, bond)
            };

            foreach (KeyValuePair<string, int> kv in SalvageMath.DecodeCounts(b[6]))
                u.counts[kv.Key] = kv.Value;

            if (!u.Any)
                return null;

            return new Record { shipId = ship, where = b[1].Trim(), dataId = b[2].Trim(), u = u };
        }

        // Hand each record to the module now sitting where it was saved, and
        // put its upgrades back on. Returns how many found their item.
        internal static int Attach(List<Record> records, out int lost)
        {
            lost = 0;
            int restored = 0;

            List<KeyValuePair<Module, Place>> places = Locate();
            var taken = new HashSet<Module>();

            foreach (Record r in records)
            {
                Module hit = null;

                foreach (KeyValuePair<Module, Place> kv in places)
                {
                    if (taken.Contains(kv.Key) || kv.Value.where != r.where)
                        continue;

                    if (r.where.StartsWith("grid:", StringComparison.Ordinal) && kv.Value.shipId != r.shipId)
                        continue;

                    if (kv.Key.Data == null || kv.Key.Data.Id != r.dataId)
                        continue;

                    hit = kv.Key;
                    break;
                }

                if (hit == null)
                {
                    lost++;
                    continue;
                }

                taken.Add(hit);
                SalvageRecords.Set(hit, r.u);
                SalvageApply.OnModule(hit);
                restored++;
            }

            // ★ REBUILD EVERY SHIP GRID that got one back. A gadget built its
            // gun while the grid was being restored - before this record was
            // read - and the ship's stats were counted then too.
            if (restored > 0)
                foreach (KeyValuePair<ModuleGrid, ModuleGridOwner.Data> g in SalvageApply.ShipGrids())
                    SalvageApply.Rebuild(g.Key, g.Value);

            return restored;
        }

        // ---- writing ----------------------------------------------------------

        // `Save(bool coop)` calls `Save(string)`, so one patch covers both. The
        // overload has to be named or Harmony cannot tell the two apart.
        [HarmonyPatch(typeof(Punk.SaveLoad.GameSaver), "Save", new Type[] { typeof(string) })]
        public class OnSave
        {
            static void Postfix(Punk.SaveLoad.SaveFolder __result)
            {
                if (!SalvageShopPlugin.Active)
                    return;

                try
                {
                    Write(__result.RootDirectory);
                }
                catch (Exception e)
                {
                    SalvageShopPlugin.Log.LogWarning("salvage upgrades not saved: " + e.Message);
                }
            }
        }

        private static void Write(string folder)
        {
            if (string.IsNullOrEmpty(folder))
                return;

            int unplaced;
            List<string> lines = Lines(out unplaced);
            string path = Path.Combine(folder, FileName);

            // ★ THE DELETE IS LOAD-BEARING, not tidiness (see the header).
            if (lines.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);

                return;
            }

            // ★ THE GAME MAKES THE SAVE FOLDER ON A WORKER THREAD, racing this
            // postfix on a run's first save. Make it here (safe to race), as
            // New Game Plus does.
            Directory.CreateDirectory(folder);

            var sb = new StringBuilder();
            sb.AppendLine(Version);
            sb.AppendLine("# one line per upgraded item: ship id <TAB> where <TAB> module id <TAB> " +
                          "steps <TAB> Ex spent <TAB> Bond spent <TAB> stat:steps,...");

            foreach (string line in lines)
                sb.AppendLine(line);

            File.WriteAllText(path, sb.ToString());

            SalvageShopPlugin.Log.LogInfo(
                "saved " + lines.Count + " salvage-upgraded item(s)" +
                (unplaced > 0
                    ? " (" + unplaced + " more are no longer anywhere the game saves a module, so their " +
                      "upgrades end here)"
                    : "") + ".");
        }

        // ---- reading ------------------------------------------------------------

        // ★ THE READ IS A POSTFIX. The run-entry reset is a PREFIX on this same
        // method (SalvageRunEntry): prefix -> original -> postfix is the only
        // order that survives its own reset.
        [HarmonyPatch(typeof(Punk.SaveLoad.GameSaver), "Load")]
        public class OnLoad
        {
            static void Postfix(Punk.SaveLoad.GameSaver __instance, string folderName, bool __result)
            {
                if (!SalvageShopPlugin.Active || !__result)
                    return;

                try
                {
                    Read(__instance, folderName);
                }
                catch (Exception e)
                {
                    SalvageShopPlugin.Log.LogWarning("salvage upgrades not restored: " + e.Message);
                }
            }
        }

        private static void Read(Punk.SaveLoad.GameSaver saver, string folderName)
        {
            string root = null;

            if (saver != null)
                root = Traverse.Create(saver).Field("savesRootDirectory").GetValue<string>();

            // The field is private, so if it is ever renamed this rebuilds it
            // the way the constructor does rather than failing.
            if (string.IsNullOrEmpty(root))
                root = Path.Combine(Application.persistentDataPath, "saves");

            string path = Path.Combine(Path.Combine(root, folderName ?? string.Empty), FileName);

            if (!File.Exists(path))
                return;

            string[] raw = File.ReadAllLines(path);
            bool sawVersion = false;
            var records = new List<Record>();

            foreach (string r in raw)
            {
                string line = (r ?? "").Trim();

                if (line.Length == 0 || line[0] == '#')
                    continue;

                if (!sawVersion)
                {
                    sawVersion = true;

                    if (line != Version)
                    {
                        SalvageShopPlugin.Log.LogWarning(
                            "the save's salvage record is version '" + line + "' and this build reads '" +
                            Version + "', so it is ignored and every item starts this session un-upgraded.");
                        return;
                    }

                    continue;
                }

                Record rec = Parse(line);

                if (rec != null)
                    records.Add(rec);
            }

            if (records.Count == 0)
                return;

            int lost;
            int restored = Attach(records, out lost);

            SalvageShopPlugin.Log.LogInfo(
                restored + " salvage-upgraded item(s) restored from your save" +
                (lost > 0
                    ? " (" + lost + " record(s) found no item where they were saved and were dropped)"
                    : "") + ".");
        }
    }

    // ★ BOTH RUN ENTRIES, as PREFIXES: a new run (RunData.Initialize) and a
    // Continue (GameSaver.Load). The upgrades of the last run belong to nothing.
    internal static class SalvageRunEntry
    {
        [HarmonyPatch(typeof(RunData), "Initialize")]
        public class OnNewRun
        {
            static void Prefix()
            {
                if (SalvageShopPlugin.Active)
                    Enter();
            }
        }

        [HarmonyPatch(typeof(Punk.SaveLoad.GameSaver), "Load")]
        public class OnContinue
        {
            static void Prefix()
            {
                if (SalvageShopPlugin.Active)
                    Enter();
            }
        }

        private static void Enter()
        {
            if (!SalvageShopPlugin.Active)
                return;

            try
            {
                SalvageRecords.Reset();
                SalvageCarry.OnRunEntry();
                SalvageScreen.OnRunEntry();
            }
            catch (Exception e)
            {
                SalvageShopPlugin.Log.LogWarning("salvage run-entry reset: " + e.Message);
            }
        }
    }

    // UPGRADES ACROSS A NEW GAME PLUS JUMP (3.8: "YES"). Through New Game
    // Plus's NgCarry contract, the one Weapon Forge's rank uses:
    //
    //   Leaving   the old world, still loaded: take every record, keyed where
    //             the save would key it.
    //   Arrived   the new world's ships exist as data with their carried grids
    //             installed and the vault restored, before any gun is built:
    //             hand the records back with the Continue path's own Attach.
    //
    // New Game Plus restores each grid slot for slot and the vault in its
    // order, so every place still names the same item; only the ship ids change
    // (an id is a random number from the world's seed), and NgCarry.NewShipId
    // maps each old one.
    //
    // ★ FOUND BY TYPE NAME, NEVER A REFERENCE. With New Game Plus absent this
    // finds nothing and changes nothing. Latched on success only, retried at
    // each run entry, because plugin load order is not guaranteed.
    internal static class SalvageCarry
    {
        private const string CarryType = "GameModeForge.NewGamePlus.NgCarry";

        private static bool _subscribed;
        private static PropertyInfo _carrying;
        private static MethodInfo _newShipId;

        // Held OUTSIDE everything the run-entry reset clears, on purpose.
        private static List<string> _lines;

        internal static void OnRunEntry()
        {
            Subscribe();

            // A run that is NOT the new world of a jump: anything still held
            // belongs to nothing (a jump that never arrived).
            if (_lines != null && !Carrying())
            {
                SalvageShopPlugin.Log.LogWarning(
                    _lines.Count + " salvage-upgraded item(s) were held for a New Game Plus jump that " +
                    "never arrived - dropped.");
                _lines = null;
            }
        }

        private static void Subscribe()
        {
            if (_subscribed)
                return;

            Type t = SalvageUi.FindTypeQuietly(CarryType);

            if (t == null)
                return;   // not installed, or not loaded yet - asked again next run

            const BindingFlags S = BindingFlags.Public | BindingFlags.Static;

            EventInfo leaving = t.GetEvent("Leaving", S);
            EventInfo arrived = t.GetEvent("Arrived", S);
            _carrying = t.GetProperty("Carrying", S);
            _newShipId = t.GetMethod("NewShipId", S, null, new[] { typeof(int) }, null);

            _subscribed = true;

            if (leaving == null || arrived == null || _carrying == null || _newShipId == null ||
                leaving.EventHandlerType != typeof(Action) || arrived.EventHandlerType != typeof(Action))
            {
                SalvageShopPlugin.Log.LogWarning(
                    "New Game Plus is installed but its carry contract is not the one this build reads - " +
                    "salvage upgrades will NOT carry across a jump.");
                return;
            }

            leaving.AddEventHandler(null, new Action(OnLeaving));
            arrived.AddEventHandler(null, new Action(OnArrived));

            SalvageShopPlugin.Log.LogInfo(
                "New Game Plus found - salvage upgrades carry across a jump with their items.");
        }

        private static bool Carrying()
        {
            try
            {
                return _carrying != null && (bool)_carrying.GetValue(null, null);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void OnLeaving()
        {
            int unplaced;
            _lines = SalvageSave.Lines(out unplaced);

            SalvageShopPlugin.Log.LogInfo(
                "taking " + _lines.Count + " salvage-upgraded item(s) across the New Game Plus jump" +
                (unplaced > 0 ? " (" + unplaced + " are nowhere a save keeps a module and stay behind)" : "") +
                ".");
        }

        private static void OnArrived()
        {
            List<string> lines = _lines;
            _lines = null;

            if (lines == null || lines.Count == 0)
                return;

            var records = new List<SalvageSave.Record>();
            int left = 0;

            foreach (string line in lines)
            {
                SalvageSave.Record r = SalvageSave.Parse(line);

                if (r == null)
                    continue;

                if (r.shipId != 0)
                {
                    int now = NewShipId(r.shipId);

                    if (now == 0)
                    {
                        left++;
                        continue;
                    }

                    r.shipId = now;
                }

                records.Add(r);
            }

            int lost = 0;
            int restored = records.Count > 0 ? SalvageSave.Attach(records, out lost) : 0;

            SalvageShopPlugin.Log.LogInfo(
                restored + " salvage-upgraded item(s) handed back in the new world" +
                (left + lost > 0
                    ? " (" + (left + lost) + " stayed behind - their ship or their carry switch did not cross)"
                    : "") + ".");
        }

        private static int NewShipId(int old)
        {
            try
            {
                return (int)_newShipId.Invoke(null, new object[] { old });
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
