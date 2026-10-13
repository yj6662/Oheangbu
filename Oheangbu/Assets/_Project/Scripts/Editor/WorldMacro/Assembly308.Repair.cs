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
    // #308 assembly repairs, round 2.
    //   diag:<seq,seq,...>            colliders, materials, LOD levels and prefab source of units of the last scan (read only)
    //   footing:build | footing:clear  stone plinths (battered wall + cap stone course) under every unit of `footingFor` whose base hangs over
    //                                  its support (terrain or any other collider). All plinths live in the play scene root Assembly308Footing.
    //   seat:apply                     whole units of `seatFor` that hang clear of the ground are lowered onto it (ledger Ledger/moves.json; moves:revert)
    //   col:plan | col:apply | col:clear   rows of `colliderOps` in assembly308.json (drop:<id regex>:<collider name regex> / box:<id regex>) -> Ledger/colliders.json
    public static partial class Assembly308
    {
        [Serializable] sealed class RepCfg
        {
            public string footingFor = "", footingMaterial = "", seatFor = "", footingFlat = ""; public string[] footingMaterials = Array.Empty<string>(), colliderOps = Array.Empty<string>();
            public float footingCell = .5f, footingSink = .4f, footingMinGap = .06f, footingCapH = .16f, footingCapOut = .07f, footingBatter = .08f, footingMaxH = 9f, seatSink = .04f, seatLevel = .35f; public int footingGrow = 1;
            public float[] willReplacePoly = Array.Empty<float>();
        }
        static RepCfg ReadRep() => JsonUtility.FromJson<RepCfg>(File.ReadAllText(Path.Combine(Stage, "assembly308.json")).TrimStart('﻿'));
        internal static bool InPoly(float[] p, float x, float z)
        {
            if (p == null || p.Length < 6) return false; bool c = false; int n = p.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++) { float xi = p[i * 2], zi = p[i * 2 + 1], xj = p[j * 2], zj = p[j * 2 + 1]; if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) c = !c; }
            return c;
        }
        static bool UnderRoot(Transform t, Transform root) { for (; t != null; t = t.parent) if (t == root) return true; return false; }

        static string RunRepair(Scene main, string c, string[] a)
        {
            if (a[0] == "sansa" && a.Length > 1) return Sansa(main, a[1]);
            if (a[0] == "diag" && a.Length > 1) return Diag(main, a[1]);
            if (a[0] == "footing" && a.Length > 1 && (a[1] == "build" || a[1] == "clear")) return Footing2(main, a[1] == "build");
            if (c == "seat:apply") return Seat(main);
            if (a[0] == "col" && a.Length > 1) return ColliderOps(main, a[1]);
            return null;
        }

        // ------------------------------------------------------------------ diag
        static string Diag(Scene main, string seqs)
        {
            var want = new HashSet<string>(seqs.Split(',')); var rows = new List<(string seq, string id)>();
            foreach (string line in File.ReadAllLines(Path.Combine(OutDir, "units.csv")).Skip(1)) { var c = SplitCsv(line); if (want.Contains(c[0])) rows.Add((c[0], c[1])); }
            var sb = new StringBuilder();
            foreach (var grp in rows.GroupBy(r => r.id.Substring(0, r.id.IndexOf('/'))))
            {
                string sceneName = grp.Key; Scene opened = default; bool didOpen = false;
                if (sceneName.StartsWith("Realm_", StringComparison.Ordinal)) { string p = StreamDir + "/" + sceneName + ".unity"; var s = SceneManager.GetSceneByPath(p); if (!s.IsValid() || !s.isLoaded) { opened = EditorSceneManager.OpenScene(p, OpenSceneMode.Additive); didOpen = true; SceneManager.SetActiveScene(main); } }
                Physics.SyncTransforms();
                foreach (var (seq, id) in grp)
                {
                    var t = ResolveId(id); if (t == null) { sb.Append(seq).Append(" UNRESOLVED ").Append(id).Append('\n'); continue; }
                    BoundsOf(t, out var b); sb.Append("== ").Append(seq).Append(' ').Append(id).Append("  pos ").Append(t.position.ToString("F2")).Append(" bounds c ").Append(b.center.ToString("F1")).Append(" s ").Append(b.size.ToString("F1"));
                    if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) sb.Append("  prefab ").Append(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject));
                    sb.Append('\n'); int n = 0;
                    foreach (var col in t.GetComponentsInChildren<Collider>(true))
                    {
                        if (n++ >= 14) { sb.Append("   ... more colliders\n"); break; } var cb = col.bounds; var mc = col as MeshCollider;
                        sb.Append("   col ").Append(col.GetType().Name).Append(' ').Append(col.name).Append(col.enabled && col.gameObject.activeInHierarchy ? "" : " [off]").Append(col.isTrigger ? " [trigger]" : "").Append(" c ").Append(cb.center.ToString("F1")).Append(" s ").Append(cb.size.ToString("F1"));
                        if (mc != null && mc.sharedMesh != null) sb.Append(" mesh ").Append(mc.sharedMesh.name).Append(" v").Append(mc.sharedMesh.vertexCount).Append(" mb ").Append(mc.sharedMesh.bounds.size.ToString("F1")); sb.Append("  @").Append(IdOf(col.transform).Substring(Math.Max(0, IdOf(col.transform).Length - 70))).Append('\n');
                    }
                    var ext = Physics.OverlapBox(b.center, b.extents * .7f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Where(x => !UnderRoot(x.transform, t) && !IsTerrain(x.transform)).Take(5).ToList();
                    foreach (var x in ext) sb.Append("   near ").Append(x.GetType().Name).Append(' ').Append(IdOf(x.transform)).Append(" s ").Append(x.bounds.size.ToString("F1")).Append('\n');
                    var mats = t.GetComponentsInChildren<Renderer>(false).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Take(5).ToList();
                    foreach (var m in mats) sb.Append("   mat ").Append(AssetDatabase.GetAssetPath(m)).Append('\n');
                    foreach (var lod in t.GetComponentsInChildren<LODGroup>(false).Take(2))
                    {
                        var lods = lod.GetLODs(); sb.Append("   LODGroup ").Append(lod.name).Append(" levels ").Append(lods.Length);
                        for (int k = 0; k < lods.Length; k++) { Bounds bk = default; bool hk = false; int rn = 0; foreach (var r in lods[k].renderers ?? new Renderer[0]) if (r != null) { rn++; if (hk) bk.Encapsulate(r.bounds); else { bk = r.bounds; hk = true; } } sb.Append(" | L").Append(k).Append(" r").Append(rn).Append(hk ? " c " + bk.center.ToString("F2") + " s " + bk.size.ToString("F1") : " -"); }
                        sb.Append('\n');
                    }
                }
                if (didOpen) { if (opened.isDirty) return "REFUSED realm scene became dirty during diag"; EditorSceneManager.CloseScene(opened, true); }
            }
            File.WriteAllText(Path.Combine(OutDir, "diag.txt"), sb.ToString()); return sb.ToString();
        }

        // ------------------------------------------------------------------ base cells and their support
        sealed class BaseInfo { public Quaternion rot; public Vector3 origin; public float minY, localMin, worst, least = float.MaxValue, lift, ownLow = float.MaxValue; public HashSet<long> own; public Dictionary<long, float> support = new Dictionary<long, float>(); }
        static BaseInfo BaseOf(Unit u, BuildingAudit308.MeshCache308 cache, float cs, Transform footRoot, int grow = 0)
        {
            var bi = new BaseInfo { rot = Quaternion.Euler(0f, u.root.eulerAngles.y, 0f), origin = u.root.position }; var inv = Quaternion.Inverse(bi.rot); float minY = float.MaxValue; var soups = new List<(Vector3[] v, int[] t)>();
            foreach (var r in u.rs) { var mf = r.GetComponent<MeshFilter>(); var m = mf != null ? cache.Get(mf.sharedMesh) : null; if (m == null) continue; var w = r.transform.localToWorldMatrix; var v = new Vector3[m.v.Length]; for (int i = 0; i < v.Length; i++) { var p = w.MultiplyPoint3x4(m.v[i]); if (p.y < minY) minY = p.y; v[i] = inv * (p - bi.origin); } soups.Add((v, m.t)); }
            if (soups.Count == 0) return null; bi.minY = minY; bi.localMin = minY - bi.origin.y; var cells = new HashSet<long>(); var cellLow = new Dictionary<long, float>();
            foreach (var (v, t) in soups) for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 p0 = v[t[i]], p1 = v[t[i + 1]], p2 = v[t[i + 2]]; if (Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)) > bi.localMin + .3f) continue;
                float ext = Mathf.Max(Mathf.Max(Mathf.Abs(p1.x - p0.x), Mathf.Abs(p2.x - p0.x)), Mathf.Max(Mathf.Abs(p1.z - p0.z), Mathf.Abs(p2.z - p0.z))); int n = Mathf.Clamp(Mathf.CeilToInt(ext / (cs * .6f)), 1, 64);
                for (int ia = 0; ia <= n; ia++) for (int ib = 0; ib <= n - ia; ib++) { var p = p0 + (p1 - p0) * (ia / (float)n) + (p2 - p0) * (ib / (float)n); if (p.y > bi.localMin + .3f) continue; long ck = Key(Mathf.FloorToInt(p.x / cs), Mathf.FloorToInt(p.z / cs)); cells.Add(ck); if (!cellLow.TryGetValue(ck, out float cl) || p.y < cl) cellLow[ck] = p.y; }
            }
            foreach (var kv in cellLow) bi.lift = Mathf.Max(bi.lift, Mathf.Min(.25f, kv.Value - bi.localMin));
            var own = new HashSet<long>(cells); bi.own = own; for (int g = 0; g < grow; g++) { foreach (long k in cells.ToList()) { int gx = (int)(k >> 32), gz = (int)(uint)(k & 0xffffffff); for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) cells.Add(Key(gx + dx, gz + dz)); } }
            foreach (long k in cells)
            {
                int x = (int)(k >> 32), z = (int)(uint)(k & 0xffffffff); var wp = bi.origin + bi.rot * new Vector3((x + .5f) * cs, 0f, (z + .5f) * cs); float sup = float.NaN;
                foreach (var hit in Physics.RaycastAll(new Vector3(wp.x, minY + .45f, wp.z), Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (UnderRoot(hit.transform, u.root) || (footRoot != null && UnderRoot(hit.transform, footRoot))) continue; if (float.IsNaN(sup) || hit.point.y > sup) sup = hit.point.y;
                }
                if (float.IsNaN(sup)) continue; bi.support[k] = sup; if (!own.Contains(k)) continue; if (sup < bi.ownLow) bi.ownLow = sup; float gap = minY - sup; if (gap > bi.worst) bi.worst = gap; if (gap < bi.least) bi.least = gap;
            }
            return bi;
        }

        // ------------------------------------------------------------------ plinth mesh: battered wall, cap course standing proud of it
        static void Quad(List<Vector3> v, List<int> idx, List<Vector3> nrm, List<Vector2> uv, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            int s = v.Count; bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f; Vector3[] q = { a, b, c, d };
            foreach (var p in q) { v.Add(p); nrm.Add(n); uv.Add(Mathf.Abs(n.y) > .5f ? new Vector2(p.x, p.z) * .5f : Mathf.Abs(n.x) > .5f ? new Vector2(p.z, p.y) * .5f : new Vector2(p.x, p.y) * .5f); }
            if (flip) { idx.Add(s); idx.Add(s + 2); idx.Add(s + 1); idx.Add(s); idx.Add(s + 3); idx.Add(s + 2); } else { idx.Add(s); idx.Add(s + 1); idx.Add(s + 2); idx.Add(s); idx.Add(s + 2); idx.Add(s + 3); }
        }
        static Mesh PlinthMesh(Dictionary<long, float> bottom, float top, float cs, RepCfg fc, string name)
        {
            var v = new List<Vector3>(); var idx = new List<int>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); float yc = top - fc.footingCapH, co = fc.footingCapOut;
            Func<int, int, bool> has = (x, z) => bottom.TryGetValue(Key(x, z), out float bb) && top - bb > .05f;
            foreach (var kv in bottom)
            {
                float B = kv.Value; if (top - B <= .05f) continue; int x = (int)(kv.Key >> 32), z = (int)(uint)(kv.Key & 0xffffffff);
                bool L = !has(x - 1, z), R = !has(x + 1, z), D = !has(x, z - 1), U = !has(x, z + 1); float x0 = x * cs, x1 = (x + 1) * cs, z0 = z * cs, z1 = (z + 1) * cs;
                float cx0 = x0 - (L ? co : 0f), cx1 = x1 + (R ? co : 0f), cz0 = z0 - (D ? co : 0f), cz1 = z1 + (U ? co : 0f);
                Quad(v, idx, nrm, uv, new Vector3(cx0, top, cz0), new Vector3(cx0, top, cz1), new Vector3(cx1, top, cz1), new Vector3(cx1, top, cz0), Vector3.up);
                float h = Mathf.Max(0f, yc - B), bt = fc.footingBatter * h; bool wall = yc - B > .02f;
                if (L)
                {
                    Quad(v, idx, nrm, uv, new Vector3(cx0, yc, cz0), new Vector3(cx0, top, cz0), new Vector3(cx0, top, cz1), new Vector3(cx0, yc, cz1), Vector3.left);
                    Quad(v, idx, nrm, uv, new Vector3(cx0, yc, cz0), new Vector3(cx0, yc, cz1), new Vector3(x0, yc, cz1), new Vector3(x0, yc, cz0), Vector3.down);
                    if (wall) Quad(v, idx, nrm, uv, new Vector3(x0 - bt, B, z0 - (D ? bt : 0f)), new Vector3(x0, yc, z0), new Vector3(x0, yc, z1), new Vector3(x0 - bt, B, z1 + (U ? bt : 0f)), Vector3.left);
                }
                if (R)
                {
                    Quad(v, idx, nrm, uv, new Vector3(cx1, yc, cz0), new Vector3(cx1, top, cz0), new Vector3(cx1, top, cz1), new Vector3(cx1, yc, cz1), Vector3.right);
                    Quad(v, idx, nrm, uv, new Vector3(cx1, yc, cz0), new Vector3(cx1, yc, cz1), new Vector3(x1, yc, cz1), new Vector3(x1, yc, cz0), Vector3.down);
                    if (wall) Quad(v, idx, nrm, uv, new Vector3(x1 + bt, B, z0 - (D ? bt : 0f)), new Vector3(x1, yc, z0), new Vector3(x1, yc, z1), new Vector3(x1 + bt, B, z1 + (U ? bt : 0f)), Vector3.right);
                }
                if (D)
                {
                    Quad(v, idx, nrm, uv, new Vector3(cx0, yc, cz0), new Vector3(cx0, top, cz0), new Vector3(cx1, top, cz0), new Vector3(cx1, yc, cz0), Vector3.back);
                    Quad(v, idx, nrm, uv, new Vector3(cx0, yc, cz0), new Vector3(cx1, yc, cz0), new Vector3(cx1, yc, z0), new Vector3(cx0, yc, z0), Vector3.down);
                    if (wall) Quad(v, idx, nrm, uv, new Vector3(x0 - (L ? bt : 0f), B, z0 - bt), new Vector3(x0, yc, z0), new Vector3(x1, yc, z0), new Vector3(x1 + (R ? bt : 0f), B, z0 - bt), Vector3.back);
                }
                if (U)
                {
                    Quad(v, idx, nrm, uv, new Vector3(cx0, yc, cz1), new Vector3(cx0, top, cz1), new Vector3(cx1, top, cz1), new Vector3(cx1, yc, cz1), Vector3.forward);
                    Quad(v, idx, nrm, uv, new Vector3(cx0, yc, cz1), new Vector3(cx1, yc, cz1), new Vector3(cx1, yc, z1), new Vector3(cx0, yc, z1), Vector3.down);
                    if (wall) Quad(v, idx, nrm, uv, new Vector3(x0 - (L ? bt : 0f), B, z1 + bt), new Vector3(x0, yc, z1), new Vector3(x1, yc, z1), new Vector3(x1 + (R ? bt : 0f), B, z1 + bt), Vector3.forward);
                }
            }
            if (v.Count == 0) return null;
            var mesh = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 }; mesh.SetVertices(v); mesh.SetNormals(nrm); mesh.SetUVs(0, uv); mesh.SetTriangles(idx, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
        }

        static string Footing2(Scene main, bool build)
        {
            var fc = ReadRep(); var old = main.GetRootGameObjects().FirstOrDefault(g => g.name == FootRoot);
            if (!build && old == null) return "Assembly308 footing:clear: root absent"; string dir = Backup(main, "footing");
            if (old != null) Object.DestroyImmediate(old);
            if (AssetDatabase.IsValidFolder(FootAssets)) foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { FootAssets })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            if (!build) { EditorSceneManager.MarkSceneDirty(main); EditorSceneManager.SaveScene(main); return "Assembly308 footing:clear: removed, backup " + dir; }
            if (!AssetDatabase.IsValidFolder(FootAssets)) AssetDatabase.CreateFolder("Assets/_Project/Art/World", "Assembly308");
            var defMat = AssetDatabase.LoadAssetAtPath<Material>(fc.footingMaterial); if (defMat == null) return "REFUSED no material " + fc.footingMaterial;
            var mats = new List<(Regex re, Material m)>(); foreach (string row in fc.footingMaterials) { int bar = row.LastIndexOf('|'); var m = AssetDatabase.LoadAssetAtPath<Material>(row.Substring(bar + 1)); if (m == null) return "REFUSED no material " + row.Substring(bar + 1); mats.Add((new Regex(row.Substring(0, bar)), m)); }
            var cfg = ReadCfg(); var root = new GameObject(FootRoot); SceneManager.MoveGameObjectToScene(root, main); int made = 0, seen = 0; long tris = 0; float tallest = 0f; var sb = new StringBuilder(); float cs = fc.footingCell; string stamp = DateTime.Now.ToString("HHmmss", Inv);
            var mainCache = new BuildingAudit308.MeshCache308(); var mainUnits = Gather(main, cfg, mainCache, null); var done = new HashSet<Unit>(); var skipped = new StringBuilder();
            Action<Unit, BuildingAudit308.MeshCache308> one = (u, cache) =>
            {
                if (u.supportOnly || u.cellFilter != null || u.prot || u.kind == "ruin" || !Regex.IsMatch(u.family + ":" + u.id, fc.footingFor)) return;
                if (InPoly(fc.willReplacePoly, u.b.center.x, u.b.center.z)) return; seen++;
                bool flat = fc.footingFlat.Length > 0 && Regex.IsMatch(u.family + ":" + u.id, fc.footingFlat); var bi = BaseOf(u, cache, cs, root.transform, flat ? 0 : fc.footingGrow); if (bi != null && flat) bi.lift = 0f; if (bi == null || bi.support.Count == 0 || bi.worst < fc.footingMinGap) return;
                if (bi.worst > fc.footingMaxH) { skipped.Append("  SKIPPED ").Append(u.id).Append(" base over support ").Append(F(bi.worst)).Append(" m (over footingMaxH)\n"); return; }
                var bottom = new Dictionary<long, float>(); foreach (var kv in bi.support) { if (!bi.own.Contains(kv.Key) && kv.Value < bi.ownLow - .5f) continue; bottom[kv.Key] = kv.Value - fc.footingSink - bi.origin.y; }
                var mesh = PlinthMesh(bottom, bi.localMin + .03f + bi.lift, cs, fc, "Footing_" + stamp + "_" + made.ToString("00", Inv) + "_" + u.root.name); if (mesh == null) return;
                AssetDatabase.CreateAsset(mesh, FootAssets + "/" + mesh.name + ".asset"); AssetDatabase.SaveAssetIfDirty(mesh);
                var mat = defMat; string tag = u.family + ":" + u.id; foreach (var (re, m) in mats) if (re.IsMatch(tag)) { mat = m; break; }
                var go = new GameObject("Footing_" + u.root.name); go.transform.SetParent(root.transform, false); go.transform.SetPositionAndRotation(bi.origin, bi.rot); go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = mat; go.AddComponent<MeshCollider>().sharedMesh = mesh; go.layer = u.root.gameObject.layer;
                int tn = (int)(mesh.GetIndexCount(0) / 3); made++; tris += tn; tallest = Mathf.Max(tallest, bi.worst + fc.footingSink); sb.Append("  ").Append(u.id.Substring(Math.Max(0, u.id.Length - 44)).PadRight(44)).Append(" base over support ").Append(F(bi.worst)).Append(" m, triangles ").Append(tn).Append(", ").Append(mat.name).Append('\n');
            };
            EachScene(main, s =>
            {
                var cache = new BuildingAudit308.MeshCache308(); var us = Gather(s, cfg, cache, null); if (us.Count == 0) return; var rb = us[0].b; foreach (var u in us) rb.Encapsulate(u.b); rb.Expand(new Vector3(60f, 4000f, 60f));
                foreach (var u in us) one(u, cache);
                foreach (var u in mainUnits) if (!done.Contains(u) && rb.Contains(u.b.center)) { done.Add(u); one(u, mainCache); }
            }, false);
            Physics.SyncTransforms(); foreach (var u in mainUnits) if (!done.Contains(u)) one(u, mainCache);
            EditorSceneManager.MarkSceneDirty(main); if (!EditorSceneManager.SaveScene(main)) return "FAILED the play scene was not saved";
            return "Assembly308 footing:build: " + made + " plinths of " + seen + " units, triangles " + tris + ", tallest " + F(tallest) + " m, backup " + dir + "\n" + sb + skipped;
        }

        // ------------------------------------------------------------------ whole units that hang clear of the ground are lowered onto it
        static string Seat(Scene main)
        {
            var fc = ReadRep(); if (fc.seatFor.Length == 0) return "REFUSED seatFor is empty"; var cfg = ReadCfg(); var sb = new StringBuilder(); int n = 0;
            var led = File.Exists(MoveFile) ? JsonUtility.FromJson<MoveLedger>(File.ReadAllText(MoveFile)) : new MoveLedger(); var footRoot = main.GetRootGameObjects().FirstOrDefault(g => g.name == FootRoot);
            Func<Scene, int> pass = s =>
            {
                var cache = new BuildingAudit308.MeshCache308(); int k = 0; Physics.SyncTransforms();
                foreach (var u in Gather(s, cfg, cache, null))
                {
                    if (u.supportOnly || u.cellFilter != null || u.prot || u.kind == "ruin" || !Regex.IsMatch(u.family + ":" + u.id, fc.seatFor) || InPoly(fc.willReplacePoly, u.b.center.x, u.b.center.z)) continue;
                    var bi = BaseOf(u, cache, fc.footingCell, footRoot != null ? footRoot.transform : null); if (bi == null || bi.support.Count == 0 || bi.least > 3f || (bi.least < .08f && !(bi.worst - bi.least <= fc.seatLevel && bi.worst >= .08f))) { sb.Append("  left ").Append(u.id).Append(bi == null ? " (no mesh)" : " gaps " + F(bi.least) + " - " + F(bi.worst)).Append('\n'); continue; }
                    float d = (bi.worst - bi.least <= fc.seatLevel ? bi.worst : bi.least) + fc.seatSink; var row = led.rows.FirstOrDefault(r => r.id == u.id); if (row == null) { row = new MoveRow { id = u.id, before = u.root.position }; led.rows.Add(row); }
                    u.root.position += Vector3.down * d; row.after = u.root.position; k++; sb.Append("  ").Append(u.id).Append(" down ").Append(F(d)).Append(" m (gaps were ").Append(F(bi.least)).Append(" - ").Append(F(bi.worst)).Append(")\n");
                }
                return k;
            };
            string dir = ""; int km = pass(main); n += km;
            foreach (string p in RealmScenePaths())
            {
                var s = SceneManager.GetSceneByPath(p); if (s.IsValid() && s.isLoaded) return "REFUSED close the realm scenes first"; s = EditorSceneManager.OpenScene(p, OpenSceneMode.Additive); SceneManager.SetActiveScene(main);
                int k = pass(s); n += k; if (k > 0) { dir = Backup(s, "seat"); EditorSceneManager.MarkSceneDirty(s); if (!EditorSceneManager.SaveScene(s)) return "FAILED not saved " + p; }
                EditorSceneManager.CloseScene(s, true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(MoveFile)); File.WriteAllText(MoveFile, JsonUtility.ToJson(led, true));
            if (km > 0) { dir = Backup(main, "seat"); EditorSceneManager.MarkSceneDirty(main); if (!EditorSceneManager.SaveScene(main)) return "FAILED the play scene was not saved"; }
            return "Assembly308 seat:apply: " + n + " lowered, last backup " + dir + "\n" + sb;
        }

        // ------------------------------------------------------------------ colliders
        [Serializable] sealed class ColRow { public string op = "", id = "", name = ""; public Vector3 center, size; public bool wasEnabled; }
        [Serializable] sealed class ColLedger { public List<ColRow> rows = new List<ColRow>(); }
        static string ColFile => Path.Combine(Stage, "Ledger", "colliders.json");
        const string ColPrefix = "Col308_";
        static string ColliderOps(Scene main, string mode)
        {
            var fc = ReadRep(); var cfg = ReadCfg(); var sb = new StringBuilder(); bool apply = mode == "apply", clear = mode == "clear"; if (!apply && !clear && mode != "plan") return "REFUSED col:plan | col:apply | col:clear";
            var led = File.Exists(ColFile) ? JsonUtility.FromJson<ColLedger>(File.ReadAllText(ColFile)) : new ColLedger(); int nDrop = 0, nBox = 0, nUndo = 0;
            var ops = fc.colliderOps.Select(r => r.Split(new[] { ':' }, 3)).Where(p => p.Length >= 2).ToList();
            Func<Scene, bool> pass = s =>
            {
                bool changed = false; Physics.SyncTransforms();
                if (clear)
                {
                    foreach (var row in led.rows.ToList())
                    {
                        if (!row.id.StartsWith(s.name + "/", StringComparison.Ordinal)) continue; var t = ResolveId(row.id); if (t == null) continue;
                        if (row.op == "drop") { foreach (var col in t.GetComponents<Collider>()) if (!col.enabled) { col.enabled = true; changed = true; nUndo++; } }
                        else if (row.op == "dedup") { var r = t.GetComponent<Renderer>(); if (r != null && !r.enabled) { r.enabled = true; changed = true; nUndo++; } }
                        else { for (int i = t.childCount - 1; i >= 0; i--) if (t.GetChild(i).name.StartsWith(ColPrefix, StringComparison.Ordinal)) { Object.DestroyImmediate(t.GetChild(i).gameObject); changed = true; nUndo++; } }
                        led.rows.Remove(row);
                    }
                    return changed;
                }
                var cache = new BuildingAudit308.MeshCache308(); var units = Gather(s, cfg, cache, null);
                foreach (var op in ops)
                {
                    var re = new Regex(op[1]);
                    foreach (var u in units)
                    {
                        if (u.prot || !re.IsMatch(u.family + ":" + u.id) || InPoly(fc.willReplacePoly, u.b.center.x, u.b.center.z) && op[0] != "drop") continue;
                        if (op[0] == "drop")
                        {
                            var nre = new Regex(op.Length > 2 ? op[2] : ".*");
                            foreach (var col in u.root.GetComponentsInChildren<Collider>(false))
                            {
                                if (!col.enabled || col.isTrigger || !nre.IsMatch(col.name)) continue; var cb = col.bounds; var rb = u.b; float over = Mathf.Max(Mathf.Max(rb.min.x - cb.min.x, cb.max.x - rb.max.x), Mathf.Max(rb.min.z - cb.min.z, cb.max.z - rb.max.z)); if (over < 20f) continue;
                                sb.Append("  drop ").Append(IdOf(col.transform)).Append(' ').Append(col.GetType().Name).Append(" size ").Append(cb.size.ToString("F0")).Append(" (").Append(F1(over)).Append(" m outside the drawn body)\n"); nDrop++;
                                if (apply) { col.enabled = false; EditorUtility.SetDirty(col); led.rows.Add(new ColRow { op = "drop", id = IdOf(col.transform), name = col.name, center = cb.center, size = cb.size, wasEnabled = true }); changed = true; }
                            }
                        }
                        else if (op[0] == "dedup")
                        {
                            var big = u.rs.Where(r => r.enabled && r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null).ToList();
                            for (int i = 0; i < big.Count; i++) for (int j = i + 1; j < big.Count; j++)
                            {
                                if (!big[j].enabled || big[i].GetComponent<MeshFilter>().sharedMesh != big[j].GetComponent<MeshFilter>().sharedMesh || (big[i].transform.position - big[j].transform.position).sqrMagnitude > cfg.dupM * cfg.dupM || Quaternion.Angle(big[i].transform.rotation, big[j].transform.rotation) > 2f || (big[i].transform.lossyScale - big[j].transform.lossyScale).magnitude > .02f) continue;
                                sb.Append("  dedup ").Append(IdOf(big[j].transform)).Append(" (same mesh as ").Append(big[i].name).Append(")\n"); nDrop++;
                                if (apply) { big[j].enabled = false; EditorUtility.SetDirty(big[j]); led.rows.Add(new ColRow { op = "dedup", id = IdOf(big[j].transform), name = big[j].name, wasEnabled = true }); changed = true; }
                            }
                        }
                        else if (op[0] == "box")
                        {
                            if (u.root.GetComponentsInChildren<Collider>(false).Any(c => c.enabled && !c.isTrigger) || u.supportOnly || u.cellFilter != null) continue;
                            var boxes = WallBoxes(u, cache); if (boxes.Count == 0) continue; sb.Append("  box ").Append(u.id).Append(" x").Append(boxes.Count).Append('\n'); nBox++;
                            if (!apply) continue; int k = 0;
                            foreach (var (c, size) in boxes) { var go = new GameObject(ColPrefix + k++); go.transform.SetParent(u.root, false); go.layer = u.root.gameObject.layer; var bc = go.AddComponent<BoxCollider>(); go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; var ls = u.root.lossyScale; go.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z); go.transform.rotation = Quaternion.Euler(0f, u.root.eulerAngles.y, 0f); bc.center = c; bc.size = size; }
                            led.rows.Add(new ColRow { op = "box", id = u.id, name = ColPrefix }); changed = true;
                        }
                    }
                }
                return changed;
            };
            string dir = ""; bool cm = pass(main);
            foreach (string p in RealmScenePaths())
            {
                var s = SceneManager.GetSceneByPath(p); if (s.IsValid() && s.isLoaded) return "REFUSED close the realm scenes first"; s = EditorSceneManager.OpenScene(p, OpenSceneMode.Additive); SceneManager.SetActiveScene(main);
                bool ch = pass(s); if (ch && (apply || clear)) { dir = Backup(s, "col"); EditorSceneManager.MarkSceneDirty(s); if (!EditorSceneManager.SaveScene(s)) return "FAILED not saved " + p; }
                EditorSceneManager.CloseScene(s, true);
            }
            if (cm && (apply || clear)) { dir = Backup(main, "col"); EditorSceneManager.MarkSceneDirty(main); if (!EditorSceneManager.SaveScene(main)) return "FAILED the play scene was not saved"; }
            if (apply || clear) { Directory.CreateDirectory(Path.GetDirectoryName(ColFile)); File.WriteAllText(ColFile, JsonUtility.ToJson(led, true)); }
            return "Assembly308 col:" + mode + ": drop " + nDrop + ", box units " + nBox + ", undone " + nUndo + (dir.Length > 0 ? ", last backup " + dir : "") + "\n" + sb;
        }

        /// <summary>Boxes (centre, size in the unit's yaw frame, metres) around the wall band of a building: 0.6 - 2.2 m over its base, cells of 0.5 m, rows merged.</summary>
        static List<(Vector3 c, Vector3 size)> WallBoxes(Unit u, BuildingAudit308.MeshCache308 cache)
        {
            var res = new List<(Vector3, Vector3)>(); var rot = Quaternion.Euler(0f, u.root.eulerAngles.y, 0f); var inv = Quaternion.Inverse(rot); var origin = u.root.position; float minY = float.MaxValue; const float cs = .5f; var soups = new List<(Vector3[] v, int[] t)>();
            foreach (var r in u.rs) { var mf = r.GetComponent<MeshFilter>(); var m = mf != null ? cache.Get(mf.sharedMesh) : null; if (m == null) continue; var w = r.transform.localToWorldMatrix; var v = new Vector3[m.v.Length]; for (int i = 0; i < v.Length; i++) { var p = w.MultiplyPoint3x4(m.v[i]); if (p.y < minY) minY = p.y; v[i] = inv * (p - origin); } soups.Add((v, m.t)); }
            if (soups.Count == 0) return res; float lo = minY - origin.y + .6f, hi = minY - origin.y + 2.2f; var cells = new HashSet<long>();
            foreach (var (v, t) in soups) for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 p0 = v[t[i]], p1 = v[t[i + 1]], p2 = v[t[i + 2]]; if (Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y)) < lo || Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)) > hi) continue;
                float ext = Mathf.Max(Mathf.Max(Mathf.Abs(p1.x - p0.x), Mathf.Abs(p2.x - p0.x)), Mathf.Max(Mathf.Abs(p1.z - p0.z), Mathf.Abs(p2.z - p0.z))); int n = Mathf.Clamp(Mathf.CeilToInt(ext / (cs * .6f)), 1, 64);
                for (int ia = 0; ia <= n; ia++) for (int ib = 0; ib <= n - ia; ib++) { var p = p0 + (p1 - p0) * (ia / (float)n) + (p2 - p0) * (ib / (float)n); if (p.y < lo || p.y > hi) continue; cells.Add(Key(Mathf.FloorToInt(p.x / cs), Mathf.FloorToInt(p.z / cs))); }
            }
            if (cells.Count == 0) return res;
            // fill each row between its outermost wall cells (a house is solid), then merge equal runs of neighbouring rows
            var rows = new SortedDictionary<int, (int a, int b)>();
            foreach (long k in cells) { int x = (int)(k >> 32), z = (int)(uint)(k & 0xffffffff); if (rows.TryGetValue(z, out var e)) rows[z] = (Math.Min(e.a, x), Math.Max(e.b, x)); else rows[z] = (x, x); }
            var list = rows.ToList(); int i0 = 0;
            while (i0 < list.Count)
            {
                int i1 = i0; int a = list[i0].Value.a, b = list[i0].Value.b;
                while (i1 + 1 < list.Count && list[i1 + 1].Key == list[i1].Key + 1 && Math.Abs(list[i1 + 1].Value.a - a) <= 2 && Math.Abs(list[i1 + 1].Value.b - b) <= 2) { i1++; a = Math.Max(a, list[i1].Value.a); b = Math.Min(b, list[i1].Value.b); }
                float x0 = a * cs, x1 = (b + 1) * cs, z0 = list[i0].Key * cs, z1 = (list[i1].Key + 1) * cs;
                if (x1 - x0 > .6f && z1 - z0 > .2f) res.Add((new Vector3((x0 + x1) * .5f, minY - origin.y + 1.5f, (z0 + z1) * .5f), new Vector3(x1 - x0, 3f, z1 - z0)));
                i0 = i1 + 1;
            }
            return res.Count > 12 ? res.OrderByDescending(r => r.Item2.x * r.Item2.z).Take(12).ToList() : res;
        }
    }
}
