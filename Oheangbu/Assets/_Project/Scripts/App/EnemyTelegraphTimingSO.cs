using UnityEngine;

namespace Oheangbu.App
{
    // #306 속성 기관 예고·방어 성공 먹 획의 수치(SPEC-PLAYTEST-306 #12, D306) — 전부 TEST. 표현 전용: 판정 창·그로기·피해는
    // CombatConfigSO가 그대로 소유한다(여기 값을 바꿔도 ParryJudge 결과는 불변).
    // 시계 = scaled(ParryJudge와 같음): t_peak = t_impact − PeakLead / t_on = max(t0, t_peak − (Reaction + Draw × DrawSlow)).
    // #308(SPEC-TELEGRAPH-ORGAN-308, D308-4): 예고 모양 = 모델 기관 표면 덧칠(Oheangbu/InkOrganSurface). 쿼드 먹 테·볼트 먹 테·끈 필드는
    // LEGACY(읽지 않음, 되돌림용으로 남김 — 직렬화 이름 불변). D308-4d: 결정 둘레 마석 오염(같은 덧칠 셰이더의 오염 층, 평소에도 붙음).
    [CreateAssetMenu(menuName = "Oheangbu/Combat/Enemy Telegraph Timing (306)", fileName = "EnemyTelegraphTiming306")]
    public sealed class EnemyTelegraphTimingSO : ScriptableObject
    {
        public const string ResourcePath = "Telegraph306/EnemyTelegraphTiming306";
        public const string SurfaceShaderName = "Oheangbu/InkOrganSurface";

        [Header("예고 시점 [TEST]")]
        [Tooltip("절정 = 충돌 예측 − 이 값. 0.45 = 패링 창 0.9의 절반(창 한가운데)")]
        [Min(0f)] public float PeakLead = .45f;
        [Min(0f)] public float ReactionSeconds = .25f;
        [Tooltip("작도 시간 × 작도 감속 — 오름 시작을 절정 앞으로 당긴다")]
        [Min(0f)] public float DrawSeconds = 1f;
        [Range(0f, 1f)] public float DrawSlowFactor = .35f;
        [Tooltip("절정 맥동 — 먹 고리가 닫힌다")]
        [Min(.01f)] public float PeakPulseSeconds = .12f;
        [Tooltip("충돌 뒤 먹으로 가라앉음")]
        [Min(.01f)] public float FadeAfterImpact = .25f;
        [Tooltip("취소·끊김(목으로 끊은 남문 등) — 먹으로 꺼짐")]
        [Min(.01f)] public float CancelFadeSeconds = .2f;

        [Header("기관 표면 [TEST] — #308 덧칠, LDR ≤ MaxBrightness(블룸 문턱 아래, ART-INK 상한)")]
        [Tooltip("덧칠 재질(Oheangbu/InkOrganSurface) — 비면 셰이더 이름으로 런타임 사본. 렌더러마다 사본 하나")]
        public Material SurfaceMaterial;
        [Range(0f, 1f)] public float MaxBrightness = .85f;
        [Range(0f, 1f)] public float PaperBrightness = .78f;
        [Tooltip("고리 반지름 = 기관 Radius × 이 값")]
        [Range(.2f, 2f)] public float RingScale = 1f;
        [Tooltip("고리 폭(d = 기관 중심 거리 ÷ 반지름 기준)")]
        [Range(.02f, .6f)] public float RingWidth = .18f;
        [Tooltip("영역 가장자리 흐림(d 기준)")]
        [Range(.01f, .6f)] public float Feather = .12f;
        [Tooltip("마스크 하한(Mask 모드)")]
        [Range(0f, 1f)] public float MaskThreshold = .1f;
        [Tooltip("보이는 기관: 바깥 법선 · 카메라 방향 ≥ 이 값")]
        [Range(-1f, 1f)] public float VisibleDot = -.1f;
        [Tooltip("다시 고르기 여유 — 켜진 기관 내적이 VisibleDot − 이 값 아래로 VisibleReselectSeconds 넘게 머물 때만")]
        [Range(0f, 1f)] public float VisibleHysteresis = .2f;
        [Min(0f)] public float VisibleReselectSeconds = .25f;
        [Tooltip("등줄기 사슬에서 한 마디가 빛나는 폭(진행 0..1 기준)")]
        [Min(.01f)] public float ChainWidth = .35f;
        [Tooltip("깊이 바이어스(셰이더 Offset 단위, 음수 = 카메라 쪽)")]
        public float ZBias = -1f;

        [Header("마석 오염 [TEST] — #308 D308-4d, 몸 표면 덧칠(평소 무채 = 팔레트 오염 먹, 예고 창 안에서만 속성색 LDR)")]
        [Tooltip("결정 바로 둘레(마스크 값 1)의 어둡힘 — 몸 색 × (1 − 이 값)")]
        [Range(0f, 1f)] public float ContaminationDarkNear = .72f;
        [Tooltip("오염 가장자리(마스크 값 0 근처)의 어둡힘")]
        [Range(0f, 1f)] public float ContaminationDarkFar = .30f;
        [Tooltip("어둡힌 자리에 더하는 오염 먹(검보라·묵색)의 비율 — 0 = 순수 어둡힘")]
        [Range(0f, 1f)] public float ContaminationInkMix = .35f;
        [Tooltip("예고 창 안 속성색이 결 위에 서는 불투명도(LDR ≤ MaxBrightness는 그대로)")]
        [Range(0f, 1f)] public float ContaminationTintAlpha = .75f;
        [Tooltip("번짐 앞머리 흐림(측지 거리 비 기준)")]
        [Range(.01f, .5f)] public float ContaminationFlowSoftness = .15f;
        [Tooltip("절정(t_peak)에 번짐 앞머리가 닿는 거리(측지 거리 비) — 1 = 오염 끝까지")]
        [Range(.1f, 1.5f)] public float ContaminationFlowReach = 1.05f;
        [Tooltip("이 거리(m) 안의 적만 오염 덧칠을 붙인다(그리기 1회 추가) — 밖으로 나가면 뗀다")]
        [Min(1f)] public float ContaminationAttachDistance = 60f;
        [Tooltip("떼는 거리 = 붙이는 거리 + 이 값(경계에서 붙였다 뗐다 반복 방지)")]
        [Min(0f)] public float ContaminationDetachMargin = 6f;
        [Tooltip("이 거리(m)부터 평소 어둡힘이 풀리기 시작해 붙이는 거리에서 0이 된다 — 붙고 떨어질 때 튀지 않는다. 붙이는 거리 이상이면 풀림 없음")]
        [Min(0f)] public float ContaminationFadeStartDistance = 36f;
        [Tooltip("오염 마스크를 읽는 밉 단계 상한 — 몸 UV 아틀라스의 이웃 섬(몸의 다른 곳)으로 번지는 것을 막는다(2 = 깨끗한 살의 0.5 % 이하)")]
        [Range(0f, 6f)] public float ContaminationMaxMip = 2f;

        [Header("방어 성공 먹 획 [TEST]")]
        [Min(.01f)] public float StrokeFlightSeconds = .2f;
        [Tooltip("시작 폭(m) — 기관 쪽 끝은 StrokeEndWidth배")]
        [Min(.001f)] public float StrokeWidth = .09f;
        [Range(.05f, 1f)] public float StrokeEndWidth = .35f;
        [Min(1f)] public float BlossomWidthScale = 1.5f;
        [Range(.05f, 1f)] public float HalfWidthScale = .45f;
        [Tooltip("반만 성공 — 획 머리가 이 비율까지 가서 녹는다")]
        [Range(.1f, 1f)] public float HalfReach = .5f;
        [Range(0f, 1f)] public float TailLength = .65f;
        [Min(.01f)] public float TailMeltSeconds = .18f;
        [Tooltip("획의 휨(경로 길이 비)")]
        [Range(0f, .5f)] public float StrokeArc = .12f;
        [Tooltip("먹 → 방어 글자 속성색 물듦(LDR 틴트만, HDR 가산 0)")]
        [Range(0f, 1f)] public float StrokeTint = .85f;
        [Tooltip("도착 뒤 기관 빛이 먹 얼룩으로 꺼지는 시간")]
        [Min(.01f)] public float ExtinguishSeconds = .35f;
        [Tooltip("기관이 없을 때 가슴 뼈에서 카메라 쪽으로 획 끝을 띄우는 거리(m)")]
        [Min(0f)] public float FallbackChestLift = .22f;
        [Range(1, 8)] public int MaxLiveStrokes = 3;
        [Tooltip("히트스톱 중에도 실시간의 이 비율 이상으로 진행(정지 메뉴 timeScale 0은 멈춤)")]
        [Range(0f, 1f)] public float RealtimeFloor = .5f;
        [Tooltip("실시간 상한 — 이 시간(실초) 안에 반드시 도착·해제된다")]
        [Min(.05f)] public float RealtimeCap = .6f;

        [Header("플레이어 쪽 [TEST]")]
        [Range(0f, 2f)] public float PlayerParryBurstScale = .6f;

        [Header("공정성 검사 [TEST] — 준비 + 최소 비행 ≥ 이 값")]
        [Min(0f)] public float FairnessMinimum = 1.05f;
        [Tooltip("투사체 최소 비행을 재는 가까운 거리(m)")]
        [Min(.1f)] public float FairnessNearDistance = 3f;

        [Header("재질(표현) — 비우면 셰이더 이름으로 런타임 사본")]
        public Material StrokeMaterial;

        [Header("LEGACY — #306 쿼드 먹 테·볼트 먹 테·끈(#308에서 읽지 않음, PASS 뒤 정리)")]
        [Tooltip("LEGACY: 쿼드 지름 = 기관 Radius × 2 × 이 값")]
        [Min(.1f)] public float HaloScale = 2.4f;
        [Tooltip("LEGACY: 몸 안 뼈에서 카메라 쪽으로 (Radius + 이 값) 띄운 쿼드")]
        [Min(0f)] public float SurfaceOffset = .08f;
        [Tooltip("LEGACY: 볼트·파동머리의 먹 테 반지름(m)")]
        [Min(.01f)] public float ProjectileHaloRadius = .16f;
        [Tooltip("LEGACY: 기관→볼트 끈 폭(m)")]
        [Min(0f)] public float ThreadWidth = .035f;
        [Tooltip("LEGACY: 도착 터짐 — 쿼드가 이 비율만큼 부풀었다(#308 덧칠 터짐은 기관 구 안에서 먹 점으로)")]
        [Min(0f)] public float OrganBurstScale = .8f;
        [Tooltip("LEGACY: InkOrganHalo 쿼드 재질")]
        public Material HaloMaterial;

        public float RiseLead => ReactionSeconds + DrawSeconds * DrawSlowFactor;
    }
}
