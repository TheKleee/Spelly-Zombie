using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Zombie body: Flesh-tagged rigidbody; decisions live in ZombieBrain.
    [RequireComponent(typeof(Rigidbody))]
    public class Zombie : MonoBehaviour
    {
        /// WHAT THIS ONE CAN DO. There are no kinds of zombie - a zombie is
        /// a zombie, and what separates them is the list of abilities it was
        /// summoned with. Melee gets charge, ranged gets throw, and the demon
        /// gets every spell there is, which is the whole reason this is a list
        /// and not a label.
        public readonly List<string> Abilities = new List<string>();

        public bool Can(string ability) => Abilities.Contains(ability);

        public const string Charge = "charge";

        /// ★ WEAR A BODY DEFINITION. Its natural state is what this zombie is
        /// born as - stamped onto the Element the way a biome stamps anything
        /// - and its abilities are what it can do. Its colour shades the green,
        /// its movement rides the body.
        public void Wear(SpellDef def)
        {
            if (def == null) return;
            Abilities.Clear();
            Abilities.AddRange(def.Abilities);

            var el = GetComponent<Element>();
            if (el != null)
            {
                var born = def.Payload;
                var n = el.Natural;
                for (int i = 0; i < SpellPayload.AxisCount; i++)
                    if (i != 6 && Mathf.Abs(born[i]) > 0.001f) n[i] = born[i];   // strength is the body's own
                if (born.Strength > 0f) n.Strength = born.Strength;
                if (n.Int <= 0f) n.Int = 1f;           // a zombie has a mind, whatever else it is
                if (n.Courage <= 0f) n.Courage = 1f;
                el.Natural = n;
                el.Data = n;
            }

            // refresh the cache: Awake ran at Spawn, before the summon
            // component was added, so OwnerId and the move clips read nothing
            _summon = GetComponent<SummonedZombie>();
            if (_summon != null) _summon.Spell = def;
        }

        public float WalkSpeed = 1.3f;
        public float AttackRange = 1.4f;
        public float AttackDamage = 10f;
        public float AttackCooldown = 1.2f;

        /// ★ THE ACOLYTE WHO SUMMONED THIS ZOMBIE. Every zombie has one -
        /// there are only two kinds, melee and ranged, and both are acolyte
        /// work. This is what lets an acolyte oversee their own zombies, and
        /// what makes a zombie's kill belong to somebody.
        ///
        /// It used to return the zombie's OWN instance id, from a dead design
        /// where zombies drew their own seals. They do not draw seals.
        public int OwnerId => _summon != null ? _summon.SummonedBy : -1;
        SummonedZombie _summon;

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

        /// ONE BODY. NetZombieProxy reads this so host and client can never
        /// drift. Melee and ranged are told apart by colour, which
        /// SummonedZombie paints, not by a different silhouette.
        public static Vector3 BodyScale => RawBodyScale * DrawingConfig.ZombieBodyScale;
        static Vector3 RawBodyScale => new Vector3(0.7f, 1f, 0.7f);
        public static Color BodySkin => SkinColor;

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

        public static Zombie Spawn(Vector3 pos, float speedMul = 1f)
        {
            // zombies exist only on the host; clients get NetZombieProxy stand-ins
            if (NetGame.Connected && !NetGame.IsHost) return null;

            Color skin = BodySkin;
            GameObject go, head;

            // ONE authored body, from the CollectionManager slot - kind is a
            // shape applied on top of it, not a separate prefab. It used to
            // come from Resources, where a stale copy quietly won.
            var custom = CollectionManager.ZombieBody;
            bool graybox = custom == null;

            if (!graybox)
            {
                go = Instantiate(custom);
                go.name = "Zombie";
                go.transform.position = pos;

                // kind shape relative to a Walker, applied on top of the authored scale
                go.transform.localScale *= DrawingConfig.ZombieBodyScale;

                head = FindNamed(go.transform, "Head") ?? go;   // eyes mount here
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "Zombie";
                go.transform.position = pos;
                go.transform.localScale = BodyScale;
                go.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(skin, MoteShade.Opaque);

                head = GameObject.CreatePrimitive(PrimitiveType.Cube);
                head.name = "Head";
                Destroy(head.GetComponent<Collider>());
                head.transform.SetParent(go.transform, false);
                head.transform.localPosition = new Vector3(0f, 1.05f, 0.05f);
                head.transform.localScale = new Vector3(0.55f, 0.4f, 0.55f);
                head.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(skin * 1.15f, MoteShade.Opaque);
            }


            // A BODY WITH NO COLLIDER FALLS STRAIGHT THROUGH THE WORLD. The
            // graybox capsule brought its own; an authored prefab need not, so
            // one is fitted to the mesh. Put a collider on the prefab and this
            // never runs - yours is always the one that is used.
            if (go.GetComponentInChildren<Collider>(true) == null) FitCollider(go);

            // components are added only if missing; per-kind stats apply only to code-created ones
            var rb = Adopt.Component<Rigidbody>(go, out bool rbNew);
            if (rbNew)
            {
                rb.mass = 70f;
                rb.constraints = RigidbodyConstraints.FreezeRotation;
            }

            var dmg = Adopt.Component<Element>(go, out bool dmgNew);
            if (dmgNew)
                dmg.Health = 60f;

            var creature = Adopt.Component<Creature>(go);
            Adopt.Component<WeightSag>(go);   // weight you can see before it crushes
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
                brain.Capacity = 3;
            }

            var z = Adopt.Component<Zombie>(go, out bool zNew);
            if (zNew)
            {
            }
            z.WalkSpeed *= speedMul; // rounds make everything faster
            dmg.OnDeath += z.OnDeath;
            dmg.OnDamaged += z.OnDamaged;

            // AN AUTHORED BODY NEVER GOES THROUGH THE WARDROBE, and the paint
            // shell used to be built in there - so drawing on one had nothing
            // to land on. It carries its own now, inside its own hierarchy.
            if (!graybox)
            {
                ZombieDress.AttachPaintShell(go.GetComponentInChildren<SkinnedMeshRenderer>(true));
                // and it still walks: the in-place dress wires and drives its
                // animator, which the DressUp bypass silently never did
                z._dress = ZombieDress.DressInPlace(z);
            }

            // wardrobe: shared model follows the capsule; a prefab body is already dressed
            if (graybox)
            {
                float widthMul = 1f;
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

            if (FindFloor(out var floor)) StandOn(floor);

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
            foreach (var c in cols) if (c != null) c.enabled = true;

            // stand it again at the end: the summon sets the body's real size
            // AFTER Spawn returns, so the offset measured on the way down is
            // stale by the time it climbs out
            if (FindFloor(out var settled)) StandOn(settled);
            else transform.position = surface;

            if (_rb != null) _rb.isKinematic = wasKinematic;
            _rising = false;
        }

        static readonly RaycastHit[] _standBuf = new RaycastHit[16];

        /// The highest floor under this zombie. Every hit is considered, not
        /// just the first: a ray that starts inside the body can open on the
        /// zombie's own paint shell, and taking that one hit and rejecting it
        /// for not being floor-like is what left them hanging in mid-air.
        bool FindFloor(out Vector3 point)
        {
            point = default;
            Bounds box = Body();
            Vector3 from = new Vector3(transform.position.x, box.max.y + 0.5f,
                transform.position.z);

            int mask = Physics.DefaultRaycastLayers
                & ~(1 << InkCanvasLayer.Layer) & ~(1 << VesselShell.Layer);
            int n = Physics.RaycastNonAlloc(from, Vector3.down, _standBuf, 40f, mask,
                QueryTriggerInteraction.Ignore);

            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = _standBuf[i];
                if (h.collider == null) continue;
                // itself - and its paint shell, which lives outside the
                // hierarchy, so a plain parent check would miss it and let the
                // zombie stand on top of its own skin
                if (ZombieOwner.From(h.collider) == this) continue;
                if (h.normal.y <= 0.55f) continue;             // a wall, not a floor
                // a surface up past its waist is something it is standing
                // BESIDE, not on; without this a zombie raised next to a table
                // hops onto the table
                if (h.point.y > box.center.y) continue;
                if (h.point.y > best) { best = h.point.y; point = h.point; }
            }
            return best > float.NegativeInfinity;
        }

        /// Rests the body's LOWEST point on a spot. The pivot of an authored
        /// prefab sits wherever the artist left it, so placing the pivot on the
        /// ground buries some bodies and floats others.
        void StandOn(Vector3 ground)
        {
            float lift = transform.position.y - Body().min.y;
            transform.position = new Vector3(ground.x, ground.y + lift, ground.z);
        }

        /// What the zombie occupies: its colliders, or its meshes when the
        /// colliders are off mid-rise.
        Bounds Body()
        {
            bool any = false;
            Bounds b = new Bounds(transform.position, Vector3.zero);

            foreach (var c in GetComponentsInChildren<Collider>())
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                if (any) b.Encapsulate(c.bounds); else { b = c.bounds; any = true; }
            }
            if (!any)
                foreach (var r in GetComponentsInChildren<Renderer>())
                {
                    if (r == null || !r.enabled) continue;
                    if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
                }
            return b;
        }

        static bool _warnedNoCollider;

        /// Wraps whatever the body draws in an upright capsule, so a prefab
        /// that ships without one still has something to stand on.
        public static void FitCollider(GameObject go)
        {
            bool any = false;
            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            if (!any) return;

            Vector3 s = go.transform.lossyScale;
            var cap = go.AddComponent<CapsuleCollider>();
            cap.direction = 1;   // upright
            cap.center = go.transform.InverseTransformPoint(b.center);
            cap.height = b.size.y / Mathf.Max(0.0001f, Mathf.Abs(s.y));
            cap.radius = Mathf.Max(0.05f, 0.5f * Mathf.Min(
                b.size.x / Mathf.Max(0.0001f, Mathf.Abs(s.x)),
                b.size.z / Mathf.Max(0.0001f, Mathf.Abs(s.z))));

            if (_warnedNoCollider) return;
            _warnedNoCollider = true;
            Debug.Log("[SpellyZombie] The zombie body has no collider, so one was fitted to "
                + "its mesh. Add a Capsule Collider to the prefab to shape it yourself.");
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
            HoldPose(seconds);
        }

        Animator _anim;
        float _poseHeldUntil;

        /// An authored body has no dress to settle, so IT holds still: the
        /// shell is cast in the bind pose, and ink lands where the mesh is not
        /// if the body keeps animating out from under it.
        void HoldPose(float seconds)
        {
            if (_dress != null) return;   // the dress does its own settling
            if (_anim == null) _anim = GetComponentInChildren<Animator>(true);
            if (_anim == null) return;
            if (_poseHeldUntil <= Time.time)
            {
                _anim.enabled = false;
                _anim.Rebind();           // back to the pose the shell was cast in
            }
            _poseHeldUntil = Mathf.Max(_poseHeldUntil, Time.time + seconds);
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

        Element _dmg2;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _creature = GetComponent<Creature>();
            _brain = GetComponent<ZombieBrain>();
            _dmg2 = GetComponent<Element>();
            _summon = GetComponent<SummonedZombie>();
            // A ZOMBIE HAS A MIND, which is what makes it a living thing that
            // poison can touch and scenery is not.
            var el = GetComponent<Element>();
            if (el != null && el.Natural.Int <= 0f)
            {
                var n = el.Natural; n.Int = 1f; n.Courage = 1f; el.Natural = n;
                var d = el.Data; d.Int = 1f; d.Courage = 1f; el.Data = d;
            }
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

        /// The move's authored animation if the worn spell linked one,
        /// otherwise the body's built-in tell.
        void PlayMove(string move, System.Action builtIn)
        {
            var def = _summon != null ? _summon.Spell : null;
            var clip = def != null ? def.MoveClip(move) : null;
            if (clip == null || !OneShotClip.Play(gameObject, clip)) builtIn?.Invoke();
        }

        /// Fires this kind's attack: scribblers throw the curse, the rest
        /// charge. False if it has nothing to fire or is on cooldown.
        public bool GhostAbility(Vector3 aimDir)
        {
            if (_creature == null || _brain == null) return false;
            if (aimDir.sqrMagnitude < 0.01f) aimDir = transform.forward;
            aimDir.Normalize();

            var castName = FirstCastable();
            if (castName != null)
            {
                if (_castCooldown > 0f) return false;
                _castCooldown = 1.4f;
                ClearCastRing();
                _dress?.Attack();
                // the FULL look, pitch included - where you aim is where it
                // goes; flattening it spat everything at chest height
                CastNamed(castName, aimDir);
                _brain.Mumble("PTOO!", 1.5f);
                return true;
            }

            // a charge is ground-bound: only ITS aim flattens
            Vector3 flat = aimDir; flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) flat = transform.forward;
            flat.Normalize();
            if (_chargeCooldown > 0f || _charging) return false;
            _chargeCooldown = 4f;
            PlayMove(Charge, () => _dress?.Scream());
            _charging = true;
            _chargeLeft = 2.2f;
            _chargeDir = flat;
            _brain.Mumble("RRAAH!", 1.5f);
            return true;
        }

        /// The first ability that is a castable spell in the book. Charge is
        /// the one MOVE; everything else a body does is a cast.
        string FirstCastable()
        {
            foreach (var a in Abilities)
            {
                if (a == Charge) continue;
                var def = SpellBook.Live.Spell(a);
                if (def != null && !def.IsBody) return a;
            }
            return null;
        }

        /// ★ A BODY CASTS FROM THE BOOK (data summons, code moves): the
        /// authored particle is emitted with the zombie as caster. Numbers,
        /// area, colour, splash - all the spell's own. Nothing hard-coded.
        void CastNamed(string spellName, Vector3 aim)
        {
            var def = SpellBook.Live.Spell(spellName);
            if (def == null)
            {
                Debug.LogWarning($"[SpellyZombie] '{name}' wants to cast '{spellName}' " +
                    "but the book has no such spell.");
                return;
            }
            var p = SpellParticle.Emit(ParticleKind.Push,
                HeadAt + aim * 0.4f, aim, 1.4f);   // the muzzle is along the AIM,
                                                   // not wherever the body faces
            if (p == null) return;
            // NOT Clamped(): the clamp floors Strength at 0, which stripped
            // the goo's -9 bite and stopped it ever fusing into Goo at all
            p.Data = def.Payload;
            p.OwnerId = OwnerId;
            p.SrcSize = DrawingConfig.RuneSizeMin * 2f;
            p.GrammarLevel = def.Level;   // a lvl2 hit lands on its whole area
            p.Vel = aim * 16f;
            p.Wake();
            p.RefreshIdentity_Public();
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
            // the pen let go: the body may move again
            if (_poseHeldUntil > 0f && Time.time >= _poseHeldUntil)
            {
                _poseHeldUntil = 0f;
                if (_anim != null) _anim.enabled = true;
            }

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

            // trance: full stop (steered, not hard-set, so forces still apply).
            // ★ NOT WHILE RIDDEN (his rule): the ghost is the mind, so fresh
            // ink and decoys cannot fool the body - a driven zombie is smarter
            // than itself.
            if (_brain.Tranced && !Possessed)
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


            ScanHostiles();
            var target = _brain.AttackTarget;
            string casts = FirstCastable();
            if (target == null && casts != null) target = DistantMark();
            if (target == null) { TickIdleKind(); AutoSwipe(); return; }
            float dist = Vector3.Distance(transform.position, target.position);

            // ABILITIES, NOT KINDS. What it does is what it was given.
            if (casts != null) TickCaster(casts, target, dist);
            else if (Can(Charge)) TickChargerWindup(target, dist);
            else TrySwipe(target, dist);
        }

        // ---------------------------------------------------------- hostiles --
        float _hostileScan;

        /// Golems are the enemy ecology: any zombie that SEES one hates it.
        void ScanHostiles()
        {
            _hostileScan -= Time.fixedDeltaTime;
            if (_hostileScan > 0f) return;
            _hostileScan = 1f;
            float best = _brain.SightRange * _brain.SightRange;
            Transform found = null;
            foreach (var g in Golem.All)
            {
                if (g == null) continue;
                Vector3 to = g.transform.position - transform.position;
                float d = to.sqrMagnitude;
                if (d >= best) continue;
                // only golems it can SEE - in front, not behind its back
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f
                    && Vector3.Dot(transform.forward, to.normalized) < 0.35f) continue;
                best = d; found = g.transform;
            }
            if (found != null) _brain.GetMadAt(found);
        }

        /// ★ ARTILLERY MARKS (his call): a ranged zombie on its own picks the
        /// farther fight - a golem, a wizard, the cauldron - whatever sits in
        /// throw range.
        Transform DistantMark()
        {
            float best = DrawingConfig.GooThrowRange * DrawingConfig.GooThrowRange;
            Transform found = null;
            void Consider(Transform t)
            {
                if (t == null) return;
                Vector3 to = t.position - transform.position;
                float d = to.sqrMagnitude;
                if (d >= best) return;
                // ★ ONLY WHAT IT SEES IN FRONT (his rule): no 360-degree
                // artillery - it patrols until something crosses its face.
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f
                    && Vector3.Dot(transform.forward, to.normalized) < 0.35f) return;
                best = d; found = t;
            }
            foreach (var g in Golem.All) if (g != null) Consider(g.transform);
            foreach (var p in SimpleFPSController.All)
                if (p != null && Sides.SideOfThing(p.gameObject) == Side.Wizard)
                    Consider(p.transform);
            if (CauldronEconomy.Active != null) Consider(CauldronEconomy.Active.transform);
            return found;
        }

        /// Cast from a distance and KITE whatever can chase: back away when
        /// they close in, cast again once clear. The same cast the ghost
        /// fires, on the zombie's own judgement.
        void TickCaster(string spellName, Transform target, float dist)
        {
            _castCooldown -= Time.fixedDeltaTime;

            bool itChases = target.GetComponentInParent<CauldronEconomy>() == null;
            if (itChases && dist < DrawingConfig.GooKiteRange)
            {
                _brain.MoveDir = (transform.position - target.position).normalized;
                _brain.SpeedScale = 1.4f;
            }

            if (_castCooldown > 0f) return;
            if (dist > DrawingConfig.GooThrowRange) return;

            // ★ TURN TO CAST (his rule): the spit only ever goes FORWARDS, so
            // the body must actually be looking at its mark before it fires.
            Vector3 aim = target.position - transform.position;
            aim.y = 0f;
            if (aim.sqrMagnitude < 0.01f) return;
            aim.Normalize();
            if (Vector3.Dot(transform.forward, aim) < 0.9f)
            {
                // STAND TO AIM: the movement block's own facing slerp fights
                // this one and they deadlock ~65 degrees off - so the feet
                // stop while the body turns
                _brain.MoveDir = Vector3.zero;
                _brain.SpeedScale = 0f;
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(aim), Time.fixedDeltaTime * 6f);
                return;   // not looking yet - no cast
            }

            _castCooldown = DrawingConfig.GooThrowCooldown;
            _dress?.Attack();
            CastNamed(spellName, transform.forward);
            _brain.Mumble("PTOO!", 1.5f);
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
            var obstacle = hit.collider.GetComponentInParent<Element>();
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
            var d = target.GetComponentInParent<Element>();
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
                    PlayMove(Charge, () => _dress?.Scream());
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
            DeathPoof(cause);

            // death releases the gas cloud at GasRadius, unscaled;
            // detonation is the SummonGasDetonateMul version
            var mine = GetComponent<SummonedZombie>();
            mine?.FreeGas();   // the living aura lingers where the body fell
            PoisonField.Open(
                transform.position + Vector3.up * transform.localScale.y * 0.35f,
                // world zombies (no seal) get the minimum radius
                mine != null ? mine.GasRadius : DrawingConfig.SummonGasRadiusMin,
                DrawingConfig.ZombieDeathCloudSeconds);

            // the summoner whistles at their own position; 3D falloff limits the tell
            if (mine != null) WhistleOwner(mine.SummonedBy);

            // NOTHING TO DROP: OwnerId is the SUMMONER now, and dropping here
            // wiped the acolyte's own earned book every time a zombie died.
        }

        /// It never just vanishes: a burst in its own colour, a thud, and a
        /// line naming what did it - killed or timed out, the same tell.
        public void DeathPoof(string cause)
        {
            Color c = BodySkin;
            var view = GetComponent<StateView>();
            if (view != null && view.DriveTint) c = view.Tint;

            Vector3 at = transform.position + Vector3.up * transform.localScale.y * 0.4f;
            GrammarFX.PuffBurst(at, c, 7);
            if (FxLibrary.I != null) FxLibrary.SpawnTinted(FxLibrary.I.Poof, at, c);
            Juice.Pop(transform.position);
            Juice.Thud(transform.position);

            DrawingWorld.Instance?.LogEvent(string.IsNullOrEmpty(cause)
                ? $"{name} falls apart"
                : $"{name} falls apart: {cause}");
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
