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
            foreach (var f in _found)
            {
                long v = f.R.LastValue;
                f.Sum += v;
                f.RunSum += v;
            }
            _runFrames++;
        }

        // ---- every timer the engine has, found as it appears ----
        // A script's own timer ("Golem.Update() [Invoke]") exists only once the first golem has run, and the
        // engine's loop phases go by names that change between Unity versions: so nothing is named here, the
        // list is read again each second and whatever measures time is followed from then on.
        class Found { public string Name; public bool Script; public ProfilerRecorder R; public double Sum, RunSum; }
        readonly List<Found> _found = new List<Found>();
        readonly HashSet<string> _foundNames = new HashSet<string>();
        readonly List<ProfilerRecorderHandle> _handles = new List<ProfilerRecorderHandle>();
        int _handlesRead, _runFrames;
        const int MostFollowed = 700;

        static readonly string[] EngineNames =
        {
            "SZ ", "PlayerLoop", "EditorLoop", "Initialization.", "EarlyUpdate.", "FixedUpdate.", "PreUpdate.", "Update.",
            "PreLateUpdate.", "PostLateUpdate.", "Inl_", "RenderPipelineManager.", "UniversalRenderPipeline.",
            "Camera.Render", "Culling", "Shadows.", "RenderLoop.", "SRPBatcher", "MeshSkinning", "Gfx.", "GUI.",
            "UGUI.", "Canvas.", "Physics.", "Animator", "Director.", "ParticleSystem.", "GC.", "Destroy",
            "Instantiate", "Loading.", "AudioManager", "UpdateRendererBoundingVolumes", "EventSystem",
        };

        /// Once a second: follow the timers that appeared since the last look.
        public void Rescan()
        {
            if (_found.Count >= MostFollowed) return;
            _handles.Clear();
            ProfilerRecorderHandle.GetAvailable(_handles);
            // the engine appends new timers at the end of its list
            for (int i = _handlesRead; i < _handles.Count && _found.Count < MostFollowed; i++)
            {
                var d = ProfilerRecorderHandle.GetDescription(_handles[i]);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                string name = d.Name;
                bool script = name.Contains("[Invoke]") || name.Contains("[Coroutine");
                if (!script && !IsEngine(name)) continue;
                if (!_foundNames.Add(name)) continue;
                var rec = new ProfilerRecorder(_handles[i], 1, ProfilerRecorderOptions.Default);
                rec.Start();
                _found.Add(new Found { Name = name, Script = script, R = rec });
            }
            _handlesRead = _handles.Count;
        }

        static bool IsEngine(string name)
        {
            foreach (var n in EngineNames)
                if (name.StartsWith(n, System.StringComparison.Ordinal)) return true;
            return false;
        }

        /// The costliest timers since the last Clear, "name ms" per frame, scripts or engine.
        public string Top(bool scripts, int count, int frames)
            => List(scripts, count, f => f.Sum, frames, 0.05);

        /// The costliest timers of the whole run, one per line.
        public string TopOfRun(bool scripts, int count)
            => List(scripts, count, f => f.RunSum, _runFrames, 0.02, "\n  ");

        string List(bool scripts, int count, System.Func<Found, double> of, int frames, double floorMs, string sep = ", ")
        {
            var pick = new List<Found>();
            foreach (var f in _found) if (f.Script == scripts) pick.Add(f);
            pick.Sort((a, b) => of(b).CompareTo(of(a)));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < pick.Count && i < count; i++)
            {
                double ms = of(pick[i]) / 1e6 / System.Math.Max(1, frames);
                if (ms < floorMs) break;
                if (sb.Length > 0) sb.Append(sep);
                sb.Append(pick[i].Name.Replace(" [Invoke]", "").Replace("()", "")).Append(' ').Append(ms.ToString("0.0"));
            }
            return sb.Length > 0 ? sb.ToString() : "-";
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
            foreach (var f in _found) f.Sum = 0;
        }

        public void Dispose()
        {
            foreach (var r in _recs) r.R.Dispose();
            _recs.Clear();
            foreach (var f in _found) f.R.Dispose();
            _found.Clear();
        }
    }
}
#endif
