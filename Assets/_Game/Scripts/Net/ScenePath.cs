using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// A transform's address that reads the same on every machine: its name
    /// path, with *n on the nth of several same-named siblings (the walls and
    /// floor tiles inside a house share names). A path with no repeats is
    /// exactly what GameObject.Find reads, as it always was.
    public static class ScenePath
    {
        const char Mark = '*';
        static readonly List<GameObject> _roots = new List<GameObject>();

        public static string Of(Transform t)
        {
            if (t == null) return "";
            var sb = new StringBuilder();
            Append(t, sb);
            return sb.ToString();
        }

        /// The object at a path made by Of, or null.
        public static GameObject Find(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (path.IndexOf(Mark) < 0) return GameObject.Find(path);
            var parts = path.Split('/'); // parts[0] is empty: the path starts with '/'
            Transform at = null;
            for (int i = 1; i < parts.Length; i++)
            {
                Segment(parts[i], out string name, out int n);
                at = i == 1 ? Root(name, n) : Child(at, name, n);
                if (at == null) return null;
            }
            return at != null ? at.gameObject : null;
        }

        static void Append(Transform t, StringBuilder sb)
        {
            if (t.parent != null) Append(t.parent, sb);
            sb.Append('/').Append(t.name);
            int n = Earlier(t);
            if (n > 0) sb.Append(Mark).Append(n);
        }

        /// How many earlier siblings carry the same name.
        static int Earlier(Transform t)
        {
            string me = t.name;
            int n = 0;
            var p = t.parent;
            if (p != null)
            {
                for (int i = 0, end = t.GetSiblingIndex(); i < end; i++)
                    if (p.GetChild(i).name == me) n++;
                return n;
            }
            var scene = t.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) return 0;
            scene.GetRootGameObjects(_roots);
            foreach (var r in _roots)
            {
                if (r.transform == t) break;
                if (r.name == me) n++;
            }
            return n;
        }

        static void Segment(string seg, out string name, out int n)
        {
            name = seg;
            n = 0;
            int k = seg.LastIndexOf(Mark);
            if (k <= 0 || k == seg.Length - 1) return;
            int v = 0;
            for (int i = k + 1; i < seg.Length; i++)
            {
                char c = seg[i];
                if (c < '0' || c > '9') return;
                v = v * 10 + (c - '0');
            }
            name = seg.Substring(0, k);
            n = v;
        }

        static Transform Root(string name, int n)
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                scene.GetRootGameObjects(_roots);
                int seen = 0;
                foreach (var r in _roots)
                    if (r.name == name && seen++ == n) return r.transform;
            }
            return null;
        }

        static Transform Child(Transform parent, string name, int n)
        {
            int seen = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == name && seen++ == n) return c;
            }
            return null;
        }
    }
}
