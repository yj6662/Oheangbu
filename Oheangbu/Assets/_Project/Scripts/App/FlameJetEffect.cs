using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App
{
    // [SPELL-FIDELITY §4.2 · SPEC-SPELL-FX-REWORK §3.1.2 — 노 「지정 영역에 화염 방사」] 전용 구현(#138 — 재조합 아님).
    // 문법: 커밋 문양이 시전자 앞에 서고(부모 Bloom — 의례 언어), 그 중심(노즐)에서 판정 계획의 방향으로 불이 「뿜어진다」.
    //
    // 2026-09-04 재설계(예준 지시 + 참고 이미지=화염방사기, DECISIONS #143): §3.1.1의 「불혀 메시 30개 풀 + 항력 제트」를
    // 폐기하고 **연속 분사 파티클 3계층**으로 대체한다. 236tris 강체 30개가 40° 부채꼴에 흩어지면 어떤 밀도로도
    // 「흐름」이 되지 않고 「불덩이를 여러 개 뿌리는」 것으로 읽힌다(자연스러움 감사 노-①). 화염방사기는 낱개가
    // 세어지지 않을 만큼 촘촘히 겹친 하나의 줄기다.
    //   본류(Jet)   — 노즐 원판에서 좁은 원뿔로 뿜어져 감속하며 부풀고, 나이(=거리)에 따라 심지→주홍→먹연기로 식는다.
    //   연기(Smoke) — 본류보다 느리고 크고 어둡게, 위로 말려 오르며 뒤에 남는다.
    //   불티(Spark) — 드물고 작고 빠르게, 중력으로 떨어진다(예준 2026-09-04 — 「투사체 개별 파티클 없음」은 소 한정 지시였다).
    // 불혀 메시는 **밑불(문양 테두리 링) 전용**으로 존치한다 — 「문양에서 앞으로」 독법의 출발 신호.
    //
    // 색: 팔레트 tint 하나에서 **명도 파생만**(제2 색 출처 없음). 심지 = tint→소지(_paperColor) lerp = 역번짐 발광의
    // 색 슬롯(ART-INK LOCKED 「빛 퍼짐 = 먹의 물러남, 어둠이 밀려난 자리로 소지가 드러난다」). lerp이므로 상한이
    // 소지색으로 하드 클램프된다 — 최대 채널 ≈0.845 < 1.0이라 Bloom 미진입·백색 포화 산술적 불가(담채 유지).
    // 판정 계획(AreaImpactPlan: Direction·Length·Angle·Delay)은 읽기만 한다 — 판정 시각(PendingCast)·CombatConfig·
    // AreaGeometry 무접촉. 직렬 수치는 계획이 없을 때의 폴백(FX-REWORK §8-2 — 감사 씬). 전 수치 [TEST].
    // 발광 상한: Light 1개가 점화에 켜져 전방으로 슬라이드하며 감쇠 소멸(순간 발광). Emission 프로퍼티 없음.
    // 틴트 계약: 어댑터는 Begin(분리) 뒤 AttachBloom(TintHierarchy)을 부른다 — 이 계층은 이미 분리돼 순회되지 않으므로
    // 파티클 색(colorOverLifetime)과 밑불 재질 _BaseColor를 여기서 직접 물린다(ThornLance 선례).
    public sealed class FlameJetEffect : SpellSequenceEffect
    {
        public const string EmberName = "Ember_"; // 밑불 — FlameJetAudit이 본류 파티클과 가른다
        public const string JetName = "Jet";
        public const string SmokeName = "Smoke";
        public const string SparkName = "Spark";

        [Header("시계 [TEST — FX-REWORK §3.1.2] 판정 시각(plan.Delay)은 불변 — 연출 점화=Delay−_sweepLead(FIDELITY §8 예외 4)")]
        [SerializeField, Min(0f)] private float _formTime = 0.4f;      // 폴백 — plan.Delay 우선
        [SerializeField, Min(0f)] private float _sweepLead = 0.25f;    // 점화 선행 — 전선이 판정 시각에 닿는다(0=개시 동기)
        [SerializeField, Min(0.1f)] private float _jetDuration = 1.0f; // 분사 지속 — 연료 차단 = Delay + 이 값
        [SerializeField, Min(0f)] private float _settleTime = 1.2f;    // 차단 후 잔여 입자 소멸 대기 상한

        [Header("부채꼴 [TEST] — 반각·사거리는 plan(Angle·Length) 우선, 직렬값=폴백")]
        [SerializeField, Range(5f, 60f)] private float _coneAngle = 40f; // 판정 반각(°) 폴백
        [SerializeField, Min(1f)] private float _reach = 10f;            // 사거리(m) 폴백
        [SerializeField, Min(0f)] private float _nozzleRadius = 0.3f;    // 노즐 원판 반지름(m, 문양 판 평면)
        [SerializeField, Range(2f, 45f)] private float _jetConeAngle = 40f; // 분사 원뿔 반각(°) — 판정 반각 안으로 클램프(예준 2026-09-04: 판정 폭까지 넓힘)
        [SerializeField, Range(0f, 20f)] private float _jetPitchDown = 2.5f; // 하향 각(°) — 가슴 높이 노즐 → 적 몸통
        [Tooltip("수직 납작화 — 판정(AreaGeometry.InCone)은 수평 부채꼴이다. 1=사방 원뿔(위로도 같은 각으로 부푼다), 0.45=수평 폭 유지·수직만 눌러 판정 모양과 일치")]
        [SerializeField, Range(0.1f, 1f)] private float _jetFlatten = 0.45f;

        [Header("본류 Jet [TEST] — 낱개가 세어지지 않을 밀도가 「흐름」의 조건")]
        [SerializeField] private Vector2 _jetSpeed = new Vector2(16f, 22f); // 초속(m/s)
        [SerializeField, Range(0.1f, 1f)] private float _jetSlow = 0.4f;    // 수명 끝 속도 비율(limitVelocity 곡선 종점)
        [SerializeField, Range(0f, 1f)] private float _jetDampen = 0.6f;    // 감속 강도
        [SerializeField, Range(0.2f, 1.2f)] private float _frontFactor = 0.85f; // 평균 속도 계수 — 수명 = 사거리/(평균속×이 값). 0.72=실측 11.58m 오버슛(감쇠 후 실효 평균속 ≈15.5m/s)
        [SerializeField, Min(1f)] private float _jetRate = 380f;            // 초당 방출 — 반각 40° 확대로 단면적 ≈4.8배, 밀도(=흐름) 유지에 동반 상향
        [SerializeField, Range(16, 600)] private int _jetMax = 440;
        [SerializeField] private Vector2 _jetSize = new Vector2(1.9f, 2.9f); // 최대 지름(m) — 곡선이 노즐 쪽을 줄인다. 넓은 원뿔은 큰 덩이로 채우는 편이 입자 수 증가보다 싸다
        [SerializeField, Range(0.05f, 1f)] private float _jetSizeStart = 0.22f; // 노즐 크기 비율
        [SerializeField, Range(0f, 1f)] private float _jetAlpha = 0.85f;
        [SerializeField, Min(0f)] private float _jetNoise = 0.55f;          // 난류 세기(m/s)
        [SerializeField, Min(0.01f)] private float _jetNoiseFreq = 0.65f;
        [SerializeField, Range(0f, 1f)] private float _coreMix = 0.45f;    // 심지의 소지 혼합 — 0.6은 겹침이 쌓여 「흰 연기」로 읽혔다(주홍 채도 0.28). 0.45=채도 0.37 유지

        [Header("연기 Smoke [TEST] — 본류보다 느리고 크고 어둡게, 위로 말려 오른다")]
        [SerializeField, Min(0f)] private float _smokeRate = 32f;
        [SerializeField] private Vector2 _smokeSpeed = new Vector2(7f, 11f);
        [SerializeField, Min(0.1f)] private float _smokeLife = 1.5f;
        [SerializeField] private Vector2 _smokeSize = new Vector2(1.8f, 2.8f);
        [SerializeField] private float _smokeRise = 1.4f;                   // +Y(m/s)
        [SerializeField, Range(0f, 1f)] private float _smokeAlpha = 0.22f;
        [SerializeField, Range(0f, 1f)] private float _smokeInk = 0.55f;    // 시작색의 먹 혼합

        [Header("불티 Spark [TEST] — 예준 2026-09-04 허용(「투사체 파티클 없음」은 소 한정 지시였다)")]
        [SerializeField, Min(0f)] private float _sparkRate = 45f;
        [SerializeField] private Vector2 _sparkSpeed = new Vector2(12f, 24f);
        [SerializeField] private Vector2 _sparkLife = new Vector2(0.35f, 0.8f);
        [SerializeField] private Vector2 _sparkSize = new Vector2(0.05f, 0.12f);
        [SerializeField, Min(0f)] private float _sparkGravity = 0.35f;
        [SerializeField, Min(0f)] private float _sparkStretch = 2.5f;

        [Header("밑불 [TEST] — 문양 테두리 링(불혀 메시)에서 위로 팝인, 판정 직전 흡수 → 「문양에서 앞으로」 독법")]
        [SerializeField] private Mesh _tongueMesh;
        [SerializeField, Range(0, 16)] private int _emberCount = 6;
        [SerializeField, Min(0f)] private float _originRadius = 0.3f;
        [SerializeField] private Vector2 _emberScale = new Vector2(0.12f, 0.25f);
        [SerializeField, Min(0.01f)] private float _emberPopTime = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _emberAge = 0.5f;
        [SerializeField, Min(0f)] private float _emberErodeLead = 0.1f;
        [SerializeField, Min(0.01f)] private float _emberErodeTime = 0.15f;

        [Header("자원 — 밑불=M_SpellFlame_No(InkFlame) · 파티클=M_FlameSpray_No(InkSpray 무텍스처 소프트 디스크)")]
        [SerializeField] private Material _flameMaterial;
        [SerializeField] private Material _jetMaterial;

        [Header("색 종점 [TEST] — 팔레트 미러(FX-REWORK §8-1 _inkColor 선례)")]
        [SerializeField] private Color _inkColor = new Color(0.16f, 0.15f, 0.13f);
        [Tooltip("한지 소지 미러(팔레트 PaperColor) — 심지의 명도 상한. lerp이므로 백색 포화 불가")]
        [SerializeField] private Color _paperColor = new Color(0.969f, 0.945f, 0.894f);

        [Header("순간 광원 [TEST] — 점화에 켜져 전방으로 슬라이드, (1−k²) 감쇠 후 off(지속 발광 금지)")]
        [SerializeField, Min(0f)] private float _lightIntensity = 2.5f;
        [SerializeField, Min(0.5f)] private float _lightRange = 6f;
        [SerializeField, Range(0f, 1f)] private float _lightTravel = 0.45f;
        [SerializeField, Min(0.01f)] private float _lightSweep = 0.3f;

        [Header("검수 — 에디터 전용 로그(판정 프레임 1회)")]
        [SerializeField] private bool _debugLog = false;

        // 기술 상수(튜닝 대상 아님)
        private const float EaseBackC1 = 1.70158f;
        private const float EaseBackC3 = EaseBackC1 + 1f;
        private const float EmberRingJitter = 0.25f;
        private const float ConeAngleMargin = 3f; // 분사 원뿔이 판정 반각을 넘지 않도록 두는 여유(°)

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int AgeId = Shader.PropertyToID("_Age");
        private static readonly int ErodeId = Shader.PropertyToID("_Erode");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");

        private struct Ember
        {
            public Transform Tr;
            public MeshRenderer Rend;
            public bool Active;
            public float Scale;
            public float Seed;
        }

        private AreaImpactPlan _plan;
        private Material _owned;
        private MaterialPropertyBlock _block;
        private Ember[] _embers;
        private ParticleSystem _jet;
        private ParticleSystem _smoke;
        private ParticleSystem _spark;
        private Light _light;
        private Vector3 _origin;
        private Vector3 _dir;   // 방사축(수평 단위) = transform.forward
        private Vector3 _right;
        private float _reachUsed;
        private float _delayUsed;
        private float _halfAngle;
        private float _igniteAt;
        private float _startTime = -1f;
        private bool _ignited;
        private bool _firing;
        private bool _judgeLogged;

        // 검수 읽기(FlameJetAudit.Probe)
        public Vector3 Origin => _origin;
        public Vector3 Axis => _dir;
        public float HalfAngleUsed => _halfAngle;
        public float ReachUsed => _reachUsed;
        public float DelayUsed => _delayUsed;
        public float IgniteAt => _igniteAt;
        public float Elapsed => _startTime < 0f ? -1f : Time.time - _startTime;
        public float NozzleRadius => _nozzleRadius;
        public ParticleSystem JetSystem => _jet; // 본류 — Probe의 계측 대상(World 좌표 입자)

        // 판정 계획 [SPELL-AREA-SHAPES §3] — Cone만 소비. 다른 형상이 실리면 무시(폴백 직렬값)
        public override void SetAreaPlan(AreaImpactPlan plan)
        {
            if (plan != null && plan.Shape == AreaShape.Cone) _plan = plan;
        }

        public override void Begin(Vector3 origin, Transform target, Vector3 fallbackPoint, Color tint)
        {
            // 부모(문양 프리팹)의 수명·글자 스케일과 분리 — 분사는 자체 시계·실크기로 완주한다
            transform.SetParent(null, true);
            transform.localScale = Vector3.one;
            transform.position = origin;
            _origin = origin;

            // 방사축 = 계획 Direction(시전자 전방, 수평). 계획이 없을 때만 목표점으로 — cone은 조준 무관
            Vector3 goal = target != null ? target.position : fallbackPoint;
            Vector3 dir = _plan != null ? AreaGeometry.Flat(_plan.Direction) : AreaGeometry.Flat(goal - origin);
            if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
            _dir = dir.normalized;
            transform.rotation = Quaternion.LookRotation(_dir, Vector3.up);
            _right = Vector3.Cross(Vector3.up, _dir);

            _reachUsed = _plan != null && _plan.Length > 0f ? _plan.Length : _reach;
            _delayUsed = _plan != null && _plan.Delay > 0f ? _plan.Delay : _formTime;
            _halfAngle = _plan != null && _plan.Angle > 0f ? _plan.Angle : _coneAngle;
            _igniteAt = Mathf.Max(0f, _delayUsed - _sweepLead);
            _startTime = Time.time;
            _ignited = false;
            _firing = false;

            BuildJet(tint);

            // 밑불 — 메시가 없으면 건너뛴다(본류는 메시 없이도 돈다)
            if (_tongueMesh != null) BuildEmbers(tint);

            // 순간 광원 — 점화에 점등, 노즐에서 전방으로 슬라이드(로컬 forward = 방사축)
            var lightGo = new GameObject("JetLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = Vector3.zero;
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = tint;
            _light.intensity = 0f;
            _light.range = _lightRange;
            _light.shadows = LightShadows.None;
            _light.enabled = false;
        }

        private void Update()
        {
            if (_startTime < 0f) return;
            float now = Time.time - _startTime;
            float upperBound = _delayUsed + _jetDuration + _settleTime;

            // (a) 점화 = 판정 − 선행. 세 계층이 동시에 열린다
            if (!_ignited && now >= _igniteAt)
            {
                _ignited = true;
                _firing = true;
                Fire(_jet, true);
                Fire(_smoke, true);
                Fire(_spark, true);
                if (_light != null) _light.enabled = true;
            }

            // (c) 연료 차단 — 방출만 멈추고 이미 뿜어진 불은 제 수명대로 식는다(뿌리부터 빔)
            if (_firing && now >= _delayUsed + _jetDuration)
            {
                _firing = false;
                Fire(_jet, false);
                Fire(_smoke, false);
                Fire(_spark, false);
            }

            int activeEmbers = TickEmbers(now);
            TickLight(now);

            if (_debugLog && !_judgeLogged && now >= _delayUsed)
            {
                _judgeLogged = true;
                LogAtJudgement(now);
            }

            // (f) 전부 꺼지면 즉시 정리, 아니면 상한에서 — 잔광·잔존 0
            bool lightOff = _light == null || !_light.enabled;
            bool particlesOut = Alive(_jet) == 0 && Alive(_smoke) == 0 && Alive(_spark) == 0;
            bool allOut = _ignited && !_firing && particlesOut && activeEmbers == 0 && lightOff;
            if (allOut || now >= upperBound) Destroy(gameObject);
        }

        // ---- 본류·연기·불티 ----------------------------------------------------------------

        private void BuildJet(Color tint)
        {
            // 분사 원뿔은 판정 반각 안으로(연출이 판정보다 넓으면 「맞았는데 안 맞은」 오독이 생긴다)
            float coneHalf = Mathf.Min(_jetConeAngle, Mathf.Max(1f, _halfAngle - ConeAngleMargin));
            float meanSpeed = Mathf.Max(0.1f, (_jetSpeed.x + _jetSpeed.y) * 0.5f);
            float jetLife = Mathf.Clamp(_reachUsed / (meanSpeed * Mathf.Max(0.05f, _frontFactor)), 0.15f, 4f);

            Color core = Color.Lerp(tint, _paperColor, _coreMix);            // 심지 — 소지가 드러난다(역번짐)
            Color warm = Color.Lerp(tint, _paperColor, _coreMix * 0.35f);
            Color cool = Color.Lerp(tint, _inkColor, 0.65f);

            // 본류 — 노즐에서 좁게 나가 부풀며 식는다. 나이 = 노즐로부터의 거리이므로 색 램프가 곧 단면 구조다
            _jet = CreateSystem(JetName, jetLife, _jetSpeed, _jetSize, _jetRate, _jetMax, coneHalf, 0f);
            SetGradient(_jet, new[]
                {
                    new GradientColorKey(core, 0f),
                    new GradientColorKey(core, 0.12f),   // 심지 유지 구간 — 노즐 근처는 계속 밝다
                    new GradientColorKey(warm, 0.34f),
                    new GradientColorKey(tint, 0.60f),
                    new GradientColorKey(cool, 0.84f),
                    new GradientColorKey(_inkColor, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(_jetAlpha, 0.08f),
                    new GradientAlphaKey(_jetAlpha * 0.85f, 0.55f),
                    new GradientAlphaKey(0f, 1f),
                });
            SetGrowth(_jet, _jetSizeStart);
            SetSlowdown(_jet, _jetSpeed.y);
            SetNoise(_jet, _jetNoise, _jetNoiseFreq);

            // 연기 — 느리고 크고 어둡게, 위로 말려 오른다(본류에 뒤처져 뒤에 남는 층)
            _smoke = CreateSystem(SmokeName, _smokeLife, _smokeSpeed, _smokeSize, _smokeRate,
                Mathf.Max(16, Mathf.RoundToInt(_smokeRate * _smokeLife * 1.4f)), coneHalf * 0.85f, 0f);
            Color smokeStart = Color.Lerp(tint, _inkColor, _smokeInk);
            SetGradient(_smoke, new[]
                {
                    new GradientColorKey(smokeStart, 0f),
                    new GradientColorKey(_inkColor, 0.6f),
                    new GradientColorKey(_inkColor, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(_smokeAlpha, 0.2f),
                    new GradientAlphaKey(_smokeAlpha * 0.7f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            SetGrowth(_smoke, 0.35f);
            SetRise(_smoke, _smokeRise);
            SetNoise(_smoke, _jetNoise * 0.7f, _jetNoiseFreq * 0.6f);

            // 불티 — 드물고 작고 빠르게, 중력으로 떨어진다
            _spark = CreateSystem(SparkName, (_sparkLife.x + _sparkLife.y) * 0.5f, _sparkSpeed, _sparkSize,
                _sparkRate, Mathf.Max(16, Mathf.RoundToInt(_sparkRate * _sparkLife.y * 1.4f)), coneHalf, _sparkGravity);
            var sparkMain = _spark.main;
            sparkMain.startLifetime = new ParticleSystem.MinMaxCurve(_sparkLife.x, _sparkLife.y);
            SetGradient(_spark, new[]
                {
                    new GradientColorKey(core, 0f),
                    new GradientColorKey(tint, 0.5f),
                    new GradientColorKey(_inkColor, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.9f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            var sparkRend = _spark.GetComponent<ParticleSystemRenderer>();
            sparkRend.renderMode = ParticleSystemRenderMode.Stretch;
            sparkRend.lengthScale = _sparkStretch;
        }

        // 공용 뼈대 — World 공간(뿜어진 불은 세상에 남는다) · 원뿔 방출(로컬 +Z = 방사축) · 재생 시점은 코드가 소유
        private ParticleSystem CreateSystem(string name, float life, Vector2 speed, Vector2 size,
            float rate, int max, float coneHalfDeg, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(_jetPitchDown, 0f, 0f); // 하향 각(노즐→몸통)

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;                 // 방출 구간은 코드가 통제(Play/Stop)
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = max;
            main.startLifetime = life;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = Color.white;    // 색은 colorOverLifetime이 전부 싣는다(램프=단면 구조)
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = rate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneHalfDeg;
            shape.radius = Mathf.Max(0.01f, _nozzleRadius);
            shape.radiusThickness = 1f;
            // 원뿔의 로컬 X/Y = 원판 평면(Z=축). Y만 눌러 「수평으로 넓고 수직으로 얇은」 부채꼴로 만든다 —
            // 판정이 수평 cone이므로 위로 부푼 불은 판정에 기여하지 않으면서 화면만 채운다
            shape.scale = new Vector3(1f, Mathf.Clamp(_jetFlatten, 0.1f, 1f), 1f);

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f); // 뒤척임(rad/s)

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.sortMode = ParticleSystemSortMode.Distance; // 프리멀티 반투명 겹침의 팝핑 제거
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = LightProbeUsage.Off;
            rend.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (_jetMaterial != null) rend.sharedMaterial = _jetMaterial;

            return ps;
        }

        private static void SetGradient(ParticleSystem ps, GradientColorKey[] colors, GradientAlphaKey[] alphas)
        {
            var gradient = new Gradient();
            gradient.SetKeys(colors, alphas);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        // 노즐에서 작고 끝에서 최대 — startSize가 최대치이고 곡선이 앞쪽을 줄인다
        private static void SetGrowth(ParticleSystem ps, float startFraction)
        {
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, Mathf.Clamp01(startFraction), 1f, 1f));
        }

        // 뿜어져 나가다 멎으며 부푼다 — 화염방사기의 「몸통이 정체한다」
        private void SetSlowdown(ParticleSystem ps, float topSpeed)
        {
            var lv = ps.limitVelocityOverLifetime;
            lv.enabled = true;
            lv.separateAxes = false;
            lv.limit = new ParticleSystem.MinMaxCurve(topSpeed,
                AnimationCurve.EaseInOut(0f, 1f, 1f, Mathf.Clamp01(_jetSlow)));
            lv.dampen = _jetDampen;
        }

        // 세 축을 같은 모드(Constant)로 — 곡선 모드가 섞이면 "Particle Velocity curves must all be in the same mode"
        private static void SetRise(ParticleSystem ps, float rise)
        {
            var vol = ps.velocityOverLifetime;
            vol.enabled = true;
            vol.space = ParticleSystemSimulationSpace.World;
            vol.x = new ParticleSystem.MinMaxCurve(0f);
            vol.y = new ParticleSystem.MinMaxCurve(rise);
            vol.z = new ParticleSystem.MinMaxCurve(0f);
        }

        private static void SetNoise(ParticleSystem ps, float strength, float frequency)
        {
            if (strength <= 0f) return;
            var n = ps.noise;
            n.enabled = true;
            n.strength = strength;
            n.frequency = frequency;
            n.damping = true;
            n.octaveCount = 2;
            n.scrollSpeed = 0.6f;
        }

        private static void Fire(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            if (on) ps.Play(false);
            else ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        private static int Alive(ParticleSystem ps) => ps == null ? 0 : ps.particleCount;

        // ---- 밑불 · 광원 -------------------------------------------------------------------

        private void BuildEmbers(Color tint)
        {
            // 재질: 1장 인스턴스화, 팔레트 틴트를 _BaseColor에 — 색의 정본은 팔레트 하나
            _owned = _flameMaterial != null ? new Material(_flameMaterial) : null;
            if (_owned != null)
            {
                if (_owned.HasProperty(BaseColorId)) _owned.SetColor(BaseColorId, tint);
                else if (_owned.HasProperty(ColorId)) _owned.SetColor(ColorId, tint);
            }
            _block = new MaterialPropertyBlock();

            _embers = new Ember[_emberCount];
            float ringStep = _emberCount > 0 ? 2f * Mathf.PI / _emberCount : 0f;
            for (int k = 0; k < _embers.Length; k++)
            {
                ref Ember e = ref _embers[k];
                e.Rend = CreateBody(EmberName + k, out e.Tr);
                float a = ringStep * (k + Random.Range(-EmberRingJitter, EmberRingJitter));
                Vector3 pos = _origin + (_right * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * _originRadius;
                Quaternion rot = Quaternion.LookRotation(Vector3.up, _dir) * Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.forward);
                e.Tr.SetPositionAndRotation(pos, rot);
                e.Scale = Random.Range(_emberScale.x, _emberScale.y);
                e.Seed = Random.Range(0f, 100f);
                e.Active = true;
            }
        }

        // 밑불 — 팝인 후 고정 나이, 판정 직전부터 침식돼 흡수된다
        private int TickEmbers(float now)
        {
            if (_embers == null) return 0;
            int active = 0;
            float erodeAt = _delayUsed - _emberErodeLead;
            for (int k = 0; k < _embers.Length; k++)
            {
                ref Ember e = ref _embers[k];
                if (!e.Active) continue;
                float erode = Mathf.Clamp01((now - erodeAt) / _emberErodeTime);
                if (erode >= 1f)
                {
                    SetVisible(e.Rend, false);
                    e.Active = false;
                    continue;
                }
                float s = e.Scale * EaseOutBack(Mathf.Clamp01(now / _emberPopTime));
                e.Tr.localScale = new Vector3(s, s, s);
                SetVisible(e.Rend, true);
                ApplyBlock(e.Rend, _emberAge, erode, e.Seed);
                active++;
            }
            return active;
        }

        // 광원 — 점화부터 (1−k²) 감쇠, 노즐→사거리×_lightTravel 슬라이드, k≥1에 off(지속 발광 금지)
        private void TickLight(float now)
        {
            if (_light == null || !_light.enabled) return;
            float since = now - _igniteAt;
            float k = since / (_jetDuration + _sweepLead + _lightSweep);
            _light.intensity = _lightIntensity * Mathf.Max(0f, 1f - k * k);
            float slide = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(since / _lightSweep));
            _light.transform.localPosition = Vector3.forward * (_reachUsed * _lightTravel * slide);
            if (k >= 1f) _light.enabled = false;
        }

        private MeshRenderer CreateBody(string name, out Transform tr)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _tongueMesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = LightProbeUsage.Off;
            rend.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (_owned != null) rend.sharedMaterial = _owned;
            rend.enabled = false;
            tr = go.transform;
            return rend;
        }

        private void ApplyBlock(MeshRenderer rend, float age, float erode, float seed)
        {
            _block.SetFloat(AgeId, age);
            _block.SetFloat(ErodeId, erode);
            _block.SetFloat(SeedId, seed);
            rend.SetPropertyBlock(_block);
        }

        private static void SetVisible(MeshRenderer rend, bool visible)
        {
            if (rend.enabled != visible) rend.enabled = visible;
        }

        private static float EaseOutBack(float x)
        {
            float m = x - 1f;
            return 1f + EaseBackC3 * m * m * m + EaseBackC1 * m * m;
        }

        // 판정 프레임 1회(_debugLog) — 「뒤로 새는 입자 없음」·전선 도달의 정량 기록(FX-REWORK §6-3)
        private void LogAtJudgement(float now)
        {
            float backward = 0f;
            float front = 0f;
            int visible = 0;
            if (_jet != null)
            {
                var buffer = new ParticleSystem.Particle[_jet.particleCount];
                int n = _jet.GetParticles(buffer);
                for (int i = 0; i < n; i++)
                {
                    float along = Vector3.Dot(buffer[i].position - _origin, _dir);
                    if (along < backward) backward = along;
                    if (along > front) front = along;
                }
                visible = n;
            }
            Debug.Log($"[FlameJet] 판정 프레임 now={now:F3} ignite={_igniteAt:F2} plan(반각 {_halfAngle:F1}° 사거리 {_reachUsed:F1} 판정 {_delayUsed:F2})"
                + $" 본류 {visible} 연기 {Alive(_smoke)} 불티 {Alive(_spark)} backwardExtent={backward:F2} frontReach={front:F2}", this);
        }

        private void OnDestroy()
        {
            if (_owned != null) Destroy(_owned);
        }
    }
}
