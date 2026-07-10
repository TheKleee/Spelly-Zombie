using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// End-of-round social moment: every seal cast this round, rendered as a
    /// little ink thumbnail (boundary gold, runes cyan) with what it did.
    /// Displayed by RoundDirector during intermissions and on the end screen —
    /// the "look at this idiot's circle" feature from the roadmap.
    public static class SealGallery
    {
        public class Entry
        {
            public Texture2D Tex;
            public string Label;
            public float Time;                       // when it activated
            public List<List<Vector2>> BoundaryPts;  // kept for the autopsy replay
            public List<List<Vector2>> RunePts;
        }

        public static readonly List<Entry> Round = new List<Entry>();
        const int MaxEntries = 12;
        const int Size = 96;

        static readonly Color Bg = new Color(0.07f, 0.06f, 0.1f, 0.92f);
        static readonly Color BoundaryInk = new Color(1f, 0.8f, 0.25f);
        static readonly Color RuneInk = new Color(0.3f, 0.9f, 1f);
        static readonly Color FizzleInk = new Color(0.55f, 0.55f, 0.55f);

        public static void Clear()
        {
            foreach (var e in Round)
                if (e.Tex != null) Object.Destroy(e.Tex);
            Round.Clear();
        }

        /// Snapshot a seal the moment it activates (node positions are live now;
        /// they may be consumed later).
        public static void Capture(Seal seal, string comboName)
        {
            // gather boundary + payload points, projected into the seal plane
            Vector3 n = seal.PlaneNormal;
            Vector3 right = Vector3.ProjectOnPlane(
                Camera.main != null ? Camera.main.transform.right : Vector3.right, n);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.ProjectOnPlane(Vector3.forward, n);
            right.Normalize();
            Vector3 up = Vector3.Cross(right, n).normalized;
            Vector3 origin = seal.PlaneOrigin;

            var boundary = new List<List<Vector2>>();
            foreach (var e in seal.Boundary)
                boundary.Add(ProjectStroke(e.Stroke, origin, right, up));

            var runes = new List<List<Vector2>>();
            int recognized = 0, fizzles = 0;
            foreach (var g in seal.Runes)
            {
                if (g.Rune != RuneType.None) recognized++; else fizzles++;
                foreach (var m in g.Members)
                    runes.Add(ProjectStroke(m, origin, right, up));
            }

            var tex = Rasterize(boundary, runes);
            if (tex == null) return;

            string label = comboName ?? (recognized > 0
                ? $"{recognized} rune{(recognized > 1 ? "s" : "")}{(fizzles > 0 ? " +fizzle" : "")}"
                : "fizzle");

            Round.Add(new Entry
            {
                Tex = tex, Label = label, Time = UnityEngine.Time.time,
                BoundaryPts = boundary, RunePts = runes
            });
            while (Round.Count > MaxEntries)
            {
                if (Round[0].Tex != null) Object.Destroy(Round[0].Tex);
                Round.RemoveAt(0);
            }
        }

        static List<Vector2> ProjectStroke(Stroke s, Vector3 origin, Vector3 right, Vector3 up)
        {
            var pts = new List<Vector2>();
            if (s == null) return pts;
            foreach (var node in s.Nodes)
            {
                if (node == null) continue;
                Vector3 d = node.transform.position - origin;
                pts.Add(new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up)));
            }
            return pts;
        }

        static Texture2D Rasterize(List<List<Vector2>> boundary, List<List<Vector2>> runes)
        {
            // shared bounds over everything
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            void Grow(List<List<Vector2>> set)
            {
                foreach (var line in set)
                    foreach (var p in line)
                    {
                        min = Vector2.Min(min, p);
                        max = Vector2.Max(max, p);
                    }
            }
            Grow(boundary); Grow(runes);
            if (min.x > max.x) return null;
            Vector2 span = Vector2.Max(max - min, Vector2.one * 0.001f);
            float scale = (Size - 12) / Mathf.Max(span.x, span.y);
            Vector2 center = (min + max) * 0.5f;

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var pixels = new Color[Size * Size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Bg;

            void DrawSet(List<List<Vector2>> set, Color ink)
            {
                foreach (var line in set)
                    for (int i = 1; i < line.Count; i++)
                        DrawLine(pixels, ToPix(line[i - 1]), ToPix(line[i]), ink);
            }
            Vector2Int ToPix(Vector2 p)
            {
                Vector2 local = (p - center) * scale;
                return new Vector2Int(
                    Mathf.Clamp((int)(local.x + Size / 2f), 0, Size - 1),
                    Mathf.Clamp((int)(local.y + Size / 2f), 0, Size - 1));
            }

            DrawSet(boundary, BoundaryInk);
            DrawSet(runes, runes.Count > 0 ? RuneInk : FizzleInk);

            tex.SetPixels(pixels);
            tex.Apply(false);
            return tex;
        }

        static void DrawLine(Color[] px, Vector2Int a, Vector2Int b, Color ink)
        {
            int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            if (steps == 0) { px[a.y * Size + a.x] = ink; return; }
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                int x = Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t));
                int y = Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t));
                px[y * Size + x] = ink;
                if (x + 1 < Size) px[y * Size + x + 1] = ink; // 2px-ish weight
            }
        }

        /// Grid renderer for RoundDirector's OnGUI (intermission + end screen).
        public static void DrawGrid(float centerX, float y)
        {
            if (Round.Count == 0) return;
            const float tile = 104f;
            float total = Round.Count * tile;
            float x = centerX - total / 2f;

            var style = new GUIStyle(GUI.skin.label)
            { fontSize = 10, alignment = TextAnchor.UpperCenter };
            style.normal.textColor = new Color(1f, 1f, 1f, 0.85f);

            GUI.Label(new Rect(centerX - 200f, y - 22f, 400f, 20f),
                "— seals this round —", style);
            foreach (var e in Round)
            {
                if (e.Tex != null)
                    GUI.DrawTexture(new Rect(x + 4f, y, 96f, 96f), e.Tex);
                GUI.Label(new Rect(x, y + 98f, tile, 16f), e.Label, style);
                x += tile;
            }
        }
    }
}
