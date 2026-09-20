using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// Lobby pillar: aim + E opens a color wheel that repaints the hat live.
    /// The pick saves per player and dresses every spawn.
    public class HatPillar : MonoBehaviour
    {
        [Tooltip("How close the player must stand for the offer, meters.")]
        public float Range = 2.8f;

        public static bool PanelOpen { get; private set; }

        RectTransform _panel;
        RectTransform _dot;
        Renderer _beam, _glow;
        float _h = 0.02f, _s = 0.85f, _v = 0.9f;
        SimpleFPSController _viewer;   // whose view the wheel moved
        bool _enteredThird;            // the wheel put the view in third person; closing takes it back

        void Start()
        {
            var c = HatColor.Saved();
            if (c != null) Color.RGBToHSV(c.Value, out _h, out _s, out _v);
            // beam in the picked color; authored art on this object suppresses it
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
            // the beam shows the colour the hat wears (a taken pick turns), no visibility floor
            Color worn = HatColor.Worn() ?? Color.HSVToRGB(_h, _s, _v);
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
            // the hat is on your head: you watch it from behind while you paint it
            _viewer = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            _enteredThird = _viewer != null && !SimpleFPSController.ThirdPersonActive;
            if (_enteredThird) _viewer.EnterThirdPerson();
            // no backing panel; the controls float free, off to the side so the hat stays in view
            _panel = UIKit.Group(UIKit.Root, "HatPanel");
            UIKit.Place(_panel, new Vector2(0.78f, 0.5f), Vector2.zero, new Vector2(380f, 420f));

            // hue runs around the wheel, saturation runs outward
            var wheelGo = new GameObject("Wheel", typeof(RectTransform), typeof(Image));
            wheelGo.transform.SetParent(_panel, false);
            var wImg = wheelGo.GetComponent<Image>();
            wImg.sprite = WheelSprite();
            wImg.preserveAspect = true;
            UIKit.Place((RectTransform)wheelGo.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(250f, 250f));
            var drag = wheelGo.AddComponent<HatWheelDrag>();
            // the wheel shows full brightness, so it picks at full brightness
            drag.Pick = (h, s) => { _h = h; _s = s; _v = 1f; Repaint(); };

            // the drag surface moves the dot; Repaint places it only for preset clicks and reopen
            var dotGo = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            dotGo.transform.SetParent(wheelGo.transform, false);
            _dot = (RectTransform)dotGo.transform;
            var dImg = dotGo.GetComponent<Image>();
            dImg.raycastTarget = false;
            dImg.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            _dot.sizeDelta = new Vector2(12f, 12f);
            drag.Dot = _dot;

            // preset row; the pillar itself is the preview
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

        // the HSV disc, generated once and shared (hue by angle, saturation by radius)
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
            if (_enteredThird && _viewer != null && SimpleFPSController.ThirdPersonActive) _viewer.EnterFirstPerson();
            _enteredThird = false;
            _viewer = null;
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
        public RectTransform Dot; // moved directly under the pointer

        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) => Read(e);
        public void OnDrag(UnityEngine.EventSystems.PointerEventData e) => Read(e);

        void Read(UnityEngine.EventSystems.PointerEventData e)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rt, e.position, e.pressEventCamera, out var lp)) return;
            // measure from the rect's center, not the pivot (UIKit.Place pivots this rect at its top)
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
            // the host rules on a taken colour; asked here first, so the hat never shows a colour it will lose
            Color? free = NetSync.FreeHat(c);
            _given = free != null && !Same(free.Value, c) ? free : null;
            DressLocal();
            NetSync.PushLocalOutfit(); // the pick re-announces: friends' copies wear it too
        }

        // ---- no two hats alike: a colour somebody in the lobby wears is taken ----
        /// Closer than this in RGB, two hats read as one colour. The wheel's rim holds 24 that are not.
        const float SameHat = 0.25f;

        static Color? _given; // worn this session in place of a taken pick; the saved pick is kept

        /// What the local hat wears: the pick, or what the host turned it to.
        public static Color? Worn() => _given ?? Saved();

        /// The host's answer to the local hat.
        public static void Given(Color? verdict)
        {
            var saved = Saved();
            Color? given = verdict != null && (saved == null || !Same(verdict.Value, saved.Value)) ? verdict : null;
            if (given == null ? _given == null : _given != null && Same(given.Value, _given.Value)) return;
            _given = given;
            DressLocal();
        }

        /// Out of the session: the next lobby starts from the saved pick again.
        public static void Forget() => _given = null;

        static void DressLocal()
        {
            var p = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (p != null) Dress(p);
        }

        static bool Same(Color a, Color b)
        {
            Color32 x = a, y = b;
            return x.r == y.r && x.g == y.g && x.b == y.b;
        }

        /// An unpainted hat shows its material's own colour.
        static Color Bare()
        {
            var p = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            var hat = p != null ? HatOf(p) : null;
            if (hat != null)
                foreach (var r in hat.GetComponentsInChildren<Renderer>(true))
                    if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(BaseColorId))
                        return r.sharedMaterial.GetColor(BaseColorId);
            return Color.white;
        }

        static float Gap(Color c, bool bare, System.Collections.Generic.List<Color?> worn, Color bareColor)
        {
            float gap = float.MaxValue;
            foreach (var w in worn)
            {
                if (w == null && bare) return 0f; // two unpainted hats are the same hat
                Color o = w ?? bareColor;
                gap = Mathf.Min(gap, new Vector3(c.r - o.r, c.g - o.g, c.b - o.b).magnitude);
            }
            return gap;
        }

        /// The wish itself when nobody in `worn` wears it (null = unpainted), else the nearest
        /// colour nobody wears: around the wheel from the wish, then bolder, paler, darker.
        /// An unpainted hat that must change starts from a hue of its own (`seed`).
        public static Color? Free(Color? wish, System.Collections.Generic.List<Color?> worn, int seed)
        {
            Color bareColor = Bare();
            if (Gap(wish ?? bareColor, wish == null, worn, bareColor) >= SameHat) return wish;

            float h, s, v;
            if (wish != null) Color.RGBToHSV(wish.Value, out h, out s, out v);
            else { h = Mathf.Repeat(seed * 0.618034f, 1f); s = 1f; v = 1f; }
            Color best = wish ?? Color.HSVToRGB(h, s, v);
            float bestGap = -1f;
            for (int ring = 0; ring < 5; ring++)
            {
                float rs = ring == 0 ? Mathf.Max(s, 0.35f) : ring == 1 || ring == 3 ? 1f : 0.5f;
                float rv = ring == 0 ? Mathf.Max(v, 0.35f) : ring < 3 ? 1f : 0.6f;
                for (int k = 0; k <= 50; k++)
                    for (int sign = 1; sign >= -1; sign -= 2)
                    {
                        if (k == 0 && sign < 0) continue;
                        Color c = Color.HSVToRGB(Mathf.Repeat(h + sign * k * 0.01f, 1f), rs, rv);
                        float gap = Gap(c, false, worn, bareColor);
                        if (gap >= SameHat) return c;
                        if (gap > bestGap) { bestGap = gap; best = c; }
                    }
            }
            return best; // a lobby too full for the wheel: the colour furthest from everyone's
        }

        /// The socket the wardrobe dresses; every renderer under it is the hat.
        const string SocketName = "Socket.Hat";

        /// Only a real hat is remembered: the body and its sockets build after
        /// spawn, so a miss is looked up again next time.
        static readonly System.Collections.Generic.Dictionary<SimpleFPSController, Transform> _found =
            new System.Collections.Generic.Dictionary<SimpleFPSController, Transform>();

        static Transform HatOf(SimpleFPSController p)
        {
            if (_found.TryGetValue(p, out var hat) && hat != null) return hat;
            var rig = p.GetComponentInChildren<CharacterRig>();
            hat = rig != null ? rig.HatSocket : null;
            if (hat == null)
                foreach (var t in p.GetComponentsInChildren<Transform>(true))
                    if (t.name == SocketName) { hat = t; break; }
            if (hat != null) _found[p] = hat;
            return hat;
        }

        /// Part of a worn hat?
        public static bool IsHat(Transform t)
        {
            for (var w = t; w != null; w = w.parent)
                if (w.name == SocketName) return true;
            return false;
        }

        /// Paints every renderer under the hat socket. Property block only: the
        /// material asset is never touched. The side hint comes from SideLook,
        /// which starts from this colour too.
        /// Friends' copies get the colour through OutfitMsg; NetAvatar paints it the same way.
        public static void Dress(SimpleFPSController p)
        {
            var saved = Worn();
            if (p == null || saved == null) return;
            var hat = HatOf(p);
            if (hat == null)
            {
                var rig = p.GetComponentInChildren<CharacterRig>();
                if (rig != null && rig.HasBody && !_warnedNoHat)
                {
                    _warnedNoHat = true;
                    Debug.LogWarning("[SpellyZombie] A hat color is saved but the player has no " +
                        SocketName + " to paint.");
                }
                return;
            }
            var look = p.GetComponent<SideLook>();
            Color c = look != null ? look.HatShade(saved.Value) : saved.Value;
            if (_blk == null) _blk = new MaterialPropertyBlock();
            foreach (var r in hat.GetComponentsInChildren<Renderer>(true))
            {
                r.GetPropertyBlock(_blk);
                _blk.SetColor(BaseColorId, c);
                _blk.SetColor(ColorId, c);
                r.SetPropertyBlock(_blk);
            }
        }
    }
}
