using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ A CONTROLLER IN THE MENUS. A controller has no pointer: while one is in use and the
    /// pointer is free, one thing in the top panel is always picked. A gold frame shows which,
    /// the D-pad and the left stick move it, A presses it, left and right step a slider and the
    /// stick slides one as far as it is pushed. While a panel has the controller the stick is the
    /// panel's, not the body's; B hands it back to the body where there is one to walk (the book
    /// stand's panel has no other way out), and the D-pad or A takes it again. The pick never
    /// leaves the top panel, and text boxes stay the keyboard's. Unity's own navigation is off
    /// meanwhile, so nothing moves twice.
    public static class PadFocus
    {
        /// A panel has the controller: A, B, the D-pad and the left stick are the panel's (Keys
        /// keeps them from the game).
        public static bool Engaged { get; private set; }

        const float FirstRepeat = 0.4f, NextRepeat = 0.12f; // a held direction, seconds
        const float SliderStep = 0.05f;                      // of the slider's range, per D-pad move
        const float SliderPerSecond = 0.6f;                  // of the slider's range, at a full stick push
        const float StickPush = 0.55f;                       // the stick moves the pick past this
        const float StickSlide = 0.25f;                      // and slides a slider past this
        static bool _parked;                                 // B gave the controller back to the body
        static float _sliderAcc;                             // a whole-number slider: the stick's fractions, saved up

        static Selectable _pick;
        static string _panelName;
        static Vector2 _lastAt;   // the screen point of the pick: a rebuilt panel picks up from there
        static Vector2Int _dir;
        static float _repeatAt;
        static RectTransform _frame;
        static readonly Vector3[] _corners = new Vector3[4];
        static readonly List<Selectable> _found = new List<Selectable>();
        static Selectable[] _all = new Selectable[64];

        internal static void Tick()
        {
            var es = EventSystem.current;
            if (es != null && es.sendNavigationEvents == Keys.PadActive) es.sendNavigationEvents = !Keys.PadActive;

            var gp = Gamepad.current;
            bool could = Keys.PadActive && gp != null && Cursor.lockState != CursorLockMode.Locked
                && !MapCreator.Active && !PhotoBooth.Active; // the creators are the mouse's
            if (!could) { Drop(); return; }
            // a binding waiting for its button, a text box being typed in: hands off, the place is kept
            if (Keys.Swallowing || UIKit.Typing) { Engaged = Keys.Swallowing; ShowFrame(null); return; }

            var panel = TopPanel();
            if (panel == null) { Drop(); return; }
            if (panel.name != _panelName) _parked = false; // a new panel takes the controller
            if (_parked)
            {
                // the D-pad or A takes the panel back; that press is the panel's, not the body's
                if (DpadDown(gp) || gp.buttonSouth.wasPressedThisFrame) { _parked = false; Keys.Swallow(); }
                else { Engaged = false; _panelName = panel.name; ShowFrame(null); return; }
            }
            Engaged = true;

            // a pressed button goes dead for a beat (ButtonJuice): held, not lost
            if (_pick == null || !_pick.isActiveAndEnabled || !_pick.transform.IsChildOf(panel))
            {
                Canvas.ForceUpdateCanvases(); // a panel built this frame has no layout yet
                _pick = panel.name == _panelName ? Nearest(_lastAt) : First();
                if (_pick != null) Reveal((RectTransform)_pick.transform);
            }
            _panelName = panel.name;
            if (_pick == null) { ShowFrame(null); return; }

            // B where there is a body to walk: the controller goes back to it (a menu that closes
            // on B closes, and its panel is gone anyway)
            if (gp.buttonEast.wasPressedThisFrame && !GameMenu.IsOpen && !PoseStudio.IsOpen
                && ActiveScene.Name != "Menu")
            {
                _parked = true;
                Engaged = false;
                ShowFrame(null);
                return;
            }

            // a slider under the pick follows the stick's sideways push, as far as it is pushed
            Vector2 stick = gp.leftStick.ReadValue();
            var slider = _pick as Slider;
            bool across = slider != null && slider.IsInteractable()
                && (slider.direction == Slider.Direction.LeftToRight || slider.direction == Slider.Direction.RightToLeft);
            if (across && Mathf.Abs(stick.x) > StickSlide && Mathf.Abs(stick.x) >= Mathf.Abs(stick.y))
                Slide(slider, stick.x);
            else _sliderAcc = 0f;

            Vector2Int want = ReadDir(gp, stick, across);
            if (want != _dir)
            {
                _dir = want;
                _repeatAt = Time.unscaledTime + FirstRepeat;
                if (want != Vector2Int.zero) Step(want);
            }
            else if (want != Vector2Int.zero && Time.unscaledTime >= _repeatAt)
            {
                _repeatAt = Time.unscaledTime + NextRepeat;
                Step(want);
            }

            if (gp.buttonSouth.wasPressedThisFrame && _pick.IsInteractable() && es != null && !Keys.Swallowing)
                ExecuteEvents.Execute(_pick.gameObject, new BaseEventData(es), ExecuteEvents.submitHandler);

            _lastAt = Centre((RectTransform)_pick.transform);
            ShowFrame((RectTransform)_pick.transform);
        }

        static void Drop()
        {
            Engaged = false;
            _parked = false;
            _pick = null;
            _panelName = null;
            _dir = Vector2Int.zero;
            _sliderAcc = 0f;
            ShowFrame(null);
        }

        static bool DpadDown(Gamepad gp) =>
            gp.dpad.up.wasPressedThisFrame || gp.dpad.down.wasPressedThisFrame
            || gp.dpad.left.wasPressedThisFrame || gp.dpad.right.wasPressedThisFrame;

        /// The D-pad, else the stick pushed far enough. On a slider the stick's sideways push
        /// slides it (Slide), so only up and down move the pick on from there.
        static Vector2Int ReadDir(Gamepad gp, Vector2 stick, bool sliding)
        {
            Vector2 v = gp.dpad.ReadValue();
            if (v.sqrMagnitude < 0.25f)
            {
                v = stick.magnitude < StickPush ? Vector2.zero : stick;
                if (sliding && Mathf.Abs(v.x) >= Mathf.Abs(v.y)) v = Vector2.zero;
            }
            if (v.sqrMagnitude < 0.25f) return Vector2Int.zero;
            return Mathf.Abs(v.x) > Mathf.Abs(v.y) ? new Vector2Int(v.x > 0f ? 1 : -1, 0) : new Vector2Int(0, v.y > 0f ? 1 : -1);
        }

        /// The stick slides the slider: a full push crosses the whole range in under two seconds,
        /// a light push creeps. A whole-number slider saves the fractions up.
        static void Slide(Slider slider, float x)
        {
            float push = Mathf.Sign(x) * Mathf.Pow((Mathf.Abs(x) - StickSlide) / (1f - StickSlide), 2f);
            if (slider.direction == Slider.Direction.RightToLeft) push = -push;
            float delta = push * (slider.maxValue - slider.minValue) * SliderPerSecond * Time.unscaledDeltaTime;
            if (slider.wholeNumbers)
            {
                _sliderAcc += delta;
                float whole = Mathf.Sign(_sliderAcc) * Mathf.Floor(Mathf.Abs(_sliderAcc));
                if (whole == 0f) return;
                _sliderAcc -= whole;
                delta = whole;
            }
            slider.value += delta;
        }

        // ------------------------------------------------------------ the panel --
        static bool Usable(Selectable s) =>
            s != null && s.isActiveAndEnabled && !(s is Scrollbar) && !(s is InputField) && !(s is TMPro.TMP_InputField);

        /// The top panel that holds anything to press: the last such child of the canvas, which is
        /// the one drawn over the rest. _found is left holding what is in it.
        static RectTransform TopPanel()
        {
            _found.Clear();
            var root = UIKit.RootIfBuilt;
            if (root == null) return null;
            int n = Selectable.allSelectableCount;
            if (_all.Length < n) _all = new Selectable[Mathf.NextPowerOfTwo(n)];
            n = Selectable.AllSelectablesNoAlloc(_all);

            Transform top = null;
            for (int i = 0; i < n; i++)
            {
                if (!Usable(_all[i])) continue;
                var under = ChildOfRoot(_all[i].transform, root);
                if (under != null && (top == null || under.GetSiblingIndex() > top.GetSiblingIndex())) top = under;
            }
            if (top == null) return null;
            for (int i = 0; i < n; i++)
                if (Usable(_all[i]) && _all[i].transform.IsChildOf(top)) _found.Add(_all[i]);
            System.Array.Clear(_all, 0, n);
            return (RectTransform)top;
        }

        static Transform ChildOfRoot(Transform t, Transform root)
        {
            while (t != null && t.parent != root) t = t.parent;
            return t;
        }

        // ------------------------------------------------------------- the pick --
        static Rect ScreenRect(RectTransform rt)
        {
            rt.GetWorldCorners(_corners); // an overlay canvas: world is screen pixels
            Vector2 min = _corners[0], max = _corners[0];
            for (int i = 1; i < 4; i++) { min = Vector2.Min(min, _corners[i]); max = Vector2.Max(max, _corners[i]); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        static Vector2 Centre(RectTransform rt) => ScreenRect(rt).center;

        /// The highest thing in the panel, the leftmost of a row.
        static Selectable First()
        {
            Selectable best = null;
            float bestY = 0f, bestX = 0f;
            foreach (var s in _found)
            {
                if (!s.IsInteractable()) continue;
                var r = ScreenRect((RectTransform)s.transform);
                if (best == null || r.center.y > bestY + 4f || (r.center.y > bestY - 4f && r.center.x < bestX))
                { best = s; bestY = r.center.y; bestX = r.center.x; }
            }
            return best;
        }

        static Selectable Nearest(Vector2 to)
        {
            Selectable best = null;
            float bestGap = float.MaxValue;
            foreach (var s in _found)
            {
                if (!s.IsInteractable()) continue;
                var r = ScreenRect((RectTransform)s.transform);
                var inside = new Vector2(Mathf.Clamp(to.x, r.xMin, r.xMax), Mathf.Clamp(to.y, r.yMin, r.yMax));
                float gap = (inside - to).sqrMagnitude;
                if (gap < bestGap) { bestGap = gap; best = s; }
            }
            return best;
        }

        static void Step(Vector2Int dir)
        {
            if (_pick is Slider slider && slider.IsInteractable() && dir.x != 0
                && (slider.direction == Slider.Direction.LeftToRight || slider.direction == Slider.Direction.RightToLeft))
            {
                float step = (slider.maxValue - slider.minValue) * SliderStep;
                if (slider.wholeNumbers) step = Mathf.Max(1f, Mathf.Round(step));
                slider.value += (slider.direction == Slider.Direction.LeftToRight ? dir.x : -dir.x) * step;
                return;
            }

            // from the edge of the pick that faces the way: the nearest thing that way wins
            var from = ScreenRect((RectTransform)_pick.transform);
            Vector2 d = dir;
            Vector2 origin = from.center + Vector2.Scale(d, from.size * 0.5f);
            Selectable best = null;
            float bestScore = 0f;
            foreach (var s in _found)
            {
                if (s == _pick || !s.IsInteractable()) continue;
                Vector2 to = Centre((RectTransform)s.transform) - origin;
                float along = Vector2.Dot(to, d);
                if (along <= 0.5f) continue;
                float score = along / to.sqrMagnitude;
                if (score > bestScore) { bestScore = score; best = s; }
            }
            if (best == null) return;
            _pick = best;
            Reveal((RectTransform)best.transform);
            Juice.Sound2D(Sfx.UiHover, 0.6f);
        }

        /// A pick outside the window of its scroll list pulls the list along.
        static void Reveal(RectTransform item)
        {
            var scroll = item.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null || item == scroll.content) return;
            var view = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            item.GetWorldCorners(_corners);
            float top = float.MinValue, bottom = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                float y = view.InverseTransformPoint(_corners[i]).y;
                top = Mathf.Max(top, y);
                bottom = Mathf.Min(bottom, y);
            }
            var window = view.rect;
            float shift = top > window.yMax ? top - window.yMax + 4f
                : bottom < window.yMin ? bottom - window.yMin - 4f : 0f;
            if (shift == 0f) return;
            var at = scroll.content.anchoredPosition;
            at.y -= shift; // the list moves the other way
            scroll.content.anchoredPosition = at;
            scroll.velocity = Vector2.zero;
        }

        // ------------------------------------------------------------ the frame --
        static void ShowFrame(RectTransform around)
        {
            if (around == null)
            {
                if (_frame != null && _frame.gameObject.activeSelf) _frame.gameObject.SetActive(false);
                return;
            }
            if (_frame == null) BuildFrame();
            if (!_frame.gameObject.activeSelf) _frame.gameObject.SetActive(true);
            var r = ScreenRect(around);
            float pad = Mathf.Max(3f, Screen.height / 270f);
            _frame.position = r.center;
            _frame.sizeDelta = r.size + new Vector2(pad, pad) * 2f;
        }

        /// Four gold bars on a canvas of their own, over every other one, in screen pixels.
        static void BuildFrame()
        {
            var go = new GameObject("SZ_PadFocus", typeof(RectTransform));
            Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            _frame = new GameObject("Frame", typeof(RectTransform)).GetComponent<RectTransform>();
            _frame.SetParent(go.transform, false);
            _frame.anchorMin = _frame.anchorMax = Vector2.zero;
            _frame.pivot = new Vector2(0.5f, 0.5f);
            float t = Mathf.Max(2f, Screen.height / 360f);
            Bar(new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, t));  // top
            Bar(new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, t));  // bottom
            Bar(new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(t, 0f));  // left
            Bar(new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(t, 0f));  // right

            void Bar(Vector2 min, Vector2 max, Vector2 size)
            {
                var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)bar.transform;
                rt.SetParent(_frame, false);
                rt.anchorMin = min;
                rt.anchorMax = max;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = size;
                var img = bar.GetComponent<Image>();
                img.color = UIKit.Gold;
                img.raycastTarget = false;
            }
        }
    }
}
