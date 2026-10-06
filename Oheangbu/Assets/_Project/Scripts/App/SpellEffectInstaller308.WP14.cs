using VContainer;

namespace Oheangbu.App
{
    // #308 WP-14: sword form (the five eo + siot glyphs).
    public static partial class SpellEffectInstaller308
    {
        static partial void InstallWP14(SpellEffectList308 effects)
        {
            effects.Register<SwordFormEffect308>();
        }
    }
}
