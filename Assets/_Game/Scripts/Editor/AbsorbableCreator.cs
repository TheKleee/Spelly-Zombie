using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ THE ABSORBABLE CREATOR, laid out like the Spell Creator: the mote on
    /// the LEFT of the numbers, rendered and posable, coloured live by the
    /// light slider. Shapes come from the SAME library the spells use - a
    /// funnel saved there wears here too.
    ///
    /// Each save is a prefab under Prefabs/Absorbables: the posed blob, its
    /// glow light, its flight trail and the AbsorbSource data. Place them
    /// anywhere by hand - a candle holds a flame absorbable.
    public class AbsorbableCreator : EditorWindow
    {
        [MenuItem("Spelly Zombie/Spells/Absorbable Creator")]
        static void Open() => GetWindow<AbsorbableCreator>("Absorbables").minSize = new Vector2(900, 600);

        const string Folder = "Assets/_Game/Prefabs/Absorbables";

        readonly SpellPreview _preview = new SpellPreview();
        SpellBook _book;
        List<GameObject> _saved = new List<GameObject>();
        int _picked = -1;
        Vector2 _list, _right;
        GameObject _shownBlob;

        // ---- the one being crafted ----
        string _name = "Absorbable";
        int _temp, _light, _density, _balance, _state, _affinity;
        bool _infinite = true;
        float _range = 3.5f;
        float _regrow = 8f;
        GameObject _blob;
        float _blobScale = 0.35f;
        Color _lightColor = new Color(1f, 0.85f, 0.4f);
        float _lightIntensity = 2f;
        float _lightRange = 5f;
        float _trailTime = 0.55f;
        float _trailWidth = 0.14f;
        Material _trailMat;
        float _stateT = 1f;   // 0 gas · 0.5 liquid · 1 solid
        SpellTable.Look _look = new SpellTable.Look();
        string _shapeName = "";

        void OnEnable()
        {
            _book = SpellBook.Load();
            _preview.OnNeedsRepaint = Repaint;
            if (_blob == null) _blob = CollectionManager.ParticleBlob; // his authored slot
            RefreshSaved();
        }
        void OnDisable() => _preview.Dispose();
        void OnFocus() => RefreshSaved();

        void RefreshSaved()
        {
            _saved.Clear();
            if (!AssetDatabase.IsValidFolder(Folder)) return;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Folder }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go != null && go.GetComponent<AbsorbSource>() != null) _saved.Add(go);
            }
            _saved.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        }

        void OnGUI()
        {
            if (_book == null) OnEnable();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawList();
                DrawPreviewColumn();
                DrawControls();
            }
        }

        // ---------------------------------------------------------- the list
        void DrawList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(160)))
            {
                EditorGUILayout.LabelField("ABSORBABLES", EditorStyles.boldLabel);
                _list = EditorGUILayout.BeginScrollView(_list);
                for (int i = 0; i < _saved.Count; i++)
                {
                    bool on = i == _picked;
                    if (GUILayout.Toggle(on, _saved[i].name, "Button") && !on)
                    {
                        _picked = i;
                        LoadFrom(_saved[i]);
                    }
                }
                EditorGUILayout.EndScrollView();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("New"))
                    {
                        _picked = -1;
                        _name = "Absorbable";
                        _temp = _light = _density = _balance = _state = _affinity = 0;
                        _shownBlob = null;   // fresh unposed blob in the pane
                    }
                    if (GUILayout.Button("Delete") && _picked >= 0 && _picked < _saved.Count)
                        DeleteSaved(_saved[_picked]);
                }
                if (GUILayout.Button("Design Starters"))
                {
                    DesignStarters();
                    RefreshSaved();
                }
                EditorGUILayout.Space();
                if (GUILayout.Button("SAVE", GUILayout.Height(28))) Save();
            }
        }

        void DeleteSaved(GameObject go)
        {
            string path = AssetDatabase.GetAssetPath(go);
            if (!EditorUtility.DisplayDialog("Delete absorbable",
                $"Delete \"{go.name}\"?\n\n{path}\n\nScenes holding placed copies keep them "
                + "as missing prefabs.", "Delete", "Keep")) return;
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
            _picked = -1;
            RefreshSaved();
        }

        // -------------------------------------------------------- the preview
        void DrawPreviewColumn()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(340)))
            {
                EditorGUILayout.LabelField("THE MOTE", EditorStyles.boldLabel);

                var rect = GUILayoutUtility.GetRect(340, 340, GUILayout.ExpandWidth(false));
                // Shown dies on every domain reload while _shownBlob, a Unity
                // reference, survives one - without the second test the pane
                // stayed "nothing to show yet" until the blob was reassigned
                if (_blob != null && (_shownBlob != _blob || _preview.Shown == null))
                {
                    _preview.Show(_blob);
                    _shownBlob = _blob;
                }
                _preview.Tint(_lightColor, _stateT, _look);
                _preview.Draw(rect, posable: true);

                EditorGUILayout.HelpBox(
                    "Drag to orbit, wheel to zoom. The squares are bones: green is up, red is "
                    + "right, blue is forward, pale is the opposite end. Drag one to reshape "
                    + "the mote. Colour follows the light.", MessageType.None);

                DrawShapeTools();
            }
        }

        /// The SAME shape library the Spell Creator uses - the book first,
        /// legacy prefabs second. A pose saved on either window loads on both.
        void DrawShapeTools()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("SHAPE", EditorStyles.boldLabel);

            var names = new List<string> { "load a saved shape..." };
            var datas = new List<ShapeDef>();
            var prefabs = new List<GameObject>();
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var s in _book.shapes)
                if (!string.IsNullOrEmpty(s.Name) && seen.Add(s.Name))
                { names.Add(s.Name); datas.Add(s); prefabs.Add(null); }
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab",
                new[] { "Assets/_Game/Prefabs/Particle Shapes" }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go != null && seen.Add(go.name))
                { names.Add(go.name); datas.Add(null); prefabs.Add(go); }
            }

            using (new EditorGUI.DisabledScope(_preview.Shown == null))
            {
                int pick = EditorGUILayout.Popup(0, names.ToArray());
                if (pick > 0)
                {
                    if (datas[pick - 1] != null) _preview.ApplyPose(datas[pick - 1]);
                    else _preview.ApplyPose(prefabs[pick - 1]);
                    _shapeName = names[pick];
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _shapeName = EditorGUILayout.TextField(_shapeName);
                using (new EditorGUI.DisabledScope(_preview.Shown == null
                                                   || string.IsNullOrEmpty(_shapeName)))
                    if (GUILayout.Button("Save as", GUILayout.Width(70)))
                    {
                        var def = new ShapeDef { Name = _shapeName };
                        foreach (var t in _preview.Shown.GetComponentsInChildren<Transform>(true))
                            if (t.name.StartsWith("D_"))
                                def.Bones.Add(new BonePose
                                { Bone = t.name, P = t.localPosition, R = t.localRotation, S = t.localScale });
                        _book.shapes.RemoveAll(s =>
                            string.Equals(s.Name, _shapeName, System.StringComparison.OrdinalIgnoreCase));
                        _book.shapes.Add(def);
                        _book.Save();
                        ShowNotification(new GUIContent($"Saved {_shapeName}"));
                    }
            }
            EditorGUILayout.LabelField(
                "The pose in the pane is what the prefab bakes.", EditorStyles.miniLabel);
        }

        // ------------------------------------------------------- the controls
        void DrawControls()
        {
            using (var sv = new EditorGUILayout.ScrollViewScope(_right))
            {
                _right = sv.scrollPosition;

                EditorGUILayout.LabelField("WHAT IT IS", EditorStyles.boldLabel);
                _name = EditorGUILayout.TextField("Name", _name);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("AXES (human units)", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("The strongest ABSOLUTE value teaches first; the sign "
                    + "picks the rune (-35 heat teaches Chill). Equal values teach in random "
                    + "order. These are also what the absorbable gives.", MessageType.None);
                _temp = EditorGUILayout.IntSlider("Temperature", _temp, -150, 150);
                _light = EditorGUILayout.IntSlider("Light", _light, -150, 150);
                _density = EditorGUILayout.IntSlider("Density", _density, -150, 150);
                _balance = EditorGUILayout.IntSlider("Balance", _balance, -150, 150);
                _state = EditorGUILayout.IntSlider("State", _state, -150, 150);
                _affinity = EditorGUILayout.IntSlider("Affinity", _affinity, -150, 150);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("BEHAVIOUR", EditorStyles.boldLabel);
                _infinite = EditorGUILayout.Toggle("Infinite (regrows)", _infinite);
                _range = EditorGUILayout.Slider("Absorb Range", _range, 1.5f, 8f);
                _regrow = EditorGUILayout.Slider("Regrow Seconds", _regrow, 1f, 60f);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("LOOK", EditorStyles.boldLabel);
                var blob = (GameObject)EditorGUILayout.ObjectField(
                    "Blob", _blob, typeof(GameObject), false);
                if (blob != _blob) { _blob = blob; _shownBlob = null; }
                _blobScale = EditorGUILayout.Slider("Blob Scale", _blobScale, 0.1f, 1.5f);
                _lightColor = EditorGUILayout.ColorField("Light Color", _lightColor);
                _lightIntensity = EditorGUILayout.Slider("Light Intensity", _lightIntensity, 0f, 8f);
                _lightRange = EditorGUILayout.Slider("Light Range", _lightRange, 1f, 12f);
                EditorGUILayout.HelpBox("Lights are real point lights: the PC renderer runs "
                    + "Forward+, good for 256 visible at once. Small ranges keep them cheap.",
                    MessageType.None);
                _trailTime = EditorGUILayout.Slider("Trail Seconds", _trailTime, 0.1f, 2f);
                _trailWidth = EditorGUILayout.Slider("Trail Width", _trailWidth, 0.02f, 0.5f);
                _trailMat = (Material)EditorGUILayout.ObjectField(
                    "Trail Material (optional)", _trailMat, typeof(Material), false);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("MATERIAL", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Everything starts still. Move a slider to add movement.",
                                           EditorStyles.wordWrappedMiniLabel);
                _stateT = EditorGUILayout.Slider(
                    new GUIContent("State", "0 gas · 0.5 liquid · 1 solid"), _stateT, 0f, 1f);
                _look ??= new SpellTable.Look();
                var k = _look;
                k.Wobble      = EditorGUILayout.Slider("Liquid wobble", k.Wobble, 0f, 0.5f);
                k.WobbleSpeed = EditorGUILayout.Slider("Liquid speed", k.WobbleSpeed, 0f, 8f);
                k.Swirl       = EditorGUILayout.Slider("Gas swirl", k.Swirl, 0f, 6f);
                k.SwirlSpeed  = EditorGUILayout.Slider("Swirl speed", k.SwirlSpeed, 0f, 6f);
                k.Turbulence  = EditorGUILayout.Slider("Turbulence", k.Turbulence, 0f, 1f);
                k.Bubbles     = EditorGUILayout.Slider("Bubbles", k.Bubbles, 0f, 1f);
                k.BubbleSize  = EditorGUILayout.Slider("Bubble size", k.BubbleSize, 1f, 40f);
                k.BubbleRise  = EditorGUILayout.Slider("Bubble rise", k.BubbleRise, 0f, 3f);
                k.Holes       = EditorGUILayout.Slider("Break-up", k.Holes, 0f, 1f);
                k.HoleSize    = EditorGUILayout.Slider("Hole size", k.HoleSize, 1f, 40f);
                k.Rim         = EditorGUILayout.Slider("Rim glow", k.Rim, 0f, 3f);
            }
        }

        // ----------------------------------------------------------- baking
        /// Everything a bake needs, so the window and the starter designs go
        /// through the ONE build path.
        internal struct BakeConfig
        {
            public string Name;
            public int Temp, Light, Density, Balance, State, Affinity;
            public bool Infinite;
            public float Range, Regrow;
            public GameObject Blob;
            public float BlobScale;
            public Color LightColor;
            public float LightIntensity, LightRange;
            public float TrailTime, TrailWidth;
            public Material TrailMat;
            public float StateT;
            public SpellTable.Look Look;
        }

        void Save()
        {
            if (_blob == null)
            {
                EditorUtility.DisplayDialog("Absorbables",
                    "No blob assigned and the Collection Manager holds none. Assign one - "
                    + "nothing is auto-picked for you.", "OK");
                return;
            }
            if (string.IsNullOrWhiteSpace(_name))
            {
                EditorUtility.DisplayDialog("Absorbables", "Name it first.", "OK");
                return;
            }
            var path = Bake(new BakeConfig
            {
                Name = _name,
                Temp = _temp, Light = _light, Density = _density,
                Balance = _balance, State = _state, Affinity = _affinity,
                Infinite = _infinite, Range = _range, Regrow = _regrow,
                Blob = _blob, BlobScale = _blobScale,
                LightColor = _lightColor, LightIntensity = _lightIntensity,
                LightRange = _lightRange,
                TrailTime = _trailTime, TrailWidth = _trailWidth, TrailMat = _trailMat,
                StateT = _stateT, Look = _look,
            }, _preview.Shown);
            RefreshSaved();
            for (int i = 0; i < _saved.Count; i++)
                if (_saved[i] != null && _saved[i].name == _name) _picked = i;
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            ShowNotification(new GUIContent($"Saved {_name}"));
        }

        /// Build and save one absorbable prefab. posedSource, when given, has
        /// its D_ bones copied by name onto the baked blob - the pane's pose.
        internal static string Bake(BakeConfig c, GameObject posedSource)
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Game/Prefabs", "Absorbables");
            string path = $"{Folder}/{c.Name}.prefab";

            var root = new GameObject(c.Name);
            try
            {
                var src = root.AddComponent<AbsorbSource>();
                src.Temperature = c.Temp; src.Light = c.Light; src.Density = c.Density;
                src.Balance = c.Balance; src.State = c.State; src.Affinity = c.Affinity;
                src.Infinite = c.Infinite; src.Range = c.Range; src.RegrowSeconds = c.Regrow;

                // the aim ray needs a body to hit; a trigger so nothing bumps it
                var col = root.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = Mathf.Max(0.3f, c.BlobScale * 0.9f);

                var mote = new GameObject("Mote");
                mote.transform.SetParent(root.transform, false);
                src.Mote = mote.transform;

                var blob = (GameObject)PrefabUtility.InstantiatePrefab(c.Blob, mote.transform);
                blob.transform.localPosition = Vector3.zero;
                blob.transform.localScale = Vector3.one * c.BlobScale;

                // ★ THE POSE IN THE PANE IS THE PREFAB. Bones copy by name from
                // the preview onto the baked blob, exactly what you dragged.
                if (posedSource != null)
                {
                    var posed = new Dictionary<string, Transform>();
                    foreach (var t in posedSource.GetComponentsInChildren<Transform>(true))
                        if (t.name.StartsWith("D_") && !posed.ContainsKey(t.name))
                            posed[t.name] = t;
                    foreach (var t in blob.GetComponentsInChildren<Transform>(true))
                        if (posed.TryGetValue(t.name, out var from))
                        {
                            t.localPosition = from.localPosition;
                            t.localRotation = from.localRotation;
                            t.localScale = from.localScale;
                        }
                }

                // the light colour is the mote's colour, live at runtime too;
                // state and material bake exactly as the pane shows them
                var view = blob.GetComponentInChildren<StateView>(true);
                if (view == null) view = blob.AddComponent<StateView>();
                view.Tint = c.LightColor;
                view.DriveTint = true;
                view.StateT = c.Look != null ? c.StateT : 1f;
                if (c.Look != null)
                    view.Look = JsonUtility.FromJson<SpellTable.Look>(
                        JsonUtility.ToJson(c.Look));   // a copy, never shared

                var lightGo = new GameObject("Glow");
                lightGo.transform.SetParent(mote.transform, false);
                var li = lightGo.AddComponent<Light>();
                li.type = LightType.Point;
                li.color = c.LightColor;
                li.intensity = c.LightIntensity;
                li.range = c.LightRange;
                li.shadows = LightShadows.None;

                var trail = mote.AddComponent<TrailRenderer>();
                trail.time = c.TrailTime;
                trail.startWidth = c.TrailWidth;
                trail.endWidth = 0f;
                trail.startColor = c.LightColor;
                trail.endColor = new Color(c.LightColor.r, c.LightColor.g, c.LightColor.b, 0f);
                trail.emitting = false;   // wakes only for the flight
                trail.sharedMaterial = c.TrailMat != null ? c.TrailMat : TrailMaterial(path);

                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.SaveAssets();
                return path;
            }
            finally { DestroyImmediate(root); }
        }

        static Material TrailMaterial(string prefabPath)
        {
            string matPath = prefabPath.Replace(".prefab", "_Trail.mat");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (existing != null) return existing;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var m = new Material(shader);
            AssetDatabase.CreateAsset(m, matPath);
            return m;
        }

        /// ★ THE FOUR HE DICTATED (Aug 14): Flame, Puddle, Mud, Rock - his
        /// world-object lineup, spoken in the new axes model. Strongest
        /// absolute value teaches first, the sign picks the side, so each
        /// object's teach order is authored by the gaps between its numbers.
        /// Mud's old triple wanted Liquid AND Solid - two signs of one axis -
        /// so it leans Sticky/Liquid/Compress; retune in the window.
        [MenuItem("Spelly Zombie/Spells/Design Starter Absorbables")]
        static void DesignStarters()
        {
            var blob = CollectionManager.ParticleBlob;
            if (blob == null)
            {
                EditorUtility.DisplayDialog("Absorbables",
                    "No Particle Blob found - open a scene whose Collection Manager has "
                    + "the blob slot assigned.", "OK");
                return;
            }

            var flame = new Color(1f, 0.55f, 0.2f);
            var water = new Color(0.35f, 0.65f, 1f);
            var mud = new Color(0.6f, 0.45f, 0.28f);
            var rock = new Color(0.55f, 0.5f, 0.65f);

            // never overwrite a starter he has already tuned - only the
            // missing ones are designed
            void BakeIfMissing(BakeConfig c)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/{c.Name}.prefab") != null) return;
                Bake(c, null);
            }

            var f = Starter("Flame", blob, flame, 2.5f);
            f.Temp = 90; f.Light = 55; f.Density = -30;
            f.StateT = 0.15f;   // near gas
            f.Look = new SpellTable.Look { Swirl = 2.5f, SwirlSpeed = 3f, Turbulence = 0.5f, Rim = 1.2f };
            BakeIfMissing(f);

            var p = Starter("Puddle", blob, water, 1.8f);
            p.State = -70; p.Temp = -45; p.Balance = -25;
            p.StateT = 0.5f;    // liquid
            p.Look = new SpellTable.Look { Wobble = 0.3f, WobbleSpeed = 3f, Bubbles = 0.3f, BubbleRise = 1f };
            BakeIfMissing(p);

            var m = Starter("Mud", blob, mud, 1.4f);
            m.Balance = 80; m.State = -45; m.Density = 25;
            m.StateT = 0.6f;    // thick liquid
            m.Look = new SpellTable.Look { Wobble = 0.12f, WobbleSpeed = 1.2f };
            BakeIfMissing(m);

            var r = Starter("Rock", blob, rock, 1.2f);
            r.State = 85; r.Light = -50; r.Density = 25;
            r.StateT = 1f;      // solid
            r.Look = new SpellTable.Look();
            BakeIfMissing(r);

            Debug.Log("[SpellyZombie] starter absorbables designed: Flame (Heat, Light, "
                + "Spread), Puddle (Liquid, Chill, Slick), Mud (Sticky, Liquid, Compress), "
                + "Rock (Solid, Dark, Compress) - in Prefabs/Absorbables.");
            EditorGUIUtility.PingObject(
                AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/Flame.prefab"));
        }

        static BakeConfig Starter(string name, GameObject blob, Color c, float glow) =>
            new BakeConfig
            {
                Name = name,
                Infinite = true,
                Range = 3.5f,
                Regrow = 8f,
                Blob = blob,
                BlobScale = 0.35f,
                LightColor = c,
                LightIntensity = glow,
                LightRange = 5f,
                TrailTime = 0.55f,
                TrailWidth = 0.14f,
            };

        // ----------------------------------------------------------- loading
        void LoadFrom(GameObject go)
        {
            var src = go.GetComponent<AbsorbSource>();
            if (src == null) return;
            _name = go.name;
            _temp = src.Temperature; _light = src.Light; _density = src.Density;
            _balance = src.Balance; _state = src.State; _affinity = src.Affinity;
            _infinite = src.Infinite; _range = src.Range; _regrow = src.RegrowSeconds;

            var li = go.GetComponentInChildren<Light>(true);
            if (li != null)
            {
                _lightColor = li.color;
                _lightIntensity = li.intensity;
                _lightRange = li.range;
            }
            var tr = go.GetComponentInChildren<TrailRenderer>(true);
            if (tr != null)
            {
                _trailTime = tr.time;
                _trailWidth = tr.startWidth;
                _trailMat = tr.sharedMaterial;
            }
            var sv = go.GetComponentInChildren<StateView>(true);
            if (sv != null)
            {
                _stateT = sv.StateT;
                _look = sv.Look != null
                    ? JsonUtility.FromJson<SpellTable.Look>(JsonUtility.ToJson(sv.Look))
                    : new SpellTable.Look();
            }
            var baked = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (baked != null) _blobScale = baked.transform.root != null
                ? Mathf.Max(0.1f, ScaleOf(go)) : _blobScale;

            // wear the saved pose in the pane: fresh blob, then the prefab's
            // own bones copied on by name
            _shownBlob = null;
            if (_blob != null)
            {
                _preview.Show(_blob);
                _shownBlob = _blob;
                _preview.ApplyPose(go);
            }
        }

        static float ScaleOf(GameObject go)
        {
            var src = go.GetComponent<AbsorbSource>();
            if (src == null || src.Mote == null || src.Mote.childCount == 0) return 0.35f;
            return src.Mote.GetChild(0).localScale.x;
        }
    }
}
