using UnityEngine;

namespace SpellyZombie
{
    /// THE AXIOM'S MARKER ("You have no control, I can change
    /// anything freely and easily"). Generators stamp this on everything THEY
    /// create, so a rebuild can delete its own output and never touch what he
    /// placed by hand. Rule for every clear/rebuild pass in the project:
    /// **delete only what is marked, and name what you kept.**
    public class CaveGenerated : MonoBehaviour { }

    /// Drop this on anything you want the tooling to leave completely alone -
    /// the prefab doctor and any future auto-healer skip it.
    public class HandsOff : MonoBehaviour { }

    /// ADOPT, NEVER DICTATE - the helper the axiom audit found would close five
    /// violation classes at once. Code must use the component authored and
    /// only create one when it is genuinely absent.
    public static class Adopt
    {
        /// Returns the existing component if the prefab already carries one,
        /// otherwise adds it. `created` tells the caller whether it is safe to
        /// configure - never overwrite values set in the Inspector.
        public static T Component<T>(GameObject go, out bool created) where T : Component
        {
            var existing = go.GetComponent<T>();
            created = existing == null;
            return created ? go.AddComponent<T>() : existing;
        }

        public static T Component<T>(GameObject go) where T : Component
            => Component<T>(go, out _);

        /// Same, but searches children too (the art often puts the real
        /// component one level down) - used for Light/Collider style lookups.
        public static T InChildren<T>(GameObject go, out bool created) where T : Component
        {
            var existing = go.GetComponentInChildren<T>(true);
            created = existing == null;
            return created ? go.AddComponent<T>() : existing;
        }
    }
}
