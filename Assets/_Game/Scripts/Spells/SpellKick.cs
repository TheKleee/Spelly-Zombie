using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The kick: a spell leaving the hand, or waking in it, shoves the caster
    /// and whatever stands near it away, by its power and size. A big one
    /// throws the caster off their feet: out of their own blast, and funny.
    public static class SpellKick
    {
        static readonly HashSet<object> _seen = new HashSet<object>();

        /// How hard this spell kicks, in m/s.
        public static float Strength(SpellParticle p) =>
            DrawingConfig.SpellKick * Mathf.Max(0f, p.Power)
            * Mathf.Clamp(p.SrcSize / Mathf.Max(0.05f, DrawingConfig.RuneSizeMin), 0.5f, 6f);

        /// `throwDir` is the throw (zero for a release in place): the caster is
        /// kicked back along it, else straight away from the spell. `caster` is
        /// a body on this machine or a friend's stand-in; `casterOwner` finds
        /// the friend's machine.
        public static void Apply(SpellParticle p, Vector3 throwDir, Transform caster, int casterOwner)
        {
            if (p == null) return;
            float k = Strength(p);
            if (k < 0.2f) return;
            Vector3 at = p.transform.position;
            float knockAt = DrawingConfig.SpellKickKnock;
            if (caster != null)
            {
                Vector3 away = throwDir.sqrMagnitude > 0.01f ? -throwDir.normalized : Flat(caster.position - at);
                Shove(caster, casterOwner, (away + Vector3.up * 0.35f).normalized * k, k >= knockAt);
            }

            // whatever else stands close gets its share, fading with distance
            float r = DrawingConfig.SpellKickRadius;
            int n = Physics.OverlapSphereNonAlloc(at, r, GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Ignore);
            _seen.Clear();
            for (int i = 0; i < n; i++)
            {
                var c = GrammarFX.ScanBuffer[i];
                if (c == null || c.transform.IsChildOf(p.transform)) continue;
                if (caster != null && c.transform.IsChildOf(caster)) continue;
                Vector3 to = c.bounds.center - at;
                float share = k * (1f - Mathf.Clamp01(to.magnitude / r));
                if (share < 0.2f) continue;
                Vector3 push = (Flat(to) + Vector3.up * 0.35f).normalized * share;
                var pl = c.GetComponentInParent<SimpleFPSController>();
                if (pl != null)
                {
                    if (_seen.Add(pl)) Shove(pl.transform, -1, push, share >= knockAt);
                    continue;
                }
                var av = c.GetComponentInParent<NetAvatar>();
                if (av != null)
                {
                    if (_seen.Add(av)) NetSync.SendKick(NetSync.OwnerIdOf(av.Id), push, share >= knockAt);
                    continue;
                }
                var rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic && _seen.Add(rb)) rb.AddForce(push, ForceMode.VelocityChange);
            }
        }

        static void Shove(Transform body, int owner, Vector3 impulse, bool knock)
        {
            var pl = body.GetComponent<SimpleFPSController>();
            if (pl != null)
            {
                if (pl.IsDowned) return;
                pl.TakeHit(impulse, 0f, "kick");
                if (knock) pl.KnockDown(1.1f);
                return;
            }
            if (owner >= 0) NetSync.SendKick(owner, impulse, knock); // a friend's body lives on their machine
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.back;
        }
    }
}
