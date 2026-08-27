using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.BrushRender
{
    // 획 점 목록 → 리본 메시. 점마다 진행 방향의 수직으로 폭의 절반씩 벌린 2정점 띠를 잇는다.
    // 레거시 InkStroke에서 메시 수학을 추출·재작성(SPEC-MIG-BRUSH-STROKE §5)한 뒤,
    // 서예 기준을 기하로 번역해 확장했다(SPEC-SPIKE-BRUSH-RENDERER §14):
    //   · 장봉(藏鋒)·회봉(回鋒) = 획 양끝의 반원 캡 — 붓끝을 감춘 둥근 기필·수필
    //   · 필력 보정 = 위치 평활(Chaikin) + 폭 저역 통과 — 잔떨림은 필력 부족의 표징이다
    // 보정은 전부 표현 전용이다 — 인식은 Raw를 따로 쓰므로 여기서 무엇을 다듬어도 판정은 불변.
    // 버퍼(리스트)를 재사용해 리빌드마다 GC 할당이 생기지 않게 한다(§13 Validation).
    // 전제: 획은 로컬 XY 평면 위에 있다(작도면=카메라 평행 평면) — 임의 평면 일반화는 스파이크 확정 사항.
    public sealed class RibbonMeshBuilder
    {
        private const float LengthEpsilon = 0.0001f; // 길이 0 획의 0 나눗셈 방지

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color32> _colors = new List<Color32>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Vector2> _uvs1 = new List<Vector2>(); // UV1.x=점 탄생 시각(번짐 나이 — INK-LOOKDEV §2)
        private readonly List<int> _triangles = new List<int>();

        // 보정용 스크래치 — 원본(BrushStrokeData)은 건드리지 않고 여기 복사해 다듬는다
        private readonly List<Vector3> _posA = new List<Vector3>();
        private readonly List<Vector3> _posB = new List<Vector3>();
        private readonly List<float> _widthA = new List<float>();
        private readonly List<float> _widthB = new List<float>();
        private readonly List<float> _inkA = new List<float>();
        private readonly List<float> _inkB = new List<float>();
        private readonly List<float> _timeA = new List<float>();
        private readonly List<float> _timeB = new List<float>();

        public void Build(BrushStrokeData stroke, Color inkColor, float widthMultiplier,
            int positionSmoothing, int widthSmoothing, int capSegments, Mesh target)
        {
            target.Clear();
            var points = stroke.Points;
            if (points.Count < 2) return; // 점 하나로는 띠를 만들 수 없다

            CopyToScratch(points);
            // 위치 평활(필력 보정) — 손떨림을 걷어내 「자신 있는 획」으로. 꺾임(折)은 패스가 적을수록 산다
            for (int pass = 0; pass < positionSmoothing; pass++) ChaikinPass();
            // 폭 저역 통과 — 굵기 변화가 「의도된 흐름」으로 보이게 샘플 간 튐을 없앤다
            for (int pass = 0; pass < widthSmoothing; pass++) SmoothWidthPass();

            _vertices.Clear();
            _colors.Clear();
            _uvs.Clear();
            _uvs1.Clear();
            _triangles.Clear();

            int count = _posA.Count;
            float totalLength = 0f;
            for (int i = 1; i < count; i++) totalLength += Vector3.Distance(_posA[i], _posA[i - 1]);

            float traveled = 0f;
            // 겹친 점(진행 방향 0)이 나오면 직전 수직을 재사용한다 — 획이 한 점에서 터지는 것을 막는다
            Vector3 normal = Vector3.up;

            for (int i = 0; i < count; i++)
            {
                Vector3 p = _posA[i];
                // 진행 방향: 양끝은 한쪽 이웃, 중간은 양쪽 이웃 차 — 꺾임에서 폭이 급변하지 않게 평균 방향을 쓴다
                Vector3 dir = i == 0 ? _posA[1] - p
                    : i == count - 1 ? p - _posA[i - 1]
                    : _posA[i + 1] - _posA[i - 1];
                if (i > 0) traveled += Vector3.Distance(p, _posA[i - 1]);

                var flat = new Vector2(dir.x, dir.y);
                if (flat.sqrMagnitude > 0f)
                {
                    flat.Normalize();
                    normal = new Vector3(-flat.y, flat.x, 0f);
                }

                float half = _widthA[i] * widthMultiplier * 0.5f;
                _vertices.Add(p + normal * half);
                _vertices.Add(p - normal * half);

                var color = MakeColor(inkColor, _inkA[i]);
                _colors.Add(color);
                _colors.Add(color);

                // ---- UV 규약 [계약 확정 — SPEC-SPIKE-BRUSH-RENDERER §4 · INK-LOOKDEV §2 확장] ----
                // UV0.U = 획 진행률(기필 0 → 수필 1) : 붓결 텍스처가 획을 따라 흐르고, 양끝 처리가 얹힌다
                // UV0.V = 폭 방향(0 ↔ 1, 중앙 0.5) : 갈필이 「가장자리부터」 갈라지게 하는 좌표
                // 정점 알파 = 먹 농도 : Noise Cutout 셰이더가 읽을 채널
                // UV1.x = 점 탄생 시각 : 번짐이 붓 뒤를 따라오게 하는 나이 기준(INK-LOOKDEV §5.2)
                // InkStroke 셰이더가 이 규약 위에 서 있다 — 여기를 바꾸면 셰이더가 깨진다.
                float u = totalLength > LengthEpsilon ? traveled / totalLength : 0f;
                _uvs.Add(new Vector2(u, 1f));
                _uvs.Add(new Vector2(u, 0f));
                var birth = new Vector2(_timeA[i], 0f);
                _uvs1.Add(birth);
                _uvs1.Add(birth);
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

            // 장봉·회봉 — 양끝을 반원으로 감싼다. 붓끝(鋒)을 획 안에 감추는 서예 문법의 기하 번역.
            // 수필 쪽은 taper가 남긴 잔여 폭 위에 얹혀 「거둔 붓」의 둥근 맺음이 된다(잔여 폭 0이면 노봉).
            if (capSegments > 0)
            {
                AddCap(_posA[0], _posA[1] - _posA[0], _widthA[0] * widthMultiplier * 0.5f,
                    MakeColor(inkColor, _inkA[0]), 0f, _timeA[0], capSegments, true);
                AddCap(_posA[count - 1], _posA[count - 1] - _posA[count - 2],
                    _widthA[count - 1] * widthMultiplier * 0.5f,
                    MakeColor(inkColor, _inkA[count - 1]), 1f, _timeA[count - 1], capSegments, false);
            }

            target.SetVertices(_vertices);
            target.SetTriangles(_triangles, 0);
            target.SetColors(_colors);
            target.SetUVs(0, _uvs);
            target.SetUVs(1, _uvs1);
            target.RecalculateBounds();
        }

        private void CopyToScratch(IReadOnlyList<BrushStrokePoint> points)
        {
            _posA.Clear();
            _widthA.Clear();
            _inkA.Clear();
            _timeA.Clear();
            for (int i = 0; i < points.Count; i++)
            {
                _posA.Add(points[i].Position);
                _widthA.Add(points[i].Width);
                _inkA.Add(points[i].Ink);
                _timeA.Add(points[i].Time);
            }
        }

        // Chaikin 모서리 깎기 1패스 — 양 끝점은 보존(기필·수필 위치가 밀리지 않게).
        // 폭·농도도 같은 가중치로 함께 보간해 위치와 어긋나지 않게 한다.
        private void ChaikinPass()
        {
            int n = _posA.Count;
            if (n < 3) return;
            _posB.Clear();
            _widthB.Clear();
            _inkB.Clear();
            _timeB.Clear();

            _posB.Add(_posA[0]);
            _widthB.Add(_widthA[0]);
            _inkB.Add(_inkA[0]);
            _timeB.Add(_timeA[0]);
            for (int i = 0; i < n - 1; i++)
            {
                _posB.Add(Vector3.Lerp(_posA[i], _posA[i + 1], 0.25f));
                _widthB.Add(Mathf.Lerp(_widthA[i], _widthA[i + 1], 0.25f));
                _inkB.Add(Mathf.Lerp(_inkA[i], _inkA[i + 1], 0.25f));
                _timeB.Add(Mathf.Lerp(_timeA[i], _timeA[i + 1], 0.25f));
                _posB.Add(Vector3.Lerp(_posA[i], _posA[i + 1], 0.75f));
                _widthB.Add(Mathf.Lerp(_widthA[i], _widthA[i + 1], 0.75f));
                _inkB.Add(Mathf.Lerp(_inkA[i], _inkA[i + 1], 0.75f));
                _timeB.Add(Mathf.Lerp(_timeA[i], _timeA[i + 1], 0.75f));
            }
            _posB.Add(_posA[n - 1]);
            _widthB.Add(_widthA[n - 1]);
            _inkB.Add(_inkA[n - 1]);
            _timeB.Add(_timeA[n - 1]);

            SwapScratch();
        }

        // 폭 저역 통과 1패스(1-2-1 커널) — 양 끝점 보존
        private void SmoothWidthPass()
        {
            int n = _widthA.Count;
            if (n < 3) return;
            _widthB.Clear();
            _widthB.Add(_widthA[0]);
            for (int i = 1; i < n - 1; i++)
            {
                _widthB.Add((_widthA[i - 1] + _widthA[i] * 2f + _widthA[i + 1]) * 0.25f);
            }
            _widthB.Add(_widthA[n - 1]);

            _widthA.Clear();
            _widthA.AddRange(_widthB);
        }

        private void SwapScratch()
        {
            _posA.Clear();
            _posA.AddRange(_posB);
            _widthA.Clear();
            _widthA.AddRange(_widthB);
            _inkA.Clear();
            _inkA.AddRange(_inkB);
            _timeA.Clear();
            _timeA.AddRange(_timeB);
        }

        // 반원 캡 — center에서 direction의 반대쪽(backward=기필) 또는 같은 쪽(수필)으로 부채꼴을 편다
        private void AddCap(Vector3 center, Vector3 direction, float half, Color32 color,
            float u, float birthTime, int segments, bool backward)
        {
            var flat = new Vector2(direction.x, direction.y);
            if (flat.sqrMagnitude <= 0f || half <= 0f) return;
            flat.Normalize();
            var d = new Vector3(flat.x, flat.y, 0f) * (backward ? -1f : 1f);
            var n = new Vector3(-flat.y, flat.x, 0f);

            var birth = new Vector2(birthTime, 0f); // 캡은 끝점의 탄생 시각을 상속(번짐 나이 연속)

            int centerIndex = _vertices.Count;
            _vertices.Add(center);
            _colors.Add(color);
            _uvs.Add(new Vector2(u, 0.5f));
            _uvs1.Add(birth);

            for (int k = 0; k <= segments; k++)
            {
                float angle = Mathf.PI * k / segments; // +n(0) → 바깥(π/2) → -n(π)
                Vector3 arc = center + (n * Mathf.Cos(angle) + d * Mathf.Sin(angle)) * half;
                _vertices.Add(arc);
                _colors.Add(color);
                _uvs.Add(new Vector2(u, (Mathf.Cos(angle) + 1f) * 0.5f)); // V=폭 방향 규약 유지
                _uvs1.Add(birth);
            }
            for (int k = 0; k < segments; k++)
            {
                _triangles.Add(centerIndex);
                _triangles.Add(centerIndex + 1 + k);
                _triangles.Add(centerIndex + 2 + k);
            }
        }

        private static Color32 MakeColor(Color inkColor, float ink)
        {
            byte alpha = (byte)(Mathf.Clamp01(ink) * inkColor.a * byte.MaxValue);
            return new Color32(
                (byte)(inkColor.r * byte.MaxValue),
                (byte)(inkColor.g * byte.MaxValue),
                (byte)(inkColor.b * byte.MaxValue),
                alpha);
        }
    }
}
