#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

namespace SpellyZombie
{
    /// Editor-only: the engine's timers and the game's own (PerfMarkers), read
    /// once a frame and averaged over the frames between two reports. Shared by
    /// the Frame Report and the Stress Test.
    public class FrameTimers : System.IDisposable
    {
        // what we want to know -> the timer names, first found wins
        public static readonly (string key, string label, string[] names, bool time)[] Wanted =
        {
            ("main", "main thread", new[] { "Main Thread" }, true),
            ("update", "scripts Update", new[] { "BehaviourUpdate" }, true),
            ("turns", "  of it, element turns", new[] { "SZ Element Turns" }, true),
            ("late", "scripts LateUpdate", new[] { "LateBehaviourUpdate" }, true),
            ("fixed", "scripts FixedUpdate", new[] { "FixedBehaviourUpdate" }, true),
            ("brains", "  of it, golem brains", new[] { "SZ Golem Brains" }, true),
            ("physics", "physics", new[] { "Physics.Simulate", "Physics.Processing" }, true),
            ("impacts", "collision hits", new[] { "SZ Impacts" }, true),
            ("births", "golem births", new[] { "SZ Golem Births" }, true),
            ("logs", "console lines", new[] { "SZ Log Lines" }, true),
            ("particles", "particles", new[] { "PreLateUpdate.ParticleSystemBeginUpdateAll", "ParticleSystem.Update" }, true),
            ("animation", "animation", new[] { "PreLateUpdate.DirectorUpdateAnimationBegin", "Animators.Update" }, true),
            ("render", "rendering", new[] { "PostLateUpdate.FinishFrameRendering", "FinishFrameRendering",
                "RenderPipelineManager.DoRenderLoop_Internal()", "UniversalRenderPipeline.RenderCameraStack" }, true),
            ("gpu", "waiting on GPU", new[] { "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForGfxCommandsFromMainThread" }, true),
            ("gc", "GC per frame (KB)", new[] { "GC Allocated In Frame" }, false),
            ("draws", "draw calls", new[] { "Draw Calls Count" }, false),
            ("batches", "batches", new[] { "Batches Count" }, false),
            ("setpass", "setpass calls", new[] { "SetPass Calls Count" }, false),
            ("tris", "triangles (K)", new[] { "Triangles Count" }, false),
            ("shadows", "shadow casters", new[] { "Shadow Casters Count" }, false),
        };

        class Rec { public string Key, Label; public bool Time; public ProfilerRecorder R; public double Sum; }
        readonly List<Rec> _recs = new List<Rec>();

        public FrameTimers()
        {
            PerfMarkers.Touch(); // the game's timers exist before we look them up
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var byName = new Dictionary<string, ProfilerRecorderHandle>();
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (!byName.ContainsKey(d.Name)) byName[d.Name] = h;
            }
            foreach (var w in Wanted)
                foreach (var name in w.names)
                    if (byName.TryGetValue(name, out var h))
                    {
                        var rec = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default);
                        rec.Start();
                        _recs.Add(new Rec { Key = w.key, Label = w.label, Time = w.time, R = rec });
                        break;
                    }
        }

        /// Once a frame.
        public void Sample()
        {
            foreach (var r in _recs) r.Sum += r.R.LastValue;
        }

        /// Per-frame average: milliseconds for times, else the timer's own unit
        /// (KB for GC, thousands for triangles). -1 when the engine has no such timer.
        public double Avg(string key, int frames)
        {
            foreach (var r in _recs)
                if (r.Key == key) return Scaled(r, r.Sum / System.Math.Max(1, frames));
            return -1;
        }

        public IEnumerable<(string key, string label, bool time, double avg)> All(int frames)
        {
            foreach (var r in _recs)
                yield return (r.Key, r.Label, r.Time, Scaled(r, r.Sum / System.Math.Max(1, frames)));
        }

        static double Scaled(Rec r, double v) =>
            r.Time ? v / 1e6 : r.Label.EndsWith("(KB)") ? v / 1024.0 : r.Label.EndsWith("(K)") ? v / 1000.0 : v;

        public void Clear()
        {
            foreach (var r in _recs) r.Sum = 0;
        }

        public void Dispose()
        {
            foreach (var r in _recs) r.R.Dispose();
            _recs.Clear();
        }
    }
}
#endif
