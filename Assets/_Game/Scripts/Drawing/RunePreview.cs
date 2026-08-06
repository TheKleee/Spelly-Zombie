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

        public static void Show(Vector3 worldPos, string text, Color color)
        {
            if (_current != null) Destroy(_current.gameObject);
            var go = new GameObject("RunePreview");
            _current = go.AddComponent<RunePreview>();
            _current.Build(worldPos, text, color);
        }

        void Build(Vector3 pos, string text, Color color)
        {
            transform.position = pos;
            _color = color;
            // legacy TextMesh has ONE font atlas and no sprite support, so a
            // rune's emoji could only ever be a tofu box (only ❄ survived —
            // it's the one glyph of the twelve that lives in a normal font)
            _tm = gameObject.AddComponent<TextMeshPro>();
            // Midline = centred BOTH ways. TMP's "Center" is horizontal-only
            // and top-anchored, which parked every icon up-left of the ink.
            _tm.alignment = TextAlignmentOptions.Midline;
            _tm.fontSize = 1.4f;
            _tm.color = color;
            _tm.enableWordWrapping = false;
            _tm.text = Ghost(text);
            var rt = _tm.rectTransform;
            rt.sizeDelta = new Vector2(2f, 2f);
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
                Destroy(gameObject);
                return;
            }
            var c = _color;
            c.a *= Mathf.Clamp01(a);
            if (_tm != null) _tm.color = c;
        }
    }
}
