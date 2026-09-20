using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ THE CREATURE CREATOR, over the whole screen, opened from the Objects
    /// window: the map's creatures, each designed once. A golem or a zombie
    /// body with its size and shape, what it is born as, how it lives, what it
    /// can do (any spell, a summoning spell included) and how it looks. No team
    /// and no runes here: whoever raises it decides its team, and a summoning
    /// spell in the Spells screen names it. Placing stays in the Objects window.
    public partial class MapCreator
    {
        int _creature = -1;              // index into the map book's creatures
        PreviewPane _creaturePane;
        RectTransform _creatureBody, _creatureShape;
        Text _creatureNote;
        CreatureDef _shownCreature;
        SpellBody _shownCreatureBody;
        Vector3 _creatureBaseScale = Vector3.one;
        bool _sureCreature;

        CreatureDef CurrentCreature
        {
            get
            {
                var list = SpellBook.Live.creatures;
                return _creature >= 0 && _creature < list.Count ? list[_creature] : null;
            }
        }

        /// Columns: the list | its body, preview and shape | its numbers | what it drops and its material.
        void BuildCreaturesScreen()
        {
            var s = _screen;
            var mid = s.Columns[1];
            float W = s.Widths[1] - 12f;
            _creatureBody = CreatorScreen.Section(mid, "Body");
            _creaturePane = PreviewPane.Create(mid, W);
            _creaturePane.Posed = CaptureCreaturePose;
            _creatureNote = CreatorUI.Note(mid, W, "", 34f);
            _creatureShape = CreatorScreen.Section(mid, "Shape");
            _shownCreature = null;
            if (CurrentCreature == null) _creature = SpellBook.Live.creatures.Count > 0 ? 0 : -1;
            RefreshCreatures();
        }

        /// The Objects window's and a summon's Edit: the screen, on this creature.
        void OpenCreature(string name)
        {
            int at = SpellBook.Live.creatures.FindIndex(x => x.Name == name);
            if (at >= 0) _creature = at;
            _sureCreature = false;
            OpenScreen(ScreenKind.Creatures);
        }

        void RefreshCreatures()
        {
            if (_screen == null || _screenKind != ScreenKind.Creatures) return;
            var book = SpellBook.Live;
            if (CurrentCreature == null) _creature = book.creatures.Count > 0 ? 0 : -1;
            var cr = CurrentCreature;
            CreatureList(_screen.Columns[0], _screen.Widths[0] - 12f, book);
            CreatureBodySwitch(cr);
            ShowCreature(cr);
            CreatureShapeTools(cr);
            CreatureNumbers(_screen.Columns[2], _screen.Widths[2] - 12f, cr, book);
            CreatureAreaAndLook(_screen.Columns[3], _screen.Widths[3] - 12f, cr, book);
            RefreshArea();
        }

        /// Every frame: the panes wear its numbers as they move.
        void TickCreatures()
        {
            var cr = CurrentCreature;
            if (cr == null) return;
            var pay = cr.Payload;
            float state = SpellPayload.StateT01(pay.State);
            if (_creaturePane != null && _creaturePane.Shown != null) _creaturePane.Tint(cr.PreviewTint(), state, cr.Skin);
            if (_areaPane != null && _areaPane.Shown != null) _areaPane.Tint(pay.Tint(), state, cr.Skin);
        }

        // ------------------------------------------------------------- list --
        void CreatureList(RectTransform col, float W, SpellBook book) => KeepScroll(col, () =>
        {
            CreatorUI.Note(col, W, Loc.T("mc.creatures.note"), 46f);
            var row = CreatorUI.Row(col, W);
            UIKit.Button(row, Loc.T("mc.creature.new"), () =>
            {
                string stem = Loc.T("mc.creature.new"), name = stem;
                for (int n = 2; book.Creature(name) != null; n++) name = stem + " " + n;
                book.creatures.Add(new CreatureDef { Name = name });
                PickCreature(book.creatures.Count - 1);
            }, CreatorUI.Pick(true), 14);
            var cr = CurrentCreature;
            if (cr != null)
                UIKit.Button(row, _sureCreature ? Loc.T("mc.creature.sure") : Loc.T("creator.delete"), () =>
                {
                    if (!_sureCreature) { _sureCreature = true; RefreshCreatures(); return; }
                    // the placed ones go with it; a summon of it raises nothing until given another
                    _sureCreature = false;
                    if (_sel == Sel.Spawn) Select(Sel.None, -1);
                    Editing.Spawns.RemoveAll(s => s.Body == cr.Name);
                    book.creatures.Remove(cr);
                    _creature = book.creatures.Count > 0 ? 0 : -1;
                    DressPieces();
                    RefreshCreatures();
                }, CreatorUI.Red, 14);
            var names = new List<string>();
            foreach (var c in book.creatures) names.Add(c.Name);
            CreatorUI.Grid(col, W, 1, names, i => i == _creature, PickCreature, 30f, 13);
        });

        void PickCreature(int i)
        {
            _creature = i;
            _sureCreature = false;
            RefreshCreatures();
        }

        // ------------------------------------------------ body, preview, shape --
        void CreatureBodySwitch(CreatureDef cr)
        {
            CreatorScreen.Clear(_creatureBody);
            if (cr == null) return;
            CreatorUI.Switch(_creatureBody, _screen.Widths[1] - 12f, Loc.T("mc.creature.body"),
                new[] { Loc.T("mc.body.golem"), Loc.T("mc.body.zombie") }, cr.Body == SpellBody.Zombie ? 1 : 0,
                i => { cr.Body = i == 1 ? SpellBody.Zombie : SpellBody.Golem; RefreshCreatures(); });
        }

        void ShowCreature(CreatureDef cr)
        {
            if (_creaturePane == null) return;
            if (cr == _shownCreature && (cr == null || cr.Body == _shownCreatureBody)) return;
            _shownCreature = cr;
            if (cr == null) { _creaturePane.Clear(); return; }
            _shownCreatureBody = cr.Body;
            _creaturePane.Posable = cr.Body == SpellBody.Golem; // a golem's squares are its shape
            _creaturePane.Show(SpellDef.BodyPrefab(cr.Body));
            _creatureBaseScale = _creaturePane.Shown != null ? _creaturePane.Shown.transform.localScale : Vector3.one;
            ShapeCreaturePreview(cr);
        }

        /// The creature's shape on the pane's body: its proportions on a zombie body, its pose on a golem.
        void ShapeCreaturePreview(CreatureDef cr)
        {
            if (_creaturePane == null || _creaturePane.Shown == null || cr == null) return;
            if (cr.Body == SpellBody.Zombie)
                _creaturePane.Shown.transform.localScale =
                    Vector3.Scale(_creatureBaseScale, new Vector3(cr.Width, cr.Height, cr.Width));
            CreatureLook.Shape(_creaturePane.Shown, cr);
        }

        /// A dragged square: the golem's pose is its shape now.
        void CaptureCreaturePose()
        {
            var cr = CurrentCreature;
            if (cr == null || cr.Body != SpellBody.Golem || _creaturePane == null) return;
            cr.Pose = _creaturePane.CapturePose("").Bones;
        }

        void CreatureShapeTools(CreatureDef cr)
        {
            CreatorScreen.Clear(_creatureShape);
            if (_creatureNote != null)
                _creatureNote.text = cr != null && cr.Body == SpellBody.Golem ? Loc.T("mc.note.handles") : Loc.T("mc.creature.preview");
            if (cr == null) return;
            var b = _creatureShape;
            float W = _screen.Widths[1] - 12f;
            CreatorUI.Head(b, W, Loc.T("mc.spellshape"));
            if (cr.Body == SpellBody.Zombie)
            {
                BuildRow(b, W, cr, Loc.T("mc.build.height"), cr.Height, v => cr.Height = v);
                BuildRow(b, W, cr, Loc.T("mc.build.width"), cr.Width, v => cr.Width = v);
                BuildRow(b, W, cr, Loc.T("mc.build.head"), cr.Head, v => cr.Head = v);
                BuildRow(b, W, cr, Loc.T("mc.build.arms"), cr.Arms, v => cr.Arms = v);
                BuildRow(b, W, cr, Loc.T("mc.build.legs"), cr.Legs, v => cr.Legs = v);
            }
            CreatorUI.Do(b, W, Loc.T("mc.shape.reset"), () =>
            {
                cr.Pose.Clear();
                cr.Height = cr.Width = cr.Head = cr.Arms = cr.Legs = 1f;
                _shownCreature = null; // the plain body again
                RefreshCreatures();
            }, CreatorUI.Pick(false), 28f);
        }

        void BuildRow(RectTransform b, float W, CreatureDef cr, string name, float value, System.Action<float> set) =>
            CreatorUI.Number(b, W, name, CreatureDef.BuildMin, CreatureDef.BuildMax, value, false, v =>
            {
                set(v);
                ShapeCreaturePreview(cr);
            }, v => name + ": " + Mathf.RoundToInt(v * 100f) + "%");

        // ----------------------------------------------------------- numbers --
        void CreatureNumbers(RectTransform col, float W, CreatureDef cr, SpellBook book) => KeepScroll(col, () =>
        {
            if (cr == null)
            {
                CreatorUI.Note(col, W, Loc.T("mc.creature.none"), 30f);
                return;
            }
            CreatorUI.Field(col, W, Loc.T("mc.name"), cr.Name, v =>
            {
                // the placed ones and the summons of it follow the name
                foreach (var s in Editing.Spawns) if (s.Body == cr.Name) s.Body = v;
                foreach (var sp in book.spells) if (sp.Creature == cr.Name) sp.Creature = v;
                cr.Name = v;
                CreatureList(_screen.Columns[0], _screen.Widths[0] - 12f, book);
            });
            CreatorUI.Number(col, W, Loc.T("mc.size"), CreatureDef.SizeMin, CreatureDef.SizeMax, cr.Size, false,
                v => cr.Size = v, v => Loc.T("mc.size") + ": " + v.ToString("0.0") + "x");
            CreatorUI.Note(col, W, Loc.T("mc.note.size"), 34f);

            if (Section(col, W, "creature:behaviour", Loc.T("mc.behaviour"), true, RefreshCreatures))
            {
                string[] ways = { Loc.T("mc.behaviour.roams"), Loc.T("mc.behaviour.hunts"),
                    Loc.T("mc.behaviour.guards"), Loc.T("mc.behaviour.skittish") };
                CreatorUI.Grid(col, W, 2, ways, i => (int)cr.Behaviour == i,
                    i => { cr.Behaviour = (CreatureBehaviour)i; RefreshCreatures(); }, 28f, 13);
                CreatorUI.Note(col, W, BehaviourNote(cr.Behaviour), 34f);
                if (cr.Behaviour == CreatureBehaviour.Guards)
                    CreatorUI.Number(col, W, Loc.T("mc.guardrange"), 3f, 40f, cr.GuardRange, true,
                        v => cr.GuardRange = Mathf.Round(v), v => Loc.T("mc.guardrange") + ": " + Mathf.RoundToInt(v) + " m");
                CreatorUI.Switch(col, W, Loc.T("mc.boss"), new[] { Loc.T("opt.off"), Loc.T("opt.on") }, cr.Boss ? 1 : 0,
                    i => { cr.Boss = i == 1; RefreshCreatures(); });
                CreatorUI.Note(col, W, Loc.T("mc.note.boss"), 34f);
            }

            BornAs(col, W, cr.Axis, "creature:bornas", RefreshCreatures);
            CanDo(col, W, cr.Abilities, cr.Body, book, "creature:cando", RefreshCreatures);
        });

        static string BehaviourNote(CreatureBehaviour way)
        {
            switch (way)
            {
                case CreatureBehaviour.Hunts: return Loc.T("mc.note.hunts");
                case CreatureBehaviour.Guards: return Loc.T("mc.note.guards");
                case CreatureBehaviour.Skittish: return Loc.T("mc.note.skittish");
                default: return Loc.T("mc.note.roams");
            }
        }

        // ------------------------------------------------- area and material --
        void CreatureAreaAndLook(RectTransform col, float W, CreatureDef cr, SpellBook book) => KeepScroll(col, () =>
        {
            if (cr == null) return;
            CreatorUI.Head(col, W, Loc.T("mc.area"));
            CreatorUI.Note(col, W, Loc.T("mc.note.creaturearea"), 34f);
            if (cr.Body == SpellBody.Golem)
                AreaPicker(col, W, book, "creature:area", cr.Name, () => cr.Aoe, v => cr.Aoe = v,
                    a => OpenArea(a, Loc.T("mc.back.creature"), RefreshCreatures), RefreshCreatures);
            if (cr.Skin == null) cr.Skin = new SpellTable.Look();
            MaterialSection(col, W, cr.Skin, "creature:material", RefreshCreatures);
        });
    }
}
