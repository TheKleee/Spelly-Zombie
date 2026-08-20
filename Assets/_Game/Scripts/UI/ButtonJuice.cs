using UnityEngine;
using UnityEngine.EventSystems;

namespace SpellyZombie
{
    /// The feel every button in every menu gets: a swell under the cursor, a
    /// squash while held, then a tiny overshoot and wiggle on release. Added
    /// by UIKit.Button, so nothing has to remember it. Unscaled time, menus
    /// may be paused.
    [DisallowMultipleComponent]
    public class ButtonJuice : MonoBehaviour,
        IPointerEnterHandler, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        const float Life = 0.32f;

        /// The click spring as a borrowable curve - k runs 0..1 over SpringLife.
        /// RuneToast pops with this, so there is one definition of the feel.
        public const float SpringLife = Life;
        public static float SpringScale(float k) =>
            1f + Mathf.Sin(k * Mathf.PI * 2f) * 0.06f * (1f - k);
        public static float SpringRollDeg(float k) =>
            Mathf.Sin(k * Mathf.PI * 3f) * 2.4f * (1f - k);

        RectTransform _rt;
        Vector3 _baseScale;
        Quaternion _baseRot;
        float _t = Life;
        bool _down;
        bool _over;        // cursor is on it
        float _swell = 1f; // eased hover/press size, multiplied into the spring

        void Awake()
        {
            _rt = (RectTransform)transform;
            _baseScale = _rt.localScale;
            _baseRot = _rt.localRotation;
        }

        /// Plays the clicked button's spring, then fires the action a beat
        /// later so the animation is seen even when the click rebuilds the
        /// panel. Every UIKit button routes its click through here.
        public static void Press(UnityEngine.UI.Button b, System.Action act)
        {
            if (b == null) { act?.Invoke(); return; }
            var j = b.GetComponent<ButtonJuice>();
            if (j != null) { j._down = false; j._t = 0f; }
            b.interactable = false; // one click per beat
            Runner().StartCoroutine(Fire(b, act));
        }

        static System.Collections.IEnumerator Fire(UnityEngine.UI.Button b, System.Action act)
        {
            yield return new WaitForSecondsRealtime(0.16f);
            if (b != null) b.interactable = true;
            act?.Invoke();
        }

        class JuiceRunner : MonoBehaviour { }
        static JuiceRunner _runner;

        static JuiceRunner Runner()
        {
            if (_runner == null)
            {
                var go = new GameObject("~UIJuice");
                Object.DontDestroyOnLoad(go);
                _runner = go.AddComponent<JuiceRunner>();
            }
            return _runner;
        }

        public void OnPointerEnter(PointerEventData e) { _over = true; }

        public void OnPointerDown(PointerEventData e) { _down = true; }

        public void OnPointerUp(PointerEventData e)
        {
            if (!_down) return;
            _down = false;
            _t = 0f;
        }

        public void OnPointerExit(PointerEventData e)
        {
            _over = false;
            if (_down) { _down = false; _t = Life; }
        }

        void OnDisable()
        {
            _over = false;
            _down = false;
            _swell = 1f;
            if (_rt == null) return;
            _rt.localScale = _baseScale;
            _rt.localRotation = _baseRot;
        }

        void Update()
        {
            if (_rt == null) return;
            float dt = Time.unscaledDeltaTime;

            // pressed beats hovered beats resting
            float want = _down ? 0.94f
                : _over ? DrawingConfig.ButtonHoverScale
                : 1f;
            _swell = Mathf.MoveTowards(_swell, want,
                dt * DrawingConfig.ButtonHoverSpeed * Mathf.Max(0.05f, Mathf.Abs(want - _swell) + 0.1f));

            float spring = 1f, roll = 0f;
            if (_t < Life)
            {
                _t += dt;
                float k = Mathf.Clamp01(_t / Life);
                spring = SpringScale(k);
                roll = SpringRollDeg(k);
            }

            _rt.localScale = _baseScale * (_swell * spring);
            _rt.localRotation = _baseRot * Quaternion.Euler(0f, 0f, roll);
        }
    }
}
