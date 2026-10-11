// SPEC-SPELL-120-308 L2 cases of WP-09 (companion shots): buff.companion, five rows, one rule (CompanionShotRule308).
// What the runner compares: the same accepted cast with and without the buff (shot count, power, element, landing time),
// the buff timing rule shared by every #308 buff, and the conditions without a shot. That a Companion-sourced hit gives no
// five-element credit and no groggy is EnemyVitals behaviour: the edit-mode fixture checks it (Spell120Checks308.WP09).
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
        static SpellActorSnap[] WP09Actors()
        {
            return new[]
            {
                new SpellActorSnap(0, new Vector3(0f, 0f, 3f), true, 1, 1f, false),
                new SpellActorSnap(1, new Vector3(0f, 0f, 10f), true, 5, 1f, true),
                new SpellActorSnap(2, new Vector3(.2f, 0f, 4f), false, 3, 0f, false),
            };
        }

        static CompanionShotRule308.Companion WP09Companion(SpellRow row)
        {
            return new CompanionShotRule308.Companion(row.Letter[0], row.Element, row.F("companion.power", 0f), Mathf.RoundToInt(row.F("companion.shots", 1f)),
                row.F("companion.speed", 0f), row.F("companion.interval", 0f));
        }

        static partial void RunWP09(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W09", false, "WP-09 cases need the table"); return; }
            var rows = c.Build.Rows.Where(x => x.Handler == "buff.companion").ToArray();
            if (!r.Check("W09", rows.Length == 5, "five rows use the companion handler", rows.Length.ToString())) return;

            // ---- rows: the eo-medial ieung-final cell of each initial, a buff without power of its own ----
            Element[] elements = { Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water };
            int[] cells = { 12, 36, 60, 84, 108 };
            r.Check("W09", rows.Select(x => x.Index).SequenceEqual(cells) && rows.Select(x => x.Element).SequenceEqual(elements) &&
                rows.All(x => x.Category == SpellCategory.Buff && x.Kind == SpellKind.Buff && x.Final == SpellFinal.Ieung && x.BasePower == 0f &&
                    x.Gate == SpellGateMode.Final && x.Feature == SpellLegacyFeature.None && string.IsNullOrEmpty(x.Pending)),
                "the five companion buffs: one per element in grid order, buff kind, ieung final, no power of their own",
                string.Join(",", rows.Select(x => x.Index)));
            r.Check("W09", rows.All(x => x.F("duration", 0f) == rows[0].F("duration", 0f) && x.F("companion.power", 0f) == rows[0].F("companion.power", 0f) &&
                x.F("companion.shots", 0f) == rows[0].F("companion.shots", 0f) && x.F("companion.speed", 0f) == rows[0].F("companion.speed", 0f)) &&
                rows[0].F("duration", 0f) > 0f && rows[0].F("companion.power", 0f) > 0f && rows[0].F("companion.shots", 0f) >= 1f && rows[0].F("companion.speed", 0f) > 0f,
                "the five rows carry the same numbers (only the element differs) and every number is usable");
            foreach (var row in rows)
            {
                var gate = new ModelGate();
                var unregistered = SpellResolveCore308.Resolve(row.Letter[0], .37f, .2f, row, gate, out _);
                gate.Registered.Add(row.Handler);
                var locked = SpellResolveCore308.Resolve(row.Letter[0], .37f, .2f, row, gate, out _);
                gate.Unlocked.Add(SpellFinal.Ieung);
                var open = SpellResolveCore308.Resolve(row.Letter[0], .37f, .2f, row, gate, out SpellCast cast);
                r.Check("W09", unregistered == SpellResolveStatus.Unavailable && locked == SpellResolveStatus.Locked && open == SpellResolveStatus.Ok &&
                    cast.Kind == SpellKind.Buff && cast.Power == 0f && cast.Element == row.Element,
                    "cell " + row.Index + ": Unavailable without its handler, Locked behind the ieung final, Ok when unlocked (a buff cast, power 0)",
                    unregistered + " " + locked + " " + open);
            }

            var actors = WP09Actors();
            Vector3 from = Vector3.zero;
            for (int k = 0; k < rows.Length; k++)
            {
                var row = rows[k]; string clause = row.Letter + "-1";
                var companion = WP09Companion(row);
                float duration = row.F("duration", 0f), flight = 3f / companion.Speed;
                var state = new CompanionShotRule308.State(); var timers = new SpellBuffState308();
                var shots = new List<CompanionShotRule308.Shot>();

                // without the buff: the accepted cast is alone
                state.Accompany(timers, actors, 0, from, 6f, shots);
                r.Check(clause, shots.Count == 0 && state.Count == 0, "without the buff an accepted cast gets no companion shot");

                // with the buff: the same accepted cast gets the row's shots at the aimed enemy
                bool on = state.Activate(timers, companion, 5f, duration);
                state.Accompany(timers, actors, 0, from, 6f, shots);
                r.Check(clause, on && state.Count == 1 && state.Active(timers, row.Letter[0], 6f) && shots.Count == companion.Shots && shots.Count >= 1,
                    "while the buff lasts every accepted cast is accompanied by the row's shots (the rule takes no cast kind: any accepted cast)", shots.Count + " shots");
                r.Check(clause, shots.Count > 0 && shots.All(s => s.TargetId == 0 && s.Life == actors[0].Life && s.Element == row.Element && s.Element == elements[k] &&
                    s.Power == companion.Power && s.Letter == row.Letter[0]) && r.Near(shots[0].At, 6f + flight),
                    "the shot goes to the aimed enemy with the buff glyph's element, the row's power, and lands after distance / speed",
                    shots.Count > 0 ? shots[0].Element + " power " + shots[0].Power + " at " + shots[0].At : "none");
                if (k == 0)
                {
                    r.Check("겅-1", shots.Count == companion.Shots, "every accepted cast with an aimed enemy, whatever its kind");
                    r.Check("겅-2", shots.Count >= 1 && shots[0].At > 6f && shots[0].Power > 0f, "a small extra shot is added to the cast: its own landing time and power");
                    r.Check("겅-3", shots.All(s => s.Element == Element.Wood) && row.Element == Element.Wood, "the body of the shot is wood");
                }

                // conditions without a shot
                shots.Clear(); state.Accompany(timers, actors, -1, from, 6f, shots);
                r.Check(clause, shots.Count == 0, "no aimed enemy: no companion shot");
                state.Accompany(timers, actors, 2, from, 6f, shots); state.Accompany(timers, actors, 9, from, 6f, shots); state.Accompany(timers, null, 0, from, 6f, shots);
                r.Check(clause, shots.Count == 0, "a dead or missing aimed enemy: no companion shot");

                // a farther enemy: the same shot, later
                state.Accompany(timers, actors, 1, from, 6f, shots);
                r.Check(clause, shots.Count == companion.Shots && shots[0].TargetId == 1 && shots[0].Life == 5 && r.Near(shots[0].At, 6f + 10f / companion.Speed),
                    "the landing time follows the distance to the aimed enemy (a boss is aimed at like anyone)");

                // timing rule of every #308 buff: recast replaces the remaining time, never adds
                shots.Clear();
                state.Activate(timers, companion, 20f, duration);
                r.Check(clause, state.Count == 1 && r.Near(timers.Remaining(row.Letter[0], 20f), duration) && state.Active(timers, row.Letter[0], 20f + duration - .01f) &&
                    !state.Active(timers, row.Letter[0], 20f + duration), "casting the same buff again starts its time over (one entry, never added up)");
                state.Accompany(timers, actors, 0, from, 20f + duration, shots);
                var ended = new List<char>();
                r.Check(clause, shots.Count == 0 && state.Expire(timers, 20f + duration - .01f, ended) == 0 && state.Expire(timers, 20f + duration, ended) == 1 &&
                    state.Count == 0 && ended.Count == 1 && ended[0] == row.Letter[0], "after the duration the buff is over: no shot, and the entry is dropped");

                // the host clears the shared timers on death, rest and scene leave
                state.Activate(timers, companion, 100f, duration); timers.Clear();
                state.Accompany(timers, actors, 0, from, 100f, shots);
                r.Check(clause, shots.Count == 0 && state.Expire(timers, 100f, null) == 1 && state.Count == 0, "cleared timers end the buff at once");
            }

            // ---- two different companion buffs run side by side: one shot each, each with its own element ----
            {
                var state = new CompanionShotRule308.State(); var timers = new SpellBuffState308(); var shots = new List<CompanionShotRule308.Shot>();
                state.Activate(timers, WP09Companion(rows[0]), 5f, rows[0].F("duration", 0f));
                state.Activate(timers, WP09Companion(rows[1]), 8f, rows[1].F("duration", 0f));
                state.Accompany(timers, actors, 0, from, 9f, shots);
                r.Check("W09", state.Count == 2 && shots.Count == 2 && shots[0].Element == Element.Wood && shots[1].Element == Element.Fire,
                    "two companion buffs at once: each accepted cast gets one shot of each element");
                shots.Clear(); state.Accompany(timers, actors, 0, from, 5f + rows[0].F("duration", 0f), shots);
                r.Check("W09", shots.Count == 1 && shots[0].Element == Element.Fire, "each buff ends on its own time: the later one keeps accompanying");
            }

            // ---- several shots per cast are staggered by the interval; unusable numbers never activate ----
            {
                var state = new CompanionShotRule308.State(); var timers = new SpellBuffState308(); var shots = new List<CompanionShotRule308.Shot>();
                char letter = rows[4].Letter[0];
                state.Activate(timers, new CompanionShotRule308.Companion(letter, Element.Water, 2f, 3, 20f, .1f), 0f, 30f);
                state.Accompany(timers, actors, 0, from, 1f, shots);
                r.Check("W09", shots.Count == 3 && shots.Select(s => s.Index).SequenceEqual(new[] { 0, 1, 2 }) && r.Near(shots[0].At, 1.15f) && r.Near(shots[1].At, 1.25f) && r.Near(shots[2].At, 1.35f),
                    "three shots per cast: landing times one interval apart");
                var bad = new CompanionShotRule308.State();
                r.Check("W09", !bad.Activate(timers, new CompanionShotRule308.Companion(letter, Element.Water, 0f, 1, 20f, 0f), 0f, 30f) &&
                    !bad.Activate(timers, new CompanionShotRule308.Companion(letter, Element.Water, 2f, 0, 20f, 0f), 0f, 30f) &&
                    !bad.Activate(timers, new CompanionShotRule308.Companion(letter, Element.Water, 2f, 1, 0f, 0f), 0f, 30f) &&
                    !bad.Activate(timers, new CompanionShotRule308.Companion(letter, Element.Water, 2f, 1, 20f, 0f), 0f, 0f) &&
                    !bad.Activate(null, new CompanionShotRule308.Companion(letter, Element.Water, 2f, 1, 20f, 0f), 0f, 30f) && bad.Count == 0,
                    "no power, no shots, no speed, no duration or no timers: the buff does not start (the host restores the ink)");
            }
        }
    }
}
