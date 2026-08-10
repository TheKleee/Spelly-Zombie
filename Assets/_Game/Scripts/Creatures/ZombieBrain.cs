using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    public enum MemKind { Player, Danger, MadAt, Strategy, Stare }
    public enum StrategyKind { Surround, Conga, Brave, Oops }

    /// The only events that can enter a zombie head; duration rolled per zombie.
    public enum MemEvent
    {
        SawPlayer,      // 4–10s  spotted you with its own googly eyes
        HeardDanger,    // 4–10s  explosion / buddy died / spell went off nearby
        BigSpectacle,   // 2–5s   something huge far away — stops to watch
        ShinyInk,       // 1–3s   fresh ink being drawn — decoy stare
        GossipDanger,   // 3–8s   a buddy warned it (secondhand panic)
        GossipPlayer,   // 3–8s   a buddy snitched your position
        StrategyPact,   // 5–14s  agreed on a plan with a buddy
        Grudge,         // 5–12s  got knocked over / hit — personal now
    }

    /// FIFO memory slots with random forget-timers; capacity IS intelligence (Charger 1 / Walker 3 / Scribbler 5); perception goes through the googly eyes, fresh ink is a decoy.
    public class ZombieBrain : MonoBehaviour
    {
        public struct Memory
        {
            public MemKind Kind;
            public Vector3 Where;
            public Transform Who;
            public StrategyKind Strategy;
            public float Until;
            public bool Approach; // curious stares: shuffle TOWARD it (ink bait!)
        }

        /// How many things fit in this head at once (set per zombie kind).
        public int Capacity = 3;
        public readonly List<Memory> Memories = new List<Memory>(5);

        /// Duration table for the memorable-events list above.
        static void DurationOf(MemEvent evt, out float min, out float max)
        {
            switch (evt)
            {
                case MemEvent.SawPlayer: min = 4f; max = 10f; break;
                case MemEvent.HeardDanger: min = 4f; max = 10f; break;
                case MemEvent.BigSpectacle: min = 2f; max = 5f; break;
                case MemEvent.ShinyInk: min = 1f; max = 3f; break;
                case MemEvent.GossipDanger: min = 3f; max = 8f; break;
                case MemEvent.GossipPlayer: min = 3f; max = 8f; break;
                case MemEvent.StrategyPact: min = 5f; max = 14f; break;
                case MemEvent.Grudge: min = 5f; max = 12f; break;
                default: min = 3f; max = 8f; break;
            }
        }

        public GooglyEyes Eyes;
        public float SightRange = 14f;
        public float HearRange = 9f;

        // what the body should do this tick (Zombie reads these)
        public Vector3 MoveDir;
        public float SpeedScale;       // 0 = stand
        public Transform AttackTarget; // player or a zombie it's mad at

        /// Ink flowing on/next to this zombie = bliss, full stop — drawing on zombies is a legit pinning tactic.
        public bool Tranced =>
            WorldEvents.InkIsFresh &&
            Vector3.Distance(transform.position, WorldEvents.LatestInkPos) < 1.9f;

        TextMesh _mumble;
        float _mumbleUntil, _gossipCooldown, _confusedUntil;
        Creature _creature;

        // patrol: nothing in the head → roam the map, bounce off walls
        Vector3 _patrolTarget, _lastPatrolPos;
        bool _hasPatrol;
        float _patrolPause, _stuckCheck, _stuckTime;

        static readonly List<ZombieBrain> AllBrains = new List<ZombieBrain>();

        void Awake()
        {
            _creature = GetComponent<Creature>();
            AllBrains.Add(this);
            BuildMumbleText();
        }

        void OnDestroy() => AllBrains.Remove(this);

        /// Demons: nothing scares them (their own calamities froze the boss solid otherwise).
        public bool Fearless;

        public static void ScareVisible(Vector3 pos, float radius, float luminance)
        {
            if (luminance < 0.15f) return; // too dim to register on a googly eye
            foreach (var b in AllBrains)
            {
                if (b == null || b.Fearless) continue;
                Vector3 to = pos - b.transform.position;
                if (to.sqrMagnitude > radius * radius) continue;
                if (b._creature != null && b._creature.Blinded) continue;
                if (Vector3.Dot(b.transform.forward, to.normalized) < -0.2f) continue; // behind it
                b.Remember(MemKind.Danger, MemEvent.HeardDanger, pos);
                b.Eyes?.SetMood(EyeMood.Scared, 1.5f);
                if (Random.value < 0.25f) b.Mumble("BLEH! BLEH!", 1.2f);
                else if (Random.value < 0.12f && FxLibrary.I != null) // comic beat (Marko's texts)
                    FxLibrary.Spawn(FxLibrary.I.TextWow, b.transform.position + Vector3.up * 1.7f, null, 2.5f);
            }
        }

        // ------------------------------------------------------------ memory --
        public void Remember(MemKind kind, MemEvent evt, Vector3 where,
            Transform who = null, StrategyKind strat = StrategyKind.Oops)
        {
            DurationOf(evt, out float minDur, out float maxDur);

            // refresh an existing slot of the same kind+who instead of stacking
            for (int i = 0; i < Memories.Count; i++)
                if (Memories[i].Kind == kind && Memories[i].Who == who)
                {
                    var m = Memories[i];
                    m.Where = where; m.Until = Time.time + Random.Range(minDur, maxDur);
                    Memories[i] = m;
                    return;
                }

            while (Memories.Count >= Capacity) // oldest is simply gone (FIFO)
            {
                // the 1-slot charger visibly loses its train of thought when a
                // new event bumps out a DIFFERENT old one — instant distraction
                if (Capacity == 1 && Memories[0].Kind != kind) Mumble("OOH!", 1.2f);
                Memories.RemoveAt(0);
            }
            Memories.Add(new Memory
            {
                Kind = kind, Where = where, Who = who, Strategy = strat,
                Until = Time.time + Random.Range(minDur, maxDur)
            });
        }

        public bool TryGet(MemKind kind, out Memory found)
        {
            for (int i = Memories.Count - 1; i >= 0; i--)
                if (Memories[i].Kind == kind) { found = Memories[i]; return true; }
            found = default;
            return false;
        }

        public void Forget(MemKind kind)
        {
            for (int i = Memories.Count - 1; i >= 0; i--)
                if (Memories[i].Kind == kind) Memories.RemoveAt(i);
        }

        // ------------------------------------------------------------- think --
        void Update()
        {
            float now = Time.time;

            // forgetting: when a memory lapses the zombie is briefly CONFUSED
            for (int i = Memories.Count - 1; i >= 0; i--)
                if (now > Memories[i].Until)
                {
                    if (Memories[i].Kind == MemKind.Player || Memories[i].Kind == MemKind.Danger)
                    {
                        Mumble("GRUH?", 1.5f);          // ".. what was I doing?"
                        _confusedUntil = now + Random.Range(1f, 2.5f);
                    }
                    Memories.RemoveAt(i);
                }

            if (Tranced)
            {
                // bliss: stand perfectly still, saucer eyes, sees NOTHING
                MoveDir = Vector3.zero;
                SpeedScale = 0f;
                AttackTarget = null;
                Eyes?.SetMood(EyeMood.Wowed, 0.3f);
                if (now > _mumbleUntil && Random.value < 0.02f) Mumble("mmmm~", 1.5f);
            }
            else
            {
                Perceive();
                Gossip();
                Decide();
                SteerAround(); // Decide() says WHERE, this says HOW to get there
            }
            if (_mumble != null)
            {
                if (now > _mumbleUntil) _mumble.text = "";
                _mumble.transform.rotation = Camera.main != null
                    ? Quaternion.LookRotation(_mumble.transform.position - Camera.main.transform.position)
                    : _mumble.transform.rotation;
            }
        }

        void Perceive()
        {
            // big far events are hypnotic; near ones are terrifying
            if (WorldEvents.TryGetLoudest(3f, out var evt))
            {
                float dist = Vector3.Distance(transform.position, evt.Pos);
                if (evt.Intensity >= 2f && dist < HearRange)
                {
                    Remember(MemKind.Danger, MemEvent.HeardDanger, evt.Pos);
                    Mumble("BLEH! BLEH!", 2f);
                    Eyes?.SetMood(EyeMood.Scared, 2f);
                }
                else if (evt.Intensity >= 2.5f && dist >= HearRange && dist < 45f)
                {
                    Remember(MemKind.Stare, MemEvent.BigSpectacle, evt.Pos);
                    Eyes?.SetMood(EyeMood.Wowed, 2.5f);
                }
                else if (evt.Intensity >= 1.2f && evt.Intensity < 2f && dist < SightRange)
                {
                    // a gentle glow below the fear line is a LURE (sticky light):
                    // stare — and shuffle over to look. moths, all of them.
                    Remember(MemKind.Stare, MemEvent.BigSpectacle, evt.Pos);
                    Eyes?.SetMood(EyeMood.Wowed, 2f);
                    for (int i = 0; i < Memories.Count; i++)
                        if (Memories[i].Kind == MemKind.Stare)
                        { var m = Memories[i]; m.Approach = true; Memories[i] = m; }
                }
            }

            // BLINDED (standing in conjured darkness): all visual perception off
            if (_creature != null && _creature.Blinded) return;

            // distraction gate: while staring (at ink, at fireworks) it sees NOTHING
            if (TryGet(MemKind.Stare, out _)) return;
            if (WorldEvents.InkIsFresh)
            {
                float inkDist = Vector3.Distance(transform.position, WorldEvents.LatestInkPos);
                // ink ON its own body (or right at its feet) is imperceptible — a
                // zombie cannot see its own back, so sneaking up to draw on one works
                if (inkDist > 1.6f && inkDist < SightRange)
                {
                    // ooh, shiny moving ink — stare at it instead of hunting you;
                    // sometimes (close by, dice willing) it shuffles OVER to look.
                    // Wasted sub-40% scribbles are genuine zombie bait.
                    Remember(MemKind.Stare, MemEvent.ShinyInk, WorldEvents.LatestInkPos);
                    if (inkDist < 10f && Random.value < 0.4f)
                        for (int i = 0; i < Memories.Count; i++)
                            if (Memories[i].Kind == MemKind.Stare)
                            { var m = Memories[i]; m.Approach = true; Memories[i] = m; }
                    return;
                }
            }

            // actually seeing the player: FOV + line of sight through the eyes.
            // Downed players don't register — the horde loses interest in you
            // the moment you hit the floor (three memory slots, none for pity).
            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || p.IsDowned) continue;
                // ACOLYTES ARE NOT PREY. They summoned us. A zombie that hunts its
                // own master is funny exactly once and then it is a bug.
                if (Sides.IsAcolytePlayer(p)) continue;
                Vector3 to = p.transform.position - transform.position;
                if (to.sqrMagnitude > SightRange * SightRange) continue;
                if (Vector3.Angle(transform.forward, to) > 70f) continue; // it's behind me? doesn't exist
                if (Physics.Raycast(transform.position + Vector3.up * 1.4f, to.normalized,
                        out var hit, SightRange) && hit.collider.GetComponentInParent<SimpleFPSController>() == null)
                    continue; // wall in the way
                // THE WAND IS YOUR AUTHORITY (Marko's standing law, and until now
                // never actually built: WandState.HasWand carried a comment saying
                // "Phase 4's fear/attack system will read this" and nothing ever
                // did). Zombies feared fireballs and shrugged at the person
                // holding the wand.
                //
                // Armed, they run. Wandless, you are just meat and they come for
                // you. That one flip is what makes losing your wand frightening
                // and what makes the cauldron worth defending.
                if (IsArmed(p) && !Fearless)
                {
                    Remember(MemKind.Danger, MemEvent.HeardDanger, p.transform.position, p.transform);
                    if (Random.value < 0.15f) Mumble("NO NO NO", 1.5f);
                    Eyes?.SetMood(EyeMood.Scared, 0.4f);
                    continue;
                }

                Remember(MemKind.Player, MemEvent.SawPlayer, p.transform.position, p.transform);
                if (!TryGet(MemKind.MadAt, out _)) Mumble("BRAAINS!", 2f);
            }
        }

        void Gossip()
        {
            _gossipCooldown -= Time.deltaTime;
            if (_gossipCooldown > 0f) return;

            foreach (var other in AllBrains)
            {
                if (other == this) continue;
                if ((other.transform.position - transform.position).sqrMagnitude > 2.5f * 2.5f) continue;

                _gossipCooldown = Random.Range(6f, 12f);
                other._gossipCooldown = _gossipCooldown;
                Mumble("MMBL MMBL", 2f);
                other.Mumble("MMBL?", 2f);

                float roll = Random.value;
                if (roll < 0.35f && TryGet(MemKind.Danger, out var danger))
                    other.Remember(MemKind.Danger, MemEvent.GossipDanger, danger.Where); // panic spreads
                else if (roll < 0.6f && TryGet(MemKind.Player, out var player))
                    other.Remember(MemKind.Player, MemEvent.GossipPlayer, player.Where, player.Who); // snitching
                else
                {
                    // invent a strategy together (they will absolutely forget it)
                    var strat = (StrategyKind)Random.Range(0, 4);
                    Remember(MemKind.Strategy, MemEvent.StrategyPact, transform.position, other.transform, strat);
                    other.Remember(MemKind.Strategy, MemEvent.StrategyPact, other.transform.position, transform, strat);
                    if (strat == StrategyKind.Oops)
                    {
                        // the strategy was: forget everything
                        Memories.Clear(); other.Memories.Clear();
                        Mumble("GRUH?", 2f); other.Mumble("GRUH?", 2f);
                    }
                }
                break;
            }
        }

        void Decide()
        {
            MoveDir = Vector3.zero;
            SpeedScale = 0f;
            AttackTarget = null;

            // frozen or sliding is PHYSICAL: an order cannot beat that
            if (_creature != null && (!_creature.CanMove || _creature.Slipping)) return;

            // ...but being confused is MENTAL, and an order overrides the mind,
            // so a commanded zombie marches even while it has forgotten why.
            if (Time.time < _confusedUntil && !_hasOrder) { LookAround(); return; }

            // 0. AN ORDER OVERRIDES EVERYTHING (Marko, Aug 6: "Zombies should
            // listen to our command as it should override their brain. So if we
            // say walk there they walk there even if attacked or spells are
            // nearby or they are fighting each other or destroying things... it
            // doesn't matter").
            //
            // This sits ABOVE fear, beef, prey and curiosity on purpose. It is
            // mind control, not a suggestion. An acolyte paid ink and stood in
            // the open to draw that arrow, so it has to be worth more than a
            // zombie's own opinion. I had it below everything, which made
            // commands look like they were being ignored.
            if (_hasOrder)
            {
                Vector3 toOrder = _orderTarget - transform.position; toOrder.y = 0f;
                if (toOrder.sqrMagnitude < 2f * 2f) SetOrdered(false);   // arrived, think for yourself again
                else { Head(_orderTarget, 1f); return; }
            }

            // 1. beef comes first — zombies have priorities
            if (TryGet(MemKind.MadAt, out var mad) && mad.Who != null)
            {
                Head(mad.Who.position, 1.15f);
                AttackTarget = mad.Who;
                Eyes?.SetMood(EyeMood.Mad, 0.3f);
                return;
            }

            // 1.5 THEIR BACK IS TURNED. Fear is the rule; this is the exception.
            // A zombie will not face a wizard, but it will absolutely jump one who
            // has stopped looking. Marko: "they want to be chased, that's their
            // role" — a struck player turns around, and a player turning around is
            // a player not doing something else.
            if (StrikesTurnedBacks)
            {
                var back = NearbyPlayer(out bool facingMe);
                if (back != null && !facingMe)
                {
                    Head(back.position, 1.25f);
                    AttackTarget = back;
                    Eyes?.SetMood(EyeMood.Mad, 0.3f);
                    return;
                }
            }

            // 2. run AWAY from remembered danger — but FEAR IS REACTIVE. They flee
            // whoever is actually coming for them and stop the moment nobody is,
            // rather than panicking on a timer. Without this they sprint away from
            // a wizard who left thirty seconds ago, which reads as broken and
            // makes them useless as the bait they are supposed to be.
            if (TryGet(MemKind.Danger, out var danger2))
            {
                if (NearbyPlayer(out _) == null)
                {
                    Forget(MemKind.Danger);   // nobody is chasing: get back to work
                }
                else
                {
                    Vector3 away = transform.position - danger2.Where; away.y = 0f;
                    if (away.sqrMagnitude > 0.01f) { MoveDir = away.normalized; SpeedScale = 1.6f; }
                    return;
                }
            }

            // 3. gawk — or, if curious, shuffle over for a closer look
            if (TryGet(MemKind.Stare, out var stare))
            {
                if (Eyes != null) Eyes.LookTarget = stare.Where;
                if (stare.Approach && (stare.Where - transform.position).sqrMagnitude > 2f * 2f)
                    Head(stare.Where, 0.6f);
                else
                    Face(stare.Where);
                return;
            }

            // 4. hunt (strategy flavored)
            if (TryGet(MemKind.Player, out var prey))
            {
                Vector3 target = prey.Who != null ? prey.Who.position : prey.Where;
                if (TryGet(MemKind.Strategy, out var strat))
                {
                    switch (strat.Strategy)
                    {
                        case StrategyKind.Surround: // approach from a flank
                            Vector3 side = Vector3.Cross(Vector3.up, (target - transform.position).normalized);
                            target += side * (GetInstanceID() % 2 == 0 ? 4f : -4f);
                            break;
                        case StrategyKind.Conga:    // follow the buddy instead (conga line)
                            if (strat.Who != null) target = strat.Who.position - strat.Who.forward * 1.2f;
                            break;
                        case StrategyKind.Brave:    // full send
                            SpeedScale = 0.4f;
                            break;
                    }
                }
                Head(target, SpeedScale > 0f ? SpeedScale + 1f : 1f);
                AttackTarget = prey.Who;
                return;
            }

            // 5. nothing in the head: patrol the map
            Patrol();
        }

        /// OFF by default, because it contradicts the co-op mode's law that zombies
        /// never attack you. The acolyte mode switches it ON: there, a zombie's job
        /// is to make you deal with it, and ignoring one has to cost something.
        public bool StrikesTurnedBacks;

        // where an acolyte's arrow sent us
        Vector3 _orderTarget;
        bool _hasOrder;

        /// GO THERE. Marko's rule: the acolyte draws an arrow inside the summoning
        /// seal and that one drawing is the whole order, how many and which way.
        /// Cleared on arrival, so a commanded zombie goes back to being a zombie
        /// rather than standing on a spot forever.
        public void Order(Vector3 where)
        {
            _orderTarget = where;
            SetOrdered(true);
        }

        /// Red pupils for as long as somebody else is doing the thinking.
        void SetOrdered(bool on)
        {
            if (_hasOrder == on) return;
            _hasOrder = on;
            Eyes?.SetPupilTint(on ? DrawingConfig.MindControlEyeColor : (Color?)null);
        }

        /// Holding a working wand. Marko's rule is "armed = holding a wand OR a
        /// grabbed spell"; the wand half is what exists today, so that is what
        /// this reads. The grabbed-spell half is the disarmed player's recovery
        /// move and belongs with the rest of the levitation work.
        static bool IsArmed(SimpleFPSController p)
        {
            if (p == null) return false;
            var w = p.GetComponent<WandState>();
            return w == null || w.HasWand;   // no WandState (lobby, studio) = armed
        }

        /// Closest live player within ZombieChaseRange, and whether they are facing
        /// this zombie. One scan answers both of Marko's rules: someone facing me
        /// and close is CHASING me, someone close with their back to me is PREY.
        /// Downed players count for neither, same as everywhere else in this file.
        Transform NearbyPlayer(out bool facingMe)
        {
            facingMe = false;
            float range = DrawingConfig.ZombieChaseRange;
            float bestSq = range * range;
            Transform best = null;
            bool bestFacing = false;

            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || p.IsDowned) continue;
                if (Sides.IsAcolytePlayer(p)) continue;   // acolytes are our masters
                Vector3 to = p.transform.position - transform.position;
                to.y = 0f;
                float d2 = to.sqrMagnitude;
                if (d2 > bestSq) continue;

                // Are they looking at me? `to` runs zombie -> player, so the way
                // from THEM to ME is -to. Their forward pointing that way means
                // they can see me; more than ZombieBackAngle off it means their
                // back is turned.
                Vector3 theirFwd = p.transform.forward; theirFwd.y = 0f;
                bool looking = Vector3.Angle(theirFwd, -to) < DrawingConfig.ZombieBackAngle;

                bestSq = d2;
                best = p.transform;
                bestFacing = looking;
            }

            facingMe = bestFacing;
            return best;
        }

        // (Forget(MemKind) already exists above — no duplicate needed)

        // ------------------------------------------------ navigation ------
        // Decide() picks a DIRECTION it wants. It has no idea there is a wall in
        // the way. This layer sits between that decision and the body, and only
        // ever bends MoveDir — every behaviour above (fear, hunting, gossip,
        // patrol, trance) keeps working exactly as written.
        //
        // Two rules, in order:
        //   1. STEER. Probe ahead; if blocked, take the nearest clear angle.
        //   2. WALL-FOLLOW. Steering alone cannot escape concave shapes: inside a
        //      courtyard with one gate a zombie mills against the wall forever.
        //      So when it stops making progress it commits to hugging ONE side
        //      until it can head for its target again. That is the cheap classic
        //      fix and it gets them out of most real geometry.
        //
        // Being stupid is on brand, and zombies expire on a timer, so a genuinely
        // stuck one is self-cleaning. This does not need to be good, only sane.

        /// How far ahead this zombie looks. Raise for a smarter variant, lower for
        /// a dumber one that walks into things.
        public float LookAhead = -1f;   // -1 = use the tuning default

        float _noProgress, _progressCheck, _wallFollowUntil;
        Vector3 _lastNavPos;
        int _wallSide = 1;              // +1 hug right, -1 hug left
        static readonly RaycastHit[] _navHits = new RaycastHit[8];

        void SteerAround()
        {
            if (MoveDir.sqrMagnitude < 0.0001f || SpeedScale <= 0f)
            {
                _noProgress = 0f;          // standing still on purpose is not stuck
                _lastNavPos = transform.position;
                return;
            }

            float dt = Time.deltaTime;

            // --- am I actually getting anywhere? ---
            _progressCheck -= dt;
            if (_progressCheck <= 0f)
            {
                const float Beat = 0.3f;
                _progressCheck = Beat;
                float moved = (transform.position - _lastNavPos).magnitude;
                _lastNavPos = transform.position;
                // wanting to move and barely moving = something is in the way
                _noProgress = moved < 0.06f ? _noProgress + Beat : 0f;

                if (_noProgress >= DrawingConfig.ZombieStuckSeconds)
                {
                    _noProgress = 0f;
                    // pick the side with more room, and commit to it
                    _wallSide = Clear(Rotate(MoveDir, 55f)) ? 1
                              : Clear(Rotate(MoveDir, -55f)) ? -1
                              : (GetInstanceID() & 1) == 0 ? 1 : -1;
                    _wallFollowUntil = Time.time + DrawingConfig.ZombieWallFollowSeconds;
                }
            }

            Vector3 want = MoveDir;

            // --- hugging a wall: bias hard to the committed side ---
            if (Time.time < _wallFollowUntil)
            {
                Vector3 hug = Rotate(want, _wallSide * 70f);
                if (Clear(hug)) { MoveDir = hug; return; }
                Vector3 slight = Rotate(want, _wallSide * 35f);
                if (Clear(slight)) { MoveDir = slight; return; }
                if (Clear(want)) { _wallFollowUntil = 0f; MoveDir = want; return; } // escaped
            }

            // --- ordinary steering: straight if you can, else nearest clear ---
            if (Clear(want)) return;

            for (float a = 25f; a <= 130f; a += 25f)
            {
                Vector3 first = Rotate(want, _wallSide * a);   // prefer the side we last committed to
                if (Clear(first)) { MoveDir = first; return; }
                Vector3 second = Rotate(want, -_wallSide * a);
                if (Clear(second)) { MoveDir = second; return; }
            }

            MoveDir = -want; // boxed in on every side: turn around
        }

        static Vector3 Rotate(Vector3 dir, float degrees) =>
            Quaternion.AngleAxis(degrees, Vector3.up) * dir;

        /// Is there room to walk this way? Sweeps a zombie-width sphere at chest
        /// height. Creatures are NOT obstacles: a zombie that dodges its own prey
        /// never arrives, and shoving past each other is what a crowd does.
        bool Clear(Vector3 dir)
        {
            float reach = LookAhead > 0f ? LookAhead : DrawingConfig.ZombieLookAhead;
            Vector3 origin = transform.position + Vector3.up * 1.1f;
            int n = Physics.SphereCastNonAlloc(origin, DrawingConfig.ZombieProbeRadius,
                dir, _navHits, reach, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < n; i++)
            {
                var t = _navHits[i].transform;
                if (t == null || t.IsChildOf(transform)) continue;           // itself
                if (AttackTarget != null && t.IsChildOf(AttackTarget)) continue; // never dodge prey
                if (t.GetComponentInParent<Creature>() != null) continue;   // players and other zombies
                return false;
            }
            return true;
        }

        void Head(Vector3 target, float speedScale)
        {
            Vector3 to = target - transform.position; to.y = 0f;
            if (to.sqrMagnitude < 0.04f) return;
            MoveDir = to.normalized;
            SpeedScale = speedScale;
            if (Eyes != null) Eyes.LookTarget = target + Vector3.up * 1.5f;
        }

        void Face(Vector3 target)
        {
            Vector3 to = target - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.04f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(to.normalized), Time.deltaTime * 3f);
        }

        /// Roam waypoint-to-waypoint; walls/no-progress pick a new destination, arrival earns a dazed pause.
        void Patrol()
        {
            float dt = Time.deltaTime;
            if (_patrolPause > 0f) { _patrolPause -= dt; LookAround(); return; }
            if (!_hasPatrol) { PickPatrolPoint(); return; }

            Vector3 to = _patrolTarget - transform.position; to.y = 0f;
            if (to.magnitude < 1.2f) // arrived — pause, then wander on
            {
                _hasPatrol = false;
                _patrolPause = Random.Range(0.8f, 2.5f);
                if (Random.value < 0.25f) Mumble("NNNGH…", 2f);
                return;
            }
            MoveDir = to.normalized;
            SpeedScale = 0.55f;
            if (Eyes != null) Eyes.LookTarget = transform.position + MoveDir * 6f + Vector3.up * 1.2f;

            // going nowhere? (walked into something) — pick a new destination
            _stuckCheck -= dt;
            if (_stuckCheck <= 0f)
            {
                _stuckCheck = 0.4f;
                float moved = (transform.position - _lastPatrolPos).magnitude;
                _lastPatrolPos = transform.position;
                _stuckTime = moved < 0.08f ? _stuckTime + 0.4f : 0f;
                if (_stuckTime > 1.2f) { _stuckTime = 0f; _hasPatrol = false; }
            }
        }

        void PickPatrolPoint()
        {
            for (int tries = 0; tries < 6; tries++)
            {
                Vector2 c = Random.insideUnitCircle.normalized;
                Vector3 dir = new Vector3(c.x, 0f, c.y);
                // don't aim straight into a nearby wall
                if (Physics.Raycast(transform.position + Vector3.up * 1.2f, dir, 3f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                _patrolTarget = transform.position + dir * Random.Range(6f, 18f);
                _hasPatrol = true;
                _lastPatrolPos = transform.position;
                _stuckTime = 0f;
                return;
            }
            // boxed in on all sides tried: just turn around
            _patrolTarget = transform.position - transform.forward * 8f;
            _hasPatrol = true;
            _lastPatrolPos = transform.position;
        }

        void LookAround() // confused head swivel after forgetting something
        {
            if (Eyes != null)
                Eyes.LookTarget = transform.position +
                    Quaternion.Euler(0f, Mathf.Sin(Time.time * 3f) * 90f, 0f) * transform.forward * 5f;
        }

        // ------------------------------------------------------------ social --
        /// Zombies hold grudges badly but sincerely.
        public void GetMadAt(Transform offender)
        {
            if (offender == null) return;
            Remember(MemKind.MadAt, MemEvent.Grudge, offender.position, offender);
            Mumble("GRRRR!", 2f);
            Eyes?.SetMood(EyeMood.Mad, 2f);
        }

        public void Mumble(string text, float seconds)
        {
            if (_mumble == null) return;
            _mumble.text = text;
            _mumbleUntil = Time.time + seconds;
        }

        void BuildMumbleText()
        {
            var go = new GameObject("Mumble");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            _mumble = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _mumble.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            _mumble.characterSize = 0.14f;
            _mumble.fontSize = 32;
            _mumble.anchor = TextAnchor.MiddleCenter;
            _mumble.color = new Color(0.9f, 1f, 0.85f);
            _mumble.text = "";
        }

        /// Hard zombie-on-zombie contact can spark a brawl; wall bumps re-plan patrol (floor normals don't count).
        void OnCollisionEnter(Collision col)
        {
            var other = col.collider.GetComponentInParent<ZombieBrain>();
            if (other == null)
            {
                if (col.rigidbody == null && col.contactCount > 0 &&
                    col.GetContact(0).normal.y < 0.4f && _hasPatrol)
                {
                    _hasPatrol = false;      // wall. new plan. any plan.
                    _patrolPause = 0.25f;
                }
                return;
            }
            if (col.relativeVelocity.magnitude < 2.5f) return;
            if (Random.value < 0.5f) GetMadAt(other.transform);
            if (Random.value < 0.5f) other.GetMadAt(transform);
        }
    }
}
