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

        public static NetMoteProxy Build(int id, byte shape, Color32 tint, Vector3 pos)
        {
            // ★ THE SAME POSED BLOB THE HOST IS WEARING. The shape index is
            // into the authored list, which is identical in every copy of a
            // build - so a client shows a tornado as a tornado without a name
            // being sent for every particle in every snapshot.
            var art = CollectionManager.ParticleShapeAt(shape) ?? CollectionManager.ParticleBlob;

            GameObject go;
            if (art != null)
            {
                go = Instantiate(art, pos, Quaternion.identity);
                foreach (var col in go.GetComponentsInChildren<Collider>(true))
                    Destroy(col);   // a proxy is a picture, never a body
            }
            else
            {
                // nothing authored yet: the old sphere, so a client is never
                // left looking at empty air
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(go.GetComponent<Collider>());
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * 0.14f;
            }

            go.name = "NetMote";
            var p = go.AddComponent<NetMoteProxy>();
            p.Shape = shape;
            p.Target(pos, go.transform.localScale.x);
            p.Wear(tint, 1);
            return p;
        }

        /// Which posed blob this is showing; a change means rebuild.
        public byte Shape;

        /// Colour and level, pushed through a property block so the authored
        /// material survives. The host computes both off the payload; a client
        /// has no payload, so it is told.
        public void Wear(Color32 tint, byte level)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.sharedMaterial == null
                    || !r.sharedMaterial.HasProperty("_StateT")) continue;   // eyes keep their own
                r.GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", tint);
                r.SetPropertyBlock(_block);
                _tint = tint;
            }
            if (level >= 2 && _ring == null)
                _ring = GrammarFX.GroundRing(transform, new Color(1f, 1f, 1f, 0.35f));
            else if (level < 2 && _ring != null) { Destroy(_ring.gameObject); _ring = null; }

            WearRow(level);
        }

        /// ★ THE SLIDERS AND THE TRAIL COST NOTHING TO REPLICATE. The shape
        /// index already says which row this is, and every client has the same
        /// table - so a tornado spins on every screen because each machine
        /// looks up the same numbers, not because they were sent.
        void WearRow(byte level)
        {
            var art = CollectionManager.ParticleShapeAt(Shape);
            if (art == null) return;
            var row = SpellTable.ByName(art.name) ?? SpellTable.ByName(StripLevel(art.name));
            if (row == null) return;

            if (row.TrailWidth > 0f)
            {
                if (_tail == null) _tail = gameObject.AddComponent<TrailRenderer>();
                _tail.time = Mathf.Max(0.05f, row.TrailSeconds);
                _tail.widthMultiplier = row.TrailWidth;
                _tail.minVertexDistance = 0.08f;
                _tail.sharedMaterial = MatterFX.Get(_tint, MoteShade.Additive);
            }
            else if (_tail != null) { Destroy(_tail); _tail = null; }

            if (row.Skin == null || _skinned) return;
            _skinned = true;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.sharedMaterial == null
                    || !r.sharedMaterial.HasProperty("_StateT")) continue;   // eyes keep their own
                r.GetPropertyBlock(_block);
                Put(_block, "_Wobble", row.Skin.Wobble);
                Put(_block, "_WobbleSpeed", row.Skin.WobbleSpeed);
                Put(_block, "_Swirl", row.Skin.Swirl);
                Put(_block, "_SwirlSpeed", row.Skin.SwirlSpeed);
                Put(_block, "_Turbulence", row.Skin.Turbulence);
                Put(_block, "_Bubbles", row.Skin.Bubbles);
                Put(_block, "_BubbleScale", row.Skin.BubbleSize);
                Put(_block, "_BubbleRise", row.Skin.BubbleRise);
                Put(_block, "_Holes", row.Skin.Holes);
                Put(_block, "_HoleScale", row.Skin.HoleSize);
                Put(_block, "_Rim", row.Skin.Rim);
                r.SetPropertyBlock(_block);
            }
            if (!string.IsNullOrEmpty(row.Skin.Fx))
                FxLibrary.SpawnNamed(row.Skin.Fx, transform.position, transform);
        }

        /// "Attract 2" is the level-2 look of the Attract row.
        static string StripLevel(string n)
        {
            int sp = n.LastIndexOf(' ');
            return sp > 0 && sp == n.Length - 2 && char.IsDigit(n[n.Length - 1]) ? n.Substring(0, sp) : n;
        }

        static void Put(MaterialPropertyBlock b, string id, float v) => b.SetFloat(id, Mathf.Max(0f, v));

        int _rides;

        /// Hang onto what the host says it caught, so it travels with its
        /// victim on every screen instead of being lerped after them.
        public void Ride(int hostNetId)
        {
            if (hostNetId == _rides) return;
            _rides = hostNetId;
            if (hostNetId == 0) { transform.SetParent(null, true); return; }
            var host = Element.ById(hostNetId);
            if (host != null) transform.SetParent(host.transform, true);
        }

        TrailRenderer _tail;
        Color32 _tint = new Color32(255, 255, 255, 255);
        bool _skinned;

        MaterialPropertyBlock _block;
        Transform _ring;

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
