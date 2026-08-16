using UnityEngine;

namespace SpellyZombie
{
    /// THE WORKSHOP HOOK ("make sure it's mod-able so people can make
    /// their custom stuff in modded maps - via Steam Workshop").
    ///
    /// A map maker drops this component anywhere in their scene, hooks up
    /// their own ShapeLibrary asset, and their shapes are live for as long as
    /// that map is loaded. Nothing else to wire, no code to write, no name to
    /// get right - the map itself carries its content.
    ///
    /// Slots they leave EMPTY fall through to the base game, so a mod that
    /// only makes a new wooden wheel gets every other shape for free. Two
    /// mods loaded at once stack in load order, last one winning ties.
    ///
    /// The shelf unregisters itself when the map unloads, so leaving a map
    /// cleanly takes its shapes with it.
    public class ModShapes : MonoBehaviour
    {
        [Tooltip("Your own Shape Library asset (Create ▸ Spelly Zombie ▸ Shape Library). " +
                 "Fill only the slots you made. Everything else falls back to the base game.")]
        public ShapeLibrary Shapes;

        void OnEnable()
        {
            if (Shapes == null)
            {
                Debug.LogWarning("[SpellyZombie] ModShapes has no Shape Library assigned. " +
                    "This map adds no shapes.", this);
                return;
            }
            ShapeLibrary.Push(Shapes);
        }

        void OnDisable()
        {
            if (Shapes != null) ShapeLibrary.Pop(Shapes);
        }
    }
}
