using Oheangbu.Core.Domain;

namespace Oheangbu.Drawing
{
    // 인식된 자모 이름 → Jamo enum · 완성 글자 조합.
    // 유니코드 한글 조합 공식(가 = AC00 + (초성×21 + 중성)×28 + 종성)을 쓴다 —
    // 표가 아니라 문자 체계 자체의 산술이라 데이터화 대상이 아니다.
    public static class HangulComposer
    {
        private const int HangulBase = 0xAC00;
        private const int MedialCount = 21;
        private const int FinalCount = 28;

        public static bool TryCompose(string initialName, string medialName, string finalName,
            out char letter, out Jamo initial, out Jamo medial, out Jamo? final)
        {
            letter = default;
            initial = default;
            medial = default;
            final = null;

            int cho = InitialIndex(initialName, out initial);
            int jung = MedialIndex(medialName, out medial);
            if (cho < 0 || jung < 0) return false;

            int jong = 0;
            if (!string.IsNullOrEmpty(finalName))
            {
                jong = FinalIndex(finalName, out var finalJamo);
                if (jong < 0) return false;
                final = finalJamo;
            }

            letter = (char)(HangulBase + (cho * MedialCount + jung) * FinalCount + jong);
            return true;
        }

        // 유니코드 초성 인덱스 — 오행부 어휘는 아음·설음·순음·치음·후음 5종만(SPELL-VOCAB 고정)
        private static int InitialIndex(string name, out Jamo jamo)
        {
            switch (name)
            {
                case "ㄱ": jamo = Jamo.Giyeok; return 0;
                case "ㄴ": jamo = Jamo.Nieun; return 2;
                case "ㅁ": jamo = Jamo.Mieum; return 6;
                case "ㅅ": jamo = Jamo.Siot; return 9;
                case "ㅇ": jamo = Jamo.Ieung; return 11;
                default: jamo = default; return -1;
            }
        }

        private static int MedialIndex(string name, out Jamo jamo)
        {
            switch (name)
            {
                case "ㅏ": jamo = Jamo.A; return 0;
                case "ㅓ": jamo = Jamo.Eo; return 4;
                case "ㅗ": jamo = Jamo.O; return 8;
                case "ㅜ": jamo = Jamo.U; return 13;
                default: jamo = default; return -1;
            }
        }

        private static int FinalIndex(string name, out Jamo jamo)
        {
            switch (name)
            {
                case "ㄱ": jamo = Jamo.Giyeok; return 1;
                case "ㄴ": jamo = Jamo.Nieun; return 4;
                case "ㅁ": jamo = Jamo.Mieum; return 16;
                case "ㅅ": jamo = Jamo.Siot; return 19;
                case "ㅇ": jamo = Jamo.Ieung; return 21;
                default: jamo = default; return -1;
            }
        }
    }
}
