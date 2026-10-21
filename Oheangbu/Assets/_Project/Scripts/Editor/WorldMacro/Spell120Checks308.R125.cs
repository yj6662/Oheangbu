using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-125 R125 fixtures: 상 · 송 · 막 · 공 against the main world's own stone-throwers. The real 돌팔매 profile
    // (MineRangedNeutral308: the 황경 길목 도적, the 논두렁 좀도둑 pair and the 폐광 test body; loaded, never written) on real
    // EnemyControllers in the fixture scene. A fixture never calls Awake (it would leave the stone it makes in the open scene),
    // so the controller has no stone object: the needle and the barrage read the thrower's body, as they do in Play while the
    // stone is hidden. A stone in the air is the controller's Flight; Edit Mode's Time.time does not move, so a flight lasts
    // until the check lands it (its impact time put in the past) or a glyph takes it down.
    //  상: the stone in the fan is taken out of the air: the attack ends as a cancel (that clears the stone's look), no damage.
    //  송: the pair's two stones go down together; a stone behind the caster flies on.
    //  막 / 공: with the rock (the wave front) between, the real stone lands through the real doorway and is stopped there
    //  (an impact, no damage); a thrower to the side still hurts; after the cover the same stone hurts again.
    // Every cast below is on the main game's proof alone (Gate final, no TEST unlock). Left to Play: the flight and its look.
    public static partial class Spell120Checks308
    {
        const string StoneThrow = "Assets/_Project/Art/World/Pacing308/Data/MineRangedNeutral308.asset";
        const string MainWorld = "Assets/_Project/Scenes/World/W_Demo_Main.unity";

        static partial void FixturesR125(Report r)
        {
            // grid cells by index (no glyph is named): 18 wood o + ieung, 50 earth a + giyeok, 78 metal a + ieung, 90 metal o + ieung
            char wave = SpellGrammar308.LetterAt(18), rock = SpellGrammar308.LetterAt(50), needle = SpellGrammar308.LetterAt(78), barrage = SpellGrammar308.LetterAt(90);
            if (!r.Wants(new string(new[] { wave, rock, needle, barrage }))) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var needleFx = effects.OfType<InterceptShotEffect308>().FirstOrDefault(); var barrageFx = effects.OfType<InterceptBarrageEffect308>().FirstOrDefault();
            var rockFx = effects.OfType<RockCoverEffect308>().FirstOrDefault(); var waveFx = effects.OfType<WaveCoverEffect308>().FirstOrDefault();
            var fallFx = effects.OfType<BallisticEffect308>().FirstOrDefault();
            if (!r.Check("R125", needleFx != null && barrageFx != null && rockFx != null && waveFx != null && fallFx != null, "the four handlers (and the rock's first stage) are registered")) return;
            var stone = AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(StoneThrow);
            if (!r.Check("R125", stone != null && stone.TryValidate(out _) && stone.Delivery == EnemyAttackDelivery.HomingProjectile && !stone.Elemental && stone.Damage > 0f && stone.Range > 0f,
                "the main world's stone-throw loads (a plain projectile: its strike is Ranged at the doorway): " + StoneThrow)) return;
            string world = Path.Combine(Path.GetDirectoryName(Application.dataPath), MainWorld);
            string guid = AssetDatabase.AssetPathToGUID(StoneThrow);
            int armed = File.Exists(world) && !string.IsNullOrEmpty(guid) ? Regex.Matches(File.ReadAllText(world), "_attackProfile: \\{fileID: 11400000, guid: " + guid).Count : 0;
            r.Check("R125", armed >= 3, "the main world arms it (the 길목 도적, the 논두렁 좀도둑 pair): " + armed + " bodies in " + MainWorld);
            string stoneJson = JsonUtility.ToJson(stone); bool stoneDirty = EditorUtility.IsDirty(stone);

            using (var f = NewFixture(r, effects, WP13Sheets))
            {
                var host = (ISpellCastHost)f.Wiring;
                f.Finals.Proven.Add(SpellFinal.Giyeok); f.Finals.Proven.Add(SpellFinal.Ieung);   // the main-game proof, no TEST unlock
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
                    f.Player.transform.position = origin;
                    Sync();
                }
                void SendAway()
                {
                    for (int i = 0; i < everyone.Length; i++) everyone[i].transform.position = origin + new Vector3(300f + i * 10f, 0f, 300f);
                    Sync();
                }
                void Tick(ISpellEffect effect, float clock) => effect.Tick(new SpellTickContext(clock, 1f / 60f, host, f.Fx));

                EnemyController thrower = null, partner = null, flanker = null;
                int impacts = 0, ended = 0; bool cancelled = false; EnemyAttackImpact impact = default;
                Action<EnemyAttackImpact> onImpact = e => { impacts++; impact = e; };
                Action<AttackProvenance, bool> onEnded = (_, off) => { ended++; cancelled = off; };
                try
                {
                    var rows = new[] { wave, rock, needle, barrage }.Select(x => f.Resolver.TryRow(x, out var row) ? row : null).ToArray();
                    if (!r.Check("R125", rows.All(x => x != null && x.Gate == SpellGateMode.Final),
                        "the four rows are Gate final (D308-125 R125: the main world's stone-throwers are their target)")) return;
                    Reset(); SendAway();
                    EnemyController Hook(EnemyVitals body)
                    {
                        var c = body.gameObject.AddComponent<EnemyController>();
                        SpellFixture308.Set(c, "_config", f.Config); SpellFixture308.Set(c, "_vitals", body);
                        SpellFixture308.Set(c, "_player", f.Player.transform); SpellFixture308.Set(c, "_playerVitals", f.Player);
                        c.Init(new ParryJudge(f.Config));
                        c.AttackImpactResolved += onImpact; c.AttackPresentationEnded += onEnded;
                        c.Configure(stone);
                        return c;
                    }
                    thrower = Hook(f.Inside[1]); partner = Hook(f.Inside[2]); flanker = Hook(f.Outside[0]);
                    void Put(EnemyController c, Vector3 body)
                    {
                        c.transform.position = body;
                        Vector3 to = Vector3.ProjectOnPlane(f.Player.transform.position - body, Vector3.up);
                        if (to.sqrMagnitude > 1e-4f) c.transform.rotation = Quaternion.LookRotation(to.normalized);
                        Sync();
                    }
                    // the telegraph runs out and the stone leaves the hand: the controller's own steps, called by hand
                    bool Throw(EnemyController c)
                    {
                        c.AttackEnabled = true;
                        SpellFixture308.Call(c, "BeginAuthoredTelegraph");
                        SpellFixture308.Set(c, "_stateUntil", Time.time - 1f);
                        SpellFixture308.Call(c, "TickAuthoredAttack");
                        return c.IsProjectileFlying;
                    }
                    // the stone's impact time comes (the player restored first: one strike, judged on its own)
                    void Lands(EnemyController c)
                    {
                        f.Player.Restore();
                        SpellFixture308.Set(c, "_impactTime", Time.time - 1f);
                        SpellFixture308.Call(c, "TickAuthoredAttack");
                    }
                    bool Hurt(int impacts0) => impacts == impacts0 + 1 && impact.InShape && impact.AppliedDamage > 0f && impact.AppliedDamage <= stone.Damage + .001f && f.Player.Hp01 < 1f;
                    bool Stopped(int impacts0) => impacts == impacts0 + 1 && impact.InShape && impact.AppliedDamage == 0f && Near(f.Player.Hp01, 1f) && !cancelled;

                    // ---- 상: one stone in the air ----
                    if (r.Wants(needle.ToString()))
                    {
                        Reset(); SendAway();
                        Put(thrower, origin + forward * 8f);
                        bool flying = Throw(thrower);
                        int impacts0 = impacts, ended0 = ended, downed0 = needleFx.Downed;
                        f.Aim(thrower.GetComponent<EnemyVitals>()); float clock = Time.time; f.Cast(needle);
                        r.Check("상-3", flying && f.Accepted.Count == 1 && f.Misfires.Count == 0 && needleFx.NeedlesInFlight == 1 && f.PendingCount == 0,
                            "a real thrower's stone in the needle's fan: the needle goes for it (no hit is scheduled on the thrower)");
                        Tick(needleFx, clock + 5f);
                        r.Check("상-3", !thrower.IsProjectileFlying && needleFx.Downed == downed0 + 1 && ended == ended0 + 1 && cancelled && impacts == impacts0 && Near(f.Player.Hp01, 1f),
                            "the stone is taken out of the air: the throw ends as a cancel (the stone's look is cleared), no impact, the player takes nothing");
                    }

                    // ---- 송: the pair's two stones at once ----
                    if (r.Wants(barrage.ToString()))
                    {
                        Reset(); SendAway();
                        Put(thrower, origin + forward * 6f + Vector3.right); Put(partner, origin + forward * 8f - Vector3.right); Put(flanker, origin - forward * 5f);
                        bool flying = Throw(thrower) && Throw(partner) && Throw(flanker);
                        int ended0 = ended, downed0 = barrageFx.Downed;
                        float clock = Time.time; f.Cast(barrage);
                        bool volley = f.Accepted.Count == 1 && f.Plans.Count == 1 && f.Plans[0].Area != null && f.Plans[0].Area.Shape == AreaShape.Volley && barrageFx.BarragesForming == 1;
                        r.Check("송-4", flying && volley && thrower.IsProjectileFlying && partner.IsProjectileFlying, "the cast is the base glyph's volley; both stones are still in the air before the barrage forms");
                        if (volley)
                        {
                            Tick(barrageFx, clock + f.Plans[0].Area.Delay + .05f);
                            r.Check("송-4", !thrower.IsProjectileFlying && !partner.IsProjectileFlying && flanker.IsProjectileFlying && barrageFx.Downed == downed0 + 2 && ended == ended0 + 2 && Near(f.Player.Hp01, 1f),
                                "the barrage forms and takes both stones of the pair down together; the stone thrown from behind the caster flies on");
                            int impacts0 = impacts; Lands(flanker);
                            r.Check("송-4", Hurt(impacts0), "that stone lands and hurts: the barrage covers its fan, not the caster's back");
                        }
                    }

                    // ---- 막: the rock between the thrower and the player ----
                    if (r.Wants(rock.ToString()))
                    {
                        Reset();
                        var row = rows[1]; float life = row.F("cover.life", 0f);
                        var target = f.Inside[0];
                        Put(thrower, origin + new Vector3(80f, 0f, 80f)); Put(flanker, origin + new Vector3(90f, 0f, 80f));
                        f.Aim(target); float clock = Time.time; f.Cast(rock);
                        bool planned = f.Accepted.Count == 1 && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && rockFx.RocksInFlight == 1;
                        r.Check("막-3", planned, "the rock is cast at the dummy ahead on the main-game proof");
                        if (planned)
                        {
                            float landsAt = f.Plans[0].Hits[0].ImpactTime;
                            f.Land(); Tick(fallFx, landsAt + .01f); Tick(rockFx, landsAt + .01f);
                            // the thrower in line behind the rock, within its range of the player; the flanker well to the side
                            Put(thrower, target.transform.position + forward * 8f); Put(flanker, origin + Vector3.right * 10f);
                            Tick(rockFx, landsAt + .1f);
                            bool inRange = Vector3.Distance(thrower.transform.position, f.Player.transform.position) <= stone.Range && rockFx.PieceCount == 1 && rockFx.ScreenedEnemies >= 1;
                            bool thrown = Throw(thrower); int impacts0 = impacts; Lands(thrower);
                            r.Check("막-4", inRange && thrown && Stopped(impacts0) && thrower.IsRecovering,
                                "the real stone of a thrower behind the rock lands and is stopped at the doorway: an impact, no damage, the thrower recovers as after any throw");
                            Throw(flanker); impacts0 = impacts; Lands(flanker);
                            r.Check("막-4", Hurt(impacts0), "a thrower to the side has the rock out of its line: its stone hurts");
                            Tick(rockFx, landsAt + life + .1f);
                            Throw(thrower); impacts0 = impacts; Lands(thrower);
                            r.Check("막-4", rockFx.PieceCount == 0 && Hurt(impacts0) && thrower.GetComponent<EnemyVitals>().Modifiers.IsEmpty,
                                "after cover.life the rock is gone: the same thrower's stone hurts again and nothing stays on it");
                        }
                    }

                    // ---- 공: the wave front between ----
                    if (r.Wants(wave.ToString()))
                    {
                        Reset();
                        Put(thrower, origin + new Vector3(80f, 0f, 80f)); Put(flanker, origin + new Vector3(90f, 0f, 80f));
                        f.Aim(f.Inside[0]);                                              // straight ahead: the corridor runs along the caster's facing
                        float clock = Time.time; f.Cast(wave);
                        bool planned = f.Accepted.Count == 1 && f.Plans.Count == 1 && f.Plans[0].Area != null && f.Plans[0].Area.Shape == AreaShape.Path && waveFx.PieceCount == 1;
                        r.Check("공-3", planned, "the wave is cast straight ahead on the main-game proof");
                        if (planned)
                        {
                            var area = f.Plans[0].Area;
                            float running = clock + area.Delay + area.Length * .5f / area.Speed;
                            f.Player.transform.position = origin + forward; Sync();       // the caster walks behind the wave
                            Put(thrower, origin + forward * (area.Length * .5f + 4f)); Put(flanker, origin - forward * 6f);
                            Tick(waveFx, running);
                            bool inRange = Vector3.Distance(thrower.transform.position, f.Player.transform.position) <= stone.Range && waveFx.ScreenedEnemies >= 1;
                            bool thrown = Throw(thrower); int impacts0 = impacts; Lands(thrower);
                            r.Check("공-4", inRange && thrown && Stopped(impacts0),
                                "while the wave runs, the real stone of a thrower ahead of it lands and is stopped at the doorway (an impact, no damage)");
                            Throw(flanker); impacts0 = impacts; Lands(flanker);
                            r.Check("공-4", Hurt(impacts0), "a thrower behind the player is not cut off: its stone hurts");
                            Tick(waveFx, clock + area.Delay + area.Length / area.Speed + .1f);
                            Throw(thrower); impacts0 = impacts; Lands(thrower);
                            r.Check("공-4", waveFx.PieceCount == 0 && Hurt(impacts0) && thrower.GetComponent<EnemyVitals>().Modifiers.IsEmpty,
                                "when the wave reaches the far end of its corridor the cover ends: the same stone hurts again");
                        }
                    }
                }
                finally
                {
                    foreach (var c in new[] { thrower, partner, flanker })
                        if (c != null) { c.AttackImpactResolved -= onImpact; c.AttackPresentationEnded -= onEnded; Object.DestroyImmediate(c); }
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    f.Finals.Proven.Clear(); f.Finals.Open.Clear();
                }
            }
            r.Check("R125", JsonUtility.ToJson(stone) == stoneJson && EditorUtility.IsDirty(stone) == stoneDirty, "the real profile is read, never written (same values, not marked dirty)");
        }
    }
}
