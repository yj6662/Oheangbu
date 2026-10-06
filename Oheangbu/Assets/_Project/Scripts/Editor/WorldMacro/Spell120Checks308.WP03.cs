using System;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 WP-03 fixtures (SPEC-SPELL-120-308): the five two-stage attack glyphs on the real wiring. Every cast comes from the
    // resolver. The primary lands through the wiring's own tick (SpellFixture308.Land); a second stage is driven with an
    // explicit clock, because Edit Mode has no frame loop: an effect judges with the clock of its previous tick, so two ticks
    // with the same clock judge everything due by then. Expected sets come from AreaGeometry, not from the rules under test.
    // What stays for Play: real flight and frame timing, the stroke adapter's presentation of both stages, enemies that move.
    public static partial class Spell120Checks308
    {
        static readonly string[] WP03Sheets = { "Rules308_Base", "Rules308_WP00", "Rules308_WP03" };

        static partial void FixturesWP03(Report r)
        {
            // grid cells by index (no glyph is named): 39 = fire cone + nieun final, 51 = earth single + nieun, 54 = earth single + ieung,
            // 62 = earth path + giyeok, 110 = water path + giyeok
            char cone = SpellGrammar308.LetterAt(39), blast = SpellGrammar308.LetterAt(51), skip = SpellGrammar308.LetterAt(54);
            char spine = SpellGrammar308.LetterAt(62), tide = SpellGrammar308.LetterAt(110);
            if (!r.Wants(new string(new[] { cone, blast, skip, spine, tide }))) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var coneFx = effects.OfType<BackblastEffect308>().FirstOrDefault(); var blastFx = effects.OfType<ImpactBlastEffect308>().FirstOrDefault();
            var skipFx = effects.OfType<SkipEffect308>().FirstOrDefault(); var spineFx = effects.OfType<StalagmiteEffect308>().FirstOrDefault();
            var tideFx = effects.OfType<TideEffect308>().FirstOrDefault();
            if (!r.Check("W03", coneFx != null && blastFx != null && skipFx != null && spineFx != null && tideFx != null, "the five WP-03 effects are registered")) return;

            using (var f = NewFixture(r, effects, WP03Sheets))
            {
                var host = (ISpellCastHost)f.Wiring;
                f.Finals.Open.Add(SpellFinal.Giyeok); f.Finals.Open.Add(SpellFinal.Nieun); f.Finals.Open.Add(SpellFinal.Ieung);
                float brush = f.Brush(), cost = f.Config.SpellInkCost;
                Vector3 origin = SpellFixture308.Origin, forward = Vector3.forward;
                var everyone = f.Inside.Concat(f.Outside).Concat(new[] { f.Boss }).ToArray();
                var home = everyone.Select(e => e.transform.position).ToArray();
                var slots = Enumerable.Range(0, everyone.Length).ToArray();
                int Slot(EnemyVitals enemy) => Array.IndexOf(everyone, enemy);
                float[] Hp() => everyone.Select(e => e.Hp).ToArray();
                bool Same(EnemyVitals[] a, EnemyVitals[] b) => a.OrderBy(Slot).SequenceEqual(b.OrderBy(Slot));
                void Reset()
                {
                    for (int i = 0; i < everyone.Length; i++) everyone[i].transform.position = home[i];
                    Physics.SyncTransforms();
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    f.RestoreAll(); f.Aim(null);
                }
                void Judge(ISpellEffect effect, float clock)
                {
                    var ctx = new SpellTickContext(clock, 0f, host, f.Fx);
                    effect.Tick(ctx); effect.Tick(ctx);
                }

                // ---- cone + burst at the far end ----
                f.Resolver.TryRow(cone, out var coneRow);
                {
                    float range = f.Config.AreaConeRange, angle = f.Config.AreaConeAngle;
                    float radius = coneRow.F("blast.radius", 0f), delay = coneRow.F("blast.delay", 0f), scale = coneRow.F("blast.power", 0f), power = coneRow.BasePower * brush;
                    Vector3 end = origin + forward * range;
                    void Layout()
                    {
                        // two targets are moved for this scenario: one beyond the cone's range (burst only), one just inside its far end (cone and burst)
                        Reset();
                        f.Outside[1].transform.position = end + forward * (radius * .5f);
                        f.Outside[2].transform.position = end - forward * (radius * .5f);
                        Physics.SyncTransforms();
                    }
                    Layout();
                    var inCone = everyone.Where(e => AreaGeometry.InCone(origin, forward, e.transform.position, angle, range, out _, out _)).ToArray();
                    var inBurst = everyone.Where(e => AreaGeometry.InCircle(end, e.transform.position, radius, out _)).ToArray();
                    float ink = f.Ink.Value, castAt = Time.time;
                    f.Cast(cone);
                    bool planned = f.Plans.Count == 1 && f.Plans[0].Area != null && f.Plans[0].Area.Shape == AreaShape.Cone;
                    r.Check("논-3", f.Misfires.Count == 0 && f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && planned &&
                        Same(f.Plans[0].Hits.Select(h => h.Target).ToArray(), inCone) && f.PendingCount == inCone.Length && coneFx.PendingStages == 1,
                        "the cast is the base glyph's cone (one spell cost, one scheduled hit per enemy in the cone); the burst waits as a second stage");
                    if (planned)
                    {
                        float primaryAt = castAt + f.Plans[0].Area.Delay;
                        var before = Hp();
                        f.Land();
                        r.Check("논-3", f.Hits.Count == inCone.Length && slots.All(i => Near(before[i] - everyone[i].Hp, inCone.Contains(everyone[i]) ? power : 0f)),
                            "when the cone lands only the cone hits apply");
                        Judge(coneFx, primaryAt + delay * .5f);
                        r.Check("논-3", f.Hits.Count == inCone.Length && coneFx.PendingStages == 1, "nothing bursts before blast.delay has passed");
                        var mid = Hp(); int landed = f.Hits.Count;
                        Judge(coneFx, primaryAt + delay + .1f);
                        var burst = f.Hits.Skip(landed).ToArray();
                        r.Check("논-1", Same(burst.Select(h => h.Target).ToArray(), inBurst) && slots.All(i => Near(mid[i] - everyone[i].Hp, inBurst.Contains(everyone[i]) ? power * scale : 0f)) &&
                            inBurst.Contains(f.Outside[1]) && !inCone.Contains(f.Outside[1]) && inBurst.Contains(f.Outside[2]) && inCone.Contains(f.Outside[2]),
                            "the burst hits the enemies inside blast.radius of the cone's end point, once each: one of them stands beyond the cone's range, one was also hit by the cone");
                        r.Check("논-2", scale > 1f && burst.Length > 0 && burst.All(h => h.AppliedDamage > power), "every burst hit is bigger than a cone hit (cast power x blast.power)");
                        r.Check("논-3", burst.Length > 0 && burst.Select(h => h.Attack.AttackId).Distinct().Count() == 1 && burst.All(h => h.Attack.Source == DamageSource.PlayerDirect && h.Attack.Element == coneRow.Element) &&
                            f.Plans[0].Hits.All(h => h.AttackId != burst[0].Attack.AttackId) && coneFx.PendingStages == 0,
                            "the burst is a direct player hit of the cast's element with one attack identity of its own");
                        Judge(coneFx, primaryAt + delay + 5f);
                        r.Check("논-3", f.Hits.Count == landed + burst.Length, "the burst happens once");

                        // an enemy the cone kills gets no burst
                        Layout();
                        f.Outside[2].TakeDamage(f.Outside[2].Hp - power * .5f);
                        castAt = Time.time; f.Cast(cone); f.Land();
                        Judge(coneFx, castAt + f.Plans[0].Area.Delay + delay + .1f);
                        r.Check("논-3", !f.Outside[2].IsAlive && f.Hits.Count(h => h.Target == f.Outside[2]) == 1 && f.Hits.Count(h => h.Target == f.Outside[1]) == 1,
                            "an enemy the cone killed gets no burst; the burst still reaches the others");

                        // a cleared effect (death, rest, scene leave, disable) drops its pending second stage
                        Layout();
                        castAt = Time.time; f.Cast(cone); f.Land(); landed = f.Hits.Count;
                        f.Wiring.ClearSpells308(SpellClearReason.PlayerDied);
                        Judge(coneFx, castAt + f.Plans[0].Area.Delay + delay + .1f);
                        r.Check("논-3", coneFx.PendingStages == 0 && f.Hits.Count == landed, "after ClearSpells308 no burst follows");
                    }
                }

                // ---- single shot + burst at the impact point ----
                f.Resolver.TryRow(blast, out var blastRow);
                {
                    float radius = blastRow.F("blast.radius", 0f), scale = blastRow.F("blast.power", 0f), power = blastRow.BasePower * brush;
                    var target = f.Inside[0];
                    Reset(); f.Aim(target);
                    f.Cast(blast);
                    bool planned = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && f.Plans[0].Hits[0].Target == target;
                    r.Check("만-1", planned && f.PendingCount == 1 && blastFx.PendingStages == 1,
                        "the cast is the base glyph's single shot: one scheduled hit. The burst is not scheduled; it waits for the impact");
                    // the target walks while the shot flies: the burst is centred where it stands at the impact
                    target.transform.position = home[Slot(target)] + Vector3.right;
                    Physics.SyncTransforms();
                    var near = everyone.Where(e => e != target && AreaGeometry.InCircle(target.transform.position, e.transform.position, radius, out _)).ToArray();
                    var before = Hp();
                    f.Land();
                    r.Check("만-1", f.Hits.Count == 1 + near.Length && f.Hits.Count > 1 && f.Hits[0].Target == target && blastFx.PendingStages == 0,
                        "the burst is judged in the same call as the primary's impact, with no tick in between");
                    r.Check("만-2", near.Length > 0 && slots.All(i => Near(before[i] - everyone[i].Hp, everyone[i] == target ? power : near.Contains(everyone[i]) ? power * scale : 0f)),
                        "the enemies inside blast.radius of the impact point take cast power x blast.power once; the target takes the primary only");
                    r.Check("만-2", f.Hits.Count > 1 && f.Hits.Skip(1).Select(h => h.Attack.AttackId).Distinct().Count() == 1 && f.Hits[1].Attack.AttackId != f.Hits[0].Attack.AttackId &&
                        f.Hits.Skip(1).All(h => h.Attack.Source == DamageSource.PlayerDirect && h.Attack.Element == blastRow.Element),
                        "the burst is a direct player hit with one attack identity of its own");

                    // the target is gone before the shot lands: nothing lands, nothing bursts
                    Reset(); f.Aim(target);
                    f.Cast(blast);
                    float impactAt = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 ? f.Plans[0].Hits[0].ImpactTime : Time.time;
                    target.TakeDamage(target.Hp + 1f);
                    before = Hp();
                    f.Land();
                    r.Check("만-2", f.Hits.Count == 0 && slots.All(i => before[i] == everyone[i].Hp) && blastFx.PendingStages == 1, "a shot whose target died first lands on nothing and bursts nothing");
                    Judge(blastFx, impactAt + 1f);
                    r.Check("만-2", blastFx.PendingStages == 0 && f.Hits.Count == 0, "the unused burst record is dropped once its impact time has passed");
                }

                // ---- single shot + skip to a second point ----
                f.Resolver.TryRow(skip, out var skipRow);
                {
                    float distance = skipRow.F("skip.distance", 0f), delay = skipRow.F("skip.delay", 0f), radius = skipRow.F("skip.radius", 0f), scale = skipRow.F("skip.power", 0f);
                    float power = skipRow.BasePower * brush;
                    var target = f.Inside[0];
                    Reset();
                    Vector3 second = target.transform.position + AreaGeometry.Flat(target.transform.position - origin).normalized * distance;
                    f.Outside[1].transform.position = second;          // one target is moved onto the second point
                    Physics.SyncTransforms();
                    var near = everyone.Where(e => AreaGeometry.InCircle(second, e.transform.position, radius, out _)).ToArray();
                    f.Aim(target); f.Cast(skip);
                    float landedAt = Time.time;
                    f.Land();
                    r.Check("망-1", f.Hits.Count == 1 && f.Hits[0].Target == target && Near(f.Hits[0].AppliedDamage, power) && skipFx.PendingStages == 1,
                        "the primary lands alone; the second landing is pending");
                    Judge(skipFx, landedAt + delay * .5f);
                    r.Check("망-1", f.Hits.Count == 1 && skipFx.PendingStages == 1, "nothing lands a second time before skip.delay has passed");
                    var mid = Hp();
                    Judge(skipFx, landedAt + delay + .1f);
                    var again = f.Hits.Skip(1).ToArray();
                    r.Check("망-2", near.Contains(f.Outside[1]) && !near.Contains(target) && Same(again.Select(h => h.Target).ToArray(), near),
                        "the second landing is skip.distance beyond the impact point, along the direction the shot travelled: the enemies around that point are the ones hit");
                    r.Check("망-3", again.Length > 0 && slots.All(i => Near(mid[i] - everyone[i].Hp, near.Contains(everyone[i]) ? power * scale : 0f)) &&
                        again.Select(h => h.Attack.AttackId).Distinct().Count() == 1 && again[0].Attack.AttackId != f.Hits[0].Attack.AttackId && skipFx.PendingStages == 0,
                        "each enemy inside skip.radius of the second point takes cast power x skip.power once, under one attack identity of its own");
                    Judge(skipFx, landedAt + delay + 5f);
                    r.Check("망-3", f.Hits.Count == 1 + again.Length, "the second landing happens once");
                }

                // ---- path + a second front trailing the storm front ----
                f.Resolver.TryRow(spine, out var spineRow); f.Resolver.TryRow(SpellGrammar308.BaseOf(spine), out var spineBase);
                {
                    float lag = spineRow.F("second.delay", 0f), scale = spineRow.F("second.power", 0f), power = spineRow.BasePower * brush;
                    Reset(); f.Aim(f.Inside[0]);
                    float castAt = Time.time;
                    f.Cast(spine);
                    var area = f.Plans.Count == 1 ? f.Plans[0].Area : null;
                    bool planned = area != null && area.Shape == AreaShape.Path && spineBase != null;
                    if (r.Check("목-1", planned && area.Radius == spineBase.AreaRadius && area.Length == spineBase.AreaLength && area.Speed == spineBase.AreaSpeed &&
                        area.Delay == spineBase.AreaImpactDelay && spineRow.BasePower == spineBase.BasePower, "the cast is the base glyph's corridor: same half width, length, speed, delay and base power"))
                    {
                        float AlongOf(EnemyVitals e) { AreaGeometry.InCorridor(area.Point, area.Direction, e.transform.position, area.Radius, area.Length, out float along, out _); return along; }
                        var inPath = everyone.Where(e => AreaGeometry.InCorridor(area.Point, area.Direction, e.transform.position, area.Radius, area.Length, out _, out _)).ToArray();
                        r.Check("목-1", Same(f.Plans[0].Hits.Select(h => h.Target).ToArray(), inPath) && f.PendingCount == inPath.Length && inPath.Length >= 3 && spineFx.PendingStages == 1,
                            "one scheduled storm hit per enemy in the corridor; the second front waits as a second stage");
                        f.Land();
                        int landed = f.Hits.Count;
                        float frontStartsAt = castAt + area.Delay + lag;
                        Judge(spineFx, frontStartsAt - .1f);
                        r.Check("목-2", landed == inPath.Length && f.Hits.Count == landed && lag > 0f, "the second front has not started second.delay after the storm: nothing more lands");
                        // the cut lies between two targets of the fixture (4.5 m and 6 m along the corridor)
                        const float cut = 5.25f;
                        var nearPart = inPath.Where(e => AlongOf(e) < cut).ToArray();
                        var mid = Hp();
                        Judge(spineFx, frontStartsAt + cut / area.Speed);
                        r.Check("목-2", nearPart.Length > 0 && nearPart.Length < inPath.Length && Same(f.Hits.Skip(landed).Select(h => h.Target).ToArray(), nearPart),
                            "the second front follows the storm front along the corridor: part-way through, only the near enemies have been hit a second time");
                        // an enemy that leaves the corridor before the second front reaches it is spared
                        var leaver = inPath.OrderByDescending(AlongOf).First();
                        leaver.transform.position = leaver.transform.position + Vector3.right * 40f;
                        Physics.SyncTransforms();
                        Judge(spineFx, frontStartsAt + area.Length / area.Speed + .1f);
                        var second = f.Hits.Skip(landed).ToArray();
                        var expected = inPath.Where(e => e != leaver).ToArray();
                        r.Check("목-3", Same(second.Select(h => h.Target).ToArray(), expected) && slots.All(i => Near(mid[i] - everyone[i].Hp, expected.Contains(everyone[i]) ? power * scale : 0f)) &&
                            !nearPart.Contains(leaver) && spineFx.PendingStages == 0,
                            "every enemy standing in the corridor when the second front reaches it takes cast power x second.power once; the one that left takes nothing");
                        r.Check("목-3", second.Length > 0 && second.Select(h => h.Attack.AttackId).Distinct().Count() == 1 && f.Plans[0].Hits.All(h => h.AttackId != second[0].Attack.AttackId) &&
                            second.All(h => h.Attack.Source == DamageSource.PlayerDirect), "the second front is a direct player hit with one attack identity of its own");
                        Judge(spineFx, frontStartsAt + area.Length / area.Speed + 5f);
                        r.Check("목-3", f.Hits.Count == landed + second.Length, "the second front passes once");
                    }
                }

                // ---- path + returning front ----
                f.Resolver.TryRow(tide, out var tideRow); f.Resolver.TryRow(SpellGrammar308.BaseOf(tide), out var tideBase);
                {
                    float pause = tideRow.F("return.pause", 0f), speedScale = tideRow.F("return.speed", 0f), scale = tideRow.F("return.power", 0f), power = tideRow.BasePower * brush;
                    Reset();
                    var doomed = f.Inside[2];
                    doomed.TakeDamage(doomed.Hp - power * .5f);          // the way out kills this one
                    f.Aim(f.Inside[0]);
                    float castAt = Time.time;
                    f.Cast(tide);
                    var area = f.Plans.Count == 1 ? f.Plans[0].Area : null;
                    bool planned = area != null && area.Shape == AreaShape.Path && tideBase != null;
                    if (r.Check("옥-1", planned && area.Radius == tideBase.AreaRadius && area.Length == tideBase.AreaLength && area.Speed == tideBase.AreaSpeed &&
                        area.Delay == tideBase.AreaImpactDelay && tideRow.BasePower == tideBase.BasePower, "the way out is the base glyph's corridor: same half width, length, speed, delay and base power"))
                    {
                        float AlongOf(EnemyVitals e) { AreaGeometry.InCorridor(area.Point, area.Direction, e.transform.position, area.Radius, area.Length, out float along, out _); return along; }
                        var inPath = everyone.Where(e => AreaGeometry.InCorridor(area.Point, area.Direction, e.transform.position, area.Radius, area.Length, out _, out _)).ToArray();
                        f.Land();
                        int landed = f.Hits.Count;
                        r.Check("옥-1", Same(f.Plans[0].Hits.Select(h => h.Target).ToArray(), inPath) && landed == inPath.Length && !doomed.IsAlive && inPath.Contains(doomed) && tideFx.PendingStages == 1,
                            "one way-out hit per enemy in the corridor (one of them dies of it); the returning front waits as a second stage");
                        float backSpeed = area.Speed * speedScale;
                        float backStartsAt = castAt + area.Delay + area.Length / area.Speed + pause;
                        Judge(tideFx, backStartsAt - .1f);
                        r.Check("옥-2", f.Hits.Count == landed, "the wave does not come back before it has reached the far end and paused");
                        // the returning front has come back to 5.25 m from the caster: between two targets of the fixture
                        const float cut = 5.25f;
                        var living = inPath.Where(e => e != doomed).ToArray();
                        var farPart = living.Where(e => AlongOf(e) > cut).ToArray();
                        Judge(tideFx, backStartsAt + (area.Length - cut) / backSpeed);
                        r.Check("옥-2", farPart.Length > 0 && farPart.Length < living.Length && Same(f.Hits.Skip(landed).Select(h => h.Target).ToArray(), farPart),
                            "the front runs from the far end towards the caster: part-way back, only the far enemies have been hit a second time");
                        Judge(tideFx, backStartsAt + area.Length / backSpeed + .1f);
                        var back = f.Hits.Skip(landed).ToArray();
                        r.Check("옥-3", Same(back.Select(h => h.Target).ToArray(), living) && back.All(h => Near(h.AppliedDamage, power * scale)) &&
                            slots.All(i => f.Hits.Count(h => h.Target == everyone[i]) == (living.Contains(everyone[i]) ? 2 : everyone[i] == doomed ? 1 : 0)) && tideFx.PendingStages == 0,
                            "two hits per enemy in the corridor (one out, one back), one for the enemy the way out killed, none outside");
                        r.Check("옥-3", back.Length > 0 && back.Select(h => h.Attack.AttackId).Distinct().Count() == 1 && f.Plans[0].Hits.All(h => h.AttackId != back[0].Attack.AttackId) &&
                            back.All(h => h.Attack.Source == DamageSource.PlayerDirect && h.Attack.Element == tideRow.Element), "the way back is a direct player hit with one attack identity of its own");
                        Judge(tideFx, backStartsAt + area.Length / backSpeed + 5f);
                        r.Check("옥-3", f.Hits.Count == landed + back.Length, "the wave comes back once: never a third hit");
                    }
                }

                // ---- presentation independence, groggy ----
                r.Check("A9", f.Fx.Begun == 0 && f.Fx.Cues == 0, "the two-stage effects ask nothing of the presenter (the stroke adapter presents the cast); every result above came with the null presenter");
                r.Check("A10", everyone.All(e => e.Groggy.Value01 == 0f), "no second stage raised groggy");
            }
        }
    }
}
