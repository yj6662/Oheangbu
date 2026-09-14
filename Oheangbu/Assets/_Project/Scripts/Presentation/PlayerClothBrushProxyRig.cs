using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace Oheangbu.Presentation
{
    /// <summary>Visual-only native Cloth proxies following the actual world brush after final hand IK.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(1500)]
    public sealed class PlayerClothBrushProxyRig : MonoBehaviour
    {
        [Serializable] private sealed class Binding
        {
            public CapsuleCollider Capsule;
            public Transform Start, End;
            public Vector3 StartLocal, EndLocal;
            public int[] Vertices = Array.Empty<int>();
            public bool Bristle;
        }
        [SerializeField] private Transform _worldRoot, _proxyRoot;
        [SerializeField] private GameObject _brush;
        [SerializeField] private MeshFilter _shaft;
        [SerializeField] private SkinnedMeshRenderer _bristles;
        [SerializeField] private Binding[] _bindings = Array.Empty<Binding>();
        [SerializeField] private int _shaftVertexCount, _bristleVertexCount;
        private Mesh _baked;
        private Mesh _cachedShaftMesh;
        private Vector3[] _shaftPositions;
        private readonly List<Vector3> _bristlePositions = new List<Vector3>(10000);
        private Mesh _cachedBristleMesh;
        private int[][] _representativeVertices;
        private LocalPose[] _sourcePose;
        private float[] _shapeWeights;
        private Vector3 _brushWorldScale;
        private SkinQuality _bakedQuality;
        private SkinWeights _bakedGlobalSkinWeights;
        private bool _bakePoseValid;
        public bool CacheUnchangedRadiusFits = true;
        public bool CacheStaticShaftRadius = true;
        private float[] _cachedRadii;
        private bool _radiiValid;
        private bool _shaftRadiusValid;
        private Vector3 _shaftRadiusStart, _shaftRadiusEnd;
        private float _shaftLocalRadius;
        public int StaticShaftRadiusFitCount { get; private set; }
        public int StaticShaftRadiusCacheHits { get; private set; }
        public int RadiusFitCount { get; private set; }
        public int RadiusCacheHits { get; private set; }
        private struct LocalPose
        {
            public Transform Node, Parent;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
        }
        public string LastError { get; private set; }
        public bool IsConfigured { get; private set; }
        public GameObject WorldBrush => _brush;
        public CapsuleCollider[] Capsules => _bindings.Select(b => b.Capsule).ToArray();
        public int RefreshCount { get; private set; }
        public float LargestRadiusMeters { get; private set; }
        public double LastRefreshMilliseconds { get; private set; }
        public long LastRefreshAllocatedBytes { get; private set; }
        public int BakeCount { get; private set; }
        public int CachedBakeReuseCount { get; private set; }
        public int UniqueShaftVertices { get; private set; }
        public int UniqueBristleVertices { get; private set; }
        public int LastBakeFrame { get; private set; } = -1;

        public bool Configure(Transform worldRoot, GameObject brush, out string error)
        {
            error = null;
            try
            {
                Require(_proxyRoot == null && worldRoot != null && brush != null, "Use a fresh component and an explicit world brush.");
                Require(transform == worldRoot && Uniform(worldRoot.lossyScale), "Proxy owner must be the positive-uniform world representation root.");
                _worldRoot = worldRoot; _brush = brush;
                _shaft = brush.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == "DosaBrushV2_Handle");
                _bristles = brush.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s => s.name == "DosaBrushV2_Bristles");
                Require(_shaft.sharedMesh != null && _shaft.sharedMesh.isReadable && _bristles.sharedMesh != null && _bristles.sharedMesh.isReadable, "Actual readable shaft and bristle meshes are required.");
                _shaftVertexCount = _shaft.sharedMesh.vertexCount; _bristleVertexCount = _bristles.sharedMesh.vertexCount;
                Require(_shaftVertexCount > 0 && _bristleVertexCount > 0, "Brush geometry is empty.");
                Transform Find(string name) => brush.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
                var chain = Enumerable.Range(1, 6).Select(i => Find("Bristle_" + i.ToString("00"))).Concat(new[] { Find("TipSocket") }).ToArray();
                Transform grip = Find("GripSocket"); Vector3 axis = (chain[0].position - grip.position).normalized;
                Require(axis.sqrMagnitude > .99f, "Brush shaft axis has no measured extent.");
                Vector3[] shaftWorld = _shaft.sharedMesh.vertices.Select(_shaft.transform.TransformPoint).ToArray();
                float low = shaftWorld.Min(p => Vector3.Dot(p - grip.position, axis)), high = shaftWorld.Max(p => Vector3.Dot(p - grip.position, axis));
                var root = new GameObject("DosaV2_ClothBrushProxies") { layer = 2 }; _proxyRoot = root.transform; _proxyRoot.SetParent(worldRoot, false);
                var bindings = new List<Binding> { new Binding { Capsule = Create("DosaV2_ClothProxy_Brush_Shaft"), Start = _shaft.transform, End = _shaft.transform,
                    StartLocal = _shaft.transform.InverseTransformPoint(grip.position + axis * low), EndLocal = _shaft.transform.InverseTransformPoint(grip.position + axis * high),
                    Vertices = Enumerable.Range(0, _shaftVertexCount).ToArray() } };
                EnsureBake(); _bristles.BakeMesh(_baked, false);
                Require(_baked.vertexCount == _bristleVertexCount, "Actual bristle bake changed source vertex indexing.");
                Vector3[] bristleWorld = _baked.vertices.Select(_bristles.transform.TransformPoint).ToArray();
                var groups = Enumerable.Range(0, 6).Select(_ => new List<int>()).ToArray();
                for (int v = 0; v < bristleWorld.Length; v++)
                {
                    int nearest = 0; for (int i = 1; i < 6; i++) if (Distance(bristleWorld[v], chain[i].position, chain[i + 1].position) < Distance(bristleWorld[v], chain[nearest].position, chain[nearest + 1].position)) nearest = i;
                    groups[nearest].Add(v);
                }
                for (int i = 0; i < 6; i++)
                {
                    Require(groups[i].Count > 0, "Every bristle segment needs actual source vertices.");
                    bindings.Add(new Binding { Capsule = Create("DosaV2_ClothProxy_Brush_Bristle_" + i), Start = chain[i], End = chain[i + 1], Bristle = true, Vertices = groups[i].ToArray() });
                }
                _bindings = bindings.ToArray(); Require(RefreshNow(), LastError); return true;
            }
            catch (Exception exception)
            {
                error = LastError = exception.GetBaseException().Message; IsConfigured = false;
                if (_proxyRoot != null) { DestroyOwned(_proxyRoot.gameObject); _proxyRoot = null; } _bindings = Array.Empty<Binding>(); return false;
            }
        }
        private CapsuleCollider Create(string name)
        {
            var obj = new GameObject(name) { layer = 2 }; obj.transform.SetParent(_proxyRoot, false);
            var capsule = obj.AddComponent<CapsuleCollider>(); capsule.direction = 1; capsule.isTrigger = true; capsule.excludeLayers = -1; capsule.includeLayers = 0;
            Require(PlayerSecondaryMotionRig.ValidateProxy(capsule, out string error), error); return capsule;
        }
        private void OnEnable() { _bakePoseValid = false; if (_bindings.Length > 0) RefreshNow(); }
        private void LateUpdate() { if (_bindings.Length > 0) RefreshNow(); }
        private void OnDisable() { foreach (var binding in _bindings) if (binding.Capsule != null) binding.Capsule.enabled = false; IsConfigured = false; _bakePoseValid = false; }
        private void OnDestroy() { if (_baked != null) DestroyOwned(_baked); if (_proxyRoot != null) DestroyOwned(_proxyRoot.gameObject); }
        private void EnsureBake() { if (_baked == null) _baked = new Mesh { name = "ActualWorldBristleProxyBake" }; }

        /// <summary>Fixture may call after direct static bone posing, before the native Cloth solve.</summary>
        public bool RefreshNow() => Refresh(false);

        /// <summary>Diagnostic reference path: still uses every equivalent source point but forces an actual native bake.</summary>
        public bool RefreshWithForcedBake() => Refresh(true);

        /// <summary>Call after deliberate runtime edits to source mesh data, skin bindings or brush-internal hierarchy.
        /// Normal hand/brush motion and bone/shape posing are detected automatically without invalidation.</summary>
        public void InvalidateGeometryCache()
        {
            _cachedShaftMesh = null; _cachedBristleMesh = null; _shaftPositions = null;
            _representativeVertices = null; _sourcePose = null; _shapeWeights = null; _bakePoseValid = false;
            _shaftRadiusValid = false;
        }

        private bool Refresh(bool forceBake)
        {
            long started = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                Require(_worldRoot != null && _proxyRoot != null && _proxyRoot.IsChildOf(_worldRoot) && Uniform(_worldRoot.lossyScale), "World proxy ownership/scale is invalid.");
                Require(_shaft != null && _bristles != null && _brush != null && _bindings.Length == 7, "Serialized actual brush references are incomplete.");
                Require(_shaft.sharedMesh.vertexCount == _shaftVertexCount && _bristles.sharedMesh.vertexCount == _bristleVertexCount, "Brush source topology changed; rebuild proxy source-index groups.");
                EnsureGeometryCache();
                EnsureBake();
                bool poseChanged = forceBake || !SourcePoseMatches();
                if (poseChanged)
                {
                    _bristles.BakeMesh(_baked, false);
                    Require(_baked.vertexCount == _bristleVertexCount, "Current bristle bake has a different vertex count.");
                    _baked.GetVertices(_bristlePositions); CaptureSourcePose(); BakeCount++; LastBakeFrame = Time.frameCount;
                }
                else CachedBakeReuseCount++;
                bool fitRadius = poseChanged || !_radiiValid || !CacheUnchangedRadiusFits;
                if (_cachedRadii == null || _cachedRadii.Length != _bindings.Length) { _cachedRadii = new float[_bindings.Length]; fitRadius = true; }
                Matrix4x4 shaftToWorld = _shaft.transform.localToWorldMatrix, bristlesToWorld = _bristles.transform.localToWorldMatrix;
                bool active = _brush.activeInHierarchy;
                for (int index = 0; index < _bindings.Length; index++)
                {
                    var binding = _bindings[index];
                    Require(binding.Capsule != null && binding.Start != null && binding.End != null && binding.Vertices.Length > 0, "Serialized brush capsule binding is incomplete.");
                    Require(PlayerSecondaryMotionRig.ValidateProxy(binding.Capsule, out string error), error);
                    Vector3 a = binding.Start.TransformPoint(binding.StartLocal), b = binding.End.TransformPoint(binding.EndLocal);
                    Vector3 axis = b - a; float inverseLengthSquared = axis.sqrMagnitude > 1e-12f ? 1f / axis.sqrMagnitude : 0f;
                    bool staticShaft = TryStaticShaftRadius(binding, index, shaftToWorld, out float shaftRadius);
                    float radiusSquared = 0f;
                    if (fitRadius && !staticShaft) foreach (int vertex in _representativeVertices[index])
                    {
                        Vector3 point = binding.Bristle ? bristlesToWorld.MultiplyPoint3x4(_bristlePositions[vertex]) : shaftToWorld.MultiplyPoint3x4(_shaftPositions[vertex]);
                        Require(Finite(point), "Actual brush vertex is nonfinite.");
                        float t = inverseLengthSquared > 0f ? Mathf.Clamp01(Vector3.Dot(point - a, axis) * inverseLengthSquared) : 0f;
                        radiusSquared = Mathf.Max(radiusSquared, (point - (a + axis * t)).sqrMagnitude);
                    }
                    float radius = staticShaft ? shaftRadius : fitRadius ? Mathf.Sqrt(radiusSquared) : _cachedRadii[index];
                    if (staticShaft) { _cachedRadii[index] = radius; RadiusCacheHits++; }
                    else if (fitRadius) { _cachedRadii[index] = radius; RadiusFitCount++; } else RadiusCacheHits++;
                    Require(Finite(a) && Finite(b) && Finite(radius) && radius > .000001f, "Actual brush capsule fit is empty/nonfinite.");
                    Vector3 delta = b - a; var capsule = binding.Capsule; float scale = capsule.transform.lossyScale.x;
                    capsule.transform.SetPositionAndRotation((a + b) * .5f, delta.sqrMagnitude > 1e-12f ? Quaternion.FromToRotation(Vector3.up, delta) : Quaternion.identity);
                    capsule.center = Vector3.zero; capsule.radius = radius / scale; capsule.height = (delta.magnitude + 2f * radius) / scale;
                    capsule.enabled = active; LargestRadiusMeters = Mathf.Max(LargestRadiusMeters, radius);
                }
                _radiiValid = true;
                IsConfigured = true; LastError = null; RefreshCount++; return true;
            }
            catch (Exception exception)
            {
                LastError = exception.GetBaseException().Message; IsConfigured = false; _radiiValid = false;
                _shaftRadiusValid = false;
                foreach (var binding in _bindings) if (binding.Capsule != null) binding.Capsule.enabled = false;
                return false;
            }
            finally { LastRefreshMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency; LastRefreshAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated; }
        }

        private bool TryStaticShaftRadius(Binding binding, int index, Matrix4x4 shaftToWorld, out float radius)
        {
            radius = 0f;
            if (binding.Bristle) return false;
            // The serialized shaft axis and vertices share one rigid local frame. Bristle
            // shape/bone changes do not change this radius, but nonuniform scale or shear can.
            // Keep the original world-point fit for those transforms and for reference runs.
            if (!CacheStaticShaftRadius || binding.Start != _shaft.transform || binding.End != _shaft.transform
                || !TryUniformLinearScale(shaftToWorld, out float scale))
            { _shaftRadiusValid = false; return false; }
            if (!_shaftRadiusValid || !_shaftRadiusStart.Equals(binding.StartLocal)
                || !_shaftRadiusEnd.Equals(binding.EndLocal))
            {
                Vector3 axis = binding.EndLocal - binding.StartLocal;
                float inverseLengthSquared = axis.sqrMagnitude > 1e-12f ? 1f / axis.sqrMagnitude : 0f;
                float radiusSquared = 0f;
                foreach (int vertex in _representativeVertices[index])
                {
                    Vector3 point = _shaftPositions[vertex]; Require(Finite(point), "Actual shaft vertex is nonfinite.");
                    float t = inverseLengthSquared > 0f ? Mathf.Clamp01(Vector3.Dot(point - binding.StartLocal, axis) * inverseLengthSquared) : 0f;
                    radiusSquared = Mathf.Max(radiusSquared, (point - (binding.StartLocal + axis * t)).sqrMagnitude);
                }
                _shaftLocalRadius = Mathf.Sqrt(radiusSquared);
                _shaftRadiusStart = binding.StartLocal; _shaftRadiusEnd = binding.EndLocal;
                _shaftRadiusValid = true; StaticShaftRadiusFitCount++;
            }
            else StaticShaftRadiusCacheHits++;
            // Cache only local geometry; a uniform world-scale change is applied immediately
            // rather than reusing a previously scaled radius or refitting identical local points.
            radius = _shaftLocalRadius * scale;
            return true;
        }

        private static bool TryUniformLinearScale(Matrix4x4 matrix, out float scale)
        {
            Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
            float xx = x.sqrMagnitude, yy = y.sqrMagnitude, zz = z.sqrMagnitude;
            scale = Mathf.Sqrt(xx);
            // Relative arithmetic tolerance only: it is much smaller than the existing
            // 10-micrometre source-point comparison gate at the authored brush scale.
            float tolerance = xx * .000001f;
            return Finite(scale) && scale > .000001f && Finite(yy) && Finite(zz)
                && Mathf.Abs(xx - yy) <= tolerance && Mathf.Abs(xx - zz) <= tolerance
                && Mathf.Abs(Vector3.Dot(x, y)) <= tolerance && Mathf.Abs(Vector3.Dot(x, z)) <= tolerance
                && Mathf.Abs(Vector3.Dot(y, z)) <= tolerance;
        }

        private void EnsureGeometryCache()
        {
            if (_cachedShaftMesh == _shaft.sharedMesh && _cachedBristleMesh == _bristles.sharedMesh && _representativeVertices != null) return;
            _cachedShaftMesh = _shaft.sharedMesh; _cachedBristleMesh = _bristles.sharedMesh;
            _shaftRadiusValid = false;
            _shaftPositions = _cachedShaftMesh.vertices;
            var shaftMap = BuildEquivalentVertexRepresentatives(_shaftPositions, null, null, null);
            Vector3[] rest = _cachedBristleMesh.vertices;
            byte[] counts; BoneWeight1[] weights;
            using (var sourceCounts = _cachedBristleMesh.GetBonesPerVertex()) counts = sourceCounts.ToArray();
            using (var sourceWeights = _cachedBristleMesh.GetAllBoneWeights()) weights = sourceWeights.ToArray();
            var skinBones = _bristles.bones;
            Require(weights.All(w => w.boneIndex >= 0 && w.boneIndex < skinBones.Length), "Source brush skin contains an invalid bone index.");
            var frames = new List<Vector3[]>();
            for (int shape = 0; shape < _cachedBristleMesh.blendShapeCount; shape++)
                for (int frame = 0; frame < _cachedBristleMesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    var delta = new Vector3[rest.Length];
                    _cachedBristleMesh.GetBlendShapeFrameVertices(shape, frame, delta, null, null);
                    frames.Add(delta);
                }
            var bristleMap = BuildEquivalentVertexRepresentatives(rest, counts, weights, frames.ToArray());
            UniqueShaftVertices = shaftMap.Distinct().Count(); UniqueBristleVertices = bristleMap.Distinct().Count();
            _representativeVertices = new int[_bindings.Length][];
            for (int b = 0; b < _bindings.Length; b++)
            {
                var binding = _bindings[b]; int[] map = binding.Bristle ? bristleMap : shaftMap;
                Require(binding.Vertices.All(v => v >= 0 && v < map.Length), "Serialized brush source-index group is out of range.");
                // Keep group membership and exact first representatives; all serialized full indices remain unchanged.
                _representativeVertices[b] = binding.Vertices.Select(v => map[v]).Distinct().ToArray();
            }
            var nodes = new HashSet<Transform>();
            void AddPath(Transform node)
            {
                Require(node != null && node.IsChildOf(_brush.transform), "Brush skin/renderer must share the explicit brush root.");
                for (var cursor = node; cursor != _brush.transform; cursor = cursor.parent) nodes.Add(cursor);
            }
            AddPath(_bristles.transform);
            AddPath(_shaft.transform);
            foreach (var binding in _bindings) { AddPath(binding.Start); AddPath(binding.End); }
            foreach (var bone in skinBones) AddPath(bone);
            _sourcePose = nodes.OrderBy(n => n.GetInstanceID()).Select(n => new LocalPose { Node = n, Parent = n.parent }).ToArray();
            _shapeWeights = new float[_cachedBristleMesh.blendShapeCount];
            if (_bristlePositions.Capacity < _bristleVertexCount) _bristlePositions.Capacity = _bristleVertexCount;
            _bakePoseValid = false; _radiiValid = false;
        }
        private bool SourcePoseMatches()
        {
            if (!_bakePoseValid || !_brush.transform.lossyScale.Equals(_brushWorldScale) || _bristles.quality != _bakedQuality || QualitySettings.skinWeights != _bakedGlobalSkinWeights) return false;
            for (int i = 0; i < _sourcePose.Length; i++)
            {
                var pose = _sourcePose[i];
                Require(pose.Node != null && pose.Node.parent == pose.Parent, "Brush-internal hierarchy changed; explicitly rebuild the geometry cache.");
                if (!pose.Node.localPosition.Equals(pose.Position) || !pose.Node.localRotation.Equals(pose.Rotation) || !pose.Node.localScale.Equals(pose.Scale)) return false;
            }
            for (int i = 0; i < _shapeWeights.Length; i++) if (_bristles.GetBlendShapeWeight(i) != _shapeWeights[i]) return false;
            return true;
        }
        private void CaptureSourcePose()
        {
            for (int i = 0; i < _sourcePose.Length; i++)
            {
                var pose = _sourcePose[i];
                Require(pose.Node != null && pose.Node.parent == pose.Parent, "Brush-internal hierarchy changed; explicitly rebuild the geometry cache.");
                pose.Position = pose.Node.localPosition; pose.Rotation = pose.Node.localRotation; pose.Scale = pose.Node.localScale; _sourcePose[i] = pose;
            }
            for (int i = 0; i < _shapeWeights.Length; i++)
            { _shapeWeights[i] = _bristles.GetBlendShapeWeight(i); Require(Finite(_shapeWeights[i]), "Brush blendshape weight is nonfinite."); }
            _brushWorldScale = _brush.transform.lossyScale; _bakedQuality = _bristles.quality; _bakedGlobalSkinWeights = QualitySettings.skinWeights; _bakePoseValid = true;
        }

        /// <summary>Exact deformation-equivalence only: rest position, ordered full weights and every shape-frame
        /// position delta must agree exactly. Normals/UV seams never move BakeMesh positions.</summary>
        public static int[] BuildEquivalentVertexRepresentatives(Vector3[] positions, byte[] counts, BoneWeight1[] weights, Vector3[][] shapeFrames)
        {
            if (positions == null || positions.Length == 0 || positions.Any(p => !Finite(p))) throw new ArgumentException("Complete finite source positions are required.");
            bool skinned = counts != null || weights != null;
            if (skinned && (counts == null || counts.Length != positions.Length || weights == null || counts.Sum(c => (int)c) != weights.Length))
                throw new ArgumentException("The complete skin-weight stream must match all source vertices.");
            if (shapeFrames != null && shapeFrames.Any(f => f == null || f.Length != positions.Length || f.Any(p => !Finite(p))))
                throw new ArgumentException("Every shape frame must preserve complete finite positional deltas.");
            int[] offsets = new int[positions.Length]; int offset = 0;
            if (skinned)
            {
                for (int i = 0; i < counts.Length; i++) { if (counts[i] == 0) throw new ArgumentException("Unweighted skinned vertex."); offsets[i] = offset; offset += counts[i]; }
                if (weights.Any(w => w.boneIndex < 0 || !Finite(w.weight) || w.weight < 0f)) throw new ArgumentException("Invalid skin influence.");
            }
            bool Equal(int a, int b)
            {
                if (skinned)
                {
                    if (counts[a] != counts[b]) return false;
                    for (int i = 0; i < counts[a]; i++)
                    { var x = weights[offsets[a] + i]; var y = weights[offsets[b] + i]; if (x.boneIndex != y.boneIndex || x.weight != y.weight) return false; }
                }
                if (shapeFrames != null) foreach (var frame in shapeFrames) if (!frame[a].Equals(frame[b])) return false;
                return true;
            }
            var byPosition = new Dictionary<Vector3, List<int>>(); var result = new int[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                if (!byPosition.TryGetValue(positions[i], out var candidates)) { candidates = new List<int>(); byPosition.Add(positions[i], candidates); }
                int representative = -1;
                foreach (int candidate in candidates) if (Equal(i, candidate)) { representative = candidate; break; }
                if (representative < 0) { representative = i; candidates.Add(i); }
                result[i] = representative;
            }
            return result;
        }

        [Serializable] public sealed class BrushProxyAudit
        {
            public string scope = "Actual raw-vertex TransformPoint/Distance reference versus optimized capsules. Geometry equivalence within 10um is separate from native Cloth collision/stability approval. Allocation timings separate exact-unchanged-pose cache hits from forced actual native bakes.";
            public bool geometryEquivalent;
            public string error;
            public int rawShaftVertices, rawBristleVertices, uniqueShaftVertices, uniqueBristleVertices, checkedRawPoints, cachedRefreshCalls, forcedBakeCalls;
            public float maximumRadiusDifferenceMeters, maximumRawPointOutsideMeters, maximumEndpointDifferenceMeters;
            public float cacheVsForcedMaximumBakedDeltaMeters;
            public long cachedAllocatedBytes, forcedBakeAllocatedBytes;
            public double cachedMeanMilliseconds, forcedBakeMeanMilliseconds;
        }
        public BrushProxyAudit AuditAgainstFullSource(int repeatRefreshCalls = 64)
        {
            var result = new BrushProxyAudit();
            try
            {
                Require(RefreshNow(), LastError);
                // Compare the normal path with a fresh independent native bake before warming timings.
                Vector3[] cachedPositions = _bristlePositions.ToArray();
                Require(RefreshWithForcedBake(), LastError);
                for (int i = 0; i < cachedPositions.Length; i++)
                    result.cacheVsForcedMaximumBakedDeltaMeters = Mathf.Max(result.cacheVsForcedMaximumBakedDeltaMeters, Vector3.Distance(cachedPositions[i], _bristlePositions[i]));
                // Warm the exact cache-hit path before measuring its managed allocations.
                Require(RefreshNow(), LastError);
                int cachedCalls = Mathf.Clamp(repeatRefreshCalls, 1, 1024), forcedCalls = Mathf.Clamp(cachedCalls / 8, 1, 16);
                long allocated = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
                for (int i = 0; i < cachedCalls; i++) Require(RefreshNow(), LastError);
                result.cachedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                result.cachedMeanMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency / cachedCalls; result.cachedRefreshCalls = cachedCalls;
                allocated = GC.GetAllocatedBytesForCurrentThread(); started = Stopwatch.GetTimestamp();
                for (int i = 0; i < forcedCalls; i++) Require(RefreshWithForcedBake(), LastError);
                result.forcedBakeAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                result.forcedBakeMeanMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency / forcedCalls; result.forcedBakeCalls = forcedCalls;
                result.rawShaftVertices = _shaftVertexCount; result.rawBristleVertices = _bristleVertexCount;
                result.uniqueShaftVertices = UniqueShaftVertices; result.uniqueBristleVertices = UniqueBristleVertices;
                foreach (var binding in _bindings)
                {
                    Vector3 a = binding.Start.TransformPoint(binding.StartLocal), b = binding.End.TransformPoint(binding.EndLocal);
                    float rawRadius = 0f;
                    foreach (int vertex in binding.Vertices)
                    {
                        Vector3 point = binding.Bristle ? _bristles.transform.TransformPoint(_bristlePositions[vertex]) : _shaft.transform.TransformPoint(_shaftPositions[vertex]);
                        Require(Finite(point), "Full original brush reference contains a nonfinite vertex.");
                        rawRadius = Mathf.Max(rawRadius, Distance(point, a, b)); result.checkedRawPoints++;
                    }
                    var capsule = binding.Capsule; float scale = capsule.transform.lossyScale.x;
                    float actualRadius = capsule.radius * scale, half = Mathf.Max(0f, capsule.height * .5f - capsule.radius) * scale;
                    Vector3 center = capsule.transform.TransformPoint(capsule.center), direction = capsule.transform.up;
                    Vector3 ca = center - direction * half, cb = center + direction * half;
                    float endpoints = Mathf.Min(Mathf.Max(Vector3.Distance(a, ca), Vector3.Distance(b, cb)), Mathf.Max(Vector3.Distance(a, cb), Vector3.Distance(b, ca)));
                    result.maximumEndpointDifferenceMeters = Mathf.Max(result.maximumEndpointDifferenceMeters, endpoints);
                    result.maximumRadiusDifferenceMeters = Mathf.Max(result.maximumRadiusDifferenceMeters, Mathf.Abs(rawRadius - actualRadius));
                    // Test all original points against the actual serialized capsule transform, not merely its target radius.
                    foreach (int vertex in binding.Vertices)
                    {
                        Vector3 point = binding.Bristle ? _bristles.transform.TransformPoint(_bristlePositions[vertex]) : _shaft.transform.TransformPoint(_shaftPositions[vertex]);
                        result.maximumRawPointOutsideMeters = Mathf.Max(result.maximumRawPointOutsideMeters, Distance(point, ca, cb) - actualRadius);
                    }
                }
                result.geometryEquivalent = result.checkedRawPoints == _shaftVertexCount + _bristleVertexCount
                    && result.maximumRadiusDifferenceMeters <= .00001f && result.maximumEndpointDifferenceMeters <= .00001f && result.maximumRawPointOutsideMeters <= .00001f
                    && result.cacheVsForcedMaximumBakedDeltaMeters <= .00001f;
            }
            catch (Exception e) { result.error = e.GetBaseException().Message; }
            return result;
        }
        private static float Distance(Vector3 point, Vector3 a, Vector3 b)
        { Vector3 d = b - a; float t = d.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(point - a, d) / d.sqrMagnitude) : 0f; return Vector3.Distance(point, a + d * t); }
        private static bool Uniform(Vector3 s) => Finite(s) && s.x > 0f && Mathf.Abs(s.x - s.y) < .0001f && Mathf.Abs(s.x - s.z) < .0001f;
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static void Require(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
        private static void DestroyOwned(UnityEngine.Object obj) { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    }
}
