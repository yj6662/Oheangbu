using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.BrushRender
{
    // 획 점 목록 → 리본 메시. 점마다 진행 방향의 수직으로 폭의 절반씩 벌린 2정점 띠를 잇는다.
    // 레거시 InkStroke에서 메시 수학만 추출·재작성한 것(SPEC-MIG-BRUSH-STROKE §5 Keep).
    // 버퍼(리스트)를 재사용해 리빌드마다 GC 할당이 생기지 않게 한다(§13 Validation).
    // 전제: 획은 로컬 XY 평면 위에 있다(작도면=카메라 평행 평면) — 임의 평면 일반화는 스파이크 확정 사항.
    public sealed class RibbonMeshBuilder
    {
        private const float LengthEpsilon = 0.0001f; // 길이 0 획의 0 나눗셈 방지

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color32> _colors = new List<Color32>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();

        public void Build(BrushStrokeData stroke, Color inkColor, float widthMultiplier, Mesh target)
        {
            target.Clear();
            var points = stroke.Points;
            int count = points.Count;
            if (count < 2) return; // 점 하나로는 띠를 만들 수 없다

            _vertices.Clear();
            _colors.Clear();
            _uvs.Clear();
            _triangles.Clear();

            float traveled = 0f;
            // 겹친 점(진행 방향 0)이 나오면 직전 수직을 재사용한다 — 획이 한 점에서 터지는 것을 막는다
            Vector3 normal = Vector3.up;

            for (int i = 0; i < count; i++)
            {
                var p = points[i];
                // 진행 방향: 양끝은 한쪽 이웃, 중간은 양쪽 이웃 차 — 꺾임에서 폭이 급변하지 않게 평균 방향을 쓴다
                Vector3 dir = i == 0 ? points[1].Position - p.Position
                    : i == count - 1 ? p.Position - points[i - 1].Position
                    : points[i + 1].Position - points[i - 1].Position;
                if (i > 0) traveled += Vector3.Distance(p.Position, points[i - 1].Position);

                var flat = new Vector2(dir.x, dir.y);
                if (flat.sqrMagnitude > 0f)
                {
                    flat.Normalize();
                    normal = new Vector3(-flat.y, flat.x, 0f);
                }

                float half = p.Width * widthMultiplier * 0.5f;
                _vertices.Add(p.Position + normal * half);
                _vertices.Add(p.Position - normal * half);

                // 먹 농도는 정점 알파로 — 이후 Noise Cutout 셰이더의 입력 채널 계약은 스파이크에서 확정한다
                byte alpha = (byte)(Mathf.Clamp01(p.Ink) * inkColor.a * byte.MaxValue);
                var color = new Color32(
                    (byte)(inkColor.r * byte.MaxValue),
                    (byte)(inkColor.g * byte.MaxValue),
                    (byte)(inkColor.b * byte.MaxValue),
                    alpha);
                _colors.Add(color);
                _colors.Add(color);

                // U=획 진행률(기필 0 → 수필 1), V=폭 방향 0~1 — 붓결 텍스처·노이즈의 좌표 계약
                float u = stroke.TotalLength > LengthEpsilon ? traveled / stroke.TotalLength : 0f;
                _uvs.Add(new Vector2(u, 1f));
                _uvs.Add(new Vector2(u, 0f));
            }

            for (int i = 0; i < count - 1; i++)
            {
                int v = i * 2;
                _triangles.Add(v);
                _triangles.Add(v + 1);
                _triangles.Add(v + 2);
                _triangles.Add(v + 2);
                _triangles.Add(v + 1);
                _triangles.Add(v + 3);
            }

            target.SetVertices(_vertices);
            target.SetTriangles(_triangles, 0);
            target.SetColors(_colors);
            target.SetUVs(0, _uvs);
            target.RecalculateBounds();
        }
    }
}
