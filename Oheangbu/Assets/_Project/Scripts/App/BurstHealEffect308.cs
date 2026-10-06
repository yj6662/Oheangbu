using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // WP-08 wood buff with the nieun final: a large heal paid out evenly over a short window (BurstHealState308). The heal
    // runs in Tick, never in a player-hit hook. A dead player is not revived: Prepare refuses, and a death inside the window
    // ends it.
    public sealed class BurstHealEffect308 : SelfBuffEffect308
    {
        public const string HandlerId = "buff.burstheal";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("duration", .1f, 600f), new SpellParamSpec("heal.hp01", 0f, 1f) };
        readonly BurstHealState308 _heal = new BurstHealState308();
        PlayerVitals _vitals;

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        protected override bool OnActivating(in SpellCastContext ctx, float duration)
        {
            _vitals = ctx.Host.PlayerVitals;
            // a recast first pays what the old window owed up to now, then replaces the rest
            Give(_heal.Begin(ctx.Now, duration, ctx.Row.F("heal.hp01", 0f), _vitals.Hp01 > 0f));
            return true;
        }

        protected override void OnTick(in SpellTickContext ctx)
        {
            if (_vitals != null && _heal.Running) Give(_heal.Advance(ctx.Now, _vitals.Hp01 > 0f));
        }

        protected override void OnCleared(SpellClearReason reason) { _heal.Clear(); _vitals = null; }

        void Give(float share01)
        {
            if (share01 > 0f && _vitals != null) _vitals.Heal(_vitals.MaxHp * share01);
        }
    }
}
