using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Newtonsoft.Json.Linq;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 SPEC-ENEMY-RIG-VERIFY-308 "개정 3" (TEST), D308-24: candidate A stays on for the three monsters; the round that applies it also
    //   R6  level-dry | level-apply | level-revert[:<id>]   every take the data lists (idle, attack, hit, stun) is moved by ITS OWN measured
    //       amount until its planted height (a statistic of the per-frame lowest vertex) is at the monster's TARGET: the middle of the
    //       planted heights of its reference takes (level.targetRoles: the repaired walk, the run clip - measured in the same fixture,
    //       never moved) + level.targetLowestCm. "On the ground like the repaired walk", not on a typed zero. The value written is the
    //       take's root height offset of the model importer (.meta `level:`); several passes, each measured, never an assumed unit.
    //   R7  bounds-dry | bounds-apply | bounds-revert[:<id>]   the culling sphere of the three prefabs from the farthest vertex of every
    //       clip (the M7 measure itself) plus a data margin: two lines of each prefab file. Scene copies follow through apply ... ops=bounds.
    //   gate  apply of the candidate A pairing asks for the monster's GO in gate_enemyrig3.json (gate.applyNeedsGo), see GateGuard.
    // Every refusal is a string, nothing opens a dialog, AssetDatabase.SaveAssets is never called. No Transform, collider or NavMesh
    // component of a scene or prefab object is written here (SPEC 개정 3 11절); the fixture lives in its own preview scene.
    public static partial class EnemyRigVerify308
    {
        const int ClassMonoBehaviour = 114, ClassSkinnedMeshRenderer = 137;   // Unity YAML class ids of the two documents bounds-apply edits
        static string LevelRecord => Path.Combine(Out, "Records", "level_takes.json");
        static string LegacyLevelRecord => Path.Combine(Out, "Records", "idle_level.json");   // revision 2's record (idle take only)
        static string BoundsRecord => Path.Combine(Out, "Records", "bounds_prefabs.json");
        static string F2(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        static double R2(float v) => Math.Round(v, 2);

        // ------------------------------------------------------------------ planted height of a take
        sealed class TakeStats { public float min, entry, exit, ends, mean, median, max; public int samples; }

        static TakeStats StatsOf(IList<float> lowestCm)
        {
            var s = new TakeStats { samples = lowestCm.Count }; if (lowestCm.Count == 0) return s;
            var sorted = lowestCm.OrderBy(v => v).ToArray(); int n = sorted.Length;
            s.min = sorted[0]; s.max = sorted[n - 1]; s.entry = lowestCm[0]; s.exit = lowestCm[n - 1]; s.ends = (s.entry + s.exit) / 2f; s.mean = lowestCm.Average();
            s.median = n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2f;
            return s;
        }
        static bool Stat(TakeStats s, string stat, out float value)
        {
            switch (stat)
            {
                case "min": value = s.min; return true;
                case "entry": value = s.entry; return true;
                case "exit": value = s.exit; return true;
                case "ends": value = s.ends; return true;
                case "mean": value = s.mean; return true;
                case "median": value = s.median; return true;
                default: value = 0; return false;
            }
        }
        static float[] LowestOf(SetResult set) => set.poses.Select(p => p.lowestVertex * 100f).ToArray();
        static float[] LowestOf(Fixture f, AnimationClip clip)
        {
            int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * clip.frameRate)); var list = new float[frames + 1];
            for (int i = 0; i <= frames; i++)
            {
                Sample(f, clip, Mathf.Min(i / clip.frameRate, clip.length)); float low = float.PositiveInfinity;
                foreach (var v in CompactFolklore298.PhysicalSkinVertices298(f.skin)) low = Mathf.Min(low, v.y);
                list[i] = low * 100f;
            }
            return list;
        }
        // A looping take is entered and left at any moment: its planted height stands for both ends of a transition.
        static bool RoleLoops(string role) => role == "idle" || role == "walk" || role == "walkfix" || role == "run";
        // The clip a level / bounds role names: the manifest roles, plus the repaired walk and the run clip of the run-clip stage.
        static AnimationClip RoleClip(Monster m, CompactFolklore298.ModelRow row, string role)
        {
            switch (role)
            {
                case "walk": return OnlyClip(m.walkSource);
                case "walkfix": return FixedClip(m);
                case "run": return RunClip(m);
                case "death": return ClipIn(m.motionSource, m.deathTake) ?? CompactFolklore298.Clip(row, "death", false);
                default: return CompactFolklore298.Clip(row, role, false);
            }
        }
        // from > to: what the lowest point of the figure does at the cut (centimetres, + = it rises)
        static float StepCm(LevelSettings set, Dictionary<string, TakeStats> stats, string from, string to)
        {
            Stat(stats[from], set.takes.First(t => t.role == from).stat, out float fromPlanted); Stat(stats[to], set.takes.First(t => t.role == to).stat, out float toPlanted);
            return (RoleLoops(to) ? toPlanted : stats[to].entry) - (RoleLoops(from) ? fromPlanted : stats[from].exit);
        }
        // The height every listed take of one monster is brought to. level.targetRoles names the takes whose own planted height IS the
        // ground for that monster (never moved); the target is the middle of those that are imported, plus level.targetLowestCm.
        // Without targetRoles the target is level.targetLowestCm above the prefab's ground plane. false = no reference take is imported.
        static bool LevelTarget(LevelSettings set, Func<string, TakeStats> statsOf, out float target, out string basis)
        {
            target = set.targetLowestCm; basis = "the prefab's ground plane";
            if (set.targetRoles == null || set.targetRoles.Length == 0) return true;
            var read = new List<float>(); var names = new List<string>();
            foreach (string role in set.targetRoles)
            {
                var s = statsOf(role); if (s == null || s.samples == 0) continue;
                Stat(s, set.takes.First(t => t.role == role).stat, out float planted); read.Add(planted); names.Add(role + " " + F2(planted));
            }
            if (read.Count == 0) { basis = "none of level.targetRoles (" + string.Join(", ", set.targetRoles) + ") is imported"; return false; }
            target = (read.Min() + read.Max()) / 2f + set.targetLowestCm; basis = "the middle of " + string.Join(", ", names) + " cm";
            return true;
        }
        static string LevelSettingsProblem(LevelSettings s)
        {
            if (s == null || s.takes == null || s.takes.Length == 0) return "config level.takes is empty";
            if (s.landCm <= 0 || s.needCm < 0 || s.maxStepCm <= 0 || s.maxPasses < 1) return "config level needs landCm > 0, needCm >= 0, maxStepCm > 0 and maxPasses >= 1";
            if (s.aimCm <= 0 || s.aimCm > s.landCm || s.aimCm > s.needCm) return "config level needs 0 < aimCm <= needCm and aimCm <= landCm";
            if (s.direction != "both" && s.direction != "down") return "config level.direction takes both or down";
            foreach (string role in s.targetRoles ?? Array.Empty<string>())
                if (s.takes.Count(t => t.role == role && !t.level) != 1) return "config level.targetRoles: '" + role + "' must be a listed take that is never moved (level = false)";
            foreach (var t in s.takes)
            {
                if (string.IsNullOrEmpty(t.role) || s.takes.Count(x => x.role == t.role) != 1) return "config level takes need unique roles";
                if (!Stat(new TakeStats(), t.stat, out _)) return "config level take " + t.role + ": unknown stat '" + t.stat + "' (min | entry | exit | ends | mean | median)";
                if (!t.level && string.IsNullOrEmpty(t.reason)) return "config level take " + t.role + " is listed as never moved without a reason";
            }
            foreach (string pair in s.pairs ?? Array.Empty<string>())
            {
                var ends = pair.Split('>');
                if (ends.Length != 2 || s.takes.All(t => t.role != ends[0]) || s.takes.All(t => t.role != ends[1])) return "config level pair '" + pair + "' does not name two listed takes (from>to)";
            }
            return null;
        }

        // ------------------------------------------------------------------ R6: take root heights
        [Serializable] sealed class LevelRow { public string id = "", asset = "", role = "", take = "", stat = "", metaBackup = ""; public float offsetBefore, offsetAfter, plantedBeforeCm, plantedAfterCm, minAfterCm, targetCm; public int passes; }
        [Serializable] sealed class LevelLog { public string utc = ""; public List<LevelRow> rows = new List<LevelRow>(); }
        sealed class TakeRead { public LevelTake take; public string asset = "", takeName = "", clipName = ""; public TakeStats stats; public float offset, planted; public bool hasEntry; }

        // Every listed take of one monster on the avatar that is imported now: stats of the lowest vertex, and the importer entry that
        // carries its root height offset. A reference take that is not imported (no run clip) is simply absent from the list.
        static List<TakeRead> ReadTakes(Config cfg, Monster m, CompactFolklore298.ModelRow row, out string why)
        {
            why = null; var list = new List<TakeRead>();
            using (var f = new Fixture(m))
                foreach (var take in cfg.level.takes)
                {
                    var clip = RoleClip(m, row, take.role);
                    if (clip == null) { if (take.level) { why = "no clip for the role '" + take.role + "'"; return null; } continue; }
                    var read = new TakeRead { take = take, asset = AssetDatabase.GetAssetPath(clip), clipName = clip.name, stats = StatsOf(LowestOf(f, clip)) };
                    Stat(read.stats, take.stat, out read.planted);
                    var importer = AssetImporter.GetAtPath(read.asset) as ModelImporter;
                    var entry = importer != null ? importer.clipAnimations.FirstOrDefault(t => t.name == clip.name || clip.name.EndsWith("|" + t.name, StringComparison.Ordinal)) : null;
                    if (take.level && entry == null) { why = "the importer of " + read.asset + " has no clip entry for " + clip.name; return null; }
                    if (entry != null) { read.hasEntry = true; read.takeName = entry.name; read.offset = entry.heightOffset; }
                    list.Add(read);
                }
            return list;
        }
        static void WriteOffsets(string asset, Dictionary<string, float> offsets)
        {
            var importer = AssetImporter.GetAtPath(asset) as ModelImporter ?? throw new InvalidOperationException("no model importer at " + asset);
            var takes = importer.clipAnimations; int written = 0;
            foreach (var take in takes) if (offsets.TryGetValue(take.name, out float value)) { take.heightOffset = value; written++; }
            if (written != offsets.Count) throw new InvalidOperationException(asset + ": " + (offsets.Count - written) + " take(s) not found in the importer");
            importer.clipAnimations = takes; importer.SaveAndReimport();
        }
        static string StepText(Config cfg, List<TakeRead> takes, out float worst, out List<string> over)
        {
            worst = 0; over = new List<string>(); var stats = takes.ToDictionary(t => t.take.role, t => t.stats); var parts = new List<string>();
            foreach (string pair in cfg.level.pairs)
            {
                var ends = pair.Split('>'); if (!stats.ContainsKey(ends[0]) || !stats.ContainsKey(ends[1])) continue;
                float step = StepCm(cfg.level, stats, ends[0], ends[1]); worst = Mathf.Max(worst, Mathf.Abs(step));
                parts.Add(pair + " " + (step >= 0 ? "+" : "") + F2(step)); if (Mathf.Abs(step) > cfg.level.maxStepCm) over.Add(pair + " " + F2(step) + " cm");
            }
            return parts.Count == 0 ? "no pair measurable" : string.Join(", ", parts);
        }
        static bool Needed(LevelSettings set, TakeRead t, float target) =>
            t.take.level && Mathf.Abs(t.planted - target) > set.needCm && (set.direction == "both" || t.planted > target);
        static TakeStats StatsOfRole(List<TakeRead> takes, string role) => takes.FirstOrDefault(t => t.take.role == role)?.stats;
        static string TakeLine(TakeRead t) =>
            t.take.role + " '" + t.takeName + "' " + t.take.stat + " " + F2(t.planted) + " cm (min " + F2(t.stats.min) + ", entry " + F2(t.stats.entry) + ", exit " + F2(t.stats.exit) + ", " + t.stats.samples + " samples)";

        // What a later step of the round was measured on: reverting an earlier one under it would leave that step describing another state.
        // Order of a full revert: scenes (revert-all, and the run-clip tool's) -> bounds-revert -> level-revert -> avatar-revert:<id>.
        // reverting = "bounds" | "level" (id null = every monster of the record, else that monster's rows alone) | "avatar" (always with the id).
        static string RevertOrderGuard(Config cfg, string reverting, string id = null)
        {
            var blockers = new List<string>(); string one = string.IsNullOrEmpty(id) ? "" : ":" + id;
            foreach (string scene in Ledger)
            {
                if (!File.Exists(RecordFile(scene))) continue;
                var rec = JsonUtility.FromJson<SceneRecord>(File.ReadAllText(RecordFile(scene)));
                // bounds: the scene's own copies were set to the prefab radius. avatar: the walk-0 profile on this monster's actors needs avatar A.
                bool hasBounds = rec.actors.Any(a => (one == "" || a.monster == id) && a.bounds != null && a.bounds.Length > 0);
                bool hasArms = rec.actors.Any(a => a.monster == id && a.changed.Contains("RelaxProfile") && cfg.monsters.Any(m => m.id == id && m.relaxProfile == a.newRelaxProfile));
                if (reverting == "bounds" ? hasBounds : reverting == "avatar" && hasArms) blockers.Add("revert:" + Path.GetFileNameWithoutExtension(scene) + " (scene record " + RecordFile(scene) + ")");
            }
            if (File.Exists(BoundsRecord))
            {
                bool mine = JsonUtility.FromJson<BoundsLog>(File.ReadAllText(BoundsRecord)).rows.Any(r => one == "" || r.id == id);
                if ((reverting == "level" && mine && cfg.bounds.requireLevel) || (reverting == "avatar" && mine && !string.IsNullOrEmpty(cfg.bounds.requireAvatar))) blockers.Add("bounds-revert" + one + " (the radius was measured on this state)");
            }
            if (reverting == "avatar" && !string.IsNullOrEmpty(cfg.level.requireAvatar) && File.Exists(LevelRecord) && JsonUtility.FromJson<LevelLog>(File.ReadAllText(LevelRecord)).rows.Any(r => r.id == id))
                blockers.Add("level-revert" + one + " (the take heights were measured on avatar " + cfg.level.requireAvatar + ")");
            return blockers.Count == 0 ? null : "later steps of the round are still applied; revert them first, in this order: " + string.Join(" -> ", blockers);
        }
        // A revert of one monster's rows leaves the other monsters' rows in the record, under the record's own utc (the gate compares
        // the measure's time with it); the rows taken out are kept beside it. The last rows out close the record as a whole revert does.
        static void CloseRecord<T>(string recordFile, string only, bool anyLeft, T kept, T taken)
        {
            if (string.IsNullOrEmpty(only) || !anyLeft) { File.Move(recordFile, recordFile.Replace(".json", "-reverted-" + Stamp + ".json")); return; }
            File.WriteAllText(recordFile.Replace(".json", "-reverted-" + Stamp + "-" + only + ".json"), JsonUtility.ToJson(taken, true));
            File.WriteAllText(recordFile, JsonUtility.ToJson(kept, true));
        }

        static string Level(Config cfg, string mode, string only = "")
        {
            string problem = LevelSettingsProblem(cfg.level); if (problem != null) return "REFUSED " + problem;
            if (EditorApplication.isCompiling) return "REFUSED the editor is compiling";
            if (File.Exists(LegacyLevelRecord)) return "REFUSED a revision-2 record is still live (" + LegacyLevelRecord + "): it was written by the previous tool version - put that stage back and level-revert there first";
            if (mode != "revert" && !string.IsNullOrEmpty(only)) return "REFUSED level-" + mode + " takes no id (every monster of the data; level-revert:<id> alone takes one)";
            var manifest = CompactFolklore298.ReadManifest(); var set = cfg.level;
            if (mode == "revert")
            {
                if (!File.Exists(LevelRecord)) return "REFUSED no record " + LevelRecord;
                if (!string.IsNullOrEmpty(only) && cfg.monsters.All(x => x.id != only)) return "REFUSED unknown monster id '" + only + "' (" + string.Join(" | ", cfg.monsters.Select(x => x.id)) + ")";
                var log = JsonUtility.FromJson<LevelLog>(File.ReadAllText(LevelRecord));
                var mine = log.rows.Where(r => string.IsNullOrEmpty(only) || r.id == only).ToList(); if (mine.Count == 0) return "REFUSED the record holds no take of '" + only + "' (" + LevelRecord + ")";
                string order = RevertOrderGuard(cfg, "level", only); if (order != null) return "REFUSED " + order;
                string dirtyNow = DirtyScene(); if (dirtyNow != null) return "REFUSED dirty scene " + dirtyNow;
                var sbr = new StringBuilder("level-revert" + (string.IsNullOrEmpty(only) ? "" : ":" + only) + " (" + mine.Count + " of " + log.rows.Count + " take(s))\n"); bool back = true;
                foreach (var group in mine.GroupBy(r => r.asset)) WriteOffsets(group.Key, group.ToDictionary(r => r.take, r => r.offsetBefore));
                foreach (var byMonster in mine.GroupBy(r => r.id))
                {
                    var m = cfg.monsters.FirstOrDefault(x => x.id == byMonster.Key); var row = manifest.rows.FirstOrDefault(r => r.id == byMonster.Key);
                    if (m == null || row == null) { sbr.AppendLine("  WARN " + byMonster.Key + " is not in the config / manifest (offsets written back, not re-measured)"); continue; }
                    var now = ReadTakes(cfg, m, row, out string why); if (now == null) { sbr.AppendLine("  " + m.id + ": offsets written back; could not re-measure (" + why + ")"); back = false; continue; }
                    foreach (var r in byMonster)
                    {
                        var t = now.FirstOrDefault(x => x.take.role == r.role); bool same = t != null && Mathf.Abs(t.planted - r.plantedBeforeCm) <= set.landCm; back &= same;
                        sbr.AppendLine("  " + r.id + ": " + r.role + " '" + r.take + "' root height offset " + F(r.offsetAfter) + " -> " + F(r.offsetBefore) + " | planted " + (t != null ? F2(t.planted) : "?") + " cm (before the apply " + F2(r.plantedBeforeCm) + ")" + (same ? "" : " WARN not back within " + F2(set.landCm) + " cm"));
                    }
                    sbr.AppendLine("    " + ClipRowsLine(cfg, m));
                }
                var kept = new LevelLog { utc = log.utc, rows = log.rows.Except(mine).ToList() };
                CloseRecord(LevelRecord, only, kept.rows.Count > 0, kept, new LevelLog { utc = log.utc, rows = mine });
                string dirtyAfterRevert = DirtyScene(); if (dirtyAfterRevert != null) sbr.AppendLine("  WARN the re-import left " + dirtyAfterRevert + " dirty: do not save it (reload the scene without saving)");
                string closed = kept.rows.Count > 0 && !string.IsNullOrEmpty(only) ? "record keeps " + kept.rows.Count + " take(s) of " + string.Join(", ", kept.rows.Select(r => r.id).Distinct()) : "record closed";
                return sbr.Append(back ? "  " + closed : "  " + closed + "; a take did not read back as before - compare the .meta with its backup").ToString();
            }
            if (mode == "apply" && File.Exists(LevelRecord)) return "ALREADY APPLIED (record " + LevelRecord + "); level-revert first";
            if (mode == "apply") { string dirty = DirtyScene(); if (dirty != null) return "REFUSED dirty scene " + dirty + " (a re-import must not run over unsaved scene edits)"; }
            string onto = set.targetRoles.Length == 0 ? F2(set.targetLowestCm) + " cm above the prefab ground" :
                "each monster's own " + string.Join(" / ", set.targetRoles) + " height" + (set.targetLowestCm != 0 ? " " + (set.targetLowestCm > 0 ? "+" : "") + F2(set.targetLowestCm) + " cm" : "");
            var sb = new StringBuilder("level-" + mode + " (planted height -> " + onto + "; moved when off by more than " + F2(set.needCm) + " cm, aimed within " + F2(set.aimCm) + " cm, accepted within " + F2(set.landCm) +
                " cm, direction " + set.direction + "; the value is each take's root height offset, measured pass by pass)\n");
            var done = new LevelLog { utc = DateTime.UtcNow.ToString("O") };
            try
            {
                foreach (var m in cfg.monsters)
                {
                    var row = manifest.rows.FirstOrDefault(r => r.id == m.id); if (row == null) { sb.AppendLine("  " + m.id + ": REFUSED no manifest row"); continue; }
                    string avatar = AvatarState(cfg, m, out _); bool avatarOk = string.IsNullOrEmpty(set.requireAvatar) || avatar == set.requireAvatar;
                    var takes = ReadTakes(cfg, m, row, out string why); if (takes == null) { sb.AppendLine("  " + m.id + ": REFUSED " + why); continue; }
                    if (!LevelTarget(set, role => StatsOfRole(takes, role), out float target, out string basis)) { sb.AppendLine("  " + m.id + ": REFUSED " + basis + " - there is no ground to level onto"); continue; }
                    string stepsBefore = StepText(cfg, takes, out float worstBefore, out _);
                    sb.AppendLine("  " + m.id + " (avatar '" + avatar + "'" + (avatarOk ? "" : ", level.requireAvatar = " + set.requireAvatar) + ") target " + F2(target) + " cm = " + basis);
                    foreach (var t in takes)
                        sb.AppendLine("    " + TakeLine(t) + (t.hasEntry ? " | offset " + F(t.offset) : "") + (!t.take.level ? " | never moved: " + t.take.reason : Needed(set, t, target) ? " | MOVE by " + F2(target - t.planted) + " cm" :
                            Mathf.Abs(t.planted - target) > set.needCm ? " | below the target and direction = down: left" : " | within " + F2(set.needCm) + " cm of the target: left"));
                    sb.AppendLine("    steps now: " + stepsBefore + " | worst " + F2(worstBefore) + " cm (limit " + F2(set.maxStepCm) + ")");
                    var moving = takes.Where(t => Needed(set, t, target)).ToList();
                    if (mode == "dry" || moving.Count == 0) { if (moving.Count == 0) sb.AppendLine("    nothing to move"); continue; }
                    if (!avatarOk) { sb.AppendLine("    REFUSED the imported avatar is '" + avatar + "': avatar-apply:" + m.id + " first (the takes are converted again when the avatar changes)"); continue; }
                    // ---- record and .meta backups before the first re-import
                    string records = Path.GetDirectoryName(LevelRecord); Directory.CreateDirectory(records); var rows = new List<LevelRow>(); var backups = new Dictionary<string, string>();
                    foreach (var t in moving)
                    {
                        if (!backups.TryGetValue(t.asset, out string backup))
                        { backup = Path.Combine(records, m.id + "-" + Path.GetFileName(t.asset) + ".meta.before-level-" + Stamp); File.Copy(ProjectFile(t.asset) + ".meta", backup, true); backups[t.asset] = backup; }
                        rows.Add(new LevelRow { id = m.id, asset = t.asset, role = t.take.role, take = t.takeName, stat = t.take.stat, metaBackup = backup, offsetBefore = t.offset, offsetAfter = t.offset, plantedBeforeCm = t.planted, targetCm = target });
                    }
                    done.rows.AddRange(rows); File.WriteAllText(LevelRecord, JsonUtility.ToJson(done, true));
                    // ---- passes: write every moving take's offset at once, re-import, measure again. First step assumes metres; every
                    // later one uses the slope the last two measurements gave (the offset's unit is the importer's: measured, not assumed).
                    // The passes go on while a take is further than aimCm from the target; what is accepted in the end is landCm.
                    // The target is the one read before the first write: the reference takes are not moved. Their height is read again
                    // after every pass, and a target that drifted fails the monster (something else than the takes' offsets moved).
                    var history = moving.ToDictionary(t => t.take.role, t => new List<Vector2> { new Vector2(t.offset, t.planted) }); var now = takes; int pass = 0; bool atAim = false;
                    try
                    {
                    while (pass < set.maxPasses && !atAim)
                    {
                        var next = new Dictionary<string, Dictionary<string, float>>();
                        foreach (var t in moving)
                        {
                            var h = history[t.take.role]; var last = h[h.Count - 1]; if (Mathf.Abs(last.y - target) <= set.aimCm) continue;
                            float slope = 100f;   // centimetres of height per unit of offset when the unit is the metre
                            if (h.Count > 1) { var prev = h[h.Count - 2]; float run = last.x - prev.x, rise = last.y - prev.y; if (Mathf.Abs(run) > 1e-7f && Mathf.Abs(rise) > 1e-4f) slope = rise / run; }
                            if (!next.TryGetValue(t.asset, out var byTake)) next[t.asset] = byTake = new Dictionary<string, float>();
                            byTake[t.takeName] = last.x + (target - last.y) / slope;
                        }
                        if (next.Count == 0) break;
                        pass++;
                        foreach (var kv in next) WriteOffsets(kv.Key, kv.Value);
                        now = ReadTakes(cfg, m, row, out why) ?? throw new InvalidOperationException(m.id + ": takes not readable after the re-import (" + why + ")");
                        if (!LevelTarget(set, role => StatsOfRole(now, role), out float targetNow, out _) || Mathf.Abs(targetNow - target) > set.aimCm)
                            throw new InvalidOperationException(m.id + ": the reference height moved with the re-import (" + F2(target) + " -> " + F2(targetNow) + " cm)");
                        atAim = true;
                        foreach (var t in moving) { var read = now.First(x => x.take.role == t.take.role); history[t.take.role].Add(new Vector2(read.offset, read.planted)); atAim &= Mathf.Abs(read.planted - target) <= set.aimCm; }
                    }
                    }
                    catch (Exception e)
                    {
                        // a pass broke (an importer or a clip that no longer loads): the recorded offsets go back; when even that fails the rows stay in the record for level-revert
                        string state;
                        try
                        {
                            foreach (var group in rows.GroupBy(r => r.asset)) WriteOffsets(group.Key, group.ToDictionary(r => r.take, r => r.offsetBefore));
                            foreach (var r in rows) done.rows.Remove(r);
                            state = "every offset of " + m.id + " was put back";
                        }
                        catch (Exception again) { state = "the offsets could NOT be put back (" + again.Message + "): the rows stay in the record - level-revert, or restore " + string.Join(", ", backups.Values); }
                        if (done.rows.Count > 0) File.WriteAllText(LevelRecord, JsonUtility.ToJson(done, true)); else if (File.Exists(LevelRecord)) File.Delete(LevelRecord);
                        sb.AppendLine("    FAILED in pass " + pass + ": " + e.Message + " | " + state); continue;
                    }
                    bool landed = moving.All(t => Mathf.Abs(history[t.take.role].Last().y - target) <= set.landCm);
                    if (!landed)
                    {
                        var reached = string.Join(", ", moving.Select(t => t.take.role + " " + F2(history[t.take.role].Last().y) + " cm"));
                        foreach (var group in rows.GroupBy(r => r.asset)) WriteOffsets(group.Key, group.ToDictionary(r => r.take, r => r.offsetBefore));
                        var restored = ReadTakes(cfg, m, row, out _); foreach (var r in rows) done.rows.Remove(r);
                        if (done.rows.Count > 0) File.WriteAllText(LevelRecord, JsonUtility.ToJson(done, true)); else File.Delete(LevelRecord);
                        sb.AppendLine("    NOT APPLIED: after " + pass + " pass(es) not every take landed within " + F2(set.landCm) + " cm of " + F2(target) + " cm (" + reached + "); every offset of " + m.id + " was put back" +
                            (restored != null ? " (" + string.Join(", ", moving.Select(t => t.take.role + " " + F2(restored.First(x => x.take.role == t.take.role).planted))) + " cm now)" : ""));
                        continue;
                    }
                    foreach (var r in rows) { var read = now.First(x => x.take.role == r.role); r.offsetAfter = read.offset; r.plantedAfterCm = read.planted; r.minAfterCm = read.stats.min; r.passes = pass; }
                    File.WriteAllText(LevelRecord, JsonUtility.ToJson(done, true));
                    foreach (var r in rows)
                    {
                        var read = now.First(x => x.take.role == r.role);
                        sb.AppendLine("    applied " + r.role + " '" + r.take + "': offset " + F(r.offsetBefore) + " -> " + F(r.offsetAfter) + " | " + r.stat + " " + F2(r.plantedBeforeCm) + " -> " + F2(r.plantedAfterCm) + " cm (target " + F2(target) + ", min " + F2(read.stats.min) + ", entry " +
                            F2(read.stats.entry) + ", exit " + F2(read.stats.exit) + ")" + (read.stats.min < -set.dipWarnCm ? " WARN dip: the lowest frame is " + F2(-read.stats.min) + " cm under the ground (look at it)" : ""));
                    }
                    string stepsAfter = StepText(cfg, now, out float worstAfter, out var over);
                    sb.AppendLine("    " + pass + " pass(es) | steps after: " + stepsAfter + " | worst " + F2(worstAfter) + " cm (limit " + F2(set.maxStepCm) + ")" + (over.Count > 0 ? " | STEP OVER: " + string.Join("; ", over) : " | every step inside the limit"));
                    sb.AppendLine("    meta backup " + string.Join(", ", backups.Values) + " | " + ClipRowsLine(cfg, m));
                }
            }
            finally { Sweep(); }
            if (mode == "dry") sb.Append("  dry: nothing changed");
            else
            {
                sb.Append(done.rows.Count == 0 ? "  nothing changed (no record written)" : "  record " + LevelRecord + " (" + done.rows.Count + " take(s))");
                string dirtyAfter = DirtyScene(); if (dirtyAfter != null) sb.Append("\n  WARN the re-import left " + dirtyAfter + " dirty: do not save it (reload the scene without saving)");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ two-bone skinning (the "2 bones" quality level, assumed rule)
        // The two largest of a vertex's weights, renormalised. Unity's own conversion is not public: SPEC 개정 3 8절 marks this as inferred.
        static BoneWeight TwoLargest(BoneWeight w)
        {
            var ranked = new[] { new KeyValuePair<float, int>(w.weight0, w.boneIndex0), new KeyValuePair<float, int>(w.weight1, w.boneIndex1), new KeyValuePair<float, int>(w.weight2, w.boneIndex2), new KeyValuePair<float, int>(w.weight3, w.boneIndex3) }
                .OrderByDescending(x => x.Key).ToArray();
            float sum = ranked[0].Key + ranked[1].Key; if (sum <= 0) return w;
            return new BoneWeight { boneIndex0 = ranked[0].Value, weight0 = ranked[0].Key / sum, boneIndex1 = ranked[1].Value, weight1 = ranked[1].Key / sum };
        }
        static Mesh TwoBoneMesh(Mesh source)
        {
            var mesh = Object.Instantiate(source); mesh.name = source.name + "_TwoBones308"; mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.boneWeights = source.boneWeights.Select(TwoLargest).ToArray(); return mesh;
        }
        static Vector3[] TwoBoneVertices(SkinnedMeshRenderer skin, Vector3[] bind, BoneWeight[] two, Matrix4x4[] bindposes)
        {
            var bones = skin.bones; var matrices = new Matrix4x4[bones.Length]; for (int i = 0; i < bones.Length; i++) matrices[i] = bones[i].localToWorldMatrix * bindposes[i];
            var result = new Vector3[bind.Length];
            for (int i = 0; i < bind.Length; i++)
            {
                var w = two[i]; Vector3 p = matrices[w.boneIndex0].MultiplyPoint3x4(bind[i]) * w.weight0;
                if (w.weight1 > 0) p += matrices[w.boneIndex1].MultiplyPoint3x4(bind[i]) * w.weight1;
                result[i] = p;
            }
            return result;
        }

        // ------------------------------------------------------------------ R7: culling bounds of the prefabs
        [Serializable] sealed class BoundsRow { public string id = "", prefab = "", backup = "", shaBefore = "", shaAfter = "", extentBefore = "", extentAfter = "", reachRole = ""; public float radiusBefore, radiusAfter, reachMax, margin; public long componentId, skinId; }
        [Serializable] sealed class BoundsLog { public string utc = ""; public List<BoundsRow> rows = new List<BoundsRow>(); }
        sealed class Reach { public string role = ""; public float fromRenderer, fromRootBone, both; public int outside, samples; }

        static string BoundsSettingsProblem(BoundsSettings s)
        {
            if (s == null || s.roles == null || s.roles.Length == 0) return "config bounds.roles is empty";
            if (s.marginMetres <= 0 || s.maxRadiusMetres <= 0 || s.equalToleranceMetres <= 0 || s.roundUpMetres < 0) return "config bounds needs marginMetres > 0, maxRadiusMetres > 0, equalToleranceMetres > 0 and roundUpMetres >= 0";
            return null;
        }
        // The culling component of a monster's prefab ASSET: exactly one entry, a cube about the origin (what #298 FitBounds wrote).
        static bool PrefabBounds(Config cfg, Monster m, out FolkloreCullingBounds298 component, out string why)
        {
            component = null; why = null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CompactFolklore298.PrefabPath(m.id)); if (prefab == null) { why = "no prefab " + CompactFolklore298.PrefabPath(m.id); return false; }
            component = prefab.GetComponent<FolkloreCullingBounds298>(); if (component == null || component.Entries == null) { why = "the prefab root has no FolkloreCullingBounds298"; return false; }
            if (component.Entries.Length != 1 || component.Entries[0] == null || component.Entries[0].Skin == null) { why = "the prefab's culling component must hold exactly one entry with a skin (it has " + component.Entries.Length + ")"; return false; }
            var b = component.Entries[0].LocalBounds; float t = cfg.bounds.equalToleranceMetres;
            if (b.center.magnitude > t || Mathf.Abs(b.extents.x - b.extents.y) > t || Mathf.Abs(b.extents.x - b.extents.z) > t) { why = "the prefab's culling bounds are not a cube about the origin (" + b + ")"; return false; }
            return true;
        }
        static bool SameBounds(Bounds a, Bounds b, float tolerance) => (a.center - b.center).magnitude <= tolerance && (a.extents - b.extents).magnitude <= tolerance;

        // The M7 quantity itself, per role: for every vertex the larger of its distance in the root bone's frame and in the renderer's
        // frame, over every frame of the clip; idle and walks with the relax off AND on (as measure poses them); four-weight skinning
        // and, when bounds.twoBoneSkin, the two-weight one. `outside` = vertex samples beyond `radius` (four-weight, as M7 counts).
        static List<Reach> ReachOf(Config cfg, Monster m, CompactFolklore298.ModelRow row, float radius, out float minScale)
        {
            var list = new List<Reach>();
            using (var f = new Fixture(m))
            {
                var skin = f.skin; var anchor = skin.rootBone != null ? skin.rootBone : skin.transform; var profile = Profile(m.relaxProfile); var source = OnlyClip(m.walkSource);
                minScale = Mathf.Max(1e-4f, Mathf.Min(Mathf.Abs(anchor.lossyScale.x), Mathf.Abs(anchor.lossyScale.y), Mathf.Abs(anchor.lossyScale.z), Mathf.Abs(skin.transform.lossyScale.x), Mathf.Abs(skin.transform.lossyScale.y), Mathf.Abs(skin.transform.lossyScale.z)));
                var bind = skin.sharedMesh.vertices; var two = cfg.bounds.twoBoneSkin ? skin.sharedMesh.boneWeights.Select(TwoLargest).ToArray() : null; var bindposes = skin.sharedMesh.bindposes;
                foreach (string role in cfg.bounds.roles)
                {
                    var clip = RoleClip(m, row, role); if (clip == null) continue;
                    var reach = new Reach { role = role }; int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * clip.frameRate));
                    var modes = new List<string> { "off" };
                    if (role == "idle" || role == "walk") modes.Add("legacy");
                    if ((role == "idle" || role == "walkfix") && profile != null) modes.Add("profile");
                    for (int i = 0; i <= frames; i++)
                        foreach (string mode in modes)
                        {
                            Sample(f, clip, Mathf.Min(i / clip.frameRate, clip.length));
                            if (mode != "off") Relax(f, mode, role == "walkfix" ? clip : source, mode == "profile" ? profile : null, role == "idle");
                            void Take(Vector3[] vertices, bool counted)
                            {
                                foreach (var v in vertices)
                                {
                                    float a = anchor.InverseTransformPoint(v).magnitude, b = skin.transform.InverseTransformPoint(v).magnitude, both = Mathf.Max(a, b);
                                    reach.fromRootBone = Mathf.Max(reach.fromRootBone, a); reach.fromRenderer = Mathf.Max(reach.fromRenderer, b); reach.both = Mathf.Max(reach.both, both);
                                    if (counted && mode == "off" && both > radius) reach.outside++;
                                }
                            }
                            Take(CompactFolklore298.PhysicalSkinVertices298(skin), true);
                            if (two != null) Take(TwoBoneVertices(skin, bind, two, bindposes), false);
                            reach.samples++;
                        }
                    list.Add(reach);
                }
            }
            return list;
        }
        static float WantedRadius(BoundsSettings set, List<Reach> reach, float minScale, float radiusNow)
        {
            float wanted = reach.Max(r => r.both) + set.marginMetres / minScale;
            if (set.roundUpMetres > 0) wanted = Mathf.Ceil(wanted / set.roundUpMetres - set.equalToleranceMetres) * set.roundUpMetres;
            return set.allowShrink ? wanted : Mathf.Max(wanted, radiusNow);
        }
        static string ExtentText(float radius) { string r = radius.ToString("0.####", CultureInfo.InvariantCulture); return "{x: " + r + ", y: " + r + ", z: " + r + "}"; }

        // One `m_Extent:` line of one YAML document of a prefab file. The document is `--- !u!<class> &<fileId>`; inside it `marker` must
        // occur exactly once, be followed by a `m_Center` line that is the origin and then by the `m_Extent` line. Only the value of that
        // one line is replaced; every other byte of the file stays. Throws (nothing is written by this method) when the shape differs.
        static string SetExtentLine(string text, int classId, long fileId, string marker, string value, out string before)
        {
            string head = "--- !u!" + classId.ToString(CultureInfo.InvariantCulture) + " &" + fileId.ToString(CultureInfo.InvariantCulture); int start = -1, seen = 0;
            for (int at = text.IndexOf(head, StringComparison.Ordinal); at >= 0; at = text.IndexOf(head, at + head.Length, StringComparison.Ordinal))
            {
                int after = at + head.Length; bool lineStart = at == 0 || text[at - 1] == '\n', lineEnd = after < text.Length && (text[after] == '\n' || text[after] == '\r');
                if (lineStart && lineEnd) { start = at; seen++; }
            }
            if (seen != 1) throw new InvalidOperationException("document '" + head + "' found " + seen + " time(s), not once");
            int end = text.IndexOf("\n--- !u!", start + head.Length, StringComparison.Ordinal); if (end < 0) end = text.Length;
            int mark = text.IndexOf(marker, start, end - start, StringComparison.Ordinal);
            if (mark < 0 || text.IndexOf(marker, mark + marker.Length, end - mark - marker.Length, StringComparison.Ordinal) >= 0) throw new InvalidOperationException("'" + marker + "' is not in '" + head + "' exactly once");
            int centre0 = text.IndexOf('\n', mark) + 1, centre1 = text.IndexOf('\n', centre0), extent0 = centre1 + 1, extent1 = text.IndexOf('\n', extent0);
            if (centre0 <= 0 || centre1 < 0 || extent1 < 0 || extent1 > end) throw new InvalidOperationException("no m_Center / m_Extent lines after '" + marker + "'");
            if (extent1 > extent0 && text[extent1 - 1] == '\r') extent1--;
            const string centre = "m_Center: {x: 0, y: 0, z: 0}", key = "m_Extent: ";
            if (text.Substring(centre0, centre1 - centre0).Trim() != centre) throw new InvalidOperationException("the bounds after '" + marker + "' are not centred on the origin: " + text.Substring(centre0, centre1 - centre0).Trim());
            string line = text.Substring(extent0, extent1 - extent0); int k = line.IndexOf(key, StringComparison.Ordinal);
            if (k < 0 || line.Substring(0, k).Trim().Length != 0) throw new InvalidOperationException("no m_Extent line after '" + marker + "': " + line.Trim());
            before = line.Substring(k + key.Length);
            return text.Substring(0, extent0 + k + key.Length) + value + text.Substring(extent1);
        }
        static bool ParseExtent(string text, out float x)
        {
            var match = Regex.Match(text ?? "", @"^\{x: ([-0-9.eE+]+), y: ([-0-9.eE+]+), z: ([-0-9.eE+]+)\}$"); x = 0;
            return match.Success && float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x) && match.Groups[1].Value == match.Groups[2].Value && match.Groups[2].Value == match.Groups[3].Value;
        }
        static int LinesThatDiffer(string a, string b, out bool onlyExtents)
        {
            var x = a.Split('\n'); var y = b.Split('\n'); onlyExtents = x.Length == y.Length; int differ = Math.Abs(x.Length - y.Length);
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++) if (x[i] != y[i]) { differ++; if (!x[i].TrimStart().StartsWith("m_Extent: ", StringComparison.Ordinal) || !y[i].TrimStart().StartsWith("m_Extent: ", StringComparison.Ordinal)) onlyExtents = false; }
            return differ;
        }
        static string BoundsStatusLine(Config cfg) =>
            string.Join(", ", cfg.monsters.Select(m => m.id + " " + (PrefabBounds(cfg, m, out var c, out string why) ? F(c.Entries[0].LocalBounds.extents.x) + " m" : "(" + why + ")")));

        static string BoundsOp(Config cfg, string mode, string only = "")
        {
            string problem = BoundsSettingsProblem(cfg.bounds); if (problem != null) return "REFUSED " + problem;
            if (EditorApplication.isCompiling) return "REFUSED the editor is compiling";
            if (mode != "revert" && !string.IsNullOrEmpty(only)) return "REFUSED bounds-" + mode + " takes no id (every monster of the data; bounds-revert:<id> alone takes one)";
            var set = cfg.bounds;
            if (mode == "revert")
            {
                if (!File.Exists(BoundsRecord)) return "REFUSED no record " + BoundsRecord;
                if (!string.IsNullOrEmpty(only) && cfg.monsters.All(x => x.id != only)) return "REFUSED unknown monster id '" + only + "' (" + string.Join(" | ", cfg.monsters.Select(x => x.id)) + ")";
                var log = JsonUtility.FromJson<BoundsLog>(File.ReadAllText(BoundsRecord));
                var mine = log.rows.Where(x => string.IsNullOrEmpty(only) || x.id == only).ToList(); if (mine.Count == 0) return "REFUSED the record holds no prefab of '" + only + "' (" + BoundsRecord + ")";
                string order = RevertOrderGuard(cfg, "bounds", only); if (order != null) return "REFUSED " + order;
                string dirtyNow = DirtyScene(); if (dirtyNow != null) return "REFUSED dirty scene " + dirtyNow;
                var sbr = new StringBuilder("bounds-revert" + (string.IsNullOrEmpty(only) ? "" : ":" + only) + " (" + mine.Count + " of " + log.rows.Count + " prefab(s))\n"); bool all = true;
                foreach (var r in mine)
                {
                    string file = ProjectFile(r.prefab);
                    try
                    {
                        string how;
                        if (Sha(file) == r.shaAfter && File.Exists(r.backup) && Sha(r.backup) == r.shaBefore) { File.Copy(r.backup, file, true); how = "the pre-apply file was put back (sha " + r.shaBefore.Substring(0, 12) + ")"; }
                        else
                        {
                            // the prefab changed after bounds-apply: only the two lines go back, whatever else changed stays
                            string text = File.ReadAllText(file, new UTF8Encoding(false));
                            text = SetExtentLine(text, ClassMonoBehaviour, r.componentId, "LocalBounds:", r.extentBefore, out _); text = SetExtentLine(text, ClassSkinnedMeshRenderer, r.skinId, "m_AABB:", r.extentBefore, out _);
                            File.WriteAllText(file, text, new UTF8Encoding(false)); how = "the prefab had changed since bounds-apply: only the two extent lines were put back (sha now " + Sha(file).Substring(0, 12) + ", before the apply " + r.shaBefore.Substring(0, 12) + ")";
                        }
                        AssetDatabase.ImportAsset(r.prefab, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                        var m = cfg.monsters.FirstOrDefault(x => x.id == r.id); bool ok = m != null && PrefabBounds(cfg, m, out var c, out _) && Mathf.Abs(c.Entries[0].LocalBounds.extents.x - r.radiusBefore) <= set.equalToleranceMetres &&
                            Mathf.Abs(c.Entries[0].Skin.localBounds.extents.x - r.radiusBefore) <= set.equalToleranceMetres;
                        all &= ok; sbr.AppendLine("  " + r.id + ": radius " + F(r.radiusAfter) + " -> " + F(r.radiusBefore) + " m | " + how + (ok ? "" : " | WARN the imported prefab does not read " + F(r.radiusBefore) + " m"));
                    }
                    catch (Exception e) { all = false; sbr.AppendLine("  " + r.id + ": FAILED " + e.Message + " (backup " + r.backup + ")"); }
                }
                if (!all) return sbr.Append("  record KEPT (a prefab did not come back): restore it from its backup by hand, then bounds-revert again").ToString();
                var kept = new BoundsLog { utc = log.utc, rows = log.rows.Except(mine).ToList() };
                CloseRecord(BoundsRecord, only, kept.rows.Count > 0, kept, new BoundsLog { utc = log.utc, rows = mine });
                string dirtyAfterRevert = DirtyScene(); if (dirtyAfterRevert != null) sbr.AppendLine("  WARN the prefab import left " + dirtyAfterRevert + " dirty: do not save it (reload the scene without saving)");
                return sbr.Append(kept.rows.Count > 0 && !string.IsNullOrEmpty(only) ? "  record keeps " + kept.rows.Count + " prefab(s): " + string.Join(", ", kept.rows.Select(x => x.id)) : "  record closed").ToString();
            }
            if (mode == "apply" && File.Exists(BoundsRecord)) return "ALREADY APPLIED (record " + BoundsRecord + "); bounds-revert first";
            if (mode == "apply") { string dirty = DirtyScene(); if (dirty != null) return "REFUSED dirty scene " + dirty + " (a prefab import must not run over unsaved scene edits)"; }
            var manifest = CompactFolklore298.ReadManifest(); var done = new BoundsLog { utc = DateTime.UtcNow.ToString("O") }; bool levelled = File.Exists(LevelRecord);
            var sb = new StringBuilder("bounds-" + mode + " (culling sphere = farthest vertex of every clip by the M7 measure + " + F(set.marginMetres) + " m, rounded up to " + F(set.roundUpMetres) + " m" + (set.allowShrink ? "" : ", never smaller than now") +
                "; take heights " + (levelled ? "levelled (record present)" : "NOT levelled") + ")\n");
            try
            {
                foreach (var m in cfg.monsters)
                {
                    var row = manifest.rows.FirstOrDefault(r => r.id == m.id); if (row == null) { sb.AppendLine("  " + m.id + ": REFUSED no manifest row"); continue; }
                    if (!PrefabBounds(cfg, m, out var component, out string why)) { sb.AppendLine("  " + m.id + ": REFUSED " + why); continue; }
                    string avatar = AvatarState(cfg, m, out _); float radiusNow = component.Entries[0].LocalBounds.extents.x;
                    var refusals = new List<string>();
                    if (!string.IsNullOrEmpty(set.requireAvatar) && avatar != set.requireAvatar) refusals.Add("the imported avatar is '" + avatar + "' (bounds.requireAvatar = " + set.requireAvatar + "): avatar-apply:" + m.id + " first");
                    if (set.requireLevel && !levelled) refusals.Add("no level record (bounds.requireLevel): level-apply first - lowering a take changes how far it reaches");
                    var stale = StaleClipRows(m, cfg.avatar.rowTolerance, out _); if (stale.Count > 0) refusals.Add("clip rows STALE (" + string.Join("; ", stale) + "): avatar-sync:" + m.id + " first");
                    var reach = ReachOf(cfg, m, row, radiusNow, out float minScale); if (reach.Count == 0) { sb.AppendLine("  " + m.id + ": REFUSED no clip of bounds.roles is imported"); continue; }
                    var far = reach.OrderByDescending(r => r.both).First(); float wanted = WantedRadius(set, reach, minScale, radiusNow);
                    sb.AppendLine("  " + m.id + " (avatar '" + avatar + "'): radius now " + F(radiusNow) + " m | farthest vertex " + F(far.both) + " m in '" + far.role + "' | wanted " + F(wanted) + " m" +
                        (Mathf.Abs(wanted - radiusNow) <= set.equalToleranceMetres ? " = now" : ""));
                    sb.AppendLine("    reach by clip (renderer frame / root bone frame; outside the radius now): " + string.Join(", ", reach.Select(r => r.role + " " + F2(r.fromRenderer) + " / " + F2(r.fromRootBone) + " m; " + r.outside)));
                    sb.AppendLine("    outside the radius now: " + reach.Sum(r => r.outside) + " vertex sample(s) | outside the wanted radius: " + reach.Count(r => r.both > wanted) + " clip(s)");
                    if (wanted > set.maxRadiusMetres) refusals.Add("the wanted radius " + F(wanted) + " m is over bounds.maxRadiusMetres " + F(set.maxRadiusMetres) + " m (a broken sample?)");
                    if (mode == "dry" || Mathf.Abs(wanted - radiusNow) <= set.equalToleranceMetres) { foreach (string r in refusals) sb.AppendLine("    apply would refuse: " + r); continue; }
                    if (refusals.Count > 0) { foreach (string r in refusals) sb.AppendLine("    REFUSED " + r); continue; }
                    // ---- the two lines of the prefab file
                    string prefabPath = CompactFolklore298.PrefabPath(m.id), file = ProjectFile(prefabPath), records = Path.GetDirectoryName(BoundsRecord); Directory.CreateDirectory(records);
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component, out _, out long componentId) || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component.Entries[0].Skin, out _, out long skinId))
                    { sb.AppendLine("    REFUSED the prefab's culling component / skin have no local file id"); continue; }
                    string original = File.ReadAllText(file, new UTF8Encoding(false)), value = ExtentText(wanted), edited, beforeA, beforeB;
                    // the text must write back to the very bytes it was read from (no byte-order mark, no undecodable byte), or the edit would touch more than two lines
                    if (!new UTF8Encoding(false).GetBytes(original).SequenceEqual(File.ReadAllBytes(file))) { sb.AppendLine("    REFUSED " + prefabPath + " is not plain UTF-8 text without a byte-order mark (nothing written)"); continue; }
                    try { edited = SetExtentLine(original, ClassMonoBehaviour, componentId, "LocalBounds:", value, out beforeA); edited = SetExtentLine(edited, ClassSkinnedMeshRenderer, skinId, "m_AABB:", value, out beforeB); }
                    catch (InvalidOperationException e) { sb.AppendLine("    REFUSED " + prefabPath + ": " + e.Message + " (nothing written)"); continue; }
                    if (beforeA != beforeB || !ParseExtent(beforeA, out float parsed) || Mathf.Abs(parsed - radiusNow) > set.equalToleranceMetres)
                    { sb.AppendLine("    REFUSED " + prefabPath + ": the two extent lines (" + beforeA + " | " + beforeB + ") are not the cube the importer reads (" + F(radiusNow) + " m); nothing written"); continue; }
                    int differ = LinesThatDiffer(original, edited, out bool onlyExtents);
                    if (differ != 2 || !onlyExtents) { sb.AppendLine("    REFUSED " + prefabPath + ": the edit would change " + differ + " line(s), not the two extent lines (nothing written)"); continue; }
                    var entry = new BoundsRow { id = m.id, prefab = prefabPath, backup = Path.Combine(records, Path.GetFileName(prefabPath) + ".before-bounds-" + Stamp), shaBefore = Sha(file), extentBefore = beforeA, extentAfter = value,
                        radiusBefore = radiusNow, radiusAfter = wanted, reachMax = far.both, reachRole = far.role, margin = set.marginMetres, componentId = componentId, skinId = skinId };
                    File.Copy(file, entry.backup, true); done.rows.Add(entry); File.WriteAllText(BoundsRecord, JsonUtility.ToJson(done, true));   // record before the write
                    try
                    {
                        File.WriteAllText(file, edited, new UTF8Encoding(false)); AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                        if (!PrefabBounds(cfg, m, out var after, out string whyAfter)) throw new InvalidOperationException("after the import: " + whyAfter);
                        if (Mathf.Abs(after.Entries[0].LocalBounds.extents.x - wanted) > set.equalToleranceMetres || !SameBounds(after.Entries[0].Skin.localBounds, after.Entries[0].LocalBounds, set.equalToleranceMetres))
                            throw new InvalidOperationException("after the import the component reads " + F(after.Entries[0].LocalBounds.extents.x) + " m and the renderer " + F(after.Entries[0].Skin.localBounds.extents.x) + " m, not " + F(wanted) + " m");
                        if (PrefabAvatar(m) == null) throw new InvalidOperationException("after the import the prefab has no avatar");
                        var check = ReachOf(cfg, m, row, wanted, out _); int outside = check.Sum(r => r.outside);
                        if (outside != 0) throw new InvalidOperationException(outside + " vertex sample(s) are still outside the new radius");
                        entry.shaAfter = Sha(file); File.WriteAllText(BoundsRecord, JsonUtility.ToJson(done, true));
                        sb.AppendLine("    applied: radius " + F(radiusNow) + " -> " + F(wanted) + " m | 2 lines of " + prefabPath + " (component &" + componentId + " LocalBounds, renderer &" + skinId + " m_AABB) | sha " + entry.shaBefore.Substring(0, 12) + " -> " +
                            entry.shaAfter.Substring(0, 12) + " | every clip inside: outside 0 | backup " + entry.backup);
                    }
                    catch (Exception e)
                    {
                        File.Copy(entry.backup, file, true); AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport); done.rows.Remove(entry);
                        if (done.rows.Count > 0) File.WriteAllText(BoundsRecord, JsonUtility.ToJson(done, true)); else File.Delete(BoundsRecord);
                        sb.AppendLine("    FAILED " + e.Message + " | the prefab file was put back from its backup and imported again (sha " + Sha(file).Substring(0, 12) + ")");
                    }
                }
            }
            finally { Sweep(); }
            if (mode == "dry") sb.Append("  dry: nothing changed");
            else
            {
                sb.Append(done.rows.Count == 0 ? "  nothing changed (no record written)" : "  record " + BoundsRecord + " (" + done.rows.Count + " prefab(s)) | scene copies: apply:<scene>|ops=...+bounds");
                string dirtyAfter = DirtyScene(); if (dirtyAfter != null) sb.Append("\n  WARN the prefab import left " + dirtyAfter + " dirty: do not save it (reload the scene without saving)");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ scene side of R7 and the candidate A guard (called by SceneOp / Revert)
        [Serializable] sealed class BoundsEdit { public string indexPath = "", namePath = ""; public int entry; public Bounds before, after; }
        sealed class PendingBounds { public FolkloreCullingBounds298 component; public int entry; public Bounds after; }

        // The walk-0 profile is the candidate A pairing (D308-24): on the live avatar it leaves the arms 40 degrees out (measured).
        static string ArmsAvatarGuard(Config cfg, Monster[] monsters)
        {
            if (string.IsNullOrEmpty(cfg.gate.armsNeedAvatar)) return null;
            var wrong = monsters.Select(m => new { m.id, state = AvatarState(cfg, m, out _) }).Where(x => x.state != cfg.gate.armsNeedAvatar).ToList();
            return wrong.Count == 0 ? null : "ops=arms with the walk-0 profile needs avatar '" + cfg.gate.armsNeedAvatar + "' (gate.armsNeedAvatar): " + string.Join(", ", wrong.Select(x => x.id + " reads '" + x.state + "'")) +
                " - avatar-apply:<id> first, or leave that monster out with ids=, or profile=B";
        }
        // AC-17: a scene APPLY of the candidate A pairing needs the monster's GO of the apply gate (Stage308_enemyrig3/_Tools/gate_enemyrig3.py,
        // run by the operator outside the editor), computed on the very A measure that is on disk now (sha256). plan is never refused by it.
        static string GateFile => Path.Combine(Out, "gate_enemyrig3.json");
        static string GateGuard(Config cfg, Monster[] monsters)
        {
            if (!cfg.gate.applyNeedsGo) return null;
            const string how = " - python Tools/Unity/Stage308_enemyrig3/_Tools/gate_enemyrig3.py, then ids=<the GO monsters joined by +>";
            if (!File.Exists(GateFile)) return "the apply gate has not been run (no " + GateFile + ", gate.applyNeedsGo)" + how;
            JObject gate; try { gate = JObject.Parse(File.ReadAllText(GateFile)); } catch (Exception e) { return "the apply gate file is unreadable (" + e.Message + ")" + how; }
            var bad = new List<string>();
            foreach (var m in monsters)
            {
                var row = gate["monsters"]?[m.id] as JObject; string verdict = (string)row?["verdict"], measured = (string)row?["measureSha256"], file = Path.Combine(Out, "measure_" + m.id + "_A.json");
                if (verdict != "GO") bad.Add(m.id + " is " + (verdict ?? "not in the gate file") + (row?["stoppedBy"] is JArray stops && stops.Count > 0 ? " (" + string.Join(", ", stops.Select(x => (string)x)) + ")" : ""));
                else if (!File.Exists(file) || string.IsNullOrEmpty(measured) || Sha(file) != measured) bad.Add(m.id + ": the gate was not computed on the A measure that is on disk now (run it again)");
            }
            return bad.Count == 0 ? null : "the apply gate does not say GO for every monster of this apply (gate.applyNeedsGo): " + string.Join("; ", bad) + how;
        }
        static string BoundsSceneGuard(Config cfg, Monster[] monsters)
        {
            string problem = BoundsSettingsProblem(cfg.bounds); if (problem != null) return problem;
            if (!File.Exists(BoundsRecord)) return "ops=bounds copies the PREFAB radius into the scene's own copies, and no bounds record is live (" + BoundsRecord + "): bounds-apply first, or drop bounds from ops";
            foreach (var m in monsters) if (!PrefabBounds(cfg, m, out _, out string why)) return m.id + ": " + why;
            return null;
        }
        // Culling components under one actor whose values are not the prefab's: copies the scene holds itself (a prefab instance without
        // an override already reads the prefab's value and is left alone).
        static List<PendingBounds> BoundsEdits(Config cfg, EnemyRigMotion298 rig, Monster m, out string note)
        {
            var list = new List<PendingBounds>(); PrefabBounds(cfg, m, out var prefab, out _);
            var want = prefab.Entries[0].LocalBounds; var mesh = prefab.Entries[0].Skin.sharedMesh; int same = 0; float tolerance = cfg.bounds.equalToleranceMetres; var old = new List<string>();
            foreach (var component in rig.GetComponentsInChildren<FolkloreCullingBounds298>(true))
                for (int i = 0; component.Entries != null && i < component.Entries.Length; i++)
                {
                    var entry = component.Entries[i]; if (entry == null || entry.Skin == null || entry.Skin.sharedMesh != mesh) continue;
                    if (SameBounds(entry.LocalBounds, want, tolerance) && SameBounds(entry.Skin.localBounds, want, tolerance)) { same++; continue; }
                    list.Add(new PendingBounds { component = component, entry = i, after = want }); old.Add(F(entry.LocalBounds.extents.x));
                }
            note = " | bounds " + (same + list.Count == 0 ? "no culling component of this mesh" : list.Count > 0 ? list.Count + " own cop" + (list.Count == 1 ? "y " : "ies ") + string.Join("/", old) + " -> " + F(want.extents.x) + " m" : "reads the prefab's " + F(want.extents.x) + " m");
            return list;
        }
        static BoundsEdit[] ApplyBoundsEdits(List<PendingBounds> edits)
        {
            var done = new List<BoundsEdit>();
            foreach (var edit in edits)
            {
                var entry = edit.component.Entries[edit.entry];
                done.Add(new BoundsEdit { indexPath = IndexPath(edit.component.transform), namePath = NamePath(edit.component.transform), entry = edit.entry, before = entry.LocalBounds, after = edit.after });
                entry.LocalBounds = edit.after; edit.component.Apply();
                EditorUtility.SetDirty(edit.component); EditorUtility.SetDirty(entry.Skin);
                if (PrefabUtility.IsPartOfPrefabInstance(edit.component)) { PrefabUtility.RecordPrefabInstancePropertyModifications(edit.component); PrefabUtility.RecordPrefabInstancePropertyModifications(entry.Skin); }
            }
            return done.ToArray();
        }
        static int RevertBoundsEdits(Scene scene, BoundsEdit[] edits, StringBuilder sb)
        {
            int restored = 0;
            foreach (var edit in edits ?? Array.Empty<BoundsEdit>())
            {
                var t = Resolve(scene, edit.indexPath); var component = t != null && NamePath(t) == edit.namePath ? t.GetComponent<FolkloreCullingBounds298>() : null;
                if (component == null || component.Entries == null || edit.entry >= component.Entries.Length || component.Entries[edit.entry] == null || component.Entries[edit.entry].Skin == null)
                { sb.AppendLine("  WARN culling component not found: " + edit.namePath + " - bounds not restored"); continue; }
                var entry = component.Entries[edit.entry]; entry.LocalBounds = edit.before; component.Apply();
                EditorUtility.SetDirty(component); EditorUtility.SetDirty(entry.Skin);
                if (PrefabUtility.IsPartOfPrefabInstance(component)) { PrefabUtility.RecordPrefabInstancePropertyModifications(component); PrefabUtility.RecordPrefabInstancePropertyModifications(entry.Skin); }
                restored++; sb.AppendLine("    bounds " + edit.namePath + ": " + F(edit.after.extents.x) + " -> " + F(edit.before.extents.x) + " m");
            }
            return restored;
        }

        // ------------------------------------------------------------------ what measure writes for the gate (gate_enemyrig3.py)
        static JObject StatsJson(float[] lowestCm)
        {
            var s = StatsOf(lowestCm);
            return new JObject { ["min"] = R2(s.min), ["entry"] = R2(s.entry), ["exit"] = R2(s.exit), ["ends"] = R2(s.ends), ["mean"] = R2(s.mean), ["median"] = R2(s.median), ["max"] = R2(s.max), ["samples"] = s.samples };
        }
        static JObject CullingJson(Config cfg, Monster m, List<SetResult> sets)
        {
            var o = new JObject { ["boundsRecord"] = File.Exists(BoundsRecord) ? "applied" : "not applied", ["outsideTotal"] = sets.Sum(s => s.poses.Sum(p => p.outsideCull)) };
            if (PrefabBounds(cfg, m, out var component, out string why)) o["radiusM"] = Math.Round(component.Entries[0].LocalBounds.extents.x, 4); else o["problem"] = why;
            var far = sets.Where(s => s.poses.Count > 0).OrderByDescending(s => s.Max(p => p.cullReach)).FirstOrDefault();
            if (far != null) { o["reachMaxM"] = Math.Round(far.Max(p => p.cullReach), 4); o["reachMaxSet"] = far.key; }
            return o;
        }
        static JObject LevelJson(Config cfg, Func<string, SetResult> get)
        {
            var set = cfg.level; var o = new JObject { ["record"] = File.Exists(LevelRecord) ? "applied" : "not applied", ["targetOffsetCm"] = set.targetLowestCm, ["targetRoles"] = new JArray(set.targetRoles ?? Array.Empty<string>()),
                ["needCm"] = set.needCm, ["aimCm"] = set.aimCm, ["landCm"] = set.landCm, ["maxStepCm"] = set.maxStepCm };
            if (LevelSettingsProblem(set) is string problem) { o["problem"] = problem; return o; }
            var stats = new Dictionary<string, TakeStats>(); var takes = new JObject();
            foreach (var take in set.takes)
            {
                var result = get(take.role + "/off"); if (result == null || result.poses.Count == 0) continue;
                stats[take.role] = StatsOf(LowestOf(result));
            }
            // the same target level-apply aims at: the middle of this monster's reference takes as THIS measure read them
            if (!LevelTarget(set, role => stats.TryGetValue(role, out var found) ? found : null, out float target, out string basis)) { o["problem"] = basis; return o; }
            o["targetCm"] = R2(target); o["targetBasis"] = basis;
            foreach (var take in set.takes)
            {
                if (!stats.TryGetValue(take.role, out var s)) continue;
                Stat(s, take.stat, out float planted);
                takes[take.role] = new JObject { ["stat"] = take.stat, ["level"] = take.level, ["plantedCm"] = R2(planted), ["offTargetCm"] = R2(planted - target), ["minCm"] = R2(s.min), ["entryCm"] = R2(s.entry), ["exitCm"] = R2(s.exit) };
            }
            var pairs = new JArray(); float worst = 0;
            foreach (string pair in set.pairs)
            {
                var ends = pair.Split('>'); if (!stats.ContainsKey(ends[0]) || !stats.ContainsKey(ends[1])) continue;
                float step = StepCm(set, stats, ends[0], ends[1]); worst = Mathf.Max(worst, Mathf.Abs(step)); pairs.Add(new JObject { ["pair"] = pair, ["stepCm"] = R2(step) });
            }
            o["takes"] = takes; o["pairs"] = pairs; o["worstStepCm"] = R2(worst); return o;
        }
        static string CullingLine(JObject culling) =>
            "컬링 구(프리팹 `FolkloreCullingBounds298`): 반경 " + (culling["radiusM"] != null ? culling["radiusM"] + " m" : "읽을 수 없음") + " · 가장 먼 정점 " + (culling["reachMaxM"] != null ? culling["reachMaxM"] + " m (`" + culling["reachMaxSet"] + "`)" : "-") +
            " · 구 밖 정점 합 " + culling["outsideTotal"] + " · 범위 기록 " + ((string)culling["boundsRecord"] == "applied" ? "있음(`bounds-apply`)" : "없음(#298 맞춤 그대로)") + "\n";
        static string LevelLine(JObject level)
        {
            if (level["problem"] != null) return "테이크 높이: 자료 문제 — " + level["problem"] + "\n";
            var takes = (JObject)level["takes"]; var parts = takes.Properties().Select(p => p.Name + " " + p.Value["plantedCm"] + " cm(" + p.Value["stat"] + ")");
            var pairs = ((JArray)level["pairs"]).Select(p => p["pair"] + " " + p["stepCm"]);
            return "테이크 심긴 높이(가장 낮은 정점, 목표 " + level["targetCm"] + " cm = " + level["targetBasis"] + " · 기록 " + ((string)level["record"] == "applied" ? "있음" : "없음") + "): " + string.Join(" · ", parts) +
                "\n\n전환 단차(cm, + = 올라감, 한계 " + level["maxStepCm"] + "): " + string.Join(" · ", pairs) + " → 최대 " + level["worstStepCm"] + " cm\n";
        }
        static string Rev3Status(Config cfg) =>
            "  take root heights (level): " + (File.Exists(LevelRecord) ? "APPLIED (record " + LevelRecord + ")" : "not applied") + (File.Exists(LegacyLevelRecord) ? " | LEGACY record " + LegacyLevelRecord + " present" : "") +
            "\n  culling bounds (prefabs): " + (File.Exists(BoundsRecord) ? "APPLIED (record " + BoundsRecord + ")" : "not applied") + " | radius now: " + BoundsStatusLine(cfg);
    }
}
