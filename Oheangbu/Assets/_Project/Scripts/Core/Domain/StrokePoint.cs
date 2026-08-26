using UnityEngine;

namespace Oheangbu.Core.Domain
{
    // 획 위의 표본점 하나 — 위치와 찍힌 시각.
    // 시각을 함께 기록하는 이유: 필세의 세(勢)는 시간으로 계산되고(SPELL-BRUSH),
    // 인식(Drawing)과 렌더링(BrushRender)이 같은 원본 좌표를 공유해야 하기 때문(인식 불가침).
    public readonly struct StrokePoint
    {
        public readonly Vector2 Position;
        public readonly float Time;

        public StrokePoint(Vector2 position, float time)
        {
            Position = position;
            Time = time;
        }
    }
}
