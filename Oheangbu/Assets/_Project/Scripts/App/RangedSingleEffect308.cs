using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // single.ranged (SPEC-SPELL-120-308 WP-11): the straight single shot with a range. A target within the row's range is
    // hit by the wiring's own single judgement, unchanged (same flight time, same scheduled hit, the stroke adapter
    // presents it as before). A target beyond the range is not hit: the ink is spent, nothing is scheduled, and the
    // adapter flies its default pattern for a cast without a target. A cast without any target is the air cast of before.
    // Reached only through the handover of SpellTakeover308: without an effect registry the row casts on its old route.
    // It is also the first stage of every glyph of the same body behind a final consonant (ISpellPrimaryStage308): those
    // glyphs reach as far as this row's range and no further.
    public sealed class RangedSingleEffect308 : ISpellEffect, ISpellPrimaryStage308
    {
        public const string HandlerId = "single.ranged";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("range", .5f, 100f) };

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int OutOfRangeCasts { get; private set; }   // casts whose target stood beyond the range (checks read this)

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx) => Launch(ctx.Host, ctx.Cast, ctx.Row, ctx.Now, true).Launched;

        public SpellPrimaryShot308 Launch(ISpellCastHost host, in SpellCast cast, SpellRow ballistics, float now, bool feedAdapter)
        {
            EnemyVitals target = host.AimedTarget();
            if (target != null && !RangedSingleRule308.Reaches(host.PlayerPosition, target.transform.position, ballistics.F("range", 0f)))
            {
                OutOfRangeCasts++;
                return SpellPrimaryShot308.Nothing;            // the shot dies at the end of its range
            }
            return SpellPrimary308.LockOn(host, cast, feedAdapter);
        }

        // this stage holds nothing: within range the hit is the wiring's own scheduled hit (the host takes that back itself)
        public int HeldShots => 0;
        public void Recall(int mark) { }

        public void Tick(in SpellTickContext ctx) { }
        public void Clear(SpellClearReason reason) { }
    }
}
