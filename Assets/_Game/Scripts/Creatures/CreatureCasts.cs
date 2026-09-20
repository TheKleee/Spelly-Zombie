using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ WHAT A CREATURE CASTS (its Can do list): every entry but Charge is a
    /// spell from the book, used in turn. A spell leaves its head; a summoning
    /// spell raises its creature in front of it, for the same team, within a
    /// budget the whole map shares so summoners cannot flood it.
    public static class CreatureCasts
    {
        static readonly List<GameObject> _raised = new List<GameObject>();
        const float SummonSpread = 50f; // degrees either side of the aim a summon may stand

        /// How far out a summon stands from this body: past its own edge, and a stride more.
        public static float ClearReach(Transform body)
        {
            Bounds b = ShapeShift.FindObjectBounds(body);
            return Mathf.Max(b.extents.x, b.extents.z) + 2f;
        }

        /// The spell whose turn it is, or null when it casts nothing.
        public static string Pick(List<string> abilities, int turn)
        {
            if (abilities == null) return null;
            int count = CastableCount(abilities);
            if (count == 0) return null;
            int want = ((turn % count) + count) % count;
            foreach (var a in abilities)
                if (Castable(a) && want-- == 0) return a;
            return null;
        }

        static bool Castable(string ability) => ability != Zombie.Charge && SpellBook.Live.Spell(ability) != null;

        /// How many of these it can cast (Charge is a move, not a cast).
        public static int CastableCount(List<string> abilities)
        {
            int count = 0;
            if (abilities != null) foreach (var a in abilities) if (Castable(a)) count++;
            return count;
        }

        /// Seconds until the next cast: raising a creature takes longer than a throw.
        public static float Cooldown(string spellName)
        {
            var def = SpellBook.Live.Spell(spellName);
            return def != null && def.IsSummon ? DrawingConfig.CreatureSummonCooldown : DrawingConfig.GooThrowCooldown;
        }

        /// Cast it for `owner`: a spell from `muzzle` along `aim`, a summon `reach` metres
        /// in front of `feet`. False when nothing came of it.
        public static bool Cast(string spellName, int owner, Vector3 muzzle, Vector3 aim, Vector3 feet, float reach,
            Transform caster = null)
        {
            var def = SpellBook.Live.Spell(spellName);
            if (def == null)
            {
                Debug.LogWarning($"[SpellyZombie] a creature wants to cast '{spellName}' but the book has no such spell.");
                return false;
            }
            if (def.IsSummon)
            {
                Vector3 flat = new Vector3(aim.x, 0f, aim.z);
                if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
                // clear of its body, somewhere in front, so the raised never stand on each other
                flat = Quaternion.Euler(0f, Random.Range(-SummonSpread, SummonSpread), 0f) * flat.normalized;
                return Summon(def, owner, feet + flat * reach);
            }

            var p = SpellParticle.Emit(ParticleKind.Push, muzzle, aim, 1.4f);
            if (p == null) return false;
            // thrown like a player's throw: it leaves through the caster and
            // its areas raise where it lands, never at the head
            p.PrimeToBlow(caster);
            // NOT Clamped(): the clamp floors Strength at 0, which stripped
            // the goo's -9 bite and stopped it ever fusing into Goo at all
            p.Data = def.Payload;
            p.OwnerId = owner;
            p.FromMinion = true;
            p.SrcSize = DrawingConfig.RuneSizeMin * 2f;
            p.GrammarLevel = def.Level;   // a lvl2 hit lands on its whole area
            p.Vel = aim * 16f;
            p.Wake();
            p.RefreshIdentity_Public();
            return true;
        }

        static bool Summon(SpellDef def, int owner, Vector3 spot)
        {
            var creature = SpellBook.Live.Creature(def.Creature);
            if (creature == null) return false;
            _raised.RemoveAll(g => g == null);
            if (_raised.Count >= DrawingConfig.CreatureSummonBudget) return false;
            // on the floor, never on the ink or inside a shell
            int floorMask = Physics.DefaultRaycastLayers
                & ~(1 << InkCanvasLayer.Layer) & ~(1 << VesselShell.Layer);
            if (Physics.Raycast(spot + Vector3.up * 3f, Vector3.down, out var stand, 10f, floorMask, QueryTriggerInteraction.Ignore)
                && stand.normal.y > 0.55f)
                spot = stand.point + Vector3.up * 0.15f;
            var body = Spell.RaiseBody(creature, owner, spot, 1f);
            if (body == null) return false;
            _raised.Add(body);
            return true;
        }
    }
}
