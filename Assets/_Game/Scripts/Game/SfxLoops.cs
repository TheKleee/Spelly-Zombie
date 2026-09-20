using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The sounds that keep going: the pencil and the eraser while ink is laid
    /// or rubbed out, the pot bubbling while it holds ink, and the hum of the
    /// spell areas near the listener (one voice per axis direction, louder the
    /// closer you stand, full inside). Every machine works out its own from
    /// what it already sees, so nothing crosses the wire.
    public class SfxLoops : MonoBehaviour
    {
        const float ScratchHold = 0.16f;    // ink laid this long ago still counts as drawing
        const float ScratchReach = 3f;      // pings this close together are one hand
        const int MaxScratches = 6;
        const float PotRange = 16f;
        const float AreaLevel = 0.85f;
        const float ScanEvery = 0.2f;

        static SfxLoops _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_instance != null) return;
            var go = new GameObject("~SfxLoops");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SfxLoops>();
        }

        // ------------------------------------------------ pencil and eraser --
        class Scratch
        {
            public AudioSource Src;
            public bool Rub;
            public float Until, Level;
        }

        readonly List<Scratch> _scratches = new List<Scratch>();

        /// Ink was just laid here: by the local pen, a friend's growing line, or the book.
        public static void Pen(Vector3 at) { if (_instance != null) _instance.Ping(false, at); }

        /// Ink was just rubbed out here.
        public static void Rub(Vector3 at) { if (_instance != null) _instance.Ping(true, at); }

        void Ping(bool rub, Vector3 at)
        {
            var lib = AudioLibrary.I;
            var clip = lib == null ? null : rub ? lib.ErasingLoop : lib.PencilLoop;
            if (clip == null) return;

            Scratch mine = null, idle = null;
            foreach (var s in _scratches)
            {
                if (s.Rub == rub && s.Level > 0f
                    && (s.Src.transform.position - at).sqrMagnitude < ScratchReach * ScratchReach) { mine = s; break; }
                if (s.Level <= 0f && Time.unscaledTime > s.Until) idle = s;
            }
            if (mine == null)
            {
                mine = idle;
                if (mine == null)
                {
                    if (_scratches.Count >= MaxScratches) return;
                    mine = new Scratch { Src = Loop("Scratch", DrawingConfig.VoiceRangeMeters, 1.5f) };
                    _scratches.Add(mine);
                }
                mine.Rub = rub;
                mine.Src.clip = clip;
                mine.Src.pitch = Random.Range(0.95f, 1.06f);
                mine.Src.time = Random.value * clip.length * 0.9f;
            }
            mine.Src.transform.position = at;
            mine.Until = Time.unscaledTime + ScratchHold;
        }

        void TickScratches(float dt)
        {
            foreach (var s in _scratches)
            {
                bool on = Time.unscaledTime <= s.Until;
                s.Level = Mathf.MoveTowards(s.Level, on ? 1f : 0f, dt / (on ? 0.04f : 0.14f));
                Drive(s.Src, s.Level);
            }
        }

        // -------------------------------------------------------- the pot --
        AudioSource _pot;
        float _potLevel;

        void TickPot(float dt)
        {
            var lib = AudioLibrary.I;
            var pot = CauldronEconomy.Active;
            bool on = lib != null && lib.BubblesLoop != null && pot != null
                && CauldronEconomy.InkShows && CauldronEconomy.Fill01 > 0.01f
                && !MapCreator.Active && !PhotoBooth.Active;
            if (on)
            {
                if (_pot == null) _pot = Loop("PotBubbles", PotRange, 1.5f);
                if (_pot.clip != lib.BubblesLoop) _pot.clip = lib.BubblesLoop;
                _pot.transform.position = pot.transform.position;
            }
            // a fuller pot bubbles louder
            float want = on ? Mathf.Lerp(0.45f, 1f, CauldronEconomy.Fill01) : 0f;
            _potLevel = Mathf.MoveTowards(_potLevel, want, dt / 0.6f);
            if (_pot != null) Drive(_pot, _potLevel);
        }

        // ------------------------------------------------- the spell areas --
        // one voice per axis direction: it sits on the area of that kind heard best from here
        class Hum
        {
            public AudioSource Src;
            public float Level, Want, Pitch = 1f, Radius;
            public Vector3 At;
            public float Best;
        }

        readonly Hum[] _hums = new Hum[AudioLibrary.SoundAxes * 2];
        float _scanIn;

        void TickAreas(float dt)
        {
            _scanIn -= dt;
            if (_scanIn <= 0f)
            {
                _scanIn = ScanEvery;
                ScanAreas();
            }
            for (int i = 0; i < _hums.Length; i++)
            {
                var h = _hums[i];
                if (h == null) continue;
                h.Level = Mathf.MoveTowards(h.Level, h.Want, dt / (h.Want > h.Level ? 0.35f : 0.6f));
                if (h.Level > 0f)
                {
                    // a voice already sounding glides to the next area instead of jumping there
                    h.Src.transform.position = h.Src.isPlaying
                        ? Vector3.MoveTowards(h.Src.transform.position, h.At, dt * 30f) : h.At;
                    h.Src.minDistance = Mathf.Max(1.5f, h.Radius);
                    h.Src.maxDistance = Mathf.Max(1.5f, h.Radius) + DrawingConfig.VoiceRangeMeters;
                    h.Src.pitch = Mathf.MoveTowards(h.Src.pitch, h.Pitch, dt * 0.5f);
                }
                Drive(h.Src, h.Level * AreaLevel);
            }
        }

        void ScanAreas()
        {
            for (int i = 0; i < _hums.Length; i++)
                if (_hums[i] != null) { _hums[i].Want = 0f; _hums[i].Best = 0f; }
            var lib = AudioLibrary.I;
            var ears = Juice.Ears();
            if (lib == null || ears == null) return;
            Vector3 here = ears.position;

            var places = ArtificialBiome.Living;
            for (int i = 0; i < places.Count; i++)
                if (places[i] != null) Hear(lib, here, places[i].transform.position, places[i].Radius, places[i].Offsets, -1);
            // the host's lvl3 particles are places too (a client holds a quiet ArtificialBiome for each)
            var motes = SpellParticle.Biomes;
            for (int i = 0; i < motes.Count; i++)
            {
                var m = motes[i];
                if (m == null || m.Dead) continue;
                Hear(lib, here, m.transform.position, m.AuraRadius, m.PayloadNow, i);
            }
        }

        /// One area, heard from here: each axis it holds bids for that axis's voice.
        void Hear(AudioLibrary lib, Vector3 here, Vector3 at, float radius, in SpellPayload p, int mote)
        {
            float reach = radius + DrawingConfig.VoiceRangeMeters;
            float d = Vector3.Distance(here, at);
            if (d >= reach) return;
            float near = d <= radius ? 1f : 1f - (d - radius) / DrawingConfig.VoiceRangeMeters;
            for (int ax = 0; ax < AudioLibrary.SoundAxes; ax++)
            {
                if (mote >= 0 && !SpellParticle.Biomes[mote].BiomeOn(ax)) continue;
                float u = Mathf.Abs(p.Unit(ax));
                if (u < 0.15f) continue;   // the same floor the area looks use
                bool up = p[ax] > 0f;
                var clip = lib.AreaOf(ax, up);
                if (clip == null) continue;
                float t = Mathf.InverseLerp(0.15f, 1f, u);
                float heard = Mathf.Lerp(0.4f, 1f, t) * near;

                int slot = ax * 2 + (up ? 0 : 1);
                var h = _hums[slot];
                if (h == null)
                {
                    h = _hums[slot] = new Hum { Src = Loop("AreaHum", DrawingConfig.VoiceRangeMeters, 1.5f) };
                    h.Src.clip = clip;
                    h.Src.time = Random.value * clip.length * 0.9f;
                    h.Src.transform.position = at;
                }
                else if (h.Src.clip != clip) h.Src.clip = clip;
                // more areas of one kind make it a little louder, never a wall of it
                h.Want = Mathf.Min(1f, Mathf.Max(h.Want, Mathf.Lerp(0.4f, 1f, t)) + (h.Best > 0f ? 0.08f : 0f));
                if (heard <= h.Best) continue;
                h.Best = heard;
                h.At = at;
                h.Radius = radius;
                h.Pitch = Mathf.Lerp(1.06f, 0.9f, t);   // more of it is lower
            }
        }

        // ---------------------------------------------------------- shared --
        AudioSource Loop(string label, float range, float full)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.volume = 0f;
            src.spatialBlend = 1f;      // the proximity path voices take
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = full;
            src.maxDistance = range;
            src.dopplerLevel = 0f;
            src.spread = 30f;
            return src;
        }

        /// A loop plays only while it can be heard; silent ones cost nothing.
        static void Drive(AudioSource src, float level)
        {
            if (src == null) return;
            if (level <= 0.001f)
            {
                if (src.isPlaying) src.Stop();
                return;
            }
            src.volume = level * AudioOptions.Sfx;
            if (!src.isPlaying && src.clip != null) src.Play();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            TickScratches(dt);
            TickPot(dt);
            TickAreas(dt);
        }
    }

    /// Steps, jumps and landings of one body, worked out from how it moves: the
    /// local player and every puppet run the same one, so everyone is heard
    /// the same way and nothing crosses the wire.
    public class Footfalls
    {
        Vector3 _last;
        bool _has, _wasAir;
        float _walked, _airFor;

        /// feet = where the body stands. quiet = no steps at all (a ghost, a body on the floor).
        public void Tick(Vector3 feet, bool airborne, bool quiet, bool crouched, bool sprinting, float dt)
        {
            if (!_has || quiet)
            {
                _has = !quiet;
                _last = feet;
                _wasAir = airborne;
                _walked = 0f;
                _airFor = 0f;
                return;
            }
            Vector3 moved = feet - _last;
            _last = feet;
            // a teleport or a respawn is not a stride
            if (moved.sqrMagnitude > 9f) { _walked = 0f; _wasAir = airborne; return; }

            if (airborne)
            {
                // leaving the ground upward is a jump; walking off a ledge is not
                if (!_wasAir && moved.y > 0.02f) Juice.Sound(Sfx.Jump, feet, crouched ? 0.5f : 0.8f, Random.Range(0.94f, 1.06f));
                _airFor += dt;
            }
            else
            {
                if (_wasAir && _airFor > 0.25f)
                {
                    Juice.Sound(Sfx.Land, feet, Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(0.25f, 1.2f, _airFor)),
                        Random.Range(0.94f, 1.06f));
                    _walked = 0f;
                }
                _airFor = 0f;
                moved.y = 0f;
                _walked += moved.magnitude;
                float stride = crouched ? 1.2f : sprinting ? 2.1f : 1.7f;
                if (_walked >= stride)
                {
                    _walked = 0f;
                    Juice.Sound(Surface(feet), feet, crouched ? 0.35f : sprinting ? 0.9f : 0.65f, Random.Range(0.9f, 1.1f));
                }
            }
            _wasAir = airborne;
        }

        /// A heavy body (a golem): no surface, one stomp a stride, all of it by the body's size.
        public void TickHeavy(Vector3 feet, float size, bool quiet, float dt)
        {
            if (!_has || quiet || dt <= 0f)
            {
                _has = !quiet;
                _last = feet;
                _walked = 0f;
                return;
            }
            Vector3 moved = feet - _last;
            _last = feet;
            // thrown, falling or carried: not walking
            if (moved.sqrMagnitude > 9f || Mathf.Abs(moved.y) / dt > 2f) { _walked = 0f; return; }
            moved.y = 0f;
            // faster than any walk is a charge, and the charge has a sound of its own
            if (moved.magnitude / dt > 6f) { _walked = 0f; return; }
            _walked += moved.magnitude;
            if (_walked < Mathf.Clamp(size * 1.1f, 0.9f, 4f)) return;
            _walked = 0f;
            float big = Mathf.InverseLerp(0.5f, 3f, size);
            Juice.Sound(Sfx.GolemStep, feet, Mathf.Lerp(0.4f, 1f, big), Mathf.Lerp(1.25f, 0.72f, big) * Random.Range(0.95f, 1.05f));
        }

        /// What is underfoot: wood by its tag, the ground as snow where the biome
        /// is below zero and grass elsewhere, anything else the plain (rock) step.
        static Sfx Surface(Vector3 feet)
        {
            if (!Physics.Raycast(feet + Vector3.up * 0.4f, Vector3.down, out var hit, 1.4f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return Sfx.StepRock;
            var tag = hit.collider.GetComponentInParent<SurfaceMaterialTag>();
            if (tag != null) return tag.Material == SurfaceMaterialType.Wood ? Sfx.StepWood : Sfx.StepRock;
            if (hit.collider is TerrainCollider)
                return Biome.CompositeAt(hit.point).Temp < 0f ? Sfx.StepSnow : Sfx.StepGrass;
            return Sfx.StepRock;
        }
    }
}
