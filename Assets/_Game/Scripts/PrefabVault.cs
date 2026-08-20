using UnityEngine;

namespace SpellyZombie
{
    /// Prefab overrides: drop a prefab into Assets/_Game/Resources/Custom/
    /// named after the thing it replaces and the game uses it. A missing
    /// prefab falls back to the code-built placeholder.
    ///
    /// Hook names:
    ///
    /// CHARACTERS &amp; PROPS
    ///   PlayerBody   - your wizard model. Same Mixamo skeleton = everything works.
    ///   ZombieBody   - the undead model. Needs a head bone (humanoid Head, or a
    ///                  bone named/ending in "Head") or eyes + hats have no mount.
    ///                  A "HeadTop" bone lets it auto-fit the capsule; without one
    ///                  YOUR authored scale is kept as-is.
    ///   Wand         - right-hand pen. Pivot at the grip, +Z out of the fist.
    ///   Grimoire     - left-palm book. Pivot at the spine, authored closed.
    ///                  Optional child "PageAnchor" on the open spread marks where
    ///                  page art sits (its SCALE sets the spread size).
    ///   Eyes         — (or "GooglyEyes") googly rig. Children named "Eye", each
    ///                  with a child "Pupil", get the full googly behaviour.
    ///   Chest        — mystery chest. Optional child "Lid" or "*_Lid"/"*_Cover"
    ///                  (pivot on the hinge edge, authored CLOSED) swings open.
    ///   InkRuneStone - the fuel ore.        Cauldron - the ink pot (planned hook).
    ///   SpellPage / GrimoirePage_&lt;card&gt; - page art per rune card.
    ///
    /// SPELL VISUALS  (code keeps the physics root; your prefab is the LOOK)
    ///   FX_&lt;ParticleKind&gt;  - Spark, Frost, Light, Dark, Glue, Repel, Dense,
    ///                         Spread, Push, Flame, Lightning, Laser, Shadow,
    ///                         BlackHole, BarrierMote.
    ///   FX_&lt;FieldClass&gt;    - PoisonField. Author it ~1 unit across - it is
    ///                         scaled to the field.
    ///   FX_StateBlob       - the matter soft body. To react to solid/liquid/gas
    ///                         give it ONE of: an Animator float "StateT"
    ///                         (1 = solid, 0.5 = liquid, 0.1 = gas - plus an
    ///                         optional bool "Muddy"), a material property
    ///                         "_StateT", or a script with OnStateT(float).
    ///                         Without one of those it just sits there - and the
    ///                         console will tell you so.
    ///
    /// SOLID SHAPES are NOT named here - they live in the ShapeLibrary asset
    /// (Resources/ShapeLibrary.asset): a prefab slot per material per seal
    /// line count, filled in the Inspector whenever you like.
    ///
    /// BODY-FX SOCKETS (not via this class - BodyState reads them off any rig):
    ///   Empties named Socket_Burn / Socket_Freeze / Socket_Bleed / Socket_Eyes
    ///   placed on the BONES of a character (several per kind is fine -
    ///   Socket_Burn on the chest, Socket_Burn2 on a shoulder; any suffix
    ///   works). Burn flames, frost crystals, blood drips and blind/glare eye
    ///   FX spawn as children there, riding animation and ragdoll. With no
    ///   authored sockets, code guesses spots on the humanoid bones.
    ///
    /// COSTUME (not via this class - see SocketWardrobe / CostumeLibrary):
    ///   Z&lt;Socket&gt;_*  prefabs, and the SZ_Wardrobe* ScriptableObjects.
    /// ALSO IN Resources/Custom but NOT PrefabVault: Cursor, Music_Chill,
    ///   Music_Action, Loc_&lt;lang&gt;.
    ///
    /// A prefab whose name matches nothing loads nothing; an editor audit
    /// warns when one is dropped in.
    public static class PrefabVault
    {
        public static GameObject Get(string name)
            => Resources.Load<GameObject>("Custom/" + name);

        /// Instantiate the prefab under `parent`, locked to identity;
        /// null when no prefab exists (caller builds its placeholder).
        public static GameObject Spawn(string name, Transform parent)
        {
            var prefab = Get(name);
            return prefab == null ? null : Object.Instantiate(prefab, parent, false);
        }
    }
}
