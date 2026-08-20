using UnityEngine;

namespace SpellyZombie
{
    /// Workshop hook: a modded map drops this component with its own
    /// ShapeLibrary; the shapes are live while the map is loaded. Empty slots
    /// fall through to the base game; unregisters itself on unload.
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
