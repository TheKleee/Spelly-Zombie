using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Acolyte overwatch: from first person, R looks out through your own
    /// zombies; 1..9 then 0 pick one (ten keys = the cap of ten); you cannot
    /// steer it; leave with R, TAB or F. A seal drawn on it detonates it.
    /// Runs after ShapeShift (220) so the camera write lands last.
    [DefaultExecutionOrder(221)]
    public class ZombieWatch : MonoBehaviour
    {
        /// Open right now? Read by anything that must stand down while the
        /// acolyte is looking through a corpse.
        public static bool IsOpen => Local != null && Local._open;

        /// The zombie currently on screen, or null. The detonation reads this
        /// so a seal drawn in this mode knows what it is drawn on.
        public static SummonedZombie Watched =>
            Local != null && Local._open ? Local.Current : null;

        static ZombieWatch Local;

        /// A big enough blast closes overwatch. Driven by Shove.
        public static void Blown() { if (Local != null && Local._open) Local.Close(); }

        readonly List<SummonedZombie> _mine = new List<SummonedZombie>();
        int _index = -1;
        bool _open;
        Vector3 _pin;
        SimpleFPSController _pilot;

        /// Camera lifecycle: EaselOrbit borrow / orbit / release.
        readonly EaselOrbit.Borrowed _view = new EaselOrbit.Borrowed();

        SummonedZombie Current =>
            _index >= 0 && _index < _mine.Count ? _mine[_index] : null;

        void Awake() => Local = this;
        void OnDisable() { if (_open) Close(); }

        /// Every living zombie this acolyte raised, capped at the ten. Rebuilt
        /// each frame because they expire on their own clock - a watched zombie
        /// that dies mid-look must not leave the camera staring at nothing.
        void Gather()
        {
            _mine.Clear();
            foreach (var z in Zombie.All)
            {
                if (z == null) continue;
                var mine = z.GetComponent<SummonedZombie>();
                if (mine == null || mine.SummonedBy != Grimoire.LocalPlayerId) continue;
                _mine.Add(mine);
                if (_mine.Count >= DrawingConfig.AcolyteZombieCap) break;
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || UIKit.Typing || GameMenu.IsOpen || PoseStudio.IsOpen) return;

            // local viewer only - SideBootstrap adds this to every controller,
            // and a remote body must not answer the local R
            if (_pilot == null) _pilot = GetComponent<SimpleFPSController>();
            if (_pilot == null || !_pilot.IsLocalViewer) { if (_open) Close(); return; }

            // ACOLYTES ONLY, AND ONLY FROM FIRST PERSON. While transformed R is
            // the pose mode and ShapeShift owns it - the two never overlap.
            if (Sides.Of(Grimoire.LocalPlayerId) != Side.Acolyte
                || ShapeShift.LocalIsShaped
                || SimpleFPSController.ThirdPersonActive)
            {
                if (_open) Close();
                return;
            }

            Gather();
            if (_open && _mine.Count == 0) { Close(); return; }

            if (kb.rKey.wasPressedThisFrame)
            {
                if (_open) { Close(); return; }
                if (_mine.Count == 0)
                {
                    DrawingWorld.Instance?.LogEvent("you have no dead to look through");
                    return;
                }
                Open();
                return;
            }

            if (!_open) return;

            if (kb.tabKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame
                || kb.escapeKey.wasPressedThisFrame) { Close(); return; }

            // 1..9 then 0 - ten keys for the ten zombies
            for (int i = 0; i < 9; i++)
            {
                var key = kb[(Key)((int)Key.Digit1 + i)];
                if (key != null && key.wasPressedThisFrame) Look(i);
            }
            if (kb.digit0Key.wasPressedThisFrame) Look(9);

            // the watched one expired while you were looking at it
            if (Current == null) Look(0);

            UIPrompt.Show("R", _mine.Count > 1
                ? $"watching {_index + 1} of {_mine.Count}  ·  1-0 picks  ·  draw a seal on it to blow it"
                : "draw a seal on it to blow it  ·  R leaves",
                new Color(0.6f, 1f, 0.55f));
        }

        void Look(int i)
        {
            if (i < 0 || i >= _mine.Count) return;
            _index = i;
        }

        void Open()
        {
            _open = true;
            _index = 0;
            _pin = transform.position;
            _view.Borrow(GetComponentInChildren<Camera>(true),
                transform.eulerAngles.y, 18f, 3f);
        }

        void Close()
        {
            _open = false;
            _index = -1;
            _view.Release();
        }

        void LateUpdate()
        {
            if (!_open) return;

            // the body stays pinned while overwatch is open
            transform.position = _pin;

            var z = Current;
            if (z == null || !_view.Held) return;

            // tight camera leash - a free orbit would turn each zombie into a
            // scouting camera
            float lift = Mathf.Max(0.3f, z.transform.localScale.y * 1.1f);
            float near = lift * 1.8f;
            _view.Orbit(Keyboard.current, Mouse.current,
                z.transform.position + Vector3.up * lift,
                zoomMin: near, zoomMax: near * 1.35f);
        }
    }
}
