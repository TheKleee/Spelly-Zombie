using UnityEngine;

namespace SpellyZombie
{
    /// Chemistry for PAINTED terrain: paints the ground with terrain
    /// layers; this tells the spell system what each painted patch is made of.
    /// Put it on the Terrain and fill LayerMaterials in the same order as the
    /// terrain's paint layers - e.g. layer 0 grass  Earth, layer 1
    /// cobblestone  Stone, layer 2 dirt path  Earth. A seal drawn on the
    /// ground resolves its surface through the DOMINANT painted layer beneath
    /// it, so State runes conjure stone on the plaza and mud on the meadow.
    [RequireComponent(typeof(Terrain))]
    public class TerrainSurfaceMap : MonoBehaviour
    {
        [Tooltip("One entry per terrain paint layer, same order as on the terrain.")]
        public SurfaceMaterialType[] LayerMaterials = { SurfaceMaterialType.Earth };

        Terrain _terrain;

        public SurfaceMaterialType MaterialAt(Vector3 worldPos)
        {
            if (_terrain == null) _terrain = GetComponent<Terrain>();
            if (_terrain == null || _terrain.terrainData == null || LayerMaterials.Length == 0)
                return SurfaceMaterialType.Earth;

            var data = _terrain.terrainData;
            Vector3 local = worldPos - _terrain.transform.position;
            int mapX = Mathf.Clamp(Mathf.RoundToInt(local.x / data.size.x * (data.alphamapWidth - 1)),
                0, data.alphamapWidth - 1);
            int mapZ = Mathf.Clamp(Mathf.RoundToInt(local.z / data.size.z * (data.alphamapHeight - 1)),
                0, data.alphamapHeight - 1);
            float[,,] weights = data.GetAlphamaps(mapX, mapZ, 1, 1);

            int best = 0;
            float bestWeight = 0f;
            int layers = Mathf.Min(weights.GetLength(2), LayerMaterials.Length);
            for (int i = 0; i < layers; i++)
                if (weights[0, 0, i] > bestWeight) { bestWeight = weights[0, 0, i]; best = i; }
            return LayerMaterials[Mathf.Clamp(best, 0, LayerMaterials.Length - 1)];
        }
    }
}
