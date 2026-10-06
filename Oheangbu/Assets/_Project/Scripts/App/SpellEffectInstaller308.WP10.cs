using VContainer;

namespace Oheangbu.App
{
    // #308 WP-10: harmony install (the five a + mieum glyphs).
    public static partial class SpellEffectInstaller308
    {
        static partial void InstallWP10(SpellEffectList308 effects)
        {
            effects.Register<InstallEffect308>();
        }
    }
}
