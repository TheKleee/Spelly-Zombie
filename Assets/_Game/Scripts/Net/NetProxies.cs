using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Client stand-in for a HOST-simulated matter blob (netcode §3): kinematic,
    /// lerped to snapshots, no chemistry - but it keeps a real (disabled) Matter
    /// so grabs read the state rule and the LiquidVolume shell still wades you.
    public class NetMatterProxy : MonoBehaviour
    {
        public int HostId;
        public SurfaceMaterialType Mat;
        public MatterPhase Phase;

        Matter _matter;   // disabled - fields feed LiquidVolume, Update never runs
        Renderer _rend;
        Vector3 _tp, _tscale;
        Quaternion _trot;
        int _lastLook = -1;

        public static NetMatterProxy Build(int id, SurfaceMaterialType mat, MatterPhase phase,
            Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(phase == MatterPhase.Solid
                ? PrimitiveType.Cube : PrimitiveType.Sphere);
            go.name = "NetMatter_" + mat;
            go.transform.position = pos;
            go.transform.localScale = scale;

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; // snapshots own the position

            // the real Matter, DISABLED: Init builds the liquid shell/layer and the
            // surface tag, then the host keeps all the chemistry (netcode §3)
            var m = go.AddComponent<Matter>();
            m.Init(mat, phase, Mathf.Max(0.05f, scale.x));
            m.enabled = false;
            m.Touched = false;

            var proxy = go.AddComponent<NetMatterProxy>();
            proxy.HostId = id;
            proxy.Mat = mat;
            proxy.Phase = phase;
            proxy._matter = m;
            proxy._rend = go.GetComponent<Renderer>();
            proxy._tp = pos;
            proxy._trot = go.transform.rotation;
            proxy._tscale = scale;
            return proxy;
        }

        public void Target(Vector3 pos, Quaternion rot, Vector3 scale, byte look)
        {
            _tp = pos;
            _trot = rot;
            _tscale = scale;
            if (look == _lastLook) return;
            _lastLook = look;
            ApplyLook(look);
        }

        /// Mirror of Matter.Refresh, driven by the wire byte instead of live chemistry.
        void ApplyLook(byte look)
        {
            bool burning = (look & 1) != 0;
            bool molten = (look & 2) != 0;
            bool ice = (look & 4) != 0;
            if (_matter != null)
            {
                // approximate fields so wading effects behave (LiquidVolume reads them)
                _matter.Temperature = molten ? 400f : burning ? 200f : ice ? -25f : 18f;
                _matter.DarkAura = (look & 8) != 0;
            }
            if (_rend == null) return;
            var info = SurfaceMaterialDB.Info(Mat);
            Color c;
            MoteShade shade;
            if (burning) { c = new Color(1f, 0.4f, 0.1f, 0.95f); shade = MoteShade.Additive; }
            else if (Phase == MatterPhase.Gas) { c = new Color(0.9f, 0.92f, 0.95f, 0.4f); shade = MoteShade.Transparent; }
            else if (Phase == MatterPhase.Liquid) { c = info.LiquidColor; shade = molten ? MoteShade.Additive : MoteShade.Transparent; }
            else if (ice) { c = new Color(0.72f, 0.88f, 1f); shade = MoteShade.Opaque; }
            else { c = info.SolidColor; shade = MoteShade.Opaque; }
            _rend.sharedMaterial = Phase == MatterPhase.Solid
                ? MatterFX.Get(c, shade)
                : MatterFX.Particle(c, shade, 0.07f, 0.5f);
        }

        void Update()
        {
            float k = Time.deltaTime * 10f;
            transform.position = Vector3.Lerp(transform.position, _tp, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, _trot, k);
            transform.localScale = Vector3.Lerp(transform.localScale, _tscale, k);
        }
    }

    /// Client stand-in for a HOST-simulated spell particle (netcode §3): pure
    /// visual - no triggers, no chemistry. The grab aims at these and ships a
    /// ClaimIntent instead of touching physics.
    public class NetMoteProxy : MonoBehaviour
    {
        public int HostId;
        public ParticleKind Kind;

        static readonly List<NetMoteProxy> _all = new List<NetMoteProxy>();
        public static IReadOnlyList<NetMoteProxy> Living => _all;

        Vector3 _tp;
        float _ts = 0.14f;

        public static NetMoteProxy Build(int id, ParticleKind kind, Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "NetMote_" + kind;
            Object.Destroy(go.GetComponent<Collider>()); // visual only - never blocks a ray
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * (kind == ParticleKind.Push ? 0.09f : 0.14f);

            // SpellParticle.RefreshLook's palette, condensed - one look per kind
            Color c;
            MoteShade shade = MoteShade.Additive;
            switch (kind)
            {
                case ParticleKind.Spark: c = new Color(1f, 0.55f, 0.12f); break;
                case ParticleKind.Frost: c = new Color(0.6f, 0.85f, 1f); break;
                case ParticleKind.Light: c = new Color(1f, 0.97f, 0.8f); break;
                case ParticleKind.Dark: c = new Color(0.2f, 0.1f, 0.3f); shade = MoteShade.Transparent; break;
                case ParticleKind.Glue: c = new Color(0.4f, 0.8f, 0.35f); break;
                case ParticleKind.Repel: c = new Color(0.85f, 0.85f, 0.9f); break;
                case ParticleKind.Dense: c = new Color(0.75f, 0.55f, 0.3f); break;
                case ParticleKind.Spread: c = new Color(0.7f, 1f, 0.8f); break;
                case ParticleKind.Push: c = new Color(1f, 0.95f, 0.4f); break;
                case ParticleKind.Lightning: c = new Color(0.75f, 0.9f, 1f); break;
                case ParticleKind.Flame: c = new Color(1f, 0.45f, 0.08f); break;
                case ParticleKind.BlackHole: c = new Color(0.03f, 0.01f, 0.06f); shade = MoteShade.Transparent; break;
                case ParticleKind.BarrierMote: c = new Color(0.6f, 0.9f, 1f, 0.7f); shade = MoteShade.Transparent; break;
                default: c = Color.white; break;
            }
            go.GetComponent<Renderer>().sharedMaterial = MatterFX.Particle(c, shade, 0.04f,
                shade == MoteShade.Additive ? 0.9f : 0.35f);

            if (kind == ParticleKind.Light)
            {
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 4.5f;
                l.intensity = 2.2f;
                l.color = new Color(1f, 0.96f, 0.8f);
            }

            var p = go.AddComponent<NetMoteProxy>();
            p.HostId = id;
            p.Kind = kind;
            p._tp = pos;
            return p;
        }

        void Awake() => _all.Add(this);
        void OnDestroy() => _all.Remove(this);

        public void Target(Vector3 pos, float scale)
        {
            _tp = pos;
            _ts = scale;
        }

        void Update()
        {
            float k = Time.deltaTime * 10f;
            transform.position = Vector3.Lerp(transform.position, _tp, k);
            float s = Mathf.Lerp(transform.localScale.x, _ts, k);
            transform.localScale = Vector3.one * s;
        }
    }

    /// Client follower for a scene prop the HOST lifted/tore loose (netcode §4):
    /// the local copy goes kinematic and lerps to PropSnap targets.
    public class NetPropGhost : MonoBehaviour
    {
        Vector3 _tp;
        Quaternion _trot;
        bool _has;

        public void Target(Vector3 pos, Quaternion rot)
        {
            _tp = pos;
            _trot = rot;
            _has = true;
        }

        void Update()
        {
            if (!_has) return;
            float k = Time.deltaTime * 12f;
            transform.position = Vector3.Lerp(transform.position, _tp, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, _trot, k);
        }
    }

    /// Client display of a HOST seal (netcode §2): the gold ring, nothing else -
    /// no detection, no spell, no payload. Dies with SealEndMsg (or a timeout,
    /// in case the end packet is never seen).
    public class NetSealRing : MonoBehaviour
    {
        float _die;

        public static NetSealRing Show(Vector3[] pts, float duration)
        {
            var go = new GameObject("NetSealRing");
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = DrawingWorld.Instance != null ? DrawingWorld.Instance.LineMaterial : null;
            lr.widthMultiplier = DrawingConfig.InkWidth;
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.startColor = Stroke.SealColor;
            lr.endColor = Stroke.SealColor;
            var ring = go.AddComponent<NetSealRing>();
            ring._die = duration + 2f;
            return ring;
        }

        void Update()
        {
            _die -= Time.deltaTime;
            if (_die <= 0f) Destroy(gameObject);
        }
    }
}
