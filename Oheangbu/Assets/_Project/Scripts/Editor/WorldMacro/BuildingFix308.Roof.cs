using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-1 roof track (DEFECTS_308 "지붕 파손/투시" 17 stills). Own queue entry so BuildingFix308.cs stays untouched:
    //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.BuildingFix308 Roof "<command>"
    //   check:<alias>                       read only: every entry of the compound layouts (Finish297/<compound>/unity.json) against the
    //                                       scene — root pose and LOD0/LOD1/LOD2/Collision local pose -> Fix/roof-trs-<alias>-<utc>.json
    //   probe:<alias>                       read only, every kit building and LOD: roof tile triangles without an underside twin and the
    //                                       share of vertical columns under the tile that reach the sky (the check the audit lacked)
    //                                       -> Fix/roof-probe-<alias>-<utc>.json
    //   plan:<alias>[:listed|family]        read only -> Fix/roof-plan-<alias>.json
    //   apply:<alias>[:listed|family]       R1 child poses back to the authored identity, R2 roof underside; backs the scene file up to
    //                                       SceneBackup/<alias>-<utc>-roof.unity, saves only that scene, Fix/roof-ledger-<alias>.json
    //   apply-all[:listed|family]           plans the three scenes first (any blocker = nothing changes), then applies in the order
    //                                       architecture296 -> folklore298 -> main
    //   verify:<alias>[:listed|family]      read only post-conditions -> Fix/roof-verify-<alias>-<utc>.json
    //   revert:<alias>[:R1|R2]              back to the ledger "before" values (exact scene backup when nothing else changed since)
    // Finding (2026-10-03, offline, all three scenes): no roof child ever moved. A kit building is one mesh per LOD and every
    // entry still equals the authored layout; the stills show hanok297.build_roof's top-face-only tile slopes from below (no
    // underside past the gable wall of a 맞배 roof, none inside a building without a ceiling). So R1 is expected to plan 0 changes
    // and R2 carries the repair: a private copy of the LOD mesh (Finish297/BuildingFix308/RoofUnder/…) = the kit mesh + the tile
    // triangles reversed (normals flipped) in the existing soffit slot; LOD2 has no soffit slot, so the material list grows by
    // one there. Not Cull Off: the kit materials are URP/Lit shared by every kit building, no two-sided variant exists, and URP
    // Lit does not flip the normal on back faces (the underside would be lit like the sunny top and show roof tiles). Not a
    // second renderer either: C4 flags coplanar faces of different renderers and the LODGroup would need new entries.
    // Shared kit meshes and materials are never edited, other buildings keep their meshes. No emission, no shader or runtime
    // change. Values come from BuildingAudit308/roof308.json. AssetDatabase.SaveAssets is never called.
    public static partial class BuildingFix308
    {
        static string RoofConfigFile => Path.Combine(BuildingAudit308.Folder, "roof308.json");
        static string RoofLedgerFile(string alias) => Path.Combine(FixDir, "roof-ledger-" + alias + ".json");
        static readonly string[] RoofSteps = { "R1", "R2" };
        static readonly string[] RoofOrder = { "architecture296", "folklore298", "main" };

        [Serializable] sealed class RoofCompound308 { public string root = "", name = "", meshFolder = "", unityJson = ""; }
        [Serializable] sealed class RoofCfg308
        {
            public string version = "", status = "", finding = "";
            public RoofCompound308[] compounds = Array.Empty<RoofCompound308>();
            public string[] listed = Array.Empty<string>(), familyKinds = Array.Empty<string>(), childNames = Array.Empty<string>(), lodNames = Array.Empty<string>();
            public string tileMaterial = "", undersideMaterial = "", derivedFolder = "", derivedSuffix = "";
            public float uScale = 1f, vScale = 1f, twinTolerance, columnStep, downNormalY, minOpenShare, childPoseTolerance, childAngleTolerance, rootPoseTolerance, rootYawTolerance;
        }
        // the part of Finish297/<compound>/unity.json this track reads (CompactRebuildAuthoring.KitCompound297 is private)
        [Serializable] sealed class RoofEntry308 { public string name = "", kind = ""; public bool world; public float[] position = Array.Empty<float>(); public float yaw; }
        [Serializable] sealed class RoofLayout308 { public string compound = "", root = ""; public RoofEntry308[] meshes = Array.Empty<RoofEntry308>(); }
        sealed class RoofTarget308 { public string key = "", name = ""; public RoofCompound308 compound; public RoofEntry308 entry; }
        // twinless = tile triangles with no reversed twin (same corners, opposite face): each one is see-through from behind.
        // cols/open = vertical columns under the tile / those with no downward face at all (sky straight up from the floor).
        sealed class RoofInfo308 { public int cols, open, tileTris, tileVerts, twinless; public long indices; public float Share => cols > 0 ? (float)open / cols : 0f; }

        static RoofCfg308 RoofLoad(out string error)
        {
            error = null;
            if (!File.Exists(RoofConfigFile)) { error = "roof config missing: " + RoofConfigFile; return null; }
            try
            {
                var c = JsonUtility.FromJson<RoofCfg308>(File.ReadAllText(RoofConfigFile).TrimStart('﻿'));
                if (c == null || c.compounds.Length == 0 || c.lodNames.Length == 0 || c.columnStep <= 0f || c.twinTolerance <= 0f || string.IsNullOrEmpty(c.tileMaterial) || string.IsNullOrEmpty(c.undersideMaterial) || string.IsNullOrEmpty(c.derivedFolder) || string.IsNullOrEmpty(c.derivedSuffix))
                { error = "roof config incomplete (compounds/lodNames/columnStep/twinTolerance/tileMaterial/undersideMaterial/derivedFolder/derivedSuffix): " + RoofConfigFile; return null; }
                return c;
            }
            catch (Exception e) { error = "roof config unreadable: " + e.Message; return null; }
        }

        // scope: listed (roof308.json "listed") | family (every entry whose kind is in familyKinds) | all (every entry — check only)
        static List<RoofTarget308> RoofTargets(RoofCfg308 rc, string scope, out string error)
        {
            error = null; var list = new List<RoofTarget308>();
            foreach (var comp in rc.compounds)
            {
                string file = Path.Combine(BuildingAudit308.RepoRoot, comp.unityJson);
                if (!File.Exists(file)) { error = "authored layout missing: " + file; return null; }
                var layout = JsonUtility.FromJson<RoofLayout308>(File.ReadAllText(file).TrimStart('﻿'));
                if (layout == null || layout.meshes.Length == 0) { error = "authored layout empty: " + file; return null; }
                foreach (var e in layout.meshes)
                {
                    string key = comp.root + "/" + e.name;
                    bool take = scope == "all" || (scope == "family" ? rc.familyKinds.Contains(e.kind) : rc.listed.Contains(key));
                    if (take) list.Add(new RoofTarget308 { key = key, name = e.name, compound = comp, entry = e });
                }
            }
            if (scope == "listed")
            {
                var missing = rc.listed.Where(k => list.All(t => t.key != k)).ToArray();
                if (missing.Length > 0) { error = "listed target(s) not in the authored layouts: " + string.Join(", ", missing); return null; }
            }
            return list;
        }

        static string RoofScope(string[] a, int index, out string error)
        {
            error = null; string s = a.Length > index && a[index].Length > 0 ? a[index] : "listed";
            if (s != "listed" && s != "family") error = "refused: scope is listed | family (got " + s + ")";
            return s;
        }

        static Ledger308 RoofLoadLedger(string alias)
        {
            string f = RoofLedgerFile(alias);
            return File.Exists(f) ? JsonUtility.FromJson<Ledger308>(File.ReadAllText(f)) ?? new Ledger308 { alias = alias } : new Ledger308 { alias = alias };
        }
        static void RoofSaveLedger(Ledger308 l) { Directory.CreateDirectory(FixDir); File.WriteAllText(RoofLedgerFile(l.alias), JsonUtility.ToJson(l, true), new UTF8Encoding(false)); }

        static string RoofVec(Vector3 v) => string.Format(Inv, "{0:R},{1:R},{2:R}", v.x, v.y, v.z);
        static Vector3 RoofParseVec(string s) { var p = s.Split(',').Select(x => float.Parse(x, Inv)).ToArray(); if (p.Length != 3) throw new FormatException(s); return new Vector3(p[0], p[1], p[2]); }
        static string RoofShare(RoofInfo308 i) => i.open + "/" + i.cols + " (" + i.Share.ToString("F2", Inv) + ")";
        static string RoofFacts(RoofInfo308 i) => "tile triangles without an underside " + i.twinless + "/" + i.tileTris + ", sky straight up " + RoofShare(i);
        static Mesh RoofMeshAt(string state)
        {
            if (string.IsNullOrEmpty(state)) return null;
            string path = state.Split('#')[0];
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(m => state.EndsWith("#" + m.name, StringComparison.Ordinal)) ?? AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }
        static string[] RoofMaterialPaths(Renderer r) => r.sharedMaterials.Select(m => m == null ? "" : AssetDatabase.GetAssetPath(m)).ToArray();

        // ------------------------------------------------------------------ entry

        public static string Roof(string command)
        {
            command = (command ?? "").Trim();
            const string usage = "refused: BuildingFix308.Roof check:<alias> | probe:<alias> | plan:<alias>[:listed|family] | apply:<alias>[:listed|family] | apply-all[:listed|family] | verify:<alias>[:listed|family] | revert:<alias>[:R1|R2]";
            try
            {
                var a = command.Split(':'); string scope, err;
                switch (a[0])
                {
                    case "check": return a.Length > 1 ? RoofCheck(a[1]) : usage;
                    case "probe": return a.Length > 1 ? RoofProbe(a[1]) : usage;
                    case "plan": if (a.Length < 2) return usage; scope = RoofScope(a, 2, out err); return err ?? RoofPlanCommand(a[1], scope);
                    case "apply": if (a.Length < 2) return usage; scope = RoofScope(a, 2, out err); return err ?? RoofApply(a[1], scope, command);
                    case "apply-all": scope = RoofScope(a, 1, out err); return err ?? RoofApplyAll(scope);
                    case "verify": if (a.Length < 2) return usage; scope = RoofScope(a, 2, out err); return err ?? RoofVerify(a[1], scope);
                    case "revert": return a.Length > 1 ? RoofRevert(a[1], a.Length > 2 && a[2].Length > 0 ? a[2].ToUpperInvariant() : null) : usage;
                    default: return usage;
                }
            }
            catch (Exception e) { return "FAILED: " + e; }
        }

        // ------------------------------------------------------------------ mesh analysis / derivation

        static void RoofMark(bool[] grid, int nx, int nz, float gx0, float gz0, float step, Vector3 a, Vector3 b, Vector3 c)
        {
            double den = (double)(b.z - c.z) * (a.x - c.x) + (double)(c.x - b.x) * (a.z - c.z);
            if (Math.Abs(den) <= 1e-12) return;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - gx0) / step)), i1 = Mathf.Min(nx - 1, Mathf.CeilToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - gx0) / step));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - gz0) / step)), j1 = Mathf.Min(nz - 1, Mathf.CeilToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - gz0) / step));
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    double px = gx0 + i * step, pz = gz0 + j * step;
                    double w1 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / den, w2 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / den;
                    if (w1 >= -1e-6 && w2 >= -1e-6 && 1.0 - w1 - w2 >= -1e-6) grid[i * nz + j] = true;
                }
        }

        // triangle key on the twinTolerance lattice: its three corner keys sorted (winding-independent)
        static (long, long, long) RoofTriKey(Vector3 a, Vector3 b, Vector3 c, float q)
        {
            long K(Vector3 p) => (Mathf.RoundToInt(p.x / q) * 73856093L) ^ (Mathf.RoundToInt(p.y / q) * 19349663L) ^ (Mathf.RoundToInt(p.z / q) * 83492791L);
            long x = K(a), y = K(b), z = K(c), t;
            if (x > y) { t = x; x = y; y = t; }
            if (y > z) { t = y; y = z; z = t; }
            if (x > y) { t = x; x = y; y = t; }
            return (x, y, z);
        }

        // twinless: tile triangles with no reversed twin anywhere in the mesh (see-through from behind at any angle).
        // cols/open: columns (columnStep grid over the tile slot's XZ extent, mesh space) with roof tile above / of those, the ones
        // with no downward-facing triangle anywhere in the column (sky straight up through the back-face-culled tile).
        static RoofInfo308 RoofAnalyse(Mesh m, int tileSub, RoofCfg308 rc)
        {
            var info = new RoofInfo308();
            for (int s = 0; s < m.subMeshCount; s++) info.indices += (long)m.GetIndexCount(s);
            if (tileSub < 0 || tileSub >= m.subMeshCount) return info;
            var v = m.vertices; int[] tile = m.GetTriangles(tileSub);
            info.tileTris = tile.Length / 3; info.tileVerts = tile.Distinct().Count();
            if (tile.Length == 0) return info;
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (int i in tile) { var p = v[i]; x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z); }
            float step = rc.columnStep, gx0 = x0 + step * .5f, gz0 = z0 + step * .5f;
            int nx = Mathf.Max(0, Mathf.CeilToInt((x1 - gx0) / step)), nz = Mathf.Max(0, Mathf.CeilToInt((z1 - gz0) / step));
            var under = new bool[nx * nz]; var blocked = new bool[nx * nz];
            var faces = new Dictionary<(long, long, long), List<Vector3>>();
            for (int k = 0; k + 2 < tile.Length; k += 3) RoofMark(under, nx, nz, gx0, gz0, step, v[tile[k]], v[tile[k + 1]], v[tile[k + 2]]);
            for (int s = 0; s < m.subMeshCount; s++)
            {
                if (m.GetTopology(s) != MeshTopology.Triangles) continue;
                int[] t = m.GetTriangles(s);
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    Vector3 a = v[t[k]], b = v[t[k + 1]], c = v[t[k + 2]]; var n = Vector3.Cross(b - a, c - a); float len = n.magnitude;
                    if (len <= 1e-12f) continue;
                    var key = RoofTriKey(a, b, c, rc.twinTolerance);
                    if (!faces.TryGetValue(key, out var l)) faces[key] = l = new List<Vector3>(1);
                    l.Add(n);
                    if (n.y / len < rc.downNormalY) RoofMark(blocked, nx, nz, gx0, gz0, step, a, b, c);
                }
            }
            for (int k = 0; k + 2 < tile.Length; k += 3)
            {
                Vector3 a = v[tile[k]], b = v[tile[k + 1]], c = v[tile[k + 2]]; var n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude <= 1e-24f) continue;
                if (!faces.TryGetValue(RoofTriKey(a, b, c, rc.twinTolerance), out var l) || !l.Any(o => Vector3.Dot(o, n) < 0f)) info.twinless++;
            }
            for (int i = 0; i < under.Length; i++) if (under[i]) { info.cols++; if (!blocked[i]) info.open++; }
            return info;
        }

        // dst = src + the tile triangles reversed (same corners, normal and tangent sign flipped) written into submesh underSub
        // (== src.subMeshCount: a new last submesh). Coincident on purpose: of a face and its twin exactly one is front-facing from
        // any eye, so they cannot z-fight, and the eave line stays watertight against the fascia. UV = tile UV scaled so the
        // soffit rafters run up the slope.
        static void RoofBuildDerived(Mesh src, Mesh dst, int tileSub, int underSub, RoofCfg308 rc)
        {
            var v = src.vertices; var n = src.normals; var t = src.tangents; var uv = src.uv; var col = src.colors; int vc = v.Length;
            if (n.Length != vc) throw new InvalidOperationException("source mesh has no normals: " + src.name);
            int[] tile = src.GetTriangles(tileSub);
            var map = new Dictionary<int, int>(); var add = new List<int>();
            foreach (int i in tile) if (!map.ContainsKey(i)) { map[i] = vc + add.Count; add.Add(i); }
            int total = vc + add.Count;
            var V = new Vector3[total]; var N = new Vector3[total]; Array.Copy(v, V, vc); Array.Copy(n, N, vc);
            Vector4[] T = t.Length == vc ? new Vector4[total] : null; if (T != null) Array.Copy(t, T, vc);
            Vector2[] U = uv.Length == vc ? new Vector2[total] : null; if (U != null) Array.Copy(uv, U, vc);
            Color[] C = col.Length == vc ? new Color[total] : null; if (C != null) Array.Copy(col, C, vc);
            for (int k = 0; k < add.Count; k++)
            {
                int i = add[k], o = vc + k;
                V[o] = v[i]; N[o] = -n[i];
                if (T != null) T[o] = new Vector4(t[i].x, t[i].y, t[i].z, -t[i].w);
                if (U != null) U[o] = new Vector2(uv[i].x * rc.uScale, uv[i].y * rc.vScale);
                if (C != null) C[o] = col[i];
            }
            int subs = Mathf.Max(src.subMeshCount, underSub + 1);
            var tris = new List<int>[subs];
            for (int s = 0; s < subs; s++) tris[s] = s < src.subMeshCount ? new List<int>(src.GetTriangles(s)) : new List<int>();
            for (int k = 0; k + 2 < tile.Length; k += 3) { tris[underSub].Add(map[tile[k]]); tris[underSub].Add(map[tile[k + 2]]); tris[underSub].Add(map[tile[k + 1]]); }
            var bounds = src.bounds;
            dst.Clear();
            dst.indexFormat = total > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            dst.vertices = V; dst.normals = N; if (T != null) dst.tangents = T; if (U != null) dst.uv = U; if (C != null) dst.colors = C;
            dst.subMeshCount = subs;
            for (int s = 0; s < subs; s++) dst.SetTriangles(tris[s], s, true);   // per-submesh bounds too (the twin slot grows past the eave soffit)
            dst.bounds = bounds;
        }

        // the derived copy of this kit mesh: kit + one twin vertex per tile vertex, one twin triangle per tile triangle, no twinless tile
        // and the twins sit in the slot and carry the UV scale the config asks for NOW (a changed undersideMaterial / uScale / vScale
        // makes the asset stale: it is rebuilt in place instead of being reported "already")
        static bool RoofDerivedValid(Mesh d, Mesh src, RoofInfo308 info, bool append, int tileSub, int underSub, RoofCfg308 rc)
        {
            if (d == null || !d.isReadable || d.vertexCount != src.vertexCount + info.tileVerts || d.subMeshCount != src.subMeshCount + (append ? 1 : 0)) return false;
            long idx = 0; for (int s = 0; s < d.subMeshCount; s++) idx += (long)d.GetIndexCount(s);
            if (idx != info.indices + info.tileTris * 3L) return false;
            if (underSub < 0 || underSub >= d.subMeshCount || (long)d.GetIndexCount(underSub) != (append ? 0L : (long)src.GetIndexCount(underSub)) + info.tileTris * 3L) return false;
            var su = src.uv;
            if (su.Length == src.vertexCount)
            {
                var du = d.uv; if (du.Length != d.vertexCount) return false;
                var seen = new HashSet<int>(); int k = src.vertexCount;
                foreach (int i in src.GetTriangles(tileSub))
                    if (seen.Add(i)) { if ((du[k] - new Vector2(su[i].x * rc.uScale, su[i].y * rc.vScale)).sqrMagnitude > 1e-6f) return false; k++; }
            }
            return RoofAnalyse(d, tileSub, rc).twinless == 0;
        }

        // root pose against the authored layout entry; null when equal within the root tolerances
        static string RoofRootDeviation(Transform b, RoofEntry308 e, RoofCfg308 rc)
        {
            Vector3 want = !e.world && e.position != null && e.position.Length >= 3 ? new Vector3(e.position[0], e.position[1], e.position[2]) : Vector3.zero;
            var wantRot = !e.world && e.position != null && e.position.Length >= 3 ? Quaternion.Euler(0f, e.yaw, 0f) : Quaternion.identity;
            float dp = Vector3.Distance(b.position, want), da = Quaternion.Angle(b.rotation, wantRot), ds = (b.lossyScale - Vector3.one).magnitude;
            if (dp <= rc.rootPoseTolerance && da <= rc.rootYawTolerance && ds <= rc.rootPoseTolerance) return null;
            return "position off " + dp.ToString("F3", Inv) + " m, rotation off " + da.ToString("F2", Inv) + " deg, scale off " + ds.ToString("F3", Inv) + " (authored " + BuildingAudit308.V(want) + " yaw " + e.yaw.ToString("F2", Inv) + ", scene " + BuildingAudit308.V(b.position) + ")";
        }

        static bool RoofChildOff(Transform c, RoofCfg308 rc, out bool pose, out bool scale)
        {
            pose = c.localPosition.magnitude > rc.childPoseTolerance || Quaternion.Angle(c.localRotation, Quaternion.identity) > rc.childAngleTolerance;
            scale = (c.localScale - Vector3.one).magnitude > rc.childPoseTolerance;
            return pose || scale;
        }

        // ------------------------------------------------------------------ plan

        static Plan308 RoofMakePlan(BuildingAudit308.Config308 cfg, RoofCfg308 rc, string alias, Scene scene, string scope, List<RoofTarget308> targets)
        {
            var plan = new Plan308 { alias = alias, scene = scene.path, utc = BuildingAudit308.Utc(), sceneSha = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path)), configVersion = rc.version + " scope " + scope, quality = BuildingAudit308.QualityName() };
            var r1 = new StepPlan308 { step = "R1", target = scope }; var r2 = new StepPlan308 { step = "R2", target = scope };
            void Block(StepPlan308 sp, string why) { sp.status = "blocked"; sp.detail = string.IsNullOrEmpty(sp.detail) ? why : sp.detail + " | " + why; }
            var tileMat = AssetDatabase.LoadAssetAtPath<Material>(rc.tileMaterial); var underMat = AssetDatabase.LoadAssetAtPath<Material>(rc.undersideMaterial);
            if (tileMat == null) Block(r2, "tile material missing: " + rc.tileMaterial);
            if (underMat == null) Block(r2, "underside material missing: " + rc.undersideMaterial);
            if (underMat != null && (underMat.IsKeywordEnabled("_EMISSION") || (underMat.HasProperty("_EmissionColor") && underMat.GetColor("_EmissionColor").maxColorComponent > 0f))) Block(r2, "underside material emits (ART-INK: no emission): " + rc.undersideMaterial);
            if (cfg.ProtectedAsset(rc.derivedFolder + "/x.asset")) Block(r2, "derived folder is protected: " + rc.derivedFolder);
            int rootsOff = 0, cleanLods = 0, derive = 0;
            foreach (var tg in targets)
            {
                if (cfg.ProtectedRoot(tg.compound.root) || (!string.IsNullOrEmpty(cfg.attractionRoot) && tg.key.StartsWith(cfg.attractionRoot, StringComparison.Ordinal))) { Block(r1, "protected object " + tg.key); continue; }
                var b = BuildingAudit308.Resolve(scene, tg.key);
                if (b == null) { Block(r1, "absent " + tg.key); continue; }
                // R1: the children the kit builds at identity (CompactRebuildAuthoring.Compound297)
                foreach (string cn in rc.childNames)
                {
                    var c = b.Find(cn); if (c == null) continue;
                    if (!RoofChildOff(c, rc, out bool pose, out bool scale)) continue;
                    string key = BuildingAudit308.KeyOf(c);
                    if (pose) r1.changes.Add(new Change308 { step = "R1", key = key, kind = "pose", before = Pose(c), after = Pose(Vector3.zero, Quaternion.identity), state = "apply", at = b.position, note = "child local pose -> authored identity" });
                    if (scale) r1.changes.Add(new Change308 { step = "R1", key = key, kind = "scale", before = RoofVec(c.localScale), after = RoofVec(Vector3.one), state = "apply", at = b.position, note = "child local scale -> authored 1" });
                }
                string dev = RoofRootDeviation(b, tg.entry, rc);
                if (dev != null) { rootsOff++; r1.notes.Add("INFO root differs from the authored layout (not changed here — re-seating belongs to the ground track): " + tg.key + " " + dev); }
                if (r2.status == "blocked") continue;
                // R2: the roof underside per LOD
                foreach (string ln in rc.lodNames)
                {
                    var lt = b.Find(ln); if (lt == null) continue;
                    var mf = lt.GetComponent<MeshFilter>(); var mr = lt.GetComponent<MeshRenderer>();
                    string key = BuildingAudit308.KeyOf(lt);
                    if (mf == null || mr == null || mf.sharedMesh == null) { r2.notes.Add("skip " + key + ": no mesh or renderer"); continue; }
                    string orig = tg.compound.meshFolder.TrimEnd('/') + "/" + tg.name + "_" + ln + ".asset";
                    string derived = rc.derivedFolder.TrimEnd('/') + "/" + tg.compound.name + "/" + tg.name + "_" + ln + rc.derivedSuffix + ".asset";
                    string derivedState = derived + "#" + Path.GetFileNameWithoutExtension(derived);
                    string cur = AssetDatabase.GetAssetPath(mf.sharedMesh);
                    var src = AssetDatabase.LoadAssetAtPath<Mesh>(orig);
                    void Mismatch(string kind, string why) => r2.changes.Add(new Change308 { step = "R2", key = key, kind = kind, before = MeshState(mf), after = derivedState, state = "mismatch", at = b.position, note = why });
                    if (src == null) { Mismatch("mesh", "kit mesh missing: " + orig); continue; }
                    if (!src.isReadable) { Block(r2, "kit mesh is not readable: " + orig); continue; }
                    string[] mats = RoofMaterialPaths(mr);
                    int tileSub = Array.IndexOf(mats, rc.tileMaterial);
                    if (tileSub < 0 || tileSub >= src.subMeshCount) { cleanLods++; r2.notes.Add("clean " + key + ": no roof tile slot"); continue; }
                    int underAt = Array.IndexOf(mats, rc.undersideMaterial);
                    int underSub = underAt >= 0 && underAt < src.subMeshCount ? underAt : src.subMeshCount; bool append = underSub == src.subMeshCount;
                    var info = RoofAnalyse(src, tileSub, rc);
                    string basePaths = string.Join("|", mats.Take(src.subMeshCount)), afterPaths = append ? basePaths + "|" + rc.undersideMaterial : basePaths;
                    string note = orig + "|" + tileSub.ToString(Inv) + "|" + underSub.ToString(Inv) + "|kit: " + RoofFacts(info);
                    if (cur == orig)
                    {
                        if (mats.Length != src.subMeshCount) { Mismatch("materials", "material count " + mats.Length + " != submesh count " + src.subMeshCount); continue; }
                        // every tile triangle without a reversed twin gets one, also where the vertical columns are closed by a ceiling
                        // and soffit: an open storey still shows the slopes from behind at an angle (Jangdae: 73 of 137 offline eyes)
                        if (info.twinless == 0) { cleanLods++; r2.notes.Add("clean " + key + ": the kit mesh already has the underside (" + RoofFacts(info) + ")"); continue; }
                        bool valid = RoofDerivedValid(AssetDatabase.LoadAssetAtPath<Mesh>(derived), src, info, append, tileSub, underSub, rc);
                        r2.changes.Add(new Change308 { step = "R2", key = key, kind = "create-mesh", before = "", after = derived, state = valid ? "already" : "apply", at = b.position, note = note });
                        r2.changes.Add(new Change308 { step = "R2", key = key, kind = "mesh", before = MeshState(mf), after = derivedState, state = "apply", at = b.position, note = "kit: " + RoofFacts(info) + " -> 0 without" });
                        if (append) r2.changes.Add(new Change308 { step = "R2", key = key, kind = "materials", before = basePaths, after = afterPaths, state = "apply", at = b.position, note = "underside slot appended (this LOD has no soffit slot)" });
                        derive++;
                    }
                    else if (cur == derived)
                    {
                        bool valid = RoofDerivedValid(mf.sharedMesh, src, info, append, tileSub, underSub, rc);
                        r2.changes.Add(new Change308 { step = "R2", key = key, kind = "create-mesh", before = "", after = derived, state = valid ? "already" : "apply", at = b.position, note = note + (valid ? "" : " (stale: rebuilt in place)") });
                        r2.changes.Add(new Change308 { step = "R2", key = key, kind = "mesh", before = orig + "#" + src.name, after = derivedState, state = "already", at = b.position, note = "" });
                        bool layoutNow = string.Join("|", mats) == afterPaths, layoutBefore = string.Join("|", mats) == basePaths;
                        if (append) r2.changes.Add(new Change308 { step = "R2", key = key, kind = "materials", before = basePaths, after = afterPaths, state = layoutNow ? "already" : layoutBefore ? "apply" : "mismatch", at = b.position, note = "underside slot" });
                        else if (!layoutNow) Mismatch("materials", "material list changed since the roof apply");
                    }
                    else Mismatch("mesh", "mesh is neither the kit mesh nor the #308 derived mesh: " + cur);
                }
            }
            if (r1.status != "blocked") { Settle(r1); r1.detail = (r1.changes.Count(c => c.state == "apply") == 0 ? "children at the authored identity pose on all " : r1.changes.Count(c => c.state == "apply") + " child pose/scale change(s) on ") + targets.Count + " building(s); " + rootsOff + " root(s) differ from the authored layout (info)"; }
            if (r2.status != "blocked")
            {
                Settle(r2);
                if (r2.status != "mismatch") r2.detail = derive + " LOD mesh(es) get an underside, " + r2.changes.Count(c => c.kind == "mesh" && c.state == "already") + " already have it, " + cleanLods + " LOD(s) left alone (no tile slot or already two-faced)";
            }
            plan.steps.Add(r1); plan.steps.Add(r2);
            foreach (var sp in plan.steps) if (sp.status == "blocked" || sp.status == "mismatch") { plan.blocked = true; plan.blockers.Add(sp.step + " " + sp.status + ": " + sp.detail); }
            plan.notes.Add("targets " + targets.Count + " (" + scope + ")");
            return plan;
        }

        static void RoofWritePlan(Plan308 p) { Directory.CreateDirectory(FixDir); File.WriteAllText(Path.Combine(FixDir, "roof-plan-" + p.alias + ".json"), JsonUtility.ToJson(p, true), new UTF8Encoding(false)); }

        static string RoofPlanText(Plan308 p)
        {
            var sb = new StringBuilder("roof plan " + p.alias + " (" + p.scene + ") sha " + p.sceneSha + " config " + p.configVersion + (p.blocked ? " — BLOCKED" : "") + "\n");
            foreach (var s in p.steps)
            {
                sb.AppendLine("  " + s.step + " " + s.status + ": " + s.detail + " (" + s.changes.Count(c => c.state == "apply") + " to apply, " + s.changes.Count(c => c.state == "already") + " already)");
                if (s.changes.Any(c => c.state == "apply")) sb.AppendLine("     kinds to apply: " + string.Join(", ", s.changes.Where(c => c.state == "apply").GroupBy(c => c.kind).Select(g => g.Key + " " + g.Count())));
                foreach (var c in s.changes.Where(c => c.state == "mismatch").Take(8)) sb.AppendLine("     · mismatch " + c.key + " " + c.kind + ": " + c.note);
                foreach (var n in s.notes.Take(8)) sb.AppendLine("     · " + n);
                if (s.notes.Count > 8) sb.AppendLine("     · … " + (s.notes.Count - 8) + " more note(s) in the plan file");
            }
            foreach (var b in p.blockers) sb.AppendLine("  blocker: " + b);
            sb.AppendLine("-> " + Path.Combine(FixDir, "roof-plan-" + p.alias + ".json"));
            return sb.ToString();
        }

        static string RoofPlanCommand(string alias, string scope)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var rc = RoofLoad(out err); if (rc == null) return "refused: " + err;
            var targets = RoofTargets(rc, scope, out err); if (targets == null) return "refused: " + err;
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            try { var plan = RoofMakePlan(cfg, rc, alias, scene, scope, targets); RoofWritePlan(plan); return RoofPlanText(plan); }
            finally { GoBack(previous, scene, opened, true); }
        }

        // ------------------------------------------------------------------ apply / revert

        static void RoofExecute(RoofCfg308 rc, Scene scene, Change308 c, List<Object> touched)
        {
            var t = BuildingAudit308.Resolve(scene, c.key) ?? throw new InvalidOperationException("missing " + c.key);
            switch (c.kind)
            {
                case "pose":
                    if (!ParsePose(c.after, out var p, out var q)) throw new FormatException(c.after);
                    t.localPosition = p; t.localRotation = q; EditorUtility.SetDirty(t);
                    break;
                case "scale":
                    t.localScale = RoofParseVec(c.after); EditorUtility.SetDirty(t);
                    break;
                case "create-mesh":
                {
                    var parts = c.note.Split('|');
                    var src = AssetDatabase.LoadAssetAtPath<Mesh>(parts[0]) ?? throw new InvalidOperationException("kit mesh missing " + parts[0]);
                    int tileSub = int.Parse(parts[1], Inv), underSub = int.Parse(parts[2], Inv);
                    var dst = AssetDatabase.LoadAssetAtPath<Mesh>(c.after); bool isNew = dst == null;
                    if (isNew) dst = new Mesh();
                    RoofBuildDerived(src, dst, tileSub, underSub, rc);
                    dst.name = Path.GetFileNameWithoutExtension(c.after);
                    if (isNew) { EnsureFolder(Path.GetDirectoryName(c.after).Replace('\\', '/')); AssetDatabase.CreateAsset(dst, c.after); }
                    else EditorUtility.SetDirty(dst);
                    touched.Add(dst);
                    break;
                }
                case "mesh":
                {
                    var f = t.GetComponent<MeshFilter>(); if (f == null) throw new InvalidOperationException("no MeshFilter on " + c.key);
                    var mesh = RoofMeshAt(c.after);
                    if (mesh == null || mesh.vertexCount == 0) throw new InvalidOperationException("mesh asset missing or empty " + c.after);
                    f.sharedMesh = mesh; EditorUtility.SetDirty(f);
                    break;
                }
                case "materials":
                    RoofSetMaterials(t, c.after, c.key);
                    break;
                default: throw new InvalidOperationException("unknown roof change kind " + c.kind);
            }
        }

        static void RoofSetMaterials(Transform t, string paths, string key)
        {
            var r = t.GetComponent<MeshRenderer>(); if (r == null) throw new InvalidOperationException("no MeshRenderer on " + key);
            var list = paths.Split('|').Select(x => AssetDatabase.LoadAssetAtPath<Material>(x)).ToArray();
            if (list.Any(m => m == null)) throw new InvalidOperationException("material missing in " + paths);
            r.sharedMaterials = list; EditorUtility.SetDirty(r);
        }

        static void RoofUndo(Scene scene, Change308 c)
        {
            if (c.kind == "create-mesh") return;   // the derived asset stays on disk; nothing references it after the revert
            var t = BuildingAudit308.Resolve(scene, c.key) ?? throw new InvalidOperationException("missing " + c.key);
            switch (c.kind)
            {
                case "pose": if (!ParsePose(c.before, out var p, out var q)) throw new FormatException(c.before); t.localPosition = p; t.localRotation = q; EditorUtility.SetDirty(t); break;
                case "scale": t.localScale = RoofParseVec(c.before); EditorUtility.SetDirty(t); break;
                case "mesh":
                {
                    var f = t.GetComponent<MeshFilter>(); if (f == null) throw new InvalidOperationException("no MeshFilter on " + c.key);
                    // only undo what this track put there: anything else on top (another track's mesh) is refused, not overwritten
                    string now = MeshState(f);
                    if (now == c.before) break;
                    if (now != c.after) throw new InvalidOperationException("mesh of " + c.key + " is neither the roof-applied nor the original value (" + now + ") — revert the later change first");
                    var mesh = RoofMeshAt(c.before); if (mesh == null) throw new InvalidOperationException("mesh asset missing " + c.before);
                    f.sharedMesh = mesh; EditorUtility.SetDirty(f); break;
                }
                case "materials":
                {
                    var r = t.GetComponent<MeshRenderer>(); if (r == null) throw new InvalidOperationException("no MeshRenderer on " + c.key);
                    string now = string.Join("|", RoofMaterialPaths(r));
                    if (now == c.before) break;
                    if (now != c.after) throw new InvalidOperationException("materials of " + c.key + " are neither the roof-applied nor the original list (" + now + ") — revert the later change first");
                    RoofSetMaterials(t, c.before, c.key); break;
                }
            }
        }

        static string RoofApply(string alias, string scope, string commandText)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var rc = RoofLoad(out err); if (rc == null) return "refused: " + err;
            var targets = RoofTargets(rc, scope, out err); if (targets == null) return "refused: " + err;
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            var op = new Op308 { utc = BuildingAudit308.Utc(), command = "roof " + commandText, scene = scene.path, alias = alias, steps = RoofSteps, quality = BuildingAudit308.QualityName() };
            try
            {
                var plan = RoofMakePlan(cfg, rc, alias, scene, scope, targets);
                RoofWritePlan(plan);
                if (plan.blocked) { GoBack(previous, scene, opened, true); return "refused: nothing changed — " + string.Join(" | ", plan.blockers) + "\n" + RoofPlanText(plan); }
                var todo = plan.steps.SelectMany(s => s.changes).Where(c => c.state == "apply").ToList();
                op.attractionBefore = AttractionHash(scene, cfg.attractionRoot);
                op.shaBefore = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
                op.changes.AddRange(plan.steps.SelectMany(s => s.changes));
                op.stepResults = plan.steps.Select(s => s.step + " " + (s.changes.Any(c => c.state == "apply") ? "applied" : s.status) + ": " + s.detail).ToList();
                // derived assets that already exist and are valid are shared by the three scenes: only scene-side changes need a save
                bool sceneChange = todo.Any(c => c.kind != "create-mesh");
                if (todo.Count == 0)
                {
                    op.status = "already"; op.detail = "no change (idempotent); scene not saved"; op.shaAfter = op.shaBefore; op.attractionAfter = op.attractionBefore;
                    var ledger0 = RoofLoadLedger(alias); ledger0.scene = scene.path; ledger0.ops.Add(op); RoofSaveLedger(ledger0);
                    GoBack(previous, scene, opened, true);
                    return "already: " + alias + " — no change, scene SHA " + op.shaBefore + "\n" + string.Join("\n", op.stepResults);
                }
                if (sceneChange)
                {
                    string backupDir = Path.Combine(BuildingAudit308.Folder, "SceneBackup"); Directory.CreateDirectory(backupDir);
                    op.backup = Path.Combine(backupDir, alias + "-" + op.utc + "-roof.unity");
                    File.Copy(BuildingAudit308.Abs(scene.path), op.backup, false);
                }
                var touched = new List<Object>();
                foreach (var c in todo) RoofExecute(rc, scene, c, touched);
                foreach (var o in touched) if (o != null && AssetDatabase.Contains(o)) AssetDatabase.SaveAssetIfDirty(o);
                if (!sceneChange)
                {
                    op.status = "applied"; op.detail = todo.Count + " derived mesh asset(s) rebuilt; scene unchanged"; op.shaAfter = op.shaBefore; op.attractionAfter = op.attractionBefore;
                }
                else
                {
                    ClearStaleSuffix(scene);
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) { op.status = "FAILED"; op.detail = "SaveScene returned false; the scene stays open and dirty; backup " + op.backup; }
                    else
                    {
                        op.shaAfter = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
                        op.attractionAfter = AttractionHash(scene, cfg.attractionRoot);
                        op.status = op.attractionAfter == op.attractionBefore ? "applied" : "FAILED";
                        op.detail = todo.Count + " change(s) saved" + (op.attractionAfter == op.attractionBefore ? "" : "; Finish297_Attraction block CHANGED — revert with Roof revert:" + alias);
                    }
                }
                var ledger = RoofLoadLedger(alias); ledger.scene = scene.path; ledger.ops.Add(op); RoofSaveLedger(ledger);
                GoBack(previous, scene, opened);
                return op.status + ": roof " + alias + " sha " + op.shaBefore + " -> " + op.shaAfter + (string.IsNullOrEmpty(op.backup) ? "" : ", backup " + op.backup) + "\n" + string.Join("\n", op.stepResults) + "\n" + op.detail;
            }
            catch (Exception e)
            {
                op.status = "FAILED"; op.detail = e.ToString();
                string discarded = "";
                try
                {
                    if (scene.IsValid() && scene.isDirty && string.IsNullOrEmpty(op.shaAfter))
                    {
                        string path = scene.path;
                        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                        discarded = "; unsaved edits discarded (scene reloaded from disk, SHA " + BuildingAudit308.Sha(BuildingAudit308.Abs(path)) + ")";
                        if (opened && !string.IsNullOrEmpty(previous) && previous != path && File.Exists(BuildingAudit308.Abs(previous))) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                    }
                }
                catch (Exception e2) { discarded = "; reload failed, the scene is still open and dirty: " + e2.Message; }
                op.detail += discarded;
                var ledger = RoofLoadLedger(alias); ledger.ops.Add(op); RoofSaveLedger(ledger);
                return "FAILED: roof " + alias + " — " + e.Message + discarded + (string.IsNullOrEmpty(op.backup) ? "" : " (backup " + op.backup + ")");
            }
        }

        static string RoofApplyAll(string scope)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var rc = RoofLoad(out err); if (rc == null) return "refused: " + err;
            var targets = RoofTargets(rc, scope, out err); if (targets == null) return "refused: " + err;
            var lines = new List<string>();
            foreach (var alias in RoofOrder)
            {
                string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
                if (refuse != null) return refuse;
                Plan308 plan;
                try { plan = RoofMakePlan(cfg, rc, alias, scene, scope, targets); RoofWritePlan(plan); }
                finally { GoBack(previous, scene, opened, true); }
                lines.Add(RoofPlanText(plan).TrimEnd());
                if (plan.blocked) return "refused: roof apply-all stopped before any change — " + alias + " has blockers\n" + string.Join("\n", lines);
            }
            foreach (var alias in RoofOrder)
            {
                string r = RoofApply(alias, scope, "apply-all:" + scope);
                lines.Add(alias + ": " + r.Split('\n')[0]);
                if (r.StartsWith("refused", StringComparison.Ordinal) || r.StartsWith("FAILED", StringComparison.Ordinal)) { lines.Add("stopped at " + alias); break; }
            }
            return string.Join("\n", lines);
        }

        static string RoofRevert(string alias, string step)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            if (step != null && !RoofSteps.Contains(step)) return "refused: roof steps are R1 R2 (got " + step + ")";
            var ledger = RoofLoadLedger(alias);
            bool Live(Change308 c) => c.state == "apply" && c.kind != "create-mesh" && (step == null || c.step == step);
            var ops = ledger.ops.Where(o => !o.reverted && (o.status == "applied" || (o.status == "FAILED" && !string.IsNullOrEmpty(o.shaAfter))) && o.changes.Any(Live)).ToList();
            if (ops.Count == 0) return "refused: nothing to revert in " + RoofLedgerFile(alias) + (step != null ? " for " + step : "");
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            string sha = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
            var last = ops.Last();
            var rev = new Op308 { utc = BuildingAudit308.Utc(), command = "roof revert:" + alias + (step != null ? ":" + step : ""), scene = scene.path, alias = alias, shaBefore = sha, steps = step != null ? new[] { step } : RoofSteps };
            // exact restore: one live roof apply, nothing changed the scene since, and it holds only what is being reverted
            bool exact = sha == last.shaAfter && last.scene == scene.path && !cfg.ProtectedAsset(last.scene) && !string.IsNullOrEmpty(last.backup) && File.Exists(last.backup) && ops.Count == 1 && (step == null || last.changes.Where(c => c.state == "apply" && c.kind != "create-mesh").All(c => c.step == step));
            if (exact)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                File.Copy(last.backup, BuildingAudit308.Abs(last.scene), true);
                AssetDatabase.ImportAsset(last.scene, ImportAssetOptions.ForceUpdate);
                EditorSceneManager.OpenScene(last.scene, OpenSceneMode.Single);
                foreach (var c in last.changes.Where(c => c.state == "apply")) c.state = "reverted";
                last.reverted = true; last.revertedUtc = rev.utc;
                rev.status = "reverted"; rev.backup = last.backup; rev.shaAfter = BuildingAudit308.Sha(BuildingAudit308.Abs(last.scene));
                rev.detail = "scene file restored from " + last.backup + (rev.shaAfter == last.shaBefore ? " (SHA = before)" : " (SHA differs from the recorded before " + last.shaBefore + ")");
                ledger.ops.Add(rev); RoofSaveLedger(ledger);
                if (!string.IsNullOrEmpty(previous) && previous != last.scene && File.Exists(BuildingAudit308.Abs(previous))) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                return rev.status + ": roof " + alias + " " + rev.detail;
            }
            string backupDir = Path.Combine(BuildingAudit308.Folder, "SceneBackup"); Directory.CreateDirectory(backupDir);
            rev.backup = Path.Combine(backupDir, alias + "-" + rev.utc + "-roof-prerevert.unity"); File.Copy(BuildingAudit308.Abs(scene.path), rev.backup, false);
            int n = 0;
            try
            {
                foreach (var op in Enumerable.Reverse(ops))
                    foreach (var c in Enumerable.Reverse(op.changes).Where(Live).ToList()) { RoofUndo(scene, c); n++; }
            }
            catch (Exception e)
            {
                string path = scene.path;
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                if (opened && !string.IsNullOrEmpty(previous) && previous != path && File.Exists(BuildingAudit308.Abs(previous))) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                return "FAILED: roof revert " + alias + " after " + n + " change(s) — " + e.Message + "; nothing saved (scene reloaded from disk, pre-revert copy " + rev.backup + ")";
            }
            foreach (var op in ops)
            {
                foreach (var c in op.changes.Where(c => c.state == "apply" && (step == null || c.step == step))) c.state = "reverted";
                if (op.changes.All(c => c.state != "apply")) { op.reverted = true; op.revertedUtc = rev.utc; }
            }
            ClearStaleSuffix(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            rev.shaAfter = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path));
            rev.status = saved ? "reverted" : "FAILED"; rev.detail = n + " change(s) set back to their ledger values (derived mesh assets stay on disk, unreferenced)" + (saved ? "" : "; SaveScene returned false");
            ledger.ops.Add(rev); RoofSaveLedger(ledger);
            GoBack(previous, scene, opened);
            return rev.status + ": roof " + alias + " sha " + sha + " -> " + rev.shaAfter + " — " + rev.detail + " (pre-revert backup " + rev.backup + ")";
        }

        // ------------------------------------------------------------------ read-only reports

        [Serializable] sealed class RoofReport308 { public string alias = "", scene = "", utc = "", sceneSha = "", scope = "", quality = ""; public List<string> rows = new List<string>(); public int fail; }

        static string RoofWriteReport(RoofReport308 r, string name)
        {
            Directory.CreateDirectory(FixDir);
            string file = Path.Combine(FixDir, name + "-" + r.alias + "-" + r.utc + ".json");
            File.WriteAllText(file, JsonUtility.ToJson(r, true), new UTF8Encoding(false));
            return file;
        }

        // (a) every entry of the authored layouts against the scene: root pose and the kit children's local pose
        static string RoofCheck(string alias)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var rc = RoofLoad(out err); if (rc == null) return "refused: " + err;
            var targets = RoofTargets(rc, "all", out err); if (targets == null) return "refused: " + err;
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            var rep = new RoofReport308 { alias = alias, scene = scene.path, utc = BuildingAudit308.Utc(), sceneSha = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path)), scope = "all", quality = BuildingAudit308.QualityName() };
            int absent = 0, rootOff = 0, childOff = 0, children = 0, extra = 0;
            try
            {
                foreach (var tg in targets)
                {
                    var b = BuildingAudit308.Resolve(scene, tg.key);
                    if (b == null) { absent++; rep.rows.Add("ABSENT " + tg.key); continue; }
                    string dev = RoofRootDeviation(b, tg.entry, rc);
                    if (dev != null) { rootOff++; rep.rows.Add("ROOT " + tg.key + " " + dev); }
                    foreach (Transform c in b)
                    {
                        if (!rc.childNames.Contains(c.name)) { extra++; continue; }
                        children++;
                        if (RoofChildOff(c, rc, out _, out _)) { childOff++; rep.rows.Add("CHILD " + BuildingAudit308.KeyOf(c) + " local " + Pose(c) + " scale " + RoofVec(c.localScale)); }
                    }
                }
            }
            finally { GoBack(previous, scene, opened, true); }
            rep.fail = absent + childOff;
            string file = RoofWriteReport(rep, "roof-trs");
            return "roof check " + alias + ": " + targets.Count + " authored entries, " + absent + " absent, " + rootOff + " root(s) off the authored pose, " + childOff + "/" + children + " kit children off identity (" + extra + " other children ignored) -> " + file + (rep.rows.Count > 0 ? "\n" + string.Join("\n", rep.rows.Take(30)) : "");
        }

        // roof sky share per building and LOD on the meshes the scene uses now (family scope)
        static string RoofProbe(string alias)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var rc = RoofLoad(out err); if (rc == null) return "refused: " + err;
            var targets = RoofTargets(rc, "family", out err); if (targets == null) return "refused: " + err;
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            var rep = new RoofReport308 { alias = alias, scene = scene.path, utc = BuildingAudit308.Utc(), sceneSha = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path)), scope = "family", quality = BuildingAudit308.QualityName() };
            int lods = 0, openLods = 0, skyLods = 0, openBuildings = 0, openListed = 0;
            try
            {
                foreach (var tg in targets)
                {
                    var b = BuildingAudit308.Resolve(scene, tg.key); if (b == null) { rep.rows.Add("ABSENT " + tg.key); continue; }
                    var parts = new List<string>(); bool any = false;
                    foreach (string ln in rc.lodNames)
                    {
                        var lt = b.Find(ln); var mf = lt != null ? lt.GetComponent<MeshFilter>() : null; var mr = lt != null ? lt.GetComponent<MeshRenderer>() : null;
                        if (mf == null || mr == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                        int tileSub = Array.IndexOf(RoofMaterialPaths(mr), rc.tileMaterial); if (tileSub < 0) continue;
                        var info = RoofAnalyse(mf.sharedMesh, tileSub, rc); lods++;
                        bool open = info.twinless > 0; if (open) { openLods++; any = true; }
                        if (info.Share >= rc.minOpenShare) skyLods++;
                        parts.Add(ln + " " + info.twinless + "/" + info.tileTris + " sky " + RoofShare(info));
                    }
                    if (any) { openBuildings++; if (rc.listed.Contains(tg.key)) openListed++; }
                    rep.rows.Add((any ? "OPEN " : "CLOSED ") + tg.key + (rc.listed.Contains(tg.key) ? " [listed]" : "") + ": " + string.Join(", ", parts));
                }
            }
            finally { GoBack(previous, scene, opened, true); }
            rep.fail = openLods;
            string file = RoofWriteReport(rep, "roof-probe");
            return "roof probe " + alias + ": " + targets.Count + " buildings, " + lods + " LOD meshes with a tile slot; one-faced roof (tile triangles without an underside): " + openLods + " LOD mesh(es) on " + openBuildings + " building(s), " + openListed + " of them listed; sky straight up (share >= " + rc.minOpenShare.ToString("0.###", Inv) + "): " + skyLods + " LOD mesh(es) -> " + file + "\n" + string.Join("\n", rep.rows.Where(r => r.StartsWith("OPEN", StringComparison.Ordinal)).Take(60));
        }

        static string RoofVerify(string alias, string scope)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var rc = RoofLoad(out err); if (rc == null) return "refused: " + err;
            var targets = RoofTargets(rc, scope, out err); if (targets == null) return "refused: " + err;
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            var rep = new RoofReport308 { alias = alias, scene = scene.path, utc = BuildingAudit308.Utc(), sceneSha = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path)), scope = scope, quality = BuildingAudit308.QualityName() };
            void Row(bool ok, string text) { rep.rows.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) rep.fail++; }
            try
            {
                var underMat = AssetDatabase.LoadAssetAtPath<Material>(rc.undersideMaterial);
                Row(underMat != null && !underMat.IsKeywordEnabled("_EMISSION") && !(underMat.HasProperty("_EmissionColor") && underMat.GetColor("_EmissionColor").maxColorComponent > 0f), "ART-INK underside material has no emission: " + rc.undersideMaterial);
                foreach (var tg in targets)
                {
                    var b = BuildingAudit308.Resolve(scene, tg.key);
                    if (b == null) { Row(false, tg.key + " absent"); continue; }
                    var off = rc.childNames.Select(cn => b.Find(cn)).Where(c => c != null && RoofChildOff(c, rc, out _, out _)).Select(c => c.name).ToArray();
                    Row(off.Length == 0, "R1 " + tg.key + " kit children at the authored identity pose" + (off.Length > 0 ? " — off: " + string.Join(", ", off) : ""));
                    string dev = RoofRootDeviation(b, tg.entry, rc);
                    if (dev != null) rep.rows.Add("INFO R1 " + tg.key + " root differs from the authored layout: " + dev);
                    foreach (string ln in rc.lodNames)
                    {
                        var lt = b.Find(ln); if (lt == null) continue;
                        var mf = lt.GetComponent<MeshFilter>(); var mr = lt.GetComponent<MeshRenderer>();
                        if (mf == null || mr == null || mf.sharedMesh == null) continue;
                        string[] mats = RoofMaterialPaths(mr); int tileSub = Array.IndexOf(mats, rc.tileMaterial);
                        if (tileSub < 0) continue;
                        var now = RoofAnalyse(mf.sharedMesh, tileSub, rc);
                        var src = AssetDatabase.LoadAssetAtPath<Mesh>(tg.compound.meshFolder.TrimEnd('/') + "/" + tg.name + "_" + ln + ".asset");
                        string before = src != null && src.isReadable && tileSub < src.subMeshCount ? RoofFacts(RoofAnalyse(src, tileSub, rc)) : "n/a";
                        Row(now.twinless == 0 && now.Share < rc.minOpenShare, "R2 " + tg.key + "/" + ln + " " + RoofFacts(now) + " (kit mesh: " + before + ")");
                        Row(mats.Length == mf.sharedMesh.subMeshCount && mats.All(m => m.Length > 0), "R2 " + tg.key + "/" + ln + " materials " + mats.Length + " = submeshes " + mf.sharedMesh.subMeshCount);
                    }
                }
            }
            finally { GoBack(previous, scene, opened, true); }
            string file = RoofWriteReport(rep, "roof-verify");
            return "roof verify " + alias + " (" + scope + ", " + targets.Count + " buildings): " + (rep.fail == 0 ? "PASS" : rep.fail + " FAIL") + " -> " + file + "\n" + string.Join("\n", rep.rows.Where(r => !r.StartsWith("PASS", StringComparison.Ordinal)).Take(60));
        }
    }
}
