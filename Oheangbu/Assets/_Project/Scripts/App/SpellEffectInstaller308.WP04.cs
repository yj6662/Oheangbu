using VContainer;

namespace Oheangbu.App
{
    // SPEC-SPELL-120-308 WP-04: effects derived from a confirmed hit (crescents, droplets, ricochet, spreading ember, cycle shot).
    public static partial class SpellEffectInstaller308
    {
        static partial void InstallWP04(SpellEffectList308 effects)
        {
            effects.Register<CrescentEffect308>();
            effects.Register<DropletEffect308>();
            effects.Register<RicochetEffect308>();
            effects.Register<EmberSpreadEffect308>();
            effects.Register<CycleEffect308>();
        }
    }
}
