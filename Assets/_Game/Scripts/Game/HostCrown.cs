using UnityEngine;

namespace SpellyZombie
{
    /// The lobby's "who owns this room" marker: HIS crown prefab floating
    /// and spinning above the host's head, on every machine. It hovers well
    /// clear of the hat everyone already wears, so it reads as a marker and
    /// never as clothing. Lives in the Lobby scene - leaving takes it along.
    public class HostCrown : MonoBehaviour
    {
        [Tooltip("Your crown model. Floats over the lobby host's head.")]
        public GameObject CrownPrefab;

        const float Clearance = 0.4f;  // above the bounds top, hat included
        const float BobAmp = 0.09f;
        const float SpinDeg = 55f;

        GameObject _crown;
        Transform _target;
        float _headY, _checkIn;
        bool _warned;

        void LateUpdate()
        {
            if ((_checkIn -= Time.deltaTime) <= 0f)
            {
                _checkIn = 0.5f;
                Resolve();
            }
            if (_crown == null || _target == null || !_crown.activeSelf) return;

            Vector3 at = _target.position;
            at.y = _headY + Clearance + Mathf.Sin(Time.time * 2.2f) * BobAmp;
            _crown.transform.position = at;
            _crown.transform.Rotate(0f, SpinDeg * Time.deltaTime, 0f, Space.World);
        }

        /// Host = the machine running the server; on clients that is the
        /// avatar with CLIENT id 0 (the host connects to itself first).
        /// An undeclared solo lobby has no host and shows no crown.
        void Resolve()
        {
            Transform target = null;
            if (RoundDirector.InLobby && NetGame.Connected)
            {
                if (NetGame.IsHost)
                {
                    foreach (var p in SimpleFPSController.All)
                        if (p != null && p.IsLocalViewer) { target = p.transform; break; }
                }
                else
                {
                    foreach (var a in NetAvatar.All)
                        if (a != null && a.Id == 0) { target = a.transform; break; }
                }
            }

            if (target != _target)
            {
                _target = target;
                if (_crown != null) _crown.SetActive(_target != null);
            }
            if (_target == null) return;

            if (_crown == null)
            {
                if (CrownPrefab == null)
                {
                    if (!_warned)
                        Debug.LogError("[SpellyZombie] HostCrown: CrownPrefab is empty - assign your crown model.", this);
                    _warned = true;
                    return;
                }
                _crown = Instantiate(CrownPrefab, transform);
            }
            _headY = ShapeShift.FindObjectBounds(_target).max.y;
        }
    }
}
