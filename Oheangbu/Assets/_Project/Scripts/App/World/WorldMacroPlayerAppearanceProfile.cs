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

        [Header("#300 Dodge presentation (distance, time and immunity stay in CombatConfig)")]
        [Tooltip("Decouple the dodge clip from the dash: the dash plays the moving span, then a recovery tail plays the rest.")]
        public bool DodgeRecoveryTail;
        [Tooltip("Clip normalized time at dash start (x) and dash end (y); the 4-way blend shares one time.")]
        public Vector2 DodgeDashSpan = new Vector2(.15f, .72f);
        [Tooltip("Seconds of the tail that plays the clip from the dash end to its end while standing.")]
        [Min(.01f)] public float DodgeTailSeconds = .3f;
        [Tooltip("With movement input the tail hands over to locomotion after this many seconds (the controller cross-fades).")]
        [Min(0f)] public float DodgeTailMovingSeconds = .08f;
        [Tooltip("Planar speed (m/s) above which the tail counts as moving.")]
        [Min(0f)] public float DodgeTailCutSpeed = 1.2f;
    }
}
