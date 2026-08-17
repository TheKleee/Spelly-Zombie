using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The feel layer: camera shake, hit-stop, and PROCEDURAL sound - every
    /// clip is synthesized at runtime (sine/noise/sweeps), so the game has full
    /// audio with zero asset files. Replace with real SFX later; the hook
    /// points stay identical.
    public static class Juice
    {
        // ------------------------------------------------------------ shake --
        public static void Shake(float intensity, float duration = 0.35f)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var shaker = cam.GetComponent<CameraShaker>();
            if (shaker == null) shaker = cam.gameObject.AddComponent<CameraShaker>();
            shaker.Kick(intensity, duration);
        }

        // --------------------------------------------------------- hit-stop --
        public static void HitStop(float seconds = 0.18f, float scale = 0.25f)
            => JuiceRunner.Instance.DoHitStop(seconds, scale);

        // ------------------------------------------------------------ sound --
        public static void Boom(Vector3 at, float power = 1f) =>
            Play(Clip("boom", SynthBoom), at, Mathf.Clamp(0.6f + power * 0.3f, 0.4f, 1f), Random.Range(0.85f, 1.1f));
        public static void Pop(Vector3 at) =>
            Play(Clip("pop", SynthPop), at, 0.5f, Random.Range(0.85f, 1.25f));
        public static void Whoosh(Vector3 at) =>
            Play(Clip("whoosh", SynthWhoosh), at, 0.55f, Random.Range(0.9f, 1.1f));
        public static void Crackle(Vector3 at) =>
            Play(Clip("crackle", SynthCrackle), at, 0.55f, Random.Range(0.9f, 1.1f));
        public static void Thud(Vector3 at) =>
            Play(Clip("thud", SynthThud), at, 0.7f, Random.Range(0.9f, 1.05f));
        public static void Chime(Vector3 at) =>
            Play(Clip("chime", SynthChime), at, 0.6f, 1f);
        public static void Sting(Vector3 at) =>
            Play(Clip("sting", SynthSting), at, 0.7f, 1f);
        public static void Drum(Vector3 at) =>
            Play(Clip("drum", SynthDrum), at, 0.65f, 1f);
        public static void Whistle(Vector3 at) =>
            Play(Clip("whistle", SynthWhistle), at, 0.75f, Random.Range(0.95f, 1.08f));

        /// A real clip through the same 3D one-shot path the synths use.
        public static void PlayClip(AudioClip clip, Vector3 at, float volume = 0.8f, float pitch = 1f)
        {
            if (clip != null) Play(clip, at, volume, pitch);
        }

        // ------------------------------------------------------- internals --
        const int Rate = 44100;
        static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();

        static AudioClip Clip(string name, System.Func<float[]> synth)
        {
            if (_clips.TryGetValue(name, out var c) && c != null) return c;
            float[] data = synth();
            var clip = AudioClip.Create("SZ_" + name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            _clips[name] = clip;
            return clip;
        }

        static void Play(AudioClip clip, Vector3 at, float volume, float pitch)
        {
            var go = new GameObject("SFX_" + clip.name);
            go.transform.position = at;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.pitch = pitch;
            src.spatialBlend = 0.85f;      // mostly 3D, slightly present everywhere
            src.rolloffMode = AudioRolloffMode.Linear;
            src.maxDistance = 35f;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.5f, pitch) + 0.1f);
        }

        // ----------------------------------------------------- synthesizers --
        static float[] Buf(float seconds) => new float[(int)(seconds * Rate)];
        static float Env(int i, int n, float attack = 0.01f) // fast attack, exp decay
        {
            float t = i / (float)n;
            float a = Mathf.Clamp01(t / attack);
            return a * Mathf.Exp(-4.5f * t);
        }

        static float[] SynthBoom()
        {
            var d = Buf(0.6f);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                float freq = Mathf.Lerp(130f, 36f, t);          // falling low sweep
                phase += 2f * Mathf.PI * freq / Rate;
                float noise = (Random.value * 2f - 1f) * Mathf.Exp(-9f * t) * 0.5f;
                d[i] = (Mathf.Sin(phase) * 0.8f + noise) * Env(i, d.Length, 0.005f);
            }
            return d;
        }

        static float[] SynthPop()
        {
            var d = Buf(0.12f);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                phase += 2f * Mathf.PI * Mathf.Lerp(300f, 70f, t) / Rate;
                d[i] = Mathf.Sin(phase) * Env(i, d.Length, 0.003f);
            }
            return d;
        }

        static float[] SynthWhoosh()
        {
            var d = Buf(0.4f);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                float raw = Random.value * 2f - 1f;
                float cutoff = Mathf.Lerp(0.06f, 0.4f, Mathf.Sin(t * Mathf.PI)); // swells
                lp += (raw - lp) * cutoff;
                d[i] = lp * Mathf.Sin(t * Mathf.PI) * 0.9f;
            }
            return d;
        }

        static float[] SynthCrackle()
        {
            var d = Buf(0.4f);
            for (int i = 0; i < d.Length; i++)
            {
                // sparse icy ticks over a thin hiss
                float tick = Random.value < 0.004f ? (Random.value * 2f - 1f) : 0f;
                float hiss = (Random.value * 2f - 1f) * 0.06f;
                d[i] = (tick + hiss) * Env(i, d.Length, 0.02f) * 1.6f;
            }
            return d;
        }

        static float[] SynthThud()
        {
            var d = Buf(0.15f);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                phase += 2f * Mathf.PI * 85f / Rate;
                d[i] = Mathf.Sin(phase) * Env(i, d.Length, 0.004f);
            }
            return d;
        }

        static float[] SynthChime()
        {
            var d = Buf(0.5f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float s = Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.5f
                        + Mathf.Sin(2f * Mathf.PI * 1318f * t) * 0.35f; // major-ish sparkle
                d[i] = s * Env(i, d.Length, 0.01f);
            }
            return d;
        }

        static float[] SynthSting()
        {
            var d = Buf(0.7f);
            float[] notes = { 392f, 311f, 233f }; // descending - bad news
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                int n = Mathf.Min(2, (int)(t * 3f));
                float local = (t * 3f) - n;
                d[i] = Mathf.Sin(2f * Mathf.PI * notes[n] * i / Rate)
                     * Mathf.Exp(-3f * local) * 0.8f;
            }
            return d;
        }

        static float[] SynthWhistle()
        {
            var d = Buf(0.6f);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                float sec = i / (float)Rate;
                bool first = t < 0.42f;
                if (!first && t < 0.48f) continue;             // the breath between notes
                float seg = first ? t / 0.42f : (t - 0.48f) / 0.52f;
                float freq = first ? Mathf.Lerp(980f, 1480f, seg)   // rise...
                                   : Mathf.Lerp(1420f, 780f, seg);  // ...guilty fall
                freq += Mathf.Sin(2f * Mathf.PI * 5.5f * sec) * 14f; // human wobble
                phase += 2f * Mathf.PI * freq / Rate;
                d[i] = Mathf.Sin(phase) * Mathf.Sin(Mathf.Clamp01(seg) * Mathf.PI) * 0.55f;
            }
            return d;
        }

        static float[] SynthDrum()
        {
            var d = Buf(0.5f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                float hit = t < 0.5f ? t * 2f : (t - 0.5f) * 2f; // two thumps
                float freq = Mathf.Lerp(110f, 55f, hit);
                d[i] = Mathf.Sin(2f * Mathf.PI * freq * i / Rate) * Mathf.Exp(-6f * hit) * 0.85f;
            }
            return d;
        }

    }

    /// Positional camera shake - offsets the camera's LOCAL position with
    /// decaying noise, never touching rotation (the look controls own that).
    public class CameraShaker : MonoBehaviour
    {
        float _intensity, _left, _duration;
        Vector3 _basePos;
        bool _active;

        public void Kick(float intensity, float duration)
        {
            if (!_active) _basePos = transform.localPosition;
            _intensity = Mathf.Max(_intensity, intensity);
            _duration = duration;
            _left = duration;
            _active = true;
        }

        void LateUpdate()
        {
            if (!_active) return;
            _left -= Time.unscaledDeltaTime;
            if (_left <= 0f)
            {
                transform.localPosition = _basePos;
                _intensity = 0f;
                _active = false;
                return;
            }
            float falloff = _left / _duration;
            transform.localPosition = _basePos + (Vector3)(Random.insideUnitCircle * _intensity * 0.08f * falloff);
        }
    }

    /// Hit-stop host (time-scale dips need a coroutine that survives them).
    public class JuiceRunner : MonoBehaviour
    {
        static JuiceRunner _instance;
        public static JuiceRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("JuiceRunner");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<JuiceRunner>();
                }
                return _instance;
            }
        }

        float _stopLeft;
        bool _stopping;

        public void DoHitStop(float seconds, float scale)
        {
            if (_stopping) { _stopLeft = Mathf.Max(_stopLeft, seconds); return; }
            _stopping = true;
            _stopLeft = seconds;
            Time.timeScale = scale;
        }

        void Update()
        {
            if (!_stopping) return;
            _stopLeft -= Time.unscaledDeltaTime;
            if (_stopLeft <= 0f)
            {
                if (!GameMenu.IsOpen) Time.timeScale = 1f; // never un-pause the pause menu
                _stopping = false;
            }
        }
    }
}
