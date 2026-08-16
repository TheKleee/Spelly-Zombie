using UnityEngine;

namespace SpellyZombie
{
    /// YOUR BODY IS SOMEONE ELSE'S CANVAS ("make your body
    /// paintable by others cause it's funny").
    ///
    /// Nothing here is new machinery - it is the zombie's paint shell worn by a
    /// player, assembled from parts that already exist:
    ///   · the MOUNT is the axiom-approved one (sharedMesh at identity under
    ///     the body renderer - CharacterRig's own accepted shell)
    ///   · the LAYER trick is the wall-canvas system: raycast-visible,
    ///     physics-invisible (InkCanvasLayer)
    ///   · the BODY is resolved through CharacterRig.BodySmr, never a fresh
    ///     depth-first search (the grimoire-as-body lesson)
    ///   · ink ROUTES TO THE LIMBS via the same NearestLimbSurface the
    ///     self-paint shell uses, so it rides the animation and replicates
    ///     through the existing mixamorig body-ink path - MP parity for free
    ///
    /// THE ONE TWIST: whose pen may see it. Your own pen must ignore your own
    /// body (layer 2's whole job - a sprint-crossing forearm once won the aim),
    /// but OTHER pens must hit it. Per machine, the LOCAL viewer's shell sits
    /// on layer 2 and every remote player's sits on the ink-canvas layer - so
    /// on your screen you can paint everyone but yourself, which is exactly
    /// the joke as ruled. Self-painting keeps its own easel (R), unchanged.
    public class BodyCanvas : MonoBehaviour
    {
        float _retryIn;

        void Update()
        {
            // the rig assembles over several frames - retry gently, then stop
            if ((_retryIn -= Time.deltaTime) > 0f) return;
            _retryIn = 0.5f;

            var rig = GetComponent<CharacterRig>();
            var smr = rig != null ? rig.BodySmr : null;
            if (smr == null || smr.sharedMesh == null) return;
            if (smr.transform.Find("BodyCanvas") != null) { enabled = false; return; }

            var shell = new GameObject("BodyCanvas");
            shell.transform.SetParent(smr.transform, false);
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localRotation = Quaternion.identity;
            shell.transform.localScale = Vector3.one;
            shell.AddComponent<MeshCollider>().sharedMesh = smr.sharedMesh;

            var pilot = GetComponent<SimpleFPSController>();
            bool mine = pilot != null && pilot.IsLocalViewer;
            shell.layer = mine ? 2 : InkCanvasLayer.Layer;

            enabled = false; // built once; the shell needs no ticking
        }
    }
}
