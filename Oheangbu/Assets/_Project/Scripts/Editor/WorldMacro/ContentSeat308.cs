using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 seat fix after the content scene pass (Art/Playtest308/Pacing/SEATFIX308_SCOPE.md, D308-9e: reversible ledgers only) [TEST].
    // Frame = BuildingFix308: plan -> backup -> apply -> verify -> revert; the command opens its own scene and goes back to the scene
    // that was open. Queue entry Run(string); every refusal is a "refused: …" string, never a dialog:
    //   status                         data / scene-data state, ledgers, open scene (read only)
    //   plan:<alias>                   read only -> Art/Playtest308/Pacing/seat308_plan_<alias>.json, numbers as "num <name> <value>"
    //   apply:<alias>[:D1,D2,…]        D1 inn move + lower + skirt · D2 keeper point + body · D3 lantern caps · D5 firewood stack ·
    //                                  D6 old road ribbons. Backs the scene file (and a changed content asset) up to
    //                                  Pacing/Backups/seat308-<utc>/, saves only the target scene (EditorSceneManager.SaveScene) and
    //                                  the one content asset / new mesh (SaveAssetIfDirty); ledger Pacing/ledger_seat308_<scene>.json
    //   verify:<alias>                 post-conditions with numbers, compared with the offline dry run (seat308_dry_<alias>.json)
    //   revert:<alias>[:<D1|…|D6>][:force]   ledger "before" values back (objects created are destroyed; mesh assets stay on disk).
    //                                  A change whose object is gone is closed as "lost"; a change whose present value is neither the
    //                                  ledger's before nor its after is skipped ("drifted") unless :force is given
    //   grass-plan[:<alias>] | grass-apply[:<alias>] | grass-revert      D4, the shared grass seed field, once (ContentSeat308.Grass.cs).
    //                                  grass-apply is refused while grass.apply_enabled is false in the data (D4 is PROPOSED)
    //   diag:<alias>:<base|nograss|lod0|nograss+lod0>:<name>:<eye x,y,z>:<target x,y,z>[:fov=..][:w=..][:h=..]
    //                                  one Presentation297 still with the grass renderers off and / or the LODGroups near the target
    //                                  forced to LOD0, in memory only (restored, the scene is never saved): which of the two hides the altar
    //   skirt-reset                    renames the generated StoneSkirt308 mesh away (only while no scene ledger uses it)
    //   apply:<alias>:D7 | :D8         the two D308-16 relayout steps, run only when named: D7 door hinges (HingeWorld of the rest
    //                                  doors to the door leaf, ContentSeat308.Hinge.cs), D8 per-piece re-seat move of a prop set (rigid
    //                                  root move, every piece back onto the ground, ContentSeat308.Props.cs). plan / verify list them
    //                                  when the data carries "hinges" / "reseat"; revert:<alias> without a step leaves them alone
    //                                  (revert:<alias>:D7 / :D8). With data that has neither section nothing here differs from before.
    // <alias> = arch296 | folk298 | main (PostLedger308.Short). Every number is in
    // Art/World/Compact/Rebuild/CliffBoundary308/contentseat308.json; objects are addressed by BuildingAudit308.KeyOf keys only.
    // Each change is idempotent: current = before -> apply, current = after -> already, neither -> mismatch (the whole apply refuses).
    // Never written: Watershed295* / Reworld292* / MountainTrail285* trees and folders, W_Demo_Compact, 03_Content,
    // Finish297_Attraction (hashed before and after). AssetDatabase.SaveAssets is never called. Editor-only statics: none are kept
    // between calls (Enter Play Mode domain reload is off).
    public static partial class ContentSeat308
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly string[] SceneSteps = { "D1", "D2", "D3", "D5", "D6" };
        // the relayout steps: never part of a default apply / revert; plan / verify list them only when the data has their section
        static readonly string[] NamedSteps = { "D7", "D8" };
        static string[] AllSteps => SceneSteps.Concat(NamedSteps).ToArray();
        static string SectionOf(string step) => step == "D7" ? "hinges" : step == "D8" ? "reseat" : null;
        static string[] StepsInData(JObject data) => SceneSteps.Concat(NamedSteps.Where(s => data != null && data[SectionOf(s)] != null)).ToArray();
        const string Usage = "status | plan:<alias> | apply:<alias>[:D1,D2,D3,D5,D6] | verify:<alias> | revert:<alias>[:<step>][:force] | grass-plan[:<alias>] | grass-apply[:<alias>] | grass-revert[:force] | skirt-reset | diag:<alias>:<base|nograss|lod0|nograss+lod0>:<name>:<eye>:<target>[:fov=][:w=][:h=]   (alias = arch296 | folk298 | main)";

        static string DataFile => Path.Combine(Harness303.RepoRoot, "Art", "World", "Compact", "Rebuild", "CliffBoundary308", "contentseat308.json");
        static string OutDir => Path.Combine(Harness303.RepoRoot, "Art", "Playtest308", "Pacing");
        static string SceneName(string scenePath) => Path.GetFileNameWithoutExtension(scenePath);
        static string LedgerFile(string scenePath) => Path.Combine(OutDir, "ledger_seat308_" + SceneName(scenePath) + ".json");
        static string SharedFile => Path.Combine(OutDir, "ledger_seat308_shared.json");

        sealed class Refuse : Exception { public Refuse(string m) : base(m) { } }

        // scene -> its session Content asset (the same table Content308 checks against the opened scene's session)
        static readonly Dictionary<string, string> ContentOf = new Dictionary<string, string>
        {
            { PostLedger308.Arch296, "Assets/_Project/Art/World/Architecture296/Data/a3cb428dc79f23e45a4caf7729b52193_7a0b9698e5728bd4ea84bd5116570b9f_2b30a7723e5289c47a4fd3cde0d788cf_Content.asset" },
            { PostLedger308.Folk298, "Assets/_Project/Art/Characters/Folklore298/Data/WorldContent298.asset" },
            { PostLedger308.Main, "Assets/_Project/Scenes/World/Main/WorldContent_Main.asset" },
        };

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            try
            {
                var a = command.Split(':');
                switch (a[0])
                {
                    case "status": return Status();
                    case "plan": return a.Length > 1 ? PlanCommand(a[1]) : "refused: plan:<alias>";
                    case "apply": return a.Length > 1 ? Apply(a[1], a.Length > 2 ? a[2].Split(',') : SceneSteps, command) : "refused: apply:<alias>[:D1,D2,D3,D5,D6]";
                    case "verify": return a.Length > 1 ? Verify(a[1]) : "refused: verify:<alias>";
                    case "revert": return a.Length > 1 ? Revert(a[1], a.Skip(2).ToArray()) : "refused: revert:<alias>[:<step>][:force]";
                    case "diag": return Diag(a);
                    case "grass-plan": return GrassRun(a.Length > 1 ? a[1] : null, false);
                    case "grass-apply": return GrassRun(a.Length > 1 ? a[1] : null, true);
                    case "grass-revert": return GrassRevert(a.Length > 1 && string.Equals(a[1].Trim(), "force", StringComparison.OrdinalIgnoreCase));
                    case "skirt-reset": return SkirtReset();
                    default: return "refused: ContentSeat308 " + Usage;
                }
            }
            catch (Refuse r) { return "refused: " + r.Message; }
            catch (Exception e) { return "FAILED: " + e; }
        }

        // ------------------------------------------------------------------ data (Newtonsoft: nested arrays)

        static JObject LoadJson(string file, string what)
        {
            if (!File.Exists(file)) throw new Refuse(what + " missing: " + file);
            try { return JObject.Parse(File.ReadAllText(file).TrimStart('﻿')); }
            catch (Exception e) { throw new Refuse(what + " does not parse: " + file + " (" + e.Message + ")"); }
        }
        static JToken Req(JToken o, string key)
        {
            var t = o?[key];
            if (t == null || t.Type == JTokenType.Null) throw new Refuse("data: missing '" + key + "' in " + (o == null ? "(null)" : string.IsNullOrEmpty(o.Path) ? "(root)" : o.Path));
            return t;
        }
        static float Num(JToken o, string key) => Req(o, key).Value<float>();
        static float OptNum(JToken o, string key, float fallback = 0f) { var t = o?[key]; return t == null || t.Type == JTokenType.Null ? fallback : t.Value<float>(); }
        static Vector3 Vec3(JToken o, string key) { var a = Req(o, key); return new Vector3(At(a, 0), At(a, 1), At(a, 2)); }
        static string Str(JToken o, string key) => Req(o, key).Value<string>();
        static string Opt(JToken o, string key) { var t = o?[key]; return t == null || t.Type == JTokenType.Null ? null : t.Value<string>(); }
        static bool Flag(JToken o, string key) { var t = o?[key]; return t != null && t.Type == JTokenType.Boolean && t.Value<bool>(); }
        static IEnumerable<JToken> Arr(JToken o, string key) { var t = o?[key] as JArray; return t ?? (IEnumerable<JToken>)Array.Empty<JToken>(); }
        static float At(JToken array, int i)
        {
            var a = array as JArray;
            if (a == null || a.Count <= i) throw new Refuse("data: " + (array == null ? "(null)" : array.Path) + " needs " + (i + 1) + " numbers");
            return a[i].Value<float>();
        }
        static string F(float v, string f = "F2") => float.IsNaN(v) ? "NaN" : v.ToString(f, Inv);
        static string V(Vector3 v) => string.Format(Inv, "({0:F2}, {1:F2}, {2:F2})", v.x, v.y, v.z);
        static string Short(string sha) => string.IsNullOrEmpty(sha) ? "-" : sha.Substring(0, Math.Min(12, sha.Length));
        static string ShaOf(string file) => File.Exists(file) ? BuildingAudit308.Sha(file) : "";

        static string SceneOf(string alias)
        {
            alias = (alias ?? "").Trim();
            foreach (var s in PostLedger308.Scenes) if (string.Equals(PostLedger308.Short(s), alias, StringComparison.OrdinalIgnoreCase) || s == alias) return s;
            if (alias == "architecture296") return PostLedger308.Arch296;
            if (alias == "folklore298") return PostLedger308.Folk298;
            throw new Refuse("unknown scene '" + alias + "' (" + string.Join(", ", PostLedger308.Scenes.Select(PostLedger308.Short)) + ")");
        }

        // ------------------------------------------------------------------ ledger / plan shapes (JsonUtility)

        [Serializable] sealed class Change
        {
            // kind: pose | scale | enable | skirt | mesh | create-mesh | spawn | point | hash
            // state: apply | already | mismatch | blocked | info | reverted | lost (its object is gone: nothing left to undo)
            public string step = "", key = "", kind = "", before = "", after = "", note = "", state = "";
            public Vector3 at;
        }
        [Serializable] sealed class StepPlan
        {
            public string step = "", status = "", detail = "";   // ready | already | clean | off | blocked | mismatch
            public List<Change> changes = new List<Change>(); public List<string> notes = new List<string>(); public List<string> numbers = new List<string>();
        }
        [Serializable] sealed class Plan
        {
            public string alias = "", scene = "", utc = "", sceneSha = "", dataSha = "", sceneDataSha = ""; public bool blocked;
            public List<StepPlan> steps = new List<StepPlan>(); public List<string> blockers = new List<string>(), dry = new List<string>();
        }
        [Serializable] sealed class Op
        {
            public string utc = "", command = "", scene = "", alias = "", status = "", detail = "", backup = "", shaBefore = "", shaAfter = "", attractionBefore = "", attractionAfter = "", dataSha = "", sceneDataSha = "", revertedUtc = "";
            public string[] steps = Array.Empty<string>(); public bool reverted;
            public List<string> stepResults = new List<string>(), assetBackups = new List<string>(); public List<Change> changes = new List<Change>();
        }
        [Serializable] sealed class Ledger { public string alias = "", scene = ""; public List<Op> ops = new List<Op>(); }
        [Serializable] sealed class Shared { public string skirtMesh = "", skirtPose = "", utc = "", scene = ""; }

        static Ledger LoadLedger(string scenePath)
        {
            string f = LedgerFile(scenePath);
            var l = File.Exists(f) ? JsonUtility.FromJson<Ledger>(File.ReadAllText(f)) : null;
            return l ?? new Ledger { alias = PostLedger308.Short(scenePath), scene = scenePath };
        }
        static void SaveLedger(Ledger l) { Directory.CreateDirectory(OutDir); File.WriteAllText(LedgerFile(l.scene), JsonUtility.ToJson(l, true), new UTF8Encoding(false)); }
        static Shared LoadShared() => File.Exists(SharedFile) ? JsonUtility.FromJson<Shared>(File.ReadAllText(SharedFile)) ?? new Shared() : new Shared();

        // the newest live (applied, not reverted) change of a step / key / kind
        static Change Prior(Ledger l, string step, string key, string kind)
        {
            for (int i = l.ops.Count - 1; i >= 0; i--)
            {
                var op = l.ops[i]; if (op.reverted || op.status != "applied") continue;
                var c = op.changes.LastOrDefault(x => x.step == step && x.key == key && x.kind == kind && x.state == "apply");
                if (c != null) return c;
            }
            return null;
        }
        static bool StepLive(Ledger l, string step) => l.ops.Any(o => !o.reverted && o.status == "applied" && o.changes.Any(c => c.step == step && c.state == "apply"));

        // ------------------------------------------------------------------ context

        sealed class Ctx
        {
            public string Alias, ScenePath; public Scene Scene; public JObject Data, SceneData; public string DataSha, SceneDataSha;
            public BuildingAudit308.Config308 Cfg; public WorldMacroPlaytestSession Session; public WorldMacroPlaytestSO Content; public Ledger Ledger;
            public float PoseTol, YawTol;                       // the Content308 scene pass tolerances (content308_scene.json ground)
            public readonly HashSet<Collider> Skip = new HashSet<Collider>(); public readonly List<Transform> Ignore = new List<Transform>();
            public readonly Dictionary<string, float> Numbers = new Dictionary<string, float>();
            public Transform OwnRoot;
        }

        static void RequireEditable()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Refuse("Edit mode only (Play is running)");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorUtility.scriptCompilationFailed) throw new Refuse("scripts are compiling / importing or failed to compile");
            string dirty = BuildingAudit308.DirtyOpenScene();
            if (dirty != null) throw new Refuse("scene " + (string.IsNullOrEmpty(dirty) ? "(untitled)" : dirty) + " has unsaved changes - save or discard them first");
            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.isDirty) throw new Refuse("an open scene has unsaved changes"); }
        }

        // names of the in-memory look previews that are up (content308_scene.json preview_roots): while one is up real renderers are
        // switched off, so a scene save would write them and a scene switch would orphan the preview's restore list
        static List<string> PreviewsUp(JObject sceneData)
        {
            var names = new HashSet<string>(Arr(sceneData, "preview_roots").Select(t => t.Value<string>()));
            if (names.Count == 0) throw new Refuse("scene data: 'preview_roots' must list the look preview root names");
            return Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && !EditorUtility.IsPersistent(g) && g.transform.parent == null && names.Contains(g.name)).Select(g => g.name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        // opens the target Single when it is not the only open scene; previous = the scene to go back to
        static Ctx Begin(string alias, bool write, out string previous, out bool opened)
        {
            previous = null; opened = false;
            RequireEditable();
            var k = new Ctx { ScenePath = SceneOf(alias) };
            k.Alias = PostLedger308.Short(k.ScenePath);
            if (Harness303.IsProtected(k.ScenePath) || PostLedger308.IsProtectedPath(k.ScenePath)) throw new Refuse("protected scene " + k.ScenePath);
            k.Data = LoadJson(DataFile, "seat data (copy Tools/Unity/Stage308_seatfix/Data/contentseat308.json there)"); k.DataSha = ShaOf(DataFile);
            string sceneDataFile = Path.Combine(Harness303.RepoRoot, Str(k.Data, "scene_data"));
            k.SceneData = LoadJson(sceneDataFile, "scene data"); k.SceneDataSha = ShaOf(sceneDataFile);
            k.Cfg = BuildingAudit308.LoadConfig(out string err); if (k.Cfg == null) throw new Refuse(err);
            var g = Req(k.SceneData, "ground"); k.PoseTol = Num(g, "pose_tol_m"); k.YawTol = Num(g, "yaw_tol_deg");
            var active = SceneManager.GetActiveScene();
            bool here = active.path == k.ScenePath && SceneManager.sceneCount == 1;
            var up = PreviewsUp(k.SceneData);
            // refused for every command: a write would save the renderers the preview switched off, and even a read-only pass may have
            // to reload the scene (ExecuteAlways dirt), which would orphan the preview's restore list
            if (up.Count > 0) throw new Refuse("a look preview is up (" + string.Join(", ", up) + ") - run that tool's preview:off first" + (write ? " (a scene save would write the renderers it switched off)" : ""));
            if (!here && SceneManager.sceneCount > 1) throw new Refuse(SceneManager.sceneCount + " scenes are open: opening " + k.ScenePath + " would close them - leave one scene open first");
            if (here) k.Scene = active;
            else { previous = active.path; k.Scene = EditorSceneManager.OpenScene(k.ScenePath, OpenSceneMode.Single); opened = true; }
            try
            {
                var sessions = k.Scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).ToArray();
                if (sessions.Length != 1) throw new Refuse(k.ScenePath + " has " + sessions.Length + " WorldMacroPlaytestSession components (expected 1)");
                k.Session = sessions[0];
                string contentPath = ContentOf[k.ScenePath];
                if (Harness303.IsProtected(contentPath)) throw new Refuse("protected content " + contentPath);
                k.Content = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(contentPath);
                if (k.Content == null) throw new Refuse("content asset missing " + contentPath);
                if (k.Session.Content != k.Content) throw new Refuse(k.ScenePath + " session Content is " + AssetDatabase.GetAssetPath(k.Session.Content) + ", expected " + contentPath);
                k.Ledger = LoadLedger(k.ScenePath);
                PrepareGround(k);
            }
            catch { GoBack(previous, k.Scene, opened); throw; }
            return k;
        }

        // back to the scene that was open. The target is never left dirty: unsaved edits are dropped by reloading it from disk.
        static void GoBack(string previous, Scene scene, bool opened)
        {
            if (scene.IsValid() && scene.isDirty && !string.IsNullOrEmpty(scene.path)) scene = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
            if (!opened || string.IsNullOrEmpty(previous) || previous == scene.path || !File.Exists(Harness303.Abs(previous))) return;
            EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
        }

        // ------------------------------------------------------------------ ground (the Content308 rule, re-implemented: its Ground308 is private)

        static void PrepareGround(Ctx k)
        {
            k.Skip.Clear(); k.Ignore.Clear();
            foreach (var a in k.Session.Actors ?? Array.Empty<Oheangbu.App.Prologue.PrologueEncounter>()) if (a != null) foreach (var c in a.GetComponentsInChildren<Collider>(true)) k.Skip.Add(c);
            if (k.Session.Walker != null && k.Session.Walker.Body != null) foreach (var c in k.Session.Walker.Body.GetComponentsInChildren<Collider>(true)) k.Skip.Add(c);
            var names = new HashSet<string>(Arr(k.Data, "ground_ignore_roots").Select(t => t.Value<string>()));
            string own = Str(k.Data, "root");
            foreach (var g in k.Scene.GetRootGameObjects()) { if (names.Contains(g.name)) k.Ignore.Add(g.transform); if (g.name == own) k.OwnRoot = g.transform; }
            Physics.SyncTransforms();
        }
        static bool Usable(Ctx k, RaycastHit h)
        {
            var c = h.collider; if (c == null || h.normal.y <= .5f || c is CharacterController || k.Skip.Contains(c)) return false;
            for (var t = c.transform; t != null; t = t.parent) if ((t.gameObject.hideFlags & HideFlags.DontSave) != 0) return false;
            foreach (var r in k.Ignore) if (r != null && c.transform.IsChildOf(r)) return false;
            return true;
        }
        static bool TerrainLike(Collider c)
        {
            if (c is TerrainCollider) return true;
            for (var t = c.transform; t != null; t = t.parent) { var n = t.name; if (n.Contains("Terrain") || n.Contains("Surface")) return true; }
            return false;
        }
        // the highest walkable support within 1.5 m over the terrain (decks, flat stones), else the highest walkable hit
        static bool Ground(Ctx k, float x, float z, out Vector3 p, out float slope)
        {
            p = default; slope = 90f; float baseY = float.NegativeInfinity;
            var hits = Physics.RaycastAll(new Vector3(x, 2000f, z), Vector3.down, 4000f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits) if (Usable(k, h) && TerrainLike(h.collider) && h.point.y > baseY) baseY = h.point.y;
            if (float.IsNegativeInfinity(baseY)) foreach (var h in hits) if (Usable(k, h) && h.point.y > baseY) baseY = h.point.y;
            if (float.IsNegativeInfinity(baseY)) return false;
            bool any = false; RaycastHit best = default;
            foreach (var h in hits) { if (!Usable(k, h) || h.point.y < baseY - .05f || h.point.y > baseY + 1.5f) continue; if (!any || h.point.y > best.point.y) { best = h; any = true; } }
            p = best.point; slope = Vector3.Angle(best.normal, Vector3.up); return true;
        }
        static float GroundY(Ctx k, float x, float z) => Ground(k, x, z, out var p, out _) ? p.y : float.NaN;

        // ------------------------------------------------------------------ keys, protection, small geometry

        // a key must resolve to exactly itself: a bare name on a segment with same-named siblings would silently pick the first one
        static Transform Strict(Ctx k, string key, bool required = true)
        {
            var t = BuildingAudit308.Resolve(k.Scene, key);
            if (t == null) { if (required) throw new Refuse("object not found: " + key); return null; }
            string real = BuildingAudit308.KeyOf(t);
            if (real != key) throw new Refuse("ambiguous key " + key + " (the object's own key is " + real + "): same-named siblings exist - use the '#k' form in contentseat308.json");
            return t;
        }
        static bool ProtectedObject(Ctx k, Transform t)
        {
            if (t == null) return false;
            if (PostLedger308.UnderProtectedTree(t)) return true;
            string root = t.root.name;
            return k.Cfg.ProtectedRoot(root) || (!string.IsNullOrEmpty(k.Cfg.attractionRoot) && root == k.Cfg.attractionRoot);
        }
        static bool ProtectedAsset(Ctx k, string path) => Harness303.IsProtected(path) || PostLedger308.IsProtectedPath(path) || k.Cfg.ProtectedAsset(path);
        static bool UnderOwnRoot(Ctx k, Transform t) => k.OwnRoot != null && t != null && t.IsChildOf(k.OwnRoot);

        static Vector3 Local(float yaw, float lx, float lz) => Quaternion.Euler(0, yaw, 0) * new Vector3(lx, 0, lz);
        static Vector2 ToLocal(float yaw, Vector3 origin, Vector3 world) { var l = Quaternion.Euler(0, -yaw, 0) * new Vector3(world.x - origin.x, 0, world.z - origin.z); return new Vector2(l.x, l.z); }
        // signed distance of a frame point to a rect (x0, x1, z0, z1): > 0 outside, < 0 inside
        static float RectOut(float[] r, Vector2 l)
        {
            float ex = Mathf.Max(r[0] - l.x, 0, l.x - r[1]), ez = Mathf.Max(r[2] - l.y, 0, l.y - r[3]);
            return ex > 0 || ez > 0 ? Mathf.Sqrt(ex * ex + ez * ez) : -Mathf.Min(Mathf.Min(l.x - r[0], r[1] - l.x), Mathf.Min(l.y - r[2], r[3] - l.y));
        }
        static float[] Rect(JToken o, string key) { var a = Req(o, key); return new[] { At(a, 0), At(a, 1), At(a, 2), At(a, 3) }; }

        // distance from an XZ point to the nearest route centre line (routes.json + content paths); a segment longer than runGap is a jump
        static float CentreLine(Vector2 p, List<BuildingAudit308.Corridor308> corridors, float runGap, out string id)
        {
            float best = float.PositiveInfinity; id = "";
            foreach (var c in corridors)
                for (int i = 1; i < c.pts.Length; i++)
                {
                    var a = new Vector2(c.pts[i - 1].x, c.pts[i - 1].z); var b = new Vector2(c.pts[i].x, c.pts[i].z);
                    if (Vector2.Distance(a, b) > runGap) continue;
                    float d = BuildingAudit308.SegDist2(p, a, b);
                    if (d < best) { best = d; id = c.id; }
                }
            return best;
        }

        // Tree / Shrub rows of the scene's placement sheets near a point
        static List<(Vector3 p, string id)> Trunks(Ctx k, Vector3 near, float radius)
        {
            var list = new List<(Vector3, string)>();
            foreach (var sheet in BuildingAudit308.SceneSheets(k.Scene, k.Cfg))
            {
                var tall = new HashSet<string>(sheet.Prototypes.Where(x => x != null && (x.Category == WorldMacroDressingSheetSO.Kind.Tree || x.Category == WorldMacroDressingSheetSO.Kind.Shrub)).Select(x => x.Id));
                foreach (var f in sheet.FixedPlacements)
                    if (tall.Contains(f.PrototypeId) && Harness303.Flat(f.Position, near) <= radius) list.Add((f.Position, f.Id));
            }
            return list;
        }

        static string PoseText(Vector3 p, Quaternion q) => BuildingFix308.Pose(p, q);
        static bool SamePose(Ctx k, Transform t, string pose)
        {
            float tolP = Num(Req(k.Data, "tolerances"), "pose_m"), tolA = Num(Req(k.Data, "tolerances"), "angle_deg");
            return BuildingFix308.ParsePose(pose, out var p, out var q) && Vector3.Distance(t.localPosition, p) <= tolP && Quaternion.Angle(t.localRotation, q) <= tolA;
        }
        // local pose that puts t at a world pose under its present parent
        static string LocalPoseFor(Transform t, Vector3 worldPos, Quaternion worldRot)
        {
            var parent = t.parent;
            return parent == null ? PoseText(worldPos, worldRot) : PoseText(parent.InverseTransformPoint(worldPos), Quaternion.Inverse(parent.rotation) * worldRot);
        }
        // the scene pass tolerances (0.02 m / 0.2 deg): "the object still stands where the ledger says"
        static bool NearPose(Ctx k, Transform t, string pose)
            => BuildingFix308.ParsePose(pose, out var p, out var q) && Vector3.Distance(t.localPosition, p) <= k.PoseTol && Quaternion.Angle(t.localRotation, q) <= k.YawTol;
        static string LocalPoseUnder(Transform parent, Vector3 worldPos, Quaternion worldRot)
            => parent == null ? PoseText(worldPos, worldRot) : PoseText(parent.InverseTransformPoint(worldPos), Quaternion.Inverse(parent.rotation) * worldRot);
        static Vector3 LocalScaleUnder(Transform parent, Vector3 lossy)
        {
            var pl = parent != null ? parent.lossyScale : Vector3.one;
            return new Vector3(lossy.x / pl.x, lossy.y / pl.y, lossy.z / pl.z);
        }
        // the newest skirt record of a plate in a live op, whether a skirt was made ("apply") or was not needed ("info")
        static Change SkirtRecord(Ledger l, string plateKey)
        {
            for (int i = l.ops.Count - 1; i >= 0; i--)
            {
                var op = l.ops[i]; if (op.reverted || op.status != "applied") continue;
                var c = op.changes.LastOrDefault(x => x.step == "D1" && x.key == plateKey && x.kind == "skirt" && (x.state == "apply" || x.state == "info"));
                if (c != null) return c;
            }
            return null;
        }
        static string MeshState(MeshFilter f) => f == null ? "nofilter" : f.sharedMesh == null ? "" : AssetDatabase.GetAssetPath(f.sharedMesh) + "#" + f.sharedMesh.name;
        static string Vec(Vector3 v) => string.Format(Inv, "{0:R},{1:R},{2:R}", v.x, v.y, v.z);
        static bool ParseVec(string s, out Vector3 v)
        {
            v = default; var a = (s ?? "").Split(','); if (a.Length != 3) return false;
            v = new Vector3(float.Parse(a[0], Inv), float.Parse(a[1], Inv), float.Parse(a[2], Inv)); return true;
        }

        // every component of a subtree except the listed children, as one hash (D6: the root's other children must not change)
        static string SubtreeHash(Transform root, HashSet<Transform> except)
        {
            var sb = new StringBuilder();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (except.Any(e => t == e || t.IsChildOf(e))) continue;
                sb.Append(BuildingAudit308.KeyOf(t)).Append('|').Append(t.gameObject.activeSelf).Append('\n');
                foreach (var c in t.GetComponents<Component>()) if (c != null) sb.Append(c.GetType().Name).Append(':').Append(EditorJsonUtility.ToJson(c)).Append('\n');
            }
            return BuildingAudit308.ShaText(sb.ToString());
        }

        // ------------------------------------------------------------------ status

        static string Status()
        {
            var sb = new StringBuilder("ContentSeat308 status " + DateTime.UtcNow.ToString("O", Inv) + " (play=" + EditorApplication.isPlaying + " compiling=" + EditorApplication.isCompiling + ")\n");
            sb.AppendLine("  data " + DataFile + (File.Exists(DataFile) ? " sha " + Short(ShaOf(DataFile)) : " MISSING (copy Tools/Unity/Stage308_seatfix/Data/contentseat308.json there)"));
            if (File.Exists(DataFile))
            {
                var data = LoadJson(DataFile, "seat data");
                sb.AppendLine("  version " + Opt(data, "version") + ", inn mode " + Opt(data["inn"], "mode") + ", firewood visual " + Opt(data["firewood"], "visual") + ", D4 grass-apply " + (Flag(data["grass"], "apply_enabled") ? "enabled" : "HELD (grass.apply_enabled false: PROPOSED)"));
                string sceneDataFile = Path.Combine(Harness303.RepoRoot, Str(data, "scene_data"));
                if (File.Exists(sceneDataFile))
                {
                    var sd = LoadJson(sceneDataFile, "scene data");
                    sb.AppendLine("  scene data " + sceneDataFile + " sha " + Short(ShaOf(sceneDataFile)) + ": " + SceneDataState(data, sd, out _, out _));
                }
                else sb.AppendLine("  scene data MISSING " + sceneDataFile);
            }
            foreach (var s in PostLedger308.Scenes)
            {
                var l = LoadLedger(s);
                sb.AppendLine("  " + PostLedger308.Short(s) + ": scene sha " + Short(ShaOf(Harness303.Abs(s))) + ", ledger " + (File.Exists(LedgerFile(s)) ? l.ops.Count + " op(s), live steps [" + string.Join(",", AllSteps.Where(st => StepLive(l, st))) + "]" : "none"));
            }
            var sh = LoadShared();
            sb.AppendLine("  shared skirt mesh: " + (string.IsNullOrEmpty(sh.skirtMesh) ? "none recorded" : sh.skirtMesh + " pose " + sh.skirtPose));
            sb.AppendLine("  grass ledger: " + (File.Exists(GrassLedgerFile) ? "present" : "none"));
            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); sb.AppendLine("  open scene " + (string.IsNullOrEmpty(s.path) ? "(untitled)" : s.path) + (s.isDirty ? " (DIRTY)" : "")); }
            return sb.ToString();
        }

        // which inn candidate content308_scene.json names, and whether the firewood rows are off (the two rows Content308 scene-apply
        // would otherwise undo). candidate = -1 when none matches.
        static string SceneDataState(JObject data, JObject sceneData, out int candidate, out bool firewoodOff)
        {
            candidate = -1; firewoodOff = true;
            var inn = Req(data, "inn"); string restId = Str(inn, "rest_id");
            var rest = Arr(sceneData, "rests").FirstOrDefault(r => Opt(r, "id") == restId);
            var part = rest != null ? Arr(rest, "parts").FirstOrDefault() : null;
            if (part == null) return "rest " + restId + " has no part in the scene data";
            var off = Req(part, "offset"); float yaw = Num(part, "yaw");
            var cands = Candidates(inn);
            for (int i = 0; i < cands.Count; i++)
                if (Mathf.Abs(At(off, 0) - cands[i].x) < 1e-3f && Mathf.Abs(At(off, 2) - cands[i].z) < 1e-3f && Mathf.Abs(yaw - cands[i].yaw) < 1e-3f && Mathf.Abs(At(off, 1) + cands[i].embed) < 1e-3f) { candidate = i; break; }
            var bad = new List<string>();
            foreach (var pc in Arr(Req(data, "firewood"), "pieces"))
            {
                string id = Str(pc, "id"); var row = Arr(sceneData, "props").FirstOrDefault(p => Opt(p, "id") == id);
                if (row != null && Flag(row, "enabled")) { firewoodOff = false; bad.Add(id); }
            }
            return "inn part offset [" + F(At(off, 0)) + ", " + F(At(off, 1)) + ", " + F(At(off, 2)) + "] yaw " + F(yaw, "F1") + " = " + (candidate >= 0 ? "candidate " + candidate : "NO candidate (data mismatch)") +
                   "; firewood rows " + (firewoodOff ? "off" : "still enabled: " + string.Join(", ", bad) + " (data mismatch)");
        }
        // a candidate may carry its own embed_m (how far the pivot sits under the ground); else inn.embed_m
        static List<(float x, float z, float yaw, float embed)> Candidates(JToken inn)
        {
            var list = new List<(float, float, float, float)>(); float embed = Num(inn, "embed_m");
            if (Str(inn, "mode") == "lower_in_place") { var p = Req(inn, "in_place"); list.Add((At(Req(p, "offset"), 0), At(Req(p, "offset"), 1), Num(p, "yaw"), OptNum(p, "embed_m", embed))); }
            else foreach (var c in Arr(inn, "candidates")) list.Add((At(Req(c, "offset"), 0), At(Req(c, "offset"), 1), Num(c, "yaw"), OptNum(c, "embed_m", embed)));
            if (list.Count == 0) throw new Refuse("data: inn has no candidate for mode " + Str(inn, "mode"));
            return list;
        }

        // ------------------------------------------------------------------ plan

        static string PlanCommand(string alias)
        {
            var k = Begin(alias, false, out string previous, out bool opened);
            try
            {
                var plan = MakePlan(k, StepsInData(k.Data));
                WritePlan(plan);
                return PlanText(k, plan);
            }
            finally { GoBack(previous, k.Scene, opened); }
        }

        static Plan MakePlan(Ctx k, string[] steps)
        {
            Physics.SyncTransforms();
            var plan = new Plan { alias = k.Alias, scene = k.ScenePath, utc = BuildingAudit308.Utc(), sceneSha = ShaOf(Harness303.Abs(k.ScenePath)), dataSha = k.DataSha, sceneDataSha = k.SceneDataSha };
            string state = SceneDataState(k.Data, k.SceneData, out int candidate, out bool firewoodOff);
            foreach (var s in steps)
            {
                StepPlan sp;
                try
                {
                    switch (s)
                    {
                        case "D1": sp = PlanD1(k, candidate, state); break;
                        case "D2": sp = PlanD2(k, candidate, steps.Contains("D1")); break;
                        case "D3": sp = PlanD3(k); break;
                        case "D5": sp = PlanD5(k, firewoodOff, state); break;
                        case "D6": sp = PlanD6(k); break;
                        case "D7": sp = PlanD7(k); break;
                        case "D8": sp = PlanD8(k); break;
                        default: sp = new StepPlan { step = s, status = "blocked", detail = s == "D4" ? "D4 is the shared grass field: grass-plan / grass-apply" : "unknown step" }; break;
                    }
                }
                catch (Refuse r) { sp = new StepPlan { step = s, status = "blocked", detail = r.Message }; }
                foreach (var c in sp.changes.Where(c => c.state == "apply"))
                {
                    // a spawn's own object does not exist yet: its parent is the object that must be there and unprotected
                    string objectKey = c.kind == "point" || c.kind == "hinge" ? null : c.kind == "spawn" ? c.key.Substring(0, c.key.LastIndexOf('/')) : c.key;
                    var t = objectKey != null ? BuildingAudit308.Resolve(k.Scene, objectKey) : null;
                    if (c.kind == "spawn" && ProtectedAsset(k, c.after.Split(';')[0])) { c.state = "blocked"; sp.status = "blocked"; sp.detail += "; protected asset " + c.after.Split(';')[0]; }
                    if (objectKey != null && (t == null || ProtectedObject(k, t))) { c.state = "blocked"; sp.status = "blocked"; sp.detail += "; " + (t == null ? "missing " : "protected object ") + c.key; }
                    if (c.kind == "point" && ProtectedAsset(k, c.key.Split('|')[0])) { c.state = "blocked"; sp.status = "blocked"; sp.detail += "; protected asset " + c.key; }
                    if ((c.kind == "mesh" || c.kind == "create-mesh" || c.kind == "skirt") && !string.IsNullOrEmpty(c.after) && ProtectedAsset(k, c.after.Split('#')[0])) { c.state = "blocked"; sp.status = "blocked"; sp.detail += "; protected asset " + c.after; }
                }
                plan.steps.Add(sp);
                if (sp.status == "blocked" || sp.status == "mismatch") { plan.blocked = true; plan.blockers.Add(s + " " + sp.status + ": " + sp.detail); }
            }
            plan.dry = CompareDry(k);
            return plan;
        }

        // ready / already / clean from the change states; a mismatch or a blocked change wins
        static void Settle(StepPlan sp)
        {
            if (sp.status == "blocked" || sp.status == "mismatch" || sp.status == "off") return;
            if (sp.changes.Any(c => c.state == "mismatch")) { sp.status = "mismatch"; sp.detail += "; " + string.Join("; ", sp.changes.Where(c => c.state == "mismatch").Select(c => c.key + " (" + c.kind + ") is neither the before nor the after value: " + c.note)); return; }
            if (sp.changes.Any(c => c.state == "blocked")) { sp.status = "blocked"; return; }
            sp.status = sp.changes.Any(c => c.state == "apply") ? "ready" : sp.changes.Any(c => c.state == "already") ? "already" : "clean";
        }
        static void N(Ctx k, StepPlan sp, string name, float value) { k.Numbers[name] = value; sp.numbers.Add("num " + name + " " + F(value, "F3")); }

        static string PlanFile(string alias) => Path.Combine(OutDir, "seat308_plan_" + alias + ".json");
        static void WritePlan(Plan p) { Directory.CreateDirectory(OutDir); File.WriteAllText(PlanFile(p.alias), JsonUtility.ToJson(p, true), new UTF8Encoding(false)); }
        static string PlanText(Ctx k, Plan p)
        {
            var sb = new StringBuilder("ContentSeat308 plan " + p.alias + " (" + p.scene + ") scene sha " + Short(p.sceneSha) + ", data sha " + Short(p.dataSha) + ", scene data sha " + Short(p.sceneDataSha) + (p.blocked ? " - BLOCKED" : "") + "\n");
            foreach (var s in p.steps)
            {
                sb.AppendLine("  " + s.step + " " + s.status + ": " + s.detail + " (" + s.changes.Count(c => c.state == "apply") + " to apply, " + s.changes.Count(c => c.state == "already") + " already)");
                foreach (var c in s.changes.Where(c => c.state != "info")) sb.AppendLine("     [" + c.state + "] " + c.kind + " " + c.key + (string.IsNullOrEmpty(c.note) ? "" : " - " + c.note));
                foreach (var n in s.notes) sb.AppendLine("     · " + n);
                foreach (var n in s.numbers) sb.AppendLine("     " + n);
            }
            foreach (var b in p.blockers) sb.AppendLine("  blocker: " + b);
            foreach (var d in p.dry) sb.AppendLine("  " + d);
            if (k.Scene.isDirty) sb.AppendLine("  note: the scene raised its dirty flag during a read-only pass (ExecuteAlways components); it is reloaded, nothing is saved");
            sb.AppendLine("-> " + PlanFile(p.alias));
            return sb.ToString();
        }

        // the numbers this run measured next to the offline dry run's (Tools/Art/contentseat308_dry.py -> seat308_dry_<alias>.json)
        static List<string> CompareDry(Ctx k)
        {
            var rows = new List<string>();
            string file = Path.Combine(OutDir, "seat308_dry_" + k.Alias + ".json");
            if (!File.Exists(file)) { rows.Add("dry: no offline dry run to compare with (" + file + ")"); return rows; }
            JObject doc; try { doc = JObject.Parse(File.ReadAllText(file)); } catch (Exception e) { rows.Add("dry: unreadable " + file + " (" + e.Message + ")"); return rows; }
            var nums = doc["numbers"] as JObject; if (nums == null) { rows.Add("dry: no numbers in " + file); return rows; }
            var tol = Req(k.Data, "tolerances"); float tm = Num(tol, "dry_tol_m"), td = Num(tol, "dry_tol_deg"); int same = 0, diff = 0;
            foreach (var kv in k.Numbers.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var t = nums[kv.Key]; if (t == null) continue;
                float off = t.Value<float>(); bool angle = kv.Key.EndsWith("yaw", StringComparison.Ordinal);
                float d = angle ? Mathf.Abs(Mathf.DeltaAngle(kv.Value, off)) : Mathf.Abs(kv.Value - off);
                bool ok = d <= (angle ? td : tm) + 1e-4f; if (ok) same++; else diff++;
                rows.Add((ok ? "dry-same " : "DRY-DIFF ") + kv.Key + " editor " + F(kv.Value, "F3") + " offline " + F(off, "F3") + " (|d| " + F(d, "F3") + ")");
            }
            rows.Insert(0, "dry: " + same + " number(s) within " + F(tm) + " m / " + F(td, "F1") + " deg of the offline dry run, " + diff + " differ (height field " + Opt(doc, "height_sha") + ")");
            return rows;
        }

        // ------------------------------------------------------------------ apply

        static string Apply(string alias, string[] stepArgs, string commandText)
        {
            var steps = stepArgs.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).ToArray();
            var bad = steps.Where(s => !AllSteps.Contains(s)).ToArray();
            if (bad.Length > 0) return "refused: scene steps are " + string.Join(" ", AllSteps) + " (" + string.Join(",", bad) + " - D4 = grass-plan / grass-apply)";
            steps = AllSteps.Where(steps.Contains).ToArray();   // fixed order: the keeper point (D2) is measured from the moved house (D1)
            var k = Begin(alias, true, out string previous, out bool opened);
            var op = new Op { utc = BuildingAudit308.Utc(), command = commandText, scene = k.ScenePath, alias = k.Alias, steps = steps, dataSha = k.DataSha, sceneDataSha = k.SceneDataSha };
            bool saved = false;
            try
            {
                var plan = MakePlan(k, steps);
                WritePlan(plan);
                if (plan.blocked) { string text = PlanText(k, plan); GoBack(previous, k.Scene, opened); return "refused: nothing changed - " + string.Join(" | ", plan.blockers) + "\n" + text; }
                var todo = plan.steps.SelectMany(s => s.changes).Where(c => c.state == "apply").ToList();
                op.shaBefore = ShaOf(Harness303.Abs(k.ScenePath));
                op.attractionBefore = BuildingFix308.AttractionHash(k.Scene, k.Cfg.attractionRoot);
                op.changes.AddRange(plan.steps.SelectMany(s => s.changes));
                op.stepResults = plan.steps.Select(s => s.step + " " + (s.changes.Any(c => c.state == "apply") ? "applied" : s.status) + ": " + s.detail).ToList();
                if (todo.Count == 0)
                {
                    op.status = "already"; op.detail = "no change (idempotent); scene not saved"; op.shaAfter = op.shaBefore; op.attractionAfter = op.attractionBefore;
                    k.Ledger.ops.Add(op); SaveLedger(k.Ledger);
                    GoBack(previous, k.Scene, opened);
                    return "already: " + k.Alias + " - no change, scene sha " + Short(op.shaBefore) + "\n" + string.Join("\n", op.stepResults);
                }
                if (EditorUtility.IsDirty(k.Content) && todo.Any(c => c.kind == "point")) throw new Refuse(AssetDatabase.GetAssetPath(k.Content) + " has unsaved in-memory changes (another session?) - save or discard them first");
                string backupDir = Path.Combine(OutDir, "Backups", "seat308-" + op.utc); Directory.CreateDirectory(backupDir);
                op.backup = Path.Combine(backupDir, Path.GetFileName(k.ScenePath));
                File.Copy(Harness303.Abs(k.ScenePath), op.backup, false);
                // the ledger file must be writable before anything is saved
                SaveLedger(k.Ledger);
                var created = new List<Object>(); bool contentTouched = false;
                foreach (var c in todo)
                {
                    if (c.kind == "point" && !contentTouched)
                    {
                        string cp = AssetDatabase.GetAssetPath(k.Content); string copy = Path.Combine(backupDir, Path.GetFileName(cp));
                        File.Copy(Harness303.Abs(cp), copy, false); op.assetBackups.Add(cp + "|" + copy + "|" + ShaOf(Harness303.Abs(cp))); contentTouched = true;
                    }
                    Execute(k, c, created);
                }
                foreach (var o in created) if (o != null && AssetDatabase.Contains(o)) AssetDatabase.SaveAssetIfDirty(o);
                EditorSceneManager.MarkSceneDirty(k.Scene);
                if (!EditorSceneManager.SaveScene(k.Scene)) throw new InvalidOperationException("SaveScene returned false");
                saved = true;
                if (contentTouched) { EditorUtility.SetDirty(k.Content); AssetDatabase.SaveAssetIfDirty(k.Content); }
                op.shaAfter = ShaOf(Harness303.Abs(k.ScenePath));
                op.attractionAfter = BuildingFix308.AttractionHash(k.Scene, k.Cfg.attractionRoot);
                op.status = "applied";
                op.detail = todo.Count + " change(s) saved" + (op.attractionAfter == op.attractionBefore ? "" : "; WARNING Finish297_Attraction block hash changed - revert with revert:" + k.Alias);
                k.Ledger.ops.Add(op); SaveLedger(k.Ledger);
                string stale = k.Session != null && WorldMacroPlaytestSession.StaleHarnessSuffix307(k.Session.TestSaveSuffix) ? "\nWARN the session carries a stale harness save suffix '" + k.Session.TestSaveSuffix + "' (not written by this tool; see the play-exit backup note)" : "";
                GoBack(previous, k.Scene, opened);
                return "applied: " + k.Alias + " sha " + Short(op.shaBefore) + " -> " + Short(op.shaAfter) + ", backup " + op.backup + "\n" + string.Join("\n", op.stepResults) + "\n" + op.detail + stale +
                       "\nnext: ContentSeat308 verify:" + k.Alias + " -> Content308 scene-dry:" + k.Alias + " (no re-seat expected) -> Content308 scene-check:" + k.Alias + " -> NpcJobs306 author:" + k.ScenePath;
            }
            catch (Exception e)
            {
                op.status = "FAILED"; op.detail = e is Refuse ? e.Message : e.ToString();
                string after = "";
                try
                {
                    if (saved)
                    {
                        // the scene is on disk with the changes but the content asset / ledger step failed: record it as applied so revert works
                        op.shaAfter = ShaOf(Harness303.Abs(k.ScenePath)); op.status = "applied"; op.detail = "saved, then failed: " + op.detail;
                        after = "; the scene was saved - the ledger records the op as applied (revert:" + k.Alias + " undoes it)";
                    }
                    else
                    {
                        // nothing was saved: drop the half-applied in-memory edits (scene and content asset)
                        if (k.Content != null && EditorUtility.IsDirty(k.Content)) { Resources.UnloadAsset(k.Content); }
                        after = "; nothing saved, the scene is reloaded from disk";
                    }
                    k.Ledger.ops.Add(op); SaveLedger(k.Ledger);
                    GoBack(previous, k.Scene, opened);
                }
                catch (Exception e2) { after += "; clean-up failed: " + e2.Message; }
                return (e is Refuse ? "refused: " : "FAILED: ") + k.Alias + " - " + e.Message + after + (string.IsNullOrEmpty(op.backup) ? "" : " (backup " + op.backup + ")");
            }
        }

        // ------------------------------------------------------------------ execute / undo one change

        static void Execute(Ctx k, Change c, List<Object> created)
        {
            if (c.kind == "point")
            {
                var parts = c.key.Split('|'); var point = Array.Find(k.Content.Points ?? Array.Empty<PrologueContentSO.Point>(), p => p != null && p.Id == parts[1]);
                if (point == null || !ParseVec(c.after, out var v)) throw new InvalidOperationException("content point " + c.key);
                point.Position = v; EditorUtility.SetDirty(k.Content);
                return;
            }
            if (c.kind == "spawn")
            {
                // after = <prefab path>;<local pose>;<local scale>   key = <parent key>/<name>
                int cut = c.key.LastIndexOf('/'); var parent = Strict(k, c.key.Substring(0, cut)); var f3 = c.after.Split(';');
                if (ProtectedObject(k, parent) || !UnderOwnRoot(k, parent)) throw new Refuse("spawn parent " + c.key.Substring(0, cut) + " is protected or not under " + Str(k.Data, "root"));
                if (BuildingAudit308.Resolve(k.Scene, c.key) != null) throw new InvalidOperationException("spawn target already exists " + c.key);
                var prefab = f3.Length == 3 ? AssetDatabase.LoadAssetAtPath<GameObject>(f3[0]) : null;
                if (prefab == null || !BuildingFix308.ParsePose(f3[1], out var sp0, out var sq0) || !ParseVec(f3[2], out var ss0)) throw new InvalidOperationException("spawn " + c.key + ": " + c.after);
                var go = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject ?? throw new InvalidOperationException("InstantiatePrefab failed " + f3[0]);
                go.name = c.key.Substring(cut + 1); go.transform.localPosition = sp0; go.transform.localRotation = sq0; go.transform.localScale = ss0;
                EditorUtility.SetDirty(go);
                return;
            }
            if (c.kind == "hinge") { ExecuteHinge(k, c); return; }
            var t = BuildingAudit308.Resolve(k.Scene, c.key) ?? throw new InvalidOperationException("missing " + c.key);
            if (ProtectedObject(k, t)) throw new Refuse("protected object " + c.key);
            switch (c.kind)
            {
                case "pose":
                    if (!BuildingFix308.ParsePose(c.after, out var p, out var q)) throw new FormatException(c.after);
                    t.localPosition = p; t.localRotation = q; EditorUtility.SetDirty(t);
                    Physics.SyncTransforms();
                    break;
                case "scale":
                    if (!ParseVec(c.after, out var sv)) throw new FormatException(c.after);
                    t.localScale = sv; EditorUtility.SetDirty(t);
                    Physics.SyncTransforms();
                    break;
                case "enable":
                {
                    var r = t.GetComponent<MeshRenderer>() ?? throw new InvalidOperationException("no MeshRenderer on " + c.key);
                    r.enabled = c.after == "1"; EditorUtility.SetDirty(r);
                    break;
                }
                case "skirt":
                {
                    // t = the plate; the holder is created under the plate's parent (the inn)
                    string path = GroundFit299.StoneSkirt308(t, c.note.Split('|')[0], out string note);
                    if (path == null)
                    {
                        // only "the plate sits on the ground all round" is a result; a missing folder / material / plate mesh is a failure
                        if (!note.StartsWith("plate edge", StringComparison.Ordinal)) throw new InvalidOperationException("StoneSkirt308: " + note);
                        // nothing was made, so there is nothing to undo: the record is kept as "info" (not a live "apply"), with the
                        // pose hash in its note so the next plan answers "already" for the same pose
                        c.after = ""; c.state = "info"; c.note += "|no skirt: " + note; break;
                    }
                    if (path != c.after) c.note += "|mesh path differs from the plan: " + path;
                    c.after = path; c.note += "|" + note;
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (mesh != null) created.Add(mesh);
                    string pose = c.note.Split('|').Length > 1 ? c.note.Split('|')[1] : "";
                    Directory.CreateDirectory(OutDir);
                    File.WriteAllText(SharedFile, JsonUtility.ToJson(new Shared { skirtMesh = path, skirtPose = pose, utc = BuildingAudit308.Utc(), scene = k.ScenePath }, true), new UTF8Encoding(false));
                    break;
                }
                case "create-mesh":
                {
                    if (AssetDatabase.LoadAssetAtPath<Mesh>(c.after) != null) break;
                    var mesh = BuildingFix308.BuildCapMesh(k.Cfg, t, c.note);
                    mesh.name = Path.GetFileNameWithoutExtension(c.after);
                    BuildingFix308.EnsureFolder(Path.GetDirectoryName(c.after).Replace('\\', '/'));
                    AssetDatabase.CreateAsset(mesh, c.after); created.Add(mesh);
                    break;
                }
                case "mesh":
                {
                    var f = t.GetComponent<MeshFilter>() ?? throw new InvalidOperationException("no MeshFilter on " + c.key);
                    string path = c.after.Split('#')[0];
                    var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(m => c.after.EndsWith("#" + m.name, StringComparison.Ordinal)) ?? AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (mesh == null || mesh.vertexCount == 0) throw new InvalidOperationException("mesh asset missing or empty " + c.after);
                    f.sharedMesh = mesh; EditorUtility.SetDirty(f);
                    break;
                }
                default: throw new InvalidOperationException("unknown change kind " + c.kind);
            }
        }

        // null = undone, or the value is already the ledger's before value; "lost: …" = the object is gone, so nothing is left to undo
        // and the change is closed; anything else = skipped and still live (fix the cause and revert again, or revert …:force)
        static string Undo(Ctx k, Change c, string holderName, bool force)
        {
            const string forceHint = " (revert …:force writes the before value anyway)";
            if (c.kind == "point")
            {
                var parts = c.key.Split('|'); var point = Array.Find(k.Content.Points ?? Array.Empty<PrologueContentSO.Point>(), p => p != null && p.Id == parts[1]);
                if (point == null) return "lost: content point gone";
                if (!ParseVec(c.before, out var v)) return "no before value";
                float tol = Num(Req(k.Data, "keeper"), "body_tol_m");
                if (Vector3.Distance(point.Position, v) <= tol) return null;
                if (!force && ParseVec(c.after, out var av) && Vector3.Distance(point.Position, av) > tol) return "drifted: the point is at " + V(point.Position) + ", not the ledger's after value" + forceHint;
                point.Position = v; EditorUtility.SetDirty(k.Content); return null;
            }
            if (c.kind == "create-mesh" || c.kind == "hash") return null;   // the generated mesh asset stays on disk; nothing references it after the revert
            if (c.kind == "hinge") return UndoHinge(k, c, force);
            if (c.kind == "spawn")
            {
                var made = BuildingAudit308.Resolve(k.Scene, c.key);
                if (made == null) return null;                              // already gone = already the before state
                if (BuildingAudit308.KeyOf(made) != c.key) return "ambiguous key (same-named siblings): not destroyed";
                if (ProtectedObject(k, made) || !UnderOwnRoot(k, made)) return "not under " + Str(k.Data, "root") + " or protected: not destroyed";
                var parent = made.parent; Object.DestroyImmediate(made.gameObject); if (parent != null) EditorUtility.SetDirty(parent.gameObject);
                return null;
            }
            var t = BuildingAudit308.Resolve(k.Scene, c.key);
            if (t == null) return "lost: object not found";
            if (ProtectedObject(k, t)) return "under a protected tree";
            switch (c.kind)
            {
                case "pose":
                    if (!BuildingFix308.ParsePose(c.before, out var p, out var q)) return "no before value (the pose was already there at apply: restore content308_scene.json and run Content308 scene-apply)";
                    if (NearPose(k, t, c.before)) return null;
                    // a parent re-seated since the apply (Content308 scene-apply keeps the children's world pose) changes the local pose:
                    // the ledger's local before value would then be another world place
                    if (!force && !NearPose(k, t, c.after)) return "drifted: the local pose is " + PoseText(t.localPosition, t.localRotation) + ", not the ledger's after value (was its parent re-seated?)" + forceHint;
                    t.localPosition = p; t.localRotation = q; EditorUtility.SetDirty(t); Physics.SyncTransforms(); return null;
                case "scale":
                    if (!ParseVec(c.before, out var sv)) return "no before value";
                    t.localScale = sv; EditorUtility.SetDirty(t); Physics.SyncTransforms(); return null;
                case "enable":
                {
                    var r = t.GetComponent<MeshRenderer>(); if (r == null) return "lost: MeshRenderer gone";
                    r.enabled = c.before == "1"; EditorUtility.SetDirty(r); return null;
                }
                case "skirt":
                {
                    var holder = t.parent != null ? t.parent.Find(holderName) : null;
                    if (holder == null) return null;                        // no holder = already the before state
                    Object.DestroyImmediate(holder.gameObject); EditorUtility.SetDirty(t.parent.gameObject); return null;
                }
                case "mesh":
                {
                    var f = t.GetComponent<MeshFilter>(); if (f == null) return "lost: MeshFilter gone";
                    string now = MeshState(f);
                    if (now == c.before) return null;
                    if (!force && now != c.after) return "drifted: the mesh is " + (now.Length == 0 ? "(none)" : now) + ", not the ledger's after value" + forceHint;
                    if (string.IsNullOrEmpty(c.before)) f.sharedMesh = null;
                    else { string path = c.before.Split('#')[0]; f.sharedMesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(m => c.before.EndsWith("#" + m.name, StringComparison.Ordinal)) ?? AssetDatabase.LoadAssetAtPath<Mesh>(path); }
                    EditorUtility.SetDirty(f); return null;
                }
                default: return "unknown change kind " + c.kind;
            }
        }

        // ------------------------------------------------------------------ revert

        static string Revert(string alias, string[] args)
        {
            var words = (args ?? Array.Empty<string>()).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            bool force = words.Any(x => string.Equals(x, "force", StringComparison.OrdinalIgnoreCase));
            var rest = words.Where(x => !string.Equals(x, "force", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (rest.Length > 1) return "refused: revert:<alias>[:" + string.Join("|", AllSteps) + "][:force] (one step at a time)";
            string step = rest.Length == 0 ? null : rest[0].ToUpperInvariant();
            if (step != null && !AllSteps.Contains(step)) return "refused: revert:<alias>[:" + string.Join("|", AllSteps) + "][:force]";
            // the keeper's point is measured from the moved house: D1 never goes back alone
            var wanted = step == null ? SceneSteps : step == "D1" ? new[] { "D1", "D2" } : new[] { step };
            string scenePath = SceneOf(alias);
            var probe = LoadLedger(scenePath);
            if (!probe.ops.Any(o => !o.reverted && o.status == "applied" && o.changes.Any(c => c.state == "apply" && wanted.Contains(c.step))))
                return "refused: nothing to revert in " + LedgerFile(scenePath) + (step != null ? " for " + string.Join("+", wanted) : "");
            var k = Begin(alias, true, out string previous, out bool opened);
            var rev = new Op { utc = BuildingAudit308.Utc(), command = "revert:" + alias + (step != null ? ":" + step : "") + (force ? ":force" : ""), scene = k.ScenePath, alias = k.Alias, steps = wanted, dataSha = k.DataSha, sceneDataSha = k.SceneDataSha };
            try
            {
                var ops = k.Ledger.ops.Where(o => !o.reverted && o.status == "applied" && o.changes.Any(c => c.state == "apply" && wanted.Contains(c.step))).ToList();
                rev.shaBefore = ShaOf(Harness303.Abs(k.ScenePath));
                bool points = ops.Any(o => o.changes.Any(c => c.state == "apply" && c.kind == "point" && wanted.Contains(c.step)));
                if (points && EditorUtility.IsDirty(k.Content)) throw new Refuse(AssetDatabase.GetAssetPath(k.Content) + " has unsaved in-memory changes (another session?)");
                string backupDir = Path.Combine(OutDir, "Backups", "seat308-" + rev.utc + "-prerevert"); Directory.CreateDirectory(backupDir);
                rev.backup = Path.Combine(backupDir, Path.GetFileName(k.ScenePath)); File.Copy(Harness303.Abs(k.ScenePath), rev.backup, false);
                if (points) { string cp = AssetDatabase.GetAssetPath(k.Content); string copy = Path.Combine(backupDir, Path.GetFileName(cp)); File.Copy(Harness303.Abs(cp), copy, false); rev.assetBackups.Add(cp + "|" + copy + "|" + ShaOf(Harness303.Abs(cp))); }
                string holderName = Str(Req(k.Data, "inn"), "skirt_holder");
                int undone = 0; var skipped = new List<string>(); var done = new List<Change>(); var lost = new List<Change>();
                foreach (var op in Enumerable.Reverse(ops))
                    foreach (var c in Enumerable.Reverse(op.changes).Where(c => c.state == "apply" && wanted.Contains(c.step)).ToList())
                    {
                        string why = Undo(k, c, holderName, force);
                        if (why == null) { undone++; done.Add(c); }
                        else { skipped.Add(c.kind + " " + c.key + ": " + why); if (why.StartsWith("lost:", StringComparison.Ordinal)) lost.Add(c); }
                    }
                if (undone == 0)
                {
                    // nothing was set back: the scene and the content asset are not saved, and the copy just made is not kept
                    try { Directory.Delete(backupDir, true); } catch (Exception) { }
                    if (k.Content != null && EditorUtility.IsDirty(k.Content)) Resources.UnloadAsset(k.Content);
                    if (lost.Count == 0)
                    {
                        GoBack(previous, k.Scene, opened);
                        return "refused: revert " + k.Alias + " - nothing was set back and nothing was saved; still live: " + string.Join(" | ", skipped);
                    }
                    foreach (var c in lost) { c.state = "lost"; c.note += "|lost at revert " + rev.utc; }
                    foreach (var op in ops) if (op.changes.All(c => c.state != "apply")) { op.reverted = true; op.revertedUtc = rev.utc; }
                    rev.backup = ""; rev.assetBackups.Clear(); rev.shaAfter = rev.shaBefore; rev.status = "reverted";
                    rev.detail = "0 change(s) set back; " + lost.Count + " closed as lost (their object is gone); scene not saved" + (skipped.Count > lost.Count ? "; still live: " + (skipped.Count - lost.Count) : "") + ": " + string.Join(" | ", skipped);
                    rev.stepResults = skipped;
                    k.Ledger.ops.Add(rev); SaveLedger(k.Ledger);
                    GoBack(previous, k.Scene, opened);
                    return "reverted: " + k.Alias + " - " + rev.detail;
                }
                EditorSceneManager.MarkSceneDirty(k.Scene);
                if (!EditorSceneManager.SaveScene(k.Scene)) throw new InvalidOperationException("SaveScene returned false");
                if (points) { EditorUtility.SetDirty(k.Content); AssetDatabase.SaveAssetIfDirty(k.Content); }
                foreach (var c in done) c.state = "reverted";
                foreach (var c in lost) { c.state = "lost"; c.note += "|lost at revert " + rev.utc; }
                foreach (var op in ops) if (op.changes.All(c => c.state != "apply")) { op.reverted = true; op.revertedUtc = rev.utc; }
                rev.shaAfter = ShaOf(Harness303.Abs(k.ScenePath)); rev.status = "reverted";
                int live = skipped.Count - lost.Count;
                rev.detail = undone + " change(s) set back to their ledger values" + (lost.Count > 0 ? "; " + lost.Count + " closed as lost (object gone)" : "") + (live > 0 ? "; " + live + " skipped and STILL LIVE" : "") + (skipped.Count > 0 ? ": " + string.Join(" | ", skipped) : "");
                rev.stepResults = skipped;
                k.Ledger.ops.Add(rev); SaveLedger(k.Ledger);
                // a revert without a step never touches the relayout steps: say so while one of them is still applied
                var stay = NamedSteps.Where(s => !wanted.Contains(s) && StepLive(k.Ledger, s)).ToArray();
                string namedNote = stay.Length > 0 ? "\nnote: " + string.Join(", ", stay) + " stay applied (revert:" + k.Alias + ":<step> undoes one)" : "";
                GoBack(previous, k.Scene, opened);
                string remind = wanted.Contains("D1") || wanted.Contains("D5") ? "\nremember: content308_scene.json still names the repaired rows - run 'python Tools/Art/contentseat308_dry.py unpatch-data --write' before the next Content308 scene-apply, or that pass moves the inn / leaves the firewood rows off" : "";
                return "reverted: " + k.Alias + " sha " + Short(rev.shaBefore) + " -> " + Short(rev.shaAfter) + " - " + rev.detail + " (pre-revert backup " + rev.backup + ")" + remind + namedNote;
            }
            catch (Exception e)
            {
                // the ledger file is untouched; drop the half-reverted in-memory state
                try { if (k.Content != null && EditorUtility.IsDirty(k.Content)) Resources.UnloadAsset(k.Content); GoBack(previous, k.Scene, opened); } catch (Exception) { }
                return (e is Refuse ? "refused: " : "FAILED: ") + "revert " + k.Alias + " - " + e.Message + "; nothing saved (scene reloaded from disk" + (string.IsNullOrEmpty(rev.backup) ? "" : ", pre-revert copy " + rev.backup) + ")";
            }
        }

        // ------------------------------------------------------------------ diag (read only: one still with a cause switched off)

        // D4 cause test (SEATFIX308_SCOPE.md D4): the same still with (nograss) every CompactGrassRenderer266 of the scene disabled and / or
        // (lod0) every LODGroup within diag.lod_radius_m of the target forced to LOD0. Both switches live in memory only and are put
        // back before the scene is left; nothing is marked dirty and nothing is saved. The still is Presentation297's own "shot".
        static string Diag(string[] a)
        {
            if (a.Length < 6) return "refused: diag:<alias>:<base|nograss|lod0|nograss+lod0>:<name>:<eye x,y,z>:<target x,y,z>[:fov=60][:w=1600][:h=900]";
            var variants = a[2].Split('+').Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).ToArray();
            if (variants.Length == 0 || variants.Any(x => x != "base" && x != "nograss" && x != "lod0")) return "refused: diag variant is base | nograss | lod0 | nograss+lod0";
            var tv = a[5].Split(','); if (tv.Length != 3) return "refused: diag target is x,y,z";
            var target = new Vector3(float.Parse(tv[0], Inv), float.Parse(tv[1], Inv), float.Parse(tv[2], Inv));
            var k = Begin(a[1], false, out string previous, out bool opened);
            var grassOff = new List<CompactGrassRenderer266>(); var forced = new List<LODGroup>();
            var sb = new StringBuilder("ContentSeat308 diag " + k.Alias + " [" + string.Join("+", variants) + "] (in memory only; the scene is never saved)\n");
            try
            {
                float radius = Num(Req(k.Data, "diag"), "lod_radius_m");
                var roots = k.Scene.GetRootGameObjects();
                foreach (var g in roots.SelectMany(r => r.GetComponentsInChildren<LODGroup>(false)))
                {
                    if (Vector3.Distance(g.transform.position, target) > radius) continue;
                    var lods = g.GetLODs();
                    sb.AppendLine("  LODGroup " + BuildingAudit308.KeyOf(g.transform) + ": fadeMode " + g.fadeMode + ", animateCrossFading " + g.animateCrossFading + ", " + lods.Length + " LOD(s) [" + string.Join(", ", lods.Select(l => (l.renderers?.Length ?? 0) + " r @ " + F(l.screenRelativeTransitionHeight, "F3") + " fade " + F(l.fadeTransitionWidth))) + "]");
                    foreach (var r in lods.SelectMany(l => l.renderers ?? Array.Empty<Renderer>()).Where(r => r != null).Distinct())
                        sb.AppendLine("     " + r.name + " enabled " + r.enabled + ": " + string.Join(", ", r.sharedMaterials.Where(m => m != null).Select(m => m.name + " (" + m.shader.name + ", queue " + m.renderQueue + (m.HasProperty("_ZWrite") ? ", ZWrite " + F(m.GetFloat("_ZWrite"), "F0") : "") + ")")));
                    if (variants.Contains("lod0")) { g.ForceLOD(0); forced.Add(g); }
                }
                foreach (var gr in roots.SelectMany(r => r.GetComponentsInChildren<CompactGrassRenderer266>(false)))
                {
                    sb.AppendLine("  grass renderer " + BuildingAudit308.KeyOf(gr.transform) + " enabled " + gr.enabled + ", field " + (gr.Field != null ? AssetDatabase.GetAssetPath(gr.Field) : "(none)") + (gr.Field != null && gr.Field.Material != null ? ", material " + gr.Field.Material.name + " (" + gr.Field.Material.shader.name + ", queue " + gr.Field.Material.renderQueue + ")" : ""));
                    if (variants.Contains("nograss") && gr.enabled) { gr.enabled = false; grassOff.Add(gr); }
                }
                sb.AppendLine("  switched for this still: " + grassOff.Count + " grass renderer(s) off, " + forced.Count + " LODGroup(s) forced to LOD0 (within " + F(radius, "F0") + " m of the target)");
                string shot = Presentation297.Run("shot:" + string.Join(":", a.Skip(3)));
                sb.AppendLine("  still: " + shot);
            }
            finally
            {
                foreach (var gr in grassOff) if (gr != null) gr.enabled = true;
                foreach (var g in forced) if (g != null) g.ForceLOD(-1);
                GoBack(previous, k.Scene, opened);
            }
            sb.AppendLine("  restored: " + grassOff.Count + " grass renderer(s) on, " + forced.Count + " LODGroup(s) back to automatic");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ skirt-reset (the shared generated skirt mesh)

        static string SkirtReset()
        {
            RequireEditable();
            var data = LoadJson(DataFile, "seat data"); var inn = Req(data, "inn");
            foreach (var s in PostLedger308.Scenes)
                if (LoadLedger(s).ops.Any(o => !o.reverted && o.status == "applied" && o.changes.Any(c => c.kind == "skirt" && c.state == "apply")))
                    return "refused: " + PostLedger308.Short(s) + " still uses the skirt mesh (revert:" + PostLedger308.Short(s) + ":D1 first)";
            var sh = LoadShared();
            if (string.IsNullOrEmpty(sh.skirtMesh)) return "refused: no skirt mesh recorded in " + SharedFile;
            if (!sh.skirtMesh.StartsWith(Str(inn, "skirt_folder") + "/", StringComparison.Ordinal) || Harness303.IsProtected(sh.skirtMesh)) return "refused: " + sh.skirtMesh + " is not under the skirt folder";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(sh.skirtMesh) == null) { File.Delete(SharedFile); return "skirt-reset: " + sh.skirtMesh + " is already gone; record cleared"; }
            string stale = Path.GetFileNameWithoutExtension(sh.skirtMesh) + "_stale" + BuildingAudit308.Utc();
            string err = AssetDatabase.RenameAsset(sh.skirtMesh, stale);
            if (!string.IsNullOrEmpty(err)) return "refused: rename failed - " + err;
            File.Delete(SharedFile);
            return "skirt-reset: " + sh.skirtMesh + " renamed to " + stale + " (nothing deleted); the next apply generates a new mesh for the new pose";
        }
    }
}
