using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    [CreateAssetMenu(menuName = "Oheangbu/Demo/Summon Combat Profile", fileName = "SummonCombatProfile")]
    public sealed class SummonCombatProfile : ScriptableObject
    {
        public string Letter = "곰";
        public Element Element = Element.Wood;
        public GameObject PresentationPrefab;
        public AnimationClip IdleClip, WalkClip, AttackClip;
        public AnimationClip FormationClip, DissolveClip;
        public GameObject FormationSeal, DissolveDebris;
        [Min(.01f)] public float WalkClipMetresPerSecond = 1.5f;
        [Min(0)] public float FormationSeconds = 1.2f;
        [Min(.01f)] public float ActivitySeconds = 20f;
        [Min(0)] public float DissolveSeconds = .8f;
        [Min(.01f)] public float FollowSpeed = 3.6f;
        [Min(.01f)] public float DetectionRange = 12f;
        [Min(.01f)] public float LeashRange = 18f;
        [Min(.01f)] public float AttackRange = 2.4f;
        [Min(0)] public float WindupSeconds = .7f;
        [Min(.01f)] public float CooldownSeconds = 1.2f;
        [Min(0)] public float DamageMultiplier = .8f;
        [Min(0)] public float SpawnDistance = 4f;
        [Min(.01f)] public float FootprintWidth = .8f, FootprintLength = 1.8f, BodyHeight = 1.7f;
        public Vector3 FootprintOffset = Vector3.zero;
        [Range(0, 89)] public float MaxSlope = 32f;
        [Min(0)] public float FootHeightTolerance = .3f;
        [Min(.01f)] public float GroundProbeUp = 2.5f, GroundProbeDown = 4f;
        public LayerMask GroundMask = ~0;
        public int NavMeshAreaMask = -1;
        [Min(0)] public float FollowDistance = 2.5f;
        [Min(.01f)] public float RepathSeconds = .35f;

        [Header("Optional fixed-direction ground roots (demo TEST)")]
        public bool RootAttackEnabled;
        public Vector3 RootStartOffset = new Vector3(0, 0, .75f);
        [Min(.01f)] public float RootAttackRange = 6f, RootAttackWidth = .7f, RootTravelSpeed = 8f;
        [Min(0)] public float RootWindupSeconds = .55f, RootRecoverySeconds = .45f;
        [Min(.01f)] public float RootCooldownSeconds = 1.2f;
        [Min(0)] public float RootDamageMultiplier = 1f;
        [Min(.01f)] public float RootGroundSampleSpacing = .25f, RootMaxStepHeight = .3f;
        [Min(0)] public float RootSegmentHoldSeconds = .45f, RootSegmentRetractSeconds = .25f;
        public AnimationClip RootAttackClip;
        public Mesh RootMesh;
        public Material RootMaterial;

        [Header("Optional distance-keeping fire fan (demo TEST)")]
        public bool FlameAttackEnabled;
        [Min(.01f)] public float FlamePreferredDistance = 3.5f, FlameMinimumDistance = 2.5f, FlameRange = 4.5f;
        [Range(1, 89)] public float FlameHalfAngleDegrees = 30f;
        [Min(0)] public float FlameWindupSeconds = .45f, FlameRecoverySeconds = .5f;
        [Min(.01f)] public float FlameSpraySeconds = .65f, FlameVerticalTolerance = 1.6f;
        [Min(0)] public float FlameDamageMultiplier = 1f;
        public Vector3 FlameOriginOffset = new Vector3(0, 1.15f, .8f);
        public AnimationClip BackwardWalkClip, FlameAttackClip;
        public GameObject FlamePrefab;
        public bool FlameConstrainVisual;
        public Shader FlameAdditiveShader, FlameAlphaShader;
        [Min(.001f)] public float FlameBoundaryFeather = .15f;
        [Range(0, 1)] public float FlameVisualIntensity = .7f;

        [Header("Optional metal tiger leap and alternating claws (demo TEST)")]
        public bool TigerAttackEnabled;
        [Min(.01f)] public float TigerRunSpeed = 4f, TigerRunClipMetresPerSecond = 2.75f;
        [Min(.01f)] public float TigerLeapMinRange = 3f, TigerLeapMaxRange = 5f, TigerLandingStandOff = 2f;
        [Min(0)] public float TigerLeapPrepareSeconds = .2f, TigerLeapArcHeight = .38f;
        [Min(.01f)] public float TigerLeapLandSeconds = .8f, TigerLeapSeconds = 1.2f;
        [Min(.01f)] public float TigerClawSeconds = .8f, TigerClawContactSeconds = .3f;
        [Min(0)] public float TigerClawDamageMultiplier = .5f, TigerRecoverySeconds = .35f;
        public AnimationClip TigerRunClip, TigerLeapClip, TigerClawLeftClip, TigerClawRightClip;

        [Header("Optional slow earth club sweep (demo TEST)")]
        public bool ClubAttackEnabled;
        [Min(.01f)] public float ClubRange = 2.8f, ClubHeightTolerance = 1.5f;
        [Range(1, 89)] public float ClubHalfAngleDegrees = 70f;
        [Range(0, 20)] public float ClubAngularToleranceDegrees = 8f;
        [Min(0)] public float ClubOriginHeight = .8f, ClubWindupSeconds = .8f, ClubRecoverySeconds = .6f;
        [Min(.01f)] public float ClubSweepSeconds = .3f;
        [Min(0)] public float ClubDamageMultiplier = 1.2f;

        [Header("Optional fixed water stream support (demo TEST)")]
        public bool WaterAttackEnabled;
        [Min(.01f)] public float WaterPreferredDistance = 4f, WaterMinimumDistance = 2.5f, WaterRange = 6f;
        [Min(.01f)] public float WaterRadius = .22f, WaterTravelSpeed = 12f, WaterStreamSeconds = .7f;
        [Min(0)] public float WaterWindupSeconds = .6f, WaterRecoverySeconds = .5f, WaterDamageMultiplier = 1f;
        public Vector3 WaterOriginOffset = new Vector3(0, .7f, 1f);
        public AnimationClip WaterAttackClip;
        public Material WaterJetMaterial, WaterFoamMaterial;

        public bool TryValidate(out string error, bool requirePresentation = false)
        {
            bool mapped = (Letter == "곰" && Element == Element.Wood) || (Letter == "놈" && Element == Element.Fire) ||
                (Letter == "솜" && Element == Element.Metal) || (Letter == "몸" && Element == Element.Earth) ||
                (Letter == "옴" && Element == Element.Water);
            if (!mapped) { error = "Summon letter and element must match one of the five demo summons."; return false; }
            if (requirePresentation && PresentationPrefab == null) { error = "Presentation prefab is required for runtime spawning."; return false; }
            float[] nonNegative = { FormationSeconds, DissolveSeconds, WindupSeconds, DamageMultiplier,
                SpawnDistance, MaxSlope, FootHeightTolerance, FollowDistance };
            foreach (float value in nonNegative)
                if (!float.IsFinite(value) || value < 0) { error = "Timings and damage must be finite and non-negative."; return false; }
            float[] positive = { ActivitySeconds, FollowSpeed, DetectionRange, LeashRange, AttackRange, CooldownSeconds,
                WalkClipMetresPerSecond, FootprintWidth, FootprintLength, BodyHeight, GroundProbeUp, GroundProbeDown, RepathSeconds };
            foreach (float value in positive)
                if (!float.IsFinite(value) || value <= 0) { error = "Activity, movement, ranges and cooldown must be finite and positive."; return false; }
            if (AttackRange > DetectionRange || DetectionRange > LeashRange)
            { error = "Ranges must satisfy AttackRange <= DetectionRange <= LeashRange."; return false; }
            if (WindupSeconds >= ActivitySeconds)
            { error = "An attack must be able to finish inside the activity lifetime."; return false; }
            if (MaxSlope >= 90 || FollowDistance >= LeashRange || GroundMask.value == 0 || NavMeshAreaMask == 0)
            { error = "Placement needs a walkable slope, ground/nav layers, and follow distance below leash range."; return false; }
            if (RootAttackEnabled)
            {
                if (!float.IsFinite(RootStartOffset.x) || !float.IsFinite(RootStartOffset.y) || !float.IsFinite(RootStartOffset.z))
                { error = "Root start offset must be finite."; return false; }
                foreach (float value in new[] { RootAttackRange, RootAttackWidth, RootTravelSpeed, RootCooldownSeconds, RootGroundSampleSpacing, RootMaxStepHeight })
                    if (!float.IsFinite(value) || value <= 0) { error = "Root range, width, speed, cooldown and ground sampling must be finite and positive."; return false; }
                foreach (float value in new[] { RootWindupSeconds, RootRecoverySeconds, RootDamageMultiplier, RootSegmentHoldSeconds, RootSegmentRetractSeconds })
                    if (!float.IsFinite(value) || value < 0) { error = "Root timing and damage must be finite and non-negative."; return false; }
                if (RootAttackRange <= AttackRange || RootAttackRange > DetectionRange || RootAttackWidth > RootAttackRange ||
                    RootGroundSampleSpacing > RootAttackWidth || RootAttackRange / RootGroundSampleSpacing > 256 ||
                    RootWindupSeconds + RootAttackRange / RootTravelSpeed >= ActivitySeconds)
                { error = "Root attack must extend beyond horn range, remain detectable, use bounded ground samples and fit the activity budget."; return false; }
            }
            if (!float.IsFinite(FootprintOffset.x) || !float.IsFinite(FootprintOffset.y) || !float.IsFinite(FootprintOffset.z))
            { error = "Footprint offset must be finite."; return false; }
            if (FlameAttackEnabled)
            {
                if(FlameConstrainVisual && (FlameAdditiveShader==null || FlameAlphaShader==null ||
                    !float.IsFinite(FlameBoundaryFeather) || FlameBoundaryFeather<=0 ||
                    !float.IsFinite(FlameVisualIntensity) || FlameVisualIntensity<0 || FlameVisualIntensity>1))
                { error="Constrained flame needs both derived shaders and finite visual settings."; return false; }
                if (Letter != "놈" || Element != Element.Fire || RootAttackEnabled)
                { error = "Distance-keeping flame is exclusive to the fire summon and cannot also select roots."; return false; }
                foreach (float value in new[] { FlamePreferredDistance, FlameMinimumDistance, FlameRange, FlameHalfAngleDegrees, FlameSpraySeconds, FlameVerticalTolerance })
                    if (!float.IsFinite(value) || value <= 0) { error = "Flame distances, cone, spray and height must be finite and positive."; return false; }
                foreach (float value in new[] { FlameWindupSeconds, FlameRecoverySeconds, FlameDamageMultiplier })
                    if (!float.IsFinite(value) || value < 0) { error = "Flame timing and damage must be finite and non-negative."; return false; }
                if (!float.IsFinite(FlameOriginOffset.x) || !float.IsFinite(FlameOriginOffset.y) || !float.IsFinite(FlameOriginOffset.z) ||
                    FlameMinimumDistance >= FlamePreferredDistance || FlamePreferredDistance > FlameRange || FlameRange > DetectionRange ||
                    FlameHalfAngleDegrees >= 90 || FlameWindupSeconds + FlameSpraySeconds >= ActivitySeconds)
                { error = "Flame distances must satisfy minimum < preferred <= range <= detection, with a finite mouth origin and a cast inside the lifetime."; return false; }
            }
            if (TigerAttackEnabled)
            {
                if (Letter != "솜" || Element != Element.Metal || RootAttackEnabled || FlameAttackEnabled)
                { error = "Tiger attacks are exclusive to the metal summon and cannot select roots or flame."; return false; }
                foreach (float value in new[] { TigerRunSpeed, TigerRunClipMetresPerSecond, TigerLeapMinRange,
                    TigerLeapMaxRange, TigerLandingStandOff, TigerLeapLandSeconds, TigerLeapSeconds,
                    TigerClawSeconds, TigerClawContactSeconds })
                    if (!float.IsFinite(value) || value <= 0)
                    { error = "Tiger movement, ranges and clip times must be finite and positive."; return false; }
                foreach (float value in new[] { TigerLeapPrepareSeconds, TigerLeapArcHeight, TigerClawDamageMultiplier, TigerRecoverySeconds })
                    if (!float.IsFinite(value) || value < 0)
                    { error = "Tiger preparation, clearance and damage must be finite and non-negative."; return false; }
                if (TigerLandingStandOff >= TigerLeapMinRange || TigerLandingStandOff > AttackRange ||
                    TigerLeapMinRange >= TigerLeapMaxRange || TigerLeapMaxRange > DetectionRange ||
                    TigerLeapPrepareSeconds >= TigerLeapLandSeconds || TigerLeapLandSeconds >= TigerLeapSeconds ||
                    TigerClawContactSeconds >= TigerClawSeconds || TigerLeapSeconds + TigerClawSeconds * 2 >= ActivitySeconds)
                { error = "Tiger leap needs ordered distances/times, reachable landing claws and a combo inside its lifetime."; return false; }
            }
            if (ClubAttackEnabled)
            {
                if (Letter != "몸" || Element != Element.Earth || RootAttackEnabled || FlameAttackEnabled || TigerAttackEnabled)
                { error = "Club sweep is exclusive to the earth summon."; return false; }
                foreach (float value in new[] { ClubRange, ClubHeightTolerance, ClubHalfAngleDegrees, ClubSweepSeconds })
                    if (!float.IsFinite(value) || value <= 0)
                    { error = "Club range, height, angle and sweep must be finite and positive."; return false; }
                foreach (float value in new[] { ClubOriginHeight, ClubWindupSeconds, ClubRecoverySeconds, ClubDamageMultiplier, ClubAngularToleranceDegrees })
                    if (!float.IsFinite(value) || value < 0)
                    { error = "Club origin, timing, power and angular tolerance must be finite and non-negative."; return false; }
                if (ClubRange > DetectionRange || ClubHalfAngleDegrees >= 90 || ClubAngularToleranceDegrees > 20 ||
                    ClubAngularToleranceDegrees > ClubHalfAngleDegrees || ClubOriginHeight > BodyHeight ||
                    ClubWindupSeconds + ClubSweepSeconds + ClubRecoverySeconds >= ActivitySeconds)
                { error = "Club sweep must fit detection, body height and activity, with bounded angles."; return false; }
            }
            if (WaterAttackEnabled)
            {
                if (Letter != "옴" || Element != Element.Water || RootAttackEnabled || FlameAttackEnabled || TigerAttackEnabled || ClubAttackEnabled)
                { error = "Water stream is exclusive to the water summon."; return false; }
                foreach (float value in new[] { WaterPreferredDistance, WaterMinimumDistance, WaterRange, WaterRadius, WaterTravelSpeed, WaterStreamSeconds })
                    if (!float.IsFinite(value) || value <= 0)
                    { error = "Water ranges, radius, speed and stream must be finite and positive."; return false; }
                foreach (float value in new[] { WaterWindupSeconds, WaterRecoverySeconds, WaterDamageMultiplier })
                    if (!float.IsFinite(value) || value < 0)
                    { error = "Water timing and power must be finite and non-negative."; return false; }
                if (!float.IsFinite(WaterOriginOffset.x) || !float.IsFinite(WaterOriginOffset.y) || !float.IsFinite(WaterOriginOffset.z) ||
                    WaterMinimumDistance >= WaterPreferredDistance || WaterPreferredDistance > WaterRange || WaterRange > DetectionRange ||
                    WaterRadius >= WaterRange || WaterRange / WaterTravelSpeed >= WaterStreamSeconds || WaterRecoverySeconds < WaterRange / WaterTravelSpeed ||
                    WaterWindupSeconds + WaterStreamSeconds + WaterRecoverySeconds >= ActivitySeconds)
                { error = "Water needs a finite mouth, minimum < preferred <= range <= detection, a front reaching range during stream, and a cast within activity."; return false; }
            }
            error = null; return true;
        }
    }
}
