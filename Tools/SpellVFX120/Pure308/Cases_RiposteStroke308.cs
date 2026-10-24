using System;
using Oheangbu.App.World;
using UnityEngine;

namespace Pure308
{
    // #308 D308-148 step 5 (앞잡 = 일필휘지 한 획): the rule half of the one stroke. Runs the REAL pure classes of the game:
    //   RiposteStroke308Path (geometry, retarget, freeze), RiposteStroke308Timeline (reveal, look, dry, pose), RiposteStroke308Impact
    //   (the hit latch and the one damage), RiposteStroke308HitStop (the mended hit stop).
    // Fixtures (names of the brief): PathPiercing (RSP-PP), RevealImpactSync (RSP-RIS), SingleHitDamage (RSP-SHD, the latch half),
    // plus the clock, retarget and hit-stop rules they stand on (RSP-CLK, RSP-RET, RSP-HS).
    // What it cannot see (Riposte308Checks S1 - S5 in edit mode, the mp4 pass in Play): EnemyVitals' window and its 1.5 (SingleHitDamage,
    // WindowBTiming, WindowGeneration + LifeRevision), the real RibbonMeshBuilder mesh, the pool and the look volume, anything rendered.
    public static partial class Program
    {
        static Vector3 Pt(float x, float y, float z) => new Vector3(x, y, z);

        static partial void RunRiposteStroke308(Report r, Context c)
        {
            var shape = RiposteStroke308Shape.Default; var timing = RiposteStroke308Timing.Default;

            // ---------------------------------------------------------------- PathPiercing
            {
                var rng = new System.Random(308148);
                int cases = 0, bad = 0, badOrder = 0, badWidth = 0, tails = 0, badTail = 0; float worst = 0f, worstSide = 99f;
                var path = new RiposteStroke308Path();
                for (int k = 0; k < 400; k++)
                {
                    float F() => (float)rng.NextDouble();
                    var caster = Pt((F() - .5f) * 40f, F() * 3f, (F() - .5f) * 40f);
                    float dist = .5f + F() * 13f, ang = F() * 6.2832f;
                    var torso = caster + Pt(Mathf.Cos(ang) * dist, .6f + F() * 1.4f, Mathf.Sin(ang) * dist);
                    var eye = caster + Pt(0f, 1.5f + F() * .4f, 0f);
                    bool tip = k % 3 != 0;
                    var aim = new RiposteStroke308Aim { Caster = caster, Eye = eye, Torso = torso, TorsoRadius = .2f + F() * .6f, HasBrushTip = tip,
                        BrushTip = eye + Pt((F() - .3f) * .8f, -.2f - F() * .6f, .4f + F() * .8f) };
                    path.Build(shape, aim); cases++;
                    // the centre line passes the torso mark, entering and leaving the body, in order
                    float half = Mathf.Max(shape.TorsoHalf, aim.TorsoRadius);
                    float d = path.DistanceToCentreline(torso), e = Vector3.Distance(path.SampleWorld(path.EntryIndex), torso), x = Vector3.Distance(path.SampleWorld(path.ExitIndex), torso);
                    worst = Mathf.Max(worst, d); worstSide = Mathf.Min(worstSide, Mathf.Min(e, x) / half);
                    if (d > 1e-3f || Mathf.Abs(e - half) > 2e-3f || Mathf.Abs(x - half) > 2e-3f) bad++;
                    if (!(path.EntryU < path.ImpactU && path.ImpactU < path.ExitU && path.ExitU < 1f && path.Length > 1f && path.Count >= RiposteStroke308Path.MinSamples && path.Count <= RiposteStroke308Path.MaxSamples)) badOrder++;
                    for (int i = 1; i < path.Count; i++) if (!(path.Arc[i] > path.Arc[i - 1])) { badOrder++; break; }
                    // the mid sample IS the torso mark; the squeeze across the body is narrower than the wet head
                    float head = 0f; for (int i = 0; i < path.LeadCount; i++) head = Mathf.Max(head, path.Width[i]);
                    if (!(path.Width[path.MidIndex] > 0f && path.Width[path.MidIndex] < head && path.Width[path.Count - 1] < path.Width[path.MidIndex])) badWidth++;
                    // a stroke made with a usable brush tip ends on the line eye -> brush tip, never under the ground
                    if (path.TailOnBrush)
                    {
                        tails++;
                        Vector3 endW = path.SampleWorld(path.Count - 1);
                        if (endW.y < torso.y - shape.TailFloorDrop - 1e-2f) badTail++;
                    }
                }
                r.Check("RSP-PP-1", bad == 0 && worst < 1e-3f, "PathPiercing: in " + cases + " random casts the centre line passes the torso mark (worst " + worst.ToString("0.#####") + " m); the entry and exit samples sit one torso half away on both sides", bad + " bad");
                r.Check("RSP-PP-2", badOrder == 0, "PathPiercing: entry < mid < exit < 1, strictly growing arc length, 24..64 samples", badOrder + " bad");
                r.Check("RSP-PP-3", badWidth == 0, "PathPiercing: the wet head is wider than the squeeze across the body, which is wider than the dry tail's end", badWidth + " bad");
                // the typical right-handed cast: the brush tip low and to the right of the eye, the target 3 - 13 m away
                int usable = 0, typical = 0;
                for (int k = 0; k < 300; k++)
                {
                    float F() => (float)rng.NextDouble();
                    var caster = Pt(0f, 0f, 0f); float dist = 3f + F() * 10f, ang = F() * 6.2832f;
                    var dir = Pt(Mathf.Cos(ang), 0f, Mathf.Sin(ang)); var rightHand = Vector3.Cross(Vector3.up, dir);
                    var torso = caster + dir * dist + Pt(0f, 1.1f + F() * .3f, 0f); var eye = Pt(0f, 1.6f, 0f);
                    var aim = new RiposteStroke308Aim { Caster = caster, Eye = eye, Torso = torso, TorsoRadius = .4f, HasBrushTip = true,
                        BrushTip = eye + rightHand * (.1f + F() * .5f) + Vector3.down * (.15f + F() * .45f) + dir * (.5f + F() * .5f) };
                    path.Build(shape, aim); typical++; if (path.TailOnBrush) usable++;
                }
                r.Check("RSP-PP-6", usable >= typical * .9f, "PathPiercing: with the brush low and to the right of the eye the tail runs to the brush tip's projection (" + usable + " of " + typical + ")", (typical - usable) + " fell back to the default hook");
                r.Check("RSP-PP-4", tails > 10 && badTail == 0,"PathPiercing: a tail that runs to the brush tip's projection never dives below the floor (" + tails + " such tails)", badTail + " below");
                r.Fact("rsp_pp_worst_m", worst);
                // determinism and the degenerate caster (stands on the torso mark's vertical)
                var a = new RiposteStroke308Path(); var b = new RiposteStroke308Path();
                var aim0 = new RiposteStroke308Aim { Caster = Pt(1, 0, 1), Eye = Pt(1, 1.6f, 1), Torso = Pt(1, 1.2f, 1), TorsoRadius = .4f };
                a.Build(shape, aim0); b.Build(shape, aim0);
                bool same = a.Count == b.Count; for (int i = 0; same && i < a.Count; i++) same = a.Pos[i] == b.Pos[i] && a.Width[i] == b.Width[i];
                bool finite = true; for (int i = 0; i < a.Count; i++) finite &= !float.IsNaN(a.Pos[i].x + a.Pos[i].y + a.Pos[i].z);
                r.Check("RSP-PP-5", same && finite && a.DistanceToCentreline(aim0.Torso) < 1e-3f, "PathPiercing: the same fire builds the same stroke; a caster on the target's vertical still builds a finite stroke through it");
            }

            // ---------------------------------------------------------------- Retarget (the written part stays, the rest follows)
            {
                var aim = new RiposteStroke308Aim { Caster = Pt(0, 0, 0), Eye = Pt(0, 1.6f, 0), Torso = Pt(0, 1.2f, 6), TorsoRadius = .4f, HasBrushTip = true, BrushTip = Pt(.3f, 1.1f, .8f) };
                var p = new RiposteStroke308Path(); p.Build(shape, aim);
                Vector3 s0 = p.SampleWorld(0), sEarly = p.SampleWorld(3), mid0 = p.MidWorld, endWorld = p.SampleWorld(p.Count - 1);
                float headU = p.U(5);
                p.Retarget(aim.Torso, headU);                                    // freezes the samples the head has reached
                var moved = aim.Torso + Pt(.5f, -.1f, .3f);
                p.Retarget(moved, headU);
                bool startStays = Vector3.Distance(p.SampleWorld(0), s0) < 1e-4f && Vector3.Distance(p.SampleWorld(3), sEarly) < 1e-4f;
                bool midFollows = Vector3.Distance(p.MidWorld, moved) < 1e-3f && p.DistanceToCentreline(moved) < 1e-3f;
                bool endStays = Vector3.Distance(p.SampleWorld(p.Count - 1), endWorld) < 1e-3f;   // the brush end is not dragged along
                r.Check("RSP-RET-1", startStays && midFollows && endStays && p.IsFrozen(0) && !p.IsFrozen(p.MidIndex),
                    "Retarget: samples the head has written stay in the world, the unwritten ones follow the moved target (mid exactly on it), the brush end stays put");
                p.Freeze();
                var later = moved + Pt(2, 0, 0); p.Retarget(later, 1f);
                r.Check("RSP-RET-2", Vector3.Distance(p.MidWorld, moved) < 1e-3f, "Retarget: after the pierce (Freeze) nothing moves any more - the stroke never stretches after a staggering target");
            }

            // ---------------------------------------------------------------- RevealImpactSync (+ SingleHitDamage latch half)
            {
                float[][] patterns =
                {
                    new[] { 1f / 60f }, new[] { 1f / 30f }, new[] { 1f / 20f }, new[] { .2f }, new[] { 5f }, new[] { 1f / 144f },
                    new[] { 1f / 60f, 1f / 60f, .5f, 1f / 60f }, new[] { .01f, .3f, .002f, .08f },
                };
                var aim = new RiposteStroke308Aim { Caster = Pt(0, 0, 0), Eye = Pt(0, 1.6f, 0), Torso = Pt(0, 1.2f, 6), TorsoRadius = .4f };
                int badFires = 0, early = 0, late = 0, badDamage = 0, runs = 0;
                for (int pi = 0; pi < patterns.Length; pi++)
                    for (int mover = 0; mover < 2; mover++)
                    {
                        var p = new RiposteStroke308Path(); p.Build(shape, aim);
                        var hit = new RiposteStroke308Impact();
                        float t = 0f, prevU = 0f, firedT = -1f, damage = 0f; int fires = 0, frame = 0; bool beforeWasBelow = true;
                        Vector3 torso = aim.Torso;
                        runs++;
                        while (t < 2f)
                        {
                            float dt = patterns[pi][frame % patterns[pi].Length]; frame++; t += dt;
                            float u = RiposteStroke308Timeline.Reveal(timing, t);
                            if (mover == 1) torso += Pt(.004f, 0, .002f);          // a target drifting while it is written
                            if (!hit.Fired)
                            {
                                p.Retarget(torso, prevU);
                                if (hit.Advance(u, p.ImpactU)) { fires++; firedT = t; p.Freeze(); beforeWasBelow = prevU < p.ImpactU; damage += hit.TakeDamage(45f); }
                            }
                            else { if (hit.Advance(u, p.ImpactU)) fires++; damage += hit.TakeDamage(45f); }
                            prevU = u;
                        }
                        float predicted = RiposteStroke308Timeline.TimeAtReveal(timing, p.ImpactU);
                        float lastDt = 0f; { float tt = 0f; int f2 = 0; while (tt < firedT - 1e-4f) { lastDt = patterns[pi][f2 % patterns[pi].Length]; f2++; tt += lastDt; } }
                        if (fires != 1) badFires++;
                        if (firedT < predicted - 1e-3f - (mover == 1 ? .02f : 0f) || !beforeWasBelow) early++;
                        if (firedT > predicted + lastDt + 1e-3f + (mover == 1 ? .02f : 0f)) late++;
                        if (Mathf.Abs(damage - 45f) > 1e-4f) badDamage++;
                    }
                r.Check("RSP-RIS-1", badFires == 0, "RevealImpactSync: over " + runs + " runs (60/30/20/5/144 fps, a 5 s frame, stutters, a drifting target) the head reaches the torso mark exactly once", badFires + " runs with another count");
                r.Check("RSP-RIS-2", early == 0 && late == 0, "RevealImpactSync: the hit comes in the first frame the head is at the mark - never before the head got there, never a frame later", early + " early, " + late + " late");
                r.Check("RSP-SHD-1", badDamage == 0, "SingleHitDamage (latch half): the stroke hands out the damage of ONE hit, 45, once - however many frames it takes", badDamage + " runs off");
                var h = new RiposteStroke308Impact();
                r.Check("RSP-SHD-2", h.TakeDamage(45f) == 0f && !h.Fired && h.Advance(0f, .5f) == false && h.Advance(.6f, .5f) && !h.Advance(.9f, .5f) && h.TakeDamage(45f) == 45f && h.TakeDamage(45f) == 0f && Mathf.Approximately(h.FiredAtU, .6f),
                    "SingleHitDamage (latch half): no damage before the pierce, the first frame past the mark fires, a second never fires, the amount is 45 then 0");
                h.Reset();
                r.Check("RSP-SHD-3", !h.Fired && h.Advance(1f, .7f) && h.TakeDamage(45f) == 45f, "SingleHitDamage (latch half): a reset latch (the next fire) hits once again");
            }

            // ---------------------------------------------------------------- the clock
            {
                float prev = -1f; bool mono = true, endpoints = Mathf.Approximately(RiposteStroke308Timeline.Reveal(timing, 0f), 0f) && Mathf.Approximately(RiposteStroke308Timeline.Reveal(timing, timing.PrepSeconds), 0f)
                    && Mathf.Approximately(RiposteStroke308Timeline.Reveal(timing, timing.PrepSeconds + timing.RevealSeconds), 1f) && Mathf.Approximately(RiposteStroke308Timeline.Reveal(timing, 9f), 1f);
                for (float t = 0f; t < .4f; t += .005f) { float u = RiposteStroke308Timeline.Reveal(timing, t); mono &= u >= prev - 1e-6f; prev = u; }
                bool inverse = true; for (float u = .05f; u < 1f; u += .05f) inverse &= Mathf.Abs(RiposteStroke308Timeline.Reveal(timing, RiposteStroke308Timeline.TimeAtReveal(timing, u)) - u) < 1e-3f;
                r.Check("RSP-CLK-1", mono && endpoints && inverse, "clock: nothing is written during the prep, the write is monotonic from .05 to .20 s, TimeAtReveal is its inverse");
                // the look: off before the rise, up to 1 at the pierce, held, back to 0 at the return
                float impact = .17f;
                bool look = RiposteStroke308Timeline.LookWeight(timing, impact - timing.LookRise - .01f, impact) == 0f
                    && RiposteStroke308Timeline.LookWeight(timing, impact - timing.LookRise * .5f, impact) > .3f && RiposteStroke308Timeline.LookWeight(timing, impact - timing.LookRise * .5f, impact) < .7f
                    && RiposteStroke308Timeline.LookWeight(timing, impact, impact) == 1f && RiposteStroke308Timeline.LookWeight(timing, impact + timing.LookHold * .9f, impact) == 1f
                    && RiposteStroke308Timeline.LookWeight(timing, impact + timing.LookReturn + .001f, impact) == 0f;
                float lp = 2f; bool falling = true; for (float t = impact + timing.LookHold; t < impact + timing.LookReturn; t += .01f) { float w = RiposteStroke308Timeline.LookWeight(timing, t, impact); falling &= w <= lp + 1e-6f && w >= 0f && w <= 1f; lp = w; }
                r.Check("RSP-CLK-2", look && falling, "clock: the colour drain rises over .04 s to the pierce, holds .08 s, eases back to nothing .45 s after it - a smooth curve between 0 and 1");
                float dp = -1f; bool dryOk = RiposteStroke308Timeline.Dry(timing, 0f) == 0f && RiposteStroke308Timeline.Dry(timing, timing.DryStart) == 0f && RiposteStroke308Timeline.Dry(timing, timing.FallEnd) == 1f
                    && Mathf.Abs(RiposteStroke308Timeline.Dry(timing, timing.CrackEnd) - .7f) < 1e-4f;
                for (float t = 0f; t < 1.4f; t += .01f) { float d = RiposteStroke308Timeline.Dry(timing, t); dryOk &= d >= dp - 1e-6f; dp = d; }
                r.Check("RSP-CLK-3", dryOk, "clock: drying is 0 until .60 s, .7 (cracks open) at .85 s, 1 (all fallen) at 1.20 s, monotonic");
                float pose0 = RiposteStroke308Timeline.Pose(0f, .17f, .2f, .15f, .3f), poseUp = RiposteStroke308Timeline.Pose(.2f, .17f, .2f, .15f, .3f), poseEnd = RiposteStroke308Timeline.Pose(1f, .17f, .2f, .15f, .3f);
                r.Check("RSP-CLK-4", pose0 == 0f && poseUp > .99f && poseEnd == 0f, "clock: the arm pose is up with the swing and down after the pierce");
                var seen = new System.Collections.Generic.HashSet<float>(); bool inRange = true;
                for (int i = 0; i < 64; i++) { float v = RiposteStroke308Timeline.Hash01(12345u, i); inRange &= v >= 0f && v < 1f; seen.Add(v); }
                r.Check("RSP-CLK-5", inRange && seen.Count > 56 && RiposteStroke308Timeline.Hash01(7u, 3) == RiposteStroke308Timeline.Hash01(7u, 3) && RiposteStroke308Timeline.Hash01(7u, 3) != RiposteStroke308Timeline.Hash01(8u, 3),
                    "clock: the flake scatter hash is in [0,1), varied and deterministic (no allocation)");
            }

            // ---------------------------------------------------------------- the hit stop
            {
                var s = new RiposteStroke308HitStop(); float set, restore;
                bool started = s.Begin(1f, .06f, .08f, out set) && Mathf.Approximately(set, .06f) && s.Running;
                bool noSecond = !s.Begin(.06f, .06f, .08f, out _);                                                 // not while one runs
                bool held = !s.Tick(.05f, .06f, out restore) && s.Running && s.Tick(.05f, .06f, out restore) && Mathf.Approximately(restore, 1f) && !s.Running;
                r.Check("RSP-HS-1", started && noSecond && held, "hit stop: starts at the stop scale, counts real seconds, writes back the saved scale (1)");
                var slow = new RiposteStroke308HitStop();
                slow.Begin(.4f, .06f, .08f, out set); bool backTo = slow.Tick(.1f, set, out restore) && Mathf.Approximately(restore, .4f);
                var slower = new RiposteStroke308HitStop(); bool noRaise = slower.Begin(.03f, .06f, .08f, out set) && Mathf.Approximately(set, .03f);
                r.Check("RSP-HS-2", backTo && noRaise, "hit stop: another system's slow motion (.4) is restored as it was - not Max(1, saved) - and a slower scale is never raised to the stop's");
                var menu = new RiposteStroke308HitStop(); menu.Begin(1f, .06f, .08f, out set);
                bool waits = !menu.Tick(1f, 0f, out _) && menu.Running && !menu.Tick(1f, 0f, out _) && menu.Left > .079f;
                bool resumes = !menu.Tick(.05f, .06f, out _) && menu.Tick(.05f, .06f, out restore) && Mathf.Approximately(restore, 1f);
                r.Check("RSP-HS-3", waits && resumes, "hit stop: a menu pause (scale 0) holds the countdown and the pause is never undone; when the stop's scale is back it finishes");
                var other = new RiposteStroke308HitStop(); other.Begin(1f, .06f, .08f, out set);
                bool lets = !other.Tick(.01f, .5f, out restore) && !other.Running && Mathf.Approximately(restore, .5f);
                var abort = new RiposteStroke308HitStop(); abort.Begin(1f, .06f, .08f, out set);
                bool abortMine = abort.Abort(.06f, out restore) && Mathf.Approximately(restore, 1f) && !abort.Running;
                var abort2 = new RiposteStroke308HitStop(); abort2.Begin(1f, .06f, .08f, out set);
                bool abortOther = !abort2.Abort(.7f, out restore) && Mathf.Approximately(restore, .7f) && !abort2.Running;
                bool noPausedStart = !new RiposteStroke308HitStop().Begin(0f, .06f, .08f, out _);
                r.Check("RSP-HS-4", lets && abortMine && abortOther && noPausedStart, "hit stop: when another system took the scale the stop lets go without writing; a disable restores only its own value; no stop from a paused game");
            }
        }
    }
}
