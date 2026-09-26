using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// A floating page over a world object: do something here and this page
    /// is yours. While the situation holds it shows a question mark where the
    /// drawing would be, plus a bar underneath when there is a fill time; on
    /// Flip the question mark becomes the earned page, which pops, holds and
    /// poofs. Screen-space float on UIKit.FloatRoot, like AimBadge. The card
    /// is local; the poof and pop reach the other machines through UnlockMsg.
    public class UnlockMark : MonoBehaviour
    {
        static readonly Color Ink = new Color(0.15f, 0.1f, 0.2f);
        const float StaleSeconds = 0.5f;  // unrefreshed marks fade after this
        const float FadeSeconds = 0.35f;
        const float RiseSeconds = 0.15f;  // a new mark eases in
        const float BarGap = 4f;
        const float BarHeight = 14f; // on screen, whatever the card is scaled to: the fill inside is 8 tall
        const float Aspect = 1024f / 742f; // his page spread

        /// The waiting mark is a key badge wide, like F and E; the flip grows it to the page.
        static float HintScale => UIKit.KeyBadgeSize * DrawingConfig.UnlockMarkHintBadgeMul
            / Mathf.Max(1f, DrawingConfig.UnlockMarkPageWidth);

        static UnlockMark _me;

        /// The key for a mark over a world object, the same from every caller.
        public static int KeyFor(Transform t) => 10000 + (t.GetInstanceID() & 0x3FF);

        class Mark
        {
            public int Key;
            public Transform Anchor;
            public float Up;
            public Vector3 At;           // last known world point
            public float Progress = -1f;
            public float LastShow;       // unscaled time of the last Show

            public RectTransform Card;
            public CanvasGroup Cg;
            public Text Question;
            public RawImage Art;
            public UIKit.UIBar Bar;

            public bool Flipping;        // resolved: refreshes no longer apply
            public bool Fading, Poofed;
            public float Fade, Age, Alpha;
        }

        readonly List<Mark> _marks = new List<Mark>();

        // another machine's page: the poof lands here when their page would
        readonly List<Vector3> _poofAt = new List<Vector3>();
        readonly List<float> _poofDue = new List<float>();

        static UnlockMark Me
        {
            get
            {
                if (_me == null)
                {
                    var go = new GameObject("UnlockMarks");
                    DontDestroyOnLoad(go);
                    _me = go.AddComponent<UnlockMark>();
                }
                return _me;
            }
        }

        /// Creates or refreshes the mark for this key; the world point is the
        /// anchor lifted by up. progress01 below 0 hides the bar.
        public static void Show(int key, Transform anchor, float up, float progress01)
        {
            if (anchor == null) return;
            var m = Me.Touch(key, anchor.position + Vector3.up * up, progress01);
            if (m == null) return;
            m.Anchor = anchor;
            m.Up = up;
        }

        /// Same, at a fixed world point.
        public static void ShowAt(int key, Vector3 worldPos, float progress01)
        {
            var m = Me.Touch(key, worldPos, progress01);
            if (m == null) return;
            m.Anchor = null;
        }

        /// The situation resolved: the question mark becomes the rune's page.
        /// RuneType.None = no page, the mark just fades. False when no mark
        /// was waiting under this key.
        public static bool Flip(int key, RuneType rune)
        {
            if (_me == null) return false;
            var m = _me.Find(key);
            if (m == null || m.Flipping) return false;
            _me.Resolve(m, rune);
            return true;
        }

        /// Flip a mark that may not exist yet (instant deeds).
        public static void FlipAt(int key, Vector3 worldPos, RuneType rune)
        {
            var m = Me.Find(key);
            if (m != null && m.Flipping) return;
            if (m == null)
            {
                if (rune == RuneType.None) return;
                m = Me.Touch(key, worldPos, -1f);
                if (m == null) return;
            }
            m.Anchor = null;
            m.At = worldPos;
            Me.Resolve(m, rune);
        }

        /// Someone else earned a page over this world point (UnlockMsg): no
        /// card here, just the poof and pop when their page would poof.
        public static void PoofAt(Vector3 worldPos)
        {
            Me._poofAt.Add(worldPos);
            Me._poofDue.Add(Time.unscaledTime + DrawingConfig.UnlockMarkPageSeconds);
        }

        /// Every machine plays its own from UnlockMsg: never relayed as FX.
        static void Poof(Vector3 at)
        {
            if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, at, null, 0f, false);
            bool quiet = NetSync.FxQuiet;
            NetSync.FxQuiet = true;
            try { Juice.Pop(at); }
            finally { NetSync.FxQuiet = quiet; }
        }

        /// The situation ended with nothing: fade out.
        public static void Hide(int key)
        {
            if (_me == null) return;
            var m = _me.Find(key);
            if (m == null || m.Flipping) return;
            m.Fading = true;
        }

        Mark Find(int key)
        {
            for (int i = 0; i < _marks.Count; i++)
                if (_marks[i].Key == key) return _marks[i];
            return null;
        }

        Mark Touch(int key, Vector3 at, float progress01)
        {
            var m = Find(key);
            if (m != null && m.Flipping) return null;
            if (m == null)
            {
                m = new Mark { Key = key };
                BuildCard(m);
                if (m.Card == null) return null;
                _marks.Add(m);
            }
            m.At = at;
            m.Progress = progress01;
            m.LastShow = Time.unscaledTime;
            m.Fading = false; // a refresh revives a fading mark
            m.Fade = 0f;
            return m;
        }

        void BuildCard(Mark m)
        {
            var root = UIKit.FloatRoot;
            if (root == null) return;
            float w = DrawingConfig.UnlockMarkPageWidth;
            // one object per live mark: a shared name would be re-adopted by the next build
            var rt = UIKit.PageCard(root, "UnlockMark#" + m.Key, w, Aspect, out var raw, out var cg);
            float h = rt.sizeDelta.y;
            raw.gameObject.SetActive(false); // the drawing comes with the flip

            var q = UIKit.Label(rt, "?", Mathf.RoundToInt(h * 0.62f), Ink, TextAnchor.MiddleCenter, true);
            UIKit.Stretch((RectTransform)q.transform);

            // the card shrinks to a hint, the bar keeps its screen size: as wide as the '?' page
            float hint = HintScale;
            var skin = UISkin.I;
            var size = new Vector2(w * hint, BarHeight);
            var bar = UIKit.Bar(rt, skin != null ? skin.ProgressGreen : null, size,
                skin != null ? (Color?)null : new Color(0.35f, 0.8f, 0.3f));
            UIKit.Place(bar.Rt, new Vector2(0.5f, 0f), new Vector2(0f, -BarGap / hint), size);
            bar.Rt.pivot = new Vector2(0.5f, 1f); // hangs below the page
            bar.Rt.localScale = Vector3.one / hint;
            bar.Rt.gameObject.SetActive(false);

            rt.localScale = Vector3.one * hint; // a hint, not the page yet

            m.Card = rt;
            m.Cg = cg;
            m.Question = q;
            m.Art = raw;
            m.Bar = bar;
        }

        void Resolve(Mark m, RuneType rune)
        {
            Texture2D art = null;
            if (rune != RuneType.None)
            {
                art = GrimoirePages.PageArt(rune, Sides.LocalIsAcolyte);
                if (art == null) art = Wardrobe.RuneIcon(rune, Ink);
            }
            if (art == null)
            {
                m.Fading = true; // no page: the question mark just goes
                return;
            }
            m.Flipping = true;
            m.Age = 0f;
            m.Key = int.MinValue; // the key is free again while this page finishes
            if (m.Question != null) m.Question.gameObject.SetActive(false);
            if (m.Bar != null && m.Bar.Rt != null) m.Bar.Rt.gameObject.SetActive(false);
            if (m.Art != null)
            {
                m.Art.texture = art;
                m.Art.gameObject.SetActive(true);
            }
        }

        void LateUpdate()
        {
            float now = Time.unscaledTime;
            for (int i = _poofDue.Count - 1; i >= 0; i--)
            {
                if (now < _poofDue[i]) continue;
                Poof(_poofAt[i]);
                _poofAt.RemoveAt(i);
                _poofDue.RemoveAt(i);
            }

            if (_marks.Count == 0) return;
            var cam = Camera.main;
            float dt = Time.unscaledDeltaTime; // a pause must not freeze the tell

            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                var m = _marks[i];
                if (m.Card == null) { _marks.RemoveAt(i); continue; }
                if (m.Anchor != null) m.At = m.Anchor.position + Vector3.up * m.Up;

                if (m.Flipping) TickFlip(m, dt);
                else TickHold(m, dt, now);

                if (m.Card == null) { _marks.RemoveAt(i); continue; }
                Float(m.Card, cam, m.At);
            }
        }

        void TickHold(Mark m, float dt, float now)
        {
            if (!m.Fading && now - m.LastShow > StaleSeconds) m.Fading = true;
            if (m.Fading)
            {
                m.Fade += dt;
                if (m.Fade >= FadeSeconds) { Retire(m); return; }
                m.Alpha = Mathf.Min(m.Alpha, 1f - m.Fade / FadeSeconds);
            }
            else m.Alpha = Mathf.Min(1f, m.Alpha + dt / RiseSeconds);
            if (m.Cg != null) m.Cg.alpha = m.Alpha;

            bool bar = m.Progress >= 0f;
            if (m.Bar != null && m.Bar.Rt != null)
            {
                if (m.Bar.Rt.gameObject.activeSelf != bar) m.Bar.Rt.gameObject.SetActive(bar);
                if (bar) m.Bar.Set(m.Progress);
            }
        }

        void TickFlip(Mark m, float dt)
        {
            m.Age += dt;
            float hold = DrawingConfig.UnlockMarkPageSeconds;
            float pop = ButtonJuice.SpringLife;
            if (m.Age < pop)
            {
                // the hint grows into the real page
                float k = m.Age / pop;
                float grow = Mathf.Lerp(HintScale, 1f, 1f - (1f - k) * (1f - k));
                m.Card.localScale = Vector3.one * (grow * ButtonJuice.SpringScale(k));
                m.Card.localRotation = Quaternion.Euler(0f, 0f, ButtonJuice.SpringRollDeg(k));
            }
            else
            {
                m.Card.localScale = Vector3.one;
                m.Card.localRotation = Quaternion.identity;
            }

            float a = Mathf.Max(m.Alpha, Mathf.Clamp01(m.Age / 0.05f)); // a fresh mark lands at once
            if (m.Age >= hold)
            {
                if (!m.Poofed)
                {
                    m.Poofed = true;
                    Poof(m.At); // the others got theirs from UnlockMsg
                }
                a *= Mathf.Clamp01(1f - (m.Age - hold) / FadeSeconds);
            }
            if (m.Cg != null) m.Cg.alpha = a;

            if (m.Age >= hold + FadeSeconds) Retire(m);
        }

        void Retire(Mark m)
        {
            if (m.Card != null) UIKit.Retire(m.Card);
            m.Card = null;
            m.Cg = null;
            m.Question = null;
            m.Art = null;
            m.Bar = null;
        }

        /// Pins a float to the world point; hidden behind the camera.
        static void Float(RectTransform rt, Camera cam, Vector3 at)
        {
            if (rt == null) return;
            if (cam == null)
            {
                if (rt.gameObject.activeSelf) rt.gameObject.SetActive(false);
                return;
            }
            Vector3 sp = cam.WorldToScreenPoint(at);
            bool visible = sp.z > 0f;
            if (rt.gameObject.activeSelf != visible) rt.gameObject.SetActive(visible);
            if (visible) rt.position = sp;
        }
    }
}
