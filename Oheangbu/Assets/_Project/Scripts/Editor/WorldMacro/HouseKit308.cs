using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 whole traditional buildings from the downloaded packs, made placeable (D308-65). The packs' models are hundreds of small renderers
    // each; a house placed fifty times would be thousands of draw calls. `bake` merges one source into ONE mesh (a submesh per material, the
    // pack's own materials) and saves a prefab: origin at the middle of the footprint on the ground, one BoxCollider inside the walls.
    //   status
    //   bake      every row of Tools/Unity/Stage308_housekit/housekit308.json -> Assets/_Project/Art/World/HouseKit308/{Meshes,Prefabs}
    // Sources are only read. Nothing in a scene is touched. Queue-safe (no dialog).
    public static class HouseKit308
    {
        const string AssetDir = "Assets/_Project/Art/World/HouseKit308";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        [Serializable] sealed class Row { public string name = "", source = "", kind = ""; public float colliderShrink = .82f, colliderHeight = .6f; public string[] skip = Array.Empty<string>(); }
        [Serializable] sealed class Cfg { public Row[] kit = Array.Empty<Row>(); }
        static Cfg Read() => JsonUtility.FromJson<Cfg>(File.ReadAllText(Path.Combine(RepoRoot, "Tools", "Unity", "Stage308_housekit", "housekit308.json")));
        static void Folder(string path) { if (AssetDatabase.IsValidFolder(path)) return; string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "status") return "HouseKit308 status: " + Read().kit.Length + " rows, prefabs " + (AssetDatabase.IsValidFolder(AssetDir + "/Prefabs") ? AssetDatabase.FindAssets("t:Prefab", new[] { AssetDir + "/Prefabs" }).Length : 0);
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only (Play is running)";
                if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "REFUSED scripts are compiling or failed to compile";
                if (c == "bake") return Bake();
                return "REFUSED unknown command '" + c + "' (status | bake)";
            }
            catch (Exception e) { return "REFUSED " + e.Message; }
        }

        static string Bake()
        {
            Folder(AssetDir + "/Meshes"); Folder(AssetDir + "/Prefabs"); var sb = new StringBuilder("HouseKit308 bake");
            foreach (var row in Read().kit)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(row.source); if (asset == null) { sb.Append("\n  MISSING ").Append(row.source); continue; }
                var inst = Object.Instantiate(asset); inst.hideFlags = HideFlags.HideAndDontSave; inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                try
                {
                    var parts = inst.GetComponentsInChildren<MeshFilter>(false).Where(f => f.sharedMesh != null && f.GetComponent<MeshRenderer>() != null && f.GetComponent<MeshRenderer>().enabled && !row.skip.Any(s => f.name.Contains(s))).ToList();
                    if (parts.Count == 0) { sb.Append("\n  EMPTY ").Append(row.source); continue; }
                    var b = parts[0].GetComponent<Renderer>().bounds; foreach (var f in parts) b.Encapsulate(f.GetComponent<Renderer>().bounds); var origin = new Vector3(b.center.x, b.min.y, b.center.z);
                    var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var subs = new Dictionary<Material, List<int>>(); int sources = 0;
                    foreach (var f in parts)
                    {
                        var mesh = f.sharedMesh; var mats = f.GetComponent<MeshRenderer>().sharedMaterials; var m = f.transform.localToWorldMatrix; var mv = mesh.vertices; var mn = mesh.normals; var mu = mesh.uv; int k0 = v.Count; bool flip = m.determinant < 0f;
                        for (int i = 0; i < mv.Length; i++) { v.Add(m.MultiplyPoint3x4(mv[i]) - origin); var nn = i < mn.Length ? m.inverse.transpose.MultiplyVector(mn[i]) : Vector3.up; n.Add(nn.sqrMagnitude > 1e-12f ? nn.normalized : Vector3.up); uv.Add(i < mu.Length ? mu[i] : Vector2.zero); }
                        for (int s = 0; s < mesh.subMeshCount; s++)
                        {
                            var mat = s < mats.Length ? mats[s] : null; if (mat == null) continue; if (!subs.TryGetValue(mat, out var list)) subs[mat] = list = new List<int>(); var tri = mesh.GetTriangles(s);
                            for (int t = 0; t < tri.Length; t += 3) { list.Add(k0 + tri[t]); list.Add(k0 + tri[flip ? t + 2 : t + 1]); list.Add(k0 + tri[flip ? t + 1 : t + 2]); }
                        }
                        sources++;
                    }
                    var merged = new Mesh { name = "HouseKit308_" + row.name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 }; merged.SetVertices(v); merged.SetNormals(n); merged.SetUVs(0, uv); merged.subMeshCount = subs.Count; int si = 0; var order = subs.Keys.OrderBy(m => m.name, StringComparer.Ordinal).ToList();
                    foreach (var mat in order) merged.SetTriangles(subs[mat], si++, false); merged.RecalculateBounds(); merged.RecalculateTangents();
                    string meshPath = AssetDir + "/Meshes/" + merged.name + ".asset"; var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);      // written in place: deleting and re-creating the asset left the prefab pointing at nothing
                    if (existing != null) { EditorUtility.CopySerialized(merged, existing); EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing); Object.DestroyImmediate(merged); merged = existing; }
                    else { AssetDatabase.CreateAsset(merged, meshPath); AssetDatabase.SaveAssetIfDirty(merged); }
                    AssetDatabase.ImportAsset(meshPath, ImportAssetOptions.ForceUpdate); merged = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    var go = new GameObject(row.name); go.AddComponent<MeshFilter>().sharedMesh = merged; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = order.ToArray();
                    var box = go.AddComponent<BoxCollider>(); var mb = merged.bounds; box.size = new Vector3(mb.size.x * row.colliderShrink, mb.size.y * row.colliderHeight, mb.size.z * row.colliderShrink); box.center = new Vector3(mb.center.x, box.size.y * .5f, mb.center.z);
                    string prefabPath = AssetDir + "/Prefabs/" + row.name + ".prefab"; PrefabUtility.SaveAsPrefabAsset(go, prefabPath); Object.DestroyImmediate(go); AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate); var check = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath); if (check == null || check.GetComponent<MeshFilter>().sharedMesh == null) sb.Append(" BROKEN prefab (mesh reference empty): ").Append(prefabPath);
                    sb.Append("\n  ").Append(row.name).Append(" [").Append(row.kind).Append("]: ").Append(sources).Append(" parts -> 1 mesh, vertices ").Append(v.Count).Append(", materials ").Append(order.Count).Append(", footprint ").Append(mb.size.x.ToString("F1", Inv)).Append(" x ").Append(mb.size.z.ToString("F1", Inv)).Append(" m, height ").Append(mb.size.y.ToString("F1", Inv));
                }
                finally { Object.DestroyImmediate(inst); }
            }
            return sb.ToString();
        }
    }
}
