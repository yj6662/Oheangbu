using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 seat fix D4: the tall grass at the rest places. The grass is one baked seed field (CompactGrassField266) shared by the
    // three scenes; CompactGrassRenderer266 has no clear list / mask / road exclusion, and the established way to clear a place is
    // to take its seeds out of the field (#297 Grass297, Roadside303 building footprints, Floating307). This does the same, once:
    //   grass-plan[:<alias>]    read only: seeds per zone (contentseat308.json grass.clear)
    //   grass-apply[:<alias>]   backs the asset (+ .meta) up, records every removed seed (cell, index, position, normal) and each changed
    //                           cell's bounds in Pacing/ledger_seat308_grass.json, then SetDirty + SaveAssetIfDirty(the field) only.
    //                           A second call finds nothing to remove ("already"): the thin band keeps the same seeds every time
    //                           (the keep test is a fixed hash of the seed position).
    //   grass-revert[:force]    puts the recorded seeds back at their old indices and the old cell bounds; no scene is needed. Refused
    //                           when the asset on disk is not the file the last apply wrote (another tool changed the field since: the
    //                           old indices may be other places now) unless :force is given (seeds then go back by index where the cell
    //                           is long enough, else at its end).
    // D4 is PROPOSED: grass-apply is refused while grass.apply_enabled is false (the cause of the hidden altar is not settled - see
    // "diag" in ContentSeat308.cs). The field is shared by the three scenes and is changed once: a second apply that would remove more
    // seeds is refused unless grass.reapply_confirmed is true.
    // The scene (default grass.default_scene) is opened read-only for the altar / inn poses and is never saved.
    // D308-16 relayout: a grass.clear row with "closed": true is not read at all (the logging_front rows: that altar moves away and
    // no clearing is planned at its new place). grass-apply stays locked by grass.apply_enabled.
    public static partial class ContentSeat308
    {
        static string GrassLedgerFile => Path.Combine(OutDir, "ledger_seat308_grass.json");

        [Serializable] sealed class SeedRow { public int cell, index; public Vector3 p; public Vector2 n; public string zone = ""; }
        [Serializable] sealed class CellRow { public int cell, countBefore; public Vector3 boundsCentre, boundsSize; }
        [Serializable] sealed class GrassOp
        {
            public string utc = "", command = "", status = "", detail = "", asset = "", backup = "", shaBefore = "", shaAfter = "", sceneUsed = "", dataSha = "", revertedUtc = "";
            public int countBefore, countAfter; public bool reverted;
            public List<string> zones = new List<string>(); public List<SeedRow> seeds = new List<SeedRow>(); public List<CellRow> cells = new List<CellRow>();
        }
        [Serializable] sealed class GrassLedger { public List<GrassOp> ops = new List<GrassOp>(); }

        static GrassLedger LoadGrassLedger() => File.Exists(GrassLedgerFile) ? JsonUtility.FromJson<GrassLedger>(File.ReadAllText(GrassLedgerFile)) ?? new GrassLedger() : new GrassLedger();
        static void SaveGrassLedger(GrassLedger l) { Directory.CreateDirectory(OutDir); File.WriteAllText(GrassLedgerFile, JsonUtility.ToJson(l, true), new UTF8Encoding(false)); }

        sealed class Zone
        {
            public string Id = "", Kind = "", Skip = ""; public bool Enabled, Required;
            public Vector2 A, B; public float Full, Thin, Yaw, Margin; public float[] Rect; public Vector3 Origin;
            public int FullCount, ThinRemove, ThinKeep, Probe;
            public Vector2 Centre => Kind == "rect" ? new Vector2(Origin.x, Origin.z) : A;
            public float Reach => Kind == "rect" ? Mathf.Max(Mathf.Abs(Rect[0]), Mathf.Abs(Rect[1])) + Mathf.Max(Mathf.Abs(Rect[2]), Mathf.Abs(Rect[3])) + Margin : Vector2.Distance(A, B) + Mathf.Max(Full, Thin);
        }

        // 0 = keep, 1 = remove (full zone), 2 = remove (thin band), 3 = kept by the thin band's fixed hash
        static int Classify(Zone z, Vector3 p, float thinKeep)
        {
            var xz = new Vector2(p.x, p.z);
            if (z.Kind == "rect") return RectOut(z.Rect, ToLocal(z.Yaw, z.Origin, p)) <= z.Margin ? 1 : 0;
            float d = z.Kind == "disc" ? Vector2.Distance(xz, z.A) : BuildingAudit308.SegDist2(xz, z.A, z.B);
            if (d <= z.Full) return 1;
            if (d > z.Thin) return 0;
            return ThinKept(p, thinKeep) ? 3 : 2;
        }
        // a fixed hash of the seed position: the same seeds are kept on every run, so a second apply removes nothing more
        static bool ThinKept(Vector3 p, float keep)
        {
            unchecked
            {
                uint h = (uint)BitConverter.SingleToInt32Bits(p.x) * 73856093u ^ (uint)BitConverter.SingleToInt32Bits(p.z) * 19349663u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return h % 1000u < (uint)Mathf.RoundToInt(Mathf.Clamp01(keep) * 1000f);
            }
        }

        static List<Zone> Zones(Ctx k, JToken gr)
        {
            var list = new List<Zone>();
            foreach (var c in Arr(gr, "clear"))
            {
                if (Flag(c, "closed")) continue;
                var z = new Zone { Id = Str(c, "id"), Kind = Str(c, "kind"), Enabled = Flag(c, "enabled"), Required = Flag(c, "required") };
                string needs = Opt(c, "needs");
                if (z.Enabled && !string.IsNullOrEmpty(needs) && !StepLive(k.Ledger, needs)) z.Skip = "needs " + needs + " applied in " + k.Alias + " first (the zone follows the repaired place)";
                switch (z.Kind)
                {
                    case "rest":
                    {
                        var altar = Strict(k, Str(c, "altar_key"), false);
                        var cp = Array.Find(k.Content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>(), p => p != null && p.Id == z.Id);
                        if (altar == null || cp == null) { z.Skip = "altar or checkpoint missing"; break; }
                        var fp = Rect(c, "footprint"); var a = altar.TransformPoint(new Vector3((fp[0] + fp[1]) * .5f, 0, (fp[2] + fp[3]) * .5f));
                        z.A = new Vector2(a.x, a.z); z.B = new Vector2(cp.Feet.x, cp.Feet.z); z.Full = Num(c, "full_m"); z.Thin = Mathf.Max(z.Full, Num(c, "thin_m"));
                        break;
                    }
                    case "disc":
                        z.A = z.B = new Vector2(At(Req(c, "centre_xz"), 0), At(Req(c, "centre_xz"), 1)); z.Full = z.Thin = Num(c, "radius_m");
                        break;
                    case "rect":
                    {
                        var t = Strict(k, Str(c, "key"), false);
                        if (t == null) { z.Skip = "object missing " + Str(c, "key"); break; }
                        z.Origin = t.position; z.Yaw = t.eulerAngles.y; z.Rect = Rect(c, "rect"); z.Margin = Num(c, "margin_m");
                        break;
                    }
                    default: throw new Refuse("data: grass zone " + z.Id + " has unknown kind '" + z.Kind + "' (rest | disc | rect)");
                }
                list.Add(z);
            }
            return list;
        }

        static string GrassRun(string alias, bool apply)
        {
            var data = LoadJson(DataFile, "seat data (copy Tools/Unity/Stage308_seatfix/Data/contentseat308.json there)");
            var gr0 = Req(data, "grass");
            if (!Flag(gr0, "enabled")) return "refused: grass is off in data";
            var k = Begin(string.IsNullOrEmpty(alias) ? Str(gr0, "default_scene") : alias, false, out string previous, out bool opened);
            try
            {
                var gr = Req(k.Data, "grass"); string path = Str(gr, "asset"); float thinKeep = Num(gr, "thin_keep"), yWindow = Num(gr, "y_window_m"), probe = Num(gr, "blocked_probe_m");
                if (ProtectedAsset(k, path)) return "refused: protected asset " + path;
                if (AssetDatabase.AssetPathToGUID(path) != Str(gr, "guid")) return "refused: " + path + " has guid " + AssetDatabase.AssetPathToGUID(path) + ", data says " + Str(gr, "guid");
                var field = AssetDatabase.LoadAssetAtPath<CompactGrassField266>(path);
                if (field == null) return "refused: grass field missing " + path;
                var renderers = k.Scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CompactGrassRenderer266>(true)).ToArray();
                int users = renderers.Count(r => r.Field == field);
                if (users == 0) return "refused: no CompactGrassRenderer266 in " + k.ScenePath + " draws " + path + " (" + renderers.Length + " renderer(s) draw other fields)";
                var zones = Zones(k, gr);
                // ---- scan (read only)
                var remove = new Dictionary<int, List<(int index, string zone)>>(); int sum = 0;
                foreach (var cell in field.Cells) if (cell != null && cell.Seeds != null) sum += cell.Seeds.Length;
                foreach (var z in zones)
                {
                    if (z.Skip.Length > 0) continue;
                    var c0 = field.Coordinate(new Vector3(z.Centre.x, 0, z.Centre.y)); int reach = Mathf.CeilToInt((z.Reach + probe) / CompactGrassField266.CellSize) + 1;
                    for (int cz = c0.y - reach; cz <= c0.y + reach; cz++)
                        for (int cx = c0.x - reach; cx <= c0.x + reach; cx++)
                        {
                            int id = field.Index(cx, cz); if (id < 0 || field.Cells[id] == null || field.Cells[id].Seeds == null) continue;
                            var seeds = field.Cells[id].Seeds;
                            for (int i = 0; i < seeds.Length; i++)
                            {
                                var p = seeds[i].Position;
                                if (Vector2.Distance(new Vector2(p.x, p.z), z.Centre) <= probe) { float gy0 = GroundY(k, p.x, p.z); if (float.IsNaN(gy0) || Mathf.Abs(p.y - gy0) <= yWindow) z.Probe++; }
                                int cls = Classify(z, p, thinKeep); if (cls == 0) continue;
                                float gy = GroundY(k, p.x, p.z);
                                if (!float.IsNaN(gy) && Mathf.Abs(p.y - gy) > yWindow) continue;   // a seed on another level (a ledge above, a cave below)
                                if (cls == 3) { z.ThinKeep++; continue; }
                                if (cls == 1) z.FullCount++; else z.ThinRemove++;
                                if (!z.Enabled) continue;
                                if (!remove.TryGetValue(id, out var l)) remove[id] = l = new List<(int, string)>();
                                if (!l.Any(x => x.index == i)) l.Add((i, z.Id));
                            }
                        }
                }
                int total = remove.Sum(kv => kv.Value.Count); var ledger = LoadGrassLedger();
                bool everApplied = ledger.ops.Any(o => o.status == "applied" || o.status == "writing");
                // mesh_reach_scale = the largest per-seed scale CompactGrassRenderer266 draws a clump with (data, read off that renderer)
                float meshReach = 0; if (field.NearMesh != null) { var b = field.NearMesh.bounds; meshReach = Mathf.Max(Mathf.Abs(b.center.x) + b.extents.x, Mathf.Abs(b.center.z) + b.extents.z) * Num(gr, "mesh_reach_scale"); }
                var sb = new StringBuilder("ContentSeat308 " + (apply ? "grass-apply" : "grass-plan") + " (scene " + k.Alias + " read for the poses; never saved)\n");
                sb.AppendLine("  field " + path + " sha " + Short(ShaOf(Harness303.Abs(path))) + ", Count " + field.Count + " (cells hold " + sum + "), " + users + " renderer(s) in this scene draw it" + (EditorUtility.IsDirty(field) ? " - DIRTY in memory" : ""));
                sb.AppendLine("  one seed = one clump: near mesh reach " + F(meshReach) + " m around the seed (a clump that close to a zone edge still leans over it; widen full_m / radius_m in the data if the still shows that)");
                var blocked = new List<string>();
                foreach (var z in zones)
                {
                    k.Numbers["D4." + z.Id + ".full"] = z.FullCount; k.Numbers["D4." + z.Id + ".thin"] = z.ThinRemove + z.ThinKeep;
                    string what = z.Kind == "rect" ? "rect + " + F(z.Margin, "F1") + " m at " + V(z.Origin) : z.Kind == "disc" ? "disc r " + F(z.Full, "F1") + " at (" + F(z.A.x, "F1") + ", " + F(z.A.y, "F1") + ")" : "capsule (" + F(z.A.x, "F1") + ", " + F(z.A.y, "F1") + ") -> (" + F(z.B.x, "F1") + ", " + F(z.B.y, "F1") + ") full " + F(z.Full, "F1") + " thin " + F(z.Thin, "F1");
                    sb.AppendLine("  " + z.Id + " [" + what + "]: " + (z.Skip.Length > 0 ? "SKIPPED - " + z.Skip : "full zone " + z.FullCount + ", thin band remove " + z.ThinRemove + " / keep " + z.ThinKeep + ", within " + F(probe, "F1") + " m of the centre " + z.Probe + (z.Enabled ? "" : " (measured only)")));
                    sb.AppendLine("     num D4." + z.Id + ".full " + z.FullCount + "\n     num D4." + z.Id + ".thin " + (z.ThinRemove + z.ThinKeep));
                    if (z.Enabled && z.Skip.Length > 0) blocked.Add(z.Id + ": " + z.Skip);
                    if (z.Enabled && z.Required && z.Skip.Length == 0 && z.Probe == 0 && !everApplied) blocked.Add(z.Id + ": no seed within " + F(probe, "F1") + " m - the grass band here is not this field (fallback: move the altar; see SEATFIX308_SCOPE.md D4)");
                }
                foreach (var d in CompareDry(k)) sb.AppendLine("  " + d);
                sb.AppendLine("  to remove: " + total + " seed(s) in " + remove.Count + " cell(s)" + (everApplied ? " (the ledger already holds an apply)" : ""));
                bool held = !Flag(gr, "apply_enabled");
                if (held) sb.AppendLine("  HELD: grass.apply_enabled is false - D4 is PROPOSED (" + (Opt(gr, "hold_note") ?? "no note") + ")");
                if (!apply) { foreach (var b in blocked) sb.AppendLine("  blocker: " + b); return sb.ToString(); }
                if (held) return "refused: D4 is PROPOSED - grass.apply_enabled is false in contentseat308.json; nothing written\n" + sb;
                if (ledger.ops.Any(o => !o.reverted && o.status == "writing")) return "refused: an earlier grass-apply did not finish (ledger status 'writing') - run grass-revert first; nothing written\n" + sb;
                if (total > 0 && ledger.ops.Any(o => !o.reverted && o.status == "applied") && !Flag(gr, "reapply_confirmed"))
                    return "refused: the shared field was already changed once by this tool and this call would remove " + total + " more seed(s). The field is shared by the three scenes and is changed once: grass-revert, settle the numbers with grass-plan and the stills, then apply once - or set grass.reapply_confirmed true after the user agreed; nothing written\n" + sb;
                if (blocked.Count > 0) return "refused: blocked - nothing written - " + string.Join(" | ", blocked) + "\n" + sb;
                if (total == 0) return "already: nothing to remove (every zone is clear)\n" + sb;
                if (EditorUtility.IsDirty(field)) return "refused: " + path + " has unsaved in-memory changes (another session?) - nothing written\n" + sb;
                // ---- write: backup, ledger first, then the one asset
                var op = new GrassOp { utc = BuildingAudit308.Utc(), command = "grass-apply", asset = path, sceneUsed = k.ScenePath, dataSha = k.DataSha, countBefore = field.Count, shaBefore = ShaOf(Harness303.Abs(path)), status = "writing" };
                string backupDir = Path.Combine(OutDir, "Backups", "seat308-grass-" + op.utc); Directory.CreateDirectory(backupDir);
                op.backup = Path.Combine(backupDir, Path.GetFileName(path)); File.Copy(Harness303.Abs(path), op.backup, false);
                if (File.Exists(Harness303.Abs(path) + ".meta")) File.Copy(Harness303.Abs(path) + ".meta", op.backup + ".meta.txt", false);
                op.zones = zones.Where(z => z.Enabled && z.Skip.Length == 0).Select(z => z.Id + " full " + z.FullCount + " thin " + z.ThinRemove).ToList();
                foreach (var kv in remove.OrderBy(x => x.Key))
                {
                    var cell = field.Cells[kv.Key];
                    op.cells.Add(new CellRow { cell = kv.Key, countBefore = cell.Seeds.Length, boundsCentre = cell.Bounds.center, boundsSize = cell.Bounds.size });
                    foreach (var (index, zone) in kv.Value.OrderBy(x => x.index)) op.seeds.Add(new SeedRow { cell = kv.Key, index = index, p = cell.Seeds[index].Position, n = cell.Seeds[index].NormalXZ, zone = zone });
                }
                ledger.ops.Add(op); SaveGrassLedger(ledger);
                try
                {
                    foreach (var kv in remove)
                    {
                        var cell = field.Cells[kv.Key]; var drop = new HashSet<int>(kv.Value.Select(x => x.index));
                        var keep = new List<CompactGrassField266.Seed>(cell.Seeds.Length);
                        for (int i = 0; i < cell.Seeds.Length; i++) if (!drop.Contains(i)) keep.Add(cell.Seeds[i]);
                        cell.Seeds = keep.ToArray();
                        if (keep.Count > 0)
                        {
                            // the #297 bounds rule (a copy of CompactRebuildAuthoring.Grass297: 2 m seed box, +1 m up, expand 2)
                            var bounds = new Bounds(keep[0].Position, Vector3.one * 2); foreach (var s in keep) bounds.Encapsulate(s.Position + Vector3.up); bounds.Expand(2); cell.Bounds = bounds;
                        }
                    }
                    field.Count -= total;
                    EditorUtility.SetDirty(field); AssetDatabase.SaveAssetIfDirty(field);
                }
                catch (Exception e)
                {
                    // the field must never stay half-changed and dirty in memory (another session's SaveAssets would write it): when the
                    // file on disk is still the old one, the in-memory object is dropped and reloads from disk on its next use
                    string disk = ShaOf(Harness303.Abs(path));
                    if (disk == op.shaBefore)
                    {
                        try { Resources.UnloadAsset(field); } catch (Exception) { }
                        op.status = "FAILED"; op.reverted = true; op.detail = "nothing written, in-memory changes dropped: " + e.Message;
                    }
                    else { op.shaAfter = disk; op.status = "applied"; op.detail = "the asset was written, then failed: " + e.Message + " (grass-revert puts the seeds back)"; }
                    SaveGrassLedger(ledger);
                    return "FAILED: grass-apply - " + op.detail + " (backup " + op.backup + ")\n" + sb;
                }
                foreach (var r in Object.FindObjectsByType<CompactGrassRenderer266>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (r.Field == field) r.Invalidate();
                op.countAfter = field.Count; op.shaAfter = ShaOf(Harness303.Abs(path)); op.status = "applied"; op.detail = total + " seed(s) removed in " + remove.Count + " cell(s)";
                SaveGrassLedger(ledger);
                int now = 0; foreach (var cell in field.Cells) if (cell != null && cell.Seeds != null) now += cell.Seeds.Length;
                sb.AppendLine("  applied: " + op.detail + "; Count " + op.countBefore + " -> " + op.countAfter + " (cells hold " + now + ", was " + sum + ": " + (sum - now == total ? "only the listed seeds left" : "MISMATCH") + "); sha " + Short(op.shaBefore) + " -> " + Short(op.shaAfter));
                sb.AppendLine("  backup " + op.backup + "; ledger " + GrassLedgerFile);
                sb.AppendLine("  next: grass-plan (must report 0 to remove), then the 7 m still of each rest place. The asset is shared: the other two scenes need no call.");
                return sb.ToString();
            }
            finally { GoBack(previous, k.Scene, opened); }
        }

        static string GrassRevert(bool force)
        {
            RequireEditable();
            var ledger = LoadGrassLedger();
            var ops = ledger.ops.Where(o => !o.reverted && (o.status == "applied" || o.status == "writing")).ToList();
            if (ops.Count == 0) return "refused: nothing to revert in " + GrassLedgerFile;
            string path = ops[0].asset;
            if (ops.Any(o => o.asset != path)) return "refused: the grass ledger names more than one asset";
            if (Harness303.IsProtected(path) || PostLedger308.IsProtectedPath(path)) return "refused: protected asset " + path;
            var field = AssetDatabase.LoadAssetAtPath<CompactGrassField266>(path);
            if (field == null) return "refused: grass field missing " + path;
            if (EditorUtility.IsDirty(field)) return "refused: " + path + " has unsaved in-memory changes (another session?)";
            // the old indices are only right in the file the last apply wrote
            string shaNow = ShaOf(Harness303.Abs(path)); var lastOp = ops[ops.Count - 1];
            string expect = lastOp.status == "applied" ? lastOp.shaAfter : lastOp.shaBefore; bool changedSince = !string.IsNullOrEmpty(expect) && shaNow != expect && !(lastOp.status == "writing" && shaNow == lastOp.shaAfter);
            if (changedSince && !force) return "refused: " + path + " is sha " + Short(shaNow) + " on disk, the last apply left " + Short(expect) + " - another tool changed the field since (Grass297 apply?): the recorded indices may be other places now. Compare with grass-plan, then grass-revert:force (seeds go back by index where the cell is long enough, else at its end) or restore the backup " + lastOp.backup + " by hand if nothing else must be kept";
            string utc = BuildingAudit308.Utc(); string backupDir = Path.Combine(OutDir, "Backups", "seat308-grass-" + utc + "-prerevert"); Directory.CreateDirectory(backupDir);
            string copy = Path.Combine(backupDir, Path.GetFileName(path)); File.Copy(Harness303.Abs(path), copy, false);
            int back = 0, appended = 0; string shaBefore = ShaOf(Harness303.Abs(path));
            foreach (var op in Enumerable.Reverse(ops))
            {
                foreach (var group in op.seeds.GroupBy(s => s.cell))
                {
                    if (group.Key < 0 || group.Key >= field.Cells.Length || field.Cells[group.Key] == null) continue;
                    var cell = field.Cells[group.Key]; var list = new List<CompactGrassField266.Seed>(cell.Seeds ?? Array.Empty<CompactGrassField266.Seed>());
                    // "writing" = the op was recorded but the removal may not have reached the cell: a seed that is still there is not added twice
                    foreach (var row in group.OrderBy(s => s.index))
                    {
                        if (list.Any(s => s.Position == row.p)) continue;
                        var seed = new CompactGrassField266.Seed { Position = row.p, NormalXZ = row.n };
                        if (row.index <= list.Count) list.Insert(row.index, seed); else { list.Add(seed); appended++; }
                        back++;
                    }
                    cell.Seeds = list.ToArray();
                    var cr = op.cells.FirstOrDefault(c => c.cell == group.Key);
                    if (cr != null) cell.Bounds = new Bounds(cr.boundsCentre, cr.boundsSize);
                }
            }
            // the exact inverse of the apply (Count -= removed): Count is not rebuilt from the cells, the two may differ in the source
            int sum = 0; foreach (var cell in field.Cells) if (cell != null && cell.Seeds != null) sum += cell.Seeds.Length;
            field.Count += back;
            string countNote = field.Count == ops[0].countBefore ? "" : " WARNING Count is " + field.Count + ", the first apply started from " + ops[0].countBefore + (changedSince ? " (the field was changed by another tool since)" : "");
            if (back > 0) { EditorUtility.SetDirty(field); AssetDatabase.SaveAssetIfDirty(field); }
            foreach (var r in Object.FindObjectsByType<CompactGrassRenderer266>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (r.Field == field) r.Invalidate();
            foreach (var op in ops) { op.reverted = true; op.revertedUtc = utc; }
            ledger.ops.Add(new GrassOp { utc = utc, command = "grass-revert", status = "reverted", asset = path, backup = copy, shaBefore = shaBefore, shaAfter = ShaOf(Harness303.Abs(path)), countAfter = field.Count, detail = back + " seed(s) put back" + (appended > 0 ? " (" + appended + " appended: their cell changed since the apply)" : " at their old indices") + (changedSince ? "; FORCED over a field another tool changed (sha " + Short(shaNow) + " vs " + Short(expect) + ")" : "") + countNote });
            SaveGrassLedger(ledger);
            return "grass-revert: " + back + " seed(s) put back into " + path + (appended > 0 ? " (" + appended + " appended at the end of their cell: it changed since the apply)" : " at their old indices, cell bounds restored") + "; Count " + field.Count + " (cells hold " + sum + "; the first apply started from " + ops[0].countBefore + ")" + countNote + (back == 0 ? "; nothing was missing, the asset was not written" : "") + "; pre-revert backup " + copy;
        }
    }
}
