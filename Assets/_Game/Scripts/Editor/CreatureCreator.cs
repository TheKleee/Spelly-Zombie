using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ THE CREATURE CREATOR, laid out like the Spell Creator: the creature on
    /// the LEFT, rendered, and its numbers on the RIGHT. A golem's squares drag
    /// its shape; a zombie body takes its height, width, head, arms and legs.
    /// Summoning spells in the Spell Creator name these, and a creature casts
    /// any spell in the book, a summoning spell included.
    public class CreatureCreator : EditorWindow
    {
        [MenuItem("Spelly Zombie/Spells/Creature Creator")]
        static void OpenMenu() => Open(null);

        /// Open on this creature, if the book has it.
        public static void Open(string name)
        {
            var w = GetWindow<CreatureCreator>("Creature Creator");
            w.minSize = new Vector2(900, 600);
            if (w._book == null) w.OnEnable();
            int at = string.IsNullOrEmpty(name) ? -1 : w._book.creatures.FindIndex(c => c.Name == name);
            if (at >= 0) w._picked = at;
            w.Repaint();
        }

        static SpellBook _shared;

        /// One book for both creators, so saving one never undoes the other's changes.
        internal static SpellBook SharedBook(bool reload)
        {
            if (_shared == null || reload) _shared = SpellBook.Load();
            return _shared;
        }

        SpellBook _book;
        int _picked = -1;
        Vector2 _list, _right;
        readonly SpellPreview _preview = new SpellPreview();
        CreatureDef _shownFor;
        SpellBody _shownBody;
        Vector3 _baseScale = Vector3.one;

        void OnEnable()
        {
            _book = SharedBook(false);
            if (_picked < 0 || _picked >= _book.creatures.Count) _picked = _book.creatures.Count > 0 ? 0 : -1;
            _preview.OnNeedsRepaint = Repaint;
        }
        void OnDisable() => _preview.Dispose();

        void OnGUI()
        {
            if (_book == null) OnEnable();
            if (_book != SharedBook(false)) { _book = SharedBook(false); _shownFor = null; }
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawList();
                DrawPreviewColumn();
                DrawControls();
            }
        }

        CreatureDef Current => _picked >= 0 && _picked < _book.creatures.Count ? _book.creatures[_picked] : null;

        /// The creature's shape on a preview's body, from the scale the body was shown at.
        internal static void ShapeOn(GameObject shown, CreatureDef c, Vector3 baseScale)
        {
            if (shown == null || c == null) return;
            if (c.Body == SpellBody.Zombie)
                shown.transform.localScale = Vector3.Scale(baseScale, new Vector3(c.Width, c.Height, c.Width));
            CreatureLook.Shape(shown, c);
        }

        // ---------------------------------------------------------- the list
        void DrawList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(160)))
            {
                EditorGUILayout.LabelField("CREATURES", EditorStyles.boldLabel);
                _list = EditorGUILayout.BeginScrollView(_list);
                for (int i = 0; i < _book.creatures.Count; i++)
                {
                    bool on = i == _picked;
                    if (GUILayout.Toggle(on, _book.creatures[i].Name, "Button") && !on) _picked = i;
                }
                EditorGUILayout.EndScrollView();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("New"))
                    {
                        string name = "New creature";
                        for (int n = 2; _book.Creature(name) != null; n++) name = "New creature " + n;
                        _book.creatures.Add(new CreatureDef { Name = name });
                        _picked = _book.creatures.Count - 1;
                    }
                    if (GUILayout.Button("Delete") && Current != null)
                    {
                        var c = Current;
                        int summons = 0;
                        foreach (var sp in _book.spells) if (sp.Creature == c.Name) summons++;
                        string msg = $"Delete the creature \"{c.Name}\"?";
                        if (summons > 0) msg += $"\n\n{summons} summoning spell(s) raise it. They will raise nothing until given another.";
                        if (EditorUtility.DisplayDialog("Delete creature", msg, "Delete", "Keep"))
                        {
                            _book.creatures.Remove(c);
                            _picked = Mathf.Min(_picked, _book.creatures.Count - 1);
                        }
                    }
                }

                EditorGUILayout.Space();
                if (GUILayout.Button("SAVE", GUILayout.Height(28)))
                {
                    _book.Save();
                    ShowNotification(new GUIContent("Saved"));
                }
                if (GUILayout.Button("Reload"))
                {
                    _book = SharedBook(true);
                    _picked = Mathf.Min(_picked, _book.creatures.Count - 1);
                    _shownFor = null;
                }
            }
        }

        // -------------------------------------------------------- the preview
        void DrawPreviewColumn()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(340)))
            {
                var c = Current;
                EditorGUILayout.LabelField("BODY", EditorStyles.boldLabel);
                if (c != null)
                    c.Body = GUILayout.Toolbar(c.Body == SpellBody.Zombie ? 1 : 0, new[] { "Golem", "Zombie" }) == 1
                        ? SpellBody.Zombie : SpellBody.Golem;

                var rect = GUILayoutUtility.GetRect(340, 340, GUILayout.ExpandWidth(false));
                if (c != null)
                {
                    if (_shownFor != c || _shownBody != c.Body)
                    {
                        _preview.Show(SpellDef.BodyPrefab(c.Body));
                        _baseScale = _preview.Shown != null ? _preview.Shown.transform.localScale : Vector3.one;
                        ShapeOn(_preview.Shown, c, _baseScale);
                        _shownFor = c;
                        _shownBody = c.Body;
                    }
                    _preview.Tint(c.PreviewTint(), SpellPayload.StateT01(c.Payload.State), c.Skin);
                }
                bool golem = c != null && c.Body == SpellBody.Golem;
                if (_preview.Draw(rect, posable: golem) && golem) CapturePose(c);

                EditorGUILayout.HelpBox(golem
                    ? "Drag to orbit, wheel to zoom. The squares are its shape: green is up, red is right, " +
                      "blue is forward, pale is the opposite end. Colour follows the sliders."
                    : "Drag to orbit, wheel to zoom. Colour follows the sliders.", MessageType.None);

                DrawShape(c);
            }
        }

        void CapturePose(CreatureDef c)
        {
            var shown = _preview.Shown;
            if (shown == null) return;
            c.Pose.Clear();
            foreach (var t in shown.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("D_"))
                    c.Pose.Add(new BonePose { Bone = t.name, P = t.localPosition, R = t.localRotation, S = t.localScale });
        }

        void DrawShape(CreatureDef c)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("SHAPE", EditorStyles.boldLabel);
            if (c == null) return;
            if (c.Body == SpellBody.Zombie)
            {
                EditorGUI.BeginChangeCheck();
                c.Height = EditorGUILayout.Slider("Height", c.Height, CreatureDef.BuildMin, CreatureDef.BuildMax);
                c.Width = EditorGUILayout.Slider("Width", c.Width, CreatureDef.BuildMin, CreatureDef.BuildMax);
                c.Head = EditorGUILayout.Slider("Head", c.Head, CreatureDef.BuildMin, CreatureDef.BuildMax);
                c.Arms = EditorGUILayout.Slider("Arms", c.Arms, CreatureDef.BuildMin, CreatureDef.BuildMax);
                c.Legs = EditorGUILayout.Slider("Legs", c.Legs, CreatureDef.BuildMin, CreatureDef.BuildMax);
                if (EditorGUI.EndChangeCheck()) ShapeOn(_preview.Shown, c, _baseScale);
            }
            else
                EditorGUILayout.LabelField(c.Pose.Count > 0 ? "Its own shape." : "The plain blob.", EditorStyles.miniLabel);
            if (GUILayout.Button("Reset shape"))
            {
                c.Pose.Clear();
                c.Height = c.Width = c.Head = c.Arms = c.Legs = 1f;
                _shownFor = null; // the plain body again
            }
        }

        // ------------------------------------------------------- the controls
        void DrawControls()
        {
            var c = Current;
            if (c == null) { EditorGUILayout.LabelField("Make a creature."); return; }

            _right = EditorGUILayout.BeginScrollView(_right);
            string was = c.Name;
            c.Name = EditorGUILayout.TextField("Name", c.Name);
            if (c.Name != was)
                foreach (var sp in _book.spells) if (sp.Creature == was) sp.Creature = c.Name; // its summons follow
            c.Size = EditorGUILayout.Slider(new GUIContent("Size", "Times the body's own size. A seal or a placement scales it on top."),
                c.Size, CreatureDef.SizeMin, CreatureDef.SizeMax);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("BEHAVIOUR", EditorStyles.boldLabel);
            c.Behaviour = (CreatureBehaviour)GUILayout.Toolbar((int)c.Behaviour, new[] { "Roams", "Hunts", "Guards", "Skittish" });
            if (c.Behaviour == CreatureBehaviour.Guards)
                c.GuardRange = Mathf.Round(EditorGUILayout.Slider("Guard distance (m)", c.GuardRange, 3f, 40f));
            c.Boss = EditorGUILayout.Toggle(new GUIContent("Boss", "Everyone sees its health. A counting environment team stands while one lives."), c.Boss);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("BORN AS", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Its natural state. It drifts from here like anything in the world - " +
                                       "a zombie born hot is at home in fire. Strength is its health.", EditorStyles.wordWrappedMiniLabel);
            for (int i = 0; i < SpellPayload.AxisCount; i++)
            {
                SpellPayload.SpellRange(i, out int lo, out int hi);
                c.Axis[i] = EditorGUILayout.IntSlider(
                    new GUIContent(SpellCreator.Names[i] + SpellPayload.UnitName(i), SpellCreator.Poles[i]), c.Axis[i], lo, hi);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("CAN DO", EditorStyles.boldLabel);
            DrawAbilities(c);

            if (c.Body == SpellBody.Golem)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("AREA", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("What it drops around itself as it walks.", EditorStyles.wordWrappedMiniLabel);
                var names = new List<string> { "none" };
                foreach (var a in _book.aoes) names.Add(a.Name);
                int at = Mathf.Max(0, names.IndexOf(c.Aoe ?? ""));
                int now = EditorGUILayout.Popup("Area", at, names.ToArray());
                c.Aoe = now <= 0 ? "" : names[now];
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Create new area"))
                    {
                        var a = new AoeDef { Name = c.Name + " area" };
                        _book.aoes.Add(a);
                        c.Aoe = a.Name;
                        AoeCreator.Show(_book, a, null);
                    }
                    using (new EditorGUI.DisabledScope(!c.HasAoe))
                        if (GUILayout.Button("Open area")) AoeCreator.Show(_book, _book.Aoe(c.Aoe), null);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("MATERIAL", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Everything starts still. Move a slider to add movement.",
                                       EditorStyles.wordWrappedMiniLabel);
            c.Skin ??= new SpellTable.Look();
            SpellCreator.LookSliders(c.Skin);
            EditorGUILayout.EndScrollView();
        }

        /// ★ MOVES AND CASTS ARE TWO MENUS. A move is the body itself acting -
        /// engine code wearing an animation you pick. A cast is any spell from
        /// the book, taken in turn; a summoning spell raises its creature.
        void DrawAbilities(CreatureDef c)
        {
            EditorGUILayout.LabelField("MOVES", EditorStyles.miniBoldLabel);
            if (c.Body == SpellBody.Golem)
                EditorGUILayout.LabelField("A golem always charges at what it fights.", EditorStyles.miniLabel);
            bool anyMove = false;
            for (int i = 0; i < c.Abilities.Count; i++)
            {
                string key = c.Abilities[i];
                if (!IsMove(key)) continue;
                anyMove = true;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(MoveLabel(key), GUILayout.Width(70));
                    var clip = c.MoveClip(key);
                    var picked = (AnimationClip)EditorGUILayout.ObjectField(clip, typeof(AnimationClip), false);
                    if (picked != clip) SetMoveClip(c, key, picked);
                    if (GUILayout.Button("-", GUILayout.Width(24)))
                    { c.Abilities.RemoveAt(i); SetMoveClip(c, key, null); break; }
                }
            }
            if (!anyMove)
                EditorGUILayout.LabelField("No moves of its own.", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                var options = new List<string> { "add a move..." };
                var keys = new List<string> { "" };
                foreach (var (key, label) in Innate)
                    if (!c.Abilities.Contains(key)) { options.Add(label); keys.Add(key); }
                using (new EditorGUI.DisabledScope(options.Count == 1))
                {
                    int pick = EditorGUILayout.Popup(0, options.ToArray(), GUILayout.Width(160));
                    if (pick > 0) c.Abilities.Add(keys[pick]);
                }
            }
            EditorGUILayout.LabelField("The animation is yours to pick. Empty = its built-in tell.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("CASTS", EditorStyles.miniBoldLabel);
            bool anyCast = false;
            for (int i = 0; i < c.Abilities.Count; i++)
            {
                string key = c.Abilities[i];
                if (IsMove(key)) continue;
                anyCast = true;
                using (new EditorGUILayout.HorizontalScope())
                {
                    var sp = _book.Spell(key);
                    EditorGUILayout.LabelField(sp != null && sp.IsSummon ? key + "  (summons " + sp.Creature + ")" : key);
                    if (GUILayout.Button("-", GUILayout.Width(24))) { c.Abilities.RemoveAt(i); break; }
                }
            }
            if (!anyCast)
                EditorGUILayout.LabelField("It casts nothing.", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                var options = new List<string> { "add a cast..." };
                var keys = new List<string> { "" };
                foreach (var sp in _book.spells)
                    if (!c.Abilities.Contains(sp.Name))
                    { options.Add(sp.IsSummon ? sp.Name + " (summon)" : sp.Name); keys.Add(sp.Name); }
                int pick = EditorGUILayout.Popup(0, options.ToArray(), GUILayout.Width(160));
                if (pick > 0) c.Abilities.Add(keys[pick]);
                if (GUILayout.Button("Every spell", GUILayout.Width(90)))
                    foreach (var sp in _book.spells)
                        if (!c.Abilities.Contains(sp.Name)) c.Abilities.Add(sp.Name);
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                    c.Abilities.RemoveAll(a => !IsMove(a));
            }
        }

        static bool IsMove(string key)
        {
            foreach (var (k, _) in Innate) if (k == key) return true;
            return false;
        }

        static void SetMoveClip(CreatureDef c, string move, AnimationClip clip)
        {
            for (int i = 0; i < c.MoveAnims.Count; i++)
                if (c.MoveAnims[i].Move == move)
                {
                    if (clip == null) c.MoveAnims.RemoveAt(i);
                    else c.MoveAnims[i].Clip = clip;
                    return;
                }
            if (clip != null) c.MoveAnims.Add(new MoveAnim { Move = move, Clip = clip });
        }

        // ★ CHARGE IS THE ONLY MOVE. Everything else a body does is a CAST of
        // a book spell - goo included (the ranged zombie casts "Goo").
        static readonly (string key, string label)[] Innate =
        {
            (Zombie.Charge, "Charge"),
        };

        static string MoveLabel(string key)
        {
            foreach (var (k, label) in Innate) if (k == key) return label;
            return key;
        }
    }
}
