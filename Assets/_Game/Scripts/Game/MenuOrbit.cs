using UnityEngine;

namespace SpellyZombie
{
    /// Marko's menu staging v2: the WORLD stays put (it's a real terrain
    /// now) and the CAMERA circles it the way a turntable used to feel —
    /// same relative motion, zero moving geometry. Spinning pauses the
    /// moment ink flows and resumes after 3 consecutive quiet seconds.
    public class MenuOrbit : MonoBehaviour
    {
        [Tooltip("Point the camera circles: the cauldron / diorama center. If empty, the legacy Turntable wiring is used as the pivot.")]
        public Transform Pivot;

        [Tooltip("Legacy field (was the object being rotated), kept so existing scene wiring works: it now serves as the pivot when Pivot is empty.")]
        public Transform Turntable;

        [Tooltip("Camera to orbit. Empty = Camera.main.")]
        public Transform Cam;

        public float DegreesPerSecond = 6f;

        void LateUpdate()
        {
            var pivot = Pivot != null ? Pivot : Turntable;
            if (pivot == null) return;
            var cam = Cam != null ? Cam
                : Camera.main != null ? Camera.main.transform : null;
            if (cam == null) return;

            if (Time.time - MenuCauldron.LastDrawTime < 3f) return; // ink wins
            cam.RotateAround(pivot.position, Vector3.up, DegreesPerSecond * Time.deltaTime);
        }
    }
}
