using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactNavigation
    {
        const float SeamMaxXZ = 1.75f, SeamMaxY = 4f, SeamMaxCrossing = 3f;
        static string SeamProposalPath => Path.Combine(Output, "navigation_seam_proposal.json");
        static string SeamAppliedPath => Path.Combine(Output, "navigation_seam_repairs.json");
        [Serializable] sealed class SeamRepairRow
        {
            public string path, status, finding, startSurface, endSurface;
            public Vector3 beforeStart, beforeEnd, mappedStart, mappedEnd, proposedStart, proposedEnd, startAnchor, endAnchor;
            public bool validated, applied, crossingSupported, approachPathsComplete, completeChainAfterApply, ownLinkWitness;
            public float crossingMetres, maximumXZCorrection, maximumYCorrection;
            public int candidateAttempts;
            public List<SeamStageCounter> stageCounters = new List<SeamStageCounter>();
            public List<string> failureDetails = new List<string>(), blockedColliderPaths = new List<string>();
        }
        [Serializable] sealed class SeamStageCounter { public string stage; public int passed, failed; }
        [Serializable] sealed class SeamRepairReceipt
        {
            public string status, utc, sceneHash, sceneHashAfter, sourceSceneHash, progressHash, navDataHash, proposalHash;
            public string scope = "Only derived endpoints of the original 28 compact outdoor escort seams. No terrain, NavData, transforms, link IDs, agent/width/cost/direction/activation metadata or source assets changed. Repairs require actual owner-isolated NavData detail-sample proof with finally-restored registrations, same-side cell bounds, grounded approaches, short supported crossing and an applied chain witnessing that exact link. Capsule clearance covers static nonterrain obstacles and excludes dynamic actors and Terrain_ surfaces; it does not certify terrain overhang clearance. Unchanged links retain their prior audit scope. Native escort traversal remains unverified.";
            public int links, candidates, validated, applied, retainedFindings;
            public bool sourceUnchanged, navDataUnchanged, nativeTraversalVerified;
            public SeamRepairRow[] rows;
        }
        sealed class SeamQuery
        {
            public Progress progress;
            public MeshCollider[] terrain;
            public NavMeshSurface[] surfaces;
            public NavMeshLink[] links;
            public Dictionary<string, SeamOwnerProof> ownerProofs = new Dictionary<string, SeamOwnerProof>();
            public Collider[] overlaps = new Collider[128];
            public SeamRepairRow diagnosticRow;

        }
        static bool SeamTrace(SeamQuery q, string stage, bool passed, Vector3 at, string detail = null)
        {
            var row = q.diagnosticRow; if (row == null) return passed;
            var count = row.stageCounters.FirstOrDefault(c => c.stage == stage);
            if (count == null) { count = new SeamStageCounter { stage = stage }; row.stageCounters.Add(count); }
            if (passed) count.passed++; else
            {
                count.failed++;
                if (count.failed <= 2 && row.failureDetails.Count < 28) row.failureDetails.Add(stage + " at " + at.ToString("R") + ": " + detail);
            }
            return passed;
        }
        sealed class SeamOwnerProof { public bool valid; public string detail; }
        static readonly FieldInfo SeamDataInstanceField = typeof(NavMeshSurface).GetField("m_NavMeshDataInstance", BindingFlags.Instance | BindingFlags.NonPublic);
        static bool SeamRegistered(NavMeshSurface surface)
        {
            if (SeamDataInstanceField == null) throw new InvalidOperationException("Installed NavMeshSurface registration field differs; no isolation attempted.");
            return ((NavMeshDataInstance)SeamDataInstanceField.GetValue(surface)).valid;
        }
        static bool SeamInsideXZ(Vector3 p, Bounds bounds, float margin = 0f) => p.x >= bounds.min.x + margin && p.x <= bounds.max.x - margin && p.z >= bounds.min.z + margin && p.z <= bounds.max.z - margin;
        static float SeamXZ(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        static Bounds SeamBounds(SurfaceRecord s) => new Bounds(s.newRoot + s.newCenter, s.newSize);
        static string SeamNavHash(Progress p)
        {
            var rows = p.surfaces.OrderBy(s => s.path, StringComparer.Ordinal).Select(s => s.path + "|" + s.targetData + "|" + Hash(AssetFile(s.targetData)));
            using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", rows)))).Replace("-", "").ToLowerInvariant();
        }
        static SeamQuery SeamContext()
        {
            RequireCompact(true); RequireCurrentBake();
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the current compact scene before proposing/applying seam repairs.");
            var p = Load();
            foreach (var s in p.surfaces)
            {
                var actual = Find<NavMeshSurface>(s.path);
                if (actual.navMeshData == null || EditorUtility.IsDirty(actual.navMeshData) || AssetDatabase.GetAssetPath(actual.navMeshData) != s.targetData || !actual.isActiveAndEnabled ||
                    Vector3.Distance(actual.transform.position, s.newRoot) > .001f || Vector3.Distance(actual.center, s.newCenter) > .001f || Vector3.Distance(actual.size, s.newSize) > .001f)
                    throw new InvalidOperationException("Owning surface differs from the completed compact bake: " + s.path);
            }
            var surfaces = SceneComponents<NavMeshSurface>().ToArray(); var links = SceneComponents<NavMeshLink>().ToArray();
            if (surfaces.Length != ExpectedSurfaces || links.Length != ExpectedLinks || surfaces.Any(s => !SeamRegistered(s))) throw new InvalidOperationException("Exactly20 registered compact surfaces and28 links are required before temporary owner isolation.");
            return new SeamQuery { progress = p, surfaces = surfaces, links = links, terrain = SceneComponents<MeshCollider>().Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && c.name.StartsWith("Terrain_", StringComparison.Ordinal) && Hierarchy(c.transform).StartsWith("WorldMacro_AuthoredGeography/01_GlobalTerrain_IndependentOfRoads/", StringComparison.Ordinal)).ToArray() };
        }
        static bool SeamFloor(SeamQuery q, Vector3 expected, out Vector3 floor)
        {
            floor = default; bool found = false; float best = float.PositiveInfinity;
            var ray = new Ray(expected + Vector3.up * SeamMaxY, Vector3.down);
            foreach (var c in q.terrain)
            {
                if (!SeamInsideXZ(expected, c.bounds, -.001f) || !c.Raycast(ray, out var hit, SeamMaxY * 2)) continue;
                float error = Mathf.Abs(hit.point.y - expected.y);
                if (error < best) { best = error; floor = hit.point; found = true; }
            }
            return SeamTrace(q, "floor", found && best <= SeamMaxY, expected, "No Terrain_ floor inside unchanged +/-4m ray window; closest delta=" + best.ToString("R"));
        }
        static bool SeamDynamic(Transform t) => t.GetComponentInParent<NavMeshAgent>() != null || t.GetComponentInParent<CharacterController>() != null || t.GetComponentInParent<WorldMacroPalanquinController>() != null;
        static bool SeamClear(SeamQuery q, Vector3 ground)
        {
            int count = Physics.OverlapCapsuleNonAlloc(ground + Vector3.up * .3f, ground + Vector3.up * 1.4f, .28f, q.overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count == q.overlaps.Length) return SeamTrace(q, "clearance", false, ground, "128-collider nonalloc buffer full; fail closed.");
            for (int i = 0; i < count; i++)
            {
                var c = q.overlaps[i];
                if (c != null && c.gameObject.scene == SceneManager.GetActiveScene() && !q.terrain.Contains(c) && !SeamDynamic(c.transform))
                {
                    string path = Hierarchy(c.transform);
                    if (q.diagnosticRow != null && q.diagnosticRow.blockedColliderPaths.Count < 8 && !q.diagnosticRow.blockedColliderPaths.Contains(path)) q.diagnosticRow.blockedColliderPaths.Add(path);
                    return SeamTrace(q, "clearance", false, ground, path + " [" + c.GetType().Name + "] bounds min=" + c.bounds.min.ToString("R") + " max=" + c.bounds.max.ToString("R"));
                }
            }
            return SeamTrace(q, "clearance", true, ground);
        }
        static bool SeamGroundConnection(SeamQuery q, Vector3 a, Vector3 b)
        {
            if (!SeamTrace(q, "crossing_length", Vector3.Distance(a, b) <= SeamMaxCrossing + .001f, a, "length=" + Vector3.Distance(a, b).ToString("R") + ", maximum=3m")) return false;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / .2f)); Vector3 previous = a;
            for (int i = 0; i <= steps; i++)
            {
                var expected = Vector3.Lerp(a, b, (float)i / steps);
                if (!SeamFloor(q, expected, out var floor)) return SeamTrace(q, "crossing", false, expected, "Floor stage failed.");
                if (!SeamTrace(q, "crossing_floor_line", Mathf.Abs(floor.y - expected.y) <= .15f, expected, "ground=" + floor.y.ToString("R") + ", lineDelta=" + Mathf.Abs(floor.y - expected.y).ToString("R") + ", maximum=.15m")) return false;
                if (!SeamClear(q, floor)) return SeamTrace(q, "crossing", false, expected, "Clearance stage failed.");
                if (i > 0 && !SeamTrace(q, "crossing_step", Mathf.Abs(floor.y - previous.y) <= .16f, expected, "verticalStep=" + Mathf.Abs(floor.y - previous.y).ToString("R") + ", maximum=.16m")) return false;
                previous = floor;
            }
            return SeamTrace(q, "crossing", true, a);
        }
        static bool SeamOwnedPoint(SeamQuery q, SurfaceRecord owner, Vector3 point)
        {
            var bounds = SeamBounds(owner);
            if (!SeamTrace(q, "owned_point_bounds", SeamInsideXZ(point, bounds, .02f), point, owner.path)) return false;
            var other = q.progress.surfaces.FirstOrDefault(s => s.path != owner.path && SeamInsideXZ(point, SeamBounds(s), .02f) && Mathf.Abs(point.y - SeamBounds(s).center.y) <= s.newSize.y * .5f);
            if (!SeamTrace(q, "owned_unique_surface", other == null, point, "Owner=" + owner.path + ", also inside=" + other?.path)) return false;
            string key = owner.path + "|" + point.ToString("R");
            if (q.ownerProofs.TryGetValue(key, out var cached)) return SeamTrace(q, "owned_nav_data", cached.valid, point, cached.detail);
            // CalculateTriangulation omits the detail mesh used by SamplePosition and its
            // coarse vertices can overshoot nominal volume bounds by voxel quantization.
            // Prove ownership against the actual one registered NavData instead of those
            // invalid coarse-triangle/height assumptions. The physical tolerances stay fixed.
            var actual = q.surfaces.Single(s => Hierarchy(s.transform) == owner.path);
            var registered = q.surfaces.Select(SeamRegistered).ToArray();
            var proof = new SeamOwnerProof();
            try
            {
                for (int i = 0; i < q.surfaces.Length; i++) if (registered[i]) q.surfaces[i].RemoveData();
                if (NavMesh.CalculateTriangulation().indices.Length != 0) throw new InvalidOperationException("An untracked NavData remains after removing all20 surfaces; owner proof refused.");
                actual.AddData();
                if (!SeamRegistered(actual) || q.surfaces.Any(s => s != actual && SeamRegistered(s))) throw new InvalidOperationException("Owner NavData registration isolation was not exact.");
                var filter = new NavMeshQueryFilter { agentTypeID = actual.agentTypeID, areaMask = NavMesh.AllAreas };
                bool sampled = NavMesh.SamplePosition(point, out var hit, .4f, filter);
                bool samePoint = sampled && Vector3.Distance(point, hit.position) <= .03f;
                bool grounded = sampled && SeamFloor(q, hit.position, out var floor) && Mathf.Abs(floor.y - hit.position.y) <= .2f;
                proof.valid = sampled && samePoint && grounded && SeamInsideXZ(hit.position, bounds, .02f);
                proof.detail = owner.path + "; exact owning NavData=" + owner.targetData + "; sampled=" + sampled + "; sameDetailPointWithin.03m=" + samePoint + "; currentTerrainWithin.2m=" + grounded + "; ownerHit=" + hit.position.ToString("R");
            }
            finally
            {
                // Restoration also runs if collection, sampling or logging fails. Never save
                // assets or edit component fields while their native registrations are isolated.
                Exception restoration = null;
                for (int i = 0; i < q.surfaces.Length; i++)
                {
                    try { if (registered[i] && !SeamRegistered(q.surfaces[i])) q.surfaces[i].AddData(); else if (!registered[i] && SeamRegistered(q.surfaces[i])) q.surfaces[i].RemoveData(); }
                    catch (Exception exception) { restoration = restoration ?? exception; }
                }
                foreach (var link in q.links)
                {
                    try { if (link.isActiveAndEnabled) link.UpdateLink(); }
                    catch (Exception exception) { restoration = restoration ?? exception; }
                }
                if (restoration != null || q.surfaces.Where((s, i) => SeamRegistered(s) != registered[i]).Any()) throw new InvalidOperationException("Owner diagnostic could not restore all prior NavData/link registrations.", restoration);
            }
            q.ownerProofs[key] = proof;
            return SeamTrace(q, "owned_nav_data", proof.valid, point, proof.detail);
        }
        static bool SeamEndpoint(SeamQuery q, SurfaceRecord owner, NavMeshQueryFilter filter, Vector3 candidate, Vector3 mapped, out Vector3 result)
        {
            result = default;
            if (!SeamTrace(q, "endpoint_bounds", SeamInsideXZ(candidate, SeamBounds(owner), .02f), candidate, owner.path)) return false;
            if (!SeamFloor(q, candidate, out var floor)) return false;
            if (!NavMesh.SamplePosition(floor, out var nav, .4f, filter))
            {
                string detail = owner.path + "; no NavMesh within unchanged .4m radius at actual floor " + floor.ToString("R");
                if (q.diagnosticRow != null && (q.diagnosticRow.stageCounters.FirstOrDefault(c => c.stage == "nav_sample")?.failed ?? 0) < 2 && NavMesh.SamplePosition(floor, out var diagnostic, 2f, filter))
                    detail += "; DIAGNOSTIC ONLY nearest within2m=" + diagnostic.position.ToString("R") + ", delta=" + Vector3.Distance(floor, diagnostic.position).ToString("R");
                return SeamTrace(q, "nav_sample", false, floor, detail);
            }
            SeamTrace(q, "nav_sample", true, floor);
            if (!SeamTrace(q, "nav_ground_delta", Mathf.Abs(nav.position.y - floor.y) <= .2f, nav.position, "floor=" + floor.ToString("R") + ", delta=" + Mathf.Abs(nav.position.y - floor.y).ToString("R"))) return false;
            result = nav.position;
            if (!SeamTrace(q, "endpoint_correction", SeamXZ(result, mapped) <= SeamMaxXZ && Mathf.Abs(result.y - mapped.y) <= SeamMaxY, result, "XZ=" + SeamXZ(result, mapped).ToString("R") + ", Y=" + Mathf.Abs(result.y - mapped.y).ToString("R"))) return false;
            if (!SeamOwnedPoint(q, owner, result) || !SeamClear(q, floor)) return false;
            return SeamTrace(q, "endpoint", true, result);
        }
        static bool SeamApproach(SeamQuery q, SurfaceRecord owner, NavMeshQueryFilter filter, Vector3 endpoint, Vector3 inward, out Vector3 anchor)
        {
            anchor = default; var cross = new Vector3(-inward.z, 0, inward.x);
            foreach (float tangent in new[] { 0f, -.5f, .5f })
            {
                var test = endpoint + inward * 2f + cross * tangent;
                if (!SeamEndpoint(q, owner, filter, test, test, out var candidate)) { SeamTrace(q, "approach_endpoint", false, test, owner.path); continue; }
                if (!SeamGroundConnection(q, candidate, endpoint)) { SeamTrace(q, "approach_ground", false, test, owner.path); continue; }
                var path = new NavMeshPath();
                bool complete = NavMesh.CalculatePath(candidate, endpoint, filter, path) && path.status == NavMeshPathStatus.PathComplete;
                if (!SeamTrace(q, "approach_path", complete, candidate, owner.path + ", status=" + path.status)) continue;
                if (!SeamTrace(q, "approach_path_bounds", !path.corners.Any(p => !SeamInsideXZ(p, SeamBounds(owner), .01f)), candidate, owner.path + ", corners=" + string.Join("/", path.corners.Select(p => p.ToString("R"))))) continue;
                float length = 0; for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                if (!SeamTrace(q, "approach_path_length", length <= 3.5f, candidate, "length=" + length.ToString("R"))) continue; anchor = candidate; return SeamTrace(q, "approach", true, anchor);
            }
            return SeamTrace(q, "approach", false, endpoint, owner.path + ": all three 2m-inward anchor candidates failed earlier stages.");
        }
        static SurfaceRecord SeamOwner(Progress p, Vector3 mapped)
        {
            var source = Mapping.Inverse(mapped);
            var matches = p.surfaces.Where(s => s.path.StartsWith("Demo_EscortNavigation/Cell_", StringComparison.Ordinal) && new Bounds(s.oldRoot + s.oldCenter, s.oldSize).Contains(source)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        static bool SeamAxes(SurfaceRecord a, SurfaceRecord b, out Vector3 axis)
        {
            axis = default; var d = (b.oldRoot + b.oldCenter) - (a.oldRoot + a.oldCenter); d.y = 0;
            if (Mathf.Abs(Mathf.Abs(d.x) - 128f) < .1f && Mathf.Abs(d.z) < .1f) axis = Vector3.right * Mathf.Sign(d.x);
            else if (Mathf.Abs(Mathf.Abs(d.z) - 128f) < .1f && Mathf.Abs(d.x) < .1f) axis = Vector3.forward * Mathf.Sign(d.z);
            return axis != Vector3.zero;
        }
        static bool SeamRevalidate(SeamQuery q, SeamRepairRow row, NavMeshLink link, out string error)
        {
            error = "Constrained endpoint/approach/ground revalidation failed.";
            var a = q.progress.surfaces.Single(s => s.path == row.startSurface); var b = q.progress.surfaces.Single(s => s.path == row.endSurface);
            var filter = new NavMeshQueryFilter { agentTypeID = link.agentTypeID, areaMask = NavMesh.AllAreas };
            if (!SeamAxes(a, b, out var axis) || Vector3.Dot(row.proposedEnd - row.proposedStart, axis) <= .04f ||
                !SeamEndpoint(q, a, filter, row.proposedStart, row.mappedStart, out var pa) || !SeamEndpoint(q, b, filter, row.proposedEnd, row.mappedEnd, out var pb) ||
                Vector3.Distance(pa, row.proposedStart) > .03f || Vector3.Distance(pb, row.proposedEnd) > .03f || !SeamGroundConnection(q, pa, pb) ||
                !SeamApproach(q, a, filter, pa, -axis, out var aa) || !SeamApproach(q, b, filter, pb, axis, out var bb) ||
                Vector3.Distance(aa, row.startAnchor) > .03f || Vector3.Distance(bb, row.endAnchor) > .03f) return false;
            error = null; return true;
        }
        static string SeamWrite(string path, SeamRepairReceipt receipt)
        {
            string json = JsonUtility.ToJson(receipt, true), temporary = path + ".tmp"; File.WriteAllText(temporary, json);
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); return json;
        }
        static bool SeamOrderedLinkWitness(Vector3[] corners, Vector3 start, Vector3 end)
        {
            // A complete path could otherwise silently use a neighboring seam less than a
            // metre away. Distinct ordered entry/exit corners must witness this exact link.
            for (int i = 0; i + 1 < corners.Length; i++)
                if (Vector3.Distance(corners[i], start) <= .12f && Vector3.Distance(corners[i + 1], end) <= .12f) return true;
            return false;
        }
        public static string ProposeSeamRepairs()
        {
            var q = SeamContext(); var p = q.progress;
            var receipt = new SeamRepairReceipt { status = "DRY_RUN_PROPOSAL_NO_SCENE_CHANGE", utc = DateTime.UtcNow.ToString("O"), sceneHash = Hash(AssetFile(ScenePath)), sourceSceneHash = Hash(AssetFile(WorldMacroCompactAuthoring.SourceScene)), progressHash = Hash(ProgressPath), navDataHash = SeamNavHash(p), links = p.links.Length };
            var rows = new List<SeamRepairRow>();
            foreach (var record in p.links)
            {
                var link = Find<NavMeshLink>(record.path); var proof = link.GetComponent<DemoEscortNavigationSeam>();
                var row = new SeamRepairRow { path = record.path, status = "UNCHANGED" }; rows.Add(row); q.diagnosticRow = row;
                if (record.externalStart || record.externalEnd || proof == null || !link.isActiveAndEnabled || !link.activated) { row.status = "RETAINED_FINDING"; row.finding = "External endpoints, missing proof, or inactive link; no metadata change allowed."; continue; }
                RequireUnitTransform(link.transform);
                row.beforeStart = link.transform.TransformPoint(link.startPoint); row.beforeEnd = link.transform.TransformPoint(link.endPoint);
                row.mappedStart = link.transform.TransformPoint(record.newStart); row.mappedEnd = link.transform.TransformPoint(record.newEnd);
                var filter = new NavMeshQueryFilter { agentTypeID = link.agentTypeID, areaMask = NavMesh.AllAreas };
                if (Sample(row.beforeStart, filter, .6f, out var oldA) && Sample(row.beforeEnd, filter, .6f, out var oldB) && Complete(oldA.position, oldB.position, filter) && proof.Matches(row.beforeStart, row.beforeEnd))
                { row.finding = "Existing endpoint/path audit passes; unchanged link receives no new physical continuity certification."; continue; }
                receipt.candidates++;
                var a = SeamOwner(p, row.mappedStart); var b = SeamOwner(p, row.mappedEnd);
                if (a == null || b == null || !SeamAxes(a, b, out var axis)) { row.status = "RETAINED_FINDING"; row.finding = "Unique adjacent original source cell ownership was not proven."; continue; }
                row.startSurface = a.path; row.endSurface = b.path;
                var tangent = new Vector3(-axis.z, 0, axis.x); float best = float.PositiveInfinity;
                foreach (float lateral in new[] { 0f, -.25f, .25f, -.5f, .5f })
                foreach (float inset in new[] { 0f, .25f, .5f, .75f, 1f, 1.25f })
                {
                    if (best == 0f) continue; // Zero XZ displacement is the exact lower bound; no further native queries can improve it.
                    row.candidateAttempts++;
                    var ta = row.mappedStart - axis * inset + tangent * lateral; var tb = row.mappedEnd + axis * inset + tangent * lateral;
                    if (!SeamEndpoint(q, a, filter, ta, row.mappedStart, out var pa) || !SeamEndpoint(q, b, filter, tb, row.mappedEnd, out var pb) || !SeamTrace(q, "same_side", Vector3.Dot(pb - pa, axis) > .04f, pa, "axis separation=" + Vector3.Dot(pb - pa, axis).ToString("R")) || !SeamGroundConnection(q, pa, pb) ||
                        !SeamApproach(q, a, filter, pa, -axis, out var aa) || !SeamApproach(q, b, filter, pb, axis, out var bb)) continue;
                    float score = SeamXZ(pa, row.mappedStart) + SeamXZ(pb, row.mappedEnd);
                    if (score >= best) continue; best = score; row.proposedStart = pa; row.proposedEnd = pb; row.startAnchor = aa; row.endAnchor = bb; row.validated = true;
                }
                row.status = row.validated ? "VALIDATED_CANDIDATE" : "RETAINED_FINDING";
                row.finding = row.validated ? "Ready for explicit apply and then complete local chain verification; native traversal unverified." : "No pair met actual owning-NavData detail-sample proof, 1.75m XZ/4m Y correction, 3m clear supported crossing and 2m approach limits.";
                if (row.validated) { row.crossingSupported = row.approachPathsComplete = true; row.crossingMetres = Vector3.Distance(row.proposedStart, row.proposedEnd); row.maximumXZCorrection = Mathf.Max(SeamXZ(row.proposedStart, row.mappedStart), SeamXZ(row.proposedEnd, row.mappedEnd)); row.maximumYCorrection = Mathf.Max(Mathf.Abs(row.proposedStart.y - row.mappedStart.y), Mathf.Abs(row.proposedEnd.y - row.mappedEnd.y)); }
            }
            receipt.rows = rows.ToArray(); receipt.validated = rows.Count(r => r.validated); receipt.retainedFindings = rows.Count(r => r.status == "RETAINED_FINDING"); receipt.sourceUnchanged = receipt.sourceSceneHash == Hash(AssetFile(WorldMacroCompactAuthoring.SourceScene)) && SourceHashesMatch(p); receipt.navDataUnchanged = receipt.navDataHash == SeamNavHash(p);
            if (receipt.sceneHash != Hash(AssetFile(ScenePath)) || SceneManager.GetActiveScene().isDirty || !receipt.sourceUnchanged || !receipt.navDataUnchanged) throw new InvalidOperationException("Dry run changed or raced a saved dependency; proposal not published.");
            return SeamWrite(SeamProposalPath, receipt);
        }
        public static string ApplySeamRepairs()
        {
            var q = SeamContext(); var p = q.progress;
            if (!File.Exists(SeamProposalPath)) throw new InvalidOperationException("Run nav-seam-propose and inspect the dry-run proposal before apply.");
            var receipt = JsonUtility.FromJson<SeamRepairReceipt>(File.ReadAllText(SeamProposalPath)); string proposalHash = Hash(SeamProposalPath), sceneHash = Hash(AssetFile(ScenePath));
            if (File.Exists(SeamAppliedPath))
            {
                var prior = JsonUtility.FromJson<SeamRepairReceipt>(File.ReadAllText(SeamAppliedPath));
                if (prior.proposalHash == proposalHash && prior.sceneHashAfter == sceneHash && prior.progressHash == Hash(ProgressPath) && prior.navDataHash == SeamNavHash(p)) return File.ReadAllText(SeamAppliedPath);
            }
            if (receipt.status != "DRY_RUN_PROPOSAL_NO_SCENE_CHANGE" || receipt.sceneHash != sceneHash || receipt.progressHash != Hash(ProgressPath) || receipt.navDataHash != SeamNavHash(p) || receipt.sourceSceneHash != Hash(AssetFile(WorldMacroCompactAuthoring.SourceScene)) || receipt.rows == null || receipt.rows.Length != ExpectedLinks || receipt.rows.Select(r => r.path).Distinct().Count() != ExpectedLinks || receipt.rows.Any(r => !p.links.Any(l => l.path == r.path)))
                throw new InvalidOperationException("Proposal/source/scene/current-bake dependencies changed; make a new proposal.");
            foreach (var row in receipt.rows.Where(r => r.validated))
            {
                var link = Find<NavMeshLink>(row.path); var proof = link.GetComponent<DemoEscortNavigationSeam>();
                string error = "Link identity, activation or existing endpoint changed.";
                if (link.startTransform != null || link.endTransform != null || proof == null || !link.isActiveAndEnabled || !link.activated ||
                    Vector3.Distance(link.transform.TransformPoint(link.startPoint), row.beforeStart) > .001f || Vector3.Distance(link.transform.TransformPoint(link.endPoint), row.beforeEnd) > .001f || !SeamRevalidate(q, row, link, out error))
                { row.status = "RETAINED_FINDING"; row.finding = "Apply preflight failed: " + error; continue; }
                var oldStart = link.startPoint; var oldEnd = link.endPoint; var oldProofStart = proof.StartWorld; var oldProofEnd = proof.EndWorld;
                bool committed = false;
                try
                {
                    link.startPoint = link.transform.InverseTransformPoint(row.proposedStart); link.endPoint = link.transform.InverseTransformPoint(row.proposedEnd); proof.StartWorld = row.proposedStart; proof.EndWorld = row.proposedEnd; link.UpdateLink();
                    var filter = new NavMeshQueryFilter { agentTypeID = link.agentTypeID, areaMask = NavMesh.AllAreas }; var chain = new NavMeshPath();
                    bool complete = NavMesh.CalculatePath(row.startAnchor, row.endAnchor, filter, chain) && chain.status == NavMeshPathStatus.PathComplete; float length = 0;
                    var a = p.surfaces.Single(s => s.path == row.startSurface); var b = p.surfaces.Single(s => s.path == row.endSurface);
                    for (int i = 0; i < chain.corners.Length; i++) { complete &= SeamInsideXZ(chain.corners[i], SeamBounds(a), -.03f) || SeamInsideXZ(chain.corners[i], SeamBounds(b), -.03f); if (i > 0) length += Vector3.Distance(chain.corners[i - 1], chain.corners[i]); }
                    row.ownLinkWitness = SeamOrderedLinkWitness(chain.corners, row.proposedStart, row.proposedEnd);
                    complete &= row.ownLinkWitness && length <= row.crossingMetres + 6f && SeamGroundConnection(q, row.proposedStart, row.proposedEnd) && proof.Matches(row.proposedStart, row.proposedEnd);
                    if (!complete) { row.status = "RETAINED_FINDING"; row.finding = "Local chain did not witness this exact link inside the owning two-cell corridor; endpoints restored."; continue; }
                    EditorUtility.SetDirty(link); EditorUtility.SetDirty(proof); row.applied = row.completeChainAfterApply = committed = true; row.status = "APPLIED_VALIDATED_LOCAL_CHAIN";
                }
                catch (Exception exception) { row.status = "RETAINED_FINDING"; row.finding = "Apply verification threw; endpoints restored: " + exception.Message; }
                finally
                {
                    if (!committed) { link.startPoint = oldStart; link.endPoint = oldEnd; proof.StartWorld = oldProofStart; proof.EndWorld = oldProofEnd; link.UpdateLink(); }
                }
            }
            receipt.proposalHash = proposalHash; receipt.applied = receipt.rows.Count(r => r.applied); receipt.retainedFindings = receipt.rows.Count(r => r.status == "RETAINED_FINDING"); receipt.status = receipt.retainedFindings == 0 ? "APPLIED_LOCAL_SEAMS_REQUIRES_NAV_AUDIT" : "APPLIED_LOCAL_SEAMS_WITH_RETAINED_FINDINGS";
            if (receipt.applied > 0) SaveScene(); receipt.sceneHashAfter = Hash(AssetFile(ScenePath)); receipt.utc = DateTime.UtcNow.ToString("O"); receipt.sourceUnchanged = receipt.sourceSceneHash == Hash(AssetFile(WorldMacroCompactAuthoring.SourceScene)) && SourceHashesMatch(p); receipt.navDataUnchanged = receipt.navDataHash == SeamNavHash(p);
            if (!receipt.sourceUnchanged || !receipt.navDataUnchanged) throw new InvalidOperationException("Source or NavData hash changed during endpoint-only apply.");
            return SeamWrite(SeamAppliedPath, receipt);
        }
    }
}
