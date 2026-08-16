using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// PIECE FACTORY for PrefabExporter - watchtowers, sky islands, crate
    /// stacks, gravestones. (All map-BUILDING code was deleted at 's
    /// order - composes maps by hand from the exported prefabs.)
    public static class GameMapBuilder
    {
        static Transform _root;
        static Material _stoneMat;
        static Material _graveMat;

        /// PrefabExporter points the helpers at its temp export root.
        public static void SetRoot(Transform root) => _root = root;

        public static void CrateStack(Vector3 pos)
        {
            var bottom = VillageBuilder.Place("Crate_Wooden", pos, Random.Range(0f, 360f),
                1f, SurfaceMaterialType.Wood);
            if (bottom == null) return;
            float top = VillageBuilder.BoundsOf(bottom).max.y;
            var second = VillageBuilder.Place("Crate_Wooden",
                new Vector3(pos.x + Random.Range(-0.1f, 0.1f), top, pos.z + Random.Range(-0.1f, 0.1f)),
                Random.Range(0f, 360f), 0.9f, SurfaceMaterialType.Wood);
            if (second != null && Random.value < 0.4f)
                VillageBuilder.Place("Crate_Wooden",
                    new Vector3(pos.x, VillageBuilder.BoundsOf(second).max.y, pos.z),
                    Random.Range(0f, 360f), 0.75f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Barrel",
                pos + new Vector3(Random.Range(0.8f, 1.2f), 0f, Random.Range(-0.4f, 0.4f)),
                0f, 1f, SurfaceMaterialType.Wood);
        }

        /// A floating stone island: mossy disc, rocks, grass, a torch beacon,
        /// and a reward (0 = coins, 1 = rare card spot, 2 = weapon pickup).
        public static void SkyIsland(Vector3 at, float r, int reward)
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "SkyIsland";
            disc.transform.SetParent(_root, false);
            disc.transform.position = at;
            disc.transform.localScale = new Vector3(r * 2f, 0.55f, r * 2f);
            Object.DestroyImmediate(disc.GetComponent<CapsuleCollider>());
            disc.AddComponent<MeshCollider>();
            disc.GetComponent<Renderer>().sharedMaterial = StoneMat(new Color(0.42f, 0.52f, 0.30f));
            disc.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Stone;

            float top = at.y + 0.55f;
            VillageBuilder.PlacePT(
                "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Rocks/PT_Generic_Rock_01.prefab",
                new Vector3(at.x + r * 0.5f, top, at.z), Random.Range(0f, 360f), 0.8f, SurfaceMaterialType.Stone);
            for (int i = 0; i < 3; i++)
                VillageBuilder.PlacePT(
                    "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Plants/PT_Grass_02.prefab",
                    new Vector3(at.x + Random.Range(-r * 0.6f, r * 0.6f), top,
                        at.z + Random.Range(-r * 0.6f, r * 0.6f)),
                    Random.Range(0f, 360f), 1f, SurfaceMaterialType.Unknown, keepColliders: false);

            var torch = VillageBuilder.Place("Torch_Metal",
                new Vector3(at.x - r * 0.4f, top, at.z + r * 0.3f), Random.Range(0f, 360f),
                1.1f, SurfaceMaterialType.Metal);
            VillageBuilder.AddWarmLight(torch, 2f, 9f);

            if (reward == 1)
                CardSpot(new Vector3(at.x, top, at.z), true);
            else if (reward == 2)
                VillageBuilder.PlaceWeaponPickup(new Vector3(at.x, top, at.z));
            else
                VillageBuilder.Place("Coin_Pile", new Vector3(at.x, top, at.z),
                    Random.Range(0f, 360f), 1f, SurfaceMaterialType.Gold);
        }

        /// Ramp-climbable two-deck watchtower with a torch and coin on top.
        public static void Watchtower(Vector3 basePos, float yaw)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            for (int cx = -1; cx <= 1; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    var leg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    leg.name = "TowerLeg";
                    leg.transform.SetParent(_root, false);
                    leg.transform.position = basePos + rot * new Vector3(cx * 1.3f, 2.9f, cz * 1.3f);
                    leg.transform.localScale = new Vector3(0.22f, 2.9f, 0.22f);
                    leg.GetComponent<Renderer>().sharedMaterial =
                        MatterFX.Get(new Color(0.4f, 0.28f, 0.17f), MoteShade.Opaque);
                    leg.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;
                }

            foreach (float h in new[] { 2.9f, 5.7f })
            {
                var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
                deck.name = "TowerDeck";
                deck.transform.SetParent(_root, false);
                deck.transform.position = basePos + Vector3.up * h;
                deck.transform.rotation = rot;
                deck.transform.localScale = new Vector3(3.4f, 0.16f, 3.4f);
                deck.GetComponent<Renderer>().sharedMaterial =
                    MatterFX.Get(new Color(0.52f, 0.38f, 0.22f), MoteShade.Opaque);
                deck.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;
            }

            Ramp(basePos + rot * new Vector3(0f, 0f, 4.6f), basePos + rot * new Vector3(0f, 2.9f, 1.4f));
            Ramp(basePos + rot * new Vector3(1.7f, 2.9f, 0f), basePos + rot * new Vector3(-1.7f, 5.7f, 0f));

            var torch = VillageBuilder.Place("Torch_Metal",
                basePos + Vector3.up * 5.78f + rot * new Vector3(1.2f, 0f, 1.2f),
                yaw, 1f, SurfaceMaterialType.Metal);
            VillageBuilder.AddWarmLight(torch, 1.8f, 8f);
            VillageBuilder.Place("Coin_Pile_2", basePos + Vector3.up * 5.78f,
                Random.Range(0f, 360f), 1f, SurfaceMaterialType.Gold);
        }

        static void Ramp(Vector3 from, Vector3 to)
        {
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "TowerRamp";
            ramp.transform.SetParent(_root, false);
            Vector3 delta = to - from;
            ramp.transform.position = (from + to) * 0.5f;
            ramp.transform.rotation = Quaternion.LookRotation(delta.normalized);
            ramp.transform.localScale = new Vector3(1.3f, 0.12f, delta.magnitude);
            ramp.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.5f, 0.36f, 0.2f), MoteShade.Opaque);
            ramp.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;
        }

        public static void Gravestone(Vector3 pos, float yaw)
        {
            if (_graveMat == null)
            {
                var surfaceShader = Shader.Find("SpellyZombie/Surface");
                _graveMat = surfaceShader != null
                    ? new Material(surfaceShader)
                    : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _graveMat.SetColor("_BaseColor", new Color(0.55f, 0.56f, 0.58f));
            }
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Gravestone";
            slab.transform.SetParent(_root, false);
            slab.transform.position = pos + Vector3.up * 0.36f;
            slab.transform.rotation = Quaternion.Euler(Random.Range(-6f, 6f), yaw, Random.Range(-5f, 5f));
            slab.transform.localScale = new Vector3(0.55f, 0.72f, 0.13f);
            slab.GetComponent<Renderer>().sharedMaterial = _graveMat;
            slab.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Stone;
            if (Random.value < 0.3f)
            {
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(arm.GetComponent<Collider>());
                arm.transform.SetParent(slab.transform, false);
                arm.transform.localPosition = new Vector3(0f, 0.72f, 0f);
                arm.transform.localScale = new Vector3(0.7f, 0.18f, 0.6f);
                arm.GetComponent<Renderer>().sharedMaterial = _graveMat;
            }
        }

        static void CardSpot(Vector3 pos, bool rare)
        {
            var pedestal = VillageBuilder.Place("Barrel", pos, Random.Range(0f, 360f),
                1f, SurfaceMaterialType.Wood);
            if (pedestal == null) return;
            pedestal.AddComponent<RuneCardSpawnPoint>().Rare = rare;
        }

        static Material StoneMat(Color c)
        {
            if (_stoneMat != null) return _stoneMat;
            var surfaceShader = Shader.Find("SpellyZombie/Surface");
            _stoneMat = surfaceShader != null
                ? new Material(surfaceShader)
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _stoneMat.SetColor("_BaseColor", c);
            return _stoneMat;
        }
    }
}
