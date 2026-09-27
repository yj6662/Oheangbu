using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        [Serializable] sealed class CorridorCandidate
        { public bool readyForSculpt; public CorridorPatch[] patches; }
        [Serializable] sealed class CorridorPatch
        {
            public string id; public bool carriage; public float width;
            public int[] oldIndexRange, newIndexRange;
            public Vector3[] oldMappedOriginalPoints, newPoints, points;
            public double[] oldOriginalSourceIndex;
            public float[] oldPointToNewNormalizedStation, newNormalizedStation;
        }
        [Serializable] sealed class CorridorHash { public string path, before, after; }
        [Serializable] sealed class CorridorBaselineInput { public string path, hash; public bool original; }
        [Serializable] sealed class CorridorBaseline { public string mappingHash; public CorridorBaselineInput[] inputs; }
        [Serializable] sealed class CorridorRoadReceipt
        {
            public string id, path, source, target, sourceGuid, sourceHash, verticesHash;
            public long sourceFileId;
            public int vertices, triangles, rows, remappedRows, protectedVertices, mapPoints;
            public float maxSourceProjectionXZ, maxSourceProjectionY, maxMappedProjectionXZ, maxMappedProjectionY;
            public float maxWidthError, maxProtectedVertexError, maxProfilePointError, maxMapPointError;
            public bool repeatedInputs, samePlannedVertices;
        }
        [Serializable] sealed class CorridorReceipt
        {
            public string status, utc, candidateHash, fitHash, mappingHash, gradeHash, sourceSceneHash, transformSignature;
            public string immutableSnapshotPath, immutableSnapshotHash, snapshotBaselineHash;
            public string scope = "Source-reconstructed authored road ribbon and matching private map vector lines only. Original widths, IDs, UVs, colours, topology and protected F(vertex) positions retained. Run grade-attachments for actual terrain Y, then physical road audit; no traversal or visual approval.";
            public bool sourceSceneUnchanged, originalAssetsUnchanged, mapTexturesUnchanged, privateTargetsOnly;
            public int patches, roadMeshes, mapLines, mapTextures;
            public CorridorRoadReceipt[] roads;
            public CorridorHash[] originalInputs, unchangedTextures;
            public string[] remaining = {
                "Run grade-attachments after final terrain sculpt; this step places planned Y only.",
                "Rebuild compact terrain _GroundPathMask from the updated Geo routes using materials; old mask contains the former bend.",
                "BaseMap and MiniTerrain rasters encode relief, not authored roads. IllustratedMap is a quiet backdrop with authoritative live road vectors. No map image warp is required for this route change; current terrain relief remains illustrative.",
                "Prior map_alignment_report.json exact-F route checks predate this local route exception; this receipt validates affected vector lines against current Geo/profile.",
                "Actual support, width, collision, navigation and runtime remain separate checks."
            };
        }
        sealed class CorridorRoadPlan
        { public Mesh source, target; public MeshCollider collider; public Transform transform; public Vector3[] vertices; public CorridorRoadReceipt receipt; }
        sealed class CorridorMapPlan { public WorldMapBakedDataSO target; public WorldMapLineSpec line; public Vector2[] points; }
        struct CorridorProjection { public int segment; public double t, score; public float xz, dy; }
        static string CorridorReceiptPath => Path.Combine(Output, "corridor_attachments.json");
        static Vector2 CorridorXZ(Vector3 p) => new Vector2(p.x, p.z);
        static bool CorridorFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static bool CorridorFinite(Vector3 p) => CorridorFinite(p.x) && CorridorFinite(p.y) && CorridorFinite(p.z);
        static void CorridorGuard()
        { if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%; corridor work stopped before further mutation."); }
        static void CorridorPrivate(string path)
        { if (string.IsNullOrEmpty(path) || !path.StartsWith(Folder + "/", StringComparison.Ordinal)) throw new InvalidOperationException("Corridor destination is not private: " + path); }
        static string CorridorSave(CorridorReceipt report)
        {
            string json = JsonUtility.ToJson(report, true), temporary = CorridorReceiptPath + ".tmp";
            File.WriteAllText(temporary, json);
            if (File.Exists(CorridorReceiptPath)) File.Replace(temporary, CorridorReceiptPath, null); else File.Move(temporary, CorridorReceiptPath);
            return json;
        }
        static string CorridorVertexHash(Vector3[] values)
        {
            using var stream = new MemoryStream(values.Length * 12);
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                foreach (var p in values) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
            using var sha = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
        }
        static string CorridorTextHash(string value)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        static void CorridorStations(float[] values, int count, bool normalized, string label)
        {
            if (values == null || values.Length != count) throw new InvalidOperationException("Missing corridor correspondence: " + label);
            for (int i = 0; i < count; i++) if (!CorridorFinite(values[i]) || (i > 0 && values[i] <= values[i - 1]))
                throw new InvalidOperationException("Corridor station must be finite and strictly increasing: " + label);
            if (normalized && (Mathf.Abs(values[0]) > .0001f || Mathf.Abs(values[count - 1] - 1) > .0001f))
                throw new InvalidOperationException("Corridor local station must span [0,1]: " + label);
        }
        static void CorridorStations(double[] values, int count, bool normalized, string label)
        {
            if (values == null || values.Length != count) throw new InvalidOperationException("Missing corridor correspondence: " + label);
            for (int i = 0; i < count; i++) if (double.IsNaN(values[i]) || double.IsInfinity(values[i]) || (i > 0 && values[i] <= values[i - 1]))
                throw new InvalidOperationException("Corridor station must be finite and strictly increasing: " + label);
            if (normalized && (Math.Abs(values[0]) > .0001 || Math.Abs(values[count - 1] - 1) > .0001))
                throw new InvalidOperationException("Corridor local station must span [0,1]: " + label);
        }
        static Vector3 CorridorSourceLerp(Vector3 a, Vector3 b, double t) => new Vector3(
            (float)(a.x + ((double)b.x - a.x) * t), (float)(a.y + ((double)b.y - a.y) * t), (float)(a.z + ((double)b.z - a.z) * t));
        static CorridorProjection CorridorProject(Vector3 point, Vector3[] line, double[] sourceParameters = null, double sourceParameter = 0)
        {
            var best = new CorridorProjection { segment = -1, score = double.PositiveInfinity };
            for (int i = 0; i < line.Length - 1; i++)
            {
                // Authored source segment correspondence excludes the neighbouring hairpin even
                // where compressed XZ overlaps. Original Y remains a second independent discriminator.
                if (sourceParameters != null && (sourceParameter < sourceParameters[i] - .0005 || sourceParameter > sourceParameters[i + 1] + .0005)) continue;
                double dx = (double)line[i + 1].x - line[i].x, dz = (double)line[i + 1].z - line[i].z;
                double length = dx * dx + dz * dz;
                if (length <= 1e-10) continue;
                double t = Math.Max(0, Math.Min(1, (((double)point.x - line[i].x) * dx + ((double)point.z - line[i].z) * dz) / length));
                double qx = line[i].x + dx * t, qz = line[i].z + dz * t;
                double qy = line[i].y + ((double)line[i + 1].y - line[i].y) * t;
                double ex = point.x - qx, ez = point.z - qz, dy = Math.Abs(point.y - qy);
                double xz = Math.Sqrt(ex * ex + ez * ez), score = xz * xz + .01 * dy * dy;
                if (score < best.score) best = new CorridorProjection { segment = i, t = t, xz = (float)xz, dy = (float)dy, score = score };
            }
            if (best.segment < 0) throw new InvalidOperationException("Cannot project original corridor row with valid source correspondence.");
            return best;
        }
        static Vector3 CorridorAt(CorridorPatch patch, float station, out Vector3 tangent)
        {
            int i = Array.BinarySearch(patch.newNormalizedStation, Mathf.Clamp01(station));
            if (i < 0) i = ~i - 1; i = Mathf.Clamp(i, 0, patch.newPoints.Length - 2);
            float t = Mathf.InverseLerp(patch.newNormalizedStation[i], patch.newNormalizedStation[i + 1], station);
            var a = patch.newPoints[i]; var b = patch.newPoints[i + 1];
            tangent = Vector3.ProjectOnPlane(b - a, Vector3.up).normalized;
            if (tangent.sqrMagnitude < .99f) throw new InvalidOperationException("Zero-length corridor tangent: " + patch.id);
            return Vector3.Lerp(a, b, t);
        }
        static float CorridorValidatePatch(CorridorPatch p, WorldMacroSheetSO source, WorldMacroSheetSO geo, WorldMacroRoadGradeSO profile)
        {
            if (p == null || string.IsNullOrEmpty(p.id) || p.oldIndexRange?.Length != 2 || p.newIndexRange?.Length != 2 ||
                p.oldMappedOriginalPoints == null || p.newPoints == null || p.points == null || p.oldMappedOriginalPoints.Length < 2 || p.newPoints.Length < 2 ||
                p.oldIndexRange[0] < 0 || p.newIndexRange[0] < 0 || p.oldIndexRange[1] - p.oldIndexRange[0] + 1 != p.oldMappedOriginalPoints.Length ||
                p.newIndexRange[1] - p.newIndexRange[0] + 1 != p.newPoints.Length || p.newIndexRange[1] >= p.points.Length)
                throw new InvalidOperationException("Corridor patch/range schema is incomplete.");
            CorridorStations(p.oldOriginalSourceIndex, p.oldMappedOriginalPoints.Length, false, p.id + "/source indices");
            CorridorStations(p.oldPointToNewNormalizedStation, p.oldMappedOriginalPoints.Length, true, p.id + "/old-to-new");
            CorridorStations(p.newNormalizedStation, p.newPoints.Length, true, p.id + "/new stations");
            var original = source.Routes.Single(r => r.Id == p.id);
            var route = geo.Routes.Single(r => r.Id == p.id);
            var gradeLine = profile.Lines.Single(r => r.Id == p.id);
            if (route.Carriage != original.Carriage || route.Carriage != p.carriage || Mathf.Abs(route.Width - original.Width) > .0001f ||
                Mathf.Abs(route.Width - p.width) > .0001f || Mathf.Abs(gradeLine.Width - p.width) > .0001f ||
                route.Points.Length != p.points.Length || gradeLine.Points.Length != p.points.Length)
                throw new InvalidOperationException("Grade-reset must install this complete route without changing its width/kind: " + p.id);
            float maxError = 0;
            for (int i = 0; i < p.points.Length; i++)
            {
                if (!CorridorFinite(p.points[i])) throw new InvalidOperationException("Nonfinite candidate point.");
                maxError = Mathf.Max(maxError, Vector3.Distance(p.points[i], route.Points[i]), Vector3.Distance(p.points[i], gradeLine.Points[i]));
            }
            for (int i = 0; i < p.newPoints.Length; i++) maxError = Mathf.Max(maxError, Vector3.Distance(p.newPoints[i], route.Points[p.newIndexRange[0] + i]));
            if (maxError > .015f) throw new InvalidOperationException("Current Geo/profile does not match ready corridor candidate: " + p.id + " error=" + maxError);
            for (int i = 0; i < p.oldMappedOriginalPoints.Length; i++)
            {
                double parameter = p.oldOriginalSourceIndex[i];
                if (parameter < 0 || parameter > original.Points.Length - 1) throw new InvalidOperationException("Original corridor source index is out of range.");
                int index = Math.Min((int)Math.Floor(parameter), original.Points.Length - 2);
                var expected = Compression.Map(CorridorSourceLerp(original.Points[index], original.Points[index + 1], parameter - index));
                if (!CorridorFinite(p.oldMappedOriginalPoints[i]) || Vector3.Distance(expected, p.oldMappedOriginalPoints[i]) > .03f)
                    throw new InvalidOperationException("Immutable original route does not match corridor correspondence: " + p.id + "/" + i);
                if (profile.IsProtected(expected.x, expected.z))
                {
                    var moved = CorridorAt(p, p.oldPointToNewNormalizedStation[i], out _);
                    if (Vector3.Distance(expected, moved) > .03f) throw new InvalidOperationException("Corridor candidate moves a protected original route point: " + p.id);
                }
            }
            return maxError;
        }

        public static string ApplyCorridorAttachments()
        {
            RequireCompact(); CorridorGuard(); var scene = SceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("Save compact scene before applying corridor attachments.");
            var progress = ReadProgress();
            if (Hash(SourceScene) != progress.sourceHash) throw new InvalidOperationException("Original source scene changed.");
            string candidatePath = Path.Combine(Output, "route_corridor_local_candidate.json"), fitPath = Path.Combine(Output, "route_grade_fit.json");
            var candidate = JsonUtility.FromJson<CorridorCandidate>(File.ReadAllText(candidatePath));
            if (candidate == null || !candidate.readyForSculpt || candidate.patches == null || candidate.patches.Length == 0 ||
                candidate.patches.Select(p => p.id).Distinct(StringComparer.Ordinal).Count() != candidate.patches.Length)
                throw new InvalidOperationException("Corridor candidate is not ready or repeats a route.");
            var profile = Grade; if (profile == null) throw new InvalidOperationException("Run grade-reset with final corridor fit first."); profile.Validate();
            var gradeProgress = JsonUtility.FromJson<GradeProgress>(File.ReadAllText(GradeProgressPath));
            if (gradeProgress.fitHash != Hash(fitPath) || gradeProgress.profileVersion != WorldMacroRoadGradeSO.CurrentVersion)
                throw new InvalidOperationException("Grade progress does not match the current full fit.");
            var geoPair = progress.assets.Single(a => AssetDatabase.LoadMainAssetAtPath(a.target) is WorldMacroSheetSO);
            var geo = AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(geoPair.target);
            var sourceGeo = AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(geoPair.source);
            CorridorPrivate(geoPair.target); CorridorPrivate(AssetDatabase.GetAssetPath(profile));
            string snapshotPath = null, snapshotHash = null, snapshotBaselineHash = null;
            if (geo.CompressionSource != sourceGeo)
            {
                // Terrain evaluates the frozen source snapshot; ribbon correspondence still
                // uses the original authored sourceGeo routes below.
                snapshotPath = Folder + "/SourceGeographySnapshot.asset";
                string baselinePath = Path.Combine(Output, "road_physics_v2_baseline.json");
                if (geo.CompressionSource == null || AssetDatabase.GetAssetPath(geo.CompressionSource) != snapshotPath ||
                    AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(snapshotPath) != geo.CompressionSource || !File.Exists(baselinePath))
                    throw new InvalidOperationException("Private geography must reference the original sheet or the recorded immutable source snapshot.");
                var baseline = JsonUtility.FromJson<CorridorBaseline>(File.ReadAllText(baselinePath));
                var entries = baseline?.inputs?.Where(i => i.path == snapshotPath).ToArray();
                if (baseline == null || baseline.mappingHash != Hash(Folder + "/Compression.asset") || entries == null || entries.Length != 1 ||
                    entries[0].original || string.IsNullOrEmpty(entries[0].hash) || entries[0].hash.Length != 64 ||
                    !string.Equals(Hash(snapshotPath), entries[0].hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Immutable source snapshot hash does not match road_physics_v2_baseline.json.");
                snapshotHash = entries[0].hash; snapshotBaselineHash = Hash(baselinePath);
            }
            if (geo.Compression != Compression || geo.CompactRoadGrade != profile)
                throw new InvalidOperationException("Private geography compression/grade references are invalid.");
            var mapPairs = progress.assets.Where(a => AssetDatabase.LoadMainAssetAtPath(a.target) is WorldMapBakedDataSO).ToArray();
            if (mapPairs.Length != 1) throw new InvalidOperationException("Expected one private runtime map asset.");
            var report = new CorridorReceipt { status = "PREPARING", utc = DateTime.UtcNow.ToString("o"), candidateHash = Hash(candidatePath), fitHash = Hash(fitPath),
                mappingHash = Hash(Folder + "/Compression.asset"), gradeHash = Hash(AssetDatabase.GetAssetPath(profile)), sourceSceneHash = progress.sourceHash,
                patches = candidate.patches.Length, privateTargetsOnly = true,
                immutableSnapshotPath = snapshotPath, immutableSnapshotHash = snapshotHash, snapshotBaselineHash = snapshotBaselineHash };
            var originalPaths = new HashSet<string>(progress.assets.Select(a => a.source), StringComparer.Ordinal) { SourceScene };
            if (snapshotPath != null) originalPaths.Add(snapshotPath);
            foreach (var asset in progress.assets) if (Hash(asset.source) != asset.hash) throw new InvalidOperationException("Original asset hash differs from source manifest: " + asset.source);
            var texturePaths = new HashSet<string>(StringComparer.Ordinal);
            var roadPlans = new List<CorridorRoadPlan>(); var mapPlans = new List<CorridorMapPlan>(); var rows = new List<CorridorRoadReceipt>();
            foreach (var patch in candidate.patches)
            {
                CorridorGuard(); float profileError = CorridorValidatePatch(patch, sourceGeo, geo, profile);
                var row = new CorridorRoadReceipt { id = patch.id, maxProfilePointError = profileError }; rows.Add(row);
                foreach (var pair in mapPairs)
                {
                    CorridorPrivate(pair.target); var map = AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(pair.target); var originalMap = AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(pair.source);
                    var line = map.Lines.Single(l => l.Id == patch.id); var oldLine = originalMap.Lines.Single(l => l.Id == patch.id);
                    if (line.Kind != oldLine.Kind || line.PixelWidth != oldLine.PixelWidth || line.Kind != (patch.carriage ? WorldMapLineKind.Road : WorldMapLineKind.Trail))
                        throw new InvalidOperationException("Map route kind/pixel width changed: " + patch.id);
                    var points = geo.Routes.Single(r => r.Id == patch.id).Points.Select(CorridorXZ).ToArray();
                    mapPlans.Add(new CorridorMapPlan { target = map, line = line, points = points }); row.mapPoints += points.Length;
                    foreach (var texture in new[] { map.BaseMap, map.IllustratedMap }.Concat((map.RegionTiles ?? Array.Empty<WorldMapRegionTile>()).Select(t => t.Texture)).Where(t => t != null))
                        texturePaths.Add(AssetDatabase.GetAssetPath(texture));
                }
                var jobs = progress.meshes.Where(j => j.road && j.path == "Playtest_VisualCorridor/01_TerrainConformedRoutes/" + patch.id).ToArray();
                if (jobs.Length > 1) throw new InvalidOperationException("Ambiguous authored corridor ribbon: " + patch.id);
                if (jobs.Length == 0) continue; // Preserve the existing authored ribbon inventory; no synthetic return ribbon.
                var job = jobs[0]; var t = Find(job.path); if (t == null) throw new InvalidOperationException("Authored ribbon transform missing.");
                var filter = t.GetComponent<MeshFilter>(); var collider = t.GetComponent<MeshCollider>(); var sourceMesh = AssetDatabase.LoadAssetAtPath<Mesh>(job.source);
                if (sourceMesh == null || filter == null || filter.sharedMesh == null || AssetDatabase.GetAssetPath(filter.sharedMesh) != job.result ||
                    sourceMesh == filter.sharedMesh || job.source.StartsWith(Folder + "/", StringComparison.Ordinal) || (collider != null && collider.sharedMesh != filter.sharedMesh))
                    throw new InvalidOperationException("Authored ribbon source/private renderer/collider mesh binding is invalid.");
                CorridorPrivate(job.result); originalPaths.Add(job.source);
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sourceMesh, out row.sourceGuid, out row.sourceFileId)) throw new InvalidOperationException("Original ribbon mesh identity unavailable.");
                var src = sourceMesh.vertices; if (src.Length % 4 != 0 || src.Length != filter.sharedMesh.vertexCount) throw new InvalidOperationException("Authored ribbon must retain its original four-vertex rows.");
                row.path = job.path; row.source = job.source; row.target = job.result; row.sourceHash = Hash(job.source); row.vertices = src.Length; row.rows = src.Length / 4;
                row.triangles = sourceMesh.triangles.Length / 3; var vertices = new Vector3[src.Length]; var originalRoute = sourceGeo.Routes.Single(r => r.Id == patch.id).Points;
                for (int i = 0; i < src.Length; i += 4)
                {
                    var a = job.originalMatrix.MultiplyPoint3x4(src[i]); var b = job.originalMatrix.MultiplyPoint3x4(src[i + 3]);
                    var sourceCenter = (a + b) * .5f; var mappedCenter = Compression.Map(sourceCenter);
                    var originalAcross = Vector3.ProjectOnPlane(b - a, Vector3.up).normalized;
                    var across = Vector3.ProjectOnPlane(Compression.Map(b) - Compression.Map(a), Vector3.up).normalized;
                    if (originalAcross.sqrMagnitude < .99f || across.sqrMagnitude < .99f) throw new InvalidOperationException("Collapsed source/compact ribbon cross section.");
                    var originalProjection = CorridorProject(sourceCenter, originalRoute);
                    row.maxSourceProjectionXZ = Mathf.Max(row.maxSourceProjectionXZ, originalProjection.xz); row.maxSourceProjectionY = Mathf.Max(row.maxSourceProjectionY, originalProjection.dy);
                    if (originalProjection.xz > .05f || originalProjection.dy > 6) throw new InvalidOperationException("Original ribbon row cannot be assigned reliably to its authored route: " + patch.id + "/" + (i / 4));
                    double sourceParameter = originalProjection.segment + originalProjection.t; bool remapped = sourceParameter >= patch.oldOriginalSourceIndex[0] - .0001 && sourceParameter <= patch.oldOriginalSourceIndex.Last() + .0001;
                    if (remapped)
                    {
                        var projected = CorridorProject(mappedCenter, patch.oldMappedOriginalPoints, patch.oldOriginalSourceIndex, sourceParameter);
                        row.maxMappedProjectionXZ = Mathf.Max(row.maxMappedProjectionXZ, projected.xz); row.maxMappedProjectionY = Mathf.Max(row.maxMappedProjectionY, projected.dy);
                        if (projected.xz > 1.1f || projected.dy > 6) throw new InvalidOperationException("Ambiguous compressed source ribbon projection: " + patch.id + "/" + (i / 4));
                        double stationA = patch.oldPointToNewNormalizedStation[projected.segment], stationB = patch.oldPointToNewNormalizedStation[projected.segment + 1];
                        float station = (float)(stationA + (stationB - stationA) * projected.t);
                        mappedCenter = CorridorAt(patch, station, out var tangent); across = Vector3.Cross(Vector3.up, tangent).normalized;
                        var sourceTangent = Vector3.ProjectOnPlane(originalRoute[originalProjection.segment + 1] - originalRoute[originalProjection.segment], Vector3.up).normalized;
                        if (Vector3.Dot(originalAcross, Vector3.Cross(Vector3.up, sourceTangent)) < 0) across = -across;
                        row.remappedRows++;
                    }
                    bool protectedRow = false;
                    for (int j = 0; j < 4; j++)
                    {
                        var originalWorld = job.originalMatrix.MultiplyPoint3x4(src[i + j]); var exact = Compression.Map(originalWorld);
                        float signedWidth = Vector3.Dot(originalWorld - sourceCenter, originalAcross);
                        var final = mappedCenter + across * signedWidth; final.y = remapped ? mappedCenter.y + originalWorld.y - sourceCenter.y : exact.y;
                        if (profile.IsProtected(exact.x, exact.z)) { final = exact; row.protectedVertices++; protectedRow = true; }
                        if (!CorridorFinite(final)) throw new InvalidOperationException("Nonfinite remapped ribbon vertex.");
                        vertices[i + j] = t.InverseTransformPoint(final);
                        if (profile.IsProtected(exact.x, exact.z)) row.maxProtectedVertexError = Mathf.Max(row.maxProtectedVertexError, Vector3.Distance(t.TransformPoint(vertices[i + j]), exact));
                    }
                    if (!protectedRow) row.maxWidthError = Mathf.Max(row.maxWidthError, Mathf.Abs(Vector2.Distance(CorridorXZ(t.TransformPoint(vertices[i])), CorridorXZ(t.TransformPoint(vertices[i + 3]))) - Vector2.Distance(CorridorXZ(a), CorridorXZ(b))));
                }
                if (row.maxWidthError > .003f || row.maxProtectedVertexError > .003f) throw new InvalidOperationException("Corridor width/protected vertex preservation failed.");
                row.verticesHash = CorridorVertexHash(vertices); roadPlans.Add(new CorridorRoadPlan { source = sourceMesh, target = filter.sharedMesh, collider = collider, transform = t, vertices = vertices, receipt = row });
            }
            report.transformSignature = CorridorTextHash(string.Join("\n", roadPlans.Select(p => p.receipt.path + "|" + p.transform.localToWorldMatrix.ToString("R"))));
            var previous = File.Exists(CorridorReceiptPath) ? JsonUtility.FromJson<CorridorReceipt>(File.ReadAllText(CorridorReceiptPath)) : null;
            if (previous != null && previous.candidateHash == report.candidateHash && previous.gradeHash == report.gradeHash && previous.mappingHash == report.mappingHash && previous.transformSignature == report.transformSignature)
                foreach (var plan in roadPlans)
                {
                    var old = previous.roads?.SingleOrDefault(r => r.path == plan.receipt.path);
                    if (old == null || old.sourceHash != plan.receipt.sourceHash) continue;
                    plan.receipt.repeatedInputs = true; plan.receipt.samePlannedVertices = old.verticesHash == plan.receipt.verticesHash;
                    if (!plan.receipt.samePlannedVertices) throw new InvalidOperationException("Identical corridor source/input produced different planned vertices.");
                }
            report.roads = rows.ToArray(); report.roadMeshes = roadPlans.Count; report.mapLines = mapPlans.Count; report.mapTextures = texturePaths.Count;
            report.originalInputs = originalPaths.OrderBy(p => p, StringComparer.Ordinal).Select(p => new CorridorHash { path = p, before = Hash(p) }).ToArray();
            report.unchangedTextures = texturePaths.OrderBy(p => p, StringComparer.Ordinal).Select(p => new CorridorHash { path = p, before = Hash(p) }).ToArray();
            CorridorGuard();
            if (Hash(candidatePath) != report.candidateHash || Hash(fitPath) != report.fitHash || Hash(AssetDatabase.GetAssetPath(profile)) != report.gradeHash)
                throw new InvalidOperationException("Corridor/grade input changed during preflight.");
            report.status = "APPLYING"; CorridorSave(report);
            foreach (var plan in roadPlans)
            {
                // Reset every channel from the immutable source. Existing private asset GUID and
                // original UV/colour/topology remain stable; no prior sculpted vertices are inputs.
                string name = plan.target.name; EditorUtility.CopySerialized(plan.source, plan.target); plan.target.name = name;
                plan.target.vertices = plan.vertices; plan.target.RecalculateNormals(); plan.target.RecalculateBounds();
                EditorUtility.SetDirty(plan.target); AssetDatabase.SaveAssetIfDirty(plan.target);
                if (plan.collider != null) { plan.collider.sharedMesh = null; plan.collider.sharedMesh = plan.target; EditorUtility.SetDirty(plan.collider); }
                if (CorridorVertexHash(plan.target.vertices) != plan.receipt.verticesHash) throw new InvalidOperationException("Saved corridor vertices differ from the preflight plan.");
            }
            foreach (var plan in mapPlans)
            {
                plan.line.Points = plan.points; EditorUtility.SetDirty(plan.target);
                var route = geo.Routes.Single(r => r.Id == plan.line.Id);
                float error = 0; for (int i = 0; i < plan.points.Length; i++) error = Mathf.Max(error, Vector2.Distance(plan.line.Points[i], CorridorXZ(route.Points[i])));
                rows.Single(r => r.id == plan.line.Id).maxMapPointError = error;
            }
            foreach (var map in mapPlans.Select(p => p.target).Distinct()) AssetDatabase.SaveAssetIfDirty(map);
            if (roadPlans.Any(p => p.collider != null)) { Physics.SyncTransforms(); EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save compact ribbon collider bindings."); }
            foreach (var input in report.originalInputs) input.after = Hash(input.path);
            foreach (var input in report.unchangedTextures) input.after = Hash(input.path);
            report.sourceSceneUnchanged = Hash(SourceScene) == report.sourceSceneHash;
            report.originalAssetsUnchanged = report.originalInputs.All(p => p.before == p.after);
            report.mapTexturesUnchanged = report.unchangedTextures.All(p => p.before == p.after);
            report.status = report.sourceSceneUnchanged && report.originalAssetsUnchanged && report.mapTexturesUnchanged ? "APPLIED_REQUIRES_GROUNDING_AND_PHYSICS" : "FINDINGS_SOURCE_OR_TEXTURE_HASH_CHANGED";
            return CorridorSave(report);
        }
    }
}
