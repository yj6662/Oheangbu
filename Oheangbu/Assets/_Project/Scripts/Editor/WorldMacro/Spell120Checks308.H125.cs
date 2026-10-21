using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-125 H125 fixtures: 몽's blinding cloud against the main world's own field brains. The consumer is the D308-70 brain
    // (PrologueEncounter.Brain308) the main world's session hands to every field actor (WorldContent_Main.EnemyBrain): blind in the
    // cloud it stops sensing the player, keeps the last known place, and after LoseSightSeconds searches there (GiveUpSeconds later
    // it goes home). The field bosses run the same brain; a boss (EnemyVitals.IsBoss) keeps the player and only misses (canonical
    // text). The fixture builds real PrologueEncounter actors on a field body and on the fixture's boss body, reads the real content
    // and the real brain (never written), casts the row on the main game's proof alone (Gate final, no TEST unlock) and asks each
    // actor's own sensing step (Sensed308) inside and outside the cloud, aware and patrolling.
    // Left to Play: the walk to the last known place, the look-around, the pack's call, the look of the cloud.
    public static partial class Spell120Checks308
    {
        static partial void FixturesH125(Report r)
        {
            // grid cell 66 = earth o + ieung (no glyph is named)
            char blind = SpellGrammar308.LetterAt(66);
            if (!r.Wants(blind.ToString())) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var blindFx = effects.OfType<BlindZoneEffect308>().FirstOrDefault();
            if (!r.Check("H125", blindFx != null, "the blind cloud handler is registered")) return;
            var content = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(MainContent);
            var brain = content != null ? content.EnemyBrain : null;
            if (!r.Check("H125", content != null && brain != null && brain.Usable && content.Encounters != null,
                "the main world's content loads and hands out a usable brain: " + MainContent)) return;
            r.Check("몽-2", brain.LoseSightSeconds > 0f && brain.GiveUpSeconds > brain.LoseSightSeconds && brain.SearchArriveMeters > 0f,
                "the handed-out brain searches before it gives up: lost sight " + brain.LoseSightSeconds + " s -> search at the last known place (" + brain.SearchArriveMeters + " m) -> home after " + brain.GiveUpSeconds + " s");

            // the main scene's session reads this content, and field actors (and field bosses) of it stand in the scene
            string world = Path.Combine(Path.GetDirectoryName(Application.dataPath), MainWorld);
            string text = File.Exists(world) ? File.ReadAllText(world) : "";
            string contentGuid = AssetDatabase.AssetPathToGUID(MainContent);
            bool read = text.Length > 0 && !string.IsNullOrEmpty(contentGuid) && text.Contains("Content: {fileID: 11400000, guid: " + contentGuid);
            bool Stands(WorldMacroPlaytestSO.Encounter x) => Regex.IsMatch(text, "\\n  Id: " + Regex.Escape(x.Id) + "\\r?\\n");
            var encounters = content.Encounters.Where(x => x != null && !string.IsNullOrEmpty(x.Id)).ToArray();
            int field = encounters.Count(x => !x.Id.StartsWith("fieldboss308/", StringComparison.Ordinal) && Stands(x));
            int bosses = encounters.Count(x => x.Id.StartsWith("fieldboss308/", StringComparison.Ordinal) && Stands(x));
            text = null;
            r.Check("H125", read && field >= 10 && bosses >= 1,
                "the main world arms the cloud's target: " + field + " field encounters and " + bosses + " field boss encounter(s) stand as actors in " + MainWorld + ", whose session reads " + MainContent);
            string contentJson = JsonUtility.ToJson(content), brainJson = JsonUtility.ToJson(brain);
            bool contentDirty = EditorUtility.IsDirty(content), brainDirty = EditorUtility.IsDirty(brain);

            using (var f = NewFixture(r, effects, WP15Sheets))
            {
                f.Finals.Proven.Add(SpellFinal.Ieung);                                    // the main-game proof, no TEST unlock
                var host = (ISpellCastHost)f.Wiring;
                var everyone = f.Inside.Concat(f.Outside).Concat(new[] { f.Boss }).ToArray();
                var home = everyone.Select(e => e.transform.position).ToArray();
                var bodies = new[] { f.Inside[0], f.Boss };
                var actors = new PrologueEncounter[2]; var controllers = new EnemyController[2]; var agents = new NavMeshAgent[2];
                try
                {
                    if (!r.Check("H125", f.Resolver.TryRow(blind, out var row) && row.Gate == SpellGateMode.Final,
                        "the row is Gate final (D308-125 H125: the main world's field brains are its target)")) return;
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled); f.RestoreAll(); f.Aim(null);
                    for (int i = 0; i < bodies.Length; i++)
                    {
                        var body = bodies[i];
                        // the real actor on the fixture's own body; Awake never runs in Edit Mode, so its parts are handed over by hand
                        body.gameObject.SetActive(false);
                        actors[i] = body.gameObject.AddComponent<PrologueEncounter>();     // brings EnemyController and NavMeshAgent
                        agents[i] = body.GetComponent<NavMeshAgent>(); if (agents[i] != null) agents[i].enabled = false;
                        controllers[i] = body.GetComponent<EnemyController>();
                        body.gameObject.SetActive(true);
                        SpellFixture308.Set(controllers[i], "_config", f.Config); SpellFixture308.Set(controllers[i], "_vitals", body);
                        SpellFixture308.Set(controllers[i], "_player", f.Player.transform); SpellFixture308.Set(controllers[i], "_playerVitals", f.Player);
                        controllers[i].Init(new ParryJudge(f.Config));
                        SpellFixture308.Set(actors[i], "vitals", body);
                        actors[i].Brain = brain;                                            // what the session hands out (Start)
                    }
                    var fieldActor = actors[0]; var bossActor = actors[1];
                    r.Check("H125", fieldActor.BrainActive308 && bossActor.BrainActive308 && !f.Inside[0].IsBoss && f.Boss.IsBoss,
                        "a field body and a boss body (EnemyVitals.IsBoss, as the field bosses) both run the handed-out brain");

                    // the cloud: cast on the path ahead with everybody away, then put the two bodies in it
                    for (int i = 0; i < everyone.Length; i++) everyone[i].transform.position = SpellFixture308.Origin + new Vector3(300f + i * 10f, 0f, 300f);
                    Physics.SyncTransforms();
                    f.Cast(blind);
                    bool planned = f.Accepted.Count == 1 && f.Plans.Count == 1 && f.Plans[0].Area != null && blindFx.Zones.CountOf(BlindZoneEffect308.HandlerId) == 1;
                    if (!r.Check("H125", planned, "the cast is accepted on the ieung proof alone and leaves one cloud")) return;
                    var zone = blindFx.Zones.Zones.First(z => z.Owner == BlindZoneEffect308.HandlerId);
                    float during = zone.From + .1f, radius = zone.Radius;
                    Vector3 inside = zone.Centre, outside = zone.Centre + Vector3.right * (radius + 3f);
                    void Put(Vector3 field, Vector3 boss, float clock)
                    {
                        f.Inside[0].transform.position = field; f.Boss.transform.position = boss; Physics.SyncTransforms();
                        blindFx.Tick(new SpellTickContext(clock, 1f / 60f, host, f.Fx));
                    }
                    bool Senses(PrologueEncounter actor, PrologueEncounter.Behaviour state, float distance, float clock)
                    {
                        SpellFixture308.Set(actor, "<Current>k__BackingField", state);
                        return (bool)SpellFixture308.Call(actor, "Sensed308", distance, 0f, true, clock);
                    }
                    var chase = PrologueEncounter.Behaviour.Chase; var patrol = PrologueEncounter.Behaviour.Patrol;
                    float near = Mathf.Min(3f, brain.ProximityRadius * .5f);

                    Put(outside, outside + Vector3.forward * 4f, during);
                    bool sees = Senses(fieldActor, chase, 4f, during) && Senses(fieldActor, patrol, near, during);
                    Put(inside, inside, during + .05f);
                    bool fieldBlind = f.Inside[0].Modifiers.Veil.Blinded(during + .05f);
                    bool lost = !Senses(fieldActor, chase, 4f, during + .05f) && !Senses(fieldActor, patrol, near, during + .05f);
                    r.Check("몽-2", sees && fieldBlind && lost,
                        "a field actor in the cloud stops sensing the player (aware at 4 m, or patrolling within its close-by ring) - its chase falls back to the last known place and searches; outside it senses as before");
                    bool bossBlind = f.Boss.Modifiers.Veil.Blinded(during + .05f);
                    bool bossSees = Senses(bossActor, chase, 4f, during + .05f);
                    int strikes = 20, expected = strikes - Mathf.RoundToInt(strikes * Mathf.Clamp01(zone.Value)), landed = 0;
                    for (int i = 0; i < strikes; i++) { f.Player.Restore(); if (EnemyStrike308.Deliver(f.Player, f.Boss, 1f, IncomingDamageKind.Melee)) landed++; }
                    r.Check("몽-2", bossBlind && bossSees && zone.Value > 0f && landed == expected,
                        "a boss in the cloud keeps the player (canonical text: a boss only misses) and haze.miss of its strikes miss  [" + landed + " of " + strikes + " landed]");
                    Put(outside, outside + Vector3.forward * 4f, during + .1f);
                    r.Check("몽-3", Senses(fieldActor, chase, 4f, during + .1f) && !f.Inside[0].Modifiers.Veil.Blinded(during + .1f),
                        "the field actor that walked out senses the player again at once");
                    Put(inside, inside, zone.Until + .1f);
                    r.Check("몽-3", Senses(fieldActor, chase, 4f, zone.Until + .1f) && blindFx.Zones.CountOf(BlindZoneEffect308.HandlerId) == 0,
                        "after haze.life the cloud is gone and the actor in its place senses again");
                }
                finally
                {
                    for (int i = 0; i < actors.Length; i++)
                    {
                        if (actors[i] != null) { actors[i].Brain = null; Object.DestroyImmediate(actors[i]); }
                        if (controllers[i] != null) Object.DestroyImmediate(controllers[i]);
                        if (agents[i] != null) Object.DestroyImmediate(agents[i]);
                    }
                    for (int i = 0; i < everyone.Length; i++) everyone[i].transform.position = home[i];
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    f.Finals.Proven.Clear(); f.Finals.Open.Clear();
                }
            }
            r.Check("H125", JsonUtility.ToJson(content) == contentJson && JsonUtility.ToJson(brain) == brainJson
                && EditorUtility.IsDirty(content) == contentDirty && EditorUtility.IsDirty(brain) == brainDirty,
                "the real content and the real brain are read, never written (same values, not marked dirty)");
        }
    }
}
