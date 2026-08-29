using UnityEngine;

namespace Oheangbu.BrushRender
{
    // 붓 획 표현 수치 일체(SPEC-SPIKE-BRUSH-RENDERER §11-1) — 코드에 미감 상수를 두지 않는다.
    // 여기 있는 값 중 무엇도 위력에 개입하지 않는다: 갈필·농도는 순수 비주얼이고
    // 필세는 형×세뿐이다(ART-INK LOCKED · DECISIONS #73). 렌더러를 통째로 갈아도 인식은 불변이다.
    [CreateAssetMenu(menuName = "Oheangbu/BrushRender/Brush Style", fileName = "BrushStyle")]
    public sealed class BrushStyleSO : ScriptableObject
    {
        [Header("먹색 — ART-COLOR 기본 팔레트의 먹(#2A2622)")]
        [SerializeField] private Color _inkColor = new Color(0x2A / 255f, 0x26 / 255f, 0x22 / 255f, 1f);

        [Header("폭 — 속도가 빠를수록 가늘어진다(붓털이 덜 눌린다)")]
        [SerializeField, Min(0f)] private float _baseWidth = 0.01f;
        [Tooltip("이 속도(px/s) 이상이면 가장 가는 획")]
        [SerializeField, Min(1f)] private float _fastSpeed = 2200f;
        [SerializeField, Range(0.1f, 1f)] private float _minWidthFactor = 0.45f;
        [SerializeField, Range(1f, 3f)] private float _maxWidthFactor = 1.5f;
        [Tooltip("굵기의 유기적 흔들림 — 0이면 기계적으로 균일해진다")]
        [SerializeField, Range(0f, 0.5f)] private float _widthNoise = 0.08f;
        [Tooltip("노이즈의 공간 주기 — 클수록 잘게 떨린다")]
        [SerializeField, Min(0.01f)] private float _widthNoiseFrequency = 6f;
        [Tooltip("표현용 속도의 저역 통과(EMA 계수) — 낮을수록 순간 튐이 굵기에 덜 박힌다. " +
            "획 첫 점은 속도를 잴 수 없으므로 항상 중립 폭으로 시작한다")]
        [SerializeField, Range(0.05f, 1f)] private float _speedSmoothing = 0.35f;

        [Header("기필(起筆) — 붓을 종이에 대는 순간의 눌림")]
        [SerializeField, Range(1f, 3f)] private float _startPressFactor = 1.35f;
        [SerializeField, Min(0)] private int _startPressSamples = 5;

        [Header("수필(收筆) — 획 끝을 빼는 흔적")]
        [SerializeField, Min(0)] private int _taperSteps = 4;
        [SerializeField, Min(0f)] private float _taperLength = 0.04f;
        [Tooltip("수필 끝에 남기는 폭 비율 — 0=노봉(뾰족하게 뺌), 높일수록 회봉(붓을 되돌려 둥글게 거둠)에 가깝다. " +
            "남은 폭은 끝 반원 캡이 감싼다")]
        [SerializeField, Range(0f, 1f)] private float _taperTipWidth = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _taperInkLoss = 0.25f;
        [Tooltip("수필 방향을 마지막 N샘플의 평균 진행 방향(현)으로 잡는다 — 붓을 떼는 순간 손이 " +
            "살짝 틀어져도 꼬리가 옆으로 새지 않게. 1이면 구 방식(마지막 구간만)")]
        [SerializeField, Range(1, 12)] private int _taperDirectionWindow = 5;

        [Header("먹 농도 — 먹이 적으면 획이 마른다(ART-UI LOCKED, 먹 미터의 보조 언어)")]
        [Tooltip("느린 획의 농도(진함)")]
        [SerializeField, Range(0f, 1f)] private float _maxDensity = 1f;
        [Tooltip("빠른 획의 농도(옅음)")]
        [SerializeField, Range(0f, 1f)] private float _minDensity = 0.8f;
        [Tooltip("이 잔량 비율까지는 항상 진하게, 그 아래부터 급격히 마른다 — " +
            "선형 감쇠는 「거의 항상 조금 흐린」 상태를 만들어 가독을 해친다")]
        [SerializeField, Range(0.02f, 0.5f)] private float _paleThreshold = 0.1f;
        [Tooltip("먹이 다 마른 붓의 농도 하한")]
        [SerializeField, Range(0.05f, 1f)] private float _dryDensityFloor = 0.25f;

        [Header("먹 룩 — 번짐·갈필·증발 (SPEC-SPIKE-INK-LOOKDEV §5)")]
        [Tooltip("번짐 여유 폭 비율 — 메시를 (1+margin)배 폭으로 만들고 셰이더가 명목 가장자리를 안쪽에 둔다. " +
            "widthMultiplier 훅을 이 용도로 점유한다")]
        [SerializeField, Range(0f, 0.5f)] private float _bleedMargin = 0.15f;
        [Tooltip("번짐 시정수 τ(초) — 붓 뒤를 따라 빠르게 번지다 느려지는 곡선(1-e^(-age/τ)). 작을수록 빠르다")]
        [SerializeField, Min(0.01f)] private float _bleedTau = 0.45f;
        [Tooltip("그리는 순간의 폭 비율 — 낮을수록 갓 그은 획이 얇고, 번지며 본 폭이 된다")]
        [SerializeField, Range(0.3f, 1f)] private float _bleedStartWidth = 0.85f;
        [Tooltip("갈필 노이즈 결 밀도(오브젝트 공간)")]
        [SerializeField, Min(0.1f)] private float _noiseScale = 40f;
        [Tooltip("갈필 세기 — 먹이 마를수록 가장자리부터 갈라지는 정도")]
        [SerializeField, Range(0f, 1f)] private float _crackStrength = 0.55f;
        [SerializeField, Range(0.01f, 0.6f)] private float _edgeSoftness = 0.12f;
        [Tooltip("가장자리 지터 — 종이 위 먹의 요철(농도 무관, 항상). 기필 반원 완화도 겸한다(ART-INK-LOOK §4.3-4.4)")]
        [SerializeField, Range(0f, 0.3f)] private float _edgeJitter = 0.07f;
        [Tooltip("피격·불발 시 먹이 증발하는 시간(초)")]
        [SerializeField, Min(0.05f)] private float _dissolveDuration = 0.4f;
        [Tooltip("투시 농도 — 작도 중 몸·벽에 가려진 획이 비쳐 보이는 불투명도(3차 카메라 검수). 커밋 후엔 투시 없음")]
        [SerializeField, Range(0f, 1f)] private float _xrayOpacity = 0.35f;

        [Header("커밋 문양 — 글자가 그 자리에서 전통 문양으로 변형된다(4차 검수) [TEST]")]
        [Tooltip("글자 크기 대비 문양 배율 — 개화부 원 크기(≈2.3m/유닛 스케일)를 감안한 시작값")]
        [SerializeField, Min(0.1f)] private float _patternScale = 0.6f;
        [Tooltip("문양 수명(초) — 70% 시점에 방출 중단(잔여 입자 자연 소멸), 끝에 파괴. 지속 발광 금지 규약 합치")]
        [SerializeField, Min(0.2f)] private float _patternLifetime = 2.2f;
        [Tooltip("허공(비명중) 공격의 착탄 거리(m) — 카메라 전방 이 지점에서 개화. 명중 비행은 CombatConfig 소유")]
        [SerializeField, Min(2f)] private float _patternMissRange = 14f;

        [Header("술식 플래시 — 커밋 순간 속성색 발광(ART-INK 중등 광원: 순간만, 지속 금지)")]
        [SerializeField, Min(0.05f)] private float _flashDuration = 0.6f;
        [Tooltip("플래시 시간 곡선(x=0..1 진행, y=강도 배율) — 어택 후 먹으로 가라앉는 모양")]
        [SerializeField] private AnimationCurve _flashCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.1f, 1f), new Keyframe(1f, 0f));
        [Tooltip("틴트 비율 — 피크에서 먹이 속성색으로 물드는 정도(LDR 안전 — 백색 포화 금지의 주역)")]
        [SerializeField, Range(0f, 1f)] private float _flashTint = 0.85f;
        [Tooltip("HDR 가산 배율 — Bloom threshold를 넘겨 속성색 글로우를 만드는 성분(ART-INK-LOOK §4.5)")]
        [SerializeField, Min(0f)] private float _flashAdd = 1.5f;
        [Tooltip("플래시 후 알파 페이드아웃(초) — 속성색인 채로 사라지는 시간")]
        [SerializeField, Min(0.05f)] private float _fadeOutDuration = 0.35f;
        [Header("속성 모티프 — 글자에 붙는 작은 스프라이트 (가독: 무슨 속성인지 즉시 읽힘 — ART-INK-LOOK §4.6)")]
        [SerializeField, Range(1, 12)] private int _motifCount = 4;
        [Tooltip("스프라이트 크기 — 글자 최대 변 대비 비율")]
        [SerializeField, Range(0.05f, 0.6f)] private float _motifSize = 0.18f;
        [Tooltip("획 위 부착점에서 벗어나는 산포(글자 크기 대비)")]
        [SerializeField, Range(0f, 0.5f)] private float _motifScatter = 0.08f;
        [Tooltip("회전 지터(±도) — 잎사귀가 제각각 붙게")]
        [SerializeField, Range(0f, 90f)] private float _motifAngleJitter = 30f;

        [Header("위력 근사 [TEST] — 필세(형×세)의 표현용 근사. Spellcraft 정식 공식 도입 시 교체(§10 예외 2)")]
        [Tooltip("정확도(평균 거리)→0..1 구간: x=최악 거리(0점), y=최선 거리(1점)")]
        [SerializeField] private Vector2 _powerAccuracyRange = new Vector2(1.6f, 0.6f);
        [Tooltip("세(총 획 시간 초)→0..1 구간: x=느림(0점), y=빠름(1점)")]
        [SerializeField] private Vector2 _powerSpeedRange = new Vector2(4f, 1f);
        [Tooltip("위력 p→플래시 배율 곡선 — 못 그려도 바닥은 살짝 빛나게 하한을 둔다")]
        [SerializeField] private AnimationCurve _powerToFlashCurve = AnimationCurve.Linear(0f, 0.2f, 1f, 1f);

        [Header("보정 — 서예 기준의 기하 번역 (SPEC-SPIKE-BRUSH-RENDERER §14)")]
        [Tooltip("위치 평활(Chaikin) 패스 — 잔떨림 제거(필력). 꺾임(折)을 살리려면 낮게")]
        [SerializeField, Range(0, 3)] private int _positionSmoothing = 2;
        [Tooltip("폭 저역 통과 패스 — 굵기 튐 제거(획질)")]
        [SerializeField, Range(0, 4)] private int _widthSmoothing = 2;
        [Tooltip("양끝 반원 캡 분할 수 — 장봉(둥근 기필)·회봉(둥근 수필). 0=캡 없음(노봉)")]
        [SerializeField, Range(0, 12)] private int _capSegments = 6;

        public Color InkColor => _inkColor;
        public float BaseWidth => _baseWidth;
        public float FastSpeed => _fastSpeed;
        public float MinWidthFactor => _minWidthFactor;
        public float MaxWidthFactor => _maxWidthFactor;
        public float WidthNoise => _widthNoise;
        public float WidthNoiseFrequency => _widthNoiseFrequency;
        public float SpeedSmoothing => _speedSmoothing;
        public float StartPressFactor => _startPressFactor;
        public int StartPressSamples => _startPressSamples;
        public int TaperSteps => _taperSteps;
        public float TaperLength => _taperLength;
        public float TaperTipWidth => _taperTipWidth;
        public float TaperInkLoss => _taperInkLoss;
        public int TaperDirectionWindow => _taperDirectionWindow;
        public int PositionSmoothing => _positionSmoothing;
        public int WidthSmoothing => _widthSmoothing;
        public int CapSegments => _capSegments;
        public float MaxDensity => _maxDensity;
        public float MinDensity => _minDensity;
        public float PaleThreshold => _paleThreshold;
        public float DryDensityFloor => _dryDensityFloor;
        public float BleedMargin => _bleedMargin;
        public float BleedTau => _bleedTau;
        public float BleedStartWidth => _bleedStartWidth;
        public float NoiseScale => _noiseScale;
        public float CrackStrength => _crackStrength;
        public float EdgeSoftness => _edgeSoftness;
        public float DissolveDuration => _dissolveDuration;
        public float EdgeJitter => _edgeJitter;
        public float XrayOpacity => _xrayOpacity;
        public float PatternScale => _patternScale;
        public float PatternLifetime => _patternLifetime;
        public float PatternMissRange => _patternMissRange;
        public float FlashDuration => _flashDuration;
        public float FlashTint => _flashTint;
        public float FlashAdd => _flashAdd;
        public float FadeOutDuration => _fadeOutDuration;
        public int MotifCount => _motifCount;
        public float MotifSize => _motifSize;
        public float MotifScatter => _motifScatter;
        public float MotifAngleJitter => _motifAngleJitter;

        public float EvaluateFlash01(float normalizedTime)
        {
            return Mathf.Max(0f, _flashCurve.Evaluate(Mathf.Clamp01(normalizedTime)));
        }

        // 필세(형×세)의 표현용 근사 [TEST] — 정확도·속도를 각각 0..1로 눕혀 곱한 뒤 곡선을 통과시킨다.
        // 위력의 정본은 Spellcraft 소유다: 정식 공식이 생기면 이 근사는 그 결과를 「읽는」 것으로 교체된다.
        public float EstimateFlashPower(float averageDistance, float strokeDuration)
        {
            float accuracy = Mathf.InverseLerp(_powerAccuracyRange.x, _powerAccuracyRange.y, averageDistance);
            float speed = Mathf.InverseLerp(_powerSpeedRange.x, _powerSpeedRange.y, strokeDuration);
            return Mathf.Clamp01(_powerToFlashCurve.Evaluate(accuracy * speed));
        }

        // 먹 잔량(0~1) → 농도 배율. 임계 이상은 1.0으로 평평하고, 그 아래부터 하한까지 떨어진다.
        // 잔량을 「읽기만」 한다 — 먹을 깎는 것은 Combat 소유다(SPEC §5.2의 방향 규칙).
        public float InkChargeToDensity(float inkNormalized)
        {
            float charge = inkNormalized >= _paleThreshold
                ? 1f
                : inkNormalized / Mathf.Max(_paleThreshold, 0.001f);
            return Mathf.Lerp(_dryDensityFloor, 1f, charge);
        }

        // 표현용 속도(px/s) → 폭 배율. 빠를수록 가늘다.
        public float SpeedToWidthFactor(float speed)
        {
            float t = Mathf.InverseLerp(0f, _fastSpeed, speed);
            return Mathf.Lerp(_maxWidthFactor, _minWidthFactor, t);
        }

        // 표현용 속도(px/s) → 농도. 빠를수록 옅다.
        public float SpeedToDensity(float speed)
        {
            float t = Mathf.InverseLerp(0f, _fastSpeed, speed);
            return Mathf.Lerp(_maxDensity, _minDensity, t);
        }
    }
}
