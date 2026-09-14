using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Geometry = Oheangbu.Presentation.PlayerSecondaryCollisionGeometry;

namespace Oheangbu.Presentation
{
    /// <summary>Optional expression-only spring collision. Called by the secondary rig;
    /// never moves a pivot, changes its scale, deforms a rigid mesh or invokes game Physics.
    /// Positive remaining overlap is an authoring/constraint failure, never hidden as a pass.</summary>
    [DisallowMultipleComponent]
    public sealed partial class PlayerSecondaryCollisionRig : MonoBehaviour
    {
        [Serializable] public sealed class PartMeasurement
        {
            public string name;
            public bool rigid, unresolved, atSwingLimit;
            public int sourceVertices, sourceTriangles, queries, triangleTests, projections;
            public int uniqueSkinnedVertices, queryCacheHits, skinEvaluations, skinCacheHits, bvhRefits;
            public double sourceHashInitializationMilliseconds, bvhInitializationMilliseconds;
            public float initialPenetrationMeters, remainingPenetrationMeters, remainingSupportMarginMeters;
            public int worstCapsule = -1, worstSurface = -1, worstTriangle = -1;
        }
        [SerializeField] private Transform _representation;
        [SerializeField] private PlayerClothBodyProxyRig _body;
        [SerializeField] private CapsuleCollider[] _bodyCapsules = Array.Empty<CapsuleCollider>();
        [SerializeField] private SecondaryCollisionPart[] _parts = Array.Empty<SecondaryCollisionPart>();
        // Solver clearance, not an allowed penetration or reduced source geometry.
        private const float ContactMargin = .0001f;
        private const int ProjectionIterations = 12;
        private const float Improvement = .0000001f;
        private sealed class SurfaceState
        {
            public SecondaryCollisionSurface Binding;
            public Vector3[] Positions;
            public Matrix4x4[] Skin;
            public int[] UsedBones;
            public Matrix4x4[] BoneWorld;
            public Geometry.ExactSkinCache SkinCache;
            public Geometry.TriangleTree Tree;
        }
        private sealed class PartState
        {
            public SecondaryCollisionPart Binding;
            public SurfaceState[] Surfaces;
            public PartMeasurement Measurement;
            public bool HasQuery;
            public int LastCapsuleRevision;
            public Matrix4x4 LastForward, LastInverse;
            public float LastScale;
            public Geometry.Contact LastResult;
        }
        private sealed class NodeState
        {
            public PartState Part;
            public Transform Bone;
            public bool[][] AffectedBones;
            public Vector3 LastSafeAngle;
            public bool HasSafeAngle;
        }
        private PartState[] _states = Array.Empty<PartState>();
        private readonly Dictionary<Transform, NodeState> _nodes = new Dictionary<Transform, NodeState>();
        private Geometry.Capsule[] _capsules = Array.Empty<Geometry.Capsule>();
        private Geometry.Capsule[] _localCapsules = Array.Empty<Geometry.Capsule>();
        private int _capsuleRevision;
        private bool _ready, _frameReady;
        private long _frameStarted, _allocatedStart;
        public string LastError { get; private set; }
        public bool IsConfigured => _ready;
        public bool FrameReady => _frameReady;
        public int Frames { get; private set; }
        public int Resets { get; private set; }
        public int UnresolvedParts { get; private set; }
        public float MaximumRemainingPenetrationMeters { get; private set; }
        public double LastFrameMainThreadMilliseconds { get; private set; }
        public long LastFrameAllocatedBytes { get; private set; }
        [Serializable] public sealed class InitializationPhases
        {
            public bool completed;
            public int sourceHashCalls, sourceMeshes, sourceVertices, sourceTriangles, bvhBuilds;
            public double elapsedMilliseconds, sourceHashMilliseconds, bvhBuildMilliseconds;
            public string scope = "Nested initialization phase timers only. Original BeginFrame-through-EndFrame CPU and allocation accounting are unchanged and include all timer overhead. Failed initialization yields completed=false; zero/unavailable partial timings are not success. No separate warm-query CPU attribution is inferred from counters.";
        }
        public InitializationPhases LastInitialization { get; private set; }
        public PartMeasurement[] Measurements { get; private set; } = Array.Empty<PartMeasurement>();
        public SecondaryCollisionPart[] AuthoredParts => _parts;
        public CapsuleCollider[] BodyCapsules => _bodyCapsules;
        public string CoverageScope => _runtimeCapsules ? "Runtime bone-following capsule approximation. Does not certify original triangle surface contact." : "Every retained original triangle, including mixed-weight boundary corners, against the complete body capsule array. No runtime BakeMesh or native/game Physics. Supports are not shrunk. Body proxy overhang can cause conservative false collisions; head/neck, enclosed solid-volume containment, ornament-to-ornament and continuous-time sweeps are not certified.";

        public bool Configure(Transform representation, PlayerClothBodyProxyRig body,
            CapsuleCollider[] bodyCapsules, SecondaryCollisionPart[] parts)
        {
            _representation = representation; _body = body; _bodyCapsules = bodyCapsules; _parts = parts;
            _ready = _frameReady = false;
            try { Initialize(); return true; } catch (Exception e) { LastError = e.GetBaseException().Message; return false; }
        }
        private void OnEnable() { _ready = false; _frameReady = false; }
        private void OnDisable() { ResetHistory(); }
        public bool Owns(Transform representation) => _representation == representation;
        public bool HasNode(Transform node) => _ready && node != null && _nodes.ContainsKey(node);
        public void ResetHistory()
        {
            _frameReady = false; Resets++;
            foreach (var node in _nodes.Values) { node.HasSafeAngle = false; node.LastSafeAngle = Vector3.zero; }
            foreach (var state in _states) if (state != null) state.HasQuery = false;
        }
        private void Initialize()
        {
            _partCapsules.Clear();
            long initializationStarted = Stopwatch.GetTimestamp(); LastInitialization = new InitializationPhases();
            var initializedMeshes = new HashSet<Mesh>();
            _ready = false; _frameReady = false; LastError = null; _nodes.Clear();
            Need(_representation != null && _body != null && (_body.transform == _representation || _body.transform.IsChildOf(_representation)), "Collision requires its owned representation and actual body fitter.");
            Need(_parts != null && _parts.Length > 0 && _bodyCapsules != null && _bodyCapsules.Length > 0, "Authored part geometry and explicit complete body capsules required.");
            var proxies = new HashSet<CapsuleCollider>();
            foreach (var proxy in _bodyCapsules)
                Need(proxy != null && proxies.Add(proxy) && PlayerSecondaryMotionRig.ValidateProxy(proxy, out _) && proxy.transform.IsChildOf(_representation)
                    && !proxy.name.Contains("_Brush_"), "Body collision proxies must be unique owned representation-only capsules; brush candidates are excluded.");
            _capsules = new Geometry.Capsule[_bodyCapsules.Length]; _states = new PartState[_parts.Length]; Measurements = new PartMeasurement[_parts.Length];
            _localCapsules = new Geometry.Capsule[_bodyCapsules.Length]; _capsuleRevision++;
            for (int p = 0; p < _parts.Length; p++)
            {
                var binding = _parts[p];
                Need(binding != null && binding.DrivenBones != null && binding.DrivenBones.Length > 0 && binding.Surfaces != null && binding.Surfaces.Length > 0, "Every part needs explicit driven bones and source geometry.");
                Need(!binding.Rigid || binding.DrivenBones.Length == 1, "Rigid part requires one exact pivot.");
                var state = new PartState { Binding = binding, Surfaces = new SurfaceState[binding.Surfaces.Length],
                    Measurement = new PartMeasurement { name = binding.Name, rigid = binding.Rigid } };
                _states[p] = state; Measurements[p] = state.Measurement;
                for (int s = 0; s < binding.Surfaces.Length; s++)
                {
                    var surface = binding.Surfaces[s];
                    Need(surface != null && surface.SourceRenderer != null && surface.SourceMesh != null && surface.Rigid == binding.Rigid,
                        binding.Name + ": source renderer/mesh or geometry kind is missing.");
                    Need(surface.Positions != null && surface.Positions.Length > 0 && surface.OriginalVertexIndices.Length == surface.Positions.Length
                        && surface.Triangles.Length > 0 && surface.OriginalTriangleIndices.Length * 3 == surface.Triangles.Length,
                        binding.Name + ": complete original vertex/triangle provenance required.");
                    Mesh actual = surface.SourceRenderer is SkinnedMeshRenderer smr ? smr.sharedMesh : surface.SourceRenderer.GetComponent<MeshFilter>()?.sharedMesh;
                    Need(actual == surface.SourceMesh, binding.Name + ": source mesh changed after collision authoring; rebuild binding.");
                    long hashStarted = Stopwatch.GetTimestamp();
                    string actualHash = Geometry.SourceMeshHash(actual);
                    double hashMilliseconds = (Stopwatch.GetTimestamp() - hashStarted) * 1000d / Stopwatch.Frequency;
                    LastInitialization.sourceHashMilliseconds += hashMilliseconds; LastInitialization.sourceHashCalls++;
                    state.Measurement.sourceHashInitializationMilliseconds += hashMilliseconds;
                    initializedMeshes.Add(actual);
                    Need(!string.IsNullOrEmpty(surface.SourceGeometrySha256) && actualHash == surface.SourceGeometrySha256,
                        binding.Name + ": exact source vertex/triangle/weight identity changed; rebuild collision authoring.");
                    foreach (var v in surface.Positions) Need(Geometry.Finite(v), binding.Name + ": nonfinite authored vertex.");
                    var item = new SurfaceState { Binding = surface, Positions = (Vector3[])surface.Positions.Clone() };
                    if (!binding.Rigid)
                    {
                        Need(surface.Bones.Length > 0 && surface.Bones.Length == surface.Bindposes.Length && surface.WeightStarts.Length == surface.Positions.Length
                            && surface.WeightCounts.Length == surface.Positions.Length, binding.Name + ": complete skinned chain binding required.");
                        var used = new HashSet<int>();
                        for (int v = 0; v < surface.Positions.Length; v++)
                        {
                            Need(surface.WeightCounts[v] > 0 && surface.WeightCounts[v] <= 4 && surface.WeightStarts[v] >= 0
                                && surface.WeightStarts[v] + surface.WeightCounts[v] <= surface.Weights.Length, "Invalid exact chain weight ranges.");
                            float sum = 0f;
                            for (int i = surface.WeightStarts[v]; i < surface.WeightStarts[v] + surface.WeightCounts[v]; i++)
                            {
                                var w = surface.Weights[i]; Need(float.IsFinite(w.Weight) && w.Weight >= 0 && w.Bone >= 0 && w.Bone < surface.Bones.Length && surface.Bones[w.Bone] != null, "Invalid exact chain influence.");
                                sum += w.Weight; if (w.Weight > 0) used.Add(w.Bone);
                            }
                            Need(Mathf.Abs(sum - 1f) <= .0001f, "Chain weights must sum to one; runtime does not normalize source data.");
                        }
                        item.UsedBones = new int[used.Count]; used.CopyTo(item.UsedBones); Array.Sort(item.UsedBones);
                        item.BoneWorld = new Matrix4x4[surface.Bones.Length];
                        item.SkinCache = new Geometry.ExactSkinCache(surface, item.Positions);
                        item.Skin = item.SkinCache.SkinMatrices;
                        state.Measurement.uniqueSkinnedVertices += item.SkinCache.UniqueVertexCount;
                        Skin(item, _representation.worldToLocalMatrix);
                    }
                    long treeStarted = Stopwatch.GetTimestamp();
                    item.Tree = new Geometry.TriangleTree(item.Positions, surface.Triangles); state.Surfaces[s] = item;
                    double treeMilliseconds = (Stopwatch.GetTimestamp() - treeStarted) * 1000d / Stopwatch.Frequency;
                    LastInitialization.bvhBuildMilliseconds += treeMilliseconds; LastInitialization.bvhBuilds++;
                    state.Measurement.bvhInitializationMilliseconds += treeMilliseconds;
                    LastInitialization.sourceVertices += surface.Positions.Length; LastInitialization.sourceTriangles += surface.Triangles.Length / 3;
                    state.Measurement.sourceVertices += surface.Positions.Length; state.Measurement.sourceTriangles += surface.Triangles.Length / 3;
                }
                foreach (var bone in binding.DrivenBones)
                {
                    Need(bone != null && bone.IsChildOf(_representation) && !_nodes.ContainsKey(bone), binding.Name + ": duplicated or missing owned driven bone.");
                    Need(UniformScale(bone.lossyScale), binding.Name + ": collision preserves only positive uniform scales.");
                    var node = new NodeState { Part = state, Bone = bone, AffectedBones = new bool[state.Surfaces.Length][] };
                    for (int s = 0; s < state.Surfaces.Length; s++)
                    {
                        var source = state.Surfaces[s].Binding; node.AffectedBones[s] = new bool[source.Bones.Length];
                        for (int i = 0; i < source.Bones.Length; i++) if (source.Bones[i] != null) node.AffectedBones[s][i] = source.Bones[i] == bone || source.Bones[i].IsChildOf(bone);
                    }
                    _nodes.Add(bone, node);
                }
            }
            _ready = true; LastInitialization.completed = true; LastInitialization.sourceMeshes = initializedMeshes.Count;
            LastInitialization.elapsedMilliseconds = (Stopwatch.GetTimestamp() - initializationStarted) * 1000d / Stopwatch.Frequency;
        }

        public bool BeginFrame()
        {
            _frameReady = false; _frameStarted = Stopwatch.GetTimestamp(); _allocatedStart = GC.GetAllocatedBytesForCurrentThread();
            LastRuntimeBroadphaseSkips = LastRuntimeNarrowphaseTests = LastRuntimeQueryCacheHits = 0;
            try
            {
                if (!_ready) Initialize();
                if (_runtimeCapsules && _partCapsules.Count == 0) BuildRuntimeCapsules();
                Need(UniformScale(_representation.lossyScale), "Representation collision frame requires positive uniform scale.");
                // Explicit refresh remains active when ALL native Cloth is disabled,
                // including LOD1/2. Cloth's own LateUpdate optimization is unrelated.
                Need(_body.RefreshNow(), "Current posed body fit unavailable: " + _body.LastError);
                bool capsulesChanged = false;
                for (int i = 0; i < _capsules.Length; i++)
                {
                    var current = ReadCapsule(_bodyCapsules[i]);
                    if (!Geometry.Exact(_capsules[i], current)) capsulesChanged = true;
                    _capsules[i] = current;
                }
                if (capsulesChanged) _capsuleRevision++;
                foreach (var state in _states)
                {
                    foreach (var surface in state.Surfaces)
                    {
                        var source = surface.Binding;
                        Mesh actual = source.SourceRenderer is SkinnedMeshRenderer smr ? smr.sharedMesh : source.SourceRenderer.GetComponent<MeshFilter>()?.sharedMesh;
                        Need(actual == source.SourceMesh, "Collision source mesh was replaced at runtime; serialized support must be rebuilt.");
                    }
                    var m = state.Measurement; m.queries = m.triangleTests = m.projections = 0;
                    m.queryCacheHits = m.skinEvaluations = m.skinCacheHits = m.bvhRefits = 0;
                    m.initialPenetrationMeters = m.remainingPenetrationMeters = m.remainingSupportMarginMeters = 0f;
                    m.unresolved = m.atSwingLimit = false; m.worstCapsule = m.worstSurface = m.worstTriangle = -1;
                }
                LastError = null; _frameReady = true; Frames++; return true;
            }
            catch (Exception e) { LastError = e.GetBaseException().Message; return false; }
        }

        public void Constrain(Transform bone, Quaternion restRotation, float maximumRadians, ref Vector3 angle, ref Vector3 velocity)
        {
            if (!_frameReady || !_nodes.TryGetValue(bone, out var node)) return;
            try
            {
                Need(Geometry.Finite(angle) && Geometry.Finite(velocity), "Nonfinite spring state.");
                var part = node.Part; Apply(bone, restRotation, angle);
                var contact = Query(part); part.Measurement.initialPenetrationMeters = Mathf.Max(part.Measurement.initialPenetrationMeters, Mathf.Max(0f, contact.Depth - ContactMargin));
                for (int iteration = 0; contact.Hit && iteration < (_runtimeCapsules ? _capsuleIterations : ProjectionIterations); iteration++)
                {
                    Vector3 before = angle, bestAngle = angle;
                    var best = contact;
                    Vector3 lever = RotationLever(node, contact), gradient = Vector3.Cross(lever, contact.Normal);
                    if (gradient.sqrMagnitude > 1e-14f)
                    {
                        float radians = Mathf.Min(8f * Mathf.Deg2Rad, contact.Depth / gradient.magnitude + .00001f);
                        Vector3 worldAxis = gradient.normalized;
                        for (int search = 0; search < 6; search++)
                        {
                            Vector3 trial = Geometry.CorrectWorldRotation(before, restRotation,
                                bone.parent != null ? bone.parent.rotation : Quaternion.identity, worldAxis * radians, maximumRadians);
                            TryTrial(node, restRotation, trial, ref bestAngle, ref best);
                            Apply(bone, restRotation, before); radians *= .5f;
                            if (!best.Hit || best.Depth < contact.Depth - Improvement) break;
                        }
                    }
                    if (best.Hit && best.Depth >= contact.Depth - Improvement)
                    {
                        // Axis/interior contacts or opposing capsules can make the
                        // closest-witness gradient degenerate. Bounded alternative
                        // rotations retain every original triangle and angle limit.
                        float step = Mathf.Min(8f * Mathf.Deg2Rad, Mathf.Max(.01f, contact.Depth / Mathf.Max(.005f, lever.magnitude)));
                        for (int axis = 0; axis < 3; axis++) for (int sign = -1; sign <= 1; sign += 2)
                        {
                            Vector3 delta = Vector3.zero; delta[axis] = step * sign;
                            TryTrial(node, restRotation, Vector3.ClampMagnitude(before + delta, maximumRadians), ref bestAngle, ref best);
                        }
                    }
                    if (best.Hit && best.Depth >= contact.Depth - Improvement && node.HasSafeAngle)
                        TryTrial(node, restRotation, Vector3.ClampMagnitude(node.LastSafeAngle, maximumRadians), ref bestAngle, ref best);
                    angle = bestAngle; Apply(bone, restRotation, angle);
                    if (best.Hit && best.Depth >= contact.Depth - Improvement) break;
                    Geometry.RemoveInwardVelocity(ref velocity, angle - before);
                    part.Measurement.projections++; contact = Query(part);
                }
                Apply(bone, restRotation, angle); contact = Query(part);
                if (!contact.Hit) { node.LastSafeAngle = angle; node.HasSafeAngle = true; }
                else
                {
                    // The spring must not keep injecting inward momentum while the
                    // fixed anchor/angle limit or a local minimum prevents resolution.
                    // This is explicitly unresolved, never a collision pass.
                    velocity = Vector3.zero;
                    part.Measurement.atSwingLimit |= angle.magnitude >= Mathf.Max(0f, maximumRadians - .0001f);
                }
            }
            catch (Exception e) { LastError = e.GetBaseException().Message; _frameReady = false; velocity = Vector3.zero; }
        }
        private void TryTrial(NodeState node, Quaternion rest, Vector3 trial, ref Vector3 bestAngle, ref Geometry.Contact best)
        {
            if (!Geometry.Finite(trial)) return;
            Apply(node.Bone, rest, trial); var candidate = Query(node.Part);
            if (!candidate.Hit || candidate.Depth < best.Depth - Improvement) { best = candidate; bestAngle = trial; }
        }
        public void EndFrame()
        {
            UnresolvedParts = 0; MaximumRemainingPenetrationMeters = 0f;
            if (_frameReady)
                try
                {
                    foreach (var part in _states)
                    {
                        var result = Query(part); var m = part.Measurement;
                        m.remainingSupportMarginMeters = result.Depth;
                        m.remainingPenetrationMeters = Mathf.Max(0f, result.Depth - ContactMargin);
                        m.unresolved = result.Hit;
                        if (m.unresolved) UnresolvedParts++;
                        m.worstCapsule = result.Capsule; m.worstSurface = result.Surface;
                        m.worstTriangle = result.Hit && !_runtimeCapsules ? part.Surfaces[result.Surface].Binding.OriginalTriangleIndices[result.Triangle] : -1;
                        MaximumRemainingPenetrationMeters = Mathf.Max(MaximumRemainingPenetrationMeters, m.remainingPenetrationMeters);
                    }
                }
                catch (Exception e) { LastError = e.GetBaseException().Message; _frameReady = false; }
            LastFrameMainThreadMilliseconds = (Stopwatch.GetTimestamp() - _frameStarted) * 1000d / Stopwatch.Frequency;
            LastFrameAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - _allocatedStart;
        }
        private Geometry.Contact Query(PartState part)
        {
            if (_runtimeCapsules) return QueryRuntimeCapsules(part);
            var best = new Geometry.Contact { Triangle = -1, Surface = -1, Capsule = -1 };
            Transform frame = part.Binding.Rigid ? part.Binding.DrivenBones[0] : _representation;
            float scale = frame.lossyScale.x; Matrix4x4 inverse = frame.worldToLocalMatrix, forward = frame.localToWorldMatrix;
            part.Measurement.queries++;
            bool poseChanged = false;
            if (!part.Binding.Rigid)
                foreach (var surface in part.Surfaces)
                {
                    if (Skin(surface, inverse))
                    { surface.Tree.Refit(); part.Measurement.skinEvaluations++; part.Measurement.bvhRefits++; poseChanged = true; }
                    else part.Measurement.skinCacheHits++;
                }
            // Exact current source matrices plus the complete body snapshot are the
            // cache key. Time/frame number, approximate quaternion equality and an
            // unchanged driven bone alone cannot establish an unchanged surface.
            if (!poseChanged && part.HasQuery && part.LastCapsuleRevision == _capsuleRevision &&
                part.LastScale == scale && Geometry.Exact(part.LastForward, forward) && Geometry.Exact(part.LastInverse, inverse))
            { part.Measurement.queryCacheHits++; return part.LastResult; }
            for (int c = 0; c < _capsules.Length; c++)
            {
                var capsule = _capsules[c];
                _localCapsules[c] = new Geometry.Capsule { A = inverse.MultiplyPoint3x4(capsule.A),
                    B = inverse.MultiplyPoint3x4(capsule.B), Radius = (capsule.Radius + ContactMargin) / scale };
            }
            for (int s = 0; s < part.Surfaces.Length; s++)
            {
                var surface = part.Surfaces[s];
                for (int c = 0; c < _capsules.Length; c++)
                {
                    var result = surface.Tree.Query(_localCapsules[c]);
                    part.Measurement.triangleTests += surface.Tree.LastTriangleTests;
                    if (!result.Hit || result.Depth * scale <= best.Depth) continue;
                    result.Depth *= scale; result.Surface = s; result.Capsule = c;
                    result.SurfacePoint = forward.MultiplyPoint3x4(result.SurfacePoint); result.AxisPoint = forward.MultiplyPoint3x4(result.AxisPoint);
                    result.Normal = frame.TransformDirection(result.Normal).normalized; best = result;
                }
            }
            part.HasQuery = true; part.LastCapsuleRevision = _capsuleRevision; part.LastScale = scale;
            part.LastForward = forward; part.LastInverse = inverse; part.LastResult = best;
            return best;
        }
        private static bool Skin(SurfaceState surface, Matrix4x4 inverse)
        {
            foreach (int i in surface.UsedBones) surface.BoneWorld[i] = surface.Binding.Bones[i].localToWorldMatrix;
            return surface.SkinCache.Refresh(inverse, surface.BoneWorld);
        }
        private Vector3 RotationLever(NodeState node, Geometry.Contact contact)
        {
            if (_runtimeCapsules) return contact.SurfacePoint - node.Bone.position;
            if (node.Part.Binding.Rigid) return contact.SurfacePoint - node.Bone.position;
            var state = node.Part.Surfaces[contact.Surface]; var source = state.Binding; int t = contact.Triangle * 3;
            int a = source.Triangles[t], b = source.Triangles[t + 1], c = source.Triangles[t + 2];
            Vector3 point = _representation.InverseTransformPoint(contact.SurfacePoint);
            Vector3 bary = Geometry.Barycentric(point, state.Positions[a], state.Positions[b], state.Positions[c]);
            return VertexLever(node, state, contact.Surface, a) * bary.x + VertexLever(node, state, contact.Surface, b) * bary.y + VertexLever(node, state, contact.Surface, c) * bary.z;
        }
        private Vector3 VertexLever(NodeState node, SurfaceState state, int surfaceIndex, int vertex)
        {
            var source = state.Binding; Vector3 result = Vector3.zero;
            for (int i = source.WeightStarts[vertex]; i < source.WeightStarts[vertex] + source.WeightCounts[vertex]; i++)
            {
                var w = source.Weights[i]; if (!node.AffectedBones[surfaceIndex][w.Bone]) continue;
                Vector3 world = _representation.TransformPoint(state.Skin[w.Bone].MultiplyPoint3x4(source.Positions[vertex]));
                result += (world - node.Bone.position) * w.Weight;
            }
            return result;
        }

        [Serializable] public sealed class SourceAudit
        {
            public bool completed, exactSourceGeometryEquivalent, exactMaximumDepthEquivalent;
            public string error;
            public int parts, surfaces, sourceVertices, sourceTriangles, bodyCapsules;
            public int sourcePositionMismatches, partDepthMismatches;
            public float maximumSourcePositionErrorMeters, maximumPartDepthErrorMeters;
            public double elapsedMilliseconds;
            public string scope = "Explicit diagnostic only: unshared full-weight source skin and exhaustive original filled triangles against the same complete current body snapshot. No BVH or cached witness supplies the reference; no spring/transform/source mutation. Allocates and is excluded from runtime profiling. This does not establish body anatomy or collision-free quality.";
        }
        public SourceAudit AuditAgainstFullSource()
        {
            var audit = new SourceAudit(); long started = Stopwatch.GetTimestamp();
            try
            {
                Need(_ready && _frameReady, "A valid BeginFrame/body snapshot is required before auditing collision geometry.");
                audit.bodyCapsules = _capsules.Length;
                foreach (var part in _states)
                {
                    var cached = Query(part); float referenceDepth = 0f;
                    Transform frame = part.Binding.Rigid ? part.Binding.DrivenBones[0] : _representation;
                    Matrix4x4 inverse = frame.worldToLocalMatrix; float scale = frame.lossyScale.x;
                    foreach (var surface in part.Surfaces)
                    {
                        var source = surface.Binding; var fullPositions = new Vector3[source.Positions.Length];
                        if (source.Rigid) Array.Copy(source.Positions, fullPositions, fullPositions.Length);
                        else
                        {
                            var fullMatrices = new Matrix4x4[source.Bones.Length];
                            // Read each original influence, including boundary influences,
                            // independently of the optimized palette/duplicate cache.
                            for (int v = 0; v < fullPositions.Length; v++)
                            {
                                Vector3 point = Vector3.zero;
                                for (int w = source.WeightStarts[v]; w < source.WeightStarts[v] + source.WeightCounts[v]; w++)
                                {
                                    var influence = source.Weights[w];
                                    fullMatrices[influence.Bone] = inverse * source.Bones[influence.Bone].localToWorldMatrix * source.Bindposes[influence.Bone];
                                    point += fullMatrices[influence.Bone].MultiplyPoint3x4(source.Positions[v]) * influence.Weight;
                                }
                                fullPositions[v] = point;
                            }
                        }
                        for (int v = 0; v < fullPositions.Length; v++)
                        {
                            Need(Geometry.Finite(fullPositions[v]), "Nonfinite independent source position.");
                            if (!fullPositions[v].Equals(surface.Positions[v])) audit.sourcePositionMismatches++;
                            audit.maximumSourcePositionErrorMeters = Mathf.Max(audit.maximumSourcePositionErrorMeters,
                                Vector3.Distance(fullPositions[v], surface.Positions[v]));
                        }
                        foreach (var worldCapsule in _capsules)
                        {
                            Vector3 a = inverse.MultiplyPoint3x4(worldCapsule.A), b = inverse.MultiplyPoint3x4(worldCapsule.B);
                            float radius = (worldCapsule.Radius + ContactMargin) / scale;
                            for (int t = 0; t < source.Triangles.Length; t += 3)
                            {
                                float distance = Geometry.SegmentTriangleDistance(a, b, fullPositions[source.Triangles[t]],
                                    fullPositions[source.Triangles[t + 1]], fullPositions[source.Triangles[t + 2]], out _, out _);
                                referenceDepth = Mathf.Max(referenceDepth, (radius - distance) * scale);
                            }
                        }
                        audit.sourceVertices += source.Positions.Length; audit.sourceTriangles += source.Triangles.Length / 3; audit.surfaces++;
                    }
                    if (cached.Depth != referenceDepth) audit.partDepthMismatches++;
                    audit.maximumPartDepthErrorMeters = Mathf.Max(audit.maximumPartDepthErrorMeters, Mathf.Abs(cached.Depth - referenceDepth));
                    audit.parts++;
                }
                audit.completed = true; audit.exactSourceGeometryEquivalent = audit.sourcePositionMismatches == 0;
                audit.exactMaximumDepthEquivalent = audit.partDepthMismatches == 0;
            }
            catch (Exception e) { audit.error = e.GetBaseException().Message; }
            audit.elapsedMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency; return audit;
        }
        private static Geometry.Capsule ReadCapsule(CapsuleCollider c)
        {
            Need(PlayerSecondaryMotionRig.ValidateProxy(c, out _) && UniformScale(c.transform.lossyScale), "Body proxy contract changed after binding.");
            float scale = c.transform.lossyScale.x;
            Vector3 axis = c.direction == 0 ? Vector3.right : c.direction == 1 ? Vector3.up : Vector3.forward;
            axis = c.transform.TransformDirection(axis).normalized; Vector3 center = c.transform.TransformPoint(c.center);
            float radius = c.radius * scale, half = Mathf.Max(0f, c.height * .5f - c.radius) * scale;
            return new Geometry.Capsule { A = center - axis * half, B = center + axis * half, Radius = radius };
        }
        private static void Apply(Transform bone, Quaternion rest, Vector3 angle) => bone.localRotation = rest * Geometry.Rotation(angle);
        private static bool UniformScale(Vector3 s) => Geometry.Finite(s) && s.x > 0 && Mathf.Abs(s.x - s.y) <= .0001f && Mathf.Abs(s.x - s.z) <= .0001f;
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
