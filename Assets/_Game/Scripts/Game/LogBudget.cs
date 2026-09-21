using System;
using UnityEngine;

namespace SpellyZombie
{
    /// The console keeps up with an army. Every "[SpellyZombie]" line costs a string, a
    /// write to the log file and, in the editor, a stack trace and a console row; a rubble
    /// army wrote over a thousand of them a second. Lines are counted by KIND (how they
    /// begin: "Golem", "golem ", "the Water stands up"): a kind's first line in a second is
    /// always written, so a lone "Player DOWNED" is never lost in a flood of golem wounds;
    /// after that a kind gets PerKind lines a second and everything together
    /// DrawingConfig.LogLinesPerSecond. What was held back is named in one line. Warnings,
    /// errors and every other tool's lines ("[Stress]", "[RuneLibrary]") always pass.
    class LogBudget : ILogHandler
    {
        const string Ours = "[SpellyZombie]";

        readonly ILogHandler _inner;
        readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        readonly object _lock = new object();
        long _second = -1;
        int _written, _held;
        const int PerKind = 6;
        readonly System.Collections.Generic.Dictionary<int, int> _kinds
            = new System.Collections.Generic.Dictionary<int, int>();

        LogBudget(ILogHandler inner) => _inner = inner;

        static LogBudget _live;

        /// This second still writes: whoever is about to build a costly line asks first.
        public static bool HasRoom
        {
            get
            {
                var b = _live;
                if (b == null) return true;
                lock (b._lock)
                    return b._clock.ElapsedMilliseconds / 1000 != b._second || b._written < _perSecond;
            }
        }

        // read by any thread that logs, so it is a plain number here: the config class loads a file
        // (and logs) while it starts up, and a log handler waiting on that would wait on itself
        static volatile int _perSecond = 40;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Install()
        {
            var now = Debug.unityLogger.logHandler;
            if (now is LogBudget was) { _live = was; return; } // play mode entered again without a domain reload
            Debug.unityLogger.logHandler = _live = new LogBudget(now);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ReadBudget() => _perSecond = Mathf.Max(1, (int)DrawingConfig.LogLinesPerSecond);

        public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args)
        {
            string ours = type == LogType.Log ? Ours_(format, args) : null;
            if (ours != null)
            {
                int held = 0;
                int kind = Kind(ours);
                lock (_lock)
                {
                    long second = _clock.ElapsedMilliseconds / 1000;
                    if (second != _second)
                    {
                        _second = second;
                        held = _held;
                        _held = 0;
                        _written = 0;
                        _kinds.Clear();
                    }
                    _kinds.TryGetValue(kind, out int ofKind);
                    if (ofKind > 0 && (ofKind >= PerKind || _written >= _perSecond)) { _held++; return; }
                    _kinds[kind] = ofKind + 1;
                    _written++;
                }
                if (held > 0)
                    _inner.LogFormat(LogType.Log, null, "{0}",
                        $"{Ours} ...and {held} more lines in one second, not written (LogLinesPerSecond)");
            }
            _inner.LogFormat(type, context, format, args);
        }

        public void LogException(Exception exception, UnityEngine.Object context)
            => _inner.LogException(exception, context);

        // Debug.Log(message) arrives as ("{0}", message)
        static string Ours_(string format, object[] args)
        {
            string text = format == "{0}" && args != null && args.Length == 1 ? args[0] as string : format;
            return text != null && text.StartsWith(Ours, StringComparison.Ordinal) ? text : null;
        }

        /// What kind of line it is: how it begins, up to the first number or punctuation
        /// ("Golem(Clone): slammed into..., 78 hp left" and "Golem(Clone) destroyed by freezing" are one kind).
        static int Kind(string text)
        {
            int hash = 17;
            int end = Math.Min(text.Length, Ours.Length + 1 + 32);
            for (int i = Ours.Length + 1; i < end; i++)
            {
                char c = text[i];
                if (char.IsDigit(c) || c == ':' || c == ',' || c == '(' || c == '-') break;
                hash = hash * 31 + c;
            }
            return hash;
        }
    }
}
