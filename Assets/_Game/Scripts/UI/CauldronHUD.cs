using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// Cauldron status bar at top: colour = who holds the pot (black wizards,
    /// green acolytes), length = ink left, and the cauldron icon rides the
    /// ink's edge - the pointer toward the side that is winning. The trophy
    /// hovers over whoever wins if the clock hit zero now. Match maps only;
    /// art assigned in Inspector.
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
        RectTransform _ui;
        Image _fill, _icon, _trophy;
        float _shownFill = -1f;
        bool _shownCorrupt;
        float _trophyX;

        /// The one bar on screen: it outlives every pot until the scene ends.
        static CauldronHUD _shown;

        void Start()
        {
            // on a pot, the art moves to a scene object of its own and the pot's copy stands down
            if (GetComponentInParent<CauldronEconomy>() != null)
            {
                if (_shown == null) _shown = Carry();
                enabled = false;
                return;
            }
            if (_shown != null && _shown != this) { enabled = false; return; }
            _shown = this;
            Build();
        }

        CauldronHUD Carry()
        {
            var keep = new GameObject("~CauldronHUD").AddComponent<CauldronHUD>();
            keep.CauldronWizard = CauldronWizard;
            keep.CauldronAcolyte = CauldronAcolyte;
            keep.AcolyteFace = AcolyteFace;
            keep.WizardFace = WizardFace;
            keep.Trophy = Trophy;
            keep.BarWidth = BarWidth;
            keep.BarHeight = BarHeight;
            keep.IconSize = IconSize;
            return keep;
        }

        void OnDestroy()
        {
            if (_shown == this) _shown = null;
            UIKit.Retire(_ui);
        }

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

            // cauldron icon rides the ink's edge; it starts at the full end
            _icon = Img(_ui, "Cauldron");
            PlaceIcon(_icon, CauldronWizard, w * 0.5f, barY, icon);

            // the trophy row lives INSIDE the container, above the faces
            _trophy = Img(_ui, "Trophy");
            PlaceIcon(_trophy, Trophy, FaceX, barY + icon * 0.92f, icon * 0.8f);
            _trophyX = FaceX; // starts over the wizard - the pot opens black

            if (CauldronWizard == null || CauldronAcolyte == null || AcolyteFace == null
                || WizardFace == null || Trophy == null)
                Debug.LogWarning("[SpellyZombie] CauldronHUD: an art slot is EMPTY. All five sprites " +
                    "are yours to drag in — nothing is generated for you.", this);

            _shownFill = -1f;
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
            bool hidden = ActiveScene.Name == "Lobby" || ActiveScene.Name == "Menu" || MapCreator.Active || PhotoBooth.Active;
            if (_ui != null && _ui.gameObject.activeSelf == hidden) _ui.gameObject.SetActive(!hidden);
            if (hidden) return;

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

            float barW = Mathf.Max(120f, BarWidth);
            var frt = (RectTransform)_fill.transform;
            frt.sizeDelta = new Vector2((barW - 2f) * f, frt.sizeDelta.y);
            // same two ink colours as the wands
            _fill.color = Corrupt ? DrawingConfig.CorruptInkColor : DrawingConfig.InkColor;
            if (_icon != null)
            {
                // full sits at the wizards' end, empty at the acolytes'
                var irt = (RectTransform)_icon.transform;
                irt.anchoredPosition = new Vector2(-barW * 0.5f + 1f + (barW - 2f) * f, irt.anchoredPosition.y);
                var want = Corrupt ? CauldronAcolyte : CauldronWizard;
                if (want != null && _icon.sprite != want) _icon.sprite = want;
            }
        }
    }
}
