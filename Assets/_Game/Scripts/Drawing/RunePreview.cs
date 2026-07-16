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

        TextMesh _tm;
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
            _tm = gameObject.AddComponent<TextMesh>();
            _tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _tm.text = text;
            _tm.fontSize = 64;
            _tm.characterSize = 0.006f;
            _tm.anchor = TextAnchor.MiddleCenter;
            _tm.alignment = TextAlignment.Center;
            _tm.color = color;
            var mr = GetComponent<MeshRenderer>();
            if (_tm.font != null) mr.material = _tm.font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

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
