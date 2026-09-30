using UnityEngine;

namespace Oheangbu.App.World
{
    // #302 앞잡 casting pose (presentation only, SPEC-PLAYER-FEEL-300 F): after the body's own animation, raise the right
    // arm so the brush points forward and up at the circles, left arm drawn back a little. Weight is driven by Riposte301.
    [DefaultExecutionOrder(950)]
    public sealed class Riposte301Pose : MonoBehaviour
    {
        public float Weight;
        Animator _animator;
        Transform _rUpper, _rLower, _rHand, _lUpper, _lLower, _spine;

        public void Bind(Animator animator)
        {
            _animator = animator;
            _rUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm); _rLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            _lUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm); _lLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _spine = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
        }

        static void Point(Transform from, Transform to, Vector3 direction, float weight)
        {
            if (from == null || to == null || weight <= 0f) return;
            Vector3 current = (to.position - from.position).normalized;
            from.rotation = Quaternion.FromToRotation(current, Vector3.Slerp(current, direction.normalized, weight)) * from.rotation;
        }

        void LateUpdate()
        {
            if (_animator == null || Weight <= 0f) return;
            var body = _animator.transform;
            Vector3 fwd = body.forward, up = body.up, right = body.right;
            if (_spine != null) _spine.rotation = Quaternion.AngleAxis(-8f * Weight, right) * _spine.rotation;    // lean back into the cast
            Point(_rUpper, _rLower, fwd * .75f + up * .55f + right * .15f, Weight);                                // brush arm up and forward
            Point(_rLower, _rHand, fwd * .7f + up * .75f, Weight * .9f);
            Point(_lUpper, _lLower, -fwd * .25f - up * .9f - right * .3f, Weight * .6f);                           // off hand drawn back
            Point(_lLower, _lUpper == null ? null : _animator.GetBoneTransform(HumanBodyBones.LeftHand), fwd * .4f - up * .8f, Weight * .5f);
        }
    }
}
