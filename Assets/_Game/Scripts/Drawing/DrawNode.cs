using UnityEngine;

namespace SpellyZombie
{
    /// A single ink point stuck to a surface. Parented to whatever it was drawn on,
    /// so it rides moving objects - seals form and break from live node positions.
    /// No collider: pen raycasts pass straight through existing ink.
    public class DrawNode : MonoBehaviour
    {
        public Stroke Stroke { get; private set; }
        public int Index { get; private set; }

        /// Lasso split hands the tail nodes to a freshly created stroke.
        internal void SetStroke(Stroke stroke) => Stroke = stroke;

        /// The surface normal at this node - LIVE: it turns with the surface.
        public Vector3 SurfaceNormal => SurfaceDelta * _normalAtDraw;

        /// How this ink's surface has ROTATED since the ink was drawn. Ink on
        /// moving carriers (a tablet riding the camera, a posed body, a turned
        /// zombie) takes its reference frame WITH it - a rune must read the
        /// same no matter where its carrier now faces (the consistency
        /// rule: your orientation in the world never changes what a drawing
        /// is). Static world ink: identity, exactly the old behavior.
        public Quaternion SurfaceDelta =>
            _hasParentRot && transform.parent != null
                ? transform.parent.rotation * Quaternion.Inverse(_parentRotAtDraw)
                : Quaternion.identity;

        Vector3 _normalAtDraw;
        Quaternion _parentRotAtDraw;
        bool _hasParentRot;

        /// Body paint hands shell-drawn ink to a BONE afterwards: new parent,
        /// fresh rotation baseline (the frozen pose the ink was painted in IS
        /// its reference frame from here on).
        public void Rebase(Transform newParent)
        {
            transform.SetParent(newParent, true);
            _parentRotAtDraw = newParent.rotation;
            _hasParentRot = true;
        }

        /// True when the surface is a character or weapon (PersistentInkSurface in
        /// the parent chain) - such ink is never consumed by spell resolution.
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
            node._normalAtDraw = normal;
            if (surface != null)
            {
                node._parentRotAtDraw = surface.rotation;
                node._hasParentRot = true;
            }
            node.OnPersistentSurface = surface != null && surface.GetComponentInParent<PersistentInkSurface>() != null;
            return node;
        }

        void OnDestroy()
        {
            // plain field write only - safe during scene teardown
            Stroke?.MarkDirty();
        }
    }
}
