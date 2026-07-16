using UnityEngine;

namespace SpellyZombie
{
    /// MARKO'S OVERRIDE SHELF — the one rule for replacing anything the code
    /// builds: drop a prefab into Assets/_Game/Resources/Custom/ named after
    /// the thing it replaces, and the game uses YOURS from then on. No lists,
    /// no registries, no editor tools — drag into the folder, done. Missing
    /// prefab = the code-built placeholder appears instead, so testing never
    /// blocks on art.
    ///
    /// Names in service today (grep "PrefabVault.Get" for the live hooks):
    ///   Wand     — right-hand pen. Pivot at the grip, +Z out of the fist.
    ///   Grimoire — left-palm book. Pivot at the spine, authored closed.
    ///   Eyes     — googly eye rig. Children named "Eye" (each with a child
    ///              "Pupil") get the full googly behavior; any other shape
    ///              just rides the head as-is.
    ///   Chest    — mystery chest. Optional child "Lid" (pivot on the hinge
    ///              edge, identity rotation = closed) swings open.
    public static class PrefabVault
    {
        public static GameObject Get(string name)
            => Resources.Load<GameObject>("Custom/" + name);

        /// Instantiate Marko's prefab under `parent`, locked to identity —
        /// null when he hasn't made one yet (caller builds its placeholder).
        public static GameObject Spawn(string name, Transform parent)
        {
            var prefab = Get(name);
            return prefab == null ? null : Object.Instantiate(prefab, parent, false);
        }
    }
}
