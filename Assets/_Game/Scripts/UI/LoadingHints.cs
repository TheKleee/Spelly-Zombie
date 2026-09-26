using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// One random hint per load. The travel egg is the loading screen: the
    /// black panel only covers frames where no egg is closed around the
    /// camera, the hint and ink drift ride the egg's dark, and everything
    /// fades as the egg begins to open. With no egg at all the panel holds
    /// long enough to read, then fades. Call Show() right before any
    /// SceneManager.LoadScene.
    public class LoadingHints : MonoBehaviour
    {
        // split with the chips: chips teach moment-to-moment keys, these teach systems.
        // A hint that names a key names the one bound now.
        static readonly System.Func<string>[] Lines =
        {
            () => Loc.F("hint.alt", Keys.Label(Act.Precise)),
            () => Loc.T("hint.combine"),
            () => Loc.F("hint.lift", Keys.Label(Act.Use)),
            () => Loc.T("hint.erase"),
            () => Loc.F("hint.body", Keys.Label(Act.Body)),
            () => Loc.T("hint.pose"),
            () => Loc.T("hint.size"),
            () => Loc.T("hint.touch"),
            () => Loc.T("hint.declare"),
            () => Loc.T("hint.trance"),
            () => Loc.T("hint.wake"),
            () => Loc.T("hint.ghost"),
            () => Loc.T("hint.sealshape"),
            () => Loc.F("hint.sealline", Keys.Label(Act.Drop)),
        };

        const float HoldSeconds = 2.2f;
        const float FadeSeconds = 0.5f;

        static LoadingHints _live;
        static int _lastPick = -1; // never the same fact twice in a row

        RectTransform _ui;
        Image _back;
        Text _tip;
        float _bornAt, _upAt; // _upAt: when this trip's hint came up (the log's clock)
        bool _sceneArrived;
        float _arrivedAt;
        float _release = -1f;  // when the fade began
        float _backA = 1f;     // the black panel: up only while nothing else covers

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
            Debug.Log("[SpellyZombie] hint up");
        }

        void Awake()
        {
            SceneManager.sceneLoaded += OnLoaded;
            Build();
        }

        void OnDestroy()
        {
            if (_release < 0f) Debug.Log($"[SpellyZombie] hint removed early, {Time.unscaledTime - _upAt:0.0} s after it came up");
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
            if (_release >= 0f) _upAt = Time.unscaledTime; // fading from the last trip: a new trip's hint
            _bornAt = Time.unscaledTime;
            _sceneArrived = false;
            _release = -1f;
            if (!LoadEgg.Closed) _backA = 1f; // a shell already closed keeps its dark, no black flash
            SetAlpha(1f, _backA);
        }

        const int SortingOrder = 90; // over UIKit's canvas (80)

        void Build()
        {
            _bornAt = _upAt = Time.unscaledTime;
            // a canvas of its own: the egg hides UIKit's canvas for the whole trip, and took the hint with it
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            var like = UIKit.Root.GetComponent<CanvasScaler>(); // sized like the rest of the UI
            if (like != null)
            {
                scaler.uiScaleMode = like.uiScaleMode;
                scaler.referenceResolution = like.referenceResolution;
                scaler.screenMatchMode = like.screenMatchMode;
                scaler.matchWidthOrHeight = like.matchWidthOrHeight;
            }
            _ui = UIKit.Group((RectTransform)transform, "LoadingScreen");
            UIKit.Stretch(_ui);
            // opaque until the egg closes around the camera: the unloaded
            // scene must never show through, and neither must a frame of the
            // new one before the egg has it
            _back = UIKit.Panel(_ui, null, new Color(0.04f, 0.03f, 0.03f, 1f));
            UIKit.Stretch((RectTransform)_back.transform);
            BuildInkDrift();

            int pick = Random.Range(0, Lines.Length);
            if (pick == _lastPick) pick = (pick + 1) % Lines.Length;
            _lastPick = pick;
            _tip = UIKit.Label(_ui, Lines[pick](), 22,
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

            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _drift.Count; i++)
            {
                var rt = (RectTransform)_drift[i].transform;
                var p = rt.anchoredPosition + _driftVel[i] * dt;
                if (p.y > 950f) p = new Vector2(Random.Range(0f, 1600f), -50f);
                rt.anchoredPosition = p;
            }

            // the egg is the loading screen: the panel covers only frames
            // with no egg closed around the camera, and drops the moment
            // there is one
            bool eggClosed = LoadEgg.Closed;
            bool opening = LoadEgg.Active && !LoadEgg.Leaving;
            // the panel never comes back over the egg breaking open
            _backA = eggClosed || opening ? Mathf.MoveTowards(_backA, 0f, dt / 0.25f) : 1f;

            // ★ HIS CALL: with an egg the hint stays the whole trip, every load and wait included,
            // and goes the moment the egg begins to open; with no egg it holds long enough to read,
            // and a load that never lands must not trap the player behind a veil
            if (_release < 0f)
            {
                bool done = LoadEgg.Active ? opening
                    : _sceneArrived ? Time.unscaledTime - _arrivedAt > HoldSeconds
                    : Time.unscaledTime - _bornAt > 12f;
                if (!done) { SetAlpha(1f, _backA); return; }
                _release = Time.unscaledTime;
                Debug.Log($"[SpellyZombie] hint fades after {_release - _upAt:0.0} s up: " + (LoadEgg.Active ? "the egg is opening"
                    : _sceneArrived ? "no egg, read time after the load is up" : "no egg and no load in 12 s"));
            }
            float a = 1f - (Time.unscaledTime - _release) / FadeSeconds;
            if (a <= 0f) { Destroy(gameObject); return; }
            SetAlpha(a, Mathf.Min(_backA, a));
        }

        void SetAlpha(float a, float back)
        {
            a = Mathf.Clamp01(a);
            if (_back != null) { var c = _back.color; c.a = Mathf.Clamp01(back); _back.color = c; }
            if (_tip != null) { var c = _tip.color; c.a = a; _tip.color = c; }
            for (int i = 0; i < _drift.Count; i++)
                if (_drift[i] != null)
                {
                    var c = _drift[i].color; c.a = _driftBase[i] * a; _drift[i].color = c;
                }
        }
    }
}
