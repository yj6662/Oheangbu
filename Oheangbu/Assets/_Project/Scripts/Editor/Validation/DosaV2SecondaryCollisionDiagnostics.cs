using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Oheangbu.Data;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Disposable pre-gate secondary motion measurements. No production clips, gameplay physics or promotion.</summary>
    public static class DosaV2SecondaryCollisionDiagnostics
    {
        private static Session _active;
        private static JObject _last;
        private struct PoseStage { }
        private struct MeasureStage { }
        public static bool IsRunning => _active != null;
        public static string Run() => Begin();
        public static string Begin() => BeginWithAdditionalBodyCapsules(Array.Empty<string>());
        public static string BeginWithReviewedHeadNeckCandidates() =>
            BeginWithAdditionalBodyCapsules(DosaV2HeadNeckCollisionBuilder.ReviewedNames);
        public static string BeginRigidAngleProposals() =>
            BeginInternal(DosaV2HeadNeckCollisionBuilder.ReviewedNames, new[] { 25f, 35f, 45f }, true);
        public static string BeginWithReviewedBodyCandidates() => BeginWithAdditionalBodyCapsules(
            DosaV2HeadNeckCollisionBuilder.ReviewedNames.Concat(DosaV2ShoulderCollisionBuilder.ReviewedNames).ToArray());
        // Future explicitly authored head/neck capsules must be named by the caller;
        // an empty list keeps the exact existing base-27 scope, not a loosened count.
        public static string BeginWithAdditionalBodyCapsules(string[] exactAdditionalCapsuleNames)
            => BeginInternal(exactAdditionalCapsuleNames, Array.Empty<float>(), false);
        private static string BeginInternal(string[] exactAdditionalCapsuleNames, float[] proposalAngles, bool directOnly)
        {
            if (_active != null) return Status();
            if (!Application.isPlaying || EditorApplication.isPaused || SceneManager.GetActiveScene().name != "C2_PlayerV2Validation")
                return "WAIT: unpaused Play Mode in C2_PlayerV2Validation is required.";
            foreach (string name in new[] { "DosaV2ClothDiagnostics", "DosaV2ClothClockProbe", "DosaV2DrawingStaticValidation", "DosaV2LodValidation", "DosaV2ClothCapacityProbe" })
            {
                var type = typeof(DosaV2SecondaryCollisionDiagnostics).Assembly.GetType("Oheangbu.EditorTools." + name);
                if (type?.GetField("_active", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) != null)
                    return "WAIT: stop the independent " + name + " fixture first.";
            }
            if (exactAdditionalCapsuleNames == null || exactAdditionalCapsuleNames.Any(string.IsNullOrWhiteSpace)
                || exactAdditionalCapsuleNames.Distinct(StringComparer.Ordinal).Count() != exactAdditionalCapsuleNames.Length)
                return "WAIT: additional body capsules require an explicit unique nonempty name list.";
            var session = new Session(exactAdditionalCapsuleNames, proposalAngles, directOnly); _active = session;
            try { session.Start(); } catch (Exception e) { session.Finish("FAIL", e.ToString()); }
            return Status();
        }
        public static string Status()
        {
            JObject r = _active?.Report ?? _last;
            return r == null ? "NOT_STARTED" : new JObject { ["status"] = r["status"], ["error"] = r["error"], ["directory"] = r["directory"],
                ["samples"] = ((JArray)r["samples"]).Count, ["maximumTriangleProxyPenetrationMeters"] = r["maximumTriangleProxyPenetrationMeters"],
                ["collisionRuntimeEvidence"] = r["collisionRuntimeEvidence"], ["collisionRuntimeInvalidSamples"] = r["collisionRuntimeInvalidSamples"],
                ["invariantFailures"] = r["invariantFailures"], ["restored"] = r["restored"] }.ToString();
        }
        public static string Stop() { _active?.Finish("STOPPED", "Stopped before all diagnostic conditions completed."); return Status(); }
        private sealed class Geometry
        {
            public Renderer Renderer; public Mesh Mesh; public Vector3[] World;
            public int[] Triangles; public Transform[] Bones; public byte[] Counts; public BoneWeight1[] Weights;
        }
        private sealed class Surface
        {
            public Geometry Geometry; public int[] Vertices, Triangles; public Vector3[] RigidRest;
            public int MixedInfluences, MixedTriangles;
        }
        private sealed class Part
        {
            public string Name; public bool Rigid; public Transform[] Bones; public Vector3 Tip;
            public Vector3[] Positions, Scales; public Quaternion[] Rotations; public float[] Lengths;
            public float ResetRotationBeforeEvaluate;
            public readonly List<Surface> Surfaces = new List<Surface>();
        }
        private sealed class Step
        {
            public JObject Pose; public string Kind; public int LocalFrame; public float Dt;
            public bool First, Reset, Capture;
        }
        private sealed class Session
        {
            public readonly JObject Report = new JObject { ["status"] = "WAIT", ["rigGate"] = "NOT_GRANTED", ["restored"] = false,
                ["samples"] = new JArray(), ["inventory"] = new JArray(), ["sources"] = new JArray(), ["images"] = new JArray(),
                ["maximumTriangleProxyPenetrationMeters"] = 0f, ["invariantFailures"] = 0,
                ["collisionRuntimeEvidence"] = "NOT_MEASURED", ["collisionRuntimeInvalidSamples"] = 0,
                ["collisionRuntimeActiveSamples"] = 0, ["collisionRuntimeSamplesWithUnresolvedParts"] = 0,
                ["scope"] = "Actual 20 world rigid ornaments and five three-bone chains from the serialized review prefab. Every retained source triangle is tested against the complete base body capsules plus every explicitly named additional candidate, not just source points or the native Cloth-selected subset. Native Cloth is disabled; its rig bindings are omitted only in this disposable spring fixture. No production animation, input, game collision, near-arm contact, full anatomical-body approval or RIG_PASS.",
                ["limitations"] = "Positive depth means ornament/cord surface lies inside a body proxy; capsule overhang can produce an overlap without actual skin penetration. Exact included body names are listed in bodyCapsuleGeometry and explicitAdditionalBodyCapsules. Anatomical coverage is measured separately, including added shoulder surfaces; proxy presence alone is not a coverage approval. Triangle-surface tests do not classify fully enclosed closed solid volumes, ornament-to-ornament collisions or the visual quality of skin deformation. Zero sampled-frame intersections are not a continuous-time guarantee.",
                ["timing"] = "Explicit secondary.Evaluate integration dt=1/60, profile substeps unchanged. Direct static poses use dt=0. Anchor impulse is a 24-step 6.5cm lateral/3cm fore-aft and 12-degree yaw excursion, below teleport limits; 90 steps per impulse case then reset. Unity/global clocks are untouched. This is a spring diagnostic, not a GPU FPS benchmark.",
                ["measurement"] = "Point depth and complete triangle-to-segment minimum-distance depth are separate. Triangle capsule depth=radius-minDistance(axisSegment,filledTriangle), covering triangle interiors and finite endcaps. Positive and >10um counts are recorded; 10um is an instrumentation reporting guard, not an approved ornament penetration allowance.",
                ["collisionRuntimeScope"] = "Independent original LOD0 BakeMesh/full-triangle measurements remain unchanged. New runtime evidence separately records binding identity, enabled/configured/FrameReady/LastError, actual frame-counter advance, complete body-proxy identity, per-part residuals and measured CPU/GC. A missing or failed runtime binding can still yield useful independent baseline measurements but cannot establish collision-runtime execution or approval. Runtime geometry can cover additional authored LODs; its margins/residuals do not replace the independent surface result.",
                ["collisionCpuScope"] = "Runtime LastFrameMainThreadMilliseconds includes body refresh, intervening spring/projection and EndFrame queries. LastFrameAllocatedBytes is captured by the runtime over that same scope, before this diagnostic builds JSON/BakeMesh evidence. First initialization may allocate and is explicitly identified. Existing springCpuMilliseconds includes Evaluate; post-Evaluate bodyRefreshCpuMilliseconds may be a cache hit. No GPU or production-build performance claim.",
                ["resetMeasurementScope"] = "The unchanged 0.01-degree reset invariant is sampled immediately after ResetMotion, before Evaluate(0). Collision may then legitimately rotate an overlapping rest pose; that resulting rest-relative angle is separately recorded. Position/scale/rigid-shape/chain-length limits are unchanged. Default mode retains295 conditions; explicitly marked directOnlyInvestigation omits impulse/reset stages for a finite-angle study." };
            private Scene _scene;
            private GameObject _fixture;
            private Transform _world;
            private PlayerSecondaryMotionRig _secondary;
            private PlayerClothBodyProxyRig _body;
            private PlayerSecondaryCollisionRig _collision;
            private readonly string[] _additionalCapsuleNames;
            private string _collisionBindingHash;
            private bool _collisionBindingsComplete, _collisionWasConfigured, _collisionWasEnabled;
            private int _collisionFramesBefore;
            private double _collisionCpuTotal, _collisionCpuMaximum;
            private long _collisionMaximumAllocatedBytes;
            private int _collisionWarmSamples;
            private double _collisionWarmCpuTotal, _collisionWarmCpuMaximum;
            private long _collisionWarmMaximumAllocatedBytes;
            private CapsuleCollider[] _capsules;
            private PlayerClothCollisionBudgetRig.CapsuleGeometry[] _caps;
            private readonly List<Geometry> _geometry = new List<Geometry>();
            private readonly List<Part> _parts = new List<Part>();
            private readonly List<Step> _steps = new List<Step>();
            private Dictionary<string, Matrix4x4> _restMatrices;
            private KeyValuePair<string, Transform>[] _bones;
            private Dictionary<string, Quaternion> _restRotations;
            private PlayerHandGripCorrectives _correctives;
            private Mesh _baked;
            private Camera _camera;
            private RenderTexture _target;
            private Vector3 _origin; private Quaternion _orientation;
            private int _index, _posedFrame = -1, _measuredFrame = -1;
            private bool _finished; private double _started;
            private double _springCpu, _bodyCpu;
            private string _directory;

            private readonly float[] _proposalAngles;
            private readonly bool _directOnly;
            public Session(string[] additionalCapsuleNames, float[] proposalAngles, bool directOnly)
            {
                _additionalCapsuleNames = (string[])additionalCapsuleNames.Clone();
                _proposalAngles = (float[])proposalAngles.Clone(); _directOnly = directOnly;
            }

            public void Start()
            {
                _started = EditorApplication.timeSinceStartup;
                _directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2", "secondary-collision-" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")));
                Directory.CreateDirectory(_directory); Report["directory"] = _directory; Report["startedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
                Report["originalScene"] = SceneManager.GetActiveScene().path;
                string allocationControl = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/PlayerV2/Validation/allocation-counter-positive-control.json"));
                if (File.Exists(allocationControl))
                { Report["managedAllocationCounterControl"] = JObject.Parse(File.ReadAllText(allocationControl)); AddSource(allocationControl); }
                string posePath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/PlayerV2/Validation/static-pose-definitions.json"));
                JObject definitions = JObject.Parse(File.ReadAllText(posePath)); AddSource(posePath);
                Need((string)definitions["mode"] == "DIRECT_BONE_POSES" && (int)definitions["productionActions"] == 0, "Direct static pose definitions with zero production actions required.");
                _scene = SceneManager.CreateScene("DosaV2SecondaryCollision_" + Guid.NewGuid().ToString("N"));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.ValidationPrefab); Need(prefab != null, "Missing review prefab.");
                AddSource(ProjectFile(DosaV2PlayerBuilder.ValidationPrefab)); AddSource(ProjectFile(DosaV2PlayerBuilder.WorldModel)); AddSource(ProjectFile(DosaV2PlayerBuilder.WorldModel) + ".meta");
                _fixture = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(_fixture, _scene); _fixture.name = "DisposableSecondaryCollisionFixture";
                _fixture.transform.position = new Vector3(40f, 0f, 40f); _origin = _fixture.transform.position; _orientation = _fixture.transform.rotation;
                foreach (var cloth in _fixture.GetComponentsInChildren<Cloth>(true)) cloth.enabled = false;
                foreach (var lod in _fixture.GetComponentsInChildren<PlayerLodClothController>(true)) lod.SuspendForStaticFixture();
                _body = _fixture.GetComponentInChildren<PlayerClothBodyProxyRig>(true); Need(_body != null, "Serialized current body fitting component missing.");
                _world = _body.transform; _secondary = _world.GetComponent<PlayerSecondaryMotionRig>(); Need(_secondary != null, "Serialized world secondary rig missing.");
                var ornaments = Field<RigidOrnamentBinding[]>(_secondary, "_ornaments"); var chains = Field<SecondaryBoneChainBinding[]>(_secondary, "_chains");
                var profile = Field<PlayerSecondaryMotionProfileSO>(_secondary, "_profile");
                Need(DosaV2SecondaryBuilder.IsCompleteRigidLayout(_world.gameObject, ornaments, out string layoutError), layoutError);
                Need(chains?.Length == 5 && chains.All(c => c.Bones?.Length == 3), "Expected complete current world binding with five three-bone chains.");
                Report["rigidParts"] = ornaments.Length; Report["worldChains"] = chains.Length; Report["secondaryTransforms"] = ornaments.Length + chains.Sum(c => c.Bones.Length);
                Report["springProfile"] = JObject.Parse(JsonUtility.ToJson(profile)); AddSource(ProjectFile(AssetDatabase.GetAssetPath(profile)));
                foreach (var component in _fixture.GetComponentsInChildren<MonoBehaviour>(true)) if (component != _secondary) component.enabled = false;
                foreach (var camera in _fixture.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                foreach (var animator in _fixture.GetComponentsInChildren<Animator>(true))
                { Need(animator.runtimeAnimatorController == null, "Production controllers are prohibited in this fixture."); animator.enabled = false; }
                // Read binding records only. Configure a disposable copy with the exact spring settings,
                // excluding Cloth initialization/solver state; original serialized assets are never changed.
                Need(_secondary.Configure(profile, _world, ornaments, chains, Array.Empty<PlayerClothBinding>()), _secondary.LastBindingError);
                _secondary.enabled = true; _secondary.SetRepresentationActive(true);
                _collision = _secondary.SecondaryCollision;
                // This component has no autonomous Update/LateUpdate. Keep it
                // enabled to exercise the normal serialized runtime owner path.
                if (_collision != null) _collision.enabled = true;
                foreach (var cloth in _fixture.GetComponentsInChildren<Cloth>(true)) cloth.enabled = false;
                var expectedCapsules = new HashSet<string>(ExpectedBaseCapsuleNames().Concat(_additionalCapsuleNames), StringComparer.Ordinal);
                _capsules = _world.GetComponentsInChildren<CapsuleCollider>(true).Where(c => expectedCapsules.Contains(c.name)).OrderBy(c => c.name, StringComparer.Ordinal).ToArray();
                Need(ValidateCapsuleNames(_capsules.Select(c => c.name).ToArray(), _additionalCapsuleNames, out string capsuleError), capsuleError);
                Need(_capsules.All(c => PlayerSecondaryMotionRig.ValidateProxy(c, out _)), "Every exact named body capsule must retain the representation-only collision contract.");
                Report["expectedBaseBodyCapsules"] = new JArray(ExpectedBaseCapsuleNames());
                Report["explicitAdditionalBodyCapsules"] = new JArray(_additionalCapsuleNames);
                Report["headNeckCoverage"] = _additionalCapsuleNames.Length == 0 ? "NOT_INCLUDED_BASE27_ONLY" : "EXPLICIT_NAMED_PROXIES_INCLUDED_ANATOMICAL_COVERAGE_NOT_CERTIFIED";
                _caps = new PlayerClothCollisionBudgetRig.CapsuleGeometry[_capsules.Length]; Report["bodyCapsules"] = new JArray(_capsules.Select(c => c.name));
                CaptureCollisionBinding(ornaments, chains);
                foreach (var renderer in _world.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null || AssetDatabase.GetAssetPath(mesh) != DosaV2PlayerBuilder.WorldModel) continue;
                    Need(mesh.isReadable, "Readable original geometry required: " + renderer.name);
                    var geometry = new Geometry { Renderer = renderer, Mesh = mesh, Triangles = mesh.triangles };
                    if (renderer is SkinnedMeshRenderer r)
                    {
                        r.updateWhenOffscreen = true; r.forceMatrixRecalculationPerRender = true; geometry.Bones = r.bones;
                        using (var counts = mesh.GetBonesPerVertex()) geometry.Counts = counts.ToArray();
                        using (var weights = mesh.GetAllBoneWeights()) geometry.Weights = weights.ToArray();
                        Need(geometry.Counts.Length == mesh.vertexCount, "Complete native source weights required.");
                    }
                    renderer.enabled = true; renderer.shadowCastingMode = ShadowCastingMode.On; renderer.gameObject.layer = 30; _geometry.Add(geometry);
                }
                Need(_geometry.Count > 0, "No unique LOD0 source geometry.");
                foreach (var binding in ornaments) AddPart(binding.Name, true, new[] { binding.Pivot }, Vector3.zero);
                foreach (var binding in chains) AddPart(binding.Name, false, binding.Bones, binding.LastBoneTipLocal);
                _geometry.RemoveAll(g => !_parts.Any(p => p.Surfaces.Any(s => s.Geometry == g))
                    && g.Renderer.name != "DosaV2_ShoulderLining_Left" && g.Renderer.name != "DosaV2_ShoulderLining_Right");
                _baked = new Mesh { name = "SecondaryCollisionActualBake_Temporary" }; BakeAll();
                foreach (var part in _parts) foreach (var surface in part.Surfaces)
                    if (part.Rigid) surface.RigidRest = surface.Vertices.Select(i => part.Bones[0].InverseTransformPoint(surface.Geometry.World[i])).ToArray();
                var poses = ((JArray)definitions["poses"]).OfType<JObject>().ToArray(); _restMatrices = Matrices(poses.Single(p => (string)p["id"] == "rest"));
                var boneMap = _world.GetComponentsInChildren<Transform>(true).Where(t => _restMatrices.ContainsKey(Normalize(t.name))).ToDictionary(t => Normalize(t.name));
                Need(boneMap.Count == _restMatrices.Count, "All directly authored pose bones require exact unique matches.");
                _bones = boneMap.OrderBy(p => Depth(p.Value)).ToArray(); _restRotations = boneMap.ToDictionary(p => p.Key, p => Quaternion.Inverse(_world.rotation) * p.Value.rotation);
                Report["directPoseBoneCount"] = _bones.Length;
                _correctives = new PlayerHandGripCorrectives(_world.GetComponentsInChildren<Renderer>(true));
                foreach (var pose in poses) _steps.Add(new Step { Pose = pose, Kind = "direct_static", First = true, Dt = 0f,
                    Capture = new[] { "rest", "grip_down", "combined_reach" }.Contains((string)pose["id"]) });
                foreach (string id in _directOnly ? Array.Empty<string>() : new[] { "rest", "grip_down", "combined_reach" })
                {
                    var pose = poses.Single(p => (string)p["id"] == id);
                    for (int i = 0; i < 90; i++) _steps.Add(new Step { Pose = pose, Kind = "anchor_impulse", First = i == 0, LocalFrame = i, Dt = 1f / 60f, Capture = i == 12 || i == 89 });
                    _steps.Add(new Step { Pose = pose, Kind = "reset", Reset = true, Dt = 0f, Capture = true });
                }
                Report["plannedSamples"] = _steps.Count; Report["directPoseCount"] = poses.Length;
                Report["proposalSwingLimitsDegrees"] = new JArray(_proposalAngles);
                Report["proposalScope"] = "Read-only finite pose investigations. Original limits remain separately measured; active settings, source geometry and production Constrain are unchanged.";
                Report["directOnlyInvestigation"] = _directOnly;
                var cameraObject = new GameObject("SecondaryCollisionEvidenceCamera"); cameraObject.transform.SetParent(_fixture.transform, false); _camera = cameraObject.AddComponent<Camera>();
                _camera.enabled = false; _camera.cullingMask = 1 << 30; _camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(_scene);
                _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = new Color(.19f, .21f, .24f); _camera.fieldOfView = 34f; _camera.nearClipPlane = .02f; _camera.farClipPlane = 12f; _camera.aspect = 16f / 9f;
                _target = new RenderTexture(1280, 720, 24); _target.Create();
                var light = new GameObject("SecondaryCollisionEvidenceLight").AddComponent<Light>(); light.transform.SetParent(_fixture.transform, false); light.type = LightType.Directional; light.intensity = 1.3f; light.transform.localRotation = Quaternion.Euler(40f, -35f, 0f);
                foreach (string file in new[] { "PlayerSecondaryMotionRig", "PlayerSecondaryMotionMath", "PlayerClothBodyProxyRig" }) AddSource(ProjectFile("Assets/_Project/Scripts/Presentation/" + file + ".cs"));
                foreach (string file in new[] { "PlayerSecondaryCollisionRig", "PlayerSecondaryCollisionGeometry" })
                {
                    string path = ProjectFile("Assets/_Project/Scripts/Presentation/" + file + ".cs");
                    if (File.Exists(path)) AddSource(path);
                }
                string builderPath = ProjectFile("Assets/_Project/Scripts/Editor/DosaV2SecondaryCollisionBuilder.cs");
                if (File.Exists(builderPath)) AddSource(builderPath);
                AddSource(ProjectFile("Assets/_Project/Scripts/Editor/Validation/DosaV2SecondaryCollisionDiagnostics.cs"));
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                Need(Insert(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate), new PlayerLoopSystem { type = typeof(PoseStage), updateDelegate = Pose }), "Pose anchor missing.");
                Need(Insert(ref loop, typeof(UnityEngine.PlayerLoop.PostLateUpdate.UpdateAllSkinnedMeshes), new PlayerLoopSystem { type = typeof(MeasureStage), updateDelegate = Measure }), "Final actual skin anchor missing.");
                PlayerLoop.SetPlayerLoop(loop); EditorApplication.playModeStateChanged += PlayChanged; AssemblyReloadEvents.beforeAssemblyReload += Reload;
                Report["status"] = "RUNNING"; Save();
            }

            private void AddPart(string name, bool rigid, Transform[] bones, Vector3 tip)
            {
                Need(bones != null && bones.Length > 0 && bones.All(b => b != null && b.IsChildOf(_world)), name + ": missing owned pivot/chain.");
                var part = new Part { Name = name, Rigid = rigid, Bones = bones, Tip = tip,
                    Positions = bones.Select(b => b.localPosition).ToArray(), Scales = bones.Select(b => b.localScale).ToArray(), Rotations = bones.Select(b => b.localRotation).ToArray(),
                    Lengths = Enumerable.Range(0, bones.Length).Select(i => i + 1 < bones.Length ? Vector3.Distance(bones[i].position, bones[i + 1].position) : bones[i].TransformVector(tip).magnitude).ToArray() };
                foreach (var geometry in _geometry)
                {
                    var selected = new HashSet<int>(); int mixed = 0;
                    if (geometry.Bones == null)
                    {
                        if (!rigid || geometry.Renderer.transform.parent != bones[0]) continue;
                        selected.UnionWith(Enumerable.Range(0, geometry.Mesh.vertexCount));
                    }
                    else
                    {
                        int offset = 0;
                        for (int v = 0; v < geometry.Counts.Length; v++)
                        {
                            float owned = 0, other = 0;
                            for (int j = 0; j < geometry.Counts[v]; j++)
                            {
                                Need(offset < geometry.Weights.Length, "Truncated source weight stream."); var weight = geometry.Weights[offset++];
                                Need(Finite(weight.weight) && weight.weight >= 0 && weight.boneIndex >= 0 && weight.boneIndex < geometry.Bones.Length && geometry.Bones[weight.boneIndex] != null, "Invalid source influence.");
                                if (bones.Contains(geometry.Bones[weight.boneIndex])) owned += weight.weight; else other += weight.weight;
                            }
                            if (owned <= 0) continue; selected.Add(v);
                            if (rigid && (Mathf.Abs(owned - 1f) > .0001f || other > .0001f)) mixed++;
                        }
                        Need(offset == geometry.Weights.Length, "Unexpected trailing source weights.");
                    }
                    if (selected.Count == 0) continue;
                    var triangles = new List<int>(); int mixedTriangles = 0;
                    for (int i = 0; i < geometry.Triangles.Length; i += 3)
                    {
                        int count = (selected.Contains(geometry.Triangles[i]) ? 1 : 0) + (selected.Contains(geometry.Triangles[i + 1]) ? 1 : 0) + (selected.Contains(geometry.Triangles[i + 2]) ? 1 : 0);
                        if (count == 0) continue; triangles.Add(i / 3); if (count != 3) mixedTriangles++;
                    }
                    part.Surfaces.Add(new Surface { Geometry = geometry, Vertices = selected.OrderBy(i => i).ToArray(), Triangles = triangles.ToArray(), MixedInfluences = mixed, MixedTriangles = mixedTriangles });
                }
                Need(part.Surfaces.Count > 0 && part.Surfaces.Sum(s => s.Triangles.Length) > 0, name + ": no actual source surface; part cannot be omitted.");
                _parts.Add(part); var sources = new JArray();
                foreach (var surface in part.Surfaces)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(surface.Geometry.Mesh, out string guid, out long localId);
                    sources.Add(new JObject { ["renderer"] = surface.Geometry.Renderer.name, ["meshGuid"] = guid, ["meshLocalId"] = localId,
                        ["sourceVertices"] = surface.Geometry.Mesh.vertexCount, ["retainedVertices"] = surface.Vertices.Length, ["retainedTriangles"] = surface.Triangles.Length,
                        ["mixedRigidInfluenceVertices"] = surface.MixedInfluences, ["mixedBoundaryTriangles"] = surface.MixedTriangles });
                }
                ((JArray)Report["inventory"]).Add(new JObject { ["name"] = name, ["rigid"] = rigid, ["bones"] = new JArray(bones.Select(b => b.name)), ["surfaces"] = sources });
            }

            private void CaptureCollisionBinding(RigidOrnamentBinding[] ornaments, SecondaryBoneChainBinding[] chains)
            {
                var binding = new JObject { ["present"] = _collision != null, ["complete"] = false,
                    ["parts"] = new JArray(), ["runtimeBodyCapsules"] = new JArray() };
                Report["collisionBinding"] = binding;
                if (_collision == null) { Report["collisionRuntimeEvidence"] = "NO_BINDING_BASELINE_ONLY"; return; }
                binding["owner"] = HierarchyPath(_collision.transform);
                binding["ownerMatchesWorld"] = _collision.Owns(_world); binding["enabled"] = _collision.enabled;
                binding["scope"] = _collision.CoverageScope;
                binding["runtimeBodyCapsules"] = new JArray((_collision.BodyCapsules ?? Array.Empty<CapsuleCollider>()).Select(c => c != null ? HierarchyPath(c.transform) : "<missing>"));
                var expected = new Dictionary<string, Transform[]>(StringComparer.Ordinal);
                foreach (var ornament in ornaments) expected.Add(ornament.Name, new[] { ornament.Pivot });
                foreach (var chain in chains) expected.Add(chain.Name, chain.Bones);
                var seen = new HashSet<string>(StringComparer.Ordinal); var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool complete = _collision.Owns(_world) && RuntimeCapsulesMatch();
                foreach (var part in _collision.AuthoredParts ?? Array.Empty<SecondaryCollisionPart>())
                {
                    if (part == null) { complete = false; ((JArray)binding["parts"]).Add(new JObject { ["missing"] = true }); continue; }
                    bool bonesMatch = !string.IsNullOrEmpty(part.Name) && expected.TryGetValue(part.Name, out Transform[] bones)
                        && part.DrivenBones != null && bones.SequenceEqual(part.DrivenBones);
                    complete &= bonesMatch && seen.Add(part.Name ?? "<missing>") && part.Surfaces != null && part.Surfaces.Length > 0;
                    var surfaces = new JArray();
                    foreach (var surface in part.Surfaces ?? Array.Empty<SecondaryCollisionSurface>())
                    {
                        if (surface == null) { complete = false; surfaces.Add(new JObject { ["missing"] = true }); continue; }
                        string asset = surface.SourceMesh != null ? AssetDatabase.GetAssetPath(surface.SourceMesh) : null;
                        bool provenance = surface.SourceRenderer != null && surface.SourceMesh != null && !string.IsNullOrEmpty(surface.SourceGuid)
                            && !string.IsNullOrEmpty(surface.SourceFileSha256) && !string.IsNullOrEmpty(surface.SourceGeometrySha256) && !string.IsNullOrEmpty(surface.GeometrySha256)
                            && surface.Positions != null && surface.Positions.Length > 0 && surface.Triangles != null && surface.Triangles.Length > 0
                            && surface.OriginalVertexIndices?.Length == surface.Positions.Length && surface.OriginalTriangleIndices?.Length * 3 == surface.Triangles.Length;
                        complete &= provenance;
                        surfaces.Add(new JObject { ["renderer"] = surface.SourceRenderer != null ? HierarchyPath(surface.SourceRenderer.transform) : "<missing>",
                            ["sourceAsset"] = asset, ["sourceGuid"] = surface.SourceGuid, ["sourceLocalId"] = surface.SourceLocalId,
                            ["sourceFileSha256"] = surface.SourceFileSha256, ["sourceGeometrySha256"] = surface.SourceGeometrySha256,
                            ["authoredGeometrySha256"] = surface.GeometrySha256, ["rigid"] = surface.Rigid,
                            ["vertices"] = surface.Positions?.Length ?? 0, ["triangles"] = (surface.Triangles?.Length ?? 0) / 3,
                            ["provenanceFieldsPresent"] = provenance });
                        if (!string.IsNullOrEmpty(asset) && sourceFiles.Add(asset)) AddSource(ProjectFile(asset));
                    }
                    ((JArray)binding["parts"]).Add(new JObject { ["name"] = part.Name, ["rigid"] = part.Rigid,
                        ["drivenBonesMatchOriginalSecondary"] = bonesMatch,
                        ["drivenBones"] = new JArray((part.DrivenBones ?? Array.Empty<Transform>()).Select(t => t != null ? HierarchyPath(t) : "<missing>")), ["surfaces"] = surfaces });
                }
                complete &= seen.Count == expected.Count && expected.Keys.All(seen.Contains);
                binding["complete"] = complete; binding["runtimeBodyCapsulesMatchIndependentExactSet"] = RuntimeCapsulesMatch();
                using (var hash = SHA256.Create()) _collisionBindingHash = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(binding.ToString(Newtonsoft.Json.Formatting.None)))).Replace("-", "").ToLowerInvariant();
                binding["bindingManifestSha256"] = _collisionBindingHash; _collisionBindingsComplete = complete;
            }
            private bool RuntimeCapsulesMatch()
            {
                var runtime = _collision != null ? _collision.BodyCapsules : null;
                return runtime != null && runtime.Length == _capsules.Length && runtime.Distinct().Count() == runtime.Length
                    && runtime.All(c => c != null && _capsules.Contains(c));
            }
            private string HierarchyPath(Transform transform)
            {
                var names = new List<string>();
                for (var current = transform; current != null; current = current.parent)
                { names.Add(current.name); if (current == _world) break; }
                names.Reverse(); return string.Join("/", names);
            }

            private void ApplyPose(JObject pose)
            {
                _fixture.transform.SetPositionAndRotation(_origin, _orientation); var matrices = Matrices(pose);
                foreach (var bone in _bones)
                {
                    Matrix4x4 target = matrices[bone.Key], delta = UnityEditorValidationV2StaticPoseAudit.BlenderRotationDelta(_restMatrices[bone.Key], target); Vector4 p = target.GetColumn(3);
                    bone.Value.SetPositionAndRotation(_world.TransformPoint(new Vector3(-p.x, p.z, -p.y)), _world.rotation * delta.rotation * _restRotations[bone.Key]);
                }
                _correctives.Apply((float)pose["handGripCorrectives"]["right"], (float)pose["handGripCorrectives"]["left"]);
                _secondary.ResetMotion();
            }
            private void Pose()
            {
                if (_finished || _posedFrame == Time.frameCount) return; _posedFrame = Time.frameCount;
                try
                {
                    Need(EditorApplication.timeSinceStartup - _started < 240, "Secondary collision fixture exceeded its 240s wall-time ceiling.");
                    Step step = _steps[_index]; if (step.First) ApplyPose(step.Pose);
                    if (step.Reset) _secondary.ResetMotion();
                    foreach (var part in _parts)
                    {
                        part.ResetRotationBeforeEvaluate = 0f;
                        if (step.Reset) for (int i = 0; i < part.Bones.Length; i++)
                            part.ResetRotationBeforeEvaluate = Mathf.Max(part.ResetRotationBeforeEvaluate, Quaternion.Angle(part.Bones[i].localRotation, part.Rotations[i]));
                    }
                    if (step.Kind == "anchor_impulse")
                    {
                        float t = step.LocalFrame < 24 ? step.LocalFrame * 2f * Mathf.PI / 24f : 0f;
                        _fixture.transform.SetPositionAndRotation(_origin + _orientation * new Vector3(.065f * Mathf.Sin(t), 0, .03f * Mathf.Sin(t * 2f)), _orientation * Quaternion.AngleAxis(12f * Mathf.Sin(t), Vector3.up));
                    }
                    _collisionFramesBefore = _collision != null ? _collision.Frames : -1;
                    _collisionWasConfigured = _collision != null && _collision.IsConfigured;
                    _collisionWasEnabled = _collision != null && _collision.enabled;
                    var watch = Stopwatch.StartNew(); _secondary.Evaluate(step.Dt, Vector3.zero); watch.Stop(); _springCpu = watch.Elapsed.TotalMilliseconds;
                    Need(_body.RefreshNow(), _body.LastError); _bodyCpu = _body.LastRefreshMilliseconds;
                    for (int i = 0; i < _caps.Length; i++) _caps[i] = PlayerClothCollisionBudgetRig.ReadCapsule(_capsules[i]);
                    Need(_fixture.GetComponentsInChildren<Cloth>(true).All(c => !c.enabled), "An automatic owner re-enabled native Cloth inside the fixture.");
                }
                catch (Exception e) { Finish("FAIL", e.ToString()); }
            }
            private void BakeAll()
            {
                foreach (var geometry in _geometry)
                {
                    Vector3[] points;
                    if (geometry.Renderer is SkinnedMeshRenderer skin) { Need(skin.sharedMesh == geometry.Mesh, "Source mesh identity changed."); skin.BakeMesh(_baked, false); points = _baked.vertices; }
                    else points = geometry.Mesh.vertices;
                    Need(points.Length == geometry.Mesh.vertexCount, "Actual BakeMesh/source correspondence changed."); Matrix4x4 m = geometry.Renderer.localToWorldMatrix;
                    for (int i = 0; i < points.Length; i++) { points[i] = m.MultiplyPoint3x4(points[i]); Need(Finite(points[i]), "Nonfinite actual secondary source surface."); }
                    geometry.World = points;
                }
            }
            private void Measure()
            {
                if (_finished || _measuredFrame == Time.frameCount || _posedFrame != Time.frameCount) return; _measuredFrame = Time.frameCount;
                try
                {
                    var clock = Stopwatch.StartNew(); BakeAll(); Step step = _steps[_index]; var rows = new JArray();
                    foreach (var part in _parts) rows.Add(MeasurePart(part, step.Reset)); clock.Stop();
                    var sample = new JObject { ["ordinal"] = _index, ["unityFrame"] = Time.frameCount, ["pose"] = step.Pose["id"], ["kind"] = step.Kind,
                        ["localFrame"] = step.LocalFrame, ["integrationStepSeconds"] = step.Dt, ["springCpuMilliseconds"] = _springCpu,
                        ["bodyRefreshCpuMilliseconds"] = _bodyCpu, ["actualBakeAndTriangleQueryCpuMilliseconds"] = clock.Elapsed.TotalMilliseconds,
                        ["inactiveBodyCapsulesStillMeasured"] = _caps.Count(c => !c.Active),
                        ["bodyCapsuleGeometry"] = new JArray(_caps.Select((c, i) => new JObject { ["name"] = _capsules[i].name,
                            ["startWorld"] = Vec(c.Start), ["endWorld"] = Vec(c.End), ["radiusMeters"] = c.Radius, ["enabled"] = c.Active })),
                        ["spring"] = JObject.Parse(JsonUtility.ToJson(_secondary.Diagnostics)), ["parts"] = rows,
                        ["collisionRuntime"] = MeasureCollisionRuntime() };
                    // Profile snapshot above is complete before this deliberately expensive,
                    // independently skinned all-triangle reference comparison.
                    PlayerSecondaryCollisionRig.SourceAudit fullSourceAudit = null;
                    if (_collision != null && _collision.FrameReady)
                        fullSourceAudit = _collision.AuditAgainstFullSource();
                    sample["collisionFullSourceAudit"] = fullSourceAudit != null ? JObject.FromObject(fullSourceAudit)
                        : new JObject { ["completed"] = false, ["error"] = "No valid runtime frame; baseline-only measurement." };
                    ((JArray)Report["samples"]).Add(sample);
                    if (fullSourceAudit != null)
                        Need(fullSourceAudit.completed && fullSourceAudit.exactSourceGeometryEquivalent
                            && fullSourceAudit.exactMaximumDepthEquivalent,
                            "Optimized collision differs from complete original skin/triangle reference: " + fullSourceAudit.error);
                    var addedAnatomy = new JArray();
                    foreach (var geometry in _geometry)
                        if (geometry.Renderer.name == "DosaV2_ShoulderLining_Left" || geometry.Renderer.name == "DosaV2_ShoulderLining_Right")
                            addedAnatomy.Add(new JObject { ["renderer"] = geometry.Renderer.name,
                                ["coverage"] = JObject.FromObject(DosaV2AnatomyUnionCoverage.Audit(geometry.World, geometry.Triangles, _caps)) });
                    sample["newShoulderAnatomyCoverage"] = addedAnatomy;
                    string directPoseId = (string)step.Pose["id"];
                    if (step.Kind == "direct_static" && (directPoseId == "open_hand"
                        || directPoseId == "hip_flex" || directPoseId == "torso_twist"))
                        sample["rigidContactFeasibilityDiagnostic"] = JObject.Parse(Validation.DosaV2RigidContactFeasibility.Run(_secondary, null, _proposalAngles));
                    if (step.Capture) Capture(step);
                    _index++; if (_index >= _steps.Count) Finish((int)Report["invariantFailures"] == 0 ? "COMPLETE_MEASURED" : "COMPLETE_WITH_INVARIANT_FAILURES", null);
                    else if (_index % 30 == 0) Save();
                }
                catch (Exception e) { Finish("FAIL", e.ToString()); }
            }
            private JObject MeasureCollisionRuntime()
            {
                if (_collision == null)
                {
                    Report["collisionRuntimeInvalidSamples"] = (int)Report["collisionRuntimeInvalidSamples"] + 1;
                    Report["collisionRuntimeEvidence"] = "NO_BINDING_BASELINE_ONLY";
                    return new JObject { ["present"] = false, ["runtimeExecutionVerified"] = false,
                        ["reason"] = "No serialized SecondaryCollision owner; independent surface evidence is baseline only." };
                }
                var measurements = _collision.Measurements ?? Array.Empty<PlayerSecondaryCollisionRig.PartMeasurement>();
                bool partCoverage = measurements.Length == _parts.Count && measurements.All(m => m != null && _parts.Any(p => p.Name == m.name))
                    && measurements.Select(m => m.name).Distinct(StringComparer.Ordinal).Count() == _parts.Count;
                bool references = _secondary.SecondaryCollision == _collision && _collision.Owns(_world) && RuntimeCapsulesMatch();
                bool verified = _collisionBindingsComplete && references && partCoverage && _collisionWasEnabled && _collision.enabled
                    && _collision.IsConfigured && _collision.FrameReady && string.IsNullOrEmpty(_collision.LastError)
                    && _collision.Frames == _collisionFramesBefore + 1;
                string countKey = verified ? "collisionRuntimeActiveSamples" : "collisionRuntimeInvalidSamples";
                Report[countKey] = (int)Report[countKey] + 1;
                if (_collision.UnresolvedParts > 0) Report["collisionRuntimeSamplesWithUnresolvedParts"] = (int)Report["collisionRuntimeSamplesWithUnresolvedParts"] + 1;
                Report["collisionRuntimeEvidence"] = (int)Report["collisionRuntimeInvalidSamples"] > 0 ? "UNVERIFIED_RUNTIME_SAMPLES_PRESENT"
                    : _collision.UnresolvedParts > 0 || (int)Report["collisionRuntimeSamplesWithUnresolvedParts"] > 0 ? "MEASURED_ACTIVE_WITH_UNRESOLVED_CONTACTS" : "MEASURED_ACTIVE_NOT_RIG_PASS";
                _collisionCpuTotal += _collision.LastFrameMainThreadMilliseconds;
                _collisionCpuMaximum = Math.Max(_collisionCpuMaximum, _collision.LastFrameMainThreadMilliseconds);
                _collisionMaximumAllocatedBytes = Math.Max(_collisionMaximumAllocatedBytes, _collision.LastFrameAllocatedBytes);
                if (_collisionWasConfigured && verified)
                {
                    _collisionWarmSamples++; _collisionWarmCpuTotal += _collision.LastFrameMainThreadMilliseconds;
                    _collisionWarmCpuMaximum = Math.Max(_collisionWarmCpuMaximum, _collision.LastFrameMainThreadMilliseconds);
                    _collisionWarmMaximumAllocatedBytes = Math.Max(_collisionWarmMaximumAllocatedBytes, _collision.LastFrameAllocatedBytes);
                }
                int samples = _index + 1;
                Report["collisionRuntimeMeasuredCpu"] = new JObject { ["samples"] = samples,
                    ["meanMilliseconds"] = _collisionCpuTotal / samples, ["maximumMilliseconds"] = _collisionCpuMaximum,
                    ["maximumAllocatedBytes"] = _collisionMaximumAllocatedBytes,
                    ["verifiedWarmSamples"] = _collisionWarmSamples,
                    ["verifiedWarmMeanMilliseconds"] = _collisionWarmSamples > 0 ? (JToken)new JValue(_collisionWarmCpuTotal / _collisionWarmSamples) : JValue.CreateNull(),
                    ["verifiedWarmMaximumMilliseconds"] = _collisionWarmSamples > 0 ? (JToken)new JValue(_collisionWarmCpuMaximum) : JValue.CreateNull(),
                    ["verifiedWarmMaximumAllocatedBytes"] = _collisionWarmSamples > 0 ? (JToken)new JValue(_collisionWarmMaximumAllocatedBytes) : JValue.CreateNull(),
                    ["includesColdInitialization"] = true, ["scope"] = "Runtime BeginFrame through EndFrame; independent BakeMesh/JSON not included." };
                return new JObject { ["present"] = true, ["runtimeExecutionVerified"] = verified,
                    ["bindingManifestSha256"] = _collisionBindingHash, ["bindingComplete"] = _collisionBindingsComplete,
                    ["exactOwnerAndBodyReferencesMatch"] = references, ["all25PartMeasurementsPresent"] = partCoverage,
                    ["enabledDuringEvaluate"] = _collisionWasEnabled, ["enabledAfterEvaluate"] = _collision.enabled,
                    ["configuredBeforeEvaluate"] = _collisionWasConfigured, ["configuredAfterEvaluate"] = _collision.IsConfigured,
                    ["coldInitializationPossible"] = !_collisionWasConfigured,
                    ["lastInitialization"] = _collision.LastInitialization != null
                        ? JObject.FromObject(_collision.LastInitialization) : JValue.CreateNull(),
                    ["FrameReady"] = _collision.FrameReady, ["LastError"] = _collision.LastError,
                    ["frameCounterBeforeEvaluate"] = _collisionFramesBefore, ["frameCounterAfterEvaluate"] = _collision.Frames,
                    ["UnresolvedParts"] = _collision.UnresolvedParts,
                    ["MaximumRemainingPenetrationMeters"] = _collision.MaximumRemainingPenetrationMeters,
                    ["LastFrameMainThreadMilliseconds"] = _collision.LastFrameMainThreadMilliseconds,
                    ["LastFrameAllocatedBytes"] = _collision.LastFrameAllocatedBytes,
                    ["Measurements"] = new JArray(measurements.Select(m => m != null ? (JToken)JObject.Parse(JsonUtility.ToJson(m)) : JValue.CreateNull())) };
            }
            private JObject MeasurePart(Part part, bool reset)
            {
                int vertices = 0, triangles = 0, pointPairs = 0, trianglePairs = 0, trianglePairs10um = 0, pointPairs10um = 0, mixed = 0, boundary = 0;
                float pointDepth = 0, triangleDepth = 0, shapeError = 0, positionError = 0, scaleError = 0, lengthError = 0;
                float resetRotation = reset ? part.ResetRotationBeforeEvaluate : 0f, postEvaluateRestRotation = 0f;
                int worstTriangle = -1, worstCap = -1; string worstRenderer = null; Vector3 worstSurface = default, worstAxis = default;
                for (int i = 0; i < part.Bones.Length; i++)
                {
                    positionError = Mathf.Max(positionError, Vector3.Distance(part.Bones[i].localPosition, part.Positions[i]));
                    scaleError = Mathf.Max(scaleError, Vector3.Distance(part.Bones[i].localScale, part.Scales[i]));
                    postEvaluateRestRotation = Mathf.Max(postEvaluateRestRotation, Quaternion.Angle(part.Bones[i].localRotation, part.Rotations[i]));
                    if (!part.Rigid)
                    { float length = i + 1 < part.Bones.Length ? Vector3.Distance(part.Bones[i].position, part.Bones[i + 1].position) : part.Bones[i].TransformVector(part.Tip).magnitude; lengthError = Mathf.Max(lengthError, Mathf.Abs(length - part.Lengths[i])); }
                }
                foreach (var surface in part.Surfaces)
                {
                    var points = surface.Geometry.World; vertices += surface.Vertices.Length; triangles += surface.Triangles.Length; mixed += surface.MixedInfluences; boundary += surface.MixedTriangles;
                    for (int v = 0; v < surface.Vertices.Length; v++)
                    {
                        Vector3 p = points[surface.Vertices[v]];
                        if (part.Rigid) shapeError = Mathf.Max(shapeError, Vector3.Distance(part.Bones[0].InverseTransformPoint(p), surface.RigidRest[v]));
                        foreach (var capsule in _caps)
                        {
                            float depth = capsule.Radius - Vector3.Distance(p, ClosestSegmentPoint(p, capsule.Start, capsule.End));
                            if (depth > 0) pointPairs++; if (depth > .00001f) pointPairs10um++; pointDepth = Mathf.Max(pointDepth, depth);
                        }
                    }
                    foreach (int index in surface.Triangles)
                    {
                        int t = index * 3; Vector3 a = points[surface.Geometry.Triangles[t]], b = points[surface.Geometry.Triangles[t + 1]], c = points[surface.Geometry.Triangles[t + 2]];
                        Vector3 min = Vector3.Min(a, Vector3.Min(b, c)), max = Vector3.Max(a, Vector3.Max(b, c));
                        for (int i = 0; i < _caps.Length; i++)
                        {
                            var cap = _caps[i]; Vector3 lo = Vector3.Min(cap.Start, cap.End) - Vector3.one * cap.Radius, hi = Vector3.Max(cap.Start, cap.End) + Vector3.one * cap.Radius;
                            if (max.x < lo.x || min.x > hi.x || max.y < lo.y || min.y > hi.y || max.z < lo.z || min.z > hi.z) continue;
                            float distance = SegmentTriangleDistance(cap.Start, cap.End, a, b, c, out Vector3 onAxis, out Vector3 onTriangle), depth = cap.Radius - distance;
                            if (depth > 0) trianglePairs++; if (depth > .00001f) trianglePairs10um++;
                            if (depth > triangleDepth) { triangleDepth = depth; worstTriangle = index; worstCap = i; worstRenderer = surface.Geometry.Renderer.name; worstSurface = onTriangle; worstAxis = onAxis; }
                        }
                    }
                }
                bool invariant = mixed == 0 && positionError <= .0001f && scaleError <= .0001f && shapeError <= .0001f && lengthError <= .0001f && (!reset || resetRotation <= .01f);
                if (!invariant) Report["invariantFailures"] = (int)Report["invariantFailures"] + 1;
                Report["maximumTriangleProxyPenetrationMeters"] = Mathf.Max((float)Report["maximumTriangleProxyPenetrationMeters"], triangleDepth);
                return new JObject { ["name"] = part.Name, ["rigid"] = part.Rigid, ["checkedVertices"] = vertices, ["checkedTriangles"] = triangles, ["bodyCapsules"] = _caps.Length,
                    ["pointCapsulePairsPositive"] = pointPairs, ["pointCapsulePairsOver10um"] = pointPairs10um, ["triangleCapsulePairsPositive"] = trianglePairs, ["triangleCapsulePairsOver10um"] = trianglePairs10um,
                    ["maximumPointDepthMeters"] = pointDepth, ["maximumTriangleDepthMeters"] = triangleDepth, ["worstRenderer"] = worstRenderer, ["worstSourceTriangle"] = worstTriangle,
                    ["worstBodyCapsule"] = worstCap >= 0 ? _capsules[worstCap].name : null, ["worstSurfaceWorld"] = Vec(worstSurface), ["worstAxisWorld"] = Vec(worstAxis),
                    ["pivotLocalPositionDeltaMeters"] = positionError, ["pivotLocalScaleDelta"] = scaleError, ["rigidPivotLocalVertexDeltaMeters"] = shapeError,
                    ["chainSegmentLengthDeltaMeters"] = lengthError, ["resetRotationDeltaDegrees"] = resetRotation, ["mixedRigidInfluenceVertices"] = mixed,
                    ["resetRotationMeasuredBeforeEvaluate"] = reset, ["postEvaluateRotationFromAuthoredRestDegrees"] = postEvaluateRestRotation,
                    ["mixedBoundaryTrianglesRetained"] = boundary, ["invariantsWithinInstrumentationGuards"] = invariant };
            }
            private void Capture(Step step)
            {
                Vector3 target = _world.position + _world.up * 1.08f;
                foreach (var view in new[] { ("front", new Vector3(2.6f, 1.45f, 3.1f)), ("back", new Vector3(-2.2f, 1.4f, -3.2f)) })
                {
                    _camera.transform.position = _world.position + _world.rotation * view.Item2; _camera.transform.LookAt(target, _world.up);
                    var old = RenderTexture.active; var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                    try
                    {
                        RenderPipeline.SubmitRenderRequest(_camera, new RenderPipeline.StandardRequest { destination = _target }); RenderTexture.active = _target;
                        pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                        Color32[] colors = pixels.GetPixels32(); Color32 background = colors[0];
                        int foreground = colors.Count(p => Math.Abs(p.r - background.r) + Math.Abs(p.g - background.g) + Math.Abs(p.b - background.b) > 18);
                        Need(foreground > 100, "Evidence camera captured no meaningful foreground; geometry measurements do not validate a blank render.");
                        string path = Path.Combine(_directory, _index.ToString("000") + "_" + step.Pose["id"] + "_" + step.Kind + "_" + view.Item1 + ".png");
                        File.WriteAllBytes(path, pixels.EncodeToPNG()); ((JArray)Report["images"]).Add(new JObject { ["path"] = path, ["foregroundPixels"] = foreground });
                    }
                    finally { RenderTexture.active = old; Object.DestroyImmediate(pixels); }
                }
            }
            private void AddSource(string path)
            { Need(File.Exists(path), "Missing provenance source: " + path); ((JArray)Report["sources"]).Add(new JObject { ["path"] = path, ["sha256"] = Sha(path) }); }
            private void PlayChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Finish("STOPPED", "Play Mode exited."); }
            private void Reload() => Finish("STOPPED", "Assembly reload.");
            public void Finish(string status, string error)
            {
                if (_finished) return; _finished = true; Report["status"] = status; Report["error"] = error;
                EditorApplication.playModeStateChanged -= PlayChanged; AssemblyReloadEvents.beforeAssemblyReload -= Reload;
                try
                {
                    var loop = PlayerLoop.GetCurrentPlayerLoop(); if (Remove(ref loop)) PlayerLoop.SetPlayerLoop(loop);
                    if (_fixture != null) Object.DestroyImmediate(_fixture); if (_baked != null) Object.DestroyImmediate(_baked);
                    if (_target != null) { _target.Release(); Object.DestroyImmediate(_target); }
                    Report["fixtureDestroyed"] = _fixture == null;
                    if (_scene.IsValid() && _scene.isLoaded && Application.isPlaying)
                    {
                        var operation = SceneManager.UnloadSceneAsync(_scene);
                        if (operation != null) { Report["status"] = "RESTORING"; Save(); operation.completed += _ => Complete(status); return; }
                    }
                    Complete(status);
                }
                catch (Exception e) { Report["error"] = (string)Report["error"] + " RESTORE: " + e; Complete("FAIL"); }
            }
            private void Complete(string status)
            { Report["status"] = status; Report["restored"] = _fixture == null && (!_scene.IsValid() || !_scene.isLoaded); _last = Report; _active = null; Save(); }
            private void Save()
            {
                if (string.IsNullOrEmpty(_directory)) return; string json = Report.ToString(); File.WriteAllText(Path.Combine(_directory, "report.json"), json);
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2/secondary-collision-validation.json")), json);
            }
        }

        public static string[] ExpectedBaseCapsuleNames()
        {
            var names = new List<string>();
            for (int i = 0; i < 17; i++) names.Add("DosaV2_ClothProxy_BodyLining_Surface_" + i);
            foreach (string side in new[] { "Left", "Right" })
            {
                for (int i = 0; i < 3; i++) names.Add("DosaV2_ClothProxy_ArmLining_" + side + "_Posed_" + i);
                for (int i = 0; i < 2; i++) names.Add("DosaV2_ClothProxy_LegLining_" + side + "_" + i + "_0");
            }
            return names.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }
        public static bool ValidateCapsuleNames(string[] actual, string[] explicitAdditional, out string error)
        {
            var expected = new HashSet<string>(ExpectedBaseCapsuleNames(), StringComparer.Ordinal);
            if (explicitAdditional == null || actual == null) { error = "Exact capsule lists are required."; return false; }
            foreach (string name in explicitAdditional)
                if (string.IsNullOrWhiteSpace(name) || !expected.Add(name)) { error = "Additional capsules must be explicit unique names outside the fixed base-27 set."; return false; }
            if (actual.Length != expected.Count || actual.Any(string.IsNullOrWhiteSpace)
                || actual.Distinct(StringComparer.Ordinal).Count() != actual.Length || actual.Any(n => !expected.Contains(n)))
            { error = "Every exact base-27 and explicitly named additional capsule must appear once; missing, unexpected or duplicate proxies are not accepted."; return false; }
            error = null; return true;
        }

        /// <summary>Minimum distance between a finite axis segment and a complete filled triangle.</summary>
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
        private static void SegmentSegment(Vector3 p, Vector3 q, Vector3 a, Vector3 b, out Vector3 first, out Vector3 second)
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
        private static Dictionary<string, Matrix4x4> Matrices(JObject pose)
        {
            var result = new Dictionary<string, Matrix4x4>();
            foreach (var p in ((JObject)pose["boneMatricesRigLocal"]).Properties())
            { var matrix = Matrix4x4.zero; for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) matrix[r, c] = (float)p.Value[r][c]; Need(Mathf.Abs(matrix.determinant - 1f) < .001f, "Invalid direct pose basis."); result.Add(p.Name, matrix); }
            return result;
        }
        private static T Field<T>(object owner, string name)
        { var field = owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance); Need(field != null, "Serialized binding field unavailable: " + name); return (T)field.GetValue(owner); }
        private static bool Insert(ref PlayerLoopSystem node, Type anchor, PlayerLoopSystem inserted)
        {
            if (node.subSystemList == null) return false; var children = new List<PlayerLoopSystem>(node.subSystemList);
            for (int i = 0; i < children.Count; i++) { if (children[i].type == anchor) { children.Insert(i + 1, inserted); node.subSystemList = children.ToArray(); return true; } var child = children[i]; if (Insert(ref child, anchor, inserted)) { children[i] = child; node.subSystemList = children.ToArray(); return true; } } return false;
        }
        private static bool Remove(ref PlayerLoopSystem node)
        {
            if (node.subSystemList == null) return false; bool changed = false; var children = new List<PlayerLoopSystem>();
            foreach (var child in node.subSystemList) { if (child.type == typeof(PoseStage) || child.type == typeof(MeasureStage)) { changed = true; continue; } var copy = child; changed |= Remove(ref copy); children.Add(copy); }
            if (changed) node.subSystemList = children.ToArray(); return changed;
        }
        private static string Normalize(string name) { int i = name.LastIndexOf(':'); return i < 0 ? name : name.Substring(i + 1); }
        private static int Depth(Transform t) { int n = 0; while (t.parent != null) { n++; t = t.parent; } return n; }
        private static JObject Vec(Vector3 p) => new JObject { ["x"] = p.x, ["y"] = p.y, ["z"] = p.z };
        private static string ProjectFile(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        private static string Sha(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        private static bool Finite(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
        private static void Need(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    }
}
