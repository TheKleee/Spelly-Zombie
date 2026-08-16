using UnityEngine;
using UnityEngine.EventSystems;

namespace SpellyZombie
{
    /// The click feel every button in every menu gets: a squash while held,
    /// then a tiny overshoot and wiggle on release. Added by UIKit.Button, so
    /// nothing has to remember it. Unscaled time, menus may be paused.
    [DisallowMultipleComponent]
    public class ButtonJuice : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        const float Life = 0.32f;

        RectTransform _rt;
        Vector3 _baseScale;
        Quaternion _baseRot;
        float _t = Life;
        bool _down;

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

        public void OnPointerDown(PointerEventData e) { _down = true; }

        public void OnPointerUp(PointerEventData e)
        {
            if (!_down) return;
            _down = false;
            _t = 0f;
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (_down) { _down = false; _t = Life; }
        }

        void OnDisable()
        {
            if (_rt == null) return;
            _rt.localScale = _baseScale;
            _rt.localRotation = _baseRot;
        }

        void Update()
        {
            if (_down)
            {
                _rt.localScale = _baseScale * 0.94f;
                return;
            }

            if (_t >= Life) return;

            _t += Time.unscaledDeltaTime;
            if (_t >= Life)
            {
                _rt.localScale = _baseScale;
                _rt.localRotation = _baseRot;
                return;
            }
            float k = _t / Life;
            float decay = 1f - k;
            _rt.localScale = _baseScale * (1f + Mathf.Sin(k * Mathf.PI * 2f) * 0.06f * decay);
            _rt.localRotation = _baseRot
                * Quaternion.Euler(0f, 0f, Mathf.Sin(k * Mathf.PI * 3f) * 2.4f * decay);
        }
    }
}
