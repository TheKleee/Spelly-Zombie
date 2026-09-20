using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ THE PHOTO BOOTH, from the main menu (his idea, Sep 19 2026): a press
    /// kit anyone can make. Characters, creatures, spells, areas, effects and
    /// the island's pieces, placed and posed on an empty stage (his call: no map,
    /// only what is added);
    /// every effect held at its best moment; ink drawn on anything posed; the
    /// light and the look tuned; the picture taken at any size, with the world,
    /// a colour or nothing behind it. Setups are saved in folders and opened
    /// again to keep editing. Everything placed is a still copy: nothing on it
    /// runs, so the pause menu never unfreezes a photo.
    public partial class PhotoBooth : MonoBehaviour
    {
        public static bool Active { get; private set; }
        public static PhotoDef Editing { get; private set; }
        const string Stage = "Spelly Island"; // its sky and light; nothing on it grows
        /// What is placed: a photo with a colour or nothing behind it draws only this layer.
        public const int Layer = 27;
        /// The boxes an effect is clicked by (triggers, so nothing else ever meets them).
        public const int PickLayer = 26;
        /// The grid: the eye sees it, a photo never does.
        public const int GridLayer = 25;
        static string _savedAs, _savedIn; // where the setup is saved; null = not yet

        /// Opens a setup (null = a new one) on its backdrop.
        public static void Open(PhotoDef def)
        {
            Editing = def ?? new PhotoDef();
            bool saved = def != null && !string.IsNullOrEmpty(def.SavedAs) && PhotoLibrary.Exists(def.Folder, def.SavedAs);
            _savedAs = saved ? def.SavedAs : null;
            _savedIn = saved ? def.Folder : null;
            Active = true;
            Travel();
        }

        /// The stage loads as the Map Creator's blank map does: a map with no
        /// biomes stands the scene's own down and grows nothing, so only the sky
        /// and the light stay. The subjects stand again from the data once it has.
        static void Travel()
        {
            MapDef.Active = new MapDef { Name = "Photo Booth", OwnBiomes = true };
            MapDef.UseActiveRules();
            MatchLobby.Seed = 0;
            LoadEgg.Travel(Stage);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("~PhotoBooth");
            DontDestroyOnLoad(go);
            _i = go.AddComponent<PhotoBooth>();
        }

        static PhotoBooth _i;

        /// Leaves the booth for the main menu, where the photos show again (the pause menu's Quit).
        public static void ExitToMenu()
        {
            if (Active && _i != null) _i.Exit();
        }

        /// The pause menu's Esc asks first: posing, drawing or placing ends, and the menu stays shut.
        public static bool Escape() => Active && _i != null && _i.EndTool();

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnLoaded;
            Loc.Changed += Relabel;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            Loc.Changed -= Relabel;
        }

        void OnLoaded(Scene s, LoadSceneMode m)
        {
            Teardown();
            if (!Active) return;
            if (Editing == null || s.name != Stage) { Close(s.name); return; } // left by another door
            StartCoroutine(Setup());
        }

        IEnumerator Setup()
        {
            yield return null; // the scene has stood its own biomes down
            BuildCamera();
            _grid = MapCreator.MakeGrid(); // where things go; never in a photo
            _grid.layer = GridLayer;
            BuildStage();
            BuildUI();
            StandAll();
            ApplyStage();
            ResetHistory();
            Hint();
            LoadEgg.Dismiss(); // no body arrives here, the egg would wait for a match
        }

        static void Close(string arrived)
        {
            Active = false;
            MapDef.Active = null;
            MatchLobby.Seed = 0;
            MapDef.UseActiveRules();
            if (arrived == "Menu") PhotosScreen.OpenOnMenu = true;
        }

        void Teardown()
        {
            if (_ui != null) { UIKit.Retire(_ui); _ui = null; }
            if (_cam != null) { Destroy(_cam.gameObject); _cam = null; }
            if (_selBox != null) { Destroy(_selBox.gameObject); _selBox = null; }
            if (_grid != null) { Destroy(_grid); _grid = null; }
            foreach (var s in _subjects) if (s.Root != null) Destroy(s.Root);
            _subjects.Clear();
            _byRoot.Clear();
            _inks.Clear();
            _drawing = null;
            DropShells();
            TeardownStage();
            _fly = null;
            _sel = null;
            _posing = null;
            _sculpt = null;
            _playing = null;
            _tool = Tool.None;
            _dragging = false;
            _undo.Clear(); _redo.Clear(); _committed = null;
        }

        void Exit()
        {
            CaptureCamera();
            if (_ui != null) { UIKit.Retire(_ui); _ui = null; }
            LoadEgg.Travel("Menu"); // Close runs when the menu has loaded
        }

        /// Another setup on the same stage, no loading: the eye goes where it
        /// says (`moveEye`), everything placed stands again from its data.
        void Become(PhotoDef def, string savedAs, string savedIn, bool moveEye)
        {
            if (_posing != null) { _posing = null; _sculpt = null; }
            if (_tool == Tool.Ink) { EndInk(); DropShells(); }
            _playing = null;
            _tool = Tool.None;
            _pick = null;
            Editing = def;
            _savedAs = savedAs;
            _savedIn = savedIn;
            _sel = null;
            if (moveEye && _cam != null)
            {
                _cam.transform.position = def.CamPos;
                _fly.Face(def.CamYaw, def.CamPitch);
                _cam.fieldOfView = Mathf.Clamp(def.Lens, 10f, 100f);
            }
            StandAll();
            ApplyStage();
            ResetHistory();
            if (_winItem != null) _winItem.Hide();
            BuildAllWindows();
            Hint();
        }

        // ------------------------------------------------------------ camera --
        Camera _cam;
        CreatorFly _fly;
        GameObject _grid;

        /// What a click can land on: never an effect's pick box.
        static int GroundMask => Physics.DefaultRaycastLayers & ~(1 << PickLayer);

        void BuildCamera()
        {
            UniversalAdditionalCameraData before = null;
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (c.enabled && c.CompareTag("MainCamera")) before = c.GetUniversalAdditionalCameraData();
                c.enabled = false; // the scene's own eye rests
            }
            var go = new GameObject("BoothCamera") { tag = "MainCamera" };
            _cam = go.AddComponent<Camera>();
            _cam.farClipPlane = 3000f;
            _cam.cullingMask &= ~(1 << PreviewPane.Layer);
            var data = _cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true; // the look sliders show as they will be taken
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            if (before != null) data.volumeLayerMask = before.volumeLayerMask;
            go.AddComponent<AudioListener>();
            _fly = new CreatorFly(_cam, GroundMask);
            go.transform.position = Editing.CamPos;
            _fly.Face(Editing.CamYaw, Editing.CamPitch);
            _cam.fieldOfView = Mathf.Clamp(Editing.Lens, 10f, 100f);
        }

        /// The eye goes into the setup (on Save, on leaving): it is never an undo step.
        void CaptureCamera()
        {
            if (_cam == null || Editing == null) return;
            Editing.CamPos = _cam.transform.position;
            Editing.CamYaw = _fly.Yaw;
            Editing.CamPitch = _fly.Pitch;
            Editing.Lens = _cam.fieldOfView;
        }

        // ------------------------------------------------------------- input --
        enum Tool { None, Place, Ink }
        Tool _tool;
        Subject _posing;           // the character being sculpted
        PoseSculpt _sculpt;
        bool _poseDown;            // the press began off the windows

        void Update()
        {
            if (!Active || _cam == null) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            History(kb, mouse);
            bool typing = UIKit.Typing;
            _fly.Tick(kb, mouse, typing, _sculpt == null || !_sculpt.Holding);
            if (_flashUntil > 0f && Time.unscaledTime > _flashUntil) { _flashUntil = 0f; Hint(); }

            TickPlay();
            if (_tool == Tool.Ink) TickInk(mouse);
            else if (_posing != null) TickPose(mouse);
            else
            {
                if (mouse.leftButton.wasPressedThisFrame && !MapCreator.OverUI()) Press(mouse.position.ReadValue());
                if (_dragging)
                {
                    if (mouse.leftButton.isPressed) Drag(mouse.position.ReadValue());
                    else EndDrag();
                }
            }

            if (typing || _posing != null || _tool == Tool.Ink) return;
            if (kb.deleteKey.wasPressedThisFrame) DeleteSelected();
            if (kb.rKey.wasPressedThisFrame && !kb.leftCtrlKey.isPressed && !kb.rightCtrlKey.isPressed) TurnSelected(15f);
            if (kb.leftBracketKey.wasPressedThisFrame) ResizeSelected(0.9f);
            if (kb.rightBracketKey.wasPressedThisFrame) ResizeSelected(1.1f);
        }

        void LateUpdate()
        {
            if (!Active || _cam == null) return;
            RefreshInk();
            UpdateSelectBox();
            TickStage();
            // the grid is a helper: never in a clip or a photo taken with I and P either
            if (_grid != null && !_shooting && _grid.activeSelf == ClipRecorder.Busy) _grid.SetActive(!ClipRecorder.Busy);
        }

        /// Posing, drawing or placing ends. True when one did.
        bool EndTool()
        {
            if (_posing != null) { StopPosing(); return true; }
            if (_tool != Tool.None)
            {
                if (_tool == Tool.Ink) { EndInk(); DropShells(); }
                _tool = Tool.None;
                _pick = null;
                BuildAddWindow();
                BuildInkWindow();
                Hint();
                return true;
            }
            return false;
        }

        // --------------------------------------------------------- selection --
        Subject _sel;
        bool _dragging, _dragMoved, _lifting;
        Vector2 _pressScreen;
        Vector3 _grab;            // from the pointer's ground point to the subject, kept while carrying it
        float _dragLift;          // how high above the ground it rides
        float _liftStartY, _rootStartY;
        static readonly RaycastHit[] _hits = new RaycastHit[64];

        void Press(Vector2 screen)
        {
            var ray = _cam.ScreenPointToRay(screen);
            Physics.SyncTransforms(); // what moved since the last physics step is where it shows
            // a subject under the pointer is picked up, not placed on again
            var s = SubjectUnder(ray);
            if (s != null)
            {
                if (_tool == Tool.Place) { _tool = Tool.None; _pick = null; BuildAddWindow(); }
                Select(s);
                BeginDrag(s, ray, screen);
                return;
            }
            if (_tool == Tool.Place)
            {
                if (MapCreator.GroundUnder(ray, null, out var at, GroundMask)) PlaceAt(at);
                return;
            }
            Select(null);
        }

        /// The subject the pointer is on: its own shapes first, then the box of an
        /// effect in front of whatever ground the pointer meets.
        Subject SubjectUnder(Ray ray)
        {
            float ground = float.MaxValue;
            if (Physics.Raycast(ray, out var hit, 5000f, GroundMask, QueryTriggerInteraction.Ignore))
            {
                var owner = Owner(hit.collider.transform);
                if (owner != null) return owner;
                ground = hit.distance;
            }
            Subject best = null;
            int n = Physics.RaycastNonAlloc(ray, _hits, 5000f, 1 << PickLayer, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].distance >= ground) continue;
                var owner = Owner(_hits[i].collider.transform);
                if (owner == null) continue;
                ground = _hits[i].distance;
                best = owner;
            }
            return best;
        }

        Subject Owner(Transform t)
        {
            for (var w = t; w != null; w = w.parent)
                if (_byRoot.TryGetValue(w, out var s)) return s;
            return null;
        }

        void Select(Subject s)
        {
            if (_posing != null && _posing != s) StopPosing();
            if (_playing != null && _playing != s) StopPlaying(true);
            _sel = s;
            if (_winAnims != null && _winAnims.Visible) BuildAnimWindow();
            if (_winItem != null)
            {
                if (s != null) { _winItem.Show(); BuildItemWindow(); }
                else _winItem.Hide();
            }
            Hint();
        }

        void BeginDrag(Subject s, Ray ray, Vector2 screen)
        {
            _dragging = true;
            _dragMoved = false;
            _pressScreen = screen;
            var kb = Keyboard.current;
            _lifting = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
            var root = s.Root.transform;
            _rootStartY = root.position.y;
            _liftStartY = MapCreator.LinePoint(ray, root.position).y;
            _dragLift = root.position.y - GroundBelow(s);
            _grab = MapCreator.GroundUnder(ray, root, out var at, GroundMask) ? root.position - at : Vector3.zero;
            _grab.y = 0f;
        }

        void Drag(Vector2 screen)
        {
            if (!_dragMoved && (screen - _pressScreen).sqrMagnitude < 36f) return; // a click is not a drag
            if (_sel == null || _sel.Root == null) { _dragging = false; return; }
            _dragMoved = true;
            var root = _sel.Root.transform;
            var ray = _cam.ScreenPointToRay(screen);
            if (_lifting)
            {
                // Shift: up and down on its upright line
                float dy = MapCreator.LinePoint(ray, root.position).y - _liftStartY;
                root.position = new Vector3(root.position.x, _rootStartY + dy, root.position.z);
            }
            else
            {
                // over the ground and whatever stands on it, as high above it as it was
                if (!MapCreator.GroundUnder(ray, root, out var at, GroundMask)) return;
                root.position = new Vector3(at.x + _grab.x, at.y + _dragLift, at.z + _grab.z);
            }
            _sel.Item.Pos = root.position;
        }

        void EndDrag()
        {
            _dragging = false;
            if (!_dragMoved || _sel == null) return;
            Moved(_sel);
            SettleEyes(_sel);
            if (_winItem != null && _winItem.Visible) BuildItemWindow();
        }

        /// The ground under the subject's middle, itself left out.
        float GroundBelow(Subject s)
        {
            var root = s.Root.transform;
            Vector3 c = Center(s);
            var ray = new Ray(new Vector3(c.x, Mathf.Max(c.y, root.position.y) + 500f, c.z), Vector3.down);
            Physics.SyncTransforms();
            return MapCreator.GroundUnder(ray, root, out var at, GroundMask) ? at.y : 0f;
        }

        void DeleteSelected()
        {
            if (_sel == null) return;
            int index = _subjects.IndexOf(_sel);
            if (index < 0) return;
            RemoveItem(index);
            Select(null);
        }

        void TurnSelected(float degrees)
        {
            if (_sel == null) return;
            Turn(_sel, _sel.Item.Yaw + degrees, _sel.Item.Tilt, _sel.Item.Roll);
            if (_winItem != null && _winItem.Visible) BuildItemWindow();
        }

        void ResizeSelected(float k)
        {
            if (_sel == null) return;
            Resize(_sel, _sel.Item.Size * k);
            if (_winItem != null && _winItem.Visible) BuildItemWindow();
        }

        // ------------------------------------------------------- selection box --
        LineRenderer _selBox;

        /// A gold box around the selected subject, riding along as it moves, turns and grows.
        void UpdateSelectBox()
        {
            var s = _sel != null && _sel.Root != null ? _sel : null;
            if (_selBox == null)
            {
                if (s == null) return;
                _selBox = new GameObject("~SelectBox") { layer = Layer }.AddComponent<LineRenderer>(); // seen over a colour too
                _selBox.useWorldSpace = true;
                _selBox.positionCount = 16;
                _selBox.numCornerVertices = 2;
                _selBox.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _selBox.receiveShadows = false;
                _selBox.sharedMaterial = MatterFX.Get(new Color(1f, 0.82f, 0.25f), MoteShade.Opaque);
            }
            bool on = s != null && !_shooting;
            if (_selBox.gameObject.activeSelf != on) _selBox.gameObject.SetActive(on);
            if (!on) return;
            var b = Bounds(s);
            b.Expand(0.2f);
            _selBox.SetPositions(MapCreator.BoxEdges(b.center, b.size));
            _selBox.widthMultiplier = Mathf.Clamp(b.size.magnitude * 0.01f, 0.02f, 0.3f);
        }

        // ------------------------------------------------------------ posing --
        void StartPosing(Subject s)
        {
            if (s == null || s.Rig == null) return;
            if (_tool == Tool.Ink) { EndInk(); DropShells(); }
            if (_playing == s) StopPlaying(true);
            _tool = Tool.None;
            _pick = null;
            _posing = s;
            _sculpt = new PoseSculpt(_cam, s.Rig, s.Root.transform);
            BuildItemWindow();
            BuildAddWindow();
            BuildInkWindow();
            Hint();
        }

        void StopPosing()
        {
            if (_posing == null) return;
            CapturePose(_posing);
            _posing = null;
            _sculpt = null;
            _poseDown = false;
            if (_winItem != null && _winItem.Visible) BuildItemWindow();
            Hint();
        }

        void TickPose(Mouse mouse)
        {
            if (_posing.Root == null) { StopPosing(); return; }
            if (mouse.leftButton.wasPressedThisFrame) _poseDown = !MapCreator.OverUI();
            if (!_poseDown) return;
            Physics.SyncTransforms();
            _sculpt.Tick(mouse, 1 << Layer, 5000f, null);
            if (!mouse.leftButton.isPressed) _poseDown = false;
        }

        // ------------------------------------------------------------ history --
        /// ★ UNDO AND REDO (Ctrl+Z, Ctrl+R), the Map Creator's way: when a gesture
        /// ends the setup is compared with the last step, and a difference becomes
        /// a new one. The eye is never a step.
        const int HistoryMax = 100;
        readonly List<string> _undo = new List<string>(), _redo = new List<string>();
        string _committed;
        bool _wasTyping, _historyBusy;

        string StateNow()
        {
            CaptureAll();
            return JsonUtility.ToJson(Editing);
        }

        void ResetHistory()
        {
            _undo.Clear();
            _redo.Clear();
            _committed = Editing != null ? StateNow() : null;
        }

        void History(Keyboard kb, Mouse mouse)
        {
            bool typing = UIKit.Typing;
            if (mouse.leftButton.wasReleasedThisFrame || (_wasTyping && !typing)
                || kb.deleteKey.wasReleasedThisFrame || kb.rKey.wasReleasedThisFrame
                || kb.leftBracketKey.wasReleasedThisFrame || kb.rightBracketKey.wasReleasedThisFrame)
                CommitStep();
            _wasTyping = typing;
            if (typing || _dragging || _drawing != null || (_sculpt != null && _sculpt.Holding)) return;
            if (!kb.leftCtrlKey.isPressed && !kb.rightCtrlKey.isPressed) return;
            // the letters as printed: Z sits elsewhere on a QWERTZ keyboard
            var z = kb.FindKeyOnCurrentKeyboardLayout("z");
            var r = kb.FindKeyOnCurrentKeyboardLayout("r");
            if (z != null && z.wasPressedThisFrame) { Walk(_undo, _redo); _fly?.Chord(); }
            else if (r != null && r.wasPressedThisFrame) { Walk(_redo, _undo); _fly?.Chord(); }
        }

        void CommitStep()
        {
            if (_historyBusy || Editing == null || _cam == null) return;
            string now = StateNow();
            if (_committed == null) { _committed = now; return; }
            if (now == _committed) return;
            Push(_undo, _committed);
            _redo.Clear();
            _committed = now;
        }

        static void Push(List<string> stack, string s)
        {
            stack.Add(s);
            if (stack.Count > HistoryMax) stack.RemoveAt(0);
        }

        void Walk(List<string> from, List<string> to)
        {
            CommitStep(); // anything not yet a step becomes one first
            if (from.Count == 0) return;
            string s = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            Push(to, _committed);
            ApplyState(s);
            _committed = s;
        }

        /// The setup as the step holds it, and everything that shows it.
        void ApplyState(string state)
        {
            int keep = _sel != null ? _subjects.IndexOf(_sel) : -1;
            var eye = (Editing.CamPos, Editing.CamYaw, Editing.CamPitch, Editing.Lens);
            string folder = Editing.Folder, savedAs = Editing.SavedAs;
            _historyBusy = true;
            try
            {
                if (_posing != null) { _posing = null; _sculpt = null; }
                _playing = null;
                JsonUtility.FromJsonOverwrite(state, Editing);
                (Editing.CamPos, Editing.CamYaw, Editing.CamPitch, Editing.Lens) = eye;
                Editing.Folder = folder;
                Editing.SavedAs = savedAs;
                StandAll();
                if (_tool == Tool.Ink) BakeShells(); // the bodies stood again
                ApplyStage();
                _sel = null;
                Select(keep >= 0 && keep < _subjects.Count ? _subjects[keep] : null);
                BuildAllWindows();
            }
            finally { _historyBusy = false; }
        }

        // --------------------------------------------------------------- hint --
        Text _hint;
        string _flash;
        float _flashUntil;

        void FlashError(string text)
        {
            Flash(text);
            Juice.Sound2D(Sfx.UiError);
        }

        void Flash(string text)
        {
            _flash = text;
            _flashUntil = Time.unscaledTime + 3f;
            Hint();
        }

        void Hint()
        {
            if (_hint == null) return;
            var kb = Keyboard.current;
            string s;
            if (_flashUntil > 0f) s = _flash;
            else if (_posing != null) s = Loc.T("photo.hint.pose");
            else if (_tool == Tool.Ink) s = Loc.T("photo.hint.ink");
            else if (_tool == Tool.Place && _pick != null) s = Loc.F("photo.hint.place", PickName(_pick));
            else if (_sel != null)
                s = Loc.F("photo.hint.picked", MapCreator.KeyName(kb?.rKey, "R"),
                    MapCreator.KeyName(kb?.leftBracketKey, "["), MapCreator.KeyName(kb?.rightBracketKey, "]"));
            else s = Loc.T("photo.hint.idle");
            _hint.text = s;
        }
    }
}
