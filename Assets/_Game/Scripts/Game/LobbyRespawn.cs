using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// THE LOBBY REBUILDS ITSELF ("objects to spawn after being
    /// destroyed - cause we need to allow acolytes to scan and people to test
    /// spells out... we can't do that in leveled lobby map").
    ///
    /// The lobby is the practice ground, so it has to survive being practised
    /// on. An acolyte needs shapes left to scan and a wizard needs things left
    /// to break; a stripped courtyard teaches nobody anything. A broken prop
    /// comes back where it stood, whole, after a short wait.
    ///
    /// IT HIDES RATHER THAN DESTROYS, for a boring reason that matters: a
    /// destroyed GameObject cannot run the timer that would bring it back, and
    /// re-instantiating means knowing which prefab a hand-placed scene object
    /// came from - which nothing records. Hiding keeps the exact object, the
    /// materials and the Inspector settings, and puts them back untouched.
    ///
    /// LOBBY ONLY. In a real round, broken is broken.
    public class LobbyRespawn : MonoBehaviour
    {
        Vector3 _pos;
        Quaternion _rot;
        Vector3 _scale;
        float _health0 = -1f;
        float _back = -1f;
        bool _wasKinematic;   // rooted scenery is kinematic; it must go back that way

        /// True while the object is in the graveyard (invisible, waiting).
        /// The object stays ACTIVE the whole time - its scripts keep running -
        /// so anything timing itself against the comeback must ask this.
        public bool Hidden => _back > 0f;

        readonly List<Renderer> _hidden = new List<Renderer>();
        readonly List<Collider> _off = new List<Collider>();

        void Awake()
        {
            // captured BEFORE anything can shove it: a prop that was lifted,
            // thrown and then broken must return to where PUT it, not to
            // wherever it happened to die
            _pos = transform.position;
            _rot = transform.rotation;
            _scale = transform.localScale;
            var dmg = GetComponent<Damageable>();
            if (dmg != null) _health0 = dmg.Health;
        }

        /// Take this object out of the world instead of letting it be destroyed.
        /// Safe to call on anything; it attaches itself on first use.
        public static void Take(GameObject go, float seconds)
        {
            if (go == null) return;
            var r = go.GetComponent<LobbyRespawn>();
            if (r == null) r = go.AddComponent<LobbyRespawn>();
            r.Vanish(seconds);
        }

        void Vanish(float seconds)
        {
            if (_back > 0f) return;   // already gone; do not double-hide

            // THE DRAWING DIES WITH THE CANVAS ("when object is
            // destroyed in the lobby the lines on the object need to be
            // destroyed with it") — no ink floating where the prop was, and
            // the rebuilt prop comes back factory-new, ready to be drawn on.
            var world = DrawingWorld.Instance;
            if (world != null)
                for (int i = world.Strokes.Count - 1; i >= 0; i--)
                {
                    var s = world.Strokes[i];
                    if (s == null || !s.Alive || s.Surface == null) continue;
                    if (s.Surface == transform || s.Surface.IsChildOf(transform)) s.Burn();
                }

            // Damageable would Destroy() its object the moment OnDeath returns -
            // this is the one hook that stops it, restored on the way back.
            // EVERY Damageable under the object counts: authored props carry
            // theirs on mesh children, and a root-only flip let the child die.
            foreach (var d in GetComponentsInChildren<Damageable>(true))
                if (d != null) d.Destructible = false;

            _hidden.Clear();
            foreach (var rend in GetComponentsInChildren<Renderer>(true))
                if (rend.enabled) { rend.enabled = false; _hidden.Add(rend); }

            _off.Clear();
            foreach (var col in GetComponentsInChildren<Collider>(true))
                if (col.enabled) { col.enabled = false; _off.Add(col); }

            // REMEMBER WHAT IT WAS. Restoring isKinematic=false blindly turned
            // rooted scenery into a DYNAMIC body - and the village props carry
            // concave MeshColliders, which Unity refuses on a dynamic body:
            // "Concave Mesh Colliders are not supported... Scene hierarchy path
            // SZ_Village/Stall_Empty". The body then behaves undefined, and
            // anything touching it (a zombie chewing through) goes with it.
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                _wasKinematic = rb.isKinematic;
                rb.isKinematic = true;   // stop it falling while invisible
            }

            _back = seconds;
        }

        void Update()
        {
            if (_back <= 0f) return;
            _back -= Time.deltaTime;
            if (_back > 0f) return;
            _back = -1f;

            // back exactly where design it, at full health, whole
            transform.SetPositionAndRotation(_pos, _rot);
            transform.localScale = _scale;

            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                // back to what it WAS, and velocities only touched on a body
                // that is actually dynamic - writing them to a kinematic one is
                // the other warning that was flooding the console
                rb.isKinematic = _wasKinematic;
                if (!_wasKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }

            foreach (var rend in _hidden) if (rend != null) rend.enabled = true;
            foreach (var col in _off) if (col != null) col.enabled = true;
            _hidden.Clear();
            _off.Clear();

            // Revive, not just heal: the private dead-flag must drop too,
            // or the prop comes back IMMORTAL (breakable exactly once).
            // Every Damageable under the object, same reach as the Vanish.
            foreach (var d in GetComponentsInChildren<Damageable>(true))
                if (d != null)
                {
                    d.Revive(_health0 > 0f ? _health0 : 100f);
                    d.Destructible = true;
                }

            // the ink an acolyte's scan left on it goes too, so the same prop
            // can be scanned again by the next person practising
            foreach (var mark in GetComponentsInChildren<InkMark>(true))
                if (mark != null) Destroy(mark);
        }
    }
}
