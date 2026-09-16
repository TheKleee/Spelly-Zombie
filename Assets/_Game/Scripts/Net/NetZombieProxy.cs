using UnityEngine;

namespace SpellyZombie
{
    /// Two snapshot samples rendered a little behind, so a stand-in glides
    /// between them instead of chasing the newest one; the host's velocity
    /// carries it on when the wire goes quiet. Zombie and golem stand-ins share it.
    public class CreatureGlide
    {
        const float Delay = 0.15f, MaxExtrapolate = 0.3f;
        struct Beat { public float T; public Vector3 Pos; public Quaternion Rot; }
        Beat _a, _b;
        int _count;

        /// The host's rigidbody velocity on the last beat.
        public Vector3 Velocity { get; private set; }

        public void Push(Vector3 pos, Quaternion rot, Vector3 vel)
        {
            _a = _b;
            _b = new Beat { T = Time.time, Pos = pos, Rot = rot };
            Velocity = vel;
            if (_count < 2) _count++;
        }

        public bool Sample(out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            if (_count == 0) return false;
            float t = Time.time - Delay;
            if (_count == 1 || t >= _b.T)
            {
                float ahead = Mathf.Clamp(t - _b.T, 0f, MaxExtrapolate);
                pos = _b.Pos + Velocity * ahead;
                rot = _b.Rot;
                return true;
            }
            float span = _b.T - _a.T;
            float k = span > 0.0001f ? Mathf.Clamp01((t - _a.T) / span) : 1f;
            pos = Vector3.Lerp(_a.Pos, _b.Pos, k);
            rot = Quaternion.Slerp(_a.Rot, _b.Rot, k);
            return true;
        }
    }

    /// Client-side stand-in for a host-simulated zombie - no brain, glides to snapshots, valid spell target, damage relayed to the host; vanishes when snapshots stop listing it.
    public class NetZombieProxy : MonoBehaviour
    {
        /// Every live stand-in, for the things that count bodies (doors).
        public static readonly System.Collections.Generic.List<NetZombieProxy> All
            = new System.Collections.Generic.List<NetZombieProxy>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        readonly CreatureGlide _glide = new CreatureGlide();
        public int Id;             // the host's instance id, the snapshot key
        public int OwnerId = -1;   // the acolyte who drew it, from the snapshot

        /// A ghost drives it on the host: eyes hidden, the aura stops puffing.
        public bool Possessed { get; private set; }

        /// Where a rider's camera sits: the eyes, else the top of the body.
        public Vector3 HeadAt
        {
            get
            {
                var eyes = GetComponentInChildren<GooglyEyes>(true);
                return eyes != null ? eyes.transform.position
                    : transform.position + Vector3.up * (transform.localScale.y * 0.95f);
            }
        }

        /// The rider sits inside the head: the eyes get out of the lens.
        public void ShowEyes(bool on)
        {
            var eyes = GetComponentInChildren<GooglyEyes>(true);
            if (eyes == null) return;
            foreach (var r in eyes.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        }

        public static NetZombieProxy Build(int id, Vector3 pos, Vector3 scale, bool ranged = false,
            bool demon = false)
        {
            // the same two colours SummonedZombie paints on the host, so a
            // ranged zombie reads as ranged on every screen
            Color skin = ranged ? DrawingConfig.SummonRangedColor : DrawingConfig.SummonMeleeColor;
            GameObject go;

            // THE SAME BODY THE HOST RAISED. Clients used to get a capsule with
            // a cube head, so a friend's screen showed a different creature
            // than yours - and there was no mesh for their pen to land on.
            var custom = CollectionManager.ZombieBody;
            if (custom != null)
            {
                go = Instantiate(custom, pos, Quaternion.identity);
                go.name = "NetZombie";
                // the host's own scale, so kind shape AND summon size match
                go.transform.localScale = scale.sqrMagnitude > 0.0001f
                    ? scale : Zombie.BodyScale;

                // you can draw on a remote zombie too: same shell as the host's
                ZombieDress.AttachPaintShell(go.GetComponentInChildren<SkinnedMeshRenderer>(true));
                if (go.GetComponentInChildren<Collider>(true) == null) Zombie.FitCollider(go);

                // an authored body brings its own eyes; a second pair is the
                // baked-prefab trap
                if (go.GetComponentInChildren<GooglyEyes>(true) == null)
                {
                    var face = FindHead(go.transform) ?? go.transform;
                    GooglyEyes.Attach(face, 0f, DrawingConfig.ZombieEyeScale);
                }

                // the host's controller drives the same clips here
                var anim = go.GetComponentInChildren<Animator>(true);
                if (anim != null)
                {
                    if (anim.runtimeAnimatorController == null && CharacterLibrary.ZombieAnim != null)
                        anim.runtimeAnimatorController = CharacterLibrary.ZombieAnim;
                    anim.applyRootMotion = false;
                }
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "NetZombie";
                go.transform.position = pos;
                go.transform.localScale = Zombie.BodyScale; // one body - host/client looks can't drift
                go.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(skin, MoteShade.Opaque);

                var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
                head.name = "Head";
                Destroy(head.GetComponent<Collider>());
                head.transform.SetParent(go.transform, false);
                head.transform.localPosition = new Vector3(0f, 1.05f, 0.05f);
                head.transform.localScale = new Vector3(0.55f, 0.4f, 0.55f);
                head.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(skin * 1.15f, MoteShade.Opaque);
                GooglyEyes.Attach(head.transform, 0f, DrawingConfig.ZombieEyeScale);
            }

            // kinematic body: snapshots own the position, but particle triggers
            // still fire against it and heat/impacts still land
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            go.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Flesh;
            go.AddComponent<PersistentInkSurface>(); // you can still doodle on them

            var dmg = go.AddComponent<Element>();
            dmg.Rename(id);            // the HOST's name for it, so hits find it
            dmg.Health = 100000f;      // the HOST owns real health
            dmg.RemoveOnDeath = false;  // never dies locally - snapshots decide

            // the weight you can see: StateMsg feeds the burden, the sag reads it
            if (go.GetComponent<WeightSag>() == null) go.AddComponent<WeightSag>();

            // horns before the view scans, so they take the demon's tint too
            if (demon) Demon.AddHorns(go.transform);

            // the host's colour and spell skin, through the same StateView
            // SummonedZombie.Paint writes on the host (eyes stay eyes)
            var view = go.GetComponent<StateView>();
            if (view == null) view = go.AddComponent<StateView>();
            view.DriveTint = true;
            view.Tint = skin;
            view.Rescan();
            view.PushNow();

            var proxy = go.AddComponent<NetZombieProxy>();
            proxy.Id = id;
            proxy._glide.Push(pos, Quaternion.identity, Vector3.zero);
            proxy.transform.position = pos;
            proxy._view = view;
            proxy._anim = go.GetComponentInChildren<Animator>(true);
            proxy._eyes = go.GetComponentInChildren<GooglyEyes>(true);
            // the body as Demon gathers it on the host: taken now, before auras
            // and ice shells hang off it (eyes stay eyes)
            var skinRends = new System.Collections.Generic.List<Renderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (r != null && r.GetComponentInParent<GooglyEyes>() == null) skinRends.Add(r);
            proxy._skin = skinRends.ToArray();
            return proxy;
        }

        Renderer[] _skin;

        StateView _view;
        Color32 _tint;
        byte _look = 254;
        SpellDef _worn;

        /// The tint, the spell look and the phase the snapshot carries; a
        /// change (demon form, biome drift) re-paints in place.
        public void Wear(Color32 tint, byte look, float stateT)
        {
            if (_view == null) return;
            bool moved = false;
            if (!tint.Equals(_tint)) { _tint = tint; _view.Tint = tint; moved = true; }
            if (look != _look)
            {
                _look = look;
                _worn = SpellBook.Live.At(look);
                _view.Look = _worn != null ? _worn.Skin : null;
                moved = true;
            }
            _view.StateT = stateT;
            if (moved) _view.PushNow();
        }

        public void Target(Vector3 pos, Quaternion rot, Vector3 vel) => _glide.Push(pos, rot, vel);

        // ---- animation: the host's one-shots, the stride from its velocity ----
        Animator _anim;

        public void PlayAnim(byte bits)
        {
            if (bits == 0 || _tranced) return;
            var scream = _worn != null ? _worn.MoveClip(Zombie.Charge) : null;
            ZombieDress.PlayWire(gameObject, _anim, bits, scream);
        }

        // ---- eyes: mood, red pupils, the charge swell ----
        GooglyEyes _eyes;
        bool _moodHeld, _swelling;
        Color? _pupilTint;

        public void SetEyes(byte bits)
        {
            if (_eyes == null) return;
            var mood = (EyeMood)(bits & 7);
            // a held mood is re-armed every beat so AutoWatch does not take
            // over; a neutral host lets the local AutoWatch look around
            if (mood != EyeMood.Neutral) { _eyes.SetMood(mood, 0.3f); _moodHeld = true; }
            else if (_moodHeld) { _eyes.SetMood(EyeMood.Neutral, 0f); _moodHeld = false; }

            // pupil colour: red = ridden, yellow = aggressive, purple = spreading (his tells)
            Color? want = (bits & 8) != 0 ? DrawingConfig.MindControlEyeColor
                : (bits & 32) != 0 ? DrawingConfig.AggressiveEyeColor
                : (bits & 64) != 0 ? DrawingConfig.SpreadingEyeColor : (Color?)null;
            if (want != _pupilTint)
            {
                _pupilTint = want;
                _eyes.SetPupilTint(want);
            }
            bool swell = (bits & 16) != 0;
            if (swell && !_swelling)
                _eyes.Swell(DrawingConfig.ChargeTellSeconds, DrawingConfig.ChargeTellEyeSwell);
            _swelling = swell;
        }

        /// The host's pupils aimed at prey; zero lets the local AutoWatch wander.
        public void SetGaze(Vector3 gaze)
        {
            if (_eyes == null || gaze == Vector3.zero) return;
            _eyes.LookTarget = gaze;
            _eyes.HoldGazeUntil = Time.time + 0.3f;   // one beat, re-armed while it stares
        }

        /// The weight the host sees it carrying; WeightSag bends the hips by it.
        public void SetBurden(byte b)
        {
            var dmg = GetComponent<Element>();
            if (dmg != null) dmg.BurdenOverride = b / 100f;
        }

        // ---- burning, frozen, glowing, ridden ----
        bool _burning, _glows;
        float _flameIn;
        GameObject _iceShell;

        public void SetCondition(byte bits)
        {
            _burning = (bits & 1) != 0;
            bool frozen = (bits & 2) != 0;
            if (frozen && _iceShell == null) _iceShell = Creature.BuildIceShell(transform);
            else if (!frozen && _iceShell != null) { Destroy(_iceShell); _iceShell = null; }

            bool glows = (bits & 4) != 0;
            if (glows != _glows) { _glows = glows; Reshade(); }

            bool ridden = (bits & 8) != 0;
            if (ridden != Possessed)
            {
                Possessed = ridden;
                ShowEyes(!ridden);
            }
        }

        /// A demon form's material swap, as Demon.Retint does it on the host:
        /// every body renderer but the eyes wears the tint in the glow shade.
        void Reshade()
        {
            if (_skin == null) return;
            var mat = MatterFX.Get(_tint, _glows ? MoteShade.Additive : MoteShade.Opaque);
            foreach (var r in _skin)
                if (r != null) r.sharedMaterial = mat;
            if (_view == null) return;
            _view.Rescan();
            _view.PushNow();
        }

        // ---- the speech bubble ----
        TextMesh _mumble;
        float _mumbleUntil;

        public void Mumble(string text, float seconds)
        {
            if (_mumble == null) _mumble = ZombieBrain.BuildMumbleText(transform);
            _mumble.text = text ?? "";
            _mumbleUntil = Time.time + seconds;
        }

        // the host's paint-freeze trance: the body holds still and the clip pauses
        bool _tranced;
        float _animSpeed = 1f;

        public void SetTranced(bool on)
        {
            if (on == _tranced) return;
            _tranced = on;
            if (_anim == null) _anim = GetComponentInChildren<Animator>(true);
            if (_anim == null) return;
            if (on) { _animSpeed = _anim.speed; _anim.speed = 0f; }
            else _anim.speed = _animSpeed;
        }

        void Update()
        {
            if (_mumble != null)
            {
                if (Time.time > _mumbleUntil) _mumble.text = "";
                ZombieBrain.FaceMumble(_mumble);
            }
            if (_burning && (_flameIn -= Time.deltaTime) <= 0f)
            {
                _flameIn = Creature.FlameEvery;
                Creature.SpawnFlame(transform);
            }

            // tranced it still follows the host (the grave climb, a knock);
            // only the stride waits, so the paused clip stays paused
            if (_glide.Sample(out var pos, out var rot))
            {
                transform.position = pos;
                transform.rotation = rot;
            }
            if (_tranced) return;
            if (_anim != null && _anim.isActiveAndEnabled)
            {
                Vector3 v = _glide.Velocity; v.y = 0f;
                ZombieDress.Stride(_anim, v.magnitude, transform.lossyScale.y);
            }
        }

        /// The first bone whose name reads as a head, so code-built eyes land
        /// on a face rather than a hip.
        static Transform FindHead(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && t.name.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return t;
            return null;
        }

        /// Snapshot stopped listing it: the host says it's gone. Its burst and
        /// thud already arrived from the host (FxMsg), so nothing plays twice.
        public void Vanish()
        {
            // the aura outlives its body here as SummonedZombie.FreeGas does on the host
            foreach (var gas in GetComponentsInChildren<PoisonField>())
            {
                gas.transform.SetParent(null, true);
                gas.Wearer = null;
            }
            Destroy(gameObject);
        }

    }
}
