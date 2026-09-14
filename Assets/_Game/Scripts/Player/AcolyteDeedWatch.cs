using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Watches the local acolyte's situations and turns them into deeds: the
    /// floating page hangs over the thing the situation is about while it is
    /// judged, the ledger grants the rune, the page shows its drawing. Local
    /// machine only - nothing here is sent anywhere.
    public class AcolyteDeedWatch : MonoBehaviour
    {
        const int KeyNear = 1000, KeyKill = 3000, KeyMyDeath = 4000, KeyPot = 5000,
                  KeyZombie = 8000, KeyBase = 9000, KeyScanAim = 9100, KeyRevert = 9200;
        const float HeadUp = 2.0f, PotUp = 1.4f, SpotUp = 0.8f, ScanUp = 0.7f, RevertUp = 1.3f, ScanLift = 0.12f;

        // the scan mark hangs just over what the object DRAWS, not over its pivot
        Transform _topOf;
        readonly List<Renderer> _topRends = new List<Renderer>();

        Vector3 OverTop(Transform t)
        {
            if (t != _topOf) { _topOf = t; t.GetComponentsInChildren(true, _topRends); }
            bool any = false;
            var b = new Bounds();
            for (int pass = 0; pass < 2 && !any; pass++)
                for (int i = 0; i < _topRends.Count; i++)
                {
                    var r = _topRends[i];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (pass == 0 && r is ParticleSystemRenderer) continue; // flames flicker: the solid parts first
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }
            if (!any) return t.position + Vector3.up * ScanUp;
            return new Vector3(b.center.x, b.max.y + ScanLift, b.center.z);
        }

        SimpleFPSController _pilot;

        // a wizard lingering near the disguise: the bar fills, the outcome decides
        class Near { public int Owner; public Transform T; public float Dwell; public bool Full; public int Frame; public int Key; }
        readonly List<Near> _near = new List<Near>();
        readonly HashSet<int> _motesSeen = new HashSet<int>();
        readonly List<int> _casters = new List<int>(); // wizards whose spell appeared close this frame
        int _nearSlot;

        // a wizard who went down: whose kill, and does the page wait for the revive
        class Kill { public int Owner; public int NetId; public Transform T; public Vector3 At; public float Since; public bool Mine; public int Key; }
        readonly List<Kill> _kills = new List<Kill>();
        int _killSlot;

        bool _myDeathPending;

        float _potTouch, _potLastTouch = -99f;
        bool _potWasCorrupt;

        int _zombieSlot, _baseSlot;
        RuneType _grantRune; int _grantFrame = -1; // a grant with a place beats the bare unlock

        bool Local => _pilot != null && _pilot.IsLocalViewer && Sides.LocalIsAcolyte;
        int Me => Grimoire.LocalPlayerId;
        bool Want(AcolyteDeeds.Deed d) => !Grimoire.HasRune(Me, AcolyteDeeds.RuneFor(d));
        static bool KnownWizard(int owner) => owner >= 0 && Sides.Known(owner) && Sides.Of(owner) == Side.Wizard;

        void Awake() => _pilot = GetComponent<SimpleFPSController>();

        void OnEnable()
        {
            NetAvatar.DownedChanged += OnAvatarDowned;
            NetSync.AvatarGone += OnAvatarGone;
            NetSync.ZombieKilled += OnZombieKilled;
            SimpleFPSController.Downed += OnDowned;
            SimpleFPSController.Revived += OnRevived;
            AcolyteDeeds.Granted += OnGranted;
            Grimoire.Unlocked += OnUnlocked;
        }

        void OnDisable()
        {
            NetAvatar.DownedChanged -= OnAvatarDowned;
            NetSync.AvatarGone -= OnAvatarGone;
            NetSync.ZombieKilled -= OnZombieKilled;
            SimpleFPSController.Downed -= OnDowned;
            SimpleFPSController.Revived -= OnRevived;
            AcolyteDeeds.Granted -= OnGranted;
            Grimoire.Unlocked -= OnUnlocked;
            foreach (var n in _near) UnlockMark.Hide(n.Key);
            _near.Clear();
            foreach (var k in _kills) UnlockMark.Hide(k.Key);
            _kills.Clear();
        }

        void Update()
        {
            if (!Local) return;
            float dt = Time.deltaTime;
            TickNear(dt);
            TickKills();
            TickPot(dt);
            TickBase();
            if (_myDeathPending) UnlockMark.Show(KeyMyDeath, transform, HeadUp, -1f);
        }

        // ------------------------------------------------ 1 and 2: a wizard near --
        void TickNear(float dt)
        {
            bool wantDecoy = Want(AcolyteDeeds.Deed.Decoy);
            bool wantReveal = Want(AcolyteDeeds.Deed.Reveal);
            if (!ShapeShift.LocalIsShaped || (!wantDecoy && !wantReveal))
            {
                for (int i = 0; i < _near.Count; i++) UnlockMark.Hide(_near[i].Key);
                _near.Clear();
                return;
            }

            float range = DrawingConfig.UnlockNearRange;
            Vector3 me = transform.position;
            int frame = Time.frameCount;
            foreach (var av in NetAvatar.All)
            {
                if (av == null) continue;
                int owner = NetSync.OwnerIdOf(av.Id);
                if (Sides.Of(owner) != Side.Wizard) continue;
                // a wizard down on the spot has not left; his bar just waits
                NoteWizard(owner, av.transform, me, range, frame, av.Downed ? 0f : dt);
            }
            // a spell of a nearby wizard appearing close while his bar is full
            // is the attack outcome
            CollectCasters(me, DrawingConfig.UnlockCastRange);

            for (int i = _near.Count - 1; i >= 0; i--)
            {
                var n = _near[i];
                bool here = n.Frame == frame;
                if (n.Full)
                {
                    if (here && _casters.Contains(n.Owner)) { Judge(n, AcolyteDeeds.Deed.Reveal); _near.RemoveAt(i); continue; }
                    if (!here) { Judge(n, AcolyteDeeds.Deed.Decoy); _near.RemoveAt(i); continue; }
                    UnlockMark.Show(n.Key, n.T, HeadUp, 1f);
                    continue;
                }
                if (!here || n.T == null) { UnlockMark.Hide(n.Key); _near.RemoveAt(i); continue; }
                UnlockMark.Show(n.Key, n.T, HeadUp, n.Dwell / DrawingConfig.UnlockNearSeconds);
            }
        }

        void NoteWizard(int owner, Transform t, Vector3 me, float range, int frame, float dt)
        {
            if (t == null || owner == Me) return;
            float d2 = (t.position - me).sqrMagnitude;
            Near n = null;
            for (int i = 0; i < _near.Count; i++)
                if (_near[i].Owner == owner) { n = _near[i]; break; }
            // a full bar keeps its wizard a little farther out, so the walk
            // away is a real leave and not a step
            float r = n != null && n.Full ? range + DrawingConfig.UnlockNearLeaveSlack : range;
            if (d2 > r * r) return;
            if (n == null)
            {
                n = new Near { Owner = owner, T = t, Key = KeyNear + (_nearSlot++ & 63) };
                _near.Add(n);
            }
            n.T = t;
            n.Frame = frame;
            if (!n.Full)
            {
                n.Dwell += dt;
                if (n.Dwell >= DrawingConfig.UnlockNearSeconds) n.Full = true;
            }
        }

        void Judge(Near n, AcolyteDeeds.Deed deed)
        {
            Vector3 at = n.T != null ? n.T.position + Vector3.up * HeadUp : transform.position + Vector3.up * HeadUp;
            UnlockMark.Flip(n.Key, AcolyteDeeds.Grant(Me, deed, at));
        }

        /// The wizards whose spells appeared within range this frame. Host
        /// motes are pooled, so a life id tells a new one; a client sees
        /// proxies, rebuilt per life.
        void CollectCasters(Vector3 me, float range)
        {
            _casters.Clear();
            if (_motesSeen.Count > 2048) _motesSeen.Clear();
            float r2 = range * range;
            var living = SpellParticle.Living;
            for (int i = 0; i < living.Count; i++)
            {
                var p = living[i];
                if (p == null) continue;
                if (!_motesSeen.Add(p.LifeId)) continue;
                if (p.Dead || p.FromMinion || !KnownWizard(p.OwnerId)) continue;
                if ((p.transform.position - me).sqrMagnitude <= r2 && !_casters.Contains(p.OwnerId))
                    _casters.Add(p.OwnerId);
            }
            var proxies = NetMoteProxy.Living;
            for (int i = 0; i < proxies.Count; i++)
            {
                var p = proxies[i];
                if (p == null) continue;
                if (!_motesSeen.Add(p.GetInstanceID())) continue;
                if (!KnownWizard(p.OwnerId)) continue;
                if ((p.transform.position - me).sqrMagnitude <= r2 && !_casters.Contains(p.OwnerId))
                    _casters.Add(p.OwnerId);
            }
        }

        // ------------------------------------------- 3 and 7: a wizard went down --
        void OnAvatarDowned(int owner, bool downed)
        {
            if (!Local || !KnownWizard(owner)) return;
            if (downed)
            {
                if (!Want(AcolyteDeeds.Deed.DeathNeedle) && !Want(AcolyteDeeds.Deed.Aggressive)) return;
                var t = NetSync.AvatarTransformOf(owner);
                _kills.Add(new Kill
                {
                    Owner = owner,
                    NetId = Element.IdFor("player:" + owner),
                    T = t,
                    At = t != null ? t.position : transform.position,
                    Since = Time.time,
                    Key = KeyKill + (_killSlot++ & 63),
                });
                return;
            }
            for (int i = _kills.Count - 1; i >= 0; i--)
            {
                var k = _kills[i];
                if (k.Owner != owner) continue;
                if (k.Mine)
                    UnlockMark.Flip(k.Key, AcolyteDeeds.Grant(Me, AcolyteDeeds.Deed.DeathNeedle, Spot(k)));
                _kills.RemoveAt(i);
            }
        }

        void OnAvatarGone(int owner)
        {
            for (int i = _kills.Count - 1; i >= 0; i--)
                if (_kills[i].Owner == owner) { UnlockMark.Hide(_kills[i].Key); _kills.RemoveAt(i); }
            for (int i = _near.Count - 1; i >= 0; i--)
                if (_near[i].Owner == owner) { UnlockMark.Hide(_near[i].Key); _near.RemoveAt(i); }
        }

        Vector3 Spot(Kill k) => (k.T != null ? k.T.position : k.At) + Vector3.up * HeadUp;

        void TickKills()
        {
            for (int i = _kills.Count - 1; i >= 0; i--)
            {
                var k = _kills[i];
                if (k.Mine) { UnlockMark.Show(k.Key, k.T, HeadUp, -1f); continue; }
                // the blame arrives on the host's word, a moment after the fall
                int by = Marks.Get(k.NetId, Mark.KilledBy);
                if (by == Me)
                {
                    bool viaZombie = Marks.Get(k.NetId, Mark.KilledVia) == 1;
                    if (viaZombie)
                    {
                        if (Want(AcolyteDeeds.Deed.Aggressive))
                            UnlockMark.FlipAt(k.Key, Spot(k), AcolyteDeeds.Grant(Me, AcolyteDeeds.Deed.Aggressive, Spot(k)));
                        _kills.RemoveAt(i);
                    }
                    else if (Want(AcolyteDeeds.Deed.DeathNeedle)) k.Mine = true;
                    else _kills.RemoveAt(i);
                    continue;
                }
                if (by >= 0 || Time.time - k.Since > DrawingConfig.UnlockBlameWaitSeconds) _kills.RemoveAt(i);
            }
        }

        // --------------------------------------------- 4: a wizard finished me --
        void OnDowned(SimpleFPSController p)
        {
            if (p != _pilot || !Local) return;
            int by = p.DownedBy;
            _myDeathPending = by != Me && KnownWizard(by) && Want(AcolyteDeeds.Deed.LifeNeedle);
        }

        void OnRevived(SimpleFPSController p)
        {
            if (p != _pilot) return;
            if (_myDeathPending && Local)
            {
                // the camera is back in the head: the page lands in front of it
                var eye = _pilot.CameraPivot;
                Vector3 at = eye != null ? eye.position + eye.forward * 1.8f + Vector3.up * 0.2f
                    : transform.position + Vector3.up * HeadUp;
                UnlockMark.FlipAt(KeyMyDeath, at, AcolyteDeeds.Grant(Me, AcolyteDeeds.Deed.LifeNeedle, at));
            }
            _myDeathPending = false;
        }

        // ---------------------------------------------------- 5 and 6: the pot --
        void TickPot(float dt)
        {
            var pot = CauldronEconomy.Active;
            bool corrupt = CauldronEconomy.IsCorrupt;
            bool wantEvap = Want(AcolyteDeeds.Deed.Evaporation);
            bool wantTransform = Want(AcolyteDeeds.Deed.Transformation);
            // a closed pot cannot be turned, so standing at it tries nothing
            if (pot == null || CauldronEconomy.PrepRemaining > 0f || (!wantEvap && !wantTransform))
            {
                _potTouch = 0f;
                _potWasCorrupt = corrupt;
                return;
            }
            Vector3 at = pot.transform.position + Vector3.up * PotUp;
            float close = DrawingConfig.PotCloseRadius;
            bool touching = !corrupt
                && (pot.transform.position - transform.position).sqrMagnitude <= close * close;
            float now = Time.time;

            if (corrupt && !_potWasCorrupt && (touching || now - _potLastTouch < DrawingConfig.UnlockPotLeaveGraceSeconds))
            {
                UnlockMark.FlipAt(KeyPot, at, AcolyteDeeds.Grant(Me, AcolyteDeeds.Deed.Evaporation, at));
                _potTouch = 0f;
            }
            else if (touching)
            {
                _potTouch += dt;
                _potLastTouch = now;
                UnlockMark.Show(KeyPot, pot.transform, PotUp, pot.Greenness); // synced by PotMsg on clients
            }
            else if (_potTouch > 0f && now - _potLastTouch > DrawingConfig.UnlockPotLeaveDebounceSeconds)
            {
                // stepped off before it turned: a real try that failed
                if (_potTouch >= DrawingConfig.UnlockCorruptAttemptSeconds && !corrupt)
                    UnlockMark.FlipAt(KeyPot, at, AcolyteDeeds.Grant(Me, AcolyteDeeds.Deed.Transformation, at));
                else UnlockMark.Hide(KeyPot);
                _potTouch = 0f;
            }
            _potWasCorrupt = corrupt;
        }

        // ------------------------------------------ 8: a wizard killed my zombie --
        void OnZombieKilled(Vector3 pos, int owner, int killedBy, bool viaMinion)
        {
            if (!Local || owner != Me || !KnownWizard(killedBy) || !Want(AcolyteDeeds.Deed.Spreading)) return;
            Vector3 at = pos + Vector3.up * SpotUp;
            UnlockMark.FlipAt(KeyZombie + (_zombieSlot++ & 7), at,
                AcolyteDeeds.Grant(Me, AcolyteDeeds.Deed.Spreading, at));
        }

        // -------------------------------- the four base deeds show where they happened --
        // the first two have a thing to hang the page on before the deed: the
        // object under the crosshair before the scan, your own disguise before TAB
        void TickBase()
        {
            if (AimBadge.ScanTarget != null && !Grimoire.HasRune(Me, RuneType.StateSolid))
                UnlockMark.ShowAt(KeyScanAim, OverTop(AimBadge.ScanTarget), -1f);
            if (ShapeShift.LocalIsShaped && !Grimoire.HasRune(Me, RuneType.StateLiquid))
                UnlockMark.Show(KeyRevert, transform, RevertUp, -1f);
        }

        static bool IsKit(RuneType rune)
        {
            var kit = RuneLibrary.AcolyteKit;
            for (int i = 0; i < kit.Length; i++) if (kit[i] == rune) return true;
            return false;
        }

        void OnGranted(int owner, RuneType rune, Vector3 at)
        {
            if (owner != Me || !Local || !IsKit(rune)) return;
            _grantRune = rune;
            _grantFrame = Time.frameCount;
            if (rune == RuneType.StateSolid)
            {
                // the page turns where the mark was hanging
                if (!UnlockMark.Flip(KeyScanAim, rune)) UnlockMark.FlipAt(KeyScanAim, at + Vector3.up * ScanUp, rune);
                return;
            }
            if (rune == RuneType.StateLiquid) { UnlockMark.FlipAt(KeyRevert, at + Vector3.up * RevertUp, rune); return; }
            UnlockMark.FlipAt(KeyBase + (_baseSlot++ & 7), at + Vector3.up * SpotUp, rune);
        }

        /// A kit rune that arrived without a place (the summon deeds resolve on
        /// the host for a client's seal): the page shows in front of you.
        void OnUnlocked(int owner, RuneType rune)
        {
            if (owner != Me || !Local || !IsKit(rune)) return;
            if (rune == _grantRune && _grantFrame == Time.frameCount) return;
            var eye = _pilot.CameraPivot;
            Vector3 at = eye != null ? eye.position + eye.forward * 1.5f + Vector3.up * 0.1f
                : transform.position + Vector3.up * HeadUp;
            UnlockMark.FlipAt(KeyBase + (_baseSlot++ & 7), at, rune);
        }
    }
}
