using UnityEngine;

namespace SpellyZombie
{
    /// The object's claim: put this on a prefab root and size the box
    /// around the whole thing. The spawner refuses fields too tight for
    /// it and keeps every other spawn out of the box - nothing ever clips
    /// through a house because the house said how big it is.
    ///
    /// Entries: add PathPoint children where the path network must reach
    /// this object - a door (several are fine), a monument's viewing spot.
    /// The marker may sit anywhere (even a window); the path itself only
    /// ever runs on the ground beneath it.
    ///
    /// Paths never cut THROUGH the box: the network routes around it and
    /// touches it only at its PathPoint entries - the object behaves like
    /// a tiny biome of its own, adjacent to the one it stands in.
    ///
    /// Bare models without this box still work: the spawner measures their
    /// renderers instead. An authored box always wins over measuring.
    public class ObjectBox : MonoBehaviour
    {
        [Tooltip("The claimed space in local metres, centered on Center. Box bottom = the ground line.")]
        public Vector3 Size = new Vector3(2f, 2f, 2f);
        [Tooltip("Local offset of the claim, for prefabs whose pivot is not at the middle of their footprint.")]
        public Vector3 Center = new Vector3(0f, 1f, 0f);

        public Bounds LocalArea => new Bounds(Center, Size);

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.6f, 0.15f, 0.9f);
            Gizmos.DrawWireCube(Center, Size);
        }
    }
}
