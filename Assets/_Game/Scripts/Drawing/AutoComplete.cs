using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The book finishes drawings, from the wand's ink. Rune page, aim at your
    /// ink, F: your drawing goes and the rune is drawn whole in its place, at
    /// its size, animated - plain ink the recognizer reads on its own. Seal
    /// page, aim at a rune, F: the ring draws itself around the runes it finds
    /// nearby. Any pen work during an animation aborts it and returns the ink.
    public static class AutoComplete
    {
        const float NodeStep = 0.025f;     // metres between drawn nodes
        const float MinPiece = 0.015f;     // shorter than this is not ink, it is a point
        const float SamePoint = 0.002f;    // two nodes this close are one node
        const float FadeSeconds = 0.15f;   // the lines that go, going

        static bool _running;
        static readonly List<(Stroke s, RuneType rune, float at)> _recent = new List<(Stroke, RuneType, float)>();

        public static bool Running => _running;

        /// Every loop that walks ink counts its turns; past the cap it logs
        /// once and stops, so a mistake here can never freeze the game.
        static bool Guard(ref int n, string where)
        {
            if (++n < 200000) return true;
            if (n == 200000) Debug.LogError($"[AutoComplete] loop guard tripped in {where}");
            return false;
        }

        /// Habits are saved a moment after they change; a completion rubbed
        /// out right away counts against its rune.
        public static void Tick(DrawingWorld w)
        {
            RuneHabits.Tick();
            for (int i = _recent.Count - 1; i >= 0; i--)
            {
                var r = _recent[i];
                if (Time.time - r.at > 5f) { _recent.RemoveAt(i); continue; }
                if (r.s == null || !r.s.Alive) { RuneHabits.NoteMiss(r.rune); _recent.RemoveAt(i); }
            }
        }

        // -------------------------------------------------------------- ink --
        /// The wand pays for what the game draws, as it is drawn, at the
        /// completion premium; what the game erases comes back at the pen's rate.
        class Purse
        {
            readonly PlayerInk _ink;
            public Purse(PlayerInk ink) { _ink = ink; }
            float Cost(float metres) => metres * DrawingConfig.InkCostPerMeter * DrawingConfig.AutoCompleteInkMul
                * (_ink != null ? _ink.DrawRate : 1f);
            public bool CanAfford(float metres) => _ink == null || PlayerInk.Bottomless || _ink.Ink >= Cost(metres);
            public bool Pay(float metres) => _ink == null || metres <= 0f || _ink.TrySpend(Cost(metres));
            public void Refund(float metres) { if (_ink != null && metres > 0f) _ink.Award(Cost(metres)); }
            public void Return(float metres) { if (_ink != null && metres > 0f) _ink.Award(metres * DrawingConfig.InkCostPerMeter * _ink.DrawRate); }
        }

        static PlayerInk LocalInk()
        {
            foreach (var p in SimpleFPSController.All)
                if (p != null && p.IsLocalViewer) return p.GetComponent<PlayerInk>();
            return null;
        }

        // -------------------------------------------------------- rune page --
        /// The rune page: your drawing goes, the named rune is drawn whole where
        /// it was, at its size. True when it starts.
        public static bool Manual(List<Stroke> members, RuneType rune)
        {
            var w = DrawingWorld.Instance;
            if (w == null || _running || members == null || members.Count == 0 || rune == RuneType.None) return false;
            var raw = RuneGlyph.RawStrokesOf(members);
            if (raw.Count == 0) return false;
            var tmpl = RuneLibrary.SamplePath(rune);
            if (tmpl == null) return false;
            if (!RuneGlyph.Frame(members, out var origin, out var right, out var up, out var normal)) return false;
            var surface = members[0].Surface;

            var lines = new List<List<Vector3>>();
            float total = 0f;
            foreach (var f in CenterFit(raw, tmpl))
            {
                var path = new List<Vector3>();
                float len = To3D(f, origin, right, up, normal, surface, path);
                if (len >= MinPiece) { lines.Add(path); total += len; }
            }
            if (lines.Count == 0) return false;

            var purse = new Purse(LocalInk());
            if (!purse.CanAfford(total)) { w.LogEvent(Loc.T("rune.noink")); return false; }
            Debug.Log($"[AutoComplete] page: {rune}, {lines.Count} lines ({total * 100f:0} cm), {members.Count} of yours replaced");

            _running = true;
            w.StartCoroutine(Rebuild(w, members[0], new List<Stroke>(members), lines, rune, surface, normal, purse));
            return true;
        }

        /// Frame points onto the surface as a 3D path, skipping repeats; returns its length.
        static float To3D(List<Vector2> pts, Vector3 origin, Vector3 right, Vector3 up, Vector3 normal, Transform surface,
            List<Vector3> into)
        {
            float len = 0f;
            foreach (var q in pts)
            {
                Vector3 p = OnSurface(origin + right * q.x + up * q.y, normal, surface);
                if (into.Count > 0 && Vector3.Distance(into[into.Count - 1], p) < SamePoint) continue;
                if (into.Count > 0) len += Vector3.Distance(into[into.Count - 1], p);
                into.Add(p);
            }
            return len;
        }

        /// The clean glyph at the drawing's own size and centre.
        static List<List<Vector2>> CenterFit(List<List<Vector2>> raw, List<List<Vector2>> tmpl)
        {
            Bounds2(raw, out var rmin, out var rmax);
            Bounds2(tmpl, out var tmin, out var tmax);
            float rsize = Mathf.Max(0.12f, Mathf.Max(rmax.x - rmin.x, rmax.y - rmin.y));
            float tsize = Mathf.Max(1e-4f, Mathf.Max(tmax.x - tmin.x, tmax.y - tmin.y));
            float k = rsize / tsize;
            Vector2 shift = (rmin + rmax) * 0.5f - (tmin + tmax) * 0.5f * k;
            var fitted = new List<List<Vector2>>(tmpl.Count);
            foreach (var stroke in tmpl)
            {
                var f2 = new List<Vector2>(stroke.Count);
                foreach (var q in stroke) f2.Add(q * k + shift);
                fitted.Add(f2);
            }
            return fitted;
        }

        static void Bounds2(List<List<Vector2>> paths, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in paths)
                foreach (var q in p) { min = Vector2.Min(min, q); max = Vector2.Max(max, q); }
        }

        // -------------------------------------------------------- seal page --
        /// The seal page aimed at a rune: the ring draws itself around the runes
        /// the seal will read in this group. Anything nearby that is not a rune
        /// is left alone and left outside.
        public static bool SealGroup(List<Stroke> group)
        {
            var w = DrawingWorld.Instance;
            if (w == null || _running || group == null || group.Count == 0) return false;
            var runes = new List<Stroke>();
            foreach (var g in RuneGlyph.Segment(group, Grimoire.LocalPlayerId))
                if (g.Rune != RuneType.None && g.Score >= DrawingConfig.MinRuneScore) runes.AddRange(g.Members);
            if (runes.Count == 0) { w.LogEvent(Loc.T("seal.norune")); return false; }
            if (!RuneGlyph.Frame(runes, out var origin, out var right, out var up, out var normal)) return false;
            var surface = runes[0].Surface;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var s in runes)
                foreach (var n in s.Nodes)
                {
                    if (n == null) continue;
                    Vector3 d = n.transform.position - origin;
                    float x = Vector3.Dot(d, right), y = Vector3.Dot(d, up);
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            if (minX > maxX) return false;
            float m = DrawingConfig.AutoSealMargin;
            Vector2 c = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            // an ellipse hugs the runes; the sqrt(2) keeps the box corners inside it
            float rx = (maxX - minX) * 0.5f * 1.42f + m, ry = (maxY - minY) * 0.5f * 1.42f + m;
            const int N = 48;
            var ring = new List<Vector3>(N + 1);
            for (int i = 0; i <= N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                Vector2 q = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                ring.Add(OnSurface(origin + right * q.x + up * q.y, normal, surface));
            }
            var purse = new Purse(LocalInk());
            if (!purse.CanAfford(Length(ring))) { w.LogEvent(Loc.T("seal.noink")); return false; }
            _running = true;
            w.StartCoroutine(DrawRing(w, ring, surface, normal, purse));
            return true;
        }

        /// Does the aimed drawing read as a rune (declared, or readable)?
        public static bool LooksLikeRunes(List<Stroke> members)
        {
            if (members == null || members.Count == 0 || LooksLikeLoop(members)) return false;
            bool allDeclared = true;
            foreach (var m in members)
            {
                if (m == null || !m.Alive) return false;
                if (m.DeclaredRune == RuneType.None) allDeclared = false;
            }
            if (allDeclared) return true;
            var (rune, score) = RuneGlyph.ReadVerdict(members, Grimoire.LocalPlayerId);
            return rune != RuneType.None && score >= DrawingConfig.MinRuneScore;
        }

        /// A stroke that comes back to its start is a boundary, not a rune.
        static bool LooksLikeLoop(List<Stroke> members)
        {
            foreach (var m in members)
                if (m != null && m.Alive && m.First != null && m.Last != null && m.Nodes.Count > 8
                    && Vector3.Distance(m.First.transform.position, m.Last.transform.position) < 0.06f) return true;
            return false;
        }

        /// Grows `group` with every open stroke of yours on the same surface
        /// within the gather distance, one hop at a time: the closest runes.
        public static void GatherGroup(List<Stroke> group, IReadOnlyList<Stroke> all)
        {
            if (group == null || group.Count == 0) return;
            var surface = group[0].Surface;
            int me = Grimoire.LocalPlayerId;
            float reach = DrawingConfig.AutoSealGather;
            bool grew = true;
            int gg = 0;
            while (grew && Guard(ref gg, "gather"))
            {
                grew = false;
                for (int i = 0; i < all.Count; i++)
                {
                    var s = all[i];
                    if (s == null || !s.Alive || s.State != StrokeState.Open || s.OwnerId != me) continue;
                    if (s.SealResidue || s.Hidden() || s.Surface != surface || group.Contains(s)) continue;
                    var sb = RuneGlyph.StrokeBounds(s);
                    sb.Expand(reach * 2f);
                    foreach (var g in group)
                        if (sb.Intersects(RuneGlyph.StrokeBounds(g))) { group.Add(s); grew = true; break; }
                }
            }
        }

        // ------------------------------------------------------- animation --
        static Stroke NewAutoStroke(DrawingWorld w, Stroke lead, Transform surface, float autoLen)
        {
            var s = new Stroke
            {
                BasisRight = lead != null ? lead.BasisRight : Vector3.right,
                BasisUp = lead != null ? lead.BasisUp : Vector3.up,
                Surface = surface,
                OwnerId = Grimoire.LocalPlayerId,
                AutoDrawn = autoLen > 0f,
                AutoLength = autoLen
            };
            w.Register(s);
            return s;
        }

        static void Put(Stroke s, Vector3 p, Vector3 normal, Transform surface)
        {
            var last = s.Last;
            if (last != null && Vector3.Distance(last.transform.position, p) < SamePoint) return;
            s.AddNode(DrawNode.Create(s, s.Nodes.Count, p, normal, surface));
        }

        /// Adds nodes along `path` until `upTo` metres of it exist; returns the new length.
        static float Grow(Stroke s, Vector3 normal, Transform surface, List<Vector3> path, float have, float upTo)
        {
            if (path.Count == 0) return have;
            if (have <= 0f && s.Nodes.Count == 0) Put(s, path[0], normal, surface);
            float acc = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                float seg = Vector3.Distance(path[i - 1], path[i]);
                float segStart = acc;
                acc += seg;
                if (acc <= have) continue;
                float from = Mathf.Max(have, segStart);
                for (float d = from + NodeStep; d < Mathf.Min(acc, upTo); d += NodeStep)
                    Put(s, Vector3.Lerp(path[i - 1], path[i], (d - segStart) / Mathf.Max(1e-4f, seg)), normal, surface);
                if (acc <= upTo) Put(s, path[i], normal, surface);
                if (acc >= upTo) return upTo;
            }
            return acc;
        }

        static float Length(List<Vector3> path)
        {
            float l = 0f;
            for (int i = 1; i < path.Count; i++) l += Vector3.Distance(path[i - 1], path[i]);
            return l;
        }

        /// Your drawing fades and burns, its ink back in the wand; then the
        /// rune grows line by line, paid as it grows. Plain ink at the end:
        /// the recognizer reads it like anything else you drew.
        static IEnumerator Rebuild(DrawingWorld w, Stroke lead, List<Stroke> burn, List<List<Vector3>> lines, RuneType rune,
            Transform surface, Vector3 normal, Purse purse)
        {
            float seconds = DrawingConfig.AutoCompleteSeconds;
            float total = 0f;
            foreach (var l in lines) total += Length(l);
            var made = new List<Stroke>();
            bool aborted = false;

            // ---- 1. your drawing goes ----
            Vector3 fadeAt = burn.Count > 0 && burn[0] != null ? burn[0].Centroid() : Vector3.zero;
            NetSync.PushInkFx(NetSync.InkFxFade, fadeAt, FadeSeconds, burn); // the copies thin out too
            float t = 0f;
            while (t < FadeSeconds)
            {
                t += Time.deltaTime;
                if (SurfaceDrawer.IsPenActive) { aborted = true; break; }
                float k = Mathf.Lerp(1f, 0.15f, Mathf.Clamp01(t / FadeSeconds));
                foreach (var s in burn) if (s != null && s.Alive) s.SetEvaporation(k);
                yield return null;
            }
            if (aborted)
            {
                foreach (var s in burn) if (s != null && s.Alive) s.SetEvaporation(1f);
                NetSync.PushInkFx(NetSync.InkFxRestore, fadeAt, 0f, burn);
                _running = false;
                yield break;
            }
            float gone = 0f;
            foreach (var s in burn) if (s != null && s.Alive) { gone += s.PathLength(); s.Burn(); }
            NetSync.OnLocalInkBurned(burn); // its copies die everywhere too
            purse.Return(gone);

            // ---- 2. the rune, line by line ----
            float drawTime = Mathf.Max(0.05f, seconds - FadeSeconds);
            float drawn = 0f, have = 0f;
            int li = 0;
            Stroke cur = null;
            bool dry = false;
            t = 0f;
            while (li < lines.Count && !dry)
            {
                t += Time.deltaTime;
                if (SurfaceDrawer.IsPenActive) { aborted = true; break; }
                float want = total * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / drawTime));
                int gd = 0;
                while (li < lines.Count && Guard(ref gd, "draw"))
                {
                    var path = lines[li];
                    float len = Length(path);
                    if (cur == null)
                    {
                        cur = NewAutoStroke(w, lead, surface, len);
                        made.Add(cur);
                        have = 0f;
                    }
                    if (drawn >= want && have < len - 1e-4f) break;
                    float upTo = Mathf.Min(len, have + Mathf.Max(0f, want - drawn));
                    if (!purse.Pay(upTo - have)) { dry = true; break; } // the wand ran dry: the line stops here
                    float now = Grow(cur, normal, surface, path, have, upTo);
                    drawn += now - have;
                    have = now;
                    if (have >= len - 1e-4f) { li++; cur = null; }
                    else break;
                }
                if (li < lines.Count && !dry) yield return null;
            }
            if (dry) w.LogEvent(Loc.T("rune.noink"));

            if (aborted)
            {
                foreach (var s in made) if (s != null && s.Alive) s.Burn();
                purse.Refund(drawn);
                _running = false;
                yield break;
            }

            // ---- 3. plain ink, read on its own ----
            Vector3 at = Vector3.zero;
            int cnt = 0;
            foreach (var s in made)
            {
                s.CompletedAt = Time.time;
                if (s.Nodes.Count < 3 || s.PathLength() < MinPiece) { s.Burn(); continue; } // a point is not ink
                w.CompleteStroke(s, allowCloseOntoInk: false); // rune ink is never a boundary
                if (s.Alive) { at += s.Centroid(); cnt++; _recent.Add((s, rune, Time.time)); }
            }
            if (cnt > 0)
            {
                Juice.Chime(at / cnt);
                NetSync.PushInkFx(NetSync.InkFxChime, at / cnt);
            }
            _running = false;
        }

        /// The seal ring, drawn in as one stroke and paid as it grows; the
        /// closure detector seals it.
        static IEnumerator DrawRing(DrawingWorld w, List<Vector3> ring, Transform surface, Vector3 normal, Purse purse)
        {
            float seconds = DrawingConfig.AutoSealSeconds;
            float total = Length(ring);
            var s = NewAutoStroke(w, null, surface, total);
            float have = 0f, t = 0f;
            bool aborted = false, dry = false;
            while (t < seconds && have < total - 1e-4f)
            {
                t += Time.deltaTime;
                if (SurfaceDrawer.IsPenActive) { aborted = true; break; }
                float want = Mathf.Min(total, total * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds)));
                if (want > have)
                {
                    if (!purse.Pay(want - have)) { dry = true; break; }
                    have = Grow(s, normal, surface, ring, have, want);
                }
                yield return null;
            }
            if (aborted)
            {
                if (s.Alive) s.Burn();
                purse.Refund(have);
                _running = false;
                yield break;
            }
            if (!dry && purse.Pay(total - have)) have = Grow(s, normal, surface, ring, have, total);
            else if (dry) w.LogEvent(Loc.T("seal.noink"));
            if (s.Nodes.Count < 3 || s.PathLength() < MinPiece) s.Burn();
            else
            {
                w.CompleteStroke(s); // self-closure seals it (the host closes world loops)
                if (s.Alive)
                {
                    Juice.Chime(s.Centroid());
                    NetSync.PushInkFx(NetSync.InkFxChime, s.Centroid());
                }
            }
            _running = false;
        }

        // ------------------------------------------------------------ world --
        /// A frame point pressed onto the surface it was drawn on; the plane
        /// point when the ray finds nothing of it.
        static Vector3 OnSurface(Vector3 p, Vector3 normal, Transform surface)
        {
            if (surface == null) return p;
            if (Physics.Raycast(p + normal * 0.2f, -normal, out var hit, 0.5f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && (hit.collider.transform == surface || hit.collider.transform.IsChildOf(surface)))
                return hit.point;
            return p;
        }
    }
}
