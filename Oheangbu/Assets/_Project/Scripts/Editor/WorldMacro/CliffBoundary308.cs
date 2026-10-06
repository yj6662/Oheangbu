using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 cliff boundary ledger (SPEC-WORLD-CLIFF-BOUNDARY-308 "편집기 명령·원장", D308-9c Q1 = terrain tile references may be re-pointed).
    // Every command is idempotent, has a dry form, backs up what it writes, records before-values and hashes, and answers a refusal
    // STRING (never a dialog) in Play mode, on a dirty / unknown scene and on a protected path. Assets are saved one by one
    // (SaveAssetIfDirty); AssetDatabase.SaveAssets is never called. Protected FILES (Watershed295 / Reworld292 / MountainTrail285 trees,
    // W_Demo_Compact, 03_Content) are never written: the only touch under Reworld292_Terrain is the MeshFilter / MeshCollider
    // reference of the tiles the surface ledger names.
    // Queue: Oheangbu.EditorTools.WorldMacro.CliffBoundary308 Run "<command>"      (<scene> = arch296 | folk298 | main)
    //   status                               config, stage fields, ledgers, height source of every scene
    //   surface:<stage>[:dry]                stage field -> Surface/height_<stage>.bytes + re-sampled tile meshes Meshes/Terrain/<stage>/ (run in an open target scene)
    //   tiles:<scene>[:stage=<s>][:dry]      re-point the named tiles' MeshFilter / MeshCollider and the scene layout's FinalSurface
    //   revert:<scene>[:part=refs|layout][:dry]   restore the recorded originals exactly
    //   veg:<scene>[:partial][:dry]          the sheets the config lists for the stage: plants on faces removed, plants on lifted ground re-seated. The sheets
    //                                        are shared by the three scenes, so it runs ONCE, after tiles of all three (Spec chain step 5); :partial overrides
    //   veg-revert:<scene>[:stage=<s>][:dry] restore the sheet backup of that stage's veg run (default: the stage of the scene's veg step)
    //   check:<scene>[:stage=<s>][:max=N]    tile / ledger integrity, height sources, physics ground vs field, face heights, capsule probes -> Out/cb308-check-<scene>.json + .txt
    //   walk:<scene>[:stage=<s>]             the declared point-to-point walks -> Out/cb308-walk-<scene>.json + .txt
    //                                        (both take :walkablejump = the what-if capsule that jumps only from walkable support; files get that suffix)
    //   rim:<scene>[:stage=<s>][:id=<top>][:every=N][:max=N]   stage 1b, read only: the capsule pushed off every rim station of a reachable cliff top
    //                                        under the motor rules and the session fall rule as coded -> Out/cb308-rim-<scene>.json + .txt
    //   wall | skins | guk                   stage 1b: built by their own ledger tools (config `packages`): the answer names the tool, `status` lists its ledgers
    //   grass | wash | map                   phase 1b without a tool yet: a stub string (the ledger keeps a step slot for each)
    // Stage 1b (Tools/Unity/Stage308_cliff1b/terrain): veg moves plants from the field they stand on (an applied earlier stage) to this stage's field;
    // check lists recorded exceptions and declared lift / descent pieces as EXPECTED (check data format cb308.check.2).
    // surface-dry / tiles-dry / revert-dry / veg-dry / veg-revert-dry are accepted as the same dry forms.
    public static partial class CliffBoundary308
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Run(string command)
        {
            string[] a = (command ?? "").Split(':');
            string verb = a[0]; bool dry = false;
            if (verb.EndsWith("-dry", StringComparison.Ordinal)) { verb = verb.Substring(0, verb.Length - 4); dry = true; }
            try
            {
                var cfg = CliffCore308.LoadConfig();
                var opt = PostLedger308.Options(a.Skip(2));
                if (opt.ContainsKey("dry")) dry = true;
                string target = a.Length > 1 ? a[1] : "";
                switch (verb)
                {
                    case "status": return Status(cfg);
                    case "surface":
                        if (target.Length == 0) throw new PostLedger308.Refused("use surface:<stage>[:dry]");
                        return Surface(cfg, target, dry);
                    case "tiles": return Tiles(cfg, Need(target, verb), opt, dry);
                    case "revert": return Revert(cfg, Need(target, verb), opt, dry);
                    case "veg": return Veg(cfg, Need(target, verb), opt, dry);
                    case "veg-revert": return VegRevert(cfg, Need(target, verb), opt, dry);
                    case "check": return Check(cfg, Need(target, verb), opt);
                    case "walk": return Walk(cfg, Need(target, verb), opt);
                    case "rim": return Rim(cfg, Need(target, verb), opt);
                    default:
                        var owner = LoadConfig1b().packages.FirstOrDefault(p => p.name == verb);
                        if (owner != null)
                            return "use " + owner.tool + ": '" + verb + "' is built by its own stage-1b ledger tool (Oheangbu.EditorTools.WorldMacro." + owner.tool + " - plan / apply / verify / revert per scene); CliffBoundary308 read and wrote nothing. `status` lists its ledgers";
                        if (cfg.phase1b != null && cfg.phase1b.Contains(verb))
                            return "not implemented: '" + verb + "' is a phase 1b command without a tool yet (Spec 단계 1b); the scene ledger keeps a '" + verb + "' step slot, nothing was read or written";
                        throw new PostLedger308.Refused("unknown command '" + command + "'");
                }
            }
            catch (PostLedger308.Refused r) { return "refused: " + r.Message; }
            catch (Exception e) { Debug.LogException(e); return "error: " + e.GetType().Name + ": " + e.Message + " (see the console; ledgers show how far the command got)"; }
        }

        static string Need(string scene, string verb)
        {
            if (string.IsNullOrEmpty(scene)) throw new PostLedger308.Refused("use " + verb + ":<scene> (arch296 | folk298 | main)");
            return scene;
        }

        // ---------------------------------------------------------------- ledgers

        [Serializable] internal sealed class LodLedger { public string lod, filter; public CliffCore308.MeshRecord original, applied; }
        [Serializable] internal sealed class TileLedger { public string tile, key, firstTouchedUtc, firstTouchedStage; public bool applied; public LodLedger[] lods; public CliffCore308.MeshRecord colliderOriginal, colliderApplied; }
        [Serializable] internal sealed class StepLedger { public string name, state, stage, utc, note; }
        [Serializable] internal sealed class SceneLedger
        {
            public string format = "cb308.ledger.1", scene, key, state = "none", stage = "", utc = "", baseSha256 = "", opsSha256 = "", heightSha256 = "", surfaceLedgerSha256 = "", configSha256 = "";
            public string layout = "", layoutGuid = "", layoutOriginalSurface = "", layoutOriginalSurfaceGuid = "", layoutOriginalSurfaceSha256 = "", layoutAppliedSurface = "", layoutAppliedSurfaceGuid = "";
            public bool layoutApplied;
            public string sceneShaPristine = "", sceneShaBefore = "", sceneShaAfter = "", backup = "", colliderSetSha256 = "";
            public TileLedger[] tiles = new TileLedger[0]; public StepLedger[] steps = new StepLedger[0]; public string[] history = new string[0];
        }

        static string LedgerFile(CliffCore308.Config cfg, string key, string extension = "json") => CliffCore308.OutFile(cfg, "cb308-" + key + "." + extension);
        static string SurfaceLedgerFile(CliffCore308.Config cfg, string stage) => CliffCore308.OutFile(cfg, "cb308-surface-" + stage + ".json");

        internal static SceneLedger ReadLedger(CliffCore308.Config cfg, string scenePath)
        {
            string key = CliffCore308.SceneKey(cfg, scenePath); if (key == null) return null;
            string file = LedgerFile(cfg, key);
            if (!File.Exists(file)) return new SceneLedger { scene = scenePath, key = key };
            var l = JsonUtility.FromJson<SceneLedger>(File.ReadAllText(file, Encoding.UTF8));
            if (l == null || l.scene != scenePath) throw new PostLedger308.Refused("ledger " + file + " does not belong to " + scenePath);
            if (l.tiles == null) l.tiles = new TileLedger[0]; if (l.steps == null) l.steps = new StepLedger[0]; if (l.history == null) l.history = new string[0];
            return l;
        }

        static void WriteLedger(CliffCore308.Config cfg, SceneLedger l, string note)
        {
            l.utc = PostLedger308.Utc();
            l.history = l.history.Concat(new[] { l.utc + " " + note }).ToArray();
            // phase 1b commands share this file: one step slot each, filled by the command that owns it
            var steps = l.steps.ToList();
            foreach (string name in new[] { "veg" }.Concat(cfg.phase1b ?? new string[0]))
                if (!steps.Any(s => s.name == name)) steps.Add(new StepLedger { name = name, state = "none", stage = "", utc = "", note = name == "veg" ? "" : "phase 1b" });
            l.steps = steps.ToArray();
            CliffCore308.WriteText(LedgerFile(cfg, l.key), JsonUtility.ToJson(l, true));
            // the Spec's text ledger: base sha, ops sha, stage, mesh hash of every tile
            var sb = new StringBuilder();
            sb.AppendLine("#308 cliff boundary ledger - " + l.scene);
            sb.AppendLine("state " + l.state + " | stage " + (l.stage == "" ? "-" : l.stage) + " | " + l.utc);
            sb.AppendLine("base sha256   " + l.baseSha256);
            sb.AppendLine("ops sha256    " + l.opsSha256);
            sb.AppendLine("height sha256 " + l.heightSha256);
            sb.AppendLine("layout " + l.layout + ": FinalSurface " + l.layoutOriginalSurface + " -> " + (l.layoutApplied ? l.layoutAppliedSurface : "(original)"));
            sb.AppendLine("scene file sha256 pre-#308 " + l.sceneShaPristine + " | last command: before " + l.sceneShaBefore + " after " + l.sceneShaAfter + " | backup " + l.backup);
            sb.AppendLine("collider set sha256 " + l.colliderSetSha256 + " (must be equal in the three scenes before the NavMesh bake)");
            sb.AppendLine("tiles " + l.tiles.Length + " (applied " + l.tiles.Count(t => t.applied) + ")");
            foreach (var t in l.tiles)
            {
                sb.AppendLine(t.tile + " (" + t.key + ") " + (t.applied ? "applied" : "original") + ", first touched " + t.firstTouchedUtc + " stage " + t.firstTouchedStage);
                foreach (var lod in t.lods)
                    sb.AppendLine("  " + lod.lod + " original " + lod.original.path + " [" + lod.original.guid + ":" + lod.original.fileId + ", " + lod.original.vertices + " v, file sha " + lod.original.fileSha256 + "]"
                        + " -> " + (string.IsNullOrEmpty(lod.applied.guid) ? "-" : lod.applied.path + " [" + lod.applied.guid + ":" + lod.applied.fileId + ", file sha " + lod.applied.fileSha256 + "]"));
                sb.AppendLine("  collider original " + t.colliderOriginal.path + " [" + t.colliderOriginal.guid + ":" + t.colliderOriginal.fileId + "] -> " + (string.IsNullOrEmpty(t.colliderApplied.guid) ? "-" : t.colliderApplied.path));
            }
            foreach (var s in l.steps) sb.AppendLine("step " + s.name + ": " + s.state + (s.stage != "" ? " stage " + s.stage : "") + (s.note != "" ? " (" + s.note + ")" : ""));
            sb.AppendLine("history:");
            foreach (string h in l.history) sb.AppendLine("  " + h);
            CliffCore308.WriteText(LedgerFile(cfg, l.key, "txt"), sb.ToString().TrimEnd());
        }

        internal static void SetStep(SceneLedger l, string name, string state, string stage, string note)
        {
            var steps = l.steps.ToList(); var s = steps.FirstOrDefault(x => x.name == name);
            if (s == null) { s = new StepLedger { name = name }; steps.Add(s); }
            s.state = state; s.stage = stage; s.utc = PostLedger308.Utc(); s.note = note; l.steps = steps.ToArray();
        }

        static CliffCore308.SurfaceBuild ReadSurfaceLedger(CliffCore308.Config cfg, string stage, out string sha)
        {
            string file = SurfaceLedgerFile(cfg, stage); sha = "";
            if (!File.Exists(file)) return null;
            var bytes = File.ReadAllBytes(file); sha = CliffCore308.Sha(bytes);
            return JsonUtility.FromJson<CliffCore308.SurfaceBuild>(Encoding.UTF8.GetString(bytes));
        }

        /// <summary>Stages that have a surface ledger (built meshes).</summary>
        static string[] BuiltStages(CliffCore308.Config cfg)
        {
            string dir = PostLedger308.RepoPath(cfg.outDir);
            if (!Directory.Exists(dir)) return new string[0];
            return Directory.GetFiles(dir, "cb308-surface-*.json").Select(f => Path.GetFileNameWithoutExtension(f).Substring("cb308-surface-".Length)).Where(s => !s.EndsWith(".boxes", StringComparison.Ordinal)).OrderBy(s => s).ToArray();
        }

        static string StageOption(CliffCore308.Config cfg, Dictionary<string, string> opt, SceneLedger ledger)
        {
            if (opt != null && opt.TryGetValue("stage", out string s) && s.Length > 0) return s;
            var built = BuiltStages(cfg);
            if (built.Length == 1) return built[0];
            if (ledger != null && ledger.state == "applied" && ledger.stage != "") return ledger.stage;
            throw new PostLedger308.Refused(built.Length == 0 ? "no surface ledger yet (run surface:<stage> first)" : "several stages are built (" + string.Join(", ", built) + "): add :stage=<s>");
        }

        // ---------------------------------------------------------------- status

        static string Status(CliffCore308.Config cfg)
        {
            var sb = new StringBuilder();
            var scene = SceneManager.GetActiveScene();
            sb.AppendLine("CliffBoundary308 status | config " + CliffCore308.ConfigPath + " sha " + cfg.Sha256.Substring(0, 12) + " (" + cfg.version + ") | active scene " + scene.path + " dirty=" + scene.isDirty + " playing=" + EditorApplication.isPlaying);
            string baseAbs = PostLedger308.Abs(cfg.baseHeight); string baseSha = CliffCore308.ShaFile(baseAbs);
            sb.AppendLine("base height " + cfg.baseHeight + " sha " + (baseSha == "" ? "MISSING" : baseSha) + (baseSha == cfg.baseHeightSha256 ? " (= config)" : " (DIFFERS from config " + cfg.baseHeightSha256 + ")"));
            string stageDir = PostLedger308.RepoPath(cfg.stageDir);
            string pattern = cfg.stageFile.Replace("{stage}", "*");
            var fields = Directory.Exists(stageDir) ? Directory.GetFiles(stageDir, pattern).OrderBy(f => f).ToArray() : new string[0];
            sb.AppendLine("stage fields in " + cfg.stageDir + ": " + fields.Length);
            string prefix = cfg.stageFile.Substring(0, cfg.stageFile.IndexOf("{stage}", StringComparison.Ordinal)), suffix = cfg.stageFile.Substring(cfg.stageFile.IndexOf("{stage}", StringComparison.Ordinal) + 7);
            foreach (string f in fields)
            {
                string name = Path.GetFileName(f), id = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
                bool report = File.Exists(PostLedger308.RepoPath(cfg.stageDir + "/" + cfg.stageReport.Replace("{stage}", id)));
                sb.AppendLine("  " + id + ": " + name + " sha " + CliffCore308.ShaFile(f).Substring(0, 12) + ", report " + (report ? "present" : "MISSING (dry only)") + (id.IndexOf("PROTOTYPE", StringComparison.OrdinalIgnoreCase) >= 0 ? ", prototype (dry only)" : "")
                    + ", check data " + (File.Exists(PostLedger308.RepoPath(cfg.checkData.Replace("{stage}", id))) ? "present" : "missing"));
            }
            var built = BuiltStages(cfg);
            sb.AppendLine("surface ledgers: " + (built.Length == 0 ? "none" : ""));
            foreach (string stage in built)
            {
                var s = ReadSurfaceLedger(cfg, stage, out _);
                int missing = s.tile.SelectMany(t => t.lods).Count(l => !File.Exists(PostLedger308.Abs(l.mesh.path)));
                sb.AppendLine("  " + stage + ": " + s.tiles + " tiles [" + string.Join(" ", s.tileKeys) + "], height sha " + s.heightSha256.Substring(0, 12) + ", ops sha " + s.opsSha256.Substring(0, Math.Min(12, s.opsSha256.Length)) + ", built " + s.utc + " in " + s.scene + ", mesh assets missing " + missing);
            }
            sb.AppendLine("scenes:");
            foreach (var sc in cfg.scenes)
            {
                var l = ReadLedger(cfg, sc.path);
                string height = CliffHeight308.For(sc.path, out string source);
                sb.AppendLine("  " + sc.key + ": ledger " + l.state + (l.stage != "" ? " stage " + l.stage : "") + ", tiles " + l.tiles.Count(t => t.applied) + "/" + l.tiles.Length + " applied, layout surface " + (l.layoutApplied ? "applied" : "original")
                    + " | height " + height + " sha " + Short(CliffHeight308.Sha(height)) + " [" + source + "]" + (l.colliderSetSha256 != "" ? " | collider set " + l.colliderSetSha256.Substring(0, 12) : ""));
                foreach (var st in l.steps.Where(x => x.state != "none")) sb.AppendLine("    step " + st.name + ": " + st.state + " stage " + st.stage + " " + st.utc);
            }
            sb.AppendLine("other scenes (W_Demo_Compact ...): " + cfg.baseHeight + " [base]");
            AppendPackages(cfg, sb);
            string outDir = PostLedger308.RepoPath(cfg.outDir);
            foreach (string f in Directory.Exists(outDir) ? Directory.GetFiles(outDir, "cb308-rim-*.json").OrderBy(x => x).ToArray() : new string[0])
            {
                RimReport r = null; try { r = JsonUtility.FromJson<RimReport>(File.ReadAllText(f, Encoding.UTF8)); } catch (Exception) { }
                sb.AppendLine("rim probe " + Path.GetFileName(f) + ": " + (r == null ? "unreadable" : r.verdict + ", stage " + r.stage + ", stations " + r.stations + ", survives onto a blocked side " + r.survivesBlockedSide + ", utc " + r.utc));
            }
            sb.Append("phase 1b commands without a tool (stubs): " + string.Join(", ", cfg.phase1b ?? new string[0]));
            return sb.ToString();
        }

        static string Short(string sha) => string.IsNullOrEmpty(sha) ? "-" : sha.Substring(0, Math.Min(12, sha.Length));

        // ---------------------------------------------------------------- surface:<stage>

        static string Surface(CliffCore308.Config cfg, string stageId, bool dry)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
            if (!dry) PostLedger308.RequireEditable();
            var scene = SceneManager.GetActiveScene();
            string key = CliffCore308.SceneKey(cfg, scene.path);
            if (key == null) throw new PostLedger308.Refused("the active scene '" + scene.path + "' is not a #308 target scene; surface reads the tile meshes of an open target scene (" + string.Join(" | ", cfg.scenes.Select(s => s.key)) + ")");
            if (PostLedger308.IsProtectedPath(scene.path)) throw new PostLedger308.Refused("protected path " + scene.path);
            var stage = CliffCore308.LoadStage(cfg, stageId, !dry);
            string meshDir = cfg.assetRoot + "/Meshes/Terrain/" + stageId, heightAsset = cfg.assetRoot + "/Surface/height_" + stageId + ".bytes";
            if (PostLedger308.IsProtectedPath(meshDir + "/") || PostLedger308.IsProtectedPath(heightAsset)) throw new PostLedger308.Refused("protected output path " + meshDir);

            var prior = ReadSurfaceLedger(cfg, stageId, out _);
            if (!dry && prior != null)
            {
                bool same = prior.heightSha256 == stage.HeightSha && prior.boxSha256 == stage.BoxSha && prior.baseSha256 == stage.BaseSha && prior.opsSha256 == (stage.OpsSha ?? "") && prior.boxMargin == cfg.boxMargin
                    && CliffCore308.ShaFile(PostLedger308.Abs(heightAsset)) == stage.HeightSha
                    && prior.tile.SelectMany(t => t.lods).All(l => CliffCore308.ShaFile(PostLedger308.Abs(l.mesh.path)) == l.mesh.fileSha256 && l.mesh.fileSha256 != "");
                if (same) return "surface " + stageId + ": already built from this height (sha " + stage.HeightSha.Substring(0, 12) + ") - no-op. " + prior.tiles + " tiles, " + prior.tile.Sum(t => t.lods.Length) + " meshes in " + meshDir;
                var users = cfg.scenes.Select(s => ReadLedger(cfg, s.path)).Where(l => l.state == "applied" && l.stage == stageId).Select(l => l.key).ToArray();
                if (users.Length > 0) throw new PostLedger308.Refused("stage " + stageId + " meshes are referenced by [" + string.Join(", ", users) + "] and the stage data changed; revert:<scene> those scenes first (a rebuilt mesh under an applied scene would break its ledger hashes)");
            }

            // a scene that already carries a #308 stage re-samples from its recorded pre-#308 meshes, never from a stage mesh
            var ledger = ReadLedger(cfg, scene.path);
            var originals = new Dictionary<string, CliffCore308.MeshRecord>();
            foreach (var t in ledger.tiles) foreach (var l in t.lods) if (!string.IsNullOrEmpty(l.original.guid)) originals[t.tile + "/" + l.lod] = l.original;
            Func<string, Mesh> lookup = id =>
            {
                if (!originals.TryGetValue(id, out var rec)) return null;
                var m = CliffCore308.LoadMesh(rec.guid, rec.fileId);
                if (m == null) throw new PostLedger308.Refused("the recorded original of " + id + " (" + rec.path + ", guid " + rec.guid + ") cannot be loaded");
                return m;
            };

            Func<CliffCore308.TileRef, CliffCore308.LodRef, Mesh, Mesh> sink = null;
            var created = new List<string>();
            if (!dry)
            {
                DevSceneKit.EnsureFolder(meshDir); DevSceneKit.EnsureFolder(cfg.assetRoot + "/Surface");
                sink = (t, l, copy) =>
                {
                    string path = meshDir + "/" + t.Name + "_" + l.Lod + "_cb308_" + stageId + ".asset";
                    if (!path.StartsWith(cfg.assetRoot + "/", StringComparison.Ordinal) || PostLedger308.IsProtectedPath(path)) { Object.DestroyImmediate(copy); throw new PostLedger308.Refused("refusing to write " + path); }
                    copy.name = Path.GetFileNameWithoutExtension(path);
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    Mesh asset;
                    if (existing == null) { AssetDatabase.CreateAsset(copy, path); asset = copy; }
                    else { EditorUtility.CopySerialized(copy, existing); EditorUtility.SetDirty(existing); Object.DestroyImmediate(copy); asset = existing; }
                    AssetDatabase.SaveAssetIfDirty(asset);
                    created.Add(path);
                    return asset;
                };
            }
            var build = CliffCore308.BuildSurface(cfg, scene, stage, lookup, sink);
            build.utc = PostLedger308.Utc(); build.heightAsset = heightAsset;
            if (dry) return "surface-dry " + CliffCore308.Summary(build) + " | would write " + build.tile.Sum(t => t.lods.Length) + " meshes under " + meshDir + " and " + heightAsset + " | nothing written";

            string heightAbs = PostLedger308.Abs(heightAsset);
            if (CliffCore308.ShaFile(heightAbs) != stage.HeightSha) { File.Copy(stage.HeightFile, heightAbs, true); AssetDatabase.ImportAsset(heightAsset, ImportAssetOptions.ForceUpdate); }
            if (AssetDatabase.LoadAssetAtPath<TextAsset>(heightAsset) == null) throw new PostLedger308.Refused("the height asset did not import: " + heightAsset);
            // the op boxes the ledger names, beside the ledger
            var boxes = new StringBuilder("{\"stage\":\"" + stageId + "\",\"source\":\"" + stage.BoxSource + "\",\"margin\":" + cfg.boxMargin.ToString("R", Inv) + ",\"sha256\":\"" + stage.BoxSha + "\",\"boxes\":[");
            for (int i = 0; i < stage.Boxes.Count; i++)
            {
                var b = stage.Boxes[i];
                boxes.Append(i > 0 ? "," : "").Append("[").Append(b.xMin.ToString("R", Inv)).Append(",").Append(b.yMin.ToString("R", Inv)).Append(",").Append(b.xMax.ToString("R", Inv)).Append(",").Append(b.yMax.ToString("R", Inv)).Append("]");
            }
            boxes.Append("]}");
            CliffCore308.WriteText(CliffCore308.OutFile(cfg, "cb308-surface-" + stageId + ".boxes.json"), boxes.ToString());
            CliffCore308.WriteText(SurfaceLedgerFile(cfg, stageId), JsonUtility.ToJson(build, true));
            return "surface " + CliffCore308.Summary(build) + " | wrote " + created.Count + " meshes under " + meshDir + ", " + heightAsset + " | ledger " + SurfaceLedgerFile(cfg, stageId) + " | scene untouched (dirty=" + scene.isDirty + ")";
        }
    }
}
