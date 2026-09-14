using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.Presentation
{
    /// <summary>Per-surface collision selection for the measured Unity 6000.3.9f1 native limit.
    /// All candidate proxies remain intact. More than16 independently relevant capsules is WAIT_CAPACITY,
    /// even though a deterministic nearest16 subset permits an explicitly incomplete diagnostic solve.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(1700)]
    public sealed class PlayerClothCollisionBudgetRig : MonoBehaviour
    {
        public const int NativeCapsuleLimit = 16;
        [SerializeField] private Transform _worldRoot;
        [SerializeField] private Cloth[] _surfaces = Array.Empty<Cloth>();
        [SerializeField] private CapsuleCollider[] _candidates = Array.Empty<CapsuleCollider>();
        private Surface[] _states = Array.Empty<Surface>();
        private CapsuleGeometry[] _geometry = Array.Empty<CapsuleGeometry>();
        private bool _ready;
        public string LastError { get; private set; }
        public string Status { get; private set; } = "WAIT";
        public int RefreshCount { get; private set; }
        public double LastRefreshMilliseconds { get; private set; }
        public long LastRefreshAllocatedBytes { get; private set; }
        public int SkippedInactiveLateFrames { get; private set; }
        public int SkinPoseUpdates { get; private set; }
        public int ExactPoseCacheHits { get; private set; }
        public int LastPointDistanceTests { get; private set; }
        public int CandidateCount => _candidates.Length;
        public SurfaceSelection[] Selections { get; private set; } = Array.Empty<SurfaceSelection>();

        [Serializable] public sealed class SurfaceSelection
        {
            public string cloth;
            public string status = "WAIT";
            public int sourceVertices, uniqueSourceVertices, candidates, potentialCandidates, independentPotentialCandidates;
            public int containedCandidates, omittedPotentialCandidates, assignedCapsules, assignmentChanges;
            public int[] selectedCandidateIndices = Array.Empty<int>();
            public int[] omittedPotentialCandidateIndices = Array.Empty<int>();
            public string[] candidateNames = Array.Empty<string>();
            public float maximumOmittedSkinTargetPenetrationMeters;
            public string scope = "Full source skin points plus exact authored maxDistance envelopes; analytic whole-capsule containment may remove redundant candidates. Actual solved cloth penetration must be measured separately. Nearest16 is a capacity safety limit, not quality approval.";
        }
        [Serializable] public sealed class ActualOmissionAudit
        {
            public string cloth;
            public int particles, omittedCandidateCount, penetratingParticles, penetratingPointCandidatePairs;
            public float maximumOmittedPenetrationMeters;
            public string scope = "Caller-provided actual post-solve world particles against all currently unrepresented candidate proxies, including potential-envelope false negatives. No native particle coordinate assumption is made here.";
        }
        public struct CapsuleGeometry
        {
            public Vector3 Start, End;
            public float Radius;
            public bool Active;
        }
        private sealed class Surface
        {
            public Cloth Cloth;
            public SkinnedMeshRenderer Renderer;
            public Mesh Source;
            public Transform[] Bones;
            public Matrix4x4[] Bindposes, Skin;
            public Vector3[] Rest, Target;
            public int[] Indices, Offsets, UsedBones;
            public byte[] Counts;
            public BoneWeight1[] Weights;
            public float[] Mobility, AllMobility, Gap, SkinGap;
            public int[] Containers, Ranked;
            public bool[] Represented;
            public CapsuleCollider[] Assigned = Array.Empty<CapsuleCollider>();
            public CapsuleCollider[][] AssignmentBuffers;
            public PointTree Tree;
            public bool SkinReady, QueryReady;
            public float QueryScale;
            public SurfaceSelection Report;
        }

        public bool Configure(Transform worldRoot, Cloth[] surfaces, CapsuleCollider[] candidates, out string error)
        {
            error = null; _worldRoot = worldRoot; _surfaces = surfaces ?? Array.Empty<Cloth>(); _candidates = candidates ?? Array.Empty<CapsuleCollider>();
            _ready = false;
            if (!RefreshNow()) { error = LastError; return false; }
            return true; // WAIT_CAPACITY is exposed separately and never converted into quality approval.
        }
        [Min(0f)] public float RuntimeSelectionHz;
        private float _nextRuntimeSelection;
        private Vector3 _lastRuntimeRoot;
        private Quaternion _lastRuntimeRotation;
        private bool _selectionWasActive;
        private void OnEnable() { _ready = false; _selectionWasActive = false; }
        private void LateUpdate()
        {
            if (_worldRoot == null) return;
            bool active = false;
            foreach (var cloth in _surfaces) if (cloth != null && cloth.enabled && cloth.gameObject.activeInHierarchy) { active = true; break; }
            // LOD controller runs at1600. Its re-enabled Cloth is evaluated here at1700 in
            // the same frame before native simulation. Explicit fixture Refresh remains unrestricted.
            if (!active) { _selectionWasActive = false; SkippedInactiveLateFrames++; return; }
            if (RuntimeSelectionHz > 0 && _selectionWasActive && Time.time < _nextRuntimeSelection
                && Vector3.Distance(_worldRoot.position, _lastRuntimeRoot) < 1f
                && Quaternion.Angle(_worldRoot.rotation, _lastRuntimeRotation) < 45f) return;
            _selectionWasActive = true; _lastRuntimeRoot = _worldRoot.position; _lastRuntimeRotation = _worldRoot.rotation;
            _nextRuntimeSelection = Time.time + (RuntimeSelectionHz > 0 ? 1f / RuntimeSelectionHz : 0);
            RefreshNow();
        }
        private void OnDisable() { _ready = false; Status = "WAIT_DISABLED"; }

        private void Initialize()
        {
            Need(_worldRoot != null && transform == _worldRoot && Uniform(_worldRoot.lossyScale), "An explicit positive-uniform world representation is required.");
            Need(_surfaces.Length > 0 && _candidates.Length > 0 && _surfaces.Distinct().Count() == _surfaces.Length
                && _candidates.Distinct().Count() == _candidates.Length, "Unique authored Cloth surfaces and complete unique candidate proxies are required.");
            foreach (var c in _candidates)
                Need(c != null && c.transform.IsChildOf(_worldRoot) && PlayerSecondaryMotionRig.ValidateProxy(c, out _), "Every candidate must be an owned isolated visual proxy.");
            _geometry = new CapsuleGeometry[_candidates.Length]; var states = new List<Surface>();
            foreach (var cloth in _surfaces)
            {
                Need(cloth != null && cloth.transform.IsChildOf(_worldRoot), "Cloth must belong to the world representation.");
                Need(cloth.sphereColliders.Length == 0, "This selector reserves the measured32 sphere slots for <=16 capsules; separately authored sphere pairs need an explicit shared budget.");
                var renderer = cloth.GetComponent<SkinnedMeshRenderer>(); var mesh = renderer != null ? renderer.sharedMesh : null;
                Need(mesh != null && mesh.isReadable && mesh.blendShapeCount == 0, "Cloth requires a readable static source mesh with no blendshape deformation; extend target evaluation explicitly before adding cloth shapes.");
                Need(mesh.HasVertexAttribute(VertexAttribute.TexCoord3) && mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord3) == VertexAttributeFormat.Float32,
                    "Exact Float32 UV3.x cloth mobility is required.");
                var uv = new List<Vector2>(); mesh.GetUVs(3, uv);
                Need(uv.Count == mesh.vertexCount && uv.All(p => Finite(p.x) && p.x >= 0f && p.x <= 1f), "Every source vertex needs finite authored mobility.");
                var s = new Surface { Cloth = cloth, Renderer = renderer, Source = mesh, Rest = mesh.vertices,
                    Bones = renderer.bones, Bindposes = mesh.bindposes };
                using (var sourceCounts = mesh.GetBonesPerVertex()) s.Counts = sourceCounts.ToArray();
                using (var sourceWeights = mesh.GetAllBoneWeights()) s.Weights = sourceWeights.ToArray();
                Need(s.Bones.Length == s.Bindposes.Length && s.Bones.All(b => b != null && b.IsChildOf(_worldRoot))
                    && s.Weights.All(w => w.boneIndex >= 0 && w.boneIndex < s.Bones.Length), "Cloth skin source references are incomplete.");
                // The synthetic frame carries exact mobility for deduplication only; it is never applied to geometry.
                var mobilityKey = uv.Select(p => new Vector3(p.x, 0f, 0f)).ToArray();
                int[] map = PlayerClothBrushProxyRig.BuildEquivalentVertexRepresentatives(s.Rest, s.Counts, s.Weights, new[] { mobilityKey });
                s.Indices = map.Distinct().ToArray(); s.Offsets = new int[s.Rest.Length]; int offset = 0;
                for (int i = 0; i < s.Rest.Length; i++)
                {
                    s.Offsets[i] = offset; float sum = 0f;
                    Need(s.Counts[i] >= 1 && s.Counts[i] <= 4, "Cloth source must retain the approved one-to-four influence limit.");
                    for (int w = 0; w < s.Counts[i]; w++) sum += s.Weights[offset++].weight;
                    Need(Mathf.Abs(sum - 1f) <= .0001f, "Actual cloth skin weights must sum to one within0.0001.");
                }
                Need(offset == s.Weights.Length, "Complete cloth skin stream is required.");
                s.AllMobility = uv.Select(p => p.x).ToArray();
                s.Mobility = s.Indices.Select(i => uv[i].x).ToArray(); s.Target = new Vector3[s.Indices.Length];
                s.Skin = new Matrix4x4[s.Bones.Length]; s.Gap = new float[_candidates.Length]; s.SkinGap = new float[_candidates.Length];
                s.UsedBones = s.Weights.Select(w => w.boneIndex).Distinct().ToArray();
                s.Containers = new int[_candidates.Length]; s.Ranked = new int[_candidates.Length]; s.Represented = new bool[_candidates.Length];
                s.Tree = new PointTree(s.Indices.Select(i => s.Rest[i]).ToArray(), s.Mobility);
                s.AssignmentBuffers = Enumerable.Range(0, NativeCapsuleLimit + 1).Select(n => new CapsuleCollider[n]).ToArray();
                s.Report = new SurfaceSelection { cloth = cloth.name, sourceVertices = s.Rest.Length, uniqueSourceVertices = s.Indices.Length,
                    candidates = _candidates.Length, candidateNames = _candidates.Select(c => c.name).ToArray() };
                states.Add(s);
            }
            _states = states.ToArray(); Selections = _states.Select(s => s.Report).ToArray(); _ready = true;
        }

        public bool RefreshNow() => Refresh(false);
        public bool RefreshWithFullGeometry() => Refresh(true);
        private bool Refresh(bool forceGeometry)
        {
            long started = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                if (!_ready) Initialize();
                Need(Uniform(_worldRoot.lossyScale), "World representation scale must remain positive uniform.");
                bool candidatesChanged = false; LastPointDistanceTests = 0;
                for (int i = 0; i < _candidates.Length; i++)
                {
                    CapsuleGeometry current = ReadCapsule(_candidates[i]); var old = _geometry[i];
                    candidatesChanged |= !current.Start.Equals(old.Start) || !current.End.Equals(old.End) || current.Radius != old.Radius || current.Active != old.Active;
                    _geometry[i] = current;
                }
                bool omitted = false; float scale = _worldRoot.lossyScale.x;
                foreach (var s in _states)
                {
                    Need(s.Renderer != null && s.Renderer.sharedMesh == s.Source && s.Source.vertexCount == s.Rest.Length,
                        "Cloth source changed; rebuild serialized collision selection bindings.");
                    bool skinChanged = forceGeometry || !s.SkinReady;
                    foreach (int b in s.UsedBones)
                    { Matrix4x4 matrix = s.Bones[b].localToWorldMatrix * s.Bindposes[b]; skinChanged |= !matrix.Equals(s.Skin[b]); s.Skin[b] = matrix; }
                    if (skinChanged)
                    {
                        for (int i = 0; i < s.Indices.Length; i++)
                        {
                            int vertex = s.Indices[i]; Vector3 rest = s.Rest[vertex]; float x = 0f, y = 0f, z = 0f;
                            for (int w = s.Offsets[vertex], end = w + s.Counts[vertex]; w < end; w++)
                            {
                                BoneWeight1 weight = s.Weights[w]; Matrix4x4 m = s.Skin[weight.boneIndex]; float factor = weight.weight;
                                x += (m.m00 * rest.x + m.m01 * rest.y + m.m02 * rest.z + m.m03) * factor;
                                y += (m.m10 * rest.x + m.m11 * rest.y + m.m12 * rest.z + m.m13) * factor;
                                z += (m.m20 * rest.x + m.m21 * rest.y + m.m22 * rest.z + m.m23) * factor;
                            }
                            var world = new Vector3(x, y, z); Need(Finite(world), "Actual source skin target is nonfinite."); s.Target[i] = world;
                        }
                        s.Tree.Refit(s.Target); s.SkinReady = true; SkinPoseUpdates++;
                    }
                    if (!skinChanged && !candidatesChanged && s.QueryReady && scale == s.QueryScale)
                    { ExactPoseCacheHits++; omitted |= s.Report.omittedPotentialCandidates > 0; continue; }
                    s.Tree.Evaluate(s.Target, scale, _geometry, s.Gap, s.SkinGap);
                    LastPointDistanceTests += s.Tree.LastPointTests; s.QueryReady = true; s.QueryScale = scale;
                    int selected = SelectCandidates(_geometry, s.Gap, s.SkinGap, s.Containers, s.Ranked, s.Represented, NativeCapsuleLimit,
                        out int potential, out int independent);
                    int omittedCount = 0, redundant = 0; float omittedSkin = 0f;
                    for (int i = 0; i < _candidates.Length; i++)
                    {
                        if (s.Gap[i] <= 0f && s.Containers[i] != i) redundant++;
                        if (s.Gap[i] <= 0f && !s.Represented[i]) { omittedCount++; omittedSkin = Mathf.Max(omittedSkin, -s.SkinGap[i]); }
                    }
                    bool changed = s.Assigned.Length != selected;
                    for (int i = 0; !changed && i < selected; i++) changed = s.Assigned[i] != _candidates[s.Ranked[i]];
                    if (changed)
                    {
                        s.Assigned = s.AssignmentBuffers[selected];
                        for (int i = 0; i < selected; i++) s.Assigned[i] = _candidates[s.Ranked[i]];
                        s.Cloth.capsuleColliders = s.Assigned; s.Report.assignmentChanges++;
                    }
                    // Another owner's re-enable/configuration can restore its serialized initial subset.
                    // Runtime integration must call ReapplyAssignments after such explicit settings changes.
                    s.Report.potentialCandidates = potential; s.Report.independentPotentialCandidates = independent;
                    s.Report.containedCandidates = redundant; s.Report.omittedPotentialCandidates = omittedCount;
                    s.Report.maximumOmittedSkinTargetPenetrationMeters = omittedSkin; s.Report.assignedCapsules = selected;
                    s.Report.status = omittedCount == 0 ? "MEASURED_ENVELOPE_COVERED_PENDING_PHYSICS" : "WAIT_CAPACITY";
                    omitted |= omittedCount > 0;
                }
                RefreshCount++; LastError = null; Status = omitted ? "WAIT_CAPACITY" : "MEASURED_ENVELOPE_COVERED_PENDING_PHYSICS"; return true;
            }
            catch (Exception e) { LastError = e.GetBaseException().Message; Status = "WAIT"; return false; }
            finally { LastRefreshMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency; LastRefreshAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated; }
        }

        public void ReapplyAssignments()
        { foreach (var s in _states) if (s.Cloth != null) s.Cloth.capsuleColliders = s.Assigned; }
        public CapsuleCollider[] GetAssignedCapsules(Cloth cloth)
        {
            var s = _states.SingleOrDefault(s => s.Cloth == cloth);
            if (s == null) throw new ArgumentException("Cloth is not bound.");
            return (CapsuleCollider[])s.Assigned.Clone();
        }
        public SurfaceSelection[] SnapshotSelections()
        {
            foreach (var s in _states)
            {
                s.Report.selectedCandidateIndices = s.Ranked.Take(s.Assigned.Length).ToArray();
                s.Report.omittedPotentialCandidateIndices = Enumerable.Range(0, _candidates.Length).Where(i => s.Gap[i] <= 0f && !s.Represented[i]).ToArray();
            }
            return Selections;
        }
        public ActualOmissionAudit AuditActualParticles(Cloth cloth, Vector3[] actualWorldParticles)
        {
            var s = _states.SingleOrDefault(s => s.Cloth == cloth);
            Need(s != null && actualWorldParticles != null && actualWorldParticles.All(Finite), "Actual finite world particle data for a bound Cloth is required.");
            var report = new ActualOmissionAudit { cloth = cloth.name, particles = actualWorldParticles.Length };
            for (int c = 0; c < _candidates.Length; c++) if (_geometry[c].Active && !s.Represented[c]) report.omittedCandidateCount++;
            foreach (var point in actualWorldParticles)
            {
                bool penetrating = false;
                for (int c = 0; c < _candidates.Length; c++)
                {
                    if (!_geometry[c].Active || s.Represented[c]) continue;
                    float depth = _geometry[c].Radius - SegmentDistance(point, _geometry[c].Start, _geometry[c].End);
                    if (depth > 0f) { penetrating = true; report.penetratingPointCandidatePairs++; report.maximumOmittedPenetrationMeters = Mathf.Max(report.maximumOmittedPenetrationMeters, depth); }
                }
                if (penetrating) report.penetratingParticles++;
            }
            return report;
        }
        public static CapsuleGeometry ReadCapsule(CapsuleCollider c)
        {
            Need(c != null && PlayerSecondaryMotionRig.ValidateProxy(c, out _) && Uniform(c.transform.lossyScale), "Candidate proxy ownership/scale/isolation changed.");
            float scale = c.transform.lossyScale.x, radius = c.radius * scale, half = Mathf.Max(0f, c.height * .5f - c.radius) * scale;
            Vector3 center = c.transform.TransformPoint(c.center);
            Vector3 axis = c.direction == 0 ? c.transform.right : c.direction == 2 ? c.transform.forward : c.transform.up;
            Need(Finite(center) && Finite(radius) && radius > 0f, "Finite positive candidate capsule geometry is required.");
            return new CapsuleGeometry { Start = center - axis * half, End = center + axis * half, Radius = radius, Active = c.enabled && c.gameObject.activeInHierarchy };
        }

        /// <summary>Immutable source partition; all bounds refit from every current target point.
        /// Branch pruning uses conservative cluster bounds, never sampled or omitted source geometry.</summary>
        public sealed class PointTree
        {
            private struct Node
            {
                public int Start, Count, Left, Right;
                public float LowX, LowY, LowZ, HighX, HighY, HighZ, MaxMobility;
            }
            private struct Query
            {
                public float Ax, Ay, Az, Dx, Dy, Dz, InverseLengthSquared, Radius, Scale, MinGap, MinSkin;
                public float LowX, LowY, LowZ, HighX, HighY, HighZ;
                public Vector3[] Points;
            }
            private readonly int[] _order;
            private readonly float[] _mobility;
            private readonly Node[] _nodes;
            private bool _hasBounds;
            public int LastPointTests { get; private set; }
            public PointTree(Vector3[] sourcePositions, float[] immutableMobility)
            {
                if (sourcePositions == null || sourcePositions.Length == 0 || immutableMobility == null || immutableMobility.Length != sourcePositions.Length
                    || sourcePositions.Any(p => !Finite(p)) || immutableMobility.Any(v => !Finite(v) || v < 0f)) throw new ArgumentException("Complete finite immutable source partition data is required.");
                _order = Enumerable.Range(0, sourcePositions.Length).ToArray(); _mobility = (float[])immutableMobility.Clone();
                var nodes = new List<Node>();
                int Build(int start, int count)
                {
                    int id = nodes.Count; nodes.Add(default);
                    var node = new Node { Start = start, Count = count, Left = -1, Right = -1 };
                    if (count > 24)
                    {
                        Vector3 low = sourcePositions[_order[start]], high = low;
                        for (int i = start + 1; i < start + count; i++) { low = Vector3.Min(low, sourcePositions[_order[i]]); high = Vector3.Max(high, sourcePositions[_order[i]]); }
                        Vector3 size = high - low; int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                        Array.Sort(_order, start, count, Comparer<int>.Create((a, b) => { int compare = sourcePositions[a][axis].CompareTo(sourcePositions[b][axis]); return compare != 0 ? compare : a.CompareTo(b); }));
                        int half = count / 2; node.Left = Build(start, half); node.Right = Build(start + half, count - half);
                    }
                    for (int i = start; i < start + count; i++) node.MaxMobility = Math.Max(node.MaxMobility, _mobility[_order[i]]);
                    nodes[id] = node; return id;
                }
                Build(0, sourcePositions.Length); _nodes = nodes.ToArray();
            }
            public void Refit(Vector3[] points)
            {
                if (points == null || points.Length != _order.Length) throw new ArgumentException("Every original partition point must be refitted.");
                for (int n = _nodes.Length - 1; n >= 0; n--)
                {
                    Node node = _nodes[n];
                    if (node.Left < 0)
                    {
                        Vector3 p = points[_order[node.Start]];
                        node.LowX = node.HighX = p.x; node.LowY = node.HighY = p.y; node.LowZ = node.HighZ = p.z;
                        for (int i = node.Start; i < node.Start + node.Count; i++)
                        {
                            p = points[_order[i]]; if (!Finite(p)) throw new ArgumentException("Nonfinite actual point.");
                            if (p.x < node.LowX) node.LowX = p.x; if (p.y < node.LowY) node.LowY = p.y; if (p.z < node.LowZ) node.LowZ = p.z;
                            if (p.x > node.HighX) node.HighX = p.x; if (p.y > node.HighY) node.HighY = p.y; if (p.z > node.HighZ) node.HighZ = p.z;
                        }
                    }
                    else
                    {
                        Node a = _nodes[node.Left], b = _nodes[node.Right];
                        node.LowX = Math.Min(a.LowX, b.LowX); node.LowY = Math.Min(a.LowY, b.LowY); node.LowZ = Math.Min(a.LowZ, b.LowZ);
                        node.HighX = Math.Max(a.HighX, b.HighX); node.HighY = Math.Max(a.HighY, b.HighY); node.HighZ = Math.Max(a.HighZ, b.HighZ);
                    }
                    _nodes[n] = node;
                }
                _hasBounds = true;
            }
            private static float LowerSkinGap(Node n, Query q)
            {
                float x = Math.Max(0f, Math.Max(q.LowX - n.HighX, n.LowX - q.HighX));
                float y = Math.Max(0f, Math.Max(q.LowY - n.HighY, n.LowY - q.HighY));
                float z = Math.Max(0f, Math.Max(q.LowZ - n.HighZ, n.LowZ - q.HighZ));
                return (float)Math.Sqrt(x * x + y * y + z * z) - q.Radius;
            }
            private void Visit(int id, ref Query q)
            {
                Node n = _nodes[id]; float lower = LowerSkinGap(n, q);
                // One micrometre of conservative arithmetic slack affects pruning only, not any
                // source point, candidate score or acceptance threshold.
                if (lower - .000001f >= q.MinSkin && lower - n.MaxMobility * q.Scale - .000001f >= q.MinGap) return;
                if (n.Left >= 0)
                {
                    float a = LowerSkinGap(_nodes[n.Left], q) - _nodes[n.Left].MaxMobility * q.Scale;
                    float b = LowerSkinGap(_nodes[n.Right], q) - _nodes[n.Right].MaxMobility * q.Scale;
                    if (a <= b) { Visit(n.Left, ref q); Visit(n.Right, ref q); }
                    else { Visit(n.Right, ref q); Visit(n.Left, ref q); }
                    return;
                }
                for (int i = n.Start, end = n.Start + n.Count; i < end; i++)
                {
                    int vertex = _order[i]; Vector3 point = q.Points[vertex];
                    float x = point.x - q.Ax, y = point.y - q.Ay, z = point.z - q.Az;
                    float t = q.InverseLengthSquared > 0f ? (x * q.Dx + y * q.Dy + z * q.Dz) * q.InverseLengthSquared : 0f;
                    t = t < 0f ? 0f : t > 1f ? 1f : t;
                    x -= q.Dx * t; y -= q.Dy * t; z -= q.Dz * t;
                    float skin = (float)Math.Sqrt(x * x + y * y + z * z) - q.Radius, gap = skin - _mobility[vertex] * q.Scale;
                    if (skin < q.MinSkin) q.MinSkin = skin; if (gap < q.MinGap) q.MinGap = gap; LastPointTests++;
                }
            }
            public void Evaluate(Vector3[] points, float scale, CapsuleGeometry[] geometry, float[] envelopeGaps, float[] skinGaps)
            {
                if (!_hasBounds || points == null || points.Length != _order.Length || !Finite(scale) || scale <= 0f || geometry == null
                    || envelopeGaps.Length != geometry.Length || skinGaps.Length != geometry.Length) throw new ArgumentException("Refit full target points before evaluating the source partition.");
                LastPointTests = 0;
                for (int c = 0; c < geometry.Length; c++)
                {
                    var g = geometry[c];
                    if (!g.Active) { envelopeGaps[c] = skinGaps[c] = float.PositiveInfinity; continue; }
                    if (!Finite(g.Start) || !Finite(g.End) || !Finite(g.Radius) || g.Radius <= 0f) throw new ArgumentException("Invalid actual capsule.");
                    Vector3 d = g.End - g.Start;
                    var q = new Query { Points = points, Ax = g.Start.x, Ay = g.Start.y, Az = g.Start.z, Dx = d.x, Dy = d.y, Dz = d.z,
                        InverseLengthSquared = d.sqrMagnitude > 1e-12f ? 1f / d.sqrMagnitude : 0f, Radius = g.Radius, Scale = scale,
                        LowX = Math.Min(g.Start.x, g.End.x), LowY = Math.Min(g.Start.y, g.End.y), LowZ = Math.Min(g.Start.z, g.End.z),
                        HighX = Math.Max(g.Start.x, g.End.x), HighY = Math.Max(g.Start.y, g.End.y), HighZ = Math.Max(g.Start.z, g.End.z),
                        MinGap = float.PositiveInfinity, MinSkin = float.PositiveInfinity };
                    float lower = LowerSkinGap(_nodes[0], q), mobility = _nodes[0].MaxMobility * scale;
                    if (lower > mobility) { envelopeGaps[c] = lower - mobility; skinGaps[c] = lower; continue; }
                    Visit(0, ref q); envelopeGaps[c] = q.MinGap; skinGaps[c] = q.MinSkin;
                }
            }
        }
        [Serializable] public sealed class SelectionEquivalenceAudit
        {
            public string scope = "Full original source skin weights/vertices/mobility and scalar-free linear reference versus optimized current target tree and selection. Forced geometry timings exercise skin recomputation/tree refit/query every call; they are not a GPU or locomotion benchmark. Native solved cloth quality remains separate.";
            public bool selectionEquivalent;
            public string error;
            public int surfaces, rawSourcePoints, uniqueSourcePoints, selectedOrderMismatches, representationMismatches, potentialCountMismatches;
            public float maximumTargetDeltaMeters, maximumCandidateGapDifferenceMeters;
            public int cachedCalls, forcedGeometryCalls, lastForcedPointTests;
            public long cachedAllocatedBytes, forcedGeometryAllocatedBytes;
            public double cachedMeanMilliseconds, forcedGeometryMeanMilliseconds;
        }
        public SelectionEquivalenceAudit AuditAgainstFullSource(int repeatRefreshCalls = 32)
        {
            var report = new SelectionEquivalenceAudit();
            try
            {
                Need(RefreshNow(), LastError); float scale = _worldRoot.lossyScale.x;
                foreach (var s in _states)
                {
                    // Independently read the actual bone matrices and skin EVERY original imported vertex.
                    // The reference intentionally keeps UV/normal duplicates and the previous Vector3 path.
                    var skin = new Matrix4x4[s.Bones.Length];
                    foreach (int b in s.UsedBones) skin[b] = s.Bones[b].localToWorldMatrix * s.Bindposes[b];
                    var raw = new Vector3[s.Rest.Length];
                    for (int v = 0; v < raw.Length; v++)
                        for (int w = s.Offsets[v], end = w + s.Counts[v]; w < end; w++)
                        { var influence = s.Weights[w]; raw[v] += skin[influence.boneIndex].MultiplyPoint3x4(s.Rest[v]) * influence.weight; }
                    for (int i = 0; i < s.Indices.Length; i++)
                        report.maximumTargetDeltaMeters = Mathf.Max(report.maximumTargetDeltaMeters, Vector3.Distance(raw[s.Indices[i]], s.Target[i]));
                    var gap = new float[_geometry.Length]; var skinGap = new float[_geometry.Length];
                    EvaluateCandidateGaps(raw, s.AllMobility, scale, _geometry, gap, skinGap);
                    var containers = new int[_geometry.Length]; var ranked = new int[_geometry.Length]; var represented = new bool[_geometry.Length];
                    int count = SelectCandidates(_geometry, gap, skinGap, containers, ranked, represented, NativeCapsuleLimit, out int potential, out int independent);
                    if (count != s.Assigned.Length) report.selectedOrderMismatches++;
                    for (int i = 0; i < Math.Min(count, s.Assigned.Length); i++) if (ranked[i] != s.Ranked[i]) report.selectedOrderMismatches++;
                    if (potential != s.Report.potentialCandidates || independent != s.Report.independentPotentialCandidates) report.potentialCountMismatches++;
                    for (int i = 0; i < represented.Length; i++)
                    {
                        if (represented[i] != s.Represented[i]) report.representationMismatches++;
                        if (Finite(gap[i]) && Finite(s.Gap[i])) report.maximumCandidateGapDifferenceMeters = Mathf.Max(report.maximumCandidateGapDifferenceMeters, Mathf.Abs(gap[i] - s.Gap[i]));
                        if (Finite(skinGap[i]) && Finite(s.SkinGap[i])) report.maximumCandidateGapDifferenceMeters = Mathf.Max(report.maximumCandidateGapDifferenceMeters, Mathf.Abs(skinGap[i] - s.SkinGap[i]));
                    }
                    report.surfaces++; report.rawSourcePoints += raw.Length; report.uniqueSourcePoints += s.Indices.Length;
                }
                Need(RefreshNow(), LastError);
                int cached = Mathf.Clamp(repeatRefreshCalls, 1, 128), forced = Mathf.Clamp(cached / 4, 1, 16);
                long allocation = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < cached; i++) Need(RefreshNow(), LastError);
                report.cachedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
                report.cachedMeanMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency / cached; report.cachedCalls = cached;
                Need(RefreshWithFullGeometry(), LastError); // Warm the full changed-pose path before measuring.
                allocation = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
                for (int i = 0; i < forced; i++) Need(RefreshWithFullGeometry(), LastError);
                report.forcedGeometryAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
                report.forcedGeometryMeanMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency / forced; report.forcedGeometryCalls = forced;
                report.lastForcedPointTests = LastPointDistanceTests;
                report.selectionEquivalent = report.surfaces > 0 && report.selectedOrderMismatches == 0 && report.representationMismatches == 0
                    && report.potentialCountMismatches == 0 && report.maximumTargetDeltaMeters <= .00001f && report.maximumCandidateGapDifferenceMeters <= .00001f;
            }
            catch (Exception e) { report.error = e.GetBaseException().Message; }
            return report;
        }

        // Retained independent original full linear reference for actual source equivalence audits.
        public static void EvaluateCandidateGaps(Vector3[] target, float[] mobility, float scale, CapsuleGeometry[] capsules, float[] envelopeGaps, float[] skinGaps)
        {
            if (target == null || mobility == null || target.Length != mobility.Length || target.Length == 0 || !Finite(scale) || scale <= 0f
                || capsules == null || envelopeGaps.Length != capsules.Length || skinGaps.Length != capsules.Length) throw new ArgumentException("Complete target, exact mobility and candidate scratch arrays are required.");
            Vector3 low = target[0], high = target[0]; float maximumMobility = 0f;
            for (int v = 0; v < target.Length; v++)
            {
                if (!Finite(target[v]) || !Finite(mobility[v]) || mobility[v] < 0f) throw new ArgumentException("Invalid skin target or mobility.");
                low = Vector3.Min(low, target[v]); high = Vector3.Max(high, target[v]); maximumMobility = Mathf.Max(maximumMobility, mobility[v] * scale);
            }
            for (int c = 0; c < capsules.Length; c++)
            {
                float gap = float.PositiveInfinity, skin = float.PositiveInfinity; var capsule = capsules[c];
                if (capsule.Active)
                {
                    if (!Finite(capsule.Start) || !Finite(capsule.End) || !Finite(capsule.Radius) || capsule.Radius <= 0f) throw new ArgumentException("Invalid active capsule.");
                    Vector3 segmentLow = Vector3.Min(capsule.Start, capsule.End), segmentHigh = Vector3.Max(capsule.Start, capsule.End);
                    float dx = Mathf.Max(0f, Mathf.Max(segmentLow.x - high.x, low.x - segmentHigh.x));
                    float dy = Mathf.Max(0f, Mathf.Max(segmentLow.y - high.y, low.y - segmentHigh.y));
                    float dz = Mathf.Max(0f, Mathf.Max(segmentLow.z - high.z, low.z - segmentHigh.z));
                    float lowerSkinGap = Mathf.Sqrt(dx * dx + dy * dy + dz * dz) - capsule.Radius;
                    if (lowerSkinGap > maximumMobility)
                    {
                        // Disjoint containing AABBs prove every authored target+mobility sphere is
                        // outside this capsule. Positive gap is a conservative lower bound here.
                        envelopeGaps[c] = lowerSkinGap - maximumMobility; skinGaps[c] = lowerSkinGap; continue;
                    }
                    Vector3 axis = capsule.End - capsule.Start;
                    float inverseLengthSquared = axis.sqrMagnitude > 1e-12f ? 1f / axis.sqrMagnitude : 0f;
                    for (int v = 0; v < target.Length; v++)
                    {
                        Vector3 delta = target[v] - capsule.Start;
                        float t = inverseLengthSquared > 0f ? Mathf.Clamp01(Vector3.Dot(delta, axis) * inverseLengthSquared) : 0f;
                        float d = (delta - axis * t).magnitude - capsule.Radius;
                        skin = Mathf.Min(skin, d); gap = Mathf.Min(gap, d - mobility[v] * scale);
                    }
                }
                envelopeGaps[c] = gap; skinGaps[c] = skin;
            }
        }
        public static int SelectCandidates(CapsuleGeometry[] geometry, float[] gaps, float[] skinGaps, int[] containers, int[] ranked, bool[] represented,
            int limit, out int potential, out int independent)
        {
            int n = geometry?.Length ?? 0;
            if (n == 0 || gaps.Length != n || skinGaps.Length != n || containers.Length != n || ranked.Length != n || represented.Length != n
                || limit < 1 || limit > NativeCapsuleLimit) throw new ArgumentException("Complete candidate scratch arrays and measured <=16 capsule budget are required.");
            potential = independent = 0;
            for (int i = 0; i < n; i++)
            {
                represented[i] = false; containers[i] = i;
                if (!geometry[i].Active || gaps[i] > 0f) continue;
                potential++;
                for (int j = 0; j < n; j++)
                    if (i != j && geometry[j].Active && gaps[j] <= 0f && Contains(geometry[j], geometry[i])
                        && (!Contains(geometry[i], geometry[j]) || j < i))
                    { containers[i] = j; break; }
            }
            // Strict containment cannot cycle; exact coincident proxies choose the lower index.
            for (int i = 0; i < n; i++)
            {
                int root = containers[i], safety = 0;
                while (containers[root] != root && safety++ < n) root = containers[root];
                if (safety >= n) throw new ArgumentException("Invalid capsule containment cycle.");
                containers[i] = root;
                if (geometry[i].Active && gaps[i] <= 0f && root == i) ranked[independent++] = i;
            }
            // Stable insertion sort by actual skin proximity, then full envelope proximity, then original candidate index.
            for (int i = 1; i < independent; i++)
            {
                int candidate = ranked[i], j = i - 1;
                while (j >= 0 && (skinGaps[candidate] < skinGaps[ranked[j]] || skinGaps[candidate] == skinGaps[ranked[j]] &&
                    (gaps[candidate] < gaps[ranked[j]] || gaps[candidate] == gaps[ranked[j]] && candidate < ranked[j])))
                { ranked[j + 1] = ranked[j]; j--; }
                ranked[j + 1] = candidate;
            }
            int selected = Math.Min(limit, independent);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < selected; j++)
                    if (i == ranked[j] || containers[i] == ranked[j] || Contains(geometry[ranked[j]], geometry[i])) { represented[i] = true; break; }
            return selected;
        }
        public static bool Contains(CapsuleGeometry outer, CapsuleGeometry inner)
        {
            if (!outer.Active || !inner.Active || outer.Radius < inner.Radius) return false;
            // A capsule is a segment Minkowski-summed with a ball. Both inner segment endpoints inside
            // the eroded outer capsule imply complete containment by convexity, including sphere degeneracies.
            float radius = outer.Radius - inner.Radius;
            return SegmentDistance(inner.Start, outer.Start, outer.End) <= radius && SegmentDistance(inner.End, outer.Start, outer.End) <= radius;
        }
        private static float SegmentDistance(Vector3 point, Vector3 a, Vector3 b)
        { Vector3 d = b - a; float t = d.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(point - a, d) / d.sqrMagnitude) : 0f; return (point - a - d * t).magnitude; }
        private static bool Uniform(Vector3 s) => Finite(s) && s.x > 0f && Mathf.Abs(s.x - s.y) < .0001f && Mathf.Abs(s.x - s.z) < .0001f;
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static void Need(bool v, string message) { if (!v) throw new InvalidOperationException(message); }
    }
}
