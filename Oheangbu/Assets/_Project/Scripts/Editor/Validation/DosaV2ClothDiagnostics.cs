using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Oheangbu.Presentation;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Measured native Cloth on a disposable, static V2 prefab. Never grants RIG_PASS or creates animation.</summary>
    public static class DosaV2ClothDiagnostics
    {
        private static Session _active;
        private static Report _last;
        private static InitializationProbe _initializationProbe;
        private static InitializationProbeReport _lastInitializationProbe;
        private struct ControlStage { }
        private struct PhysicsLatchStage { }
        private struct MeasureStage { }
        private enum Pose { Rest, RaisedArms, Grip, Impulse, Reset }
        [Serializable] private sealed class GripOffset { public string name = null; public Quaternion offset = default; }
        [Serializable] private sealed class GripOffsets { public GripOffset[] offsets = null; }
        private sealed class Pending : Exception { public Pending(string message) : base(message) { } }

        [Serializable] private sealed class Timing
        {
            public long samples;
            public double totalMilliseconds, maximumMilliseconds, meanMilliseconds;
            public void Add(double milliseconds)
            { samples++; totalMilliseconds += milliseconds; maximumMilliseconds = Math.Max(maximumMilliseconds, milliseconds); meanMilliseconds = totalMilliseconds / samples; }
        }
        [Serializable] private sealed class SurfaceResult
        {
            public string name;
            public int particles, pinnedParticles, edgeCount, nonFiniteSamples, pinnedProxySamples, freeProxySamples;
            public float maxPinnedDistanceMeters, maxSettledPinnedDistanceMeters, maxAuthoredEnvelopeExcessMeters, maxRestEdgeStretchRatio;
            public float maxPhysicsLatchedPinnedDistanceMeters, maxSettledPhysicsLatchedPinnedDistanceMeters;
            public float maxPhysicsFramePinnedDistanceMeters;
            public float maxLiveBakedPinnedToNativeMeters, maxLiveBakedPinnedToCurrentAnchorMeters, maxLiveBakedPinnedToPhysicsLatchMeters;
            public float maxLiveBakedRepresentationFrameToNativeMeters, maxLiveBakedRootBoneFrameToNativeMeters;
            public string liveBakeFrameStatus = "UNVERIFIED: raw live-Cloth BakeMesh frame/index candidates are diagnostic only; do not infer a rendered tear from a coordinate mismatch.";
            public int liveBakedOutputFrames, physicsLatchedMeasuredFrames;
            public float maxSettledRestEdgeStretchRatio, maxSkinnedRestEdgeStretchRatio, minimumRequiredRestEdgeStretchRatio;
            public string envelopeBoundScope = "For each source edge: max(0, distance(actual skinned anchors) - both authored maxDistances) / original rest edge length. A lower bound exceeding acceptance cannot be solved by native stiffness or collision tuning. A bound below acceptance is necessary only: it does not prove a simultaneous solution for all edges, tethers, bending, backstops or body collisions.";
            public float maxProxyPenetrationMeters, maxFreeProxyPenetrationMeters, maxSettledSpeedMetersPerSecond;
            public int nativeSolvedSamples;
            public Vector3 boundsMin, boundsMax;
            public bool hasBounds;
            public int measuredFrames, settledFrames;
            public float maxPinnedAnchorProxyPenetrationMeters;
            public float maxBodyFreePenetrationMeters, maxBrushFreePenetrationMeters;
            public int bodyFreePenetrationSamples, brushFreePenetrationSamples;
            public int pinnedAnchorProxySamples;
            public int omittedPotentialMaximum, omittedParticleSamples;
            public float maximumOmittedPenetrationMeters;
            public PlayerClothCollisionBudgetRig.SurfaceSelection finalCollisionSelection;
            public EdgeEvidence worstEdge;
            public List<PinnedTiming> pinTiming = new List<PinnedTiming>();
        }
        [Serializable] private sealed class PinnedTiming
        {
            // Preserve the original fields: previousAnchor is the previous RENDER
            // snapshot, not the previous native fixed-step input.
            public int frame;
            public float gameSeconds, currentAnchorErrorMeters, previousAnchorErrorMeters;
            public bool previousAvailable, nativeSolved;
            public bool physicsLatchAvailable, physicsLatchBeforeControl, physicsLatchSameRenderFrame, liveBakeAvailable;
            public long physicsLatchSequence;
            public int physicsLatchFrame, controlFrameAtPhysicsLatch, physicsLatchCallsThisRenderFrame;
            public double measuredFixedTime, physicsLatchFixedTime;
            public float physicsLatchedAnchorErrorMeters, physicsFrameAnchorErrorMeters;
            public float liveBakedToNativeErrorMeters, liveBakedToCurrentAnchorErrorMeters, liveBakedToPhysicsLatchErrorMeters;
        }
        [Serializable] private sealed class PhysicsLatchTiming
        {
            public long sequence;
            public int frame, controlFrame;
            public double fixedTime, time;
            public float caseGameSeconds;
            public bool beforeCurrentRenderControl;
        }
        [Serializable] private sealed class EdgeEvidence
        {
            public int particleA, particleB, sourceA, sourceB;
            public bool pinnedA, pinnedB;
            public float restLengthMeters, actualLengthMeters, ratio;
            public Vector3 positionA, positionB, skinAnchorA, skinAnchorB;
            public Vector3 sourceRestA, sourceRestB;
            public float mobilityA, mobilityB;
        }
        [Serializable] private sealed class AnatomyCoverage
        {
            public string sourceRenderer;
            public int vertices, measuredFrames, uncoveredVertexSamples;
            public float maxOutsideMeters;
        }
        [Serializable] private sealed class Penetration
        {
            public string surface, proxy;
            public int particle;
            public bool pinned;
            public float depthMeters;
            public Vector3 position;
            public string measurement = "simulated_particle";
        }
        [Serializable] private sealed class CaseResult
        {
            public string id, pose;
            public List<SolverSettings> solverSettings = new List<SolverSettings>();
            public int requestedHz, frames, resetCountBefore, resetCountAfter, settledFrames;
            public float requiredWarmupSeconds = 3f;
            public int requiredSettledFrames = 60;
            public float stepMultiplier, requestedDeltaSeconds, actualGameSeconds, actualUnscaledSeconds;
            public float minimumActualDelta = float.MaxValue, maximumActualDelta, peakSecondarySwingDegrees;
            public double nativeSimulatedSeconds;
            public int nativeSolvedSamples, framesWithoutNativeStep;
            public int physicsLatchCalls;
            public List<PhysicsLatchTiming> physicsLatchTiming = new List<PhysicsLatchTiming>();
            public double actualWallSeconds;
            public bool resetObserved;
            public Timing secondaryEvaluateCpu = new Timing(), diagnosticCpu = new Timing();
            public Timing brushProxyRefreshCpu = new Timing();
            public Timing bodyProxyRefreshCpu = new Timing();
            public long bodyProxyMaximumAllocatedBytes;
            public Timing collisionSelectionCpu = new Timing();
            public long collisionSelectionMaximumAllocatedBytes;
            public int actualBrushProxies;
            public List<AnatomyCoverage> anatomyCoverage = new List<AnatomyCoverage>();
            public List<SurfaceResult> surfaces = new List<SurfaceResult>();
            public List<Penetration> worstParticlePenetrations = new List<Penetration>();
            public List<string> images = new List<string>();
        }
        [Serializable] private sealed class SolverSettings
        { public string cloth; public float frequency, stretching, bending, damping, minimumBackstop, maximumBackstop; public bool tethers, continuous; }
        [Serializable] private sealed class MarkerResult
        {
            public string name, unit;
            public Timing observedCpu = new Timing();
        }
        [Serializable] private sealed class RestMappingResult
        {
            public string surface, space = "Persisted V2 prefab in Play: renderer-local native particles, explicitly proven by full source/particle rest mapping before simulation.";
            public Vector3 nativeParticle0, bakedVertex0;
            public bool allMatched;
            public DosaV2SecondaryBuilder.ParticleMappingReport mapping;
        }
        [Serializable] private sealed class Report
        {
            public string status = "NOT_STARTED", error, directory, activeCase, prefabPath = DosaV2PlayerBuilder.ValidationPrefab;
            public string startedAtUtc, prefabSha256, originalScene, gripCalibrationPath, gripCalibrationSha256, clothLayout;
            public int gripCalibrationOffsets;
            public bool restored, globalStateRestored, fixtureDestroyed, sceneUnloadComplete;
            public bool reviewClothRestored;
            public int suspendedReviewCloth;
            public int frames, nonFiniteSamples;
            public int nativeInitializationFrames;
            public Vector3 gravityWorld;
            public double elapsedWallSeconds;
            public List<CaseResult> cases = new List<CaseResult>();
            public List<MarkerResult> nativeClothMarkers = new List<MarkerResult>();
            public List<RestMappingResult> restMappings = new List<RestMappingResult>();
            public List<string> findings = new List<string>();
            public string scope = "Static prefab in a disposable additive scene; direct Humanoid poses only. No production animation, actions, retargeting, existing scene mutation or RIG_PASS.";
            public string clock = "30/60/120 are requested game-time evaluation steps via temporary captureDeltaTime, not measured rendering FPS. Quarter-step condition uses 0.25/60 seconds. Actual deltaTime/unscaled/wall durations are recorded; all original Time settings are restored.";
            public string cpuScope = "Stopwatch measures fixture Evaluate and measurement code main-thread elapsed CPU scope. Available nanosecond Cloth ProfilerRecorder markers are reported separately, may include other scene cloth, overlap, and are not summed. No native marker means unavailable, not zero. No GPU/build performance claim.";
            public string penetrationScope = "Actual simulated particles tested against bound body and actual-brush capsule proxies, reported separately; pinned/free particles and original pinned anchors are separated. The five named inner-lining meshes are independently baked and tested for proxy union vertex coverage in every pose. No cloth-triangle, complete-body-surface or intentional-seam-contact certification is inferred.";
            public string particleSpace = "Persisted V2 Play Cloth before simulation maps fully to renderer-local independent BakeMesh rest. After the first actual solver update, world point = representation.TransformPoint(native), verified using original source-index anchors in the 80-observation initialization probe. No fixed-axis correction or increased matching tolerance.";
            public string settleContract = "Each condition runs at least 3 game seconds AND 3 accumulated native fixed-step seconds, followed by 60 actual solved snapshots in the same held pose. Cloth advances with the fixed physics clock, measured by the independent free-fall probe. A render frame without an advanced fixed clock never counts as a solved/settled sample. Speed uses the elapsed fixed clock since the previous solved snapshot, not render deltaTime. Raised/Grip are direct diagnostic poses, no clips or RIG_PASS.";
            public string pinTimingScope = "Control remains before PostLateUpdate.UpdateAllSkinnedMeshes. A separate read-only callback immediately BEFORE FixedUpdate.PhysicsClothFixedUpdate snapshots the actual current full-weight skin anchors, representation matrix, fixedTime and sequence. It never applies a pose or advances simulation. The last latch persists on render frames without a fixed update. At ClothFinish, original current/previous-render errors and latest physics-latched errors are reported together; native interpolation or a different internal target buffer can still differ from this observed pre-physics snapshot. physicsFrameAnchorError also transforms native particles using the latched representation matrix to isolate moving-root frame changes. pinTiming records the first 0.4s and every settled sample; physicsLatchTiming records every latch.";
            public string liveBakedOutputScope = "Live Cloth SkinnedMeshRenderer.BakeMesh after ClothFinish is a raw output candidate with UNVERIFIED coordinate frame and vertex indexing. Equal vertex count does not certify index correspondence. Candidate transform comparisons against native particles and independent current/physics-latched anchors are exploratory only. It is not used as the anchor, coefficient mapping, rest geometry, feasibility bound or an independent physical solution. No threshold is relaxed by a previous-frame match.";
        }
        private sealed class Surface
        {
            public Cloth Cloth;
            public SkinnedMeshRenderer Renderer;
            public Transform Representation;
            public Vector3[] Rest, PreviousWorld, PreviousExpectedWorld, PhysicsExpectedWorld;
            public BoneWeight1[] Weights;
            public int[] Offsets, ParticleSource;
            public byte[] WeightCounts;
            public Matrix4x4[] Bindposes, Skin;
            public Transform[] Bones;
            public ClothSkinningCoefficient[] Coefficients;
            public CapsuleCollider[] Proxies;
            public List<(int a, int b, float length)> Edges;
            public bool HasPrevious;
            public bool HasPhysicsLatch;
            public Matrix4x4 PhysicsRepresentationToWorld;
            public Mesh LiveOutputBake;
            public readonly List<Vector3> LiveOutputVertices = new List<Vector3>();
        }
        private sealed class Recorder
        {
            public ProfilerRecorder Value;
            public MarkerResult Result;
        }

        public static bool IsRunning => _active != null || _initializationProbe != null;
        public static string Begin()
        { return BeginSession(0); }

        public static string BeginStiffnessTuning()
        { return BeginSession(1); }

        public static string BeginFrequencyTuning()
        { return BeginSession(2); }

        // A bounded baseline for changed collision geometry. It deliberately
        // does not re-run poses whose authored skin constraints are known invalid.
        public static string BeginRestBaseline()
        { return BeginSession(3); }

        // Disposable cause-isolation only, never an acceptance or asset-setting change.
        public static string BeginWithoutCapsulesAndBackstops() => BeginSession(4);
        public static string BeginWithoutBackstops() => BeginSession(5);
        public static string BeginWithoutCapsules() => BeginSession(6);
        public static string BeginFlexibleClothIsolation() => BeginSession(7);
        public static string BeginCorePreview() => BeginSession(8);

        private static string BeginSession(int tuning)
        {
            if (_initializationProbe != null) return "WAIT: initialization probe is running.";
            if (_active != null) return Status();
            if (!Application.isPlaying || EditorApplication.isPaused)
                return "WAIT: unpaused Play Mode in the isolated C2_PlayerV2Validation scene is required.";
            if (SceneManager.GetActiveScene().name != "C2_PlayerV2Validation")
                return "WAIT: open C2_PlayerV2Validation; canonical/gameplay scenes are not timing-test fixtures.";
            var session = new Session(tuning); _active = session;
            try { session.Start(); }
            catch (Pending e) { session.Finish("WAIT", e.Message); }
            catch (Exception e) { session.Finish("FAIL", e.GetBaseException().Message); }
            return Status();
        }

        [Serializable] private sealed class MappingCandidate
        {
            public string name;
            public bool allMatched;
            public Vector3 sourceVertex0;
            public float nearestSourceToNative0;
            public float maxPinnedSourceIndexError;
            public DosaV2SecondaryBuilder.ParticleMappingReport mapping;
        }
        [Serializable] private sealed class InitializationObservation
        {
            public string condition, stage, renderer;
            public int frame, sourceVertices, particles, coefficients;
            public bool clothEnabled, nativeBaselineComparable, rigConfigured;
            public string bindingError;
            public Vector3 representationPosition, nativeParticle0;
            public float maxNativeDriftSinceSpawn, maxPinnedNativeDriftSinceSpawn, maxFreeNativeDriftSinceSpawn;
            public float maxWorldBakedVsHierarchyBaked, maxSourceBindVsHierarchyBaked, maxCpuSkinVsHierarchyBaked;
            public List<MappingCandidate> candidates = new List<MappingCandidate>();
        }
        [Serializable] private sealed class InitializationProbeReport
        {
            public string status = "WAIT", error, path, prefabSha256;
            public int waitedFrames;
            public bool restored;
            public List<InitializationObservation> observations = new List<InitializationObservation>();
            public string scope = "Two disposable prefab copies at origin and (30,0,30), both yaw23. Snapshot before freeze, after freeze, after two frame boundaries, and immediately after re-enable. No Time/input changes or coefficient rewrites. Native index drift detects simulation; renderer-local and representation-local candidates compare independent BakeMesh/source bind/CPU skin. Full mapping tolerance remains 10 micrometers.";
        }
        public static string BeginInitializationProbe()
        {
            if (IsRunning) return "WAIT: another Cloth diagnostic/probe is running.";
            if (!Application.isPlaying || EditorApplication.isPaused || SceneManager.GetActiveScene().name != "C2_PlayerV2Validation")
                return "WAIT: unpaused Play in C2_PlayerV2Validation is required.";
            var probe = new InitializationProbe(); _initializationProbe = probe;
            try { probe.Start(); } catch (Exception e) { probe.Finish("FAIL", e.GetBaseException().Message); }
            return InitializationProbeStatus();
        }
        public static string InitializationProbeStatus()
        { var report = _initializationProbe != null ? _initializationProbe.Result : _lastInitializationProbe; return report == null ? "NOT_STARTED" : JsonUtility.ToJson(new InitializationProbeStatusView
            { status = report.status, error = report.error, path = report.path, observations = report.observations.Count, waitedFrames = report.waitedFrames, restored = report.restored }); }
        [Serializable] private sealed class InitializationProbeStatusView { public string status, error, path; public int observations, waitedFrames; public bool restored; }
        public static string StopInitializationProbe()
        { _initializationProbe?.Finish("STOPPED", "Stopped by caller."); return InitializationProbeStatus(); }

        private sealed class InitializationProbe
        {
            public readonly InitializationProbeReport Result = new InitializationProbeReport();
            private Scene _scene;
            private readonly List<GameObject> _fixtures = new List<GameObject>();
            private readonly Dictionary<Cloth, Vector3[]> _baseline = new Dictionary<Cloth, Vector3[]>();
            private readonly Dictionary<Cloth, int[]> _sourceIndices = new Dictionary<Cloth, int[]>();
            private int _spawnFrame;
            private int _activeFrame = -1;
            private bool _finished;
            private double _started;
            public void Start()
            {
                Result.path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2/cloth-initialization-probe.json"));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.ValidationPrefab); Need(prefab != null, "Missing V2 validation prefab.");
                Result.prefabSha256 = FileSha(Path.GetFullPath(Path.Combine(Application.dataPath, "..", DosaV2PlayerBuilder.ValidationPrefab)));
                _scene = SceneManager.CreateScene("V2ClothInitializationProbe_" + Guid.NewGuid().ToString("N"));
                _started = Time.realtimeSinceStartupAsDouble; _spawnFrame = Time.frameCount;
                EditorApplication.playModeStateChanged += PlayChanged; AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
                for (int condition = 0; condition < 2; condition++)
                {
                    var fixture = Object.Instantiate(prefab, condition == 0 ? Vector3.zero : new Vector3(30f, 0f, 30f), Quaternion.Euler(0f, 23f, 0f));
                    fixture.name = condition == 0 ? "origin" : "translated_30m"; _fixtures.Add(fixture); SceneManager.MoveGameObjectToScene(fixture, _scene);
                    foreach (var lod in fixture.GetComponentsInChildren<PlayerLodClothController>(true)) lod.SuspendForStaticFixture();
                    foreach (var camera in fixture.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                    foreach (var listener in fixture.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
                    foreach (var skin in fixture.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    { skin.updateWhenOffscreen = true; skin.forceMatrixRecalculationPerRender = true; }
                    Observe(fixture, "spawn_before_freeze");
                    foreach (var rig in fixture.GetComponentsInChildren<PlayerSecondaryMotionRig>(true)) rig.SetRepresentationActive(false);
                    foreach (var cloth in fixture.GetComponentsInChildren<Cloth>(true)) cloth.enabled = false;
                    foreach (var renderer in fixture.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                    Observe(fixture, "spawn_after_freeze");
                }
                EditorApplication.update += WaitForNative; Result.status = "WAITING_TWO_FRAMES"; Save();
            }
            private void WaitForNative()
            {
                if (_finished) return;
                Result.waitedFrames = Time.frameCount - _spawnFrame;
                if (Result.waitedFrames < 2 && Time.realtimeSinceStartupAsDouble - _started < 5d) return;
                try
                {
                    Need(Result.waitedFrames >= 2, "Two actual frame boundaries did not elapse.");
                    if (_activeFrame >= 0)
                    {
                        if (Time.frameCount - _activeFrame < 3) return;
                        foreach (var fixture in _fixtures) Observe(fixture, "after_three_active_simulation_frames");
                        Finish("MEASURED", null); return;
                    }
                    foreach (var fixture in _fixtures)
                    {
                        Observe(fixture, "after_two_frames_before_enable");
                        foreach (var rig in fixture.GetComponentsInChildren<PlayerSecondaryMotionRig>(true)) rig.SetRepresentationActive(true);
                        foreach (var renderer in fixture.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
                        Observe(fixture, "after_two_frames_after_enable");
                    }
                    _activeFrame = Time.frameCount; Result.status = "WAITING_THREE_ACTIVE_FRAMES"; Save();
                }
                catch (Exception e) { Finish("FAIL", e.GetBaseException().Message); }
            }
            private void Observe(GameObject fixture, string stage)
            {
                foreach (var cloth in fixture.GetComponentsInChildren<Cloth>(true))
                {
                    var renderer = cloth.GetComponent<SkinnedMeshRenderer>(); var owner = cloth.GetComponentInParent<PlayerSecondaryMotionRig>();
                    Need(renderer != null && renderer.sharedMesh != null && owner != null, "Probe requires explicit renderer/representation.");
                    Vector3[] native = cloth.vertices; var coefficients = cloth.coefficients; Mesh source = renderer.sharedMesh;
                    if (!_baseline.ContainsKey(cloth)) _baseline.Add(cloth, (Vector3[])native.Clone());
                    var entry = new InitializationObservation { condition = fixture.name, stage = stage, renderer = renderer.name, frame = Time.frameCount,
                        sourceVertices = source.vertexCount, particles = native.Length, coefficients = coefficients.Length, clothEnabled = cloth.enabled,
                        rigConfigured = owner.IsConfigured, bindingError = owner.LastBindingError, representationPosition = owner.transform.position,
                        nativeParticle0 = native.Length > 0 ? native[0] : Vector3.zero, nativeBaselineComparable = native.Length > 0 && _baseline[cloth].Length == native.Length };
                    Result.observations.Add(entry);
                    if (entry.nativeBaselineComparable) for (int i = 0; i < native.Length; i++)
                    {
                        float delta = Vector3.Distance(native[i], _baseline[cloth][i]); entry.maxNativeDriftSinceSpawn = Mathf.Max(entry.maxNativeDriftSinceSpawn, delta);
                        if (i < coefficients.Length && coefficients[i].maxDistance == 0f) entry.maxPinnedNativeDriftSinceSpawn = Mathf.Max(entry.maxPinnedNativeDriftSinceSpawn, delta);
                        else entry.maxFreeNativeDriftSinceSpawn = Mathf.Max(entry.maxFreeNativeDriftSinceSpawn, delta);
                    }
                    Vector3[] baked = BakeIndependent(renderer);
                    if (!_sourceIndices.ContainsKey(cloth))
                    {
                        var indices = new int[native.Length];
                        for (int p = 0; p < native.Length; p++)
                        {
                            float best = float.MaxValue; int index = -1;
                            for (int v = 0; v < baked.Length; v++)
                            { float distance = (baked[v] - native[p]).sqrMagnitude; if (distance < best) { best = distance; index = v; } }
                            Need(index >= 0 && best <= 1e-10f, "No exact baseline source index for simulation-frame probe.");
                            indices[p] = index;
                        }
                        _sourceIndices.Add(cloth, indices);
                    }
                    Matrix4x4 worldMatrix = owner.transform.worldToLocalMatrix * renderer.localToWorldMatrix;
                    Matrix4x4 hierarchy = LocalHierarchyMatrix(renderer.transform, owner.transform);
                    Vector3[] worldBaked = baked.Select(worldMatrix.MultiplyPoint3x4).ToArray();
                    Vector3[] localBaked = baked.Select(hierarchy.MultiplyPoint3x4).ToArray();
                    Vector3[] sourceBind = source.vertices.Select(hierarchy.MultiplyPoint3x4).ToArray();
                    Vector3[] cpu = CpuSkinLocal(renderer, owner.transform);
                    entry.maxWorldBakedVsHierarchyBaked = MaximumDelta(worldBaked, localBaked);
                    entry.maxSourceBindVsHierarchyBaked = MaximumDelta(sourceBind, localBaked);
                    entry.maxCpuSkinVsHierarchyBaked = MaximumDelta(cpu, localBaked);
                    Add("world_matrix_baked", worldBaked); Add("local_hierarchy_baked", localBaked); Add("local_hierarchy_source_bind", sourceBind); Add("local_hierarchy_cpu_skin", cpu);
                    Add("renderer_local_baked", baked); Add("renderer_local_source_bind", source.vertices);
                    Matrix4x4 toRenderer = hierarchy.inverse; Add("renderer_local_cpu_skin", cpu.Select(toRenderer.MultiplyPoint3x4).ToArray());
                    void Add(string name, Vector3[] positions)
                    {
                        bool success = DosaV2SecondaryBuilder.TryMapAuthoredCoefficients(positions,
                            DosaV2SecondaryBuilder.GetAuthoredClothValues(source, owner.GetComponentsInChildren<Cloth>(true).Length == 8), native, out _, out var mapping);
                        float maxPinError = 0f;
                        for (int p = 0; p < native.Length; p++) if (coefficients[p].maxDistance == 0f)
                            maxPinError = Mathf.Max(maxPinError, Vector3.Distance(native[p], positions[_sourceIndices[cloth][p]]));
                        entry.candidates.Add(new MappingCandidate { name = name, allMatched = success, mapping = mapping,
                            maxPinnedSourceIndexError = maxPinError,
                            sourceVertex0 = positions.Length > 0 ? positions[0] : Vector3.zero,
                            nearestSourceToNative0 = native.Length > 0 && positions.Length > 0 ? positions.Min(p => Vector3.Distance(p, native[0])) : -1f });
                    }
                }
            }
            private static Vector3[] BakeIndependent(SkinnedMeshRenderer source)
            {
                var obj = new GameObject("ClothInitIndependentSkin"); var baked = new Mesh();
                try
                {
                    obj.transform.SetParent(source.transform.parent, false); obj.transform.localPosition = source.transform.localPosition;
                    obj.transform.localRotation = source.transform.localRotation; obj.transform.localScale = source.transform.localScale;
                    var renderer = obj.AddComponent<SkinnedMeshRenderer>(); renderer.enabled = false; renderer.sharedMesh = source.sharedMesh;
                    renderer.bones = source.bones; renderer.rootBone = source.rootBone; renderer.quality = source.quality; renderer.BakeMesh(baked, false); return baked.vertices;
                }
                finally { Object.DestroyImmediate(obj); Object.DestroyImmediate(baked); }
            }
            private static Matrix4x4 LocalHierarchyMatrix(Transform node, Transform ancestor)
            {
                Matrix4x4 matrix = Matrix4x4.identity; Transform current = node;
                while (current != ancestor) { Need(current != null, "The supplied representation is not an ancestor."); matrix = Matrix4x4.TRS(current.localPosition, current.localRotation, current.localScale) * matrix; current = current.parent; }
                return matrix;
            }
            private static Vector3[] CpuSkinLocal(SkinnedMeshRenderer renderer, Transform representation)
            {
                Mesh mesh = renderer.sharedMesh; Vector3[] vertices = mesh.vertices; var result = new Vector3[vertices.Length];
                Matrix4x4[] skin = renderer.bones.Select((bone, i) => LocalHierarchyMatrix(bone, representation) * mesh.bindposes[i]).ToArray();
                using (var counts = mesh.GetBonesPerVertex()) using (var weights = mesh.GetAllBoneWeights())
                {
                    Need(counts.Length == vertices.Length, "Missing full skin stream."); int offset = 0;
                    for (int i = 0; i < vertices.Length; i++) for (int j = 0; j < counts[i]; j++)
                    { Need(offset < weights.Length, "Invalid full skin stream."); var w = weights[offset++]; Need(w.boneIndex >= 0 && w.boneIndex < skin.Length, "Invalid skin bone index."); result[i] += skin[w.boneIndex].MultiplyPoint3x4(vertices[i]) * w.weight; }
                    Need(offset == weights.Length, "Unexpected trailing skin weights.");
                }
                return result;
            }
            private static float MaximumDelta(Vector3[] a, Vector3[] b)
            { Need(a.Length == b.Length, "Source-indexed snapshot count changed."); float result = 0f; for (int i = 0; i < a.Length; i++) result = Mathf.Max(result, Vector3.Distance(a[i], b[i])); return result; }
            private void PlayChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Finish("STOPPED", "Play exited."); }
            private void BeforeReload() => Finish("STOPPED", "Assembly reload.");
            public void Finish(string status, string error)
            {
                if (_finished) return; _finished = true; Result.status = status; Result.error = error;
                EditorApplication.update -= WaitForNative; EditorApplication.playModeStateChanged -= PlayChanged; AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
                foreach (var fixture in _fixtures) if (fixture != null) Object.DestroyImmediate(fixture);
                if (_scene.IsValid() && _scene.isLoaded && Application.isPlaying)
                { var operation = SceneManager.UnloadSceneAsync(_scene); if (operation != null) { Result.status = "RESTORING"; Save(); operation.completed += _ => { Result.restored = true; Complete(status); }; return; } }
                Result.restored = !_scene.IsValid() || !_scene.isLoaded; Complete(status);
            }
            private void Complete(string status) { Result.status = status; _lastInitializationProbe = Result; _initializationProbe = null; Save(); }
            private void Save() { if (string.IsNullOrEmpty(Result.path)) return; Directory.CreateDirectory(Path.GetDirectoryName(Result.path)); File.WriteAllText(Result.path, JsonUtility.ToJson(Result, true)); }
        }
        public static string Stop() { _active?.Finish("STOPPED", null); return Status(); }
        public static string Status()
        {
            Report r = _active != null ? _active.Result : _last;
            if (r == null) return "NOT_STARTED";
            // Keep polling output compact; the complete measured report is saved per completed case.
            return JsonUtility.ToJson(new Progress { status = r.status, error = r.error, directory = r.directory,
                activeCase = r.activeCase, frames = r.frames, cases = r.cases.Count, nonFiniteSamples = r.nonFiniteSamples,
                restored = r.restored, elapsedWallSeconds = r.elapsedWallSeconds });
        }
        [Serializable] private sealed class Progress
        {
            public string status, error, directory, activeCase;
            public int frames, cases, nonFiniteSamples;
            public bool restored;
            public double elapsedWallSeconds;
        }

        private sealed class Session
        {
            public readonly Report Result = new Report();
            private Scene _scene;
            private GameObject _fixture;
            private Camera _camera;
            private PlayerSecondaryMotionRig[] _rigs;
            private Animator[] _animators;
            private readonly Dictionary<Transform, Quaternion> _restRotations = new Dictionary<Transform, Quaternion>();
            private readonly Dictionary<Transform, Quaternion> _gripRotations = new Dictionary<Transform, Quaternion>();
            private readonly List<(GameObject brush, Transform hand, Transform grip, Transform tip, bool authoredProxySource)> _gripBrushes = new List<(GameObject, Transform, Transform, Transform, bool)>();
            private readonly Dictionary<Transform, (Vector3 p, Quaternion q)> _brushRestPoses = new Dictionary<Transform, (Vector3, Quaternion)>();
            private PlayerClothBrushProxyRig[] _brushProxies;
            private PlayerClothCollisionBudgetRig _collisionBudget;
            private PlayerClothBodyProxyRig _bodyProxies;
            private readonly Dictionary<Behaviour, bool> _suspendedReviewPhysics = new Dictionary<Behaviour, bool>();
            private readonly Dictionary<Cloth, bool> _suspendedReviewCloth = new Dictionary<Cloth, bool>();
            private SkinnedMeshRenderer[] _anatomy;
            private Mesh _anatomyBake;
            private PlayerHandGripCorrectives _handCorrectives;
            private readonly Dictionary<Transform, (Vector3 p, Quaternion q)> _rootPoses = new Dictionary<Transform, (Vector3, Quaternion)>();
            private readonly List<Surface> _surfaces = new List<Surface>();
            private readonly List<Recorder> _recorders = new List<Recorder>();
            private readonly List<(int hz, float scale, Pose pose)> _conditions = new List<(int, float, Pose)>();
            private float _oldCapture, _oldTimeScale;
            private double _started, _caseStarted;
            private double _lastNativeFixedTime;
            private long _physicsLatchSequence;
            private int _physicsLatchFrame = -1, _controlFrameAtPhysicsLatch = -1, _physicsLatchCallsThisRenderFrame;
            private double _physicsLatchFixedTime;
            private int _conditionIndex, _controlledFrame = -1, _measuredFrame = -1;
            private int _spawnFrame;
            private bool _savedGlobals, _finished, _recordersAttempted;
            private CaseResult _case;
            private const float WarmupSeconds = 3f;
            private const int SettledFrames = 60;
            private readonly int _tuningMode;
            private bool _tuning => _tuningMode == 1 || _tuningMode == 2;
            public Session(int tuning) { _tuningMode = tuning; }

            public void Start()
            {
                Result.startedAtUtc = DateTimeOffset.UtcNow.ToString("O");
                Result.originalScene = SceneManager.GetActiveScene().path;
                Result.gravityWorld = Physics.gravity;
                Result.directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2",
                    "cloth-" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")));
                Directory.CreateDirectory(Result.directory);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.ValidationPrefab);
                Need(prefab != null, "Static V2 validation prefab is not built.");
                Need(DosaV2SecondaryBuilder.TryResolveClothLayout(prefab.GetComponentsInChildren<Cloth>(true).Select(c => c.name), out _, out string layout, out string layoutError), layoutError);
                Result.clothLayout = layout;
                Need(prefab.GetComponentsInChildren<PlayerSecondaryMotionRig>(true).Length == 2, "Both authored secondary rigs must be serialized in the prefab.");
                Need(prefab.GetComponentsInChildren<Animator>(true).All(a => a.runtimeAnimatorController == null), "Production controllers/clips are not permitted in this fixture.");
                Need(Time.timeScale > 0f, "Game time is paused.");
                Need(Finite(Physics.gravity) && Physics.gravity.sqrMagnitude > 0f, "A nonzero, finite gravity field is required for the requested gravity-settle evidence.");
                Result.prefabSha256 = FileSha(Path.GetFullPath(Path.Combine(Application.dataPath, "..", DosaV2PlayerBuilder.ValidationPrefab)));
                _oldCapture = Time.captureDeltaTime; _oldTimeScale = Time.timeScale; _savedGlobals = true;
                // Only the isolated review scene is allowed above. Suspend its display copy
                // so native profiler markers measure this one disposable character.
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    foreach (var cloth in root.GetComponentsInChildren<Cloth>(true))
                    { _suspendedReviewCloth.Add(cloth, cloth.enabled); cloth.enabled = false; Result.suspendedReviewCloth++; }
                    foreach (var budget in root.GetComponentsInChildren<PlayerClothCollisionBudgetRig>(true))
                    { _suspendedReviewPhysics.Add(budget, budget.enabled); budget.enabled = false; }
                }
                Time.timeScale = 1f;
                _started = Time.realtimeSinceStartupAsDouble;
                EditorApplication.playModeStateChanged += PlayChanged;
                AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
                _scene = SceneManager.CreateScene("DosaV2ClothDiagnostics_" + Guid.NewGuid().ToString("N"));
                // Instantiate directly at its test transform so native Cloth never sees a large post-spawn teleport.
                _fixture = Object.Instantiate(prefab, new Vector3(30f, 0f, 30f), Quaternion.Euler(0f, 23f, 0f));
                _fixture.name = "DosaV2ClothDiagnosticFixture";
                SceneManager.MoveGameObjectToScene(_fixture, _scene);
                foreach (var lod in _fixture.GetComponentsInChildren<PlayerLodClothController>(true)) lod.SuspendForStaticFixture();
                foreach (var camera in _fixture.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                foreach (var listener in _fixture.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
                foreach (var renderer in _fixture.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                { renderer.updateWhenOffscreen = true; renderer.forceMatrixRecalculationPerRender = true; }
                _rigs = _fixture.GetComponentsInChildren<PlayerSecondaryMotionRig>(true);
                // Native Cloth and managed serialized bindings can initialize in different lifecycle
                // callbacks. Freeze only this fresh fixture while allowing Start to finish binding;
                // no simulated free-particle pose is used as the authored rest mapping.
                foreach (var rig in _rigs) rig.SetRepresentationActive(false);
                foreach (var cloth in _fixture.GetComponentsInChildren<Cloth>(true)) cloth.enabled = false;
                _spawnFrame = Time.frameCount;
                EditorApplication.update += AwaitNativeInitialization;
                Result.status = "INITIALIZING_NATIVE"; Save();
            }

            private void AwaitNativeInitialization()
            {
                if (_finished) return;
                Result.nativeInitializationFrames = Time.frameCount - _spawnFrame;
                if (Result.nativeInitializationFrames < 2) return;
                try
                {
                    if (!_rigs.All(r => r.IsConfigured) && Time.realtimeSinceStartupAsDouble - _started < 5d) return;
                    EditorApplication.update -= AwaitNativeInitialization;
                    InitializeNative();
                }
                catch (Pending e) { Finish("WAIT", e.Message); }
                catch (Exception e) { Finish("FAIL", e.GetBaseException().Message); }
            }

            private void InitializeNative()
            {
                Need(_rigs.All(r => r.IsConfigured), "Serialized secondary Configure did not bind on prefab instantiation: " + string.Join("; ", _rigs.Select(r => r.LastBindingError)));
                Need(_rigs.Count(r => r.Diagnostics.CameraRelative) == 1, "Near rig must retain its explicit camera reference in the prefab.");
                foreach (var rig in _rigs) rig.SetRepresentationActive(true);
                _animators = _fixture.GetComponentsInChildren<Animator>(true);
                foreach (var animator in _animators)
                {
                    Need(animator.isHuman && animator.avatar != null && animator.avatar.isValid, "Valid static Humanoid mapping is required.");
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                    {
                        Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                        if (bone != null) _restRotations[bone] = bone.localRotation;
                    }
                }
                foreach (var rig in _rigs) _rootPoses[rig.transform] = (rig.transform.position, rig.transform.rotation);
                foreach (var cloth in _fixture.GetComponentsInChildren<Cloth>(true)) _surfaces.Add(PrepareSurface(cloth));
                _brushProxies = _fixture.GetComponentsInChildren<PlayerClothBrushProxyRig>(true);
                Need(_brushProxies.Length <= 1, "Only the explicit world representation may own brush collision proxies.");
                var worldRig = _rigs.Single(r => !r.Diagnostics.CameraRelative);
                _collisionBudget = worldRig.GetComponent<PlayerClothCollisionBudgetRig>();
                Need(_collisionBudget != null, "Measured native collision capacity requires per-surface assignments.");
                _bodyProxies = worldRig.GetComponent<PlayerClothBodyProxyRig>();
                Need(_bodyProxies != null, "Actual posed arm surface fitting is required.");
                string[] anatomyNames = { "DosaV2_BodyLining", "DosaV2_ArmLining_Left", "DosaV2_ArmLining_Right", "DosaV2_LegLining_Left", "DosaV2_LegLining_Right" };
                _anatomy = anatomyNames.Select(name => worldRig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == name)).ToArray();
                Need(_anatomy.All(r => r.GetComponent<Cloth>() == null && r.sharedMesh != null && r.sharedMesh.isReadable), "Anatomy coverage requires the five real unsimulated inner-lining meshes.");
                _anatomyBake = new Mesh { name = "ClothDiagnosticAnatomyCoverage" };
                _handCorrectives = new PlayerHandGripCorrectives(_animators.SelectMany(a => a.GetComponentsInChildren<Renderer>(true)));
                if (_brushProxies.Length == 0) Result.findings.Add("PENDING: legacy body-only binding has no actual world brush proxy. Brush-collision coverage cannot be submitted to the gate.");
                PrepareGripPose();
                var cameraObject = new GameObject("ClothEvidenceCamera"); SceneManager.MoveGameObjectToScene(cameraObject, _scene);
                _camera = cameraObject.AddComponent<Camera>(); _camera.enabled = false; _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(.18f, .18f, .18f, 1f); _camera.nearClipPlane = .02f; _camera.farClipPlane = 20f;
                _camera.cullingMask = -1;
                _camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(_scene);
                _camera.fieldOfView = 38f;
                if (_tuning)
                {
                    // Rest-only native parameter experiments on this disposable clone.
                    // These are candidates, never new acceptance limits or source-profile writes.
                    for (int i = 0; i < 3; i++) _conditions.Add((60, 1f, Pose.Rest));
                    Result.findings.Add(_tuningMode == 1 ?
                        "TUNING ONLY: fresh reset at each rest condition; native frequency 240Hz, stretching 1, bending 0.05/0.45/1. Source profile and acceptance limits are unchanged. Not a full stability run." :
                        "TUNING ONLY: fresh reset at each rest condition; native frequency 90/240/480Hz, stretching 1, bending 0.45. Source profile and acceptance limits are unchanged. Not a full stability run.");
                }
                else if (_tuningMode == 3)
                {
                    foreach (int hz in new[] { 30, 60, 120 }) _conditions.Add((hz, 1f, Pose.Rest));
                    _conditions.Add((60, .25f, Pose.Rest));
                    Result.findings.Add("REST BASELINE ONLY: actual authored profile and current source-fitted collisions at 30/60/120 and quarter-speed 60. No posed/locomotion stability or RIG_PASS claim.");
                }
                else if (_tuningMode >= 4 && _tuningMode <= 7)
                {
                    foreach (Pose pose in new[] { Pose.Rest, Pose.RaisedArms, Pose.Grip }) _conditions.Add((60, 1f, pose));
                    Result.findings.Add("CAUSE ISOLATION ONLY: mode=" + _tuningMode + "; disable native capsules=" + (_tuningMode != 5) + "; replace backstop0 with100m=" + (_tuningMode != 6) + "; disable tethers and bending=" + (_tuningMode == 7) + ". Actual authored mobility and source assets unchanged; anatomical intersection still measured against original capsules. Not usable as normal collision/rig approval.");
                }
                else
                {
                    if (_tuningMode == 8)
                    {
                        foreach (Pose pose in new[] { Pose.Rest, Pose.RaisedArms, Pose.Grip }) _conditions.Add((60, 1f, pose));
                        Result.findings.Add("CORE PREVIEW ONLY: three principal poses with the authored physics. Not the complete timing, locomotion or RIG_PASS validation suite.");
                    }
                    else foreach (int hz in new[] { 30, 60, 120 }) foreach (Pose pose in Enum.GetValues(typeof(Pose))) _conditions.Add((hz, 1f, pose));
                    if (_tuningMode != 8) foreach (Pose pose in Enum.GetValues(typeof(Pose))) _conditions.Add((60, .25f, pose));
                }
                StartCase(); InstallLoop(); Result.status = "RUNNING"; Save();
            }

            private void StartCase()
            {
                var condition = _conditions[_conditionIndex];
                Time.captureDeltaTime = condition.scale / condition.hz;
                _case = new CaseResult { id = condition.hz + "Hz_" + condition.scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "_" + condition.pose,
                    requestedHz = condition.hz, stepMultiplier = condition.scale, pose = condition.pose.ToString(), requestedDeltaSeconds = condition.scale / condition.hz };
                if (_tuning) _case.id += "_Tuning" + _tuningMode + "_" + _conditionIndex;
                if (_tuningMode >= 4 && _tuningMode <= 7) _case.id += "_ConstraintIsolation" + _tuningMode;
                if (_tuningMode == 8) _case.id += "_CorePreview";
                Result.activeCase = _case.id; _caseStarted = Time.realtimeSinceStartupAsDouble;
                _lastNativeFixedTime = Time.fixedTimeAsDouble;
                _physicsLatchFrame = -1; _controlFrameAtPhysicsLatch = -1; _physicsLatchCallsThisRenderFrame = 0;
                foreach (var renderer in _anatomy) _case.anatomyCoverage.Add(new AnatomyCoverage { sourceRenderer = renderer.name, vertices = renderer.sharedMesh.vertexCount });
                _case.actualBrushProxies = _brushProxies.Sum(p => p.Capsules.Length);
                foreach (Surface surface in _surfaces)
                {
                    surface.HasPrevious = false;
                    surface.PreviousExpectedWorld = null;
                    surface.HasPhysicsLatch = false;
                    _case.surfaces.Add(new SurfaceResult { name = surface.Cloth.name, particles = surface.Coefficients.Length,
                        pinnedParticles = surface.Coefficients.Count(c => c.maxDistance == 0f), edgeCount = surface.Edges.Count });
                }
                _case.resetCountBefore = _rigs.Sum(r => r.Diagnostics.Resets);
                RestorePose(); foreach (var rig in _rigs) rig.ResetMotion(SecondaryMotionResetReason.Manual);
                foreach (var surface in _surfaces)
                {
                    var c = surface.Cloth;
                    if (_tuning)
                    {
                        c.enabled = false;
                        c.clothSolverFrequency = _tuningMode == 1 ? 240f : new[] { 90f, 240f, 480f }[_conditionIndex];
                        c.stretchingStiffness = 1f;
                        c.bendingStiffness = _tuningMode == 1 ? new[] { .05f, .45f, 1f }[_conditionIndex] : .45f;
                        c.ClearTransformMotion(); c.enabled = true;
                    }
                    ApplyIsolationSettings(surface);
                    var actualCoefficients = c.coefficients;
                    _case.solverSettings.Add(new SolverSettings { cloth = c.name, frequency = c.clothSolverFrequency,
                        stretching = c.stretchingStiffness, bending = c.bendingStiffness, damping = c.damping,
                        tethers = c.useTethers, continuous = c.enableContinuousCollision,
                        minimumBackstop = actualCoefficients.Min(value => value.collisionSphereDistance),
                        maximumBackstop = actualCoefficients.Max(value => value.collisionSphereDistance) });
                }
                _case.resetCountAfter = _rigs.Sum(r => r.Diagnostics.Resets);
                _case.resetObserved = _case.resetCountAfter > _case.resetCountBefore;
            }

            private void ApplyIsolationSettings(Surface surface)
            {
                if (_tuningMode < 4 || _tuningMode > 7) return;
                var cloth = surface.Cloth;
                if (_tuningMode != 6)
                {
                    var coefficients = cloth.coefficients;
                    if (coefficients.Any(value => value.collisionSphereDistance != 100f))
                    {
                        for (int i = 0; i < coefficients.Length; i++) coefficients[i].collisionSphereDistance = 100f;
                        cloth.coefficients = coefficients;
                    }
                }
                if (_tuningMode != 5)
                {
                    cloth.capsuleColliders = Array.Empty<CapsuleCollider>();
                    cloth.sphereColliders = Array.Empty<ClothSphereColliderPair>();
                }
                if (_tuningMode == 7)
                {
                    cloth.useTethers = false;
                    cloth.bendingStiffness = 0f;
                }
            }

            private void LatchPhysicsAnchors()
            {
                if (_finished || _case == null) return;
                try
                {
                    // No Time.frameCount guard: Unity may execute several fixed
                    // steps in one render frame. Record each actual entry point.
                    int frame = Time.frameCount;
                    _physicsLatchCallsThisRenderFrame = frame == _physicsLatchFrame ? _physicsLatchCallsThisRenderFrame + 1 : 1;
                    _physicsLatchFrame = frame; _controlFrameAtPhysicsLatch = _controlledFrame;
                    _physicsLatchFixedTime = Time.fixedTimeAsDouble; _physicsLatchSequence++;
                    foreach (var surface in _surfaces)
                    {
                        for (int i = 0; i < surface.Bones.Length; i++)
                            surface.Skin[i] = surface.Bones[i].localToWorldMatrix * surface.Bindposes[i];
                        for (int p = 0; p < surface.ParticleSource.Length; p++)
                        {
                            if (surface.Coefficients[p].maxDistance != 0f) continue;
                            Vector3 point = SkinSourcePoint(surface, surface.ParticleSource[p]);
                            Need(Finite(point), surface.Cloth.name + ": pre-physics pinned anchor is nonfinite.");
                            surface.PhysicsExpectedWorld[p] = point;
                        }
                        surface.PhysicsRepresentationToWorld = surface.Representation.localToWorldMatrix;
                        surface.HasPhysicsLatch = true;
                    }
                    _case.physicsLatchCalls++;
                    _case.physicsLatchTiming.Add(new PhysicsLatchTiming { sequence = _physicsLatchSequence,
                        frame = frame, controlFrame = _controlledFrame, fixedTime = _physicsLatchFixedTime,
                        time = Time.timeAsDouble, caseGameSeconds = _case.actualGameSeconds,
                        beforeCurrentRenderControl = _controlledFrame != frame });
                }
                catch (Exception e) { Finish("FAIL", e.GetBaseException().Message); }
            }

            private static Vector3 SkinSourcePoint(Surface surface, int source)
            {
                Vector3 point = Vector3.zero;
                for (int w = surface.Offsets[source]; w < surface.Offsets[source] + surface.WeightCounts[source]; w++)
                {
                    BoneWeight1 weight = surface.Weights[w];
                    point += surface.Skin[weight.boneIndex].MultiplyPoint3x4(surface.Rest[source]) * weight.weight;
                }
                return point;
            }

            private void Control()
            {
                if (_finished || _controlledFrame == Time.frameCount) return;
                _controlledFrame = Time.frameCount;
                try
                {
                    if (Time.realtimeSinceStartupAsDouble - _started > 900) { Finish("STOPPED", "900s wall-time safety limit; incomplete conditions are not certified."); return; }
                    if (!_recordersAttempted && Result.frames >= 3) StartRecorders();
                    RestorePose();
                    _handCorrectives.Apply(0f, 0f);
                    Pose pose = _conditions[_conditionIndex].pose;
                    float t = _case.actualGameSeconds;
                    if (pose == Pose.RaisedArms)
                    {
                        float weight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / .2f));
                        foreach (var animator in _animators)
                        {
                            Rotate(animator, HumanBodyBones.LeftUpperArm, animator.transform.forward, -45f * weight);
                            Rotate(animator, HumanBodyBones.RightUpperArm, animator.transform.forward, 45f * weight);
                            Rotate(animator, HumanBodyBones.LeftLowerArm, animator.transform.up, 35f * weight);
                            Rotate(animator, HumanBodyBones.RightLowerArm, animator.transform.up, -35f * weight);
                        }
                    }
                    if (pose == Pose.Grip)
                    {
                        float weight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / .2f));
                        foreach (var animator in _animators)
                        {
                            // Direct preparation pose from the imported T pose; no animation/controller.
                            Rotate(animator, HumanBodyBones.LeftUpperArm, animator.transform.forward, 55f * weight);
                            Rotate(animator, HumanBodyBones.RightUpperArm, animator.transform.forward, -55f * weight);
                            Rotate(animator, HumanBodyBones.LeftLowerArm, animator.transform.up, 65f * weight);
                            Rotate(animator, HumanBodyBones.RightLowerArm, animator.transform.up, -65f * weight);
                        }
                        foreach (var entry in _gripRotations) entry.Key.localRotation = _restRotations[entry.Key] * Quaternion.Slerp(Quaternion.identity, entry.Value, weight);
                        _handCorrectives.Apply(weight, weight);
                    }
                    foreach (var brush in _gripBrushes)
                    {
                        if (!brush.authoredProxySource) brush.brush.SetActive(pose == Pose.Grip);
                        if (pose == Pose.Grip) AlignGripBrush(brush);
                    }
                    Vector3 wind = Vector3.zero;
                    if (pose == Pose.Impulse && t < .3f)
                    {
                        float pulse = Mathf.Sin(Mathf.PI * t / .3f);
                        foreach (var rig in _rigs)
                        {
                            var original = _rootPoses[rig.transform];
                            rig.transform.position = original.p + Vector3.right * (.08f * pulse);
                            rig.transform.rotation = Quaternion.AngleAxis(10f * pulse, Vector3.up) * original.q;
                        }
                        wind = Vector3.right * (3f * pulse);
                    }
                    long before = Stopwatch.GetTimestamp();
                    foreach (var rig in _rigs)
                    {
                        rig.Evaluate(Time.deltaTime, wind);
                        _case.peakSecondarySwingDegrees = Mathf.Max(_case.peakSecondarySwingDegrees, rig.Diagnostics.LargestSwingDegrees);
                    }
                    _case.secondaryEvaluateCpu.Add(Milliseconds(before));
                    Need(_bodyProxies.RefreshNow(), "Actual skinned body proxy refresh: " + _bodyProxies.LastError);
                    _case.bodyProxyRefreshCpu.Add(_bodyProxies.LastRefreshMilliseconds);
                    _case.bodyProxyMaximumAllocatedBytes = Math.Max(_case.bodyProxyMaximumAllocatedBytes, _bodyProxies.LastRefreshAllocatedBytes);
                    before = Stopwatch.GetTimestamp();
                    foreach (var proxy in _brushProxies) Need(proxy.RefreshNow(), "Actual world brush proxy refresh: " + proxy.LastError);
                    _case.brushProxyRefreshCpu.Add(Milliseconds(before));
                    Need(_collisionBudget.RefreshNow(), "Cloth collision selection: " + _collisionBudget.LastError);
                    _case.collisionSelectionCpu.Add(_collisionBudget.LastRefreshMilliseconds);
                    _case.collisionSelectionMaximumAllocatedBytes = Math.Max(_case.collisionSelectionMaximumAllocatedBytes, _collisionBudget.LastRefreshAllocatedBytes);
                    foreach (var surface in _surfaces) ApplyIsolationSettings(surface);
                }
                catch (Exception e) { Finish("FAIL", e.GetBaseException().Message); }
            }

            private void Measure()
            {
                if (_finished || _measuredFrame == Time.frameCount || _controlledFrame != Time.frameCount) return;
                _measuredFrame = Time.frameCount;
                try
                {
                    float dt = Time.deltaTime;
                    Need(Finite(dt) && dt > 0f, "Native Cloth condition has no positive game-time step.");
                    double fixedNow = Time.fixedTimeAsDouble;
                    double nativeElapsed = fixedNow - _lastNativeFixedTime;
                    Need(nativeElapsed >= 0d && double.IsFinite(nativeElapsed), "Native physics clock moved backward/nonfinite.");
                    bool nativeSolved = nativeElapsed > 0d;
                    float nativeDt = (float)nativeElapsed;
                    long before = Stopwatch.GetTimestamp();
                    // A just-reset Cloth has not yet produced a simulated particle
                    // frame if the fixed clock did not advance. Its known pre-solve
                    // renderer-local buffer is not representation-local evidence.
                    if (nativeSolved || _case.nativeSolvedSamples > 0)
                        for (int i = 0; i < _surfaces.Count; i++) MeasureSurface(_surfaces[i], _case.surfaces[i], dt, nativeDt);
                    if (nativeSolved) { _case.nativeSolvedSamples++; _case.nativeSimulatedSeconds += nativeElapsed; _lastNativeFixedTime = fixedNow; }
                    else _case.framesWithoutNativeStep++;
                    MeasureAnatomyCoverage();
                    _case.diagnosticCpu.Add(Milliseconds(before));
                    foreach (var recorder in _recorders) if (recorder.Value.Valid && recorder.Value.Count > 0)
                        recorder.Result.observedCpu.Add(recorder.Value.LastValue / 1000000d);
                    _case.frames++; Result.frames++; _case.actualGameSeconds += dt; _case.actualUnscaledSeconds += Time.unscaledDeltaTime;
                    _case.minimumActualDelta = Mathf.Min(_case.minimumActualDelta, dt); _case.maximumActualDelta = Mathf.Max(_case.maximumActualDelta, dt);
                    Result.elapsedWallSeconds = Time.realtimeSinceStartupAsDouble - _started;
                    _case.settledFrames = _case.surfaces.Min(surface => surface.settledFrames);
                    if (!HasSettleCoverage((float)Math.Min(_case.actualGameSeconds, _case.nativeSimulatedSeconds), _case.settledFrames)) return;
                    _case.actualWallSeconds = Time.realtimeSinceStartupAsDouble - _caseStarted;
                    CaptureCase(); Result.cases.Add(_case); Save();
                    _conditionIndex++;
                    if (_conditionIndex >= _conditions.Count) Finish(Result.nonFiniteSamples == 0 ? "COMPLETE_MEASURED" : "FAIL", null);
                    else StartCase();
                }
                catch (Exception e) { Finish("FAIL", e.GetBaseException().Message); }
            }

            private void MeasureSurface(Surface surface, SurfaceResult result, float dt, float nativeDt)
            {
                Vector3[] particles = surface.Cloth.vertices;
                Need(DosaV2SecondaryBuilder.TryGetParticleFrame(surface.Renderer, surface.Representation, out _, out string frameError), frameError);
                Need(particles.Length == surface.ParticleSource.Length, surface.Cloth.name + ": native particle count changed.");
                // Raw live-Cloth output is a coordinate/index diagnostic, not a
                // trusted displayed position until its frame is independently
                // established. It never becomes the independent skin anchor.
                surface.Renderer.BakeMesh(surface.LiveOutputBake, false);
                surface.LiveOutputBake.GetVertices(surface.LiveOutputVertices);
                Need(surface.LiveOutputVertices.Count == surface.Rest.Length,
                    surface.Cloth.name + ": live Cloth BakeMesh vertex count differs; no index fallback is valid.");
                Matrix4x4 renderedToWorld = surface.Renderer.localToWorldMatrix;
                for (int i = 0; i < surface.Bones.Length; i++) surface.Skin[i] = surface.Bones[i].localToWorldMatrix * surface.Bindposes[i];
                var world = new Vector3[particles.Length];
                var expectedWorld = new Vector3[particles.Length];
                var proxies = surface.Representation.GetComponentsInChildren<CapsuleCollider>(true).Where(proxy => PlayerSecondaryMotionRig.ValidateProxy(proxy, out _)).Select(proxy =>
                {
                    CapsuleWorld(proxy, out Vector3 a, out Vector3 b, out float radius);
                    return (proxy, a, b, radius);
                }).ToArray();
                bool nativeSolved = nativeDt > 0f;
                bool settled = nativeSolved && _case.actualGameSeconds >= WarmupSeconds && _case.nativeSimulatedSeconds >= WarmupSeconds;
                float currentPinError = 0f, previousPinError = 0f, physicsPinError = 0f, physicsFramePinError = 0f;
                float liveNativeError = 0f, liveCurrentError = 0f, livePhysicsError = 0f;
                for (int p = 0; p < particles.Length; p++)
                {
                    // Native output changes frame on its first solver update: the
                    // pre-solve serialized mesh is renderer-local, simulated particles
                    // are representation-local. The 80-observation initialization
                    // probe compares original source indices before/after actual solve.
                    Vector3 point = surface.Representation.TransformPoint(particles[p]); world[p] = point;
                    if (!Finite(point)) { result.nonFiniteSamples++; Result.nonFiniteSamples++; continue; }
                    if (!result.hasBounds) { result.boundsMin = result.boundsMax = point; result.hasBounds = true; }
                    else { result.boundsMin = Vector3.Min(result.boundsMin, point); result.boundsMax = Vector3.Max(result.boundsMax, point); }
                    int source = surface.ParticleSource[p]; Vector3 expected = SkinSourcePoint(surface, source);
                    expectedWorld[p] = expected;
                    float delta = Vector3.Distance(point, expected); bool pinned = surface.Coefficients[p].maxDistance == 0f;
                    if (!Finite(expected) || !Finite(delta)) { result.nonFiniteSamples++; Result.nonFiniteSamples++; continue; }
                    if (pinned) result.maxPinnedDistanceMeters = Mathf.Max(result.maxPinnedDistanceMeters, delta);
                    if (pinned)
                    {
                        currentPinError = Mathf.Max(currentPinError, delta);
                        if (surface.PreviousExpectedWorld != null) previousPinError = Mathf.Max(previousPinError, Vector3.Distance(point, surface.PreviousExpectedWorld[p]));
                        Vector3 rendered = renderedToWorld.MultiplyPoint3x4(surface.LiveOutputVertices[source]);
                        Need(Finite(rendered), surface.Cloth.name + ": live Cloth baked pinned output is nonfinite.");
                        liveNativeError = Mathf.Max(liveNativeError, Vector3.Distance(rendered, point));
                        liveCurrentError = Mathf.Max(liveCurrentError, Vector3.Distance(rendered, expected));
                        Vector3 rawBaked = surface.LiveOutputVertices[source];
                        result.maxLiveBakedRepresentationFrameToNativeMeters = Mathf.Max(result.maxLiveBakedRepresentationFrameToNativeMeters,
                            Vector3.Distance(surface.Representation.TransformPoint(rawBaked), point));
                        if (surface.Renderer.rootBone != null)
                            result.maxLiveBakedRootBoneFrameToNativeMeters = Mathf.Max(result.maxLiveBakedRootBoneFrameToNativeMeters,
                                Vector3.Distance(surface.Renderer.rootBone.TransformPoint(rawBaked), point));
                        if (surface.HasPhysicsLatch)
                        {
                            Vector3 latched = surface.PhysicsExpectedWorld[p];
                            physicsPinError = Mathf.Max(physicsPinError, Vector3.Distance(point, latched));
                            physicsFramePinError = Mathf.Max(physicsFramePinError, Vector3.Distance(
                                surface.PhysicsRepresentationToWorld.MultiplyPoint3x4(particles[p]), latched));
                            livePhysicsError = Mathf.Max(livePhysicsError, Vector3.Distance(rendered, latched));
                        }
                    }
                    if (pinned && settled) result.maxSettledPinnedDistanceMeters = Mathf.Max(result.maxSettledPinnedDistanceMeters, delta);
                    result.maxAuthoredEnvelopeExcessMeters = Mathf.Max(result.maxAuthoredEnvelopeExcessMeters, Mathf.Max(0f, delta - surface.Coefficients[p].maxDistance));
                    if (settled && surface.HasPrevious) result.maxSettledSpeedMetersPerSecond = Mathf.Max(result.maxSettledSpeedMetersPerSecond, Vector3.Distance(point, surface.PreviousWorld[p]) / nativeDt);
                    foreach (var proxy in proxies)
                    {
                        // A pinned particle can be pushed out by collision while its authored anchor
                        // remains inside the proxy. Record that incompatibility independently from
                        // the post-simulation particle penetration so a small residual cannot hide it.
                        float anchorDepth = pinned ? PointCapsulePenetration(expected, proxy.a, proxy.b, proxy.radius) : 0f;
                        if (anchorDepth > 0f)
                        {
                            result.pinnedAnchorProxySamples++;
                            if (anchorDepth > result.maxPinnedAnchorProxyPenetrationMeters)
                            {
                                result.maxPinnedAnchorProxyPenetrationMeters = anchorDepth;
                                RecordPenetration(new Penetration { surface = surface.Cloth.name, proxy = proxy.proxy.name,
                                    particle = p, pinned = true, depthMeters = anchorDepth, position = expected, measurement = "authored_skinned_anchor" });
                            }
                        }
                        float penetration = PointCapsulePenetration(point, proxy.a, proxy.b, proxy.radius);
                        if (penetration <= 0f) continue;
                        if (pinned) result.pinnedProxySamples++; else result.freeProxySamples++;
                        if (penetration > result.maxProxyPenetrationMeters)
                        {
                            result.maxProxyPenetrationMeters = penetration;
                            RecordPenetration(new Penetration { surface = surface.Cloth.name, proxy = proxy.proxy.name, particle = p, pinned = pinned, depthMeters = penetration, position = point });
                        }
                        if (!pinned)
                        {
                            result.maxFreeProxyPenetrationMeters = Mathf.Max(result.maxFreeProxyPenetrationMeters, penetration);
                            if (proxy.proxy.name.Contains("_Brush_")) { result.brushFreePenetrationSamples++; result.maxBrushFreePenetrationMeters = Mathf.Max(result.maxBrushFreePenetrationMeters, penetration); }
                            else { result.bodyFreePenetrationSamples++; result.maxBodyFreePenetrationMeters = Mathf.Max(result.maxBodyFreePenetrationMeters, penetration); }
                        }
                    }
                }
                result.liveBakedOutputFrames++;
                result.maxLiveBakedPinnedToNativeMeters = Mathf.Max(result.maxLiveBakedPinnedToNativeMeters, liveNativeError);
                result.maxLiveBakedPinnedToCurrentAnchorMeters = Mathf.Max(result.maxLiveBakedPinnedToCurrentAnchorMeters, liveCurrentError);
                if (surface.HasPhysicsLatch)
                {
                    result.physicsLatchedMeasuredFrames++;
                    result.maxPhysicsLatchedPinnedDistanceMeters = Mathf.Max(result.maxPhysicsLatchedPinnedDistanceMeters, physicsPinError);
                    result.maxPhysicsFramePinnedDistanceMeters = Mathf.Max(result.maxPhysicsFramePinnedDistanceMeters, physicsFramePinError);
                    result.maxLiveBakedPinnedToPhysicsLatchMeters = Mathf.Max(result.maxLiveBakedPinnedToPhysicsLatchMeters, livePhysicsError);
                    if (settled) result.maxSettledPhysicsLatchedPinnedDistanceMeters = Mathf.Max(result.maxSettledPhysicsLatchedPinnedDistanceMeters, physicsPinError);
                }
                foreach (var edge in surface.Edges)
                    if (Finite(world[edge.a]) && Finite(world[edge.b]))
                    {
                        float length = Vector3.Distance(world[edge.a], world[edge.b]), ratio = length / edge.length;
                        float skinLength = Vector3.Distance(expectedWorld[edge.a], expectedWorld[edge.b]);
                        float mobility = (surface.Coefficients[edge.a].maxDistance + surface.Coefficients[edge.b].maxDistance) * surface.Representation.lossyScale.x;
                        result.maxSkinnedRestEdgeStretchRatio = Mathf.Max(result.maxSkinnedRestEdgeStretchRatio, skinLength / edge.length);
                        result.minimumRequiredRestEdgeStretchRatio = Mathf.Max(result.minimumRequiredRestEdgeStretchRatio, Mathf.Max(0f, skinLength - mobility) / edge.length);
                        if (settled) result.maxSettledRestEdgeStretchRatio = Mathf.Max(result.maxSettledRestEdgeStretchRatio, ratio);
                        if (ratio > result.maxRestEdgeStretchRatio)
                        {
                            result.maxRestEdgeStretchRatio = ratio;
                            result.worstEdge = new EdgeEvidence { particleA = edge.a, particleB = edge.b, sourceA = surface.ParticleSource[edge.a], sourceB = surface.ParticleSource[edge.b],
                                pinnedA = surface.Coefficients[edge.a].maxDistance == 0f, pinnedB = surface.Coefficients[edge.b].maxDistance == 0f,
                                restLengthMeters = edge.length, actualLengthMeters = length, ratio = ratio, positionA = world[edge.a], positionB = world[edge.b],
                                skinAnchorA = expectedWorld[edge.a], skinAnchorB = expectedWorld[edge.b],
                                sourceRestA = surface.Rest[surface.ParticleSource[edge.a]], sourceRestB = surface.Rest[surface.ParticleSource[edge.b]],
                                mobilityA = surface.Coefficients[edge.a].maxDistance, mobilityB = surface.Coefficients[edge.b].maxDistance };
                        }
                    }
                var omission = _collisionBudget.AuditActualParticles(surface.Cloth, world);
                result.maximumOmittedPenetrationMeters = Mathf.Max(result.maximumOmittedPenetrationMeters, omission.maximumOmittedPenetrationMeters);
                result.omittedParticleSamples += omission.penetratingParticles;
                var selection = _collisionBudget.Selections.Single(s => s.cloth == surface.Cloth.name);
                result.omittedPotentialMaximum = Math.Max(result.omittedPotentialMaximum, selection.omittedPotentialCandidates);
                if (HasSettleCoverage((float)Math.Min(_case.actualGameSeconds + dt, _case.nativeSimulatedSeconds + nativeDt), result.settledFrames + (settled ? 1 : 0)))
                    result.finalCollisionSelection = JsonUtility.FromJson<PlayerClothCollisionBudgetRig.SurfaceSelection>(JsonUtility.ToJson(_collisionBudget.SnapshotSelections().Single(s => s.cloth == surface.Cloth.name)));
                if (nativeSolved) { surface.PreviousWorld = world; surface.HasPrevious = true; result.nativeSolvedSamples++; }
                if (_case.actualGameSeconds <= .4f || settled) result.pinTiming.Add(new PinnedTiming {
                    frame = Time.frameCount, gameSeconds = _case.actualGameSeconds,
                    nativeSolved = nativeSolved, currentAnchorErrorMeters = currentPinError,
                    previousAnchorErrorMeters = previousPinError, previousAvailable = surface.PreviousExpectedWorld != null,
                    physicsLatchAvailable = surface.HasPhysicsLatch, physicsLatchSequence = _physicsLatchSequence,
                    physicsLatchFrame = _physicsLatchFrame, controlFrameAtPhysicsLatch = _controlFrameAtPhysicsLatch,
                    physicsLatchBeforeControl = surface.HasPhysicsLatch && _controlFrameAtPhysicsLatch != _physicsLatchFrame,
                    physicsLatchSameRenderFrame = surface.HasPhysicsLatch && _physicsLatchFrame == Time.frameCount,
                    physicsLatchCallsThisRenderFrame = _physicsLatchFrame == Time.frameCount ? _physicsLatchCallsThisRenderFrame : 0,
                    measuredFixedTime = Time.fixedTimeAsDouble, physicsLatchFixedTime = _physicsLatchFixedTime,
                    physicsLatchedAnchorErrorMeters = physicsPinError, physicsFrameAnchorErrorMeters = physicsFramePinError,
                    liveBakeAvailable = true, liveBakedToNativeErrorMeters = liveNativeError,
                    liveBakedToCurrentAnchorErrorMeters = liveCurrentError, liveBakedToPhysicsLatchErrorMeters = livePhysicsError });
                surface.PreviousExpectedWorld = expectedWorld;
                result.measuredFrames++;
                if (settled) result.settledFrames++;
            }

            private void RecordPenetration(Penetration sample)
            {
                _case.worstParticlePenetrations.Add(sample);
                _case.worstParticlePenetrations = _case.worstParticlePenetrations.OrderByDescending(v => v.depthMeters).Take(24).ToList();
            }

            private void MeasureAnatomyCoverage()
            {
                var proxies = _collisionBudget.GetComponentsInChildren<CapsuleCollider>(true)
                    .Where(p => PlayerSecondaryMotionRig.ValidateProxy(p, out _) && !p.name.Contains("_Brush_")).Select(p =>
                { CapsuleWorld(p, out Vector3 a, out Vector3 b, out float radius); return (a, b, radius); }).ToArray();
                Need(proxies.Length > 0, "No body anatomy capsules are bound.");
                for (int i = 0; i < _anatomy.Length; i++)
                {
                    var renderer = _anatomy[i]; renderer.BakeMesh(_anatomyBake, false);
                    Need(_anatomyBake.vertexCount == renderer.sharedMesh.vertexCount, "Actual anatomy source vertex count changed.");
                    var result = _case.anatomyCoverage[i]; result.measuredFrames++;
                    foreach (Vector3 local in _anatomyBake.vertices)
                    {
                        Vector3 point = renderer.transform.TransformPoint(local); Need(Finite(point), "Nonfinite actual inner-body skin vertex.");
                        float outside = float.PositiveInfinity;
                        foreach (var proxy in proxies)
                        {
                            Vector3 axis = proxy.b - proxy.a; float t = axis.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(point - proxy.a, axis) / axis.sqrMagnitude) : 0f;
                            outside = Mathf.Min(outside, Vector3.Distance(point, proxy.a + axis * t) - proxy.radius);
                        }
                        if (outside > .00001f) result.uncoveredVertexSamples++;
                        result.maxOutsideMeters = Mathf.Max(result.maxOutsideMeters, outside);
                    }
                }
            }

            private Surface PrepareSurface(Cloth cloth)
            {
                var renderer = cloth.GetComponent<SkinnedMeshRenderer>();
                Need(cloth.enabled && cloth.gameObject.activeInHierarchy && cloth.useGravity, cloth.name + ": enabled world Cloth with gravity is required for native settling measurement.");
                Need(renderer != null && renderer.sharedMesh != null && renderer.sharedMesh.isReadable, cloth.name + ": readable skinned mesh is required.");
                var mesh = renderer.sharedMesh;
                var owner = cloth.GetComponentInParent<PlayerSecondaryMotionRig>();
                Need(owner != null, cloth.name + ": explicit representation owner is missing.");
                Need(DosaV2SecondaryBuilder.TryGetParticleFrame(renderer, owner.transform, out Matrix4x4 bakedToParticle, out string frameError), frameError);
                var surface = new Surface { Cloth = cloth, Renderer = renderer, Rest = mesh.vertices, Bones = renderer.bones,
                    Representation = owner.transform, Bindposes = mesh.bindposes, Coefficients = cloth.coefficients, Proxies = cloth.capsuleColliders };
                Need(surface.Coefficients.Any(c => c.maxDistance == 0f) && surface.Coefficients.Any(c => c.maxDistance > 0f), cloth.name + ": authored pins/free particles are required.");
                Need(surface.Proxies.Length > 0 && surface.Proxies.All(p => PlayerSecondaryMotionRig.ValidateProxy(p, out _)), cloth.name + ": explicit representation-only proxies are required.");
                Vector3[] native = cloth.vertices;
                // A separate, non-Cloth SMR bakes the original skin in the same transform. Baking the live
                // Cloth renderer would read simulation output and make the anchor comparison circular.
                // The initialization probe separated persisted Play particles from freshly-added Edit
                // Cloth snapshots. Accept this runtime renderer-local contract only when ALL native
                // particles and source vertices map at the original 10um tolerance; no fallback frame.
                Vector3[] bakedRest = BakeUnsimulatedRest(renderer);
                bool mapped = DosaV2SecondaryBuilder.TryMapAuthoredCoefficients(bakedRest,
                    DosaV2SecondaryBuilder.GetAuthoredClothValues(mesh, Result.clothLayout == "LEGACY_SPLIT_COAT_8"), native, out var authored, out var mapping);
                Result.restMappings.Add(new RestMappingResult { surface = cloth.name, allMatched = mapped, mapping = mapping,
                    nativeParticle0 = native.Length > 0 ? native[0] : Vector3.zero, bakedVertex0 = bakedRest.Length > 0 ? bakedRest[0] : Vector3.zero });
                Need(mapped, cloth.name + ": runtime renderer-local baked rest particle mapping failed: " + mapping.error);
                Need(authored.Length == surface.Coefficients.Length && authored.Select(c => c.maxDistance).SequenceEqual(surface.Coefficients.Select(c => c.maxDistance)), cloth.name + ": serialized coefficients differ from exact authored mobility (UV3 for V2, explicit colors only for legacy).");
                using (var counts = mesh.GetBonesPerVertex()) surface.WeightCounts = counts.ToArray();
                using (var weights = mesh.GetAllBoneWeights()) surface.Weights = weights.ToArray();
                Need(surface.WeightCounts.Length == surface.Rest.Length && surface.Bindposes.Length == surface.Bones.Length && surface.Bones.All(b => b != null), cloth.name + ": full skin binding stream is invalid.");
                surface.Offsets = new int[surface.Rest.Length]; int offset = 0;
                for (int i = 0; i < surface.Offsets.Length; i++) { surface.Offsets[i] = offset; offset += surface.WeightCounts[i]; }
                Need(offset == surface.Weights.Length && surface.Weights.All(w => w.boneIndex >= 0 && w.boneIndex < surface.Bones.Length && Finite(w.weight) && w.weight >= 0f), cloth.name + ": full skin stream is malformed.");
                surface.Skin = new Matrix4x4[surface.Bones.Length];
                surface.ParticleSource = MapRestParticles(bakedRest, native);
                surface.PhysicsExpectedWorld = new Vector3[native.Length];
                var sourceParticle = new int[surface.Rest.Length];
                var particlesByPosition = new Dictionary<Vector3, int>();
                for (int p = 0; p < native.Length; p++) particlesByPosition[native[p]] = p;
                for (int i = 0; i < sourceParticle.Length; i++)
                {
                    if (!particlesByPosition.TryGetValue(bakedRest[i], out int p)) p = Nearest(native, bakedRest[i]);
                    Need(p >= 0, cloth.name + ": source topology cannot map to native particles."); sourceParticle[i] = p;
                    int canonical = surface.ParticleSource[p];
                    Need(SameWeights(surface, i, canonical), cloth.name + ": welded duplicates have different skin weights; anchored position is ambiguous.");
                }
                var edges = new HashSet<(int, int)>(); int[] triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                    for (int side = 0; side < 3; side++)
                    { int a = sourceParticle[triangles[i + side]], b = sourceParticle[triangles[i + (side + 1) % 3]]; if (a != b) edges.Add(a < b ? (a, b) : (b, a)); }
                surface.Edges = edges.Select(e => (e.Item1, e.Item2, Vector3.Distance(renderer.transform.TransformPoint(native[e.Item1]), renderer.transform.TransformPoint(native[e.Item2])))).Where(e => e.Item3 > .000001f).ToList();
                surface.LiveOutputBake = new Mesh { name = "ActualClothOutput_" + cloth.name };
                return surface;
            }

            private static Vector3[] BakeUnsimulatedRest(SkinnedMeshRenderer source)
            {
                var obj = new GameObject("UnsimulatedSkinRest_Temporary");
                var mesh = new Mesh { name = "UnsimulatedSkinRest_Temporary" };
                try
                {
                    obj.transform.SetParent(source.transform.parent, false);
                    obj.transform.localPosition = source.transform.localPosition;
                    obj.transform.localRotation = source.transform.localRotation;
                    obj.transform.localScale = source.transform.localScale;
                    var renderer = obj.AddComponent<SkinnedMeshRenderer>(); renderer.enabled = false;
                    renderer.sharedMesh = source.sharedMesh; renderer.bones = source.bones; renderer.rootBone = source.rootBone;
                    renderer.quality = source.quality; renderer.BakeMesh(mesh, false);
                    Need(mesh.vertexCount == source.sharedMesh.vertexCount, "Unsimulated BakeMesh changed source vertex indexing.");
                    return mesh.vertices;
                }
                finally { Object.DestroyImmediate(obj); Object.DestroyImmediate(mesh); }
            }

            private void PrepareGripPose()
            {
                Result.gripCalibrationPath = "Art/PlayerV2/Calibration/unity-grip-offsets.json";
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../..", Result.gripCalibrationPath));
                Need(File.Exists(path), "Accepted imported static hand calibration is missing.");
                Result.gripCalibrationSha256 = FileSha(path);
                var file = JsonUtility.FromJson<GripOffsets>(File.ReadAllText(path));
                Need(file != null && file.offsets != null && file.offsets.Length == 30 && file.offsets.Select(o => o.name).Distinct().Count() == 30,
                    "Expected the complete 30-bone imported two-hand static grip calibration.");
                var brushAsset = AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.BrushModel);
                Need(brushAsset != null, "The current imported V2 brush is required for the static grip pose.");
                foreach (var animator in _animators)
                {
                    Transform[] bones = animator.GetComponentsInChildren<Transform>(true);
                    foreach (var offset in file.offsets)
                    {
                        var matches = bones.Where(b => b.name == offset.name).ToArray();
                        Need(matches.Length == 1 && _restRotations.ContainsKey(matches[0]), "Static calibration requires one mapped finger bone: " + offset.name);
                        float length = Mathf.Sqrt(offset.offset.x * offset.offset.x + offset.offset.y * offset.offset.y + offset.offset.z * offset.offset.z + offset.offset.w * offset.offset.w);
                        Need(Finite(length) && Mathf.Abs(length - 1f) <= .0001f, "Static calibration has an invalid quaternion: " + offset.name);
                        _gripRotations.Add(matches[0], offset.offset);
                    }
                    var hand = bones.SingleOrDefault(b => b.name == "RightBrushGrip"); Need(hand != null, "The authored RightBrushGrip is missing.");
                    var proxyOwner = _brushProxies.SingleOrDefault(p => p.transform == animator.transform || animator.transform.IsChildOf(p.transform));
                    bool authoredProxySource = proxyOwner != null;
                    var brush = authoredProxySource ? proxyOwner.WorldBrush : Object.Instantiate(brushAsset);
                    if (authoredProxySource) _brushRestPoses.Add(brush.transform, (brush.transform.position, brush.transform.rotation));
                    else { brush.name = "StaticGripBrush_" + animator.name; brush.transform.SetParent(_fixture.transform, true); }
                    Need(brush.GetComponentsInChildren<Animator>(true).All(a => a.runtimeAnimatorController == null), "Static brush fixture cannot contain a production controller.");
                    foreach (var child in brush.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = animator.gameObject.layer;
                    foreach (var skin in brush.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    { skin.updateWhenOffscreen = true; skin.forceMatrixRecalculationPerRender = true; }
                    Transform grip = brush.GetComponentsInChildren<Transform>(true).Single(t => t.name == "GripSocket");
                    Transform tip = brush.GetComponentsInChildren<Transform>(true).Single(t => t.name == "TipSocket");
                    _gripBrushes.Add((brush, hand, grip, tip, authoredProxySource)); if (!authoredProxySource) brush.SetActive(false);
                }
                Result.gripCalibrationOffsets = file.offsets.Length;
                Result.findings.Add("Grip-settle uses imported finger quaternions and hand/brush socket alignment from static grip inspection. When serialized world brush proxies exist, their exact source brush is posed and their seven capsules refresh from actual shaft/bristle geometry before each solve; complete triangle-level cloth/brush intersection review remains separate.");
            }

            private static void AlignGripBrush((GameObject brush, Transform hand, Transform grip, Transform tip, bool authoredProxySource) state)
            {
                Transform root = state.brush.transform;
                Vector3 localGrip = root.InverseTransformPoint(state.grip.position);
                Quaternion localRotation = Quaternion.Inverse(root.rotation) * state.grip.rotation;
                Vector3 axisInGrip = state.grip.InverseTransformDirection(state.tip.position - state.grip.position).normalized;
                root.rotation = state.hand.rotation * Quaternion.FromToRotation(axisInGrip, Vector3.up) * Quaternion.Inverse(localRotation);
                root.position = state.hand.position - root.TransformVector(localGrip);
            }

            private void RestorePose()
            {
                foreach (var entry in _restRotations) if (entry.Key != null) entry.Key.localRotation = entry.Value;
                foreach (var entry in _rootPoses) if (entry.Key != null) entry.Key.SetPositionAndRotation(entry.Value.p, entry.Value.q);
                foreach (var entry in _brushRestPoses) if (entry.Key != null) entry.Key.SetPositionAndRotation(entry.Value.p, entry.Value.q);
            }
            private static void Rotate(Animator animator, HumanBodyBones id, Vector3 axis, float degrees)
            { Transform bone = animator.GetBoneTransform(id); Need(bone != null, "Missing direct-pose bone: " + id); bone.Rotate(axis, degrees, Space.World); }

            private void StartRecorders()
            {
                _recordersAttempted = true;
                try
                {
                    var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
                    foreach (var handle in handles)
                    {
                        var description = ProfilerRecorderHandle.GetDescription(handle);
                        if (description.Name.IndexOf("Cloth", StringComparison.OrdinalIgnoreCase) < 0 || description.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                        var value = ProfilerRecorder.StartNew(description.Category, description.Name, 1);
                        if (!value.Valid) { value.Dispose(); continue; }
                        var result = new MarkerResult { name = description.Name, unit = description.UnitType.ToString() };
                        Result.nativeClothMarkers.Add(result); _recorders.Add(new Recorder { Value = value, Result = result });
                    }
                    if (_recorders.Count == 0) Result.findings.Add("Native Cloth CPU markers are unavailable on this Editor; no native solver CPU value is inferred.");
                }
                catch (Exception e) { Result.findings.Add("Native Cloth CPU recorder unavailable: " + e.Message); }
            }

            private void CaptureCase()
            {
                // Frame the actually measured world cloth, not all imported/LOD skinned
                // renderer bounds (those include separate near-rig authoring transforms).
                Bounds bounds = new Bounds(); bool initialized = false;
                foreach (var surface in _case.surfaces)
                    if (surface.hasBounds)
                    {
                        if (!initialized) { bounds = new Bounds(surface.boundsMin, Vector3.zero); initialized = true; }
                        bounds.Encapsulate(surface.boundsMin); bounds.Encapsulate(surface.boundsMax);
                    }
                if (!initialized) { Result.findings.Add(_case.id + ": no renderer bounds for image capture."); return; }
                var target = RenderTexture.GetTemporary(1024, 1024, 24, RenderTextureFormat.ARGB32);
                var pixels = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    foreach (var view in new[] { ("front", new Vector3(0f, .1f, 1f)), ("side", new Vector3(1f, .1f, 0f)) })
                    {
                        float distance = Mathf.Max(3f, bounds.size.magnitude * 1.8f);
                        _camera.transform.position = bounds.center + _fixture.transform.TransformDirection(view.Item2).normalized * distance;
                        _camera.transform.LookAt(bounds.center); _camera.targetTexture = target;
                        if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(_camera, new RenderPipeline.StandardRequest { destination = target });
                        else _camera.Render();
                        RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0, false); pixels.Apply(false);
                        string path = Path.Combine(Result.directory, _case.id + "_" + view.Item1 + ".png"); File.WriteAllBytes(path, pixels.EncodeToPNG()); _case.images.Add(path);
                    }
                }
                finally { _camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(pixels); }
            }

            private void InstallLoop()
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop(); Remove(ref loop);
                // Keep the existing pose/capture order. Physics may have consumed
                // a prior skin target already: independently observe its actual
                // fixed-step entry instead of moving gameplay/IK timing to hide lag.
                bool a = Insert(ref loop, typeof(UnityEngine.PlayerLoop.PostLateUpdate.UpdateAllSkinnedMeshes), new PlayerLoopSystem { type = typeof(ControlStage), updateDelegate = Control }, false);
                bool b = Insert(ref loop, typeof(UnityEngine.PlayerLoop.PostLateUpdate.PhysicsSkinnedClothFinishUpdate), new PlayerLoopSystem { type = typeof(MeasureStage), updateDelegate = Measure }, true);
                // Unity 6000.3 exposes this actual loop node but keeps its C# type
                // internal. Resolve only its exact observed full name, with no
                // fallback to general PhysicsFixedUpdate or a guessed stage.
                Type fixedAnchor = FindLoopType(loop, "UnityEngine.PlayerLoop.FixedUpdate+PhysicsClothFixedUpdate", out int matches);
                Need(matches == 1 && fixedAnchor != null, "Expected exactly one actual PhysicsClothFixedUpdate loop node.");
                bool c = Insert(ref loop, fixedAnchor, new PlayerLoopSystem { type = typeof(PhysicsLatchStage), updateDelegate = LatchPhysicsAnchors }, false);
                Need(a && b && c, "Native Cloth PlayerLoop fixed/skin/finish anchors are unavailable; no guessed measurement stage used.");
                PlayerLoop.SetPlayerLoop(loop);
            }
            private void PlayChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Finish("PLAY_EXIT", null); }
            private void BeforeReload() => Finish("DOMAIN_RELOAD", null);

            public void Finish(string status, string error)
            {
                if (_finished) return; _finished = true; Result.status = status; Result.error = error;
                Result.elapsedWallSeconds = _savedGlobals ? Time.realtimeSinceStartupAsDouble - _started : 0;
                EditorApplication.playModeStateChanged -= PlayChanged; AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
                EditorApplication.update -= AwaitNativeInitialization;
                try
                {
                    var loop = PlayerLoop.GetCurrentPlayerLoop(); if (Remove(ref loop)) PlayerLoop.SetPlayerLoop(loop);
                    if (_savedGlobals) { Time.captureDeltaTime = _oldCapture; Time.timeScale = _oldTimeScale; }
                    foreach (var suspended in _suspendedReviewPhysics) if (suspended.Key != null) suspended.Key.enabled = suspended.Value;
                    foreach (var suspended in _suspendedReviewCloth) if (suspended.Key != null) suspended.Key.enabled = suspended.Value;
                    Result.reviewClothRestored = _suspendedReviewPhysics.All(s => s.Key == null || s.Key.enabled == s.Value)
                        && _suspendedReviewCloth.All(s => s.Key == null || s.Key.enabled == s.Value);
                    Result.globalStateRestored = true;
                    foreach (var recorder in _recorders) recorder.Value.Dispose();
                    foreach (var surface in _surfaces) if (surface.LiveOutputBake != null) Object.DestroyImmediate(surface.LiveOutputBake);
                    if (_fixture != null) Object.DestroyImmediate(_fixture); Result.fixtureDestroyed = _fixture == null;
                    if (_anatomyBake != null) Object.DestroyImmediate(_anatomyBake);
                    if (_scene.IsValid() && _scene.isLoaded && Application.isPlaying)
                    {
                        var unload = SceneManager.UnloadSceneAsync(_scene);
                        if (unload != null)
                        {
                            Result.status = "RESTORING"; Save();
                            unload.completed += _ => { Result.sceneUnloadComplete = true; Complete(status); };
                            return;
                        }
                    }
                    Result.sceneUnloadComplete = !_scene.IsValid() || !_scene.isLoaded;
                    Complete(status);
                }
                catch (Exception e)
                { Result.error = (Result.error ?? "") + " RESTORE: " + e.GetBaseException().Message; Complete("FAIL"); }
            }
            private void Complete(string status)
            {
                Result.status = status; Result.restored = Result.globalStateRestored && Result.reviewClothRestored && Result.fixtureDestroyed && Result.sceneUnloadComplete;
                if (!Result.restored) Result.findings.Add("Cleanup did not confirm every temporary scene/object removal; inspect cleanup flags before treating the run as complete.");
                _last = Result; _active = null; Save();
            }
            private void Save()
            {
                if (string.IsNullOrEmpty(Result.directory)) return;
                string json = JsonUtility.ToJson(Result, true);
                File.WriteAllText(Path.Combine(Result.directory, "report.json"), json, new UTF8Encoding(false));
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2/cloth-diagnostics.json")), json, new UTF8Encoding(false));
            }
        }

        /// <summary>Coverage requires simulated warm-up AND subsequent actual solver samples, including slow-quarter conditions.</summary>
        public static bool HasSettleCoverage(float simulatedSeconds, int additionalSettledFrames)
            => Finite(simulatedSeconds) && simulatedSeconds >= 3f && additionalSettledFrames >= 60;

        /// <summary>Particle-vs-capsule depth only; no claim about triangle interiors or a complete body mesh.</summary>
        public static float PointCapsulePenetration(Vector3 point, Vector3 a, Vector3 b, float radius)
        {
            if (!Finite(point) || !Finite(a) || !Finite(b) || !Finite(radius) || radius < 0f) return float.NaN;
            Vector3 axis = b - a; float lengthSquared = axis.sqrMagnitude;
            float t = lengthSquared > .000000000001f ? Mathf.Clamp01(Vector3.Dot(point - a, axis) / lengthSquared) : 0f;
            return Mathf.Max(0f, radius - Vector3.Distance(point, a + axis * t));
        }
        private static void CapsuleWorld(CapsuleCollider capsule, out Vector3 a, out Vector3 b, out float radius)
        {
            float scale = capsule.transform.lossyScale.x; Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
            axis = capsule.transform.TransformDirection(axis).normalized; Vector3 center = capsule.transform.TransformPoint(capsule.center);
            radius = capsule.radius * scale; float half = Mathf.Max(0f, capsule.height * .5f - capsule.radius) * scale;
            a = center - axis * half; b = center + axis * half;
        }
        private static int[] MapRestParticles(Vector3[] source, Vector3[] particles)
        {
            var exact = new Dictionary<Vector3, int>(); for (int i = 0; i < source.Length; i++) if (!exact.ContainsKey(source[i])) exact.Add(source[i], i);
            var result = new int[particles.Length];
            for (int i = 0; i < result.Length; i++)
            { if (!exact.TryGetValue(particles[i], out int sourceIndex)) sourceIndex = Nearest(source, particles[i]); Need(sourceIndex >= 0, "Native rest particle source was not found."); result[i] = sourceIndex; }
            return result;
        }
        private static int Nearest(Vector3[] candidates, Vector3 point)
        { int best = -1; float distance = .00001f * .00001f; for (int i = 0; i < candidates.Length; i++) { float d = (candidates[i] - point).sqrMagnitude; if (d <= distance) { distance = d; best = i; } } return best; }
        private static bool SameWeights(Surface surface, int a, int b)
        {
            if (surface.WeightCounts[a] != surface.WeightCounts[b]) return false;
            for (int i = 0; i < surface.WeightCounts[a]; i++)
            { var x = surface.Weights[surface.Offsets[a] + i]; var y = surface.Weights[surface.Offsets[b] + i]; if (x.boneIndex != y.boneIndex || x.weight != y.weight) return false; }
            return true;
        }
        private static bool Insert(ref PlayerLoopSystem node, Type anchor, PlayerLoopSystem stage, bool after)
        {
            if (node.subSystemList == null) return false; var children = (PlayerLoopSystem[])node.subSystemList.Clone();
            for (int i = 0; i < children.Length; i++)
            { if (children[i].type == anchor) { var list = new List<PlayerLoopSystem>(children); list.Insert(i + (after ? 1 : 0), stage); node.subSystemList = list.ToArray(); return true; } if (Insert(ref children[i], anchor, stage, after)) { node.subSystemList = children; return true; } }
            return false;
        }
        private static Type FindLoopType(PlayerLoopSystem node, string fullName, out int matches)
        {
            matches = node.type != null && node.type.FullName == fullName ? 1 : 0;
            Type result = matches == 1 ? node.type : null;
            if (node.subSystemList == null) return result;
            foreach (var child in node.subSystemList)
            {
                Type found = FindLoopType(child, fullName, out int childMatches);
                matches += childMatches; if (found != null) result = found;
            }
            return result;
        }
        private static bool Remove(ref PlayerLoopSystem node)
        {
            if (node.subSystemList == null) return false; bool changed = false; var children = new List<PlayerLoopSystem>();
            foreach (var child in node.subSystemList)
            { if (child.type == typeof(ControlStage) || child.type == typeof(MeasureStage) || child.type == typeof(PhysicsLatchStage)) { changed = true; continue; } var copy = child; changed |= Remove(ref copy); children.Add(copy); }
            if (changed) node.subSystemList = children.ToArray(); return changed;
        }
        private static double Milliseconds(long before) => (Stopwatch.GetTimestamp() - before) * 1000d / Stopwatch.Frequency;
        private static string FileSha(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static void Need(bool value, string reason) { if (!value) throw new Pending(reason); }
    }
}
