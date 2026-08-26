using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.BrushRender
{
    // 붓 획 표현용 점 하나 — 위치(렌더러 로컬)·폭·먹 농도.
    // Core의 StrokePoint(인식용 원본: 화면 좌표+시각)와 일부러 별개 형으로 둔다:
    // 표현 계층이 자기 데이터 계약만 다루면, 렌더러를 통째로 갈아끼워도
    // 인식 쪽 데이터에 닿을 길 자체가 없다(인식 불가침 — SPEC-MIG-BRUSH-STROKE §9).
    public readonly struct BrushStrokePoint
    {
        public readonly Vector3 Position; // 렌더러 로컬 좌표(작도면=로컬 XY 평면 전제)
        public readonly float Width;      // 이 지점의 획 폭
        public readonly float Ink;        // 0~1 먹 농도 — 갈필·번짐 셰이더의 입력 예약(현재는 정점 알파로만 표현)

        public BrushStrokePoint(Vector3 position, float width, float ink)
        {
            Position = position;
            Width = width;
            Ink = ink;
        }
    }

    // 획 하나의 표현용 점 목록 + 누적 길이.
    // 누적 길이를 추가 시점에 미리 쌓아 두는 이유: 리본 UV의 u(진행률) 계산이
    // 리빌드마다 전체 길이를 다시 합산하지 않게 하기 위해서다.
    public sealed class BrushStrokeData
    {
        private readonly List<BrushStrokePoint> _points = new List<BrushStrokePoint>();

        public IReadOnlyList<BrushStrokePoint> Points => _points;
        public float TotalLength { get; private set; }

        public void Add(BrushStrokePoint point)
        {
            if (_points.Count > 0)
            {
                TotalLength += Vector3.Distance(_points[_points.Count - 1].Position, point.Position);
            }
            _points.Add(point);
        }

        public void Clear()
        {
            _points.Clear();
            TotalLength = 0f;
        }
    }
}
