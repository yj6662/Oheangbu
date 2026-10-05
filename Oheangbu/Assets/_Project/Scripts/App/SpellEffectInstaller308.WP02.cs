using VContainer;

namespace Oheangbu.App
{
    // WP-02 (SPEC-SPELL-120-308 section 13): numeric / conditional attacks. Three handlers; the fourth glyph of the package
    // reuses core.single (registered by the foundation) with its own base power, so it needs no class here.
    public static partial class SpellEffectInstaller308
    {
        static partial void InstallWP02(SpellEffectList308 effects)
        {
            effects.Register<VolleyRampEffect308>();
            effects.Register<VolleyHeatEffect308>();
            effects.Register<ExecuteEffect308>();
        }
    }
}
