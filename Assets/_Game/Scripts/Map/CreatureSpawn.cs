using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// A string field naming a creature from the book; the Inspector shows a
    /// dropdown of them (Editor/BodyNameDrawer).
    public class BodyNameAttribute : PropertyAttribute { }

    /// ★ A CREATURE ON THE MAP FROM THE START. Placed by hand or by the Map
    /// Creator: once the match is live the HOST raises the creature named here
    /// at this spot, after Delay, once or in waves. Clients see it through the
    /// creature snapshots like any summon.
    public class CreatureSpawn : MonoBehaviour
    {
        [BodyName, Tooltip("A creature from the Creature Creator.")]
        public string Body = "";
        [Tooltip("Size, as a seal would give it. 1 = the creature's own.")]
        public float Size = 1f;
        [Tooltip("Whose side it fights on. Neutral = wild, hunts everyone.")]
        public Team Team = Team.Neutral;
        [Tooltip("Lives until killed. Off = the summon lifetime (a golem 30 s, a zombie as a summon).")]
        public bool Permanent = true;
        [Tooltip("Seconds after the match goes live before the first appears.")]
        public float Delay;
        [Tooltip("0 = once. Otherwise a new wave every this many seconds.")]
        public float Every;
        [Tooltip("How many per wave.")]
        public int Count = 1;
        [Tooltip("Never more than this many of its own alive at once. 0 = no cap.")]
        public int MaxAlive;
        [Tooltip("Metres around the marker a wave scatters over.")]
        public float Scatter = 1.5f;

        // a side's own creatures belong to nobody's kill count but fight for that side
        const int WizardsOwner = 990001, AcolytesOwner = 990002;

        float _live;   // seconds the match has been live
        float _nextAt;
        bool _armed, _warned, _fired;
        readonly List<GameObject> _mine = new List<GameObject>();

        public static readonly List<CreatureSpawn> All = new List<CreatureSpawn>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// An environment boss that has not stood up yet: the environment is not beaten before it has.
        public static bool BossPending
        {
            get
            {
                foreach (var s in All)
                {
                    if (s == null || s._fired || s.Team != Team.Neutral) continue;
                    var creature = SpellBook.Live.Creature(s.Body);
                    if (creature != null && creature.Boss) return true;
                }
                return false;
            }
        }

        void Update()
        {
            if (!NetGame.IsAuthority || !RoundDirector.RunActive)
            {
                _armed = false;
                return;
            }
            if (!_armed) { _armed = true; _live = 0f; _nextAt = Delay; _fired = false; }
            _live += Time.deltaTime;
            if (_live < _nextAt) return;
            _nextAt = Every > 0f ? _nextAt + Every : float.MaxValue;
            Wave();
        }

        int OwnerFor()
        {
            switch (Team)
            {
                case Team.Wizard: Sides.Set(WizardsOwner, Side.Wizard); return WizardsOwner;
                case Team.Acolyte: Sides.Set(AcolytesOwner, Side.Acolyte); return AcolytesOwner;
                default: return -1;
            }
        }

        void Wave()
        {
            _fired = true;
            var def = SpellBook.Live.Creature(Body);
            if (def == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning($"[SpellyZombie] CreatureSpawn '{name}': no creature named '{Body}' in the book.", this);
                }
                return;
            }
            _mine.RemoveAll(g => g == null);
            int owner = OwnerFor();
            for (int i = 0; i < Mathf.Max(1, Count); i++)
            {
                if (MaxAlive > 0 && _mine.Count >= MaxAlive) return;
                Vector2 r = Random.insideUnitCircle * Scatter;
                var raised = Spell.RaiseBody(def, owner, transform.position + new Vector3(r.x, 0f, r.y),
                    Mathf.Max(0.05f, Size), Permanent);
                if (raised != null) _mine.Add(raised);
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Team == Team.Wizard ? new Color(0.3f, 0.6f, 1f)
                : Team == Team.Acolyte ? new Color(0.4f, 0.9f, 0.3f) : new Color(1f, 0.6f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.2f, Scatter));
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * Mathf.Max(0.5f, Size));
        }
    }
}
