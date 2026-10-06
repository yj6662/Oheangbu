using VContainer;

namespace Oheangbu.App
{
    // #308 WP-05 (enemy control): circle.interrupt, single.freeze, path.undertow.
    public static partial class SpellEffectInstaller308
    {
        static partial void InstallWP05(SpellEffectList308 effects)
        {
            effects.Register<InterruptEffect308>();
            effects.Register<FreezeEffect308>();
            effects.Register<UndertowEffect308>();
        }
    }
}
