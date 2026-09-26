using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace SpellyZombie
{
    /// ★ WHAT STANDS IN A PHOTO: still copies built the way the game builds
    /// them (a character as a friend's copy is dressed, a creature as the
    /// creators show it), with every script taken off. Its effects and its
    /// moves are held at the moment its time slider says; a character or a
    /// zombie body is posed by its limbs and wears the Creature Creator's head,
    /// arms, legs, height and width.
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
            public readonly List<Ribbon> Ribbons = new List<Ribbon>(); // its trails, held still
            public readonly List<Transform> Parts = new List<Transform>(); // an effect's or an area's switchable parts
        }

        /// A trail as the booth holds it: a still line with the trail's look.
        class Ribbon
        {
            public LineRenderer Line;
            public float Time;  // how many seconds of path the trail keeps
            public float Width; // the trail's own width
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
            if (HasParts(s))
            {
                Ribbons(s);
                GatherParts(s);
            }
            foreach (var t in s.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            // what the build added (sockets, outfit, eyes, the pose joints) holds still too
            foreach (var mb in s.Root.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && !(mb is StateView)) mb.enabled = false;
            Gather(s);
            Place(s); // before its effects run: world-space particles start where it stands
            Hold(s);
            ApplyParts(s);
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

        /// The effect names the booth offers, once each, in the order their shown names read.
        static List<string> EffectNames()
        {
            var names = EffectFiles();
            names.Sort((a, b) => string.Compare(EffectName(a), EffectName(b), System.StringComparison.OrdinalIgnoreCase));
            return names;
        }

        /// The effects' own file names: what a setup saves and finds them by.
        static List<string> EffectFiles()
        {
            var names = new List<string>();
            var lib = FxLibrary.I;
            if (lib == null) return names;
            foreach (var p in lib.AllPrefabs())
                if (p != null && !names.Contains(p.name)) names.Add(p.name);
            names.Sort(System.StringComparer.OrdinalIgnoreCase);
            return names;
        }

        static Dictionary<string, string> _effectNames;
        static readonly HashSet<string> PackWords = new HashSet<string> { "HDR", "3D", "Solo", "PLAIN", "Alt", "WW", "Misc" };

        /// ★ An effect as the booth names it (his go): the pack's file name without its codes, so
        /// "CFXR2 WW Enemy Explosion" reads "Enemy Explosion". Never translated: placing one shows it.
        static string EffectName(string file)
        {
            if (string.IsNullOrEmpty(file)) return file;
            if (_effectNames == null)
            {
                // two files that clean to the same words read with a number
                _effectNames = new Dictionary<string, string>();
                var taken = new Dictionary<string, int>();
                foreach (var n in EffectFiles())
                {
                    string clean = CleanEffectName(n);
                    taken.TryGetValue(clean, out int k);
                    taken[clean] = ++k;
                    _effectNames[n] = k > 1 ? clean + " " + k : clean;
                }
            }
            return _effectNames.TryGetValue(file, out var shown) ? shown : CleanEffectName(file);
        }

        static string CleanEffectName(string file)
        {
            string s = Regex.Replace(file, @"^CFXR\d*\s+", "");
            s = Regex.Replace(s, @"\((HDR|Loop|Air|Lit|Smaller|Random Color)\)", " ");
            s = Regex.Replace(s.Replace('_', ' ').Replace('-', ' '), "(?<=[a-z])(?=[A-Z])", " "); // RevealTrail reads Reveal Trail
            var words = new List<string>();
            foreach (var w in s.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (w.Length == 1 && char.IsUpper(w[0])) continue; // a variant letter
                if (int.TryParse(w, out _)) continue;             // a variant number
                if (!PackWords.Contains(w)) words.Add(w);
            }
            return words.Count > 0 ? string.Join(" ", words) : file;
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
            if (Editing.Shaded) Shade(body);
            Paint(s);
        }

        /// ★ SHADED CHARACTERS (his go, the booth only; in the game the flat look stays): a player's
        /// materials are Unlit, so no light or shadow ever reached them and a hand held in front of
        /// the chest vanished into it. Here each draws with a lit, matte copy of itself (the same
        /// colour and picture), shaded by the sun, edged by the back light, shadowed. Eyes stay as they are.
        static void Shade(GameObject body)
        {
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (r.GetComponentInParent<GooglyEyes>(true) != null) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var lit = LitTwin(mats[i]);
                    if (lit != mats[i]) { mats[i] = lit; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        static readonly Dictionary<Material, Material> _litTwins = new Dictionary<Material, Material>();
        static Shader _litShader;

        static Material LitTwin(Material m)
        {
            if (m == null || m.shader == null || m.shader.name != "Universal Render Pipeline/Unlit") return m;
            if (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f) return m; // a see-through one stays as it is
            if (_litTwins.TryGetValue(m, out var twin) && twin != null) return twin;
            if (_litShader == null) _litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (_litShader == null) return m;
            twin = new Material(_litShader) { name = m.name + " (booth, lit)" };
            if (m.HasProperty("_BaseColor")) twin.SetColor("_BaseColor", m.GetColor("_BaseColor"));
            if (m.HasProperty("_BaseMap"))
            {
                twin.SetTexture("_BaseMap", m.GetTexture("_BaseMap"));
                twin.SetTextureScale("_BaseMap", m.GetTextureScale("_BaseMap"));
                twin.SetTextureOffset("_BaseMap", m.GetTextureOffset("_BaseMap"));
            }
            if (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f)
            {
                twin.SetFloat("_AlphaClip", 1f);
                twin.SetFloat("_Cutoff", m.GetFloat("_Cutoff"));
                twin.EnableKeyword("_ALPHATEST_ON");
            }
            twin.SetFloat("_Smoothness", 0.1f); // matte, like the flat look it stands in for
            twin.SetFloat("_Metallic", 0f);
            _litTwins[m] = twin;
            return twin;
        }

        /// The booth's lit copies go with the stage.
        static void DropLitTwins()
        {
            foreach (var t in _litTwins.Values) if (t != null) Destroy(t);
            _litTwins.Clear();
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

            // a zombie has the players' skeleton: its limbs pose the same way
            if (kind == SpellBody.Zombie)
            {
                s.Rig = s.Root.AddComponent<EmoteRig>();
                EmoteRig.Populate(s.Rig, body.GetComponentsInChildren<Transform>(true), root);
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
            foreach (var rb in s.Ribbons) len = Mathf.Max(len, rb.Time);
            var clip = ClipOf(s);
            if (clip != null) len = Mathf.Max(len, clip.length);
            s.Length = s.Systems.Count > 0 || clip != null || s.Ribbons.Count > 0 ? Mathf.Clamp(len, 0.5f, 15f) : 0f;
        }

        /// ★ THE TIME SLIDER: every particle starts again from its own seed and
        /// runs to this moment, then holds; a held move is sampled at it.
        void Scrub(Subject s)
        {
            ScrubParticles(s);
            ShapeRibbons(s);
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

        /// It moved, turned or grew: particles left in the world run again from where it is now,
        /// and a trail's ribbon keeps its width to its size.
        void Moved(Subject s)
        {
            if (s.Ribbons.Count > 0) { ShapeRibbons(s); FitPick(s); }
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
                Bounds rb = r is SkinnedMeshRenderer smr ? ShapeShift.SkinBounds(smr, measured: true) : r.bounds;
                if (!any) { b = rb; any = true; }
                else b.Encapsulate(rb);
            }
            // a trail's ribbon from its own points (lines are left out above: ink rides on them too)
            foreach (var ribbon in s.Ribbons)
            {
                var line = ribbon.Line;
                if (line == null || !line.enabled) continue;
                var pad = Vector3.one * line.widthMultiplier;
                for (int i = 0; i < line.positionCount; i++)
                {
                    var p = new Bounds(line.transform.TransformPoint(line.GetPosition(i)), pad);
                    if (!any) { b = p; any = true; }
                    else b.Encapsulate(p);
                }
            }
            if (!any) b = new Bounds(root.position + Vector3.up * (0.5f * s.Item.Size), Vector3.one * s.Item.Size);
            return b;
        }

        // ------------------------------------------------------------ trails --
        /// ★ A TRAIL HELD STILL (his go): a trail draws only while its thing moves and fades behind
        /// it, so a photo never caught it. In the booth each trail becomes a still ribbon with its
        /// look, swept back from where it stands; the time slider says how much of it shows.
        const float TrailSpeed = 4f;   // metres a second the held trail was drawn at: a running body
        const float RibbonStep = 0.25f; // metres between its points
        const float RibbonBend = 0.15f; // how far it curves aside, of its length

        void Ribbons(Subject s)
        {
            s.Ribbons.Clear();
            if (s.Body == null) return;
            foreach (var trail in s.Body.GetComponentsInChildren<TrailRenderer>(true))
            {
                // one renderer to a thing: the line goes on a child of the trail's own
                var go = new GameObject("~Ribbon") { layer = Layer };
                go.transform.SetParent(trail.transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = false; // it moves, turns and grows with the thing
                line.sharedMaterials = trail.sharedMaterials;
                line.widthCurve = trail.widthCurve;
                line.colorGradient = trail.colorGradient;
                line.numCornerVertices = trail.numCornerVertices;
                line.numCapVertices = trail.numCapVertices;
                line.alignment = trail.alignment;
                line.textureMode = trail.textureMode;
                line.shadowCastingMode = trail.shadowCastingMode;
                line.receiveShadows = trail.receiveShadows;
                line.sortingLayerID = trail.sortingLayerID;
                line.sortingOrder = trail.sortingOrder;
                s.Ribbons.Add(new Ribbon { Line = line, Time = trail.time, Width = trail.widthMultiplier });
                trail.emitting = false;
                trail.Clear();
                trail.enabled = false; // the ribbon is its picture now
            }
        }

        /// Each ribbon as long as its time holds, head at the thing and the tail behind it
        /// (the trail's own width and colour run the same way), as wide as the thing is big.
        void ShapeRibbons(Subject s)
        {
            if (s.Ribbons.Count == 0 || s.Root == null) return;
            float size = Mathf.Abs(s.Root.transform.lossyScale.x);
            foreach (var ribbon in s.Ribbons)
            {
                var line = ribbon.Line;
                if (line == null) continue;
                float len = TrailSpeed * Mathf.Clamp(s.Item.Time, 0f, ribbon.Time);
                int n = Mathf.Clamp(Mathf.CeilToInt(len / RibbonStep) + 1, 2, 64);
                line.positionCount = n;
                for (int i = 0; i < n; i++)
                {
                    float u = i / (float)(n - 1);
                    line.SetPosition(i, new Vector3(Mathf.Sin(u * Mathf.PI) * len * RibbonBend, 0f, -u * len));
                }
                line.widthMultiplier = ribbon.Width * size;
                line.enabled = len > 0.01f && PartShown(s, line.transform);
            }
        }

        // ------------------------------------------------------------- parts --
        /// ★ AN EFFECT'S PARTS (his go): whatever in an effect or an area draws or lights (a glow,
        /// sparks, a light) switches off for the photo on its own, named as the effect names it
        /// and kept by its path inside it.
        static bool HasParts(Subject s) => s.Item.Kind == PhotoKind.Effect || s.Item.Kind == PhotoKind.Area;

        void GatherParts(Subject s)
        {
            s.Parts.Clear();
            if (s.Body == null) return;
            foreach (var t in s.Body.GetComponentsInChildren<Transform>())
            {
                bool part = false;
                foreach (var r in t.GetComponents<Renderer>())
                    if (r.enabled && !(r is TrailRenderer)) { part = true; break; } // a trail's ribbon is the part
                if (!part)
                    foreach (var l in t.GetComponents<Light>())
                        if (l.enabled) { part = true; break; }
                if (part) s.Parts.Add(t);
            }
        }

        bool PartShown(Subject s, Transform t) =>
            s.Body == null || !s.Item.Hidden.Contains(PathOf(s.Body.transform, t));

        void ApplyParts(Subject s)
        {
            bool ribbons = false;
            foreach (var t in s.Parts)
            {
                if (t == null) continue;
                bool shown = PartShown(s, t);
                foreach (var r in t.GetComponents<Renderer>())
                {
                    if (r is TrailRenderer) continue;
                    if (r is LineRenderer) { ribbons = true; continue; } // a ribbon shows by its length too
                    r.enabled = shown;
                }
                foreach (var l in t.GetComponents<Light>()) l.enabled = shown;
            }
            if (ribbons) ShapeRibbons(s);
        }

        /// A part as its effect names it; a ribbon by the trail it holds.
        static string PartName(Transform t)
        {
            string n = t.name == "~Ribbon" && t.parent != null ? t.parent.name : t.name;
            return CleanEffectName(n.Replace("(Clone)", "").Trim());
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
                case PhotoKind.Effect: return EffectName(item.What);
                default: return item.What;
            }
        }
    }
}
