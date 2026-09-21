using UnityEngine;

namespace SpellyZombie
{
    /// Client-side stand-in for a host-simulated golem: no wandering, no
    /// charge, no decisions - it glides to snapshots, takes spells as a valid
    /// target, and relays the damage to the host. Same shape as
    /// NetZombieProxy, and the same law: what the host raised is what every
    /// screen shows.
    public class NetGolemProxy : MonoBehaviour
    {
        readonly CreatureGlide _glide = new CreatureGlide();
        Color _skin = Color.gray;
        public int OwnerId = -1; // from the snapshot, for the ghost and the achievements
        public int Id;           // the host's instance id, the snapshot key

        /// A ghost drives it on the host.
        public bool Possessed { get; private set; }

        /// Where a rider looks from: the eyes, else the top of the body.
        public Vector3 HeadAt
        {
            get
            {
                var eyes = GetComponentInChildren<GooglyEyes>(true);
                return eyes != null ? eyes.transform.position
                    : transform.position + Vector3.up * _top.Above(transform);
            }
        }

        /// Where a rider sits: inside the body, hat out the top, same as the host's golem.
        public Vector3 SeatAt => transform.position + Vector3.up * (_top.Above(transform) - 0.22f);

        readonly BodyTop _top = new BodyTop();

        public void ShowEyes(bool on)
        {
            var eyes = GetComponentInChildren<GooglyEyes>(true);
            if (eyes == null) return;
            foreach (var r in eyes.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        }

        public static NetGolemProxy Build(int id, Vector3 pos, Vector3 scale, Color skin)
        {
            var prefab = CollectionManager.Golem;
            if (prefab == null) return null;   // the slot's own error already said so

            var go = Instantiate(prefab, pos, Quaternion.identity);
            go.name = "NetGolem";
            if (scale.sqrMagnitude > 0.0001f) go.transform.localScale = scale;

            // the host's own colour: a client cannot re-derive the biome that
            // stamped it, so the tint rides the snapshot instead
            var view = go.GetComponent<StateView>();
            if (view == null) view = go.AddComponent<StateView>();
            view.DriveTint = true;
            view.Tint = skin;

            // it is a target, not a walker: snapshots own where it stands, but
            // spells, heat and impacts still have to land on it
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            // no brain on a proxy - the host does the deciding
            var brain = go.GetComponent<Golem>();
            if (brain != null) Destroy(brain);
            var charge = go.GetComponent<ChargeAttack>();
            if (charge != null) Destroy(charge);
            var split = go.GetComponent<DensitySplit>();
            if (split != null) Destroy(split);

            if (go.GetComponent<SurfaceMaterialTag>() == null)
                go.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Stone;
            if (go.GetComponent<PersistentInkSurface>() == null)
                go.AddComponent<PersistentInkSurface>();   // you can doodle on them

            var dmg = go.GetComponent<Element>();
            if (dmg == null) dmg = go.AddComponent<Element>();
            dmg.Rename(id);            // the HOST's name for it, so hits find it
            dmg.Health = 100000f;      // the HOST owns real strength
            dmg.RemoveOnDeath = false;  // never dies locally - snapshots decide
            BodyFx.DressCreature(go).HurtOverride = 0f; // blood by the snapshot's Hp, eyes by StateMsg

            // the weight you can see: StateMsg feeds the burden, the sag reads it
            if (go.GetComponent<WeightSag>() == null) go.AddComponent<WeightSag>();

            var proxy = go.AddComponent<NetGolemProxy>();
            proxy.Id = id;
            proxy._glide.Push(pos, Quaternion.identity, Vector3.zero);
            proxy._skin = skin;
            proxy._view = view;
            proxy._eyes = go.GetComponentInChildren<GooglyEyes>(true);
            if (proxy._eyes != null) proxy._eyes.Facing = go.transform; // as Golem.Awake aims them
            return proxy;
        }

        StateView _view;
        byte _look = 254;
        CreatureDef _worn;

        /// The tint, the creature and the phase the snapshot carries (Golem.Wear).
        public void Wear(Color skin, byte look, float stateT)
        {
            if (_view == null) return;
            bool moved = false;
            if (skin != _skin) { _skin = skin; _view.Tint = skin; moved = true; }
            if (look != _look)
            {
                _look = look;
                _worn = SpellBook.Live.CreatureAt(look);
                _view.Look = _worn != null ? _worn.Skin : null;
                CreatureLook.Shape(gameObject, _worn);
                moved = true;
            }
            _view.StateT = stateT;
            if (moved) _view.PushNow();
        }

        public void Target(Vector3 pos, Quaternion rot, Vector3 vel) => _glide.Push(pos, rot, vel);

        // ---- eyes: mood, red pupils, the charge swell ----
        GooglyEyes _eyes;
        bool _moodHeld, _pupilsRed, _swelling;

        public void SetEyes(byte bits)
        {
            if (_eyes == null) return;
            var mood = (EyeMood)(bits & 7);
            if (mood != EyeMood.Neutral) { _eyes.SetMood(mood, 0.3f); _moodHeld = true; }
            else if (_moodHeld) { _eyes.SetMood(EyeMood.Neutral, 0f); _moodHeld = false; }

            bool red = (bits & 8) != 0;
            if (red != _pupilsRed)
            {
                _pupilsRed = red;
                _eyes.SetPupilTint(red ? DrawingConfig.MindControlEyeColor : (Color?)null);
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

        // ---- burning, frozen, ridden ----
        bool _burning;
        float _flameIn;
        GameObject _iceShell;

        /// The host's strength as a byte of full (255 = whole): the blood drips read it.
        public void SetHurt(byte hp)
        {
            var fx = GetComponent<BodyFx>();
            if (fx != null) fx.HurtOverride = 1f - hp / 255f;
        }

        public void SetCondition(byte bits)
        {
            _burning = (bits & 1) != 0;
            bool frozen = (bits & 2) != 0;
            if (frozen && _iceShell == null) _iceShell = Creature.BuildIceShell(transform);
            else if (!frozen && _iceShell != null) { Destroy(_iceShell); _iceShell = null; }

            bool ridden = (bits & 8) != 0;
            if (ridden != Possessed)
            {
                Possessed = ridden;
                ShowEyes(!ridden);
            }
        }

        // ---- the charge: the authored tell clip on the tell edge (the hop rides the position) ----
        byte _beat;

        public void SetBeat(byte beat)
        {
            if (beat == _beat) return;
            _beat = beat;
            if (beat != 1) return;
            var tell = _worn != null ? _worn.MoveClip(Zombie.Charge) : null;
            if (tell != null) OneShotClip.Play(gameObject, tell);
        }

        void Update()
        {
            if (_burning && (_flameIn -= Time.deltaTime) <= 0f)
            {
                _flameIn = Creature.FlameEvery;
                Creature.SpawnFlame(transform);
            }
            if (_glide.Sample(out var pos, out var rot))
            {
                transform.position = pos;
                transform.rotation = rot;
            }
            _feet.TickHeavy(transform.position, transform.localScale.y, false, Time.deltaTime);
        }
        readonly Footfalls _feet = new Footfalls();

        /// Snapshot stopped listing it: the host says it came apart. Its burst
        /// and thud already arrived from the host (FxMsg), so nothing plays twice.
        public void Vanish()
        {
            Destroy(gameObject);
        }
    }
}
