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

        /// How this ink's surface has rotated since the ink was drawn - a rune must
        /// read the same wherever its carrier now faces. Static world ink: identity.
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

            // A soft body's SKIN is bone-driven and lags the root, so ink on
            // the root swims across a blob that is sloshing. Bind to the
            // nearest bone at birth - the same handoff the body makes in
            // CharacterRig.EndBodyPaint, just done immediately.
            if (surface != null)
            {
                var blob = surface.GetComponentInParent<StateBlob>();
                if (blob != null)
                {
                    var bone = blob.NearestBone(go.transform.position);
                    if (bone != null) node.Rebase(bone);
                }
            }
            return node;
        }

        void OnDestroy()
        {
            // plain field write only - safe during scene teardown
            Stroke?.MarkDirty();
        }
    }
}
