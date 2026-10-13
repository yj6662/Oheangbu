using System;
using UnityEngine;

namespace Oheangbu.Data.Spell
{
    // SPEC-SPELL-SWING-308 sections 4 / 4a / 5 / 5a / 6 (D308-80, D308-80b) [every number TEST unless marked]:
    // the two-handed great brush, the two idle stances, the swing motions on Animator layers and the bristle deformation.
    // Presentation only. Nothing here is read by recognition, by the brush force or by the swing rule (SpellSwingProfile308
    // owns the apex and the lock; this profile only says how the body shows them).
    // Runtime lookup: Resources "Swing308/GreatBrush308Profile". Missing = the game before this stage (old brush, old arm code).
    [CreateAssetMenu(menuName = "Oheangbu/Spell/Great Brush Profile 308", fileName = "GreatBrush308Profile")]
    public sealed class GreatBrush308Profile : ScriptableObject
    {
        public const string ResourcesPath = "Swing308/GreatBrush308Profile";

        /// <summary>Pose of the brush's GripSocket in the right hand bone's local space (the bone's own axes and scale).</summary>
        [Serializable]
        public struct Grip
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public bool IsValid => Finite(Position) && Finite(Rotation.x) && Finite(Rotation.y) && Finite(Rotation.z) && Finite(Rotation.w)
                && Rotation.x * Rotation.x + Rotation.y * Rotation.y + Rotation.z * Rotation.z + Rotation.w * Rotation.w > .5f;
        }

        [Serializable]
        public struct StateEntry
        {
            [Tooltip("애니메이터 상태 이름(두 층에서 같은 이름).")]
            public string State;
            [Tooltip("클립 길이(초). 0 = 클립 없음(이 상태는 재생하지 않는다).")]
            [Min(0)] public float ClipSeconds;
            [Tooltip("클립 안의 정점 시각(초). 0 = 정점 맞추기 없음(실제 속도로 재생).")]
            [Min(0)] public float ClipApexSeconds;
            public bool Loop;
            [Tooltip("이 상태가 끝나면 이어지는 상태(비우면 전투 태세로). 예: GuardIn → GuardHold.")]
            public string Next;
            [Tooltip("Next 상태에 머무는 최소 시간(초). 잠금이 더 길면 잠금 끝까지.")]
            [Min(0)] public float HoldSeconds;
            [Tooltip("왼손이 자루를 쥐는지(x = 클립 시각 초, y = 0 또는 1). 계단 곡선: 그 시각 이전의 마지막 열쇠 값.")]
            public Vector2[] LeftHand;
        }

        [Serializable]
        public struct Alias { public string From, To; }

        [Serializable]
        public struct BristleSettings
        {
            [Header("가운데 사슬 (Bristle_01..08)")]
            [Tooltip("한쪽으로 휘는 최대 각(도). 사슬 전체의 합.")]
            [Range(0, 60)] public float MaximumBendDegrees;
            [Tooltip("가운데 사슬 스프링 주파수(Hz).")]
            [Min(.1f)] public float CentreFrequency;
            [Tooltip("가운데 사슬 감쇠비(1 = 출렁임 없음 — 붓끝이 포인터에 고정되므로 출렁이면 자루가 흔들린다).")]
            [Range(.2f, 2f)] public float CentreDamping;
            [Tooltip("휨이 끝 쪽으로 몰리는 정도(1 = 고르게, 2 = 끝이 더 휜다).")]
            [Range(.5f, 4f)] public float TipBias;

            [Header("둘레 사슬 (BristleRing_A..F_01..04)")]
            [Tooltip("둘레 사슬 스프링 주파수(Hz).")]
            [Min(.1f)] public float RingFrequency;
            [Range(.1f, 2f)] public float RingDamping;
            [Tooltip("사슬마다 뻣뻣함이 달라지는 폭(0 = 모두 같다). 가닥이 따로 놀게 한다.")]
            [Range(0, .9f)] public float RingStiffnessSpread;
            [Tooltip("둘레 사슬이 가운데보다 더 휘는 배율.")]
            [Range(.5f, 2f)] public float RingBendScale;
            [Tooltip("퍼짐 1일 때 둘레 사슬이 바깥으로 벌어지는 각(도).")]
            [Range(0, 40)] public float RingFlareDegrees;

            [Header("입력")]
            [Tooltip("가까운 화면: 이 속도(화면 높이의 절반 / 초)에서 최대로 휜다.")]
            [Min(.01f)] public float ViewSpeedForFullBend;
            [Tooltip("몸 기준 붓끝 속도(m/s)가 이 값이면 최대로 휜다(휘두르기).")]
            [Min(.01f)] public float WorldSpeedForFullBend;
            [Tooltip("가까운 화면에서 붓끝이 떠 있을 때(획 사이) 휨의 비율.")]
            [Range(0, 1)] public float HoverBendShare;
            [Tooltip("휘두를 때(닿지 않음) 휨의 비율.")]
            [Range(0, 1)] public float AirBendShare;

            [Header("눌림 · 퍼짐 (블렌드셰이프 BristlePress · BristleSplay)")]
            [Tooltip("획 시작 때 눌림이 오르는 시간(초).")]
            [Min(.005f)] public float PressRiseSeconds;
            [Min(.005f)] public float PressReleaseSeconds;
            [Tooltip("멈춰 있을 때 눌림(0..1).")]
            [Range(0, 1)] public float PressDeep;
            [Tooltip("빠르게 그을 때 눌림(0..1).")]
            [Range(0, 1)] public float PressLight;
            [Tooltip("눌림 1일 때 더해지는 휨(도) — 닿은 붓끝이 꺾이는 모양.")]
            [Range(0, 45)] public float PressBendDegrees;
            [Tooltip("속도에 따른 퍼짐(최대 휨 속도에서의 값).")]
            [Range(0, 1)] public float SplayBySpeed;
            [Range(0, 1)] public float SplayByPress;
            [Min(.1f)] public float SplayResponse;
            [Tooltip("프레임 간격이 이보다 길면 변형을 초기화한다(초).")]
            [Min(.05f)] public float ResetAfterGap;
            [Tooltip("붓을 눕혔을 때 젖은 털이 제 무게로 처지는 각(도). 세우면 0, 수평이면 이 값. 0 = 끔.")]
            [Range(0, 30)] public float GravitySagDegrees;

            public static BristleSettings Default => new BristleSettings
            {
                MaximumBendDegrees = 45f, CentreFrequency = 5.5f, CentreDamping = 1f, TipBias = 1.8f,
                RingFrequency = 4.2f, RingDamping = .55f, RingStiffnessSpread = .35f, RingBendScale = 1.2f, RingFlareDegrees = 14f,
                ViewSpeedForFullBend = 1.5f, WorldSpeedForFullBend = 9f, HoverBendShare = .2f, AirBendShare = .8f,
                PressRiseSeconds = .08f, PressReleaseSeconds = .14f, PressDeep = 1f, PressLight = .35f, PressBendDegrees = 14f,
                SplayBySpeed = .55f, SplayByPress = .35f, SplayResponse = 14f, ResetAfterGap = .5f, GravitySagDegrees = 9f
            };

            public bool IsValid =>
                Finite(MaximumBendDegrees) && Finite(CentreFrequency) && CentreFrequency > 0f && Finite(CentreDamping) && CentreDamping > 0f &&
                Finite(TipBias) && TipBias > 0f && Finite(RingFrequency) && RingFrequency > 0f && Finite(RingDamping) && RingDamping > 0f &&
                Finite(RingStiffnessSpread) && Finite(RingBendScale) && Finite(RingFlareDegrees) &&
                Finite(ViewSpeedForFullBend) && ViewSpeedForFullBend > 0f && Finite(WorldSpeedForFullBend) && WorldSpeedForFullBend > 0f &&
                Finite(HoverBendShare) && Finite(AirBendShare) && Finite(PressRiseSeconds) && PressRiseSeconds > 0f &&
                Finite(PressReleaseSeconds) && PressReleaseSeconds > 0f && Finite(PressDeep) && Finite(PressLight) && Finite(PressBendDegrees) &&
                Finite(SplayBySpeed) && Finite(SplayByPress) && Finite(SplayResponse) && SplayResponse > 0f && Finite(ResetAfterGap) && ResetAfterGap > 0f;
        }

        [Header("붓")]
        [Tooltip("큰 붓 모델(씬의 C02_WorldBrush308 · C02_NearBrush308 이 이 모델의 사본). 런타임은 읽지 않는다 — 도구와 검사용.")]
        public GameObject BrushPrefab;

        [Header("쥐기 — 오른손 뼈 로컬에서 본 GripSocket 자세 (motion2/grip.json 에서 도구가 계산)")]
        public Grip TwoHandGrip = new Grip { Rotation = Quaternion.identity };
        [Tooltip("어깨 걸치기. grip.json 은 두 손 쥐기와 같은 변환을 쓴다.")]
        public Grip CarryGrip = new Grip { Rotation = Quaternion.identity };
        [Tooltip("오른손이 쥐는 곳: GripSocket 에서 꼬리 쪽으로(m). common_m2.SHIFT. 도구가 왼손 쥠 점을 구할 때 쓴다.")]
        [Min(0)] public float RightHandBehindGripSocket = .30f;

        [Header("왼손 (GripSocketL 에 두 뼈 IK)")]
        [Tooltip("왼손 뼈 로컬에서 자루 축이 지나는 손바닥 점(도구가 오른손 쥠 점을 좌우 대칭으로 옮겨 계산).")]
        public Vector3 LeftHandHoldLocal = new Vector3(0f, .08f, .03f);
        [Tooltip("팔꿈치 힌트: 애니메이션의 팔꿈치 자리에 더하는 방향(몸 기준, 오른쪽 · 위 · 앞).")]
        public Vector3 LeftElbowBias = new Vector3(-.05f, -.08f, 0f);
        [Range(.5f, .9999f)] public float LeftArmMaximumExtension = .985f;
        [Tooltip("왼손 쥠 가중치가 따라가는 시간(초). 곡선은 0 / 1 계단이다.")]
        [Min(.01f)] public float LeftHandBlendSeconds = .10f;
        [Tooltip("그리는 동안(가까운 화면) 월드 몸의 왼손을 자루에 붙이는 정도.")]
        [Range(0, 1)] public float DrawLeftHandOnShaft = 1f;

        [Header("대기 자세 둘 (SPEC 5a)")]
        [Tooltip("마지막 전투 행동 뒤 이 시간 동안 아무 일도 없고 쫓는 적이 없으면 어깨에 걸친다(초).")]
        [Min(0)] public float RelaxAfterSeconds = 6f;
        [Min(.05f)] public float ToStanceSeconds = .35f;
        [Min(.05f)] public float ToCarrySeconds = .7f;
        [Tooltip("이 거리 안에서 플레이어를 쫓는 적이 있으면 전투 태세를 유지한다(m).")]
        [Min(0)] public float ThreatRadius = 28f;
        [Min(.05f)] public float ThreatPollSeconds = .25f;

        [Header("등에 메기 (달릴 때 · 탈것 · 앉을 때) — 사용자 요청 2026-10-13")]
        [Tooltip("전력 질주를 이만큼 이어가면 붓을 등에 멘다(초). 전투 중(전투 태세)에는 메지 않는다.")]
        [Min(0)] public float BackAfterSeconds = .25f;
        [Tooltip("질주를 멈추고 이만큼 지나면 다시 어깨에 걸친다(초).")]
        [Min(0)] public float BackReleaseSeconds = .6f;
        [Tooltip("손 ↔ 등 옮기는 시간(초).")]
        [Min(.05f)] public float BackSeconds = .35f;
        [Tooltip("등에 멨을 때 GripSocket 자리: 두 어깨 가운데에서 몸통 기준(오른쪽, 위, 앞) m.")]
        public Vector3 BackSocketOffset = new Vector3(0f, -.25f, -.17f);
        [Tooltip("등에 멨을 때 자루 방향(꼬리 → 붓머리), 몸통 기준(오른쪽, 위, 앞). 오른 어깨 위로 붓머리가 올라온다.")]
        public Vector3 BackShaftDirection = new Vector3(.45f, .85f, -.05f);

        [Header("애니메이터 (AC_PlaytestLocomotion_Brush308)")]
        [Tooltip("윗몸 층(허리 위 마스크): 대기 자세 둘 + 모든 동작.")]
        public string Layer = "Brush308";
        [Tooltip("온몸 층: 서 있을 때만 휘두르기 동작의 하체까지 덮는다. 비우면 쓰지 않는다.")]
        public string BodyLayer = "Brush308Body";
        [Tooltip("한 번 재생하는 동작(휘두르기 · 막기 들기 · 버프)의 정규화 시각 매개변수.")]
        public string TimeParameter = "Brush308Time";
        [Tooltip("자세 전환(ToStance · ToCarry)의 정규화 시각 매개변수.")]
        public string ShiftParameter = "Brush308Shift";
        public string StanceIdle = "StanceIdle", CarryIdle = "CarryIdle", ToStance = "ToStance", ToCarry = "ToCarry";
        [Tooltip("SpellSwingProfile308 의 상태 이름을 이 층의 상태 이름으로 바꾼다.")]
        public Alias[] Aliases = { new Alias { From = "SwingPlant", To = "GuardIn" }, new Alias { From = "SwingDraw", To = "BuffDraw" } };
        public StateEntry[] States = Array.Empty<StateEntry>();

        [Header("층 가중치 · 전환")]
        [Min(.01f)] public float SwingFadeIn = .06f;
        [Min(.01f)] public float SwingFadeOut = .20f;
        [Tooltip("불발 · 회피 · 다시 쓰기로 동작을 끊을 때.")]
        [Min(.01f)] public float CutFade = .08f;
        [Min(.01f)] public float IdleFade = .20f;
        [Min(.01f)] public float LayerFade = .12f;
        [Min(.01f)] public float BodyFade = .12f;
        [Tooltip("이 속도(m/s)보다 느릴 때만 온몸 층을 켠다(움직이면 하체는 이동 층).")]
        [Min(0)] public float StandingSpeed = .35f;
        [Tooltip("정점 뒤 클립 재생 속도.")]
        [Min(.1f)] public float PostApexSpeed = 1f;
        [Tooltip("그리는 동안 윗몸 층 가중치. 0 = 그리기 분기의 팔 풀이가 예전과 같은 자세(이동 층)에서 시작한다.")]
        [Range(0, 1)] public float DrawLayerWeight = 1f;   // 2026-10-13: the close-up copies the fingers from this pose (the clip grip), so the stance stays on in the draw mode

        [Header("붓털")]
        public BristleSettings Bristles = BristleSettings.Default;

        [Header("가까운 화면 (그리기) — 옛 작은 붓 값과 따로 둔다")]
        [Tooltip("가까운 화면의 붓을 쥐는 곳: GripSocket 에서 붓끝 쪽으로(m). 1.55 m 붓을 0.95 m 뒤에서 쥐면 손이 화면 밖이다.")]
        public float NearGripAlongShaft = -.28f;   // 2026-10-13 Play: .40 put the shaft across the camera; behind GripSocket the brush points away like a polearm
        [Tooltip("켜면 아래 두 값이 그리기 쥐기(쌍구 쥐기 값)를 대신한다. 끄면 제스처 프로필 값 그대로.")]
        public bool NearGripOverride;
        public Vector3 NearGripPosition;
        public Quaternion NearGripRotation = Quaternion.identity;
        [Tooltip("가까운 화면을 양손 쥐기로(사용자 2026-10-13): 왼손이 앞에서 축(당구대 받침), 오른손이 꼬리를 쥐고 움직인다. 끄면 옛 한 팔 풀이.")]
        public bool NearTwoHand = true;
        [Tooltip("포인터가 화면 가운데일 때 오른손이 쥐는 자리(카메라 기준 오른쪽, 위, 앞 m).")]
        public Vector3 NearRightRest = new Vector3(.24f, -.36f, .22f);
        [Tooltip("왼손 받침 자리: 붓끝에서 자루를 따라(m, 가운데 자세 기준). 이 점은 화면에서 움직이지 않는다.")]
        [Min(.2f)] public float NearPivotFromTip = .80f;
        [Tooltip("몸을 돌린 각(도, +면 왼 어깨가 앞).")]
        [Range(-90, 90)] public float NearBodyYawDegrees = 50f;
        [Tooltip("오른손이 자루와 이루는 각(도). 손목이 자루 아래에서 올라와 엄지가 위에 놓인다.")]
        [Range(0, 90)] public float NearRightTiltDegrees = 60f;
        [Tooltip("받치는 왼손 손가락이 붓머리 쪽으로 기운 각(도).")]
        [Range(0, 60)] public float NearLeftTowardTipDegrees = 20f;
        [Tooltip("손목에서 자루까지: x = 손가락 방향 길이, y = 손바닥 두께(m).")]
        public Vector2 NearPalm = new Vector2(.085f, .035f);
        [Tooltip("오른 손목에서 오른 어깨까지(카메라 기준). 팔뚝이 화면 아래에서 올라오게 한다.")]
        public Vector3 NearShoulderFromHand = new Vector3(.08f, -.12f, -.42f);
        [Tooltip("왼손의 쥐기(GripSocketL 을 왼손 뼈 기준으로). brush_truth.json 에서 계산한다(3인칭용).")]
        public Grip LeftTwoHandGrip = new Grip { Rotation = Quaternion.identity };
        [Min(.05f)] public float NearTipDepth = 1.3f;
        [Min(.05f)] public float NearMinimumDepth = .7f;
        [Min(.05f)] public float NearMaximumDepth = 1.6f;
        public Vector3 NearShoulderOffset = new Vector3(.23f, -.29f, .15f);
        [Tooltip("가까운 화면의 호흡 흔들림: 어깨가 움직이는 폭(m). 붓끝은 그대로 포인터 위에 있고 자루 쪽이 숨결처럼 흔들린다. 0 = 끔.")]
        [Range(0, .05f)] public float NearBreathAmplitude = .014f;
        [Min(.01f)] public float NearBreathHz = .27f;
        [Min(0)] public float NearRaiseSeconds = .28f;
        public Vector3 NearRaiseOffset = new Vector3(.06f, -.42f, -.12f);

        public bool IsValid =>
            TwoHandGrip.IsValid && CarryGrip.IsValid && Finite(LeftHandHoldLocal) && Finite(LeftElbowBias) && Finite(LeftArmMaximumExtension) &&
            Finite(RelaxAfterSeconds) && RelaxAfterSeconds >= 0f && Finite(ToStanceSeconds) && ToStanceSeconds > 0f &&
            Finite(ToCarrySeconds) && ToCarrySeconds > 0f && Finite(ThreatRadius) && Finite(ThreatPollSeconds) && ThreatPollSeconds > 0f &&
            !string.IsNullOrEmpty(Layer) && !string.IsNullOrEmpty(StanceIdle) && !string.IsNullOrEmpty(CarryIdle) &&
            Finite(SwingFadeIn) && Finite(SwingFadeOut) && Finite(CutFade) && Finite(IdleFade) && Finite(LayerFade) && Finite(BodyFade) &&
            Finite(StandingSpeed) && Finite(PostApexSpeed) && PostApexSpeed > 0f && Finite(LeftHandBlendSeconds) && LeftHandBlendSeconds > 0f &&
            Bristles.IsValid && States != null &&
            Finite(NearGripAlongShaft) && Finite(NearGripPosition) && Finite(NearTipDepth) && Finite(NearMinimumDepth) && Finite(NearMaximumDepth) &&
            Finite(NearShoulderOffset) && Finite(NearRaiseSeconds) && Finite(NearRaiseOffset);

        /// <summary>The layer's state for a swing profile state name (alias applied). Null or empty stays as it is.</summary>
        public string Resolve(string state)
        {
            if (string.IsNullOrEmpty(state) || Aliases == null) return state;
            for (int i = 0; i < Aliases.Length; i++) if (Aliases[i].From == state && !string.IsNullOrEmpty(Aliases[i].To)) return Aliases[i].To;
            return state;
        }

        /// <summary>Index into States, or -1.</summary>
        public int Find(string state)
        {
            if (string.IsNullOrEmpty(state) || States == null) return -1;
            for (int i = 0; i < States.Length; i++) if (States[i].State == state) return i;
            return -1;
        }

        /// <summary>The left-hand step curve of a state at a clip time (0 when the state has no keys).</summary>
        public static float LeftHandAt(in StateEntry entry, float clipSeconds)
        {
            var keys = entry.LeftHand;
            if (keys == null || keys.Length == 0) return 0f;
            float value = keys[0].y;
            for (int i = 0; i < keys.Length && keys[i].x <= clipSeconds; i++) value = keys[i].y;
            return value >= .5f ? 1f : 0f;
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    }
}
