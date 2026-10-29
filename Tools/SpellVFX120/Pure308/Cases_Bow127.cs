// SPEC-SPELL-120-308 L2 cases of D308-127 debate 16 (TEST): the fire form is the horn bow. Hold the click to draw for
// shot.draw seconds, release to shoot one arrow (earlier is a fizzle, no ink), sword.interval is the recovery after a shot.
// The arrow goes to the aimed enemy inside shot.range and lands after distance / speed; the bow deals 70 % of the melee
// forms' power per second (the price of range). Clause ids are built from the rows' letters: no glyph literal in this file.
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
        static partial void RunBow127(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("B127", false, "debate 16 cases need the table"); return; }
            var rows = c.Build.Rows.Where(x => x.Handler == WP14HandlerId).ToArray();
            var bow = rows.FirstOrDefault(x => x.Element == Element.Fire);
            var spear = rows.FirstOrDefault(x => x.Element == Element.Wood);
            if (bow == null || spear == null) { r.Check("B127", false, "the fire and wood forms carry sword.form", rows.Length.ToString()); return; }
            string f8 = bow.Letter + "-8", f9 = bow.Letter + "-9";

            float draw = bow.F("shot.draw", 0f), recover = bow.F("sword.interval", 0f), speed = bow.F("shot.speed", 0f), range = bow.F("shot.range", 0f);
            float ink = bow.F("shot.ink", 0f), move = bow.F("shot.move", 1f), drain = bow.F("sword.drain", 0f);
            r.Check(f8, bow.BasePower == 7f && r.Near(draw, .7f) && r.Near(recover, .3f) && r.Near(speed, 40f) && r.Near(range, 30f) && r.Near(ink, .02f) &&
                r.Near(move, .5f) && bow.F("groggy.steps", -1f) == 0f,
                "debate 16 numbers: power 7, a .7 s draw, .3 s recovery, 40 m/s, 30 m, .02 ink a shot, x.5 movement while drawn, no groggy",
                bow.BasePower + " " + draw + " " + recover + " " + speed + " " + range + " " + ink + " " + move);
            r.Check(f8, rows.Where(x => x != bow).All(x => x.F("shot.draw", 0f) == 0f && x.F("shot.ink", 0f) == 0f), "only the bow draws and pays per shot");
            r.Check(f8, Mathf.RoundToInt(bow.F("combo.hits", 0f)) < 2 && bow.F("hold.per", 0f) == 0f && bow.F("strike.count", 1f) == 1f,
                "the bow keeps none of the melee keys (combo, hold, several enemies)");
            float melee = CycleRate127(spear), ranged = bow.BasePower / Mathf.Max(1e-3f, draw + recover);
            r.Check(f8, r.Near(ranged, melee * .7f), "the price of range: one shot per " + (draw + recover) + " s = " + ranged + " power per second, 70 % of the melee forms' " + melee);

            // ---- the click: hold to draw, release to shoot ----
            // a frame loop at 60 fps; press(t) says whether the click is down at t
            List<(float at, BowStep308 step)> Run(Func<float, bool> press, float seconds)
            {
                var log = new List<(float, BowStep308)>();
                bool drawing = false, was = false; float start = 0f, ready = 0f;
                for (int frame = 0; frame <= Mathf.RoundToInt(seconds * 60f); frame++)
                {
                    float now = frame / 60f; bool down = press(now);
                    var step = SwordFormRule308.BowStep(drawing, now, start, draw, down, was && !down, ready);
                    was = down;
                    if (step == BowStep308.None) continue;
                    log.Add((now, step));
                    if (step == BowStep308.Begin) { drawing = true; start = now; }
                    else { drawing = false; if (step == BowStep308.Fire) ready = SwordFormRule308.NextStrikeAt(now, recover); }
                }
                return log;
            }
            var full = Run(t => t < 1f, 2f);
            r.Check(f8, full.Count == 2 && full[0].step == BowStep308.Begin && full[0].at == 0f && full[1].step == BowStep308.Fire && Math.Abs(full[1].at - 1f) <= 1f / 60f + 1e-4f,
                "held 1 s and released: the draw begins at once, one arrow on the release", string.Join(" ", full.Select(e => e.step + "@" + e.at.ToString("0.00"))));
            var tap = Run(t => t < .5f, 1f);
            r.Check(f8, tap.Count == 2 && tap[1].step == BowStep308.Fizzle, "released at .5 s (before the .7 s draw): a fizzle, no arrow", string.Join(" ", tap.Select(e => e.step.ToString())));
            var edge = Run(t => t < draw, 1.5f);
            r.Check(f8, edge.Count == 2 && edge[1].step == BowStep308.Fire, "released on the frame the draw completes: it fires", string.Join(" ", edge.Select(e => e.step + "@" + e.at.ToString("0.000"))));
            var wait = Run(t => t < 5f, 4.9f);
            r.Check(f8, wait.Count == 1 && wait[0].step == BowStep308.Begin, "held 4.9 s: still drawn, no automatic shot (a full draw waits for the release)");
            r.Check(f8, SwordFormRule308.BowStep(true, 2f, 1f, draw, false, false, 0f) == BowStep308.Cancel &&
                SwordFormRule308.BowStep(true, 1.2f, 1f, draw, false, false, 0f) == BowStep308.Cancel,
                "drawing, and the click is neither held nor released (a menu, a dodge, a jump, death shut the input): the draw is cancelled");
            // pressed again during the recovery: the draw waits for it, then begins on the held click
            var again = Run(t => t < 1f || (t >= 1.1f && t < 3f), 3.5f);
            int fireAt = again.FindIndex(e => e.step == BowStep308.Fire);
            bool held = fireAt >= 0 && again.Count > fireAt + 1 && again[fireAt + 1].step == BowStep308.Begin &&
                Math.Abs(again[fireAt + 1].at - (again[fireAt].at + recover)) <= 1f / 60f + 1e-4f;
            r.Check(f8, held, "a click held through the recovery begins the next draw when the recovery ends (" + recover + " s after the shot)",
                string.Join(" ", again.Select(e => e.step + "@" + e.at.ToString("0.00"))));
            // the fastest player: 10 s of draw-release at the limit = one shot a second
            {
                float cycle = draw + recover;
                var spam = Run(t => (t % cycle) < draw + 1f / 60f, 10f);
                int shots = spam.Count(e => e.step == BowStep308.Fire);
                r.Check(f8, shots >= 9 && shots <= 10, "draw and release at the limit for 10 s: about one shot a second", shots + " shots");
            }
            r.Check(f8, SwordFormRule308.Drawn01(1f, 1f, draw) == 0f && r.Near(SwordFormRule308.Drawn01(1.35f, 1f, draw), .5f) && SwordFormRule308.Drawn01(3f, 1f, draw) == 1f &&
                SwordFormRule308.Drawn01(1f, 1f, 0f) == 1f, "how far the bow is drawn runs 0 to 1 over the draw and stays full");
            r.Check(f8, r.Near(SwordFormRule308.MoveScale(true, move), .5f) && SwordFormRule308.MoveScale(false, move) == 1f && SwordFormRule308.MoveScale(true, 0f) == .05f &&
                SwordFormRule308.MoveScale(true, 3f) == 1f, "x.5 movement only while drawn; the scale never stops the player and never speeds them up");
            // the ink: the upkeep and one shot a second, from a full pool
            {
                float pool = 1f, t = 0f; int shots = 0;
                while (pool > 0f && t < 120f) { pool -= drain * (draw + recover); if (pool <= 0f) break; pool -= ink; shots++; t += draw + recover; }
                r.Check(f8, shots >= 14 && shots <= 18, "a full pool lasts about " + t.ToString("0") + " s and " + shots + " shots at one shot a second (upkeep .04/s + .02 a shot)");
            }

            // ---- the arrow ----
            Vector3 origin = new Vector3(1f, 1.4f, -2f);
            SpellActorSnap At(int id, float distance, bool alive = true) => new SpellActorSnap(id, origin + new Vector3(.3f, 0f, 1f).normalized * distance, alive, 1, 1f, false);
            var crowd = new[] { At(0, 5f), At(1, 20f), At(2, range + .5f), At(3, 10f, false) };
            r.Check(f9, SwordFormRule308.ShotTarget(crowd, origin, range, 1) == 1 && SwordFormRule308.ShotTarget(crowd, origin, range, 2) == -1 &&
                SwordFormRule308.ShotTarget(crowd, origin, range, 3) == -1 && SwordFormRule308.ShotTarget(crowd, origin, range, -1) == -1 &&
                SwordFormRule308.ShotTarget(crowd, origin, range, 9) == -1,
                "the arrow goes to the aimed enemy inside 30 m only: beyond it, dead, nobody aimed or not in the list = no target (the nearer enemy is not taken instead)");
            r.Check(f9, SwordFormRule308.ShotTarget(new[] { At(0, range) }, origin, range, 0) == 0, "30 m exactly is inside");
            r.Check(f9, r.Near(SwordFormRule308.FlightTime(range, speed, 1f), .75f) && r.Near(SwordFormRule308.FlightTime(15f, speed, 1f), .375f) &&
                r.Near(SwordFormRule308.FlightTime(range, speed, 1.5f), .5f) && SwordFormRule308.FlightTime(10f, 0f, 1f) == 0f && SwordFormRule308.FlightTime(-3f, speed, 1f) == 0f,
                "30 m flies .75 s at 40 m/s (15 m: .375 s); the haste of the moment shortens it; no speed lands at once");
        }
    }
}
