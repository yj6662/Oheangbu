using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Far band (SPEC-WORLD-REALM-STREAM-308 §far): what stays visible of a realm while its scene is not loaded.
    //   far:plan    which moved units are tall or wide enough (rules farMinHeight / farMinSpan), their lowest-LOD renderers and vertices
    //   far:build   one low mesh per unit and material under RealmFar308/<Realm> in the play scene (vertex clustering on farCell,
    //               normals recomputed, no collider, no shadow), assets under farFolder; the loader hides a realm's band while its scene is in
    //   far:clear   take the band out of the play scene (the mesh assets stay on disk)
    //   far:check   counts, no collider, no shadow, loader wiring
    public static partial class RealmStream308
    {
        const string FarRootName = "RealmFar308";

        sealed class FarUnit { public Transform Root; public string Realm; public Bounds Bounds; public List<MeshRenderer> Renderers = new List<MeshRenderer>(); public long Vertices; }

        static string FarCommand(string sub)
        {
            switch (sub)
            {
                case "plan": return FarPlan();
                case "build": return FarBuild();
                case "clear": return FarClear();
                case "check": return FarCheck();
                default: return "far: plan | build | clear | check";
            }
        }

        /// <summary>The moved units that are tall or wide, read from the open realm scenes.</summary>
        static List<FarUnit> FarUnits(Rules r)
        {
            var list = new List<FarUnit>();
            foreach (var realm in LoadRealms(r))
            {
                var scene = SceneManager.GetSceneByPath(ScenePath(r, realm.Id));
                if (!scene.IsValid() || !scene.isLoaded) throw new Refuse("run open-all first (" + ScenePath(r, realm.Id) + " is not open)");
                var root = RealmRoot(scene, realm.Id, false); if (root == null) continue;
                foreach (Transform t in root.transform)
                {
                    if (!t.gameObject.activeInHierarchy) continue;
                    var m = Measure(t, t.name);
                    if (!m.HasBounds || (m.Bounds.size.y < r.farMinHeight && Mathf.Max(m.Bounds.size.x, m.Bounds.size.z) < r.farMinSpan)) continue;
                    var u = new FarUnit { Root = t, Realm = realm.Id, Bounds = m.Bounds };
                    // the lowest level of every LOD group, plus what no group owns
                    var owned = new HashSet<Renderer>(); var lowest = new HashSet<Renderer>();
                    foreach (var group in t.GetComponentsInChildren<LODGroup>(false))
                    {
                        var lods = group.GetLODs();
                        for (int i = 0; i < lods.Length; i++) foreach (var x in lods[i].renderers) if (x != null) owned.Add(x);
                        for (int i = lods.Length - 1; i >= 0; i--) { var some = lods[i].renderers.Where(x => x != null).ToList(); if (some.Count == 0) continue; foreach (var x in some) lowest.Add(x); break; }
                    }
                    foreach (var mr in t.GetComponentsInChildren<MeshRenderer>(false))
                    {
                        if (owned.Contains(mr) ? !lowest.Contains(mr) : !mr.enabled) continue;
                        var f = mr.GetComponent<MeshFilter>(); if (f == null || f.sharedMesh == null) continue;
                        u.Renderers.Add(mr); u.Vertices += f.sharedMesh.vertexCount;
                    }
                    if (u.Renderers.Count > 0) list.Add(u);
                }
            }
            return list;
        }

        static string FarPlan()
        {
            var r = LoadRules(); MainScene(r, false); var units = FarUnits(r);
            var sb = new StringBuilder("RealmStream308 far:plan | height >= " + F(r.farMinHeight) + " m or span >= " + F(r.farMinSpan) + " m | cell " + F(r.farCell, "F2") + " m\n");
            foreach (var g in units.GroupBy(u => u.Realm))
            {
                var meshes = new HashSet<Mesh>(g.SelectMany(u => u.Renderers).Select(x => x.GetComponent<MeshFilter>().sharedMesh));
                sb.AppendLine("  " + g.Key.PadRight(12) + " units " + g.Count().ToString().PadLeft(4) + " lowest-LOD renderers " + N(g.Sum(u => u.Renderers.Count)).PadLeft(6) + " vertices " + N(g.Sum(u => u.Vertices)).PadLeft(11)
                    + " meshes " + meshes.Count + " " + Mb(meshes.Sum(m => Profiler.GetRuntimeMemorySizeLong(m))) + " not readable " + meshes.Count(m => !m.isReadable));
            }
            foreach (var u in units.OrderByDescending(u => u.Vertices).Take(14)) sb.AppendLine("    " + u.Realm.PadRight(12) + N(u.Vertices).PadLeft(10) + " v " + u.Renderers.Count.ToString().PadLeft(5) + " r  h " + F(u.Bounds.size.y) + " span " + F(Mathf.Max(u.Bounds.size.x, u.Bounds.size.z)) + "  " + u.Root.name);
            return sb.ToString();
        }

        static GameObject FarRoot(Scene main, bool create)
        {
            var root = main.GetRootGameObjects().FirstOrDefault(g => g.name == FarRootName);
            if (root != null || !create) return root;
            root = new GameObject(FarRootName); SceneManager.MoveGameObjectToScene(root, main); return root;
        }

        static string FarBuild()
        {
            var r = LoadRules(); var main = MainScene(r, false);
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new Refuse("scene " + SceneManager.GetSceneAt(i).path + " has unsaved changes - nothing written");
            var loader = FindLoader(main); if (loader == null) throw new Refuse("the play scene has no loader - split first");
            if (FarRoot(main, false) != null) throw new Refuse("the play scene already holds " + FarRootName + " - far:clear first");
            var units = FarUnits(r);
            if (units.Count == 0) return "far:build: no unit is tall or wide enough";
            string stamp = DateTime.Now.ToString("yyyyMMddTHHmmss"); string backup = Backup(r, stamp, "far");
            EnsureFolder(r.farFolder);
            SceneManager.SetActiveScene(main);
            var root = FarRoot(main, true); long vertices = 0, bytes = 0, source = 0; int meshes = 0, skipped = 0; var roots = new List<GameObject>(); var ids = new List<string>();
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var realmUnits in units.GroupBy(u => u.Realm))
                {
                    var realmRoot = new GameObject(realmUnits.Key); realmRoot.transform.SetParent(root.transform, false); roots.Add(realmRoot); ids.Add(realmUnits.Key);
                    var taken = new HashSet<string>();
                    foreach (var u in realmUnits)
                    {
                        source += u.Vertices;
                        // one low mesh per material: world positions relative to the unit's bounds centre, clustered
                        Vector3 origin = u.Bounds.center;
                        var byMaterial = new Dictionary<Material, FarSoup>();
                        foreach (var mr in u.Renderers)
                        {
                            var mesh = mr.GetComponent<MeshFilter>().sharedMesh;
                            if (!mesh.isReadable) { skipped++; continue; }
                            var v = mesh.vertices; var uv = mesh.uv; var mats = mr.sharedMaterials; var m = mr.transform.localToWorldMatrix;
                            bool flip = m.determinant < 0;
                            for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                            {
                                if (mats[s] == null || mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                                if (!byMaterial.TryGetValue(mats[s], out var soup)) byMaterial[mats[s]] = soup = new FarSoup();
                                soup.Add(v, uv, mesh.GetTriangles(s), m, origin, r.farCell, flip);
                            }
                        }
                        string name = u.Root.name; for (int k = 2; !taken.Add(name); k++) name = u.Root.name + "_" + k;
                        var host = new GameObject(name); host.transform.SetParent(realmRoot.transform, false); host.transform.position = origin;
                        int part = 0;
                        foreach (var kv in byMaterial)
                        {
                            var low = kv.Value.ToMesh(); if (low == null) continue;
                            low.name = "far_" + realmUnits.Key + "_" + name + "_" + part;
                            string path = r.farFolder + "/" + Safe(low.name) + ".asset";
                            if (File.Exists(Path.Combine(Repo, "Oheangbu", path))) AssetDatabase.DeleteAsset(path);
                            AssetDatabase.CreateAsset(low, path);
                            var go = new GameObject("part_" + part++); go.transform.SetParent(host.transform, false);
                            go.AddComponent<MeshFilter>().sharedMesh = low;
                            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = kv.Key;
                            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                            vertices += low.vertexCount; meshes++;
                        }
                        if (host.transform.childCount == 0) Object.DestroyImmediate(host);
                    }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) bytes += Profiler.GetRuntimeMemorySizeLong(mf.sharedMesh);
            loader.FarRealms = ids.ToArray(); loader.FarRoots = roots.ToArray();
            EditorSceneManager.MarkSceneDirty(main);
            if (!EditorSceneManager.SaveScene(main)) { EditorSceneManager.OpenScene(r.mainScene, OpenSceneMode.Single); return "FAILED far:build: SaveScene returned false - the play scene was reloaded from disk; backup " + backup; }
            FarShow(r, main);   // saved switched on; hidden now only because the realm scenes are open (drawn twice otherwise)
            return "RealmStream308 far:build: " + units.Count + " units -> " + meshes + " meshes | vertices " + N(source) + " -> " + N(vertices) + " | mesh memory " + Mb(bytes) + " | renderers of unreadable meshes skipped " + skipped
                + " | saved " + r.mainScene + " (band switched off while the realm scenes are open; close-all switches it on) | assets " + r.farFolder + " | backup " + backup;
        }

        static string Safe(string name) { foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_'); return name.Replace(' ', '_'); }

        sealed class FarSoup
        {
            readonly Dictionary<Vector3Int, int> index = new Dictionary<Vector3Int, int>(); readonly List<Vector3> sum = new List<Vector3>(); readonly List<int> count = new List<int>(); readonly List<Vector2> uvs = new List<Vector2>();
            readonly HashSet<(int, int, int)> seen = new HashSet<(int, int, int)>(); readonly List<int> tris = new List<int>();
            public void Add(Vector3[] v, Vector2[] uv, int[] t, Matrix4x4 m, Vector3 origin, float cell, bool flip)
            {
                var map = new int[v.Length]; var done = new bool[v.Length];
                for (int i = 0; i < t.Length; i += 3)
                {
                    int a = Map(t[i]), b = Map(t[flip ? i + 2 : i + 1]), c = Map(t[flip ? i + 1 : i + 2]);
                    if (a == b || b == c || a == c) continue;
                    int lo = Mathf.Min(a, Mathf.Min(b, c)), hi = Mathf.Max(a, Mathf.Max(b, c)), mid = a + b + c - lo - hi;
                    if (!seen.Add((lo, mid, hi))) continue;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                }
                int Map(int i)
                {
                    if (done[i]) return map[i];
                    Vector3 p = m.MultiplyPoint3x4(v[i]) - origin;
                    var key = new Vector3Int(Mathf.RoundToInt(p.x / cell), Mathf.RoundToInt(p.y / cell), Mathf.RoundToInt(p.z / cell));
                    if (!index.TryGetValue(key, out int n)) { index[key] = n = sum.Count; sum.Add(Vector3.zero); count.Add(0); uvs.Add(i < uv.Length ? uv[i] : Vector2.zero); }
                    sum[n] += p; count[n]++; done[i] = true; return map[i] = n;
                }
            }
            public Mesh ToMesh()
            {
                if (tris.Count == 0) return null;
                // only the points a kept triangle uses
                var remap = new int[sum.Count]; for (int i = 0; i < remap.Length; i++) remap[i] = -1;
                var points = new List<Vector3>(); var uv = new List<Vector2>(); var t = new int[tris.Count];
                for (int i = 0; i < tris.Count; i++) { int n = tris[i]; if (remap[n] < 0) { remap[n] = points.Count; points.Add(sum[n] / count[n]); uv.Add(uvs[n]); } t[i] = remap[n]; }
                var mesh = new Mesh { indexFormat = points.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(points); mesh.SetUVs(0, uv); mesh.triangles = t; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }
        }

        static string FarClear()
        {
            var r = LoadRules(); var main = MainScene(r, false);
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new Refuse("scene " + SceneManager.GetSceneAt(i).path + " has unsaved changes - nothing written");
            var root = FarRoot(main, false); if (root == null) return "far:clear: 변경 없음 (no " + FarRootName + " in the play scene)";
            string backup = Backup(r, DateTime.Now.ToString("yyyyMMddTHHmmss"), "farclear");
            int n = root.GetComponentsInChildren<MeshRenderer>(true).Length;
            var loader = FindLoader(main); if (loader != null) { loader.FarRealms = Array.Empty<string>(); loader.FarRoots = Array.Empty<GameObject>(); }
            Object.DestroyImmediate(root); EditorSceneManager.MarkSceneDirty(main);
            if (!EditorSceneManager.SaveScene(main)) { EditorSceneManager.OpenScene(r.mainScene, OpenSceneMode.Single); return "FAILED far:clear: SaveScene returned false; backup " + backup; }
            return "RealmStream308 far:clear: " + n + " renderers taken out, saved " + r.mainScene + " (mesh assets left in " + r.farFolder + "); backup " + backup;
        }

        /// <summary>Edit mode only: the band on when the realm scene is closed, off when it is open (not saved - the saved state is what far:build wrote).</summary>
        static string FarShow(Rules r, Scene main)
        {
            var root = FarRoot(main, false); if (root == null) return "";
            int on = 0;   // SetActive alone does not mark the scene dirty
            foreach (Transform t in root.transform)
            {
                bool open = SceneManager.GetSceneByPath(ScenePath(r, t.name)).isLoaded;
                t.gameObject.SetActive(!open); if (!open) on++;
            }
            return "; far band on for " + on + " of " + root.transform.childCount + " realms";
        }

        static string FarCheck()
        {
            var r = LoadRules(); var main = MainScene(r, false); var root = FarRoot(main, false);
            var sb = new StringBuilder("RealmStream308 far:check\n"); int pass = 0, fail = 0;
            void Line(bool ok, string id, string text) { if (ok) pass++; else fail++; sb.AppendLine((ok ? "PASS " : "FAIL ") + id + " " + text); }
            if (root == null) { Line(false, "AC-F1", "no " + FarRootName + " in the play scene"); return sb + "checks, " + pass + " pass " + fail + " fail"; }
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true); var meshes = new HashSet<Mesh>(root.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).Where(m => m != null));
            long vertices = meshes.Sum(m => (long)m.vertexCount), bytes = meshes.Sum(m => Profiler.GetRuntimeMemorySizeLong(m));
            Line(renderers.Length > 0 && meshes.Count == renderers.Length, "AC-F1", "renderers " + renderers.Length + ", meshes " + meshes.Count + ", vertices " + N(vertices) + ", mesh memory " + Mb(bytes));
            Line(bytes <= (long)(r.farBudgetMb * 1048576), "AC-F2", "mesh memory " + Mb(bytes) + " within the budget " + F(r.farBudgetMb) + " MB");
            Line(root.GetComponentsInChildren<Collider>(true).Length == 0 && root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, "AC-F3", "no collider and no script in the band");
            Line(renderers.All(x => x.shadowCastingMode == ShadowCastingMode.Off && x.sharedMaterial != null), "AC-F4", "no shadow casting, every renderer has a material");
            Line(meshes.All(m => AssetDatabase.GetAssetPath(m).StartsWith(r.farFolder, StringComparison.Ordinal)), "AC-F5", "every band mesh is an asset under " + r.farFolder);
            var loader = FindLoader(main); var sheet = loader != null ? loader.Sheet : null;
            bool wired = loader != null && sheet != null && loader.FarRealms != null && loader.FarRoots != null && loader.FarRealms.Length == root.transform.childCount && loader.FarRoots.Length == loader.FarRealms.Length;
            if (wired) for (int i = 0; i < loader.FarRealms.Length; i++) wired &= loader.FarRoots[i] != null && loader.FarRoots[i].name == loader.FarRealms[i] && loader.FarRoots[i].transform.parent == root.transform && sheet.Realms.Any(x => x.Id == loader.FarRealms[i]);
            Line(wired, "AC-F6", "the loader knows the band of every realm (" + (loader != null && loader.FarRealms != null ? loader.FarRealms.Length : 0) + ")");
            return sb + "checks, " + pass + " pass " + fail + " fail";
        }
    }
}
