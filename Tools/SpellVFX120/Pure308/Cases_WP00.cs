// SPEC-SPELL-120-308 L2 cases of WP-00 (defects of glyphs that already resolve): summon power, boss bind, lift and spike numbers as data.
using System;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Pure308
{
    public static partial class Program
    {
        static partial void RunWP00(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W00", false, "WP-00 cases need the table"); return; }

            // ---- the five summons: a resolved cast carries power 0 (book), the profile supplies it ----
            var summons = c.Build.Rows.Where(x => x.Category == SpellCategory.Summon).ToArray();
            r.Check("W00", summons.Length == 5, "five summon rows");
            var gate = new LegacySpellGate(false, false, false, false, false);
            foreach (var row in summons)
            {
                string clause = row.Letter + "-1";
                var status = SpellResolveCore308.Resolve(row.Letter[0], .37f, .2f, row, gate, out SpellCast cast);
                r.Check(clause, status == SpellResolveStatus.Ok && cast.Kind == SpellKind.Summon && cast.Power == 0f && cast.Brush == .37f,
                    "a drawn summon resolves with book power 0 and carries its brush multiplier", status + " power " + cast.Power);
                float before = cast.Power;                                                    // what the manager used before: 0
                float after = SummonPowerRule308.CastPower(cast.Power, 10f, cast.Brush, cast.HoldScale);
                r.Check(clause, before == 0f && r.Near(after, 3.7f), "profile base power x brush x hold gives the summon a positive power", "before " + before + " after " + after);
            }
            r.Check("W00", SummonPowerRule308.CastPower(10f, 10f, .5f, 1f) == 10f, "a caller that hands in a positive power is used as is (existing checks unchanged)");
            r.Check("W00", SummonPowerRule308.CastPower(0f, 10f, 1f, .5f) == 5f, "hold decay applies to the profile power like to any cast");
            r.Check("W00", SummonPowerRule308.CastPower(0f, 0f, 1f, 1f) == 0f, "a profile without base power still yields 0 (declared, not hidden)");
            r.Check("W00", float.IsNaN(SummonPowerRule308.CastPower(0f, 10f, float.NaN, 1f)), "a non-finite brush stays non-finite (the manager rejects it)");

            // ---- wood giyeok glyph: a boss is slowed, not bound ----
            RootBindRule308.Resolve(true, .45f, out float bossSpeed, out bool bossBlocked);
            RootBindRule308.Resolve(false, .45f, out float speed, out bool blocked);
            r.Check("각-2", bossSpeed == .45f && !bossBlocked, "IsBoss target: movement scaled, actions not blocked");
            r.Check("각-2", speed == 0f && blocked, "ordinary target: movement 0, actions blocked");

            // ---- wood field glyph: lift numbers come from the row ----
            var lift = c.Build.Rows.Single(x => x.Feature == SpellLegacyFeature.Guk);
            var legacyLift = new FieldLiftSpec(2.4f, 1.2f, .9f, 20f);                           // the constants the row replaces
            var fromRow = FieldLiftSpec.From(lift, legacyLift);
            r.Check("국-1", fromRow.Height == legacyLift.Height && fromRow.RiseSpeed == legacyLift.RiseSpeed && fromRow.DescentSpeed == legacyLift.DescentSpeed &&
                fromRow.HoldSeconds == legacyLift.HoldSeconds, "lift numbers read from the row equal the constants they replace",
                fromRow.Height + " " + fromRow.RiseSpeed + " " + fromRow.DescentSpeed + " " + fromRow.HoldSeconds);
            var other = FieldLiftSpec.From(lift, new FieldLiftSpec(9f, 9f, 9f, 9f));
            r.Check("국-1", other.Height == 2.4f && other.RiseSpeed == 1.2f && other.DescentSpeed == .9f && other.HoldSeconds == 20f, "the numbers really come from the row, not from the fallback");
            r.Check("국-1", lift.Has("lift.height") && lift.Has("lift.rise") && lift.Has("lift.descent") && lift.Has("lift.hold"), "the row carries all four lift keys");
            var none = FieldLiftSpec.From(null, legacyLift);
            r.Check("국-1", none.Height == 2.4f && none.HoldSeconds == 20f, "a cast without row data keeps the previous numbers");
            var broken = new SpellRow { Params = new[] { new SpellParam { Key = "lift.height", Value = -1f } } };
            r.Check("국-1", FieldLiftSpec.From(broken, legacyLift).Height == 2.4f, "an invalid row value falls back instead of building a broken lift");

            // ---- wood circle glyph: spike numbers come from the row, the plan equals the pre-#308 plan ----
            var circle = c.Build.Rows.Single(x => x.F(SpellGrammar308.SpikesKey, 0f) > 0f);
            var spec = AreaSpikeSpec.From(circle); var legacy = AreaSpikeSpec.Legacy;
            r.Check("고-2", spec.Count == legacy.Count && spec.Fill == legacy.Fill && spec.Window == legacy.Window && spec.Gap == legacy.Gap && spec.Inner == legacy.Inner,
                "spike numbers read from the row equal the constants they replace", spec.Count + " " + spec.Fill + " " + spec.Window + " " + spec.Gap + " " + spec.Inner);
            r.Check("고-2", circle.Has("spike.count") && circle.Has("spike.fill") && circle.Has("spike.window") && circle.Has("spike.gap") && circle.Has("spike.inner"),
                "the row carries all five spike keys");
            int different = 0, staggered = 0;
            var center = new Vector3(3.5f, 0f, -2.25f); float radius = circle.AreaRadius, delay = circle.AreaImpactDelay;
            for (int seed = 1; seed <= 200; seed++)
            {
                var points = new Vector3[spec.Count]; var rise = new float[spec.Count];
                AreaSpikeRule308.Plan(seed, spec, center, radius, delay, points, rise);
                OldSpikes(seed, center, radius, delay, out Vector3[] oldPoints, out float[] oldRise);
                for (int i = 0; i < spec.Count; i++)
                    if (!points[i].x.Equals(oldPoints[i].x) || !points[i].y.Equals(oldPoints[i].y) || !points[i].z.Equals(oldPoints[i].z) || !rise[i].Equals(oldRise[i])) different++;
                if (rise.Distinct().Count() == spec.Count && rise.Min() == delay && rise.Max() <= delay + spec.Window + 1e-4f) staggered++;
            }
            r.Check("고-2", different == 0, "200 seeds: positions and rise times are bit-identical to the pre-#308 planner", different + " different values");
            r.Check("고-2", staggered == 200, "every spike has its own rise time inside the window (scattered rise)");
            r.Check("고-2", radius == 2.5f && delay == .3f, "radius and delay of the row equal the book entry");
        }

        // AreaSpikePlanner.Fill as it was before #308 (constants in code), on plain arrays.
        static void OldSpikes(int seed, Vector3 point, float planRadius, float planDelay, out Vector3[] points, out float[] riseAt)
        {
            points = new Vector3[17]; riseAt = new float[17];
            var random = new System.Random(seed); int[] order = new int[17]; float[] gaps = new float[16]; float sum = 0;
            for (int i = 0; i < 17; i++) order[i] = i;
            for (int i = 16; i > 0; i--) { int j = random.Next(i + 1); int t = order[i]; order[i] = order[j]; order[j] = t; }
            for (int i = 0; i < 16; i++) { gaps[i] = .35f + (float)random.NextDouble(); sum += gaps[i]; }
            for (int i = 0; i < 17; i++)
            {
                float a = i * 2.39996323f; float radius = Mathf.Sqrt((i + .35f) / 17f) * planRadius * .82f;
                points[i] = point + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
            }
            float time = planDelay;
            for (int rank = 0; rank < 17; rank++) { riseAt[order[rank]] = time; if (rank < 16) time += .6f * gaps[rank] / sum; }
        }
    }
}
