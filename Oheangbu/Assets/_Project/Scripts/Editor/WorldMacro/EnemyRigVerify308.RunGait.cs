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
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-23 (SPEC-ENEMY-RIG-VERIFY-308, section "run", TEST): run clips of agwi / changgui. A new part of the same tool:
    // fixtures, vertex sets, the penetration world and the pose capture are the ones of the walk measure (EnemyRigVerify308.Measure),
    // so run and walk numbers share one definition. Queue-safe: every refusal is a string, never a dialog. RunGait(string):
    //   status                       config, run FBX, profile assets, records (no scene is opened, nothing sampled); when the active scene
    //                                is a ledger scene, also the actors that carry a run clip / profile that is not their species'
    //   clip-check[:<id>]            run FBX: file = the gated one, humanoid, avatar copied from the rig, clip flags, length = cycle frames
    //   clip-fix:<id>                re-applies the importer settings of the run FBX (only needed when clip-check refuses on settings)
    //   parity                       AC-R4: without a RunProfile, or at run weight 0, the three-weight relax poses the arms as the two-weight one
    //   measure[:<id>]               the run clip on a prefab copy in a preview scene, arms corrected by EnemyRigMotion298.ApplyArmRelax at full
    //                                run weight (the method Play runs): stance-foot speed, slide at the profile's RunMetresPerSecond, sole height,
    //                                penetration, loop seam, knee angles, cadence per actor speed next to the walk at the rate it would need.
    //                                Writes Art/Characters308/EnemyRigVerify/run_measure_<id>.json + run_measure.md. Saves nothing.
    //   stills:<id>                  studio stills of the run phases (front / side / back) through the #298 CPU-baked still; GPU work: the
    //                                operator runs it under Tools/resource_guard.py --gpu-run
    //   plan:<scene>[|ids=a+b]       dry: what apply would change in one ledger scene (296 | 298 | main). Opens the scene, changes nothing.
    //   apply:<scene>[|ids=a+b]      ledger row R5 "run clip + switch data": Run -> run clip, RunProfile -> profile asset on every actor of
    //                                the species. Nothing else (Speed, Walk, WalkMetresPerSecond, RunThresholdMetresPerSecond stay).
    //                                An actor that carries this stage's run clip / profile WITHOUT being that species (a clone of a
    //                                template actor inherits the slots - CompactFolklore298 builds new species from mine_beast/0) gets
    //                                both slots cleared, and is listed. Scene file backed up, record written, only that scene saved.
    //   apply-all[|ids=a+b] | revert:<scene> | revert-all     ledger order 296 -> 298 -> main (revert: the reverse)
    // Stance of a run = the sole is down (height <= contactEpsCm) at both ends of an interval: a run has a flight phase, and the walk's
    // speed-plateau rule alone would count the last airborne interval before touch-down.
    public static partial class EnemyRigVerify308
    {
        static string RunConfigFile => Path.Combine(Out, "run308.json");
        static string RunRecordFile(string scene) => Path.Combine(Out, "Records", "run_apply_" + Path.GetFileNameWithoutExtension(scene) + ".json");

        // ------------------------------------------------------------------ data (run308.json: every number is TEST data, none in code)
        [Serializable] public sealed class RunMonster
        {
            public string id = "", runClip = "", runTake = "", runSha256 = "", runProfile = "", blenderRun = "";
            public int runFrames;
        }
        [Serializable] public sealed class RunLimits
        {
            public float gaitVsBlenderRatio, slideRatio, slidePerStepCm, soleMinCm, stanceSoleMeanMaxCm, penForeHandCm, penUpperCm, loopCloseDeg, seamStepRatio, stanceKneeMinDeg;
            public float sectionMinRatio, humanoidArmDeltaDeg, actorScaleTolerance, patrolLegMinMetres;
        }
        [Serializable] public sealed class RunStills { public float[] phases = Array.Empty<float>(), yaws = Array.Empty<float>(); }
        [Serializable] public sealed class RunConfig
        {
            public string schema = "";
            public float contactEpsCm;
            public RunMonster[] monsters = Array.Empty<RunMonster>();
            public RunLimits limits = new RunLimits();
            public RunStills stills = new RunStills();
        }

        static RunConfig ReadRunConfig(Config cfg, out string why)
        {
            why = null;
            if (!File.Exists(RunConfigFile)) { why = "no run config " + RunConfigFile; return null; }
            var run = JsonUtility.FromJson<RunConfig>(File.ReadAllText(RunConfigFile));
            if (run?.monsters == null || run.monsters.Length == 0) { why = "run config has no monsters: " + RunConfigFile; return null; }
            if (run.monsters.Any(r => string.IsNullOrEmpty(r.id) || string.IsNullOrEmpty(r.runClip) || string.IsNullOrEmpty(r.runTake) || string.IsNullOrEmpty(r.runProfile) || r.runFrames <= 0)) { why = "run config monster row incomplete (id / runClip / runTake / runProfile / runFrames)"; return null; }
            if (run.monsters.GroupBy(r => r.id).Any(g => g.Count() != 1)) { why = "run config monster ids are not unique"; return null; }
            if (run.monsters.Any(r => cfg.monsters.All(m => m.id != r.id))) { why = "run config names a monster that verify308.json does not have"; return null; }
            if (run.limits == null || run.limits.slideRatio <= 0 || run.contactEpsCm <= 0) { why = "run config limits / contactEpsCm missing"; return null; }
            return run;
        }

        public static string RunGait(string command)
        {
            command = (command ?? "").Trim();
            try
            {
                var cfg = ReadConfig(out string why); if (cfg == null) return "REFUSED " + why;
                var run = ReadRunConfig(cfg, out why); if (run == null) return "REFUSED " + why;
                if (command == "status") return RunStatus(cfg, run);
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Play mode (EnemyRigVerify308 works in Edit mode only)";
                string head = command, tail = "";
                int bar = command.IndexOf('|'); if (bar >= 0) { head = command.Substring(0, bar); tail = command.Substring(bar + 1); }
                string verb = head, arg = "";
                int colon = head.IndexOf(':'); if (colon >= 0) { verb = head.Substring(0, colon); arg = head.Substring(colon + 1).Trim(); }
                string ids = "";
                foreach (var part in tail.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = part.IndexOf('='); if (eq <= 0 || part.Substring(0, eq).Trim() != "ids") return "REFUSED unknown option '" + part + "' (ids=a+b)";
                    ids = part.Substring(eq + 1).Trim();
                }
                switch (verb)
                {
                    case "clip-check": return RunClipCheck(cfg, run, arg);
                    case "clip-fix": return RunClipFix(cfg, run, arg);
                    case "parity": return Guarded(() => RunParity(cfg, run));
                    case "measure": return Guarded(() => RunMeasure(cfg, run, arg));
                    case "stills": return Guarded(() => RunStillsCommand(cfg, run, arg));
                    case "plan": return RunSceneOp(cfg, run, arg, ids, false);
                    case "apply": return RunSceneOp(cfg, run, arg, ids, true);
                    case "apply-all": return RunAll(cfg, run, ids, true);
                    case "revert": return RunRevert(arg);
                    case "revert-all": return RunAll(cfg, run, ids, false);
                }
                return "REFUSED unknown command '" + command + "' (status | clip-check[:<id>] | clip-fix:<id> | parity | measure[:<id>] | stills:<id> | plan:<296|298|main> | apply:<scene> | apply-all | revert:<scene> | revert-all ; option ids=a+b)";
            }
            catch (Exception e) { return "FAILED " + e; }
        }

        static RunMonster[] PickRun(RunConfig run, string only, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(only)) return run.monsters;
            var ids = only.Split('+'); var picked = run.monsters.Where(r => ids.Contains(r.id)).ToArray();
            if (picked.Length != ids.Length) { why = "unknown run monster id in '" + only + "' (" + string.Join(" | ", run.monsters.Select(r => r.id)) + ")"; return null; }
            return picked;
        }
        static Monster MonsterOf(Config cfg, RunMonster r) => cfg.monsters.First(m => m.id == r.id);
        static AnimationClip RunClipOf(RunMonster r) => ClipIn(r.runClip, r.runTake);
        static EnemyRunProfile308 RunProfileOf(RunMonster r) => AssetDatabase.LoadAssetAtPath<EnemyRunProfile308>(r.runProfile);
        static string ProfileLine(EnemyRunProfile308 p) =>
            "enter " + F(p.EnterMetresPerSecond) + " exit " + F(p.ExitMetresPerSecond) + " hold " + F(p.EnterHoldSeconds) + " s " + (p.WalkOnPatrol ? "walk-on-patrol" : "runs on patrol too") + " blend " + F(p.BlendSeconds) + " s run " + F(p.RunMetresPerSecond) +
            " m/s relax " + F(p.RunArm) + "/" + F(p.RunElbow) + " avatar " + (p.Avatar != null ? p.Avatar.name : "none (species guard off)") + (p.Usable ? "" : " NOT USABLE");

        // Shortest patrol leg of an actor (m); -1 = it stands (fewer than two patrol points).
        static float MinPatrolLeg(PrologueEncounter enc)
        {
            var p = enc != null ? enc.PatrolPoints : null; if (p == null || p.Length < 2) return -1f;
            float min = float.PositiveInfinity; for (int i = 0; i < p.Length; i++) min = Mathf.Min(min, Vector3.Distance(p[i], p[(i + 1) % p.Length]));
            return min;
        }

        // Actors that carry this stage's run clip / profile without being that species. A clone of a template actor inherits the slots
        // (CompactFolklore298 builds every new species from mine_beast/0 and BindMotion does not clear Run / RunProfile). The runtime
        // never runs such an actor (EnemyRigMotion298.HasRun: clip kind and profile avatar must fit) - this list is the scene hygiene.
        static List<(EnemyRigMotion298 rig, string why)> ForeignRunCarriers(Scene scene, Config cfg, RunConfig run)
        {
            var list = new List<(EnemyRigMotion298, string)>(); var clips = new Dictionary<string, AnimationClip>(); var profiles = new Dictionary<string, EnemyRunProfile308>(); var species = new Dictionary<EnemyRigMotion298, string>();
            foreach (var r in run.monsters) { clips[r.id] = RunClipOf(r); profiles[r.id] = RunProfileOf(r); }
            foreach (var (rig, m, _) in Targets(scene, cfg)) species[rig] = m.id;
            foreach (var rig in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EnemyRigMotion298>(true)))
            {
                if (rig == null || ProtectedTree(rig.transform)) continue;
                bool stageClip = rig.Run != null && clips.Values.Any(c => c == rig.Run), stageProfile = rig.RunProfile != null && profiles.Values.Any(p => p == rig.RunProfile);
                if (!stageClip && !stageProfile) continue;
                species.TryGetValue(rig, out string id); string why = null;
                if (id == null || !clips.ContainsKey(id)) why = "not a run species (walk clip " + (rig.Walk != null ? rig.Walk.name : "none") + ")";
                else if (stageClip && rig.Run != clips[id] || stageProfile && rig.RunProfile != profiles[id]) why = "a " + id + " that carries another species' run";
                else if (rig.Animator != null && rig.Run != null && rig.Run.isHumanMotion != rig.Animator.isHuman) why = "humanoid run clip on a non-humanoid Animator";
                if (why != null) list.Add((rig, why));
            }
            return list;
        }
        static string CarrierName(EnemyRigMotion298 rig) { var enc = rig.GetComponent<PrologueEncounter>(); return enc != null && !string.IsNullOrEmpty(enc.Id) ? enc.Id : NamePath(rig.transform); }

        // ------------------------------------------------------------------ status
        static string RunStatus(Config cfg, RunConfig run)
        {
            var sb = new StringBuilder("EnemyRigVerify308 run status (" + run.schema + ")\n");
            foreach (var r in run.monsters)
            {
                var clip = RunClipOf(r); var profile = RunProfileOf(r); var m = MonsterOf(cfg, r);
                sb.AppendLine("  " + r.id + ": run clip " + (clip != null ? clip.name + " " + F(clip.length) + "s" : "not imported") +
                    " | profile " + (profile != null ? ProfileLine(profile) : "absent") +
                    " | repaired walk " + (FixedClip(m) != null ? "imported" : "not imported (the enemyrig stage comes first)"));
            }
            foreach (var scene in Ledger) sb.AppendLine("  " + scene + ": " + (File.Exists(RunRecordFile(scene)) ? "RUN APPLIED (record " + RunRecordFile(scene) + ")" : "run not applied"));
            var active = SceneManager.GetActiveScene();
            if (Ledger.Contains(active.path))
            {
                var foreign = ForeignRunCarriers(active, cfg, run);
                sb.AppendLine("  open scene " + active.path + ": " + (foreign.Count == 0 ? "no actor carries a run clip / profile that is not its species'" : foreign.Count + " FOREIGN run carrier(s) - plan:<scene> lists them, apply clears them:"));
                foreach (var (rig, why) in foreign) sb.AppendLine("    " + CarrierName(rig) + ": " + why);
            }
            return sb.ToString().TrimEnd();
        }

        // ------------------------------------------------------------------ run FBX
        static string CheckRunClip(Config cfg, RunMonster r, out bool ok)
        {
            ok = false; var errors = new List<string>(); var notes = new List<string>(); var m = MonsterOf(cfg, r);
            string file = ProjectFile(r.runClip);
            if (!File.Exists(file)) return r.id + ": " + r.runClip + " is not in Assets (copy the stage, then one Refresh)";
            string sha = Sha(file);
            if (!string.IsNullOrEmpty(r.runSha256) && sha != r.runSha256) errors.Add("file is not the gated one (sha " + sha.Substring(0, 12) + " != config " + r.runSha256.Substring(0, Math.Min(12, r.runSha256.Length)) + ")");
            var importer = AssetImporter.GetAtPath(r.runClip) as ModelImporter;
            if (importer == null) return r.id + ": no model importer at " + r.runClip + " (Refresh first)";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CompactFolklore298.PrefabPath(r.id)); var avatar = prefab != null ? prefab.GetComponentInChildren<Animator>(true)?.avatar : null;
            if (avatar == null) errors.Add("prefab avatar missing");
            if (importer.animationType != ModelImporterAnimationType.Human) errors.Add("animationType " + importer.animationType + " (want Human)");
            if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther) errors.Add("avatarSetup " + importer.avatarSetup + " (want CopyFromOther)");
            if (avatar != null && importer.sourceAvatar != avatar) errors.Add("sourceAvatar is not the avatar of " + CompactFolklore298.PrefabPath(r.id));
            var entry = importer.clipAnimations.FirstOrDefault(c => c.name == r.runTake || c.takeName == r.runTake);
            if (entry == null) errors.Add("clipAnimations has no take " + r.runTake + " (has " + string.Join(", ", importer.clipAnimations.Select(c => c.name)) + ")");
            else
            {
                if (!(entry.loopTime && entry.lockRootHeightY && entry.lockRootPositionXZ && entry.lockRootRotation && entry.keepOriginalOrientation && entry.keepOriginalPositionXZ && entry.keepOriginalPositionY))
                    errors.Add("clip flags differ from the walk (want loopTime + bake-into-pose x3 + keep-original x3)");
                if (Mathf.RoundToInt(entry.firstFrame) != 0 || Mathf.RoundToInt(entry.lastFrame) != r.runFrames) errors.Add("clip frames " + F(entry.firstFrame) + ".." + F(entry.lastFrame) + " (want 0.." + r.runFrames + ")");
            }
            var clip = RunClipOf(r);
            if (clip == null) errors.Add("clip " + r.runTake + " not imported");
            else
            {
                if (clip.legacy || clip.length <= 0) errors.Add("clip is legacy or empty");
                if (!clip.isHumanMotion) errors.Add("clip is not a humanoid clip");
                float want = r.runFrames / Mathf.Max(1f, clip.frameRate);
                if (Mathf.Abs(clip.length - want) > .5f / Mathf.Max(1f, clip.frameRate)) errors.Add("length " + F(clip.length) + " s != " + r.runFrames + " frames at " + F(clip.frameRate) + " fps (" + F(want) + " s)");
                notes.Add(clip.name + " " + F(clip.length) + " s @" + F(clip.frameRate) + " fps loop " + clip.isLooping);
            }
            if (FixedClip(m) == null) notes.Add("repaired walk not imported yet");
            // the profile says whom the clip is for (EnemyRigMotion298.HasRun): it must be this rig's avatar, or the Run node is never built
            var runProfile = RunProfileOf(r);
            if (runProfile != null && runProfile.Avatar == null) notes.Add("run profile names no avatar (species guard off)");
            else if (runProfile != null && avatar != null && runProfile.Avatar != avatar) errors.Add("run profile Avatar '" + runProfile.Avatar.name + "' is not the avatar of " + CompactFolklore298.PrefabPath(r.id) + " (the Run node would never be built)");
            ok = errors.Count == 0;
            return r.id + ": " + (ok ? "ok" : "REFUSED") + " " + r.runClip + " sha " + sha.Substring(0, 12) + (notes.Count > 0 ? " | " + string.Join("; ", notes) : "") + (errors.Count > 0 ? "\n    " + string.Join("\n    ", errors) : "");
        }

        static string RunClipCheck(Config cfg, RunConfig run, string only)
        {
            var picked = PickRun(run, only, out string why); if (picked == null) return "REFUSED " + why;
            var sb = new StringBuilder(); bool all = true;
            foreach (var r in picked) { sb.AppendLine("  " + CheckRunClip(cfg, r, out bool ok)); all &= ok; }
            return (all ? "RUN CLIP-CHECK OK\n" : "RUN CLIP-CHECK REFUSED\n") + sb.ToString().TrimEnd();
        }

        // The importer settings the repaired walk FBX has (humanoid, avatar copied from the rig, loop, bake-into-pose), frames 0..runFrames.
        static string RunClipFix(Config cfg, RunConfig run, string only)
        {
            if (string.IsNullOrEmpty(only)) return "REFUSED clip-fix needs one id";
            var picked = PickRun(run, only, out string why); if (picked == null) return "REFUSED " + why;
            var sb = new StringBuilder();
            foreach (var r in picked)
            {
                var importer = AssetImporter.GetAtPath(r.runClip) as ModelImporter; if (importer == null) { sb.AppendLine("  " + r.id + ": REFUSED no model importer at " + r.runClip); continue; }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CompactFolklore298.PrefabPath(r.id)); var avatar = prefab != null ? prefab.GetComponentInChildren<Animator>(true)?.avatar : null;
                if (avatar == null) { sb.AppendLine("  " + r.id + ": REFUSED prefab avatar missing"); continue; }
                importer.isReadable = true; importer.importAnimation = true; importer.importCameras = false; importer.importLights = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None; importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; importer.sourceAvatar = avatar; importer.animationCompression = ModelImporterAnimationCompression.Off;
                var takes = importer.defaultClipAnimations;
                foreach (var take in takes)
                {
                    take.loopTime = true; take.lockRootHeightY = true; take.lockRootPositionXZ = true; take.lockRootRotation = true;
                    take.keepOriginalOrientation = true; take.keepOriginalPositionXZ = true; take.keepOriginalPositionY = true; take.events = Array.Empty<AnimationEvent>();
                    take.firstFrame = 0; take.lastFrame = r.runFrames;
                }
                importer.clipAnimations = takes; importer.SaveAndReimport();
                sb.AppendLine("  " + CheckRunClip(cfg, r, out _));
            }
            return "run clip-fix (importer settings re-applied, asset re-imported)\n" + sb.ToString().TrimEnd();
        }

        // ------------------------------------------------------------------ parity (AC-R4)
        static string RunParity(Config cfg, RunConfig run)
        {
            var sb = new StringBuilder("run parity (three-weight relax vs two-weight relax: no RunProfile at run weight .5, and a RunProfile at run weight 0)\n"); bool all = true;
            var bones = new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand };
            foreach (var r in run.monsters)
            {
                var m = MonsterOf(cfg, r); var walk = OnlyClip(m.walkSource); var profile = RunProfileOf(r);
                if (walk == null) { sb.AppendLine("  " + r.id + ": walk source clip not found (skipped)"); all = false; continue; }
                using (var f = new Fixture(m))
                {
                    float worst = 0; int samples = 0;
                    foreach (var relax in new[] { null, Profile(m.relaxProfile) })
                        foreach (var w in new[] { new Vector2(.3f, .7f), new Vector2(1, 0), new Vector2(0, 1) })
                            for (int i = 0; i < 8; i++)
                            {
                                float t = walk.length * i / 8f; f.rig.Walk = walk; f.rig.ArmRelax = -1; f.rig.ElbowRelax = -1; f.rig.RelaxProfile = relax;
                                Sample(f, walk, t); f.rig.RunProfile = null; f.rig.ApplyArmRelax(w.x, w.y);
                                var reference = bones.Select(b => f.animator.GetBoneTransform(b).rotation).ToArray();
                                Sample(f, walk, t); f.rig.RunProfile = null; f.rig.ApplyArmRelax(w.x, w.y, .5f);
                                for (int k = 0; k < bones.Length; k++) worst = Mathf.Max(worst, Quaternion.Angle(reference[k], f.animator.GetBoneTransform(bones[k]).rotation));
                                if (profile != null)
                                {
                                    Sample(f, walk, t); f.rig.RunProfile = profile; f.rig.ApplyArmRelax(w.x, w.y, 0f);
                                    for (int k = 0; k < bones.Length; k++) worst = Mathf.Max(worst, Quaternion.Angle(reference[k], f.animator.GetBoneTransform(bones[k]).rotation));
                                }
                                samples++;
                            }
                    f.rig.RunProfile = null; f.rig.RelaxProfile = null;
                    bool ok = worst <= 0f; all &= ok;
                    sb.AppendLine("  " + r.id + ": " + samples + " poses, max arm bone difference " + worst.ToString("0.#####", CultureInfo.InvariantCulture) + " deg " + (ok ? "(equal)" : "MISMATCH") + (profile == null ? " | run profile asset absent: second half not tested" : ""));
                }
            }
            return sb.Append(all ? "RUN PARITY OK" : "RUN PARITY FAILED").ToString();
        }

        // ------------------------------------------------------------------ measure
        sealed class RunSample { public Pose pose; public readonly float[] knee = new float[2]; }
        sealed class RunGaitResult
        {
            public bool valid; public float speed, plateauSpeed, stanceSeconds, seconds; public int flightSamples, doubleSamples;
            public readonly int[] contactSamples = new int[2], contactIntervals = new int[2]; public readonly float[] stanceSpeed = new float[2], slideInClip = new float[2], soleMeanContact = new float[2], soleMinCycle = new float[2], soleMaxCycle = new float[2];
            public readonly float[] kneeMinContact = { float.PositiveInfinity, float.PositiveInfinity }, kneeMin = { float.PositiveInfinity, float.PositiveInfinity }, kneeMax = new float[2], kneeMaxStep = new float[2];
        }

        static float Knee(Animator a, bool left)
        {
            var up = a.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg); var lo = a.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            var foot = a.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            return up == null || lo == null || foot == null ? 0 : Vector3.Angle(lo.position - up.position, foot.position - lo.position);
        }

        // The run clip as the game shows it at full run weight: PlayableGraph sample, then ApplyArmRelax(0, 0, 1) with the actor's profiles.
        static List<RunSample> SampleRun(Fixture f, Geo g, PenWorld pen, Config cfg, FolkloreCullingBounds298 culling, AnimationClip clip, AnimationClip walk, EnemyArmRelaxProfile308 relax, EnemyRunProfile308 profile, bool penetration)
        {
            var list = new List<RunSample>(); int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * clip.frameRate));
            for (int i = 0; i <= frames; i++)
            {
                float t = Mathf.Min(i / clip.frameRate, clip.length);
                Sample(f, clip, t);
                f.rig.Walk = walk; f.rig.ArmRelax = -1f; f.rig.ElbowRelax = -1f; f.rig.RelaxProfile = relax; f.rig.RunProfile = profile; f.rig.ApplyArmRelax(0f, 0f, 1f);
                var s = new RunSample { pose = Capture(f, g, pen, cfg, t, penetration, culling) };
                s.knee[0] = Knee(f.animator, true); s.knee[1] = Knee(f.animator, false); list.Add(s);
            }
            f.rig.RunProfile = null; f.rig.RelaxProfile = null;
            return list;
        }

        static RunGaitResult MeasureRunGait(List<RunSample> samples, Config cfg, float eps, float frameRate)
        {
            var result = new RunGaitResult(); int n = samples.Count - 1; if (n < 8) return result;
            float dt = 1f / frameRate; result.seconds = n * dt; double sum = 0; int count = 0; int intervalsTotal = 0;
            var contact = new bool[2][];
            for (int s = 0; s < 2; s++)
            {
                contact[s] = new bool[n + 1]; float soleSum = 0; result.soleMinCycle[s] = float.PositiveInfinity; result.soleMaxCycle[s] = float.NegativeInfinity;
                for (int k = 0; k <= n; k++) contact[s][k] = samples[k].pose.soleMin[s] <= eps;
                for (int k = 0; k < n; k++)
                {
                    var p = samples[k].pose; float sole = p.soleMin[s]; result.soleMinCycle[s] = Mathf.Min(result.soleMinCycle[s], sole); result.soleMaxCycle[s] = Mathf.Max(result.soleMaxCycle[s], sole);
                    float knee = samples[k].knee[s]; result.kneeMin[s] = Mathf.Min(result.kneeMin[s], knee); result.kneeMax[s] = Mathf.Max(result.kneeMax[s], knee);
                    result.kneeMaxStep[s] = Mathf.Max(result.kneeMaxStep[s], Mathf.Abs(samples[k + 1].knee[s] - knee));
                    if (contact[s][k]) { result.contactSamples[s]++; soleSum += sole; result.kneeMinContact[s] = Mathf.Min(result.kneeMinContact[s], knee); }
                }
                result.soleMeanContact[s] = result.contactSamples[s] > 0 ? soleSum / result.contactSamples[s] : 0;
                var back = new List<float>();
                for (int k = 0; k < n; k++)
                {
                    if (!contact[s][k] || !contact[s][k + 1]) continue;
                    Vector3 a = Vector3.zero, b = Vector3.zero; var low = samples[k].pose.low[s];
                    foreach (int v in low) { a += samples[k].pose.verts[v]; b += samples[k + 1].pose.verts[v]; }
                    back.Add(-(b.z - a.z) / low.Length / dt);
                }
                result.contactIntervals[s] = back.Count; if (back.Count == 0) continue;
                float mean = back.Average(); result.stanceSpeed[s] = mean; result.slideInClip[s] = back.Sum(x => Mathf.Abs(x - mean)) * dt;
                sum += back.Sum(); count += back.Count; intervalsTotal += back.Count;
            }
            for (int k = 0; k < n; k++) { if (!contact[0][k] && !contact[1][k]) result.flightSamples++; if (contact[0][k] && contact[1][k]) result.doubleSamples++; }
            result.speed = count > 0 ? (float)(sum / count) : 0; result.stanceSeconds = intervalsTotal * .5f * dt; result.valid = result.speed > .05f;
            var plateau = MeasureGait(samples.Select(x => x.pose).ToList(), cfg, frameRate); result.plateauSpeed = plateau.valid ? plateau.speed : 0;
            return result;
        }

        static string RunMeasure(Config cfg, RunConfig run, string only)
        {
            var picked = PickRun(run, only, out string why); if (picked == null) return "REFUSED " + why;
            Directory.CreateDirectory(Out);
            var active = SceneManager.GetActiveScene(); bool ledgerScene = Ledger.Contains(active.path);
            var md = new StringBuilder("# 달리기 클립 측정 (편집기) — EnemyRigVerify308 RunGait measure\n\n");
            md.AppendLine("- 시각(UTC): " + DateTime.UtcNow.ToString("O") + " · 품질 단계: " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " · 정점당 본: " + QualitySettings.skinWeights);
            md.AppendLine("- 자세: 프리팹 사본(미리보기 씬) + PlayableGraph 표본 + `EnemyRigMotion298.ApplyArmRelax(0, 0, 1)`(Play와 같은 함수, 달리기 가중치 1). 씬 · 에셋 저장 0.");
            md.AppendLine("- 디딤 = 발바닥 높이 ≤ " + F(run.contactEpsCm) + " cm인 표본 사이 구간(달리기는 체공이 있어 속도 고원만으로 가리지 않는다). 걷기 열은 걷기 측정과 같은 속도 고원 규칙이다.");
            md.AppendLine("- 배우 속도: " + (ledgerScene ? "열린 씬 `" + active.path + "`에서 읽음" : "열린 씬이 원장 씬이 아니라 읽지 않음(걸음 빠르기 표 없음)") + "\n");
            var lines = new List<string>();
            foreach (var r in picked) lines.Add(RunMeasureOne(cfg, run, r, ledgerScene ? active : default, md));
            File.WriteAllText(Path.Combine(Out, string.IsNullOrEmpty(only) ? "run_measure.md" : "run_measure_" + only.Replace('+', '_') + ".md"), md.ToString(), new UTF8Encoding(false));
            return "run measure -> " + Out + "\n" + string.Join("\n", lines);
        }

        static string RunMeasureOne(Config cfg, RunConfig run, RunMonster r, Scene actorScene, StringBuilder md)
        {
            var lim = run.limits; var m = MonsterOf(cfg, r);
            var clip = RunClipOf(r); if (clip == null) return r.id + ": REFUSED run clip not imported (clip-check)";
            var profile = RunProfileOf(r); if (profile == null) return r.id + ": REFUSED run profile asset missing (" + r.runProfile + ")";
            var walk = FixedClip(m) ?? OnlyClip(m.walkSource); if (walk == null) return r.id + ": REFUSED no walk clip (repaired or source)";
            bool repairedWalk = FixedClip(m) != null; var relax = Profile(m.relaxProfile);
            float walkSpeed = StanceSpeed(cfg, m, walk, out _, out float walkSeconds);
            List<RunSample> samples; string selfTest; float eps = run.contactEpsCm / 100f;
            using (var f = new Fixture(m))
            {
                Sample(f, clip, 0); var g = BuildGeo(f); var culling = f.visual.GetComponent<FolkloreCullingBounds298>();
                using (var pen = new PenWorld(f, g, cfg.pen))
                {
                    pen.Update(CompactFolklore298.PhysicalSkinVertices298(f.skin)); pen.Check(g.hips.position, g.hips.position + Vector3.up * 5f); selfTest = pen.SelfTest;
                    samples = SampleRun(f, g, pen, cfg, culling, clip, walk, relax, profile, true);
                }
            }
            var gait = MeasureRunGait(samples, cfg, eps, clip.frameRate); int n = samples.Count - 1; var poses = samples.Select(x => x.pose).ToList();
            // loop: the last sample is the first pose again; the step that closes the loop must be no rougher than its neighbours
            int bones = poses[0].local.Length; float close = 0, seam = 0, inner = 0;
            for (int b = 0; b < bones; b++)
            {
                close = Mathf.Max(close, Quaternion.Angle(poses[0].local[b], poses[n].local[b]));
                var step = new float[n]; for (int k = 0; k < n; k++) step[k] = Quaternion.Angle(poses[k].local[b], poses[k + 1].local[b]);
                for (int k = 0; k < n; k++) { float change = Mathf.Abs(step[(k + 1) % n] - step[k]); if (k == n - 1) seam = Mathf.Max(seam, change); else inner = Mathf.Max(inner, change); }
            }
            float Max(Func<Pose, float> pick) => poses.Take(n).Max(pick); float Min(Func<Pose, float> pick) => poses.Take(n).Min(pick); float Mean(Func<Pose, float> pick) => poses.Take(n).Average(pick);
            bool penMeasured = !(selfTest.Contains("OUTSIDE") || selfTest.Contains("INSIDE"));
            // Blender reference: stance speed and the arm angles frame by frame (M3 for the run)
            float blenderSpeed = 0, armDelta = -1; string blenderNote = "";
            try
            {
                string path = string.IsNullOrEmpty(r.blenderRun) ? "" : Path.Combine(Repo, r.blenderRun);
                if (path != "" && File.Exists(path))
                {
                    var reference = JObject.Parse(File.ReadAllText(path)); blenderSpeed = reference["against_limits"]?["stance_speed_m_s"]?.Value<float>() ?? 0; armDelta = 0;
                    foreach (var (name, pick) in new (string, Func<Pose, int, float>)[] { ("abduction", (p, i) => p.abduction[i]), ("swing", (p, i) => p.swing[i]), ("elbow", (p, i) => p.elbow[i]) })
                        for (int side = 0; side < 2; side++)
                        {
                            var values = (reference["ref_run"]?[SideName[side]]?[name] as JArray)?.Select(x => x.Value<float>()).ToArray(); if (values == null) continue;
                            for (int k = 0; k < Mathf.Min(values.Length, poses.Count); k++) armDelta = Mathf.Max(armDelta, Mathf.Abs(Mathf.DeltaAngle(values[k], pick(poses[k], side))));
                        }
                }
                else blenderNote = "no Blender reference file";
            }
            catch (Exception e) { blenderNote = e.Message; }
            // ---- actors of the open ledger scene (read only)
            var actorRows = new JArray(); var actorLines = new List<string>();
            if (actorScene.IsValid())
                foreach (var (rig, monster, fixedClip) in Targets(actorScene, cfg).Where(x => x.monster.id == r.id))
                {
                    var enc = rig.GetComponent<PrologueEncounter>(); float speed = enc != null ? enc.Speed : 0; bool runs = speed > profile.EnterMetresPerSecond;
                    float walkRate = walkSpeed > 0 ? speed / walkSpeed : 0, runRate = speed / Mathf.Max(.1f, profile.RunMetresPerSecond);
                    float walkCadence = walkSeconds > 0 ? 120f * walkRate / walkSeconds : 0, runCadence = 120f * runRate / gait.seconds; float leg = MinPatrolLeg(enc);
                    bool fits = profile.Avatar == null || rig.Animator.avatar == profile.Avatar;
                    actorRows.Add(new JObject { ["id"] = enc != null ? enc.Id : "", ["object"] = rig.name, ["speed"] = Math.Round(speed, 3), ["gaitAtSpeed"] = runs ? "run" : "walk", ["walkRate"] = Math.Round(walkRate, 3), ["walkStepsPerMinute"] = Math.Round(walkCadence, 1),
                        ["runRate"] = Math.Round(runRate, 3), ["runStepsPerMinute"] = Math.Round(runCadence, 1), ["runAssigned"] = rig.Run == clip, ["runProfileAssigned"] = rig.RunProfile == profile, ["walkClip"] = fixedClip ? "walk_fix308" : "walking_man",
                        ["patrolLegMinMetres"] = Math.Round(leg, 2), ["runsOnPatrol"] = runs && !profile.WalkOnPatrol, ["avatarFitsProfile"] = fits });
                    string gaitText = !runs ? "걷기" : profile.WalkOnPatrol ? "추격 · 복귀 = 달리기, 순찰 = 걷기" : "달리기(순찰 포함)";
                    actorLines.Add("| " + (enc != null ? enc.Id : rig.name) + " | " + F(speed) + " | " + gaitText + " | " + F(walkRate) + "배속 · " + F1(walkCadence) + " | " + (runs ? F(runRate) + "배속 · " + F1(runCadence) : "-") + " | " + (leg < 0 ? "제자리" : F(leg) + " m") + " | " +
                        (rig.Run == clip && rig.RunProfile == profile ? "물림" : "안 물림") + (fits ? "" : " · **아바타 불일치**") + " |");
                }
            // ---- limits (TEST)
            var fails = new List<string>(); void Limit(bool ok, string text) { if (!ok) fails.Add(text); }
            float slideRatio = gait.valid ? Mathf.Abs(1 - gait.speed / Mathf.Max(.1f, profile.RunMetresPerSecond)) : -1, slideStep = gait.valid ? Mathf.Abs(profile.RunMetresPerSecond - gait.speed) * gait.stanceSeconds * 100 : -1;
            Limit(gait.valid, "디딤 구간을 찾지 못함(발바닥 높이 ≤ " + F(run.contactEpsCm) + " cm인 구간 없음)");
            if (gait.valid)
            {
                Limit(slideRatio <= lim.slideRatio && slideStep <= lim.slidePerStepCm, "미끄러짐 " + F1(slideRatio * 100) + " % · 한 걸음 " + F1(slideStep) + " cm (프로필 " + F(profile.RunMetresPerSecond) + " m/s, 잰 값 " + F(gait.speed) + " m/s; 한계 " + F1(lim.slideRatio * 100) + " % · " + F1(lim.slidePerStepCm) + " cm) → 프로필 값을 잰 값으로 다시 낸다");
                Limit(Mathf.Min(gait.soleMinCycle[0], gait.soleMinCycle[1]) * 100 >= lim.soleMinCm, "발바닥 최저 " + F1(Mathf.Min(gait.soleMinCycle[0], gait.soleMinCycle[1]) * 100) + " cm < " + F1(lim.soleMinCm));
                Limit(Mathf.Max(gait.soleMeanContact[0], gait.soleMeanContact[1]) * 100 <= lim.stanceSoleMeanMaxCm, "디딤 발바닥 평균 " + F1(Mathf.Max(gait.soleMeanContact[0], gait.soleMeanContact[1]) * 100) + " cm > " + F1(lim.stanceSoleMeanMaxCm));
                Limit(Mathf.Min(gait.kneeMinContact[0], gait.kneeMinContact[1]) >= lim.stanceKneeMinDeg, "디딤 무릎 굽힘 최소 " + F1(Mathf.Min(gait.kneeMinContact[0], gait.kneeMinContact[1])) + "° < " + F1(lim.stanceKneeMinDeg) + "°(무릎 잠김)");
                if (blenderSpeed > 0) Limit(Mathf.Abs(gait.speed / blenderSpeed - 1) <= lim.gaitVsBlenderRatio, "디딘 발 속도 Unity " + F(gait.speed) + " vs Blender " + F(blenderSpeed) + " (±" + F1(lim.gaitVsBlenderRatio * 100) + " %)");
            }
            if (penMeasured) for (int i = 0; i < 2; i++)
            {
                int side = i;
                Limit(Max(p => p.penFore[side]) * 100 <= lim.penForeHandCm, SideName[i] + " 손 · 아래팔 관통 " + F1(Max(p => p.penFore[side]) * 100) + " cm");
                Limit(Max(p => p.penUpper[side]) * 100 <= lim.penUpperCm, SideName[i] + " 소매 관통 " + F1(Max(p => p.penUpper[side]) * 100) + " cm > " + F1(lim.penUpperCm));
            }
            for (int i = 0; i < 2; i++) { int side = i; Limit(Min(p => p.sectionShoulder[side]) >= lim.sectionMinRatio && Min(p => p.sectionElbow[side]) >= lim.sectionMinRatio, SideName[i] + " 어깨 / 팔꿈치 단면 " + F(Min(p => p.sectionShoulder[side])) + " / " + F(Min(p => p.sectionElbow[side])) + " < " + F(lim.sectionMinRatio)); }
            Limit(close <= lim.loopCloseDeg, "루프 이음(첫 = 마지막) " + F(close) + "° > " + F(lim.loopCloseDeg) + "°");
            Limit(seam <= Mathf.Max(1f, inner) * lim.seamStepRatio, "루프를 닫는 걸음의 변화 " + F1(seam) + "°가 안쪽 최대 " + F1(inner) + "°의 " + F(lim.seamStepRatio) + "배를 넘음(속도 불연속)");
            Limit(poses.Sum(p => p.outsideCull) == 0, "컬링 구 밖 정점 " + poses.Sum(p => p.outsideCull));
            if (armDelta >= 0) Limit(armDelta <= lim.humanoidArmDeltaDeg, "Humanoid − Blender 팔 각도 최대 " + F1(armDelta) + "° > " + F1(lim.humanoidArmDeltaDeg) + "°");
            // ---- JSON
            JArray Two(Func<int, float> pick, float scale = 1f) => new JArray(Math.Round(pick(0) * scale, 3), Math.Round(pick(1) * scale, 3));
            var json = new JObject { ["schema"] = "308.enemyrun.unity.1", ["id"] = r.id, ["utc"] = DateTime.UtcNow.ToString("O"), ["clip"] = clip.name, ["seconds"] = Math.Round(clip.length, 4), ["samples"] = samples.Count,
                ["quality"] = QualitySettings.names[QualitySettings.GetQualityLevel()], ["skinWeights"] = QualitySettings.skinWeights.ToString(), ["penetrationSelfTest"] = selfTest, ["penetrationMeasured"] = penMeasured,
                ["walkClip"] = repairedWalk ? "walk_fix308" : "walking_man", ["walkStanceSpeed"] = Math.Round(walkSpeed, 4), ["relaxProfile"] = relax != null ? relax.name : "",
                ["profile"] = new JObject { ["enter"] = profile.EnterMetresPerSecond, ["exit"] = profile.ExitMetresPerSecond, ["enterHoldSeconds"] = profile.EnterHoldSeconds, ["walkOnPatrol"] = profile.WalkOnPatrol, ["blendSeconds"] = profile.BlendSeconds,
                    ["runMetresPerSecond"] = profile.RunMetresPerSecond, ["runArm"] = profile.RunArm, ["runElbow"] = profile.RunElbow, ["avatar"] = profile.Avatar != null ? AssetDatabase.GetAssetPath(profile.Avatar) : "" },
                ["gait"] = new JObject { ["stanceSpeed"] = Math.Round(gait.speed, 4), ["stanceSpeedPlateauRule"] = Math.Round(gait.plateauSpeed, 4), ["stanceSpeedPerFoot"] = Two(i => gait.stanceSpeed[i]), ["stride"] = Math.Round(gait.speed * gait.seconds, 4),
                    ["cycleSeconds"] = Math.Round(gait.seconds, 4), ["stepsPerMinuteAtClipRate"] = Math.Round(120.0 / Math.Max(.001, gait.seconds), 1), ["contactSamples"] = new JArray(gait.contactSamples[0], gait.contactSamples[1]),
                    ["flightSamples"] = gait.flightSamples, ["doubleSupportSamples"] = gait.doubleSamples, ["slideRatioAtProfile"] = Math.Round(slideRatio, 4), ["slidePerStepCm"] = Math.Round(slideStep, 2), ["slideInClipCm"] = Two(i => gait.slideInClip[i], 100f),
                    ["soleCycleMinCm"] = Two(i => gait.soleMinCycle[i], 100f), ["soleCycleMaxCm"] = Two(i => gait.soleMaxCycle[i], 100f), ["soleContactMeanCm"] = Two(i => gait.soleMeanContact[i], 100f),
                    ["kneeMinContactDeg"] = Two(i => float.IsInfinity(gait.kneeMinContact[i]) ? -1 : gait.kneeMinContact[i]), ["kneeMinDeg"] = Two(i => gait.kneeMin[i]), ["kneeMaxDeg"] = Two(i => gait.kneeMax[i]), ["kneeMaxStepDeg"] = Two(i => gait.kneeMaxStep[i]) },
                ["arms"] = new JObject { ["abductionMeanDeg"] = Two(i => Mean(p => p.abduction[i])), ["swingMinDeg"] = Two(i => Min(p => p.swing[i])), ["swingMaxDeg"] = Two(i => Max(p => p.swing[i])), ["elbowMinDeg"] = Two(i => Min(p => p.elbow[i])), ["elbowMaxDeg"] = Two(i => Max(p => p.elbow[i])),
                    ["wristForeAftCm"] = Two(i => Max(p => p.wrist[i].z - p.chest.z) - Min(p => p.wrist[i].z - p.chest.z), 100f), ["penForeHandMaxCm"] = Two(i => Max(p => p.penFore[i]), 100f), ["penSleeveMaxCm"] = Two(i => Max(p => p.penUpper[i]), 100f),
                    ["sectionShoulderMin"] = Two(i => Min(p => p.sectionShoulder[i])), ["sectionElbowMin"] = Two(i => Min(p => p.sectionElbow[i])), ["hyperextensionSamples"] = new JArray(poses.Count(p => p.flex[0] < 0), poses.Count(p => p.flex[1] < 0)) },
                ["loop"] = new JObject { ["firstLastDeg"] = Math.Round(close, 4), ["seamStepChangeDeg"] = Math.Round(seam, 2), ["innerStepChangeMaxDeg"] = Math.Round(inner, 2) },
                ["blender"] = new JObject { ["stanceSpeed"] = Math.Round(blenderSpeed, 4), ["armAngleMaxDeltaDeg"] = Math.Round(armDelta, 2), ["note"] = blenderNote },
                ["outsideCullingVertices"] = poses.Sum(p => p.outsideCull), ["actors"] = actorRows, ["limitMisses"] = new JArray(fails.ToArray()) };
            File.WriteAllText(Path.Combine(Out, "run_measure_" + r.id + ".json"), json.ToString(), new UTF8Encoding(false));
            // ---- Korean table
            string LR(Func<int, float> pick, float scale = 1f) => F1(pick(0) * scale) + " / " + F1(pick(1) * scale);
            md.AppendLine("## " + m.displayName + " (`" + r.id + "`) — " + clip.name + " · " + F(clip.length) + " s\n");
            md.AppendLine("관통 검사 자가 확인: " + selfTest + (penMeasured ? "" : " → **관통 미측정**") + " · 걷기 = " + (repairedWalk ? "수리본" : "원본") + "(디딘 발 속도 " + F(walkSpeed) + " m/s)\n");
            md.AppendLine("| 항목 | 달리기 | 한계 |"); md.AppendLine("|---|---|---|");
            md.AppendLine("| 디딘 발 속도(발바닥 접지 구간) | " + F(gait.speed) + " m/s (속도 고원 규칙으로는 " + F(gait.plateauSpeed) + ") | Blender " + F(blenderSpeed) + " ±" + F1(lim.gaitVsBlenderRatio * 100) + " % |");
            md.AppendLine("| 프로필 속도에서의 미끄러짐 | " + F1(slideRatio * 100) + " % · 한 걸음 " + F1(slideStep) + " cm (프로필 " + F(profile.RunMetresPerSecond) + " m/s) | ≤ " + F1(lim.slideRatio * 100) + " % · " + F1(lim.slidePerStepCm) + " cm |");
            md.AppendLine("| 주기 · 걸음 빠르기(1배속) | " + F(gait.seconds) + " s · 분당 " + F1(120f / Mathf.Max(.001f, gait.seconds)) + "걸음 · 보폭 " + F(gait.speed * gait.seconds) + " m | |");
            md.AppendLine("| 접지 표본(좌 / 우) · 체공 표본 | " + gait.contactSamples[0] + " / " + gait.contactSamples[1] + " · " + gait.flightSamples + " (전체 " + n + ") | 기록 |");
            md.AppendLine("| 발바닥 전 주기 최저(좌 / 우) | " + LR(i => gait.soleMinCycle[i], 100f) + " cm | ≥ " + F1(lim.soleMinCm) + " cm |");
            md.AppendLine("| 디딤 발바닥 평균(좌 / 우) | " + LR(i => gait.soleMeanContact[i], 100f) + " cm | ≤ " + F1(lim.stanceSoleMeanMaxCm) + " cm |");
            md.AppendLine("| 무릎 굽힘: 디딤 최소 · 전체 최대 · 한 표본 최대 변화 | " + LR(i => float.IsInfinity(gait.kneeMinContact[i]) ? -1 : gait.kneeMinContact[i]) + "° · " + LR(i => gait.kneeMax[i]) + "° · " + LR(i => gait.kneeMaxStep[i]) + "° | 디딤 최소 ≥ " + F1(lim.stanceKneeMinDeg) + "° |");
            md.AppendLine("| 손 · 아래팔 관통 / 소매 관통(보정 포함) | " + LR(i => Max(p => p.penFore[i]), 100f) + " cm / " + LR(i => Max(p => p.penUpper[i]), 100f) + " cm | " + F1(lim.penForeHandCm) + " / ≤ " + F1(lim.penUpperCm) + " cm |");
            md.AppendLine("| 어깨 · 팔꿈치 단면 최소(바인드 대비) | " + F(Min(p => p.sectionShoulder[0])) + " / " + F(Min(p => p.sectionShoulder[1])) + " · " + F(Min(p => p.sectionElbow[0])) + " / " + F(Min(p => p.sectionElbow[1])) + " | ≥ " + F(lim.sectionMinRatio) + " |");
            md.AppendLine("| 루프: 첫 = 마지막 · 닫는 걸음의 변화 / 안쪽 최대 | " + F(close) + "° · " + F1(seam) + "° / " + F1(inner) + "° | ≤ " + F(lim.loopCloseDeg) + "° · ≤ " + F(lim.seamStepRatio) + "배 |");
            md.AppendLine("| Humanoid − Blender 팔 각도 | " + (armDelta >= 0 ? F1(armDelta) + "°" : "기준 없음") + " | ≤ " + F1(lim.humanoidArmDeltaDeg) + "° |");
            md.AppendLine("| 컬링 구 밖 정점 | " + poses.Sum(p => p.outsideCull) + " | 0 |\n");
            if (actorLines.Count > 0)
            {
                md.AppendLine("프로필: " + ProfileLine(profile)); md.AppendLine();
                md.AppendLine("| 배우 | Speed | 전환 규칙의 걸음새 | 걷기로 틀면(배속 · 분당 걸음) | 달리기(배속 · 분당 걸음) | 가장 짧은 순찰 구간 | 달리기 칸 |"); md.AppendLine("|---|---|---|---|---|---|---|");
                foreach (var line in actorLines) md.AppendLine(line);
                md.AppendLine();
            }
            md.AppendLine(fails.Count == 0 ? "한계 밖 0건.\n" : "한계 밖 " + fails.Count + "건:\n" + string.Join("\n", fails.Select(x => "- " + x)) + "\n");
            return r.id + ": " + samples.Count + " samples | run stance speed " + F(gait.speed) + " m/s (plateau rule " + F(gait.plateauSpeed) + ", Blender " + F(blenderSpeed) + ") | profile " + F(profile.RunMetresPerSecond) + " -> slide " + F1(slideRatio * 100) + " % | flight samples " + gait.flightSamples +
                " | sole min " + F1(Mathf.Min(gait.soleMinCycle[0], gait.soleMinCycle[1]) * 100) + " cm | pen self-test: " + selfTest + " | actors " + actorRows.Count + " | limit misses " + fails.Count + (fails.Count > 0 ? " (" + string.Join("; ", fails.Take(3)) + (fails.Count > 3 ? "; ..." : "") + ")" : "");
        }

        // ------------------------------------------------------------------ stills
        [Serializable] sealed class RunStillRow { public string file = "", clip = ""; public float phase, yaw; }
        [Serializable] sealed class RunStillLog { public string id = "", utc = "", quality = ""; public List<RunStillRow> rows = new List<RunStillRow>(); }

        static string RunStillsCommand(Config cfg, RunConfig run, string id)
        {
            if (string.IsNullOrEmpty(id)) return "REFUSED stills needs one id";
            var picked = PickRun(run, id, out string why); if (picked == null) return "REFUSED " + why;
            if (picked.Length != 1) return "REFUSED stills takes one id per call";
            var r = picked[0]; var m = MonsterOf(cfg, r);
            if (run.stills.yaws.Length == 0 || run.stills.phases.Length == 0) return "REFUSED run config stills block is empty";
            var clip = RunClipOf(r); if (clip == null) return "REFUSED run clip not imported (clip-check)";
            var profile = RunProfileOf(r); if (profile == null) return "REFUSED run profile asset missing";
            var row = CompactFolklore298.ReadManifest().rows.FirstOrDefault(x => x.id == r.id); if (row == null) return "REFUSED no manifest row " + r.id;
            var idle = CompactFolklore298.Clip(row, "idle"); var walk = FixedClip(m) ?? OnlyClip(m.walkSource); var relax = Profile(m.relaxProfile);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CompactFolklore298.PrefabPath(r.id)); if (prefab == null) return "REFUSED no prefab " + CompactFolklore298.PrefabPath(r.id);
            string folder = Path.Combine(Out, "Stills", r.id, "run308"); Directory.CreateDirectory(folder);
            var log = new RunStillLog { id = r.id, utc = DateTime.UtcNow.ToString("O"), quality = QualitySettings.names[QualitySettings.GetQualityLevel()] };
            var preview = new PreviewRenderUtility(false, true);
            try
            {
                var host = EditorUtility.CreateGameObjectWithHideFlags(Marker + "runstill_" + r.id, HideFlags.HideAndDontSave);
                preview.AddSingleGO(host);
                if (host.scene == SceneManager.GetActiveScene()) { Object.DestroyImmediate(host); return "FAILED still host stayed in the open scene (destroyed; nothing rendered)"; }
                var visual = Object.Instantiate(prefab, host.transform); visual.name = "Folklore298_Visual";
                visual.transform.localPosition = Vector3.zero; visual.transform.localRotation = Quaternion.identity; visual.transform.localScale = Vector3.one;
                foreach (var t in host.GetComponentsInChildren<Transform>(true)) { t.gameObject.layer = 0; t.gameObject.hideFlags = HideFlags.HideAndDontSave; }
                foreach (var skin in host.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = false;
                var animator = visual.GetComponentInChildren<Animator>(true); if (animator == null || !animator.isHuman) return "FAILED prefab has no humanoid Animator";
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion = false; animator.runtimeAnimatorController = null; animator.fireEvents = false;
                var rig = host.AddComponent<EnemyRigMotion298>(); rig.enabled = false; rig.Animator = animator;
                CompactFolklore298.SamplePoseGraph298(animator, idle, 0);
                var frame = CompactFolklore298.MeshBoundsVerify308(host);   // the idle frame of the walk stills: run tiles line up with them
                foreach (float phase in run.stills.phases)
                {
                    CompactFolklore298.SamplePoseGraph298(animator, clip, Mathf.Clamp01(phase) * clip.length);
                    rig.Walk = walk; rig.ArmRelax = -1f; rig.ElbowRelax = -1f; rig.RelaxProfile = relax; rig.RunProfile = profile; rig.ApplyArmRelax(0f, 0f, 1f);
                    foreach (float yaw in run.stills.yaws)
                    {
                        string name = "run_p" + Mathf.RoundToInt(phase * 1000).ToString("000", CultureInfo.InvariantCulture) + "_y" + Mathf.RoundToInt(yaw).ToString("000", CultureInfo.InvariantCulture) + ".png";
                        CompactFolklore298.RenderPoseVerify308(preview, host, frame, yaw, Path.Combine(folder, name));
                        log.rows.Add(new RunStillRow { file = name, clip = clip.name, phase = phase, yaw = yaw });
                    }
                }
            }
            finally { preview.Cleanup(); }
            File.WriteAllText(Path.Combine(folder, "stills.json"), JsonUtility.ToJson(log, true), new UTF8Encoding(false));
            return "run stills " + r.id + ": " + log.rows.Count + " PNG -> " + folder + " (sheet: python Tools/Unity/Stage308_enemyrun/_Tools/run_stills_sheet308.py " + r.id + ")";
        }

        // ------------------------------------------------------------------ scenes (R5: run clip + switch data)
        [Serializable] sealed class RunActorRecord
        {
            public string indexPath = "", namePath = "", actorId = "", monster = "", runProfile = "", newRunProfile = "";
            public string[] changed = Array.Empty<string>();
            public ClipRef run = new ClipRef(), newRun = new ClipRef();
            public float speed, runThresholdMetresPerSecond, runMetresPerSecond, scale;
        }
        // cleared = actors that carried this stage's run clip / profile without being that species: both slots set to none by the apply.
        // A revert does not give them back (they were never that actor's to have).
        [Serializable] sealed class RunSceneRecord { public string scene = "", utc = "", backup = "", options = ""; public List<RunActorRecord> actors = new List<RunActorRecord>(), cleared = new List<RunActorRecord>(); }

        static string RunSceneOp(Config cfg, RunConfig run, string key, string ids, bool apply)
        {
            string path = ScenePath(key); if (path == null) return "REFUSED scene not in the #308 ledger: " + key + " (296 | 298 | main)";
            var picked = PickRun(run, ids, out string why); if (picked == null) return "REFUSED " + why;
            string record = RunRecordFile(path);
            if (apply && File.Exists(record)) return "ALREADY APPLIED " + path + " (record " + record + "); revert:" + key + " first to re-apply";
            string dirty = DirtyScene(); if (dirty != null) return "REFUSED dirty scene " + dirty + " (save or discard it first)";
            try
            {
                // preconditions on assets, before any scene is opened
                var clips = new Dictionary<string, AnimationClip>(); var profiles = new Dictionary<string, EnemyRunProfile308>(); var fixtureScale = new Dictionary<string, float>(); var walkSpeed = new Dictionary<string, float>(); var walkSeconds = new Dictionary<string, float>();
                foreach (var r in picked)
                {
                    string text = CheckRunClip(cfg, r, out bool ok); if (!ok) return "REFUSED run clip not ready (clip-check):\n  " + text;
                    var profile = RunProfileOf(r); if (profile == null) return "REFUSED " + r.id + ": run profile asset missing (" + r.runProfile + ")";
                    if (!profile.Usable) return "REFUSED " + r.id + ": run profile " + r.runProfile + " is not usable (enter > 0, 0 < exit <= enter, RunMetresPerSecond > 0)";
                    clips[r.id] = RunClipOf(r); profiles[r.id] = profile; var m = MonsterOf(cfg, r);
                    var walk = FixedClip(m) ?? OnlyClip(m.walkSource); if (walk == null) return "REFUSED " + r.id + ": no walk clip";
                    walkSpeed[r.id] = StanceSpeed(cfg, m, walk, out float scale, out float seconds); fixtureScale[r.id] = scale; walkSeconds[r.id] = seconds;
                }
                var scene = SceneManager.GetActiveScene().path == path ? SceneManager.GetActiveScene() : EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                // 2026-10-06: opening a scene (and the stance fixture) unloads unused assets - a profile or clip loaded above can be a
                // destroyed object here ("EnemyRunProfile308 has been destroyed"). Load again what was lost.
                foreach (var r in picked) { if (!profiles[r.id]) profiles[r.id] = RunProfileOf(r); if (!clips[r.id]) clips[r.id] = RunClipOf(r); }
                var targets = Targets(scene, cfg).Where(t => picked.Any(r => r.id == t.monster.id)).ToList();
                var foreign = ForeignRunCarriers(scene, cfg, run);
                if (targets.Count == 0 && foreign.Count == 0) return "NO TARGETS " + path + ": no " + string.Join("/", picked.Select(r => r.id)) + " actor outside protected trees (scene unchanged, nothing saved)";
                string optionLine = string.IsNullOrEmpty(ids) ? "" : "ids=" + ids;
                var sb = new StringBuilder(path + " (" + (apply ? "run apply" : "run plan") + (optionLine == "" ? "" : " " + optionLine) + ")\n");
                foreach (var r in picked) sb.AppendLine("  profile " + r.id + ": " + ProfileLine(profiles[r.id]));
                var rec = new RunSceneRecord { scene = path, utc = DateTime.UtcNow.ToString("O"), options = optionLine }; bool recordWritten = false, saved = false; int runners = 0, warns = 0;
                try
                {
                    foreach (var (rig, m, fixedClip) in targets)
                    {
                        // an actor of a picked species that carries ANOTHER species' run gets its own here; what it carried was never its own,
                        // so the record keeps "none" for it (a revert must not put the foreign clip back)
                        bool wasForeign = foreign.Any(f => f.rig == rig);
                        var enc = rig.GetComponent<PrologueEncounter>(); var clip = clips[m.id]; var profile = profiles[m.id];
                        var a = new RunActorRecord { indexPath = IndexPath(rig.transform), namePath = NamePath(rig.transform), actorId = enc != null ? enc.Id : "", monster = m.id, run = wasForeign ? new ClipRef() : Ref(rig.Run),
                            runProfile = !wasForeign && rig.RunProfile != null ? AssetDatabase.GetAssetPath(rig.RunProfile) : "", speed = enc != null ? enc.Speed : 0, runThresholdMetresPerSecond = rig.RunThresholdMetresPerSecond, runMetresPerSecond = rig.RunMetresPerSecond,
                            scale = rig.Animator.transform.lossyScale.y / Mathf.Max(1e-6f, fixtureScale[m.id]) };
                        var changed = new List<string>(); if (rig.Run != clip) changed.Add("Run"); if (rig.RunProfile != profile) changed.Add("RunProfile");
                        bool runs = a.speed > profile.EnterMetresPerSecond; if (runs) runners++;
                        float walkRate = walkSpeed[m.id] > 0 ? a.speed / (walkSpeed[m.id] * a.scale) : 0, runRate = a.speed / Mathf.Max(.1f, profile.RunMetresPerSecond);
                        float walkCadence = walkSeconds[m.id] > 0 ? 120f * walkRate / walkSeconds[m.id] : 0, runCadence = 120f * runRate / clip.length; float leg = MinPatrolLeg(enc);
                        // the profile names the avatar its clip is for: an actor with another avatar would carry the slots and never run
                        bool fits = profile.Avatar == null || rig.Animator.avatar == profile.Avatar;
                        // patrol: with WalkOnPatrol the actor keeps the walk there; without it a short leg switches the gait on every turn
                        bool shortLeg = runs && !profile.WalkOnPatrol && leg >= 0 && leg < run.limits.patrolLegMinMetres; if (shortLeg || !fits) warns++;
                        string patrol = !runs ? "" : profile.WalkOnPatrol ? " | patrol " + (leg < 0 ? "stands" : "legs min " + F(leg) + " m") + ": walk x" + F(walkRate) + " (WalkOnPatrol), run when it chases or returns"
                            : " | patrol " + (leg < 0 ? "stands" : "legs min " + F(leg) + " m: runs there too") + (shortLeg ? " WARN shorter than " + F(run.limits.patrolLegMinMetres) + " m: the gait would switch on every leg (set WalkOnPatrol in the profile)" : "");
                        string note = (Mathf.Abs(a.scale - 1f) > run.limits.actorScaleTolerance ? " WARN actor scale x" + F(a.scale) + ": the profile's RunMetresPerSecond is for the prefab scale (slide " + F1(Mathf.Abs(a.scale - 1f) * 100) + " %)" : "") +
                            (fixedClip ? "" : " NOTE walk is still walking_man (the enemyrig arms op is not applied here)") + (rig.RunThresholdMetresPerSecond > 0 ? " NOTE RunThresholdMetresPerSecond " + F(rig.RunThresholdMetresPerSecond) + " is ignored once a RunProfile is set" : "") +
                            (fits ? "" : " WARN Animator avatar is not the profile's avatar: this actor would never run (apply refuses)") +
                            (wasForeign ? " NOTE it carried another species' run (FOREIGN): replaced by its own, recorded as none" : "");
                        sb.AppendLine("  " + (a.actorId != "" ? a.actorId : a.namePath) + " [" + m.id + "] Speed " + F(a.speed) + " (unchanged) -> " + (runs ? "RUN x" + F(runRate) + " = " + F1(runCadence) + " steps/min (walk would need x" + F(walkRate) + " = " + F1(walkCadence) + ")" : "walk x" + F(walkRate) + " = " + F1(walkCadence) + " steps/min (below enter " + F(profile.EnterMetresPerSecond) + ")") +
                            patrol + " | Run " + (rig.Run != null ? rig.Run.name : "none") + " -> " + clip.name + " | RunProfile " + (rig.RunProfile != null ? rig.RunProfile.name : "none") + " -> " + profile.name + " | " + (changed.Count == 0 ? "unchanged" : string.Join(",", changed)) + note);
                        if (changed.Count == 0 || !apply) continue;
                        if (!fits) throw new InvalidOperationException(a.namePath + ": Animator avatar is not the avatar of " + profile.name + " (the run would never play)");
                        rig.Run = clip; rig.RunProfile = profile;
                        if (!rig.IsConfigured || !rig.HasRun) throw new InvalidOperationException(a.namePath + " EnemyRigMotion298 not configured / no run gate after apply");
                        a.changed = changed.ToArray(); a.newRun = Ref(clip); a.newRunProfile = AssetDatabase.GetAssetPath(profile);
                        EditorUtility.SetDirty(rig); rec.actors.Add(a);
                    }
                    foreach (var (rig, whyForeign) in foreign)
                    {
                        if (targets.Any(t => t.rig == rig)) continue;     // an actor of a picked species: it got its own run above
                        var enc = rig.GetComponent<PrologueEncounter>();
                        var a = new RunActorRecord { indexPath = IndexPath(rig.transform), namePath = NamePath(rig.transform), actorId = enc != null ? enc.Id : "", monster = "", run = Ref(rig.Run),
                            runProfile = rig.RunProfile != null ? AssetDatabase.GetAssetPath(rig.RunProfile) : "", speed = enc != null ? enc.Speed : 0, runThresholdMetresPerSecond = rig.RunThresholdMetresPerSecond, runMetresPerSecond = rig.RunMetresPerSecond, changed = new[] { "Run", "RunProfile" } };
                        sb.AppendLine("  FOREIGN " + CarrierName(rig) + ": " + whyForeign + " | Run " + (rig.Run != null ? rig.Run.name : "none") + " -> none | RunProfile " + (rig.RunProfile != null ? rig.RunProfile.name : "none") + " -> none" + (apply ? "" : " (apply clears it)"));
                        if (!apply) continue;
                        rig.Run = null; rig.RunProfile = null;
                        if (!rig.IsConfigured) throw new InvalidOperationException(a.namePath + " EnemyRigMotion298 not configured after clearing a foreign run");
                        EditorUtility.SetDirty(rig); rec.cleared.Add(a);
                    }
                    sb.AppendLine("  " + targets.Count + " actor(s): " + runners + " run at their Speed" + (picked.Any(r => profiles[r.id].WalkOnPatrol) ? " (WalkOnPatrol profiles: only while they chase or return)" : "") + ", " + (targets.Count - runners) + " stay on the walk; foreign run carriers " + foreign.Count + "; warnings " + warns);
                    if (!apply) return sb.Append("  plan only: nothing changed, nothing saved").ToString();
                    if (rec.actors.Count == 0 && rec.cleared.Count == 0) return sb.Append("  nothing changed (not saved)").ToString();
                    string records = Path.GetDirectoryName(record); Directory.CreateDirectory(records);
                    rec.backup = Path.Combine(records, Path.GetFileNameWithoutExtension(path) + "-pre308enemyrun-" + Stamp + ".unity"); File.Copy(ProjectFile(path), rec.backup, true);
                    // record first: a saved scene without a record could neither be reverted nor refused as ALREADY APPLIED
                    File.WriteAllText(record, JsonUtility.ToJson(rec, true)); recordWritten = true;
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("SaveScene returned false");
                    saved = true;
                    return sb.Append("  saved " + rec.actors.Count + " actor(s)" + (rec.cleared.Count > 0 ? ", cleared " + rec.cleared.Count + " foreign carrier(s)" : "") + "; record " + record + "; scene backup " + rec.backup).ToString();
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

        static string RunRevert(string key)
        {
            string path = ScenePath(key); if (path == null) return "REFUSED scene not in the #308 ledger: " + key;
            string record = RunRecordFile(path); if (!File.Exists(record)) return "REFUSED no run apply record " + record;
            string dirty = DirtyScene(); if (dirty != null) return "REFUSED dirty scene " + dirty;
            var rec = JsonUtility.FromJson<RunSceneRecord>(File.ReadAllText(record));
            var scene = SceneManager.GetActiveScene().path == path ? SceneManager.GetActiveScene() : EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EnemyRigMotion298>(true)).ToArray();
            var sb = new StringBuilder(path + " (run revert)\n");
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
                    rig.Run = Load(a.run); rig.RunProfile = string.IsNullOrEmpty(a.runProfile) ? null : AssetDatabase.LoadAssetAtPath<EnemyRunProfile308>(a.runProfile);
                    if (!rig.IsConfigured) throw new InvalidOperationException(a.namePath + " not configured after the run revert");
                    EditorUtility.SetDirty(rig); restored++;
                    sb.AppendLine("  " + (a.actorId != "" ? a.actorId : a.namePath) + ": restored " + string.Join(",", a.changed) + " (Run " + (string.IsNullOrEmpty(a.run.name) ? "none" : a.run.name) + ", RunProfile " + (a.runProfile == "" ? "none" : Path.GetFileNameWithoutExtension(a.runProfile)) + ")");
                }
                bool onlyCleared = rec.actors.Count == 0 && rec.cleared != null && rec.cleared.Count > 0;
                if (rec.cleared != null && rec.cleared.Count > 0) sb.AppendLine("  " + rec.cleared.Count + " foreign run carrier(s) cleared by the apply stay cleared: " + string.Join(", ", rec.cleared.Select(c => c.actorId != "" ? c.actorId : c.namePath)));
                if (restored == 0 && !onlyCleared) return sb.Append("  nothing restored (record kept, scene not saved)").ToString();
                if (restored > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("SaveScene returned false");
                }
                File.Move(record, record.Replace(".json", "-reverted-" + Stamp + ".json"));
                return sb.Append(restored > 0 ? "  saved; scene backup kept at " + rec.backup : "  nothing to restore (the apply only cleared foreign carriers); record closed, scene not saved").ToString();
            }
            catch (Exception e)
            {
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                return "FAILED " + path + " (run revert edits discarded, record kept): " + e.Message;
            }
        }

        static string RunAll(Config cfg, RunConfig run, string ids, bool apply)
        {
            var lines = new List<string>();
            foreach (var scene in apply ? Ledger : Ledger.Reverse())
            {
                if (!apply && !File.Exists(RunRecordFile(scene))) { lines.Add(scene + ": no run record (skipped)"); continue; }
                string r = apply ? RunSceneOp(cfg, run, scene, ids, true) : RunRevert(scene);
                lines.Add(r);
                if (r.StartsWith("REFUSED", StringComparison.Ordinal) || r.StartsWith("FAILED", StringComparison.Ordinal)) { lines.Add("stopped at " + scene); break; }
            }
            return string.Join("\n", lines);
        }
    }
}
