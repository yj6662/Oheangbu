// SPEC-SPELL-120-308 L2 cases of WP-14 (sword form, the five eo + siot glyphs): no spell while the form lasts, the strike
// (target, cadence, power), the counter strike and its small groggy, upkeep, intake stop and the end with the ink.
using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Pure308
{
    public static partial class Program
    {
        const string WP14HandlerId = "sword.form";

        // A plain model of InkPool (fill fraction 0..1, amounts divided by the capacity multiplier) driven the way the effect
        // drives it: an upkeep spend every tick, and every reported income handed back while the form lasts.
        sealed class WP14Pool
        {
            public float Value, Capacity = 1f;
            public bool Released;
            public void Tick(float drainPerSecond, float delta)
            {
                if (Released) return;
                Value = Mathf.Clamp01(Value - SwordFormRule308.DrainAmount(drainPerSecond, delta) / Capacity);
                Released = SwordFormRule308.Exhausted(Value);
            }
            // InkPool.Gain: clamps at full, reports what really came in (the Gained event)
            public void Gain(float amount)
            {
                float previous = Value;
                Value = Mathf.Clamp01(Value + Mathf.Max(0f, amount) / Capacity);
                float received = Value - previous;
                if (received > 0f && !Released) Value = SwordFormRule308.IntakeStop(Value, received);
            }
        }

        static partial void RunWP14(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W14", false, "WP-14 cases need the table"); return; }
            var rows = c.Build.Rows.Where(x => x.Handler == WP14HandlerId).ToArray();
            r.Check("W14", rows.Length == 5 && rows.All(x => x.Category == SpellCategory.Buff && x.Kind == SpellKind.Buff && x.Final == SpellFinal.Siot && x.Medial == Jamo.Eo),
                "five sword rows (eo + siot) carry the handler sword.form", rows.Length.ToString());
            r.Check("W14", rows.Select(x => x.Element).Distinct().Count() == 5, "one sword per element");
            string[] keys = { "sword.reach", "sword.arc", "sword.interval", "sword.drain", "groggy.hits", "groggy.steps", "release.manual", "draw.lock" };
            r.Check("W14", rows.All(x => keys.All(x.Has)), "every sword row carries the eight parameters");
            if (rows.Length != 5) return;
            string Common(SpellRow x) => x.BasePower + "|" + string.Join("|", keys.Select(k => x.F(k, -1f).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var open = new ModelGate { Registered = new HashSet<string> { WP14HandlerId }, Unlocked = new HashSet<SpellFinal> { SpellFinal.Siot } };
            var shut = new ModelGate { Registered = new HashSet<string> { WP14HandlerId } };
            var wood = rows.Single(x => x.Element == Element.Wood);
            float reach = wood.F("sword.reach", 0f), arc = wood.F("sword.arc", 0f), interval = wood.F("sword.interval", 0f), drain = wood.F("sword.drain", 0f);
            float lockScale = wood.F("draw.lock", 0f);
            int hits = (int)wood.F("groggy.hits", 0f), steps = (int)wood.F("groggy.steps", 0f);
            string c1 = wood.Letter + "-1", c2 = wood.Letter + "-2", c3 = wood.Letter + "-3", c4 = wood.Letter + "-4";

            // ---- clause 1: no drawing (no spell can be cast while the form lasts) ----
            r.Check(c1, SwordFormRule308.CostScale(true, lockScale) == lockScale && lockScale > 1f && SwordFormRule308.CostScale(false, lockScale) == 1f,
                "the cost hook answers the row's lock multiplier while the form lasts and 1 otherwise", lockScale.ToString());
            float[] costs = { .15f, .10f, .30f, .15f * .5f, .15f * 2f * .7f };       // spell, parry, summon, discounted spell, discounted summon
            float[] capacities = { 1f, 1.5f, 3f };
            int refused = 0, cases = 0, free = 0;
            foreach (float cost in costs)
                foreach (float capacity in capacities)
                {
                    cases++;
                    if (SwordFormRule308.SpendRefused(1f, cost, SwordFormRule308.CostScale(true, lockScale), capacity)) refused++;
                    if (!SwordFormRule308.SpendRefused(1f, cost, SwordFormRule308.CostScale(false, lockScale), capacity)) free++;
                }
            r.Check(c1, refused == cases, "with a full ink pool every spell price (spell, parry, summon, discounted, larger capacity) is refused in sword form", refused + " of " + cases);
            r.Check(c1, free == cases, "the same prices are paid normally outside the form (the lock touches nothing else)", free + " of " + cases);
            r.Check(c1, SwordFormRule308.CostScale(true, 0f) == 1f && SwordFormRule308.CostScale(true, 1f) == 1f, "draw.lock 0 or 1 switches the price lock off (for the day the draw mode itself is gated)");
            r.Check(c1, !SwordFormRule308.SpendRefused(1f, 0f, SwordFormRule308.CostScale(true, lockScale), 1f),
                "declared limit: a spell whose price is 0 cannot be refused by a price (no such row exists today)");

            // ---- clause 2: a click is a sword strike ----
            var status = SpellResolveCore308.Resolve(wood.Letter[0], .8f, .8f, wood, open, out SpellCast cast);
            r.Check(c2, status == SpellResolveStatus.Ok && cast.Kind == SpellKind.Buff && r.Near(cast.Power, wood.BasePower * .8f) && cast.Power > 0f,
                "the form resolves as a buff whose power is the strike power: row base power x brush", status + " power " + cast.Power);
            Vector3 origin = new Vector3(10f, 0f, 10f), forward = Vector3.forward;
            SpellActorSnap At(int id, float x, float z, bool alive = true) => new SpellActorSnap(id, origin + new Vector3(x, 0f, z), alive, 1, 1f, false);
            var near = new[] { At(0, 0f, reach * .9f), At(1, .3f, reach * .5f), At(2, 0f, reach * 1.2f), At(3, 0f, -1f), At(4, 0f, reach * .2f, false) };
            r.Check(c2, SwordFormRule308.SelectTarget(near, origin, forward, reach, arc, -1) == 1, "the strike takes the nearest living enemy inside the reach and the arc");
            r.Check(c2, SwordFormRule308.SelectTarget(near, origin, forward, reach, arc, 0) == 0, "the aimed enemy wins when it stands inside the reach and the arc");
            r.Check(c2, SwordFormRule308.SelectTarget(near, origin, forward, reach, arc, 2) == 1, "an aimed enemy out of reach is not struck: the nearest one inside is");
            r.Check(c2, SwordFormRule308.SelectTarget(new[] { At(0, 0f, reach * 1.01f), At(1, 0f, -1f), At(2, 0f, 1f, false) }, origin, forward, reach, arc, -1) == -1,
                "out of reach, behind the player, or dead: the strike cuts air");
            float side = Mathf.Tan((arc + 5f) * Mathf.Deg2Rad), inside = Mathf.Tan((arc - 5f) * Mathf.Deg2Rad);
            r.Check(c2, SwordFormRule308.SelectTarget(new[] { At(0, side, 1f) }, origin, forward, reach * 10f, arc, -1) == -1 &&
                SwordFormRule308.SelectTarget(new[] { At(0, inside, 1f) }, origin, forward, reach * 10f, arc, -1) == 0, "the arc is a half angle around the player's forward");
            r.Check(c2, SwordFormRule308.SelectTarget(new[] { new SpellActorSnap(0, origin + new Vector3(0f, 30f, reach * .5f), true, 1, 1f, false) }, origin, forward, reach, arc, -1) == 0,
                "horizontal judgement, like every area shape (height is not asked)");
            float next = SwordFormRule308.NextStrikeAt(5f, interval);
            r.Check(c2, SwordFormRule308.CanStrike(5f, 5f) && !SwordFormRule308.CanStrike(5f + interval * .5f, next) && SwordFormRule308.CanStrike(5f + interval, next) && interval > 0f,
                "one strike per interval: a second click inside the interval does nothing", interval.ToString());
            foreach (float fps in new[] { 30f, 60f, 120f })
            {
                // a click every frame for 10 seconds
                int strikes = 0; float nextAt = 0f;
                for (int frame = 0; frame < (int)(10f * fps); frame++)
                {
                    float now = frame / fps;
                    if (!SwordFormRule308.CanStrike(now, nextAt)) continue;
                    strikes++; nextAt = SwordFormRule308.NextStrikeAt(now, interval);
                }
                int most = (int)Math.Ceiling(10f / interval) + 1, least = (int)Math.Floor(10f / (interval + 1f / fps));
                r.Check(c2, strikes <= most && strikes >= least, "click spam at " + fps + " fps yields the interval's strike count", strikes + " strikes in 10 s");
            }

            // ---- clause 3: a counter strike adds a little groggy ----
            var elements = (Element[])Enum.GetValues(typeof(Element));
            foreach (var row in rows)
            {
                var countered = elements.Where(e => SwordFormRule308.IsCounter(row.Element, true, e)).ToArray();
                bool one = countered.Length == 1 && ElementRelations.Overcomes(row.Element, countered[0]) && countered[0] != row.Element;
                r.Check(c3, one, row.Element + " sword: exactly one enemy element is countered, the one it overcomes", string.Join(" ", countered));
                r.Check(c3, elements.All(e => !SwordFormRule308.IsCounter(row.Element, false, e)), row.Element + " sword: an enemy without an element is never countered");
            }
            r.Check(c3, SwordFormRule308.IsCounter(Element.Metal, true, Element.Wood) && SwordFormRule308.IsCounter(Element.Wood, true, Element.Earth) &&
                SwordFormRule308.IsCounter(Element.Water, true, Element.Fire) && !SwordFormRule308.IsCounter(Element.Wood, true, Element.Metal) &&
                !SwordFormRule308.IsCounter(Element.Wood, true, Element.Fire), "the relation is the parry table's: metal beats wood, wood beats earth, water beats fire; never the reverse or the generating pair");
            int progress = 0, gainedSteps = 0; var gains = new List<int>();
            for (int strike = 1; strike <= hits * 3; strike++)
            {
                int gain = SwordFormRule308.CounterGain(ref progress, hits, steps);
                gainedSteps += gain; if (gain > 0) gains.Add(strike);
            }
            r.Check(c3, gainedSteps == steps * 3 && gains.SequenceEqual(new[] { hits, hits * 2, hits * 3 }), "every " + hits + "th counter strike adds " + steps + " groggy step(s); the strikes between add none",
                string.Join(",", gains));
            var install = c.Build.Rows.FirstOrDefault(x => x.Category == SpellCategory.Install && x.HasHandler);
            float perStrike = steps / (float)hits;
            r.Check(c3, perStrike < 1f && (install == null || perStrike < install.F("detonate.groggy", 0f)),
                "small: one counter strike is worth less than one parry step, and less than a harmony detonation", perStrike.ToString());
            int idle = 7;
            r.Check(c3, SwordFormRule308.CounterGain(ref idle, hits, 0) == 0 && idle == 0, "groggy.steps 0 switches the gain off");
            int single = 0;
            r.Check(c3, SwordFormRule308.CounterGain(ref single, 0, 2) == 2, "a hits value below 1 is read as 1 (never a division by zero)");

            // ---- clause 4: the form ends when the ink is gone; ink never rises while it lasts ----
            foreach (float fps in new[] { 30f, 60f, 120f })
            {
                float start = .85f, expected = start / drain;
                var pool = new WP14Pool { Value = start };
                float clock = 0f, endedAt = -1f; bool rose = false;
                while (clock < expected * 2f && endedAt < 0f)
                {
                    clock += 1f / fps; float before = pool.Value;
                    pool.Tick(drain, 1f / fps);
                    if (pool.Value > before) rose = true;
                    if (pool.Released) endedAt = clock;
                }
                r.Check(c4, endedAt > 0f && Math.Abs(endedAt - expected) <= 2f / fps + expected * 1e-3f && !rose,
                    "at " + fps + " fps the form ends when the ink reaches 0 (start / drain seconds)", "ended " + endedAt + " expected " + expected);
                r.Check(c4, endedAt <= SwordFormRule308.LongestLife(start, 1f, drain) + 2f / fps + expected * 1e-3f,
                    "at " + fps + " fps the form never outlives the bound its presentation is given", SwordFormRule308.LongestLife(start, 1f, drain).ToString());
            }
            {
                // intake stop: a harvest chunk and a row of refunds in the middle of the form are handed back as they arrive
                var plain = new WP14Pool { Value = .6f }; var fed = new WP14Pool { Value = .6f };
                float dt = 1f / 60f, plainEnd = -1f, fedEnd = -1f, clock = 0f; bool above = false;
                for (int frame = 0; frame < 60 * 60 && (plainEnd < 0f || fedEnd < 0f); frame++)
                {
                    clock += dt;
                    float before = fed.Value;
                    if (frame == 120) fed.Gain(.25f);                           // one harvest chunk
                    if (frame > 300 && frame < 600) fed.Gain(.05f * dt);        // a small refund every frame
                    if (fed.Value > before + 1e-6f) above = true;
                    plain.Tick(drain, dt); fed.Tick(drain, dt);
                    if (plain.Released && plainEnd < 0f) plainEnd = clock;
                    if (fed.Released && fedEnd < 0f) fedEnd = clock;
                }
                r.Check(c4, plainEnd > 0f && Math.Abs(plainEnd - fedEnd) <= .05f && !above, "ink gained during the form is handed back: the form ends at the same time with or without income",
                    "plain " + plainEnd + " fed " + fedEnd);
                r.Check(c4, SwordFormRule308.IntakeStop(.8f, .3f) == .8f - .3f && SwordFormRule308.IntakeStop(.2f, .5f) == 0f && SwordFormRule308.IntakeStop(.4f, -1f) == .4f,
                    "the pool goes back exactly by what was received, never below empty, and a negative report changes nothing");
                var full = new WP14Pool { Value = .9f }; full.Gain(.5f);
                r.Check(c4, r.Near(full.Value, .9f), "income clamped at a full pool is handed back by the amount that really came in (nothing is lost)", full.Value.ToString());
            }
            {
                var pool = new WP14Pool { Value = .5f, Capacity = 2f };
                pool.Tick(drain, 1f);
                r.Check(c4, r.Near(pool.Value, .5f - drain / 2f), "the upkeep is an ink amount in base-capacity units, like every other ink cost (a larger pool drains more slowly)", pool.Value.ToString());
                var last = new WP14Pool { Value = drain * .5f };
                last.Tick(drain, 1f);
                r.Check(c4, last.Value == 0f && last.Released, "the tick that empties the pool ends the form");
            }
            r.Check(c4, SwordFormRule308.LongestLife(.5f, 2f, drain) == 2f * SwordFormRule308.LongestLife(.5f, 1f, drain) && SwordFormRule308.LongestLife(.5f, 1f, 0f) == 0f &&
                SwordFormRule308.LongestLife(0f, 1f, drain) == 0f, "the bound follows the pool's capacity; without upkeep or without ink there is none");
            r.Check(c4, SwordFormRule308.DrainAmount(drain, 0f) == 0f && SwordFormRule308.DrainAmount(-1f, 1f) == 0f && SwordFormRule308.DrainAmount(float.NaN, 1f) == 0f && drain > 0f,
                "the upkeep is never negative or non-finite; the row drains ink (TEST " + drain + " per second)");

            // ---- the other four swords: the common sword spec ----
            foreach (var row in rows)
            {
                if (row == wood) continue;
                string clause = row.Letter + "-1";
                r.Check(clause, Common(row) == Common(wood), "same strike power and the same eight numbers as the wood sword (the common sword spec)", Common(row));
                var rowStatus = SpellResolveCore308.Resolve(row.Letter[0], 1f, 1f, row, open, out SpellCast rowCast);
                r.Check(clause, rowStatus == SpellResolveStatus.Ok && rowCast.Kind == SpellKind.Buff && rowCast.Element == row.Element && rowCast.Power == row.BasePower,
                    "resolves as a sword of its initial's element", rowStatus + " " + rowCast.Element);
                r.Check(clause, SpellResolveCore308.Resolve(row.Letter[0], 1f, 1f, row, shut, out _) == SpellResolveStatus.Locked, "behind the siot final: Locked until that final is unlocked");
                r.Check(clause, elements.Count(e => SwordFormRule308.IsCounter(row.Element, true, e)) == 1, "counters exactly the element it overcomes");
            }
            r.Check(c1, SpellResolveCore308.Resolve(wood.Letter[0], 1f, 1f, wood, shut, out _) == SpellResolveStatus.Locked, "the wood sword too sits behind the siot final");
        }
    }
}
