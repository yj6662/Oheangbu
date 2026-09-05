using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App
{
    // [SPEC-SPELL-FX-REWORK §3.3 — 오(水) 「전진하는 느린 파도」] 전용 구현(#138) — GroundWaveEffect는 모(土) 전용으로 남긴다.
    // 문법: 발치 스밈(潤下의 예고) → 뒤로 눕힌 낮은 둔덕이 부풀어 서는 형성(Rise) → 계획 속도 전진(헤이브·피치·스웨이·
    //   부서짐 주기에 립 물보라) → 사거리 끝에서 퍼지며 앞으로 무너지는 소멸(Sink·디졸브). 잔존·발광 없음.
    // 실체: 로브 메시(WaveLobe_O) ×N 지그재그 + 물몸(내장 Sphere). 재질 Oheangbu/InkWater 인스턴스 1장을 로브·물몸이 공유,
    //   파티클 4종(립 물보라·앞물·먹자국 꼬리·바닥 스밈)은 코드 구성(FlameJetEffect.BuildJet 선례) + Oheangbu/InkSpray.
    // 판정 시계(SPELL-AREA-SHAPES §3): AreaImpactPlan(Point·Direction·Length·Speed·Delay)을 읽기만 한다 — 루트 병진이 시계를
    //   소유하고 로브·물몸·파티클은 루트 기준 오프셋만 갖는다. 중앙 로브 립 첨단 = 루트 = 판정 전선(_frontLead 자동 산출),
    //   측면 로브는 뒤로만 스태거(전선 앞에 립 없음), Sink 전진은 _sinkCreep 상한. 피해는 배선 시계 그대로(연출≠실판정).
    // 색: PatternEffectLifetime.TintHierarchy를 호출하지 않는다 — _ownedMaterial _BaseColor/_Color = tint,
    //   PS startColor = tint × 명도 스칼라(0.7~1.2)만(팔레트 단일 출처, 그라디언트 종점은 무채 먹빛).
    //   루트에 TintHierarchy를 다시 걸면 PS startColor가 tint로 덮여 물보라 명도 파생이 사라진다 — 걸지 말 것.
    //   어댑터는 sequence.Begin(SetParent(null)) 뒤에 AttachBloom을 호출하므로 분리된 이 연출은 부모 틴트를 받지 않는다.
    // 축 규약(FX-ASSETS §4.2-6): 로브 메시 +Z 높이·+Y 전진(립)·+X 폭, 바닥 피벗 — LookRotation(up, dir)이 +Z→up·+Y→dir.
    //   반입 후 MeshAxisProbe(립·높이 부호) 판정 없이 배선 금지. 물몸 Sphere도 같은 회전(로컬 x=폭·y=깊이·z=높이).
    // 인식 불가침: 표현 전용 — 무엇을 바꿔도 인식·판정·필세는 불변이다.
    public sealed class WaterWaveEffect : SpellSequenceEffect
    {
        [Header("자원 — 로브 메시(+Z 높이·+Y 립·+X 폭·바닥 피벗)·물몸(내장 Sphere)·재질. 비우면 해당 요소 생략")]
        [SerializeField] private Mesh _lobeMesh;           // WaveLobe_O(폴백: WaveCrest_O + _lobeScaleXYZ)
        [SerializeField] private Mesh _lobeMeshAlt;        // 중앙 로브 변형 B(G1 반복감 판정 후) — 비우면 미사용. 깊이(+Y)는 달라도 되나 높이(size.z)는 _lobeMesh와 동일 정규화(_HeightOS 1값 공유)
        [SerializeField] private Mesh _swellMesh;          // 물몸 — 내장 Sphere(fileID 10207)
        [SerializeField] private Material _bodyMaterial;   // M_SpellBodyWater — Begin에서 1장 인스턴스화
        [SerializeField] private Material _sprayMaterial;  // M_InkSpray — 공유(색은 startColor 경로). 비우면 기본 파티클 재질
        [SerializeField] private Vector3 _lobeExtraEuler;  // 반입 메시 축 미세 보정(검수용 — 리컴파일 없이)

        [Header("계획 폴백 [TEST] — 정본=SpellBook·AreaImpactPlan(REWORK §8-2). 감사 씬은 계획 없이 이 값으로 돈다")]
        [SerializeField, Min(0f)] private float _groundDrop = 1.2f;    // 문양(가슴 높이)→지면 근사 낙차
        [SerializeField, Min(0.01f)] private float _riseTime = 0.45f;  // 형성(=AreaImpactDelay)
        [SerializeField, Min(0.1f)] private float _speed = 6f;         // 전진(=AreaSpeed) — 「느린 파도」
        [SerializeField, Min(0.1f)] private float _maxRange = 14f;     // (=AreaLength)
        [SerializeField, Min(0f)] private float _overshoot = 2.5f;     // 목표를 지나쳐 훑는 여분(계획 없을 때)
        [SerializeField, Min(0.01f)] private float _sinkTime = 0.6f;   // 소멸
        [SerializeField, Min(0f)] private float _tailGrace = 1.2f;     // Sink 종료 후 파티클 여운 → 파괴

        [Header("로브 조합 [TEST] — 지그재그: 측면 로브는 뒤로만 스태거(전선 앞에 립 없음)")]
        [SerializeField, Min(1)] private int _lobeCount = 3;
        [SerializeField, Min(0f)] private float _lobeSpacing = 1.0f;         // 측방 간격(m)
        [SerializeField, Min(0.01f)] private float _lobeScale = 1.6f;        // 정규 폭 1.0 → 실폭(m)
        [SerializeField] private Vector3 _lobeScaleXYZ = Vector3.one;         // 핀 폴백(WaveCrest_O)용 비등방 승수 (7, 2.2, 4.2)
        [SerializeField, Range(0f, 0.5f)] private float _lobeScaleJitter = 0.08f;
        [SerializeField, Range(0f, 45f)] private float _lobeYawJitter = 7f;   // 요 ±(도)
        [SerializeField, Min(0f)] private float _lobeStagger = 0.35f;        // 측면 로브 후퇴(m)
        [SerializeField] private float _frontLead = -1f;                      // <0 = 자동(bounds.max.y × 배율): 중앙 립 첨단 = 루트

        [Header("형성 [TEST] — 뒤로 눕힌 낮은 둔덕이 부풀어 선다(EaseOutCubic)")]
        [SerializeField, Range(0f, 1f)] private float _riseHeightFrom = 0.2f;
        [SerializeField] private float _risePitchFrom = -20f;                 // 피치(도): −=뒤로 눕힘 / +=앞으로 기욺
        [SerializeField, Min(0f)] private float _riseBury = 0.3f;             // 시작 매장(m)
        [SerializeField, Range(0f, 1.5f)] private float _riseWidthFrom = 0.9f;
        [SerializeField, Range(0f, 1f)] private float _riseSwellHeightFrom = 0.15f;
        [SerializeField, Min(0)] private int _riseSeepBurst = 12;             // 커밋 즉시 발치 스밈
        [SerializeField, Range(0f, 1f)] private float _frontStartK = 0.67f;   // 앞물 가동 시점(Rise 진행률)
        [SerializeField, Range(0f, 1f)] private float _firstSprayK = 0.9f;    // 립이 서며 첫 물보라
        [SerializeField, Min(0)] private int _firstSprayBurst = 6;            // 로브당

        [Header("전진 [TEST] — 헤이브·피치·스웨이(로브별 위상)·부서짐 주기(립 붕괴 시 물보라 버스트)")]
        [SerializeField, Range(0f, 0.5f)] private float _heaveAmp = 0.08f;
        [SerializeField, Min(0f)] private float _heaveHz = 1.6f;
        [SerializeField, Range(0f, 30f)] private float _pitchAmp = 5f;        // 도
        [SerializeField, Min(0f)] private float _pitchHz = 1.1f;
        [SerializeField] private float _pitchPhaseLag = 0.7f;                 // 피치 위상 지연(rad) — 헤이브와 어긋나게
        [SerializeField, Range(0f, 0.5f)] private float _swayAmp = 0.05f;     // 측방(m)
        [SerializeField, Min(0f)] private float _swayHz = 0.9f;
        [SerializeField, Min(0.05f)] private float _breakPeriod = 0.9f;       // 부서짐 주기(s)
        [SerializeField, Range(0.01f, 0.5f)] private float _breakRiseFrac = 0.15f; // 주기 내 피크 도달 비율
        [SerializeField, Range(0.2f, 0.95f)] private float _breakDipFrac = 0.5f;   // 립 붕괴 종료 비율(이후 회복)
        [SerializeField, Min(1f)] private float _breakPeak = 1.12f;
        [SerializeField, Range(0.3f, 1f)] private float _breakDip = 0.90f;
        [SerializeField, Min(0)] private int _breakBurst = 8;                 // 립 붕괴 진입 시 로브당

        [Header("소멸 [TEST] — 퍼지며 앞으로 무너지고 위에서부터 갈라져 스민다(潤下)")]
        [SerializeField, Range(0f, 1f)] private float _sinkHeightTo = 0.05f;
        [SerializeField, Min(1f)] private float _sinkWidthGrow = 1.3f;
        [SerializeField] private float _sinkPitch = 18f;                      // 도(+ = 앞으로 무너짐)
        [SerializeField, Min(0f)] private float _sinkCreep = 0.3f;            // Sink 중 전진 상한(m) — 판정 길이 밖 훑기 금지
        [SerializeField, Min(0)] private int _seepBurst = 30;

        [Header("물몸 [TEST] — 마루 뒤에 딸린 낮고 넓은 둔덕(내장 Sphere), 호흡")]
        [SerializeField, Min(0f)] private float _swellWidth = 3.6f;
        [SerializeField, Min(0f)] private float _swellDepth = 2.6f;
        [SerializeField, Min(0f)] private float _swellHeight = 0.7f;
        [SerializeField, Min(0f)] private float _swellLag = 0.7f;             // 루트 뒤(m)
        [SerializeField] private float _swellDrop = 0.1f;                     // 중심 침하(m) → 정점 지면 +0.25
        [SerializeField, Range(0f, 0.5f)] private float _swellBreath = 0.10f;
        [SerializeField, Min(0f)] private float _swellBreathHz = 1.2f;

        [Header("파티클 공통 [TEST] — 색 = tint × 명도 → 먹빛 침전(_inkColor = 팔레트 _fallback 미러, REWORK §8-1)")]
        [SerializeField] private Color _inkColor = new Color(0.16f, 0.15f, 0.13f);
        [Tooltip("한지 소지 미러(팔레트 PaperColor) — 포말이 파면보다 밝아야 물보라로 읽힌다(역번짐: 소지가 드러난다)")]
        [SerializeField] private Color _paperColor = new Color(0.969f, 0.945f, 0.894f);
        [SerializeField] private bool _debugFront;                            // 기즈모: 루트에 판정 전선(반폭) 선분

        [Header("립 물보라 [TEST] — 로브당 1, 립 위치·스트레치 빌보드")]
        [SerializeField, Min(0f)] private float _sprayRatePerMeter = 6f;
        [SerializeField] private Vector2 _spraySize = new Vector2(0.06f, 0.16f);
        [SerializeField] private Vector2 _sprayLife = new Vector2(0.45f, 0.8f);
        [SerializeField] private Vector2 _sprayUp = new Vector2(1.8f, 3.2f);   // 상승 속도(m/s)
        [SerializeField, Min(0f)] private float _sprayForward = 2f;           // 전방 속도(m/s)
        [SerializeField, Min(0f)] private float _sprayGravity = 1f;
        [SerializeField, Min(0f)] private float _sprayBrightness = 1.2f;
        [Tooltip("립 물보라의 소지 혼합 — 0=현행(파면보다 어두움) / 0.55=포말이 파면보다 밝다")]
        [SerializeField, Range(0f, 1f)] private float _sprayPaperMix = 0.55f;
        [SerializeField, Range(0f, 1f)] private float _sprayAlpha = 0.55f;
        [SerializeField, Min(0f)] private float _sprayStretchVelocity = 0.08f;
        [SerializeField, Min(0f)] private float _sprayStretchLength = 1.6f;
        [SerializeField, Min(1)] private int _sprayMax = 100;
        [SerializeField] private Vector2 _lipAnchor = new Vector2(0.9f, 0.95f); // 립 위치 = (max.y × x, size.z × y) 메시 로컬

        [Header("앞물 [TEST] — 전선 앞 지면에서 밀려나는 빌보드")]
        [SerializeField, Min(0f)] private float _frontRatePerMeter = 8f;
        [SerializeField, Min(0f)] private float _frontAhead = 0.5f;
        [SerializeField, Min(0f)] private float _frontSpeed = 1.2f;
        [SerializeField] private Vector2 _frontSize = new Vector2(0.08f, 0.2f);
        [SerializeField, Min(0.05f)] private float _frontLife = 0.4f;
        [SerializeField, Min(0f)] private float _frontBrightness = 0.9f;
        [SerializeField, Range(0f, 1f)] private float _frontAlpha = 0.5f;
        [SerializeField, Min(1)] private int _frontMax = 120;

        [Header("먹자국 꼬리 [TEST] — 루트 뒤 지면 수평 빌보드(소멸 페이드 ≤0.9s — 장판 아님, REWORK §8-5)")]
        [SerializeField, Min(0f)] private float _sheetRatePerMeter = 4f;      // 장판으로 읽히면 0
        [SerializeField, Min(0f)] private float _sheetBack = 0.8f;
        [SerializeField] private Vector2 _sheetSize = new Vector2(0.6f, 1.1f);
        [SerializeField, Min(0.05f)] private float _sheetLife = 0.9f;
        [SerializeField] private Vector2 _sheetGrow = new Vector2(0.6f, 1.3f);
        [SerializeField, Min(0f)] private float _sheetBrightness = 0.8f;
        [SerializeField, Range(0f, 1f)] private float _sheetAlpha = 0.3f;
        [SerializeField, Min(1)] private int _sheetMax = 80;

        [Header("바닥 스밈 [TEST] — 코드 Emit만(형성 예고·소멸), 방사 감쇠·수평 빌보드")]
        [SerializeField, Min(0f)] private float _seepSpeed = 1.0f;
        [SerializeField, Range(0f, 1f)] private float _seepDampen = 0.9f;      // 시뮬 스텝마다 적용 — 0.9면 수 프레임에 멎는다. G1: 번짐이 짧으면 0.05~0.15로 낮추거나 _seepSpeed 0 + _seepGrow만으로 퍼뜨린다
        [SerializeField, Min(0.05f)] private float _seepLife = 0.9f;
        [SerializeField] private Vector2 _seepGrow = new Vector2(0.3f, 1.4f);
        [SerializeField, Min(0f)] private float _seepBrightness = 0.7f;
        [SerializeField, Range(0f, 1f)] private float _seepAlpha = 0.4f;
        [SerializeField, Range(0f, 1f)] private float _seepRadiusMul = 0.8f;   // 전면 반폭 × 이 값
        [SerializeField, Min(1)] private int _seepMax = 60;

        // 기술 상수 — 룩 노브가 아니다
        private const float Epsilon = 1e-4f;
        private const float GroundHover = 0.02f;        // 수평 빌보드가 지면과 z-fight 하지 않게
        private const float FrontHover = 0.05f;         // 앞물 방출 높이
        private const float EmitterThickness = 0.05f;   // 박스 방출 두께
        private const float LipBoxDepth = 0.1f;         // 립 방출 박스 전후 두께(메시 로컬)
        private const float LipBoxWidthMul = 0.9f;      // 립 방출 폭 = 메시 폭 × 이 값
        private const float FrontBoxDepth = 0.3f;
        private const float SheetBoxDepth = 0.4f;
        private const float SeepLimitSpeed = 0.05f;     // 스밈 속도 제한(이 아래로 감쇠)
        private const float InkAlphaMid = 0.85f;        // 그라디언트 알파 중간 키
        private const float InkAlphaMidTime = 0.4f;
        private const float DebugHalfWidthFallback = 1.5f; // 계획 없을 때 기즈모 반폭(SpellBook 오 AreaRadius)

        private enum Phase { Idle, Rise, Advance, Sink, Done }

        private sealed class Lobe
        {
            public Transform Tr;
            public MeshRenderer Renderer;
            public ParticleSystem Spray;
            public Vector3 BaseScale;
            public float Lateral;      // 측방 오프셋(m)
            public float Forward;      // 전진 오프셋(m) — 립 첨단 정렬 + 스태거
            public float Phase;        // 오실레이터 위상(rad)
            public float BreakOffset;  // 부서짐 주기 위상(0..1)
            public float Yaw;          // 도
            public int BreakSeg;       // 0=피크 상승 구간 / 1=붕괴·회복 구간(진입 에지에서 버스트)
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        private static readonly int HeightOsId = Shader.PropertyToID("_HeightOS");

        // InkSpray 정점 스트림 계약 — TEXCOORD0 = (uv.x, uv.y, stableRandom.x)
        private static readonly List<ParticleSystemVertexStream> SprayStreams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position,
            ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.StableRandomX,
        };

        private readonly List<Lobe> _lobes = new List<Lobe>();
        private Transform _swell;
        private MeshRenderer _swellRenderer;
        private ParticleSystem _front;
        private ParticleSystem _sheet;
        private ParticleSystem _seep;
        private Material _ownedMaterial;
        private Gradient _inkGradient;
        private AreaImpactPlan _plan;

        private Phase _phase = Phase.Idle;
        private float _phaseStart;
        private float _beginTime;
        private Color _tint;
        private Vector3 _dir;
        private Vector3 _right;
        private Quaternion _baseRot;
        private float _runSpeed;
        private float _runRiseTime;
        private float _travelDistance;
        private float _traveled;
        private float _creepLeft;
        private float _frontLeadResolved;
        private float _frontWidth;
        private bool _frontStarted;
        private bool _firstSprayDone;

        // 판정 계획 [SPELL-AREA-SHAPES §3] — 시작점·방향·길이·속도·차오름을 판정과 공유한다(직렬 값은 폴백)
        public override void SetAreaPlan(AreaImpactPlan plan)
        {
            _plan = plan;
        }

        public override void Begin(Vector3 origin, Transform target, Vector3 fallbackPoint, Color tint)
        {
            transform.SetParent(null, true);
            transform.localScale = Vector3.one; // 글자 크기 비의존 — 파도 실크기는 _lobeScale·물몸 치수가 정본
            _tint = tint;

            // 기점 = 시전자(문양) 아래 지면 근사 — 파도는 내 앞에서 태어나 목표를 향해 밀려간다(GroundWaveEffect와 동일 계약)
            Vector3 goal = target != null ? target.position : fallbackPoint;
            Vector3 start = origin;
            start.y = target != null ? target.position.y : origin.y - _groundDrop;

            Vector3 flat = goal - start;
            flat.y = 0f;
            _dir = flat.sqrMagnitude > 0.001f ? flat.normalized : Vector3.forward;
            _travelDistance = Mathf.Min(_maxRange, flat.magnitude + _overshoot);
            _runSpeed = _speed;
            _runRiseTime = _riseTime;

            if (_plan != null)
            {
                // 판정이 정한 복도 그대로 — 전선이 닿는 시각(차오름 + 거리/속도)이 피해 시각과 같아진다
                Vector3 planDir = new Vector3(_plan.Direction.x, 0f, _plan.Direction.z);
                if (planDir.sqrMagnitude > 0.001f) _dir = planDir.normalized;
                if (_plan.Length > 0f) _travelDistance = _plan.Length;
                if (_plan.Speed > 0f) _runSpeed = _plan.Speed;
                if (_plan.Delay > 0f) _runRiseTime = _plan.Delay;
                start = new Vector3(_plan.Point.x, start.y, _plan.Point.z);
            }

            // 루트: +Z=전진·+X=우측 — 루트 자식 파티클(앞물·꼬리·스밈)의 방출 형상이 복도에 정렬된다
            transform.SetPositionAndRotation(start, Quaternion.LookRotation(_dir, Vector3.up));
            _right = Vector3.Cross(Vector3.up, _dir);
            // 로브·물몸: 메시 +Z→up·+Y→dir(§4.2-6) — LookRotation(up, dir)
            _baseRot = Quaternion.LookRotation(Vector3.up, _dir) * Quaternion.Euler(_lobeExtraEuler);

            if (_bodyMaterial != null)
            {
                _ownedMaterial = new Material(_bodyMaterial); // 인스턴스 1장 공유 — 원본 불변(ThornRiseEffect 선례)
                if (_ownedMaterial.HasProperty(BaseColorId)) _ownedMaterial.SetColor(BaseColorId, tint);
                if (_ownedMaterial.HasProperty(ColorId)) _ownedMaterial.SetColor(ColorId, tint);
                if (_lobeMesh != null && _ownedMaterial.HasProperty(HeightOsId))
                {
                    _ownedMaterial.SetFloat(HeightOsId, _lobeMesh.bounds.size.z); // hOS = posOS.z / 높이(바닥 0 → 립 1)
                }
                if (_ownedMaterial.HasProperty(DissolveId)) _ownedMaterial.SetFloat(DissolveId, 0f);
            }

            // 립 첨단 정렬: 로브 전진 오프셋 = −frontLead → 중앙 로브 립 첨단 = 루트 = 판정 전선.
            // 전선을 정하는 것은 중앙 로브가 실제로 쓰는 메시(o3 변형 B 배선 시 깊이 정규화가 달라도 첨단이 전선에서 밀리지 않게)
            Mesh centerMesh = _lobeMeshAlt != null ? _lobeMeshAlt : _lobeMesh;
            float meshForward = centerMesh != null ? centerMesh.bounds.max.y : 0f;
            float meshWidth = _lobeMesh != null ? _lobeMesh.bounds.size.x : 0f;
            _frontLeadResolved = _frontLead >= 0f
                ? _frontLead
                : meshForward * _lobeScale * _lobeScaleXYZ.y;
            _frontWidth = meshWidth * _lobeScale * _lobeScaleXYZ.x + Mathf.Max(0, _lobeCount - 1) * _lobeSpacing;

            _inkGradient = BuildInkGradient();
            BuildLobes();
            BuildSwell();
            BuildParticles();

            _traveled = 0f;
            _creepLeft = _sinkCreep;
            _frontStarted = false;
            _firstSprayDone = false;
            _beginTime = Time.time;
            _phaseStart = _beginTime;
            _phase = Phase.Rise;

            // 첫 자세를 먼저 잡고 재생 — rateOverDistance가 초기 배치 점프를 이동 거리로 세지 않게
            ApplyBody(0f, _riseHeightFrom, _riseWidthFrom, _risePitchFrom, _riseBury, _riseSwellHeightFrom, 0f);
            Restart(_sheet);
            Restart(_seep);
            for (int i = 0; i < _lobes.Count; i++) Restart(_lobes[i].Spray);
            if (_seep != null) _seep.Emit(_riseSeepBurst); // 「물이 먼저 바닥에 스며 나온다」

#if UNITY_EDITOR
            Debug.Log($"[WaterWaveEffect] frontLead={_frontLeadResolved:F3}m frontWidth={_frontWidth:F2}m " +
                      $"travel={_travelDistance:F2}m speed={_runSpeed:F2} rise={_runRiseTime:F2}s plan={(_plan != null)}", this);
#endif
        }

        private void Update()
        {
            if (_phase == Phase.Idle || _phase == Phase.Done) return;
            float now = Time.time;
            float t = now - _phaseStart;
            float age = now - _beginTime; // 오실레이터는 연속 시계(위상 단절 없음)

            float height = 1f, width = 1f, pitch = 0f, bury = 0f, swellHeight = 1f, oscEnv = 1f;

            switch (_phase)
            {
                case Phase.Rise: // 수면이 부풀어 마루가 선다 — 潤下의 느긋한 형성(루트 정지)
                {
                    float k = Mathf.Clamp01(t / Mathf.Max(0.01f, _runRiseTime));
                    float e = 1f - (1f - k) * (1f - k) * (1f - k); // EaseOutCubic
                    height = Mathf.Lerp(_riseHeightFrom, 1f, e);
                    pitch = Mathf.Lerp(_risePitchFrom, 0f, e);
                    bury = _riseBury * (1f - e);
                    width = Mathf.Lerp(_riseWidthFrom, 1f, e);
                    swellHeight = Mathf.Lerp(_riseSwellHeightFrom, 1f, e);
                    oscEnv = e;

                    if (!_frontStarted && k >= _frontStartK)
                    {
                        _frontStarted = true;
                        Restart(_front);
                    }
                    if (!_firstSprayDone && k >= _firstSprayK)
                    {
                        _firstSprayDone = true;
                        for (int i = 0; i < _lobes.Count; i++)
                        {
                            if (_lobes[i].Spray != null) _lobes[i].Spray.Emit(_firstSprayBurst);
                        }
                    }
                    if (k >= 1f) { _phase = Phase.Advance; _phaseStart = now; }
                    break;
                }
                case Phase.Advance: // 계획 속도 그대로 밀려간다 — 전선 도달 = 피해 시각
                {
                    float step = _runSpeed * Time.deltaTime;
                    transform.position += _dir * step;
                    _traveled += step;
                    if (_traveled >= _travelDistance) EnterSink(now);
                    break;
                }
                case Phase.Sink: // 끝에서 퍼지며 무너지고 갈라져 스민다 — 남는 것 없음(잔존·발광 금지)
                {
                    float k = Mathf.Clamp01(t / Mathf.Max(0.01f, _sinkTime));
                    // 전진은 (1−k)² 감쇠, 누적 _sinkCreep 상한 — 판정 길이 밖 지면을 물이 훑지 않는다
                    float want = _runSpeed * (1f - k) * (1f - k) * Time.deltaTime;
                    float creep = Mathf.Min(want, _creepLeft);
                    _creepLeft -= creep;
                    transform.position += _dir * creep;

                    height = Mathf.Lerp(1f, _sinkHeightTo, k * k);
                    width = Mathf.Lerp(1f, _sinkWidthGrow, k);
                    pitch = Mathf.Lerp(0f, _sinkPitch, k);
                    swellHeight = Mathf.Lerp(1f, _sinkHeightTo, k * k);
                    oscEnv = 1f - k;
                    if (_ownedMaterial != null) _ownedMaterial.SetFloat(DissolveId, k);

                    if (k >= 1f)
                    {
                        Finish();
                        return;
                    }
                    break;
                }
            }

            ApplyBody(age, height, width, pitch, bury, swellHeight, oscEnv);
        }

        // 로브·물몸의 위치·회전·배율 — 루트 기준 오프셋 + 오실레이터(env로 형성·소멸 구간에 서서히 섞인다)
        private void ApplyBody(float age, float height, float width, float pitch, float bury, float swellHeight, float env)
        {
            const float twoPi = Mathf.PI * 2f;
            Vector3 root = transform.position;
            bool advancing = _phase == Phase.Advance;

            for (int i = 0; i < _lobes.Count; i++)
            {
                Lobe lobe = _lobes[i];
                if (lobe.Tr == null) continue;

                // 부서짐 주기: 피크 → 립 붕괴(진입 에지에서 물보라 버스트) → 회복
                float bp = Mathf.Repeat(age / _breakPeriod + lobe.BreakOffset, 1f);
                int seg = bp < _breakRiseFrac ? 0 : 1;
                if (advancing && seg == 1 && lobe.BreakSeg == 0 && lobe.Spray != null) lobe.Spray.Emit(_breakBurst);
                lobe.BreakSeg = seg;

                float heave = 1f + _heaveAmp * Mathf.Sin(twoPi * _heaveHz * age + lobe.Phase) * env;
                float breakMul = Mathf.Lerp(1f, BreakCurve(bp), env);
                float lobeHeight = height * heave * breakMul;
                float lobePitch = pitch + _pitchAmp * Mathf.Sin(twoPi * _pitchHz * age + lobe.Phase + _pitchPhaseLag) * env;
                float sway = _swayAmp * Mathf.Sin(twoPi * _swayHz * age + lobe.Phase) * env;

                Vector3 pos = root + _right * (lobe.Lateral + sway) + _dir * lobe.Forward - Vector3.up * bury;
                // 요=월드 up 축 / 피치=로컬 X(폭축) — 양(+)이면 립이 앞으로 기운다(AngleAxis(+θ, right)는 상단을 −Y로 보내므로 부호 반전)
                Quaternion rot = Quaternion.AngleAxis(lobe.Yaw, Vector3.up) * _baseRot
                               * Quaternion.AngleAxis(-lobePitch, Vector3.right);
                lobe.Tr.SetPositionAndRotation(pos, rot);
                lobe.Tr.localScale = new Vector3(lobe.BaseScale.x * width, lobe.BaseScale.y, lobe.BaseScale.z * lobeHeight);
            }

            if (_swell != null)
            {
                float breath = 1f + _swellBreath * Mathf.Sin(twoPi * _swellBreathHz * age) * env;
                _swell.SetPositionAndRotation(root - _dir * _swellLag - Vector3.up * _swellDrop, _baseRot);
                _swell.localScale = new Vector3(_swellWidth, _swellDepth, _swellHeight * swellHeight * breath);
            }
        }

        private float BreakCurve(float bp)
        {
            if (bp < _breakRiseFrac)
                return Mathf.Lerp(1f, _breakPeak, bp / Mathf.Max(_breakRiseFrac, Epsilon));
            if (bp < _breakDipFrac)
                return Mathf.Lerp(_breakPeak, _breakDip, (bp - _breakRiseFrac) / Mathf.Max(_breakDipFrac - _breakRiseFrac, Epsilon));
            return Mathf.Lerp(_breakDip, 1f, (bp - _breakDipFrac) / Mathf.Max(1f - _breakDipFrac, Epsilon));
        }

        private void EnterSink(float now)
        {
            _phase = Phase.Sink;
            _phaseStart = now;
            StopEmitting(_front);
            StopEmitting(_sheet);
            for (int i = 0; i < _lobes.Count; i++) StopEmitting(_lobes[i].Spray);
            if (_seep != null) _seep.Emit(_seepBurst); // 소멸 스밈 — 낮게 퍼지며 먹빛으로
        }

        private void Finish()
        {
            for (int i = 0; i < _lobes.Count; i++)
            {
                if (_lobes[i].Renderer != null) _lobes[i].Renderer.enabled = false;
            }
            if (_swellRenderer != null) _swellRenderer.enabled = false;
            _phase = Phase.Done;
            Destroy(gameObject, _tailGrace); // 잔여 파티클(≤ 스밈·꼬리 수명) 뒤 정리 — 잔존 0
        }

        private void OnDestroy()
        {
            if (_ownedMaterial != null) Destroy(_ownedMaterial);
        }

        // ---- 구성 ----

        private void BuildLobes()
        {
            if (_lobeMesh == null) return;
            int n = Mathf.Max(1, _lobeCount);
            int center = (n - 1) / 2;
            float half = (n - 1) * 0.5f;

            for (int i = 0; i < n; i++)
            {
                // 중앙 로브만 변형 B(있으면) — 립 앵커·방출 폭은 그 로브가 실제로 쓰는 메시의 bounds로 잡는다(_frontLead 산출과 동일 메시)
                Mesh mesh = (i == center && _lobeMeshAlt != null) ? _lobeMeshAlt : _lobeMesh;
                Bounds mb = mesh.bounds;
                float jitter = Random.Range(1f - _lobeScaleJitter, 1f + _lobeScaleJitter);
                var lobe = new Lobe
                {
                    Lateral = (i - half) * _lobeSpacing,
                    Forward = -_frontLeadResolved - (i == center ? 0f : _lobeStagger),
                    Yaw = Random.Range(-_lobeYawJitter, _lobeYawJitter),
                    BaseScale = _lobeScaleXYZ * (_lobeScale * jitter),
                    Phase = Random.Range(0f, Mathf.PI * 2f),
                    BreakOffset = (float)i / n,
                    BreakSeg = 0,
                };

                var go = new GameObject("Lobe" + i);
                go.transform.SetParent(transform, false); // 수명 동반 — 위치는 매 프레임 월드로 구동
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                lobe.Renderer = go.AddComponent<MeshRenderer>();
                ConfigureBodyRenderer(lobe.Renderer);
                lobe.Tr = go.transform;

                // 립 물보라 — 립 위치(메시 로컬: 폭 0·전진 max.y×anchor·높이 size.z×anchor), 로브 배율을 그대로 따라간다
                Vector3 lip = new Vector3(0f, mb.max.y * _lipAnchor.x, mb.size.z * _lipAnchor.y);
                lobe.Spray = CreateSystem("LipSpray", go.transform, lip, _sprayBrightness, _sprayAlpha, _sprayMax,
                    ParticleSystemRenderMode.Stretch, _sprayPaperMix);
                ConfigureLipSpray(lobe.Spray, mb.size.x);

                _lobes.Add(lobe);
            }
        }

        private void BuildSwell()
        {
            if (_swellMesh == null) return;
            var go = new GameObject("Swell");
            go.transform.SetParent(transform, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = _swellMesh;
            _swellRenderer = go.AddComponent<MeshRenderer>();
            ConfigureBodyRenderer(_swellRenderer);
            _swell = go.transform;
        }

        private void ConfigureBodyRenderer(MeshRenderer renderer)
        {
            renderer.sharedMaterial = _ownedMaterial; // null이면 마젠타 — 배선 누락이 눈에 보이게
            renderer.shadowCastingMode = ShadowCastingMode.Off; // 고체 그림자 제거 — 물은 그림자를 드리우지 않는다
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        // 루트 자식 파티클 3종 — 앞물(전선 앞)·먹자국 꼬리(루트 뒤)·바닥 스밈(루트). 루트 로컬 +Z=전진·+X=우측·+Y=위
        private void BuildParticles()
        {
            _front = CreateSystem("Front", transform, new Vector3(0f, FrontHover, _frontAhead),
                _frontBrightness, _frontAlpha, _frontMax, ParticleSystemRenderMode.Billboard);
            {
                var main = _front.main;
                main.startSpeed = _frontSpeed; // 박스 +Z(=루트 전진) 방향으로 밀려난다
                main.startSize = new ParticleSystem.MinMaxCurve(_frontSize.x, _frontSize.y);
                main.startLifetime = _frontLife;
                var emission = _front.emission;
                emission.rateOverTime = 0f;
                emission.rateOverDistance = _frontRatePerMeter;
                var shape = _front.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(_frontWidth, EmitterThickness, FrontBoxDepth);
            }

            _sheet = CreateSystem("Sheet", transform, new Vector3(0f, GroundHover, -_sheetBack),
                _sheetBrightness, _sheetAlpha, _sheetMax, ParticleSystemRenderMode.HorizontalBillboard);
            {
                var main = _sheet.main;
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(_sheetSize.x, _sheetSize.y);
                main.startLifetime = _sheetLife;
                var emission = _sheet.emission;
                emission.rateOverTime = 0f;
                emission.rateOverDistance = _sheetRatePerMeter;
                var shape = _sheet.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(_frontWidth, 0f, SheetBoxDepth);
                SetGrowth(_sheet, _sheetGrow);
            }

            _seep = CreateSystem("Seep", transform, new Vector3(0f, GroundHover, 0f),
                _seepBrightness, _seepAlpha, _seepMax, ParticleSystemRenderMode.HorizontalBillboard);
            {
                var main = _seep.main;
                main.startSpeed = _seepSpeed; // 원판 방사
                main.startSize = 1f;          // 크기는 sizeOverLifetime이 소유
                main.startLifetime = _seepLife;
                var emission = _seep.emission;
                emission.rateOverTime = 0f;   // 코드 Emit만
                emission.rateOverDistance = 0f;
                var shape = _seep.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = Mathf.Max(0.05f, _frontWidth * 0.5f * _seepRadiusMul);
                shape.radiusThickness = 1f;
                shape.rotation = new Vector3(90f, 0f, 0f); // 원판(XY)을 지면(XZ)에 눕힌다
                var limit = _seep.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.separateAxes = false;
                limit.limit = SeepLimitSpeed;
                limit.dampen = _seepDampen; // 번지다 멎는다
                SetGrowth(_seep, _seepGrow);
            }
        }

        // 립 물보라 — 전방+상승 속도(World), 중력 낙하, 스트레치 빌보드(물보라 실). 방출은 로브 이동 거리 기반
        private void ConfigureLipSpray(ParticleSystem ps, float meshWidth)
        {
            var main = ps.main;
            main.startSpeed = 0f; // 속도는 velocityOverLifetime(World)이 소유
            main.startSize = new ParticleSystem.MinMaxCurve(_spraySize.x, _spraySize.y);
            main.startLifetime = new ParticleSystem.MinMaxCurve(_sprayLife.x, _sprayLife.y);
            main.gravityModifier = _sprayGravity;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = _sprayRatePerMeter;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(meshWidth * LipBoxWidthMul, EmitterThickness, LipBoxDepth); // 메시 로컬 — 로브 배율 상속

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            // 세 축 곡선은 같은 모드여야 한다(Unity: "Particle Velocity curves must all be in the same mode") — 전부 TwoConstants
            velocity.x = new ParticleSystem.MinMaxCurve(_dir.x * _sprayForward, _dir.x * _sprayForward);
            velocity.y = new ParticleSystem.MinMaxCurve(_sprayUp.x, _sprayUp.y);
            velocity.z = new ParticleSystem.MinMaxCurve(_dir.z * _sprayForward, _dir.z * _sprayForward);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.velocityScale = _sprayStretchVelocity;
            renderer.lengthScale = _sprayStretchLength;
        }

        // 공통 파티클 골격 — World 시뮬·색 = tint × 명도(a=alpha) → 먹빛 침전·InkSpray 재질·정점 스트림 계약·그림자 없음
        private ParticleSystem CreateSystem(string name, Transform parent, Vector3 localPos,
            float brightness, float alpha, int maxParticles, ParticleSystemRenderMode mode, float paperMix = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // 재생 시점은 코드가 소유
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true; // 방출 시간은 Stop이 통제
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 뿌려진 물은 세상에 남는다(지면 흔적)
            main.scalingMode = ParticleSystemScalingMode.Shape; // 부모 배율은 방출 형상에만(입자 크기는 m 그대로)
            main.maxParticles = maxParticles;
            main.startColor = new ParticleSystem.MinMaxGradient(DeriveColor(brightness, alpha, paperMix));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(_inkGradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mode;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (_sprayMaterial != null) renderer.sharedMaterial = _sprayMaterial; // 비우면 기본 파티클 재질(G1 급행 허용)
            renderer.SetActiveVertexStreams(SprayStreams);
            return ps;
        }

        // 색 파생: 팔레트 tint × 명도 스칼라 — 두 번째 색 출처 없음(백색·시안 포화 불가: 현색 ×1.2 = 최대 채널 0.43)
        private Color DeriveColor(float brightness, float alpha, float paperMix = 0f)
        {
            Color c = new Color(_tint.r * brightness, _tint.g * brightness, _tint.b * brightness, alpha);
            if (paperMix <= 0f) return c;
            // 소지 혼합(역번짐) — 곱셈만으로는 상한이 tint를 못 넘어 포말이 파면보다 어두워진다(현색 ×1.2 = 0.43 < 파면 0.47).
            // lerp이므로 상한이 소지색으로 하드 클램프되어 백색 포화는 불가하고, 알파는 보존한다
            Color p = Color.Lerp(c, _paperColor, paperMix);
            p.a = alpha;
            return p;
        }

        // 백(startColor 그대로) → 먹빛 침전(무채) — 알파는 페이드(FlameJetEffect 선례)
        private Gradient BuildInkGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(_inkColor, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(InkAlphaMid, InkAlphaMidTime),
                    new GradientAlphaKey(0f, 1f),
                });
            return gradient;
        }

        private static void SetGrowth(ParticleSystem ps, Vector2 fromTo)
        {
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            float peak = Mathf.Max(fromTo.x, fromTo.y, Epsilon);
            sol.size = new ParticleSystem.MinMaxCurve(peak,
                AnimationCurve.Linear(0f, fromTo.x / peak, 1f, fromTo.y / peak));
        }

        private static void Restart(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.Simulate(0f, true, true);
            ps.Play(true);
        }

        private static void StopEmitting(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); // 잔여 입자 자연 소멸
        }

#if UNITY_EDITOR
        // 판정 전선 대조 — 루트에 반폭 선분(계획 Radius, 없으면 SpellBook 오 1.5). 중앙 립 첨단이 이 선 위에 있어야 한다
        private void OnDrawGizmos()
        {
            if (!_debugFront || _phase == Phase.Idle || _phase == Phase.Done) return;
            float halfWidth = _plan != null && _plan.Radius > 0f ? _plan.Radius : DebugHalfWidthFallback;
            Gizmos.color = _tint;
            Vector3 p = transform.position;
            Gizmos.DrawLine(p - _right * halfWidth, p + _right * halfWidth);
            Gizmos.DrawLine(p, p + Vector3.up * 0.5f);
        }
#endif
    }
}
