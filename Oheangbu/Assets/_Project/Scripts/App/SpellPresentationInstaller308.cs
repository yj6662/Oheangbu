using VContainer;

namespace Oheangbu.App
{
    // The single registration of the spell presenter (SPEC-SPELL-120-308 section 9). The deploy layer
    // (SPEC-SPELL-DEPLOY-308) implements ISpellPresenter, or wraps Vfx120SpellPresenter308, and changes only this line.
    // Effect files and rule sheets never name a presenter.
    public static class SpellPresentationInstaller308
    {
        public static void Install(IContainerBuilder builder)
        {
            builder.Register<SpellVFX120.SpellDeployPresenter308>(Lifetime.Singleton).As<ISpellPresenter>();
        }
    }
}
