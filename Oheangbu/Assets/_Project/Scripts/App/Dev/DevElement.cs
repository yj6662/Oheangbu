using Oheangbu.Core.Domain;

namespace Oheangbu.App
{
    // [하네스 표기 보조] 속성 → 한글 이름·초성·패링 글자. 언어적 사실의 미러(밸런스 아님) — 정본은 작도어휘 CSV.
    public static class DevElement
    {
        public static string Name(Element element)
        {
            switch (element)
            {
                case Element.Fire: return "화";
                case Element.Earth: return "토";
                case Element.Metal: return "금";
                case Element.Water: return "수";
                default: return "목";
            }
        }

        public static char Initial(Element element)
        {
            switch (element)
            {
                case Element.Fire: return 'ㄴ';
                case Element.Earth: return 'ㅁ';
                case Element.Metal: return 'ㅅ';
                case Element.Water: return 'ㅇ';
                default: return 'ㄱ';
            }
        }

        // 패링 5자(초성 + ㅓ) — 거·너·머·서·어
        public static char GuardLetter(Element element)
        {
            switch (element)
            {
                case Element.Fire: return '너';
                case Element.Earth: return '머';
                case Element.Metal: return '서';
                case Element.Water: return '어';
                default: return '거';
            }
        }
    }
}
