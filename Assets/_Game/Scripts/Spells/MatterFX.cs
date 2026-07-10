using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpellyZombie
{
    public enum MoteShade { Opaque, Transparent, Additive }

    /// Cached mote materials so spawning hundreds of particles doesn't allocate a
    /// material each. Opaque for solids (bricks, rock), transparent for fluids
    /// (water, air), additive for glowing stuff (fire) so overlapping motes read
    /// as one bright volume.
    public static class MatterFX
    {
        static readonly Dictionary<long, Material> _cache = new Dictionary<long, Material>();

        public static Material Get(Color c, MoteShade shade)
        {
            long key = ((long)shade << 40) ^ (uint)c.GetHashCode();
            if (_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(shader);
            if (shade != MoteShade.Opaque)
            {
                m.SetFloat("_Surface", 1f); // transparent
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", shade == MoteShade.Additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            }
            m.color = c;
            _cache[key] = m;
            return m;
        }
    }

}
