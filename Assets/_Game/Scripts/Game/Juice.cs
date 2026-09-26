using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The feel layer: camera shake, hit-stop, and sound. His clips come from
    /// the AudioLibrary; a moment whose slot is empty keeps the placeholder
    /// synthesized here at runtime.
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
        // Every sound in the game is one of HIS files (AudioLibrary). These names are older than his
        // library; each now asks for the file of his that says the same thing, and a moment he has no
        // file for is silent. Nothing is generated.
        /// A blast. magic = a spell letting go, not something blowing up.
        public static void Boom(Vector3 at, float power = 1f, bool magic = false)
        {
            float volume = Mathf.Clamp(0.6f + power * 0.3f, 0.4f, 1f);
            // bigger is lower
            float pitch = Mathf.Lerp(1.12f, 0.82f, Mathf.InverseLerp(0.4f, 1.5f, power)) * Random.Range(0.96f, 1.04f);
            if (magic && Sound(Sfx.MagicBurst, at, volume, pitch)) return;
            Sound(Sfx.Explosion, at, volume, pitch);
        }
        public static void Pop(Vector3 at) => Sound(Sfx.InkPop1, at, 1f, Random.Range(0.9f, 1.2f));
        public static void Whoosh(Vector3 at) => Sound(Sfx.ExpandImpact, at, 0.55f, Random.Range(0.95f, 1.1f));
        public static void Crackle(Vector3 at) => Sound(Sfx.ChillImpact, at, 0.55f, Random.Range(0.95f, 1.1f));
        public static void Thud(Vector3 at) => Sound(Sfx.ThrownObjectHitting, at, 0.7f, Random.Range(0.9f, 1.05f));
        public static void Chime(Vector3 at) => Sound(Sfx.Idea, at, 0.6f);
        public static void Sting(Vector3 at) => Sound(Sfx.UiError, at, 0.8f);
        public static void Drum(Vector3 at) => Sound(Sfx.MatchStart, at);
        public static void Whistle(Vector3 at) => Sound(Sfx.Whistle, at, 0.85f, Random.Range(0.95f, 1.05f));

        // ------------------------------------------------------- his sounds --
        /// His clip for this moment, at a place in the world. False while its
        /// slot is empty, so the caller keeps its placeholder. The host's world
        /// sounds reach the clients the way the placeholders do (FxMsg).
        /// ride: the sound travels with this body (a golem's charge) instead of staying where it began.
        public static bool Sound(Sfx id, Vector3 at, float volume = 1f, float pitch = 1f, bool relay = true,
            Transform ride = null)
        {
            var clip = ClipOf(id);
            if (clip == null) return false;
            if (relay && (int)id < AudioLibrary.WorldCount && NetSync.WantsFxRelay)
            {
                var carrier = ride != null ? ride.GetComponentInParent<Element>() : null;
                NetSync.PushFx((byte)(FxLibrary.SndClips + (int)id), at, Vector3.zero, Color.white,
                    carrier != null ? carrier.NetId : 0, pitch, 0, volume);
            }
            Emit(clip, at, volume, pitch,
                IsSmall(id) ? Shape.Small : IsNear(id) ? Shape.Near : IsMap(id) ? Shape.Map : Shape.World, ride);
            return true;
        }

        /// A clip of his that sits on one object instead of in the library (a prop's own break sound).
        public static void Clip3D(AudioClip clip, Vector3 at, float volume = 1f, float pitch = 1f)
        {
            if (clip != null) Emit(clip, at, volume, pitch, Shape.World);
        }

        /// The same, in the ears: menus, jingles, what is about you and not about a place.
        public static bool Sound2D(Sfx id, float volume = 1f, float pitch = 1f)
        {
            var clip = ClipOf(id);
            if (clip == null) return false;
            Emit(clip, Vector3.zero, volume, pitch, Shape.Flat);
            return true;
        }

        /// A spell landing, heard as its numbers: the strongest axes it carries,
        /// each with its own sound, louder and lower the more of it there is.
        /// amounts = the six axis amounts the impact look uses (0 = not carried).
        public static void SpellImpact(in SpellPayload p, float[] amounts, Vector3 at)
        {
            if (AudioLibrary.I == null) return;
            for (int n = 0; n < SpellVoices; n++)
            {
                int best = -1;
                for (int ax = 0; ax < AudioLibrary.SoundAxes; ax++)
                    if (amounts[ax] > 0f && (best < 0 || amounts[ax] > amounts[best])) best = ax;
                if (best < 0) return;
                float t = Mathf.InverseLerp(0.3f, 1.25f, amounts[best]);
                amounts[best] = 0f;
                // the second and third axes sit under the first
                Sound(AudioLibrary.ImpactOf(best, p[best] > 0f), at,
                    Mathf.Lerp(0.5f, 1f, t) * (n == 0 ? 1f : 0.7f), Mathf.Lerp(1.1f, 0.86f, t) * Random.Range(0.97f, 1.03f));
            }
        }
        const int SpellVoices = 3;

        static AudioClip ClipOf(Sfx id)
        {
            var lib = AudioLibrary.I;
            return lib != null ? lib.Clip(id) : null;
        }

        /// What the pot does is news for the whole map: it opens, ink lands in it, it changes
        /// hands. Heard from anywhere, from the pot's direction, a little louder close by.
        static bool IsMap(Sfx id) => id == Sfx.PotFromTheSky || id == Sfx.PotTurningAcolyte
            || id == Sfx.PotTurningWizard;

        /// The small sounds of being around (doors, steps, pages, pops) sit under the game:
        /// heard only close by, never across the map.
        static bool IsSmall(Sfx id) => id == Sfx.Door || id == Sfx.Chest || id == Sfx.BookClose || id == Sfx.PageFlip
            || id == Sfx.StepRock || id == Sfx.StepSnow || id == Sfx.StepWood || id == Sfx.StepGrass
            || id == Sfx.Jump || id == Sfx.Land || id == Sfx.InkPop1 || id == Sfx.InkPop2;

        /// Spell sounds, zombie voices and an acolyte changing shape travel the way voices do;
        /// everything else carries further.
        static bool IsNear(Sfx id) => (id >= Sfx.HeatImpact && id <= Sfx.RepelImpact)
            || id == Sfx.ZombieGroan || id == Sfx.ZombieGrowl || id == Sfx.ZombieAttack || id == Sfx.ZombieBite
            || id == Sfx.GolemStep || id == Sfx.GolemCroak
            || id == Sfx.AcolyteTransform || id == Sfx.AcolyteBack;

        /// The ears of this machine (the listener moves between cameras and scenes).
        public static Transform Ears()
        {
            if (_ears != null && _ears.gameObject.activeInHierarchy) return _ears;
            var l = Object.FindAnyObjectByType<AudioListener>();
            _ears = l != null ? l.transform : null;
            return _ears;
        }
        static Transform _ears;

        /// A sound the host's sim made with one of his clips (FxMsg).
        public static bool IsClipWire(byte kind) =>
            kind >= FxLibrary.SndClips && kind < FxLibrary.SndClips + AudioLibrary.WorldCount;

        /// The blasts shake the camera of whoever stands near, on every machine.
        public static bool IsBlastWire(byte kind) => kind == FxLibrary.SndBoom
            || kind == FxLibrary.SndClips + (int)Sfx.Explosion || kind == FxLibrary.SndClips + (int)Sfx.MagicBurst;

        // ------------------------------------------------------- internals --
        /// A sound the host's sim made, replayed on a client (FxMsg).
        public static void PlayWire(byte kind, Vector3 at, float volume, float pitch, Transform ride = null)
        {
            if (IsClipWire(kind))
            {
                Sound((Sfx)(kind - FxLibrary.SndClips), at, volume, pitch, false, ride);
                return;
            }
            // the ids an older host used for its placeholder sounds: the same files the names above ask for
            switch (kind)
            {
                case FxLibrary.SndBoom: Sound(Sfx.Explosion, at, volume, pitch, false); break;
                case FxLibrary.SndPop: Sound(Sfx.InkPop1, at, volume, pitch, false); break;
                case FxLibrary.SndWhoosh: Sound(Sfx.ExpandImpact, at, volume, pitch, false); break;
                case FxLibrary.SndCrackle: Sound(Sfx.ChillImpact, at, volume, pitch, false); break;
                case FxLibrary.SndThud: Sound(Sfx.ThrownObjectHitting, at, volume, pitch, false); break;
            }
        }

        // ------------------------------------------------------- the voices --
        /// How a sound sits in the world.
        enum Shape { World, Near, Small, Flat, Map }
        const float MapRange = 200f; // twice across the island: the far side still hears half

        class Voice
        {
            public AudioSource Src;
            public AudioClip Clip;
            public float Started;
            public Transform Ride;
        }

        /// Riding voices follow their body (JuiceRunner, once a frame).
        internal static void TickVoices()
        {
            if (_riding <= 0) return;
            _riding = 0;
            foreach (var v in _voices)
            {
                if (v.Ride == null || v.Src == null) continue;
                if (!v.Src.isPlaying) { v.Ride = null; continue; }
                v.Src.transform.position = v.Ride.position;
                _riding++;
            }
        }
        static int _riding;

        // voices are built once and reused; a crowd of one sound keeps its newest few
        const int MaxVoices = 40, MaxSame = 5;
        const float SameGap = 0.03f;   // the same sound twice within this is one sound
        const float CrowdWindow = 0.35f; // the same sound again within this is part of a crowd
        const float SmallRange = 16f;
        static readonly List<Voice> _voices = new List<Voice>();
        static Transform _voiceRoot;

        static void Emit(AudioClip clip, Vector3 at, float volume, float pitch, Shape shape, Transform ride = null)
        {
            // out of earshot: it takes no voice from the sounds that can be heard
            if (shape == Shape.Near || shape == Shape.Small)
            {
                var ears = Ears();
                float reach = shape == Shape.Near ? DrawingConfig.VoiceRangeMeters : SmallRange;
                if (ears != null && (ears.position - at).sqrMagnitude > reach * reach) return;
            }
            float now = Time.unscaledTime;
            Voice free = null, oldest = null, oldestSame = null;
            int same = 0, crowd = 0;
            for (int i = _voices.Count - 1; i >= 0; i--)
            {
                var v = _voices[i];
                if (v.Src == null) { _voices.RemoveAt(i); continue; }
                if (!v.Src.isPlaying) { free = v; continue; }
                if (oldest == null || v.Started < oldest.Started) oldest = v;
                if (v.Clip != clip) continue;
                if (shape != Shape.Flat && now - v.Started < SameGap
                    && (v.Src.transform.position - at).sqrMagnitude < 4f) return;
                same++;
                if (now - v.Started < CrowdWindow) crowd++;
                if (oldestSame == null || v.Started < oldestSame.Started) oldestSame = v;
            }
            // a crowd making one sound at once (five zombies rising) is louder than one, not five times as loud
            if (shape != Shape.Flat && crowd > 0) volume /= 1f + crowd * 0.7f;
            var voice = same >= MaxSame ? oldestSame
                : free != null ? free
                : _voices.Count < MaxVoices ? NewVoice()
                : oldest;
            if (voice == null) return;

            var src = voice.Src;
            src.Stop();
            src.transform.position = at;
            src.clip = clip;
            src.volume = volume * AudioOptions.Sfx;
            src.pitch = pitch;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.dopplerLevel = 0f;
            src.priority = shape == Shape.Flat ? 32 : 128; // a crowded fight never swallows a jingle
            switch (shape)
            {
                case Shape.Near:   // the proximity path voices take
                    src.spatialBlend = 1f;
                    src.minDistance = 1.5f;
                    src.maxDistance = DrawingConfig.VoiceRangeMeters;
                    src.spread = 30f;
                    break;
                case Shape.Small:  // close by only
                    src.spatialBlend = 1f;
                    src.minDistance = 1f;
                    src.maxDistance = SmallRange;
                    src.spread = 30f;
                    break;
                case Shape.Flat:
                    src.spatialBlend = 0f;
                    src.spread = 0f;
                    break;
                case Shape.Map:    // the whole map hears it, from where it happened
                    src.spatialBlend = 1f;
                    src.minDistance = 8f;
                    src.maxDistance = MapRange;
                    src.spread = 60f;
                    break;
                default:
                    src.spatialBlend = 0.85f;      // mostly 3D, slightly present everywhere
                    src.minDistance = 1f;
                    src.maxDistance = 35f;
                    src.spread = 0f;
                    break;
            }
            voice.Clip = clip;
            voice.Started = now;
            voice.Ride = ride;
            if (ride != null) { _riding++; _ = JuiceRunner.Instance; }
            src.Play();
        }

        static Voice NewVoice()
        {
            if (_voiceRoot == null)
            {
                var root = new GameObject("~SfxVoices");
                Object.DontDestroyOnLoad(root);
                _voiceRoot = root.transform;
            }
            var go = new GameObject("Voice");
            go.transform.SetParent(_voiceRoot, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            var voice = new Voice { Src = src };
            _voices.Add(voice);
            return voice;
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
            Juice.TickVoices();
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
