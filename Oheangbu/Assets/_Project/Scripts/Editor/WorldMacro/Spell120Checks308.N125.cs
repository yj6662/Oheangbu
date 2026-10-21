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
    // #308 D308-125 N125 fixtures: 농's haze against the main world's own enemies. The haze judges every kind of strike at
    // the enemy -> player doorway (EnemyStrike308.Deliver), so its consumers are the three strike paths the main world arms:
    //  the authored melee arc   Attack308_goat (the 산양 bodies; the fox and wolf profiles strike through the same path)
    //  the old melee            an EnemyController with CombatConfig_Default and no profile (BeginTelegraph -> OnTelegraphEnd)
    //  the stone-throw          MineRangedNeutral308 (the R125 throwers)
    // The real profiles and the real config are loaded, never written. Each body stands alone in a real 농 haze, cast on the
    // main game's proof alone (Gate final, no TEST unlock), and strikes a player within its reach: haze.miss of its strikes
    // miss (an impact in shape, no damage); walked out of the haze it strikes true; after haze.life nobody misses.
    // Left to Play: the haze's look, the enemy brains walking in and out of it.
    public static partial class Spell120Checks308
    {
        const string GoatArc = "Assets/_Project/Art/Characters/Creatures308/Data/Attack308_goat.asset";
        const string DefaultConfig = "Assets/_Project/Art/World/Architecture296/Data/7d70f0b4e25ade346b3067d5ab0bfa8a_4c0334a6532a1b54f90f19a5d9e05adb_3c72e0f096c47d94290503c64ebe3d1c_CombatConfig_Default.asset";

        static partial void FixturesN125(Report r)
        {
            // grid cell by index (no glyph is named): 42 fire o + ieung
            char haze = SpellGrammar308.LetterAt(42);
            if (!r.Wants(haze.ToString())) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var hazeFx = effects.OfType<HazeZoneEffect308>().FirstOrDefault();
            if (!r.Check("N125", hazeFx != null, "the haze handler is registered")) return;
            var goat = AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(GoatArc);
            var stone = AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(StoneThrow);
            var config = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DefaultConfig);
            if (!r.Check("N125", goat != null && goat.TryValidate(out _) && goat.Delivery == EnemyAttackDelivery.MeleeArc && !goat.Elemental && goat.Damage > 0f && goat.Range > 0f,
                "the main world's goat arc loads (a plain melee arc: its strike is Melee at the doorway): " + GoatArc)) return;
            if (!r.Check("N125", stone != null && stone.TryValidate(out _) && stone.Delivery == EnemyAttackDelivery.HomingProjectile && !stone.Elemental && stone.Range > 0f,
                "the main world's stone-throw loads (a plain projectile: its strike is Ranged at the doorway): " + StoneThrow)) return;
            if (!r.Check("N125", config != null && config.MeleeRange > 0f && config.MeleeDamage > 0f,
                "the main world's default combat config loads (the old melee's range and damage): " + DefaultConfig)) return;

            string world = Path.Combine(Path.GetDirectoryName(Application.dataPath), MainWorld);
            string text = File.Exists(world) ? File.ReadAllText(world) : "";
            int Armed(string path)
            {
                string g = AssetDatabase.AssetPathToGUID(path);
                return text.Length > 0 && !string.IsNullOrEmpty(g) ? Regex.Matches(text, "_attackProfile: \\{fileID: 11400000, guid: " + g).Count : 0;
            }
            // an old melee body: an EnemyController block with the default config, melee only and no profile
            string controllerGuid = AssetDatabase.FindAssets("EnemyController t:MonoScript").Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileName(p) == "EnemyController.cs").Select(AssetDatabase.AssetPathToGUID).FirstOrDefault() ?? "";
            string configGuid = AssetDatabase.AssetPathToGUID(DefaultConfig);
            int legacy = 0;
            if (controllerGuid.Length > 0 && !string.IsNullOrEmpty(configGuid))
                for (int at = text.IndexOf("guid: " + controllerGuid, StringComparison.Ordinal); at >= 0; at = text.IndexOf("guid: " + controllerGuid, at + 1, StringComparison.Ordinal))
                {
                    int from = text.LastIndexOf("--- !u!", at, StringComparison.Ordinal), to = text.IndexOf("--- !u!", at, StringComparison.Ordinal);
                    string block = text.Substring(Math.Max(0, from), (to < 0 ? text.Length : to) - Math.Max(0, from));
                    if (block.Contains("_config: {fileID: 11400000, guid: " + configGuid) && block.Contains("_attackProfile: {fileID: 0}") && Regex.IsMatch(block, "\\n  _attackMode: 1\\r?\\n")) legacy++;
                }
            int goats = Armed(GoatArc), throwers = Armed(StoneThrow);
            r.Check("N125", goats >= 1 && throwers >= 3 && legacy >= 1,
                "the main world arms all three strike paths: " + goats + " goat arcs, " + throwers + " stone-throws, " + legacy + " old melee bodies in " + MainWorld);
            text = null;
            string goatJson = JsonUtility.ToJson(goat), stoneJson = JsonUtility.ToJson(stone), configJson = JsonUtility.ToJson(config);
            bool goatDirty = EditorUtility.IsDirty(goat), stoneDirty = EditorUtility.IsDirty(stone), configDirty = EditorUtility.IsDirty(config);

            using (var f = NewFixture(r, effects, WP13Sheets))
            {
                var host = (ISpellCastHost)f.Wiring;
                f.Finals.Proven.Add(SpellFinal.Ieung);                                    // the main-game proof, no TEST unlock
                Vector3 origin = SpellFixture308.Origin;
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

                EnemyController arc = null, old = null, thrower = null;
                int impacts = 0; EnemyAttackImpact impact = default;
                Action<EnemyAttackImpact> onImpact = e => { impacts++; impact = e; };
                try
                {
                    if (!r.Check("N125", f.Resolver.TryRow(haze, out var row) && row.Gate == SpellGateMode.Final,
                        "the row is Gate final (D308-125 N125: the main world's own enemies are its target)")) return;
                    float miss = row.F("haze.miss", 0f), radius = row.F("haze.radius", 0f);
                    EnemyController Hook(EnemyVitals body, EnemyAttackProfileSO profile)
                    {
                        var c = body.gameObject.AddComponent<EnemyController>();
                        SpellFixture308.Set(c, "_config", f.Config); SpellFixture308.Set(c, "_vitals", body);
                        SpellFixture308.Set(c, "_player", f.Player.transform); SpellFixture308.Set(c, "_playerVitals", f.Player);
                        c.Init(new ParryJudge(f.Config));
                        c.AttackImpactResolved += onImpact;
                        if (profile != null) c.Configure(profile);
                        return c;
                    }
                    Reset(); SendAway();
                    arc = Hook(f.Inside[1], goat); thrower = Hook(f.Outside[0], stone);
                    // the old body: no profile at all (ProfileActive false), the main world's config, melee only
                    old = Hook(f.Inside[2], null);
                    SpellFixture308.Set(old, "_config", config); SpellFixture308.Set(old, "_attackMode", EnemyController.AttackMode.MeleeOnly);
                    void Put(EnemyController c, Vector3 body)
                    {
                        c.transform.position = body;
                        Vector3 to = Vector3.ProjectOnPlane(f.Player.transform.position - body, Vector3.up);
                        if (to.sqrMagnitude > 1e-4f) c.transform.rotation = Quaternion.LookRotation(to.normalized);
                        Sync();
                    }
                    // one strike of each path, the controller's own steps called by hand; the player restored first (one strike,
                    // judged on its own). true = it hurt. `reached` turns false if a strike did not reach the player at all.
                    bool reached = true;
                    bool Swing(EnemyController c)
                    {
                        f.Player.Restore(); int impacts0 = impacts;
                        c.AttackEnabled = true;
                        SpellFixture308.Call(c, "BeginAuthoredTelegraph");
                        SpellFixture308.Set(c, "_stateUntil", Time.time - 1f);
                        SpellFixture308.Call(c, "TickAuthoredAttack");
                        if (impacts != impacts0 + 1 || !impact.InShape) { reached = false; return false; }
                        return impact.AppliedDamage > 0f && f.Player.Hp01 < 1f;
                    }
                    bool Strike(EnemyController c)
                    {
                        f.Player.Restore();
                        SpellFixture308.Call(c, "BeginTelegraph");
                        SpellFixture308.Call(c, "OnTelegraphEnd");
                        return f.Player.Hp01 < 1f;
                    }
                    bool Throw(EnemyController c)
                    {
                        f.Player.Restore(); int impacts0 = impacts;
                        c.AttackEnabled = true;
                        SpellFixture308.Call(c, "BeginAuthoredTelegraph");
                        SpellFixture308.Set(c, "_stateUntil", Time.time - 1f);
                        SpellFixture308.Call(c, "TickAuthoredAttack");
                        if (!c.IsProjectileFlying) { reached = false; return false; }
                        SpellFixture308.Set(c, "_impactTime", Time.time - 1f);
                        SpellFixture308.Call(c, "TickAuthoredAttack");
                        if (impacts != impacts0 + 1 || !impact.InShape) { reached = false; return false; }
                        return impact.AppliedDamage > 0f && f.Player.Hp01 < 1f;
                    }
                    int Hurts(EnemyController body, Func<EnemyController, bool> strike, int count)
                    {
                        int hurts = 0;
                        for (int i = 0; i < count; i++) if (strike(body)) hurts++;
                        return hurts;
                    }

                    var cases = new[]
                    {
                        (body: arc, name: "the goat arc (authored melee)", reach: goat.Range * .6f, strike: (Func<EnemyController, bool>)Swing),
                        (body: old, name: "the old melee (default config, no profile)", reach: config.MeleeRange * .6f, strike: (Func<EnemyController, bool>)Strike),
                        (body: thrower, name: "the stone-throw", reach: Mathf.Min(4f, stone.Range * .6f), strike: (Func<EnemyController, bool>)Throw),
                    };
                    const int strikes = 4;
                    int expected = strikes - Mathf.RoundToInt(strikes * miss);
                    foreach (var c in cases)
                    {
                        Reset();
                        // straight ahead at the dummy; the shape is read back from the plan
                        f.Aim(f.Inside[0]); float castAt = Time.time; f.Cast(haze);
                        bool planned = f.Accepted.Count == 1 && f.Misfires.Count == 0 && f.Plans.Count == 1 && f.Plans[0].Area != null && f.Plans[0].Area.Shape == row.AreaShape
                            && hazeFx.Zones.CountOf(HazeZoneEffect308.HandlerId) == 1;
                        r.Check("농-3", planned, "the haze is cast on the main-game proof alone (" + c.name + ")");
                        if (!planned) continue;
                        var area = f.Plans[0].Area;
                        var zone = hazeFx.Zones.Zones.First(z => z.Owner == HazeZoneEffect308.HandlerId);
                        Vector3 forward = Vector3.ProjectOnPlane(area.Direction, Vector3.up).normalized, side = Vector3.Cross(Vector3.up, forward);
                        float during = zone.From + .1f;
                        SendAway();
                        // the body at the haze's middle, the player within its reach on the caster's side
                        reached = true;
                        f.Player.transform.position = zone.Centre - forward * c.reach; Sync();
                        Put(c.body, zone.Centre);
                        Tick(hazeFx, during);
                        int hurt = Hurts(c.body, c.strike, strikes);
                        r.Check("농-3", reached && hurt == expected && hazeFx.VeiledEnemies == 1,
                            c.name + " standing in the haze: " + hurt + " of " + strikes + " strikes hurt (expected " + expected + ", haze.miss " + miss + "); the rest reach the player and miss at the doorway");
                        // walked out: the body and the player side by side, out of the circle
                        Vector3 shift = side * (radius + c.body.GetComponent<EnemyVitals>().BodyRadius + 3f);
                        f.Player.transform.position += shift; Sync();
                        Put(c.body, zone.Centre + shift);
                        Tick(hazeFx, during + .1f);
                        reached = true; hurt = Hurts(c.body, c.strike, strikes);
                        r.Check("농-3", reached && hurt == strikes && hazeFx.VeiledEnemies == 0 && c.body.GetComponent<EnemyVitals>().Modifiers.IsEmpty,
                            c.name + " walked out of the haze: every strike hurts again, nothing stays on it");
                        // back in the middle after haze.life: the haze is gone
                        f.Player.transform.position -= shift; Sync();
                        Put(c.body, zone.Centre);
                        Tick(hazeFx, zone.Until + .1f);
                        reached = true; hurt = Hurts(c.body, c.strike, strikes);
                        r.Check("농-3", reached && hurt == strikes && hazeFx.Zones.CountOf(HazeZoneEffect308.HandlerId) == 0 && c.body.GetComponent<EnemyVitals>().Modifiers.IsEmpty,
                            c.name + " after haze.life (" + (zone.Until - zone.From) + " s, cast at " + castAt + "): the haze is gone and every strike hurts");
                    }
                }
                finally
                {
                    foreach (var c in new[] { arc, old, thrower })
                        if (c != null) { c.AttackImpactResolved -= onImpact; Object.DestroyImmediate(c); }
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    f.Finals.Proven.Clear(); f.Finals.Open.Clear();
                }
            }
            r.Check("N125", JsonUtility.ToJson(goat) == goatJson && JsonUtility.ToJson(stone) == stoneJson && JsonUtility.ToJson(config) == configJson
                && EditorUtility.IsDirty(goat) == goatDirty && EditorUtility.IsDirty(stone) == stoneDirty && EditorUtility.IsDirty(config) == configDirty,
                "the real profiles and the real config are read, never written (same values, not marked dirty)");
        }
    }
}
