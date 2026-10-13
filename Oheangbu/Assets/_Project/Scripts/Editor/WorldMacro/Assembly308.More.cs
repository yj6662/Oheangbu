using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 assembly audit: units, measurements, ledger files. See ASSEMBLY_RULES.md.
    //   scan[:<Realm|Main>]          every building unit of the play scene and of each realm scene (opened one at a time)  -> Out/units.csv, findings.csv, summary.txt (read only)
    //   check:<path prefix>          the units of the OPEN scenes whose id starts with the prefix (e.g. check:Realm_Hwanggyeong/Hanyang308)  -> PASS / FAIL lines
    //   check:<x0>,<z0>,<x1>,<z1>    the units of the OPEN scenes whose centre lies in the rectangle
    //   shots:<tag>:<seq,seq,...>    Presentation297 shot lines for units of the last scan -> Out/shots_<tag>.txt
    //   open:<Realm> | close         one realm scene additively (for stills); close refuses when it is dirty
    public static partial class Assembly308
    {
        [Serializable] sealed class Cfg
        {
            public float cell = 1f, floatM = .10f, buriedM = .40f, vgapM = .05f, roofOffM = .10f, dupM = .3f, lodOffM = .3f, farOffM = 1.5f, colM = .5f, tiltDeg = 2f, unitMaxM = 45f, realmUnitMaxM = 90f, minH = 2.2f, minSpan = 3f, smallSpan = 7f, outlierM = 2f, overlapCells = 4f, floatFrac = .1f, buriedFrac = .25f;
            public float roofOffSpan = .04f, baseBandM = .3f; public string[] skirts = Array.Empty<string>(), mainRoots = Array.Empty<string>(), protectedParts = Array.Empty<string>(), clusterRoots = Array.Empty<string>(), skipFamilies = Array.Empty<string>();
            public string ruin = "", structure = "", foundation = "", prop = "", noGround = "", willReplace = ""; public float[] willReplacePoly = Array.Empty<float>(); public string[] accept = Array.Empty<string>();
        }
        [Serializable] sealed class ManUnit { public string realm = "", name = "", parentPath = ""; }
        [Serializable] sealed class Man { public ManUnit[] units = Array.Empty<ManUnit>(); }
        static Cfg ReadCfg() => JsonUtility.FromJson<Cfg>(File.ReadAllText(Path.Combine(Stage, "assembly308.json")).TrimStart('﻿'));

        sealed class Chunk { public Vector3[] v; public int[] t; }
        sealed class Unit
        {
            public int seq; public string id = "", scene = "", realm = "", family = "", kind = "", mesh0 = ""; public Transform root; public Transform selfRoot; public Bounds b; public int rend, verts, nBase, cols, dups, outliers, overlapCells, nCells;
            public float floatMax, floatFr, buriedMax, buriedFr, relief, vgap, vgapY, vgapAbove, qgap, roofOff, tilt, lodOff, farOff = -1f, colOver, outMax, baseY, terrainHi; public bool blocked = true, prot, read = true; public string overlapWith = "", note = "";
            public float eaveX, eaveZ; public bool supportOnly; public HashSet<long> cellFilter; public List<Renderer> rs = new List<Renderer>();
            public Dictionary<long, Vector2> cells;
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
        static bool IsTerrain(Transform t) => t.root.name.StartsWith("Reworld292_Terrain", StringComparison.Ordinal);
        static string IdOf(Transform t) { var parts = new List<string>(); for (; t.parent != null; t = t.parent) parts.Add(t.name + "#" + t.GetSiblingIndex()); parts.Add(t.name); parts.Reverse(); return t.gameObject.scene.name + "/" + string.Join("/", parts); }
        internal static Transform ResolveId(string id)
        {
            int slash = id.IndexOf('/'); var scene = SceneManager.GetSceneByName(id.Substring(0, slash)); if (!scene.IsValid() || !scene.isLoaded) return null; var parts = id.Substring(slash + 1).Split('/');
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == parts[0]); if (root == null) return null; var t = root.transform;
            for (int i = 1; i < parts.Length && t != null; i++) { string p = parts[i]; int hash = p.LastIndexOf('#'); if (hash < 0 || !int.TryParse(p.Substring(hash + 1), out int k) || k >= t.childCount || t.GetChild(k).name != p.Substring(0, hash)) return null; t = t.GetChild(k); }
            return t;
        }

        static string RunMore(Scene main, string c, string[] a)
        {
            if (a[0] == "scan") { var cfg = ReadCfg(); var units = ScanAll(main, cfg, a.Length > 1 ? a[1] : null); return WriteOut(cfg, units, a.Length > 1 ? "_" + a[1] : ""); }
            if (a[0] == "check")
            {
                if (a.Length < 2) return "REFUSED check:<path prefix> or check:<x0>,<z0>,<x1>,<z1>";
                var cfg = ReadCfg(); var units = new List<Unit>(); var cache = new BuildingAudit308.MeshCache308(); string arg = c.Substring(6); Func<Unit, bool> keep; string prefix = null;
                var n = arg.Split(','); if (n.Length == 4 && n.All(x => float.TryParse(x, System.Globalization.NumberStyles.Float, Inv, out _))) { var f = n.Select(x => float.Parse(x, Inv)).ToArray(); keep = u => u.b.center.x >= Mathf.Min(f[0], f[2]) && u.b.center.x <= Mathf.Max(f[0], f[2]) && u.b.center.z >= Mathf.Min(f[1], f[3]) && u.b.center.z <= Mathf.Max(f[1], f[3]); }
                else { keep = u => u.id.StartsWith(arg, StringComparison.Ordinal); prefix = arg; }
                Physics.SyncTransforms(); var support = new Dictionary<long, List<Span>>(); var all = new List<Unit>();
                for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.isLoaded) all.AddRange(Gather(s, cfg, cache, prefix)); }
                Prepass(all, support, cache); foreach (var u in all) if (keep(u)) { Measure(cfg, u, cache, support); if (u.rend > 0) units.Add(u); }
                Finish(main, cfg, units); return CheckReport(cfg, units, arg);
            }
            if (a[0] == "open") { string p = StreamDir + "/Realm_" + a[1] + ".unity"; if (!File.Exists(Path.Combine(Repo, "Oheangbu", p))) return "REFUSED no scene " + p; if (!SceneManager.GetSceneByPath(p).isLoaded) { EditorSceneManager.OpenScene(p, OpenSceneMode.Additive); SceneManager.SetActiveScene(main); } return "Assembly308 open: " + p + ", open scenes " + SceneManager.sceneCount; }
            if (a[0] == "close") { int n = 0; foreach (string p in RealmScenePaths()) { var s = SceneManager.GetSceneByPath(p); if (s.IsValid() && s.isLoaded) { if (s.isDirty) return "REFUSED " + p + " has unsaved changes"; EditorSceneManager.CloseScene(s, true); n++; } } EditorUtility.UnloadUnusedAssetsImmediate(true); return "Assembly308 close: closed " + n + ", open scenes " + SceneManager.sceneCount; }
            if (a[0] == "shots") return Shots(a[1], a[2]);
            if (a[0] == "where")
            {
                var sb = new StringBuilder(); int n = 0;
                for (int i = 0; i < SceneManager.sceneCount; i++) foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.IndexOf(a[1], StringComparison.OrdinalIgnoreCase) < 0 || n++ > 30) continue; bool has = BoundsOf(t, out var b);
                    sb.Append(IdOf(t)).Append(t.gameObject.activeInHierarchy ? "" : " [OFF]").Append(" pos ").Append(t.position.ToString("F2")).Append(" rot ").Append(t.eulerAngles.ToString("F0")).Append(" scale ").Append(t.lossyScale.ToString("F2")).Append(has ? " bounds c " + b.center.ToString("F1") + " s " + b.size.ToString("F1") : " (not drawn)").Append('\n');
                }
                return sb.ToString();
            }
            return RunFix(main, c, a);
        }

        // ------------------------------------------------------------------ units
        static Dictionary<string, string> manFamily;
        sealed class Span { public float lo, hi; public int owner; }
        static List<Unit> ScanAll(Scene main, Cfg cfg, string only)
        {
            var units = new List<Unit>(); var support = new Dictionary<long, List<Span>>(); var mainCache = new BuildingAudit308.MeshCache308(); var mainUnits = only == null || only == "Main" ? Gather(main, cfg, mainCache, null) : new List<Unit>(); Prepass(mainUnits, support, mainCache); var done = new HashSet<Unit>();
            EachScene(main, s =>
            {
                var cache = new BuildingAudit308.MeshCache308(); var us = Gather(s, cfg, cache, null); Prepass(us, support, cache); if (us.Count == 0) return; var rb = us[0].b; foreach (var u in us) rb.Encapsulate(u.b); rb.Expand(new Vector3(60f, 4000f, 60f));
                foreach (var u in us) { Measure(cfg, u, cache, support); if (u.rend > 0) units.Add(u); } Far(s, us);
                foreach (var u in mainUnits) if (!done.Contains(u) && rb.Contains(u.b.center)) { Measure(cfg, u, mainCache, support); done.Add(u); if (u.rend > 0) units.Add(u); }
                foreach (var u in us) { u.root = null; u.selfRoot = null; u.rs = null; }
            }, false, only == "Main" ? "-" : only);
            foreach (var u in mainUnits) if (!done.Contains(u)) { Measure(cfg, u, mainCache, support); if (u.rend > 0) units.Add(u); }
            Finish(main, cfg, units); return units;
        }
        static void Prepass(List<Unit> us, Dictionary<long, List<Span>> support, BuildingAudit308.MeshCache308 cache)
        {
            foreach (var u in us)
            {
                if (u.kind != "foundation") continue;
                foreach (var r in u.rs)
                {
                    var mf = r.GetComponent<MeshFilter>(); var m = mf != null ? cache.Get(mf.sharedMesh) : null; if (m == null) continue; var w = r.transform.localToWorldMatrix; int owner = r.GetInstanceID(); var local = new Dictionary<long, Span>();
                    for (int i = 0; i + 2 < m.t.Length; i += 3)
                    {
                        Vector3 p0 = w.MultiplyPoint3x4(m.v[m.t[i]]), p1 = w.MultiplyPoint3x4(m.v[m.t[i + 1]]), p2 = w.MultiplyPoint3x4(m.v[m.t[i + 2]]);
                        float ext = Mathf.Max(Mathf.Max(Mathf.Abs(p1.x - p0.x), Mathf.Abs(p2.x - p0.x)), Mathf.Max(Mathf.Abs(p1.z - p0.z), Mathf.Abs(p2.z - p0.z))); int n = Mathf.Clamp(Mathf.CeilToInt(ext / .7f), 1, 64);
                        for (int ia = 0; ia <= n; ia++) for (int ib = 0; ib <= n - ia; ib++) { var p = p0 + (p1 - p0) * (ia / (float)n) + (p2 - p0) * (ib / (float)n); long k = Key(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z)); if (local.TryGetValue(k, out var e)) { if (p.y < e.lo) e.lo = p.y; if (p.y > e.hi) e.hi = p.y; } else local[k] = new Span { lo = p.y, hi = p.y, owner = owner }; }
                    }
                    foreach (var kv in local) { if (!support.TryGetValue(kv.Key, out var l)) support[kv.Key] = l = new List<Span>(1); l.Add(kv.Value); }
                }
            }
        }
        static List<Unit> Gather(Scene s, Cfg cfg, BuildingAudit308.MeshCache308 cache, string prefix)
        {
            var units = new List<Unit>(); bool isMain = s.path == MainPath; var found = new List<(Transform t, string family, Transform self)>();
            if (manFamily == null || true)
            {
                manFamily = new Dictionary<string, string>(); string mf = Path.Combine(Repo, "Tools", "Unity", "Stage308_stream", "manifest.json");
                if (File.Exists(mf)) foreach (var u in JsonUtility.FromJson<Man>(File.ReadAllText(mf).TrimStart('﻿')).units) { string k = u.realm + "/" + u.name; if (!manFamily.ContainsKey(k)) manFamily[k] = u.parentPath.Split('/')[0] + (u.parentPath.StartsWith("Realms308Dressing", StringComparison.Ordinal) ? "" : ""); }
            }
            foreach (var root in s.GetRootGameObjects())
            {
                if (!root.activeInHierarchy) continue;
                if (isMain)
                {
                    bool listed = cfg.mainRoots.Contains(root.name) || (prefix != null && (prefix + "/").StartsWith(s.name + "/" + root.name + "/", StringComparison.Ordinal)); if (!listed) continue;
                    if (cfg.clusterRoots.Contains(root.name)) { foreach (Transform ch in root.transform) { if (!ch.gameObject.activeInHierarchy || !BoundsOf(ch, out var cb)) continue; if (Mathf.Max(cb.size.x, cb.size.z) <= cfg.unitMaxM) found.Add((ch, root.name, ch)); } Clusters(root.transform, cfg, units, cache); continue; }
                    var list = new List<Transform>(); Collect(root.transform, cfg.unitMaxM, list, 0); foreach (var t in list) found.Add((t, root.name, t));
                }
                else
                {
                    string realm = s.name.StartsWith("Realm_", StringComparison.Ordinal) ? s.name.Substring(6) : s.name;
                    foreach (Transform bundle in root.transform)
                    {
                        if (!bundle.gameObject.activeInHierarchy || !BoundsOf(bundle, out var bb)) continue; string fam = manFamily.TryGetValue(realm + "/" + bundle.name, out string f) ? f : root.name;
                        if (Mathf.Max(bb.size.x, bb.size.z) <= cfg.realmUnitMaxM || bundle.GetComponent<Renderer>() != null) { found.Add((bundle, fam, bundle)); continue; }
                        var list = new List<Transform>(); Collect(bundle, cfg.unitMaxM, list, 0); foreach (var t in list) found.Add((t, fam, t));
                    }
                    if (root.transform.childCount == 0 || found.Count == 0) { var list = new List<Transform>(); Collect(root.transform, cfg.unitMaxM, list, 0); foreach (var t in list) found.Add((t, root.name, t)); }
                }
            }
            foreach (var (t, family, self) in found)
            {
                if (cfg.skipFamilies.Contains(family)) continue; if (!BoundsOf(t, out var b)) continue;
                var u = new Unit { root = t, selfRoot = self, id = IdOf(t), scene = s.name, family = family, b = b }; if (prefix != null && !u.id.StartsWith(prefix, StringComparison.Ordinal)) continue;
                u.rs = Lod0(t); u.kind = KindOf(cfg, family + ":" + u.id, t.name, family, b); u.prot = cfg.protectedParts.Any(p => u.id.IndexOf(p, StringComparison.Ordinal) >= 0);
                if (u.kind == "prop") continue;
                units.Add(u);
            }
            return units;
        }
        static void Far(Scene s, List<Unit> units)
        {
            bool isMain = s.path == MainPath;
            // far proxies (play scene RealmFar308/<Realm>/<name>) - measured while the realm scene is open
            if (!isMain && s.name.StartsWith("Realm_", StringComparison.Ordinal))
            {
                var mainScene = SceneManager.GetSceneByPath(MainPath); var far = mainScene.GetRootGameObjects().FirstOrDefault(g => g.name == "RealmFar308"); var realmT = far != null ? far.transform.Find(s.name.Substring(6)) : null;
                if (realmT != null) foreach (var u in units)
                {
                    if (u.scene != s.name || u.root == null || u.root.parent == null || u.root.parent.parent != null) continue; var host = realmT.Find(u.root.name); if (host == null) continue;
                    bool any = false; var fb = new Bounds(); foreach (var mf in host.GetComponentsInChildren<MeshFilter>(true)) { if (mf.sharedMesh == null) continue; var mb = mf.sharedMesh.bounds; var w = mf.transform.localToWorldMatrix; for (int i = 0; i < 8; i++) { var p = w.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))); if (any) fb.Encapsulate(p); else { fb = new Bounds(p, Vector3.zero); any = true; } } }
                    string nm = u.root.name; if (any && realmT.Cast<Transform>().Count(t => t.name == nm) == 1 && u.root.parent.Cast<Transform>().Count(t => t.name == nm) == 1) u.farOff = Mathf.Max((fb.center - u.b.center).magnitude, (fb.size - u.b.size).magnitude * .5f);
                }
            }
        }
        static void Collect(Transform t, float max, List<Transform> units, int depth)
        {
            if (!t.gameObject.activeInHierarchy || !BoundsOf(t, out var b)) return;
            bool groups = false; foreach (Transform c in t) if (c.gameObject.activeInHierarchy && c.GetComponentInChildren<Renderer>(false) != null) { groups = true; break; }
            if (depth > 0 && (Mathf.Max(b.size.x, b.size.z) <= max || !groups || t.GetComponent<Renderer>() != null || t.GetComponent<LODGroup>() != null)) { units.Add(t); return; }
            foreach (Transform c in t) Collect(c, max, units, depth + 1);
        }
        static List<Renderer> Lod0(Transform t)
        {
            var list = new List<Renderer>();
            foreach (var r in t.GetComponentsInChildren<Renderer>(false))
            {
                if (!Drawn(r) || !(r is MeshRenderer)) continue; var lod = r.GetComponentInParent<LODGroup>();
                if (lod != null) { var lods = lod.GetLODs(); bool inAny = lods.Any(l => l.renderers != null && l.renderers.Contains(r)); if (inAny && !(lods.Length > 0 && lods[0].renderers != null && lods[0].renderers.Contains(r))) continue; }
                list.Add(r);
            }
            return list;
        }
        static string KindOf(Cfg cfg, string id, string name, string family, Bounds b)
        {
            float span = Mathf.Max(b.size.x, b.size.z);
            if (cfg.ruin.Length > 0 && Regex.IsMatch(id, cfg.ruin)) return span < 1.5f ? "prop" : "ruin";
            if (cfg.foundation.Length > 0 && Regex.IsMatch(id, cfg.foundation)) return "foundation";
            if (cfg.structure.Length > 0 && Regex.IsMatch(id, cfg.structure)) return "structure";
            if (cfg.prop.Length > 0 && Regex.IsMatch(name, cfg.prop)) return "prop";
            if (b.size.y < cfg.minH || span < cfg.minSpan) return "prop";
            return span < cfg.smallSpan ? "small" : "building";
        }

        // realm-wide merged meshes (one mesh per part class): buildings are the connected groups of occupied cells
        static void Clusters(Transform root, Cfg cfg, List<Unit> units, BuildingAudit308.MeshCache308 cache)
        {
            var groups = new Dictionary<string, List<Renderer>>();
            foreach (Transform ch in root) { var r = ch.GetComponent<MeshRenderer>(); if (!ch.gameObject.activeInHierarchy || !Drawn(r) || Mathf.Max(r.bounds.size.x, r.bounds.size.z) <= cfg.unitMaxM) continue; string k = ch.name.Split('_')[0]; if (!groups.TryGetValue(k, out var l)) groups[k] = l = new List<Renderer>(); l.Add(r); }
            foreach (var g in groups)
            {
                var occ = new HashSet<long>();
                foreach (var r in g.Value) { var m = cache.Get(r.GetComponent<MeshFilter>().sharedMesh); if (m == null) continue; var w = r.transform.localToWorldMatrix; for (int i = 0; i + 2 < m.t.Length; i += 3) { var p = w.MultiplyPoint3x4((m.v[m.t[i]] + m.v[m.t[i + 1]] + m.v[m.t[i + 2]]) / 3f); occ.Add(Key(Mathf.FloorToInt(p.x / 2f), Mathf.FloorToInt(p.z / 2f))); } }
                var seen = new HashSet<long>(); int n = 0;
                foreach (long start in occ.OrderBy(x => x))
                {
                    if (!seen.Add(start)) continue; var comp = new HashSet<long>(); var q = new Queue<long>(); q.Enqueue(start);
                    while (q.Count > 0) { long k = q.Dequeue(); comp.Add(k); int x = (int)(k >> 32), z = (int)(uint)(k & 0xffffffff); for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) { long nk = Key(x + dx, z + dz); if (occ.Contains(nk) && seen.Add(nk)) q.Enqueue(nk); } }
                    var u = new Unit { root = root, selfRoot = root, scene = root.gameObject.scene.name, family = root.name, cellFilter = comp, rs = g.Value, realm = g.Key };
                    var first = comp.First(); u.id = root.gameObject.scene.name + "/" + root.name + "/" + g.Key + "~" + (n++).ToString(Inv);
                    u.kind = "cluster"; units.Add(u);
                }
                foreach (var r in g.Value) if (r.name.EndsWith("_Base", StringComparison.Ordinal)) units.Add(new Unit { root = r.transform, selfRoot = root, scene = root.gameObject.scene.name, family = root.name, id = root.gameObject.scene.name + "/" + root.name + "/" + r.name + "~support", kind = "foundation", rs = new List<Renderer> { r }, b = r.bounds, supportOnly = true });
            }
        }

        // ------------------------------------------------------------------ measurements
        static void Measure(Cfg cfg, Unit u, BuildingAudit308.MeshCache308 cache, Dictionary<long, List<Span>> support)
        {
            if (u.supportOnly) { u.rend = 0; return; }
            var own = new HashSet<int>(u.rs.Select(r => r.GetInstanceID()));
            var chunks = new List<Chunk>(); bool any = false; var bb = new Bounds(); int bestV = -1;
            foreach (var r in u.rs)
            {
                var mf = r.GetComponent<MeshFilter>(); var m = mf != null ? cache.Get(mf.sharedMesh) : null; if (m == null) { if (mf != null && mf.sharedMesh != null) u.read = false; continue; }
                var w = r.transform.localToWorldMatrix; var v = new Vector3[m.v.Length]; for (int i = 0; i < v.Length; i++) v[i] = w.MultiplyPoint3x4(m.v[i]);
                int[] t = m.t;
                if (u.cellFilter != null) { var keep = new List<int>(); for (int i = 0; i + 2 < t.Length; i += 3) { var p = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f; if (u.cellFilter.Contains(Key(Mathf.FloorToInt(p.x / 2f), Mathf.FloorToInt(p.z / 2f)))) { keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]); } } t = keep.ToArray(); if (t.Length == 0) continue; }
                chunks.Add(new Chunk { v = v, t = t }); if (u.cellFilter == null && m.v.Length > bestV) { bestV = m.v.Length; u.mesh0 = mf.sharedMesh.name; }
                for (int i = 0; i < t.Length; i++) { if (any) bb.Encapsulate(v[t[i]]); else { bb = new Bounds(v[t[i]], Vector3.zero); any = true; } }
                u.verts += u.cellFilter == null ? m.v.Length : t.Length;
            }
            u.rend = u.rs.Count; if (!any) { u.read = false; return; }
            if (u.cellFilter != null) { u.b = bb; u.kind = KindOf(cfg, u.family + ":" + u.id, u.id, u.family, u.b); if (u.kind == "prop") { u.rend = 0; return; } }
            float minY = bb.min.y, H = Mathf.Max(.01f, bb.size.y); u.baseY = minY;
            // cells (lowest / highest point per cell) and vertical coverage (whole + four quadrants)
            float cs = cfg.cell; var cells = new Dictionary<long, Vector2>(); var lowAt = new Dictionary<long, Vector2>(); const float bin = .025f; int nb = Mathf.CeilToInt(H / bin) + 1; var cov = new float[5][]; for (int i = 0; i < 5; i++) cov[i] = new float[nb + 1]; float cx = bb.center.x, cz = bb.center.z;
            Bounds top = default, mid = default; bool hasTop = false, hasMid = false; float y75 = minY + .75f * H, y25 = minY + .25f * H, y55 = minY + .55f * H;
            foreach (var ch in chunks)
            {
                var v = ch.v; var t = ch.t;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 p0 = v[t[i]], p1 = v[t[i + 1]], p2 = v[t[i + 2]]; float lo = Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)), hi = Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y));
                    float area = Vector3.Cross(p1 - p0, p2 - p0).magnitude * .5f; if (area < 1e-7f) continue; var c = (p0 + p1 + p2) / 3f; int q = 1 + (c.x >= cx ? 1 : 0) + (c.z >= cz ? 2 : 0);
                    int b0 = Mathf.Clamp(Mathf.FloorToInt((lo - minY) / bin), 0, nb - 1), b1 = Mathf.Clamp(Mathf.FloorToInt((hi - minY) / bin), 0, nb - 1); float per = area / (b1 - b0 + 1);
                    for (int b = b0; b <= b1; b++) { cov[0][b] += per; cov[q][b] += per; }
                    if (c.y >= y75) { if (hasTop) { top.Encapsulate(p0); top.Encapsulate(p1); top.Encapsulate(p2); } else { top = new Bounds(p0, Vector3.zero); top.Encapsulate(p1); top.Encapsulate(p2); hasTop = true; } }
                    else if (c.y >= y25 && c.y <= y55) { if (hasMid) { mid.Encapsulate(p0); mid.Encapsulate(p1); mid.Encapsulate(p2); } else { mid = new Bounds(p0, Vector3.zero); mid.Encapsulate(p1); mid.Encapsulate(p2); hasMid = true; } }
                    float ext = Mathf.Max(Mathf.Max(Mathf.Abs(p1.x - p0.x), Mathf.Abs(p2.x - p0.x)), Mathf.Max(Mathf.Abs(p1.z - p0.z), Mathf.Abs(p2.z - p0.z))); int n = Mathf.Clamp(Mathf.CeilToInt(ext / (cs * .7f)), 1, 48);
                    for (int ia = 0; ia <= n; ia++) for (int ib = 0; ib <= n - ia; ib++)
                    {
                        float fa = ia / (float)n, fb = ib / (float)n; var p = p0 + (p1 - p0) * fa + (p2 - p0) * fb; long k = Key(Mathf.FloorToInt(p.x / cs), Mathf.FloorToInt(p.z / cs));
                        if (cells.TryGetValue(k, out var e)) { if (p.y < e.x) { e.x = p.y; lowAt[k] = new Vector2(p.x, p.z); } if (p.y > e.y) e.y = p.y; cells[k] = e; } else { cells[k] = new Vector2(p.y, p.y); lowAt[k] = new Vector2(p.x, p.z); }
                    }
                }
            }
            u.cells = u.kind == "building" ? cells : null; u.nCells = cells.Count;
            // vertical gaps: an empty band with drawn area below and above it
            for (int qi = 0; qi < 5; qi++)
            {
                float total = cov[qi].Sum(); if (total <= 0f) continue; float below = 0f; int run = 0; float best = 0f, bestY = 0f, bestAbove = 0f;
                for (int b = 0; b < nb; b++)
                {
                    if (cov[qi][b] > 0f) { if (run >= 2 && below > 0f) { float g = run * bin; float above = total - below; float need = qi == 0 ? .02f : .15f; if (g > best && above / total > need && below / total > need) { best = g; bestY = minY + (b - run) * bin; bestAbove = above / total; } } run = 0; below += cov[qi][b]; }
                    else run++;
                }
                if (qi == 0) { u.vgap = best; u.vgapY = bestY; u.vgapAbove = bestAbove; } else if (best > u.qgap) u.qgap = best;
            }
            if (hasTop && hasMid && H >= 3f) { u.eaveX = (top.size.x - mid.size.x) * .5f; u.eaveZ = (top.size.z - mid.size.z) * .5f; }
            if (hasTop && hasMid && H >= 3f) u.roofOff = Vector2.Distance(new Vector2(top.center.x, top.center.z), new Vector2(mid.center.x, mid.center.z));
            if (u.cellFilter == null) u.tilt = Vector3.Angle(u.root.up, Vector3.up);
            // ground under the base cells
            float tLo = float.MaxValue, tHi = float.MinValue; int nF = 0, nBu = 0, nB = 0;
            foreach (var kv in cells)
            {
                if (kv.Value.x > minY + cfg.baseBandM) continue; var la = lowAt[kv.Key]; var at = new Vector3(la.x, 0f, la.y); float lo = kv.Value.x;
                float terrain = float.NaN; foreach (var hit in Physics.RaycastAll(new Vector3(at.x, bb.max.y + 300f, at.z), Vector3.down, 1200f, ~0, QueryTriggerInteraction.Ignore)) if (IsTerrain(hit.transform) && (float.IsNaN(terrain) || hit.point.y > terrain)) terrain = hit.point.y;
                if (float.IsNaN(terrain)) continue; nB++; tLo = Mathf.Min(tLo, terrain); tHi = Mathf.Max(tHi, terrain); float gap;
                if (terrain >= lo - .02f) gap = 0f;
                else
                {
                    float sup = terrain; foreach (var hit in Physics.RaycastAll(new Vector3(at.x, lo + .3f, at.z), Vector3.down, lo + .3f - terrain + .1f, ~0, QueryTriggerInteraction.Ignore)) { if (hit.transform.IsChildOf(u.selfRoot)) continue; if (hit.point.y > sup) sup = hit.point.y; }
                    if (support.TryGetValue(Key(Mathf.FloorToInt(at.x), Mathf.FloorToInt(at.z)), out var spans)) foreach (var sp in spans) { if (own.Contains(sp.owner)) continue; if (sp.lo <= lo + .05f && sp.hi >= lo - .05f) sup = lo; else if (sp.hi <= lo + .3f && sp.hi > sup) sup = sp.hi; }
                    gap = Mathf.Max(0f, lo - sup);
                }
                if (gap > cfg.floatM) nF++; u.floatMax = Mathf.Max(u.floatMax, gap); float bur = terrain - lo; if (bur > cfg.buriedM) nBu++; u.buriedMax = Mathf.Max(u.buriedMax, bur);
            }
            u.nBase = nB; if (nB > 0) { u.floatFr = nF / (float)nB; u.buriedFr = nBu / (float)nB; u.relief = tHi - tLo; u.terrainHi = tHi; }
            // colliders
            if (u.cellFilter == null)
            {
                var cols = u.root.GetComponentsInChildren<Collider>(false).Where(c => c.enabled && !c.isTrigger).ToArray(); u.cols = cols.Length;
                if (cols.Length > 0) { var cb = cols[0].bounds; foreach (var c in cols) cb.Encapsulate(c.bounds); var rb = u.b; u.colOver = Mathf.Max(0f, Mathf.Max(Mathf.Max(rb.min.x - cb.min.x, cb.max.x - rb.max.x), Mathf.Max(Mathf.Max(rb.min.z - cb.min.z, cb.max.z - rb.max.z), cb.max.y - rb.max.y))); }
                // LOD levels against LOD0
                foreach (var lod in u.root.GetComponentsInChildren<LODGroup>(false))
                {
                    var lods = lod.GetLODs(); if (lods.Length < 2 || lods[0].renderers == null) continue; Bounds b0 = default; bool h0 = false; foreach (var r in lods[0].renderers) if (Drawn(r)) { if (h0) b0.Encapsulate(r.bounds); else { b0 = r.bounds; h0 = true; } } if (!h0) continue;
                    for (int k = 1; k < lods.Length; k++) { Bounds bk = default; bool hk = false; if (lods[k].renderers == null) continue; foreach (var r in lods[k].renderers) if (Drawn(r)) { if (hk) bk.Encapsulate(r.bounds); else { bk = r.bounds; hk = true; } } if (!hk) continue; u.lodOff = Mathf.Max(u.lodOff, (bk.center - b0.center).magnitude); }
                }
                // leftover copies and detached parts
                var big = u.rs.Where(r => Mathf.Max(r.bounds.size.x, Mathf.Max(r.bounds.size.y, r.bounds.size.z)) >= 1f).ToList();
                foreach (var grp in big.GroupBy(r => r.GetComponent<MeshFilter>().sharedMesh)) { var l = grp.ToList(); if (grp.Key == null || l.Count < 2 || l.Count > 400) continue; for (int i = 0; i < l.Count; i++) for (int j = i + 1; j < l.Count; j++) if ((l[i].transform.position - l[j].transform.position).sqrMagnitude < cfg.dupM * cfg.dupM && Quaternion.Angle(l[i].transform.rotation, l[j].transform.rotation) < 5f && (l[i].transform.lossyScale - l[j].transform.lossyScale).magnitude < .05f) { u.dups++; if (u.note.Length < 200) u.note += "dup " + l[i].name + "~" + l[j].name + " "; } }
                if (u.rs.Count >= 6)
                {
                    var xs = u.rs.Select(r => r.bounds.center.x).OrderBy(x => x).ToArray(); var ys = u.rs.Select(r => r.bounds.center.y).OrderBy(x => x).ToArray(); var zs = u.rs.Select(r => r.bounds.center.z).OrderBy(x => x).ToArray(); int a5 = xs.Length / 20, a95 = xs.Length - 1 - xs.Length / 20;
                    var core = new Bounds(); core.SetMinMax(new Vector3(xs[a5], ys[a5], zs[a5]), new Vector3(xs[a95], ys[a95], zs[a95])); core.Expand(new Vector3(Mathf.Max(cfg.outlierM * 2f, core.size.x), Mathf.Max(cfg.outlierM * 2f, core.size.y), Mathf.Max(cfg.outlierM * 2f, core.size.z)));
                    foreach (var r in u.rs) { if (core.Intersects(r.bounds)) continue; float d = Mathf.Sqrt(core.SqrDistance(r.bounds.center)); u.outliers++; if (d > u.outMax) u.outMax = d; if (u.note.Length < 200) u.note += "far part " + r.name + " " + F1(d) + "m "; }
                }
            }
        }

        static void Finish(Scene main, Cfg cfg, List<Unit> units)
        {
            // realm of each unit: realm scene name, a realm id in the path, or the nearest realm scene unit
            var cents = units.Where(u => u.scene.StartsWith("Realm_", StringComparison.Ordinal)).GroupBy(u => u.scene.Substring(6)).ToDictionary(g => g.Key, g => new Vector2(g.Average(u => u.b.center.x), g.Average(u => u.b.center.z)));
            string[] ids = { "Cheongrim", "Jeokro", "Cheolong", "Hyeongang", "Hwanggyeong" }; var fixedCents = new Dictionary<string, Vector2> { { "Cheongrim", new Vector2(3100, 2700) }, { "Jeokro", new Vector2(2150, 1150) }, { "Cheolong", new Vector2(750, 3300) }, { "Hyeongang", new Vector2(2000, 4900) }, { "Hwanggyeong", new Vector2(1900, 3100) } };
            foreach (var u in units)
            {
                if (u.scene.StartsWith("Realm_", StringComparison.Ordinal)) { u.realm = u.scene.Substring(6); continue; }
                string hit = ids.FirstOrDefault(i => u.id.IndexOf(i, StringComparison.OrdinalIgnoreCase) >= 0); if (hit != null) { u.realm = hit; continue; }
                var p = new Vector2(u.b.center.x, u.b.center.z); u.realm = fixedCents.OrderBy(k => (k.Value - p).sqrMagnitude).First().Key;
            }
            // footprint overlap between buildings
            var map = new Dictionary<long, List<int>>(); var b = units.Select((u, i) => (u, i)).Where(x => x.u.kind == "building" && x.u.cells != null).ToList();
            foreach (var (u, i) in b) foreach (var k in u.cells.Keys) { if (!map.TryGetValue(k, out var l)) map[k] = l = new List<int>(2); l.Add(i); }
            var pair = new Dictionary<long, int>();
            foreach (var kv in map) { var l = kv.Value; if (l.Count < 2) continue; for (int i = 0; i < l.Count; i++) for (int j = i + 1; j < l.Count; j++) { var a = units[l[i]].cells[kv.Key]; var c = units[l[j]].cells[kv.Key]; if (Mathf.Min(a.y, c.y) - Mathf.Max(a.x, c.x) < .3f) continue; long pk = ((long)l[i] << 32) | (uint)l[j]; pair[pk] = pair.TryGetValue(pk, out int n) ? n + 1 : 1; } }
            foreach (var kv in pair) { var u1 = units[(int)(kv.Key >> 32)]; var u2 = units[(int)(uint)(kv.Key & 0xffffffff)]; if (kv.Value < Mathf.Max(cfg.overlapCells, .03f * Mathf.Min(u1.nCells, u2.nCells))) continue; if (kv.Value > u1.overlapCells) { u1.overlapCells = kv.Value; u1.overlapWith = u2.id; } if (kv.Value > u2.overlapCells) { u2.overlapCells = kv.Value; u2.overlapWith = u1.id; } }
            units.Sort((x, y) => string.CompareOrdinal(x.id, y.id)); for (int i = 0; i < units.Count; i++) { units[i].seq = i + 1; units[i].cells = null; }
        }

        // ------------------------------------------------------------------ findings
        static List<(string type, float mag)> Findings(Cfg cfg, Unit u)
        {
            var f = new List<(string, float)>(); if (!u.read && u.verts == 0) { f.Add(("UNREAD", 0f)); return f; }
            string tag = u.family + ":" + u.id; bool body = u.kind == "building" || u.kind == "small", ground = u.kind != "ruin" && !(cfg.noGround.Length > 0 && Regex.IsMatch(tag, cfg.noGround));
            if (ground && u.nBase > 0 && u.floatMax > cfg.floatM && u.floatFr >= cfg.floatFrac) f.Add(("FLOAT", u.floatMax));
            float skirt = SkirtOf(cfg, tag); if (ground && body && u.nBase > 0 && u.buriedMax > cfg.buriedM + skirt && u.buriedFr >= cfg.buriedFrac) f.Add(("BURIED", u.buriedMax - skirt));
            if (u.kind != "ruin" && u.vgap > cfg.vgapM) f.Add(("VGAP", u.vgap));
            else if (u.kind != "ruin" && body && u.qgap > Mathf.Max(cfg.vgapM, .15f)) f.Add(("QGAP", u.qgap));
            if (u.kind == "building" && u.roofOff > Mathf.Max(cfg.roofOffM, cfg.roofOffSpan * Mathf.Max(u.b.size.x, u.b.size.z))) f.Add(("ROOFOFF", u.roofOff));
            if (body && u.tilt > cfg.tiltDeg) f.Add(("TILT", u.tilt));
            if (u.lodOff > Mathf.Max(cfg.lodOffM, .05f * Mathf.Max(u.b.size.x, u.b.size.z))) f.Add(("LOD", u.lodOff));
            if (u.farOff > cfg.farOffM) f.Add(("FAR", u.farOff));
            if (u.colOver > cfg.colM) f.Add(("COLOVER", u.colOver));
            if (u.kind == "building" && u.cols == 0 && u.cellFilter == null) f.Add(("NOCOL", 0f));
            if (u.dups > 0) f.Add(("DUP", u.dups));
            if (u.outliers > 0 && u.kind != "ruin") f.Add(("OUTLIER", u.outMax));
            if (u.overlapCells > 0) f.Add(("OVERLAP", u.overlapCells));
            return f;
        }

        /// <summary>"open", or "accepted: reason" when a row TYPE|regex|reason of `accept` matches family:id.</summary>
        static string AcceptOf(Cfg cfg, string type, Unit u)
        {
            string tag = u.family + ":" + u.id;
            foreach (string row in cfg.accept) { int i0 = row.IndexOf('|'), i1 = row.LastIndexOf('|'); if (i0 < 0 || i1 <= i0) continue; string ty = row.Substring(0, i0); if ((ty == type || ty == "*") && Regex.IsMatch(tag, row.Substring(i0 + 1, i1 - i0 - 1))) return "accepted: " + row.Substring(i1 + 1); }
            return "open";
        }
        static float SkirtOf(Cfg cfg, string id) { foreach (string row in cfg.skirts) { int bar = row.LastIndexOf('|'); if (Regex.IsMatch(id, row.Substring(0, bar))) return float.Parse(row.Substring(bar + 1), Inv); } return 0f; }
        static string Csv(string s) => s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        static string WriteOut(Cfg cfg, List<Unit> units, string suffix)
        {
            var us = new StringBuilder("seq,id,scene,realm,family,kind,protected,willReplace,x,z,y,spanX,spanZ,H,rend,verts,mesh0,nBase,floatMax,floatFr,buriedMax,buriedFr,relief,vgap,vgapY,vgapAbove,qgap,roofOff,tilt,lodOff,farOff,eaveX,eaveZ,cols,colOver,dups,outliers,outMax,overlapCells,overlapWith,note\n");
            var fs = new StringBuilder("seq,id,scene,realm,family,kind,protected,willReplace,x,z,defect,magnitude,status\n"); var open = new SortedDictionary<string, int>(); var count = new SortedDictionary<string, int>(); var inv = new SortedDictionary<string, int[]>(); string[] realms = { "Cheongrim", "Jeokro", "Cheolong", "Hyeongang", "Hwanggyeong" };
            foreach (var u in units)
            {
                bool will = (cfg.willReplace.Length > 0 && Regex.IsMatch(u.family + ":" + u.id, cfg.willReplace)) || InPoly(cfg.willReplacePoly, u.b.center.x, u.b.center.z);
                us.Append(u.seq).Append(',').Append(Csv(u.id)).Append(',').Append(u.scene).Append(',').Append(u.realm).Append(',').Append(u.family).Append(',').Append(u.kind).Append(',').Append(u.prot ? 1 : 0).Append(',').Append(will ? 1 : 0).Append(',').Append(F1(u.b.center.x)).Append(',').Append(F1(u.b.center.z)).Append(',').Append(F1(u.b.min.y)).Append(',').Append(F1(u.b.size.x)).Append(',').Append(F1(u.b.size.z)).Append(',').Append(F1(u.b.size.y)).Append(',').Append(u.rend).Append(',').Append(u.verts).Append(',').Append(Csv(u.mesh0)).Append(',').Append(u.nBase).Append(',')
                  .Append(F(u.floatMax)).Append(',').Append(F(u.floatFr)).Append(',').Append(F(u.buriedMax)).Append(',').Append(F(u.buriedFr)).Append(',').Append(F(u.relief)).Append(',').Append(F(u.vgap)).Append(',').Append(F1(u.vgapY)).Append(',').Append(F(u.vgapAbove)).Append(',').Append(F(u.qgap)).Append(',').Append(F(u.roofOff)).Append(',').Append(F1(u.tilt)).Append(',').Append(F(u.lodOff)).Append(',').Append(F(u.farOff)).Append(',').Append(F(u.eaveX)).Append(',').Append(F(u.eaveZ)).Append(',').Append(u.cols).Append(',').Append(F(u.colOver)).Append(',').Append(u.dups).Append(',').Append(u.outliers).Append(',').Append(F1(u.outMax)).Append(',').Append(u.overlapCells).Append(',').Append(Csv(u.overlapWith)).Append(',').Append(Csv(u.note.Trim())).Append('\n');
                if (u.kind == "building" || u.kind == "small" || u.kind == "ruin") { string k = u.family + (u.kind == "ruin" ? " (ruin)" : u.kind == "small" ? " (small)" : ""); if (!inv.TryGetValue(k, out var row)) inv[k] = row = new int[5]; int ri = Array.IndexOf(realms, u.realm); if (ri >= 0) row[ri]++; }
                foreach (var (type, mag) in Findings(cfg, u)) { fs.Append(u.seq).Append(',').Append(Csv(u.id)).Append(',').Append(u.scene).Append(',').Append(u.realm).Append(',').Append(u.family).Append(',').Append(u.kind).Append(',').Append(u.prot ? 1 : 0).Append(',').Append(will ? 1 : 0).Append(',').Append(F1(u.b.center.x)).Append(',').Append(F1(u.b.center.z)).Append(',').Append(type).Append(',').Append(F(mag)); string st = will ? "willReplace" : u.prot ? "protected" : AcceptOf(cfg, type, u); fs.Append(',').Append(Csv(st)).Append('\n'); count[type] = count.TryGetValue(type, out int n) ? n + 1 : 1; if (st == "open") open[type] = open.TryGetValue(type, out int n2) ? n2 + 1 : 1; }
            }
            var sum = new StringBuilder("Assembly308 scan" + suffix + ": units " + units.Count + " (building " + units.Count(u => u.kind == "building") + ", small " + units.Count(u => u.kind == "small") + ", structure " + units.Count(u => u.kind == "structure") + ", foundation " + units.Count(u => u.kind == "foundation") + ", ruin " + units.Count(u => u.kind == "ruin") + ")\n");
            sum.Append("inventory (buildings; " + string.Join(" ", realms) + ")\n"); foreach (var kv in inv) sum.Append("  ").Append(kv.Key.PadRight(40)).Append(string.Join(" ", kv.Value.Select(v => v.ToString(Inv).PadLeft(4)))).Append('\n');
            sum.Append("defects (raw): ").Append(string.Join("  ", count.Select(kv => kv.Key + " " + kv.Value))).Append('\n'); sum.Append("defects (open = not willReplace, not protected, not accepted): ").Append(open.Count == 0 ? "none" : string.Join("  ", open.Select(kv => kv.Key + " " + kv.Value))).Append('\n');
            File.WriteAllText(Path.Combine(OutDir, "units" + suffix + ".csv"), us.ToString()); File.WriteAllText(Path.Combine(OutDir, "findings" + suffix + ".csv"), fs.ToString()); File.WriteAllText(Path.Combine(OutDir, "summary" + suffix + ".txt"), sum.ToString());
            return sum.ToString();
        }

        static string CheckReport(Cfg cfg, List<Unit> units, string arg)
        {
            var sb = new StringBuilder("Assembly308 check " + arg + ": units " + units.Count + "\n"); if (units.Count == 0) return sb + "FAIL no unit matched (is the scene open?)\nchecks, 0 pass 1 fail";
            string[] types = { "FLOAT", "BURIED", "VGAP", "QGAP", "ROOFOFF", "TILT", "LOD", "COLOVER", "NOCOL", "DUP", "OUTLIER", "OVERLAP", "UNREAD" }; var by = types.ToDictionary(t => t, t => new List<string>());
            int accepted = 0; foreach (var u in units) foreach (var (type, mag) in Findings(cfg, u)) if (u.prot || InPoly(cfg.willReplacePoly, u.b.center.x, u.b.center.z) || AcceptOf(cfg, type, u) != "open") accepted++; else if (by.ContainsKey(type)) by[type].Add(u.id.Substring(u.id.IndexOf('/') + 1) + " " + F(mag));
            int pass = 0, fail = 0; foreach (string t in types) { var l = by[t]; bool soft = t == "NOCOL"; if (l.Count == 0) { pass++; sb.Append("PASS ").Append(t).Append(" 0\n"); } else if (soft) { pass++; sb.Append("WARN ").Append(t).Append(' ').Append(l.Count).Append(" (look at them; not a failure): ").Append(string.Join("; ", l.Take(4))).Append(l.Count > 4 ? "; ..." : "").Append('\n'); } else { fail++; sb.Append("FAIL ").Append(t).Append(' ').Append(l.Count).Append(": ").Append(string.Join("; ", l.Take(6))).Append(l.Count > 6 ? "; ..." : "").Append('\n'); } }
            sb.Append("accepted (assembly308.json accept rows, protected, willReplace): ").Append(accepted).Append('\n'); return sb.Append("checks, ").Append(pass).Append(" pass ").Append(fail).Append(" fail").ToString();
        }

        // shot lines for Presentation297 from the last scan's units.csv
        static string Shots(string tag, string seqs)
        {
            var want = new HashSet<string>(seqs.Split(',')); var sb = new StringBuilder(); int n = 0;
            foreach (string line in File.ReadAllLines(Path.Combine(OutDir, "units.csv")).Skip(1))
            {
                var c = SplitCsv(line); if (!want.Contains(c[0])) continue; float x = float.Parse(c[8], Inv), z = float.Parse(c[9], Inv), y = float.Parse(c[10], Inv), sx = float.Parse(c[11], Inv), sz = float.Parse(c[12], Inv), h = float.Parse(c[13], Inv);
                float span = Mathf.Max(sx, sz), dist = span * .75f + h * .6f + 5f; var target = new Vector3(x, y + h * .45f, z); Vector3 best = Vector3.zero; float bestScore = float.MinValue;
                for (int i = 0; i < 12; i++)
                {
                    float ang = i * Mathf.PI / 6f; var eye = target + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * dist; float ground = float.NaN;
                    foreach (var hit in Physics.RaycastAll(eye + Vector3.up * 400f, Vector3.down, 1200f, ~0, QueryTriggerInteraction.Ignore)) if (IsTerrain(hit.transform) && (float.IsNaN(ground) || hit.point.y > ground)) ground = hit.point.y;
                    if (float.IsNaN(ground)) continue; eye.y = Mathf.Max(ground + 1.7f, y + h * .35f + 1f); float score = -Mathf.Abs(eye.y - (y + h * .5f)) - (Physics.Linecast(eye, target, out var block) && IsTerrain(block.transform) ? 50f : 0f);
                    if (score > bestScore) { bestScore = score; best = eye; }
                }
                if (bestScore == float.MinValue) best = target + new Vector3(dist, h, dist);
                sb.Append(c[2]).Append('|').Append("shot:asm_").Append(tag).Append('_').Append(c[0]).Append(':').Append(F1(best.x)).Append(',').Append(F1(best.y)).Append(',').Append(F1(best.z)).Append(':').Append(F1(target.x)).Append(',').Append(F1(target.y)).Append(',').Append(F1(target.z)).Append(":fov=55:w=1280:h=720:hideplayer\n"); n++;
            }
            File.WriteAllText(Path.Combine(OutDir, "shots_" + tag + ".txt"), sb.ToString()); return "Assembly308 shots: " + n + " lines -> Out/shots_" + tag + ".txt";
        }
        internal static string[] SplitCsv(string line)
        {
            var r = new List<string>(); var cur = new StringBuilder(); bool q = false;
            for (int i = 0; i < line.Length; i++) { char ch = line[i]; if (q) { if (ch == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else q = false; } else cur.Append(ch); } else if (ch == '"') q = true; else if (ch == ',') { r.Add(cur.ToString()); cur.Clear(); } else cur.Append(ch); }
            r.Add(cur.ToString()); return r.ToArray();
        }
    }
}
