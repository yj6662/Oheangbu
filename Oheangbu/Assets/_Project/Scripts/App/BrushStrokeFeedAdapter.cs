using System.Collections.Generic;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Drawing;
using Oheangbu.Spellcraft;
using UnityEngine;
using UnityEngine.Serialization;

namespace Oheangbu.App
{
    // 단일 Raw 입력 → 표현(BrushRender) 어댑터(SPEC-DRAWING-INPUT §6의 Render Adapter)이자
    // 붓 형태 언어의 계산 지점(SPEC-SPIKE-BRUSH-RENDERER §3) + 먹 룩 구동부(SPEC-SPIKE-INK-LOOKDEV §5·§7):
    //   · 획별 머티리얼 인스턴스(스텐실 Ref·노이즈 시드 — Ref는 MPB로 세팅 불가, §11.1)
    //   · 전역 시계 _InkNow(번짐은 점 나이로 셰이더가 자율 진행)
    //   · 커밋 = 속성색 플래시(위력 근사 비례) 후 먹으로 가라앉으며 소멸
    //   · 불발·피격 = 먹 증발(디졸브)
    //
    // 여기서 「표현용」 속도를 다시 측정한다 — 인식이 쓰는 StrokePoint.Time을 읽어오지 않는다.
    // 이 컴포넌트를 통째로 꺼도 인식 결과는 그대로다(인식 불가침의 어댑터 층 증명).
    public sealed class BrushStrokeFeedAdapter : MonoBehaviour
    {
        [SerializeField] private DrawingInputController _input;
        [SerializeField] private BrushStyleSO _style;                     // 미감 수치 일체(데이터)
        [SerializeField] private Material _strokeMaterial;                // InkStroke 머티리얼 — 비우면 Sprites/Default 폴백(스텐실·증발·플래시 없음)
        [SerializeField] private Material _motifMaterial;                 // InkMotif 머티리얼 — 비우면 모티프 생략(플래시만)
        [SerializeField] private GameObject _commitPatternPrefab;         // [4차 검수] 커밋 문양(유료 에셋 슬롯 — §9 예외 4). 배정 시 모티프 대체, 비우면 복귀
        [SerializeField] private SpellVisualSetSO _visualSet;             // [FX-ASSETS §4.4] 어휘별 시각 분화 — 미등재 글자=위 기본 슬롯 폴백
        [SerializeField] private SpellBookSO _spellBook;                  // 술식 종류 조회(공격=투사체/패링=제자리 개화) — 어휘 미러 읽기만
        [SerializeField] private CombatConfigSO _combatConfig;            // 허공 비행 속도 참조(읽기만) — 명중 비행시간은 배선이 밀어준다
        [SerializeField] private ElementPaletteSO _elementPalette;        // 술식 플래시 속성색(초성→오행, TEST)
        [SerializeField] private DrawnLetterEventChannelSO _letterDrawn;  // 커밋 글자 — 속성·위력 근사 원자료(읽기만)
        [SerializeField, Min(0.01f)] private float _surfaceDistance = 1f; // 눈(피벗) 앞 작도면 거리 [TEST]
        [FormerlySerializedAs("_detachAnchor")]
        [SerializeField] private Transform _eyeAnchor;                    // [실험] 눈=CameraPivot. 작도면을 카메라가 아니라 눈 앞에 앵커한다. 비우면 카메라 기준(기존)

        private static readonly int StencilRefId = Shader.PropertyToID("_StencilRef");
        private static readonly int NoiseSeedId = Shader.PropertyToID("_NoiseSeed");
        private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
        private static readonly int CrackStrengthId = Shader.PropertyToID("_CrackStrength");
        private static readonly int EdgeMarginId = Shader.PropertyToID("_EdgeMargin");
        private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
        private static readonly int BleedTauId = Shader.PropertyToID("_BleedTau");
        private static readonly int BleedStartWidthId = Shader.PropertyToID("_BleedStartWidth");
        private static readonly int DissolveId = Shader.PropertyToID("_DissolveT");
        private static readonly int FadeMulId = Shader.PropertyToID("_FadeMul");
        private static readonly int EdgeJitterId = Shader.PropertyToID("_EdgeJitter");
        private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
        private static readonly int FlashStrengthId = Shader.PropertyToID("_FlashStrength");
        private static readonly int FlashTintId = Shader.PropertyToID("_FlashTint");
        private static readonly int FlashAddId = Shader.PropertyToID("_FlashAdd");
        private static readonly int MotifTintId = Shader.PropertyToID("_Tint");
        private static readonly int MotifStrengthId = Shader.PropertyToID("_Strength");
        private static readonly int MotifFadeId = Shader.PropertyToID("_FadeMul");
        private static readonly int InkNowId = Shader.PropertyToID("_InkNow");
        private static readonly int XrayOpacityId = Shader.PropertyToID("_XrayOpacity");

        // 투시 패스의 LightMode 태그명 — SetShaderPassEnabled는 패스 Name이 아니라 이 태그를 받는다.
        // 그리는 동안 켜져 있고(인스턴스가 enabled 상속), 커밋(BeginFading)에서 끈다 — 유리판→세상 물체
        private const string XrayPassTag = "SRPDefaultUnlit";

        private readonly List<BrushStrokeRenderer> _strokes = new List<BrushStrokeRenderer>();
        private BrushStrokeRenderer _current;
        private Material _fallbackMaterial;
        private int _stencilRef; // 1..255 순환 — 살아있는 획이 없을 때 리셋(§11.1)

        // 소멸 진행 중인 글자 묶음 — 커밋(플래시)과 증발(디졸브)을 같은 틀로 굴린다
        private sealed class FadingGroup
        {
            public readonly List<BrushStrokeRenderer> Strokes = new List<BrushStrokeRenderer>();
            public float StartTime;
            public bool IsFlash;      // true=커밋 플래시 / false=먹 증발
            public Color FlashColor;
            public float FlashPower;  // 위력 근사 0..1 [TEST]
            public float PeakFlash01; // 도달한 최대 강도 — 어택 후 유지(속성색인 채로 사라진다)
            // 속성 모티프(§4.6) — 글자에 붙는 작은 스프라이트 몇 개(잎사귀·불꽃 낱개).
            // 붙은 자리에서 움직이지 않는다 — 크기만 0에서 은은히 자란다(상승 폐기 — 예준 4차 검수)
            public readonly List<Transform> Motifs = new List<Transform>();
            public readonly List<Material> MotifMaterials = new List<Material>();
            public readonly List<Vector3> MotifBaseScales = new List<Vector3>();
        }

        private readonly List<FadingGroup> _fading = new List<FadingGroup>();

        // 커밋 플래시 스태시 — _letterDrawn(글자)이 Committed(bool)보다 먼저 발화한다(같은 프레임)
        private bool _hasPendingFlash;
        private Color _pendingFlashColor;
        private float _pendingFlashPower;
        private Texture2D _pendingMotif;
        private bool _pendingAttack; // 공격 작도인가 — 문양이 투사체로 날아갈지(5차 검수) 제자리 개화할지
        private bool _pendingParry;  // 패링 작도인가 — 방어막 가독 타임라인(SPELL-FIDELITY §4.6) 적용 여부
        private Transform _pendingAttackTarget; // 배선이 밀어준 명중 대상(유도 추적) — 없으면 허공 착탄
        private float _pendingAttackDuration;   // 배선이 확정한 비행시간 — 피해 착탄 시각과 같은 시계
        private bool _pendingCastFailed;        // 배선이 알린 시전 불성립(먹 부족·미등재) — 플래시·문양 대신 불발 증발
        private GameObject _pendingFxPrefab;    // 어휘별 프리팹 스태시(_visualSet 조회) — 없으면 기본 슬롯
        private float _pendingFxScaleMul = 1f;
        private float _pendingFxArcHeight; // 연출 포물선 높이(마 — §3.1) — 매핑 SO에서 읽기만

        // 표현용 상태 — 획 하나가 그려지는 동안의 붓 상태다. 인식 데이터와 공유하지 않는다.
        private Vector2 _lastInputScreen;  // 직전 입력 좌표 — 속도 측정용
        private Vector3 _lastLocal;
        private bool _hasLastLocal;
        private int _sampleInStroke;
        private float _noiseSeed;
        private float _lastSampleTime;
        private float _smoothedSpeed;   // 표현용 속도의 저역 통과 값
        private bool _hasSpeed;         // 첫 점은 이동거리 0이라 속도를 잴 수 없다 — 중립 폭으로 시작
        private float _strokeScale = 1f; // 획 화면 등가 계수 — 깊이·FOV가 1인칭과 달라도 폭·수필·갈필 결이 화면상 동일
        private float _baseFov;          // 작도 진입 전 기준 FOV(클로즈업 FOV 스냅과 무관한 등가 계산용)

        // 먹 잔량(0~1) — 지금은 밖에서 넣어준다(하네스 슬라이더).
        // 먹 풀이 생기면 이 값만 꽂으면 된다: 읽기 전용이며 여기서 먹을 깎지 않는다(SPEC §5.2).
        public float InkNormalized { get; set; } = 1f;

        private void OnEnable()
        {
            // 기준 FOV 캡처 — 씬 시작 시(작도 전)의 값. 클로즈업 FOV 스냅 이후의 획도 이 기준으로 등가 환산
            var baseCam = Camera.main;
            if (_baseFov <= 0f && baseCam != null) _baseFov = baseCam.fieldOfView;

            if (_input == null) return;
            _input.StrokeStarted += OnStrokeStarted;
            _input.StrokePointAdded += OnStrokePointAdded;
            _input.StrokeEnded += OnStrokeEnded;
            _input.Committed += OnCommitted;
            _input.ModeExited += EvaporateRemaining;
            _input.LetterInterrupted += EvaporateRemaining;
            if (_letterDrawn != null) _letterDrawn.Subscribe(OnLetterDrawn);
        }

        private void OnDisable()
        {
            if (_input != null)
            {
                _input.StrokeStarted -= OnStrokeStarted;
                _input.StrokePointAdded -= OnStrokePointAdded;
                _input.StrokeEnded -= OnStrokeEnded;
                _input.Committed -= OnCommitted;
                _input.ModeExited -= EvaporateRemaining;
                _input.LetterInterrupted -= EvaporateRemaining;
            }
            if (_letterDrawn != null) _letterDrawn.Unsubscribe(OnLetterDrawn);
            DestroyAllImmediate(); // 비활성 시에는 연출 없이 정리
        }

        private void Update()
        {
            // 전역 시계 — 번짐은 셰이더가 점 나이(_InkNow - 탄생)로 자율 진행한다(§5.2).
            // scaled time: 일시정지하면 먹 번짐도 멈추는 게 자연스럽다.
            Shader.SetGlobalFloat(InkNowId, Time.time);
            TickFading();
        }

        private void OnStrokeStarted()
        {
            var go = new GameObject($"BrushStroke_{_strokes.Count}");
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * ComputeStrokeScale();
            _current = go.AddComponent<BrushStrokeRenderer>();

            // 획마다 다른 결 — 같은 시드를 쓰면 모든 획이 똑같이 떨린다(폭 노이즈·갈필 공용)
            _noiseSeed = Random.value * 100f;

            // 화면에 살아있는 획이 없으면 스텐실 Ref를 처음부터 — 순환 충돌(같은 Ref 겹침=구멍) 예방
            if (_strokes.Count == 0 && _fading.Count == 0) _stencilRef = 0;

            Material material;
            bool owns = false;
            if (_strokeMaterial != null)
            {
                // 획별 인스턴스 — 스텐실 Ref는 렌더 스테이트라 MPB로 세팅할 수 없다(§11.1).
                // SRP Batcher는 셰이더 단위 배칭이라 인스턴스가 늘어도 배칭은 유지된다.
                material = new Material(_strokeMaterial);
                _stencilRef = _stencilRef % 255 + 1;
                material.SetFloat(StencilRefId, _stencilRef);
                material.SetFloat(NoiseSeedId, _noiseSeed);
                ApplyLookValues(material);
                owns = true;
            }
            else
            {
                material = GetFallbackMaterial();
            }

            _current.Configure(material, _style, owns, _strokes.Count);
            _strokes.Add(_current);

            _sampleInStroke = 0;
            _hasLastLocal = false;
            _hasSpeed = false;
            _lastSampleTime = Time.unscaledTime;
        }

        private void OnStrokePointAdded(Vector2 screen)
        {
            if (_current == null || _style == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            float now = Time.unscaledTime;
            float dt = Mathf.Max(now - _lastSampleTime, 0.0001f);

            if (_sampleInStroke == 0) _lastInputScreen = screen;

            // 표현용 속도 — 입력 좌표의 이동량으로 잰다(px/s).
            // 첫 점은 이동거리 0 = 「속도 0」이 되는데 이건 느리게 그은 게 아니라 측정 불가의 부산물이다 —
            // 그대로 쓰면 시작이 최대 폭으로 부풀므로(1차 육안 지적), 첫 실측값이 나올 때까지 중립으로 둔다.
            float rawSpeed = Vector2.Distance(screen, _lastInputScreen) / dt;
            _lastInputScreen = screen;
            _lastSampleTime = now;
            if (_sampleInStroke > 0)
            {
                _smoothedSpeed = _hasSpeed
                    ? Mathf.Lerp(_smoothedSpeed, rawSpeed, _style.SpeedSmoothing)
                    : rawSpeed;
                _hasSpeed = true;
            }
            float speed = _smoothedSpeed;

            // 붓끝 관성은 폐기됐다(2026-08-27 예준 — BRUSH-RENDERER §11.1): 표현은 입력과 정확히 일치한다.
            // 깊이 = 눈(피벗) 앞 _surfaceDistance — 카메라가 뒤로 물러난 포즈(숄더뷰·클로즈업)에서도
            // 작도면은 플레이어 모델 앞에 선다(2차 카메라 검수). 점은 획-로컬(÷스케일)로 저장 —
            // 폭·수필·갈필 결의 화면 등가는 획 localScale이 담당한다.
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, EffectiveSurfaceDistance(cam)));
            Vector3 local = transform.InverseTransformPoint(world) / _strokeScale;

            float traveled = _hasLastLocal ? Vector3.Distance(local, _lastLocal) : 0f;
            _lastLocal = local;
            _hasLastLocal = true;
            _sampleInStroke++;

            // 탄생 시각은 scaled — 번짐(연출)의 시계와 같아야 한다(§5.2)
            _current.AddPoint(local, ComputeWidth(speed, traveled), ComputeInk(speed), Time.time);
        }

        // 작도면 유효 깊이 — 카메라가 아니라 눈(피벗) 앞 _surfaceDistance에 평면을 앵커한다(2차 카메라 검수).
        // 카메라 블렌드·붐이 카메라를 움직여도 dot 항이 자동 상쇄해 평면은 항상 피벗 앞에 남는다.
        // 1인칭(카메라=피벗)은 dot≈0이라 기존과 동일 — 대조군 보존.
        private float EffectiveSurfaceDistance(Camera cam)
        {
            if (_eyeAnchor == null) return _surfaceDistance;
            float toEye = Vector3.Dot(_eyeAnchor.position - cam.transform.position, cam.transform.forward);
            return _surfaceDistance + Mathf.Max(0f, toEye);
        }

        // 획 화면 등가 계수 = 프러스텀 반높이 비 — 깊이가 멀어지거나 FOV가 스냅돼도
        // 같은 제스처가 같은 화면 굵기·수필 길이·갈필 결을 남긴다(획 단위 고정)
        private float ComputeStrokeScale()
        {
            _strokeScale = 1f;
            var cam = Camera.main;
            if (cam == null || _baseFov <= 0f) return _strokeScale;

            float frustumNow = EffectiveSurfaceDistance(cam) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float frustumBase = _surfaceDistance * Mathf.Tan(_baseFov * 0.5f * Mathf.Deg2Rad);
            if (frustumBase > 0.0001f) _strokeScale = frustumNow / frustumBase;
            return _strokeScale;
        }

        // 폭 = 기본 × 속도(빠르면 가늘게) × 유기적 노이즈 × 기필(시작 눌림)
        private float ComputeWidth(float speed, float traveled)
        {
            // 속도 실측 전(첫 점)에는 중립 배율 1 — 시작 뭉침 방지
            float speedFactor = _hasSpeed ? _style.SpeedToWidthFactor(speed) : 1f;
            float width = _style.BaseWidth * speedFactor;

            if (_style.WidthNoise > 0f)
            {
                // 위치가 아니라 「그은 거리」로 노이즈를 훑는다 — 같은 자리를 맴돌아도 결이 흐른다
                float n = Mathf.PerlinNoise(_noiseSeed, (_sampleInStroke + traveled) * _style.WidthNoiseFrequency);
                width *= 1f + _style.WidthNoise * (n - 0.5f) * 2f;
            }

            // 기필(起筆): 획 시작 몇 샘플은 눌린 채로 시작해 점점 평상 굵기로 풀린다
            int pressSamples = _style.StartPressSamples;
            if (pressSamples > 0 && _sampleInStroke <= pressSamples)
            {
                float t = (_sampleInStroke - 1f) / pressSamples;
                width *= Mathf.Lerp(_style.StartPressFactor, 1f, Mathf.Clamp01(t));
            }
            return width;
        }

        // 농도 = 속도(빠르면 옅게) × 먹 잔량(적을수록 마름)
        // 잔량은 읽기만 한다 — 먹을 깎는 것은 Combat 소유다(SPEC §5.2의 방향 규칙).
        private float ComputeInk(float speed)
        {
            float density = _hasSpeed ? _style.SpeedToDensity(speed) : _style.MaxDensity;
            return density * _style.InkChargeToDensity(InkNormalized);
        }

        private void OnStrokeEnded()
        {
            if (_current == null) return;
            _current.EndStroke();
            _current = null;
        }

        // ---- 소멸 연출 (SPEC-SPIKE-INK-LOOKDEV §7) ----

        // _letterDrawn은 Committed(bool)보다 먼저, 같은 프레임에 발화한다 — 여기서 속성색·위력 근사를 스태시
        private void OnLetterDrawn(DrawnLetter letter)
        {
            if (_style == null) return;
            char initial = InitialToChar(letter.Initial);
            _pendingFlashColor = _elementPalette != null ? _elementPalette.GetFlashColor(initial) : Color.white;
            _pendingMotif = _elementPalette != null ? _elementPalette.GetMotif(initial) : null;
            // 위력 근사 [TEST — §10 예외 2]: 정본은 Spellcraft. 표현은 근사치를 「읽기만」 한다.
            _pendingFlashPower = _style.EstimateFlashPower(letter.AverageDistance, letter.StrokeDuration);
            // 술식 종류도 읽기만 — 공격이면 문양이 투사체로 나간다(5차 검수). 미등재 글자=제자리 개화
            SpellBookSO.Entry spell = default;
            bool known = _spellBook != null && _spellBook.TryGet(letter.Letter, out spell);
            _pendingAttack = known && spell.Kind != SpellKind.Parry;
            _pendingParry = known && spell.Kind == SpellKind.Parry;
            // 어휘별 시각 분화(FX-ASSETS §4.4) — 표현 조회만. 미등재면 기본 슬롯이 받는다
            _pendingFxPrefab = null;
            _pendingFxScaleMul = 1f;
            _pendingFxArcHeight = 0f;
            if (_visualSet != null && _visualSet.TryGet(letter.Letter, out var visual))
            {
                _pendingFxPrefab = visual.FxPrefab;
                _pendingFxScaleMul = visual.ScaleMul > 0f ? visual.ScaleMul : 1f;
                _pendingFxArcHeight = Mathf.Max(0f, visual.ArcHeight);
            }
            _hasPendingFlash = true;
        }

        private void OnCommitted(bool success)
        {
            // 시전 불성립(먹 부족·미등재 — 배선이 알림)은 인식이 성공했어도 불발 취급이다(§10.1) —
            // 술식이 서지 않았는데 플래시·문양을 틀지 않는다(7차 검수)
            if (success && _hasPendingFlash && !_pendingCastFailed)
            {
                BeginFading(isFlash: true, _pendingFlashColor, _pendingFlashPower);
            }
            else
            {
                BeginFading(isFlash: false, default, 0f); // 불발 — 먹 증발
            }
            _hasPendingFlash = false;
            _pendingCastFailed = false;
            // 명중 스태시는 커밋마다 무조건 소거 — 어떤 조기 반환 경로로도 다음 글자에 잔상이 새지 않게.
            // OnLetterDrawn에서 지우면 안 된다: 채널 구독 순서상 배선의 푸시가 먼저 올 수 있다
            _pendingAttackTarget = null;
            _pendingAttackDuration = 0f;
            _pendingFxPrefab = null;
            _pendingFxScaleMul = 1f;
            _pendingFxArcHeight = 0f;
            _pendingParry = false;
        }

        // 피격(글자만 소멸)·조용한 취소 등 커밋 경로 밖의 소거 — 남아 있는 획을 증발시킨다.
        // 커밋 직후의 ModeExited는 _strokes가 이미 비어 있어 아무 일도 하지 않는다.
        private void EvaporateRemaining()
        {
            BeginFading(isFlash: false, default, 0f);
        }

        private void BeginFading(bool isFlash, Color flashColor, float flashPower)
        {
            if (_strokes.Count == 0) { _current = null; return; }

            var group = new FadingGroup
            {
                StartTime = Time.time,
                IsFlash = isFlash,
                FlashColor = flashColor,
                FlashPower = flashPower,
            };
            foreach (var stroke in _strokes)
            {
                if (stroke == null) continue;
                group.Strokes.Add(stroke);
                // 소멸이 시작되는 순간 월드에 떼어놓는다(예준 지시) — 작도면이 이미 눈(피벗) 앞 평면이라
                // 그 자리가 곧 「플레이어 모델 앞」이다. 유리판이었던 글자는 이제 세상 물체 —
                // 투시 패스를 꺼서 차폐가 세상 규칙으로 돌아간다(3차 카메라 검수)
                stroke.transform.SetParent(null, true);
                stroke.OwnedMaterial?.SetShaderPassEnabled(XrayPassTag, false);
            }
            _strokes.Clear();
            _current = null;
            if (isFlash)
            {
                // 문양이 배정돼 있으면 모티프 대신 — 글자가 그 자리에서 전통 문양으로 변형된다(4차 검수) [TEST]
                if (_pendingFxPrefab != null || _commitPatternPrefab != null) SpawnPattern(group);
                else SpawnMotifs(group);
            }
            _fading.Add(group);
        }

        // 커밋 문양(4·5차 검수): 글자가 있던 자리에서 전통 문양으로 변형된다 — 색은 팔레트 속성색
        // (색=의미 단일 출처 — group.FlashColor가 이미 속성 연동). 수명·틴트는 PatternEffectLifetime.
        //   패링 작도 = 제자리 개화(방어막의 얼굴) / 공격 작도 = 문양 투사체가 날아가 착탄 개화.
        // 피해 판정은 Combat의 즉발 그대로다(§11 절단면) — 여기는 연출만 후행한다 [TEST].
        private void SpawnPattern(FadingGroup group)
        {
            if (_style == null || !TryComputeLetterBounds(group, out Bounds bounds)) return;

            // 어휘별 프리팹 우선(FX-ASSETS §4.4), 미등재=기본 슬롯 — 기존 동작 비파괴
            GameObject prefab = _pendingFxPrefab != null ? _pendingFxPrefab : _commitPatternPrefab;
            if (prefab == null) return;

            float letterSize = Mathf.Max(bounds.size.x, bounds.size.y, 0.01f);
            var go = Instantiate(prefab, bounds.center, transform.rotation);
            go.name = "SpellPattern";
            go.transform.localScale = Vector3.one * (letterSize * _style.PatternScale * _pendingFxScaleMul);
            go.SetActive(true); // 일부 에셋 프리팹은 비활성 자식 포함 — 루트만 보장

            // 자체 시계 연출(8차 검수 소 → P4 일반화): 프리팹이 SpellSequenceEffect를 품으면 문양은
            // 제자리 개화, 연출(일제·솟음·전진)이 자체 시계로 목표를 향한다. 피해는 배선의 착탄 시계
            // 그대로(연출≠실판정 §9-1)
            var sequence = go.GetComponentInChildren<SpellSequenceEffect>(true);
            if (_pendingAttack && sequence != null)
            {
                Transform sequenceTarget = _pendingAttackTarget;
                float sequenceDuration = _pendingAttackDuration;
                _pendingAttackTarget = null;
                _pendingAttackDuration = 0f;
                // 판정 착탄 시계 전달(SPELL-FIDELITY §4.4) — 단일 유도 연출(가 랜스)의 동기 통로.
                // 광역 자체 시계는 기본 no-op로 무시한다
                if (sequenceDuration > 0f) sequence.SetImpactClock(sequenceDuration);
                sequence.Begin(bounds.center, sequenceTarget, MissPoint(bounds.center), group.FlashColor);
                PatternEffectLifetime.AttachBloom(go, _style.PatternLifetime, group.FlashColor);
                return;
            }

            if (_pendingAttack)
            {
                // 소비하며 비운다 — 다음 글자(패링·먹 부족 허공)에 잔상이 남지 않게
                Transform target = _pendingAttackTarget;
                float duration = _pendingAttackDuration;
                _pendingAttackTarget = null;
                _pendingAttackDuration = 0f;

                if (target == null || duration <= 0f)
                {
                    // 허공 발사(비명중·먹 부족) — 조준(카메라) 전방으로 연출만 날아가 개화
                    float speed = _combatConfig != null ? Mathf.Max(1f, _combatConfig.SpellProjectileSpeed) : 18f;
                    duration = _style.PatternMissRange / speed;
                }

                PatternEffectLifetime.AttachProjectile(go, _style.PatternLifetime, group.FlashColor,
                    target, MissPoint(bounds.center), duration, _pendingFxArcHeight);
            }
            else if (_pendingParry && _combatConfig != null)
            {
                // 방어막 가독(SPELL-FIDELITY §4.6): 문양 수명=GuardDuration — 방어막이 사는 동안
                // 문양도 산다. 창(ParryWindow) 동안 진하게 → 은은 → 꼬리 1s 스러짐(수치 읽기만)
                PatternEffectLifetime.AttachBloom(go, _combatConfig.GuardDuration, group.FlashColor,
                    _combatConfig.ParryWindow, 1f);
            }
            else
            {
                PatternEffectLifetime.AttachBloom(go, _style.PatternLifetime, group.FlashColor);
            }
        }

        // 배선(CombatLoopWiring)이 커밋 프레임에 밀어주는 명중 문맥 — 시각이 하나뿐이라 피해와 연출이 어긋나지 않는다.
        // _letterDrawn(배선의 판정)이 Committed(여기 소비)보다 먼저 발화하는 순서에 기댄다(같은 프레임 보장).
        public void SetPatternAttackTarget(Transform target, float flightDuration)
        {
            _pendingAttackTarget = target;
            _pendingAttackDuration = flightDuration;
        }

        // 시전 불성립(먹 부족·미러 밖 글자) — 배선이 커밋 프레임에 알린다. 표현은 불발(증발)로 따른다
        public void NotifyCastFailed()
        {
            _pendingCastFailed = true;
        }

        // 허공 착탄점 — 조준(카메라) 전방. 규칙이 아니라 연출의 목적지다
        private Vector3 MissPoint(Vector3 origin)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                return cam.transform.position + cam.transform.forward * _style.PatternMissRange;
            }
            return origin + transform.forward * _style.PatternMissRange;
        }

        // 글자의 월드 경계 — 표현 점 전체를 감싼다(문양 위치·크기의 기준)
        private static bool TryComputeLetterBounds(FadingGroup group, out Bounds bounds)
        {
            bounds = default;
            bool has = false;
            foreach (var stroke in group.Strokes)
            {
                if (stroke == null) continue;
                var strokeTransform = stroke.transform;
                var data = stroke.Data.Points;
                for (int i = 0; i < data.Count; i++)
                {
                    Vector3 world = strokeTransform.TransformPoint(data[i].Position);
                    if (!has) { bounds = new Bounds(world, Vector3.zero); has = true; }
                    else bounds.Encapsulate(world);
                }
            }
            return has;
        }

        // 속성 모티프(§4.6): 배경 그림이 아니라 「획 위 몇 곳에 붙는 작은 스프라이트」다 —
        // 목=잎사귀가 글자에 붙고, 화=불꽃이 약하게 피어오른다. 목적은 장식이 아니라 가독:
        // 무슨 속성을 썼는지 즉시 읽혀야 한다(예준 지시 2026-08-27).
        private void SpawnMotifs(FadingGroup group)
        {
            if (_motifMaterial == null || _pendingMotif == null || _style == null) return;

            // 부착점 후보 = 획의 표현 점들(월드) — 스프라이트는 획 「위」에 앉는다
            var points = new List<Vector3>();
            var bounds = new Bounds();
            bool hasBounds = false;
            foreach (var stroke in group.Strokes)
            {
                if (stroke == null) continue;
                var strokeTransform = stroke.transform;
                var data = stroke.Data.Points;
                for (int i = 0; i < data.Count; i++)
                {
                    Vector3 world = strokeTransform.TransformPoint(data[i].Position);
                    points.Add(world);
                    if (!hasBounds) { bounds = new Bounds(world, Vector3.zero); hasBounds = true; }
                    else bounds.Encapsulate(world);
                }
            }
            if (points.Count == 0) return;

            float letterSize = Mathf.Max(bounds.size.x, bounds.size.y, 0.01f);

            for (int k = 0; k < _style.MotifCount; k++)
            {
                Vector3 position = points[Random.Range(0, points.Count)];
                Vector2 scatter = Random.insideUnitCircle * (_style.MotifScatter * letterSize);
                position += transform.right * scatter.x + transform.up * scatter.y;

                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "SpellMotif";
                Destroy(go.GetComponent<Collider>());
                // 모티프도 월드에 남는 글자 곁에 — 카메라를 따라가지 않는다
                go.transform.SetParent(null, true);
                go.transform.rotation = transform.rotation
                    * Quaternion.Euler(0f, 0f, Random.Range(-_style.MotifAngleJitter, _style.MotifAngleJitter));
                go.transform.position = position;
                // 크기 지터 상수는 §4.6 실측 후 필요 시 SO 승격
                float size = letterSize * _style.MotifSize * Random.Range(0.75f, 1.3f);
                var baseScale = new Vector3(size, size, 1f);
                go.transform.localScale = Vector3.zero; // 어택과 함께 자라난다(TickFading)

                var material = new Material(_motifMaterial);
                material.mainTexture = _pendingMotif;
                material.SetColor(MotifTintId, group.FlashColor);
                var quadRenderer = go.GetComponent<MeshRenderer>();
                quadRenderer.sharedMaterial = material;
                quadRenderer.sortingOrder = 100; // 글자 획 앞 — 「붙어 있는」 것이 보여야 한다
                quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                quadRenderer.receiveShadows = false;

                group.Motifs.Add(go.transform);
                group.MotifMaterials.Add(material);
                group.MotifBaseScales.Add(baseScale);
            }
        }

        private void TickFading()
        {
            if (_fading.Count == 0 || _style == null) return;

            for (int gi = _fading.Count - 1; gi >= 0; gi--)
            {
                var group = _fading[gi];
                float elapsed = Time.time - group.StartTime;
                bool done;

                if (group.IsFlash)
                {
                    // 어택은 곡선을 따라 오르되, 오른 뒤에는 내려오지 않는다(피크 유지) —
                    // 글자는 먹으로 돌아오지 않고 「속성색인 채로」 페이드아웃한다(예준 지시 2026-08-27).
                    float flashT = Mathf.Clamp01(elapsed / _style.FlashDuration);
                    group.PeakFlash01 = Mathf.Max(group.PeakFlash01, _style.EvaluateFlash01(flashT));
                    float strength = group.PeakFlash01 * group.FlashPower;
                    float fade = elapsed <= _style.FlashDuration
                        ? 1f
                        : 1f - Mathf.Clamp01((elapsed - _style.FlashDuration) / _style.FadeOutDuration);
                    done = elapsed >= _style.FlashDuration + _style.FadeOutDuration;

                    foreach (var stroke in group.Strokes)
                    {
                        var material = stroke != null ? stroke.OwnedMaterial : null;
                        if (material == null) continue; // 폴백 경로 — 연출 없이 시간만 채우고 파괴
                        material.SetColor(FlashColorId, group.FlashColor);
                        material.SetFloat(FlashStrengthId, strength);
                        material.SetFloat(FadeMulId, fade);
                    }

                    // 모티프: 붙은 자리에서 크기만 0 → 제 크기로 은은히 자라고, 글자와 함께 사라진다
                    if (group.Motifs.Count > 0)
                    {
                        float grow = Mathf.SmoothStep(0f, 1f,
                            Mathf.Clamp01(elapsed / Mathf.Max(_style.FlashDuration * 0.4f, 0.01f)));
                        for (int mi = 0; mi < group.Motifs.Count; mi++)
                        {
                            var motif = group.Motifs[mi];
                            if (motif == null) continue;
                            motif.localScale = group.MotifBaseScales[mi] * grow;
                            group.MotifMaterials[mi].SetFloat(MotifStrengthId, strength);
                            group.MotifMaterials[mi].SetFloat(MotifFadeId, fade);
                        }
                    }
                }
                else
                {
                    // 먹 증발 — 갈필 노이즈 임계를 쓸어올리는 디졸브(피격·불발은 빛나지 않는다)
                    float t = Mathf.Clamp01(elapsed / _style.DissolveDuration);
                    done = t >= 1f;
                    foreach (var stroke in group.Strokes)
                    {
                        var material = stroke != null ? stroke.OwnedMaterial : null;
                        if (material == null) continue;
                        material.SetFloat(DissolveId, t);
                    }
                }

                if (done)
                {
                    foreach (var stroke in group.Strokes)
                    {
                        if (stroke != null) Destroy(stroke.gameObject); // 머티리얼은 렌더러 OnDestroy가 파괴
                    }
                    DestroyMotifs(group);
                    _fading.RemoveAt(gi);
                }
            }
        }

        private static void DestroyMotifs(FadingGroup group)
        {
            foreach (var motif in group.Motifs)
            {
                if (motif != null) Destroy(motif.gameObject);
            }
            foreach (var material in group.MotifMaterials)
            {
                if (material != null) Destroy(material);
            }
            group.Motifs.Clear();
            group.MotifMaterials.Clear();
        }

        private void DestroyAllImmediate()
        {
            foreach (var stroke in _strokes)
            {
                if (stroke != null) Destroy(stroke.gameObject);
            }
            _strokes.Clear();
            foreach (var group in _fading)
            {
                foreach (var stroke in group.Strokes)
                {
                    if (stroke != null) Destroy(stroke.gameObject);
                }
                DestroyMotifs(group);
            }
            _fading.Clear();
            _current = null;
        }

        // 초성 Jamo → 문자 — 한글 자모라는 언어적 사실의 번역(밸런스 수치 아님)
        private static char InitialToChar(Jamo initial)
        {
            switch (initial)
            {
                case Jamo.Giyeok: return 'ㄱ';
                case Jamo.Nieun: return 'ㄴ';
                case Jamo.Mieum: return 'ㅁ';
                case Jamo.Siot: return 'ㅅ';
                case Jamo.Ieung: return 'ㅇ';
                default: return '\0';
            }
        }

        // 스타일 수치를 획 머티리얼 인스턴스에 반영 — 머티리얼 에셋 값과 SO가 어긋나지 않게
        // 룩 수치의 단일 출처를 SO로 고정한다(데이터 주도).
        private void ApplyLookValues(Material material)
        {
            if (_style == null) return;
            material.SetFloat(NoiseScaleId, _style.NoiseScale);
            material.SetFloat(CrackStrengthId, _style.CrackStrength);
            material.SetFloat(EdgeMarginId, _style.BleedMargin);
            material.SetFloat(EdgeSoftnessId, _style.EdgeSoftness);
            material.SetFloat(BleedTauId, _style.BleedTau);
            material.SetFloat(BleedStartWidthId, _style.BleedStartWidth);
            material.SetFloat(EdgeJitterId, _style.EdgeJitter);
            material.SetFloat(FlashTintId, _style.FlashTint);
            material.SetFloat(FlashAddId, _style.FlashAdd);
            material.SetFloat(XrayOpacityId, _style.XrayOpacity);
        }

        private Material GetFallbackMaterial()
        {
            if (_fallbackMaterial == null)
            {
                // 스파이크 폴백 — 스텐실·번짐·증발·플래시 없음. 정식 InkStroke 머티리얼 배선 전제.
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
                _fallbackMaterial = new Material(shader);
            }
            return _fallbackMaterial;
        }
    }
}
