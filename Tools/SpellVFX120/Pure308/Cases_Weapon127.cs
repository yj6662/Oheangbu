// SPEC-SPELL-120-308 L2 cases of D308-127 step 1 (TEST): the wood form is a spear (a line through two enemies, weaker behind
// and weaker too close), the earth form an axe (slow, heavy, one enemy, a landed strike lowers its defence). The other three
// keep the common sword numbers (Cases_WP14). Clause ids are built from the rows' letters: no glyph literal in this file.
using System;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Pure308
{
    public static partial class Program
    {
        static partial void RunWeapon127(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W127", false, "D308-127 cases need the table"); return; }
            var rows = c.Build.Rows.Where(x => x.Handler == WP14HandlerId).ToArray();
            var spear = rows.FirstOrDefault(x => x.Element == Element.Wood);
            var axe = rows.FirstOrDefault(x => x.Element == Element.Earth);
            var sword = rows.FirstOrDefault(x => x.Element == Element.Metal);
            if (spear == null || axe == null || sword == null) { r.Check("W127", false, "the wood, earth and metal forms carry sword.form", rows.Length.ToString()); return; }
            string s2 = spear.Letter + "-2", s3 = spear.Letter + "-3", s5 = spear.Letter + "-5", s6 = spear.Letter + "-6";
            string a1 = axe.Letter + "-1", a2 = axe.Letter + "-2";

            float reach = spear.F("sword.reach", 0f), width = spear.F("strike.width", 0f), through = spear.F("strike.through", 1f);
            float near = spear.F("strike.near", 0f), inner = spear.F("strike.inner", 1f), aim = spear.F("strike.aim", 0f);
            int count = Mathf.RoundToInt(spear.F("strike.count", 1f));
            float swordReach = sword.F("sword.reach", 0f), swordArc = sword.F("sword.arc", 0f), swordInterval = sword.F("sword.interval", 0f);
            Vector3 origin = new Vector3(-4f, 0f, 7f), forward = Vector3.forward;
            SpellActorSnap At(int id, float x, float z, bool alive = true) => new SpellActorSnap(id, origin + new Vector3(x, 0f, z), alive, 1, 1f, false);
            var ids = new int[8]; var distances = new float[8];
            int Line(SpellActorSnap[] actors, Vector3 direction, int aimed)
                => SwordFormRule308.SelectTargets(actors, origin, direction, reach, 0f, width, aimed, count, ids, distances);

            // ---- spear: the line through two enemies ----
            r.Check(s5, width > 0f && count == 2 && through > 0f && through < 1f && aim > 0f && reach > swordReach,
                "the spear row is a line (strike.width = half width), two enemies per strike, the one behind weaker, an aim correction and a longer reach than the sword",
                "width " + width + " count " + count + " through " + through + " aim " + aim + " reach " + reach);
            float d1 = reach * .7f, d2 = reach * .95f;   // both inside the line (the 3.0 / 4.0 m film check needs the lunge, step 3)
            int n = Line(new[] { At(0, 0f, d2), At(1, 0f, d1) }, forward, -1);
            r.Check(s5, n == 2 && ids[0] == 1 && ids[1] == 0 && r.Near(distances[0], d1) && r.Near(distances[1], d2),
                "two enemies on the line are both struck, the nearer first", n + ": " + ids[0] + "," + ids[1]);
            r.Check(s5, r.Near(SwordFormRule308.TargetShare(0, d1, through, near, inner), 1f) && r.Near(SwordFormRule308.TargetShare(1, d2, through, near, inner), through),
                "the first enemy takes the whole strike, the one behind it strike.through of it (film check (3): x0.6)");
            n = Line(new[] { At(0, 0f, d2), At(1, 0f, d1), At(2, 0f, (d1 + d2) * .5f) }, forward, -1);
            r.Check(s5, n == count && ids[0] == 1 && ids[1] == 2, "three on the line: the two nearest are struck (the count is a cap)", n.ToString());
            n = Line(new[] { At(0, width + .1f, d1), At(1, -(width + .1f), d1), At(2, 0f, reach * 1.01f), At(3, 0f, -1f), At(4, 0f, d1, false) }, forward, -1);
            r.Check(s5, n == 0, "beside the line (half width + 0.1 m), past the reach, behind the player or dead: not struck", n.ToString());
            r.Check(s5, width < .5f && Line(new[] { At(0, .5f, d1) }, forward, -1) == 0, "film check (3): an enemy standing 0.5 m to the side takes nothing");
            n = Line(new[] { At(0, 0f, 3f), At(1, 0f, 4f) }, forward, -1);
            r.Check(s5, reach < 4f && n == 1 && ids[0] == 0,
                "declared limit: without the first-strike lunge (D308-127 step 3) an enemy 4.0 m away is outside the line; the film check at 3.0 / 4.0 m waits for the lunge",
                "reach " + reach);
            // the line in general: sorted along the line, inside the corridor, never more than the cap
            var rng = new System.Random(127);
            int lineOk = 0, lineCases = 0;
            for (int k = 0; k < 3000; k++)
            {
                var actors = new SpellActorSnap[1 + rng.Next(7)];
                for (int i = 0; i < actors.Length; i++)
                    actors[i] = new SpellActorSnap(i, origin + new Vector3((float)(rng.NextDouble() * 2 - 1), 0f, (float)(rng.NextDouble() * 6 - 1)), rng.NextDouble() > .15, 1, 1f, false);
                n = Line(actors, forward, -1);
                bool ok = n <= count;
                for (int i = 0; i < n && ok; i++)
                {
                    var a = actors[ids[i]];
                    ok = a.Alive && AreaGeometry.InCorridor(origin, forward, a.Position, width, reach, out float along, out _) && r.Near(along, distances[i]) && (i == 0 || distances[i - 1] <= distances[i]);
                }
                int inside = actors.Count(a => a.Alive && AreaGeometry.InCorridor(origin, forward, a.Position, width, reach, out _, out _));
                lineCases++; if (ok && n == Math.Min(inside, count)) lineOk++;
            }
            r.Check(s5, lineOk == lineCases, "random crowds: the line takes min(inside, cap) living enemies inside the corridor, nearest along it first", lineOk + " of " + lineCases);
            // the aim correction
            float inAim = (aim - 2f) * Mathf.Deg2Rad, outAim = (aim + 8f) * Mathf.Deg2Rad;
            var aimedIn = At(0, Mathf.Sin(inAim) * d1, Mathf.Cos(inAim) * d1);
            var aimedOut = At(0, Mathf.Sin(outAim) * d1, Mathf.Cos(outAim) * d1);
            var bent = SwordFormRule308.StrikeDirection(new[] { aimedIn }, origin, forward, reach, aim, 0);
            var kept = SwordFormRule308.StrikeDirection(new[] { aimedOut }, origin, forward, reach, aim, 0);
            r.Check(s5, Vector3.Angle(bent, aimedIn.Position - origin) < .01f && Line(new[] { aimedIn }, bent, 0) == 1,
                "an aimed enemy within strike.aim degrees of forward bends the line to it and is struck");
            r.Check(s5, kept == forward && Line(new[] { aimedOut }, kept, 0) == 0, "an aimed enemy past strike.aim keeps the line straight ahead (no free turn)");
            r.Check(s5, SwordFormRule308.StrikeDirection(new[] { aimedIn }, origin, forward, reach, 0f, 0) == forward &&
                SwordFormRule308.StrikeDirection(new[] { aimedIn }, origin, forward, reach, aim, -1) == forward &&
                SwordFormRule308.StrikeDirection(new[] { At(0, aimedIn.Position.x - origin.x, aimedIn.Position.z - origin.z, false) }, origin, forward, reach, aim, 0) == forward,
                "no aim correction without strike.aim, without an aimed enemy, or for a dead one");

            // ---- spear: too close is weak ----
            r.Check(s6, near > 0f && near < reach && inner > 0f && inner < 1f, "the spear row names a near zone and its share", "near " + near + " inner " + inner);
            r.Check(s6, r.Near(SwordFormRule308.TargetShare(0, near * .9f, through, near, inner), inner) && r.Near(SwordFormRule308.TargetShare(0, near * 1.1f, through, near, inner), 1f) &&
                r.Near(SwordFormRule308.TargetShare(1, near * .9f, through, near, inner), through * inner),
                "inside strike.near a strike is worth strike.inner of itself (film check (3): x0.4 inside 1.2 m); behind and close multiply");
            n = Line(new[] { At(0, 0f, near * .5f), At(1, 0f, d2) }, forward, -1);
            r.Check(s6, n == 2 && ids[0] == 0 && distances[0] < near, "a close enemy still takes the line first (struck, weakly); the one behind it is struck too");
            r.Check(s6, SwordFormRule308.TargetShare(0, near * .5f, 1f, 0f, inner) == 1f && SwordFormRule308.TargetShare(1, 1f, 1f, 0f, 1f) == 1f,
                "a row without strike.near / strike.through is the plain sword (the whole strike on every enemy)");

            // ---- the cone path is the old sword: count 1 is SelectTarget ----
            int same = 0, cases = 0;
            for (int k = 0; k < 4000; k++)
            {
                var actors = new SpellActorSnap[1 + rng.Next(6)];
                for (int i = 0; i < actors.Length; i++)
                    actors[i] = new SpellActorSnap(i, origin + new Vector3((float)(rng.NextDouble() * 8 - 4), 0f, (float)(rng.NextDouble() * 8 - 3)), rng.NextDouble() > .15, 1, 1f, false);
                int aimed = rng.Next(-1, actors.Length);
                int one = SwordFormRule308.SelectTarget(actors, origin, forward, swordReach, swordArc, aimed);
                int got = SwordFormRule308.SelectTargets(actors, origin, forward, swordReach, swordArc, 0f, aimed, 1, ids, distances);
                cases++; if ((got == 0 && one == -1) || (got == 1 && ids[0] == one)) same++;
            }
            r.Check(s2, same == cases, "the cone with one enemy per strike picks exactly what the sword always picked (aimed first, then the nearest)", same + " of " + cases);
            r.Check(s2, SwordFormRule308.SelectTargets(new[] { At(0, 0f, 1f) }, origin, forward, swordReach, swordArc, 0f, -1, 1, null, distances) == 0 &&
                SwordFormRule308.SelectTargets(new[] { At(0, 0f, 1f) }, origin, forward, 0f, swordArc, 0f, -1, 1, ids, distances) == 0 &&
                SwordFormRule308.SelectTargets(new[] { At(0, 0f, 1f), At(1, 0f, 1.5f) }, origin, forward, swordReach, swordArc, 0f, -1, 5, new int[1], new float[1]) == 1,
                "no buffer or no reach: nothing is struck; the buffers bound the count");

            // ---- axe: slow, heavy, one enemy ----
            float axeInterval = axe.F("sword.interval", 0f), axeReach = axe.F("sword.reach", 0f), axeArc = axe.F("sword.arc", 0f);
            r.Check(a1, axeInterval > swordInterval && axe.BasePower > sword.BasePower && axe.F("strike.width", 0f) == 0f && Mathf.RoundToInt(axe.F("strike.count", 1f)) == 1 &&
                axeArc < swordArc && axeReach < swordReach,
                "the axe strikes slower and harder than the sword: one enemy in a shorter, narrower cone",
                "interval " + axeInterval + " power " + axe.BasePower + " reach " + axeReach + " arc " + axeArc);
            n = SwordFormRule308.SelectTargets(new[] { At(0, 0f, axeReach * .8f), At(1, .2f, axeReach * .5f) }, origin, forward, axeReach, axeArc, 0f, -1, 1, ids, distances);
            r.Check(a1, n == 1 && ids[0] == 1, "two enemies inside its cone: the axe strikes one (the nearer)");
            // debate 15: read per cycle (a combo row's chain of strikes and its rest); the combo rows may carry a premium of at most
            // 15 % (every strike of the chain must land on one enemy to earn it)
            // (the bow is the ranged exception of debate 16: 70 % of this, read in Cases_Bow127)
            var plainRates = rows.Where(x => Mathf.RoundToInt(x.F("combo.hits", 0f)) < 2 && x.F("shot.draw", 0f) <= 0f).Select(CycleRate127).ToArray();
            var comboRates = rows.Where(x => Mathf.RoundToInt(x.F("combo.hits", 0f)) >= 2).Select(CycleRate127).ToArray();
            r.Check(a1, plainRates.Length >= 3 && plainRates.All(v => Math.Abs(v - plainRates[0]) <= plainRates[0] * 1e-3f) &&
                comboRates.All(v => v >= plainRates[0] * (1f - 1e-3f) && v <= plainRates[0] * 1.15f),
                "D308-127 parity reading (per cycle since debate 15): every form deals the same power per second to its first enemy; the axe's weight is per strike, not per second; the combo rows earn at most +15 %",
                "plain " + string.Join(" ", plainRates.Select(v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))) +
                " combo " + string.Join(" ", comboRates.Select(v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))));
            float spearGroggy = spear.F("groggy.hits", 0f) * spear.F("sword.interval", 0f), axeGroggy = axe.F("groggy.hits", 0f) * axeInterval;
            r.Check(s3, spearGroggy >= 1.8f - 1e-3f && axeGroggy <= 2.4f + 1e-3f && spearGroggy < axeGroggy &&
                spear.F("groggy.steps", 0f) / spear.F("groggy.hits", 1f) < 1f && axe.F("groggy.steps", 0f) / axe.F("groggy.hits", 1f) < 1f,
                "counter strikes to the first groggy step take groggy.hits x interval: inside D308-127's 1.8 - 2.4 s, the spear before the axe, and still less than a parry step per strike",
                "spear " + spearGroggy + " s, axe " + axeGroggy + " s");

            // ---- axe: a landed strike lowers the enemy's defence (the earth armour-break path and numbers) ----
            float shred = axe.F("break.defence", 0f), shredFor = axe.F("break.duration", 0f);
            var armour = c.Build.Rows.FirstOrDefault(x => x.Handler == "mod.armorbreak" && x.Element == Element.Earth);
            r.Check(a2, shred > 0f && shred <= 1f && shredFor > 0f && armour != null && armour.F("break.defence", -1f) == shred && armour.F("break.duration", -1f) == shredFor,
                "the axe lowers defence by the earth armour-break row's numbers (the same path, the same values)", "defence " + shred + " for " + shredFor + " s");
            r.Check(a2, rows.Where(x => x != axe).All(x => x.F("break.defence", 0f) == 0f && x.F("break.duration", 0f) == 0f), "only the axe lowers defence");
            var state = new EnemyModifierState308();
            float t0 = 3f;
            bool on = state.ShredDefence(t0 + shredFor, shred);
            // (a read past the end drops the entry, so the clock only moves forward here)
            r.Check(a2, on && r.Near(state.DefenceShred(t0), shred) && r.Near(state.DefenceNow(.5f, t0), Mathf.Max(0f, .5f - shred)) && state.DefenceNow(shred * .5f, t0) == 0f &&
                state.DefenceShred(t0 + shredFor + .01f) == 0f,
                "the shred runs for break.duration and lowers the defence the intake reads (never below 0)");
            state = new EnemyModifierState308();
            state.ShredDefence(t0 + shredFor, shred); state.ShredDefence(t0 + 1f + shredFor, shred);
            r.Check(a2, r.Near(state.DefenceShred(t0 + 1f), shred) && r.Near(state.DefenceShred(t0 + shredFor + .5f), shred),
                "a second strike refreshes the shred, it does not stack (the strongest running one counts)");
            state.EndShred();
            r.Check(a2, state.DefenceShred(t0 + 1f) == 0f, "taken back on Clear (death, rest, leaving): EndShred ends the running shred");
            r.Check(a2, !state.ShredDefence(t0, 0f), "a form without break.defence lowers nothing (the state refuses a zero shred)");
        }

        // Power per second on the first enemy over one cycle: a plain row is base power / interval; a combo row is its chain
        // ((hits - 1) strikes and the finisher's share) over (hits - 1) intervals and the rest.
        static float CycleRate127(SpellRow x)
        {
            float interval = x.F("sword.interval", 0f); int hits = Mathf.RoundToInt(x.F("combo.hits", 0f));
            if (hits < 2) return x.BasePower / Mathf.Max(1e-3f, interval);
            float power = x.BasePower * ((hits - 1) + x.F("combo.finish", 1f)), time = interval * (hits - 1) + x.F("combo.rest", 0f);
            return power / Mathf.Max(1e-3f, time);
        }
    }
}
