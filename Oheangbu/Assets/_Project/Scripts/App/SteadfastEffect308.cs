using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // WP-08 fire buff with the mieum final: while it runs, a hit does not make the wiring drop the letter being drawn.
    // The hit itself is untouched (the hook runs after the damage was taken and cannot change it); nothing is given back.
    public sealed class SteadfastEffect308 : SelfBuffEffect308, IPlayerHitHook
    {
        public const string HandlerId = "buff.steadfast";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("duration", .1f, 600f) };

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        public void OnPlayerHit(in PlayerHitInfo hit, in SpellTickContext ctx, ref bool interruptsDrawing)
        {
            BuffRule308.Steadfast(Active(ctx.Now), hit.Amount, ref interruptsDrawing);
        }
    }
}
