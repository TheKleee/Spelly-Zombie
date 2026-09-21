using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// The little spheres effects are made of: puffs, fire motes, flames. Each used to be a
    /// new GameObject with its own Rigidbody, made and destroyed per effect; a golem fight
    /// made hundreds a second and every one sat in the physics world as one more body.
    /// Here they are kept on a shelf and moved by one loop, the way physics moved them:
    /// a steady velocity, with gravity for the ones that fall, gone when their time is up.
    public class MotePool : MonoBehaviour
    {
        struct Mote
        {
            public Transform T;
            public Renderer R;
            public Vector3 Vel;
            public bool Falls;
            public float DieAt;
        }

        static MotePool _pool;
        readonly List<Mote> _live = new List<Mote>(256);
        readonly Stack<Mote> _shelf = new Stack<Mote>(256);

        static MotePool Pool
        {
            get
            {
                if (_pool != null) return _pool;
                var go = new GameObject("MotePool");
                DontDestroyOnLoad(go);
                _pool = go.AddComponent<MotePool>();
                SceneManager.sceneLoaded += (_, mode) =>
                {
                    // a new place starts clean, as it did when the motes belonged to the old scene
                    if (_pool != null && mode == LoadSceneMode.Single) _pool.ShelveAll();
                };
                return _pool;
            }
        }

        /// One sphere at `at`, `scale` across, flying at `velocity` for `life` seconds.
        public static void Spawn(Vector3 at, float scale, Material look, Vector3 velocity, bool falls, float life)
        {
            if (!Application.isPlaying) return;
            var pool = Pool;
            Mote m;
            if (pool._shelf.Count > 0)
            {
                m = pool._shelf.Pop();
                if (m.T == null) m = pool.Make(); // lost with something it rode on
            }
            else m = pool.Make();
            m.T.position = at;
            m.T.localScale = Vector3.one * scale;
            m.R.sharedMaterial = look;
            m.R.enabled = true;
            m.Vel = velocity;
            m.Falls = falls;
            m.DieAt = Time.time + life;
            pool._live.Add(m);
        }

        Mote Make()
        {
            // the same primitive as ever, so it is lit and shadowed as ever; only its collider goes
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Mote";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            return new Mote { T = go.transform, R = go.GetComponent<Renderer>() };
        }

        void Update() { using (PerfMarkers.UpdMotes.Auto()) Turn(); }

        void Turn()
        {
            float dt = Time.deltaTime, now = Time.time;
            Vector3 fall = Physics.gravity * dt;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var m = _live[i];
                if (m.T == null) { RemoveAt(i); continue; }
                if (now >= m.DieAt)
                {
                    m.R.enabled = false;
                    _shelf.Push(m);
                    RemoveAt(i);
                    continue;
                }
                if (m.Falls) { m.Vel += fall; _live[i] = m; }
                m.T.position += m.Vel * dt;
            }
        }

        void RemoveAt(int i)
        {
            int last = _live.Count - 1;
            _live[i] = _live[last];
            _live.RemoveAt(last);
        }

        void ShelveAll()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var m = _live[i];
                if (m.T == null) continue;
                m.R.enabled = false;
                _shelf.Push(m);
            }
            _live.Clear();
        }
    }
}
