using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 WP-11 fixtures (SPEC-SPELL-120-308): the three single shots that are handed over to a handler, on the real wiring.
    //  - with the effect registry: the handler route (range, unguided fall, turn-limited guidance);
    //  - without it (old scenes, old checks): the same glyphs on their old route, exactly one scheduled hit as before.
    // Every cast comes from the resolver. Held hits are driven with an explicit clock (Edit Mode has no frame loop).
    // What stays for Play: the real flight, the stroke adapter's pattern against the held hit, enemies that move by themselves.
    public static partial class Spell120Checks308
    {
        static readonly string[] WP11Sheets = { "Rules308_Base", "Rules308_WP00", "Rules308_WP11" };

        static partial void FixturesWP11(Report r)
        {
            // grid cells by index (no glyph is named): 1 = wood single, 49 = earth single, 97 = water single, 25 = fire single (not handed over)
            char wood = SpellGrammar308.LetterAt(1), earth = SpellGrammar308.LetterAt(49), water = SpellGrammar308.LetterAt(97), fire = SpellGrammar308.LetterAt(25);
            // the single shots behind a final whose first stage is one of those three (same initial, vowel a): wood 3 5 6,
            // earth 50 51 53 54, water 99 101 102 (nieun / siot / ieung, and giyeok for earth)
            int[] woodBody = { 3, 5, 6 }, earthBody = { 50, 51, 53, 54 }, waterBody = { 99, 101, 102 };
            if (!r.Wants(new string(new[] { wood, earth, water }.Concat(woodBody.Concat(earthBody).Concat(waterBody).Select(SpellGrammar308.LetterAt)).ToArray()))) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var ranged = effects.OfType<RangedSingleEffect308>().FirstOrDefault(); var rock = effects.OfType<BallisticEffect308>().FirstOrDefault();
            var guided = effects.OfType<HomingEffect308>().FirstOrDefault();
            if (!r.Check("W11", ranged != null && rock != null && guided != null, "the three WP-11 effects are registered")) return;

            using (var f = NewFixture(r, effects, WP11Sheets))
            {
                var host = (ISpellCastHost)f.Wiring;
                float brush = f.Brush(), cost = f.Config.SpellInkCost;
                Vector3 origin = SpellFixture308.Origin;
                var near = f.Inside[0]; var far = f.Outside[1];
                Vector3 nearHome = near.transform.position, farHome = far.transform.position;
                void Reset()
                {
                    near.transform.position = nearHome; far.transform.position = farHome;
                    Physics.SyncTransforms();
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    f.RestoreAll(); f.Aim(null);
                }
                f.Resolver.TryRow(wood, out var woodRow); f.Resolver.TryRow(earth, out var earthRow); f.Resolver.TryRow(water, out var waterRow); f.Resolver.TryRow(fire, out var fireRow);
                r.Check("W11", SpellTakeover308.Declared(woodRow) && SpellTakeover308.Declared(earthRow) && SpellTakeover308.Declared(waterRow) && !SpellTakeover308.Declared(fireRow) &&
                    woodRow.Feature == SpellLegacyFeature.Book, "the fixture table hands the three rows over and keeps their legacy feature");

                // ---- wood single: range ----
                float range = woodRow.F("range", 0f);
                Reset(); f.Aim(near);
                float ink = f.Ink.Value, hp = near.Hp; f.Cast(wood);
                r.Check("가-3", f.Misfires.Count == 0 && f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 &&
                    f.Plans[0].Hits[0].Target == near && Near(f.Plans[0].Hits[0].Power, woodRow.BasePower * brush) && ranged.OutOfRangeCasts == 0,
                    "a target within the range: one spell cost, one accepted cast, the wiring's own scheduled hit of base power x brush");
                f.Land();
                r.Check("가-3", Near(hp - near.Hp, woodRow.BasePower * brush) && f.Hits.Count == 1, "that hit lands through the wiring's own tick");
                Reset(); far.transform.position = origin + Vector3.forward * (range + 1f); Physics.SyncTransforms(); f.Aim(far);
                ink = f.Ink.Value; hp = far.Hp; int before = ranged.OutOfRangeCasts; f.Cast(wood);
                r.Check("가-3", f.Misfires.Count == 0 && f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && f.Plans.Count == 0 && f.PendingCount == 0 &&
                    ranged.OutOfRangeCasts == before + 1, "a locked target one metre beyond the range: the ink is spent, the cast is accepted, nothing is scheduled");
                f.Land();
                r.Check("가-3", far.Hp == hp && f.Hits.Count == 0, "and nothing lands on it");

                // ---- earth single: the rock falls where the target stood ----
                float fall = earthRow.F("fall.radius", 0f);
                Reset(); f.Aim(near);
                ink = f.Ink.Value; hp = near.Hp; float castAt = Time.time; f.Cast(earth);
                bool planned = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1;
                r.Check("마-4", f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && planned && f.Plans[0].Hits[0].Target == near && f.Plans[0].Hits[0].Power == 0f && rock.RocksInFlight == 1,
                    "the cast keeps the adapter's flight plan (same target, same flight time) but that plan carries no damage; the rock is held by the handler");
                if (planned)
                {
                    float landsAt = f.Plans[0].Hits[0].ImpactTime;
                    f.Land();
                    r.Check("마-4", near.Hp == hp && f.Hits.Count == 0, "the adapter's plan lands without any damage");
                    rock.Tick(new SpellTickContext(landsAt - .01f, 0f, host, f.Fx));
                    r.Check("마-4", near.Hp == hp && rock.RocksInFlight == 1, "nothing lands before the flight time is over");
                    rock.Tick(new SpellTickContext(landsAt + .01f, 0f, host, f.Fx));
                    r.Check("마-4", Near(hp - near.Hp, earthRow.BasePower * brush) && f.Hits.Count == 1 && f.Hits[0].Attack.Source == DamageSource.PlayerDirect &&
                        f.Hits[0].Attack.Element == earthRow.Element && rock.RocksInFlight == 0 && rock.Misses == 0,
                        "a target that stayed on its spot is hit once with base power x brush, as a direct player hit of the row's element");
                    Reset(); f.Aim(near); hp = near.Hp; int misses = rock.Misses; f.Cast(earth);
                    landsAt = f.Plans[0].Hits[0].ImpactTime;
                    near.transform.position = nearHome + Vector3.right * (fall + .5f); Physics.SyncTransforms();
                    rock.Tick(new SpellTickContext(landsAt + .01f, 0f, host, f.Fx)); f.Land();
                    r.Check("마-4", near.Hp == hp && f.Hits.Count == 0 && rock.Misses == misses + 1 && rock.RocksInFlight == 0,
                        "a target that walked out of fall.radius during the flight is missed: the rock is not guided");
                    Reset(); f.Aim(near); f.Cast(earth);
                    f.Wiring.ClearSpells308(SpellClearReason.PlayerDied);
                    r.Check("마-4", rock.RocksInFlight == 0, "ClearSpells308 drops a rock in flight");
                }

                // ---- water single: guided within its turn limit ----
                Reset(); f.Aim(near);
                ink = f.Ink.Value; hp = near.Hp; f.Cast(water);
                r.Check("아-4", f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && f.Plans[0].Hits[0].Power == 0f && guided.ShotsInFlight == 1,
                    "the cast keeps the adapter's flight plan without damage; the guided shot is held by the handler");
                float clock = Time.time; int steps = 0;
                near.transform.position = nearHome + Vector3.right * 1.5f; Physics.SyncTransforms();   // the target stepped aside after the cast
                while (guided.ShotsInFlight > 0 && steps++ < 600) { clock += 1f / 60f; guided.Tick(new SpellTickContext(clock, 1f / 60f, host, f.Fx)); }
                r.Check("아-4", Near(hp - near.Hp, waterRow.BasePower * brush) && f.Hits.Count == 1 && guided.Misses == 0,
                    "a target that stepped aside after the cast is followed and hit once with base power x brush");
                Reset(); f.Aim(near); hp = near.Hp; int lost = guided.Misses; f.Cast(water);
                clock = Time.time; steps = 0;
                near.transform.position = origin - Vector3.forward * 30f + Vector3.right * 6f; Physics.SyncTransforms();   // far behind the caster
                while (guided.ShotsInFlight > 0 && steps++ < 1200) { clock += 1f / 60f; guided.Tick(new SpellTickContext(clock, 1f / 60f, host, f.Fx)); }
                r.Check("아-3", near.Hp == hp && guided.Misses == lost + 1 && guided.ShotsInFlight == 0,
                    "a target that got far behind the shot is missed: the shot cannot turn past its limit (or gives up after flight.max)");

                // an air cast of each: the old air cast (ink spent, an empty plan, nothing held)
                foreach (char letter in new[] { wood, earth, water })
                {
                    Reset();
                    for (int i = 0; i < f.Inside.Length; i++) f.Inside[i].TakeDamage(f.Inside[i].Hp);     // nobody to free-aim at
                    f.Boss.TakeDamage(f.Boss.Hp); foreach (var enemy in f.Outside) enemy.TakeDamage(enemy.Hp);
                    f.ClearLog(); ink = f.Ink.Value; f.Cast(letter);
                    r.Check("W11", f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && f.PendingCount == 0 && rock.RocksInFlight == 0 && guided.ShotsInFlight == 0,
                        "an air cast (cell " + SpellGrammar308.IndexOf(letter) + "): ink spent, nothing scheduled, nothing held");
                }
            }

            // ---- without the registry: the old route, whatever the sheet says ----
            using (var f = NewFixture(r, null, WP11Sheets))
            {
                float brush = f.Brush(), cost = f.Config.SpellInkCost;
                var far = f.Outside[1];
                foreach (char letter in new[] { wood, earth, water })
                {
                    f.RestoreAll(); f.Aim(far);
                    f.Resolver.TryRow(letter, out var row);
                    float ink = f.Ink.Value, hp = far.Hp; f.Cast(letter);
                    bool one = f.Misfires.Count == 0 && f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 &&
                        f.Plans[0].Hits[0].Target == far && Near(f.Plans[0].Hits[0].Power, row.BasePower * brush);
                    f.Land();
                    r.Check("A4", one && Near(hp - far.Hp, row.BasePower * brush),
                        "no registry injected (cell " + SpellGrammar308.IndexOf(letter) + "): the handed-over glyph casts on its old route, one scheduled hit of base power x brush on a far locked target");
                }
            }

            // ---- decision of 2026-10-04: how a shot flies belongs to the initial consonant (Spellcraft Bible chapter 4) ----
            // A single shot behind a final keeps its own second effect and takes its first stage from the handler of the glyph
            // without the final, with that glyph's numbers: the wood body reaches no further than the base range, the earth
            // body falls unguided and misses an enemy that walked away, the water body loses an enemy that got behind it.
            string[] bodySheets = { "Rules308_Base", "Rules308_WP00", "Rules308_WP02", "Rules308_WP03", "Rules308_WP04", "Rules308_WP05", "Rules308_WP07", "Rules308_WP11", "Rules308_WP13" };
            using (var f = NewFixture(r, effects, bodySheets))
            {
                var host = (ISpellCastHost)f.Wiring;
                foreach (SpellFinal final in new[] { SpellFinal.Giyeok, SpellFinal.Nieun, SpellFinal.Siot, SpellFinal.Ieung }) f.Finals.Open.Add(final);
                float cost = f.Config.SpellInkCost;
                Vector3 origin = SpellFixture308.Origin;
                var near = f.Inside[0]; var far = f.Outside[1];
                Vector3 nearHome = near.transform.position, farHome = far.transform.position;
                void Reset()
                {
                    near.transform.position = nearHome; far.transform.position = farHome;
                    Physics.SyncTransforms();
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    f.RestoreAll(); f.Aim(null);
                }
                f.Resolver.TryRow(wood, out var woodRow); f.Resolver.TryRow(earth, out var earthRow);
                float range = woodRow.F("range", 0f), fall = earthRow.F("fall.radius", 0f);

                foreach (int cell in woodBody)
                {
                    char letter = SpellGrammar308.LetterAt(cell);
                    Reset(); far.transform.position = origin + Vector3.forward * (range + 1f); Physics.SyncTransforms(); f.Aim(far);
                    float ink = f.Ink.Value, hp = far.Hp; int before = ranged.OutOfRangeCasts; f.Cast(letter);
                    bool beyond = f.Misfires.Count == 0 && f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && f.PendingCount == 0 && ranged.OutOfRangeCasts == before + 1;
                    f.Land();
                    beyond &= far.Hp == hp && f.Hits.Count == 0;
                    Reset(); f.Aim(near); f.Cast(letter);
                    r.Check("W11", beyond && f.Accepted.Count == 1 && f.PendingCount >= 1,
                        "cell " + cell + ": one metre beyond the base glyph's range nothing is scheduled and nothing lands (the ink is spent); within the range the shot is scheduled");
                }

                foreach (int cell in earthBody)
                {
                    char letter = SpellGrammar308.LetterAt(cell);
                    Reset(); f.Aim(near); float hp = near.Hp; int misses = rock.Misses; f.Cast(letter);
                    bool held = f.Misfires.Count == 0 && f.Accepted.Count == 1 && rock.RocksInFlight == 1 && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && f.Plans[0].Hits[0].Power == 0f;
                    float landsAt = held ? f.Plans[0].Hits[0].ImpactTime : Time.time;
                    near.transform.position = nearHome + Vector3.right * (fall + .5f); Physics.SyncTransforms();     // the enemy walked on during the flight
                    rock.Tick(new SpellTickContext(landsAt + .01f, 0f, host, f.Fx)); f.Land();
                    r.Check("W11", held && near.Hp == hp && f.Hits.Count == 0 && rock.Misses == misses + 1 && rock.RocksInFlight == 0,
                        "cell " + cell + ": its first stage is the base glyph's unguided fall (held by that handler, no damage on the adapter's plan); an enemy that walked out of fall.radius is missed");
                }

                foreach (int cell in waterBody)
                {
                    char letter = SpellGrammar308.LetterAt(cell);
                    Reset(); f.Aim(near); float hp = near.Hp; int lost = guided.Misses; f.Cast(letter);
                    bool held = f.Misfires.Count == 0 && f.Accepted.Count == 1 && guided.ShotsInFlight == 1;
                    float clock = Time.time; int steps = 0;
                    near.transform.position = origin - Vector3.forward * 30f + Vector3.right * 6f; Physics.SyncTransforms();   // far behind the caster
                    while (guided.ShotsInFlight > 0 && steps++ < 1200) { clock += 1f / 60f; guided.Tick(new SpellTickContext(clock, 1f / 60f, host, f.Fx)); }
                    f.Land();
                    r.Check("W11", held && near.Hp == hp && f.Hits.Count == 0 && guided.Misses == lost + 1 && guided.ShotsInFlight == 0,
                        "cell " + cell + ": its first stage is the base glyph's guided shot (held by that handler); an enemy that got far behind it is lost");
                }
                f.Wiring.ClearSpells308(SpellClearReason.Disabled);
            }
        }
    }
}
