// SPEC-SPELL-120-308 L2 cases of D308-127 debate 15 (TEST): the metal form (two-handed sword) strikes in a chain of three, the
// last one a heavier finisher followed by a rest; the fire form borrowed it until the bow (debate 16). The water form is the flail:
// three enemies in a wide cone, the ones after the first weaker, and every landed swing holds the ink upkeep a little.
// Clause ids are built from the rows' letters: no glyph literal in this file.
using System;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Pure308
{
    public static partial class Program
    {
        static partial void RunCombo127(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("C127", false, "debate 15 cases need the table"); return; }
            var rows = c.Build.Rows.Where(x => x.Handler == WP14HandlerId).ToArray();
            var sword = rows.FirstOrDefault(x => x.Element == Element.Metal);
            var fire = rows.FirstOrDefault(x => x.Element == Element.Fire);
            var flail = rows.FirstOrDefault(x => x.Element == Element.Water);
            if (sword == null || fire == null || flail == null) { r.Check("C127", false, "the metal, fire and water forms carry sword.form", rows.Length.ToString()); return; }
            string s7 = sword.Letter + "-7", s8 = sword.Letter + "-8", f7 = fire.Letter + "-7", w5 = flail.Letter + "-5", w6 = flail.Letter + "-6";

            // ---- the two-handed sword's chain ----
            int hits = Mathf.RoundToInt(sword.F("combo.hits", 0f));
            float interval = sword.F("sword.interval", 0f), rest = sword.F("combo.rest", 0f), window = sword.F("combo.window", 0f);
            float buffer = sword.F("combo.buffer", 0f), finish = sword.F("combo.finish", 1f);
            r.Check(s7, hits == 3 && r.Near(interval, .5f) && r.Near(rest, .9f) && r.Near(window, .7f) && r.Near(buffer, .12f) && r.Near(finish, 1.5f),
                "debate 15 numbers: three strikes .5 s apart, the finisher x1.5 and a .9 s rest, a .7 s window, a .12 s buffer",
                hits + " " + interval + " " + rest + " " + window + " " + buffer + " " + finish);
            // a click every frame: the steps run 1 2 3 1 2 3, at 0, .5, 1.0, 1.9, 2.4, 2.9
            {
                int step = 0; float next = 0f; var at = new System.Collections.Generic.List<float>(); var steps = new System.Collections.Generic.List<int>();
                for (int frame = 0; frame < 60 * 4; frame++)
                {
                    float now = frame / 60f;
                    if (!SwordFormRule308.CanStrike(now, next)) continue;
                    step = SwordFormRule308.ComboStep(step, now, next, hits, window);
                    next = SwordFormRule308.NextStrikeAt(now, SwordFormRule308.ComboGap(step, hits, interval, rest));
                    at.Add(now); steps.Add(step);
                }
                bool order = steps.Take(6).SequenceEqual(new[] { 1, 2, 3, 1, 2, 3 });
                float[] expect = { 0f, .5f, 1f, 1.9f, 2.4f, 2.9f };
                bool times = at.Count >= 6 && expect.Select((e, i) => Math.Abs(at[i] - e) <= 1f / 60f + 1e-4f).All(x => x);
                r.Check(s7, order && times, "held click at 60 fps: steps 1 2 3 1 2 3 at 0, .5, 1.0, 1.9, 2.4, 2.9 s (the finisher rests)",
                    string.Join(",", steps.Take(6)) + " @ " + string.Join(",", at.Take(6).Select(v => v.ToString("0.00"))));
            }
            // the window: measured from when the next strike was allowed; on the edge it holds, past it the chain starts over
            r.Check(s7, SwordFormRule308.ComboStep(1, 1.2f, .5f, hits, window) == 2 && SwordFormRule308.ComboStep(1, 1.2f + 1e-3f, .5f, hits, window) == 1 &&
                SwordFormRule308.ComboStep(2, .9f, .5f, hits, window) == 3, "a strike within the window after the allowed moment goes on (edge included); later starts over at 1");
            r.Check(s7, SwordFormRule308.ComboStep(3, 1.9f, 1.9f, hits, window) == 1 && SwordFormRule308.ComboStep(0, 5f, 0f, hits, window) == 1,
                "after the finisher, and for the first strike of a form, the chain starts at 1");
            r.Check(s7, SwordFormRule308.ComboStep(2, 1f, .5f, 0, window) == 0 && SwordFormRule308.ComboStep(2, 1f, .5f, 1, window) == 0 &&
                SwordFormRule308.ComboGap(0, 0, .6f, .9f) == .6f && SwordFormRule308.ComboShare(0, 0, 1.5f) == 1f,
                "a row without combo.hits (or with one) is the plain sword: no step, its interval, the whole strike");
            r.Check(s7, r.Near(SwordFormRule308.ComboShare(1, hits, finish), 1f) && r.Near(SwordFormRule308.ComboShare(2, hits, finish), 1f) &&
                r.Near(SwordFormRule308.ComboShare(3, hits, finish), finish) && r.Near(SwordFormRule308.ComboGap(3, hits, interval, rest), rest) &&
                r.Near(SwordFormRule308.ComboGap(2, hits, interval, rest), interval), "only the finisher is heavier and rests longer");
            // the buffer: a click inside it is kept; earlier is dropped; on or after the moment it is no buffer (it strikes)
            r.Check(s7, SwordFormRule308.Buffered(.5f - buffer, .5f, buffer) && SwordFormRule308.Buffered(.5f - .01f, .5f, buffer) &&
                !SwordFormRule308.Buffered(.5f - buffer - .01f, .5f, buffer) && !SwordFormRule308.Buffered(.5f, .5f, buffer) && !SwordFormRule308.Buffered(.45f, .5f, 0f),
                "a click up to combo.buffer before the next strike is kept for it; earlier, or with no buffer, it is dropped as before");
            // the groggy reading: counter strikes on one enemy reach the first step on the finisher (1.0 s), one per 1.9 s cycle
            {
                int progress = 0, gainAt = -1;
                for (int k = 1; k <= hits * 2; k++) if (SwordFormRule308.CounterGain(ref progress, Mathf.RoundToInt(sword.F("groggy.hits", 0f)), 1) > 0 && gainAt < 0) gainAt = k;
                float cycle = interval * (hits - 1) + rest;
                r.Check(s8, gainAt == hits && cycle >= 1.8f - 1e-3f && cycle <= 2.4f + 1e-3f,
                    "groggy.hits = the chain: the finisher of a chain of counter strikes raises the groggy; the cycle (" + cycle + " s) is inside D308-127's 1.8 - 2.4 s",
                    "gain on strike " + gainAt);
            }

            // ---- the fire form was the two-handed sword's stand-in until the bow (debate 16): it keeps none of the combo ----
            r.Check(f7, Mathf.RoundToInt(fire.F("combo.hits", 0f)) < 2 && fire.F("combo.finish", 1f) == 1f && fire.F("hold.per", 0f) == 0f && fire.F("shot.draw", 0f) > 0f,
                "the stand-in is gone: the fire form is the bow (Cases_Bow127), no combo and no hold");

            // ---- the flail ----
            float reach = flail.F("sword.reach", 0f), arc = flail.F("sword.arc", 0f), through = flail.F("strike.through", 1f);
            int count = Mathf.RoundToInt(flail.F("strike.count", 1f));
            float per = flail.F("hold.per", 0f), cap = flail.F("hold.cap", 0f), flailInterval = flail.F("sword.interval", 0f);
            r.Check(w5, r.Near(reach, 3f) && r.Near(arc, 65f) && count == 3 && r.Near(through, .6f) && r.Near(flailInterval, 1f) && flail.BasePower == 10f &&
                Mathf.RoundToInt(flail.F("groggy.hits", 0f)) == 4 && Mathf.RoundToInt(flail.F("combo.hits", 0f)) < 2,
                "debate 15 numbers: 3.0 m and 130 degrees in all (65 half), three enemies, the ones after the first x.6, power 10 every 1.0 s, groggy every 4th",
                reach + " " + arc + " " + count + " " + through + " " + flailInterval + " " + flail.BasePower);
            Vector3 origin = new Vector3(2f, 0f, -3f), forward = Vector3.forward;
            SpellActorSnap At(int id, float degrees, float distance, bool alive = true)
                => new SpellActorSnap(id, origin + new Vector3(Mathf.Sin(degrees * Mathf.Deg2Rad), 0f, Mathf.Cos(degrees * Mathf.Deg2Rad)) * distance, alive, 1, 1f, false);
            var ids = new int[8]; var distances = new float[8];
            int n = SwordFormRule308.SelectTargets(new[] { At(0, -60f, 2.5f), At(1, 0f, 2f), At(2, 60f, 2.8f), At(3, 0f, 1f) }, origin, forward, reach, arc, 0f, -1, count, ids, distances);
            r.Check(w5, n == 3 && ids[0] == 3 && ids[1] == 1 && ids[2] == 0, "four inside the cone: the three nearest are struck (the count is a cap)", n + ": " + string.Join(",", ids.Take(n)));
            n = SwordFormRule308.SelectTargets(new[] { At(0, -70f, 2f), At(1, 70f, 2f), At(2, 0f, reach * 1.01f), At(3, 180f, 1f), At(4, 0f, 1f, false) }, origin, forward, reach, arc, 0f, -1, count, ids, distances);
            r.Check(w5, n == 0, "outside 65 degrees each side, past 3.0 m, behind or dead: not struck", n.ToString());
            n = SwordFormRule308.SelectTargets(new[] { At(0, 10f, 1f), At(1, 50f, 2.9f), At(2, -20f, 1.5f), At(3, 0f, 2f) }, origin, forward, reach, arc, 0f, 1, count, ids, distances);
            r.Check(w5, n == 3 && ids[0] == 1 && ids[1] == 0 && ids[2] == 2, "the aimed enemy is struck first, then the two nearest", string.Join(",", ids.Take(n)));
            float shares = SwordFormRule308.TargetShare(0, 2f, through, 0f, 1f) + SwordFormRule308.TargetShare(1, 2f, through, 0f, 1f) + SwordFormRule308.TargetShare(2, 2f, through, 0f, 1f);
            r.Check(w5, r.Near(shares, 1f + 2f * through) && r.Near(flail.BasePower * shares / flailInterval, 22f),
                "three enemies take 1 + .6 + .6 of a strike: 22 power per second over the crowd, 10 on the first (parity)", shares.ToString());
            // the ink hold
            r.Check(w6, r.Near(per, .3f) && r.Near(cap, .9f) && cap < flailInterval, "debate 15 hold: .3 s per enemy landed, at most .9 s ahead (less than the interval: the ink still drains)");
            float h = float.NegativeInfinity;
            h = SwordFormRule308.HoldUntil(h, 10f, 1, per, cap);
            bool one = r.Near(h, 10.3f);
            h = SwordFormRule308.HoldUntil(h, 10.1f, 3, per, cap);
            bool three = r.Near(h, 11.0f);
            float kept = SwordFormRule308.HoldUntil(h, 10.2f, 1, per, cap);
            bool notShortened = r.Near(kept, 11.0f);
            bool none = SwordFormRule308.HoldUntil(5f, 9f, 0, per, cap) == 5f && SwordFormRule308.HoldUntil(5f, 9f, 3, 0f, cap) == 5f && SwordFormRule308.HoldUntil(5f, 9f, 3, per, 0f) == 5f;
            r.Check(w6, one && three && notShortened && none,
                "one enemy holds .3 s, three hold .9 s (the cap) from now; a later smaller swing never shortens it; no enemy landed (or no hold numbers) changes nothing",
                h + " " + kept);
            {
                // three enemies landed every swing for 30 s: the held share of time stays below 1 (the form still ends with the ink)
                float until = float.NegativeInfinity, held = 0f, dt = 1f / 60f;
                for (int frame = 0; frame < 60 * 30; frame++)
                {
                    float now = frame * dt;
                    if (frame % 60 == 0) until = SwordFormRule308.HoldUntil(until, now, 3, per, cap);
                    if (now < until) held += dt;
                }
                r.Check(w6, held / 30f <= cap / flailInterval + .02f && held / 30f >= cap / flailInterval - .02f,
                    "three enemies every swing hold the upkeep " + (cap / flailInterval * 100f).ToString("0") + " % of the time, never all of it", (held / 30f).ToString("0.000"));
            }
            float flailGroggy = flail.F("groggy.hits", 0f) * flailInterval;
            r.Check(w5, flailGroggy > 2.4f,
                "declared exception: the flail reaches a groggy step on one enemy in " + flailGroggy + " s, outside D308-127's 1.8 - 2.4 s (a crowd weapon: debate 15)");
        }
    }
}
