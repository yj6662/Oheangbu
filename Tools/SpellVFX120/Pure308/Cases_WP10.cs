// SPEC-SPELL-120-308 L2 cases of WP-10 (harmony install, the five a + mieum glyphs): attach to one enemy without damage,
// detonate on an attack spell's hit only, groggy and element of the detonation from the row, expiry, cap, lost marks.
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
        // six actors: three ahead of the caster, one behind, one far, one dead
        static SpellActorSnap[] WP10Actors()
        {
            return new[]
            {
                new SpellActorSnap(0, new Vector3(0f, 0f, 3f), true, 7, 1f, false),
                new SpellActorSnap(1, new Vector3(.8f, 0f, 4.5f), true, 3, 1f, false),
                new SpellActorSnap(2, new Vector3(-.8f, 0f, 6f), true, 1, .5f, true),
                new SpellActorSnap(3, new Vector3(0f, 0f, -5f), true, 1, 1f, false),
                new SpellActorSnap(4, new Vector3(0f, 0f, 60f), true, 2, 1f, false),
                new SpellActorSnap(5, new Vector3(2f, 0f, 2f), false, 4, 0f, false),
            };
        }

        // What the effect does with a confirmed hit: every live mark of that enemy and life fires, one at a time.
        static int WP10Hit(List<InstallMark308> marks, int targetId, uint life, InstallRule308.Origin origin, float applied, float now, List<InstallMark308> fired)
        {
            int count = 0;
            if (!InstallRule308.Detonates(origin, applied)) return 0;
            while (InstallRule308.TakeForDetonation(marks, targetId, life, now, out var mark)) { fired.Add(mark); count++; }
            return count;
        }

        static partial void RunWP10(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W10", false, "WP-10 cases need the table"); return; }
            var rows = c.Build.Rows.Where(x => x.Handler == WP10HandlerId).ToArray();
            r.Check("W10", rows.Length == 5 && rows.All(x => x.Category == SpellCategory.Install && x.Kind == SpellKind.Install && x.Final == SpellFinal.Mieum),
                "five install rows (a + mieum) carry the handler install.mark", rows.Length.ToString());
            r.Check("W10", rows.Select(x => x.Element).Distinct().Count() == 5, "one install row per element");
            r.Check("W10", rows.All(x => x.Has("install.life") && x.Has("install.max") && x.Has("detonate.groggy") && x.Has("detonate.power")),
                "every install row carries the four parameters");
            r.Check("W10", rows.Select(x => x.F("install.life", -1f) + "/" + x.F("install.max", -1f) + "/" + x.F("detonate.groggy", -1f) + "/" + x.F("detonate.power", -1f)).Distinct().Count() == 1,
                "the five glyphs share one interim detonation (same numbers)");
            var open = new ModelGate { Registered = new HashSet<string> { WP10HandlerId }, Unlocked = new HashSet<SpellFinal> { SpellFinal.Mieum } };
            var shut = new ModelGate { Registered = new HashSet<string> { WP10HandlerId } };
            var actors = WP10Actors();
            const float brush = .8f, hold = .75f, now = 100f;
            var origins = (InstallRule308.Origin[])Enum.GetValues(typeof(InstallRule308.Origin));

            foreach (var row in rows)
            {
                char letter = row.Letter[0];
                float life = row.F("install.life", 0f), sheetPower = row.F("detonate.power", 0f);
                int max = (int)row.F("install.max", 0f), groggy = (int)row.F("detonate.groggy", 0f);

                // ---- clause 1: attached to one enemy, the install itself deals no damage ----
                var status = SpellResolveCore308.Resolve(letter, brush, brush, row, open, out SpellCast cast);
                r.Check(row.Letter + "-1", status == SpellResolveStatus.Ok && cast.Kind == SpellKind.Install && cast.Power == 0f && cast.Brush == brush && cast.Element == row.Element,
                    "a drawn install resolves as Install with power 0 exactly: attaching deals no damage", status + " power " + cast.Power);
                r.Check(row.Letter + "-1", SpellResolveCore308.Resolve(letter, brush, brush, row, shut, out _) == SpellResolveStatus.Locked,
                    "behind the mieum final: Locked until that final is unlocked");
                float flight = InstallRule308.FlightSeconds(Vector3.zero, actors[1].Position, 20f, cast.SpeedMul);
                r.Check(row.Letter + "-1", r.Near(flight, Vector3.Distance(Vector3.zero, actors[1].Position) / 20f) && flight > 0f,
                    "the mark flies to its enemy at the single-spell speed (distance / speed)", flight.ToString());
                r.Check(row.Letter + "-1", r.Near(InstallRule308.FlightSeconds(Vector3.zero, actors[1].Position, .1f, 1f), Vector3.Distance(Vector3.zero, actors[1].Position)),
                    "a projectile speed below 1 is floored at 1, like the wiring's single judgement");
                var marks = new List<InstallMark308>(); var replaced = new List<InstallMark308>(); var fired = new List<InstallMark308>();
                long serial = 0;
                InstallMark308 New(int id, float at) => new InstallMark308
                {
                    Serial = ++serial, TargetId = id, Life = actors[id].Life, Element = cast.Element,
                    Power = InstallRule308.DetonationPower(sheetPower, cast.Brush, hold), Groggy = groggy, ExpiresAt = at + life,
                };
                // before the mark has arrived nothing can detonate ("after attaching")
                r.Check(row.Letter + "-2", WP10Hit(marks, 1, actors[1].Life, InstallRule308.Origin.AttackSpell, 12f, now, fired) == 0,
                    "an attack hit while the mark is still in flight detonates nothing (attach first)");
                r.Check(row.Letter + "-1", InstallRule308.Holds(actors, 1, actors[1].Life) && !InstallRule308.Holds(actors, 5, actors[5].Life) &&
                    !InstallRule308.Holds(actors, 1, actors[1].Life + 1) && !InstallRule308.Holds(actors, 9, 0),
                    "the mark attaches only to the living enemy and life it was thrown at");
                InstallRule308.Attach(marks, New(1, now), max, replaced);
                r.Check(row.Letter + "-1", marks.Count == 1 && marks[0].TargetId == 1 && replaced.Count == 0 && marks.Count(m => m.TargetId != 1) == 0,
                    "exactly one enemy carries the mark");

                // ---- clause 2: only the hit of an attack spell detonates, and only on the marked enemy ----
                foreach (var origin in origins)
                {
                    if (origin == InstallRule308.Origin.AttackSpell) continue;
                    int n = WP10Hit(marks, 1, actors[1].Life, origin, 12f, now + 1f, fired);
                    r.Check(row.Letter + "-2", n == 0 && marks.Count == 1, origin + " damage does not detonate the mark");
                }
                r.Check(row.Letter + "-2", WP10Hit(marks, 1, actors[1].Life, InstallRule308.Origin.AttackSpell, 0f, now + 1f, fired) == 0 && marks.Count == 1,
                    "an attack that applied no damage (refused or absorbed) does not detonate");
                r.Check(row.Letter + "-2", WP10Hit(marks, 0, actors[0].Life, InstallRule308.Origin.AttackSpell, 12f, now + 1f, fired) == 0 && marks.Count == 1,
                    "an attack hit on another enemy does not detonate");
                // review B: the check above was satisfied by the two enemies' different life numbers alone (a rule that forgot to compare
                // the enemy still passed). The enemies of a scene normally share one life number, so the enemy itself must be compared.
                r.Check(row.Letter + "-2", WP10Hit(marks, 0, actors[1].Life, InstallRule308.Origin.AttackSpell, 12f, now + 1f, fired) == 0 &&
                    WP10Hit(marks, 4, actors[1].Life, InstallRule308.Origin.AttackSpell, 12f, now + 1f, fired) == 0 && marks.Count == 1,
                    "an attack hit on another enemy that lives the same life number as the marked one does not detonate (the mark belongs to its enemy, not to a number)");
                r.Check(row.Letter + "-2", WP10Hit(marks, 1, actors[1].Life + 1, InstallRule308.Origin.AttackSpell, 12f, now + 1f, fired) == 0 && marks.Count == 1,
                    "the same slot living another life does not detonate the old mark");
                int detonations = WP10Hit(marks, 1, actors[1].Life, InstallRule308.Origin.AttackSpell, 12f, now + 1f, fired);
                r.Check(row.Letter + "-2", detonations == 1 && marks.Count == 0 && fired.Count == 1 && fired[0].TargetId == 1,
                    "an attack spell's hit on the marked enemy detonates the mark");

                // ---- clause 3: a large groggy gain, from the row ----
                r.Check(row.Letter + "-3", fired.Count == 1 && fired[0].Groggy == groggy && groggy == (int)row.F("detonate.groggy", -1f) && groggy >= 2,
                    "one detonation carries the row's groggy steps, more than one parry-sized step", groggy.ToString());

                // ---- clause 4: one hit of the installed element ----
                r.Check(row.Letter + "-4", fired.Count == 1 && fired[0].Element == row.Element && fired[0].Element == cast.Element,
                    "the detonation carries the element of the installing glyph's initial", fired.Count == 1 ? fired[0].Element.ToString() : "none");
                r.Check(row.Letter + "-4", fired.Count == 1 && r.Near(fired[0].Power, sheetPower * brush * hold) && fired[0].Power > 0f,
                    "detonation power = sheet value x brush x hold decay of the installing cast", fired.Count == 1 ? fired[0].Power.ToString() : "none");
                r.Check(row.Letter + "-4", WP10Hit(marks, 1, actors[1].Life, InstallRule308.Origin.AttackSpell, 12f, now + 2f, fired) == 0 && fired.Count == 1,
                    "the next attack hit finds no mark: one detonation per install");
            }

            // ---- COMBAT-INSTALL clauses shared by the five glyphs ----
            var first = rows[0];
            float lifeSeconds = first.F("install.life", 0f); int cap = (int)first.F("install.max", 0f);
            InstallMark308 Mark(long serial, int id, float at) => new InstallMark308
            { Serial = serial, TargetId = id, Life = actors[id].Life, Element = first.Element, Power = 14f, Groggy = 2, ExpiresAt = at + lifeSeconds };

            // an unfired mark expires (ink only)
            var list = new List<InstallMark308>(); var expired = new List<InstallMark308>(); var lost = new List<InstallMark308>(); var shots = new List<InstallMark308>();
            InstallRule308.Attach(list, Mark(1, 0, now), cap, null);
            InstallRule308.Sweep(list, actors, now + lifeSeconds - .01f, expired, lost);
            r.Check("W10", list.Count == 1 && expired.Count == 0 && lost.Count == 0, "an unfired mark is kept until its life ends");
            InstallRule308.Sweep(list, actors, now + lifeSeconds, expired, lost);
            r.Check("W10", list.Count == 0 && expired.Count == 1 && lost.Count == 0, "an unfired mark expires at install.life (nothing is refunded, nothing fires)");
            var probe = Mark(7, 0, now);
            r.Check("W10", InstallRule308.FateOf(probe, true, now) == InstallRule308.Fate.Keep && InstallRule308.FateOf(probe, true, now + lifeSeconds) == InstallRule308.Fate.Expired &&
                InstallRule308.FateOf(probe, false, now) == InstallRule308.Fate.Lost && InstallRule308.FateOf(probe, false, now + lifeSeconds) == InstallRule308.Fate.Lost,
                "per mark and frame: kept, expired at its time, lost with its enemy (lost wins over expired)");
            InstallRule308.Attach(list, Mark(2, 0, now), cap, null);
            r.Check("W10", WP10Hit(list, 0, actors[0].Life, InstallRule308.Origin.AttackSpell, 5f, now + lifeSeconds, shots) == 0,
                "a hit at the very moment of expiry does not detonate");
            list.Clear();

            // the cap: over it the oldest mark makes room
            var gone = new List<InstallMark308>();
            r.Check("W10", cap == 1, "TEST cap: one mark at a time", cap.ToString());
            InstallRule308.Attach(list, Mark(1, 0, now), 1, gone);
            InstallRule308.Attach(list, Mark(2, 1, now + 1f), 1, gone);
            r.Check("W10", list.Count == 1 && list[0].Serial == 2 && list[0].TargetId == 1 && gone.Count == 1 && gone[0].Serial == 1,
                "cap 1: a second install replaces the first (the old mark is lost unfired)");
            list.Clear(); gone.Clear();
            InstallRule308.Attach(list, Mark(5, 0, now), 2, gone); InstallRule308.Attach(list, Mark(3, 1, now), 2, gone); InstallRule308.Attach(list, Mark(9, 2, now), 2, gone);
            r.Check("W10", list.Count == 2 && gone.Count == 1 && gone[0].Serial == 3 && list.All(m => m.Serial != 3),
                "cap 2: the third install replaces the oldest mark (smallest serial), whatever its place in the list");
            list.Clear(); gone.Clear();
            InstallRule308.Attach(list, Mark(1, 0, now), 0, gone);
            r.Check("W10", list.Count == 1, "a cap below 1 is read as 1 (a cast always installs)");
            list.Clear();

            // two marks on one enemy (cap 2): one attack hit fires both, oldest first, each exactly once
            InstallRule308.Attach(list, Mark(4, 2, now), 2, null); InstallRule308.Attach(list, Mark(2, 2, now), 2, null);
            shots.Clear();
            int both = WP10Hit(list, 2, actors[2].Life, InstallRule308.Origin.AttackSpell, 5f, now + 1f, shots);
            r.Check("W10", both == 2 && list.Count == 0 && shots[0].Serial == 2 && shots[1].Serial == 4, "repeated installs on one enemy all fire on the next attack hit, oldest first");

            // a mark dies with its enemy's life
            InstallRule308.Attach(list, Mark(1, 1, now), 3, null); InstallRule308.Attach(list, Mark(2, 0, now), 3, null); InstallRule308.Attach(list, Mark(3, 1, now), 3, null);
            var later = WP10Actors();
            later[1] = new SpellActorSnap(1, later[1].Position, true, later[1].Life + 1, 1f, false);   // defeated and restored
            expired.Clear(); lost.Clear();
            InstallRule308.Sweep(list, later, now + 1f, expired, lost);
            r.Check("W10", list.Count == 1 && list[0].TargetId == 0 && lost.Count == 2 && expired.Count == 0, "marks on an enemy that lives another life are lost, the others stay");
            later[0] = new SpellActorSnap(0, later[0].Position, false, later[0].Life, 0f, false);
            lost.Clear(); InstallRule308.Sweep(list, later, now + 1f, expired, lost);
            r.Check("W10", list.Count == 0 && lost.Count == 1, "a mark on a dead enemy is lost");
            InstallRule308.Attach(list, Mark(1, 1, now), 3, null); InstallRule308.Attach(list, Mark(2, 0, now), 3, null);
            lost.Clear(); InstallRule308.DropTarget(list, 1, lost);
            r.Check("W10", list.Count == 1 && list[0].TargetId == 0 && lost.Count == 1 && lost[0].TargetId == 1, "a defeated enemy drops its marks at once");

            // which origins may detonate: the attack spell alone (A10: no other source reaches the harmony groggy)
            r.Check("A10", origins.Count(o => InstallRule308.Detonates(o, 1f)) == 1 && InstallRule308.Detonates(InstallRule308.Origin.AttackSpell, 1f) &&
                !InstallRule308.Detonates(InstallRule308.Origin.Harmony, 1f), "exactly one origin detonates a mark; a detonation never detonates another mark");

            // power arithmetic never produces a broken hit
            r.Check("W10", InstallRule308.DetonationPower(14f, 1f, 1f) == 14f && InstallRule308.DetonationPower(14f, float.NaN, 1f) == 0f &&
                InstallRule308.DetonationPower(14f, -1f, 1f) == 0f && InstallRule308.DetonationPower(0f, 1f, 1f) == 0f,
                "detonation power: sheet value at brush 1, and 0 instead of a non-finite or negative number");
        }

        const string WP10HandlerId = "install.mark";
    }
}
