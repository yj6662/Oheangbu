using VContainer;

namespace Oheangbu.App
{
    // WP-12 path-opening field glyphs (SPEC-SPELL-120-308, opened by D308-13 Q5).
    public static partial class SpellEffectInstaller308
    {
        static partial void InstallWP12(SpellEffectList308 effects)
        {
            effects.Register<FieldBurnEffect308>();
            effects.Register<FieldCutEffect308>();
            effects.Register<FieldPurifyEffect308>();
        }
    }
}
