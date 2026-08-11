using UnityEngine;

namespace SpellyZombie
{
    /// The precision-mode cursor: a clean brush circle with a center dot
    /// (Photoshop / Meccha Chameleon style) instead of the OS arrow — you're
    /// drawing, and the cursor should say so. Generated once at runtime,
    /// white ring with a dark outline so it reads on any surface.
    public static class PrecisionCursor
    {
        static Texture2D _tex;

        public static void Apply()
        {
            if (_tex == null) Build();
            Cursor.SetCursor(_tex, new Vector2(_tex.width / 2f, _tex.height / 2f), CursorMode.Auto);
        }

        public static void Clear() =>
            InkCursor.Apply(); // hand back to the quill, never the OS arrow

        static void Build()
        {
            // 32×32 IS LOAD-BEARING (Marko Aug 11: "aim appears and
            // disappears — it should follow the mouse"). Windows only makes
            // a HARDWARE cursor from a 32×32 texture; this ring was 48 and
            // silently fell back to Unity's software cursor — drawn a frame
            // late and skipped entirely on hitch frames, so the brush circle
            // flickered while the 32px quill never did.
            const int size = 32;
            const float ring = 10f;   // ring radius in px
            const float thick = 1.6f; // ring stroke width
            _tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - size / 2f + 0.5f, dy = y - size / 2f + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ringDist = Mathf.Abs(d - ring);

                    Color c = clear;
                    if (ringDist < thick) c = Color.white;                       // the circle
                    else if (ringDist < thick + 1.2f) c = new Color(0f, 0f, 0f, 0.8f); // dark outline
                    if (d < 1.6f) c = Color.white;                               // center dot
                    else if (d < 2.6f && c.a == 0f) c = new Color(0f, 0f, 0f, 0.8f);
                    _tex.SetPixel(x, y, c);
                }
            _tex.Apply();
        }
    }
}
