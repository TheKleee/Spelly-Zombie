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

        float _born;
        Vector3 _side;
        Transform _target;
        bool _delivered;

        /// The payoff, exactly once, wherever the flight ends.
        void Deliver(Vector3 at)
        {
            if (_delivered) return;
            _delivered = true;
            if (Rune != RuneType.None && Owner == Grimoire.LocalPlayerId)
            {
                Grimoire.UnlockRune(Owner, Rune);
                DrawingWorld.Instance?.LogEvent(
                    $"absorbed: it teaches {RuneLibrary.Icon(Rune)}");
            }
            if (FxLibrary.I != null && FxLibrary.I.AbsorbBurst != null)
                FxLibrary.Spawn(FxLibrary.I.AbsorbBurst, at);
            Juice.Chime(at);
            Destroy(gameObject);
        }

        void Start()
        {
            _born = Time.time;
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
