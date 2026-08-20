using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// One random hint per loading screen. Scene loads are blocking, so the
    /// veil masks the new scene's first seconds, holds long enough to read,
    /// then fades. Call Show() right before any SceneManager.LoadScene.
    public class LoadingHints : MonoBehaviour
    {
        // split with the chips: chips teach moment-to-moment keys, these teach systems
        static readonly string[] Keys =
        {
            "hint.alt", "hint.combine", "hint.lift", "hint.erase",
            "hint.body", "hint.pose", "hint.size", "hint.touch",
            "hint.declare", "hint.trance", "hint.wake", "hint.ghost",
        };

        const float HoldSeconds = 2.2f;
        const float FadeSeconds = 0.5f;

        static LoadingHints _live;
        static int _lastPick = -1; // never the same fact twice in a row

        RectTransform _ui;
        Image _back;
        Text _tip;
        float _bornAt;
        bool _sceneArrived;
        float _arrivedAt;

        // runes and ink specks drift up through the dark while the map loads
        readonly System.Collections.Generic.List<Graphic> _drift
            = new System.Collections.Generic.List<Graphic>();
        readonly System.Collections.Generic.List<Vector2> _driftVel
            = new System.Collections.Generic.List<Vector2>();
        readonly System.Collections.Generic.List<float> _driftBase
            = new System.Collections.Generic.List<float>();

        public static void Show()
        {
            if (_live != null) { _live.Restart(); return; }
            var go = new GameObject("LoadingHints");
            DontDestroyOnLoad(go);
            _live = go.AddComponent<LoadingHints>();
        }

        void Awake()
        {
            SceneManager.sceneLoaded += OnLoaded;
            Build();
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            if (_live == this) _live = null;
            UIKit.Retire(_ui);
        }

        void OnLoaded(Scene s, LoadSceneMode m)
        {
            _sceneArrived = true;
            _arrivedAt = Time.unscaledTime;
        }

        void Restart()
        {
            _bornAt = Time.unscaledTime;
            _sceneArrived = false;
            SetAlpha(1f);
        }

        void Build()
        {
            _bornAt = Time.unscaledTime;
            _ui = UIKit.Group(UIKit.Root, "LoadingScreen");
            UIKit.Stretch(_ui);
            // fully opaque: the unloaded scene must never show through
            _back = UIKit.Panel(_ui, null, new Color(0.04f, 0.03f, 0.03f, 1f));
            UIKit.Stretch((RectTransform)_back.transform);
            BuildInkDrift();

            int pick = Random.Range(0, Keys.Length);
            if (pick == _lastPick) pick = (pick + 1) % Keys.Length;
            _lastPick = pick;
            _tip = UIKit.Label(_ui, Loc.T(Keys[pick]), 22,
                new Color(0.95f, 0.9f, 0.78f), TextAnchor.MiddleCenter, true);
            UIKit.Stretch((RectTransform)_tip.transform);
        }

        void BuildInkDrift()
        {
            for (int i = 0; i < 46; i++)
            {
                bool rune = i < 14;
                Graphic g;
                if (rune)
                {
                    var t = new GameObject("Rune").AddComponent<TMPro.TextMeshProUGUI>();
                    t.text = RuneLibrary.IconFor((RuneType)(1 + i % 12), 0);
                    t.fontSize = Random.Range(26f, 56f);
                    t.alignment = TMPro.TextAlignmentOptions.Center;
                    g = t;
                }
                else
                {
                    g = new GameObject("Ink").AddComponent<Image>();
                }
                g.raycastTarget = false;
                g.transform.SetParent(_ui, false);
                var rt = (RectTransform)g.transform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.sizeDelta = rune ? new Vector2(70f, 70f)
                    : Vector2.one * Random.Range(2f, 5f);
                rt.anchoredPosition = new Vector2(Random.Range(0f, 1600f), Random.Range(0f, 900f));

                float baseA = rune ? Random.Range(0.14f, 0.34f) : Random.Range(0.2f, 0.5f);
                g.color = new Color(0.95f, 0.9f, 0.78f, baseA);

                _drift.Add(g);
                _driftVel.Add(new Vector2(Random.Range(-7f, 7f), Random.Range(9f, 30f)));
                _driftBase.Add(baseA);
            }
        }

        void Update()
        {
            if (_ui == null) { Destroy(gameObject); return; }
            _ui.SetAsLastSibling(); // newly built scene UI must not cover the veil

            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _drift.Count; i++)
            {
                var rt = (RectTransform)_drift[i].transform;
                var p = rt.anchoredPosition + _driftVel[i] * dt;
                if (p.y > 950f) p = new Vector2(Random.Range(0f, 1600f), -50f);
                rt.anchoredPosition = p;
            }

            // a load that never lands must not trap the player behind a veil
            if (!_sceneArrived)
            {
                if (Time.unscaledTime - _bornAt > 12f) Destroy(gameObject);
                return;
            }

            float over = Time.unscaledTime - _arrivedAt - HoldSeconds;
            if (over <= 0f) return;
            float a = 1f - over / FadeSeconds;
            if (a <= 0f) { Destroy(gameObject); return; }
            SetAlpha(a);
        }

        void SetAlpha(float a)
        {
            a = Mathf.Clamp01(a);
            if (_back != null) { var c = _back.color; c.a = a; _back.color = c; }
            if (_tip != null) { var c = _tip.color; c.a = a; _tip.color = c; }
            for (int i = 0; i < _drift.Count; i++)
                if (_drift[i] != null)
                {
                    var c = _drift[i].color; c.a = _driftBase[i] * a; _drift[i].color = c;
                }
        }
    }
}
