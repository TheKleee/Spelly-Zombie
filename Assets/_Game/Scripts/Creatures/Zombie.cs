using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    // Charger doubles as the spec's "brute", Scribbler as the "spitter".
    public enum ZombieKind { Walker, Charger, Scribbler, Runner, Swarm }

    /// The design's enemy: slow enough that you have room to draw, physical
    /// enough that every spell works on it, and DUMB in a legible way (see
    /// ZombieBrain — three memory slots and a mumble vocabulary). Three kinds:
    ///   Walker    — shambles at you, swipes
    ///   Charger   — winds up, sprints in a LOCKED straight line, bowls over
    ///               everything including itself when it meets a wall
    ///   Scribbler — keeps distance and scrawls a curse: real conjured matter
    ///               (a stone block over your head, lava at your feet).
    ///               Hitting it interrupts the cast.
    /// All of them are Flesh-tagged rigidbodies, so the whole chemistry applies.
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

        /// Live registry — cheaper than FindObjectsByType in per-tick paths,
        /// and the RoundDirector's alive count.
        public static readonly List<Zombie> All = new List<Zombie>();

        static readonly Color SkinColor = new Color(0.45f, 0.62f, 0.35f);
        static readonly Color ChargerColor = new Color(0.6f, 0.45f, 0.3f);
        static readonly Color ScribblerColor = new Color(0.5f, 0.35f, 0.72f); // wizard purple
        static readonly Color HatColor = new Color(0.28f, 0.16f, 0.45f);
        static readonly Color RunnerColor = new Color(0.72f, 0.68f, 0.35f);  // sickly sprinter yellow
        static readonly Color SwarmColor = new Color(0.3f, 0.45f, 0.25f);    // little dark gremlins

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

        public static Zombie Spawn(Vector3 pos)
        {
            float r = Random.value; // sandbox mix (rounds use RoundDirector's table)
            return Spawn(pos, r < 0.45f ? ZombieKind.Walker
                : r < 0.70f ? ZombieKind.Charger : ZombieKind.Scribbler);
        }

        public static Zombie Spawn(Vector3 pos, ZombieKind kind, float speedMul = 1f)
        {
            // B4: zombies exist ONLY on the host — clients get NetZombieProxy
            // stand-ins from the snapshot stream (so a client-side void rift
            // summons nothing real; the host's world is the truth)
            if (NetGame.Connected && !NetGame.IsHost) return null;

            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Zombie_" + kind;
            go.transform.position = pos + Vector3.up * 1.1f;
            go.transform.localScale =
                kind == ZombieKind.Charger ? new Vector3(0.9f, 0.95f, 0.9f)   // stocky
                : kind == ZombieKind.Runner ? new Vector3(0.5f, 1.05f, 0.5f)  // lanky
                : kind == ZombieKind.Swarm ? new Vector3(0.42f, 0.5f, 0.42f)  // gremlin
                : new Vector3(0.7f, 1f, 0.7f);
            Color skin = kind == ZombieKind.Charger ? ChargerColor
                : kind == ZombieKind.Scribbler ? ScribblerColor
                : kind == ZombieKind.Runner ? RunnerColor
                : kind == ZombieKind.Swarm ? SwarmColor : SkinColor;
            go.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(skin, MoteShade.Opaque);

            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            Destroy(head.GetComponent<Collider>());
            head.transform.SetParent(go.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.05f, 0.05f);
            head.transform.localScale = new Vector3(0.55f, 0.4f, 0.55f);
            head.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(skin * 1.15f, MoteShade.Opaque);

            // the scribbler is unmistakably The Wizard: pointy hat, crooked tip
            if (kind == ZombieKind.Scribbler)
            {
                AddHatPart(go.transform, new Vector3(0f, 1.32f, 0.05f), new Vector3(0.72f, 0.05f, 0.72f), 0f);   // brim
                AddHatPart(go.transform, new Vector3(0f, 1.45f, 0.05f), new Vector3(0.4f, 0.22f, 0.4f), 4f);     // base
                AddHatPart(go.transform, new Vector3(0.03f, 1.64f, 0.05f), new Vector3(0.24f, 0.2f, 0.24f), 10f); // mid
                AddHatPart(go.transform, new Vector3(0.08f, 1.8f, 0.05f), new Vector3(0.1f, 0.18f, 0.1f), 22f);  // crooked tip
            }

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.mass = kind == ZombieKind.Charger ? 110f : kind == ZombieKind.Swarm ? 25f : 70f;
            rb.constraints = RigidbodyConstraints.FreezeRotation;

            var dmg = go.AddComponent<Damageable>();
            dmg.Health = kind == ZombieKind.Charger ? 90f
                : kind == ZombieKind.Runner ? 30f
                : kind == ZombieKind.Swarm ? 12f : 60f;

            var creature = go.AddComponent<Creature>();
            go.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Flesh;
            go.AddComponent<PersistentInkSurface>(); // runes drawn ON zombies ride them and persist

            // the googly soul — mounted on the head so it wobbles with every step
            var eyes = GooglyEyes.Attach(head.transform, 0f, 2.2f);

            var brain = go.AddComponent<ZombieBrain>();
            brain.Eyes = eyes;
            // capacity IS intelligence: charger's whole world is the last thing
            // that happened; the scribbler is the horde's intellectual
            brain.Capacity = kind == ZombieKind.Charger || kind == ZombieKind.Swarm ? 1
                : kind == ZombieKind.Scribbler ? 5 : 3;
            if (kind == ZombieKind.Runner) { brain.SightRange = 17f; brain.HearRange = 12f; } // skittish

            var z = go.AddComponent<Zombie>();
            z.Kind = kind;
            switch (kind) // body stats — the roster is mostly numbers on one bean
            {
                case ZombieKind.Runner: z.WalkSpeed = 3.4f; z.AttackDamage = 6f; z.AttackCooldown = 0.7f; break;
                case ZombieKind.Swarm: z.WalkSpeed = 2.3f; z.AttackDamage = 3f; z.AttackCooldown = 0.5f; z.AttackRange = 0.9f; break;
                case ZombieKind.Charger: z.AttackDamage = 14f; break;
            }
            z.WalkSpeed *= speedMul; // rounds make everything faster
            dmg.OnDeath += z.OnDeath;
            dmg.OnDamaged += z.OnDamaged;

            // every zombie carries rune cards (scribblers carry two — they're
            // the ones who actually use them, and the juiciest to hunt)
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

            // the wardrobe: the shared character model + zombie animations,
            // following the physics capsule (graybox continues if not wired)
            float widthMul = kind == ZombieKind.Charger ? 1.25f
                : kind == ZombieKind.Runner ? 0.72f : 1f;
            z._dress = ZombieDress.DressUp(z, skin, widthMul, eyes);
            return z;
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

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _creature = GetComponent<Creature>();
            _brain = GetComponent<ZombieBrain>();
            All.Add(this);
        }

        void OnDestroy() => All.Remove(this);

        /// Walk WITHOUT erasing physics. The old code hard-set velocity every
        /// tick, which silently deleted every spell push within 0.02s — zombies
        /// looked immune to force. Now legs steer toward intent with limited
        /// grip (barely any mid-air), so knockbacks, launches and explosions
        /// visibly win until the zombie recovers its footing.
        void Steer(Vector3 dir, float speed)
        {
            Vector3 v = _rb.linearVelocity;
            Vector3 horiz = new Vector3(v.x, 0f, v.z);
            Vector3 desired = new Vector3(dir.x, 0f, dir.z);
            if (desired.sqrMagnitude > 0.01f) desired = desired.normalized * speed;
            else desired = Vector3.zero;

            bool grounded = Physics.Raycast(transform.position, Vector3.down, 1.35f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float grip = grounded ? 14f : 2f;

            Vector3 blended = Vector3.MoveTowards(horiz, desired, grip * Time.fixedDeltaTime);
            _rb.linearVelocity = new Vector3(blended.x, v.y, blended.z);
        }

        void FixedUpdate()
        {
            if (_creature == null || _brain == null) return;

            if (_charging) { TickCharge(); return; }
            if (!_creature.CanMove) return;

            // someone is drawing on it: TRANCE. Full stop — no walking, no
            // swipes, no compulsion, no windup. Just bliss. (Steered, not
            // hard-set: a fireball still sends a tranced zombie flying.)
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

        /// The scribbler's disease: it completes spells. ANY open ink — yours,
        /// another zombie's, ink drawn ON a zombie — gets a circle drawn around
        /// it, closing a REAL seal owned by this zombie, cast with ITS cards.
        /// (Draw a heat rune on a zombie's back and wait.) Also tags nearby
        /// buddies with runes from its own deck, so the next scribbler that
        /// walks past has something to circle. Returns true while busy.
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
                    Debug.Log("[SpellyZombie] A scribbler locked onto a doodle — it MUST complete it.");
                }
            }

            // no doodle yet, but ink is flowing somewhere? fresh ink is a MAGNET —
            // the scribbler shuffles toward the pen (this is the decoy loop: it
            // arrives right as you finish, and then it simply must join in)
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

            // from time to time the wizard casts ITS OWN SEAL on whatever is in
            // front — wall, crate, another zombie — else the floor. One glyph per
            // card it carries, then the circle: free intel on its whole deck.
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

            // nothing to complete, nothing flowing: the wizard doodles anyway.
            // It scrawls runes from its own deck on the ground — graffiti that
            // other scribblers will find and compulsively circle (zombie-cast
            // spells with no player involved), and that YOU can seal to steal
            // a cast if you own the card.
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
        // The CoD flow: a zombie that WANTS to walk but is bodily blocked by
        // something breakable (fence, window insert, door, even a conjured
        // wall) swipes it apart instead of shuffling in place. Solid walls
        // stay walls — the patrol repick handles those.
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
            if (!Physics.Raycast(transform.position + Vector3.up * 0.2f, dir, out var hit, 1.3f,
                    mask, QueryTriggerInteraction.Ignore)) return;

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

            // a BIG single hit ragdolls the zombie — it tumbles, flails, and
            // struggles back up (unless it doesn't get the chance)
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

        void OnDeath(string cause)
        {
            WorldEvents.Report(WorldEventKind.Death, transform.position, 2f); // others hear a buddy pop
            RoundDirector.NotifyKill(this); // round economy: kills are the ink mine
            Juice.Pop(transform.position);

            // it drops the ACTUAL cards it carried — kill the zombie whose rune
            // you need (bait-test it into circling your glyph to find out)
            foreach (var card in Cards)
                RuneCardPickup.Spawn(transform.position + Vector3.up * 0.5f + Random.insideUnitSphere * 0.3f, card);
            Grimoire.Drop(OwnerId);
        }
    }
}
