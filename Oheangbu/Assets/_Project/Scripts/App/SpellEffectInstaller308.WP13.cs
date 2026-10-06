using VContainer;

namespace Oheangbu.App
{
    // WP-13 (SPEC-SPELL-120-308, opened by D308-13 Q5): the eight glyphs that answer to something an enemy does or leaves
    // behind (a projectile in the air, a guard stance, a strike, a hazard on the ground). Each is the spell rule plus a
    // TEST target; the enemies that really do those things are separate work.
    // The haze handler takes the zone list of WP-06 through its constructor (registered once, in InstallWP06).
    public static partial class SpellEffectInstaller308
    {
        static partial void InstallWP13(SpellEffectList308 effects)
        {
            effects.Register<InterceptShotEffect308>();
            effects.Register<InterceptBarrageEffect308>();
            effects.Register<UnblockableEffect308>();
            effects.Register<HazeZoneEffect308>();
            effects.Register<PurgeEffect308>();
            effects.Register<RockCoverEffect308>();
            effects.Register<WaveCoverEffect308>();
        }
    }
}
