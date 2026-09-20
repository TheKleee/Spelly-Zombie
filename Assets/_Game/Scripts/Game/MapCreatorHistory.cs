using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// ★ UNDO AND REDO (Ctrl+Z, Ctrl+R): one history of the map, its book and
    /// its kept drawings. When a gesture ends - a click or a drag let go, an
    /// editing key released, a text field left - the state is compared with the
    /// last step and a difference becomes a new one. The drawing pad's strokes
    /// take their turn in the same line, so the last thing done is undone first.
    public partial class MapCreator
    {
        /// A canvas stroke (Canvas set) or the state before a change.
        struct Step { public IUndoCanvas Canvas; public string State; }

        const int HistoryMax = 100;
        const string BookSplit = "\nBOOK\n";
        readonly List<Step> _undo = new List<Step>(), _redo = new List<Step>();
        string _committed;
        bool _historyBusy, _wasTyping;

        string StateNow()
        {
            string keepBook = Editing.BookJson;
            Editing.BookJson = ""; // written only on Save; the live book rides alongside
            string map = JsonUtility.ToJson(Editing);
            Editing.BookJson = keepBook;
            return map + BookSplit + SpellBook.LiveJson();
        }

        void ResetHistory()
        {
            _undo.Clear();
            _redo.Clear();
            _committed = Editing != null ? StateNow() : null;
        }

        /// Ends of gestures become steps; Ctrl+Z and Ctrl+R walk the line.
        void History(Keyboard kb, Mouse mouse)
        {
            bool typing = UIKit.Typing;
            if (mouse.leftButton.wasReleasedThisFrame || (_wasTyping && !typing)
                || kb.deleteKey.wasReleasedThisFrame || kb.rKey.wasReleasedThisFrame
                || kb.leftBracketKey.wasReleasedThisFrame || kb.rightBracketKey.wasReleasedThisFrame)
                CommitStep();
            _wasTyping = typing;
            if (typing || _dragging) return;
            if (!kb.leftCtrlKey.isPressed && !kb.rightCtrlKey.isPressed) return;
            // the letters as printed: Z sits elsewhere on a QWERTZ keyboard
            var z = kb.FindKeyOnCurrentKeyboardLayout("z");
            var r = kb.FindKeyOnCurrentKeyboardLayout("r");
            if (z != null && z.wasPressedThisFrame) { Travel(_undo, _redo, true); Chord(); }
            else if (r != null && r.wasPressedThisFrame) { Travel(_redo, _undo, false); Chord(); }
        }

        /// What changed since the last step becomes one.
        void CommitStep()
        {
            if (_historyBusy || Editing == null) return;
            string now = StateNow();
            if (_committed == null) { _committed = now; return; }
            if (now == _committed) return;
            Push(_undo, new Step { State = _committed });
            _redo.Clear();
            _committed = now;
        }

        /// A canvas drew, erased, stamped or cleared.
        void CanvasStep(IUndoCanvas canvas)
        {
            if (_historyBusy || canvas == null) return;
            Push(_undo, new Step { Canvas = canvas });
            _redo.Clear();
        }

        /// A canvas's own buttons, or a load, move it without making a step.
        void Quiet(System.Action act)
        {
            _historyBusy = true;
            try { act(); }
            finally { _historyBusy = false; }
        }

        static void Push(List<Step> stack, Step s)
        {
            stack.Add(s);
            if (stack.Count > HistoryMax) stack.RemoveAt(0);
        }

        void Travel(List<Step> from, List<Step> to, bool back)
        {
            CommitStep(); // anything not yet a step becomes one first
            while (from.Count > 0)
            {
                var s = from[from.Count - 1];
                from.RemoveAt(from.Count - 1);
                if (s.Canvas != null)
                {
                    var alive = s.Canvas as Object;
                    if (alive == null || !(back ? s.Canvas.CanUndo : s.Canvas.CanRedo)) continue; // that canvas is gone or spent
                    Quiet(() => { if (back) s.Canvas.Undo(); else s.Canvas.Redo(); });
                    Push(to, s);
                    return;
                }
                Push(to, new Step { State = _committed });
                ApplyState(s.State);
                _committed = s.State;
                return;
            }
        }

        /// A shortcut used Ctrl: whatever it sank the camera comes back.
        void Chord() => _fly?.Chord();

        /// The map, the book and the drawings as the step holds them, and everything that shows them.
        void ApplyState(string state)
        {
            int cut = state.IndexOf(BookSplit, System.StringComparison.Ordinal);
            if (cut < 0) return;
            string biomesBefore = BiomesText();
            string keepBook = Editing.BookJson;
            JsonUtility.FromJsonOverwrite(state.Substring(0, cut), Editing);
            Editing.BookJson = keepBook;
            Editing.Repair();
            var book = SpellBook.Live;
            JsonUtility.FromJsonOverwrite(state.Substring(cut + BookSplit.Length), book);
            book.Repair();
            RuneLibrary.UseMapSamples(Editing.SampleList());
            if (BiomesText() != biomesBefore) _stale = true;

            // what was selected may be gone
            int count = _sel == Sel.Biome ? Editing.Biomes.Count
                : _sel == Sel.Spawn ? Editing.Spawns.Count
                : _sel == Sel.Prop ? Editing.Props.Count : 0;
            if (_selIndex >= count) { _sel = Sel.None; _selIndex = -1; }
            _selPiece = null;
            RebuildViews();
            DressPieces();
            Select(_sel, _selIndex); // the selection, its windows and the hint
            if (_winMap != null && _winMap.Visible) BuildMapWindow();
            if (_screen != null)
            {
                _shownSpell = null;
                _shownCreature = null;
                if (_screenKind == ScreenKind.Spells) RefreshSpells();
                else if (_screenKind == ScreenKind.Creatures) RefreshCreatures();
                else RefreshRunes();
            }
        }

        string BiomesText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in Editing.Biomes) sb.Append(JsonUtility.ToJson(b));
            return sb.ToString();
        }
    }
}
