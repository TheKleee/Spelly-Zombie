using UnityEngine;

namespace SpellyZombie
{
    // NOTE: the ComboBook (named combo recipes, banners, resonance boosts) was
    // REMOVED by design verdict: no predetermined outcomes — "I want mayhem."
    // Zones run, physics composes, nothing announces or nudges. Only the
    // generic banner component below survives (round announcements use it).

    /// Big centered announcement that fades — used by the RoundDirector for
    /// round starts, wipes, and victories.
    public class ComboBanner : MonoBehaviour
    {
        string _text;
        Color _color;
        float _life = 2.4f, _age;
        GUIStyle _style;

        public static void Show(string text, Color color)
        {
            var go = new GameObject("ComboBanner");
            var b = go.AddComponent<ComboBanner>();
            b._text = text;
            b._color = color;
        }

        void Update()
        {
            _age += Time.deltaTime;
            if (_age > _life) Destroy(gameObject);
        }

        void OnGUI()
        {
            if (_style == null)
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 46,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            float t = Mathf.Clamp01(_age / _life);
            float alpha = 1f - t * t;
            float rise = 20f * t;

            var rect = new Rect(0, Screen.height * 0.22f - rise, Screen.width, 60);
            _style.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.6f);
            GUI.Label(new Rect(rect.x + 3, rect.y + 3, rect.width, rect.height), _text, _style);
            _style.normal.textColor = new Color(_color.r, _color.g, _color.b, alpha);
            GUI.Label(rect, _text, _style);
        }
    }
}
