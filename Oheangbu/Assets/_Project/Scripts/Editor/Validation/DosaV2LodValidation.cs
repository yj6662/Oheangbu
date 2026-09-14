using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    /// <summary>Actual disposable Play LOD/Cloth lifecycle inspection. No actions, production animation or rig/physics quality approval.</summary>
    public static class DosaV2LodValidation
    {
        private static Session _active;
        private static Report _last;
        private struct ControlStage { }
        private struct MeasureStage { }
        private sealed class Pending : Exception { public Pending(string message) : base(message) { } }
        [Serializable] private sealed class Timing
        {
            public int samples;
            public double totalMilliseconds, maximumMilliseconds, meanMilliseconds;
            public long allocatedBytes;
            public void Add(long ticks, long allocated)
            {
                double elapsed = ticks * 1000d / Stopwatch.Frequency;
                samples++; totalMilliseconds += elapsed; maximumMilliseconds = Math.Max(maximumMilliseconds, elapsed);
                meanMilliseconds = totalMilliseconds / samples; allocatedBytes += allocated;
            }
        }
        [Serializable] private sealed class Level
        {
            public int lod, renderers, triangles, vertices, lowerBonesOutsideOriginalSkeleton, rigidParentMismatches, correctiveChannels;
        }
        [Serializable] private sealed class Corrective
        {
            public int lod, vertices;
            public string renderer;
            public float maximumActualBakedDeltaMeters;
        }
        [Serializable] private sealed class Frame
        {
            public int frame, phase, expectedLod, selectedLod, enabledCloth, transitions, resets, particles, nonFinite;
            public bool visible, clothActive;
            public float cameraDistance, relativeHeight, deltaSeconds, maximumPinnedDistanceMeters, maximumEnvelopeExcessMeters, rigidMatrixError, correctiveWeightError;
        }
        [Serializable] private sealed class Phase
        {
            public string name;
            public int expectedLod, frames, resetsAtStart, resetsAtEnd, nativeParticles, nonFiniteSamples;
            public float simulatedSeconds, actualCameraDistance, targetRelativeHeight, maximumPinnedDistanceMeters, maximumEnvelopeExcessMeters;
            public int[] idPixels = new int[3];
            public bool nativeRenderSelectionMatches;
            public List<string> images = new List<string>();
        }
        [Serializable] private sealed class Report
        {
            public string status = "NOT_STARTED", error, directory, startedAtUtc, prefabSha256, prefabSha256After;
            public bool restored, fixtureDestroyed, sceneUnloadComplete, sourcePrefabUnchanged, loopRemoved;
            public int initializationFrames, frames, failures, worldAnimators, productionControllers, legacyAnimations, gameplayInputComponents, duplicateRendererReferences, newSkeletonBones;
            public bool lodIntegrationChecksPassed;
            public int returnResetCounterDelta;
            public float serializedGroupSize;
            public Vector3 serializedGroupReferencePoint;
            public Bounds actualRestGeometryBounds;
            public int actualRestGeometryVertices;
            public bool returnResetRequested, returnPostSolveFinite;
            public float returnMaximumPinnedDistanceMeters, returnMaximumEnvelopeExcessMeters;
            public string resetStatus = "PENDING_ACTUAL_LOD_RETURN";
            public double elapsedWallSeconds;
            public List<Level> levels = new List<Level>();
            public List<Corrective> handCorrectives = new List<Corrective>();
            public List<Phase> phases = new List<Phase>();
            public List<Frame> measurements = new List<Frame>();
            public List<string> findings = new List<string>();
            public Timing directPoseAndSecondaryCpu = new Timing(), diagnosticCpu = new Timing(), capturesCpu = new Timing();
            public string scope = "Actual isolated prefab: distance-selected 0/1/2/0, shared skeleton/pivots, complete actual corrective BakeMesh deltas, LOD0 native Cloth enable/reset and post-solver measurements. No input, controller, clips, retargeting, canonical modification or RIG_PASS.";
            public string qualityScope = "COMPLETE_MEASURED only describes fixture completion. Integration assertions are counted separately. Pinned drift, envelope excess and reset output remain measured data; this fixture never declares native Cloth stability, complete collision, art or full rig PASS.";
            public string selectionScope = "LOD controller reads only the dedicated fixture camera. Actual native renderer selection is independently inspected with temporary red/green/blue LOD ID materials and GPU readback; all clone materials/property blocks are restored before normal captures.";
            public string timingScope = "Original Time and global Quality settings are never changed. Each phase needs at least 0.75 actual simulated seconds and 30 real frames. Stopwatch/allocated-byte fields measure fixture control, diagnostic and capture code on the current thread, not native solver/GPU FPS or a build benchmark.";
            public string nativeSpace = "Before simulation: independent renderer-local BakeMesh/source-index mapping at 10 micrometers. After actual native solve: representation.TransformPoint(Cloth.vertices), matching the prior measured V2 initialization contract.";
        }
        [Serializable] private sealed class Progress
        {
            public string status, error, directory;
            public int phase, frames, failures;
            public bool restored;
        }
        private sealed class Surface
        {
            public Cloth Cloth;
            public SkinnedMeshRenderer Renderer;
            public Transform Representation;
            public Vector3[] Rest;
            public Matrix4x4[] Bindposes, Skin;
            public Transform[] Bones;
            public BoneWeight1[] Weights;
            public int[] Offsets, Counts, SourceIndices;
            public ClothSkinningCoefficient[] Coefficients;
        }
        private struct TransformRest
        {
            public Transform Transform;
            public Quaternion Rotation;
        }
        public static bool IsRunning => _active != null;
        public static string Begin()
        {
            if (_active != null) return Status();
            if (!Application.isPlaying || EditorApplication.isPaused || SceneManager.GetActiveScene().name != "C2_PlayerV2Validation")
                return "WAIT: unpaused Play in C2_PlayerV2Validation is required.";
            if (DosaV2ClothDiagnostics.IsRunning || DosaV2DrawingStaticValidation.IsRunning)
                return "WAIT: finish other V2 fixtures before starting LOD validation.";
            _active = new Session();
            try { _active.Start(); }
            catch (Pending e) { _active.Finish("WAIT", e.Message); }
            catch (Exception e) { _active.Finish("FAIL", e.GetBaseException().Message); }
            return Status();
        }
        public static string Status()
        {
            var result = _active != null ? _active.Result : _last;
            return result == null ? "NOT_STARTED" : JsonUtility.ToJson(new Progress { status = result.status, error = result.error, directory = result.directory,
                phase = result.phases.Count - 1, frames = result.frames, failures = result.failures, restored = result.restored });
        }
        public static string Stop() { _active?.Finish("STOPPED", "Stopped by caller."); return Status(); }

        private sealed class Session
        {
            public readonly Report Result = new Report();
            private Scene _scene;
            private GameObject _fixture;
            private Camera _camera, _observer;
            private PlayerLodClothController _controller;
            private PlayerSecondaryMotionRig _secondary;
            private PlayerSecondaryMotionRig[] _allSecondary;
            private Animator _animator;
            private LOD[] _levels;
            private Renderer[] _renderers;
            private List<(MeshRenderer renderer, MeshRenderer baseline)> _rigid = new List<(MeshRenderer, MeshRenderer)>();
            private TransformRest[] _rest;
            private readonly List<Surface> _surfaces = new List<Surface>();
            private PlayerHandGripCorrectives _correctives;
            private Transform _rightArm, _leftArm, _samplePivot;
            private Vector3 _worldPosition;
            private Quaternion _worldRotation;
            private Bounds _actualBounds;
            private Material[] _idMaterials;
            private Mesh _bake;
            private int _spawnFrame, _phaseIndex, _lastControlFrame = -1;
            private bool _finished, _phaseCaptured;
            private double _started;
            private float _correctiveWeight;
            private readonly int[] _sequence = { 0, 1, 2, 0 };
            private Phase Current => Result.phases[_phaseIndex];

            public void Start()
            {
                Result.startedAtUtc = DateTime.UtcNow.ToString("o");
                Result.directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2/lod-" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")));
                Directory.CreateDirectory(Result.directory); _started = Time.realtimeSinceStartupAsDouble;
                Need(Time.timeScale > 0f, "Game time is paused.");
                Need(QualitySettings.maximumLODLevel == 0 && QualitySettings.lodBias > 0f, "Current Quality settings prevent all 0/1/2 levels; fixture will not change user Quality settings.");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.ValidationPrefab);
                Need(prefab != null && prefab.GetComponentsInChildren<PlayerLodClothController>(true).Length == 1, "Build the isolated prefab with exactly one world LOD controller first.");
                Result.productionControllers = prefab.GetComponentsInChildren<Animator>(true).Count(a => a.runtimeAnimatorController != null);
                Result.legacyAnimations = prefab.GetComponentsInChildren<Animation>(true).Length;
                string[] inputNames = { "PlayerInput", "DrawingInputController", "PlayerMotor", "PlayerVisualDriver" };
                Result.gameplayInputComponents = prefab.GetComponentsInChildren<MonoBehaviour>(true).Count(c => c != null && inputNames.Contains(c.GetType().Name));
                Need(Result.productionControllers == 0 && Result.legacyAnimations == 0 && Result.gameplayInputComponents == 0,
                    "The pre-gate fixture cannot contain production controllers, gameplay input/motor or legacy animation.");
                Result.prefabSha256 = HashPrefab();
                EditorApplication.playModeStateChanged += PlayChanged; AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
                _scene = SceneManager.CreateScene("DosaV2LodDiagnostic_" + Guid.NewGuid().ToString("N"));
                _fixture = Object.Instantiate(prefab, new Vector3(30f, 0f, 30f), Quaternion.identity);
                _fixture.name = "DosaV2LodFixture"; SceneManager.MoveGameObjectToScene(_fixture, _scene);
                _controller = _fixture.GetComponentInChildren<PlayerLodClothController>(true);
                _controller.enabled = false;
                _secondary = _controller.GetComponent<PlayerSecondaryMotionRig>();
                _allSecondary = _fixture.GetComponentsInChildren<PlayerSecondaryMotionRig>(true);
                foreach (var rig in _allSecondary) rig.SetRepresentationActive(false);
                foreach (var cloth in _fixture.GetComponentsInChildren<Cloth>(true)) cloth.enabled = false;
                foreach (var camera in _fixture.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                foreach (var listener in _fixture.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
                _levels = _controller.Group.GetLODs();
                Need(_levels.Length == 3, "Actual serialized LODGroup must have three levels.");
                _renderers = _levels.SelectMany(l => l.renderers).ToArray();
                Result.duplicateRendererReferences = _renderers.Length - _renderers.Distinct().Count();
                Need(Result.duplicateRendererReferences == 0 && _renderers.All(r => r != null), "Each actual renderer must belong to exactly one LOD.");
                var allowed = new HashSet<Renderer>(_renderers);
                foreach (var renderer in _fixture.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = allowed.Contains(renderer);
                    if (allowed.Contains(renderer)) renderer.gameObject.layer = 29;
                    if (renderer is SkinnedMeshRenderer skin) { skin.updateWhenOffscreen = true; skin.forceMatrixRecalculationPerRender = true; }
                }
                _camera = NewCamera("LOD distance selection camera");
                _observer = NewCamera("LOD fixed inspection camera");
                _controller.SetViewCamera(_camera);
                _worldPosition = _controller.transform.position; _worldRotation = _controller.transform.rotation;
                _spawnFrame = Time.frameCount; EditorApplication.update += AwaitInitialization;
                Result.status = "INITIALIZING_NATIVE"; Save();
            }
            private Camera NewCamera(string name)
            {
                var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, _scene);
                var camera = obj.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 1 << 29;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(_scene);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.24f, .26f, .28f);
                camera.fieldOfView = 40f; camera.nearClipPlane = .01f; camera.farClipPlane = 1000f;
                return camera;
            }
            private void AwaitInitialization()
            {
                if (_finished) return;
                Result.initializationFrames = Time.frameCount - _spawnFrame;
                if (Result.initializationFrames < 2) return;
                try
                {
                    if (!_allSecondary.All(r => r.IsConfigured) && Time.realtimeSinceStartupAsDouble - _started < 5d) return;
                    EditorApplication.update -= AwaitInitialization;
                    Need(_allSecondary.All(r => r.IsConfigured), "Native serialized secondary binding did not initialize: " + string.Join("; ", _allSecondary.Select(r => r.LastBindingError)));
                    Initialize();
                }
                catch (Pending e) { Finish("WAIT", e.Message); }
                catch (Exception e) { Finish("FAIL", e.GetBaseException().Message); }
            }
            private void Initialize()
            {
                _animator = _controller.GetComponentInChildren<Animator>(true);
                Result.worldAnimators = _controller.GetComponentsInChildren<Animator>(true).Length;
                Need(_animator != null && Result.worldAnimators == 1, "Only the original world Animator is allowed.");
                _animator.enabled = false; _animator.applyRootMotion = false;
                var originalBones = new HashSet<Transform>(_levels[0].renderers.OfType<SkinnedMeshRenderer>().SelectMany(s => s.bones));
                var originalTransforms = new HashSet<Transform>(originalBones);
                foreach (var s in _levels[0].renderers.OfType<SkinnedMeshRenderer>()) originalTransforms.Add(s.rootBone);
                var baseline = _levels[0].renderers.ToDictionary(r => r.name, r => r);
                for (int level = 0; level < 3; level++)
                {
                    var result = new Level { lod = level, renderers = _levels[level].renderers.Length };
                    foreach (var renderer in _levels[level].renderers)
                    {
                        Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                        Need(mesh != null && mesh.isReadable, renderer.name + ": actual readable mesh required.");
                        result.vertices += mesh.vertexCount;
                        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) result.triangles += (int)mesh.GetIndexCount(submesh) / 3;
                        if (renderer is SkinnedMeshRenderer skinned)
                        {
                            result.lowerBonesOutsideOriginalSkeleton += skinned.bones.Count(b => !originalBones.Contains(b));
                            Need(originalTransforms.Contains(skinned.rootBone), renderer.name + ": rootBone is not from the original skeleton.");
                            result.correctiveChannels += Enumerable.Range(0, mesh.blendShapeCount).Count(i => IsPalmShape(mesh.GetBlendShapeName(i)));
                        }
                        else if (level > 0)
                        {
                            string sourceName = renderer.name.Substring(("LOD" + level + "__").Length);
                            Need(baseline.TryGetValue(sourceName, out var source) && source is MeshRenderer, renderer.name + ": original rigid part missing.");
                            if (renderer.transform.parent != source.transform.parent) result.rigidParentMismatches++;
                            _rigid.Add(((MeshRenderer)renderer, (MeshRenderer)source));
                            originalTransforms.Add(source.transform.parent);
                        }
                    }
                    Need(result.lowerBonesOutsideOriginalSkeleton == 0 && result.rigidParentMismatches == 0, "LOD" + level + " does not share the existing skeleton/rigid pivots.");
                    Need(result.triangles <= (level == 0 ? 75000 : level == 1 ? 35000 : 18000), "Actual LOD triangle budget exceeded.");
                    Need(result.correctiveChannels > 0, "LOD" + level + " has no imported hand corrective channels.");
                    Result.newSkeletonBones += result.lowerBonesOutsideOriginalSkeleton; Result.levels.Add(result);
                }
                _rest = originalTransforms.Where(t => t != null).Select(t => new TransformRest { Transform = t, Rotation = t.localRotation }).ToArray();
                _rightArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                _leftArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                Need(_rightArm != null && _leftArm != null, "Static direct arm-pose bones are missing.");
                _samplePivot = _rigid.Select(p => p.baseline.transform.parent).FirstOrDefault(t => t.name.Contains("PackAux_"));
                Need(_samplePivot != null, "A real shared backpack pivot is required for the rigid-follow check.");
                _correctives = new PlayerHandGripCorrectives(_renderers); _bake = new Mesh { name = "LOD actual hand bake" };
                MeasureCorrectiveGeometry();
                bool hasBounds = false;
                foreach (var renderer in _levels[0].renderers)
                {
                    Vector3[] points = renderer is SkinnedMeshRenderer skin ? IndependentBake(skin) : renderer.GetComponent<MeshFilter>().sharedMesh.vertices;
                    foreach (var point in points)
                    {
                        Vector3 world = renderer.transform.TransformPoint(point);
                        if (!hasBounds) { _actualBounds = new Bounds(world, Vector3.zero); hasBounds = true; } else _actualBounds.Encapsulate(world);
                    }
                    Result.actualRestGeometryVertices += points.Length;
                }
                Need(hasBounds && _actualBounds.size.y > .5f && Finite(_actualBounds.center), "Actual frozen world geometry does not define valid body bounds.");
                Result.actualRestGeometryBounds = _actualBounds; Result.serializedGroupSize = _controller.Group.size;
                Result.serializedGroupReferencePoint = _controller.Group.localReferencePoint;
                foreach (var cloth in _controller.GetComponentsInChildren<Cloth>(true)) _surfaces.Add(PrepareSurface(cloth));
                Need(_surfaces.Count == 3 && _levels.Skip(1).SelectMany(l => l.renderers).All(r => r.GetComponent<Cloth>() == null), "The final three native Cloth surfaces must belong only to LOD0.");
                Shader idShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                Need(idShader != null, "Unlit ID-render shader is unavailable.");
                _idMaterials = new[] { Color.red, Color.green, Color.blue }.Select((color, i) =>
                {
                    var material = new Material(idShader) { name = "Temporary LOD" + i + " ID" };
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                    return material;
                }).ToArray();
                _secondary.SetRepresentationActive(true); _controller.enabled = true;
                BeginPhase();
                var loop = PlayerLoop.GetCurrentPlayerLoop(); Remove(ref loop);
                Need(InsertBefore(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate), new PlayerLoopSystem { type = typeof(ControlStage), updateDelegate = Control }), "Final pre-LateUpdate control anchor is absent.");
                Need(AppendPostLate(ref loop, new PlayerLoopSystem { type = typeof(MeasureStage), updateDelegate = Measure }), "PostLateUpdate tail is absent.");
                PlayerLoop.SetPlayerLoop(loop); Result.status = "RUNNING"; Save();
            }
            private void MeasureCorrectiveGeometry()
            {
                for (int level = 0; level < 3; level++) foreach (var skin in _levels[level].renderers.OfType<SkinnedMeshRenderer>())
                {
                    if (!Enumerable.Range(0, skin.sharedMesh.blendShapeCount).Any(i => IsPalmShape(skin.sharedMesh.GetBlendShapeName(i)))) continue;
                    _correctives.Apply(0f, 0f); skin.BakeMesh(_bake, false); var before = _bake.vertices;
                    _correctives.Apply(1f, 1f); skin.BakeMesh(_bake, false); var after = _bake.vertices;
                    Need(before.Length == after.Length && after.All(Finite), skin.name + ": nonfinite/incomplete corrective bake.");
                    float delta = 0f; for (int i = 0; i < before.Length; i++) delta = Math.Max(delta, Vector3.Distance(before[i], after[i]));
                    Need(delta > 1e-7f, skin.name + ": corrective channel changes weights but not the actual baked mesh.");
                    Result.handCorrectives.Add(new Corrective { lod = level, renderer = skin.name, vertices = after.Length, maximumActualBakedDeltaMeters = delta });
                }
                _correctives.Apply(0f, 0f);
            }
            private Surface PrepareSurface(Cloth cloth)
            {
                var skin = cloth.GetComponent<SkinnedMeshRenderer>(); Need(skin != null, cloth.name + ": SMR missing.");
                var source = skin.sharedMesh; Vector3[] independent = IndependentBake(skin), native = cloth.vertices;
                Need(DosaV2SecondaryBuilder.TryMapAuthoredCoefficients(independent, DosaV2SecondaryBuilder.GetAuthoredClothValues(source), native, out var authored, out var mapping),
                    cloth.name + ": initial actual rest mapping failed: " + mapping.error);
                Need(authored.Select(c => c.maxDistance).SequenceEqual(cloth.coefficients.Select(c => c.maxDistance)), cloth.name + ": serialized coefficients differ from exact UV3 authoring.");
                var surface = new Surface { Cloth = cloth, Renderer = skin, Representation = _controller.transform,
                    Rest = source.vertices, Bindposes = source.bindposes, Bones = skin.bones, Coefficients = authored, SourceIndices = new int[native.Length] };
                using (var counts = source.GetBonesPerVertex()) surface.Counts = counts.Select(c => (int)c).ToArray();
                using (var weights = source.GetAllBoneWeights()) surface.Weights = weights.ToArray();
                surface.Offsets = new int[surface.Counts.Length]; int offset = 0;
                for (int i = 0; i < surface.Counts.Length; i++) { surface.Offsets[i] = offset; offset += surface.Counts[i]; }
                Need(offset == surface.Weights.Length && surface.Bindposes.Length == surface.Bones.Length, cloth.name + ": full original skin stream is invalid.");
                surface.Skin = new Matrix4x4[surface.Bones.Length];
                for (int p = 0; p < native.Length; p++)
                {
                    int best = -1; float distance = 1e-10f;
                    for (int v = 0; v < independent.Length; v++) { float d = (native[p] - independent[v]).sqrMagnitude; if (d <= distance) { distance = d; best = v; } }
                    Need(best >= 0, cloth.name + ": no 10um original source-index match."); surface.SourceIndices[p] = best;
                }
                return surface;
            }
            private static Vector3[] IndependentBake(SkinnedMeshRenderer source)
            {
                var obj = new GameObject("LOD independent unsimulated rest"); var baked = new Mesh();
                try
                {
                    obj.transform.SetParent(source.transform.parent, false); obj.transform.localPosition = source.transform.localPosition;
                    obj.transform.localRotation = source.transform.localRotation; obj.transform.localScale = source.transform.localScale;
                    var skin = obj.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = source.sharedMesh; skin.bones = source.bones; skin.rootBone = source.rootBone;
                    skin.BakeMesh(baked, false); return baked.vertices;
                }
                finally { Object.DestroyImmediate(obj); Object.DestroyImmediate(baked); }
            }
            private void BeginPhase()
            {
                float[] transitions = _controller.Group.GetLODs().Select(l => l.screenRelativeTransitionHeight).ToArray();
                int expected = _sequence[_phaseIndex];
                float relative = expected == 0 ? Math.Min(.8f, transitions[0] + .15f) : expected == 1 ? (transitions[0] + transitions[1]) * .5f : transitions[1] * .4f;
                var phase = new Phase { name = _phaseIndex == 3 ? "LOD0_return" : "LOD" + expected, expectedLod = expected,
                    targetRelativeHeight = relative, resetsAtStart = _controller.ClothResets };
                Result.phases.Add(phase); _phaseCaptured = false;
            }
            private void Control()
            {
                if (_finished) return; long start = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
                try
                {
                    if (Time.realtimeSinceStartupAsDouble - _started > 120d) { Finish("STOPPED", "120s wall safety limit; partial run is not complete."); return; }
                    foreach (var rest in _rest) rest.Transform.localRotation = rest.Rotation;
                    float armAngle = _phaseIndex == 0 ? 35f : _phaseIndex == 1 ? 55f : _phaseIndex == 2 ? 20f : 45f;
                    _leftArm.rotation = Quaternion.AngleAxis(armAngle, _controller.transform.forward) * _leftArm.rotation;
                    _rightArm.rotation = Quaternion.AngleAxis(-armAngle, _controller.transform.forward) * _rightArm.rotation;
                    _correctiveWeight = (_phaseIndex & 1) == 0 ? 0f : 1f; _correctives.Apply(_correctiveWeight, _correctiveWeight);
                    _secondary.Evaluate(Time.deltaTime);
                    _samplePivot.localRotation *= Quaternion.Euler(0f, 0f, _phaseIndex % 2 == 0 ? -12f : 12f);
                    var group = _controller.Group; Vector3 scale = group.transform.lossyScale;
                    float size = group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    Vector3 center = group.transform.TransformPoint(group.localReferencePoint);
                    float distance = size * .5f * QualitySettings.lodBias / (Current.targetRelativeHeight * Mathf.Tan(_camera.fieldOfView * Mathf.Deg2Rad * .5f));
                    _camera.transform.position = center + _controller.transform.forward * distance; _camera.transform.LookAt(center);
                    _observer.transform.position = _actualBounds.center + (_controller.transform.forward + _controller.transform.right * .28f).normalized * Mathf.Max(_actualBounds.size.y, _actualBounds.size.x) * 1.6f;
                    _observer.transform.LookAt(_actualBounds.center); Current.actualCameraDistance = distance; _lastControlFrame = Time.frameCount;
                }
                catch (Exception e) { Finish("FAIL", e.GetBaseException().Message); }
                finally { Result.directPoseAndSecondaryCpu.Add(Stopwatch.GetTimestamp() - start, GC.GetAllocatedBytesForCurrentThread() - allocated); }
            }
            private void Measure()
            {
                if (_finished || _lastControlFrame != Time.frameCount) return;
                long start = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
                bool phaseReady = false;
                try
                {
                    var frame = new Frame { frame = Result.frames, phase = _phaseIndex, expectedLod = Current.expectedLod,
                        selectedLod = _controller.SelectedLod, enabledCloth = _surfaces.Count(s => s.Cloth.enabled), transitions = _controller.LodTransitions,
                        resets = _controller.ClothResets, visible = _controller.VisibleToReferenceCamera, clothActive = _controller.ClothActive,
                        relativeHeight = _controller.RelativeScreenHeight, cameraDistance = Current.actualCameraDistance, deltaSeconds = Time.deltaTime };
                    Assert(frame.selectedLod == frame.expectedLod && frame.visible, "Distance-selected level or own-camera visibility differs.");
                    Assert(frame.enabledCloth == (frame.expectedLod == 0 ? _surfaces.Count : 0) && frame.clothActive == (frame.expectedLod == 0),
                        "Native Cloth enabled state does not match LOD0-only contract.");
                    Assert(Vector3.Distance(_worldPosition, _controller.transform.position) < 1e-6f && Quaternion.Angle(_worldRotation, _controller.transform.rotation) < .001f, "Fixture world root was moved by the representation.");
                    foreach (var pair in _rigid) frame.rigidMatrixError = Math.Max(frame.rigidMatrixError, DosaV2LodBuilder.MatrixDifference(pair.renderer.localToWorldMatrix, pair.baseline.localToWorldMatrix));
                    Assert(frame.rigidMatrixError <= .0001f, "Rigid lower-LOD props did not follow the same authored pivot.");
                    foreach (var skin in _renderers.OfType<SkinnedMeshRenderer>()) for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++)
                        if (IsPalmShape(skin.sharedMesh.GetBlendShapeName(i))) frame.correctiveWeightError = Math.Max(frame.correctiveWeightError, Math.Abs(skin.GetBlendShapeWeight(i) - _correctiveWeight * 100f));
                    Assert(frame.correctiveWeightError <= .0001f, "Hand corrective weight did not propagate to every LOD.");
                    foreach (var surface in _surfaces)
                    {
                        Vector3[] particles = surface.Cloth.vertices;
                        Need(particles.Length == surface.SourceIndices.Length, surface.Cloth.name + ": native particle count changed after LOD switch.");
                        for (int b = 0; b < surface.Bones.Length; b++) surface.Skin[b] = surface.Bones[b].localToWorldMatrix * surface.Bindposes[b];
                        for (int p = 0; p < particles.Length; p++)
                        {
                            if (!Finite(particles[p])) { frame.nonFinite++; continue; }
                            frame.particles++;
                            if (!surface.Cloth.enabled) continue;
                            int vertex = surface.SourceIndices[p]; Vector3 anchor = Vector3.zero;
                            for (int w = 0; w < surface.Counts[vertex]; w++)
                            { var weight = surface.Weights[surface.Offsets[vertex] + w]; anchor += surface.Skin[weight.boneIndex].MultiplyPoint3x4(surface.Rest[vertex]) * weight.weight; }
                            float distance = Vector3.Distance(anchor, surface.Representation.TransformPoint(particles[p]));
                            if (surface.Coefficients[p].maxDistance == 0f) frame.maximumPinnedDistanceMeters = Math.Max(frame.maximumPinnedDistanceMeters, distance);
                            frame.maximumEnvelopeExcessMeters = Math.Max(frame.maximumEnvelopeExcessMeters, distance - surface.Coefficients[p].maxDistance);
                        }
                    }
                    Assert(frame.nonFinite == 0, "Native Cloth contains nonfinite particles.");
                    Current.frames++; Current.simulatedSeconds += Time.deltaTime; Current.nativeParticles = frame.particles;
                    Current.nonFiniteSamples += frame.nonFinite; Current.maximumPinnedDistanceMeters = Math.Max(Current.maximumPinnedDistanceMeters, frame.maximumPinnedDistanceMeters);
                    Current.maximumEnvelopeExcessMeters = Math.Max(Current.maximumEnvelopeExcessMeters, frame.maximumEnvelopeExcessMeters);
                    Current.resetsAtEnd = frame.resets; Result.frames++; Result.measurements.Add(frame);
                    phaseReady = HasPhaseCoverage(Current.simulatedSeconds, Current.frames);
                }
                catch (Exception e) { Finish("FAIL", e.GetBaseException().Message); }
                finally { Result.diagnosticCpu.Add(Stopwatch.GetTimestamp() - start, GC.GetAllocatedBytesForCurrentThread() - allocated); }
                if (_finished || !phaseReady || _phaseCaptured) return;
                try
                {
                    _phaseCaptured = true;
                    long captureStart = Stopwatch.GetTimestamp(), captureAlloc = GC.GetAllocatedBytesForCurrentThread();
                    try { CaptureId(); CaptureNormal(); }
                    finally { Result.capturesCpu.Add(Stopwatch.GetTimestamp() - captureStart, GC.GetAllocatedBytesForCurrentThread() - captureAlloc); }
                    Save();
                    if (_phaseIndex == _sequence.Length - 1)
                    {
                        Result.returnResetCounterDelta = Current.resetsAtEnd - Current.resetsAtStart;
                        Result.returnResetRequested = Result.returnResetCounterDelta > 0;
                        Result.returnPostSolveFinite = Current.nativeParticles > 0 && Current.nonFiniteSamples == 0;
                        Result.returnMaximumPinnedDistanceMeters = Current.maximumPinnedDistanceMeters;
                        Result.returnMaximumEnvelopeExcessMeters = Current.maximumEnvelopeExcessMeters;
                        Result.resetStatus = "RESET_REQUEST_AND_POSTSOLVE_MEASURED_QUALITY_PENDING";
                        Assert(Result.returnResetRequested, "LOD0 return did not request a Cloth reset.");
                        Finish("COMPLETE_MEASURED", null);
                    }
                    else { _phaseIndex++; BeginPhase(); }
                }
                catch (Exception e) { Finish("FAIL", e.GetBaseException().Message); }
            }
            private void CaptureId()
            {
                var saved = _renderers.Select(r => (renderer: r, materials: r.sharedMaterials, block: new MaterialPropertyBlock(), shadow: r.shadowCastingMode)).ToArray();
                foreach (var item in saved) item.renderer.GetPropertyBlock(item.block);
                Color background = _camera.backgroundColor;
                try
                {
                    _camera.backgroundColor = Color.black;
                    for (int level = 0; level < 3; level++) foreach (var renderer in _levels[level].renderers)
                    { renderer.SetPropertyBlock(null); renderer.sharedMaterials = Enumerable.Repeat(_idMaterials[level], renderer.sharedMaterials.Length).ToArray(); renderer.shadowCastingMode = ShadowCastingMode.Off; }
                    Color32[] pixels = Capture(_camera, Current.name + "_native_lod_ids", 384, 384);
                    Current.idPixels = CountLodIdPixels(pixels);
                    Current.nativeRenderSelectionMatches = Current.idPixels[Current.expectedLod] > 8 && Current.idPixels.Where((_, i) => i != Current.expectedLod).All(n => n == 0);
                    Assert(Current.nativeRenderSelectionMatches, "Native ID render does not exclusively contain the expected LOD.");
                }
                finally
                {
                    _camera.backgroundColor = background;
                    foreach (var item in saved) { item.renderer.sharedMaterials = item.materials; item.renderer.SetPropertyBlock(item.block); item.renderer.shadowCastingMode = item.shadow; }
                }
            }
            private void CaptureNormal()
            {
                Capture(_camera, Current.name + "_actual_distance", 768, 768);
                Capture(_observer, Current.name + "_fixed_inspection", 768, 768);
            }
            private Color32[] Capture(Camera camera, string name, int width, int height)
            {
                var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                var pixels = new Texture2D(width, height, TextureFormat.RGB24, false); var previous = RenderTexture.active;
                var oldTarget = camera.targetTexture;
                try
                {
                    camera.targetTexture = target;
                    if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                    else camera.Render();
                    RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); pixels.Apply(false);
                    string path = Path.Combine(Result.directory, name + ".png"); File.WriteAllBytes(path, pixels.EncodeToPNG()); Current.images.Add(path);
                    return pixels.GetPixels32();
                }
                finally { camera.targetTexture = oldTarget; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(pixels); }
            }
            private void Assert(bool condition, string message)
            {
                if (condition) return; Result.failures++;
                if (!Result.findings.Contains(message)) Result.findings.Add(message);
            }
            private void PlayChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Finish("PLAY_EXIT", null); }
            private void BeforeReload() => Finish("DOMAIN_RELOAD", null);
            public void Finish(string status, string error)
            {
                if (_finished) return; _finished = true; Result.status = status; Result.error = error;
                Result.elapsedWallSeconds = Time.realtimeSinceStartupAsDouble - _started;
                EditorApplication.update -= AwaitInitialization; EditorApplication.playModeStateChanged -= PlayChanged; AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
                try
                {
                    var loop = PlayerLoop.GetCurrentPlayerLoop(); if (Remove(ref loop)) PlayerLoop.SetPlayerLoop(loop); Result.loopRemoved = true;
                    if (_fixture != null) Object.DestroyImmediate(_fixture); Result.fixtureDestroyed = _fixture == null;
                    if (_bake != null) Object.DestroyImmediate(_bake);
                    if (_idMaterials != null) foreach (var material in _idMaterials) if (material != null) Object.DestroyImmediate(material);
                    Result.prefabSha256After = HashPrefab(); Result.sourcePrefabUnchanged = string.IsNullOrEmpty(Result.prefabSha256) || Result.prefabSha256After == Result.prefabSha256;
                    if (_scene.IsValid() && _scene.isLoaded && Application.isPlaying)
                    {
                        var unload = SceneManager.UnloadSceneAsync(_scene);
                        if (unload != null)
                        { Result.status = "RESTORING"; Save(); unload.completed += _ => { Result.sceneUnloadComplete = true; Complete(status); }; return; }
                    }
                    Result.sceneUnloadComplete = !_scene.IsValid() || !_scene.isLoaded; Complete(status);
                }
                catch (Exception e) { Result.error = (Result.error ?? "") + " RESTORE: " + e.GetBaseException().Message; Complete("FAIL"); }
            }
            private void Complete(string status)
            {
                Result.status = status; Result.restored = Result.loopRemoved && Result.fixtureDestroyed && Result.sceneUnloadComplete && Result.sourcePrefabUnchanged;
                Result.lodIntegrationChecksPassed = status == "COMPLETE_MEASURED" && Result.restored && Result.failures == 0 && Result.phases.Count == 4;
                _last = Result; _active = null; Save();
            }
            private void Save()
            {
                if (string.IsNullOrEmpty(Result.directory)) return;
                string json = JsonUtility.ToJson(Result, true); File.WriteAllText(Path.Combine(Result.directory, "report.json"), json);
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2/lod-validation.json")), json);
            }
        }
        private static bool IsPalmShape(string name) => name == "GripPalmRelax_Right" || name == "GripPalmRelax_Left" || name.EndsWith(".GripPalmRelax_Right", StringComparison.Ordinal) || name.EndsWith(".GripPalmRelax_Left", StringComparison.Ordinal);
        public static bool HasPhaseCoverage(float simulatedSeconds, int frames) => !float.IsNaN(simulatedSeconds) && !float.IsInfinity(simulatedSeconds) && simulatedSeconds >= .75f && frames >= 30;
        public static int[] CountLodIdPixels(Color32[] pixels)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            var result = new int[3];
            foreach (var pixel in pixels)
            {
                if (pixel.r > 40 && pixel.r > pixel.g * 2 + 15 && pixel.r > pixel.b * 2 + 15) result[0]++;
                else if (pixel.g > 40 && pixel.g > pixel.r * 2 + 15 && pixel.g > pixel.b * 2 + 15) result[1]++;
                else if (pixel.b > 40 && pixel.b > pixel.r * 2 + 15 && pixel.b > pixel.g * 2 + 15) result[2]++;
            }
            return result;
        }
        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        private static void Need(bool condition, string message) { if (!condition) throw new Pending(message); }
        private static string HashPrefab()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", DosaV2PlayerBuilder.ValidationPrefab));
            if (!File.Exists(path)) return null;
            using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static bool InsertBefore(ref PlayerLoopSystem node, Type anchor, PlayerLoopSystem stage)
        {
            if (node.subSystemList == null) return false; var children = (PlayerLoopSystem[])node.subSystemList.Clone();
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].type == anchor) { var list = new List<PlayerLoopSystem>(children); list.Insert(i, stage); node.subSystemList = list.ToArray(); return true; }
                if (InsertBefore(ref children[i], anchor, stage)) { node.subSystemList = children; return true; }
            }
            return false;
        }
        private static bool AppendPostLate(ref PlayerLoopSystem node, PlayerLoopSystem stage)
        {
            if (node.type == typeof(UnityEngine.PlayerLoop.PostLateUpdate))
            { var list = new List<PlayerLoopSystem>(node.subSystemList ?? Array.Empty<PlayerLoopSystem>()); list.Add(stage); node.subSystemList = list.ToArray(); return true; }
            if (node.subSystemList == null) return false; var children = (PlayerLoopSystem[])node.subSystemList.Clone();
            for (int i = 0; i < children.Length; i++) if (AppendPostLate(ref children[i], stage)) { node.subSystemList = children; return true; }
            return false;
        }
        private static bool Remove(ref PlayerLoopSystem node)
        {
            if (node.subSystemList == null) return false; bool changed = false; var children = new List<PlayerLoopSystem>();
            foreach (var child in node.subSystemList)
            {
                if (child.type == typeof(ControlStage) || child.type == typeof(MeasureStage)) { changed = true; continue; }
                var copy = child; changed |= Remove(ref copy); children.Add(copy);
            }
            if (changed) node.subSystemList = children.ToArray(); return changed;
        }
    }
}
