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

        /// What a mischief spell sounds like: the wizard sound of the rune it is cast with (the table above).
        public static Sfx SoundOf(MischiefKind k)
        {
            switch (k)
            {
                case MischiefKind.Decoy: return Sfx.HeatImpact;
                case MischiefKind.Reveal: return Sfx.ChillImpact;
                case MischiefKind.DeathNeedle: return Sfx.StickyImpact;
                case MischiefKind.LifeNeedle: return Sfx.SlickImpact;
                case MischiefKind.Evaporation: return Sfx.LightImpact;
                case MischiefKind.Transformation: return Sfx.DarkImpact;
                case MischiefKind.Aggressive: return Sfx.CompressImpact;
                default: return Sfx.ExpandImpact;
            }
        }

        public static bool IsDart(MischiefKind k) => k >= MischiefKind.Decoy && k <= MischiefKind.Transformation;

        /// The rune a mischief spell is cast with (the table above, read back).
        public static RuneType RuneOf(MischiefKind k)
        {
            foreach (RuneType r in System.Enum.GetValues(typeof(RuneType)))
                if (Of(r) == k) return r;
            return RuneType.None;
        }

        /// Death Needle's rule: only a wizard this caster already killed.
        public static bool DeathTarget(int wizard, int caster) =>
            KillLedger.Killed(Element.IdFor("player:" + wizard), caster);

        /// Life Needle's rule: the wizard who finished this caster, or the acolyte whose curse the
        /// caster is under (thrown back, it undoes the curse).
        public static bool LifeTarget(int target, int caster) =>
            Grimoires.CurserOf(caster) == target || KillLedger.Killed(Element.IdFor("player:" + caster), target);

        /// Which needles of this owner exist right now, asleep, held or flying: the host counts its
        /// darts, a client its stand-ins of them.
        public static void NeedlesOf(int owner, out bool death, out bool life)
        {
            int got = 0;
            if (NetGame.IsAuthority)
            {
                foreach (var p in SpellParticle.Living)
                    if (p != null && !p.Dead && p.OwnerId == owner) got |= p.DartBits;
            }
            else
                foreach (var m in NetMoteProxy.Living)
                    if (m != null && m.OwnerId == owner) got |= m.Darts;
            death = (got & (1 << (int)MischiefKind.DeathNeedle)) != 0;
            life = (got & (1 << (int)MischiefKind.LifeNeedle)) != 0;
        }

        /// The effect a spell of this kind shows where it happens, from the library.
        public static GameObject FxOf(MischiefKind k)
        {
            var lib = FxLibrary.I;
            if (lib == null) return null;
            switch (k)
            {
                case MischiefKind.Decoy: return Slot(lib.MischiefDecoy, "MischiefDecoy");
                case MischiefKind.Reveal: return Slot(lib.MischiefReveal, "MischiefReveal");
                case MischiefKind.DeathNeedle: return Slot(lib.MischiefDeathNeedle, "MischiefDeathNeedle");
                case MischiefKind.LifeNeedle: return Slot(lib.MischiefLifeNeedle, "MischiefLifeNeedle");
                case MischiefKind.Evaporation: return Slot(lib.MischiefEvaporation, "MischiefEvaporation");
                case MischiefKind.Transformation: return Slot(lib.MischiefTransformation, "MischiefTransformation");
                case MischiefKind.Aggressive: return Slot(lib.MischiefAggressive, "MischiefAggressive");
                case MischiefKind.Spreading: return Slot(lib.MischiefSpreading, "MischiefSpreading");
                default: return null;
            }
        }

        static readonly HashSet<string> _emptySlots = new HashSet<string>();

        /// A spell's effect grown with its might (the relay carries the size).
        static void Grow(GameObject fx, float might)
        {
            if (fx != null) fx.transform.localScale *= Mathf.Clamp(Mathf.Sqrt(might), 1f, 3f);
        }

        /// A library slot; an empty one says so once, that spell then shows nothing there.
        public static GameObject Slot(GameObject prefab, string name)
        {
            if (prefab == null && _emptySlots.Add(name))
                Debug.LogWarning($"[SpellyZombie] FxLibrary: the '{name}' slot is empty, so that moment shows nothing. Drop a prefab in.");
            return prefab;
        }
        public static bool IsBuff(MischiefKind k) => k == MischiefKind.Aggressive || k == MischiefKind.Spreading;

        /// The mischief a book row casts (a creature casting "Decoy" throws the decoy dart).
        public static MischiefKind KindOfRow(string spellName)
        {
            for (int k = 1; k <= (int)MischiefKind.Spreading; k++)
                if (string.Equals(RowName((MischiefKind)k), spellName, System.StringComparison.OrdinalIgnoreCase))
                    return (MischiefKind)k;
            return MischiefKind.None;
        }

        /// The spellbook row each mischief spell is authored under (Spell Creator, acolyte book).
        public static string RowName(MischiefKind k)
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

        /// What a dart never turns into a free body: players, creatures, held weapons, the pot and its bowl.
        static bool NeverFreed(Rigidbody rb) =>
            rb.GetComponentInParent<SimpleFPSController>() != null
            || rb.GetComponentInParent<CharacterRig>() != null
            || rb.GetComponentInParent<Creature>() != null
            || rb.GetComponentInParent<HeldWeapon>() != null
            || rb.GetComponentInParent<CauldronEconomy>() != null
            || rb.GetComponentInParent<VesselShell>() != null;

        /// An object an acolyte's dart takes: what an acolyte may scan, minus the pot, its bowl and
        /// a held weapon, with its numbers alive.
        static bool IsObject(Collider c, out Element el)
        {
            el = null;
            if (!ShapeShift.CanScan(c, out _)) return false;
            if (c.GetComponentInParent<CauldronEconomy>() != null || VesselShell.VesselOf(c) != null
                || c.GetComponentInParent<HeldWeapon>() != null) return false;
            el = c.GetComponentInParent<Element>();
            return el != null && el.Health > 0f;
        }

        /// A prop's body made free to move: a rooted one torn loose, a resting kinematic one freed
        /// the way a lift frees it. Walls, world-scale things and bodies stay as they are (null).
        static Rigidbody FreeBody(Collider c, string dart)
        {
            var rb = c.attachedRigidbody;
            if (rb == null)
            {
                var lift = c.GetComponentInParent<Liftable>();
                if (lift != null) rb = lift.TearLoose();
            }
            if (rb == null || !rb.isKinematic) return rb;
            if (NeverFreed(rb) || Liftable.WorldScale(rb.transform, out _))
            {
                Debug.Log($"[SpellyZombie] the {dart} leaves {rb.name} where it is: not a prop");
                return null;
            }
            Liftable.MakePhysicsLegal(rb.transform);
            rb.isKinematic = false;
            var lf = rb.GetComponent<Liftable>();
            if (lf != null) lf.Rooted = false;
            HandGrab.WakeRiders(rb);
            return rb;
        }

        /// A summon the needles take: no boss, and not already on the caster's side. `side` = whom it
        /// serves after a Life Needle: the acolyte, a cursed wizard's curser.
        static bool SummonTaken(Component body, int owner, int caster, out int side)
        {
            side = Grimoires.SummonerFor(caster);
            if (body.GetComponentInParent<BossMark>() != null)
            {
                Debug.Log($"[SpellyZombie] the needle cannot touch {body.name}: a boss");
                return false;
            }
            return Teams.Enemies(Teams.OfOwner(owner), Teams.OfOwner(side));
        }

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

        /// HOST: a dart landed on this collider: its own spell, then every other it carries
        /// (sleeping darts combine).
        public static void Land(SpellParticle dart, Collider c)
        {
            Land((MischiefKind)dart.Mischief, dart, c);
            for (int k = 1; k <= (int)MischiefKind.Transformation; k++)
                if (k != dart.Mischief && (dart.MischiefMore & (1 << k)) != 0) Land((MischiefKind)k, dart, c);
        }

        /// What it hit decides the shape of the effect; his law: none of it works on acolytes
        /// or the caster.
        static void Land(MischiefKind kind, SpellParticle dart, Collider c)
        {
            int caster = dart.OwnerId;
            // one clean rune in a circle = 1; joined runes and a triangle add to it, every effect scales
            float might = Mathf.Max(0.2f, dart.MightOf(kind));
            float seconds = DrawingConfig.MischiefSeconds * might;
            float transformFor = DrawingConfig.TransformSeconds * might;
            Vector3 at = dart.transform.position;
            Debug.Log($"[SpellyZombie] {kind} dart from player {caster} lands on {c.transform.root.name}/{c.name} x{might:0.0}");
            var burst = FxLibrary.Spawn(FxOf(kind), at); // its own burst, on every machine
            if (burst != null && dart.IsDebris) burst.transform.localScale *= 0.5f; // debris bursts small

            // the evaporation ink dries whatever ink lies where it burst, whoever it hit
            if (kind == MischiefKind.Evaporation)
                DrawingWorld.Instance?.EraseAt(at, DrawingConfig.EvaporateInkRadius * Mathf.Sqrt(might));

            var pilot = c.GetComponentInParent<SimpleFPSController>();
            var puppet = c.GetComponentInParent<NetAvatar>();
            if (pilot != null || puppet != null)
            {
                int owner = pilot != null ? Grimoire.LocalPlayerId : NetSync.OwnerIdOf(puppet.Id);
                // the Life Needle back at the one who cursed you undoes it (his rule), acolyte or not
                if (kind == MischiefKind.LifeNeedle && owner != caster && Grimoires.CurserOf(caster) == owner)
                {
                    NetSync.SendCurse(caster, (byte)MischiefKind.Restore, 0f, owner);
                    return;
                }
                if (owner == caster || Sides.Of(owner) != Side.Wizard)
                {
                    Debug.Log($"[SpellyZombie] the {kind} dart does nothing to player {owner}: "
                        + (owner == caster ? "the caster" : "not a wizard"));
                    return;
                }
                var el = c.GetComponentInParent<Element>();
                switch (kind)
                {
                    case MischiefKind.Reveal: Revealed.Mark(el, seconds, caster); break;
                    case MischiefKind.Decoy: NetSync.SendCurse(owner, (byte)kind, seconds, caster); break;
                    case MischiefKind.DeathNeedle:
                        // only a wizard you already killed (his rule)
                        if (!DeathTarget(owner, caster)) { Log("the needle knows no such death"); return; }
                        el?.TakeDamage(999999f, "death needle", caster);
                        NetSync.SendCurse(owner, (byte)kind, seconds, caster);
                        break;
                    case MischiefKind.LifeNeedle:
                        // only a wizard who finished you off
                        if (!LifeTarget(owner, caster)) { Log("only the one who finished you"); return; }
                        NetSync.SendCurse(owner, (byte)kind, DrawingConfig.LifeCurseSeconds * might, caster);
                        break;
                    case MischiefKind.Evaporation:
                    {
                        NetSync.SendCurse(owner, (byte)kind, DrawingConfig.InkMax * DrawingConfig.EvaporateWandFraction * might, caster);
                        // his ink boils off him where everyone sees it
                        var body = Element.ById(Element.IdFor("player:" + owner));
                        if (body != null)
                            Grow(FxLibrary.Spawn(FxOf(kind), ShapeShift.FindObjectBounds(body.transform).center,
                                body.transform, 2f), might);
                        break;
                    }
                    case MischiefKind.Transformation:
                        string shape = NetSync.LastScanOf(caster);
                        if (string.IsNullOrEmpty(shape)) { Log("scan something first"); return; }
                        NetSync.SendCurse(owner, (byte)kind, transformFor, caster, shape);
                        break;
                }
                return;
            }

            var z = ZombieOwner.From(c);
            if (z != null)
            {
                if (kind == MischiefKind.Reveal) Revealed.Mark(z.GetComponent<Element>(), seconds, caster);
                else if (kind == MischiefKind.Decoy)
                {
                    var brain = z.GetComponent<ZombieBrain>();
                    if (brain != null) brain.DecoyedUntil = Mathf.Max(brain.DecoyedUntil, Time.time + seconds); // a second dart only lengthens it
                }
                else if (kind == MischiefKind.Transformation)
                {
                    string shape = NetSync.LastScanOf(caster);
                    if (string.IsNullOrEmpty(shape)) { Log("scan something first"); return; }
                    var brain = z.GetComponent<ZombieBrain>();
                    if (brain != null) brain.TransformedUntil = Mathf.Max(brain.TransformedUntil, Time.time + transformFor);
                    WornLook.Put(z.gameObject, shape, transformFor);
                    NetSync.PushWorn(1, z.gameObject.GetInstanceID(), "", shape, transformFor);
                }
                else if (kind == MischiefKind.DeathNeedle || kind == MischiefKind.LifeNeedle)
                {
                    var summon = z.GetComponent<SummonedZombie>(); // the map's own horde is nobody's summon
                    if (summon == null || !SummonTaken(z, summon.SummonedBy, caster, out int side)) return;
                    if (kind == MischiefKind.DeathNeedle) z.GetComponent<Element>()?.TakeDamage(999999f, "death needle", caster);
                    else summon.Serve(side);
                }
                return;
            }

            // ★ SUMMONS (his rule): the Death Needle kills one outright, the Life Needle makes it the
            // acolytes'; never a boss. A living object is still an object (the switch below).
            var golem = c.GetComponentInParent<Golem>();
            if (golem != null && golem.GetComponent<LivingObject>() == null
                && (kind == MischiefKind.DeathNeedle || kind == MischiefKind.LifeNeedle))
            {
                if (!SummonTaken(golem, golem.OwnerId, caster, out int side)) return;
                if (kind == MischiefKind.DeathNeedle) golem.GetComponent<Element>()?.TakeDamage(999999f, "death needle", caster);
                else golem.OwnerId = side; // the snapshots tell every machine
                return;
            }

            switch (kind)
            {
                case MischiefKind.Reveal:
                    Revealed.Mark(c.GetComponentInParent<Element>(), seconds, caster);
                    return;
                case MischiefKind.DeathNeedle:
                {
                    // any object dies at once (his call); walls, the ground, the pot and a held weapon are not objects
                    if (!IsObject(c, out var el)) return;
                    // a prop with no break of its own still goes as what it is made of, heard by everyone
                    if (el.GetComponent<Breakable>() == null)
                        NetSync.PlayAndPushBodyFx(NetSync.BreakFx(el), ShapeShift.FindObjectBounds(el.transform).center);
                    el.TakeDamage(999999f, "death needle", caster);
                    return;
                }
                case MischiefKind.LifeNeedle:
                {
                    // his rule: it gets eyes and lives as the caster's for the needle's time; only one object
                    // every machine has, its body and its life on one root, asked before anything is freed
                    if (!IsObject(c, out var el)) return;
                    var lift = c.attachedRigidbody == null ? c.GetComponentInParent<Liftable>() : null;
                    var root = c.attachedRigidbody != null ? c.attachedRigidbody.gameObject : lift != null ? lift.gameObject : null;
                    if (!el.PathNamed || root != el.gameObject)
                    {
                        Debug.Log($"[SpellyZombie] the life needle leaves {el.name} as it is: not one object every machine has");
                        return;
                    }
                    var rb = FreeBody(c, "life needle");
                    if (rb == null) return;
                    LivingObject.Wake(rb, Grimoires.SummonerFor(caster), DrawingConfig.LifeCurseSeconds * might);
                    return;
                }
                case MischiefKind.Decoy:
                {
                    // a rooted prop is torn loose first; a wall has no body and stays a wall
                    var rb = FreeBody(c, "decoy");
                    if (rb == null) return;
                    NetSync.TrackProp(rb); // clients see it run
                    Decoyed.Mark(rb, seconds);
                    return;
                }
                case MischiefKind.Evaporation:
                {
                    // the ink drawn on the object boils off, all of it, a line still being drawn too (his call)
                    if (ShapeShift.CanScan(c, out var inked) && inked != null)
                    {
                        Bounds ib = ShapeShift.FindObjectBounds(inked);
                        DrawingWorld.Instance?.EraseAt(ib.center, ib.extents.magnitude + 0.1f);
                    }
                    // the pot holding the ink dries a little wherever it is struck, whoever owns it;
                    // its bowl shell is a separate root object, and a hit there is a hit on the pot
                    var vessel = VesselShell.VesselOf(c);
                    var pot = (vessel != null ? vessel : c.transform).GetComponentInParent<CauldronEconomy>();
                    if (pot != null && pot == CauldronEconomy.Active)
                    {
                        pot.Evaporate(CauldronEconomy.Capacity * DrawingConfig.EvaporatePotFraction * might
                            * (dart.FromMinion ? DrawingConfig.MinionEvaporateShare : 1f));
                        // the ink boils off where everyone sees it
                        Vector3 top = pot.InkSurface != null ? pot.InkSurface.position : pot.transform.position + Vector3.up * 0.6f;
                        Grow(FxLibrary.Spawn(FxOf(kind), top, null, 2f), might);
                        Juice.Sound(Sfx.LiquidImpact, top, 0.8f, 0.75f); // its ink sloshing away: low, the pot is big
                    }
                    return;
                }
                case MischiefKind.Transformation:
                {
                    string shape = NetSync.LastScanOf(caster);
                    if (string.IsNullOrEmpty(shape)) { Log("scan something first"); return; }
                    // objects only, by the rule that says what an acolyte can scan: walls and ground stay
                    if (!ShapeShift.CanScan(c, out var root) || root == null)
                    {
                        Debug.Log($"[SpellyZombie] nothing to transform: {c.transform.root.name} is not an object");
                        return;
                    }
                    string path = ScenePath.Of(root);
                    if (path == shape) return; // it already is that object
                    WornLook.Put(root.gameObject, shape, transformFor);
                    NetSync.PushWorn(2, 0, path, shape, transformFor);
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

        static readonly List<int> _partners = new List<int>();

        public static void Record(int victimNetId, int by)
        {
            if (by < 0) return;
            if (!_killers.TryGetValue(victimNetId, out var s)) _killers[victimNetId] = s = new HashSet<int>();
            s.Add(by);
            // a combined seal's kill is everyone's who cast it (his call)
            CoCast.PartnersOf(by, _partners);
            foreach (int p in _partners) s.Add(p);
        }

        public static bool Killed(int victimNetId, int by) =>
            by >= 0 && _killers.TryGetValue(victimNetId, out var s) && s.Contains(by);

        public static void Clear() => _killers.Clear();
    }

    /// Reveal: whatever this is, it bursts into poison when it dies within the minute. Every
    /// machine marks it (the host tells the others, NetSync.RevealMsg): its trail in the acolyte's
    /// ink keeps the whole path since the mark for the whole reveal, and only the acolyte team sees it.
    public class Revealed : MonoBehaviour
    {
        const float TrailTail = 0.4f; // the oldest stretch of the trail still shows this much of its ink

        float _until, _since;
        Element _el;
        GameObject _trail;
        TrailRenderer[] _trails;

        int _by = -1; // the dart's caster: a kill by the burst is theirs

        public static void Mark(Element el, float seconds, int caster)
        {
            if (el == null) return;
            var r = el.GetComponent<Revealed>();
            if (r == null) { r = el.gameObject.AddComponent<Revealed>(); r._since = Time.time; }
            r._until = Mathf.Max(r._until, Time.time + seconds); // a second dart only lengthens it
            r._by = caster;
            if (r._el == null) { r._el = el; el.OnDeath += r.Burst; }
            NetSync.SendReveal(el.NetId, seconds, caster); // the host tells every other machine
            r.DressTrail();
        }

        /// The mark rides the marked thing for the whole reveal and keeps its path since the mark.
        void DressTrail()
        {
            float left = _until - Time.time;
            var lib = FxLibrary.I;
            if (lib != null && (_trail == null || !_trail.activeInHierarchy))
            {
                _trail = FxLibrary.SpawnTinted(MischiefLaw.Slot(lib.RevealMark, "RevealMark"),
                    ShapeShift.FindObjectBounds(transform).center, DrawingConfig.CorruptInkColor,
                    transform, left, relay: false);
                _trails = _trail != null ? _trail.GetComponentsInChildren<TrailRenderer>(true) : null;
            }
            if (_trail == null) return;
            var keeper = _trail.GetComponent<FxReturn>();
            if (keeper != null) keeper.Arm(left); // as long as the reveal, lengthened with it
            if (_trails == null) return;
            Color c = DrawingConfig.CorruptInkColor;
            foreach (var tr in _trails)
            {
                if (tr == null) continue;
                tr.time = _until - _since;
                tr.endColor = new Color(c.r, c.g, c.b, TrailTail);
            }
            ShowTrail();
        }

        /// The acolyte team's mark: on every other machine it stays unseen.
        void ShowTrail()
        {
            if (_trails == null) return;
            bool ours = Sides.Of(Grimoire.LocalPlayerId) == Side.Acolyte;
            foreach (var tr in _trails)
                if (tr != null && tr.enabled != ours) tr.enabled = ours;
        }

        void Burst(string cause)
        {
            if (!NetGame.IsAuthority) return; // the host's gas reaches everyone as a field
            Vector3 at = transform.position + Vector3.up * 0.4f;
            var gas = PoisonField.Open(at, DrawingConfig.RevealGasRadius, DrawingConfig.RevealGasSeconds);
            gas.Owner = _by;
            NetSync.PushField(3, at, DrawingConfig.RevealGasRadius, DrawingConfig.RevealGasSeconds, 0, _by);
            Juice.Sound(Sfx.ChillImpact, at, 1f, 0.75f); // the reveal answering, deeper: its cloud is bigger than its dart
        }

        void Update()
        {
            ShowTrail(); // a side can change while it lasts
            if (Time.time < _until) return;
            // the acolytes hear their mark let go: a small pop where it was
            if (Sides.Of(Grimoire.LocalPlayerId) == Side.Acolyte)
                Juice.Sound(Sfx.InkPop2, ShapeShift.FindObjectBounds(transform).center, 0.5f, 1.1f, false);
            // over: the pooled trail goes back as it came, seen by all, for the next one that wears it
            if (_trails != null) foreach (var tr in _trails) if (tr != null) tr.enabled = true;
            if (_trail != null && _trail.activeInHierarchy) FxLibrary.Recycle(_trail);
            Destroy(this);
        }

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
            d._until = Mathf.Max(d._until, Time.time + seconds); // a second dart only lengthens it
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
            var lib = FxLibrary.I;
            if (lib != null) FxLibrary.Spawn(MischiefLaw.Slot(lib.DecoyHop, "DecoyHop"), _rb.position);
        }
    }

    /// The summon buffs: one at a time, the newest wins, a minute from the
    /// cast. Aggressive = nothing scares it; Spreading = it comes apart into
    /// two when killed. The pupils tell which (yellow, purple). Every summon
    /// of the caster's takes them: zombies, golems, living objects.
    public class ZombieBuff : MonoBehaviour
    {
        public MischiefKind Kind;
        public float Until;
        public int By;
        public bool Active => Time.time < Until;
        /// The pupil bit the creature beats carry: 32 aggressive, 64 spreading.
        public byte EyeBit => !Active ? (byte)0 : Kind == MischiefKind.Aggressive ? (byte)32
            : Kind == MischiefKind.Spreading ? (byte)64 : (byte)0;
        ZombieBrain _brain;
        Golem _golem;
        GooglyEyes _eyes;

        /// Every summon this player has alive right now: their zombies (the seal's included), their
        /// golems and their living objects, carried ones too.
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
            if (owner < 0) return n; // a wild golem is nobody's
            n += ApplyTo(Golem.All, owner, kind, seconds);
            n += ApplyTo(Golem.Carried, owner, kind, seconds);
            return n;
        }

        static int ApplyTo(List<Golem> golems, int owner, MischiefKind kind, float seconds)
        {
            int n = 0;
            foreach (var g in golems)
            {
                if (g == null || g.OwnerId != owner || !g.Alive) continue;
                Apply(g, kind, seconds, owner);
                n++;
            }
            return n;
        }

        public static void Apply(Component body, MischiefKind kind, float seconds, int by)
        {
            var b = body.GetComponent<ZombieBuff>();
            if (b == null) b = body.gameObject.AddComponent<ZombieBuff>();
            b.Kind = kind;
            b.Until = Time.time + seconds;
            b.By = by;
            b.Refresh();
            var t = body.transform;
            var golem = body as Golem;
            Juice.Sound(MischiefLaw.SoundOf(kind), t.position + Vector3.up * (golem != null ? golem.BodyScale : t.localScale.y), 0.8f);
            // the rally shows on the body, here and on its stand-ins
            var fx = FxLibrary.Spawn(MischiefLaw.FxOf(kind), t.position + Vector3.up * 0.3f, t, 1.3f);
            // a living object's root keeps its import scale (a kit prop is x100): the rally a golem its size wears
            if (fx != null && golem != null && golem.ObjectScale > 0f)
                fx.transform.localScale *= golem.ObjectScale / Mathf.Max(1e-4f, t.lossyScale.x);
        }

        void Refresh()
        {
            if (_brain == null) _brain = GetComponent<ZombieBrain>();
            if (_golem == null) _golem = GetComponent<Golem>();
            if (_eyes == null) _eyes = _brain != null ? _brain.Eyes : _golem != null ? _golem.Eyes : GetComponentInChildren<GooglyEyes>();
            bool on = Active;
            bool fearless = on && Kind == MischiefKind.Aggressive;
            if (_brain != null) _brain.BuffFearless = fearless;
            if (_golem != null) _golem.BuffFearless = fearless;
            if (_eyes != null)
                _eyes.SetPupilTint(!on ? (Color?)null
                    : Kind == MischiefKind.Aggressive ? DrawingConfig.AggressiveEyeColor
                    : DrawingConfig.SpreadingEyeColor);
        }

        void Update()
        {
            if (Active) return;
            // ran out by itself (End zeroes it): the rally leaving, its own voice small and high
            if (Until > 0f)
                Juice.Sound(MischiefLaw.SoundOf(Kind),
                    transform.position + Vector3.up * (_golem != null ? _golem.BodyScale : transform.localScale.y), 0.35f, 1.3f);
            Destroy(this);
        }

        /// Over now: the nerve and the pupils as they were (a split's pieces copy the body, not the rune).
        public void End()
        {
            Until = 0f;
            if (_brain != null) _brain.BuffFearless = false;
            if (_golem != null) _golem.BuffFearless = false;
            if (_eyes != null) _eyes.SetPupilTint(null);
        }

        void OnDestroy() => End();
    }

    /// The transformation ink on a zombie or a prop: it wears another object's
    /// look for a while - a look-only clone centred where its body is, every
    /// other renderer hidden - on the host and on every client alike.
    public class WornLook : MonoBehaviour
    {
        float _until;
        GameObject _clone;
        readonly List<Renderer> _hidden = new List<Renderer>();

        /// The borrowed look while it is worn: not the object's own meshes.
        public Transform Look => _clone != null ? _clone.transform : null;

        public static void Put(GameObject target, string shapeKey, float seconds)
        {
            if (target == null) return;
            var src = ShapeShift.ResolveShape(shapeKey, out _);
            if (src == null) return;
            var w = target.GetComponent<WornLook>();
            if (w == null) w = target.AddComponent<WornLook>();
            else
            {
                seconds = Mathf.Max(seconds, w._src != null ? w._seconds : w._until - Time.time); // a second dart only lengthens it
                if (w._src == null && w._clone != null && w._from == src) { w._until = Time.time + seconds; return; } // the same look, longer
            }
            // dressed on its own next turn: a dart lands inside a physics step, where Unity refuses
            // to strip the copy, and the "look" kept its colliders and scripts
            w._src = src;
            w._seconds = seconds;
            w._until = Time.time + seconds + 1f;
        }

        Transform _src, _from;
        float _seconds;

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
            _from = src;
            // what an acolyte's disguise sounds like going on, bigger is lower (every machine dresses its own)
            Juice.Sound(Sfx.AcolyteTransform, mine.center, 1f, SizePitch(mine), false);
        }

        static float SizePitch(Bounds b) => Mathf.Lerp(1.15f, 0.85f, Mathf.InverseLerp(0.5f, 3f, b.size.y));

        void Undress()
        {
            foreach (var r in _hidden) if (r != null) r.enabled = true;
            _hidden.Clear();
            if (_clone != null) Destroy(_clone);
            _clone = null;
        }

        void Update()
        {
            if (_src != null)
            {
                var src = _src;
                _src = null;
                Dress(src, _seconds);
                return;
            }
            if (Time.time >= _until)
            {
                // its own look back: an acolyte's disguise coming off
                var own = ShapeShift.FindObjectBounds(transform);
                Juice.Sound(Sfx.AcolyteBack, own.center, 1f, SizePitch(own), false);
                Undress();
                Destroy(this);
            }
        }

        void OnDestroy() => Undress();
    }
}
