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
        public readonly float Brush;    // #308: the brush multiplier already inside Power (form x speed through the curve); one for legacy callers
        public readonly float Brush01;  // #308: form x speed before the curve, 0..1 (read only: presentation grade); one for legacy callers

        public AreaShape AreaShape => Area.Shape;

        public SpellCast(char letter, SpellKind kind, Element element, float power,
            AreaSpec area, float speedMul, float holdScale = 1f, float brush = 1f, float brush01 = 1f)
        {
            Letter = letter;
            Kind = kind;
            Element = element;
            Power = power;
            Area = area;
            SpeedMul = speedMul;
            HoldScale = float.IsFinite(holdScale) && holdScale > 0 ? UnityEngine.Mathf.Clamp(holdScale, .1f, 1f) : 1f;
            Brush = brush;
            Brush01 = brush01;
        }
    }

    // DrawnLetter → SpellCast 해석기. 순수 로직(Mono 아님) — VContainer 등록 대상.
    // 위력은 여기서 확정한다: Combat은 「무엇을 얼마나」만 받고, 필세 수식은 Spellcraft 소유
    // (DrawnLetter 주석의 분업 — 원자료는 Drawing, 해석은 이쪽).
    // #308 (SPEC-SPELL-120-308 section 3): the glyph -> effect route is the book's table (SpellBookSO.TryGetRow), not a list in
    // this file. A book without imported rows answers with the row its _entries implied before #308.
    public sealed class SpellResolver
    {
        private readonly SpellBookSO _book;

        public SpellResolver(SpellBookSO book)
        {
            _book = book;
        }

        // The one entry point. Ok = cast (and its row); anything else is a misfire whose reason the caller may re-broadcast.
        public SpellResolveStatus Resolve(DrawnLetter letter, ISpellGate gate, out SpellCast cast, out SpellRow row)
        {
            cast = default; row = null;
            if (_book == null || !_book.TryGetRow(letter.Letter, out row)) return SpellResolveStatus.NotInVocabulary;
            float brush = _book.EvaluateBrushPower(letter.WorstJamoDistance, letter.StrokeDuration);
            float brush01 = _book.EvaluateBrush01(letter.WorstJamoDistance, letter.StrokeDuration);
            return SpellResolveCore308.Resolve(letter.Letter, brush, brush01, row, gate, out cast);
        }

        // Row lookup without a gate (callers that hold only a glyph: trace judgement, reports).
        public bool TryRow(char letter, out SpellRow row)
        {
            row = null;
            return _book != null && _book.TryGetRow(letter, out row);
        }

        public bool TryUnlock(SpellFinal final, out SpellUnlockRule rule)
        {
            rule = default;
            return _book != null && _book.TryGetUnlock(final, out rule);
        }

        public bool HasTable => _book != null && _book.HasTable;

        // ---- old overloads (Temporary Exception): the five opt-in flags become a LegacySpellGate over the same core. ----
        // false = 어휘 미등재(프로토 미러 밖의 글자) — 효과 없음.
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
            var gate = new LegacySpellGate(allowGuk, allowEABuffs, allowWards, allowGiyeok, allowMum);
            return Resolve(letter, gate, out cast, out _) == SpellResolveStatus.Ok;
        }
    }
}
