using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #307 user report (2026-10-01): green objects floating in the sky ("이 공중에 떠있는 오브젝트들 정리"). Finds grass seeds and
    // dressing-sheet placements whose base is above the current ground (the terrain was edited after they were baked):
    //   scan[:min=<m>]      Edit or Play: every CompactGrassRenderer266 field seed and every art renderer sheet FixedPlacement;
    //                       ray down from base + 0.3 m; floating = first hit farther than min (default 1.5 m) or no hit within 80 m.
    //                       Writes Art/Performance/Perf307/Floating/<utc>.txt (+ .csv of every floating item). Nothing is changed.
    //   remove[:min=<m>]    Edit mode: removes those seeds / placements from their assets (backup to Tools/Unity/Stage307_backup_floating/<utc>/)
    //                       and saves only those assets. Protected trees (Watershed295 / Reworld292 / MountainTrail285) are skipped.
    public static class Floating307
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Performance/Perf307/Floating"));
        static bool Protected(string path) => path.Contains("/Watershed295/") || path.Contains("/Reworld292/") || path.Contains("/MountainTrail285/");

        // #307: never persist a stale editor-harness save suffix (Play exit restores it onto the edit-mode session; seen 2026-10-01)
        static void ClearStaleSuffix(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var g in scene.GetRootGameObjects()) foreach (var s in g.GetComponentsInChildren<WorldMacroPlaytestSession>(true))
                if (WorldMacroPlaytestSession.StaleHarnessSuffix307(s.TestSaveSuffix)) s.TestSaveSuffix = "";
        }
        static string PathOf(Transform t) { var s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; }

        // renderers[:min=m][:all] — every enabled Renderer in the loaded scenes (art/grass instanced draws are not Renderers) whose
        // bounds bottom is more than min metres above the ground under its centre (column ray; the renderer's own colliders ignored).
        // Grouped by parent path; :all lists every item. Nothing is changed.
        static string Renderers(float min, bool all)
        {
            var groups = new Dictionary<string, List<string>>(); int total = 0, floating = 0;
            var allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(x => x.gameObject.activeInHierarchy).ToArray();
            foreach (var r in allRenderers)
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.gameObject.layer == 5) continue;   // UI
                if (r is ParticleSystemRenderer psr && psr.GetComponent<ParticleSystem>() is var ps && ps != null && ps.particleCount == 0 && Application.isPlaying) continue;
                total++; var b = r.bounds; if (b.size.sqrMagnitude < 1e-6f) continue;
                var own = r.GetComponentsInChildren<Collider>(true).Concat(r.GetComponentsInParent<Collider>(true)).ToArray();
                var hits = Physics.RaycastAll(new Vector3(b.center.x, b.max.y + 300f, b.center.z), Vector3.down, 800f, ~0, QueryTriggerInteraction.Ignore);
                float ground = float.NegativeInfinity;
                foreach (var h in hits) if (!own.Contains(h.collider) && h.point.y <= b.min.y + .5f && h.point.y > ground) ground = h.point.y;
                float gap = float.IsNegativeInfinity(ground) ? float.PositiveInfinity : b.min.y - ground;
                if (gap <= min) continue;
                // unsupported only: no other renderer (terrain-sized boxes excluded) fills the column between the ground and this
                // bounds' bottom — a roof over walls or a deck on posts is supported; a vein hanging in the air is not
                float floor = float.IsInfinity(gap) ? b.min.y - 50f : ground;
                var column = new Bounds(new Vector3(b.center.x, (floor + b.min.y) * .5f, b.center.z), new Vector3(Mathf.Max(.2f, b.size.x * .8f), Mathf.Max(.01f, b.min.y - floor - .2f), Mathf.Max(.2f, b.size.z * .8f)));
                bool supported = false;
                foreach (var o in allRenderers)
                {
                    if (o == r || !o.enabled) continue; var ob = o.bounds; if (ob.size.magnitude > 150f) continue;
                    if (o.transform.IsChildOf(r.transform)) continue;
                    if (ob.Intersects(column) && ob.min.y < b.min.y - .2f) { supported = true; break; }
                }
                if (supported) continue;
                floating++;
                var t = r.transform; string key = t.parent != null ? PathOf(t.parent) : "(root)";
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<string>();
                list.Add(r.name + " [" + r.GetType().Name + (r.sharedMaterial != null ? " " + r.sharedMaterial.name + " / " + (r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "?") : "") + "] at " + b.center.ToString("F0") + " size " + b.size.ToString("F1") + " gap " + (float.IsInfinity(gap) ? "no ground" : gap.ToString("F1") + " m"));
            }
            var sb = new StringBuilder("#307 floating renderers (min " + min + " m): " + floating + " of " + total + " enabled renderers, " + groups.Count + " parents\n");
            foreach (var g in groups.OrderByDescending(g => g.Value.Count))
            {
                sb.Append("- ").Append(g.Key).Append(" (").Append(g.Value.Count).Append(")\n");
                foreach (var line in g.Value.Take(all ? 1000 : 3)) sb.Append("    ").Append(line).Append('\n');
            }
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", Inv) + "_renderers.txt"), sb.ToString(), new UTF8Encoding(false));
            return sb.ToString();
        }

        public static string Run(string arg)
        {
            arg = (arg ?? "").Trim();
            float min = 1.5f; var m = System.Text.RegularExpressions.Regex.Match(arg, @"min=([0-9.]+)"); if (m.Success) min = float.Parse(m.Groups[1].Value, Inv);
            if (arg.StartsWith("renderers", StringComparison.Ordinal)) return Renderers(arg.Contains("min=") ? min : 5f, arg.Contains("all"));
            // ore-off — #307: the Finish297 cave dressing ore seams (InkLightSource strips + their point lights) were fitted to the walls
            // of the pre-relocation cave and now hang 45-66 m above the ground (user screenshot 2026-10-01). Deactivates the group in the
            // open W_Demo_Main (refuses when that scene already has unsaved changes) and saves it. Re-promotion from the candidate scenes
            // would bring them back: CompactFinish297.CaveDressing needs ore origins inside the current cave.
            if (arg == "dirty")   // which objects of the open scenes are marked dirty (nothing changed)
            {
                var sbd = new StringBuilder();
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                {
                    var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); sbd.Append(sc.name).Append(" dirty ").Append(sc.isDirty).Append('\n');
                    int n = 0;
                    foreach (var g in sc.GetRootGameObjects()) foreach (var c in g.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null || !EditorUtility.IsDirty(c)) continue; if (n++ > 30) continue;
                        string path = c.name; for (var q = c.transform.parent; q != null; q = q.parent) path = q.name + "/" + path;
                        sbd.Append("  ").Append(path).Append(" [").Append(c.GetType().Name).Append("]\n");
                    }
                    sbd.Append("  dirty components ").Append(n).Append('\n');
                }
                return sbd.ToString();
            }
            if (arg == "save-main")   // saves the open W_Demo_Main as is (after `dirty` showed only ExecuteAlways re-applies)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only";
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("W_Demo_Main");
                if (!scene.IsValid() || !scene.isLoaded) return "refused: W_Demo_Main is not open";
                ClearStaleSuffix(scene);
                return "saved " + UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            }
            if (arg == "ore-off")
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only";
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("W_Demo_Main");
                if (!scene.IsValid() || !scene.isLoaded) return "refused: W_Demo_Main is not open";
                if (scene.isDirty) return "refused: W_Demo_Main has unsaved changes; save or revert them first";
                Transform ore = null;
                foreach (var g in scene.GetRootGameObjects()) { var found = g.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Ore_Seams297"); if (found != null) { ore = found; break; } }
                if (ore == null) return "refused: Ore_Seams297 not found";
                int renderers = ore.GetComponentsInChildren<Renderer>(true).Length, lights = ore.GetComponentsInChildren<Light>(true).Length;
                string path = ore.name; for (var q = ore.parent; q != null; q = q.parent) path = q.name + "/" + path;
                if (!ore.gameObject.activeSelf) return "already inactive: " + path;
                Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath, "../../Tools/Unity/Stage307_backup_floating")));
                File.Copy(Path.GetFullPath(Path.Combine(Application.dataPath, "..", scene.path)), Path.GetFullPath(Path.Combine(Application.dataPath, "../../Tools/Unity/Stage307_backup_floating/W_Demo_Main_" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", Inv) + ".unity")), true);
                Undo.RecordObject(ore.gameObject, "ore-off"); ore.gameObject.SetActive(false);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                ClearStaleSuffix(scene);
                bool saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                return "deactivated " + path + " (" + renderers + " renderers, " + lights + " lights); scene saved " + saved;
            }
            bool remove = arg.StartsWith("remove", StringComparison.Ordinal);
            if (!remove && !arg.StartsWith("scan", StringComparison.Ordinal)) return "refused: scan[:min=m] | remove[:min=m]";
            if (remove && EditorApplication.isPlayingOrWillChangePlaymode) return "refused: remove runs in Edit mode only";
            var sb = new StringBuilder("#307 Floating307 " + (remove ? "REMOVE" : "scan") + " min " + min + " m " + DateTime.Now.ToString("s") + "\n");
            var csv = new StringBuilder("kind,asset,id,prototype,x,y,z,gap_m\n");
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", Inv);
            string backup = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Tools/Unity/Stage307_backup_floating/" + stamp));

            // height of the base above the nearest surface under it: a column ray from 300 m above; the highest hit at or below
            // base + .5 m is the ground under the base. All surfaces above the base = buried (-1, not floating); no hit = no collider (NaN).
            int buried = 0, voids = 0;
            float Gap(Vector3 p)
            {
                var hits = Physics.RaycastAll(p + Vector3.up * 300f, Vector3.down, 700f, ~0, QueryTriggerInteraction.Ignore);
                if (hits.Length == 0) { voids++; return float.NaN; }
                float best = float.NegativeInfinity;
                foreach (var h in hits) if (h.point.y <= p.y + .5f && h.point.y > best) best = h.point.y;
                if (float.IsNegativeInfinity(best)) { buried++; return -1f; }
                return p.y - best;
            }

            // grass fields
            var fields = Object.FindObjectsByType<CompactGrassRenderer266>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(g => new SerializedObject(g).FindProperty("Field")?.objectReferenceValue as CompactGrassField266).Where(f => f != null).Distinct().ToList();
            foreach (var field in fields)
            {
                string path = AssetDatabase.GetAssetPath(field); int total = 0, floating = 0;
                var buckets = new Dictionary<Vector2Int, int>();
                foreach (var cell in field.Cells)
                {
                    if (cell?.Seeds == null) continue;
                    var keep = new List<CompactGrassField266.Seed>(cell.Seeds.Length);
                    foreach (var seed in cell.Seeds)
                    {
                        total++; float gap = Gap(seed.Position);
                        if (!float.IsNaN(gap) && gap > min) { floating++; var k = new Vector2Int(Mathf.FloorToInt(seed.Position.x / 50), Mathf.FloorToInt(seed.Position.z / 50)); buckets[k] = buckets.TryGetValue(k, out int n) ? n + 1 : 1;
                            csv.Append("grass,").Append(path).Append(",,,").Append(seed.Position.x.ToString("F1", Inv)).Append(',').Append(seed.Position.y.ToString("F1", Inv)).Append(',').Append(seed.Position.z.ToString("F1", Inv)).Append(',').Append(float.IsInfinity(gap) ? "inf" : gap.ToString("F1", Inv)).Append('\n'); }
                        else keep.Add(seed);
                    }
                    if (remove && keep.Count != cell.Seeds.Length) cell.Seeds = keep.ToArray();
                }
                sb.Append("grass field ").Append(path).Append(": ").Append(floating).Append(" / ").Append(total).Append(" seeds floating");
                if (buckets.Count > 0) sb.Append(" | densest 50 m cells: ").Append(string.Join(", ", buckets.OrderByDescending(kv => kv.Value).Take(8).Select(kv => "(" + (kv.Key.x * 50 + 25) + "," + (kv.Key.y * 50 + 25) + ") " + kv.Value)));
                sb.Append('\n');
                if (remove && floating > 0)
                {
                    if (Protected(path)) { sb.Append("  SKIP protected\n"); continue; }
                    Directory.CreateDirectory(backup); File.Copy(Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)), Path.Combine(backup, Path.GetFileName(path)), true);
                    field.Count = field.Cells.Sum(c => c?.Seeds?.Length ?? 0);
                    EditorUtility.SetDirty(field); AssetDatabase.SaveAssetIfDirty(field); sb.Append("  removed, field Count now ").Append(field.Count).Append('\n');
                }
            }

            // dressing sheet placements
            var sheets = Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy).Select(r => r.Sheet).Where(s => s != null).Distinct().ToList();
            foreach (var sheet in sheets)
            {
                string path = AssetDatabase.GetAssetPath(sheet); int total = 0, floating = 0;
                var byProto = new Dictionary<string, int>(); var keep = new List<WorldMacroDressingSheetSO.FixedPlacement>();
                foreach (var p in sheet.FixedPlacements ?? Array.Empty<WorldMacroDressingSheetSO.FixedPlacement>())
                {
                    if (p == null) continue; total++; float gap = Gap(p.Position);
                    if (!float.IsNaN(gap) && gap > min)
                    {
                        floating++; byProto[p.PrototypeId ?? "?"] = byProto.TryGetValue(p.PrototypeId ?? "?", out int n) ? n + 1 : 1;
                        csv.Append("sheet,").Append(path).Append(',').Append(p.Id).Append(',').Append(p.PrototypeId).Append(',').Append(p.Position.x.ToString("F1", Inv)).Append(',').Append(p.Position.y.ToString("F1", Inv)).Append(',').Append(p.Position.z.ToString("F1", Inv)).Append(',').Append(float.IsInfinity(gap) ? "inf" : gap.ToString("F1", Inv)).Append('\n');
                    }
                    else keep.Add(p);
                }
                sb.Append("sheet ").Append(path).Append(": ").Append(floating).Append(" / ").Append(total).Append(" placements floating");
                if (byProto.Count > 0) sb.Append(" | ").Append(string.Join(", ", byProto.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + " " + kv.Value)));
                sb.Append('\n');
                if (remove && floating > 0)
                {
                    if (Protected(path)) { sb.Append("  SKIP protected\n"); continue; }
                    Directory.CreateDirectory(backup); File.Copy(Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)), Path.Combine(backup, Path.GetFileName(path)), true);
                    sheet.FixedPlacements = keep.ToArray(); EditorUtility.SetDirty(sheet); AssetDatabase.SaveAssetIfDirty(sheet); sb.Append("  removed\n");
                    foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (r.Sheet == sheet) r.Invalidate();
                }
            }
            sb.Append("columns with no collider (not judged): ").Append(voids).Append(", bases under a surface (buried, not floating): ").Append(buried).Append('\n');
            if (remove && Directory.Exists(backup)) sb.Append("backup ").Append(backup).Append('\n');
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, stamp + (remove ? "_remove" : "") + ".txt"), sb.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(Folder, stamp + (remove ? "_remove" : "") + ".csv"), csv.ToString(), new UTF8Encoding(false));
            return sb.ToString();
        }
    }
}
