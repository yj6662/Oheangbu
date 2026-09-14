using System.Collections.Generic;
using UnityEngine;
using Geometry = Oheangbu.Presentation.PlayerSecondaryCollisionGeometry;

namespace Oheangbu.Presentation
{
    public sealed partial class PlayerSecondaryCollisionRig
    {
        private sealed class BoneCapsule
        {
            public Transform Bone;
            public Geometry.Capsule Local;
            public Matrix4x4 Matrix;
            public bool HasMatrix;
            public Vector3 A, B;
            public float Radius;
            public Bounds Bounds;
        }
        public bool OptimizeRuntimeCapsuleQueries = true;
        private Bounds[] _runtimeBodyBounds;
        private int _runtimeBodyBoundsRevision = -1;
        public int LastRuntimeBroadphaseSkips { get; private set; }
        public int LastRuntimeNarrowphaseTests { get; private set; }
        public int LastRuntimeQueryCacheHits { get; private set; }
        private bool _runtimeCapsules;
        private int _capsuleIterations = 3;
        private readonly Dictionary<PartState, BoneCapsule[]> _partCapsules = new Dictionary<PartState, BoneCapsule[]>();
        public bool UsesRuntimeCapsules => _runtimeCapsules;
        public int RuntimeCapsuleCount { get; private set; }

        public void SetRuntimeCapsules(bool enabled, int iterations)
        {
            _body?.SetBoneAttachedRuntimeCapsules(enabled);
            _capsuleIterations = Mathf.Clamp(iterations, 1, 6);
            if (_runtimeCapsules == enabled) return;
            _runtimeCapsules = enabled;
            ResetHistory();
        }

        private void BuildRuntimeCapsules()
        {
            _runtimeBodyBoundsRevision = -1;
            RuntimeCapsuleCount = 0;
            foreach (var part in _states)
            {
                var groups = new Dictionary<Transform, List<Vector3>>();
                foreach (var bone in part.Binding.DrivenBones) groups.Add(bone, new List<Vector3>());
                foreach (var state in part.Surfaces)
                {
                    var surface = state.Binding;
                    if (part.Binding.Rigid) { groups[part.Binding.DrivenBones[0]].AddRange(surface.Positions); continue; }
                    for (int v = 0; v < surface.Positions.Length; v++)
                    {
                        float best = -1f; int index = -1; Transform owner = null;
                        for (int w = surface.WeightStarts[v]; w < surface.WeightStarts[v] + surface.WeightCounts[v]; w++)
                        {
                            var influence = surface.Weights[w];
                            Transform candidate = surface.Bones[influence.Bone];
                            while (candidate != null && !groups.ContainsKey(candidate)) candidate = candidate.parent;
                            if (candidate == null || influence.Weight <= best) continue;
                            best = influence.Weight; index = influence.Bone; owner = candidate;
                        }
                        if (owner == null) continue;
                        Vector3 boneLocal = surface.Bindposes[index].MultiplyPoint3x4(surface.Positions[v]);
                        Vector3 point = owner == surface.Bones[index] ? boneLocal
                            : owner.InverseTransformPoint(surface.Bones[index].TransformPoint(boneLocal));
                        groups[owner].Add(point);
                    }
                }
                var capsules = new List<BoneCapsule>();
                foreach (var group in groups)
                    if (group.Value.Count > 0) capsules.Add(new BoneCapsule { Bone = group.Key, Local = FitRuntimeCapsule(group.Value) });
                Need(capsules.Count > 0, "Missing runtime collision proxy for " + part.Binding.Name);
                _partCapsules.Add(part, capsules.ToArray()); RuntimeCapsuleCount += capsules.Count;
            }
        }

        // One-time fitting; the runtime only transforms these endpoints with their owner bone.
        public static Geometry.Capsule FitRuntimeCapsule(IReadOnlyList<Vector3> points)
        {
            Need(points != null && points.Count > 0, "Capsule fitting needs source points.");
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) bounds.Encapsulate(p);
            Vector3 size = bounds.size, center = bounds.center;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            float radiusSquared = 0f;
            foreach (var p in points) { Vector3 radial = p - center; radial[axis] = 0; radiusSquared = Mathf.Max(radiusSquared, radial.sqrMagnitude); }
            float radius = Mathf.Max(.001f, Mathf.Sqrt(radiusSquared));
            float first = float.PositiveInfinity, last = float.NegativeInfinity;
            foreach (var p in points)
            {
                Vector3 radial = p - center; radial[axis] = 0;
                float reach = Mathf.Sqrt(Mathf.Max(0, radius * radius - radial.sqrMagnitude));
                first = Mathf.Min(first, p[axis] + reach); last = Mathf.Max(last, p[axis] - reach);
            }
            if (first > last) first = last = (first + last) * .5f;
            Vector3 a = center, b = center; a[axis] = first; b[axis] = last;
            return new Geometry.Capsule { A = a, B = b, Radius = radius };
        }

        private Geometry.Contact QueryRuntimeCapsules(PartState part)
        {
            if (!OptimizeRuntimeCapsuleQueries) return QueryRuntimeCapsulesReference(part);
            if (_runtimeBodyBounds == null || _runtimeBodyBounds.Length != _capsules.Length)
            { _runtimeBodyBounds = new Bounds[_capsules.Length]; _runtimeBodyBoundsRevision = -1; }
            if (_runtimeBodyBoundsRevision != _capsuleRevision)
            {
                for (int i = 0; i < _capsules.Length; i++)
                    _runtimeBodyBounds[i] = CapsuleBounds(_capsules[i].A, _capsules[i].B, _capsules[i].Radius + ContactMargin);
                _runtimeBodyBoundsRevision = _capsuleRevision;
            }
            bool changed = false;
            var proxies = _partCapsules[part];
            foreach (var proxy in proxies)
            {
                Matrix4x4 matrix = proxy.Bone.localToWorldMatrix;
                if (proxy.HasMatrix && Geometry.Exact(matrix, proxy.Matrix)) continue;
                changed = true; proxy.Matrix = matrix; proxy.HasMatrix = true;
                proxy.A = matrix.MultiplyPoint3x4(proxy.Local.A); proxy.B = matrix.MultiplyPoint3x4(proxy.Local.B);
                proxy.Radius = proxy.Local.Radius * proxy.Bone.lossyScale.x;
                proxy.Bounds = CapsuleBounds(proxy.A, proxy.B, proxy.Radius);
            }
            part.Measurement.queries++;
            if (!changed && part.HasQuery && part.LastCapsuleRevision == _capsuleRevision)
            { LastRuntimeQueryCacheHits++; part.Measurement.queryCacheHits++; return part.LastResult; }
            var best = new Geometry.Contact { Triangle = -1, Surface = -1, Capsule = -1 };
            foreach (var proxy in proxies)
                for (int i = 0; i < _capsules.Length; i++)
                {
                    if (!proxy.Bounds.Intersects(_runtimeBodyBounds[i])) { LastRuntimeBroadphaseSkips++; continue; }
                    var body = _capsules[i]; LastRuntimeNarrowphaseTests++;
                    Geometry.SegmentSegment(proxy.A, proxy.B, body.A, body.B, out Vector3 onPart, out Vector3 onBody);
                    Vector3 delta = onPart - onBody; float totalRadius = proxy.Radius + body.Radius + ContactMargin;
                    if (delta.sqrMagnitude >= totalRadius * totalRadius) continue;
                    float distance = delta.magnitude, depth = totalRadius - distance;
                    if (depth <= 0 || depth <= best.Depth) continue;
                    Vector3 normal = distance > .000001f ? delta / distance
                        : ((proxy.A + proxy.B) * .5f - (body.A + body.B) * .5f).normalized;
                    if (normal.sqrMagnitude < .001f) normal = _representation.forward;
                    best = new Geometry.Contact { Hit = true, Depth = depth, Capsule = i, Surface = -1, Triangle = -1,
                        Normal = normal, AxisPoint = onBody, SurfacePoint = onPart - normal * proxy.Radius };
                }
            part.HasQuery = true; part.LastCapsuleRevision = _capsuleRevision; part.LastResult = best;
            return best;
        }

        private static Bounds CapsuleBounds(Vector3 a, Vector3 b, float radius)
        {
            var bounds = new Bounds((a + b) * .5f, Vector3.zero);
            bounds.SetMinMax(Vector3.Min(a, b) - Vector3.one * radius, Vector3.Max(a, b) + Vector3.one * radius);
            return bounds;
        }

        public string ValidateRuntimeQueryEquivalence()
        {
            if (!_frameReady || !_runtimeCapsules) return "NOT_READY";
            bool saved = OptimizeRuntimeCapsuleQueries; float largest = 0;
            try
            {
                OptimizeRuntimeCapsuleQueries = true;
                foreach (var part in _states)
                {
                    part.HasQuery = false;
                    var expected = QueryRuntimeCapsulesReference(part);
                    var actual = QueryRuntimeCapsules(part);
                    float error = Mathf.Abs(expected.Depth - actual.Depth); largest = Mathf.Max(largest, error);
                    Need(expected.Hit == actual.Hit && error <= .00001f, "Runtime contact changed for " + part.Binding.Name);
                    var cached = QueryRuntimeCapsules(part);
                    Need(cached.Hit == actual.Hit && cached.Depth == actual.Depth, "Runtime contact cache changed the answer.");
                }
                return "PASS " + _states.Length + " actual parts, maximum depth difference metres=" + largest;
            }
            finally { OptimizeRuntimeCapsuleQueries = saved; ResetHistory(); }
        }

        private Geometry.Contact QueryRuntimeCapsulesReference(PartState part)
        {
            var best = new Geometry.Contact { Triangle = -1, Surface = -1, Capsule = -1 };
            part.Measurement.queries++;
            foreach (var proxy in _partCapsules[part])
            {
                Vector3 a = proxy.Bone.TransformPoint(proxy.Local.A), b = proxy.Bone.TransformPoint(proxy.Local.B);
                float radius = proxy.Local.Radius * proxy.Bone.lossyScale.x;
                for (int i = 0; i < _capsules.Length; i++)
                {
                    var body = _capsules[i];
                    Geometry.SegmentSegment(a, b, body.A, body.B, out Vector3 onPart, out Vector3 onBody);
                    Vector3 delta = onPart - onBody; float distance = delta.magnitude;
                    float depth = radius + body.Radius + ContactMargin - distance;
                    if (depth <= 0 || depth <= best.Depth) continue;
                    Vector3 normal = distance > .000001f ? delta / distance
                        : ((a + b) * .5f - (body.A + body.B) * .5f).normalized;
                    if (normal.sqrMagnitude < .001f) normal = _representation.forward;
                    best = new Geometry.Contact { Hit = true, Depth = depth, Capsule = i, Surface = -1, Triangle = -1,
                        Normal = normal, AxisPoint = onBody, SurfacePoint = onPart - normal * radius };
                }
            }
            return best;
        }
    }
}
