using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Hold the book open on the absorb page and every source a wizard can
    /// still learn from lights up; hold the scan page and every shape an
    /// acolyte can wear lights up. One shader, both sides.
    /// Eligibility is never re-decided here - the wizard list is the same
    /// filter GrimoireAbsorb.Best uses, the acolyte test is ShapeShift.CanScan.
    public class RevealOutline : MonoBehaviour
    {
        static RevealOutline _me;
        static Material _wizardMat, _acolyteMat;

        readonly List<Renderer> _lit = new List<Renderer>();
        readonly List<Renderer> _next = new List<Renderer>();
        static readonly Collider[] _buf = new Collider[64];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_me != null) return;
            var go = new GameObject("~RevealOutline");
            DontDestroyOnLoad(go);
            _me = go.AddComponent<RevealOutline>();
        }

        static Material Mat(ref Material cache, Color c)
        {
            if (cache != null) return cache;
            var sh = Shader.Find("SpellyZombie/SZOutline");
            if (sh == null) return null;
            cache = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            cache.SetColor("_Color", c);
            cache.SetFloat("_Width", DrawingConfig.OutlineWidth);
            return cache;
        }

        void LateUpdate()
        {
            _next.Clear();

            bool wizard = GrimoirePages.BookOpen && GrimoirePages.AbsorbPageOpen;
            bool acolyte = GrimoirePages.BookOpen && GrimoirePages.ScanPageOpen;

            if (wizard) GatherSources();
            else if (acolyte) GatherShapes();

            Apply(wizard ? Mat(ref _wizardMat, DrawingConfig.OutlineWizardColor)
                         : Mat(ref _acolyteMat, DrawingConfig.OutlineAcolyteColor));
        }

        /// Wizard: anything still holding a rune for me.
        void GatherSources()
        {
            int me = Grimoire.LocalPlayerId;
            foreach (var a in Analyzable.Living)
            {
                if (a == null || !a.CanTeach) continue;
                if (a.NextFor(me) == RuneType.None) continue;
                Collect(a.transform);
            }
        }

        /// Acolyte: anything the scan would actually accept, within reach.
        void GatherShapes()
        {
            var pilot = LocalPilot();
            if (pilot == null) return;
            int n = Physics.OverlapSphereNonAlloc(pilot.transform.position,
                DrawingConfig.OutlineRange, _buf,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (!ShapeShift.CanScan(_buf[i], out var root)) continue;
                Collect(root);
            }
        }

        static SimpleFPSController LocalPilot()
        {
            foreach (var p in SimpleFPSController.All)
                if (p != null && p.IsLocalViewer) return p;
            return null;
        }

        void Collect(Transform root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (r != null && r.enabled && !_next.Contains(r)) _next.Add(r);
        }

        /// Add the outline material to anything newly lit, strip it from
        /// anything that dropped out. Never touches the object's own materials.
        void Apply(Material outline)
        {
            for (int i = _lit.Count - 1; i >= 0; i--)
            {
                var r = _lit[i];
                if (r == null) { _lit.RemoveAt(i); continue; }
                if (_next.Contains(r)) continue;
                Strip(r);
                _lit.RemoveAt(i);
            }
            if (outline == null) return;
            foreach (var r in _next)
            {
                if (_lit.Contains(r)) continue;
                var mats = new List<Material>(r.sharedMaterials) { outline };
                r.sharedMaterials = mats.ToArray();
                _lit.Add(r);
            }
        }

        static void Strip(Renderer r)
        {
            var mats = new List<Material>(r.sharedMaterials);
            for (int i = mats.Count - 1; i >= 0; i--)
                if (mats[i] != null && mats[i].shader != null
                    && mats[i].shader.name == "SpellyZombie/SZOutline") mats.RemoveAt(i);
            r.sharedMaterials = mats.ToArray();
        }

        void OnDisable()
        {
            foreach (var r in _lit) if (r != null) Strip(r);
            _lit.Clear();
        }
    }
}
