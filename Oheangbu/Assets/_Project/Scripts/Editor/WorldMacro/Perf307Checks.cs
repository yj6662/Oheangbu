using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #307 phase 1 (Tools/Unity/Plan307/PERF_DESIGN.md C.1) — old-vs-new identity checks for the CPU/GC changes. Each check runs
    // the pre-#307 algorithm (copied here verbatim as a reference) next to the new code on the same inputs and physics state and
    // reports PASS/FAIL lines. Queue-safe: every refusal or failure is a returned string, never a dialog; nothing is saved.
    //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.Perf307Checks Run "<check>"
    // Edit or Play: campaign-identity (ForEvent/CanComplete), save-bytes (store bytes old vs new sync vs background, writer rules).
    // Play only (W_Demo_Main after arrival): focus-identity (CanInteract, focus, views; jittered positions around every point),
    //   feet-identity (TrySafeFeet core bits, natural-solid Covers soundness, foot skinning delta), gesture-identity (torso skip),
    //   cull-colliders (boss collider rule), rig-skip (skip counters). all = every check the current mode allows.
    public static class Perf307Checks
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static string Run(string arg)
        {
            string check = (arg ?? "").Trim();
            if (check.Length == 0) check = "all";
            var known = new[] { "campaign-identity", "save-bytes", "focus-identity", "feet-identity", "gesture-identity", "cull-colliders", "rig-skip", "all" };
            if (Array.IndexOf(known, check) < 0) return "refused: unknown check '" + check + "' (" + string.Join(" | ", known) + ")";
            if (EditorApplication.isCompiling) return "refused: scripts are compiling";
            bool playing = EditorApplication.isPlaying;
            bool playOnly = check != "campaign-identity" && check != "save-bytes" && check != "all";
            if (playOnly && !playing) return "refused: " + check + " needs Play mode (W_Demo_Main after arrival)";
            var report = new Report();
            try
            {
                if (check == "campaign-identity" || check == "all") CampaignIdentity(report);
                if (check == "save-bytes" || check == "all") SaveBytes(report);
                if (playing)
                {
                    if (check == "focus-identity" || check == "all") FocusIdentity(report);
                    if (check == "feet-identity" || check == "all") FeetIdentity(report);
                    if (check == "gesture-identity" || check == "all") GestureIdentity(report);
                    if (check == "cull-colliders" || check == "all") CullColliders(report);
                    if (check == "rig-skip" || check == "all") RigSkip(report);
                }
                else if (check == "all") report.Note("Play-only checks skipped (Edit mode): focus-identity, feet-identity, gesture-identity, cull-colliders, rig-skip");
            }
            catch (Exception e) { report.Fail("exception: " + e.GetType().Name + ": " + e.Message); }
            return report.ToString();
        }

        sealed class Report
        {
            readonly List<string> lines = new List<string>(); int passed, failed;
            public void Check(bool ok, string label) { if (ok) passed++; else failed++; lines.Add((ok ? "PASS " : "FAIL ") + label); }
            public void Fail(string label) => Check(false, label);
            public void Note(string label) => lines.Add("NOTE " + label);
            public override string ToString() => (failed == 0 ? "PASS" : "FAIL") + " #307 phase-1 checks: " + passed + " passed, " + failed + " failed\n" + string.Join("\n", lines);
        }

        // ------------------------------------------------------------------ campaign-identity
        // Pre-#307 DemoCampaignProgression.CanComplete / AvailableStages / ForEvent, verbatim.
        static class OldProgression
        {
            public static bool CanComplete(DemoCampaignProfile profile, DemoCampaignState state, string stageId, ICollection<string> defeated = null)
            {
                if (profile?.Stages == null || state?.Completed == null || profile.CampaignId != state.CampaignId) return false;
                var stage = Array.Find(profile.Stages, s => s.Id == stageId);
                if (stage == null || !stage.Implemented || state.Completed.Contains(stageId) || !DemoCampaignProgression.PrerequisitesMet(stage, state)) return false;
                foreach (var id in stage.RequiredDefeatedIds ?? Array.Empty<string>())
                    if ((defeated == null || !defeated.Contains(id)) && (state.EncounterEvidence == null || !state.EncounterEvidence.Contains(id))) return false;
                return true;
            }
            static IEnumerable<DemoCampaignProfile.Stage> AvailableStages(DemoCampaignProfile profile, DemoCampaignState state, ICollection<string> defeated = null)
            {
                if (profile?.Stages == null) yield break;
                foreach (var stage in profile.Stages) if (CanComplete(profile, state, stage.Id, defeated)) yield return stage;
            }
            public static DemoCampaignProfile.Stage ForEvent(DemoCampaignProfile profile, DemoCampaignState state, DemoEventKind kind, string triggerId, ICollection<string> defeated = null)
            {
                if (profile == null) return null;
                if (!profile.UseExplicitPrerequisites)
                {
                    var current = DemoCampaignProgression.CurrentRequired(profile, state);
                    return current != null && current.Event == kind && current.TriggerId == triggerId ? current : null;
                }
                DemoCampaignProfile.Stage result = null;
                foreach (var stage in AvailableStages(profile, state, defeated))
                    if (stage.Event == kind && stage.TriggerId == triggerId) { if (result != null) return null; result = stage; }
                return result;
            }
        }

        static void CampaignIdentity(Report report)
        {
            var guids = AssetDatabase.FindAssets("t:DemoCampaignProfile");
            if (guids.Length == 0) { report.Note("campaign-identity: no DemoCampaignProfile asset found"); return; }
            var kinds = (DemoEventKind[])Enum.GetValues(typeof(DemoEventKind));
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<DemoCampaignProfile>(path);
                if (asset == null || asset.Stages == null || asset.Stages.Any(s => s == null)) { report.Note("campaign-identity: skipped " + path + " (null stage entries)"); continue; }
                // Both prerequisite modes, on an in-memory copy (the asset is never modified).
                foreach (bool explicitMode in new[] { true, false })
                {
                    var profile = Object.Instantiate(asset); profile.hideFlags = HideFlags.HideAndDontSave; profile.UseExplicitPrerequisites = explicitMode;
                    try
                    {
                        int compared = 0, mismatched = 0; string first = null;
                        var triggers = profile.Stages.Select(s => s.TriggerId).Concat(new[] { "perf307_missing", null }).Distinct().ToArray();
                        var stageIds = profile.Stages.Select(s => s.Id).Concat(new[] { "perf307_missing", null }).ToArray();
                        foreach (var state in States(profile, 48))
                            foreach (var defeated in DefeatedSets(profile))
                            {
                                foreach (var trigger in triggers)
                                    foreach (var kind in kinds)
                                    {
                                        compared++;
                                        var a = OldProgression.ForEvent(profile, state, kind, trigger, defeated);
                                        var b = DemoCampaignProgression.ForEvent(profile, state, kind, trigger, defeated);
                                        if (!ReferenceEquals(a, b)) { mismatched++; first ??= "ForEvent(" + kind + "," + trigger + ") old=" + a?.Id + " new=" + b?.Id; }
                                    }
                                foreach (var id in stageIds)
                                {
                                    compared++;
                                    if (OldProgression.CanComplete(profile, state, id, defeated) != DemoCampaignProgression.CanComplete(profile, state, id, defeated))
                                    { mismatched++; first ??= "CanComplete(" + id + ")"; }
                                }
                            }
                        foreach (var id in stageIds)
                        {
                            compared++;
                            var a = id == null ? Array.Find(profile.Stages, s => s.Id == null) : Array.Find(profile.Stages, s => s.Id == id);
                            if (!ReferenceEquals(a, profile.FindStage(id))) { mismatched++; first ??= "FindStage(" + id + ")"; }
                        }
                        report.Check(mismatched == 0, "campaign-identity " + Path.GetFileName(path) + (explicitMode ? " explicit" : " ordered") + ": " + compared + " old/new answers, " + mismatched + " differ" + (first != null ? " (first: " + first + ")" : ""));
                    }
                    finally { Object.DestroyImmediate(profile); }
                }
            }
        }

        // Campaign states: every ordered prefix, plus seeded random completion subsets with their granted facts and some evidence.
        static IEnumerable<DemoCampaignState> States(DemoCampaignProfile profile, int random)
        {
            var stages = profile.Stages;
            for (int k = 0; k <= stages.Length; k++) yield return StateOf(profile, stages.Take(k));
            var rng = new System.Random(307);
            for (int n = 0; n < random; n++) yield return StateOf(profile, stages.Where(_ => rng.NextDouble() < .5), rng.NextDouble() < .5);
            yield return new DemoCampaignState { CampaignId = "perf307_other" };
            yield return new DemoCampaignState { CampaignId = profile.CampaignId, Completed = null };
            yield return null;
        }
        static DemoCampaignState StateOf(DemoCampaignProfile profile, IEnumerable<DemoCampaignProfile.Stage> done, bool evidence = false)
        {
            var state = new DemoCampaignState { CampaignId = profile.CampaignId };
            foreach (var stage in done)
            {
                if (!state.Completed.Contains(stage.Id)) state.Completed.Add(stage.Id);
                foreach (var fact in stage.GrantedFacts ?? Array.Empty<string>()) if (!state.Facts.Contains(fact)) state.Facts.Add(fact);
                if (evidence) foreach (var id in stage.RequiredDefeatedIds ?? Array.Empty<string>()) if (!state.EncounterEvidence.Contains(id)) state.EncounterEvidence.Add(id);
            }
            return state;
        }
        static IEnumerable<ICollection<string>> DefeatedSets(DemoCampaignProfile profile)
        {
            yield return null;
            yield return new List<string>();
            yield return profile.Stages.SelectMany(s => s.RequiredDefeatedIds ?? Array.Empty<string>()).Distinct().ToList();
        }

        // ------------------------------------------------------------------ save-bytes
        // Pre-#307 AtomicJsonStore.Save, verbatim (reference writer).
        static void OldSave(string path, WorldMacroProgress state)
        {
            if (state == null || !WorldMacroProgress.Valid(state)) throw new InvalidDataException("Refusing invalid save state");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8, 1024, true)) { writer.Write(JsonUtility.ToJson(state, true)); writer.Flush(); }
                stream.Flush(true);
            }
            bool primaryValid = false;
            if (File.Exists(path)) try { var previous = JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(path)); primaryValid = previous != null && WorldMacroProgress.Valid(previous); } catch (ArgumentException) { }
            if (File.Exists(path)) File.Replace(temporary, path, primaryValid ? path + ".bak" : null);
            else File.Move(temporary, path);
        }

        static WorldMacroProgress Fixture(int step)
        {
            var state = WorldMacroProgress.CreateNew("perf307-fixture", new Vector3(1234.5f + step * .1f, 56.25f, -789.125f), 17.5f + step);
            state.ledger.currency = 37 + step * 11; state.ledger.hp = step % 2 == 0 ? 1f : .625f; state.ledger.ink = .5f;
            state.ledger.completed.Add("perf307_step_" + step);
            return state;
        }

        static void SaveBytes(Report report)
        {
            string root = Path.Combine(Application.temporaryCachePath, "Perf307Checks", Guid.NewGuid().ToString("N"));
            try
            {
                string a = Path.Combine(root, "old", "slot.json"), b = Path.Combine(root, "sync", "slot.json"), c = Path.Combine(root, "background", "slot.json");
                var sync = new AtomicJsonStore<WorldMacroProgress>(b, WorldMacroProgress.Valid);
                var background = new AtomicJsonStore<WorldMacroProgress>(c, WorldMacroProgress.Valid);
                var states = Enumerable.Range(0, 6).Select(Fixture).ToArray();
                report.Check(states.All(WorldMacroProgress.Valid), "save-bytes: fixtures are valid progress states");
                for (int step = 0; step < states.Length; step++)
                {
                    if (step == 3)
                    {
                        // A corrupt primary written by someone else: the stamp no longer matches, so validity is re-read from disk.
                        foreach (var path in new[] { a, b, c }) File.WriteAllText(path, "corrupt " + step);
                    }
                    OldSave(a, states[step]);
                    sync.Save(states[step]);
                    background.SaveInBackground(background.Serialize(states[step]), step);
                    background.WaitForBackground();
                    bool took = background.TryTakeBackgroundResult(out var result);
                    report.Check(took && result.Error == null && Equals(result.Tag, step), "save-bytes step " + step + ": background commit reported success for its own tag" + (result.Error != null ? " (" + result.Error.Message + ")" : ""));
                    foreach (string suffix in new[] { "", ".bak", ".tmp" })
                    {
                        bool same = SameFile(a + suffix, b + suffix) && SameFile(a + suffix, c + suffix);
                        report.Check(same, "save-bytes step " + step + ": " + (suffix.Length == 0 ? "primary" : suffix) + " bytes identical (old / sync / background)" + (same ? "" : " " + Describe(a + suffix, b + suffix, c + suffix)));
                    }
                }
                // Newest wins: many enqueued commits -> the last state is on disk, every reported result is a real commit in order.
                string d = Path.Combine(root, "burst", "slot.json"); var burst = new AtomicJsonStore<WorldMacroProgress>(d, WorldMacroProgress.Valid);
                for (int step = 0; step < states.Length; step++) burst.SaveInBackground(burst.Serialize(states[step]), step);
                burst.WaitForBackground();
                var tags = new List<int>(); bool errors = false;
                while (burst.TryTakeBackgroundResult(out var r)) { tags.Add((int)r.Tag); errors |= r.Error != null; }
                bool ordered = tags.Count >= 1 && tags.SequenceEqual(tags.OrderBy(t => t)) && tags[tags.Count - 1] == states.Length - 1;
                report.Check(!errors && ordered && File.ReadAllText(d) == burst.Serialize(states[states.Length - 1]),
                    "save-bytes: newest-wins writer committed " + tags.Count + "/" + states.Length + " jobs in order, last state on disk (" + string.Join(",", tags) + ")");
                // A sync Save right after enqueueing waits for the writer: its state is the one on disk.
                burst.SaveInBackground(burst.Serialize(states[1]), "late"); burst.Save(states[2]);
                while (burst.TryTakeBackgroundResult(out _)) { }
                report.Check(File.ReadAllText(d) == burst.Serialize(states[2]), "save-bytes: synchronous Save waits for the background writer (no reordering)");
                // Errors surface as a result, not a throw on the caller.
                string blocker = Path.Combine(root, "blocker"); Directory.CreateDirectory(Path.GetDirectoryName(blocker)); File.WriteAllText(blocker, "file, not a directory");
                var failing = new AtomicJsonStore<WorldMacroProgress>(Path.Combine(blocker, "slot.json"), WorldMacroProgress.Valid);
                failing.SaveInBackground(failing.Serialize(states[0]), "fail"); failing.WaitForBackground();
                bool failed = failing.TryTakeBackgroundResult(out var failure) && failure.Error != null &&
                    (failure.Error is IOException || failure.Error is UnauthorizedAccessException || failure.Error is ArgumentException);
                report.Check(failed, "save-bytes: a failing background commit reports its IO error (the session turns it into SaveError on the next Update)" + (failure.Error != null ? " (" + failure.Error.GetType().Name + ")" : ""));
                bool syncThrows = false; try { failing.Save(states[0]); } catch (IOException) { syncThrows = true; } catch (UnauthorizedAccessException) { syncThrows = true; }
                report.Check(syncThrows, "save-bytes: synchronous Save still throws the filesystem failure to its caller");
                bool invalidThrows = false; var invalid = Fixture(0); invalid.ledger.currency = -1;
                try { sync.Serialize(invalid); } catch (InvalidDataException) { invalidThrows = true; }
                report.Check(invalidThrows, "save-bytes: Serialize refuses an invalid state with InvalidDataException (as Save did)");
            }
            finally { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        static bool SameFile(string x, string y)
        {
            bool ex = File.Exists(x), ey = File.Exists(y);
            if (ex != ey) return false;
            return !ex || File.ReadAllBytes(x).SequenceEqual(File.ReadAllBytes(y));
        }
        static string Describe(params string[] paths) => "[" + string.Join(" | ", paths.Select(p => File.Exists(p) ? new FileInfo(p).Length + " B" : "missing")) + "]";

        // ------------------------------------------------------------------ play helpers
        static WorldMacroPlaytestSession Session(Report report)
        {
            var s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (s == null || !s.InitializationComplete || s.Walker == null || s.Walker.Body == null) { report.Fail("no initialised WorldMacroPlaytestSession in the loaded scenes (open W_Demo_Main from the lobby first)"); return null; }
            return s;
        }
        static object Get(object target, string name)
        {
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, Any); if (f != null) return f.GetValue(target);
                var p = t.GetProperty(name, Any); if (p != null) return p.GetValue(target);
            }
            throw new MissingMemberException(target.GetType().Name, name);
        }
        static object Call(object target, string name, Type[] types, params object[] args)
        {
            var m = target.GetType().GetMethod(name, Any, null, types, null) ?? throw new MissingMethodException(target.GetType().Name, name);
            return m.Invoke(target, args);
        }
        static bool Same(Vector3 x, Vector3 y) => x.x.Equals(y.x) && x.y.Equals(y.y) && x.z.Equals(y.z);
        static string F(Vector3 v) => v.x.ToString("R", CultureInfo.InvariantCulture) + "," + v.y.ToString("R", CultureInfo.InvariantCulture) + "," + v.z.ToString("R", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ focus-identity
        // Pre-#307 CanInteract from an explicit body position (the view is still built by FindInteractionPoint, whose Position and
        // Radius the new geometry path must reproduce), and the pre-#307 focus loop.
        static bool OldGate(WorldMacroPlaytestSession s)
        {
            var w = s.Walker;
            return (bool)Get(s, "ready") && !(bool)Get(s, "HasPendingDefeats") && !s.GameplayInputBlocked && !w.Seated && w.Motor.enabled && !w.Motor.IsDrawing && !w.Drawing.InDrawMode;
        }
        static PrologueContentSO.Point OldFind(WorldMacroPlaytestSession s, string id) =>
            (PrologueContentSO.Point)Call(s, "FindInteractionPoint", new[] { typeof(string) }, id);
        static bool OldCanInteractAt(WorldMacroPlaytestSession s, Vector3 body, string id)
        {
            if (!OldGate(s)) return false;
            var p = OldFind(s, id);
            bool lost = id == "CurrencyDrop" && s.Progress.ledger.dropCurrency > 0;
            if (p == null && !lost) return false;
            if (!lost && (bool)Call(s, "ShortcutDoorOpened", new[] { typeof(string) }, id)) return false;
            Vector3 target = lost ? s.Progress.ledger.dropPosition : p.Position;
            if (Vector3.Distance(body, target) > (lost ? 2.5f : p.Radius)) return false;
            Vector3 eye = body + Vector3.up * s.Walker.EyeHeight, to = target + Vector3.up * 1.25f - eye;
            foreach (var h in Physics.RaycastAll(eye, to.normalized, to.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.transform.IsChildOf(s.Walker.Body.transform)) continue;
                var point = h.transform.GetComponentInParent<WorldMacroContentPoint>(); if (point != null && point.Id == id) continue;
                return false;
            }
            return true;
        }
        static bool OldCanInteractAt(WorldMacroPlaytestSession s, Vector3 body, WorldMacroFragmentPickup pickup)
        {
            bool input = (bool)Call(s, "CanUseInteractionInput", Type.EmptyTypes);
            if (pickup == null || !pickup.isActiveAndEnabled || !input) return false;
            if (!WorldMacroCollectionCatalog.TryGetBundle(pickup.BundleId, out var bundle) || WorldMacroInventoryService.IsBundleCollected(s.Progress.ui, bundle)) return false;
            Vector3 target = pickup.InteractionPosition;
            if (Vector3.Distance(body, target) > pickup.Radius) return false;
            Vector3 eye = body + Vector3.up * s.Walker.EyeHeight, to = target + Vector3.up * .5f - eye;
            foreach (var hit in Physics.RaycastAll(eye, to.normalized, to.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(s.Walker.Body.transform) || hit.transform.IsChildOf(pickup.transform)) continue;
                return false;
            }
            return true;
        }
        static void OldFocus(WorldMacroPlaytestSession s, Vector3 body, PrologueContentSO.Point[] points, List<WorldMacroFragmentPickup> pickups, out string id, out string bundle)
        {
            id = null; bundle = null; float closest = float.MaxValue;
            foreach (var p in points) if (OldCanInteractAt(s, body, p.Id)) { float distance = Vector3.Distance(OldFind(s, p.Id).Position, body); if (distance < closest) { id = p.Id; closest = distance; } }
            if (OldCanInteractAt(s, body, "CurrencyDrop")) { id = "CurrencyDrop"; closest = Vector3.Distance(s.Progress.ledger.dropPosition, body); }
            foreach (var pickup in pickups)
            {
                if (!OldCanInteractAt(s, body, pickup)) continue; float distance = Vector3.Distance(pickup.InteractionPosition, body);
                if (distance < closest) { id = null; bundle = pickup.BundleId; closest = distance; }
            }
        }

        static void FocusIdentity(Report report)
        {
            var s = Session(report); if (s == null) return;
            var points = (PrologueContentSO.Point[])Get(s, "InteractionPoints");
            var pickups = (List<WorldMacroFragmentPickup>)Get(s, "fragmentPickups");
            var canAt = s.GetType().GetMethod("CanInteractAt", Any, null, new[] { typeof(Vector3), typeof(string) }, null);
            var canPickupAt = s.GetType().GetMethod("CanInteractAt", Any, null, new[] { typeof(Vector3), typeof(WorldMacroFragmentPickup) }, null);
            var resolve = s.GetType().GetMethod("ResolveFocus", Any);
            var raw = s.GetType().GetMethod("RawInteractionPoint", Any, null, new[] { typeof(string) }, null);
            var geometry = s.GetType().GetMethod("TryInteractionGeometry", Any);
            if (canAt == null || canPickupAt == null || resolve == null || raw == null || geometry == null) { report.Fail("focus-identity: #307 session members missing (stage not applied?)"); return; }
            report.Check(OldGate(s) == (bool)Call(s, "InteractionGateOpen", Type.EmptyTypes), "focus-identity: interaction gate identical (" + OldGate(s) + ")");
            if (!OldGate(s)) report.Note("focus-identity: the interaction gate is closed now (seated, drawing, blocked or pending defeats): point answers compare as all-false; stand in the world to exercise the geometry and sight paths");

            // Lookup, geometry and views per point
            int lookups = 0, lookupBad = 0, views = 0, viewBad = 0; string firstBad = null;
            foreach (var p in points.Where(x => x != null).Concat(new PrologueContentSO.Point[] { null }))
            {
                string id = p?.Id ?? "perf307_missing";
                var expected = Array.Find(points, x => x.Id == id);
                lookups++; if (!ReferenceEquals(expected, raw.Invoke(s, new object[] { id }))) { lookupBad++; firstBad ??= "lookup " + id; }
                var view = OldFind(s, id); var again = OldFind(s, id);
                var args = new object[] { id, null, null }; bool known = (bool)geometry.Invoke(s, args);
                views++;
                bool viewOk = view == null ? !known : known && Same(view.Position, (Vector3)args[1]) && view.Radius.Equals((float)args[2]);
                if (view != null && expected != null && !ReferenceEquals(view, expected) && view.Id != "wangso_w1") viewOk &= ReferenceEquals(view, again) && ExpectedView(s, expected, view);
                if (!viewOk) { viewBad++; firstBad ??= "view/geometry " + id; }
            }
            report.Check(lookupBad == 0, "focus-identity: Id lookup = Array.Find for " + lookups + " ids (" + lookupBad + " differ)");
            report.Check(viewBad == 0, "focus-identity: geometry = view Position/Radius and cached views = freshly built views for " + views + " ids (" + viewBad + " differ)" + (firstBad != null ? " first: " + firstBad : ""));

            // Jittered bodies around every point (inside/outside the radius), the player's own position and the pickups
            var bodies = new List<Vector3> { s.Walker.Body.transform.position };
            foreach (var p in points.Where(x => x != null))
                foreach (float f in new[] { .5f, .97f, 1.04f })
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * Mathf.PI / 3 + f; var at = OldFind(s, p.Id)?.Position ?? p.Position;
                        bodies.Add(at + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * (p.Radius * f) + Vector3.down * .05f);
                    }
            foreach (var pickup in pickups.Where(x => x != null))
                for (int k = 0; k < 6; k++) { float a = k * Mathf.PI / 3; bodies.Add(pickup.InteractionPosition + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * (pickup.Radius * (k % 2 == 0 ? .6f : 1.03f))); }
            int answers = 0, differ = 0, focusDiffer = 0, trueAnswers = 0; string first = null;
            var ids = points.Where(x => x != null).Select(x => x.Id).Distinct().Concat(new[] { "CurrencyDrop", "perf307_missing" }).ToArray();
            foreach (var body in bodies)
            {
                foreach (var id in ids)
                {
                    bool oldAnswer = OldCanInteractAt(s, body, id);
                    bool newAnswer = OldGate(s) && (bool)canAt.Invoke(s, new object[] { body, id });
                    answers++; if (oldAnswer) trueAnswers++;
                    if (oldAnswer != newAnswer) { differ++; first ??= id + " at " + F(body) + " old=" + oldAnswer; }
                }
                foreach (var pickup in pickups.Where(x => x != null))
                {
                    answers++;
                    if (OldCanInteractAt(s, body, pickup) != (bool)canPickupAt.Invoke(s, new object[] { body, pickup })) { differ++; first ??= "pickup " + pickup.BundleId + " at " + F(body); }
                }
                OldFocus(s, body, points, pickups, out var oldId, out var oldBundle);
                var focusArgs = new object[] { body, null, null }; resolve.Invoke(s, focusArgs);
                if (oldId != (string)focusArgs[1] || oldBundle != (string)focusArgs[2]) { focusDiffer++; first ??= "focus at " + F(body) + " old=" + oldId + "/" + oldBundle + " new=" + focusArgs[1] + "/" + focusArgs[2]; }
            }
            report.Check(differ == 0, "focus-identity: CanInteract old = new for " + answers + " (position, point) pairs, " + trueAnswers + " reachable (" + differ + " differ)" + (first != null ? " first: " + first : ""));
            report.Check(focusDiffer == 0, "focus-identity: focused point/bundle old = new at " + bodies.Count + " body positions (" + focusDiffer + " differ)");
            report.Check(s.CanInteract("perf307_missing") == false, "focus-identity: unknown id rejected");

            // Allocation per focus pass at the player's position (the old loop allocated views, iterators and closures per point)
            Vector3 here = s.Walker.Body.transform.position;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) OldFocus(s, here, points, pickups, out _, out _);
            long oldBytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 20;
            var args2 = new object[] { here, null, null };
            resolve.Invoke(s, args2);   // warm (reflection argument boxing is outside the measured loop below)
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) resolve.Invoke(s, args2);
            long newBytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 20;
            report.Note("focus-identity: bytes allocated per focus pass here - reference loop (includes reflection) " + oldBytes + " B, #307 ResolveFocus (via reflection, incl. its boxing) " + newBytes + " B; the Profiler marker Oh.Session.Focus is the in-game measure");
        }
        // The view the pre-#307 DemoInteractionPoint built for this point now (no cache).
        static bool ExpectedView(WorldMacroPlaytestSession s, PrologueContentSO.Point original, PrologueContentSO.Point view)
        {
            var content = s.Content; var progress = s.Progress;
            if (content?.Campaign == null || progress?.campaign == null || progress.campaign.CampaignId != content.Campaign.CampaignId) return ReferenceEquals(view, original);
            var step = OldProgression.ForEvent(content.Campaign, progress.campaign, DemoEventKind.Interaction, original.Id, progress.defeated);
            string prompt, text; string[] lines;
            if (content.Campaign.UseExplicitPrerequisites)
            {
                text = content.Campaign.DialogueFor(original.Id, progress.campaign, step?.Dialogue ?? original.Text);
                prompt = string.IsNullOrEmpty(step?.Prompt) ? original.Prompt : step.Prompt; lines = text == original.Text ? original.Lines : Array.Empty<string>();
                if (view.RequiredCompleted != original.RequiredCompleted || view.RequiredDefeated != original.RequiredDefeated || view.LockedText != original.LockedText) return false;
            }
            else
            {
                if (step == null || !step.Implemented || step.Event != DemoEventKind.Interaction || step.TriggerId != original.Id) return ReferenceEquals(view, original);
                prompt = string.IsNullOrEmpty(step.Prompt) ? original.Prompt : step.Prompt; text = string.IsNullOrEmpty(step.Dialogue) ? original.Text : step.Dialogue;
                lines = string.IsNullOrEmpty(step.Dialogue) ? original.Lines : Array.Empty<string>();
            }
            return view.Id == original.Id && view.Kind == original.Kind && Same(view.Position, original.Position) && view.Radius.Equals(original.Radius) &&
                view.Currency == original.Currency && view.Prompt == prompt && view.Text == text && view.Speaker == original.Speaker && view.Services == original.Services &&
                (view.Lines ?? Array.Empty<string>()).SequenceEqual(lines ?? Array.Empty<string>());
        }

        // ------------------------------------------------------------------ feet-identity
        // Pre-#307 TrySafeFeet loop body, verbatim (without the natural-solid probe, which the check drives separately).
        static bool OldSafeFeetCore(WorldMacroPlaytestSession s, Vector3 candidate, out Vector3 feet)
        {
            feet = default; var traversal = s.Traversal; var body = s.Walker.Body;
            foreach (var h in Physics.RaycastAll(candidate + Vector3.up * 1.5f, Vector3.down, 4, 1, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (traversal != null && !traversal.IsPermanentDrySupport(h.point, h.collider)) continue;
                if (h.transform.IsChildOf(body.transform) || h.normal.y < Mathf.Cos(body.slopeLimit * Mathf.Deg2Rad)) continue;
                var p = h.point + Vector3.up * .05f;
                bool blocked = Physics.OverlapCapsule(p + Vector3.up * .31f, p + Vector3.up * 1.47f, .27f, ~0, QueryTriggerInteraction.Ignore).Any(c => !c.transform.IsChildOf(body.transform));
                if (blocked) continue;
                bool supported = true;
                foreach (var offset in new[] { Vector3.right * .25f, Vector3.left * .25f, Vector3.forward * .25f, Vector3.back * .25f })
                    supported &= Physics.Raycast(p + offset + Vector3.up * .3f, Vector3.down, .65f, 1, QueryTriggerInteraction.Ignore);
                if (supported) { feet = p; return true; }
            }
            return false;
        }

        static void FeetIdentity(Report report)
        {
            var s = Session(report); if (s == null) return;
            var core = s.GetType().GetMethod("SafeFeetCore", Any);
            if (core == null) { report.Fail("feet-identity: SafeFeetCore missing (stage not applied?)"); return; }
            Vector3 here = s.Walker.Body.transform.position;
            var candidates = new List<Vector3> { here, s.LastSafeFeet };
            for (int x = -6; x <= 6; x++) for (int z = -6; z <= 6; z++) candidates.Add(here + new Vector3(x * 1.7f, .2f, z * 1.7f));
            if (s.Content != null)
            {
                candidates.Add(s.Content.StartFeet);
                foreach (var checkpoint in s.Content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>()) if (checkpoint != null) candidates.Add(checkpoint.Feet);
                foreach (var p in s.Content.Points ?? Array.Empty<PrologueContentSO.Point>()) if (p != null) candidates.Add(p.Position);
            }
            int compared = 0, differ = 0, found = 0; string first = null;
            foreach (var c in candidates)
            {
                bool oldOk = OldSafeFeetCore(s, c, out var oldFeet);
                var args = new object[] { c, null }; bool newOk = (bool)core.Invoke(s, args); var newFeet = (Vector3)args[1];
                compared++; if (oldOk) found++;
                if (oldOk != newOk || !Same(oldFeet, newFeet)) { differ++; first ??= F(c) + " old=" + oldOk + " " + F(oldFeet) + " new=" + newOk + " " + F(newFeet); }
            }
            report.Check(differ == 0, "feet-identity: TrySafeFeet core old = new, bit for bit, at " + compared + " candidates (" + found + " safe, " + differ + " differ)" + (first != null ? " first: " + first : ""));

            // Covers soundness: where the walker-feet probe is now skipped, an EnsureAround there changes no result near the walker.
            var pool = Object.FindFirstObjectByType<CompactNaturalSolids>();
            float margin = s.NaturalCoverMargin;
            if (pool == null || !pool.Ready) report.Note("feet-identity: no prepared CompactNaturalSolids in the scene - Covers not exercised");
            else
            {
                var near = new List<Vector3> { here };
                for (int k = 0; k < 8; k++) { float a = k * Mathf.PI / 4; near.Add(here + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 1.5f); }
                var covered = near.Where(p => pool.Covers(p, margin)).ToList();
                var beforeFeet = covered.Select(p => { var args = new object[] { p, null }; bool ok = (bool)core.Invoke(s, args); return (ok, (Vector3)args[1]); }).ToList();
                if (covered.Count > 0)
                {
                    foreach (var p in covered) pool.EnsureAround(p);
                    Physics.SyncTransforms();
                    int changed = 0;
                    for (int i = 0; i < covered.Count; i++)
                    {
                        var args = new object[] { covered[i], null }; bool ok = (bool)core.Invoke(s, args);
                        if (ok != beforeFeet[i].ok || !Same((Vector3)args[1], beforeFeet[i].Item2)) changed++;
                    }
                    Call(s, "RestoreNaturalFocus", Type.EmptyTypes);
                    report.Check(changed == 0, "feet-identity: Covers(margin " + margin.ToString("0.##", CultureInfo.InvariantCulture) + " m) held at " + covered.Count + "/" + near.Count + " walker-near points; an extra EnsureAround there changed " + changed + " results");
                }
                else report.Note("feet-identity: Covers was false at all " + near.Count + " walker-near points now (backlog, height or pending live change) - the old probe path runs there; walk a few metres and re-run");
            }

            // Foot skinning: matrix path vs per-influence TransformPoint on the current pose (float round-off only)
            var foot = Object.FindFirstObjectByType<WorldMacroPlayerFootPlacement>();
            if (foot == null || !foot.IsBound) report.Note("feet-identity: no bound WorldMacroPlayerFootPlacement - skinning delta not measured");
            else
            {
                float delta = foot.MeasureSkinningDelta307(out float magnitude);
                float tolerance = Mathf.Max(1e-4f, magnitude * 8f / (1 << 23));   // 1e-4 m, or 8 float steps at this world coordinate
                report.Check(delta <= tolerance, "feet-identity: sole skinning matrix path within " + tolerance.ToString("0.#####", CultureInfo.InvariantCulture) + " m of TransformPoint (max " + delta.ToString("0.########", CultureInfo.InvariantCulture) + " m at |coord| " + magnitude.ToString("0.#", CultureInfo.InvariantCulture) + ")");
            }
        }

        // ------------------------------------------------------------------ gesture-identity
        static void GestureIdentity(Report report)
        {
            var rigs = Object.FindObjectsByType<WorldMacroPlayerGestureRig>(FindObjectsSortMode.None);
            if (rigs.Length == 0) { report.Note("gesture-identity: no WorldMacroPlayerGestureRig in the scene"); return; }
            foreach (var rig in rigs)
            {
                float delta = rig.MeasureZeroParticipationTorsoDelta307();
                if (float.IsNaN(delta)) { report.Note("gesture-identity: " + rig.name + " unbound"); continue; }
                report.Check(delta < 1e-6f, "gesture-identity: " + rig.name + " ApplyTorso(0) changes the pose by " + delta.ToString("0.#########", CultureInfo.InvariantCulture) + " (< 1e-6 required for the zero-participation skip)");
            }
        }

        // ------------------------------------------------------------------ cull-colliders
        static void CullColliders(Report report)
        {
            var s = Session(report); if (s == null) return;
            Vector3 body = s.Walker.Body.transform.position; float margin = Mathf.Max(0, s.BossColliderActivationMargin);
            int checkedActors = 0;
            foreach (var actor in s.Actors)
            {
                if (actor == null) continue;
                var spec = Array.Find(s.Content.Encounters, x => x.Id == actor.Id); if (spec == null) continue;
                bool special = actor.Id == WorldMacroPlaytestSession.CheongryongId || actor.Id == WorldMacroPlaytestSession.SouthGateGeneralId || spec.ContentId == DemoGrowthLessonLink.LessonId;
                if (!special) continue;
                checkedActors++;
                bool available = (bool)Call(s, "DemoEncounterAvailable", new[] { typeof(string), typeof(string) }, actor.Id, spec.ContentId);
                var vitals = actor.GetComponent<Oheangbu.Combat.EnemyVitals>();
                float distance = Vector3.Distance(body, actor.transform.position);
                bool expected = available && vitals != null && vitals.IsAlive && distance < spec.Activation + margin;
                var colliders = actor.GetComponentsInChildren<Collider>();
                int wrong = colliders.Count(c => c.enabled != expected);
                var sync = actor.GetComponentInChildren<CheongryongColliderPoseSync>();
                report.Check(wrong == 0, "cull-colliders: " + actor.Id + " at " + distance.ToString("0.0", CultureInfo.InvariantCulture) + " m (Activation " + spec.Activation + " + " + margin + "): " + colliders.Length + " colliders " + (expected ? "on" : "off") + " as expected, " + wrong + " wrong" +
                    (sync != null ? "; pose-sync flushes so far " + sync.SynchronizationCount : "") + " (Cull runs at 5 Hz: re-run if you just crossed the distance)");
            }
            if (checkedActors == 0) report.Note("cull-colliders: no dragon/general/lesson actor in this scene");
        }

        // ------------------------------------------------------------------ rig-skip
        static void RigSkip(Report report)
        {
            var sb = new StringBuilder();
            int total = 0, skipping = 0;
            void Row(Component c, int skipped, IEnumerable<Renderer> renderers, bool active)
            {
                total++; bool drawn = renderers != null && renderers.Any(r => r != null && r.enabled && r.gameObject.activeInHierarchy);
                if (skipped > 0) skipping++;
                sb.Append("\n     ").Append(c.GetType().Name).Append(' ').Append(c.name).Append(": skipped ").Append(skipped).Append(active ? ", active" : ", inactive").Append(drawn ? ", drawn" : ", hidden");
            }
            foreach (var r in Object.FindObjectsByType<EnemyRigMotion298>(FindObjectsSortMode.None))
                Row(r, r.SkippedEvaluations, r.Animator != null ? r.Animator.GetComponentsInChildren<Renderer>(true) : null, r.Enemy != null && r.Enemy.isActiveAndEnabled || r.General != null && r.General.isActiveAndEnabled);
            foreach (var r in Object.FindObjectsByType<EnemyRigMotion273>(FindObjectsSortMode.None))
                Row(r, r.SkippedEvaluations, r.Animator != null ? r.Animator.GetComponentsInChildren<Renderer>(true) : null, r.Enemy != null && r.Enemy.isActiveAndEnabled);
            foreach (var r in Object.FindObjectsByType<SinmokRigAnimation>(FindObjectsSortMode.None))
                Row(r, r.SkippedEvaluations, r.Animator != null ? r.Animator.GetComponentsInChildren<Renderer>(true) : null, r.Combat != null && r.Combat.isActiveAndEnabled);
            foreach (var r in Object.FindObjectsByType<SouthGateGeneralPresentation>(FindObjectsSortMode.None)) Row(r, r.SkippedEvaluations, r.GetComponentsInChildren<Renderer>(true), false);
            foreach (var r in Object.FindObjectsByType<CheongryongRigAnimation>(FindObjectsSortMode.None)) Row(r, r.SkippedEvaluations, r.GetComponentsInChildren<Renderer>(true), false);
            report.Note("rig-skip: " + total + " presentation rigs, " + skipping + " have skipped frames (a skip needs inactive + no drawn renderer; a drawn rig never skips - run twice: a drawn rig's counter must not rise)" + sb);
        }
    }
}
