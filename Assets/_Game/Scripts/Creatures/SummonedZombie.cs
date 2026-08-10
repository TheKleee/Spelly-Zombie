using UnityEngine;

namespace SpellyZombie
{
    /// A ZOMBIE THAT CAME OUT OF A SEAL. Marko's rule: zombies are SPELLS, so they
    /// belong to whoever cast them and they expire.
    ///
    /// Kept as its own component rather than fields on Zombie, so the ordinary
    /// world zombies of the co-op mode are completely unaffected: no owner, no
    /// clock, nothing new to think about.
    ///
    /// The owner matters later and matters a lot: when one of these kills a
    /// player, the soul binds to the ACOLYTE WHO SUMMONED IT, not to the zombie,
    /// because a spell belongs to its caster.
    public class SummonedZombie : MonoBehaviour
    {
        /// Grimoire owner id of the acolyte who drew the seal.
        public int SummonedBy = -1;

        /// Melee zombies got the Solid rune, ranged ones got Liquid.
        public bool Ranged;

        float _left, _paintRetry;
        bool _painted;
        static MaterialPropertyBlock _block;

        public void Begin(int owner, bool ranged, float seconds)
        {
            SummonedBy = owner;
            Ranged = ranged;
            _left = seconds;
            Paint();
        }

        /// Corrupt ink makes corrupt bodies. Melee is a brownish green, ranged a
        /// light green, so you can tell across a courtyard which one is walking at
        /// you without any icon.
        ///
        /// Through MatterFX.Get, which is exactly how Zombie.Spawn colours a
        /// zombie in the first place. A property block did NOT work here: these
        /// bodies use MatterFX's shared materials and the tint never showed, so
        /// the melee ones stayed brown. MatterFX caches one material per colour,
        /// so this stays batched for a crowd.
        ///
        /// HIS PREFAB IS NEVER RECOLOURED. If a custom ZombieBody is dressing
        /// this zombie, his materials stand, per his own axiom in ZombieDress.
        void Paint()
        {
            var rends = GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return;      // body not built yet; Update retries

            Color c = Ranged ? DrawingConfig.SummonRangedColor : DrawingConfig.SummonMeleeColor;

            // HIS BAKED BODY GETS A PROPERTY BLOCK, not a new material: his
            // shader and his maps survive, only the colour changes. The graybox
            // gets a MatterFX material, which is how it was coloured to begin with.
            var dress = GetComponent<ZombieDress>();
            bool his = dress != null && dress.IsCustomBody;

            foreach (var r in rends)
            {
                if (r == null) continue;
                bool head = r.transform.name == "Head";
                Color mine = head ? c * 1.15f : c;

                if (his)
                {
                    if (_block == null) _block = new MaterialPropertyBlock();
                    r.GetPropertyBlock(_block);
                    _block.SetColor("_BaseColor", mine);
                    _block.SetColor("_Color", mine);
                    r.SetPropertyBlock(_block);
                }
                else r.sharedMaterial = MatterFX.Get(mine, MoteShade.Opaque);
            }
            _painted = true;
        }

        void Update()
        {
            // the rig may finish building a frame or two after we spawned
            if (!_painted && (_paintRetry -= Time.deltaTime) <= 0f)
            {
                _paintRetry = 0.2f;
                Paint();
            }

            _left -= Time.deltaTime;
            if (_left > 0f) return;

            // no corpse, no drops: it was never really alive, it was a spell
            if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, transform.position);
            Destroy(gameObject);
        }
    }
}
