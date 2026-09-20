using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ A BOSS (the Creature Creator's Boss switch). Everyone sees its health,
    /// and a counting environment team stands while one of its own lives. The
    /// host keeps who hurt the environment's bosses and who landed the last
    /// blow, for a race to the last boss. A component with a saved name, so the
    /// halves of a split golem are bosses too.
    public class BossMark : MonoBehaviour
    {
        public static readonly List<BossMark> All = new List<BossMark>();
        public string Name = "";

        /// Owner of the last blow on the last boss that fell; -1 = none yet.
        public static int LastBlow = -1;
        /// Where the last of the environment's bosses fell (the ending camera looks there).
        public static Vector3 FellAt;
        static readonly Dictionary<int, float> _hurtBy = new Dictionary<int, float>();

        Element _el;

        void OnEnable()
        {
            All.Add(this);
            if (_el == null) _el = GetComponent<Element>();
            if (_el != null)
            {
                _el.OnDamaged += Hurt;
                _el.OnDeath += Fell;
            }
        }

        void OnDisable()
        {
            All.Remove(this);
            if (_el != null)
            {
                _el.OnDamaged -= Hurt;
                _el.OnDeath -= Fell;
            }
        }

        void Hurt(float amount, string cause)
        {
            int by = _el.LastHitBy;
            if (by < 0 || amount <= 0f || !OfEnvironment) return;
            _hurtBy.TryGetValue(by, out float had);
            _hurtBy[by] = had + amount;
        }

        void Fell(string cause)
        {
            if (!OfEnvironment) return;
            LastBlow = _el.LastHitBy;
            FellAt = transform.position;
        }

        public bool Alive => _el == null || _el.Health > 0f;

        // ★ IT GETS WORSE AS IT BREAKS (his call): every 20% of health lost is
        // a phase. At full health it fights with its body only; from the first
        // 20% on it casts its abilities, more often every phase, all out in its
        // last 20%. Each new phase opens with every ability cast back to back.
        static readonly float[] PhaseWait = { 0f, 1f, 0.7f, 0.45f, 0.25f };
        const float VolleyGap = 0.5f;
        int _phaseSeen, _volley;

        /// 0 at full health .. 4 in the last 20%.
        public int Phase => _el == null || _el.MaxStrength <= 0f ? 0
            : Mathf.Clamp(Mathf.FloorToInt((1f - Mathf.Clamp01(_el.Health / _el.MaxStrength)) * 5f), 0, 4);

        /// It casts its abilities at all yet.
        public bool Casts => Phase > 0;

        /// True once as a new phase begins: cast now, and the next
        /// `abilities` casts come back to back.
        public bool NewPhase(int abilities)
        {
            int p = Phase;
            if (p <= _phaseSeen) return false;
            _phaseSeen = p;
            _volley = Mathf.Max(0, abilities - 1);
            return true;
        }

        /// The wait after a cast: a beat inside the opening volley, else the
        /// usual wait shortened by the phase.
        public float WaitAfterCast(float usual)
        {
            if (_volley > 0) { _volley--; return VolleyGap; }
            return usual * PhaseWait[Phase];
        }

        // ★ IT LEARNS WHAT HURTS IT (his pick): the kind that keeps hurting it
        // does less and less, heat and cold are one balance (hardened to fire
        // is open to cold), and what stops hurting it is forgotten. Host only:
        // the numbers stay at 0 on a client, the puffs travel as fx.
        enum Kind { Fire, Cold, Hit, Magic }
        float _heat;         // -1 hardened to cold .. +1 hardened to fire
        float _hits, _magic; // 0 .. 1
        float _showAt;

        /// A hit scaled by what it has learned so far; then it learns from it.
        public float Resist(float amount, string cause)
        {
            if (amount <= 0f || _el == null) return amount;
            float learn = amount / Mathf.Max(1f, _el.MaxStrength * DrawingConfig.BossResistLearnShare);
            float most = DrawingConfig.BossResistMax;
            switch (KindOf(cause))
            {
                case Kind.Fire:
                    amount *= 1f - most * _heat;
                    _heat = Mathf.Min(1f, _heat + learn);
                    break;
                case Kind.Cold:
                    amount *= 1f + most * _heat;
                    _heat = Mathf.Max(-1f, _heat - learn);
                    break;
                case Kind.Hit:
                    amount *= 1f - most * _hits;
                    _hits = Mathf.Min(1f, _hits + learn);
                    break;
                default:
                    amount *= 1f - most * _magic;
                    _magic = Mathf.Min(1f, _magic + learn);
                    break;
            }
            return amount;
        }

        // by the cause names Element's callers pass
        static Kind KindOf(string cause)
        {
            if (string.IsNullOrEmpty(cause)) return Kind.Magic;
            if (cause.StartsWith("burning") || cause == "on fire" || cause == "flame burst") return Kind.Fire;
            if (cause.StartsWith("freezing")) return Kind.Cold;
            if (cause.StartsWith("hit") || cause.StartsWith("slammed") || cause.StartsWith("crushed")
                || cause.EndsWith(" charge") || cause.EndsWith(" brawl") || cause.EndsWith(" tearing through")
                || cause == "flying debris" || cause == "impact" || cause == "meteor impact"
                || cause == "detonated" || cause == "pressure burst" || cause == "shattered"
                || cause == "pecked by chicken") return Kind.Hit;
            return Kind.Magic;
        }

        void Update()
        {
            float fade = Time.deltaTime / Mathf.Max(0.1f, DrawingConfig.BossResistForgetSeconds);
            _heat = Mathf.MoveTowards(_heat, 0f, fade);
            _hits = Mathf.MoveTowards(_hits, 0f, fade);
            _magic = Mathf.MoveTowards(_magic, 0f, fade);
            ShowResist();
        }

        /// What it resists, worn on its body: embers for fire, frost for cold,
        /// dust for hits, sparkles for magic, thicker the more it has learned.
        void ShowResist()
        {
            if (Time.time < _showAt) return;
            _showAt = Time.time + 0.5f;
            if (Mathf.Abs(_heat) < 0.05f && _hits < 0.05f && _magic < 0.05f) return;
            var b = ShapeShift.FindObjectBounds(transform);
            if (_heat > 0.05f) Puffs(b, new Color(1f, 0.5f, 0.12f), _heat);
            else if (_heat < -0.05f) Puffs(b, new Color(0.85f, 0.95f, 1f), -_heat);
            if (_hits > 0.05f) Puffs(b, new Color(0.55f, 0.5f, 0.42f), _hits);
            if (_magic > 0.05f) Puffs(b, new Color(0.8f, 0.6f, 1f), _magic);
        }

        static void Puffs(Bounds b, Color c, float level)
        {
            int bursts = Mathf.Clamp(2 + Mathf.RoundToInt(b.extents.magnitude), 2, 10);
            int n = 1 + Mathf.RoundToInt(level * 4f);
            for (int i = 0; i < bursts; i++)
                GrammarFX.PuffBurst(b.center + Vector3.Scale(Random.onUnitSphere, b.extents) * 0.9f, c, n);
        }

        /// Nobody raised it: the map's own, on the environment's team.
        public bool OfEnvironment => Teams.Of(this) == Team.Neutral;

        /// One of the environment's bosses lives; `fielded` = one exists at all.
        public static bool EnvironmentStands(out bool fielded)
        {
            fielded = false;
            bool alive = false;
            foreach (var b in All)
            {
                if (b == null || !b.OfEnvironment) continue;
                fielded = true;
                if (b.Alive) alive = true;
            }
            return alive;
        }

        /// Every living boss at once: their health together, how many, the first one's name.
        public static void Summary(out float health01, out int count, out string name)
        {
            float hp = 0f, max = 0f;
            count = 0;
            name = "";
            foreach (var b in All)
            {
                if (b == null || !b.Alive) continue;
                if (count == 0) name = b.Name;
                count++;
                if (b._el == null) continue;
                hp += Mathf.Max(0f, b._el.Health);
                max += Mathf.Max(1f, b._el.MaxStrength);
            }
            health01 = max > 0f ? Mathf.Clamp01(hp / max) : 1f;
        }

        /// A new match: nobody has hurt anything yet.
        public static void ResetMatch()
        {
            LastBlow = -1;
            FellAt = Vector3.zero;
            _hurtBy.Clear();
        }

        /// The side that beat the bosses: the last blow's, else the side that
        /// hurt them most. 1 wizards, 2 acolytes, 0 when no side did.
        public static int WinningSide()
        {
            int side = SideOf(LastBlow);
            if (side != 0) return side;
            float wizards = 0f, acolytes = 0f;
            foreach (var kv in _hurtBy)
            {
                int s = SideOf(kv.Key);
                if (s == 1) wizards += kv.Value;
                else if (s == 2) acolytes += kv.Value;
            }
            if (wizards <= 0f && acolytes <= 0f) return 0;
            return wizards >= acolytes ? 1 : 2;
        }

        static int SideOf(int owner) => owner < 0 ? 0 : Sides.IsAcolyte(owner) ? 2 : 1;
    }
}
