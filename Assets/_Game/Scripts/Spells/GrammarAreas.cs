using UnityEngine;

namespace SpellyZombie
{
    /// The poison field and the shared field plumbing. The old per-recipe
    /// combination fields died with Spells V2 - lvl3 is ArtificialBiome now.
    public static class GrammarFX
    {
        static readonly Collider[] _hits = new Collider[48];

        public static Collider[] ScanBuffer => _hits;

        /// Shared soft-sphere visual for every field; alpha kept low so it reads from inside.
        public static Transform FieldBall(Vector3 at, float radius, Color c, MoteShade shade)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "GrammarField";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = at;
            go.transform.localScale = Vector3.one * radius * 2f;
            c.a *= 0.45f;
            go.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(c, shade);
            return go.transform;
        }

        /// Bright ground circle marking a field's boundary.
        public static Transform GroundRing(Transform parent, Color c)
        {
            var go = new GameObject("AreaRing");
            go.transform.SetParent(parent, false);

            if (Physics.Raycast(parent.position + Vector3.up * 0.3f, Vector3.down,
                    out var hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                go.transform.position = hit.point + Vector3.up * 0.04f;

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.widthMultiplier = 0.09f;
            lr.positionCount = 36;
            for (int i = 0; i < 36; i++)
            {
                float a = i / 36f * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }
            lr.sharedMaterial = MatterFX.Get(new Color(c.r, c.g, c.b, 0.9f), MoteShade.Additive);
            return go.transform;
        }

        public static void PuffBurst(Vector3 at, Color c, int n = 4)
        {
            for (int i = 0; i < n; i++)
            {
                var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                puff.name = "Puff";
                Object.Destroy(puff.GetComponent<Collider>());
                puff.transform.position = at + Random.insideUnitSphere * 0.15f;
                puff.transform.localScale = Vector3.one * Random.Range(0.08f, 0.18f);
                puff.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(c, MoteShade.Transparent);
                var rb = puff.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.linearVelocity = Vector3.up * Random.Range(0.6f, 1.4f) + Random.insideUnitSphere * 0.35f;
                Object.Destroy(puff, 0.7f);
            }
        }

        /// Spawns one fire mote sphere.
        public static GameObject FireMote(Vector3 at, float scale, float life)
        {
            var f = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            f.name = "Fire";
            Object.Destroy(f.GetComponent<Collider>());
            f.transform.position = at;
            f.transform.localScale = Vector3.one * scale;
            f.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(
                Color.Lerp(new Color(1f, 0.75f, 0.15f), new Color(1f, 0.25f, 0.05f), Random.value),
                MoteShade.Additive);
            Object.Destroy(f, life);
            return f;
        }

        /// A gravity-free fling of fire motes - the shared bloom look.
        public static void FireBloom(Vector3 at, int count, float speed, float upKick)
        {
            for (int i = 0; i < count; i++)
            {
                var f = FireMote(at + Random.insideUnitSphere * 0.4f,
                    Random.Range(0.15f, 0.3f), Random.Range(0.4f, 0.9f));
                var rb = f.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.linearVelocity = Random.onUnitSphere * speed + Vector3.up * upKick;
            }
        }

        /// Spark lvl3 - FLAME BURST: flames burst across the area, once.
        public static void FlameBurst(Vector3 at, float power)
        {
            float r = DrawingConfig.UltimateRadius;
            Juice.Boom(at, 0.7f);
            if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.FireBurst, at);
            WorldEvents.Report(WorldEventKind.Explosion, at, 2f);
            DrawingWorld.Instance?.LogEvent("FLAME BURST");
            int n = Physics.OverlapSphereNonAlloc(at, r, _hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i];
                if (c == null) continue;
                var pl = c.GetComponent<SimpleFPSController>();
                if (pl != null) { pl.TakeHit((pl.transform.position - at).normalized * 9f, 28f * power); continue; }
                SpellParticle.GiveHeatTo(c, 200f * power); // combinations heat harder than single runes
                var rb = c.attachedRigidbody;
                if (rb != null) rb.AddForce((rb.worldCenterOfMass - at).normalized * 9f, ForceMode.VelocityChange);
            }
            FireBloom(at, 10, 4f, 2f);
        }
    }

    /// Base class for timed fields: a soft sphere that ticks an effect on
    /// everything inside, then fades. Subclasses implement one Affect().
    public abstract class GrammarField : MonoBehaviour
    {
        public float Power = 1f;
        public float Radius = 3.5f;
        public float Seconds = 5f;
        public Color Tint;      // full-strength field colour - feeds the inside-HUD

        protected Transform Ball;
        /// Authored FX skin; code must not add art inside it or change its scale.
        [System.NonSerialized] public bool HasSkin;
        [System.NonSerialized] public Transform Skin;
        [System.NonSerialized] public Vector3 SkinBase = Vector3.one;
        /// Skin scale with the field; default is the dome diameter.
        protected virtual Vector3 SkinShape => Vector3.one * Radius * 2f;
        Transform _ring;        // the ground boundary circle
        float _age, _tick;
        static readonly System.Collections.Generic.HashSet<Component> _seenRoots =
            new System.Collections.Generic.HashSet<Component>(); // per-tick body dedupe

        protected virtual float TickPeriod => 0.35f;
        protected abstract void Affect(Collider c, float dt);
        protected virtual void Grow(float dt) { }
        protected virtual void ShapeBall() { if (Ball != null) Ball.localScale = Vector3.one * Radius * 2f; }

        /// False hides the dome sphere; zone, ring and inside-HUD still work.
        protected virtual bool ShowDome => true;

        /// Whether the field affects this player; asked before the inside-HUD pulse.
        protected virtual bool AffectsPlayer(SimpleFPSController p) => true;

        public void ShowGroundRing(bool on)
        {
            if (_ring != null) _ring.gameObject.SetActive(on);
        }
        protected virtual void OnExpire() { }

        protected void Extend(float seconds)
        {
            Seconds = Mathf.Max(Seconds, seconds);
            _age = 0f;
        }

        /// Radius scales by SizeMul(size); size 0 leaves the radius unchanged.
        protected static T Spawn<T>(Vector3 at, float power, float radius, float seconds, Color c, MoteShade shade,
            float size = 0f)
            where T : GrammarField
        {
            radius *= SpellParticle.SizeMul(size);
            var go = new GameObject(typeof(T).Name);
            go.transform.position = at;
            var f = go.AddComponent<T>();
            f.Power = power;
            f.Radius = radius;
            f.Seconds = seconds;
            f.Tint = new Color(c.r, c.g, c.b, 1f);
            f.Ball = GrammarFX.FieldBall(at, radius, c, shade);
            f.Ball.SetParent(go.transform, true);
            f._ring = GrammarFX.GroundRing(go.transform, c);

            // a prefab named FX_<FieldClass> in Resources/Custom replaces the code dome; ring + HUD stay
            var skin = PrefabVault.Get("FX_" + typeof(T).Name);
            if (skin != null)
            {
                f.Skin = Object.Instantiate(skin, go.transform, false).transform;
                f.SkinBase = f.Skin.localScale;
                f.HasSkin = true;                 // fields must not build code art over it
                var dome = f.Ball.GetComponent<Renderer>();
                if (dome != null) dome.enabled = false;
            }
            else if (FxLibrary.I != null)
            {
                // rides the field for its whole life; an FX_<FieldClass> override wins instead
                var jmo = FxLibrary.I.FieldFor(typeof(T).Name);
                if (jmo != null) FxLibrary.Spawn(jmo, at, go.transform, seconds + 0.5f);
            }

            if (!f.ShowDome)
            {
                var bare = f.Ball.GetComponent<Renderer>();
                if (bare != null) bare.enabled = false;
            }
            return f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            Grow(dt);
            ShapeBall();
            if (_ring != null) _ring.localScale = Vector3.one * Radius; // grows with the field
            if (Skin != null) Skin.localScale = Vector3.Scale(SkinBase, SkinShape);

            _tick -= dt;
            if (_tick <= 0f)
            {
                _tick = TickPeriod;
                // Collide, not Ignore: spell particles are trigger spheres
                int n = Physics.OverlapSphereNonAlloc(transform.position, Radius,
                    GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Collide);
                _seenRoots.Clear();
                for (int i = 0; i < n; i++)
                {
                    var c = GrammarFX.ScanBuffer[i];
                    if (c == null) continue;
                    // one body = one tick: rigs are many limb colliders; dedupe by root
                    Component root = (Component)c.GetComponentInParent<SimpleFPSController>()
                        ?? (Component)c.GetComponentInParent<Creature>()
                        ?? (Component)c.GetComponent<Matter>()
                        ?? (Component)c.attachedRigidbody;
                    if (root != null && !_seenRoots.Add(root)) continue;
                    // standing in a field pulses the screen edges in its colour
                    if (root is SimpleFPSController pilotRoot)
                    {
                        // pulse only when the field actually affects this player
                        if (!AffectsPlayer(pilotRoot)) continue;
                        GrammarFieldHUD.Inside(Tint);
                        // hand fields the pilot's capsule; a limb bone would miss GetComponent checks
                        var pilotCC = pilotRoot.GetComponent<CharacterController>();
                        if (pilotCC != null) c = pilotCC;
                    }
                    Affect(c, TickPeriod);
                }
            }

            if (_age >= Seconds) { OnExpire(); Destroy(gameObject); }
        }
    }

    /// Poison gas zone. Three uses: the cloud a zombie breathes, the bigger
    /// one a detonation leaves, and the one clinging to a body.
    public class PoisonField : GrammarField
    {
        /// Body wearing this cloud; it never poisons its own host.
        [System.NonSerialized] public Transform Wearer;

        static readonly Color Sick = new Color(0.55f, 0.85f, 0.25f);

        /// Gas is drawn by the CFXR cloud; the dome stays hidden.
        protected override bool ShowDome => false;

        const float PuffLife = 2.8f;
        float _puffIn;
        bool _firstPuff = true;

        /// Live poison zone count; caps total particle cost.
        static int _liveFields;
        void OnEnable() => _liveFields++;
        void OnDisable() => _liveFields--;

        protected override void Grow(float dt)
        {
            if ((_puffIn -= dt) > 0f) return;
            if (FxLibrary.I == null) return;

            // a possessed zombie emits no puffs so the driver can see
            if (Wearer != null)
            {
                var host = Wearer.GetComponent<Zombie>();
                if (host != null && host.Possessed) return;
            }

            // one puff per tick, never a burst - FxLibrary drops spawns past its
            // per-frame budget. Cadence stretches with _liveFields so total smoke stays roughly fixed.
            float crowd = Mathf.Max(1f, _liveFields / DrawingConfig.PoisonFxCrowd);
            _puffIn = DrawingConfig.PoisonPuffEvery * crowd;

            // skip puffs farther than PoisonFxDistance from the camera
            var eye = Camera.main;
            if (eye != null)
            {
                float far = DrawingConfig.PoisonFxDistance;
                if ((eye.transform.position - transform.position).sqrMagnitude > far * far)
                    return;
            }

            // scatter through the sphere; the first puff lands centre so a new cloud shows at once
            Vector3 spot = transform.position;
            if (!_firstPuff) spot += Random.insideUnitSphere * Radius * 0.6f;
            _firstPuff = false;

            var fx = FxLibrary.Spawn(FxLibrary.I.GasCloud, spot, null, PuffLife);
            // the prefab emits 2-3 UNIT particles, so metres need converting
            if (fx != null)
                fx.transform.localScale = Vector3.one *
                    Mathf.Max(0.05f, Radius * DrawingConfig.PoisonFxScale);
        }

        /// ring: ground circle, on for detonations, off for body-carried gas.
        public static PoisonField Open(Vector3 at, float radius, float seconds,
            Transform rideOn = null, bool ring = false)
        {
            var f = Spawn<PoisonField>(at, 1f, radius, seconds, Sick, MoteShade.Transparent);
            f.ShowGroundRing(ring);
            // stagger so simultaneous clouds don't starve each other
            f._puffIn = Random.value * DrawingConfig.PoisonPuffEvery;
            if (rideOn != null)
            {
                f.transform.SetParent(rideOn, true);
                f.Wearer = rideOn;
            }
            return f;
        }

        /// Acolytes are immune; asked before the pulse so they get no edges either.
        protected override bool AffectsPlayer(SimpleFPSController p) =>
            !Sides.IsAcolytePlayer(p);

        protected override void Affect(Collider c, float dt)
        {
            var p = c.GetComponentInParent<SimpleFPSController>();
            if (p == null || p.IsDowned) return;
            if (Wearer != null && p.transform == Wearer) return;  // your own cloud
            if (!AffectsPlayer(p)) return;   // one predicate, asked here and by the HUD

            p.TakeHit(Vector3.zero, DrawingConfig.PoisonDamage * dt, "the corruption");
            Cling(p, dt);
        }

        /// Attaches a small PoisonField to the victim's head; it grows with
        /// exposure and poisons others in turn.
        static void Cling(SimpleFPSController victim, float dt)
        {
            var worn = victim.GetComponentInChildren<PoisonField>();
            if (worn == null)
            {
                // on the head, small, visible to other players
                PoisonField.Open(victim.transform.position + Vector3.up * 1.6f,
                    DrawingConfig.PoisonClingRadius,
                    DrawingConfig.PoisonClingSeconds, victim.transform);
                return;
            }
            worn.Radius = Mathf.Min(worn.Radius + DrawingConfig.PoisonClingGrow * dt,
                DrawingConfig.PoisonClingMax);
            worn.Extend(DrawingConfig.PoisonClingSeconds);
        }
    }
    /// Fields ping this each tick while affecting the local player; the screen
    /// edges glow and pulse in the field's colour.
    public class GrammarFieldHUD : MonoBehaviour
    {
        static GrammarFieldHUD _i;
        Color _color;
        float _until;

        public static void Inside(Color c)
        {
            if (_i == null)
            {
                var go = new GameObject("GrammarFieldHUD");
                DontDestroyOnLoad(go);
                _i = go.AddComponent<GrammarFieldHUD>();
            }
            _i._color = c;
            _i._until = Time.time + 0.55f; // outlives one tick - no flicker
        }

        void OnGUI()
        {
            if (Time.time > _until) return;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4.2f);
            float w = Screen.width, h = Screen.height;
            var c = _color;

            // soft full-screen wash
            GUI.color = new Color(c.r, c.g, c.b, 0.05f + 0.03f * pulse);
            GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);

            // glowing edges, two strips each for a cheap gradient
            float e1 = h * 0.045f, e2 = h * 0.10f;
            GUI.color = new Color(c.r, c.g, c.b, 0.28f + 0.12f * pulse);
            GUI.DrawTexture(new Rect(0f, 0f, w, e1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, h - e1, w, e1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, 0f, e1, h), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(w - e1, 0f, e1, h), Texture2D.whiteTexture);
            GUI.color = new Color(c.r, c.g, c.b, 0.12f + 0.06f * pulse);
            GUI.DrawTexture(new Rect(0f, 0f, w, e2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, h - e2, w, e2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, 0f, e2, h), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(w - e2, 0f, e2, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
