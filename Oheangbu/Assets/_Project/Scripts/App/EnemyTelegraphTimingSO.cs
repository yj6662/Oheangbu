using UnityEngine;

namespace Oheangbu.App
{
    // #306 속성 기관 예고·방어 성공 먹 획의 수치(SPEC-PLAYTEST-306 #12, D306) — 전부 TEST. 표현 전용: 판정 창·그로기·피해는
    // CombatConfigSO가 그대로 소유한다(여기 값을 바꿔도 ParryJudge 결과는 불변).
    // 시계 = scaled(ParryJudge와 같음): t_peak = t_impact − PeakLead / t_on = max(t0, t_peak − (Reaction + Draw × DrawSlow)).
    [CreateAssetMenu(menuName = "Oheangbu/Combat/Enemy Telegraph Timing (306)", fileName = "EnemyTelegraphTiming306")]
    public sealed class EnemyTelegraphTimingSO : ScriptableObject
    {
        public const string ResourcePath = "Telegraph306/EnemyTelegraphTiming306";

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

        [Header("기관 먹 테 [TEST] — LDR ≤ 1(블룸 문턱 아래, ART-INK 상한)")]
        [Range(0f, 1f)] public float MaxBrightness = .85f;
        [Range(0f, 1f)] public float PaperBrightness = .78f;
        [Tooltip("쿼드 지름 = 기관 Radius × 2 × 이 값")]
        [Min(.1f)] public float HaloScale = 2.4f;
        [Tooltip("몸 안 뼈에서 카메라 쪽으로 (Radius + 이 값) 띄운다 — ZTest는 그대로(벽 너머로 보이지 않게)")]
        [Min(0f)] public float SurfaceOffset = .08f;
        [Tooltip("등줄기 사슬에서 한 마디가 빛나는 폭(진행 0..1 기준)")]
        [Min(.01f)] public float ChainWidth = .35f;
        [Tooltip("볼트·파동머리의 같은 색 먹 테 반지름(m)")]
        [Min(.01f)] public float ProjectileHaloRadius = .16f;
        [Tooltip("기관→볼트 끈 폭(m)")]
        [Min(0f)] public float ThreadWidth = .035f;

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
        [Tooltip("도착 터짐 — 먹 테가 이 비율만큼 부풀며 흩어진다")]
        [Min(0f)] public float OrganBurstScale = .8f;
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
        public Material HaloMaterial;
        public Material StrokeMaterial;

        public float RiseLead => ReactionSeconds + DrawSeconds * DrawSlowFactor;
    }
}
