using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;

namespace SpellyZombie
{
    /// Broadcast-based sync: live avatars + replicated world strokes (each client detects/casts), plus host-authoritative zombies/rounds.
    public class NetSync : MonoBehaviour
    {
        // ------------------------------------------------------------ types --
        public struct PlayerState : IBroadcast
        {
            public int Id;
            public Vector3 Pos;
            public float Yaw;
            public float Pitch; // look pitch (+down) - remote heads follow it
            // 1 downed, 2 sprawled, 4 GHOST is flying, 8 pen down, 16 crouched,
            // 32 sprinting, 64 airborne, 128 frozen cube, 256 wandless, 512 hands full,
            // 1024 posed still (paint easel, studio, pose mode), 2048 pen shown
            public ushort Flags;
            public byte Team;  // MatchLobby team color index
            // ghost position; the corpse stays at Pos so friends can revive it
            public Vector3 GhostPos;
            public float GhostYaw;
            public bool Acolyte; // ghost tint: green acolyte / wizard ink
            public Vector2 Move;  // planar velocity in body space, m/s (x right, y forward) - drives the puppet's legs
            public byte Book;     // bit 7 = grimoire open, low bits = page index
            public byte Ink;      // wand fraction 0..255
            public sbyte Emote;   // -1 none, else the EmoteLibrary slot held
            public byte HeldKind; // 0 none, 1 tracked prop, 2 matter, 3 mote
            public int Held;      // the host-side id of what the hands carry
            public byte Hp;       // strength fraction 0..255: what this body's health lets it lift and throw
            public ushort Health10; // their health in tenths: the host's stand-in follows their own mending
            public byte Eyes;     // their googly eyes as EyeBits: mood, red pupils, the wide moment
            public Vector3 Gaze;  // where their pupils point: the pen tip while drawing, what scared them
        }

        public struct EmoteMsg : IBroadcast // owner, host relays to all: the pose a slot plays, whole
        {
            public int Owner;
            public sbyte Slot;
            public string Json; // EmoteDef as JsonUtility text
        }

        /// host -> all: a poison field opened here. 0 = an acolyte died
        /// (cloud + blast), 1 = an acolyte left its disguise, 2 = a summon's
        /// living aura riding zombie RideOn, 3 = a zombie's detonation or
        /// death cloud. Clients open a mirror: it bills only their own body.
        public struct FieldMsg : IBroadcast
        {
            public byte Kind;
            public Vector3 At;
            public float Radius;
            public float Seconds;
            public int RideOn; // the host's zombie id the aura rides, 0 = free
        }

        public struct ScanMsg : IBroadcast // owner, host relays to all: an acolyte scanned this prop
        {
            public int Owner;   // -1 = the host replaying old scans to a joiner
            public string Path; // ScenePath of the prop
        }

        /// any -> host -> all: a sound or ground puff a body made. Kinds: 0 thud,
        /// 2 whoosh, 3 pop, 5 thaw puff, 6 mend puff, 7 slip puff, 8 glue puff,
        /// 9 bounce puff, 4 chime (blink, ghost possession); 1 sting rides the presence edge instead.
        public struct BodyFxMsg : IBroadcast
        {
            public int Owner;
            public byte Kind;
            public Vector3 At;
        }

        public struct KickMsg : IBroadcast // host -> the kicked player: a spell's kick lands on their own body
        {
            public int Owner;
            public Vector3 Impulse; // Kind 1: an acceleration instead
            public bool Knock;
            public byte Kind;       // 0 impulse, 1 held acceleration for Seconds
            public float Seconds;
            public float Lum;       // Kind 1: a body-light push, once
            public bool Blast;      // Kind 0: a radial blast - lands through Shove.Hit, so it breaks modes
        }

        public struct CurseMsg : IBroadcast // host -> all: an acolyte's mischief on a player (every machine keeps the book, the owner runs the rest)
        {
            public int Owner;
            public byte Kind;   // MischiefKind
            public float Seconds; // or an ink amount, for Evaporation
            public int By;       // the acolyte who cast it
            public string Shape; // Transformation: the address of the object to become
        }

        public struct WornMsg : IBroadcast // host -> clients: a zombie (Kind 1, by snapshot id) or a prop (Kind 2, by path) wears another object's look
        {
            public byte Kind;
            public int Id;
            public string Path;
            public string Shape;
            public float Seconds;
        }

        /// host -> clients: an effect or a sound the host's sim made. Kind is
        /// a FxLibrary wire id (prefab index, code-built look, or sound).
        public struct FxMsg : IBroadcast
        {
            public byte Kind;
            public Vector3 At;
            public Vector3 Aux;   // prefab: final localScale; bloom: upKick.x; bolt: the far end
            public float Scale;   // sound: volume; bloom/cone: speed; lantern: intensity
            public Color32 Tint;
            public int Parent;    // Element.NetId it rides, 0 = the world
            public float Life;    // seconds; sound: pitch; puff/bloom/cone: count
            public byte Flags;    // 1 tinted
        }

        public struct ReviveTickMsg : IBroadcast // rescuer, host, then the downed owner: a living friend stands at their body
        {
            public int Rescuer;
            public int Target;
            public float Dt;
        }

        public struct ReviveDoneMsg : IBroadcast // the revived owner, host, then the rescuer
        {
            public int Rescuer;
        }

        public struct RideAskMsg : IBroadcast // client to host: my ghost takes this creature (On) or lets go
        {
            public int Owner;
            public byte Kind; // 1 zombie, 2 golem
            public int Id;    // the host's instance id, the one the snapshots carry
            public bool On;
        }

        public struct RideGiveMsg : IBroadcast // host to all: the verdict, or a forced release
        {
            public int Owner;
            public byte Kind;
            public int Id;
            public bool On;
        }

        public struct RideDriveMsg : IBroadcast // rider to host, unreliable: this frame's reins
        {
            public int Owner;
            public int Id;
            public Vector3 Move;
            public Vector3 Look;
            public bool Ability;
        }

        public struct ReadyMsg : IBroadcast // client  host: lobby ready toggle
        {
            public bool Ready;
        }

        public struct LobbyMsg : IBroadcast // host  clients: lobby state
        {
            public byte Ready;
            public byte Total;
            public float Countdown;
            public string Map; // the host's pick - everyone sees it
            public int Seed;
            public byte AcolytePct;
            public byte DurationMin;
        }

        public struct ReadyCallMsg : IBroadcast { } // host asks everyone: B yes, C no

        public struct SideAssignMsg : IBroadcast // host  all: who plays acolyte this match
        {
            public int[] AcolyteOwners;
        }

        public struct StandMsg : IBroadcast // the host's book: open while their menu is
        {
            public bool Open;
        }

        public struct SpawnAskMsg : IBroadcast // client → host: where do I stand?
        {
            public int Owner;
            public string Scene; // the asker's active scene: a joiner's map replay waits for the host's
        }

        public struct VoiceMsg : IBroadcast // any → host → all: a slice of compressed voice
        {
            public int Owner;
            public bool Ghost;
            public byte[] Data;
        }

        public struct AbsorbAskMsg : IBroadcast // client → host: I pull at this source
        {
            public int Owner;
            public Vector3 At;
        }

        public struct AbsorbGiveMsg : IBroadcast // host → all: the mote flies to the winner (Owner -1: taken before you came)
        {
            public int Owner;
            public Vector3 At;
        }

        public struct SpawnGiveMsg : IBroadcast // host → all: stand HERE
        {
            public int Owner;
            public Vector3 Point;
        }

        public struct StrokeMsg : IBroadcast
        {
            public int Owner;
            public string SurfacePath;
            public Vector3 Normal;
            public Vector3[] Points;
            public int DeclaredRune;
            public int StrokeId;       // (Owner, StrokeId) names this ink everywhere (netcode §0)
            public int ReadRune;       // the OWNER's pen-up verdict for its touching cluster -
            public float ReadScore;    // the host primes its cache, never re-reads (netcode §1)
            public int[] ClusterOwners;
            public int[] ClusterIds;
            public bool AutoDrawn;     // the game drew some of it: the copy wears the dimmer colour too
            public float AutoLength;   // how much, in metres
            // Non-empty = body ink: Points and Normal are in that bone's local
            // space, and the receiver mounts them on the same-named bone of the
            // owner's avatar. Empty = world ink.
            public string BoneName;
            // Non-zero = ink on a zombie: Points and Normal in the zombie root's
            // local space; the id is the snapshot id (host: the Zombie, client: its proxy).
            public int CreatureId;
            // World ink on a moving prop: Points and Normal in the surface's local space.
            public bool SurfaceLocal;
            // The live preview this stroke replaces (0 = none).
            public int LiveId;
        }

        /// Any -> host -> all, unreliable, 10 Hz while the pen is down: the
        /// nodes added since the last packet, same three frames as StrokeMsg.
        /// Empty Points = still drawing, nothing new; End = it went nowhere.
        public struct StrokeGrowMsg : IBroadcast
        {
            public int Owner;
            public int LiveId;
            public string SurfacePath;
            public string BoneName;
            public int CreatureId;
            public bool SurfaceLocal;
            public Vector3 Normal;
            public Vector3[] Points;
            public int From;   // index of Points[0] in the stroke
            public bool End;
        }

        /// Any -> host -> all: how ink looks after a seal event. Kind 0 sealed
        /// (boundary gold, payload cyan/grey), 1 plain again (broken, re-armed),
        /// 2 spent (brown; a copy that is not persistent burns instead).
        public struct SealLookMsg : IBroadcast
        {
            public int Owner;   // the sender, for the self-echo
            public byte Kind;
            public int[] BoundaryOwners;
            public int[] BoundaryIds;
            public bool Loop;   // one-stroke boundary: the ring closes visually
            public int[] PayloadOwners;
            public int[] PayloadIds;
            public byte[] PayloadRead; // 1 = read as a rune, 0 = fizzle
        }

        /// Any -> host -> all: the small feel of drawing actions. Kind 0 fade
        /// (Ids thin out over Seconds then burn), 1 restore (the fade was
        /// aborted), 2 chime at At, 3 poof + chime at At.
        public struct InkFxMsg : IBroadcast
        {
            public int Owner;
            public byte Kind;
            public Vector3 At;
            public float Seconds;
            public int[] Owners;
            public int[] Ids;
        }

        /// Any -> host -> all: a seal closure split (Owner, SrcId) into pieces
        /// that carry their own ids from here on. Index ranges into the source's
        /// nodes; SrcCount and SrcFirst (surface-local) pick the right copy when
        /// an erase already split it. Erase splits still replay from EraseMsg.
        public struct SplitMsg : IBroadcast
        {
            public int Sender;
            public int Owner;
            public int SrcId;
            public int SrcCount;
            public Vector3 SrcFirst;
            public string BoneName;
            public int[] From;
            public int[] To;
            public bool[] Reverse;
            public bool[] Tiny;
            public bool[] Residue;
            public int[] NewIds;
        }

        /// One piece of a shipped split, as DrawingWorld records it.
        public struct SplitPart
        {
            public int From, To, Id;
            public bool Reverse, Tiny, Residue;
        }

        /// Body ink drunk/burned by its owner (I key) - the copies on every
        /// other machine must die too. (Owner, id) pairs, same naming as
        /// StrokeMsg (netcode §0).
        public struct InkBurnMsg : IBroadcast
        {
            public int Owner;
            public int[] Ids;
            public bool Leash; // the host's leash took it: the owner's machine burns AND refunds
        }

        // ---- the BOOK STAND lobby ----
        /// A joiner announces the password it typed at the stand; the host
        /// kicks mismatches. No password set = open lobby, nothing checked.
        public struct JoinAuthMsg : IBroadcast
        {
            public string Password;
        }

        /// A player likes the map they want (client  host). One like per
        /// player - liking again just moves it.
        public struct MapLikeMsg : IBroadcast
        {
            public string Map;
        }

        /// Host  all: the current like tally, shown at the book stand.
        public struct MapLikesMsg : IBroadcast
        {
            public string[] Maps;
            public int[] Counts;
        }

        /// Host  all, 2 Hz: the pot's truth (one ink pool, host law).
        public struct PotMsg : IBroadcast
        {
            public float Fill01;
            public bool Corrupt;
            public float Prep;
            public string Pot; // the live pot's scene path, so clients pour from the same one
            public bool Grounded; // every pot broken: the ink pools at the InkGrave
            public float Capacity; // the host's pot size, so client bills scale the same
            public byte Green; // corruption/defuse progress 0..255, the black-to-green lerp
        }

        /// Host to the owner (or a client to the host, relayed): an acolyte's
        /// spell paid their wand back.
        public struct WandCreditMsg : IBroadcast
        {
            public int Owner;
            public float Amount;
        }

        /// Client → host: how much the wand drank from the pot.
        public struct PotDrinkMsg : IBroadcast
        {
            public float Amount;
        }

        public struct PlayerLeft : IBroadcast
        {
            public int Id;
        }

        public struct DisguiseMsg : IBroadcast // owner, host relays to all: an acolyte wearing an object
        {
            public int Id;
            public bool Worn;
            public string Shape;   // ScenePath of the object, or ShapeShift.LentKey for a biome-lent prop
            public Quaternion Rot; // the worn shape's world rotation
            public float Lift;     // its centre above the body's networked point
            public bool Puff;      // taking it off: the host opens the exit poison
            public bool Poof;      // a fresh scan went on: the swap burst
        }

        public struct OutfitMsg : IBroadcast // any  host  all: outfit choices
        {
            public int Id;
            public string Code; // SocketManager wire format ("2,0,1"), slot order = catalog order
            public bool HasHat; // a pillar colour was picked
            public Color32 Hat; // the pillar colour
        }

        // ---- host-authoritative zombies & rounds ----
        public struct ZombieSnap : IBroadcast // host  clients, 10 Hz unreliable (reliable when a one-shot rides)
        {
            public int[] Ids;
            public Vector3[] Pos;
            public Quaternion[] Rot;  // the whole rotation: a toppled body reads toppled
            public Vector3[] Vel;     // rigidbody velocity: the stand-in glides and strides by it
            public byte[] Kinds;      // 1 ranged, 2 demon
            public Vector3[] Scale;   // a summon's size is not in its kind
            public int[] Owner;       // the acolyte who drew it, -1 for a wild one
            public byte[] Tranced;    // 1 = pinned by fresh ink: the stand-in holds still too
            public Color32[] Tint;    // the body colour its StateView wears (spell, biome, demon form)
            public byte[] Look;       // the creature it is, by index in the book's creatures; 255 = none
            public sbyte[] Phase;     // StateView.StateT * 100
            public byte[] Anim;       // one-shots since the last beat (ZombieDress.Anim*), attack variant in the top two bits
            public byte[] Eyes;       // bits 0-2 mood, 8 red pupils, 16 swelling
            public byte[] Cond;       // 1 burning, 2 frozen, 4 demon glows, 8 ridden by a ghost
            public byte[] Hp;         // health as a byte of full, 255 = whole: the stand-in's blood reads it
            public Vector3[] Gaze;    // where the held mood's pupils point; zero = let the local eyes wander
            public byte[] Burden;     // Element.Burden01 * 100: the hip sag its weight shows
            public int[] Gone;        // died since the last beat: a beat names only the zombies near its receiver, so absence is not death
        }

        public struct GolemSnap : IBroadcast // host  clients, 10 Hz unreliable
        {
            public int[] Ids;
            public Vector3[] Pos;
            public Quaternion[] Rot;
            public Vector3[] Vel;
            public Vector3[] Scale;
            public Color[] Skin;      // the biome that raised it, not re-derived
            public int[] Owner;       // the player whose spell raised it, -1 for nature's own
            public byte[] Look;       // the creature it is, by index in the book's creatures; 255 = none
            public sbyte[] Phase;     // StateView.StateT * 100
            public byte[] Eyes;       // as ZombieSnap.Eyes
            public byte[] Cond;       // as ZombieSnap.Cond
            public byte[] Hp;         // as ZombieSnap.Hp
            public byte[] Beat;       // ChargeAttack beat: 0 idle, 1 tell, 2 run, 3 recover
            public Vector3[] Gaze;    // as ZombieSnap.Gaze
            public byte[] Burden;     // as ZombieSnap.Burden
        }

        /// host -> clients: a zombie said something; its stand-in says it too.
        public struct MumbleMsg : IBroadcast
        {
            public int Id;        // the host's zombie id, the snapshot key
            public string Text;
            public float Seconds;
        }

        public struct BiomeMsg : IBroadcast // host  clients: a lvl3 spell opened
        {
            public Vector3 At;
            public float Temp, Lum, Pressure, Balance, State;
            public float Affinity, Strength, Int, Courage, Clones;
            public float Radius;
            public float Seconds; // 0 = the lvl3 default; short = a terrain-burst linger
            public int ParticleId; // a lvl3 PARTICLE's numbers (host instance id), 0 = an ordinary biome
            public bool Close;     // that particle stopped being a place
        }

        /// host -> all: something happened TO a body. ONE channel for every
        /// effect that lands on a player, because a remote player is only a
        /// puppet on the host - their real body, health and state live on their
        /// own machine, and nothing could reach it before this.
        public struct PlayerFxMsg : IBroadcast
        {
            public int Owner;     // whose body
            public byte Kind;     // 0 hurt, 1 heal, 2 buff, 3 phase, 4 blink, 5 trail, 6 fade, 7 grip push, 8 weight push
            public float Amount;  // damage / heal / buff / seconds
            public byte Phase;    // Kind 3 only: the MatterPhase to wear
            public Vector3 Point; // Kind 4: where to. Kind 0: the shove
        }

        /// client -> host: I want to hurt the thing with this id.
        /// The client does NOT subtract anything itself.
        public struct HurtIntent : IBroadcast
        {
            public int NetId;
            public float Amount;
            public int By;    // owner id the asker blames, -1 = nobody it knows
            public byte Via;  // 1 = through a summoned creature
        }

        /// host -> all: a mark was left on something. Curses read these, so
        /// every machine has to agree about who did what to whom.
        public struct MarkMsg : IBroadcast
        {
            public int Owner;
            public byte What;
            public int Value;
        }

        /// host -> all: a combined seal fired; these players share the
        /// caster's kills for this long (CoCast).
        public struct CoCastMsg : IBroadcast
        {
            public int Caster;
            public int[] With;
            public float Seconds;
        }

        /// host -> all: this is what that thing's health IS. Applied verbatim,
        /// so one tree cannot end up on two different numbers.
        /// ★ WHAT THINGS IN THE WORLD CURRENTLY ARE. Health already crossed;
        /// nothing else did - so on a client a burning crate was not hot, a
        /// thing turning to gas never faded, and a frozen zombie looked fine.
        ///
        /// Only the axes you can SEE travel, and only for things that have
        /// actually left their natural, which is a small minority at any moment.
        /// Simulation stays entirely on the host; this is the picture of it.
        public struct StateMsg : IBroadcast
        {
            public int[] Ids;
            public short[] Temp;    // degrees, rounded - a degree is not visible
            public sbyte[] State;   // -100..100, so transparency and phase read
            public sbyte[] Lum;
            // ★ THE WHOLE BOARD RIDES (parity): without these a client could
            // not feel slick, sticky, weight, bravery or drunkenness at all
            public sbyte[] Press;   // -100..100 of AxisCap
            public sbyte[] Bal;
            public sbyte[] Aff;
            public sbyte[] Mind;
            public sbyte[] Cour;
        }

        public struct HealthMsg : IBroadcast
        {
            public int NetId;
            public float Health;
            public float Max;
            public int By;      // owner that dealt it, -1 when nobody
            public byte Via;    // 1 = through a summoned creature
            public byte Cause;  // Element.CauseCode: 0 hit, 1 burning, 2 freezing, 3 other
        }

        public struct KillFeed : IBroadcast // host  clients: shared ink for the kill
        {
            public Vector3 Pos;
            public int Owner;     // the dead zombie's summoner, -1 when wild
            public int KilledBy;  // Mark.KilledBy of the dead zombie
            public byte Via;      // 1 = killed through a summoned creature
        }

        public struct RoundState : IBroadcast // host  clients, 2 Hz
        {
            public byte Phase; // RoundDirector.Phase ordinal
            public int Round;
            public int Left;
            public float Timer;
            public int Kills;
            public byte Ending; // Achievements.Ending, set when Phase is Over
            public byte BossHp; // the living bosses' health together, 255 = full
            public byte Bosses; // how many bosses live
            public string BossName;
            public Vector3 CauseAt; // where the ending camera looks (EndingShot), set when Phase is Over
        }

        // ---- host-authoritative seals/matter/particles/lifting (netcode §1-§4) ----
        public struct UnlockMsg : IBroadcast // any  host  all: grimoire truth
        {
            public int Owner;
            public int Card; // -1 = none
            public int Rune; // -1 = none
            public Vector3 At; // where the deed happened: the poof shows there on every other machine
            public bool HasAt;
        }

        /// ★ ONE BOOK FOR EVERYONE (his law): the host's spellbook is the
        /// match's spellbook, carried whole as JSON.
        public struct BookMsg : IBroadcast
        {
            public string Json;
        }

        public struct DeclareRuneMsg : IBroadcast // any → host → others: declare a rune for strokes
        {
            public int Owner;
            public int[] Owners;
            public int[] Ids;
            public int Rune;
            public float Score;  // the read's score (1 for a stamp)
            public bool Stamp;   // true = DeclaredRune lands; false = a re-read's tint only
        }

        public struct BodySealFire : IBroadcast // client  host: a body seal fired (its ink never replicates)
        {
            public Vector3 Origin;
            public Vector3 Normal;
            public int Edges;
            public float Duration;
            public int[] Runes;
            public float[] Strengths;
            public Vector3[] Centers;
            public Vector3[] PushDirs;
            public float[] Sizes;
            public float SealRadius; // the seal's own size: reach is the rune's size relative to it
        }

        public struct SealMsg : IBroadcast // host  clients: gold ring, display only
        {
            public int SealId;
            public Vector3[] Loop;
            public float Duration;
            public string SurfacePath; // the loop's carrier: a lifted crate takes its ring along
            // the zones' looks (dark sphere, light, ember), built the same way here
            public int[] Runes;
            public Vector3[] Centers;
            public float[] Radii;
            public float[] Intensities;
            public Vector3[] PushDirs;
            public float DarkSpread;
            // a body seal: no ring, Centers and PushDirs in the caster's body space
            public bool Body;
            public int Owner; // that caster: the zones ride their pilot or puppet
        }

        public struct SealEndMsg : IBroadcast // host  clients: ring down; resolved burns the ink
        {
            public int SealId;
            public bool Resolved;
            public int Owner;
            public int[] BurnOwners;
            public int[] BurnIds;
        }

        public struct EraseMsg : IBroadcast // any  host  others: ink graphs must not drift
        {
            public int Owner;
            public Vector3 From;
            public Vector3 To;
            public float Radius;
            public string BoneName; // non-empty = body paint: From/To in that bone's local space
        }

        public struct MatterSnap : IBroadcast // host  clients, 10 Hz unreliable
        {
            public int[] Ids;
            public Vector3[] Pos;
            public Quaternion[] Rot;
            public Vector3[] Scale;
            public byte[] Mats;
            public byte[] Phases;
            public byte[] Looks;
            public byte[] Edges;  // the seal's line count: picks the ShapeLibrary skin
            public Vector3[] Vels;  // the blob's velocity: the current a client wades in
            public float[] Sticks;  // Matter.Stickiness: a client's slick pool
        }

        public struct ParticleSnap : IBroadcast // host  clients, 10 Hz unreliable
        {
            public int[] Ids;
            /// WHICH POSED BLOB, by its index in the authored shape list - the
            /// same list on every machine in a build, so a client wears what
            /// the host wears without a name per particle per snapshot.
            public byte[] Kinds;
            /// The colour its ten numbers came out as. A client cannot compute
            /// this: it never sees the payload.
            public Color32[] Tints;
            /// 1 · 2 · 3 - a lvl2 shows its ring, a lvl3 shows it is a place.
            public byte[] Levels;
            /// WHAT IT IS RIDING, by that thing's net id - 0 for a free mote.
            /// An attached particle moves with its host, so a client that does
            /// not know the host would show a hook hanging in mid air while the
            /// person it caught walks off.
            public int[] Rides;
            /// Whose spell it is - a client judges a nearby cast by its caster.
            public int[] Owners;
            public Vector3[] Pos;
            public float[] Scale;
            /// 1 dormant, 2 claimed, 4 hidden area child, 8 carries a light.
            public byte[] Flags;
            /// AuraRadius of a lvl3, 0 below: the reach ring's true size.
            public float[] Reach;
            /// ParticleKind: the idle flames or arcs a burning kind wears.
            public byte[] Kinds2;
            /// The worn area's index in the book (SpellParticle.WireArea), 255 none.
            public byte[] Areas;
            /// The book row whose authored look it wears (SpellParticle.SkinRow), 255 none.
            public byte[] Looks;
        }

        public struct PropReg : IBroadcast // host  clients: a scene prop went dynamic
        {
            public int Id;
            public string Path;
        }

        public struct PropSnap : IBroadcast // host  clients, 10 Hz unreliable
        {
            public int[] Ids;
            public Vector3[] Pos;
            public Quaternion[] Rot;
        }

        public struct GrabIntent : IBroadcast // client  host: E on a thing
        {
            public int MatterId; // host instance id of a matter blob, 0 = use Path
            public string Path;
            public float HoldDist;
            public int CreatureId;    // a zombie or golem by its snapshot id, 0 = none
            public byte CreatureKind; // 1 zombie, 2 golem
        }

        public struct LiftAim : IBroadcast // client  host, 10 Hz unreliable: where the hand is
        {
            public Vector3 Hand;
            public Quaternion Rot;
        }

        public struct ThrowIntent : IBroadcast // client  host: E while holding
        {
            public Vector3 Dir;
        }

        public struct DropIntent : IBroadcast { public bool Wake; } // client -> host: let go; Wake = F, wake it in the hand

        public struct MapDefMsg : IBroadcast { public string Json; } // host -> clients: the picked map's layer, empty = a plain scene

        public struct PushIntent : IBroadcast // client -> host, unreliable: walked into or shoved a loose prop
        {
            public string Path; // the prop's scene path
            public Vector3 Vel; // the flat velocity it set, or the change it added (Add)
            public bool Add;
        }

        public struct ClaimIntent : IBroadcast // client  host: grab a spell particle
        {
            public int ParticleId;
        }

        public struct AbsorbMsg : IBroadcast // a world source was absorbed (scene path)
        {
            public string Path;
        }

        public struct ChestAskMsg : IBroadcast // client -> host: I open this chest
        {
            public int Id;
        }

        public struct ChestMsg : IBroadcast // host -> all: it opens, roll with this seed
        {
            public int Id;
            public int Seed;
        }

        public struct IdentityMsg : IBroadcast // who a player id IS (persona + steam id)
        {
            public int Id;
            public string Name;
            public ulong SteamId;
        }

        public struct GrabAck : IBroadcast // host  the asking client: verdict on a grab
        {
            public bool Ok;
            public string Note;
            public float Mass; // the held body's mass on an Ok grab: the lifter's carried weight
        }

        // ------------------------------------------------------------ state --
        public static int RemoteCount => _instance != null ? _instance._avatars.Count : 0;

        /// True while a received stroke is being rebuilt - suppresses re-send.
        public static bool ApplyingRemote { get; private set; }

        /// Latest round state received from the host (client HUD reads these).
        public static bool HasRound;
        public static byte NetPhase;
        public static int NetRound, NetLeft, NetKills;
        public static float NetTimer;
        public static byte NetBossHp, NetBosses, NetEnding;
        public static string NetBossName = "";
        public static Vector3 NetCauseAt;

        /// Client wizards' music: any zombie proxy near this point
        /// (hosts ask Zombie.All directly).
        public static bool AnyZombieNear(Vector3 at, float range)
        {
            if (_instance == null) return false;
            float sq = range * range;
            foreach (var kv in _instance._proxies)
            {
                var p = kv.Value;
                if (p != null && (p.transform.position - at).sqrMagnitude < sq) return true;
            }
            return false;
        }

        /// Client music: any host-run golem of an enemy owner near this point.
        public static bool AnyEnemyGolemNear(Vector3 at, float range, Team mine)
        {
            if (_instance == null) return false;
            float sq = range * range;
            foreach (var kv in _instance._golems)
            {
                var g = kv.Value;
                if (g == null || !Teams.Enemies(mine, Teams.OfOwner(g.OwnerId))) continue;
                if ((g.transform.position - at).sqrMagnitude < sq) return true;
            }
            return false;
        }

        /// Client music: any loose enemy mote near this point (a riding mote's threat is its carrier).
        public static bool AnyEnemyMoteNear(Vector3 at, float range, Team mine)
        {
            if (_instance == null) return false;
            float sq = range * range;
            foreach (var kv in _instance._moteProxies)
            {
                var m = kv.Value;
                if (m == null || m.Rides != 0 || !Teams.Enemies(mine, Teams.OfOwner(m.OwnerId))) continue;
                if ((m.transform.position - at).sqrMagnitude < sq) return true;
            }
            return false;
        }

        /// Host referee: any REMOTE player of this side still standing.
        public static bool AnySideAlive(Side side)
        {
            if (_instance == null) return false;
            foreach (var kv in _instance._avatars)
            {
                var a = kv.Value;
                if (a == null || a.Downed) continue;
                if (Sides.Of(OwnerIdOf(kv.Key)) == side) return true;
            }
            return false;
        }

        /// Host referee: any remote player of this side in the match at all.
        public static bool AnySidePresent(Side side)
        {
            if (_instance == null) return false;
            foreach (var kv in _instance._avatars)
                if (kv.Value != null && Sides.Of(OwnerIdOf(kv.Key)) == side) return true;
            return false;
        }

        /// True when every remote player reports downed (host wipe check).
        public static bool AllRemotesDown
        {
            get
            {
                if (_instance == null) return true;
                foreach (var kv in _instance._avatars)
                    if (kv.Value != null && !kv.Value.Downed) return false;
                return true;
            }
        }

        static NetSync _instance;

        readonly Dictionary<int, NetAvatar> _avatars = new Dictionary<int, NetAvatar>();
        readonly Dictionary<int, NetZombieProxy> _proxies = new Dictionary<int, NetZombieProxy>();
        readonly Dictionary<int, float> _proxySeenAt = new Dictionary<int, float>(); // client: the last beat that named each zombie
        const float ProxyTimeout = 4f; // unnamed for this long = dead and its Gone word was lost

        // host: everyone's newest presence, and what each client has had of it
        readonly Dictionary<int, PlayerState> _latest = new Dictionary<int, PlayerState>();
        readonly Dictionary<int, int> _latestSeq = new Dictionary<int, int>();
        class FanSlot { public int Seq = -1; public float NextAt; }
        readonly Dictionary<int, Dictionary<int, FanSlot>> _fan = new Dictionary<int, Dictionary<int, FanSlot>>();
        readonly Dictionary<int, Dictionary<int, float>> _zombieNextAt = new Dictionary<int, Dictionary<int, float>>();
        readonly HashSet<int> _snapPrevIds = new HashSet<int>();
        readonly List<int> _snapGone = new List<int>();
        readonly List<int> _pick = new List<int>();
        readonly HashSet<int> _seen = new HashSet<int>();
        static readonly List<int> _gone = new List<int>();
        static readonly System.Collections.Generic.Dictionary<int, string> _outfits
            = new System.Collections.Generic.Dictionary<int, string>();
        static readonly Dictionary<int, IdentityMsg> _identities = new Dictionary<int, IdentityMsg>();
        float _sendTimer, _zombieTimer;
        bool _registered;
        bool _outfitSent;
        bool _identityAdopted; // stable ClientId adopted this connection (netcode §0)

        // ---- cross-machine stroke ledger: (owner, id)  the local copies (netcode §0) ----
        // one id names every piece an erase or a closure split it into
        static readonly Dictionary<long, List<Stroke>> _netStrokes = new Dictionary<long, List<Stroke>>();
        static readonly List<long> _strokePrune = new List<long>();
        static readonly List<Stroke> _clusterBuf = new List<Stroke>();
        static readonly List<Stroke> _pieceBuf = new List<Stroke>();
        static int _nextStrokeId = 1;
        static int _nextLiveId = 1;
        static int _nextSplitN = 0;

        static long StrokeKey(int owner, int id) => ((long)(uint)owner << 32) | (uint)id;

        /// A fresh id for a closure piece: negative, striped by the minting
        /// machine, so the host's and an owner's body-ink splits never collide
        /// with each other or with the owner's own positive stroke ids.
        public static int NextSplitId() => -(++_nextSplitN * 256 + (Grimoire.LocalPlayerId & 255));

        /// True when a closure split of `src` on this machine must ship: the
        /// host for world ink, the owner for its own body ink.
        public static bool ShipsSplit(Stroke src)
        {
            if (_instance == null || !NetGame.Connected || src == null || src.NetId == 0 || src.OnPuppet) return false;
            return NetGame.IsHost || (src.Persistent && src.OwnerId == Grimoire.LocalPlayerId);
        }

        /// A stroke's first node in its surface's space - the same numbers on
        /// every machine, unlike the world position of a bone.
        static Vector3 LocalFirst(Stroke s)
        {
            var first = s.First;
            if (first == null) return Vector3.zero;
            return s.Surface != null ? s.Surface.InverseTransformPoint(first.transform.position) : first.transform.position;
        }

        public static void PushSplit(Stroke src, List<SplitPart> parts)
        {
            if (!ShipsSplit(src) || parts == null || parts.Count == 0) return;
            int n = parts.Count;
            var msg = new SplitMsg
            {
                Sender = Grimoire.LocalPlayerId, Owner = src.OwnerId, SrcId = src.NetId,
                SrcCount = src.Nodes.Count, SrcFirst = LocalFirst(src),
                BoneName = src.Persistent && src.Surface != null ? src.Surface.name : "",
                From = new int[n], To = new int[n], Reverse = new bool[n], Tiny = new bool[n],
                Residue = new bool[n], NewIds = new int[n]
            };
            for (int i = 0; i < n; i++)
            {
                msg.From[i] = parts[i].From; msg.To[i] = parts[i].To; msg.NewIds[i] = parts[i].Id;
                msg.Reverse[i] = parts[i].Reverse; msg.Tiny[i] = parts[i].Tiny; msg.Residue[i] = parts[i].Residue;
            }
            InstanceFinder.ClientManager.Broadcast(msg);
        }

        static bool AnyAlive(List<Stroke> list)
        {
            foreach (var s in list)
                if (s != null && s.Alive) return true;
            return false;
        }

        /// File a stroke (or a split piece) under its (owner, id).
        public static void RegisterNetStroke(Stroke s)
        {
            if (s == null || s.NetId == 0) return;
            if (_netStrokes.Count > 1024)
            {
                _strokePrune.Clear();
                foreach (var kv in _netStrokes)
                    if (kv.Value == null || !AnyAlive(kv.Value)) _strokePrune.Add(kv.Key);
                foreach (var k in _strokePrune) _netStrokes.Remove(k);
            }
            long key = StrokeKey(s.OwnerId, s.NetId);
            if (!_netStrokes.TryGetValue(key, out var list))
                _netStrokes[key] = list = new List<Stroke>(1);
            if (list.Contains(s)) return;
            list.RemoveAll(x => x == null || !x.Alive); // retired sources drop as their pieces file in
            list.Add(s);
        }

        /// The first living piece of (owner, id), or null.
        static Stroke FindNetStroke(int owner, int id)
        {
            if (!_netStrokes.TryGetValue(StrokeKey(owner, id), out var list)) return null;
            foreach (var s in list)
                if (s != null && s.Alive) return s;
            return null;
        }

        /// Every living piece of (owner, id) appended to `into`; true when any.
        static bool FindNetStrokes(int owner, int id, List<Stroke> into)
        {
            if (!_netStrokes.TryGetValue(StrokeKey(owner, id), out var list)) return false;
            bool any = false;
            foreach (var s in list)
                if (s != null && s.Alive) { into.Add(s); any = true; }
            return any;
        }

        /// Pairs of (owner, id) arrays resolved to every living piece.
        static void FindNetStrokes(int[] owners, int[] ids, List<Stroke> into)
        {
            if (owners == null || ids == null || owners.Length != ids.Length) return;
            for (int i = 0; i < ids.Length; i++) FindNetStrokes(owners[i], ids[i], into);
        }

        // ---- live previews: friends' strokes still under the pen (owner, live id) ----
        readonly Dictionary<long, Stroke> _live = new Dictionary<long, Stroke>();
        readonly Dictionary<long, float> _liveAt = new Dictionary<long, float>();
        const float LiveTimeout = 2f; // no packet this long = the owner is gone, the preview goes

        /// SurfaceDrawer names a fresh stroke before its first node.
        public static int ClaimLiveId() => _nextLiveId++;

        void DropLive(long key)
        {
            if (_live.TryGetValue(key, out var s) && s != null && s.Alive)
            {
                s.Burn();
                DrawingWorld.Instance?.Strokes.Remove(s);
            }
            _live.Remove(key);
            _liveAt.Remove(key);
        }

        void PruneLive()
        {
            if (_live.Count == 0) return;
            float now = Time.unscaledTime;
            _strokePrune.Clear();
            foreach (var kv in _liveAt)
                if (now - kv.Value > LiveTimeout) _strokePrune.Add(kv.Key);
            foreach (var k in _strokePrune) DropLive(k);
        }

        // client-side proxies for host truth (netcode §2/§3)
        readonly Dictionary<int, NetMatterProxy> _matterProxies = new Dictionary<int, NetMatterProxy>();
        readonly Dictionary<int, NetMoteProxy> _moteProxies = new Dictionary<int, NetMoteProxy>();
        readonly Dictionary<int, NetSealRing> _rings = new Dictionary<int, NetSealRing>();
        readonly Dictionary<int, Transform> _propGhosts = new Dictionary<int, Transform>();

        // host-side: remote friends' holds + net-lifted props (netcode §4)
        class RemoteHold
        {
            public int Owner;
            public Rigidbody Body;
            public InkMark[] Marks;
            public SpellParticle Mote;
            public Vector3 Hand;
            public Quaternion Rot;
            public Quaternion RelRot = Quaternion.identity; // cargo pose relative to the lifter's heading at grab
            public bool HasAim;
            public bool HadGravity;
        }
        readonly Dictionary<int, RemoteHold> _holds = new Dictionary<int, RemoteHold>();

        /// HOST: is this body in a remote friend's hand right now.
        public static bool IsRemoteHeld(Rigidbody rb)
        {
            if (rb == null || _instance == null || !NetGame.IsHost) return false;
            foreach (var kv in _instance._holds)
                if (kv.Value.Body == rb) return true;
            return false;
        }

        // each owner's strength fraction as their presence last announced it
        static readonly Dictionary<int, float> _strengthOf = new Dictionary<int, float>();

        /// Lift and throw strength of an owner: the local body reads its own
        /// health, a remote one what its presence announced (HandGrab law).
        public static float StrengthOf(int ownerId)
        {
            if (ownerId == Grimoire.LocalPlayerId) return HandGrab.LocalStrength();
            return _strengthOf.TryGetValue(ownerId, out float f)
                ? Mathf.Lerp(DrawingConfig.StrengthFloorMul, 1f, f) : 1f;
        }
        static readonly List<int> _holdGone = new List<int>();
        readonly Dictionary<int, float> _lastBodyFire = new Dictionary<int, float>();
        static readonly List<Rigidbody> _trackedProps = new List<Rigidbody>();
        static readonly List<int> _trackedPropIds = new List<int>();
        static int _nextPropId = 1;
        // host: every prop tracked on this map, its newest id and the path it first
        // registered by, replayed to joiners at the pose it has then
        static readonly List<Rigidbody> _everProps = new List<Rigidbody>();
        static readonly List<int> _everPropIds = new List<int>();
        static readonly List<string> _everPropPaths = new List<string>();
        static readonly List<int> _burnOwnersBuf = new List<int>();
        static readonly List<int> _burnIdsBuf = new List<int>();

        /// The outfit code a remote player announced ("" = defaults).
        public static string OutfitOf(int id)
            => _outfits.TryGetValue(id, out var code) ? code : "";

        /// The pillar hat colour a remote player announced (null = unpainted).
        static readonly Dictionary<int, Color> _hats = new Dictionary<int, Color>();
        public static Color? HatOf(int id)
            => _hats.TryGetValue(id, out var c) ? c : (Color?)null;

        static readonly List<Color?> _hatsWorn = new List<Color?>();
        int _outfitAsks, _outfitAnswers; // the host answers every announcement, in order
        float _outfitAskedAt;

        /// NO TWO HATS ALIKE: a colour somebody here already wears is taken, and the asker's turns to
        /// the nearest one nobody wears (HatColor.Free). The host rules on every announcement; the
        /// asker's own machine asks the same question first.
        public static Color? FreeHat(Color? wish, int asker)
        {
            if (_instance == null || !NetGame.Connected) return wish;
            _hatsWorn.Clear();
            if (NetGame.IsHost)
            {
                foreach (var id in InstanceFinder.ServerManager.Clients.Keys)
                    if (id != asker && _outfits.ContainsKey(id)) _hatsWorn.Add(HatOf(id));
            }
            else
                foreach (var id in _instance._avatars.Keys)
                    if (id != asker) _hatsWorn.Add(HatOf(id));
            return HatColor.Free(wish, _hatsWorn, asker);
        }

        public static Color? FreeHat(Color? wish)
            => _instance != null ? FreeHat(wish, _instance.LocalId) : wish;

        /// The custom pose a remote player last played per slot, so the puppet
        /// replays the same keyframes and not the built-in.
        static readonly Dictionary<long, EmoteDef> _emoteDefs = new Dictionary<long, EmoteDef>();
        public static EmoteDef EmoteDefOf(int owner, int slot)
            => _emoteDefs.TryGetValue(((long)owner << 8) | (uint)(slot & 0xff), out var d) ? d : null;

        /// Announce the local player's outfit to everyone (once on connect;
        /// the lobby outfit picker and the hat pillar call this again after
        /// every change).
        public static void PushLocalOutfit()
        {
            if (!NetGame.Connected || _instance == null) return;
            var hat = HatColor.Saved(); // the wish; what is worn comes back from the host
            _instance._outfitAsks++;
            _instance._outfitAskedAt = Time.unscaledTime;
            InstanceFinder.ClientManager.Broadcast(new OutfitMsg
            {
                Id = _instance.LocalId,
                Code = SocketManager.LocalOutfitCode(),
                HasHat = hat != null,
                Hat = hat ?? Color.white
            });
        }

        /// The local acolyte put a disguise on, took it off, or turned it.
        public static void PushLocalDisguise(bool worn, string shape, Quaternion rot, float lift, bool puff = false,
            bool poof = false)
        {
            if (!NetGame.Connected || _instance == null) return;
            InstanceFinder.ClientManager.Broadcast(new DisguiseMsg
            {
                Id = _instance.LocalId,
                Worn = worn,
                Shape = shape ?? "",
                Rot = rot,
                Lift = lift,
                Puff = puff,
                Poof = poof
            });
        }

        /// The local acolyte scanned a prop: its green mark shows everywhere.
        public static void PushLocalScan(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            _lastScanOf[Grimoire.LocalPlayerId] = path; // the transformation ink reads it here too, offline included
            if (!NetGame.Connected || _instance == null) return;
            InstanceFinder.ClientManager.Broadcast(new ScanMsg { Owner = Grimoire.LocalPlayerId, Path = path });
        }

        // host: scan counts per prop path, replayed to joiners
        static readonly Dictionary<string, int> _scans = new Dictionary<string, int>();

        // host: ids this map destroyed for good, in order, replayed to joiners
        static readonly List<int> _goneForGood = new List<int>();

        // host: chests this map opened and the seed each rolled, replayed to joiners
        static readonly Dictionary<int, int> _chestSeeds = new Dictionary<int, int>();

        // host: world sources this map consumed, by scene path, replayed to joiners
        static readonly List<string> _spentSources = new List<string>();

        // host: joiners still owed the map-bound replay (broken props, ink, scans)
        static readonly HashSet<int> _owedMapWelcome = new HashSet<int>();

        static void OnSceneSwapped(UnityEngine.SceneManagement.Scene a, UnityEngine.SceneManagement.Scene b)
        {
            _scans.Clear(); // paths belong to the map that was
            _goneForGood.Clear();
            _everProps.Clear(); _everPropIds.Clear(); _everPropPaths.Clear();
            _chestSeeds.Clear();
            _spentSources.Clear();
            _owedMapWelcome.Clear();
        }

        /// HOST: an element every machine names the same way died for good here.
        public static void NoteGone(int netId)
        {
            if (NetGame.IsHost) _goneForGood.Add(netId);
        }

        // host: each acolyte's newest scan - what the transformation ink turns a target into
        static readonly Dictionary<int, string> _lastScanOf = new Dictionary<int, string>();
        public static string LastScanOf(int owner) => _lastScanOf.TryGetValue(owner, out var p) ? p : "";

        /// His double edge: while the curser hides as something, the cursed
        /// wizard's scan re-dresses the curser as THAT.
        static void CurserFollowsScan(ScanMsg msg)
        {
            if (msg.Owner < 0 || Grimoires.CurserOf(msg.Owner) != Grimoire.LocalPlayerId) return;
            if (ShapeShift.LocalIsShaped) ShapeShift.WearKey(msg.Path);
        }

        static void ApplyScan(ScanMsg msg)
        {
            var t = ShapeShift.ResolveShape(msg.Path, out _);
            if (t == null) return;
            ShapeShift.TintScanGreen(t);
            if (msg.Owner >= 0 && !Juice.Sound(Sfx.Scan, t.position)) Juice.Chime(t.position);
        }

        /// The local player started a pose on a slot: the whole keyframe set
        /// travels so a custom pose plays the same on every screen.
        public static void PushLocalEmote(int slot, EmoteDef def)
        {
            if (!NetGame.Connected || _instance == null || def == null) return;
            InstanceFinder.ClientManager.Broadcast(new EmoteMsg
            {
                Owner = Grimoire.LocalPlayerId,
                Slot = (sbyte)slot,
                Json = JsonUtility.ToJson(def)
            });
        }

        /// The local player is sculpting a pose live (pose mode, the studio on
        /// their own body): presence carries LiveEmoteSlot and the working pose
        /// travels as that slot whenever it changes, throttled.
        public const sbyte LiveEmoteSlot = 127;
        public static bool LiveEmote { get; private set; }
        static float _liveNext;
        static string _liveJson;

        public static void BeginLiveEmote()
        {
            LiveEmote = true;
            _liveJson = null;
            _liveNext = 0f;
        }

        public static void EndLiveEmote() => LiveEmote = false;

        public static void PushLiveEmote(EmoteRig rig)
        {
            if (rig == null || !NetGame.Connected || _instance == null) return;
            if (Time.time < _liveNext) return;
            _liveNext = Time.time + 0.2f;
            var def = new EmoteDef { name = "Live", loop = false };
            def.frames.Add(rig.CapturePose());
            string json = JsonUtility.ToJson(def);
            if (json == _liveJson) return;
            _liveJson = json;
            InstanceFinder.ClientManager.Broadcast(new EmoteMsg
            {
                Owner = Grimoire.LocalPlayerId,
                Slot = LiveEmoteSlot,
                Json = json
            });
        }

        /// A body made a sound or a ground puff: everyone hears and sees it.
        public static void PushBodyFx(byte kind, Vector3 at)
        {
            if (!NetGame.Connected || _instance == null) return;
            var msg = new BodyFxMsg { Owner = Grimoire.LocalPlayerId, Kind = kind, At = at };
            if (NetGame.IsHost) InstanceFinder.ServerManager.Broadcast(msg);
            else InstanceFinder.ClientManager.Broadcast(msg);
        }

        /// This machine's own body sound or puff: played here under FxQuiet
        /// (BodyFxMsg is the one copy the others get) and sent.
        public static void PlayAndPushBodyFx(byte kind, Vector3 at)
        {
            PlayBodyFx(kind, at);
            PushBodyFx(kind, at);
        }

        /// HOST: a zombie mumbled - its stand-ins show the same bubble.
        public static void PushMumble(int zombieId, string text, float seconds)
        {
            if (_instance == null || !NetGame.IsHost || !NetGame.Connected) return;
            InstanceFinder.ServerManager.Broadcast(new MumbleMsg
                { Id = zombieId, Text = text, Seconds = seconds }, true, Channel.Unreliable);
        }

        void OnMumbleClient(MumbleMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (_proxies.TryGetValue(msg.Id, out var proxy) && proxy != null)
                proxy.Mumble(msg.Text, msg.Seconds);
        }

        /// HOST: a poison field opened on a body here - clients open the same.
        public static void PushField(byte kind, Vector3 at, float radius, float seconds, int rideOn = 0)
        {
            if (_instance == null || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new FieldMsg
                { Kind = kind, At = at, Radius = radius, Seconds = seconds, RideOn = rideOn });
        }

        /// The same cloud (and, for a death, the same blast) on every machine.
        /// `mirror`: a client's copy of the host's field - it bills only the
        /// local body, the host's own field bills the creatures.
        static void OpenField(byte kind, Vector3 at, float radius, float seconds, bool mirror,
            Transform rideOn = null)
        {
            var f = PoisonField.Open(at, radius, seconds, rideOn);
            f.Mirror = mirror;
            if (kind == 0)
            {
                // the clients open the same field from FieldMsg: its thud and
                // its shove on their own body are theirs, so no kicks ship
                bool quiet = FxQuiet;
                FxQuiet = true;
                try { Shove.Blast(at, radius, DrawingConfig.DetonateShove, 0f, "a dying acolyte burst", shipKicks: false); }
                finally { FxQuiet = quiet; }
            }
        }

        // client: auras announced before their zombie's first snapshot
        readonly Dictionary<int, FieldMsg> _pendingAuras = new Dictionary<int, FieldMsg>();
        readonly Dictionary<int, float> _pendingAuraAt = new Dictionary<int, float>();

        /// The summon's aura on its stand-in, where SummonedZombie.Begin put it.
        static void OpenAura(NetZombieProxy proxy, FieldMsg msg)
        {
            float bodyHeight = proxy.transform.localScale.y * 2f;
            OpenField(2, proxy.transform.position + Vector3.up * bodyHeight * 0.35f,
                msg.Radius, msg.Seconds, true, proxy.transform);
        }

        /// HOST: an acolyte puppet went down - its death cloud and blast, here
        /// and on every client (the dying machine itself opens nothing).
        public static void HostAcolyteDeath(Vector3 at)
        {
            if (!NetGame.IsAuthority) return;
            OpenField(0, at, DrawingConfig.PoisonDeathRadius, DrawingConfig.PoisonDeathSeconds, false);
            PushField(0, at, DrawingConfig.PoisonDeathRadius, DrawingConfig.PoisonDeathSeconds);
        }

        /// HOST: a disguise came off at `at` - the exit cloud, here and everywhere.
        public static void HostDisguiseExit(Vector3 at)
        {
            if (!NetGame.IsAuthority) return;
            OpenField(1, at, DrawingConfig.PoisonExitRadius, DrawingConfig.PoisonExitSeconds, false);
            PushField(1, at, DrawingConfig.PoisonExitRadius, DrawingConfig.PoisonExitSeconds);
        }

        static void PlayBodyFx(byte kind, Vector3 at)
        {
            bool quiet = FxQuiet;
            FxQuiet = true; // every machine plays this from the message itself
            try { PlayBodyFxNow(kind, at); }
            finally { FxQuiet = quiet; }
        }

        static void PlayBodyFxNow(byte kind, Vector3 at)
        {
            switch (kind)
            {
                case 0: if (!Juice.Sound(Sfx.ThrownObjectHitting, at, 0.8f, Random.Range(0.9f, 1.05f))) Juice.Thud(at); break;
                case 1: Juice.Sting(at); break;
                case 2: Juice.Whoosh(at); break;
                case 3: Juice.Pop(at); break;
                case 4: Juice.Sound(Sfx.MagicBurst, at, 0.8f, 1.1f, false); break; // somebody arrives out of thin air
                case 5: GrammarFX.PuffBurst(at, new Color(0.85f, 0.95f, 1f), 5); break;
                case 6: GrammarFX.PuffBurst(at, new Color(0.45f, 1f, 0.55f), 2); break;
                case 7: GrammarFX.PuffBurst(at, new Color(0.7f, 0.9f, 1f), 4); break;
                case 8: GrammarFX.PuffBurst(at, new Color(0.85f, 0.65f, 0.2f), 4); break;
                case 9: Juice.Pop(at); GrammarFX.PuffBurst(at, new Color(1f, 0.9f, 0.6f), 4); break;
                case 10: if (!Juice.Sound(Sfx.GhostTake, at)) Juice.Chime(at); break; // a ghost takes something over, or lets go
                case 11: if (!Juice.Sound(Sfx.ChillImpact, at)) Juice.Thud(at); break; // a body freezes into a statue
            }
        }

        /// The thing a puppet's hands are on, resolved on THIS machine from
        /// the host-side id its owner announced (null when unknown here).
        public static Transform ResolveHeld(byte kind, int id)
        {
            if (_instance == null || kind == 0 || id == 0) return null;
            if (NetGame.IsAuthority)
            {
                switch (kind)
                {
                    case 1:
                        int i = _trackedPropIds.IndexOf(id);
                        return i >= 0 && _trackedProps[i] != null ? _trackedProps[i].transform : null;
                    case 2:
                        foreach (var m in Matter.Living)
                            if (m != null && m.gameObject.GetInstanceID() == id) return m.transform;
                        return null;
                    case 3:
                        foreach (var p in SpellParticle.Living)
                            if (p != null && p.gameObject.GetInstanceID() == id) return p.transform;
                        return null;
                }
                return null;
            }
            switch (kind)
            {
                case 1: return _instance._propGhosts.TryGetValue(id, out var t) ? t : null;
                case 2: return _instance._matterProxies.TryGetValue(id, out var mp) && mp != null ? mp.transform : null;
                case 3: return _instance._moteProxies.TryGetValue(id, out var mo) && mo != null ? mo.transform : null;
            }
            return null;
        }

        /// The wire name of what the local hands hold, for the presence.
        public static void HeldIdOf(Rigidbody body, SpellParticle mote, Transform remoteCargo,
            out byte kind, out int id)
        {
            kind = 0; id = 0;
            if (mote != null) { kind = 3; id = mote.gameObject.GetInstanceID(); return; }
            if (body != null)
            {
                var blob = body.GetComponentInParent<Matter>(); // MatterSnap names the blob, not its core
                if (blob != null) { kind = 2; id = blob.gameObject.GetInstanceID(); return; }
                int i = _trackedProps.IndexOf(body);
                if (i >= 0) { kind = 1; id = _trackedPropIds[i]; }
                return;
            }
            if (remoteCargo == null || _instance == null) return;
            var mp = remoteCargo.GetComponent<NetMatterProxy>();
            if (mp != null) { kind = 2; id = mp.HostId; return; }
            var mo = remoteCargo.GetComponent<NetMoteProxy>();
            if (mo != null) { kind = 3; id = mo.HostId; return; }
            foreach (var kv in _instance._propGhosts)
                if (kv.Value == remoteCargo) { kind = 1; id = kv.Key; return; }
        }

        /// The last disguise each remote player announced, for avatars built later.
        static readonly Dictionary<int, DisguiseMsg> _disguises = new Dictionary<int, DisguiseMsg>();

        void Awake() => _instance = this;

        void Update() { using (PerfMarkers.UpdNet.Auto()) Turn(); }

        void Turn()
        {
            if (!NetGame.HasManager) return;
            RegisterOnce();

            if (!NetGame.Connected)
            {
                _outfitSent = false; // re-announce the look on the next session
                _outfitAsks = _outfitAnswers = 0;
                HatColor.Forget();
                _identityAdopted = false;
                _owedMapWelcome.Clear();
                return;
            }

            // connected identity = FishNet ClientId, so strokes and grimoire
            // keys agree across machines (netcode §0)
            if (!_identityAdopted && LocalId >= 0)
            {
                _identityAdopted = true;
                int stable = OwnerIdOf(LocalId);
                int old = Grimoire.LocalPlayerId;
                if (old != 0 && old != stable)
                {
                    Grimoire.Rekey(old, stable);
                    Sides.Rekey(old, stable); // the pillar's choice rides along
                    DrawingWorld.Instance?.RekeyOwner(old, stable); // and the ink already drawn
                }
                Grimoire.LocalPlayerId = stable;
                AnnounceUnlocks(); // the host answers IsUnlocked truthfully (netcode §1)
                AnnounceInk();     // ink drawn before connecting shows up too
            }

            _sendTimer -= Time.unscaledDeltaTime;
            if (_sendTimer <= 0f)
            {
                _sendTimer = 0.05f; // 20 Hz presence
                SendLocalState();
            }
            PruneLive();
            if (NetGame.IsHost) FanOutPresence(); // near friends at once, far ones on their clock

            // the outfit is static - announce it ONCE per connection (the
            // lobby outfit picker re-announces via PushLocalOutfit later)
            if (!_outfitSent && LocalId >= 0) // with no id yet the message names nobody and joiners never get it
            {
                _outfitSent = true;
                PushLocalOutfit();
                AnnounceIdentity();
            }

            // the host streams its zombies to everyone (10 Hz, unreliable -
            // an empty snapshot is meaningful too: it clears dead proxies)
            if (NetGame.IsHost)
            {
                _zombieTimer -= Time.unscaledDeltaTime;
                if (_zombieTimer <= 0f)
                {
                    _zombieTimer = 0.1f;
                    SendZombieSnap();
                    SendGolemSnap();   // nature's own, same beat
                    TickRiders();      // ridden bodies that died or tumbled let go
                    SendMatterSnap();   // host-simulated matter (netcode §3)
                    SendParticleSnap(); // host-simulated particles (netcode §3)
                    SendPropSnap();     // lifted/torn scene props (netcode §4)
                    FlushZoneForces();  // spell forces held on remote bodies
                }
            }
            else TickHeldForce();
        }

        void AnnounceUnlocks()
        {
            foreach (var c in Grimoire.CardsOf(Grimoire.LocalPlayerId))
                PushUnlock(Grimoire.LocalPlayerId, (int)c, -1);
            foreach (var r in Grimoire.RunesOf(Grimoire.LocalPlayerId))
                PushUnlock(Grimoire.LocalPlayerId, -1, (int)r);
        }

        // reusable snapshot buffers - resized only when the horde count changes, not 10 Hz garbage
        int[] _snapIds = System.Array.Empty<int>();
        Vector3[] _snapPos = System.Array.Empty<Vector3>();
        Quaternion[] _snapRot = System.Array.Empty<Quaternion>();
        Vector3[] _snapVel = System.Array.Empty<Vector3>();
        byte[] _snapKinds = System.Array.Empty<byte>();
        Vector3[] _snapScale = System.Array.Empty<Vector3>();
        int[] _snapOwner = System.Array.Empty<int>();
        byte[] _snapTranced = System.Array.Empty<byte>();
        Color32[] _snapTint = System.Array.Empty<Color32>();
        byte[] _snapLook = System.Array.Empty<byte>();
        sbyte[] _snapPhase = System.Array.Empty<sbyte>();
        byte[] _snapAnim = System.Array.Empty<byte>();
        byte[] _snapEyes = System.Array.Empty<byte>();
        byte[] _snapCond = System.Array.Empty<byte>();
        byte[] _snapHp = System.Array.Empty<byte>();
        Vector3[] _snapGaze = System.Array.Empty<Vector3>();
        byte[] _snapBurden = System.Array.Empty<byte>();
        byte[] _gLook = System.Array.Empty<byte>();
        int[] _gIds = System.Array.Empty<int>();
        Vector3[] _gPos = System.Array.Empty<Vector3>();
        Quaternion[] _gRot = System.Array.Empty<Quaternion>();
        Vector3[] _gVel = System.Array.Empty<Vector3>();
        Vector3[] _gScale = System.Array.Empty<Vector3>();
        Color[] _gSkin = System.Array.Empty<Color>();
        int[] _gOwner = System.Array.Empty<int>();
        sbyte[] _gPhase = System.Array.Empty<sbyte>();
        byte[] _gEyes = System.Array.Empty<byte>();
        byte[] _gCond = System.Array.Empty<byte>();
        byte[] _gHp = System.Array.Empty<byte>();
        byte[] _gBeat = System.Array.Empty<byte>();
        Vector3[] _gGaze = System.Array.Empty<Vector3>();
        byte[] _gBurden = System.Array.Empty<byte>();

        /// The eyes as the snapshots carry them: mood, red pupils, the charge swell.
        static byte EyeBits(GooglyEyes eyes)
        {
            if (eyes == null) return 0;
            return (byte)(((int)eyes.Mood & 7) | (eyes.PupilTinted ? 8 : 0) | (eyes.Swelling ? 16 : 0));
        }

        /// Where a held mood is staring; zero while the mood is neutral, when
        /// the host's own AutoWatch is steering and the stand-in's may too.
        static Vector3 GazeOf(GooglyEyes eyes) =>
            eyes != null && eyes.Mood != EyeMood.Neutral ? eyes.LookTarget : Vector3.zero;

        static byte BurdenOf(Element dmg) =>
            (byte)Mathf.RoundToInt(Mathf.Clamp01(dmg != null ? dmg.Burden01 : 0f) * 100f);

        /// Burning, frozen, glowing, ridden - what the stand-in has to wear on top of its pose.
        /// Health as a byte of full strength: the stand-in's blood drips read it.
        static byte HpByte(Element el) =>
            el == null || el.MaxStrength <= 0f ? (byte)255
            : (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(el.Health / el.MaxStrength));

        static byte CondBits(Creature body, bool ridden, bool glows = false)
        {
            int c = (ridden ? 8 : 0) | (glows ? 4 : 0);
            if (body != null) c |= (body.Burning ? 1 : 0) | (body.Frozen ? 2 : 0);
            return (byte)c;
        }

        static sbyte PhaseOf(StateView view) =>
            (sbyte)Mathf.RoundToInt(Mathf.Clamp01(view != null ? view.StateT : StateView.Solid) * 100f);

        void SendZombieSnap()
        {
            int n = Zombie.All.Count;
            if (_snapIds.Length != n) // receivers read Ids.Length - size must match exactly
            {
                _snapIds = new int[n];
                _snapPos = new Vector3[n];
                _snapRot = new Quaternion[n];
                _snapVel = new Vector3[n];
                _snapKinds = new byte[n];
                _snapScale = new Vector3[n];
                _snapOwner = new int[n];
                _snapTranced = new byte[n];
                _snapTint = new Color32[n];
                _snapLook = new byte[n];
                _snapPhase = new sbyte[n];
                _snapAnim = new byte[n];
                _snapEyes = new byte[n];
                _snapCond = new byte[n];
                _snapHp = new byte[n];
                _snapGaze = new Vector3[n];
                _snapBurden = new byte[n];
            }
            bool oneShot = false;
            for (int i = 0; i < n; i++)
            {
                var z = Zombie.All[i];
                if (z == null) // zero the slot - reused buffers would otherwise leak a stale zombie
                {
                    _snapIds[i] = 0; _snapPos[i] = default; _snapRot[i] = Quaternion.identity; _snapVel[i] = default;
                    _snapKinds[i] = 0; _snapScale[i] = Vector3.one; _snapOwner[i] = -1;
                    _snapTranced[i] = 0; _snapTint[i] = default; _snapLook[i] = 255;
                    _snapPhase[i] = 100; _snapAnim[i] = 0; _snapEyes[i] = 0; _snapCond[i] = 0; _snapHp[i] = 255;
                    _snapGaze[i] = default; _snapBurden[i] = 0;
                    continue;
                }
                _snapTranced[i] = (byte)(z.Tranced ? 1 : 0);
                _snapIds[i] = z.gameObject.GetInstanceID();
                _snapPos[i] = z.transform.position;
                _snapRot[i] = z.transform.rotation;
                var rb = z.GetComponent<Rigidbody>();
                _snapVel[i] = rb != null ? rb.linearVelocity : Vector3.zero;
                // one body, but MELEE VS RANGED still has to reach every
                // screen: it is the colour that tells a player which zombie
                // is about to throw something at them
                var sz = z.GetComponent<SummonedZombie>();
                _snapKinds[i] = (byte)((sz != null && sz.Ranged ? 1 : 0) | (z.IsDemon ? 2 : 0));
                _snapScale[i] = z.transform.localScale;
                _snapOwner[i] = sz != null ? sz.SummonedBy : -1;
                // the colour and the spell skin its StateView wears, as painted here
                var view = z.GetComponent<StateView>();
                _snapTint[i] = view != null && view.DriveTint ? (Color32)view.Tint
                    : (Color32)(sz != null && sz.Ranged ? DrawingConfig.SummonRangedColor : DrawingConfig.SummonMeleeColor);
                _snapLook[i] = SpellBook.Live.CreatureIndex(sz != null ? sz.Creature : null);
                _snapPhase[i] = PhaseOf(view);
                // the one-shots since the last beat, then the latch clears
                _snapAnim[i] = z.AnimLatch;
                z.AnimLatch = 0;
                if (_snapAnim[i] != 0) oneShot = true;
                var brain = z.GetComponent<ZombieBrain>();
                var eyes = brain != null ? brain.Eyes : z.GetComponentInChildren<GooglyEyes>();
                _snapEyes[i] = EyeBits(eyes);
                var buff = z.GetComponent<ZombieBuff>();
                if (buff != null && buff.Active) // the buff colours the pupils, in place of the ridden red
                    _snapEyes[i] = (byte)((_snapEyes[i] & ~8) | buff.EyeBit);
                _snapGaze[i] = GazeOf(eyes);
                var demon = z.GetComponent<Demon>();
                _snapCond[i] = CondBits(z.GetComponent<Creature>(), z.Possessed, demon != null && demon.Glows);
                _snapHp[i] = HpByte(z.GetComponent<Element>());
                _snapBurden[i] = BurdenOf(z.GetComponent<Element>());
            }
            // who died since the last beat, named to every client: a beat
            // carries only the zombies near its receiver, so absence is not death
            _snapGone.Clear();
            foreach (int old in _snapPrevIds) if (System.Array.IndexOf(_snapIds, old) < 0) _snapGone.Add(old);
            _snapPrevIds.Clear();
            int live = 0;
            for (int i = 0; i < n; i++) if (_snapIds[i] != 0) { _snapPrevIds.Add(_snapIds[i]); live++; }
            int[] gone = _snapGone.Count > 0 ? _snapGone.ToArray() : System.Array.Empty<int>();

            var snap = new ZombieSnap { Ids = _snapIds, Pos = _snapPos, Rot = _snapRot, Vel = _snapVel,
                Kinds = _snapKinds, Scale = _snapScale, Owner = _snapOwner, Tranced = _snapTranced,
                Tint = _snapTint, Look = _snapLook, Phase = _snapPhase, Anim = _snapAnim,
                Eyes = _snapEyes, Cond = _snapCond, Hp = _snapHp, Gaze = _snapGaze, Burden = _snapBurden, Gone = gone };
            // a beat carrying a one-shot or a death must land: an attack that never showed is a bite from nowhere
            var channel = oneShot || gone.Length > 0 ? Channel.Reliable : Channel.Unreliable;
            float now = Time.unscaledTime;
            float near2 = DrawingConfig.ZombieNearMeters * DrawingConfig.ZombieNearMeters;
            float farEvery = 1f / Mathf.Max(0.2f, DrawingConfig.ZombieFarHz);
            foreach (var conn in InstanceFinder.ServerManager.Clients.Values)
            {
                if (conn == null || conn.IsLocalClient) continue;
                // every beat for the zombies near this client, ZombieFarHz for the far ones, a one-shot always
                if (!EyeOf(conn.ClientId, out var eye))
                {
                    InstanceFinder.ServerManager.Broadcast(conn, snap, true, channel);
                    continue;
                }
                if (!_zombieNextAt.TryGetValue(conn.ClientId, out var next))
                    _zombieNextAt[conn.ClientId] = next = new Dictionary<int, float>();
                foreach (int old in _snapGone) next.Remove(old);
                _pick.Clear();
                for (int i = 0; i < n; i++)
                {
                    int id = _snapIds[i];
                    if (id == 0) continue;
                    bool due = _snapAnim[i] != 0 || (_snapPos[i] - eye).sqrMagnitude < near2
                        || !next.TryGetValue(id, out float at) || now >= at;
                    if (!due) continue;
                    next[id] = now + farEvery;
                    _pick.Add(i);
                }
                if (_pick.Count == live) { InstanceFinder.ServerManager.Broadcast(conn, snap, true, channel); continue; }
                InstanceFinder.ServerManager.Broadcast(conn, new ZombieSnap
                {
                    Ids = Gather(_snapIds), Pos = Gather(_snapPos), Rot = Gather(_snapRot), Vel = Gather(_snapVel),
                    Kinds = Gather(_snapKinds), Scale = Gather(_snapScale), Owner = Gather(_snapOwner),
                    Tranced = Gather(_snapTranced), Tint = Gather(_snapTint), Look = Gather(_snapLook),
                    Phase = Gather(_snapPhase), Anim = Gather(_snapAnim), Eyes = Gather(_snapEyes),
                    Cond = Gather(_snapCond), Gaze = Gather(_snapGaze), Burden = Gather(_snapBurden), Gone = gone
                }, true, channel);
            }
        }

        /// The picked rows of a snapshot column: one client's beat.
        T[] Gather<T>(T[] column)
        {
            var rows = new T[_pick.Count];
            for (int i = 0; i < rows.Length; i++) rows[i] = column[_pick[i]];
            return rows;
        }

        void SendGolemSnap()
        {
            // a carried golem is disabled (out of All) but still on the field
            int free = Golem.All.Count;
            int n = free + Golem.Carried.Count;
            if (_gIds.Length != n) // receivers read Ids.Length - size must match exactly
            {
                _gIds = new int[n];
                _gPos = new Vector3[n];
                _gRot = new Quaternion[n];
                _gVel = new Vector3[n];
                _gScale = new Vector3[n];
                _gSkin = new Color[n];
                _gOwner = new int[n];
                _gLook = new byte[n];
                _gPhase = new sbyte[n];
                _gEyes = new byte[n];
                _gCond = new byte[n];
                _gHp = new byte[n];
                _gBeat = new byte[n];
                _gGaze = new Vector3[n];
                _gBurden = new byte[n];
            }
            for (int i = 0; i < n; i++)
            {
                var g = i < free ? Golem.All[i] : Golem.Carried[i - free];
                if (g == null) // zero the slot - a reused buffer would leak a stale golem
                {
                    _gIds[i] = 0; _gPos[i] = default; _gRot[i] = Quaternion.identity; _gVel[i] = default;
                    _gScale[i] = Vector3.one; _gSkin[i] = Color.gray; _gOwner[i] = -1;
                    _gLook[i] = 255; _gPhase[i] = 100; _gEyes[i] = 0; _gCond[i] = 0; _gBeat[i] = 0; _gHp[i] = 255;
                    _gGaze[i] = default; _gBurden[i] = 0;
                    continue;
                }
                _gIds[i] = g.gameObject.GetInstanceID();
                _gPos[i] = g.transform.position;
                _gRot[i] = g.transform.rotation;
                var rb = g.GetComponent<Rigidbody>();
                _gVel[i] = rb != null ? rb.linearVelocity : Vector3.zero;
                _gScale[i] = g.transform.localScale;
                // the tint its StateView wears - Wear() shades the stone by the spell
                var view = g.GetComponent<StateView>();
                _gSkin[i] = view != null && view.DriveTint ? view.Tint : g.Skin;
                _gOwner[i] = g.OwnerId;
                _gLook[i] = SpellBook.Live.CreatureIndex(g.Worn);
                _gPhase[i] = PhaseOf(view);
                _gEyes[i] = EyeBits(g.Eyes);
                _gGaze[i] = GazeOf(g.Eyes);
                _gCond[i] = CondBits(g.GetComponent<Creature>(), g.Possessed);
                _gHp[i] = HpByte(g.GetComponent<Element>());
                _gBeat[i] = g.ChargeBeat;
                _gBurden[i] = BurdenOf(g.GetComponent<Element>());
            }
            InstanceFinder.ServerManager.Broadcast(new GolemSnap
            { Ids = _gIds, Pos = _gPos, Rot = _gRot, Vel = _gVel, Scale = _gScale, Skin = _gSkin, Owner = _gOwner,
              Look = _gLook, Phase = _gPhase, Eyes = _gEyes, Cond = _gCond, Hp = _gHp, Beat = _gBeat,
              Gaze = _gGaze, Burden = _gBurden },
                true, Channel.Unreliable);
        }

        // reusable matter/particle/prop snapshot buffers - same law as the zombie ones
        int[] _mIds = System.Array.Empty<int>();
        Vector3[] _mPos = System.Array.Empty<Vector3>();
        Quaternion[] _mRot = System.Array.Empty<Quaternion>();
        Vector3[] _mScale = System.Array.Empty<Vector3>();
        byte[] _mMats = System.Array.Empty<byte>();
        byte[] _mPhases = System.Array.Empty<byte>();
        byte[] _mLooks = System.Array.Empty<byte>();
        byte[] _mEdges = System.Array.Empty<byte>();
        Vector3[] _mVels = System.Array.Empty<Vector3>();
        float[] _mSticks = System.Array.Empty<float>();

        void SendMatterSnap()
        {
            var all = Matter.Living;
            int n = all.Count;
            if (_mIds.Length != n)
            {
                _mIds = new int[n];
                _mPos = new Vector3[n];
                _mRot = new Quaternion[n];
                _mScale = new Vector3[n];
                _mMats = new byte[n];
                _mPhases = new byte[n];
                _mLooks = new byte[n];
                _mEdges = new byte[n];
                _mVels = new Vector3[n];
                _mSticks = new float[n];
            }
            for (int i = 0; i < n; i++)
            {
                var m = all[i];
                if (m == null)
                {
                    _mIds[i] = 0;
                    continue;
                }
                _mIds[i] = m.gameObject.GetInstanceID();
                _mPos[i] = m.transform.position;
                _mRot[i] = m.transform.rotation;
                _mScale[i] = m.transform.localScale;
                _mMats[i] = (byte)m.Material;
                _mPhases[i] = (byte)m.Phase;
                _mLooks[i] = m.NetLook;
                _mEdges[i] = (byte)Mathf.Clamp(m.Edges, 0, 255);
                _mVels[i] = m.Body != null ? m.Body.linearVelocity : Vector3.zero; // what LiquidVolume reads
                _mSticks[i] = m.Stickiness;
            }
            InstanceFinder.ServerManager.Broadcast(new MatterSnap
            {
                Ids = _mIds, Pos = _mPos, Rot = _mRot, Scale = _mScale,
                Mats = _mMats, Phases = _mPhases, Looks = _mLooks, Edges = _mEdges,
                Vels = _mVels, Sticks = _mSticks
            }, true, Channel.Unreliable);
        }

        int[] _pIds = System.Array.Empty<int>();
        byte[] _pKinds = System.Array.Empty<byte>();
        Color32[] _pTints = System.Array.Empty<Color32>();
        byte[] _pLevels = System.Array.Empty<byte>();
        int[] _pRides = System.Array.Empty<int>();
        int[] _pOwners = System.Array.Empty<int>();
        Vector3[] _pPos = System.Array.Empty<Vector3>();
        float[] _pScale = System.Array.Empty<float>();
        byte[] _pFlags = System.Array.Empty<byte>();
        float[] _pReach = System.Array.Empty<float>();
        byte[] _pKinds2 = System.Array.Empty<byte>();
        byte[] _pAreas = System.Array.Empty<byte>();
        byte[] _pLooks = System.Array.Empty<byte>();

        void SendParticleSnap()
        {
            var all = SpellParticle.Living;
            int n = all.Count;
            if (_pIds.Length != n)
            {
                _pIds = new int[n];
                _pKinds = new byte[n];
                _pTints = new Color32[n];
                _pLevels = new byte[n];
                _pRides = new int[n];
                _pOwners = new int[n];
                _pPos = new Vector3[n];
                _pScale = new float[n];
                _pFlags = new byte[n];
                _pReach = new float[n];
                _pKinds2 = new byte[n];
                _pAreas = new byte[n];
                _pLooks = new byte[n];
            }
            for (int i = 0; i < n; i++)
            {
                var p = all[i];
                if (p == null)
                {
                    _pIds[i] = 0;
                    continue;
                }
                _pIds[i] = p.gameObject.GetInstanceID();
                // ITS LOOK IS ITS NUMBERS, and the client has no numbers -
                // so the shape and the colour have to travel. Sending the KIND
                // was enough when a kind was all a particle was.
                _pKinds[i] = p.ShapeId;
                _pTints[i] = p.ShownTint;
                _pLevels[i] = p.WireLevel;
                _pRides[i] = p.RidingId;
                _pOwners[i] = p.OwnerId;
                _pPos[i] = p.transform.position;
                _pScale[i] = p.transform.localScale.x;
                _pFlags[i] = p.WireFlags;
                _pReach[i] = p.WireLevel >= 3 ? p.AuraRadius : 0f;
                _pKinds2[i] = (byte)p.Kind;
                _pAreas[i] = p.WireArea;
                _pLooks[i] = SpellBook.Live.IndexOf(p.SkinRow);
            }
            InstanceFinder.ServerManager.Broadcast(new ParticleSnap
                { Ids = _pIds, Kinds = _pKinds, Tints = _pTints, Levels = _pLevels,
                  Rides = _pRides, Owners = _pOwners, Pos = _pPos, Scale = _pScale,
                  Flags = _pFlags, Reach = _pReach, Kinds2 = _pKinds2, Areas = _pAreas, Looks = _pLooks },
                true, Channel.Unreliable);
        }

        int[] _prIds = System.Array.Empty<int>();
        Vector3[] _prPos = System.Array.Empty<Vector3>();
        Quaternion[] _prRot = System.Array.Empty<Quaternion>();

        void SendPropSnap()
        {
            // drop dead/settled props - a sleeping prop stays where clients last saw it
            for (int i = _trackedProps.Count - 1; i >= 0; i--)
                if (_trackedProps[i] == null || _trackedProps[i].IsSleeping())
                {
                    // the last unreliable beat may never land: the rest pose goes reliably
                    if (_trackedProps[i] != null)
                        InstanceFinder.ServerManager.Broadcast(new PropSnap
                        {
                            Ids = new[] { _trackedPropIds[i] },
                            Pos = new[] { _trackedProps[i].transform.position },
                            Rot = new[] { _trackedProps[i].transform.rotation },
                        }, true, Channel.Reliable);
                    _trackedProps.RemoveAt(i);
                    _trackedPropIds.RemoveAt(i);
                }
            int n = _trackedProps.Count;
            if (_prIds.Length != n)
            {
                _prIds = new int[n];
                _prPos = new Vector3[n];
                _prRot = new Quaternion[n];
            }
            for (int i = 0; i < n; i++)
            {
                _prIds[i] = _trackedPropIds[i];
                _prPos[i] = _trackedProps[i].transform.position;
                _prRot[i] = _trackedProps[i].transform.rotation;
            }
            InstanceFinder.ServerManager.Broadcast(new PropSnap
                { Ids = _prIds, Pos = _prPos, Rot = _prRot }, true, Channel.Unreliable);
        }

        /// A scene prop went dynamic under a wizard's ink - clients must see it move (netcode §4).
        public static void TrackProp(Rigidbody rb)
        {
            if (rb == null || !NetGame.IsHost || !NetGame.Connected) return;
            if (rb.GetComponent<Matter>() != null) return; // matter rides MatterSnap
            if (_trackedProps.Contains(rb)) return;
            int id = _nextPropId++;
            _trackedProps.Add(rb);
            _trackedPropIds.Add(id);
            string path = FullPath(rb.transform);
            int k = _everProps.IndexOf(rb);
            if (k >= 0) _everPropIds[k] = id;
            else { _everProps.Add(rb); _everPropIds.Add(id); _everPropPaths.Add(path); }
            InstanceFinder.ServerManager.Broadcast(new PropReg { Id = id, Path = path });
        }

        /// TrackProp next frame: a piece born inside a death must reach the
        /// clients after the HealthMsg that spawns their copy of it.
        public static void TrackPropLater(Rigidbody rb)
        {
            if (rb == null || _instance == null || !NetGame.IsHost || !NetGame.Connected) return;
            _instance.StartCoroutine(_instance.TrackNextFrame(rb));
        }

        System.Collections.IEnumerator TrackNextFrame(Rigidbody rb)
        {
            yield return null;
            TrackProp(rb);
        }

        // ---- host physics drive for remote holds (netcode §4) ----
        void FixedUpdate()
        {
            if (!NetGame.IsHost || _holds.Count == 0) return;
            _holdGone.Clear();
            foreach (var kv in _holds)
            {
                var h = kv.Value;
                if (h.Mote != null)
                {
                    if (h.Mote.Dead || !h.Mote.Claimed)
                    {
                        _holdGone.Add(kv.Key);
                        NotifyHoldLost(kv.Key, "what you held is gone, merged or spent");
                        continue;
                    }
                    if (h.HasAim)
                        h.Mote.transform.position = Vector3.Lerp(
                            h.Mote.transform.position, h.Hand, 14f * Time.fixedDeltaTime);
                    continue;
                }
                if (h.Body == null)
                {
                    _holdGone.Add(kv.Key);
                    NotifyHoldLost(kv.Key, "what you held is gone, merged or spent");
                    continue;
                }
                // the lobby hides a broken prop instead of destroying it: the hand opens all the same
                if (LobbyRespawn.IsHidden(h.Body))
                {
                    ReleaseHeldBody(h, Vector3.zero);
                    _holdGone.Add(kv.Key);
                    NotifyHoldLost(kv.Key, "what you held is gone, merged or spent");
                    continue;
                }
                if (!h.HasAim) continue;

                // the LevitateTick law with the REMOTE owner's ink (HandGrab mirrors this)
                float auth = HandGrab.AuthorityFor(h.Body, h.Marks, h.Owner, out float share);
                if (auth <= 0f)
                {
                    ReleaseHeldBody(h, Vector3.zero);
                    _holdGone.Add(kv.Key);
                    NotifyHoldLost(kv.Key, "your ink is gone, it drops");
                    continue;
                }
                Vector3 delta = h.Hand - h.Body.worldCenterOfMass;
                float accel = Mathf.Lerp(4f, 90f, auth) * share;
                Vector3 target = Vector3.ClampMagnitude(delta * 8f, Mathf.Lerp(2.5f, 14f, auth));
                h.Body.linearVelocity = Vector3.MoveTowards(
                    h.Body.linearVelocity, target, accel * Time.fixedDeltaTime);
                h.Body.useGravity = false;
                h.Body.AddForce(Physics.gravity * (1f - auth), ForceMode.Acceleration);
                if (auth < 1f) continue; // can't lift it = can't turn it
                float turn = Mathf.Lerp(2f, 12f, auth) * Mathf.Clamp01(10f / Mathf.Max(1f, h.Body.mass));
                HandGrab.TurnAboutCenter(h.Body, h.Rot * h.RelRot, turn);
            }
            foreach (var k in _holdGone) _holds.Remove(k);
        }

        static void ReleaseHeldBody(RemoteHold h, Vector3 impulse, int thrower = -1)
        {
            if (h.Body == null) return;
            h.Body.GetComponentInParent<Golem>()?.BeReleased(); // wakes back up on release (parity)
            var z = h.Body.GetComponentInParent<Zombie>();
            if (z != null) z.Held = false;
            if (!h.Body.isKinematic)
            {
                h.Body.linearVelocity = Vector3.ClampMagnitude(h.Body.linearVelocity, 4f);
                h.Body.angularVelocity = Vector3.ClampMagnitude(h.Body.angularVelocity, 4f);
                var sm = h.Body.GetComponent<Matter>();
                if (sm != null && sm.SpellBorn) impulse *= DrawingConfig.SpellThrowMul; // same law as the local hand
                if (impulse != Vector3.zero) h.Body.AddForce(impulse, ForceMode.VelocityChange);
                if (thrower >= 0) Homing.Arm(h.Body, thrower); // drawn to enemies ahead as it flies, like the local hand's
            }
            h.Body.useGravity = h.HadGravity;
            var m = h.Body.GetComponent<Matter>();
            if (m != null) m.Touched = true; // TOUCH = WORLD
        }

        /// The host force-released a client's hold - tell their hand to open (netcode §4).
        void NotifyHoldLost(int clientId, string why)
        {
            if (InstanceFinder.ServerManager.Clients.TryGetValue(clientId, out var conn))
                Ack(conn, false, why);
        }

        void ReleaseHold(int clientId, Vector3 impulse, bool wake = false)
        {
            if (!_holds.TryGetValue(clientId, out var h)) return;
            _holds.Remove(clientId);
            Transform thrower = _avatars.TryGetValue(clientId, out var av) && av != null ? av.transform : null;
            if (h.Mote != null)
            {
                if (!h.Mote.Dead && h.Mote.Claimed)
                {
                    // ★ SAME LAW AS THE LOCAL HAND (parity): E primes (detonate
                    // on impact, thrower briefly immune), gusts and wakes at
                    // once; F wakes in the hand and gusts; a plain drop only
                    // lets go, and it wakes on its own timer where it lies
                    bool thrown = impulse.sqrMagnitude > 1f;
                    int owner = OwnerIdOf(clientId);
                    h.Mote.ReleaseHeld(impulse);
                    if (thrown)
                    {
                        h.Mote.PrimeToBlow(thrower);
                        SpellKick.Apply(h.Mote, impulse, thrower, owner);
                        h.Mote.Wake();
                    }
                    else if (wake)
                    {
                        h.Mote.Wake();
                        SpellKick.Apply(h.Mote, Vector3.zero, thrower, owner);
                    }
                }
                return;
            }
            ReleaseHeldBody(h, impulse, impulse.sqrMagnitude > 1f ? OwnerIdOf(clientId) : -1);
        }

        // ------------------------------------------------ the kick over the wire --
        /// A spell's kick on a player: this machine's own body takes it here,
        /// a friend's body takes it on theirs (the host hands it out).
        public static void SendKick(int owner, Vector3 impulse, bool knock, bool blast = false)
        {
            if (owner < 0) return;
            if (owner == Grimoire.LocalPlayerId) { ApplyKickLocal(impulse, knock, blast); return; }
            if (_instance == null || !NetGame.Connected || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new KickMsg { Owner = owner, Impulse = impulse, Knock = knock, Blast = blast });
        }

        /// HOST: an acolyte's curse landed on a wizard. Every machine keeps what
        /// it implies for the books; the cursed player's own machine runs the rest.
        public static void SendCurse(int owner, byte kind, float seconds, int by = -1, string shape = "")
        {
            if (owner < 0) return;
            var msg = new CurseMsg { Owner = owner, Kind = kind, Seconds = seconds, By = by, Shape = shape ?? "" };
            ApplyCurseEverywhere(msg);
            if (owner == Grimoire.LocalPlayerId) ApplyCurseLocal(msg);
            if (_instance == null || !NetGame.Connected || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(msg);
        }

        void OnCurseClient(CurseMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            ApplyCurseEverywhere(msg);
            if (msg.Owner == Grimoire.LocalPlayerId) ApplyCurseLocal(msg);
        }

        /// The part every machine must agree on: whose book is in whose hands.
        static void ApplyCurseEverywhere(CurseMsg msg)
        {
            switch ((MischiefKind)msg.Kind)
            {
                case MischiefKind.LifeNeedle: Grimoires.Curse(msg.Owner, msg.By, msg.Seconds); break;
                case MischiefKind.Restore: Grimoires.Restore(msg.Owner); break;
            }
        }

        /// The cursed player's own machine: legs, death, ink, shape, silence.
        static void ApplyCurseLocal(CurseMsg msg)
        {
            var p = LocalPilot();
            if (p == null) return;
            switch ((MischiefKind)msg.Kind)
            {
                case MischiefKind.Decoy: p.Decoy(msg.Seconds); break;
                case MischiefKind.DeathNeedle:
                    p.DieOutright();
                    GhostState.ReviveIn(msg.Seconds);
                    DrawingWorld.Instance?.LogEvent("a needle. a ghost for a minute");
                    break;
                case MischiefKind.LifeNeedle:
                    DrawingWorld.Instance?.LogEvent("your grimoire is not yours now. it is theirs");
                    break;
                case MischiefKind.Restore:
                    DrawingWorld.Instance?.LogEvent("your own book again");
                    break;
                case MischiefKind.Evaporation:
                    p.GetComponent<PlayerInk>()?.Evaporate(msg.Seconds); // the amount rides Seconds
                    break;
                case MischiefKind.Transformation:
                    ShapeShift.WearKey(msg.Shape, msg.Seconds);
                    VoiceChat.GagUntil = Time.time + msg.Seconds;
                    break;
            }
        }

        /// HOST: a zombie or a prop wears another object's look for a while (the transformation ink).
        public static void PushWorn(byte kind, int id, string path, string shape, float seconds)
        {
            if (_instance == null || !NetGame.Connected || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new WornMsg
                { Kind = kind, Id = id, Path = path ?? "", Shape = shape ?? "", Seconds = seconds });
        }

        void OnWornClient(WornMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Kind == 1)
            {
                if (_proxies.TryGetValue(msg.Id, out var proxy) && proxy != null)
                    WornLook.Put(proxy.gameObject, msg.Shape, msg.Seconds);
                return;
            }
            var go = ScenePath.Find(msg.Path);
            if (go != null) WornLook.Put(go, msg.Shape, msg.Seconds);
        }

        void OnKickClient(KickMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Owner != Grimoire.LocalPlayerId) return;
            if (msg.Kind == 1) HoldForce(msg.Impulse, msg.Seconds, msg.Lum);
            else ApplyKickLocal(msg.Impulse, msg.Knock, msg.Blast);
        }

        static SimpleFPSController LocalPilot()
        {
            foreach (var p in SimpleFPSController.All)
                if (p != null && p.IsLocalViewer) return p;
            return null;
        }

        static void ApplyKickLocal(Vector3 impulse, bool knock, bool blast = false)
        {
            var p = LocalPilot();
            if (p == null || p.IsDowned) return;
            if (blast) Shove.Hit(p, impulse, 0f, "kick"); // a big blast breaks modes here too, as on the host
            else p.TakeHit(impulse, 0f, "kick");
            if (knock) p.KnockDown(1.1f);
        }

        // ---- continuous spell forces on a remote body (zones, pulls, black holes) --
        // HOST: this beat's acceleration per owner, flushed at 10 Hz as KickMsg Kind 1
        static readonly Dictionary<int, Vector3> _zoneForce = new Dictionary<int, Vector3>();
        static readonly Dictionary<int, float> _zoneLum = new Dictionary<int, float>();
        static readonly List<int> _zoneOwners = new List<int>();

        /// HOST: an acceleration a spell puts on a player's body this frame.
        /// The local pilot takes it now; a remote one gets it on the beat.
        public static void AccumulateForce(int owner, Vector3 accel)
        {
            if (owner < 0 || accel.sqrMagnitude < 0.0001f) return;
            if (owner == Grimoire.LocalPlayerId)
            {
                LocalPilot()?.AddSpellForce(accel, Time.deltaTime);
                return;
            }
            if (!NetGame.IsHost || !NetGame.Connected) return;
            _zoneForce.TryGetValue(owner, out var sum);
            _zoneForce[owner] = sum + accel * Time.deltaTime; // integrated over the beat
        }

        public static void AccumulateLum(int owner, float lum)
        {
            if (owner < 0 || Mathf.Abs(lum) < 0.00001f) return;
            if (owner == Grimoire.LocalPlayerId)
            {
                var p = LocalPilot();
                if (p != null) BodyState.Of(p)?.PushLum(lum);
                return;
            }
            if (!NetGame.IsHost || !NetGame.Connected) return;
            _zoneLum.TryGetValue(owner, out var sum);
            _zoneLum[owner] = sum + lum;
        }

        const float ZoneBeat = 0.1f;

        void FlushZoneForces()
        {
            if (_zoneForce.Count == 0 && _zoneLum.Count == 0) return;
            _zoneOwners.Clear();
            foreach (var kv in _zoneForce) _zoneOwners.Add(kv.Key);
            foreach (var kv in _zoneLum) if (!_zoneForce.ContainsKey(kv.Key)) _zoneOwners.Add(kv.Key);
            foreach (int owner in _zoneOwners)
            {
                _zoneForce.TryGetValue(owner, out var dv);
                _zoneLum.TryGetValue(owner, out var lum);
                InstanceFinder.ServerManager.Broadcast(new KickMsg
                {
                    Owner = owner, Kind = 1, Impulse = dv / ZoneBeat, Seconds = ZoneBeat * 1.5f, Lum = lum,
                }, true, Channel.Unreliable);
            }
            _zoneForce.Clear();
            _zoneLum.Clear();
        }

        // CLIENT: the acceleration the host says a spell holds on my body
        static Vector3 _heldAccel;
        static float _heldUntil;

        static void HoldForce(Vector3 accel, float seconds, float lum)
        {
            _heldAccel = accel;
            _heldUntil = Time.time + Mathf.Clamp(seconds, 0.05f, 1f);
            if (Mathf.Abs(lum) > 0.00001f)
            {
                var p = LocalPilot();
                if (p != null) BodyState.Of(p)?.PushLum(lum);
            }
        }

        void TickHeldForce()
        {
            if (Time.time >= _heldUntil) return;
            var p = LocalPilot();
            if (p == null || p.IsDowned) return;
            p.AddSpellForce(_heldAccel, Time.deltaTime);
        }

        // ------------------------------------------------ effects over the wire --
        /// True while this machine should ship the effects its sim makes.
        /// FxQuiet marks stretches where the clients already replay the same
        /// thing on their own (a relayed message, a mirrored field).
        public static bool WantsFxRelay => !FxQuiet && _instance != null && NetGame.IsHost && NetGame.Connected;
        public static bool FxQuiet;

        /// HOST: an effect or a sound, exactly as the helper made it here.
        public static void PushFx(byte kind, Vector3 at, Vector3 aux, Color tint, int parent,
            float life, byte flags, float scale = 1f, bool reliable = false)
        {
            if (!WantsFxRelay || kind == FxLibrary.FxNone) return;
            InstanceFinder.ServerManager.Broadcast(new FxMsg
            {
                Kind = kind, At = at, Aux = aux, Scale = scale, Tint = tint,
                Parent = parent, Life = life, Flags = flags,
            }, true, reliable ? Channel.Reliable : Channel.Unreliable);
        }

        void OnFxClient(FxMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            var carrier = msg.Parent != 0 ? Element.ById(msg.Parent) : null;
            var parent = carrier != null ? carrier.transform : null;
            int n = Mathf.Clamp(Mathf.RoundToInt(msg.Life), 1, 64);
            switch (msg.Kind)
            {
                case FxLibrary.FxPuff: GrammarFX.PuffBurst(msg.At, msg.Tint, n, false); break;
                case FxLibrary.FxFireBloom: GrammarFX.FireBloom(msg.At, n, msg.Scale, msg.Aux.x, false); break;
                case FxLibrary.FxFireCone: Spell.SpawnBurst(msg.At, msg.Aux, n, msg.Scale, false); break;
                case FxLibrary.FxBolt: SpellParticle.Bolt(msg.At, msg.Aux, false); break;
                case FxLibrary.FxLantern: GrammarFX.Lantern(parent, msg.At, msg.Scale, false); break;
                case FxLibrary.FxGlint: GrammarFX.Glint(msg.At, msg.Tint, false); break;
                case FxLibrary.FxCometDown:
                    // the ink falls onto a pot; the fill itself arrives by PotMsg
                    if (parent != null)
                        SkyBeam.Down(parent, msg.Tint, () =>
                        {
                            if (parent != null && !Juice.Sound(Sfx.PotFromTheSky, parent.position)) Juice.Chime(parent.position);
                        });
                    else SkyBeam.Down(msg.At, msg.Tint);
                    break;
                case FxLibrary.SndBoom:
                case FxLibrary.SndPop:
                case FxLibrary.SndWhoosh:
                case FxLibrary.SndCrackle:
                case FxLibrary.SndThud:
                case >= FxLibrary.SndClips and < FxLibrary.FxNone:
                    Juice.PlayWire(msg.Kind, msg.At, msg.Scale, msg.Life, parent);
                    if (Juice.IsBlastWire(msg.Kind) && Camera.main != null)
                    {
                        // the shake stays a local feel: by distance, as SpellDebris does
                        float power = Mathf.Max(0f, (msg.Scale - 0.6f) / 0.3f);
                        float near = Mathf.Clamp01(1f - Vector3.Distance(Camera.main.transform.position, msg.At) / 14f);
                        if (near > 0f) Juice.Shake(Mathf.Min(1f, 0.4f + power * 0.3f) * near, 0.3f);
                    }
                    break;
                default:
                    var prefab = FxLibrary.PrefabAt(msg.Kind);
                    if (prefab == null) return;
                    float life = Mathf.Max(0.05f, msg.Life);
                    var fx = (msg.Flags & 1) != 0
                        ? FxLibrary.SpawnTinted(prefab, msg.At, msg.Tint, parent, life, false)
                        : FxLibrary.Spawn(prefab, msg.At, parent, life, false);
                    if (fx != null && msg.Aux.sqrMagnitude > 0.000001f) fx.transform.localScale = msg.Aux;
                    break;
            }
        }

        // ---------------------------------------------- revive over the wire --
        /// A living player standing at a remote friend's body. Only the owner
        /// of that body can stand it up, so the presence travels to them.
        public static void SendReviveTick(int target, float dt)
        {
            if (_instance == null || !NetGame.Connected || target < 0 || dt <= 0f) return;
            var msg = new ReviveTickMsg { Rescuer = Grimoire.LocalPlayerId, Target = target, Dt = dt };
            if (NetGame.IsHost) _instance.RouteReviveTick(msg);
            else InstanceFinder.ClientManager.Broadcast(msg);
        }

        void OnReviveTickServer(NetworkConnection conn, ReviveTickMsg msg, Channel channel)
        {
            if (msg.Rescuer != OwnerIdOf(conn.ClientId)) return; // you rescue as yourself only
            RouteReviveTick(msg);
        }

        void RouteReviveTick(ReviveTickMsg msg)
        {
            if (msg.Target == Grimoire.LocalPlayerId) GhostState.ApplyRemoteRevive(msg.Dt, msg.Rescuer);
            else ShinePuppet(msg);
            // the target counts it, every other client shines its puppet
            InstanceFinder.ServerManager.Broadcast(msg);
        }

        void OnReviveTickClient(ReviveTickMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Target == Grimoire.LocalPlayerId) GhostState.ApplyRemoteRevive(msg.Dt, msg.Rescuer);
            else ShinePuppet(msg);
        }

        /// A third machine watching a revive: the shine off the target's
        /// puppet (the rescuer's own machine already shines it).
        void ShinePuppet(ReviveTickMsg msg)
        {
            if (msg.Rescuer == Grimoire.LocalPlayerId) return;
            if (_avatars.TryGetValue(msg.Target - 1, out var av) && av != null) av.ReviveShine();
        }

        /// The revived owner tells the rescuer it worked.
        public static void SendReviveDone(int rescuer)
        {
            if (_instance == null || !NetGame.Connected || rescuer < 0) return;
            var msg = new ReviveDoneMsg { Rescuer = rescuer };
            if (NetGame.IsHost) _instance.RouteReviveDone(msg);
            else InstanceFinder.ClientManager.Broadcast(msg);
        }

        void OnReviveDoneServer(NetworkConnection conn, ReviveDoneMsg msg, Channel channel) => RouteReviveDone(msg);

        void RouteReviveDone(ReviveDoneMsg msg)
        {
            if (msg.Rescuer == Grimoire.LocalPlayerId) Achievements.Unlock(Achievements.ReviveFriend);
            else InstanceFinder.ServerManager.Broadcast(msg);
        }

        void OnReviveDoneClient(ReviveDoneMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Rescuer == Grimoire.LocalPlayerId) Achievements.Unlock(Achievements.ReviveFriend);
        }

        // ------------------------------------------- ghosts riding creatures --
        // The host owns every zombie and golem: a client ghost asks to ride,
        // the host says yes or no, and the reins arrive as intents.
        readonly Dictionary<int, int> _riders = new Dictionary<int, int>(); // creature id, owner
        readonly List<int> _rideGone = new List<int>();

        static Zombie FindZombie(int id)
        {
            foreach (var z in Zombie.All)
                if (z != null && z.gameObject.GetInstanceID() == id) return z;
            return null;
        }

        static Golem FindGolem(int id)
        {
            foreach (var g in Golem.All)
                if (g != null && g.gameObject.GetInstanceID() == id) return g;
            return null;
        }

        public static void SendRideAsk(byte kind, int id, bool on)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new RideAskMsg
                { Owner = Grimoire.LocalPlayerId, Kind = kind, Id = id, On = on });
        }

        public static void SendRideDrive(int id, Vector3 move, Vector3 look, bool ability)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new RideDriveMsg
                { Owner = Grimoire.LocalPlayerId, Id = id, Move = move, Look = look, Ability = ability },
                Channel.Unreliable);
        }

        /// The client-side stand-in of a creature the host named.
        public static Transform RideProxy(byte kind, int id)
        {
            if (_instance == null) return null;
            if (kind == 1 && _instance._proxies.TryGetValue(id, out var z) && z != null) return z.transform;
            if (kind == 2 && _instance._golems.TryGetValue(id, out var g) && g != null) return g.transform;
            return null;
        }

        void OnRideAskServer(NetworkConnection conn, RideAskMsg msg, Channel channel)
        {
            if (msg.Owner != OwnerIdOf(conn.ClientId)) return;
            if (!msg.On) { ReleaseRide(msg.Id, msg.Owner); return; }
            bool ok = false;
            if (msg.Kind == 1)
            {
                var z = FindZombie(msg.Id);
                var mine = z != null ? z.GetComponent<SummonedZombie>() : null;
                var dmg = z != null ? z.GetComponent<Element>() : null;
                if (z != null && !z.Possessed && mine != null && (dmg == null || dmg.Health > 0f)
                    && Sides.Of(mine.SummonedBy) == Side.Acolyte && Sides.Of(msg.Owner) == Side.Acolyte)
                {
                    z.PossessBy(true);
                    ok = true;
                }
            }
            else if (msg.Kind == 2)
            {
                var g = FindGolem(msg.Id);
                if (g != null && !g.Possessed && g.Alive && g.OwnerId >= 0
                    && Sides.Of(g.OwnerId) == Sides.Of(msg.Owner))
                {
                    g.PossessBy(true);
                    ok = true;
                }
            }
            if (!ok) return; // a silent no: the ghost keeps flying
            _riders[msg.Id] = msg.Owner;
            InstanceFinder.ServerManager.Broadcast(new RideGiveMsg
                { Owner = msg.Owner, Kind = msg.Kind, Id = msg.Id, On = true });
        }

        void ReleaseRide(int id, int owner)
        {
            if (!_riders.TryGetValue(id, out var who) || who != owner) return;
            _riders.Remove(id);
            var z = FindZombie(id);
            if (z != null) z.PossessBy(false);
            var g = FindGolem(id);
            if (g != null) g.PossessBy(false);
            InstanceFinder.ServerManager.Broadcast(new RideGiveMsg
                { Owner = owner, Kind = (byte)(z != null ? 1 : 2), Id = id, On = false });
        }

        void OnRideDriveServer(NetworkConnection conn, RideDriveMsg msg, Channel channel)
        {
            if (msg.Owner != OwnerIdOf(conn.ClientId)) return;
            if (!_riders.TryGetValue(msg.Id, out var who) || who != msg.Owner) return;
            Vector3 look = msg.Look.sqrMagnitude > 0.01f ? msg.Look.normalized : Vector3.forward;
            Vector3 flat = look; flat.y = 0f;
            Vector3 move = msg.Move; move.y = 0f;
            bool moving = move.sqrMagnitude > 0.01f;
            var z = FindZombie(msg.Id);
            if (z != null)
            {
                var brain = z.GetComponent<ZombieBrain>();
                if (brain != null)
                {
                    brain.MoveDir = moving ? move.normalized : Vector3.zero;
                    brain.SpeedScale = moving ? 1f : 0f;
                }
                if (flat.sqrMagnitude > 0.01f) z.PossessedFace = flat.normalized;
                if (msg.Ability) z.GhostAbility(look);
                return;
            }
            var g = FindGolem(msg.Id);
            if (g != null)
            {
                g.PossessedMove = moving ? move.normalized : Vector3.zero;
                if (flat.sqrMagnitude > 0.01f) g.PossessedFace = flat.normalized;
                if (msg.Ability) g.GhostAbility(look);
            }
        }

        /// Host, on the snapshot beat: a ridden body that died or tumbled
        /// throws its rider, the rule the host's own ghost already lives by.
        void TickRiders()
        {
            if (_riders.Count == 0) return;
            _rideGone.Clear();
            foreach (var kv in _riders)
            {
                var z = FindZombie(kv.Key);
                if (z != null)
                {
                    var body = z.GetComponent<Creature>();
                    var dmg = z.GetComponent<Element>();
                    bool dead = dmg != null && dmg.Health <= 0f;
                    if (dead || (body != null && (body.Slipping || body.GettingUp))) _rideGone.Add(kv.Key);
                    continue;
                }
                var g = FindGolem(kv.Key);
                if (g == null || !g.Alive) _rideGone.Add(kv.Key);
            }
            foreach (int id in _rideGone)
                if (_riders.TryGetValue(id, out var owner)) ReleaseRide(id, owner);
        }

        void ReleaseRidesOf(int owner)
        {
            _rideGone.Clear();
            foreach (var kv in _riders) if (kv.Value == owner) _rideGone.Add(kv.Key);
            foreach (int id in _rideGone) ReleaseRide(id, owner);
        }

        void OnRideGiveClient(RideGiveMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            GhostState.OnRideGive(msg.Owner, msg.Kind, msg.Id, msg.On);
        }

        void RegisterOnce()
        {
            if (_registered) return;
            _registered = true;

            InstanceFinder.ServerManager.RegisterBroadcast<PlayerState>(OnPlayerStateServer);
            InstanceFinder.ClientManager.RegisterBroadcast<PlayerState>(OnPlayerStateClient);
            InstanceFinder.ServerManager.RegisterBroadcast<StrokeMsg>(OnStrokeServer);
            InstanceFinder.ClientManager.RegisterBroadcast<StrokeMsg>(OnStrokeClient);
            InstanceFinder.ServerManager.RegisterBroadcast<BodyFxMsg>(OnBodyFxServer);
            InstanceFinder.ClientManager.RegisterBroadcast<BodyFxMsg>(OnBodyFxClient);
            InstanceFinder.ServerManager.RegisterBroadcast<EmoteMsg>(OnEmoteServer);
            InstanceFinder.ClientManager.RegisterBroadcast<EmoteMsg>(OnEmoteClient);
            InstanceFinder.ClientManager.RegisterBroadcast<FieldMsg>(OnFieldClient);
            InstanceFinder.ClientManager.RegisterBroadcast<MumbleMsg>(OnMumbleClient);
            InstanceFinder.ClientManager.RegisterBroadcast<PlayerLeft>(OnPlayerLeftClient);
            InstanceFinder.ClientManager.RegisterBroadcast<ZombieSnap>(OnZombieSnapClient);
            InstanceFinder.ClientManager.RegisterBroadcast<KillFeed>(OnKillFeedClient);
            InstanceFinder.ClientManager.RegisterBroadcast<RoundState>(OnRoundStateClient);
            InstanceFinder.ServerManager.RegisterBroadcast<ReadyMsg>(OnReadyServer);
            InstanceFinder.ClientManager.RegisterBroadcast<LobbyMsg>(OnLobbyClient);
            InstanceFinder.ServerManager.RegisterBroadcast<OutfitMsg>(OnOutfitServer);
            InstanceFinder.ClientManager.RegisterBroadcast<OutfitMsg>(OnOutfitClient);
            InstanceFinder.ServerManager.RegisterBroadcast<DisguiseMsg>(OnDisguiseServer);
            InstanceFinder.ClientManager.RegisterBroadcast<DisguiseMsg>(OnDisguiseClient);
            InstanceFinder.ServerManager.RegisterBroadcast<ScanMsg>(OnScanServer);
            InstanceFinder.ClientManager.RegisterBroadcast<ScanMsg>(OnScanClient);
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnSceneSwapped;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneSwapped;
            InstanceFinder.ServerManager.RegisterBroadcast<VoiceMsg>(OnVoiceServer);
            InstanceFinder.ClientManager.RegisterBroadcast<VoiceMsg>(OnVoiceClient);
            VoiceChat.Touch();
            InstanceFinder.ServerManager.RegisterBroadcast<AbsorbMsg>(OnAbsorbServer);
            InstanceFinder.ClientManager.RegisterBroadcast<AbsorbMsg>(OnAbsorbClient);
            InstanceFinder.ServerManager.RegisterBroadcast<ChestAskMsg>(OnChestAskServer);
            InstanceFinder.ClientManager.RegisterBroadcast<ChestMsg>(OnChestClient);
            InstanceFinder.ServerManager.RegisterBroadcast<IdentityMsg>(OnIdentityServer);
            InstanceFinder.ClientManager.RegisterBroadcast<IdentityMsg>(OnIdentityClient);
            InstanceFinder.ClientManager.RegisterBroadcast<ReadyCallMsg>(OnReadyCallClient);
            InstanceFinder.ClientManager.RegisterBroadcast<SideAssignMsg>(OnSideAssignClient);
            InstanceFinder.ClientManager.RegisterBroadcast<MapDefMsg>(OnMapDefClient);
            InstanceFinder.ClientManager.RegisterBroadcast<StandMsg>(OnStandClient);
            InstanceFinder.ServerManager.RegisterBroadcast<SpawnAskMsg>(OnSpawnAskServer);
            InstanceFinder.ClientManager.RegisterBroadcast<SpawnGiveMsg>(OnSpawnGiveClient);
            InstanceFinder.ServerManager.RegisterBroadcast<AbsorbAskMsg>(OnAbsorbAskServer);
            InstanceFinder.ClientManager.RegisterBroadcast<AbsorbGiveMsg>(OnAbsorbGiveClient);
            InstanceFinder.ClientManager.RegisterBroadcast<GolemSnap>(OnGolemSnapClient);
            InstanceFinder.ClientManager.RegisterBroadcast<BiomeMsg>(OnBiomeClient);
            InstanceFinder.ClientManager.RegisterBroadcast<PlayerFxMsg>(OnPlayerFxClient);
            InstanceFinder.ServerManager.RegisterBroadcast<HurtIntent>(OnHurtServer);
            InstanceFinder.ClientManager.RegisterBroadcast<HealthMsg>(OnHealthClient);
            InstanceFinder.ClientManager.RegisterBroadcast<StateMsg>(OnElementStateClient);
            InstanceFinder.ClientManager.RegisterBroadcast<MarkMsg>(OnMarkClient);
            InstanceFinder.ClientManager.RegisterBroadcast<CoCastMsg>(OnCoCastClient);
            InstanceFinder.ServerManager.RegisterBroadcast<ReviveTickMsg>(OnReviveTickServer);
            InstanceFinder.ClientManager.RegisterBroadcast<ReviveTickMsg>(OnReviveTickClient);
            InstanceFinder.ServerManager.RegisterBroadcast<ReviveDoneMsg>(OnReviveDoneServer);
            InstanceFinder.ClientManager.RegisterBroadcast<ReviveDoneMsg>(OnReviveDoneClient);
            InstanceFinder.ServerManager.RegisterBroadcast<RideAskMsg>(OnRideAskServer);
            InstanceFinder.ClientManager.RegisterBroadcast<RideGiveMsg>(OnRideGiveClient);
            InstanceFinder.ServerManager.RegisterBroadcast<RideDriveMsg>(OnRideDriveServer);

            // host-authoritative channels (netcode §1-§4)
            InstanceFinder.ServerManager.RegisterBroadcast<UnlockMsg>(OnUnlockServer);
            InstanceFinder.ClientManager.RegisterBroadcast<UnlockMsg>(OnUnlockClient);
            InstanceFinder.ServerManager.RegisterBroadcast<DeclareRuneMsg>(OnDeclareRuneServer);
            InstanceFinder.ClientManager.RegisterBroadcast<DeclareRuneMsg>(OnDeclareRuneClient);
            InstanceFinder.ServerManager.RegisterBroadcast<BodySealFire>(OnBodySealServer);
            InstanceFinder.ClientManager.RegisterBroadcast<SealMsg>(OnSealClient);
            InstanceFinder.ClientManager.RegisterBroadcast<SealEndMsg>(OnSealEndClient);
            InstanceFinder.ClientManager.RegisterBroadcast<KickMsg>(OnKickClient);
            InstanceFinder.ClientManager.RegisterBroadcast<CurseMsg>(OnCurseClient);
            InstanceFinder.ClientManager.RegisterBroadcast<WornMsg>(OnWornClient);
            InstanceFinder.ClientManager.RegisterBroadcast<FxMsg>(OnFxClient);
            InstanceFinder.ServerManager.RegisterBroadcast<EraseMsg>(OnEraseServer);
            InstanceFinder.ClientManager.RegisterBroadcast<EraseMsg>(OnEraseClient);
            InstanceFinder.ServerManager.RegisterBroadcast<InkBurnMsg>(OnInkBurnServer);
            InstanceFinder.ClientManager.RegisterBroadcast<InkBurnMsg>(OnInkBurnClient);
            InstanceFinder.ServerManager.RegisterBroadcast<StrokeGrowMsg>(OnStrokeGrowServer);
            InstanceFinder.ClientManager.RegisterBroadcast<StrokeGrowMsg>(OnStrokeGrowClient);
            InstanceFinder.ServerManager.RegisterBroadcast<SealLookMsg>(OnSealLookServer);
            InstanceFinder.ClientManager.RegisterBroadcast<SealLookMsg>(OnSealLookClient);
            InstanceFinder.ServerManager.RegisterBroadcast<InkFxMsg>(OnInkFxServer);
            InstanceFinder.ClientManager.RegisterBroadcast<InkFxMsg>(OnInkFxClient);
            InstanceFinder.ServerManager.RegisterBroadcast<SplitMsg>(OnSplitServer);
            InstanceFinder.ClientManager.RegisterBroadcast<SplitMsg>(OnSplitClient);

            // the book stand lobby: password gate + map likes
            InstanceFinder.ServerManager.RegisterBroadcast<JoinAuthMsg>(OnJoinAuthServer);
            InstanceFinder.ServerManager.RegisterBroadcast<MapLikeMsg>(OnMapLikeServer);
            InstanceFinder.ClientManager.RegisterBroadcast<MapLikesMsg>(OnMapLikesClient);
            InstanceFinder.ClientManager.OnClientConnectionState += OnLocalClientState;
            InstanceFinder.ClientManager.RegisterBroadcast<BookMsg>(OnBookClient);

            // the pot
            InstanceFinder.ClientManager.RegisterBroadcast<PotMsg>(OnPotClient);
            InstanceFinder.ServerManager.RegisterBroadcast<PotDrinkMsg>(OnPotDrinkServer);
            InstanceFinder.ServerManager.RegisterBroadcast<WandCreditMsg>(OnWandCreditServer);
            InstanceFinder.ClientManager.RegisterBroadcast<WandCreditMsg>(OnWandCreditClient);
            InstanceFinder.ClientManager.RegisterBroadcast<MatterSnap>(OnMatterSnapClient);
            InstanceFinder.ClientManager.RegisterBroadcast<ParticleSnap>(OnParticleSnapClient);
            InstanceFinder.ClientManager.RegisterBroadcast<PropReg>(OnPropRegClient);
            InstanceFinder.ClientManager.RegisterBroadcast<PropSnap>(OnPropSnapClient);
            InstanceFinder.ServerManager.RegisterBroadcast<GrabIntent>(OnGrabIntentServer);
            InstanceFinder.ServerManager.RegisterBroadcast<LiftAim>(OnLiftAimServer);
            InstanceFinder.ServerManager.RegisterBroadcast<ThrowIntent>(OnThrowIntentServer);
            InstanceFinder.ServerManager.RegisterBroadcast<DropIntent>(OnDropIntentServer);
            InstanceFinder.ServerManager.RegisterBroadcast<PushIntent>(OnPushIntentServer);
            InstanceFinder.ServerManager.RegisterBroadcast<ClaimIntent>(OnClaimIntentServer);
            InstanceFinder.ClientManager.RegisterBroadcast<GrabAck>(OnGrabAckClient);

            InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnection;
            InstanceFinder.ServerManager.OnAuthenticationResult += OnAuthenticated;
        }

        // ---------------------------------------------------------- outgoing --
        int LocalId => InstanceFinder.ClientManager.Connection != null
            ? InstanceFinder.ClientManager.Connection.ClientId : -1;

        /// The stable FishNet ClientId (-1 when unknown) - the co-op identity (netcode §0).
        public static int LocalClientId => _instance != null ? _instance.LocalId : -1;

        /// ClientId  owner id. Offset by 1: the host's ClientId is 0, and
        /// LocalPlayerId 0 is the codebase's "no player yet" sentinel (netcode §0).
        public static int OwnerIdOf(int clientId) => clientId + 1;

        /// The stable owner id for THIS machine (-1 when not connected/known).
        public static int LocalOwnerId => _instance != null && _instance.LocalId >= 0
            ? OwnerIdOf(_instance.LocalId) : -1;

        /// Scene-path naming for things without a net id (props, colliders).
        public static string PathOf(Transform t) => FullPath(t);

        void SendLocalState()
        {
            var player = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (player == null || LocalId < 0) return;

            ushort flags = 0;
            if (player.IsDowned) flags |= 1;
            if (player.IsSprawled || player.IsAirTumbling) flags |= 2; // both read as "helpless ragdoll" remotely

            var ghost = player.GetComponent<GhostState>();
            bool flying = ghost != null && ghost.IsGhost;
            if (flying) flags |= 4;
            if (SurfaceDrawer.IsPenActive) flags |= 8; // creatures on the host read it as drawing
            if (player.IsCrouched) flags |= 16;
            if (player.IsSprinting) flags |= 32;
            if (!player.IsGrounded) flags |= 64;
            if (player.IsFrozenCube) flags |= 128;
            var wand = player.GetComponent<WandState>();
            if (wand != null && !wand.HasWand) flags |= 256;
            if (HandGrab.LocalHolding) flags |= 512;
            if (PoseStudio.IsOpen || SelfPaint.IsActive || PoseGrab.IsOpen) flags |= 1024;
            var rig = player.GetComponentInChildren<CharacterRig>();
            if (rig != null && rig.PenShown) flags |= 2048;

            Vector3 lv = player.transform.InverseTransformDirection(player.Velocity);
            var ink = player.GetComponent<PlayerInk>();
            var emotes = player.GetComponent<EmotePlayer>();
            HandGrab.LocalHeldId(out byte heldKind, out int heldId);

            InstanceFinder.ClientManager.Broadcast(new PlayerState
            {
                Id = LocalId,
                Pos = player.transform.position,
                Yaw = player.transform.eulerAngles.y,
                Pitch = player.LookPitch,
                Flags = flags,
                Team = MatchLobby.LocalTeam,
                GhostPos = flying ? ghost.SpiritAt : player.transform.position,
                GhostYaw = flying ? ghost.SpiritYaw : 0f,
                Acolyte = Sides.Of(Grimoire.LocalPlayerId) == Side.Acolyte,
                Move = new Vector2(lv.x, lv.z),
                Book = (byte)((GrimoirePages.BookOpen ? 128 : 0) | (GrimoirePages.LocalPage & 127)),
                Ink = (byte)Mathf.RoundToInt(Mathf.Clamp01(ink != null ? ink.Fraction : 1f) * 255f),
                Emote = LiveEmote ? LiveEmoteSlot : (sbyte)(emotes != null ? emotes.ActiveSlot : -1),
                HeldKind = heldKind,
                Held = heldId,
                Hp = (byte)Mathf.RoundToInt(Sides.StrengthFraction(Grimoire.LocalPlayerId, player.Health) * 255f),
                Health10 = (ushort)Mathf.Clamp(Mathf.RoundToInt(player.Health * 10f), 0, 65535),
                Eyes = EyeBits(player.Eyes),
                Gaze = player.Eyes != null ? player.Eyes.LookTarget : Vector3.zero
            }, Channel.Unreliable);
        }

        /// DrawingWorld calls this whenever a local stroke finishes - replicate
        /// it if it lives on world geometry or the local player's body.
        /// `cluster` = the pen-up's touching-cluster flood, reused instead of flooding again.
        public static void OnLocalStrokeFinished(Stroke s, List<Stroke> cluster = null)
        {
            if (_instance == null || !NetGame.Connected || ApplyingRemote) return;
            if (s == null || !s.Alive) return;
            // your own pen - and, on the HOST, the zombie scribes too (their ink is host truth)
            if (s.OwnerId != Grimoire.LocalPlayerId && !NetGame.IsHost) return;
            if (!TryEncode(s, out var msg, cluster)) return;
            InstanceFinder.ClientManager.Broadcast(msg);
        }

        /// The frame a stroke's points travel in. World ink: none (world space).
        /// Body ink: the bone. Zombie ink: the zombie root (snapshot id). Prop
        /// ink: the surface itself (path + local). False = stays local: weapon
        /// engravings and the PaintShell fallback (neither exists on a remote),
        /// and copies already riding a puppet (their owner ships those).
        static bool Frame(Stroke s, out Transform frame, out string surfacePath, out string bone,
            out int creature, out bool surfaceLocal)
        {
            frame = null; surfacePath = ""; bone = ""; creature = 0; surfaceLocal = false;
            var surface = s.Surface;
            if (surface == null) return false;
            // body strokes ride mixamorig bones that carry ragdoll Rigidbodies,
            // so this branch must be decided before the dynamic-surface checks
            if (s.Persistent && surface.name.StartsWith("mixamorig:")
                && surface.GetComponentInParent<SimpleFPSController>() != null)
            {
                frame = surface;
                bone = surface.name;
                return true;
            }
            if (surface.GetComponentInParent<NetAvatar>() != null) return false; // a friend's body ink copy
            var zombie = surface.GetComponentInParent<Zombie>();
            if (zombie != null)
            {
                frame = zombie.transform;
                creature = zombie.gameObject.GetInstanceID(); // the snapshot's name for it
                return true;
            }
            var proxy = surface.GetComponentInParent<NetZombieProxy>();
            if (proxy != null)
            {
                frame = proxy.transform;
                creature = proxy.Id;
                return true;
            }
            // golems the same way: the host's body or a client's stand-in, by snapshot id
            var golem = surface.GetComponentInParent<Golem>();
            if (golem != null)
            {
                frame = golem.transform;
                creature = golem.gameObject.GetInstanceID();
                return true;
            }
            var golemProxy = surface.GetComponentInParent<NetGolemProxy>();
            if (golemProxy != null)
            {
                frame = golemProxy.transform;
                creature = golemProxy.Id;
                return true;
            }
            if (s.Persistent || surface.GetComponentInParent<Creature>() != null) return false;
            surfacePath = FullPath(surface);
            if (surface.GetComponentInParent<Rigidbody>() != null)
            {
                frame = surface; // a crate that moves: the ink rides it in its own space
                surfaceLocal = true;
            }
            return true;
        }

        /// Encode a finished stroke as its StrokeMsg; names it (owner, id) when
        /// it has no name yet. False = this ink stays on this machine.
        static bool TryEncode(Stroke s, out StrokeMsg msg, List<Stroke> cluster = null)
        {
            msg = default;
            if (s == null || !s.Alive) return false;
            if (!Frame(s, out var frame, out var surfacePath, out var bone, out var creature, out var local))
                return false;

            var pts = new List<Vector3>();
            Vector3 normal = Vector3.up;
            foreach (var n in s.Nodes)
            {
                if (n == null) continue;
                // bone / zombie / prop ink travels in its carrier's local space -
                // the only frame that means the same thing on both machines
                pts.Add(frame != null ? frame.InverseTransformPoint(n.transform.position)
                                      : n.transform.position);
                normal = n.SurfaceNormal;
            }
            if (pts.Count < 2) return false;
            if (frame != null) normal = frame.InverseTransformDirection(normal);

            if (s.NetId == 0) s.NetId = _nextStrokeId++;
            RegisterNetStroke(s);

            // the OWNER's pen-up verdict rides along - the host primes, never re-reads (netcode §1)
            int readRune = 0;
            float readScore = 0f;
            int[] clOwners = null, clIds = null;
            if (s.DeclaredRune == RuneType.None && s.OwnerId == Grimoire.LocalPlayerId
                && DrawingWorld.Instance != null)
            {
                var members = _clusterBuf;
                if (RuneGlyph.FloodedFor(cluster, s)) members = cluster; // the pen-up already flooded it
                else
                {
                    _clusterBuf.Clear();
                    _clusterBuf.Add(s);
                    RuneGlyph.GrowTouchingCluster(_clusterBuf, DrawingWorld.Instance.Strokes);
                }
                if (RuneGlyph.CachedVerdict(members, s.OwnerId, out var vr, out var vs))
                {
                    readRune = (int)vr;
                    readScore = vs;
                    clOwners = new int[members.Count];
                    clIds = new int[members.Count];
                    for (int i = 0; i < members.Count; i++)
                    {
                        clOwners[i] = members[i].OwnerId;
                        clIds[i] = members[i].NetId;
                    }
                }
            }

            msg = new StrokeMsg
            {
                Owner = s.OwnerId,
                SurfacePath = surfacePath, // empty for body and zombie ink: the bone / creature IS the address
                Normal = normal,
                Points = pts.ToArray(),
                DeclaredRune = (int)s.DeclaredRune,
                StrokeId = s.NetId,
                ReadRune = readRune,
                ReadScore = readScore,
                ClusterOwners = clOwners,
                ClusterIds = clIds,
                AutoDrawn = s.AutoDrawn,
                AutoLength = s.AutoLength,
                BoneName = bone,
                CreatureId = creature,
                SurfaceLocal = local,
                LiveId = s.LiveId
            };
            return true;
        }

        // ---- the line under the pen, as it grows ----
        Stroke _liveStroke;
        int _liveSent;
        float _liveTimer;
        static readonly List<Vector3> _livePts = new List<Vector3>();

        /// SurfaceDrawer calls this every frame the pen is down on its stroke:
        /// 10 Hz packets carry the nodes added since the last one (an empty
        /// packet keeps the preview alive while the hand rests).
        public static void OnLocalStrokeGrow(Stroke s)
        {
            if (_instance == null || !NetGame.Connected || s == null || s.LiveId == 0) return;
            var me = _instance;
            if (s != me._liveStroke)
            {
                // the stroke that was under the pen a moment ago ends here: its
                // final StrokeMsg (or nothing, if it was too short) follows
                me._liveStroke = s;
                me._liveSent = 0;
                me._liveTimer = 0f;
            }
            me._liveTimer -= Time.unscaledDeltaTime;
            if (me._liveTimer > 0f) return;
            me._liveTimer = 0.1f;
            if (!Frame(s, out var frame, out var surfacePath, out var bone, out var creature, out var local)) return;

            _livePts.Clear();
            Vector3 normal = Vector3.up;
            int from = me._liveSent;
            for (int i = from; i < s.Nodes.Count; i++)
            {
                var n = s.Nodes[i];
                if (n == null) continue;
                _livePts.Add(frame != null ? frame.InverseTransformPoint(n.transform.position)
                                           : n.transform.position);
                normal = n.SurfaceNormal;
            }
            if (frame != null) normal = frame.InverseTransformDirection(normal);
            me._liveSent = s.Nodes.Count;
            InstanceFinder.ClientManager.Broadcast(new StrokeGrowMsg
            {
                Owner = s.OwnerId,
                LiveId = s.LiveId,
                SurfacePath = surfacePath,
                BoneName = bone,
                CreatureId = creature,
                SurfaceLocal = local,
                Normal = normal,
                Points = _livePts.ToArray(),
                From = from
            }, Channel.Unreliable);
        }

        /// A stroke under the pen came to nothing (too short to be ink): the
        /// previews of it go too.
        public static void OnLocalStrokeDropped(Stroke s)
        {
            if (_instance == null || !NetGame.Connected || ApplyingRemote || s == null || s.LiveId == 0) return;
            if (s.OwnerId != Grimoire.LocalPlayerId) return;
            InstanceFinder.ClientManager.Broadcast(new StrokeGrowMsg
                { Owner = s.OwnerId, LiveId = s.LiveId, End = true });
        }

        /// Ink drawn before this connection existed (a lobby doodle, a scene
        /// entered offline) is announced once the stable id is known.
        static void AnnounceInk()
        {
            if (_instance == null || !NetGame.Connected || DrawingWorld.Instance == null) return;
            foreach (var s in DrawingWorld.Instance.Strokes)
            {
                if (s == null || !s.Alive || s.NetId != 0 || s.State == StrokeState.Drawing) continue;
                if (s.OwnerId != Grimoire.LocalPlayerId) continue;
                if (!TryEncode(s, out var msg)) continue;
                InstanceFinder.ClientManager.Broadcast(msg);
            }
        }

        static readonly List<int> _drinkBuf = new List<int>();
        static readonly List<Stroke> _tintOne = new List<Stroke>();

        /// The owner drank/burned their own body ink - tell everyone, so the
        /// copies riding their avatar die too. Strokes that never replicated
        /// (NetId 0: drawn offline, shell-fallback ink) are skipped.
        public static void OnLocalInkBurned(List<Stroke> burned)
        {
            if (_instance == null || !NetGame.Connected || burned == null) return;
            _drinkBuf.Clear();
            foreach (var s in burned)
                if (s != null && s.NetId != 0 && s.OwnerId == Grimoire.LocalPlayerId)
                    _drinkBuf.Add(s.NetId);
            if (_drinkBuf.Count == 0) return;
            InstanceFinder.ClientManager.Broadcast(new InkBurnMsg
                { Owner = Grimoire.LocalPlayerId, Ids = _drinkBuf.ToArray() });
        }

        /// HOST: the picked map's layer to every client (empty = a plain scene);
        /// the host's is the law, like the book.
        public static void PushMapDef(MapDef def)
        {
            if (_instance == null || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new MapDefMsg { Json = def != null ? MapDef.ToJson(def) : "" });
        }

        void OnMapDefClient(MapDefMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            MatchLobby.NetMapDef(msg.Json);
        }

        /// HOST: the leash took a stroke - every machine burns it, the owner's
        /// wand gets it back on their machine (netcode §2).
        public static void PushLeashBurn(Stroke s)
        {
            if (_instance == null || !NetGame.IsHost || s == null || s.NetId == 0) return;
            InstanceFinder.ServerManager.Broadcast(new InkBurnMsg
                { Owner = s.OwnerId, Ids = new[] { s.NetId }, Leash = true });
        }

        /// Every living piece of a replicated stroke, by its wire id.
        public static void PiecesOf(int owner, int id, List<Stroke> into) => FindNetStrokes(owner, id, into);

        static string FullPath(Transform t) => ScenePath.Of(t);

        // ---------------------------------------------- outgoing helpers --
        /// A proxy took damage on a client: tell the host (called by NetZombieProxy).
        /// Host RoundDirector streams round state to clients (2 Hz).
        public static void PushRoundState(byte phase, int round, int left, float timer, int kills, byte ending,
            byte bossHp = 0, byte bosses = 0, string bossName = "", Vector3 causeAt = default)
        {
            if (!NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new RoundState
            {
                Phase = phase, Round = round, Left = left, Timer = timer, Kills = kills, Ending = ending,
                BossHp = bossHp, Bosses = bosses, BossName = bossName ?? "", CauseAt = causeAt,
            });
        }

        /// A zombie died: (pos, its owner, who killed it, via a summoned creature).
        /// Fires on the host from RoundDirector and on clients from the kill feed.
        public static event System.Action<Vector3, int, int, bool> ZombieKilled;

        /// HOST/SOLO: raise ZombieKilled on this machine.
        public static void RaiseZombieKilled(Vector3 pos, int owner, int killedBy, bool viaMinion)
            => ZombieKilled?.Invoke(pos, owner, killedBy, viaMinion);

        /// A remote player's avatar was removed; the owner id.
        public static event System.Action<int> AvatarGone;

        /// Host announces a kill so clients share the ink economy.
        public static void PushKill(Vector3 pos, int owner = -1, int killedBy = -1, bool viaMinion = false)
        {
            if (!NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new KillFeed
                { Pos = pos, Owner = owner, KilledBy = killedBy, Via = (byte)(viaMinion ? 1 : 0) });
        }

        /// Client's lobby ready toggle, host-ward.
        public static void SendReady(bool ready)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new ReadyMsg { Ready = ready });
        }

        /// Host streams lobby state to clients (MatchLobby throttles).
        public static void PushLobby(byte ready, byte total, float countdown, string map,
            int seed, byte acolytePct, byte durationMin)
        {
            if (!NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new LobbyMsg
            {
                Ready = ready, Total = total, Countdown = countdown, Map = map,
                Seed = seed, AcolytePct = acolytePct, DurationMin = durationMin
            });
        }

        public static void PushReadyCall()
        {
            if (!NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new ReadyCallMsg());
        }

        public static void PushSideAssign(int[] acolyteOwners)
        {
            if (!NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new SideAssignMsg { AcolyteOwners = acolyteOwners });
        }

        /// Ask the host for a spawn point. Safe to call when offline - it just
        /// does nothing, and SpawnPlan picks locally.
        public static void AskSpawn(int owner)
        {
            if (_instance == null || !NetGame.Connected) return;
            InstanceFinder.ClientManager.Broadcast(new SpawnAskMsg { Owner = owner, Scene = ActiveScene.Name });
        }

        public static void PushStandOpen(bool open)
        {
            if (!NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new StandMsg { Open = open });
        }

        /// Host boots a client. The disconnect is the whole message.
        public static void Kick(int clientId)
        {
            if (!NetGame.IsHost) return;
            if (InstanceFinder.ServerManager.Clients.TryGetValue(clientId, out var conn))
                conn.Disconnect(true);
        }

        /// Connected remote client ids, for rosters.
        public static IEnumerable<int> RemoteIds
        {
            get
            {
                if (_instance == null) yield break;
                foreach (var id in _instance._avatars.Keys) yield return id;
            }
        }

        // ------------------------------- outgoing: seals/ink/lifting (netcode §1-§4) --
        /// A local unlock - the host's IsUnlocked must answer truthfully (netcode §1).
        public static void PushUnlock(int owner, int card, int rune, Vector3? at = null)
        {
            if (_instance == null || !NetGame.Connected) return;
            if (owner != Grimoire.LocalPlayerId) return;
            InstanceFinder.ClientManager.Broadcast(new UnlockMsg
            {
                Owner = owner, Card = card, Rune = rune,
                At = at ?? Vector3.zero, HasAt = at.HasValue,
            });
        }

        /// ★ A HOST-SIDE GRANT FOR A REMOTE OWNER (summon deeds run in host
        /// code): relayed to everyone, so the earner celebrates and every
        /// mirror agrees.
        public static void PushUnlockFor(int owner, int rune, Vector3? at = null)
        {
            if (_instance == null || !NetGame.Connected || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new UnlockMsg
            {
                Owner = owner, Card = -1, Rune = rune,
                At = at ?? Vector3.zero, HasAt = at.HasValue,
            });
        }

        /// The host's book, to everyone - on join and on every save.
        public static void PushBook()
        {
            if (_instance == null || !NetGame.Connected || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new BookMsg { Json = SpellBook.LiveJson() });
        }

        /// Declare a rune: every machine stamps the same strokes (netcode §1).
        public static void PushDeclare(List<Stroke> members, RuneType rune)
        {
            if (_instance == null || !NetGame.Connected || members == null) return;
            int n = 0;
            foreach (var m in members)
                if (m != null && m.NetId != 0) n++;
            if (n == 0) return;
            var owners = new int[n];
            var ids = new int[n];
            int i = 0;
            foreach (var m in members)
                if (m != null && m.NetId != 0) { owners[i] = m.OwnerId; ids[i] = m.NetId; i++; }
            InstanceFinder.ClientManager.Broadcast(new DeclareRuneMsg
                { Owner = Grimoire.LocalPlayerId, Owners = owners, Ids = ids, Rune = (int)rune, Score = 1f, Stamp = true });
        }

        /// A re-read after an erase: the copies wear the same verdict colour.
        public static void PushTint(List<Stroke> members, RuneType rune, float score)
        {
            if (_instance == null || !NetGame.Connected || members == null) return;
            int n = 0;
            foreach (var m in members)
                if (m != null && m.NetId != 0) n++;
            if (n == 0) return;
            var owners = new int[n];
            var ids = new int[n];
            int i = 0;
            foreach (var m in members)
                if (m != null && m.NetId != 0) { owners[i] = m.OwnerId; ids[i] = m.NetId; i++; }
            InstanceFinder.ClientManager.Broadcast(new DeclareRuneMsg
                { Owner = Grimoire.LocalPlayerId, Owners = owners, Ids = ids, Rune = (int)rune, Score = score, Stamp = false });
        }

        // ---- ink looks: seal gold, rune cyan/grey, spent brown, plain again ----
        public const byte InkLookSealed = 0, InkLookPlain = 1, InkLookSpent = 2;

        static void Ids(IEnumerable<Stroke> strokes, out int[] owners, out int[] ids)
        {
            _pieceBuf.Clear();
            foreach (var s in strokes)
                if (s != null && s.NetId != 0) _pieceBuf.Add(s);
            owners = new int[_pieceBuf.Count];
            ids = new int[_pieceBuf.Count];
            for (int i = 0; i < _pieceBuf.Count; i++)
            {
                owners[i] = _pieceBuf[i].OwnerId;
                ids[i] = _pieceBuf[i].NetId;
            }
        }

        static IEnumerable<Stroke> BoundaryStrokes(Seal seal)
        {
            foreach (var e in seal.Boundary) yield return e.Stroke;
        }

        /// A seal closed, broke or resolved here: every copy of its ink takes
        /// the same colours (host world seals and body seals alike).
        public static void PushSealLook(Seal seal, byte kind, NetworkConnection to = null)
        {
            if (_instance == null || !NetGame.Connected || seal == null) return;
            Ids(BoundaryStrokes(seal), out var bOwners, out var bIds);
            Ids(seal.Payload, out var pOwners, out var pIds);
            var read = new byte[pIds.Length];
            if (kind == InkLookSealed)
            {
                int k = 0;
                foreach (var g in seal.Runes)
                    foreach (var m in g.Members)
                        if (m != null && m.NetId != 0 && k < read.Length) read[k++] = (byte)(g.Rune != RuneType.None ? 1 : 0);
            }
            var msg = new SealLookMsg
            {
                Owner = Grimoire.LocalPlayerId,
                Kind = kind,
                BoundaryOwners = bOwners,
                BoundaryIds = bIds,
                Loop = seal.Boundary.Count == 1,
                PayloadOwners = pOwners,
                PayloadIds = pIds,
                PayloadRead = read
            };
            if (to != null) InstanceFinder.ServerManager.Broadcast(to, msg);
            else InstanceFinder.ClientManager.Broadcast(msg);
        }

        /// Spent ink re-armed (plain) or a re-closed loop spent again (brown).
        public static void PushInkState(List<Stroke> strokes, byte kind)
        {
            if (_instance == null || !NetGame.Connected || strokes == null) return;
            Ids(strokes, out var owners, out var ids);
            if (ids.Length == 0) return;
            InstanceFinder.ClientManager.Broadcast(new SealLookMsg
            {
                Owner = Grimoire.LocalPlayerId,
                Kind = kind,
                BoundaryOwners = owners,
                BoundaryIds = ids,
                PayloadOwners = System.Array.Empty<int>(),
                PayloadIds = System.Array.Empty<int>(),
                PayloadRead = System.Array.Empty<byte>()
            });
        }

        // ---- the feel of drawing actions, heard and seen by everyone ----
        public const byte InkFxFade = 0, InkFxRestore = 1, InkFxChime = 2, InkFxPoof = 3, InkFxCrackle = 4,
            InkFxRune = 5, InkFxRunePoof = 6; // a rune finished: by the book, by a correction

        public static void PushInkFx(byte kind, Vector3 at, float seconds = 0f, List<Stroke> strokes = null)
        {
            if (_instance == null || !NetGame.Connected) return;
            int[] owners = null, ids = null;
            if (strokes != null)
            {
                Ids(strokes, out owners, out ids);
                if (ids.Length == 0 && (kind == InkFxFade || kind == InkFxRestore)) return;
            }
            InstanceFinder.ClientManager.Broadcast(new InkFxMsg
                { Owner = Grimoire.LocalPlayerId, Kind = kind, At = at, Seconds = seconds, Owners = owners, Ids = ids });
        }

        /// A client body seal fired - body ink never replicates, so the whole
        /// resolved payload ships and the HOST builds the spell (netcode §2).
        public static void SendBodySealFire(Seal seal)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost || seal == null) return;
            int n = 0;
            foreach (var g in seal.Runes)
                if (g.Rune != RuneType.None && g.Strength > 0.02f) n++;
            n = Mathf.Min(n, 12);
            if (n == 0) return;
            var runes = new int[n];
            var strengths = new float[n];
            var centers = new Vector3[n];
            var dirs = new Vector3[n];
            var sizes = new float[n];
            int i = 0;
            foreach (var g in seal.Runes)
            {
                if (g.Rune == RuneType.None || g.Strength <= 0.02f || i >= n) continue;
                runes[i] = (int)g.Rune;
                strengths[i] = g.Strength;
                centers[i] = g.Centroid();
                sizes[i] = g.WorldBounds().size.magnitude * 0.5f;
                dirs[i] = (g.Rune == RuneType.Attract || g.Rune == RuneType.Repel)
                    ? Spell.ArrowDirFor(g, seal.PlaneNormal, g.Rune)
                    : seal.PlaneNormal;
                i++;
            }
            InstanceFinder.ClientManager.Broadcast(new BodySealFire
            {
                Origin = seal.PlaneOrigin,
                Normal = seal.PlaneNormal,
                Edges = seal.Edges,
                Duration = seal.Duration,
                Runes = runes,
                Strengths = strengths,
                Centers = centers,
                PushDirs = dirs,
                Sizes = sizes,
                SealRadius = Spell.SealRadius(seal)
            });
        }

        /// Host seal activated - clients get the gold ring, display only (netcode §2).
        public static void PushSeal(Seal seal)
        {
            if (_instance == null || !NetGame.IsHost || !NetGame.Connected || seal == null) return;
            // a body seal's ring is its own gold ink, riding the bones (SealLookMsg)
            bool body = seal.Boundary.Count > 0;
            foreach (var e in seal.Boundary)
                if (e.Stroke == null || !e.Stroke.Persistent) { body = false; break; }
            if (body)
            {
                // no ring, but its zones' looks ride the caster's body on every screen
                var bodyAt = SealBody(seal.OwnerId);
                var bodySpell = seal.Spell;
                if (bodyAt == null || bodySpell == null || bodySpell.Zones.Count == 0) return;
                var look = new SealMsg
                {
                    SealId = seal.Id, Loop = System.Array.Empty<Vector3>(), Duration = seal.Duration,
                    SurfacePath = "", Body = true, Owner = seal.OwnerId
                };
                PutZones(ref look, bodySpell, bodyAt);
                InstanceFinder.ServerManager.Broadcast(look);
                return;
            }
            var nodes = seal.LoopNodes;
            var pts = new Vector3[nodes.Count]; // one-shot per seal close, not per frame
            Transform carrier = null;
            for (int i = 0; i < nodes.Count; i++)
            {
                pts[i] = nodes[i] != null ? nodes[i].transform.position : seal.PlaneOrigin;
                if (carrier == null && nodes[i] != null) carrier = nodes[i].transform.parent;
            }
            var msg = new SealMsg
            {
                SealId = seal.Id, Loop = pts, Duration = seal.Duration,
                SurfacePath = carrier != null ? FullPath(carrier) : ""
            };
            // the spell's zones: a dark sphere, a light, an ember - the same
            // look here and there, from the same numbers
            var spell = seal.Spell;
            if (spell != null && spell.Zones.Count > 0) PutZones(ref msg, spell, null);
            InstanceFinder.ServerManager.Broadcast(msg);
        }

        /// A spell's zones into a seal message: world space, or a body's space when given one.
        static void PutZones(ref SealMsg msg, Spell spell, Transform space)
        {
            var zones = spell.Zones;
            int zn = zones.Count;
            msg.Runes = new int[zn];
            msg.Centers = new Vector3[zn];
            msg.Radii = new float[zn];
            msg.Intensities = new float[zn];
            msg.PushDirs = new Vector3[zn];
            msg.DarkSpread = spell.DarkSpread;
            for (int i = 0; i < zn; i++)
            {
                var z = zones[i];
                msg.Runes[i] = (int)z.Rune;
                msg.Centers[i] = space != null ? space.InverseTransformPoint(z.Center) : z.Center;
                msg.Radii[i] = z.Radius;
                msg.Intensities[i] = z.Intensity;
                msg.PushDirs[i] = space != null ? space.InverseTransformDirection(z.PushDir) : z.PushDir;
            }
        }

        /// The body a body seal's zones ride: our own pilot, else that player's puppet.
        static Transform SealBody(int owner)
        {
            if (owner == Grimoire.LocalPlayerId)
            {
                var p = LocalPilot();
                return p != null ? p.transform : null;
            }
            return _instance != null && _instance._avatars.TryGetValue(owner - 1, out var av) && av != null
                ? av.transform : null;
        }

        /// Host seal ended - ring down; resolved also burns the matching client ink (netcode §2).
        public static void PushSealEnd(Seal seal, bool resolved)
        {
            if (_instance == null || !NetGame.IsHost || !NetGame.Connected || seal == null) return;
            _burnOwnersBuf.Clear();
            _burnIdsBuf.Clear();
            if (resolved)
            {
                foreach (var e in seal.Boundary) CollectBurn(e.Stroke);
                foreach (var s in seal.Payload) CollectBurn(s);
            }
            InstanceFinder.ServerManager.Broadcast(new SealEndMsg
            {
                SealId = seal.Id,
                Resolved = resolved,
                Owner = seal.OwnerId,
                BurnOwners = _burnOwnersBuf.ToArray(),
                BurnIds = _burnIdsBuf.ToArray()
            });
        }

        // a client's body seal has no Seal here: its looks go out under negative ids
        static int _bodySealIds;

        /// A client's body seal ran out on the host: its zone looks go down on every screen.
        public static void PushBodySealEnd(int sealId, int owner)
        {
            if (_instance == null || !NetGame.IsHost || !NetGame.Connected) return;
            InstanceFinder.ServerManager.Broadcast(new SealEndMsg
            {
                SealId = sealId,
                Owner = owner,
                BurnOwners = System.Array.Empty<int>(),
                BurnIds = System.Array.Empty<int>()
            });
        }

        static void CollectBurn(Stroke s)
        {
            if (s == null || s.NetId == 0 || s.Persistent) return;
            for (int i = 0; i < _burnIdsBuf.Count; i++) // split pieces share the source id
                if (_burnIdsBuf[i] == s.NetId && _burnOwnersBuf[i] == s.OwnerId) return;
            _burnOwnersBuf.Add(s.OwnerId);
            _burnIdsBuf.Add(s.NetId);
        }

        /// True while a received erase is being replayed - suppresses re-send.
        public static bool ApplyingRemoteErase { get; private set; }

        /// Local erase/scoop - the host's ink graph must not drift (netcode §2).
        public static void OnLocalErase(Vector3 from, Vector3 to, float radius)
        {
            if (_instance == null || !NetGame.Connected || ApplyingRemoteErase) return;
            InstanceFinder.ClientManager.Broadcast(new EraseMsg
                { Owner = Grimoire.LocalPlayerId, From = from, To = to, Radius = radius, BoneName = "" });
        }

        /// A body-paint rub: the sweep travels in the limb's own space, so the
        /// copies on the owner's puppet (posed and placed differently) are hit.
        public static void OnLocalBodyErase(Transform bone, Vector3 from, Vector3 to, float radius)
        {
            if (_instance == null || !NetGame.Connected || ApplyingRemoteErase || bone == null) return;
            InstanceFinder.ClientManager.Broadcast(new EraseMsg
            {
                Owner = Grimoire.LocalPlayerId,
                From = bone.InverseTransformPoint(from),
                To = bone.InverseTransformPoint(to),
                Radius = radius,
                BoneName = bone.name
            });
        }

        // lifting intents (netcode §4)
        public static void SendGrabIntent(int matterId, string path, float holdDist,
            int creatureId = 0, byte creatureKind = 0)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new GrabIntent
                { MatterId = matterId, Path = path ?? "", HoldDist = holdDist,
                  CreatureId = creatureId, CreatureKind = creatureKind });
        }

        public static void SendLiftAim(Vector3 hand, Quaternion rot)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new LiftAim { Hand = hand, Rot = rot }, Channel.Unreliable);
        }

        public static void SendThrowIntent(Vector3 dir)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new ThrowIntent { Dir = dir });
        }

        public static void SendDropIntent(bool wake = false)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new DropIntent { Wake = wake });
        }

        static readonly Dictionary<Transform, float> _pushNext = new Dictionary<Transform, float>();

        /// CLIENT: this body walked into (add false) or shoved (add true) a
        /// loose prop. Props move on the host, so the push goes there. False
        /// when the collider is no prop the host drives: push it here as before.
        public static bool SendPushIntent(Collider c, Vector3 vel, bool add)
        {
            if (_instance == null || c == null || !NetGame.Connected || NetGame.IsHost) return false;
            Transform prop = null;
            var ghost = c.GetComponentInParent<NetPropGhost>();
            if (ghost != null) prop = ghost.transform;
            else
            {
                // a loose prop the host has not moved yet is still dynamic here
                var rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic && rb.GetComponent<Liftable>() != null) prop = rb.transform;
            }
            if (prop == null) return false;
            if (!add)
            {
                // a walk push streams every frame: 10 Hz per prop, like LiftAim
                if (_pushNext.TryGetValue(prop, out float next) && Time.time < next) return true;
                if (_pushNext.Count > 32) _pushNext.Clear();
                _pushNext[prop] = Time.time + 0.1f;
            }
            InstanceFinder.ClientManager.Broadcast(new PushIntent
                { Path = FullPath(prop), Vel = vel, Add = add }, Channel.Unreliable);
            return true;
        }

        public static void SendClaimIntent(int particleId)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new ClaimIntent { ParticleId = particleId });
        }

        // ------------------------------------------------- server relaying --
        void OnPlayerStateServer(NetworkConnection conn, PlayerState msg, Channel channel)
        {
            msg.Id = conn.ClientId; // trust the connection, not the packet
            ApplyState(msg);
            // relayed by FanOutPresence: at once to whoever stands near, on a slower clock to the far
            _latest[msg.Id] = msg;
            _latestSeq.TryGetValue(msg.Id, out int seq);
            _latestSeq[msg.Id] = seq + 1;
        }

        /// Where this client looks from, by its newest presence: the spirit
        /// when it flies, the body otherwise. False = nothing heard yet.
        bool EyeOf(int clientId, out Vector3 at)
        {
            if (_latest.TryGetValue(clientId, out var s))
            {
                at = (s.Flags & 4) != 0 ? s.GhostPos : s.Pos;
                return true;
            }
            at = default;
            return false;
        }

        /// Squared distance from a viewer to a presence: its body or, flying, its spirit, whichever is closer.
        static float Dist2(PlayerState s, Vector3 eye)
        {
            float d = (s.Pos - eye).sqrMagnitude;
            if ((s.Flags & 4) != 0) d = Mathf.Min(d, (s.GhostPos - eye).sqrMagnitude);
            return d;
        }

        /// The host relays presence by distance: every state to a client who
        /// stands near, PresenceMidHz to one farther, PresenceFarHz beyond, and
        /// one client's states a second stay under PresenceBudgetPerClient
        /// however big the crowd. Everyone still learns of everyone within a
        /// half second: it is the frame rate of far bodies that drops, never
        /// their existence.
        void FanOutPresence()
        {
            if (_latest.Count < 2) return;
            float now = Time.unscaledTime;
            float midHz = Mathf.Max(0.5f, DrawingConfig.PresenceMidHz), farHz = Mathf.Max(0.2f, DrawingConfig.PresenceFarHz);
            float near2 = DrawingConfig.PresenceNearMeters * DrawingConfig.PresenceNearMeters;
            float mid2 = DrawingConfig.PresenceMidMeters * DrawingConfig.PresenceMidMeters;
            foreach (var conn in InstanceFinder.ServerManager.Clients.Values)
            {
                if (conn == null || conn.IsLocalClient) continue; // the host applied everything on arrival
                int cid = conn.ClientId;
                bool eyeKnown = EyeOf(cid, out var eye);
                if (!_fan.TryGetValue(cid, out var slots)) _fan[cid] = slots = new Dictionary<int, FanSlot>();
                // the crowd shares one budget: when the wanted rate outruns it, every clock slows alike
                float wanted = 0f;
                if (eyeKnown)
                    foreach (var kv in _latest)
                    {
                        if (kv.Key == cid) continue;
                        float d2 = Dist2(kv.Value, eye);
                        wanted += d2 < near2 ? 20f : d2 < mid2 ? midHz : farHz;
                    }
                float slow = Mathf.Max(1f, wanted / Mathf.Max(20f, DrawingConfig.PresenceBudgetPerClient));
                foreach (var kv in _latest)
                {
                    int owner = kv.Key;
                    if (owner == cid) continue;
                    if (!slots.TryGetValue(owner, out var slot)) slots[owner] = slot = new FanSlot();
                    _latestSeq.TryGetValue(owner, out int seq);
                    if (slot.Seq == seq || now < slot.NextAt) continue; // nothing new, or not due
                    float every = 0f;
                    if (eyeKnown)
                    {
                        float d2 = Dist2(kv.Value, eye);
                        every = d2 < near2 ? 0f : d2 < mid2 ? 1f / midHz : 1f / farHz;
                    }
                    if (slow > 1f) every = Mathf.Max(every, 0.05f) * slow;
                    slot.Seq = seq;
                    slot.NextAt = now + every;
                    InstanceFinder.ServerManager.Broadcast(conn, kv.Value, true, Channel.Unreliable);
                }
            }
        }

        void OnStrokeServer(NetworkConnection conn, StrokeMsg msg, Channel channel)
        {
            ApplyStroke(msg);
            // a friend's pen on a zombie pins it, as the host's own pen does
            if (msg.CreatureId != 0)
                ZombieById(msg.CreatureId)?.PaintFreeze(DrawingConfig.ZombiePaintFreezeSeconds);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        /// The host's zombie under a snapshot id.
        static Zombie ZombieById(int id)
        {
            foreach (var z in Zombie.All)
                if (z != null && z.gameObject.GetInstanceID() == id) return z;
            return null;
        }

        void OnStrokeGrowServer(NetworkConnection conn, StrokeGrowMsg msg, Channel channel)
        {
            ApplyStrokeGrow(msg);
            // the pen is a decoy for creature eyes: the newest point, in world space
            if (msg.Points != null && msg.Points.Length > 0 && !msg.End)
            {
                var frame = InkFrame(msg.Owner, msg.SurfacePath, msg.BoneName, msg.CreatureId, msg.SurfaceLocal, out bool local);
                if (frame != null || !local)
                {
                    Vector3 p = msg.Points[msg.Points.Length - 1];
                    WorldEvents.Report(WorldEventKind.Ink, local ? frame.TransformPoint(p) : p, 0.5f);
                }
                if (msg.CreatureId != 0)
                    ZombieById(msg.CreatureId)?.PaintFreeze(DrawingConfig.ZombiePaintFreezeSeconds);
            }
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg, true, Channel.Unreliable);
        }

        void OnStrokeGrowClient(StrokeGrowMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            ApplyStrokeGrow(msg);
        }

        /// The carrier a stroke's points are expressed in. Null with local =
        /// false means world space; null with local = true means the carrier
        /// is missing here (skip).
        Transform InkFrame(int owner, string surfacePath, string boneName, int creatureId, bool surfaceLocal,
            out bool local)
        {
            local = true;
            if (!string.IsNullOrEmpty(boneName))
            {
                // avatars key by CLIENT id; stroke owners are OwnerIdOf = client+1
                if (!_avatars.TryGetValue(owner - 1, out var avatar) || avatar == null) return null;
                foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
                    if (t.name == boneName) return t;
                return null; // capsule-fallback avatar: no skeleton
            }
            if (creatureId != 0)
            {
                if (NetGame.IsHost)
                {
                    var z = ZombieById(creatureId);
                    if (z != null) return z.transform;
                    return FindGolem(creatureId)?.transform;
                }
                if (_proxies.TryGetValue(creatureId, out var proxy) && proxy != null) return proxy.transform;
                return _golems.TryGetValue(creatureId, out var gproxy) && gproxy != null ? gproxy.transform : null;
            }
            local = surfaceLocal;
            var go = ScenePath.Find(surfacePath);
            return go != null ? go.transform : null;
        }

        void ApplyStrokeGrow(StrokeGrowMsg msg)
        {
            if (msg.Owner == Grimoire.LocalPlayerId || DrawingWorld.Instance == null) return;
            long key = StrokeKey(msg.Owner, msg.LiveId);
            if (msg.End) { DropLive(key); return; }
            _liveAt[key] = Time.unscaledTime;
            if (msg.Points == null || msg.Points.Length == 0) return; // the hand rests: keepalive

            var frame = InkFrame(msg.Owner, msg.SurfacePath, msg.BoneName, msg.CreatureId, msg.SurfaceLocal, out bool local);
            if (frame == null && (local || !string.IsNullOrEmpty(msg.SurfacePath) || msg.CreatureId != 0)) return;
            Vector3 normal = local ? frame.TransformDirection(msg.Normal) : msg.Normal;

            if (!_live.TryGetValue(key, out var s) || s == null || !s.Alive)
            {
                ZombieScribe.PlaneBasis(normal, out var right, out var up);
                s = new Stroke { BasisRight = right, BasisUp = up, Surface = frame, OwnerId = msg.Owner, LiveId = msg.LiveId };
                DrawingWorld.Instance.Register(s); // State stays Drawing: no seal, no evaporation, no read
                _live[key] = s;
            }
            // a lost packet leaves a gap the final stroke fills; a repeat is skipped
            int skip = Mathf.Max(0, s.Nodes.Count - msg.From);
            for (int i = skip; i < msg.Points.Length; i++)
            {
                Vector3 p = local ? frame.TransformPoint(msg.Points[i]) : msg.Points[i];
                s.AddNode(DrawNode.Create(s, s.Nodes.Count, p, normal, frame));
                if (i == msg.Points.Length - 1) SfxLoops.Pen(p); // their pencil, heard where it writes
            }
            s.MarkDirty();
        }

        void OnBodyFxServer(NetworkConnection conn, BodyFxMsg msg, Channel channel)
        {
            msg.Owner = OwnerIdOf(conn.ClientId);
            PlayBodyFx(msg.Kind, msg.At);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        void OnBodyFxClient(BodyFxMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // host played it on the server path
            if (msg.Owner == Grimoire.LocalPlayerId) return;
            PlayBodyFx(msg.Kind, msg.At);
        }

        void OnEmoteServer(NetworkConnection conn, EmoteMsg msg, Channel channel)
        {
            msg.Owner = OwnerIdOf(conn.ClientId);
            ApplyEmoteDef(msg);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        void OnEmoteClient(EmoteMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Owner == Grimoire.LocalPlayerId) return;
            ApplyEmoteDef(msg);
        }

        void ApplyEmoteDef(EmoteMsg msg)
        {
            if (string.IsNullOrEmpty(msg.Json)) return;
            EmoteDef def;
            try { def = JsonUtility.FromJson<EmoteDef>(msg.Json); }
            catch { return; }
            if (def == null) return;
            _emoteDefs[((long)msg.Owner << 8) | (uint)(msg.Slot & 0xff)] = def;
            if (_avatars.TryGetValue(msg.Owner - 1, out var avatar) && avatar != null)
                avatar.ReplayEmote(msg.Slot, def);
        }

        void OnFieldClient(FieldMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // the host opened its own
            if (msg.Kind == 2)
            {
                // the aura rides the stand-in; the summon message can beat its first snapshot
                if (_proxies.TryGetValue(msg.RideOn, out var proxy) && proxy != null)
                    OpenAura(proxy, msg);
                else
                {
                    _pendingAuras[msg.RideOn] = msg;
                    _pendingAuraAt[msg.RideOn] = Time.time;
                }
                return;
            }
            OpenField(msg.Kind, msg.At, msg.Radius, msg.Seconds, true);
        }

        void OnScanServer(NetworkConnection conn, ScanMsg msg, Channel channel)
        {
            msg.Owner = OwnerIdOf(conn.ClientId);
            if (string.IsNullOrEmpty(msg.Path)) return;
            _scans.TryGetValue(msg.Path, out int n);
            _scans[msg.Path] = n + 1;
            _lastScanOf[msg.Owner] = msg.Path; // the transformation ink turns things into this
            if (msg.Owner != Grimoire.LocalPlayerId) ApplyScan(msg); // the host painted its own
            CurserFollowsScan(msg);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        void OnScanClient(ScanMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // the host painted it on the server path
            if (msg.Owner == Grimoire.LocalPlayerId) return;
            ApplyScan(msg);
            CurserFollowsScan(msg);
        }

        void OnReadyServer(NetworkConnection conn, ReadyMsg msg, Channel channel) =>
            MatchLobby.SetRemoteReady(conn.ClientId, msg.Ready);

        void OnVoiceServer(NetworkConnection conn, VoiceMsg msg, Channel channel)
        {
            msg.Owner = OwnerIdOf(conn.ClientId); // the host names the speaker, never the packet
            // only to listeners within earshot (a spirit's voice only to spirits):
            // a voice nobody could hear is not worth a byte of the host's upload
            float reach = DrawingConfig.VoiceRangeMeters * Mathf.Max(1f, DrawingConfig.VoiceRelayMargin);
            bool speakerKnown = _latest.TryGetValue(conn.ClientId, out var speaker);
            foreach (var to in InstanceFinder.ServerManager.Clients.Values)
            {
                if (to == null || to == conn) continue;
                if (speakerKnown && _latest.TryGetValue(to.ClientId, out var listener))
                {
                    bool listenerFlies = (listener.Flags & 4) != 0;
                    if (msg.Ghost && !listenerFlies) continue;
                    Vector3 ear = listenerFlies ? listener.GhostPos : listener.Pos;
                    float d2 = (speaker.Pos - ear).sqrMagnitude;
                    if ((speaker.Flags & 4) != 0) d2 = Mathf.Min(d2, (speaker.GhostPos - ear).sqrMagnitude);
                    if (d2 > reach * reach) continue;
                }
                InstanceFinder.ServerManager.Broadcast(to, msg, true, Channel.Unreliable);
            }
        }

        void OnVoiceClient(VoiceMsg msg, Channel channel) => VoiceChat.Receive(msg.Owner, msg.Ghost, msg.Data);

        void OnOutfitServer(NetworkConnection conn, OutfitMsg msg, Channel channel)
        {
            _outfits[msg.Id] = msg.Code ?? "";
            Color? hat = FreeHat(msg.HasHat ? (Color)msg.Hat : (Color?)null, msg.Id);
            msg.HasHat = hat != null;
            msg.Hat = hat ?? Color.white;
            if (msg.HasHat) _hats[msg.Id] = msg.Hat; else _hats.Remove(msg.Id);
            InstanceFinder.ServerManager.Broadcast(msg); // relay: everyone sees the look, the asker learns what it wears
        }

        readonly Dictionary<int, float> _exitPuffAt = new Dictionary<int, float>();

        void OnDisguiseServer(NetworkConnection conn, DisguiseMsg msg, Channel channel)
        {
            InstanceFinder.ServerManager.Broadcast(msg); // relay: everyone sees the prop
            // the exit cloud is host law: opened at their puppet, sent to all
            if (msg.Worn || !msg.Puff) return;
            _exitPuffAt.TryGetValue(conn.ClientId, out float last);
            if (Time.time < last + DrawingConfig.PoisonExitCooldown) return;
            _exitPuffAt[conn.ClientId] = Time.time;
            if (_avatars.TryGetValue(conn.ClientId, out var av) && av != null)
                HostDisguiseExit(av.transform.position + Vector3.up * 0.9f);
        }

        void OnDisguiseClient(DisguiseMsg msg, Channel channel)
        {
            if (msg.Id == LocalId) return;
            _disguises[msg.Id] = msg;
            if (_avatars.TryGetValue(msg.Id, out var avatar) && avatar != null)
            {
                // heard on the change only: a turn of the prop or a repeat for a newcomer says nothing
                bool was = avatar.Disguised;
                avatar.ApplyDisguise(msg.Worn, msg.Shape, msg.Rot, msg.Lift, msg.Poof);
                if (msg.Worn && !was) Juice.Sound(Sfx.AcolyteTransform, avatar.transform.position + Vector3.up * 0.5f);
                else if (!msg.Worn && was && msg.Puff) Juice.Sound(Sfx.AcolyteBack, avatar.transform.position + Vector3.up * 0.9f);
            }
        }

        void AnnounceIdentity()
        {
            string name = SteamLobby.SteamReady
                ? Steamworks.SteamFriends.GetPersonaName() : System.Environment.UserName;
            ulong sid = SteamLobby.SteamReady
                ? Steamworks.SteamUser.GetSteamID().m_SteamID : 0UL;
            InstanceFinder.ClientManager.Broadcast(new IdentityMsg
            { Id = LocalId, Name = name, SteamId = sid });
        }

        void OnIdentityServer(NetworkConnection conn, IdentityMsg msg, Channel channel)
        {
            msg.Id = conn.ClientId; // trust the connection, not the packet

            if (BanList.Contains(msg.SteamId))
            {
                conn.Disconnect(true);
                return;
            }

            _identities[msg.Id] = msg;
            InstanceFinder.ServerManager.Broadcast(msg);
            // late joiners get everyone already known
            foreach (var kv in _identities)
                if (kv.Key != msg.Id)
                    InstanceFinder.ServerManager.Broadcast(conn, kv.Value);
        }

        void OnIdentityClient(IdentityMsg msg, Channel channel)
            => _identities[msg.Id] = msg;

        /// Who a player id is, for the lobby inspect popup and the host's
        /// kick list. False until their announcement arrived.
        public static bool IdentityOf(int clientId, out string name, out ulong steamId)
        {
            if (_identities.TryGetValue(clientId, out var m))
            { name = m.Name; steamId = m.SteamId; return true; }
            name = ""; steamId = 0UL;
            return false;
        }

        /// A player absorbed a world source: everyone's copy vanishes (and
        /// respawns on its own lobby timer). The rune grant itself is not in
        /// here; unlocks already replicate through UnlockMsg.
        public static void SendAbsorb(Analyzable a)
        {
            if (_instance == null || !NetGame.Connected || a == null) return;
            InstanceFinder.ClientManager.Broadcast(new AbsorbMsg { Path = PathOf(a.transform) });
        }

        void OnAbsorbServer(NetworkConnection conn, AbsorbMsg msg, Channel channel)
        {
            NoteSpent(msg.Path);
            ApplyAbsorb(msg);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg, true);
        }

        /// HOST: a source consumed on a map stays gone for joiners; the lobby's come back.
        static void NoteSpent(string path)
        {
            if (string.IsNullOrEmpty(path) || ActiveScene.Name == "Lobby" || _spentSources.Contains(path)) return;
            var go = ScenePath.Find(path);
            var a = go != null ? go.GetComponent<Analyzable>() : null;
            if (a != null && !a.Consume) return; // a teaching object everyone can read
            _spentSources.Add(path);
        }

        void OnAbsorbClient(AbsorbMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            ApplyAbsorb(msg);
        }

        static void ApplyAbsorb(AbsorbMsg msg)
        {
            if (string.IsNullOrEmpty(msg.Path)) return;
            var go = ScenePath.Find(msg.Path);
            if (go != null) go.GetComponent<Analyzable>()?.VanishRemote();
        }

        /// One chest opening: offline it rolls here; the host rolls a fresh
        /// seed and tells everyone; a client asks and waits. Same road as an absorb.
        public static void ChestOpen(ChestLid lid)
        {
            if (lid == null) return;
            int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            if (!NetGame.Connected) { lid.Reveal(seed); return; }
            if (InstanceFinder.ServerManager.Started)
            {
                bool quiet = FxQuiet;
                FxQuiet = true; // the clients open theirs from ChestMsg
                try { lid.Reveal(seed); }
                finally { FxQuiet = quiet; }
                _chestSeeds[lid.Id] = seed;
                InstanceFinder.ServerManager.Broadcast(new ChestMsg { Id = lid.Id, Seed = seed });
            }
            else InstanceFinder.ClientManager.Broadcast(new ChestAskMsg { Id = lid.Id });
        }

        void OnChestAskServer(NetworkConnection conn, ChestAskMsg msg, Channel channel)
        {
            var lid = ChestLid.Find(msg.Id);
            if (lid == null) return;
            if (lid.Open)
            {
                // beaten to it, or opened before they joined: theirs rolls the same seed
                if (_chestSeeds.TryGetValue(msg.Id, out int rolled))
                    InstanceFinder.ServerManager.Broadcast(conn, new ChestMsg { Id = msg.Id, Seed = rolled });
                return;
            }
            int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            bool quiet = FxQuiet;
            FxQuiet = true; // the clients open theirs from ChestMsg
            try { lid.Reveal(seed); }
            finally { FxQuiet = quiet; }
            _chestSeeds[msg.Id] = seed;
            InstanceFinder.ServerManager.Broadcast(new ChestMsg { Id = msg.Id, Seed = seed });
        }

        void OnChestClient(ChestMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            ChestLid.Find(msg.Id)?.Reveal(msg.Seed);
        }

        void OnOutfitClient(OutfitMsg msg, Channel channel)
        {
            if (msg.Id == LocalId)
            {
                // only the answer to the latest announcement counts: older ones are overtaken picks
                if (++_outfitAnswers >= _outfitAsks || Time.unscaledTime - _outfitAskedAt > 1f)
                    HatColor.Given(msg.HasHat ? (Color)msg.Hat : (Color?)null);
                return;
            }
            _outfits[msg.Id] = msg.Code ?? "";
            if (msg.HasHat) _hats[msg.Id] = msg.Hat; else _hats.Remove(msg.Id);
            // arrived after the avatar was built? re-dress it in place
            if (_avatars.TryGetValue(msg.Id, out var avatar) && avatar != null)
                avatar.ApplyOutfit(msg.Code);
        }

        /// Everything drawn before this joiner arrived, in registration order
        /// (clusters resolve in order), then the colours seals gave it. Body
        /// ink is not here: the _bodyInk cache replays it once the puppet stands.
        void SendInkSnapshot(NetworkConnection conn)
        {
            var world = DrawingWorld.Instance;
            if (world == null) return;
            var spent = new List<Stroke>();
            foreach (var s in world.Strokes)
            {
                if (s == null || !s.Alive || s.State == StrokeState.Drawing || s.Hidden()) continue;
                if (s.Surface != null && s.Surface.name.StartsWith("mixamorig:")) continue;
                if (!TryEncode(s, out var msg)) continue;
                InstanceFinder.ServerManager.Broadcast(conn, msg);
                if (s.State == StrokeState.Spent) spent.Add(s);
            }
            foreach (var seal in world.ActiveSeals) PushSealLook(seal, InkLookSealed, conn);
            if (spent.Count > 0)
            {
                Ids(spent, out var owners, out var ids);
                InstanceFinder.ServerManager.Broadcast(conn, new SealLookMsg
                {
                    Owner = Grimoire.LocalPlayerId, Kind = InkLookSpent,
                    BoundaryOwners = owners, BoundaryIds = ids,
                    PayloadOwners = System.Array.Empty<int>(), PayloadIds = System.Array.Empty<int>(),
                    PayloadRead = System.Array.Empty<byte>()
                });
            }
        }

        void OnRemoteConnection(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Started)
            {
                // a full lobby turns the joiner away before they receive anything
                // (the connection is already counted, the host's own included)
                int seats = Mathf.Clamp(SteamLobby.PendingSize, 2, SteamLobby.MaxPlayers);
                if (InstanceFinder.ServerManager.Clients.Count > seats)
                {
                    Debug.Log($"[SpellyZombie] Lobby full ({seats}): client {conn.ClientId} turned away.");
                    conn.Disconnect(true);
                    return;
                }
                return; // the welcome waits for authentication (OnAuthenticated): FishNet drops sends before it
            }
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            RemoveAvatar(conn.ClientId);
            _owedMapWelcome.Remove(conn.ClientId);
            _latest.Remove(conn.ClientId); _latestSeq.Remove(conn.ClientId);
            _fan.Remove(conn.ClientId); _zombieNextAt.Remove(conn.ClientId);
            foreach (var kv in _fan) kv.Value.Remove(conn.ClientId);
            ReleaseRidesOf(OwnerIdOf(conn.ClientId)); // leavers climb off
            ReleaseHold(conn.ClientId, Vector3.zero); // leavers let go (netcode §4)
            MatchLobby.SetRemoteReady(conn.ClientId, false); // leavers aren't ready
            InstanceFinder.ServerManager.Broadcast(new PlayerLeft { Id = conn.ClientId });
        }

        /// The joiner's welcome, sent once FishNet has authenticated them.
        void OnAuthenticated(NetworkConnection conn, bool authenticated)
        {
            if (!authenticated || !conn.IsActive) return;
            // ★ the joiner plays by the HOST's book, from the first frame
            InstanceFinder.ServerManager.Broadcast(conn,
                new BookMsg { Json = SpellBook.LiveJson() });
            // and grows the host's island: the lobby tick only runs in the lobby scene
            InstanceFinder.ServerManager.Broadcast(conn, new LobbyMsg
            {
                Ready = (byte)MatchLobby.ReadyCount, Total = (byte)(1 + RemoteCount), Countdown = -1f,
                Map = MatchLobby.SelectedMap, Seed = MatchLobby.Seed,
                AcolytePct = (byte)MatchLobby.AcolytePercent, DurationMin = (byte)MatchLobby.DurationMin
            });
            // and the picked map's layer, so the joiner's island grows the same
            InstanceFinder.ServerManager.Broadcast(conn, new MapDefMsg
                { Json = MapDef.Active != null ? MapDef.ToJson(MapDef.Active) : "" });
            // and the sides as the match dealt them (in the lobby presence carries the pillar picks)
            if (MatchLobby.LastAcolytes != null && !RoundDirector.InLobby)
                InstanceFinder.ServerManager.Broadcast(conn, new SideAssignMsg { AcolyteOwners = MatchLobby.LastAcolytes });
            // joiners arrive in the lobby: on a map the map-bound part waits for their spawn ask from it
            if (ActiveScene.Name == "Lobby") SendMapWelcome(conn);
            else _owedMapWelcome.Add(conn.ClientId);
            // and sees everyone's outfit and hat as they already are
            foreach (var kv in _outfits)
            {
                if (kv.Key == conn.ClientId) continue;
                bool hasHat = _hats.TryGetValue(kv.Key, out var hat);
                InstanceFinder.ServerManager.Broadcast(conn, new OutfitMsg
                    { Id = kv.Key, Code = kv.Value, HasHat = hasHat, Hat = hasHat ? hat : Color.white });
            }
            // and every disguise still worn, quietly: no exit cloud, no swap burst
            foreach (var kv in _disguises)
            {
                if (kv.Key == conn.ClientId || !kv.Value.Worn) continue;
                var worn = kv.Value; worn.Puff = false; worn.Poof = false;
                InstanceFinder.ServerManager.Broadcast(conn, worn);
            }
            // and everyone's body ink and custom poses as they stand today
            int joiner = OwnerIdOf(conn.ClientId);
            foreach (var kv in _bodyInk)
            {
                if (kv.Key == joiner) continue;
                PruneBodyInk(kv.Value);
                foreach (var m in kv.Value) InstanceFinder.ServerManager.Broadcast(conn, m);
            }
            foreach (var kv in _emoteDefs)
            {
                int owner = (int)(kv.Key >> 8);
                if (owner == joiner || kv.Value == null) continue;
                InstanceFinder.ServerManager.Broadcast(conn, new EmoteMsg
                {
                    Owner = owner,
                    Slot = (sbyte)(byte)(kv.Key & 0xff),
                    Json = JsonUtility.ToJson(kv.Value)
                });
            }
        }

        /// What is named by map id or scene path, once the joiner shares the
        /// host's scene: props already broken (the same seeded Shatter), chests
        /// already open (the same seed), sources consumed, props moved, the
        /// ink, and the green every scanned prop already wears.
        void SendMapWelcome(NetworkConnection conn)
        {
            // chests first: an item lifted out of one is named inside it, and
            // one broken after the chest opened must exist before it can die
            foreach (var kv in _chestSeeds)
                InstanceFinder.ServerManager.Broadcast(conn, new ChestMsg { Id = kv.Key, Seed = kv.Value });
            foreach (int id in _goneForGood)
                InstanceFinder.ServerManager.Broadcast(conn, new HealthMsg { NetId = id, Health = 0f, By = -1, Cause = 3 });
            foreach (string path in _spentSources)
                InstanceFinder.ServerManager.Broadcast(conn, new AbsorbMsg { Path = path });
            for (int i = 0; i < _everProps.Count; i++)
            {
                var rb = _everProps[i];
                if (rb == null) continue; // gone on the host
                InstanceFinder.ServerManager.Broadcast(conn, new PropReg { Id = _everPropIds[i], Path = _everPropPaths[i] });
                InstanceFinder.ServerManager.Broadcast(conn, new PropSnap
                {
                    Ids = new[] { _everPropIds[i] },
                    Pos = new[] { rb.transform.position },
                    Rot = new[] { rb.transform.rotation },
                });
            }
            SendInkSnapshot(conn);
            foreach (var kv in _scans)
                for (int i = 0; i < kv.Value; i++)
                    InstanceFinder.ServerManager.Broadcast(conn, new ScanMsg { Owner = -1, Path = kv.Key });
        }

        // ---------------------- server handlers: seals/ink/lifting (netcode §1-§4) --
        void OnUnlockServer(NetworkConnection conn, UnlockMsg msg, Channel channel)
        {
            msg.Owner = OwnerIdOf(conn.ClientId); // trust the connection, not the packet
            if (msg.Owner != Grimoire.LocalPlayerId)
            {
                Grimoire.UnlockRemote(msg.Owner, msg.Card, msg.Rune);
                if (msg.HasAt) UnlockMark.PoofAt(msg.At); // the earner's page poofs there too
            }
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        void OnUnlockClient(UnlockMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Owner == Grimoire.LocalPlayerId)
            {
                // ★ A DEED THE HOST GRANTED ME (summon deeds run in host
                // code): taken the FULL way - toast, pages, recognition.
                // Self-echoes no-op inside UnlockRune's already-known check.
                if (msg.Rune >= 0 && !Grimoire.HasRune(msg.Owner, (RuneType)msg.Rune))
                    Grimoire.UnlockRune(msg.Owner, (RuneType)msg.Rune);
                return;
            }
            Grimoire.UnlockRemote(msg.Owner, msg.Card, msg.Rune);
            if (msg.HasAt) UnlockMark.PoofAt(msg.At);
        }

        void OnBookClient(BookMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            SpellBook.Adopt(msg.Json);
        }

        void OnDeclareRuneServer(NetworkConnection conn, DeclareRuneMsg msg, Channel channel)
        {
            ApplyDeclare(msg);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        void OnDeclareRuneClient(DeclareRuneMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            ApplyDeclare(msg);
        }

        void ApplyDeclare(DeclareRuneMsg msg)
        {
            if (msg.Owner == Grimoire.LocalPlayerId) return; // self-echo
            if (msg.Owners == null || msg.Ids == null || msg.Owners.Length != msg.Ids.Length) return;
            _clusterBuf.Clear();
            FindNetStrokes(msg.Owners, msg.Ids, _clusterBuf); // every piece of every named stroke
            if (_clusterBuf.Count == 0) return;
            var rune = (RuneType)msg.Rune;
            if (msg.Stamp)
            {
                foreach (var s in _clusterBuf)
                {
                    s.DeclaredRune = rune;
                    s.MarkDirty();
                }
                DrawingWorld.TintCluster(_clusterBuf, rune, 1f); // the power colour, not the flat cyan
                return;
            }
            // a re-read (erase lifted): the same verdict, primed and worn here
            RuneGlyph.Prime(_clusterBuf, msg.Owner, rune, msg.Score);
            DrawingWorld.TintCluster(_clusterBuf, rune, msg.Score);
        }

        void OnBodySealServer(NetworkConnection conn, BodySealFire msg, Channel channel)
        {
            // rate cap, like ZombieHit - no packet spam machine-gunning spells
            if (_lastBodyFire.TryGetValue(conn.ClientId, out var t)
                && Time.unscaledTime - t < 0.25f) return;
            _lastBodyFire[conn.ClientId] = Time.unscaledTime;
            if (msg.Runes == null || msg.Strengths == null || msg.Centers == null
                || msg.PushDirs == null || msg.Sizes == null) return;
            int n = msg.Runes.Length;
            if (n == 0 || msg.Strengths.Length != n || msg.Centers.Length != n
                || msg.PushDirs.Length != n || msg.Sizes.Length != n) return;
            Transform caster = _avatars.TryGetValue(conn.ClientId, out var av) && av != null
                ? av.transform : null;
            float duration = Mathf.Clamp(msg.Duration, 0.5f, DrawingConfig.SealProduceSeconds);
            var spell = Spell.CreateRemote(OwnerIdOf(conn.ClientId), msg.Origin, msg.Normal,
                Mathf.Clamp(msg.Edges, 1, 10), duration,
                msg.Runes, msg.Strengths, msg.Centers, msg.PushDirs, msg.Sizes, caster,
                Mathf.Clamp(msg.SealRadius, 0.05f, 10f));
            // its zones' looks on every client, the caster's own included (it builds no spell)
            if (spell == null || caster == null || spell.Zones.Count == 0) return;
            Juice.Sound(Sfx.SealComplete, caster.position);
            var look = new SealMsg
            {
                SealId = --_bodySealIds, Loop = System.Array.Empty<Vector3>(), Duration = duration,
                SurfacePath = "", Body = true, Owner = OwnerIdOf(conn.ClientId)
            };
            PutZones(ref look, spell, caster);
            spell.NetSealId = look.SealId;
            InstanceFinder.ServerManager.Broadcast(look);
        }

        void OnEraseServer(NetworkConnection conn, EraseMsg msg, Channel channel)
        {
            // a player's eraser is capped; the host's own erases (evaporation) go out whole
            if (conn != null && !conn.IsLocalClient) msg.Radius = Mathf.Min(msg.Radius, 0.5f);
            ApplyErase(msg);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        void OnEraseClient(EraseMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            ApplyErase(msg);
        }

        void ApplyErase(EraseMsg msg)
        {
            if (msg.Owner == Grimoire.LocalPlayerId || DrawingWorld.Instance == null) return;
            Vector3 from = msg.From, to = msg.To;
            if (!string.IsNullOrEmpty(msg.BoneName))
            {
                // a body rub: the sweep lands on the same bone of their puppet here
                var bone = InkFrame(msg.Owner, "", msg.BoneName, 0, false, out _);
                if (bone == null) return;
                from = bone.TransformPoint(from);
                to = bone.TransformPoint(to);
            }
            ApplyingRemoteErase = true;
            try
            {
                DrawingWorld.Instance.EraseAlong(from, to, msg.Radius, null); // the host capped a player's eraser on arrival
            }
            finally
            {
                ApplyingRemoteErase = false;
            }
        }

        void OnGrabIntentServer(NetworkConnection conn, GrabIntent msg, Channel channel)
        {
            ReleaseHold(conn.ClientId, Vector3.zero); // one hold per hand
            Rigidbody rb = null;
            if (msg.MatterId != 0)
            {
                Matter blob = null;
                foreach (var m in Matter.Living)
                    if (m != null && m.gameObject.GetInstanceID() == msg.MatterId) { blob = m; break; }
                if (blob == null)
                {
                    Ack(conn, false, "what you aimed at is gone");
                    return;
                }
                // once touched, only a solid grabs again
                if (blob.Touched && blob.Phase != MatterPhase.Solid)
                {
                    Ack(conn, false, $"the {blob.Material} has been handled, only a SOLID grabs again");
                    return;
                }
                if (blob.Core != null) rb = HandGrab.AcquireBody(blob.Core, OwnerIdOf(conn.ClientId));
            }
            else if (msg.CreatureId != 0)
            {
                // a creature by its snapshot id: the stand-in's scene path means nothing here
                Component body = msg.CreatureKind == 2 ? (Component)FindGolem(msg.CreatureId)
                                                       : FindZombie(msg.CreatureId);
                var col = body != null ? body.GetComponentInChildren<Collider>() : null;
                if (col == null)
                {
                    Ack(conn, false, "what you aimed at is gone");
                    return;
                }
                rb = HandGrab.AcquireBody(col, OwnerIdOf(conn.ClientId));
            }
            else if (!string.IsNullOrEmpty(msg.Path))
            {
                var go = ScenePath.Find(msg.Path);
                // the collider physics would answer with: a live one first (a build can leave a switched-off mesh collider first in line)
                Collider col = null;
                if (go != null)
                    foreach (var c in go.GetComponentsInChildren<Collider>(true))
                    {
                        if (col == null) col = c;
                        if (c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger) { col = c; break; }
                    }
                if (col != null) rb = HandGrab.AcquireBody(col, OwnerIdOf(conn.ClientId));
            }
            if (rb == null)
            {
                Ack(conn, false, "the host refused: no ink, or the world itself");
                return;
            }
            // parity: a remote friend's golem goes limp in their hands too, a zombie stops steering
            rb.GetComponentInParent<Golem>()?.BeCarried();
            var held = rb.GetComponentInParent<Zombie>();
            if (held != null) held.Held = true;
            // the cargo keeps its ground pose relative to the lifter, as the local hand does
            float lifterYaw = _avatars.TryGetValue(conn.ClientId, out var lifter) && lifter != null
                ? lifter.transform.eulerAngles.y : 0f;
            _holds[conn.ClientId] = new RemoteHold
            {
                Owner = OwnerIdOf(conn.ClientId),
                Body = rb,
                Marks = rb.GetComponentsInChildren<InkMark>(true),
                HadGravity = rb.useGravity,
                RelRot = Quaternion.Inverse(Quaternion.Euler(0f, lifterYaw, 0f)) * rb.rotation
            };
            if (rb.GetComponent<Matter>() == null) TrackProp(rb); // matter rides MatterSnap
            Ack(conn, true, "", rb.mass); // read after BeCarried, as the local grab reads it
        }

        void OnLiftAimServer(NetworkConnection conn, LiftAim msg, Channel channel)
        {
            if (!_holds.TryGetValue(conn.ClientId, out var h)) return;
            h.Hand = msg.Hand;
            h.Rot = msg.Rot;
            h.HasAim = true;
        }

        void OnThrowIntentServer(NetworkConnection conn, ThrowIntent msg, Channel channel)
        {
            Vector3 dir = msg.Dir.sqrMagnitude > 0.01f ? msg.Dir.normalized : Vector3.forward;
            if (_holds.TryGetValue(conn.ClientId, out var h) && h.Mote != null)
                ReleaseHold(conn.ClientId, dir * HandGrab.ThrowSpeedFor(OwnerIdOf(conn.ClientId)));
            else
                ReleaseHold(conn.ClientId, dir * HandGrab.ThrowImpulse);
        }

        void OnDropIntentServer(NetworkConnection conn, DropIntent msg, Channel channel)
            => ReleaseHold(conn.ClientId, Vector3.zero, msg.Wake);

        void OnPushIntentServer(NetworkConnection conn, PushIntent msg, Channel channel)
        {
            var go = ScenePath.Find(msg.Path);
            var rb = go != null ? go.GetComponent<Rigidbody>() : null;
            if (rb == null || rb.isKinematic) return;
            // the push the host's own body gives (SimpleFPSController)
            if (msg.Add) rb.AddForce(msg.Vel, ForceMode.VelocityChange);
            else rb.linearVelocity = new Vector3(msg.Vel.x, rb.linearVelocity.y, msg.Vel.z);
            Element.TrackLoose(rb);
        }

        void OnClaimIntentServer(NetworkConnection conn, ClaimIntent msg, Channel channel)
        {
            ReleaseHold(conn.ClientId, Vector3.zero);
            SpellParticle mote = null;
            foreach (var p in SpellParticle.Living)
                if (p != null && p.gameObject.GetInstanceID() == msg.ParticleId) { mote = p; break; }
            if (mote == null || mote.Dead || mote.Claimed)
            {
                Ack(conn, false, "that spell is gone (or already claimed)");
                return;
            }
            Transform holder = _avatars.TryGetValue(conn.ClientId, out var av) && av != null
                ? av.transform : null;
                mote.Claim(holder); // claiming is harvesting - the rune re-emits
            _holds[conn.ClientId] = new RemoteHold { Owner = OwnerIdOf(conn.ClientId), Mote = mote };
            Ack(conn, true, "");
        }

        void Ack(NetworkConnection conn, bool ok, string note, float mass = 0f)
            => InstanceFinder.ServerManager.Broadcast(conn, new GrabAck { Ok = ok, Note = note, Mass = mass });

        void OnGrabAckClient(GrabAck msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (!msg.Ok) HandGrab.RemoteHoldRefused(msg.Note);
            else HandGrab.RemoteHoldTaken(msg.Mass);
        }

        // -------------------------------------------------- client applying --
        void OnPlayerStateClient(PlayerState msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // host applied via server path
            ApplyState(msg);
        }

        void OnStrokeClient(StrokeMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            ApplyStroke(msg);
        }

        void OnPlayerLeftClient(PlayerLeft msg, Channel channel) => RemoveAvatar(msg.Id);

        void OnZombieSnapClient(ZombieSnap msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // host has the real ones
            if (msg.Ids == null) return;

            _seen.Clear();
            for (int i = 0; i < msg.Ids.Length; i++)
            {
                int id = msg.Ids[i];
                if (id == 0) continue;   // a zeroed slot is not a zombie
                _seen.Add(id);
                if (!_proxies.TryGetValue(id, out var proxy) || proxy == null)
                {
                    Vector3 scale = msg.Scale != null && i < msg.Scale.Length
                        ? msg.Scale[i] : Vector3.zero;   // zero = fall back to the kind table
                    proxy = NetZombieProxy.Build(id, msg.Pos[i], scale, (msg.Kinds[i] & 1) != 0,
                        (msg.Kinds[i] & 2) != 0);
                    _proxies[id] = proxy;
                    if (_pendingAuras.TryGetValue(id, out var aura))
                    {
                        _pendingAuras.Remove(id);
                        float born = _pendingAuraAt[id];
                        _pendingAuraAt.Remove(id);
                        aura.Seconds -= Time.time - born;
                        if (aura.Seconds > 0f) OpenAura(proxy, aura);
                    }
                }
                proxy.Target(msg.Pos[i], At(msg.Rot, i, Quaternion.identity), At(msg.Vel, i, Vector3.zero));
                proxy.OwnerId = msg.Owner != null && i < msg.Owner.Length ? msg.Owner[i] : -1;
                proxy.SetTranced(msg.Tranced != null && i < msg.Tranced.Length && msg.Tranced[i] == 1);
                if (msg.Tint != null && i < msg.Tint.Length)
                    proxy.Wear(msg.Tint[i], At(msg.Look, i, (byte)255), At(msg.Phase, i, (sbyte)100) / 100f);
                proxy.PlayAnim(At(msg.Anim, i, (byte)0));
                proxy.SetEyes(At(msg.Eyes, i, (byte)0));
                proxy.SetGaze(At(msg.Gaze, i, Vector3.zero));
                proxy.SetCondition(At(msg.Cond, i, (byte)0));
                proxy.SetHurt(At(msg.Hp, i, (byte)255));
                proxy.SetBurden(At(msg.Burden, i, (byte)0));
            }

            // a beat names the zombies near this client and the far ones on a
            // slower clock, so absence is not death: the host names its dead,
            // and a stand-in no beat has named for ProxyTimeout is gone too
            float now = Time.unscaledTime;
            foreach (int id in _seen) _proxySeenAt[id] = now;
            _gone.Clear();
            if (msg.Gone != null) _gone.AddRange(msg.Gone);
            foreach (var kv in _proxies)
                if (!_proxySeenAt.TryGetValue(kv.Key, out float at) || now - at > ProxyTimeout) _gone.Add(kv.Key);
            foreach (int id in _gone)
            {
                if (_proxies.TryGetValue(id, out var dead) && dead != null) dead.Vanish();
                _proxies.Remove(id);
                _proxySeenAt.Remove(id);
            }
        }

        readonly Dictionary<int, NetGolemProxy> _golems = new Dictionary<int, NetGolemProxy>();

        /// A snapshot column's entry, or the fallback when the column is missing or short.
        static T At<T>(T[] column, int i, T fallback) =>
            column != null && i < column.Length ? column[i] : fallback;

        /// Client side: does a golem raised by this owner stand anywhere right now.
        public static bool AnyGolemOwnedBy(int owner)
        {
            if (_instance == null || owner < 0) return false;
            foreach (var kv in _instance._golems)
                if (kv.Value != null && kv.Value.OwnerId == owner) return true;
            return false;
        }

        void OnGolemSnapClient(GolemSnap msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // host has the real ones
            if (msg.Ids == null) return;

            _seen.Clear();
            for (int i = 0; i < msg.Ids.Length; i++)
            {
                int id = msg.Ids[i];
                if (id == 0) continue;   // a zeroed slot is not a golem
                _seen.Add(id);
                if (!_golems.TryGetValue(id, out var proxy) || proxy == null)
                {
                    Vector3 scale = msg.Scale != null && i < msg.Scale.Length
                        ? msg.Scale[i] : Vector3.zero;
                    Color skin = msg.Skin != null && i < msg.Skin.Length
                        ? msg.Skin[i] : Color.gray;
                    proxy = NetGolemProxy.Build(id, msg.Pos[i], scale, skin);
                    if (proxy == null) continue;   // no prefab in the slot
                    _golems[id] = proxy;
                }
                proxy.Target(msg.Pos[i], At(msg.Rot, i, Quaternion.identity), At(msg.Vel, i, Vector3.zero));
                proxy.OwnerId = msg.Owner != null && i < msg.Owner.Length ? msg.Owner[i] : -1;
                if (msg.Skin != null && i < msg.Skin.Length)
                    proxy.Wear(msg.Skin[i], At(msg.Look, i, (byte)255), At(msg.Phase, i, (sbyte)100) / 100f);
                proxy.SetEyes(At(msg.Eyes, i, (byte)0));
                proxy.SetGaze(At(msg.Gaze, i, Vector3.zero));
                proxy.SetCondition(At(msg.Cond, i, (byte)0));
                proxy.SetBeat(At(msg.Beat, i, (byte)0));
                proxy.SetHurt(At(msg.Hp, i, (byte)255));
                proxy.SetBurden(At(msg.Burden, i, (byte)0));
            }

            // gone from the host's list = it came apart; poof the proxy
            _gone.Clear();
            foreach (var kv in _golems)
                if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (int id in _gone)
            {
                if (_golems[id] != null) _golems[id].Vanish();
                _golems.Remove(id);
            }
        }

        /// A client's spell hurt a golem: only the host may actually wound it.
        /// A lvl3 spell opened a biome on the host. Clients open their own copy
        /// so body drift, strength caps and buoyancy agree on every machine -
        /// without this, only the caster was standing in their own spell.
        void OnBiomeClient(BiomeMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            var payload = new SpellPayload
            {
                Temp = msg.Temp, Lum = msg.Lum, Pressure = msg.Pressure,
                Balance = msg.Balance, State = msg.State, Affinity = msg.Affinity,
                Strength = msg.Strength, Int = msg.Int,
                Courage = msg.Courage, Clones = msg.Clones,
            };
            if (msg.ParticleId != 0)
            {
                // a lvl3 particle's numbers: the mote proxy is the look, this is
                // only what the ground reads (ISpellData.Here, Sides)
                if (msg.Close) ArtificialBiome.CloseQuiet(msg.ParticleId);
                else ArtificialBiome.OpenQuiet(msg.ParticleId, msg.At, payload, msg.Radius, msg.Seconds);
                return;
            }
            ArtificialBiome.OpenLocal(msg.At, payload, msg.Radius, msg.Seconds);
        }

        /// HOST: a lvl3 particle became (or stopped being) a place - its masked
        /// numbers, so client-side ground readers agree with the host.
        public static void PushParticleBiome(int id, Vector3 at, SpellPayload p, float radius, float seconds,
            bool close = false)
        {
            if (_instance == null || !NetGame.IsHost || !NetGame.Connected || id == 0) return;
            InstanceFinder.ServerManager.Broadcast(new BiomeMsg
            {
                At = at, Radius = radius, Seconds = seconds, ParticleId = id, Close = close,
                Temp = p.Temp, Lum = p.Lum, Pressure = p.Pressure,
                Balance = p.Balance, State = p.State, Affinity = p.Affinity,
                Strength = p.Strength, Int = p.Int,
                Courage = p.Courage, Clones = p.Clones,
            });
        }

        /// A client asked to hurt something. ONLY the host does the arithmetic,
        /// and it answers to everybody at once - that is what keeps the same
        /// tree on the same health on every machine.
        void OnHurtServer(NetworkConnection conn, HurtIntent msg, Channel channel)
        {
            var d = Element.ById(msg.NetId);
            if (d == null) return;              // not a thing we know; drop it
            // the asker's own blame when it names one, else the asker itself -
            // as an OWNER id, the same currency every other damage source uses
            int by = msg.By >= 0 ? msg.By : OwnerIdOf(conn.ClientId);
            // per-hit cap the old zombie channel carried: no one-packet nukes
            d.TakeDamage(Mathf.Min(msg.Amount, DrawingConfig.NetHitCap), "a friend's magic", by, msg.Via == 1);
        }

        void OnMarkClient(MarkMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            Marks.SetLocal(msg.Owner, (Mark)msg.What, msg.Value);
        }

        void OnCoCastClient(CoCastMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            CoCast.Add(msg.Caster, msg.With, msg.Seconds);
        }

        /// HOST: tell every machine who shares a combined seal's kills.
        public static void PushCoCast(int caster, int[] with, float seconds)
        {
            if (_instance == null || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new CoCastMsg { Caster = caster, With = with, Seconds = seconds });
        }

        /// HOST: publish a mark so every machine can answer the same curse.
        public static void PushMark(int owner, Mark what, int value)
        {
            if (_instance == null || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new MarkMsg
                { Owner = owner, What = (byte)what, Value = value });
        }

        void OnHealthClient(HealthMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            Element.ById(msg.NetId)?.TakeNetHealth(msg.Health, msg.Max, msg.By, msg.Via == 1, msg.Cause);
        }

        /// CLIENT: ask the host to hurt something. Safe offline - it does
        /// nothing and the caller stays authoritative.
        public static void AskHurt(int netId, float amount, int by = -1, bool viaMinion = false)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new HurtIntent
                { NetId = netId, Amount = amount, By = by, Via = (byte)(viaMinion ? 1 : 0) });
        }

        /// HOST: publish the truth after it changed something's health.
        static readonly List<int> _stIds = new List<int>();
        static readonly List<short> _stTemp = new List<short>();
        static readonly List<sbyte> _stState = new List<sbyte>();
        static readonly List<sbyte> _stLum = new List<sbyte>();
        static readonly List<sbyte> _stPress = new List<sbyte>();
        static readonly List<sbyte> _stBal = new List<sbyte>();
        static readonly List<sbyte> _stAff = new List<sbyte>();
        static readonly List<sbyte> _stMind = new List<sbyte>();
        static readonly List<sbyte> _stCour = new List<sbyte>();
        static int _stCursor;

        /// Called on the host beat. Sends only what has MOVED from its natural,
        /// so a quiet map sends nothing at all and a burning one sends the
        /// things that are burning.
        public static void PushElementState()
        {
            if (!NetGame.IsHost || !NetGame.Connected) return;
            _stIds.Clear(); _stTemp.Clear(); _stState.Clear(); _stLum.Clear();
            _stPress.Clear(); _stBal.Clear(); _stAff.Clear(); _stMind.Clear(); _stCour.Clear();

            sbyte Pack(float v) => (sbyte)Mathf.Clamp(
                Mathf.RoundToInt(v / DrawingConfig.AxisCap * 100f), -100, 100);

            // a full snapshot resumes where the last one stopped, so every deviating element ships in turn
            int live = Element.Live.Count;
            bool capped = false;
            for (int k = 0; k < live; k++)
            {
                var e = Element.Live[(_stCursor + k) % live];
                if (e == null) continue;
                var d = e.Data; var n = e.Natural;
                float dT = d.Temp - n.Temp, dS = d.State - n.State, dL = d.Lum - n.Lum;
                float dP = d.Pressure - n.Pressure, dB = d.Balance - n.Balance;
                float dA = d.Affinity - n.Affinity, dM = d.Int - n.Int, dC = d.Courage - n.Courage;
                if (Mathf.Abs(dT) < 4f && Mathf.Abs(dS) < 0.08f && Mathf.Abs(dL) < 0.15f
                    && Mathf.Abs(dP) < 0.08f && Mathf.Abs(dB) < 0.08f && Mathf.Abs(dA) < 0.08f
                    && Mathf.Abs(dM) < 0.08f && Mathf.Abs(dC) < 0.08f) continue;

                _stIds.Add(e.NetId);
                _stTemp.Add((short)Mathf.Clamp(Mathf.RoundToInt(d.Temp), -30000, 30000));
                _stState.Add(Pack(d.State));
                _stLum.Add(Pack(d.Lum));
                _stPress.Add(Pack(d.Pressure));
                _stBal.Add(Pack(d.Balance));
                _stAff.Add(Pack(d.Affinity));
                _stMind.Add(Pack(d.Int));
                _stCour.Add(Pack(d.Courage));
                if (_stIds.Count >= 200) { _stCursor = (_stCursor + k + 1) % live; capped = true; break; }
            }
            if (!capped) _stCursor = 0;
            if (_stIds.Count == 0) return;

            InstanceFinder.ServerManager.Broadcast(new StateMsg
            {
                Ids = _stIds.ToArray(), Temp = _stTemp.ToArray(),
                State = _stState.ToArray(), Lum = _stLum.ToArray(),
                Press = _stPress.ToArray(), Bal = _stBal.ToArray(),
                Aff = _stAff.ToArray(), Mind = _stMind.ToArray(),
                Cour = _stCour.ToArray()
            }, true, Channel.Unreliable);
        }

        /// A client APPLIES the host's answer and never computes its own. Its
        /// element beat does not run, so these numbers are the only ones it has.
        void OnElementStateClient(StateMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // the host has the real boards
            if (msg.Ids == null) return;
            for (int i = 0; i < msg.Ids.Length; i++)
            {
                var e = Element.ById(msg.Ids[i]);
                if (e == null) continue;
                var d = e.Data;
                d.Temp = msg.Temp[i];
                d.State = msg.State[i] * DrawingConfig.AxisCap / 100f;
                d.Lum = msg.Lum[i] * DrawingConfig.AxisCap / 100f;
                // the whole board (older hosts ship without these arrays)
                if (msg.Press != null && i < msg.Press.Length)
                {
                    float k = DrawingConfig.AxisCap / 100f;
                    d.Pressure = msg.Press[i] * k;
                    d.Balance = msg.Bal[i] * k;
                    d.Affinity = msg.Aff[i] * k;
                    d.Int = msg.Mind[i] * k;
                    d.Courage = msg.Cour[i] * k;
                }
                e.Data = d;
                e.ShowState();
            }
        }

        public static void PushHealth(int netId, float health, float max, int by = -1, bool viaMinion = false,
            byte cause = 3)
        {
            if (_instance == null || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new HealthMsg
                { NetId = netId, Health = health, Max = max, By = by, Via = (byte)(viaMinion ? 1 : 0), Cause = cause });
        }

        /// Which player a collider IS - the local one, or the owner behind a
        /// remote puppet. -1 when it is not a body at all.
        public static int OwnerOfBody(Collider c)
        {
            if (c == null) return -1;
            if (c.GetComponentInParent<SimpleFPSController>() != null)
                return Grimoire.LocalPlayerId;
            var av = c.GetComponentInParent<NetAvatar>();
            return av != null ? OwnerIdOf(av.Id) : -1;
        }

        /// HOST: land an effect on a body, wherever that body actually lives.
        /// True means it was shipped to a remote owner, so the caller must NOT
        /// also apply it to the puppet standing here.
        public static bool PushPlayerFx(int owner, byte kind, float amount,
            MatterPhase phase = MatterPhase.Solid, Vector3 point = default)
        {
            if (owner < 0 || owner == Grimoire.LocalPlayerId) return false;
            if (_instance == null || !NetGame.IsHost) return false;
            // the cosmetic kinds show on the puppet standing here too
            if (_instance._avatars.TryGetValue(owner - 1, out var av) && av != null)
                PuppetFx(av, kind, amount, point, phase);
            InstanceFinder.ServerManager.Broadcast(new PlayerFxMsg
            {
                Owner = owner, Kind = kind, Amount = amount,
                Phase = (byte)phase, Point = point,
            });
            return true;
        }

        /// HOST: a look (trail 5, fade 6) landed on the host's OWN body, which
        /// PushPlayerFx never ships - the puppet of the host wears it on every
        /// client. Call after the local Wear/Fade.
        public static void PushPlayerLook(int owner, byte kind, float amount, Vector3 point = default)
        {
            if (owner < 0 || owner != Grimoire.LocalPlayerId) return;
            if (_instance == null || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new PlayerFxMsg
            {
                Owner = owner, Kind = kind, Amount = amount,
                Phase = (byte)MatterPhase.Solid, Point = point,
            });
        }

        /// The kinds every copy applies: the looks, and the heal the store must carry.
        static void PuppetFx(NetAvatar av, byte kind, float amount, Vector3 point,
            MatterPhase phase = MatterPhase.Solid)
        {
            switch (kind)
            {
                case 1: av.TakeHeal(amount); break;
                case 3: av.GetComponentInChildren<StateView>()?.Set(phase); break;
                case 4: Juice.Sound(Sfx.MagicBurst, point, 0.8f, 1.1f, false); break; // the snap itself rides the next presence sample
                case 5: TrailMark.Wear(av.transform, amount); break;
                case 6: av.GetComponentInChildren<StateView>()?.Fade(point.x, amount); break;
            }
        }

        void OnPlayerFxClient(PlayerFxMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Owner != Grimoire.LocalPlayerId)   // not my body: their puppet shows the look
            {
                if (_avatars.TryGetValue(msg.Owner - 1, out var av) && av != null)
                    PuppetFx(av, msg.Kind, msg.Amount, msg.Point, (MatterPhase)msg.Phase);
                return;
            }

            SimpleFPSController me = null;
            foreach (var pl in SimpleFPSController.All)
                if (pl != null && pl.IsLocalViewer) { me = pl; break; }
            if (me == null) return;

            switch (msg.Kind)
            {
                case 0: me.TakeHit(msg.Point, msg.Amount, "magic"); break;
                case 1:
                    if (!me.IsDowned)
                        me.Health = Mathf.Min(Sides.MaxHealthFor(msg.Owner),
                            me.Health + msg.Amount);
                    break;
                case 2: Sides.AddBuff(msg.Owner, msg.Amount); break;
                case 3: BodyState.Of(me.transform)?.SetPhase((MatterPhase)msg.Phase, msg.Amount); break;
                case 4:
                    FallCatcher.Teleport(me, msg.Point + Vector3.up * 0.3f);
                    Juice.Sound(Sfx.MagicBurst, msg.Point, 0.8f, 1.1f, false);
                    break;
                case 5: TrailMark.Wear(me.transform, msg.Amount); break;
                case 6:
                    // Point.x carries how visible to become, Amount how long
                    me.GetComponentInChildren<StateView>()?.Fade(msg.Point.x, msg.Amount);
                    break;
                // the board pushes a spell put on my puppet (SpellParticle.TouchBody)
                case 7: BodyState.Of(me)?.PushGrip(msg.Amount); break;
                case 8: BodyState.Of(me)?.PushWeight(msg.Amount); break;
            }
        }

        /// HOST: tell everyone a biome just opened.
        public static void PushBiome(Vector3 at, SpellPayload p, float radius, float seconds = 0f)
        {
            if (_instance == null || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new BiomeMsg
            {
                At = at, Radius = radius, Seconds = seconds,
                Temp = p.Temp, Lum = p.Lum, Pressure = p.Pressure,
                Balance = p.Balance, State = p.State, Affinity = p.Affinity,
                Strength = p.Strength, Int = p.Int,
                Courage = p.Courage, Clones = p.Clones,
            });
        }

        void OnKillFeedClient(KillFeed msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            PlayerInk.AwardAll(DrawingConfig.InkPerKill); // shared economy, client side
            SealAutopsy.OnKill();
            // a summoner's zombie fell: the whistle plays at the summoner here too
            if (msg.Owner >= 0) Zombie.WhistleOwner(msg.Owner);
            ZombieKilled?.Invoke(msg.Pos, msg.Owner, msg.KilledBy, msg.Via == 1);
        }

        byte _lastPhase = 255;
        float _lastTimer;

        void OnRoundStateClient(RoundState msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            HasRound = true;
            NetPhase = msg.Phase;
            NetRound = msg.Round;
            NetLeft = msg.Left;
            NetTimer = msg.Timer;
            NetKills = msg.Kills;
            NetBossHp = msg.BossHp;
            NetBosses = msg.Bosses;
            NetBossName = msg.BossName ?? "";
            NetEnding = msg.Ending;
            NetCauseAt = msg.CauseAt;
            // the match went live: this machine's books start over too
            if (msg.Phase == 1 && _lastPhase != 1) SideBootstrap.ResetBooksForMatch();
            // the referee's cues are personal sounds: each machine plays its own on the edge
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool onMap = scene != "Lobby" && scene != "Menu";
            if (msg.Phase == 1 && _lastPhase != 1 && onMap) RoundDirector.StartCue(LocalPilotAt());
            // one minute left: the timer arrives twice a second, the edge is heard once
            if (msg.Phase == 1 && _lastPhase == 1 && onMap && _lastTimer > RoundDirector.WarnAt && msg.Timer <= RoundDirector.WarnAt
                && msg.Timer > 0f) RoundDirector.WarnCue();
            _lastTimer = msg.Timer;
            if (msg.Phase == 2 && _lastPhase != 2 && onMap)
            {
                RoundDirector.EndCue(RoundDirector.WonHere(msg.Round), LocalPilotAt());
                Achievements.MatchEnded(msg.Round, (Achievements.Ending)msg.Ending);
            }
            _lastPhase = msg.Phase;

            // phase 1 = the host's match went live: clients still in the lobby
            // follow to the host's map. Only the lobby auto-leaves.
            string here = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (msg.Phase == 1 && here == "Lobby")
            {
                string map = MatchLobby.HostScene; // a saved map travels to its base scene
                if (!string.IsNullOrEmpty(map) && Application.CanStreamedLevelBeLoaded(map))
                    LoadEgg.Travel(map);
            }
            // and home again: the match ended and the host stands in the lobby
            else if (msg.Phase == 0 && here != "Lobby" && here != "Menu")
                LoadEgg.Travel("Lobby");
        }

        static Vector3 LocalPilotAt()
        {
            foreach (var p in SimpleFPSController.All)
                if (p != null && p.IsLocalViewer) return p.transform.position;
            return Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        }

        void OnLobbyClient(LobbyMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            MatchLobby.NetLobby(msg.Ready, msg.Total, msg.Countdown, msg.Map,
                msg.Seed, msg.AcolytePct, msg.DurationMin);
        }

        void OnReadyCallClient(ReadyCallMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            MatchLobby.OnReadyCall();
        }

        void OnSideAssignClient(SideAssignMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started || msg.AcolyteOwners == null) return;
            MatchLobby.ApplySideAssign(msg.AcolyteOwners);
        }

        void OnStandClient(StandMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            LobbyStand.HostMenuOpen = msg.Open;
        }

        /// Anybody may ask where to stand; the HOST decides, so no two players
        /// are handed the same tile. Its own copy is recorded here too - the
        /// host never hears its own broadcast come back.
        void OnSpawnAskServer(NetworkConnection conn, SpawnAskMsg msg, Channel channel)
        {
            // an asker in the host's scene: sources taken here stay taken for them too
            if (msg.Scene == ActiveScene.Name)
                foreach (var taken in AbsorbSource.TakenOnThisMap)
                    InstanceFinder.ServerManager.Broadcast(conn, new AbsorbGiveMsg { Owner = -1, At = taken });
            // a joiner who came mid-match gets the map-bound replay once, asked from the host's scene
            if (msg.Scene == ActiveScene.Name && _owedMapWelcome.Remove(conn.ClientId)) SendMapWelcome(conn);
            if (!SpawnPlan.IssueFor(msg.Owner, out var at)) return; // asker picks its own
            SpawnPlan.TakeAssigned(msg.Owner, at);
            // their puppet's home biome is the tile they were handed, as a pilot's is
            if (_avatars.TryGetValue(conn.ClientId, out var av) && av != null) av.StampHome(at);
            InstanceFinder.ServerManager.Broadcast(
                new SpawnGiveMsg { Owner = msg.Owner, Point = at });
        }

        void OnSpawnGiveClient(SpawnGiveMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            SpawnPlan.TakeAssigned(msg.Owner, msg.Point);
        }

        /// Route one absorb: offline grants now; the host grants and tells
        /// everyone; a client asks and waits for the give. Only one wizard
        /// wins a mote - a later ask finds the source not Ready and whiffs.
        public static void AbsorbCast(AbsorbSource src, int owner)
        {
            if (src == null || !src.Ready) return;
            if (!NetGame.Connected) { src.Grant(owner); return; }
            if (InstanceFinder.ServerManager.Started)
            {
                src.Grant(owner);
                InstanceFinder.ServerManager.Broadcast(
                    new AbsorbGiveMsg { Owner = owner, At = src.transform.position });
            }
            else InstanceFinder.ClientManager.Broadcast(
                new AbsorbAskMsg { Owner = owner, At = src.transform.position });
        }

        void OnAbsorbAskServer(NetworkConnection conn, AbsorbAskMsg msg, Channel channel)
        {
            var src = AbsorbSource.Near(msg.At);
            if (src == null || !src.Ready) return;   // beaten to it
            src.Grant(msg.Owner);
            InstanceFinder.ServerManager.Broadcast(
                new AbsorbGiveMsg { Owner = msg.Owner, At = msg.At });
        }

        void OnAbsorbGiveClient(AbsorbGiveMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Owner < 0) { AbsorbSource.TakenBeforeJoin(msg.At); return; }
            AbsorbSource.Near(msg.At)?.Grant(msg.Owner);
        }

        /// The remote body standing in for this owner, or null.
        public static Transform AvatarTransformOf(int owner)
        {
            if (_instance == null) return null;
            foreach (var kv in _instance._avatars)
                if (OwnerIdOf(kv.Key) == owner && kv.Value != null)
                    return kv.Value.transform;
            return null;
        }

        /// Where this owner is heard from: their spirit while it flies, their body otherwise.
        public static Transform VoiceTransformOf(int owner)
        {
            if (_instance == null) return null;
            foreach (var kv in _instance._avatars)
                if (OwnerIdOf(kv.Key) == owner && kv.Value != null)
                    return kv.Value.Ghost != null ? kv.Value.Ghost : kv.Value.transform;
            return null;
        }

        // ------------------- client applying: seals/matter/particles (netcode §2/§3) --
        void OnSealClient(SealMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            Transform body = null;
            if (msg.Body)
            {
                // a body seal: no ring, its zones ride the caster (our own pilot, else their puppet)
                body = SealBody(msg.Owner);
                if (body == null) return;
            }
            else if (msg.Loop == null || msg.Loop.Length < 3) return;
            if (_rings.TryGetValue(msg.SealId, out var old) && old != null) Destroy(old.gameObject);
            NetSealRing ring;
            if (body != null) ring = NetSealRing.OnBody(body, msg.Duration);
            else
            {
                var carrier = string.IsNullOrEmpty(msg.SurfacePath) ? null : ScenePath.Find(msg.SurfacePath);
                ring = NetSealRing.Show(msg.Loop, msg.Duration, carrier != null ? carrier.transform : null);
            }
            _rings[msg.SealId] = ring;
            // a body seal of our own already sounded when its loop closed here
            if (body != null)
            {
                if (msg.Owner != Grimoire.LocalPlayerId) Juice.Sound(Sfx.SealComplete, body.position);
            }
            else
            {
                Vector3 heardAt = Vector3.zero;
                foreach (var p in msg.Loop) heardAt += p;
                Juice.Sound(Sfx.SealComplete, heardAt / msg.Loop.Length);
            }
            // the zones' looks, built by the same code the host's Spell uses
            if (msg.Runes != null && msg.Centers != null && msg.Radii != null
                && msg.Intensities != null && msg.PushDirs != null)
            {
                int n = Mathf.Min(msg.Runes.Length, msg.Centers.Length, msg.Radii.Length);
                n = Mathf.Min(n, Mathf.Min(msg.Intensities.Length, msg.PushDirs.Length));
                for (int i = 0; i < n; i++)
                    ring.AddZone((RuneType)msg.Runes[i],
                        body != null ? body.TransformPoint(msg.Centers[i]) : msg.Centers[i],
                        msg.Radii[i], msg.Intensities[i],
                        body != null ? body.TransformDirection(msg.PushDirs[i]) : msg.PushDirs[i], msg.DarkSpread);
            }
        }

        void OnSealEndClient(SealEndMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (_rings.TryGetValue(msg.SealId, out var ring))
            {
                if (ring != null) Destroy(ring.gameObject);
                _rings.Remove(msg.SealId);
            }
            // resolved on the host = that environment ink is CONSUMED here too
            if (msg.Resolved && msg.Owner == Grimoire.LocalPlayerId) Achievements.Unlock(Achievements.FirstSpell);
            if (!msg.Resolved || msg.BurnIds == null || msg.BurnOwners == null
                || msg.BurnIds.Length != msg.BurnOwners.Length) return;
            _pieceBuf.Clear();
            FindNetStrokes(msg.BurnOwners, msg.BurnIds, _pieceBuf); // every piece the erase left
            foreach (var s in _pieceBuf)
                if (!s.Persistent) s.Burn();
        }

        void OnMatterSnapClient(MatterSnap msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Ids == null) return;

            _seen.Clear();
            for (int i = 0; i < msg.Ids.Length; i++)
            {
                int id = msg.Ids[i];
                if (id == 0) continue;
                _seen.Add(id);
                var mat = (SurfaceMaterialType)msg.Mats[i];
                var phase = (MatterPhase)msg.Phases[i];
                byte edges = msg.Edges != null && i < msg.Edges.Length ? msg.Edges[i] : (byte)0;
                if (_matterProxies.TryGetValue(id, out var proxy) && proxy != null
                    && (proxy.Mat != mat || proxy.Phase != phase || proxy.Edges != edges))
                {
                    Destroy(proxy.gameObject); // phase/material/shape changed - rebuild with the new shell
                    proxy = null;
                }
                if (proxy == null)
                {
                    proxy = NetMatterProxy.Build(id, mat, phase, msg.Pos[i], msg.Scale[i], edges);
                    _matterProxies[id] = proxy;
                }
                proxy.Target(msg.Pos[i], msg.Rot[i], msg.Scale[i], msg.Looks[i]);
                proxy.Flow = msg.Vels != null && i < msg.Vels.Length ? msg.Vels[i] : Vector3.zero;
                if (msg.Sticks != null && i < msg.Sticks.Length) proxy.Stickiness = msg.Sticks[i];
            }

            _gone.Clear();
            foreach (var kv in _matterProxies)
                if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (int id in _gone)
            {
                if (_matterProxies[id] != null) Destroy(_matterProxies[id].gameObject);
                _matterProxies.Remove(id);
            }
        }

        void OnParticleSnapClient(ParticleSnap msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Ids == null) return;

            _seen.Clear();
            for (int i = 0; i < msg.Ids.Length; i++)
            {
                int id = msg.Ids[i];
                if (id == 0) continue;
                _seen.Add(id);
                byte shape = msg.Kinds[i];
                var tint = msg.Tints != null && i < msg.Tints.Length ? msg.Tints[i] : (Color32)Color.white;
                byte level = msg.Levels != null && i < msg.Levels.Length ? msg.Levels[i] : (byte)1;

                if (_moteProxies.TryGetValue(id, out var proxy) && proxy != null && proxy.Shape != shape)
                {
                    Destroy(proxy.gameObject); // it became something else - rebuild the body
                    proxy = null;
                }
                if (proxy == null)
                {
                    proxy = NetMoteProxy.Build(id, shape, tint, msg.Pos[i]);
                    _moteProxies[id] = proxy;
                }
                byte flags = msg.Flags != null && i < msg.Flags.Length ? msg.Flags[i] : (byte)0;
                float reach = msg.Reach != null && i < msg.Reach.Length ? msg.Reach[i] : 0f;
                var kind = (ParticleKind)(msg.Kinds2 != null && i < msg.Kinds2.Length ? msg.Kinds2[i] : 0);
                byte area = msg.Areas != null && i < msg.Areas.Length ? msg.Areas[i] : (byte)255;
                proxy.Look = msg.Looks != null && i < msg.Looks.Length ? msg.Looks[i] : (byte)255;
                proxy.Wear(tint, level, flags, reach, kind, area);
                proxy.OwnerId = msg.Owners != null && i < msg.Owners.Length ? msg.Owners[i] : -1;
                proxy.Ride(msg.Rides != null && i < msg.Rides.Length ? msg.Rides[i] : 0);
                proxy.Target(msg.Pos[i], msg.Scale[i]);
            }

            _gone.Clear();
            foreach (var kv in _moteProxies)
                if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (int id in _gone)
            {
                if (_moteProxies[id] != null) Destroy(_moteProxies[id].gameObject);
                _moteProxies.Remove(id);
            }
        }

        void OnPropRegClient(PropReg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (string.IsNullOrEmpty(msg.Path)) return;
            var go = ScenePath.Find(msg.Path);
            if (go == null) return; // scene mismatch - skip quietly
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true; // the HOST simulates; we follow
            if (go.GetComponent<NetPropGhost>() == null) go.AddComponent<NetPropGhost>();
            _propGhosts[msg.Id] = go.transform;
        }

        void OnPropSnapClient(PropSnap msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Ids == null) return;
            for (int i = 0; i < msg.Ids.Length; i++)
            {
                if (!_propGhosts.TryGetValue(msg.Ids[i], out var t) || t == null) continue;
                var ghost = t.GetComponent<NetPropGhost>();
                if (ghost != null) ghost.Target(msg.Pos[i], msg.Rot[i]);
            }
        }

        // ------------------------------------------------------------ apply --
        void ApplyState(PlayerState msg)
        {
            if (msg.Id == LocalId) return;
            bool built = false;
            if (!_avatars.TryGetValue(msg.Id, out var avatar) || avatar == null)
            {
                avatar = NetAvatar.Build(msg.Id);
                _avatars[msg.Id] = avatar;
                built = true;
                LobbyCue(true);
                // a disguise announced before this body existed, and ours for them
                if (_disguises.TryGetValue(msg.Id, out var worn))
                    avatar.ApplyDisguise(worn.Worn, worn.Shape, worn.Rot, worn.Lift);
                ShapeShift.RepushLocal();
            }
            // their side as they announce it, so ink colour, book pages and the
            // host's volunteer count agree in the lobby; in a match SideAssign
            // is the law and presence only fills in an owner it never reached
            int owner = OwnerIdOf(msg.Id);
            if (RoundDirector.InLobby || !Sides.Known(owner))
                Sides.Set(owner, msg.Acolyte ? Side.Acolyte : Side.Wizard);
            _strengthOf[owner] = msg.Hp / 255f;
            // a puppet built after its spawn point was issued is born there, not at the origin
            if (built && SpawnPlan.AssignedFor(owner, out var home)) avatar.StampHome(home);
            avatar.Target(msg.Pos, msg.Yaw, msg.Flags, msg.Team, msg.Pitch, msg.Move);
            avatar.FollowHealth(msg.Health10 / 10f); // the host's store and every other machine's copy
            avatar.TargetGhost((msg.Flags & 4) != 0, msg.GhostPos, msg.GhostYaw, msg.Acolyte);
            avatar.TargetHands(msg.Book, msg.Ink, msg.Emote, msg.HeldKind, msg.Held);
            avatar.TargetEyes(msg.Eyes, msg.Gaze);
            if (built) ReplayBodyInk(OwnerIdOf(msg.Id)); // ink that arrived before the body
        }

        /// Each owner's body ink as last announced: a puppet built later and a
        /// joiner get it too. The host caches its own through the loopback path.
        static readonly Dictionary<int, List<StrokeMsg>> _bodyInk = new Dictionary<int, List<StrokeMsg>>();

        static void CacheBodyInk(StrokeMsg msg)
        {
            if (!_bodyInk.TryGetValue(msg.Owner, out var list))
                _bodyInk[msg.Owner] = list = new List<StrokeMsg>();
            for (int i = 0; i < list.Count; i++)
                if (list[i].StrokeId == msg.StrokeId) { list[i] = msg; return; }
            list.Add(msg);
        }

        static void ForgetBodyInk(int owner, int id)
        {
            if (!_bodyInk.TryGetValue(owner, out var list)) return;
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].StrokeId == id) list.RemoveAt(i);
        }

        /// Drops entries whose local copy burned or left with its scene.
        static void PruneBodyInk(List<StrokeMsg> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var s = FindNetStroke(list[i].Owner, list[i].StrokeId);
                if (s != null && (!s.Alive || s.Surface == null)) list.RemoveAt(i);
            }
        }

        void ReplayBodyInk(int owner)
        {
            if (!_bodyInk.TryGetValue(owner, out var list)) return;
            PruneBodyInk(list);
            foreach (var m in list) ApplyBodyStroke(m);
        }

        void ApplyStroke(StrokeMsg msg)
        {
            if (!string.IsNullOrEmpty(msg.BoneName)) CacheBodyInk(msg);
            if (msg.Owner == Grimoire.LocalPlayerId || DrawingWorld.Instance == null) return;
            if (msg.Points == null || msg.Points.Length < 2) return;

            // the final line replaces the preview that grew under their pen
            if (msg.LiveId != 0) DropLive(StrokeKey(msg.Owner, msg.LiveId));

            // body ink names a BONE, not a scene path
            if (!string.IsNullOrEmpty(msg.BoneName)) { ApplyBodyStroke(msg); return; }

            // zombie ink names the creature, prop ink rides its surface
            var frame = InkFrame(msg.Owner, msg.SurfacePath, "", msg.CreatureId, msg.SurfaceLocal, out bool local);
            if (frame == null) return; // scene mismatch, or the zombie is gone - skip quietly

            ApplyingRemote = true;
            try
            {
                Vector3 normal = local ? frame.TransformDirection(msg.Normal) : msg.Normal;
                ZombieScribe.PlaneBasis(normal, out var right, out var up);
                var s = new Stroke
                {
                    BasisRight = right,
                    BasisUp = up,
                    Surface = frame,
                    OwnerId = msg.Owner,
                    DeclaredRune = (RuneType)msg.DeclaredRune,
                    AutoDrawn = msg.AutoDrawn,
                    AutoLength = msg.AutoLength,
                    NetId = msg.StrokeId
                };
                RegisterNetStroke(s); // (owner, id)  this copy (netcode §0)
                DrawingWorld.Instance.Register(s);
                // what the book drew for them grows in here too, over the same time
                float grow = msg.AutoDrawn && msg.AutoLength > 0f
                    ? Mathf.Clamp(msg.AutoLength * 2f, 0.1f, DrawingConfig.AutoCompleteSeconds) : 0f;
                if (grow > 0f) StartCoroutine(GrowRemote(s, msg, frame, normal, local, grow));
                else
                {
                    for (int i = 0; i < msg.Points.Length; i++)
                        s.AddNode(DrawNode.Create(s, i, local ? frame.TransformPoint(msg.Points[i]) : msg.Points[i],
                            normal, frame));
                    FinishRemoteStroke(s, msg);
                }
            }
            finally
            {
                ApplyingRemote = false;
            }
        }

        /// The book's auto-drawn line arrives whole; it is laid down node by
        /// node so friends watch it grow the way its owner did.
        IEnumerator GrowRemote(Stroke s, StrokeMsg msg, Transform frame, Vector3 normal, bool local, float seconds)
        {
            int n = msg.Points.Length;
            int placed = 0;
            float t = 0f;
            while (placed < n)
            {
                if (!s.Alive || frame == null) yield break; // burned or its carrier left mid-growth
                t += Time.deltaTime;
                int want = Mathf.Clamp(Mathf.CeilToInt(Mathf.SmoothStep(0f, 1f, t / seconds) * n), 1, n);
                while (placed < want)
                {
                    s.AddNode(DrawNode.Create(s, placed, local ? frame.TransformPoint(msg.Points[placed]) : msg.Points[placed],
                        normal, frame));
                    placed++;
                }
                if (s.Nodes.Count > 0 && s.Nodes[s.Nodes.Count - 1] != null)
                    SfxLoops.Pen(s.Nodes[s.Nodes.Count - 1].transform.position);
                s.MarkDirty();
                if (placed < n) yield return null;
            }
            ApplyingRemote = true;
            try { FinishRemoteStroke(s, msg); }
            finally { ApplyingRemote = false; }
        }

        /// Pen-up on a copy: completed, tinted by the owner's verdict, scanned.
        void FinishRemoteStroke(Stroke s, StrokeMsg msg)
        {
            DrawingWorld.Instance.CompleteStroke(s);
            // declared ink is a full read: same colour here as on the owner's screen
            if (s.Alive && s.DeclaredRune != RuneType.None)
            {
                _tintOne.Clear();
                _tintOne.Add(s);
                DrawingWorld.TintCluster(_tintOne, s.DeclaredRune, 1f);
            }
            TintRemoteCluster(msg);
            DrawingWorld.Instance.RequestDetect(); // their circle seals here too
        }

        /// The OWNER's verdict primes the cache - never recomputed here (netcode §1).
        void TintRemoteCluster(StrokeMsg msg)
        {
            if (msg.ClusterIds == null || msg.ClusterOwners == null
                || msg.ClusterIds.Length == 0 || msg.ClusterOwners.Length != msg.ClusterIds.Length) return;
            _clusterBuf.Clear();
            for (int i = 0; i < msg.ClusterIds.Length; i++)
                if (!FindNetStrokes(msg.ClusterOwners[i], msg.ClusterIds[i], _clusterBuf)) return; // a member is missing here
            RuneGlyph.Prime(_clusterBuf, msg.Owner, (RuneType)msg.ReadRune, msg.ReadScore);
            DrawingWorld.TintCluster(_clusterBuf, (RuneType)msg.ReadRune, msg.ReadScore); // their ink wears its rune here too
        }

        /// A friend's body ink: points arrive bone-local and mount on the same-named
        /// avatar bone. The copy is cosmetic and evaporation-exempt - no recognition,
        /// claim or closure (the owner's BodySealFire carries any cast, netcode §2).
        void ApplyBodyStroke(StrokeMsg msg)
        {
            // avatars key by CLIENT id; stroke owners are OwnerIdOf = client+1
            if (!_avatars.TryGetValue(msg.Owner - 1, out var avatar) || avatar == null) return;
            if (msg.Points == null || msg.Points.Length < 2) return;
            var had = FindNetStroke(msg.Owner, msg.StrokeId);
            if (had != null && had.Alive && had.Surface != null) return; // already on this body

            Transform bone = null;
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
                if (t.name == msg.BoneName) { bone = t; break; }
            if (bone == null) return; // capsule-fallback avatar: no skeleton, skip quietly

            ApplyingRemote = true;
            try
            {
                Vector3 normal = bone.TransformDirection(msg.Normal);
                ZombieScribe.PlaneBasis(normal, out var right, out var up);
                var s = new Stroke
                {
                    BasisRight = right,
                    BasisUp = up,
                    Surface = bone,
                    OwnerId = msg.Owner,
                    DeclaredRune = (RuneType)msg.DeclaredRune,
                    AutoDrawn = msg.AutoDrawn,
                    AutoLength = msg.AutoLength,
                    NetId = msg.StrokeId
                };
                RegisterNetStroke(s); // (owner, id)  this copy, so drink-burns find it
                DrawingWorld.Instance.Register(s);
                for (int i = 0; i < msg.Points.Length; i++)
                    s.AddNode(DrawNode.Create(s, i,
                        bone.TransformPoint(msg.Points[i]), normal, bone));
                DrawingWorld.Instance.CompleteStroke(s,
                    allowCloseOntoInk: false, silent: true, preview: false);
                // stamped ink and the owner's pen-up verdict colour it here too (display only)
                if (s.Alive && s.DeclaredRune != RuneType.None)
                {
                    _tintOne.Clear();
                    _tintOne.Add(s);
                    DrawingWorld.TintCluster(_tintOne, s.DeclaredRune, 1f);
                }
                TintRemoteCluster(msg);
            }
            finally
            {
                ApplyingRemote = false;
            }
        }

        // --------------------------------------------------- book stand lobby --
        static readonly Dictionary<int, string> _mapLikes = new Dictionary<int, string>();   // host: clientId  liked map
        static readonly Dictionary<string, int> _likeCounts = new Dictionary<string, int>(); // everyone: map  likes (stand UI reads)

        /// Likes for a map, as last announced by the host.
        public static int LikeCount(string map) =>
            !string.IsNullOrEmpty(map) && _likeCounts.TryGetValue(map, out var n) ? n : 0;

        /// Local player likes a map at the stand. One like per player; liking
        /// another map moves it.
        public static void SendMapLike(string map)
        {
            if (_instance == null || !NetGame.Connected || string.IsNullOrEmpty(map)) return;
            if (NetGame.IsHost) _instance.ApplyLike(-1, map); // the host's own like, id -1
            else InstanceFinder.ClientManager.Broadcast(new MapLikeMsg { Map = map });
        }

        void OnMapLikeServer(NetworkConnection conn, MapLikeMsg msg, Channel channel)
            => ApplyLike(conn.ClientId, msg.Map);

        void ApplyLike(int clientId, string map)
        {
            _mapLikes[clientId] = map ?? "";
            _likeCounts.Clear();
            foreach (var kv in _mapLikes)
                if (!string.IsNullOrEmpty(kv.Value))
                    _likeCounts[kv.Value] = (_likeCounts.TryGetValue(kv.Value, out var n) ? n : 0) + 1;
            var maps = new string[_likeCounts.Count];
            var counts = new int[_likeCounts.Count];
            int i = 0;
            foreach (var kv in _likeCounts) { maps[i] = kv.Key; counts[i] = kv.Value; i++; }
            InstanceFinder.ServerManager.Broadcast(new MapLikesMsg { Maps = maps, Counts = counts });
        }

        void OnMapLikesClient(MapLikesMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // host tallied it itself
            _likeCounts.Clear();
            if (msg.Maps == null || msg.Counts == null) return;
            for (int i = 0; i < msg.Maps.Length && i < msg.Counts.Length; i++)
                _likeCounts[msg.Maps[i]] = msg.Counts[i];
        }

        /// The moment our CLIENT connection stands, announce the password we
        /// typed at the stand. The host ignores empty-password lobbies.
        void OnLocalClientState(FishNet.Transporting.ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Stopped)
            {
                NetGame.ClientStarting = false;
                HostGone();
                return;
            }
            if (args.ConnectionState != FishNet.Transporting.LocalConnectionState.Started) return;
            NetGame.ClientStarting = false;
            _leaving = false;
            _wasClient = !InstanceFinder.ServerManager.Started;
            if (InstanceFinder.ServerManager.Started) return; // the host trusts itself
            InstanceFinder.ClientManager.Broadcast(new JoinAuthMsg
                { Password = NetGame.JoinPassword ?? "" });
        }

        bool _wasClient;   // we were a guest, not the host running its own client
        bool _leaving;     // the local player walked out (Quit): no banner, no trip home

        /// The local player walks out on purpose (the Quit button): the same
        /// teardown as a lost host, without the banner or the trip home.
        public static void LeaveSession()
        {
            // the Steam lobby is joined before FishNet connects: leave it even mid-handshake
            if (NetGame.Connected && _instance != null) _instance._leaving = true;
            if (NetGame.IsHost) SteamLobby.DeleteLobby();
            else SteamLobby.LeaveJoined();
        }

        /// THE HOST WENT AWAY. Nothing used to catch this: every orphan kept
        /// its dead proxies standing, and `IsAuthority` (!Connected || IsHost)
        /// quietly promoted each of them to referee a match that no longer
        /// existed. Tear the borrowed world down and go home.
        void HostGone()
        {
            bool walkedOut = _leaving;
            _leaving = false;
            if (!_wasClient) return;   // the host stopping its own server is not this
            _wasClient = false;

            foreach (var kv in _proxies) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _proxies.Clear();
            _proxySeenAt.Clear();
            foreach (var kv in _golems) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _golems.Clear();
            foreach (var kv in _avatars) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _avatars.Clear();
            foreach (var kv in _rings) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _rings.Clear();
            HasRound = false;

            if (walkedOut) return;   // the Quit button travels on its own
            DrawingWorld.Instance?.LogEvent("the host left. back to the lobby");
            ComboBanner.Show(Loc.T("net.hostleft"), new Color(1f, 0.6f, 0.5f));

            if (ActiveScene.Name == "Lobby") return;   // already home
            LoadEgg.Travel("Lobby");
        }

        /// Wrong password = disconnected on the spot. No password set = open
        /// lobby, everyone passes.
        void OnJoinAuthServer(NetworkConnection conn, JoinAuthMsg msg, Channel channel)
        {
            if (string.IsNullOrEmpty(NetGame.HostPassword)) return;
            if ((msg.Password ?? "") == NetGame.HostPassword) return;
            Debug.Log($"[SpellyZombie] Book stand: wrong lobby password from client {conn.ClientId} — kicked.");
            conn.Disconnect(true);
        }

        // ---------------------------------------------------------- the pot --
        /// Local player excluded - the sim already counts it directly.
        public static void EachRemotePlayer(System.Action<int, Vector3> visit)
        {
            if (_instance == null) return;
            foreach (var kv in _instance._avatars)
                if (kv.Value != null) visit(OwnerIdOf(kv.Key), kv.Value.transform.position);
        }

        public static void PushPot(float fill01, bool corrupt, float prep, string pot, bool grounded, float green)
        {
            if (_instance == null || !NetGame.IsHost || !NetGame.Connected) return;
            InstanceFinder.ServerManager.Broadcast(new PotMsg
            {
                Fill01 = fill01, Corrupt = corrupt, Prep = prep, Pot = pot,
                Grounded = grounded, Capacity = CauldronEconomy.Capacity,
                Green = (byte)(Mathf.Clamp01(green) * 255f),
            });
        }

        /// An acolyte's wand credit for a spell judged on another machine:
        /// the host hands it to the owner, a client hands it to the host.
        public static void SendWandCredit(int owner, float amount)
        {
            if (_instance == null || !NetGame.Connected || owner < 0) return;
            var msg = new WandCreditMsg { Owner = owner, Amount = amount };
            if (NetGame.IsHost)
            {
                if (InstanceFinder.ServerManager.Clients.TryGetValue(owner - 1, out var conn))
                    InstanceFinder.ServerManager.Broadcast(conn, msg);
            }
            else InstanceFinder.ClientManager.Broadcast(msg);
        }

        void OnWandCreditServer(NetworkConnection conn, WandCreditMsg msg, Channel channel)
        {
            float amount = Mathf.Clamp(msg.Amount, 0f, DrawingConfig.InkMax * 0.15f);
            if (msg.Owner == Grimoire.LocalPlayerId) PlayerInk.CreditWand(msg.Owner, amount);
            else SendWandCredit(msg.Owner, amount);
        }

        void OnWandCreditClient(WandCreditMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // the host credited itself
            PlayerInk.CreditWand(msg.Owner, msg.Amount);
        }

        public static void SendPotDrink(float amount)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new PotDrinkMsg { Amount = amount });
        }

        void OnPotClient(PotMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // the host IS the truth
            CauldronEconomy.ApplyNet(msg.Fill01, msg.Corrupt, msg.Prep, msg.Pot, msg.Grounded, msg.Capacity,
                msg.Green / 255f);
        }

        void OnPotDrinkServer(NetworkConnection conn, PotDrinkMsg msg, Channel channel)
        {
            // sanity-capped: nobody drinks more than a second of near-rate plus spill per bill
            float amount = Mathf.Clamp(msg.Amount, 0f,
                DrawingConfig.PotRefillNearPerSec + DrawingConfig.PotSpillPerSec * CauldronEconomy.Scale);
            CauldronEconomy.Active?.BillInk(amount);
        }

        void ApplyInkBurn(InkBurnMsg msg)
        {
            if (msg.Ids == null) return;
            foreach (var id in msg.Ids) ForgetBodyInk(msg.Owner, id);
            bool mine = msg.Owner == Grimoire.LocalPlayerId;
            if (mine && !msg.Leash) return; // a drink: the owner burned its own already
            int burned = 0;
            _pieceBuf.Clear();
            foreach (var id in msg.Ids) FindNetStrokes(msg.Owner, id, _pieceBuf); // every piece an erase left
            foreach (var s in _pieceBuf)
            {
                if (mine) DrawingWorld.ReturnToWand(s); // the host's leash: the ink comes home here
                s.Burn(); burned++; // the sweep culls the dead entry
            }
            // the drink's chime at their body, as the owner hears it at theirs
            if (!msg.Leash && burned > 0 && _avatars.TryGetValue(msg.Owner - 1, out var av) && av != null)
                Juice.Sound(Sfx.InkPop2, av.transform.position);
        }

        void OnInkBurnServer(NetworkConnection conn, InkBurnMsg msg, Channel channel)
        {
            ApplyInkBurn(msg);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        void OnInkBurnClient(InkBurnMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // host applied via server path
            ApplyInkBurn(msg);
        }

        // ---- seal looks on the copies (netcode §2) ----
        void OnSealLookServer(NetworkConnection conn, SealLookMsg msg, Channel channel)
        {
            ApplySealLook(msg);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        void OnSealLookClient(SealLookMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            ApplySealLook(msg);
        }

        void ApplySealLook(SealLookMsg msg)
        {
            if (msg.Owner == Grimoire.LocalPlayerId) return; // self-echo
            _pieceBuf.Clear();
            FindNetStrokes(msg.BoundaryOwners, msg.BoundaryIds, _pieceBuf);
            switch (msg.Kind)
            {
                case InkLookSealed:
                {
                    foreach (var s in _pieceBuf)
                    {
                        s.SetColor(Stroke.SealColor);
                        if (msg.Loop && _pieceBuf.Count == 1) s.SetLoop(true); // a whole, unsplit ring closes
                    }
                    var boundary = new List<Stroke>(_pieceBuf);
                    var payload = new List<Stroke>();
                    var read = new List<byte>();
                    if (msg.PayloadOwners != null && msg.PayloadIds != null && msg.PayloadRead != null
                        && msg.PayloadOwners.Length == msg.PayloadIds.Length
                        && msg.PayloadRead.Length == msg.PayloadIds.Length)
                        for (int i = 0; i < msg.PayloadIds.Length; i++)
                        {
                            _pieceBuf.Clear();
                            FindNetStrokes(msg.PayloadOwners[i], msg.PayloadIds[i], _pieceBuf);
                            foreach (var s in _pieceBuf)
                            {
                                s.SetColor(msg.PayloadRead[i] == 1 ? Stroke.RuneColor : Stroke.FizzleColor);
                                payload.Add(s);
                                read.Add(msg.PayloadRead[i]);
                            }
                        }
                    if (boundary.Count > 0) SealGallery.CaptureCopy(boundary, payload, read); // their seal joins this round's gallery
                    return;
                }
                case InkLookPlain:
                    FindNetStrokes(msg.PayloadOwners, msg.PayloadIds, _pieceBuf);
                    foreach (var s in _pieceBuf)
                    {
                        s.SetColor(Stroke.InkColorFor(s.OwnerId));
                        s.SetLoop(false);
                    }
                    return;
                case InkLookSpent:
                    FindNetStrokes(msg.PayloadOwners, msg.PayloadIds, _pieceBuf);
                    foreach (var s in _pieceBuf)
                    {
                        if (s.Persistent) s.SetColor(Stroke.SpentColor);
                        else s.Burn(); // environment ink is consumed where its owner consumed it
                    }
                    return;
            }
        }

        // ---- the feel of drawing actions (netcode §2) ----
        void OnInkFxServer(NetworkConnection conn, InkFxMsg msg, Channel channel)
        {
            ApplyInkFx(msg);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        void OnInkFxClient(InkFxMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            ApplyInkFx(msg);
        }

        void ApplyInkFx(InkFxMsg msg)
        {
            if (msg.Owner == Grimoire.LocalPlayerId) return; // self-echo
            bool quiet = FxQuiet;
            FxQuiet = true; // relayed already: every machine plays it from the message
            try { ApplyInkFxNow(msg); }
            finally { FxQuiet = quiet; }
        }

        void ApplyInkFxNow(InkFxMsg msg)
        {
            switch (msg.Kind)
            {
                case InkFxFade:
                {
                    var fading = new List<Stroke>();
                    FindNetStrokes(msg.Owners, msg.Ids, fading);
                    if (fading.Count > 0) StartCoroutine(FadeRemote(fading, Mathf.Clamp(msg.Seconds, 0.05f, 2f)));
                    return;
                }
                case InkFxRestore:
                    _pieceBuf.Clear();
                    FindNetStrokes(msg.Owners, msg.Ids, _pieceBuf);
                    foreach (var s in _pieceBuf)
                    {
                        s.SetEvaporation(1f);
                        _fadeCancelled.Add(s);
                    }
                    return;
                case InkFxChime:
                    Juice.Sound(Sfx.SealComplete, msg.At);
                    return;
                case InkFxPoof:
                    if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, msg.At);
                    Juice.Chime(msg.At);
                    return;
                case InkFxRune:
                    if (!Juice.Sound(Sfx.RuneComplete, msg.At)) Juice.Chime(msg.At);
                    return;
                case InkFxRunePoof:
                    if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, msg.At);
                    if (!Juice.Sound(Sfx.RuneComplete, msg.At)) Juice.Chime(msg.At);
                    return;
                case InkFxCrackle:
                    Juice.Sound(Sfx.WandDry, msg.At);
                    return;
            }
        }

        // ---- closure splits: the copies split into the same pieces (netcode §0) ----
        void OnSplitServer(NetworkConnection conn, SplitMsg msg, Channel channel)
        {
            msg.Sender = OwnerIdOf(conn.ClientId);
            ApplySplit(msg);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        void OnSplitClient(SplitMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            ApplySplit(msg);
        }

        void ApplySplit(SplitMsg msg)
        {
            if (msg.From == null || msg.To == null || msg.Reverse == null || msg.Tiny == null
                || msg.Residue == null || msg.NewIds == null) return;
            int n = msg.From.Length;
            if (n == 0 || msg.To.Length != n || msg.Reverse.Length != n || msg.Tiny.Length != n
                || msg.Residue.Length != n || msg.NewIds.Length != n) return;
            if (!string.IsNullOrEmpty(msg.BoneName)) SplitBodyInkCache(msg); // joiners get the pieces
            if (msg.Sender == Grimoire.LocalPlayerId || DrawingWorld.Instance == null) return; // self-echo

            // the copy the ranges index into: same node count, nearest first node
            _pieceBuf.Clear();
            FindNetStrokes(msg.Owner, msg.SrcId, _pieceBuf);
            Stroke src = null;
            float best = float.MaxValue;
            foreach (var s in _pieceBuf)
            {
                if (s.Nodes.Count != msg.SrcCount) continue;
                float d = (LocalFirst(s) - msg.SrcFirst).sqrMagnitude;
                if (d < best) { best = d; src = s; }
            }
            if (src == null) return;
            ApplyingRemote = true;
            try
            {
                DrawingWorld.Instance.ApplySplit(src, msg.From, msg.To, msg.Reverse, msg.Tiny, msg.Residue, msg.NewIds);
            }
            finally
            {
                ApplyingRemote = false;
            }
        }

        /// A cached body stroke that split: the cache holds the pieces instead,
        /// so a puppet built later wears the same ids the seal messages name.
        static void SplitBodyInkCache(SplitMsg msg)
        {
            if (!_bodyInk.TryGetValue(msg.Owner, out var list)) return;
            int at = -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i].StrokeId == msg.SrcId) { at = i; break; }
            if (at < 0) return;
            var srcMsg = list[at];
            list.RemoveAt(at);
            if (srcMsg.Points == null || srcMsg.Points.Length != msg.SrcCount) return; // drifted: better missing than wrong
            for (int i = 0; i < msg.From.Length; i++)
            {
                int from = Mathf.Max(0, msg.From[i]);
                int to = Mathf.Min(msg.To[i], srcMsg.Points.Length - 1);
                int count = to - from + 1;
                if (count <= 0 || (!msg.Tiny[i] && count < DrawingConfig.MinStrokeNodes)) continue; // the split destroyed it
                var pts = new Vector3[count];
                for (int k = 0; k < count; k++)
                    pts[k] = srcMsg.Points[msg.Reverse[i] ? to - k : from + k];
                var piece = srcMsg;
                piece.Points = pts;
                piece.StrokeId = msg.NewIds[i];
                piece.LiveId = 0;
                piece.ClusterOwners = null;
                piece.ClusterIds = null;
                piece.ReadRune = 0;
                piece.ReadScore = 0f;
                list.Insert(at++, piece);
            }
        }

        /// The book takes their drawing: the copies thin out over the same
        /// time, then burn (the redrawn rune follows as StrokeMsgs). An
        /// InkFxRestore in the meantime widens them back and this stops.
        static readonly HashSet<Stroke> _fadeCancelled = new HashSet<Stroke>();

        IEnumerator FadeRemote(List<Stroke> fading, float seconds)
        {
            foreach (var s in fading) _fadeCancelled.Remove(s);
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Lerp(1f, 0.15f, Mathf.Clamp01(t / seconds));
                foreach (var s in fading)
                {
                    if (s == null || !s.Alive) continue;
                    if (_fadeCancelled.Remove(s)) yield break; // the owner's pen aborted it
                    s.SetEvaporation(k);
                }
                yield return null;
            }
            foreach (var s in fading)
                if (s != null && s.Alive) s.Burn();
        }

        /// Someone walked into the lobby, or left it. Quiet while a scene settles,
        /// when everyone already here appears at once.
        static void LobbyCue(bool joined)
        {
            if (ActiveScene.Name != "Lobby" || !NetGame.Connected) return;
            if (Time.timeSinceLevelLoad < 3f || Time.unscaledTime < _lobbyCueAt) return;
            _lobbyCueAt = Time.unscaledTime + 0.5f;
            Juice.Sound2D(joined ? Sfx.JoinedLobby : Sfx.LeftLobby);
        }
        static float _lobbyCueAt;

        void RemoveAvatar(int id)
        {
            if (_avatars.TryGetValue(id, out var avatar) && avatar != null)
            {
                Destroy(avatar.gameObject);
                LobbyCue(false);
            }
            _avatars.Remove(id);
            _outfits.Remove(id); // a leaver's hat colour is free again
            _hats.Remove(id);
            _disguises.Remove(id);
            _bodyInk.Remove(OwnerIdOf(id));
            AvatarGone?.Invoke(OwnerIdOf(id));
        }
    }

    /// A remote player's puppet: the same body prefab, driven by their
    /// presence - motion, legs, look, hands, ragdoll and the status looks.
    public class NetAvatar : MonoBehaviour
    {
        public static readonly System.Collections.Generic.List<NetAvatar> All
            = new System.Collections.Generic.List<NetAvatar>();

        public int Id { get; private set; }
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        // presence samples, rendered a little behind so the puppet glides
        // between them instead of chasing the newest one
        const float RenderDelay = 0.15f, MaxExtrapolate = 0.3f;
        struct Sample { public float T; public Vector3 Pos; public float Yaw; }
        readonly Sample[] _samples = new Sample[4];
        int _sampleCount;
        Vector3 _prevSample; // the last presence position, to tell a teleport from a run
        readonly Sample[] _ghostSamples = new Sample[4];
        int _ghostSampleCount;

        Vector3 _targetPos;
        float _targetYaw, _targetPitch;
        ushort _flags;
        byte _team = 255;
        Vector2 _move;
        Vector3 _prevTarget;
        float _prevAt = -1f;
        /// How they move, from the last two states: creatures read a friend closing in.
        public Vector3 Velocity { get; private set; }
        Transform _head, _spine1; // bend with their look pitch, after the animator
        // the owner's rig on the puppet: crouch yaw fix, noodle walk, posing shoulders
        Quaternion _modelRot = Quaternion.identity; // the body after FaceForward
        float _crouchYawW, _bob;
        Quaternion _bindSpine1, _bindHead;
        Transform _clavL, _clavR, _armL, _armR;
        Quaternion _clavLRest, _clavRRest, _armLRest, _armRRest, _armLWritten, _armRWritten;

        // the body: animator, bones, book, wand, hands
        Animator _anim;
        bool _animChecked, _hasAirParams, _hasCrouch;
        readonly System.Collections.Generic.List<Rigidbody> _bones
            = new System.Collections.Generic.List<Rigidbody>();
        readonly System.Collections.Generic.List<(Transform t, Vector3 pos, Quaternion rot)> _bind
            = new System.Collections.Generic.List<(Transform, Vector3, Quaternion)>();
        bool _ragdolling;
        GrimoirePages _book;
        WandInk _wand;
        GameObject _wandGo;
        EmoteRig _emoteRig;
        EmotePlayer _emotes;
        GooglyEyes _eyes;
        bool _moodHeld, _pupilsRed, _swelling;
        bool _restCaptured, _doll;
        HandIK _ik;
        Element _el;
        float _hostHurtAt = -10f; // the last hit this machine gave or saw on this body
        float _hostHealAt = -10f; // the last heal shipped to this body
        byte _bookState;
        sbyte _emote = -1;
        byte _heldKind;
        int _heldId;

        /// What their hands are on, as this machine knows it (null = nothing known).
        public Transform HeldTransform { get; private set; }
        public bool BookOpen => (_bookState & 128) != 0;
        public bool HandsFull => (_flags & 512) != 0;
        public float Pitch => _targetPitch;
        public Transform Head => _head;

        // the looks a state edge brings
        GameObject _heartFx;
        readonly System.Collections.Generic.List<GameObject> _ice
            = new System.Collections.Generic.List<GameObject>();
        float _headFxAt, _shineUntil;
        MaterialPropertyBlock _lookBlock;
        readonly System.Collections.Generic.HashSet<Renderer> _lookMine
            = new System.Collections.Generic.HashSet<Renderer>();

        void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (_head == null && t.name.EndsWith(":Head")) _head = t;
                else if (_spine1 == null && t.name.EndsWith(":Spine1")) _spine1 = t;
            }
            // the wobble's ground when no animator writes these bones
            if (_spine1 != null) _bindSpine1 = _spine1.localRotation;
            if (_head != null) _bindHead = _head.localRotation;
            _el = GetComponent<Element>();
        }

        readonly Footfalls _feet = new Footfalls();

        public bool Downed => (_flags & 1) != 0;
        public bool PenDown => (_flags & 8) != 0;
        public bool Wandless => (_flags & 256) != 0; // creatures on the host: prey, not a threat
        /// Their wand's fill, from their presence: an acolyte drinks only from a wand that has ink.
        public float InkFraction { get; private set; } = 1f;

        /// A remote player's downed flag flipped: (owner id, downed now).
        public static event System.Action<int, bool> DownedChanged;

        bool _acolyte;
        public bool Acolyte => _acolyte;

        /// Their ghost is flying and hovering at its own body: the revive law's home.
        public bool GhostHome => _ghost != null && (_ghostTarget - _targetPos).sqrMagnitude <= 2.5f * 2.5f;

        /// The nearest downed friend whose ghost is home, for a living rescuer.
        public static NetAvatar RevivableNear(Vector3 at, bool acolyte, float range)
        {
            NetAvatar best = null;
            float bestSqr = range * range;
            foreach (var a in All)
            {
                if (a == null || !a.Downed || !a.GhostHome) continue;
                if (a._acolyte != acolyte && !MapRules.Together) continue; // a together map: any player
                float d = (a.transform.position - at).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = a; }
            }
            return best;
        }

        /// Held or worn by another system - not skin, not robe, not a bone.
        static bool UnderProp(Transform t, Transform stop)
        {
            for (var w = t; w != null && w != stop; w = w.parent)
            {
                string n = w.name;
                if (n == "Wand" || n == "Grimoire" || n == "Shapes" || n.StartsWith("Socket.")) return true;
            }
            return false;
        }

        public static NetAvatar Build(int id)
        {
            GameObject go;
            System.Collections.Generic.List<GameObject> costume = null;
            SocketSet sockets = null;
            Animator anim = null;
            HandIK ik = null;
            GrimoirePages book = null;
            WandInk wand = null;
            GameObject wandGo = null;
            EmoteRig emoteRig = null;
            GooglyEyes puppetEyes = null;
            var bones = new System.Collections.Generic.List<Rigidbody>();
            Quaternion modelRot = Quaternion.identity;
            Transform clavL = null, clavR = null, upL = null, upR = null;
            Quaternion clavLRest = Quaternion.identity, clavRRest = Quaternion.identity;
            Quaternion upLRest = Quaternion.identity, upRRest = Quaternion.identity;
            // the SAME body the local player wears - what exists in the game
            // exists for everyone, so a friend is never a different model
            var prefab = CollectionManager.PlayerBody;
            if (prefab != null)
            {
                go = new GameObject($"NetPlayer_{id}");
                // makes their body-ink copies Persistent: evaporation-exempt and
                // never consumed by spells, same as on the owner's machine
                go.AddComponent<PersistentInkSurface>();
                var body = Object.Instantiate(prefab, go.transform);
                body.name = "Body";
                body.transform.localPosition = Vector3.zero;   // set from its own height below
                // their grimoire stays fully visible; its page arrows do not
                foreach (var pages in body.GetComponentsInChildren<GrimoirePages>(true))
                {
                    pages.HideForRemote(NetSync.OwnerIdOf(id));
                    if (book == null) book = pages;
                }
                Transform armL = null, armR = null, handL = null, handR = null, head = null;
                Transform footL = null, toeL = null, headTop = null;
                var allBones = body.GetComponentsInChildren<Transform>(true);
                foreach (var t in allBones)
                {
                    if (t.name.EndsWith("LeftArm")) armL = t;
                    else if (t.name.EndsWith("RightArm")) armR = t;
                    else if (t.name.EndsWith("LeftHand")) handL = t;
                    else if (t.name.EndsWith("RightHand")) handR = t;
                    else if (t.name.EndsWith("LeftToeBase")) toeL = t;
                    else if (t.name.EndsWith("LeftFoot")) footL = t;
                    else if (t.name.EndsWith(":Head")) head = t;
                    else if (t.name.Contains("HeadTop")) headTop = t;
                }

                // THE NETWORKED POINT IS MID-BODY, so the model hangs half its
                // own height below it. Measured off the bones the same way the
                // local rig measures - a hardcoded drop fits one body height
                // and sinks or floats every other.
                var crown = headTop != null ? headTop : head;
                if (footL != null && crown != null)
                {
                    float bodyH = Mathf.Clamp(crown.position.y - footL.position.y, 0.8f, 3f);
                    body.transform.localPosition = new Vector3(0f, -bodyH * 0.5f, 0f);
                }

                // the owner's order - arms down, emote rig, then face forward: the
                // elbow hinges are read off the bones as they stand at that moment,
                // and a rig built after the yaw bends nothing the owner bent
                LowerArm(armL, handL);
                LowerArm(armR, handR);
                // the shoulders' rest beside the arms' rest, as the owner's rig reads them
                clavL = SocketSet.FindBone(allBones, "LeftShoulder");
                clavR = SocketSet.FindBone(allBones, "RightShoulder");
                if (clavL != null) clavLRest = clavL.localRotation;
                if (clavR != null) clavRRest = clavR.localRotation;
                upL = armL;
                upR = armR;
                if (upL != null) upLRest = upL.localRotation;
                if (upR != null) upRRest = upR.localRotation;
                emoteRig = go.AddComponent<EmoteRig>();
                EmoteRig.Populate(emoteRig, allBones, go.transform);
                CharacterRig.FaceForward(body.transform, footL, toeL, go.transform.forward);
                modelRot = body.transform.localRotation; // the crouch yaw fix turns from here

                // the same locomotion clips the local rig plays; the animator
                // faces the model, the presence feeds it
                anim = body.GetComponent<Animator>();
                if (anim != null && CharacterLibrary.Anim != null)
                {
                    anim.runtimeAnimatorController = CharacterLibrary.Anim;
                    anim.applyRootMotion = false;
                    anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    ik = body.GetComponent<HandIK>();
                    if (ik == null) ik = body.AddComponent<HandIK>();
                    ik.RestCaptured = false; // rest is read in the first LateUpdate, as on the owner
                }
                else anim = null;

                // the prefab's kinematic bone bodies: the ragdoll when their
                // flags say so, kinematic and home the rest of the time
                foreach (var rb in body.GetComponentsInChildren<Rigidbody>(true))
                {
                    if (rb == null || UnderProp(rb.transform, body.transform)) continue;
                    rb.isKinematic = true;
                    rb.interpolation = RigidbodyInterpolation.None;
                    bones.Add(rb);
                }

                // friends wear the team outfit too (retinted in Target) -
                // dressed with THEIR announced outfit, not ours
                sockets = SocketSet.Build(body, go.transform);
                costume = Wardrobe.DressPlayer(
                    sockets, new Color(0.35f, 0.55f, 0.9f), null,
                    outfitCode: NetSync.OutfitOf(id));
                // the authored body keeps its own material, as it does for the owner
                var skin = body.GetComponentInChildren<BodySkin>(true);
                var smr = skin != null && skin.Renderer != null ? skin.Renderer
                    : body.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null) smr.updateWhenOffscreen = true;

                // the baked wand shows their ink level and wandless state
                var gripR = sockets.Get("HandR");
                var bakedWand = gripR != null ? gripR.Find("Wand") : null;
                if (bakedWand != null)
                {
                    wandGo = bakedWand.gameObject;
                    wand = bakedWand.GetComponent<WandInk>();
                    if (wand == null) wand = bakedWand.gameObject.AddComponent<WandInk>();
                }

                // an authored body brings its OWN eyes; attaching a second
                // pair on top of them is the baked-prefab trap
                var faceEyes = body.GetComponentInChildren<GooglyEyes>(true);
                bool authoredEyes = faceEyes != null;
                if (!authoredEyes)
                    faceEyes = GooglyEyes.Attach(head != null ? head : go.transform,
                        head != null ? 0f : 0.6f, CharacterRig.EyeScale);

                // only a code-built pair gets placed by the shared knobs; a
                // pair that came with the body stays exactly where it was put
                if (!authoredEyes && head != null && faceEyes != null)
                {
                    // one knob for all eyes: CharacterRig.EyeLocalPos
                    faceEyes.transform.localPosition = CharacterRig.EyeLocalPos;
                    faceEyes.transform.localRotation = Quaternion.identity;
                    faceEyes.transform.localScale = Vector3.one * CharacterRig.EyeRigScale;
                }
                faceEyes?.SetVisible(true);
                puppetEyes = faceEyes;
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = $"NetPlayer_{id}";
                go.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
                go.GetComponent<Renderer>().sharedMaterial =
                    MatterFX.Get(new Color(0.35f, 0.55f, 0.9f), MoteShade.Opaque); // friendly blue
                Object.Destroy(go.GetComponent<Collider>()); // presence, not physics (yet)
                var eyes = GooglyEyes.Attach(go.transform, 0.6f, 1.4f);
                eyes.SetVisible(true);
                puppetEyes = eyes;
            }

            // A REMOTE BODY WEARS THE SAME NAME ITS OWNER GAVE ITSELF, so a
            // spell that hits the puppet reaches the real person. Nothing has
            // to search for a network component: the id IS on the object.
            int owner = NetSync.OwnerIdOf(id);
            var hurtBox = go.GetComponent<Element>();
            if (hurtBox == null) hurtBox = go.AddComponent<Element>();
            hurtBox.Rename(Element.IdFor("player:" + owner));
            hurtBox.RemoveOnDeath = false;   // a puppet is never destroyed locally

            // ON THE HOST THIS IS THE REAL STORE for that player's strength -
            // it does the subtracting and answers with HealthMsg, and the owner
            // mirrors it. So it has to start at their true ceiling, not 100.
            hurtBox.MaxStrength = Sides.MaxHealthFor(owner);
            hurtBox.Health = hurtBox.MaxStrength;
            // a player is alive, the same assertion the owner's controller makes:
            // OnlyLiving spells and poison read Int > 0 to land at all
            if (hurtBox.Natural.Int <= 0f)
            {
                var n = hurtBox.Natural; n.Int = 1f; n.Courage = 1f; hurtBox.Natural = n;
                var d = hurtBox.Data; d.Int = 1f; d.Courage = 1f; hurtBox.Data = d;
            }

            // the body wears its damage here too: flames, eye wisps, drips
            // read off the same Element the host's beat and StateMsg write
            if (go.GetComponent<BodyFx>() == null) go.AddComponent<BodyFx>();

            var a = go.AddComponent<NetAvatar>();
            a.Id = id;
            hurtBox.OnDamaged += (_, __) => a._hostHurtAt = Time.time;
            a._targetPos = go.transform.position;
            a._costume = costume;
            a._sockets = sockets;
            a._anim = anim;
            a._book = book;
            a._wand = wand;
            a._wandGo = wandGo;
            a._emoteRig = emoteRig;
            a._eyes = puppetEyes;
            a._modelRot = modelRot;
            a._clavL = clavL;
            a._clavR = clavR;
            a._armL = upL;
            a._armR = upR;
            a._clavLRest = clavLRest;
            a._clavRRest = clavRRest;
            a._armLRest = a._armLWritten = upLRest;
            a._armRRest = a._armRWritten = upRRest;
            a._bones.AddRange(bones);
            foreach (var rb in bones)
                a._bind.Add((rb.transform, rb.transform.localPosition, rb.transform.localRotation));
            if (emoteRig != null)
            {
                a._emotes = go.AddComponent<EmotePlayer>();
                a._emotes.Remote = true;
            }
            if (ik != null) { ik.Puppet = a; a._ik = ik; }
            a.RefreshLook();
            NameTag.Give(a);
            return a;
        }

        System.Collections.Generic.List<GameObject> _costume;
        SocketSet _sockets;

        // ---- their ghost ----
        Transform _ghost;
        /// Their spirit (the NetGhost) while it flies, else null.
        public Transform Ghost => _ghost;
        Vector3 _ghostTarget;
        float _ghostYaw;

        /// Their spirit: shown while their flags say ghost, hidden the moment
        /// they are revived. The wisp is the ghost prefab, the same one the
        /// local player flies (GhostState.SharedPrefab) - nothing built here.
        bool _homeStamped;

        /// The home biome the host's law measures this body from (Element.Natural).
        /// Safe to repeat: the stamp re-derives from the authored snapshot.
        public void StampHome(Vector3 at)
        {
            _homeStamped = true;
            // warmth is DeriveFrom's job: Awake derived this body before Build
            // marked it alive, and BiomeStamp derives only where a biome is -
            // in the lobby that left every puppet at room temperature, iced
            GetComponent<Element>()?.DeriveFrom(at);
            BiomeStamp.Apply(gameObject, at);
        }

        /// Their eyes as on their own screen: mood, red pupils, the wide
        /// moment, and the spot the pupils point at. A fresh gaze holds the
        /// puppet's own AutoWatch off for one presence beat.
        public void TargetEyes(byte bits, Vector3 gaze)
        {
            if (_eyes == null) return;
            var mood = (EyeMood)(bits & 7);
            if (mood != EyeMood.Neutral) { _eyes.SetMood(mood, 0.3f); _moodHeld = true; }
            else if (_moodHeld) { _eyes.SetMood(EyeMood.Neutral, 0f); _moodHeld = false; }
            bool red = (bits & 8) != 0;
            if (red != _pupilsRed)
            {
                _pupilsRed = red;
                _eyes.SetPupilTint(red ? DrawingConfig.MindControlEyeColor : (Color?)null);
            }
            bool swell = (bits & 16) != 0;
            if (swell && !_swelling) _eyes.Swell(DrawingConfig.ChargeTellSeconds, DrawingConfig.ChargeTellEyeSwell);
            _swelling = swell;
            if (gaze != Vector3.zero)
            {
                _eyes.LookTarget = gaze;
                _eyes.HoldGazeUntil = Time.time + 0.3f;
            }
        }

        public void TargetGhost(bool flying, Vector3 at, float yaw, bool acolyte)
        {
            if (acolyte != _acolyte)
            {
                _acolyte = acolyte;
                RefreshLook(); // the side shows on the wand and the robe
            }
            _ghostTarget = at;
            _ghostYaw = yaw;

            if (!flying)
            {
                if (_ghost != null) { Destroy(_ghost.gameObject); _ghost = null; }
                _ghostSampleCount = 0;
                return;
            }
            // a loop wrap moves the spirit too: it reappears instead of streaking across
            if (_ghostSampleCount > 0 && (at - _ghostSamples[_ghostSampleCount - 1].Pos).sqrMagnitude > 16f)
            {
                _ghostSampleCount = 0;
                if (_ghost != null) _ghost.position = at;
            }
            Push(_ghostSamples, ref _ghostSampleCount, at, yaw);
            if (_ghost == null)
            {
                var prefab = GhostState.SharedPrefab;
                if (prefab == null) return; // the slot is empty - GhostState already said so
                var go = Instantiate(prefab, at, Quaternion.Euler(0f, yaw, 0f));
                go.name = "NetGhost";
                foreach (var c in go.GetComponentsInChildren<Camera>(true)) c.enabled = false;
                foreach (var a in go.GetComponentsInChildren<AudioListener>(true)) a.enabled = false;
                foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
                Color side = GhostState.GhostSideColor(acolyte);
                Color hat = NetSync.HatOf(Id) ?? side; // their pillar colour, as on their own ghost
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (GhostState.Named(r.transform, "eye")) continue;
                    PillarBeam.Tint(r, GhostState.Named(r.transform, "hat") ? hat : side);
                }
                _ghost = go.transform;
                Juice.Sound(Sfx.GhostOut, at);
            }
        }

        void OnDestroy() { if (_ghost != null) Destroy(_ghost.gameObject); }

        // ---- their disguise ----
        GameObject _worn;
        string _wornShape;
        Biome _wornHome;
        Quaternion _wornRot = Quaternion.identity;
        float _wornLift;
        Vector3 _wornCenterLocal, _wornLossy = Vector3.one;
        readonly System.Collections.Generic.List<Renderer> _bodyHidden
            = new System.Collections.Generic.List<Renderer>();

        string _pendingShape; // announced, not resolved here yet
        float _pendingRetry;

        /// Hiding as a prop right now - lock-on and golems pass them by.
        public bool Disguised => _worn != null && _worn.activeSelf;
        Vector3 _wornHomeAt;  // where the worn prop was raised

        /// The prop they wear, when disguised.
        public Transform Worn => _worn != null ? _worn.transform : null;

        /// Their announced disguise: the same object worn the same way as on
        /// their own machine, and the ground that raised it as their own.

        public void ApplyDisguise(bool worn, string shape, Quaternion rot, float lift, bool poof = false)
        {
            WearDisguise(worn, shape, rot, lift, poof);
            // the small acolyte body is the true form only: worn, the object is its own size
            GetComponent<Element>()?.RefreshLook();
        }

        void WearDisguise(bool worn, string shape, Quaternion rot, float lift, bool poof)
        {
            var el = GetComponent<Element>();
            _pendingShape = null;
            if (!worn)
            {
                if (_worn != null) _worn.SetActive(false);
                foreach (var r in _bodyHidden) if (r != null) r.enabled = true;
                _bodyHidden.Clear();
                if (el != null) el.ShedGround();
                return;
            }
            if (worn && poof && FxLibrary.I != null) // every machine replays the DisguiseMsg
                FxLibrary.Spawn(FxLibrary.I.Poof, transform.position + Vector3.up * 0.5f, null, 0f, false);
            if (_worn == null || shape != _wornShape)
            {
                var source = ShapeShift.ResolveShape(shape, out var home);
                if (source == null)
                {
                    // not here yet: hide the body and keep asking
                    Debug.LogWarning($"[SpellyZombie] NetPlayer_{Id}: disguise '{shape}' not on this machine");
                    _pendingShape = shape;
                    _pendingRetry = 0.5f;
                    _wornRot = rot;
                    _wornLift = lift;
                    if (_worn != null) _worn.SetActive(false);
                    return;
                }
                if (_worn != null) Destroy(_worn);
                _worn = ShapeShift.CloneShape(source, transform);
                _wornShape = shape;
                _wornHome = home;
                // a scene prop was raised where it stands; a lent prefab by its biome
                _wornHomeAt = source.gameObject.scene.IsValid() ? source.position
                    : home != null ? home.transform.position : transform.position;
                _wornLossy = source.lossyScale;
                _worn.transform.localScale = _wornLossy;
                _worn.transform.rotation = rot;
                _wornCenterLocal = ShapeShift.FindObjectCenterLocal(_worn.transform, true);
            }
            _wornRot = rot;
            _wornLift = lift;
            _worn.SetActive(true);
            if (el != null) el.WearGround(_wornHomeAt, _wornHome);
        }

        /// A (re)announced outfit arrived after this avatar was built -
        /// strip the old costume and wear the new one, keeping the team tint.
        public void ApplyOutfit(string code)
        {
            if (_sockets == null) return;
            if (_costume != null)
                foreach (var p in _costume)
                    if (p != null) Destroy(p);
            _costume = Wardrobe.DressPlayer(_sockets, new Color(0.35f, 0.55f, 0.9f), null,
                outfitCode: code);
            if (_team != 255)
                Wardrobe.Retint(_costume,
                    MatchLobby.TeamColors[Mathf.Min(_team, (byte)(MatchLobby.TeamColors.Length - 1))]);
            RefreshLook();
        }

        /// The side on the wand and robe, the pillar colour on the hat - the
        /// same paint the owner's SideLook and HatColor put on their own body.
        void RefreshLook()
        {
            if (_sockets == null) return;
            if (_lookBlock == null) _lookBlock = new MaterialPropertyBlock();
            var gripR = _sockets.Get("HandR");
            var wand = gripR != null ? gripR.Find("Wand") : null;
            SideLook.Paint(transform, wand, _acolyte, NetSync.HatOf(Id), _lookBlock, _lookMine,
                skip: _worn != null ? _worn.transform : null);
            // the side's body size: a client puppet never beats, so it is asked here
            GetComponent<Element>()?.RefreshLook();
        }

        /// Swing an arm from the T-pose toward hanging down (sign-proof).
        internal static void LowerArm(Transform upper, Transform hand)
        {
            if (upper == null || hand == null) return;
            Vector3 dir = hand.position - upper.position;
            if (dir.sqrMagnitude < 1e-6f) return;
            Vector3 target = Vector3.Slerp(dir.normalized, Vector3.down, 0.75f);
            upper.rotation = Quaternion.FromToRotation(dir, target) * upper.rotation;
        }

        // ---- presence buffer ----
        static void Push(Sample[] buf, ref int count, Vector3 pos, float yaw)
        {
            if (count == buf.Length)
            {
                for (int i = 1; i < count; i++) buf[i - 1] = buf[i];
                count--;
            }
            buf[count++] = new Sample { T = Time.time, Pos = pos, Yaw = yaw };
        }

        /// Where the samples say they were `RenderDelay` ago - between two
        /// samples when both are known, dead reckoned a short way past the
        /// last one when the next has not arrived.
        static bool Render(Sample[] buf, int count, Vector3 vel, out Vector3 pos, out float yaw)
        {
            pos = default; yaw = 0f;
            if (count == 0) return false;
            float t = Time.time - RenderDelay;
            var last = buf[count - 1];
            if (t >= last.T)
            {
                float over = Mathf.Min(t - last.T, MaxExtrapolate);
                pos = last.Pos + vel * over;
                yaw = last.Yaw;
                return true;
            }
            for (int i = count - 1; i > 0; i--)
            {
                var a = buf[i - 1];
                var b = buf[i];
                if (t < a.T) continue;
                float k = b.T > a.T ? Mathf.InverseLerp(a.T, b.T, t) : 1f;
                pos = Vector3.Lerp(a.Pos, b.Pos, k);
                yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, k);
                return true;
            }
            pos = buf[0].Pos;
            yaw = buf[0].Yaw;
            return true;
        }

        public void Target(Vector3 pos, float yaw, ushort flags, byte team, float pitch = 0f,
            Vector2 move = default)
        {
            float now = Time.time;
            if (_prevAt >= 0f && now > _prevAt + 0.01f) Velocity = (pos - _prevTarget) / (now - _prevAt);
            _prevTarget = pos;
            _prevAt = now;
            _targetPos = pos;
            _targetYaw = yaw;
            _targetPitch = pitch;
            _move = move;
            // over 4 m in one tick is a teleport, not a run: the puppet
            // vanishes and reappears instead of dashing across the gap
            bool jumped = _sampleCount > 0 && (pos - _prevSample).sqrMagnitude > 16f;
            _prevSample = pos;
            if (jumped) { _sampleCount = 0; Velocity = Vector3.zero; }
            Push(_samples, ref _sampleCount, pos, yaw);
            if (_sampleCount == 1) // a new puppet stands where its first word says, no glide from the origin
            {
                transform.position = pos;
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            if (!_homeStamped) StampHome(pos); // no spawn point on record: born where first seen

            // the stand-in body mirrors the downed gate: a corpse's puppet
            // takes no damage and pays no wand credit on this machine either
            bool downedEdge = ((flags ^ _flags) & 1) != 0;
            if (downedEdge)
            {
                var standIn = Element.ById(Element.IdFor("player:" + NetSync.OwnerIdOf(Id)));
                if (standIn != null)
                {
                    standIn.DeadStill = (flags & 1) != 0;
                    // back on their feet: the host's store follows the revive (50, as the body sets)
                    if (!standIn.DeadStill && standIn.Health <= 0f) standIn.Revive(50f);
                }
            }
            bool frozenEdge = ((flags ^ _flags) & 128) != 0;
            _flags = flags;
            if (downedEdge)
            {
                bool down = (flags & 1) != 0;
                DownedChanged?.Invoke(NetSync.OwnerIdOf(Id), down);
                ShowDowned(down);
            }
            if (frozenEdge)
            {
                if ((flags & 128) != 0) SimpleFPSController.FreezeOntoBones(transform, _ice);
                else SimpleFPSController.ThawIce(transform, _ice);
            }
            bool wantRagdoll = _bones.Count > 0 && (flags & 3) != 0;
            if (wantRagdoll != _ragdolling) SetRagdoll(wantRagdoll);

            if (team != _team) // friends wear their team color
            {
                _team = team;
                var teamColor = MatchLobby.TeamColors[
                    Mathf.Min(team, (byte)(MatchLobby.TeamColors.Length - 1))];
                // the OUTFIT carries the team color (the body stays skin) -
                // capsule fallback tints itself since it has no clothes
                if (_costume != null)
                    Wardrobe.Retint(_costume, teamColor);
                else
                {
                    var rend = GetComponent<Renderer>();
                    if (rend != null)
                        rend.sharedMaterial = MatterFX.Get(teamColor, MoteShade.Opaque);
                }
                RefreshLook(); // the side hint goes back over the fresh team colour
            }
        }

        /// The rest of their presence: book, wand ink, pose and cargo.
        public void TargetHands(byte book, byte ink, sbyte emote, byte heldKind, int held)
        {
            bool open = (book & 128) != 0;
            if (book != _bookState)
            {
                _bookState = book;
                if (_book != null && _book.gameObject.activeSelf) _book.RemoteSet(open, book & 127);
            }
            InkFraction = ink / 255f;
            if (_wand != null) _wand.RemoteDrive(ink / 255f, (_flags & 256) == 0);
            if (_book != null) _book.RemoteTuck(HandsFull);

            // slot 1's tools show as on the owner's screen: pen selected in
            // first person; the easel keeps an open book up
            bool penShown = (_flags & 2048) != 0;
            if (_wandGo != null && _wandGo.activeSelf != penShown) _wandGo.SetActive(penShown);
            bool bookAlive = penShown || ((_flags & 1024) != 0 && open);
            if (_book != null && _book.gameObject.activeSelf != bookAlive)
            {
                _book.gameObject.SetActive(bookAlive);
                if (bookAlive) _book.RemoteSet(open, book & 127, fx: false); // back as it was
            }

            if (heldKind != _heldKind || held != _heldId)
            {
                _heldKind = heldKind;
                _heldId = held;
            }
            HeldTransform = NetSync.ResolveHeld(_heldKind, _heldId);

            if (emote != _emote)
            {
                _emote = emote;
                if (_emotes != null)
                {
                    if (emote < 0) _emotes.StopToRest();
                    else if (_emotes.ActiveSlot != emote)
                    {
                        // the live sculpt has no built-in: wait for its EmoteMsg
                        var def = NetSync.EmoteDefOf(NetSync.OwnerIdOf(Id), emote)
                            ?? (emote == NetSync.LiveEmoteSlot ? null : EmoteLibrary.GetSlot(emote));
                        if (def != null && def.frames.Count > 0) _emotes.Play(def, emote);
                    }
                }
            }
        }

        /// Their health as their own machine keeps it (the player heal runs
        /// there). The host's store and every other copy follow: down at once,
        /// up only when no hit has landed here since, so an older report never
        /// undoes a hit.
        public void FollowHealth(float health)
        {
            var el = _el != null ? _el : GetComponent<Element>();
            if (el == null || el.DeadStill || health <= 0f) return;
            // a report from before a shipped heal must not undo it
            if (health < el.Health - 0.05f) { if (Time.time - _hostHealAt > 0.5f) el.Health = health; }
            else if (health > el.Health + 0.05f && Time.time - _hostHurtAt > 1f) el.Health = health;
        }

        /// A heal shipped to their machine lands in this copy too, or the next
        /// HealthMsg after a hit would carry the unhealed value back to them.
        public void TakeHeal(float amount)
        {
            var el = _el != null ? _el : GetComponent<Element>();
            if (el == null || el.DeadStill || amount <= 0f) return;
            el.Health = Mathf.Min(Sides.MaxHealthFor(NetSync.OwnerIdOf(Id)), el.Health + amount);
            _hostHealAt = Time.time;
        }

        /// The keyframes they are posing with arrived: play exactly those.
        public void ReplayEmote(sbyte slot, EmoteDef def)
        {
            if (_emotes == null || def == null || def.frames.Count == 0) return;
            _emote = slot;
            _emotes.Play(def, slot);
        }

        /// The downed edge: the broken heart over the body, the soul leaving,
        /// the sting - and, on the host, an acolyte's death cloud and blast.
        void ShowDowned(bool down)
        {
            if (!down)
            {
                if (_heartFx != null) { Destroy(_heartFx); _heartFx = null; }
                Juice.Sound(Sfx.Revival, transform.position);
                return;
            }
            // the horde thinks here only: a friend's death reaches it the way its own does
            if (NetGame.IsAuthority) WorldEvents.Report(WorldEventKind.Death, transform.position, 2f);
            if (!Juice.Sound(Sfx.Death, transform.position)) Juice.Sting(transform.position);
            if (FxLibrary.I != null) // presence-driven: every machine shows it on its own
            {
                FxLibrary.Spawn(FxLibrary.I.SoulsOut, transform.position + Vector3.up * 1.2f, null, 0f, false);
                if (_heartFx == null)
                    _heartFx = FxLibrary.Spawn(FxLibrary.I.BrokenHeart,
                        transform.position + Vector3.up * 2.1f, transform, 0f, false);
            }
            if (_acolyte) NetSync.HostAcolyteDeath(transform.position + Vector3.up * 0.9f);
        }

        /// A revive in progress on this body, seen from a third machine.
        public void ReviveShine()
        {
            if (Time.time < _shineUntil || FxLibrary.I == null) return;
            _shineUntil = Time.time + 0.9f;
            FxLibrary.Spawn(FxLibrary.I.HealShine, transform.position + Vector3.up * 0.35f,
                transform, 1.1f, false);
        }

        /// The prefab's bone bodies go live (a sprawl, a tumble, a corpse) or
        /// come home to the animation - the same handover CharacterRig makes.
        void SetRagdoll(bool on)
        {
            _ragdolling = on;
            if (on)
            {
                if (_anim != null) _anim.enabled = false;
                _emotes?.Interrupt();
                float drag = Downed ? 2.2f : 0.12f;
                foreach (var rb in _bones)
                {
                    if (rb == null) continue;
                    rb.isKinematic = false;
                    rb.useGravity = true;
                    rb.linearVelocity = Velocity;
                    rb.linearDamping = drag;
                    rb.angularDamping = 3f;
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                }
                return;
            }
            foreach (var rb in _bones)
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.interpolation = RigidbodyInterpolation.None;
                }
            foreach (var (t, pos, rot) in _bind)
            {
                if (t == null) continue;
                t.localPosition = pos;
                t.localRotation = rot;
            }
        }

        void Update()
        {
            // a disguise that arrived before its prop grew here
            if (_pendingShape != null && (_pendingRetry -= Time.deltaTime) <= 0f)
            {
                _pendingRetry = 0.5f;
                if (ShapeShift.ResolveShape(_pendingShape, out _) != null)
                    ApplyDisguise(true, _pendingShape, _wornRot, _wornLift);
            }

            // their spirit glides on the same buffer their body uses
            if (_ghost != null && Render(_ghostSamples, _ghostSampleCount, Vector3.zero,
                    out var gpos, out var gyaw))
            {
                _ghost.position = Vector3.Lerp(_ghost.position, gpos, Time.deltaTime * 25f);
                _ghost.rotation = Quaternion.Slerp(_ghost.rotation,
                    Quaternion.Euler(0f, gyaw, 0f), Time.deltaTime * 12f);
            }

            // a live doll lies where it fell; the root only moves it again
            // once the animation has the bones back
            if (!_ragdolling && Render(_samples, _sampleCount, Velocity, out var pos, out var yaw))
            {
                transform.position = Vector3.Lerp(transform.position, pos, Time.deltaTime * 25f);
                // the capsule stand-in has no bones to drop: it tips over instead
                float roll = _bones.Count > 0 ? 0f : (_flags & 1) != 0 ? 80f : (_flags & 2) != 0 ? 70f : 0f;
                var target = Quaternion.Euler(0f, yaw, roll);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * 12f);
            }

            // steps, jumps and landings from how the puppet moves (downed, ragdoll, ghost and ice are silent)
            _feet.Tick(transform.position, (_flags & 64) != 0, _ragdolling || (_flags & (1 | 2 | 4 | 128)) != 0,
                (_flags & 16) != 0, (_flags & 32) != 0, Time.deltaTime);

            bool posing = _emotes != null && _emotes.IsPosing;
            // posed still at the easel, the studio or pose mode: the animator
            // stands down and the joints rest, as on their own screen
            bool doll = !posing && (_flags & 1024) != 0;
            if (doll != _doll)
            {
                _doll = doll;
                if (doll && _restCaptured) _emoteRig?.ResetAll();
            }
            if (_anim != null)
            {
                _anim.enabled = !_ragdolling && !posing && !doll;
                if (_anim.enabled) DriveLegs();
            }

            // courage shows around the head, the way it does on the owner
            if (_el != null)
                SimpleFPSController.BlindHeadFx(
                    SpellPayload.ToHuman(8, _el.Data.Courage - _el.Natural.Courage),
                    transform.position + Vector3.up * 1.7f, ref _headFxAt);
        }

        /// The owner's planar speed shaped onto the ring the shift state
        /// owns, the same way CharacterRig feeds its own animator.
        void DriveLegs()
        {
            if (!_animChecked)
            {
                _animChecked = true;
                foreach (var p in _anim.parameters)
                {
                    if (p.name == "Airborne") _hasAirParams = true;
                    else if (p.name == "Crouched") _hasCrouch = true;
                }
            }
            bool crouch = (_flags & 16) != 0, sprint = (_flags & 32) != 0, air = (_flags & 64) != 0;
            var pilot = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            var rig = pilot != null ? pilot.GetComponent<CharacterRig>() : null;
            float moveSpeed = pilot != null ? pilot.MoveSpeed : 4.5f;
            float sprintSpeed = pilot != null ? pilot.SprintSpeed : 7f;
            float walkClip = rig != null ? rig.WalkClipSpeed : 2.4f;
            float runClip = rig != null ? rig.RunClipSpeed : 4.5f;

            float speed2d = _move.magnitude;
            float ring = crouch ? 2.25f : sprint ? 4.5f : 2f;
            float fullSpeed = sprint ? sprintSpeed : moveSpeed;
            if (crouch) fullSpeed *= 0.5f;
            float shape = speed2d < 0.05f ? 0f
                : ring * Mathf.Clamp01(speed2d / Mathf.Max(0.1f, fullSpeed)) / speed2d;
            _anim.SetFloat("MoveX", _move.x * shape, 0.12f, Time.deltaTime);
            _anim.SetFloat("MoveZ", _move.y * shape, 0.12f, Time.deltaTime);
            float refSpeed = sprint ? runClip : walkClip;
            float target = air || speed2d < 0.15f ? 1f
                : Mathf.Clamp(speed2d / Mathf.Max(0.3f, refSpeed), 0.7f, 2.2f);
            _anim.speed = Mathf.Lerp(_anim.speed, target, 8f * Time.deltaTime);
            if (_hasCrouch) _anim.SetBool("Crouched", crouch);
            if (_hasAirParams)
            {
                _anim.SetBool("Airborne", air);
                _anim.SetFloat("AirSpeed", speed2d, 0.1f, Time.deltaTime);
            }

            // the crouch clip's baked lean turned back, faded with the crouch (CharacterRig)
            float yawFix = rig != null ? rig.CrouchYawFix : 38f;
            _crouchYawW = Mathf.MoveTowards(_crouchYawW, crouch ? 1f : 0f, Time.deltaTime * 5f);
            _anim.transform.localRotation = _modelRot * Quaternion.Euler(0f, yawFix * _crouchYawW, 0f);
        }

        /// After the animator: the head follows their look, a disguised body
        /// stays hidden under its prop, and the prop keeps its true size,
        /// their rotation, and its centre where theirs is.
        void LateUpdate()
        {
            if (!_restCaptured && _anim != null)
            {
                _restCaptured = true;
                _emoteRig?.CaptureRest(); // post-animator, like the local rig
                if (_ik != null) _ik.RestCaptured = true;
            }

            // the owner's noodle walk and posing shoulders (CharacterRig), from their
            // movement and pose; the engraving calm is not on the wire
            if (!_ragdolling && _head != null)
            {
                if (_emotes != null && _emotes.IsPosing)
                {
                    var pilot = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
                    var rig = pilot != null ? pilot.GetComponent<CharacterRig>() : null;
                    float follow = rig != null ? rig.ClavicleFollow : 0.3f;
                    if (follow > 0.001f)
                    {
                        CharacterRig.FollowClavicle(_clavL, _clavLRest, _armL, _armLRest, ref _armLWritten, follow);
                        CharacterRig.FollowClavicle(_clavR, _clavRRest, _armR, _armRRest, ref _armRWritten, follow);
                    }
                }
                else if (!_doll)
                {
                    var lv = new Vector3(_move.x, Velocity.y, _move.y);
                    CharacterRig.Wobble(ref _bob, lv, lv.magnitude, 1f, _spine1, _head,
                        _anim != null && _anim.enabled, _bindSpine1, _bindHead);
                }
            }

            // composed over the animator's pose each frame, so it never accumulates
            // (a held pose has no animator rewrite under it: no bend then)
            if (!_ragdolling && _head != null && (_anim == null || _anim.enabled))
            {
                float bend = Mathf.Clamp(_targetPitch, -CharacterRig.FollowPitchCap, CharacterRig.FollowPitchCap);
                if (Mathf.Abs(bend) > 0.01f)
                {
                    if (_spine1 != null)
                        _spine1.rotation = Quaternion.AngleAxis(bend * CharacterRig.SpineFollowPitch, transform.right) * _spine1.rotation;
                    _head.rotation = Quaternion.AngleAxis(bend * CharacterRig.HeadFollowPitch, transform.right) * _head.rotation;
                }
            }

            if (!Disguised && _pendingShape == null) return;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                if (_worn != null && r.transform.IsChildOf(_worn.transform)) continue;
                r.enabled = false;
                if (!_bodyHidden.Contains(r)) _bodyHidden.Add(r);
            }
            if (!Disguised) return; // hidden while the prop is still missing here
            var ps = transform.lossyScale;
            _worn.transform.localScale = new Vector3(
                _wornLossy.x / Mathf.Max(1e-4f, ps.x),
                _wornLossy.y / Mathf.Max(1e-4f, ps.y),
                _wornLossy.z / Mathf.Max(1e-4f, ps.z));
            _worn.transform.rotation = _wornRot;
            Vector3 c = _worn.transform.TransformPoint(_wornCenterLocal);
            _worn.transform.position += transform.position + Vector3.up * _wornLift - c;
        }
    }
}
