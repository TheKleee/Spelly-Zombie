using UnityEngine;

namespace SpellyZombie
{
    /// MARKO'S BOOK STAND (his Aug 8 spec): multiplayer is not controlled by a
    /// floating permanent panel — you WALK to the stand he placed near spawn
    /// and the menu pops up there, mouse and keyboard both. Create/join a
    /// lobby, optional password, the HOST picks the map (and, when more than
    /// one exists, the game mode); everyone else can leave a LIKE on the map
    /// they want.
    ///
    /// HE ADDS THIS COMPONENT TO HIS STAND — nothing is auto-found (the
    /// no-silent-autodetect law: his content, his slot). Several stands are
    /// fine; near any of them counts.
    ///
    /// NetGame owns the panel itself; this component only answers "is the
    /// local player at a stand?" and frees the cursor while the panel is up.
    /// The cursor is re-asserted every frame the panel stays open, mirroring
    /// how the easel modes hold it free against the controller's re-lock.
    public class LobbyStand : MonoBehaviour
    {
        [Tooltip("How close the player must stand for the menu to appear, meters.")]
        public float Range = 2.8f;

        static int _nearFrame = -1;

        /// True while the local player is within range of ANY stand.
        public static bool NearLocal => _nearFrame >= Time.frameCount - 1;

        /// True while the stand menu is actually showing (NetGame drives it).
        public static bool PanelOpen { get; private set; }

        /// Called by NetGame every frame the stand panel is visible — keeps
        /// the cursor free for real mouse use without touching the controller.
        public static void HoldPanel(bool open)
        {
            PanelOpen = open;
            if (!open) return; // closed: the controller's click-to-relock rule takes over
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Update()
        {
            var p = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (p == null) return;
            if ((p.transform.position - transform.position).sqrMagnitude <= Range * Range)
                _nearFrame = Time.frameCount;
        }
    }
}
