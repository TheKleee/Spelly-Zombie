using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ THE BOOTH'S WINDOWS, the Map Creator's kind: Add (what can be placed),
    /// Objects (everything placed, by name), the picked thing's own window, Ink,
    /// Light and look, and Photo (the name, the folder, the canvas, what is
    /// behind, where it is taken).
    public partial class PhotoBooth
    {
        RectTransform _ui;
        CreatorWindow _winAdd, _winItem, _winInk, _winLook, _winPhoto, _winAnims, _winList;
        readonly HashSet<string> _open = new HashSet<string>();
        string _newFolder = "";

        CreatorWindow[] AllWindows() => new[] { _winAdd, _winItem, _winInk, _winLook, _winPhoto, _winAnims, _winList };

        void BuildUI()
        {
            if (_ui != null) UIKit.Retire(_ui);
            _ui = UIKit.Group(UIKit.Root, "PhotoBoothUI");
            UIKit.Stretch(_ui);
            BuildOverlay(_ui);
            BuildRibbon();

            float right = Mathf.Max(820f, _ui.rect.width - 420f);
            _winAdd = CreatorWindow.Create(_ui, "WinAdd", Loc.T("photo.add"), new Vector2(16f, -104f), 340f, 620f);
            _winPhoto = CreatorWindow.Create(_ui, "WinPhoto", Loc.T("photo.photo"), new Vector2(16f, -104f), 360f, 660f);
            _winInk = CreatorWindow.Create(_ui, "WinInk", Loc.T("photo.ink"), new Vector2(372f, -104f), 320f, 540f);
            _winLook = CreatorWindow.Create(_ui, "WinLook", Loc.T("photo.look"), new Vector2(372f, -104f), 360f, 660f);
            _winItem = CreatorWindow.Create(_ui, "WinItem", "", new Vector2(right, -104f), 400f, 700f);
            _winAnims = CreatorWindow.Create(_ui, "WinAnims", Loc.T("photo.anims"), new Vector2(right - 336f, -104f), 320f, 560f);
            _winAnims.Hide();
            _winList = CreatorWindow.Create(_ui, "WinList", Loc.T("photo.list"), new Vector2(372f, -104f), 300f, 560f);
            _winList.Hide();
            _winAnims.Closed = () => { StopPlaying(true); BuildItemWindow(); };
            _winPhoto.Hide();
            _winInk.Hide();
            _winLook.Hide();
            _winItem.Hide();
            _winItem.Closed = () => { if (_sel != null) Select(null); };
            _winInk.Closed = () => { if (_tool == Tool.Ink) EndTool(); };
            BuildAllWindows();
            if (_sel != null) { _winItem.Show(); BuildItemWindow(); }
        }

        void BuildAllWindows()
        {
            BuildAddWindow();
            BuildItemWindow();
            BuildInkWindow();
            BuildLookWindow();
            BuildPhotoWindow();
            BuildAnimWindow();
            BuildListWindow();
        }

        /// A language change mid-edit builds every word again; each window keeps
        /// its place, its stacking and whether it was open.
        void Relabel()
        {
            if (!Active || _ui == null) return;
            int uiOrder = _ui.GetSiblingIndex();
            var was = AllWindows();
            var kept = new (Vector2 at, bool open, int order)[was.Length];
            for (int i = 0; i < was.Length; i++)
                if (was[i] != null)
                    kept[i] = (((RectTransform)was[i].transform).anchoredPosition, was[i].Visible, was[i].transform.GetSiblingIndex());
            BuildUI();
            _ui.SetSiblingIndex(uiOrder); // an open pause menu stays on top
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
            BuildAllWindows();
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
            UIKit.Place(rt, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(800f, 58f));
            var row = UIKit.Group(rt, "RibbonRow");
            UIKit.Stretch(row);
            row.offsetMin = new Vector2(12f, 10f);
            row.offsetMax = new Vector2(-12f, -10f);
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 6f;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = hl.childForceExpandHeight = true;

            void Tab(string label, System.Func<CreatorWindow> win, System.Action build) =>
                UIKit.Button(row, label, () =>
                {
                    var w = win();
                    w.Toggle();
                    if (w.Visible) build();
                }, CreatorUI.Pick(false), 15);
            Tab(Loc.T("photo.add"), () => _winAdd, BuildAddWindow);
            Tab(Loc.T("photo.list"), () => _winList, BuildListWindow);
            Tab(Loc.T("photo.ink"), () => _winInk, BuildInkWindow);
            Tab(Loc.T("photo.look"), () => _winLook, BuildLookWindow);
            Tab(Loc.T("photo.photo"), () => _winPhoto, BuildPhotoWindow);

            // leaving on the left, the shutter on the right: neither is a window
            Corner(new Vector2(0f, 1f), new Vector2(8f, -8f), Loc.T("mc.back.menu"), Exit, CreatorUI.Red);
            Corner(new Vector2(1f, 1f), new Vector2(-8f, -8f), Loc.T("photo.take"), TakePhoto, CreatorUI.Pick(true));

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

        static float Keep(CreatorWindow w) => w.Body.anchoredPosition.y;
        static void Restore(CreatorWindow w, float y) => w.Body.anchoredPosition = new Vector2(w.Body.anchoredPosition.x, y);

        /// The controls line with the letters printed on this keyboard.
        static string HelpText()
        {
            var kb = Keyboard.current;
            string keys = kb == null ? "WASD"
                : MapCreator.KeyName(kb.wKey, "W") + MapCreator.KeyName(kb.aKey, "A")
                + MapCreator.KeyName(kb.sKey, "S") + MapCreator.KeyName(kb.dKey, "D");
            return Loc.F("photo.help", keys);
        }

        // --------------------------------------------------------- add window --
        void BuildAddWindow()
        {
            var w = _winAdd;
            if (w == null) return;
            float keep = Keep(w);
            w.Clear();
            var b = w.Body;
            float W = w.Width;
            CreatorUI.Note(b, W, Loc.T("photo.add.note"), 30f);
            var book = SpellBook.Live;

            if (CreatorUI.Section(b, W, _open, "add:ground", Loc.T("photo.ground"), false, BuildAddWindow))
            {
                CreatorUI.Grid(b, W, 2, new List<string> { Loc.T("mc.none"), Loc.T("photo.ground.island") },
                    i => (i == 1) == Editing.Island, i =>
                    {
                        SetIsland(i == 1, false);
                        BuildAddWindow();
                    }, 28f, 12);
                if (Editing.Island) CreatorUI.Do(b, W, Loc.T("photo.ground.again"), () => SetIsland(true, true), null, 30f);
            }

            if (CreatorUI.Section(b, W, _open, "add:characters", Loc.T("photo.add.characters"), true, BuildAddWindow))
                Picks(b, W, new List<PhotoDef.Item> { Character(false), Character(true) });

            if (CreatorUI.Section(b, W, _open, "add:creatures", Loc.T("photo.add.creatures"), false, BuildAddWindow))
            {
                var list = new List<PhotoDef.Item> { Plain(SpellBody.Zombie), Plain(SpellBody.Golem) };
                foreach (var cr in book.creatures) if (cr != null && !string.IsNullOrEmpty(cr.Name)) list.Add(Creature(cr));
                Picks(b, W, list);
            }

            if (CreatorUI.Section(b, W, _open, "add:spells", Loc.T("mc.spells"), false, BuildAddWindow))
            {
                var list = new List<PhotoDef.Item>();
                foreach (var sp in book.spells)
                    if (sp != null && !sp.IsSummon && !string.IsNullOrEmpty(sp.Name)) list.Add(Thing(PhotoKind.Spell, sp.Name, 0f));
                Picks(b, W, list);
            }

            if (CreatorUI.Section(b, W, _open, "add:areas", Loc.T("photo.add.areas"), false, BuildAddWindow))
            {
                var list = new List<PhotoDef.Item>();
                foreach (var aoe in book.aoes)
                    if (aoe != null && !string.IsNullOrEmpty(aoe.Name) && aoe.Prefab != null) list.Add(Thing(PhotoKind.Area, aoe.Name, 1f));
                Picks(b, W, list);
            }

            if (CreatorUI.Section(b, W, _open, "add:effects", Loc.T("photo.add.effects"), false, BuildAddWindow))
            {
                var list = new List<PhotoDef.Item>();
                foreach (var n in EffectNames()) list.Add(Thing(PhotoKind.Effect, n, 0.4f));
                Picks(b, W, list);
            }

            if (CreatorUI.Section(b, W, _open, "add:pieces", Loc.T("photo.add.pieces"), false, BuildAddWindow))
                foreach (var g in MapPalette.Groups)
                {
                    var pieces = g.Pieces;
                    string name = string.IsNullOrEmpty(g.Name) ? Loc.T("photo.add.inside") : g.Name;
                    CreatorUI.Dropdown(b, W, _open, "add:group:" + g.Name, name, "", pieces,
                        i => IsPick(Thing(PhotoKind.Piece, pieces[i], 0f)),
                        i => PickUp(Thing(PhotoKind.Piece, pieces[i], 0f)), BuildAddWindow, stayOpen: true);
                }
            Restore(w, keep);
        }

        void Picks(RectTransform b, float W, List<PhotoDef.Item> items)
        {
            if (items.Count == 0) { CreatorUI.Note(b, W, Loc.T("photo.add.empty"), 22f); return; }
            var names = items.ConvertAll(PickName);
            CreatorUI.Grid(b, W, 2, names, i => IsPick(items[i]), i =>
            {
                PickUp(items[i]);
                BuildAddWindow();
            }, 28f, 12);
        }

        // ------------------------------------------------------ objects window --
        /// ★ EVERYTHING PLACED, by name (his ask): picked here to change it, however it stands
        /// among the others and without moving anything to reach it.
        void BuildListWindow()
        {
            var w = _winList;
            if (w == null) return;
            float keep = Keep(w);
            w.Clear();
            var b = w.Body;
            float W = w.Width;
            CreatorUI.Note(b, W, Loc.T("photo.list.note"), 44f);
            if (_subjects.Count == 0) CreatorUI.Note(b, W, Loc.T("photo.add.empty"), 22f);
            else
            {
                // the same name twice reads with a number
                var names = new List<string>(_subjects.Count);
                var count = new Dictionary<string, int>();
                foreach (var s in _subjects)
                {
                    string n = PickName(s.Item);
                    count.TryGetValue(n, out int k);
                    count[n] = ++k;
                    names.Add(k > 1 ? n + " " + k : n);
                }
                // picked again here: the eye goes to it
                CreatorUI.Grid(b, W, 1, names, i => i < _subjects.Count && _subjects[i] == _sel, i =>
                {
                    if (i >= _subjects.Count) return;
                    if (_subjects[i] == _sel) FindSelected();
                    else Select(_subjects[i]);
                }, 28f, 13);
            }
            Restore(w, keep);
        }

        bool IsPick(PhotoDef.Item item) =>
            _tool == Tool.Place && _pick != null && _pick.Kind == item.Kind && _pick.What == item.What
            && _pick.Body == item.Body && _pick.Acolyte == item.Acolyte;

        /// Picked in Add: the next clicks on the ground place it; picked again, placing stops.
        void PickUp(PhotoDef.Item item)
        {
            bool again = IsPick(item);
            if (_posing != null) StopPosing();
            if (_tool == Tool.Ink) { EndInk(); DropShells(); }
            _tool = again ? Tool.None : Tool.Place;
            _pick = again ? null : item;
            BuildInkWindow();
            Hint();
        }

        // -------------------------------------------------- the picked thing --
        void BuildItemWindow()
        {
            var w = _winItem;
            if (w == null) return;
            var s = _sel;
            if (s == null || s.Root == null) { if (w.Visible) w.Hide(); return; }
            float keep = Keep(w);
            w.Clear();
            var b = w.Body;
            float W = w.Width;
            var it = s.Item;
            w.Title.text = PickName(it);

            var row = CreatorUI.Row(b, W);
            UIKit.Button(row, Loc.T("photo.find"), FindSelected, CreatorUI.Pick(false), 14);
            UIKit.Button(row, Loc.T("mc.duplicate"), () => Duplicate(s), CreatorUI.Pick(false), 14);
            UIKit.Button(row, Loc.T("creator.delete"), DeleteSelected, CreatorUI.Red, 14);
            // locked: presses in the scene only move this one (lit while on)
            CreatorUI.Do(b, W, _locked ? Loc.T("photo.unlock") : Loc.T("photo.lock"), ToggleLock, CreatorUI.Pick(_locked), 32f);

            // where it stands, how it turns, how big it is
            CreatorUI.Number(b, W, "", -5f, 40f, Mathf.Clamp(LiftOf(s), -5f, 40f), false, v => Lift(s, v),
                v => Loc.T("photo.lift") + ": " + v.ToString("0.0") + " m");
            CreatorUI.Number(b, W, "", 0f, 360f, it.Yaw, true, v => Turn(s, v, it.Tilt, it.Roll),
                v => Loc.T("creator.rotate") + ": " + Mathf.RoundToInt(v) + "°");
            CreatorUI.Number(b, W, "", -90f, 90f, it.Tilt, true, v => Turn(s, it.Yaw, v, it.Roll),
                v => Loc.T("photo.tilt") + ": " + Mathf.RoundToInt(v) + "°");
            CreatorUI.Number(b, W, "", -180f, 180f, it.Roll, true, v => Turn(s, it.Yaw, it.Tilt, v),
                v => Loc.T("photo.roll") + ": " + Mathf.RoundToInt(v) + "°");
            CreatorUI.Number(b, W, "", Mathf.Log10(SizeMin), Mathf.Log10(SizeMax), Mathf.Log10(Mathf.Max(SizeMin, it.Size)), false,
                v => Resize(s, Mathf.Pow(10f, v)), v => Loc.T("mc.size") + ": " + Mathf.Pow(10f, v).ToString("0.##"));

            // its moment: effects here, animations in their own window
            if (s.Clips.Length > 0)
            {
                bool shown = _winAnims != null && _winAnims.Visible;
                CreatorUI.Do(b, W, Loc.T("photo.anims") + (string.IsNullOrEmpty(it.Clip) ? "" : ": " + it.Clip), () =>
                {
                    if (_winAnims == null) return;
                    _winAnims.Toggle();
                    if (_winAnims.Visible) BuildAnimWindow();
                    BuildItemWindow();
                }, CreatorUI.Pick(shown), 34f);
            }
            if ((s.Systems.Count > 0 || s.Ribbons.Count > 0) && s.Length > 0f)
                CreatorUI.Number(b, W, "", 0f, s.Length, Mathf.Clamp(it.Time, 0f, s.Length), false, v =>
                {
                    it.Time = v;
                    Retime(s);
                }, TimeSay);

            // an effect's or an area's parts, each on or off (lit = in the photo)
            if (s.Parts.Count > 1 && s.Body != null
                && CreatorUI.Section(b, W, _open, "item:parts", Loc.T("photo.parts"), false, BuildItemWindow))
            {
                var names = new List<string>(s.Parts.Count);
                var count = new Dictionary<string, int>();
                foreach (var t in s.Parts)
                {
                    string n = t != null ? PartName(t) : "?";
                    count.TryGetValue(n, out int k);
                    count[n] = ++k;
                    names.Add(k > 1 ? n + " " + k : n);
                }
                CreatorUI.Grid(b, W, 2, names, i => s.Parts[i] != null && PartShown(s, s.Parts[i]), i =>
                {
                    if (s.Parts[i] == null) return;
                    string path = PathOf(s.Body.transform, s.Parts[i]);
                    if (!it.Hidden.Remove(path)) it.Hidden.Add(path);
                    ApplyParts(s);
                    FitPick(s);
                    BuildItemWindow();
                }, 26f, 12);
            }

            if (it.Kind == PhotoKind.Character) CharacterRows(s, b, W);
            if (it.Kind == PhotoKind.Creature) ColorRows(s, b, W);
            if (s.Rig != null) PoseRows(s, b, W);
            if (s.Eyes != null) EyeRows(s, b, W);
            if (HasBuild(s) && CreatorUI.Section(b, W, _open, "item:body", Loc.T("photo.body"), false, BuildItemWindow))
            {
                Build(b, W, Loc.T("mc.build.height"), it.Height, v => it.Height = v, s);
                Build(b, W, Loc.T("mc.build.width"), it.Width, v => it.Width = v, s);
                Build(b, W, Loc.T("mc.build.head"), it.Head, v => it.Head = v, s);
                Build(b, W, Loc.T("mc.build.arms"), it.Arms, v => it.Arms = v, s);
                Build(b, W, Loc.T("mc.build.legs"), it.Legs, v => it.Legs = v, s);
            }
            Restore(w, keep);
        }

        static string TimeSay(float v) => Loc.T("photo.time") + ": " + v.ToString("0.00") + " s";

        // --------------------------------------------------- animations window --
        Slider _animTime;
        Text _animTimeLine;
        Button _animPlay;

        /// ★ THE PICKED THING'S ANIMATIONS (his ask: "I should be able to play all
        /// of the animations it has", in a window of their own): every one by its
        /// name, picked to play it, paused anywhere, held at any moment.
        void BuildAnimWindow()
        {
            var w = _winAnims;
            if (w == null) return;
            _animTime = null;
            _animTimeLine = null;
            _animPlay = null;
            var s = _sel;
            if (s == null || s.Root == null || s.Clips.Length == 0) { if (w.Visible) w.Hide(); return; }
            float keep = Keep(w);
            w.Clear();
            var b = w.Body;
            float W = w.Width;
            var it = s.Item;
            w.Title.text = Loc.T("photo.anims") + ": " + PickName(it);
            CreatorUI.Note(b, W, Loc.T("photo.anim.note"), 30f);
            var labels = new List<string> { Loc.T("mc.none") };
            labels.AddRange(s.ClipNames);
            CreatorUI.Grid(b, W, 2, labels, i => i == 0 ? string.IsNullOrEmpty(it.Clip) : s.ClipNames[i - 1] == it.Clip, i =>
            {
                if (i == 0) { StopPlaying(false); Relax(s); BuildItemWindow(); BuildAnimWindow(); return; }
                PlayClip(s, s.ClipNames[i - 1]);
            }, 28f, 13);
            var clip = ClipOf(s);
            if (clip != null)
            {
                bool playing = _playing == s;
                _animPlay = CreatorUI.Do(b, W, playing ? Loc.T("photo.anim.pause") : Loc.T("photo.anim.play"), () =>
                {
                    if (_playing == s) StopPlaying(true);
                    else StartPlaying(s);
                }, CreatorUI.Pick(playing), 36f);
                _animTime = CreatorUI.Number(b, W, "", 0f, Mathf.Max(0.01f, clip.length), Mathf.Clamp(it.Time, 0f, clip.length), false, v =>
                {
                    // a hand on the slider holds it; the window stays, so the drag goes on
                    if (_playing == s) { _playing = null; ShowPlaying(false); }
                    it.Time = v;
                    Retime(s);
                }, TimeSay, out _animTimeLine);
            }
            Restore(w, keep);
        }

        /// The play button says what a click does next.
        void ShowPlaying(bool playing)
        {
            if (_animPlay == null) return;
            var label = _animPlay.GetComponentInChildren<Text>();
            if (label != null) label.text = playing ? Loc.T("photo.anim.pause") : Loc.T("photo.anim.play");
            if (_animPlay.image != null) _animPlay.image.sprite = CreatorUI.Pick(playing);
        }

        void Build(RectTransform b, float W, string name, float value, System.Action<float> set, Subject s) =>
            CreatorUI.Number(b, W, name, CreatureDef.BuildMin, CreatureDef.BuildMax, value, false, v =>
            {
                set(v);
                Proportions(s);
                FitPick(s);
                SettleEyes(s);
            });

        void CharacterRows(Subject s, RectTransform b, float W)
        {
            var it = s.Item;
            CreatorUI.Switch(b, W, Loc.T("photo.side"), new[] { Loc.T("mc.side.wizard"), Loc.T("mc.side.acolyte") },
                it.Acolyte ? 1 : 0, i =>
                {
                    it.Acolyte = i == 1;
                    Paint(s);
                    BuildItemWindow();
                });
            var row = CreatorUI.Row(b, W);
            UIKit.Button(row, Loc.T("photo.outfit.mine"), () =>
            {
                it.Outfit = SocketManager.LocalOutfitCode();
                Restand(s);
                BuildItemWindow();
            }, CreatorUI.Pick(false), 13);
            UIKit.Button(row, Loc.T("photo.outfit.shuffle"), () =>
            {
                it.Outfit = ShuffledOutfit();
                Restand(s);
                BuildItemWindow();
            }, CreatorUI.Pick(false), 13);

            if (CreatorUI.Section(b, W, _open, "item:hat", Loc.T("photo.hat"), false, BuildItemWindow))
            {
                Color.RGBToHSV(it.HatSet ? it.Hat : Color.white, out float h, out float sat, out float val);
                void Hat(float nh, float ns, float nv)
                {
                    it.HatSet = true;
                    it.Hat = Color.HSVToRGB(nh, ns, nv);
                    Paint(s);
                }
                CreatorUI.Number(b, W, Loc.T("photo.hat.hue"), 0f, 1f, h, false, v => { h = v; Hat(h, sat, val); }, v => Loc.T("photo.hat.hue"));
                CreatorUI.Number(b, W, Loc.T("photo.hat.sat"), 0f, 1f, sat, false, v => { sat = v; Hat(h, sat, val); }, v => Loc.T("photo.hat.sat"));
                CreatorUI.Number(b, W, Loc.T("photo.hat.light"), 0.05f, 1f, val, false, v => { val = v; Hat(h, sat, val); }, v => Loc.T("photo.hat.light"));
                CreatorUI.Do(b, W, Loc.T("photo.hat.mine"), () =>
                {
                    var saved = HatColor.Saved();
                    it.HatSet = saved != null;
                    it.Hat = saved ?? Color.white;
                    Restand(s); // a hat painted back to its own colour needs its own material again
                    BuildItemWindow();
                }, null, 30f);
            }
        }

        /// Posing by the limbs, back to standing, or a pose saved in the Pose Studio: anything with the players' skeleton.
        void PoseRows(Subject s, RectTransform b, float W)
        {
            bool posing = _posing == s;
            var pose = CreatorUI.Row(b, W, 36f);
            UIKit.Button(pose, posing ? Loc.T("photo.pose.stop") : Loc.T("photo.pose.start"), () =>
            {
                if (_posing == s) StopPosing();
                else StartPosing(s);
            }, CreatorUI.Pick(posing), 14);
            UIKit.Button(pose, Loc.T("photo.pose.relax"), () =>
            {
                Relax(s);
                BuildItemWindow();
            }, CreatorUI.Pick(false), 14);
            var poses = EmoteLibrary.Poses;
            if (poses.Count == 0) CreatorUI.Note(b, W, Loc.T("photo.pose.none"), 30f);
            else
            {
                var names = new List<string>();
                foreach (var p in poses) names.Add(p != null ? p.name : "");
                CreatorUI.Dropdown(b, W, _open, "item:poses", Loc.T("photo.pose.saved"), "", names, i => false, i =>
                {
                    var target = _sel;
                    if (target != null) WearPose(target, poses[i]);
                }, BuildItemWindow);
            }
        }

        /// A creature in any colour, or back to its own.
        void ColorRows(Subject s, RectTransform b, float W)
        {
            if (!CreatorUI.Section(b, W, _open, "item:color", Loc.T("photo.color"), false, BuildItemWindow)) return;
            var it = s.Item;
            Color.RGBToHSV(ColorOf(s), out float h, out float sat, out float val);
            void Paint(float nh, float ns, float nv)
            {
                it.TintSet = true;
                it.Tint = Color.HSVToRGB(nh, ns, nv);
                Recolor(s);
            }
            CreatorUI.Number(b, W, Loc.T("photo.hat.hue"), 0f, 1f, h, false, v => { h = v; Paint(h, sat, val); }, v => Loc.T("photo.hat.hue"));
            CreatorUI.Number(b, W, Loc.T("photo.hat.sat"), 0f, 1f, sat, false, v => { sat = v; Paint(h, sat, val); }, v => Loc.T("photo.hat.sat"));
            CreatorUI.Number(b, W, Loc.T("photo.hat.light"), 0.05f, 1f, val, false, v => { val = v; Paint(h, sat, val); }, v => Loc.T("photo.hat.light"));
            if (it.TintSet)
                CreatorUI.Do(b, W, Loc.T("photo.color.own"), () =>
                {
                    it.TintSet = false;
                    Restand(s); // a plain body's own look needs its own materials back
                    BuildItemWindow();
                }, null, 30f);
        }

        void EyeRows(Subject s, RectTransform b, float W)
        {
            var it = s.Item;
            CreatorUI.Switch(b, W, Loc.T("photo.eyes"), new[]
            {
                Loc.T("photo.mood.neutral"), Loc.T("photo.mood.scared"), Loc.T("photo.mood.wowed"),
                Loc.T("photo.mood.mad"), Loc.T("photo.mood.dizzy"),
            }, Mathf.Clamp(it.Mood, 0, 4), i =>
            {
                it.Mood = i;
                SettleEyes(s);
                BuildItemWindow();
            });
            CreatorUI.Switch(b, W, Loc.T("photo.gaze"), new[] { Loc.T("photo.gaze.camera"), Loc.T("photo.gaze.ahead") },
                it.LookAtCamera ? 0 : 1, i =>
                {
                    it.LookAtCamera = i == 0;
                    SettleEyes(s);
                    BuildItemWindow();
                });
        }

        // --------------------------------------------------------- ink window --
        void BuildInkWindow()
        {
            var w = _winInk;
            if (w == null) return;
            float keep = Keep(w);
            w.Clear();
            var b = w.Body;
            float W = w.Width;
            CreatorUI.Note(b, W, Loc.T("photo.ink.note"), 44f);
            bool on = _tool == Tool.Ink;
            CreatorUI.Do(b, W, on ? Loc.T("photo.ink.stop") : Loc.T("photo.ink.draw"), () =>
            {
                if (_tool == Tool.Ink) EndTool();
                else StartInk();
            }, CreatorUI.Pick(on), 36f);
            var labels = new List<string>();
            foreach (var c in InkColors) labels.Add(InkName(c.Key));
            CreatorUI.Grid(b, W, 2, labels, i => _inkColor == InkColors[i].Color, i =>
            {
                _inkColor = InkColors[i].Color;
                BuildInkWindow();
            }, 28f, 13);
            CreatorUI.Number(b, W, "", 0.005f, 0.2f, _inkWidth, false, v => _inkWidth = v,
                v => Loc.T("photo.ink.width") + ": " + Mathf.RoundToInt(v * 1000f) + " mm");
            CreatorUI.Do(b, W, Loc.T("photo.ink.undo"), UndoInk, null, 30f);
            if (_sel != null) CreatorUI.Do(b, W, Loc.T("photo.ink.wipepicked"), () => WipeInk(_sel), null, 30f);
            CreatorUI.Do(b, W, Loc.T("photo.ink.wipe"), () => WipeInk(null), CreatorUI.Red, 30f);
            Restore(w, keep);
        }

        static string InkName(string key)
        {
            switch (key)
            {
                case "photo.ink.ink": return Loc.T("photo.ink.ink");
                case "photo.ink.green": return Loc.T("photo.ink.green");
                case "photo.ink.gold": return Loc.T("photo.ink.gold");
                case "photo.ink.blue": return Loc.T("photo.ink.blue");
                case "photo.ink.white": return Loc.T("photo.ink.white");
                case "photo.ink.black": return Loc.T("photo.ink.black");
                default: return Loc.T("photo.ink.red");
            }
        }

        // -------------------------------------------------------- look window --
        void BuildLookWindow()
        {
            var w = _winLook;
            if (w == null) return;
            float keep = Keep(w);
            w.Clear();
            var b = w.Body;
            float W = w.Width;
            var d = Editing;

            if (CreatorUI.Section(b, W, _open, "look:light", Loc.T("photo.light"), true, BuildLookWindow))
            {
                // characters in light and shadow, or flat as in the game (lit = shaded)
                CreatorUI.Do(b, W, Loc.T("photo.shade"), () =>
                {
                    d.Shaded = !d.Shaded;
                    foreach (var s in _subjects.ToArray())
                        if (s.Item.Kind == PhotoKind.Character) Restand(s);
                    BuildLookWindow();
                }, CreatorUI.Pick(d.Shaded), 30f);
                var (turn, height, power) = SunNow();
                CreatorUI.Number(b, W, "", 0f, 360f, turn, true, v => { TakeSun(); d.SunTurn = v; ApplyLight(); },
                    v => Loc.T("photo.sun.turn") + ": " + Mathf.RoundToInt(v) + "°");
                CreatorUI.Number(b, W, "", 2f, 90f, height, true, v => { TakeSun(); d.SunHeight = v; ApplyLight(); },
                    v => Loc.T("photo.sun.height") + ": " + Mathf.RoundToInt(v) + "°");
                CreatorUI.Number(b, W, "", 0f, 3f, power, false, v => { TakeSun(); d.SunPower = v; ApplyLight(); },
                    v => Loc.T("photo.sun.power") + ": " + v.ToString("0.0") + "x");
                if (d.SunMoved)
                    CreatorUI.Do(b, W, Loc.T("photo.sun.reset"), () =>
                    {
                        d.SunMoved = false;
                        ApplyLight();
                        BuildLookWindow();
                    }, null, 30f);
                CreatorUI.Number(b, W, "", 0f, 4f, d.Rim, false, v => { d.Rim = v; ApplyRim(); },
                    v => Loc.T("photo.rim") + ": " + v.ToString("0.0"));
                CreatorUI.Number(b, W, "", 0f, 360f, d.RimTurn, true, v => { d.RimTurn = v; ApplyRim(); },
                    v => Loc.T("photo.rim.turn") + ": " + Mathf.RoundToInt(v) + "°");
            }

            if (CreatorUI.Section(b, W, _open, "look:picture", Loc.T("photo.picture"), true, BuildLookWindow))
            {
                Look(b, W, "photo.brightness", -2f, 2f, d.Brightness, v => d.Brightness = v);
                Look(b, W, "photo.contrast", -60f, 60f, d.Contrast, v => d.Contrast = v);
                Look(b, W, "photo.warmth", -60f, 60f, d.Warmth, v => d.Warmth = v);
                Look(b, W, "photo.saturation", -100f, 60f, d.Saturation, v => d.Saturation = v);
                Look(b, W, "photo.glow", 0f, 5f, d.Glow, v => d.Glow = v);
                Look(b, W, "photo.vignette", 0f, 0.6f, d.Vignette, v => d.Vignette = v);
                Look(b, W, "photo.blur", 0f, 1f, d.Blur, v => d.Blur = v);
                CreatorUI.Number(b, W, "", 0.3f, 80f, d.Focus, false, v => { d.Focus = v; ApplyLook(); },
                    v => Loc.T("photo.focus") + ": " + v.ToString("0.0") + " m");
                if (_sel != null)
                    CreatorUI.Do(b, W, Loc.T("photo.focus.picked"), () =>
                    {
                        if (_sel == null || _cam == null) return;
                        d.Focus = Mathf.Max(0.3f, Vector3.Distance(_cam.transform.position, Center(_sel)));
                        ApplyLook();
                        BuildLookWindow();
                    }, null, 30f);
                if (_cam != null)
                    CreatorUI.Number(b, W, "", 12f, 90f, _cam.fieldOfView, true, v => { if (_cam != null) _cam.fieldOfView = v; },
                        v => Loc.T("photo.lens") + ": " + Mathf.RoundToInt(v) + "°");
                CreatorUI.Do(b, W, Loc.T("photo.look.reset"), () =>
                {
                    d.Brightness = d.Contrast = d.Warmth = d.Saturation = d.Glow = d.Vignette = d.Blur = 0f;
                    ApplyLook();
                    BuildLookWindow();
                }, null, 30f);
            }
            Restore(w, keep);
        }

        void Look(RectTransform b, float W, string key, float min, float max, float value, System.Action<float> set) =>
            CreatorUI.Number(b, W, "", min, max, value, false, v =>
            {
                set(v);
                ApplyLook();
            }, v => LookName(key) + ": " + v.ToString("0.##"));

        static string LookName(string key)
        {
            switch (key)
            {
                case "photo.brightness": return Loc.T("photo.brightness");
                case "photo.contrast": return Loc.T("photo.contrast");
                case "photo.warmth": return Loc.T("photo.warmth");
                case "photo.saturation": return Loc.T("photo.saturation");
                case "photo.glow": return Loc.T("photo.glow");
                case "photo.vignette": return Loc.T("photo.vignette");
                default: return Loc.T("photo.blur");
            }
        }

        /// The first touch of a sun slider takes the sun from where the scene put it.
        void TakeSun()
        {
            if (Editing.SunMoved) return;
            var (turn, height, power) = SunNow();
            Editing.SunTurn = turn;
            Editing.SunHeight = height;
            Editing.SunPower = power;
            Editing.SunMoved = true;
        }

        // ------------------------------------------------------- photo window --
        void BuildPhotoWindow()
        {
            var w = _winPhoto;
            if (w == null) return;
            float keep = Keep(w);
            w.Clear();
            var b = w.Body;
            float W = w.Width;
            var d = Editing;
            CreatorUI.Note(b, W, HelpText(), 62f);
            CreatorUI.Field(b, W, Loc.T("mc.name"), d.Name, v => d.Name = v);

            var folders = PhotoLibrary.Folders();
            var folderNames = new List<string> { Loc.T("photos.nofolder") };
            folderNames.AddRange(folders);
            CreatorUI.Dropdown(b, W, _open, "photo:folder", Loc.T("photos.folder"),
                string.IsNullOrEmpty(d.Folder) ? Loc.T("photos.nofolder") : d.Folder, folderNames,
                i => i == 0 ? string.IsNullOrEmpty(d.Folder) : string.Equals(folders[i - 1], d.Folder, System.StringComparison.OrdinalIgnoreCase),
                i => d.Folder = i == 0 ? "" : folders[i - 1], BuildPhotoWindow);
            if (_open.Contains("photo:folder"))
            {
                CreatorUI.Field(b, W, Loc.T("photos.folder.new"), _newFolder, v => _newFolder = v);
                CreatorUI.Do(b, W, Loc.T("photos.folder.make"), () =>
                {
                    if (string.IsNullOrWhiteSpace(_newFolder)) return;
                    d.Folder = PhotoLibrary.MakeFolder(_newFolder);
                    _newFolder = "";
                    _open.Remove("photo:folder");
                    BuildPhotoWindow();
                }, null, 30f);
            }
            var files = CreatorUI.Row(b, W);
            UIKit.Button(files, Loc.T("creator.save"), Save, CreatorUI.Pick(true), 14);
            UIKit.Button(files, Loc.T("photo.new"), NewPhoto, CreatorUI.Pick(false), 14);

            // the canvas
            CreatorUI.Head(b, W, Loc.T("photo.canvas"));
            CreatorUI.Field(b, W, Loc.T("photo.width"), d.Width.ToString(), v =>
            {
                if (int.TryParse(v, out int n)) d.Width = Mathf.Clamp(n, 16, MaxSide);
            });
            CreatorUI.Field(b, W, Loc.T("photo.height"), d.Height.ToString(), v =>
            {
                if (int.TryParse(v, out int n)) d.Height = Mathf.Clamp(n, 16, MaxSide);
            });
            int preset = SizeNow();
            var sizeNames = new List<string>();
            foreach (var c in Sizes) sizeNames.Add(SizeName(c) + "  " + c.W + "x" + c.H);
            CreatorUI.Dropdown(b, W, _open, "photo:sizes", Loc.T("photo.sizes"), preset >= 0 ? SizeName(Sizes[preset]) : "",
                sizeNames, i => i == preset, i =>
                {
                    d.Width = Sizes[i].W;
                    d.Height = Sizes[i].H;
                }, BuildPhotoWindow);
            if (preset >= 0 && Sizes[preset].Safe != Vector2.zero) CreatorUI.Note(b, W, Loc.T("photo.safe.note"), 30f);
            if (preset >= 0 && Sizes[preset].SeeThrough && d.Background != 2) CreatorUI.Note(b, W, Loc.T("photo.seethrough.note"), 30f);

            // what is behind
            CreatorUI.Switch(b, W, Loc.T("photo.back"),
                new[] { Loc.T("photo.back.world"), Loc.T("photo.back.colour"), Loc.T("photo.back.none") },
                Mathf.Clamp(d.Background, 0, 2), i =>
                {
                    d.Background = i;
                    ApplyBackground();
                    BuildPhotoWindow();
                });
            if (d.Background == 1)
            {
                Channel(b, W, "photo.red", d.BackColor.r, v => d.BackColor.r = v);
                Channel(b, W, "photo.green", d.BackColor.g, v => d.BackColor.g = v);
                Channel(b, W, "photo.blue", d.BackColor.b, v => d.BackColor.b = v);
            }
            if (d.Background == 2) CreatorUI.Note(b, W, Loc.T("photo.back.none.note"), 44f);

            CreatorUI.Do(b, W, Loc.T("photo.take"), TakePhoto, CreatorUI.Pick(true), 40f);
            CreatorUI.Do(b, W, Loc.T("photos.files"), () => PhotoLibrary.Reveal(PhotoLibrary.PhotosFolder(d.Folder)), null, 30f);

            // the other setups in this folder, to hop between thumbnails
            if (CreatorUI.Section(b, W, _open, "photo:saved", Loc.T("photo.saved"), false, BuildPhotoWindow))
            {
                var mine = PhotoLibrary.In(d.Folder);
                if (mine.Count == 0) CreatorUI.Note(b, W, Loc.T("photo.saved.none"), 22f);
                var names = mine.ConvertAll(p => p.Name);
                CreatorUI.Grid(b, W, 1, names, i => mine[i].Name == _savedAs && mine[i].Folder == (_savedIn ?? ""),
                    i => Become(mine[i], mine[i].SavedAs, mine[i].Folder, true), 28f, 13);
            }
            Restore(w, keep);
        }

        void Channel(RectTransform b, float W, string key, float value, System.Action<float> set) =>
            CreatorUI.Number(b, W, "", 0f, 255f, value * 255f, true, v =>
            {
                set(v / 255f);
                ApplyBackground();
            }, v => ChannelName(key) + ": " + Mathf.RoundToInt(v));

        static string ChannelName(string key) =>
            key == "photo.red" ? Loc.T("photo.red") : key == "photo.green" ? Loc.T("photo.green") : Loc.T("photo.blue");

        /// Keeps this setup to open and edit again, with its picture for the card.
        void Save()
        {
            if (Store()) Flash(Loc.F("photo.stored", Editing.Name));
        }

        /// The setup and its card picture written: Save, and every photo taken. False when it failed (said on screen).
        bool Store()
        {
            CaptureAll();
            CaptureCamera();
            var d = Editing;
            if (string.IsNullOrWhiteSpace(d.Name)) d.Name = Loc.T("photos.newphoto");
            d.Folder = d.Folder ?? "";
            bool same = _savedAs != null && _savedAs == d.Name && (_savedIn ?? "") == d.Folder;
            if (!same && PhotoLibrary.Exists(d.Folder, d.Name)) d.Name = PhotoLibrary.FreeName(d.Folder, d.Name);
            try
            {
                PhotoLibrary.Save(d, Thumbnail());
                if (_savedAs != null && !same) PhotoLibrary.Delete(_savedIn ?? "", _savedAs);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SpellyZombie] Photo Booth: the setup could not be saved: {e}");
                FlashError(Loc.T("photo.failed"));
                return false;
            }
            _savedAs = d.Name;
            _savedIn = d.Folder;
            d.SavedAs = d.Name;
            BuildPhotoWindow();
            return true;
        }

        /// A fresh setup on the same stage, from the same eye.
        void NewPhoto()
        {
            CaptureCamera();
            var old = Editing;
            Become(new PhotoDef
            {
                Folder = old.Folder,
                CamPos = old.CamPos, CamYaw = old.CamYaw, CamPitch = old.CamPitch, Lens = old.Lens,
                Width = old.Width, Height = old.Height,
                Background = old.Background, BackColor = old.BackColor,
                Island = old.Island, IslandSeed = old.IslandSeed,
            }, null, null, false);
        }
    }
}
