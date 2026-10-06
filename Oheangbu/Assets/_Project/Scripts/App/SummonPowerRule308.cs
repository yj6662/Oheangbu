// PURE308
namespace Oheangbu.App
{
    // Where a summon's power comes from (SPEC-SPELL-120-308 WP-00, Q3 default waiting for the user). The spell book keeps
    // base power 0 for the five summon glyphs, so a hand-drawn summon used to arrive with power 0 and every strike of the
    // summon was refused. The summon profile now carries the base power; the brush and hold multipliers apply as they do
    // for every other cast. A caller that hands in a positive power (the existing checks) is used as is.
    public static class SummonPowerRule308
    {
        public static float CastPower(float castPower, float profileBasePower, float brush, float holdScale)
            => castPower > 0f ? castPower : profileBasePower * brush * holdScale;
    }
}
