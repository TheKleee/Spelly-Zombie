using TMPro;
using UnityEngine;

namespace SpellyZombie
{
    /// Floating label over freshly drawn ink showing what the game reads it as;
    /// each new reading replaces the old.
    public class RunePreview : MonoBehaviour
    {
        static RunePreview _current; // one at a time - the newest reading wins

        TextMeshPro _tm;   // TMP, not legacy TextMesh - emoji are sprites
        float _age;
        Color _color;
        const float Hold = 1.1f; // fully visible…
        const float Fade = 1.8f; // …then fades away slowly

        /// Pooled - one label for the whole session.
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
            _tm = gameObject.AddComponent<TextMeshPro>();
            // Midline = centred both ways (TMP's "Center" is horizontal-only)
            _tm.alignment = TextAlignmentOptions.Midline;
            _tm.fontSize = 1.4f;
            _tm.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
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

        /// Sprites ignore the label colour (TMP tints sprites only with "Tint All
        /// Sprites" on) - the alpha tag is what softens them.
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
                gameObject.SetActive(false); // parked, not destroyed - Show reuses it
                return;
            }
            var c = _color;
            c.a *= Mathf.Clamp01(a);
            if (_tm != null) _tm.color = c;
        }
    }
}
