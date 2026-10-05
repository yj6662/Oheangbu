// PURE308
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // Shared rules of the two-stage attack glyphs (SPEC-SPELL-120-308 WP-03). The primary judgement of such a glyph is the
    // base glyph's shape, scheduled by the wiring at cast time exactly as before. The second judgement is made later, on
    // the enemies as they are then:
    //  - only an enemy that is still in the life the cast saw can be hit (a target the primary killed gets nothing, and a
    //    life number that changed cancels: dead, disabled, restored),
    //  - every enemy is hit at most once by one second stage,
    //  - a second stage is judged with the clock of the previous frame (TwoStageClock308), so every primary hit that was
    //    due by then has already been applied, whatever the frame length.
    // The per-glyph files (BackblastRule308, ImpactBlastRule308, SkipRule308, StalagmiteRule308, TideRule308) only say
    // where and when; PathFrontRule308 is the moving-front judgement two of them share.
    public static class TwoStageRule308
    {
        // The life the cast saw: alive at cast, alive now, same life number.
        public static bool SameLife(in SpellActorSnap atCast, in SpellActorSnap now)
            => atCast.Alive && now.Alive && atCast.Life == now.Life;

        // judged = the clock a frame judges with (TwoStageClock308.Advance). False for a time that is not a number.
        public static bool Due(float at, float judged) => judged >= at;

        // Circle second stage: every enemy of the cast's life that stands inside the radius now, once.
        // exceptId = an actor that is never hit (the primary's own target), -1 = nobody is exempt.
        public static int Circle(SpellActorSnap[] atCast, SpellActorSnap[] now, Vector3 centre, float radius, int exceptId,
            float at, float power, Element element, List<SpellHitOrder> orders)
        {
            if (atCast == null || now == null || orders == null || !(radius > 0f) || !(power > 0f)) return 0;
            int count = Mathf.Min(atCast.Length, now.Length), added = 0;
            for (int i = 0; i < count; i++)
            {
                if (i == exceptId || !SameLife(atCast[i], now[i])) continue;
                if (!AreaGeometry.InCircle(centre, now[i].Position, radius, out _)) continue;
                orders.Add(new SpellHitOrder(i, at, power, element));
                added++;
            }
            return added;
        }
    }

    // The clock second stages are judged with. The wiring ticks the effects first and applies its scheduled (primary) hits
    // after that, in the same frame. Judging a second stage with this frame's clock could therefore run before a primary
    // hit of the same frame; judging with the previous frame's clock cannot. Cost: a second stage lands one frame after
    // its nominal time.
    public sealed class TwoStageClock308
    {
        float _previous = float.NegativeInfinity;

        // Call once per frame with the frame's clock. Returns the clock to judge with (negative infinity on the first frame).
        public float Advance(float now)
        {
            float judged = _previous;
            _previous = now;
            return judged;
        }

        public void Reset() { _previous = float.NegativeInfinity; }
    }
}
