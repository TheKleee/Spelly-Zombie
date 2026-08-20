using UnityEngine;

namespace SpellyZombie
{
    /// The effect primitives the threshold table points at. Each is one verb,
    /// built on systems that already exist - heat goes through GiveHeatTo,
    /// buffs through Sides.AddBuff, phase through BodyState.SetPhase, the
    /// meteor through Matter + MeteorRise. A table row = a name, a region and
    /// one of these ids; new spells reuse these or earn a new primitive.
    public static class SpellEffects
    {
        /// A fusion touching one target (lvl1) or a body inside its area (lvl2).
        public static void Apply(SpellTable.Row row, Collider target, Vector3 at,
            float power, float dt, int casterId)
        {
            if (row == null || target == null) return;
            switch (row.Effect)
            {
                case "flame":
                    // pour heat; ignition past IgnitePoint is Thermal's own
                    // threshold - flame spreading IS the reaction table working
                    SpellParticle.GiveHeatTo(target, 160f * power * dt);
                    break;

                case "zap":
                    Damage(target, 16f * power, "lightning");
                    Shove(target, Vector3.up * 2f, 3f * power);
                    break;

                case "heal":
                {
                    var pl = target.GetComponentInParent<SimpleFPSController>();
                    if (pl != null && !pl.IsDowned)
                        pl.Health = Mathf.Min(Sides.MaxHealthFor(Grimoire.LocalPlayerId),
                            pl.Health + row.Param * power * dt);
                    else Mend(target, row.Param * power * dt);
                    break;
                }

                case "buff":
                {
                    // raising a ceiling is the buff - cast on an enemy it is a
                    // weapon, their mending slows (never restricted to allies)
                    var pl = target.GetComponentInParent<SimpleFPSController>();
                    if (pl != null) Sides.AddBuff(Grimoire.LocalPlayerId, row.Param * power);
                    break;
                }

                case "invisible":
                {
                    var body = BodyState.Of(target);
                    if (body != null)
                        body.SetPhase(MatterPhase.Liquid, row.Param); // you become liquid state
                    break;
                }

                case "teleport":
                    // to where the slick seal was drawn - the caster's anchor
                    if (SealAnchors.TryGet(casterId, out var home))
                        Blink(target, home);
                    break;

                case "trail":
                    TrailMark.Wear(target.transform, row.Param);
                    break;

                case "sun":
                    SpellParticle.GiveHeatTo(target, 300f * power * dt);
                    break;

                case "cloud":
                {
                    var body = BodyState.Of(target);
                    if (body != null)
                        body.SetPhase(MatterPhase.Gas, 4f);  // the cloud makes you gas
                    Mend(target, 12f * power * dt);
                    break;
                }

                case "explode_away":
                {
                    // the victim teleports away, a blast stays, and the victim
                    // ARRIVES BUFFED - the double-edged sword, by ruling
                    Vector3 was = target.transform.position;
                    Blink(target, was + Random.onUnitSphere.WithY(0.2f) * 9f);
                    Boom(was, power);
                    var pl = target.GetComponentInParent<SimpleFPSController>();
                    if (pl != null) Sides.AddBuff(Grimoire.LocalPlayerId, 20f * power);
                    break;
                }

                case "steam":
                    Steam(at, power);
                    break;

                case "meteor":
                    Meteor(at, power, 1);
                    break;
            }
        }

        static Vector3 WithY(this Vector3 v, float y) => new Vector3(v.x, y, v.z);

        // ---- the small verbs the cases above share ----

        static void Damage(Collider c, float amount, string cause)
        {
            var d = c.GetComponentInParent<Damageable>();
            if (d != null) { d.TakeDamage(amount, cause); return; }
            var p = c.GetComponentInParent<SimpleFPSController>();
            if (p != null) p.TakeHit(Vector3.zero, amount, cause);
        }

        static void Shove(Collider c, Vector3 dir, float force)
        {
            var rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic) rb.AddForce(dir * force, ForceMode.VelocityChange);
        }

        static void Mend(Collider c, float amount)
        {
            var d = c.GetComponentInParent<Damageable>();
            if (d != null && d.MaxStrength > 0f)
                d.Health = Mathf.Min(d.MaxStrength, d.Health + amount);
        }

        static void Blink(Collider c, Vector3 to)
        {
            var root = c.attachedRigidbody != null ? c.attachedRigidbody.transform
                : c.GetComponentInParent<SimpleFPSController>()?.transform
                ?? c.transform;
            var cc = root.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;      // CC must be off to teleport
            root.position = to + Vector3.up * 0.3f;
            if (cc != null) cc.enabled = true;
            Juice.Chime(to);
        }

        /// Heat + chill meeting: the one gas substance. A scalding steam blob.
        public static void Steam(Vector3 at, float power)
        {
            var m = Matter.Spawn(SurfaceMaterialType.Water, MatterPhase.Gas,
                0.35f * Mathf.Max(0.5f, power), at + Vector3.up * 0.3f);
            if (m != null) m.Temperature = 130f;
            DrawingWorld.Instance?.LogEvent("fire and frost make SCALDING STEAM");
        }

        /// The meteor: a stone conjured overhead that rises, swells and dives
        /// at the spot. count > 1 = it is raining meteorites (lvl2).
        public static void Meteor(Vector3 at, float power, int count)
        {
            for (int i = 0; i < Mathf.Max(1, count); i++)
            {
                Vector3 spot = at;
                if (count > 1) spot += new Vector3(Random.insideUnitCircle.x, 0f,
                    Random.insideUnitCircle.y) * 4f;
                var rock = Matter.Spawn(SurfaceMaterialType.Stone, MatterPhase.Solid,
                    0.4f * Mathf.Max(0.5f, power), spot + Vector3.up * 0.5f);
                if (rock == null) continue;
                var rise = rock.gameObject.AddComponent<MeteorRise>();
                rise.Reach = power;
            }
        }

        static void Boom(Vector3 at, float power)
        {
            GrammarFX.PuffBurst(at, new Color(1f, 0.6f, 0.2f), 6);
            foreach (var c in Physics.OverlapSphere(at, 2.5f * power))
            {
                Damage(c, 10f * power, "the explosion left behind");
                Shove(c, (c.transform.position - at).normalized + Vector3.up * 0.5f, 6f * power);
            }
        }
    }

    /// Where each caster's slick seal was drawn - the teleport home.
    public static class SealAnchors
    {
        static readonly System.Collections.Generic.Dictionary<int, Vector3> _at =
            new System.Collections.Generic.Dictionary<int, Vector3>();
        public static void Set(int owner, Vector3 at) => _at[owner] = at;
        public static bool TryGet(int owner, out Vector3 at) => _at.TryGetValue(owner, out at);
    }

    /// The tracking trail a Trail fusion pins on a body.
    public class TrailMark : MonoBehaviour
    {
        float _left;
        TrailRenderer _tr;

        public static void Wear(Transform who, float seconds)
        {
            var root = who.GetComponentInParent<SimpleFPSController>()?.transform ?? who;
            var t = root.GetComponentInChildren<TrailMark>();
            if (t == null)
            {
                var go = new GameObject("TrailMark");
                go.transform.SetParent(root, false);
                go.transform.localPosition = Vector3.up * 0.8f;
                t = go.AddComponent<TrailMark>();
                t._tr = go.AddComponent<TrailRenderer>();
                t._tr.time = 6f;
                t._tr.startWidth = 0.12f;
                t._tr.endWidth = 0.01f;
                t._tr.material = MatterFX.Get(new Color(1f, 0.9f, 0.3f, 0.8f), MoteShade.Additive);
            }
            t._left = Mathf.Max(t._left, seconds);
        }

        void Update()
        {
            _left -= Time.deltaTime;
            if (_left <= 0f) Destroy(gameObject);
        }
    }
}
