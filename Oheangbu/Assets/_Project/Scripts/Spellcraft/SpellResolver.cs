using Oheangbu.Core.Domain;

namespace Oheangbu.Spellcraft
{
    // 해석된 술식 한 발 — Combat이 소비하는 유일한 형태.
    public readonly struct SpellCast
    {
        public readonly char Letter;
        public readonly SpellKind Kind;
        public readonly Element Element;
        public readonly float Power; // 기본 위력 × 필세(형×세) — 이미 곱해진 최종치
        public readonly AreaSpec Area;  // 광역 실판정 기하 [#137·#141] — Shape None=단일 유지
        public readonly float HoldScale; // Candidate cast snapshot; one for legacy callers.
        public readonly float SpeedMul; // 탄속 배율(정규화 완료 — 항상 양수)

        public AreaShape AreaShape => Area.Shape;

        public SpellCast(char letter, SpellKind kind, Element element, float power,
            AreaSpec area, float speedMul, float holdScale = 1f)
        {
            Letter = letter;
            Kind = kind;
            Element = element;
            Power = power;
            Area = area;
            SpeedMul = speedMul;
            HoldScale = float.IsFinite(holdScale) && holdScale > 0 ? UnityEngine.Mathf.Clamp(holdScale, .1f, 1f) : 1f;
        }
    }

    // DrawnLetter → SpellCast 해석기. 순수 로직(Mono 아님) — VContainer 등록 대상.
    // 위력은 여기서 확정한다: Combat은 「무엇을 얼마나」만 받고, 필세 수식은 Spellcraft 소유
    // (DrawnLetter 주석의 분업 — 원자료는 Drawing, 해석은 이쪽).
    public sealed class SpellResolver
    {
        private readonly SpellBookSO _book;

        public SpellResolver(SpellBookSO book)
        {
            _book = book;
        }

        // false = 어휘 미등재(프로토 미러 밖의 글자) — 효과 없음. CSV 완주는 임포터 이후.
        public bool TryResolve(DrawnLetter letter, out SpellCast cast)
            => TryResolve(letter, out cast, false);

        // Demo-only opt-in is supplied by the existing central cast owner after checking saved unlocks.
        // The shared spell-book asset and recognition/quality rules are never rewritten.
        public bool TryResolve(DrawnLetter letter, out SpellCast cast, bool allowGuk)
            => TryResolve(letter, out cast, allowGuk, false);

        public bool TryResolve(DrawnLetter letter, out SpellCast cast, bool allowGuk, bool allowEABuffs)
            => TryResolve(letter, out cast, allowGuk, allowEABuffs, false);

        public bool TryResolve(DrawnLetter letter, out SpellCast cast, bool allowGuk, bool allowEABuffs, bool allowWards, bool allowGiyeok = false, bool allowMum = false)
        {
            int wardIndex = "구누무수우".IndexOf(letter.Letter);
            if (_book != null && allowWards && wardIndex >= 0)
            {
                cast = new SpellCast(letter.Letter, SpellKind.Ward, (Element)wardIndex, 0f, default, 1f);
                return true;
            }
            int buffIndex = "걱넉먹석억".IndexOf(letter.Letter);
            if (_book != null && allowEABuffs && buffIndex >= 0)
            {
                cast = new SpellCast(letter.Letter, SpellKind.Buff, (Element)buffIndex, 0f, default, 1f);
                return true;
            }
            if (_book != null && allowGuk && letter.Letter == '국')
            {
                cast = new SpellCast('국', SpellKind.Field, Element.Wood, 0f, default, 1f);
                return true;
            }
            if (letter.Letter == '뭄')
            {
                if(_book==null||!allowMum){cast=default;return false;}
                cast = new SpellCast('뭄', SpellKind.Field, Element.Earth, 0f, default, 1f);
                return true;
            }
            char lookup = allowGiyeok && (letter.Letter == '각' || letter.Letter == '낙' || letter.Letter == '삭' || letter.Letter == '악') ? (char)(letter.Letter - 1) : letter.Letter;
            if (_book == null || !_book.TryGet(lookup, out var entry))
            {
                cast = default;
                return false;
            }

            float brush = _book.EvaluateBrushPower(letter.WorstJamoDistance, letter.StrokeDuration);
            float speedMul = entry.ProjectileSpeedMul > 0f ? entry.ProjectileSpeedMul : 1f; // 미기입 데이터 안전
            cast = new SpellCast(letter.Letter, entry.Kind, entry.Element, entry.BasePower * brush,
                entry.ToAreaSpec(), speedMul);
            return true;
        }
    }
}
