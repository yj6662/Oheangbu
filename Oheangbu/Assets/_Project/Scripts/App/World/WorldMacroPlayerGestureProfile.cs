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

        [Header("Optional vertical double-hook grip (쌍구법) — off keeps grip A unchanged")]
        [Tooltip("Drawing blends into the fitted vertical grip by drawing weight × (1 − harvest); carry and harvest keep grip A.")]
        public bool ShuanggouGrip;
        [Tooltip("Hand-local brush GripSocket position of the vertical grip, fitted by WorldMacroPlayerReRigAuthoring.Shuanggou297 fit.")]
        public Vector3 ShuanggouGripPosition;
        [Tooltip("Hand-local GripSocket rotation of the vertical grip: the shaft runs along the radial→ulnar axis, bristles below the little finger.")]
        public Quaternion ShuanggouGripRotation = Quaternion.identity;
        [Tooltip("Fit input: metres from the ferrule (Bristle_01) back to the grasp centre. 0 = automatic from the handle mesh.")]
        [Min(0f)] public float ShuanggouHoldFromFerrule;
        [Tooltip("Back of the hand toward the view (palm faces the stroke) instead of the finger direction toward view-up.")]
        public bool ShuanggouDorsalFacing = true;
        [Tooltip("Seconds for the close-up arm + brush to rise into view when draw mode opens.")]
        [Min(0f)] public float NearRaiseSeconds = .28f;
        [Tooltip("Camera-local start offset of the rising close-up arm (m).")]
        public Vector3 NearRaiseOffset = new Vector3(.06f, -.42f, -.12f);
        [Tooltip("Pen-grip elbow: out to the right, following the brush right and dropping as it moves left (replaces ElbowOutward/ElbowDown for the vertical grip).")]
        public bool ShuanggouElbowRule;
        public float ShuanggouElbowRight = .9f, ShuanggouElbowRightPerX = .5f;
        [Min(0f)] public float ShuanggouElbowDown = .25f, ShuanggouElbowDownPerLeft = .8f;
        [Tooltip("Bend-plane cost weights under the elbow rule: forearm-across-shaft preference and authored-direction preference.")]
        [Min(0f)] public float ShuanggouBendPerpendicularWeight = .25f, ShuanggouBendDesiredWeight = 2f;
        [Tooltip("Fist grip for the big brush: elbow anchored out to the right, up/down strokes bend the elbow, left/right strokes turn the wrist (brush parallel to the upper arm on the right, perpendicular on the left).")]
        public bool ShuanggouFist;
        [Tooltip("View/body-local upper-arm direction from the shoulder to the anchored elbow.")]
        public Vector3 FistElbowDirection = new Vector3(.72f, -.34f, .62f);
        [Tooltip("Seconds for the elbow to drift back to its anchor after reach moved it.")]
        [Min(0f)] public float FistElbowReturn = .35f;
        [Tooltip("Centered screen x (-1..1) where the wrist sweep is fully perpendicular (x) and fully parallel (y).")]
        public Vector2 FistSweepRange = new Vector2(0f, 1f);
        [Tooltip("Forward added to the perpendicular (left) brush direction; 0 = exactly perpendicular to the upper arm.")]
        [Min(0f)] public float FistLeftForward;
        [Tooltip("Upward lean of the brush (butt below the tip) so the fist sits under the stroke instead of covering it.")]
        [Min(0f)] public float FistRise = .3f;
        [Tooltip("Roll of the back of the hand about the forearm (degrees).")]
        public float FistKnuckleRoll;
        [Tooltip("Largest forearm roll (degrees) away from back-of-hand-up used to face the fist's brush side toward the stroke.")]
        [Range(0f, 180f)] public float FistRollLimit = 100f;
        [Tooltip("Uniform brush size multiplier (first and third person).")]
        [Range(.5f, 3f)] public float BrushSize = 1f;
        [Tooltip("Brush tilt from the drawing-plane normal; the butt leans toward ShuanggouLeanDirection so the grasp never hides the tip.")]
        [Range(0f, 45f)] public float ShuanggouViewTilt = 26f;
        [Tooltip("View/body plane direction (right, up) toward which the butt leans. Down keeps the long brush's grasp reachable below the tip.")]
        public Vector2 ShuanggouLeanDirection = new Vector2(.25f, -.97f);
        [Tooltip("Lean reduction by right/top pointer position (0 = constant lean).")]
        public Vector2 ShuanggouEdgeLean = Vector2.zero;
        [Tooltip("Extra butt lean toward the smoothed stroke direction, degrees at full sweep.")]
        [Range(0f, 20f)] public float ShuanggouTilt = 8f;
        [Tooltip("Pivot cone for small finger/wrist strokes; shrinks to zero for arm-sized strokes.")]
        [Range(0f, 20f)] public float ShuanggouCone = 8f;
        [Tooltip("Knuckle direction: view up rotated about the shaft (positive = up-left).")]
        [Range(-60f, 60f)] public float ShuanggouKnuckleRoll = 22f;
        [Tooltip("Blend of the knuckle direction toward the solved forearm (less wrist deviation).")]
        [Range(0f, 1f)] public float ShuanggouForearmFollow = .5f;
        [Tooltip("Preferred close-up shoulder-to-wrist distance as a fraction of arm reach.")]
        [Range(.5f, .97f)] public float ShuanggouReach = .80f;
        [Tooltip("Tip distance of the synthetic world drawing plane while the vertical grip is active.")]
        [Min(.1f)] public float ShuanggouDrawingDistance = .95f;
        [Tooltip("Handle-only visual length stretch pinned at the ferrule. Brush root, bristle bones, sockets and tip offset stay unit scale.")]
        [Range(1f, 1.6f)] public float BrushScale = 1f;

        [Header("Vertical grip follow chain (tip exact; arm follows)")]
        [Range(0f, 1f)] public float ShoulderFollow = .6f;
        [Tooltip("Close-up shoulder anchor follow in metres, counted inside MaximumNearShoulderCorrection.")]
        [Range(0f, .05f)] public float NearShoulderFollowMaximum = .05f;
        [Range(0f, 15f)] public float ShoulderYaw = 7f;
        [Range(0f, 15f)] public float ShoulderRaise = 6f;
        [Min(0f)] public float ElbowOutward = .30f;
        [Tooltip("Exponential time constants in seconds: grasp point, elbow bend plane, close-up shoulder.")]
        [Min(0f)] public float WristLag = .03f, ElbowLag = .10f, ShoulderLag = .16f;

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
