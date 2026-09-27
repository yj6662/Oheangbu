using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.Data.World;
using Oheangbu.App.World.Dressing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        [Serializable] sealed class GradeRoute
        {
            public string id; public bool carriage; public float width;
            public Vector3[] points; public float[] mappedOriginalY;
        }
        [Serializable] sealed class GradeMainPathReport { public float[] mappedOriginalY; }
        [Serializable] sealed class GradeInput
        {
            public bool readyForSculpt; public GradeRoute[] routes; public Vector3[] mainPath;
            public GradeMainPathReport mainPathReport;
        }
        [Serializable] sealed class GradeTerrainRecord
        {
            public string path, source, target;
            public int sourceVertices, sourceTriangles, rebuiltVertices, rebuiltTriangles;
            public float maxVertexChange;
        }
        [Serializable] sealed class GradeProgress
        {
            public string phase, fitHash, profileHash, reliefHash;
            public int profileVersion;
            public string[] terrain;
            public int nextTerrain, movedProps, adjustedRoadVertices, restoredStandaloneRoots, retainedTerrain;
            public int sourceVertices, sourceTriangles, rebuiltVertices, rebuiltTriangles;
            public float maxVertexChange;
            public bool attachmentsHeld = true, legacyTypedAttachmentsRequireRestore;
            public string attachmentStatus = "Terrain only. Ground attachments and road ribbons require a separate source-based restore and physical support pass.";
            public GradeTerrainRecord[] rebuilt = Array.Empty<GradeTerrainRecord>();
        }
        sealed class GradeRootRestore { public Transform target; public float originalY; }
        static string GradeProgressPath => Path.Combine(Output, "grade_progress.json");
        static WorldMacroRoadGradeSO Grade => AssetDatabase.LoadAssetAtPath<WorldMacroRoadGradeSO>(Folder + "/RoadGrade.asset");
        static string GradeReceipt(GradeProgress p)
        {
            string json = JsonUtility.ToJson(p, true); File.WriteAllText(GradeProgressPath, json); return json;
        }
        static string BeginGrade() => PrepareGrade(false);
        static string ResetGrade() => PrepareGrade(true);
        static string PatchGrade() => PrepareGrade(true, true);

        static Rect[] ChangedGradeFootprints(WorldMacroRoadGradeSO previous, WorldMacroRoadGradeSO next)
        {
            if(previous.Relief!=null||next.Relief!=null)
                throw new InvalidOperationException("A relief field is coupled across the terrain; rebuild it with grade-reset.");
            if(previous.Version!=next.Version||previous.Shoulder!=next.Shoulder||previous.RoadbedPadding!=next.RoadbedPadding||previous.RoadbedTransition!=next.RoadbedTransition||!previous.Protected.SequenceEqual(next.Protected))
                throw new InvalidOperationException("Incremental grade requires unchanged evaluator, influence radii and protection. Use grade-reset.");
            var changed=new List<Rect>();
            void Include(WorldMacroRoadGradeSO.Line line,int i)
            {
                var a=line.Points[i-1];var b=line.Points[i];float radius=line.Width*.5f+next.RoadbedPadding+Mathf.Max(next.Shoulder,next.RoadbedTransition)+.01f;
                changed.Add(Rect.MinMaxRect(Mathf.Min(a.x,b.x)-radius,Mathf.Min(a.z,b.z)-radius,Mathf.Max(a.x,b.x)+radius,Mathf.Max(a.z,b.z)+radius));
            }
            foreach(var line in previous.Lines)
            {
                var other=next.Lines.SingleOrDefault(n=>n.Id==line.Id);
                if(other==null)throw new InvalidOperationException("Incremental grade cannot remove a route.");
                if(line.Width!=other.Width||line.Points.Length!=other.Points.Length)
                {for(int i=1;i<line.Points.Length;i++)Include(line,i);for(int i=1;i<other.Points.Length;i++)Include(other,i);continue;}
                for(int i=1;i<line.Points.Length;i++)
                    if(!line.Points[i-1].Equals(other.Points[i-1])||!line.Points[i].Equals(other.Points[i])||line.OriginalY[i-1]!=other.OriginalY[i-1]||line.OriginalY[i]!=other.OriginalY[i])
                    {Include(line,i);Include(other,i);}
            }
            if(previous.Lines.Length!=next.Lines.Length)throw new InvalidOperationException("Incremental grade cannot add a route.");
            return changed.ToArray();
        }

        // Resolve every root before mutating anything. Original world Y also restores all
        // descendants by translation; prefab subparts and typed foliage packets are not edited.
        static GradeRootRestore[] GradeOriginalRootHeights(Progress progress)
        {
            var original = JsonUtility.FromJson<CompactOriginalSnapshot>(File.ReadAllText(Path.Combine(Output, "content_audit.json")));
            if (original?.transforms == null || original.sceneSha256 != progress.sourceHash)
                throw new InvalidOperationException("Original transform snapshot does not match the prepared source scene.");
            var byPath = original.transforms.Where(t => t.position != null && t.position.Length == 3)
                .GroupBy(t => t.path).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            var result = new List<GradeRootRestore>();
            foreach (string parent in new[] { "Playtest_VisualCorridor/02_MountainAndForest", "Playtest_EarlyArt" })
            {
                var scope = Find(parent);
                if (scope == null) throw new InvalidOperationException("Ground attachment scope is missing: " + parent);
                foreach (Transform child in scope)
                {
                    if (child.GetComponent<EarlyRegionFoliage>() != null) continue;
                    string path = Hierarchy(child);
                    if (!byPath.TryGetValue(path, out var candidates))
                        throw new InvalidOperationException("Cannot restore ground root without original transform: " + path);
                    var matches = candidates.Where(c =>
                    {
                        var expected = Compression.Map(new Vector3(c.position[0], c.position[1], c.position[2]));
                        return new Vector2(expected.x - child.position.x, expected.z - child.position.z).sqrMagnitude <= .025f * .025f;
                    }).ToArray();
                    if (matches.Length != 1)
                        throw new InvalidOperationException("Original ground root is missing or ambiguous at compact XZ: " + path);
                    result.Add(new GradeRootRestore { target = child, originalY = matches[0].position[1] });
                }
            }
            return result.ToArray();
        }

        static string PrepareGrade(bool reset, bool incremental=false)
        {
            RequireCompact();
            if (!reset && File.Exists(GradeProgressPath))
                throw new InvalidOperationException("Grade already begun; continue grade-step or use grade-reset to rebuild from original meshes.");
            var progress = ReadProgress();
            if (Hash(SourceScene) != progress.sourceHash)
                throw new InvalidOperationException("Original source scene changed; grading inputs are stale.");
            string fitPath = Path.Combine(Output, "route_grade_fit.json");
            var fit = JsonUtility.FromJson<GradeInput>(File.ReadAllText(fitPath));
            if (fit == null || !fit.readyForSculpt || fit.routes == null || fit.mainPath == null || fit.mainPathReport == null)
                throw new InvalidOperationException("Coupled route grade plan has not passed numeric constraints or lacks original Y.");
            var geo = progress.assets.Select(a => AssetDatabase.LoadMainAssetAtPath(a.target)).OfType<WorldMacroSheetSO>().Single();
            var content = progress.assets.Select(a => AssetDatabase.LoadMainAssetAtPath(a.target)).OfType<WorldMacroPlaytestSO>().Single();
            // Validate correspondence before writing profile, geometry data or scene transforms.
            foreach (var route in geo.Routes)
                if (fit.routes.Count(r => r.id == route.Id) != 1)
                    throw new InvalidOperationException("Fitted route identity is missing or duplicated: " + route.Id);
            var previous = File.Exists(GradeProgressPath) ? JsonUtility.FromJson<GradeProgress>(File.ReadAllText(GradeProgressPath)) : null;
            var roots = reset ? GradeOriginalRootHeights(progress) : Array.Empty<GradeRootRestore>();
            var terrain = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<MeshFilter>(true))
                .Where(f => f.name.StartsWith("Terrain_", StringComparison.Ordinal) && f.sharedMesh != null &&
                    AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith(Folder + "/Meshes/", StringComparison.Ordinal))
                .Select(f => Hierarchy(f.transform)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (terrain.Length == 0) throw new InvalidOperationException("No compact terrain meshes found.");
            foreach (string path in terrain)
            {
                var job = progress.meshes.Single(j => j.path == path);
                if (job.road || string.IsNullOrEmpty(job.source) || job.source.StartsWith(Folder + "/", StringComparison.Ordinal) ||
                    AssetDatabase.LoadAssetAtPath<Mesh>(job.source) == null || job.source == job.result)
                    throw new InvalidOperationException("Immutable source terrain mesh is invalid: " + path);
            }
            var candidate = ScriptableObject.CreateInstance<WorldMacroRoadGradeSO>();
            try
            {
                candidate.Version = WorldMacroRoadGradeSO.CurrentVersion;
                candidate.Relief = AssetDatabase.LoadAssetAtPath<WorldMacroReliefGridSO>(Folder + "/Relief.asset");
                if(candidate.Relief!=null && candidate.Relief.FitHash!=Hash(fitPath))
                    throw new InvalidOperationException("Relief was solved with a different road plan; regenerate the coupled field first.");
                var lines = fit.routes.Select(r => new WorldMacroRoadGradeSO.Line
                    { Id = r.id, Width = r.width, Points = r.points, OriginalY = r.mappedOriginalY }).ToList();
                lines.Add(new WorldMacroRoadGradeSO.Line
                    { Id = "Content.MainPath", Width = 4.1f, Points = fit.mainPath, OriginalY = fit.mainPathReport.mappedOriginalY });
                candidate.Lines = lines.ToArray();
                candidate.Protected = progress.zones.Where(z => z.kind != "navigationVolume").Select(z =>
                {
                    var a = Map2(new Vector2(z.minX - 2, z.minZ - 2)); var b = Map2(new Vector2(z.maxX + 2, z.maxZ + 2));
                    return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
                }).ToArray();
                candidate.Validate();
                var profile = Grade;
                GradeTerrainRecord[] retained=Array.Empty<GradeTerrainRecord>();
                if(incremental)
                {
                    if(profile==null||previous==null||previous.phase!="GRADED_TERRAIN_READY_FOR_PHYSICAL_AUDIT"||previous.rebuilt.Length!=terrain.Length)
                        throw new InvalidOperationException("Incremental grade requires a complete recorded prior sculpt.");
                    var influence=ChangedGradeFootprints(profile,candidate);
                    var affected=terrain.Where(path=>
                    {
                        var renderer=Find(path).GetComponent<MeshRenderer>();if(renderer==null)throw new InvalidOperationException("Missing terrain bounds: "+path);
                        var b=renderer.bounds;var rect=Rect.MinMaxRect(b.min.x,b.min.z,b.max.x,b.max.z);
                        return influence.Any(r=>r.Overlaps(rect,true));
                    }).ToHashSet(StringComparer.Ordinal);
                    retained=previous.rebuilt.Where(r=>!affected.Contains(r.path)).ToArray();
                    terrain=terrain.Where(affected.Contains).ToArray();
                }
                if (profile == null)
                {
                    AssetDatabase.CreateAsset(candidate, Folder + "/RoadGrade.asset"); profile = candidate; candidate = null;
                }
                else
                {
                    string originalName = profile.name;
                    EditorUtility.CopySerialized(candidate, profile); profile.name = originalName;
                }
                profile.InvalidateCache(); refinementProfile = null; refinementRoadIndex = null;
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                foreach (var route in geo.Routes) route.Points = fit.routes.Single(r => r.id == route.Id).points;
                content.MainPath = fit.mainPath; geo.CompactRoadGrade = profile;
                EditorUtility.SetDirty(geo); EditorUtility.SetDirty(content);
                AssetDatabase.SaveAssetIfDirty(geo); AssetDatabase.SaveAssetIfDirty(content);
                int restored = 0;
                foreach (var root in roots)
                {
                    var position = root.target.position;
                    if (Mathf.Abs(position.y - root.originalY) <= .00001f) continue;
                    position.y = root.originalY; root.target.position = position; restored++;
                }
                if (reset && File.Exists(GradeProgressPath))
                    File.Copy(GradeProgressPath, Path.Combine(Output, "grade_progress.before-reset-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".json"), false);
                Physics.SyncTransforms(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                return GradeReceipt(new GradeProgress
                {
                    phase = terrain.Length==0?"GRADED_TERRAIN_READY_FOR_PHYSICAL_AUDIT":"TERRAIN", profileVersion = WorldMacroRoadGradeSO.CurrentVersion, fitHash = Hash(fitPath),
                    profileHash=Hash(Folder+"/RoadGrade.asset"), reliefHash=profile.Relief==null?"":Hash(AssetDatabase.GetAssetPath(profile.Relief)),
                    terrain = terrain, restoredStandaloneRoots = restored, retainedTerrain=retained.Length, rebuilt=retained,
                    sourceVertices=retained.Sum(r=>r.sourceVertices),sourceTriangles=retained.Sum(r=>r.sourceTriangles),rebuiltVertices=retained.Sum(r=>r.rebuiltVertices),rebuiltTriangles=retained.Sum(r=>r.rebuiltTriangles),maxVertexChange=retained.Select(r=>r.maxVertexChange).DefaultIfEmpty(0).Max(),
                    legacyTypedAttachmentsRequireRestore = previous != null &&
                        (previous.legacyTypedAttachmentsRequireRestore || previous.movedProps > 0 ||
                         previous.adjustedRoadVertices > 0 || previous.phase == "GRADED_GEOMETRY_READY_FOR_PHYSICAL_AUDIT")
                });
            }
            finally { if (candidate != null) Object.DestroyImmediate(candidate); }
        }

        static int GradeTriangleCount(Mesh mesh)
        {
            int count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (mesh.GetTopology(i) != MeshTopology.Triangles) throw new InvalidOperationException("Terrain submesh is not triangles.");
                count = checked(count + (int)mesh.GetIndexCount(i) / 3);
            }
            return count;
        }

        static string StepGrade()
        {
            RequireCompact();
            var p = JsonUtility.FromJson<GradeProgress>(File.ReadAllText(GradeProgressPath));
            var profile = Grade;
            if (profile == null || p.profileVersion != WorldMacroRoadGradeSO.CurrentVersion)
                throw new InvalidOperationException("Grade profile/progress is stale; use grade-reset.");
            profile.Validate();
            if (p.fitHash != Hash(Path.Combine(Output, "route_grade_fit.json")))
                throw new InvalidOperationException("Grade fit changed after begin; use grade-reset.");
            if(p.profileHash!=Hash(Folder+"/RoadGrade.asset") || (p.reliefHash??"")!=(profile.Relief==null?"":Hash(AssetDatabase.GetAssetPath(profile.Relief))))
                throw new InvalidOperationException("Grade or relief asset changed after begin; use grade-reset.");
            if (p.phase == "GRADED_TERRAIN_READY_FOR_PHYSICAL_AUDIT") return JsonUtility.ToJson(p, true);
            if (p.phase != "TERRAIN")
                throw new InvalidOperationException("Automatic ground attachments are held. Use grade-reset for source-based terrain rebuilding.");
            var progress = ReadProgress();
            // One refined chunk per call bounds transient mesh memory and editor blocking.
            if (p.nextTerrain < p.terrain.Length)
            {
                string path = p.terrain[p.nextTerrain];
                var job = progress.meshes.Single(j => j.path == path);
                var target = Find(path);
                if (target == null) throw new InvalidOperationException("Terrain transform missing: " + path);
                var filter = target.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || AssetDatabase.GetAssetPath(mesh) != job.result ||
                    !job.result.StartsWith(Folder + "/Meshes/", StringComparison.Ordinal) || job.source == job.result)
                    throw new InvalidOperationException("Terrain destination is not the recorded compact derivative: " + path);
                var original = AssetDatabase.LoadAssetAtPath<Mesh>(job.source);
                if (original == null || original == mesh || job.source.StartsWith(Folder + "/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Original terrain mesh is missing or aliases its derivative: " + path);
                Mesh rebuilt = null;
                try
                {
                    // Always starts from original vertices/triangles and job.originalMatrix.
                    // A retry or reset never uses the height/topology of the previous sculpt.
                    rebuilt = BuildRefinedTerrain(original, job, target, profile, out float maxChange);
                    if (rebuilt == null || rebuilt == original || rebuilt == mesh)
                        throw new InvalidOperationException("Terrain refinement must return an independent transient mesh.");
                    var row = new GradeTerrainRecord
                    {
                        path = path, source = job.source, target = job.result,
                        sourceVertices = original.vertexCount, sourceTriangles = GradeTriangleCount(original),
                        rebuiltVertices = rebuilt.vertexCount, rebuiltTriangles = GradeTriangleCount(rebuilt),
                        maxVertexChange = maxChange
                    };
                    string meshName = mesh.name;
                    EditorUtility.CopySerialized(rebuilt, mesh); mesh.name = meshName;
                    mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
                    filter.sharedMesh = mesh;
                    var collider = target.GetComponent<MeshCollider>();
                    if (collider != null) { collider.sharedMesh = null; collider.sharedMesh = mesh; }
                    p.sourceVertices += row.sourceVertices; p.sourceTriangles += row.sourceTriangles;
                    p.rebuiltVertices += row.rebuiltVertices; p.rebuiltTriangles += row.rebuiltTriangles;
                    p.maxVertexChange = Mathf.Max(p.maxVertexChange, maxChange);
                    p.rebuilt = (p.rebuilt ?? Array.Empty<GradeTerrainRecord>()).Concat(new[] { row }).ToArray();
                    p.nextTerrain++;
                }
                finally { if (rebuilt != null && rebuilt != original && rebuilt != mesh) Object.DestroyImmediate(rebuilt); }
            }
            if (p.nextTerrain == p.terrain.Length) p.phase = "GRADED_TERRAIN_READY_FOR_PHYSICAL_AUDIT";
            Physics.SyncTransforms(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            return GradeReceipt(p);
        }
    }
}
