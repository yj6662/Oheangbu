using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-1 repair ledger (SPEC-WORLD-BUILDING-AUDIT-308 §D). Frame = Escort303Fix: plan -> backup -> apply -> verify, the
    // command opens its own scene and goes back. One idempotent ledger applied to the three scenes in order (#296 candidate ->
    // #298 candidate -> W_Demo_Main); no re-promotion. Queue entry Run(string), refusals are "refused: …" strings, never dialogs:
    //   plan:<architecture296|folklore298|main>          read only -> BuildingAudit308/Fix/plan-<alias>.json
    //   apply:<alias>[:F1,F3,…]                          F1 F3 F4 F5; backs the scene file up to SceneBackup/<alias>-<utc>.unity,
    //                                                     saves only the target scene, appends Fix/ledger-<alias>.json (SHA before/after)
    //   apply-all                                        plans all three first; any blocker = nothing changes; then applies in order
    //   sheet-plan | sheet-apply                         F2, the shared DryLandscape sheet, once (Fix/sheet-plan.json, sheet-removals.json)
    //   verify:<alias>                                   post-conditions, read only -> Fix/verify-<alias>-<utc>.json
    //   revert:<alias>[:<F1|F3|F4|F5>]                   back to the ledger "before" values (or the exact scene backup when nothing
    //                                                     else changed since that apply); revert:sheet restores the sheet backup
    //   nav-prep | nav-after                             F6 around the single NavMesh bake (CompactArchitecture296 open + nav, run by
    //                                                     the main agent): C0 three-scene check + Navigation.asset backup + file snapshot,
    //                                                     then the list of files the bake's SaveAssets changed
    // Protected: Watershed295_*/Reworld292_*/MountainTrail285* objects, protected asset folders, Finish297_Attraction (hashed
    // before and after each apply). AssetDatabase.SaveAssets is never called (the sheet and new meshes use SaveAssetIfDirty).
    public static partial class BuildingFix308
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string FixDir => Path.Combine(BuildingAudit308.Folder, "Fix");
        static string LedgerFile(string alias) => Path.Combine(FixDir, "ledger-" + alias + ".json");
        static readonly string[] SceneSteps = { "F1", "F3", "F4", "F5" };

        // ------------------------------------------------------------------ config (part of BuildingAudit308/config.json "fix")

        [Serializable] internal sealed class House308 { public string path = "", label = ""; public float[] searchCentre = Array.Empty<float>(); }
        [Serializable] internal sealed class Choice308 { public string path = ""; public float[] xz = Array.Empty<float>(); }
        [Serializable] internal sealed class F1Cfg
        {
            public House308[] houses = Array.Empty<House308>(); public string parent = "";
            public float radius, step, rangeMax, buildingGap, contentRadius, propRadius, propSeatSink, propGroupLink, skirtHover, apron;
            public int maxCandidates; public string propSeat = ""; public string[] propTokens = Array.Empty<string>(), buildingRoots = Array.Empty<string>(), allowContentNear = Array.Empty<string>();
            public Choice308[] choices = Array.Empty<Choice308>(); public string floatingList = "", skirtMaterial = ""; public bool requireBeforeStill;
        }
        [Serializable] internal sealed class Expect308 { public string building = "", kind = ""; public int count; }
        [Serializable] internal sealed class F2Cfg { public string sheet = "", reason = ""; public string[] buildingRoots = Array.Empty<string>(), ids = Array.Empty<string>(); public Expect308[] expected = Array.Empty<Expect308>(); public float positionTolerance; }
        [Serializable] internal sealed class F3Cfg { public string parent = "", offsetsFile = "", seat = ""; public float seatSink, matchTolerance, maxShift;
            // a return to the object's own baseline place may stand this far inside a route corridor's edge (the corridor width is a margin for new places)
            public float homeCorridorSlack = .5f; }
        [Serializable] internal sealed class F4Cfg { public string root = "", housePart = ""; public string[] houses = Array.Empty<string>(), parts = Array.Empty<string>(); public float probeHeight, probeRadius, interactionRadius; }
        [Serializable] internal sealed class F5Cfg
        {
            public string root = "", surfacePrefix = "", holder = "", support = "", sourcePrefab = "", kcisaMeshFolder = "", meshFolder = "", quarantineList = "", quarantineRoot = "";
            public float tileMax;
        }
        [Serializable] internal sealed class F6Cfg { public string navAsset = "", snapshotRoot = ""; }
        [Serializable] internal sealed class Fix308 { public F1Cfg f1 = new F1Cfg(); public F2Cfg f2 = new F2Cfg(); public F3Cfg f3 = new F3Cfg(); public F4Cfg f4 = new F4Cfg(); public F5Cfg f5 = new F5Cfg(); public F6Cfg f6 = new F6Cfg(); }

        // ------------------------------------------------------------------ ledger / plan shapes

        [Serializable] internal sealed class Change308
        {
            public string step = "", key = "", kind = "", before = "", after = "", note = "", state = "";   // state: apply | already | mismatch | blocked
            public Vector3 at;
        }
        [Serializable] internal sealed class Candidate308 { public float x, z, ground, range, dist, corridorClearance, buildingGap; public string blocker = ""; public bool ok; }
        [Serializable] internal sealed class StepPlan308
        {
            public string step = "", target = "", status = "", detail = "";   // ready | already | clean | absent | blocked | mismatch
            public List<Change308> changes = new List<Change308>(); public List<Candidate308> candidates = new List<Candidate308>(); public List<string> notes = new List<string>();
        }
        [Serializable] internal sealed class Plan308
        {
            public string alias = "", scene = "", utc = "", sceneSha = "", configVersion = "", quality = ""; public bool blocked;
            public List<StepPlan308> steps = new List<StepPlan308>(); public List<string> blockers = new List<string>(); public List<string> notes = new List<string>();
        }
        [Serializable] internal sealed class Op308
        {
            public string utc = "", command = "", scene = "", alias = "", status = "", detail = "", backup = "", shaBefore = "", shaAfter = "", attractionBefore = "", attractionAfter = "", revertedUtc = "", quality = "";
            public string[] steps = Array.Empty<string>(); public bool reverted;
            public List<string> stepResults = new List<string>(); public List<Change308> changes = new List<Change308>();
        }
        [Serializable] internal sealed class Ledger308 { public string alias = "", scene = ""; public List<Op308> ops = new List<Op308>(); }

        internal static Ledger308 LoadLedger(string alias)
        {
            string f = LedgerFile(alias);
            return File.Exists(f) ? JsonUtility.FromJson<Ledger308>(File.ReadAllText(f)) ?? new Ledger308 { alias = alias } : new Ledger308 { alias = alias };
        }
        static void SaveLedger(Ledger308 l) { Directory.CreateDirectory(FixDir); File.WriteAllText(LedgerFile(l.alias), JsonUtility.ToJson(l, true), new UTF8Encoding(false)); }

        // the newest applied (not reverted) change of a step/key/kind in this scene's ledger
        internal static Change308 Prior(Ledger308 l, string step, string key, string kind)
        {
            for (int i = l.ops.Count - 1; i >= 0; i--)
            {
                var op = l.ops[i]; if (op.reverted) continue;
                var c = op.changes.LastOrDefault(x => x.step == step && x.key == key && x.kind == kind && (x.state == "apply" || x.state == "already"));
                if (c != null) return c;
            }
            return null;
        }

        // repair sites for C0 (after positions recorded in this scene's ledger)
        internal static IEnumerable<(string name, Vector3 c)> LedgerSites(string alias)
        {
            if (string.IsNullOrEmpty(alias) || !File.Exists(LedgerFile(alias))) yield break;
            var seen = new HashSet<string>();
            foreach (var op in LoadLedger(alias).ops.Where(o => !o.reverted && (o.status == "applied" || o.status == "already")))
                foreach (var c in op.changes.Where(c => c.state == "apply" || c.state == "already"))
                    if (c.at != Vector3.zero && seen.Add(c.step + ":" + c.key)) yield return (c.step + ":" + c.key, c.at);
        }

        // the scene steps whose newest ledger state is applied / already / clean / absent, replayed in time order so a later
        // revert (of one step or of all) takes the step back out — an "already" op written before that revert no longer counts
        internal static HashSet<string> StepsDone(Ledger308 l)
        {
            var done = new HashSet<string>();
            foreach (var op in l.ops)
            {
                if (op.command.StartsWith("revert:", StringComparison.Ordinal))
                {
                    if (op.status == "reverted") foreach (var s in op.steps) done.Remove(s);
                    continue;
                }
                if (op.reverted || (op.status != "applied" && op.status != "already")) continue;
                foreach (var st in SceneSteps)
                    if (op.stepResults.Any(r => r.StartsWith(st + " applied", StringComparison.Ordinal) || r.StartsWith(st + " already", StringComparison.Ordinal) || r.StartsWith(st + " clean", StringComparison.Ordinal) || r.StartsWith(st + " absent", StringComparison.Ordinal)))
                        done.Add(st);
            }
            return done;
        }

        // C0 judges repair-site differences as FAIL only once every scene's ledger shows every scene step (F1 F3 F4 F5) done
        internal static bool AllScenesRepaired(BuildingAudit308.Config308 cfg)
            => cfg.scenes.All(s => File.Exists(LedgerFile(s.alias)) && SceneSteps.All(StepsDone(LoadLedger(s.alias)).Contains));

        // ------------------------------------------------------------------ value encoding

        internal static string Pose(Transform t) => string.Format(Inv, "{0:R},{1:R},{2:R}|{3:R},{4:R},{5:R},{6:R}", t.localPosition.x, t.localPosition.y, t.localPosition.z, t.localRotation.x, t.localRotation.y, t.localRotation.z, t.localRotation.w);
        internal static string Pose(Vector3 p, Quaternion q) => string.Format(Inv, "{0:R},{1:R},{2:R}|{3:R},{4:R},{5:R},{6:R}", p.x, p.y, p.z, q.x, q.y, q.z, q.w);
        internal static bool ParsePose(string s, out Vector3 p, out Quaternion q)
        {
            p = Vector3.zero; q = Quaternion.identity;
            var parts = (s ?? "").Split('|'); if (parts.Length != 2) return false;
            var a = parts[0].Split(',').Select(x => float.Parse(x, Inv)).ToArray(); var b = parts[1].Split(',').Select(x => float.Parse(x, Inv)).ToArray();
            if (a.Length != 3 || b.Length != 4) return false;
            p = new Vector3(a[0], a[1], a[2]); q = new Quaternion(b[0], b[1], b[2], b[3]); return true;
        }
        internal static bool SamePose(Transform t, string pose)
            => ParsePose(pose, out var p, out var q) && Vector3.Distance(t.localPosition, p) <= .005f && Quaternion.Angle(t.localRotation, q) <= .05f;

        static string MeshState(MeshFilter f) => f == null ? "nofilter" : f.sharedMesh == null ? "" : AssetDatabase.GetAssetPath(f.sharedMesh) + "#" + f.sharedMesh.name;
        static string ColliderState(Transform t, Mesh expected)
        {
            var mcs = t.GetComponents<MeshCollider>();
            if (mcs.Length == 0) return "none";
            var m = mcs.FirstOrDefault(c => c.sharedMesh == expected && !c.convex);
            return m != null ? "MeshCollider:" + AssetDatabase.GetAssetPath(expected) + "#" + expected.name + ":convex=0" : "MeshCollider:other";
        }

        // ------------------------------------------------------------------ entry

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            try
            {
                var a = command.Split(':');
                switch (a[0])
                {
                    case "plan": return a.Length > 1 ? PlanCommand(a[1]) : "refused: plan:<architecture296|folklore298|main>";
                    case "apply": return a.Length > 1 ? Apply(a[1], a.Length > 2 ? a[2].Split(',').Where(s => s.Length > 0).ToArray() : SceneSteps, "apply:" + string.Join(":", a.Skip(1))) : "refused: apply:<alias>[:F1,F3,…]";
                    case "apply-all": return ApplyAll();
                    case "sheet-plan": return SheetPlan();
                    case "sheet-apply": return SheetApply();
                    case "verify": return a.Length > 1 ? Verify(a[1]) : "refused: verify:<alias>";
                    case "revert": return a.Length > 1 ? (a[1] == "sheet" ? SheetRevert() : Revert(a[1], a.Length > 2 ? a[2] : null)) : "refused: revert:<alias>[:<step>] | revert:sheet";
                    case "nav-prep": return NavPrep();
                    case "nav-after": return NavAfter();
                    default: return "refused: BuildingFix308 plan:<alias> | apply:<alias>[:F1,F3,F4,F5] | apply-all | sheet-plan | sheet-apply | verify:<alias> | revert:<alias>[:<step>] | revert:sheet | nav-prep | nav-after";
                }
            }
            catch (Exception e) { return "FAILED: " + e; }
        }

        // open the target (Single) when it is not active; every open scene must be clean
        static string OpenTarget(BuildingAudit308.Config308 cfg, string alias, out Scene scene, out string previous, out bool opened)
        {
            scene = default; previous = SceneManager.GetActiveScene().path; opened = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only (Play is running)";
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "refused: scripts are compiling or failed to compile";
            string path = cfg.ScenePath(alias);
            if (path == null) return "refused: unknown scene alias " + alias + " (" + string.Join(", ", cfg.scenes.Select(s => s.alias)) + ")";
            if (cfg.ProtectedAsset(path)) return "refused: protected scene " + path;
            string dirty = BuildingAudit308.DirtyOpenScene();
            if (dirty != null) return "refused: " + dirty + " has unsaved changes";
            var active = SceneManager.GetActiveScene();
            if (active.path == path) { scene = active; return null; }
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single); opened = true;
            return null;
        }
        static void GoBack(string previous, Scene scene, bool opened, bool force = false)
        {
            if (!opened || string.IsNullOrEmpty(previous) || previous == scene.path || !File.Exists(BuildingAudit308.Abs(previous))) return;
            // a failed save leaves the target open (dirty) so nothing is lost; read-only commands may leave ExecuteAlways dirt behind
            if (scene.isDirty && !force) return;
            EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
        }

        // Finish297_Attraction serialized block (every component's editor JSON, in hierarchy order); "" when absent
        internal static string AttractionHash(Scene scene, string root)
        {
            if (string.IsNullOrEmpty(root)) return "";
            var go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == root);
            if (go == null) return "absent";
            var sb = new StringBuilder();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                sb.Append(BuildingAudit308.PathOf(t)).Append('|').Append(t.gameObject.activeSelf).Append('\n');
                foreach (var c in t.GetComponents<Component>()) if (c != null) sb.Append(c.GetType().Name).Append(':').Append(EditorJsonUtility.ToJson(c)).Append('\n');
            }
            return BuildingAudit308.ShaText(sb.ToString());
        }

        static void ClearStaleSuffix(Scene scene)
        {
            foreach (var g in scene.GetRootGameObjects())
                foreach (var s in g.GetComponentsInChildren<WorldMacroPlaytestSession>(true))
                    if (WorldMacroPlaytestSession.StaleHarnessSuffix307(s.TestSaveSuffix)) { s.TestSaveSuffix = ""; EditorUtility.SetDirty(s); }
        }

        // ------------------------------------------------------------------ plan

        static string PlanCommand(string alias)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err);
            if (cfg == null) return "refused: " + err;
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            try
            {
                var plan = MakePlan(cfg, alias, scene, SceneSteps);
                WritePlan(plan);
                return PlanText(plan);
            }
            finally { GoBack(previous, scene, opened, true); }
        }

        internal static Plan308 MakePlan(BuildingAudit308.Config308 cfg, string alias, Scene scene, string[] steps)
        {
            Physics.SyncTransforms();
            var plan = new Plan308 { alias = alias, scene = scene.path, utc = BuildingAudit308.Utc(), sceneSha = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path)), configVersion = cfg.version, quality = BuildingAudit308.QualityName() };
            var ledger = LoadLedger(alias);
            foreach (var s in steps)
            {
                StepPlan308 sp;
                switch (s)
                {
                    case "F1": sp = PlanF1(cfg, scene, ledger); break;
                    case "F3": sp = PlanF3(cfg, scene, ledger); break;
                    case "F4": sp = PlanF4(cfg, scene, ledger); break;
                    case "F5": sp = PlanF5(cfg, scene, ledger); break;
                    default: sp = new StepPlan308 { step = s, status = "blocked", detail = s == "F2" ? "F2 is the shared sheet step: sheet-plan / sheet-apply" : s == "F6" ? "F6 is the NavMesh bake: nav-prep, CompactArchitecture296 open + nav, nav-after" : "unknown step" }; break;
                }
                // protected objects are never written
                foreach (var c in sp.changes.Where(c => c.state == "apply"))
                    if (cfg.ProtectedRoot(c.key.Split('/')[0]) || (!string.IsNullOrEmpty(cfg.attractionRoot) && c.key.StartsWith(cfg.attractionRoot, StringComparison.Ordinal)))
                    { c.state = "blocked"; sp.status = "blocked"; sp.notes.Add("protected object " + c.key); }
                plan.steps.Add(sp);
                if (sp.status == "blocked" || sp.status == "mismatch") { plan.blocked = true; plan.blockers.Add(s + " " + sp.status + ": " + sp.detail); }
            }
            return plan;
        }

        static void WritePlan(Plan308 p) { Directory.CreateDirectory(FixDir); File.WriteAllText(Path.Combine(FixDir, "plan-" + p.alias + ".json"), JsonUtility.ToJson(p, true), new UTF8Encoding(false)); }

        static string PlanText(Plan308 p)
        {
            var sb = new StringBuilder("plan " + p.alias + " (" + p.scene + ") sha " + p.sceneSha + " quality " + p.quality + (p.blocked ? " — BLOCKED" : "") + "\n");
            foreach (var s in p.steps)
            {
                sb.AppendLine("  " + s.step + " " + s.status + ": " + s.detail + " (" + s.changes.Count(c => c.state == "apply") + " to apply, " + s.changes.Count(c => c.state == "already") + " already)");
                foreach (var n in s.notes.Take(12)) sb.AppendLine("     · " + n);
            }
            foreach (var b in p.blockers) sb.AppendLine("  blocker: " + b);
            sb.AppendLine("-> " + Path.Combine(FixDir, "plan-" + p.alias + ".json"));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ apply

        static string Apply(string alias, string[] steps, string commandText)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err);
            if (cfg == null) return "refused: " + err;
            steps = steps.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).ToArray();
            var bad = steps.Where(s => !SceneSteps.Contains(s)).ToArray();
            if (bad.Length > 0) return "refused: scene steps are F1 F3 F4 F5 (" + string.Join(",", bad) + " — F2 = sheet-plan/sheet-apply, F6 = nav-prep/nav-after)";
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            var op = new Op308 { utc = BuildingAudit308.Utc(), command = commandText, scene = scene.path, alias = alias, steps = steps, quality = BuildingAudit308.QualityName() };
            try
            {
                var plan = MakePlan(cfg, alias, scene, steps);
                WritePlan(plan);
                if (plan.blocked) { GoBack(previous, scene, opened, true); return "refused: nothing changed — " + string.Join(" | ", plan.blockers) + "\n" + PlanText(plan); }
                var todo = plan.steps.SelectMany(s => s.changes).Where(c => c.state == "apply").ToList();
                op.attractionBefore = AttractionHash(scene, cfg.attractionRoot);
                op.shaBefore = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
                op.changes.AddRange(plan.steps.SelectMany(s => s.changes));
                op.stepResults = plan.steps.Select(s => s.step + " " + (s.changes.Any(c => c.state == "apply") ? "applied" : s.status) + ": " + s.detail).ToList();
                if (todo.Count == 0)
                {
                    op.status = "already"; op.detail = "no change (idempotent); scene not saved"; op.shaAfter = op.shaBefore; op.attractionAfter = op.attractionBefore;
                    var ledger0 = LoadLedger(alias); ledger0.scene = scene.path; ledger0.ops.Add(op); SaveLedger(ledger0);
                    GoBack(previous, scene, opened, true);
                    return "already: " + alias + " — no change, scene SHA " + op.shaBefore + "\n" + string.Join("\n", op.stepResults);
                }
                string backupDir = Path.Combine(BuildingAudit308.Folder, "SceneBackup"); Directory.CreateDirectory(backupDir);
                op.backup = Path.Combine(backupDir, alias + "-" + op.utc + ".unity");
                File.Copy(BuildingAudit308.Abs(scene.path), op.backup, false);
                var created = new List<Object>();
                foreach (var c in todo) Execute(cfg, scene, c, created);
                // F1: after the houses moved, a plate that still floats gets the dry stone skirt (evaluated on the moved geometry)
                if (steps.Contains("F1")) op.changes.AddRange(SkirtAfterMove(cfg, scene, op.changes.Where(c => c.step == "F1" && c.kind == "pose" && c.note.StartsWith("house", StringComparison.Ordinal)).ToList(), created));
                foreach (var o in created) if (o != null && AssetDatabase.Contains(o)) AssetDatabase.SaveAssetIfDirty(o);
                ClearStaleSuffix(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) { op.status = "FAILED"; op.detail = "SaveScene returned false; the scene stays open and dirty; backup " + op.backup; }
                else
                {
                    op.shaAfter = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
                    op.attractionAfter = AttractionHash(scene, cfg.attractionRoot);
                    op.status = op.attractionAfter == op.attractionBefore ? "applied" : "FAILED";
                    op.detail = todo.Count + " change(s) saved" + (op.attractionAfter == op.attractionBefore ? "" : "; Finish297_Attraction block CHANGED — revert with revert:" + alias);
                }
                var ledger = LoadLedger(alias); ledger.scene = scene.path; ledger.ops.Add(op); SaveLedger(ledger);
                GoBack(previous, scene, opened);
                return op.status + ": " + alias + " sha " + op.shaBefore + " -> " + op.shaAfter + ", backup " + op.backup + "\n" + string.Join("\n", op.stepResults) + "\n" + op.detail;
            }
            catch (Exception e)
            {
                op.status = "FAILED"; op.detail = e.ToString();
                // nothing was saved (SaveScene clears the dirty flag): drop the half-applied in-memory edits by reloading the file, so
                // the editor is left clean and the next queue call is not refused for a dirty scene
                string discarded = "";
                try
                {
                    if (scene.IsValid() && scene.isDirty && string.IsNullOrEmpty(op.shaAfter))
                    {
                        string path = scene.path;
                        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                        discarded = "; unsaved edits discarded (scene reloaded from disk, SHA " + BuildingAudit308.Sha(BuildingAudit308.Abs(path)) + ")";
                        if (opened && !string.IsNullOrEmpty(previous) && previous != path && File.Exists(BuildingAudit308.Abs(previous))) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                    }
                }
                catch (Exception e2) { discarded = "; reload failed, the scene is still open and dirty: " + e2.Message; }
                op.detail += discarded;
                var ledger = LoadLedger(alias); ledger.ops.Add(op); SaveLedger(ledger);
                return "FAILED: " + alias + " — " + e.Message + discarded + (string.IsNullOrEmpty(op.backup) ? "" : " (backup " + op.backup + ")");
            }
        }

        static string ApplyAll()
        {
            var cfg = BuildingAudit308.LoadConfig(out string err);
            if (cfg == null) return "refused: " + err;
            var order = new[] { "architecture296", "folklore298", "main" };
            var lines = new List<string>();
            // plan every scene first: one blocker anywhere = nothing changes anywhere
            foreach (var alias in order)
            {
                string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
                if (refuse != null) return refuse;
                Plan308 plan;
                try { plan = MakePlan(cfg, alias, scene, SceneSteps); WritePlan(plan); }
                finally { GoBack(previous, scene, opened, true); }
                lines.Add(PlanText(plan).TrimEnd());
                if (plan.blocked) return "refused: apply-all stopped before any change — " + alias + " has blockers\n" + string.Join("\n", lines);
            }
            foreach (var alias in order)
            {
                string r = Apply(alias, SceneSteps, "apply-all");
                lines.Add(alias + ": " + r.Split('\n')[0]);
                if (r.StartsWith("refused", StringComparison.Ordinal) || r.StartsWith("FAILED", StringComparison.Ordinal)) { lines.Add("stopped at " + alias); break; }
            }
            return string.Join("\n", lines);
        }

        // ------------------------------------------------------------------ execute / revert one change

        static void Execute(BuildingAudit308.Config308 cfg, Scene scene, Change308 c, List<Object> created)
        {
            var t = BuildingAudit308.Resolve(scene, c.key) ?? throw new InvalidOperationException("missing " + c.key);
            switch (c.kind)
            {
                case "pose":
                    if (!ParsePose(c.after, out var p, out var q)) throw new FormatException(c.after);
                    t.localPosition = p; t.localRotation = q; EditorUtility.SetDirty(t);
                    break;
                case "active":
                    t.gameObject.SetActive(c.after == "1"); EditorUtility.SetDirty(t.gameObject);
                    break;
                case "collider":
                {
                    var f = t.GetComponent<MeshFilter>(); if (f == null || f.sharedMesh == null) throw new InvalidOperationException("no mesh on " + c.key);
                    var mc = t.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = f.sharedMesh; mc.convex = false; EditorUtility.SetDirty(t.gameObject);
                    break;
                }
                case "restore-asset":
                {
                    // a quarantined (#307) generator mesh goes back to its original path with its .meta (same GUID)
                    var parts = c.after.Split('|'); string src = parts[0], dst = parts[1];
                    if (File.Exists(BuildingAudit308.Abs(dst))) break;
                    File.Copy(src, BuildingAudit308.Abs(dst), false);
                    if (File.Exists(src + ".meta")) File.Copy(src + ".meta", BuildingAudit308.Abs(dst) + ".meta", false);
                    AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceSynchronousImport);
                    break;
                }
                case "create-mesh":
                {
                    if (AssetDatabase.LoadAssetAtPath<Mesh>(c.after) != null) break;
                    var mesh = BuildCapMesh(cfg, t, c.note);
                    mesh.name = Path.GetFileNameWithoutExtension(c.after);
                    EnsureFolder(Path.GetDirectoryName(c.after).Replace('\\', '/'));
                    AssetDatabase.CreateAsset(mesh, c.after); created.Add(mesh);
                    break;
                }
                case "mesh":
                {
                    var f = t.GetComponent<MeshFilter>(); if (f == null) throw new InvalidOperationException("no MeshFilter on " + c.key);
                    string path = c.after.Split('#')[0];
                    var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(m => c.after.EndsWith("#" + m.name, StringComparison.Ordinal)) ?? AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (mesh == null || mesh.vertexCount == 0) throw new InvalidOperationException("mesh asset missing or empty " + c.after);
                    f.sharedMesh = mesh; EditorUtility.SetDirty(f);
                    break;
                }
                default: throw new InvalidOperationException("unknown change kind " + c.kind);
            }
        }

        static void Undo308(Scene scene, Change308 c)
        {
            var t = BuildingAudit308.Resolve(scene, c.key);
            if (t == null && c.kind != "restore-asset" && c.kind != "create-mesh") throw new InvalidOperationException("missing " + c.key);
            switch (c.kind)
            {
                case "pose": ParsePose(c.before, out var p, out var q); t.localPosition = p; t.localRotation = q; EditorUtility.SetDirty(t); break;
                case "active": t.gameObject.SetActive(c.before == "1"); EditorUtility.SetDirty(t.gameObject); break;
                case "collider":
                {
                    var f = t.GetComponent<MeshFilter>();
                    var mc = t.GetComponents<MeshCollider>().LastOrDefault(x => f != null && x.sharedMesh == f.sharedMesh && !x.convex);
                    if (mc != null) Object.DestroyImmediate(mc);
                    EditorUtility.SetDirty(t.gameObject); break;
                }
                case "skirt":
                {
                    var holder = t.Find("StoneSkirt308");
                    if (holder != null) Object.DestroyImmediate(holder.gameObject);
                    EditorUtility.SetDirty(t.gameObject); break;
                }
                case "mesh":
                {
                    var f = t.GetComponent<MeshFilter>();
                    if (string.IsNullOrEmpty(c.before)) f.sharedMesh = null;
                    else { string path = c.before.Split('#')[0]; f.sharedMesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(m => c.before.EndsWith("#" + m.name, StringComparison.Ordinal)) ?? AssetDatabase.LoadAssetAtPath<Mesh>(path); }
                    EditorUtility.SetDirty(f); break;
                }
                // asset files (restored or created meshes) stay on disk: nothing references them after the scene revert
                case "restore-asset": case "create-mesh": break;
            }
        }

        static string Revert(string alias, string step)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err);
            if (cfg == null) return "refused: " + err;
            var ledger = LoadLedger(alias);
            // a FAILED apply counts only when it was saved (shaAfter recorded); an unsaved failure never reached the file
            var ops = ledger.ops.Where(o => !o.reverted && (o.status == "applied" || (o.status == "FAILED" && !string.IsNullOrEmpty(o.shaAfter))) && o.changes.Any(c => c.state == "apply" && (step == null || c.step == step))).ToList();
            if (ops.Count == 0) return "refused: nothing to revert in " + LedgerFile(alias) + (step != null ? " for " + step : "");
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            string sha = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
            var last = ops.Last();
            var rev = new Op308 { utc = BuildingAudit308.Utc(), command = "revert:" + alias + (step != null ? ":" + step : ""), scene = scene.path, alias = alias, shaBefore = sha, steps = step != null ? new[] { step } : SceneSteps };
            // exact restore: the newest apply holds only what is being reverted and nothing changed the scene since
            // (only one live apply touches the step(s): an older apply of the same step would survive a restore of the newest backup)
            bool exact = sha == last.shaAfter && File.Exists(last.backup) && ops.Count == 1 && (step == null || last.changes.Where(c => c.state == "apply").All(c => c.step == step));
            if (exact)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                File.Copy(last.backup, BuildingAudit308.Abs(scene.path), true);
                AssetDatabase.ImportAsset(last.scene, ImportAssetOptions.ForceUpdate);
                EditorSceneManager.OpenScene(last.scene, OpenSceneMode.Single);
                last.reverted = true; last.revertedUtc = rev.utc;
                if (step == null) rev.steps = last.steps;
                rev.status = "reverted"; rev.backup = last.backup; rev.shaAfter = BuildingAudit308.Sha(BuildingAudit308.Abs(last.scene));
                rev.detail = "scene file restored from " + last.backup + (rev.shaAfter == last.shaBefore ? " (SHA = before)" : " (SHA differs from the recorded before " + last.shaBefore + ")");
                ledger.ops.Add(rev); SaveLedger(ledger);
                if (!string.IsNullOrEmpty(previous) && previous != last.scene && File.Exists(BuildingAudit308.Abs(previous))) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                return rev.status + ": " + alias + " " + rev.detail;
            }
            string backupDir = Path.Combine(BuildingAudit308.Folder, "SceneBackup"); Directory.CreateDirectory(backupDir);
            rev.backup = Path.Combine(backupDir, alias + "-" + rev.utc + "-prerevert.unity"); File.Copy(BuildingAudit308.Abs(scene.path), rev.backup, false);
            int n = 0;
            try
            {
                foreach (var op in Enumerable.Reverse(ops))
                    foreach (var c in Enumerable.Reverse(op.changes).Where(c => c.state == "apply" && (step == null || c.step == step)).ToList()) { Undo308(scene, c); n++; }
            }
            catch (Exception e)
            {
                // the ledger file is untouched; drop the half-reverted in-memory scene so the editor stays clean
                string path = scene.path;
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                if (opened && !string.IsNullOrEmpty(previous) && previous != path && File.Exists(BuildingAudit308.Abs(previous))) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                return "FAILED: revert " + alias + " after " + n + " change(s) — " + e.Message + "; nothing saved (scene reloaded from disk, pre-revert copy " + rev.backup + ")";
            }
            foreach (var op in ops)
            {
                foreach (var c in op.changes.Where(c => c.state == "apply" && (step == null || c.step == step))) c.state = "reverted";
                if (op.changes.All(c => c.state != "apply")) { op.reverted = true; op.revertedUtc = rev.utc; }
            }
            ClearStaleSuffix(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            rev.shaAfter = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
            rev.status = saved ? "reverted" : "FAILED"; rev.detail = n + " change(s) set back to their ledger values" + (saved ? "" : "; SaveScene returned false");
            ledger.ops.Add(rev); SaveLedger(ledger);
            GoBack(previous, scene, opened);
            return rev.status + ": " + alias + " sha " + sha + " -> " + rev.shaAfter + " — " + rev.detail + " (pre-revert backup " + rev.backup + ")";
        }

        internal static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            string parent = Path.GetDirectoryName(assetFolder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
        }

        // ------------------------------------------------------------------ F6 around the single NavMesh bake

        [Serializable] sealed class NavPrep308 { public string utc = "", navAsset = "", navSha = "", backup = "", snapshot = "", quality = ""; public List<string> checks = new List<string>(); public List<string> dirtyAssets = new List<string>(); }

        static string NavPrep()
        {
            var cfg = BuildingAudit308.LoadConfig(out string err);
            if (cfg == null) return "refused: " + err;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only";
            var prep = new NavPrep308 { utc = BuildingAudit308.Utc(), navAsset = cfg.fix.f6.navAsset, quality = BuildingAudit308.QualityName() };
            var problems = new List<string>();
            var scans = new Dictionary<string, BuildingAudit308.Report308>();
            foreach (var s in cfg.scenes)
            {
                var l = LoadLedger(s.alias);
                var done = StepsDone(l);
                foreach (var st in SceneSteps)
                    if (!done.Contains(st)) problems.Add(s.alias + " " + st + " not applied (ledger)");
                string dir = BuildingAudit308.NewestScan(s.alias);
                string lastUtc = l.ops.Count > 0 ? l.ops.Last().utc : "";
                if (dir == null || string.CompareOrdinal(Path.GetFileName(dir), lastUtc) < 0) { problems.Add(s.alias + ": scan after the last apply missing (run BuildingAudit308 scan:scene=" + s.alias + ":checks=C0)"); continue; }
                scans[s.alias] = BuildingAudit308.LoadReport(dir);
                prep.checks.Add(s.alias + " scan " + Path.GetFileName(dir));
            }
            if (scans.Count == cfg.scenes.Length)
            {
                var first = scans.Values.First();
                foreach (var site in first.sites)
                    foreach (var kv in scans.Skip(1))
                    {
                        var o = kv.Value.sites.FirstOrDefault(x => x.name == site.name);
                        if (o == null) problems.Add("site " + site.name + " missing in " + kv.Key);
                        else if (o.hash != site.hash) problems.Add("site " + site.name + " static colliders differ: " + first.alias + " " + site.hash + " vs " + kv.Key + " " + o.hash);
                    }
                foreach (var root in first.roots.Where(r => r.present))
                    foreach (var kv in scans.Skip(1))
                    {
                        var o = kv.Value.roots.FirstOrDefault(x => x.name == root.name);
                        if (o != null && o.present && o.colliderHash != root.colliderHash) prep.checks.Add("recorded (existing state): root " + root.name + " colliders differ " + first.alias + " vs " + kv.Key);
                    }
            }
            prep.dirtyAssets = Resources.FindObjectsOfTypeAll<Object>().Where(o => o != null && EditorUtility.IsDirty(o) && AssetDatabase.Contains(o)).Select(o => AssetDatabase.GetAssetPath(o) + " (" + o.GetType().Name + ")").Distinct().Take(500).ToList();
            if (problems.Count > 0)
            {
                prep.checks.AddRange(problems.Select(p => "BLOCK " + p));
                Directory.CreateDirectory(FixDir); File.WriteAllText(Path.Combine(FixDir, "nav-prep.json"), JsonUtility.ToJson(prep, true), new UTF8Encoding(false));
                return "refused: nav-prep — " + string.Join(" | ", problems.Take(20));
            }
            string nav = BuildingAudit308.Abs(cfg.fix.f6.navAsset);
            if (!File.Exists(nav)) return "refused: no " + cfg.fix.f6.navAsset;
            string backupDir = Path.Combine(BuildingAudit308.Folder, "Backup"); Directory.CreateDirectory(backupDir);
            prep.backup = Path.Combine(backupDir, "Navigation-before308.asset");
            if (!File.Exists(prep.backup)) { File.Copy(nav, prep.backup, false); if (File.Exists(nav + ".meta")) File.Copy(nav + ".meta", prep.backup + ".meta.txt", false); }
            else prep.checks.Add("backup already present (kept the first one): " + prep.backup);
            prep.navSha = BuildingAudit308.Sha(nav);
            prep.snapshot = Path.Combine(FixDir, "nav-snapshot-" + prep.utc + ".txt");
            WriteSnapshot(Path.Combine(BuildingAudit308.RepoRoot, "Oheangbu", string.IsNullOrEmpty(cfg.fix.f6.snapshotRoot) ? "Assets" : cfg.fix.f6.snapshotRoot), prep.snapshot);
            Directory.CreateDirectory(FixDir); File.WriteAllText(Path.Combine(FixDir, "nav-prep.json"), JsonUtility.ToJson(prep, true), new UTF8Encoding(false));
            return "nav-prep ok: C0 repair sites equal in " + scans.Count + " scenes; " + cfg.fix.f6.navAsset + " sha " + prep.navSha + " backed up to " + prep.backup + "; " + prep.dirtyAssets.Count + " dirty asset(s) in memory (other sessions?) listed in Fix/nav-prep.json; snapshot " + prep.snapshot +
                   "\nnext: CompactArchitecture296 Run \"open\" -> CompactArchitecture296 Run \"nav\" -> BuildingFix308 Run \"nav-after\" -> reopen W_Demo_Main";
        }

        static void WriteSnapshot(string dir, string file)
        {
            using (var w = new StreamWriter(file, false, new UTF8Encoding(false)))
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var i = new FileInfo(f);
                    w.Write(f.Substring(dir.Length).Replace('\\', '/')); w.Write('\t'); w.Write(i.Length.ToString(Inv)); w.Write('\t'); w.Write(i.LastWriteTimeUtc.Ticks.ToString(Inv)); w.Write('\n');
                }
        }

        static string NavAfter()
        {
            var cfg = BuildingAudit308.LoadConfig(out string err);
            if (cfg == null) return "refused: " + err;
            string prepFile = Path.Combine(FixDir, "nav-prep.json");
            if (!File.Exists(prepFile)) return "refused: run nav-prep first";
            var prep = JsonUtility.FromJson<NavPrep308>(File.ReadAllText(prepFile));
            if (string.IsNullOrEmpty(prep.snapshot) || !File.Exists(prep.snapshot)) return "refused: nav-prep did not finish (no snapshot)";
            var before = new Dictionary<string, string>();
            foreach (var line in File.ReadLines(prep.snapshot)) { int k = line.IndexOf('\t'); if (k > 0) before[line.Substring(0, k)] = line.Substring(k + 1); }
            string root = Path.Combine(BuildingAudit308.RepoRoot, "Oheangbu", string.IsNullOrEmpty(cfg.fix.f6.snapshotRoot) ? "Assets" : cfg.fix.f6.snapshotRoot);
            string now = Path.Combine(FixDir, "nav-snapshot-after-" + BuildingAudit308.Utc() + ".txt");
            WriteSnapshot(root, now);
            var changed = new List<string>(); var seen = new HashSet<string>();
            foreach (var line in File.ReadLines(now))
            {
                int k = line.IndexOf('\t'); if (k <= 0) continue; string p = line.Substring(0, k); seen.Add(p);
                if (!before.TryGetValue(p, out var v)) changed.Add("added    " + p); else if (v != line.Substring(k + 1)) changed.Add("changed  " + p);
            }
            foreach (var p in before.Keys) if (!seen.Contains(p)) changed.Add("removed  " + p);
            string outFile = Path.Combine(FixDir, "nav-after-" + BuildingAudit308.Utc() + ".txt");
            string navSha = BuildingAudit308.Sha(BuildingAudit308.Abs(cfg.fix.f6.navAsset));
            File.WriteAllLines(outFile, new[] { "Navigation.asset sha before " + prep.navSha + " after " + navSha }.Concat(changed), new UTF8Encoding(false));
            var prot = changed.Where(l => cfg.ProtectedAsset("Assets" + l.Substring(9))).ToList();
            return "nav-after: " + changed.Count + " file(s) changed under " + root + " since nav-prep (" + outFile + "); Navigation.asset " + (navSha == prep.navSha ? "UNCHANGED (bake not run?)" : "rebaked") + (prot.Count > 0 ? "; PROTECTED PATHS CHANGED: " + string.Join(", ", prot.Take(10)) : "; protected paths unchanged");
        }

        // ------------------------------------------------------------------ verify (read only)

        [Serializable] sealed class Verify308 { public string alias = "", scene = "", utc = "", sceneSha = "", attraction = "", quality = ""; public List<string> rows = new List<string>(); public int fail; }

        static string Verify(string alias)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err);
            if (cfg == null) return "refused: " + err;
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            var v = new Verify308 { alias = alias, scene = scene.path, utc = BuildingAudit308.Utc(), sceneSha = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path)), attraction = AttractionHash(scene, cfg.attractionRoot), quality = BuildingAudit308.QualityName() };
            void Row(bool ok, string text) { v.rows.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) v.fail++; }
            void Info(string text) => v.rows.Add("INFO " + text);
            try
            {
                Physics.SyncTransforms();
                VerifySteps(cfg, scene, LoadLedger(alias), Row, Info);
                if (alias == "main")
                {
                    var ore = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "Ore_Seams297");
                    Row(ore != null && !ore.gameObject.activeSelf, "AC-B10 Ore_Seams297 present and inactive in W_Demo_Main");
                    var boss = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "MineTutorialBoss306");
                    Row(boss != null, "AC-B10 MineTutorialBoss306 present in W_Demo_Main");
                }
                Info("Finish297_Attraction block hash " + v.attraction);
            }
            finally { GoBack(previous, scene, opened, true); }
            Directory.CreateDirectory(FixDir);
            string file = Path.Combine(FixDir, "verify-" + alias + "-" + v.utc + ".json");
            File.WriteAllText(file, JsonUtility.ToJson(v, true), new UTF8Encoding(false));
            return "verify " + alias + ": " + (v.fail == 0 ? "PASS" : v.fail + " FAIL") + " -> " + file + "\n" + string.Join("\n", v.rows);
        }
    }
}
