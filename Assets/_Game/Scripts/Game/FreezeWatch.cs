using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace SpellyZombie
{
    /// A freeze leaves nothing behind: the game just stops writing its log. A
    /// thread beside the game notices when no frame has finished for a few
    /// seconds and writes to freeze.log (next to Player.log) where the main
    /// thread last said it was: which part of the frame, which PerfMarkers
    /// sections it is inside, and how much was alive. It writes again while the
    /// freeze lasts and once more when the game moves again.
    public static class FreezeWatch
    {
        const int StuckSeconds = 5, AgainSeconds = 5;

        static readonly string[] _inside = new string[32];
        static volatile int _depth;
        static int _mainThread;
        static long _beat;                      // frames finished
        static volatile string _phase = "starting";
        static volatile string _scene = "";
        static volatile int _golems, _matter, _spells, _zombies;
        static volatile bool _asleep, _stop;
        static string _path, _version;
        static Thread _thread;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            _path = Path.Combine(Application.persistentDataPath, "freeze.log");
            _version = Application.version + " / Unity " + Application.unityVersion + (Application.isEditor ? " / editor" : " / build");
            _stop = false;
            _asleep = false;
            _depth = 0;

            var early = new GameObject("SZ_FreezeWatch");
            UnityEngine.Object.DontDestroyOnLoad(early);
            early.AddComponent<FreezeBeatEarly>();
            early.AddComponent<FreezeBeatLate>();

            Application.quitting += () => _stop = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.pauseStateChanged += s => _asleep = s == UnityEditor.PauseState.Paused;
#endif
            if (_thread == null || !_thread.IsAlive)
            {
                _thread = new Thread(Watch) { IsBackground = true, Name = "SZ FreezeWatch" };
                _thread.Start();
            }
        }

        // ---- the main thread says where it is (PerfMarkers and the two beat components) ----
        internal static void Enter(string section)
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThread) return;
            int d = _depth;
            if (d < _inside.Length) _inside[d] = section;
            _depth = d + 1;
        }

        internal static void Leave()
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThread) return;
            if (_depth > 0) _depth = _depth - 1;
        }

        internal static void Phase(string phase) => _phase = phase;

        internal static void FrameDone()
        {
            Interlocked.Increment(ref _beat);
            _depth = 0; // a section an exception skipped out of does not pile up
            _scene = ActiveScene.Name;
            _golems = Golem.All.Count;
            _matter = Matter.Living.Count;
            _spells = SpellParticle.Living.Count;
            _zombies = Zombie.All.Count;
        }

        /// No frames on purpose (the window lost focus and the game does not run behind): not a freeze.
        internal static void Asleep(bool asleep) => _asleep = asleep;

        // ---- the thread beside the game ----
        static void Watch()
        {
            long last = -1;
            int still = 0, toldAt = 0;
            try
            {
                while (!_stop)
                {
                    Thread.Sleep(1000);
                    long now = Interlocked.Read(ref _beat);
                    if (now != last || _asleep)
                    {
                        if (toldAt > 0 && now != last) Write($"moving again after about {still} s");
                        last = now; still = 0; toldAt = 0;
                        continue;
                    }
                    still++;
                    if (still < StuckSeconds || (toldAt > 0 && still - toldAt < AgainSeconds)) continue;
                    toldAt = still;
                    Write($"no frame for {still} s. {Where()}");
                }
            }
            catch (ThreadAbortException) { } // the editor reloads its code
            catch (Exception) { }
        }

        static string Where()
        {
            var sb = new StringBuilder(256);
            sb.Append("Last known: ").Append(_phase);
            int d = Math.Min(_depth, _inside.Length);
            for (int i = 0; i < d; i++) sb.Append(" > ").Append(_inside[i]);
            if (d == 0) sb.Append(" > (in none of the game's timed sections)");
            sb.Append(" | scene ").Append(_scene)
              .Append(" | golems ").Append(_golems).Append(", matter ").Append(_matter)
              .Append(", spells ").Append(_spells).Append(", zombies ").Append(_zombies)
              .Append(" | ").Append(_version);
            return sb.ToString();
        }

        static void Write(string what)
        {
            try { File.AppendAllText(_path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + what + Environment.NewLine); }
            catch (Exception) { }
        }
    }

    /// First in every part of the frame: says which part the scripts are in.
    [DefaultExecutionOrder(-32000)]
    class FreezeBeatEarly : MonoBehaviour
    {
        void FixedUpdate() => FreezeWatch.Phase("the scripts' physics step (FixedUpdate)");
        void Update() => FreezeWatch.Phase("the scripts' frame update (Update)");
        void LateUpdate() => FreezeWatch.Phase("the scripts' late update (LateUpdate)");
        void OnApplicationFocus(bool focused) => FreezeWatch.Asleep(!focused && !Application.runInBackground);
        void OnApplicationPause(bool paused) => FreezeWatch.Asleep(paused);
    }

    /// Last in every part of the frame: what follows is the engine's own work.
    [DefaultExecutionOrder(32000)]
    class FreezeBeatLate : MonoBehaviour
    {
        void FixedUpdate() => FreezeWatch.Phase("the physics engine, after the scripts' physics step (PhysX and its collision and trigger calls)");
        void Update() => FreezeWatch.Phase("after the scripts' frame update (animation, coroutines)");
        void LateUpdate()
        {
            FreezeWatch.Phase("drawing the frame (rendering, UI, end of frame)");
            FreezeWatch.FrameDone();
        }
    }
}
