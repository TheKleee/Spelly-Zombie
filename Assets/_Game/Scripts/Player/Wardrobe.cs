using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    // (CostumeLibrary moved to its own file — Unity refuses to bind
    //  ScriptableObject assets whose class hides in a mismatched filename.
    //  It serves WEAPON SKINS only now; costumes live in the three
    //  SocketWardrobe catalogs.)

    /// Dresses bodies: library pieces when Marko has made them, placeholder
    /// hat + cloak until then. Demo look = team-colored hat and cloak; the
    /// cloak's back carries your STARTING RUNE icon (the locked cape design).
    public static class Wardrobe
    {
        // ------------------------------------------------------- attaching --

        /// Player outfit. Marko's SocketWardrobe catalog dresses FIRST — the
        /// local player wears their saved choices, remote avatars wear the
        /// outfitCode that arrived over the wire; hat/cape placeholders only
        /// fill sockets the catalog left bare. Returns the pieces so callers
        /// can retint when the team changes. clothColliders (chest/hips
        /// capsules) keep the simulated cape off the body.
        public static List<GameObject> DressPlayer(SocketSet set, Color team, RuneCardType? capeIcon,
            CapsuleCollider[] clothColliders = null, string outfitCode = null)
        {
            var pieces = SocketManager.Dress(set, SocketManager.Player,
                outfitCode != null ? SocketManager.ChooserFromCode(outfitCode)
                                   : SocketManager.GetChoice);

            // THE CATALOG IS LAW (Marko's control rule): once SZ_WardrobePlayer
            // exists, an EMPTY slot means "he wants nothing there" — no
            // placeholder sneaks in behind his back. Delete the WizardCape
            // option from the Cape slot and the cape is gone, permanently.
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
            // CLOTH RETIRED (Marko, after three rounds of Unity Cloth
            // misbehaving: stiff board → fell off → wrapped the neck).
            // Capes are RIGID pieces that inherit the body's clumsy wobble
            // through the spine socket — life without simulation.

            Retint(pieces, team);
            if (cape != null && capeIcon.HasValue) StampRune(cape.transform, capeIcon.Value);

            // any worn piece with "Wiggle"-named children comes alive (the
            // scarf-tail contract — see ScarfWiggle)
            ScarfWiggle.AttachAll(set.gameObject);
            return pieces;
        }

        /// REAL cloth simulation on a cape piece: works on Marko's Blender
        /// planes (subdivided, pivot at the top per the convention — a
        /// MeshFilter is converted in place) and on the placeholder. The top
        /// 8% of vertices pin to the shoulders; the rest hang, sway with
        /// movement, and collide with the given body capsules. Rigid capes
        /// (fewer than 12 verts) are left alone.
        public static void MakeCloth(GameObject capePiece, CapsuleCollider[] colliders)
        {
            var smr = capePiece.GetComponentInChildren<SkinnedMeshRenderer>();
            GameObject target;
            Mesh mesh;
            var mf = smr == null ? capePiece.GetComponentInChildren<MeshFilter>() : null;
            if (smr != null)
            {
                target = smr.gameObject;
                mesh = smr.sharedMesh;
            }
            else
            {
                if (mf == null || mf.sharedMesh == null) return;
                mesh = mf.sharedMesh;
                target = mf.gameObject;
            }

            // DISQUALIFY BEFORE CONVERTING — a cape that stays rigid must
            // keep its original renderer (the old order converted first and
            // could leave the cape rendererless → invisible cape).
            if (mesh == null || mesh.vertexCount < 12) return;
            if (!mesh.isReadable)
            {
                // cloth pins by reading vertices — a mesh imported without
                // Read/Write would throw mid-dressing. Stay stiff, say why.
                Debug.LogWarning($"[SpellyZombie] Cape mesh '{mesh.name}' has no Read/Write — the cape stays STIFF. " +
                    "Fix: select the piece and re-run Spelly Zombie → Wardrobe → Add Selection (it enables it now), " +
                    "or tick Read/Write in the model's Import Settings.");
                return;
            }
            if (target.GetComponent<Cloth>() != null) return;

            if (smr == null)
            {
                // ONE RENDERER PER GAMEOBJECT (old ship lesson): the deferred
                // Destroy left the MeshRenderer alive this frame, the
                // SkinnedMeshRenderer add silently failed, and Marko's cape
                // vanished. Immediate removal makes the swap legal.
                var mr = target.GetComponent<MeshRenderer>();
                var mat = mr != null ? mr.sharedMaterial : null;
                Object.DestroyImmediate(mf);
                if (mr != null) Object.DestroyImmediate(mr);
                smr = target.AddComponent<SkinnedMeshRenderer>();
                if (smr == null) return; // never strand a rendererless cape
                smr.sharedMesh = mesh;
                smr.sharedMaterial = mat;
            }

            var cloth = target.AddComponent<Cloth>();
            var verts = mesh.vertices;

            // PIN BY THE PIVOT, not by "highest local Y": the convention is
            // pivot-at-the-attach-edge, and Marko's meshes ship with baked
            // import rotations that make local Y mean anything. Y-top pinning
            // grabbed the wrong edge of his cape — gravity then peeled the
            // whole thing off the shoulders ("the cape falls off the player").
            // Vertices nearest the pivot = the attach edge, every time.
            float minD = float.MaxValue, maxD = 0f;
            for (int i = 0; i < verts.Length; i++)
            {
                float d = verts[i].magnitude;
                if (d < minD) minD = d;
                if (d > maxD) maxD = d;
            }
            float pinDist = minD + Mathf.Max((maxD - minD) * 0.15f, 0.04f);

            // every free vertex gets a leash equal to its natural PENDULUM
            // ARC (distance from the pinned band): any authored angle drapes
            // fully to vertical, but the hem can never travel past its own
            // swing radius (no toga-flip over the shoulder).
            var coeffs = new ClothSkinningCoefficient[verts.Length];
            var pinCenter = Vector3.zero;
            int pinCount = 0;
            for (int i = 0; i < verts.Length; i++)
                if (verts[i].magnitude <= pinDist) { pinCenter += verts[i]; pinCount++; }
            if (pinCount == 0 || pinCount == verts.Length) return; // degenerate mesh — leave rigid
            pinCenter /= pinCount;
            for (int i = 0; i < verts.Length; i++)
            {
                bool pinned = verts[i].magnitude <= pinDist;
                float arc = (verts[i] - pinCenter).magnitude;
                coeffs[i].maxDistance = pinned ? 0f : Mathf.Max(0.05f, arc * 1.25f);
                coeffs[i].collisionSphereDistance = 0f;
            }
            cloth.coefficients = coeffs;
            cloth.damping = 0.5f;
            cloth.worldVelocityScale = 0.35f;     // running streams the cape
            cloth.worldAccelerationScale = 0.5f;
            if (colliders != null)
            {
                var valid = new List<CapsuleCollider>();
                foreach (var c in colliders) if (c != null) valid.Add(c);
                cloth.capsuleColliders = valid.ToArray();
            }
        }

        /// Zombies: Marko's zombie catalog rolls RANDOM pieces per socket (a
        /// non-zero seed makes the roll deterministic — host and clients dress
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

            // zombie accessories wiggle too (a dangling chain, a droopy hood
            // tip — same "Wiggle" naming contract as the player scarf)
            ScarfWiggle.AttachAll(set.gameObject);
        }

        /// The demon (summoned from the darkness — zombie moves, darker look):
        /// EVERY filled slot of the demon catalog rolls a random piece. Call
        /// wherever the demon's rigged body is built, with its id as seed.
        public static List<GameObject> DressDemon(SocketSet set, int seed = 0)
            => SocketManager.DressRandom(set, SocketManager.Demon, 1f, seed);

        /// A weapon's Blender-made look, or null while primitives stand in.
        /// THE OVERRIDE SHELF WINS (one rule everywhere): a prefab named
        /// after the weapon in Resources/Custom beats the costume catalog, so
        /// weapons need no wizard run and no "Weapon_" prefix — drag, done.
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

        /// "_Team"-named renderers take the color; pieces without any get
        /// tinted whole (the placeholders work this way).
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
                foreach (var r in marked.Count > 0 ? marked : new List<Renderer>(all))
                    if (!(r is LineRenderer)) r.sharedMaterial = MatterFX.Get(team, MoteShade.Opaque);
            }
        }

        // ---------------------------------------------- demo placeholders --

        static GameObject PlaceholderHat(Transform socket)
        {
            if (socket == null) return null;
            var hat = new GameObject("Hat_Placeholder");
            hat.transform.SetParent(socket, false); // sits ON the crown — it was right before
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

        /// A subdivided plane hanging from the shoulder line — MakeCloth turns
        /// it into simulated fabric (exactly what Marko's Blender capes will be).
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
                    // both windings — a cape is seen from both sides
                    tris.AddRange(new[] { i, i + 1, below, i + 1, below + 1, below });
                    tris.AddRange(new[] { i, below, i + 1, i + 1, below, below + 1 });
                }
            var mesh = new Mesh { vertices = verts, uv = uvs, triangles = tris.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var smr = sheet.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.sharedMaterial = MatterFX.Get(Color.white, MoteShade.Opaque);
            smr.updateWhenOffscreen = true;

            foreach (var t in cloak.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = socket.gameObject.layer;
            return cloak;
        }

        // ------------------------------------------------ the cape's rune --

        /// The locked design: the cloak's back shows the STARTING rune you
        /// chose, rasterized from the actual glyph template at runtime.
        public static void StampRune(Transform cape, RuneCardType card)
        {
            var poly = RuneLibrary.GlyphPolyline(IconRune(card));
            if (poly == null || poly.Count < 2 || cape == null) return;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "RuneIcon";
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(cape, false);
            // near the pinned top edge, where simulated cloth barely moves
            quad.transform.localPosition = new Vector3(0f, -0.06f, -0.125f);
            quad.transform.localRotation = Quaternion.Euler(-6f, 180f, 0f); // face out the back
            quad.transform.localScale = Vector3.one * 0.2f;
            quad.layer = cape.gameObject.layer;

            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
                quad.GetComponent<Renderer>().material =
                    new Material(shader) { mainTexture = Rasterize(poly, Color.white) };
        }

        /// A rune glyph as a texture (grimoire pages, cape icons, cards…).
        /// Prefers YOUR recorded handwriting (the F-key templates) — the book
        /// shows the alphabet you actually taught the game, not the seed shapes.
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

        /// Arbitrary strokes as a texture — the grimoire's diagrams (seal
        /// lesson etc.) draw with the same pen the rune icons use.
        public static Texture2D InkTexture(IReadOnlyList<IReadOnlyList<Vector2>> strokes, Color ink)
            => Rasterize(strokes, ink);

        /// Multi-stroke rasterizer — recorded handwriting keeps its pen lifts.
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
