#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace SpellyZombie
{
    /// Editor-only load test (his ask, Sep 18): three 20 s stages of meteors,
    /// boss summons and breaking shields (rubble that stands up as golems),
    /// 30 m in front of the player, through the game's own cast and matter
    /// paths. Prints the frame rate, where the frame's time goes (the engine's
    /// timers and PerfMarkers) and what is alive every second, then a summary
    /// per stage, also copied to the clipboard. Play mode only; the local
    /// player is kept standing so the round does not end mid-test.
    public class StressTest : MonoBehaviour
    {
        struct Stage { public string Name; public float Meteors, Summons, Rubble; } // per second
        static readonly Stage[] Stages =
        {
            new Stage { Name = "boss last phase", Meteors = 1f, Summons = 0.4f, Rubble = 0.5f },
            new Stage { Name = "full lobby fight", Meteors = 5f, Summons = 0.4f, Rubble = 1f },
            new Stage { Name = "past the limits", Meteors = 10f, Summons = 1f, Rubble = 2f },
        };
        const float StageSeconds = 20f;
        const float Ahead = 30f, Spread = 10f;
        const string MeteorSpell = "Meteor", SummonSpell = "Solid Golem";

        int _stage;
        float _stageT, _secT, _meteorDue, _summonDue, _rubbleDue;
        int _frames, _gc0;
        float _worst;
        Vector3 _center;
        readonly StringBuilder _summary = new StringBuilder();
        int _stageFrames, _peakSpells, _peakGolems, _peakZombies, _peakRubble;
        float _stageTime, _stageWorst;
        FrameTimers _timers;
        int _steps; // physics steps since the last report

        [MenuItem("Spelly Zombie/Test/Stress Test (Play mode)")]
        static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Stress] Enter Play mode on a map first."); return; }
            if (FindAnyObjectByType<StressTest>() != null) { Debug.LogWarning("[Stress] Already running."); return; }
            new GameObject("~StressTest").AddComponent<StressTest>();
        }

        void Start()
        {
            var cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.up * 2f;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            _center = Ground(eye + fwd.normalized * Ahead);
            _gc0 = System.GC.CollectionCount(0);
            _timers = new FrameTimers();
            if (SpellBook.Live.Spell(MeteorSpell) == null) Debug.LogWarning($"[Stress] The book has no '{MeteorSpell}' spell: no meteors.");
            if (SpellBook.Live.Spell(SummonSpell) == null) Debug.LogWarning($"[Stress] The book has no '{SummonSpell}' spell: no summons.");
            Debug.Log($"[Stress] start: {Stages.Length} stages of {StageSeconds:0} s, {Ahead:0} m in front of you");
            BeginStage();
        }

        void BeginStage()
        {
            _stageT = _stageTime = _stageWorst = 0f;
            _stageFrames = _peakSpells = _peakGolems = _peakZombies = _peakRubble = 0;
            Debug.Log($"[Stress] stage {_stage + 1}: {Stages[_stage].Name}");
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var s = Stages[_stage];
            _frames++;
            _stageFrames++;
            _stageTime += dt;
            _worst = Mathf.Max(_worst, dt);
            _stageWorst = Mathf.Max(_stageWorst, dt);
            _timers?.Sample();

            _meteorDue += s.Meteors * Time.deltaTime;
            while (_meteorDue >= 1f) { _meteorDue -= 1f; Meteor(); }
            _summonDue += s.Summons * Time.deltaTime;
            while (_summonDue >= 1f) { _summonDue -= 1f; Summon(); }
            _rubbleDue += s.Rubble * Time.deltaTime;
            while (_rubbleDue >= 1f) { _rubbleDue -= 1f; Rubble(); }

            _peakSpells = Mathf.Max(_peakSpells, SpellParticle.Living.Count);
            _peakGolems = Mathf.Max(_peakGolems, Golem.All.Count);
            _peakZombies = Mathf.Max(_peakZombies, Zombie.All.Count);
            _peakRubble = Mathf.Max(_peakRubble, Matter.Living.Count);

            _secT += dt;
            if (_secT >= 1f) Report();

            _stageT += dt;
            if (_stageT >= StageSeconds) EndStage();
        }

        void FixedUpdate() => _steps++;

        void OnDestroy() => _timers?.Dispose();

        // the test must not end the round
        void LateUpdate()
        {
            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || !p.IsLocalViewer || p.IsDowned) continue;
                var el = p.GetComponent<Element>();
                if (el != null && el.Health < el.MaxStrength) el.Health = el.MaxStrength;
            }
        }

        void Meteor()
        {
            Vector2 r = Random.insideUnitCircle * Spread;
            Vector3 target = Ground(_center + new Vector3(r.x, 0f, r.y));
            Vector3 from = target + new Vector3(Random.Range(-4f, 4f), 12f, Random.Range(-4f, 4f));
            CreatureCasts.Cast(MeteorSpell, -1, from, (target - from).normalized, from, 1f);
        }

        void Summon()
        {
            Vector2 d = Random.insideUnitCircle.normalized;
            Vector3 aim = new Vector3(d.x, 0f, d.y);
            CreatureCasts.Cast(SummonSpell, -1, _center + Vector3.up * 2f, aim, _center, 3f);
        }

        // a broken shield: three chunks that merge and stand up as a golem
        void Rubble()
        {
            Vector2 r = Random.insideUnitCircle * Spread;
            Vector3 at = Ground(_center + new Vector3(r.x, 0f, r.y)) + Vector3.up;
            for (int i = 0; i < 3; i++)
                Matter.Spawn(SurfaceMaterialType.Stone, MatterPhase.Solid, 0.15f, at + Random.insideUnitSphere * 0.12f);
        }

        void Report()
        {
            int gc = System.GC.CollectionCount(0);
            Debug.Log($"[Stress] {Stages[_stage].Name} {_stageT:0}s | {Mathf.RoundToInt(_frames / _secT)} fps, " +
                $"worst frame {_worst * 1000f:0} ms, {(float)_steps / Mathf.Max(1, _frames):0.0} physics steps a frame | " +
                $"ms a frame: scripts {Ms("update")} (element turns {Ms("turns")}), fixed {Ms("fixed")} " +
                $"(golem brains {Ms("brains")}), physics {Ms("physics")}, collision hits {Ms("impacts")}, " +
                $"golem births {Ms("births")}, console lines {Ms("logs")}, late {Ms("late")}, " +
                $"animation {Ms("animation")}, rendering {Ms("render")}, GPU wait {Ms("gpu")} | " +
                $"spells {SpellParticle.Living.Count}/{DrawingConfig.ParticleCap}, " +
                $"golems {Golem.All.Count}, zombies {Zombie.All.Count}, rubble {Matter.Living.Count} | " +
                $"batches {UnityStats.batches} | GC {gc - _gc0}x, heap {Profiler.GetMonoUsedSizeLong() / 1048576} MB");
            _gc0 = gc;
            _secT = 0f;
            _frames = 0;
            _worst = 0f;
            _steps = 0;
            _timers?.Clear();
        }

        string Ms(string key)
        {
            double v = _timers != null ? _timers.Avg(key, _frames) : -1;
            return v < 0 ? "-" : v.ToString("0.0");
        }

        void EndStage()
        {
            var s = Stages[_stage];
            float avg = _stageFrames / Mathf.Max(0.001f, _stageTime);
            _summary.AppendLine($"{s.Name}: avg {avg:0} fps, worst frame {_stageWorst * 1000f:0} ms, peak spells {_peakSpells}, " +
                $"golems {_peakGolems}, zombies {_peakZombies}, rubble {_peakRubble}");
            _stage++;
            if (_stage < Stages.Length) { BeginStage(); return; }
            string text = "[Stress] DONE (copied to the clipboard)\n" + _summary;
            Debug.Log(text);
            EditorGUIUtility.systemCopyBuffer = text;
            Destroy(gameObject);
        }

        static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out var hit, 200f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point;
            return p;
        }
    }
}
#endif
