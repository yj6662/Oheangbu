using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace Oheangbu.Presentation
{
    /// <summary>Fits explicit arm collision candidates to current linear-skinned triangle fragments.
    /// Cloth anchors never enter the fit. Whole-fragment coverage follows capsule convexity.
    /// Candidate coverage alone does not approve garment deformation or native collision.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(1400)]
    public sealed partial class PlayerClothBodyProxyRig : MonoBehaviour
    {
        [Serializable] public struct Corner { public int a, b, c; public Vector3 bary; }
        [Serializable] public sealed class Region
        {
            public float lower, upper;
            public Corner[] corners;
            // Unmerged polygon corners retain the proof over every original triangle fragment.
            // Rebuild old bindings: fitting points alone cannot attest partition completeness.
            public Corner[] coverageCorners;
            public int[] polygonSizes;
            public CapsuleCollider capsule;
        }
        [Serializable] public sealed class Binding
        { public SkinnedMeshRenderer renderer; public Region[] regions; public double restArea, partitionAreaError; }
        [Serializable] public struct Fit
        {
            public Vector3 start, end;
            public float radius, volume, maximumOutside, maximumRawCornerOutside, axisStepDegrees, relativeEigenGap;
            public int corners, rawCorners;
        }
        [Serializable] public sealed class Snapshot
        { public string renderer; public Fit[] regions; }
        [SerializeField] private Transform _representation;
        [SerializeField] private Binding[] _bindings = Array.Empty<Binding>();
        private Source[] _sources = Array.Empty<Source>();
        private Transform[] _tracked = Array.Empty<Transform>();
        private Vector3[] _positions, _scales;
        private Quaternion[] _rotations;
        private Transform[] _parents;
        private bool _ready, _hasPose;
        private Cloth[] _cloth = Array.Empty<Cloth>();
        public string LastError { get; private set; }
        public double LastRefreshMilliseconds { get; private set; }
        public long LastRefreshAllocatedBytes { get; private set; }
        public int FitCount { get; private set; }
        public int PoseCacheHits { get; private set; }
        public int SkippedInactiveLateFrames { get; private set; }
        public Snapshot[] Measurements { get; private set; } = Array.Empty<Snapshot>();
        private static readonly float[] Planes = { .12f, .22f, .442f, .68f };

        private sealed class Source
        {
            public Binding Binding; public Mesh Mesh; public Transform[] Bones;
            public Matrix4x4[] Bindposes, Skin; public BoneWeight[] Weights;
            public Vector3[] Rest, Current; public FitWorkspace[] Regions;
            public int[] Representatives, UsedBones;
        }
        /// <summary>Reusable pure geometry workspace. Construct once; MeasureFit does not allocate.</summary>
        public sealed class FitWorkspace
        {
            public Vector3[] Points;
            public Vector3 PreviousAxis = Vector3.right;
            internal Vector3[] RadialOffsets;
            internal float[] Projection, RadialSquared;
            internal readonly float[] LeftMaximum = new float[19], RightMaximum = new float[19];
            internal readonly float[] LeftRadius = new float[19], RightRadius = new float[19];
            internal readonly float[] LeftPosition = new float[19], RightPosition = new float[19];
            internal bool HasAxis;
            public FitWorkspace(int count)
            {
                Need(count >= 3, "At least three anatomical corners required.");
                Points = new Vector3[count]; Projection = new float[count]; RadialSquared = new float[count]; RadialOffsets = new Vector3[count];
            }
        }
        private struct Tagged { public Corner Corner; public float Parameter; }

        public bool Configure(Transform representation, Binding[] bindings, out string error)
        {
            _representation = representation; _bindings = bindings; _ready = false; _hasPose = false;
            bool valid = RefreshNow(); error = LastError; return valid;
        }
        private void OnEnable() { _ready = false; _hasPose = false; }
        private void LateUpdate()
        {
            if (_representation == null) return;
            // Native Cloth is disabled at distant LODs. Explicit RefreshNow remains
            // available for binding and for same-frame diagnostic pose changes.
            if (!_ready) { try { Initialize(); } catch (Exception e) { LastError=e.GetBaseException().Message; return; } }
            bool active=false; foreach(var cloth in _cloth) if(cloth!=null && cloth.enabled && cloth.gameObject.activeInHierarchy) { active=true; break; }
            if (!active) { SkippedInactiveLateFrames++; return; }
            RefreshNow();
        }

        private static Source ReadSource(Binding binding)
        {
            var r = binding.renderer; var mesh = r != null ? r.sharedMesh : null;
            Need(mesh != null && mesh.isReadable && mesh.blendShapeCount == 0, "Readable shape-free anatomical source required.");
            var source = new Source { Binding = binding, Mesh = mesh, Bones = r.bones, Bindposes = mesh.bindposes,
                Rest = mesh.vertices, Weights = mesh.boneWeights, Current = new Vector3[mesh.vertexCount] };
            Need(source.Weights.Length == mesh.vertexCount && source.Bones.Length == source.Bindposes.Length, "Complete four-weight anatomical source required.");
            using (var counts = mesh.GetBonesPerVertex())
            using (var weights = mesh.GetAllBoneWeights())
            {
                foreach (byte count in counts) Need(count >= 1 && count <= 4, "Anatomical skin exceeds four influences.");
                source.Representatives = PlayerClothBrushProxyRig.BuildEquivalentVertexRepresentatives(source.Rest, counts.ToArray(), weights.ToArray(), null);
            }
            foreach (var bone in source.Bones) Need(bone != null, "Missing anatomical bone transform.");
            var usedBones = new HashSet<int>();
            foreach (var w in source.Weights)
            {
                Need(Finite(w.weight0) && Finite(w.weight1) && Finite(w.weight2) && Finite(w.weight3)
                    && w.weight0 >= 0f && w.weight1 >= 0f && w.weight2 >= 0f && w.weight3 >= 0f
                    && Mathf.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1f) <= .0001f, "Invalid anatomical weights.");
                Need(w.boneIndex0 >= 0 && w.boneIndex0 < source.Bones.Length && w.boneIndex1 >= 0 && w.boneIndex1 < source.Bones.Length
                    && w.boneIndex2 >= 0 && w.boneIndex2 < source.Bones.Length && w.boneIndex3 >= 0 && w.boneIndex3 < source.Bones.Length, "Missing anatomical bone.");
                if (w.weight0 > 0f) usedBones.Add(w.boneIndex0);
                if (w.weight1 > 0f) usedBones.Add(w.boneIndex1);
                if (w.weight2 > 0f) usedBones.Add(w.boneIndex2);
                if (w.weight3 > 0f) usedBones.Add(w.boneIndex3);
            }
            source.UsedBones = usedBones.OrderBy(i => i).ToArray();
            source.Skin = new Matrix4x4[source.Bones.Length];
            return source;
        }

        private static void Skin(Source s, Transform representation)
        {
            Matrix4x4 inverse = representation.worldToLocalMatrix;
            foreach (int i in s.UsedBones) s.Skin[i] = inverse * s.Bones[i].localToWorldMatrix * s.Bindposes[i];
            for (int i = 0; i < s.Rest.Length; i++)
            {
                if (s.Representatives[i] != i) { s.Current[i] = s.Current[s.Representatives[i]]; continue; }
                var w = s.Weights[i]; Vector3 p = s.Rest[i];
                s.Current[i] = s.Skin[w.boneIndex0].MultiplyPoint3x4(p) * w.weight0 + s.Skin[w.boneIndex1].MultiplyPoint3x4(p) * w.weight1
                    + s.Skin[w.boneIndex2].MultiplyPoint3x4(p) * w.weight2 + s.Skin[w.boneIndex3].MultiplyPoint3x4(p) * w.weight3;
                Need(Finite(s.Current[i]), "Nonfinite anatomical skin.");
            }
        }

        /// <summary>Call only on the fresh imported rest model. Parameters are serialized
        /// so loading a prefab during a posed frame never redefines its partition.</summary>
        public static Binding AuthorRestBinding(SkinnedMeshRenderer renderer, Transform representation, out Fit[] fits)
        {
            var binding = new Binding { renderer = renderer, regions = new Region[3] };
            Source source = ReadSource(binding); Skin(source, representation); int[] triangles = source.Mesh.triangles;
            binding.regions = AuthorPartitions(source.Current, triangles, source.Representatives, out double area, out double areaError);
            binding.restArea = area; binding.partitionAreaError = areaError;
            fits = new Fit[3];
            for (int i = 0; i < 3; i++)
            {
                var scratch = new FitWorkspace(binding.regions[i].corners.Length);
                FillPoints(source, binding.regions[i], scratch); fits[i] = MeasureFit(scratch);
                fits[i].maximumRawCornerOutside = MaximumCornerOutside(source.Current, binding.regions[i].coverageCorners, fits[i]);
                fits[i].rawCorners = binding.regions[i].coverageCorners.Length;
            }
            return binding;
        }

        /// <summary>Pure authoring operation: partition the complete rest triangle stream.
        /// Representatives must describe exact rest-position AND skin-influence equivalence.
        /// Never pass a tolerance-based position weld or a posed-space spatial merge.</summary>
        public static Region[] AuthorPartitions(Vector3[] rest, int[] triangles, int[] representatives, out double restArea, out double areaError)
        {
            Need(rest != null && representatives != null && representatives.Length == rest.Length && triangles != null && triangles.Length % 3 == 0,
                "Complete rest geometry and exact representative map required.");
            for (int i = 0; i < rest.Length; i++) Need(Finite(rest[i]) && representatives[i] >= 0 && representatives[i] <= i && rest[i].Equals(rest[representatives[i]]), "Invalid rest representative.");
            float[] parameters = rest.Select(p => Mathf.Abs(p.x)).ToArray();
            Need(parameters.Min() >= Planes[0] && parameters.Max() <= Planes[3], "Rest arm geometry lies outside authored partition domain.");
            var result = new Region[3]; restArea = 0d; double partitionArea = 0d;
            foreach (int index in triangles) Need(index >= 0 && index < rest.Length, "Invalid anatomical triangle index.");
            for (int i = 0; i < triangles.Length; i += 3) restArea += TriangleArea(rest[triangles[i]], rest[triangles[i + 1]], rest[triangles[i + 2]]);
            for (int region = 0; region < 3; region++)
            {
                var corners = new List<Corner>(); var raw = new List<Corner>(); var sizes = new List<int>();
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    var poly = new List<Tagged> {
                        new Tagged { Corner = new Corner { a=a,b=b,c=c,bary=Vector3.right }, Parameter=parameters[a] },
                        new Tagged { Corner = new Corner { a=a,b=b,c=c,bary=Vector3.up }, Parameter=parameters[b] },
                        new Tagged { Corner = new Corner { a=a,b=b,c=c,bary=Vector3.forward }, Parameter=parameters[c] } };
                    poly = Clip(Clip(poly, Planes[region], true), Planes[region + 1], false);
                    if (poly.Count >= 3)
                    {
                        sizes.Add(poly.Count);
                        foreach (var p in poly) { raw.Add(p.Corner); corners.Add(CanonicalCorner(p, representatives, parameters)); }
                        for (int j = 1; j + 1 < poly.Count; j++)
                            partitionArea += TriangleArea(Point(rest, poly[0].Corner), Point(rest, poly[j].Corner), Point(rest, poly[j + 1].Corner));
                    }
                }
                Need(corners.Count > 3, "Empty anatomical partition.");
                // A shared corner is one PCA sample, regardless of UV seams or incident
                // triangle count. Canonical edge interpolation also removes direction-dependent rounding.
                var unique = corners.GroupBy(c => (c.a,c.b,c.c,c.bary.x,c.bary.y,c.bary.z)).Select(g => g.First()).ToArray();
                result[region] = new Region { lower = Planes[region], upper = Planes[region + 1], corners = unique, coverageCorners = raw.ToArray(), polygonSizes = sizes.ToArray() };
            }
            areaError = Math.Abs(restArea - partitionArea);
            Need(areaError <= Math.Max(1e-7d, restArea * 1e-5d), "Triangle partition does not conserve original rest surface area.");
            return result;
        }
        private static Corner CanonicalCorner(Tagged tagged, int[] representatives, float[] parameters)
        {
            var p = tagged.Corner;
            Span<int> ids = stackalloc int[3]; Span<float> weights = stackalloc float[3]; int count = 0;
            for (int j = 0; j < 3; j++)
            {
                float weight = p.bary[j]; if (weight == 0f) continue;
                int id = representatives[j == 0 ? p.a : j == 1 ? p.b : p.c], slot = 0;
                for (; slot < count && ids[slot] != id; slot++) { }
                if (slot == count) { ids[count] = id; weights[count++] = 0f; }
                weights[slot] += weight;
            }
            Need(count >= 1 && count <= 2, "Unexpected interior corner from parallel rest-parameter clipping.");
            if (count == 1) return new Corner { a = ids[0], b = ids[0], c = ids[0], bary = Vector3.right };
            int a = Math.Min(ids[0], ids[1]), b = Math.Max(ids[0], ids[1]);
            float delta = parameters[b] - parameters[a]; Need(delta != 0f, "Clipped edge has constant parameter.");
            float t = (tagged.Parameter - parameters[a]) / delta;
            Need(Finite(t) && t >= 0f && t <= 1f, "Invalid clipped canonical edge interpolation.");
            if (t == 0f) return new Corner { a = a, b = a, c = a, bary = Vector3.right };
            if (t == 1f) return new Corner { a = b, b = b, c = b, bary = Vector3.right };
            return new Corner { a = a, b = b, c = a, bary = new Vector3(1f - t, t, 0f) };
        }
        private static double TriangleArea(Vector3 a, Vector3 b, Vector3 c)
        {
            double ux = (double)b.x - a.x, uy = (double)b.y - a.y, uz = (double)b.z - a.z;
            double vx = (double)c.x - a.x, vy = (double)c.y - a.y, vz = (double)c.z - a.z;
            double x = uy * vz - uz * vy, y = uz * vx - ux * vz, z = ux * vy - uy * vx;
            return Math.Sqrt(x * x + y * y + z * z) * .5d;
        }
        public static Vector3 Point(Vector3[] vertices, Corner p) => vertices[p.a] * p.bary.x + vertices[p.b] * p.bary.y + vertices[p.c] * p.bary.z;
        public static float MaximumCornerOutside(Vector3[] vertices, Corner[] corners, Fit fit)
        {
            float result = 0f;
            foreach (var corner in corners) result = Math.Max(result, SignedDistance(Point(vertices, corner), fit));
            return result;
        }
        private static float SignedDistance(Vector3 point, Fit fit)
        {
            Vector3 segment = fit.end - fit.start, d = point - fit.start;
            float lengthSquared = segment.sqrMagnitude, t = lengthSquared > 1e-14f ? Mathf.Clamp01(Vector3.Dot(d, segment) / lengthSquared) : 0f;
            return (d - segment * t).magnitude - fit.radius;
        }
        private static List<Tagged> Clip(List<Tagged> input, float plane, bool lower)
        {
            var result = new List<Tagged>();
            for (int i = 0; i < input.Count; i++)
            {
                Tagged current=input[i], previous=input[(i+input.Count-1)%input.Count];
                bool was=lower?previous.Parameter>=plane:previous.Parameter<=plane;
                bool now=lower?current.Parameter>=plane:current.Parameter<=plane;
                if (was != now)
                {
                    float t=(plane-previous.Parameter)/(current.Parameter-previous.Parameter);
                    Corner c=previous.Corner; c.bary=Vector3.LerpUnclamped(previous.Corner.bary,current.Corner.bary,t);
                    result.Add(new Tagged { Corner=c,Parameter=plane });
                }
                if (now) result.Add(current);
            }
            return result;
        }
        private static void FillPoints(Source source, Region region, FitWorkspace scratch)
        {
            for (int i=0;i<region.corners.Length;i++)
            {
                var p=region.corners[i];
                scratch.Points[i]=source.Current[p.a]*p.bary.x+source.Current[p.b]*p.bary.y+source.Current[p.c]*p.bary.z;
            }
        }

        private void Initialize()
        {
            Need(_representation==transform && _bindings!=null && _bindings.Length>=2
                && _bindings.All(b=>b!=null && b.renderer!=null)
                && _bindings.Select(b=>b.renderer).Distinct().Count()==_bindings.Length,
                "Existing arm sources plus explicit distinct additional anatomy required.");
            _sources=_bindings.Select(ReadSource).ToArray(); var tracked=new HashSet<Transform>();
            foreach(var s in _sources)
            {
                Need(s.Binding.regions!=null && s.Binding.regions.Length>=1 && s.Binding.regions.Length<=3
                    && s.Binding.regions.All(r=>r!=null && r.capsule!=null && r.capsule.transform.parent==_representation
                    && PlayerSecondaryMotionRig.ValidateProxy(r.capsule,out _)), "Owned root-relative fitted candidates required.");
                foreach (var region in s.Binding.regions)
                {
                    Need(region.corners != null && region.corners.Length >= 3 && region.coverageCorners != null && region.coverageCorners.Length >= region.corners.Length
                        && region.polygonSizes != null && region.polygonSizes.All(n => n >= 3) && region.polygonSizes.Sum() == region.coverageCorners.Length,
                        "Complete original triangle partition evidence required; rebuild legacy body bindings.");
                    foreach (var corner in region.corners.Concat(region.coverageCorners))
                        Need(corner.a >= 0 && corner.a < s.Rest.Length && corner.b >= 0 && corner.b < s.Rest.Length && corner.c >= 0 && corner.c < s.Rest.Length
                            && Finite(corner.bary) && corner.bary.x >= 0f && corner.bary.y >= 0f && corner.bary.z >= 0f && Math.Abs(corner.bary.x + corner.bary.y + corner.bary.z - 1f) <= .00001f,
                            "Invalid serialized anatomical fragment corner.");
                }
                s.Regions=s.Binding.regions.Select(r=>new FitWorkspace(r.corners.Length)).ToArray();
                // FBX stores unused fingers/ornament bones in this renderer's
                // palette. Their secondary motion does not deform the anatomy.
                // Track all ancestors of every positive influence, but no unrelated chain.
                foreach(var bone in s.UsedBones.Select(i => s.Bones[i]))
                    for(Transform t=bone;t!=_representation;t=t.parent)
                    { Need(t!=null,"Anatomical bone escaped representation."); tracked.Add(t); }
            }
            _tracked=tracked.ToArray(); _positions=new Vector3[_tracked.Length]; _rotations=new Quaternion[_tracked.Length]; _scales=new Vector3[_tracked.Length];
            _parents = _tracked.Select(t => t.parent).ToArray();
            Measurements=_sources.Select(s=>new Snapshot { renderer=s.Binding.renderer.name,regions=new Fit[s.Binding.regions.Length] }).ToArray();
            _cloth=_representation.GetComponentsInChildren<Cloth>(true);
            _ready=true; _hasPose=false;
        }
        public bool RefreshNow()
        {
            long start=Stopwatch.GetTimestamp(), allocated=GC.GetAllocatedBytesForCurrentThread();
            try
            {
                if(!_ready) { Initialize(); _runtimeBindings = null; }
                if (_runtimeBoneAttached && _runtimeBindings != null) { FollowRuntimeBones(); LastError = null; return true; }
                Vector3 rootScale = _representation.lossyScale;
                Need(Finite(rootScale) && rootScale.x > 0f && Math.Abs(rootScale.x - rootScale.y) <= .00001f && Math.Abs(rootScale.x - rootScale.z) <= .00001f,
                    "Positive uniform representation scale required for metric capsules.");
                bool changed=!_hasPose;
                for(int i=0;i<_tracked.Length;i++)
                {
                    Transform t=_tracked[i]; Need(t != null && t.parent == _parents[i], "Anatomical hierarchy changed; rebuild bindings.");
                    Vector3 p=t.localPosition,s=t.localScale; Quaternion q=t.localRotation;
                    changed|=!p.Equals(_positions[i])||!q.Equals(_rotations[i])||!s.Equals(_scales[i]);
                    _positions[i]=p;_rotations[i]=q;_scales[i]=s;
                }
                foreach (var source in _sources) Need(source.Binding.renderer.sharedMesh == source.Mesh, "Anatomical source changed; rebuild bindings.");
                if(!changed){ PoseCacheHits++; LastError = null; return true; }
                for(int i=0;i<_sources.Length;i++)
                {
                    Source s=_sources[i]; Need(s.Binding.renderer.sharedMesh==s.Mesh,"Anatomical source changed; rebuild bindings."); Skin(s,_representation);
                    for(int j=0;j<s.Binding.regions.Length;j++)
                    {
                        Region r=s.Binding.regions[j]; FillPoints(s,r,s.Regions[j]); Fit fit=MeasureFit(s.Regions[j]);
                        fit.maximumRawCornerOutside = MaximumCornerOutside(s.Current, r.coverageCorners, fit); fit.rawCorners = r.coverageCorners.Length;
                        Need(fit.maximumOutside<=.000002f && fit.maximumRawCornerOutside<=.000002f,"Original anatomical fragment escaped fitted candidate.");
                        Vector3 axis=fit.end-fit.start; var c=r.capsule;
                        c.transform.localPosition=(fit.start+fit.end)*.5f;
                        c.transform.localRotation=axis.sqrMagnitude>1e-14f?Quaternion.FromToRotation(Vector3.up,axis):Quaternion.identity;
                        c.transform.localScale=Vector3.one; c.direction=1;c.center=Vector3.zero;c.radius=fit.radius;c.height=axis.magnitude+2f*fit.radius;
                        Measurements[i].regions[j]=fit;
                    }
                }
                _hasPose=true;FitCount++;LastError=null;
                if (_runtimeBoneAttached) BuildRuntimeBoneBindings();
                return true;
            }
            catch(Exception e){_hasPose=false;LastError=e.GetBaseException().Message;return false;}
            finally{LastRefreshMilliseconds=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;LastRefreshAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;}
        }

        [Serializable] public sealed class BakedSourceAudit
        {
            public string scope = "Actual native BakeMesh versus complete source-weight skin; original triangle fragments versus fitted convex capsules. This is geometry/CPU evidence, not cloth, outward-overhang or RIG_PASS approval.";
            public bool geometryEquivalent;
            public string error;
            public int sourceVertices, uniqueSourceVertices, uniqueFitCorners, originalFragmentCorners, checkedFragments, cachedCalls, forcedFitCalls;
            public int usedSkinBones, trackedHierarchyTransforms;
            public float maximumSkinErrorMeters, maximumBakedFragmentOutsideMeters, maximumGridEndpointDifferenceMeters, maximumGridRadiusDifferenceMeters;
            public float maximumAppliedCapsuleEndpointDifferenceMeters, maximumAppliedCapsuleRadiusDifferenceMeters;
            public double maximumPosedAreaDifferenceSquareMeters, cachedMeanMilliseconds, forcedFitMeanMilliseconds;
            public long cachedAllocatedBytes, forcedFitAllocatedBytes;
            public SkinQuality[] rendererSkinQuality;
            public SkinWeights globalSkinWeights;
        }
        /// <summary>Diagnostic-only, deliberately allocates its report/native meshes outside timing windows.
        /// Run after final bone poses; does not alter poses, quality settings, animation, or candidate membership.</summary>
        public BakedSourceAudit AuditAgainstBakedSources(int repeatRefreshCalls = 32)
        {
            var result = new BakedSourceAudit();
            try
            {
                Need(RefreshNow(), LastError); result.globalSkinWeights = QualitySettings.skinWeights;
                result.usedSkinBones = _sources.SelectMany(s => s.UsedBones.Select(i => s.Bones[i])).Distinct().Count();
                result.trackedHierarchyTransforms = _tracked.Length;
                result.rendererSkinQuality = _sources.Select(s => s.Binding.renderer.quality).ToArray();
                foreach (var source in _sources)
                {
                    Mesh baked = new Mesh { name = "BodyProxyAudit_TemporaryBake" };
                    try
                    {
                        source.Binding.renderer.BakeMesh(baked, false);
                        Vector3[] actual = baked.vertices;
                        Need(actual.Length == source.Current.Length, "Native anatomy bake changed vertex indexing.");
                        Matrix4x4 toRepresentation = _representation.worldToLocalMatrix * source.Binding.renderer.localToWorldMatrix;
                        for (int i = 0; i < actual.Length; i++)
                        {
                            actual[i] = toRepresentation.MultiplyPoint3x4(actual[i]);
                            Need(Finite(actual[i]), "Nonfinite native anatomical bake.");
                            result.maximumSkinErrorMeters = Math.Max(result.maximumSkinErrorMeters, (actual[i] - source.Current[i]).magnitude);
                            if (source.Representatives[i] == i) result.uniqueSourceVertices++;
                        }
                        result.sourceVertices += actual.Length;
                        double originalArea = 0d, fragmentsArea = 0d; int[] triangles = source.Mesh.triangles;
                        for (int i = 0; i < triangles.Length; i += 3) originalArea += TriangleArea(actual[triangles[i]], actual[triangles[i + 1]], actual[triangles[i + 2]]);
                        for (int j = 0; j < source.Binding.regions.Length; j++)
                        {
                            Region region = source.Binding.regions[j]; FitWorkspace normal = source.Regions[j], reference = new FitWorkspace(normal.Points.Length);
                            Array.Copy(normal.Points, reference.Points, normal.Points.Length); reference.PreviousAxis = normal.PreviousAxis;
                            Fit optimized = MeasureFit(normal), full = MeasureFit(reference, true);
                            result.maximumGridEndpointDifferenceMeters = Math.Max(result.maximumGridEndpointDifferenceMeters, Math.Max((optimized.start - full.start).magnitude, (optimized.end - full.end).magnitude));
                            result.maximumGridRadiusDifferenceMeters = Math.Max(result.maximumGridRadiusDifferenceMeters, Math.Abs(optimized.radius - full.radius));
                            CapsuleCollider capsule = region.capsule;
                            Need(capsule.direction == 1 && capsule.transform.parent == _representation && capsule.transform.localScale.Equals(Vector3.one), "Fitted capsule ownership/metric transform changed.");
                            Quaternion rotation = capsule.transform.localRotation;
                            Vector3 center = capsule.transform.localPosition + rotation * capsule.center, axis = rotation * Vector3.up;
                            float half = Math.Max(0f, capsule.height * .5f - capsule.radius);
                            Fit applied = new Fit { start = center - axis * half, end = center + axis * half, radius = capsule.radius };
                            result.maximumAppliedCapsuleEndpointDifferenceMeters = Math.Max(result.maximumAppliedCapsuleEndpointDifferenceMeters,
                                Math.Max((applied.start - optimized.start).magnitude, (applied.end - optimized.end).magnitude));
                            result.maximumAppliedCapsuleRadiusDifferenceMeters = Math.Max(result.maximumAppliedCapsuleRadiusDifferenceMeters, Math.Abs(applied.radius - optimized.radius));
                            result.maximumBakedFragmentOutsideMeters = Math.Max(result.maximumBakedFragmentOutsideMeters, MaximumCornerOutside(actual, region.coverageCorners, applied));
                            result.uniqueFitCorners += region.corners.Length; result.originalFragmentCorners += region.coverageCorners.Length;
                            int offset = 0;
                            foreach (int size in region.polygonSizes)
                            {
                                for (int i = 1; i + 1 < size; i++) fragmentsArea += TriangleArea(Point(actual, region.coverageCorners[offset]), Point(actual, region.coverageCorners[offset + i]), Point(actual, region.coverageCorners[offset + i + 1]));
                                offset += size; result.checkedFragments++;
                            }
                        }
                        result.maximumPosedAreaDifferenceSquareMeters = Math.Max(result.maximumPosedAreaDifferenceSquareMeters, Math.Abs(originalArea - fragmentsArea));
                    }
                    finally { if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked); }
                }
                // Warm both exact-pose and forced actual skin/fit paths before recording.
                Need(RefreshWithForcedFit(), LastError); Need(RefreshNow(), LastError);
                result.cachedCalls = Mathf.Clamp(repeatRefreshCalls, 1, 1024); result.forcedFitCalls = Mathf.Clamp(result.cachedCalls / 4, 1, 32);
                long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < result.cachedCalls; i++) Need(RefreshNow(), LastError);
                result.cachedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                result.cachedMeanMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency / result.cachedCalls;
                allocated = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
                for (int i = 0; i < result.forcedFitCalls; i++) Need(RefreshWithForcedFit(), LastError);
                result.forcedFitAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                result.forcedFitMeanMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency / result.forcedFitCalls;
                result.geometryEquivalent = result.maximumSkinErrorMeters <= .00001f && result.maximumBakedFragmentOutsideMeters <= .00001f
                    && result.maximumGridEndpointDifferenceMeters <= .000002f && result.maximumGridRadiusDifferenceMeters <= .000002f
                    && result.maximumAppliedCapsuleEndpointDifferenceMeters <= .000002f && result.maximumAppliedCapsuleRadiusDifferenceMeters <= .000002f
                    && result.maximumPosedAreaDifferenceSquareMeters <= .000001d;
            }
            catch (Exception e) { result.error = e.GetBaseException().Message; result.geometryEquivalent = false; }
            return result;
        }
        public bool RefreshWithForcedFit() { _hasPose = false; return RefreshNow(); }

        public static Fit MeasureFit(FitWorkspace scratch, bool fullGridReference = false)
        {
            Vector3[] points=scratch.Points; double mx=0,my=0,mz=0;
            foreach(Vector3 p in points){ Need(Finite(p), "Nonfinite fit corner."); mx+=p.x;my+=p.y;mz+=p.z; }
            mx/=points.Length;my/=points.Length;mz/=points.Length;
            Vector3 mean=new Vector3((float)mx,(float)my,(float)mz);
            double xx=0,xy=0,xz=0,yy=0,yz=0,zz=0;
            foreach(Vector3 point in points)
            { double x=point.x-mx,y=point.y-my,z=point.z-mz;xx+=x*x;xy+=x*y;xz+=x*z;yy+=y*y;yz+=y*z;zz+=z*z; }
            // Symmetric Jacobi eigen decomposition avoids a power-iteration seed
            // orthogonal to the dominant axis in a folded or nearly spherical region.
            Vector3 axis=PrincipalAxis(xx,xy,xz,yy,yz,zz,out float eigenGap);
            if(Vector3.Dot(axis,scratch.PreviousAxis)<0f)axis=-axis;
            float axisStep=scratch.HasAxis?(float)(Math.Acos(Math.Max(-1d,Math.Min(1d,Vector3.Dot(axis,scratch.PreviousAxis))))*180d/Math.PI):0f;
            scratch.PreviousAxis=axis;scratch.HasAxis=true;
            Fit best=FitCenter(scratch,mean,axis,fullGridReference);
            for(int i=0;i<points.Length;i++)
            { Vector3 d=points[i]-mean;scratch.RadialOffsets[i]=d-axis*Vector3.Dot(d,axis); }
            // Preserve operation/tie order but avoid 160*N Unity Vector3 operator
            // and property calls in the Editor's managed execution path.
            float shiftX=0f,shiftY=0f,shiftZ=0f; Vector3[] radialOffsets=scratch.RadialOffsets;
            for(int step=0;step<160;step++)
            {
                float farX=0f,farY=0f,farZ=0f,distance=-1f;
                for(int i=0;i<radialOffsets.Length;i++)
                {
                    Vector3 p=radialOffsets[i];float x=p.x-shiftX,y=p.y-shiftY,z=p.z-shiftZ;
                    float squared=x*x+y*y+z*z;
                    if(squared>distance){distance=squared;farX=x;farY=y;farZ=z;}
                }
                float divisor=step+2f;shiftX+=farX/divisor;shiftY+=farY/divisor;shiftZ+=farZ/divisor;
            }
            Fit adjusted=FitCenter(scratch,new Vector3(mean.x+shiftX,mean.y+shiftY,mean.z+shiftZ),axis,fullGridReference);if(adjusted.volume<best.volume)best=adjusted;
            float outside=0f;foreach(Vector3 point in points) outside=Math.Max(outside,SignedDistance(point,best));
            best.maximumOutside=outside;best.corners=points.Length;best.axisStepDegrees=axisStep;best.relativeEigenGap=eigenGap;
            Need(Finite(best.start)&&Finite(best.end)&&Finite(best.radius)&&best.radius>0f&&Finite(best.volume),"Nonfinite anatomical fit.");return best;
        }
        private static Fit FitCenter(FitWorkspace s,Vector3 center,Vector3 axis,bool fullGridReference)
        {
            float low=float.PositiveInfinity,high=float.NegativeInfinity,baseSquared=0f;
            Vector3[] points=s.Points;float[] projection=s.Projection,radialSquared=s.RadialSquared,leftMaximum=s.LeftMaximum,rightMaximum=s.RightMaximum;
            for(int i=0;i<points.Length;i++)
            {
                Vector3 p=points[i];float x=p.x-center.x,y=p.y-center.y,z=p.z-center.z;
                float t=x*axis.x+y*axis.y+z*axis.z,r=x*x+y*y+z*z-t*t;if(r<0f)r=0f;
                projection[i]=t;radialSquared[i]=r;if(t<low)low=t;if(t>high)high=t;if(r>baseSquared)baseSquared=r;
            }
            float inset=Mathf.Min((high-low)*.5f,Mathf.Sqrt(baseSquared)*1.5f);
            float[] leftPosition=s.LeftPosition,rightPosition=s.RightPosition,leftRadius=s.LeftRadius,rightRadius=s.RightRadius;
            for(int k=0;k<19;k++){leftPosition[k]=low+inset*k/18f;rightPosition[k]=high-inset*k/18f;}
            if(!fullGridReference)
            {
                // max_i(radial_i²+max(left_i,right_i,0)²) separates exactly into
                // max(max_i(left term),max_i(right term)). All 361 candidates and
                // their strict tie order remain unchanged; scan 38*N instead of 361*N.
                for(int k=0;k<19;k++)
                {
                    float a=leftPosition[k],b=rightPosition[k],left=0f,right=0f;
                    for(int i=0;i<points.Length;i++)
                    {
                        float dl=a-projection[i],dr=projection[i]-b,radial=radialSquared[i];
                        if(dl<0f)dl=0f;if(dr<0f)dr=0f;
                        float leftSquared=radial+dl*dl,rightSquared=radial+dr*dr;
                        if(leftSquared>left)left=leftSquared;if(rightSquared>right)right=rightSquared;
                    }
                    leftMaximum[k]=left;rightMaximum[k]=right;
                    // Every candidate radius is one of these 38 values. Select by
                    // squared distance exactly as before; no approximate square root.
                    leftRadius[k]=Mathf.Sqrt(left)+.000001f;rightRadius[k]=Mathf.Sqrt(right)+.000001f;
                }
            }
            float bestVolume=float.PositiveInfinity,bestStart=0f,bestEnd=0f,bestRadius=0f;
            for(int l=0;l<19;l++)for(int r=0;r<19;r++)
            {
                float a=leftPosition[l],b=rightPosition[r],radiusSquared=0f,radius;
                if(fullGridReference)
                {
                    for(int i=0;i<points.Length;i++)
                    {float d=Mathf.Max(0f,Mathf.Max(a-projection[i],projection[i]-b));radiusSquared=Mathf.Max(radiusSquared,radialSquared[i]+d*d);}
                    radius=Mathf.Sqrt(radiusSquared)+.000001f;
                }
                else radius=leftMaximum[l]>rightMaximum[r]?leftRadius[l]:rightRadius[r];
                float volume=Mathf.PI*radius*radius*(Mathf.Max(0f,b-a)+4f*radius/3f);
                if(volume<bestVolume){bestStart=a;bestEnd=b;bestRadius=radius;bestVolume=volume;}
            }
            return new Fit{start=center+axis*bestStart,end=center+axis*bestEnd,radius=bestRadius,volume=bestVolume};
        }
        private static Vector3 PrincipalAxis(double xx,double xy,double xz,double yy,double yz,double zz,out float relativeGap)
        {
            // Double/scalar Jacobi: no Unity native matrix calls or heap allocation.
            Span<double> a=stackalloc double[9],v=stackalloc double[9];a.Clear();v.Clear();
            a[0]=xx;a[1]=a[3]=xy;a[2]=a[6]=xz;a[4]=yy;a[5]=a[7]=yz;a[8]=zz;v[0]=v[4]=v[8]=1d;
            for(int step=0;step<24;step++)
            {
                int p=0,q=1;double largest=Math.Abs(a[1]);
                if(Math.Abs(a[2])>largest){p=0;q=2;largest=Math.Abs(a[2]);}
                if(Math.Abs(a[5])>largest){p=1;q=2;largest=Math.Abs(a[5]);}
                if(largest<1e-12d)break;
                double app=a[p*3+p],aqq=a[q*3+q],apq=a[p*3+q];
                double angle=.5d*Math.Atan2(2d*apq,aqq-app),c=Math.Cos(angle),s=Math.Sin(angle);
                for(int k=0;k<3;k++)if(k!=p&&k!=q)
                {
                    double akp=a[k*3+p],akq=a[k*3+q];
                    a[k*3+p]=a[p*3+k]=c*akp-s*akq;a[k*3+q]=a[q*3+k]=s*akp+c*akq;
                }
                a[p*3+p]=c*c*app-2d*c*s*apq+s*s*aqq;
                a[q*3+q]=s*s*app+2d*c*s*apq+c*c*aqq;a[p*3+q]=a[q*3+p]=0d;
                for(int k=0;k<3;k++)
                {double vkp=v[k*3+p],vkq=v[k*3+q];v[k*3+p]=c*vkp-s*vkq;v[k*3+q]=s*vkp+c*vkq;}
            }
            int index=a[4]>a[0]?1:0;if(a[8]>a[index*3+index])index=2;
            double second=double.NegativeInfinity;for(int k=0;k<3;k++)if(k!=index)second=Math.Max(second,a[k*3+k]);
            relativeGap=(float)((a[index*3+index]-second)/Math.Max(a[index*3+index],1e-30d));
            return new Vector3((float)v[index],(float)v[3+index],(float)v[6+index]).normalized;
        }
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        private static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
