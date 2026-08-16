using UnityEngine;

namespace SpellyZombie
{
    /// The default cursor, in-world: a wizard's QUILL with a gold nib and a
    /// falling rune-cyan ink drop - the OS arrow doesn't belong in a game
    /// about drawing. Procedural like PrecisionCursor (no asset files);
    /// applied once at load and re-applied whenever precision mode hands the
    /// cursor back. overrides it by dropping a Texture2D at
    /// Resources/Custom/Cursor (import: Read/Write ON, compression None) -
    /// the art wins without touching code.
    public static class InkCursor
    {
        static Texture2D _tex;
        static Vector2 _hotspot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Apply()
        {
            if (_tex == null) Build();
            Cursor.SetCursor(_tex, _hotspot, CursorMode.Auto);
        }

        static void Build()
        {
            var custom = Resources.Load<Texture2D>("Custom/Cursor");
            if (custom != null)
            {
                _tex = custom;
                _hotspot = new Vector2(4f, 4f); // the art: tip at top-left
                return;
            }

            const int S = 32;
            _tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color[S * S];

            // strongest-alpha-wins painter - shadow first, art over it
            void Dot(float cx, float cy, float r, Color c)
            {
                for (int y = Mathf.Max(0, (int)(cy - r - 1f)); y <= Mathf.Min(S - 1, (int)(cy + r + 1f)); y++)
                    for (int x = Mathf.Max(0, (int)(cx - r - 1f)); x <= Mathf.Min(S - 1, (int)(cx + r + 1f)); x++)
                    {
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        float a = Mathf.Clamp01(r - d) * c.a; // soft 1px edge
                        int i = y * S + x;
                        if (a > px[i].a) px[i] = new Color(c.r, c.g, c.b, a);
                    }
            }
            void Line(Vector2 a, Vector2 b, float r0, float r1, Color c)
            {
                int steps = (int)((b - a).magnitude * 2f) + 1;
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    var p = Vector2.Lerp(a, b, t);
                    Dot(p.x, p.y, Mathf.Lerp(r0, r1, t), c);
                }
            }

            // tip top-left, feather tail bottom-right (SetPixel coords: y up)
            var tip = new Vector2(5f, 26f);
            var tail = new Vector2(25f, 7f);
            var mid = Vector2.Lerp(tip, tail, 0.45f);
            Vector2 perp = new Vector2(-(tail - tip).y, (tail - tip).x).normalized;

            var shadow = new Color(0f, 0f, 0f, 0.55f);
            var inkDark = new Color(0.13f, 0.11f, 0.18f);       // the ink itself
            var feather = new Color(0.5f, 0.36f, 0.68f);        // wizardly purple
            var featherLight = new Color(0.66f, 0.55f, 0.82f);
            var nibGold = new Color(1f, 0.78f, 0.25f);          // sealed-ink gold
            var dropCyan = new Color(0.35f, 0.88f, 1f);         // rune cyan

            Line(tip + new Vector2(1f, -1f), tail + new Vector2(1f, -1f), 1.3f, 2.8f, shadow);
            Line(mid + perp * 1.6f, tail + perp * 2.6f, 1.1f, 1.5f, feather);       // barbs
            Line(mid - perp * 1.6f, tail - perp * 2.6f, 1.1f, 1.5f, featherLight);
            Line(mid, tail, 1.3f, 2f, feather);                                     // tail body
            Line(tip, tail, 0.9f, 1.6f, inkDark);                                   // the shaft
            Line(tip, Vector2.Lerp(tip, tail, 0.16f), 0.9f, 1.2f, nibGold);         // gold nib
            Dot(3.4f, 21.2f, 1.5f, dropCyan);                                       // the drop

            _tex.SetPixels(px);
            _tex.filterMode = FilterMode.Bilinear;
            _tex.Apply();
            _hotspot = new Vector2(tip.x, S - 1f - tip.y); // hotspot = the nib tip
        }
    }
}
