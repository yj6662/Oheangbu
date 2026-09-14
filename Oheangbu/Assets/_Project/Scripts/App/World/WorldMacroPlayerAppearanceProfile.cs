using UnityEngine;

namespace Oheangbu.App.World
{
    [CreateAssetMenu(menuName = "Oheangbu/World Macro/Player Appearance Profile")]
    public sealed class WorldMacroPlayerAppearanceProfile : ScriptableObject
    {
        public RuntimeAnimatorController Controller;
        public AnimationClip Idle;
        public AnimationClip Walk;
        public AnimationClip Run;
        public bool DirectionalLocomotion;
        public bool RecoveryMotions;
        public bool IdleLookTurn;
        public bool NaturalLocomotion;
        public AnimationClip CrouchRoll;
        [Range(45f, 100f)] public float IdleLookDegrees = 90f;
        public AnimationClip[] DirectionalCrouch = new AnimationClip[4];
        public AnimationClip CrouchIdle, StartForward, TurnLeft, TurnRight;
        public AnimationClip[] DirectionalWalk = new AnimationClip[4];
        public AnimationClip[] DirectionalRun = new AnimationClip[4];
        public AnimationClip[] DirectionalDodge = new AnimationClip[4];
        public AnimationClip SitDown, SitIdle, StandUp, JumpRise, JumpFall, Land;
        [Min(.1f)] public float ModelHeight = 1.75f;
        [Min(.01f)] public float WalkSpeed = 1.5537528f;
        [Min(.01f)] public float RunSpeed = 4.5f;
        [Min(.01f)] public float NativeRunSpeed = 5.362285f;
        [Min(0f)] public float VelocityDamping = 12f;
        public float FacingYaw;
        public float UniformScale = 1f;
        public Vector3 LocalOffset;
        [Range(0f, 1f)] public float DrawingArmWeight = .32f;
        [Min(.1f)] public float DrawingReach = .58f;
        [Min(.05f)] public float BlinkDuration = .16f;
        [Min(.25f)] public float BlinkInterval = 4.2f;
    }
}
