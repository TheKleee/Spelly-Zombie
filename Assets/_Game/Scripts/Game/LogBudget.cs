using System;
using UnityEngine;

namespace SpellyZombie
{
    /// The console keeps up with an army. Every "[SpellyZombie]" line costs a string, a
    /// write to the log file and, in the editor, a stack trace and a console row; a rubble
    /// army wrote about 300 of them a second. Past DrawingConfig.LogLinesPerSecond the
    /// rest of that second is counted and named in one line. Warnings, errors and every
    /// other tool's lines ("[Stress]", "[RuneLibrary]") always pass.
    class LogBudget : ILogHandler
    {
        const string Ours = "[SpellyZombie]";

        readonly ILogHandler _inner;
        readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        readonly object _lock = new object();
        long _second = -1;
        int _written, _held;

        LogBudget(ILogHandler inner) => _inner = inner;

        // read by any thread that logs, so it is a plain number here: the config class loads a file
        // (and logs) while it starts up, and a log handler waiting on that would wait on itself
        static volatile int _perSecond = 40;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Install()
        {
            var now = Debug.unityLogger.logHandler;
            if (now is LogBudget) return; // play mode entered again without a domain reload
            Debug.unityLogger.logHandler = new LogBudget(now);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ReadBudget() => _perSecond = Mathf.Max(1, (int)DrawingConfig.LogLinesPerSecond);

        public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args)
        {
            if (type == LogType.Log && IsOurs(format, args))
            {
                int held = 0;
                lock (_lock)
                {
                    long second = _clock.ElapsedMilliseconds / 1000;
                    if (second != _second)
                    {
                        _second = second;
                        held = _held;
                        _held = 0;
                        _written = 0;
                    }
                    if (_written >= _perSecond) { _held++; return; }
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
        static bool IsOurs(string format, object[] args)
        {
            string text = format == "{0}" && args != null && args.Length == 1 ? args[0] as string : format;
            return text != null && text.StartsWith(Ours, StringComparison.Ordinal);
        }
    }
}
