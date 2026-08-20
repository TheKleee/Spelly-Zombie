using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    public enum ZombieKind { Walker, Charger, Scribbler, Runner, Swarm }

    /// Zombie body: Flesh-tagged rigidbody; decisions live in ZombieBrain.
    [RequireComponent(typeof(Rigidbody))]
    public class Zombie : MonoBehaviour
    {
        public ZombieKind Kind = ZombieKind.Walker;
        public float WalkSpeed = 1.3f;
        public float AttackRange = 1.4f;
        public float AttackDamage = 10f;
        public float AttackCooldown = 1.2f;

        /// Rune cards this zombie owns; used when it completes a seal.
        public readonly List<RuneCardType> Cards = new List<RuneCardType>();

        /// Owner id in the Grimoire (seal ownership + rune checks).
        public int OwnerId => gameObject.GetInstanceID();

        /// Set by Demon on attach; demons don't count toward ending a round.
        public bool IsDemon;

        /// Live registry; the RoundDirector's alive count.
        public static readonly List<Zombie> All = new List<Zombie>();

        static readonly Color SkinColor = new Color(0.45f, 0.62f, 0.35f);
        static readonly Color ChargerColor = new Color(0.6f, 0.45f, 0.3f);
        static readonly Color ScribblerColor = new Color(0.5f, 0.35f, 0.72f); // wizard purple
        static readonly Color HatColor = new Color(0.28f, 0.16f, 0.45f);
        static readonly Color RunnerColor = new Color(0.72f, 0.68f, 0.35f);  // sickly sprinter yellow
        static readonly Color SwarmColor = new Color(0.3f, 0.45f, 0.25f);    // little dark gremlins

        /// Shared kindlook table - NetZombieProxy reads these so host and client visuals can never drift.
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

        // full-seal scrawl: runes first, circle a beat later
        float _sealScrawl, _scrawlCircleIn;
        Vector3 _scrawlCenter, _scrawlNormal;
        float _scrawlRadius;
        Transform _scrawlSurface;

        public static Zombie Spawn(Vector3 pos, ZombieKind kind, float speedMul = 1f)
        {
            // zombies exist only on the host; clients get NetZombieProxy stand-ins
            if (NetGame.Connected && !NetGame.IsHost) return null;

            Color skin = KindSkin(kind);
            GameObject go, head;

            // prefab override: Zombie_<kind> checked first, then Zombie; the kind
            // scale multiplies the authored scale, components below fill gaps only
            var custom = PrefabVault.Get("Zombie_" + kind) ?? PrefabVault.Get("Zombie");
            bool graybox = custom == null;

            if (!graybox)
            {
                go = Instantiate(custom);
                go.name = "Zombie_" + kind;
                go.transform.position = pos + Vector3.up * 1.1f;

                // kind shape relative to a Walker, applied on top of the authored scale
                Vector3 rel = RawKindScale(kind);
                Vector3 baseK = RawKindScale(ZombieKind.Walker);
                go.transform.localScale = Vector3.Scale(go.transform.localScale,
                    new Vector3(rel.x / baseK.x, rel.y / baseK.y, rel.z / baseK.z))
                    * DrawingConfig.ZombieBodyScale;

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

            // ScribblerHat prefab replaces the code-built hat cubes
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

            // components are added only if missing; per-kind stats apply only to code-created ones
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

            // prefab eyes if present, otherwise code-built ones on the head
            var eyes = go.GetComponentInChildren<GooglyEyes>(true)
                ?? GooglyEyes.Attach(head.transform, 0f, DrawingConfig.ZombieEyeScale);

            var brain = Adopt.Component<ZombieBrain>(go, out bool brainNew);
            brain.Eyes = eyes;
            if (brainNew)
            {
                // memory slot count per kind
                brain.Capacity = kind == ZombieKind.Charger || kind == ZombieKind.Swarm ? 1
                    : kind == ZombieKind.Scribbler ? 5 : 3;
                if (kind == ZombieKind.Runner) { brain.SightRange = 17f; brain.HearRange = 12f; } // skittish
            }

            var z = Adopt.Component<Zombie>(go, out bool zNew);
            z.Kind = kind;
            if (zNew)
            {
                switch (kind) // per-kind body stats
                {
                    case ZombieKind.Runner: z.WalkSpeed = 3.4f; z.AttackDamage = 6f; z.AttackCooldown = 0.7f; break;
                    case ZombieKind.Swarm: z.WalkSpeed = 2.3f; z.AttackDamage = 3f; z.AttackCooldown = 0.5f; z.AttackRange = 0.9f; break;
                    case ZombieKind.Charger:
                        z.AttackDamage = 14f;
                        // the name finally means something: the shared charge,
                        // same hop-then-committed-line a golem uses
                        var ch = z.gameObject.GetComponent<ChargeAttack>();
                        if (ch == null) ch = z.gameObject.AddComponent<ChargeAttack>();
                        ch.Damage = z.AttackDamage;
                        break;
                }
            }
            z.WalkSpeed *= speedMul; // rounds make everything faster
            dmg.OnDeath += z.OnDeath;
            dmg.OnDamaged += z.OnDamaged;

            // every zombie carries rune cards; scribblers carry two
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

            // wardrobe: shared model follows the capsule; a prefab body is already dressed
            if (graybox)
            {
                float widthMul = kind == ZombieKind.Charger ? 1.25f
                    : kind == ZombieKind.Runner ? 0.72f : 1f;
                z._dress = ZombieDress.DressUp(z, skin, widthMul, eyes);
            }
            z.RiseFromGrave();
            return z;
        }

        /// Spawn rise: plays the StandUp clip with the brain tranced for its
        /// duration. Tune ZombieRiseSeconds to the clip.
        public void RiseFromGrave()
        {
            if (_brain == null) _brain = GetComponent<ZombieBrain>();
            if (_brain != null)
                _brain.TrancedUntil = Mathf.Max(_brain.TrancedUntil,
                    Time.time + DrawingConfig.ZombieRiseSeconds);
            StartCoroutine(DigUp());
        }

        bool _rising;

        /// Snap to the ground, bury the body, climb out over ZombieRiseSeconds.
        System.Collections.IEnumerator DigUp()
        {
            _rising = true;
            if (_rb == null) _rb = GetComponent<Rigidbody>();

            // probe from just above the feet; accept only floor-like hits
            if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down,
                    out var hit, 40f,
                    Physics.DefaultRaycastLayers & ~(1 << InkCanvasLayer.Layer),
                    QueryTriggerInteraction.Ignore)
                && hit.normal.y > 0.55f)
                transform.position = hit.point;

            Vector3 surface = transform.position;
            float depth = Mathf.Max(0.8f, transform.localScale.y * 2.2f);

            bool wasKinematic = _rb != null && _rb.isKinematic;
            if (_rb != null) _rb.isKinematic = true;
            var cols = GetComponentsInChildren<Collider>();
            foreach (var c in cols) if (c != null) c.enabled = false;

            transform.position = surface - Vector3.up * depth;
            if (FxLibrary.I != null)
                FxLibrary.Spawn(FxLibrary.I.GroundHit, surface);

            float dur = Mathf.Max(0.4f, DrawingConfig.ZombieRiseSeconds);
            float t = 0f;
            bool burst = false;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = 1f - (1f - Mathf.Clamp01(t / dur)) * (1f - Mathf.Clamp01(t / dur));
                if (!burst && k > 0.3f)
                {
                    burst = true;
                    if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.GroundHit, surface);
                    Juice.Thud(surface);
                    _dress?.Hit();
                }
                transform.position = surface - Vector3.up * (depth * (1f - k));
                yield return null;
            }
            transform.position = surface;

            foreach (var c in cols) if (c != null) c.enabled = true;
            if (_rb != null) _rb.isKinematic = wasKinematic;
            _rising = false;
        }

        /// First child whose name contains `name`, case-insensitive.
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

        /// Pen on this zombie pins it; SurfaceDrawer calls this every inked frame.
        /// The dress settles into the rest pose so the paint-shell collider and
        /// the visible mesh stay the same shape while drawing.
        public void PaintFreeze(float seconds)
        {
            if (_brain != null)
                _brain.TrancedUntil = Mathf.Max(_brain.TrancedUntil, Time.time + seconds);
            _dress?.PaintHold(seconds);
        }

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

        // ---- ghost possession ----

        /// True while an acolyte ghost drives this one. Suppresses the brain.
        public bool Possessed { get; private set; }

        /// Flattened look direction from the driving ghost; the body faces it.
        public Vector3 PossessedFace { get; set; }

        /// Where the driving camera sits: the googly eyes ride the visible
        /// head on both the graybox and the dressed rig.
        public Vector3 HeadAt
        {
            get
            {
                var eyes = _brain != null ? _brain.Eyes : null;
                return eyes != null ? eyes.transform.position
                    : transform.position + Vector3.up * (transform.localScale.y * 0.95f);
            }
        }

        public void PossessBy(bool on)
        {
            Possessed = on;
            if (_brain != null && on) _brain.AttackTarget = null;
            // the driver sits inside the head: the eyes get out of the lens
            var e = _brain != null ? _brain.Eyes : null;
            if (e != null)
                foreach (var r in e.GetComponentsInChildren<Renderer>(true))
                    r.enabled = !on;
        }

        /// Fires this kind's attack: scribblers throw the curse, the rest
        /// charge. False if it has nothing to fire or is on cooldown.
        public bool GhostAbility(Vector3 aimDir)
        {
            if (_creature == null || _brain == null) return false;
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude < 0.01f) aimDir = transform.forward;
            aimDir.Normalize();

            // ranged = the Scribbler kind, or a summon born from Liquid
            var summon = GetComponent<SummonedZombie>();
            if (Kind == ZombieKind.Scribbler || (summon != null && summon.Ranged))
            {
                if (_castCooldown > 0f) return false;
                _castCooldown = 1.4f;
                ClearCastRing();
                _dress?.Attack();
                StartCoroutine(GooThrow(aimDir));
                _brain.Mumble("PTOO!", 1.5f);
                return true;
            }

            if (_chargeCooldown > 0f || _charging) return false;
            _chargeCooldown = 4f;
            _dress?.Scream();
            _charging = true;
            _chargeLeft = 2.2f;
            _chargeDir = aimDir;
            _brain.Mumble("RRAAH!", 1.5f);
            return true;
        }

        static readonly Color GooGreen = new Color(0.45f, 0.85f, 0.2f);

        /// The goo leaves the zombie's arm in an arc; wherever it comes to
        /// rest it splashes green and opens a short poison puddle.
        System.Collections.IEnumerator GooThrow(Vector3 aim)
        {
            Vector3 hand = transform.position
                + Vector3.up * (transform.localScale.y * 1.1f)
                + transform.forward * 0.45f;
            var goo = Matter.Spawn(SurfaceMaterialType.Stone, MatterPhase.Liquid, 0.35f, hand);
            if (goo == null) yield break;
            foreach (var r in goo.GetComponentsInChildren<Renderer>())
                PillarBeam.Tint(r, GooGreen);

            var rb = goo.GetComponent<Rigidbody>();
            if (rb != null) rb.linearVelocity = aim * 19f + Vector3.up * 5f;

            float life = 0f;
            while (goo != null && life < 3f)
            {
                life += Time.deltaTime;
                if (life > 0.25f && rb != null && rb.linearVelocity.sqrMagnitude < 1f)
                    break; // it landed
                yield return null;
            }
            if (goo == null) yield break; // the chemistry ate it mid-flight

            Vector3 land = goo.transform.position;
            if (FxLibrary.I != null)
                FxLibrary.SpawnTinted(FxLibrary.I.Splash, land, GooGreen);
            PoisonField.Open(land + Vector3.up * 0.4f, 1.4f, 4f);
            WorldEvents.Report(WorldEventKind.Spell, land, 2f);
        }

        /// Steer with limited grip instead of hard-setting velocity, so external forces still win.
        void Steer(Vector3 dir, float speed)
        {
            Vector3 v = _rb.linearVelocity;
            Vector3 horiz = new Vector3(v.x, 0f, v.z);
            Vector3 desired = new Vector3(dir.x, 0f, dir.z);
            if (desired.sqrMagnitude > 0.01f) desired = desired.normalized * speed;
            else desired = Vector3.zero;

            // ground probe: reach scales with the capsule half-height (localScale.y);
            // ink-canvas layer masked out so paint shells never count as floor
            bool grounded = Physics.Raycast(transform.position, Vector3.down,
                transform.localScale.y + 0.53f,
                Physics.DefaultRaycastLayers & ~(1 << InkCanvasLayer.Layer),
                QueryTriggerInteraction.Ignore);
            float grip = grounded ? 14f : 2f;

            Vector3 blended = Vector3.MoveTowards(horiz, desired, grip * Time.fixedDeltaTime);
            _rb.linearVelocity = new Vector3(blended.x, v.y, blended.z);
        }

        void FixedUpdate()
        {
            if (_creature == null || _brain == null) return;

            // below KillY it dies where it fell (drops and kill credit, no teleport)
            if (transform.position.y < FallCatcher.KillY)
            {
                _dmg2?.TakeDamage(99999f, "the void");
                return;
            }

            // no physics or decisions while still climbing out of the ground
            if (_rising) return;

            if (_charging) { TickCharge(); return; }
            if (!_creature.CanMove) return;

            // while held by a grab: no steering, no turning, no chewing
            if (HandGrab.LocalHeldBody == _rb) { _windup = 0f; return; }

            // trance: full stop (steered, not hard-set, so forces still apply)
            if (_brain.Tranced)
            {
                Steer(Vector3.zero, 0f);
                _windup = 0f;
                return;
            }

            // ---- apply the brain's decision ----
            float speed = WalkSpeed * _brain.SpeedScale * _creature.SpeedMultiplier;
            if (_brain.MoveDir.sqrMagnitude > 0.01f && speed > 0.01f)
            {
                Steer(_brain.MoveDir, speed);
                // a driven body faces the ghost's look so it can strafe
                Vector3 face = Possessed && PossessedFace.sqrMagnitude > 0.01f
                    ? PossessedFace : _brain.MoveDir;
                if (_creature.SpeedMultiplier >= 0.5f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(face), Time.fixedDeltaTime * 4f);
                TryChewObstacle(speed); // breakable obstacles get chewed through
            }

            // possessed: no own decisions, but attack cooldowns keep ticking
            if (Possessed)
            {
                _castCooldown -= Time.fixedDeltaTime;
                _chargeCooldown -= Time.fixedDeltaTime;
                if (PossessedFace.sqrMagnitude > 0.01f && _brain.MoveDir.sqrMagnitude <= 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(PossessedFace), Time.fixedDeltaTime * 6f);
                AutoSwipe();
                return;
            }

            // scribbler compulsion runs before combat
            if (Kind == ZombieKind.Scribbler && TickCompulsion()) return;

            var target = _brain.AttackTarget;
            if (target == null) { TickIdleKind(); AutoSwipe(); return; }
            float dist = Vector3.Distance(transform.position, target.position);

            switch (Kind)
            {
                case ZombieKind.Walker: TrySwipe(target, dist); break;
                case ZombieKind.Charger: TickChargerWindup(target, dist); break;
                case ZombieKind.Scribbler: TickScribbler(target, dist); break;
            }
        }

        /// Scribbler: completes open ink into a real seal with its cards, tags buddies with runes. Returns true while busy.
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

            // fresh ink lures it (decoy behavior)
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

                ClearCastRing();
                var surface = _doodle.First != null ? _doodle.First.transform.parent : null;
                bool closed = ZombieScribe.DrawCircle(center, normal, radius, surface, OwnerId);
                _brain.Mumble(closed ? "MMM: DONE." : "GRUH?!", 2.5f);
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
                    _brain.Mumble(closed ? "HMM. MINE." : "GRUH?!", 2.5f);
                }
                return true;
            }

            // occasionally casts its own seal on whatever is in front
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

            // idle: doodles deck runes on the ground; players can seal them
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
        // blocked by a breakable: swipe it apart; solid walls are patrol repick's job
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
            // SphereCast from belly height so low fences are hit
            if (!Physics.SphereCast(transform.position + Vector3.down * 0.35f, 0.4f,
                    dir, out var hit, 1.6f, mask, QueryTriggerInteraction.Ignore)) return;

            if (hit.collider.GetComponentInParent<Creature>() != null) return;            // creatures excluded
            if (hit.collider.GetComponentInParent<SimpleFPSController>() != null) return; // players excluded
            var obstacle = hit.collider.GetComponentInParent<Damageable>();
            if (obstacle == null) return; // real wall - go around

            _chewTimer = AttackCooldown * 1.1f;
            _dress?.Attack();
            obstacle.TakeDamage(AttackDamage * 1.4f, $"{name} tearing through");
            Juice.Thud(hit.point);
            _brain.Mumble("RRAGH!!", 1.2f);
            _brain.Eyes?.SetMood(EyeMood.Mad, 1f);
        }

        float _autoScan;

        /// Bites whatever is in reach: players (never its summoner), occasionally other zombies.
        void AutoSwipe()
        {
            _autoScan -= Time.fixedDeltaTime;
            if (_autoScan > 0f) return;
            _autoScan = 0.4f;

            var mine = GetComponent<SummonedZombie>();
            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || p.IsDowned) continue;
                if (mine != null && p.IsLocalViewer && Grimoire.LocalPlayerId == mine.SummonedBy)
                    continue;
                float d = Vector3.Distance(transform.position, p.transform.position);
                if (d <= AttackRange) { TrySwipe(p.transform, d); return; }
            }
            foreach (var z in All)
            {
                if (z == this || z == null || z._rising) continue;
                float d = Vector3.Distance(transform.position, z.transform.position);
                if (d <= AttackRange && Random.value < 0.25f) { TrySwipe(z.transform, d); return; }
            }
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
            transform.position += new Vector3(tremble.x, 0f, tremble.y);
            if (_windup < 1f)
            {
                if (_windup < 0.1f)
                {
                    _brain.Mumble("HRRNK!!", 1.5f);
                    _brain.Eyes?.SetMood(EyeMood.Mad, 1.5f);
                    _dress?.Scream();
                }
                return;
            }

            // charge direction locks once started
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
                // hit something immovable
                EndCharge(true);
                return;
            }

            // bowled someone over
            var creature = col.collider.GetComponentInParent<Creature>();
            if (creature != null && creature != _creature)
            {
                creature.KnockDown(2.5f);
                creature.GetComponent<ZombieBrain>()?.GetMadAt(transform);
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
                _creature.KnockDown(3f);       // self-stun
                _brain.Mumble("@#$%!", 2.5f);
            }
        }

        // --------------------------------------------------------- scribbler --
        void TickScribbler(Transform target, float dist)
        {
            _castCooldown -= Time.fixedDeltaTime;

            // keeps its distance
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

            // the curse: real Matter dropped at the target
            _castLeft = 0f;
            _castCooldown = Random.Range(6f, 10f);
            ClearCastRing();
            _dress?.Attack();
            if (Random.value < 0.5f)
                Matter.Spawn(SurfaceMaterialType.Stone, MatterPhase.Solid, 0.7f,
                    target.position + Vector3.up * 4f);           // solid from above
            else
                Matter.Spawn(SurfaceMaterialType.Stone, MatterPhase.Liquid, 0.45f,
                    target.position + Vector3.up * 0.4f);          // liquid at the feet
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


        /// Seal closed on this zombie by an acolyte: a poison blast that shoves.
        /// sizeMul widens the blast; potency scales shove and damage.
        public void Detonate(float sizeMul, float potency)
        {
            // base radius is the death-cloud radius, not the body-tight living aura
            var summoned = GetComponent<SummonedZombie>();
            float radius = (summoned != null ? summoned.GasRadius : DrawingConfig.SummonGasRadiusMin)
                * DrawingConfig.SummonGasDetonateMul * Mathf.Max(0.2f, sizeMul);

            // never smaller than the body that blew up
            float bodyHeight = transform.localScale.y * 2f;
            radius = Mathf.Max(radius, bodyHeight * DrawingConfig.DetonateBodyMul);
            Vector3 at = transform.position + Vector3.up * bodyHeight * 0.35f;

            // leaves one poison field where it stood; the field owns its visuals and lifetime
            PoisonField.Open(at, radius, DrawingConfig.DetonateFieldSeconds);

            // shared blast: players shoved with falloff, acolytes never damaged,
            // own body excluded from the prop throw
            Shove.Blast(at, radius, DrawingConfig.DetonateShove * potency,
                DrawingConfig.DetonateDamage * potency, "a zombie went off", _rb);

            // the detonation kills it; OnDeath adds its small corpse cloud on top
            _dmg2?.TakeDamage(999999f, "detonated");
        }

        void OnDeath(string cause)
        {
            WorldEvents.Report(WorldEventKind.Death, transform.position, 2f); // nearby zombies hear it
            RoundDirector.NotifyKill(this); // round economy
            Juice.Pop(transform.position);

            // death releases the gas cloud at GasRadius, unscaled;
            // detonation is the SummonGasDetonateMul version
            var mine = GetComponent<SummonedZombie>();
            PoisonField.Open(
                transform.position + Vector3.up * transform.localScale.y * 0.35f,
                // world zombies (no seal) get the minimum radius
                mine != null ? mine.GasRadius : DrawingConfig.SummonGasRadiusMin,
                DrawingConfig.ZombieDeathCloudSeconds);

            // the summoner whistles at their own position; 3D falloff limits the tell
            if (mine != null) WhistleOwner(mine.SummonedBy);

            // no card drops; runes are learned by analyzing objects with the grimoire
            Grimoire.Drop(OwnerId);
        }

        static void WhistleOwner(int ownerId)
        {
            if (ownerId < 0) return;
            Vector3 at;
            if (Grimoire.LocalPlayerId == ownerId)
            {
                SimpleFPSController me = null;
                foreach (var p in SimpleFPSController.All)
                    if (p != null && p.IsLocalViewer) { me = p; break; }
                if (me == null || me.IsDowned) return;   // no living body, no tell
                at = me.transform.position;
            }
            else
            {
                // avatars key by CLIENT id; owner ids are client+1 (netcode §0)
                NetAvatar owner = null;
                foreach (var a in NetAvatar.All)
                    if (a != null && a.Id == ownerId - 1) { owner = a; break; }
                if (owner == null || owner.Downed) return;
                at = owner.transform.position;
            }
            at += Vector3.up * 1.4f;
            var clip = AudioLibrary.I != null ? AudioLibrary.I.AcolyteWhistle : null;
            if (clip != null) Juice.PlayClip(clip, at, 0.85f, Random.Range(0.95f, 1.05f));
            else Juice.Whistle(at);
        }
    }
}
