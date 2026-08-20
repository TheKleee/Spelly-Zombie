using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Weapon skins only (costumes live in the SocketWardrobe catalogs).
    /// Wired from Assets/_Game/Prefabs/Weapons; naming is the API:
    /// "Weapon_<Key>". Pivot at the GRIP, +Z along the weapon away from the
    /// holder, +Y up, real meters. Children named after functional parts
    /// (Slide / Strip / Bridge / Plate / Grip / Ink) replace the primitives.
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
