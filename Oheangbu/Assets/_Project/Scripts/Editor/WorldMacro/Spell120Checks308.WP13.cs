using System;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 WP-13 fixtures (SPEC-SPELL-120-308, opened by D308-13 Q5): the eight glyphs that answer to something an enemy does or
    // leaves behind, on the real wiring, against TEST targets built in the fixture's own scene (a dummy projectile, a guarding
    // dummy, a hazard patch) and against strikes delivered through the real enemy -> player doorway. Every cast goes through
    // the resolver. Effects are driven with an explicit clock (Edit Mode has no frame loop). The TEST targets are born in the
    // fixture scene (SpellFixture308.Hazard / Projectile) and go with it: none of them ever stands in an open scene.
    // What stays for Play: enemies that really shoot, guard or leave hazards, the projectile of today's EnemyController being
    // stopped in flight, the look of everything (the fixtures use the null presenter).
    public static partial class Spell120Checks308
    {
        static readonly string[] WP13Sheets = { "Rules308_Base", "Rules308_WP00", "Rules308_WP11", "Rules308_WP13" };

        static partial void FixturesWP13(Report r)
        {
            // grid cells by index (no glyph is named): 18 wood o + ieung, 42 fire o + ieung, 50 earth a + giyeok, 66 earth o + ieung,
            // 77 metal a + siot, 78 metal a + ieung, 90 metal o + ieung, 111 water o + nieun, 73 = the metal single shot without a final
            char wave = SpellGrammar308.LetterAt(18), steam = SpellGrammar308.LetterAt(42), rock = SpellGrammar308.LetterAt(50), mud = SpellGrammar308.LetterAt(66);
            char thrust = SpellGrammar308.LetterAt(77), needle = SpellGrammar308.LetterAt(78), barrage = SpellGrammar308.LetterAt(90), purge = SpellGrammar308.LetterAt(111);
            char plain = SpellGrammar308.LetterAt(73);
            if (!r.Wants(new string(new[] { wave, steam, rock, mud, thrust, needle, barrage, purge }))) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var needleFx = effects.OfType<InterceptShotEffect308>().FirstOrDefault(); var barrageFx = effects.OfType<InterceptBarrageEffect308>().FirstOrDefault();
            var thrustFx = effects.OfType<UnblockableEffect308>().FirstOrDefault(); var hazeFx = effects.OfType<HazeZoneEffect308>().FirstOrDefault();
            var purgeFx = effects.OfType<PurgeEffect308>().FirstOrDefault(); var rockFx = effects.OfType<RockCoverEffect308>().FirstOrDefault();
            var waveFx = effects.OfType<WaveCoverEffect308>().FirstOrDefault();
            // the rock's first stage: the unguided fall of the earth single shot without a final (decision of 2026-10-04,
            // Spellcraft Bible chapter 4). This fixture's table carries the WP-11 sheet, so that handler holds the hit.
            var fallFx = effects.OfType<BallisticEffect308>().FirstOrDefault();
            if (!r.Check("W13", needleFx != null && barrageFx != null && thrustFx != null && hazeFx != null && purgeFx != null && rockFx != null && waveFx != null,
                "the seven WP-13 effects are registered")) return;

            using (var f = NewFixture(r, effects, WP13Sheets))
            {
                var host = (ISpellCastHost)f.Wiring;
                foreach (SpellFinal final in new[] { SpellFinal.Giyeok, SpellFinal.Nieun, SpellFinal.Siot, SpellFinal.Ieung }) f.Finals.Open.Add(final);
                float brush = f.Brush(), cost = f.Config.SpellInkCost;
                Vector3 origin = SpellFixture308.Origin, forward = Vector3.forward;
                var everyone = f.Inside.Concat(f.Outside).Concat(new[] { f.Boss }).ToArray();
                var home = everyone.Select(e => e.transform.position).ToArray();
                void Sync()
                {
                    Physics.SyncTransforms();
                    var physics = f.Scene.GetPhysicsScene();
                    if (!physics.IsValid() || physics.Equals(Physics.defaultPhysicsScene)) throw new InvalidOperationException("The fixture scene needs its own physics scene.");
                    physics.Simulate(.001f);
                }
                void Reset()
                {
                    for (int i = 0; i < everyone.Length; i++) everyone[i].transform.position = home[i];
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    f.RestoreAll(); f.Aim(null);
                    Sync();
                }
                // Nobody near the caster: a cast without a lock then goes straight ahead, and only the enemies a scenario places matter.
                void SendAway()
                {
                    for (int i = 0; i < everyone.Length; i++) everyone[i].transform.position = origin + new Vector3(300f + i * 10f, 0f, 300f);
                    Sync();
                }
                void Tick(ISpellEffect effect, float clock) => effect.Tick(new SpellTickContext(clock, 1f / 60f, host, f.Fx));
                int Strikes(EnemyVitals attacker, int count, IncomingDamageKind kind)
                {
                    int landed = 0;
                    for (int i = 0; i < count; i++) { f.Player.Restore(); if (EnemyStrike308.Deliver(f.Player, attacker, 1f, kind)) landed++; }
                    return landed;
                }

                // ---- the unblockable thrust against a guarding dummy ----
                if (r.Wants(thrust.ToString()))
                {
                    Reset();
                    var dummy = f.Inside[0];
                    var guard = dummy.gameObject.AddComponent<SpellGuardTestTarget308>();
                    guard.Configure(true, 1f);
                    f.Resolver.TryRow(thrust, out var row);
                    f.Aim(dummy); float hp = dummy.Hp; f.Cast(plain); f.Land();
                    r.Check("삿-1", dummy.Modifiers.Stance.Raised && dummy.Hp == hp && f.Hits.Count == 0, "TEST target: a guarding dummy stops the plain single shot");
                    f.ClearLog(); f.Aim(dummy); f.Cast(thrust);
                    bool told = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && dummy.Modifiers.Stance.Passes(f.Plans[0].Hits[0].AttackId);
                    f.Land();
                    r.Check("삿-1", told && Near(hp - dummy.Hp, row.BasePower * brush) && f.Hits.Count == 1 && dummy.Modifiers.Stance.Raised && thrustFx.HitsOnTheWay == 0,
                        "the thrust lands whole through the guard; the guard itself stays up and the entry is used up");
                    f.ClearLog(); hp = dummy.Hp; f.Aim(dummy); f.Cast(plain); f.Land();
                    r.Check("삿-1", dummy.Hp == hp, "the next plain shot is stopped again");
                    guard.Configure(false, 1f);
                    UnityEngine.Object.DestroyImmediate(guard);
                }

                // ---- the haze (two bodies, one act) ----
                foreach (char letter in new[] { steam, mud })
                {
                    if (!r.Wants(letter.ToString())) continue;
                    Reset(); SendAway();
                    f.Resolver.TryRow(letter, out var row);
                    float radius = row.F("haze.radius", 0f), life = row.F("haze.life", 0f), miss = row.F("haze.miss", 0f);
                    float castAt = Time.time; float ink = f.Ink.Value; f.Cast(letter);
                    bool planned = f.Plans.Count == 1 && f.Plans[0].Area != null && f.Plans[0].Area.Shape == row.AreaShape;
                    r.Check(letter + "-1", f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && planned && hazeFx.Zones.CountOf(HazeZoneEffect308.HandlerId) == 1,
                        "the cast is the base glyph's own shape (one spell cost) and leaves one haze");
                    if (!planned) continue;
                    var area = f.Plans[0].Area;
                    Vector3 centre = area.Point + area.Direction * (area.Length * .5f);
                    var zone = hazeFx.Zones.Zones.First(z => z.Owner == HazeZoneEffect308.HandlerId);
                    r.Check(letter + "-1", (zone.Centre - centre).magnitude < .01f && Near(zone.Radius, radius) && Near(zone.Until - zone.From, life) && Near(zone.From - castAt, area.Delay),
                        "the haze stands at the middle of the sprayed shape, haze.radius wide, for haze.life seconds after the shape forms");
                    var inside = f.Outside[1]; var outside = f.Outside[2];
                    inside.transform.position = centre; outside.transform.position = centre + Vector3.right * (radius + 2f); Sync();
                    float during = zone.From + .1f;
                    Tick(hazeFx, during);
                    int strikes = 20, expected = strikes - Mathf.RoundToInt(strikes * miss);
                    r.Check(letter + "-2", Strikes(inside, strikes, IncomingDamageKind.Melee) == expected && Strikes(outside, strikes, IncomingDamageKind.Melee) == strikes && hazeFx.VeiledEnemies == 1,
                        "strikes of the enemy inside the haze miss haze.miss of the time at the doorway; the enemy outside it never misses");
                    inside.transform.position = centre + Vector3.right * (radius + 2f); Sync();
                    Tick(hazeFx, during + .1f);
                    r.Check(letter + "-2", Strikes(inside, strikes, IncomingDamageKind.Melee) == strikes && hazeFx.VeiledEnemies == 0 && inside.Modifiers.IsEmpty, "an enemy that walked out strikes true again");
                    inside.transform.position = centre; Sync();
                    Tick(hazeFx, zone.Until + .1f);
                    r.Check(letter + "-1", hazeFx.Zones.CountOf(HazeZoneEffect308.HandlerId) == 0 && Strikes(inside, strikes, IncomingDamageKind.Melee) == strikes && inside.Modifiers.IsEmpty,
                        "after haze.life the haze is gone and nobody misses");
                }

                // ---- the cleansing wave against TEST hazards ----
                if (r.Wants(purge.ToString()))
                {
                    Reset(); SendAway();
                    f.Resolver.TryRow(purge, out var row);
                    var onPath = f.Hazard(origin + forward * 5f, 1f);
                    var beside = f.Hazard(origin + forward * 5f + Vector3.right * (row.AreaRadius + 3f), 1f);
                    var behind = f.Hazard(origin - forward * 4f, 1f);
                    Sync();
                    float castAt = Time.time; f.Cast(purge);
                    bool planned = f.Plans.Count == 1 && f.Plans[0].Area != null && f.Plans[0].Area.Shape == AreaShape.Path && purgeFx.WavesRunning == 1;
                    r.Check("온-1", planned && !onPath.Purged, "the cast is the base glyph's wave; nothing is wiped at cast time");
                    if (planned)
                    {
                        var area = f.Plans[0].Area;
                        float reachesAt = castAt + area.Delay + 4f / area.Speed;      // the patch's near edge is four metres along the path
                        Tick(purgeFx, reachesAt - .05f);
                        r.Check("온-1", !onPath.Purged && purgeFx.Wiped == 0, "the patch on the path is still there just before the front reaches it");
                        Tick(purgeFx, reachesAt + .05f);
                        r.Check("온-1", onPath.Purged && onPath.PurgedBy > 0 && !beside.Purged && !behind.Purged && purgeFx.Wiped == 1, "the front wipes the patch on its path when it gets there; the patches beside and behind stay");
                        Tick(purgeFx, castAt + area.Delay + area.Length / area.Speed + .1f);
                        r.Check("온-1", purgeFx.WavesRunning == 0 && !beside.Purged && !behind.Purged && purgeFx.Wiped == 1, "the wave ends at the far end of its corridor");
                    }
                }

                // ---- the rock that stays as cover ----
                if (r.Wants(rock.ToString()))
                {
                    Reset();
                    f.Resolver.TryRow(rock, out var row);
                    float radius = row.F("cover.radius", 0f), life = row.F("cover.life", 0f);
                    var target = f.Inside[0]; var shooter = f.Outside[1]; var flanker = f.Outside[2];
                    f.Aim(target); float hp = target.Hp; f.Cast(rock);
                    bool planned = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && rockFx.RocksInFlight == 1;
                    r.Check("막-1", planned && rockFx.PieceCount == 0, "the cast is the base glyph's single rock; the cover does not stand before the rock lands");
                    if (planned)
                    {
                        float landsAt = f.Plans[0].Hits[0].ImpactTime;
                        f.Land();
                        r.Check("막-1", fallFx != null && fallFx.RocksInFlight == 1 && target.Hp == hp && f.Plans[0].Hits[0].Power == 0f,
                            "the first stage is the base glyph's unguided fall: the adapter's plan carries no damage and the hit is held by that glyph's handler");
                        if (fallFx != null) Tick(fallFx, landsAt + .01f);     // the held rock lands when its own handler is ticked
                        Tick(rockFx, landsAt + .01f);
                        r.Check("막-1", Near(hp - target.Hp, row.BasePower * brush) && rockFx.PieceCount == 1 && rockFx.RocksInFlight == 0, "the rock hits its target and takes root where it landed");
                        // a shooter straight behind the rock, a flanker well to the side
                        shooter.transform.position = target.transform.position + forward * 8f; flanker.transform.position = origin + Vector3.right * 10f; Sync();
                        Tick(rockFx, landsAt + .1f);
                        r.Check("막-2", Strikes(shooter, 5, IncomingDamageKind.Ranged) == 0 && Strikes(shooter, 5, IncomingDamageKind.Melee) == 5 && Strikes(flanker, 5, IncomingDamageKind.Ranged) == 5 && rockFx.ScreenedEnemies >= 1,
                            "ranged strikes of an enemy behind the rock are stopped at the doorway; its melee strikes and the strikes of an enemy to the side are not");
                        Tick(rockFx, landsAt + life + .1f);
                        r.Check("막-2", rockFx.PieceCount == 0 && Strikes(shooter, 5, IncomingDamageKind.Ranged) == 5 && shooter.Modifiers.IsEmpty, "after cover.life the rock is gone and nothing is stopped");
                    }
                    Reset();
                    foreach (var enemy in everyone) enemy.TakeDamage(enemy.Hp);                 // nobody to aim at
                    f.ClearLog(); float clock = Time.time; f.Cast(rock);
                    Tick(rockFx, clock + 60f);
                    r.Check("막-1", f.Accepted.Count == 1 && rockFx.PieceCount == 1, "a cast without a target still drops its rock, ahead of the caster");
                }

                // ---- the wave that covers who walks behind it ----
                if (r.Wants(wave.ToString()))
                {
                    Reset();
                    f.Resolver.TryRow(wave, out var row);
                    var ahead = f.Outside[1]; var rear = f.Outside[0];
                    ahead.transform.position = origin + forward * (row.AreaLength + 6f); rear.transform.position = origin - forward * 6f; Sync();
                    f.Aim(f.Inside[0]);                                                 // straight ahead: the corridor runs along the caster's facing
                    float castAt = Time.time, ink = f.Ink.Value; f.Cast(wave);
                    bool planned = f.Plans.Count == 1 && f.Plans[0].Area != null && f.Plans[0].Area.Shape == AreaShape.Path && waveFx.PieceCount == 1;
                    r.Check("공-1", f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && planned && f.Plans[0].Hits.Count > 0 && f.Plans[0].Hits.All(h => Near(h.Power, row.BasePower * brush)),
                        "the cast is a path judgement with the row's small power: every enemy in the corridor gets one scheduled hit");
                    if (planned)
                    {
                        var area = f.Plans[0].Area;
                        float running = castAt + area.Delay + area.Length * .5f / area.Speed;
                        f.Player.transform.position = origin + forward * 1f; Sync();       // the caster walks behind the wave
                        Tick(waveFx, running);
                        r.Check("공-2", Strikes(ahead, 5, IncomingDamageKind.Ranged) == 0 && Strikes(rear, 5, IncomingDamageKind.Ranged) == 5 && Strikes(ahead, 5, IncomingDamageKind.Melee) == 5,
                            "while the wave runs, ranged strikes of an enemy ahead of it are stopped; an enemy behind the player is not cut off; melee is never stopped");
                        Tick(waveFx, castAt + area.Delay + area.Length / area.Speed + .1f);
                        r.Check("공-2", waveFx.PieceCount == 0 && Strikes(ahead, 5, IncomingDamageKind.Ranged) == 5 && ahead.Modifiers.IsEmpty, "the cover ends when the wave reaches the far end of its corridor");
                        f.Player.transform.position = origin; Sync();
                    }
                }

                // ---- the needle and the barrage against TEST projectiles ----
                if (r.Wants(needle.ToString()) || r.Wants(barrage.ToString()))
                {
                    Reset();
                    f.Resolver.TryRow(needle, out var needleRow); f.Resolver.TryRow(barrage, out var barrageRow);
                    SpellProjectileTestTarget308 Shot(Vector3 at, Vector3 velocity) => f.Projectile(at, velocity);
                    var incoming = Shot(origin + forward * 6f + Vector3.up, -forward * 6f);
                    var leaving = Shot(origin + forward * 3f + Vector3.up, forward * 6f);
                    Sync();
                    f.Aim(f.Inside[0]); float hp = f.Inside[0].Hp, clock = Time.time; f.Cast(needle);
                    r.Check("상-1", f.Accepted.Count == 1 && f.PendingCount == 0 && needleFx.NeedlesInFlight == 1 && !incoming.Frozen,
                        "with an incoming projectile in its fan the needle goes for it: no hit is scheduled on the aimed enemy");
                    Tick(needleFx, clock + 5f);
                    r.Check("상-2", incoming.Frozen && !incoming.InFlight && incoming.InterceptedBy > 0 && !leaving.Frozen && needleFx.Downed == 1 && f.Inside[0].Hp == hp,
                        "TEST target: the incoming projectile is frozen and out of the air; the one flying away is left alone");
                    f.ClearLog(); f.Aim(f.Inside[0]); f.Cast(needle);
                    r.Check("상-1", f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && Near(f.Plans[0].Hits[0].Power, needleRow.BasePower * brush) && needleFx.NeedlesInFlight == 0,
                        "with nothing left to intercept the cast is the base glyph's single shot");

                    Reset();
                    var swarm = new[] { Shot(origin + forward * 5f + Vector3.up, -forward * 4f), Shot(origin + forward * 7f + Vector3.right + Vector3.up, -forward * 4f),
                        Shot(origin + forward * 9f - Vector3.right + Vector3.up, -forward * 4f), Shot(origin - forward * 5f + Vector3.up, forward * 4f) };
                    incoming.Relaunch(origin + forward * 200f, -forward); leaving.Relaunch(origin + forward * 210f, forward);   // out of every fan
                    Sync();
                    clock = Time.time; f.Cast(barrage);
                    bool volley = f.Plans.Count == 1 && f.Plans[0].Area != null && f.Plans[0].Area.Shape == AreaShape.Volley && barrageFx.BarragesForming == 1;
                    r.Check("송-1", volley && swarm.All(s => !s.Frozen), "the cast is the base glyph's volley; nothing is shot down before the barrage forms");
                    if (volley)
                    {
                        float formsAt = clock + f.Plans[0].Area.Delay;
                        Tick(barrageFx, formsAt - .05f);
                        bool early = swarm.All(s => !s.Frozen);
                        Tick(barrageFx, formsAt + .05f);
                        r.Check("송-2", early && swarm[0].Frozen && swarm[1].Frozen && swarm[2].Frozen && !swarm[3].Frozen && barrageFx.Downed == 3 && barrageFx.BarragesForming == 0,
                            "TEST targets: at the moment the barrage forms the three projectiles in its fan are shot down together; the one behind the caster is not");
                    }
                }
            }

            // ---- the rock row is TEST-only (Gate test in its sheet; D308-13 Q5: an enemy / world dependent glyph is the spell
            // rule plus a TEST target). A normal save that earned its final opens the other rows of that final, never this one.
            if (r.Wants(rock.ToString()))
            {
                char spine = SpellGrammar308.LetterAt(62);      // earth o + giyeok: a row of the same final that the main game does open
                string[] sheets = { "Rules308_Base", "Rules308_WP00", "Rules308_WP03", "Rules308_WP11", "Rules308_WP13" };
                using (var g = NewFixture(r, effects, sheets))
                {
                    g.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    g.Resolver.TryRow(rock, out var rockRow); g.Resolver.TryRow(spine, out var spineRow);
                    g.Finals.Proven.Add(SpellFinal.Giyeok);                         // the main-game proof, no TEST unlock
                    g.Aim(g.Inside[0]); g.Cast(spine);
                    bool mainOpens = spineRow != null && spineRow.Gate == SpellGateMode.Final && g.Accepted.Count == 1 && g.Misfires.Count == 0;
                    g.Wiring.ClearSpells308(SpellClearReason.Disabled); g.RestoreAll(); g.Aim(g.Inside[0]);
                    float ink = g.Ink.Value; g.Cast(rock);
                    r.Check("A5", rockRow != null && rockRow.Gate == SpellGateMode.Test && mainOpens && g.Accepted.Count == 0 && g.Misfires.Count == 1 &&
                        g.Misfires[0].Value == SpellResolveStatus.Locked && Near(ink - g.Ink.Value, g.Config.MisfireInkCost) && g.PendingCount == 0 && rockFx.RocksInFlight == 0,
                        "a normal save that proved the final: the other row of that final casts, the TEST-only rock row is a Locked misfire (misfire ink, nothing scheduled, no rock)");
                    g.Finals.Proven.Clear(); g.Finals.Open.Add(SpellFinal.Giyeok);  // the TEST unlock of the isolated store
                    g.RestoreAll(); g.Aim(g.Inside[0]); g.Cast(rock);
                    r.Check("A5", g.Accepted.Count == 1 && g.Misfires.Count == 0 && rockFx.RocksInFlight == 1, "the TEST unlock of its final opens the rock row");
                    g.Wiring.ClearSpells308(SpellClearReason.Disabled);
                }
            }
        }
    }
}
