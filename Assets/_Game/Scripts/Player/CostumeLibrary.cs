using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// WEAPON SKINS ONLY (legacy costume duty retired — costumes live in the
    /// three SocketWardrobe catalogs: SZ_WardrobePlayer / Zombie / Demon).
    /// Wired by the character wizard from Assets/_Game/Prefabs/Weapons.
    /// NAMING IS THE API: "Weapon_<Key>" — Weapon_Wand, Weapon_Grimoire,
    /// Weapon_SealTablet, Weapon_RuneChamber.
    /// BLENDER PIVOT CONVENTION (all weapons share it): pivot at the GRIP
    /// point, +Z along the weapon away from the holder, +Y up, real meters.
    /// Weapon skins may contain children named after functional parts
    /// (Slide / Strip / Bridge / Plate / Grip / Ink) — those REPLACE the
    /// primitive parts including drawable ink surfaces and moving-part duties.
    public class CostumeLibrary : ScriptableObject
    {
        public List<GameObject> Pieces = new List<GameObject>();

        static CostumeLibrary _loaded;
        static bool _tried;

        public static CostumeLibrary I
        {
            get
            {
                if (_loaded == null && !_tried)
                {
                    _tried = true;
                    _loaded = Resources.Load<CostumeLibrary>("CostumeLibrary");
                }
                return _loaded;
            }
        }
    }
}
