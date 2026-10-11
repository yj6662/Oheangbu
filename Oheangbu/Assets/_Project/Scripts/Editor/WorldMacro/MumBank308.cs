using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 the river bank where 뭄 is filmed (Cheolong west river; user 2026-10-09: "건너편에 식생도 프롭도 건물도 없어 허전하다 … 철옹답게",
    // "원경에 보이는 산에도 … 잔해들이 깔려있는 느낌 … LOD로 무겁지 않게").
    // World dressing only, [TEST]: existing prefabs stood on the ground, under ONE root (MumBank308) of the main scene. No collider comes
    // along (the banks stay as the bridge planner found them), no script, no light of its own making, no terrain / water / NavMesh /
    // content change. Rows: Tools/Unity/Stage308_trailer/mumbank_dress308.json
    //   { "minY": <rows on lower ground are left out>, "rows": [ { "prefab": "Assets/...prefab", "x":, "z":, "yaw":, "pitch":, "roll":, "s":, "dy":,
    //       "lite": <cell m: the object is drawn with a light copy only (far ruins)>, "lod": <cell m: a light copy takes over at a distance> } ] }
    // Light copies are Ymc308.Cluster of the prefab's own meshes (same materials), kept in Assets/_Project/Art/World/MumBank308/Lite.
    // "thin": <share of the leaf cards kept> for foliage: its light copy keeps whole cards (connected pieces) instead of clustering,
    //       which tears leaf cards into shards (2026-10-10). Used with "lite" or "lod" (their cell is then ignored).
    // Every object gets a LODGroup that stops drawing it when it is a few pixels tall.
    //   plan | build | clear | status
    public static class MumBank308
    {
        const string RootName = "MumBank308", LiteDir = "Assets/_Project/Art/World/MumBank308/Lite";
        const float SwitchAt = .08f, CullAt = .006f;   // share of the screen height [TEST]
        [Serializable] sealed class Row { public string prefab = ""; public float x, z, yaw, pitch, roll, s = 1f, dy, lite, lod, thin; }
        [Serializable] sealed class Data { public float minY = -1e9f; public Row[] rows = Array.Empty<Row>(); }
        static string DataFile => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Tools", "Unity", "Stage308_trailer", "mumbank_dress308.json"));
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Run(string command)
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED edit mode only";
                var scene = SceneManager.GetActiveScene(); var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
                if (command == "status")
                    return root == null ? "no dressing" : RootName + " in " + scene.name + ", " + root.transform.childCount + " objects, triangles nearest level "
                        + root.GetComponentsInChildren<LODGroup>().Sum(l => l.GetLODs()[0].renderers.Sum(r => Triangles(r))).ToString("N0", Inv) + ", farthest level "
                        + root.GetComponentsInChildren<LODGroup>().Sum(l => l.GetLODs().Last().renderers.Sum(r => Triangles(r))).ToString("N0", Inv);
                if (command.StartsWith("water:", StringComparison.Ordinal))
                {
                    // water:<x>,<y>,<z> - which renderers with water in their shader or name reach this point (read only)
                    var c = command.Substring(6).Split(',').Select(v => float.Parse(v, Inv)).ToArray(); var at = new Vector3(c[0], c[1], c[2]);
                    return string.Join(((char)10).ToString(), Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.sharedMaterial != null && r.bounds.SqrDistance(at) < 4f
                        && System.Text.RegularExpressions.Regex.IsMatch(r.sharedMaterial.shader.name + " " + r.name + " " + r.transform.root.name, "(?i)water|river|lake"))
                        .Select(r => r.transform.root.name + "/" + r.name + " | " + AssetDatabase.GetAssetPath(r.sharedMaterial) + " | " + r.sharedMaterial.shader.name + " | y " + r.bounds.min.y.ToString("0.0", Inv) + ".." + r.bounds.max.y.ToString("0.0", Inv) + Flow(r, at)).DefaultIfEmpty("none"));
                }
                if (!scene.path.EndsWith("W_Demo_Main.unity", StringComparison.Ordinal)) return "REFUSED open W_Demo_Main first (active scene " + scene.path + ")";
                if (command == "clear" || command == "build") { string moved = RealmStream308.RefusalFor(RootName); if (moved != null) return moved; }
                if (command == "clear") { if (root != null) { Object.DestroyImmediate(root); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); } return "cleared"; }
                if (command != "plan" && command != "build") return "REFUSED plan | build | clear | status";
                var data = JsonUtility.FromJson<Data>(File.ReadAllText(DataFile)); var sb = new System.Text.StringBuilder(RootName + " " + command + ": " + data.rows.Length + " rows");
                if (command == "build") { if (root != null) Object.DestroyImmediate(root); root = new GameObject(RootName); SceneManager.MoveGameObjectToScene(root, scene); }
                int placed = 0, skipped = 0; var lites = new Dictionary<string, Mesh>();
                foreach (var row in data.rows)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(row.prefab);
                    if (prefab == null) { sb.Append("\n  MISSING ").Append(row.prefab); continue; }
                    if (!Physics.Raycast(new Vector3(row.x, 2000f, row.z), Vector3.down, out var hit, 4000f, ~0, QueryTriggerInteraction.Ignore)) { sb.Append("\n  NO GROUND ").Append(row.x).Append(',').Append(row.z); continue; }
                    if (hit.point.y < data.minY) { skipped++; continue; }
                    if (command == "plan") { sb.Append("\n  ").Append(Path.GetFileNameWithoutExtension(row.prefab)).Append(" ground ").Append(hit.point.y.ToString("0.0", Inv)).Append(" on ").Append(hit.collider.name); continue; }
                    var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform); g.name = Path.GetFileNameWithoutExtension(row.prefab) + "_" + placed.ToString("000");
                    g.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(row.pitch, row.yaw, row.roll)); g.transform.localScale = prefab.transform.localScale * row.s;
                    foreach (var c in g.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                    foreach (var old in g.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(old);
                    var near = g.GetComponentsInChildren<MeshRenderer>().Where(r => r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null).ToArray();
                    if (near.Length == 0) { Object.DestroyImmediate(g); sb.Append("\n  NO RENDERER ").Append(row.prefab); continue; }
                    var b = near[0].bounds; foreach (var r in near) b.Encapsulate(r.bounds);
                    // base of what is drawn on the ground, centred on the row's point
                    g.transform.position = new Vector3(row.x - b.center.x, hit.point.y - b.min.y + row.dy, row.z - b.center.z); placed++;
                    var group = g.AddComponent<LODGroup>();
                    if (row.lite > 0f)
                    {
                        foreach (var r in near) { var f = r.GetComponent<MeshFilter>(); f.sharedMesh = row.thin > 0f ? Thin(f.sharedMesh, row.thin, lites) : Lite(f.sharedMesh, row.lite, r.transform.lossyScale.x / row.s, lites); r.shadowCastingMode = ShadowCastingMode.Off; }
                        group.SetLODs(new[] { new LOD(CullAt, near.Cast<Renderer>().ToArray()) });
                    }
                    else if (row.lod > 0f)
                    {
                        var far = new List<Renderer>();
                        foreach (var r in near)
                        {
                            var copy = new GameObject(r.name + "_Far"); copy.transform.SetParent(r.transform, false);
                            { var src = r.GetComponent<MeshFilter>().sharedMesh; copy.AddComponent<MeshFilter>().sharedMesh = row.thin > 0f ? Thin(src, row.thin, lites) : Lite(src, row.lod, r.transform.lossyScale.x / row.s, lites); }
                            var cr = copy.AddComponent<MeshRenderer>(); cr.sharedMaterials = r.sharedMaterials; cr.shadowCastingMode = ShadowCastingMode.Off; far.Add(cr);
                        }
                        group.SetLODs(new[] { new LOD(SwitchAt, near.Cast<Renderer>().ToArray()), new LOD(CullAt, far.ToArray()) });
                    }
                    else group.SetLODs(new[] { new LOD(CullAt, near.Cast<Renderer>().ToArray()) });
                    group.RecalculateBounds();
                }
                if (command == "build")
                {
                    EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) return "REFUSED the scene was not saved";
                    sb.Append("\n  placed ").Append(placed).Append(", left out under the water line ").Append(skipped).Append(", light copies ").Append(lites.Count).Append('\n').Append(Run("status"));
                }
                return sb.ToString();
            }
            catch (Exception e) { return "REFUSED " + e.GetType().Name + ": " + e.Message; }
        }

        // what the water mesh carries near the point: uv (x across, y along), uv2 (flow), colour (depth, -, edge)
        static string Flow(MeshRenderer r, Vector3 at)
        {
            var m = r.GetComponent<MeshFilter>()?.sharedMesh; if (m == null) return "";
            var v = m.vertices; var uv = m.uv; var uv2 = m.uv2; var col = m.colors; var local = r.transform.InverseTransformPoint(at); int best = 0; float d = 1e18f;
            for (int i = 0; i < v.Length; i++) { float q = (v[i] - local).sqrMagnitude; if (q < d) { d = q; best = i; } }
            int next = best; float d2 = 1e18f; for (int i = 0; i < v.Length; i++) { float q = (v[i] - v[best]).sqrMagnitude; if (i != best && q > .5f && q < d2 && uv.Length == v.Length && Mathf.Abs(uv[i].y - uv[best].y) > 1e-4f) { d2 = q; next = i; } }
            var along = r.transform.TransformVector(v[next] - v[best]) * Mathf.Sign(uv.Length == v.Length ? uv[next].y - uv[best].y : 1f);
            return " | vertices " + v.Length + " uv " + (uv.Length == v.Length ? uv[best].ToString("F2") : "none") + " uv2 " + (uv2.Length == v.Length ? uv2[best].ToString("F3") : "none") + " colour " + (col.Length == v.Length ? col[best].ToString("F2") : "none")
                + " | +uv.y runs toward " + along.normalized.ToString("F2") + " water y " + r.transform.TransformPoint(v[best]).y.ToString("0.00", Inv);
        }
        static long Triangles(Renderer r) { var m = r.GetComponent<MeshFilter>()?.sharedMesh; if (m == null) return 0; long n = 0; for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3; return n; }

        /// <summary>Light copy of card foliage: every connected piece (a leaf card, a twig) is kept whole or dropped; uv and normals untouched.</summary>
        static Mesh Thin(Mesh source, float keep, Dictionary<string, Mesh> cache)
        {
            if (source.vertexCount < 1500) return source;
            string key = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)).Substring(0, 8) + "_" + source.name + "_t" + Mathf.RoundToInt(keep * 100f);
            foreach (char bad in Path.GetInvalidFileNameChars()) key = key.Replace(bad, '_');
            if (cache.TryGetValue(key, out var known)) return known;
            string path = LiteDir + "/" + key + ".asset"; var thin = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (thin == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Project/Art/World/MumBank308")) AssetDatabase.CreateFolder("Assets/_Project/Art/World", "MumBank308");
                if (!AssetDatabase.IsValidFolder(LiteDir)) AssetDatabase.CreateFolder("Assets/_Project/Art/World/MumBank308", "Lite");
                var pos = source.vertices; var nor = source.normals; var uv = source.uv; int n = pos.Length;
                // pieces: vertices at the same place count as one (cards are often split along uv seams)
                var parent = new int[n]; for (int i = 0; i < n; i++) parent[i] = i;
                int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
                var at = new Dictionary<Vector3, int>();
                for (int i = 0; i < n; i++) { if (at.TryGetValue(pos[i], out int first)) parent[Find(i)] = Find(first); else at[pos[i]] = i; }
                var tris = Enumerable.Range(0, source.subMeshCount).Select(source.GetTriangles).ToArray();
                foreach (var t in tris) for (int i = 0; i < t.Length; i += 3) { int a = Find(t[i]); parent[Find(t[i + 1])] = a; parent[Find(t[i + 2])] = a; }
                // every k-th piece in the order the pieces first appear: an even thinning over the whole plant
                var order = new Dictionary<int, int>(); var map = new int[n]; var outPos = new List<Vector3>(); var outNor = new List<Vector3>(); var outUv = new List<Vector2>();
                for (int i = 0; i < n; i++) map[i] = -1;
                float step = 1f / Mathf.Clamp(keep, .01f, 1f);
                bool Kept(int piece) { if (!order.TryGetValue(piece, out int k)) { k = order.Count; order[piece] = k; } return Mathf.FloorToInt((k + 1) / step) > Mathf.FloorToInt(k / step); }
                var outTris = new List<int>[tris.Length];
                for (int s = 0; s < tris.Length; s++)
                {
                    outTris[s] = new List<int>();
                    for (int i = 0; i < tris[s].Length; i += 3)
                    {
                        if (!Kept(Find(tris[s][i]))) continue;
                        for (int c = 0; c < 3; c++)
                        {
                            int v = tris[s][i + c];
                            if (map[v] < 0) { map[v] = outPos.Count; outPos.Add(pos[v]); if (nor.Length == n) outNor.Add(nor[v]); if (uv.Length == n) outUv.Add(uv[v]); }
                            outTris[s].Add(map[v]);
                        }
                    }
                }
                thin = new Mesh { name = key, indexFormat = outPos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                thin.SetVertices(outPos); if (outNor.Count == outPos.Count) thin.SetNormals(outNor); if (outUv.Count == outPos.Count) thin.SetUVs(0, outUv);
                thin.subMeshCount = tris.Length; for (int s = 0; s < tris.Length; s++) thin.SetTriangles(outTris[s], s);
                if (outNor.Count != outPos.Count) thin.RecalculateNormals();
                thin.RecalculateBounds(); AssetDatabase.CreateAsset(thin, path);
            }
            cache[key] = thin; return thin;
        }

        static Mesh Lite(Mesh source, float cell, float unit, Dictionary<string, Mesh> cache)   // cell in metres of the placed prefab; unit = metres per mesh unit
        {
            if (source.vertexCount < 1500) return source;                     // already light
            string key = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)).Substring(0, 8) + "_" + source.name + "_c" + Mathf.RoundToInt(cell * 100f);
            foreach (char bad in Path.GetInvalidFileNameChars()) key = key.Replace(bad, '_');
            if (cache.TryGetValue(key, out var known)) return known;
            string path = LiteDir + "/" + key + ".asset"; var lite = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (lite == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Project/Art/World/MumBank308")) AssetDatabase.CreateFolder("Assets/_Project/Art/World", "MumBank308");
                if (!AssetDatabase.IsValidFolder(LiteDir)) AssetDatabase.CreateFolder("Assets/_Project/Art/World/MumBank308", "Lite");
                lite = Ymc308.Cluster(source.vertices, source.uv, Enumerable.Range(0, source.subMeshCount).Select(source.GetTriangles).ToArray(), cell / Mathf.Max(1e-5f, Mathf.Abs(unit)), key);
                AssetDatabase.CreateAsset(lite, path);
            }
            cache[key] = lite; return lite;
        }
    }
}

