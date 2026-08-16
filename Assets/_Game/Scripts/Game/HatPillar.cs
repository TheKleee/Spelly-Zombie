using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// THE HAT COLOR PILLAR ("anyone will be able to go to a
    /// color picker pillar to open up a menu to change their hat color to
    /// whatever they want while in lobby"). design the pillar object and
    /// adds this component; walking up offers E, E opens three sliders -
    /// hue, saturation, brightness - that repaint the hat LIVE on the wizard
    /// standing there. Direct manipulation, nothing to read (the hat itself
    /// is the preview). The pick saves per user and dresses every spawn.
    public class HatPillar : MonoBehaviour
    {
        [Tooltip("How close the player must stand for the offer, meters.")]
        public float Range = 2.8f;

        public static bool PanelOpen { get; private set; }

        RectTransform _panel;
        RectTransform _dot;
        Renderer _beam, _glow;
        float _h = 0.02f, _s = 0.85f, _v = 0.9f;

        void Start()
        {
            var c = HatColor.Saved();
            if (c != null) Color.RGBToHSV(c.Value, out _h, out _s, out _v);
            // the pillar's body: a shaft of light in the picked color (the
            // own art on this object suppresses it automatically)
            _beam = PillarBeam.Build(transform, out _glow);
            // a dedicated aim body: trigger only, so bodies pass through and
            // the pen ignores it (pen raycasts skip triggers). Tall as the beam.
            if (transform.Find("AimTarget") == null)
            {
                var aim = new GameObject("AimTarget");
                aim.transform.SetParent(transform, false);
                var col = aim.AddComponent<CapsuleCollider>();
                col.isTrigger = true;
                col.center = Vector3.up * 1.6f;
                col.height = 3.6f;
                col.radius = 0.5f;
            }
        }

        void Update()
        {
            // THE PILLAR IS THE HAT ("pillar should be the same color
            // as the hat as well not to confuse people... Pillar is the
            // selected color") — the EXACT pick, no visibility floors: a
            // white hat means a white beam
            Color worn = Color.HSVToRGB(_h, _s, _v);
            PillarBeam.Tint(_beam, worn);
            PillarBeam.Tint(_glow, worn);

            var p = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (p == null) return;
            bool near = (p.transform.position - transform.position).sqrMagnitude <= Range * Range;

            if (!near)
            {
                if (PanelOpen) Close();
                return;
            }

            // aimed, never area: opening takes the crosshair ON the pillar;
            // walking away still closes an open panel
            var kb = Keyboard.current;
            if (ReferenceEquals(AimBadge.Aimed, this) && !UIKit.Typing
                && kb != null && kb.eKey.wasPressedThisFrame && !GameMenu.IsOpen)
            {
                if (PanelOpen) Close();
                else Open();
            }

            if (PanelOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
            }
        }

        void Open()
        {
            PanelOpen = true;
            // NO BACKING PANEL ("The brown outline is not necessary
            // it looks totally out of place") — the wheel, slider, presets
            // and done all float free over the world
            _panel = UIKit.Group(UIKit.Root, "HatPanel");
            UIKit.Place(_panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(380f, 420f));

            // THE COLOR WHEEL (Meccha-style): hue runs around the wheel,
            // saturation runs outward; click or drag anywhere on it
            var wheelGo = new GameObject("Wheel", typeof(RectTransform), typeof(Image));
            wheelGo.transform.SetParent(_panel, false);
            var wImg = wheelGo.GetComponent<Image>();
            wImg.sprite = WheelSprite();
            wImg.preserveAspect = true;
            UIKit.Place((RectTransform)wheelGo.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(250f, 250f));
            var drag = wheelGo.AddComponent<HatWheelDrag>();
            // the wheel is WYSIWYG: it shows full brightness, so it picks at
            // full brightness (the slider is gone per the sketch; darker
            // shades live in the presets)
            drag.Pick = (h, s) => { _h = h; _s = s; _v = 1f; Repaint(); };

            // the marker riding the wheel: the drag surface moves it DIRECTLY
            // under the pointer ("the indicator is not following the
            // mouse"), Repaint only places it for preset clicks and reopen
            var dotGo = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            dotGo.transform.SetParent(wheelGo.transform, false);
            _dot = (RectTransform)dotGo.transform;
            var dImg = dotGo.GetComponent<Image>();
            dImg.raycastTarget = false;
            dImg.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            _dot.sizeDelta = new Vector2(12f, 12f);
            drag.Dot = _dot;

            // presets wear the same button chrome as [done], the
            // row centered mid-screen; no tray, no preview box - THE PILLAR
            // is the selected color
            Color[] presets =
            {
                new Color(0.86f, 0.22f, 0.18f), new Color(1f, 0.62f, 0.12f),
                new Color(0.98f, 0.88f, 0.25f), new Color(0.32f, 0.78f, 0.35f),
                new Color(0.28f, 0.45f, 0.95f), new Color(0.68f, 0.4f, 0.9f),
            };
            for (int i = 0; i < presets.Length; i++)
            {
                Color pc = presets[i];
                var frame = UIKit.Button(_panel, "", () =>
                { Color.RGBToHSV(pc, out _h, out _s, out _v); Repaint(); }, null, 12);
                UIKit.Place((RectTransform)frame.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(-95f + i * 38f, -95f), new Vector2(36f, 36f));
                var inner = new GameObject("Color", typeof(RectTransform), typeof(Image));
                inner.transform.SetParent(frame.transform, false);
                var img = inner.GetComponent<Image>();
                img.color = pc;
                img.raycastTarget = false;
                var irt = (RectTransform)inner.transform;
                irt.anchorMin = Vector2.zero;
                irt.anchorMax = Vector2.one;
                irt.offsetMin = new Vector2(6f, 6f);
                irt.offsetMax = new Vector2(-6f, -6f);
            }

            var done = UIKit.Button(_panel, Loc.T("hat.done"), Close, null, 18);
            UIKit.Place((RectTransform)done.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -155f), new Vector2(120f, 40f));

            Repaint();
        }

        void Repaint()
        {
            Color c = Color.HSVToRGB(_h, _s, _v);
            if (_dot != null)
            {
                float ang = _h * Mathf.PI * 2f;
                _dot.anchoredPosition = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (_s * 125f);
            }
            HatColor.Set(c); // saves + dresses the wizard live
        }

        // the HSV disc, generated once and shared (hue by angle, saturation
        // by radius, full brightness - the slider below owns brightness)
        static Sprite _wheelSprite;
        static Sprite WheelSprite()
        {
            if (_wheelSprite != null) return _wheelSprite;
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f;
                    float dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r > 1f) { px[y * N + x] = new Color32(0, 0, 0, 0); continue; }
                    float h = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f);
                    if (h < 0f) h += 1f;
                    Color c = Color.HSVToRGB(h, Mathf.Clamp01(r), 1f);
                    c.a = Mathf.Clamp01((1f - r) * N * 0.5f); // soft rim
                    px[y * N + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply();
            _wheelSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
            return _wheelSprite;
        }

        void Close()
        {
            PanelOpen = false;
            UIKit.Retire(_panel);
            _panel = null;
        }

        void OnDisable() { if (PanelOpen) Close(); }
    }

    /// The wheel's pointer surface - converts a click or drag on the disc
    /// into hue (angle) and saturation (radius). Added at runtime only,
    /// never scene-serialized.
    class HatWheelDrag : MonoBehaviour,
        UnityEngine.EventSystems.IPointerDownHandler,
        UnityEngine.EventSystems.IDragHandler
    {
        public System.Action<float, float> Pick;
        public RectTransform Dot; // moved DIRECTLY under the pointer, never derived

        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) => Read(e);
        public void OnDrag(UnityEngine.EventSystems.PointerEventData e) => Read(e);

        void Read(UnityEngine.EventSystems.PointerEventData e)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rt, e.position, e.pressEventCamera, out var lp)) return;
            // measure from the RECT'S CENTER, not the pivot - UIKit.Place
            // pivots this rect at its top, which put "wheel center" a full
            // radius below the cursor (the bug: dot at the bottom while the
            // mouse sat in the middle)
            Vector2 p = lp - rt.rect.center;
            float half = rt.rect.width * 0.5f;
            float dx = p.x / half;
            float dy = p.y / half;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            if (r > 1.15f) return;            // clicked well past the rim
            if (r > 1f) { dx /= r; dy /= r; r = 1f; } // dragging outside rides the rim
            if (Dot != null) Dot.anchoredPosition = new Vector2(dx, dy) * half;
            float h = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f);
            if (h < 0f) h += 1f;
            Pick?.Invoke(h, Mathf.Clamp01(r));
        }
    }

    /// The saved hat color, applied to the LOCAL wizard wherever they spawn
    /// (SideBootstrap's sweep re-dresses each scene).
    public static class HatColor
    {
        const string Key = "sz_hatcolor";
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static MaterialPropertyBlock _blk;
        static bool _warnedNoHat;

        public static Color? Saved()
        {
            var s = PlayerPrefs.GetString(Key, "");
            return ColorUtility.TryParseHtmlString(s, out var c) ? c : (Color?)null;
        }

        public static void Set(Color c)
        {
            PlayerPrefs.SetString(Key, "#" + ColorUtility.ToHtmlStringRGB(c));
            var p = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (p != null) Dress(p);
        }

        /// Tints every renderer under the child whose name contains "Hat"
        /// (the authoring convention, same family as PageAnchor/Tip - warned
        /// loudly when absent, never guessed further). Property block only:
        /// the material asset is never touched.
        /// MP PARITY GAP (flagged): remote wizards' hat colors need a slot
        /// in the player-state sync - it rides with the known sides-sync gap.
        // found once per pilot - the full-rig name walk allocated every sweep
        static readonly System.Collections.Generic.Dictionary<SimpleFPSController, Transform> _found =
            new System.Collections.Generic.Dictionary<SimpleFPSController, Transform>();

        public static void Dress(SimpleFPSController p)
        {
            var saved = Saved();
            if (p == null || saved == null) return;
            if (!_found.TryGetValue(p, out var hat))
            {
                foreach (var t in p.GetComponentsInChildren<Transform>(true))
                    if (t.name.IndexOf("hat", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    { hat = t; break; }
                _found[p] = hat; // null remembered too - never rescan this rig
            }
            if (hat == null)
            {
                if (!_warnedNoHat)
                {
                    _warnedNoHat = true;
                    Debug.LogWarning("[SpellyZombie] A hat color is saved but no child containing " +
                        "'Hat' exists under the player rig — name the hat object (e.g. \"Hat\") so " +
                        "the pillar has something to paint.");
                }
                return;
            }
            if (_blk == null) _blk = new MaterialPropertyBlock();
            foreach (var r in hat.GetComponentsInChildren<Renderer>(true))
            {
                r.GetPropertyBlock(_blk);
                _blk.SetColor(BaseColorId, saved.Value);
                _blk.SetColor(ColorId, saved.Value);
                r.SetPropertyBlock(_blk);
            }
        }
    }
}
