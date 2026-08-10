using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    // Charger doubles as the spec's "brute", Scribbler as the "spitter".
    public enum ZombieKind { Walker, Charger, Scribbler, Runner, Swarm }

    /// The design's enemy: slow, physical, legibly dumb (see ZombieBrain); all Flesh-tagged rigidbodies so the whole chemistry applies.
    [RequireComponent(typeof(Rigidbody))]
    public class Zombie : MonoBehaviour
    {
        public ZombieKind Kind = ZombieKind.Walker;
        public float WalkSpeed = 1.3f;
        public float AttackRange = 1.4f;
        public float AttackDamage = 10f;
        public float AttackCooldown = 1.2f;

        /// The rune cards this zombie possesses — used when IT completes a seal,
        /// and dropped as pickups when it dies (design rule 4).
        public readonly List<RuneCardType> Cards = new List<RuneCardType>();

        /// Owner id in the Grimoire (seal ownership + rune checks).
        public int OwnerId => gameObject.GetInstanceID();

        /// Set by Demon on attach — demons don't count toward ending a round (spares a GetComponent per zombie per tick).
        public bool IsDemon;

        /// Live registry — cheaper than FindObjectsByType in per-tick paths,
        /// and the RoundDirector's alive count.
        public static readonly List<Zombie> All = new List<Zombie>();

        static readonly Color SkinColor = new Color(0.45f, 0.62f, 0.35f);
        static readonly Color ChargerColor = new Color(0.6f, 0.45f, 0.3f);
        static readonly Color ScribblerColor = new Color(0.5f, 0.35f, 0.72f); // wizard purple
        static readonly Color HatColor = new Color(0.28f, 0.16f, 0.45f);
        static readonly Color RunnerColor = new Color(0.72f, 0.68f, 0.35f);  // sickly sprinter yellow
        static readonly Color SwarmColor = new Color(0.3f, 0.45f, 0.25f);    // little dark gremlins

        /// Shared kind→look table — NetZombieProxy reads these so host and client visuals can never drift.
        public static Vector3 KindScale(ZombieKind kind) => RawKindScale(kind) * DrawingConfig.ZombieBodyScale;

        static Vector3 RawKindScale(ZombieKind kind) =>
            kind == ZombieKind.Charger ? new Vector3(0.9f, 0.95f, 0.9f)   // stocky
            : kind == ZombieKind.Runner ? new Vector3(0.5f, 1.05f, 0.5f)  // lanky
            : kind == ZombieKind.Swarm ? new Vector3(0.42f, 0.5f, 0.42f)  // gremlin
            : new Vector3(0.7f, 1f, 0.7f);

        public static Color KindSkin(ZombieKind kind) =>
            kind == ZombieKind.Charger ? ChargerColor
            : kind == ZombieKind.Scribbler ? ScribblerColor
            : kind == ZombieKind.Runner ? RunnerColor
            : kind == ZombieKind.Swarm ? SwarmColor : SkinColor;

        Rigidbody _rb;
        Creature _creature;
        ZombieBrain _brain;

        // charger state
        float _windup, _chargeLeft, _chargeCooldown;
        Vector3 _chargeDir;
        bool _charging;

        // scribbler state
        float _castLeft, _castCooldown;
        LineRenderer _castRing;

        // scribbler compulsions: completing doodles + tagging buddies with runes
        Stroke _doodle;
        float _doodleScan, _ritualLeft, _completeCooldown, _tagCooldown, _idleDoodle;

        // full-seal scrawl: runes first, circle a beat later (readable — and it
        // SHOWS players which cards this wizard carries)
        float _sealScrawl, _scrawlCircleIn;
        Vector3 _scrawlCenter, _scrawlNormal;
        float _scrawlRadius;
        Transform _scrawlSurface;

        public static Zombie Spawn(Vector3 pos, ZombieKind kind, float speedMul = 1f)
        {
            // B4: zombies exist ONLY on the host — clients get NetZombieProxy stand-ins
            if (NetGame.Connected && !NetGame.IsHost) return null;

            Color skin = KindSkin(kind);
            GameObject go, head;

            // ---- HIS PREFAB WINS, WHOLESALE ----------------------------------
            // ONE prefab covers every kind. Drag a zombie out of the Hierarchy,
            // drop it at Resources/Custom/Zombie, and the game spawns THAT
            // instead of building a capsule. (Resources/Custom/Zombie_Charger
            // and friends are checked first, so a per-kind override is possible
            // but never required.)
            //
            // The KINDS still differ, because that is code's job: a Charger is
            // stocky and a Runner is lanky. His authored scale is the base and
            // the kind multiplies it, so his prefab's size is respected and the
            // roster still reads at a glance from one model.
            //
            // His materials, his hat, his head placement are left alone, and
            // every component below is filled in ONLY IF MISSING, the same
            // "fills gaps only" rule his scenery tools already follow.
            var custom = PrefabVault.Get("Zombie_" + kind) ?? PrefabVault.Get("Zombie");
            bool graybox = custom == null;

            if (!graybox)
            {
                go = Instantiate(custom);
                go.name = "Zombie_" + kind;
                go.transform.position = pos + Vector3.up * 1.1f;

                // kind shape relative to a Walker, applied on top of HIS scale
                Vector3 rel = RawKindScale(kind);
                Vector3 baseK = RawKindScale(ZombieKind.Walker);
                go.transform.localScale = Vector3.Scale(go.transform.localScale,
                    new Vector3(rel.x / baseK.x, rel.y / baseK.y, rel.z / baseK.z));

                head = FindNamed(go.transform, "Head") ?? go;   // eyes mount here
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "Zombie_" + kind;
                go.transform.position = pos + Vector3.up * 1.1f;
                go.transform.localScale = KindScale(kind);
                go.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(skin, MoteShade.Opaque);

                head = GameObject.CreatePrimitive(PrimitiveType.Cube);
                head.name = "Head";
                Destroy(head.GetComponent<Collider>());
                head.transform.SetParent(go.transform, false);
                head.transform.localPosition = new Vector3(0f, 1.05f, 0.05f);
                head.transform.localScale = new Vector3(0.55f, 0.4f, 0.55f);
                head.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(skin * 1.15f, MoteShade.Opaque);
            }

            // scribbler = The Wizard; HIS HAT WINS: Resources/Custom/ScribblerHat replaces the code cubes
            if (graybox && kind == ZombieKind.Scribbler)
            {
                var hatSkin = PrefabVault.Spawn("ScribblerHat", go.transform);
                if (hatSkin != null)
                {
                    hatSkin.name = "Hat"; // rides the head bone with the rest
                    hatSkin.transform.localPosition = new Vector3(0f, 1.32f, 0.05f);
                }
                else
                {
                    AddHatPart(go.transform, new Vector3(0f, 1.32f, 0.05f), new Vector3(0.72f, 0.05f, 0.72f), 0f);   // brim
                    AddHatPart(go.transform, new Vector3(0f, 1.45f, 0.05f), new Vector3(0.4f, 0.22f, 0.4f), 4f);     // base
                    AddHatPart(go.transform, new Vector3(0.03f, 1.64f, 0.05f), new Vector3(0.24f, 0.2f, 0.24f), 10f); // mid
                    AddHatPart(go.transform, new Vector3(0.08f, 1.8f, 0.05f), new Vector3(0.1f, 0.18f, 0.1f), 22f);  // crooked tip
                }
            }

            // EVERY COMPONENT BELOW FILLS A GAP. If his prefab already carries
            // one, it is used exactly as he configured it and none of these
            // numbers get written over the top. Only components the code itself
            // created take the per-kind stat pass.
            var rb = Adopt.Component<Rigidbody>(go, out bool rbNew);
            if (rbNew)
            {
                rb.mass = kind == ZombieKind.Charger ? 110f : kind == ZombieKind.Swarm ? 25f : 70f;
                rb.constraints = RigidbodyConstraints.FreezeRotation;
            }

            var dmg = Adopt.Component<Damageable>(go, out bool dmgNew);
            if (dmgNew)
                dmg.Health = kind == ZombieKind.Charger ? 90f
                    : kind == ZombieKind.Runner ? 30f
                    : kind == ZombieKind.Swarm ? 12f : 60f;

            var creature = Adopt.Component<Creature>(go);
            var tag = Adopt.Component<SurfaceMaterialTag>(go, out bool tagNew);
            if (tagNew) tag.Material = SurfaceMaterialType.Flesh;
            Adopt.Component<PersistentInkSurface>(go); // runes drawn ON zombies ride them and persist

            // the googly soul — HIS eyes if the prefab already has a pair,
            // otherwise the code-built ones on the head
            var eyes = go.GetComponentInChildren<GooglyEyes>(true)
                ?? GooglyEyes.Attach(head.transform, 0f, DrawingConfig.ZombieEyeScale);

            var brain = Adopt.Component<ZombieBrain>(go, out bool brainNew);
            brain.Eyes = eyes;
            if (brainNew)
            {
                // capacity IS intelligence: charger 1 slot, scribbler the horde's intellectual
                brain.Capacity = kind == ZombieKind.Charger || kind == ZombieKind.Swarm ? 1
                    : kind == ZombieKind.Scribbler ? 5 : 3;
                if (kind == ZombieKind.Runner) { brain.SightRange = 17f; brain.HearRange = 12f; } // skittish
            }

            var z = Adopt.Component<Zombie>(go, out bool zNew);
            z.Kind = kind;
            if (zNew)
            {
                switch (kind) // body stats — the roster is mostly numbers on one bean
                {
                    case ZombieKind.Runner: z.WalkSpeed = 3.4f; z.AttackDamage = 6f; z.AttackCooldown = 0.7f; break;
                    case ZombieKind.Swarm: z.WalkSpeed = 2.3f; z.AttackDamage = 3f; z.AttackCooldown = 0.5f; z.AttackRange = 0.9f; break;
                    case ZombieKind.Charger: z.AttackDamage = 14f; break;
                }
            }
            z.WalkSpeed *= speedMul; // rounds make everything faster
            dmg.OnDeath += z.OnDeath;
            dmg.OnDamaged += z.OnDamaged;

            // every zombie carries rune cards (scribblers carry two — juiciest to hunt)
            var all = (RuneCardType[])System.Enum.GetValues(typeof(RuneCardType));
            int cardCount = kind == ZombieKind.Scribbler ? 2 : 1;
            for (int i = 0; i < cardCount; i++)
            {
                var card = all[Random.Range(0, all.Length)];
                if (!z.Cards.Contains(card)) z.Cards.Add(card);
                Grimoire.Unlock(z.OwnerId, card);
            }

            if (kind == ZombieKind.Scribbler)
            {
                z._idleDoodle = Random.Range(3f, 6f);   // first doodle comes fast
                z._sealScrawl = Random.Range(9f, 15f);  // first full seal a bit later
                Debug.Log($"[SpellyZombie] Scribbler spawned (hat, purple) carrying: {string.Join(", ", z.Cards)}");
            }

            // wardrobe: shared character model follows the capsule (graybox continues if not wired).
            // HIS PREFAB IS ALREADY DRESSED — do not wear a second body over it.
            if (graybox)
            {
                float widthMul = kind == ZombieKind.Charger ? 1.25f
                    : kind == ZombieKind.Runner ? 0.72f : 1f;
                z._dress = ZombieDress.DressUp(z, skin, widthMul, eyes);
            }
            return z;
        }

        /// First child whose name contains `name`, case-insensitive. Lets his
        /// prefab call the head "Head", "head_geo" or "SZ_Head" and still get eyes.
        static GameObject FindNamed(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && t.name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return t.gameObject;
            return null;
        }

        ZombieDress _dress;

        /// The visual follower wearing this zombie's model (null in graybox).
        public ZombieDress Dress => _dress;

        static void AddHatPart(Transform body, Vector3 localPos, Vector3 localScale, float tiltZ)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = "Hat";
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(body, false);
            part.transform.localPosition = localPos;
            part.transform.localScale = localScale;
            part.transform.localRotation = Quaternion.Euler(0f, 0f, tiltZ);
            part.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(HatColor, MoteShade.Opaque);
        }

        Damageable _dmg2;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _creature = GetComponent<Creature>();
            _brain = GetComponent<ZombieBrain>();
            _dmg2 = GetComponent<Damageable>();
            All.Add(this);
        }

        void OnDestroy() => All.Remove(this);

        /// Walk WITHOUT erasing physics: legs steer with limited grip so spell pushes visibly win (hard-set velocity made zombies force-immune).
        void Steer(Vector3 dir, float speed)
        {
            Vector3 v = _rb.linearVelocity;
            Vector3 horiz = new Vector3(v.x, 0f, v.z);
            Vector3 desired = new Vector3(dir.x, 0f, dir.z);
            if (desired.sqrMagnitude > 0.01f) desired = desired.normalized * speed;
            else desired = Vector3.zero;

            // REVERTED to the fixed reach (Marko Aug 11). Scaling it off
            // localScale.y was defensible on its own, but it is part of the same
            // movement pass that broke, and known-good beats clever.
            bool grounded = Physics.Raycast(transform.position, Vector3.down, 1.35f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float grip = grounded ? 14f : 2f;

            Vector3 blended = Vector3.MoveTowards(horiz, desired, grip * Time.fixedDeltaTime);
            _rb.linearVelocity = new Vector3(blended.x, v.y, blended.z);
        }

        void FixedUpdate()
        {
            if (_creature == null || _brain == null) return;

            // THE VOID RULE (Marko): below the floor it dies where it fell — drops, kill credit, no teleport home
            if (transform.position.y < FallCatcher.KillY)
            {
                _dmg2?.TakeDamage(99999f, "the void");
                return;
            }

            if (_charging) { TickCharge(); return; }
            if (!_creature.CanMove) return;

            // LIMP WHILE LIFTED. A zombie that keeps walking in mid-air fights the
            // levitation and jitters, and one that steers itself while flying does
            // not read as thrown. While something has hold of it, it is cargo
            // rather than a creature: no steering, no turning, no chewing.
            if (HandGrab.LocalHeldBody == _rb) { _windup = 0f; return; }

            // TRANCE: full stop, just bliss (steered, not hard-set — fireballs still launch it)
            if (_brain.Tranced)
            {
                Steer(Vector3.zero, 0f);
                _windup = 0f;
                return;
            }

            // ---- the brain decided; the body obeys (badly) ----
            float speed = WalkSpeed * _brain.SpeedScale * _creature.SpeedMultiplier;
            if (_brain.MoveDir.sqrMagnitude > 0.01f && speed > 0.01f)
            {
                Steer(_brain.MoveDir, speed);
                if (_creature.SpeedMultiplier >= 0.5f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(_brain.MoveDir), Time.fixedDeltaTime * 4f);
                TryChewObstacle(speed); // CoD rule: barricades get eaten, not respected
            }

            // the compulsion outranks EVERYTHING: a scribbler that sees an
            // unfinished doodle must complete it — even the smart one is a zombie
            if (Kind == ZombieKind.Scribbler && TickCompulsion()) return;

            var target = _brain.AttackTarget;
            if (target == null) { TickIdleKind(); return; }
            float dist = Vector3.Distance(transform.position, target.position);

            switch (Kind)
            {
                case ZombieKind.Walker: TrySwipe(target, dist); break;
                case ZombieKind.Charger: TickChargerWindup(target, dist); break;
                case ZombieKind.Scribbler: TickScribbler(target, dist); break;
            }
        }

        /// The scribbler's disease: it completes ANY open ink into a REAL seal with ITS cards, and tags buddies with runes. Returns true while busy.
        bool TickCompulsion()
        {
            float dt = Time.fixedDeltaTime;
            _completeCooldown -= dt;
            _tagCooldown -= dt;
            if (_completeCooldown > 0f) return false;

            // find something unfinished to obsess over
            _doodleScan -= dt;
            if (_doodle == null && _doodleScan <= 0f)
            {
                _doodleScan = 0.8f;
                _doodle = ZombieScribe.FindDoodle(transform.position, 18f, OwnerId, transform);
                if (_doodle != null)
                {
                    _brain.Mumble("OOOH…", 2f);
                    _ritualLeft = 1.8f;
                    Debug.Log("[SpellyZombie] A scribbler locked onto a doodle. It MUST complete it.");
                }
            }

            // fresh ink is a MAGNET — the decoy loop: it arrives as you finish and must join in
            if (_doodle == null && WorldEvents.InkIsFresh)
            {
                Vector3 ink = WorldEvents.LatestInkPos;
                float inkDist = Vector3.Distance(transform.position, ink);
                if (inkDist > 2.2f && inkDist < 20f) // >2.2m: ink on its OWN back doesn't lure it
                {
                    Vector3 to = ink - transform.position; to.y = 0f;
                    if (to.sqrMagnitude > 0.01f)
                    {
                        Steer(to, WalkSpeed);
                        transform.rotation = Quaternion.Slerp(transform.rotation,
                            Quaternion.LookRotation(to.normalized), Time.fixedDeltaTime * 4f);
                        if (_brain.Eyes != null) _brain.Eyes.LookTarget = ink;
                    }
                    return true;
                }
            }

            if (_doodle != null)
            {
                if (!_doodle.Alive || _doodle.State != StrokeState.Open) { _doodle = null; return false; }

                ZombieScribe.MeasureDoodle(_doodle, out var center, out var normal, out float radius);
                if (_brain.Eyes != null) _brain.Eyes.LookTarget = center;
                float dist = Vector3.Distance(transform.position, center);

                if (dist > 2.6f) // walk to the doodle, entranced
                {
                    Vector3 to = center - transform.position; to.y = 0f;
                    Steer(to, WalkSpeed);
                    if (to.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.Slerp(transform.rotation,
                            Quaternion.LookRotation(to.normalized), Time.fixedDeltaTime * 4f);
                    return true;
                }

                Steer(Vector3.zero, 0f);
                _ritualLeft -= dt;
                DrawCastRing(1f - Mathf.Clamp01(_ritualLeft / 1.8f));
                if (_ritualLeft > 0f) return true;

                // it HAS to. it cannot not.
                ClearCastRing();
                var surface = _doodle.First != null ? _doodle.First.transform.parent : null;
                bool closed = ZombieScribe.DrawCircle(center, normal, radius, surface, OwnerId);
                _brain.Mumble(closed ? "MMM: DONE." : "GRUH?!", 2.5f); // botched it. tragic.
                if (!closed) _brain.Eyes?.SetMood(EyeMood.Scared, 2f);
                _doodle = null;
                _completeCooldown = 6f;
                return true;
            }

            // finishing a seal scrawl: the circle lands a beat after the runes
            if (_scrawlCircleIn > 0f)
            {
                Steer(Vector3.zero, 0f);
                _scrawlCircleIn -= dt;
                if (_scrawlCircleIn <= 0f)
                {
                    bool closed = _scrawlSurface != null &&
                        ZombieScribe.DrawCircle(_scrawlCenter, _scrawlNormal, _scrawlRadius, _scrawlSurface, OwnerId);
                    _brain.Mumble(closed ? "HMM. MINE." : "GRUH?!", 2.5f); // the canvas walked away
                }
                return true;
            }

            // occasionally casts ITS OWN SEAL on whatever is in front — free intel on its whole deck
            _sealScrawl -= dt;
            if (_doodle == null && _sealScrawl <= 0f && Cards.Count > 0)
            {
                _sealScrawl = Random.Range(16f, 26f);
                Vector3 center = default, normal = default;
                Transform surface = null;

                Vector3 eye = transform.position + Vector3.up * 0.3f;
                if (Physics.Raycast(eye, transform.forward, out var front, 2.2f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && front.collider.GetComponentInParent<SimpleFPSController>() == null)
                {
                    center = front.point + front.normal * 0.02f;
                    normal = front.normal;
                    surface = front.collider.transform;
                }
                else if (Physics.Raycast(transform.position + transform.forward * 1.3f + Vector3.up * 0.5f,
                        Vector3.down, out var ground, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && ground.collider.GetComponentInParent<Zombie>() == null)
                {
                    center = ground.point + ground.normal * 0.02f;
                    normal = ground.normal;
                    surface = ground.collider.transform;
                }

                if (surface != null)
                {
                    ZombieScribe.PlaneBasis(normal, out var right, out _);
                    int count = Mathf.Min(Cards.Count, 2);
                    for (int i = 0; i < count; i++)
                        ZombieScribe.DrawGlyph(RandomRuneOf(Cards[i]),
                            center + right * ((i - (count - 1) * 0.5f) * 0.55f),
                            normal, 0.4f, surface, OwnerId);

                    _scrawlCenter = center;
                    _scrawlNormal = normal;
                    _scrawlRadius = count == 2 ? 0.85f : 0.55f;
                    _scrawlSurface = surface;
                    _scrawlCircleIn = 1.2f;
                    _brain.Mumble("SKRTCH SKRTCH", 2f);
                    Debug.Log($"[SpellyZombie] A scribbler is scrawling its OWN seal ({count} rune(s)) on {surface.name}.");
                    return true;
                }
            }

            // idle: doodle deck runes on the ground — graffiti other scribblers circle, and YOU can seal to steal a cast
            _idleDoodle -= dt;
            if (_doodle == null && _idleDoodle <= 0f && Cards.Count > 0)
            {
                _idleDoodle = Random.Range(7f, 13f);
                Vector3 spot = transform.position + transform.forward * 1.3f + Vector3.up * 0.5f;
                if (Physics.Raycast(spot, Vector3.down, out var ground, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && ground.collider.GetComponentInParent<Zombie>() == null)
                {
                    var doodleRune = RandomRuneOf(Cards[Random.Range(0, Cards.Count)]);
                    ZombieScribe.DrawGlyph(doodleRune, ground.point + ground.normal * 0.02f,
                        ground.normal, 0.5f, ground.collider.transform, OwnerId);
                    _brain.Mumble("skrtch skrtch…", 2.5f);
                    Debug.Log($"[SpellyZombie] A scribbler doodled {doodleRune} on the ground.");
                }
            }

            // no doodle around: maybe vandalize a buddy with a rune it owns
            if (_tagCooldown <= 0f && Cards.Count > 0)
            {
                foreach (var buddy in All)
                {
                    if (buddy == this) continue;
                    Vector3 to = buddy.transform.position - transform.position;
                    if (to.sqrMagnitude > 3f * 3f) continue;

                    _tagCooldown = Random.Range(12f, 20f);
                    var card = Cards[Random.Range(0, Cards.Count)];
                    var rune = RandomRuneOf(card);
                    Vector3 normal = -to.normalized; normal.y = 0f;
                    if (normal.sqrMagnitude < 0.01f) normal = -transform.forward;
                    normal.Normalize();
                    ZombieScribe.DrawGlyph(rune, buddy.transform.position + Vector3.up * 0.35f + normal * 0.4f,
                        normal, 0.45f, buddy.transform, OwnerId);
                    _brain.Mumble("HRR HRR HRR", 2.5f);
                    buddy.GetComponent<ZombieBrain>()?.Mumble("...?", 2f);
                    break;
                }
            }
            return false;
        }

        static RuneType RandomRuneOf(RuneCardType card)
        {
            switch (card)
            {
                case RuneCardType.Heat: return Random.value < 0.5f ? RuneType.HeatUp : RuneType.HeatDown;
                case RuneCardType.State: return Random.value < 0.5f ? RuneType.StateSolid : RuneType.StateLiquid;
                case RuneCardType.Luminance: return Random.value < 0.5f ? RuneType.LuminanceUp : RuneType.LuminanceDown;
                case RuneCardType.Sticky: return Random.value < 0.5f ? RuneType.StickyUp : RuneType.StickyDown;
                case RuneCardType.Direction: return Random.value < 0.5f ? RuneType.DirectionAway : RuneType.DirectionToward;
                default: return Random.value < 0.5f ? RuneType.DensityUp : RuneType.DensityDown;
            }
        }

        // ------------------------------------------------- barricade chewing --
        // CoD flow: blocked by a breakable → swipe it apart; solid walls stay walls (patrol repick handles those)
        float _chewTimer;
        void TryChewObstacle(float wantedSpeed)
        {
            _chewTimer -= Time.fixedDeltaTime;
            if (wantedSpeed < 0.1f) return;

            // actually stuck? it wanted to walk but is barely moving
            Vector3 v = _rb.linearVelocity;
            v.y = 0f;
            if (v.sqrMagnitude > wantedSpeed * wantedSpeed * 0.15f) return;
            if (_chewTimer > 0f) return;

            Vector3 dir = _brain.MoveDir.normalized;
            int mask = Physics.DefaultRaycastLayers & ~(1 << InkCanvasLayer.Layer);
            // SphereCast from the BELLY — root-height rays sailed over fences (Marko: "zombies do not destroy anything")
            if (!Physics.SphereCast(transform.position + Vector3.down * 0.35f, 0.4f,
                    dir, out var hit, 1.6f, mask, QueryTriggerInteraction.Ignore)) return;

            if (hit.collider.GetComponentInParent<Creature>() != null) return;            // brawls are elsewhere
            if (hit.collider.GetComponentInParent<SimpleFPSController>() != null) return; // that's lunch, not lumber
            var obstacle = hit.collider.GetComponentInParent<Damageable>();
            if (obstacle == null) return; // real wall — go around

            _chewTimer = AttackCooldown * 1.1f;
            _dress?.Attack();
            obstacle.TakeDamage(AttackDamage * 1.4f, $"{name} tearing through");
            Juice.Thud(hit.point);
            _brain.Mumble("RRAGH!!", 1.2f);
            _brain.Eyes?.SetMood(EyeMood.Mad, 1f);
        }

        // ------------------------------------------------------------ walker --
        float _attackTimer;
        void TrySwipe(Transform target, float dist)
        {
            _attackTimer -= Time.fixedDeltaTime;
            if (dist > AttackRange || _attackTimer > 0f) return;
            _attackTimer = AttackCooldown;
            _dress?.Attack();

            var player = target.GetComponent<SimpleFPSController>();
            if (player != null)
            {
                Vector3 dir = (target.position - transform.position).normalized;
                player.TakeHit(dir * 6f + Vector3.up * 2f, AttackDamage);
                return;
            }
            // zombie brawl: swiping the zombie it's mad at
            var d = target.GetComponentInParent<Damageable>();
            if (d != null) d.TakeDamage(AttackDamage * 1.5f, $"{name} brawl");
            var c = target.GetComponentInParent<Creature>();
            if (c != null && Random.value < 0.35f) c.KnockDown(2f);
        }

        // ----------------------------------------------------------- charger --
        void TickChargerWindup(Transform target, float dist)
        {
            _chargeCooldown -= Time.fixedDeltaTime;
            if (_chargeCooldown > 0f) { TrySwipe(target, dist); return; }
            if (dist > 12f || dist < 2f) { TrySwipe(target, dist); return; }

            _windup += Time.fixedDeltaTime;
            _brain.SpeedScale = 0f; // dig in
            Vector2 tremble = Random.insideUnitCircle * 0.01f;
            transform.position += new Vector3(tremble.x, 0f, tremble.y); // shaking with intent
            if (_windup < 1f)
            {
                if (_windup < 0.1f)
                {
                    _brain.Mumble("HRRNK!!", 1.5f);
                    _brain.Eyes?.SetMood(EyeMood.Mad, 1.5f);
                    _dress?.Scream(); // the agonized windup howl
                }
                return;
            }

            // GO. Direction is LOCKED — steering is for the living.
            _windup = 0f;
            _charging = true;
            _chargeLeft = 3f;
            _chargeDir = (target.position - transform.position).normalized;
            _chargeDir.y = 0f;
        }

        void TickCharge()
        {
            _chargeLeft -= Time.fixedDeltaTime;
            Vector3 v = _rb.linearVelocity;
            _rb.linearVelocity = new Vector3(_chargeDir.x * 7f, v.y, _chargeDir.z * 7f);
            transform.rotation = Quaternion.LookRotation(_chargeDir);
            if (_chargeLeft <= 0f) EndCharge(false);
        }

        void OnCollisionEnter(Collision col)
        {
            if (!_charging) return;
            if (col.collider.attachedRigidbody == null && col.collider.GetComponent<CharacterController>() == null)
            {
                // met an immovable object: the wall wins, always
                EndCharge(true);
                return;
            }

            // bowled someone over
            var creature = col.collider.GetComponentInParent<Creature>();
            if (creature != null && creature != _creature)
            {
                creature.KnockDown(2.5f);
                creature.GetComponent<ZombieBrain>()?.GetMadAt(transform); // they remember this
            }
            var player = col.collider.GetComponentInParent<SimpleFPSController>();
            if (player != null)
                player.TakeHit(_chargeDir * 12f + Vector3.up * 4f, AttackDamage * 2f);
            var rb = col.collider.attachedRigidbody;
            if (rb != null) rb.AddForce(_chargeDir * 5f + Vector3.up * 2f, ForceMode.VelocityChange);
        }

        void EndCharge(bool ateWall)
        {
            _charging = false;
            _chargeCooldown = Random.Range(4f, 7f);
            if (ateWall)
            {
                _creature.KnockDown(3f);       // self-stun, dizzy pupils orbiting
                _brain.Mumble("@#$%!", 2.5f);
            }
        }

        // --------------------------------------------------------- scribbler --
        void TickScribbler(Transform target, float dist)
        {
            _castCooldown -= Time.fixedDeltaTime;

            // keeps its distance like a coward
            if (dist < 5f) { _brain.MoveDir = (transform.position - target.position).normalized; _brain.SpeedScale = 1.2f; }
            else _brain.SpeedScale = Mathf.Min(_brain.SpeedScale, 0.4f);

            if (_castCooldown > 0f) return;
            _castLeft += Time.fixedDeltaTime;
            if (_castLeft < 4f)
            {
                if (_castLeft < 0.1f) _brain.Mumble("SKRTCH SKRTCH", 3.5f);
                DrawCastRing(_castLeft / 4f);
                return;
            }

            // the curse lands: real chemistry, dropped on your position
            _castLeft = 0f;
            _castCooldown = Random.Range(6f, 10f);
            ClearCastRing();
            if (Random.value < 0.5f)
                Matter.Spawn(SurfaceMaterialType.Stone, MatterPhase.Solid, 0.7f,
                    target.position + Vector3.up * 4f);           // anvil, classic
            else
                Matter.Spawn(SurfaceMaterialType.Stone, MatterPhase.Liquid, 0.45f,
                    target.position + Vector3.up * 0.4f);          // lava at your feet
            WorldEvents.Report(WorldEventKind.Spell, target.position, 2f);
        }

        void DrawCastRing(float t)
        {
            if (_castRing == null)
            {
                var go = new GameObject("CastRing");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0.1f, 0f);
                _castRing = go.AddComponent<LineRenderer>();
                _castRing.loop = true;
                _castRing.widthMultiplier = 0.05f;
                _castRing.positionCount = 24;
                _castRing.useWorldSpace = false;
                _castRing.sharedMaterial = MatterFX.Get(new Color(1f, 0.3f, 0.9f, 0.9f), MoteShade.Additive);
            }
            float r = 0.4f + t * 0.8f;
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                _castRing.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }

        void ClearCastRing()
        {
            if (_castRing != null) Destroy(_castRing.gameObject);
            _castRing = null;
        }

        void TickIdleKind()
        {
            _windup = 0f;
            if (_castLeft > 0f) { _castLeft = 0f; ClearCastRing(); } // forgot mid-cast
        }

        // ------------------------------------------------------------ damage --
        void OnDamaged(float amount, string cause)
        {
            if (amount >= 6f) _dress?.Hit(); // burn ticks are too small to flinch

            // a BIG single hit ragdolls the zombie
            if (amount >= 18f && _creature != null)
                _creature.KnockDown(Mathf.Min(3.5f, 1.2f + amount / 25f));

            // pain interrupts the scribbler's cast
            if (Kind == ZombieKind.Scribbler && _castLeft > 0f)
            {
                _castLeft = 0f;
                _castCooldown = 3f;
                ClearCastRing();
                _brain.Mumble("@#$%!", 2f);
                _brain.Eyes?.SetMood(EyeMood.Scared, 2f);
            }
        }

        static readonly Collider[] _boomHits = new Collider[48];

        /// THE CURSED INK GOING OFF (Marko Aug 10). A seal closed on a zombie by
        /// an acolyte blows it: the same gas it was already breathing, at
        /// SummonGasDetonateMul — his flat "always 3x larger" — and this one
        /// SHOVES, which is the whole point. His words: "a detonation makes a
        /// much bigger cloud that SHOVES people away, which is how you steal the
        /// cauldron", and the play he wants is a wizard chasing a zombie and
        /// getting it blown in his face.
        ///
        /// Both dials arrive from the seal: bigger seal = bigger explosion,
        /// fewer lines = more potent ("a triangle on a zombie is a really potent
        /// poison"). Size widens the blast, potency drives what it does to you.
        public void Detonate(float sizeMul, float potency)
        {
            // 3x THE DEATH CLOUD, not 3x the living aura — the aura is only a
            // body-width now, so multiplying that would have quietly shrunk
            // every detonation to nothing.
            var summoned = GetComponent<SummonedZombie>();
            float radius = (summoned != null ? summoned.GasRadius : DrawingConfig.SummonGasRadiusMin)
                * DrawingConfig.SummonGasDetonateMul * Mathf.Max(0.2f, sizeMul);

            // AND NEVER SMALLER THAN THE THING THAT BLEW UP (Marko Aug 10: "it
            // should be larger than the zombie body at least 3x"). The gas
            // radius comes off the rune-to-seal ratio, which knows nothing about
            // how big the body is — so a fat zombie with a small rune could
            // detonate inside its own silhouette. Measured off the body it
            // actually has: DIAMETER at least 3x its height, hence 1.5x here.
            float bodyHeight = transform.localScale.y * 2f;
            radius = Mathf.Max(radius, bodyHeight * DrawingConfig.DetonateBodyMul);
            Vector3 at = transform.position + Vector3.up * bodyHeight * 0.35f;

            // IT LEAVES THE GROUND POISONED — one zone, standing where the
            // zombie stood, at his flat 3x. The field draws its own dome, ring
            // and skin and fades on its own clock, so there is nothing here to
            // hand-roll: no puff cadence, no sprite scaling, no lifetime timer.
            PoisonField.Open(at, radius, DrawingConfig.DetonateFieldSeconds);
            Juice.Thud(at);

            float shove = DrawingConfig.DetonateShove * potency;

            // ⛔ PLAYERS BY LIST, PROPS BY QUERY — and this split IS the fix.
            // The old single OverlapSphere used Physics.DefaultRaycastLayers,
            // which is ~(1 << 2), while every player collider lives on layer 2
            // on purpose ("the pen ignores our own body"). So the player branch
            // below was unreachable code: only furniture ever flew, and the
            // blast in your face moved you nowhere. Same root cause as the gas.
            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || p.IsDowned) continue;
                Vector3 away = p.transform.position - at;
                away.y = 0f;
                float dist = away.magnitude;
                if (dist > radius) continue;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;

                // falls off with distance, so standing at the edge shoves you
                // rather than deleting you
                float t = 1f - Mathf.Clamp01(dist / Mathf.Max(0.1f, radius));

                // THE BLAST IS PHYSICS, THE POISON IS CORRUPTION (Marko Aug 10:
                // "it shouldn't deal damage to Acolytes[,] the explosion itself
                // should push acolytes away if caught in the blast as well").
                // The shove reaches EVERYONE — an acolyte who stands too close
                // to their own trick still gets thrown — while the poison passes
                // through their corrupted body. Asked PER VICTIM, so this stays
                // right when the other side is a different player.
                bool corrupted = Sides.IsAcolytePlayer(p);

                // through Shove, not TakeHit — a blast this size has to break
                // your drawing before the push can land, or his controller
                // zeroes it the same frame
                Shove.Hit(p,
                    away.normalized * shove * t + Vector3.up * shove * 0.3f * t,
                    corrupted ? 0f : DrawingConfig.DetonateDamage * potency * t,
                    "a zombie went off");
            }

            // loose props ride the blast too — the cloud you cannot see through
            // plus furniture in the air is the escape window. Props sit on
            // ordinary layers, so the query mask is fine for them.
            int n = Physics.OverlapSphereNonAlloc(at, radius, _boomHits,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (_boomHits[i] == null) continue;
                var rb = _boomHits[i].attachedRigidbody;
                if (rb != null && !rb.isKinematic && rb != _rb)
                    rb.AddExplosionForce(shove * 18f, at, radius, 0.4f, ForceMode.Impulse);
            }

            // it was a spell; spells end. OnDeath adds its own small puff on top,
            // which reads as the corpse gassing off inside the bigger blast.
            _dmg2?.TakeDamage(999999f, "detonated");
        }

        void OnDeath(string cause)
        {
            WorldEvents.Report(WorldEventKind.Death, transform.position, 2f); // others hear a buddy pop
            RoundDirector.NotifyKill(this); // round economy: kills are the ink mine
            Juice.Pop(transform.position);

            // A CORPSE GASSES OFF (Marko Aug 10: "when zombie dies it should
            // create a poison cloud just not as explosive and large as when
            // detonated"). It is the SAME cloud it was already breathing, at the
            // SAME radius — dying just makes it visible as an event. The
            // detonation is the loud version at SummonGasDetonateMul (3x), which
            // is why this one is deliberately not scaled up at all.
            // THE CORPSE IS THE CLOUD (Marko Aug 10). Alive it only carried a
            // body-tight aura; everything the rune-to-seal ratio bought is
            // released HERE. That is what makes killing one a decision instead
            // of a reward, and it is why a courtyard where a fight happened
            // stays poisoned afterwards — the atmosphere is earned, not ambient.
            var mine = GetComponent<SummonedZombie>();
            PoisonField.Open(
                transform.position + Vector3.up * transform.localScale.y * 0.35f,
                // a world zombie has no seal behind it, so no ratio — it gets the
                // small end of the range rather than a number invented for it
                mine != null ? mine.GasRadius : DrawingConfig.SummonGasRadiusMin,
                DrawingConfig.ZombieDeathCloudSeconds);

            // NO CARD DROPS. Runes are learned by ANALYZING NATURAL OBJECTS with
            // the grimoire (his acquisition ruling), not by picking loot off a
            // corpse. Floating pickups were replaced long ago and this drop was
            // the last place still spawning them.
            Grimoire.Drop(OwnerId);
        }
    }
}
