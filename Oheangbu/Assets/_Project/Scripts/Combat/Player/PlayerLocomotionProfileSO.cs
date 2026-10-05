using UnityEngine;

namespace Oheangbu.Combat
{
    [CreateAssetMenu(menuName = "Oheangbu/Combat/Playtest Locomotion")]
    public sealed class PlayerLocomotionProfileSO : ScriptableObject
    {
        [Min(.1f)] public float WalkSpeed = 2.2f;
        [Min(.1f)] public float RunSpeed = 5.5f;
        // #300 (user, 2026-09-28): the run key toggles running instead of running while held. Off keeps the held behaviour.
        // A latched run ends after this long without movement input, and on crouch, sit or drawing; jumps and dodges keep it.
        public bool SprintToggle;
        [Min(0f)] public float SprintToggleStopSeconds = .35f;
        [Min(.1f)] public float CrouchSpeed = 1.2f;
        public bool CrouchRollEnabled;
        public bool PreserveAuthoredFootRoll;
        [Range(.4f, 1.2f)] public float CrouchRollSeconds = .8f;
        public float CrouchingHeight = 1.25f, CrouchingEyeHeight = 1.1f, CrouchTransitionSeconds = .22f;
        [Min(.1f)] public float Acceleration = 16f;
        [Min(.1f)] public float Deceleration = 22f;
        [Min(0f)] public float AirAcceleration = 4f;
        [Min(.05f)] public float JumpHeight = .75f;
        [Tooltip("D308-9d: a jump starts only from walkable ground (a support whose slope is within the controller's slope limit). Off = the old rule: any contact below the capsule, which let repeated jumps climb cliff faces.")]
        public bool JumpNeedsWalkableGround = true;
        [Tooltip("How far above the walkable support the feet may be for a jump to start (m).")]
        [Range(.02f, .2f)] public float JumpSupportGap = .06f;
        [Range(.02f, .4f)] public float GroundSnap = .24f;
        [Min(.1f)] public float GroundStickSpeed = 2f;
        [Min(.1f)] public float SitDownSeconds = .55f;
        [Min(.1f)] public float StandUpSeconds = .45f;
        [Range(.6f, 1.5f)] public float SittingHeight = .95f;
        [Range(.4f, 1.5f)] public float SittingEyeHeight = .78f;
        public LayerMask GroundLayers = ~0;
    }
}
