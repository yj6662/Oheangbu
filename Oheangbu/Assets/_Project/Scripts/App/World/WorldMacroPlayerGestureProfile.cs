using System;
using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>Presentation values for the rebuilt C02 hands. Positions are metres unless labelled bone local.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/World Macro/Player Gesture Profile")]
    public sealed class WorldMacroPlayerGestureProfile : ScriptableObject
    {
        [Serializable]
        public sealed class FingerPose
        {
            public string BoneName;
            public bool UseExplicitRestPose;
            public Quaternion RestLocalRotation = Quaternion.identity;
            public Quaternion CarryOffset = Quaternion.identity;
            public Quaternion DrawingOffset = Quaternion.identity;
            public Quaternion HarvestOffset = Quaternion.identity;
            public Quaternion ReleasedOffset = Quaternion.identity;
        }

        [Header("Imported hand calibration (Unity bone-local coordinates)")]
        public FingerPose[] Fingers = Array.Empty<FingerPose>();
        public string RightHandName = "RightHand";
        public string RightForearmName = "RightForeArm";
        public string RightUpperArmName = "RightArm";
        public string RightShoulderName = "RightShoulder";
        public string LeftHandName = "LeftHand";
        public string LeftForearmName = "LeftForeArm";
        public string LeftUpperArmName = "LeftArm";
        public string ChestName = "Spine2";
        [Tooltip("In imported hand-local units, including the source model's unit conversion.")]
        public Vector3 RightHandGripPosition;
        [Tooltip("Hand local rotation of the brush GripSocket, authored from the fitted grip.")]
        public Quaternion RightHandGripRotation = Quaternion.identity;
        [Range(0f, 1f)] public float FingerPoseWeight = 1f;

        [Header("Relaxed carry")]
        public Vector3 CarryWristOffset = new Vector3(.16f, -.44f, .10f);
        public Vector3 CarryBrushDirection = new Vector3(.30f, -.34f, .90f);
        public Vector3 CarryElbowPole = new Vector3(.30f, -.22f, -.10f);
        [Range(0f, 1f)] public float CarryArmWeight = 1f;
        public bool PreserveAnimatedCarry, AnatomicalNearReach;
        public bool OverhandGrip;
        public Vector3 HandForwardLocal = Vector3.up, HandDorsalLocal = Vector3.forward, HandThumbSideLocal = Vector3.left;
        public Quaternion HandRestInForearm = Quaternion.identity;
        [Range(0f, 1f)] public float CarryGaitInfluence = .22f;
        [Min(0f)] public float CarryMaximumSwing = .07f;
        [Range(-180f, 180f)] public float CarryBrushRoll = -90f;

        [Header("Optional seated carry, authored for the selected A grasp")]
        public bool UseSeatedCarry;
        public Vector3 SeatedWristOffset = new Vector3(.16f, -.20f, .34f);
        public Vector3 SeatedBrushDirection = new Vector3(.68f, .10f, .58f);
        public Vector3 SeatedElbowPole = new Vector3(.32f, -.14f, .02f);

        [Header("Drawing and recovery")]
        public bool ArticulatedStrokes;
        [Range(.01f, .2f)] public float WristStrokeSpan = .055f;
        [Range(.1f, .6f)] public float ArmStrokeSpan = .24f;
        [Range(0f, 100f)] public float WristAnchorWeight = 45f;
        [Range(0f, 1f)] public float DirectionalElbowWeight = .55f;
        [Min(.01f)] public float EnterResponse = 14f;
        [Min(.01f)] public float ExitResponse = 9f;
        [Min(.01f)] public float BodyResponse = 8f;
        [Range(.5f, .999f)] public float MaximumArmExtension = .97f;
        [Tooltip("Use a torso-relative down/out bend plane instead of deriving the elbow from brush roll.")]
        public bool StableDrawingElbow = true;
        [Min(0f)] public float ElbowOut = .27f;
        [Min(0f)] public float ElbowDown = .20f;
        [Range(-180f, 180f)] public float DrawingBrushRoll = -90f;
        [Min(.1f)] public float WorldDrawingDistance = .70f;
        [Min(.1f)] public float WorldDrawingHeight = 1.32f;
        public Vector2 WorldDrawingHalfSize = new Vector2(.32f, .27f);
        [Min(0f)] public float PenLiftDistance = .035f;
        [Range(0f, 20f)] public float ChestYaw = 8f;
        [Range(0f, 15f)] public float ChestLean = 5f;
        [Range(0f, 15f)] public float ShoulderLift = 6f;
        [Range(0f, .15f)] public float OffHandCounter = .045f;
        [Range(0f, 1f)] public float SmallStrokeBodyWeight = .18f;
        [Range(.05f, 1.5f)] public float LargeStrokeViewportSpan = .50f;
        [Min(.01f)] public float CommitRecoverySeconds = .16f;
        [Min(.01f)] public float InterruptRecoverySeconds = .12f;

        [Header("Harvest")]
        [Min(.01f)] public float HarvestResponse = 11f;
        public Vector3 HarvestWristOffset = new Vector3(.08f, -.17f, .40f);
        [Range(-180f, 180f)] public float HarvestBrushRoll = -90f;

        [Header("Optional close-up arm, no second Animator")]
        public Vector3 NearShoulderOffset = new Vector3(.23f, -.29f, .15f);
        [Min(.05f)] public float NearTipDepth = 1.20f;
        [Min(.05f)] public float NearMinimumDepth = .80f;
        [Min(.05f)] public float NearMaximumDepth = 1.65f;
        [Tooltip("A translation of the separate presentation shoulder, never an arm or shaft scale.")]
        [Min(0f)] public float MaximumNearShoulderCorrection = 1.25f;

        public static bool IsUsableRotation(Quaternion value)
        {
            float length = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            return !float.IsNaN(length) && !float.IsInfinity(length) && length > .000001f;
        }

        public static Quaternion SafeRotation(Quaternion value)
        {
            return IsUsableRotation(value) ? value.normalized : Quaternion.identity;
        }
    }
}
