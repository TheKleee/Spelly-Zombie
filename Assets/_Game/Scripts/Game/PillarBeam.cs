using UnityEngine;

namespace SpellyZombie
{
    /// Lobby pillar visual: a translucent beam of light plus a ground disc,
    /// built only when the pillar object has no renderer of its own. No
    /// colliders: the beam cannot be inked, scanned apart, or bumped into.
    public static class PillarBeam
    {
        static MaterialPropertyBlock _mpb;

        /// Returns the beam renderer (and the ground glow) - or null when
        /// the object already has visuals of its own.
        public static Renderer Build(Transform at, out Renderer groundGlow)
        {
            groundGlow = null;
            if (at.GetComponentsInChildren<Renderer>(true).Length > 0) return null;

            var beam = Shaft(at, "Beam",
                new Vector3(0f, 1.6f, 0f), new Vector3(0.55f, 1.6f, 0.55f));
            groundGlow = Shaft(at, "GroundGlow",
                new Vector3(0f, 0.02f, 0f), new Vector3(1.4f, 0.012f, 1.4f));
            return beam;
        }

        static Renderer Shaft(Transform at, string name, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(at, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.layer = 2; // invisible to the pen
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // shared material; per-beam color rides a property block
            r.sharedMaterial = MatterFX.Get(new Color(1f, 1f, 1f, 0.45f), MoteShade.Transparent);
            return r;
        }

        /// Point on the pillar column at the player's aim height, so the
        /// floating E badge follows the look instead of a fixed altitude.
        public static Vector3 BadgePoint(Transform pillar)
        {
            var cam = Camera.main;
            if (cam == null) return pillar.position + Vector3.up * 1.5f;
            Vector3 axis = pillar.position;
            Vector3 eye = cam.transform.position;
            Vector3 fwd = cam.transform.forward;
            float dist = new Vector2(axis.x - eye.x, axis.z - eye.z).magnitude;
            float flatLen = Mathf.Max(0.05f, new Vector2(fwd.x, fwd.z).magnitude);
            float y = eye.y + fwd.y / flatLen * dist; // the view ray's height at the pillar
            y = Mathf.Clamp(y, axis.y + 0.25f, axis.y + 2.4f);
            return new Vector3(axis.x, y, axis.z);
        }

        /// Tint via property block - drag-repaints happen every frame.
        public static void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            // 0.45 is the ceiling; a caller may ask for thinner (ghosts do)
            c.a = Mathf.Min(c.a >= 1f ? 0.45f : c.a, 0.45f);
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            r.SetPropertyBlock(_mpb);
        }
    }
}
