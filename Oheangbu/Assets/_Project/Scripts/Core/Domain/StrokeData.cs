using System.Collections.Generic;

namespace Oheangbu.Core.Domain
{
    // 획 하나의 원본 기록 — 점 목록과 시작·종료 시각.
    // MonoBehaviour가 아닌 순수 데이터로 둔다: 인식·렌더링·필세 계산이
    // 엔진 수명주기와 무관하게 같은 기록을 읽게 하기 위해서다(인식 불가침의 데이터 층).
    public sealed class StrokeData
    {
        private readonly List<StrokePoint> _points = new List<StrokePoint>();

        public IReadOnlyList<StrokePoint> Points => _points;

        public float StartTime { get; private set; }
        public float EndTime { get; private set; }

        // 시작 시각은 첫 점에서, 종료 시각은 마지막 점에서 따라간다 —
        // 별도 기록과 점 목록이 어긋나는 상태를 만들지 않기 위해 점 추가로만 갱신한다.
        public void Add(StrokePoint point)
        {
            if (_points.Count == 0)
            {
                StartTime = point.Time;
            }
            _points.Add(point);
            EndTime = point.Time;
        }

        public void Clear()
        {
            _points.Clear();
            StartTime = default;
            EndTime = default;
        }
    }
}
