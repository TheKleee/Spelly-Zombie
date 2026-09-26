using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The book finishes drawings, from the wand's ink. Rune page, aim at your
    /// ink, F: your drawing goes and the rune is drawn whole in its place, at
    /// its size, animated - plain ink the recognizer reads on its own. Aim at a
    /// rune, F: the seal draws itself around the runes it finds nearby, a ring,
    /// or a triangle on a straight line of yours. It draws as far as the wand
    /// pays and stops short there. Any pen work during an animation aborts it
    /// and returns the ink. The paths are computed apart from the drawing: the
    /// floating F shows them first (GhostHand).
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
            /// How many metres the book can draw right now, `returned` metres of your own ink
            /// coming back to the wand first (what F burns before it draws).
            public float Metres(float returned)
            {
                if (_ink == null || PlayerInk.Bottomless) return float.MaxValue;
                float rate = DrawingConfig.InkCostPerMeter * _ink.DrawRate;
                return (_ink.Ink + returned * rate) / Mathf.Max(1e-4f, rate * DrawingConfig.AutoCompleteInkMul);
            }
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

        static void NoInk(DrawingWorld w, string message)
        {
            w.LogEvent(message);
            Juice.Sound2D(Sfx.UiError);
        }

        /// How many metres of the book's drawing the local wand pays for right now, `returned`
        /// metres of your own ink coming back to it first. The ghost hand stops short there.
        public static float AffordableMetres(float returned = 0f) => new Purse(LocalInk()).Metres(returned);

        // -------------------------------------------------------- rune page --
        /// The rune page: your drawing goes, the named rune is drawn whole where
        /// it was, at its size. True when it starts.
        public static bool Manual(List<Stroke> members, RuneType rune)
        {
            var w = DrawingWorld.Instance;
            if (w == null || _running) return false;
            if (!RuneLines(members, rune, out var lines, out var surface, out var normal)) return false;
            float total = 0f;
            foreach (var l in lines) total += Length(l);
            // no ink check up front: it draws as far as the wand goes and stops short there
            Debug.Log($"[AutoComplete] page: {rune}, {lines.Count} lines ({total * 100f:0} cm), {members.Count} of yours replaced");

            _running = true;
            w.StartCoroutine(Rebuild(w, members[0], new List<Stroke>(members), lines, rune, surface, normal,
                new Purse(LocalInk())));
            return true;
        }

        /// The rune's lines as F would draw them: the clean glyph at the drawing's own size and
        /// centre, on its surface. What the floating F shows first.
        public static bool RuneLines(List<Stroke> members, RuneType rune, out List<List<Vector3>> lines,
            out Transform surface, out Vector3 normal)
        {
            lines = null; surface = null; normal = Vector3.up;
            if (members == null || members.Count == 0 || rune == RuneType.None) return false;
            var raw = RuneGlyph.RawStrokesOf(members);
            if (raw.Count == 0) return false;
            var tmpl = RuneLibrary.SamplePath(rune);
            if (tmpl == null) return false;
            if (!RuneGlyph.Frame(members, out var origin, out var right, out var up, out normal)) return false;
            surface = members[0].Surface;
            lines = new List<List<Vector3>>();
            foreach (var f in CenterFit(raw, tmpl))
            {
                var path = new List<Vector3>();
                float len = To3D(f, origin, right, up, normal, surface, path);
                if (len >= MinPiece) lines.Add(path);
            }
            return lines.Count > 0;
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

        // -------------------------------------------------------- the seal --
        /// F on a rune: the seal draws itself around the runes it finds in this group, and it
        /// closes only what your own lines make (fewer lines, stronger spell: Spell.PowerFor).
        /// Straight lines of yours beside the runes that meet end to end are one polyline; when
        /// ONE straight line closes it around the runes, that is the seal (a V becomes the
        /// triangle, a U a box). A single line is the base of a seven-cornered arch over the
        /// runes. Nothing drawn: the ring. Your lines go and come back as the seal's sides.
        /// Anything else nearby that is not a rune is left alone and left outside.
        public static bool SealGroup(List<Stroke> group)
        {
            var w = DrawingWorld.Instance;
            if (w == null || _running || group == null || group.Count == 0) return false;
            var runes = RunesIn(group);
            if (runes.Count == 0) { w.LogEvent(Loc.T("seal.norune")); return false; }
            if (!SealPath(group, runes, out var path, out var surface, out var normal, out var sides)) return false;
            // no ink check up front: it draws as far as the wand goes and stops short there
            _running = true;
            w.StartCoroutine(DrawSeal(w, path, surface, normal, sides, new Purse(LocalInk())));
            return true;
        }

        /// The seal F would draw around the runes in `group`, and the lines of yours that become
        /// its sides (empty for a ring), silently: what the floating F shows first. False when
        /// no rune is there.
        public static bool SealPath(List<Stroke> group, out List<Vector3> path, out Vector3 normal, out List<Stroke> sides)
        {
            path = null; normal = Vector3.up; sides = null;
            if (group == null || group.Count == 0) return false;
            var runes = RunesIn(group);
            return runes.Count > 0 && SealPath(group, runes, out path, out _, out normal, out sides);
        }

        /// The members of `group` that read as runes, declared or recognised.
        static List<Stroke> RunesIn(List<Stroke> group)
        {
            var runes = new List<Stroke>();
            foreach (var g in RuneGlyph.Segment(group, Grimoire.LocalPlayerId))
                if (g.Rune != RuneType.None && g.Score >= DrawingConfig.MinRuneScore) runes.AddRange(g.Members);
            return runes;
        }

        /// One of your straight lines beside the runes, in the runes' own frame.
        struct Side { public Stroke S; public Vector2 A, B; public float Len; }

        const float JoinGap = 0.15f;    // two line ends this close meet at one corner
        const int ArchSegments = 6;     // the arch over one line: seven corners in all, fewer than a ring's eight
        const float ArmStretch = 1.5f;  // how much longer the book may make the arms of a V to close it

        static bool SealPath(List<Stroke> group, List<Stroke> runes, out List<Vector3> path,
            out Transform surface, out Vector3 normal, out List<Stroke> sides)
        {
            path = null; sides = null;
            surface = runes[0].Surface;
            if (!RuneGlyph.Frame(runes, out var origin, out var right, out var up, out normal)) return false;
            Vector2 Flat(Vector3 p) { Vector3 d = p - origin; return new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up)); }
            // the runes' points and box in their own frame
            var pts = new List<Vector2>();
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var s in runes)
                foreach (var n in s.Nodes)
                {
                    if (n == null) continue;
                    var q = Flat(n.transform.position);
                    pts.Add(q);
                    minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x);
                    minY = Mathf.Min(minY, q.y); maxY = Mathf.Max(maxY, q.y);
                }
            if (minX > maxX) return false;
            Transform srf = surface;
            Vector3 nrm = normal;
            System.Func<Vector3, Vector3> onto = p => OnSurface(p, nrm, srf);
            System.Func<Vector2, Vector3> lift = q => onto(origin + right * q.x + up * q.y);

            var lines = StraightLinesIn(group, runes, surface, Flat);
            if (lines.Count == 0)
            {
                path = Ellipse(origin, right, up, minX, maxX, minY, maxY, onto);
                return true;
            }
            // the longest polyline your lines make, closed with one line when that holds the runes
            var chain = Chain(lines, out var verts);
            var poly = chain.Count >= 2 ? CloseChain(verts, pts) : null;
            if (poly == null)
            {
                // one line, or lines one line cannot close around the runes: the longest is the base of an arch
                poly = Arch(lines[0].A, lines[0].B, pts);
                chain = new List<Stroke> { lines[0].S };
            }
            path = Polygon(poly, lift);
            sides = chain;
            return true;
        }

        /// Your straight lines beside the runes that are not runes themselves, on their surface,
        /// longest first. A line: every node close to the chord, the path barely longer than it.
        static List<Side> StraightLinesIn(List<Stroke> group, List<Stroke> runes, Transform surface,
            System.Func<Vector3, Vector2> flat)
        {
            var lines = new List<Side>();
            foreach (var s in group)
            {
                if (s == null || !s.Alive || s.Persistent || s.Surface != surface) continue;
                if (s.DeclaredRune != RuneType.None || runes.Contains(s)) continue;
                if (s.First == null || s.Last == null) continue;
                Vector3 p0 = s.First.transform.position, p1 = s.Last.transform.position;
                float chord = Vector3.Distance(p0, p1);
                if (chord < 0.12f) continue; // shorter is a mark, not a side
                if (s.PathLength() > chord * 1.12f) continue;
                Vector3 dir = (p1 - p0) / chord;
                bool straight = true;
                foreach (var n in s.Nodes)
                    if (n != null && Vector3.ProjectOnPlane(n.transform.position - p0, dir).magnitude > chord * 0.06f)
                    { straight = false; break; }
                if (!straight) continue;
                lines.Add(new Side { S = s, A = flat(p0), B = flat(p1), Len = chord });
            }
            lines.Sort((x, y) => y.Len.CompareTo(x.Len));
            return lines;
        }

        /// Your lines whose ends meet (JoinGap) make one polyline, corner to corner: the longest
        /// such, in order, from one free end to the other, its corners cleaned up. Lines that
        /// meet all the way round are opened at their widest gap: that gap is what F closes.
        static List<Stroke> Chain(List<Side> lines, out List<Vector2> verts)
        {
            int n = lines.Count;
            Vector2 End(int e) => (e & 1) == 0 ? lines[e >> 1].A : lines[e >> 1].B;
            // each end meets its nearest other line's end within the gap, when that end agrees
            var link = new int[n * 2];
            for (int e = 0; e < n * 2; e++)
            {
                int best = -1;
                float bestGap = JoinGap * JoinGap;
                for (int f = 0; f < n * 2; f++)
                {
                    if ((f >> 1) == (e >> 1)) continue;
                    float g = (End(e) - End(f)).sqrMagnitude;
                    if (g < bestGap) { bestGap = g; best = f; }
                }
                link[e] = best;
            }
            for (int e = 0; e < n * 2; e++)
                if (link[e] >= 0 && link[link[e]] != e) link[e] = -1;

            // a ring of lines has no free end: open it at its widest gap
            var comp = new int[n];
            for (int i = 0; i < n; i++) comp[i] = -1;
            var stack = new List<int>();
            for (int i = 0, c = 0; i < n; i++)
            {
                if (comp[i] >= 0) continue;
                stack.Clear(); stack.Add(i); comp[i] = c;
                bool free = false;
                int wideE = -1; float wide = -1f;
                while (stack.Count > 0)
                {
                    int k = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1);
                    for (int side = 0; side < 2; side++)
                    {
                        int e = k * 2 + side, f = link[e];
                        if (f < 0) { free = true; continue; }
                        float g = (End(e) - End(f)).sqrMagnitude;
                        if (g > wide) { wide = g; wideE = e; }
                        if (comp[f >> 1] < 0) { comp[f >> 1] = c; stack.Add(f >> 1); }
                    }
                }
                if (!free && wideE >= 0) { link[link[wideE]] = -1; link[wideE] = -1; }
                c++;
            }

            // walk from every free end; the longest polyline wins
            List<Stroke> bestChain = null; List<Vector2> bestVerts = null; float bestLen = 0f;
            var used = new bool[n];
            for (int start = 0; start < n * 2; start++)
            {
                if (link[start] >= 0 || used[start >> 1]) continue;
                var chain = new List<Stroke>(); var vs = new List<Vector2>(); float len = 0f;
                int e = start;
                vs.Add(End(e));
                int guard = 0;
                while (Guard(ref guard, "chain"))
                {
                    int i = e >> 1;
                    used[i] = true; chain.Add(lines[i].S); len += lines[i].Len;
                    int exit = e ^ 1, next = link[exit];
                    if (next < 0 || used[next >> 1]) { vs.Add(End(exit)); break; }
                    vs.Add(Corner(lines[i], lines[next >> 1], End(exit), End(next)));
                    e = next;
                }
                if (len > bestLen) { bestLen = len; bestChain = chain; bestVerts = vs; }
            }
            verts = bestVerts ?? new List<Vector2>();
            return bestChain ?? new List<Stroke>();
        }

        /// Where two lines meet: their crossing when it lies near both ends, else halfway between the ends.
        static Vector2 Corner(Side p, Side q, Vector2 endP, Vector2 endQ)
        {
            Vector2 d1 = p.B - p.A, d2 = q.B - q.A;
            float den = Cross(d1, d2);
            if (Mathf.Abs(den) > 1e-6f)
            {
                float t = Cross(q.A - p.A, d2) / den;
                Vector2 x = p.A + d1 * t;
                if ((x - endP).magnitude <= JoinGap * 1.5f && (x - endQ).magnitude <= JoinGap * 1.5f) return x;
            }
            return (endP + endQ) * 0.5f;
        }

        /// The polygon your polyline makes with ONE straight line closing it, when that holds
        /// every rune with the margin. The arms of a V may grow up to ArmStretch for the closing
        /// line to clear the runes. Null when one line cannot close it around them.
        static List<Vector2> CloseChain(List<Vector2> verts, List<Vector2> pts)
        {
            float m = DrawingConfig.AutoSealMargin;
            var poly = new List<Vector2>(verts);
            if (poly.Count == 3)
            {
                Vector2 o = poly[1], p1 = poly[0], p2 = poly[2];
                Vector2 n = Perp((p2 - p1).normalized);
                float dO = Vector2.Dot(o - p1, n);
                if (Mathf.Abs(dO) < 0.02f) return null; // no corner to speak of
                if (dO > 0f) { n = -n; dO = -dO; }      // n points away from the corner, the corner sits -dO behind the line
                float depth = -dO;
                float s = 1f;
                foreach (var q in pts) s = Mathf.Max(s, 1f + (Vector2.Dot(q - p1, n) + m) / depth);
                if (s > ArmStretch) return null;
                poly[0] = o + (p1 - o) * s;
                poly[2] = o + (p2 - o) * s;
            }
            int last = poly.Count - 1;
            foreach (var q in pts)
                if (!Inside(poly, q) || DistToSegment(q, poly[last], poly[0]) < m * 0.5f) return null;
            // the closing line must not cut across the polyline
            for (int i = 1; i < last - 1; i++)
                if (Intersect(poly[last], poly[0], poly[i], poly[i + 1])) return null;
            return poly;
        }

        /// One line: the base of an arch of straight segments over the runes, seven corners in
        /// all. The base keeps your line's place unless the runes crowd it, then it backs off to
        /// the margin. Drawn from the end at a, along the line first.
        static List<Vector2> Arch(Vector2 a, Vector2 b, List<Vector2> pts)
        {
            float m = DrawingConfig.AutoSealMargin;
            Vector2 dir = (b - a).normalized;
            Vector2 across = Perp(dir);
            float mean = 0f; // the runes lie on this side of the line
            foreach (var q in pts) mean += Vector2.Dot(q - a, across);
            if (mean < 0f) across = -across;
            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
            foreach (var q in pts)
            {
                float u = Vector2.Dot(q - a, dir), v = Vector2.Dot(q - a, across);
                u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u);
                v0 = Mathf.Min(v0, v); v1 = Mathf.Max(v1, v);
            }
            float vb = Mathf.Min(0f, v0 - m);
            float halfW = (u1 - u0) * 0.5f + m, h = (v1 + m) - vb; // the box the arch must hold
            float cu = (u0 + u1) * 0.5f;
            // a half ellipse through the box's top corners, grown so the straight sides between the
            // arch's corners still pass outside them
            float grow = 1.42f / Mathf.Cos(Mathf.PI / (2f * ArchSegments));
            float rx = halfW * grow, ry = h * grow;
            var poly = new List<Vector2>
            {
                a + dir * (cu - rx) + across * vb,
                a + dir * (cu + rx) + across * vb,
            };
            for (int i = 1; i < ArchSegments; i++)
            {
                float ang = Mathf.PI * i / ArchSegments; // from the base's end back over to its start
                poly.Add(a + dir * (cu + Mathf.Cos(ang) * rx) + across * (vb + Mathf.Sin(ang) * ry));
            }
            return poly;
        }

        /// The ring: an ellipse hugging the box, the corners inside it, the margin the book
        /// keeps. It starts at the bottom, the way a hand would.
        public static List<Vector3> Ellipse(Vector3 origin, Vector3 right, Vector3 up,
            float minX, float maxX, float minY, float maxY, System.Func<Vector3, Vector3> onto)
        {
            float m = DrawingConfig.AutoSealMargin;
            Vector2 c = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            // the sqrt(2) keeps the box corners inside it
            float rx = (maxX - minX) * 0.5f * 1.42f + m, ry = (maxY - minY) * 0.5f * 1.42f + m;
            const int N = 48;
            var ring = new List<Vector3>(N + 1);
            for (int i = 0; i <= N; i++)
            {
                float ang = -Mathf.PI * 0.5f + i * Mathf.PI * 2f / N;
                Vector2 q = c + new Vector2(Mathf.Cos(ang) * rx, Mathf.Sin(ang) * ry);
                ring.Add(onto(origin + right * q.x + up * q.y));
            }
            return ring;
        }

        /// The polygon as the hand draws it: every side in steps pressed onto the surface, closed
        /// on its first point.
        static List<Vector3> Polygon(List<Vector2> poly, System.Func<Vector2, Vector3> lift)
        {
            var path = new List<Vector3>();
            for (int i = 0; i < poly.Count; i++) Walk(path, poly[i], poly[(i + 1) % poly.Count], lift);
            path.Add(lift(poly[0]));
            return path;
        }

        /// The points of a straight side, a few centimetres apart so it follows the surface.
        static void Walk(List<Vector3> into, Vector2 from, Vector2 to, System.Func<Vector2, Vector3> lift)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(from, to) / 0.04f));
            for (int i = 0; i < n; i++) into.Add(lift(Vector2.Lerp(from, to, (float)i / n)));
        }

        static Vector2 Perp(Vector2 d) => new Vector2(-d.y, d.x);
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool Inside(List<Vector2> poly, Vector2 q)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 pi = poly[i], pj = poly[j];
                if ((pi.y > q.y) != (pj.y > q.y) && q.x < (pj.x - pi.x) * (q.y - pi.y) / (pj.y - pi.y) + pi.x)
                    inside = !inside;
            }
            return inside;
        }

        static float DistToSegment(Vector2 q, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / len2) : 0f;
            return (q - (a + ab * t)).magnitude;
        }

        /// Two segments crossing properly (not merely touching at an end).
        static bool Intersect(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2)
        {
            float d1 = Cross(q2 - q, p - q), d2 = Cross(q2 - q, p2 - q);
            float d3 = Cross(p2 - p, q - p), d4 = Cross(p2 - p, q2 - p);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }

        /// Does the aimed drawing read as a rune (declared, or readable)? A caller that has
        /// already read these strokes hands the verdict in, and they are not read twice.
        public static bool LooksLikeRunes(List<Stroke> members, (RuneType rune, float score)? read = null)
        {
            if (members == null || members.Count == 0 || LooksLikeLoop(members)) return false;
            bool allDeclared = true;
            foreach (var m in members)
            {
                if (m == null || !m.Alive) return false;
                if (m.DeclaredRune == RuneType.None) allDeclared = false;
            }
            if (allDeclared) return true;
            var (rune, score) = read ?? RuneGlyph.ReadVerdict(members, Grimoire.LocalPlayerId);
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
            SfxLoops.Pen(p); // the book writes with a pencil too
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

        /// Your ink thins out, then burns with its ink back in the wand; the copies on other
        /// machines follow. Any pen work aborts it and the ink stays (aborted[0]).
        static IEnumerator BurnAway(List<Stroke> burn, Purse purse, bool[] aborted)
        {
            Vector3 fadeAt = burn.Count > 0 && burn[0] != null ? burn[0].Centroid() : Vector3.zero;
            NetSync.PushInkFx(NetSync.InkFxFade, fadeAt, FadeSeconds, burn); // the copies thin out too
            float t = 0f;
            while (t < FadeSeconds)
            {
                t += Time.deltaTime;
                if (SurfaceDrawer.IsPenActive) { aborted[0] = true; break; }
                float k = Mathf.Lerp(1f, 0.15f, Mathf.Clamp01(t / FadeSeconds));
                foreach (var s in burn) if (s != null && s.Alive) s.SetEvaporation(k);
                yield return null;
            }
            if (aborted[0])
            {
                foreach (var s in burn) if (s != null && s.Alive) s.SetEvaporation(1f);
                NetSync.PushInkFx(NetSync.InkFxRestore, fadeAt, 0f, burn);
                yield break;
            }
            float gone = 0f;
            foreach (var s in burn) if (s != null && s.Alive) { gone += s.PathLength(); s.Burn(); }
            NetSync.OnLocalInkBurned(burn); // its copies die everywhere too
            purse.Return(gone);
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
            var stopped = new[] { false };
            yield return w.StartCoroutine(BurnAway(burn, purse, stopped));
            if (stopped[0]) { _running = false; yield break; }

            // ---- 2. the rune, line by line ----
            float drawTime = Mathf.Max(0.05f, seconds - FadeSeconds);
            float drawn = 0f, have = 0f;
            int li = 0;
            Stroke cur = null;
            bool dry = false;
            float t = 0f;
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
            if (dry) NoInk(w, Loc.T("rune.noink"));

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
                if (!Juice.Sound(Sfx.RuneComplete, at / cnt)) Juice.Chime(at / cnt);
                NetSync.PushInkFx(NetSync.InkFxRune, at / cnt);
            }
            _running = false;
        }

        /// Your lines go and come back as the seal's sides; then the seal draws itself.
        static IEnumerator DrawSeal(DrawingWorld w, List<Vector3> path, Transform surface, Vector3 normal,
            List<Stroke> sides, Purse purse)
        {
            var burn = new List<Stroke>();
            if (sides != null) foreach (var s in sides) if (s != null && s.Alive) burn.Add(s);
            if (burn.Count > 0)
            {
                var stopped = new[] { false };
                yield return w.StartCoroutine(BurnAway(burn, purse, stopped));
                if (stopped[0]) { _running = false; yield break; }
            }
            yield return w.StartCoroutine(DrawRing(w, path, surface, normal, purse));
        }

        /// The seal, drawn in as one stroke and paid as it grows; the closure
        /// detector seals it. Dry, it stops short and the line stays as ink.
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
            else if (dry) NoInk(w, Loc.T("seal.noink"));
            if (s.Nodes.Count < 3 || s.PathLength() < MinPiece) s.Burn();
            else
            {
                w.CompleteStroke(s); // self-closure seals it (the host closes world loops)
                if (s.Alive)
                {
                    if (!Juice.Sound(Sfx.RuneComplete, s.Centroid())) Juice.Chime(s.Centroid());
                    NetSync.PushInkFx(NetSync.InkFxRune, s.Centroid());
                }
            }
            _running = false;
        }

        // ------------------------------------------------------------ world --
        /// A frame point pressed onto the surface it was drawn on; the plane
        /// point when the ray finds nothing of it.
        public static Vector3 OnSurface(Vector3 p, Vector3 normal, Transform surface)
        {
            if (surface == null) return p;
            var ray = new Ray(p + normal * 0.2f, -normal);
            if (Physics.Raycast(ray, out var hit, 0.5f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && (hit.collider.transform == surface || hit.collider.transform.IsChildOf(surface)))
            {
                // a zombie: onto its skin as posed, as the pen draws it
                var skin = SkinHit.OfZombie(hit.collider);
                if (skin == null) return hit.point;
                if (SkinHit.Raycast(skin, ray, 0.5f, out var onSkin, out _, out _)) return onSkin;
            }
            return p;
        }
    }
}
