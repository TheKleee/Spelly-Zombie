using UnityEngine;

namespace SpellyZombie
{
    /// A taken mote on its way to the winner's grimoire. Deliberately public:
    /// the trail tells every wizard AND every hidden acolyte who absorbed -
    /// who is dangerous is information, and information is strategy.
    public class AbsorbFlight : MonoBehaviour
    {
        public int Owner;

        /// The rune this mote carries for the LOCAL winner - unlocked when it
        /// hits the grimoire, never before. None on every other machine.
        public RuneType Rune = RuneType.None;

        /// The thing the mote left. The winner's floating mark hangs over it
        /// while the mote flies and becomes the page where the mote came from.
        public Transform Source;

        float _born;
        Vector3 _side;
        Transform _target;
        bool _delivered;
        Vector3 _origin;
        float _markUp;
        int _markKey;

        Vector3 MarkAt => (Source != null ? Source.position : _origin) + Vector3.up * _markUp;

        /// The payoff, exactly once, wherever the flight ends.
        void Deliver(Vector3 at)
        {
            if (_delivered) return;
            _delivered = true;
            if (Rune != RuneType.None && Owner == Grimoire.LocalPlayerId)
            {
                bool fresh = !Grimoire.HasRune(Owner, Rune);
                Grimoire.UnlockRune(Owner, Rune);
                DrawingWorld.Instance?.LogEvent(
                    $"absorbed: it teaches {RuneLibrary.Icon(Rune)}");
                if (fresh) UnlockMark.FlipAt(_markKey, MarkAt, Rune);
                else UnlockMark.Hide(_markKey);
            }
            if (FxLibrary.I != null && FxLibrary.I.AbsorbBurst != null)
                FxLibrary.Spawn(FxLibrary.I.AbsorbBurst, at);
            Juice.Sound(Sfx.SealComplete, at); // the unlock lands (the winner's own toast plays the same file, and one of the two is dropped)
            Destroy(gameObject);
        }

        void Start()
        {
            _born = Time.time;
            _origin = transform.position;
            _markUp = Source != null ? Mathf.Max(0.35f, _origin.y - Source.position.y + 0.45f) : 0.35f;
            _markKey = UnlockMark.KeyFor(Source != null ? Source : transform);
            _side = Random.onUnitSphere;
            _side.y = Mathf.Abs(_side.y) * 0.5f;
            // the trail wears the mote's own light colour, read at takeoff -
            // so the prefab's light is the one place colour is authored
            var glow = GetComponentInChildren<Light>(true);
            foreach (var t in GetComponentsInChildren<TrailRenderer>(true))
            {
                if (glow != null)
                {
                    t.startColor = glow.color;
                    var end = glow.color; end.a = 0f;
                    t.endColor = end;
                }
                t.Clear();
                t.emitting = true;
            }
        }

        Transform FindTarget()
        {
            if (Owner == Grimoire.LocalPlayerId)
                foreach (var p in SimpleFPSController.All)
                    if (p != null && p.IsLocalViewer)
                    {
                        var rig = p.GetComponent<CharacterRig>();
                        var book = rig != null ? rig.BookTransform : null;
                        return book != null ? book : p.transform;
                    }
            return NetSync.AvatarTransformOf(Owner);
        }

        void Update()
        {
            if (_target == null)
            {
                _target = FindTarget();
                if (_target == null)
                {
                    // never lose the rune to a missing body
                    if (Time.time - _born > 1.5f) Deliver(transform.position);
                    return;
                }
            }

            Vector3 goal = _target.position + Vector3.up * 0.15f;
            // the winner watches the rune arrive: the mark over the source
            // fills with the flight and turns into the page on impact
            if (Rune != RuneType.None && Owner == Grimoire.LocalPlayerId)
            {
                float total = (goal - _origin).magnitude;
                float left = (goal - transform.position).magnitude;
                float t = total > 0.05f ? Mathf.Clamp01(1f - left / total) : 1f;
                if (Source != null) UnlockMark.Show(_markKey, Source, _markUp, t);
                else UnlockMark.ShowAt(_markKey, MarkAt, t);
            }
            float age = Time.time - _born;
            float sp = Mathf.Lerp(10f, 48f, age * 1.8f);   // an arrow, not a drift
            Vector3 arc = _side * Mathf.Sin(Mathf.Min(age * 6f, Mathf.PI)) * 1.4f;
            transform.position = Vector3.MoveTowards(
                transform.position, goal, sp * Time.deltaTime) + arc * Time.deltaTime;

            if ((transform.position - goal).sqrMagnitude < 0.16f || age > 4f)
                Deliver(goal);
        }
    }
}
