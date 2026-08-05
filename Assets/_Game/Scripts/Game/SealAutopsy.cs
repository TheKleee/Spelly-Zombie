using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// The clip generator: after a kill burst near a seal (or a wipe), a 5s overlay replays the seal being drawn; P saves a PNG — manufactures the shareable moment.
    public class SealAutopsy : MonoBehaviour
    {
        const int Size = 256;
        const float DrawPhase = 2.2f, HoldPhase = 2.8f;

        static SealAutopsy _active;
        static readonly List<float> _recentKills = new List<float>();

        Texture2D _tex;
        Color[] _pixels;
        List<Vector2[]> _lines;      // flattened boundary + runes, draw order
        int _boundaryLines;
        string _verdict;
        float _age;
        int _drawnSegments;
        Vector2 _min, _max;

        // ------------------------------------------------------------ hooks --
        /// RoundDirector reports every kill; a burst near a recent seal = clip.
        public static void OnKill()
        {
            float now = Time.time;
            _recentKills.Add(now);
            for (int i = _recentKills.Count - 1; i >= 0; i--) // no closure alloc per kill
                if (now - _recentKills[i] > 4f) _recentKills.RemoveAt(i);
            if (_recentKills.Count < 3 || _active != null) return;

            var seal = LastRecentSeal(6f);
            if (seal != null) Play(seal, $"{_recentKills.Count} KILLS — THE SEAL THAT DID IT");
        }

        /// Team wipe: show what doomed them (comedically, whatever fired last).
        public static void OnWipe()
        {
            var seal = LastRecentSeal(30f);
            if (seal != null && _active == null) Play(seal, "THE SEAL THAT DOOMED YOU");
        }

        static SealGallery.Entry LastRecentSeal(float window)
        {
            for (int i = SealGallery.Round.Count - 1; i >= 0; i--)
                if (Time.time - SealGallery.Round[i].Time <= window)
                    return SealGallery.Round[i];
            return null;
        }

        // ------------------------------------------------------------- play --
        static void Play(SealGallery.Entry entry, string verdict)
        {
            var go = new GameObject("SealAutopsy");
            var a = go.AddComponent<SealAutopsy>();
            _active = a;
            a._verdict = verdict + (entry.Label != null ? $"  ({entry.Label})" : "");
            a.PrepareLines(entry);
            a._tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            a._pixels = new Color[Size * Size];
            for (int i = 0; i < a._pixels.Length; i++)
                a._pixels[i] = new Color(0.06f, 0.05f, 0.09f, 0.94f);
        }

        void PrepareLines(SealGallery.Entry e)
        {
            _lines = new List<Vector2[]>();
            _min = new Vector2(float.MaxValue, float.MaxValue);
            _max = new Vector2(float.MinValue, float.MinValue);

            void Add(List<List<Vector2>> set)
            {
                if (set == null) return;
                foreach (var line in set)
                {
                    if (line.Count < 2) continue;
                    _lines.Add(line.ToArray());
                    foreach (var p in line)
                    {
                        _min = Vector2.Min(_min, p);
                        _max = Vector2.Max(_max, p);
                    }
                }
            }
            Add(e.BoundaryPts);
            _boundaryLines = _lines.Count;
            Add(e.RunePts);
        }

        void Update()
        {
            _age += Time.unscaledDeltaTime;
            if (_age > DrawPhase + HoldPhase)
            {
                _active = null;
                Destroy(_tex);
                Destroy(gameObject);
                return;
            }

            // progressive ink reveal
            int totalSegments = 0;
            foreach (var l in _lines) totalSegments += l.Length - 1;
            int target = Mathf.CeilToInt(Mathf.Clamp01(_age / DrawPhase) * totalSegments);
            while (_drawnSegments < target) DrawNextSegment();
            if (_drawnSegments >= target)
            {
                _tex.SetPixels(_pixels);
                _tex.Apply(false);
            }

            var kb = Keyboard.current;
            if (kb != null && kb.pKey.wasPressedThisFrame) SavePng();
        }

        void DrawNextSegment()
        {
            int seg = _drawnSegments++;
            int lineIndex = 0, segInLine = seg;
            while (lineIndex < _lines.Count && segInLine >= _lines[lineIndex].Length - 1)
            {
                segInLine -= _lines[lineIndex].Length - 1;
                lineIndex++;
            }
            if (lineIndex >= _lines.Count) return;

            Color ink = lineIndex < _boundaryLines
                ? new Color(1f, 0.8f, 0.25f) : new Color(0.3f, 0.9f, 1f);
            DrawLine(ToPix(_lines[lineIndex][segInLine]), ToPix(_lines[lineIndex][segInLine + 1]), ink);
        }

        Vector2Int ToPix(Vector2 p)
        {
            Vector2 span = Vector2.Max(_max - _min, Vector2.one * 0.001f);
            float scale = (Size - 24) / Mathf.Max(span.x, span.y);
            Vector2 local = (p - (_min + _max) * 0.5f) * scale;
            return new Vector2Int(
                Mathf.Clamp((int)(local.x + Size / 2f), 0, Size - 1),
                Mathf.Clamp((int)(local.y + Size / 2f), 0, Size - 1));
        }

        void DrawLine(Vector2Int a, Vector2Int b, Color ink)
        {
            int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : i / (float)steps;
                int x = Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t));
                int y = Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t));
                _pixels[y * Size + x] = ink;
                if (x + 1 < Size) _pixels[y * Size + x + 1] = ink;
                if (y + 1 < Size) _pixels[(y + 1) * Size + x] = ink;
            }
        }

        void SavePng()
        {
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "autopsies");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"seal_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");
                File.WriteAllBytes(file, _tex.EncodeToPNG());
                DrawingWorld.Instance?.LogEvent($"Autopsy saved: {file}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SpellyZombie] Autopsy save failed: {e.Message}");
            }
        }

        void OnGUI()
        {
            float w = 260f;
            float x = (Screen.width - w) / 2f, y = 40f;

            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(x, y, w, w), _tex);

            var style = new GUIStyle(GUI.skin.label)
            { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = new Color(1f, 0.85f, 0.4f);
            GUI.Label(new Rect(0, y + w + 4f, Screen.width, 24f), _verdict, style);

            var small = new GUIStyle(GUI.skin.label)
            { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            small.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(0, y + w + 28f, Screen.width, 18f), "P = save PNG", small);
        }
    }
}
