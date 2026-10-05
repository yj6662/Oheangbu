using VContainer;

namespace Oheangbu.App
{
    // SPEC-SPELL-120-308 WP-08: the nine self buffs (one handler per glyph, shared base SelfBuffEffect308, rules in BuffRule308).
    public static partial class SpellEffectInstaller308
    {
        static partial void InstallWP08(SpellEffectList308 effects)
        {
            effects.Register<BurstHealEffect308>();
            effects.Register<EmpowerEffect308>();
            effects.Register<SteadfastEffect308>();
            effects.Register<SwiftEffect308>();
            effects.Register<ChainDiscountEffect308>();
            effects.Register<InkReserveEffect308>();
            effects.Register<ReflectEffect308>();
            effects.Register<SlowbackEffect308>();
            effects.Register<InsightEffect308>();
        }
    }
}
