using TMPro;
using UnityEngine;

namespace SpellyZombie
{
    /// The little truth-teller (Marko's spec): finish a stroke and a small
    /// label floats over the ink saying what the game READS it as — keep
    /// editing and the label updates (each new reading replaces the old), so
    /// you always know what a seal will fire before you close one.
    public class RunePreview : MonoBehaviour
    {
        static RunePreview _current; // one at a time — the newest reading wins

        TextMeshPro _tm;   // TMP, not legacy TextMesh — emoji are sprites
        float _age;
        Color _color;
        const float Hold = 1.1f; // fully visible…
        const float Fade = 1.8f; // …then fades away slowly

        /// POOLED — ONE label for the whole session (Marko Aug 8: "those tmp's
        /// should be pooled like all effects"). It used to Destroy the old label
        /// and `new GameObject` + AddComponent<TextMeshPro> a fresh one on every
        /// reading, and building a TMP is not cheap: mesh, material and font
        /// atlas work each time. Harmless on the floor, where one drag is one
        /// reading — brutal on the BODY, where the pen crosses a bone every few
        /// centimetres and each crossing used to end a stroke and read again.
        /// (That second half is fixed at the source in SurfaceDrawer.)
        public static void Show(Vector3 worldPos, string text, Color color)
        {
            // == null also catches a label destroyed by a scene load
            if (_current == null)
            {
                var go = new GameObject("RunePreview");
                _current = go.AddComponent<RunePreview>();
                _current.BuildOnce();
            }
            _current.gameObject.SetActive(true);
            _current.Reuse(worldPos, text, color);
        }

        /// The expensive half, paid exactly once.
        void BuildOnce()
        {
            // legacy TextMesh has ONE font atlas and no sprite support, so a
            // rune's emoji could only ever be a tofu box (only ❄ survived —
            // it's the one glyph of the twelve that lives in a normal font)
            _tm = gameObject.AddComponent<TextMeshPro>();
            // Midline = centred BOTH ways. TMP's "Center" is horizontal-only
            // and top-anchored, which parked every icon up-left of the ink.
            _tm.alignment = TextAlignmentOptions.Midline;
            _tm.fontSize = 1.4f;
            _tm.enableWordWrapping = false;
            _tm.rectTransform.sizeDelta = new Vector2(2f, 2f);
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// The cheap half, paid per reading: move it, retext it, restart the fade.
        void Reuse(Vector3 pos, string text, Color color)
        {
            transform.position = pos;
            _color = color;
            _age = 0f;
            if (_tm == null) return;
            _tm.color = color;
            _tm.text = Ghost(text);
        }

        /// A reading is a HINT, not a billboard — the icon sits back so the
        /// ink stays the loudest thing on screen. Sprites ignore the label's
        /// colour (TMP tints sprites only if "Tint All Sprites" is on), so
        /// the alpha tag is what actually softens them.
        static string Ghost(string s) => $"<alpha=#B4>{s}";

        void LateUpdate()
        {
            _age += Time.deltaTime;
            var cam = Camera.main;
            if (cam == null && Camera.allCamerasCount > 0) cam = Camera.allCameras[0];
            if (cam != null) // always face the reader
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
            transform.position += Vector3.up * (0.06f * Time.deltaTime); // gentle rise

            float a = _age < Hold ? 1f : 1f - (_age - Hold) / Fade;
            if (a <= 0f)
            {
                gameObject.SetActive(false); // parked, not destroyed — Show reuses it
                return;
            }
            var c = _color;
            c.a *= Mathf.Clamp01(a);
            if (_tm != null) _tm.color = c;
        }
    }
}
