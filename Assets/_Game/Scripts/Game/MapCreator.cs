using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ THE MAP CREATOR, opened from the Maps screen. A blank map under the
    /// base scene's sky: place biomes, shape and tune each one, grow the ground
    /// from them, set pieces and creatures on it, write the map's own spells
    /// and runes. Every tool is a floating window the ribbon shows and hides.
    /// Preview opens a map to fly around it and nothing else.
    public partial class MapCreator : MonoBehaviour
    {
        public static bool Active { get; private set; }
        public static bool Previewing { get; private set; }
        public static MapDef Editing { get; private set; }
        static MapWorkshop.Item _previewItem;   // a shared map being looked at: Save keeps it
        static string _savedAs;                 // the file the map stands in, null = never saved
        bool _savable;                          // what the Map window's Save button shows

        public static void Open(MapDef def) => Open(def, false, null);

        /// Opens a map in the creator (null = a blank one), or only to look at it.
        public static void Open(MapDef def, bool preview, MapWorkshop.Item item)
        {
            Editing = def ?? new MapDef { OwnBiomes = true };
            Editing.Repair();
            if (Editing.Seed == 0) Editing.Seed = Random.Range(1, int.MaxValue);
            if (string.IsNullOrEmpty(Editing.BookJson))
            {
                SpellBook.Forget(); // a new map starts from the game's own spells and runes
                Editing.BookJson = SpellBook.LiveJson();
            }
            if (string.IsNullOrEmpty(Editing.Author) && SteamLobby.SteamReady)
                Editing.Author = Steamworks.SteamFriends.GetPersonaName();
            _savedAs = MapLibrary.Exists(Editing.Name) ? Editing.Name : null;
            Previewing = preview;
            _previewItem = item;
            Active = true;
            MapDef.Active = Editing;
            MapDef.UseActiveRules();
            MatchLobby.Seed = Editing.Seed;
            LoadEgg.Travel(Editing.Base);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("~MapCreator");
            DontDestroyOnLoad(go);
            _i = go.AddComponent<MapCreator>();
        }

        static MapCreator _i;

        /// Leaves the creator for the main menu, where the maps show again (the pause menu's Quit).
        public static void ExitToMenu()
        {
            if (Active && _i != null) _i.Exit();
        }

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
            if (Editing == null || s.name != Editing.Base) { Close(s.name); return; } // left by another door
            StartCoroutine(Setup());
        }

        IEnumerator Setup()
        {
            yield return null; // the base scene's Start has stood this map's biomes and pieces
            _map = FindFirstObjectByType<SpellyMap>();
            BuildCamera();
            BuildGrid();
            BuildUI();
            RebuildViews();
            DressPieces();
            if (_grid != null) _grid.SetActive(Editing.Biomes.Count == 0 || !Editing.OwnBiomes);
            ResetHistory();
            // the cover lifts LAST, with the map and the windows already standing:
            // no half-built map, and no windows floating over the dark
            LoadEgg.Dismiss(); // no body arrives here, the egg would wait for a match
        }

        /// The creator is over: the game's own book and drawings come back.
        static void Close(string arrived)
        {
            Active = false;
            Previewing = false;
            _previewItem = null;
            MapDef.Active = null;
            MatchLobby.Seed = 0;
            MapDef.UseActiveRules();
            if (arrived == "Menu") MapsScreen.OpenOnMenu = true;
        }

        void Teardown()
        {
            if (_ui != null) { UIKit.Retire(_ui); _ui = null; }
            if (_cam != null) { Destroy(_cam.gameObject); _cam = null; }
            if (_grid != null) { Destroy(_grid); _grid = null; }
            foreach (var v in _views) if (v.Root != null) Destroy(v.Root);
            _views.Clear();
            for (int i = 0; i < _handles.Length; i++) if (_handles[i] != null) { Destroy(_handles[i].gameObject); _handles[i] = null; }
            if (_selBox != null) { Destroy(_selBox.gameObject); _selBox = null; }
            _selPiece = null; _dragPiece = null;
            _undo.Clear(); _redo.Clear(); _committed = null; _fly = null;
            _screen = null; _screenKind = ScreenKind.None; _areaScreen = null;
            _sel = Sel.None; _selIndex = -1;
            _tool = Tool.None; _pick = -1; _pickTemplate = -1;
            _dragging = false; _stale = false;
        }

        // ------------------------------------------------------------ camera --
        SpellyMap _map;
        Camera _cam;
        Transform _rig;
        CreatorFly _fly;

        void BuildCamera()
        {
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false; // the scene's own eye rests
            var go = new GameObject("CreatorCamera") { tag = "MainCamera" };
            _cam = go.AddComponent<Camera>();
            _cam.farClipPlane = 3000f;
            _cam.cullingMask &= ~(1 << PreviewPane.Layer);
            go.AddComponent<AudioListener>();
            _rig = go.transform;
            _fly = new CreatorFly(_cam, Physics.DefaultRaycastLayers);
            _rig.position = new Vector3(0f, 70f, -90f);
            _fly.Face(0f, 40f);
        }

        internal static bool OverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        internal static string KeyName(KeyControl k, string plain) =>
            k != null && !string.IsNullOrEmpty(k.displayName) ? k.displayName.ToUpperInvariant() : plain;

        /// The controls line with the letters printed on this keyboard.
        static string HelpText()
        {
            var kb = Keyboard.current;
            if (kb == null) return Loc.F("creator.help", "WASD", "R", "[", "]");
            return Loc.F("creator.help", KeyName(kb.wKey, "W") + KeyName(kb.aKey, "A") + KeyName(kb.sKey, "S") + KeyName(kb.dKey, "D"),
                KeyName(kb.rKey, "R"), KeyName(kb.leftBracketKey, "["), KeyName(kb.rightBracketKey, "]"));
        }

        void Update()
        {
            if (!Active || _cam == null) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            if (!Previewing) History(kb, mouse);
            if (_screen != null) // a creator over the screen owns the input
            {
                _fly.Release();
                if (_screenKind == ScreenKind.Spells) TickSpells();
                else if (_screenKind == ScreenKind.Creatures) TickCreatures();
                return;
            }
            bool typing = UIKit.Typing;
            _fly.Tick(kb, mouse, typing);

            if (_flashUntil > 0f && Time.unscaledTime > _flashUntil) { _flashUntil = 0f; Hint(); }
            if (Previewing) return;

            if (mouse.leftButton.wasPressedThisFrame && !OverUI()) Press(mouse.position.ReadValue());
            if (_dragging)
            {
                if (mouse.leftButton.isPressed) Drag(mouse.position.ReadValue());
                else EndDrag();
            }

            if (typing) return;
            if (kb.escapeKey.wasPressedThisFrame && _tool != Tool.None)
            {
                _tool = Tool.None;
                BuildBiomesWindow();
                BuildObjectsWindow();
                Hint();
            }
            if (kb.deleteKey.wasPressedThisFrame) DeleteSelected();
            if (kb.rKey.wasPressedThisFrame && !kb.leftCtrlKey.isPressed && !kb.rightCtrlKey.isPressed) TurnSelected(15f);
            if (kb.leftBracketKey.wasPressedThisFrame) ScaleSelected(0.9f);
            if (kb.rightBracketKey.wasPressedThisFrame) ScaleSelected(1.1f);
        }

        void LateUpdate()
        {
            if (!Active || _cam == null) return;
            if (!Previewing && MapLibrary.CanSave(Editing) != _savable) BuildMapWindow(); // the first biome placed or the last one gone
            // box names ride over their boxes
            if (_labels != null)
                for (int i = 0; i < _views.Count && i < Editing.Biomes.Count; i++)
                {
                    var v = _views[i];
                    if (v.Label == null) continue;
                    var d = Editing.Biomes[i];
                    Vector3 sp = _cam.WorldToScreenPoint(d.Pos + Vector3.up * (d.Size.y * 0.5f + 1f));
                    bool show = sp.z > 0f;
                    if (v.Label.gameObject.activeSelf != show) v.Label.gameObject.SetActive(show);
                    if (!show) continue;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(_labels, sp, null, out var local);
                    v.Label.rectTransform.anchoredPosition = local;
                }
            // handles keep a size you can grab from any distance
            foreach (var h in _handles)
                if (h != null && h.gameObject.activeSelf)
                    h.localScale = Vector3.one * Mathf.Clamp(Vector3.Distance(_rig.position, h.position) * 0.025f, 0.6f, 8f);
            UpdateSelectBox();
        }

        // ------------------------------------------------------------- grid --
        const float GridSize = 600f;
        GameObject _grid;

        /// A blank map is sky and this grid: something to click on.
        void BuildGrid() => _grid = MakeGrid();

        /// The grid on the zero plane, no collider: a click meets the plane itself (the Photo Booth's stage too).
        internal static GameObject MakeGrid()
        {
            var grid = GameObject.CreatePrimitive(PrimitiveType.Quad);
            grid.name = "~CreatorGrid";
            Destroy(grid.GetComponent<Collider>());
            grid.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            grid.transform.position = Vector3.zero;
            grid.transform.localScale = new Vector3(GridSize, GridSize, 1f);
            var mat = new Material(MatterFX.Get(new Color(1f, 1f, 1f, 0.55f), MoteShade.Transparent));
            mat.mainTexture = GridTexture();
            mat.mainTextureScale = new Vector2(GridSize / 10f, GridSize / 10f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f); // seen from above and below
            grid.GetComponent<Renderer>().sharedMaterial = mat;
            return grid;
        }

        internal static Texture2D GridTexture()
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    px[y * N + x] = x < 2 || y < 2 ? new Color32(255, 255, 255, 200) : new Color32(255, 255, 255, 18);
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // ------------------------------------------------------ biome boxes --
        class BoxView { public GameObject Root; public Transform Fill; public LineRenderer Edges; public Text Label; }
        readonly List<BoxView> _views = new List<BoxView>();
        readonly Transform[] _handles = new Transform[5]; // four sides, then the lift on top
        const int LiftHandle = 4, PieceDrag = -2;
        RectTransform _labels;
        bool _stale;   // the boxes changed since the ground last grew

        void RebuildViews()
        {
            foreach (var v in _views)
            {
                if (v.Root != null) Destroy(v.Root);
                if (v.Label != null) UIKit.Retire(v.Label.rectTransform);
            }
            _views.Clear();
            if (Previewing) return;
            for (int i = 0; i < Editing.Biomes.Count; i++) _views.Add(MakeView(i));
            UpdateHandles();
        }

        BoxView MakeView(int i)
        {
            var d = Editing.Biomes[i];
            var root = new GameObject("~BiomeBox");
            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(fill.GetComponent<Collider>());
            fill.transform.SetParent(root.transform, false);
            var edges = root.AddComponent<LineRenderer>();
            edges.useWorldSpace = true;
            edges.positionCount = 16;
            edges.numCornerVertices = 2;
            edges.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            edges.receiveShadows = false;
            var v = new BoxView { Root = root, Fill = fill.transform, Edges = edges };
            if (_labels != null)
            {
                v.Label = UIKit.Label(_labels, d.Name, 15, UIKit.Parchment, TextAnchor.MiddleCenter, true);
                var rt = v.Label.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(260f, 24f);
                v.Label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            }
            PlaceView(v, d, _sel == Sel.Biome && _selIndex == i);
            return v;
        }

        void PlaceView(BoxView v, BiomeDef d, bool selected)
        {
            v.Fill.position = d.Pos;
            v.Fill.localScale = d.Size;
            Color c = d.Color;
            v.Fill.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(c.r, c.g, c.b, selected ? 0.22f : 0.12f), MoteShade.Transparent);
            v.Edges.SetPositions(BoxEdges(d.Pos, d.Size));
            v.Edges.widthMultiplier = selected ? 0.45f : 0.2f;
            v.Edges.sharedMaterial = MatterFX.Get(selected ? new Color(1f, 0.82f, 0.25f) : new Color(c.r, c.g, c.b), MoteShade.Opaque);
            if (v.Label != null) v.Label.text = d.Name;
        }

        /// A box's twelve edges as one line strip.
        internal static Vector3[] BoxEdges(Vector3 center, Vector3 size)
        {
            Vector3 h = size * 0.5f, p = center;
            Vector3 b0 = p + new Vector3(-h.x, -h.y, -h.z), b1 = p + new Vector3(h.x, -h.y, -h.z);
            Vector3 b2 = p + new Vector3(h.x, -h.y, h.z), b3 = p + new Vector3(-h.x, -h.y, h.z);
            Vector3 up = Vector3.up * size.y;
            Vector3 t0 = b0 + up, t1 = b1 + up, t2 = b2 + up, t3 = b3 + up;
            return new[] { b0, b1, b2, b3, b0, t0, t1, b1, t1, t2, b2, t2, t3, b3, t3, t0 };
        }

        void UpdateHandles()
        {
            bool on = _sel == Sel.Biome && _selIndex >= 0 && _selIndex < Editing.Biomes.Count && !Previewing;
            for (int i = 0; i < _handles.Length; i++)
            {
                if (_handles[i] == null)
                {
                    if (!on) continue;
                    var s = GameObject.CreatePrimitive(i == LiftHandle ? PrimitiveType.Capsule : PrimitiveType.Sphere);
                    s.name = "~BoxHandle";
                    Destroy(s.GetComponent<Collider>());
                    s.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(new Color(1f, 0.82f, 0.25f), MoteShade.Opaque);
                    _handles[i] = s.transform;
                }
                _handles[i].gameObject.SetActive(on);
            }
            if (!on) return;
            var d = Editing.Biomes[_selIndex];
            Vector3 top = d.Pos + Vector3.up * d.Size.y * 0.5f;
            _handles[0].position = top + Vector3.right * d.Size.x * 0.5f;
            _handles[1].position = top - Vector3.right * d.Size.x * 0.5f;
            _handles[2].position = top + Vector3.forward * d.Size.z * 0.5f;
            _handles[3].position = top - Vector3.forward * d.Size.z * 0.5f;
            _handles[LiftHandle].position = top;
        }

        int HandleUnder(Vector2 screen)
        {
            for (int i = 0; i < _handles.Length; i++)
            {
                var h = _handles[i];
                if (h == null || !h.gameObject.activeSelf) continue;
                Vector3 sp = _cam.WorldToScreenPoint(h.position);
                if (sp.z > 0f && (new Vector2(sp.x, sp.y) - screen).sqrMagnitude < 24f * 24f) return i;
            }
            return -1;
        }

        /// The smallest box under the pointer, so boxes inside boxes can be picked.
        int BiomeUnder(Ray ray)
        {
            int best = -1;
            float bestVolume = float.MaxValue;
            for (int i = 0; i < Editing.Biomes.Count; i++)
            {
                var d = Editing.Biomes[i];
                if (!new Bounds(d.Pos, d.Size).IntersectRay(ray)) continue;
                float vol = d.Size.x * d.Size.y * d.Size.z;
                if (vol < bestVolume) { bestVolume = vol; best = i; }
            }
            return best;
        }

        static Vector3 PlanePoint(Ray ray, float y)
        {
            var plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
            return plane.Raycast(ray, out float t) ? ray.GetPoint(t) : ray.origin;
        }

        static readonly RaycastHit[] _hits = new RaycastHit[64];

        /// Where the pointer meets the world: the grown ground and what stands
        /// on it, else the grid. `ignore` is looked past (a piece being carried).
        static bool GroundUnder(Ray ray, Transform ignore, out Vector3 at) =>
            GroundUnder(ray, ignore, out at, Physics.DefaultRaycastLayers);

        /// The first thing on these layers the ray meets, else the zero plane.
        internal static bool GroundUnder(Ray ray, Transform ignore, out Vector3 at, int mask)
        {
            int n = Physics.RaycastNonAlloc(ray, _hits, 5000f, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            at = default;
            for (int i = 0; i < n; i++)
            {
                if (ignore != null && _hits[i].collider.transform.IsChildOf(ignore)) continue;
                if (_hits[i].distance < best) { best = _hits[i].distance; at = _hits[i].point; }
            }
            if (best < float.MaxValue) return true;
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out float t)) { at = ray.GetPoint(t); return true; }
            return false;
        }

        /// The placed piece the pointer is on, if the first thing it meets is one.
        static MapPiece PieceUnder(Ray ray)
        {
            if (!Physics.Raycast(ray, out var hit, 5000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return null;
            return hit.collider.GetComponentInParent<MapPiece>();
        }

        /// The point on the upright line through `at` nearest the pointer's ray.
        internal static Vector3 LinePoint(Ray ray, Vector3 at)
        {
            Vector3 w = at - ray.origin;
            float b = Vector3.Dot(Vector3.up, ray.direction);
            float denom = 1f - b * b;
            if (denom < 1e-3f) return at; // looking straight along the line
            float t = (b * Vector3.Dot(ray.direction, w) - Vector3.Dot(Vector3.up, w)) / denom;
            return at + Vector3.up * t;
        }

        static float Snap(float v) => Mathf.Round(v * 2f) / 2f;

        // ------------------------------------------------------------ tools --
        enum Tool { None, PlaceBiome, PlaceObject }
        enum Sel { None, Biome, Spawn, Prop }
        Tool _tool;
        Sel _sel;
        int _selIndex = -1, _pickTemplate = -1, _pick = -1;
        readonly List<string> _bodies = new List<string>();
        readonly List<string> _pieces = new List<string>();

        bool _dragging, _dragMoved;
        int _dragHandle;   // -1 the box itself, 0-3 its sides, LiftHandle up and down, PieceDrag a placed piece
        Vector2 _pressScreen;
        Vector3 _dragStart, _dragPos0, _dragSize0;
        float _dragY;
        Transform _dragPiece;
        Vector3 _pieceGrab;   // from the pointer's ground point to the piece, kept while carrying it

        void Press(Vector2 screen)
        {
            var ray = _cam.ScreenPointToRay(screen);
            // the selected box's handles first
            if (_tool == Tool.None && _sel == Sel.Biome)
            {
                int hnd = HandleUnder(screen);
                if (hnd >= 0) { BeginDrag(hnd, screen); return; }
            }
            // a placed piece under the pointer is picked up, not placed on again
            var piece = _tool == Tool.PlaceBiome ? null : PieceUnder(ray);
            if (piece != null)
            {
                _tool = Tool.None;
                Select(piece.Kind == MapPiece.Spawn ? Sel.Spawn : Sel.Prop, piece.Index);
                BeginPieceDrag(piece.transform, ray, screen);
                return;
            }
            if (_tool == Tool.PlaceBiome)
            {
                if (GroundUnder(ray, null, out var at)) PlaceBiome(at);
                return;
            }
            if (_tool == Tool.PlaceObject)
            {
                if (GroundUnder(ray, null, out var at)) PlaceObject(at);
                return;
            }
            int b = BiomeUnder(ray);
            if (b >= 0)
            {
                if (_sel != Sel.Biome || _selIndex != b) Select(Sel.Biome, b);
                BeginDrag(-1, screen);
                return;
            }
            Select(Sel.None, -1);
        }

        void BeginDrag(int handle, Vector2 screen)
        {
            var d = Editing.Biomes[_selIndex];
            _dragging = true;
            _dragMoved = false;
            _dragHandle = handle;
            _pressScreen = screen;
            _dragPos0 = d.Pos;
            _dragSize0 = d.Size;
            _dragY = d.Pos.y + d.Size.y * 0.5f;
            var ray = _cam.ScreenPointToRay(screen);
            _dragStart = handle == LiftHandle ? LinePoint(ray, _handles[LiftHandle].position) : PlanePoint(ray, _dragY);
        }

        void BeginPieceDrag(Transform piece, Ray ray, Vector2 screen)
        {
            _dragging = true;
            _dragMoved = false;
            _dragHandle = PieceDrag;
            _pressScreen = screen;
            _dragPiece = piece;
            _pieceGrab = GroundUnder(ray, piece, out var at) ? piece.position - at : Vector3.zero;
            _pieceGrab.y = 0f;
        }

        void Drag(Vector2 screen)
        {
            if (!_dragMoved && (screen - _pressScreen).sqrMagnitude < 36f) return; // a click is not a drag
            if (_dragHandle == PieceDrag) { DragPiece(screen); return; }
            if (_selIndex < 0 || _selIndex >= Editing.Biomes.Count) { _dragging = false; return; }
            _dragMoved = true;
            var d = Editing.Biomes[_selIndex];
            if (_dragHandle == LiftHandle)
            {
                // up and down on the handle's upright line, the bottom inside the Bottom height range
                float dy = LinePoint(_cam.ScreenPointToRay(screen), _dragStart).y - _dragStart.y;
                float bottom = Mathf.Clamp(Snap(_dragPos0.y - _dragSize0.y * 0.5f + dy), -40f, 80f);
                d.Pos = new Vector3(_dragPos0.x, bottom + _dragSize0.y * 0.5f, _dragPos0.z);
                PlaceView(_views[_selIndex], d, true);
                UpdateHandles();
                if (!_stale) { _stale = true; Hint(); }
                return;
            }
            Vector3 delta = PlanePoint(_cam.ScreenPointToRay(screen), _dragY) - _dragStart;
            if (_dragHandle < 0)
            {
                d.Pos = new Vector3(Snap(_dragPos0.x + delta.x), _dragPos0.y, Snap(_dragPos0.z + delta.z));
            }
            else
            {
                float minX = _dragPos0.x - _dragSize0.x * 0.5f, maxX = _dragPos0.x + _dragSize0.x * 0.5f;
                float minZ = _dragPos0.z - _dragSize0.z * 0.5f, maxZ = _dragPos0.z + _dragSize0.z * 0.5f;
                switch (_dragHandle)
                {
                    case 0: maxX = Mathf.Max(minX + 1f, Snap(maxX + delta.x)); break;
                    case 1: minX = Mathf.Min(maxX - 1f, Snap(minX + delta.x)); break;
                    case 2: maxZ = Mathf.Max(minZ + 1f, Snap(maxZ + delta.z)); break;
                    default: minZ = Mathf.Min(maxZ - 1f, Snap(minZ + delta.z)); break;
                }
                d.Pos = new Vector3((minX + maxX) * 0.5f, _dragPos0.y, (minZ + maxZ) * 0.5f);
                d.Size = new Vector3(maxX - minX, _dragSize0.y, maxZ - minZ);
            }
            PlaceView(_views[_selIndex], d, true);
            UpdateHandles();
            if (!_stale) { _stale = true; Hint(); }
        }

        /// The piece follows the pointer over the ground and whatever stands on it, on the half-metre grid.
        void DragPiece(Vector2 screen)
        {
            if (_dragPiece == null) { _dragging = false; return; }
            _dragMoved = true;
            if (!GroundUnder(_cam.ScreenPointToRay(screen), _dragPiece, out var at)) return;
            var pos = new Vector3(Snap(at.x + _pieceGrab.x), at.y, Snap(at.z + _pieceGrab.z));
            _dragPiece.position = pos;
            if (_sel == Sel.Spawn && _selIndex >= 0 && _selIndex < Editing.Spawns.Count) Editing.Spawns[_selIndex].Pos = pos;
            else if (_sel == Sel.Prop && _selIndex >= 0 && _selIndex < Editing.Props.Count) Editing.Props[_selIndex].Pos = pos;
        }

        void EndDrag()
        {
            _dragging = false;
            if (_dragHandle == PieceDrag) { _dragPiece = null; return; }
            if (_dragMoved && _winBiome != null && _winBiome.Visible) BuildBiomeWindow();
        }

        // ------------------------------------------------------- selection box --
        LineRenderer _selBox;
        MapPiece _selPiece;

        /// The placed piece that is selected, found again after the pieces are stood anew.
        Transform SelectedPiece()
        {
            int kind = _sel == Sel.Spawn ? MapPiece.Spawn : _sel == Sel.Prop ? MapPiece.Prop : -1;
            if (kind < 0 || _selIndex < 0) return null;
            if (_selPiece != null && _selPiece.Kind == kind && _selPiece.Index == _selIndex
                && _selPiece.transform.root.name == MapDef.PiecesName)
                return _selPiece.transform;
            _selPiece = null;
            var root = GameObject.Find(MapDef.PiecesName);
            if (root == null) return null;
            foreach (var p in root.GetComponentsInChildren<MapPiece>())
                if (p.Kind == kind && p.Index == _selIndex) { _selPiece = p; break; }
            return _selPiece != null ? _selPiece.transform : null;
        }

        /// A gold box around the selected piece, riding along as it moves, turns and grows.
        void UpdateSelectBox()
        {
            var t = Previewing ? null : SelectedPiece();
            if (_selBox == null)
            {
                if (t == null) return;
                _selBox = new GameObject("~SelectBox").AddComponent<LineRenderer>();
                _selBox.useWorldSpace = true;
                _selBox.positionCount = 16;
                _selBox.numCornerVertices = 2;
                _selBox.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _selBox.receiveShadows = false;
                _selBox.sharedMaterial = MatterFX.Get(new Color(1f, 0.82f, 0.25f), MoteShade.Opaque);
            }
            bool on = t != null;
            if (_selBox.gameObject.activeSelf != on) _selBox.gameObject.SetActive(on);
            if (!on) return;
            var b = ShapeShift.FindObjectBounds(t);
            if (b.size.sqrMagnitude < 1e-4f) b = new Bounds(t.position + Vector3.up * 0.5f, Vector3.one);
            b.Expand(0.3f);
            _selBox.SetPositions(BoxEdges(b.center, b.size));
            _selBox.widthMultiplier = Mathf.Clamp(b.size.magnitude * 0.012f, 0.04f, 0.3f);
        }

        void PlaceBiome(Vector3 at)
        {
            var temps = MapPalette.Templates;
            if (_pickTemplate < 0 || _pickTemplate >= temps.Count) return;
            var d = temps[_pickTemplate].Clone();
            d.Pos = new Vector3(Snap(at.x), d.Pos.y, Snap(at.z)); // its own height band, here
            d.Name = UniqueName(d.Name);
            Editing.Biomes.Add(d);
            Editing.OwnBiomes = true;
            _tool = Tool.None;
            RebuildViews();
            Select(Sel.Biome, Editing.Biomes.Count - 1);
            _stale = true;
            BuildBiomesWindow();
            Hint();
        }

        string UniqueName(string name)
        {
            string stem = string.IsNullOrEmpty(name) ? "Biome" : name;
            string pick = stem;
            for (int n = 2; Editing.Biomes.Exists(b => b.Name == pick); n++) pick = stem + " " + n;
            return pick;
        }

        void PlaceObject(Vector3 at)
        {
            if (_pick < 0) return;
            if (_pick < _bodies.Count)
            {
                Editing.Spawns.Add(new MapDef.SpawnDef { Body = _bodies[_pick], Pos = at });
                DressPieces();
                Select(Sel.Spawn, Editing.Spawns.Count - 1);
            }
            else if (_pick - _bodies.Count < _pieces.Count)
            {
                Editing.Props.Add(new MapDef.PropDef { Prefab = _pieces[_pick - _bodies.Count], Pos = at });
                DressPieces();
                Select(Sel.Prop, Editing.Props.Count - 1);
            }
        }

        void Select(Sel kind, int index)
        {
            int was = _sel == Sel.Biome ? _selIndex : -1;
            _sel = index < 0 ? Sel.None : kind;
            _selIndex = index;
            if (was >= 0 && was < _views.Count && was < Editing.Biomes.Count) PlaceView(_views[was], Editing.Biomes[was], false);
            if (_sel == Sel.Biome && index < _views.Count) PlaceView(_views[index], Editing.Biomes[index], true);
            UpdateHandles();

            if (_winBiome != null)
            {
                if (_sel == Sel.Biome) { _winBiome.Show(); BuildBiomeWindow(); }
                else _winBiome.Hide();
            }
            if (_winPiece != null)
            {
                if (_sel == Sel.Spawn || _sel == Sel.Prop) { _winPiece.Show(); BuildPieceWindow(); }
                else _winPiece.Hide();
            }
            if (_winObjects != null && _winObjects.Visible) BuildObjectsWindow();
            if (_winBiomes != null && _winBiomes.Visible) BuildBiomesWindow();
            Hint();
        }

        void DeleteSelected()
        {
            if (_selIndex < 0) return;
            switch (_sel)
            {
                case Sel.Biome when _selIndex < Editing.Biomes.Count:
                    Editing.Biomes.RemoveAt(_selIndex);
                    Select(Sel.None, -1);
                    RebuildViews();
                    _stale = true;
                    if (_winBiomes != null && _winBiomes.Visible) BuildBiomesWindow();
                    break;
                case Sel.Spawn when _selIndex < Editing.Spawns.Count:
                    Editing.Spawns.RemoveAt(_selIndex);
                    Select(Sel.None, -1);
                    DressPieces();
                    break;
                case Sel.Prop when _selIndex < Editing.Props.Count:
                    Editing.Props.RemoveAt(_selIndex);
                    Select(Sel.None, -1);
                    DressPieces();
                    break;
            }
            Hint();
        }

        void TurnSelected(float degrees)
        {
            if (_sel != Sel.Prop || _selIndex < 0 || _selIndex >= Editing.Props.Count) return;
            Editing.Props[_selIndex].Yaw = Mathf.Repeat(Editing.Props[_selIndex].Yaw + degrees, 360f);
            DressPieces();
        }

        void ScaleSelected(float k)
        {
            if (_sel == Sel.Prop && _selIndex >= 0 && _selIndex < Editing.Props.Count)
                Editing.Props[_selIndex].Scale = Mathf.Clamp(Editing.Props[_selIndex].Scale * k, 0.1f, 10f);
            else if (_sel == Sel.Spawn && _selIndex >= 0 && _selIndex < Editing.Spawns.Count)
            {
                Editing.Spawns[_selIndex].Size = Mathf.Clamp(Editing.Spawns[_selIndex].Size * k, 0.2f, 6f);
                BuildPieceWindow(); // its size slider follows
            }
            else return;
            DressPieces();
        }

        /// The map's pieces stand again from the data; a creature marker also
        /// wears a capsule in its team's colour so it can be seen and clicked.
        void DressPieces()
        {
            var root = MapDef.ApplyActive();
            if (root == null) return;
            foreach (var m in root.GetComponentsInChildren<CreatureSpawn>())
            {
                var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                cap.name = "StandIn";
                cap.transform.SetParent(m.transform, false);
                cap.transform.localPosition = Vector3.up * Mathf.Max(0.5f, m.Size);
                cap.transform.localScale = Vector3.one * Mathf.Max(0.5f, m.Size);
                Color c = m.Team == Team.Wizard ? new Color(0.3f, 0.6f, 1f)
                    : m.Team == Team.Acolyte ? new Color(0.4f, 0.9f, 0.3f) : new Color(1f, 0.6f, 0.2f);
                cap.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(c, MoteShade.Opaque);
            }
        }

        // ------------------------------------------------------------ ground --
        /// ★ The ground grows from the boxes, the same pass a match runs.
        void Generate()
        {
            if (_map == null) _map = FindFirstObjectByType<SpellyMap>();
            if (_map == null) return;
            Editing.OwnBiomes = true;
            MapDef.StandDownAuthored();
            MapDef.BuildBiomes(Editing);
            if (Editing.Biomes.Count > 0) _map.Generate(Editing.Seed);
            else _map.ClearGenerated();
            DressPieces();
            _stale = false;
            if (_grid != null) _grid.SetActive(Editing.Biomes.Count == 0);
            Hint();
        }

        /// Starts from the base scene's own layout: every one of its biomes, as they stand.
        void StartFromBase()
        {
            Editing.Biomes.Clear();
            Editing.Biomes.AddRange(MapPalette.BaseBiomes());
            Select(Sel.None, -1);
            RebuildViews();
            Generate();
            BuildMapWindow();
            BuildBiomesWindow();
        }

        // --------------------------------------------------------------- UI --
        RectTransform _ui;
        Text _hint;
        string _flash;
        float _flashUntil;
        CreatorWindow _winMap, _winBiomes, _winBiome, _winObjects, _winPiece;
        readonly HashSet<string> _open = new HashSet<string>();

        void BuildUI()
        {
            if (_ui != null) UIKit.Retire(_ui);
            _screen = null; // it went with the old UI
            _areaScreen = null;
            _ui = UIKit.Group(UIKit.Root, "MapCreatorUI");
            UIKit.Stretch(_ui);
            _labels = UIKit.Group(_ui, "BoxLabels");
            UIKit.Stretch(_labels);

            BuildRibbon();
            if (Previewing) { Hint(); return; }

            float right = Mathf.Max(820f, _ui.rect.width - 420f);
            _winMap = CreatorWindow.Create(_ui, "WinMap", Loc.T("mc.map"), new Vector2(16f, -104f), 360f, 640f);
            _winBiomes = CreatorWindow.Create(_ui, "WinBiomes", Loc.T("mc.biomes"), new Vector2(392f, -104f), 340f, 620f);
            _winObjects = CreatorWindow.Create(_ui, "WinObjects", Loc.T("mc.objects"), new Vector2(392f, -104f), 360f, 620f);
            _winBiome = CreatorWindow.Create(_ui, "WinBiome", Loc.T("mc.biome"), new Vector2(right, -104f), 400f, 700f);
            _winPiece = CreatorWindow.Create(_ui, "WinPiece", Loc.T("mc.objects"), new Vector2(right, -104f), 400f, 700f);
            _winObjects.Hide();
            _winBiome.Hide();
            _winPiece.Hide();
            _winBiome.Closed = () => { if (_sel == Sel.Biome) Select(Sel.None, -1); };
            _winPiece.Closed = () => { if (_sel == Sel.Spawn || _sel == Sel.Prop) Select(Sel.None, -1); };
            BuildMapWindow();
            BuildBiomesWindow();
            Hint();
        }

        // ----------------------------------------------------- full screens --
        enum ScreenKind { None, Spells, Runes, Creatures }
        CreatorScreen _screen;
        ScreenKind _screenKind;

        /// ★ The Spells and Runes creators take the whole screen; the map waits behind.
        void OpenScreen(ScreenKind kind)
        {
            if (_ui == null || kind == ScreenKind.None) return;
            CloseAreaScreen();
            if (_screen != null) { _screen.Closed = null; _screen.Close(); }
            _screenKind = kind;
            _fly?.Release();
            _dragging = false;
            string back = Loc.T("mc.back.map");
            if (kind == ScreenKind.Spells)
            {
                _screen = CreatorScreen.Create(_ui, "SpellsScreen", Loc.T("mc.spells"), back, 240f, 500f, 0f, 0f);
                BuildSpellsScreen();
            }
            else if (kind == ScreenKind.Creatures)
            {
                _screen = CreatorScreen.Create(_ui, "CreaturesScreen", Loc.T("mc.creatures.open"), back, 240f, 500f, 0f, 0f);
                BuildCreaturesScreen();
            }
            else
            {
                _screen = CreatorScreen.Create(_ui, "RunesScreen", Loc.T("mc.runes"), back, 260f, 540f, 0f, 0f);
                BuildRunesScreen();
            }
            _screen.Closed = ScreenClosed;
        }

        void CloseAreaScreen()
        {
            if (_areaScreen == null) return;
            _areaScreen.Closed = null;
            _areaScreen.Close();
            _areaScreen = null;
            _areaPane = null;
            _areaNoLook = null;
            _areaName = null;
        }

        void ScreenClosed()
        {
            CloseAreaScreen();
            // creatures may have come or gone: a picked one could now point at another
            if (_screenKind == ScreenKind.Creatures && _tool == Tool.PlaceObject) { _tool = Tool.None; _pick = -1; }
            _screen = null;
            _screenKind = ScreenKind.None;
            _spellPane = null;
            _creaturePane = null;
            _creatureNote = null;
            _shownCreature = null;
            _shownSummoned = null;
            _sureCreature = false;
            _areaPane = null;
            _verdict = null;
            _pad = null;
            _padReads = null;
            _padNote = null;
            _page = null;
            _pageName = null;
            foreach (var t in _thumbs) if (t != null) Destroy(t);
            _thumbs.Clear();
            if (_winObjects != null && _winObjects.Visible) BuildObjectsWindow(); // bodies may have changed
            if (_winPiece != null && _winPiece.Visible) BuildPieceWindow();     // and the picked one's name
            Hint();
        }

        CreatorWindow[] AllWindows() => new[] { _winMap, _winBiomes, _winObjects, _winBiome, _winPiece };

        /// A language change mid-edit builds every word again; each window keeps
        /// its place, its stacking and whether it was open.
        void Relabel()
        {
            if (!Active || _ui == null) return;
            int uiOrder = _ui.GetSiblingIndex();
            var reopen = _screenKind;
            var was = AllWindows();
            var kept = new (Vector2 at, bool open, int order)[was.Length];
            for (int i = 0; i < was.Length; i++)
                if (was[i] != null)
                    kept[i] = (((RectTransform)was[i].transform).anchoredPosition, was[i].Visible, was[i].transform.GetSiblingIndex());

            BuildUI();
            _ui.SetSiblingIndex(uiOrder); // an open pause menu stays on top
            RebuildViews();

            var now = AllWindows();
            var order = new List<int>();
            for (int i = 0; i < now.Length; i++)
            {
                if (now[i] == null || was[i] == null) continue;
                ((RectTransform)now[i].transform).anchoredPosition = kept[i].at;
                now[i].gameObject.SetActive(kept[i].open);
                order.Add(i);
            }
            order.Sort((a, b) => kept[a].order.CompareTo(kept[b].order));
            foreach (int i in order) now[i].transform.SetAsLastSibling();

            if (_winObjects != null && _winObjects.Visible) BuildObjectsWindow();
            BuildBiomeWindow();
            BuildPieceWindow();
            _screenKind = ScreenKind.None;
            if (reopen != ScreenKind.None) OpenScreen(reopen);
            Hint();
        }

        void BuildRibbon()
        {
            var skin = UISkin.I;
            var back = UIKit.Panel(_ui, skin != null ? skin.PanelBrown : null,
                skin != null ? (Color?)null : new Color(0f, 0f, 0f, 0.6f));
            back.raycastTarget = true;
            back.name = "Ribbon";
            var rt = back.rectTransform;
            UIKit.Place(rt, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(Previewing ? 620f : 740f, 58f));
            var row = UIKit.Group(rt, "RibbonRow");
            UIKit.Stretch(row);
            row.offsetMin = new Vector2(12f, 10f);
            row.offsetMax = new Vector2(-12f, -10f);
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 6f;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = hl.childForceExpandHeight = true;

            if (Previewing)
            {
                UIKit.Label(row, Editing.Name, 18, UIKit.Ink, TextAnchor.MiddleCenter, true);
                if (_previewItem != null && !MapLibrary.Exists(Editing.Name))
                    UIKit.Button(row, Loc.T("maps.keep"), KeepPreview, CreatorUI.Pick(true), 15);
                UIKit.Button(row, Loc.T("maps.back"), Exit, CreatorUI.Red, 15);
            }
            else
            {
                void Tab(string key, System.Func<CreatorWindow> win, System.Action build) =>
                    UIKit.Button(row, Loc.T(key), () =>
                    {
                        var w = win();
                        w.Toggle();
                        if (w.Visible) build();
                    }, CreatorUI.Pick(false), 15);
                Tab("mc.map", () => _winMap, BuildMapWindow);
                Tab("mc.biomes", () => _winBiomes, BuildBiomesWindow);
                Tab("mc.objects", () => _winObjects, BuildObjectsWindow);
                UIKit.Button(row, Loc.T("mc.spells"), () => OpenScreen(ScreenKind.Spells), CreatorUI.Pick(false), 15);
                UIKit.Button(row, Loc.T("mc.runes"), () => OpenScreen(ScreenKind.Runes), CreatorUI.Pick(false), 15);

                // leaving on the left, growing the ground on the right: neither is a window
                Corner(new Vector2(0f, 1f), new Vector2(8f, -8f), Loc.T("mc.back.menu"), Exit, CreatorUI.Red);
                Corner(new Vector2(1f, 1f), new Vector2(-8f, -8f), Loc.T("mc.generate"), Generate, CreatorUI.Pick(true));
            }

            _hint = UIKit.Label(_ui, "", 15, UIKit.Parchment, TextAnchor.MiddleCenter, true);
            UIKit.Place(_hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(1000f, 26f));
            _hint.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
        }

        /// One big button in a panel of its own, pinned to a top corner.
        void Corner(Vector2 corner, Vector2 at, string label, System.Action act, Sprite sprite)
        {
            var skin = UISkin.I;
            var back = UIKit.Panel(_ui, skin != null ? skin.PanelBrown : null,
                skin != null ? (Color?)null : new Color(0f, 0f, 0f, 0.6f));
            back.raycastTarget = true;
            back.name = "Corner";
            var rt = back.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = corner;
            rt.anchoredPosition = at;
            rt.sizeDelta = new Vector2(250f, 58f);
            var b = UIKit.Button(rt, label, act, sprite, 16);
            var brt = (RectTransform)b.transform;
            UIKit.Stretch(brt);
            brt.offsetMin = new Vector2(12f, 10f);
            brt.offsetMax = new Vector2(-12f, -10f);
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
            string s;
            if (_flashUntil > 0f) s = _flash;
            else if (Previewing) s = Loc.F("mc.hint.preview", HelpKeys());
            else if (_tool == Tool.PlaceBiome && _pickTemplate >= 0 && _pickTemplate < MapPalette.Templates.Count)
                s = Loc.F("mc.hint.place", MapPalette.Templates[_pickTemplate].Name);
            else if (_tool == Tool.PlaceObject) s = Loc.F("mc.hint.placemany", PickName());
            else if (_sel == Sel.Biome && _selIndex < Editing.Biomes.Count) s = Loc.F("mc.hint.biome", Editing.Biomes[_selIndex].Name);
            else if (_sel == Sel.Spawn || _sel == Sel.Prop)
            {
                var kb = Keyboard.current;
                s = Loc.F("mc.hint.object", KeyName(kb?.rKey, "R"), KeyName(kb?.leftBracketKey, "["), KeyName(kb?.rightBracketKey, "]"));
            }
            else s = Loc.T("mc.hint.idle");
            if (_stale && !Previewing) s += "   " + Loc.T("mc.hint.stale");
            _hint.text = s;
        }

        static string HelpKeys()
        {
            var kb = Keyboard.current;
            return kb == null ? "WASD"
                : KeyName(kb.wKey, "W") + KeyName(kb.aKey, "A") + KeyName(kb.sKey, "S") + KeyName(kb.dKey, "D");
        }

        string PickName()
        {
            if (_pick < 0) return "";
            if (_pick < _bodies.Count) return Loc.F("creator.body", _bodies[_pick]);
            int i = _pick - _bodies.Count;
            return i < _pieces.Count ? _pieces[i] : "";
        }

        static float Keep(CreatorWindow w) => w.Body.anchoredPosition.y;
        static void Restore(CreatorWindow w, float y) => w.Body.anchoredPosition = new Vector2(w.Body.anchoredPosition.x, y);

        // --------------------------------------------------------- map window --
        void BuildMapWindow()
        {
            var w = _winMap;
            if (w == null) return;
            _savable = MapLibrary.CanSave(Editing);
            float keep = Keep(w);
            w.Clear();
            var b = w.Body;
            float W = w.Width;
            CreatorUI.Note(b, W, HelpText(), 60f);
            CreatorUI.Field(b, W, Loc.T("mc.name"), Editing.Name, v => Editing.Name = v);

            CreatorUI.Head(b, W, Loc.T("mc.groundhead"));
            var row = CreatorUI.Row(b, W);
            UIKit.Button(row, Loc.T("mc.generate"), Generate, CreatorUI.Pick(true), 14);
            UIKit.Button(row, Loc.T("mc.newseed"), () =>
            {
                Editing.Seed = Random.Range(1, int.MaxValue);
                MatchLobby.Seed = Editing.Seed;
                Generate();
            }, CreatorUI.Pick(false), 14);
            if (Editing.Biomes.Count == 0)
                CreatorUI.Do(b, W, Loc.T("mc.frombase"), StartFromBase, null, 30f);

            CreatorUI.Number(b, W, "", 0f, 15f, Editing.TimerMinutes, true, v => Editing.TimerMinutes = Mathf.RoundToInt(v),
                v => v <= 0f ? Loc.T("creator.noclock") : Loc.F("creator.timer", Mathf.RoundToInt(v)));

            // ★ THE TEAMS (his WC3 model): who plays together, and which teams count for victory
            if (Section(b, W, "map:teams", Loc.T("mc.teams"), false, BuildMapWindow))
            {
                bool together = Editing.Teams.Exists(t => t.Who == TeamWho.Everyone);
                CreatorUI.Switch(b, W, Loc.T("mc.teams.players"),
                    new[] { Loc.T("mc.teams.split"), Loc.T("mc.teams.together") }, together ? 1 : 0, i =>
                    {
                        Editing.Teams = i == 1 ? MapRules.TogetherTeams() : MapRules.SplitTeams();
                        BuildMapWindow();
                    });
                var teams = together ? new[] { TeamWho.Everyone, TeamWho.Environment }
                    : new[] { TeamWho.Wizards, TeamWho.Acolytes, TeamWho.Environment };
                foreach (var who in teams)
                {
                    var team = who;
                    CreatorUI.Switch(b, W, CountsLabel(team), new[] { Loc.T("opt.off"), Loc.T("opt.on") },
                        MapRules.Counts(Editing, team) ? 1 : 0, i =>
                        {
                            if (Editing.Teams.Count == 0) Editing.Teams = MapRules.SplitTeams();
                            var t = Editing.Teams.Find(x => x.Who == team);
                            if (t == null) Editing.Teams.Add(t = new TeamDef { Who = team });
                            t.Counts = i == 1;
                            BuildMapWindow();
                        });
                }
                CreatorUI.Note(b, W, Loc.T("mc.note.teams"), 62f);
                // an environment boss is one placed on the environment's team
                if (MapRules.Counts(Editing, TeamWho.Environment) && !Editing.Spawns.Exists(s =>
                    { var c = SpellBook.Live.Creature(s.Body); return s.Team == 0 && c != null && c.Boss; }))
                    CreatorUI.Note(b, W, Loc.T("mc.note.noboss"), 34f);
            }

            CreatorUI.Head(b, W, Loc.T("creator.load"));
            CreatorUI.Note(b, W, Loc.T("mc.picture"), 30f);
            var files = CreatorUI.Row(b, W);
            UIKit.Button(files, Loc.T("creator.save"), Save, CreatorUI.Pick(_savable), 14).interactable = _savable;
            UIKit.Button(files, Loc.T("creator.new"), () => Open(null), CreatorUI.Pick(false), 14);
            if (!_savable) CreatorUI.Note(b, W, Loc.T("mc.needbiome"), 30f);
            var maps = MapLibrary.All();
            var names = new List<string>();
            foreach (var m in maps) names.Add(m.Name);
            CreatorUI.Grid(b, W, 1, names, i => maps[i].Name == Editing.Name, i => Open(maps[i]), 28f, 13);
            Restore(w, keep);
        }

        static string CountsLabel(TeamWho who)
        {
            switch (who)
            {
                case TeamWho.Everyone: return Loc.T("mc.counts.everyone");
                case TeamWho.Wizards: return Loc.T("mc.counts.wizards");
                case TeamWho.Acolytes: return Loc.T("mc.counts.acolytes");
                default: return Loc.T("mc.counts.environment");
            }
        }

        void Save()
        {
            if (!MapLibrary.CanSave(Editing))
            {
                Flash(Loc.T("mc.needbiome"));
                Juice.Sound2D(Sfx.UiError);
                BuildMapWindow();
                return;
            }
            if (MapLibrary.Locked(Editing.Name)) { Flash(Loc.T("mc.official")); return; } // the game's own: a new name keeps a copy
            Editing.BookJson = SpellBook.LiveJson();
            Editing.OwnBiomes = Editing.OwnBiomes || Editing.Biomes.Count > 0;
            if (_savedAs != null && _savedAs != Editing.Name) MapLibrary.Delete(_savedAs);
            MapLibrary.Save(Editing, Snapshot());
            MapPages.Save(Editing.Name); // the pages painted for its runes
            _savedAs = Editing.Name;
            BuildMapWindow();
            Flash(Loc.F("creator.saved", Editing.Name));
        }

        void KeepPreview()
        {
            if (_previewItem != null) Flash(MapWorkshop.Keep(_previewItem) ? Loc.F("creator.saved", Editing.Name) : Loc.T("mc.needbiome"));
            BuildUI();
        }

        /// What the camera sees, without the creator's boxes and grid: the map's picture.
        byte[] Snapshot()
        {
            if (_cam == null) return null;
            const int W = 640, H = 360;
            var hidden = new List<GameObject>();
            foreach (var v in _views) if (v.Root != null && v.Root.activeSelf) { v.Root.SetActive(false); hidden.Add(v.Root); }
            foreach (var h in _handles) if (h != null && h.gameObject.activeSelf) { h.gameObject.SetActive(false); hidden.Add(h.gameObject); }
            if (_selBox != null && _selBox.gameObject.activeSelf) { _selBox.gameObject.SetActive(false); hidden.Add(_selBox.gameObject); }
            if (_grid != null && _grid.activeSelf) { _grid.SetActive(false); hidden.Add(_grid); }
            var rt = RenderTexture.GetTemporary(W, H, 24);
            var before = _cam.targetTexture;
            _cam.targetTexture = rt;
            _cam.Render();
            _cam.targetTexture = before;
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply(false);
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            foreach (var g in hidden) g.SetActive(true);
            byte[] png = tex.EncodeToPNG();
            Destroy(tex);
            return png;
        }

        void Exit()
        {
            if (_ui != null) { UIKit.Retire(_ui); _ui = null; }
            LoadEgg.Travel("Menu"); // Close runs when the menu has loaded
        }

        // ------------------------------------------------------ biomes window --
        void BuildBiomesWindow()
        {
            var w = _winBiomes;
            if (w == null) return;
            float keep = Keep(w);
            w.Clear();
            var b = w.Body;
            float W = w.Width;
            CreatorUI.Note(b, W, Loc.T("mc.biomes.note"), 30f);
            var temps = MapPalette.Templates;
            var names = new List<string>();
            foreach (var t in temps) names.Add(t.Name);
            CreatorUI.Grid(b, W, 2, names, i => _tool == Tool.PlaceBiome && _pickTemplate == i, i =>
            {
                bool again = _tool == Tool.PlaceBiome && _pickTemplate == i;
                _tool = again ? Tool.None : Tool.PlaceBiome;
                _pickTemplate = again ? -1 : i;
                BuildBiomesWindow();
                if (_winObjects != null && _winObjects.Visible) BuildObjectsWindow();
                Hint();
            }, 30f, 12);

            CreatorUI.Head(b, W, Loc.F("mc.onmap", Editing.Biomes.Count));
            var mine = new List<string>();
            foreach (var d in Editing.Biomes) mine.Add(d.Name);
            CreatorUI.Grid(b, W, 1, mine, i => _sel == Sel.Biome && _selIndex == i, i => Select(Sel.Biome, i), 28f, 13);
            Restore(w, keep);
        }

        // ------------------------------------------------------- biome window --
        void BuildBiomeWindow()
        {
            var w = _winBiome;
            if (w == null) return;
            if (_sel != Sel.Biome || _selIndex < 0 || _selIndex >= Editing.Biomes.Count) { w.Hide(); return; }
            float keep = Keep(w);
            w.Clear();
            var d = Editing.Biomes[_selIndex];
            var b = w.Body;
            float W = w.Width;
            w.Title.text = d.Name;

            CreatorUI.Field(b, W, Loc.T("mc.name"), d.Name, v =>
            {
                d.Name = v;
                w.Title.text = v;
                if (_selIndex < _views.Count && _views[_selIndex].Label != null) _views[_selIndex].Label.text = v;
            });
            CreatorUI.Switch(b, W, null, new[] { Loc.T("mc.kind.ground"), Loc.T("mc.kind.liquid") }, d.Liquid ? 1 : 0,
                i => { d.Liquid = i == 1; Changed(false); BuildBiomeWindow(); });

            // sections open one at a time as needed; the box shows first
            if (Section(b, W, "bio:box", Loc.T("mc.shape"), true, BuildBiomeWindow))
            {
                CreatorUI.Number(b, W, Loc.T("mc.width"), 1f, 400f, d.Size.x, true, v => { d.Size.x = v; Changed(true); });
                CreatorUI.Number(b, W, Loc.T("mc.depth"), 1f, 400f, d.Size.z, true, v => { d.Size.z = v; Changed(true); });
                CreatorUI.Number(b, W, Loc.T("mc.height"), 1f, 80f, d.Size.y, true, v =>
                {
                    float bottom = d.Pos.y - d.Size.y * 0.5f;
                    d.Size.y = v;
                    d.Pos.y = bottom + v * 0.5f;
                    Changed(true);
                });
                CreatorUI.Number(b, W, Loc.T("mc.bottom"), -40f, 80f, d.Pos.y - d.Size.y * 0.5f, true, v =>
                {
                    d.Pos.y = v + d.Size.y * 0.5f;
                    Changed(true);
                });
                CreatorUI.Number(b, W, Loc.T("mc.layer"), 0f, 30f, d.Layer, true, v => { d.Layer = Mathf.RoundToInt(v); Changed(false); });
                CreatorUI.Number(b, W, Loc.T("mc.core"), 0f, 1f, d.ProtectedCore, false, v => { d.ProtectedCore = v; Changed(false); });
            }

            if (Section(b, W, "bio:ground", Loc.T("mc.groundhead"), false, BuildBiomeWindow))
            {
                CreatorUI.Number(b, W, Loc.T("mc.rough"), 0f, 8f, d.FloorNoise, false, v => { d.FloorNoise = v; Changed(false); });
                CreatorUI.Number(b, W, Loc.T("mc.hills"), 1f, 60f, d.NoiseScale, true, v => { d.NoiseScale = v; Changed(false); });
                OnOff(b, W, Loc.T("mc.paths"), d.CanPath, on => d.CanPath = on);
                CreatorUI.Number(b, W, Loc.T("mc.pathbend"), 0f, 1f, d.PathCurve, false, v => { d.PathCurve = v; Changed(false); });
                Choice(b, W, "brush", Loc.T("mc.brush"), MapPalette.LayerNames, d.FloorLayer, false, v => d.FloorLayer = v);
                Choice(b, W, "pathbrush", Loc.T("mc.pathbrush"), MapPalette.LayerNames, d.PathLayer, true, v => d.PathLayer = v);
            }

            if (Section(b, W, "bio:fill", Loc.T("mc.fill"), false, BuildBiomeWindow))
            {
                CreatorUI.Number(b, W, Loc.T("mc.spacing"), 0.5f, 10f, d.FieldSize, false, v => { d.FieldSize = v; Changed(false); });
                CreatorUI.Number(b, W, Loc.T("mc.slope"), 0f, 89f, d.MaxSlope, true, v => { d.MaxSlope = v; Changed(false); });
                Members(b, W, "mc.props", MapPalette.PropNames, d.Props);
                Members(b, W, "mc.sources", MapPalette.SourceNames, d.Sources);
                CreatorUI.Number(b, W, Loc.T("mc.minsources"), 0f, 20f, d.MinSources, true, v => { d.MinSources = Mathf.RoundToInt(v); Changed(false); });
                CreatorUI.Number(b, W, Loc.T("mc.maxsources"), 0f, 20f, d.MaxSources, true, v => { d.MaxSources = Mathf.RoundToInt(v); Changed(false); });
                Choice(b, W, "walllight", Loc.T("mc.walllight"), MapPalette.WallLightNames, d.WallLight, true, v => d.WallLight = v);
                Choice(b, W, "cauldron", Loc.T("mc.cauldron"), MapPalette.CauldronNames, d.Cauldron, true, v => d.Cauldron = v);
                Choice(b, W, "landmark", Loc.T("mc.landmark"), MapPalette.LandmarkNames, d.Landmark, true, v => d.Landmark = v);
            }

            // the same ten axes, names and units as a spell
            if (Section(b, W, "bio:imposed", Loc.T("mc.imposed"), false, BuildBiomeWindow))
            {
                AxisNumber(b, W, 0, -200f, 300f, d.HeatOffset, v => d.HeatOffset = v);
                AxisNumber(b, W, 1, -100f, 100f, d.LightOffset, v => d.LightOffset = v);
                AxisNumber(b, W, 2, -100f, 100f, d.DensityOffset, v => d.DensityOffset = v);
                AxisNumber(b, W, 3, -100f, 100f, d.StickOffset, v => d.StickOffset = v);
                AxisNumber(b, W, 4, -150f, 150f, d.StateOffset, v => d.StateOffset = v);
                AxisNumber(b, W, 5, -100f, 100f, d.AffinityOffset, v => d.AffinityOffset = v);
            }

            if (Section(b, W, "bio:allowed", Loc.T("mc.allowed"), false, BuildBiomeWindow))
            {
                AxisNumber(b, W, 6, 0f, 500f, d.StrengthCap, v => d.StrengthCap = v);
                AxisNumber(b, W, 7, 0f, 100f, d.IntCap, v => d.IntCap = v);
                AxisNumber(b, W, 8, 0f, 100f, d.CourageCap, v => d.CourageCap = v);
                AxisNumber(b, W, 9, 0f, 3f, d.ClonesCap, v => d.ClonesCap = v);
                CreatorUI.Number(b, W, Loc.T("mc.healing"), 0f, 3f, d.RegenScale, false, v => { d.RegenScale = v; Changed(false); });
            }

            if (Section(b, W, "bio:spawn", Loc.T("mc.spawn"), false, BuildBiomeWindow))
            {
                OnOff(b, W, Loc.T("mc.wizardhome"), d.WizardSpawn, on => d.WizardSpawn = on);
                CreatorUI.Number(b, W, Loc.T("mc.spread"), 0.05f, 1f, d.WizardSpawnSpread, false, v => { d.WizardSpawnSpread = v; Changed(false); });
            }

            if (Section(b, W, "bio:byobjects", Loc.T("mc.byobjects"), false, BuildBiomeWindow))
            {
                OnOff(b, W, null, d.ByObjects, on => d.ByObjects = on);
                CreatorUI.Number(b, W, Loc.T("mc.full"), 0f, 20f, d.ObjectFull, false, v => { d.ObjectFull = v; Changed(false); });
                CreatorUI.Number(b, W, Loc.T("mc.reach"), 0f, 40f, d.ObjectReach, false, v => { d.ObjectReach = v; Changed(false); });
            }

            if (d.Liquid && Section(b, W, "bio:liquid", Loc.T("mc.kind.liquid"), true, BuildBiomeWindow))
            {
                CreatorUI.Number(b, W, Loc.T("mc.buoyancy"), 0f, 1f, d.Buoyancy, false, v => { d.Buoyancy = v; Changed(false); });
                Choice(b, W, "surface", Loc.T("mc.surface"), MapPalette.SurfaceNames, d.Surface, true, v => d.Surface = v);
            }

            CreatorUI.Head(b, W, "");
            var row = CreatorUI.Row(b, W, 34f);
            UIKit.Button(row, Loc.T("mc.duplicate"), () =>
            {
                var c = d.Clone();
                c.Name = UniqueName(d.Name);
                c.Pos += new Vector3(d.Size.x + 4f, 0f, 0f);
                Editing.Biomes.Add(c);
                RebuildViews();
                Select(Sel.Biome, Editing.Biomes.Count - 1);
                Changed(false);
            }, CreatorUI.Pick(false), 14);
            UIKit.Button(row, Loc.T("creator.delete"), DeleteSelected, CreatorUI.Red, 14);
            Restore(w, keep);
        }

        /// A heading that opens and closes what is under it. `start` is how it first
        /// shows; the key remembers a click away from that.
        bool Section(RectTransform b, float W, string key, string title, bool start, System.Action rebuild) =>
            CreatorUI.Section(b, W, _open, key, title, start, rebuild);

        void Changed(bool shape)
        {
            if (shape && _selIndex >= 0 && _selIndex < _views.Count && _selIndex < Editing.Biomes.Count)
            {
                PlaceView(_views[_selIndex], Editing.Biomes[_selIndex], true);
                UpdateHandles();
            }
            if (!_stale) { _stale = true; Hint(); }
        }

        void AxisNumber(RectTransform b, float W, int axis, float min, float max, float value, System.Action<int> set) =>
            CreatorUI.Number(b, W, Loc.T("axis." + axis), min, max, value, true, v => { set(Mathf.RoundToInt(v)); Changed(false); },
                v => Loc.T("axis." + axis) + ": " + Mathf.RoundToInt(v) + SpellPayload.UnitName(axis));

        void OnOff(RectTransform b, float W, string name, bool on, System.Action<bool> set) =>
            CreatorUI.Switch(b, W, name, new[] { Loc.T("opt.off"), Loc.T("opt.on") }, on ? 1 : 0, i =>
            {
                set(i == 1);
                Changed(false);
                BuildBiomeWindow();
            });

        /// One pick from a list (the lit one is current); folded until opened.
        void Choice(RectTransform b, float W, string key, string name, IReadOnlyList<string> options, string current,
            bool allowNone, System.Action<string> set)
        {
            var labels = new List<string>();
            if (allowNone) labels.Add(Loc.T("mc.none"));
            labels.AddRange(options);
            int offset = allowNone ? 1 : 0;
            Dropdown(b, W, key, name, string.IsNullOrEmpty(current) ? Loc.T("mc.none") : current, labels,
                i => i < offset ? string.IsNullOrEmpty(current) : options[i - offset] == current,
                i => { set(i < offset ? "" : options[i - offset]); Changed(false); },
                BuildBiomeWindow);
        }

        /// A DROP DOWN: its name and current pick on one button, every choice
        /// under it only while it is open. A pick closes it, unless it stays open.
        void Dropdown(RectTransform b, float W, string key, string name, string current, IReadOnlyList<string> options,
            System.Func<int, bool> lit, System.Action<int> picked, System.Action rebuild, bool stayOpen = false) =>
            CreatorUI.Dropdown(b, W, _open, key, name, current, options, lit, picked, rebuild, stayOpen);

        /// A set picked from a pool (lit = in the set); folded until opened.
        void Members(RectTransform b, float W, string key, IReadOnlyList<string> pool, List<string> members)
        {
            bool open = _open.Contains(key);
            CreatorUI.Do(b, W, Loc.F(key, members.Count) + (open ? "  -" : "  +"), () =>
            {
                if (!_open.Remove(key)) _open.Add(key);
                BuildBiomeWindow();
            }, null, 28f);
            if (!open) return;
            CreatorUI.Grid(b, W, 2, pool, i => members.Contains(pool[i]), i =>
            {
                if (!members.Remove(pool[i])) members.Add(pool[i]);
                Changed(false);
                BuildBiomeWindow();
            }, 24f, 11);
        }

        // ----------------------------------------------------- objects window --
        void BuildObjectsWindow()
        {
            var w = _winObjects;
            if (w == null) return;
            float keep = Keep(w);
            w.Clear();
            var b = w.Body;
            float W = w.Width;
            CreatorUI.Note(b, W, Loc.T("creator.pickone"), 22f);
            _bodies.Clear();
            foreach (var c in SpellBook.Live.creatures) _bodies.Add(c.Name);
            _pieces.Clear();
            _pieces.AddRange(MapPalette.Names);

            // the palette in groups that open one by one: creatures, each biome's things, the inside of houses
            CreatorUI.Head(b, W, Loc.T("creator.place"));
            CreatorUI.Do(b, W, Loc.T("mc.creatures.open"), () => OpenScreen(ScreenKind.Creatures), CreatorUI.Pick(false), 30f);
            var bodies = new List<int>();
            for (int i = 0; i < _bodies.Count; i++) bodies.Add(i);
            PlaceGroup(b, W, "obj:creatures", Loc.T("mc.objects.creatures"), bodies, i => _bodies[i]);
            foreach (var g in MapPalette.Groups)
            {
                var picks = new List<int>();
                foreach (var n in g.Pieces)
                {
                    int at = _pieces.IndexOf(n);
                    if (at >= 0) picks.Add(_bodies.Count + at);
                }
                string title = string.IsNullOrEmpty(g.Name) ? Loc.T("mc.objects.inside") : g.Name;
                PlaceGroup(b, W, "obj:" + g.Name, title, picks, i => _pieces[i - _bodies.Count]);
            }
            Restore(w, keep);
        }

        // ------------------------------------------------ picked object window --
        /// ★ THE PICKED OBJECT HAS ITS OWN WINDOW (his call), like the picked
        /// biome: the Objects window only places things.
        void BuildPieceWindow()
        {
            var w = _winPiece;
            if (w == null) return;
            bool spawn = _sel == Sel.Spawn && _selIndex >= 0 && _selIndex < Editing.Spawns.Count;
            bool prop = _sel == Sel.Prop && _selIndex >= 0 && _selIndex < Editing.Props.Count;
            if (!spawn && !prop) { w.Hide(); return; }
            float keep = Keep(w);
            w.Clear();
            var b = w.Body;
            float W = w.Width;

            if (spawn)
            {
                var s = Editing.Spawns[_selIndex];
                w.Title.text = s.Body;
                CreatorUI.Do(b, W, Loc.T("mc.creature.edit"), () => OpenCreature(s.Body), CreatorUI.Pick(false), 28f);
                CreatorUI.Switch(b, W, Loc.T("mc.team"),
                    new[] { Loc.T("mc.team.wild"), Loc.T("mc.team.wizards"), Loc.T("mc.team.acolytes") },
                    Mathf.Clamp(s.Team, 0, 2), i => { s.Team = i; DressPieces(); BuildPieceWindow(); });
                CreatorUI.Number(b, W, Loc.T("mc.size"), 0.2f, 6f, s.Size, false, v => { s.Size = v; DressPieces(); });
                CreatorUI.Number(b, W, Loc.T("mc.delay"), 0f, 600f, s.Delay, true, v => s.Delay = v);
                CreatorUI.Number(b, W, Loc.T("mc.every"), 0f, 600f, s.Every, true, v => s.Every = v);
                CreatorUI.Number(b, W, Loc.T("mc.count"), 1f, 20f, s.Count, true, v => s.Count = Mathf.RoundToInt(v));
                CreatorUI.Number(b, W, Loc.T("mc.maxalive"), 0f, 50f, s.MaxAlive, true, v => s.MaxAlive = Mathf.RoundToInt(v));
                CreatorUI.Switch(b, W, Loc.T("mc.forever"), new[] { Loc.T("opt.off"), Loc.T("opt.on") }, s.Permanent ? 1 : 0,
                    i => { s.Permanent = i == 1; BuildPieceWindow(); });
            }
            else
            {
                w.Title.text = Editing.Props[_selIndex].Prefab;
                var row = CreatorUI.Row(b, W);
                UIKit.Button(row, Loc.T("creator.rotate"), () => TurnSelected(15f), CreatorUI.Pick(false), 13);
                UIKit.Button(row, Loc.T("creator.bigger"), () => ScaleSelected(1.1f), CreatorUI.Pick(false), 13);
                UIKit.Button(row, Loc.T("creator.smaller"), () => ScaleSelected(0.9f), CreatorUI.Pick(false), 13);
            }
            CreatorUI.Do(b, W, Loc.T("creator.delete"), DeleteSelected, CreatorUI.Red, 30f);
            Restore(w, keep);
        }

        /// One group of the palette: its name and count, its pieces only while open.
        void PlaceGroup(RectTransform b, float W, string key, string title, List<int> picks, System.Func<int, string> label)
        {
            if (picks.Count == 0) return;
            bool open = _open.Contains(key);
            bool holdsPick = _tool == Tool.PlaceObject && picks.Contains(_pick);
            CreatorUI.Do(b, W, title + "  (" + picks.Count + ")" + (open ? "  -" : "  +"), () =>
            {
                if (!_open.Remove(key)) _open.Add(key);
                BuildObjectsWindow();
            }, CreatorUI.Pick(open || holdsPick), 30f);
            if (!open) return;
            var names = new List<string>();
            foreach (int i in picks) names.Add(label(i));
            CreatorUI.Grid(b, W, 2, names, n => _tool == Tool.PlaceObject && _pick == picks[n], n =>
            {
                int i = picks[n];
                bool again = _tool == Tool.PlaceObject && _pick == i;
                _tool = again ? Tool.None : Tool.PlaceObject;
                _pick = again ? -1 : i;
                BuildObjectsWindow();
                if (_winBiomes != null && _winBiomes.Visible) BuildBiomesWindow();
                Hint();
            }, 26f, 11);
        }
    }
}
