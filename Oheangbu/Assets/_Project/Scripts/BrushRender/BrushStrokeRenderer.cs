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
        // 미감 수치는 전부 BrushStyleSO(데이터)에 있다 — 코드에 상수를 두지 않는다.
        [SerializeField] private BrushStyleSO _style;
        [SerializeField, Min(0f)] private float _widthMultiplier = 1f; // 전역 배율(번짐 연출 등의 훅)

        private readonly BrushStrokeData _data = new BrushStrokeData();
        private readonly RibbonMeshBuilder _builder = new RibbonMeshBuilder();
        private Mesh _mesh;
        private MeshRenderer _meshRenderer;
        private bool _dirty;
        private Material _ownedMaterial; // 획별 인스턴스(스텐실 Ref·시드·플래시) — 파괴 시 동반 파괴

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

        // 머티리얼 주입 — 렌더러가 셰이더를 스스로 고르지 않는다(의존성 분리, §6).
        // ownsMaterial=true면 획별 인스턴스(스텐실 Ref는 MPB로 세팅 불가 — INK-LOOKDEV §11.1)로
        // 취급해 파괴 시 함께 파괴한다. sortingOrder=같은 평면 투명 획들의 그리기 순서 고정.
        public void Configure(Material material, BrushStyleSO style, bool ownsMaterial = false, int sortingOrder = 0)
        {
            var renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = sortingOrder;
            _ownedMaterial = ownsMaterial ? material : null;
            _style = style;
            // widthMultiplier는 번짐 여유 폭 용도로 점유한다(INK-LOOKDEV §5.2) —
            // 셰이더의 V 재매핑과 짝이므로 다른 연출에 재사용하지 말 것.
            if (style != null) _widthMultiplier = 1f + style.BleedMargin;
        }

        // 어댑터가 플래시·디졸브를 구동할 인스턴스 — 소유 인스턴스가 아니면 null(폴백 경로)
        public Material OwnedMaterial => _ownedMaterial;

        public void AddPoint(Vector3 localPosition, float width, float ink, float time)
        {
            _data.Add(new BrushStrokePoint(localPosition, width, ink, time));
            _dirty = true;
        }

        // 획 마무리 — 마지막 진행 방향으로 taper 점을 덧붙여 붓을 떼는 흔적을 남긴다(수필 收筆)
        public void EndStroke()
        {
            var points = _data.Points;
            if (_style == null || points.Count < 2 || _style.TaperSteps <= 0) return;

            var last = points[points.Count - 1];
            // 수필 방향 = 마지막 N샘플의 현(chord) — 떼는 순간의 미세한 손 틀림이 꼬리를 꺾지 않게.
            // 회봉은 「그어 온 방향」으로 거두는 것이지 떼는 순간의 방향을 따르는 게 아니다(§14).
            int window = Mathf.Min(_style.TaperDirectionWindow, points.Count - 1);
            Vector3 dir = last.Position - points[points.Count - 1 - window].Position;
            if (dir.sqrMagnitude <= 0f)
            {
                dir = last.Position - points[points.Count - 2].Position; // 현이 0이면 구 방식 폴백
                if (dir.sqrMagnitude <= 0f) return; // 같은 자리에서 뗀 붓은 taper를 만들 수 없다
            }
            dir.Normalize();

            int steps = _style.TaperSteps;
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                // 선형(스파이크) 대신 SmoothStep — 붓이 「빠지는」 곡선. 끝은 0이 아니라
                // 잔여 폭(TipWidth)까지만 줄고, 그 위를 회봉 캡이 둥글게 감싼다(§14)
                float eased = Mathf.SmoothStep(0f, 1f, t);
                AddPoint(
                    last.Position + dir * (_style.TaperLength * eased),
                    last.Width * Mathf.Lerp(1f, _style.TaperTipWidth, eased),
                    last.Ink * (1f - _style.TaperInkLoss * eased),
                    last.Time); // 수필 점은 마지막 점의 탄생 시각 상속 — 번짐 나이 연속
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
            var inkColor = _style != null ? _style.InkColor : Color.black;
            _builder.Build(_data, inkColor, _widthMultiplier,
                _style != null ? _style.PositionSmoothing : 0,
                _style != null ? _style.WidthSmoothing : 0,
                _style != null ? _style.CapSegments : 0,
                _mesh);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_ownedMaterial != null) Destroy(_ownedMaterial); // 획별 인스턴스 누수 방지(§11.1)
        }
    }
}
