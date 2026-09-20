using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace SpellyZombie
{
    /// ★ WHAT STANDS IN A PHOTO: still copies built the way the game builds
    /// them (a character as a friend's copy is dressed, a creature as the
    /// creators show it), with every script taken off. Its effects and its
    /// moves are held at the moment its time slider says; a character is
    /// posed by its limbs, and a character or a zombie body wears the Creature
    /// Creator's head, arms, legs, height and width.
    public partial class PhotoBooth
    {
        /// One placed thing, standing in the scene.
        class Subject
        {
            public PhotoDef.Item Item;
            public GameObject Root;                 // where it stands, how it turns, its size
            public GameObject Body;                 // the still copy inside it
            public Vector3 BodyScale = Vector3.one; // the copy's own scale under the build sliders
            public EmoteRig Rig;
            public GooglyEyes Eyes;
            public Transform Wand;
            public readonly List<ParticleSystem> Systems = new List<ParticleSystem>();
            public readonly List<ParticleSystem> Tops = new List<ParticleSystem>(); // Simulate starts from these
            public Animator Animator;
            public AnimationClip[] Clips = new AnimationClip[0];
            public string[] ClipNames = new string[0]; // each clip's state name (AnimNames)
            public float Length;                    // how far the time slider goes; 0 = nothing to hold
            public BoxCollider Pick;
            public MaterialPropertyBlock Block;
            public readonly HashSet<Renderer> Painted = new HashSet<Renderer>();
        }

        readonly List<Subject> _subjects = new List<Subject>();
        readonly Dictionary<Transform, Subject> _byRoot = new Dictionary<Transform, Subject>();
        const float SizeMin = 0.05f, SizeMax = 40f;

        /// Everything placed stands again from the data.
        void StandAll()
        {
            foreach (var s in _subjects) if (s.Root != null) Destroy(s.Root);
            _subjects.Clear();
            _byRoot.Clear();
            _inks.Clear();
            _drawing = null;
            foreach (var item in Editing.Items) _subjects.Add(Stand(item));
            foreach (var ink in Editing.Inks) DrawInk(ink);
        }

        Subject Stand(PhotoDef.Item item)
        {
            var s = new Subject { Item = item };
            s.Root = new GameObject("~Photo " + item.Kind);
            _byRoot[s.Root.transform] = s;
            switch (item.Kind)
            {
                case PhotoKind.Character: BuildCharacter(s); break;
                case PhotoKind.Creature: BuildCreature(s); break;
                case PhotoKind.Spell: BuildSpell(s); break;
                case PhotoKind.Area: BuildArea(s); break;
                case PhotoKind.Effect: s.Body = Inert(Effect(item.What), s.Root.transform); break;
                default: s.Body = Inert(MapPalette.Find(item.What), s.Root.transform); break;
            }
            if (s.Body == null) Debug.LogWarning($"[SpellyZombie] Photo Booth: nothing to show for {item.Kind} '{item.What}'");
            foreach (var t in s.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            // what the build added (sockets, outfit, eyes, the pose joints) holds still too
            foreach (var mb in s.Root.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && !(mb is StateView)) mb.enabled = false;
            Gather(s);
            Place(s); // before its effects run: world-space particles start where it stands
            Hold(s);
            FitPick(s);
            SettleEyes(s);
            return s;
        }

        /// Built again from its data (a new side, outfit or body), its pose kept
        /// unless `keepPose` is false.
        Subject Restand(Subject s, bool keepPose = true)
        {
            int index = _subjects.IndexOf(s);
            if (index < 0) return s;
            if (keepPose) CapturePose(s);
            bool posing = _posing == s;
            if (posing) { _posing = null; _sculpt = null; }
            _byRoot.Remove(s.Root.transform);
            Destroy(s.Root);
            var fresh = Stand(s.Item);
            _subjects[index] = fresh;
            RedrawInk(index);
            if (_sel == s) _sel = fresh;
            if (posing) StartPosing(fresh);
            return fresh;
        }

        /// Its moves and effects at its time, then its pose, then its build.
        void Hold(Subject s)
        {
            Scrub(s);
            ApplyPose(s);
            Proportions(s);
        }

        /// The data catches up with what stands: where each one is and how it is posed.
        void CaptureAll()
        {
            foreach (var s in _subjects)
            {
                if (s.Root == null) continue;
                s.Item.Pos = s.Root.transform.position;
                CapturePose(s);
            }
        }

        // ------------------------------------------------------------ copies --
        /// A still copy: born under a sleeping parent, so nothing on it wakes
        /// before its scripts are gone. Its shapes stay (ink lands on them), its
        /// bodies and joints go, its animators and sounds rest.
        static GameObject Inert(GameObject prefab, Transform parent)
        {
            if (prefab == null) return null;
            var holder = new GameObject("Holder");
            holder.SetActive(false);
            holder.transform.SetParent(parent, false);
            var go = Instantiate(prefab, holder.transform);
            go.transform.localPosition = Vector3.zero; // its own turn and scale stay (tilted kit roots)
            PreviewPane.Strip(go, keepColliders: true);
            foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.playOnAwake = false;
            }
            go.transform.SetParent(parent, false);
            Destroy(holder);
            return go;
        }

        static GameObject Effect(string name)
        {
            var lib = FxLibrary.I;
            if (lib == null || string.IsNullOrEmpty(name)) return null;
            foreach (var p in lib.AllPrefabs())
                if (p != null && p.name == name) return p;
            return null;
        }

        /// The effect names the booth offers, once each.
        static List<string> EffectNames()
        {
            var names = new List<string>();
            var lib = FxLibrary.I;
            if (lib == null) return names;
            foreach (var p in lib.AllPrefabs())
                if (p != null && !names.Contains(p.name)) names.Add(p.name);
            names.Sort(System.StringComparer.OrdinalIgnoreCase);
            return names;
        }

        // --------------------------------------------------------- character --
        /// The body every player wears, dressed as a friend's copy is: arms
        /// down, the pose joints, facing forward, the outfit, the eyes, the side.
        void BuildCharacter(Subject s)
        {
            var prefab = CollectionManager.PlayerBody;
            if (prefab == null)
            {
                Debug.LogError("[SpellyZombie] Photo Booth: the Collection Manager's Player Body slot is empty");
                return;
            }
            var root = s.Root.transform;
            var body = Inert(prefab, root);
            body.name = "Body";
            s.Body = body;
            s.BodyScale = body.transform.localScale;

            Transform armL = null, armR = null, handL = null, handR = null, head = null, footL = null, toeL = null;
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
            }
            // the owner's order: arms down, the joints, then face forward
            NetAvatar.LowerArm(armL, handL);
            NetAvatar.LowerArm(armR, handR);
            s.Rig = s.Root.AddComponent<EmoteRig>();
            EmoteRig.Populate(s.Rig, allBones, root);
            CharacterRig.FaceForward(body.transform, footL, toeL, root.forward);

            var sockets = SocketSet.Build(body, root);
            var costume = Wardrobe.DressPlayer(sockets, new Color(0.35f, 0.55f, 0.9f), null, outfitCode: s.Item.Outfit ?? "");
            foreach (var piece in costume) if (piece != null) PreviewPane.Strip(piece, keepColliders: true); // scarves hold still
            var gripR = sockets.Get("HandR");
            s.Wand = gripR != null ? gripR.Find("Wand") : null;

            // an authored body brings its own eyes; otherwise a pair on the head, placed as the game places it
            var eyes = body.GetComponentInChildren<GooglyEyes>(true);
            if (eyes == null)
            {
                eyes = GooglyEyes.Attach(head != null ? head : root, head != null ? 0f : 0.6f, CharacterRig.EyeScale);
                if (head != null && eyes != null)
                {
                    eyes.transform.localPosition = CharacterRig.EyeLocalPos;
                    eyes.transform.localRotation = Quaternion.identity;
                    eyes.transform.localScale = Vector3.one * CharacterRig.EyeRigScale;
                }
            }
            if (eyes != null) { eyes.enabled = false; eyes.SetVisible(true); }
            s.Eyes = eyes;

            s.Animator = body.GetComponent<Animator>();
            var moves = CharacterLibrary.Anim;
            if (s.Animator != null && moves != null) s.Clips = AnimNames.Of(moves, out s.ClipNames);
            Paint(s);
        }

        /// The side on the wand and robe, the hat's colour: the paint a friend's copy wears.
        void Paint(Subject s)
        {
            if (s.Item.Kind != PhotoKind.Character) return;
            if (s.Block == null) s.Block = new MaterialPropertyBlock();
            SideLook.Paint(s.Root.transform, s.Wand, s.Item.Acolyte, s.Item.HatSet ? s.Item.Hat : (Color?)null,
                s.Block, s.Painted);
        }

        /// A new outfit code: one pick per slot of the players' wardrobe.
        static string ShuffledOutfit()
        {
            var c = SocketManager.Player;
            if (c == null) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < c.Slots.Count; i++)
            {
                if (i > 0) sb.Append(',');
                int n = c.Slots[i] != null && c.Slots[i].Options != null ? c.Slots[i].Options.Count : 1;
                sb.Append(Random.Range(0, Mathf.Max(1, n)));
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------- creature --
        /// A design from the book on its body, or a plain zombie or golem.
        void BuildCreature(Subject s)
        {
            var cr = string.IsNullOrEmpty(s.Item.What) ? null : SpellBook.Live.Creature(s.Item.What);
            var kind = cr != null ? cr.Body : s.Item.Body;
            var root = s.Root.transform;
            var body = Inert(SpellDef.BodyPrefab(kind), root);
            if (body == null) return;
            s.Body = body;
            if (kind == SpellBody.Zombie) body.transform.localScale *= DrawingConfig.ZombieBodyScale;
            s.BodyScale = body.transform.localScale;

            if (cr != null && kind == SpellBody.Golem) CreatureLook.Shape(body, cr); // its handles' pose; a zombie's build is the sliders
            Recolor(s);

            var eyes = body.GetComponentInChildren<GooglyEyes>(true);
            if (eyes == null && kind == SpellBody.Zombie)
            {
                Transform head = null;
                foreach (var t in body.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Head" || t.name.EndsWith(":Head")) { head = t; break; }
                eyes = GooglyEyes.Attach(head != null ? head : body.transform, 0f, DrawingConfig.ZombieEyeScale);
            }
            if (eyes != null) { eyes.enabled = false; eyes.SetVisible(true); }
            s.Eyes = eyes;

            // a plain zombie wears what the island's zombies wear, rolled from its seed
            if (cr == null && kind == SpellBody.Zombie)
            {
                Wardrobe.DressZombie(SocketSet.Build(body, root), 0.35f, s.Item.Seed);
            }

            s.Animator = body.GetComponentInChildren<Animator>(true);
            if (s.Animator != null && s.Animator.runtimeAnimatorController != null)
            {
                s.Clips = AnimNames.Of(s.Animator.runtimeAnimatorController, out s.ClipNames);
                s.Animator.runtimeAnimatorController = null; // the booth holds its moves, the controller never runs
            }
        }

        // ------------------------------------------------------ spell, area --
        void BuildSpell(Subject s)
        {
            var book = SpellBook.Live;
            var sp = book.Spell(s.Item.What);
            var body = Inert(SpellDef.BodyPrefab(SpellBody.Particle), s.Root.transform);
            s.Body = body;
            if (body == null || sp == null) return;
            var shape = book.Shape(string.IsNullOrEmpty(sp.Shape) ? sp.Name : sp.Shape);
            if (shape != null) SpellParticle.PoseNow(body.transform, shape);
            Tint(body, sp.PreviewTint(), SpellPayload.StateT01(sp.Payload.State), sp.Skin ?? shape?.Look);
        }

        void BuildArea(Subject s)
        {
            var book = SpellBook.Live;
            var aoe = book.Aoe(s.Item.What);
            var body = Inert(aoe != null ? aoe.Prefab : null, s.Root.transform);
            s.Body = body;
            if (body == null) return;
            var owner = book.Spell(aoe.Spell);
            if (owner != null)
                Tint(body, owner.Payload.Tint(), SpellPayload.StateT01(owner.Payload.State), owner.Skin);
        }

        /// A creature's colour: the one picked for the photo, else its design's; a plain body keeps its own.
        void Recolor(Subject s)
        {
            if (s.Item.Kind != PhotoKind.Creature || s.Body == null) return;
            var cr = string.IsNullOrEmpty(s.Item.What) ? null : SpellBook.Live.Creature(s.Item.What);
            float state = cr != null ? SpellPayload.StateT01(cr.Payload.State) : StateView.Solid;
            if (s.Item.TintSet) Tint(s.Body, s.Item.Tint, state, cr?.Skin);
            else if (cr != null) Tint(s.Body, cr.PreviewTint(), state, cr.Skin);
        }

        /// The colour the sliders start from.
        Color ColorOf(Subject s)
        {
            if (s.Item.TintSet) return s.Item.Tint;
            var cr = string.IsNullOrEmpty(s.Item.What) ? null : SpellBook.Live.Creature(s.Item.What);
            return cr != null ? cr.PreviewTint() : Color.white;
        }

        /// Colour, state and the material sliders through the body's one writer, as the creators' previews do.
        static void Tint(GameObject body, Color c, float state01, SpellTable.Look look)
        {
            var view = body.GetComponentInChildren<StateView>(true);
            if (view == null) view = body.AddComponent<StateView>();
            view.Tint = c;
            view.DriveTint = true;
            view.StateT = state01;
            view.Look = look ?? PreviewPane.Quiet;
            view.PushNow();
        }

        // -------------------------------------------------------------- time --
        /// The clip it holds, by its state's name.
        static AnimationClip ClipOf(Subject s)
        {
            if (string.IsNullOrEmpty(s.Item.Clip)) return null;
            for (int i = 0; i < s.Clips.Length && i < s.ClipNames.Length; i++)
                if (s.ClipNames[i] == s.Item.Clip) return s.Clips[i];
            return null;
        }

        /// Its particle systems and how long its time slider runs.
        void Gather(Subject s)
        {
            s.Systems.Clear();
            s.Tops.Clear();
            if (s.Body != null)
            {
                s.Body.GetComponentsInChildren(true, s.Systems);
                foreach (var ps in s.Systems)
                {
                    var up = ps.transform.parent != null ? ps.transform.parent.GetComponentInParent<ParticleSystem>(true) : null;
                    if (up == null || !s.Systems.Contains(up)) s.Tops.Add(ps);
                }
            }
            Measure(s);
        }

        void Measure(Subject s)
        {
            float len = 0f;
            foreach (var ps in s.Systems)
            {
                var m = ps.main;
                len = Mathf.Max(len, m.startDelay.constantMax + m.duration + m.startLifetime.constantMax);
            }
            var clip = ClipOf(s);
            if (clip != null) len = Mathf.Max(len, clip.length);
            s.Length = s.Systems.Count > 0 || clip != null ? Mathf.Clamp(len, 0.5f, 15f) : 0f;
        }

        /// ★ THE TIME SLIDER: every particle starts again from its own seed and
        /// runs to this moment, then holds; a held move is sampled at it.
        void Scrub(Subject s)
        {
            ScrubParticles(s);
            var clip = ClipOf(s);
            if (clip != null && s.Animator != null) Sample(s.Animator, clip, Mathf.Clamp(s.Item.Time, 0f, Mathf.Max(0f, s.Length)));
        }

        void ScrubParticles(Subject s)
        {
            if (s.Systems.Count == 0) return;
            float t = Mathf.Clamp(s.Item.Time, 0f, Mathf.Max(0f, s.Length));
            uint seed = (uint)Mathf.Max(1, s.Item.Seed);
            for (int i = 0; i < s.Systems.Count; i++)
            {
                var ps = s.Systems[i];
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.useAutoRandomSeed = false;
                ps.randomSeed = seed * 7919u + (uint)i;
            }
            foreach (var top in s.Tops) top.Simulate(t, true, true, true);
        }

        /// It moved, turned or grew: particles left in the world run again from where it is now.
        void Moved(Subject s)
        {
            foreach (var ps in s.Systems)
                if (ps.main.simulationSpace == ParticleSystemSimulationSpace.World) { ScrubParticles(s); FitPick(s); return; }
        }

        /// One frame of a clip on the bones, without the animator ever running.
        static void Sample(Animator anim, AnimationClip clip, float t)
        {
            var graph = PlayableGraph.Create("PhotoMove");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Move", anim);
                var play = AnimationClipPlayable.Create(graph, clip);
                play.SetApplyFootIK(false);
                play.SetTime(t);
                output.SetSourcePlayable(play);
                bool was = anim.enabled;
                anim.enabled = true;
                graph.Evaluate(0f);
                anim.enabled = was;
            }
            finally { graph.Destroy(); }
        }

        /// A new moment on the time slider: everything held there, the pose and build again on top.
        void Retime(Subject s)
        {
            Scrub(s);
            if (ClipOf(s) != null) CapturePose(s); // the move sets the joints: they are the pose now
            else ApplyPose(s);
            Proportions(s);
            FitPick(s);
            SettleEyes(s);
        }

        // -------------------------------------------------------------- pose --
        void ApplyPose(Subject s)
        {
            if (s.Rig == null || s.Item.Pose == null) return;
            foreach (var p in s.Item.Pose)
            {
                var j = s.Rig.Find(p.joint);
                if (j?.T == null) continue;
                j.T.localRotation = Quaternion.Euler(p.euler);
                EmoteRig.Constrain(j);
            }
        }

        void CapturePose(Subject s)
        {
            if (s.Rig == null) return;
            s.Item.Pose = s.Rig.CapturePose().poses;
        }

        /// A saved pose from the Pose Studio: its last frame, as a pose emote holds it.
        void WearPose(Subject s, EmoteDef def)
        {
            if (s.Rig == null || def == null || def.frames.Count == 0) return;
            s.Item.Pose = new List<JointPose>(def.frames[def.frames.Count - 1].poses);
            ApplyPose(s);
            CapturePose(s);
            Proportions(s);
            SettleEyes(s);
        }

        // -------------------------------------------------------------- play --
        Subject _playing; // the one whose animation runs

        /// Picked in the Animations window: it plays from its start.
        void PlayClip(Subject s, string name)
        {
            StopPlaying(false);
            s.Item.Clip = name;
            s.Item.Time = 0f;
            Measure(s);
            Retime(s);
            StartPlaying(s);
            BuildItemWindow();
        }

        void StartPlaying(Subject s)
        {
            if (s == null || ClipOf(s) == null) return;
            if (_posing == s) StopPosing();
            _playing = s;
            BuildAnimWindow();
        }

        /// It holds where it is; `hold` samples that moment exactly and makes it the pose.
        void StopPlaying(bool hold)
        {
            var s = _playing;
            _playing = null;
            if (hold && s != null && s.Root != null)
            {
                Retime(s);
                BuildAnimWindow();
            }
        }

        /// Every frame it plays: the clip runs on and loops, the slider follows.
        void TickPlay()
        {
            var s = _playing;
            if (s == null) return;
            var clip = ClipOf(s);
            if (s.Root == null || s != _sel || clip == null || s.Animator == null) { StopPlaying(false); return; }
            float len = Mathf.Max(0.01f, clip.length);
            s.Item.Time = Mathf.Repeat(s.Item.Time + Time.unscaledDeltaTime, len);
            Sample(s.Animator, clip, s.Item.Time);
            Proportions(s);
            SettleEyes(s);
            if (_animTime != null) _animTime.SetValueWithoutNotify(s.Item.Time);
            if (_animTimeLine != null) _animTimeLine.text = TimeSay(s.Item.Time);
        }

        /// Back to standing: no move, no pose.
        void Relax(Subject s)
        {
            s.Item.Clip = "";
            s.Item.Pose.Clear();
            Restand(s, keepPose: false);
        }

        // ------------------------------------------------------------- build --
        static bool HasBuild(Subject s) =>
            s.Item.Kind == PhotoKind.Character
            || (s.Item.Kind == PhotoKind.Creature && BodyOf(s.Item) == SpellBody.Zombie);

        static SpellBody BodyOf(PhotoDef.Item item)
        {
            var cr = string.IsNullOrEmpty(item.What) ? null : SpellBook.Live.Creature(item.What);
            return cr != null ? cr.Body : item.Body;
        }

        /// ★ The Creature Creator's build: height and width on the body, head,
        /// arms and legs on their bones (the hips rise so longer legs still stand).
        void Proportions(Subject s)
        {
            if (s.Body == null || !HasBuild(s)) return;
            var it = s.Item;
            s.Body.transform.localScale = Vector3.Scale(s.BodyScale, new Vector3(it.Width, it.Height, it.Width));
            CreatureBones.Wear(s.Body, it.Head, it.Arms, it.Legs);
        }

        // -------------------------------------------------------------- eyes --
        void SettleEyes(Subject s)
        {
            if (s.Eyes == null || s.Root == null) return;
            Vector3 at = s.Item.LookAtCamera && _cam != null
                ? _cam.transform.position
                : s.Eyes.transform.position + s.Root.transform.forward * 5f;
            s.Eyes.Settle((EyeMood)s.Item.Mood, at);
        }

        // ----------------------------------------------------- where it stands --
        void Place(Subject s)
        {
            var t = s.Root.transform;
            t.position = s.Item.Pos;
            t.rotation = Quaternion.Euler(s.Item.Tilt, s.Item.Yaw, s.Item.Roll);
            t.localScale = Vector3.one * Mathf.Clamp(s.Item.Size, SizeMin, SizeMax);
        }

        /// Turns it around its middle, never its feet.
        void Turn(Subject s, float yaw, float tilt, float roll)
        {
            var t = s.Root.transform;
            Vector3 c = Center(s);
            var to = Quaternion.Euler(tilt, yaw, roll);
            t.position = c + to * (Quaternion.Inverse(t.rotation) * (t.position - c));
            t.rotation = to;
            s.Item.Yaw = Mathf.Repeat(yaw, 360f);
            s.Item.Tilt = tilt;
            s.Item.Roll = roll;
            s.Item.Pos = t.position;
            Moved(s);
            SettleEyes(s);
        }

        /// Bigger or smaller from where it stands.
        void Resize(Subject s, float size)
        {
            size = Mathf.Clamp(size, SizeMin, SizeMax);
            s.Root.transform.localScale = Vector3.one * size;
            s.Item.Size = size;
            Moved(s);
            SettleEyes(s);
        }

        /// How high above the ground under it: 0 = standing on it.
        float LiftOf(Subject s) => s.Root.transform.position.y - GroundBelow(s);

        void Lift(Subject s, float lift)
        {
            var t = s.Root.transform;
            t.position = new Vector3(t.position.x, GroundBelow(s) + lift, t.position.z);
            s.Item.Pos = t.position;
            Moved(s);
            SettleEyes(s);
        }

        Vector3 Center(Subject s) => Bounds(s).center;

        /// What it covers now: meshes, posed skins and live particles.
        static Bounds Bounds(Subject s)
        {
            var root = s.Root.transform;
            var b = new Bounds(root.position, Vector3.zero);
            bool any = false;
            foreach (var r in s.Root.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r is TrailRenderer || r is LineRenderer) continue;
                if (r is ParticleSystemRenderer)
                {
                    var ps = r.GetComponent<ParticleSystem>();
                    if (ps == null || ps.particleCount == 0) continue;
                }
                Bounds rb = r is SkinnedMeshRenderer smr ? ShapeShift.SkinBounds(smr) : r.bounds;
                if (!any) { b = rb; any = true; }
                else b.Encapsulate(rb);
            }
            if (!any) b = new Bounds(root.position + Vector3.up * (0.5f * s.Item.Size), Vector3.one * s.Item.Size);
            return b;
        }

        /// A thing with no solid shape of its own (an effect, an area) is clicked by a box around it.
        void FitPick(Subject s)
        {
            if (s.Root == null) return;
            bool solid = false;
            foreach (var c in s.Root.GetComponentsInChildren<Collider>(true))
                if (c != null && c != s.Pick && !c.isTrigger) { solid = true; break; }
            if (solid) { if (s.Pick != null) s.Pick.enabled = false; return; }
            if (s.Pick == null)
            {
                var go = new GameObject("~Pick") { layer = PickLayer };
                go.transform.SetParent(s.Root.transform, false);
                s.Pick = go.AddComponent<BoxCollider>();
                s.Pick.isTrigger = true;
            }
            s.Pick.enabled = true;
            var root = s.Root.transform;
            var b = Bounds(s);
            float k = Mathf.Max(0.0001f, root.lossyScale.x);
            s.Pick.center = s.Pick.transform.InverseTransformPoint(b.center);
            s.Pick.size = Vector3.Max(b.size / k, Vector3.one * 0.3f);
        }

        // ---------------------------------------------------- adding, removing --
        PhotoDef.Item _pick; // what a click on the ground places

        void PlaceAt(Vector3 at)
        {
            if (_pick == null) return;
            var item = _pick.Clone();
            item.Pos = at;
            item.Yaw = Mathf.Repeat(_fly.Yaw + 180f, 360f); // it faces the camera
            item.Seed = Random.Range(1, 100000);
            Editing.Items.Add(item);
            var s = Stand(item);
            _subjects.Add(s);
            Select(s);
        }

        void Duplicate(Subject s)
        {
            int from = _subjects.IndexOf(s);
            if (from < 0) return;
            CapturePose(s);
            var item = s.Item.Clone();
            item.Pos += _cam.transform.right * Mathf.Max(1f, Bounds(s).size.x);
            Editing.Items.Add(item);
            int index = Editing.Items.Count - 1;
            var copy = Stand(item);
            _subjects.Add(copy);
            foreach (var ink in Editing.Inks.ToArray())
            {
                if (ink.Item != from) continue;
                var twin = JsonUtility.FromJson<PhotoDef.Ink>(JsonUtility.ToJson(ink));
                twin.Item = index;
                Editing.Inks.Add(twin);
                DrawInk(twin);
            }
            Select(copy);
        }

        void RemoveItem(int index)
        {
            var s = _subjects[index];
            if (_posing == s) { _posing = null; _sculpt = null; }
            _byRoot.Remove(s.Root.transform);
            Destroy(s.Root);
            _subjects.RemoveAt(index);
            Editing.Items.RemoveAt(index);
            Editing.Inks.RemoveAll(k => k.Item == index);
            _inks.RemoveAll(l => l.Data.Item == index);
            foreach (var k in Editing.Inks) if (k.Item > index) k.Item--;
        }

        /// A new placement of each kind, with what the book says about it.
        static PhotoDef.Item Character(bool acolyte)
        {
            var hat = HatColor.Saved();
            return new PhotoDef.Item
            {
                Kind = PhotoKind.Character, Acolyte = acolyte,
                Outfit = SocketManager.LocalOutfitCode(),
                HatSet = hat != null, Hat = hat ?? Color.white,
            };
        }

        static PhotoDef.Item Creature(CreatureDef cr) => new PhotoDef.Item
        {
            Kind = PhotoKind.Creature, What = cr.Name, Body = cr.Body, Size = cr.Size,
            Height = cr.Height, Width = cr.Width, Head = cr.Head, Arms = cr.Arms, Legs = cr.Legs,
        };

        static PhotoDef.Item Plain(SpellBody body) => new PhotoDef.Item { Kind = PhotoKind.Creature, Body = body };

        static PhotoDef.Item Thing(PhotoKind kind, string what, float time) =>
            new PhotoDef.Item { Kind = kind, What = what, Time = time };

        static string PickName(PhotoDef.Item item)
        {
            switch (item.Kind)
            {
                case PhotoKind.Character: return Loc.T(item.Acolyte ? "mc.side.acolyte" : "mc.side.wizard");
                case PhotoKind.Creature:
                    if (!string.IsNullOrEmpty(item.What)) return item.What;
                    return Loc.T(item.Body == SpellBody.Golem ? "mc.body.golem" : "mc.body.zombie");
                default: return item.What;
            }
        }
    }
}
