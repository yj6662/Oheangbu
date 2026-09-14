using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace Oheangbu.Presentation
{
    [Serializable] public struct SecondaryCollisionWeight { public int Bone; public float Weight; }
    [Serializable] public sealed class SecondaryCollisionSurface
    {
        public Renderer SourceRenderer;
        public Mesh SourceMesh;
        public string SourceGuid, SourceFileSha256, SourceGeometrySha256, GeometrySha256;
        public long SourceLocalId;
        public bool Rigid;
        // Rigid positions are authored pivot-local actual BakeMesh positions.
        // Flexible positions are original mesh-local vertices with full weights.
        public Vector3[] Positions = Array.Empty<Vector3>();
        public int[] Triangles = Array.Empty<int>(), OriginalVertexIndices = Array.Empty<int>(), OriginalTriangleIndices = Array.Empty<int>();
        public Transform[] Bones = Array.Empty<Transform>();
        public Matrix4x4[] Bindposes = Array.Empty<Matrix4x4>();
        public int[] WeightStarts = Array.Empty<int>();
        public byte[] WeightCounts = Array.Empty<byte>();
        public SecondaryCollisionWeight[] Weights = Array.Empty<SecondaryCollisionWeight>();
    }
    [Serializable] public sealed class SecondaryCollisionPart
    {
        public string Name;
        public bool Rigid;
        public Transform[] DrivenBones = Array.Empty<Transform>();
        public SecondaryCollisionSurface[] Surfaces = Array.Empty<SecondaryCollisionSurface>();
    }

    /// <summary>Complete original triangle surfaces; no shrunken collision support or native Physics query.</summary>
    public static class PlayerSecondaryCollisionGeometry
    {
        // Initialization/authoring only. Preserves provenance after an in-place FBX
        // reimport retaining the same Unity GUID/local ID. Never used in hot queries.
        public static string SourceMeshHash(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable) throw new ArgumentException("Readable collision source required.");
            using (var bytes = new MemoryStream())
            using (var write = new BinaryWriter(bytes))
            {
                Vector3[] vertices = mesh.vertices; int[] triangles = mesh.triangles; Matrix4x4[] bindposes = mesh.bindposes;
                write.Write(vertices.Length); write.Write(triangles.Length); write.Write(bindposes.Length); write.Write(mesh.blendShapeCount);
                foreach (var p in vertices) { write.Write(p.x); write.Write(p.y); write.Write(p.z); }
                foreach (int i in triangles) write.Write(i);
                foreach (var matrix in bindposes) for (int i = 0; i < 16; i++) write.Write(matrix[i]);
                using (var counts = mesh.GetBonesPerVertex()) foreach (byte count in counts) write.Write(count);
                using (var weights = mesh.GetAllBoneWeights()) foreach (var weight in weights) { write.Write(weight.boneIndex); write.Write(weight.weight); }
                write.Flush(); bytes.Position = 0;
                using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            }
        }
        public struct Contact
        {
            public bool Hit;
            public float Depth;
            public int Triangle, Capsule, Surface;
            public Vector3 AxisPoint, SurfacePoint, Normal;
        }
        public struct Capsule
        {
            public Vector3 A, B;
            public float Radius;
        }

        // Exact float comparison, intentionally not Unity's approximate ==.
        public static bool Exact(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++) if (a[i] != b[i]) return false;
            return true;
        }
        public static bool Exact(Capsule a, Capsule b) => a.A.Equals(b.A) && a.B.Equals(b.B) && a.Radius == b.Radius;

        /// <summary>Same full-weight arithmetic, with exact input pose caching and
        /// exact position/ordered-weight duplicate reuse. Triangle indices stay original.</summary>
        public sealed class ExactSkinCache
        {
            private struct VertexKey : IEquatable<VertexKey>
            {
                public Vector3 P;
                public byte Count;
                public int B0, B1, B2, B3;
                public float W0, W1, W2, W3;
                public bool Equals(VertexKey o) => P.Equals(o.P) && Count == o.Count &&
                    B0 == o.B0 && B1 == o.B1 && B2 == o.B2 && B3 == o.B3 &&
                    W0 == o.W0 && W1 == o.W1 && W2 == o.W2 && W3 == o.W3;
                public override bool Equals(object o) => o is VertexKey k && Equals(k);
                public override int GetHashCode()
                {
                    unchecked { int h = P.GetHashCode() * 397 ^ Count;
                        h = h * 397 ^ B0; h = h * 397 ^ W0.GetHashCode(); h = h * 397 ^ B1; h = h * 397 ^ W1.GetHashCode();
                        h = h * 397 ^ B2; h = h * 397 ^ W2.GetHashCode(); h = h * 397 ^ B3; return h * 397 ^ W3.GetHashCode(); }
                }
            }
            private readonly SecondaryCollisionSurface _source;
            private readonly Vector3[] _output;
            private readonly int[] _representatives;
            private readonly Matrix4x4[] _previousWorld;
            private Matrix4x4 _previousInverse;
            private bool _hasPose;
            public readonly int[] UsedBones;
            public readonly Matrix4x4[] SkinMatrices;
            public int UniqueVertexCount { get; private set; }
            public int Revision { get; private set; }
            public int SkinEvaluations { get; private set; }
            public int CacheHits { get; private set; }
            public ExactSkinCache(SecondaryCollisionSurface source, Vector3[] output)
            {
                _source = source; _output = output;
                Need(output.Length == source.Positions.Length, "Exact skin output must retain every original vertex.");
                _representatives = new int[output.Length];
                var keys = new Dictionary<VertexKey, int>(); var used = new HashSet<int>();
                for (int v = 0; v < output.Length; v++)
                {
                    var key = new VertexKey { P = source.Positions[v], Count = source.WeightCounts[v] };
                    Need(key.Count > 0 && key.Count <= 4, "Original full-weight count is required.");
                    for (int j = 0; j < key.Count; j++)
                    {
                        var w = source.Weights[source.WeightStarts[v] + j];
                        if (w.Weight > 0) used.Add(w.Bone);
                        switch (j) { case 0: key.B0 = w.Bone; key.W0 = w.Weight; break;
                            case 1: key.B1 = w.Bone; key.W1 = w.Weight; break;
                            case 2: key.B2 = w.Bone; key.W2 = w.Weight; break;
                            default: key.B3 = w.Bone; key.W3 = w.Weight; break; }
                    }
                    if (!keys.TryGetValue(key, out int representative)) { representative = v; keys.Add(key, v); }
                    _representatives[v] = representative;
                }
                UniqueVertexCount = keys.Count; UsedBones = new int[used.Count]; used.CopyTo(UsedBones); Array.Sort(UsedBones);
                SkinMatrices = new Matrix4x4[source.Bindposes.Length]; _previousWorld = new Matrix4x4[SkinMatrices.Length];
            }
            public bool Refresh(Matrix4x4 representationInverse, Matrix4x4[] boneWorld)
            {
                bool inverseChanged = !_hasPose || !Exact(_previousInverse, representationInverse);
                bool changed = inverseChanged;
                for (int j = 0; j < UsedBones.Length; j++)
                {
                    int i = UsedBones[j];
                    if (inverseChanged || !Exact(_previousWorld[i], boneWorld[i]))
                    { SkinMatrices[i] = representationInverse * boneWorld[i] * _source.Bindposes[i]; _previousWorld[i] = boneWorld[i]; changed = true; }
                }
                if (!changed) { CacheHits++; return false; }
                for (int v = 0; v < _output.Length; v++)
                {
                    int original = _representatives[v];
                    if (original != v) { _output[v] = _output[original]; continue; }
                    Vector3 point = Vector3.zero;
                    for (int i = _source.WeightStarts[v]; i < _source.WeightStarts[v] + _source.WeightCounts[v]; i++)
                    { var w = _source.Weights[i]; point += SkinMatrices[w.Bone].MultiplyPoint3x4(_source.Positions[v]) * w.Weight; }
                    Need(Finite(point), "Nonfinite exact chain skin."); _output[v] = point;
                }
                _previousInverse = representationInverse; _hasPose = true; SkinEvaluations++; Revision++; return true;
            }
        }
        public sealed class TriangleTree
        {
            private struct Node { public Vector3 Min, Max, Center; public float Radius; public int Left, Right, Start, Count; }
            private readonly Vector3[] _positions;
            // Constructor-only reuse: a triangle center is invariant throughout
            // Build, even though sort comparers request it repeatedly at each level.
            // Query/refit geometry and the original tie-breaking order are unchanged.
            private readonly Vector3[] _buildCenters;
            private readonly int[] _triangles, _order;
            private readonly Node[] _nodes;
            private int _next;
            public int TriangleCount => _order.Length;
            public int LastTriangleTests { get; private set; }
            public TriangleTree(Vector3[] positions, int[] triangles)
            {
                if (positions == null || triangles == null || triangles.Length == 0 || triangles.Length % 3 != 0)
                    throw new ArgumentException("Complete triangle geometry required.");
                _positions = positions; _triangles = triangles; _order = new int[triangles.Length / 3];
                for (int i = 0; i < _order.Length; i++) _order[i] = i;
                foreach (int index in triangles) if (index < 0 || index >= positions.Length) throw new ArgumentException("Triangle index outside source geometry.");
                _buildCenters = new Vector3[_order.Length];
                for (int triangle = 0; triangle < _order.Length; triangle++)
                {
                    int i = triangle * 3;
                    _buildCenters[triangle] = (_positions[_triangles[i]] + _positions[_triangles[i + 1]] + _positions[_triangles[i + 2]]) / 3f;
                }
                _nodes = new Node[_order.Length * 2]; Build(0, _order.Length); Refit();
                _buildCenters = null; // No center cache is retained for changing poses.
            }
            private int Build(int start, int count)
            {
                int index = _next++; var node = new Node { Start = start, Count = count, Left = -1, Right = -1 };
                if (count > 8)
                {
                    Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
                    for (int i = start; i < start + count; i++) { Vector3 p = Center(_order[i]); min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                    Vector3 extent = max - min; int axis = extent.x >= extent.y && extent.x >= extent.z ? 0 : extent.y >= extent.z ? 1 : 2;
                    Array.Sort(_order, start, count, Comparer<int>.Create((a, b) => { int c = Center(a)[axis].CompareTo(Center(b)[axis]); return c != 0 ? c : a.CompareTo(b); }));
                    int half = count / 2; node.Left = Build(start, half); node.Right = Build(start + half, count - half); node.Count = 0;
                }
                _nodes[index] = node; return index;
            }
            private Vector3 Center(int triangle) => _buildCenters[triangle];
            public void Refit() => Refit(0);
            private void Refit(int index)
            {
                var node = _nodes[index];
                if (node.Count > 0)
                {
                    node.Min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); node.Max = -node.Min;
                    for (int i = node.Start; i < node.Start + node.Count; i++) for (int corner = 0; corner < 3; corner++)
                    { Vector3 p = _positions[_triangles[_order[i] * 3 + corner]]; node.Min = Vector3.Min(node.Min, p); node.Max = Vector3.Max(node.Max, p); }
                }
                else { Refit(node.Left); Refit(node.Right); node.Min = Vector3.Min(_nodes[node.Left].Min, _nodes[node.Right].Min); node.Max = Vector3.Max(_nodes[node.Left].Max, _nodes[node.Right].Max); }
                node.Center = (node.Min + node.Max) * .5f;
                // Circumscribed bound is enlarged, never used as collision geometry.
                node.Radius = (node.Max - node.Min).magnitude * .500001f + .000001f;
                _nodes[index] = node;
            }
            public Contact Query(Capsule capsule)
            {
                LastTriangleTests = 0;
                var result = new Contact { Triangle = -1, Capsule = -1, Surface = -1 };
                Vector3 expansion = Vector3.one * capsule.Radius;
                Query(0, capsule, Vector3.Min(capsule.A, capsule.B) - expansion, Vector3.Max(capsule.A, capsule.B) + expansion, ref result);
                return result;
            }
            private void Query(int index, Capsule capsule, Vector3 min, Vector3 max, ref Contact result)
            {
                var node = _nodes[index];
                if (node.Max.x < min.x || node.Min.x > max.x || node.Max.y < min.y || node.Min.y > max.y || node.Max.z < min.z || node.Min.z > max.z) return;
                Vector3 closest = ClosestSegmentPoint(node.Center, capsule.A, capsule.B);
                float boundRadius = node.Radius + capsule.Radius;
                if ((closest - node.Center).sqrMagnitude > boundRadius * boundRadius) return;
                if (node.Count == 0) { Query(node.Left, capsule, min, max, ref result); Query(node.Right, capsule, min, max, ref result); return; }
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    int triangle = _order[i], t = triangle * 3;
                    Vector3 a = _positions[_triangles[t]], b = _positions[_triangles[t + 1]], c = _positions[_triangles[t + 2]];
                    // Filled triangle is contained in these bounds. Only disjoint
                    // capsule bounding boxes are rejected; triangle math is unchanged.
                    if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < min.x || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > max.x ||
                        Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < min.y || Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > max.y ||
                        Mathf.Max(a.z, Mathf.Max(b.z, c.z)) < min.z || Mathf.Min(a.z, Mathf.Min(b.z, c.z)) > max.z) continue;
                    LastTriangleTests++;
                    float distance = SegmentTriangleDistance(capsule.A, capsule.B, a, b, c, out Vector3 axis, out Vector3 surface);
                    float depth = capsule.Radius - distance;
                    if (depth <= result.Depth) continue;
                    Vector3 normal = surface - axis;
                    if (normal.sqrMagnitude < 1e-16f)
                    {
                        Vector3 center = (a + b + c) / 3f;
                        normal = center - ClosestSegmentPoint(center, capsule.A, capsule.B);
                        if (normal.sqrMagnitude < 1e-16f) normal = Vector3.Cross(b - a, c - a);
                    }
                    result = new Contact { Hit = true, Depth = depth, Triangle = triangle, AxisPoint = axis,
                        SurfacePoint = surface, Normal = normal.sqrMagnitude > 1e-20f ? normal.normalized : Vector3.zero };
                }
            }
        }

        // All-angle rotation-vector conversion is pure managed math for contract tests.
        public static Quaternion Rotation(Vector3 angle)
        {
            double length = Math.Sqrt(angle.sqrMagnitude);
            if (length < 1e-12) return Quaternion.identity;
            double s = Math.Sin(length * .5) / length;
            return new Quaternion((float)(angle.x * s), (float)(angle.y * s), (float)(angle.z * s), (float)Math.Cos(length * .5));
        }
        public static Vector3 Log(Quaternion q)
        {
            double length = Math.Sqrt(q.x * (double)q.x + q.y * (double)q.y + q.z * (double)q.z + q.w * (double)q.w);
            if (length < 1e-12 || double.IsNaN(length)) return Vector3.zero;
            double sign = q.w < 0 ? -1 : 1, x = q.x * sign / length, y = q.y * sign / length, z = q.z * sign / length, w = q.w * sign / length;
            double vector = Math.Sqrt(x * x + y * y + z * z);
            if (vector < 1e-12) return Vector3.zero;
            double factor = 2 * Math.Atan2(vector, w) / vector;
            return new Vector3((float)(x * factor), (float)(y * factor), (float)(z * factor));
        }
        public static Quaternion InverseUnit(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);
        public static Vector3 CorrectWorldRotation(Vector3 currentAngle, Quaternion rest, Quaternion parentWorld,
            Vector3 worldIncrementRadians, float maximumRadians)
        {
            Quaternion world = parentWorld * rest * Rotation(currentAngle);
            Quaternion corrected = InverseUnit(rest) * InverseUnit(parentWorld) * Rotation(worldIncrementRadians) * world;
            return Vector3.ClampMagnitude(Log(corrected), Mathf.Max(0f, maximumRadians));
        }
        public static void RemoveInwardVelocity(ref Vector3 velocity, Vector3 outwardAngleDirection)
        {
            float length = outwardAngleDirection.magnitude;
            if (length <= 1e-9f) return;
            Vector3 direction = outwardAngleDirection / length;
            velocity -= direction * Mathf.Min(0f, Vector3.Dot(velocity, direction));
        }
        public static Vector3 Barycentric(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 u = b - a, v = c - a, p = point - a;
            double uu = Vector3.Dot(u, u), uv = Vector3.Dot(u, v), vv = Vector3.Dot(v, v), pu = Vector3.Dot(p, u), pv = Vector3.Dot(p, v);
            double denominator = uu * vv - uv * uv;
            if (Math.Abs(denominator) <= 1e-24)
            {
                float da = (point - a).sqrMagnitude, db = (point - b).sqrMagnitude, dc = (point - c).sqrMagnitude;
                return da <= db && da <= dc ? Vector3.right : db <= dc ? Vector3.up : Vector3.forward;
            }
            float y = (float)((vv * pu - uv * pv) / denominator), z = (float)((uu * pv - uv * pu) / denominator);
            // Contact lies on the filled triangle; clamp only float roundoff and renormalize.
            Vector3 result = Vector3.Max(Vector3.zero, new Vector3(1f - y - z, y, z)); return result / (result.x + result.y + result.z);
        }
        public static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        private static void Need(bool value, string error) { if (!value) throw new ArgumentException(error); }

        public static float SegmentTriangleDistance(Vector3 start, Vector3 end, Vector3 a, Vector3 b, Vector3 c, out Vector3 onSegment, out Vector3 onTriangle)
        {
            Need(Finite(start) && Finite(end) && Finite(a) && Finite(b) && Finite(c), "Nonfinite segment/triangle evidence.");
            Vector3 direction = end - start;
            double e1x = b.x - (double)a.x, e1y = b.y - (double)a.y, e1z = b.z - (double)a.z;
            double e2x = c.x - (double)a.x, e2y = c.y - (double)a.y, e2z = c.z - (double)a.z;
            double px = direction.y * e2z - direction.z * e2y, py = direction.z * e2x - direction.x * e2z, pz = direction.x * e2y - direction.y * e2x;
            double denominator = e1x * px + e1y * py + e1z * pz;
            if (Math.Abs(denominator) > 1e-24)
            {
                double sx = start.x - (double)a.x, sy = start.y - (double)a.y, sz = start.z - (double)a.z;
                double u = (sx * px + sy * py + sz * pz) / denominator;
                double qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
                double v = (direction.x * qx + direction.y * qy + direction.z * qz) / denominator;
                double t = (e2x * qx + e2y * qy + e2z * qz) / denominator;
                if (t >= 0 && t <= 1 && u >= 0 && v >= 0 && u + v <= 1)
                {
                    onSegment = start + direction * (float)t; onTriangle = a + (b - a) * (float)u + (c - a) * (float)v; return 0f;
                }
            }
            onSegment = start; onTriangle = ClosestTrianglePoint(start, a, b, c); float squared = (onSegment - onTriangle).sqrMagnitude;
            Vector3 q = ClosestTrianglePoint(end, a, b, c); if ((end - q).sqrMagnitude < squared) { onSegment = end; onTriangle = q; squared = (end - q).sqrMagnitude; }
            ConsiderEdge(start, end, a, b, ref squared, ref onSegment, ref onTriangle);
            ConsiderEdge(start, end, b, c, ref squared, ref onSegment, ref onTriangle);
            ConsiderEdge(start, end, c, a, ref squared, ref onSegment, ref onTriangle);
            return Mathf.Sqrt(Mathf.Max(0f, squared));
        }
        private static void ConsiderEdge(Vector3 start, Vector3 end, Vector3 a, Vector3 b, ref float squared, ref Vector3 onSegment, ref Vector3 onTriangle)
        {
            SegmentSegment(start, end, a, b, out Vector3 p, out Vector3 q); float d = (p - q).sqrMagnitude;
            if (d < squared) { squared = d; onSegment = p; onTriangle = q; }
        }
        public static Vector3 ClosestSegmentPoint(Vector3 point, Vector3 start, Vector3 end)
        { Vector3 d = end - start; return start + d * (d.sqrMagnitude > 1e-20f ? Mathf.Clamp01(Vector3.Dot(point - start, d) / d.sqrMagnitude) : 0f); }
        public static void SegmentSegment(Vector3 p, Vector3 q, Vector3 a, Vector3 b, out Vector3 first, out Vector3 second)
        {
            Vector3 d = q - p, e = b - a, r = p - a; float dd = Vector3.Dot(d, d), ee = Vector3.Dot(e, e), er = Vector3.Dot(e, r), s, t;
            if (dd <= 1e-20f && ee <= 1e-20f) { first = p; second = a; return; }
            if (dd <= 1e-20f) { s = 0; t = Mathf.Clamp01(er / ee); }
            else
            {
                float dr = Vector3.Dot(d, r);
                if (ee <= 1e-20f) { t = 0; s = Mathf.Clamp01(-dr / dd); }
                else
                {
                    float de = Vector3.Dot(d, e), denominator = dd * ee - de * de;
                    s = denominator > 1e-20f ? Mathf.Clamp01((de * er - dr * ee) / denominator) : 0f; t = (de * s + er) / ee;
                    if (t < 0) { t = 0; s = Mathf.Clamp01(-dr / dd); } else if (t > 1) { t = 1; s = Mathf.Clamp01((de - dr) / dd); }
                }
            }
            first = p + d * s; second = a + e * t;
        }
        private static Vector3 ClosestTrianglePoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a;
            if (Vector3.Cross(ab, ac).sqrMagnitude < 1e-24f)
            {
                Vector3 x = ClosestSegmentPoint(p, a, b), y = ClosestSegmentPoint(p, b, c), z = ClosestSegmentPoint(p, c, a);
                return (p - x).sqrMagnitude <= (p - y).sqrMagnitude && (p - x).sqrMagnitude <= (p - z).sqrMagnitude ? x : (p - y).sqrMagnitude <= (p - z).sqrMagnitude ? y : z;
            }
            Vector3 ap = p - a; float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap); if (d1 <= 0 && d2 <= 0) return a;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp); if (d3 >= 0 && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2; if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp); if (d6 >= 0 && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6; if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4; if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6));
            float inv = 1f / (va + vb + vc); return a + ab * (vb * inv) + ac * (vc * inv);
        }
    }
}
