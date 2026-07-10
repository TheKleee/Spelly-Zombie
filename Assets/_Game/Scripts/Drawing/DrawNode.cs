using UnityEngine;

namespace SpellyZombie
{
    /// A single ink point stuck to a surface. Parented to whatever it was drawn on,
    /// so it rides moving objects — seals form and break from live node positions.
    /// No collider: pen raycasts pass straight through existing ink.
    public class DrawNode : MonoBehaviour
    {
        public Stroke Stroke { get; private set; }
        public int Index { get; private set; }

        /// Lasso split hands the tail nodes to a freshly created stroke.
        internal void SetStroke(Stroke stroke) => Stroke = stroke;
        public Vector3 SurfaceNormal { get; private set; }

        /// True when the surface is a character or weapon (PersistentInkSurface in
        /// the parent chain) — such ink is never consumed by spell resolution.
        public bool OnPersistentSurface { get; private set; }

        public static DrawNode Create(Stroke stroke, int index, Vector3 position, Vector3 normal, Transform surface)
        {
            var go = new GameObject($"Node_{stroke.Id}_{index}");
            go.transform.position = position + normal * DrawingConfig.SurfaceOffset;
            if (surface != null)
                go.transform.SetParent(surface, worldPositionStays: true);
            var node = go.AddComponent<DrawNode>();
            node.Stroke = stroke;
            node.Index = index;
            node.SurfaceNormal = normal;
            node.OnPersistentSurface = surface != null && surface.GetComponentInParent<PersistentInkSurface>() != null;
            return node;
        }

        void OnDestroy()
        {
            // plain field write only — safe during scene teardown
            Stroke?.MarkDirty();
        }
    }
}
