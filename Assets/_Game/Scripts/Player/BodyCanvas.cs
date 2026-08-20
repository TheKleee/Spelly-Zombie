using UnityEngine;

namespace SpellyZombie
{
    /// A paintable shell over the player body (sharedMesh at identity under
    /// the body renderer). The LOCAL viewer's shell sits on layer 2 so your
    /// own pen ignores your own body; remote shells sit on the ink-canvas
    /// layer - you can paint everyone but yourself.
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
