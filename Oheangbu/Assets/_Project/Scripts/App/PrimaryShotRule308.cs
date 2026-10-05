// PURE308
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // Which judgement flies the first stage of a single shot (Spellcraft Bible chapter 4: range, parabola and guidance are
    // properties of the initial consonant; a final consonant adds an effect, it does not change the flight).
    // The answer is data: the row of the glyph without a final. When that row is run by a handler (today: the three
    // handed-over single shots of the WP-11 sheet), every glyph of the same body flies its first stage through that
    // handler, with that row's numbers. Otherwise the first stage is the wiring's lock-on single judgement, as before.
    // No glyph is named here.
    public static class PrimaryShotRule308
    {
        public const char None = '\0';

        // The glyph whose row says how this row's first stage flies: the same initial and vowel without a final (the glyph
        // itself when it has no final). None = the row's first judgement is not a single shot.
        public static char BallisticsLetter(SpellRow row)
        {
            if (row == null || string.IsNullOrEmpty(row.Letter) || row.Kind != SpellKind.AttackSingle || row.AreaShape != AreaShape.None) return None;
            return SpellGrammar308.BaseOf(row.Letter[0]);
        }

        // The handler id that flies the first stage. ballistics = the row of BallisticsLetter; handlerRegistered = an effect
        // registry is present and holds ballistics.Handler. "" = the lock-on single judgement.
        public static string StageHandler(SpellRow ballistics, bool handlerRegistered)
        {
            if (ballistics == null || !ballistics.HasHandler || ballistics.Final != SpellFinal.None || !handlerRegistered) return "";
            return SpellTakeover308.UsesHandler(ballistics, true) ? ballistics.Handler : "";
        }

        // A row whose first stage is not its own: a single shot behind a final consonant. The table's rows of this kind whose
        // base row answers a stage handler are the glyphs that must never fall back to the lock-on plan.
        public static bool FollowsBase(SpellRow row)
        {
            return row != null && row.Final != SpellFinal.None && row.HasHandler && row.Feature == SpellLegacyFeature.None &&
                BallisticsLetter(row) != None;
        }
    }
}
