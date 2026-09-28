using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        [Serializable] sealed class ReliefInput
        {
            public int version, width, height;
            public bool readyForApply, includesRoadGrades;
            public float originX, originZ, spacing;
            public string targetFile, targetSha256, weightFile, weightSha256, sourceHash, mappingHash, fitHash, roadBlendMode;
        }
        [Serializable] sealed class ReliefContextProgress
        {
            public string status, reliefHash, sourceHash;
            public int next, changedVertices;
            public float maximumDelta;
            public string[] paths;
        }

        static string ImportRelief()
        {
            RequireCompact();
            var input = JsonUtility.FromJson<ReliefInput>(File.ReadAllText(Path.Combine(Output, "relief_grid_candidate.json")));
            if (input == null || input.version != 2 || !input.readyForApply || !input.includesRoadGrades || input.roadBlendMode != "bed_and_6m_transition_only" ||
                input.sourceHash != Hash(SourceScene) || input.mappingHash != Hash(Folder + "/Compression.asset") || input.fitHash != Hash(Path.Combine(Output,"route_grade_fit.json")))
                throw new InvalidOperationException("Relief candidate provenance or numeric acceptance missing.");
            float[] ReadField(string file, string sha)
            {
                string dataPath = Path.GetFullPath(Path.Combine(Output, file ?? ""));
                if (!dataPath.StartsWith(Path.GetFullPath(Output) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(dataPath) || Hash(dataPath) != sha)
                    throw new InvalidOperationException("Relief field must match its reviewed local binary.");
                var bytes = File.ReadAllBytes(dataPath);
                if (!BitConverter.IsLittleEndian || bytes.LongLength != (long)input.width * input.height * sizeof(float))
                    throw new InvalidOperationException("Relief field size/endian mismatch.");
                var values = new float[checked(input.width * input.height)]; Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length); return values;
            }
            var field = ScriptableObject.CreateInstance<WorldMacroReliefGridSO>();
            try
            {
                field.Origin = new Vector2(input.originX, input.originZ); field.Spacing = input.spacing;
                field.Width = input.width; field.Height = input.height;
                field.TargetHeights = ReadField(input.targetFile, input.targetSha256); field.Weights = ReadField(input.weightFile, input.weightSha256);
                field.SourceHash = input.sourceHash; field.MappingHash = input.mappingHash;
                field.IncludesRoadGrades = input.includesRoadGrades;
                field.DataHash = input.targetSha256; field.WeightHash = input.weightSha256; field.FitHash = input.fitHash; field.Validate();
                if(field.Origin.x > -2000 || field.Origin.y > -3000 || field.Origin.x + (field.Width-1)*field.Spacing < 2000 || field.Origin.y + (field.Height-1)*field.Spacing < 3000)
                    throw new InvalidOperationException("Relief grid does not cover the compact world bounds.");
                for(int z=0;z<field.Height;z++) for(int x=0;x<field.Width;x++)
                    if((x==0||z==0||x==field.Width-1||z==field.Height-1) && field.Weights[z*field.Width+x] > .000001f)
                        throw new InvalidOperationException("Relief outer boundary must fade to zero weight.");
                string path = Folder + "/Relief.asset";
                var saved = AssetDatabase.LoadAssetAtPath<WorldMacroReliefGridSO>(path);
                if (saved == null) { AssetDatabase.CreateAsset(field, path); saved = field; field = null; }
                else EditorUtility.CopySerialized(field, saved);
                saved.name = "CompactTerrainRelief"; EditorUtility.SetDirty(saved); AssetDatabase.SaveAssetIfDirty(saved);
                return JsonUtility.ToJson(new ReliefContextProgress { status = "IMPORTED_REQUIRES_FULL_GRADE_RESET", reliefHash = Hash(path), sourceHash = input.sourceHash }, true);
            }
            finally { if (field != null) Object.DestroyImmediate(field); }
        }

        static string ReliefContextStep()
        {
            RequireCompact(); var grade = Grade; grade.Validate();
            if (grade.Relief == null) throw new InvalidOperationException("Import relief and reset grade first.");
            var gp = JsonUtility.FromJson<GradeProgress>(File.ReadAllText(GradeProgressPath));
            if (gp.phase != "GRADED_TERRAIN_READY_FOR_PHYSICAL_AUDIT") throw new InvalidOperationException("Finish playable terrain before context.");
            if(gp.profileHash!=Hash(Folder+"/RoadGrade.asset") || gp.reliefHash!=Hash(AssetDatabase.GetAssetPath(grade.Relief)))
                throw new InvalidOperationException("Playable terrain was generated with a different relief/profile dependency.");
            string path = Path.Combine(Output, "relief_context_progress.json"), hash = Hash(Folder + "/RoadGrade.asset") + ":" + Hash(Folder + "/Relief.asset");
            var p = File.Exists(path) ? JsonUtility.FromJson<ReliefContextProgress>(File.ReadAllText(path)) : null;
            var progress = ReadProgress();
            if (p == null || p.reliefHash != hash)
                p = new ReliefContextProgress { status = "RUNNING", reliefHash = hash, sourceHash = Hash(SourceScene),
                    paths = progress.meshes.Where(j => j.path.Contains("BackgroundContext_RenderOnly")).Select(j => j.path).OrderBy(s => s).ToArray() };
            if (p.sourceHash != Hash(SourceScene)) throw new InvalidOperationException("Source changed during context relief.");
            if (p.next < p.paths.Length)
            {
                var job = progress.meshes.Single(j => j.path == p.paths[p.next]); var target = Find(job.path);
                var source = AssetDatabase.LoadAssetAtPath<Mesh>(job.source); var mesh = target.GetComponent<MeshFilter>().sharedMesh;
                if (source == null || source == mesh || AssetDatabase.GetAssetPath(mesh) != job.result || !job.result.StartsWith(Folder + "/Meshes/"))
                    throw new InvalidOperationException("Invalid private context derivative.");
                var vertices = source.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var v = Compression.Map(job.originalMatrix.MultiplyPoint3x4(vertices[i]));
                    float y = grade.Height(v.x, v.z, v.y), delta = y - v.y;
                    if (Mathf.Abs(delta) > .00001f) p.changedVertices++;
                    p.maximumDelta = Mathf.Max(p.maximumDelta, Mathf.Abs(delta)); v.y = y;
                    vertices[i] = target.InverseTransformPoint(v);
                }
                mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                if (mesh.uv.Length == vertices.Length) mesh.RecalculateTangents();
                EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); p.next++;
            }
            p.status = p.next == p.paths.Length ? "COMPLETE" : "RUNNING";
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            string json = JsonUtility.ToJson(p, true); File.WriteAllText(path, json); return json;
        }

        sealed class NormalRow { public Mesh mesh; public Transform transform; public Vector3[] vertices, normals; }
        static string WeldTerrainNormals()
        {
            RequireCompact();
            var rows = ReadProgress().meshes.Where(j => j.path.Contains("01_GlobalTerrain_") || j.path.Contains("BackgroundContext_RenderOnly"))
                .Select(j => Find(j.path)).Where(t => t != null).Select(t => new NormalRow { transform = t, mesh = t.GetComponent<MeshFilter>().sharedMesh }).ToArray();
            var groups = new Dictionary<(long, long, long), List<(int row, int vertex)>>();
            for (int r = 0; r < rows.Length; r++)
            {
                var row = rows[r]; row.vertices = row.mesh.vertices; row.normals = row.mesh.normals;
                if (!AssetDatabase.GetAssetPath(row.mesh).StartsWith(Folder + "/Meshes/") || row.normals.Length != row.vertices.Length)
                    throw new InvalidOperationException("Only complete private terrain normals may be welded.");
                // Irregular playable/context outlines often lie inside the mesh AABB.
                // Find real topological boundary vertices instead of filtering bounds.
                var edges = new Dictionary<ulong, int>();
                void Edge(int a,int b)
                {
                    ulong key=((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
                    edges.TryGetValue(key,out int count);edges[key]=count+1;
                }
                for(int s=0;s<row.mesh.subMeshCount;s++)
                {
                    var triangles=row.mesh.GetTriangles(s);
                    for(int i=0;i<triangles.Length;i+=3)
                    {Edge(triangles[i],triangles[i+1]);Edge(triangles[i+1],triangles[i+2]);Edge(triangles[i+2],triangles[i]);}
                }
                var boundary=new HashSet<int>();
                foreach(var edge in edges)if(edge.Value==1){boundary.Add((int)(edge.Key>>32));boundary.Add((int)(uint)edge.Key);}
                foreach (int i in boundary)
                {
                    var v = row.vertices[i];
                    var p = row.transform.TransformPoint(v);
                    var key = ((long)Math.Round(p.x * 100), (long)Math.Round(p.y * 100), (long)Math.Round(p.z * 100));
                    if (!groups.TryGetValue(key, out var list)) groups.Add(key, list = new List<(int, int)>());
                    list.Add((r, i));
                }
            }
            int welded = 0, points = 0;
            foreach (var group in groups.Values)
            {
                if (group.Select(v => v.row).Distinct().Count() < 2) continue;
                Vector3 sum = Vector3.zero;
                foreach (var v in group) sum += rows[v.row].transform.TransformDirection(rows[v.row].normals[v.vertex]);
                if (sum.sqrMagnitude < .000001f) continue;
                foreach (var v in group) { rows[v.row].normals[v.vertex] = rows[v.row].transform.InverseTransformDirection(sum.normalized); welded++; }
                points++;
            }
            foreach (var row in rows)
            { row.mesh.normals = row.normals; if (row.mesh.uv.Length == row.vertices.Length) row.mesh.RecalculateTangents(); EditorUtility.SetDirty(row.mesh); AssetDatabase.SaveAssetIfDirty(row.mesh); }
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            string json = JsonUtility.ToJson(new NormalReceipt { status = "SHARED_CHUNK_NORMALS_WELDED", meshes = rows.Length, sharedPoints = points, vertexNormals = welded, sourceUnchanged = Hash(SourceScene) == ReadProgress().sourceHash }, true);
            File.WriteAllText(Path.Combine(Output, "terrain_normal_weld.json"), json); return json;
        }
        [Serializable] sealed class NormalReceipt { public string status; public int meshes, sharedPoints, vertexNormals; public bool sourceUnchanged; }
    }
}
