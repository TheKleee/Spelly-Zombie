using UnityEngine;

namespace SpellyZombie
{
    /// ★ A CREATURE'S SHAPE ON ITS BODY (the Creature Creator): a golem wears
    /// the pose its handles were dragged to, a zombie body its height, width,
    /// head, arms and legs. The same on the host's body, a friend's stand-in
    /// and both creators' previews.
    public static class CreatureLook
    {
        /// Height and width on the root, once at birth; the collider grows with them.
        public static void Proportion(Transform root, CreatureDef c)
        {
            if (root == null || c == null || c.Body != SpellBody.Zombie) return;
            root.localScale = Vector3.Scale(root.localScale, new Vector3(c.Width, c.Height, c.Width));
        }

        /// The handles' pose on a golem, the head, arms and legs on a zombie body.
        public static void Shape(GameObject body, CreatureDef c)
        {
            if (body == null || c == null) return;
            if (c.Body == SpellBody.Golem)
            {
                if (c.Pose != null && c.Pose.Count > 0) SpellParticle.PoseNow(body.transform, c.Pose);
            }
            else CreatureBones.Wear(body, c.Head, c.Arms, c.Legs);
        }
    }

    /// ★ A ZOMBIE BODY'S PROPORTIONS: head, arms and legs scaled on their
    /// bones, the hips raised so longer legs still stand on the ground. Held
    /// after the animator every frame.
    public class CreatureBones : MonoBehaviour
    {
        Transform _headBone, _armL, _armR, _legL, _legR, _hips;
        float _head = 1f, _arms = 1f, _legs = 1f, _legLength;
        Vector3 _hipsBase, _hipsLifted;
        bool _found;

        /// Proportions on this body; a plain body gets no component at all.
        public static void Wear(GameObject body, float head, float arms, float legs)
        {
            if (body == null) return;
            var bones = body.GetComponent<CreatureBones>();
            if (bones == null)
            {
                if (Mathf.Approximately(head, 1f) && Mathf.Approximately(arms, 1f) && Mathf.Approximately(legs, 1f)) return;
                bones = body.AddComponent<CreatureBones>();
            }
            bones.Set(head, arms, legs);
        }

        public void Set(float head, float arms, float legs)
        {
            Find();
            _head = head;
            _arms = arms;
            _legs = legs;
            Hold();
        }

        void Find()
        {
            if (_found) return;
            _found = true;
            Transform legL = null, footL = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (_hips == null && n.EndsWith("Hips")) _hips = t;
                else if (_headBone == null && n.EndsWith("Head")) _headBone = t;
                else if (_armL == null && n.EndsWith("LeftArm")) _armL = t;
                else if (_armR == null && n.EndsWith("RightArm")) _armR = t;
                else if (_legL == null && n.EndsWith("LeftUpLeg")) _legL = t;
                else if (_legR == null && n.EndsWith("RightUpLeg")) _legR = t;
                else if (legL == null && n.EndsWith("LeftLeg")) legL = t;
                else if (footL == null && n.EndsWith("LeftFoot")) footL = t;
            }
            // the leg as built, hip joint to ankle, in the hips' parent's units
            if (_hips != null && _hips.parent != null && _legL != null && legL != null && footL != null)
            {
                var room = _hips.parent;
                _legLength = Vector3.Distance(room.InverseTransformPoint(_legL.position), room.InverseTransformPoint(legL.position))
                    + Vector3.Distance(room.InverseTransformPoint(legL.position), room.InverseTransformPoint(footL.position));
            }
        }

        void LateUpdate() => Hold();

        void Hold()
        {
            Scale(_headBone, _head);
            Scale(_armL, _arms);
            Scale(_armR, _arms);
            Scale(_legL, _legs);
            Scale(_legR, _legs);
            if (_hips == null) return;
            // whatever placed the hips since (the animator) is the new base; the lift goes on top once
            var at = _hips.localPosition;
            if (at != _hipsLifted) _hipsBase = at;
            _hipsLifted = _hipsBase + Vector3.up * ((_legs - 1f) * _legLength);
            _hips.localPosition = _hipsLifted;
        }

        static void Scale(Transform bone, float k)
        {
            if (bone != null) bone.localScale = Vector3.one * k;
        }
    }
}
