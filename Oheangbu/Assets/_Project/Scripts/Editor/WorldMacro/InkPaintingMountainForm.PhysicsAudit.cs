using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class InkPaintingMountainForm
    {
        const float PhysicalRouteSpacing = 5, PhysicalCellSize = 64;
        const float RoadChangeTolerance = .01f, PhysicsCpuTolerance = .05f, PhysicalGradeTolerance = .1f;
        const double PhysicalWorkBudgetMilliseconds = 24000;
        const int PhysicalFindingLimit = 100;

        [Serializable] public sealed class PhysicalHit
        {
            public bool found; public Vector3 point, normal; public string path, kind;
            public int triangle = -1; public float plannedHeightError;
        }
        [Serializable] public sealed class PhysicalFinding
        {
            public string kind, route, side; public Vector3 planned;
            public float distance, heightDelta, normalDeltaDegrees;
            public float beforeLongitudinal, afterLongitudinal, beforeCrossSlope, afterCrossSlope;
            public PhysicalHit before, after;
        }
        [Serializable] public sealed class PhysicalRouteRow
        {
            public string id; public bool carriage;
            public float width, length, gradeLimit;
            public int stations, expectedProbes, comparedProbes, beforeHoles, afterHoles, newHoles, resolvedHoles, changedHeights;
            public int beforeGradeFindings, afterGradeFindings, newGradeFindings, changedSupportOwners;
            public float maximumHeightDelta, maximumNormalDelta, maximumBeforePlannedError, maximumAfterPlannedError;
            public float maximumBeforeLongitudinal, maximumAfterLongitudinal, maximumBeforeCrossSlope, maximumAfterCrossSlope;
        }
        [Serializable] public sealed class PhysicalSurfaceRow
        {
            public Vector3 position, cpuFinalNormal;
            public float cpuBeforeY, cpuFinalY, beforeError, finalError, normalDeltaDegrees;
            public bool cpuFound; public PhysicalHit before, after;
            public string cpuBeforeSource, cpuAfterSource; public int cpuBeforeTriangle, cpuAfterTriangle;
        }
        [Serializable] public sealed class PhysicalBoundaryRow
        {
            public string generatedProbeClassification; public Vector3 position;
            public bool cpuReferenceFound, runtimeSamplerFound;
            public PhysicalHit before, after;
        }
        [Serializable] public sealed class PhysicalAuditReceipt
        {
            public string utc, scene, status, scope;
            public float stationSpacing = PhysicalRouteSpacing, roadHeightChangeTolerance = RoadChangeTolerance,
                terrainCpuHeightTolerance = PhysicsCpuTolerance, gradeComparisonTolerance = PhysicalGradeTolerance;
            public int authoredRoutes, gradeLines, terrainColliders, bridgeColliders, caveFloorColliders, colliderBindingsTemporarilySwapped;
            public int plannedRoadProbes, beforeRoadProbes, afterRoadProbes, comparedRoadProbes, preexistingRoadHoles, newRoadHoles,
                changedRoadHeights, preexistingGradeFindings, newGradeFindings, terrainSamples, terrainCpuMissing, terrainBeforeHoles,
                terrainAfterHoles, terrainCpuHeightMismatches, terrainNormalDifferences;
            public int rawBoundaryDiagnosticSamples, rawBoundaryBeforeHoles, rawBoundaryAfterHoles, rawBoundaryNewHoles;
            public int normalChangeSamples; public bool normalFindingsTruncated;
            public float maximumRoadHeightDelta, maximumRoadNormalDelta, maximumCpuBeforeError, maximumCpuFinalError, maximumCpuNormalDelta;
            public bool completed, preservationPass, colliderBindingsRestored, wasDirty, isDirty, budgetExceeded;
            public bool nativeTraversalVerified, navMeshBaked, sceneSaved;
            public double elapsedMilliseconds, originalColliderSwapMilliseconds, previewColliderRestoreMilliseconds,
                beforeProbeMilliseconds, afterProbeMilliseconds;
            public PhysicalRouteRow[] routes; public PhysicalFinding[] findings; public PhysicalSurfaceRow[] terrain;
            public PhysicalFinding[] normalFindings;
            public PhysicalBoundaryRow[] rawBoundaryDiagnostics;
            public string[] errors, notes;
        }
        public static PhysicalAuditReceipt LastPhysicalAudit { get; private set; }

        sealed class PhysicsSupport
        { public Collider collider; public Bounds bounds; public string path, kind; }
        sealed class PhysicsBinding
        { public MeshCollider target; public Mesh original, preview; public string path; }
        sealed class RouteStation
        { public int route; public float distance; public Vector3 centre, left, right; }
        sealed class RouteProbe
        { public int route, station, side; public Vector3 planned; public float distance; }
        sealed class PhysicalWork
        {
            public State state; public PhysicalAuditReceipt receipt;
            public readonly List<PhysicsBinding> bindings = new List<PhysicsBinding>();
            public readonly List<PhysicsSupport> supports = new List<PhysicsSupport>();
            public readonly Dictionary<long, List<PhysicsSupport>> bins = new Dictionary<long, List<PhysicsSupport>>();
            public readonly List<RouteStation> stations = new List<RouteStation>();
            public readonly List<RouteProbe> probes = new List<RouteProbe>();
            public readonly List<PhysicalFinding> findings = new List<PhysicalFinding>();
            public readonly List<PhysicalFinding> normalFindings = new List<PhysicalFinding>();
            public readonly List<string> errors = new List<string>();
            public PhysicalHit[] before, after, terrainBefore, terrainAfter;
            public PhysicalHit[] boundaryBefore, boundaryAfter;
            public SupportIssue[] boundaryIssues;
            public Vector3[] terrainPoints;
            public readonly System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
        }

        /// <summary>
        /// Audit an ACTIVE temporary preview. Only prepared terrain collider mesh references
        /// are switched to originals for before probes, then restored in finally. Render mesh,
        /// static placements, assets, scene save state and NavMesh data are not authored here.
        /// Returns its own JSON; never overwrites the existing road-physics reports.
        /// </summary>
        public static string AuditPhysicalSupport()
        {
            Guard();
            var s = prepared;
            if (s == null || !s.applied) throw new InvalidOperationException("An active mountain-form preview is required for physical support auditing.");
            if (EditorApplication.isCompiling) throw new InvalidOperationException("Wait for compilation before the physical audit.");
            var receipt = new PhysicalAuditReceipt { utc = DateTime.UtcNow.ToString("o"), scene = s.scene.path,
                status = "RUNNING", wasDirty = s.scene.isDirty,
                scope = "Actual Collider.Raycast comparison: all 36 authored fitted routes plus Content.MainPath, centre and both actual-width edges at <=5m intervals. Original collider references are temporarily restored for before probes; active preview colliders are restored in finally. Stratified changed-terrain probes compare CPU highest final surface to actual physics. No player/vehicle traversal, save or NavMesh bake." };
            LastPhysicalAudit = receipt;
            var work = new PhysicalWork { state = s, receipt = receipt };
            bool beforeComplete = false;
            try
            {
                PreparePhysicsBindings(work);
                PreparePhysicalRoutes(work);
                CollectPhysicalSupports(work);
                SelectPhysicalTerrainPoints(work);
                work.before = new PhysicalHit[work.probes.Count]; work.after = new PhysicalHit[work.probes.Count];
                work.terrainBefore = new PhysicalHit[work.terrainPoints.Length]; work.terrainAfter = new PhysicalHit[work.terrainPoints.Length];
                work.boundaryIssues = s.receipt.rawBoundaryDetails ?? Array.Empty<SupportIssue>();
                work.boundaryBefore = new PhysicalHit[work.boundaryIssues.Length]; work.boundaryAfter = new PhysicalHit[work.boundaryIssues.Length];
                try
                {
                    var swap = System.Diagnostics.Stopwatch.StartNew();
                    foreach (var binding in work.bindings)
                    {
                        CheckPhysicalBudget(work);
                        binding.target.sharedMesh = binding.original;
                    }
                    Physics.SyncTransforms(); RefreshPhysicalBounds(work);
                    receipt.originalColliderSwapMilliseconds = swap.Elapsed.TotalMilliseconds;
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    ProbePhysicalPhase(work, work.before, work.terrainBefore, true);
                    receipt.beforeProbeMilliseconds = timer.Elapsed.TotalMilliseconds;
                    beforeComplete = true;
                }
                finally
                {
                    var restore = System.Diagnostics.Stopwatch.StartNew();
                    RestorePhysicalBindings(work);
                    receipt.previewColliderRestoreMilliseconds = restore.Elapsed.TotalMilliseconds;
                }
                if (!receipt.colliderBindingsRestored) throw new InvalidOperationException("Preview collider restoration failed; remaining audit phases were skipped.");
                CheckPhysicalBudget(work);
                var afterTimer = System.Diagnostics.Stopwatch.StartNew();
                ProbePhysicalPhase(work, work.after, work.terrainAfter, false);
                receipt.afterProbeMilliseconds = afterTimer.Elapsed.TotalMilliseconds;
                ComparePhysicalRoutes(work);
                ComparePhysicalTerrain(work);
                ComparePhysicalBoundaries(work);
                CheckPhysicalBudget(work);
                receipt.completed = beforeComplete && receipt.beforeRoadProbes == work.probes.Count && receipt.afterRoadProbes == work.probes.Count;
                receipt.preservationPass = receipt.completed && receipt.colliderBindingsRestored && receipt.newRoadHoles == 0 &&
                    receipt.changedRoadHeights == 0 && receipt.newGradeFindings == 0 && receipt.terrainSamples > 0 &&
                    receipt.terrainCpuMissing == 0 && receipt.terrainAfterHoles == 0 && receipt.terrainCpuHeightMismatches == 0 && receipt.rawBoundaryNewHoles == 0;
                receipt.status = !receipt.preservationPass ? "FAIL_PHYSICAL_PRESERVATION" :
                    receipt.preexistingRoadHoles > 0 || receipt.preexistingGradeFindings > 0 || receipt.terrainBeforeHoles > 0 || receipt.rawBoundaryBeforeHoles > 0 ?
                    "PASS_PRESERVATION_WITH_PREEXISTING_FINDINGS" : "PASS_SAMPLED_PHYSICAL_PRESERVATION";
            }
            catch (TimeoutException e) { receipt.status = "INCOMPLETE_TIME_BUDGET"; receipt.budgetExceeded = true; work.errors.Add(e.Message); }
            catch (Exception e) { receipt.status = "FAILED_AUDIT"; work.errors.Add(e.ToString()); }
            finally
            {
                // Also retry here if a previous restoration failed. Never abandon an
                // original collider binding because another independent restoration threw.
                if (work.bindings.Any(b => b.target != null && b.target.sharedMesh != b.preview)) RestorePhysicalBindings(work);
                receipt.colliderBindingsRestored = work.bindings.All(b => b.target != null && b.target.sharedMesh == b.preview);
                receipt.isDirty = s.scene.IsValid() && s.scene.isDirty;
                receipt.elapsedMilliseconds = work.timer.Elapsed.TotalMilliseconds;
                receipt.findings = work.findings.ToArray(); receipt.errors = work.errors.ToArray();
                receipt.normalFindings = work.normalFindings.OrderByDescending(v => v.normalDeltaDegrees).ToArray();
                receipt.notes = new[] {
                    "PASS applies only to sampled before/after collision preservation. Native walking, carriage traction, off-route reachability and NavMesh/link validation remain unverified.",
                    "Road supports use nearest fitted height; explicit bridge/cave floors are eligible only within +0.8/-4m and take priority when no farther than terrain +0.05m, matching the existing audit policy.",
                    "Existing planned-height deviations >1m and grade exceedances (>14deg carriage, >30deg foot, +0.1deg tolerance) remain findings even when unchanged.",
                    "The 24s work cutoff leaves time for mandatory restoration. A single native collider cooking call cannot be preempted; any overrun is reported, never treated as a completed pass.",
                    "CPU-vs-physics height tolerance is 0.05m; road preservation tolerance is 0.01m. Normal differences are reported separately, including ambiguous triangle-edge normals."
                    ,"Raw generated boundary misses remain a separate before/after actual-collider diagnostic; inward validation points never erase these counts. A new raw-boundary physical hole fails preservation."
                };
                if (!receipt.colliderBindingsRestored) { receipt.status = "FAIL_COLLIDER_RESTORATION"; receipt.preservationPass = false; }
                if (!receipt.completed || work.errors.Count > 0) receipt.preservationPass = false;
                if (receipt.status.StartsWith("PASS_", StringComparison.Ordinal) && !receipt.preservationPass) receipt.status = "FAILED_AUDIT";
            }
            return JsonUtility.ToJson(receipt, true);
        }

        static void PreparePhysicsBindings(PhysicalWork work)
        {
            foreach (var source in work.state.sources)
            {
                if (source.collider == null || source.derived == null) continue;
                if (!source.collider.enabled || !source.collider.gameObject.activeInHierarchy || source.collider.sharedMesh != source.derived || source.filter.sharedMesh != source.derived)
                    throw new InvalidOperationException("Active preview mesh/collider binding changed: " + source.row.path);
                work.bindings.Add(new PhysicsBinding { target = source.collider, original = source.original, preview = source.collider.sharedMesh, path = source.row.path });
            }
            if (work.bindings.Count == 0) throw new InvalidOperationException("No changed preview terrain colliders are available.");
            work.receipt.colliderBindingsTemporarilySwapped = work.bindings.Count;
        }
        static void RestorePhysicalBindings(PhysicalWork work)
        {
            foreach (var binding in work.bindings)
            {
                try
                {
                    if (binding.target == null) throw new InvalidOperationException("Collider destroyed during audit: " + binding.path);
                    if (binding.target.sharedMesh != binding.preview) binding.target.sharedMesh = binding.preview;
                }
                catch (Exception e) { work.errors.Add("Restoration: " + e.Message); }
            }
            try { Physics.SyncTransforms(); RefreshPhysicalBounds(work); }
            catch (Exception e) { work.errors.Add("Restoration transform sync: " + e.Message); }
            work.receipt.colliderBindingsRestored = work.bindings.All(b => b.target != null && b.target.sharedMesh == b.preview);
        }

        static void PreparePhysicalRoutes(PhysicalWork work)
        {
            var geo = AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(work.state.settings.Geography);
            var grade = geo != null ? geo.CompactRoadGrade : null;
            if (geo == null || grade == null || geo.Routes == null || geo.Routes.Length != 36 || grade.Lines == null || grade.Lines.Length != 37)
                throw new InvalidOperationException("Expected the exact 36 authored routes plus Content.MainPath in the current compact road grade.");
            grade.Validate();
            var lines = grade.Lines.ToDictionary(v => v.Id, StringComparer.Ordinal);
            if (!lines.ContainsKey("Content.MainPath") || geo.Routes.Any(v => !lines.ContainsKey(v.Id)))
                throw new InvalidOperationException("Authored route IDs do not match the current fitted road-grade lines.");
            work.receipt.authoredRoutes = geo.Routes.Length; work.receipt.gradeLines = grade.Lines.Length;
            var rows = new List<PhysicalRouteRow>();
            foreach (var id in geo.Routes.Select(v => v.Id).Concat(new[] { "Content.MainPath" }))
            {
                var line = lines[id]; var authored = geo.Routes.FirstOrDefault(v => v.Id == id);
                bool carriage = authored != null && authored.Carriage;
                var arc = new float[line.Points.Length];
                for (int i = 1; i < arc.Length; i++) arc[i] = arc[i - 1] + Vector2.Distance(XZ(line.Points[i - 1]), XZ(line.Points[i]));
                float length = arc[arc.Length - 1]; if (length < .001f) throw new InvalidOperationException("Degenerate fitted route: " + id);
                int intervals = Mathf.CeilToInt(length / PhysicalRouteSpacing), routeIndex = rows.Count;
                var row = new PhysicalRouteRow { id = id, carriage = carriage, width = line.Width, length = length, gradeLimit = carriage ? 14 : 30,
                    stations = intervals + 1, expectedProbes = (intervals + 1) * 3 };
                rows.Add(row);
                for (int k = 0; k <= intervals; k++)
                {
                    float distance = length * k / intervals;
                    var centre = PhysicalAt(line.Points, arc, distance);
                    var tangent = PhysicalAt(line.Points, arc, Mathf.Min(length, distance + 1.5f)) - PhysicalAt(line.Points, arc, Mathf.Max(0, distance - 1.5f)); tangent.y = 0;
                    if (tangent.sqrMagnitude < 1e-8f) throw new InvalidOperationException("Unresolved route tangent: " + id + " at " + distance);
                    var side = new Vector3(-tangent.z, 0, tangent.x).normalized * (line.Width * .5f);
                    var station = new RouteStation { route = routeIndex, distance = distance, centre = centre, left = centre + side, right = centre - side };
                    int stationIndex = work.stations.Count; work.stations.Add(station);
                    work.probes.Add(new RouteProbe { route = routeIndex, station = stationIndex, side = 0, planned = centre, distance = distance });
                    work.probes.Add(new RouteProbe { route = routeIndex, station = stationIndex, side = 1, planned = station.left, distance = distance });
                    work.probes.Add(new RouteProbe { route = routeIndex, station = stationIndex, side = 2, planned = station.right, distance = distance });
                }
            }
            work.receipt.routes = rows.ToArray(); work.receipt.plannedRoadProbes = work.probes.Count;
        }
        static Vector3 PhysicalAt(Vector3[] points, float[] arc, float distance)
        {
            if (distance <= 0) return points[0]; if (distance >= arc[arc.Length - 1]) return points[points.Length - 1];
            int i = Array.BinarySearch(arc, distance); if (i >= 0) return points[i]; i = ~i;
            return Vector3.Lerp(points[i - 1], points[i], Mathf.InverseLerp(arc[i - 1], arc[i], distance));
        }

        static string PhysicalSupportKind(Collider c, string path)
        {
            if (c is MeshCollider && path.StartsWith(TerrainRoot, StringComparison.Ordinal) && c.name.StartsWith("Terrain_", StringComparison.Ordinal)) return "terrain";
            if (path.StartsWith("WorldMacro_AuthoredGeography/03_SettlementAndLandmark_Massing/", StringComparison.Ordinal) &&
                (c.name == "Bridge_Deck_TEST" || c.name.StartsWith("Bridge_Apron_TEST_", StringComparison.Ordinal))) return "bridge";
            if (path.StartsWith("Playtest_NaturalCave/", StringComparison.Ordinal) && !path.Contains("_Previous_") &&
                (c.name == "Natural_Cave_Floor" || c.name == "Continuous_Approach_Soil" || c.name == "Portal_Outer_Soil" || c.name == "Exterior_Mine_Approach" || c.name == "Natural_Mine_Broad_Apron")) return "cave_floor";
            return null;
        }
        static void CollectPhysicalSupports(PhysicalWork work)
        {
            foreach (var collider in work.state.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>(true)))
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger || collider.attachedRigidbody != null) continue;
                string path = Hierarchy(collider.transform), kind = PhysicalSupportKind(collider, path); if (kind == null) continue;
                if (collider is MeshCollider m && m.sharedMesh == null) throw new InvalidOperationException("Missing support mesh: " + path);
                work.supports.Add(new PhysicsSupport { collider = collider, bounds = collider.bounds, path = path, kind = kind });
            }
            work.supports.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
            foreach (var support in work.supports)
            {
                var b = support.bounds;
                for (int z = Mathf.FloorToInt((b.min.z - .01f) / PhysicalCellSize); z <= Mathf.FloorToInt((b.max.z + .01f) / PhysicalCellSize); z++)
                    for (int x = Mathf.FloorToInt((b.min.x - .01f) / PhysicalCellSize); x <= Mathf.FloorToInt((b.max.x + .01f) / PhysicalCellSize); x++)
                    { long key = Key(x, z); if (!work.bins.TryGetValue(key, out var bucket)) work.bins[key] = bucket = new List<PhysicsSupport>(); bucket.Add(support); }
            }
            work.receipt.terrainColliders = work.supports.Count(v => v.kind == "terrain");
            work.receipt.bridgeColliders = work.supports.Count(v => v.kind == "bridge");
            work.receipt.caveFloorColliders = work.supports.Count(v => v.kind == "cave_floor");
            if (work.receipt.terrainColliders == 0) throw new InvalidOperationException("Actual terrain colliders are missing.");
        }
        static void RefreshPhysicalBounds(PhysicalWork work)
        { foreach (var support in work.supports) if (support.collider != null) support.bounds = support.collider.bounds; }
        static bool PhysicalInside(Bounds b, Vector3 p) => p.x >= b.min.x - .01f && p.x <= b.max.x + .01f && p.z >= b.min.z - .01f && p.z <= b.max.z + .01f;
        static PhysicalHit ProbePhysical(PhysicalWork work, Vector3 planned, bool terrainTop)
        {
            var result = new PhysicalHit();
            if (!work.bins.TryGetValue(Key(Mathf.FloorToInt(planned.x / PhysicalCellSize), Mathf.FloorToInt(planned.z / PhysicalCellSize)), out var candidates)) return result;
            float terrainError = float.PositiveInfinity, deckError = float.PositiveInfinity, highest = float.NegativeInfinity;
            PhysicalHit deck = null;
            foreach (var candidate in candidates)
            {
                if (terrainTop && candidate.kind != "terrain" || !PhysicalInside(candidate.bounds, planned)) continue;
                var ray = new Ray(new Vector3(planned.x, candidate.bounds.max.y + 2, planned.z), Vector3.down);
                if (!candidate.collider.Raycast(ray, out var hit, candidate.bounds.size.y + 4) || (!terrainTop && hit.normal.y < .1f)) continue;
                float error = Mathf.Abs(hit.point.y - planned.y);
                if (candidate.kind == "terrain")
                {
                    if (terrainTop ? hit.point.y <= highest : error >= terrainError) continue;
                    highest = hit.point.y; terrainError = error;
                    result = MakePhysicalHit(candidate, hit, error);
                }
                else if (hit.point.y <= planned.y + .8f && hit.point.y >= planned.y - 4 && error < deckError)
                { deckError = error; deck = MakePhysicalHit(candidate, hit, error); }
            }
            return !terrainTop && deck != null && deckError <= terrainError + .05f ? deck : result;
        }
        static PhysicalHit MakePhysicalHit(PhysicsSupport support, RaycastHit hit, float error) =>
            new PhysicalHit { found = true, point = hit.point, normal = hit.normal, path = support.path, kind = support.kind, triangle = hit.triangleIndex, plannedHeightError = error };

        static void SelectPhysicalTerrainPoints(PhysicalWork work)
        {
            const int columns = 24, rows = 16;
            var choices = new Vector3[columns * rows]; var scores = Enumerable.Repeat(float.PositiveInfinity, choices.Length).ToArray();
            var rect = work.state.domain;
            foreach (var t in work.state.triangles)
            {
                if (Mathf.Max(Mathf.Abs(t.delta.x), Mathf.Max(Mathf.Abs(t.delta.y), Mathf.Abs(t.delta.z))) < .01f) continue;
                var p = (t.a + t.b + t.c) / 3; if (!Inclusive(rect, p.x, p.z)) continue;
                int x = Mathf.Clamp((int)((p.x - rect.xMin) / rect.width * columns), 0, columns - 1),
                    z = Mathf.Clamp((int)((p.z - rect.yMin) / rect.height * rows), 0, rows - 1), i = z * columns + x;
                var centre = new Vector2(rect.xMin + (x + .5f) * rect.width / columns, rect.yMin + (z + .5f) * rect.height / rows);
                float score = (XZ(p) - centre).sqrMagnitude;
                if (score < scores[i]) { scores[i] = score; choices[i] = p; }
            }
            work.terrainPoints = choices.Where((_, i) => !float.IsPositiveInfinity(scores[i])).ToArray();
            work.receipt.terrainSamples = work.terrainPoints.Length;
        }
        static void ProbePhysicalPhase(PhysicalWork work, PhysicalHit[] roads, PhysicalHit[] terrain, bool before)
        {
            for (int i = 0; i < work.probes.Count; i++)
            {
                if ((i & 127) == 0) CheckPhysicalBudget(work);
                roads[i] = ProbePhysical(work, work.probes[i].planned, false);
                if (before) work.receipt.beforeRoadProbes++; else work.receipt.afterRoadProbes++;
            }
            for (int i = 0; i < work.terrainPoints.Length; i++)
            { if ((i & 63) == 0) CheckPhysicalBudget(work); terrain[i] = ProbePhysical(work, work.terrainPoints[i], true); }
            var boundaries = before ? work.boundaryBefore : work.boundaryAfter;
            for (int i = 0; i < work.boundaryIssues.Length; i++)
            { CheckPhysicalBudget(work); boundaries[i] = ProbePhysical(work, work.boundaryIssues[i].position, true); }
        }

        static void ComparePhysicalRoutes(PhysicalWork work)
        {
            for (int i = 0; i < work.probes.Count; i++)
            {
                if ((i & 255) == 0) CheckPhysicalBudget(work);
                var p = work.probes[i]; var row = work.receipt.routes[p.route]; var before = work.before[i]; var after = work.after[i];
                row.comparedProbes++; work.receipt.comparedRoadProbes++;
                if (!before.found) { row.beforeHoles++; work.receipt.preexistingRoadHoles++; }
                if (!after.found) row.afterHoles++;
                if (before.found && !after.found) { row.newHoles++; work.receipt.newRoadHoles++; }
                if (!before.found && after.found) row.resolvedHoles++;
                float delta = before.found && after.found ? after.point.y - before.point.y : 0;
                float normal = before.found && after.found ? Vector3.Angle(before.normal, after.normal) : 0;
                row.maximumHeightDelta = Mathf.Max(row.maximumHeightDelta, Mathf.Abs(delta)); row.maximumNormalDelta = Mathf.Max(row.maximumNormalDelta, normal);
                work.receipt.maximumRoadHeightDelta = Mathf.Max(work.receipt.maximumRoadHeightDelta, Mathf.Abs(delta));
                work.receipt.maximumRoadNormalDelta = Mathf.Max(work.receipt.maximumRoadNormalDelta, normal);
                if (before.found) row.maximumBeforePlannedError = Mathf.Max(row.maximumBeforePlannedError, before.plannedHeightError);
                if (after.found) row.maximumAfterPlannedError = Mathf.Max(row.maximumAfterPlannedError, after.plannedHeightError);
                if (before.found && after.found && before.path != after.path) row.changedSupportOwners++;
                if (Mathf.Abs(delta) > RoadChangeTolerance) { row.changedHeights++; work.receipt.changedRoadHeights++; }
                if (normal > 1)
                {
                    work.receipt.normalChangeSamples++;
                    var change = new PhysicalFinding { kind = "ROAD_NORMAL_CHANGED", route = row.id, side = SideName(p.side), planned = p.planned,
                        distance = p.distance, heightDelta = delta, normalDeltaDegrees = normal, before = before, after = after };
                    if (work.normalFindings.Count < 256) work.normalFindings.Add(change);
                    else
                    {
                        work.receipt.normalFindingsTruncated = true;
                        int least = 0; for (int n = 1; n < work.normalFindings.Count; n++) if (work.normalFindings[n].normalDeltaDegrees < work.normalFindings[least].normalDeltaDegrees) least = n;
                        if (normal > work.normalFindings[least].normalDeltaDegrees) work.normalFindings[least] = change;
                    }
                }
                if (!before.found || !after.found || Mathf.Abs(delta) > RoadChangeTolerance || normal > 1)
                    AddPhysicalFinding(work, new PhysicalFinding { kind = before.found && !after.found ? "NEW_ROAD_HOLE" : !before.found ? "PREEXISTING_ROAD_HOLE" :
                        Mathf.Abs(delta) > RoadChangeTolerance ? "ROAD_HEIGHT_CHANGED" : "ROAD_NORMAL_CHANGED", route = row.id, side = SideName(p.side), planned = p.planned,
                        distance = p.distance, heightDelta = delta, normalDeltaDegrees = normal, before = before, after = after });
            }
            RouteStation previous = null; int previousIndex = -1;
            for (int i = 0; i < work.stations.Count; i++)
            {
                var station = work.stations[i]; var row = work.receipt.routes[station.route]; int at = i * 3;
                float bLong = 0, aLong = 0, bCross = 0, aCross = 0;
                if (previous != null && previous.route == station.route)
                {
                    if (work.before[at].found && work.before[previousIndex * 3].found) bLong = PhysicalDegrees(work.before[at].point.y - work.before[previousIndex * 3].point.y, station.distance - previous.distance);
                    if (work.after[at].found && work.after[previousIndex * 3].found) aLong = PhysicalDegrees(work.after[at].point.y - work.after[previousIndex * 3].point.y, station.distance - previous.distance);
                }
                if (work.before[at + 1].found && work.before[at + 2].found) bCross = PhysicalDegrees(work.before[at + 1].point.y - work.before[at + 2].point.y, row.width);
                if (work.after[at + 1].found && work.after[at + 2].found) aCross = PhysicalDegrees(work.after[at + 1].point.y - work.after[at + 2].point.y, row.width);
                bool bBad = bLong > row.gradeLimit + PhysicalGradeTolerance || bCross > row.gradeLimit + PhysicalGradeTolerance || work.before[at].found && work.before[at].plannedHeightError > 1;
                bool aBad = aLong > row.gradeLimit + PhysicalGradeTolerance || aCross > row.gradeLimit + PhysicalGradeTolerance || work.after[at].found && work.after[at].plannedHeightError > 1;
                if (bBad) { row.beforeGradeFindings++; work.receipt.preexistingGradeFindings++; }
                if (aBad) row.afterGradeFindings++;
                if (aBad && !bBad) { row.newGradeFindings++; work.receipt.newGradeFindings++; }
                row.maximumBeforeLongitudinal = Mathf.Max(row.maximumBeforeLongitudinal, bLong); row.maximumAfterLongitudinal = Mathf.Max(row.maximumAfterLongitudinal, aLong);
                row.maximumBeforeCrossSlope = Mathf.Max(row.maximumBeforeCrossSlope, bCross); row.maximumAfterCrossSlope = Mathf.Max(row.maximumAfterCrossSlope, aCross);
                if (bBad || aBad) AddPhysicalFinding(work, new PhysicalFinding { kind = aBad && !bBad ? "NEW_GRADE_FINDING" : "PREEXISTING_GRADE_FINDING", route = row.id,
                    side = "centre_and_edges", planned = station.centre, distance = station.distance, beforeLongitudinal = bLong, afterLongitudinal = aLong,
                    beforeCrossSlope = bCross, afterCrossSlope = aCross, before = work.before[at], after = work.after[at] });
                previous = station; previousIndex = i;
            }
        }
        static void ComparePhysicalTerrain(PhysicalWork work)
        {
            var rows = new List<PhysicalSurfaceRow>();
            for (int i = 0; i < work.terrainPoints.Length; i++)
            {
                if ((i & 63) == 0) CheckPhysicalBudget(work);
                var p = work.terrainPoints[i]; var cpu = ProbeTriangles(work.state, p.x, p.z);
                var row = new PhysicalSurfaceRow { position = p, cpuFound = cpu.hit, before = work.terrainBefore[i], after = work.terrainAfter[i] };
                if (!cpu.hit) work.receipt.terrainCpuMissing++;
                else
                {
                    row.cpuBeforeY = (float)cpu.before; row.cpuFinalY = (float)cpu.after;
                    var b = work.state.triangles[cpu.beforeTriangle]; var a = work.state.triangles[cpu.afterTriangle];
                    row.cpuBeforeSource = work.state.sources[b.source].row.path; row.cpuBeforeTriangle = b.triangle;
                    row.cpuAfterSource = work.state.sources[a.source].row.path; row.cpuAfterTriangle = a.triangle;
                    var va = a.a; var vb = a.b; var vc = a.c; va.y += a.delta.x; vb.y += a.delta.y; vc.y += a.delta.z;
                    row.cpuFinalNormal = Vector3.Cross(vb - va, vc - va).normalized; if (row.cpuFinalNormal.y < 0) row.cpuFinalNormal *= -1;
                    if (row.before.found) row.beforeError = Mathf.Abs(row.before.point.y - row.cpuBeforeY);
                    if (row.after.found)
                    {
                        row.finalError = Mathf.Abs(row.after.point.y - row.cpuFinalY); row.normalDeltaDegrees = Vector3.Angle(row.after.normal, row.cpuFinalNormal);
                        if (row.finalError > PhysicsCpuTolerance) work.receipt.terrainCpuHeightMismatches++;
                        if (row.normalDeltaDegrees > 1) work.receipt.terrainNormalDifferences++;
                    }
                    work.receipt.maximumCpuBeforeError = Mathf.Max(work.receipt.maximumCpuBeforeError, row.beforeError);
                    work.receipt.maximumCpuFinalError = Mathf.Max(work.receipt.maximumCpuFinalError, row.finalError);
                    work.receipt.maximumCpuNormalDelta = Mathf.Max(work.receipt.maximumCpuNormalDelta, row.normalDeltaDegrees);
                }
                if (!row.before.found) work.receipt.terrainBeforeHoles++;
                if (!row.after.found) work.receipt.terrainAfterHoles++;
                rows.Add(row);
            }
            work.receipt.terrain = rows.ToArray();
        }
        static float PhysicalDegrees(float rise, float run) => Mathf.Atan2(Mathf.Abs(rise), Mathf.Max(.000001f, run)) * Mathf.Rad2Deg;
        static void ComparePhysicalBoundaries(PhysicalWork work)
        {
            var rows = new List<PhysicalBoundaryRow>();
            for (int i = 0; i < work.boundaryIssues.Length; i++)
            {
                var issue = work.boundaryIssues[i]; var before = work.boundaryBefore[i]; var after = work.boundaryAfter[i];
                rows.Add(new PhysicalBoundaryRow { generatedProbeClassification = issue.kind, position = issue.position,
                    cpuReferenceFound = issue.hits > 0, runtimeSamplerFound = issue.runtimeHit, before = before, after = after });
                if (!before.found) work.receipt.rawBoundaryBeforeHoles++;
                if (!after.found) work.receipt.rawBoundaryAfterHoles++;
                if (before.found && !after.found) work.receipt.rawBoundaryNewHoles++;
            }
            work.receipt.rawBoundaryDiagnosticSamples = rows.Count; work.receipt.rawBoundaryDiagnostics = rows.ToArray();
        }
        static string SideName(int side) => side == 0 ? "centre" : side == 1 ? "left" : "right";
        static void AddPhysicalFinding(PhysicalWork work, PhysicalFinding finding)
        {
            if (work.findings.Count < PhysicalFindingLimit) { work.findings.Add(finding); return; }
            // Retain new regressions even when a preexisting problem consumed the cap.
            bool regression = finding.kind.StartsWith("NEW_", StringComparison.Ordinal) || finding.kind == "ROAD_HEIGHT_CHANGED";
            if (!regression) return;
            int replace = work.findings.FindLastIndex(v => v.kind.StartsWith("PREEXISTING_", StringComparison.Ordinal));
            if (replace >= 0) work.findings[replace] = finding;
        }
        static void CheckPhysicalBudget(PhysicalWork work)
        { if (work.timer.Elapsed.TotalMilliseconds > PhysicalWorkBudgetMilliseconds) throw new TimeoutException("Physical audit reached its 24s work budget; preview collider restoration is still mandatory."); }
    }
}
