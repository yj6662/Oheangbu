using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 WP-09 fixtures (SPEC-SPELL-120-308 section 13): the five companion-shot buffs. Every cast is a drawn letter that
    // goes through the resolver and the real wiring. What the fixture adds to the offline rule cases: the buff really costs
    // one spell and plans no hit of its own, accepted casts of different kinds (attack, parry, the buff itself) are
    // accompanied, the accompanied cast's own plan is unchanged, and a landed companion shot is a Companion-sourced hit that
    // gives no five-element credit and no groggy on the real EnemyVitals.
    // Companion shots land in the handler's Tick; the fixture ticks the handler with a later clock (Edit Mode has no frames).
    // What stays for Play: shots against moving enemies, presentation, real input, how the pace feels.
    public static partial class Spell120Checks308
    {
        static partial void FixturesWP09(Report r)
        {
            // grid cells by index: 12 / 36 / 60 / 84 / 108 = the five companion buffs (wood, fire, earth, metal, water),
            // 1 = wood single, 25 = fire single (the accompanied direct casts), 7 = wood parry
            int[] buffCells = { 12, 36, 60, 84, 108 };
            Element[] elements = { Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water };
            char woodSingle = SpellGrammar308.LetterAt(1), fireSingle = SpellGrammar308.LetterAt(25), parry = SpellGrammar308.LetterAt(7);
            if (!r.Wants(new string(buffCells.Select(SpellGrammar308.LetterAt).ToArray()))) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var companion = effects.OfType<CompanionShotEffect308>().Single();
            using (var f = NewFixture(r, effects, new[] { "Rules308_Base", "Rules308_WP00", "Rules308_WP09" }))
            {
                ISpellCastHost host = f.Wiring;
                float cost = f.Config.SpellInkCost, brush = f.Brush(), now = Time.time;
                var target = f.Inside[0];

                // the buff is behind the ieung final
                f.RestoreAll(); f.Aim(target); f.Cast(SpellGrammar308.LetterAt(buffCells[0]));
                r.Check("W09", f.Misfires.Count == 1 && f.Misfires[0].Value == SpellResolveStatus.Locked && companion.ActiveCount == 0, "without the ieung final the buff is Locked (misfire)");
                f.Finals.Open.Add(SpellFinal.Ieung);

                for (int k = 0; k < buffCells.Length; k++)
                {
                    char buff = SpellGrammar308.LetterAt(buffCells[k]); string clause = buff + "-1";
                    // a direct single-target cast of another element than the buff: a companion shot that counted would add its bit
                    char single = elements[k] == Element.Wood ? fireSingle : woodSingle;
                    f.Resolver.TryRow(buff, out var row); f.Book.TryGet(single, out var plain);
                    if (!r.Check(clause, row != null && row.Handler == CompanionShotEffect308.HandlerId && row.Element == elements[k], "cell " + buffCells[k] + ": a companion row of its element")) continue;
                    float power = row.F("companion.power", 0f), duration = row.F("duration", 0f); int shots = Mathf.RoundToInt(row.F("companion.shots", 1f));
                    float plainPower = plain.BasePower * brush;

                    // without the buff: the cast is alone
                    f.Wiring.ClearSpells308(SpellClearReason.Rest); f.RestoreAll(); f.Aim(target);
                    f.Cast(single);
                    bool alone = companion.PendingShots == 0 && companion.ActiveCount == 0 && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && Near(f.Plans[0].Hits[0].Power, plainPower);
                    f.Land();
                    companion.Tick(new SpellTickContext(now + 1f, 0f, host, f.Fx));
                    r.Check(clause, alone && f.Hits.Count == 1 && f.Hits[0].Attack.Source == DamageSource.PlayerDirect, "without the buff the cast lands alone: one direct hit, no companion");

                    // the buff cast
                    f.Wiring.ClearSpells308(SpellClearReason.Rest); f.RestoreAll(); f.Aim(target);
                    float ink = f.Ink.Value; int begun = f.Fx.Begun;
                    f.Cast(buff);
                    r.Check(clause, f.Misfires.Count == 0 && f.Accepted.Count == 1 && f.Accepted[0].Kind == SpellKind.Buff && Near(ink - f.Ink.Value, cost) && f.Plans.Count == 0 &&
                        companion.ActiveCount == 1 && host.Buffs.Active(buff, now) && Near(host.Buffs.Remaining(buff, now), duration) && f.Fx.Begun == begun + 1,
                        "the buff costs one spell, is accepted as a buff, plans no hit of its own, runs for the row's duration, and asks the presenter once");
                    r.Check(clause, companion.PendingShots == shots, "the accepted buff cast itself is accompanied (any accepted cast with an aimed enemy)");
                    companion.Tick(new SpellTickContext(now + 1f, 0f, host, f.Fx));
                    r.Check(clause, companion.PendingShots == 0 && f.Hits.Count == shots && f.Hits.All(h => h.Target == target && h.Attack.Source == DamageSource.Companion &&
                        h.Attack.Element == elements[k] && Near(h.AppliedDamage, power)),
                        "the shot lands in the handler's tick: a Companion-sourced hit of the buff's element with the row's power");

                    // an accompanied direct cast while the enemy's weak point is open
                    target.OpenWeakPoint();
                    float groggy = target.Groggy.Value01; f.ClearLog();
                    f.Cast(single);
                    r.Check(clause, companion.PendingShots == shots && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && Near(f.Plans[0].Hits[0].Power, plainPower),
                        "with the buff the same cast keeps its own plan (one hit, same power) and gets the companion shots on top");
                    f.Land();
                    int mask = target.WeakPointElementMask; float hp = target.Hp;
                    bool direct = f.Hits.Count == 1 && f.Hits[0].Attack.Source == DamageSource.PlayerDirect && mask == 1 << (int)plain.Element;
                    companion.Tick(new SpellTickContext(now + 1f, 0f, host, f.Fx));
                    r.Check(clause, direct && f.Hits.Count == 1 + shots && Near(hp - target.Hp, power * target.DamageMultiplier * shots) &&
                        f.Hits.Skip(1).All(h => h.Attack.Source == DamageSource.Companion && h.Attack.Element == elements[k]),
                        "the direct hit lands first, then the companion shots of the buff's element (amplified by the open weak point like any hit)");
                    r.Check(clause, target.WeakPointActive && target.WeakPointElementMask == mask && !target.CompletionAwarded && target.Groggy.Value01 == groggy,
                        "the companion shot gives no five-element credit (the mask is unchanged) and no groggy");

                    // any kind of accepted cast is accompanied; a second cast of the buff starts its time over
                    f.ClearLog(); f.Cast(parry);
                    r.Check(clause, f.Accepted.Count == 1 && f.Accepted[0].Kind == SpellKind.Parry && companion.PendingShots == shots, "an accepted parry is accompanied too (the kind does not matter)");
                    f.Cast(buff);
                    r.Check(clause, companion.ActiveCount == 1 && Near(host.Buffs.Remaining(buff, now), duration) && companion.PendingShots == shots * 2,
                        "casting the buff again keeps one entry and starts its time over");

                    // a shot whose enemy died before it landed is lost; the buff ends on death, rest and scene leave
                    target.TakeDamage(target.MaxHp * 2f); f.ClearLog();
                    companion.Tick(new SpellTickContext(now + 1f, 0f, host, f.Fx));
                    r.Check(clause, f.Hits.Count == 0 && companion.PendingShots == 0, "a companion shot never lands on a dead enemy or on its next life");
                    f.Wiring.ClearSpells308(SpellClearReason.PlayerDied);
                    r.Check(clause, companion.ActiveCount == 0 && companion.PendingShots == 0 && !host.Buffs.Active(buff, now), "a clear (player death) ends the buff and drops its shots");
                }

                // two buffs side by side: one shot of each element per accepted cast
                f.Wiring.ClearSpells308(SpellClearReason.Rest); f.RestoreAll(); f.Aim(target);
                f.Cast(SpellGrammar308.LetterAt(buffCells[0])); f.Cast(SpellGrammar308.LetterAt(buffCells[1]));
                companion.Tick(new SpellTickContext(now + 1f, 0f, host, f.Fx)); f.ClearLog();
                f.Cast(woodSingle);
                companion.Tick(new SpellTickContext(now + 1f, 0f, host, f.Fx));
                r.Check("W09", companion.ActiveCount == 2 && f.Hits.Count(h => h.Attack.Source == DamageSource.Companion) == 2 &&
                    f.Hits.Any(h => h.Attack.Source == DamageSource.Companion && h.Attack.Element == Element.Wood) &&
                    f.Hits.Any(h => h.Attack.Source == DamageSource.Companion && h.Attack.Element == Element.Fire),
                    "two companion buffs run side by side: each accepted cast gets one shot of each element");
                f.Wiring.ClearSpells308(SpellClearReason.Rest);
            }
        }
    }
}
