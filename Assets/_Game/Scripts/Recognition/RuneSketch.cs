using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The drawing pad's math, one copy for the editor's Rune Creator and the
    /// game's: pad pixels to the sample frame and back, a round brush into a
    /// texture, the eraser that cuts a stroke in two, a small picture.
    public static class RuneSketch
    {
        /// Pad pixels (y down) to the sample frame: metres, y up, centred. A
        /// glyph filling the pad comes out about the size of one drawn in the game.
        public static List<List<Vector2>> ToSample(IReadOnlyList<IReadOnlyList<Vector2>> strokes, float w, float h, float metres = 0.4f)
        {
            float k = metres / Mathf.Max(1f, Mathf.Max(w, h));
            var outp = new List<List<Vector2>>();
            foreach (var s in strokes)
            {
                var o = new List<Vector2>(s.Count);
                foreach (var p in s) o.Add(new Vector2((p.x - w * 0.5f) * k, (h * 0.5f - p.y) * k));
                outp.Add(o);
            }
            return outp;
        }

        /// A sample fitted into a box on the pad, aspect kept, turned by angle
        /// degrees about the box centre, y flipped back down.
        public static List<List<Vector2>> Fit(IReadOnlyList<IReadOnlyList<Vector2>> sample, Rect box, float angle)
        {
            var outp = new List<List<Vector2>>();
            if (sample == null) return outp;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var s in sample) foreach (var p in s)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            if (minX > maxX) return outp;
            float span = Mathf.Max(maxX - minX, maxY - minY, 1e-4f);
            float k = Mathf.Min(box.width, box.height) / span;
            Vector2 mid = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            float c = Mathf.Cos(angle * Mathf.Deg2Rad), sn = Mathf.Sin(angle * Mathf.Deg2Rad);
            foreach (var s in sample)
            {
                var o = new List<Vector2>(s.Count);
                foreach (var p in s)
                {
                    float dx = (p.x - mid.x) * k, dy = -(p.y - mid.y) * k;
                    o.Add(new Vector2(box.center.x + dx * c - dy * sn, box.center.y + dx * sn + dy * c));
                }
                outp.Add(o);
            }
            return outp;
        }

        /// A round brush along one pad stroke into a texture's pixels; k =
        /// image pixels per pad pixel. Texture rows run bottom-up.
        public static void Paint(Color32[] px, int w, int h, IReadOnlyList<Vector2> stroke, float k, float radius, Color32 ink)
        {
            if (stroke == null || stroke.Count == 0) return;
            float step = Mathf.Max(1f, radius * 0.5f);
            for (int i = 0; i < stroke.Count; i++)
            {
                Vector2 a = stroke[Mathf.Max(0, i - 1)] * k, b = stroke[i] * k;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / step));
                for (int s = 0; s <= steps; s++) Disc(px, w, h, Vector2.Lerp(a, b, s / (float)steps), radius, ink);
            }
        }

        static void Disc(Color32[] px, int w, int h, Vector2 c, float r, Color32 ink)
        {
            float cy = h - 1 - c.y;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(c.x - r)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(c.x + r));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(cy + r));
            float r2 = r * r;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if ((x - c.x) * (x - c.x) + (y - cy) * (y - cy) <= r2) px[y * w + x] = ink;
        }

        /// Rubs out every point within radius; a stroke cut in two lives on as
        /// two. `cut(i, n)` hears stroke i replaced by n pieces, so a list kept
        /// beside the strokes can follow.
        public static bool Erase(List<List<Vector2>> strokes, Vector2 at, float radius, System.Action<int, int> cut = null)
        {
            float r2 = radius * radius;
            bool any = false;
            for (int i = strokes.Count - 1; i >= 0; i--)
            {
                var s = strokes[i];
                bool hit = false;
                foreach (var p in s) if ((p - at).sqrMagnitude <= r2) { hit = true; break; }
                if (!hit) continue;
                any = true;
                var pieces = new List<List<Vector2>>();
                List<Vector2> run = null;
                foreach (var p in s)
                {
                    if ((p - at).sqrMagnitude <= r2) { run = null; continue; }
                    if (run == null) { run = new List<Vector2>(); pieces.Add(run); }
                    run.Add(p);
                }
                strokes.RemoveAt(i);
                int insert = i;
                foreach (var piece in pieces)
                {
                    if (piece.Count < 2) continue;
                    strokes.Insert(insert, piece);
                    insert++;
                }
                cut?.Invoke(i, insert - i);
            }
            return any;
        }

        /// A small square picture of a sample (sample frame in, texture out).
        public static Texture2D Thumbnail(IReadOnlyList<IReadOnlyList<Vector2>> sample, int size, Color32 ink, Color32 ground,
            float radius = 1.6f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = ground;
            float pad = size * 0.12f;
            foreach (var s in Fit(sample, new Rect(pad, pad, size - pad * 2f, size - pad * 2f), 0f))
                Paint(px, size, size, s, 1f, radius, ink);
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }
    }
}
