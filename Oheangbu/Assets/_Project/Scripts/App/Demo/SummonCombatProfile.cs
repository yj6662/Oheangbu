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
        [Tooltip("#308 D308-107 (f) [TEST]: 참이면 옛 소환 연출(인장 · Formation/Mane/Dissolve 입자 · 파편)을 내지 않는다 — 새 먹 몸만. 몸 · 동작 · 공격 연출 · 시계는 그대로. 라이브 사본에만 retire:summon 명령이 켠다(기록 · 되돌리기).")]
        public bool RetireLegacySummonFx308;
        [Tooltip("#308 D308-108 [TEST]: 활동이 시작된 뒤 이 시간 동안만 몸 심(_CoreGlow)이 밝아진다 — 12 Hz 셀 2칸(1/6 s). 0이면 끔. 지속 발광 없음.")]
        [Min(0)] public float CoreBirthGlowSeconds308 = 1f / 6f;
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
        [Tooltip("#308 [TEST, 기본안 — 사용자 확인 대기] 소환수의 기본 위력. 책의 소환 항목은 위력 0이라, 손으로 그린 소환은 이 값 x 필세 x 체류 배율로 위력을 얻는다. 10 = 기존 검사가 직접 넣던 값. 데이터 쓰기는 spell308-summon-power 명령(원본 + 사본).")]
        [Min(0)] public float BasePower = 10f;
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
        [Tooltip("#308 SPEC-SPELL-VFX-OVERHAUL-308 11.47 [TEST]: 있으면 뿌리 조각을 RootMaterial 대신 이 먹 재질(InkSolid308)로 그린다 — 솟을 때 먹으로 나타나 2칸에 걸쳐 안료가 스민다(_Soak). 비면 식물 껍질 그대로. 라이브 사본에만 beast:ink:connect가 쓴다(기록 · 되돌리기).")]
        public Material RootInk308Material;

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
        [Tooltip("#308 SPEC-SPELL-VFX-OVERHAUL-308 11.44 [TEST]: 있으면 불숨을 KTP 불꽃 대신 이 먹 몸으로 그린다 — 균일 배율(사거리 / 저작 사거리), 원뿔 각 = 계획 반각, 셰이더 교체 없음. 비면 FlamePrefab 경로 그대로. 라이브 사본에만 haetae:flame:connect가 쓴다(기록 · 되돌리기).")]
        public GameObject FlameInk308Prefab;
        [Min(.01f)] public float FlameInk308AuthoredRange = 4.5f;

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
        [Tooltip("#308 SPEC-SPELL-VFX-OVERHAUL-308 11.47 [TEST]: 있으면 물줄기 관을 WaterJetMaterial 대신 이 먹 재질(InkSolid308)로 그리고 거품 방울을 만들지 않는다 — 결이 앞으로 흐르고, 먹으로 나와 2칸에 걸쳐 안료가 스민다. 비면 물 셰이더 + 거품 그대로. 라이브 사본에만 beast:ink:connect가 쓴다.")]
        public Material WaterJetInk308Material;

        [Header("D308-115 Behaviour (TEST, all off by default — live copies only via behaviour308:on)")]
        [Tooltip("곰: 확정 명중한 적에게 뿌리 표식. 표식 적을 플레이어가 맞히면 다음 Tick에 BasePower × RootMarkBurstMultiplier 한 번.")]
        public bool RootMark308Enabled;
        [Min(.01f)] public float RootMarkSeconds = 4f;
        [Min(0)] public float RootMarkBurstMultiplier = .6f;
        [Tooltip("놈: 플레이어를 노린 공격을 끝낸 적에게 죄 표식, 표식 적만 화염 부채로 응징(간격마다 한 번).")]
        public bool JudgeEnabled;
        [Min(0)] public float JudgeFollowDistance = 4f;
        [Min(.01f)] public float JudgeIntervalSeconds = 2.5f, JudgeMarkSeconds = 5f;
        [Min(0)] public float JudgeMultiplier = 1f;
        [Tooltip("몸: 패링 성공 · 되받아치기 · 응보 뒤 BeatDelay초에 같은 적을 몽둥이 경로로 내려찍음.")]
        public bool BeatEnabled;
        [Min(0)] public float BeatDelay = .3f, BeatMultiplier = 1.8f, BeatSideDistance = 3f;
        [Min(.01f)] public float BeatCooldown = 3f;
        [Tooltip("솜: HP 비율이 문턱 이하인 적을 먼저 노리고 할퀴기 위력 배.")]
        public bool HuntEnabled;
        [Range(0, 1)] public float HuntHpThreshold = .25f;
        [Min(0)] public float HuntMultiplier = 2f;
        [Tooltip("옴: 패링 방어막이 없을 때 플레이어에게 닿는 원거리 탄을 막음(충전식). 물줄기는 제자리에서만.")]
        public bool WardEnabled;
        [Min(1)] public int WardCharges = 2;
        [Min(.01f)] public float WardRechargeSeconds = 8f;
        [Min(0)] public float WardFollowDistance = 2f;

        public bool TryValidate(out string error, bool requirePresentation = false)
        {
            bool mapped = (Letter == "곰" && Element == Element.Wood) || (Letter == "놈" && Element == Element.Fire) ||
                (Letter == "솜" && Element == Element.Metal) || (Letter == "몸" && Element == Element.Earth) ||
                (Letter == "옴" && Element == Element.Water);
            if (!mapped) { error = "Summon letter and element must match one of the five demo summons."; return false; }
            if (requirePresentation && PresentationPrefab == null) { error = "Presentation prefab is required for runtime spawning."; return false; }
            float[] nonNegative = { FormationSeconds, DissolveSeconds, WindupSeconds, DamageMultiplier, BasePower,
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
            // D308-115 values are checked even while every behaviour is off, so a NaN never waits in the asset for the switch.
            foreach (float value in new[] { RootMarkSeconds, RootMarkBurstMultiplier, JudgeFollowDistance, JudgeIntervalSeconds, JudgeMarkSeconds,
                JudgeMultiplier, BeatDelay, BeatMultiplier, BeatSideDistance, BeatCooldown, HuntHpThreshold, HuntMultiplier,
                WardRechargeSeconds, WardFollowDistance })
                if (!float.IsFinite(value) || value < 0) { error = "D308-115 behaviour values must be finite and non-negative."; return false; }
            if (RootMark308Enabled || JudgeEnabled || BeatEnabled || HuntEnabled || WardEnabled)
            {
                if ((RootMark308Enabled && Letter != "곰") || (JudgeEnabled && Letter != "놈") || (BeatEnabled && Letter != "몸") ||
                    (HuntEnabled && Letter != "솜") || (WardEnabled && Letter != "옴"))
                { error = "D308-115 behaviours are exclusive: root mark 곰, judge 놈, beat 몸, hunt 솜, ward 옴."; return false; }
                if (RootMarkSeconds <= 0 || JudgeIntervalSeconds <= 0 || JudgeMarkSeconds <= 0 || BeatCooldown <= 0 || WardRechargeSeconds <= 0 ||
                    WardCharges < 1 || HuntHpThreshold > 1 || BeatDelay >= BeatCooldown ||
                    JudgeFollowDistance >= LeashRange || BeatSideDistance >= LeashRange || WardFollowDistance >= LeashRange)
                { error = "D308-115 lifetimes, intervals and charges must be positive; follow distances stay inside the leash; beat delay below cooldown."; return false; }
                if ((JudgeEnabled && !FlameAttackEnabled) || (BeatEnabled && !ClubAttackEnabled) || (HuntEnabled && !TigerAttackEnabled))
                { error = "Judge needs the flame fan, beat needs the club sweep and hunt needs the tiger claws."; return false; }
            }
            error = null; return true;
        }
    }
}
