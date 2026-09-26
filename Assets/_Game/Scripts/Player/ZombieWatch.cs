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

        /// The body currently on screen (the zombie itself on the host, its
        /// stand-in on a client), or null.
        public static Transform Watched =>
            Local != null && Local._open ? Local.Current : null;

        static ZombieWatch Local;

        /// A big enough blast closes overwatch. Driven by Shove.
        public static void Blown() { if (Local != null && Local._open) Local.Close(); }

        readonly List<Transform> _mine = new List<Transform>();
        int _index = -1;
        bool _open;
        Vector3 _pin;
        SimpleFPSController _pilot;

        /// Camera lifecycle: EaselOrbit borrow / orbit / release.
        readonly EaselOrbit.Borrowed _view = new EaselOrbit.Borrowed();

        Transform Current =>
            _index >= 0 && _index < _mine.Count ? _mine[_index] : null;

        void Awake() => Local = this;
        void OnDisable() { if (_open) Close(); }

        /// Every living zombie this acolyte raised, capped at the ten. Rebuilt
        /// each frame because they expire on their own clock - a watched zombie
        /// that dies mid-look must not leave the camera staring at nothing.
        void Gather() => MineInto(_mine);

        /// The local acolyte's living zombies. On the host they are the zombies
        /// themselves; on a client the host's zombies are stand-ins, and whose
        /// they are comes with the snapshot.
        static void MineInto(List<Transform> into)
        {
            into.Clear();
            int me = Grimoire.LocalPlayerId;
            foreach (var z in Zombie.All)
            {
                if (into.Count >= DrawingConfig.AcolyteZombieCap) return;
                if (z == null) continue;
                var mine = z.GetComponent<SummonedZombie>();
                if (mine != null && mine.SummonedBy == me) into.Add(z.transform);
            }
            foreach (var p in NetZombieProxy.All)
            {
                if (into.Count >= DrawingConfig.AcolyteZombieCap) return;
                if (p != null && p.OwnerId == me) into.Add(p.transform);
            }
        }

        static readonly List<Transform> _any = new List<Transform>();
        /// Does the local acolyte have a zombie to look through? (The R chip asks.)
        public static bool OwnsAny()
        {
            MineInto(_any);
            return _any.Count > 0;
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

            if (Keys.Down(Act.Body))
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

            if (Keys.Down(Act.View) || Keys.Down(Act.Drop)
                || kb.escapeKey.wasPressedThisFrame || Keys.BackDown) { Close(); return; }

            // 1..9 then 0 - ten keys for the ten zombies
            for (int i = 0; i < 9; i++)
            {
                var key = kb[(Key)((int)Key.Digit1 + i)];
                if (key != null && key.wasPressedThisFrame) Look(i);
            }
            if (kb.digit0Key.wasPressedThisFrame) Look(9);
            // a controller steps through them
            if (_mine.Count > 1 && Keys.Down(Act.Next)) Look((_index + 1) % _mine.Count);
            if (_mine.Count > 1 && Keys.Down(Act.Prev)) Look((_index - 1 + _mine.Count) % _mine.Count);

            // the watched one expired while you were looking at it
            if (Current == null) Look(0);

            UIPrompt.Show("R", _mine.Count > 1
                ? Loc.F("watch.many", _index + 1, _mine.Count)
                : Loc.T("watch.one"),
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
            float lift = Mathf.Max(0.3f, z.localScale.y * 1.1f);
            float near = lift * 1.8f;
            _view.Orbit(Keyboard.current, Mouse.current,
                z.position + Vector3.up * lift,
                zoomMin: near, zoomMax: near * 1.35f);
        }
    }
}
