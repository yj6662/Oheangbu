using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Joins an offline terrain-minus-cave field to the untouched original terrain.</summary>
    public static class NaturalCaveSolidPortal
    {
        const float MinX = -96, MaxX = -8, MinZ = -42, MaxZ = 48;
        static readonly string[] TerrainNames = { "Terrain_052", "Terrain_060" };
        [Serializable] public class HeightGrid
        {
            public float[] xs, zs, heights;
            public string ordering = "x-major: heights[xIndex * zs.Length + zIndex]; cave-local metres";
            public Vector3 rootPosition;
            public float rootYaw;
            public string[] sources;
        }
        struct Vertex
        {
            public Vector3 P, Q, N;
            public Vector2 U;
            public Color C;
            public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex
            {
                P = Vector3.LerpUnclamped(a.P, b.P, t), Q = Vector3.LerpUnclamped(a.Q, b.Q, t),
                N = Vector3.LerpUnclamped(a.N, b.N, t).normalized,
                U = Vector2.LerpUnclamped(a.U, b.U, t), C = Color.LerpUnclamped(a.C, b.C, t)
            };
        }
        public static string Execute(string command)
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Playtest Edit scene required.");
            if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%; stopped.");
            if (command == "export") return Export();
            if (command == "apply") return Apply();
            throw new ArgumentException(command);
        }
        static Transform CaveRoot()
        {
            var root = GameObject.Find(WorldMacroNaturalCave.RootName)?.transform;
            if (root == null) throw new InvalidOperationException("Natural cave missing.");
            return root;
        }
        static Mesh Original(string name)
        {
            string path = WorldMacroBuilder.Folder + "/Meshes/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) throw new InvalidOperationException("Original terrain missing: " + path);
            return mesh;
        }
        public static string Export()
        {
            var root = CaveRoot();
            var temporary = new List<GameObject>();
            var colliders = new List<MeshCollider>();
            bool oldBackfaces = Physics.queriesHitBackfaces;
            try
            {
                foreach (string name in TerrainNames)
                {
                    var active = GameObject.Find(name)?.transform;
                    if (active == null) throw new InvalidOperationException("Active terrain missing: " + name);
                    var go = new GameObject("Temporary_Original_Height_" + name) { hideFlags = HideFlags.HideAndDontSave, layer = 31 };
                    temporary.Add(go);
                    go.transform.SetPositionAndRotation(active.position, active.rotation);
                    go.transform.localScale = active.lossyScale;
                    var collider = go.AddComponent<MeshCollider>();
                    collider.sharedMesh = Original(name);
                    colliders.Add(collider);
                }
                Physics.queriesHitBackfaces = true;
                Physics.SyncTransforms();
                var grid = new HeightGrid
                {
                    xs = Enumerable.Range(0, 89).Select(i => MinX + i).ToArray(),
                    zs = Enumerable.Range(0, 91).Select(i => MinZ + i).ToArray(),
                    heights = new float[89 * 91], rootPosition = root.position, rootYaw = root.eulerAngles.y,
                    sources = TerrainNames.Select(n => AssetDatabase.GetAssetPath(Original(n))).ToArray()
                };
                for (int x = 0; x < grid.xs.Length; x++) for (int z = 0; z < grid.zs.Length; z++)
                {
                    var ray = new Ray(root.TransformPoint(new Vector3(grid.xs[x], 1000, grid.zs[z])), Vector3.down);
                    float height = float.NegativeInfinity;
                    foreach (var collider in colliders)
                        if (collider.Raycast(ray, out var hit, 2000)) height = Mathf.Max(height, root.InverseTransformPoint(hit.point).y);
                    if (float.IsNegativeInfinity(height)) throw new InvalidOperationException("Original height query missed at " + grid.xs[x] + "," + grid.zs[z]);
                    grid.heights[x * grid.zs.Length + z] = height;
                }
                Directory.CreateDirectory(WorldMacroNaturalCave.Output);
                string path = WorldMacroNaturalCave.Output + "/portal_original_height.json";
                File.WriteAllText(path, JsonUtility.ToJson(grid));
                return path + "; 89 x 91 original height samples, x-major; local Y range " + grid.heights.Min() + ".." + grid.heights.Max();
            }
            finally
            {
                Physics.queriesHitBackfaces = oldBackfaces;
                foreach (var go in temporary) Object.DestroyImmediate(go);
                Physics.SyncTransforms();
            }
        }
        public static string Apply()
        {
            var root = CaveRoot();
            var geometry = JsonUtility.FromJson<WorldMacroNaturalCave.Geometry>(File.ReadAllText(WorldMacroNaturalCave.Output + "/portal_solid_geometry.json"));
            if (geometry?.meshes == null || geometry.meshes.Length == 0) throw new InvalidOperationException("Combined portal geometry missing.");
            foreach (var record in geometry.meshes)
                if (record.vertices == null || record.vertices.Length < 3 || record.triangles == null || record.triangles.Length < 3 || record.triangles.Any(i => i < 0 || i >= record.vertices.Length))
                    throw new InvalidOperationException("Invalid combined geometry: " + record.name);
            var material = AssetDatabase.LoadAssetAtPath<Material>(WorldMacroNaturalCave.Folder + "/Materials/CaveRock.mat");
            if (material == null) throw new InvalidOperationException("Owned CaveRock material missing.");
            var terrainMaterial = GameObject.Find("Terrain_052")?.GetComponent<MeshRenderer>()?.sharedMaterial;
            if (terrainMaterial == null) throw new InvalidOperationException("Original macro terrain material missing.");
            DevSceneKit.EnsureFolder(WorldMacroNaturalCave.Folder + "/Meshes");
            // Validate sources before changing any references. Always restart from provider-independent
            // macro terrain originals, discarding all previous entrance lift/cut derivative geometry.
            foreach (string name in TerrainNames)
            {
                Original(name);
                if (GameObject.Find(name)?.GetComponent<MeshCollider>() == null) throw new InvalidOperationException("Active terrain collider missing: " + name);
            }
            var lines = new List<string>
            {
                "Portal patch: local X[-96,-8], Z[-42,48]. Original terrain outside this rectangle is preserved.",
                "Imported field geometry: portal_solid_geometry.json. No rectangle side walls added."
            };
            foreach (string name in TerrainNames) CutTerrain(root, name, lines);

            // Keep the already tested ground underlay and walking approach. Retire only the failed
            // transition bands, including their colliders, so they cannot render through the new solid.
            var transition = root.Find("Mountain_Entrance_Transition");
            if (transition != null)
            {
                transition.gameObject.SetActive(true);
                foreach (Transform child in transition)
                {
                    bool keep = child.name.Contains("Apron");
                    child.gameObject.SetActive(keep);
                    lines.Add((keep ? "Kept " : "Retired ") + child.name);
                }
            }
            var directBadBand = root.Find("Mountain_Rock_Transition");
            if (directBadBand != null) directBadBand.gameObject.SetActive(false);
            var old = root.Find("Solid_Mountain_Portal");
            if (old != null)
            {
                old.name = "Solid_Mountain_Portal_Previous_" + DateTime.Now.ToString("HHmmssfff");
                old.gameObject.SetActive(false);
            }
            var group = new GameObject("Solid_Mountain_Portal").transform;
            group.SetParent(root, false);
            foreach (var record in geometry.meshes)
            {
                var mesh = new Mesh { name = record.name, indexFormat = IndexFormat.UInt32 };
                mesh.vertices = record.vertices;
                mesh.triangles = record.triangles;
                if (record.normals != null && record.normals.Length == record.vertices.Length) mesh.normals = record.normals;
                else mesh.RecalculateNormals();
                mesh.uv = record.vertices.Select(p => new Vector2(p.x, p.z) * .16f).ToArray();
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                Save(mesh);
                var go = new GameObject(record.name);
                go.transform.SetParent(group, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = record.name.Contains("Terrain") ? terrainMaterial : material;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                go.isStatic = true;
                lines.Add(record.name + " vertices=" + mesh.vertexCount + " tris=" + record.triangles.Length / 3 + " local bounds=" + mesh.bounds);
            }
            Physics.SyncTransforms();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            lines.Add("Preserved original embedded interior, cave floor, approach, broad apron, gameplay path and content coordinates.");
            lines.Add("UNVERIFIED: final screenshot, outer seam visibility, entry clearance and route validation require subsequent checks.");
            File.WriteAllLines(WorldMacroNaturalCave.Output + "/portal_solid_integration.txt", lines);
            return string.Join("\n", lines);
        }
        static void CutTerrain(Transform root, string name, List<string> lines)
        {
            var filter = GameObject.Find(name).GetComponent<MeshFilter>();
            var source = Original(name);
            var p = source.vertices; var normals = source.normals; var uv = source.uv; var colors = source.colors;
            var transform = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var vertices = new Vertex[p.Length];
            for (int i = 0; i < p.Length; i++) vertices[i] = new Vertex
            {
                P = p[i], Q = transform.MultiplyPoint3x4(p[i]),
                N = normals.Length == p.Length ? normals[i] : Vector3.up,
                U = uv.Length == p.Length ? uv[i] : Vector2.zero,
                C = colors.Length == p.Length ? colors[i] : Color.white
            };
            // Positive signed distance means outside the rectangle. Retain each outside fragment,
            // then continue clipping only the remainder. The final inside polygon is discarded.
            var planes = new[]
            {
                new Plane(Vector3.left, new Vector3(MinX, 0, 0)),
                new Plane(Vector3.right, new Vector3(MaxX, 0, 0)),
                new Plane(Vector3.back, new Vector3(0, 0, MinZ)),
                new Plane(Vector3.forward, new Vector3(0, 0, MaxZ))
            };
            var output = new List<Vertex>();
            int touched = 0, removed = 0;
            int[] indices = source.triangles;
            for (int i = 0; i < indices.Length; i += 3)
            {
                var polygon = new List<Vertex> { vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]] };
                if (planes.Any(plane => polygon.All(v => plane.GetDistanceToPoint(v.Q) >= 0)))
                { output.AddRange(polygon); continue; }
                touched++;
                int before = output.Count;
                foreach (var plane in planes)
                {
                    if (polygon.Count < 3) break;
                    var inside = new List<Vertex>(); var outside = new List<Vertex>();
                    for (int k = 0; k < polygon.Count; k++)
                    {
                        var a = polygon[k]; var b = polygon[(k + 1) % polygon.Count];
                        float da = plane.GetDistanceToPoint(a.Q), db = plane.GetDistanceToPoint(b.Q);
                        bool ia = da <= 0, ib = db <= 0;
                        if (ia) inside.Add(a); else outside.Add(a);
                        if (ia != ib)
                        {
                            var crossing = Vertex.Lerp(a, b, Mathf.Clamp01(da / (da - db)));
                            inside.Add(crossing); outside.Add(crossing);
                        }
                    }
                    Triangulate(outside, output);
                    polygon = inside;
                }
                if (output.Count == before) removed++;
            }
            var mesh = new Mesh { name = name + "_SolidPortalOutside", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(output.Select(v => v.P).ToList());
            mesh.SetNormals(output.Select(v => v.N).ToList());
            mesh.SetUVs(0, output.Select(v => v.U).ToList());
            if (colors.Length == p.Length) mesh.SetColors(output.Select(v => v.C).ToList());
            mesh.SetTriangles(Enumerable.Range(0, output.Count).ToArray(), 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            Save(mesh);
            filter.sharedMesh = mesh;
            var collider = filter.GetComponent<MeshCollider>();
            collider.sharedMesh = null; collider.sharedMesh = mesh;
            lines.Add(name + " source=" + AssetDatabase.GetAssetPath(source) + " tris=" + indices.Length / 3 + " -> " + output.Count / 3 + "; touched=" + touched + "; removed=" + removed);
        }
        static void Triangulate(List<Vertex> polygon, List<Vertex> output)
        {
            for (int i = 1; i < polygon.Count - 1; i++)
            {
                if (Vector3.Cross(polygon[i].P - polygon[0].P, polygon[i + 1].P - polygon[0].P).sqrMagnitude < 1e-10f) continue;
                output.Add(polygon[0]); output.Add(polygon[i]); output.Add(polygon[i + 1]);
            }
        }
        static void Save(Mesh mesh)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(WorldMacroNaturalCave.Folder + "/Meshes/" + mesh.name + ".asset");
            AssetDatabase.CreateAsset(mesh, path);
        }
    }
}
