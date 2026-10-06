using VContainer;

namespace Oheangbu.App
{
    // #308 WP-07: enemy modifiers (damage-taken brand, defence shred, defence-ignoring hit, dulled next attack, weakened attack).
    public static partial class SpellEffectInstaller308
    {
        static partial void InstallWP07(SpellEffectList308 effects)
        {
            effects.Register<BrandEffect308>();
            effects.Register<ArmorBreakEffect308>();
            effects.Register<DefencePierceEffect308>();
            effects.Register<DullEffect308>();
            effects.Register<WeakenEffect308>();
        }
    }
}
