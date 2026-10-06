using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // Shared plumbing of the nine self buffs of SPEC-SPELL-120-308 WP-08 (one handler class per glyph derives from this).
    //  - the timer is the host's shared SpellBuffState308, keyed by the glyph that was cast: recasting replaces the remaining
    //    time, different buffs run side by side, nothing is saved, death / rest / scene leave clear it (Q8 default),
    //  - the presentation is one Aura request that follows the player, plus an Expire or Consume cue; nothing is read back,
    //  - hooks that get no host (cost, cast) use the timer reference kept from the last Commit.
    // Every number comes from the row; each handler file declares its own keys, "duration" included (the static check reads
    // the declarations per file).
    public abstract class SelfBuffEffect308 : ISpellEffect
    {
        public const string DurationKey = "duration";

        SpellFxHandle _aura;
        bool _running;

        protected SpellBuffState308 Buffs { get; private set; }
        protected char Letter { get; private set; }
        protected Element Element { get; private set; }

        public abstract string Id { get; }
        public abstract IReadOnlyList<SpellParamSpec> Params { get; }

        // 0 = a plain timed buff; above 0 the buff also ends with its last charge.
        protected virtual int Charges => 0;

        public bool Active(float now) => Buffs != null && Buffs.Active(Letter, now);

        // A buff needs a living caster (no revival through a buff) and a duration from its row.
        public virtual bool Prepare(in SpellCastContext ctx)
            => ctx.Host != null && ctx.Host.Buffs != null && ctx.Row != null && ctx.Row.F(DurationKey, 0f) > 0f &&
               ctx.Host.PlayerVitals != null && ctx.Host.PlayerVitals.Hp01 > 0f;

        public bool Commit(in SpellCastContext ctx)
        {
            float duration = ctx.Row.F(DurationKey, 0f);
            if (!(duration > 0f) || !OnActivating(ctx, duration)) return false;
            if (_running && ctx.Fx != null && _aura.Shown) ctx.Fx.End(_aura, true);   // a recast replaces the old aura
            Buffs = ctx.Host.Buffs; Letter = ctx.Cast.Letter; Element = ctx.Cast.Element;
            Buffs.Activate(Letter, ctx.Now, duration, Charges);
            _running = true; _aura = default;
            // The rule is complete at this point. A presenter that fails must not undo it.
            try
            {
                if (ctx.Fx != null)
                {
                    Vector3 origin = ctx.Host.PlayerPosition;
                    _aura = ctx.Fx.Begin(new SpellFxRequest
                    {
                        Letter = Letter, Role = SpellFxRole.Aura, Element = Element, Origin = origin, FallbackPoint = origin + ctx.Host.PlayerForward,
                        Follow = ctx.Host.Player, Duration = duration, Grade01 = ctx.Cast.Brush01,
                    });
                    if (_aura.Shown) ctx.Host.PresentationOwned();
                }
            }
            catch (Exception exception) { Debug.LogException(exception); _aura = default; }
            return true;
        }

        public void Tick(in SpellTickContext ctx)
        {
            OnTick(ctx);
            if (_running && !Active(ctx.Now)) Finish(ctx.Fx, SpellFxCue.Expire);
        }

        public void Clear(SpellClearReason reason)
        {
            OnCleared(reason);
            // the host ends every presentation itself (ISpellPresenter.EndAll) and clears the shared timers
            Buffs = null; Letter = default; _aura = default; _running = false;
        }

        // The buff is over (ran out, or its last charge was used): one cue, then the aura is released.
        protected void Finish(ISpellPresenter fx, SpellFxCue cue)
        {
            if (!_running) return;
            _running = false;
            if (fx != null && _aura.Shown) { fx.Cue(_aura, cue, default); fx.End(_aura); }
            _aura = default;
        }

        protected static bool Melee(IncomingDamageKind kind) => kind == IncomingDamageKind.Melee || kind == IncomingDamageKind.ElementalMelee;

        // Who a retaliation is attributed to: the player's vitals when the scene has them, else the player object.
        protected static UnityEngine.Object Instigator(ISpellCastHost host)
            => host.PlayerVitals != null ? (UnityEngine.Object)host.PlayerVitals : host.Player;

        // Reads the row and prepares the buff's own state; false refuses the cast (the host restores the ink).
        protected virtual bool OnActivating(in SpellCastContext ctx, float duration) => true;
        protected virtual void OnTick(in SpellTickContext ctx) { }
        protected virtual void OnCleared(SpellClearReason reason) { }
    }
}
