using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Tiny marker components the map graybox carries so later systems plug in
    /// without touching geometry: spell resolution reads SurfaceMaterialTag,
    /// the wave spawner reads ZombieEntryPoint, pickups read spawn points, the
    /// zone simulation reads ZoneVolume, and water kills ink via WaterSurface.

    // Unknown is the fallback for anything not marked. Everything after Unknown
    // is a spell-made tier reached by transmutation (Density-up compresses a
    // material into its stronger form: WoodCoalDiamond, EarthStoneDiamond,
    // MetalGold, FleshBone, WaterSlime). Appended so serialized tags keep
    // their values.
    public enum SurfaceMaterialType { Stone, Wood, Earth, Metal, Water, Flesh, Unknown, Coal, Diamond, Gold, Bone, Slime }

    /// The "ingredient" of the spell system: what this surface is made of.
    public class SurfaceMaterialTag : MonoBehaviour
    {
        public SurfaceMaterialType Material = SurfaceMaterialType.Stone;
    }

    public enum CauldronType { Survival, Drawing, Spell, Weapon }

    /// Fixed-location perk cauldron. One perk per type per player, lost on death.
    public class CauldronMarker : MonoBehaviour
    {
        public CauldronType Type;
    }

    /// Where zombie waves come from (windows, alley gaps, cave mouths).
    public class ZombieEntryPoint : MonoBehaviour
    {
        /// Live registry - spares the spawner a FindObjectsByType per spawn tick (Zombie.All pattern).
        public static readonly List<ZombieEntryPoint> All = new List<ZombieEntryPoint>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
    }

    /// Where a rune card can appear this run. Rare spots live in the dungeon.
    public class RuneCardSpawnPoint : MonoBehaviour
    {
        public bool Rare;
    }

    /// One of the possible mystery chest locations this run.
    public class MysteryChestSpawnPoint : MonoBehaviour
    {
    }

    /// Environment zone bounds + baseline conditions for the zone simulation.
    [RequireComponent(typeof(BoxCollider))]
    public class ZoneVolume : MonoBehaviour
    {
        public string ZoneName = "Zone";
        public float BaselineTemperature = 18f;
    }

    /// Ink cannot exist here - the pen refuses the surface, and (later) seals
    /// touching water are destroyed. The fountain is the classic seal-killer.
    public class WaterSurface : MonoBehaviour
    {
    }
}
