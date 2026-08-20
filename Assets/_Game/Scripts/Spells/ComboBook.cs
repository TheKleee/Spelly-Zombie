using UnityEngine;

namespace SpellyZombie
{
    /// Big centered announcement on the curtain banner that fades and rises -
    /// used by the RoundDirector for round starts, wipes, and victories.
    public class ComboBanner : MonoBehaviour
    {
        float _life = 2.4f, _age;
        RectTransform _ui;
        CanvasGroup _fade;

        static ComboBanner _live;

        public static void Show(string text, Color color)
        {
            // newest banner wins; the old one is destroyed
            if (_live != null)
            {
                UIKit.Retire(_live._ui);
                Destroy(_live.gameObject);
            }
            var go = new GameObject("ComboBanner");
            DontDestroyOnLoad(go); // its group lives on the persistent canvas, so survive scene loads
            var b = go.AddComponent<ComboBanner>();
            _live = b;
            b.BuildUI(text, color);
        }

        void BuildUI(string text, Color color)
        {
            var skin = UISkin.I;
            _ui = UIKit.Group(UIKit.Root, "Announcement");
            UIKit.Place(_ui, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(820f, 110f));
            _fade = _ui.gameObject.AddComponent<CanvasGroup>();

            var cloth = UIKit.Panel(_ui, skin != null ? skin.BannerCurtain : null,
                skin != null ? Color.white : new Color(0f, 0f, 0f, 0.55f));
            UIKit.Stretch((RectTransform)cloth.transform);

            var label = UIKit.Label(_ui, text, 40, color, TextAnchor.MiddleCenter, true);
            label.text = text;   // an adopted prefab label keeps its authored text - override it
            label.color = color;
            var lr = (RectTransform)label.transform;
            UIKit.Stretch(lr);
            lr.offsetMin = new Vector2(60f, 8f);
            lr.offsetMax = new Vector2(-60f, -8f);
        }

        void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _life);
            if (_fade != null) _fade.alpha = 1f - t * t;
            if (_ui != null) _ui.anchoredPosition = new Vector2(0f, -170f + 24f * t);
            if (_age > _life)
            {
                UIKit.Retire(_ui);
                if (_live == this) _live = null;
                Destroy(gameObject);
            }
        }
    }
}
