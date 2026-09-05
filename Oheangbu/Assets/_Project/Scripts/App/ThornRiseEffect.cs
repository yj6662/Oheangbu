using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // [SPEC-SPELL-FX-REWORK §3.4 — 고(木 광역) 「곧게 뻗는 생목 가시」] 재작업(감사 원인 ①~⑥ 교정).
    // 문법: 흙이 먼저 터지고(_soilLead 선행 — 「땅이 갈라진다」의 인과) → 덩굴 가닥이 첨단부터 자라며(InkBody _Grow 성장 전선)
    //       눕힘(_emergeTilt)에서 직립을 살짝 지나 되튀고(EaseOutBack) → 절정=커밋+plan.Delay(=판정 시각) →
    //       감쇠 흔들림(셰이더 sway) → 첨단부터 되감기며 먹으로 가라앉음(_Grow 1→0·_Value↓) → 잔존 0(곳·곡 어휘 침범 없음).
    // 실체=세장 덩굴 단품 메시(뿌리 피벗·첨단 +Z·z∈[0,1] — FX-ASSETS §4.2-6) 12본 해바라기 배치, 재질=Oheangbu/InkBody.
    // 지면 앵커: 본별 하향 레이캐스트(대상·자기·Vitals·CharacterController 계층 제외) — 대상 캡슐 중심 y(1.1)를 지면으로
    // 쓰던 부유 버그(감사 원인 ⑤) 교정. 흙·먼지·먹 방울은 코드 구성 파티클(FlameJet.BuildJet 선례 — 프리팹 짜깁기 없음).
    // 판정 무접촉: AreaImpactPlan(Point·Radius·Delay)은 읽기만 — 연출≠실판정(FX-ASSETS §9-1·AREA-SHAPES §3).
    // 무리깅(§4.2-5): 움직임=트랜스폼(회전·균일 스케일)+셰이더 정점 오프셋+파티클뿐.
    public sealed class ThornRiseEffect : SpellSequenceEffect
    {
        [Header("자원 — 덩굴 메시(첨단 +Z·뿌리 z=0 — §4.2-6)·재질(InkBody)·흙 파티클 재질(URP Particles/Unlit Transparent)")]
        [SerializeField] private Mesh[] _vineMeshes = new Mesh[0]; // [0]=주역 A(go2), [1..]=변형 B(go3) — 하나뿐이면 A만
        [SerializeField] private Material _material;               // M_SpellBodyThorn — 인스턴스 1장 소유
        [SerializeField] private Material _soilMaterial;           // M_SpellSoil — 비우면 파티클 생략(URP 기본 파티클 재질 폴백 불신)
        [SerializeField] private Mesh _clodMesh;                   // 선택(룩 게이트 후) — 메시 흙덩이. null=파티클 흙만

        [Header("시계 [TEST §7] — 절정=커밋+plan.Delay(계획=정본, 직렬값=폴백 — §8 예외 2)")]
        [SerializeField, Min(0.05f)] private float _growTime = 0.20f;    // 폴백 — 계획 시 plan.Delay−_spawnStagger
        [SerializeField, Min(0.05f)] private float _growTimeMin = 0.12f; // 계획 Delay가 짧아도 이 아래로는 줄이지 않는다(절정이 판정보다 늦어짐 — 로그로 드러남)
        [SerializeField, Min(0f)] private float _spawnStagger = 0.10f;    // 중심→외곽 시간차(거리 비례) — 최외곽 본 완주=Delay
        [SerializeField, Min(0f)] private float _holdTime = 0.45f;        // 직립 홀드 — 감쇠 흔들림 구간
        [SerializeField, Min(0.05f)] private float _witherTime = 0.35f;  // 시듦 — 첨단부터 되감김
        [SerializeField, Min(0f)] private float _settleTail = 0.5f;       // 전 본 소멸 후 파티클 여운
        [SerializeField, Min(0f)] private float _soilLead = 0.04f;        // 흙 터짐이 본보다 먼저

        [Header("배치 [TEST] — 예준 2026-09-05: 「사방에서 올라와 서로 얽히고 겹친다」. 둘레 링 발아 → 안쪽·접선 소용돌이")]
        [SerializeField, Min(1)] private int _count = 18;                        // 본 수(「덩굴 수 더 많이」)
        [SerializeField] private Vector2 _segRange = new Vector2(3f, 4f);        // 본당 마디 수. 낮출수록 같은 예산에 본이 늘고 마디가 길어져(=균일 스케일이라) 굵어진다
        [SerializeField, Min(4)] private int _maxSegments = 60;                  // 총 마디 상한 = 폴리 잠금(소진 시 본 생성 중단)
        [SerializeField, Min(0.2f)] private float _runLength = 2.0f;             // 본 수평 길이(m) ×(0.85~1.15)
        [SerializeField] private Vector2 _ringRange = new Vector2(0.62f, 0.88f); // 발아 링 = plan.Radius × 이 구간
        [SerializeField, Min(0f)] private float _ringYawJitter = 18f;            // 링 각 지터(도) — 「완전한 원」 회피
        [SerializeField, Min(0f)] private float _inwardSpread = 25f;             // 초기 헤딩 지터(도)
        [SerializeField, Range(0.1f, 1f)] private float _swirlStart = 0.75f;     // 이 반경 안쪽부터 접선으로 감긴다
        [SerializeField, Range(0f, 1f)] private float _swirlMax = 0.75f;         // 접선 블렌드 상한(휘감음의 세기)
        [SerializeField, Min(0f)] private float _startSpread = 0.45f;            // 본별 발아 지연 산포 — 「한번에 말고 산발적으로」

        [Header("지면 기복 [TEST] — 「바닥을 뚫기도 하고 위로 솟기도」. 관절 높이 = 지면고 + 사인 기복")]
        [SerializeField] private Vector2 _weaveAmp = new Vector2(0.16f, 0.26f);      // 수직 진폭(m)
        [SerializeField] private Vector2 _weaveLambda = new Vector2(1.15f, 1.55f);   // 수직 파장(m)
        [SerializeField, Range(0f, 1f)] private float _weaveBias = 0.35f;            // 하향 편의 — 대체로 흙 속, 가끔 등만 내민다
        [SerializeField] private Vector2 _meanderYaw = new Vector2(8f, 18f);         // 수평 사행 진폭(도)
        [SerializeField] private Vector2 _meanderLambda = new Vector2(0.8f, 1.4f);   // 수평 사행 파장(m)
        [SerializeField, Range(0f, 0.3f)] private float _jointOverlap = 0.07f;       // 마디 겹침 — 관절 틈 은폐
        [SerializeField, Range(1f, 6f)] private float _slowFactor = 2.9f;            // 절정 이후 성장 감속 배수(「천천히 휘감는다」)
        [SerializeField, Range(1, 4)] private int _climaxSegs = 2;                   // plan.Delay에 솟아 있는 마디 수(=판정 절정)
        [SerializeField, Min(0.05f)] private float _writheFreq = 1.1f;               // 꿈틀 주파수(Hz) — 「더 느리게」(셰이더 기본 3.5)
        [SerializeField, Min(0.1f)] private float _areaRadius = 1.6f;            // 폴백 산개 반경(계획 없을 때)
        [SerializeField, Range(0.1f, 1f)] private float _spreadFraction = 0.64f; // 계획: plan.Radius×(2.5→1.6 — AREA-SHAPES §4 「반경=산개+적 반경」)
        [SerializeField, Min(0f)] private float _placeJitter = 0.15f;            // 반경 지터(m)
        [SerializeField, Min(0f)] private float _yawJitter = 15f;                // 황금각 지터(도)
        [SerializeField, Min(0)] private int _primaryMeshCount = 7;              // A 본 수 — 나머지는 B 순환(7:5), 고르게 섞는다

        [Header("줄기 [TEST] — 크기·자세")]
        [SerializeField, Min(0.1f)] private float _vineScale = 1.4f;             // 메시 z 1.0 → 높이(m)
        [SerializeField] private Vector2 _scaleJitter = new Vector2(0.7f, 1.2f); // 대소 혼합 → 0.98~1.68m
        [SerializeField, Min(0.1f)] private float _secondaryScaleMax = 1.1f;     // B(나선) 본 지터 상한 — 관통 완화
        [SerializeField, Range(0f, 1f)] private float _emergeScale = 0.6f;       // 솟는 순간 균일 스케일 시작 비율
        [SerializeField, Range(0f, 89f)] private float _emergeTilt = 55f;        // 솟는 순간 바깥 눕힘(도)
        [SerializeField, Range(0f, 45f)] private float _settleTiltMax = 12f;     // 정착 기울기 상한(본별 무작위)
        [Header("눕힘·꿈틀 [TEST] — 예준 2026-09-04: 「솟은 뒤 바닥으로 누워 뻗으며 휘감고 꿈틀거린다」")]
        [SerializeField, Range(0f, 89f)] private float _layTilt = 78f;           // 최종 눕힘(도) — 90=완전히 지면과 평행
        [SerializeField, Range(0f, 89f)] private float _settleTiltMin = 74f;     // 정착 기울기 하한 — 상한과 함께 좁히면 「솟음」이 사라진다(예준 2026-09-04)
        [SerializeField, Min(0f)] private float _serpAmp = 0.16f;                // 사행 폭 — 긴 덩굴이 좌우로 구불거린다
        [SerializeField, Min(0f)] private float _serpFreq = 1.8f;                // 줄기 하나에 실리는 물결 수
        [Header("새싹 [TEST] — 예준 2026-09-05: 「50%부터 뿌리에서 초록 풀과 세부 가지가 자란다」")]
        [SerializeField, Range(1, 4)] private int _sproutSegsPerRun = 2;         // 본당 새싹 마디 수(폴리 손잡이)
        [SerializeField, Range(0, 12)] private int _sproutSites = 6;             // 마디 하나에 나는 부착점 수
        [SerializeField, Range(0, 8)] private int _grassPerSite = 4;             // 부착점당 풀날(1tri) — 초록
        [SerializeField, Range(0, 3)] private int _twigPerSite = 1;              // 부착점당 잔가지(Y자 3tri) — 목질 갈색
        [SerializeField] private Vector2 _sproutZ = new Vector2(0.12f, 0.86f);   // 부착점 z 구간(성장 창을 피한다)
        [SerializeField] private Vector2 _grassLen = new Vector2(0.16f, 0.30f);  // 풀 길이(오브젝트 — z 1.0=마디 길이)
        [SerializeField, Min(0.002f)] private float _grassWidth = 0.030f;        // 풀 밑동 반폭
        [SerializeField] private Vector2 _twigLen = new Vector2(0.20f, 0.34f);   // 잔가지 길이
        [SerializeField, Min(0.002f)] private float _twigWidth = 0.018f;
        [SerializeField, Range(0.05f, 1f)] private float _girthMin = 0.28f;      // 발아 순간 굵기 배수(「얇은 뿌리」)
        [SerializeField, Range(0.1f, 1.5f)] private float _girthTip = 0.78f;     // 체인 끝 가늘어짐
        [SerializeField, Range(0f, 1f)] private float _greenMax = 0.85f;         // 줄기 녹화 상한
        [SerializeField, Range(0f, 1f)] private float _greenFrom = 0.5f;         // 녹화가 시작되는 성숙도
        [SerializeField, Range(0f, 1f)] private float _sproutStart = 0.5f;       // 새싹이 트는 성숙도 — 예준 「50%」

        [Header("마디 메시 실측표 [TEST] — Blender 실측(런 로그). 배열 인덱스 = _vineMeshes 인덱스")]
        [SerializeField] private Vector4[] _meshSpine = new Vector4[0];          // xy=z0 단면 중심 · zw=z1 단면 중심
        [SerializeField] private Vector2[] _meshHalf = new Vector2[0];           // 단면 반폭(x, y)

        [Header("잎 [TEST] — 참고 이미지: 갈색 덩굴 매트 위 청록 잎. 줄기 메시에 구워 넣어 셰이더 변형을 함께 받는다")]
        [SerializeField, Range(0, 24)] private int _leafCount = 9;               // 줄기 하나에 붙는 잎 수
        [SerializeField, Min(0f)] private float _leafSize = 0.17f;               // 잎 길이(오브젝트 단위 — z 1.0=줄기 길이)
        [SerializeField] private Vector2 _leafSpan = new Vector2(0.15f, 0.94f);  // 잎이 붙는 z 구간
        [SerializeField, Min(0f)] private float _leafLift = 0.012f;              // 줄기 축에서 띄우는 높이(z-파이팅 회피)
        [SerializeField, Min(0.01f)] private float _layTime = 0.35f;             // 눕는 데 걸리는 시간(홀드 안)
        [SerializeField, Min(0f)] private float _writheMul = 1f;                 // 누운 동안 꿈틀 진폭 배수(감쇠 없음 — 살아 움직인다)
        [SerializeField, Min(0f)] private float _curlAmp = 0.26f;                // 감김 폭(오브젝트 단위) — 曲直의 曲. 0.10은 14cm라 육안 한계(컷 판독)
        [SerializeField, Min(0f)] private float _waveK = 4f;                     // 꿈틀 파수 — 0=제자리 스윙
        [SerializeField, Range(0f, 45f)] private float _droopTilt = 15f;         // 시듦 처짐(도)
        [SerializeField, Min(0f)] private float _rootSink = 0.05f;               // 뿌리를 지면 아래로(m)
        [SerializeField, Min(0f)] private float _sinkDrop = 0.15f;               // 시듦 침강(m)
        [SerializeField, Range(0f, 1f)] private float _sinkValue = 0.55f;        // 먹 침전 명도(InkBody _Value 종점)
        [SerializeField, Range(0.05f, 1f)] private float _inkSinkPortion = 0.6f; // 시듦 구간 중 먹 침전이 완료되는 비율
        [SerializeField, Range(0f, 1f)] private float _slopeAlign = 0.5f;        // 지면 법선 정렬 비율(0=수직 1=법선)

        [Header("흔들림 [TEST] — 셰이더 sway(감쇠 탄성)")]
        [SerializeField, Min(0f)] private float _swayAmp = 0.025f;               // 첨단 진폭(오브젝트 단위)
        [SerializeField, Min(0.01f)] private float _swayDecay = 0.35f;           // 지수 감쇠 시정수(s)
        [SerializeField, Min(0f)] private float _riseSwayMul = 1.6f;             // 성장 중 진폭 배수 — 자라는 힘으로 휘두른다(0=성장 중 정지)

        [Header("파티클 [TEST] — 흙·먼지·먹 방울(코드 구성, 먹색·저알파·무발광)")]
        [SerializeField, Min(0)] private int _soilClods = 12;
        [SerializeField] private Vector2 _soilUpSpeed = new Vector2(2.2f, 3.6f);
        [SerializeField] private Vector2 _soilSideSpeed = new Vector2(0.4f, 1.0f);
        [SerializeField] private Vector2 _soilSize = new Vector2(0.05f, 0.12f);
        [SerializeField] private Vector2 _soilLife = new Vector2(0.35f, 0.55f);
        [SerializeField] private float _soilGravity = 1.4f;
        [SerializeField, Range(0f, 1f)] private float _soilAlpha = 0.85f;
        [SerializeField, Range(0f, 1f)] private float _soilAlphaHold = 0.7f;      // 수명 중 알파 유지 비율 — 이후 소멸 페이드
        [SerializeField] private Vector2 _soilSizeCurve = new Vector2(1f, 0.6f); // 수명 시작→끝 크기 배율
        [SerializeField, Min(0)] private int _dustPuffs = 6;
        [SerializeField] private float _dustUpSpeed = 0.25f;
        [SerializeField] private float _dustSideSpeed = 0.6f;
        [SerializeField] private Vector2 _dustSize = new Vector2(0.45f, 0.8f);
        [SerializeField] private Vector2 _dustLife = new Vector2(0.45f, 0.65f);
        [SerializeField] private float _dustGravity = -0.05f;
        [SerializeField, Range(0f, 1f)] private float _dustAlpha = 0.28f;
        [SerializeField] private Vector2 _dustSizeCurve = new Vector2(0.6f, 1.4f);
        [SerializeField, Range(0f, 1f)] private float _dustTintMix = 0.35f;       // 먹↔틴트 혼합(채도 하강)
        [SerializeField, Min(0)] private int _dripCount = 3;                      // 시듦 진입 순간 첨단 먹 방울
        [SerializeField] private float _dripSpeed = 0.5f;
        [SerializeField] private float _dripSize = 0.04f;
        [SerializeField] private float _dripLife = 0.4f;
        [SerializeField, Range(0f, 1f)] private float _dripAlpha = 0.6f;         // 전용 Drip PS — 알파 0.6→0 선형(Soil 곡선과 분리)
        [SerializeField] private float _dripGravity = 1.0f;
        [SerializeField] private Color _inkColor = new Color(0.16f, 0.15f, 0.13f, 1f); // 먹빛 — 팔레트 _fallback 복제(§8 예외 1)
        [Tooltip("목질 줄기색 — 팔레트 WoodColor 미러(§8-1 _inkColor 선례). 끝·가시는 속성 강조색(tint)이 맡는다")]
        [SerializeField] private Color _woodColor = new Color(0.541f, 0.353f, 0.200f); // #8A5A33
        [SerializeField, Min(16)] private int _maxParticles = 256;

        [Header("메시 흙덩이 [TEST] — _clodMesh 배선 시만(SpikeImpactShard 0.3s 재사용)")]
        [SerializeField, Min(0)] private int _clodVines = 6;                     // 중심 쪽 N본에만
        [SerializeField, Min(0)] private int _clodsPerVine = 2;
        [SerializeField, Min(0f)] private float _clodScale = 0.07f;
        [SerializeField, Range(0f, 1f)] private float _clodSpread = 0.5f;        // 위 방향 대비 옆으로 튀는 비율

        [Header("지면 [TEST] — 레이캐스트 앵커(대상·자기·EnemyVitals/PlayerVitals/CharacterController 계층·트리거 제외)")]
        [SerializeField, Min(0f)] private float _groundProbeUp = 3.0f;           // 탐침 시작 높이(m)
        [SerializeField, Min(0.1f)] private float _groundProbeDown = 8f;         // 탐침 하향 거리(m)
        [SerializeField] private float _groundDrop = 1.2f;                       // 콜라이더 없는 바닥 폴백: 문양(가슴 높이)−낙차

        private enum Phase { Wait, Rise, Hold, Wither, Done }

        // 마디 한 토막 — 관절 두 점이 자세를 결정한다(피치를 따로 두지 않는다)
        private sealed class Seg
        {
            public Transform Tr;
            public MeshRenderer Renderer;
            public MeshRenderer LeafRenderer;
            public MaterialPropertyBlock Mpb;
            public Vector3 Pos;
            public Quaternion Rot;
            public float Len;      // 월드 길이(=균일 스케일 배수, 메시 z 1.0 기준)
            public float Expose;   // 이 마디 뿌리 관절의 지면 대비 높이(m). 음수=흙 속 — 새싹 게이트에 쓴다
            public float Z0, Z1;   // 체인 전체에서 이 마디가 차지하는 z 구간 → 셰이더 _ChainZ
            public float Seed;
        }

        // 한 본(run) = 마디 체인. 관절 높이를 지면고+사인 기복으로 잡으면 마디 피치가 저절로 나와
        // 위를 향한 마디는 흙을 뚫고 솟고 아래를 향한 마디는 잠긴다 — 관절이 곧 구멍이다
        private sealed class Vine
        {
            public Seg[] Segments;
            public Phase Phase;
            public float PhaseStart;
            public float Delay;
            public bool SoilEmitted;
            public Vector3 GroundPos;   // 발아점(흙 파티클)
            public Vector3 UpN;
            public float Scale;         // 마디 길이 배수(=굵기 동반 — 균일 스케일이라 무리깅 조항 무저촉)
            public float Seed;
            public Mesh Mesh;
            public int MeshIndex;       // _vineMeshes 인덱스 — 실측 척추·반폭 표를 고르는 키
            public int Index;
            public float Grown;         // 전선 위치 G ∈ [0, Segments.Length]
            public float Maturity;      // 성숙도 = Grown/마디수 ∈ [0,1]. 굵기·새싹·녹화의 공통 시계(예준 「50% 시점」)
            public Mesh[] Sprouts;      // 마디별 새싹 메시(없으면 null) — OnDestroy에서 정리한다
        }

        private const float GoldenAngleDeg = 137.50776f; // 해바라기 배치의 황금각(기하 상수)
        private const int GroundHitBuffer = 16;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TipColorId = Shader.PropertyToID("_TipColor");
        private static readonly int CurlAmpId = Shader.PropertyToID("_CurlAmp");
        private static readonly int WaveKId = Shader.PropertyToID("_WaveK");
        private static readonly int SerpAmpId = Shader.PropertyToID("_SerpAmp");
        private static readonly int SerpFreqId = Shader.PropertyToID("_SerpFreq");
        private static readonly int SerpDirId = Shader.PropertyToID("_SerpDir");
        private static readonly int LeafModeId = Shader.PropertyToID("_LeafMode");
        private static readonly int ChainZId = Shader.PropertyToID("_ChainZ");
        private static readonly int MaturityId = Shader.PropertyToID("_Maturity");
        private static readonly int SpineId = Shader.PropertyToID("_Spine");
        private static readonly int GreenMixId = Shader.PropertyToID("_GreenMix");
        private static readonly int SwayFreqId = Shader.PropertyToID("_SwayFreq");
        private const float TwoPi = 6.2831853f;

        // 정점색 규약 — b가 잎 마스크(셰이더 _LeafMode). rg는 _SpineMode용이라 중립 0.5로 둔다
        private static readonly Color StemColorKey = new Color(0.5f, 0.5f, 0f, 1f);
        private static readonly Color LeafColorKey = new Color(0.5f, 0.5f, 1f, 1f);
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int GrowId = Shader.PropertyToID("_Grow");
        private static readonly int SwayAmpId = Shader.PropertyToID("_SwayAmp");
        private static readonly int SwaySeedId = Shader.PropertyToID("_SwaySeed");
        private static readonly int ValueId = Shader.PropertyToID("_Value");
        private static readonly RaycastHit[] s_groundHits = new RaycastHit[GroundHitBuffer]; // Begin 한정 — 프레임 할당 0
        private static readonly Comparison<Vine> ByDelay = (a, b) => a.Delay.CompareTo(b.Delay);

        private readonly List<Vine> _vines = new List<Vine>();
        private Material _ownedMaterial;
        private Material _ownedSoil;
        private Material _ownedClod;
        private Mesh _leafMesh;            // 런타임 생성 잎 메시 — OnDestroy에서 정리
        private Material _ownedLeafMaterial;
        private ParticleSystem _soil;
        private ParticleSystem _dust;
        private ParticleSystem _drip;
        private AreaImpactPlan _plan;
        private float _beginTime;
        private float _resolvedGrowTime;
        private float _growStride = 0.893f; // 1/(1+_GrowWindow) — 마디 첨단이 front=1.0에 닿는 _Grow 값. Begin에서 재질에서 읽는다
        private bool _running;
        private Color _tint;
#if UNITY_EDITOR
        private Transform _target;  // 검증 로그용(§6-7) — 빌드 제외
        private float _centerY;

#if UNITY_EDITOR
        // [§3.4.4 검수] 연출 반경 실측용 — 판정 중심(계획 Point). 바운즈 대각은 반경이 아니므로
        //   FlameJetAudit.ThornProbe가 이 점을 기준으로 수평 도달(reachXZ)을 잰다. 읽기 전용·판정 무접촉.
        public Vector3 AuditCenter => _plan != null ? _plan.Point : transform.position;
#endif
        private bool _climaxLogged;
#endif

        // 판정 계획 [SPELL-AREA-SHAPES §3] — 중심=Point·산개=Radius×_spreadFraction·성장=Delay−스태거(절정=판정 시각). 읽기만
        public override void SetAreaPlan(AreaImpactPlan plan)
        {
            _plan = plan;
        }

        public override void Begin(Vector3 origin, Transform target, Vector3 fallbackPoint, Color tint)
        {
            transform.SetParent(null, true);
            // 어댑터 루트 스케일(letterSize×PatternScale) 상속 차단 — 본 localScale을 매 프레임 쓰므로 필수(GroundWave 선례)
            transform.localScale = Vector3.one;
            _tint = tint;
            _beginTime = Time.time;
            _running = true;
#if UNITY_EDITOR
            _target = target;
#endif

            if (_vineMeshes == null || _vineMeshes.Length == 0 || _vineMeshes[0] == null)
            {
                Debug.LogWarning("[ThornRise] _vineMeshes 미배선 — 덩굴 생략", this); // [0]=A가 폴백 정본 — 비면 전 본 조용히 소멸하므로 경고
            }
            if (_material != null)
            {
                _ownedMaterial = new Material(_material); // 인스턴스 1장 공유 — 원본 불변, 본별 값은 MPB
                // 줄기=목질 갈색 / 끝·가시=속성 강조색 — 실제 덩굴로 읽히면서 속성 식별을 끝에서 지킨다(예준 2026-09-04)
                if (_ownedMaterial.HasProperty(BaseColorId)) _ownedMaterial.SetColor(BaseColorId, _woodColor);
                if (_ownedMaterial.HasProperty(TipColorId)) _ownedMaterial.SetColor(TipColorId, tint);
                if (_ownedMaterial.HasProperty(ColorId)) _ownedMaterial.SetColor(ColorId, _woodColor);
                if (_ownedMaterial.HasProperty(CurlAmpId)) _ownedMaterial.SetFloat(CurlAmpId, _curlAmp);
                if (_ownedMaterial.HasProperty(WaveKId)) _ownedMaterial.SetFloat(WaveKId, _waveK);
                if (_ownedMaterial.HasProperty(SerpAmpId)) _ownedMaterial.SetFloat(SerpAmpId, _serpAmp);
                if (_ownedMaterial.HasProperty(SerpFreqId)) _ownedMaterial.SetFloat(SerpFreqId, Mathf.Round(_serpFreq)); // 정수 필수 — 비정수면 z=1에서 사행이 0으로 안 닫혀 관절이 어긋난다
                if (_ownedMaterial.HasProperty(SwayFreqId)) _ownedMaterial.SetFloat(SwayFreqId, _writheFreq);
                // 성장 보폭 = 1/(1+_GrowWindow) — 재질에서 읽어 단일 출처를 지킨다
                if (_ownedMaterial.HasProperty("_GrowWindow"))
                    _growStride = 1f / (1f + Mathf.Max(0.001f, _ownedMaterial.GetFloat("_GrowWindow")));
                if (_ownedMaterial.HasProperty(LeafModeId)) _ownedMaterial.SetFloat(LeafModeId, 0f); // 줄기 메시엔 정점색이 없다(백색=전부 잎으로 오독 방지)
                ApplyGrowthParams(_ownedMaterial, tint, 2f); // Cull Back — 줄기는 닫힌 입체다
            }
            if (_soilMaterial != null)
            {
                _ownedSoil = new Material(_soilMaterial);
            }
            else
            {
                Debug.LogWarning("[ThornRise] _soilMaterial 미배선 — 흙·먼지·먹 방울 파티클 생략(URP 기본 파티클 재질 폴백 불신)", this);
            }
            if (_clodMesh != null && _material != null)
            {
                _ownedClod = new Material(_material); // 흙덩이=먹색(팔레트 먹 — §8 예외 1)
                if (_ownedClod.HasProperty(BaseColorId)) _ownedClod.SetColor(BaseColorId, _inkColor);
                if (_ownedClod.HasProperty(ColorId)) _ownedClod.SetColor(ColorId, _inkColor);
            }

            // 중심 = 계획점(정본) / 대상 / 허공 착탄점 → 지면은 레이캐스트로(대상 캡슐 중심 y 사용 금지 — 감사 원인 ⑤)
            Vector3 center = _plan != null ? _plan.Point : (target != null ? target.position : fallbackPoint);
            float fallbackY = origin.y - _groundDrop;
            ResolveGround(center, target, fallbackY, out float centerY, out _);
            center.y = centerY;
            transform.position = center;
#if UNITY_EDITOR
            _centerY = centerY;
#endif

            float radius = _plan != null && _plan.Radius > 0f ? _plan.Radius * _spreadFraction : _areaRadius;
            // 최외곽 본 완주 시각 = _spawnStagger + 성장 = plan.Delay → 절정이 판정 프레임과 같다(시계는 하나, 시각은 바꾸지 않는다)
            _resolvedGrowTime = _plan != null && _plan.Delay > 0f
                ? Mathf.Max(_growTimeMin, _plan.Delay - _spawnStagger)
                : _growTime;

            // 잎 메시 1장을 캐스트당 한 번 굽고 전 본이 공유한다(본별 변화는 셰이더 시드·배치가 준다)
            _leafMesh = BuildLeafMesh();
            if (_leafMesh != null && _material != null)
            {
                _ownedLeafMaterial = new Material(_material);
                if (_ownedLeafMaterial.HasProperty(LeafModeId)) _ownedLeafMaterial.SetFloat(LeafModeId, 1f); // 정점색 b=1 → 강조색
                if (_ownedLeafMaterial.HasProperty(TipColorId)) _ownedLeafMaterial.SetColor(TipColorId, tint);
                if (_ownedLeafMaterial.HasProperty(BaseColorId)) _ownedLeafMaterial.SetColor(BaseColorId, _woodColor);
                if (_ownedLeafMaterial.HasProperty(SerpAmpId)) _ownedLeafMaterial.SetFloat(SerpAmpId, _serpAmp);
                if (_ownedLeafMaterial.HasProperty(SerpFreqId)) _ownedLeafMaterial.SetFloat(SerpFreqId, _serpFreq);
                if (_ownedLeafMaterial.HasProperty(CurlAmpId)) _ownedLeafMaterial.SetFloat(CurlAmpId, _curlAmp);
                if (_ownedLeafMaterial.HasProperty(WaveKId)) _ownedLeafMaterial.SetFloat(WaveKId, _waveK);
                ApplyGrowthParams(_ownedLeafMaterial, tint, 0f); // Cull Off — 단면 지오메트리라 양면이 보여야 한다
            }
            BuildRuns(center, radius, target, fallbackY);
            if (_ownedSoil != null) BuildParticles();
        }

        // 해바라기 배치 — 황금각 나선, r=R·sqrt((i+0.5)/N)로 면적 균등. 본별 지면 레이캐스트(경사·단차 대응),
        // 지연은 중심 거리 비례(중심 먼저·외곽 나중 — 「번져 나오는 일제」)
        // 사방 발아 + 지면 들락 — 판정 원 **둘레**에 뿌리를 내리고 안쪽으로 진행하되, 중심에 가까워질수록
        // 접선으로 블렌드해 대상 둘레를 감는다(소용돌이 부호는 캐스트당 하나로 공유 — 그래야 한 방향으로 얽힌다).
        // 관절 높이 h = 지면고 + amp·(sin(2πs/λ + φ) − sin φ) − bias·amp·min(1, s/λ):
        //   − sin φ 항이 뿌리 고정(s=0에서 0) — 셰이더가 밑동 밀림을 막는 기법(InkBody.shader `- sin(_SwaySeed)`)과 동형.
        //   φ ∈ [−1,1] rad이면 cos φ > 0이라 첫 마디는 반드시 솟으며 시작하고, 잠수·재부상은 사인이 만든다.
        //   bias는 평균 높이를 내려 「대체로 흙 속, 가끔 등만 내미는」 뿌리 독법을 만든다.
        // 마디 피치는 관절 두 점에서 파생되므로(LookRotation) 높이 프로파일과 자세가 절대 어긋나지 않는다.
        private void BuildRuns(Vector3 center, float radius, Transform ignore, float fallbackY)
        {
            _vines.Clear();
            int meshCount = _vineMeshes != null ? _vineMeshes.Length : 0;
            float swirlSide = UnityEngine.Random.value < 0.5f ? -1f : 1f; // 캐스트 공통 — 8본이 한 소용돌이를 이룬다
            int segBudget = _maxSegments;

            for (int i = 0; i < _count && segBudget >= 2; i++)
            {
                int segs = Mathf.Clamp(Mathf.RoundToInt(UnityEngine.Random.Range(_segRange.x, _segRange.y)), 2, 12);
                segs = Mathf.Min(segs, segBudget);
                segBudget -= segs;

                float runLen = _runLength * UnityEngine.Random.Range(0.85f, 1.15f);
                float d = runLen / segs;
                float lam = UnityEngine.Random.Range(_weaveLambda.x, _weaveLambda.y);
                // 최대 피치 = atan(2πA/λ). A ≤ λ·0.143이면 41.9°로 고정된다(마디 수와 무관 — 사인의 기울기가 결정)
                float amp = Mathf.Min(UnityEngine.Random.Range(_weaveAmp.x, _weaveAmp.y), lam * 0.143f);
                float phi = UnityEngine.Random.Range(-1f, 1f);
                float sinPhi = Mathf.Sin(phi);
                float mAmp = UnityEngine.Random.Range(_meanderYaw.x, _meanderYaw.y);
                float mLam = Mathf.Max(0.05f, UnityEngine.Random.Range(_meanderLambda.x, _meanderLambda.y));
                float mPhi = UnityEngine.Random.Range(0f, TwoPi);
                float headJitter = UnityEngine.Random.Range(-_inwardSpread, _inwardSpread);

                float ang = i * (360f / Mathf.Max(1, _count)) + UnityEngine.Random.Range(-_ringYawJitter, _ringYawJitter);
                float r0 = radius * UnityEngine.Random.Range(_ringRange.x, _ringRange.y)
                         + UnityEngine.Random.Range(-_placeJitter, _placeJitter);
                Vector3 radial = Quaternion.AngleAxis(ang, Vector3.up) * Vector3.forward;
                Vector3 cur = center + radial * Mathf.Max(0f, r0);

                var joints = new Vector3[segs + 1];
                var norms = new Vector3[segs + 1];
                var exposeAt = new float[segs + 1];   // 관절이 지면 위로 얼마나 나왔는가 — 새싹 게이트
                for (int k = 0; k <= segs; k++)
                {
                    float sArc = k * d;
                    ResolveGround(cur, ignore, fallbackY, out float gy, out Vector3 n);
                    float weave = amp * (Mathf.Sin(TwoPi * sArc / lam + phi) - sinPhi)
                                - _weaveBias * amp * Mathf.Min(1f, sArc / lam);
                    exposeAt[k] = weave - (k == 0 ? _rootSink : 0f);   // 지면고 기준 상대 높이
                    joints[k] = new Vector3(cur.x, gy + exposeAt[k], cur.z);
                    norms[k] = Vector3.Slerp(Vector3.up, n, _slopeAlign).normalized;

                    Vector3 rad = cur - center; rad.y = 0f;
                    float dist = rad.magnitude;
                    rad = dist > 1e-3f ? rad / dist : radial;
                    Vector3 tangent = Vector3.Cross(Vector3.up, rad) * swirlSide;
                    float w = 1f - Mathf.Clamp01(dist / Mathf.Max(0.01f, radius * _swirlStart));
                    Vector3 baseDir = Vector3.Slerp(-rad, tangent, Mathf.SmoothStep(0f, 1f, w) * _swirlMax).normalized;
                    float yawM = headJitter + mAmp * Mathf.Sin(TwoPi * sArc / mLam + mPhi);
                    cur += (Quaternion.AngleAxis(yawM, Vector3.up) * baseDir) * d;
                }

                var segArr = new Seg[segs];
                float acc = 0f;
                for (int k = 0; k < segs; k++)
                {
                    Vector3 v3 = joints[k + 1] - joints[k];
                    float len = v3.magnitude;
                    Vector3 dir = len > 1e-4f ? v3 / len : Vector3.forward;
                    if (len <= 1e-4f) len = d;
                    segArr[k] = new Seg
                    {
                        Pos = joints[k],
                        Expose = exposeAt[k],
                        // 로컬 X = cross(up, forward)라 항상 수평 → 셰이더 사행·꿈틀이 저절로 지면 평면에 눕는다
                        // (_SerpDir MPB 배선이 통째로 불필요해진 이유)
                        Rot = Quaternion.LookRotation(dir, norms[k]),
                        Len = len * (1f + _jointOverlap),
                        Z0 = acc / Mathf.Max(0.01f, runLen),
                        Z1 = (acc + len) / Mathf.Max(0.01f, runLen),
                        Seed = UnityEngine.Random.Range(0f, TwoPi),
                    };
                    acc += len;
                }

                int meshIndex = meshCount > 0 ? i % meshCount : 0;   // 굵기·형태 3종을 고르게 돌린다(예준: 「굵기도 모양도 다양」)
                Mesh mesh = meshCount > 0 ? _vineMeshes[meshIndex] : null;
                if (mesh == null && meshCount > 0) mesh = _vineMeshes[0];

                var vine = new Vine
                {
                    Phase = Phase.Wait,
                    Delay = UnityEngine.Random.Range(0f, _startSpread),   // 산발적 발아 — 한번에 자라지 않는다
                    GroundPos = joints[0],
                    UpN = norms[0],
                    Scale = UnityEngine.Random.Range(_scaleJitter.x, _scaleJitter.y),
                    Seed = UnityEngine.Random.Range(0f, TwoPi),
                    Mesh = mesh,
                    MeshIndex = meshIndex,
                    Segments = segArr,
                };
                BuildSprouts(vine);
                _vines.Add(vine);
            }

            _vines.Sort(ByDelay);
            for (int i = 0; i < _vines.Count; i++) _vines[i].Index = i;
        }


        // 지면 앵커 — 하향 레이캐스트에서 대상·자기·Vitals·CharacterController 계층을 제외한 최근접 히트.
        // Enemy·Ground·Slab이 전부 Layer 0이라 마스크 대신 계층 제외. 히트 없음 → 대상 콜라이더 바닥 → 문양 낙차 근사
        private bool ResolveGround(Vector3 p, Transform ignore, float fallbackY, out float y, out Vector3 n)
        {
            Vector3 from = p + Vector3.up * _groundProbeUp;
            int hitCount = Physics.RaycastNonAlloc(from, Vector3.down, s_groundHits, _groundProbeUp + _groundProbeDown,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (hitCount == GroundHitBuffer) Debug.LogWarning($"[ThornRise] 지면 탐침 버퍼 포화({GroundHitBuffer}) — 최근접 지면 누락 가능", this);
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = s_groundHits[i];
                if (hit.distance >= bestDistance) continue;
                Transform tr = hit.collider.transform;
                if (ignore != null && tr.IsChildOf(ignore)) continue;
                if (tr.IsChildOf(transform)) continue;
                if (hit.collider.GetComponentInParent<EnemyVitals>() != null) continue;
                if (hit.collider.GetComponentInParent<PlayerVitals>() != null) continue;
                if (hit.collider.GetComponentInParent<CharacterController>() != null) continue;
                bestDistance = hit.distance;
                best = i;
            }
            if (best >= 0)
            {
                y = s_groundHits[best].point.y;
                n = s_groundHits[best].normal;
                return true;
            }
            Collider ignoreCollider = ignore != null ? ignore.GetComponent<Collider>() : null;
            y = ignoreCollider != null ? ignoreCollider.bounds.min.y : fallbackY;
            n = Vector3.up;
            return false;
        }

        private void Update()
        {
            if (!_running) return;
            float now = Time.time;
            float sinceBegin = now - _beginTime;
            bool allDone = true;
            bool allRisen = _vines.Count > 0;

            // 전선 속도: 절정(=판정 시각)까지 _climaxSegs개가 솟고, 그 뒤로는 _slowFactor배 느리게 감아 나간다.
            // 판정 시각(plan.Delay)과 그때 솟아 있는 마디 수는 불변 — 이후는 순수 여운이라 시계 무접촉이다
            float fastRate = _climaxSegs / Mathf.Max(0.01f, _resolvedGrowTime);
            float slowRate = fastRate / Mathf.Max(1f, _slowFactor);

            for (int i = 0; i < _vines.Count; i++)
            {
                Vine v = _vines[i];
                if (v.Phase == Phase.Done) continue;
                allDone = false;

                if (v.Phase == Phase.Wait)
                {
                    allRisen = false;
                    if (!v.SoilEmitted && sinceBegin >= v.Delay - _soilLead)
                    {
                        v.SoilEmitted = true; // 흙이 먼저 갈라진다 — 인과
                        EmitSoil(v.GroundPos, v.UpN);
                    }
                    if (sinceBegin < v.Delay) continue;
                    SpawnRun(v);
                    if (v.Segments == null || v.Segments.Length == 0 || v.Segments[0].Tr == null) { v.Phase = Phase.Done; continue; }
                    v.Phase = Phase.Rise;
                    v.PhaseStart = now;
                    v.Grown = 0f;
                    if (_ownedClod != null && v.Index < _clodVines) SpawnClods(v);
                }

                float p = now - v.PhaseStart;
                int segCount = v.Segments.Length;
                switch (v.Phase)
                {
                    case Phase.Rise: // 마디가 차례로 흙을 뚫고 뻗는다 — 전선 G가 마디 경계를 넘을 때마다 다음 마디가 발아
                    {
                        allRisen = false;
                        float dt = Time.deltaTime;
                        v.Grown += (v.Grown < _climaxSegs ? fastRate : slowRate) * dt;
                        float g = v.Grown;
                        // 성숙도 = 본이 얼마나 자랐는가 ∈ [0,1]. 굵기·새싹·녹화의 공통 시계다.
                        // 예준 「50% 정도 자라난 시점부터」 = _sproutStart 0.5 · _greenFrom 0.5
                        v.Maturity = Mathf.Clamp01(g / Mathf.Max(1, segCount));
                        float greenMix = _greenMax * Mathf.InverseLerp(_greenFrom, 1f, v.Maturity);
                        for (int k = 0; k < segCount; k++)
                        {
                            // 마디 k는 G=k에 발아해 G=k+1에 완성. _growStride를 곱해야 첨단이 front=1.0에 정확히 닿는다
                            // (front = _Grow·(1+_GrowWindow)이므로 _Grow=1/(1+window)에서 이미 완성 — 안 곱하면 마디마다 정지 구간)
                            float gk = Mathf.Clamp01(g - k) * _growStride;
                            Seg sg = v.Segments[k];
                            sg.Mpb.SetFloat(GrowId, gk);
                            sg.Mpb.SetFloat(MaturityId, v.Maturity);
                            sg.Mpb.SetFloat(GreenMixId, greenMix);
                            // 자라는 마디만 크게 뒤챈다 — 뻗는 힘이 곧 흔들림의 원인
                            float local = Mathf.Clamp01(g - k);
                            sg.Mpb.SetFloat(SwayAmpId, _swayAmp * _riseSwayMul * Mathf.Sin(Mathf.PI * local));
                            ApplyVineBlock(sg);
                        }
                        if (g >= segCount)
                        {
                            v.Phase = Phase.Hold;
                            v.PhaseStart = now;
                        }
                        break;
                    }
                    case Phase.Hold: // 다 감은 뒤에도 꿈틀거린다 — 감쇠 없음(감쇠는 시듦이 맡는다)
                    {
                        v.Maturity = 1f;
                        for (int k = 0; k < segCount; k++)
                        {
                            v.Segments[k].Mpb.SetFloat(SwayAmpId, _swayAmp * _writheMul);
                            v.Segments[k].Mpb.SetFloat(MaturityId, 1f);
                            v.Segments[k].Mpb.SetFloat(GreenMixId, _greenMax);
                            ApplyVineBlock(v.Segments[k]);
                        }
                        if (p >= _holdTime)
                        {
                            v.Phase = Phase.Wither;
                            v.PhaseStart = now;
                            Seg tip = v.Segments[segCount - 1];
                            if (tip.Tr != null) EmitDrips(tip.Tr.TransformPoint(Vector3.forward));
                        }
                        break;
                    }
                    case Phase.Wither: // 첨단 마디부터 되감기며 먹으로 가라앉는다 — 잔존 0
                    {
                        float k01 = Mathf.Clamp01(p / _witherTime);
                        float front = (1f - k01 * k01) * segCount;   // EaseInQuad로 전선이 뒤로 물러난다
                        for (int k = 0; k < segCount; k++)
                        {
                            Seg sg = v.Segments[k];
                            sg.Mpb.SetFloat(GrowId, Mathf.Clamp01(front - k) * _growStride);
                            sg.Mpb.SetFloat(ValueId, Mathf.Lerp(1f, _sinkValue, Mathf.Min(1f, k01 / _inkSinkPortion)));
                            ApplyVineBlock(sg);
                            if (sg.Tr != null) sg.Tr.position = sg.Pos - v.UpN * (_sinkDrop * k01);
                        }
                        if (k01 >= 1f)
                        {
                            for (int k = 0; k < segCount; k++)
                            {
                                if (v.Segments[k].Tr != null) Destroy(v.Segments[k].Tr.gameObject);
                            }
                            v.Phase = Phase.Done;
                        }
                        break;
                    }
                }
            }

#if UNITY_EDITOR
            // 검증 훅(§6-7): 절정 = 전 본이 _climaxSegs 마디 이상 솟은 시각 — 판정 시각과 대조한다
            if (!_climaxLogged && allRisen)
            {
                _climaxLogged = true;
                string planDelay = _plan != null ? _plan.Delay.ToString("F2") : "null";
                string targetY = _target != null ? _target.position.y.ToString("F2") : "null";
                int segTotal = 0;
                for (int i = 0; i < _vines.Count; i++) segTotal += _vines[i].Segments != null ? _vines[i].Segments.Length : 0;
                Debug.Log($"[ThornRise] climax=+{sinceBegin:F2}s plan.Delay={planDelay} ground.y={_centerY:F2} target.y={targetY} runs={_vines.Count} segs={segTotal}", this);
            }
#endif

            if (allDone)
            {
                _running = false;
                Destroy(gameObject, _settleTail); // 파티클 여운 뒤 정리
            }
        }


        private static void ApplyVineBlock(Seg sg)
        {
            if (sg.Renderer != null) sg.Renderer.SetPropertyBlock(sg.Mpb);
            if (sg.LeafRenderer != null) sg.LeafRenderer.SetPropertyBlock(sg.Mpb);
        }

        // 잎 메시(잎만) — 원본 줄기 FBX는 isReadable=false라 런타임에 못 읽는다. 대신 **자식 렌더러**로 얹는다:
        // 자식의 로컬 변환이 항등이면 오브젝트 공간이 줄기와 동일하므로 셰이더 DeformOS(_Grow 신장·사행·꿈틀)가
        // 줄기와 잎에 **똑같이** 걸린다 — 잎이 줄기에서 떨어져 나가지 않는다(임포트 설정 의존도 0).
        // 잎은 로컬 XZ 평면(법선 ±Y)에 눕는다. Yaw 정렬 덕에 그 평면이 곧 지면이다. 양면으로 굽는다.
        private Mesh BuildLeafMesh()
        {
            if (_leafCount <= 0) return null;
            var verts = new List<Vector3>(_leafCount * 12);
            var norms = new List<Vector3>(_leafCount * 12);
            var cols = new List<Color>(_leafCount * 12);
            var tris = new List<int>(_leafCount * 24);

            for (int i = 0; i < _leafCount; i++)
            {
                float t = (i + 0.5f) / _leafCount;
                float z = Mathf.Lerp(_leafSpan.x, _leafSpan.y, t) + UnityEngine.Random.Range(-0.02f, 0.02f);
                float side = (i % 2 == 0) ? 1f : -1f;
                float len = _leafSize * UnityEngine.Random.Range(0.7f, 1.3f);
                float half = len * 0.42f;
                float skew = UnityEngine.Random.Range(-0.3f, 0.3f) * len;

                // 6정점 잎꼴 — 뿌리에서 좁게 시작해 중간이 가장 넓고 끝이 뾰족하다.
                // 4정점 마름모는 「각진 파편」으로 읽혔다(컷 판독) → 날개를 앞뒤 두 쌍으로 나눠 윤곽을 둥글린다
                Vector3 root = new Vector3(0f, _leafLift, z);
                Vector3 lowA = new Vector3(side * len * 0.28f, _leafLift, z + half * 0.62f);
                Vector3 midA = new Vector3(side * len * 0.66f, _leafLift, z + half * 0.72f + skew * 0.4f);
                Vector3 tip = new Vector3(side * len, _leafLift, z + skew);
                Vector3 midB = new Vector3(side * len * 0.66f, _leafLift, z - half * 0.72f + skew * 0.4f);
                Vector3 lowB = new Vector3(side * len * 0.28f, _leafLift, z - half * 0.62f);

                int b0 = verts.Count;
                verts.Add(root); verts.Add(lowA); verts.Add(midA); verts.Add(tip); verts.Add(midB); verts.Add(lowB);
                for (int k = 0; k < 6; k++) { norms.Add(Vector3.up); cols.Add(LeafColorKey); }
                for (int k = 1; k <= 4; k++) { tris.Add(b0); tris.Add(b0 + k); tris.Add(b0 + k + 1); }

                int b1 = verts.Count; // 뒷면 — 컬링 방향과 무관하게 보이도록
                verts.Add(root); verts.Add(lowA); verts.Add(midA); verts.Add(tip); verts.Add(midB); verts.Add(lowB);
                for (int k = 0; k < 6; k++) { norms.Add(Vector3.down); cols.Add(LeafColorKey); }
                for (int k = 1; k <= 4; k++) { tris.Add(b1); tris.Add(b1 + k + 1); tris.Add(b1 + k); }
            }

            var mesh = new Mesh { name = "ThornLeaves" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // 본 GO — 뿌리를 지면 아래(_rootSink)에 두고 눕힌 채·축소된 채 태어난다. 성장은 셰이더 _Grow(MPB)가 맡는다
        // 본 = 마디 GameObject 체인. 마디마다 자기 관절 자세를 그대로 쓰고, 길이는 **균일 스케일**로 맞춘다
        // (비균일 스케일을 쓰면 무리깅 조항 §4.2-5의 「균일 스케일」 문면을 벗어나 Spec 예외 등재가 선행돼야 한다)
        private void SpawnRun(Vine v)
        {
            if (v.Mesh == null || _ownedMaterial == null || v.Segments == null) return;
            for (int k = 0; k < v.Segments.Length; k++)
            {
                Seg sg = v.Segments[k];
                var go = new GameObject("Seg" + k);
                go.transform.SetPositionAndRotation(sg.Pos, sg.Rot);
                go.transform.localScale = Vector3.one * (sg.Len * v.Scale);
                go.transform.SetParent(transform, true);
                go.AddComponent<MeshFilter>().sharedMesh = v.Mesh;
                var rend = go.AddComponent<MeshRenderer>();
                rend.sharedMaterial = _ownedMaterial;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;

                var mpb = new MaterialPropertyBlock();
                mpb.SetFloat(GrowId, 0f);
                mpb.SetFloat(SwayAmpId, 0f);
                mpb.SetFloat(SwaySeedId, sg.Seed);
                mpb.SetFloat(ValueId, 1f);
                // 줄기 색 그라디언트를 체인 전체 기준으로 — 안 주면 마디마다 갈색→청록이 반복돼 줄무늬가 된다
                mpb.SetVector(ChainZId, new Vector4(sg.Z0, sg.Z1, 0f, 0f));
                // 마디 단면 중심(z 선형 척추) — 실측상 A·B는 z=0 단면도 원점이 아니다.
                // 굵기 스케일이 이 점 기준이라야 얇을 때 뿌리가 축에서 미끄러지지 않는다
                mpb.SetVector(SpineId, SpineOf(v.MeshIndex));
                mpb.SetFloat(MaturityId, 0f);
                mpb.SetFloat(GreenMixId, 0f);
                rend.SetPropertyBlock(mpb);

                sg.Tr = go.transform;
                sg.Renderer = rend;
                sg.Mpb = mpb;

                // 새싹은 **끝 2마디 중 지면 위로 나온 마디에만**(예준 2026-09-05 확정).
                // 근위 절반이 맨 갈색으로 남는 것은 폴리 예산에서 나온 의도적 축소이며,
                // 밑동 갈색 → 끝 청록 그라디언트(#144)와 같은 방향이다
                if (v.Sprouts != null && v.Sprouts[k] != null && _ownedLeafMaterial != null)
                {
                    var sproutGo = new GameObject("Leaves");   // 이름은 ThornProbe 계측 계약(자식명으로 잎을 센다)
                    sproutGo.transform.SetParent(go.transform, false);
                    sproutGo.AddComponent<MeshFilter>().sharedMesh = v.Sprouts[k];
                    var sproutRend = sproutGo.AddComponent<MeshRenderer>();
                    sproutRend.sharedMaterial = _ownedLeafMaterial;
                    sproutRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    sproutRend.receiveShadows = false;
                    sproutRend.SetPropertyBlock(mpb);
                    sg.LeafRenderer = sproutRend;
                }
            }
        }

        // 굵기·새싹·녹화의 재질 상수. 기본값이 전부 항등(_GirthMin 1 · _GirthTip 1 · _GreenMix 0)이라
        // 배선 안 된 사용처는 오늘과 픽셀 동일하다 — 그 계약을 깨지 않으려면 여기서만 값을 넣는다
        private void ApplyGrowthParams(Material m, Color tint, float cull)
        {
            if (m == null) return;
            if (m.HasProperty("_GirthMin")) m.SetFloat("_GirthMin", _girthMin);
            if (m.HasProperty("_GirthTip")) m.SetFloat("_GirthTip", _girthTip);
            if (m.HasProperty("_SproutStart")) m.SetFloat("_SproutStart", _sproutStart);
            if (m.HasProperty("_GreenFromZ")) m.SetFloat("_GreenFromZ", 0.25f);
            // 녹화 종점 = 목 강조색 그대로. 새 색을 만들지 않는다(색 단일 출처)
            if (m.HasProperty("_MossColor")) m.SetColor("_MossColor", tint);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", cull);
        }

        private Vector4 SpineOf(int meshIndex)
        {
            return (_meshSpine != null && meshIndex >= 0 && meshIndex < _meshSpine.Length)
                ? _meshSpine[meshIndex] : Vector4.zero;
        }

        private Vector2 HalfOf(int meshIndex)
        {
            return (_meshHalf != null && meshIndex >= 0 && meshIndex < _meshHalf.Length)
                ? _meshHalf[meshIndex] : new Vector2(0.12f, 0.18f);
        }

        // 어느 마디에 새싹을 붙일지 — 끝 2마디 중 **지면 위로 나온** 것. 둘 다 묻혔으면
        // 본 전체에서 가장 많이 드러난 마디 하나에 붙인다(초록이 땅속에서 자라는 것을 막는다).
        // 후보를 **전부 판정한 뒤** 폴백을 정한다 — 순서를 뒤집으면 폴백이 게이트를 선점한다
        private void BuildSprouts(Vine v)
        {
            int n = v.Segments.Length;
            v.Sprouts = new Mesh[n];
            if (_sproutSites <= 0 || (_grassPerSite <= 0 && _twigPerSite <= 0)) return;

            // 본당 마디 수(예준 확정 2)는 지키되, **끝 2마디가 아니라 가장 많이 드러난 2마디**를 고른다.
            // 본은 링에서 안쪽으로 진행하므로 끝 마디 = 중심부다 — 끝에 붙이면 초록이 가운데에만 뭉친다(컷 판독).
            // 지면 위로 나온 마디에만 붙는다는 게이트는 그대로다(초록이 땅속에서 자라면 안 된다).
            var pick = new List<int>(_sproutSegsPerRun);
            for (int slot = 0; slot < _sproutSegsPerRun; slot++)
            {
                int best = -1;
                for (int k = 0; k < n; k++)
                {
                    if (v.Segments[k].Expose <= 0f || pick.Contains(k)) continue;
                    if (best < 0 || v.Segments[k].Expose > v.Segments[best].Expose) best = k;
                }
                if (best < 0) break;
                pick.Add(best);
            }
            if (pick.Count == 0)
            {
                // 본 전체가 흙 속인 드문 경우 — 그래도 가장 덜 묻힌 마디 하나에는 낸다
                int best = 0;
                for (int k = 1; k < n; k++)
                {
                    if (v.Segments[k].Expose > v.Segments[best].Expose) best = k;
                }
                pick.Add(best);
            }

            Vector4 spine = SpineOf(v.MeshIndex);
            Vector2 half = HalfOf(v.MeshIndex);
            for (int i = 0; i < pick.Count; i++)
            {
                v.Sprouts[pick[i]] = BuildSproutMesh(spine, half);
            }
        }

        // 새싹 메시 — 부착점(uv.xyz)은 줄기 표면, 형상은 그 점에서 자란다. 셰이더가
        // 부착점만 굵기를 따라 옮기고 형상은 open(발아 진행)으로만 키운다.
        //   정점색: rg=0.5 중립(_SpineMode 미간섭) · b=1 풀(청록) / 0 잔가지(목질 갈색) · a=발아 순서
        //   풀날 1tri · 잔가지 Y자 3tri. 재질이 Cull Off라 양면이 공짜다
        private Mesh BuildSproutMesh(Vector4 spine, Vector2 half)
        {
            int cap = _sproutSites * (_grassPerSite * 3 + _twigPerSite * 9);
            var verts = new List<Vector3>(cap);
            var norms = new List<Vector3>(cap);
            var cols = new List<Color>(cap);
            var uvs = new List<Vector4>(cap);
            var tris = new List<int>(cap);

            for (int i = 0; i < _sproutSites; i++)
            {
                float z = Mathf.Lerp(_sproutZ.x, _sproutZ.y, (i + UnityEngine.Random.value) / _sproutSites);
                // 단면 위쪽 호를 따라 부착 — 가로 매개변수 u가 실제로 자리를 옮긴다(정수리에만 몰리지 않게)
                float u = UnityEngine.Random.Range(-0.85f, 0.85f);
                Vector2 c = Vector2.Lerp(new Vector2(spine.x, spine.y), new Vector2(spine.z, spine.w), Mathf.Clamp01(z));
                var anchor = new Vector3(c.x + half.x * u,
                                         c.y + half.y * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u)) * 0.92f,
                                         z);
                var a4 = new Vector4(anchor.x, anchor.y, anchor.z, 0f);
                float order = (i + 0.5f) / _sproutSites;   // 부착점 순서대로 트인다 — 한꺼번에 안 핀다

                for (int g = 0; g < _grassPerSite; g++)
                {
                    float h = UnityEngine.Random.Range(_grassLen.x, _grassLen.y);
                    float yaw = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    var side = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * _grassWidth;
                    var lean = new Vector3(Mathf.Cos(yaw + 1.1f), 0f, Mathf.Sin(yaw + 1.1f)) * (h * 0.30f);
                    Vector3 apex = anchor + Vector3.up * h + lean;
                    var col = new Color(0.5f, 0.5f, 1f, order);     // b=1 → 청록
                    AddTri(verts, norms, cols, uvs, tris, anchor - side, anchor + side, apex, col, a4);
                }

                for (int t = 0; t < _twigPerSite; t++)
                {
                    float h = UnityEngine.Random.Range(_twigLen.x, _twigLen.y);
                    float yaw = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    var side = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * _twigWidth;
                    var fwd = new Vector3(Mathf.Cos(yaw + 1.5708f), 0f, Mathf.Sin(yaw + 1.5708f));
                    Vector3 node = anchor + Vector3.up * (h * 0.55f) + fwd * (h * 0.12f);
                    var col = new Color(0.5f, 0.5f, 0f, order);     // b=0 → 목질 갈색(줄기색 경로)
                    AddTri(verts, norms, cols, uvs, tris, anchor - side, anchor + side, node, col, a4);
                    // 두 갈래 — 「가지」로 읽히는 유일한 신호는 분기다(긴 풀날로는 대체되지 않는다)
                    Vector3 p1 = node + Vector3.up * (h * 0.45f) + fwd * (h * 0.42f);
                    Vector3 p2 = node + Vector3.up * (h * 0.50f) - fwd * (h * 0.34f);
                    AddTri(verts, norms, cols, uvs, tris, node - side * 0.7f, node + side * 0.7f, p1, col, a4);
                    AddTri(verts, norms, cols, uvs, tris, node - side * 0.7f, node + side * 0.7f, p2, col, a4);
                }
            }

            if (tris.Count == 0) return null;
            var mesh = new Mesh { name = "ThornSprouts" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetColors(cols);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // 삼각형 하나 — 법선을 면 법선으로 굽는다(미설정이면 셰이더의 normalize가 NaN이 된다)
        private static void AddTri(List<Vector3> verts, List<Vector3> norms, List<Color> cols,
                                   List<Vector4> uvs, List<int> tris,
                                   Vector3 a, Vector3 b, Vector3 c, Color col, Vector4 anchor)
        {
            Vector3 nrm = Vector3.Cross(b - a, c - a);
            nrm = nrm.sqrMagnitude > 1e-12f ? nrm.normalized : Vector3.up;
            int i0 = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            norms.Add(nrm); norms.Add(nrm); norms.Add(nrm);
            cols.Add(col); cols.Add(col); cols.Add(col);
            uvs.Add(anchor); uvs.Add(anchor); uvs.Add(anchor);
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
        }


        // 선택 — 메시 흙덩이(SpikeImpactShard 0.3s 재사용, 먹색 재질 인스턴스). 루트 미종속 — 0.3s 안에 스스로 소멸
        private void SpawnClods(Vine v)
        {
            for (int i = 0; i < _clodsPerVine; i++)
            {
                var go = new GameObject("Clod");
                go.transform.position = v.GroundPos;
                go.transform.rotation = UnityEngine.Random.rotation;
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = _clodMesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _ownedClod;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                Vector3 dir = (v.UpN + RandomHorizontal(v.UpN) * _clodSpread).normalized;
                go.AddComponent<SpikeImpactShard>().Init(dir, _clodScale * v.Scale);
            }
        }

        // 흙·먼지·먹 방울 파티클 3종 전 구성이 여기 있다(FlameJet.BuildJet 선례 — 코드 구성). 색은 전부 EmitParams.startColor(먹/먹↔틴트),
        // colorOverLifetime 색 키는 백색·알파만(TintHierarchy 백색 중립화와 동형). 방출률 0 — Emit만, loop로 시뮬레이션 유지
        private void BuildParticles()
        {
            var soilGradient = new Gradient();
            soilGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(_soilAlpha, 0f),
                    new GradientAlphaKey(_soilAlpha, _soilAlphaHold),
                    new GradientAlphaKey(0f, 1f),
                });
            var dustGradient = new Gradient();
            dustGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(_dustAlpha, 0f), new GradientAlphaKey(0f, 1f) });

            var dripGradient = new Gradient(); // 먹 방울 전용 — 알파 _dripAlpha→0 선형(Soil의 유지 구간·크기 곡선과 겹치지 않음)
            dripGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(_dripAlpha, 0f), new GradientAlphaKey(0f, 1f) });

            _soil = BuildSystem("Soil", _soilGravity, soilGradient, _soilSizeCurve);
            _dust = BuildSystem("Dust", _dustGravity, dustGradient, _dustSizeCurve);
            _drip = BuildSystem("Drip", _dripGravity, dripGradient, Vector2.one);
        }

        private ParticleSystem BuildSystem(string name, float gravity, Gradient colorOverLife, Vector2 sizeCurve)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true; // 방출률 0·Emit만 — 비루프는 duration 만료 후 Emit이 잠들 수 있어 루프로 시뮬레이션 유지
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 튄 흙은 세상에 남는다(루트 회전 무관)
            main.maxParticles = _maxParticles;
            main.gravityModifier = gravity;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(colorOverLife);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, sizeCurve.x), new Keyframe(1f, sizeCurve.y)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = _ownedSoil;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ps.Play();
            return ps;
        }

        // 흙 터짐 — 먹색 흙 알갱이(위+방사, 중력 낙하) + 먹↔틴트 먼지(낮게 퍼짐, 살짝 뜸)
        private void EmitSoil(Vector3 pos, Vector3 upN)
        {
            if (_soil == null) return;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < _soilClods; i++)
            {
                ep.position = pos;
                ep.velocity = upN * UnityEngine.Random.Range(_soilUpSpeed.x, _soilUpSpeed.y)
                    + RandomHorizontal(upN) * UnityEngine.Random.Range(_soilSideSpeed.x, _soilSideSpeed.y);
                ep.startSize = UnityEngine.Random.Range(_soilSize.x, _soilSize.y);
                ep.startLifetime = UnityEngine.Random.Range(_soilLife.x, _soilLife.y);
                ep.startColor = _inkColor;
                _soil.Emit(ep, 1);
            }

            if (_dust == null) return;
            Color dustColor = Color.Lerp(_inkColor, _tint, _dustTintMix);
            dustColor.a = 1f; // 알파는 colorOverLifetime 곡선이 정본
            for (int i = 0; i < _dustPuffs; i++)
            {
                ep.position = pos;
                ep.velocity = upN * _dustUpSpeed + RandomHorizontal(upN) * _dustSideSpeed;
                ep.startSize = UnityEngine.Random.Range(_dustSize.x, _dustSize.y);
                ep.startLifetime = UnityEngine.Random.Range(_dustLife.x, _dustLife.y);
                ep.startColor = dustColor;
                _dust.Emit(ep, 1);
            }
        }

        // 시듦 진입 — 첨단에서 먹 방울 몇 알이 떨어진다(먹으로 가라앉는 소산의 예고)
        private void EmitDrips(Vector3 tip)
        {
            if (_drip == null) return;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < _dripCount; i++)
            {
                ep.position = tip;
                ep.velocity = Vector3.down * _dripSpeed;
                ep.startSize = _dripSize;
                ep.startLifetime = _dripLife;
                ep.startColor = _inkColor; // 알파는 Drip colorOverLifetime(_dripAlpha→0)이 정본
                _drip.Emit(ep, 1);
            }
        }

        // 법선에 수직인 무작위 단위 벡터(방사 방향)
        private static Vector3 RandomHorizontal(Vector3 upN)
        {
            Vector3 rnd = UnityEngine.Random.insideUnitSphere;
            Vector3 side = rnd - upN * Vector3.Dot(rnd, upN);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(upN, Vector3.right);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(upN, Vector3.forward);
            return side.normalized;
        }

        private static float EaseOutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            t -= 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }

        private void OnDestroy()
        {
            // 본별 새싹 메시는 런타임 생성물이라 명시 해제한다(잎 메시 선례 동류)
            for (int i = 0; i < _vines.Count; i++)
            {
                var sp = _vines[i].Sprouts;
                if (sp == null) continue;
                for (int k = 0; k < sp.Length; k++)
                {
                    if (sp[k] != null) Destroy(sp[k]);
                }
            }

            if (_ownedMaterial != null) Destroy(_ownedMaterial);
            if (_ownedSoil != null) Destroy(_ownedSoil);
            if (_ownedClod != null) Destroy(_ownedClod);
            if (_leafMesh != null) Destroy(_leafMesh);
            if (_ownedLeafMaterial != null) Destroy(_ownedLeafMaterial);
        }
    }
}
