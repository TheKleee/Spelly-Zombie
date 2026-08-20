using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// PHASE 1 TEST SCENE : the
    /// wand tether in isolation - draw to drain the wand, watch it dissolve,
    /// refill at the cauldron to reform it. No RoundDirector, so nothing
    /// drains or regens except the WandState itself and the cauldron.
    ///
    /// Build into a FRESH empty scene (File  New Scene), then press Play.
    /// • left-click to draw on the floor/wall  the wand-ink bar drops
    ///   • let it hit empty → "WAND DRYING" → after the grace → WANDLESS (can't draw)
    /// • walk onto the cauldron  it refills you  the wand reforms
    public static class WandTestBuilder
    {
        [MenuItem("Spelly Zombie/Test/Build Wand Tether Test (empty scene)")]
        public static void Build()
        {
            foreach (var guard in new[] { "SZ_Menu", "SZ_Village", "SZ_GameMap", "SZ_Player", "SZ_Test", "SZ_WandTest" })
                if (GameObject.Find(guard) != null)
                {
                    Debug.LogError($"[SpellyZombie] REFUSING: scene already contains '{guard}'. Use File → New Scene first.");
                    return;
                }

            EnvironmentTools.WireFxLibrary();
            var root = new GameObject("SZ_WandTest").transform;

            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                cam.gameObject.SetActive(false);

            if (Object.FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("SZ_Sun");
                sun.transform.SetParent(root, false);
                var l = sun.AddComponent<Light>();
                l.type = LightType.Directional; l.intensity = 1.15f; l.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            var lit = Shader.Find("Universal Render Pipeline/Lit");

            // floor - one big drawable stone surface
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(root, false);
            floor.transform.localScale = new Vector3(3f, 1f, 3f);
            if (lit != null) floor.GetComponent<Renderer>().sharedMaterial =
                new Material(lit) { color = new Color(0.6f, 0.6f, 0.58f) };
            floor.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Stone;

            // a wall you can draw on at eye level
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "DrawWall";
            wall.transform.SetParent(root, false);
            wall.transform.position = new Vector3(0f, 1.6f, 4.5f);
            wall.transform.localScale = new Vector3(5f, 3.2f, 0.3f);
            if (lit != null) wall.GetComponent<Renderer>().sharedMaterial =
                new Material(lit) { color = new Color(0.7f, 0.68f, 0.64f) };
            wall.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Stone;

            // the cauldron, pre-filled so refill works instantly. No ignition:
            // a hot pot burn-damages itself to death and kills the refill loop.
            var pot = CaveCauldron.Conjure(new Vector3(3.5f, 0f, 1.5f));
            pot.Fill = CaveCauldron.Capacity; // brimming, so wands refill on contact

            // spare ink ore to feed it, so refilling the pot itself is testable
            for (int i = 0; i < 3; i++)
            {
                var ore = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ore.name = "InkRuneStone";
                ore.transform.SetParent(root, false);
                ore.transform.position = new Vector3(-3f + i * 0.6f, 0.3f, 1.5f);
                ore.transform.localScale = Vector3.one * 0.5f;
                var rb = ore.AddComponent<Rigidbody>();
                rb.mass = 8f;
                ore.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Stone;
                ore.AddComponent<InkRuneStone>();
            }

            // the player - reuse the sandbox bean, then bolt on the tether
            TestSandboxBuilder.BuildBeanPlayer(root);
            var player = GameObject.Find("SZ_Player");
            if (player != null)
            {
                player.transform.position = new Vector3(0f, 1.1f, 0f);
                if (player.GetComponent<WandState>() == null) player.AddComponent<WandState>();
            }

            Selection.activeGameObject = player;
            Debug.Log("[SpellyZombie] Wand Tether test built. Press Play, draw to drain, refill at the cauldron.");
        }
    }
}
