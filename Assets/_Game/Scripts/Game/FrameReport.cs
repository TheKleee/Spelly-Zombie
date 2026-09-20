#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Editor-only: where the frame goes (his ask, Sep 18: the Map Creator
    /// lags). Records the engine's own timers for 5 s in Play mode and prints
    /// one report - scripts, physics, particles, rendering, waiting on the GPU,
    /// garbage, draw calls - plus what exists in the scene, then copies it to
    /// the clipboard.
    public class FrameReport : MonoBehaviour
    {
        const float Seconds = 5f;

        // the timers that do not sit inside another one
        static readonly string[] TopLevel = { "update", "late", "fixed", "physics", "particles", "animation", "render", "gpu" };

        FrameTimers _timers;
        float _left = Seconds, _time;
        int _frames;

        [MenuItem("Spelly Zombie/Test/Frame Report (5 s)")]
        static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Frame] Enter Play mode where it lags first."); return; }
            if (FindAnyObjectByType<FrameReport>() != null) return;
            new GameObject("~FrameReport").AddComponent<FrameReport>();
            Debug.Log($"[Frame] recording {Seconds:0} s - keep the view where it lags");
        }

        void Start() => _timers = new FrameTimers();

        void Update()
        {
            _frames++;
            _time += Time.unscaledDeltaTime;
            _timers.Sample();
            _left -= Time.unscaledDeltaTime;
            if (_left > 0f) return;

            var sb = new StringBuilder("[Frame] REPORT (copied to the clipboard)\n");
            float avgMs = _time / Mathf.Max(1, _frames) * 1000f;
            sb.AppendLine($"frame {avgMs:0.0} ms = {1000f / Mathf.Max(0.01f, avgMs):0} fps");
            string biggest = null;
            double biggestMs = 0;
            foreach (var t in _timers.All(_frames))
            {
                if (t.time)
                {
                    sb.AppendLine($"  {t.label}: {t.avg:0.00} ms");
                    if (System.Array.IndexOf(TopLevel, t.key) >= 0 && t.avg > biggestMs) { biggestMs = t.avg; biggest = t.label; }
                }
                else if (t.label.EndsWith("(KB)")) sb.AppendLine($"  {t.label}: {t.avg:0.0}");
                else sb.AppendLine($"  {t.label}: {t.avg:0}");
            }
            if (biggest != null) sb.AppendLine($"most of it: {biggest} ({biggestMs:0.0} ms)");

            int awake = 0;
            var bodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            foreach (var b in bodies) if (!b.isKinematic && !b.IsSleeping()) awake++;
            sb.AppendLine($"scene: {FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length} renderers, " +
                $"{FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length} particle systems, " +
                $"{FindObjectsByType<Element>(FindObjectsSortMode.None).Length} elements, " +
                $"{FindObjectsByType<Collider>(FindObjectsSortMode.None).Length} colliders, " +
                $"{bodies.Length} rigidbodies ({awake} awake), {FindObjectsByType<Light>(FindObjectsSortMode.None).Length} lights, " +
                $"{FindObjectsByType<Animator>(FindObjectsSortMode.None).Length} animators");

            string text = sb.ToString();
            Debug.Log(text);
            EditorGUIUtility.systemCopyBuffer = text;
            _timers.Dispose();
            _timers = null;
            Destroy(gameObject);
        }

        void OnDestroy() => _timers?.Dispose();
    }
}
#endif
