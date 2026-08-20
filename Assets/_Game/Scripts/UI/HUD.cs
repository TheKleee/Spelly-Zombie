using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// The survival HUD: no bars, red screen edges for hurt, perk badges,
    /// round banner. Death has no UI at all.
    public class HUD : MonoBehaviour
    {
        static HUD _i;

        RectTransform _group;
        Image _vignette;
        Text _bannerText;
        RectTransform _banner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (_i != null) return;
            var go = new GameObject("SZ_HUD");
            DontDestroyOnLoad(go);
            _i = go.AddComponent<HUD>();
        }

        void Start() => BuildUI();

        void BuildUI()
        {
            var skin = UISkin.I;
            _group = UIKit.Group(UIKit.Root, "HUD");
            UIKit.Stretch(_group);
            _group.SetAsFirstSibling(); // everything else draws over the HUD

            // hurt vignette (no HP bar); stale baked copies die first - the runtime sprite can't serialize into the prefab
            for (int i = _group.childCount - 1; i >= 0; i--)
            {
                var stale = _group.GetChild(i);
                if (stale.name == "HurtVignette") DestroyImmediate(stale.gameObject);
            }
            _vignette = new GameObject("HurtVignette").AddComponent<Image>();
            _vignette.transform.SetParent(_group, false);
            UIKit.Stretch((RectTransform)_vignette.transform);
            _vignette.sprite = Sprite.Create(VignetteTex(), new Rect(0f, 0f, 128f, 128f),
                Vector2.one * 0.5f);
            _vignette.raycastTarget = false;
            _vignette.color = new Color(0.55f, 0f, 0f, 0f);
            _vignette.transform.SetAsFirstSibling(); // under everything in the HUD

            // top-center round banner; side insets keep text off the ribbon border art
            _banner = UIKit.Group(_group, "RoundBanner");
            UIKit.Place(_banner, new Vector2(0.5f, 1f), new Vector2(0f, 2f), new Vector2(560f, 78f));
            var cloth = UIKit.Panel(_banner, skin != null ? skin.BannerHanging : null,
                skin != null ? Color.white : new Color(0f, 0f, 0f, 0.55f));
            UIKit.Stretch((RectTransform)cloth.transform);
            _bannerText = UIKit.Label(_banner, "", 21, UIKit.Ink, TextAnchor.MiddleCenter, true);
            var btr = (RectTransform)_bannerText.transform;
            UIKit.Stretch(btr);
            btr.offsetMin = new Vector2(70f, 20f); // off the tails
            btr.offsetMax = new Vector2(-70f, -16f);
        }

        /// Radial blood-edge texture: clear center, red creeping in from the
        /// borders. Alpha animates with health; the shape is baked once.
        static Texture2D VignetteTex()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - size / 2f) / (size / 2f);
                    float dy = (y - size / 2f) / (size / 2f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);       // 0 center  ~1.4 corner
                    float a = Mathf.SmoothStep(0f, 1f, (d - 0.55f) / 0.75f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);   // tinted by Image.color
                }
            tex.SetPixels(px);
            tex.Apply(false);
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        void Update()
        {
            if (_group == null) return;

            bool menuScene = ActiveScene.Name == "Menu"; // cached — no per-frame string alloc
            if (_group.gameObject.activeSelf == menuScene)
                _group.gameObject.SetActive(!menuScene); // the menu owns its screen
            if (menuScene) return;

            var player = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;

            // hurt = red edges creeping in as health drops
            if (player != null && _vignette != null)
            {
                float f = Mathf.Clamp01(player.Health / Sides.MaxHealthFor(Grimoire.LocalPlayerId));
                float a = (1f - f) * (1f - f) * 0.85f;
                // panic pulse only under 20%
                if (f < 0.2f && !player.IsDowned)
                    a += (Mathf.Sin(Time.time * 6f) * 0.5f + 0.5f) * 0.12f;
                _vignette.color = new Color(0.55f, 0f, 0f, Mathf.Clamp01(a));
            }

            // round banner; suppressed in the lobby
            string status = ActiveScene.Name == "Lobby" ? "" : RoundDirector.HudStatus();
            if (_banner.gameObject.activeSelf != !string.IsNullOrEmpty(status))
                _banner.gameObject.SetActive(!string.IsNullOrEmpty(status));
            _bannerText.text = status;
        }
    }
}
