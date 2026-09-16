using UnityEngine;

namespace SpellyZombie
{
    /// Delivers impulses to players. Over ShoveBreaksDrawing the hit ends any
    /// draw/easel mode first, so the impulse lands on a body free to move
    /// (the controller eats shoves while a mode is open). Every force source
    /// should route through here, not TakeHit directly.
    public static class Shove
    {
        /// Hit a player with an impulse and optional damage. Breaks drawing
        /// first when the impulse is over the threshold.
        public static void Hit(SimpleFPSController player, Vector3 impulse,
            float damage, string cause = null, int by = -1, bool viaMinion = false)
        {
            if (player == null) return;

            // each mode pins the body, detaches the camera, or eats the shove -
            // a big enough hit must end them all here first
            if (impulse.magnitude < DrawingConfig.ShoveBreaksDrawing)
            {
                player.TakeHit(impulse, damage, cause, by, viaMinion);
                return;
            }

            // camera after a blast: unchanged unless a mode forced it. Body
            // pose lands in third person; shape/pose/overwatch/paint land in
            // first person.
            bool wasShaped = ShapeShift.LocalIsShaped;
            bool wasAcolyteMode = wasShaped || ShapeShift.PoseOpen || ZombieWatch.IsOpen;
            bool wasPaint = SelfPaint.IsActive;
            bool wasPose = PoseGrab.IsOpen;
            bool wasInAMode = wasAcolyteMode || wasPaint || wasPose || HeldWeapon.DrawMode;

            HeldWeapon.CancelDrawMode();
            SelfPaint.Blown();
            PoseGrab.Blown();
            ShapeShift.Blown();
            ZombieWatch.Blown();

            // asked for by name, never toggled - a toggle would land the
            // victim in the wrong view
            if (wasPose)
            {
                // body pose keeps the view on your own thrown body
                player.EnterThirdPerson();
            }
            else if (wasAcolyteMode || wasPaint)
            {
                // first person also strips a shaped acolyte's disguise -
                // ShapeShift's own rule unwears on the next frame (deliberate)
                player.EnterFirstPerson();
            }
            // no mode forced a camera change - leave it as it was

            if (wasInAMode)
                DrawingWorld.Instance?.LogEvent("the blast throws you out of it");

            player.TakeHit(impulse, damage, cause, by, viaMinion);
        }

        static readonly Collider[] _blastHits = new Collider[48];

        /// A radial blast: players shoved with distance falloff (acolytes
        /// shoved but never damaged - poison is corruption, blasts are
        /// physics), loose props thrown. One implementation for the zombie
        /// detonation and the acolyte death burst.
        /// `shipKicks` false: every client runs this same blast on its own
        /// body (the FieldMsg mirror), so puppets get no kick from here.
        public static void Blast(Vector3 at, float radius, float power,
            float baseDamage, string cause, Rigidbody except = null,
            int by = -1, bool viaMinion = false, bool shipKicks = true)
        {
            Juice.Thud(at);

            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || p.IsDowned) continue;
                Vector3 away = p.transform.position - at;
                away.y = 0f;
                float dist = away.magnitude;
                if (dist > radius) continue;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;

                float t = 1f - Mathf.Clamp01(dist / Mathf.Max(0.1f, radius));
                bool corrupted = Sides.IsAcolytePlayer(p);
                Hit(p,
                    away.normalized * power * t + Vector3.up * power * 0.3f * t,
                    corrupted ? 0f : baseDamage * t,
                    cause, by, viaMinion);
            }

            // friends' puppets (host only): the same falloff, shipped to the
            // body that can move; the wound lands on the puppet's Element
            if (NetGame.IsAuthority)
                foreach (var av in NetAvatar.All)
                {
                    if (av == null || av.Downed) continue;
                    Vector3 away = av.transform.position - at;
                    away.y = 0f;
                    float dist = away.magnitude;
                    if (dist > radius) continue;
                    if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
                    float t = 1f - Mathf.Clamp01(dist / Mathf.Max(0.1f, radius));
                    int owner = NetSync.OwnerIdOf(av.Id);
                    Vector3 impulse = away.normalized * power * t + Vector3.up * power * 0.3f * t;
                    if (shipKicks) // their own Shove.Hit breaks their modes, TakeHit decides the fall
                        NetSync.SendKick(owner, impulse, false, true);
                    if (baseDamage > 0f && Sides.Of(owner) != Side.Acolyte)
                        av.GetComponent<Element>()?.TakeDamage(baseDamage * t, cause, by, viaMinion);
                }

            int n = Physics.OverlapSphereNonAlloc(at, radius, _blastHits,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (_blastHits[i] == null) continue;
                var rb = _blastHits[i].attachedRigidbody;
                if (rb != null && !rb.isKinematic && rb != except)
                {
                    Element.TrackLoose(rb); // the clients see it fly
                    rb.AddExplosionForce(power * 18f, at, radius, 0.4f, ForceMode.Impulse);
                }
            }
        }
    }
}
