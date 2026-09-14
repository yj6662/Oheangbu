using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Staged Edit-mode authoring helper. It only creates explicitly isolated additional
    /// representation colliders after validating actual imported rest skin. The caller owns candidate
    /// registration, native Cloth's 16-capsule selection and dynamic secondary-motion approval.</summary>
    public static class DosaV2HeadNeckCollisionBuilder
    {
        public const string Prefix = "__DosaSecondaryProxy_HeadNeck_";
        public static string[] ReviewedNames => new[] { Prefix + "Neck", Prefix + "Jaw", Prefix + "Face_R",
            Prefix + "Face_L", Prefix + "Occiput", Prefix + "Crown" };
        private const float RoundTripTolerance = .00005f;

        [Serializable] public sealed class CapsulePlan
        {
            public string name, anchorBone;
            public float[] start, end;
            public float radius;
        }
        [Serializable] public sealed class SemanticMask
        {
            public string[] rendererNames;
            public float minimumHeadWeight, collarHeadCentroidMaximumZ,
                collarHeadCentroidAbsXGreaterThan, collarHeadCentroidYGreaterThan;
            public string rule;
        }
        [Serializable] public sealed class AuthoredPlan
        {
            public string source, sourceSha256, status, space;
            public CapsulePlan[] capsules;
            public SemanticMask semanticMask;
            public int includedTriangles, excludedUpperGarmentTriangles;
        }
        [Serializable] public sealed class TriangleExclusion
        {
            public string renderer, reason;
            public int triangle;
            public Vector3 centroidBlender;
        }
        [Serializable] public sealed class RendererAudit
        {
            public string renderer;
            public int vertices, triangles, includedTriangles, excludedMixedWeightTriangles, excludedCollarTriangles;
            public float maximumIncludedHeadWeightError;
        }
        [Serializable] public sealed class ProxyAudit
        {
            public string name, anchorBone;
            public Vector3 startBlender, endBlender, startRepresentation, endRepresentation;
            public Vector3 startBoneLocal, endBoneLocal;
            public float radiusMeters;
        }
        [Serializable] public sealed class BindingReport
        {
            public string status = "WAIT", error, planPath, planSha256, sourceBlendSha256;
            public string scope = "Actual imported rest Head/Hair complete triangles under the authored semantic mask. All included corners are rigid Head-weighted. Same-convex-capsule containment proves complete-face coverage. Excluded collar/shoulder garment remains explicitly listed. No dynamic Cloth or ornament acceptance is inferred.";
            public bool rigPass = false;
            public int includedTriangles, excludedTriangles, authoredIncludedTriangles, authoredExcludedTriangles;
            public float maximumWholeTriangleOutsideM = float.NegativeInfinity;
            public float allowedRoundTripToleranceM = RoundTripTolerance;
            public string worstRenderer;
            public int worstTriangle = -1;
            public List<RendererAudit> renderers = new List<RendererAudit>();
            public List<TriangleExclusion> exclusions = new List<TriangleExclusion>();
            public List<ProxyAudit> proxies = new List<ProxyAudit>();
        }

        public static bool TryBind(Transform representation, Animator animator, string authoredPlanPath,
            out CapsuleCollider[] additionalCapsules, out BindingReport report)
        {
            additionalCapsules = Array.Empty<CapsuleCollider>();
            report = new BindingReport { planPath = authoredPlanPath };
            var created = new List<GameObject>();
            try
            {
                Require(!Application.isPlaying, "Bind on a fresh Edit-mode rest instance; this helper never changes a live pose.");
                Require(representation != null && animator != null &&
                    (animator.transform == representation || animator.transform.IsChildOf(representation)),
                    "An explicit representation root and its Animator are required.");
                Require(UniformPositive(representation.lossyScale), "Representation world scale must be positive and uniform.");
                Require(!string.IsNullOrEmpty(authoredPlanPath) && File.Exists(authoredPlanPath), "Authored capsule JSON is missing.");
                var plan = JsonUtility.FromJson<AuthoredPlan>(File.ReadAllText(authoredPlanPath));
                ValidatePlan(plan);
                report.planSha256 = HashFile(authoredPlanPath); report.sourceBlendSha256 = plan.sourceSha256;
                report.authoredIncludedTriangles = plan.includedTriangles;
                report.authoredExcludedTriangles = plan.excludedUpperGarmentTriangles;
                Transform head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
                if (head == null)
                {
                    var matches = animator.GetComponentsInChildren<Transform>(true).Where(t => Normalize(t.name) == "Head").ToArray();
                    Require(matches.Length == 1, "Exactly one explicit Head bone is required."); head = matches[0];
                }
                Require(head.IsChildOf(representation) && UniformPositive(head.lossyScale), "Head must be a uniformly scaled descendant of the representation.");
                foreach (Transform t in representation.GetComponentsInChildren<Transform>(true))
                    Require(!t.name.StartsWith(Prefix, StringComparison.Ordinal), "Additional HeadNeck proxies already exist; reuse their registered set or recreate the isolated fixture.");

                var renderers = representation.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (string rendererName in plan.semanticMask.rendererNames)
                {
                    var matches = renderers.Where(r => Normalize(r.name) == rendererName).ToArray();
                    Require(matches.Length == 1, "Expected one complete source renderer: " + rendererName);
                    AuditRenderer(matches[0], representation, head, plan, report);
                }
                Require(report.includedTriangles > 0, "No anatomical triangles were validated.");
                Require(report.includedTriangles == plan.includedTriangles && report.excludedTriangles == plan.excludedUpperGarmentTriangles,
                    "Imported semantic triangle counts differ from the reviewed source. Review topology/weights instead of silently excluding changed skin.");
                Require(report.maximumWholeTriangleOutsideM <= RoundTripTolerance,
                    "Actual imported triangle escapes every single convex head capsule: " + report.worstRenderer + "/" + report.worstTriangle + " by " + report.maximumWholeTriangleOutsideM + "m.");

                var result = new List<CapsuleCollider>();
                foreach (CapsulePlan p in plan.capsules)
                {
                    Vector3 aLocal = BlenderToRepresentation(ToVector(p.start)), bLocal = BlenderToRepresentation(ToVector(p.end));
                    Vector3 a = representation.TransformPoint(aLocal), b = representation.TransformPoint(bLocal), delta = b - a;
                    float radiusWorld = p.radius * representation.lossyScale.x;
                    var obj = new GameObject(p.name) { layer = 2 }; created.Add(obj);
                    obj.transform.SetParent(head, false);
                    obj.transform.SetPositionAndRotation((a + b) * .5f,
                        delta.sqrMagnitude > 1e-14f ? Quaternion.FromToRotation(Vector3.up, delta) : Quaternion.identity);
                    Require(UniformPositive(obj.transform.lossyScale), p.name + " has nonuniform world scale.");
                    float scale = obj.transform.lossyScale.x;
                    var collider = obj.AddComponent<CapsuleCollider>();
                    collider.direction = 1; collider.center = Vector3.zero;
                    collider.radius = radiusWorld / scale; collider.height = (delta.magnitude + 2f * radiusWorld) / scale;
                    collider.isTrigger = true; collider.excludeLayers = -1; collider.includeLayers = 0;
                    Require(collider.attachedRigidbody == null, "Representation-only proxies cannot inherit a Rigidbody.");
                    result.Add(collider);
                    report.proxies.Add(new ProxyAudit { name = p.name, anchorBone = head.name,
                        startBlender = ToVector(p.start), endBlender = ToVector(p.end),
                        startRepresentation = aLocal, endRepresentation = bLocal,
                        startBoneLocal = head.InverseTransformPoint(a), endBoneLocal = head.InverseTransformPoint(b), radiusMeters = p.radius });
                }
                additionalCapsules = result.ToArray();
                report.status = "BOUND_ACTUAL_REST_TRIANGLE_COVERAGE_PENDING_DYNAMIC_SECONDARY_REPLAY";
                return true;
            }
            catch (Exception e)
            {
                for (int i = created.Count - 1; i >= 0; i--) if (created[i] != null) Object.DestroyImmediate(created[i]);
                report.status = "WAIT"; report.error = e.Message; return false;
            }
        }

        private static void AuditRenderer(SkinnedMeshRenderer renderer, Transform representation, Transform head,
            AuthoredPlan plan, BindingReport report)
        {
            Mesh source = renderer.sharedMesh;
            Require(source != null && source.isReadable, renderer.name + " requires the actual readable source skin.");
            var weights = source.boneWeights; var bones = renderer.bones;
            Require(weights.Length == source.vertexCount, renderer.name + " needs explicit four-weight source skin data.");
            var audit = new RendererAudit { renderer = Normalize(renderer.name), vertices = source.vertexCount };
            var baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked, false);
                Vector3[] vertices = baked.vertices; int[] indices = baked.triangles;
                Require(vertices.Length == source.vertexCount && indices.Length % 3 == 0, renderer.name + " bake topology mismatch.");
                var points = new Vector3[vertices.Length]; var headWeights = new float[vertices.Length];
                Matrix4x4 toRoot = representation.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int i = 0; i < points.Length; i++)
                {
                    points[i] = toRoot.MultiplyPoint3x4(vertices[i]);
                    Require(Finite(points[i]), renderer.name + " has a non-finite actual baked vertex.");
                    headWeights[i] = HeadWeight(weights[i], bones, head);
                }
                audit.triangles = indices.Length / 3;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                    Vector3 a = points[ia], b = points[ib], c = points[ic];
                    Vector3 centroid = RepresentationToBlender((a + b + c) / 3f);
                    bool rigid = headWeights[ia] > plan.semanticMask.minimumHeadWeight &&
                        headWeights[ib] > plan.semanticMask.minimumHeadWeight && headWeights[ic] > plan.semanticMask.minimumHeadWeight;
                    bool collar = audit.renderer == "DosaV2_Head" && IsCollar(centroid, plan.semanticMask);
                    if (!rigid || collar)
                    {
                        if (!rigid) audit.excludedMixedWeightTriangles++; else audit.excludedCollarTriangles++;
                        report.excludedTriangles++;
                        report.exclusions.Add(new TriangleExclusion { renderer = audit.renderer, triangle = i / 3,
                            centroidBlender = centroid, reason = !rigid ? "MIXED_WEIGHT_SHOULDER_COLLAR_GARMENT" : "HEAD_RIGID_COLLAR_SEMANTIC_MASK" });
                        continue;
                    }
                    audit.includedTriangles++; report.includedTriangles++;
                    audit.maximumIncludedHeadWeightError = Mathf.Max(audit.maximumIncludedHeadWeightError,
                        Mathf.Abs(1f - headWeights[ia]), Mathf.Abs(1f - headWeights[ib]), Mathf.Abs(1f - headWeights[ic]));
                    float outside = float.PositiveInfinity;
                    foreach (CapsulePlan capsule in plan.capsules)
                        outside = Mathf.Min(outside, WholeTriangleOutside(a, b, c,
                            BlenderToRepresentation(ToVector(capsule.start)), BlenderToRepresentation(ToVector(capsule.end)), capsule.radius));
                    if (outside > report.maximumWholeTriangleOutsideM)
                    { report.maximumWholeTriangleOutsideM = outside; report.worstRenderer = audit.renderer; report.worstTriangle = i / 3; }
                }
                Require(audit.includedTriangles > 0, renderer.name + " has no eligible rigid anatomical surface.");
                // A changed/corrupt skin must not silently disappear behind the semantic exclusion rule.
                Require(audit.renderer != "DosaV2_Hair" || audit.excludedMixedWeightTriangles == 0,
                    "The authored Hair surface is entirely Head-rigid; imported mixed weights require review.");
                report.renderers.Add(audit);
            }
            finally { Object.DestroyImmediate(baked); }
        }

        public static float WholeTriangleOutside(Vector3 a, Vector3 b, Vector3 c, Vector3 start, Vector3 end, float radius)
        {
            return Mathf.Max(SignedDistance(a, start, end, radius), SignedDistance(b, start, end, radius), SignedDistance(c, start, end, radius));
        }
        public static float SignedDistance(Vector3 point, Vector3 start, Vector3 end, float radius)
        {
            Vector3 delta = end - start;
            float t = delta.sqrMagnitude > 1e-20f ? Mathf.Clamp01(Vector3.Dot(point - start, delta) / delta.sqrMagnitude) : 0f;
            return (point - start - t * delta).magnitude - radius;
        }
        public static bool IsCollar(Vector3 centroidBlender, SemanticMask mask)
        {
            return centroidBlender.z < mask.collarHeadCentroidMaximumZ &&
                (Mathf.Abs(centroidBlender.x) > mask.collarHeadCentroidAbsXGreaterThan || centroidBlender.y > mask.collarHeadCentroidYGreaterThan);
        }
        public static Vector3 BlenderToRepresentation(Vector3 value) { return new Vector3(-value.x, value.z, -value.y); }
        public static Vector3 RepresentationToBlender(Vector3 value) { return new Vector3(-value.x, -value.z, value.y); }
        private static float HeadWeight(BoneWeight weight, Transform[] bones, Transform head)
        {
            float value = 0f;
            if (weight.weight0 > 0f && bones[weight.boneIndex0] == head) value += weight.weight0;
            if (weight.weight1 > 0f && bones[weight.boneIndex1] == head) value += weight.weight1;
            if (weight.weight2 > 0f && bones[weight.boneIndex2] == head) value += weight.weight2;
            if (weight.weight3 > 0f && bones[weight.boneIndex3] == head) value += weight.weight3;
            return value;
        }
        private static void ValidatePlan(AuthoredPlan plan)
        {
            Require(plan != null && plan.semanticMask != null && plan.capsules != null && plan.capsules.Length > 0 && plan.capsules.Length <= 6,
                "A source-bound plan with at most six additional capsules is required.");
            Require(plan.semanticMask.rendererNames != null && plan.semanticMask.rendererNames.SequenceEqual(new[] { "DosaV2_Head", "DosaV2_Hair" }), "Unexpected anatomical surface roles.");
            Require(plan.semanticMask.minimumHeadWeight >= .99999f && plan.semanticMask.minimumHeadWeight < 1f, "Rigid source Head-weight threshold is required.");
            Require(plan.sourceSha256 != null && plan.sourceSha256.Length == 64, "Source SHA256 provenance is required.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (CapsulePlan p in plan.capsules)
            {
                Require(p != null && p.anchorBone == "Head" && p.name.StartsWith(Prefix, StringComparison.Ordinal) && names.Add(p.name), "Unique explicit Head-parented proxy names are required.");
                Require(p.start != null && p.start.Length == 3 && p.end != null && p.end.Length == 3 && Finite(ToVector(p.start)) && Finite(ToVector(p.end)) && Finite(p.radius) && p.radius > 0f,
                    "Finite positive capsule geometry is required.");
            }
        }
        private static Vector3 ToVector(float[] value) { return new Vector3(value[0], value[1], value[2]); }
        private static bool UniformPositive(Vector3 scale)
        { return Finite(scale) && scale.x > 0f && Mathf.Abs(scale.x - scale.y) <= .00001f * scale.x && Mathf.Abs(scale.x - scale.z) <= .00001f * scale.x; }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
        private static string Normalize(string name) { int colon = name.LastIndexOf(':'); return colon >= 0 ? name.Substring(colon + 1) : name; }
        private static string HashFile(string path)
        { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2"))); }
        private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }

        /// <summary>Pure geometry regression; callable independently from the parent Editor fixture.</summary>
        public static string RunMathChecks()
        {
            int checks = 0;
            Vector3 a = new Vector3(-.2f, 0f, 0f), b = new Vector3(.2f, 0f, 0f), c = new Vector3(0f, .3f, 0f);
            // Each corner has its own sphere, while the triangle interior lies outside their union.
            float falseUnion = Mathf.Min(WholeTriangleOutside(a, b, c, a, a, .03f),
                WholeTriangleOutside(a, b, c, b, b, .03f), WholeTriangleOutside(a, b, c, c, c, .03f));
            Require(falseUnion > 0f, "Vertex-only union incorrectly accepted a face."); checks++;
            Require(WholeTriangleOutside(a, b, c, Vector3.zero, Vector3.up * .2f, .21f) <= 0f, "One convex capsule must contain the entire triangle."); checks++;
            var rotation = Quaternion.Euler(73f, -41f, 117f); Vector3 offset = new Vector3(8f, -3f, 5f);
            float original = WholeTriangleOutside(a, b, c, Vector3.zero, Vector3.up * .2f, .21f);
            float posed = WholeTriangleOutside(rotation * a + offset, rotation * b + offset, rotation * c + offset,
                offset, rotation * (Vector3.up * .2f) + offset, .21f);
            Require(Mathf.Abs(posed - original) < .000002f, "Rigid parent transformation changed containment."); checks++;
            Require((RepresentationToBlender(BlenderToRepresentation(a)) - a).sqrMagnitude == 0f, "Coordinate roundtrip changed geometry."); checks++;
            Require(Mathf.Abs(SignedDistance(Vector3.right * .3f, Vector3.zero, Vector3.zero, .2f) - .1f) < .000001f, "Zero-segment sphere regression."); checks++;
            Require(!UniformPositive(new Vector3(1f, 2f, 1f)) && !UniformPositive(new Vector3(-1f, -1f, -1f)), "Nonuniform or reflected scales must be rejected."); checks++;
            return "PASS: " + checks + " head/neck capsule math checks; no scene, source or production mutation.";
        }
    }
}
