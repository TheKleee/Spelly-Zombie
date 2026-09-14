using UnityEngine;

namespace SpellyZombie
{
    /// The object's claim: put this on a prefab root and size the box around
    /// the whole thing. The spawner keeps every other spawn out of it; paths
    /// never cut through it and reach it only at PathPoint children (doors).
    /// Added in the editor it fits itself to the model; drag its faces in the
    /// Scene view to adjust. It turns and scales with the object it claims for.
    public class ObjectBox : MonoBehaviour
    {
        [Tooltip("The claimed space in local metres, centered on Center. Box bottom = the ground line.")]
        public Vector3 Size = new Vector3(2f, 2f, 2f);
        [Tooltip("Local offset of the claim, for prefabs whose pivot is not at the middle of their footprint.")]
        public Vector3 Center = new Vector3(0f, 1f, 0f);

        public Bounds LocalArea => new Bounds(Center, Size);

        void Reset() => FitToModel();

        /// Sizes the box to the meshes under this object, in its own local units.
        [ContextMenu("Fit To Model")]
        public void FitToModel()
        {
            var b = InteriorField.UprightBounds(gameObject, Quaternion.Inverse(transform.rotation));
            if (b.size.sqrMagnitude < 1e-8f)
            {
                Debug.LogWarning($"[SpellyZombie] The Object Box on '{name}' found no meshes to fit.", this);
                return;
            }
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, "Fit Object Box");
#endif
            Vector3 s = transform.lossyScale;
            Center = Div(b.center, s);
            Vector3 size = Div(b.size, s);
            Size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        }

        static Vector3 Div(Vector3 v, Vector3 s) => new Vector3(
            v.x / (Mathf.Abs(s.x) > 1e-6f ? s.x : 1f),
            v.y / (Mathf.Abs(s.y) > 1e-6f ? s.y : 1f),
            v.z / (Mathf.Abs(s.z) > 1e-6f ? s.z : 1f));

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.6f, 0.15f, 0.9f);
            Gizmos.DrawWireCube(Center, Size);
        }
    }
}
