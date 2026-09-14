using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// His mischief spells, by number in the Acolyte Buffs doc: 1 Decoy,
    /// 2 Reveal, 3 Death needle, 4 Life needle, 5 Evaporation ink,
    /// 6 Transformation ink, 7 Aggressive, 8 Spreading. Everything runs on
    /// the host and reaches clients through what already replicates: motes,
    /// zombie beats, prop beats, fields, the book swap, and one curse message
    /// to the player it lands on.
    public enum MischiefKind : byte
    {
        None = 0, Decoy = 1, Reveal = 2, DeathNeedle = 3, LifeNeedle = 4,
        Evaporation = 5, Transformation = 6, Aggressive = 7, Spreading = 8,
        Restore = 9 // wire only: the Life curse undone
    }

    public static class MischiefLaw
    {
        /// The mischief an acolyte casts with a rune: the same table the deeds unlock by.
        public static MischiefKind Of(RuneType rune)
        {
            switch (rune)
            {
                case RuneType.HeatUp: return MischiefKind.Decoy;
                case RuneType.HeatDown: return MischiefKind.Reveal;
                case RuneType.StickyUp: return MischiefKind.DeathNeedle;
                case RuneType.StickyDown: return MischiefKind.LifeNeedle;
                case RuneType.LuminanceUp: return MischiefKind.Evaporation;
                case RuneType.LuminanceDown: return MischiefKind.Transformation;
                case RuneType.DensityUp: return MischiefKind.Aggressive;
                case RuneType.DensityDown: return MischiefKind.Spreading;
                default: return MischiefKind.None;
            }
        }

        public static bool IsDart(MischiefKind k) => k >= MischiefKind.Decoy && k <= MischiefKind.Transformation;
        public static bool IsBuff(MischiefKind k) => k == MischiefKind.Aggressive || k == MischiefKind.Spreading;

        /// The spellbook row each mischief spell is authored under (Spell Creator, acolyte book).
        static string RowName(MischiefKind k)
        {
            switch (k)
            {
                case MischiefKind.Decoy: return "Decoy";
                case MischiefKind.Reveal: return "Reveal";
                case MischiefKind.DeathNeedle: return "Death Needle";
                case MischiefKind.LifeNeedle: return "Life Needle";
                case MischiefKind.Evaporation: return "Evaporation Ink";
                case MischiefKind.Transformation: return "Transformation Ink";
                case MischiefKind.Aggressive: return "Aggressive";
                case MischiefKind.Spreading: return "Spreading";
                default: return null;
            }
        }

        static readonly HashSet<MischiefKind> _warnedRow = new HashSet<MischiefKind>();

        /// That row, from the acolyte book first; null and one warning when the book has none.
        public static SpellDef RowFor(MischiefKind k)
        {
            string name = RowName(k);
            if (name == null) return null;
            SpellDef any = null;
            foreach (var s in SpellBook.Live.spells)
            {
                if (s == null || !string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (s.Book == BookKind.Acolyte) return s;
                if (any == null) any = s;
            }
            if (any == null && _warnedRow.Add(k))
                Debug.LogWarning($"[SpellyZombie] No spell named '{name}' in the spellbook: that dart shows the plain mote. " +
                                 $"Name its row '{name}' in the Spell Creator.");
            return any;
        }

        static void Log(string s) => DrawingWorld.Instance?.LogEvent(s);

        /// The caster's own body: their dart leaves through it untouched.
        public static bool IsCasterBody(int caster, Collider c)
        {
            var pilot = c.GetComponentInParent<SimpleFPSController>();
            if (pilot != null) return pilot.IsLocalViewer && Grimoire.LocalPlayerId == caster;
            var puppet = c.GetComponentInParent<NetAvatar>();
            return puppet != null && NetSync.OwnerIdOf(puppet.Id) == caster;
        }

        /// The nearest living wizard within `range`, other than `except`: this
        /// machine's own body and every friend's puppet.
        public static bool NearestWizard(Vector3 from, float range, int except, out Vector3 at)
        {
            at = default;
            float best = range * range;
            bool any = false;
            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || !p.IsLocalViewer || p.IsDowned) continue;
                int owner = Grimoire.LocalPlayerId;
                if (owner == except || Sides.Of(owner) != Side.Wizard) continue;
                float d = (p.transform.position - from).sqrMagnitude;
                if (d < best) { best = d; at = p.transform.position; any = true; }
            }
            foreach (var a in NetAvatar.All)
            {
                if (a == null || a.Downed) continue;
                int owner = NetSync.OwnerIdOf(a.Id);
                if (owner == except || Sides.Of(owner) != Side.Wizard) continue;
                float d = (a.transform.position - from).sqrMagnitude;
                if (d < best) { best = d; at = a.transform.position; any = true; }
            }
            return any;
        }

        /// HOST: a dart landed on this collider. What it hit decides the shape
        /// of the effect; his law: none of it works on acolytes or the caster.
        public static void Land(SpellParticle dart, Collider c)
        {
            var kind = (MischiefKind)dart.Mischief;
            int caster = dart.OwnerId;
            float seconds = DrawingConfig.MischiefSeconds;
            Vector3 at = dart.transform.position;

            // the evaporation ink dries whatever ink lies where it burst, whoever it hit
            if (kind == MischiefKind.Evaporation)
                DrawingWorld.Instance?.EraseAt(at, DrawingConfig.EvaporateInkRadius);

            var pilot = c.GetComponentInParent<SimpleFPSController>();
            var puppet = c.GetComponentInParent<NetAvatar>();
            if (pilot != null || puppet != null)
            {
                int owner = pilot != null ? Grimoire.LocalPlayerId : NetSync.OwnerIdOf(puppet.Id);
                if (owner == caster || Sides.Of(owner) != Side.Wizard) return;
                var el = c.GetComponentInParent<Element>();
                switch (kind)
                {
                    case MischiefKind.Reveal: Revealed.Mark(el, seconds); break;
                    case MischiefKind.Decoy: NetSync.SendCurse(owner, (byte)kind, seconds, caster); break;
                    case MischiefKind.DeathNeedle:
                        // only a wizard you already killed (his rule)
                        if (!KillLedger.Killed(Element.IdFor("player:" + owner), caster)) { Log("the needle knows no such death"); return; }
                        el?.TakeDamage(999999f, "death needle", caster);
                        NetSync.SendCurse(owner, (byte)kind, seconds, caster);
                        break;
                    case MischiefKind.LifeNeedle:
                        // the same spell back at the one who cursed you undoes it (his rule)
                        if (Grimoires.CurserOf(caster) == owner) { NetSync.SendCurse(caster, (byte)MischiefKind.Restore, 0f, owner); return; }
                        // only a wizard who finished you off
                        if (!KillLedger.Killed(Element.IdFor("player:" + caster), owner)) { Log("only the one who finished you"); return; }
                        NetSync.SendCurse(owner, (byte)kind, DrawingConfig.LifeCurseSeconds, caster);
                        break;
                    case MischiefKind.Evaporation:
                        NetSync.SendCurse(owner, (byte)kind, DrawingConfig.InkMax * DrawingConfig.EvaporateWandFraction, caster);
                        break;
                    case MischiefKind.Transformation:
                        string shape = NetSync.LastScanOf(caster);
                        if (string.IsNullOrEmpty(shape)) { Log("scan something first"); return; }
                        NetSync.SendCurse(owner, (byte)kind, DrawingConfig.TransformSeconds, caster, shape);
                        break;
                }
                return;
            }

            var z = ZombieOwner.From(c);
            if (z != null)
            {
                if (kind == MischiefKind.Reveal) Revealed.Mark(z.GetComponent<Element>(), seconds);
                else if (kind == MischiefKind.Decoy)
                {
                    var brain = z.GetComponent<ZombieBrain>();
                    if (brain != null) brain.DecoyedUntil = Time.time + seconds;
                }
                else if (kind == MischiefKind.Transformation)
                {
                    string shape = NetSync.LastScanOf(caster);
                    if (string.IsNullOrEmpty(shape)) { Log("scan something first"); return; }
                    var brain = z.GetComponent<ZombieBrain>();
                    if (brain != null) brain.TransformedUntil = Time.time + DrawingConfig.TransformSeconds;
                    WornLook.Put(z.gameObject, shape, DrawingConfig.TransformSeconds);
                    NetSync.PushWorn(1, z.gameObject.GetInstanceID(), "", shape, DrawingConfig.TransformSeconds);
                }
                return;
            }

            switch (kind)
            {
                case MischiefKind.Reveal:
                    Revealed.Mark(c.GetComponentInParent<Element>(), seconds);
                    return;
                case MischiefKind.Decoy:
                {
                    // a rooted prop is torn loose first; a wall has no body and stays a wall
                    var rb = c.attachedRigidbody;
                    if (rb == null)
                    {
                        var lift = c.GetComponentInParent<Liftable>();
                        if (lift != null) rb = lift.TearLoose();
                    }
                    if (rb == null || rb.isKinematic) return;
                    Decoyed.Mark(rb, seconds);
                    return;
                }
                case MischiefKind.Evaporation:
                {
                    // the pot holding the ink dries a little wherever it is struck, whoever owns it
                    var pot = c.GetComponentInParent<CauldronEconomy>();
                    if (pot != null && pot == CauldronEconomy.Active)
                        pot.Evaporate(CauldronEconomy.Capacity * DrawingConfig.EvaporatePotFraction);
                    return;
                }
                case MischiefKind.Transformation:
                {
                    string shape = NetSync.LastScanOf(caster);
                    if (string.IsNullOrEmpty(shape)) { Log("scan something first"); return; }
                    // objects only, by the rule that says what an acolyte can scan: walls and ground stay
                    if (!ShapeShift.CanScan(c, out var root) || root == null) return;
                    string path = ScenePath.Of(root);
                    if (path == shape) return; // it already is that object
                    WornLook.Put(root.gameObject, shape, DrawingConfig.TransformSeconds);
                    NetSync.PushWorn(2, 0, path, shape, DrawingConfig.TransformSeconds);
                    return;
                }
            }
        }
    }

    /// Who ever finished whom, by the victim's Element id - the host's record,
    /// fed by every kill mark. The needles ask it: "did you kill them before",
    /// "did they finish you". Cleared with the marks.
    public static class KillLedger
    {
        static readonly Dictionary<int, HashSet<int>> _killers = new Dictionary<int, HashSet<int>>();

        public static void Record(int victimNetId, int by)
        {
            if (by < 0) return;
            if (!_killers.TryGetValue(victimNetId, out var s)) _killers[victimNetId] = s = new HashSet<int>();
            s.Add(by);
        }

        public static bool Killed(int victimNetId, int by) =>
            by >= 0 && _killers.TryGetValue(victimNetId, out var s) && s.Contains(by);

        public static void Clear() => _killers.Clear();
    }

    /// Reveal: whatever this is, it bursts into poison when it dies within the minute.
    public class Revealed : MonoBehaviour
    {
        float _until;
        Element _el;

        public static void Mark(Element el, float seconds)
        {
            if (el == null) return;
            var r = el.GetComponent<Revealed>();
            if (r == null) r = el.gameObject.AddComponent<Revealed>();
            r._until = Time.time + seconds;
            if (r._el == null) { r._el = el; el.OnDeath += r.Burst; }
        }

        void Burst(string cause)
        {
            Vector3 at = transform.position + Vector3.up * 0.4f;
            PoisonField.Open(at, DrawingConfig.RevealGasRadius, DrawingConfig.RevealGasSeconds);
            NetSync.PushField(3, at, DrawingConfig.RevealGasRadius, DrawingConfig.RevealGasSeconds);
        }

        void Update() { if (Time.time >= _until) Destroy(this); }
        void OnDestroy() { if (_el != null) _el.OnDeath -= Burst; }
    }

    /// Decoy on an object: it hops away from the nearest wizard while one is near.
    public class Decoyed : MonoBehaviour
    {
        float _until, _nextHop;
        Rigidbody _rb;

        public static void Mark(Rigidbody rb, float seconds)
        {
            var d = rb.GetComponent<Decoyed>();
            if (d == null) d = rb.gameObject.AddComponent<Decoyed>();
            d._rb = rb;
            d._until = Time.time + seconds;
        }

        void FixedUpdate()
        {
            if (_rb == null || Time.time >= _until) { Destroy(this); return; }
            if (Time.time < _nextHop) return;
            _nextHop = Time.time + DrawingConfig.DecoyHopSeconds;
            if (!MischiefLaw.NearestWizard(_rb.position, DrawingConfig.DecoyRange, -1, out var wizard)) return;
            Vector3 away = _rb.position - wizard;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Random.insideUnitSphere.normalized;
            away = Quaternion.Euler(0f, Random.Range(-50f, 50f), 0f) * away; // randomly, as he wrote
            _rb.WakeUp();
            _rb.AddForce((away * DrawingConfig.DecoyHopSpeed + Vector3.up * DrawingConfig.DecoyHopUp) * _rb.mass,
                ForceMode.Impulse);
            NetSync.TrackProp(_rb); // clients see it run (no-op while tracked)
        }
    }

    /// The zombie buffs: one at a time, the newest wins, a minute from the
    /// cast. Aggressive = nothing scares it; Spreading = it comes apart into
    /// two when killed. The pupils tell which (yellow, purple).
    public class ZombieBuff : MonoBehaviour
    {
        public MischiefKind Kind;
        public float Until;
        public int By;
        public bool Active => Time.time < Until;
        /// The pupil bit the zombie beat carries: 32 aggressive, 64 spreading.
        public byte EyeBit => !Active ? (byte)0 : Kind == MischiefKind.Aggressive ? (byte)32
            : Kind == MischiefKind.Spreading ? (byte)64 : (byte)0;
        ZombieBrain _brain;
        GooglyEyes _eyes;

        /// Every zombie this acolyte has alive right now, the seal's included.
        public static int ApplyToAll(int owner, MischiefKind kind, float seconds)
        {
            int n = 0;
            foreach (var z in Zombie.All)
            {
                if (z == null) continue;
                var s = z.GetComponent<SummonedZombie>();
                if (s == null || s.SummonedBy != owner) continue;
                Apply(z, kind, seconds, owner);
                n++;
            }
            return n;
        }

        public static void Apply(Zombie z, MischiefKind kind, float seconds, int by)
        {
            var b = z.GetComponent<ZombieBuff>();
            if (b == null) b = z.gameObject.AddComponent<ZombieBuff>();
            b.Kind = kind;
            b.Until = Time.time + seconds;
            b.By = by;
            b.Refresh();
        }

        void Refresh()
        {
            if (_brain == null) _brain = GetComponent<ZombieBrain>();
            if (_eyes == null) _eyes = _brain != null ? _brain.Eyes : GetComponentInChildren<GooglyEyes>();
            bool on = Active;
            if (_brain != null) _brain.BuffFearless = on && Kind == MischiefKind.Aggressive;
            if (_eyes != null)
                _eyes.SetPupilTint(!on ? (Color?)null
                    : Kind == MischiefKind.Aggressive ? DrawingConfig.AggressiveEyeColor
                    : DrawingConfig.SpreadingEyeColor);
        }

        void Update() { if (!Active) Destroy(this); }

        void OnDestroy()
        {
            if (_brain != null) _brain.BuffFearless = false;
            if (_eyes != null) _eyes.SetPupilTint(null);
        }
    }

    /// The transformation ink on a zombie or a prop: it wears another object's
    /// look for a while - a look-only clone centred where its body is, every
    /// other renderer hidden - on the host and on every client alike.
    public class WornLook : MonoBehaviour
    {
        float _until;
        GameObject _clone;
        readonly List<Renderer> _hidden = new List<Renderer>();

        public static void Put(GameObject target, string shapeKey, float seconds)
        {
            if (target == null) return;
            var src = ShapeShift.ResolveShape(shapeKey, out _);
            if (src == null) return;
            var w = target.GetComponent<WornLook>();
            if (w == null) w = target.AddComponent<WornLook>();
            w.Dress(src, seconds);
        }

        void Dress(Transform src, float seconds)
        {
            Undress();
            _until = Time.time + seconds;
            var mine = ShapeShift.FindObjectBounds(transform, true);
            _clone = ShapeShift.CloneShape(src, transform);
            // the object's own world size, whatever the body under it is scaled to
            Vector3 parent = transform.lossyScale, want = src.lossyScale;
            _clone.transform.localScale = new Vector3(
                want.x / Mathf.Max(0.0001f, parent.x), want.y / Mathf.Max(0.0001f, parent.y), want.z / Mathf.Max(0.0001f, parent.z));
            _clone.transform.rotation = src.rotation;
            Vector3 centre = ShapeShift.FindObjectCenterLocal(_clone.transform, true);
            _clone.transform.position += mine.center - _clone.transform.TransformPoint(centre);
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || r.transform.IsChildOf(_clone.transform)) continue;
                r.enabled = false;
                _hidden.Add(r);
            }
            if (FxLibrary.I != null && FxLibrary.I.Poof != null) FxLibrary.Spawn(FxLibrary.I.Poof, mine.center);
        }

        void Undress()
        {
            foreach (var r in _hidden) if (r != null) r.enabled = true;
            _hidden.Clear();
            if (_clone != null) Destroy(_clone);
            _clone = null;
        }

        void Update() { if (Time.time >= _until) { Undress(); Destroy(this); } }
        void OnDestroy() => Undress();
    }
}
