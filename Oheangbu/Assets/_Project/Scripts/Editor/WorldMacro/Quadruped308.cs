using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Newtonsoft.Json.Linq;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-7 (SPEC-ANIM-QUADRUPED-308 design 3, TEST): free Asset Store quadruped motion, retargeted offline by
    // Tools/Blender/Quadruped308/retarget.py onto the existing Folklore298 Rig298 (our mesh, weights and bone names unchanged).
    // Queue-safe: refusals come back as strings, never a dialog. Run(string):
    //   status                          bake reports, imported assets, apply records (no scene is opened)
    //   audit                           AC-Q7: no raw pack file (extract-manifest.json size+SHA256) anywhere under Assets
    //   import[:<species>]              copy Art/Characters/Quadruped308/Retarget/<sp>/<sp>_RT308.fbx to Assets/_Project/Art/Characters/
    //                                   Quadruped308/<sp>/, Generic + CopyFromOther avatar (the Folklore298 model), then refuse unless every
    //                                   curve path resolves on the model, bone rest frames match it and each PASS clip length matches the report
    //   apply:<scene>[|k=v,...]         one ledger scene (296 | 298 | main | asset path): every EnemyRigMotion298 whose rig is
    //                                   <species>_Rig298 gets the PASS roles (gate FAIL roles keep their procedural clip), AttackPeak01 = baked
    //                                   peak, WalkMetresPerSecond = stance-foot speed measured here on the actual actor, Death visual time,
    //                                   EnemyFootPlacement298 re-calibrated from the new Idle; scene file backed up, record written, only that
    //                                   scene saved. Options (comma-separated, optional "<species>." prefix): <Role>=<extra>
    //                                   (e.g. bulgasari.Attack=Attack3, fox_spirit.Walk=Pace), Run=<extra> with RunThreshold=<m/s>,
    //                                   species=<id> (only that species). Without options only the Spec roles that PASS are used.
    //                                   Walk/Run sources must also pass the stance-slip gate (bake predictor <= .15), else the
    //                                   procedural Walk stays / the Run option is refused. The import receipt SHA must match the bake.
    //   apply-all[|k=v,...]             ledger order Architecture296 -> Folklore298 -> W_Demo_Main, stops at the first refusal/failure
    //   revert:<scene> | revert-all     restores exactly the recorded clips/values and the previous calibration pose
    // Protected trees (Watershed295*, Reworld292*, MountainTrail285*) are never touched; a dirty open scene refuses everything.
    public static class Quadruped308
    {
        public const string AssetDir = "Assets/_Project/Art/Characters/Quadruped308";
        const string Scene296 = "Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity";
        const string Scene298 = "Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity";
        const string SceneMain = "Assets/_Project/Scenes/World/W_Demo_Main.unity";
        static readonly string[] Ledger = { Scene296, Scene298, SceneMain };
        static readonly string[] Species = { "bulgasari", "fox_spirit" };
        static readonly string[] Roles = { "Idle", "Walk", "Attack", "Hit", "Stun", "Death" };
        static readonly string[] ProtectedRoots = { "Watershed295", "Reworld292", "MountainTrail285" };
        static readonly string[] Legs = { "Fore_L", "Fore_R", "Hind_L", "Hind_R" };
        const float WalkCrossCheck = .10f;   // AC-Q3: Unity-measured speed vs baked report, TEST
        // Spec design 1 offline gate "stance slip" (AC-Q3 15%, TEST): a gait clip whose baked stance-phase slip predictor exceeds this
        // is a gate FAIL for the Walk/Run slot and keeps the procedural clip (retarget.py records the predictor but does not gate it).
        const float MaxStanceSlip = .15f;

        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        static string ProjectFile(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        static string Out => Path.Combine(Repo, "Art/Characters/Quadruped308");
        static string SourceFbx(string sp) => Path.Combine(Out, "Retarget", sp, sp + "_RT308.fbx");
        static string ReportFile(string sp) => Path.Combine(Out, "Retarget", sp, sp + "_RT308.report.json");
        static string AssetFbx(string sp) => AssetDir + "/" + sp + "/" + sp + "_RT308.fbx";
        static string ImportReceipt(string sp) => Path.Combine(Out, "reports", "unity-import-" + sp + ".json");
        static string RecordFile(string scene) => Path.Combine(Out, "Backups", "apply_" + Path.GetFileNameWithoutExtension(scene) + ".json");

        // ------------------------------------------------------------------ report (JsonUtility subset of <sp>_RT308.report.json)
        [Serializable] public sealed class RoleRow { public string role, clip; public bool pass, loop, extra; public float seconds, peak01, speedUnity; }
        [Serializable] public sealed class UnityPart
        {
            public string species, rigObject, fbx;
            public float unityScale, walkMetresPerSecondNative, walkMetresPerSecondUnity, attackPeak01;
            public RoleRow[] roles = Array.Empty<RoleRow>();
        }
        [Serializable] public sealed class Roundtrip { public string status; }
        [Serializable] public sealed class Outputs { public string fbx, fbxSha256; }
        [Serializable] public sealed class Report { public string species, status; public bool candidateUnchanged; public UnityPart unity; public Roundtrip roundtrip; public Outputs outputs; }
        [Serializable] sealed class ManifestRow { public string pack, path, sha256; public long bytes; }
        [Serializable] sealed class ManifestRows { public ManifestRow[] rows = Array.Empty<ManifestRow>(); }
        [Serializable] sealed class ImportRow { public string species, asset, model, utc, fbxSha256; public string[] clips, notes; public bool passed; }

        [Serializable] sealed class ClipRef { public string path = "", name = ""; }
        // Exact pre-apply foot calibration (serialized LegBinding fields), so revert restores it bit-for-bit instead of re-deriving it.
        [Serializable] sealed class LegCal
        {
            public string name = "", calibrationSource = "";
            public Vector3 soleLocal, soleUpLocal, bendActorLocal;
            public float restSoleActorY, upperModelLength, lowerModelLength;
            public int weightedSurfaceVertices;
        }
        [Serializable] sealed class ActorRecord
        {
            public string path = "", species = "", footBefore = "", footAfter = "";
            public string[] changed = Array.Empty<string>();
            public ClipRef idle = new ClipRef(), walk = new ClipRef(), attack = new ClipRef(), hit = new ClipRef(), stun = new ClipRef(), death = new ClipRef(), run = new ClipRef();
            public float attackPeak01, walkMetresPerSecond, runThreshold, runMetresPerSecond, deathVisualSeconds;
            public float measuredWalk, reportWalk, measuredRun, bakeWalkSlip, bakeRunSlip;
            public bool loopStun, hadFeet, feetCalibrated;
            public LegCal[] legs = Array.Empty<LegCal>();
        }
        [Serializable] sealed class SceneRecord { public string scene = "", utc = "", backup = "", options = ""; public List<ActorRecord> actors = new List<ActorRecord>(); }

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            try
            {
                if (command == "status") return Status();
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Play mode (Quadruped308 edits assets/scenes in Edit mode only)";
                if (command == "audit") return Audit();
                if (command == "import") return Import(null);
                if (command.StartsWith("import:", StringComparison.Ordinal)) return Import(command.Substring(7).Trim());
                if (command == "apply-all" || command.StartsWith("apply-all|", StringComparison.Ordinal)) return All(true, command.Length > 9 ? command.Substring(10) : "");
                if (command == "revert-all") return All(false, "");
                if (command.StartsWith("apply:", StringComparison.Ordinal)) return Apply(command.Substring(6).Trim());
                if (command.StartsWith("revert:", StringComparison.Ordinal)) return Revert(command.Substring(7).Trim());
                if (command.StartsWith("probe-gait:", StringComparison.Ordinal)) return ProbeGait(command.Substring(11).Trim());
                return "REFUSED unknown command '" + command + "' (status | audit | import[:<species>] | apply:<296|298|main>[|Role=Extra,Run=Extra,RunThreshold=m/s,species=id] | apply-all[|...] | revert:<scene> | revert-all)";
            }
            catch (Exception e) { return "FAILED " + e; }
        }

        // ------------------------------------------------------------------ shared helpers
        static string ScenePath(string key)
        {
            switch (key)
            {
                case "296": case "architecture296": return Scene296;
                case "298": case "folklore298": return Scene298;
                case "main": return SceneMain;
                default: return Ledger.Contains(key) ? key : null;
            }
        }
        static string DirtyScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.isDirty) return s.path; }
            return null;
        }
        static string PathOf(Transform t) { var sb = new StringBuilder(t.name); for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/"); return sb.ToString(); }
        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static bool ProtectedTree(Transform t) { var root = t.root.name; return ProtectedRoots.Any(p => root.StartsWith(p, StringComparison.Ordinal)); }
        static string Sha(string file) { using (var sha = SHA256.Create()) using (var s = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        static Report ReadReport(string sp, out string why)
        {
            why = null; string file = ReportFile(sp);
            if (!File.Exists(file)) { why = "no bake report " + file; return null; }
            var report = JsonUtility.FromJson<Report>(File.ReadAllText(file));
            if (report?.unity?.roles == null || report.unity.species != sp) why = "unreadable report " + file;
            else if (report.status != "BAKED") why = "bake status " + report.status + " (not BAKED)";
            else if (!report.candidateUnchanged) why = "report says candidate.blend changed";
            else if (report.roundtrip == null || report.roundtrip.status != "TECHNICAL_ROUNDTRIP_PASS") why = "FBX roundtrip not PASS (" + report.roundtrip?.status + ")";
            else if (!File.Exists(SourceFbx(sp)) || report.outputs == null || report.outputs.fbxSha256 != Sha(SourceFbx(sp))) why = "baked FBX missing or not the one the report gated (outputs.fbxSha256)";
            return why == null ? report : null;
        }
        static AnimationClip ClipIn(string assetPath, string take) =>
            AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().SingleOrDefault(c => !c.name.StartsWith("__", StringComparison.Ordinal) && (c.name == take || c.name.EndsWith("|" + take, StringComparison.Ordinal)));
        static ClipRef Ref(AnimationClip clip) => clip == null ? new ClipRef() : new ClipRef { path = AssetDatabase.GetAssetPath(clip), name = clip.name };
        static AnimationClip Load(ClipRef r) => r == null || string.IsNullOrEmpty(r.path) ? null : AssetDatabase.LoadAllAssetsAtPath(r.path).OfType<AnimationClip>().FirstOrDefault(c => c.name == r.name);
        static Dictionary<string, Matrix4x4> BindPoses(SkinnedMeshRenderer smr, Transform root)
        {
            var d = new Dictionary<string, Matrix4x4>(); var bones = smr.bones; var binds = smr.sharedMesh.bindposes;
            for (int i = 0; i < bones.Length && i < binds.Length; i++)
                if (bones[i] != null) d[AnimationUtility.CalculateTransformPath(bones[i], root)] = binds[i];
            return d;
        }

        static Dictionary<string, Transform> Paths(Transform root)
        {
            var map = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t != root) map[AnimationUtility.CalculateTransformPath(t, root)] = t;
            return map;
        }

        // ------------------------------------------------------------------ status / audit
        static string Status()
        {
            var sb = new StringBuilder("Quadruped308 status\n");
            foreach (var sp in Species)
            {
                var report = File.Exists(ReportFile(sp)) ? JsonUtility.FromJson<Report>(File.ReadAllText(ReportFile(sp))) : null;
                sb.Append("  " + sp + ": bake " + (report == null ? "none" : report.status + " roundtrip " + report.roundtrip?.status));
                if (report?.unity?.roles != null) sb.Append(" | PASS " + string.Join(",", report.unity.roles.Where(r => r.pass).Select(r => r.role)) + " | FAIL " + string.Join(",", report.unity.roles.Where(r => !r.pass).Select(r => r.role)) + " | walk " + F(report.unity.walkMetresPerSecondUnity) + " m/s peak " + F(report.unity.attackPeak01));
                sb.AppendLine(" | asset " + (AssetDatabase.LoadAssetAtPath<GameObject>(AssetFbx(sp)) != null ? AssetFbx(sp) : "not imported"));
            }
            foreach (var scene in Ledger) sb.AppendLine("  " + scene + ": " + (File.Exists(RecordFile(scene)) ? "APPLIED (record " + RecordFile(scene) + ")" : "not applied"));
            return sb.ToString().TrimEnd();
        }

        static string Audit()
        {
            string manifest = Path.Combine(Out, "Packages", "extract-manifest.json");
            if (!File.Exists(manifest)) return "REFUSED no extract manifest " + manifest;
            var rows = JsonUtility.FromJson<ManifestRows>("{\"rows\":" + File.ReadAllText(manifest) + "}").rows;
            var bySize = rows.GroupBy(r => r.bytes).ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(r => r.sha256)));
            var hits = new List<string>(); int scanned = 0;
            foreach (var file in Directory.EnumerateFiles(Application.dataPath, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                long size = new FileInfo(file).Length;
                if (!bySize.TryGetValue(size, out var shas)) continue;
                scanned++;
                if (shas.Contains(Sha(file))) hits.Add(file);
            }
            return hits.Count == 0 ? "AUDIT PASS (AC-Q7): 0 raw pack files under Assets (" + rows.Length + " manifest files, " + scanned + " same-size candidates hashed)"
                : "REFUSED AC-Q7: raw Asset Store pack files under Assets:\n  " + string.Join("\n  ", hits);
        }

        // ------------------------------------------------------------------ import
        static string Import(string only)
        {
            if (only != null && !Species.Contains(only)) return "REFUSED unknown species " + only;
            string audit = Audit(); if (!audit.StartsWith("AUDIT PASS", StringComparison.Ordinal)) return audit;
            var sb = new StringBuilder(audit + "\n");
            foreach (var sp in Species.Where(s => only == null || s == only)) sb.AppendLine(ImportSpecies(sp));
            return sb.ToString().TrimEnd();
        }

        static string ImportSpecies(string sp)
        {
            var report = ReadReport(sp, out string why); if (report == null) return "REFUSED " + sp + ": " + why;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CompactFolklore298.PrefabPath(sp));
            var avatar = prefab != null ? prefab.GetComponentInChildren<Animator>(true)?.avatar : null;
            if (avatar == null || !avatar.isValid) return "REFUSED " + sp + ": Folklore298 prefab/avatar missing (" + CompactFolklore298.PrefabPath(sp) + ")";
            string modelPath = AssetDatabase.GetAssetPath(avatar);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) return "REFUSED " + sp + ": model asset for avatar not found";
            string source = SourceFbx(sp), asset = AssetFbx(sp), file = ProjectFile(asset);
            if (!File.Exists(source)) return "REFUSED " + sp + ": baked FBX missing " + source;
            Folder(AssetDir + "/" + sp);
            if (!File.Exists(file) || Sha(file) != Sha(source)) File.Copy(source, file, true);
            AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(asset) as ModelImporter;
            if (importer == null) return "FAILED " + sp + ": not a model importer at " + asset;
            importer.isReadable = true; importer.importAnimation = true; importer.importCameras = false; importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; importer.sourceAvatar = avatar;
            importer.motionNodeName = "Root";
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            var takes = importer.defaultClipAnimations;
            foreach (var take in takes)
            {
                var row = report.unity.roles.FirstOrDefault(r => take.name == r.clip || take.name.EndsWith("|" + r.clip, StringComparison.Ordinal));
                take.loopTime = row != null && row.loop;
                take.lockRootHeightY = true; take.lockRootPositionXZ = true; take.lockRootRotation = true;
                take.keepOriginalOrientation = true; take.keepOriginalPositionXZ = true; take.keepOriginalPositionY = true;
                take.events = Array.Empty<AnimationEvent>();
            }
            importer.clipAnimations = takes; importer.SaveAndReimport();

            // checks: curve paths resolve on the model, rest frames match, PASS clips present with the baked length
            var notes = new List<string>(); var errors = new List<string>();
            var imported = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
            var want = Paths(model.transform); var have = imported != null ? Paths(imported.transform) : new Dictionary<string, Transform>();
            foreach (var pair in want.Where(p => p.Key.StartsWith(report.unity.rigObject, StringComparison.Ordinal)))
                if (!have.TryGetValue(pair.Key, out _)) errors.Add("missing bone " + pair.Key);
            // Rest frames are compared on the skin bind poses, not on the model transforms: Unity poses an FBX that carries
            // animation at frame 0 of its first take, so the RT308 model transforms are an animation pose by design.
            int bindCompared = 0;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var other = imported != null ? imported.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name == smr.name) : null;
                if (other == null || smr.sharedMesh == null || other.sharedMesh == null) { errors.Add("missing skinned mesh " + smr.name); continue; }
                var wantBind = BindPoses(smr, model.transform); var haveBind = BindPoses(other, imported.transform);
                foreach (var bind in wantBind.Where(b => b.Key.StartsWith(report.unity.rigObject, StringComparison.Ordinal)))
                {
                    if (!haveBind.TryGetValue(bind.Key, out var m)) { errors.Add("bone not in " + other.name + " skin: " + bind.Key); continue; }
                    float d = 0; for (int i = 0; i < 16; i++) d = Mathf.Max(d, Mathf.Abs(m[i] - bind.Value[i]));
                    if (d > 1e-4f) errors.Add("rest frame (bind pose) differs " + bind.Key + " max element " + d);
                    bindCompared++;
                }
            }
            if (bindCompared == 0) errors.Add("no bind poses compared");
            else notes.Add("rest frames: " + bindCompared + " bind poses equal");
            var clipNames = new List<string>();
            foreach (var row in report.unity.roles.Where(r => r.pass))
            {
                var clip = ClipIn(asset, row.clip);
                if (clip == null || clip.legacy || clip.length <= 0) { errors.Add("missing clip " + row.clip); continue; }
                if (Mathf.Abs(clip.length - row.seconds) > 1f / 30 + .002f) errors.Add(row.clip + " length " + F(clip.length) + " != baked " + F(row.seconds));
                if (clip.isLooping != row.loop && row.loop) notes.Add(row.clip + " loopTime set but clip reports isLooping=" + clip.isLooping);
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    if (!string.IsNullOrEmpty(binding.path) && !want.ContainsKey(binding.path)) { errors.Add(row.clip + " curve path not on model: " + binding.path); break; }
                clipNames.Add(clip.name + " " + F(clip.length) + "s" + (row.extra ? " (extra)" : ""));
            }
            foreach (var row in report.unity.roles.Where(r => !r.pass)) notes.Add(row.role + " gate FAIL in bake: procedural clip stays");
            var receipt = new ImportRow { species = sp, asset = asset, model = modelPath, utc = DateTime.UtcNow.ToString("O"), fbxSha256 = Sha(file), clips = clipNames.ToArray(), notes = notes.Concat(errors).ToArray(), passed = errors.Count == 0 };
            Directory.CreateDirectory(Path.GetDirectoryName(ImportReceipt(sp))); File.WriteAllText(ImportReceipt(sp), JsonUtility.ToJson(receipt, true));
            return (errors.Count == 0 ? "IMPORTED " : "REFUSED ") + sp + ": " + asset + " (avatar from " + modelPath + "), clips " + string.Join(", ", clipNames)
                + (errors.Count > 0 ? "\n    errors: " + string.Join("; ", errors.Take(12)) : "") + (notes.Count > 0 ? "\n    notes: " + string.Join("; ", notes) : "");
        }

        // ------------------------------------------------------------------ apply / revert
        sealed class Plan
        {
            public string species; public Report report;
            public readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
            public readonly Dictionary<string, RoleRow> rows = new Dictionary<string, RoleRow>();
            public AnimationClip run; public RoleRow runRow; public float runThreshold;
            public float walkSlip = float.NaN, runSlip = float.NaN;
            public readonly List<string> notes = new List<string>();
        }

        static Dictionary<string, string> Options(string text)
        {
            var map = new Dictionary<string, string>();
            foreach (var part in (text ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('='); if (eq <= 0) throw new ArgumentException("option needs key=value: " + part);
                map[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim();
            }
            foreach (var key in map.Keys)
            {
                int dot = key.IndexOf('.'); string bare = dot > 0 ? key.Substring(dot + 1) : key;
                if (dot > 0 && !Species.Contains(key.Substring(0, dot))) throw new ArgumentException("unknown species prefix in " + key);
                if (!Roles.Contains(bare) && bare != "Run" && bare != "RunThreshold" && (bare != "species" || dot > 0)) throw new ArgumentException("unknown option " + key);
            }
            if (map.TryGetValue("species", out var only) && !Species.Contains(only)) throw new ArgumentException("unknown species=" + only + " (" + string.Join(" | ", Species) + ")");
            return map;
        }

        // "<species>.<key>" wins over a bare "<key>" so one apply can carry per-species choices.
        static bool Option(Dictionary<string, string> options, string sp, string key, out string value) =>
            options.TryGetValue(sp + "." + key, out value) || options.TryGetValue(key, out value);

        static Plan MakePlan(string sp, Dictionary<string, string> options, out string why)
        {
            var report = ReadReport(sp, out why); if (report == null) return null;
            string asset = AssetFbx(sp);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(asset) == null) { why = "run import:" + sp + " first"; return null; }
            var receipt = File.Exists(ImportReceipt(sp)) ? JsonUtility.FromJson<ImportRow>(File.ReadAllText(ImportReceipt(sp))) : null;
            if (receipt == null || !receipt.passed) { why = "import receipt missing or not passed for " + sp; return null; }
            // A re-bake after the import would pair the new report (peaks, speeds, PASS list) with the old clips.
            if (!File.Exists(SourceFbx(sp))) { why = "baked FBX missing " + SourceFbx(sp); return null; }
            string bakedSha = Sha(SourceFbx(sp)), assetSha = Sha(ProjectFile(asset));
            if (receipt.fbxSha256 != assetSha || assetSha != bakedSha) { why = "imported FBX is stale or edited (receipt/asset/bake SHA differ) - run import:" + sp + " again"; return null; }
            var plan = new Plan { species = sp, report = report };
            foreach (var role in Roles)
            {
                string source = Option(options, sp, role, out var extra) ? extra : role;
                var row = report.unity.roles.FirstOrDefault(r => r.role == source);
                if (row == null) { why = sp + " has no baked role " + source; return null; }
                if (!row.pass) { plan.notes.Add(role + (source != role ? "=" + source : "") + " gate FAIL: procedural clip kept"); continue; }
                if (role == "Walk")
                {
                    plan.walkSlip = BakeSlip(sp, source);
                    if (!(plan.walkSlip <= MaxStanceSlip))
                    { plan.notes.Add("Walk" + (source != role ? "=" + source : "") + " stance-slip gate FAIL (bake predictor " + (float.IsNaN(plan.walkSlip) ? "not measured" : F(plan.walkSlip)) + " > " + F(MaxStanceSlip) + "): procedural clip kept"); continue; }
                }
                var clip = ClipIn(asset, row.clip); if (clip == null) { why = "clip " + row.clip + " missing in " + asset; return null; }
                plan.clips[role] = clip; plan.rows[role] = row;
                if (source != role) plan.notes.Add(role + " uses extra " + source);
            }
            if (Option(options, sp, "Run", out var runName))
            {
                plan.runRow = report.unity.roles.FirstOrDefault(r => r.role == runName && r.pass);
                if (plan.runRow == null) { why = "Run source " + runName + " is not a PASS role of " + sp; return null; }
                plan.runSlip = BakeSlip(sp, runName);
                if (!(plan.runSlip <= MaxStanceSlip)) { why = "Run source " + runName + " fails the stance-slip gate (bake predictor " + (float.IsNaN(plan.runSlip) ? "not measured" : F(plan.runSlip)) + " > " + F(MaxStanceSlip) + ")"; return null; }
                if (!Option(options, sp, "RunThreshold", out var threshold) || !float.TryParse(threshold, NumberStyles.Float, CultureInfo.InvariantCulture, out plan.runThreshold) || plan.runThreshold <= 0)
                { why = "Run needs RunThreshold=<m/s> > 0"; return null; }
                plan.run = ClipIn(asset, plan.runRow.clip);
                if (plan.run == null) { why = "clip " + plan.runRow.clip + " missing in " + asset; return null; }
            }
            return plan;
        }

        // Read-only diagnostic: opens the scene (refuses a dirty one), samples each target's PASS walk clip on the actual actor and prints
        // the axes and per-foot stance numbers; the scene is re-opened afterwards so nothing is kept or saved.
        static string ProbeGait(string key)
        {
            string path = ScenePath(key); if (path == null) return "REFUSED unknown scene " + key;
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) return "REFUSED dirty scene " + SceneManager.GetSceneAt(i).path;
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single); var sb = new StringBuilder("probe-gait " + path + "\n");
            try
            {
                foreach (var (rig, sp) in Targets(scene).ToArray())
                {
                    var clip = ClipIn(AssetFbx(sp), "RT308_Walk") ?? ClipIn(AssetFbx(sp), "RT308_Pace"); if (clip == null) { sb.AppendLine(sp + ": no RT308 walk/pace clip"); continue; }
                    var feet = FeetOf(rig); Transform rt = rig.transform, an = rig.Animator.transform;
                    sb.AppendLine(sp + " rig " + rig.name + " culling " + rig.Animator.cullingMode + " rot " + rt.rotation.eulerAngles + " scale " + rt.lossyScale + " | animator " + an.name + " rot " + an.rotation.eulerAngles + " scale " + an.lossyScale + " | clip " + clip.name + " " + F(clip.length) + "s feet " + feet.Length);
                    int n = Mathf.Max(16, Mathf.CeilToInt(clip.length * 60f));
                    var pos = new Vector3[feet.Length, n + 1];
                    for (int i = 0; i <= n; i++) { Sample(rig, clip, clip.length * i / n); for (int f = 0; f < feet.Length; f++) pos[f, i] = feet[f].position; }
                    for (int f = 0; f < feet.Length; f++)
                    {
                        Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
                        for (int i = 0; i <= n; i++) { var d = pos[f, i] - rt.position; var v = new Vector3(Vector3.Dot(d, rt.right), Vector3.Dot(d, rt.up), Vector3.Dot(d, rt.forward)); lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
                        var w = new Vector3[n + 1]; for (int i = 0; i <= n; i++) w[i] = pos[f, i] - rt.position;
                        Vector3 wlo = Vector3.positiveInfinity, whi = Vector3.negativeInfinity; foreach (var v in w) { wlo = Vector3.Min(wlo, v); whi = Vector3.Max(whi, v); }
                        sb.AppendLine("  " + feet[f].name + " rig-frame min " + lo.ToString("F3") + " max " + hi.ToString("F3") + " | world-delta min " + wlo.ToString("F3") + " max " + whi.ToString("F3"));
                    }
                    sb.AppendLine("  MeasureGait " + F(MeasureGait(rig, clip, out string detail)) + " (" + detail + ")");
                }
            }
            finally { EditorSceneManager.OpenScene(path, OpenSceneMode.Single); }
            return sb.ToString().TrimEnd();
        }

        static IEnumerable<(EnemyRigMotion298 rig, string species)> Targets(Scene scene)
        {
            foreach (var rig in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<EnemyRigMotion298>(true)))
            {
                if (rig == null || rig.Animator == null || ProtectedTree(rig.transform)) continue;
                foreach (var sp in Species) if (rig.Animator.transform.Find(sp + "_Rig298") != null) yield return (rig, sp);
            }
        }

        static Transform[] FeetOf(EnemyRigMotion298 rig)
        {
            if (rig.FootPlacement != null && rig.FootPlacement.Legs != null && rig.FootPlacement.Legs.Length == 4 && rig.FootPlacement.Legs.All(l => l?.Foot != null))
                return rig.FootPlacement.Legs.Select(l => l.Foot).ToArray();
            var bones = rig.Animator.GetComponentsInChildren<Transform>(true);
            return Legs.Select(n => bones.FirstOrDefault(b => b.name == n + "_Foot")).Where(b => b != null).ToArray();
        }

        // Stance-foot speed of a gait clip on the actual actor: planted ankles (within 5 mm of their lowest height) move backward
        // (against the actor's forward) at the speed the actor must travel for zero slide. Same quantity the bake reports (AC-Q3).
        // World metres along the actor's up/forward axes, so an actor-root scale cannot change the m/s value.
        // The #307 actors use AnimatorCullingMode.CullUpdateTransforms, under which an edit-mode graph evaluation of an off-screen actor
        // writes nothing; sampling therefore runs with AlwaysAnimate and restores the scene's mode (the pose stays in the transforms).
        static void Sample(EnemyRigMotion298 rig, AnimationClip clip, float time)
        {
            var animator = rig.Animator; var keep = animator.cullingMode; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            try { CompactFolklore298.SamplePoseGraph298(animator, clip, time); } finally { animator.cullingMode = keep; }
        }

        static float MeasureGait(EnemyRigMotion298 rig, AnimationClip clip, out string detail)
        {
            var feet = FeetOf(rig); detail = "";
            if (feet.Length == 0) { detail = "no foot bones"; return 0; }
            int n = Mathf.Max(16, Mathf.CeilToInt(clip.length * 60f)); float dt = clip.length / n;
            var local = new Vector3[feet.Length, n + 1];
            Vector3 origin = rig.transform.position, up = rig.transform.up, forward = rig.transform.forward;
            for (int i = 0; i <= n; i++)
            {
                Sample(rig, clip, clip.length * i / n);
                for (int f = 0; f < feet.Length; f++) { var d = feet[f].position - origin; local[f, i] = new Vector3(0, Vector3.Dot(d, up), Vector3.Dot(d, forward)); }
            }
            double sum = 0; int count = 0;
            for (int f = 0; f < feet.Length; f++)
            {
                float low = float.PositiveInfinity; for (int i = 0; i <= n; i++) low = Mathf.Min(low, local[f, i].y);
                for (int i = 1; i < n; i++)
                    if (local[f, i - 1].y <= low + .005f && local[f, i].y <= low + .005f && local[f, i + 1].y <= low + .005f)
                    { sum += -(local[f, i + 1].z - local[f, i - 1].z) / (2 * dt); count++; }
            }
            detail = count + " planted samples / " + feet.Length + " feet";
            return count > 0 ? (float)(sum / count) : 0;
        }

        // LastStatus is not serialized (reads NOT_CONFIGURED after a scene load); the serialized Configured flag and sole data are compared instead.
        static string FootState(EnemyFootPlacement298 feet) => feet == null ? "" : (feet.Configured ? "CALIBRATED" : "NOT_CALIBRATED") + ": " + string.Join(" ", (feet.Legs ?? Array.Empty<EnemyFootPlacement298.LegBinding>()).Where(l => l != null).Select(l => l.Name + "(" + F(l.SoleLocal.x) + "," + F(l.SoleLocal.y) + "," + F(l.SoleLocal.z) + " y" + F(l.RestSoleActorY) + ")"));

        static LegCal[] CaptureLegs(EnemyFootPlacement298 feet) => feet == null || feet.Legs == null ? Array.Empty<LegCal>() : feet.Legs.Where(l => l != null).Select(l => new LegCal
        {
            name = l.Name, calibrationSource = l.CalibrationSource ?? "", soleLocal = l.SoleLocal, soleUpLocal = l.SoleUpLocal, bendActorLocal = l.BendActorLocal,
            restSoleActorY = l.RestSoleActorY, upperModelLength = l.UpperModelLength, lowerModelLength = l.LowerModelLength, weightedSurfaceVertices = l.WeightedSurfaceVertices
        }).ToArray();

        // Revert: bone pose as CompactFolklore298.BindMotion leaves it (old Idle at t=0), then the recorded calibration written back exactly.
        static void RestoreFeet(EnemyRigMotion298 rig, ActorRecord a)
        {
            Sample(rig, rig.Idle, 0);
            var feet = rig.FootPlacement; if (feet == null || !a.hadFeet) return;
            foreach (var cal in a.legs ?? Array.Empty<LegCal>())
            {
                var leg = feet.Legs?.FirstOrDefault(l => l != null && l.Name == cal.name) ?? throw new InvalidOperationException("foot leg " + cal.name + " missing on " + a.path);
                leg.SoleLocal = cal.soleLocal; leg.SoleUpLocal = cal.soleUpLocal; leg.BendActorLocal = cal.bendActorLocal; leg.RestSoleActorY = cal.restSoleActorY;
                leg.UpperModelLength = cal.upperModelLength; leg.LowerModelLength = cal.lowerModelLength; leg.WeightedSurfaceVertices = cal.weightedSurfaceVertices;
                leg.CalibrationSource = cal.calibrationSource;
            }
            var so = new SerializedObject(feet); var flag = so.FindProperty("calibrated");
            if (flag == null) throw new InvalidOperationException("EnemyFootPlacement298.calibrated not found");
            flag.boolValue = a.feetCalibrated; so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feet);
        }

        // Baked stance-phase slip predictor of a report role (roles.<role>.attempts[last].walk.maxStancePhaseSlipRatio); NaN = not measured.
        static float BakeSlip(string sp, string role)
        {
            var root = JObject.Parse(File.ReadAllText(ReportFile(sp)));
            var last = (root["roles"]?[role]?["attempts"] as JArray)?.LastOrDefault();
            var value = last?["walk"]?["maxStancePhaseSlipRatio"];
            return value == null || value.Type == JTokenType.Null ? float.NaN : value.Value<float>();
        }

        static void Recalibrate(EnemyRigMotion298 rig)
        {
            Sample(rig, rig.Idle, 0);
            var feet = rig.FootPlacement; if (feet == null) return;
            if (feet.Legs == null || feet.Legs.Length != 4 || feet.Legs.Any(l => l == null)) throw new InvalidOperationException("EnemyFootPlacement298 has no four-leg binding to re-calibrate");
            var chains = feet.Legs.Select(l => new EnemyFootPlacement298.LegBinding { Name = l.Name, Upper = l.Upper, Lower = l.Lower, Foot = l.Foot, HasSoleOverride = l.HasSoleOverride, SoleLocalOverride = l.SoleLocalOverride, SoleNormalLocalOverride = l.SoleNormalLocalOverride }).ToArray();
            feet.Configure(rig.transform, feet.ModelRoot, feet.BodyRoot, feet.Skins, chains, feet.GroundMask);
            if (!feet.Configured) throw new InvalidOperationException("four-leg foot calibration failed: " + feet.LastStatus);
            EditorUtility.SetDirty(feet);
        }

        static string All(bool apply, string options)
        {
            var lines = new List<string>();
            foreach (var scene in apply ? Ledger : Ledger.Reverse())
            {
                if (!apply && !File.Exists(RecordFile(scene))) { lines.Add(scene + ": no record (skipped)"); continue; }
                string r = apply ? Apply(scene + (string.IsNullOrEmpty(options) ? "" : "|" + options)) : Revert(scene);
                lines.Add(r);
                if (r.StartsWith("REFUSED", StringComparison.Ordinal) || r.StartsWith("FAILED", StringComparison.Ordinal)) { lines.Add("stopped at " + scene); break; }
            }
            return string.Join("\n", lines);
        }

        static string Apply(string argument)
        {
            int bar = argument.IndexOf('|');
            string key = bar < 0 ? argument : argument.Substring(0, bar), optionText = bar < 0 ? "" : argument.Substring(bar + 1);
            string path = ScenePath(key); if (path == null) return "REFUSED scene not in the #308 ledger: " + key + " (296 | 298 | main)";
            Dictionary<string, string> options;
            try { options = Options(optionText); } catch (ArgumentException e) { return "REFUSED " + e.Message; }
            string record = RecordFile(path);
            if (File.Exists(record)) return "ALREADY APPLIED " + path + " (record " + record + "); revert:" + key + " first to re-apply";
            string dirty = DirtyScene(); if (dirty != null) return "REFUSED dirty scene " + dirty + " (save or discard it first)";
            var plans = new Dictionary<string, Plan>();
            foreach (var sp in Species.Where(s => !options.TryGetValue("species", out var only) || only == s))
            {
                var plan = MakePlan(sp, options, out string why); if (plan == null) return "REFUSED " + sp + ": " + why;
                plans[sp] = plan;
            }
            var scene = SceneManager.GetActiveScene().path == path ? SceneManager.GetActiveScene() : EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var targets = Targets(scene).Where(t => plans.ContainsKey(t.species)).ToList();
            if (targets.Count == 0) return "NO TARGETS " + path + ": no " + string.Join("/", plans.Keys) + " Rig298 actor outside protected trees (scene unchanged, nothing saved)";
            var sb = new StringBuilder(path + " (apply" + (optionText.Length > 0 ? " " + optionText : "") + ")\n");
            var rec = new SceneRecord { scene = path, utc = DateTime.UtcNow.ToString("O"), options = optionText };
            bool recordWritten = false, saved = false;
            try
            {
                foreach (var (rig, sp) in targets)
                {
                    var plan = plans[sp]; var actor = rig.GetComponent<PrologueEncounter>();
                    var a = new ActorRecord { path = PathOf(rig.transform), species = sp, idle = Ref(rig.Idle), walk = Ref(rig.Walk), attack = Ref(rig.Attack), hit = Ref(rig.Hit), stun = Ref(rig.Stun), death = Ref(rig.Death), run = Ref(rig.Run),
                        attackPeak01 = rig.AttackPeak01, walkMetresPerSecond = rig.WalkMetresPerSecond, runThreshold = rig.RunThresholdMetresPerSecond, runMetresPerSecond = rig.RunMetresPerSecond,
                        loopStun = rig.LoopStun, deathVisualSeconds = actor != null ? actor.DeathVisualSeconds : 0, hadFeet = rig.FootPlacement != null, footBefore = FootState(rig.FootPlacement),
                        feetCalibrated = rig.FootPlacement != null && rig.FootPlacement.Configured, legs = CaptureLegs(rig.FootPlacement), bakeWalkSlip = float.IsNaN(plan.walkSlip) ? -1 : plan.walkSlip, bakeRunSlip = float.IsNaN(plan.runSlip) ? -1 : plan.runSlip };   // -1 = not used (JsonUtility has no NaN)
                    var changed = new List<string>();
                    foreach (var role in plan.clips.Keys)
                    {
                        var clip = plan.clips[role];
                        switch (role)
                        {
                            case "Idle": if (rig.Idle != clip) { rig.Idle = clip; changed.Add(role); } break;
                            case "Walk": if (rig.Walk != clip) { rig.Walk = clip; changed.Add(role); } break;
                            case "Attack": if (rig.Attack != clip) { rig.Attack = clip; rig.AttackPeak01 = Mathf.Clamp(plan.rows[role].peak01, .05f, .95f); changed.Add(role); } break;
                            case "Hit": if (rig.Hit != clip) { rig.Hit = clip; changed.Add(role); } break;
                            case "Stun": if (rig.Stun != clip) { rig.Stun = clip; rig.LoopStun = plan.rows[role].loop; changed.Add(role); } break;
                            case "Death": if (rig.Death != clip) { rig.Death = clip; changed.Add(role); } break;
                        }
                    }
                    if (plan.run != null && rig.Run != plan.run) { rig.Run = plan.run; rig.RunThresholdMetresPerSecond = plan.runThreshold; changed.Add("Run"); }
                    if (changed.Count == 0) { sb.AppendLine("  " + a.path + ": already on RT308 clips (unchanged)"); continue; }
                    if (changed.Contains("Walk"))
                    {
                        a.measuredWalk = MeasureGait(rig, rig.Walk, out string detail); a.reportWalk = plan.rows["Walk"].speedUnity > 0 ? plan.rows["Walk"].speedUnity : plan.report.unity.walkMetresPerSecondUnity;
                        if (a.measuredWalk < .1f) throw new InvalidOperationException(a.path + " walk speed not measurable (" + detail + ")");
                        rig.WalkMetresPerSecond = a.measuredWalk;
                        float gap = a.reportWalk > 0 ? Mathf.Abs(a.measuredWalk - a.reportWalk) / a.reportWalk : 0;
                        sb.AppendLine("  " + a.path + ": WalkMetresPerSecond " + F(a.walkMetresPerSecond) + " -> " + F(a.measuredWalk) + " (measured, " + detail + "; bake " + F(a.reportWalk) + (gap > WalkCrossCheck ? ", WARN differs " + F(gap * 100) + "%" : ", within 10%") + ")");
                    }
                    if (changed.Contains("Run")) { a.measuredRun = MeasureGait(rig, rig.Run, out string detail); rig.RunMetresPerSecond = Mathf.Max(.1f, a.measuredRun); sb.AppendLine("  Run " + F(rig.RunMetresPerSecond) + " m/s above " + F(rig.RunThresholdMetresPerSecond) + " (" + detail + ")"); }
                    if (changed.Contains("Death") && actor != null) { actor.DeathVisualSeconds = Mathf.Max(actor.DeathVisualSeconds, rig.Death.length + .1f); EditorUtility.SetDirty(actor); }
                    Recalibrate(rig);
                    if (!rig.IsConfigured) throw new InvalidOperationException(a.path + " EnemyRigMotion298 not configured after apply");
                    a.changed = changed.ToArray(); a.footAfter = FootState(rig.FootPlacement);
                    EditorUtility.SetDirty(rig); rec.actors.Add(a);
                    sb.AppendLine("  " + a.path + " [" + sp + "]: " + string.Join(",", changed) + (changed.Contains("Attack") ? " AttackPeak01 " + F(rig.AttackPeak01) : "") + "; feet " + (rig.FootPlacement != null ? rig.FootPlacement.LastStatus : "none"));
                }
                foreach (var plan in plans.Values) foreach (var note in plan.notes) sb.AppendLine("  note " + plan.species + ": " + note);
                if (rec.actors.Count == 0) return sb.Append("  nothing changed (not saved)").ToString();
                string backups = Path.Combine(Out, "Backups"); Directory.CreateDirectory(backups);
                rec.backup = Path.Combine(backups, Path.GetFileNameWithoutExtension(path) + "-pre308quad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
                File.Copy(ProjectFile(path), rec.backup, true);
                // Record first: a saved scene without a record could neither be reverted nor refused as ALREADY APPLIED.
                Directory.CreateDirectory(Path.GetDirectoryName(record)); File.WriteAllText(record, JsonUtility.ToJson(rec, true)); recordWritten = true;
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("SaveScene returned false");
                saved = true;
                return sb.Append("  saved; record " + record + "; scene backup " + rec.backup).ToString();
            }
            catch (Exception e)
            {
                if (recordWritten && !saved && File.Exists(record)) File.Delete(record);
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);   // discard this command's partial edits
                return "FAILED " + path + " (partial edits discarded, nothing saved): " + e.Message;
            }
        }

        static string Revert(string key)
        {
            string path = ScenePath(key); if (path == null) return "REFUSED scene not in the #308 ledger: " + key;
            string record = RecordFile(path); if (!File.Exists(record)) return "REFUSED no apply record " + record;
            string dirty = DirtyScene(); if (dirty != null) return "REFUSED dirty scene " + dirty;
            var rec = JsonUtility.FromJson<SceneRecord>(File.ReadAllText(record));
            var scene = SceneManager.GetActiveScene().path == path ? SceneManager.GetActiveScene() : EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var rigs = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<EnemyRigMotion298>(true)).GroupBy(r => PathOf(r.transform)).ToDictionary(g => g.Key, g => g.First());
            var sb = new StringBuilder(path + " (revert)\n");
            try
            {
                foreach (var a in rec.actors)
                {
                    if (!rigs.TryGetValue(a.path, out var rig)) { sb.AppendLine("  missing " + a.path + " (skipped)"); continue; }
                    rig.Idle = Load(a.idle); rig.Walk = Load(a.walk); rig.Attack = Load(a.attack); rig.Hit = Load(a.hit); rig.Stun = Load(a.stun); rig.Death = Load(a.death); rig.Run = Load(a.run);
                    rig.AttackPeak01 = a.attackPeak01; rig.WalkMetresPerSecond = a.walkMetresPerSecond; rig.RunThresholdMetresPerSecond = a.runThreshold; rig.RunMetresPerSecond = a.runMetresPerSecond; rig.LoopStun = a.loopStun;
                    var actor = rig.GetComponent<PrologueEncounter>(); if (actor != null) { actor.DeathVisualSeconds = a.deathVisualSeconds; EditorUtility.SetDirty(actor); }
                    if (!rig.IsConfigured) throw new InvalidOperationException(a.path + " recorded clips no longer load");
                    RestoreFeet(rig, a); EditorUtility.SetDirty(rig);
                    string feetNow = FootState(rig.FootPlacement);
                    sb.AppendLine("  " + a.path + ": restored " + string.Join(",", a.changed) + "; feet " + feetNow + (feetNow == a.footBefore ? " (= before)" : " (WARN before: " + a.footBefore + ")"));
                }
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("SaveScene returned false");
                File.Move(record, record.Replace(".json", "-reverted-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"));
                return sb.Append("  saved; scene backup kept at " + rec.backup).ToString();
            }
            catch (Exception e)
            {
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                return "FAILED " + path + " (revert edits discarded, record kept): " + e.Message;
            }
        }
    }
}
