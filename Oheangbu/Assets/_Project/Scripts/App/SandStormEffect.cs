using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App
{
    // [SPEC-SPELL-FX-REWORK §3.5 — 모 「직선 경로 모래폭풍(즉발)」] 전용 구현(#138 — GroundWaveEffect와 완전 분리, FlameJetEffect 선례).
    // 문법 = 「낮게 구르는 모래 벽(haboob 전선)」:
    //   형성(Rise — 발치에서 로브 벽이 매장→상승·응결) → 전진(Advance — 계획 속도 등속, 앞머리 퍼프(Roll)·알갱이(Grain)·
    //   후미 꼬리(Haze)·먹빛 자갈(Chunk)이 World 공간에 남는다) → 풀림(Collapse — 가장자리부터 바스라져 알파로 옅어짐,
    //   명도 침전은 _valueSettle까지만 — 오염 V0.42 위) → 여운(Settle) → 잔존 0.
    // 판정 동기(#141 · AREA-SHAPES §3): AreaImpactPlan(Point·Direction·Length·Speed·Delay)을 읽기만 한다 —
    //   로브 A 선단 피벗 = 루트 = 판정 전선. face(t) = Point + dir·Speed·(t − Delay)가 적별 착탄 Delay + along/Speed와 같은 시계.
    //   직렬값은 감사 씬 폴백(REWORK §8-2). 판정 코드(CombatLoopWiring.ResolvePathAttack) 무접촉.
    // 색 = 팔레트 tint 하나 — 로브 재질 _BaseColor, 입자 startColor(tint × 명도 × α 범위). PatternEffectLifetime.TintHierarchy는
    //   경유하지 않는다(startColor α를 1로 평탄화하는 공용 경로 — 감사 원인 ⑥). 자갈만 먹빛 무채 _inkColor(REWORK §8-1 FlameJet 선례).
    // 발광 0(Light 없음 — 토는 「오염은 빛나지 않는다」 계열) · 텍스처 0(InkDust·InkSpray 절차 노이즈) · 리깅 0.
    // 인식·판정·필세 무접촉: App 계층만 — Combat·Spellcraft·Drawing·Recognition 무수정.
    public sealed class SandStormEffect : SpellSequenceEffect
    {
        [Header("자원 [TEST] — 로브 메시(Generated/Mo/SandFront_Mo: 높이+Z·전진+Y·선단 바닥 피벗·폭 1.0 정규) · 전용 재질. 메시/재질 비움=로브 생략(입자만)")]
        [SerializeField] private Mesh _frontMesh;
        [SerializeField] private Material _frontMaterial;   // M_SpellBodySand(Oheangbu/InkDust) — 인스턴스 1장(색·_Erode·_Value)
        [SerializeField] private Material _dustMaterial;    // M_InkDust(Oheangbu/InkSpray) — Roll·Grain·Haze 공용(공유, 비인스턴스)
        [SerializeField] private Mesh _chunkMesh;           // Boulder_Ma 서브메시 재사용(#138 허용면) — 비우면 큐브 폴백 또는 계층 생략
        [SerializeField] private Material _chunkMaterial;   // M_SpellBodySpike(공용·무변경) — 정점색 곱
        [SerializeField] private bool _chunkUseCubeFallback = false;
        [SerializeField] private Vector3 _lobeExtraEuler;   // 반입 메시 축 미세 보정(GroundWave _crestExtraEuler 선례 — 리컴파일 없이)
        [SerializeField] private bool _depthPrepass = true; // InkDust DustDepth 패스 — 겹침 컷 비교용 토글(게이트 ⑤)
        [SerializeField] private Color _inkColor = new Color(0.16f, 0.15f, 0.13f); // 먹빛 무채(자갈) — REWORK §8-1

        [Header("시계 [TEST §7] — 계획(AreaImpactPlan)이 있으면 Delay/Speed/Length가 정본, 직렬값=감사 씬 폴백")]
        [SerializeField, Min(0.01f)] private float _riseTime = 0.4f;
        [SerializeField, Min(0.1f)] private float _speed = 7f;
        [SerializeField, Min(0.1f)] private float _travelLength = 12f;
        [SerializeField, Min(0.01f)] private float _collapseTime = 0.6f;
        [SerializeField, Min(0f)] private float _settleTime = 1.9f;   // Haze 최장 수명 여운 뒤 파괴(총 ≈4.61s)
        [SerializeField, Min(0f)] private float _overshoot = 2.5f;    // 무계획: 목표를 지나쳐 훑는 여분
        [SerializeField, Min(1f)] private float _maxRange = 14f;      // 무계획 상한

        [Header("지면 [TEST] — 무계획 폴백: 문양 낙차 근사 → 하향 레이캐스트(대상·자기·Vitals·CharacterController 계층·트리거 제외)")]
        [SerializeField, Min(0f)] private float _groundDrop = 1.2f;
        [SerializeField, Min(0f)] private float _rayUp = 1.5f;
        [SerializeField, Min(0f)] private float _rayDown = 4f;
        [SerializeField] private float _groundLift = 0.02f;

        [Header("형상 [TEST] — 로브 A(선단=판정 전선) / B·C(후방 엇갈림). 스케일 x=폭·y=깊이·z=높이(메시 정규 1.0×0.38×0.38)")]
        [SerializeField] private Vector3 _frontScale = new Vector3(2.8f, 2.8f, 3.0f);
        [SerializeField, Min(0f)] private float _lobeBScale = 0.7f;
        [SerializeField] private Vector3 _lobeBOffset = new Vector3(-0.6f, 0f, -0.75f);
        [SerializeField] private float _lobeBYaw = -12f;
        [SerializeField, Min(0f)] private float _lobeCScale = 0.6f;
        [SerializeField] private Vector3 _lobeCOffset = new Vector3(0.7f, 0f, -0.9f);
        [SerializeField] private float _lobeCYaw = 10f;
        [SerializeField, Min(0f)] private float _buriedDepth = 1.2f;   // 형성 시작 매장 깊이(m)
        [SerializeField, Min(0f)] private float _groundSink = 0.12f;   // 접지 매장 — 열린 바닥 은폐(Mobile 프로파일 깊이 페이드 대체)
        [SerializeField, Min(0f)] private float _bobAmplitude = 0.05f;
        [SerializeField, Min(0f)] private float _bobFrequency = 1.6f;  // Hz
        [SerializeField, Min(0f)] private float _yawJitterDeg = 3f;
        [SerializeField, Min(0f)] private float _yawJitterFrequency = 0.9f; // Hz
        [SerializeField, Range(0f, 0.5f)] private float _heightPulse = 0.08f;
        [SerializeField, Min(0f)] private float _heightPulseFrequency = 1.1f; // Hz
        [SerializeField, Range(0f, 1f)] private float _erodeStart = 1.0f;    // 형성 시작(먼지)
        [SerializeField, Range(0f, 1f)] private float _erodeFormed = 0.35f;  // 응결 완료
        [SerializeField, Range(0f, 1f)] private float _valueSettle = 0.85f;  // 풀림 명도 침전 하한(오염 V0.42 위 유지)
        [SerializeField, Min(0f)] private float _collapseDrop = 0.25f;       // 풀림 하강(m)
        [SerializeField, Range(0f, 1f)] private float _collapseHeight = 0.6f; // 풀림 높이 배율

        [Header("Roll 앞머리 퍼프 [TEST] — 방출점 전방 +z(연출 선행 ≤0.2m · REWORK §8-9) · inherit+drag로 벽 위로 뒤로 말린다")]
        [SerializeField, Min(0f)] private float _rollRatePerMeter = 6f;
        [SerializeField, Min(0f)] private float _rollRate = 30f;
        [SerializeField, Min(0)] private int _rollBurst = 10;
        [SerializeField, Min(0f)] private float _rollBurstDelay = 0.05f;
        [SerializeField] private Vector3 _rollOffset = new Vector3(0f, 0.35f, 0.2f);
        [SerializeField] private Vector2 _rollSpeed = new Vector2(0.8f, 1.8f);
        [SerializeField] private float _rollPitch = -25f;             // cone 상향각(음수=위)
        [SerializeField, Range(0f, 90f)] private float _rollConeAngle = 30f;
        [SerializeField, Min(0f)] private float _rollConeRadius = 0.9f;
        [SerializeField, Range(0f, 1f)] private float _rollInherit = 0.35f;
        [SerializeField, Min(0f)] private float _rollDrag = 1.5f;
        [SerializeField, Min(0f)] private float _rollNoise = 0.45f;
        [SerializeField, Min(0f)] private float _rollNoiseFreq = 0.55f;
        [SerializeField, Min(0f)] private float _rollNoiseScroll = 0.6f;
        [SerializeField] private Vector2 _rollLife = new Vector2(0.7f, 1.1f);
        [SerializeField] private Vector2 _rollSize = new Vector2(0.6f, 1.1f);
        [SerializeField] private Vector2 _rollAlpha = new Vector2(0.25f, 0.45f);
        [SerializeField, Min(0f)] private float _rollSpin = 70f;     // °/s
        [SerializeField] private float _rollGravity = 0.15f;
        [SerializeField, Min(1)] private int _rollMax = 120;
        [SerializeField] private AnimationCurve _rollSizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.55f), new Keyframe(0.35f, 1f), new Keyframe(1f, 1.35f));
        [SerializeField] private Gradient _rollOverLife = new Gradient
        {
            // 색키 = 명도만(백→0.92→0.85, startColor=tint와 곱) · 알파 = 피어남→체류→소멸
            colorKeys = new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(0.92f, 0.92f, 0.92f), 0.6f),
                new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 1f),
            },
            alphaKeys = new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.18f),
                new GradientAlphaKey(0.85f, 0.6f),
                new GradientAlphaKey(0f, 1f),
            },
        };

        [Header("Grain 알갱이 [TEST] — 전상방 스트레치 빌보드·중력 낙하(튀어 올라 떨어진다)")]
        [SerializeField, Min(0f)] private float _grainRatePerMeter = 12f;
        [SerializeField, Min(0f)] private float _grainRate = 40f;
        [SerializeField, Min(0)] private int _grainBurst = 16;
        [SerializeField, Min(0f)] private float _grainBurstDelay = 0.10f;
        [SerializeField] private Vector2 _grainSize = new Vector2(0.03f, 0.09f);
        [SerializeField] private Vector2 _grainSpeed = new Vector2(2.5f, 5f);
        [SerializeField, Range(0f, 89f)] private float _grainUpAngle = 20f;
        [SerializeField] private float _grainGravity = 0.6f;
        [SerializeField] private Vector2 _grainLife = new Vector2(0.35f, 0.7f);
        [SerializeField] private Vector2 _grainAlpha = new Vector2(0.6f, 0.9f);
        [SerializeField, Range(0f, 1f)] private float _grainBrightness = 0.75f;
        [SerializeField] private Vector3 _grainBox = new Vector3(2.5f, 0.3f, 0.2f);
        [SerializeField] private Vector3 _grainBoxOffset = new Vector3(0f, 0.15f, 0f);
        [SerializeField, Min(0f)] private float _grainStretch = 0.2f;  // velocityScale
        [SerializeField, Min(0f)] private float _grainLength = 1.4f;   // lengthScale
        [SerializeField, Min(1)] private int _grainMax = 300;
        [SerializeField] private Gradient _grainOverLife = new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) },
        };

        [Header("Haze 후미 꼬리 [TEST] — 후상방 느린 빌보드·Noise 표류 · 지나간 복도에 남았다가 옅어진다(≤1.8s — 장판 아님)")]
        [SerializeField, Min(0f)] private float _hazeRatePerMeter = 6f;
        [SerializeField, Min(0f)] private float _hazeRiseRate = 4f;    // 형성 동안만(전진 개시에 0)
        [SerializeField, Min(0)] private int _hazeBurst = 6;
        [SerializeField] private Vector2 _hazeSize = new Vector2(0.9f, 1.6f);
        [SerializeField] private Vector2 _hazeSpeed = new Vector2(0.3f, 0.8f);
        [SerializeField] private Vector2 _hazeLife = new Vector2(1.2f, 1.8f);
        [SerializeField] private Vector2 _hazeAlpha = new Vector2(0.2f, 0.35f);
        [SerializeField, Range(0f, 1f)] private float _hazeBrightness = 0.88f;
        [SerializeField, Min(0f)] private float _hazeNoise = 0.3f;
        [SerializeField, Min(0f)] private float _hazeNoiseFreq = 0.4f;
        [SerializeField, Min(0f)] private float _hazeNoiseScroll = 0.2f;
        [SerializeField, Min(0f)] private float _hazeRotSpeed = 25f;  // °/s
        [SerializeField] private Vector3 _hazeBox = new Vector3(2.4f, 0.8f, 0.6f);
        [SerializeField] private Vector3 _hazeBoxOffset = new Vector3(0f, 0.45f, -0.4f);
        [SerializeField] private Vector3 _hazeBoxEuler = new Vector3(-140f, 0f, 0f); // 방출축 +Z → 후상방
        [SerializeField] private float _hazeSortFudge = 0.5f;          // 양수 = 뒤에(먼저) 그린다
        [SerializeField, Min(1)] private int _hazeMax = 160;
        [SerializeField] private AnimationCurve _hazeSizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.7f), new Keyframe(1f, 1.3f));
        [SerializeField] private Gradient _hazeOverLife = new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.25f),
                new GradientAlphaKey(0f, 1f),
            },
        };

        [Header("Chunk 자갈 [TEST] — 먹빛 무채 메시 입자(바이블 「자갈은 무채/먹」)·회전·중력. 동시 ≤_chunkMax")]
        [SerializeField, Min(0f)] private float _chunkRatePerMeter = 1.0f;
        [SerializeField] private Vector2 _chunkSize = new Vector2(0.05f, 0.12f);
        [SerializeField] private Vector2 _chunkSpeed = new Vector2(2f, 4f);
        [SerializeField] private Vector2 _chunkLife = new Vector2(0.5f, 0.7f);
        [SerializeField] private float _chunkGravity = 1.2f;
        [SerializeField, Min(0f)] private float _chunkSpin = 120f;   // °/s
        [SerializeField, Range(0f, 90f)] private float _chunkConeAngle = 30f;
        [SerializeField, Min(0f)] private float _chunkConeRadius = 0.5f;
        [SerializeField] private float _chunkPitch = -45f;
        [SerializeField, Min(1)] private int _chunkMax = 4;

        private enum Phase { None, Rise, Advance, Collapse, Done }

        private struct Lobe
        {
            public Transform Tr;
            public MeshRenderer Renderer;
            public Vector3 Offset;    // 루트-로컬 배치 오프셋(x 좌우 · z 전후 · y 추가 높이)
            public float BaseYaw;
            public Vector3 BaseScale;
            public float Phase;       // 봅·지터 위상차(rad)
        }

        private const int MaxLobes = 3;
        private const float LobePhaseStep = 2.1f;   // 로브별 위상차(rad) — 기술 상수(룩 노브 아님)
        private const float TwoPi = Mathf.PI * 2f;
        private const int GroundHitBuffer = 16;
        // InkDust DustDepth 패스의 LightMode — SetShaderPassEnabled는 패스 Name이 아니라 태그를 받는다(BrushStrokeFeedAdapter L59 선례).
        // 이 셰이더로는 미실측 — 게이트 ⑤(_depthPrepass=false 비교컷)에서 안 꺼지면 패스 Name "DustDepth"로 교체(둘 중 하나만 유효)
        private const string DepthPassTag = "SRPDefaultUnlit";
        private const string CubeResource = "Cube.fbx"; // 엔진 내장 메시(자갈 폴백)

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ErodeId = Shader.PropertyToID("_Erode");
        private static readonly int ValueId = Shader.PropertyToID("_Value");
        private static readonly RaycastHit[] s_groundHits = new RaycastHit[GroundHitBuffer];

        // InkSpray 정점 스트림 계약 — TEXCOORD0 = (uv.x, uv.y, stableRandom.x, agePercent): 시드 찢김 + 나이 침식
        private static readonly List<ParticleSystemVertexStream> DustStreams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position,
            ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.StableRandomX,
            ParticleSystemVertexStream.AgePercent,
        };

        private readonly Lobe[] _lobes = new Lobe[MaxLobes];
        private int _lobeCount;
        private Quaternion _lobeBaseRot = Quaternion.identity;
        private Material _mat;
        private ParticleSystem _roll;
        private ParticleSystem _grain;
        private ParticleSystem _haze;
        private ParticleSystem _chunk;
        private AreaImpactPlan _plan;
        private Phase _phase = Phase.None;
        private float _phaseStart;
        private Vector3 _dir = Vector3.forward;
        private Color _tint = Color.white;
        private float _runRise;    // 실행 시계 — 계획값 또는 직렬 폴백(직렬 필드는 불변)
        private float _runSpeed;
        private float _runLength;
        private float _traveled;
        private float _advanceEnd; // 전진 지속 시간 정확값(길이/속도) — 풀림 위상식이 이어 붙는 기준
        private bool _rollStarted;
        private bool _grainStarted;
        private bool _emissionStopped;

        // 판정 계획 [SPELL-AREA-SHAPES §3] — Begin 직전에 배선이 넘긴다. 시작점(y 포함)·방향·길이·속도·차오름을 판정과 공유
        public override void SetAreaPlan(AreaImpactPlan plan)
        {
            _plan = plan;
        }

        public override void Begin(Vector3 origin, Transform target, Vector3 fallbackPoint, Color tint)
        {
            // 부모(문양 프리팹)의 수명(AttachBloom 2.2s)·스케일·카메라 회전과 분리 — 벽은 자체 시계·경로 축으로 완주한다
            transform.SetParent(null, true);
            transform.localScale = Vector3.one; // 글자 크기 비의존(어댑터 스케일 상속 차단) — 벽 실크기는 _frontScale이 정본
            _tint = tint;

            // 폴백 기점(감사 씬·프리팹 단독 재생): 문양 아래 지면 — 낙차 근사 뒤 레이캐스트로 접지
            Vector3 goal = target != null ? target.position : fallbackPoint;
            Vector3 start = origin;
            start.y = target != null ? target.position.y : origin.y - _groundDrop;
            if (SnapToGround(start, target, out float groundY)) start.y = groundY;

            Vector3 flat = goal - start;
            flat.y = 0f;
            _dir = flat.sqrMagnitude > 0.001f ? flat.normalized : Vector3.forward;
            // 대상이 있으면 거리+여분, 무대상(감사 스포너·프리팹 단독 재생)은 직렬 _travelLength — 상한 _maxRange
            _runLength = Mathf.Min(_maxRange, target != null ? flat.magnitude + _overshoot : _travelLength);
            _runSpeed = _speed;
            _runRise = _riseTime;

            if (_plan != null)
            {
                // 판정이 정한 복도 그대로 — 전선이 닿는 시각(차오름 + 거리/속도)이 피해 시각과 같아진다(#141)
                Vector3 planDir = new Vector3(_plan.Direction.x, 0f, _plan.Direction.z);
                if (planDir.sqrMagnitude > 0.001f) _dir = planDir.normalized;
                if (_plan.Length > 0f) _runLength = _plan.Length;
                if (_plan.Speed > 0f) _runSpeed = _plan.Speed;
                if (_plan.Delay > 0f) _runRise = _plan.Delay;
                start = _plan.Point; // 시전자 발치 — y 포함(감사 원인 ⑤: GroundWave는 plan.Point.y를 버렸다)
            }

            // 진행축 정렬 — 카메라 요·피치 의존 제거(감사 원인 ④, FlameJet L46 문법). 로컬 +Z = 전진, +Y = 위
            transform.SetPositionAndRotation(start + Vector3.up * _groundLift, Quaternion.LookRotation(_dir, Vector3.up));

            BuildFront();
            BuildRoll();
            BuildGrain();
            BuildHaze();
            if (_chunkMesh != null || _chunkUseCubeFallback) BuildChunk();

            _traveled = 0f;
            _advanceEnd = 0f;
            _rollStarted = false;
            _grainStarted = false;
            _emissionStopped = false;
            _phase = Phase.Rise;
            _phaseStart = Time.time;
        }

        private void Update()
        {
            if (_phase == Phase.None || _phase == Phase.Done) return;
            float t = Time.time - _phaseStart;
            switch (_phase)
            {
                case Phase.Rise: TickRise(t); break;
                case Phase.Advance: TickAdvance(t); break;
                case Phase.Collapse: TickCollapse(t); break;
            }
        }

        // 형성 — 로브가 땅에서 밀려 올라오며(EaseOutQuad) 먼지에서 덩어리로 응결(_Erode 1→0.35). 루트 정지 = 판정 「아직 피격 없음」
        private void TickRise(float t)
        {
            float rise = Mathf.Max(0.01f, _runRise);
            float k = Mathf.Clamp01(t / rise);
            float ease = 1f - (1f - k) * (1f - k);
            float y = Mathf.Lerp(-_buriedDepth, -_groundSink, ease);
            for (int i = 0; i < _lobeCount; i++) PlaceLobe(ref _lobes[i], y, 0f, 1f);
            if (_mat != null) _mat.SetFloat(ErodeId, Mathf.Lerp(_erodeStart, _erodeFormed, Mathf.SmoothStep(0f, 1f, k)));

            if (!_rollStarted && t >= _rollBurstDelay) StartRollAndHaze();
            if (!_grainStarted && t >= _grainBurstDelay) StartGrain();

            if (k >= 1f)
            {
                // 차오름(Delay)이 버스트 지연보다 짧아도 계층이 통째로 빠지지 않는다 — 미개시 시스템 강제 개시
                if (!_rollStarted) StartRollAndHaze();
                if (!_grainStarted) StartGrain();

                // 전진 개시 — Haze의 시간 방출(형성 전용)을 끊고 거리 방출만 남긴다 · 자갈은 전진에서만 구른다
                var hazeEmission = _haze.emission;
                hazeEmission.rateOverTime = 0f;
                if (_chunk != null) _chunk.Play();
                _phase = Phase.Advance;
                _phaseStart += rise; // Rise 종점 정확값 — 전선 시계가 프레임 초과분만큼 판정보다 뒤지지 않는다
            }
        }

        private void StartRollAndHaze()
        {
            _rollStarted = true;
            _roll.Play();
            _roll.Emit(_rollBurst);
            _haze.Play();
            _haze.Emit(_hazeBurst);
        }

        private void StartGrain()
        {
            _grainStarted = true;
            _grain.Play();
            _grain.Emit(_grainBurst);
        }

        // 전진 — 루트(=로브 A 선단=판정 전선)가 등속으로 밀려간다. 로브는 봅·요 지터·높이 맥동으로 「구르는」 숨을 쉰다
        private void TickAdvance(float t)
        {
            // 시간 기반 적분 — face(t) = Point + dir·Speed·(t − Delay)가 프레임 무관하게 성립(판정 시계와 동일) · 사거리 끝에 정확히 멎는다
            float target = Mathf.Min(_runLength, _runSpeed * t);
            float step = target - _traveled;
            transform.position += _dir * step;
            _traveled = target;

            float ramp = Mathf.Clamp01(t * _bobFrequency); // 형성 종점과 이어지도록 진폭을 첫 주기 동안 올린다
            float w = t * TwoPi;
            for (int i = 0; i < _lobeCount; i++)
            {
                float phase = _lobes[i].Phase;
                float y = -_groundSink + Mathf.Sin(w * _bobFrequency + phase) * _bobAmplitude * ramp;
                float yaw = Mathf.Sin(w * _yawJitterFrequency + phase) * _yawJitterDeg * ramp;
                float h = 1f + Mathf.Sin(w * _heightPulseFrequency + phase) * _heightPulse * ramp;
                PlaceLobe(ref _lobes[i], y, yaw, h);
            }

            if (_traveled >= _runLength - 1e-4f)
            {
                _advanceEnd = _runLength / _runSpeed;
                _phase = Phase.Collapse;
                _phaseStart += _advanceEnd; // 전진 종점 정확값
            }
        }

        // 풀림 — 방출 중단, 가장자리부터 바스라지며(_Erode→1) 알파로 옅어진다. 명도는 _valueSettle까지만(오염 V0.42 위 — 먹 침전 최소)
        private void TickCollapse(float t)
        {
            if (!_emissionStopped)
            {
                _emissionStopped = true;
                StopSystem(_roll);
                StopSystem(_grain);
                StopSystem(_haze);
                StopSystem(_chunk);
            }

            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, _collapseTime));
            if (_mat != null)
            {
                _mat.SetFloat(ErodeId, Mathf.Lerp(_erodeFormed, 1f, k * k));
                _mat.SetFloat(ValueId, Mathf.Lerp(1f, _valueSettle, k));
            }
            // 전진의 봅·요 지터·높이 맥동을 (1−k) 램프로 이어 붙인다 — 위상은 전진 종점(_advanceEnd)에서 연속(첫 프레임 0 리셋 튐 없음)
            float fade = (1f - k) * Mathf.Clamp01(_advanceEnd * _bobFrequency);
            float w = (_advanceEnd + t) * TwoPi;
            float yBase = -_groundSink - _collapseDrop * k;
            float hBase = Mathf.Lerp(1f, _collapseHeight, k);
            for (int i = 0; i < _lobeCount; i++)
            {
                float phase = _lobes[i].Phase;
                float y = yBase + Mathf.Sin(w * _bobFrequency + phase) * _bobAmplitude * fade;
                float yaw = Mathf.Sin(w * _yawJitterFrequency + phase) * _yawJitterDeg * fade;
                float h = hBase * (1f + Mathf.Sin(w * _heightPulseFrequency + phase) * _heightPulse * fade);
                PlaceLobe(ref _lobes[i], y, yaw, h);
            }

            if (k >= 1f)
            {
                for (int i = 0; i < _lobeCount; i++) _lobes[i].Renderer.enabled = false;
                _phase = Phase.Done;
                Destroy(gameObject, _settleTime); // 여운 — 잔여 입자 자연 소멸 뒤 정리(잔존·잔광 0)
            }
        }

        // 지면 앵커(무계획 폴백) — 하향 레이캐스트에서 대상·자기·Vitals·CharacterController 계층을 제외한 최근접(=최상단) 히트.
        // Ground 레이어가 없어(TagManager 실측) 마스크 대신 계층 제외 — REWORK §3.4 고(ThornRiseEffect) 방식과 통일
        private bool SnapToGround(Vector3 p, Transform ignore, out float y)
        {
            y = p.y;
            Vector3 from = p + Vector3.up * _rayUp;
            int hitCount = Physics.RaycastNonAlloc(from, Vector3.down, s_groundHits, _rayUp + _rayDown,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
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
            if (best < 0) return false;
            y = s_groundHits[best].point.y;
            return true;
        }

        // ---- 로브 벽(실체) ----

        // 로브 3본 — 메시 +Z→루트 up·+Y→루트 forward(=계획 방향): LookRotation(up, forward)가 GroundWave L72 규약의 루트-로컬 판.
        // 재질 인스턴스 1장에 tint·_Erode·_Value — TintHierarchy 미경유(색 출처는 여전히 팔레트 tint 하나)
        private void BuildFront()
        {
            _lobeCount = 0;
            if (_frontMesh == null || _frontMaterial == null)
            {
                Debug.LogWarning("[SandStormEffect] _frontMesh/_frontMaterial 미배선 — 로브 생략, 입자 4계층만 재생 " +
                                 "(메시=Generated/Mo/SandFront_Mo Blender 절차 생성, 재질=M_SpellBodySand)", this);
                return;
            }

            _mat = new Material(_frontMaterial);
            _mat.SetColor(BaseColorId, _tint);
            _mat.SetColor(ColorId, _tint);
            _mat.SetFloat(ErodeId, _erodeStart);
            _mat.SetFloat(ValueId, 1f);
            if (!_depthPrepass) _mat.SetShaderPassEnabled(DepthPassTag, false);

            _lobeBaseRot = Quaternion.LookRotation(Vector3.up, Vector3.forward) * Quaternion.Euler(_lobeExtraEuler);
            AddLobe("LobeA", Vector3.zero, 0f, _frontScale, 0f);
            AddLobe("LobeB", _lobeBOffset, _lobeBYaw, _frontScale * _lobeBScale, LobePhaseStep);
            AddLobe("LobeC", _lobeCOffset, _lobeCYaw, _frontScale * _lobeCScale, LobePhaseStep * 2f);
        }

        private void AddLobe(string name, Vector3 offset, float yaw, Vector3 scale, float phase)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _frontMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off; // 먼지는 그림자를 드리우지 않는다(고체감 제거)
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var lobe = new Lobe
            {
                Tr = go.transform,
                Renderer = renderer,
                Offset = offset,
                BaseYaw = yaw,
                BaseScale = scale,
                Phase = phase,
            };
            PlaceLobe(ref lobe, -_buriedDepth, 0f, 1f); // 매장 상태로 시작 — Rise가 끌어올린다
            _lobes[_lobeCount++] = lobe;
        }

        // y = 루트-로컬 접지 높이(매장 음수) / yawJitter = 요 지터(°) / heightMul = 높이(메시 +Z = 스케일 z) 배율
        private void PlaceLobe(ref Lobe lobe, float y, float yawJitter, float heightMul)
        {
            lobe.Tr.localPosition = new Vector3(lobe.Offset.x, lobe.Offset.y + y, lobe.Offset.z);
            lobe.Tr.localRotation = Quaternion.AngleAxis(lobe.BaseYaw + yawJitter, Vector3.up) * _lobeBaseRot;
            lobe.Tr.localScale = new Vector3(lobe.BaseScale.x, lobe.BaseScale.y, lobe.BaseScale.z * heightMul);
        }

        // ---- 입자 4계층 — 전 구성이 여기 있다(코드 구성 PS — 문법의 단일 출처, FlameJet 선례) ----

        // 앞머리 퍼프 — 루트 전방 +0.2m·상 0.35m에서 방출, inherit 0.35 + drag 1.5라 탄생 직후 루트보다 느려져 벽 위로 뒤로 말린다
        // (입자는 영영 루트를 앞서지 않는다 — 연출 선행은 방출점 오프셋만). rateOverDistance는 World 공간·루트 이동에만 반응
        private void BuildRoll()
        {
            _roll = CreateSystem("Roll", _dustMaterial, _rollMax, ParticleSystemRenderMode.Billboard, true);
            var main = _roll.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(_rollLife.x, _rollLife.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(_rollSpeed.x, _rollSpeed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(_rollSize.x, _rollSize.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, TwoPi);
            main.startColor = new ParticleSystem.MinMaxGradient(Derive(1f, _rollAlpha.x), Derive(1f, _rollAlpha.y));
            main.gravityModifier = _rollGravity;

            var emission = _roll.emission;
            emission.enabled = true;
            emission.rateOverTime = _rollRate;
            emission.rateOverDistance = _rollRatePerMeter;

            var shape = _roll.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = _rollConeAngle;
            shape.radius = _rollConeRadius;
            shape.position = _rollOffset;
            shape.rotation = new Vector3(_rollPitch, 0f, 0f); // 로컬 +Z 분사 → 전상방

            var inherit = _roll.inheritVelocity;
            inherit.enabled = true;
            inherit.mode = ParticleSystemInheritVelocityMode.Initial;
            inherit.curve = new ParticleSystem.MinMaxCurve(_rollInherit);

            var limit = _roll.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = new ParticleSystem.MinMaxCurve(_rollDrag);
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;

            var noise = _roll.noise;
            noise.enabled = true;
            noise.strength = _rollNoise;
            noise.frequency = _rollNoiseFreq;
            noise.scrollSpeed = _rollNoiseScroll;
            noise.octaveCount = 2;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var rot = _roll.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = false;
            rot.z = new ParticleSystem.MinMaxCurve(-_rollSpin * Mathf.Deg2Rad, _rollSpin * Mathf.Deg2Rad);

            var sol = _roll.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, _rollSizeCurve);

            var col = _roll.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(_rollOverLife);
        }

        // 알갱이 — 전선 폭의 낮은 상자에서 전상방 20°로 튀어 중력에 떨어지는 스트레치 빌보드(모래가 「굴러간다」는 읽기)
        private void BuildGrain()
        {
            _grain = CreateSystem("Grain", _dustMaterial, _grainMax, ParticleSystemRenderMode.Stretch, true);
            var main = _grain.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(_grainLife.x, _grainLife.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(_grainSpeed.x, _grainSpeed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(_grainSize.x, _grainSize.y);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Derive(_grainBrightness, _grainAlpha.x), Derive(_grainBrightness, _grainAlpha.y));
            main.gravityModifier = _grainGravity;

            var emission = _grain.emission;
            emission.enabled = true;
            emission.rateOverTime = _grainRate;
            emission.rateOverDistance = _grainRatePerMeter;

            var shape = _grain.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = _grainBox;
            shape.position = _grainBoxOffset;
            shape.rotation = new Vector3(-_grainUpAngle, 0f, 0f); // 로컬 +Z 방출 → 전상방

            var col = _grain.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(_grainOverLife);

            var renderer = _grain.GetComponent<ParticleSystemRenderer>();
            renderer.velocityScale = _grainStretch;
            renderer.lengthScale = _grainLength;
        }

        // 후미 꼬리 — 벽 뒤 상자에서 후상방으로 느리게 떠오르며 Noise로 표류. 형성 동안은 시간 방출(_hazeRiseRate), 전진은 거리 방출만
        private void BuildHaze()
        {
            _haze = CreateSystem("Haze", _dustMaterial, _hazeMax, ParticleSystemRenderMode.Billboard, true);
            var main = _haze.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(_hazeLife.x, _hazeLife.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(_hazeSpeed.x, _hazeSpeed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(_hazeSize.x, _hazeSize.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, TwoPi);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Derive(_hazeBrightness, _hazeAlpha.x), Derive(_hazeBrightness, _hazeAlpha.y));
            main.gravityModifier = 0f;

            var emission = _haze.emission;
            emission.enabled = true;
            emission.rateOverTime = _hazeRiseRate;
            emission.rateOverDistance = _hazeRatePerMeter;

            var shape = _haze.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = _hazeBox;
            shape.position = _hazeBoxOffset;
            shape.rotation = _hazeBoxEuler;

            var noise = _haze.noise;
            noise.enabled = true;
            noise.strength = _hazeNoise;
            noise.frequency = _hazeNoiseFreq;
            noise.scrollSpeed = _hazeNoiseScroll;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var rot = _haze.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = false;
            rot.z = new ParticleSystem.MinMaxCurve(-_hazeRotSpeed * Mathf.Deg2Rad, _hazeRotSpeed * Mathf.Deg2Rad);

            var sol = _haze.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, _hazeSizeCurve);

            var col = _haze.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(_hazeOverLife);

            var renderer = _haze.GetComponent<ParticleSystemRenderer>();
            renderer.sortingFudge = _hazeSortFudge;
        }

        // 자갈 — 먹빛 무채 메시 입자(Boulder_Ma 재사용 또는 내장 큐브). 기본 정점 스트림(URP Particles/Unlit) — 재질 인스턴스 없음
        private void BuildChunk()
        {
            Mesh mesh = _chunkMesh != null ? _chunkMesh : Resources.GetBuiltinResource<Mesh>(CubeResource);
            _chunk = CreateSystem("Chunk", _chunkMaterial, _chunkMax, ParticleSystemRenderMode.Mesh, false);
            var renderer = _chunk.GetComponent<ParticleSystemRenderer>();
            renderer.mesh = mesh;

            var main = _chunk.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(_chunkLife.x, _chunkLife.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(_chunkSpeed.x, _chunkSpeed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(_chunkSize.x, _chunkSize.y);
            main.startColor = new ParticleSystem.MinMaxGradient(_inkColor); // 속성색 아님 — 지면 파편은 무채/먹
            main.gravityModifier = _chunkGravity;
            main.startRotation3D = true;
            var fullTurn = new ParticleSystem.MinMaxCurve(0f, TwoPi);
            main.startRotationX = fullTurn;
            main.startRotationY = fullTurn;
            main.startRotationZ = fullTurn;

            var emission = _chunk.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = _chunkRatePerMeter;

            var shape = _chunk.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = _chunkConeAngle;
            shape.radius = _chunkConeRadius;
            shape.rotation = new Vector3(_chunkPitch, 0f, 0f);

            var rot = _chunk.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            var spin = new ParticleSystem.MinMaxCurve(-_chunkSpin * Mathf.Deg2Rad, _chunkSpin * Mathf.Deg2Rad);
            rot.x = spin;
            rot.y = spin;
            rot.z = spin;
        }

        // 공통 파티클 골격 — World 시뮬(지나간 자리에 남는다)·Hierarchy 배율(루트 1)·그림자 없음·거리 정렬·재생 시점은 코드 소유.
        // inkSprayStreams: InkSpray 정점 스트림 계약(시드·나이) — 메시 자갈(URP Particles/Unlit)은 기본 스트림
        private ParticleSystem CreateSystem(string name, Material material, int maxParticles,
            ParticleSystemRenderMode mode, bool inkSprayStreams)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // playOnAwake 잔여 방지 — 재생은 위상이 연다
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true; // 방출 시간은 Stop이 통제
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            // rateOverDistance·inheritVelocity의 속도 출처 = 루트 Transform 이동(Rigidbody 없음 — 암묵 폴백 의존 제거)
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = maxParticles;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mode;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (material != null) renderer.sharedMaterial = material; // 비우면 기본 파티클 재질(FlameJet 선례)
            if (inkSprayStreams) renderer.SetActiveVertexStreams(DustStreams);
            return ps;
        }

        private static void StopSystem(ParticleSystem ps)
        {
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); // 잔여 입자 자연 소멸
        }

        // 색 파생: 팔레트 tint × 명도 스칼라, α = 농담 범위 — 두 번째 색 출처 없음(백색 포화 불가: 최대 = tint × 1.0)
        private Color Derive(float brightness, float alpha)
        {
            return new Color(_tint.r * brightness, _tint.g * brightness, _tint.b * brightness, alpha);
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
