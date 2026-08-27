using System.Collections.Generic;
using Oheangbu.BrushRender;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Drawing;
using UnityEngine;

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
        [SerializeField] private ElementPaletteSO _elementPalette;        // 술식 플래시 속성색(초성→오행, TEST)
        [SerializeField] private DrawnLetterEventChannelSO _letterDrawn;  // 커밋 글자 — 속성·위력 근사 원자료(읽기만)
        [SerializeField, Min(0.01f)] private float _surfaceDistance = 1f; // 카메라 앞 작도면 거리 [TEST]

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

        // 표현용 상태 — 획 하나가 그려지는 동안의 붓 상태다. 인식 데이터와 공유하지 않는다.
        private Vector2 _lastInputScreen;  // 직전 입력 좌표 — 속도 측정용
        private Vector3 _lastLocal;
        private bool _hasLastLocal;
        private int _sampleInStroke;
        private float _noiseSeed;
        private float _lastSampleTime;
        private float _smoothedSpeed;   // 표현용 속도의 저역 통과 값
        private bool _hasSpeed;         // 첫 점은 이동거리 0이라 속도를 잴 수 없다 — 중립 폭으로 시작

        // 먹 잔량(0~1) — 지금은 밖에서 넣어준다(하네스 슬라이더).
        // 먹 풀이 생기면 이 값만 꽂으면 된다: 읽기 전용이며 여기서 먹을 깎지 않는다(SPEC §5.2).
        public float InkNormalized { get; set; } = 1f;

        private void OnEnable()
        {
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
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, _surfaceDistance));
            Vector3 local = transform.InverseTransformPoint(world);

            float traveled = _hasLastLocal ? Vector3.Distance(local, _lastLocal) : 0f;
            _lastLocal = local;
            _hasLastLocal = true;
            _sampleInStroke++;

            // 탄생 시각은 scaled — 번짐(연출)의 시계와 같아야 한다(§5.2)
            _current.AddPoint(local, ComputeWidth(speed, traveled), ComputeInk(speed), Time.time);
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
            _hasPendingFlash = true;
        }

        private void OnCommitted(bool success)
        {
            if (success && _hasPendingFlash)
            {
                BeginFading(isFlash: true, _pendingFlashColor, _pendingFlashPower);
            }
            else
            {
                BeginFading(isFlash: false, default, 0f); // 불발 — 먹 증발
            }
            _hasPendingFlash = false;
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
                if (stroke != null) group.Strokes.Add(stroke);
            }
            _strokes.Clear();
            _current = null;
            if (isFlash) SpawnMotifs(group);
            _fading.Add(group);
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
                go.transform.SetParent(transform, true);
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
