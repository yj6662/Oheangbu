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
        public readonly AreaShape AreaShape; // 광역 실판정 형상 [#137] — None=단일 유지
        public readonly float SpeedMul;      // 탄속 배율(정규화 완료 — 항상 양수)

        public SpellCast(char letter, SpellKind kind, Element element, float power,
            AreaShape areaShape, float speedMul)
        {
            Letter = letter;
            Kind = kind;
            Element = element;
            Power = power;
            AreaShape = areaShape;
            SpeedMul = speedMul;
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
        {
            if (_book == null || !_book.TryGet(letter.Letter, out var entry))
            {
                cast = default;
                return false;
            }

            float brush = _book.EvaluateBrushPower(letter.WorstJamoDistance, letter.StrokeDuration);
            float speedMul = entry.ProjectileSpeedMul > 0f ? entry.ProjectileSpeedMul : 1f; // 미기입 데이터 안전
            cast = new SpellCast(letter.Letter, entry.Kind, entry.Element, entry.BasePower * brush,
                entry.AreaShape, speedMul);
            return true;
        }
    }
}
