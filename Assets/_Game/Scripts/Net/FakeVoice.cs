#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Editor-only stand-in for a microphone (his ask: no working mic to test
    /// proximity voice). "Me" feeds speech-like sound through the real mic
    /// path, so the level meter, the send gate, your eyes and what the others
    /// hear from your avatar all react as to a voice. "A talker in front of
    /// me" plays a second fake voice from a marker 5 m ahead through the same
    /// 3D speaker and packing remote voices use: walk away and hear it fade.
    public static class FakeVoice
    {
        const string MePref = "sz_fake_voice_me";
        const string MeMenu = "Spelly Zombie/Test/Fake Voice/Me (instead of the mic)";
        const string TalkerMenu = "Spelly Zombie/Test/Fake Voice/A Talker In Front Of Me";

        static bool _me;
        static SpeechSynth _mine;

        /// My player talks with the fake voice instead of the mic.
        public static bool Me => _me;

        [InitializeOnLoadMethod]
        static void Load()
        {
            _me = EditorPrefs.GetBool(MePref, false);
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredPlayMode && _me) VoiceChat.Touch();
            };
        }

        public static void Fill(float[] buf, int count, int rate)
        {
            _mine ??= new SpeechSynth(1, 140f);
            _mine.Fill(buf, count, rate);
        }

        [MenuItem(MeMenu)]
        static void ToggleMe()
        {
            _me = !_me;
            EditorPrefs.SetBool(MePref, _me);
            if (_me && Application.isPlaying) VoiceChat.Touch();
            Debug.Log(_me ? "[Voice] Fake voice on: your player talks without a mic (the mic mode still applies)."
                          : "[Voice] Fake voice off: the mic is back.");
        }

        [MenuItem(MeMenu, true)]
        static bool ToggleMeCheck()
        {
            Menu.SetChecked(MeMenu, _me);
            return true;
        }

        [MenuItem(TalkerMenu)]
        static void ToggleTalker()
        {
            var live = Object.FindAnyObjectByType<FakeTalker>();
            if (live != null) { Object.Destroy(live.gameObject); return; }
            if (!Application.isPlaying) { Debug.LogWarning("[Voice] Enter Play mode first."); return; }
            var cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.up * 1.6f;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "~FakeTalker";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * 0.3f;
            go.transform.position = eye + fwd.normalized * 5f;
            go.AddComponent<FakeTalker>();
            Debug.Log($"[Voice] A fake talker speaks 5 m in front of you; silent past {DrawingConfig.VoiceRangeMeters:0} m.");
        }

        [MenuItem(TalkerMenu, true)]
        static bool ToggleTalkerCheck()
        {
            Menu.SetChecked(TalkerMenu, Application.isPlaying && Object.FindAnyObjectByType<FakeTalker>() != null);
            return true;
        }
    }

    /// The talker: another fake voice in 20 ms frames, packed and unpacked the
    /// way a remote voice arrives, played by the game's own VoiceSpeaker.
    public class FakeTalker : MonoBehaviour
    {
        readonly SpeechSynth _voice = new SpeechSynth(7, 210f);
        readonly float[] _frame = new float[320];
        VoiceSpeaker _speaker;
        float _due;

        void Start()
        {
            _speaker = gameObject.AddComponent<VoiceSpeaker>();
            _speaker.Setup(VoiceChat.Rate);
        }

        void Update()
        {
            _due += Time.unscaledDeltaTime * VoiceChat.Rate;
            while (_due >= _frame.Length)
            {
                _due -= _frame.Length;
                _voice.Fill(_frame, _frame.Length, VoiceChat.Rate);
                var pcm = Adpcm.Decode(Adpcm.Encode(_frame, _frame.Length), out int n);
                if (n > 0) _speaker.Push(pcm, n);
            }
        }
    }

    /// Speech-like sound: phrases of syllables with a wandering pitch and
    /// changing vowels, pauses between, now and then a shout.
    public class SpeechSynth
    {
        readonly System.Random _r;
        readonly float _base;
        float _left, _t, _len, _phase, _f0, _glide, _formant, _hiss, _loud;
        int _syllables;
        bool _voiced;

        public SpeechSynth(int seed, float basePitch)
        {
            _r = new System.Random(seed);
            _base = basePitch;
            _left = 0.5f;
        }

        float Range(float lo, float hi) => lo + (float)_r.NextDouble() * (hi - lo);

        public void Fill(float[] buf, int count, int rate)
        {
            float dt = 1f / rate;
            for (int i = 0; i < count; i++)
            {
                _left -= dt;
                if (_left <= 0f) Next();
                float s = 0f;
                if (_voiced)
                {
                    _t += dt;
                    float env = Mathf.Min(1f, _t / 0.015f) * Mathf.Clamp01((_len - _t) / 0.04f);
                    _f0 = Mathf.Max(60f, _f0 + _glide * dt);
                    _phase += _f0 * dt;
                    if (_phase > 1f) _phase -= 1f;
                    float v = 0f;
                    for (int h = 1; h <= 8; h++)
                    {
                        float d = (h * _f0 - _formant) / 250f;
                        v += Mathf.Sin(2f * Mathf.PI * _phase * h) * (0.6f / h + Mathf.Exp(-d * d) * 0.5f);
                    }
                    float noise = Range(-1f, 1f);
                    s = (v * (1f - _hiss) + noise * _hiss) * env * _loud;
                }
                buf[i] = Mathf.Clamp(s, -1f, 1f);
            }
        }

        void Next()
        {
            if (_voiced)
            {
                // a syllable ended: a short gap, or the phrase is over and a pause follows
                _voiced = false;
                _left = --_syllables > 0 ? Range(0.02f, 0.09f) : Range(0.4f, 2f);
                return;
            }
            if (_syllables <= 0)
            {
                // a new phrase: its own pitch and loudness, sometimes a shout
                _syllables = _r.Next(3, 10);
                _f0 = _base * Range(0.8f, 1.2f);
                _loud = _r.NextDouble() < 0.15 ? Range(0.35f, 0.5f) : Range(0.04f, 0.25f);
            }
            _voiced = true;
            _t = 0f;
            _len = _left = Range(0.08f, 0.26f);
            _f0 *= Range(0.9f, 1.1f);
            _glide = Range(-40f, 40f);
            _formant = Range(450f, 900f);
            _hiss = Range(0f, 0.4f);
        }
    }
}
#endif
