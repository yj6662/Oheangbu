using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 assembly repairs and reference measurements.
    //   measure:<prefab or model path>[;<path>...]   proportions of a received building (size, triangles, plan size per tenth of its height, eave overhang) -> Out/measure.txt   (read only, nothing left in the scene)
    //   footing:build | footing:clear                a field-stone plinth under every play-scene building of `footingFor` whose base hangs over the ground (root Assembly308Footing, one mesh per building; never tilts or lowers the building)
    //   move:<id>:<dx>,<dy>,<dz> | moves:revert      one object moved (ledger Ledger/moves.json; revert puts every moved object back)
    // The play scene is backed up to Backup/<stamp>/ before it is written; AssetDatabase.SaveAssets is never called.
    public static partial class Assembly308
    {
        const string FootRoot = "Assembly308Footing", FootAssets = "Assets/_Project/Art/World/Assembly308";
        [Serializable] sealed class FootCfg { public string footingFor = "", footingMaterial = ""; public float footingCell = .5f, footingLip = .12f, footingSink = .4f, footingMinGap = .06f; }
        [Serializable] sealed class MoveRow { public string id = ""; public Vector3 before, after; }
        [Serializable] sealed class MoveLedger { public List<MoveRow> rows = new List<MoveRow>(); }
        static string MoveFile => Path.Combine(Stage, "Ledger", "moves.json");

        static string RunFix(Scene main, string c, string[] a)
        {
            string rr = RunRepair(main, c, a); if (rr != null) return rr;
            if (a[0] == "measure") return MeasureAssets(c.Substring(8));
            if (a[0] == "footing" && a.Length > 1 && (a[1] == "build" || a[1] == "clear")) return Footing(main, a[1] == "build");
            if (a[0] == "move" && a.Length == 3)
            {
                var t = ResolveId(a[1]); if (t == null) return "REFUSED no object " + a[1] + " (is its scene open?)"; var d = a[2].Split(',').Select(v => float.Parse(v, Inv)).ToArray(); var scene = t.gameObject.scene; string dir = Backup(scene, "move");
                var led = File.Exists(MoveFile) ? JsonUtility.FromJson<MoveLedger>(File.ReadAllText(MoveFile)) : new MoveLedger(); var row = led.rows.FirstOrDefault(r => r.id == a[1]); if (row == null) { row = new MoveRow { id = a[1], before = t.position }; led.rows.Add(row); }
                t.position += new Vector3(d[0], d[1], d[2]); row.after = t.position; Directory.CreateDirectory(Path.GetDirectoryName(MoveFile)); File.WriteAllText(MoveFile, JsonUtility.ToJson(led, true));
                EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) return "FAILED scene not saved"; return "Assembly308 move: " + a[1] + " -> " + t.position.ToString("F2") + ", backup " + dir;
            }
            if (c == "moves:revert")
            {
                if (!File.Exists(MoveFile)) return "Assembly308 moves:revert: no ledger"; var led = JsonUtility.FromJson<MoveLedger>(File.ReadAllText(MoveFile)); int n = 0; var touched = new HashSet<Scene>(); var left = new List<MoveRow>();
                foreach (var r in led.rows) { var t = ResolveId(r.id); if (t == null) { left.Add(r); continue; } if (touched.Add(t.gameObject.scene)) Backup(t.gameObject.scene, "moverevert"); t.position = r.before; n++; }
                foreach (var s in touched) { EditorSceneManager.MarkSceneDirty(s); EditorSceneManager.SaveScene(s); } led.rows = left; File.WriteAllText(MoveFile, JsonUtility.ToJson(led, true)); return "Assembly308 moves:revert: " + n + " put back, " + left.Count + " rows left (their scene is not open)";
            }
            return "commands: tree[:depth] | scan[:<Realm|Main>] | check:<prefix or x0,z0,x1,z1> | shots:<tag>:<seq,...> | open:<Realm> | close | measure:<asset;asset> | footing:build|clear | move:<id>:<dx,dy,dz> | moves:revert";
        }

        static string Backup(Scene scene, string what)
        {
            string dir = Path.Combine(Stage, "Backup", DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_" + what); Directory.CreateDirectory(dir);
            File.Copy(Path.GetFullPath(Path.Combine(Application.dataPath, "..", scene.path)), Path.Combine(dir, Path.GetFileName(scene.path)), true); return dir;
        }

        // ------------------------------------------------------------------ reference proportions of received buildings
        static string MeasureAssets(string list)
        {
            var sb = new StringBuilder(); var cache = new BuildingAudit308.MeshCache308();
            foreach (string path in list.Split(';'))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path.Trim()); if (asset == null) { sb.Append("MISSING ").Append(path).Append('\n'); continue; }
                var pts = new List<Vector3>(); long tris = 0; int rend = 0; var lodSkip = new HashSet<Renderer>();
                foreach (var lod in asset.GetComponentsInChildren<LODGroup>(true)) { var lods = lod.GetLODs(); for (int k = 1; k < lods.Length; k++) if (lods[k].renderers != null) foreach (var r in lods[k].renderers) if (r != null && !(lods[0].renderers ?? new Renderer[0]).Contains(r)) lodSkip.Add(r); }
                foreach (var mf in asset.GetComponentsInChildren<MeshFilter>(true))
                {
                    var r = mf.GetComponent<MeshRenderer>(); if (r == null || !r.enabled || lodSkip.Contains(r) || mf.sharedMesh == null) continue; var m = cache.Get(mf.sharedMesh); if (m == null) continue; rend++; tris += m.t.Length / 3; var w = mf.transform.localToWorldMatrix;
                    int step = Mathf.Max(1, m.v.Length / 60000); for (int i = 0; i < m.v.Length; i += step) pts.Add(w.MultiplyPoint3x4(m.v[i]));
                }
                if (pts.Count == 0) { sb.Append("EMPTY ").Append(path).Append('\n'); continue; }
                float y0 = pts.Min(p => p.y), y1 = pts.Max(p => p.y), H = Mathf.Max(.01f, y1 - y0); var lo = new Vector2[10]; var hi = new Vector2[10]; var has = new bool[10];
                foreach (var p in pts) { int b = Mathf.Clamp((int)((p.y - y0) / H * 10f), 0, 9); if (!has[b]) { lo[b] = hi[b] = new Vector2(p.x, p.z); has[b] = true; } else { lo[b] = Vector2.Min(lo[b], new Vector2(p.x, p.z)); hi[b] = Vector2.Max(hi[b], new Vector2(p.x, p.z)); } }
                float sx = pts.Max(p => p.x) - pts.Min(p => p.x), sz = pts.Max(p => p.z) - pts.Min(p => p.z);
                sb.Append(Path.GetFileName(path)).Append("  size ").Append(F1(sx)).Append(" x ").Append(F1(sz)).Append(" x H ").Append(F1(H)).Append("  renderers ").Append(rend).Append("  triangles ").Append(tris).Append("  lods ").Append(asset.GetComponentsInChildren<LODGroup>(true).Length).Append("\n   plan per tenth of height (x*z): ");
                for (int b = 0; b < 10; b++) sb.Append(has[b] ? F1(hi[b].x - lo[b].x) + "*" + F1(hi[b].y - lo[b].y) : "-").Append(b < 9 ? " | " : "\n");
                // body = the narrowest band between 20 % and 60 %, roof = the widest band above it
                int body = -1; for (int b = 2; b <= 5; b++) if (has[b] && (body < 0 || (hi[b] - lo[b]).x * (hi[b] - lo[b]).y < (hi[body] - lo[body]).x * (hi[body] - lo[body]).y)) body = b; int roof = -1; for (int b = 5; b <= 9; b++) if (has[b] && (roof < 0 || (hi[b] - lo[b]).x * (hi[b] - lo[b]).y > (hi[roof] - lo[roof]).x * (hi[roof] - lo[roof]).y)) roof = b;
                if (body >= 0 && roof >= 0) { var bs = hi[body] - lo[body]; var rs = hi[roof] - lo[roof]; sb.Append("   body plan ").Append(F1(bs.x)).Append(" x ").Append(F1(bs.y)).Append("  widest roof plan ").Append(F1(rs.x)).Append(" x ").Append(F1(rs.y)).Append(" at ").Append(roof * 10).Append(" % height (").Append(F1(y0 + roof * H / 10f - y0)).Append(" m)  overhang ").Append(F((rs.x - bs.x) * .5f)).Append(" / ").Append(F((rs.y - bs.y) * .5f)).Append(" m  = ").Append(F((Mathf.Min(rs.x - bs.x, rs.y - bs.y) * .5f) / Mathf.Max(.1f, roof * H / 10f))).Append(" of the eave height\n"); }
            }
            File.WriteAllText(Path.Combine(OutDir, "measure.txt"), sb.ToString()); return sb.ToString();
        }

        // ------------------------------------------------------------------ stone plinth under a building whose base hangs over the ground
        static string Footing(Scene main, bool build)
        {
            var fc = JsonUtility.FromJson<FootCfg>(File.ReadAllText(Path.Combine(Stage, "assembly308.json")).TrimStart('﻿')); var old = main.GetRootGameObjects().FirstOrDefault(g => g.name == FootRoot);
            if (!build && old == null) return "Assembly308 footing:clear: root absent"; string dir = Backup(main, "footing");
            if (old != null) Object.DestroyImmediate(old);
            if (AssetDatabase.IsValidFolder(FootAssets)) foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { FootAssets })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            if (!build) { EditorSceneManager.MarkSceneDirty(main); EditorSceneManager.SaveScene(main); return "Assembly308 footing:clear: removed, backup " + dir; }
            if (!AssetDatabase.IsValidFolder(FootAssets)) AssetDatabase.CreateFolder("Assets/_Project/Art/World", "Assembly308");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(fc.footingMaterial); if (mat == null) return "REFUSED no material " + fc.footingMaterial;
            var cfg = ReadCfg(); var cache = new BuildingAudit308.MeshCache308(); var units = Gather(main, cfg, cache, null).Where(u => !u.supportOnly && u.cellFilter == null && Regex.IsMatch(u.family + ":" + u.id, fc.footingFor)).ToList();
            var root = new GameObject(FootRoot); SceneManager.MoveGameObjectToScene(root, main); int made = 0; long tris = 0; float tallest = 0f; var sb = new StringBuilder(); Physics.SyncTransforms(); float cs = fc.footingCell;
            foreach (var u in units)
            {
                var rot = Quaternion.Euler(0f, u.root.eulerAngles.y, 0f); var inv = Quaternion.Inverse(rot); var origin = u.root.position; var cells = new Dictionary<long, float>(); float minY = float.MaxValue;
                var soups = new List<(Vector3[] v, int[] t)>();
                foreach (var r in u.rs) { var mf = r.GetComponent<MeshFilter>(); var m = mf != null ? cache.Get(mf.sharedMesh) : null; if (m == null) continue; var w = r.transform.localToWorldMatrix; var v = new Vector3[m.v.Length]; for (int i = 0; i < v.Length; i++) { var p = w.MultiplyPoint3x4(m.v[i]); if (p.y < minY) minY = p.y; v[i] = inv * (p - origin); } soups.Add((v, m.t)); }
                if (soups.Count == 0) continue; float localMin = minY - origin.y;
                foreach (var (v, t) in soups) for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 p0 = v[t[i]], p1 = v[t[i + 1]], p2 = v[t[i + 2]]; if (Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)) > localMin + .3f) continue;
                    float ext = Mathf.Max(Mathf.Max(Mathf.Abs(p1.x - p0.x), Mathf.Abs(p2.x - p0.x)), Mathf.Max(Mathf.Abs(p1.z - p0.z), Mathf.Abs(p2.z - p0.z))); int n = Mathf.Clamp(Mathf.CeilToInt(ext / (cs * .6f)), 1, 64);
                    for (int ia = 0; ia <= n; ia++) for (int ib = 0; ib <= n - ia; ib++) { var p = p0 + (p1 - p0) * (ia / (float)n) + (p2 - p0) * (ib / (float)n); if (p.y > localMin + .3f) continue; cells[Key(Mathf.FloorToInt(p.x / cs), Mathf.FloorToInt(p.z / cs))] = 0f; }
                }
                // ground under every base cell; the building needs a plinth when its base hangs over the ground somewhere
                float worst = 0f; var ground = new Dictionary<long, float>();
                foreach (long k in cells.Keys)
                {
                    int x = (int)(k >> 32), z = (int)(uint)(k & 0xffffffff); var wp = origin + rot * new Vector3((x + .5f) * cs, 0f, (z + .5f) * cs); float terrain = float.NaN;
                    foreach (var hit in Physics.RaycastAll(new Vector3(wp.x, minY + 300f, wp.z), Vector3.down, 1200f, ~0, QueryTriggerInteraction.Ignore)) if (IsTerrain(hit.transform) && (float.IsNaN(terrain) || hit.point.y > terrain)) terrain = hit.point.y;
                    if (float.IsNaN(terrain)) continue; ground[k] = terrain; worst = Mathf.Max(worst, minY - terrain);
                }
                if (worst < fc.footingMinGap || ground.Count == 0) continue;
                var verts = new List<Vector3>(); var idx = new List<int>(); var uvs = new List<Vector2>(); var norms = new List<Vector3>();
                foreach (var row in ground.Keys.GroupBy(k => (int)(uint)(k & 0xffffffff)))
                {
                    var xs = row.Select(k => (int)(k >> 32)).OrderBy(x => x).ToList(); int z = row.Key; int i0 = 0;
                    while (i0 < xs.Count)
                    {
                        int i1 = i0; float low = ground[Key(xs[i0], z)]; while (i1 + 1 < xs.Count && xs[i1 + 1] == xs[i1] + 1) { i1++; low = Mathf.Min(low, ground[Key(xs[i1], z)]); }
                        bool edgeLo = !ground.ContainsKey(Key(xs[i0], z - 1)), edgeHi = !ground.ContainsKey(Key(xs[i0], z + 1));
                        var a = new Vector3(xs[i0] * cs - fc.footingLip, low - fc.footingSink - origin.y, z * cs - fc.footingLip); var b = new Vector3((xs[i1] + 1) * cs + fc.footingLip, localMin + .03f, (z + 1) * cs + fc.footingLip);
                        if (b.y - a.y > .05f) Box(verts, idx, norms, uvs, a, b); i0 = i1 + 1;
                    }
                }
                if (verts.Count == 0) continue;
                var mesh = new Mesh { name = "Footing_" + made.ToString("00", Inv) + "_" + u.root.name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 }; mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(idx, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                AssetDatabase.CreateAsset(mesh, FootAssets + "/" + mesh.name + ".asset"); AssetDatabase.SaveAssetIfDirty(mesh);
                var go = new GameObject("Footing_" + u.root.name); go.transform.SetParent(root.transform, false); go.transform.SetPositionAndRotation(origin, rot); go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; go.AddComponent<MeshCollider>().sharedMesh = mesh; go.layer = u.root.gameObject.layer;
                made++; tris += idx.Count / 3; tallest = Mathf.Max(tallest, worst + fc.footingSink); sb.Append("  ").Append(u.root.name.PadRight(30)).Append(" base over ground ").Append(F(worst)).Append(" m, triangles ").Append(idx.Count / 3).Append('\n');
            }
            EditorSceneManager.MarkSceneDirty(main); if (!EditorSceneManager.SaveScene(main)) return "FAILED the play scene was not saved";
            return "Assembly308 footing:build: " + made + " plinths of " + units.Count + " buildings, triangles " + tris + ", tallest " + F(tallest) + " m, backup " + dir + "\n" + sb;
        }
        static void Box(List<Vector3> v, List<int> idx, List<Vector3> n, List<Vector2> uv, Vector3 a, Vector3 b)
        {
            Vector3[] c = { new Vector3(a.x, a.y, a.z), new Vector3(b.x, a.y, a.z), new Vector3(b.x, a.y, b.z), new Vector3(a.x, a.y, b.z), new Vector3(a.x, b.y, a.z), new Vector3(b.x, b.y, a.z), new Vector3(b.x, b.y, b.z), new Vector3(a.x, b.y, b.z) };
            int[][] faces = { new[] { 0, 4, 5, 1 }, new[] { 1, 5, 6, 2 }, new[] { 2, 6, 7, 3 }, new[] { 3, 7, 4, 0 }, new[] { 4, 7, 6, 5 } }; Vector3[] normal = { Vector3.back, Vector3.right, Vector3.forward, Vector3.left, Vector3.up };
            for (int f = 0; f < 5; f++)
            {
                int s = v.Count; for (int k = 0; k < 4; k++) { var p = c[faces[f][k]]; v.Add(p); n.Add(normal[f]); uv.Add(f == 4 ? new Vector2(p.x, p.z) * .5f : f % 2 == 0 ? new Vector2(p.x, p.y) * .5f : new Vector2(p.z, p.y) * .5f); }
                idx.Add(s); idx.Add(s + 1); idx.Add(s + 2); idx.Add(s); idx.Add(s + 2); idx.Add(s + 3);
            }
        }
    }
}
