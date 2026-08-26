using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.BrushRender
{
    // 붓 획 하나를 리본 메시로 그리는 렌더러 — 점을 「받기만」 하는 순수 표현 계층.
    // 인식 불가침: 입력·인식 어디에도 참조가 없다(컴파일 타임 강제 — asmdef 참조 목록이 곧 증명).
    // 머티리얼은 밖에서 주입(Configure)한다 — 셰이더 확정(Noise Cutout·갈필)은 스파이크 몫(§8 Non-Goals).
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class BrushStrokeRenderer : MonoBehaviour
    {
        // 먹색 기본값 = ART-COLOR 기본 팔레트의 먹(#2A2622). 실수치 조정은 데이터(인스펙터)에서.
        [SerializeField] private Color _inkColor = new Color(0x2A / 255f, 0x26 / 255f, 0x22 / 255f, 1f);
        [SerializeField, Min(0f)] private float _widthMultiplier = 1f;

        [Header("수필(收筆) — 획 끝을 뾰족하게 빼는 미감 수치 [TEST — 스파이크에서 확정]")]
        [SerializeField, Min(0)] private int _taperSteps = 3;
        [SerializeField, Min(0f)] private float _taperLength = 0.05f;
        [SerializeField, Range(0f, 1f)] private float _taperWidthKeep = 0.7f;
        [SerializeField, Range(0f, 1f)] private float _taperInkLoss = 0.4f;

        private readonly BrushStrokeData _data = new BrushStrokeData();
        private readonly RibbonMeshBuilder _builder = new RibbonMeshBuilder();
        private Mesh _mesh;
        private MeshRenderer _meshRenderer;
        private bool _dirty;

        public BrushStrokeData Data => _data;

        // 검증 보조 — 스파이크 조건 "Mesh 정상 종료·taper 확인"을 눈으로 셀 수 있게 한다
        public int VertexCount => _mesh != null ? _mesh.vertexCount : 0;

        private void Awake()
        {
            _mesh = new Mesh { name = "BrushStrokeMesh" };
            _mesh.MarkDynamic(); // 획이 자라는 동안 매 프레임 갱신되는 동적 메시
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _meshRenderer = GetComponent<MeshRenderer>();
            // 먹 획은 조명·그림자의 대상이 아니다 — 종이 위의 먹이지 공간의 물체가 아니기 때문
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
        }

        // 머티리얼 주입 — 렌더러가 셰이더를 스스로 고르지 않는다(의존성 분리, §6)
        public void Configure(Material material)
        {
            GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        public void AddPoint(Vector3 localPosition, float width, float ink)
        {
            _data.Add(new BrushStrokePoint(localPosition, width, ink));
            _dirty = true;
        }

        // 획 마무리 — 마지막 진행 방향으로 taper 점을 덧붙여 붓을 떼는 흔적을 남긴다(레거시 EndTaper 승계)
        public void EndStroke()
        {
            var points = _data.Points;
            if (points.Count < 2 || _taperSteps <= 0) return;

            var last = points[points.Count - 1];
            Vector3 dir = last.Position - points[points.Count - 2].Position;
            if (dir.sqrMagnitude <= 0f) return; // 같은 자리에서 뗀 붓은 taper를 만들 수 없다
            dir.Normalize();

            for (int i = 1; i <= _taperSteps; i++)
            {
                float t = (float)i / _taperSteps;
                AddPoint(
                    last.Position + dir * (_taperLength * t),
                    last.Width * (1f - t) * _taperWidthKeep,
                    last.Ink * (1f - _taperInkLoss * t));
            }
        }

        public void ClearStroke()
        {
            _data.Clear();
            _dirty = true;
        }

        // 회귀 검증용 — 렌더러를 꺼도 데이터·인식 경로에는 아무 일도 일어나지 않는다
        public void SetVisible(bool visible)
        {
            _meshRenderer.enabled = visible;
        }

        private void LateUpdate()
        {
            // 변경이 있던 프레임만 리빌드 — 입력 수집(Update)과 표현 갱신의 순서를 고정한다
            if (!_dirty) return;
            _dirty = false;
            _builder.Build(_data, _inkColor, _widthMultiplier, _mesh);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
