using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// How one rune FAMILY's emissions are buffed (stacks are ints — each
    /// pickup of the same powerup deepens it).
    public struct RuneBuff
    {
        public int More;    // +2 particles per burst
        public int Fast;    // launch speed +50% per stack
        public int Bond;    // particles carry extra glue ("connection buff")
        public int Potent;  // payload magnitude +35% per stack
        public int Big;     // size +30% per stack (rides SrcSize → bigger demons too)
        public int Rapid;   // seal re-emits 25% more often per stack
        public int Echo;    // 22%/stack: a landing particle re-emits at half power
    }

    /// Marko's Vampire-Survivors layer: kills level you up; each level offers
    /// 1-of-3 powerups tied to rune families you actually OWN (so card hunting
    /// matters), with a reroll. No pause — this is co-op — instead choosing
    /// wraps you in the INK TRANCE (the existing rule: zombies blissed by
    /// nearby drawing), so you're safe but being SURROUNDED: double-edged.
    /// Confirming a choice fires a NOVA of push particles — your escape hatch.
    public class Powerups : MonoBehaviour
    {
        public static Powerups Instance { get; private set; }
        public static bool IsChoosing => Instance != null && Instance._open;

        // local player's buffs only — remote players run their own copy
        static readonly Dictionary<RuneCardType, RuneBuff> _buffs
            = new Dictionary<RuneCardType, RuneBuff>();

        struct Offer { public RuneCardType Card; public int Stat; }
        readonly List<Offer> _offers = new List<Offer>(3);

        int _level = 1, _kills, _pending, _rerolls;
        bool _open;

        static readonly string[] StatName =
            { "MORE", "SWIFT", "BONDED", "POTENT", "BIG", "RAPID", "ECHO" };
        static readonly string[] StatDesc =
        {
            "+2 particles per burst",
            "particles launch 50% faster",
            "particles carry glue — everything connects",
            "payload +35%: hotter, darker, heavier",
            "particles 30% bigger (rifts remember size…)",
            "the seal pulses 25% more often",
            "landing particles sometimes ECHO at half power",
        };

        static readonly Collider[] _blast = new Collider[48];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("Powerups");
            Instance = go.AddComponent<Powerups>();
            DontDestroyOnLoad(go);
        }

        // ----------------------------------------------------------- lookup --
        /// What Spell.EmitParticles asks: the caster's buff for this rune.
        /// Only the LOCAL player's seals are buffed (zombies cast vanilla).
        public static RuneBuff For(int ownerId, RuneType rune)
        {
            if (ownerId != Grimoire.LocalPlayerId) return default;
            return _buffs.TryGetValue(CardOf(rune), out var b) ? b : default;
        }

        public static RuneCardType CardOf(RuneType r)
        {
            switch (r)
            {
                case RuneType.HeatUp:
                case RuneType.HeatDown: return RuneCardType.Heat;
                case RuneType.StateSolid:
                case RuneType.StateLiquid: return RuneCardType.State;
                case RuneType.LuminanceUp:
                case RuneType.LuminanceDown: return RuneCardType.Luminance;
                case RuneType.StickyUp:
                case RuneType.StickyDown: return RuneCardType.Sticky;
                case RuneType.DirectionAway:
                case RuneType.DirectionToward: return RuneCardType.Direction;
                default: return RuneCardType.Density;
            }
        }

        // --------------------------------------------------------- leveling --
        /// Every zombie kill feeds the level (host and clients both hear kills).
        public static void OnKill()
        {
            if (Instance == null || !RoundDirector.RunActive) return;
            Instance._kills++;
            if (Instance._kills >= 4 + Instance._level * 3)
            {
                Instance._kills = 0;
                Instance._level++;
                Instance._pending++;
                Instance._rerolls++;
                var p = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
                if (p != null) Juice.Chime(p.transform.position);
            }
        }

        /// Fresh run, fresh build (RoundDirector.StartRun calls this).
        public static void ResetRun()
        {
            _buffs.Clear();
            if (Instance == null) return;
            Instance._level = 1;
            Instance._kills = 0;
            Instance._pending = 0;
            Instance._rerolls = 0;
            Instance._open = false;
        }

        // ----------------------------------------------------------- choice --
        void Update()
        {
            if (!_open && _pending > 0 && Grimoire.HasAny(Grimoire.LocalPlayerId))
            {
                _open = true;
                Roll();
            }
            if (!_open) return;

            // THE TRANCE BUBBLE: choosing reads as "drawing" to zombie brains —
            // nearby zombies bliss out (they can't hit you)… and pile up around
            // you while you shop. Marko's double-edged sword, verbatim.
            var player = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (player != null)
                WorldEvents.Report(WorldEventKind.Ink, player.transform.position, 1f);

            var kb = Keyboard.current;
            if (kb == null) return;
            for (int i = 0; i < _offers.Count; i++)
            {
                var key = kb[(Key)((int)Key.Digit1 + i)];
                if (key != null && key.wasPressedThisFrame) { Pick(i); return; }
            }
            if (kb.rKey.wasPressedThisFrame && _rerolls > 0)
            {
                _rerolls--;
                Roll();
            }
        }

        void Roll()
        {
            _offers.Clear();
            var cards = new List<RuneCardType>(Grimoire.CardsOf(Grimoire.LocalPlayerId));
            if (cards.Count == 0) { _open = false; return; }
            int guard = 0;
            while (_offers.Count < 3 && guard++ < 40)
            {
                var o = new Offer
                {
                    Card = cards[Random.Range(0, cards.Count)],
                    Stat = Random.Range(0, StatName.Length)
                };
                bool dupe = false;
                foreach (var e in _offers)
                    if (e.Card == o.Card && e.Stat == o.Stat) dupe = true;
                if (!dupe) _offers.Add(o);
            }
        }

        void Pick(int i)
        {
            var o = _offers[i];
            var b = _buffs.TryGetValue(o.Card, out var cur) ? cur : default;
            switch (o.Stat)
            {
                case 0: b.More++; break;
                case 1: b.Fast++; break;
                case 2: b.Bond++; break;
                case 3: b.Potent++; break;
                case 4: b.Big++; break;
                case 5: b.Rapid++; break;
                default: b.Echo++; break;
            }
            _buffs[o.Card] = b;
            _pending--;
            DrawingWorld.Instance?.LogEvent($"POWERUP: {o.Card} rune — {StatName[o.Stat]}");

            Nova(); // the escape blast — shove the crowd that gathered

            _open = _pending > 0;
            if (_open) Roll();
        }

        /// Confirming a powerup BLASTS everything nearby with push force —
        /// real push particles (so it plays by the game's rules) plus a
        /// guaranteed direct shove, because this is your escape hatch.
        void Nova()
        {
            var p = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (p == null) return;
            Vector3 at = p.transform.position;

            Juice.Boom(at, 0.8f);
            WorldEvents.Report(WorldEventKind.Explosion, at, 2.5f);

            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a));
                SpellParticle.Emit(ParticleKind.Push, at + Vector3.up * 0.9f + dir * 0.5f, dir, 1.2f);
            }

            int n = Physics.OverlapSphereNonAlloc(at, 6f, _blast, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (_blast[i] == null) continue;
                var cr = _blast[i].GetComponentInParent<Creature>();
                if (cr != null) cr.KnockDown(1.6f);
                var rb = _blast[i].attachedRigidbody;
                if (rb == null) continue;
                Vector3 away = rb.worldCenterOfMass - at;
                away.y = 0.3f;
                if (away.sqrMagnitude < 0.01f) continue;
                rb.AddForce(away.normalized * 7f, ForceMode.VelocityChange);
            }
        }

        // -------------------------------------------------------------- HUD --
        void OnGUI()
        {
            if (!RoundDirector.RunActive && !_open) return;

            // always-on XP pip, tucked by the HP/ink bars
            int need = 4 + _level * 3;
            GUI.Label(new Rect(380, Screen.height - 46, 300, 20),
                $"LVL {_level}   {_kills}/{need} kills" + (_pending > 0 ? "   ⬆ LEVEL UP!" : ""));

            if (!_open) return;

            float w = 232f, h = 128f, gap = 14f;
            float total = _offers.Count * w + (_offers.Count - 1) * gap;
            float x0 = (Screen.width - total) / 2f;
            float y = Screen.height * 0.3f;

            GUI.Box(new Rect(x0 - 16, y - 58, total + 32, h + 106),
                "LEVEL UP — the horde is entranced by your glow… and closing in");

            for (int i = 0; i < _offers.Count; i++)
            {
                var o = _offers[i];
                var r = new Rect(x0 + i * (w + gap), y, w, h);
                var b = _buffs.TryGetValue(o.Card, out var cur) ? cur : default;
                int stacks = o.Stat switch
                {
                    0 => b.More, 1 => b.Fast, 2 => b.Bond, 3 => b.Potent,
                    4 => b.Big, 5 => b.Rapid, _ => b.Echo,
                };
                GUI.Box(r, $"[{i + 1}]  {o.Card.ToString().ToUpper()} — {StatName[o.Stat]}"
                    + $"\n\n{StatDesc[o.Stat]}"
                    + (stacks > 0 ? $"\n(owned ×{stacks})" : ""));
                if (GUI.Button(new Rect(r.x, r.yMax + 4, w, 24), "take")) { Pick(i); return; }
            }
            GUI.Label(new Rect(x0, y + h + 34, total, 22),
                _rerolls > 0 ? $"press 1-3 to choose · R = reroll ({_rerolls} left)" : "press 1-3 to choose");
        }
    }
}
