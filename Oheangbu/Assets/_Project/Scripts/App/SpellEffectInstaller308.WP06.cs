using VContainer;

namespace Oheangbu.App
{
    // WP-06 lingering zones (SPEC-SPELL-120-308). The zone list is one object per combat scope: registered here and handed
    // to each zone handler through its constructor (the container picks the constructor that takes it). Later packages that
    // need the zones (WP-13) ask for SpellZoneRuntime308 in their own constructors; they do not register it again.
    public static partial class SpellEffectInstaller308
    {
        static partial void InstallWP06(SpellEffectList308 effects)
        {
            effects.Builder.Register<SpellZoneRuntime308>(Lifetime.Singleton);
            effects.Register<VineZoneEffect308>();
            effects.Register<SpikeZoneEffect308>();
            effects.Register<HealZoneEffect308>();
            effects.Register<HealTreeEffect308>();
        }
    }
}
