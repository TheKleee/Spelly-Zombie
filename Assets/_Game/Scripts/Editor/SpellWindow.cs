using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ THE SPELL WINDOW. What ships to Workshop authors, so it has to teach
    /// the system rather than assume it: the TEN NUMBERS come first and biggest,
    /// because a spell IS a region of them, and everything else is secondary.
    ///
    /// It writes sz_spells.json - the same file the game reads at startup and
    /// the same file a Workshop package ships. One format, no export step.
    public class SpellWindow : EditorWindow
    {
        [MenuItem("Spelly Zombie/Spells/Spell Window")]
        static void Open() => GetWindow<SpellWindow>("Spells").minSize = new Vector2(720, 520);

        List<SpellTable.Row> _rows;
        int _picked = -1;
        Vector2 _listScroll, _bodyScroll;
        bool _showSummons, _showLook, _showBehaviour = true;

        static readonly string[] AxisNames =
        { "Temp", "Light", "Pressure", "Balance", "State",
          "Affinity", "Strength", "Mind", "Courage", "Clones" };

        static readonly string[] AxisPoles =
        { "chill  ·  hot", "dark  ·  bright", "spread  ·  compressed",
          "slick  ·  sticky", "gas  ·  solid", "repels  ·  attracts",
          "frail  ·  strong", "mindless  ·  clever", "afraid  ·  fearless", "alone  ·  many" };

        void OnEnable() { Load(); }

        void Load()
        {
            SpellTable.Reload();
            _rows = SpellTable.Editable;
            _picked = _rows.Count > 0 ? 0 : -1;
        }

        void OnGUI()
        {
            if (_rows == null) Load();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawList();
                DrawBody();
            }
        }

        // ---------------------------------------------------------------- list
        void DrawList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(190)))
            {
                EditorGUILayout.LabelField("SPELLS", EditorStyles.boldLabel);
                _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
                for (int i = 0; i < _rows.Count; i++)
                {
                    bool on = i == _picked;
                    if (GUILayout.Toggle(on, _rows[i].Name ?? "(unnamed)", "Button") && !on)
                        _picked = i;
                }
                EditorGUILayout.EndScrollView();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("New"))
                    {
                        _rows.Add(new SpellTable.Row { Name = "New Spell" });
                        _picked = _rows.Count - 1;
                    }
                    if (GUILayout.Button("Copy") && _picked >= 0)
                    {
                        _rows.Add(Clone(_rows[_picked]));
                        _rows[_rows.Count - 1].Name += " copy";
                        _picked = _rows.Count - 1;
                    }
                    if (GUILayout.Button("Del") && _picked >= 0)
                    {
                        _rows.RemoveAt(_picked);
                        _picked = Mathf.Min(_picked, _rows.Count - 1);
                    }
                }

                EditorGUILayout.Space();
                if (GUILayout.Button("SAVE", GUILayout.Height(28))) Save();
                if (GUILayout.Button("Reload")) Load();
                EditorGUILayout.LabelField(SpellTable.OverlayPath, EditorStyles.miniLabel);
            }
        }

        // ---------------------------------------------------------------- body
        void DrawBody()
        {
            if (_picked < 0 || _picked >= _rows.Count)
            {
                EditorGUILayout.LabelField("Pick a spell, or make one.");
                return;
            }
            var r = _rows[_picked];
            _bodyScroll = EditorGUILayout.BeginScrollView(_bodyScroll);

            r.Name = EditorGUILayout.TextField("Name", r.Name);
            EditorGUILayout.HelpBox(
                "The name is the KEY. A posed blob with this name becomes its shape; " +
                "an entry in Particle Shapes finds it by this. Rename and both follow.",
                MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("WHAT MAKES IT THIS SPELL", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "A spell is not a recipe - it is a REGION. Anything whose numbers land in here " +
                "IS this spell, whether it got there by two runes, ten, or by drifting into a " +
                "volcano. Zero means the axis is not part of it.",
                MessageType.Info);

            EditorGUILayout.LabelField("Imposed  ·  the spell pushes these onto what it touches",
                EditorStyles.miniBoldLabel);
            for (int i = 0; i < 5; i++) Axis(r, i);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Capacities  ·  the lesser of the spell's and the target's",
                EditorStyles.miniBoldLabel);
            for (int i = 5; i < 10; i++) Axis(r, i);

            Reachable(r);

            EditorGUILayout.Space();
            _showBehaviour = EditorGUILayout.Foldout(_showBehaviour, "HOW IT BEHAVES", true);
            if (_showBehaviour)
            {
                EditorGUI.indentLevel++;
                r.Spreads = Toggle("Spreads", r.Spreads,
                    "Hands itself to the nearest thing, which hands it on again. Fire and poison.");
                r.Attaches = Toggle("Attaches", r.Attaches,
                    "Rides what it hits instead of bursting. Trails, clinging poison, a hook.");
                r.OneShot = Toggle("Spent on contact", r.OneShot,
                    "Delivers once and is gone. A carried teleport.");
                r.Strikes = Toggle("Hunts a target", r.Strikes,
                    "Picks something at birth and slams into it rather than drifting.");
                r.OnlyLiving = Toggle("Only things with a mind", r.OnlyLiving,
                    "Ignores walls and crates. This is the whole of what made poison special.");
                r.SparesOwnTeam = Toggle("Spares your own side", r.SparesOwnTeam,
                    "Off means it hits everyone - which is usually right. Buffing an enemy is a tactic.");
                r.MovesToOrigin = Toggle("Sends it back to the seal", r.MovesToOrigin,
                    "The target is moved to where the spell was drawn. A recall, a hook, a trap.");
                r.Impulse = EditorGUILayout.Slider(
                    new GUIContent("Shove", "Away from the spell. Negative pulls in."),
                    r.Impulse, -20f, 20f);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            _showSummons = EditorGUILayout.Foldout(_showSummons, "WHAT IT CASTS", true);
            if (_showSummons)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "A spell can cast another spell. One child high above pushed down is a METEOR; " +
                    "six spread and staggered is a SHOWER; one at no offset riding along is an AURA; " +
                    "four at the landing thrown outward is DEBRIS. Same fields.",
                    MessageType.Info);
                r.Summons = EditorGUILayout.IntSlider("How many", r.Summons, 0, 12);
                using (new EditorGUI.DisabledScope(r.Summons <= 0))
                {
                    r.SummonHeight = EditorGUILayout.Slider("Height above", r.SummonHeight, 0f, 40f);
                    r.SummonSpread = EditorGUILayout.Slider("Scattered by", r.SummonSpread, 0f, 20f);
                    r.SummonDelay = EditorGUILayout.Slider("After", r.SummonDelay, 0f, 5f);
                    r.SummonStagger = EditorGUILayout.Slider("Apart by", r.SummonStagger, 0f, 2f);
                    r.SummonSpeed = EditorGUILayout.Slider("Pushed at", r.SummonSpeed, 0f, 60f);
                    r.SummonShare = EditorGUILayout.Slider("Carrying", r.SummonShare, 0f, 1f);
                    r.SummonFollows = Toggle("Rides the parent", r.SummonFollows,
                        "No offset, glued on. That is what an aura is.");
                    r.SummonOnImpact = Toggle("At the landing", r.SummonOnImpact,
                        "Children appear where it lands, thrown outward. That is what debris is.");
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            _showLook = EditorGUILayout.Foldout(_showLook, "HOW IT LOOKS", true);
            if (_showLook)
            {
                EditorGUI.indentLevel++;
                r.TrailWidth = EditorGUILayout.Slider("Trail width", r.TrailWidth, 0f, 1f);
                r.TrailSeconds = EditorGUILayout.Slider("Trail lasts", r.TrailSeconds, 0f, 20f);

                if (r.Skin == null && GUILayout.Button("Give it its own movement"))
                    r.Skin = new SpellTable.Look();
                if (r.Skin != null)
                {
                    EditorGUILayout.HelpBox(
                        "Everything is made of the one blob, so the material is the other half of " +
                        "what tells two spells apart. -1 leaves a slider as the material authored it.",
                        MessageType.None);
                    Skin("Liquid wobble", ref r.Skin.Wobble, 0f, 0.5f);
                    Skin("Liquid speed", ref r.Skin.WobbleSpeed, 0f, 8f);
                    Skin("Gas swirl", ref r.Skin.Swirl, 0f, 6f);
                    Skin("Swirl speed", ref r.Skin.SwirlSpeed, 0f, 6f);
                    Skin("Turbulence", ref r.Skin.Turbulence, 0f, 1f);
                    Skin("Bubbles", ref r.Skin.Bubbles, 0f, 1f);
                    Skin("Bubble size", ref r.Skin.BubbleSize, 1f, 40f);
                    Skin("Bubble rise", ref r.Skin.BubbleRise, 0f, 3f);
                    Skin("Break-up", ref r.Skin.Holes, 0f, 1f);
                    Skin("Hole size", ref r.Skin.HoleSize, 1f, 40f);
                    Skin("Rim glow", ref r.Skin.Rim, 0f, 3f);
                    r.Skin.Fx = EditorGUILayout.TextField(
                        new GUIContent("Effect", "A prefab name from FxLibrary's Named list."), r.Skin.Fx);
                    r.Skin.ImpactFx = EditorGUILayout.TextField("Impact effect", r.Skin.ImpactFx);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            DrawWorkbench(r);

            EditorGUILayout.Space();
            r.Effect = EditorGUILayout.TextField(
                new GUIContent("Engine hook", "Reserved for what numbers cannot say. Leave empty."),
                r.Effect);

            EditorGUILayout.EndScrollView();
        }

        int _testLevel = 1;

        /// ★ SEE IT. Defining the region is the LAST step of making a spell -
        /// the rest is try, look, adjust - and until this button existed the
        /// window had no "look" in it at all. You had to launch, own the right
        /// runes, and draw them, just to find out whether a number was right.
        ///
        /// It casts the row straight into the running game, in front of you,
        /// with no runes and no grimoire.
        void DrawWorkbench(SpellTable.Row r)
        {
            EditorGUILayout.LabelField("TRY IT", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Press Play and this casts the spell in front of you, " +
                    "so you can watch it before you commit to any of these numbers.",
                    MessageType.None);
                return;
            }

            _testLevel = EditorGUILayout.IntSlider(
                new GUIContent("Level", "1 hits what it touches · 2 has an area · 3 is a biome"),
                _testLevel, 1, 3);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cast in front of me", GUILayout.Height(26))) CastNow(r, 3.5f);
                if (GUILayout.Button("Drop at my feet", GUILayout.Height(26))) CastNow(r, 0f);
            }
            if (GUILayout.Button("Put a crate in front of me to hit")) DropTarget();
        }

        static Camera Eye => Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();

        void CastNow(SpellTable.Row r, float ahead)
        {
            var eye = Eye;
            if (eye == null) { Debug.LogWarning("[SpellyZombie] no camera to cast from."); return; }
            Vector3 at = eye.transform.position + eye.transform.forward * ahead;
            if (ahead <= 0f) at = eye.transform.position + eye.transform.forward * 1.5f + Vector3.down * 0.8f;
            var p = SpellParticle.Cast(r, at, eye.transform.forward, 2.2f, _testLevel);
            Debug.Log(p == null ? "[SpellyZombie] nothing cast - particle cap reached?"
                : $"[SpellyZombie] cast {r.Name} at level {_testLevel}. It reads as: {p.ShapeName}");
        }

        /// Something to actually hit, so an author can see what the spell DOES
        /// and not only what it looks like.
        void DropTarget()
        {
            var eye = Eye;
            if (eye == null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "SpellTestCrate";
            go.transform.position = eye.transform.position + eye.transform.forward * 4f;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 20f;
            var el = go.AddComponent<Element>();
            el.Natural = new SpellPayload { Strength = 60f };
            Debug.Log("[SpellyZombie] crate dropped. Burn it, freeze it, hook it, blow it up.");
        }

        // ------------------------------------------------------------- helpers
        void Axis(SpellTable.Row r, int i)
        {
            float v = r[i];
            float now = EditorGUILayout.Slider(
                new GUIContent(AxisNames[i], AxisPoles[i]), v, -3f, 3f);
            if (!Mathf.Approximately(now, v)) SetAxis(r, i, now);
        }

        static void SetAxis(SpellTable.Row r, int i, float v)
        {
            switch (i)
            {
                case 0: r.Temp = v; break;
                case 1: r.Lum = v; break;
                case 2: r.Pressure = v; break;
                case 3: r.Balance = v; break;
                case 4: r.State = v; break;
                case 5: r.Affinity = v; break;
                case 6: r.Strength = v; break;
                case 7: r.Int = v; break;
                case 8: r.Courage = v; break;
                default: r.Clones = v; break;
            }
        }

        static void Skin(string label, ref float f, float lo, float hi)
        {
            bool on = f >= 0f;
            using (new EditorGUILayout.HorizontalScope())
            {
                bool want = EditorGUILayout.Toggle(on, GUILayout.Width(18));
                if (want != on) f = want ? lo : -1f;
                using (new EditorGUI.DisabledScope(!want))
                    f = want ? EditorGUILayout.Slider(label, f, lo, hi)
                             : EditorGUILayout.Slider(label, lo, lo, hi);
                if (!want) f = -1f;
            }
        }

        static bool Toggle(string label, bool v, string tip) =>
            EditorGUILayout.Toggle(new GUIContent(label, tip), v);

        /// ★ CAN ANYONE ACTUALLY CAST THIS? A region nobody can reach is a
        /// spell that never happens, and there is nothing in the game to tell
        /// an author that. This names the runes that get there.
        void Reachable(SpellTable.Row r)
        {
            var runes = new List<string>();
            string[] pos = { "Heat", "Light", "Compress", "Sticky", "Solid", "Attract", "-", "-", "-", "-" };
            string[] neg = { "Chill", "Dark", "Spread", "Slick", "Liquid", "Repel", "-", "-", "-", "-" };
            bool impossible = false;

            for (int i = 0; i < 10; i++)
            {
                float v = r[i];
                if (Mathf.Abs(v) < 0.01f) continue;
                string name = v > 0f ? pos[i] : neg[i];
                if (name == "-") { impossible = true; continue; }
                int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(v)));
                runes.Add(n > 1 ? $"{name} ×{n}" : name);
            }

            if (runes.Count == 0 && !impossible)
                EditorGUILayout.HelpBox("Every axis is zero, so EVERYTHING is this spell. " +
                    "Give it at least one number.", MessageType.Warning);
            else if (impossible)
                EditorGUILayout.HelpBox("Uses an axis no rune can push (strength, mind, courage, clones). " +
                    "Only a biome or another spell can get a particle there - which is allowed, " +
                    "but nobody will cast it directly.", MessageType.Warning);
            else
                EditorGUILayout.HelpBox("Cast with:  " + string.Join("  +  ", runes) +
                    "\n…or anything else whose numbers land in the same place.", MessageType.None);
        }

        static SpellTable.Row Clone(SpellTable.Row a)
        {
            var j = JsonUtility.ToJson(a);
            return JsonUtility.FromJson<SpellTable.Row>(j);
        }

        void Save()
        {
            var file = new SpellTable.RowFile { rows = _rows.ToArray() };
            File.WriteAllText(SpellTable.OverlayPath, JsonUtility.ToJson(file, true));
            SpellTable.Reload();
            Debug.Log($"[SpellyZombie] {_rows.Count} spells saved to {SpellTable.OverlayPath}\n" +
                "This is the same file the game reads and the same one a Workshop package ships.");
        }
    }
}
