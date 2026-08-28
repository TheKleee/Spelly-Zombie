using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ THE SPELL CREATOR, laid out as he drew it: the spell on the LEFT,
    /// rendered and posable, and its numbers on the RIGHT. What you see is the
    /// thing you are making, coloured by the sliders as you move them.
    ///
    /// A spell carries one area. Pick an existing one from the list, or make a
    /// new one - that opens the Area window, which has its own preview and its
    /// own save.
    public class SpellCreator : EditorWindow
    {
        [MenuItem("Spelly Zombie/Spells/Spell Creator")]
        static void Open() => GetWindow<SpellCreator>("Spell Creator").minSize = new Vector2(900, 600);

        SpellBook _book;
        int _picked = -1;
        Vector2 _list, _right;
        readonly SpellPreview _preview = new SpellPreview();
        SpellDef _shownFor;
        SpellBody _shownBody;

        static readonly string[] Names =
        { "Temperature", "Light", "Pressure", "Balance", "State",
          "Affinity", "Strength", "Mind", "Courage", "Clones" };

        static readonly string[] Poles =
        { "chill · hot", "dark · bright", "spread · compressed", "slick · sticky",
          "gas below -50 · liquid · solid above +50", "repels · attracts", "frail · strong",
          "mindless · clever", "afraid · fearless", "alone · many" };

        void OnEnable()
        {
            _book = SpellBook.Load();
            _picked = _book.spells.Count > 0 ? 0 : -1;
            _preview.OnNeedsRepaint = Repaint;
        }
        void OnDisable() => _preview.Dispose();

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
                EditorGUILayout.LabelField("SPELLS", EditorStyles.boldLabel);
                _list = EditorGUILayout.BeginScrollView(_list);
                for (int i = 0; i < _book.spells.Count; i++)
                {
                    bool on = i == _picked;
                    var spx = _book.spells[i];
                    string tag = spx.Book == BookKind.Acolyte ? "  [A]" : "";
                    if (GUILayout.Toggle(on, spx.Name + tag, "Button") && !on) _picked = i;
                }
                EditorGUILayout.EndScrollView();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("New"))
                    {
                        _book.spells.Add(new SpellDef());
                        _picked = _book.spells.Count - 1;
                    }
                    if (GUILayout.Button("Delete") && _picked >= 0)
                    {
                        _book.spells.RemoveAt(_picked);
                        _picked = Mathf.Min(_picked, _book.spells.Count - 1);
                    }
                }
                EditorGUILayout.Space();
                if (GUILayout.Button("SAVE", GUILayout.Height(28)))
                {
                    _book.Save();
                    ShowNotification(new GUIContent("Saved"));
                }
                if (GUILayout.Button("Reload")) OnEnable();
            }
        }

        // -------------------------------------------------------- the preview
        void DrawPreviewColumn()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(340)))
            {
                var sp = Current;
                EditorGUILayout.LabelField("TYPE", EditorStyles.boldLabel);
                if (sp != null)
                    sp.Body = (SpellBody)GUILayout.Toolbar((int)sp.Body, new[] { "Particle", "Zombie", "Golem" });

                var rect = GUILayoutUtility.GetRect(340, 340, GUILayout.ExpandWidth(false));
                if (sp != null)
                {
                    if (_shownFor != sp || _shownBody != sp.Body)
                    {
                        _preview.Show(BodyPrefab(sp));
                        // its own saved shape, if it has one - the base blob
                        // is only what it wears until you pose it
                        // the SAME search the dropdown uses - folder and list
                        // both. Asking only the CollectionManager meant a shape
                        // saved to the folder loaded from the dropdown and then
                        // silently did not when you came back to the spell.
                        string wantName = string.IsNullOrEmpty(sp.Shape) ? sp.Name : sp.Shape;
                        var data = _book.Shape(wantName);
                        if (data != null)
                        {
                            _preview.ApplyPose(data);
                            if (sp.Skin == null && data.Look != null)
                                sp.Skin = JsonUtility.FromJson<SpellTable.Look>(JsonUtility.ToJson(data.Look));
                        }
                        else
                        {
                            var pose = FindShape(wantName);
                            if (pose != null)
                            {
                                _preview.ApplyPose(pose);
                                var skin = pose.GetComponent<ShapeSkin>();
                                if (skin != null && sp.Skin == null)
                                    sp.Skin = JsonUtility.FromJson<SpellTable.Look>(JsonUtility.ToJson(skin.Look));
                            }
                        }
                        _shownFor = sp; _shownBody = sp.Body;
                    }
                    // A BODY HAS ITS OWN COLOUR and the spell only shades it.
                    // A zombie is green before any spell touches it; a blob is
                    // nothing until the numbers colour it. Painting a zombie
                    // straight from the payload made it brown.
                    var pay = sp.Payload;
                    // Tint() is white when every axis is zero, and lerping a
                    // green toward white just paled it. A body with nothing
                    // authored is its base colour, full stop; with numbers, the
                    // numbers shade it.
                    bool any = pay.Strongest > 0.05f;
                    Color tint = sp.Body == SpellBody.Particle
                        ? pay.Tint()
                        : any ? Color.Lerp(BaseSkin(sp.Body), pay.Tint(), DrawingConfig.BiomeTintStrength)
                              : BaseSkin(sp.Body);
                    _preview.Tint(tint, SpellPayload.StateT01(pay.State), sp.Skin);
                }
                _preview.Draw(rect, posable: true);

                EditorGUILayout.HelpBox(
                    "Drag to orbit, wheel to zoom. The squares are bones: green is up, red is " +
                    "right, blue is forward, pale is the opposite end. Drag one to reshape the " +
                    "body. Colour follows the sliders.",
                    MessageType.None);

                DrawShapeTools(sp);
            }
        }

        string _shapeName = "";
        int _loadPick;

        /// ★ SHAPES ARE A LIBRARY, NOT A PROPERTY. Save the pose under any
        /// name; load any saved pose onto any spell and keep editing. So a
        /// Repel funnel can be saved, loaded onto Attract, flipped, and saved
        /// again as Attract - the shape outlives the spell it was drawn on.
        void DrawShapeTools(SpellDef sp)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("SHAPE", EditorStyles.boldLabel);

            // the name field follows the spell you are on, unless you typed
            // over it - it used to keep the last spell's name, so "Save as"
            // quietly overwrote the wrong shape
            if (sp != _nameFor) { _shapeName = sp != null ? (string.IsNullOrEmpty(sp.Shape) ? sp.Name : sp.Shape) : ""; _nameFor = sp; }

            var lib = AllShapes();
            var names = new string[lib.Count + 1];
            names[0] = "load a saved shape...";
            for (int i = 0; i < lib.Count; i++) names[i + 1] = lib[i].Name;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_preview.Shown == null))
                {
                    int pick = EditorGUILayout.Popup(0, names);
                    if (pick > 0)
                    {
                        var e = lib[pick - 1];
                        SpellTable.Look look = null;
                        if (e.Data != null)
                        {
                            _preview.ApplyPose(e.Data);
                            look = e.Data.Look;
                        }
                        else if (e.Prefab != null)
                        {
                            _preview.ApplyPose(e.Prefab);
                            var skin = e.Prefab.GetComponent<ShapeSkin>();
                            if (skin != null) look = skin.Look;
                        }
                        _shapeName = e.Name;
                        if (sp != null)
                        {
                            sp.Shape = e.Name;
                            // and its material, if the shape was saved with one
                            if (look != null)
                                sp.Skin = JsonUtility.FromJson<SpellTable.Look>(JsonUtility.ToJson(look));
                        }
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _shapeName = EditorGUILayout.TextField(_shapeName);
                using (new EditorGUI.DisabledScope(sp == null || _preview.Shown == null
                                                   || string.IsNullOrEmpty(_shapeName)))
                    if (GUILayout.Button("Save as", GUILayout.Width(70))) SaveShape(sp, _shapeName);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                string wearing = sp != null ? sp.Shape : "";
                EditorGUILayout.LabelField(
                    !string.IsNullOrEmpty(wearing) ? $"This spell wears: {wearing}" : "This spell wears the plain body.",
                    EditorStyles.miniLabel);

                // ★ DELETE. Shapes pile up - a Tornado and an Attract 2 that are
                // nearly the same, one better - and a library you cannot prune
                // is a library you stop trusting. Asks first, names what it is
                // about to do, and any spell wearing it falls back to the plain
                // body rather than pointing at nothing.
                bool canDelete = !string.IsNullOrEmpty(wearing)
                    && (_book.Shape(wearing) != null || FindShape(wearing) != null);
                using (new EditorGUI.DisabledScope(!canDelete))
                    if (GUILayout.Button("Delete shape", GUILayout.Width(90)))
                        DeleteShape(wearing);
            }
        }

        SpellDef _nameFor;

        static GameObject FindShape(string name)
        {
            foreach (var go in LegacyShapePrefabs())
                if (go != null && string.Equals(go.name, name, System.StringComparison.OrdinalIgnoreCase))
                    return go;
            return null;
        }

        void DeleteShape(string name)
        {
            var data = _book.Shape(name);
            var go = FindShape(name);
            if (data == null && go == null) return;

            int wearers = 0;
            foreach (var sp in _book.spells)
                if (string.Equals(sp.Shape, name, System.StringComparison.OrdinalIgnoreCase)) wearers++;

            string msg = $"Delete the shape \"{name}\"?";
            if (go != null) msg += $"\n\nLegacy prefab: {AssetDatabase.GetAssetPath(go)}";
            if (wearers > 0) msg += $"\n\n{wearers} spell(s) wear it. They will fall back to the plain body.";
            if (!EditorUtility.DisplayDialog("Delete shape", msg, "Delete", "Keep")) return;

            foreach (var sp in _book.spells)
                if (string.Equals(sp.Shape, name, System.StringComparison.OrdinalIgnoreCase)) sp.Shape = "";
            if (data != null) _book.shapes.Remove(data);
            _book.Save();

            if (go != null)
            {
                string path = AssetDatabase.GetAssetPath(go);
                // and out of the CollectionManager list, so nothing holds a dead reference
                var cm = CollectionManager.I;
                if (cm != null && cm.RemoveParticleShape(go)) EditorUtility.SetDirty(cm);
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.SaveAssets();
            }

            _shapeName = "";
            _nameFor = null;
            // the preview is wearing a pose that no longer exists: back to the body
            _shownFor = null;
            ShowNotification(new GUIContent($"Deleted {name}"));
        }

        class ShapeEntry { public string Name; public ShapeDef Data; public GameObject Prefab; }

        /// Every saved pose: the book first (shapes are data), then legacy
        /// prefabs the book does not shadow.
        List<ShapeEntry> AllShapes()
        {
            var list = new List<ShapeEntry>();
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var s in _book.shapes)
                if (!string.IsNullOrEmpty(s.Name) && seen.Add(s.Name))
                    list.Add(new ShapeEntry { Name = s.Name, Data = s });
            foreach (var go in LegacyShapePrefabs())
                if (seen.Add(go.name))
                    list.Add(new ShapeEntry { Name = go.name, Prefab = go });
            list.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.Ordinal));
            return list;
        }

        /// The old prefab shapes - the shape folder and the CollectionManager
        /// list both count. Kept as fallback until the book crosses the wire.
        static GameObject[] LegacyShapePrefabs()
        {
            var found = new List<GameObject>();
            var seen = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ShapeFolder }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go != null && seen.Add(go.name)) found.Add(go);
            }
            var cm = CollectionManager.I;
            if (cm != null)
                foreach (var go in cm.ParticleShapesAll)
                    if (go != null && seen.Add(go.name)) found.Add(go);
            found.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
            return found.ToArray();
        }

        const string ShapeFolder = "Assets/_Game/Prefabs/Particle Shapes";

        // ------------------------------------------------------- the controls
        void DrawControls()
        {
            var sp = Current;
            if (sp == null) { EditorGUILayout.LabelField("Make a spell."); return; }

            _right = EditorGUILayout.BeginScrollView(_right);
            sp.Name = EditorGUILayout.TextField("Name", sp.Name);

            // ★ WHICH BOOK. Wizard and acolyte spells are not one pile - the
            // list a player can cast from is the book in their hands, and the
            // acolyte curse swaps that book without changing their side.
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Book");
                sp.Book = (BookKind)GUILayout.Toolbar((int)sp.Book, new[] { "Wizard", "Acolyte" });
            }

            EditorGUILayout.Space();
            if (sp.IsBody)
            {
                // ★ A BODY IS SUMMONED, NOT BECOME. Runes raise it, it is born
                // with a natural state, and it carries abilities. None of that
                // is a threshold, so none of it reads like one.
                EditorGUILayout.LabelField("SUMMONED BY", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("The runes a seal must hold to raise this. The same rune twice " +
                                           "means twice - so two Liquids can raise something one cannot.",
                                           EditorStyles.wordWrappedMiniLabel);
                DrawRunes(sp);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("BORN AS", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Its natural state. It drifts from here like anything in the world - " +
                                           "a zombie born hot is at home in fire.", EditorStyles.wordWrappedMiniLabel);
                for (int i = 0; i < SpellPayload.AxisCount; i++) Axis(sp, i);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("CAN DO", EditorStyles.boldLabel);
                DrawAbilities(sp);
            }
            else
            {
                EditorGUILayout.LabelField("CONDITIONS", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("The conditions that need to be met for the spell to exist - " +
                                           "what it is, and what it gives to whatever it touches. " +
                                           "Tick the box to lock an axis as a biome.", EditorStyles.wordWrappedMiniLabel);
                for (int i = 0; i < 6; i++) Axis(sp, i);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("EFFECTS", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Byproducts a spell may carry - never necessary for its " +
                                           "creation. They ride the same numbers and land on whatever " +
                                           "the spell touches.", EditorStyles.wordWrappedMiniLabel);
                for (int i = 6; i < SpellPayload.AxisCount; i++) Axis(sp, i);
                Verdict(sp);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("AREA", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("The effect that rides on this spell. It works from the same numbers.",
                                       EditorStyles.wordWrappedMiniLabel);
            var names = new List<string> { "none" };
            foreach (var a in _book.aoes) names.Add(a.Name);
            int at = Mathf.Max(0, names.IndexOf(sp.Aoe ?? ""));
            int now = EditorGUILayout.Popup("Area", at, names.ToArray());
            sp.Aoe = now <= 0 ? "" : names[now];
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create new area"))
                {
                    var a = new AoeDef { Name = sp.Name + " area" };
                    _book.aoes.Add(a);
                    sp.Aoe = a.Name;
                    AoeCreator.Show(_book, a, sp);
                }
                using (new EditorGUI.DisabledScope(!sp.HasAoe))
                    if (GUILayout.Button("Open area")) AoeCreator.Show(_book, _book.Aoe(sp.Aoe), sp);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("MATERIAL", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Everything starts still. Move a slider to add movement.",
                                       EditorStyles.wordWrappedMiniLabel);
            sp.Skin ??= new SpellTable.Look();
            var k = sp.Skin;
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
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------ pieces
        SpellDef Current => _picked >= 0 && _picked < _book.spells.Count ? _book.spells[_picked] : null;

        void Axis(SpellDef sp, int i)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // ★ EACH AXIS IN ITS OWN UNITS, as whole numbers. Degrees for
                // heat, HP for strength, a count for clones, percent for the
                // rest - and SIGNED on all of them, because a spell is a delta
                // and must be able to drain as well as fill.
                SpellPayload.SpellRange(i, out int lo, out int hi);
                sp.Axis[i] = EditorGUILayout.IntSlider(
                    new GUIContent(Names[i] + SpellPayload.UnitName(i), Poles[i]), sp.Axis[i], lo, hi);
                // only CONDITIONS can be biome-locked (his rule): the effect
                // axes never create a spell, so a biome cannot be made of them
                if (i >= 6)
                {
                    sp.BiomeAxis[i] = false;
                    GUILayout.Space(22);
                    return;
                }
                bool canLock = sp.Axis[i] != 0;
                using (new EditorGUI.DisabledScope(!canLock))
                {
                    bool want = GUILayout.Toggle(sp.BiomeAxis[i] && canLock,
                        new GUIContent("", "Biome: lock this axis. Nothing but another spell can move it."),
                        GUILayout.Width(18));
                    sp.BiomeAxis[i] = want && canLock;
                }
            }
        }

        /// What was authored, said back plainly - and NOTHING about how to
        /// reach it. "Drawn with Heat x2" was a guess dressed as a fact: nobody
        /// knows what one rune's push is worth or how many stack to cross a
        /// line. The only true statement is the line itself.
        /// What was authored, said back plainly. NO LEVEL - that word is ours,
        /// for talking about the ladder, and it does not belong in front of an
        /// author. A spell is a threshold reader: numbers cross these, this
        /// happens. Ten of them along one axis is a perfectly good design.
        void Verdict(SpellDef sp)
        {
            bool any = false;
            for (int i = 0; i < SpellPayload.AxisCount; i++) if (sp.Axis[i] != 0) { any = true; break; }
            if (!any)
            {
                EditorGUILayout.HelpBox("Every axis is zero. This spell is nothing.", MessageType.Warning);
                return;
            }

            var lines = new List<string>();
            lines.Add("When something's numbers reach these, it becomes this.");
            if (sp.HasAoe) lines.Add("It carries an area, which outlives it.");
            if (sp.AnyBiome) lines.Add("Its locked axes hold against the world and do not spend on impact.");
            else if (!sp.HasAoe) lines.Add("It spends itself on whatever it touches.");
            lines.Add(sp.Physical
                ? "Force can destroy it, and what it still holds scatters."
                : "Force cannot touch it. It only goes out when its numbers run down.");
            EditorGUILayout.HelpBox(string.Join("\n", lines), MessageType.None);
        }

        static readonly (RuneType rune, string name)[] AllRunes =
        {
            (RuneType.HeatUp, "Heat"),        (RuneType.HeatDown, "Chill"),
            (RuneType.LuminanceUp, "Light"),  (RuneType.LuminanceDown, "Dark"),
            (RuneType.DensityUp, "Compress"), (RuneType.DensityDown, "Spread"),
            (RuneType.StickyUp, "Sticky"),    (RuneType.StickyDown, "Slick"),
            (RuneType.StateSolid, "Solid"),   (RuneType.StateLiquid, "Liquid"),
            (RuneType.DirectionAway, "Attract"), (RuneType.DirectionToward, "Repel"),
        };

        /// ★ A LIST, NOT A CHECKLIST. The same rune can appear more than once -
        /// two Liquids in a seal can be what raises a bigger zombie - so this
        /// is a bag of runes you add to and remove from, with a count beside
        /// each. Toggles could never say "twice".
        void DrawRunes(SpellDef sp)
        {
            // group what is there, in order of first appearance
            var order = new List<RuneType>();
            var count = new Dictionary<RuneType, int>();
            foreach (var r in sp.Runes)
            {
                if (!count.ContainsKey(r)) { count[r] = 0; order.Add(r); }
                count[r]++;
            }

            if (order.Count == 0)
                EditorGUILayout.LabelField("Nothing summons this yet.", EditorStyles.miniLabel);

            foreach (var r in order)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(RuneName(r), GUILayout.Width(90));
                    EditorGUILayout.LabelField(count[r] > 1 ? $"x{count[r]}" : "", GUILayout.Width(30));
                    if (GUILayout.Button("+", GUILayout.Width(24))) sp.Runes.Add(r);
                    if (GUILayout.Button("-", GUILayout.Width(24))) sp.Runes.Remove(r);   // removes one
                    GUILayout.FlexibleSpace();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var names = new string[AllRunes.Length + 1];
                names[0] = "add a rune...";
                for (int i = 0; i < AllRunes.Length; i++) names[i + 1] = AllRunes[i].name;
                int pick = EditorGUILayout.Popup(0, names, GUILayout.Width(140));
                if (pick > 0) sp.Runes.Add(AllRunes[pick - 1].rune);
                if (GUILayout.Button("All twelve", GUILayout.Width(80)))
                { sp.Runes.Clear(); foreach (var r in AllRunes) sp.Runes.Add(r.rune); }
                if (GUILayout.Button("Clear", GUILayout.Width(60))) sp.Runes.Clear();
            }
        }

        static string RuneName(RuneType r)
        {
            foreach (var (rune, name) in AllRunes) if (rune == r) return name;
            return r.ToString();
        }

        /// ★ MOVES AND CASTS ARE TWO MENUS. A move is the body itself acting -
        /// engine code wearing an animation you pick. A cast is a drawn spell
        /// from its own book. Mixing them in one dropdown made a body verb
        /// look like just another spell.
        void DrawAbilities(SpellDef sp)
        {
            EditorGUILayout.LabelField("MOVES", EditorStyles.miniBoldLabel);
            bool anyMove = false;
            for (int i = 0; i < sp.Abilities.Count; i++)
            {
                string key = sp.Abilities[i];
                if (!IsMove(key)) continue;
                anyMove = true;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(MoveLabel(key), GUILayout.Width(70));
                    var clip = sp.MoveClip(key);
                    var picked = (AnimationClip)EditorGUILayout.ObjectField(
                        clip, typeof(AnimationClip), false);
                    if (picked != clip) SetMoveClip(sp, key, picked);
                    if (GUILayout.Button("-", GUILayout.Width(24)))
                    { sp.Abilities.RemoveAt(i); SetMoveClip(sp, key, null); break; }
                }
            }
            if (!anyMove)
                EditorGUILayout.LabelField("No moves of its own.", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                var options = new List<string> { "add a move..." };
                var keys = new List<string> { "" };
                foreach (var (key, label) in Innate)
                    if (!sp.Abilities.Contains(key)) { options.Add(label); keys.Add(key); }
                using (new EditorGUI.DisabledScope(options.Count == 1))
                {
                    int pick = EditorGUILayout.Popup(0, options.ToArray(), GUILayout.Width(160));
                    if (pick > 0) sp.Abilities.Add(keys[pick]);
                }
            }
            EditorGUILayout.LabelField("The animation is yours to pick. Empty = its built-in tell.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("CASTS", EditorStyles.miniBoldLabel);
            bool anyCast = false;
            for (int i = 0; i < sp.Abilities.Count; i++)
            {
                string key = sp.Abilities[i];
                if (IsMove(key)) continue;
                anyCast = true;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(key);
                    if (GUILayout.Button("-", GUILayout.Width(24))) { sp.Abilities.RemoveAt(i); break; }
                }
            }
            if (!anyCast)
                EditorGUILayout.LabelField("It casts nothing.", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                var options = new List<string> { "add a cast..." };
                var keys = new List<string> { "" };
                foreach (var other in _book.spells)
                    if (other != sp && !other.IsBody && other.Book == sp.Book
                        && !sp.Abilities.Contains(other.Name))
                    { options.Add(other.Name); keys.Add(other.Name); }

                int pick = EditorGUILayout.Popup(0, options.ToArray(), GUILayout.Width(160));
                if (pick > 0) sp.Abilities.Add(keys[pick]);

                if (GUILayout.Button("Every spell in its book", GUILayout.Width(150)))
                    foreach (var other in _book.spells)
                        if (!other.IsBody && other.Book == sp.Book && !sp.Abilities.Contains(other.Name))
                            sp.Abilities.Add(other.Name);
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                    sp.Abilities.RemoveAll(a => !IsMove(a));
            }
        }

        static bool IsMove(string key)
        {
            foreach (var (k, _) in Innate) if (k == key) return true;
            return false;
        }

        static void SetMoveClip(SpellDef sp, string move, AnimationClip clip)
        {
            for (int i = 0; i < sp.MoveAnims.Count; i++)
                if (sp.MoveAnims[i].Move == move)
                {
                    if (clip == null) sp.MoveAnims.RemoveAt(i);
                    else sp.MoveAnims[i].Clip = clip;
                    return;
                }
            if (clip != null) sp.MoveAnims.Add(new MoveAnim { Move = move, Clip = clip });
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

        /// What a body looks like before any spell shades it - the same
        /// greens the game paints, so the editor and the game agree.
        static Color BaseSkin(SpellBody b) => b == SpellBody.Zombie
            ? DrawingConfig.SummonMeleeColor
            : new Color(0.55f, 0.55f, 0.5f);   // golem: stone

        static GameObject BodyPrefab(SpellDef sp) => sp.Body switch
        {
            SpellBody.Zombie => CollectionManager.ZombieBody,
            SpellBody.Golem => CollectionManager.Golem,
            _ => CollectionManager.ParticleBlob,
        };

        /// ★ A SHAPE IS DATA IN THE BOOK - the D_ bones' positions plus the
        /// material sliders, nothing else. No prefab, no asset list, so a
        /// Workshop spell ships as pure JSON. A body with no bones (the
        /// zombie) saves a material-only shape, which is still a good thing
        /// to keep and load onto another spell.
        void SaveShape(SpellDef sp, string name)
        {
            var shown = _preview.Shown;
            if (shown == null) return;

            var def = new ShapeDef { Name = name };
            foreach (var t in shown.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("D_"))
                    def.Bones.Add(new BonePose
                    { Bone = t.name, P = t.localPosition, R = t.localRotation, S = t.localScale });
            if (def.Bones.Count == 0)
                Debug.Log($"[SpellyZombie] {name}: no bones on this body, saving the material only.");

            // THE MATERIAL GOES WITH THE POSE - a copy, not a shared reference
            if (sp != null && sp.Skin != null)
                def.Look = JsonUtility.FromJson<SpellTable.Look>(JsonUtility.ToJson(sp.Skin));

            _book.shapes.RemoveAll(s => string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase));
            _book.shapes.Add(def);
            _book.Save();

            if (sp != null) sp.Shape = name;
            ShowNotification(new GUIContent($"Saved {name}"));
            Debug.Log($"[SpellyZombie] shape '{name}' saved into the book.");
        }

        /// One-time move: every shape prefab (folder and CollectionManager
        /// list) becomes book data. The prefabs stay behind as the client
        /// proxy fallback until the book crosses the wire.
        [MenuItem("Spelly Zombie/Spells/Import Shape Prefabs Into Book")]
        static void ImportShapePrefabs()
        {
            var book = SpellBook.Load();
            int n = 0;
            foreach (var go in LegacyShapePrefabs())
            {
                var def = new ShapeDef { Name = go.name };
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("D_"))
                        def.Bones.Add(new BonePose
                        { Bone = t.name, P = t.localPosition, R = t.localRotation, S = t.localScale });
                var ss = go.GetComponent<ShapeSkin>();
                if (ss != null && ss.Look != null)
                    def.Look = JsonUtility.FromJson<SpellTable.Look>(JsonUtility.ToJson(ss.Look));
                book.shapes.RemoveAll(s => string.Equals(s.Name, def.Name, System.StringComparison.OrdinalIgnoreCase));
                book.shapes.Add(def);
                n++;
            }
            book.Save();
            Debug.Log($"[SpellyZombie] {n} shape prefab(s) imported into the book.");
        }

    }
}
