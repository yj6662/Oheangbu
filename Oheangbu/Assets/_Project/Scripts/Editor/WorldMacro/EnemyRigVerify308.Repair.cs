using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Newtonsoft.Json.Linq;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 SPEC-ENEMY-RIG-VERIFY-308 R1-R4 as ledgered commands: every change is planned (dry), recorded, backed up and revertible.
    public static partial class EnemyRigVerify308
    {
        static string DeathRecord => Path.Combine(Out, "Records", "death_firstframe.json");
        static string ManifestRecord => Path.Combine(Out, "Records", "manifest_rows.json");
        static string ManifestFile => Path.Combine(CompactFolklore298.OutputRoot, "import-manifest.json");
        static string Stamp => DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ repaired walk FBX (R4)
        static string CheckClip(Monster m, out bool ok)
        {
            ok = false; var errors = new List<string>(); var notes = new List<string>();
            if (string.IsNullOrEmpty(m.walkFixed)) return m.id + ": no walkFixed in the config";
            string file = ProjectFile(m.walkFixed);
            if (!File.Exists(file)) return m.id + ": " + m.walkFixed + " is not in Assets (copy the stage, then one Refresh)";
            string sha = Sha(file);
            if (!string.IsNullOrEmpty(m.walkFixedSha256) && sha != m.walkFixedSha256) errors.Add("file is not the gated one (sha " + sha.Substring(0, 12) + " != config " + m.walkFixedSha256.Substring(0, Math.Min(12, m.walkFixedSha256.Length)) + ")");
            var importer = AssetImporter.GetAtPath(m.walkFixed) as ModelImporter;
            if (importer == null) return m.id + ": no model importer at " + m.walkFixed + " (Refresh first)";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CompactFolklore298.PrefabPath(m.id)); var avatar = prefab != null ? prefab.GetComponentInChildren<Animator>(true)?.avatar : null;
            if (avatar == null) errors.Add("prefab avatar missing");
            if (importer.animationType != ModelImporterAnimationType.Human) errors.Add("animationType " + importer.animationType + " (want Human)");
            if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther) errors.Add("avatarSetup " + importer.avatarSetup + " (want CopyFromOther)");
            if (avatar != null && importer.sourceAvatar != avatar) errors.Add("sourceAvatar is not the avatar of " + CompactFolklore298.PrefabPath(m.id));
            // rev 2 (review F3): sourceAvatar only names the avatar. The reference rows a clip is converted with are the clip file's own copy
            // (its .meta): after candidate A is applied / reverted, or after a stage brought a clip file, a copy can be the other pose's.
            // Every clip file of the monster that copies the avatar is checked here (the run clip too, when it is imported).
            foreach (string stale in StaleClipRows(m, (ReadConfig(out _) ?? new Config()).avatar.rowTolerance, out _)) errors.Add("clip rows STALE: " + stale + " (avatar-sync:" + m.id + ")");
            var entry = importer.clipAnimations.FirstOrDefault(c => c.name == m.walkFixedTake || c.takeName == m.walkFixedTake);
            if (entry == null) errors.Add("clipAnimations has no take " + m.walkFixedTake + " (has " + string.Join(", ", importer.clipAnimations.Select(c => c.name)) + ")");
            else if (!(entry.loopTime && entry.lockRootHeightY && entry.lockRootPositionXZ && entry.lockRootRotation && entry.keepOriginalOrientation && entry.keepOriginalPositionXZ && entry.keepOriginalPositionY))
                errors.Add("clip flags differ from the source walk (want loopTime + bake-into-pose x3 + keep-original x3)");
            var clip = FixedClip(m); var source = OnlyClip(m.walkSource);
            if (clip == null) errors.Add("clip " + m.walkFixedTake + " not imported");
            else
            {
                if (clip.legacy || clip.length <= 0) errors.Add("clip is legacy or empty");
                if (!clip.isHumanMotion) errors.Add("clip is not a humanoid clip");
                if (clip.name.Contains("walking_man")) errors.Add("clip name contains walking_man (the automatic #302 relax would stay on)");
                if (source != null && Mathf.Abs(clip.length - source.length) > .5f / Mathf.Max(1f, clip.frameRate)) errors.Add("length " + F(clip.length) + " != source walk " + F(source.length));
                notes.Add(clip.name + " " + F(clip.length) + " s @" + F(clip.frameRate) + " fps loop " + clip.isLooping);
            }
            ok = errors.Count == 0;
            return m.id + ": " + (ok ? "ok" : "REFUSED") + " " + m.walkFixed + " sha " + sha.Substring(0, 12) + (notes.Count > 0 ? " | " + string.Join("; ", notes) : "") + (errors.Count > 0 ? "\n    " + string.Join("\n    ", errors) : "");
        }

        static string ClipCheck(Config cfg, string only)
        {
            var monsters = Pick(cfg, only, out string why); if (monsters == null) return "REFUSED " + why;
            var sb = new StringBuilder(); bool all = true;
            foreach (var m in monsters) { sb.AppendLine("  " + CheckClip(m, out bool ok)); all &= ok; }
            return (all ? "CLIP-CHECK OK\n" : "CLIP-CHECK REFUSED\n") + sb.ToString().TrimEnd();
        }

        // Same importer settings CompactFolklore298.ModelSettings gives an animation-only FBX (humanoid, avatar copied from the rig).
        static string ClipFix(Config cfg, string only)
        {
            if (string.IsNullOrEmpty(only)) return "REFUSED clip-fix needs one id";
            var monsters = Pick(cfg, only, out string why); if (monsters == null) return "REFUSED " + why;
            var sb = new StringBuilder();
            foreach (var m in monsters)
            {
                var importer = AssetImporter.GetAtPath(m.walkFixed) as ModelImporter; if (importer == null) { sb.AppendLine("  " + m.id + ": REFUSED no model importer at " + m.walkFixed); continue; }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CompactFolklore298.PrefabPath(m.id)); var avatar = prefab != null ? prefab.GetComponentInChildren<Animator>(true)?.avatar : null;
                if (avatar == null) { sb.AppendLine("  " + m.id + ": REFUSED prefab avatar missing"); continue; }
                importer.isReadable = true; importer.importAnimation = true; importer.importCameras = false; importer.importLights = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None; importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; importer.sourceAvatar = avatar; importer.animationCompression = ModelImporterAnimationCompression.Off;
                var takes = importer.defaultClipAnimations;
                foreach (var take in takes)
                {
                    take.loopTime = true; take.lockRootHeightY = true; take.lockRootPositionXZ = true; take.lockRootRotation = true;
                    take.keepOriginalOrientation = true; take.keepOriginalPositionXZ = true; take.keepOriginalPositionY = true; take.events = Array.Empty<AnimationEvent>();
                }
                importer.clipAnimations = takes; importer.SaveAndReimport();
                sb.AppendLine("  " + CheckClip(m, out _));
            }
            return "clip-fix (importer settings re-applied, asset re-imported)\n" + sb.ToString().TrimEnd();
        }

        // Stance-foot speed of a walk clip on the prefab copy (world metres per clip second at the prefab's own scale).
        static float StanceSpeed(Config cfg, Monster m, AnimationClip clip, out float fixtureScale, out float cycleSeconds)
        {
            using (var f = new Fixture(m))
            {
                Sample(f, clip, 0); var g = BuildGeo(f); fixtureScale = f.animator.transform.lossyScale.y;
                var set = RunSet(f, g, null, cfg, null, "walk", "walk", clip, "off", clip, null, false);
                cycleSeconds = set.gait != null ? set.gait.seconds : 0;
                return set.gait != null && set.gait.valid ? set.gait.speed : 0;
            }
        }

        // ------------------------------------------------------------------ scenes (R1 speed, R3 + R4 arms)
        [Serializable] sealed class ClipRef { public string path = "", name = ""; }
        [Serializable] sealed class ActorRecord
        {
            public string indexPath = "", namePath = "", actorId = "", monster = "", relaxProfile = "", newRelaxProfile = "";
            public string[] changed = Array.Empty<string>();
            public ClipRef walk = new ClipRef(), newWalk = new ClipRef();
            public float walkMetresPerSecond, newWalkMetresPerSecond, armRelax, elbowRelax, speed, deathVisualSeconds, scale;
        }
        [Serializable] sealed class SceneRecord { public string scene = "", utc = "", backup = "", options = ""; public List<ActorRecord> actors = new List<ActorRecord>(); }
        static ClipRef Ref(AnimationClip clip) => clip == null ? new ClipRef() : new ClipRef { path = AssetDatabase.GetAssetPath(clip), name = clip.name };
        static AnimationClip Load(ClipRef r) => r == null || string.IsNullOrEmpty(r.path) ? null : AssetDatabase.LoadAllAssetsAtPath(r.path).OfType<AnimationClip>().FirstOrDefault(c => c.name == r.name);

        static string SceneOp(Config cfg, string key, Dictionary<string, string> options, bool apply)
        {
            string path = ScenePath(key); if (path == null) return "REFUSED scene not in the #308 ledger: " + key + " (296 | 298 | main)";
            if (!options.TryGetValue("ops", out string opText)) return "REFUSED ops= is required (speed | arms | speed+arms)";
            var ops = opText.Split('+'); if (ops.Length == 0 || ops.Any(o => o != "speed" && o != "arms")) return "REFUSED ops takes speed, arms or speed+arms";
            bool speed = ops.Contains("speed"), arms = ops.Contains("arms"), keepClip = false;
            if (options.TryGetValue("clip", out string clipText)) { if (clipText != "keep" && clipText != "fixed") return "REFUSED clip takes keep or fixed"; keepClip = clipText == "keep"; }
            // rev 2: profile=B = the candidate B profile (walk relax of the repaired clip as data); default = the walk-0 profile as before
            bool useB = false;
            if (options.TryGetValue("profile", out string profileText)) { if (profileText != "B" && profileText != "default") return "REFUSED profile takes B or default"; useB = profileText == "B"; }
            if (useB && (keepClip || !arms)) return "REFUSED profile=B goes with ops=arms and the repaired clip (not clip=keep)";
            options.TryGetValue("ids", out string only); var monsters = Pick(cfg, only ?? "", out string why); if (monsters == null) return "REFUSED " + why;
            string record = RecordFile(path);
            if (apply && File.Exists(record)) return "ALREADY APPLIED " + path + " (record " + record + "); revert:" + key + " first to re-apply";
            string dirty = DirtyScene(); if (dirty != null) return "REFUSED dirty scene " + dirty + " (save or discard it first)";
            string optionLine = string.Join(",", options.Select(kv => kv.Key + "=" + kv.Value));
            try
            {
                // preconditions and stance speeds on prefab copies, before any scene is opened
                var speedOrig = new Dictionary<string, float>(); var speedFixed = new Dictionary<string, float>(); var cycle = new Dictionary<string, float>(); var fixtureScale = new Dictionary<string, float>();
                foreach (var m in monsters)
                {
                    if (arms)
                    {
                        if (Profile(ArmsProfilePath(m, keepClip, useB)) == null) return "REFUSED " + m.id + ": relax profile asset missing (" + ArmsProfilePath(m, keepClip, useB) + ")";
                        if (!keepClip) { string text = CheckClip(m, out bool ok); if (!ok) return "REFUSED repaired clip not ready (clip-check):\n  " + text; }
                    }
                    var source = OnlyClip(m.walkSource); if (source == null) return "REFUSED " + m.id + ": walk source clip not found in " + m.walkSource;
                    speedOrig[m.id] = StanceSpeed(cfg, m, source, out float scale, out float seconds); fixtureScale[m.id] = scale; cycle[m.id] = seconds;
                    var repaired = FixedClip(m); if (repaired != null) speedFixed[m.id] = StanceSpeed(cfg, m, repaired, out _, out _);
                }
                var scene = SceneManager.GetActiveScene().path == path ? SceneManager.GetActiveScene() : EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var targets = Targets(scene, cfg).Where(t => monsters.Contains(t.monster)).ToList();
                if (targets.Count == 0) return "NO TARGETS " + path + ": no " + string.Join("/", monsters.Select(m => m.id)) + " actor outside protected trees (scene unchanged, nothing saved)";
                var sb = new StringBuilder(path + " (" + (apply ? "apply " : "plan ") + optionLine + ")\n");
                var rec = new SceneRecord { scene = path, utc = DateTime.UtcNow.ToString("O"), options = optionLine }; bool recordWritten = false, saved = false;
                try
                {
                    foreach (var (rig, m, _) in targets)
                    {
                        var enc = rig.GetComponent<PrologueEncounter>();
                        var a = new ActorRecord { indexPath = IndexPath(rig.transform), namePath = NamePath(rig.transform), actorId = enc != null ? enc.Id : "", monster = m.id, walk = Ref(rig.Walk),
                            relaxProfile = rig.RelaxProfile != null ? AssetDatabase.GetAssetPath(rig.RelaxProfile) : "", walkMetresPerSecond = rig.WalkMetresPerSecond, armRelax = rig.ArmRelax, elbowRelax = rig.ElbowRelax,
                            speed = enc != null ? enc.Speed : 0, deathVisualSeconds = enc != null ? enc.DeathVisualSeconds : 0, scale = rig.Animator.transform.lossyScale.y / Mathf.Max(1e-6f, fixtureScale[m.id]) };
                        var changed = new List<string>(); AnimationClip walkAfter = rig.Walk; var profileAfter = rig.RelaxProfile; float metresAfter = rig.WalkMetresPerSecond;
                        if (arms)
                        {
                            if (!keepClip) { var repaired = FixedClip(m); if (rig.Walk != repaired) { walkAfter = repaired; changed.Add("Walk"); } }
                            profileAfter = Profile(ArmsProfilePath(m, keepClip, useB)); if (rig.RelaxProfile != profileAfter) changed.Add("RelaxProfile");
                        }
                        bool repairedAfter = AssetDatabase.GetAssetPath(walkAfter) == m.walkFixed, repairedBefore = AssetDatabase.GetAssetPath(rig.Walk) == m.walkFixed;
                        float vBefore = (repairedBefore && speedFixed.ContainsKey(m.id) ? speedFixed[m.id] : speedOrig[m.id]) * a.scale;
                        float vAfter = (repairedAfter && speedFixed.ContainsKey(m.id) ? speedFixed[m.id] : speedOrig[m.id]) * a.scale;
                        if (speed)
                        {
                            if (vAfter < .1f) throw new InvalidOperationException(a.namePath + " walk stance speed not measurable");
                            if (Mathf.Abs(rig.WalkMetresPerSecond - vAfter) > 1e-4f) { metresAfter = vAfter; changed.Add("WalkMetresPerSecond"); }
                        }
                        float slideBefore = Mathf.Abs(1 - vBefore / Mathf.Max(.1f, rig.WalkMetresPerSecond)), slideAfter = Mathf.Abs(1 - vAfter / Mathf.Max(.1f, metresAfter));
                        float stepsBefore = cycle[m.id] > 0 ? 120f * (a.speed / Mathf.Max(.1f, rig.WalkMetresPerSecond)) / cycle[m.id] : 0, stepsAfter = cycle[m.id] > 0 ? 120f * (a.speed / Mathf.Max(.1f, metresAfter)) / cycle[m.id] : 0;
                        string deathNote = enc != null && rig.Death != null && enc.DeathVisualSeconds < rig.Death.length + cfg.deathVisualMargin ? " WARN DeathVisualSeconds " + F(enc.DeathVisualSeconds) + " < death " + F(rig.Death.length) + " + " + F(cfg.deathVisualMargin) : "";
                        sb.AppendLine("  " + (a.actorId != "" ? a.actorId : a.namePath) + " [" + m.id + "] Speed " + F(a.speed) + " (unchanged) | WMPS " + F(rig.WalkMetresPerSecond) + " -> " + F(metresAfter) + " | slide " + F1(slideBefore * 100) + " % -> " + F1(slideAfter * 100) +
                            " % | steps/min " + F1(stepsBefore) + " -> " + F1(stepsAfter) + " | walk " + (repairedBefore ? "repaired" : "source") + " -> " + (repairedAfter ? "repaired" : "source") +
                            " | relax " + RelaxLabel(rig) + " -> " + (profileAfter != null ? "profile " + profileAfter.name : "(as before)") + " | " + (changed.Count == 0 ? "unchanged" : string.Join(",", changed)) + deathNote);
                        if (changed.Count == 0 || !apply) continue;
                        rig.Walk = walkAfter; rig.RelaxProfile = profileAfter; rig.WalkMetresPerSecond = metresAfter;
                        if (!rig.IsConfigured) throw new InvalidOperationException(a.namePath + " EnemyRigMotion298 not configured after apply");
                        a.changed = changed.ToArray(); a.newWalk = Ref(walkAfter); a.newRelaxProfile = profileAfter != null ? AssetDatabase.GetAssetPath(profileAfter) : ""; a.newWalkMetresPerSecond = metresAfter;
                        EditorUtility.SetDirty(rig); rec.actors.Add(a);
                    }
                    if (!apply) return sb.Append("  plan only: " + targets.Count + " actor(s) read, nothing changed, nothing saved").ToString();
                    if (rec.actors.Count == 0) return sb.Append("  nothing changed (not saved)").ToString();
                    string records = Path.GetDirectoryName(record); Directory.CreateDirectory(records);
                    rec.backup = Path.Combine(records, Path.GetFileNameWithoutExtension(path) + "-pre308enemyrig-" + Stamp + ".unity"); File.Copy(ProjectFile(path), rec.backup, true);
                    // record first: a saved scene without a record could neither be reverted nor refused as ALREADY APPLIED
                    File.WriteAllText(record, JsonUtility.ToJson(rec, true)); recordWritten = true;
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("SaveScene returned false");
                    saved = true;
                    return sb.Append("  saved " + rec.actors.Count + " actor(s); record " + record + "; scene backup " + rec.backup).ToString();
                }
                catch (Exception e)
                {
                    if (recordWritten && !saved && File.Exists(record)) File.Delete(record);
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);   // discard this command's partial edits
                    return "FAILED " + path + " (partial edits discarded, nothing saved): " + e.Message;
                }
            }
            finally { Sweep(); }
        }

        static string Revert(Config cfg, string key)
        {
            string path = ScenePath(key); if (path == null) return "REFUSED scene not in the #308 ledger: " + key;
            string record = RecordFile(path); if (!File.Exists(record)) return "REFUSED no apply record " + record;
            string dirty = DirtyScene(); if (dirty != null) return "REFUSED dirty scene " + dirty;
            var rec = JsonUtility.FromJson<SceneRecord>(File.ReadAllText(record));
            var scene = SceneManager.GetActiveScene().path == path ? SceneManager.GetActiveScene() : EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EnemyRigMotion298>(true)).ToArray();
            var sb = new StringBuilder(path + " (revert)\n");
            try
            {
                int restored = 0;
                foreach (var a in rec.actors)
                {
                    // the recorded sibling-index path, accepted only when name path and actor id still match; else the unique actor id
                    var t = Resolve(scene, a.indexPath); var rig = t != null ? t.GetComponent<EnemyRigMotion298>() : null;
                    if (rig == null || NamePath(rig.transform) != a.namePath || (rig.GetComponent<PrologueEncounter>()?.Id ?? "") != a.actorId)
                    {
                        var byId = a.actorId == "" ? Array.Empty<EnemyRigMotion298>() : all.Where(r => (r.GetComponent<PrologueEncounter>()?.Id ?? "") == a.actorId).ToArray();
                        rig = byId.Length == 1 ? byId[0] : null;
                    }
                    if (rig == null) { sb.AppendLine("  WARN not found unambiguously: " + a.namePath + " (id " + a.actorId + ") - skipped"); continue; }
                    rig.Walk = Load(a.walk); rig.RelaxProfile = Profile(a.relaxProfile); rig.WalkMetresPerSecond = a.walkMetresPerSecond; rig.ArmRelax = a.armRelax; rig.ElbowRelax = a.elbowRelax;
                    if (!rig.IsConfigured) throw new InvalidOperationException(a.namePath + " recorded walk clip no longer loads");
                    EditorUtility.SetDirty(rig); restored++;
                    sb.AppendLine("  " + (a.actorId != "" ? a.actorId : a.namePath) + ": restored " + string.Join(",", a.changed) + " (WMPS " + F(a.walkMetresPerSecond) + ", walk " + a.walk.name + ", profile " + (a.relaxProfile == "" ? "none" : Path.GetFileNameWithoutExtension(a.relaxProfile)) + ")");
                }
                if (restored == 0) return sb.Append("  nothing restored (record kept, scene not saved)").ToString();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("SaveScene returned false");
                File.Move(record, record.Replace(".json", "-reverted-" + Stamp + ".json"));
                return sb.Append("  saved; scene backup kept at " + rec.backup).ToString();
            }
            catch (Exception e)
            {
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                return "FAILED " + path + " (revert edits discarded, record kept): " + e.Message;
            }
        }

        static string All(Config cfg, Dictionary<string, string> options, bool apply)
        {
            var lines = new List<string>();
            foreach (var scene in apply ? Ledger : Ledger.Reverse())
            {
                if (!apply && !File.Exists(RecordFile(scene))) { lines.Add(scene + ": no record (skipped)"); continue; }
                string r = apply ? SceneOp(cfg, scene, options, true) : Revert(cfg, scene);
                lines.Add(r);
                if (r.StartsWith("REFUSED", StringComparison.Ordinal) || r.StartsWith("FAILED", StringComparison.Ordinal)) { lines.Add("stopped at " + scene); break; }
            }
            return string.Join("\n", lines);
        }

        // ------------------------------------------------------------------ death first frame (R2)
        [Serializable] sealed class DeathRow { public string id = "", asset = "", take = "", metaBackup = ""; public float firstFrameBefore, firstFrameAfter, lengthBefore, lengthAfter, popBefore, popAfter; }
        [Serializable] sealed class DeathLog { public string utc = ""; public List<DeathRow> rows = new List<DeathRow>(); }

        static float FirstStep(Fixture f, AnimationClip clip)
        {
            var bones = f.skin.bones; Sample(f, clip, 0); var a = bones.Select(b => b != null ? b.localRotation : Quaternion.identity).ToArray();
            Sample(f, clip, 1f / Mathf.Max(1f, clip.frameRate)); float worst = 0;
            for (int i = 0; i < bones.Length; i++) if (bones[i] != null) worst = Mathf.Max(worst, Quaternion.Angle(a[i], bones[i].localRotation));
            return worst;
        }

        static string Death(Config cfg, string mode)
        {
            if (mode == "revert")
            {
                if (!File.Exists(DeathRecord)) return "REFUSED no death record " + DeathRecord;
                var log = JsonUtility.FromJson<DeathLog>(File.ReadAllText(DeathRecord)); var sbr = new StringBuilder("death-revert\n");
                foreach (var row in log.rows)
                {
                    var importer = AssetImporter.GetAtPath(row.asset) as ModelImporter; if (importer == null) { sbr.AppendLine("  WARN no importer " + row.asset + " (skipped)"); continue; }
                    var takes = importer.clipAnimations; var take = takes.FirstOrDefault(c => c.name == row.take); if (take == null) { sbr.AppendLine("  WARN take " + row.take + " missing in " + row.asset + " (skipped)"); continue; }
                    take.firstFrame = row.firstFrameBefore; importer.clipAnimations = takes; importer.SaveAndReimport();
                    sbr.AppendLine("  " + row.id + ": " + row.take + " firstFrame " + F(row.firstFrameAfter) + " -> " + F(row.firstFrameBefore) + " (re-imported)");
                }
                File.Move(DeathRecord, DeathRecord.Replace(".json", "-reverted-" + Stamp + ".json"));
                return sbr.ToString().TrimEnd();
            }
            if (mode == "apply" && File.Exists(DeathRecord)) return "ALREADY APPLIED (record " + DeathRecord + "); death-revert first";
            var sb = new StringBuilder("death-" + mode + " (limit " + F1(cfg.limits.popDeg) + " deg)\n"); var done = new DeathLog { utc = DateTime.UtcNow.ToString("O") };
            try
            {
                foreach (var m in cfg.monsters)
                {
                    var importer = AssetImporter.GetAtPath(m.motionSource) as ModelImporter; if (importer == null) { sb.AppendLine("  " + m.id + ": REFUSED no model importer at " + m.motionSource); continue; }
                    var clip = ClipIn(m.motionSource, m.deathTake); if (clip == null) { sb.AppendLine("  " + m.id + ": REFUSED take " + m.deathTake + " not found"); continue; }
                    var takes = importer.clipAnimations; var take = takes.FirstOrDefault(c => c.name == m.deathTake);
                    if (take == null) { sb.AppendLine("  " + m.id + ": REFUSED importer has no clip " + m.deathTake); continue; }
                    float pop; using (var f = new Fixture(m)) pop = FirstStep(f, clip);
                    bool needed = pop > cfg.limits.popDeg;
                    string line = "  " + m.id + ": " + m.deathTake + " firstFrame " + F(take.firstFrame) + ", length " + F(clip.length) + " s, first step " + F1(pop) + " deg -> " + (needed ? "firstFrame " + F(take.firstFrame + 1) : "no change (within the limit)");
                    if (!needed || mode == "dry") { sb.AppendLine(line); continue; }
                    string records = Path.GetDirectoryName(DeathRecord); Directory.CreateDirectory(records);
                    var row = new DeathRow { id = m.id, asset = m.motionSource, take = m.deathTake, firstFrameBefore = take.firstFrame, firstFrameAfter = take.firstFrame + 1, lengthBefore = clip.length, popBefore = pop,
                        metaBackup = Path.Combine(records, m.id + "-motion.fbx.meta.before-" + Stamp) };
                    File.Copy(ProjectFile(m.motionSource) + ".meta", row.metaBackup, true);
                    done.rows.Add(row); File.WriteAllText(DeathRecord, JsonUtility.ToJson(done, true));   // record before the re-import
                    take.firstFrame = row.firstFrameAfter; importer.clipAnimations = takes; importer.SaveAndReimport();
                    var after = ClipIn(m.motionSource, m.deathTake); if (after == null) throw new InvalidOperationException(m.id + " death clip missing after the re-import");
                    using (var f = new Fixture(m)) row.popAfter = FirstStep(f, after);
                    row.lengthAfter = after.length; File.WriteAllText(DeathRecord, JsonUtility.ToJson(done, true));
                    sb.AppendLine(line + " | applied: length " + F(row.lengthAfter) + " s, first step " + F1(row.popAfter) + " deg" + (row.popAfter > cfg.limits.popDeg ? " WARN still over the limit" : "") +
                        " | actors need DeathVisualSeconds >= " + F(row.lengthAfter + cfg.deathVisualMargin) + " (plan:<scene> lists it) | meta backup " + row.metaBackup);
                }
            }
            finally { Sweep(); }
            if (mode == "dry") sb.Append("  dry: nothing changed");
            else sb.Append(done.rows.Count == 0 ? "  nothing to change (no record written)" : "  record " + DeathRecord);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ import-manifest.json rows (R1 source of truth)
        [Serializable] sealed class ManifestRow { public string id = "", walkPathBefore = "", walkNameBefore = "", walkPathAfter = "", walkNameAfter = ""; public bool hadSpeed; public float speedBefore, speedAfter; }
        [Serializable] sealed class ManifestLog { public string utc = "", backup = "", shaAfter = ""; public List<ManifestRow> rows = new List<ManifestRow>(); }

        static string ManifestOp(Config cfg, Dictionary<string, string> options, string mode)
        {
            string file = ManifestFile; if (!File.Exists(file)) return "REFUSED no manifest " + file;
            if (mode == "revert")
            {
                if (!File.Exists(ManifestRecord)) return "REFUSED no manifest record " + ManifestRecord;
                var log = JsonUtility.FromJson<ManifestLog>(File.ReadAllText(ManifestRecord));
                if (Sha(file) != log.shaAfter) return "REFUSED the manifest changed after manifest-apply (sha differs): restore by hand from " + log.backup;
                File.Copy(log.backup, file, true); File.Move(ManifestRecord, ManifestRecord.Replace(".json", "-reverted-" + Stamp + ".json"));
                return "manifest-revert: restored " + file + " from " + log.backup;
            }
            if (mode == "apply" && File.Exists(ManifestRecord)) return "ALREADY APPLIED (record " + ManifestRecord + "); manifest-revert first";
            bool source = options.TryGetValue("clip", out string clipText) && clipText == "orig";
            if (clipText != null && clipText != "orig" && clipText != "fixed") return "REFUSED clip takes orig or fixed";
            options.TryGetValue("ids", out string only); var monsters = Pick(cfg, only ?? "", out string why); if (monsters == null) return "REFUSED " + why;
            var raw = File.ReadAllBytes(file); bool bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            var root = JObject.Parse(new UTF8Encoding(false).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0)));
            var sb = new StringBuilder("manifest-" + mode + " (" + (source ? "source walk clip" : "repaired walk clip") + ")\n"); var done = new ManifestLog { utc = DateTime.UtcNow.ToString("O") };
            try
            {
                foreach (var m in monsters)
                {
                    var row = (root["rows"] as JArray)?.OfType<JObject>().FirstOrDefault(r => (string)r["id"] == m.id); if (row == null) return "REFUSED manifest has no row " + m.id;
                    var walk = (row["clips"] as JArray)?.OfType<JObject>().FirstOrDefault(c => (string)c["role"] == "walk"); if (walk == null) return "REFUSED manifest row " + m.id + " has no walk clip";
                    AnimationClip clip;
                    if (source) clip = OnlyClip(m.walkSource);
                    else
                    {
                        string text = CheckClip(m, out bool ok); if (!ok) return "REFUSED repaired clip not ready (clip-check):\n  " + text;
                        // a later CompactFolklore298 import copies the manifest source over the asset: it must be the same file under the name the importer derives
                        string src = Path.Combine(Repo, m.manifestWalkPath); if (string.IsNullOrEmpty(m.manifestWalkPath) || !File.Exists(src)) return "REFUSED " + m.id + ": manifest source copy missing (" + m.manifestWalkPath + ")";
                        if (Sha(src) != Sha(ProjectFile(m.walkFixed))) return "REFUSED " + m.id + ": " + m.manifestWalkPath + " is not the file imported at " + m.walkFixed;
                        string derived = CompactFolklore298.AssetRoot + "/Animations/" + m.id + "/" + new DirectoryInfo(Path.GetDirectoryName(src)).Name + "_" + Path.GetFileName(src);
                        if (derived != m.walkFixed) return "REFUSED " + m.id + ": the importer would name the asset " + derived + ", the config says " + m.walkFixed;
                        clip = FixedClip(m);
                    }
                    if (clip == null) return "REFUSED " + m.id + ": walk clip not found";
                    float v = StanceSpeed(cfg, m, clip, out _, out _); if (v < .1f) return "REFUSED " + m.id + ": stance speed not measurable";
                    v = Mathf.Round(v * 10000f) / 10000f;
                    var log = new ManifestRow { id = m.id, hadSpeed = row["walkMetresPerSecond"] != null, speedBefore = row["walkMetresPerSecond"]?.Value<float>() ?? cfg.assumeWalkMetresPerSecond, speedAfter = v,
                        walkPathBefore = (string)walk["path"] ?? "", walkNameBefore = (string)walk["name"] ?? "", walkPathAfter = source ? (string)walk["path"] ?? "" : m.manifestWalkPath, walkNameAfter = source ? (string)walk["name"] ?? "" : m.manifestWalkName };
                    row["walkMetresPerSecond"] = v; if (!source) { walk["path"] = m.manifestWalkPath; walk["name"] = m.manifestWalkName; }
                    done.rows.Add(log);
                    sb.AppendLine("  " + m.id + ": walkMetresPerSecond " + (log.hadSpeed ? F(log.speedBefore) : "(absent = " + F(log.speedBefore) + ")") + " -> " + F(v) + (source ? "" : " | walk clip " + log.walkNameBefore + " -> " + log.walkNameAfter + " (" + log.walkPathAfter + ")"));
                }
            }
            finally { Sweep(); }
            if (mode == "dry") return sb.Append("  dry: nothing written").ToString();
            string records = Path.GetDirectoryName(ManifestRecord); Directory.CreateDirectory(records);
            done.backup = Path.Combine(records, "import-manifest.before-" + Stamp + ".json"); File.Copy(file, done.backup, true);
            var text2 = new UTF8Encoding(false).GetBytes(root.ToString()); File.WriteAllBytes(file, bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(text2).ToArray() : text2);
            try
            {
                var check = CompactFolklore298.ReadManifest();
                foreach (var m in monsters) { var r = check.rows.First(x => x.id == m.id); if (CompactFolklore298.Clip(r, "walk") == null) throw new Exception(m.id + " walk clip does not resolve"); }
            }
            catch (Exception e) { File.Copy(done.backup, file, true); return "FAILED manifest did not read back (restored from the backup): " + e.Message; }
            done.shaAfter = Sha(file); File.WriteAllText(ManifestRecord, JsonUtility.ToJson(done, true));
            return sb.Append("  written " + file + "; backup " + done.backup + "; record " + ManifestRecord).ToString();
        }
    }
}
