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
        BigSpectacle,   // 2–5s   something huge far away - stops to watch
        ShinyInk,       // 1–3s   fresh ink being drawn - decoy stare
        GossipDanger,   // 3–8s   a buddy warned it (secondhand panic)
        GossipPlayer,   // 3–8s   a buddy snitched your position
        StrategyPact,   // 5–14s  agreed on a plan with a buddy
        Grudge,         // 5–12s  got knocked over / hit - personal now
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
            public bool Approach; // curious stare: shuffle toward it
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

        /// Fresh ink on or next to this zombie = full stop. TrancedUntil is the
        /// direct pin: the 1.9m proximity test measures from the capsule centre
        /// and misses big bodies, so the pen sets it explicitly.
        public float TrancedUntil;
        public bool Tranced =>
            Time.time < TrancedUntil
            || (WorldEvents.InkIsFresh &&
                Vector3.Distance(transform.position, WorldEvents.LatestInkPos) < 1.9f);

        TextMesh _mumble;
        float _mumbleUntil, _gossipCooldown, _confusedUntil;
        Creature _creature;

        // patrol: nothing in the head  roam the map, bounce off walls
        Vector3 _patrolTarget, _lastPatrolPos;
        bool _hasPatrol;
        float _patrolPause, _stuckCheck, _stuckTime;

        static readonly List<ZombieBrain> AllBrains = new List<ZombieBrain>();

        ChargeAttack _charge;   // only Chargers carry one

        void Awake()
        {
            _creature = GetComponent<Creature>();
            _charge = GetComponent<ChargeAttack>();
            AllBrains.Add(this);
            BuildMumbleText();
        }

        void OnDestroy() => AllBrains.Remove(this);

        /// Demons: nothing scares them.
        /// Set by things that are simply never afraid - the demon, and a
        /// zombie under the aggression rune. Courage can also earn it.
        public bool AlwaysFearless;

        /// ★ HOW IT LIVES, from its definition (Zombie.Wear). Roams is a zombie's
        /// own way: it runs from wands and hunts the wandless. Hunters and guards
        /// never run; a skittish one is never brave on its own; a guard keeps
        /// within GuardRange of where it stood up.
        public CreatureBehaviour Behaviour = CreatureBehaviour.Roams;
        public float GuardRange = 10f;
        Vector3 _home;
        bool _hasHome;
        public void SetHome(Vector3 at) { _home = at; _hasHome = true; }
        public void CopyHomeFrom(ZombieBrain other)
        {
            if (other == null) return;
            Behaviour = other.Behaviour;
            GuardRange = other.GuardRange;
            _home = other._home;
            _hasHome = other._hasHome;
        }
        bool Steady => Behaviour == CreatureBehaviour.Hunts || Behaviour == CreatureBehaviour.Guards
            || Behaviour == CreatureBehaviour.Rampages;
        bool Guarding => Behaviour == CreatureBehaviour.Guards && _hasHome;
        bool OutsidePost(Vector3 at) => Guarding && (at - _home).sqrMagnitude > GuardRange * GuardRange;
        /// The aggression rune's minute: nothing scares it either. ZombieBuff sets and clears it.
        public bool BuffFearless;
        /// Under a decoy: every wizard is a terror, wand or not, until this time.
        public float DecoyedUntil;
        public bool Decoyed => Time.time < DecoyedUntil;
        /// The transformation ink: a crate for a moment - it walks, it does not bite.
        public float TransformedUntil;
        public bool Transformed => Time.time < TransformedUntil;

        /// ★ COURAGE IS A NUMBER, and the ground moves it. 0 is afraid of
        /// everything, high faces anything - a dreadful biome unnerves a
        /// brave zombie and a bold one emboldens a coward.
        ///
        /// It was a bare bool that only ever got switched on by hand, which
        /// meant biomes could freeze a zombie and burn it but never frighten it.
        public float Courage
        {
            get
            {
                if (_el == null) _el = GetComponentInParent<Element>();
                return _el != null ? _el.Data.Courage : 1f;
            }
        }
        Element _el;

        public bool Fearless => AlwaysFearless || BuffFearless
            || (Behaviour != CreatureBehaviour.Skittish && (Steady || Courage >= DrawingConfig.FearlessAt));

        /// How readily it takes fright at all. A coward panics at things a
        /// braver one walks past.
        public float FearChance => Mathf.Clamp01(1f - Courage / Mathf.Max(0.01f, DrawingConfig.FearlessAt));

        /// ★ INT IS HOW MUCH FITS IN ITS HEAD. His words: 0 mindless, high
        /// follows its task perfectly. A mindless place empties the head, so a
        /// zombie in one forgets what it was chasing.
        public int Headroom
        {
            get
            {
                if (_el == null) _el = GetComponentInParent<Element>();
                float mind = _el != null ? _el.Data.Int : 1f;
                return Mathf.Clamp(Mathf.RoundToInt(Capacity * mind), 0, 8);
            }
        }

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
                if (Random.value < 0.25f * b.FearChance) b.Mumble("BLEH! BLEH!", 1.2f);
                else if (Random.value < 0.12f && FxLibrary.I != null)
                    FxLibrary.Spawn(FxLibrary.I.TextWow, b.transform.position + Vector3.up * 1.7f, null, 2.5f);
            }
        }

        // ------------------------------------------------------------ memory --
        public void Remember(MemKind kind, MemEvent evt, Vector3 where,
            Transform who = null, StrategyKind strat = StrategyKind.Oops)
        {
            // a MINDLESS head holds nothing - and the slot-eviction below
            // threw on an empty list when Headroom was zero
            if (Headroom <= 0) return;
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

            while (Memories.Count >= Headroom) // oldest is simply gone (FIFO)
            {
                // a 1-slot head visibly loses its train of thought
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
        Zombie _host;

        void Update()
        {
            // a possessing ghost writes MoveDir itself
            if (_host == null) _host = GetComponent<Zombie>();
            if (_host != null && _host.Possessed) return;

            float now = Time.time;

            // forgetting: when a memory lapses the zombie is briefly CONFUSED
            for (int i = Memories.Count - 1; i >= 0; i--)
                if (now > Memories[i].Until)
                {
                    if (Memories[i].Kind == MemKind.Player || Memories[i].Kind == MemKind.Danger)
                    {
                        Mumble("GRUH?", 1.5f);
                        _confusedUntil = now + Random.Range(1f, 2.5f);
                    }
                    Memories.RemoveAt(i);
                }

            if (Tranced)
            {
                // tranced: stand still, see nothing
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
                FaceMumble(_mumble);
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
                    // a gentle glow below the fear line is a lure: stare and shuffle over
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
                // ink on its own body is imperceptible (it can't see its own back)
                if (inkDist > 1.6f && inkDist < SightRange)
                {
                    // fresh ink: stare instead of hunting; sometimes shuffle over
                    Remember(MemKind.Stare, MemEvent.ShinyInk, WorldEvents.LatestInkPos);
                    if (inkDist < 10f && Random.value < 0.4f)
                        for (int i = 0; i < Memories.Count; i++)
                            if (Memories[i].Kind == MemKind.Stare)
                            { var m = Memories[i]; m.Approach = true; Memories[i] = m; }
                    return;
                }
            }

            // seeing the player: FOV + line of sight; downed players don't register
            var mine = Teams.Of(this);
            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || p.IsDowned) continue;
                // only an enemy team is prey (a summoned zombie never hunts acolytes); a disguise fools it
                if (!Teams.Enemies(mine, Teams.Of(p)) || ShapeShift.Disguised(p)) continue;
                Vector3 to = p.transform.position - transform.position;
                if (to.sqrMagnitude > SightRange * SightRange) continue;
                if (Vector3.Angle(transform.forward, to) > 70f) continue; // outside the FOV cone
                if (Physics.Raycast(transform.position + Vector3.up * 1.4f, to.normalized,
                        out var hit, SightRange) && hit.collider.GetComponentInParent<SimpleFPSController>() == null)
                    continue; // wall in the way
                // armed players are feared; wandless players are prey
                if (Decoyed || (IsArmed(p) && !Fearless))
                {
                    Remember(MemKind.Danger, MemEvent.HeardDanger, p.transform.position, p.transform);
                    if (Random.value < 0.15f) Mumble("NO NO NO", 1.5f);
                    Eyes?.SetMood(EyeMood.Scared, 0.4f);
                    continue;
                }

                Remember(MemKind.Player, MemEvent.SawPlayer, p.transform.position, p.transform);
                if (!TryGet(MemKind.MadAt, out _)) Mumble("BRAAINS!", 2f);
            }

            // friends' puppets (host only): the same cone, sight line and wand rule
            foreach (var a in NetAvatar.All)
            {
                if (a == null || a.Downed || a.Disguised) continue;
                if (!Teams.Enemies(mine, Teams.OfOwner(NetSync.OwnerIdOf(a.Id)))) continue;
                Vector3 to = a.transform.position - transform.position;
                if (to.sqrMagnitude > SightRange * SightRange) continue;
                if (Vector3.Angle(transform.forward, to) > 70f) continue;
                if (Physics.Raycast(transform.position + Vector3.up * 1.4f, to.normalized,
                        out var hit, SightRange) && hit.collider.GetComponentInParent<NetAvatar>() == null)
                    continue;
                if (Decoyed || (!a.Wandless && !Fearless))
                {
                    Remember(MemKind.Danger, MemEvent.HeardDanger, a.transform.position, a.transform);
                    if (Random.value < 0.15f) Mumble("NO NO NO", 1.5f);
                    Eyes?.SetMood(EyeMood.Scared, 0.4f);
                    continue;
                }
                Remember(MemKind.Player, MemEvent.SawPlayer, a.transform.position, a.transform);
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
                    // invent a strategy together
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

            // 0. an order overrides everything: fear, grudges, prey, curiosity
            if (_hasOrder)
            {
                Vector3 toOrder = _orderTarget - transform.position; toOrder.y = 0f;
                if (toOrder.sqrMagnitude < 2f * 2f) SetOrdered(false);   // arrived, think for yourself again
                else
                {
                    // a player in the path (close and roughly ahead) gets attacked
                    Vector3 marchDir = toOrder.normalized;
                    foreach (var p in SimpleFPSController.All)
                    {
                        if (p == null || p.IsDowned) continue;
                        if (!Teams.Enemies(Teams.Of(this), Teams.Of(p))) continue;   // never its own side
                        Vector3 to = p.transform.position - transform.position; to.y = 0f;
                        if (to.sqrMagnitude > 3.2f * 3.2f) continue;
                        if (Vector3.Dot(to.normalized, marchDir) < 0.35f) continue;
                        Head(p.transform.position, 1.15f);
                        AttackTarget = p.transform;
                        Eyes?.SetMood(EyeMood.Mad, 0.3f);
                        return;   // the march resumes by itself once the road is clear
                    }
                    foreach (var a in NetAvatar.All)
                    {
                        if (a == null || a.Downed || a.Disguised) continue;
                        if (!Teams.Enemies(Teams.Of(this), Teams.OfOwner(NetSync.OwnerIdOf(a.Id)))) continue;
                        Vector3 to = a.transform.position - transform.position; to.y = 0f;
                        if (to.sqrMagnitude > 3.2f * 3.2f) continue;
                        if (Vector3.Dot(to.normalized, marchDir) < 0.35f) continue;
                        Head(a.transform.position, 1.15f);
                        AttackTarget = a.transform;
                        Eyes?.SetMood(EyeMood.Mad, 0.3f);
                        return;
                    }
                    Head(_orderTarget, 1f); return;
                }
            }

            // 1. grudges first; a guard lets one go past its post
            if (TryGet(MemKind.MadAt, out var madFar) && madFar.Who != null && OutsidePost(madFar.Who.position))
                Forget(MemKind.MadAt);
            if (TryGet(MemKind.MadAt, out var mad) && mad.Who != null)
            {
                Head(mad.Who.position, 1.15f);
                AttackTarget = mad.Who;
                Eyes?.SetMood(EyeMood.Mad, 0.3f);
                return;
            }

            // 1.5 exception to fear: a player with their back turned gets jumped
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

            // 2. flee remembered danger, but only while someone is actually nearby
            // (hunters and guards never run)
            if (!Steady && TryGet(MemKind.Danger, out var danger2))
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

            // 3. gawk - or, if curious, shuffle over for a closer look
            if (TryGet(MemKind.Stare, out var stare))
            {
                if (Eyes != null) Eyes.LookTarget = stare.Where;
                if (stare.Approach && (stare.Where - transform.position).sqrMagnitude > 2f * 2f)
                    Head(stare.Where, 0.6f);
                else
                    Face(stare.Where);
                return;
            }

            // 4. hunt (strategy flavored); a guard gives up past its post
            if (TryGet(MemKind.Player, out var preyFar) && OutsidePost(preyFar.Who != null ? preyFar.Who.position : preyFar.Where))
                Forget(MemKind.Player);
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
                // a Charger commits instead of closing: same tell, same
                // locked line as a golem, so it is dodgeable the same way
                if (_charge != null && prey.Who != null
                    && _charge.TryStart(prey.Who.position))
                {
                    AttackTarget = prey.Who;
                    Eyes?.SetMood(EyeMood.Mad, DrawingConfig.ChargeTellSeconds);
                    return;
                }

                Head(target, SpeedScale > 0f ? SpeedScale + 1f : 1f);
                AttackTarget = prey.Who;
                return;
            }

            // 5. nothing in the head: patrol the map
            Patrol();
        }

        /// Off by default; the acolyte mode switches it on.
        public bool StrikesTurnedBacks;

        // where an acolyte's arrow sent us
        Vector3 _orderTarget;
        bool _hasOrder;

        /// March order from an acolyte's arrow; cleared on arrival.
        public void Order(Vector3 where)
        {
            _orderTarget = where;
            SetOrdered(true);
        }

        /// Red pupils while under an order.
        void SetOrdered(bool on)
        {
            if (_hasOrder == on) return;
            _hasOrder = on;
            Eyes?.SetPupilTint(on ? DrawingConfig.MindControlEyeColor : (Color?)null);
        }

        /// Armed = holding a wand.
        static bool IsArmed(SimpleFPSController p) => WandState.Armed(p);

        /// Closest live player within ZombieChaseRange and whether they are facing
        /// this zombie. Downed players don't count.
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
                if (!Teams.Enemies(Teams.Of(this), Teams.Of(p)) || ShapeShift.Disguised(p)) continue;   // its own side, or hidden
                Vector3 to = p.transform.position - transform.position;
                to.y = 0f;
                float d2 = to.sqrMagnitude;
                if (d2 > bestSq) continue;

                // `to` runs zombie -> player, so their line of sight to me is -to;
                // more than ZombieBackAngle off it means their back is turned
                Vector3 theirFwd = p.transform.forward; theirFwd.y = 0f;
                bool looking = Vector3.Angle(theirFwd, -to) < DrawingConfig.ZombieBackAngle;

                bestSq = d2;
                best = p.transform;
                bestFacing = looking;
            }
            // friends' puppets (host only): the same range and back-turned rule
            foreach (var a in NetAvatar.All)
            {
                if (a == null || a.Downed || a.Disguised) continue;
                if (!Teams.Enemies(Teams.Of(this), Teams.OfOwner(NetSync.OwnerIdOf(a.Id)))) continue;
                Vector3 to = a.transform.position - transform.position;
                to.y = 0f;
                float d2 = to.sqrMagnitude;
                if (d2 > bestSq) continue;
                Vector3 theirFwd = a.transform.forward; theirFwd.y = 0f;
                bestSq = d2;
                best = a.transform;
                bestFacing = Vector3.Angle(theirFwd, -to) < DrawingConfig.ZombieBackAngle;
            }

            facingMe = bestFacing;
            return best;
        }

        // ------------------------------------------------ navigation ------
        // Local steering only, no navmesh: this layer only bends Decide()'s MoveDir.
        // 1. steer: probe ahead; if blocked, take the nearest clear angle.
        // 2. wall-follow: when progress stalls, hug one committed side until clear.

        /// How far ahead this zombie looks.
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

        /// Room to walk this way? Sphere sweep at chest height. Creatures are
        /// deliberately not obstacles.
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
            if (to.magnitude < 1.2f) // arrived - pause, then wander on
            {
                _hasPatrol = false;
                _patrolPause = Random.Range(0.8f, 2.5f);
                if (Random.value < 0.25f) Mumble("NNNGH…", 2f);
                return;
            }
            MoveDir = to.normalized;
            SpeedScale = 0.55f;
            if (Eyes != null) Eyes.LookTarget = transform.position + MoveDir * 6f + Vector3.up * 1.2f;

            // going nowhere? (walked into something) - pick a new destination
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
                _patrolTarget = Guarding
                    ? _home + dir * Random.Range(0.5f, Mathf.Max(1f, GuardRange * 0.4f))   // a guard paces its post
                    : transform.position + dir * Random.Range(6f, 18f);
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
        public void GetMadAt(Transform offender)
        {
            if (offender == null) return;
            Remember(MemKind.MadAt, MemEvent.Grudge, offender.position, offender);
            Mumble("GRRRR!", 2f);
            Eyes?.SetMood(EyeMood.Mad, 2f);
        }

        /// voiced false: the bubble has a sound of its own (the attack roar), so no groan goes with it.
        public void Mumble(string text, float seconds, bool voiced = true)
        {
            if (_mumble == null) return;
            // a bubble is heard once: the same words again while they still show say nothing new
            bool fresh = text != _mumble.text || Time.time > _mumbleUntil;
            _mumble.text = text;
            _mumbleUntil = Time.time + seconds;
            if (voiced && fresh) Voice(text, transform);
            NetSync.PushMumble(gameObject.GetInstanceID(), voiced ? text : Unvoiced + text, seconds); // the stand-ins say it too
        }

        /// Rides in front of a bubble whose sound the host already sent: the stand-in shows it and stays quiet.
        public const string Unvoiced = "​";

        /// Every bubble is heard, host body and stand-in alike: a groan, louder
        /// when the words shout, lower from a bigger body.
        public static void Voice(string text, Transform body)
        {
            if (string.IsNullOrEmpty(text) || body == null) return;
            bool shout = text.IndexOf('!') >= 0;
            float size = Mathf.Max(0.3f, body.localScale.y);
            Juice.Sound(Sfx.ZombieGroan, body.position + Vector3.up * size, shout ? 1f : 0.55f,
                Mathf.Clamp(1f / Mathf.Sqrt(size), 0.6f, 1.35f) * Random.Range(0.92f, 1.08f), false);
        }

        void BuildMumbleText() => _mumble = BuildMumbleText(transform);

        /// The speech bubble over a zombie's head; the stand-ins wear the same one.
        public static TextMesh BuildMumbleText(Transform body)
        {
            var go = new GameObject("Mumble");
            go.transform.SetParent(body, false);
            go.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            var mumble = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mumble.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            mumble.characterSize = 0.14f;
            mumble.fontSize = 32;
            mumble.anchor = TextAnchor.MiddleCenter;
            mumble.color = new Color(0.9f, 1f, 0.85f);
            mumble.text = "";
            return mumble;
        }

        /// Faces the bubble at whoever is looking, host body and stand-in alike.
        public static void FaceMumble(TextMesh mumble)
        {
            if (mumble == null) return;
            var eye = ViewEye();
            if (eye != null) mumble.transform.rotation = Quaternion.LookRotation(mumble.transform.position - eye.Value);
        }

        /// Where this machine looks from: a ghost's own camera (the body scripts park it at the body
        /// until the ghost flies it, late in the frame), else the main camera.
        public static Vector3? ViewEye() =>
            GhostState.LocalViewPoint ?? (Camera.main != null ? Camera.main.transform.position : (Vector3?)null);

        /// Hard zombie-on-zombie contact can spark a brawl; wall bumps re-plan patrol (floor normals don't count).
        void OnCollisionEnter(Collision col)
        {
            var other = col.collider.GetComponentInParent<ZombieBrain>();
            if (other == null)
            {
                if (col.rigidbody == null && col.contactCount > 0 &&
                    col.GetContact(0).normal.y < 0.4f && _hasPatrol)
                {
                    _hasPatrol = false;      // wall: re-plan
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
