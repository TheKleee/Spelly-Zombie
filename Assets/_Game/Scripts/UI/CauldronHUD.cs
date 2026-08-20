using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// Cauldron status bar at top: colour = who holds the pot (black wizards,
    /// green acolytes), length = ink left; the trophy hovers over whoever wins
    /// if the clock hit zero now. Match maps only; art assigned in Inspector.
    public class CauldronHUD : MonoBehaviour
    {
        [Header("the art — dragged in, never generated")]
        [Tooltip("The cauldron while the WIZARDS hold it (black ink). Square 128x128 transparent PNG, imported as Sprite (2D and UI).")]
        public Sprite CauldronWizard;
        [Tooltip("The cauldron while the ACOLYTES hold it (green ink). Swaps in automatically when the pot turns.")]
        public Sprite CauldronAcolyte;
        [Tooltip("Acolyte face for the bar's LEFT end. Square 128x128 transparent PNG.")]
        public Sprite AcolyteFace;
        [Tooltip("Wizard face for the bar's RIGHT end. Square 128x128 transparent PNG.")]
        public Sprite WizardFace;
        [Tooltip("The trophy that hovers over whoever wins if the timer hit 0 right now.")]
        public Sprite Trophy;

        [Header("Layout knobs")]
        [Tooltip("Bar length in HUD pixels.")]
        public float BarWidth = 380f;
        [Tooltip("Bar THICKNESS in HUD pixels. Thin reads better — the faces have their own size below.")]
        public float BarHeight = 12f;
        [Tooltip("Face and cauldron icon size in HUD pixels.")]
        public float IconSize = 44f;

        /// The pot's live truth - the cauldron economy writes these.
        public static float Fill = 1f;   // 0..1 ink remaining
        public static bool Corrupt;      // true = the acolytes hold it
        /// Seconds shown centred above the bar; negative hides it.
        public static float TimerSeconds = -1f;

        RectTransform _ui;
        Image _fill, _icon, _trophy;
        Text _timer;
        float _shownFill = -1f;
        bool _shownCorrupt;
        int _shownTimer = int.MinValue;
        float _trophyX;

        void Start() => Build();

        void OnDestroy() => UIKit.Retire(_ui);

        // container-local X of each face's centre - the trophy hops between these
        float FaceX => BarWidth * 0.5f + IconSize * 0.62f;

        void Build()
        {
            UIKit.Retire(_ui);
            _ui = UIKit.Group(UIKit.Root, "CauldronHUD");
            float w = Mathf.Max(120f, BarWidth);
            float h = Mathf.Max(6f, BarHeight);
            float icon = Mathf.Max(16f, IconSize);

            float totalW = w + icon * 2.6f;
            float totalH = icon * 2.15f; // top half = trophy/timer row, bottom half = the bar row
            _ui.anchorMin = _ui.anchorMax = new Vector2(0.5f, 1f);
            _ui.pivot = new Vector2(0.5f, 1f);
            _ui.anchoredPosition = new Vector2(0f, -6f);
            _ui.sizeDelta = new Vector2(totalW, totalH);

            float barY = -totalH + icon * 0.55f; // the bar row, from the container's TOP

            // the track: parchment-pale so both ink colours read against it
            var track = Img(_ui, "Track");
            track.color = new Color(0.93f, 0.89f, 0.79f, 0.6f);
            var trackRt = (RectTransform)track.transform;
            trackRt.anchorMin = trackRt.anchorMax = new Vector2(0.5f, 1f);
            trackRt.pivot = new Vector2(0.5f, 0.5f);
            trackRt.anchoredPosition = new Vector2(0f, barY);
            trackRt.sizeDelta = new Vector2(w, h);

            // fill is a child of the track, anchored to its left edge
            _fill = Img(trackRt, "Fill");
            var fillRt = (RectTransform)_fill.transform;
            fillRt.anchorMin = new Vector2(0f, 0.5f);
            fillRt.anchorMax = new Vector2(0f, 0.5f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = new Vector2(1f, 0f);
            fillRt.sizeDelta = new Vector2(w - 2f, h - 2f);

            // the faces cap the ends
            PlaceIcon(Img(_ui, "FaceAcolyte"), AcolyteFace, -FaceX, barY, icon);
            PlaceIcon(Img(_ui, "FaceWizard"), WizardFace, FaceX, barY, icon);

            // cauldron icon centred on the bar's right end
            _icon = Img(_ui, "Cauldron");
            PlaceIcon(_icon, CauldronWizard, w * 0.5f, barY, icon);

            // the trophy row lives INSIDE the container, above the faces
            _trophy = Img(_ui, "Trophy");
            PlaceIcon(_trophy, Trophy, FaceX, barY + icon * 0.92f, icon * 0.8f);
            _trophyX = FaceX; // starts over the wizard - the pot opens black

            var timerGo = new GameObject("Timer", typeof(RectTransform), typeof(Text));
            timerGo.transform.SetParent(_ui, false);
            _timer = timerGo.GetComponent<Text>();
            _timer.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _timer.fontSize = 20;
            _timer.fontStyle = FontStyle.Bold;
            _timer.alignment = TextAnchor.MiddleCenter;
            _timer.color = new Color(0.15f, 0.1f, 0.2f);
            _timer.raycastTarget = false;
            var timerRt = (RectTransform)timerGo.transform;
            timerRt.anchorMin = timerRt.anchorMax = new Vector2(0.5f, 1f);
            timerRt.pivot = new Vector2(0.5f, 0.5f);
            timerRt.anchoredPosition = new Vector2(0f, barY + icon * 0.95f);
            timerRt.sizeDelta = new Vector2(140f, 26f);

            if (CauldronWizard == null || CauldronAcolyte == null || AcolyteFace == null
                || WizardFace == null || Trophy == null)
                Debug.LogWarning("[SpellyZombie] CauldronHUD: an art slot is EMPTY. All five sprites " +
                    "are yours to drag in — nothing is generated for you.", this);

            _shownFill = -1f;
            _shownTimer = int.MinValue;
        }

        static Image Img(RectTransform parent, string imgName)
        {
            var go = new GameObject(imgName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false; // a readout, never a click surface
            return img;
        }

        void PlaceIcon(Image img, Sprite sprite, float x, float y, float size)
        {
            img.sprite = sprite;
            img.preserveAspect = true;
            img.enabled = sprite != null;
            var rt = (RectTransform)img.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(size, size);
        }

        void LateUpdate()
        {
            if (_fill == null) return;

            // match maps only; the pot object itself still works everywhere
            bool hidden = ActiveScene.Name == "Lobby" || ActiveScene.Name == "Menu";
            if (_ui != null && _ui.gameObject.activeSelf == hidden) _ui.gameObject.SetActive(!hidden);
            if (hidden) return;

            int t = TimerSeconds >= 0f ? Mathf.CeilToInt(TimerSeconds) : -1;
            if (_timer != null && t != _shownTimer)
            {
                _shownTimer = t;
                _timer.text = t >= 0 ? t.ToString() : "";
            }

            // trophy: corrupt or empty pot = acolyte side, clean = wizard side
            if (_trophy != null && _trophy.enabled)
            {
                bool acolyteWinning = Corrupt || Fill <= 0.001f;
                float targetX = acolyteWinning ? -FaceX : FaceX;
                _trophyX = Mathf.MoveTowards(_trophyX, targetX, FaceX * 6f * Time.deltaTime);
                var rt = (RectTransform)_trophy.transform;
                rt.anchoredPosition = new Vector2(_trophyX, rt.anchoredPosition.y);
            }

            float f = Mathf.Clamp01(Fill);
            if (Mathf.Abs(f - _shownFill) < 0.002f && Corrupt == _shownCorrupt) return;
            _shownFill = f;
            _shownCorrupt = Corrupt;

            var frt = (RectTransform)_fill.transform;
            frt.sizeDelta = new Vector2((Mathf.Max(120f, BarWidth) - 2f) * f, frt.sizeDelta.y);
            // same two ink colours as the wands
            _fill.color = Corrupt ? DrawingConfig.CorruptInkColor : DrawingConfig.InkColor;
            if (_icon != null)
            {
                var want = Corrupt ? CauldronAcolyte : CauldronWizard;
                if (want != null && _icon.sprite != want) _icon.sprite = want;
            }
        }
    }
}
