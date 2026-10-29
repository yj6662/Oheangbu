using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-125 S125 fixtures: 삿's thrust against the main world's own guarding field enemies. The guard is the D308-114 seam:
    // WorldContent_Main.GuardBrains names the encounter kinds that raise it; the session (WorldMacroPlaytestSession.ApplyGuardBrains308)
    // clones the brain an actor uses and writes the share into the clone only; the actor (PrologueEncounter.Guard308, called in its
    // Wait phase while it faces the player) raises the real stance on its own EnemyVitals. The fixture drives that chain on a real
    // PrologueEncounter with the real content asset and the real brain (read, never written), for every guarded kind, cast on the
    // main game's proof alone (Gate final, no TEST unlock): a plain shot loses the share, the thrust lands whole through the raised
    // guard (the guard stays up, the entry is used up), the next plain shot loses the share again, and with the guard lowered a
    // plain shot lands whole. A kind the content does not name never guards.
    // Left to Play: the brain reaching its Wait phase facing the player, the look of the guard and of the thrust.
    public static partial class Spell120Checks308
    {
        const string MainContent = "Assets/_Project/Scenes/World/Main/WorldContent_Main.asset";

        static partial void FixturesS125(Report r)
        {
            // grid cells by index (no glyph is named): 77 metal a + siot, 73 = the metal single shot without a final
            char thrust = SpellGrammar308.LetterAt(77), plain = SpellGrammar308.LetterAt(73);
            if (!r.Wants(thrust.ToString())) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var thrustFx = effects.OfType<UnblockableEffect308>().FirstOrDefault();
            if (!r.Check("S125", thrustFx != null, "the thrust handler is registered")) return;
            var content = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(MainContent);
            var brain = content != null ? content.EnemyBrain : null;
            if (!r.Check("S125", content != null && brain != null && brain.Usable && brain.GuardShare == 0f && content.Encounters != null && content.GuardBrains != null,
                "the main world's content loads and hands out a usable brain that does not guard on its own: " + MainContent)) return;

            // the guarded kinds: an entry with a share and a front, and at least one encounter of that kind
            var kinds = content.GuardBrains.Where(e => e != null && e.GuardShare > 0f && e.GuardFacingDegrees >= 1f)
                .Select(e => (entry: e, encounter: Array.Find(content.Encounters, x => x != null && x.ContentId == e.ContentId)))
                .Where(k => k.encounter != null).ToArray();
            var guardedIds = new HashSet<string>(kinds.Select(k => k.entry.ContentId));
            var guardedEncounters = content.Encounters.Where(x => x != null && guardedIds.Contains(x.ContentId)).ToArray();
            var unguarded = content.Encounters.FirstOrDefault(x => x != null && !string.IsNullOrEmpty(x.ContentId) && !content.GuardBrains.Any(e => e != null && e.ContentId == x.ContentId));
            // the main scene's session reads this content, and the actors of those encounters stand in it
            string world = Path.Combine(Path.GetDirectoryName(Application.dataPath), MainWorld);
            string text = File.Exists(world) ? File.ReadAllText(world) : "";
            string contentGuid = AssetDatabase.AssetPathToGUID(MainContent);
            bool read = text.Length > 0 && !string.IsNullOrEmpty(contentGuid) && text.Contains("Content: {fileID: 11400000, guid: " + contentGuid);
            int standing = guardedEncounters.Count(x => Regex.IsMatch(text, "\\n  Id: " + Regex.Escape(x.Id) + "\\r?\\n"));
            text = null;
            r.Check("S125", kinds.Length >= 3 && read && standing >= 3,
                "the main world arms the guard: " + kinds.Length + " guarded kinds (" + string.Join(", ", kinds.Select(k => k.entry.ContentId)) + "), " + standing + " of their "
                + guardedEncounters.Length + " encounters stand as actors in " + MainWorld + ", whose session reads " + MainContent);
            string contentJson = JsonUtility.ToJson(content), brainJson = JsonUtility.ToJson(brain);
            bool contentDirty = EditorUtility.IsDirty(content), brainDirty = EditorUtility.IsDirty(brain);
            EnemyBrainProfile308[] made = Array.Empty<EnemyBrainProfile308>();

            using (var f = NewFixture(r, effects, WP13Sheets))
            {
                f.Finals.Proven.Add(SpellFinal.Siot);                                     // the main-game proof, no TEST unlock
                float brush = f.Brush();
                var body = f.Inside[0];
                PrologueEncounter actor = null; EnemyController controller = null; NavMeshAgent agent = null;
                List<EnemyBrainProfile308> clones = null;
                try
                {
                    if (!r.Check("S125", f.Resolver.TryRow(thrust, out var row) && row.Gate == SpellGateMode.Final,
                        "the row is Gate final (D308-125 S125: the main world's guarding field enemies are its target)")) return;
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled); f.RestoreAll(); f.Aim(null);
                    // the real actor on the fixture's own body; Awake never runs in Edit Mode, so its parts are handed over by hand
                    body.gameObject.SetActive(false);
                    actor = body.gameObject.AddComponent<PrologueEncounter>();             // brings EnemyController and NavMeshAgent
                    agent = body.GetComponent<NavMeshAgent>(); if (agent != null) agent.enabled = false;
                    controller = body.GetComponent<EnemyController>();
                    body.gameObject.SetActive(true);
                    Physics.SyncTransforms();
                    SpellFixture308.Set(controller, "_config", f.Config); SpellFixture308.Set(controller, "_vitals", body);
                    SpellFixture308.Set(controller, "_player", f.Player.transform); SpellFixture308.Set(controller, "_playerVitals", f.Player);
                    controller.Init(new ParryJudge(f.Config));
                    SpellFixture308.Set(actor, "vitals", body);
                    // the real session on an inactive host (its OnDisable saves): only its guard step is called
                    var host = f.Obj("Spell308_S125_Session", SpellFixture308.Origin + new Vector3(0f, -50f, -300f));
                    host.SetActive(false);
                    var session = host.AddComponent<WorldMacroPlaytestSession>();
                    session.Content = content; session.Actors = new[] { actor };
                    clones = (List<EnemyBrainProfile308>)typeof(WorldMacroPlaytestSession).GetField("guardBrainClones308", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);

                    float planned = 0f;
                    float Shot(char letter)                                                 // HP taken by one cast at the body
                    {
                        f.ClearLog(); f.Ink.Restore(); f.Aim(body); float hp = body.Hp; f.Cast(letter);
                        planned = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 ? f.Plans[0].Hits[0].Power : -1f;
                        f.Land();
                        return f.Hits.Count == 1 ? hp - body.Hp : -1f;
                    }
                    void Raise() { body.Restore(); SpellFixture308.Call(actor, "Guard308", true, Time.time); }   // as in its Wait phase, facing the player

                    foreach (var k in kinds)
                    {
                        string name = k.entry.ContentId + " (" + k.encounter.Id + ")";
                        actor.Id = k.encounter.Id; actor.Brain = brain;                     // the brain the session hands out (Start)
                        int guarded = (int)SpellFixture308.Call(session, "ApplyGuardBrains308");
                        var clone = actor.Brain;
                        bool cloned = guarded == 1 && clone != null && clone != brain && (clone.hideFlags & HideFlags.DontSave) != 0 && clones.Contains(clone)
                            && Near(clone.GuardShare, Mathf.Clamp01(k.entry.GuardShare)) && Near(clone.GuardFacingDegrees, Mathf.Clamp(k.entry.GuardFacingDegrees, 0f, 180f))
                            && actor.BrainActive308 && brain.GuardShare == 0f;
                        r.Check("S125", cloned, name + ": the session clones the content's brain and writes the guard (" + (clone != null ? clone.GuardShare + ", " + clone.GuardFacingDegrees + " deg" : "none")
                            + ") into the clone only");
                        if (!cloned) continue;
                        float share = clone.GuardShare;

                        Raise();
                        bool up = body.Modifiers.Stance.Raised && Near(body.Modifiers.Stance.Share, share);
                        float plainTaken = Shot(plain), plainPlanned = planned;
                        r.Check("삿-3", up && plainPlanned > 0f && Near(plainTaken, plainPlanned * (1f - share)) && body.Modifiers.Stance.Raised,
                            name + ": the actor's own guard takes " + share + " of a plain shot  [" + plainTaken + " of " + plainPlanned + "]");

                        Raise();
                        f.ClearLog(); f.Ink.Restore(); f.Aim(body); float hp = body.Hp; f.Cast(thrust);
                        bool told = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && body.Modifiers.Stance.Passes(f.Plans[0].Hits[0].AttackId);
                        long attackId = told ? f.Plans[0].Hits[0].AttackId : 0;
                        f.Land();
                        float whole = row.BasePower * brush;
                        r.Check("삿-3", told && Near(hp - body.Hp, whole) && f.Hits.Count == 1 && body.Hp > 0f && body.Modifiers.Stance.Raised && Near(body.Modifiers.Stance.Share, share)
                            && !body.Modifiers.Stance.Passes(attackId) && thrustFx.HitsOnTheWay == 0,
                            name + ": the thrust lands whole through the raised guard [" + (hp - body.Hp) + " of " + whole + "]; the guard stays up and the entry is used up");
                        float again = Shot(plain);
                        r.Check("삿-3", planned > 0f && Near(again, planned * (1f - share)) && body.Modifiers.Stance.Raised,
                            name + ": the next plain shot loses the share again [" + again + " of " + planned + "]");

                        SpellFixture308.Call(actor, "Guard308", false, Time.time);          // out of its Wait phase
                        bool down = !body.Modifiers.Stance.Raised;
                        body.Restore();
                        float open = Shot(plain);
                        r.Check("삿-3", down && planned > 0f && Near(open, planned), name + ": with the guard lowered a plain shot lands whole [" + open + " of " + planned + "]");
                    }

                    if (r.Check("S125", unguarded != null, "the content has a kind it does not name in GuardBrains (the control)"))
                    {
                        actor.Id = unguarded.Id; actor.Brain = brain;
                        int none = (int)SpellFixture308.Call(session, "ApplyGuardBrains308");
                        Raise();
                        bool still = none == 0 && actor.Brain == brain && !body.Modifiers.Stance.Raised;
                        float open = Shot(plain);
                        r.Check("S125", still && planned > 0f && Near(open, planned),
                            unguarded.ContentId + " (" + unguarded.Id + ", not named in GuardBrains): no clone, the guard never rises, a plain shot lands whole");
                    }
                }
                finally
                {
                    if (clones != null)
                    {
                        made = clones.ToArray();
                        foreach (var clone in made) if (clone != null) Object.DestroyImmediate(clone);
                        clones.Clear();
                    }
                    if (actor != null) { actor.Brain = null; Object.DestroyImmediate(actor); }
                    if (controller != null) Object.DestroyImmediate(controller);
                    if (agent != null) Object.DestroyImmediate(agent);
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    f.Finals.Proven.Clear(); f.Finals.Open.Clear();
                }
            }
            r.Check("S125", made.Length > 0 && made.All(c => c == null), "the guarded brain clones (" + made.Length + ") die with the fixture");
            r.Check("S125", JsonUtility.ToJson(content) == contentJson && JsonUtility.ToJson(brain) == brainJson
                && EditorUtility.IsDirty(content) == contentDirty && EditorUtility.IsDirty(brain) == brainDirty,
                "the real content and the real brain are read, never written (same values, not marked dirty)");
        }
    }
}
