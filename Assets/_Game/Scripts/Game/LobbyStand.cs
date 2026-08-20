using UnityEngine;

namespace SpellyZombie
{
    /// The book stand: walk up and the multiplayer menu pops there. Add this
    /// component to the stand - nothing is auto-found; several stands are fine.
    /// NetGame owns the panel; this answers proximity and frees the cursor while it is up.
    public class LobbyStand : MonoBehaviour
    {
        [Tooltip("How close the player must stand for the menu to appear, meters.")]
        public float Range = 0.5f;

        [Tooltip("The stand shrugs off casual spell splash — its Damageable is raised to at least this.")]
        public float Toughness = 600f;

        [Tooltip("Your grimoire's Animator on the stand (same controller as the worn book). Its 'Open' bool follows the host: open while the host is in the stand menu, closed otherwise, visible to everyone.")]
        public Animator Book;

        /// Mirrored from the host on clients; local truth on the host.
        public static bool HostMenuOpen;

        static int _nearFrame = -1;
        static bool _sentOpen;

        void Start()
        {
            // raise durability so casual splash can't kill the menu; LobbyRespawn still covers a real kill
            var dmg = GetComponentInParent<Damageable>();
            if (dmg != null && dmg.Health < Toughness) dmg.Health = Toughness;
            BuildOpenBook();
        }

        /// Builds a simple open book on top of the stand. Any child named
        /// "Book"/"Grimoire" suppresses the builder.
        void BuildOpenBook()
        {
            if (Book != null) return; // the animated grimoire drives instead
            foreach (Transform t in transform)
                if (t.name.Contains("Book") || t.name.Contains("Grimoire")) return;

            // sit the book on the stand's real top, wherever the model puts it
            float topY = 1.05f;
            var rends = GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                topY = b.max.y - transform.position.y;
            }

            var root = new GameObject("Book (menu)");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, topY + 0.02f, 0f);
            root.transform.localRotation = Quaternion.Euler(12f, 0f, 0f); // tilted toward the reader

            void Slab(string name, Vector3 pos, Vector3 scale, float roll, Color c)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                Destroy(go.GetComponent<Collider>()); // decor, never a pen target
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = pos;
                go.transform.localScale = scale;
                go.transform.localRotation = Quaternion.Euler(0f, 0f, roll);
                go.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(c, MoteShade.Opaque);
                go.layer = 2; // invisible to the pen, like the worn grimoire
            }

            var leather = new Color(0.32f, 0.2f, 0.12f);
            var paper = new Color(0.92f, 0.88f, 0.78f);
            Slab("Cover.L", new Vector3(-0.16f, 0f, 0f), new Vector3(0.32f, 0.02f, 0.44f), 7f, leather);
            Slab("Cover.R", new Vector3(0.16f, 0f, 0f), new Vector3(0.32f, 0.02f, 0.44f), -7f, leather);
            Slab("Pages.L", new Vector3(-0.15f, 0.022f, 0f), new Vector3(0.28f, 0.018f, 0.4f), 6f, paper);
            Slab("Pages.R", new Vector3(0.15f, 0.022f, 0f), new Vector3(0.28f, 0.018f, 0.4f), -6f, paper);
        }

        /// True while the local player is within range of ANY stand.
        public static bool NearLocal => _nearFrame >= Time.frameCount - 1;

        /// True while the stand menu is actually showing (NetGame drives it).
        public static bool PanelOpen { get; private set; }

        /// Called by NetGame every frame the stand panel is visible - keeps
        /// the cursor free for real mouse use without touching the controller.
        public static void HoldPanel(bool open)
        {
            PanelOpen = open;

            // the book on the stand tells everyone whether the host is in the
            // menu: the host's own state replicates, clients just render it
            if (!NetGame.Connected || NetGame.IsHost)
            {
                HostMenuOpen = open;
                if (NetGame.IsHost && open != _sentOpen)
                {
                    _sentOpen = open;
                    NetSync.PushStandOpen(open);
                }
            }

            if (!open) return; // closed: the controller's click-to-relock rule takes over
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Update()
        {
            // near AND looking at it: running past the stand must not pop the
            // menu, walking up to it must
            var p = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (p != null
                && (p.transform.position - transform.position).sqrMagnitude <= Range * Range)
            {
                var cam = Camera.main;
                Vector3 to = transform.position - (cam != null ? cam.transform.position : p.transform.position);
                to.y = 0f;
                Vector3 fwd = cam != null ? cam.transform.forward : p.transform.forward;
                fwd.y = 0f;
                if (to.sqrMagnitude < 0.01f
                    || Vector3.Dot(fwd.normalized, to.normalized) > 0.45f)
                    _nearFrame = Time.frameCount;
            }

            // derived, so a destroyed and respawned stand shows the truth too
            if (Book != null && Book.runtimeAnimatorController != null
                && Book.GetBool("Open") != HostMenuOpen)
                Book.SetBool("Open", HostMenuOpen);
        }
    }
}
