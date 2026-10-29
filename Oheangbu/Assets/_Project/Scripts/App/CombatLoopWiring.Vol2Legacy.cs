using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // D308-150 V9 (vol.2 legacy bridge): read-only lookups the bridge's stage (Vol2LegacyStage) needs to play a glyph's OLD visuals in a Vol2 scope - the visual set
    // the main game reads (the stroke adapter's), and the legacy row of a glyph (its area shape numbers). Nothing here changes a rule, a clock or a cast.
    public sealed partial class CombatLoopWiring
    {
        /// <summary>The glyph -> body prefab set the old route presents with (the stroke adapter's; null without one).</summary>
        public SpellVisualSetSO VisualSet308 => _brushAdapter != null ? _brushAdapter.VisualSet : null;

        /// <summary>The stroke adapter (where the old route drew the letter: its drawing surface is where the old guard / launch fallback stand).</summary>
        public BrushStrokeFeedAdapter BrushAdapter308 => _brushAdapter;

        /// <summary>The legacy table row of a glyph (null when the resolver has none).</summary>
        public SpellRow LegacyRow308(char letter) => RowFor(letter);
    }
}
