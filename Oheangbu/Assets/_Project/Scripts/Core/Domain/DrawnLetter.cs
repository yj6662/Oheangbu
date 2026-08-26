namespace Oheangbu.Core.Domain
{
    // 작도 커밋 결과 — 인식된 글자와 측정 원자료(SPEC-DRAWING-INPUT §5-②).
    // 필세 수식·위력 계산은 여기서 하지 않는다: 이 구조체는 「무엇을 얼마나 잘·빨리 그렸는가」의
    // 사실만 나르고, 해석(위력)은 Spellcraft/Combat 층의 몫이다(수식 확정은 Non-Goal).
    public readonly struct DrawnLetter
    {
        public readonly char Letter;   // 조합된 완성 글자(예: '가', '검')
        public readonly Jamo Initial;
        public readonly Jamo Medial;
        public readonly Jamo? Final;   // 무받침이면 null

        public readonly float WorstJamoDistance; // 형(形) 원자료 — 가장 서툰 자모의 $P 거리(작을수록 정확)
        public readonly float AverageDistance;   // 구조 원자료 — 채택된 분할의 자모 평균 거리
        public readonly float StrokeDuration;    // 세(勢) 구간 — 첫 획 시작→마지막 획 종료(실시간 초, §3-1)
        public readonly float HoldDuration;      // 감쇠 구간 — 작도 모드 진입→릴리즈(실시간 초, §3-2)
        public readonly int StrokeCount;

        public DrawnLetter(char letter, Jamo initial, Jamo medial, Jamo? final,
            float worstJamoDistance, float averageDistance,
            float strokeDuration, float holdDuration, int strokeCount)
        {
            Letter = letter;
            Initial = initial;
            Medial = medial;
            Final = final;
            WorstJamoDistance = worstJamoDistance;
            AverageDistance = averageDistance;
            StrokeDuration = strokeDuration;
            HoldDuration = holdDuration;
            StrokeCount = strokeCount;
        }
    }
}
