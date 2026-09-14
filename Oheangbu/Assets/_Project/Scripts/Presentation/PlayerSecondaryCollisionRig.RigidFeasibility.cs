using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Geometry = Oheangbu.Presentation.PlayerSecondaryCollisionGeometry;

namespace Oheangbu.Presentation
{
    public sealed partial class PlayerSecondaryCollisionRig
    {
        [Serializable] public sealed class RigidPoseSearchRequest
        {
            public Transform Pivot;
            public Quaternion RestLocalRotation;
            public Vector3 CurrentAngleRadians;
            public float MaximumSwingDegrees;
            public string SettingsProvenance, SettingsSha256;
            // Optional investigations only. Never substitutes the original limit.
            public float[] ProposalMaximumSwingDegrees = Array.Empty<float>();
        }
        [Serializable] public sealed class RigidPoseSearchSettings
        {
            public int SphereDirections = 256, RadialShells = 6, GridAxisSamples = 9;
            public int RefinementSeeds = 8, RefinementLevels = 8;
        }
        [Serializable] public sealed class RigidPoseCandidate
        {
            public Vector3 angleRadians;
            public float angleDegrees, surfacePenetrationMeters, runtimeMarginRemainingMeters;
            public bool triangleSurfaceOverlap, runtimeClearanceSatisfied;
            public int capsuleIndex = -1, surfaceIndex = -1, originalTriangleIndex = -1;
            public string capsuleName, sourceGeometrySha256;
        }
        [Serializable] public sealed class RigidPoseSearchDomain
        {
            public string classification, proposalScope;
            public bool completed, withinOriginalLimit, initialAngleWithinSearchBall, bestWithinOriginalBall, foundNoProxySurfaceOverlap, foundRuntimeClearance;
            public float maximumSwingDegrees, maximumRadians, maximumSampledAngleDegrees;
            public float bestUninflatedBodyProxyPenetrationMeters;
            public bool bestFullTriangleWitnessVerified;
            public int evaluatedUniqueRotations, deterministicCoarseSamples, refinementSamples;
            public long triangleDistanceTests, bestIndependentTriangleTests;
            public double elapsedMilliseconds;
            public RigidPoseCandidate initial, best;
            public RigidSurfaceWitness[] bestPerSurface = Array.Empty<RigidSurfaceWitness>();
            public string bestPerSurfaceScope = "Every original surface/LOD is retained in the unchanged union witness. LOD indices come from actual renderer membership, never array order; unknown membership stays explicit. Raw signed clearance, positive overlap and existing 0.1mm support margin are distinct.";
        }
        [Serializable] public sealed class RigidSurfaceWitness
        {
            public int surfaceIndex, vertices, triangles, bodyCapsules;
            public string renderer, rendererHierarchyPath, sourceGuid, sourceFileSha256, sourceGeometrySha256, authoredGeometrySha256;
            public long sourceLocalId, fullTriangleTests;
            public bool lodMembershipVerified;
            public int[] lodIndices = Array.Empty<int>();
            public string lodGroupHierarchyPath;
            public float maximumPenetrationMeters, maximumSupportMarginPenetrationMeters, minimumSignedClearanceMeters;
            public int worstCapsuleIndex = -1, worstOriginalTriangleIndex = -1;
            public string worstCapsuleName;
            public Vector3 worstSurfaceWorld, worstAxisWorld;
        }
        [Serializable] public sealed class RigidSourceIdentity
        {
            public string renderer, sourceGuid, sourceFileSha256, sourceGeometrySha256, authoredGeometrySha256;
            public long sourceLocalId;
            public int vertices, triangles;
        }
        [Serializable] public sealed class RigidPartFeasibility
        {
            public string name, error, settingsProvenance, settingsSha256;
            public bool completed, restored, initialMatchesSuppliedSpringState, fixedPositionScalePreserved;
            public Quaternion restLocalRotation, initialLocalRotation;
            public Vector3 initialAngleRadians, initialLocalPosition, initialLocalScale;
            public int sourceVertices, sourceTriangles;
            public RigidSourceIdentity[] sources;
            public RigidPoseSearchDomain originalLimit;
            public RigidPoseSearchDomain[] proposals = Array.Empty<RigidPoseSearchDomain>();
        }
        [Serializable] public sealed class RigidCapsuleSnapshot
        {
            public string name;
            public Vector3 startWorld, endWorld;
            public float radiusMeters;
        }
        [Serializable] public sealed class RigidPoseFeasibilityReport
        {
            public string status = "WAIT", error;
            public bool completed, restored, bodySnapshotUnchanged, springHistoryUntouched = true;
            public int frameCounter, bodyCapsules, requestedParts;
            public double elapsedMilliseconds;
            public RigidPoseSearchSettings searchSettings;
            public RigidCapsuleSnapshot[] capsules;
            public RigidPartFeasibility[] parts;
            public string scope = "Diagnostic sampled search only, no production Constrain changes. Existing original full triangles/all authored LOD surfaces against the complete latched BeginFrame body capsules. Fixed pivot position/scale, rest rotation, source geometry and original swing ball. A found non-overlap rotation is a witness; failing to find one is NOT proof the swing limit is inadequate or a global optimum. No anatomy/continuous-time/closed-solid/RIG_PASS claim.";
        }
        private sealed class RigidSearchState
        {
            public PartState Part;
            public RigidPoseSearchRequest Request;
            public float MaximumRadians;
            public Dictionary<Vector3, RigidPoseCandidate> Seen = new Dictionary<Vector3, RigidPoseCandidate>();
            public List<RigidPoseCandidate> Best = new List<RigidPoseCandidate>();
            public RigidPoseSearchDomain Result;
            public int SeedCount;
        }

        /// <summary>Explicit blocking audit on a disposable fixture/current static pose.
        /// Query uses temporary PartState/measurements so live spring/cache/history are untouched.</summary>
        public RigidPoseFeasibilityReport AuditRigidPoseFeasibility(RigidPoseSearchRequest[] requests,
            RigidPoseSearchSettings settings = null)
        {
            var report = new RigidPoseFeasibilityReport(); long started = Stopwatch.GetTimestamp();
            Geometry.Capsule[] scratch = null, snapshot = null;
            var saved = new List<(Transform bone, Vector3 position, Quaternion rotation, Vector3 scale)>();
            try
            {
                Need(_ready && _frameReady && string.IsNullOrEmpty(LastError), "A valid current BeginFrame is required; this audit never initializes or advances a spring.");
                Need(requests != null && requests.Length > 0, "Explicit current rigid spring requests required.");
                settings = settings ?? new RigidPoseSearchSettings(); ValidateSearchSettings(settings);
                report.searchSettings = settings; report.frameCounter = Frames; report.requestedParts = requests.Length;
                snapshot = (Geometry.Capsule[])_capsules.Clone(); scratch = (Geometry.Capsule[])_localCapsules.Clone();
                report.bodyCapsules = snapshot.Length; report.capsules = new RigidCapsuleSnapshot[snapshot.Length];
                for (int c = 0; c < snapshot.Length; c++) report.capsules[c] = new RigidCapsuleSnapshot
                    { name = _bodyCapsules[c].name, startWorld = snapshot[c].A, endWorld = snapshot[c].B, radiusMeters = snapshot[c].Radius };
                report.parts = new RigidPartFeasibility[requests.Length]; var seen = new HashSet<Transform>();
                for (int p = 0; p < requests.Length; p++)
                {
                    var request = requests[p];
                    Need(request != null && request.Pivot != null && seen.Add(request.Pivot), "Null or duplicate rigid pivot request.");
                    Need(_nodes.TryGetValue(request.Pivot, out var node) && node.Part.Binding.Rigid && node.Part.Binding.DrivenBones.Length == 1,
                        "Only explicitly owned rigid one-pivot parts may be searched.");
                    foreach (var proxy in _bodyCapsules) Need(!proxy.transform.IsChildOf(request.Pivot),
                        "Body snapshot cannot be anchored beneath the searched rigid ornament pivot.");
                    Need(Geometry.Finite(request.CurrentAngleRadians) && float.IsFinite(request.MaximumSwingDegrees) &&
                        request.MaximumSwingDegrees >= 0f && request.MaximumSwingDegrees <= 80f, "Use the original finite spring angle limit, without expansion.");
                    Need(!string.IsNullOrEmpty(request.SettingsProvenance) && !string.IsNullOrEmpty(request.SettingsSha256), "Exact settings provenance/hash is required.");
                    Need(FiniteRotation(request.RestLocalRotation), "Finite unit rest rotation required.");
                    var bone = request.Pivot; saved.Add((bone, bone.localPosition, bone.localRotation, bone.localScale));
                    var row = new RigidPartFeasibility { name = node.Part.Binding.Name, restLocalRotation = request.RestLocalRotation,
                        initialLocalRotation = bone.localRotation, initialAngleRadians = request.CurrentAngleRadians,
                        initialLocalPosition = bone.localPosition, initialLocalScale = bone.localScale,
                        settingsProvenance = request.SettingsProvenance, settingsSha256 = request.SettingsSha256,
                        sources = new RigidSourceIdentity[node.Part.Surfaces.Length] };
                    report.parts[p] = row;
                    Quaternion expected = request.RestLocalRotation * Geometry.Rotation(request.CurrentAngleRadians);
                    row.initialMatchesSuppliedSpringState = RotationDifferenceBasis(expected, bone.localRotation) <= .00001f;
                    Need(row.initialMatchesSuppliedSpringState, "Provided Angle/RestRotation does not describe the actual current rigid pose.");
                    for (int s = 0; s < row.sources.Length; s++)
                    {
                        var source = node.Part.Surfaces[s].Binding;
                        row.sources[s] = new RigidSourceIdentity { renderer = source.SourceRenderer.name, sourceGuid = source.SourceGuid,
                            sourceLocalId = source.SourceLocalId, sourceFileSha256 = source.SourceFileSha256,
                            sourceGeometrySha256 = source.SourceGeometrySha256, authoredGeometrySha256 = source.GeometrySha256,
                            vertices = source.Positions.Length, triangles = source.Triangles.Length / 3 };
                        row.sourceVertices += source.Positions.Length; row.sourceTriangles += source.Triangles.Length / 3;
                    }
                    // The diagnostic owns its trees too: even LastTriangleTests on
                    // the production tree must remain untouched after this audit.
                    var temporary = new PartState { Binding = node.Part.Binding, Surfaces = new SurfaceState[node.Part.Surfaces.Length],
                        Measurement = new PartMeasurement { name = node.Part.Binding.Name, rigid = true } };
                    for (int s = 0; s < temporary.Surfaces.Length; s++)
                    {
                        var source = node.Part.Surfaces[s].Binding;
                        var positions = (Vector3[])source.Positions.Clone();
                        temporary.Surfaces[s] = new SurfaceState { Binding = source, Positions = positions,
                            Tree = new Geometry.TriangleTree(positions, source.Triangles) };
                    }
                    try
                    {
                        row.originalLimit = SearchRigidDomain(temporary, request, request.MaximumSwingDegrees, true, settings);
                        var proposals = request.ProposalMaximumSwingDegrees ?? Array.Empty<float>();
                        row.proposals = new RigidPoseSearchDomain[proposals.Length];
                        for (int i = 0; i < proposals.Length; i++)
                        {
                            Need(float.IsFinite(proposals[i]) && proposals[i] > request.MaximumSwingDegrees && proposals[i] <= 80f,
                                "Proposal must be explicit, finite, larger than original and within the existing solver's 80-degree hard bound.");
                            row.proposals[i] = SearchRigidDomain(temporary, request, proposals[i], false, settings);
                        }
                        row.fixedPositionScalePreserved = bone.localPosition.Equals(row.initialLocalPosition) && bone.localScale.Equals(row.initialLocalScale);
                        row.completed = true;
                    }
                    finally
                    {
                        bone.localPosition = row.initialLocalPosition; bone.localRotation = row.initialLocalRotation; bone.localScale = row.initialLocalScale;
                        row.restored = bone.localPosition.Equals(row.initialLocalPosition) && bone.localRotation.Equals(row.initialLocalRotation) && bone.localScale.Equals(row.initialLocalScale);
                    }
                }
                report.completed = true; report.status = "SAMPLED_RIGID_ROTATION_FEASIBILITY_MEASURED_NOT_GLOBAL_OPTIMUM";
            }
            catch (Exception e) { report.error = e.GetBaseException().Message; report.status = "DIAGNOSTIC_FAILED_NO_FEASIBILITY_CLAIM"; }
            finally
            {
                report.restored = true;
                foreach (var s in saved)
                {
                    if (s.bone == null) { report.restored = false; continue; }
                    s.bone.localPosition = s.position; s.bone.localRotation = s.rotation; s.bone.localScale = s.scale;
                    report.restored &= s.bone.localPosition.Equals(s.position) && s.bone.localRotation.Equals(s.rotation) && s.bone.localScale.Equals(s.scale);
                }
                if (scratch != null) Array.Copy(scratch, _localCapsules, scratch.Length);
                report.bodySnapshotUnchanged = snapshot != null && snapshot.Length == _capsules.Length;
                if (report.bodySnapshotUnchanged) for (int c = 0; c < snapshot.Length; c++) report.bodySnapshotUnchanged &= Geometry.Exact(snapshot[c], _capsules[c]);
                report.elapsedMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
            }
            return report;
        }

        private RigidPoseSearchDomain SearchRigidDomain(PartState part, RigidPoseSearchRequest request,
            float degrees, bool original, RigidPoseSearchSettings settings)
        {
            long start = Stopwatch.GetTimestamp(); part.Measurement.triangleTests = 0;
            var result = new RigidPoseSearchDomain { maximumSwingDegrees = degrees, withinOriginalLimit = original,
                maximumRadians = degrees * Mathf.Deg2Rad,
                proposalScope = original ? "ORIGINAL_PROFILE_LIMIT_UNCHANGED" : "OUTSIDE_ORIGINAL_LIMIT_PROPOSAL_ONLY_NO_RUNTIME_CHANGE" };
            var search = new RigidSearchState { Part = part, Request = request, MaximumRadians = degrees * Mathf.Deg2Rad,
                Result = result, SeedCount = settings.RefinementSeeds };
            // Include the current pose and rest; production may already be in a local basin.
            EvaluateRigidSample(search, Vector3.zero); EvaluateRigidSample(search, request.CurrentAngleRadians);
            result.initial = ReadRigidCandidate(part, request.CurrentAngleRadians, QueryAtAngle(part, request, request.CurrentAngleRadians));
            result.initialAngleWithinSearchBall = request.CurrentAngleRadians.sqrMagnitude <= search.MaximumRadians * search.MaximumRadians;
            foreach (var angle in DeterministicRigidSamples(search.MaximumRadians, settings)) EvaluateRigidSample(search, angle);
            result.deterministicCoarseSamples = search.Seen.Count;
            var seeds = search.Best.ToArray();
            foreach (var seed in seeds)
            {
                Vector3 center = seed.angleRadians; float step = search.MaximumRadians / 4f;
                for (int level = 0; level < settings.RefinementLevels; level++)
                {
                    RigidPoseCandidate localBest = ReadRigidCandidate(part, center, QueryAtAngle(part, request, center));
                    for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && y == 0 && z == 0) continue;
                        Vector3 trial = ProjectInsideRigidBall(center + new Vector3(x, y, z) * step, search.MaximumRadians);
                        var sample = EvaluateRigidSample(search, trial);
                        if (sample != null && BetterRigidCandidate(sample, localBest)) localBest = sample;
                    }
                    center = localBest.angleRadians; step *= .5f;
                }
            }
            result.best = search.Best[0]; result.evaluatedUniqueRotations = search.Seen.Count;
            float originalRadians = request.MaximumSwingDegrees * Mathf.Deg2Rad;
            result.bestWithinOriginalBall = result.best.angleRadians.sqrMagnitude <= originalRadians * originalRadians;
            result.refinementSamples = result.evaluatedUniqueRotations - result.deterministicCoarseSamples;
            // The best candidate is checked again against uninflated capsules by a
            // complete triangle scan. Subtracting the runtime margin is diagnostic
            // arithmetic, not by itself the final no-overlap witness.
            Apply(request.Pivot, request.RestLocalRotation, result.best.angleRadians);
            result.bestUninflatedBodyProxyPenetrationMeters = VerifyRigidBestFullTriangles(part, out long fullTests, out var perSurface);
            result.bestPerSurface = perSurface;
            result.bestIndependentTriangleTests = fullTests; result.bestFullTriangleWitnessVerified = true;
            result.foundNoProxySurfaceOverlap = result.bestUninflatedBodyProxyPenetrationMeters <= 0f;
            result.foundRuntimeClearance = result.best.runtimeClearanceSatisfied;
            result.classification = result.foundRuntimeClearance ? "FOUND_ROTATION_WITH_RUNTIME_CLEARANCE" :
                result.foundNoProxySurfaceOverlap ? "FOUND_NO_SURFACE_OVERLAP_CLEARANCE_MARGIN_NOT_MET" : "NO_NONOVERLAP_WITNESS_FOUND_IN_FINITE_SAMPLES_NOT_PROOF_OF_INFEASIBILITY";
            result.triangleDistanceTests = part.Measurement.triangleTests; result.completed = true;
            result.elapsedMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency; return result;
        }
        private float VerifyRigidBestFullTriangles(PartState part, out long tests, out RigidSurfaceWitness[] perSurface)
        {
            tests = 0; float deepest = 0;
            Transform frame = part.Binding.DrivenBones[0]; Matrix4x4 inverse = frame.worldToLocalMatrix; float scale = frame.lossyScale.x;
            var localCapsules = new Geometry.Capsule[_capsules.Length];
            for (int c = 0; c < localCapsules.Length; c++) localCapsules[c] = new Geometry.Capsule
                { A = inverse.MultiplyPoint3x4(_capsules[c].A), B = inverse.MultiplyPoint3x4(_capsules[c].B), Radius = _capsules[c].Radius / scale };
            perSurface = new RigidSurfaceWitness[part.Surfaces.Length];
            for (int s = 0; s < part.Surfaces.Length; s++)
            {
                var source = part.Surfaces[s].Binding;
                var row = MeasureRigidSurfaceFullTriangles(source.Positions, source.Triangles, source.OriginalTriangleIndices, localCapsules, scale);
                row.surfaceIndex = s; row.renderer = source.SourceRenderer.name;
                row.rendererHierarchyPath = RigidWitnessPath(source.SourceRenderer.transform);
                row.sourceGuid = source.SourceGuid; row.sourceLocalId = source.SourceLocalId;
                row.sourceFileSha256 = source.SourceFileSha256; row.sourceGeometrySha256 = source.SourceGeometrySha256;
                row.authoredGeometrySha256 = source.GeometrySha256;
                var lodGroup = source.SourceRenderer.GetComponentInParent<LODGroup>(true);
                if (lodGroup != null)
                {
                    var levels = new List<int>(); var lods = lodGroup.GetLODs();
                    for (int l = 0; l < lods.Length; l++) if (Array.IndexOf(lods[l].renderers, source.SourceRenderer) >= 0) levels.Add(l);
                    row.lodIndices = levels.ToArray(); row.lodMembershipVerified = levels.Count > 0;
                    row.lodGroupHierarchyPath = RigidWitnessPath(lodGroup.transform);
                }
                if (row.worstCapsuleIndex >= 0) row.worstCapsuleName = _bodyCapsules[row.worstCapsuleIndex].name;
                row.worstSurfaceWorld = frame.TransformPoint(row.worstSurfaceWorld); row.worstAxisWorld = frame.TransformPoint(row.worstAxisWorld);
                perSurface[s] = row; deepest = Mathf.Max(deepest, row.maximumPenetrationMeters); tests += row.fullTriangleTests;
            }
            return deepest;
        }
        // Diagnostic-only pure full scan. The local output points are transformed
        // to world by the caller; this never drives a bone or changes candidate scoring.
        internal static RigidSurfaceWitness MeasureRigidSurfaceFullTriangles(Vector3[] positions, int[] triangles,
            int[] originalTriangleIndices, Geometry.Capsule[] localCapsules, float scale)
        {
            Need(positions != null && positions.Length > 0 && triangles != null && triangles.Length > 0 && triangles.Length % 3 == 0 &&
                originalTriangleIndices != null && originalTriangleIndices.Length == triangles.Length / 3 &&
                localCapsules != null && localCapsules.Length > 0 && float.IsFinite(scale) && scale > 0f, "Complete finite rigid witness inputs required.");
            foreach (var p in positions) Need(Geometry.Finite(p), "Nonfinite original rigid vertex.");
            foreach (int i in triangles) Need(i >= 0 && i < positions.Length, "Invalid original rigid triangle index.");
            var row = new RigidSurfaceWitness { vertices = positions.Length, triangles = triangles.Length / 3, bodyCapsules = localCapsules.Length };
            float signedDeepest = float.NegativeInfinity;
            for (int c = 0; c < localCapsules.Length; c++)
            {
                var capsule = localCapsules[c];
                Need(Geometry.Finite(capsule.A) && Geometry.Finite(capsule.B) && float.IsFinite(capsule.Radius) && capsule.Radius >= 0f, "Invalid original body capsule.");
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    float distance = Geometry.SegmentTriangleDistance(capsule.A, capsule.B, positions[triangles[t]], positions[triangles[t + 1]], positions[triangles[t + 2]], out Vector3 axis, out Vector3 surface);
                    Need(float.IsFinite(distance), "Nonfinite full-triangle witness distance.");
                    float depth = (capsule.Radius - distance) * scale;
                    row.maximumPenetrationMeters = Mathf.Max(row.maximumPenetrationMeters, depth);
                    // This extra margin column is diagnostic only; original raw
                    // union acceptance remains the exact same uninflated scan.
                    row.maximumSupportMarginPenetrationMeters = Mathf.Max(row.maximumSupportMarginPenetrationMeters, depth + ContactMargin);
                    if (depth > signedDeepest)
                    {
                        signedDeepest = depth; row.worstCapsuleIndex = c; row.worstOriginalTriangleIndex = originalTriangleIndices[t / 3];
                        row.worstAxisWorld = axis; row.worstSurfaceWorld = surface;
                    }
                    row.fullTriangleTests++;
                }
            }
            row.minimumSignedClearanceMeters = -signedDeepest; return row;
        }
        private static string RigidWitnessPath(Transform value)
        {
            string path = value.name;
            while (value.parent != null) { value = value.parent; path = value.name + "/" + path; }
            return path;
        }
        private RigidPoseCandidate EvaluateRigidSample(RigidSearchState search, Vector3 proposed)
        {
            Vector3 angle = ProjectInsideRigidBall(proposed, search.MaximumRadians);
            if (search.Seen.TryGetValue(angle, out var existing)) return existing;
            var sample = ReadRigidCandidate(search.Part, angle, QueryAtAngle(search.Part, search.Request, angle));
            search.Seen.Add(angle, sample);
            search.Result.maximumSampledAngleDegrees = Mathf.Max(search.Result.maximumSampledAngleDegrees, sample.angleDegrees);
            int index = 0; while (index < search.Best.Count && !BetterRigidCandidate(sample, search.Best[index])) index++;
            search.Best.Insert(index, sample); if (search.Best.Count > search.SeedCount) search.Best.RemoveAt(search.Best.Count - 1);
            return sample;
        }
        private Geometry.Contact QueryAtAngle(PartState part, RigidPoseSearchRequest request, Vector3 angle)
        { Apply(request.Pivot, request.RestLocalRotation, angle); return Query(part); }
        private RigidPoseCandidate ReadRigidCandidate(PartState part, Vector3 angle, Geometry.Contact contact)
        {
            return new RigidPoseCandidate { angleRadians = angle, angleDegrees = angle.magnitude * Mathf.Rad2Deg,
                surfacePenetrationMeters = Mathf.Max(0f, contact.Depth - ContactMargin), runtimeMarginRemainingMeters = contact.Depth,
                triangleSurfaceOverlap = contact.Depth > ContactMargin, runtimeClearanceSatisfied = !contact.Hit,
                capsuleIndex = contact.Capsule, capsuleName = contact.Capsule >= 0 ? _bodyCapsules[contact.Capsule].name : null,
                surfaceIndex = contact.Surface, originalTriangleIndex = contact.Hit ? part.Surfaces[contact.Surface].Binding.OriginalTriangleIndices[contact.Triangle] : -1,
                sourceGeometrySha256 = contact.Hit ? part.Surfaces[contact.Surface].Binding.SourceGeometrySha256 : null };
        }
        private static bool BetterRigidCandidate(RigidPoseCandidate a, RigidPoseCandidate b) =>
            a.runtimeMarginRemainingMeters < b.runtimeMarginRemainingMeters ||
            (a.runtimeMarginRemainingMeters == b.runtimeMarginRemainingMeters && a.angleRadians.sqrMagnitude < b.angleRadians.sqrMagnitude);
        public static Vector3 ProjectInsideRigidBall(Vector3 angle, float radius)
        {
            Need(Geometry.Finite(angle) && float.IsFinite(radius) && radius >= 0f, "Finite angle and nonnegative radius required.");
            double squared = angle.x * (double)angle.x + angle.y * (double)angle.y + angle.z * (double)angle.z;
            double limit = radius * (double)radius;
            Vector3 result = angle;
            if (squared > limit)
            {
                double scale = radius / Math.Sqrt(squared);
                result = new Vector3((float)(angle.x * scale), (float)(angle.y * scale), (float)(angle.z * scale));
            }
            // Correct only outward float rounding under the production float norm;
            // this shrinks a boundary sample by at most float rounding, not the limit.
            while (result.sqrMagnitude > radius * radius) result *= .999999f;
            return result;
        }
        public static IEnumerable<Vector3> DeterministicRigidSamples(float radius, RigidPoseSearchSettings settings)
        {
            ValidateSearchSettings(settings);
            Need(float.IsFinite(radius) && radius >= 0f && radius <= 80f * Mathf.Deg2Rad, "Finite nonnegative diagnostic radius required.");
            for (int shell = 1; shell <= settings.RadialShells; shell++)
                for (int i = 0; i < settings.SphereDirections; i++)
                {
                    double z = 1d - 2d * (i + .5d) / settings.SphereDirections;
                    double r = Math.Sqrt(Math.Max(0d, 1d - z * z)), phi = i * 2.39996322972865332d;
                    var axis = new Vector3((float)(r * Math.Cos(phi)), (float)(r * Math.Sin(phi)), (float)z);
                    yield return ProjectInsideRigidBall(axis * (radius * shell / settings.RadialShells), radius);
                }
            int half = settings.GridAxisSamples / 2;
            for (int x = -half; x <= half; x++) for (int y = -half; y <= half; y++) for (int z = -half; z <= half; z++)
            {
                var point = new Vector3(x, y, z) * (radius / half);
                if (point.sqrMagnitude <= radius * radius) yield return point;
            }
        }
        private static void ValidateSearchSettings(RigidPoseSearchSettings settings)
        {
            Need(settings != null && settings.SphereDirections >= 32 && settings.SphereDirections <= 2048 &&
                settings.RadialShells >= 2 && settings.RadialShells <= 12 && settings.GridAxisSamples >= 5 && settings.GridAxisSamples <= 21 && settings.GridAxisSamples % 2 == 1 &&
                settings.RefinementSeeds >= 1 && settings.RefinementSeeds <= 32 && settings.RefinementLevels >= 1 && settings.RefinementLevels <= 16,
                "Explicit deterministic search settings exceed diagnostic bounds.");
        }
        private static bool FiniteRotation(Quaternion q) => float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w) &&
            Mathf.Abs(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w - 1f) <= .0001f;
        private static float RotationDifferenceBasis(Quaternion a, Quaternion b) => Mathf.Max(Vector3.Distance(a * Vector3.right, b * Vector3.right),
            Mathf.Max(Vector3.Distance(a * Vector3.up, b * Vector3.up), Vector3.Distance(a * Vector3.forward, b * Vector3.forward)));
    }
}
