using UnityEngine;

namespace SpellyZombie
{
    /// ★ WHAT IS ALLOWED TO BE AN EFFECT.
    ///
    /// Most of what a spell does is not in here and must not be: a particle
    /// hands its numbers to whatever it touches, and burning, freezing,
    /// sticking, slipping and dying all follow from those numbers on their own.
    /// Anything written here that merely pushes a number is billing the target
    /// a second time at a different rate.
    ///
    /// What belongs here is the two kinds of thing numbers cannot express:
    ///
    ///   EVENTS - a meteor falls, a target teleports, a sun appears. These
    ///   happen once, somewhere, and no amount on an axis describes them.
    ///
    ///   SELECTIVE effects - poison eats only the living. A payload reaches
    ///   everything it touches by definition, so anything that must choose its
    ///   victims needs a case.

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
                case "poison":
                    // POISON EATS THE LIVING ONLY - a mind is what living
                    // means, so a wall in a puddle is just a wet wall. This one
                    // stays a case because it is SELECTIVE: the payload reaches
                    // everything it touches, and poison must not.
                    {
                        var victim = target.GetComponentInParent<Element>();
                        if (victim != null && victim.Data.Alive)
                            victim.TakeDamage(row.Param * power * dt, "the corruption", casterId);
                    }
                    break;

                // "flame" HAS NO CASE. It used to pour 160 heat a second here,
                // on top of the heat the particle already handed over as part
                // of its payload - so every flame billed its target twice, at
                // two unrelated rates. Being hot IS the flame; the row exists
                // to NAME that region and to say it spreads, not to do it again.

                case "zap":
                    Damage(target, 16f * power, "lightning");
                    Shove(target, Vector3.up * 2f, 3f * power);
                    break;

                case "heal":
                {
                    // THE TARGET's ceiling, not the caster's - healing someone
                    // else used to cap them at whatever the local player's
                    // maximum happened to be.
                    float amount = row.Param * power * dt;
                    int who = NetSync.OwnerOfBody(target);
                    if (NetSync.PushPlayerFx(who, 1, amount)) break;

                    var pl = target.GetComponentInParent<SimpleFPSController>();
                    if (pl != null && !pl.IsDowned)
                        pl.Health = Mathf.Min(Sides.MaxHealthFor(who),
                            pl.Health + amount);
                    else Mend(target, amount);
                    break;
                }

                case "buff":
                {
                    // raising a ceiling is the buff - cast on an enemy it is a
                    // weapon, their mending slows (never restricted to allies).
                    // It raises the TARGET's ceiling; it used to raise the
                    // caster's, so buffing an enemy buffed yourself.
                    int who = NetSync.OwnerOfBody(target);
                    if (who < 0) break;
                    if (NetSync.PushPlayerFx(who, 2, row.Param * power)) break;
                    Sides.AddBuff(who, row.Param * power);
                    break;
                }

                case "invisible":
                    // half gone while it lasts. Visibility is its own channel:
                    // it does NOT claim you changed state, because state is
                    // what your weight against the medium already says.
                    FadeBody(target, DrawingConfig.FadeTransparency, row.Param);
                    break;

                case "teleport":
                    // to where the slick seal was drawn - the caster's anchor
                    if (SealAnchors.TryGet(casterId, out var home))
                    {
                        if (NetSync.PushPlayerFx(NetSync.OwnerOfBody(target), 4,
                                0f, MatterPhase.Solid, home)) break;
                        Blink(target, home);
                    }
                    break;

                case "trail":
                    if (NetSync.PushPlayerFx(NetSync.OwnerOfBody(target), 5, row.Param)) break;
                    TrailMark.Wear(target.transform, row.Param);
                    break;

                case "sun":
                    SpellParticle.GiveHeatTo(target, 300f * power * dt);
                    break;

                case "cloud":
                {
                    // three quarters gone - harder to cast, so it hides more
                    FadeBody(target, DrawingConfig.FadeCloud, row.Param);
                    int who = NetSync.OwnerOfBody(target);
                    if (!NetSync.PushPlayerFx(who, 1, 12f * power * dt))
                        Mend(target, 12f * power * dt);
                    break;
                }

                case "explode_away":
                {
                    // the victim teleports away, a blast stays, and the victim
                    // ARRIVES BUFFED - the double-edged sword, by ruling
                    Vector3 was = target.transform.position;
                    Vector3 to = was + Random.onUnitSphere.WithY(0.2f) * 9f;
                    int who = NetSync.OwnerOfBody(target);

                    // the VICTIM arrives buffed, by ruling - it used to buff
                    // whoever was local, which on a host meant the caster
                    if (!NetSync.PushPlayerFx(who, 4, 0f, MatterPhase.Solid, to))
                        Blink(target, to);
                    Boom(was, power);
                    if (who >= 0 && !NetSync.PushPlayerFx(who, 2, 20f * power))
                        Sides.AddBuff(who, 20f * power);
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
            // Element IS the network law now: it knows its own id, asks the
            // host when it is not one, and the host answers to everybody.
            var d = c.GetComponentInParent<Element>();
            if (d != null) { d.TakeDamage(amount, cause); return; }
            var p = c.GetComponentInParent<SimpleFPSController>();
            if (p != null) p.TakeHit(Vector3.zero, amount, cause);
        }

        /// Fade whatever this is - player, creature or crate. Works the same on
        /// all of them, and reaches a remote player's own machine.
        static void FadeBody(Collider c, float visible, float seconds)
        {
            if (seconds <= 0f) seconds = 1f;
            if (NetSync.PushPlayerFx(NetSync.OwnerOfBody(c), 6, seconds,
                    MatterPhase.Solid, new Vector3(visible, 0f, 0f))) return;

            var view = c.GetComponentInParent<StateView>()
                    ?? c.GetComponentInChildren<StateView>();
            if (view != null) view.Fade(visible, seconds);
        }

        static void Shove(Collider c, Vector3 dir, float force)
        {
            var rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic) rb.AddForce(dir * force, ForceMode.VelocityChange);
        }

        static void Mend(Collider c, float amount)
        {
            var d = c.GetComponentInParent<Element>();
            if (d != null && d.MaxStrength > 0f)
                d.Health = Mathf.Min(d.MaxStrength, d.Health + amount);
        }

        /// Move a body somewhere, CharacterController and all. Public because
        /// the MovesToOrigin row uses this same one - there must not be two
        /// teleports that disagree about how to put a player down.
        public static void Blink(Collider c, Vector3 to)
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
