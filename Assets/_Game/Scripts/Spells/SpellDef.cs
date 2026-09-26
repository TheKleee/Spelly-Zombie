using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpellyZombie
{
    /// What body a thing wears: a spell is a particle blob, a creature a
    /// zombie or a golem. The creators preview it as this.
    public enum SpellBody { Particle, Zombie, Golem }

    /// ★ HOW A CREATURE LIVES (the Creature Creator). Roams is each body's own
    /// way: a zombie still runs from wands, a golem wanders and fights what it
    /// sees. Rampages is a calamity: random spells from its list all over the
    /// place, never running, untouched by its own side's spells.
    /// Saved as a number, so new ones only ever go at the end.
    public enum CreatureBehaviour { Roams, Hunts, Guards, Skittish, Rampages }

    /// ★ WHICH BOOK A SPELL LIVES IN. Not which TEAM - which GRIMOIRE. The
    /// two come apart on purpose: an acolyte's curse swaps a wizard's book for
    /// an acolyte one for thirty seconds, and in that window the wizard casts
    /// acolyte spells while still being a wizard. So the list is keyed to the
    /// book in your hands, never to your side.
    public enum BookKind { Wizard, Acolyte }

    /// ★ A SPELL. What the player draws, sees and throws, or, when it names a
    /// creature, what a seal of its runes summons.
    ///
    /// It is NOT the area effect it carries - that is an Aoe, authored in its
    /// own window, and this only names one. Two objects, two editors, because
    /// one spell can carry one AOE and an AOE can outlive the spell that
    /// brought it.
    [Serializable]
    public class SpellDef
    {
        public string Name = "New Spell";

        /// The book this spell is in. A wizard holding a wizard book sees
        /// wizard spells; cursed into an acolyte book, they see these instead.
        public BookKind Book = BookKind.Wizard;

        /// The ten, in axis order, IN HUMAN UNITS: degrees, percent, HP, a
        /// count. Whole numbers.
        ///
        /// FOR A PARTICLE these are a threshold: what numbers make it this
        /// spell, and what it hands over. FOR A BODY - a zombie, a golem -
        /// these are what it is BORN AS: its natural state, which it then
        /// drifts from like anything else in the world. A body does not become
        /// itself by crossing a line; a rune summons it, and this is what
        /// stands up.
        public int[] Axis = new int[SpellPayload.AxisCount];

        /// ★ WHICH RUNES A SUMMONING SPELL NEEDS. Particles have no such list -
        /// they are regions, reached however the numbers get there. A summon is
        /// different: Solid raises a melee zombie, Liquid a ranged one, and the
        /// demon answers to every rune at once. Empty for a particle.
        public List<RuneType> Runes = new List<RuneType>();

        /// ★ THE CREATURE A SUMMONING SPELL RAISES (the Creature Creator), by
        /// name. Empty = a spell that is its numbers.
        public string Creature = "";

        public bool IsSummon => !string.IsNullOrEmpty(Creature);

        // Before creatures were their own (Sep 17) a summoning spell carried its
        // creature in these. SpellBook.Repair moves them into a CreatureDef once;
        // nothing else reads them.
        public SpellBody Body = SpellBody.Particle;
        public List<string> Abilities = new List<string>();
        public List<MoveAnim> MoveAnims = new List<MoveAnim>();
        public CreatureBehaviour Behaviour = CreatureBehaviour.Roams;
        public float GuardRange = 10f;
        public bool Boss;

        /// ★ PER AXIS: is this one a biome? A checked axis stops being movable
        /// by biomes or elements and can only be pushed by other spells; drop
        /// it below where it was when it locked and it simply stops being one.
        /// Cannot be true while the value is 0 - there is nothing to lock.
        public bool[] BiomeAxis = new bool[SpellPayload.AxisCount];

        /// The AOE it carries, by name. Empty = none, which is what makes it a
        /// level 1.
        public string Aoe = "";

        /// Which posed blob it wears. Empty = its own name.
        public string Shape = "";

        /// How it moves and breaks up - the state material's own sliders.
        public SpellTable.Look Skin;

        /// ★ THE ONE SELECTIVE RULE, and his: "poison condition is brain not
        /// being 0". A payload reaches everything it touches by definition, so
        /// anything that must CHOOSE its victims has to say so. Nothing else
        /// needs a flag - shoving is Affinity, sticking is Balance, hunting is
        /// Mind.
        public bool OnlyLiving;

        // ---- LEVELS ARE NOT AUTHORED. They are what you get. ---------------
        public bool HasAoe => !string.IsNullOrEmpty(Aoe);
        public bool AnyBiome
        {
            get
            {
                for (int i = 0; i < BiomeAxis.Length; i++)
                    if (BiomeAxis[i] && Axis[i] != 0) return true;
                return false;
            }
        }

        /// 1 hits what it touches · 2 carries an area · 3 is a place.
        /// Nobody types this. It is read off what was authored.
        public int Level => AnyBiome ? 3 : HasAoe ? 2 : 1;

        /// ★ HOW IT CAN DIE. No strength means force cannot touch it at all -
        /// it is not in the physical damage system, so it can ride things
        /// without being destroyed by them, and it can only ever go out by its
        /// own numbers running down. Which also means it dies EMPTY and leaves
        /// nothing behind.
        public bool Physical => Axis[6] != 0;

        /// THE AUTHORED NUMBERS ARE IN UNITS, THE WORLD IS NOT. An author drags
        /// Temperature to 1 meaning one unit of heat, the same size as one unit
        /// of light - but a particle carries temperature in DEGREES, where one
        /// unit is a spark's worth. Scaling here, once, is what lets a slider
        /// mean the same thing on every axis and still land in the right place
        /// in the world. Without it Heat was a twenty-fifth of a unit and read
        /// as nothing at all.
        public SpellPayload Payload
        {
            get
            {
                var p = new SpellPayload();
                for (int i = 0; i < SpellPayload.AxisCount; i++)
                    p[i] = SpellPayload.FromHuman(i, Axis[i]);
                return p;
            }
        }

        /// What a body looks like before any spell shades it - the same
        /// greens the game paints, so a creator and the game agree.
        public static Color BaseSkin(SpellBody b) => b == SpellBody.Zombie
            ? DrawingConfig.SummonMeleeColor
            : new Color(0.55f, 0.55f, 0.5f);   // golem: stone

        public static GameObject BodyPrefab(SpellBody b) => b switch
        {
            SpellBody.Zombie => CollectionManager.ZombieBody,
            SpellBody.Golem => CollectionManager.Golem,
            _ => CollectionManager.ParticleBlob,
        };

        /// The colour a creator's preview wears: a particle is nothing until
        /// its numbers colour it.
        public Color PreviewTint() => Payload.Tint();

        /// ★ ARE THESE NUMBERS THIS SPELL? Every axis it names has to be on the
        /// right side and far enough out. Compared in UNITS, or temperature's
        /// degrees would clear any threshold on sight while light had to earn
        /// it - and then a row's "+1" would mean two different sizes.
        public bool Meets(SpellPayload p)
        {
            if (IsSummon) return false;   // a creature is summoned, never become
            bool said = false;
            for (int i = 0; i < SpellPayload.AxisCount; i++)
            {
                int need = Axis[i];
                if (need == 0) continue;
                // ★ EFFECTS NEVER GATE (his rule): Strength, Mind, Courage
                // and Clones are byproducts a spell CARRIES, never conditions
                // for its creation - a Meteor with Strength 60 authored must
                // not demand strength from the runes.
                if (i >= 6) continue;
                // ★ FLAVOR DOES NOT GATE: an authored value at or under the
                // axis line is something the spell imposes, not a requirement
                // (his flame is slightly gaseous - that must not forbid it).
                if (Mathf.Abs(need) <= SpellPayload.LineFor(i)) continue;
                said = true;
                // compared in HUMAN units, past the axis's own line
                float have = SpellPayload.ToHuman(i, p[i]);
                if (Mathf.Sign(have) != Mathf.Sign(need)) return false;
                float line = Mathf.Max(SpellPayload.LineFor(i), Mathf.Abs(need) * 0.8f);
                if (Mathf.Abs(have) < line) return false;
            }
            if (said) return true;

            // ★ A DEF WHOSE EVERY NAMED AXIS IS FLAVOR still exists - it gates
            // on its own small numbers instead of the line. Without this his
            // Liquid (State -15, inside the line) could never be worn at all,
            // and the bare liquid rune came out a naked blob.
            for (int i = 0; i < 6; i++)
            {
                int need = Axis[i];
                if (need == 0) continue;
                float have = SpellPayload.ToHuman(i, p[i]);
                if (Mathf.Sign(have) != Mathf.Sign(need)) return false;
                if (Mathf.Abs(have) < Mathf.Abs(need) * 0.8f) return false;
                said = true;
            }
            return said;
        }

        /// How strongly it applies - 1 at the threshold, more the further past.
        /// A hotter flame is a bigger flame.
        public float Influence(SpellPayload p)
        {
            float sum = 0f; int n = 0;
            for (int i = 0; i < SpellPayload.AxisCount; i++)
            {
                int need = Axis[i];
                if (need == 0) continue;
                sum += Mathf.Abs(SpellPayload.ToHuman(i, p[i])) / Mathf.Abs(need);
                n++;
            }
            return n == 0 ? 1f : sum / n;
        }
    }

    /// ★ A RUNE. Its glyph lives in the recognizer's library under Id, its
    /// page png in Book Pages under its name; here: what drawing it pushes,
    /// its name and its emoji per side. 1-12 are the built-in twelve, made
    /// runes count on from 13. The Rune Creator edits these; runes only cast,
    /// what the numbers become is the spells' business.
    [Serializable]
    public class RuneDef
    {
        public int Id;
        public string Name = "New Rune";
        public string Emoji = "";
        public string AcolyteEmoji = "";
        /// What drawing it pushes, in human units per axis (degrees, percent, HP).
        public int[] Axis = new int[SpellPayload.AxisCount];

        public RuneType Type => (RuneType)Id;
        public bool BuiltIn => Id >= 1 && Id <= 12;
        public string EmojiFor(bool acolyte) =>
            acolyte && !string.IsNullOrEmpty(AcolyteEmoji) ? AcolyteEmoji : Emoji;
    }

    /// ★ A SHAPE IS DATA. One bone of a saved pose: where it sits, matched by
    /// name onto the blob. No prefab, no asset list - a Workshop spell ships
    /// as JSON and the blob wears it after the cast.
    [Serializable]
    public class BonePose
    {
        public string Bone;
        public Vector3 P, S;
        public Quaternion R;
    }

    /// A saved pose of the blob plus the material sliders it was authored
    /// with. Lives in the book; spells point at it by name.
    [Serializable]
    public class ShapeDef
    {
        public string Name = "";
        public List<BonePose> Bones = new List<BonePose>();
        public SpellTable.Look Look;
    }

    /// A body move's linked animation, by asset GUID - the same law as an
    /// area's prefab: the file stays text and a Workshop package can carry it.
    [Serializable]
    public class MoveAnim
    {
        public string Move = "";
        public string ClipGuid = "";

        [NonSerialized] AnimationClip _clip;
        public AnimationClip Clip
        {
            get
            {
#if UNITY_EDITOR
                if (_clip == null && !string.IsNullOrEmpty(ClipGuid))
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(ClipGuid);
                    if (!string.IsNullOrEmpty(path))
                        _clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                }
#endif
                return _clip;
            }
            set
            {
                _clip = value;
#if UNITY_EDITOR
                ClipGuid = value == null ? "" :
                    UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(value));
#endif
            }
        }
    }

    /// ★ AN AOE. The area effect a spell carries.
    ///
    /// IT HAS NO NUMBERS OF ITS OWN. Everything it does is worked out from the
    /// spell it belongs to - a hot spell has a hot area, and there is no way to
    /// author one that contradicts the other because there is only one set of
    /// numbers in the first place.
    ///
    /// What it owns is where it appears, whether it spreads, and what it LOOKS
    /// like. That is all an AOE is.
    ///
    /// It still outlives its spell: the spell is spent on impact while the area
    /// stays as long as those numbers hold, which is why a fire keeps burning
    /// after the mote that lit it is gone.
    [Serializable]
    public class AoeDef
    {
        public string Name = "New AOE";

        /// Where it appears relative to the spell. Whatever the offset, it
        /// rushes back toward the spell fast - which is the whole of what a
        /// meteor is: an area authored twenty metres up.
        public Vector3 Offset;

        /// Seconds from where it starts to the spell, at a steady speed. 0 = the game's own
        /// pace (the further it starts, the faster it comes). Slower than 5 m/s it settles
        /// in instead of slamming.
        public float ArriveSeconds;

        /// Reappears on nearby things that meet the same condition. Fire and
        /// poison; nothing else needs to know about "contagion".
        public bool Spreading;

        /// ★ AN AREA MAY LOAD A FULL SPELL (his rule): the child BECOMES that
        /// book spell - its axes ride in, its authored shape dresses it, its
        /// behavior follows - on top of the origin slice it already carries.
        /// A meteor area loading a hot solid spell falls as a burning rock.
        public string Spell = "";

        /// ★ ITS LOOK IS ANY PREFAB YOU LIKE - a particle effect, a trail, a
        /// posed blob. Not blob/zombie/golem: those are what SPELLS wear.
        /// A meteor's area is a blob that looks like a falling rock; a flame's
        /// is a fire effect; lightning's is a lightning effect.
        /// A REAL PREFAB, picked like any other slot in Unity. Stored as the
        /// asset's GUID so the file stays text and a Workshop package can
        /// carry it; resolved back to the object on load.
        public string PrefabGuid = "";
        public float TrailWidth, TrailSeconds;
        /// An area made in the game's Map Creator: the name of a look in the
        /// Collection Manager's Area Looks, since it cannot point at an asset.
        public string Look = "";

        [NonSerialized] GameObject _prefab;
        [NonSerialized] bool _warned;

        /// The look is found again on the next read (its Look changed).
        public void ForgetPrefab() { _prefab = null; _warned = false; }

        public GameObject Prefab
        {
            get
            {
                // the authored list resolves in builds and on clients; the GUID only in the editor
                if (_prefab == null) _prefab = CollectionManager.AreaLookFor(Name);
                if (_prefab == null && !string.IsNullOrEmpty(Look)) _prefab = CollectionManager.AreaLookFor(Look);
#if UNITY_EDITOR
                if (_prefab == null && !string.IsNullOrEmpty(PrefabGuid))
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(PrefabGuid);
                    if (!string.IsNullOrEmpty(path))
                        _prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (_prefab != null && Application.isPlaying && !_warned)
                    {
                        _warned = true;
                        Debug.LogWarning($"[SpellyZombie] area '{Name}' look resolves only in the editor: run " +
                                         "Spelly Zombie/Spells/Add Missing Area Looks To Collection Manager in the Lobby, " +
                                         "or builds and clients never see it.");
                    }
                }
#else
                // a build has no GUIDs to resolve: an unlisted look is simply missing, say so once
                if (_prefab == null && !string.IsNullOrEmpty(PrefabGuid) && !_warned)
                {
                    _warned = true;
                    Debug.LogError($"[SpellyZombie] area '{Name}' has a look in the spellbook but none in " +
                                   "CollectionManager > Area Looks: it shows nothing in this build.");
                }
#endif
                return _prefab;
            }
            set
            {
                _prefab = value;
#if UNITY_EDITOR
                PrefabGuid = value == null ? "" :
                    UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(value));
#endif
            }
        }
    }

    /// Everything authored, in one file. The editors write it, the game reads
    /// it, a Workshop package ships it - one format and no export step.
    [Serializable]
    public class SpellBook
    {
        public List<SpellDef> spells = new List<SpellDef>();
        public List<CreatureDef> creatures = new List<CreatureDef>();
        public List<AoeDef> aoes = new List<AoeDef>();
        public List<ShapeDef> shapes = new List<ShapeDef>();
        public List<RuneDef> runes = new List<RuneDef>();

        public const string FileName = "sz_spellbook.json";
        public static string Path_ =>
            System.IO.Path.Combine(Application.persistentDataPath, FileName);

        /// The copy that ships with the game. Every editor save mirrors the
        /// local file here, so a build has the book the creator wrote.
        public static string ShippedPath =>
            System.IO.Path.Combine(Application.streamingAssetsPath, FileName);
        public const string ShippedAssetPath = "Assets/StreamingAssets/" + FileName;

        /// Hash of the shipped copy this file came from, or was mirrored to.
        /// A build adopts the shipped book whenever it differs.
        public string shippedHash = "";

        static SpellBook _loaded;
        public static SpellBook Live
        {
            get
            {
                if (_loaded != null) return _loaded;
                _loaded = Load();
                return _loaded;
            }
        }

        public static void Forget() { _loaded = null; }

        /// True while this very book is the live one: nothing adopted or saved over it since.
        public static bool IsLive(SpellBook b) => b != null && _loaded == b;

        public static SpellBook Load()
        {
            AdoptShipped();
            try
            {
                if (File.Exists(Path_))
                {
                    var b = JsonUtility.FromJson<SpellBook>(File.ReadAllText(Path_));
                    if (b != null)
                    {
                        b.Repair();
                        return b;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpellyZombie] spellbook unreadable: {ex.Message}");
            }
            return new SpellBook();
        }

        static string HashOf(string text)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in text) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16");
        }

        /// A fresh machine, and a build on every update, takes the shipped
        /// book over the local file. The editor's local file is the master.
        static void AdoptShipped()
        {
            try
            {
                if (Application.isEditor && File.Exists(Path_)) return;
                if (!File.Exists(ShippedPath)) return;
                string text = File.ReadAllText(ShippedPath);
                string hash = HashOf(text);
                if (File.Exists(Path_))
                {
                    var local = JsonUtility.FromJson<SpellBook>(File.ReadAllText(Path_));
                    if (local != null && local.shippedHash == hash) return;
                }
                var shipped = JsonUtility.FromJson<SpellBook>(text);
                if (shipped == null) return;
                shipped.shippedHash = hash;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_));
                File.WriteAllText(Path_, JsonUtility.ToJson(shipped, true));
                Debug.Log($"[SpellyZombie] adopted the shipped spellbook: {shipped.spells.Count} spells.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpellyZombie] shipped spellbook unreadable: {ex.Message}");
            }
        }

        public void Save()
        {
#if UNITY_EDITOR
            MirrorShipped();
#endif
            File.WriteAllText(Path_, JsonUtility.ToJson(this, true));
            _loaded = this;
            NetSync.PushBook();   // hosting mid-edit: everyone gets the new book
        }

#if UNITY_EDITOR
        /// Writes the shipped copy (hash field blank) and stamps its hash on
        /// this book, so the local file written after it knows its origin.
        public void MirrorShipped()
        {
            try
            {
                shippedHash = "";
                string text = JsonUtility.ToJson(this, true);
                shippedHash = HashOf(text);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShippedPath));
                if (File.Exists(ShippedPath) && File.ReadAllText(ShippedPath) == text) return;
                File.WriteAllText(ShippedPath, text);
                UnityEditor.AssetDatabase.ImportAsset(ShippedAssetPath);
                Debug.Log($"[SpellyZombie] shipped spellbook updated: {ShippedAssetPath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpellyZombie] could not mirror the spellbook: {ex.Message}");
            }
        }

        /// The local file mirrors itself on editor load, so a book saved
        /// before mirroring existed still ships.
        [UnityEditor.InitializeOnLoadMethod]
        static void MirrorOnLoad() => UnityEditor.EditorApplication.delayCall += () =>
        {
            try
            {
                if (!File.Exists(Path_)) return;
                var local = JsonUtility.FromJson<SpellBook>(File.ReadAllText(Path_));
                if (local == null) return;
                string stamp = local.shippedHash;
                local.shippedHash = "";
                string text = JsonUtility.ToJson(local, true);
                if (stamp == HashOf(text) && File.Exists(ShippedPath)) return;
                local.MirrorShipped();
                File.WriteAllText(Path_, JsonUtility.ToJson(local, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpellyZombie] spellbook mirror on load failed: {ex.Message}");
            }
        };
#endif

        /// The live book as JSON - what the wire carries.
        public static string LiveJson() => JsonUtility.ToJson(Live);

        /// ★ ADOPT THE HOST'S BOOK (his law: one book for everyone). In
        /// memory only - the local authored file on disk stays untouched.
        public static void Adopt(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var b = JsonUtility.FromJson<SpellBook>(json);
                if (b == null) return;
                b.Repair();
                _loaded = b;
                Debug.Log($"[SpellyZombie] adopted the host's spellbook: {b.spells.Count} " +
                          $"spells, {b.creatures.Count} creatures, {b.aoes.Count} areas, {b.shapes.Count} shapes.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpellyZombie] host book unreadable: {ex.Message}");
            }
        }

        /// JsonUtility gives back arrays of whatever length it found, so a file
        /// written before an axis existed would come back short and index out
        /// of range on the first read.
        public void Repair()
        {
            if (shapes == null) shapes = new List<ShapeDef>();
            if (creatures == null) creatures = new List<CreatureDef>();
            MoveBodiesOut();
            foreach (var c in creatures) c.Repair();
            foreach (var s in spells)
            {
                s.Axis = Fit(s.Axis);
                if (s.Runes == null) s.Runes = new List<RuneType>();
                if (s.Creature == null) s.Creature = "";
                if (s.BiomeAxis == null || s.BiomeAxis.Length != SpellPayload.AxisCount)
                {
                    var b = new bool[SpellPayload.AxisCount];
                    if (s.BiomeAxis != null)
                        for (int i = 0; i < Mathf.Min(b.Length, s.BiomeAxis.Length); i++) b[i] = s.BiomeAxis[i];
                    s.BiomeAxis = b;
                }
            }
            EnsureRunes();
        }

        /// ★ BODIES BECAME CREATURES (Sep 17). A book from before carries each
        /// creature inside the spell that summoned it; it moves out once under
        /// the spell's name, and the spell summons it. A body no rune summoned
        /// was only ever a creature, so its spell goes.
        void MoveBodiesOut()
        {
            for (int i = spells.Count - 1; i >= 0; i--)
            {
                var s = spells[i];
                if (s == null || s.Body == SpellBody.Particle) continue;
                if (Creature(s.Name) == null)
                    creatures.Add(new CreatureDef
                    {
                        Name = s.Name, Body = s.Body, Axis = s.Axis,
                        Abilities = s.Abilities ?? new List<string>(),
                        MoveAnims = s.MoveAnims ?? new List<MoveAnim>(),
                        Aoe = s.Aoe ?? "", Skin = s.Skin,
                        Behaviour = s.Behaviour, GuardRange = s.GuardRange, Boss = s.Boss,
                    });
                if (s.Runes == null || s.Runes.Count == 0) { spells.RemoveAt(i); continue; }
                s.Creature = s.Name;
                s.Body = SpellBody.Particle;
                s.Axis = new int[SpellPayload.AxisCount];
                s.BiomeAxis = new bool[SpellPayload.AxisCount];
                s.Abilities = new List<string>();
                s.MoveAnims = new List<MoveAnim>();
                s.Aoe = "";
                s.Shape = "";
                s.Skin = null;
                s.Behaviour = CreatureBehaviour.Roams;
                s.GuardRange = 10f;
                s.Boss = false;
            }
        }

        /// The rune with this id, or null.
        public RuneDef Rune(RuneType t)
        {
            if (runes == null) return null;
            foreach (var r in runes) if (r.Id == (int)t) return r;
            return null;
        }

        /// ★ THE BOOK IS THE SOURCE (his model): a recognized rune seeds
        /// exactly what its def pushes, so rebalancing the book rebalances the
        /// runes. No def = the built-in push, 25 flat on its axis.
        public SpellPayload SeedOf(RuneType rune)
        {
            var seed = new SpellPayload();
            var def = Rune(rune);
            if (def != null)
            {
                for (int i = 0; i < SpellPayload.AxisCount; i++)
                    if (def.Axis[i] != 0) seed[i] = SpellPayload.FromHuman(i, def.Axis[i]);
                return seed;
            }
            var axes = SpellPayload.Of(rune, 1f);
            for (int i = 0; i < SpellPayload.AxisCount; i++)
                if (Mathf.Abs(axes[i]) > 0.001f)
                    seed[i] = SpellPayload.FromHuman(i, 25f * Mathf.Sign(axes[i]));
            return seed;
        }

        /// ★ THE TWELVE ALWAYS EXIST. A book from before runes were data gets
        /// them here: the push copied from the bare spell row each seeded by
        /// name (else the built-in), the emoji the built-in ones.
        public void EnsureRunes()
        {
            if (runes == null) runes = new List<RuneDef>();
            for (int id = 1; id <= 12; id++)
            {
                var t = (RuneType)id;
                if (Rune(t) != null) continue;
                var def = new RuneDef
                {
                    Id = id, Name = LegacyBareName(t),
                    Emoji = RuneLibrary.BuiltInIcon(t), AcolyteEmoji = RuneLibrary.BuiltInAcolyteIcon(t),
                };
                var bare = Spell(LegacyBareName(t));
                if (bare != null && !bare.IsSummon)
                    for (int i = 0; i < SpellPayload.AxisCount; i++) def.Axis[i] = bare.Axis[i];
                else
                {
                    var axes = SpellPayload.Of(t, 1f);
                    for (int i = 0; i < SpellPayload.AxisCount; i++)
                        if (Mathf.Abs(axes[i]) > 0.001f) def.Axis[i] = Mathf.RoundToInt(25f * Mathf.Sign(axes[i]));
                }
                runes.Add(def);
            }
            foreach (var r in runes) r.Axis = Fit(r.Axis);
        }

        /// A made rune: the next free id past the twelve.
        public RuneDef NewRune()
        {
            int id = 13;
            foreach (var r in runes) id = Mathf.Max(id, r.Id + 1);
            var def = new RuneDef { Id = id };
            runes.Add(def);
            return def;
        }

        /// The names the bare spells carried before runes were data.
        public static string LegacyBareName(RuneType r) => r switch
        {
            RuneType.HeatUp => "Heat", RuneType.HeatDown => "Chill",
            RuneType.LuminanceUp => "Light", RuneType.LuminanceDown => "Dark",
            RuneType.DensityUp => "Compress", RuneType.DensityDown => "Spread",
            RuneType.StickyUp => "Sticky", RuneType.StickyDown => "Slick",
            RuneType.StateSolid => "Solid", RuneType.StateLiquid => "Liquid",
            RuneType.Attract => "Attract", RuneType.Repel => "Repel",
            _ => r.ToString(),
        };

        internal static int[] Fit(int[] src)
        {
            if (src != null && src.Length == SpellPayload.AxisCount) return src;
            var f = new int[SpellPayload.AxisCount];
            if (src != null)
                for (int i = 0; i < Mathf.Min(f.Length, src.Length); i++) f[i] = src[i];
            return f;
        }

        public AoeDef Aoe(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var a in aoes) if (a.Name == name) return a;
            return null;
        }

        /// A def's place in the shipped book - the same file on every machine,
        /// so a byte names a spell over the wire. 255 = none.
        public byte IndexOf(SpellDef def)
        {
            if (def == null) return 255;
            int i = spells.IndexOf(def);
            return i >= 0 && i < 255 ? (byte)i : (byte)255;
        }

        public SpellDef At(byte index) =>
            index < spells.Count ? spells[index] : null;

        /// A creature's place in the book, the same way: a byte over the wire. 255 = none.
        public byte CreatureIndex(CreatureDef creature)
        {
            if (creature == null) return 255;
            int i = creatures.IndexOf(creature);
            return i >= 0 && i < 255 ? (byte)i : (byte)255;
        }

        public CreatureDef CreatureAt(byte index) =>
            index < creatures.Count ? creatures[index] : null;

        public CreatureDef Creature(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var c in creatures) if (c.Name == name) return c;
            return null;
        }

        public ShapeDef Shape(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var s in shapes)
                if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        /// ★ EVERY SPELL THESE NUMBERS ARE - not one winner. A particle that
        /// satisfies both flame and lightning IS both, because neither is a
        /// thing it turned into: each is an area riding on it, so it wears both.
        public static void All(SpellPayload p, List<SpellDef> into)
        {
            into.Clear();
            var book = Live;
            for (int i = 0; i < book.spells.Count; i++)
                if (MapSpells.Allows(book.spells[i]) && book.spells[i].Meets(p))
                    into.Add(book.spells[i]);
        }

        /// ★ ONLY THE SPELLS IN THIS BOOK. The numbers are the numbers, but a
        /// wizard's heat does not become an acolyte's decoy - the region has
        /// to be in the book they are holding.
        public static void All(SpellPayload p, BookKind book, List<SpellDef> into)
        {
            into.Clear();
            var b = Live;
            for (int i = 0; i < b.spells.Count; i++)
                if (b.spells[i].Book == book && MapSpells.Allows(b.spells[i])
                    && b.spells[i].Meets(p)) into.Add(b.spells[i]);
        }

        /// ★ WHICH SUMMONING SPELL THIS SEAL CASTS, counting runes. One that
        /// asks for two Liquids needs two in the seal; one that asks for one is
        /// satisfied by one or more. The most demanding match wins, so a seal
        /// with two Liquids raises the bigger zombie rather than two small
        /// ones - and a demon, asking for all twelve, needs all twelve.
        public SpellDef BodyForSeal(List<RuneType> sealRunes, BookKind book)
        {
            SpellDef best = null; int bestNeed = 0;
            foreach (var sp in spells)
            {
                if (!sp.IsSummon || sp.Runes.Count == 0 || sp.Book != book) continue;
                if (!MapSpells.Allows(sp)) continue;
                if (!Covers(sealRunes, sp.Runes)) continue;
                if (sp.Runes.Count > bestNeed) { best = sp; bestNeed = sp.Runes.Count; }
            }
            return best;
        }

        static bool Covers(List<RuneType> have, List<RuneType> need)
        {
            var pool = new Dictionary<RuneType, int>();
            foreach (var r in have) pool[r] = pool.TryGetValue(r, out var n) ? n + 1 : 1;
            foreach (var r in need)
            {
                if (!pool.TryGetValue(r, out var n) || n <= 0) return false;
                pool[r] = n - 1;
            }
            return true;
        }

        public SpellDef Spell(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var s in spells) if (s.Name == name) return s;
            return null;
        }
    }
}
