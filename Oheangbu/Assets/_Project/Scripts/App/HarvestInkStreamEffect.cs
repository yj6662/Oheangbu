using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // [갈무리 연출 — 붓 프롭·줄기 생애주기·꼬인 다발·역동화 (예준 4~7차, 2026-08-30)]
    // 좌클릭 홀드 동안 상대에게서 먹 다발이 뽑혀 나온다:
    //   등장 = 선단 덩어리가 몸에서 뜯겨(easeIn 저항) 얇은 줄기를 끌고 붓 끝으로 →
    //   유지 = 중앙 굵은 주줄기 + 가는 보조 가닥들이 나선으로 휘감고, 생명력 맥동(굵기 파동)이
    //   뿌리→붓으로 흐르며, 형태는 저주파 유동·장력 사이클(느슨↔팽팽)·간헐 세선/순단·몸부림
    //   임펄스로 쉼 없이 바뀐다 → 해제 = 얇아지며 스러진다(잔상 없음).
    // 내 쪽 끝은 아래로 들어왔다가 위로 감아 올라가 붓 끝(_sinkAnchor)에 닿는다(J-곡선).
    // HarvestAction의 읽기 전용 신호만 소비 — 판정·수급 수치 무접촉.
    // 먹은 빛나지 않는다 — 어두운 먹색·무광(발광 상한 합치).
    public sealed class HarvestInkStreamEffect : MonoBehaviour
    {
        [Header("배선")]
        [SerializeField] private HarvestAction _harvest;
        [SerializeField] private Transform _sinkAnchor;   // 붓 끝 — 없으면 카메라 오프셋 폴백
        [SerializeField] private Mesh _dropletMesh;       // 방울 실체(구면 충분 — 비균등 스케일로 불규칙화)
        [SerializeField] private Material _material;      // 틴트 호환(_Color/_BaseColor) — 인스턴스화해 원본 불변
        [SerializeField] private Color _inkColor = new Color(0.16f, 0.15f, 0.13f); // 먹색(팔레트 _fallback 미러) [TEST]

        [Header("생애주기")]
        [SerializeField] private float _growTime = 0.55f;    // 뽑힘 — 선단이 뿌리→붓끝으로 끌려오는 시간(easeIn: 초반 저항)
        [SerializeField] private float _thickenTime = 0.5f;  // 닿은 뒤 굵어지는 시간
        [SerializeField] private float _fadeOutTime = 0.35f; // 해제 — 얇아지며 스러지는 시간
        [SerializeField] private float _thinWidth = 0.28f;   // 등장기 굵기 배율(1=만개)

        [Header("스파인(중심 곡선)")]
        [SerializeField, Range(6, 48)] private int _pointCount = 22;
        [SerializeField] private float _sag = 0.4f;          // 처짐 기준값 — 실제 처짐은 시간에 따라 증감(가끔 위로 부풂)
        [SerializeField] private float _driftAmp = 0.45f;    // 중간 제어점들의 저주파 유동 — S자·아치로 형태가 변한다
        [SerializeField] private float _driftSpeed = 0.4f;
        [SerializeField] private float _rippleAmp = 0.03f;   // 가닥 위를 흐르는 잔물결(상대→붓 방향)
        [SerializeField] private float _rippleSpeed = 7f;
        [SerializeField] private float _rippleWaves = 2.5f;
        [SerializeField] private float _riseDip = 0.3f;      // 진입 거리 — 촉 연장선에서 이만큼 떨어져 내려앉는다
        [SerializeField] private Vector3 _sinkOffset = new Vector3(0f, -0.25f, 0.5f); // 앵커 부재 시 폴백(카메라 기준)

        [Header("다발(중앙 주줄기 + 가는 보조 가닥)")]
        [SerializeField, Range(1, 6)] private int _strandCount = 6;
        [SerializeField] private float _braidRadius = 1f;    // 보조 가닥 벌어짐 반경(양끝 0·중간 최대) — 예준 튜닝 확정(12차)
        [SerializeField] private float _braidTurns = 5f;     // 전장당 감는 회전 수
        [SerializeField] private float _braidSpin = 5f;      // 다발이 도는 요동(rad/s)
        [SerializeField] private float _widthRoot = 0.1f;    // 주줄기 뿌리 굵기(보조는 배율 축소)
        [SerializeField] private float _widthTip = 0.015f;   // 붓끝 쪽 굵기

        [Header("생명력 맥동·뽑힘 가시화")]
        [SerializeField] private float _pulseSpeed = 0.8f;    // 주줄기를 타는 굵기 파동 속도(전장/초)
        [SerializeField] private float _pulseAmp = 0.6f;      // 파동 두께 증폭
        [SerializeField] private float _pulseSigma = 0.06f;   // 파동 폭
        [SerializeField] private float _headBeadSize = 0.1f;  // 뽑혀 나오는 선단 덩어리 크기(m)
        [SerializeField] private float _tipBeadSize = 0.045f; // 붓촉 끝에 맺히는 먹 방울 크기(m) — 「촉에 맺힌다」

        [Header("역동 — 장력 사이클")]
        [SerializeField] private float _tensionSwitchMin = 3f; // 느슨↔팽팽 목표 재추첨 간격
        [SerializeField] private float _tensionSwitchMax = 6f;
        [SerializeField] private float _tensionLerp = 1.2f;    // 전환 수렴 속도(1/s)

        [Header("역동 — 간헐 세선/순단")]
        [SerializeField] private float _pinchIntervalMin = 2.5f;
        [SerializeField] private float _pinchIntervalMax = 6f;
        [SerializeField] private float _pinchDuration = 0.24f; // 얇아져 있는 시간
        [SerializeField] private float _pinchThin = 0.12f;     // 세선 굵기 배율
        [SerializeField, Range(0f, 1f)] private float _cutChance = 0.5f; // 순단(주줄기 실제 끊김) 확률

        [Header("역동 — 몸부림 임펄스")]
        [SerializeField] private float _writheIntervalMin = 2.5f;
        [SerializeField] private float _writheIntervalMax = 5f;
        [SerializeField] private float _writheAmp = 0.5f;      // 홱 젖힘 진폭(m)

        [Header("감도는 먹 방울")]
        [SerializeField, Range(0, 12)] private int _dropletCount = 6;
        [SerializeField] private float _orbitRadius = 0.11f;
        [SerializeField] private float _orbitSpeed = 6f;
        [SerializeField] private float _orbitTwist = 12f;
        [SerializeField] private float _dropletFlow = 0.4f;
        [SerializeField] private float _dropletMin = 0.03f;
        [SerializeField] private float _dropletMax = 0.06f;

#if UNITY_EDITOR
        [Header("검수 전용")]
        [SerializeField] private bool _debugHold; // true=신호 무시·유지 상태 구동(전방 4m 뿌리) — 캡처용, 저장값 false
#endif

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        // [0]=중앙 주줄기(굵게·꼬임 없음), 나머지=가는 보조 가닥 — 주줄기의 절반 수준
        // (9차 검수: 0.3대는 원거리 뷰에서 픽셀 소실 — 위계는 배율 차이로, 가시성은 절대 굵기로)
        private static readonly float[] StrandWidthScale = { 1f, 0.52f, 0.6f, 0.46f, 0.54f, 0.48f };
        // 가닥별 감김 반경 배율 — 같은 반경이면 한 덩어리로 뭉친다(리듬 있는 다발)
        private static readonly float[] StrandRadiusScale = { 0f, 1f, 0.72f, 0.88f, 0.8f, 0.95f };

        private sealed class Droplet
        {
            public Transform Tr;
            public float S;
            public float SpeedMul;
            public float Phase;
            public float Seed;
            public Vector3 BaseScale;
        }

        private readonly List<Droplet> _droplets = new List<Droplet>();
        private LineRenderer[] _lines;
        private Material _ownedMaterial;
        private readonly Vector3[] _cp = new Vector3[8]; // CR 제어점(양끝 복제 포함)
        private Vector3[] _spine;
        private Vector3[] _spineTangent;
        private Vector3[] _points;
        private Keyframe[] _pulseKeys;
        private AnimationCurve _pulseCurve;
        private Transform _headBead;
        private Transform _tipBead; // 촉 끝의 맺힘 — 유지 중 상주하며 고동친다
        private float _headBeadLife;
        private float _reveal;
        private float _width;
        private Vector3 _root;
        private bool _active;

        // 역동 상태
        private float _tension;          // 0=느슨(처짐·큰 물결) 1=팽팽(직선·잔떨림)
        private float _tensionTarget;
        private float _tensionTimer;
        private float _pinchTimer;       // 다음 세선/순단까지
        private float _pinchStart = -10f;
        private bool _pinchIsCut;
        private float _pinchCutCenter;
        private float _writheTimer;      // 다음 몸부림까지
        private float _writheStart = -10f;
        private Vector3 _writheDir;
        private float _scatter;          // 재결합 직후 방울 산란(1→0)

        // 지연 초기화 — Awake 대신 매 프레임 보증: 플레이 중 리컴파일(도메인 리로드) 후 Awake가
        // 재실행되지 않아 비직렬화 상태가 날아가도 연출이 침묵하지 않는다(11차 검수 실결함)
        private bool EnsureRuntime()
        {
            if (_spine == null || _spine.Length != Mathf.Max(6, _pointCount))
            {
                int n = Mathf.Max(6, _pointCount);
                _spine = new Vector3[n];
                _spineTangent = new Vector3[n];
                _points = new Vector3[n];
                _tensionTarget = 0.2f;
                _tensionTimer = Random.Range(_tensionSwitchMin, _tensionSwitchMax);
                _pinchTimer = Random.Range(_pinchIntervalMin, _pinchIntervalMax);
                _writheTimer = Random.Range(_writheIntervalMin, _writheIntervalMax);
            }
            if (_ownedMaterial == null && _material != null)
            {
                _ownedMaterial = new Material(_material); // 인스턴스 1장 공유 — 원본 불변
                if (_ownedMaterial.HasProperty(BaseColorId)) _ownedMaterial.SetColor(BaseColorId, _inkColor);
                if (_ownedMaterial.HasProperty(ColorId)) _ownedMaterial.SetColor(ColorId, _inkColor);
                // 리로드로 재생성됐다면 기존 렌더러들에 재바인딩
                if (_lines != null) foreach (var l in _lines) if (l != null) l.material = _ownedMaterial;
            }
            return _ownedMaterial != null;
        }

        private void Update()
        {
            if (!EnsureRuntime()) return;
            bool holding = _harvest != null && _harvest.IsExtracting;
            Vector3 sourcePos = _harvest != null ? _harvest.ExtractSourcePosition : transform.position;
#if UNITY_EDITOR
            if (_debugHold)
            {
                holding = true;
                var cam = Camera.main;
                sourcePos = cam != null
                    ? cam.transform.position + cam.transform.forward * 7f // 실전 근사 거리(플레이어 앞 ~3.5m)
                    : transform.position + transform.forward * 4f;
            }
#endif
            if (holding && _ownedMaterial != null)
            {
                if (!_active)
                {
                    _active = true;
                    _reveal = 0f;
                    _width = 0f;
                    _headBeadLife = 1f;
                    _root = sourcePos;
                }
                _root = Vector3.Lerp(_root, sourcePos, 1f - Mathf.Exp(-10f * Time.deltaTime));

                if (_reveal < 1f)
                {
                    _reveal = Mathf.Min(1f, _reveal + Time.deltaTime / Mathf.Max(0.02f, _growTime));
                    _width = Mathf.Min(_thinWidth, _width + Time.deltaTime / Mathf.Max(0.02f, _growTime) * _thinWidth);
                }
                else
                {
                    _width = Mathf.Min(1f, _width + Time.deltaTime / Mathf.Max(0.02f, _thickenTime));
                }

                TickDynamics(); // 장력·순단·몸부림은 뽑는 동안만 진행
            }
            else if (_active)
            {
                _width -= Time.deltaTime / Mathf.Max(0.02f, _fadeOutTime);
                if (_width <= 0f)
                {
                    _active = false;
                    _width = 0f;
                    _reveal = 0f;
                    if (_lines != null) foreach (var l in _lines) if (l != null) l.enabled = false;
                    foreach (var d in _droplets) if (d.Tr != null) d.Tr.gameObject.SetActive(false);
                    if (_headBead != null) _headBead.gameObject.SetActive(false);
                    if (_tipBead != null) _tipBead.gameObject.SetActive(false);
                    return;
                }
            }
            else
            {
                return;
            }

            EnsureLines();
            EnsureDroplets();
            BuildSpine();
            RenderStrands();
            UpdateHeadBead(_spine.Length);
            UpdateTipBead();
            UpdateDroplets();
        }

        // ---- 역동 상태 진행 — 장력 재추첨·세선/순단·몸부림 트리거 ----
        private void TickDynamics()
        {
            float dt = Time.deltaTime;

            _tensionTimer -= dt;
            if (_tensionTimer <= 0f)
            {
                _tensionTimer = Random.Range(_tensionSwitchMin, _tensionSwitchMax);
                // 극단 가중 — 느슨하거나 팽팽하거나(중간은 지루하다)
                _tensionTarget = Random.value < 0.5f ? Random.Range(0.1f, 0.3f) : Random.Range(0.7f, 0.95f);
                if (Random.value < 0.6f) TriggerWrithe(); // 힘이 바뀌는 순간의 출렁
            }
            _tension = Mathf.Lerp(_tension, _tensionTarget, 1f - Mathf.Exp(-_tensionLerp * dt));

            _pinchTimer -= dt;
            if (_pinchTimer <= 0f && Time.time - _pinchStart > _pinchDuration + 0.5f)
            {
                _pinchTimer = Random.Range(_pinchIntervalMin, _pinchIntervalMax);
                _pinchStart = Time.time;
                _pinchIsCut = Random.value < _cutChance;
                _pinchCutCenter = Random.Range(0.35f, 0.7f);
            }
            // 재결합 순간 — 방울 산란 점화
            float sincePinchEnd = Time.time - (_pinchStart + _pinchDuration);
            if (sincePinchEnd > 0f && sincePinchEnd < dt * 2f) _scatter = 1f;
            _scatter = Mathf.Max(0f, _scatter - dt / 0.4f);

            _writheTimer -= dt;
            if (_writheTimer <= 0f)
            {
                _writheTimer = Random.Range(_writheIntervalMin, _writheIntervalMax);
                TriggerWrithe();
            }
        }

        private void TriggerWrithe()
        {
            Vector2 flat = Random.insideUnitCircle.normalized;
            _writheDir = new Vector3(flat.x, Random.Range(-0.3f, 0.5f), flat.y).normalized; // 수평 위주·살짝 상하
            _writheStart = Time.time;
        }

        // ---- 스파인: Catmull-Rom 6점 — 뿌리·유동 중간 3점·진입점·붓촉 끝. 유동·장력·몸부림 반영 ----
        private void BuildSpine()
        {
            Vector3 sink = SinkPoint(out Vector3 approachDir);
            float slack = 1f - 0.8f * _tension;                       // 팽팽할수록 처짐·유동이 걷힌다
            float driftAmp = _driftAmp * slack * (0.4f + 0.6f * _width); // 만개기에 크게
            float T = Time.time * _driftSpeed;
            // 시변 처짐 — 깊게 처졌다가 펴지고, 가끔 위로 부푼다
            float sagNow = _sag * (Mathf.PerlinNoise(5.1f, Time.time * 0.22f) * 1.5f - 0.35f) * slack;

            Vector3 p0 = _root + WanderOffset(0.9f, 0.18f);
            _cp[1] = p0;
            _cp[2] = Vector3.Lerp(p0, sink, 0.3f) + DriftOffset(0f, T, driftAmp) + Vector3.down * (sagNow * 0.5f);
            _cp[3] = Vector3.Lerp(p0, sink, 0.55f) + DriftOffset(40f, T * 1.13f, driftAmp) + Vector3.down * sagNow;
            _cp[4] = Vector3.Lerp(p0, sink, 0.78f) + DriftOffset(80f, T * 0.87f, driftAmp * 0.7f) + Vector3.down * (sagNow * 0.45f);
            // 진입점 = 붓촉 연장선(자루 축 바깥) — 줄기가 촉 「끝」에서 내려앉아 맺힌다.
            // 자루를 스치지 않는다(8차 검수: 딥이 월드-아래 기준이라 손잡이로 들어와 보이던 문제 교정)
            _cp[5] = sink + approachDir * _riseDip;
            _cp[6] = sink;
            _cp[0] = _cp[1] + (_cp[1] - _cp[2]); // 양끝 접선용 복제
            _cp[7] = _cp[6] + (_cp[6] - _cp[5]);

            // 몸부림 임펄스 — 홱 젖혀졌다 감쇠 출렁(중간 최대)
            float wt = Time.time - _writheStart;
            Vector3 writhe = wt < 1.2f
                ? _writheDir * (_writheAmp * Mathf.Exp(-3f * wt) * Mathf.Sin(12f * wt))
                : Vector3.zero;

            float revealEased = _reveal * _reveal; // 뽑힘의 저항 — 초반 느리게
            int n = _spine.Length;
            for (int i = 0; i < n; i++)
            {
                float u = (i / (float)(n - 1)) * revealEased;
                Vector3 p = SampleCR(u);
                p += writhe * Mathf.Sin(Mathf.Clamp01(u / Mathf.Max(0.01f, revealEased)) * Mathf.PI);
                _spine[i] = p;
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 a = _spine[Mathf.Max(0, i - 1)];
                Vector3 b = _spine[Mathf.Min(n - 1, i + 1)];
                Vector3 d = b - a;
                _spineTangent[i] = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
            }
        }

        private Vector3 SampleCR(float u)
        {
            const int segments = 5; // cp1~cp6
            float x = Mathf.Clamp01(u) * segments;
            int seg = Mathf.Min(segments - 1, (int)x);
            float t = x - seg;
            Vector3 a = _cp[seg], b = _cp[seg + 1], c = _cp[seg + 2], d = _cp[seg + 3];
            return 0.5f * (2f * b + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * (t * t)
                + (-a + 3f * b - 3f * c + d) * (t * t * t));
        }

        private Vector3 DriftOffset(float seed, float t, float amp)
        {
            return new Vector3(
                (Mathf.PerlinNoise(seed + 1.3f, t) - 0.5f) * 2f,
                (Mathf.PerlinNoise(seed + 11.7f, t) - 0.5f) * 2f,
                (Mathf.PerlinNoise(seed + 23.9f, t) - 0.5f) * 2f) * amp;
        }

        // ---- 가닥 렌더 — 주줄기(중심·맥동 커브) + 보조(나선 감김). 장력·세선/순단 반영 ----
        private void RenderStrands()
        {
            int n = _spine.Length;
            // 감김 기저는 카메라 기준(10차 검수): 월드 기준이면 벌어지는 평면이 시선과 나란한 각도에서
            // 화면상 분리가 0이 되어 다발이 주줄기에 흡수돼 보인다 — n1=화면 좌우, n2=깊이
            var camT = Camera.main != null ? Camera.main.transform : null;
            Vector3 viewDir = camT != null ? camT.forward : Vector3.forward;
            float revealEased = _reveal * _reveal;
            float pulse = 1f + (Mathf.PerlinNoise(7.7f, Time.time * 2.2f) - 0.5f) * 0.3f * _width;
            float braidOpen = Mathf.Clamp01((_width - _thinWidth * 0.6f) / (1f - _thinWidth * 0.6f));
            float braidEff = _braidRadius * (1f - 0.35f * _tension); // 팽팽하면 다발도 조여든다
            float rippleAmpEff = _rippleAmp * (1f - 0.6f * _tension);
            float rippleSpeedEff = _rippleSpeed * (1f + 2f * _tension); // 팽팽=고주파 잔떨림

            // 세선/순단 — 얇기 엔벨로프(들어감 0.06s·유지·복귀 0.1s + 「탁」 오버슈트)
            float pinchFactor = 1f;
            float cutStrength = 0f;
            float pt = Time.time - _pinchStart;
            if (pt >= 0f && pt < _pinchDuration + 0.25f)
            {
                float inK = Mathf.Clamp01(pt / 0.06f);
                float outK = Mathf.Clamp01((pt - _pinchDuration) / 0.1f);
                float hold = inK * (1f - outK);
                pinchFactor = Mathf.Lerp(1f, _pinchThin, hold);
                if (outK > 0f && outK < 1f) pinchFactor *= 1f + 0.25f * (1f - outK); // 재결합 오버슈트
                if (_pinchIsCut) cutStrength = hold; // 주줄기는 실제로 끊긴다
            }

            for (int k = 0; k < _lines.Length; k++)
            {
                var line = _lines[k];
                if (line == null) continue;
                bool isCore = k == 0;
                float strandPhase = k * (Mathf.PI * 2f / Mathf.Max(1, _lines.Length - 1));
                float rippleSeed = k * 1.7f;

                for (int i = 0; i < n; i++)
                {
                    float t = (i / (float)(n - 1)) * revealEased;
                    Vector3 tangent = _spineTangent[i];
                    Vector3 n1 = Vector3.Cross(tangent, Vector3.up);
                    n1 = n1.sqrMagnitude < 0.01f ? Vector3.right : n1.normalized;
                    Vector3 n2 = Vector3.Cross(tangent, n1);

                    float tn = Mathf.Clamp01(t / Mathf.Max(0.01f, revealEased));
                    float envelope = Mathf.Sin(tn * Mathf.PI);
                    float tipClamp = 1f - Mathf.SmoothStep(0.82f, 0.98f, tn); // 촉 근처 — 모든 오프셋이 촉 끝 한 점으로 수렴(맺힘)
                    Vector3 p = _spine[i];
                    if (!isCore)
                    {
                        // 화면-좌우 순수 파형 편조(10차 확정): 깊이 성분을 없애 어느 각도에서도 가닥이
                        // 주줄기와 겹치지 않는다 — 위상이 다른 파형들이 서로 교차하며 엮여 보인다
                        Vector3 bn1 = Vector3.Cross(tangent, viewDir);
                        bn1 = bn1.sqrMagnitude < 0.01f ? n1 : bn1.normalized;
                        float angle = strandPhase + t * _braidTurns * 2f * Mathf.PI + Time.time * _braidSpin;
                        float radius = braidEff * envelope * braidOpen * tipClamp
                            * StrandRadiusScale[k % StrandRadiusScale.Length]
                            * (0.8f + 0.4f * Mathf.PerlinNoise(rippleSeed + 31f, Time.time * 1.9f + t * 2f));
                        p += bn1 * (Mathf.Sin(angle) * radius);
                    }

                    float phase = t * _rippleWaves * 2f * Mathf.PI - Time.time * rippleSpeedEff + rippleSeed;
                    p += n1 * (Mathf.Sin(phase) * rippleAmpEff * envelope * tipClamp * _width);
                    p += Vector3.up * (Mathf.Sin(phase * 0.7f + rippleSeed) * rippleAmpEff * 0.6f * envelope * tipClamp * _width);
                    _points[i] = p;
                }

                line.enabled = true;
                line.positionCount = n;
                line.SetPositions(_points);
                line.widthMultiplier = _width * pulse * pinchFactor * StrandWidthScale[k % StrandWidthScale.Length];
                if (isCore) ApplyPulseCurve(line, cutStrength);
            }
        }

        // ---- 생명력 맥동 + 순단 — 주줄기 굵기 커브: 흐르는 파동 2개, 순단 시 중간 구간이 0으로 ----
        private void ApplyPulseCurve(LineRenderer core, float cutStrength)
        {
            const int keys = 13;
            if (_pulseKeys == null)
            {
                _pulseKeys = new Keyframe[keys];
                _pulseCurve = new AnimationCurve();
            }
            float amp = _pulseAmp * _width;
            float c1 = (Time.time * _pulseSpeed) % 1.15f;
            float c2 = (c1 + 0.55f) % 1.15f;
            float twoSigmaSq = 2f * _pulseSigma * _pulseSigma;
            float cutTwoSigmaSq = 2f * 0.08f * 0.08f;
            for (int i = 0; i < keys; i++)
            {
                float t = i / (float)(keys - 1);
                float baseWidth = Mathf.Lerp(_widthRoot, _widthTip, t);
                float g1 = Mathf.Exp(-((t - c1) * (t - c1)) / twoSigmaSq);
                float g2 = Mathf.Exp(-((t - c2) * (t - c2)) / twoSigmaSq);
                float w = baseWidth * (1f + amp * (g1 + g2));
                if (cutStrength > 0f)
                {
                    float gc = Mathf.Exp(-((t - _pinchCutCenter) * (t - _pinchCutCenter)) / cutTwoSigmaSq);
                    w *= 1f - Mathf.Clamp01(gc * cutStrength * 1.15f); // 중심은 완전 절단
                }
                _pulseKeys[i] = new Keyframe(t, w);
            }
            _pulseCurve.keys = _pulseKeys;
            core.widthCurve = _pulseCurve;
        }

        // ---- 뽑혀 나오는 선단 덩어리 ----
        private void UpdateHeadBead(int n)
        {
            if (_dropletMesh == null || _ownedMaterial == null) return;
            if (_headBead == null)
            {
                var go = new GameObject("InkHead");
                go.transform.SetParent(transform, false);
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = _dropletMesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _ownedMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _headBead = go.transform;
            }

            if (_reveal >= 1f) _headBeadLife = Mathf.Max(0f, _headBeadLife - Time.deltaTime / 0.18f);
            float scale = _headBeadSize * _headBeadLife
                * (0.85f + 0.3f * Mathf.PerlinNoise(3.3f, Time.time * 6f));
            if (scale <= 0.004f)
            {
                _headBead.gameObject.SetActive(false);
                return;
            }
            _headBead.gameObject.SetActive(true);
            _headBead.position = _spine[n - 1];
            _headBead.Rotate(37f * Time.deltaTime, 61f * Time.deltaTime, 0f, Space.Self);
            _headBead.localScale = new Vector3(scale, scale * 0.8f, scale * 1.15f);
        }

        // ---- 촉 끝의 맺힘 — 뽑혀 온 먹이 붓촉 끝에 볼록하게 맺혀 고동친다 ----
        private void UpdateTipBead()
        {
            if (_dropletMesh == null || _ownedMaterial == null) return;
            if (_tipBead == null)
            {
                var go = new GameObject("InkTipBead");
                go.transform.SetParent(transform, false);
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = _dropletMesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _ownedMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _tipBead = go.transform;
            }

            float scale = _tipBeadSize * _width
                * (0.85f + 0.3f * Mathf.PerlinNoise(9.2f, Time.time * 4f)); // 삼키는 고동
            if (scale <= 0.004f)
            {
                _tipBead.gameObject.SetActive(false);
                return;
            }
            Vector3 sink = SinkPoint(out Vector3 approachDir);
            _tipBead.gameObject.SetActive(true);
            _tipBead.position = sink + approachDir * 0.008f; // 촉 끝에 살짝 걸친 물방울
            _tipBead.localScale = new Vector3(scale, scale * 1.2f, scale); // 자루 축으로 갸름한 맺힘
        }

        // ---- 감도는 먹 방울 — 스파인을 타고 흐르며 감고, 재결합 직후엔 흩어졌다 돌아온다 ----
        private void UpdateDroplets()
        {
            float vis = Mathf.Clamp01((_width - _thinWidth) / (1f - _thinWidth));
            int n = _spine.Length;
            for (int i = 0; i < _droplets.Count; i++)
            {
                var d = _droplets[i];
                if (d.Tr == null) continue;
                if (vis <= 0.02f)
                {
                    d.Tr.gameObject.SetActive(false);
                    continue;
                }

                d.S += _dropletFlow * d.SpeedMul * Time.deltaTime;
                if (d.S >= 0.96f)
                {
                    d.S = Random.Range(0.02f, 0.12f);
                    d.Phase = Random.Range(0f, Mathf.PI * 2f);
                    d.SpeedMul = Random.Range(0.7f, 1.35f);
                }

                float s = Mathf.Clamp01(d.S);
                float fi = s * (n - 1);
                int i0 = Mathf.Min(n - 2, (int)fi);
                float ft = fi - i0;
                Vector3 c = Vector3.Lerp(_spine[i0], _spine[i0 + 1], ft);
                Vector3 tangent = Vector3.Lerp(_spineTangent[i0], _spineTangent[i0 + 1], ft).normalized;
                Vector3 n1 = Vector3.Cross(tangent, Vector3.up);
                n1 = n1.sqrMagnitude < 0.01f ? Vector3.right : n1.normalized;
                Vector3 n2 = Vector3.Cross(tangent, n1);

                float angle = d.Phase + Time.time * _orbitSpeed * d.SpeedMul + s * _orbitTwist;
                float radius = _orbitRadius * (0.65f + 0.7f * Mathf.PerlinNoise(d.Seed, Time.time * 1.7f))
                    * (1f + 1.2f * _scatter)                          // 재결합 직후 산란
                    * (1f - Mathf.SmoothStep(0.8f, 0.96f, s));        // 촉 근처 — 촉 끝으로 수렴해 맺힌다
                Vector3 pos = c + (n1 * Mathf.Cos(angle) + n2 * Mathf.Sin(angle)) * (radius * vis);

                d.Tr.gameObject.SetActive(true);
                d.Tr.position = pos;
                d.Tr.Rotate(d.Seed * 3f * Time.deltaTime, d.Seed * 5f * Time.deltaTime, 0f, Space.Self);
                float endShrink = Mathf.Clamp01((0.96f - d.S) / 0.1f);
                d.Tr.localScale = d.BaseScale * (vis * Mathf.Min(1f, endShrink));
            }
        }

        private Vector3 WanderOffset(float speed, float amp)
        {
            return new Vector3(
                (Mathf.PerlinNoise(1.3f, Time.time * speed) - 0.5f) * 2f * amp,
                (Mathf.PerlinNoise(11.7f, Time.time * speed) - 0.5f) * 2f * amp,
                (Mathf.PerlinNoise(23.9f, Time.time * speed) - 0.5f) * 2f * amp);
        }

        private void EnsureLines()
        {
            if (_lines != null) return;
            _lines = new LineRenderer[Mathf.Max(1, _strandCount)];
            for (int k = 0; k < _lines.Length; k++)
            {
                var go = new GameObject($"InkStrand_{k}");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.material = _ownedMaterial;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.numCapVertices = 4;
                line.numCornerVertices = 4;
                line.textureMode = LineTextureMode.Stretch;
                line.widthCurve = AnimationCurve.Linear(0f, _widthRoot, 1f, _widthTip);
                line.enabled = false;
                _lines[k] = line;
            }
        }

        private void EnsureDroplets()
        {
            if (_dropletMesh == null || _ownedMaterial == null) return;
            while (_droplets.Count < _dropletCount)
            {
                var go = new GameObject($"InkBead_{_droplets.Count}");
                go.transform.SetParent(transform, false);
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = _dropletMesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _ownedMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.transform.rotation = Random.rotation;
                float size = Random.Range(_dropletMin, _dropletMax);
                _droplets.Add(new Droplet
                {
                    Tr = go.transform,
                    S = Random.Range(0.05f, 0.9f),
                    SpeedMul = Random.Range(0.7f, 1.35f),
                    Phase = Random.Range(0f, Mathf.PI * 2f),
                    Seed = Random.Range(0f, 100f),
                    BaseScale = new Vector3(
                        size * Random.Range(0.6f, 1.4f),
                        size * Random.Range(0.6f, 1.4f),
                        size * Random.Range(0.6f, 1.4f)),
                });
                go.SetActive(false);
            }
        }

        // 흡수점 — 붓촉 끝 앵커가 정본. approachDir = 줄기가 진입해 오는 방향(촉 연장선 = 앵커의 +Y).
        // 없으면 카메라 앞·아래 폴백(진입은 아래-뒤). 규칙이 아니라 연출의 목적지다
        private Vector3 SinkPoint(out Vector3 approachDir)
        {
            var cam = Camera.main;
            if (_sinkAnchor != null)
            {
                approachDir = _sinkAnchor.up; // 자루 축 바깥 — 촉 「끝」으로 내려앉는다
                return _sinkAnchor.position;
            }
            approachDir = (cam != null ? -cam.transform.forward : -transform.forward) * 0.4f + Vector3.down;
            approachDir.Normalize();
            if (cam == null) return transform.position;
            Transform ct = cam.transform;
            return ct.position + ct.forward * _sinkOffset.z + ct.up * _sinkOffset.y;
        }

        private void OnDestroy()
        {
            if (_ownedMaterial != null) Destroy(_ownedMaterial);
        }
    }
}
