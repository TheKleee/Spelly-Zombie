using UnityEngine;

namespace SpellyZombie
{
    /// Maps painted terrain layers to surface materials. Fill LayerMaterials
    /// in the same order as the terrain's paint layers; a point resolves to
    /// the dominant painted layer beneath it.
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
