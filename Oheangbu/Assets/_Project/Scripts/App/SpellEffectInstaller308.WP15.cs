using VContainer;

namespace Oheangbu.App
{
    // #308 WP-15 (D308-113): the pair split of the ieung glyphs whose act was shared - nang breaks a guard (it was the
    // defence-ignoring hit of gang), mong blinds (it was the haze of nong). The blind handler takes the zone list of
    // WP-06 through its constructor (registered once, in InstallWP06).
    public static partial class SpellEffectInstaller308
    {
        static partial void InstallWP15(SpellEffectList308 effects)
        {
            effects.Register<GuardBreakEffect308>();
            effects.Register<BlindZoneEffect308>();
        }
    }
}