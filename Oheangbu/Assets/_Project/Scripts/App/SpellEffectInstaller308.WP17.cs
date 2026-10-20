using VContainer;

namespace Oheangbu.App
{
    // #308 WP-17 (D308-121): the twenty 결계 (domain) glyphs. They share the zone list of WP-06 (registered once, in
    // InstallWP06) and one DomainSlot308 (the player stands in one domain at a time, whatever its glyph).
    public static partial class SpellEffectInstaller308
    {
        static partial void InstallWP17(SpellEffectList308 effects)
        {
            effects.Builder.Register<DomainSlot308>(Lifetime.Singleton);
            effects.Register<DomainSealEffect308>();
            effects.Register<DomainGroundEffect308>();
            effects.Register<DomainHushEffect308>();
            effects.Register<DomainUnguardEffect308>();
            effects.Register<DomainSlowTellEffect308>();
            effects.Register<DomainSlowTurnEffect308>();
            effects.Register<DomainAimLockEffect308>();
            effects.Register<DomainCageEffect308>();
            effects.Register<DomainThresholdEffect308>();
            effects.Register<DomainWalkWardEffect308>();
            effects.Register<DomainShellEffect308>();
            effects.Register<DomainBurnShotEffect308>();
            effects.Register<DomainReflectShotEffect308>();
            effects.Register<DomainPushEffect308>();
            effects.Register<DomainPullEffect308>();
            effects.Register<DomainWallEffect308>();
            effects.Register<DomainRegenEffect308>();
            effects.Register<DomainSwordHoldEffect308>();
            effects.Register<DomainAnchorEffect308>();
            effects.Register<DomainStatusWardEffect308>();
        }
    }
}
