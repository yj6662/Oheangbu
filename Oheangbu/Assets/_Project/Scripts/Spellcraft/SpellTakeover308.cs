// PURE308
namespace Oheangbu.Spellcraft
{
    // #308 WP-11 handover rule (SPEC-SPELL-120-308 progress record, decision "option ga"). Three glyphs that cast before
    // #308 (the wood, earth and water single shots) get a registered handler without losing their old route:
    //  - their rows KEEP Feature = book, so the old bool overloads, a wiring without an effect registry, the 36-row counts
    //    and the regression against the pre-#308 resolver see exactly what they saw before;
    //  - a book row that names a handler other than the built-in id of its own shape is a declared handover;
    //  - the handler route is taken only when an effect registry is present and holds that handler.
    // No glyph is named here: the rule sheet (Rules308_WP11.csv) says which rows are handed over.
    public static class SpellTakeover308
    {
        // The row declares a handover: a book row whose handler is not the built-in handler of its kind and shape.
        public static bool Declared(SpellRow row)
        {
            return row != null && row.Feature == SpellLegacyFeature.Book && row.HasHandler &&
                row.Handler != SpellTableBuilder308.CoreHandler(row.Kind, row.AreaShape);
        }

        // The dispatch rule of the wiring. handlerRegistered = an effect registry is present and has row.Handler.
        // Without a registry this is exactly the rule before WP-11: only a row without a legacy feature goes to a handler.
        public static bool UsesHandler(SpellRow row, bool handlerRegistered)
        {
            if (row == null) return false;
            return row.Feature == SpellLegacyFeature.None || (handlerRegistered && Declared(row));
        }
    }
}
