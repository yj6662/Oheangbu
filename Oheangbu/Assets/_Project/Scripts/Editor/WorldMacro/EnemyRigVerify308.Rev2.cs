using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World;
using Newtonsoft.Json.Linq;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 SPEC-ENEMY-RIG-VERIFY-308 "개정 2" (TEST). What the first editor round measured: the Humanoid conversion itself spreads the arms
    // and locks the elbows. Offline cause (Tools/Blender/EnemyAvatar308): the avatar's reference pose is the rig's A-pose, and Unity
    // builds every joint frame as if it were a T-pose; the forearm's bend plane then misses the upper arm by 47 - 55 degrees.
    //   candidate A   the four arm rows of the avatar reference pose become a T-pose of the SAME skeleton (avatar-dry | -apply | -revert)
    //   candidate B   the avatar stays; the repaired walk keeps part of the runtime arm relax as profile data (sweep finds the amounts)
    // Queue-safe like the rest of the tool: refusals are strings, nothing opens a dialog, no scene is opened or saved by these commands.
    public static partial class EnemyRigVerify308
    {
        // ------------------------------------------------------------------ candidate A data (avatarA308.json, built offline)
        [Serializable] sealed class AvatarRow { public string name = ""; public float x, y, z, w = 1, liveX, liveY, liveZ, liveW = 1; }
        [Serializable] sealed class AvatarMonster
        {
            public string id = "", rig = ""; public string[] dependents = Array.Empty<string>(); public AvatarRow[] rows = Array.Empty<AvatarRow>();
            public float[] elbowFloorLiveDeg = Array.Empty<float>(), elbowFloorADeg = Array.Empty<float>();
        }
        [Serializable] sealed class AvatarData { public string schema = ""; public AvatarMonster[] monsters = Array.Empty<AvatarMonster>(); }
        [Serializable] sealed class AvatarLog { public string id = "", utc = "", rig = ""; public List<string> assets = new List<string>(), metaBackups = new List<string>(); public AvatarRow[] rows = Array.Empty<AvatarRow>(); }

        static string AvatarRecord(string id) => Path.Combine(Out, "Records", "avatarA_" + id + ".json");
        // the profile the arms row (R3 + R4) binds: candidate B's, the source-clip one (clip=keep) or the walk-0 one
        static string ArmsProfilePath(Monster m, bool keepClip, bool useB) => useB ? m.relaxProfileB : keepClip ? m.relaxProfileOrigClip : m.relaxProfile;
        // review F4: the run clip of the run-clip stage, when that stage's FBX is imported. Candidate A re-converts it like every other
        // clip of the monster, so it is measured and photographed here on whichever avatar is imported (relax off).
        static AnimationClip RunClip(Monster m) => string.IsNullOrEmpty(m.runClip) || !File.Exists(ProjectFile(m.runClip)) ? null : ClipIn(m.runClip, m.runTake);

        static AvatarMonster AvatarRows(Config cfg, Monster m, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(cfg.avatar.rowsFile)) { why = "config avatar.rowsFile is empty"; return null; }
            string file = Path.Combine(Repo, cfg.avatar.rowsFile); if (!File.Exists(file)) { why = "no candidate A rows " + file; return null; }
            var data = JsonUtility.FromJson<AvatarData>(File.ReadAllText(file)); var row = data?.monsters?.FirstOrDefault(x => x.id == m.id);
            if (row == null || row.rows.Length == 0 || string.IsNullOrEmpty(row.rig)) { why = "candidate A rows have no entry for " + m.id; return null; }
            return row;
        }
        static Avatar RigAvatar(string rigAsset) => string.IsNullOrEmpty(rigAsset) ? null : AssetDatabase.LoadAllAssetsAtPath(rigAsset).OfType<Avatar>().FirstOrDefault();
        static Quaternion Live(AvatarRow r) => new Quaternion(r.liveX, r.liveY, r.liveZ, r.liveW);
        static Quaternion Target(AvatarRow r) => new Quaternion(r.x, r.y, r.z, r.w);

        // The smallest elbow angle the avatar can show: how far the upper arm lies outside the plane the forearm sweeps with its one bend
        // muscle. Read from the avatar's own joint frame (Avatar.GetPreRotation is internal: reflection, and "not readable" when absent).
        static bool ElbowFloor(Avatar avatar, out float left, out float right)
        {
            left = right = float.NaN; if (avatar == null || !avatar.isHuman) return false;
            var method = typeof(Avatar).GetMethod("GetPreRotation", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(int) }, null);
            if (method == null) return false;
            var description = avatar.humanDescription;
            float One(HumanBodyBones bone)
            {
                string boneName = description.human.FirstOrDefault(h => h.humanName == bone.ToString()).boneName; if (string.IsNullOrEmpty(boneName)) return float.NaN;
                int i = Array.FindIndex(description.skeleton, s => s.name == boneName); if (i < 0) return float.NaN;
                Vector3 direction = description.skeleton[i].position.normalized;   // the forearm joint seen from the upper arm = the upper-arm line
                var pre = (Quaternion)method.Invoke(avatar, new object[] { (int)bone });
                return Mathf.Asin(Mathf.Clamp01(Mathf.Abs(Vector3.Dot(direction, pre * Vector3.forward)))) * Mathf.Rad2Deg;
            }
            try { left = One(HumanBodyBones.LeftLowerArm); right = One(HumanBodyBones.RightLowerArm); }
            catch (Exception) { return false; }
            return !float.IsNaN(left) && !float.IsNaN(right);
        }

        // review N5: the same floor through the PUBLIC API, so that a wrong internal bone numbering cannot pass for "the cause does not
        // hold". A prefab copy in a preview scene, the human pose of that copy as it stands, then only the forearm stretch muscle is swept
        // over +-avatar.floorSweepMuscle (muscle units; 1 = the limit): the smallest and the largest elbow angle the avatar shows.
        static bool ElbowRangeBySweep(Config cfg, Monster m, float[] smallest, float[] largest)
        {
            using (var f = new Fixture(m))
            {
                var handler = new HumanPoseHandler(f.animator.avatar, f.animator.transform);
                try
                {
                    var pose = new HumanPose(); handler.GetHumanPose(ref pose);
                    int steps = Mathf.Max(8, cfg.avatar.floorSweepSteps); float span = Mathf.Max(1f, cfg.avatar.floorSweepMuscle);
                    for (int s = 0; s < 2; s++)
                    {
                        int muscle = Array.IndexOf(HumanTrait.MuscleName, (s == 0 ? "Left" : "Right") + " Forearm Stretch"); if (muscle < 0 || muscle >= pose.muscles.Length) return false;
                        var upper = f.animator.GetBoneTransform(s == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm); var lower = f.animator.GetBoneTransform(s == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                        var hand = f.animator.GetBoneTransform(s == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand); if (upper == null || lower == null || hand == null) return false;
                        float keep = pose.muscles[muscle]; smallest[s] = float.PositiveInfinity; largest[s] = float.NegativeInfinity;
                        for (int i = 0; i <= steps; i++)
                        {
                            pose.muscles[muscle] = Mathf.Lerp(-span, span, i / (float)steps); handler.SetHumanPose(ref pose);
                            float angle = Vector3.Angle(lower.position - upper.position, hand.position - lower.position);
                            smallest[s] = Mathf.Min(smallest[s], angle); largest[s] = Mathf.Max(largest[s], angle);
                        }
                        pose.muscles[muscle] = keep; handler.SetHumanPose(ref pose);
                    }
                    return true;
                }
                finally { handler.Dispose(); }
            }
        }

        // "live" (the rows the game has today) | "A" (candidate A rows) | "other" | "unknown" - read from the imported avatar, not from a record.
        static string AvatarState(Config cfg, Monster m, out string note)
        {
            var data = AvatarRows(cfg, m, out string why); if (data == null) { note = why; return "unknown"; }
            var avatar = RigAvatar(data.rig); if (avatar == null || !avatar.isHuman) { note = "no humanoid avatar in " + data.rig; return "unknown"; }
            var skeleton = avatar.humanDescription.skeleton; int live = 0, a = 0; float tolerance = cfg.avatar.stateToleranceDeg;
            foreach (var row in data.rows)
            {
                int i = Array.FindIndex(skeleton, s => s.name == row.name); if (i < 0) { note = "avatar reference pose has no row " + row.name; return "unknown"; }
                if (Quaternion.Angle(skeleton[i].rotation, Live(row)) < tolerance) live++;
                if (Quaternion.Angle(skeleton[i].rotation, Target(row)) < tolerance) a++;
            }
            string floor = ElbowFloor(avatar, out float l, out float r) ? "elbow floor " + F1(l) + " / " + F1(r) + " deg" : "elbow floor not readable";
            string state = live == data.rows.Length ? "live" : a == data.rows.Length ? "A" : "other";
            note = (state == "other" ? live + " row(s) live, " + a + " row(s) A of " + data.rows.Length : data.rows.Length + " arm rows = " + (state == "A" ? "candidate A (T-pose)" : "the rig's own pose")) + "; " + floor;
            return state;
        }

        // ------------------------------------------------------------------ review F3: every clip file's OWN copy of the reference rows
        static Avatar PrefabAvatar(Monster m)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CompactFolklore298.PrefabPath(m.id)); return prefab != null ? prefab.GetComponentInChildren<Animator>(true)?.avatar : null;
        }
        static bool SameRow(SkeletonBone x, SkeletonBone y, float tolerance)
        {
            float sign = Quaternion.Dot(x.rotation, y.rotation) < 0 ? -1f : 1f;   // q and -q are the same rotation
            return Mathf.Abs(x.rotation.x - sign * y.rotation.x) <= tolerance && Mathf.Abs(x.rotation.y - sign * y.rotation.y) <= tolerance && Mathf.Abs(x.rotation.z - sign * y.rotation.z) <= tolerance &&
                Mathf.Abs(x.rotation.w - sign * y.rotation.w) <= tolerance && (x.position - y.position).magnitude <= tolerance;
        }
        // A clip FBX that copies the rig's avatar (avatarSetup CopyFromOther) keeps its own copy of the avatar's reference pose in its
        // .meta, and its muscle clip is converted with THAT copy. sourceAvatar says only whose avatar it names. After candidate A is
        // applied or reverted, or after a stage brought a new clip file, every copy must equal the rig's rows.
        // -> one line per clip file whose copy differs; `seen` = clip files compared.
        static List<string> StaleClipRows(Monster m, float tolerance, out int seen)
        {
            seen = 0; var stale = new List<string>(); var avatar = PrefabAvatar(m);
            var rigImporter = avatar != null ? AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(avatar)) as ModelImporter : null;
            if (rigImporter == null) { stale.Add("the prefab's avatar does not come from a model importer"); return stale; }
            var rig = rigImporter.humanDescription.skeleton;
            foreach (string asset in AvatarDependents(m, avatar))
            {
                seen++; var clip = ((ModelImporter)AssetImporter.GetAtPath(asset)).humanDescription.skeleton; int differ = 0; string worstName = ""; float worst = -1f;
                if (clip == null || clip.Length == 0) { stale.Add(Path.GetFileName(asset) + " (holds no reference rows)"); continue; }
                foreach (var row in rig)
                {
                    int i = Array.FindIndex(clip, s => s.name == row.name);
                    if (i >= 0 && SameRow(clip[i], row, tolerance)) continue;
                    differ++; float angle = i < 0 ? 180f : Quaternion.Angle(clip[i].rotation, row.rotation);
                    if (angle > worst) { worst = angle; worstName = row.name + (i < 0 ? " missing" : " " + F1(angle) + " deg"); }
                }
                if (differ > 0) stale.Add(Path.GetFileName(asset) + " (" + differ + " row(s) differ, worst " + worstName + ")");
            }
            return stale;
        }
        static string ClipRowsLine(Config cfg, Monster m)
        {
            var stale = StaleClipRows(m, cfg.avatar.rowTolerance, out int seen);
            return stale.Count == 0 ? "clip rows ok (" + seen + " file(s) hold the rig's reference rows)" : "clip rows STALE: " + string.Join("; ", stale) + " -> avatar-sync:" + m.id;
        }

        static string AvatarFloor(Config cfg, string only)
        {
            var monsters = Pick(cfg, only, out string why); if (monsters == null) return "REFUSED " + why;
            var sb = new StringBuilder("avatar-floor (read only; the smallest elbow angle each avatar can show: from its own joint frames, and by a sweep of the forearm stretch muscle)\n");
            foreach (var m in monsters)
            {
                var data = AvatarRows(cfg, m, out string missing); if (data == null) { sb.AppendLine("  " + m.id + ": " + missing); continue; }
                string state = AvatarState(cfg, m, out string note); var avatar = RigAvatar(data.rig);
                string line = "  " + m.id + ": avatar '" + state + "' (" + note + ")";
                var expect = state == "A" ? data.elbowFloorADeg : state == "live" ? data.elbowFloorLiveDeg : null; bool hasExpect = expect != null && expect.Length == 2;
                bool frame = ElbowFloor(avatar, out float l, out float r); var smallest = new float[2]; var largest = new float[2]; bool swept;
                try { swept = ElbowRangeBySweep(cfg, m, smallest, largest); } catch (Exception e) { swept = false; line += " | muscle sweep failed: " + e.Message; }
                if (swept) line += " | muscle sweep: smallest elbow " + F1(smallest[0]) + " / " + F1(smallest[1]) + " deg, largest " + F1(largest[0]) + " / " + F1(largest[1]) + " deg";
                if (hasExpect)
                {
                    bool frameMatch = frame && Mathf.Abs(l - expect[0]) <= cfg.avatar.floorMaxDeg && Mathf.Abs(r - expect[1]) <= cfg.avatar.floorMaxDeg;
                    bool sweepMatch = swept && Mathf.Abs(smallest[0] - expect[0]) <= cfg.avatar.floorMaxDeg && Mathf.Abs(smallest[1] - expect[1]) <= cfg.avatar.floorMaxDeg;
                    line += " | offline value " + F1(expect[0]) + " / " + F1(expect[1]) + " deg -> joint frame " + (!frame ? "not readable" : frameMatch ? "MATCH" : "DIFFERENT") + ", muscle sweep " + (!swept ? "not run" : sweepMatch ? "MATCH" : "DIFFERENT");
                    // a DIFFERENT that only one of the two readings shows is a reading problem (internal bone numbering, or the sweep clamped), not the cause failing
                    if (frame && swept && frameMatch != sweepMatch) line += " | the two readings disagree: trust neither before the bone numbering (GetPreRotation index) and the sweep span (avatar.floorSweepMuscle) are checked";
                    else if (frame && swept && !frameMatch) line += " | both readings differ from the offline value: stop, the offline cause check does not describe this avatar";
                }
                sb.AppendLine(line); sb.AppendLine("    " + ClipRowsLine(cfg, m));
            }
            return sb.ToString().TrimEnd();
        }

        static List<string> AvatarDependents(Monster m, Avatar avatar)
        {
            var list = new List<string>(); string folder = Path.GetDirectoryName(m.walkSource).Replace('\\', '/');
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer != null && importer.avatarSetup == ModelImporterAvatarSetup.CopyFromOther && importer.sourceAvatar == avatar && !list.Contains(path)) list.Add(path);
            }
            list.Sort(StringComparer.Ordinal); return list;
        }

        static void SetRows(ModelImporter importer, AvatarRow[] rows, bool toA)
        {
            var description = importer.humanDescription; var skeleton = description.skeleton;
            foreach (var row in rows)
            {
                int i = Array.FindIndex(skeleton, s => s.name == row.name); if (i < 0) throw new InvalidOperationException("importer reference pose has no row " + row.name);
                var bone = skeleton[i]; bone.rotation = toA ? Target(row) : Live(row); skeleton[i] = bone;
            }
            description.skeleton = skeleton; importer.humanDescription = description;
        }

        // Hands the rig's CURRENT reference rows to one clip file and imports it again. Setting sourceAvatar is what the #298 import did
        // (Unity copies the avatar importer's human description with it); the description is also written explicitly so that the copy
        // does not hang on that side effect.
        static void CopyRowsIntoClip(string rigAsset, string clipAsset, Avatar avatar)
        {
            var rigImporter = AssetImporter.GetAtPath(rigAsset) as ModelImporter ?? throw new InvalidOperationException("no model importer at " + rigAsset);
            var clipImporter = AssetImporter.GetAtPath(clipAsset) as ModelImporter ?? throw new InvalidOperationException("no model importer at " + clipAsset);
            clipImporter.sourceAvatar = avatar; clipImporter.humanDescription = rigImporter.humanDescription; clipImporter.SaveAndReimport();
        }

        // What the arms of one clip do on the avatar that is imported now (relax off): the proof that a re-import changed the conversion.
        static string ArmLine(Config cfg, Monster m, AnimationClip clip)
        {
            if (clip == null) return "(clip missing)";
            using (var f = new Fixture(m))
            {
                Sample(f, clip, 0); var g = BuildGeo(f); var set = RunSet(f, g, null, cfg, null, "walk", "walk", clip, "off", clip, null, false);
                return "elbow " + F1(set.Min(p => p.elbow[0])) + "-" + F1(set.Max(p => p.elbow[0])) + " / " + F1(set.Min(p => p.elbow[1])) + "-" + F1(set.Max(p => p.elbow[1])) + " deg, spread " +
                    F1(set.Mean(p => p.abduction[0])) + " / " + F1(set.Mean(p => p.abduction[1])) + " deg";
            }
        }

        static string AvatarOp(Config cfg, string only, string mode)
        {
            if (mode != "dry" && string.IsNullOrEmpty(only)) return "REFUSED avatar-" + mode + " takes one id (one monster at a time)";
            var monsters = Pick(cfg, only, out string why); if (monsters == null) return "REFUSED " + why;
            if (mode != "dry" && monsters.Length != 1) return "REFUSED avatar-" + mode + " takes one id";
            // rev 3: level-* and bounds-* were measured on this avatar and the walk-0 profile of the scene rows needs it: they go back first
            if (mode == "revert") { string order = RevertOrderGuard(cfg, "avatar", monsters[0].id); if (order != null) return "REFUSED " + order; }
            if (EditorApplication.isCompiling) return "REFUSED the editor is compiling";
            if (mode != "dry") { string dirty = DirtyScene(); if (dirty != null) return "REFUSED dirty scene " + dirty + " (a re-import must not run over unsaved scene edits)"; }
            var sb = new StringBuilder("avatar-" + mode + " (candidate A: four arm rows of the avatar reference pose; no bone, position, weight, scene or prefab is written)\n");
            try
            {
                foreach (var m in monsters)
                {
                    var data = AvatarRows(cfg, m, out string missing); if (data == null) { sb.AppendLine("  " + m.id + ": REFUSED " + missing); continue; }
                    var importer = AssetImporter.GetAtPath(data.rig) as ModelImporter; var avatar = RigAvatar(data.rig);
                    if (importer == null || avatar == null) { sb.AppendLine("  " + m.id + ": REFUSED no model importer / avatar at " + data.rig); continue; }
                    if (importer.animationType != ModelImporterAnimationType.Human || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { sb.AppendLine("  " + m.id + ": REFUSED " + data.rig + " does not create its own humanoid avatar"); continue; }
                    string state = AvatarState(cfg, m, out string note); var dependents = AvatarDependents(m, avatar); string record = AvatarRecord(m.id);
                    var unlisted = dependents.Where(d => !data.dependents.Contains(d)).ToList(); var absent = data.dependents.Where(d => !dependents.Contains(d)).ToList();
                    sb.AppendLine("  " + m.id + ": avatar '" + state + "' (" + note + ") | clips that copy it: " + dependents.Count + (unlisted.Count > 0 ? " (also, not in the offline list: " + string.Join(", ", unlisted.Select(Path.GetFileName)) + ")" : "") +
                        (absent.Count > 0 ? " WARN the offline list names " + string.Join(", ", absent.Select(Path.GetFileName)) + " which does not copy this avatar" : ""));
                    sb.AppendLine("    " + ClipRowsLine(cfg, m));
                    if (mode == "sync")
                    {
                        // a stage that brings new clip files carries the reference rows of the day it was staged: copy the rig's current ones in
                        foreach (string asset in dependents) CopyRowsIntoClip(data.rig, asset, avatar);
                        var left = StaleClipRows(m, cfg.avatar.rowTolerance, out int synced);
                        sb.AppendLine("    synced " + dependents.Count + " clip file(s) to the avatar '" + state + "': " + string.Join(", ", dependents.Select(Path.GetFileName)) + " | walk now: " + ArmLine(cfg, m, FixedClip(m) ?? OnlyClip(m.walkSource)));
                        sb.AppendLine(left.Count == 0 ? "    clip rows ok after the sync (" + synced + " file(s))" : "    FAILED clip rows still STALE after the sync: " + string.Join("; ", left));
                        string dirtyAfterSync = DirtyScene(); if (dirtyAfterSync != null) sb.AppendLine("    WARN the re-import left " + dirtyAfterSync + " dirty: do not save it (reload the scene without saving)");
                        continue;
                    }
                    if (mode == "dry")
                    {
                        var skeleton = avatar.humanDescription.skeleton;
                        foreach (var row in data.rows) { int i = Array.FindIndex(skeleton, s => s.name == row.name); sb.AppendLine("    " + row.name + ": now " + (i < 0 ? "MISSING" : F1(Quaternion.Angle(skeleton[i].rotation, Live(row))) + " deg from live, " + F1(Quaternion.Angle(skeleton[i].rotation, Target(row))) + " deg from A")); }
                        sb.AppendLine("    apply would re-import " + data.rig + " + " + string.Join(", ", dependents.Select(Path.GetFileName)) + " | record " + (File.Exists(record) ? "EXISTS" : "none"));
                        continue;
                    }
                    bool toA = mode == "apply"; AvatarLog log;
                    if (toA)
                    {
                        if (File.Exists(record)) return sb.Append("  ALREADY APPLIED (record " + record + "); avatar-revert:" + m.id + " first").ToString();
                        if (state != "live") return sb.Append("  REFUSED the imported avatar is not the live one ('" + state + "'): nothing changed").ToString();
                        string folder = Path.Combine(Path.GetDirectoryName(record), "avatarA_" + m.id + "-" + Stamp); Directory.CreateDirectory(folder);
                        log = new AvatarLog { id = m.id, utc = DateTime.UtcNow.ToString("O"), rig = data.rig, rows = data.rows };
                        foreach (string asset in new[] { data.rig }.Concat(dependents))
                        { string backup = Path.Combine(folder, Path.GetFileName(asset) + ".meta"); File.Copy(ProjectFile(asset) + ".meta", backup, true); log.assets.Add(asset); log.metaBackups.Add(backup); }
                        File.WriteAllText(record, JsonUtility.ToJson(log, true));   // record before the first re-import
                    }
                    else
                    {
                        if (!File.Exists(record)) return sb.Append("  REFUSED no record " + record + " (candidate A was not applied by this tool)").ToString();
                        log = JsonUtility.FromJson<AvatarLog>(File.ReadAllText(record));
                    }
                    string before = ArmLine(cfg, m, FixedClip(m) ?? OnlyClip(m.walkSource));
                    try
                    {
                        SetRows(importer, toA ? data.rows : log.rows, toA); importer.SaveAndReimport();
                        avatar = RigAvatar(data.rig); if (avatar == null || !avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("the rig no longer yields a valid humanoid avatar");
                        foreach (string asset in log.assets.Skip(1).Concat(AvatarDependents(m, avatar)).Distinct().ToList()) CopyRowsIntoClip(data.rig, asset, avatar);
                        string after = AvatarState(cfg, m, out string noteAfter);
                        if (after != (toA ? "A" : "live")) throw new InvalidOperationException("after the re-import the avatar reads '" + after + "' (" + noteAfter + ")");
                        if (PrefabAvatar(m) != avatar) throw new InvalidOperationException("the prefab no longer points at the rig's avatar");
                        if (OnlyClip(m.walkSource) == null || ClipIn(m.motionSource, m.deathTake) == null || (!string.IsNullOrEmpty(m.walkFixed) && File.Exists(ProjectFile(m.walkFixed)) && FixedClip(m) == null))
                            throw new InvalidOperationException("a clip no longer loads after the re-import");
                        // review F3: the avatar is right - are the clips? Each clip file holds its own copy of the rows it is converted with.
                        var stale = StaleClipRows(m, cfg.avatar.rowTolerance, out int rowFiles);
                        if (stale.Count > 0) throw new InvalidOperationException("clip rows STALE after the re-import (a clip would be converted with the other pose): " + string.Join("; ", stale));
                        if (rowFiles < log.assets.Count - 1) throw new InvalidOperationException("only " + rowFiles + " of the " + (log.assets.Count - 1) + " recorded clip file(s) still copy the rig's avatar");
                        sb.AppendLine("    " + (toA ? "applied" : "reverted") + ": avatar '" + after + "' (" + noteAfter + ") | clip rows ok (" + rowFiles + " file(s) hold the same rows)");
                        sb.AppendLine("    walk on the avatar before: " + before); sb.AppendLine("    walk on the avatar after:  " + ArmLine(cfg, m, FixedClip(m) ?? OnlyClip(m.walkSource)));
                        if (toA) sb.AppendLine("    record " + record + " | .meta backups " + Path.GetDirectoryName(log.metaBackups[0]) + " | every clip of " + m.id + " plays differently now: measure, stills, then avatar-revert:" + m.id + " unless the user chose A");
                        else
                        {
                            // the rows go back through the importer (other importer values of these files are not touched); say whether the bytes came back too
                            int same = 0; var differs = new List<string>();
                            for (int i = 0; i < log.assets.Count && i < log.metaBackups.Count; i++)
                                if (File.Exists(log.metaBackups[i]) && File.ReadAllBytes(log.metaBackups[i]).SequenceEqual(File.ReadAllBytes(ProjectFile(log.assets[i]) + ".meta"))) same++; else differs.Add(Path.GetFileName(log.assets[i]) + ".meta");
                            File.Move(record, record.Replace(".json", "-reverted-" + Stamp + ".json"));
                            sb.AppendLine("    record closed | .meta byte-identical to the pre-apply backups: " + same + " of " + log.assets.Count +
                                (differs.Count > 0 ? " | NOT byte-identical: " + string.Join(", ", differs) + " (the rows are back - see the lines above; compare the file with its backup in " + Path.GetDirectoryName(log.metaBackups[0]) + ": these .meta files are not in git)" : ""));
                        }
                        string dirtyAfter = DirtyScene(); if (dirtyAfter != null) sb.AppendLine("    WARN the re-import left " + dirtyAfter + " dirty: do not save it (reload the scene without saving)");
                    }
                    catch (Exception e)
                    {
                        // put the .meta files back as they were before this command and import them again
                        var restored = new List<string>();
                        if (toA) for (int i = 0; i < log.assets.Count; i++) { File.Copy(log.metaBackups[i], ProjectFile(log.assets[i]) + ".meta", true); restored.Add(log.assets[i]); }
                        foreach (string asset in restored) AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                        if (toA && File.Exists(record)) File.Delete(record);
                        return sb.Append("  FAILED " + m.id + ": " + e.Message + (toA ? " | the " + restored.Count + " .meta file(s) were put back and re-imported; avatar now '" + AvatarState(cfg, m, out _) + "', " + ClipRowsLine(cfg, m)
                            : " | record kept; restore the .meta files from " + (log.metaBackups.Count > 0 ? Path.GetDirectoryName(log.metaBackups[0]) : "the backup"))).ToString();
                    }
                }
            }
            finally { Sweep(); }
            if (mode == "dry") sb.Append("  dry: nothing changed");
            return sb.ToString().TrimEnd();
        }

        // ------------------------------------------------------------------ candidate B: sweep of the walk relax (data search, nothing saved)
        sealed class SweepRow
        {
            public float arm, elbow, swingKeep, wristKeep, foreCm, sleeveCm, unsureForeCm, unsureSleeveCm, clearCm, moveCm; public readonly float[] spread = new float[2], elbowMin = new float[2], elbowMax = new float[2], swing = new float[2];
            public bool within, pareto; public float SpreadMax => Mathf.Max(spread[0], spread[1]);
            public float ElbowRangeDiff => Mathf.Abs((elbowMax[0] - elbowMin[0]) - (elbowMax[1] - elbowMin[1]));
        }

        static SweepRow SweepRowOf(SetResult set, SetResult off, float arm, float elbow)
        {
            var row = new SweepRow { arm = arm, elbow = elbow, swingKeep = float.PositiveInfinity, wristKeep = float.PositiveInfinity };
            for (int i = 0; i < 2; i++)
            {
                row.spread[i] = set.Mean(p => p.abduction[i]); row.elbowMin[i] = set.Min(p => p.elbow[i]); row.elbowMax[i] = set.Max(p => p.elbow[i]); row.swing[i] = set.Max(p => p.swing[i]) - set.Min(p => p.swing[i]);
                float swing0 = off.Max(p => p.swing[i]) - off.Min(p => p.swing[i]), wrist = set.Max(p => p.wrist[i].z - p.chest.z) - set.Min(p => p.wrist[i].z - p.chest.z), wrist0 = off.Max(p => p.wrist[i].z - p.chest.z) - off.Min(p => p.wrist[i].z - p.chest.z);
                row.swingKeep = Mathf.Min(row.swingKeep, row.swing[i] / Mathf.Max(.001f, swing0)); row.wristKeep = Mathf.Min(row.wristKeep, wrist / Mathf.Max(.001f, wrist0));
            }
            row.foreCm = set.Max(p => Mathf.Max(p.penFore[0], p.penFore[1])) * 100; row.sleeveCm = set.Max(p => Mathf.Max(p.penUpper[0], p.penUpper[1])) * 100;
            row.unsureForeCm = set.Max(p => Mathf.Max(p.unsureFore[0], p.unsureFore[1])) * 100; row.unsureSleeveCm = set.Max(p => Mathf.Max(p.unsureUpper[0], p.unsureUpper[1])) * 100;
            float clear = set.Min(p => Mathf.Min(p.clearHand[0], p.clearHand[1])); row.clearCm = float.IsInfinity(clear) ? -1 : clear * 100;
            // how far the wrists are from where the relax-off pose has them, sample by sample (the idle sweep ranks by this)
            for (int k = 0; k < Mathf.Min(set.poses.Count, off.poses.Count); k++) for (int i = 0; i < 2; i++) row.moveCm = Mathf.Max(row.moveCm, (set.poses[k].wrist[i] - off.poses[k].wrist[i]).magnitude * 100);
            return row;
        }
        static string Mode(float arm, float elbow) => arm.ToString("0.###", CultureInfo.InvariantCulture) + "," + elbow.ToString("0.###", CultureInfo.InvariantCulture);
        static string PenCell(SweepRow r) => F1(r.foreCm) + " · " + F1(r.sleeveCm) + (r.unsureForeCm > 0 || r.unsureSleeveCm > 0 ? " (미판정 " + F1(r.unsureForeCm) + " · " + F1(r.unsureSleeveCm) + ")" : "");
        static JObject SweepJson(SweepRow r) => new JObject { ["arm"] = r.arm, ["elbow"] = r.elbow, ["spreadMeanDeg"] = new JArray(Math.Round(r.spread[0], 2), Math.Round(r.spread[1], 2)), ["elbowMinDeg"] = new JArray(Math.Round(r.elbowMin[0], 2), Math.Round(r.elbowMin[1], 2)),
            ["elbowMaxDeg"] = new JArray(Math.Round(r.elbowMax[0], 2), Math.Round(r.elbowMax[1], 2)), ["elbowRangeDiffDeg"] = Math.Round(r.ElbowRangeDiff, 2), ["swingRangeDeg"] = new JArray(Math.Round(r.swing[0], 1), Math.Round(r.swing[1], 1)),
            ["swingKeep"] = float.IsInfinity(r.swingKeep) ? null : (JToken)Math.Round(r.swingKeep, 3), ["wristKeep"] = float.IsInfinity(r.wristKeep) ? null : (JToken)Math.Round(r.wristKeep, 3), ["foreHandCm"] = Math.Round(r.foreCm, 2), ["sleeveCm"] = Math.Round(r.sleeveCm, 2),
            ["unsureForeHandCm"] = Math.Round(r.unsureForeCm, 2), ["unsureSleeveCm"] = Math.Round(r.unsureSleeveCm, 2), ["handClearanceCm"] = Math.Round(r.clearCm, 2), ["wristMoveFromSourceCm"] = Math.Round(r.moveCm, 2), ["within"] = r.within, ["pareto"] = r.pareto };

        static string SweepRelax(Config cfg, string id, Dictionary<string, string> options)
        {
            if (string.IsNullOrEmpty(id)) return "REFUSED sweep needs one id";
            var picked = Pick(cfg, id, out string why); if (picked == null) return "REFUSED " + why; if (picked.Length != 1) return "REFUSED sweep takes one id per call";
            var m = picked[0];
            string which = options.TryGetValue("set", out string setText) ? setText : "walk"; if (which != "walk" && which != "idle") return "REFUSED sweep set takes walk or idle";
            if (which == "idle") return SweepIdle(cfg, m);
            var lim = cfg.limits;
            if (cfg.sweep.arm.Length == 0 || cfg.sweep.elbow.Length == 0) return "REFUSED config sweep grid is empty";
            string clipMode = options.TryGetValue("clip", out string c) ? c : "fixed"; if (clipMode != "fixed" && clipMode != "orig") return "REFUSED clip takes fixed or orig";
            var source = OnlyClip(m.walkSource); var walk = clipMode == "fixed" ? FixedClip(m) : source; if (walk == null || source == null) return "REFUSED walk clip not found (clip-check)";
            var idle = CompactFolklore298.Clip(CompactFolklore298.ReadManifest().rows.First(x => x.id == m.id), "idle");
            string avatarNow = AvatarState(cfg, m, out string avatarNote); var rows = new List<SweepRow>(); SweepRow today; string selfTest; bool trusted;
            using (var f = new Fixture(m))
            {
                Sample(f, idle, 0); var g = BuildGeo(f);
                using (var pen = new PenWorld(f, g, cfg.pen))
                {
                    pen.Update(CompactFolklore298.PhysicalSkinVertices298(f.skin)); pen.Check(g.hips.position, g.chest.position, g.hips.position + Vector3.right * cfg.pen.besideMetres); selfTest = pen.SelfTest; trusted = pen.Trusted;
                    var off = RunSet(f, g, pen, cfg, null, "off", "walk", walk, "off", walk, null, true);
                    var sourceOff = RunSet(f, g, pen, cfg, null, "source/off", "walk", source, "off", source, null, false);
                    today = SweepRowOf(RunSet(f, g, pen, cfg, null, "today", "walk", source, "legacy", source, null, true), sourceOff, f.rig.ArmRelaxAmount, f.rig.ElbowRelaxAmount);
                    foreach (float arm in cfg.sweep.arm) foreach (float elbow in cfg.sweep.elbow)
                        rows.Add(SweepRowOf(arm <= 0 && elbow <= 0 ? off : RunSet(f, g, pen, cfg, null, Mode(arm, elbow), "walk", walk, Mode(arm, elbow), walk, null, true), off, arm, elbow));
                }
            }
            // the same conditions as the "game pose against the design target" row of measure (spread, BOTH symmetry terms, penetration),
            // plus the clearance and the arm swing a relax must leave; an undetermined vertex (the two votes disagree) keeps a row out
            foreach (var r in rows)
                r.within = trusted && r.SpreadMax <= lim.walkAbductionMeanDeg && Mathf.Abs(r.spread[0] - r.spread[1]) <= lim.abductionDiffDeg && r.ElbowRangeDiff <= lim.elbowRangeDiffDeg &&
                    r.foreCm <= lim.penForeHandCm && r.sleeveCm <= lim.penUpperCm && r.unsureForeCm <= lim.penForeHandCm && r.unsureSleeveCm <= lim.penUpperCm &&
                    (r.clearCm < 0 || r.clearCm >= cfg.sweep.minClearanceCm) && r.wristKeep >= lim.swingKeepRatio;
            // Pareto: nothing else is at least as good on spread, penetration (undetermined counted), clearance AND arm swing, and better on one
            bool Better(SweepRow a, SweepRow b)
            {
                float pa = a.foreCm + a.sleeveCm + a.unsureForeCm + a.unsureSleeveCm, pb = b.foreCm + b.sleeveCm + b.unsureForeCm + b.unsureSleeveCm, ca = a.clearCm < 0 ? 99 : a.clearCm, cb = b.clearCm < 0 ? 99 : b.clearCm;
                return a.SpreadMax <= b.SpreadMax && pa <= pb && ca >= cb && a.wristKeep >= b.wristKeep && (a.SpreadMax < b.SpreadMax || pa < pb || ca > cb || a.wristKeep > b.wristKeep);
            }
            foreach (var r in rows) r.pareto = !rows.Any(o => o != r && Better(o, r));
            var pick = rows.Where(r => r.within).OrderByDescending(r => r.wristKeep).ThenBy(r => r.SpreadMax).FirstOrDefault();
            string Line(SweepRow r, string tag) => "| " + tag + " | " + F(r.arm) + " / " + F(r.elbow) + " | " + F1(r.spread[0]) + " / " + F1(r.spread[1]) + " | " + F1(r.elbowMin[0]) + "–" + F1(r.elbowMax[0]) + " / " + F1(r.elbowMin[1]) + "–" + F1(r.elbowMax[1]) +
                " (차 " + F1(r.ElbowRangeDiff) + ") | " + F1(r.swing[0]) + " / " + F1(r.swing[1]) + " (" + F1(r.wristKeep * 100) + " %) | " + PenCell(r) + " | " + (r.clearCm < 0 ? "> " + F1(cfg.pen.clearCap * 100) : F1(r.clearCm)) + " |";
            var md = new StringBuilder("# 걷기 팔 보정 훑기 (후보 B) — " + m.displayName + " (`" + m.id + "`)\n\n");
            md.AppendLine("- 시각(UTC): " + DateTime.UtcNow.ToString("O") + " · 클립: " + (clipMode == "fixed" ? "수리 걷기" : "원본 걷기") + " · 아바타: " + avatarNow + " (" + avatarNote + ")");
            md.AppendLine("- 관통 검사 자가 확인: " + selfTest + (trusted ? "" : " → **관통 · 간격 열은 믿지 말 것(한계 안 칸 없음으로 처리)**"));
            md.AppendLine("- 표의 값: 걷기 벌림 평균(좌 / 우, °) · 팔꿈치 범위(°)와 범위의 좌우 차 · 위팔 앞뒤 흔들림(°)과 손목 앞뒤 이동이 보정 끔 대비 남은 비율 · 손 · 아래팔 / 소매 관통(cm, 괄호 = 두 기준이 갈린 미판정 깊이) · 손과 몸 사이 가장 좁은 틈(cm)");
            md.AppendLine("- ★ = 파레토(벌림 · 관통 · 틈 · 흔들림에서 다른 칸에 지지 않음) · ○ = 한계 안(벌림 ≤ " + F1(lim.walkAbductionMeanDeg) + "°, 벌림 좌우 차 ≤ " + F1(lim.abductionDiffDeg) + "°, 팔꿈치 범위 좌우 차 ≤ " + F1(lim.elbowRangeDiffDeg) +
                "°, 손 관통 " + F1(lim.penForeHandCm) + ", 소매 ≤ " + F1(lim.penUpperCm) + " cm, 미판정 없음, 틈 ≥ " + F1(cfg.sweep.minClearanceCm) + " cm, 손목 흔들림 ≥ " + F1(lim.swingKeepRatio * 100) + " %)\n");
            md.AppendLine("| | 팔 / 팔꿈치 보정 | 벌림 | 팔꿈치 | 흔들림 | 관통 손 · 소매 | 틈 |"); md.AppendLine("|---|---|---|---|---|---|---|");
            md.AppendLine(Line(today, "지금 게임(원본 걷기 + 자동)"));
            foreach (var r in rows) md.AppendLine(Line(r, (r.pareto ? "★" : "") + (r.within ? "○" : "") + (r == pick ? " ← 제안" : "")));
            md.AppendLine("\n" + (pick != null ? "제안(한계 안에서 손목 흔들림이 가장 많이 남는 칸): 팔 " + F(pick.arm) + " / 팔꿈치 " + F(pick.elbow) + " — 프로필 `" + Path.GetFileName(m.relaxProfileB) + "`의 걷기 값과 비교해 그림으로 확인할 것(TEST)."
                : "한계 안에 드는 칸이 없다 — 파레토 칸(★) 가운데 그림으로 고르거나 후보 A로 간다."));
            string tag = (clipMode == "fixed" ? "" : "_orig") + (avatarNow == "live" ? "" : "_" + avatarNow); Directory.CreateDirectory(Out);
            File.WriteAllText(Path.Combine(Out, "sweep_" + m.id + tag + ".md"), md.ToString(), new UTF8Encoding(false));
            var json = new JObject { ["schema"] = "308.enemyrig.sweep.2", ["id"] = m.id, ["set"] = "walk", ["utc"] = DateTime.UtcNow.ToString("O"), ["clip"] = clipMode, ["avatar"] = avatarNow, ["penetrationSelfTest"] = selfTest, ["trusted"] = trusted,
                ["pick"] = pick != null ? new JArray(pick.arm, pick.elbow) : null };
            json["today"] = SweepJson(today); json["rows"] = new JArray(rows.Select(SweepJson).ToArray());
            File.WriteAllText(Path.Combine(Out, "sweep_" + m.id + tag + ".json"), json.ToString(), new UTF8Encoding(false));
            return "sweep " + m.id + " clip=" + clipMode + " avatar=" + avatarNow + ": " + rows.Count + " pairs, " + rows.Count(r => r.within) + " inside every limit, " + rows.Count(r => r.pareto) + " Pareto | " +
                (pick != null ? "pick " + F(pick.arm) + " / " + F(pick.elbow) + " (spread " + F1(pick.spread[0]) + " / " + F1(pick.spread[1]) + ", wrist swing kept " + F1(pick.wristKeep * 100) + " %)" : "no pair inside every limit") +
                " | today " + F1(today.spread[0]) + " / " + F1(today.spread[1]) + " | pen self-test: " + selfTest + " -> " + Path.Combine(Out, "sweep_" + m.id + tag + ".md");
        }

        // review F2: the idle relax on the avatar that is imported NOW. The source idle clip of a monster can carry a hand penetration of
        // its own (changgui: 3.5 / 2.0 cm offline); the live avatar hides it by deforming the arms, candidate A shows it. D308-23 keeps the
        // source idle POSE (the clip is not edited), so the answer is an idle relax amount: the row that moves the wrists least from the
        // source pose while no hand vertex is in the body, none is undetermined and the hand keeps sweep.minClearanceCm.
        static string SweepIdle(Config cfg, Monster m)
        {
            var lim = cfg.limits;
            if (cfg.sweep.idleArm.Length == 0 || cfg.sweep.idleElbow.Length == 0) return "REFUSED config sweep idle grid is empty";
            var source = OnlyClip(m.walkSource); if (source == null) return "REFUSED walk source clip not found";
            var idle = CompactFolklore298.Clip(CompactFolklore298.ReadManifest().rows.First(x => x.id == m.id), "idle"); var profile = Profile(m.relaxProfile);
            string avatarNow = AvatarState(cfg, m, out string avatarNote); var rows = new List<SweepRow>(); SweepRow current = null; string selfTest; bool trusted;
            int frames = Mathf.Max(1, Mathf.RoundToInt(idle.length * idle.frameRate)), every = Mathf.Max(1, cfg.sweep.idleEvery);
            using (var f = new Fixture(m))
            {
                Sample(f, idle, 0); var g = BuildGeo(f);
                using (var pen = new PenWorld(f, g, cfg.pen))
                {
                    pen.Update(CompactFolklore298.PhysicalSkinVertices298(f.skin)); pen.Check(g.hips.position, g.chest.position, g.hips.position + Vector3.right * cfg.pen.besideMetres); selfTest = pen.SelfTest; trusted = pen.Trusted;
                    SetResult Take(string mode)
                    {
                        var set = new SetResult { key = "idle/" + mode, role = "idle", clip = idle.name, relax = mode, frameRate = idle.frameRate, length = idle.length, penMeasured = pen.Trusted };
                        for (int i = 0; i <= frames; i += every)
                        {
                            float t = Mathf.Min(i / idle.frameRate, idle.length); Sample(f, idle, t); Relax(f, mode, source, null, true);
                            var p = Capture(f, g, pen, cfg, t, true, null); p.verts = null; set.poses.Add(p);
                        }
                        return set;
                    }
                    var off = Take("off");
                    foreach (float arm in cfg.sweep.idleArm) foreach (float elbow in cfg.sweep.idleElbow) rows.Add(SweepRowOf(arm <= 0 && elbow <= 0 ? off : Take(Mode(arm, elbow)), off, arm, elbow));
                    // the idle pair the walk-0 profile holds today, as its own row when the grid does not have it
                    if (profile != null) { current = rows.FirstOrDefault(r => Mathf.Approximately(r.arm, profile.IdleArm) && Mathf.Approximately(r.elbow, profile.IdleElbow)) ?? SweepRowOf(Take(Mode(profile.IdleArm, profile.IdleElbow)), off, profile.IdleArm, profile.IdleElbow); }
                }
            }
            bool Within(SweepRow r) => trusted && r.foreCm <= lim.penForeHandCm && r.unsureForeCm <= lim.penForeHandCm && (r.clearCm < 0 || r.clearCm >= cfg.sweep.minClearanceCm);
            foreach (var r in rows) r.within = Within(r);
            if (current != null) current.within = Within(current);
            var pick = rows.Where(r => r.within).OrderBy(r => r.moveCm).ThenBy(r => r.arm).ThenBy(r => r.elbow).FirstOrDefault();
            var sourceRow = rows.FirstOrDefault(r => r.arm <= 0 && r.elbow <= 0);
            string Line(SweepRow r, string tag) => "| " + tag + " | " + F(r.arm) + " / " + F(r.elbow) + " | " + F1(r.moveCm) + " | " + F1(r.spread[0]) + " / " + F1(r.spread[1]) + " | " + F1(r.elbowMin[0]) + "–" + F1(r.elbowMax[0]) + " / " + F1(r.elbowMin[1]) + "–" + F1(r.elbowMax[1]) +
                " | " + PenCell(r) + " | " + (r.clearCm < 0 ? "> " + F1(cfg.pen.clearCap * 100) : F1(r.clearCm)) + " |";
            var md = new StringBuilder("# 대기 팔 보정 훑기 — " + m.displayName + " (`" + m.id + "`)\n\n");
            md.AppendLine("- 시각(UTC): " + DateTime.UtcNow.ToString("O") + " · 클립: 원본 대기(고치지 않는다 — D308-23) · 아바타: " + avatarNow + " (" + avatarNote + ") · 표본: " + every + "프레임마다");
            md.AppendLine("- 관통 검사 자가 확인: " + selfTest + (trusted ? "" : " → **관통 · 간격 열은 믿지 말 것(한계 안 칸 없음으로 처리)**"));
            md.AppendLine("- 표의 값: 손목이 보정 끔 자세에서 가장 멀리 옮겨진 거리(cm) · 벌림 평균(좌 / 우, °) · 팔꿈치 범위(°) · 손 · 아래팔 / 소매 관통(cm, 괄호 = 미판정) · 손과 몸 사이 가장 좁은 틈(cm)");
            md.AppendLine("- ○ = 한계 안(손 관통 " + F1(lim.penForeHandCm) + " · 미판정 없음 · 틈 ≥ " + F1(cfg.sweep.minClearanceCm) + " cm). 제안 = 한계 안에서 원본 자세를 가장 덜 바꾸는 칸.\n");
            md.AppendLine("| | 팔 / 팔꿈치 보정 | 손목 이동 | 벌림 | 팔꿈치 | 관통 손 · 소매 | 틈 |"); md.AppendLine("|---|---|---|---|---|---|---|");
            if (current != null) md.AppendLine(Line(current, "지금 프로필의 대기 값" + (current.within ? " ○" : "")));
            foreach (var r in rows) md.AppendLine(Line(r, (r.within ? "○" : "") + (r == pick ? " ← 제안" : "") + (r == sourceRow ? " (원본 대기 그대로)" : "")));
            md.AppendLine("\n" + (pick != null ? "제안: 팔 " + F(pick.arm) + " / 팔꿈치 " + F(pick.elbow) + " — 손목 이동 " + F1(pick.moveCm) + " cm. 그림으로 확인할 것(TEST): `stills:" + m.id + "|set=idle,relax=" + Mode(pick.arm, pick.elbow) + (avatarNow == "A" ? ",avatar=A" : "") + "`."
                : "한계 안에 드는 칸이 없다 — 대기 보정만으로는 손 관통이 안 닫힌다(대기 클립을 고치는 것은 D308-23의 재결정이다)."));
            string tag = "_idle" + (avatarNow == "live" ? "" : "_" + avatarNow); Directory.CreateDirectory(Out);
            File.WriteAllText(Path.Combine(Out, "sweep_" + m.id + tag + ".md"), md.ToString(), new UTF8Encoding(false));
            var json = new JObject { ["schema"] = "308.enemyrig.sweep.2", ["id"] = m.id, ["set"] = "idle", ["utc"] = DateTime.UtcNow.ToString("O"), ["avatar"] = avatarNow, ["penetrationSelfTest"] = selfTest, ["trusted"] = trusted, ["sampleEvery"] = every,
                ["pick"] = pick != null ? new JArray(pick.arm, pick.elbow) : null, ["profileIdle"] = current != null ? SweepJson(current) : null };
            json["rows"] = new JArray(rows.Select(SweepJson).ToArray());
            File.WriteAllText(Path.Combine(Out, "sweep_" + m.id + tag + ".json"), json.ToString(), new UTF8Encoding(false));
            return "sweep " + m.id + " set=idle avatar=" + avatarNow + ": " + rows.Count + " pairs, " + rows.Count(r => r.within) + " inside the limits | " +
                (sourceRow != null ? "source idle (0 / 0): hand " + F1(sourceRow.foreCm) + " cm" + (sourceRow.unsureForeCm > 0 ? ", undetermined " + F1(sourceRow.unsureForeCm) + " cm" : "") + (sourceRow.within ? " = inside the limits" : " = NOT inside the limits") + " | " : "") +
                (current != null ? "profile idle " + F(current.arm) + " / " + F(current.elbow) + ": hand " + F1(current.foreCm) + " cm" + (current.within ? " inside" : " NOT inside") + " | " : "") +
                (pick != null ? "pick " + F(pick.arm) + " / " + F(pick.elbow) + " (wrists move " + F1(pick.moveCm) + " cm from the source pose)" : "no pair inside the limits") + " | pen self-test: " + selfTest + " -> " + Path.Combine(Out, "sweep_" + m.id + tag + ".md");
        }

        // (rev 3) level-dry | level-apply | level-revert live in EnemyRigVerify308.Rev3.cs now: every take the data lists, not the idle alone.
    }
}
