using UnityEngine;

namespace Oheangbu.Data
{
    [CreateAssetMenu(menuName = "Oheangbu/Presentation/Drawing Pose Profile")]
    public sealed class DrawingPoseProfileSO : ScriptableObject
    {
        [Min(0f)] public float EnterSpeed = 10f;
        [Min(0f)] public float ExitSpeed = 7f;
        [Min(0f)] public float BodyFollowSpeed = 9f;
        public Vector3 FallbackHandGripOffset = new Vector3(0f, 0.04f, 0f);
        public Vector3 FallbackHandGripEuler = Vector3.zero;
        [Min(0.01f)] public float BrushLength = 0.28f;
        [Range(-180f, 180f)] public float BrushRollDegrees = -90f;
        // Optional V2 arm-orientation postprocess. Existing profiles keep the original IK.
        public bool EnableArmRollRedistribution = false;
        [Min(1f)] public float ArmRollDegreesPerSecond = 720f;
        [Range(5f, 90f)] public float ArmRollTrackingWindowDegrees = 40f;
        [Min(0.01f)] public float PreferredTipDepth = 0.55f;
        [Min(0.01f)] public float MinTipDepth = 0.18f;
        [Min(0.01f)] public float MaxTipDepth = 1.1f;
        [Min(0f)] public float PenLiftDistance = 0.035f;
        [Range(0f, 1f)] public float SmallStrokeBodyWeight = 0.18f;
        [Min(0.01f)] public float FullBodyStrokeSpan = 0.8f;
        [Range(0.5f, 0.995f)] public float MaxArmExtension = 0.96f;
        [Min(0.01f)] public float ElbowOut = 0.3f;
        [Min(0.01f)] public float ElbowDown = 0.2f;
        [Range(0f, 1f)] public float GripAlignedElbowWeight = 0.9f;
        [Range(0f, 30f)] public float ShoulderYaw = 10f;
        [Range(0f, 20f)] public float ShoulderLift = 7f;
        [Range(0f, 25f)] public float ChestYaw = 8f;
        [Range(0f, 20f)] public float ChestLean = 5f;
        [Range(0f, 15f)] public float PelvisYaw = 3f;
        [Range(0f, 0.08f)] public float PelvisSway = 0.025f;
        [Range(0f, 0.3f)] public float OffHandCounter = 0.07f;
        public Vector3 FingerCurlAxis = Vector3.left;
        public Vector3 ThumbCurlDegrees = new Vector3(30f, 10f, 5f);
        public Vector2 ThumbOppositionDegrees = new Vector2(19.79f, 26.70f);
        public Vector3 IndexCurlDegrees = new Vector3(31.295f, 53.503f, 33.109f);
        public Vector3 MiddleCurlDegrees = new Vector3(36.846f, 53.726f, 37.861f);
        public Vector3 RingCurlDegrees = new Vector3(31.325f, 53.503f, 31.509f);
        public Vector3 PinkyCurlDegrees = new Vector3(28f, 46f, 24.33f);
        // Kept for historical profile/diagnostic compatibility; runtime uses the measured per-finger values.
        public Vector3 FingerCurlDegrees = new Vector3(40f, 58f, 35f);
        [Range(0f, 1f)] public float GripWeight = 1f;
        // Optional import-calibrated offsets for reconstructed hands (thumb/index/middle/ring/little, three joints each).
        // An empty array keeps existing profiles on their authored scalar curl path.
        public Quaternion[] CalibratedRightFingerOffsets = new Quaternion[0];
        // Optional authored grasp correction in the imported wrist's local basis.
        public bool OverrideHandGrip;
        public Vector3 HandGripLocalPosition;
        public Quaternion HandGripLocalRotation = Quaternion.identity;
        public bool KeepGripClosedOnPenLift;
        [Min(0.01f)] public float WorldPlaneDistance = 0.65f;
        public Vector2 WorldPlaneHalfSize = new Vector2(0.32f, 0.25f);
        [Min(0.01f)] public float WorldPlaneHeight = 1.3f;

        public Vector3 CurlDegrees(int finger)
        {
            switch (finger)
            {
                case 0: return ThumbCurlDegrees;
                case 1: return IndexCurlDegrees;
                case 2: return MiddleCurlDegrees;
                case 3: return RingCurlDegrees;
                default: return PinkyCurlDegrees;
            }
        }
    }
}
