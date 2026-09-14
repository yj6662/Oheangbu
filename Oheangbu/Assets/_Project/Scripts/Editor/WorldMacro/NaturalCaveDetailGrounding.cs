using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Moves whole reused rock components; source geometry and topology stay intact.</summary>
    public static class NaturalCaveDetailGrounding
    {
        sealed class UnionFind
        {
            readonly int[] parent;
            readonly byte[] rank;
            public UnionFind(int size) { parent = Enumerable.Range(0, size).ToArray(); rank = new byte[size]; }
            public int Root(int i)
            {
                while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                return i;
            }
            public void Join(int a, int b)
            {
                a = Root(a); b = Root(b); if (a == b) return;
                if (rank[a] < rank[b]) { int swap = a; a = b; b = swap; }
                parent[b] = a; if (rank[a] == rank[b]) rank[a]++;
            }
        }
        sealed class Component
        {
            public int Id, Triangles;
            public Bounds Bounds;
            public readonly List<int> Vertices = new List<int>();
        }
        public static string Execute(string command)
        {
            if (command != "inspect" && command != "apply" && command != "archive-mouth") throw new ArgumentException(command);
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Playtest Edit scene required.");
            if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%; stopped.");
            var cave = GameObject.Find(WorldMacroNaturalCave.RootName)?.transform;
            var filter = cave?.GetComponentsInChildren<MeshFilter>(false).FirstOrDefault(m => m.name == "Mine_Detail_0");
            if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException("Active Mine_Detail_0 rock batch missing.");
            var source = filter.sharedMesh;
            var vertices = source.vertices;
            var toCave = cave.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var fromCave = filter.transform.worldToLocalMatrix * cave.localToWorldMatrix;
            var local = vertices.Select(v => toCave.MultiplyPoint3x4(v)).ToArray();
            int[] triangles = source.triangles;
            var union = new UnionFind(vertices.Length);
            const float tolerance = .001f;
            var buckets = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < local.Length; i++)
            {
                var p = local[i];
                var key = new Vector3Int(Mathf.FloorToInt(p.x / tolerance), Mathf.FloorToInt(p.y / tolerance), Mathf.FloorToInt(p.z / tolerance));
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    if (buckets.TryGetValue(key + new Vector3Int(x, y, z), out var neighbours))
                        foreach (int j in neighbours) if ((local[j] - p).sqrMagnitude <= tolerance * tolerance) union.Join(i, j);
                if (!buckets.TryGetValue(key, out var same)) { same = new List<int>(); buckets.Add(key, same); }
                same.Add(i);
            }
            for (int i = 0; i < triangles.Length; i += 3)
            { union.Join(triangles[i], triangles[i + 1]); union.Join(triangles[i + 1], triangles[i + 2]); }
            var components = new Dictionary<int, Component>();
            for (int i = 0; i < vertices.Length; i++)
            {
                int id = union.Root(i);
                if (!components.TryGetValue(id, out var component))
                { component = new Component { Id = id, Bounds = new Bounds(local[i], Vector3.zero) }; components.Add(id, component); }
                component.Vertices.Add(i); component.Bounds.Encapsulate(local[i]);
            }
            for (int i = 0; i < triangles.Length; i += 3) components[union.Root(triangles[i])].Triangles++;

            if (command == "archive-mouth")
            {
                if (source.name.Contains("InteriorDetails")) throw new InvalidOperationException("Mouth detail archive already applied.");
                var removed = components.Values.Where(c => c.Bounds.center.x > -60).Select(c => c.Id).ToHashSet();
                var kept = new List<int>();
                for (int i = 0; i < triangles.Length; i += 3) if (!removed.Contains(union.Root(triangles[i])))
                { kept.Add(triangles[i]); kept.Add(triangles[i + 1]); kept.Add(triangles[i + 2]); }
                var copy = Object.Instantiate(source); copy.name = "Mine_InteriorDetails_PortalCleared";
                copy.SetTriangles(kept, 0); copy.RecalculateBounds();
                string path = AssetDatabase.GenerateUniqueAssetPath(WorldMacroNaturalCave.Folder + "/Meshes/" + copy.name + ".asset");
                AssetDatabase.CreateAsset(copy, path); filter.sharedMesh = copy;
                var collider = filter.GetComponent<MeshCollider>(); if (collider != null) { collider.sharedMesh = null; collider.sharedMesh = copy; }
                Physics.SyncTransforms(); AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                string record = "Portal silhouette cleanup: archived " + removed.Count + " decorative rock placements with centres X > -60 from the active batch. Natural portal surface, interior props and original source mesh retained.\nSource=" + AssetDatabase.GetAssetPath(source) + "\nDerivative=" + path + "\nTriangles=" + triangles.Length / 3 + " -> " + kept.Count / 3 + "; removed=" + (triangles.Length - kept.Count) / 3 + ". Not a grounding PASS; source pivot bounds passed floor depth while still reading as detached in the actual capture.\n";
                File.WriteAllText(WorldMacroNaturalCave.Output + "/detail_mouth_archive.txt", record); return record;
            }

            var floors = Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)
                .Where(m => m.enabled && m.gameObject.activeInHierarchy &&
                    (m.name == "Continuous_Approach_Soil" || m.name == "Solid_Mountain_Portal_Terrain" || m.name == "Solid_Mountain_Portal_Rock" || m.name == "Natural_Cave_Floor" || m.name == "Terrain_052" || m.name == "Terrain_060"))
                .ToArray();
            if (floors.Length == 0) throw new InvalidOperationException("Approved actual floor colliders missing.");
            Physics.SyncTransforms();
            var lines = new List<string>
            {
                "Source=" + AssetDatabase.GetAssetPath(source),
                "Object=" + filter.name + " localPosition=" + filter.transform.localPosition + " localRotation=" + filter.transform.localEulerAngles + " localScale=" + filter.transform.localScale,
                "1mm position welding joins UV seams. Components=" + components.Count + "; vertices=" + vertices.Length + "; triangles=" + triangles.Length / 3,
                "Target range: cave-local component centre X > -60. Rigid lowering only, maximum 6m. Five floor probes start at cave-local Y=3 and run downward 40m. Backface queries enabled; upward normals >.25 required.",
                "Floor colliders=" + string.Join(",", floors.Select(f => f.name))
            };
            int candidates = 0, lowered = 0, noGround = 0;
            bool oldBackfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
            foreach (var component in components.Values.OrderByDescending(c => c.Bounds.center.x))
            {
                var bounds = component.Bounds;
                string prefix = "component=" + component.Id + " vertices=" + component.Vertices.Count + " triangles=" + component.Triangles +
                    " centre=" + bounds.center.ToString("F4") + " min=" + bounds.min.ToString("F4") + " max=" + bounds.max.ToString("F4");
                if (bounds.center.x <= -60) { lines.Add(prefix + " preserved"); continue; }
                candidates++;
                var ground = new List<float>();
                var probes = new[]
                {
                    Vector3.zero,
                    new Vector3(-bounds.extents.x * .65f, 0, -bounds.extents.z * .65f),
                    new Vector3(-bounds.extents.x * .65f, 0, bounds.extents.z * .65f),
                    new Vector3(bounds.extents.x * .65f, 0, -bounds.extents.z * .65f),
                    new Vector3(bounds.extents.x * .65f, 0, bounds.extents.z * .65f)
                };
                foreach (var offset in probes)
                {
                    var point = bounds.center + offset; point.y = 3;
                    var ray = new Ray(cave.TransformPoint(point), cave.TransformDirection(Vector3.down));
                    float nearest = float.PositiveInfinity, height = 0;
                    foreach (var floor in floors)
                    {
                        if (!floor.Raycast(ray, out var hit, 40) || Vector3.Dot(hit.normal, cave.up) < .25f || hit.distance >= nearest) continue;
                        nearest = hit.distance; height = cave.InverseTransformPoint(hit.point).y;
                    }
                    if (!float.IsPositiveInfinity(nearest)) ground.Add(height);
                }
                if (ground.Count == 0) { noGround++; lines.Add(prefix + " NO_VALID_GROUND; unchanged"); continue; }
                float requestedDelta = Mathf.Min(0, ground.Min() - bounds.min.y - .12f);
                float delta = Mathf.Max(-6, requestedDelta);
                lines.Add(prefix + " validProbes=" + ground.Count + "/5 floors=" + string.Join(",", ground.Select(h => h.ToString("F4"))) + " rigidDeltaY=" + delta.ToString("F4") + " capped=" + (requestedDelta < -6));
                if (delta >= -.001f) continue;
                lowered++;
                if (command == "apply") foreach (int i in component.Vertices)
                    vertices[i] = fromCave.MultiplyPoint3x4(local[i] + Vector3.up * delta);
            }
            }
            finally { Physics.queriesHitBackfaces = oldBackfaces; }
            lines.Add("Candidates=" + candidates + "; lower=" + lowered + "; noGround=" + noGround);
            if (command == "apply" && lowered > 0)
            {
                var copy = Object.Instantiate(source);
                copy.name = "Mine_Detail_0_Grounded";
                copy.vertices = vertices;
                copy.RecalculateBounds();
                string path = AssetDatabase.GenerateUniqueAssetPath(WorldMacroNaturalCave.Folder + "/Meshes/" + copy.name + ".asset");
                AssetDatabase.CreateAsset(copy, path);
                filter.sharedMesh = copy;
                var collider = filter.GetComponent<MeshCollider>();
                if (collider != null) { collider.sharedMesh = null; collider.sharedMesh = copy; }
                if (copy.vertexCount != source.vertexCount || copy.triangles.Length != triangles.Length)
                    throw new InvalidOperationException("Topology changed unexpectedly.");
                lines.Add("Derivative=" + path + "; renderer and existing collider references updated; no new collider added.");
                Physics.SyncTransforms(); AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
            lines.Add("Unchanged: triangle/index topology, UVs, normals, materials and original source asset. Visual grounding remains to be checked after apply.");
            Directory.CreateDirectory(WorldMacroNaturalCave.Output);
            string report = WorldMacroNaturalCave.Output + "/detail_grounding_" + command + ".txt";
            File.WriteAllLines(report, lines);
            return report + "\n" + string.Join("\n", lines);
        }
    }
}
