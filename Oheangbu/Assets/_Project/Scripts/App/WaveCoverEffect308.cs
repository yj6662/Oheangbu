using System;
using System.Collections.Generic;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // path.cover (SPEC-SPELL-120-308 WP-13, opened by D308-13 Q5): a root wave that runs a corridor, hurts a little, and
    // covers the player who walks behind it.
    //  - first hit = the host's path judgement with the row's own corridor and (small) base power, presented by the stroke
    //    adapter: every enemy in the corridor is hit once when the front reaches it;
    //  - while the front runs, it is a wall across the corridor (CoverRule308.WallOf): an enemy ahead of the wave cannot
    //    reach the player behind it with a ranged strike, as long as the line between them crosses the wall. The cover moves
    //    with the front and ends when the wave reaches the far end of its corridor.
    // The row carries no parameter of its own: the corridor, the speed and the power are the row's area columns.
    public sealed class WaveCoverEffect308 : CoverEffect308
    {
        public const string HandlerId = "path.cover";
        static readonly SpellParamSpec[] Specs = Array.Empty<SpellParamSpec>();

        struct Wave { public PathFront308 Front; public long Token; }
        readonly List<Wave> _waves = new List<Wave>();

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        public override bool Commit(in SpellCastContext ctx)
        {
            var host = ctx.Host;
            var plan = host.PlanPath(ctx.Cast, true);
            if (plan == null) return false;
            var area = plan.Area;
            if (area == null) return true;
            var front = new PathFront308(area.Point, area.Direction, area.Radius, area.Length, area.Speed, ctx.Now + area.Delay, false);
            if (!front.Valid) return true;
            long token = NewToken(host, ctx.Cast.Element);
            _waves.Add(new Wave { Front = front, Token = token });
            Add(CoverRule308.WallOf(front, token, ctx.Now), 0, ctx.Fx);
            return true;
        }

        // The wall is where the front is now.
        protected override void Advance(in SpellTickContext ctx)
        {
            for (int i = 0; i < PieceCount; i++)
            {
                var piece = PieceAt(i);
                for (int w = 0; w < _waves.Count; w++)
                    if (_waves[w].Token == piece.Token) { SetPiece(i, CoverRule308.WallOf(_waves[w].Front, piece.Token, ctx.Now)); break; }
            }
        }

        protected override void OnRetired(in SpellCover308 piece)
        {
            for (int w = _waves.Count - 1; w >= 0; w--)
                if (_waves[w].Token == piece.Token) _waves.RemoveAt(w);
        }

        public override void Clear(SpellClearReason reason)
        {
            base.Clear(reason);
            _waves.Clear();
        }
    }
}
