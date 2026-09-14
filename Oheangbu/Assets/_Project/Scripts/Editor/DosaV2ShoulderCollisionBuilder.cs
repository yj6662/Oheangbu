using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.Presentation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>World-only additional anatomical sources. The caller preserves all existing
    /// bindings/capsules and owns Configure plus native16 candidate registration.</summary>
    public static class DosaV2ShoulderCollisionBuilder
    {
        public const string Prefix = "__DosaSecondaryProxy_ShoulderLining_";
        public static string[] ReviewedNames => new[] { Prefix + "Left", Prefix + "Right" };
        [Serializable] public sealed class SourceReport
        {
            public string renderer, capsule, poseDriver = "Existing PlayerClothBodyProxyRig.RefreshNow after final pose";
            public int sourceVertices, uniqueFitCorners, originalTriangles, paletteBones;
            public double restArea;
            public float maximumWholeTriangleOutsideMeters;
            public PlayerClothBodyProxyRig.Fit restFit;
        }
        [Serializable] public sealed class Report
        {
            public string status = "WAIT", error;
            public bool rigPass;
            public string scope = "Complete actual readable96-triangle ShoulderLining sources, no excluded faces. Each whole surface is enclosed by one convex fitted capsule. Original33 body candidates must remain unchanged. No Cloth pin enters fitting; native/secondary collision and current garment clearance still need validation.";
            public List<SourceReport> sources = new List<SourceReport>();
        }
        public static bool TryCreateBindings(Transform worldRepresentation,
            out PlayerClothBodyProxyRig.Binding[] additionalBindings,
            out CapsuleCollider[] additionalCapsules, out Report report)
        {
            additionalBindings = Array.Empty<PlayerClothBodyProxyRig.Binding>();
            additionalCapsules = Array.Empty<CapsuleCollider>(); report = new Report();
            var created = new List<GameObject>();
            try
            {
                Need(!Application.isPlaying, "Author on the fresh Edit-mode world rest fixture only.");
                Need(worldRepresentation != null, "Explicit world representation required.");
                Need(worldRepresentation.gameObject.layer != 31, "Near representation is outside this binding contract.");
                Vector3 scale = worldRepresentation.lossyScale;
                Need(scale.x > 0f && Mathf.Abs(scale.x - scale.y) < .00001f && Mathf.Abs(scale.x - scale.z) < .00001f, "Positive uniform world scale required.");
                Need(!worldRepresentation.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith(Prefix, StringComparison.Ordinal)), "Shoulder candidates already exist; do not append duplicates.");
                var sources = worldRepresentation.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var bindings = new List<PlayerClothBodyProxyRig.Binding>(); var capsules = new List<CapsuleCollider>();
                foreach (string side in new[] { "Left", "Right" })
                {
                    var source = sources.Single(r => r.name == "DosaV2_ShoulderLining_" + side);
                    var binding = AuthorWholeSurface(source, worldRepresentation, out var fit, out var item);
                    var obj = new GameObject(Prefix + side) { layer = 2 }; created.Add(obj);
                    obj.transform.SetParent(worldRepresentation, false);
                    Vector3 axis = fit.end - fit.start; obj.transform.localPosition = (fit.start + fit.end) * .5f;
                    obj.transform.localRotation = axis.sqrMagnitude > 1e-14f ? Quaternion.FromToRotation(Vector3.up, axis) : Quaternion.identity;
                    obj.transform.localScale = Vector3.one;
                    var capsule = obj.AddComponent<CapsuleCollider>(); capsule.direction = 1; capsule.center = Vector3.zero;
                    capsule.radius = fit.radius; capsule.height = axis.magnitude + 2f * fit.radius;
                    capsule.isTrigger = true; capsule.excludeLayers = -1; capsule.includeLayers = 0;
                    Need(PlayerSecondaryMotionRig.ValidateProxy(capsule, out string error), error);
                    binding.regions[0].capsule = capsule; item.capsule = obj.name;
                    bindings.Add(binding); capsules.Add(capsule); report.sources.Add(item);
                }
                additionalBindings = bindings.ToArray(); additionalCapsules = capsules.ToArray();
                report.status = "AUTHORED_WHOLE_TRIANGLE_COVERAGE_PENDING_DYNAMIC_CONFIGURE_AND_NATIVE_REVIEW"; return true;
            }
            catch (Exception e)
            {
                for (int i = created.Count - 1; i >= 0; i--) if (created[i] != null) Object.DestroyImmediate(created[i]);
                report.error = e.GetBaseException().Message; return false;
            }
        }
        private static PlayerClothBodyProxyRig.Binding AuthorWholeSurface(SkinnedMeshRenderer renderer,
            Transform representation, out PlayerClothBodyProxyRig.Fit fit, out SourceReport report)
        {
            Mesh mesh = renderer.sharedMesh;
            Need(mesh != null && mesh.isReadable && mesh.blendShapeCount == 0, "Readable shape-free actual shoulder skin required.");
            Vector3[] rest = mesh.vertices; BoneWeight[] weights = mesh.boneWeights; Matrix4x4[] bindposes = mesh.bindposes;
            Transform[] bones = renderer.bones; int[] triangles = mesh.triangles;
            Need(triangles.Length == 96 * 3 && weights.Length == rest.Length && bones.Length == bindposes.Length, "Reviewed shoulder topology/skin changed.");
            int[] representatives;
            using (var counts = mesh.GetBonesPerVertex())
            using (var all = mesh.GetAllBoneWeights())
            {
                foreach (byte count in counts) Need(count > 0 && count <= 4, "Shoulder skin exceeds four influences.");
                representatives = PlayerClothBrushProxyRig.BuildEquivalentVertexRepresentatives(rest, counts.ToArray(), all.ToArray(), null);
            }
            var current = new Vector3[rest.Length]; var skin = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            { Need(bones[i] != null && bones[i].IsChildOf(representation), "Invalid shoulder bone hierarchy."); skin[i] = representation.worldToLocalMatrix * bones[i].localToWorldMatrix * bindposes[i]; }
            for (int i = 0; i < current.Length; i++)
            {
                var w = weights[i]; Need(Mathf.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1f) < .0001f, "Unnormalized shoulder skin.");
                current[i] = skin[w.boneIndex0].MultiplyPoint3x4(rest[i]) * w.weight0 + skin[w.boneIndex1].MultiplyPoint3x4(rest[i]) * w.weight1
                    + skin[w.boneIndex2].MultiplyPoint3x4(rest[i]) * w.weight2 + skin[w.boneIndex3].MultiplyPoint3x4(rest[i]) * w.weight3;
            }
            var coverage = new PlayerClothBodyProxyRig.Corner[triangles.Length];
            var sizes = new int[triangles.Length / 3]; double area = 0d;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                coverage[i] = new PlayerClothBodyProxyRig.Corner { a = a, b = b, c = c, bary = Vector3.right };
                coverage[i + 1] = new PlayerClothBodyProxyRig.Corner { a = a, b = b, c = c, bary = Vector3.up };
                coverage[i + 2] = new PlayerClothBodyProxyRig.Corner { a = a, b = b, c = c, bary = Vector3.forward };
                sizes[i / 3] = 3; area += TriangleArea(current[a], current[b], current[c]);
            }
            var corners = triangles.Select(i => representatives[i]).Distinct().OrderBy(i => i)
                .Select(i => new PlayerClothBodyProxyRig.Corner { a = i, b = i, c = i, bary = Vector3.right }).ToArray();
            Need(corners.Length == 50, "Reviewed50 unique shoulder positions/weights changed; rerun source audit.");
            var workspace = new PlayerClothBodyProxyRig.FitWorkspace(corners.Length);
            for (int i = 0; i < corners.Length; i++) workspace.Points[i] = current[corners[i].a];
            fit = PlayerClothBodyProxyRig.MeasureFit(workspace);
            fit.maximumRawCornerOutside = PlayerClothBodyProxyRig.MaximumCornerOutside(current, coverage, fit); fit.rawCorners = coverage.Length;
            Need(fit.maximumRawCornerOutside <= .000002f, "Whole original shoulder triangle escaped its convex capsule.");
            var binding = new PlayerClothBodyProxyRig.Binding { renderer = renderer, restArea = area, partitionAreaError = 0d,
                regions = new[] { new PlayerClothBodyProxyRig.Region { lower = 0, upper = 1, corners = corners, coverageCorners = coverage, polygonSizes = sizes } } };
            report = new SourceReport { renderer = renderer.name, sourceVertices = rest.Length, uniqueFitCorners = corners.Length,
                originalTriangles = 96, paletteBones = bones.Length, restArea = area, restFit = fit, maximumWholeTriangleOutsideMeters = fit.maximumRawCornerOutside };
            return binding;
        }
        private static double TriangleArea(Vector3 a, Vector3 b, Vector3 c)
        {
            double ux = (double)b.x - a.x, uy = (double)b.y - a.y, uz = (double)b.z - a.z,
                vx = (double)c.x - a.x, vy = (double)c.y - a.y, vz = (double)c.z - a.z;
            double x = uy * vz - uz * vy, y = uz * vx - ux * vz, z = ux * vy - uy * vx; return Math.Sqrt(x * x + y * y + z * z) * .5d;
        }
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
