using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Logging;

namespace GameModeForge.NewGamePlus
{
    // WHERE NEW GAME PLUS'S LINES GO - a copy of Pause Options' PauseLog.
    //
    // Pause Options' answer, 2026-09-28, and the same one here: into
    // GameModeForge.log - "the mods that fall under the gamemodeforge logs
    // should fall under the logs for that mod" - and into its own
    // BepInEx\NewGamePlus.log when Game Mode Forge is not installed, because
    // this DLL has to stand alone.
    //
    // ★ FOUND BY TYPE NAME, NEVER A REFERENCE. Game Mode Forge's `ForgeLog`
    // is looked up among the loaded assemblies and its `Source(name)` is
    // called by reflection. A source made there is wired to Game Mode Forge's
    // file, its LogOutput.log error copy and its OwnLogFile switch, so this
    // mod inherits all three by handing it each line.
    //
    // ★ DECIDED ON THE FIRST LINE, AND THE FIRST LINE IS NEVER AT STARTUP.
    // Plugin load order is not guaranteed, so at this plugin's Awake Game
    // Mode Forge may simply not have loaded yet - which is why Awake writes
    // only through the registered Logger (LogOutput.log) and never through a
    // source made here. Every line made here comes from an in-game patch,
    // by which time every plugin has loaded, so "Game Mode Forge is not
    // there" is a real answer rather than a load-order accident.
    //
    // ★ THE OWN-FILE HALF IS A DELIBERATE COPY OF GAME MODE FORGE'S ForgeLog
    // (itself a copy of Weapon Forge's), per duplicate-by-default: a log file
    // is not worth a dependency. Only the names differ, plus the file opening
    // on the first line rather than at Awake - so a launch with Game Mode
    // Forge installed never creates an empty NewGamePlus.log.
    //
    // ⚠ Diagnostic code must not throw. Every path catches, and every failure
    // FALLS BACK TO LogOutput.log AND SAYS SO.
    internal static class NgLog
    {
        private const string Name = "NewGamePlus";
        private const string Title = "New Game Plus";

        // A game error belongs in NewGamePlus.log when its stack trace names
        // a class in this namespace. (With Game Mode Forge installed its own
        // catcher already claims every "GameModeForge." frame - this one's
        // included - so this catcher is only registered for the own file.)
        private const string Namespace = "GameModeForge.NewGamePlus";

        // Game Mode Forge's log class, by name. See the header.
        private const string GameModeForgeLog = "GameModeForge.ForgeLog";

        private const string UnitySource = "Unity Log";

        private const int Undecided = 0;
        private const int ToGameModeForge = 1;
        private const int ToOwnFile = 2;

        private static readonly object Gate = new object();

        private static int _route = Undecided;
        private static ManualLogSource _gmf;

        private static StreamWriter _writer;
        private static string _fileName;
        private static bool _opened;
        private static bool _closed;
        private static bool _saidWriteFailed;
        private static string _pending;

        // The plugin's own BaseUnityPlugin.Logger - the one source that IS
        // registered, so it can tell LogOutput.log when the file side fails.
        private static ManualLogSource _main;

        // Replaces `BepInEx.Logging.Logger.CreateLogSource(name)`: a source
        // that is never added to Logger.Sources reaches nobody but us.
        public static ManualLogSource Source(string name)
        {
            var source = new ManualLogSource(name);

            source.LogEvent += OnEvent;

            return source;
        }

        // First thing in Awake. Opens nothing - see the header.
        public static void Attach(ManualLogSource main)
        {
            _main = main;
        }

        // Appended to the "loaded" line in LogOutput.log.
        public static string Where
        {
            get
            {
                return " - in-game lines go to BepInEx\\GameModeForge.log (Game " +
                       "Mode Forge's log), or to BepInEx\\" + Name + ".log when " +
                       "Game Mode Forge is not installed. Only errors are copied here.";
            }
        }

        // OnApplicationQuit. Only the own file is ours to close.
        public static void Close()
        {
            lock (Gate)
            {
                _closed = true;

                if (_writer == null)
                    return;

                try
                {
                    _writer.Flush();
                    _writer.Dispose();
                }
                catch { }

                _writer = null;
            }
        }

        // ---- where a line goes ------------------------------------------

        private static void OnEvent(object sender, LogEventArgs e)
        {
            try
            {
                if (Route() == ToGameModeForge)
                {
                    // Game Mode Forge's source files it, copies errors to
                    // LogOutput.log and honours its OwnLogFile switch.
                    _gmf.Log(e.Level, e.Data);
                    return;
                }

                // The file unavailable: straight to LogOutput.log. Never
                // nowhere.
                if (!WriteLine(e.ToString()))
                {
                    Forward(sender, e);
                    SayPending();
                    return;
                }

                if ((e.Level & (LogLevel.Error | LogLevel.Fatal)) != LogLevel.None)
                    Forward(sender, e);
            }
            catch { }
        }

        private static int Route()
        {
            if (_route != Undecided)
                return _route;

            lock (Gate)
            {
                if (_route != Undecided)
                    return _route;

                ManualLogSource gmf = FindGameModeForgeLog();

                if (gmf != null)
                {
                    _gmf = gmf;
                    _route = ToGameModeForge;
                }
                else
                {
                    _route = ToOwnFile;

                    try
                    {
                        Logger.Listeners.Add(new GameErrorCatcher());
                    }
                    catch { }
                }
            }

            // Outside Gate: this line comes back through Game Mode Forge's
            // own handler, which takes its own lock.
            if (_route == ToGameModeForge)
            {
                try
                {
                    _gmf.LogInfo(Title + " (BUILD " + BuildStamp() + ") logs " +
                                 "here, in Game Mode Forge's file.");
                }
                catch { }
            }

            return _route;
        }

        // Game Mode Forge's `ForgeLog.Source(name)`, or null when Game Mode
        // Forge is not installed. Scanned quietly - AccessTools.TypeByName
        // logs a warning for an absent type, and "not installed" is a normal
        // state.
        private static ManualLogSource FindGameModeForgeLog()
        {
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t;

                    try
                    {
                        t = asm.GetType(GameModeForgeLog, false);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (t == null)
                        continue;

                    MethodInfo source = t.GetMethod(
                        "Source",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                        null, new[] { typeof(string) }, null);

                    if (source == null)
                        return null;

                    return source.Invoke(null, new object[] { Namespace })
                        as ManualLogSource;
                }
            }
            catch (Exception)
            {
                // If Game Mode Forge's log cannot be reached, use our own.
            }

            return null;
        }

        // What `Logger.Sources.Add` would have done for this one event.
        private static void Forward(object sender, LogEventArgs e)
        {
            foreach (ILogListener listener in Logger.Listeners)
            {
                if (listener == null || listener is GameErrorCatcher)
                    continue;

                if ((listener.LogLevelFilter & e.Level) == LogLevel.None)
                    continue;

                try
                {
                    listener.LogEvent(sender, e);
                }
                catch { }
            }
        }

        private static bool WriteLine(string line)
        {
            lock (Gate)
            {
                if (_closed)
                    return false;

                if (!_opened)
                    Open();

                if (_writer == null)
                    return false;

                try
                {
                    _writer.WriteLine(line);
                    return true;
                }
                catch (Exception ex)
                {
                    if (!_saidWriteFailed)
                    {
                        _saidWriteFailed = true;
                        _pending = "could not write to BepInEx\\" + _fileName +
                                   " (" + ex.GetType().Name + ": " + ex.Message +
                                   ") - from here on New Game Plus logs to " +
                                   "LogOutput.log instead.";
                    }

                    try { _writer.Dispose(); } catch { }
                    _writer = null;
                    return false;
                }
            }
        }

        private static void SayPending()
        {
            string text;

            lock (Gate)
            {
                text = _pending;
                _pending = null;
            }

            if (text != null && _main != null)
                _main.LogWarning(text);
        }

        // ---- opening the own file ---------------------------------------

        // Called under Gate, once.
        private static void Open()
        {
            _opened = true;

            string folder = null;

            try
            {
                folder = Paths.BepInExRootPath;
            }
            catch { }

            if (string.IsNullOrEmpty(folder))
            {
                _pending = "could not find the BepInEx folder, so New Game Plus " +
                           "logs to LogOutput.log.";
                return;
            }

            string name = Name + ".log";
            FileStream stream = TryOpen(Path.Combine(folder, name), 8);
            string previous = null;

            if (stream != null)
            {
                previous = KeepPrevious(stream, Path.Combine(folder, Name + ".prev.log"));
            }
            else
            {
                for (int i = 2; i <= 5 && stream == null; i++)
                {
                    name = Name + "." + i + ".log";
                    stream = TryOpen(Path.Combine(folder, name), 1);
                }

                if (stream != null)
                {
                    stream.SetLength(0);
                    previous = Name + ".log is in use - is the game running " +
                               "twice? - so this launch writes " + name +
                               ", and " + Name + ".prev.log was left alone.";
                }
            }

            if (stream == null)
            {
                _pending = "could not open " + Name + ".log for writing, so " +
                           "New Game Plus logs to LogOutput.log.";
                return;
            }

            _fileName = name;
            _writer = new StreamWriter(stream, new UTF8Encoding(false));
            _writer.AutoFlush = true;   // a crash must not eat the last lines

            _writer.WriteLine(
                "=== " + Title + " log - this launch started " +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                " - BUILD " + BuildStamp() + " ===");

            _writer.WriteLine(previous ??
                "Game Mode Forge is not installed, so New Game Plus keeps its " +
                "own log. The launch before this one is in " + Name +
                ".prev.log; LogOutput.log keeps the load lines and a copy of " +
                "every error.");
        }

        // A relaunch overlaps: wait for the quitting game to let go.
        private static FileStream TryOpen(string path, int attempts)
        {
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    return new FileStream(
                        path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                }
                catch (IOException)
                {
                    if (i + 1 < attempts)
                        Thread.Sleep(250);
                }
                catch (Exception)
                {
                    return null;
                }
            }

            return null;
        }

        // Copy the last launch into .prev.log through the handle we hold,
        // then empty it. If .prev.log cannot be written, append instead of
        // throwing the old log away.
        private static string KeepPrevious(FileStream stream, string prevPath)
        {
            if (stream.Length == 0)
                return null;

            try
            {
                using (var prev = new FileStream(
                    prevPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    stream.Position = 0;
                    stream.CopyTo(prev);
                }

                stream.SetLength(0);
                stream.Position = 0;
                return null;
            }
            catch
            {
                stream.Seek(0, SeekOrigin.End);
                return "could not write " + Name + ".prev.log (is it open " +
                       "somewhere?), so this launch is appended below the last one.";
            }
        }

        private static string BuildStamp()
        {
            try
            {
                return File.GetLastWriteTime(typeof(NgLog).Assembly.Location)
                           .ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return "unknown";
            }
        }

        // A frame must START with the namespace - at the start of a line, or
        // after a space, tab, "(" or "<". Written the same way in every Forge
        // mod.
        internal static bool NamesThisMod(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            string marker = Namespace + ".";
            int at = text.IndexOf(marker, StringComparison.Ordinal);

            while (at >= 0)
            {
                if (at == 0)
                    return true;

                char before = text[at - 1];

                if (char.IsWhiteSpace(before) || before == '(' || before == '<')
                    return true;

                at = text.IndexOf(marker, at + 1, StringComparison.Ordinal);
            }

            return false;
        }

        // Copies the game's own errors that name this mod into the own file.
        // Capped: three copies of any one error, fifty in all, per launch.
        private sealed class GameErrorCatcher : ILogListener
        {
            private const int PerError = 3;
            private const int Total = 50;

            private readonly Dictionary<string, int> _seen =
                new Dictionary<string, int>();

            private int _copied;

            public LogLevel LogLevelFilter
            {
                get { return LogLevel.Fatal | LogLevel.Error | LogLevel.Warning; }
            }

            public void LogEvent(object sender, LogEventArgs e)
            {
                try
                {
                    if (e == null || e.Source == null ||
                        e.Source.SourceName != UnitySource)
                        return;

                    string text = e.Data as string ??
                                  (e.Data != null ? e.Data.ToString() : null);

                    if (!NamesThisMod(text))
                        return;

                    string key = text;
                    int cut = text.IndexOf('\n');

                    if (cut > 0)
                        key = text.Substring(0, cut);

                    string note = null;

                    lock (_seen)
                    {
                        int n;
                        _seen.TryGetValue(key, out n);
                        _seen[key] = ++n;

                        if (n > PerError || _copied >= Total)
                        {
                            if (n == PerError + 1 && _copied < Total)
                                WriteLine("(the same game error again - no more " +
                                          "copies of it are written this launch)");
                            return;
                        }

                        if (_copied == 0)
                            note = "--- the GAME's own error below names " + Title +
                                   " in its stack trace. BepInEx leaves the game's " +
                                   "errors out of LogOutput.log by default, so " +
                                   "this file is the only place it is written down. ---";

                        _copied++;
                    }

                    if (note != null)
                        WriteLine(note);

                    WriteLine(e.ToString());
                }
                catch { }
            }

            public void Dispose()
            {
            }
        }
    }
}
