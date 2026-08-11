using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// THE HAT COLOR PILLAR (Marko Aug 11: "anyone will be able to go to a
    /// color picker pillar to open up a menu to change their hat color to
    /// whatever they want while in lobby"). HE places the pillar object and
    /// adds this component; walking up offers E, E opens three sliders —
    /// hue, saturation, brightness — that repaint the hat LIVE on the wizard
    /// standing there. Direct manipulation, nothing to read (the hat itself
    /// is the preview). The pick saves per user and dresses every spawn.
    public class HatPillar : MonoBehaviour
    {
        [Tooltip("How close the player must stand for the offer, meters.")]
        public float Range = 2.8f;

        public static bool PanelOpen { get; private set; }

        RectTransform _panel;
        Image _swatch;
        float _h = 0.02f, _s = 0.85f, _v = 0.9f;

        void Start()
        {
            var c = HatColor.Saved();
            if (c != null) Color.RGBToHSV(c.Value, out _h, out _s, out _v);
        }

        void Update()
        {
            var p = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (p == null) return;
            bool near = (p.transform.position - transform.position).sqrMagnitude <= Range * Range;

            if (!near)
            {
                if (PanelOpen) Close();
                return;
            }

            var kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame && !GameMenu.IsOpen)
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
            else
                UIPrompt.Show("E", Loc.T("hat.pillar"));
        }

        void Open()
        {
            PanelOpen = true;
            _panel = UIKit.Group(UIKit.Root, "HatPanel");
            UIKit.Place(_panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360f, 250f));
            var back = UIKit.Panel(_panel, UISkin.I != null ? UISkin.I.PanelBrownDark : null,
                UISkin.I != null ? Color.white : new Color(0.1f, 0.08f, 0.06f, 0.92f));
            UIKit.Stretch((RectTransform)back.transform);

            // the wizard IS the preview; the swatch shows the exact pick
            var swGo = new GameObject("Swatch", typeof(RectTransform), typeof(Image));
            swGo.transform.SetParent(_panel, false);
            _swatch = swGo.GetComponent<Image>();
            _swatch.raycastTarget = false;
            UIKit.Place((RectTransform)swGo.transform, new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(90f, 56f));

            void Add(float y, float val, System.Action<float> set)
            {
                var s = UIKit.Slider(_panel, 0f, 1f, val, v => { set(v); Repaint(); });
                UIKit.Place((RectTransform)s.transform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(280f, 26f));
            }
            Add(-96f, _h, v => _h = v);
            Add(-134f, _s, v => _s = v);
            Add(-172f, _v, v => _v = v); // 0 = a black hat, allowed

            var done = UIKit.Button(_panel, Loc.T("hat.done"), Close, null, 18);
            UIKit.Place((RectTransform)done.transform, new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(120f, 40f));

            Repaint();
        }

        void Repaint()
        {
            Color c = Color.HSVToRGB(_h, _s, _v);
            if (_swatch != null) _swatch.color = c;
            HatColor.Set(c); // saves + dresses the wizard live
        }

        void Close()
        {
            PanelOpen = false;
            UIKit.Retire(_panel);
            _panel = null;
        }

        void OnDisable() { if (PanelOpen) Close(); }
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
        /// (his authoring convention, same family as PageAnchor/Tip — warned
        /// loudly when absent, never guessed further). Property block only:
        /// his material asset is never touched.
        /// ⛔ MP PARITY GAP (flagged): remote wizards' hat colors need a slot
        /// in the player-state sync — it rides with the known sides-sync gap.
        // found once per pilot — the full-rig name walk allocated every sweep
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
                _found[p] = hat; // null remembered too — never rescan this rig
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
