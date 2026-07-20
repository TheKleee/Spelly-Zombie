using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    public enum MemKind { Player, Danger, MadAt, Strategy, Stare }
    public enum StrategyKind { Surround, Conga, Brave, Oops }

    /// THE LIST OF MEMORABLE EVENTS — the only things that can enter a zombie
    /// head — with how long each sticks (rolled per zombie, per event).
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

    /// A zombie's whole mind: a handful of memory slots with random forget-
    /// timers. New memories evict the oldest (FIFO). CAPACITY IS INTELLIGENCE:
    ///   Charger   → 1 slot  (whatever happened last IS its whole world —
    ///               any new memorable event overrides the current one)
    ///   Walker    → 3 slots
    ///   Scribbler → 5 slots (the horde's intellectual)
    /// Everything it does is a reading of what's currently in those slots —
    /// so behavior is legible, dumb, and hilarious:
    ///   sees you        → Player slot (chase) … until it forgets mid-chase
    ///   loud bang near  → Danger slot (flee!) … returns when it forgets why
    ///   big bang far    → Stare slot (stops to watch, wowed)
    ///   bumps a buddy   → gossip: pass a memory on, or invent a "strategy"
    ///   hit hard by one → MadAt slot (zombie brawl)
    /// Mumbles are the tell: each state has a call players learn to read.
    /// Perception goes THROUGH the googly eyes — while staring at something
    /// shiny (fresh ink!) it does not notice you. Drawing is a decoy.
    public class ZombieBrain : MonoBehaviour
    {
        public struct Memory
        {
            public MemKind Kind;
            public Vector3 Where;
            public Transform Who;
            public StrategyKind Strategy;
            public float Until;
            public float Born;
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
        public bool WantsGossip;

        /// Ink is flowing ON (or right next to) this zombie: total bliss. It
        /// stops completely — like a dog getting scratched. This is what makes
        /// drawing runes on zombies possible, and pinning one by doodling on it
        /// a legitimate tactic.
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

        /// Marko's rule: zombies fear dangerous particles THEY CAN SEE.
        /// Visibility is the particle's effective luminance — darkness hides
        /// danger (the invisible-flame trap), a blinded zombie fears nothing,
        /// and something behind it goes unnoticed. Called by SpellParticle.
        /// Set on demons: nothing scares them — they ARE the scary thing.
        /// (A grand demon's own calamities were writing Danger memories at its
        /// own feet, and Decide()'s flee-first priority froze the boss solid.)
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
                Until = Time.time + Random.Range(minDur, maxDur), Born = Time.time
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
                Vector3 to = p.transform.position - transform.position;
                if (to.sqrMagnitude > SightRange * SightRange) continue;
                if (Vector3.Angle(transform.forward, to) > 70f) continue; // it's behind me? doesn't exist
                if (Physics.Raycast(transform.position + Vector3.up * 1.4f, to.normalized,
                        out var hit, SightRange) && hit.collider.GetComponentInParent<SimpleFPSController>() == null)
                    continue; // wall in the way
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

            if (_creature != null && (!_creature.CanMove || _creature.Slipping)) return;
            if (Time.time < _confusedUntil) { LookAround(); return; }

            // 1. beef comes first — zombies have priorities
            if (TryGet(MemKind.MadAt, out var mad) && mad.Who != null)
            {
                Head(mad.Who.position, 1.15f);
                AttackTarget = mad.Who;
                Eyes?.SetMood(EyeMood.Mad, 0.3f);
                return;
            }

            // 2. run AWAY from remembered danger
            if (TryGet(MemKind.Danger, out var danger2))
            {
                Vector3 away = transform.position - danger2.Where; away.y = 0f;
                if (away.sqrMagnitude > 0.01f) { MoveDir = away.normalized; SpeedScale = 1.6f; }
                return;
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

        /// Roam waypoint-to-waypoint. Walls don't end the walk: hitting one (or
        /// making no progress) immediately picks a new destination, and arriving
        /// earns a short dazed pause before the next leg.
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
        /// Something (a charger, a hard bump, a spell) gave this zombie a reason
        /// to hate someone. Zombies hold grudges badly but sincerely.
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

        /// Hard zombie-on-zombie contact can spark a brawl (both roll for rage);
        /// bumping a WALL while patrolling immediately picks a new destination
        /// (floor contacts — mostly-vertical normals — don't count).
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
