using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// The survival HUD: NO health/ink bars (Marko's CoD-Zombies call) — red screen edges + the wand's reservoir; perk badges, round banner, downed overlay.
    public class HUD : MonoBehaviour
    {
        static HUD _i;

        RectTransform _group;
        UIKit.UIBar _bleed;
        Image _vignette;
        Text _bannerText, _downText;
        RectTransform _banner, _downGroup, _badgeRow;
        string _badgesShown = "";

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

            // hurt vignette (no HP bar); stale baked copies die first — the runtime sprite can't serialize into the prefab
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

            // bottom-left: perk badges only — no riches, no XP line (Marko: "no UI indicators of any bar on the screen")
            var corner = UIKit.Group(_group, "Vitals");
            UIKit.Place(corner, new Vector2(0f, 0f), new Vector2(18f, 18f), new Vector2(320f, 96f));

            _badgeRow = UIKit.Group(corner, "Perks");
            UIKit.Place(_badgeRow, new Vector2(0f, 1f), new Vector2(0f, -58f), new Vector2(260f, 30f));

            // ---- top-center: the round banner (near native ribbon proportions
            // — one line, generous side insets so text stays off the border art)
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

            // ---- center: downed overlay (above the gallery's parade line) ----
            _downGroup = UIKit.Group(_group, "Downed");
            UIKit.Place(_downGroup, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(660f, 84f));
            var downBack = UIKit.Panel(_downGroup, skin != null ? skin.PanelBrownDark : null,
                skin != null ? new Color(1f, 1f, 1f, 0.96f) : new Color(0f, 0f, 0f, 0.7f));
            UIKit.Stretch((RectTransform)downBack.transform);
            _downText = UIKit.Label(_downGroup, "", 24, new Color(1f, 0.5f, 0.4f), TextAnchor.MiddleCenter, true);
            var dtr = (RectTransform)_downText.transform;
            UIKit.Stretch(dtr);
            dtr.offsetMax = new Vector2(0f, -30f);
            _bleed = UIKit.Bar(_downGroup, skin != null ? skin.ProgressRed : null, new Vector2(400f, 16f));
            UIKit.Place(_bleed.Rt, new Vector2(0.5f, 0f), new Vector2(0f, 10f), _bleed.Rt.sizeDelta);
            _downGroup.gameObject.SetActive(false);

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
                    float d = Mathf.Sqrt(dx * dx + dy * dy);       // 0 center → ~1.4 corner
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
                float f = Mathf.Clamp01(player.Health / Perks.MaxHealth);
                float a = (1f - f) * (1f - f) * 0.85f;
                // panic pulse only under 20% (Marko: "we shouldn't always be in panic mode just because we're damaged")
                if (f < 0.2f && !player.IsDowned)
                    a += (Mathf.Sin(Time.time * 6f) * 0.5f + 0.5f) * 0.12f;
                _vignette.color = new Color(0.55f, 0f, 0f, Mathf.Clamp01(a));
            }

            // perk badges (rebuilt only when they change)
            string tag = Perks.HudTag();
            if (tag != _badgesShown)
            {
                _badgesShown = tag;
                // immediate: dying children must not linger where the prefab
                // adoption pass could claim them this same frame
                for (int i = _badgeRow.childCount - 1; i >= 0; i--)
                    DestroyImmediate(_badgeRow.GetChild(i).gameObject);
                float x = 0f;
                foreach (char letter in tag)
                {
                    var badge = UIKit.Group(_badgeRow, "Badge_" + letter);
                    UIKit.Place(badge, new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(26f, 32f));
                    var hex = UIKit.Panel(badge, UISkin.I != null ? UISkin.I.HexBrown : null,
                        UISkin.I != null ? Color.white : new Color(0.5f, 0.35f, 0.2f));
                    UIKit.Stretch((RectTransform)hex.transform);
                    var l = UIKit.Label(badge, letter.ToString(), 15, UIKit.Parchment, TextAnchor.MiddleCenter, true);
                    UIKit.Stretch((RectTransform)l.transform);
                    x += 30f;
                }
            }

            // round banner — the GAME's voice only; stale wipe/round text in the lobby just confuses (Marko)
            string status = ActiveScene.Name == "Lobby" ? "" : RoundDirector.HudStatus();
            if (_banner.gameObject.activeSelf != !string.IsNullOrEmpty(status))
                _banner.gameObject.SetActive(!string.IsNullOrEmpty(status));
            _bannerText.text = status;

            // downed overlay
            bool downed = player != null && player.IsDowned;
            if (_downGroup.gameObject.activeSelf != downed) _downGroup.gameObject.SetActive(downed);
            if (downed)
            {
                _downText.text = player.IsDead ? "DEAD"
                    : player.ReviveProgress > 0f ? $"REVIVING… {player.ReviveProgress * 100f:0}%"
                    : $"DOWNED — bleeding out ({player.BleedOut:0}s)   teammate: hold E";
                _downText.color = player.IsDead ? Color.red : new Color(1f, 0.5f, 0.4f);
                _bleed.Set(player.ReviveProgress > 0f
                    ? player.ReviveProgress
                    : player.BleedOut / DrawingConfig.BleedOutSeconds);
            }

            // between-rounds seal gallery CUT (Marko's call: never asked for, cluttered the screen)
        }
    }
}
