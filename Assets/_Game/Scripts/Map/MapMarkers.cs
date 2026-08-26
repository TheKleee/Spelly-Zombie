using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Marker components the map carries for later systems: spell resolution
    /// reads SurfaceMaterialTag, spawners read the spawn points, the zone
    /// simulation reads ZoneVolume, and water kills ink via WaterSurface.

    // Unknown = fallback for unmarked surfaces. Entries after it are
    // transmutation tiers, appended so serialized tags keep their values.
    public enum SurfaceMaterialType { Stone, Wood, Earth, Metal, Water, Flesh, Unknown, Coal, Diamond, Gold, Bone, Slime }


    public enum CauldronType { Survival, Drawing, Spell, Weapon }

    /// Fixed-location perk cauldron. One perk per type per player, lost on death.
    public class CauldronMarker : MonoBehaviour
    {
        public CauldronType Type;
    }

    /// Where zombie waves come from (windows, alley gaps, cave mouths).
    public class ZombieEntryPoint : MonoBehaviour
    {
        public static readonly List<ZombieEntryPoint> All = new List<ZombieEntryPoint>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
    }

    /// Where a rune card can appear this run. Rare spots live in the dungeon.
    public class RuneCardSpawnPoint : MonoBehaviour
    {
        public bool Rare;
    }

    /// Environment zone bounds + baseline conditions for the zone simulation.
    [RequireComponent(typeof(BoxCollider))]
    public class ZoneVolume : MonoBehaviour
    {
        public string ZoneName = "Zone";
        public float BaselineTemperature = 18f;
    }

    /// Ink cannot exist here - the pen refuses the surface.
    public class WaterSurface : MonoBehaviour
    {
    }
}
