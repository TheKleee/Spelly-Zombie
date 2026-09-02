using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Dresses bodies: catalog pieces, with placeholder hat + cloak until art
    /// exists. The cloak's back carries your starting rune icon.
    public static class Wardrobe
    {
        // ------------------------------------------------------- attaching --

        /// Player outfit. The catalog dresses first (local = saved choices,
        /// remote = outfitCode); placeholders only fill sockets it left bare.
        /// Returns the pieces so callers can retint when the team changes.
        public static List<GameObject> DressPlayer(SocketSet set, Color team, RuneCardType? capeIcon,
            string outfitCode = null)
        {
            var pieces = SocketManager.Dress(set, SocketManager.Player,
                outfitCode != null ? SocketManager.ChooserFromCode(outfitCode)
                                   : SocketManager.GetChoice);

            // once the catalog exists, an empty slot means nothing worn there -
            // no placeholder fills it
            bool catalogRules = SocketManager.Player != null;

            var hatSocket = set.Get("Hat");
            if (!catalogRules && hatSocket != null && hatSocket.childCount == 0)
            {
                var hat = Attach(set, "Hat", zombiePool: false);
                if (hat == null) hat = PlaceholderHat(hatSocket);
                if (hat != null) pieces.Add(hat);
            }

            var capeSocket = set.Get("Cape");
            GameObject cape = capeSocket != null && capeSocket.childCount > 0
                ? capeSocket.GetChild(0).gameObject : null;
            if (cape == null && !catalogRules)
            {
                cape = Attach(set, "Cape", zombiePool: false);
                if (cape == null) cape = PlaceholderCloak(capeSocket);
                if (cape != null) pieces.Add(cape);
            }
            // capes are RIGID pieces - no cloth simulation; they wobble
            // through the spine socket

            Retint(pieces, team);
            if (cape != null && capeIcon.HasValue) StampRune(cape.transform, capeIcon.Value);

            // any worn piece with "Wiggle"-named children comes alive (the
            // scarf-tail contract - see ScarfWiggle)
            ScarfWiggle.AttachAll(set.gameObject);
            return pieces;
        }

        /// Zombies: the zombie catalog rolls RANDOM pieces per socket (a
        /// non-zero seed makes the roll deterministic - host and clients dress
        /// the same zombie identically from its id); the legacy Z-name pool
        /// fills any socket the catalog left bare.
        public static void DressZombie(SocketSet set, float chance = 0.35f, int seed = 0)
        {
            if (set == null) return;
            SocketManager.DressRandom(set, SocketManager.Zombie, chance, seed);

            var lib = CostumeLibrary.I;
            if (lib != null)
                foreach (var socketName in new[]
                    { "Hat", "Head", "Cape", "Chest", "Belt", "ShoulderL", "ShoulderR", "LegL", "LegR" })
                {
                    var s = set.Get(socketName);
                    if (s == null || s.childCount > 0) continue; // catalog dressed it
                    if (Random.value > chance) continue;
                    Attach(set, socketName, zombiePool: true);
                }

            // zombie accessories use the same "Wiggle" naming contract
            ScarfWiggle.AttachAll(set.gameObject);
        }

        /// The demon (summoned from the darkness - zombie moves, darker look):
        /// EVERY filled slot of the demon catalog rolls a random piece. Call
        /// wherever the demon's rigged body is built, with its id as seed.
        public static List<GameObject> DressDemon(SocketSet set, int seed = 0)
            => SocketManager.DressRandom(set, SocketManager.Demon, 1f, seed);

        /// A weapon's authored look, or null while primitives stand in. A
        /// prefab named after the weapon in Resources/Custom beats the costume
        /// catalog (no "Weapon_" prefix needed there).
        public static GameObject WeaponSkin(string key)
        {
            var shelf = PrefabVault.Get(key);
            if (shelf != null) return shelf;

            var lib = CostumeLibrary.I;
            if (lib == null) return null;
            string wanted = "Weapon_" + key;
            foreach (var p in lib.Pieces)
                if (p != null && p.name == wanted) return p;
            return null;
        }

        static GameObject Attach(SocketSet set, string socketName, bool zombiePool)
        {
            var lib = CostumeLibrary.I;
            var socket = set != null ? set.Get(socketName) : null;
            if (lib == null || socket == null) return null;

            string prefix = (zombiePool ? "Z" : "") + socketName + "_";
            var candidates = new List<GameObject>();
            foreach (var p in lib.Pieces)
                if (p != null && p.name.StartsWith(prefix)) candidates.Add(p);
            if (candidates.Count == 0) return null;

            var piece = Object.Instantiate(
                candidates[Random.Range(0, candidates.Count)], socket, false);
            foreach (var t in piece.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = socket.gameObject.layer;
            return piece;
        }

        // --------------------------------------------------------- tinting --

        /// "_Team"-named renderers take the color; a piece with no _Team
        /// renderer keeps its own materials. Only empty material slots get
        /// filled, so code-built placeholders still show up.
        static readonly HashSet<string> _untinted = new HashSet<string>();

        public static void Retint(List<GameObject> pieces, Color team)
        {
            if (pieces == null) return;
            foreach (var piece in pieces)
            {
                if (piece == null) continue;
                var all = piece.GetComponentsInChildren<Renderer>(true);
                var marked = new List<Renderer>();
                foreach (var r in all)
                    if (r.gameObject.name.EndsWith("_Team")) marked.Add(r);

                if (marked.Count > 0)
                {
                    foreach (var r in marked)
                        if (!(r is LineRenderer)) r.sharedMaterial = MatterFX.Get(team, MoteShade.Opaque);
                    continue;
                }

                // the art, untouched - fill only slots that would draw nothing
                foreach (var r in all)
                    if (!(r is LineRenderer) && r.sharedMaterial == null)
                        r.sharedMaterial = MatterFX.Get(team, MoteShade.Opaque);

                string label = piece.name.Replace("(Clone)", "").Trim();
                if (_untinted.Add(label))
                    Debug.LogWarning($"[SpellyZombie] Costume piece '{label}' has no \"_Team\" renderer, " +
                        "KEEPING ITS OWN MATERIALS. Rename a mesh to end in _Team to team-tint part of it.", piece);
            }
        }

        // ---------------------------------------------- demo placeholders --

        static GameObject PlaceholderHat(Transform socket)
        {
            if (socket == null) return null;
            var hat = new GameObject("Hat_Placeholder");
            hat.transform.SetParent(socket, false); // sits ON the crown
            void Part(string partName, Vector3 pos, Vector3 scale, float tilt)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                p.name = partName + "_Team";
                Object.Destroy(p.GetComponent<Collider>());
                p.transform.SetParent(hat.transform, false);
                p.transform.localPosition = pos;
                p.transform.localScale = scale;
                p.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
            }
            Part("Brim", new Vector3(0f, 0.005f, 0f), new Vector3(0.30f, 0.008f, 0.30f), 0f);
            Part("Base", new Vector3(0f, 0.05f, 0f), new Vector3(0.16f, 0.05f, 0.16f), 3f);
            Part("Mid", new Vector3(0.015f, 0.12f, 0f), new Vector3(0.10f, 0.045f, 0.10f), 9f);
            Part("Tip", new Vector3(0.04f, 0.18f, 0f), new Vector3(0.045f, 0.04f, 0.045f), 24f);
            foreach (var t in hat.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = socket.gameObject.layer;
            return hat;
        }

        /// A subdivided plane hanging from the shoulder line - rigid, like all
        /// capes; the art replaces it wholesale.
        static GameObject PlaceholderCloak(Transform socket)
        {
            if (socket == null) return null;
            var cloak = new GameObject("Cape_Placeholder");
            cloak.transform.SetParent(socket, false);

            var sheet = new GameObject("Cloth_Team");
            sheet.transform.SetParent(cloak.transform, false);
            sheet.transform.localPosition = new Vector3(0f, 0.08f, -0.12f); // hangs off the shoulders
            sheet.transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);

            const int cols = 6, rows = 10;
            const float width = 0.34f, height = 0.62f;
            var verts = new Vector3[(cols + 1) * (rows + 1)];
            var uvs = new Vector2[verts.Length];
            for (int r = 0; r <= rows; r++)
                for (int c = 0; c <= cols; c++)
                {
                    int i = r * (cols + 1) + c;
                    verts[i] = new Vector3(width * (c / (float)cols - 0.5f),
                        -height * (r / (float)rows), 0f);
                    uvs[i] = new Vector2(c / (float)cols, 1f - r / (float)rows);
                }
            var tris = new List<int>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int i = r * (cols + 1) + c;
                    int below = i + cols + 1;
                    // both windings - a cape is seen from both sides
                    tris.AddRange(new[] { i, i + 1, below, i + 1, below + 1, below });
                    tris.AddRange(new[] { i, below, i + 1, i + 1, below, below + 1 });
                }
            var mesh = new Mesh { vertices = verts, uv = uvs, triangles = tris.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            sheet.AddComponent<MeshFilter>().sharedMesh = mesh;
            sheet.AddComponent<MeshRenderer>().sharedMaterial =
                MatterFX.Get(Color.white, MoteShade.Opaque);

            foreach (var t in cloak.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = socket.gameObject.layer;
            return cloak;
        }

        // ------------------------------------------------ the cape's rune --

        /// The cloak's back shows your starting rune, the recorded shape
        /// when there is one.
        public static void StampRune(Transform cape, RuneCardType card)
        {
            var art = RuneIcon(IconRune(card), Color.white);
            if (art == null || cape == null) return;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "RuneIcon";
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(cape, false);
            // near the pinned top edge
            quad.transform.localPosition = new Vector3(0f, -0.06f, -0.125f);
            quad.transform.localRotation = Quaternion.Euler(-6f, 180f, 0f); // face out the back
            quad.transform.localScale = Vector3.one * 0.2f;
            quad.layer = cape.gameObject.layer;

            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
                quad.GetComponent<Renderer>().material =
                    new Material(shader) { mainTexture = art };
        }

        /// A rune glyph as a texture (grimoire pages, cape icons, cards…).
        /// Prefers recorded handwriting over the seed polyline.
        public static Texture2D RuneIcon(RuneType rune, Color ink)
        {
            var recorded = RuneLibrary.RecordedStrokes(rune);
            if (recorded != null && recorded.Count > 0) return Rasterize(recorded, ink);
            var poly = RuneLibrary.GlyphPolyline(rune);
            return poly != null && poly.Count >= 2
                ? Rasterize(new IReadOnlyList<Vector2>[] { poly }, ink) : null;
        }

        static RuneType IconRune(RuneCardType card)
        {
            switch (card)
            {
                case RuneCardType.Heat: return RuneType.HeatUp;
                case RuneCardType.State: return RuneType.StateSolid;
                case RuneCardType.Luminance: return RuneType.LuminanceUp;
                case RuneCardType.Sticky: return RuneType.StickyUp;
                case RuneCardType.Direction: return RuneType.DirectionAway;
                default: return RuneType.DensityUp;
            }
        }

        static Texture2D Rasterize(IReadOnlyList<Vector2> poly, Color ink)
            => Rasterize(new IReadOnlyList<Vector2>[] { poly }, ink);

        /// Arbitrary strokes as a texture - the grimoire's diagrams (seal
        /// lesson etc.) draw with the same pen the rune icons use.
        public static Texture2D InkTexture(IReadOnlyList<IReadOnlyList<Vector2>> strokes, Color ink)
            => Rasterize(strokes, ink);

        /// Multi-stroke rasterizer - recorded handwriting keeps its pen lifts.
        static Texture2D Rasterize(IReadOnlyList<IReadOnlyList<Vector2>> strokes, Color ink)
        {
            const int size = 64;
            bool any = false;
            Vector2 min = Vector2.zero, max = Vector2.zero;
            foreach (var stroke in strokes)
                foreach (var p in stroke)
                {
                    if (!any) { min = max = p; any = true; }
                    else { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
                }
            if (!any) return null;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

            Vector2 span = Vector2.Max(max - min, Vector2.one * 0.001f);
            float scale = (size - 14) / Mathf.Max(span.x, span.y);
            Vector2 center = (min + max) * 0.5f;

            Vector2Int Pix(Vector2 p)
            {
                Vector2 local = (p - center) * scale;
                return new Vector2Int(
                    Mathf.Clamp((int)(local.x + size / 2f), 1, size - 2),
                    Mathf.Clamp((int)(local.y + size / 2f), 1, size - 2));
            }
            foreach (var poly in strokes)
                for (int i = 1; i < poly.Count; i++)
                {
                    Vector2Int a = Pix(poly[i - 1]), b = Pix(poly[i]);
                    int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y), 1);
                    for (int s = 0; s <= steps; s++)
                    {
                        float t = s / (float)steps;
                        int x = Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t));
                        int y = Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t));
                        for (int dx = 0; dx <= 1; dx++)
                            for (int dy = 0; dy <= 1; dy++)
                                px[(y + dy) * size + x + dx] = ink;
                    }
                }
            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }
    }
}
