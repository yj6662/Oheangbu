using System;
using UnityEngine;

namespace Oheangbu.Data
{
    [Serializable]
    public sealed class SecondarySpringSettings
    {
        [Range(.1f, 12f)] public float FrequencyHz = 2.5f;
        [Range(.05f, 2f)] public float DampingRatio = .75f;
        [Range(0f, 80f)] public float MaximumSwingDegrees = 35f;
        [Range(0f, 2f)] public float GravityScale = 1f;
        [Range(0f, 2f)] public float TranslationInertia = .65f;
        [Range(0f, 2f)] public float RotationInertia = .4f;
        [Range(0f, 2f)] public float WindScale = .5f;
    }

    [Serializable]
    public sealed class SecondaryClothSettings
    {
        [Range(30f, 480f)] public float SolverFrequency = 90f;
        [Range(0f, 1f)] public float BendingStiffness = .45f;
        [Range(0f, 1f)] public float StretchingStiffness = .95f;
        [Range(0f, 1f)] public float Damping = .25f;
        [Range(0f, 1f)] public float Friction = .5f;
        [Range(0f, 2f)] public float WorldVelocityScale = .5f;
        [Range(0f, 2f)] public float WorldAccelerationScale = .35f;
        [Range(0f, 2f)] public float WindScale = .5f;
        public bool UseGravity = true;
        public bool UseTethers = true;
        public bool ContinuousCollision = true;
    }

    [CreateAssetMenu(menuName = "Oheangbu/Presentation/Player Secondary Motion")]
    public sealed class PlayerSecondaryMotionProfileSO : ScriptableObject
    {
        // Opt-in for playable characters; full source-triangle diagnostics remain available.
        public bool UseCapsuleOrnamentCollision;
        [Range(1, 6)] public int CapsuleCollisionIterations = 3;
        [Min(0f)] public float ClothColliderSelectionHz;
        [Range(60, 240)] public int SimulationHz = 120;
        [Range(1, 32)] public int MaximumSubsteps = 16;
        [Range(.02f, .25f)] public float MaximumCatchUpSeconds = .1f;
        [Min(.01f)] public float TeleportDistance = 1f;
        [Range(15f, 180f)] public float TeleportAngleDegrees = 100f;
        [Min(.1f)] public float MaximumAnchorAcceleration = 35f;
        [Min(.1f)] public float MaximumAngularAcceleration = 100f;
        [Min(.005f)] public float MinimumLeverLength = .03f;
        [Range(1, 128)] public int MaximumDrivenTransforms = 72;
        public Vector3 NearGravityInCameraSpace = new Vector3(0f, -9.81f, 0f);
        public SecondarySpringSettings Ornament = new SecondarySpringSettings();
        public SecondarySpringSettings BoneChain = new SecondarySpringSettings
        { FrequencyHz = 3f, DampingRatio = .85f, MaximumSwingDegrees = 24f, TranslationInertia = .4f };
        public SecondaryClothSettings Cloth = new SecondaryClothSettings();
    }
}
