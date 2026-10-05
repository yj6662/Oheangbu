// PURE308
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // Numbers of the wood lift (SPEC-SPELL-120-308 WP-00): the row parameters lift.height / lift.rise / lift.descent /
    // lift.hold. The caller supplies the values used when a row does not carry them (a book without an imported table).
    public readonly struct FieldLiftSpec
    {
        public readonly float Height, RiseSpeed, DescentSpeed, HoldSeconds;

        public FieldLiftSpec(float height, float riseSpeed, float descentSpeed, float holdSeconds)
        { Height = height; RiseSpeed = riseSpeed; DescentSpeed = descentSpeed; HoldSeconds = holdSeconds; }

        public bool Valid => Height > 0f && RiseSpeed > 0f && DescentSpeed > 0f && HoldSeconds >= 0f &&
            !float.IsInfinity(Height) && !float.IsInfinity(RiseSpeed) && !float.IsInfinity(DescentSpeed) && !float.IsInfinity(HoldSeconds);

        public static FieldLiftSpec From(SpellRow row, in FieldLiftSpec fallback)
        {
            if (row == null) return fallback;
            var spec = new FieldLiftSpec(row.F("lift.height", fallback.Height), row.F("lift.rise", fallback.RiseSpeed),
                row.F("lift.descent", fallback.DescentSpeed), row.F("lift.hold", fallback.HoldSeconds));
            return spec.Valid ? spec : fallback;
        }
    }
}
