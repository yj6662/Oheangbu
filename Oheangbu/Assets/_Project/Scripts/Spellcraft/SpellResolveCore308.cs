// PURE308
namespace Oheangbu.Spellcraft
{
    // The five opt-in flags of the old TryResolve overloads as a gate. A row without a legacy feature never opens here, so
    // old scenes, old checks and the 50-odd editor tools keep seeing exactly the 36 glyphs they saw before #308.
    public sealed class LegacySpellGate : ISpellGate
    {
        private readonly bool _guk, _buffs, _wards, _giyeok, _mum;

        public LegacySpellGate(bool allowGuk, bool allowEABuffs, bool allowWards, bool allowGiyeok, bool allowMum)
        { _guk = allowGuk; _buffs = allowEABuffs; _wards = allowWards; _giyeok = allowGiyeok; _mum = allowMum; }

        public SpellResolveStatus Judge(SpellRow row)
        {
            bool open;
            switch (row.Feature)
            {
                case SpellLegacyFeature.Book: open = true; break;
                case SpellLegacyFeature.Ward: open = _wards; break;
                case SpellLegacyFeature.BuffG: open = _buffs; break;
                case SpellLegacyFeature.Guk: open = _guk; break;
                case SpellLegacyFeature.Giyeok: open = _giyeok; break;
                case SpellLegacyFeature.Mum: open = _mum; break;
                default: open = false; break;
            }
            return open ? SpellResolveStatus.Ok : SpellResolveStatus.Unavailable;
        }
    }

    // Glyph -> cast, without any Unity object: the resolver feeds it the row and the brush multiplier.
    public static class SpellResolveCore308
    {
        // row = null when the glyph is outside the vocabulary. brush = the power multiplier (form x speed through the curve),
        // brush01 = form x speed before the curve. The cast is only meaningful when the result is Ok.
        public static SpellResolveStatus Resolve(char letter, float brush, float brush01, SpellRow row, ISpellGate gate, out SpellCast cast)
        {
            cast = default;
            if (row == null) return SpellResolveStatus.NotInVocabulary;
            if (row.Category == SpellCategory.Blank) return SpellResolveStatus.Blank;
            if (!row.HasHandler) return SpellResolveStatus.Unimplemented;
            SpellResolveStatus status = gate != null ? gate.Judge(row) : SpellResolveStatus.Unavailable;
            if (status != SpellResolveStatus.Ok) return status;
            // A row without base power casts with power 0 exactly, as the old feature branches did.
            float power = row.BasePower == 0f ? 0f : row.BasePower * brush;
            float speedMul = row.ProjectileSpeedMul > 0f ? row.ProjectileSpeedMul : 1f; // unfilled data = 1
            cast = new SpellCast(letter, row.Kind, row.Element, power, row.ToAreaSpec(), speedMul, 1f, brush, brush01);
            return SpellResolveStatus.Ok;
        }
    }
}
