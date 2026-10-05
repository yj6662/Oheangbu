using VContainer;

namespace Oheangbu.App
{
    // #308 WP-09 (companion shots): buff.companion, one handler for the five companion buff rows.
    public static partial class SpellEffectInstaller308
    {
        static partial void InstallWP09(SpellEffectList308 effects)
        {
            effects.Register<CompanionShotEffect308>();
        }
    }
}
