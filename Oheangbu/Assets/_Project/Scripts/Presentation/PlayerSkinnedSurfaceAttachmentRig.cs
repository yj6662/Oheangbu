using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.Presentation
{
    /// <summary>Expression-only anchors attached to an actual skinned garment triangle.</summary>
    public sealed class PlayerSkinnedSurfaceAttachmentRig : MonoBehaviour
    {
        [Serializable] public sealed class Binding
        {
            public string Name;
            public Transform Anchor;
            public SkinnedMeshRenderer Source;
            public int A, B, C;
            public Vector3 Barycentric;
            // Authored in the orthonormal REST triangle frame, with its origin at the barycentric point.
            public Vector3 PositionInFrame;
            public Quaternion RotationInFrame = Quaternion.identity;
        }
        private sealed class Cached
        {
            public Binding Binding;
            public Mesh Mesh;
            public Vector3[] Rest;
            public BoneWeight1[][] Weights;
            public Transform[] Bones;
            public Matrix4x4[] Bindposes;
        }
        [SerializeField] private Binding[] _bindings = Array.Empty<Binding>();
        private Cached[] _cache = Array.Empty<Cached>();
        public bool IsConfigured { get; private set; }
        public string LastError { get; private set; }
        public int RefreshCount { get; private set; }
        public bool TryBindSerialized() => IsConfigured || Bind();
        public bool Owns(Transform owner) => owner == transform;

        private void Awake() { if (_bindings.Length > 0) Bind(); }
        public bool Configure(Binding[] bindings)
        { _bindings = bindings ?? Array.Empty<Binding>(); return Bind(); }
        private bool Bind()
        {
            IsConfigured = false; _cache = Array.Empty<Cached>();
            try
            {
                Need(_bindings.Length > 0, "Missing authored surface attachment bindings.");
                var cache = new List<Cached>(); var anchors = new HashSet<Transform>();
                foreach (var binding in _bindings)
                {
                    Need(binding != null && binding.Anchor != null && binding.Source != null, "Missing attachment/source.");
                    Need(binding.Anchor.IsChildOf(transform) && binding.Anchor != transform && binding.Source.transform.IsChildOf(transform), "Attachments must be visual descendants of the explicit owner.");
                    Need(anchors.Add(binding.Anchor), "Duplicate attachment driver.");
                    var mesh = binding.Source.sharedMesh;
                    Need(mesh != null && mesh.isReadable && mesh.blendShapeCount == 0 && binding.Source.GetComponent<Cloth>() == null,
                        "Attachment source must be readable, unsimulated skin without unhandled shape keys.");
                    int[] indices = { binding.A, binding.B, binding.C };
                    Need(indices[0] != indices[1] && indices[0] != indices[2] && indices[1] != indices[2], "Degenerate source indices.");
                    foreach (int i in indices) Need(i >= 0 && i < mesh.vertexCount, "Source index outside mesh.");
                    bool triangleFound = false; int[] triangles = mesh.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int a = triangles[i], b = triangles[i + 1], d = triangles[i + 2];
                        if ((a == binding.A || a == binding.B || a == binding.C) &&
                            (b == binding.A || b == binding.B || b == binding.C) &&
                            (d == binding.A || d == binding.B || d == binding.C) && a != b && a != d && b != d)
                        { triangleFound = true; break; }
                    }
                    Need(triangleFound, "Attachment indices must describe a complete actual source triangle.");
                    Need(Finite(binding.Barycentric) && binding.Barycentric.x >= 0 && binding.Barycentric.y >= 0 && binding.Barycentric.z >= 0 &&
                        Mathf.Abs(binding.Barycentric.x + binding.Barycentric.y + binding.Barycentric.z - 1f) <= .00001f, "Invalid barycentric source attachment.");
                    Need(Finite(binding.PositionInFrame) && Finite(binding.RotationInFrame) && Mathf.Abs(Quaternion.Dot(binding.RotationInFrame, binding.RotationInFrame) - 1f) < .0001f, "Invalid authored rest offset.");
                    var c = new Cached { Binding = binding, Mesh = mesh, Bones = binding.Source.bones, Bindposes = mesh.bindposes,
                        Rest = new Vector3[3], Weights = new BoneWeight1[3][] };
                    Need(c.Bones.Length == c.Bindposes.Length, "Incomplete attachment skin palette.");
                    Vector3[] positions = mesh.vertices;
                    using (var counts = mesh.GetBonesPerVertex()) using (var weights = mesh.GetAllBoneWeights())
                    {
                        Need(counts.Length == mesh.vertexCount, "Incomplete attachment skin stream.");
                        var offsets = new int[counts.Length]; int count = 0;
                        for (int v = 0; v < counts.Length; v++) { offsets[v] = count; count += counts[v]; }
                        Need(count == weights.Length, "Unmatched attachment skin stream.");
                        for (int k = 0; k < 3; k++)
                        {
                            int v = indices[k]; c.Rest[k] = positions[v]; c.Weights[k] = new BoneWeight1[counts[v]];
                            Need(counts[v] > 0 && counts[v] <= 4 && Finite(positions[v]), "Invalid source attachment vertex.");
                            float sum = 0;
                            for (int j = 0; j < counts[v]; j++)
                            {
                                var w = weights[offsets[v] + j];
                                Need(w.boneIndex >= 0 && w.boneIndex < c.Bones.Length && c.Bones[w.boneIndex] != null && Finite(w.weight) && w.weight >= 0, "Invalid source attachment weight.");
                                Need(w.weight == 0 || (c.Bones[w.boneIndex] != binding.Anchor && !c.Bones[w.boneIndex].IsChildOf(binding.Anchor)), "Circular surface attachment.");
                                c.Weights[k][j] = w; sum += w.weight;
                            }
                            Need(Mathf.Abs(sum - 1f) <= .0001f, "Unnormalized attachment skin weights.");
                        }
                    }
                    cache.Add(c);
                }
                _cache = cache.ToArray(); IsConfigured = true; LastError = null; return true;
            }
            catch (Exception e) { LastError = e.GetBaseException().Message; return false; }
        }
        // Explicitly call after the final body pose and before child springs; no autonomous Update order.
        public bool RefreshNow()
        {
            if (!IsConfigured) return false;
            try
            {
                foreach (var c in _cache)
                {
                    Need(c.Binding.Source.sharedMesh == c.Mesh, "Attachment source changed; re-author its source indices and rest frame.");
                    Vector3 a = Skin(c, 0), b = Skin(c, 1), d = Skin(c, 2);
                    Need(TrySurfaceFrame(a, b, d, c.Binding.Barycentric, out Vector3 origin, out Quaternion frame), "Collapsed/nonfinite attachment triangle.");
                    // An actual barycentric origin is essential when the skinned triangle changes edge lengths.
                    // A rigid delta around the first vertex would drift across the belt surface.
                    c.Binding.Anchor.SetPositionAndRotation(origin + frame * c.Binding.PositionInFrame, frame * c.Binding.RotationInFrame);
                }
                RefreshCount++; LastError = null; return true;
            }
            catch (Exception e) { LastError = e.GetBaseException().Message; return false; }
        }
        private static Vector3 Skin(Cached c, int i)
        {
            Vector3 point = Vector3.zero;
            foreach (var weight in c.Weights[i]) point += (c.Bones[weight.boneIndex].localToWorldMatrix * c.Bindposes[weight.boneIndex]).MultiplyPoint3x4(c.Rest[i]) * weight.weight;
            return point;
        }
        public static bool TrySurfaceFrame(Vector3 a, Vector3 b, Vector3 c, Vector3 barycentric, out Vector3 origin, out Quaternion frame)
        {
            origin = a * barycentric.x + b * barycentric.y + c * barycentric.z; frame = Quaternion.identity;
            Vector3 tangent = b - a, normal = Vector3.Cross(tangent, c - a);
            if (!Finite(origin) || !Finite(tangent) || !Finite(normal) || tangent.sqrMagnitude < 1e-12f || normal.sqrMagnitude < 1e-16f) return false;
            frame = Quaternion.LookRotation(tangent.normalized, normal.normalized); return Finite(frame);
        }
        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        private static bool Finite(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
        private static bool Finite(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w);
        private static void Need(bool valid, string error) { if (!valid) throw new InvalidOperationException(error); }
    }
}
