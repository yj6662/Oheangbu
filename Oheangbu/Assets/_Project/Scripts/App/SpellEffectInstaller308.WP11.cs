using VContainer;

namespace Oheangbu.App
{
    // WP-11 (SPEC-SPELL-120-308): the three single shots that are handed over from the old route (SpellTakeover308).
    // Registering a handler here is what switches the handover on for a wiring that has the registry.
    public static partial class SpellEffectInstaller308
    {
        static partial void InstallWP11(SpellEffectList308 effects)
        {
            effects.Register<RangedSingleEffect308>();
            effects.Register<BallisticEffect308>();
            effects.Register<HomingEffect308>();
        }
    }
}
