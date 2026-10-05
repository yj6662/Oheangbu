using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 WP-04 fixtures (SPEC-SPELL-120-308): effects derived from a confirmed hit, on the real wiring. Every cast comes from
    // the resolver; the first hit lands through the wiring's own tick; the derived judgements land through the registry tick with
    // an explicit clock. The glyphs are found by their handler id (no glyph is named here). What stays for Play: the flight of
    // the derived bodies, the cues on the catalogue prefabs, real enemies moving while an ember sits.
    public static partial class Spell120Checks308
    {
        static readonly string[] WP04Sheets = { "Rules308_Base", "Rules308_WP00", "Rules308_WP04" };

        static char WP04Letter(SpellFixture308 f, string handler)
        {
            for (int i = 1; i <= SpellGrammar308.Count; i++)
                if (f.Resolver.TryRow(SpellGrammar308.LetterAt(i), out var row) && row.Handler == handler && row.Feature == SpellLegacyFeature.None) return SpellGrammar308.LetterAt(i);
            return default;
        }

        // A point at a bearing (degrees, 0 = +z, positive = right) and a distance from a centre, on the same height.
        static Vector3 WP04Polar(Vector3 centre, float bearing, float distance)
        {
            float radians = bearing * Mathf.Deg2Rad;
            return centre + new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * distance;
        }

        static partial void FixturesWP04(Report r)
        {
            // the five glyphs of the package, by grid cell: 29 (fire single, siot), 99 (water single, nieun), 86 (metal volley, giyeok),
            // 3 (wood single, nieun), 102 (water single, ieung)
            if (!r.Wants(new string(new[] { 29, 99, 86, 3, 102 }.Select(SpellGrammar308.LetterAt).ToArray()))) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var crescents = effects.OfType<CrescentEffect308>().Single(); var droplets = effects.OfType<DropletEffect308>().Single();
            var ricochet = effects.OfType<RicochetEffect308>().Single(); var spread = effects.OfType<EmberSpreadEffect308>().Single();
            var cycle = effects.OfType<CycleEffect308>().Single();
            r.Check("A10", !new ISpellEffect[] { crescents, droplets, ricochet, spread, cycle }.Any(e => e is IPlayerHitHook),
                "none of the five effects listens to the player being hit (they reward hitting, never being hit)");
            using (var f = NewFixture(r, effects, WP04Sheets))
            {
                foreach (var final in new[] { SpellFinal.Giyeok, SpellFinal.Nieun, SpellFinal.Siot, SpellFinal.Ieung }) f.Finals.Open.Add(final);
                char crescentLetter = WP04Letter(f, CrescentEffect308.HandlerId), dropletLetter = WP04Letter(f, DropletEffect308.HandlerId);
                char ricochetLetter = WP04Letter(f, RicochetEffect308.HandlerId), spreadLetter = WP04Letter(f, EmberSpreadEffect308.HandlerId);
                char cycleLetter = WP04Letter(f, CycleEffect308.HandlerId);
                if (!r.Check("W04", crescentLetter != default && dropletLetter != default && ricochetLetter != default && spreadLetter != default && cycleLetter != default,
                    "the fixture table carries the five WP-04 rows (sheet Rules308_WP04)")) return;
                var all = f.Inside.Concat(f.Outside).Concat(new[] { f.Boss }).ToArray();
                var home = all.Select(e => e.transform.position).ToArray();
                float cost = f.Config.SpellInkCost, brush = f.Brush();
                var first = f.Inside[0];
                Vector3 impact = first.transform.position;

                void Reset()
                {
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    for (int i = 0; i < all.Length; i++) all[i].transform.position = home[i];
                    Physics.SyncTransforms();
                    f.RestoreAll();
                }
                // The registry tick the wiring runs every frame, with an explicit clock (Edit Mode has no frames).
                void Tick(float ahead) => f.Registry.Tick(new SpellTickContext(Time.time + ahead, 0f, f.Wiring, f.Fx));
                void FellEveryone() { foreach (var enemy in all) enemy.TakeDamage(float.MaxValue); }
                int HitsOn(EnemyVitals enemy, int from) => f.Hits.Skip(from).Count(h => h.Target == enemy);

                // =========================== crescents ===========================
                {
                    f.Resolver.TryRow(crescentLetter, out var row);
                    float angle = row.F("split.angle", 0f), range = row.F("split.range", 0f), share = row.F("split.power", 0f), delay = row.F("split.delay", 0f);
                    Reset();
                    var right = f.Inside[1]; var left = f.Inside[2]; var between = f.Boss;
                    right.transform.position = WP04Polar(impact, angle, range * .5f);
                    left.transform.position = WP04Polar(impact, -angle, range * .5f);
                    between.transform.position = WP04Polar(impact, 0f, range * .5f);
                    Physics.SyncTransforms();
                    f.Aim(first); float ink = f.Ink.Value; int begun = f.Fx.Begun;
                    f.Cast(crescentLetter);
                    bool planned = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && f.Plans[0].Hits[0].Target == first;
                    float power = planned ? f.Plans[0].Hits[0].Power : 0f;
                    r.Check("낫-1", f.Misfires.Count == 0 && f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && planned && Near(power, row.BasePower * brush) &&
                        crescents.Waiting == 1 && crescents.Flying == 0 && f.Fx.Begun == begun + 1,
                        "cast: one spell cost, one scheduled hit of base power x brush, the effect waits for that hit, one presentation begun; nothing derived yet");
                    float rightHp = right.Hp, leftHp = left.Hp, betweenHp = between.Hp, firstHp = first.Hp;
                    f.Land();
                    r.Check("낫-1", f.Hits.Count == 1 && f.Hits[0].Target == first && crescents.Waiting == 0 && crescents.Flying == 2 && right.Hp == rightHp && left.Hp == leftHp,
                        "the confirmed hit queues two crescent judgements (one enemy in each cone); they have not landed yet");
                    if (delay > 0f) { Tick(delay * .5f); r.Check("낫-1", crescents.Flying == 2 && f.Hits.Count == 1, "before split.delay has passed nothing more lands"); }
                    Tick(delay + 1f);
                    r.Check("낫-2", f.Hits.Count == 3 && HitsOn(right, 1) == 1 && HitsOn(left, 1) == 1 && crescents.Flying == 0,
                        "two secondary judgements land: one on the enemy in the right cone, one on the enemy in the left cone");
                    r.Check("낫-3", Near(rightHp - right.Hp, power * share) && Near(leftHp - left.Hp, power * share) && between.Hp == betweenHp && Near(firstHp - first.Hp, power) &&
                        f.Outside.All(e => e.Hp == e.MaxHp),
                        "each takes the hit's power x split.power; the enemy straight ahead (between the cones), the struck enemy and everyone outside take nothing more");
                    r.Check("낫-4", f.Hits.Skip(1).All(h => h.Attack.Element == Element.Fire && h.Attack.Source == DamageSource.PlayerDirect && h.Attack.AttackId != f.Hits[0].Attack.AttackId) &&
                        f.Hits.Select(h => h.Attack.AttackId).Distinct().Count() == 3,
                        "the crescents are fire, player-direct, and each has an attack identity of its own");
                    r.Check("A10", right.Groggy.Value01 == 0f && left.Groggy.Value01 == 0f && first.Groggy.Value01 == 0f, "no groggy from the shot or its crescents");

                    // nobody aimed at: ink is spent, nothing is watched, nothing is derived
                    Reset(); FellEveryone(); f.ClearLog(); ink = f.Ink.Value; begun = f.Fx.Begun;
                    f.Cast(crescentLetter); Tick(100f); Tick(200f);
                    r.Check("낫-1", f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 0 && crescents.Waiting == 0 && crescents.Flying == 0 &&
                        f.Hits.Count == 0 && f.Fx.Begun == begun, "a shot into the air costs its ink and derives nothing");
                    // the target falls before the shot arrives: no hit, so no crescents
                    Reset();
                    right.transform.position = WP04Polar(impact, angle, range * .5f); Physics.SyncTransforms();
                    f.Aim(first); f.Cast(crescentLetter); first.TakeDamage(float.MaxValue); rightHp = right.Hp;
                    f.Land(); Tick(100f); Tick(200f);
                    r.Check("낫-1", f.Hits.Count == 0 && crescents.Waiting == 0 && crescents.Flying == 0 && right.Hp == rightHp,
                        "the target fell before the shot arrived: no hit, the watch is dropped, no crescent");
                }

                // =========================== droplets (and the wall) ===========================
                {
                    f.Resolver.TryRow(dropletLetter, out var row);
                    int count = Mathf.RoundToInt(row.F("drop.count", 0f)); float radius = row.F("drop.radius", 0f), share = row.F("drop.power", 0f);
                    Reset();
                    // independent expectation: living enemies other than the struck one inside the radius, nearest first
                    var near = all.Where(e => e != first && AreaGeometry.Flat(e.transform.position - impact).magnitude <= radius)
                        .OrderBy(e => AreaGeometry.Flat(e.transform.position - impact).magnitude).Take(count).ToArray();
                    f.Aim(first); f.Cast(dropletLetter);
                    float power = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 ? f.Plans[0].Hits[0].Power : 0f;
                    var before = all.Select(e => e.Hp).ToArray();
                    f.Land();
                    r.Check("안-2", near.Length == count && f.Hits.Count == 1 && droplets.Flying == count, "the confirmed hit breaks into drop.count droplets (" + count + ")");
                    Tick(100f);
                    bool each = near.All(e => HitsOn(e, 1) == 1 && Near(before[System.Array.IndexOf(all, e)] - e.Hp, power * share));
                    r.Check("안-3", f.Hits.Count == 1 + count && each && HitsOn(first, 1) == 0 && all.Where(e => e != first && !near.Contains(e)).All(e => e.Hp == before[System.Array.IndexOf(all, e)]),
                        "each droplet lands on a different nearby enemy with the hit's power x drop.power; the struck enemy and the enemies out of reach take nothing more");
                    r.Check("안-4", f.Hits.Skip(1).All(h => h.Attack.Element == Element.Water && h.Attack.Source == DamageSource.PlayerDirect),
                        "the droplets are water and player-direct");
                    r.Check("A10", near.All(e => e.Groggy.Value01 == 0f && e.WeakPointElementMask == 0), "droplets add no groggy");

                    // wall between the point of impact and one of the chased enemies: that droplet is stopped, the others land
                    Reset();
                    SpellFixture308.Set(f.Wiring, "_environmentOcclusion", true);
                    var blocked = near[1]; Vector3 toBlocked = AreaGeometry.Flat(blocked.transform.position - impact);
                    // The droplet's line of sight is asked of the wiring's OWN scene physics (GiyeokTargetVisible ->
                    // ScenePhysicsQuery.RaycastAll(gameObject.scene, ...)), so the wall stands in the fixture scene.
                    var wall = f.Obj("Spell308_Wall", impact + toBlocked * .5f + Vector3.up * .4f);
                    wall.transform.rotation = Quaternion.LookRotation(toBlocked.normalized, Vector3.up);
                    wall.transform.localScale = new Vector3(1f, 2f, .2f);
                    wall.AddComponent<BoxCollider>(); Physics.SyncTransforms();
                    try
                    {
                        f.Aim(first); f.Cast(dropletLetter); before = all.Select(e => e.Hp).ToArray();
                        f.Land(); Tick(100f);
                        r.Check("안-3", f.Hits.Count == count && blocked.Hp == before[System.Array.IndexOf(all, blocked)] &&
                            near.Where(e => e != blocked).All(e => HitsOn(e, 1) == 1),
                            "wall between the impact and one chased enemy: that droplet is stopped (line of sight from the point of impact), the others land");
                    }
                    finally { Object.DestroyImmediate(wall); SpellFixture308.Set(f.Wiring, "_environmentOcclusion", false); Physics.SyncTransforms(); }
                }

                // =========================== ricochet ===========================
                {
                    f.Resolver.TryRow(ricochetLetter, out var row);
                    float keep = row.F("bounce.power", 0f);
                    Reset();
                    // only the first enemy stands inside the volley's fan; three more stand behind the caster, each exactly 5 m from the first
                    var others = new[] { f.Inside[1], f.Inside[2], f.Boss };
                    others[0].transform.position = impact + new Vector3(-3f, 0f, -4f);
                    others[1].transform.position = impact + new Vector3(3f, 0f, -4f);
                    others[2].transform.position = impact + new Vector3(0f, 0f, -5f);
                    Physics.SyncTransforms();
                    f.Aim(first); float ink = f.Ink.Value; f.Cast(ricochetLetter);
                    int shots = f.Plans.Count == 1 ? f.Plans[0].Hits.Count : 0;
                    float shotPower = shots > 0 ? f.Plans[0].Hits[0].Power : 0f;
                    r.Check("속-1", f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && shots > 0 && f.Plans[0].Hits.All(h => h.Target == first) && ricochet.Waiting == shots &&
                        f.Plans[0].Area != null && f.Plans[0].Area.ShotCount == row.VolleyShots,
                        "cast: the base volley (" + row.VolleyShots + " shots), " + shots + " of them cross the only enemy in the fan; each scheduled shot is watched");
                    var before = others.Select(e => e.Hp).ToArray(); float firstHp = first.Hp;
                    f.Land();
                    r.Check("속-1", f.Hits.Count == shots && ricochet.Waiting == 0 && ricochet.Flying == shots, "every shot that hit queues one bounce; nothing else does");
                    Tick(100f);
                    int[] got = others.Select(e => HitsOn(e, shots)).ToArray();
                    r.Check("속-2", f.Hits.Count == shots * 2 && HitsOn(first, shots) == 0 && Near(firstHp - first.Hp, shotPower * shots),
                        "the bounces land on other enemies: the enemy the shots hit takes no bounce");
                    r.Check("속-2", got[0] == (shots + 2) / 3 && got[1] == (shots + 1) / 3 && got[2] == shots / 3,
                        "the bounces are dealt over the three enemies in reach, round-robin by shot (" + got[0] + " / " + got[1] + " / " + got[2] + ")");
                    r.Check("속-3", ricochet.Flying == 0 && Enumerable.Range(0, others.Length).All(i => Near(before[i] - others[i].Hp, shotPower * keep * got[i])) &&
                        f.Hits.Skip(shots).All(h => h.Attack.Element == Element.Metal && h.Attack.Source == DamageSource.PlayerDirect),
                        "each bounce carries its shot's power x bounce.power, once (bounce.count 1): a bounced shot does not bounce again");
                    Tick(200f);
                    r.Check("속-3", f.Hits.Count == shots * 2, "nothing more lands afterwards");
                }

                // =========================== spreading ember ===========================
                {
                    f.Resolver.TryRow(spreadLetter, out var row);
                    float timer = row.F("spread.timer", 0f), radius = row.F("spread.radius", 0f);
                    EnemyVitals Nearest(EnemyVitals from) => all.Where(e => e != from && e.IsAlive && AreaGeometry.Flat(e.transform.position - from.transform.position).magnitude <= radius)
                        .OrderBy(e => AreaGeometry.Flat(e.transform.position - from.transform.position).magnitude).FirstOrDefault();
                    // time: the ember sits, then moves to the nearest other enemy with the same damage, twice
                    Reset(); f.Aim(first); f.Cast(spreadLetter);
                    float power = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 ? f.Plans[0].Hits[0].Power : 0f;
                    f.Land();
                    r.Check("간-1", f.Hits.Count == 1 && spread.Embers == 1 && spread.Waiting == 0, "the confirmed hit plants one ember");
                    Tick(timer * .5f);
                    r.Check("간-3", f.Hits.Count == 1 && spread.Embers == 1, "while the carrier stands and the time is not up the ember deals nothing");
                    var second = Nearest(first); float secondHp = second != null ? second.Hp : 0f;
                    Tick(timer + .5f);
                    r.Check("간-3", second != null && f.Hits.Count == 2 && f.Hits[1].Target == second && spread.Embers == 1, "when spread.timer has passed the ember moves on");
                    r.Check("간-4", second != null && f.Hits.Count == 2 && f.Hits[1].Target == second, "it moves to the nearest other enemy inside spread.radius");
                    r.Check("간-5", second != null && Near(secondHp - second.Hp, power) && f.Hits[1].Attack.Element == Element.Wood && f.Hits[1].Attack.Source == DamageSource.PlayerDirect,
                        "the move deals the damage of the hit that planted it (the same power), wood, player-direct");
                    var third = second != null ? Nearest(second) : null; float thirdHp = third != null ? third.Hp : 0f;
                    Tick(timer * 2f + 1f);
                    r.Check("간-5", third != null && f.Hits.Count == 3 && f.Hits[2].Target == third && Near(thirdHp - third.Hp, power) && spread.Embers == 0,
                        "the second move deals the same power again and uses the ember up (spread.max 2)");
                    Tick(timer * 4f);
                    r.Check("간-5", f.Hits.Count == 3, "no third move");

                    // the carrier falls: the ember moves on the next tick, without waiting
                    Reset(); f.Aim(first); f.Cast(spreadLetter); f.Land();
                    second = Nearest(first); secondHp = second != null ? second.Hp : 0f;
                    first.TakeDamage(float.MaxValue); Tick(0f);
                    r.Check("간-2", second != null && f.Hits.Count == 2 && f.Hits[1].Target == second && Near(secondHp - second.Hp, power),
                        "the carrier falls: the ember moves at once to the nearest other enemy, with the same damage");

                    // the planting hit itself fells the target: the move still carries the full power of that hit, not what was left of the target
                    Reset(); first.TakeDamage(first.MaxHp - 1f); f.Aim(first); f.Cast(spreadLetter);
                    second = Nearest(first); secondHp = second != null ? second.Hp : 0f;
                    f.Land(); Tick(0f);
                    r.Check("간-2", !first.IsAlive && second != null && f.Hits.Count == 2 && f.Hits[0].Killed && f.Hits[1].Target == second && Near(secondHp - second.Hp, power),
                        "the planting hit felled its target: the ember moves on the next tick and deals the planted power (" + power.ToString("0.###") + "), not the 1 hp that was left");

                    // nobody in reach: the ember goes out without damage
                    Reset();
                    for (int i = 1; i < all.Length; i++) all[i].transform.position = impact + new Vector3(radius + 20f + i, 0f, 0f);
                    Physics.SyncTransforms();
                    f.Aim(first); f.Cast(spreadLetter); f.Land(); Tick(timer + .5f);
                    r.Check("간-4", f.Hits.Count == 1 && spread.Embers == 0, "nobody inside spread.radius: the ember goes out and deals nothing");
                }

                // =========================== cycle shot ===========================
                {
                    f.Resolver.TryRow(cycleLetter, out var row);
                    float back = row.F("return.time", 0f), refund = row.F("refund.ink", 0f), hp01 = row.F("heal.hp01", 0f);
                    Reset(); f.Player.TakeDamage(20f);
                    float hp = f.Player.Hp01 * f.Player.MaxHp, ink = f.Ink.Value;
                    f.Aim(first); f.Cast(cycleLetter); f.Land();
                    r.Check("앙-1", f.Hits.Count == 1 && cycle.Returning == 1 && Near(ink - f.Ink.Value, cost) && Near(f.Player.Hp01 * f.Player.MaxHp, hp),
                        "the confirmed hit sends the shot back; nothing is returned yet");
                    if (back > 0f) { Tick(back * .5f); r.Check("앙-1", cycle.Returning == 1 && Near(ink - f.Ink.Value, cost), "before return.time has passed nothing is returned"); }
                    Tick(back + .1f);
                    r.Check("앙-2", cycle.Returning == 0 && Near(f.Ink.Value, ink - cost + refund) && refund < cost && f.Ink.Value < ink,
                        "on arrival the ink comes back in part: refund.ink (" + refund + ") below the cast cost (" + cost + "), so the cast still costs ink");
                    r.Check("앙-3", Near(f.Player.Hp01 * f.Player.MaxHp, hp + f.Player.MaxHp * hp01), "and health is restored once: maximum x heal.hp01");
                    Tick(back + 50f);
                    r.Check("앙-3", Near(f.Ink.Value, ink - cost + refund) && Near(f.Player.Hp01 * f.Player.MaxHp, hp + f.Player.MaxHp * hp01), "only once: later ticks return nothing more");

                    // a shot into the air returns nothing
                    Reset(); FellEveryone(); f.Player.TakeDamage(20f); hp = f.Player.Hp01 * f.Player.MaxHp; ink = f.Ink.Value;
                    f.Cast(cycleLetter); Tick(back + 50f);
                    r.Check("앙-1", cycle.Waiting == 0 && cycle.Returning == 0 && Near(ink - f.Ink.Value, cost) && Near(f.Player.Hp01 * f.Player.MaxHp, hp),
                        "a shot into the air: the ink is spent, nothing comes back");

                    // nothing returns to a caster who fell
                    Reset(); f.Aim(first); f.Cast(cycleLetter); f.Land(); ink = f.Ink.Value;
                    f.Player.ApplyFatalFall(); Tick(back + .1f);
                    r.Check("앙-3", f.Player.Hp01 == 0f && f.Ink.Value == ink, "the caster fell before the shot came back: no health, no ink (no revival)");
                    f.Player.Restore();
                    f.Cast(cycleLetter); f.Land(); int waiting = cycle.Returning;
                    f.Wiring.ClearSpells308(SpellClearReason.PlayerDied);
                    r.Check("A6", waiting == 1 && cycle.Returning == 0 && cycle.Waiting == 0 && crescents.Flying == 0 && droplets.Flying == 0 && ricochet.Flying == 0 && spread.Embers == 0,
                        "ClearSpells308 drops every pending derived judgement, ember and return");
                }
                Reset();
            }
        }
    }
}
