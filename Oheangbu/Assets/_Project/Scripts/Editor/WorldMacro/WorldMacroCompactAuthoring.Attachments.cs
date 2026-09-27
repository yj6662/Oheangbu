using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        [Serializable] sealed class AttachmentMatrix { public float[] values; }
        [Serializable] sealed class AttachmentPart
        {
            public string meshGuid, materialGuid;
            public long meshFileId, materialFileId;
            public int submesh;
            public AttachmentMatrix[] matrices;
        }
        [Serializable] sealed class AttachmentPacket
        {
            public Vector3 center, extents;
            public float distance;
            public int count;
            public AttachmentPart[] near, far;
        }
        [Serializable] sealed class AttachmentComponent
        { public string componentId, path; public AttachmentPacket[] packets; }
        [Serializable] sealed class AttachmentSnapshot
        { public string sourceSceneHash; public int componentCount, packetCount, matrixCount; public AttachmentComponent[] components; }
        [Serializable] sealed class AttachmentHash { public string path, before, after; }
        [Serializable] sealed class AttachmentReport
        {
            public string status, sourceSceneHash, snapshotHash, mappingHash, gradeHash, reliefHash, terrainSignature, utc;
            public string scope = "Source-restored attachment positions: original -> compression -> current grounding exactly once. Road ribbons retain compact XZ/full width, probe saved Terrain_ and explicit bridge decks/aprons only; protected vertices keep original Y. No actor/gameplay/nav changes or native traversal verification.";
            public bool sourceSceneUnchanged, originalAssetsUnchanged, matrixBasesPreserved, propBasesPreserved, ribbonXZPreserved;
            public int foliageComponents, packets, matrixRecords, fixedPlacements, environmentRoots, movedEnvironmentRoots;
            public int roadMeshes, roadVertices, protectedRoadVertices, terrainRoadVertices, bridgeRoadVertices, missingRoadSupport;
            public float maximumGroundDelta, maximumRibbonYChange;
            public AttachmentHash[] originalAssets;
            public string[] findings;
        }
        sealed class AttachmentFoliagePlan { public EarlyRegionFoliage target; public EarlyRegionFoliage.Packet[] packets; }
        sealed class AttachmentPropPlan { public Transform target; public float y; }
        sealed class AttachmentFixedPlan { public WorldMacroDressingSheetSO target; public Vector3[] positions; }
        sealed class AttachmentRoadPlan { public Mesh mesh; public MeshCollider collider; public Vector3[] vertices; }
        sealed class AttachmentSupport { public Collider collider; public Bounds bounds; public bool bridge; public string path; }
        static long AttachmentCell(int x, int z) => ((long)x << 32) ^ (uint)z;
        static long AttachmentCell(Vector3 p) => AttachmentCell(Mathf.FloorToInt(p.x / 64), Mathf.FloorToInt(p.z / 64));
        static string AttachmentReportPath => Path.Combine(Output, "final_grade_attachments.json");
        static void AttachmentGuard()
        { if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%; attachment work stopped before further mutations."); }
        static string SaveAttachmentReport(AttachmentReport report)
        {
            string json = JsonUtility.ToJson(report, true), temporary = AttachmentReportPath + ".tmp";
            File.WriteAllText(temporary, json); if (File.Exists(AttachmentReportPath)) File.Replace(temporary, AttachmentReportPath, null); else File.Move(temporary, AttachmentReportPath); return json;
        }
        static Matrix4x4 AttachmentReadMatrix(AttachmentMatrix value)
        {
            if (value?.values == null || value.values.Length != 16) throw new InvalidOperationException("Original foliage matrix must contain exactly 16 values.");
            var matrix = new Matrix4x4();
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++)
            { float v = value.values[r * 4 + c]; if (float.IsNaN(v) || float.IsInfinity(v)) throw new InvalidOperationException("Nonfinite original foliage matrix."); matrix[r, c] = v; }
            if (Mathf.Abs(matrix.m30) > .00001f || Mathf.Abs(matrix.m31) > .00001f || Mathf.Abs(matrix.m32) > .00001f || Mathf.Abs(matrix.m33 - 1) > .00001f) throw new InvalidOperationException("Original foliage matrix is not affine.");
            return matrix;
        }
        static Bounds AttachmentMatrixBounds(Matrix4x4 matrix, Bounds local)
        {
            var e = local.extents;
            var extent = new Vector3(Mathf.Abs(matrix.m00) * e.x + Mathf.Abs(matrix.m01) * e.y + Mathf.Abs(matrix.m02) * e.z,
                Mathf.Abs(matrix.m10) * e.x + Mathf.Abs(matrix.m11) * e.y + Mathf.Abs(matrix.m12) * e.z,
                Mathf.Abs(matrix.m20) * e.x + Mathf.Abs(matrix.m21) * e.y + Mathf.Abs(matrix.m22) * e.z);
            return new Bounds(matrix.MultiplyPoint3x4(local.center), extent * 2);
        }
        static EarlyRegionFoliage.Part[] AttachmentParts(AttachmentPart[] source, EarlyRegionFoliage.Part[] current, Func<Vector3, Vector3> place,
            AttachmentReport report, ref bool hasBounds, ref Bounds originalBounds, ref Bounds finalBounds)
        {
            if (source == null || current == null || source.Length != current.Length) throw new InvalidOperationException("Foliage part inventory changed.");
            var result = new EarlyRegionFoliage.Part[source.Length];
            for (int p = 0; p < source.Length; p++)
            {
                var old = source[p]; var part = current[p];
                if (part == null || part.Mesh == null || part.Matrices == null || part.Matrices.Length != old.matrices.Length || part.Submesh != old.submesh ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(part.Mesh, out string guid, out long fileId) || guid != old.meshGuid || fileId != old.meshFileId)
                    throw new InvalidOperationException("Original foliage mesh/submesh/matrix inventory changed.");
                var matrices = new Matrix4x4[old.matrices.Length];
                for (int i = 0; i < matrices.Length; i++)
                {
                    var matrix = AttachmentReadMatrix(old.matrices[i]);
                    for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++)
                        if (!(c == 3 && r < 3) && Mathf.Abs(matrix[r, c] - part.Matrices[i][r, c]) > .00001f) throw new InvalidOperationException("Unrelated foliage basis change; only translation may be restored.");
                    var oldBounds = AttachmentMatrixBounds(matrix, part.Mesh.bounds); var original = new Vector3(matrix.m03, matrix.m13, matrix.m23); var final = place(original);
                    matrix.m03 = final.x; matrix.m13 = final.y; matrix.m23 = final.z; matrices[i] = matrix;
                    var newBounds = AttachmentMatrixBounds(matrix, part.Mesh.bounds);
                    if (!hasBounds) { originalBounds = oldBounds; finalBounds = newBounds; hasBounds = true; }
                    else { originalBounds.Encapsulate(oldBounds); finalBounds.Encapsulate(newBounds); }
                    report.matrixRecords++;
                }
                result[p] = new EarlyRegionFoliage.Part { Mesh = part.Mesh, Material = part.Material, Submesh = part.Submesh, Matrices = matrices };
            }
            return result;
        }
        static Dictionary<long, List<AttachmentSupport>> AttachmentSupports(out string signature)
        {
            Physics.SyncTransforms(); var supports = new List<AttachmentSupport>();
            foreach (var collider in SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>(true)))
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger || collider.attachedRigidbody != null) continue;
                string path = Hierarchy(collider.transform);
                bool terrain = collider is MeshCollider && collider.name.StartsWith("Terrain_", StringComparison.Ordinal) && path.StartsWith("WorldMacro_AuthoredGeography/01_GlobalTerrain_IndependentOfRoads/", StringComparison.Ordinal);
                bool bridge = path.StartsWith("WorldMacro_AuthoredGeography/03_SettlementAndLandmark_Massing/", StringComparison.Ordinal) && (collider.name == "Bridge_Deck_TEST" || collider.name.StartsWith("Bridge_Apron_TEST_", StringComparison.Ordinal));
                if (!terrain && !bridge) continue;
                if (collider is MeshCollider mc && (mc.sharedMesh == null || EditorUtility.IsDirty(mc.sharedMesh))) throw new InvalidOperationException("Save final support mesh before attachment finalization: " + path);
                supports.Add(new AttachmentSupport { collider = collider, bounds = collider.bounds, bridge = bridge, path = path });
            }
            if (supports.All(s => s.bridge)) throw new InvalidOperationException("No actual compact Terrain_ support colliders found.");
            var index = new Dictionary<long, List<AttachmentSupport>>(); var rows = new List<string>();
            foreach (var support in supports.OrderBy(s => s.path, StringComparer.Ordinal))
            {
                rows.Add(support.path + "|" + support.bounds.ToString("R") + "|" + support.collider.transform.localToWorldMatrix.ToString("R") + "|" + (support.collider is MeshCollider mc ? AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(mc.sharedMesh)).ToString() : "box"));
                for (int z = Mathf.FloorToInt(support.bounds.min.z / 64); z <= Mathf.FloorToInt(support.bounds.max.z / 64); z++)
                    for (int x = Mathf.FloorToInt(support.bounds.min.x / 64); x <= Mathf.FloorToInt(support.bounds.max.x / 64); x++)
                    { long key = AttachmentCell(x, z); if (!index.TryGetValue(key, out var list)) { list = new List<AttachmentSupport>(); index.Add(key, list); } list.Add(support); }
            }
            using (var sha = System.Security.Cryptography.SHA256.Create()) signature = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", rows)))).Replace("-", "").ToLowerInvariant();
            return index;
        }
        static bool AttachmentFloor(Dictionary<long, List<AttachmentSupport>> index, Vector3 expected, out float height, out bool bridge)
        {
            height = 0; bridge = false; if (!index.TryGetValue(AttachmentCell(expected), out var supports)) return false;
            float terrainError = float.PositiveInfinity, deckError = float.PositiveInfinity, terrainY = 0, deckY = 0;
            foreach (var support in supports)
            {
                if (expected.x < support.bounds.min.x - .01f || expected.x > support.bounds.max.x + .01f || expected.z < support.bounds.min.z - .01f || expected.z > support.bounds.max.z + .01f) continue;
                var ray = new Ray(new Vector3(expected.x, support.bounds.max.y + 1, expected.z), Vector3.down);
                if (!support.collider.Raycast(ray, out var hit, support.bounds.size.y + 2) || hit.normal.y < .1f) continue;
                float error = Mathf.Abs(hit.point.y - expected.y);
                if (!support.bridge && error < terrainError) { terrainError = error; terrainY = hit.point.y; }
                else if (support.bridge && hit.point.y <= expected.y + .8f && hit.point.y >= expected.y - 4 && error < deckError) { deckError = error; deckY = hit.point.y; }
            }
            if (!float.IsInfinity(deckError) && deckError <= terrainError + .05f) { height = deckY; bridge = true; return true; }
            if (float.IsInfinity(terrainError)) return false; height = terrainY; return true;
        }

        public static string FinalizeGradeAttachments()
        {
            RequireCompact(); AttachmentGuard(); var scene = SceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("Save completed terrain sculpt before finalizing attachments.");
            var progress = ReadProgress(); var profile = Grade;
            if (profile == null || Hash(SourceScene) != progress.sourceHash) throw new InvalidOperationException("Final grade profile/original scene identity is invalid.");
            string snapshotPath = Path.Combine(Output, "original_attachment_snapshot.json");
            var snapshot = JsonUtility.FromJson<AttachmentSnapshot>(File.ReadAllText(snapshotPath));
            if (snapshot.sourceSceneHash != progress.sourceHash || snapshot.components == null || snapshot.componentCount != snapshot.components.Length || snapshot.packetCount != 643 || snapshot.matrixCount != 14926) throw new InvalidOperationException("Exact original foliage snapshot inventory does not match the source scene.");
            var report = new AttachmentReport { status = "PREPARING", sourceSceneHash = progress.sourceHash, snapshotHash = Hash(snapshotPath), mappingHash = Hash(Folder + "/Compression.asset"), gradeHash = Hash(AssetDatabase.GetAssetPath(profile)), reliefHash = profile.Relief == null ? "" : Hash(AssetDatabase.GetAssetPath(profile.Relief)), utc = DateTime.UtcNow.ToString("o") };
            var findings = new List<string>(); var originalPaths = new HashSet<string>(StringComparer.Ordinal);
            var geo = progress.assets.Select(a => AssetDatabase.LoadMainAssetAtPath(a.target)).OfType<WorldMacroSheetSO>().Single();
            if (geo.CompressionSource == null) throw new InvalidOperationException("Original terrain sampler is missing.");
            originalPaths.Add(AssetDatabase.GetAssetPath(geo.CompressionSource));
            foreach (string guid in snapshot.components.SelectMany(c => c.packets).SelectMany(p => p.near.Concat(p.far)).SelectMany(p => new[] { p.meshGuid, p.materialGuid }).Where(g => !string.IsNullOrEmpty(g)).Distinct(StringComparer.Ordinal))
            { string asset = AssetDatabase.GUIDToAssetPath(guid); if (string.IsNullOrEmpty(asset) || !File.Exists(asset)) throw new InvalidOperationException("Original foliage asset is missing: " + guid); originalPaths.Add(asset); }
            Vector3 PlaceOriginal(Vector3 sourcePosition)
            {
                var v = Compression.Map(sourcePosition); var sourceXZ = Compression.Inverse(v); float sourceFloor = WorldMacroTerrain.SurfaceHeight(geo.CompressionSource, sourceXZ.x, sourceXZ.z);
                if (Mathf.Abs(v.y - sourceFloor) <= 8 && !profile.IsProtected(v.x, v.z)) { float delta = profile.Height(v.x, v.z, sourceFloor) - sourceFloor; report.maximumGroundDelta = Mathf.Max(report.maximumGroundDelta, Mathf.Abs(delta)); v.y += delta; }
                return v;
            }
            var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var paths = transforms.GroupBy(Hierarchy).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            var original = JsonUtility.FromJson<CompactOriginalSnapshot>(File.ReadAllText(Path.Combine(Output, "content_audit.json")));
            var survey = JsonUtility.FromJson<CompactOriginalSurvey>(File.ReadAllText(Path.Combine(Output, "scene_survey.json")));
            if (original.sceneSha256 != progress.sourceHash || survey.scene != SourceScene) throw new InvalidOperationException("Original transform snapshots are stale.");
            var surveyPaths = survey.groups.GroupBy(g => g.path).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            var rotations = CompactSourceLocalRotations(); var props = new List<AttachmentPropPlan>();
            foreach (string parent in new[] { "Playtest_VisualCorridor/02_MountainAndForest", "Playtest_EarlyArt" })
            {
                var holder = Find(parent); if (holder == null) throw new InvalidOperationException("Original environment parent missing: " + parent);
                foreach (Transform t in holder)
                {
                    if (t.GetComponent<EarlyRegionFoliage>() != null) continue;
                    string path = Hierarchy(t); var old = original.transforms.Where(v => v.path == path).ToArray();
                    if (old.Length != 1 || !surveyPaths.TryGetValue(path, out var records) || records.Length != 1) throw new InvalidOperationException("Exact source world position is missing/ambiguous: " + path);
                    if (Vector3.Distance(t.localScale, old[0].localScale) > .0001f || !rotations.TryGetValue(old[0].transformId, out var rotation) || CompactRotationError(t.localRotation, rotation) > .00001f) throw new InvalidOperationException("Unrelated environment root basis change: " + path);
                    var final = PlaceOriginal(records[0].position);
                    if (Vector2.Distance(new Vector2(t.position.x, t.position.z), new Vector2(final.x, final.z)) > .025f) throw new InvalidOperationException("Unrelated environment root XZ change: " + path);
                    props.Add(new AttachmentPropPlan { target = t, y = final.y }); report.environmentRoots++; if (Mathf.Abs(final.y - Compression.Map(records[0].position).y) > .00001f) report.movedEnvironmentRoots++;
                }
            }
            var foliagePlans = new List<AttachmentFoliagePlan>();
            var allFoliage = transforms.Select(t => t.GetComponent<EarlyRegionFoliage>()).Where(f => f != null).ToArray();
            if (allFoliage.Length != snapshot.components.Length) throw new InvalidOperationException("Foliage component inventory changed.");
            foreach (var component in snapshot.components)
            {
                var foliage = allFoliage.Single(f => Hierarchy(f.transform) == component.path);
                if (GlobalObjectId.GetGlobalObjectIdSlow(foliage).targetObjectId.ToString(System.Globalization.CultureInfo.InvariantCulture) != component.componentId || foliage.Packets.Length != component.packets.Length) throw new InvalidOperationException("Original foliage component ID/packet count changed.");
                var packets = new EarlyRegionFoliage.Packet[component.packets.Length];
                for (int p = 0; p < packets.Length; p++)
                {
                    var old = component.packets[p]; var current = foliage.Packets[p];
                    if (current == null || current.Count != old.count || current.Distance != old.distance) throw new InvalidOperationException("Foliage packet count/distance changed.");
                    bool hasBounds = false; Bounds oldBounds = default, finalBounds = default;
                    var near = AttachmentParts(old.near, current.Near, PlaceOriginal, report, ref hasBounds, ref oldBounds, ref finalBounds);
                    var far = AttachmentParts(old.far, current.Far, PlaceOriginal, report, ref hasBounds, ref oldBounds, ref finalBounds);
                    var authoredBounds = new Bounds(old.center, old.extents * 2);
                    if (hasBounds) { var lowSlack = Vector3.Max(Vector3.zero, oldBounds.min - authoredBounds.min); var highSlack = Vector3.Max(Vector3.zero, authoredBounds.max - oldBounds.max); finalBounds.SetMinMax(finalBounds.min - lowSlack, finalBounds.max + highSlack); }
                    else finalBounds = new Bounds(PlaceOriginal(old.center), authoredBounds.size);
                    packets[p] = new EarlyRegionFoliage.Packet { Bounds = finalBounds, Near = near, Far = far, Count = old.count, Distance = old.distance }; report.packets++;
                }
                foliagePlans.Add(new AttachmentFoliagePlan { target = foliage, packets = packets }); report.foliageComponents++;
            }
            if (report.matrixRecords != snapshot.matrixCount || report.packets != snapshot.packetCount) throw new InvalidOperationException("Prepared foliage inventory differs from original.");
            var fixedPlans = new List<AttachmentFixedPlan>();
            foreach (var pair in progress.assets)
            {
                var dressing = AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(pair.target); if (dressing == null) continue;
                var source = AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(pair.source);
                if (source == null || Hash(pair.source) != pair.hash || source.FixedPlacements.Length != dressing.FixedPlacements.Length || !pair.target.StartsWith(Folder + "/", StringComparison.Ordinal)) throw new InvalidOperationException("Original/private dressing identity changed.");
                originalPaths.Add(pair.source); var sourceById = source.FixedPlacements.ToDictionary(v => v.Id, StringComparer.Ordinal); var positions = new Vector3[dressing.FixedPlacements.Length];
                for (int i = 0; i < positions.Length; i++)
                {
                    var current = dressing.FixedPlacements[i]; if (!sourceById.TryGetValue(current.Id, out var old) || current.ClusterId != old.ClusterId || current.PrototypeId != old.PrototypeId || current.Euler != old.Euler || current.Scale != old.Scale || current.Preserve != old.Preserve) throw new InvalidOperationException("Fixed placement nonposition field changed: " + current.Id);
                    positions[i] = PlaceOriginal(old.Position); report.fixedPlacements++;
                }
                fixedPlans.Add(new AttachmentFixedPlan { target = dressing, positions = positions });
            }
            AttachmentGuard(); var supportIndex = AttachmentSupports(out string terrainSignature); report.terrainSignature = terrainSignature;
            var roads = new List<AttachmentRoadPlan>();
            foreach (var job in progress.meshes.Where(m => m.road))
            {
                AttachmentGuard();
                if (!job.path.StartsWith("Playtest_VisualCorridor/01_TerrainConformedRoutes/", StringComparison.Ordinal) || !job.result.StartsWith(Folder + "/Meshes/", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected road ribbon job scope: " + job.path);
                var t = Find(job.path); var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(job.result); var source = AssetDatabase.LoadAssetAtPath<Mesh>(job.source);
                if (t == null || mesh == null || source == null || t.GetComponent<MeshFilter>()?.sharedMesh != mesh || EditorUtility.IsDirty(mesh)) throw new InvalidOperationException("Save/bind the original compact road ribbon before finalizing: " + job.path);
                originalPaths.Add(job.source); var vertices = mesh.vertices; var sourceVertices = source.vertices;
                if (vertices.Length != sourceVertices.Length) throw new InvalidOperationException("Road ribbon vertex inventory changed: " + job.path);
                for (int i = 0; i < vertices.Length; i++)
                {
                    var current = t.TransformPoint(vertices[i]); var originalWorld = job.originalMatrix.MultiplyPoint3x4(sourceVertices[i]); var expected = current; report.roadVertices++;
                    if (profile.IsProtected(current.x, current.z)) { expected.y = originalWorld.y; report.protectedRoadVertices++; }
                    else
                    {
                        expected.y = profile.Height(current.x, current.z, originalWorld.y);
                        if (AttachmentFloor(supportIndex, expected, out float supportY, out bool bridge)) { expected.y = supportY + .012f; if (bridge) report.bridgeRoadVertices++; else report.terrainRoadVertices++; }
                        else { expected.y = originalWorld.y; report.missingRoadSupport++; if (findings.Count < 100) findings.Add(job.path + " vertex " + i + " missing actual support at " + current.ToString("R")); }
                    }
                    report.maximumRibbonYChange = Mathf.Max(report.maximumRibbonYChange, Mathf.Abs(expected.y - current.y));
                    var local = t.InverseTransformPoint(expected);
                    // Verify inverse transform cannot alter the retained world XZ/full width.
                    var rebuilt = t.TransformPoint(local); if (Mathf.Abs(rebuilt.x - current.x) > .002f || Mathf.Abs(rebuilt.z - current.z) > .002f) throw new InvalidOperationException("Road ribbon XZ changed while changing Y only.");
                    vertices[i] = local;
                }
                roads.Add(new AttachmentRoadPlan { mesh = mesh, collider = t.GetComponent<MeshCollider>(), vertices = vertices }); report.roadMeshes++;
            }
            report.originalAssets = originalPaths.OrderBy(v => v, StringComparer.Ordinal).Select(path => new AttachmentHash { path = path, before = Hash(path) }).ToArray();
            report.matrixBasesPreserved = report.propBasesPreserved = report.ribbonXZPreserved = true;
            if (report.reliefHash != (profile.Relief == null ? "" : Hash(AssetDatabase.GetAssetPath(profile.Relief)))) throw new InvalidOperationException("Relief dependency changed during attachment preparation.");
            report.status = "PREPARED"; SaveAttachmentReport(report); AttachmentGuard();
            foreach (var prop in props) { var position = prop.target.position; position.y = prop.y; prop.target.position = position; }
            foreach (var foliage in foliagePlans) { foliage.target.Packets = foliage.packets; EditorUtility.SetDirty(foliage.target); }
            foreach (var plan in fixedPlans) { for (int i = 0; i < plan.positions.Length; i++) plan.target.FixedPlacements[i].Position = plan.positions[i]; EditorUtility.SetDirty(plan.target); AssetDatabase.SaveAssetIfDirty(plan.target); }
            foreach (var road in roads) { road.mesh.vertices = road.vertices; road.mesh.RecalculateNormals(); road.mesh.RecalculateBounds(); EditorUtility.SetDirty(road.mesh); AssetDatabase.SaveAssetIfDirty(road.mesh); if (road.collider != null && road.collider.sharedMesh == road.mesh) { road.collider.sharedMesh = null; road.collider.sharedMesh = road.mesh; } }
            Physics.SyncTransforms(); EditorSceneManager.SaveScene(scene);
            foreach (var asset in report.originalAssets) asset.after = Hash(asset.path);
            report.sourceSceneUnchanged = Hash(SourceScene) == report.sourceSceneHash; report.originalAssetsUnchanged = report.originalAssets.All(a => a.before == a.after);
            if (report.reliefHash != (profile.Relief == null ? "" : Hash(AssetDatabase.GetAssetPath(profile.Relief)))) findings.Add("Relief dependency changed during attachment finalization.");
            report.findings = findings.ToArray();
            report.status = report.sourceSceneUnchanged && report.originalAssetsUnchanged && report.missingRoadSupport == 0 && findings.Count == 0 ? "FINAL_GRADE_ATTACHMENTS_REBUILT_FROM_ORIGINAL" : "FINAL_GRADE_ATTACHMENT_FINDINGS";
            return SaveAttachmentReport(report);
        }
    }
}
