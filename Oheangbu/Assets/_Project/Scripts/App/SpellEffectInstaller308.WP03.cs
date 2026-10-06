using VContainer;

namespace Oheangbu.App
{
    // WP-03 (SPEC-SPELL-120-308): the five two-stage attack effects.
    public static partial class SpellEffectInstaller308
    {
        static partial void InstallWP03(SpellEffectList308 effects)
        {
            effects.Register<BackblastEffect308>();
            effects.Register<ImpactBlastEffect308>();
            effects.Register<SkipEffect308>();
            effects.Register<StalagmiteEffect308>();
            effects.Register<TideEffect308>();
        }
    }
}
