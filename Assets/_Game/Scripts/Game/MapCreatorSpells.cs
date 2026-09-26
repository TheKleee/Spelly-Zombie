using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ THE SPELLS CREATOR, over the whole screen: the Spell Crafter in the
    /// game, on the map's own book. The list; the spell rendered live, posable,
    /// with its saved shapes; its numbers; its area and material. A summon is
    /// a spell too: its runes and the creature it raises, which is designed in
    /// the Creature Creator. Long lists sit in drop downs and the less used
    /// parts fold away. An area is its own screen on top, like the Spell
    /// Crafter's Area window. What changes here travels with the map, never
    /// into the main game or another map.
    public partial class MapCreator
    {
        int _spell = -1;
        PreviewPane _spellPane, _areaPane;
        RectTransform _spellShape;
        Text _verdict, _areaNoLook;
        SpellDef _shownSpell, _shapeNameFor;
        CreatureDef _shownSummoned;
        SpellBody _shownSummonedBody;
        GameObject _shownAreaLook;
        string _shapeName = "";
        bool _sureShape, _sureArea, _summonWanted, _shownAsSummon;
        CreatorScreen _areaScreen;
        string _areaName;
        System.Action _areaClosed;

        static readonly (string key, System.Func<SpellTable.Look, float> get, System.Action<SpellTable.Look, float> set, float min, float max)[] LookRows =
        {
            ("mc.look.wobble", k => k.Wobble, (k, v) => k.Wobble = v, 0f, 0.5f),
            ("mc.look.wobblespeed", k => k.WobbleSpeed, (k, v) => k.WobbleSpeed = v, 0f, 8f),
            ("mc.look.swirl", k => k.Swirl, (k, v) => k.Swirl = v, 0f, 6f),
            ("mc.look.swirlspeed", k => k.SwirlSpeed, (k, v) => k.SwirlSpeed = v, 0f, 6f),
            ("mc.look.turbulence", k => k.Turbulence, (k, v) => k.Turbulence = v, 0f, 1f),
            ("mc.look.bubbles", k => k.Bubbles, (k, v) => k.Bubbles = v, 0f, 1f),
            ("mc.look.bubblesize", k => k.BubbleSize, (k, v) => k.BubbleSize = v, 1f, 40f),
            ("mc.look.bubblerise", k => k.BubbleRise, (k, v) => k.BubbleRise = v, 0f, 3f),
            ("mc.look.holes", k => k.Holes, (k, v) => k.Holes = v, 0f, 1f),
            ("mc.look.holesize", k => k.HoleSize, (k, v) => k.HoleSize = v, 1f, 40f),
            ("mc.look.rim", k => k.Rim, (k, v) => k.Rim = v, 0f, 3f),
        };

        SpellDef CurrentSpell
        {
            get
            {
                var book = SpellBook.Live;
                return _spell >= 0 && _spell < book.spells.Count ? book.spells[_spell] : null;
            }
        }

        static int FirstSpell() => SpellBook.Live.spells.Count > 0 ? 0 : -1;

        /// A spell's name with what it is: an acolyte's, a summon.
        static string SpellLabel(SpellDef x)
        {
            var tags = new List<string>();
            if (x.Book == BookKind.Acolyte) tags.Add(Loc.T("mc.side.acolyte"));
            if (x.IsSummon) tags.Add(Loc.T("mc.kind.summon"));
            return tags.Count == 0 ? x.Name : x.Name + "  (" + string.Join(", ", tags) + ")";
        }

        /// Columns: the list | the preview and its shape | the numbers | the area and material.
        void BuildSpellsScreen()
        {
            var s = _screen;
            var mid = s.Columns[1];
            _spellPane = PreviewPane.Create(mid, s.Widths[1] - 12f);
            CreatorUI.Note(mid, s.Widths[1] - 12f, Loc.T("mc.preview.note"), 34f);
            _spellShape = CreatorScreen.Section(mid, "Shape");
            _shownSpell = null;
            if (CurrentSpell == null) _spell = FirstSpell();
            RefreshSpells();
        }

        void RefreshSpells()
        {
            if (_screen == null || _screenKind != ScreenKind.Spells) return;
            var book = SpellBook.Live;
            if (CurrentSpell == null) _spell = FirstSpell();
            var sp = CurrentSpell;
            SpellList(_screen.Columns[0], _screen.Widths[0] - 12f, book);
            ShowSpellBody(sp, book);
            SpellShapeTools(sp, book);
            SpellNumbers(_screen.Columns[2], _screen.Widths[2] - 12f, sp, book);
            SpellAreaAndLook(_screen.Columns[3], _screen.Widths[3] - 12f, sp, book);
            RefreshArea();
        }

        /// Every frame the creator is up: the panes wear the numbers as they move.
        void TickSpells()
        {
            var sp = CurrentSpell;
            if (sp == null) return;
            if (sp.IsSummon)
            {
                // a summon looks like the creature it raises
                var raised = SpellBook.Live.Creature(sp.Creature);
                if (raised != null && _spellPane != null && _spellPane.Shown != null)
                    _spellPane.Tint(raised.PreviewTint(), SpellPayload.StateT01(raised.Payload.State), raised.Skin);
                return;
            }
            var pay = sp.Payload;
            float state = SpellPayload.StateT01(pay.State);
            if (_spellPane != null && _spellPane.Shown != null) _spellPane.Tint(sp.PreviewTint(), state, sp.Skin);
            if (_areaPane != null && _areaPane.Shown != null) _areaPane.Tint(pay.Tint(), state, sp.Skin);
            if (_verdict != null) _verdict.text = Verdict(sp);
        }

        static void KeepScroll(RectTransform col, System.Action build)
        {
            float keep = col.anchoredPosition.y;
            CreatorScreen.Clear(col);
            build();
            col.anchoredPosition = new Vector2(col.anchoredPosition.x, keep);
        }

        // ------------------------------------------------------------- list --
        void SpellList(RectTransform col, float W, SpellBook book) => KeepScroll(col, () =>
        {
            CreatorUI.Note(col, W, Loc.T("mc.spells.note"), 46f);
            var row = CreatorUI.Row(col, W);
            UIKit.Button(row, Loc.T("mc.spell.new"), () =>
            {
                book.spells.Add(new SpellDef { Name = Loc.T("mc.spell.new") });
                PickSpell(book.spells.Count - 1);
            }, CreatorUI.Pick(true), 14);
            if (CurrentSpell != null)
                UIKit.Button(row, Loc.T("creator.delete"), () =>
                {
                    book.spells.RemoveAt(_spell);
                    PickSpell(FirstSpell());
                }, CreatorUI.Red, 14);
            var names = new List<string>();
            foreach (var x in book.spells) names.Add(SpellLabel(x));
            CreatorUI.Grid(col, W, 1, names, i => i == _spell, PickSpell, 30f, 13);
        });

        void PickSpell(int i)
        {
            _spell = i;
            _sureShape = _sureArea = _summonWanted = false;
            RefreshSpells();
        }

        // ---------------------------------------------------- preview, shape --
        /// The spell's blob in the pane wearing its saved shape (one named like
        /// the spell when it names none), or a summon's creature as it stands
        /// up; shown again when either changes.
        void ShowSpellBody(SpellDef sp, SpellBook book)
        {
            if (_spellPane == null) return;
            var raised = sp != null && sp.IsSummon ? book.Creature(sp.Creature) : null;
            bool summon = sp != null && sp.IsSummon;
            if (sp == _shownSpell && summon == _shownAsSummon && raised == _shownSummoned
                && (raised == null || raised.Body == _shownSummonedBody)) return;
            _shownSpell = sp;
            _shownAsSummon = summon;
            _shownSummoned = raised;
            if (sp == null) { _spellPane.Clear(); return; }
            if (sp.IsSummon)
            {
                _spellPane.Posable = false;
                if (raised == null) { _spellPane.Clear(); return; }
                _shownSummonedBody = raised.Body;
                _spellPane.Show(SpellDef.BodyPrefab(raised.Body));
                if (_spellPane.Shown != null)
                {
                    CreatureLook.Proportion(_spellPane.Shown.transform, raised);
                    CreatureLook.Shape(_spellPane.Shown, raised);
                }
                return;
            }
            _spellPane.Posable = true;
            _spellPane.Show(SpellDef.BodyPrefab(SpellBody.Particle));
            var data = book.Shape(string.IsNullOrEmpty(sp.Shape) ? sp.Name : sp.Shape);
            if (data == null) return;
            _spellPane.ApplyPose(data);
            if (sp.Skin == null && data.Look != null) sp.Skin = CopyLook(data.Look);
        }

        static SpellTable.Look CopyLook(SpellTable.Look look) =>
            JsonUtility.FromJson<SpellTable.Look>(JsonUtility.ToJson(look));

        /// Shapes are a library: load any saved pose onto the spell and keep
        /// editing, save the pose under any name, prune the ones you don't want.
        void SpellShapeTools(SpellDef sp, SpellBook book)
        {
            CreatorScreen.Clear(_spellShape);
            if (sp == null || sp.IsSummon) return;
            var b = _spellShape;
            float W = _screen.Widths[1] - 12f;
            if (sp != _shapeNameFor)
            {
                _shapeNameFor = sp;
                _shapeName = string.IsNullOrEmpty(sp.Shape) ? sp.Name : sp.Shape;
            }
            CreatorUI.Head(b, W, Loc.T("mc.spellshape"));
            var shapes = new List<ShapeDef>();
            foreach (var x in book.shapes) if (x != null && !string.IsNullOrEmpty(x.Name)) shapes.Add(x);
            shapes.Sort((x, y) => string.Compare(x.Name, y.Name, System.StringComparison.Ordinal));
            var names = new List<string> { Loc.T("mc.none") };
            foreach (var x in shapes) names.Add(x.Name);
            Dropdown(b, W, "spell:shape", Loc.T("mc.shape.wearing"), string.IsNullOrEmpty(sp.Shape) ? Loc.T("mc.none") : sp.Shape, names,
                i => i == 0 ? string.IsNullOrEmpty(sp.Shape) : string.Equals(shapes[i - 1].Name, sp.Shape, System.StringComparison.OrdinalIgnoreCase),
                i =>
                {
                    _sureShape = false;
                    if (i == 0)
                    {
                        sp.Shape = "";
                        _shownSpell = null; // the plain body again
                        return;
                    }
                    var pick = shapes[i - 1];
                    sp.Shape = pick.Name;
                    _shapeName = pick.Name;
                    _spellPane?.ApplyPose(pick);
                    if (pick.Look != null) sp.Skin = CopyLook(pick.Look);
                }, RefreshSpells);

            CreatorUI.Field(b, W, Loc.T("mc.name"), _shapeName, v => _shapeName = v);
            var row = CreatorUI.Row(b, W);
            UIKit.Button(row, Loc.T("mc.shape.save"), () => SaveShape(sp, book), CreatorUI.Pick(true), 14);
            if (!string.IsNullOrEmpty(sp.Shape) && book.Shape(sp.Shape) != null)
                UIKit.Button(row, _sureShape ? Loc.T("maps.delete.sure") : Loc.T("mc.shape.delete"), () =>
                {
                    if (!_sureShape) { _sureShape = true; RefreshSpells(); return; }
                    _sureShape = false;
                    DeleteShape(sp.Shape, book);
                }, CreatorUI.Red, 14);
        }

        /// The bones as they stand and the material sliders, into the map's book under the typed name.
        void SaveShape(SpellDef sp, SpellBook book)
        {
            string name = _shapeName != null ? _shapeName.Trim() : "";
            if (_spellPane == null || _spellPane.Shown == null || name.Length == 0) return;
            var def = _spellPane.CapturePose(name);
            if (sp.Skin != null) def.Look = CopyLook(sp.Skin);
            book.shapes.RemoveAll(x => x != null && string.Equals(x.Name, name, System.StringComparison.OrdinalIgnoreCase));
            book.shapes.Add(def);
            sp.Shape = name;
            _sureShape = false;
            RefreshSpells();
        }

        /// Gone from the book; every spell wearing it falls back to the plain body.
        void DeleteShape(string name, SpellBook book)
        {
            book.shapes.RemoveAll(x => x != null && string.Equals(x.Name, name, System.StringComparison.OrdinalIgnoreCase));
            foreach (var x in book.spells)
                if (string.Equals(x.Shape, name, System.StringComparison.OrdinalIgnoreCase)) x.Shape = "";
            _shapeName = "";
            _shapeNameFor = null;
            _shownSpell = null;
            RefreshSpells();
        }

        // ---------------------------------------------------------- numbers --
        void SpellNumbers(RectTransform col, float W, SpellDef sp, SpellBook book) => KeepScroll(col, () =>
        {
            _verdict = null;
            if (sp == null)
            {
                CreatorUI.Note(col, W, Loc.T("mc.spell.none"), 30f);
                return;
            }
            CreatorUI.Field(col, W, Loc.T("mc.name"), sp.Name, v =>
            {
                sp.Name = v;
                SpellList(_screen.Columns[0], _screen.Widths[0] - 12f, book);
            });
            CreatorUI.Switch(col, W, Loc.T("mc.spell.book"),
                new[] { Loc.T("mc.side.wizard"), Loc.T("mc.side.acolyte") }, (int)sp.Book,
                i => { sp.Book = (BookKind)i; RefreshSpells(); });
            CreatorUI.Switch(col, W, Loc.T("mc.spell.kind"),
                new[] { Loc.T("mc.kind.spell"), Loc.T("mc.kind.summon") }, sp.IsSummon ? 1 : 0, i =>
                {
                    if (i == 0) sp.Creature = "";
                    else if (!sp.IsSummon)
                    {
                        // a summon needs a creature to raise
                        _summonWanted = book.creatures.Count == 0;
                        if (book.creatures.Count > 0) sp.Creature = book.creatures[0].Name;
                    }
                    RefreshSpells();
                });

            if (sp.IsSummon) SummonNumbers(col, W, sp, book);
            else
            {
                if (_summonWanted) CreatorUI.Note(col, W, Loc.T("mc.summons.none"), 30f);
                ParticleNumbers(col, W, sp);
            }
        });

        /// A summon: the creature it raises, and the runes a seal needs for it.
        void SummonNumbers(RectTransform col, float W, SpellDef sp, SpellBook book)
        {
            var names = new List<string>();
            foreach (var c in book.creatures) names.Add(c.Name);
            var raised = book.Creature(sp.Creature);
            Dropdown(col, W, "spell:summons", Loc.T("mc.summons"), raised != null ? raised.Name : Loc.T("mc.none"), names,
                i => book.creatures[i].Name == sp.Creature,
                i => sp.Creature = book.creatures[i].Name, RefreshSpells);
            if (raised != null)
                CreatorUI.Do(col, W, Loc.T("mc.creature.edit"), () => OpenCreature(raised.Name), CreatorUI.Pick(false), 28f);
            SummonedBy(col, W, sp, book, "spell:summoned", RefreshSpells);
        }

        /// Runes raise a summon's creature: which ones a seal must hold, counted.
        void SummonedBy(RectTransform col, float W, SpellDef sp, SpellBook book, string key, System.Action refresh)
        {
            if (Section(col, W, key, Loc.T("mc.summonedby"), true, refresh))
            {
                CreatorUI.Note(col, W, Loc.T("mc.note.summoned"), 34f);
                var summon = new List<string>();
                var have = new List<RuneType>();
                foreach (var r in sp.Runes)
                {
                    if (have.Contains(r)) continue;
                    have.Add(r);
                    int count = 0;
                    foreach (var q in sp.Runes) if (q == r) count++;
                    summon.Add((book.Rune(r)?.Name ?? r.ToString()) + (count > 1 ? " x" + count : "") + "  -");
                }
                if (summon.Count == 0) CreatorUI.Note(col, W, Loc.T("mc.summon.none"), 18f);
                else CreatorUI.Grid(col, W, 3, summon, null, i => { sp.Runes.Remove(have[i]); refresh(); }, 26f, 12);
                var runeNames = new List<string>();
                foreach (var r in book.runes) runeNames.Add(r.Name);
                Dropdown(col, W, key + ":addrune", Loc.T("mc.summon.addrune"), "", runeNames, null,
                    i => sp.Runes.Add(book.runes[i].Type), refresh, stayOpen: true);
                var row = CreatorUI.Row(col, W, 28f);
                UIKit.Button(row, Loc.T("mc.summon.all"), () =>
                {
                    sp.Runes.Clear();
                    foreach (var r in book.runes) if (r.BuiltIn) sp.Runes.Add(r.Type);
                    refresh();
                }, CreatorUI.Pick(false), 12);
                UIKit.Button(row, Loc.T("mc.clear"), () => { sp.Runes.Clear(); refresh(); }, CreatorUI.Pick(false), 12);
            }
        }

        void BornAs(RectTransform col, float W, int[] axes, string key, System.Action refresh)
        {
            if (Section(col, W, key, Loc.T("mc.bornas"), true, refresh))
            {
                CreatorUI.Note(col, W, Loc.T("mc.note.bornas"), 34f);
                for (int i = 0; i < SpellPayload.AxisCount; i++) AxisRow(col, W, axes, i, null, creature: true);
            }
        }

        /// A creature casts the spells ticked here in turn, a summon raising its
        /// creature; a zombie body charges when told, a golem always does.
        void CanDo(RectTransform col, float W, List<string> abilities, SpellBody body, SpellBook book, string key, System.Action refresh)
        {
            if (Section(col, W, key, Loc.T("mc.cando"), true, refresh))
            {
                if (body == SpellBody.Golem) CreatorUI.Note(col, W, Loc.T("mc.note.golemcharge"), 18f);
                else
                {
                    bool charges = abilities.Contains(Zombie.Charge);
                    CreatorUI.Switch(col, W, Loc.T("mc.charge"), new[] { Loc.T("opt.off"), Loc.T("opt.on") }, charges ? 1 : 0, i =>
                    {
                        abilities.Remove(Zombie.Charge);
                        if (i == 1) abilities.Add(Zombie.Charge);
                        refresh();
                    });
                }
                var castNames = new List<string>();
                int casting = 0;
                foreach (var c in book.spells) { castNames.Add(SpellLabel(c)); if (abilities.Contains(c.Name)) casting++; }
                Dropdown(col, W, key + ":casts", Loc.T("mc.casts"), casting.ToString(), castNames,
                    i => abilities.Contains(book.spells[i].Name),
                    i => { if (!abilities.Remove(book.spells[i].Name)) abilities.Add(book.spells[i].Name); },
                    refresh, stayOpen: true);
                var row = CreatorUI.Row(col, W, 28f);
                UIKit.Button(row, Loc.T("mc.casts.all"), () =>
                {
                    foreach (var c in book.spells) if (!abilities.Contains(c.Name)) abilities.Add(c.Name);
                    refresh();
                }, CreatorUI.Pick(false), 12);
                UIKit.Button(row, Loc.T("mc.clear"), () =>
                {
                    abilities.RemoveAll(a => a != Zombie.Charge);
                    refresh();
                }, CreatorUI.Pick(false), 12);
            }
        }

        void ParticleNumbers(RectTransform col, float W, SpellDef sp)
        {
            if (Section(col, W, "spell:conditions", Loc.T("mc.conditions"), true, RefreshSpells))
            {
                CreatorUI.Note(col, W, Loc.T("mc.note.conditions"), 34f);
                for (int i = 0; i < 6; i++) AxisRow(col, W, sp.Axis, i, a => sp.BiomeAxis[a] = false);
                CreatorUI.Note(col, W, Loc.T("mc.places"), 18f);
                var axisNames = new List<string>();
                for (int i = 0; i < 6; i++) axisNames.Add(Loc.T("axis." + i));
                CreatorUI.Grid(col, W, 3, axisNames, i => sp.BiomeAxis[i] && sp.Axis[i] != 0, i =>
                {
                    if (sp.Axis[i] == 0) return; // nothing there to lock
                    sp.BiomeAxis[i] = !sp.BiomeAxis[i];
                    RefreshSpells();
                }, 24f, 11);
            }

            if (Section(col, W, "spell:effects", Loc.T("mc.effects"), false, RefreshSpells))
            {
                CreatorUI.Note(col, W, Loc.T("mc.note.effects"), 34f);
                for (int i = 6; i < SpellPayload.AxisCount; i++) AxisRow(col, W, sp.Axis, i, null);
                CreatorUI.Switch(col, W, Loc.T("mc.onlyliving"), new[] { Loc.T("opt.off"), Loc.T("opt.on") }, sp.OnlyLiving ? 1 : 0,
                    i => { sp.OnlyLiving = i == 1; RefreshSpells(); });
            }
            _verdict = CreatorUI.Note(col, W, Verdict(sp), 92f);
        }

        /// One axis in its own units; `zeroed` hears when it goes to zero (a condition cannot stay a place).
        void AxisRow(RectTransform b, float W, int[] axes, int i, System.Action<int> zeroed, bool creature = false)
        {
            int lo, hi;
            if (creature) SpellPayload.CreatureRange(i, out lo, out hi);
            else SpellPayload.SpellRange(i, out lo, out hi);
            int axis = i;
            CreatorUI.Number(b, W, Loc.T("axis." + axis), lo, hi, axes[axis], true, v =>
            {
                axes[axis] = Mathf.RoundToInt(v);
                if (axes[axis] == 0) zeroed?.Invoke(axis);
            }, v => Loc.T("axis." + axis) + ": " + Mathf.RoundToInt(v) + SpellPayload.UnitName(axis));
        }

        /// What was authored, said back plainly - the Spell Crafter's own lines.
        static string Verdict(SpellDef sp)
        {
            bool any = false;
            for (int i = 0; i < SpellPayload.AxisCount; i++) if (sp.Axis[i] != 0) { any = true; break; }
            if (!any) return Loc.T("mc.verdict.nothing");
            var lines = new List<string> { Loc.T("mc.verdict.reach") };
            if (sp.HasAoe) lines.Add(Loc.T("mc.verdict.area"));
            if (sp.AnyBiome) lines.Add(Loc.T("mc.verdict.locked"));
            else if (!sp.HasAoe) lines.Add(Loc.T("mc.verdict.spends"));
            lines.Add(sp.Physical ? Loc.T("mc.verdict.physical") : Loc.T("mc.verdict.ghost"));
            return string.Join("\n", lines);
        }

        // ------------------------------------------------- area and material --
        void SpellAreaAndLook(RectTransform col, float W, SpellDef sp, SpellBook book) => KeepScroll(col, () =>
        {
            if (sp == null) return;
            if (sp.IsSummon)
            {
                CreatorUI.Note(col, W, Loc.T("mc.note.summonlook"), 34f);
                return;
            }
            CreatorUI.Head(col, W, Loc.T("mc.area"));
            CreatorUI.Note(col, W, Loc.T("mc.note.area"), 34f);
            AreaPicker(col, W, book, "spell:area", sp.Name, () => sp.Aoe, v => sp.Aoe = v,
                a => OpenArea(a, Loc.T("mc.back.spell"), RefreshSpells), RefreshSpells);
            if (sp.Skin == null) sp.Skin = new SpellTable.Look();
            MaterialSection(col, W, sp.Skin, "spell:material", RefreshSpells);
        });

        /// The area it carries: pick one of the map's, make a new one named after
        /// `owner`, open it on its own screen.
        void AreaPicker(RectTransform col, float W, SpellBook book, string key, string owner,
            System.Func<string> get, System.Action<string> set, System.Action<AoeDef> open, System.Action refresh)
        {
            string wearing = get() ?? "";
            var areas = new List<string> { Loc.T("mc.none") };
            foreach (var a in book.aoes) areas.Add(a.Name);
            Dropdown(col, W, key, Loc.T("mc.area"), wearing.Length == 0 ? Loc.T("mc.none") : wearing, areas,
                i => i == 0 ? wearing.Length == 0 : book.aoes[i - 1].Name == wearing,
                i => set(i == 0 ? "" : book.aoes[i - 1].Name), refresh);
            var row = CreatorUI.Row(col, W, 34f);
            UIKit.Button(row, Loc.T("mc.area.new"), () =>
            {
                string stem = Loc.F("mc.area.named", owner), name = stem;
                for (int n = 2; book.Aoe(name) != null; n++) name = stem + " " + n;
                var a = new AoeDef { Name = name };
                book.aoes.Add(a);
                set(name);
                refresh();
                open(a);
            }, CreatorUI.Pick(true), 14);
            var current = wearing.Length > 0 ? book.Aoe(wearing) : null;
            if (current != null)
                UIKit.Button(row, Loc.T("mc.area.open"), () => open(current), CreatorUI.Pick(false), 14);
        }

        void MaterialSection(RectTransform col, float W, SpellTable.Look look, string key, System.Action refresh)
        {
            if (Section(col, W, key, Loc.T("mc.material"), false, refresh))
            {
                CreatorUI.Note(col, W, Loc.T("mc.note.material"), 18f);
                foreach (var (lookKey, get, set, min, max) in LookRows)
                    CreatorUI.Number(col, W, Loc.T(lookKey), min, max, get(look), false, v => set(look, v));
            }
        }

        // ------------------------------------------------------ area screen --
        /// ★ An area takes a screen of its own on top: its look, rendered and
        /// coloured by the spell, and the few things an area owns.
        void OpenArea(AoeDef aoe, string back, System.Action closed)
        {
            if (_screen == null || aoe == null) return;
            if (_areaScreen != null) { _areaScreen.Closed = null; _areaScreen.Close(); }
            _areaName = aoe.Name;
            _areaClosed = closed;
            _sureArea = false;
            _areaScreen = CreatorScreen.Create(_ui, "AreaScreen", Loc.T("mc.area"), back, 620f, 0f);
            var left = _areaScreen.Columns[0];
            float LW = _areaScreen.Widths[0] - 12f;
            _areaPane = PreviewPane.Create(left, LW);
            _areaPane.Posable = true;
            CreatorUI.Note(left, LW, Loc.T("mc.note.areatint"), 20f);
            _areaNoLook = CreatorUI.Note(left, LW, Loc.T("mc.area.nolook"), 30f);
            _shownAreaLook = null;
            _areaScreen.Closed = () =>
            {
                _areaScreen = null;
                _areaPane = null;
                _areaNoLook = null;
                _areaName = null;
                var then = _areaClosed;
                _areaClosed = null;
                then?.Invoke();
            };
            RefreshArea();
        }

        void RefreshArea()
        {
            if (_areaScreen == null) return;
            var book = SpellBook.Live;
            var aoe = book.Aoe(_areaName);
            if (aoe == null) { _areaScreen.Close(); return; } // gone (undone or deleted)
            _areaScreen.Title.text = Loc.T("mc.area") + ": " + aoe.Name;

            var look = aoe.Prefab;
            if (look != _shownAreaLook)
            {
                _shownAreaLook = look;
                if (look != null) _areaPane.Show(look); else _areaPane.Clear();
            }
            if (_areaNoLook != null) _areaNoLook.gameObject.SetActive(look == null);

            var col = _areaScreen.Columns[1];
            float W = _areaScreen.Widths[1] - 12f;
            KeepScroll(col, () =>
            {
                CreatorUI.Field(col, W, Loc.T("mc.name"), aoe.Name, v =>
                {
                    foreach (var x in book.spells) if (x.Aoe == aoe.Name) x.Aoe = v;
                    foreach (var x in book.creatures) if (x.Aoe == aoe.Name) x.Aoe = v;
                    aoe.Name = v;
                    _areaName = v;
                    aoe.ForgetPrefab();
                    _areaScreen.Title.text = Loc.T("mc.area") + ": " + v;
                });

                // the look, from the looks the game ships
                var looks = CollectionManager.AreaLookNames();
                string wearing = !string.IsNullOrEmpty(aoe.Look) ? aoe.Look
                    : CollectionManager.AreaLookFor(aoe.Name) != null ? aoe.Name : "";
                var lookNames = new List<string> { Loc.T("mc.none") };
                lookNames.AddRange(looks);
                Dropdown(col, W, "area:look", Loc.T("mc.area.look"), wearing.Length == 0 ? Loc.T("mc.none") : wearing, lookNames,
                    i => i == 0 ? wearing.Length == 0 : string.Equals(looks[i - 1], wearing, System.StringComparison.OrdinalIgnoreCase),
                    i => { aoe.Look = i == 0 ? "" : looks[i - 1]; aoe.ForgetPrefab(); }, RefreshArea);
                CreatorUI.Note(col, W, Loc.T("mc.note.look"), 18f);

                var loadable = new List<SpellDef>();
                var spellNames = new List<string> { Loc.T("mc.none") };
                foreach (var x in book.spells) if (!x.IsSummon) { loadable.Add(x); spellNames.Add(x.Name); }
                Dropdown(col, W, "area:spell", Loc.T("mc.area.loadspell"),
                    string.IsNullOrEmpty(aoe.Spell) ? Loc.T("mc.none") : aoe.Spell, spellNames,
                    i => i == 0 ? string.IsNullOrEmpty(aoe.Spell) : loadable[i - 1].Name == aoe.Spell,
                    i => aoe.Spell = i == 0 ? "" : loadable[i - 1].Name, RefreshArea);
                CreatorUI.Note(col, W, Loc.T("mc.note.loadspell"), 34f);

                CreatorUI.Number(col, W, Loc.T("mc.area.trailwidth"), 0f, 1f, aoe.TrailWidth, false, v => aoe.TrailWidth = v);
                CreatorUI.Number(col, W, Loc.T("mc.area.traillasts"), 0f, 20f, aoe.TrailSeconds, false, v => aoe.TrailSeconds = v);

                CreatorUI.Head(col, W, Loc.T("mc.area.start"));
                CreatorUI.Note(col, W, Loc.T("mc.note.start"), 34f);
                CreatorUI.Number(col, W, "X", -30f, 30f, aoe.Offset.x, false, v => aoe.Offset.x = v);
                CreatorUI.Number(col, W, "Y", -10f, 40f, aoe.Offset.y, false, v => aoe.Offset.y = v);
                CreatorUI.Number(col, W, "Z", -30f, 30f, aoe.Offset.z, false, v => aoe.Offset.z = v);
                CreatorUI.Number(col, W, Loc.T("mc.area.arrive"), 0f, 20f, aoe.ArriveSeconds, false, v => aoe.ArriveSeconds = v);
                CreatorUI.Note(col, W, Loc.T("mc.note.arrive"), 34f);

                CreatorUI.Switch(col, W, Loc.T("mc.area.spreading"), new[] { Loc.T("opt.off"), Loc.T("opt.on") }, aoe.Spreading ? 1 : 0,
                    i => { aoe.Spreading = i == 1; RefreshArea(); });
                CreatorUI.Note(col, W, Loc.T("mc.note.spreading"), 18f);

                CreatorUI.Do(col, W, _sureArea ? Loc.T("maps.delete.sure") : Loc.T("mc.area.delete"), () =>
                {
                    if (!_sureArea) { _sureArea = true; RefreshArea(); return; }
                    _sureArea = false;
                    book.aoes.Remove(aoe);
                    foreach (var x in book.spells) if (x.Aoe == aoe.Name) x.Aoe = "";
                    foreach (var x in book.creatures) if (x.Aoe == aoe.Name) x.Aoe = "";
                    _areaScreen.Close();
                }, CreatorUI.Red, 34f);
            });
        }
    }
}
