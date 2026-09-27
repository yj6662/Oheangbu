using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class InkPaintingMountainForm
    {
        [Serializable] public sealed class NormalFace
        {
            public bool available, topologyExact, verticesExact;
            public string path, asset; public int triangle;
            public int[] indices; public Vector3[] original, preview;
            public Vector3 originalNormal, previewNormal;
            public float maximumVertexDelta, geometricNormalDelta, recordedHitVsOriginalNormal, recordedHitVsPreviewNormal;
        }
        [Serializable] public sealed class NormalProbePair
        {
            public string label; public Vector3 planned;
            public int expectedTriangle; public Vector3 expectedNormal;
            public PhysicalHit before, after;
            public float heightDelta, normalDelta, beforeVsExpectedNormal, afterVsExpectedNormal;
            public bool expectedOwnerAndTriangle, stable, matchesExpectedNormal;
        }
        [Serializable] public sealed class NormalCase
        {
            public string route, side, classification; public Vector3 originalRayOriginXZ;
            public float recordedNormalDelta, recordedHeightDelta, edgeThreshold, sharedEdgeDistance;
            public bool sameOwner, sharedIndexedEdge, nearSharedEdge, bothFacesUnchanged;
            public Vector3 sharedEdgeA, sharedEdgeB;
            public PhysicalHit recordedBefore, recordedAfter;
            public NormalFace beforeFace, afterFace;
            public NormalProbePair[] probes;
        }
        [Serializable] public sealed class NormalAuditReceipt
        {
            public string utc, scene, status, scope;
            public string physicalAuditUtc, preparationUtc; public Settings previewSettings;
            public bool completed, inputCoverageComplete, normalPreservationResolved, colliderBindingsRestored, budgetExceeded;
            public bool nativeTraversalVerified, sceneSaved, navMeshBaked;
            public int cases, preservedEdgeAmbiguities, changedGeometry, changedInteriorPhysics, unresolved;
            public int preexistingRoadHoles, preexistingGradeFindings;
            public float originalMaximumNormalDelta, maximumRepeatedOriginNormalDelta, maximumInteriorNormalDelta, maximumInteriorHeightDelta;
            public double elapsedMilliseconds;
            public NormalCase[] details; public string[] errors, notes;
        }
        public static NormalAuditReceipt LastNormalAudit { get; private set; }
        sealed class NormalMeshCache { public Vector3[] vertices; public int[][] indices; }

        /// <summary>Read-only diagnostic on an active preview. No saved scene/asset or main road report is written.</summary>
        public static string AuditPhysicalNormals(string physicalAuditJson = null)
        {
            Guard(); var s = prepared;
            if (s == null || !s.applied) throw new InvalidOperationException("An active mountain-form preview is required.");
            var input = string.IsNullOrWhiteSpace(physicalAuditJson) ? LastPhysicalAudit : JsonUtility.FromJson<PhysicalAuditReceipt>(physicalAuditJson);
            if (input == null || !input.completed || input.scene != s.scene.path) throw new InvalidOperationException("A completed physical audit of this scene is required.");
            var r = new NormalAuditReceipt { utc = DateTime.UtcNow.ToString("o"), scene = s.scene.path, status = "RUNNING",
                physicalAuditUtc = input.utc, preparationUtc = s.receipt.utc, previewSettings = s.settings,
                preexistingRoadHoles = input.preexistingRoadHoles, preexistingGradeFindings = input.preexistingGradeFindings,
                originalMaximumNormalDelta = input.maximumRoadNormalDelta,
                scope = "Replay recorded >1 degree road-normal changes at the exact same planned XZ; compare selected original/derived triangle vertices and geometric normals; raycast both selected triangle centroids before and after. Classify a preserved shared-edge ambiguity only after all independent checks pass. Original large normal changes remain reported." };
            LastNormalAudit = r;
            var work = new PhysicalWork { state = s, receipt = new PhysicalAuditReceipt() };
            var changes = input.normalFindings != null ? input.normalFindings : (input.findings ?? Array.Empty<PhysicalFinding>()).Where(v => v.normalDeltaDegrees > 1).ToArray();
            // Old receipts appended grades only after the entire road-probe loop.
            // At least one grade row therefore proves that its earlier normal rows
            // had not filled the finding cap. Otherwise limited input stays explicit.
            r.inputCoverageComplete = input.normalFindings != null ? !input.normalFindingsTruncated && input.normalFindings.Length == input.normalChangeSamples :
                (input.findings ?? Array.Empty<PhysicalFinding>()).Length < PhysicalFindingLimit || (input.findings ?? Array.Empty<PhysicalFinding>()).Any(v => v.kind.Contains("GRADE_FINDING"));
            r.inputCoverageComplete &= input.maximumRoadNormalDelta <= 1 || changes.Any(v => v.normalDeltaDegrees >= input.maximumRoadNormalDelta - .01f);
            var rows = new List<NormalCase>();
            try
            {
                PreparePhysicsBindings(work); CollectPhysicalSupports(work);
                var derivedVertices = new Dictionary<Source, NormalMeshCache>();
                foreach (var change in changes.OrderByDescending(v => v.normalDeltaDegrees))
                { CheckPhysicalBudget(work); rows.Add(BuildNormalCase(s, change, derivedVertices)); }
                r.cases = rows.Count;
                try
                {
                    foreach (var binding in work.bindings) { CheckPhysicalBudget(work); binding.target.sharedMesh = binding.original; }
                    Physics.SyncTransforms(); RefreshPhysicalBounds(work);
                    ProbeNormalCases(work, rows, true);
                }
                finally { RestorePhysicalBindings(work); }
                if (!work.receipt.colliderBindingsRestored) throw new InvalidOperationException("Preview collider restoration failed.");
                ProbeNormalCases(work, rows, false);
                foreach (var row in rows) ClassifyNormalCase(row, r);
                CheckPhysicalBudget(work); r.completed = true;
                r.normalPreservationResolved = r.inputCoverageComplete && r.changedGeometry == 0 && r.changedInteriorPhysics == 0 && r.unresolved == 0;
                r.status = r.normalPreservationResolved ? rows.Count == 0 ? "NO_RECORDED_NORMAL_CHANGES" : "RESOLVED_PRESERVED_SHARED_EDGE_SELECTION" : "NORMAL_CHANGE_REQUIRES_REVIEW";
            }
            catch (TimeoutException e) { r.status = "INCOMPLETE_TIME_BUDGET"; r.budgetExceeded = true; work.errors.Add(e.Message); }
            catch (Exception e) { r.status = "FAILED_NORMAL_AUDIT"; work.errors.Add(e.ToString()); }
            finally
            {
                if (work.bindings.Any(v => v.target != null && v.target.sharedMesh != v.preview)) RestorePhysicalBindings(work);
                r.colliderBindingsRestored = work.bindings.All(v => v.target != null && v.target.sharedMesh == v.preview);
                if (!r.colliderBindingsRestored) r.status = "FAIL_COLLIDER_RESTORATION";
                if (!r.completed || !r.colliderBindingsRestored || work.errors.Count > 0) r.normalPreservationResolved = false;
                if (work.errors.Count > 0 && r.status.StartsWith("RESOLVED_", StringComparison.Ordinal)) r.status = "FAILED_NORMAL_AUDIT";
                r.details = rows.ToArray(); r.errors = work.errors.ToArray(); r.elapsedMilliseconds = work.timer.Elapsed.TotalMilliseconds;
                r.notes = new[] {
                    "Original 81-degree (or other) differences are retained; only unchanged selected faces with a proven shared edge and stable centroid raycasts receive the ambiguity classification.",
                    "Near-edge threshold is max(0.5mm, eight world-coordinate float ULPs), solely a diagnostic classification threshold. Sampler tolerances, physical height thresholds and the original report are unchanged.",
                    "Any changed selected face is reported as real geometry change even at an edge. Interior height, normal, owner/triangle or geometric-normal mismatch prevents a resolved classification.",
                    "Preexisting holes and grade findings remain unchanged findings. This audit does not prove walking, wheel contact, off-road reachability or NavMesh traversal."
                };
            }
            return JsonUtility.ToJson(r, true);
        }

        static NormalFace ReadNormalFace(State s, PhysicalHit hit, Dictionary<Source, NormalMeshCache> cache)
        {
            var row = new NormalFace { path = hit?.path, triangle = hit != null ? hit.triangle : -1 };
            if (hit == null || !hit.found || hit.triangle < 0) return row;
            var source = s.sources.FirstOrDefault(v => v.row.path == hit.path && v.collider != null);
            if (source == null) return row;
            int offset = checked(hit.triangle * 3), sub = 0;
            while (sub < source.indices.Length && offset >= source.indices[sub].Length) offset -= source.indices[sub++].Length;
            if (sub >= source.indices.Length || offset + 2 >= source.indices[sub].Length) return row;
            row.indices = new[] { source.indices[sub][offset], source.indices[sub][offset + 1], source.indices[sub][offset + 2] };
            var mesh = source.derived != null ? source.derived : source.original;
            if (!cache.TryGetValue(source, out var data))
            {
                data = new NormalMeshCache { vertices = mesh.vertices, indices = new int[mesh.subMeshCount][] };
                for (int i = 0; i < data.vertices.Length; i++) data.vertices[i] = source.matrix.MultiplyPoint3x4(data.vertices[i]);
                for (int i = 0; i < data.indices.Length; i++) data.indices[i] = mesh.GetIndices(i);
                cache[source] = data;
            }
            var actualIndices = data.indices[sub];
            row.topologyExact = offset + 2 < actualIndices.Length && actualIndices[offset] == row.indices[0] && actualIndices[offset + 1] == row.indices[1] && actualIndices[offset + 2] == row.indices[2];
            row.original = row.indices.Select(v => source.world[v]).ToArray(); row.preview = row.indices.Select(v => data.vertices[v]).ToArray();
            row.verticesExact = true;
            for (int i = 0; i < 3; i++)
            {
                var a = row.original[i]; var b = row.preview[i];
                row.verticesExact &= a.x == b.x && a.y == b.y && a.z == b.z;
                row.maximumVertexDelta = Mathf.Max(row.maximumVertexDelta, Vector3.Distance(a, b));
            }
            row.originalNormal = FaceNormal(row.original); row.previewNormal = FaceNormal(row.preview);
            row.geometricNormalDelta = Vector3.Angle(row.originalNormal, row.previewNormal);
            row.recordedHitVsOriginalNormal = Vector3.Angle(hit.normal, row.originalNormal);
            row.recordedHitVsPreviewNormal = Vector3.Angle(hit.normal, row.previewNormal);
            row.available = true; row.asset = source.row.source; return row;
        }
        static Vector3 FaceNormal(Vector3[] vertices) => Vector3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]).normalized;
        static NormalCase BuildNormalCase(State s, PhysicalFinding change, Dictionary<Source, NormalMeshCache> cache)
        {
            var row = new NormalCase { route = change.route, side = change.side, originalRayOriginXZ = change.planned,
                recordedNormalDelta = change.normalDeltaDegrees, recordedHeightDelta = change.heightDelta,
                recordedBefore = change.before, recordedAfter = change.after,
                beforeFace = ReadNormalFace(s, change.before, cache), afterFace = ReadNormalFace(s, change.after, cache) };
            row.sameOwner = change.before != null && change.after != null && change.before.path == change.after.path;
            row.edgeThreshold = Mathf.Max(.0005f, 8 * Mathf.Max(SupportCoordinateUlp(change.planned.x), SupportCoordinateUlp(change.planned.z)));
            row.sharedEdgeDistance = -1;
            var probes = new List<NormalProbePair> { new NormalProbePair { label = "same_origin", planned = change.planned, expectedTriangle = -1 } };
            if (row.beforeFace.available && row.afterFace.available)
            {
                row.bothFacesUnchanged = row.beforeFace.topologyExact && row.afterFace.topologyExact && row.beforeFace.verticesExact && row.afterFace.verticesExact;
                var shared = row.beforeFace.indices.Intersect(row.afterFace.indices).ToArray();
                row.sharedIndexedEdge = row.sameOwner && row.beforeFace.triangle != row.afterFace.triangle && shared.Length == 2;
                if (row.sharedIndexedEdge)
                {
                    row.sharedEdgeA = row.beforeFace.original[Array.IndexOf(row.beforeFace.indices, shared[0])];
                    row.sharedEdgeB = row.beforeFace.original[Array.IndexOf(row.beforeFace.indices, shared[1])];
                    row.sharedEdgeDistance = PointEdgeDistance(change.planned, row.sharedEdgeA, row.sharedEdgeB);
                    row.nearSharedEdge = row.sharedEdgeDistance <= row.edgeThreshold;
                }
                probes.Add(FaceCentreProbe("before_face_centroid", row.beforeFace));
                probes.Add(FaceCentreProbe("after_face_centroid", row.afterFace));
            }
            row.probes = probes.ToArray(); return row;
        }
        static float PointEdgeDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            double dx = (double)b.x - a.x, dz = (double)b.z - a.z;
            double t = Math.Max(0, Math.Min(1, (((double)p.x - a.x) * dx + ((double)p.z - a.z) * dz) / Math.Max(1e-30, dx * dx + dz * dz)));
            double ex = p.x - (a.x + dx * t), ez = p.z - (a.z + dz * t); return (float)Math.Sqrt(ex * ex + ez * ez);
        }
        static NormalProbePair FaceCentreProbe(string label, NormalFace face)
        {
            var tri = new Triangle { a = face.original[0], b = face.original[1], c = face.original[2] };
            return new NormalProbePair { label = label, planned = InsetGeneratedSupportPoint(tri, 1.0 / 3, 1.0 / 3, 1.0 / 3, false, out _),
                expectedTriangle = face.triangle, expectedNormal = face.originalNormal };
        }
        static void ProbeNormalCases(PhysicalWork work, List<NormalCase> rows, bool before)
        {
            foreach (var row in rows) foreach (var probe in row.probes)
            { CheckPhysicalBudget(work); var hit = ProbePhysical(work, probe.planned, false); if (before) probe.before = hit; else probe.after = hit; }
        }
        static void ClassifyNormalCase(NormalCase row, NormalAuditReceipt receipt)
        {
            bool interiorStable = row.probes.Length == 3, interiorChanged = false;
            foreach (var p in row.probes)
            {
                bool both = p.before != null && p.after != null && p.before.found && p.after.found;
                if (both) { p.heightDelta = p.after.point.y - p.before.point.y; p.normalDelta = Vector3.Angle(p.before.normal, p.after.normal); }
                if (p.label == "same_origin") { receipt.maximumRepeatedOriginNormalDelta = Mathf.Max(receipt.maximumRepeatedOriginNormalDelta, p.normalDelta); continue; }
                p.expectedOwnerAndTriangle = both && p.before.path == row.beforeFace.path && p.after.path == row.beforeFace.path && p.before.triangle == p.expectedTriangle && p.after.triangle == p.expectedTriangle;
                if (both) { p.beforeVsExpectedNormal = Vector3.Angle(p.before.normal, p.expectedNormal); p.afterVsExpectedNormal = Vector3.Angle(p.after.normal, p.expectedNormal); }
                p.matchesExpectedNormal = both && p.beforeVsExpectedNormal <= .1f && p.afterVsExpectedNormal <= .1f;
                p.stable = both && Mathf.Abs(p.heightDelta) <= RoadChangeTolerance && p.normalDelta <= 1;
                interiorStable &= p.stable && p.expectedOwnerAndTriangle && p.matchesExpectedNormal;
                interiorChanged |= p.before != null && p.before.found && (p.after == null || !p.after.found || Mathf.Abs(p.heightDelta) > RoadChangeTolerance || p.normalDelta > 1);
                receipt.maximumInteriorHeightDelta = Mathf.Max(receipt.maximumInteriorHeightDelta, Mathf.Abs(p.heightDelta));
                receipt.maximumInteriorNormalDelta = Mathf.Max(receipt.maximumInteriorNormalDelta, p.normalDelta);
            }
            if (row.beforeFace.available && row.afterFace.available && !row.bothFacesUnchanged)
            { row.classification = "SELECTED_FACE_GEOMETRY_CHANGED"; receipt.changedGeometry++; }
            else if (interiorChanged) { row.classification = "INTERIOR_PHYSICS_CHANGED"; receipt.changedInteriorPhysics++; }
            else if (row.bothFacesUnchanged && row.sharedIndexedEdge && row.nearSharedEdge && interiorStable &&
                row.beforeFace.recordedHitVsOriginalNormal <= .1f && row.afterFace.recordedHitVsPreviewNormal <= .1f)
            { row.classification = "PRESERVED_SHARED_EDGE_SELECTION_AMBIGUITY"; receipt.preservedEdgeAmbiguities++; }
            else { row.classification = "UNRESOLVED_NORMAL_CHANGE"; receipt.unresolved++; }
        }
    }
}
