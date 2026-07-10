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
        static readonly Dictionary<(Color, int, int, int), Material> _pcache
            = new Dictionary<(Color, int, int, int), Material>();

        static Shader _particleShader;
        static bool _searched;
        static Shader ParticleShader
        {
            get
            {
                if (!_searched)
                {
                    _searched = true;
                    _particleShader = Shader.Find("SpellyZombie/Particle");
                }
                return _particleShader;
            }
        }

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

        /// The living look: SZParticle shader with jelly wobble + fresnel rim.
        /// Spell particles and liquid/gas matter wear this; squash is driven
        /// per-object via MaterialPropertyBlock (materials stay shared).
        /// Falls back to the flat Get() look if the shader didn't import.
        public static Material Particle(Color c, MoteShade shade, float wobble, float rim)
        {
            var sh = ParticleShader;
            if (sh == null) return Get(c, shade);

            var key = (c, (int)shade, Mathf.RoundToInt(wobble * 1000f), Mathf.RoundToInt(rim * 100f));
            if (_pcache.TryGetValue(key, out var cached) && cached != null) return cached;

            var m = new Material(sh);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_WobbleAmp", wobble);
            m.SetFloat("_Rim", rim);
            if (shade == MoteShade.Opaque)
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                m.SetFloat("_ZWrite", 1f);
                m.renderQueue = (int)RenderQueue.Geometry;
            }
            else
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", shade == MoteShade.Additive
                    ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            _pcache[key] = m;
            return m;
        }
    }

}
