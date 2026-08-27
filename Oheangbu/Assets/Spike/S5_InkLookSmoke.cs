using System.Collections.Generic;
using Oheangbu.BrushRender;
using UnityEngine;
using UnityEngine.InputSystem;

// [TEST ONLY] 먹 룩 스모크 하네스 — 입력 없이 획을 자동 생성해 InkStroke 셰이더를 눈으로 검증한다.
// SPEC-SPIKE-INK-LOOKDEV §13(육안 검수)의 보조 도구: 같은 획을 반복해 그릴 필요 없이
// 농도 단계·번짐·증발·플래시를 키 하나로 재현한다. 스파이크 종료 시 Spike 폴더와 함께 제거.
//   R = 데모 획 다시 생성(번짐이 처음부터 다시 진행)
//   F = 술식 플래시(화 주홍, 위력 1.0)   G = 약한 플래시(위력 0.3)
//   D = 먹 증발(디졸브)
public sealed class S5_InkLookSmoke : MonoBehaviour
{
    [SerializeField] private BrushStyleSO _style;
    [SerializeField] private Material _strokeMaterial; // M_InkStroke
    [SerializeField] private Material _motifMaterial;  // M_InkMotif — 속성 모티프 검증(F 누를 때마다 속성 순환)
    [SerializeField] private ElementPaletteSO _palette; // 5속성 플래시 비교판(ART-INK-LOOK §6-1)
    [SerializeField, Min(0.1f)] private float _surfaceDistance = 1.2f;

    // 획 인덱스 → 속성 초성(가로획 4단 + 곡선획 = 목화토금수) — 플래시 비교판용
    private static readonly char[] StrokeInitials = { 'ㄱ', 'ㄴ', 'ㅁ', 'ㅅ', 'ㅇ' };

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
    private static readonly int InkNowId = Shader.PropertyToID("_InkNow");

    private static readonly int MotifTintId = Shader.PropertyToID("_Tint");
    private static readonly int MotifStrengthId = Shader.PropertyToID("_Strength");
    private static readonly int MotifFadeId = Shader.PropertyToID("_FadeMul");

    private readonly List<BrushStrokeRenderer> _strokes = new List<BrushStrokeRenderer>();
    private int _stencilRef;
    private float _effectStart = -1f;
    private bool _effectIsFlash;
    private float _effectPower;
    private float _effectPeak01;      // 어택 후 유지 — 먹으로 돌아오지 않는다(예준 지시)
    private int _motifElementIndex;   // F 누를 때마다 목→화→토→금→수 순환
    private readonly List<Transform> _motifs = new List<Transform>();
    private readonly List<Material> _motifMaterials = new List<Material>();
    private readonly List<Vector3> _motifBaseScales = new List<Vector3>();
    private float _letterSize;

    private void Start()
    {
        SpawnDemo();
    }

    private void Update()
    {
        Shader.SetGlobalFloat(InkNowId, Time.time); // 어댑터와 같은 전역 시계(둘 다 있어도 같은 값)

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.rKey.wasPressedThisFrame) SpawnDemo();
            if (kb.fKey.wasPressedThisFrame) BeginFlash(1f);
            if (kb.gKey.wasPressedThisFrame) BeginFlash(0.3f);
            if (kb.dKey.wasPressedThisFrame) BeginDissolve();
        }

        TickEffect();
    }

    private void SpawnDemo()
    {
        Clear();
        if (_style == null || _strokeMaterial == null) return;
        _effectStart = -1f;

        // 화면 왼쪽 절반에 가로획 4단(농도 1.0 → 0.25)과 곡선획 하나 — 갈필 단계 비교판
        float[] densities = { 1f, 0.75f, 0.5f, 0.25f };
        for (int row = 0; row < densities.Length; row++)
        {
            var stroke = CreateStroke();
            float y = 0.30f - row * 0.16f;
            int samples = 36;
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                // 살짝 눌린 호(붓이 지나간 궤적 흉내) + 폭은 중간이 굵은 붓맛
                var pos = new Vector3(-0.55f + t * 0.5f, y + Mathf.Sin(t * Mathf.PI) * 0.012f, 0f);
                float width = _style.BaseWidth * Mathf.Lerp(0.8f, 1.15f, Mathf.Sin(t * Mathf.PI));
                stroke.AddPoint(pos, width, densities[row], Time.time + t * 0.35f); // 진행을 따라 탄생 시각 지연 — 붓 뒤 번짐 재현
            }
            stroke.EndStroke();
        }

        // 세로 S곡선 — 꺾임·캡·자기 겹침 확인
        var curve = CreateStroke();
        int n = 48;
        for (int i = 0; i <= n; i++)
        {
            float t = (float)i / n;
            var pos = new Vector3(0.10f + Mathf.Sin(t * Mathf.PI * 2f) * 0.06f, 0.32f - t * 0.62f, 0f);
            curve.AddPoint(pos, _style.BaseWidth * Mathf.Lerp(1.1f, 0.7f, t), 0.9f, Time.time + t * 0.6f);
        }
        curve.EndStroke();
    }

    private BrushStrokeRenderer CreateStroke()
    {
        var go = new GameObject($"SmokeStroke_{_strokes.Count}");
        var cam = Camera.main;
        go.transform.SetParent(transform, false);
        if (cam != null)
        {
            go.transform.position = cam.transform.position + cam.transform.forward * _surfaceDistance;
            go.transform.rotation = cam.transform.rotation;
        }

        var stroke = go.AddComponent<BrushStrokeRenderer>();
        var material = new Material(_strokeMaterial);
        _stencilRef = _stencilRef % 255 + 1;
        material.SetFloat(StencilRefId, _stencilRef);
        material.SetFloat(NoiseSeedId, Random.value * 100f);
        material.SetFloat(NoiseScaleId, _style.NoiseScale);
        material.SetFloat(CrackStrengthId, _style.CrackStrength);
        material.SetFloat(EdgeMarginId, _style.BleedMargin);
        material.SetFloat(EdgeSoftnessId, _style.EdgeSoftness);
        material.SetFloat(BleedTauId, _style.BleedTau);
        material.SetFloat(BleedStartWidthId, _style.BleedStartWidth);
        material.SetFloat(EdgeJitterId, _style.EdgeJitter);
        material.SetFloat(FlashTintId, _style.FlashTint);
        material.SetFloat(FlashAddId, _style.FlashAdd);
        stroke.Configure(material, _style, ownsMaterial: true, sortingOrder: _strokes.Count);
        _strokes.Add(stroke);
        return stroke;
    }

    private void BeginFlash(float power)
    {
        _effectStart = Time.time;
        _effectIsFlash = true;
        _effectPower = power;
        _effectPeak01 = 0f;
        SpawnMotif();
    }

    // 속성 모티프 검증(§4.6) — 획 위 몇 곳에 작은 스프라이트가 붙는다. F마다 목→화→토→금→수 순환
    private void SpawnMotif()
    {
        DestroyMotif();
        if (_motifMaterial == null || _palette == null || _style == null) return;
        char initial = StrokeInitials[_motifElementIndex % StrokeInitials.Length];
        _motifElementIndex++;
        Texture2D motif = _palette.GetMotif(initial);
        if (motif == null) return;

        var points = new List<Vector3>();
        var bounds = new Bounds();
        bool hasBounds = false;
        foreach (var stroke in _strokes)
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
        _letterSize = Mathf.Max(bounds.size.x, bounds.size.y, 0.01f);

        Color flashColor = _palette.GetFlashColor(initial);
        var cam = Camera.main;
        Quaternion plane = cam != null ? cam.transform.rotation : Quaternion.identity;
        Vector3 right = plane * Vector3.right;
        Vector3 up = plane * Vector3.up;

        for (int k = 0; k < _style.MotifCount; k++)
        {
            Vector3 position = points[Random.Range(0, points.Count)];
            Vector2 scatter = Random.insideUnitCircle * (_style.MotifScatter * _letterSize);
            position += right * scatter.x + up * scatter.y;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "SmokeMotif";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, true);
            go.transform.rotation = plane * Quaternion.Euler(0f, 0f,
                Random.Range(-_style.MotifAngleJitter, _style.MotifAngleJitter));
            go.transform.position = position;
            float size = _letterSize * _style.MotifSize * Random.Range(0.75f, 1.3f);
            go.transform.localScale = Vector3.zero;

            var material = new Material(_motifMaterial);
            material.mainTexture = motif;
            material.SetColor(MotifTintId, flashColor);
            var quadRenderer = go.GetComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = material;
            quadRenderer.sortingOrder = 100;

            _motifs.Add(go.transform);
            _motifMaterials.Add(material);
            _motifBaseScales.Add(new Vector3(size, size, 1f));
        }
    }

    private void DestroyMotif()
    {
        foreach (var motif in _motifs) { if (motif != null) Destroy(motif.gameObject); }
        foreach (var material in _motifMaterials) { if (material != null) Destroy(material); }
        _motifs.Clear();
        _motifMaterials.Clear();
        _motifBaseScales.Clear();
    }

    private void BeginDissolve()
    {
        _effectStart = Time.time;
        _effectIsFlash = false;
    }

    private void TickEffect()
    {
        if (_effectStart < 0f || _style == null) return;
        float elapsed = Time.time - _effectStart;

        foreach (var stroke in _strokes)
        {
            var material = stroke != null ? stroke.OwnedMaterial : null;
            if (material == null) continue;

            if (_effectIsFlash)
            {
                // 어택 후 피크 유지 — 먹으로 돌아오지 않고 속성색인 채로 사라진다(예준 지시)
                float flashT = Mathf.Clamp01(elapsed / _style.FlashDuration);
                _effectPeak01 = Mathf.Max(_effectPeak01, _style.EvaluateFlash01(flashT));
                float strength = _effectPeak01 * _effectPower;
                float fade = elapsed <= _style.FlashDuration
                    ? 1f
                    : 1f - Mathf.Clamp01((elapsed - _style.FlashDuration) / _style.FadeOutDuration);
                // 5속성 비교판 — 획마다 다른 속성색으로 플래시(§5 기준 1: 서로·배경과 색으로 구분되는가)
                int index = _strokes.IndexOf(stroke);
                char initial = StrokeInitials[Mathf.Clamp(index, 0, StrokeInitials.Length - 1)];
                Color flashColor = _palette != null ? _palette.GetFlashColor(initial) : Color.white;
                material.SetColor(FlashColorId, flashColor);
                material.SetFloat(FlashStrengthId, strength);
                material.SetFloat(FadeMulId, fade);

                if (_motifs.Count > 0)
                {
                    // 붙은 자리 고정 — 크기만 0 → 제 크기로 은은히(상승 폐기 — 예준 4차 검수)
                    float grow = Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01(elapsed / Mathf.Max(_style.FlashDuration * 0.4f, 0.01f)));
                    for (int mi = 0; mi < _motifs.Count; mi++)
                    {
                        var motif = _motifs[mi];
                        if (motif == null) continue;
                        motif.localScale = _motifBaseScales[mi] * grow;
                        _motifMaterials[mi].SetFloat(MotifStrengthId, strength);
                        _motifMaterials[mi].SetFloat(MotifFadeId, fade);
                    }
                }
            }
            else
            {
                material.SetFloat(DissolveId, Mathf.Clamp01(elapsed / _style.DissolveDuration));
            }
        }
    }

    private void Clear()
    {
        foreach (var stroke in _strokes)
        {
            if (stroke != null) Destroy(stroke.gameObject);
        }
        _strokes.Clear();
        _stencilRef = 0;
        DestroyMotif();
    }
}
