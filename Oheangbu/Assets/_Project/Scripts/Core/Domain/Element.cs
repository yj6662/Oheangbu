namespace Oheangbu.Core.Domain
{
    // 오행(五行). 순서가 곧 상생 고리다: 목→화→토→금→수→목 (아래 관계식이 이 순서에 기댄다)
    public enum Element
    {
        Wood,  // 목
        Fire,  // 화
        Earth, // 토
        Metal, // 금
        Water  // 수
    }

    // 상생·상극 관계식 — COMBAT-PARRY 정본표의 근원.
    // 표를 하드코딩하지 않고 고리 연산으로 도출한다: 생(生)=다음 칸, 극(克)=두 칸 건너.
    //   검산(정본표): 화 공격의 정답 '어'(수) → Overcomes(수, 화)=수극화 ✓
    //                 완전 실패 '거'(목) → Generates(목, 화)=목생화 ✓
    public static class ElementRelations
    {
        private const int Count = 5;

        // a가 b를 낳는가(상생) — 패링에서는 「공격을 生하는 글자 = 완전 실패」
        public static bool Generates(Element a, Element b)
        {
            return ((int)a + 1) % Count == (int)b;
        }

        // a가 b를 이기는가(상극) — 패링 정답·상극 검격·보스 기믹 차단의 공용 축
        public static bool Overcomes(Element a, Element b)
        {
            return ((int)a + 2) % Count == (int)b;
        }

        // 초성 → 오행. 정본은 작도어휘 CSV 3열(2026-08-27 실측: ㄱ목 ㄴ화 ㅁ토 ㅅ금 ㅇ수) —
        // 훈민정음 오행 배속의 언어적 사실이므로 코드 상수로 둔다(밸런스 수치 아님).
        public static Element FromInitial(Jamo initial)
        {
            switch (initial)
            {
                case Jamo.Giyeok: return Element.Wood;
                case Jamo.Nieun: return Element.Fire;
                case Jamo.Mieum: return Element.Earth;
                case Jamo.Siot: return Element.Metal;
                case Jamo.Ieung: return Element.Water;
                default: return Element.Wood; // 모음이 들어오면 호출부 버그 — 목으로 눕히고 지나가지 않는다
            }
        }
    }
}
