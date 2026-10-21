using System;
using Oheangbu.Combat;

namespace Pure308
{
    // #308 D308-125 적용 5 (debate decision, TEST): the ember patch a fire eruption leaves and the scorch it gives a player who
    // stands in it. Patches, the ward and HP are engine-side (CombatLoopWiring.Ember308); here only the per-frame rule.
    // Frames are 1/16 s apart so every time below is exact in float.
    public static partial class Program
    {
        sealed class EmberTally { public float Patch, Scorch; public int Started, Washed, Ended; }

        const float EmberFrame = .0625f;

        // frames calls, the first at from; returns the time of the frame after the last
        static float EmberRun(EmberExposure308 e, float from, int frames, bool inside, EmberExposure308.Numbers n, bool warded, EmberTally tally)
        {
            for (int k = 0; k < frames; k++)
            {
                var step = e.Advance(from + k * EmberFrame, inside, n, warded);
                tally.Patch += step.PatchDamage; tally.Scorch += step.ScorchDamage;
                if (step.Started) tally.Started++;
                if (step.Washed) tally.Washed++;
                if (step.Ended) tally.Ended++;
            }
            return from + frames * EmberFrame;
        }

        static partial void RunEmber125(Report r, Context c)
        {
            var n = new EmberExposure308.Numbers(.5f, 2f, .5f, .25f, 4f, 1.5f);
            var none = default(EmberExposure308.Numbers);

            {
                var e = new EmberExposure308(); var a = new EmberTally();
                float t = EmberRun(e, 10f, 49, true, n, false, a);   // 10.0 .. 13.0 in the patch
                r.Check("불씨-1", a.Patch == 12f && a.Started == 1 && e.Scorched(13f) && e.ScorchUntil == 17f,
                    "three seconds in a patch: six patch ticks (10.5 .. 13.0) and one scorch, renewed to the last frame in it",
                    "patch " + a.Patch + " started " + a.Started + " until " + e.ScorchUntil);
                r.Check("불씨-2", a.Scorch == 9f, "the scorch burns on the patch's clock from the tick it caught on (six ticks of 1.5)", "scorch " + a.Scorch);
                var b = new EmberTally();
                EmberRun(e, t, 71, false, none, false, b);   // 13.0625 .. 17.4375 outside
                r.Check("불씨-2", b.Patch == 0f && b.Scorch == 10.5f && b.Ended == 1 && !e.Scorched(17f) && float.IsNaN(e.NextTick),
                    "outside, the scorch keeps its own ticks (13.5 .. 16.5) and ends at 17.0 before a tick there; then the clock stops",
                    "patch " + b.Patch + " scorch " + b.Scorch + " ended " + b.Ended);
            }
            {
                var e = new EmberExposure308(); var a = new EmberTally();
                float t = EmberRun(e, 10f, 7, true, n, false, a);   // 10.0 .. 10.375: dwell .375
                EmberRun(e, t, 16, false, none, false, a);
                r.Check("불씨-1", a.Patch == 0f && a.Scorch == 0f && a.Started == 0 && float.IsNaN(e.NextTick),
                    "stepping through a patch in under half a second costs nothing: the first tick is one interval after entering, and it fell outside",
                    "patch " + a.Patch + " started " + a.Started);
            }
            {
                var e = new EmberExposure308(); var a = new EmberTally();
                float t = EmberRun(e, 10f, 5, true, n, false, a);   // dwell .25
                t = EmberRun(e, t, 3, false, none, false, a);       // .1875 out, inside the grace
                t = EmberRun(e, t, 4, true, n, false, a);           // the frame back in does not count
                bool before = a.Started == 0;
                EmberRun(e, t, 1, true, n, false, a);
                r.Check("불씨-3", before && a.Started == 1 && e.Scorched(10.75f),
                    "a step out shorter than the grace keeps the dwell; the time outside does not count (caught at 10.75)", "started " + a.Started);
                var e2 = new EmberExposure308(); var b = new EmberTally();
                t = EmberRun(e2, 10f, 5, true, n, false, b);
                t = EmberRun(e2, t, 6, false, none, false, b);      // .375 out: past the grace
                EmberRun(e2, t, 6, true, n, false, b);
                r.Check("불씨-3", b.Started == 0 && e2.Dwell < .5f, "a step out longer than the grace starts the dwell over", "dwell " + e2.Dwell);
            }
            {
                var e = new EmberExposure308(); var a = new EmberTally();
                float t = EmberRun(e, 10f, 17, true, n, false, a);  // scorched
                var w = new EmberTally();
                t = EmberRun(e, t, 1, false, none, true, w);        // into 운's ring
                r.Check("불씨-4", w.Washed == 1 && !e.Scorched(t) && e.Dwell == 0f, "the ward washes the scorch off at once", "washed " + w.Washed);
                var s = new EmberTally();
                t = EmberRun(e, t, 32, true, n, true, s);           // two seconds in the patch inside the ring
                r.Check("불씨-4", s.Started == 0 && s.Scorch == 0f && s.Patch > 0f && e.Dwell == 0f,
                    "inside the ward no scorch attaches, but the patch still burns (the ward does not put out ground)", "patch " + s.Patch + " started " + s.Started);
                var o = new EmberTally();
                t = EmberRun(e, t, 8, true, n, false, o);
                bool notYet = o.Started == 0;
                EmberRun(e, t, 1, true, n, false, o);
                r.Check("불씨-4", notYet && o.Started == 1, "when the ward goes, the dwell starts from zero", "started " + o.Started);
            }
            {
                var e = new EmberExposure308(); var a = new EmberTally();
                EmberRun(e, 10f, 9, true, n, false, a);             // the clock's first tick at 10.5
                var step = e.Advance(12f, true, n, false);
                r.Check("불씨-5", step.PatchDamage == 2f && e.NextTick == 12.5f,
                    "a late frame pays one tick, not the ones it missed, and the clock restarts from it", "patch " + step.PatchDamage + " next " + e.NextTick);
            }
            {
                var e = new EmberExposure308(); var a = new EmberTally();
                float t = EmberRun(e, 10f, 17, true, n, false, a);
                bool ended = e.EndScorch();
                r.Check("불씨-6", ended && !e.Scorched(t) && e.Dwell == 0f, "a lethal-guard save puts the scorch out and starts the dwell over", "dwell " + e.Dwell);
                var b = new EmberTally();
                t = EmberRun(e, t, 7, true, n, false, b);           // still standing in it: every frame counts again
                bool notYet = b.Started == 0;
                EmberRun(e, t, 1, true, n, false, b);
                r.Check("불씨-6", notYet && b.Started == 1, "a player still in the fire is caught again after another dwell", "started " + b.Started);
            }
            {
                var e = new EmberExposure308(); var a = new EmberTally();
                float t = EmberRun(e, 10f, 9, true, n, false, a);   // caught at 10.5 with scorch 1.5
                var hot = new EmberExposure308.Numbers(.5f, 6f, .5f, .25f, 4f, 5f);
                var b = new EmberTally();
                EmberRun(e, t, 8, true, hot, false, b);             // 10.5625 .. 11.0 in a hotter patch: one tick at 11.0
                r.Check("불씨-7", b.Patch == 6f && b.Scorch == 1.5f && b.Started == 0,
                    "a scorch keeps the damage it caught with; a second patch only renews it, and its own ground damage is its own", "patch " + b.Patch + " scorch " + b.Scorch);
            }
            {
                var e = new EmberExposure308(); var a = new EmberTally();
                var broken = new EmberExposure308.Numbers(0f, 2f, .5f, .25f, 4f, 1.5f);
                var nan = new EmberExposure308.Numbers(.5f, float.NaN, .5f, .25f, 4f, 1.5f);
                float t = EmberRun(e, 10f, 32, true, broken, false, a);
                EmberRun(e, t, 32, true, nan, false, a);
                r.Check("불씨-8", a.Patch == 0f && a.Scorch == 0f && a.Started == 0 && float.IsNaN(e.NextTick),
                    "broken numbers (a zero tick, a damage that is not a number) burn nothing", "patch " + a.Patch);
                var e2 = new EmberExposure308(); var b = new EmberTally();
                EmberRun(e2, 10f, 17, true, n, false, b);
                e2.Clear();
                r.Check("불씨-8", !e2.Scorched(11f) && float.IsNaN(e2.NextTick) && e2.Dwell == 0f, "Clear (death, rest) leaves nothing burning", "");
                var none2 = e2.Advance(float.NaN, true, n, false);
                r.Check("불씨-8", none2.PatchDamage == 0f && none2.ScorchDamage == 0f && !none2.Started, "a clock that is not a number does nothing", "");
            }
        }
    }
}
