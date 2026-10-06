using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // Unity half shared by the lingering-zone handlers (SPEC-SPELL-120-308 WP-06). The zones themselves live in the one
    // SpellZoneRuntime308 of the combat scope; a handler owns the zones that carry its id. Per frame: act on the zones
    // (the rule functions say who is inside and how many steps are due), then sweep the ones that ran out and tell the
    // presenter. A handler made with new (checks) gets a runtime of its own; the container hands every handler the shared one.
    public abstract class ZoneEffect308 : ISpellEffect
    {
        // The zone list this handler works on (shared by every handler the container builds; a later package reads it too).
        public SpellZoneRuntime308 Zones { get; }
        readonly List<SpellZone308> _removed = new List<SpellZone308>();

        protected ZoneEffect308(SpellZoneRuntime308 zones) { Zones = zones ?? new SpellZoneRuntime308(); }

        public abstract string Id { get; }
        public abstract IReadOnlyList<SpellParamSpec> Params { get; }
        public virtual bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;
        public abstract bool Commit(in SpellCastContext ctx);

        public void Tick(in SpellTickContext ctx)
        {
            // act first: a zone that ended inside this frame still gets its last steps and its part of the frame
            Act(ctx);
            Zones.Sweep(Id, ctx.Now, _removed);
            Retire(ctx.Fx);
        }

        // The host ends every presentation right after the effects are cleared, so nothing is cued here.
        public virtual void Clear(SpellClearReason reason)
        {
            Zones.Drop(Id, _removed);
            for (int i = 0; i < _removed.Count; i++) OnRemoved(_removed[i]);
            _removed.Clear();
        }

        protected abstract void Act(in SpellTickContext ctx);

        // A zone left the list: undo what the handler did to the world for it (presentation is handled by Retire).
        protected virtual void OnRemoved(in SpellZone308 zone) { }

        // Adds a zone (0 = refused). Zones it pushes out are retired on the spot.
        protected int Place(SpellZone308 zone, int max, float now, ISpellPresenter fx)
        {
            int id = Zones.Add(zone, max, now, _removed);
            Retire(fx);
            return id;
        }

        // Who the zone's own judgements are attributed to.
        protected static Object Instigator(ISpellCastHost host)
            => host.PlayerVitals != null ? (Object)host.PlayerVitals : host.Player;

        void Retire(ISpellPresenter fx)
        {
            for (int i = 0; i < _removed.Count; i++)
            {
                var zone = _removed[i];
                OnRemoved(zone);
                // one presentation may stand for several zones of a cast: it follows the last of them and is told once
                if (zone.Fx == 0 || fx == null || Zones.HoldsFx(Id, zone.Fx) || ToldAlready(i, zone.Fx)) continue;
                var handle = new SpellFxHandle(zone.Fx);
                if (zone.End == SpellZoneEnd.Replaced || zone.End == SpellZoneEnd.Dropped) { fx.End(handle, true); continue; }
                fx.Cue(handle, zone.End == SpellZoneEnd.Broken ? SpellFxCue.Break : SpellFxCue.Expire,
                    new SpellFxCueArgs { Point = zone.Centre, At = zone.Until, Value = zone.Radius });
            }
            _removed.Clear();
        }

        bool ToldAlready(int upTo, int fx)
        {
            for (int i = 0; i < upTo; i++) if (_removed[i].Fx == fx) return true;
            return false;
        }
    }
}
