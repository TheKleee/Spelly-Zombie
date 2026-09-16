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
        public byte Edges;
        /// The host blob's velocity and stickiness from the snapshot: this body
        /// is kinematic and its Matter never changes, so the local wader reads these.
        public Vector3 Flow;
        public float Stickiness;

        Matter _matter;   // disabled - fields feed LiquidVolume, Update never runs
        Renderer _rend;
        Vector3 _tp, _tscale;
        Quaternion _trot;
        int _lastLook = -1;
        bool _authored;   // wears a ShapeLibrary skin: its own material, tinted by glow only
        MaterialInfo _info;
        MaterialPropertyBlock _mpb;
        int _lastGlow = -1;
        static readonly int SquashID = Shader.PropertyToID("_Squash");
        static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

        public static NetMatterProxy Build(int id, SurfaceMaterialType mat, MatterPhase phase,
            Vector3 pos, Vector3 scale, byte edges = 0)
        {
            GameObject go = null;
            bool authored = false;
            // the same ShapeLibrary skin the host picked by line count (Matter.Spawn)
            if (phase == MatterPhase.Solid && edges > 0 && ShapeLibrary.Any)
            {
                var skin = ShapeLibrary.Find(mat, edges);
                if (skin != null)
                {
                    go = Instantiate(skin, pos, skin.transform.rotation);
                    go.transform.localScale = scale;
                    authored = true;
                }
            }
            if (go == null)
            {
                go = GameObject.CreatePrimitive(phase == MatterPhase.Solid
                    ? PrimitiveType.Cube : PrimitiveType.Sphere);
                go.transform.position = pos;
                go.transform.localScale = scale;
            }
            go.name = "NetMatter_" + mat;

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; // snapshots own the position

            // the real Matter, DISABLED: Init builds the liquid shell/layer and the
            // surface tag, then the host keeps all the chemistry (netcode §3)
            var m = go.GetComponent<Matter>();
            if (m == null) m = go.AddComponent<Matter>();
            m.Init(mat, phase, Mathf.Max(0.05f, scale.x));
            m.Edges = edges;
            m.enabled = false;
            m.Touched = false;
            // the same wobble body the host's code-built matter wears
            if (!authored && go.GetComponent<StateBlob>() == null) go.AddComponent<StateBlob>();

            // THE HOST'S NAME FOR IT: StateMsg (temperature, the whole board)
            // and riding particles address matter by this id
            (go.GetComponent<Element>() ?? go.AddComponent<Element>()).Rename(id);

            var proxy = go.AddComponent<NetMatterProxy>();
            proxy.HostId = id;
            proxy.Mat = mat;
            proxy.Phase = phase;
            proxy.Edges = edges;
            proxy._matter = m;
            proxy.Stickiness = m.Stickiness;
            proxy._authored = authored;
            proxy._info = SurfaceMaterialDB.Info(mat);
            proxy._rend = authored ? go.GetComponentInChildren<Renderer>() : go.GetComponent<Renderer>();
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

        /// Mirror of Matter.Refresh, driven by the wire byte instead of live
        /// chemistry. Temperature itself arrives by StateMsg on the Element.
        void ApplyLook(byte look)
        {
            bool burning = (look & 1) != 0;
            bool molten = (look & 2) != 0;
            bool ice = (look & 4) != 0;
            if (_matter != null) _matter.DarkAura = (look & 8) != 0;
            if (_rend == null) return;
            // the puddle's slump rides the high bits (Matter.NetLook)
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _rend.GetPropertyBlock(_mpb);
            _mpb.SetFloat(SquashID, ((look >> 4) & 15) / 15f * 0.35f);
            _rend.SetPropertyBlock(_mpb);
            if (_authored) return;   // the skin keeps its own material; Glow tints it
            var info = _info;
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
            _lastGlow = -1;
        }

        /// The hot-solid gradient of Matter.Refresh, from the real temperature.
        void Glow()
        {
            if (_rend == null || Phase != MatterPhase.Solid || _matter == null) return;
            float t = _matter.Temperature;
            int step = t > 120f ? 1 + (int)(t / 40f) : 0;
            if (step == _lastGlow) return;
            _lastGlow = step;
            if ((_lastLook & 5) != 0) return; // burning or ice own the colour
            Color c = step == 0 ? _info.SolidColor
                : Color.Lerp(_info.SolidColor, new Color(1f, 0.3f, 0.05f),
                    Mathf.InverseLerp(120f, _info.Meltable ? _info.MeltPoint : 500f, t));
            if (_authored)
            {
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                _rend.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorID, step == 0 ? Color.white : c);
                _rend.SetPropertyBlock(_mpb);
            }
            else _rend.sharedMaterial = MatterFX.Get(c, MoteShade.Opaque);
        }

        void Update()
        {
            Glow();
            float k = Time.deltaTime * 10f;
            transform.position = HandGrab.PredictCargo(transform, out var hand)
                ? PredictHeld(transform.position, hand, _tp)
                : Vector3.Lerp(transform.position, _tp, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, _trot, k);
            transform.localScale = Vector3.Lerp(transform.localScale, _tscale, k);
        }

        /// A proxy in the local hand rides the hand at the host's own hold
        /// pace; the snapshot still pulls it, gently, so the host's word wins
        /// over a whole round trip.
        public static Vector3 PredictHeld(Vector3 at, Vector3 hand, Vector3 snapshot)
        {
            at = Vector3.Lerp(at, hand, 14f * Time.deltaTime);
            return Vector3.Lerp(at, snapshot, 2f * Time.deltaTime);
        }
    }

    /// Client stand-in for a HOST-simulated spell particle (netcode §3): pure
    /// visual - no triggers, no chemistry. The grab aims at these and ships a
    /// ClaimIntent instead of touching physics.
    public class NetMoteProxy : MonoBehaviour
    {
        public int HostId;
        public ParticleKind Kind;
        public int OwnerId = -1;  // whose spell, from the snapshot

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
            p.Wear(tint, 1, 0, 0f, ParticleKind.Flame, 255);
            return p;
        }

        byte _area = 255;
        GameObject _areaLook;

        /// The authored look the host's area child wears (SpellParticle.WearArea),
        /// by its index in the book; the posed blob hides under it.
        void WearAreaLook(byte area)
        {
            _area = area;
            if (_areaLook != null) { Destroy(_areaLook); _areaLook = null; }
            var aoes = SpellBook.Live.aoes;
            var prefab = aoes != null && area < aoes.Count ? aoes[area].Prefab : null;
            if (prefab == null) return;
            _areaLook = Instantiate(prefab, transform);
            _areaLook.transform.localPosition = Vector3.zero;
            _areaLook.transform.localRotation = Quaternion.identity;
            foreach (var col in _areaLook.GetComponentsInChildren<Collider>(true)) Destroy(col);
        }

        /// Which posed blob this is showing; a change means rebuild.
        public byte Shape;
        /// Which spellbook row's authored look it wears, from the snapshot; 255 none.
        public byte Look = 255;

        byte _flags;
        float _reach;
        ParticleKind _idleKind = (ParticleKind)255;
        int _idleLevel = -1;
        bool _idleOn;
        Light _lamp;
        GameObject _idleFx;
        Renderer[] _rends;

        /// Colour and level, pushed through a property block so the authored
        /// material survives. The host computes both off the payload; a client
        /// has no payload, so it is told. flags/reach/kind: ParticleSnap's.
        public void Wear(Color32 tint, byte level, byte flags, float reach, ParticleKind kind, byte area)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            if (_rends == null) _rends = GetComponentsInChildren<Renderer>(true);
            bool areaChanged = area != _area;
            if (areaChanged) WearAreaLook(area);
            var tv = View();
            tv.Tint = tint;
            tv.DriveTint = true;
            _tint = tint;
            // only a lvl3 is a place: its ring is its true reach (BecomeBiome)
            _reach = reach;
            if (level >= 3 && _ring == null)
                _ring = GrammarFX.GroundRing(transform, new Color(1f, 1f, 1f, 0.5f));
            else if (level < 3 && _ring != null) { Destroy(_ring.gameObject); _ring = null; }

            if (flags != _flags || areaChanged)
            {
                _flags = flags;
                // a dormant preview is see-through, the host's own fade
                var view = View();
                if ((flags & 1) != 0) view.Fade(0.35f, 999999f); else view.ClearFade();
                view.PushNow();
                // a hidden area child is an invisible effect region, never a blob;
                // an authored area look replaces the blob the same way
                bool hidden = (flags & 4) != 0 || _areaLook != null;
                foreach (var r in _rends) if (r != null) r.enabled = !hidden;
                // a Light mote carries its lamp
                bool lit = (flags & 8) != 0;
                if (lit && _lamp == null)
                {
                    _lamp = gameObject.AddComponent<Light>();
                    _lamp.type = LightType.Point; _lamp.range = 4.5f; _lamp.intensity = 2.2f;
                    _lamp.color = new Color(1f, 0.96f, 0.8f);
                }
                else if (!lit && _lamp != null) { Destroy(_lamp); _lamp = null; }
            }

            // the idle flames or arcs the HOST's mote wears right now (flag 16):
            // a dormant ghost and a mote under an authored look wear none there,
            // so the client asks the wire, never the kind alone
            bool idle = (flags & 16) != 0;
            if (idle != _idleOn || (idle && (kind != _idleKind || level != _idleLevel)))
            {
                _idleOn = idle;
                _idleKind = kind;
                _idleLevel = level;
                if (_idleFx != null) { Destroy(_idleFx); _idleFx = null; }
                var pick = idle ? SpellParticle.IdleFxFor(kind, level) : null;
                if (pick != null)
                {
                    _idleFx = Instantiate(pick, transform.position, Quaternion.identity, transform);
                    _idleFx.name = "IdleFx";
                    _idleFx.transform.localScale *= 3.2f;
                }
            }

            WearRow(level);
        }

        /// ★ THE SLIDERS AND THE TRAIL COST NOTHING TO REPLICATE. The shape
        /// index already says which row this is, and every client has the same
        /// table - so a tornado spins on every screen because each machine
        /// looks up the same numbers, not because they were sent.
        void WearRow(byte level)
        {
            // the spellbook row the host names: the look the Spell Creator authored
            var def = Look != 255 ? SpellBook.Live.At(Look) : null;
            // and the book shape the host poses that row in, bone by bone
            string shapeName = def != null ? SpellParticle.LevelledShape(SpellParticle.ShapeOf(def), level) : null;
            if (shapeName != _posedAs)
            {
                var pose = shapeName != null ? SpellBook.Live.Shape(shapeName) : null;
                if (pose != null && pose.Bones != null && pose.Bones.Count > 0)
                {
                    SpellParticle.PoseNow(transform, pose);
                    _posedAs = shapeName;
                }
            }
            var art = CollectionManager.ParticleShapeAt(Shape);
            var row = art == null ? null : SpellTable.ByName(art.name) ?? SpellTable.ByName(StripLevel(art.name));

            // the trail is the AOE's, as on the host: an area child's own area, else the
            // named row's; the legacy table only when no book row is known
            var aoes = SpellBook.Live.aoes;
            bool booked = _area != 255 || def != null;
            var aoe = _area != 255 ? (aoes != null && _area < aoes.Count ? aoes[_area] : null)
                : def != null ? SpellBook.Live.Aoe(def.Aoe) : null;
            float trailWidth = booked ? (aoe != null ? aoe.TrailWidth : 0f) : row != null ? row.TrailWidth : 0f;
            float trailSeconds = booked ? (aoe != null ? aoe.TrailSeconds : 0f) : row != null ? row.TrailSeconds : 0f;
            if (trailWidth > 0f)
            {
                if (_tail == null) _tail = gameObject.AddComponent<TrailRenderer>();
                _tail.time = Mathf.Max(0.05f, trailSeconds);
                _tail.widthMultiplier = trailWidth;
                _tail.minVertexDistance = 0.08f;
                _tail.sharedMaterial = MatterFX.Get(_tint, MoteShade.Additive);
            }
            else if (_tail != null) { Destroy(_tail); _tail = null; }

            var skin = def != null && def.Skin != null ? def.Skin : row?.Skin;
            if (skin == null || (_skinned && _skinnedLook == Look)) return;
            _skinned = true;
            _skinnedLook = Look;
            var view = View();
            view.Look = skin;
            if (def != null) view.StateT = SpellPayload.StateT01(def.Payload.State);
            view.PushNow();
            if (!string.IsNullOrEmpty(skin.Fx))
                FxLibrary.SpawnNamed(skin.Fx, transform.position, transform);
        }

        /// "Attract 2" is the level-2 look of the Attract row.
        static string StripLevel(string n)
        {
            int sp = n.LastIndexOf(' ');
            return sp > 0 && sp == n.Length - 2 && char.IsDigit(n[n.Length - 1]) ? n.Substring(0, sp) : n;
        }

        static void Put(MaterialPropertyBlock b, string id, float v) => b.SetFloat(id, Mathf.Max(0f, v));

        int _rides;
        /// Held or riding on the host (net id), 0 when loose.
        public int Rides => _rides;

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
        byte _skinnedLook = 254;
        string _posedAs;
        StateView _view;

        /// The one writer of tint, look and fade on this body, as on the host.
        StateView View()
        {
            if (_view == null) _view = GetComponentInChildren<StateView>();
            if (_view == null) _view = gameObject.AddComponent<StateView>();
            return _view;
        }

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
            transform.position = HandGrab.PredictCargo(transform, out var hand)
                ? NetMatterProxy.PredictHeld(transform.position, hand, _tp)
                : Vector3.Lerp(transform.position, _tp, k);
            float s = Mathf.Lerp(transform.localScale.x, _ts, k);
            transform.localScale = Vector3.one * s;
            // the ring is drawn in the particle's own scale, so divide it out
            if (_ring != null && _reach > 0f)
                _ring.localScale = Vector3.one * (_reach / Mathf.Max(0.01f, s));
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
            transform.position = HandGrab.PredictCargo(transform, out var hand)
                ? NetMatterProxy.PredictHeld(transform.position, hand, _tp)
                : Vector3.Lerp(transform.position, _tp, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, _trot, k);
        }
    }

    /// Client display of a HOST seal (netcode §2): the gold ring, nothing else -
    /// no detection, no spell, no payload. Dies with SealEndMsg (or a timeout,
    /// in case the end packet is never seen).
    public class NetSealRing : MonoBehaviour
    {
        float _die;
        Transform _anchor;  // the surface the loop was drawn on: a lifted crate carries its ring
        Vector3[] _local;
        LineRenderer _lr;

        public static NetSealRing Show(Vector3[] pts, float duration, Transform anchor = null)
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
            ring._lr = lr;
            if (anchor != null)
            {
                ring._anchor = anchor;
                ring._local = new Vector3[pts.Length];
                for (int i = 0; i < pts.Length; i++) ring._local[i] = anchor.InverseTransformPoint(pts[i]);
            }
            return ring;
        }

        Transform _body; // a body seal: no loop, its zones ride this body

        /// A body seal's zone looks: no ring, riding the caster's body.
        public static NetSealRing OnBody(Transform body, float duration)
        {
            var go = new GameObject("NetBodySeal");
            go.transform.SetPositionAndRotation(body.position, body.rotation);
            var ring = go.AddComponent<NetSealRing>();
            ring._die = duration + 2f;
            ring._body = body;
            return ring;
        }

        // the zones' looks, built by Spell.BuildZoneVisual like the host's own
        readonly List<(GameObject go, Light light, float phase, float intensity)> _zones
            = new List<(GameObject, Light, float, float)>();

        public void AddZone(RuneType rune, Vector3 center, float radius, float intensity,
            Vector3 pushDir, float darkSpread)
        {
            var go = Spell.BuildZoneVisual(transform, rune, center, radius, intensity, pushDir, darkSpread,
                out var light);
            _zones.Add((go, light, Random.value * 6.28f, rune == RuneType.HeatUp ? intensity : 0f));
        }

        void Update()
        {
            _die -= Time.deltaTime;
            if (_die <= 0f) { Destroy(gameObject); return; }
            // the ember flicker of a heat zone (Spell.TickZone)
            for (int i = 0; i < _zones.Count; i++)
            {
                var z = _zones[i];
                if (z.light != null && z.intensity > 0f)
                    z.light.intensity = (0.2f + Mathf.PerlinNoise(Time.time * 9f, z.phase) * 0.3f) * z.intensity;
            }
            if (_body != null) transform.SetPositionAndRotation(_body.position, _body.rotation);
            if (_anchor == null || _local == null) return;
            for (int i = 0; i < _local.Length; i++) _lr.SetPosition(i, _anchor.TransformPoint(_local[i]));
        }
    }
}
