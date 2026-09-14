using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.Data;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Pre-gate static IK on disposable V2 imports. No Animator evaluation, clips, retargeting or gameplay input.</summary>
    public static class DosaV2DrawingStaticValidation
    {
        private static Session _active;
        private static Report _last;
        private struct PoseStage { }
        private struct CaptureStage { }
        // Conservative arithmetic allowance, deducted from evidence rather than added to acceptance.
        private const float ContactNumericalGuard = .000001f;
        private sealed class Pending : Exception { public Pending(string message) : base(message) { } }
        [Serializable] private sealed class FingerOffset { public string name = ""; public Quaternion offset = default; }
        [Serializable] private sealed class Calibration { public string status = ""; public FingerOffset[] offsets = Array.Empty<FingerOffset>(); }
        [Serializable] private sealed class Source { public string path, sha256; }
        [Serializable] private sealed class GripFingerReference
        {
            public string finger; public int[] sourceVertices; public int witnessSourceVertex;
            public float witnessGapUpperMeters, baselineMinimumSignedLowerMeters;
        }
        [Serializable] private sealed class GripTriangleReference
        { public int sourceTriangle, a, b, c, primaryMask; public float signedLowerMeters; }
        [Serializable] private sealed class GripReferenceRecord
        {
            public string representation, modelPath, modelSha256, meshGuid, deformationSha256, shaftDeformationSha256;
            public long meshLocalId;
            public Vector3[] handInGrip, shaftInGrip;
            public GripFingerReference[] fingers; public GripTriangleReference[] triangles;
            public int sourceVertexCount, sourceTriangleCount, signedDistanceQueries;
            public string selection = "Every triangle incident to a vertex whose largest full skin influence is RightHandThumb/Index/Middle. All triangle corners are retained, including mixed boundary weights. Contact witnesses are original vertices dominated by that primary finger. Ring/pinky-only triangles and wrist/palm-only regions are not primary-contact claims.";
        }
        [Serializable] private sealed class GripReferenceFile
        {
            public string method = "INDEPENDENT_FRESH_IMPORT_CALIBRATED_GRIP_WITH_ACTUAL_BAKED_SKIN_AND_FINITE_SHAFT";
            public string coordinateFrame = "Grip origin and world rotation, with unit scale: metric meters even if the source import transform has a scale. Each representation uses its own exact original vertex ordering.";
            public string surfaceBound = "Closed finite shaft triangle mesh; exact-position seams must form two-sided edges. BVH nearest distance plus two nonparallel ray parities determine sign (disagreement uses the conservative inside sign). A signed-distance 1-Lipschitz bound covers every retained hand triangle, recursively subdividing near the shaft to <=10um centroid radius when necessary. The 1um arithmetic guard is subtracted. The -0.025mm early-out is a computation reserve, not the approved -0.5mm penetration limit.";
            public List<GripReferenceRecord> representations = new List<GripReferenceRecord>();
            public Source[] sources;
        }
        [Serializable] private sealed class GripSurfaceSample
        {
            public string representation, finger; public bool marginProven;
            public int checkedVertices, checkedTriangles, witnessSourceVertex;
            public float maxPrimaryVertexDeltaMeters, maxIncidentTriangleCornerDeltaMeters, shaftDeltaMeters;
            public float contactGapUpperMeters, penetrationUpperMeters, remainingGapMarginMeters, remainingPenetrationMarginMeters;
        }
        private sealed class GripState
        {
            public SkinnedMeshRenderer Hand; public Mesh SourceMesh; public BrushState Brush;
            public GripReferenceRecord Reference; public float[] Delta;
        }
        [Serializable] private sealed class LandmarkWeight { public string bone; public int boneIndex; public float weight; }
        [Serializable] private sealed class BristleLandmark
        {
            public string representation, renderer, meshPath;
            public int sourceVertexIndex, sourceVertexCount;
            public Vector3 restPointInBrush, restSocketInBrush, axisInBrush;
            public float distalProjectionMeters, restMeshSocketGapMeters;
            public List<LandmarkWeight> weights = new List<LandmarkWeight>();
        }
        [Serializable] private sealed class Sample
        {
            public string pattern;
            public int requestedHz, ordinal;
            public float unscaledStep, scaledStep, pitch, yaw;
            public Vector2 viewport;
            public bool stroking, reachable, flexible, finite;
            public float tipPixels, nearGripMeters, worldGripMeters, worldTipScreenPixels;
            public float nearArcMeters, worldArcMeters, maxArcLengthDeltaMeters, maxSegmentLengthDeltaMeters;
            public float maxShaftVertexDeltaMeters, maxBoneLengthDeltaMeters, bendDegrees;
            public Vector3 actualTipWorld;
            public bool bakedBristleMeasured;
            public Vector3 nearBakedBristleTipWorld, worldBakedBristleTipWorld;
            public float nearBakedTipPixels, worldBakedTipScreenPixels, nearMeshSocketGapMeters, worldMeshSocketGapMeters, nearSplayWeight, worldSplayWeight;
            public bool gripSurfacesMeasured;
            public bool armRollEnabled, nearArmRollBound, worldArmRollBound;
            public PlayerArmRollSolver.Result nearArmRoll, worldArmRoll;
            public List<GripSurfaceSample> gripSurfaces = new List<GripSurfaceSample>();
        }
        [Serializable] private sealed class Report
        {
            public string status = "WAIT", error, directory, startedAtUtc, originalScene;
            public bool corePreviewOnly;
            public bool restored, fixtureDestroyed, sceneUnloaded;
            public int evaluatedSamples, failures;
            public float initialGripToTipMeters, initialNearArcMeters, initialWorldArcMeters;
            public float maxTipPixels, maxNearGripMeters, maxWorldGripMeters, maxArcLengthDeltaMeters;
            public float maxSegmentLengthDeltaMeters, maxShaftVertexDeltaMeters, maxBoneLengthDeltaMeters;
            public int bakedBristleSamples;
            public float maxBakedTipPixels, maxNearMeshSocketGapMeters, maxWorldMeshSocketGapMeters, maxNearSplayWeight, maxWorldSplayWeight;
            public List<BristleLandmark> bristleLandmarks = new List<BristleLandmark>();
            public int gripSurfaceSamples, unprovenGripMargins;
            public float maxPrimaryGripSurfaceDeltaMeters, maxGripContactGapUpperMeters, maxGripPenetrationUpperMeters;
            public Source gripReference;
            public string gripContinuity = "Each near/world reference is generated from its own fresh imported model with the independent static calibration/corrective recipe, not from ApplyFrame or a previous index order. Full rest vertices, triangles, bindposes, native weights, shape frames and bone names are hashed. Every path sample uses actual BakeMesh after skin matrices; source-index vertex movement bounds every incident triangle point by convex interpolation. The independently measured finite-shaft reference signed-distance lower bound minus hand/shaft movement must remain >=-0.5mm, and an actual finger contact witness upper gap must remain <=1.5mm. A failed conservative bound means contact is unproven, not an invented observed penetration. Intentional ring/pinky pen-up release and wrist/palm visual quality are separate.";
            public string diagnosticGuards = "Tip 2px; socket attachment and limb-length drift 0.001m; unchanged rigid shaft vertices and bristle segment/arc lengths 0.0001m. These numerical instrumentation guards do not replace the approved skin-contact gap/penetration criteria or manual visual gate.";
            public List<Sample> samples = new List<Sample>();
            public List<string> failureDetails = new List<string>(), images = new List<string>();
            public List<Source> sources = new List<Source>();
            public string scope = "STATIC_IK_ONLY: fresh V2 world/near/brush imports and cloned profiles; direct rest restoration then PlayerVisualRig.ApplyFrame, with no production controller, Animator.Update, animation clip, retargeting, input/recognition, locomotion, Cloth or full RIG_PASS claim.";
            public string timing = "30/60/120 are explicit ApplyFrame integration steps, not a GPU FPS benchmark. Slow condition keeps visual dt=1/60 and passes scaled dt=0.25/60. No global clock/input changes. Fixed path samples intentionally exercise different visual velocities at each step size.";
            public string alignment = "Actual rendered bristle endpoint is a fixed original source vertex selected once from rest BakeMesh by maximum projection along bristle-root to TipSocket. The same source index is read from actual SMR BakeMesh AFTER UpdateAllSkinnedMeshes for every sample; its near screen error must be <=2px. Socket error is retained separately and never substitutes for mesh endpoint error. World mesh tip uses the body plane and is reported separately, not required to share near-tip 3D position. Primary skin/shaft contact continuity is measured separately in the shared brush Grip frame for every sample.";
            public string captures = "Direct static pose captured after UpdateAllSkinnedMeshes with per-render skin-matrix refresh on temporary renderers. Observer temporarily enables the world representation and hides near layer31; no screen ink/input exists in this isolated static fixture.";
        }
        private struct Instruction
        {
            public string Pattern; public int Hz, Ordinal; public float ScaledRatio, Pitch, Yaw;
            public Vector2 Point; public bool Stroking, Capture;
        }
        private sealed class RestTransform
        {
            public Transform Transform; public Vector3 Position, Scale; public Quaternion Rotation;
            public void Restore() { Transform.localPosition = Position; Transform.localRotation = Rotation; Transform.localScale = Scale; }
        }
        private sealed class BrushState
        {
            public Transform Root, Grip, Tip;
            public Transform[] Chain;
            public float[] Segments;
            public float Arc;
            public BrushBristleRig Rig;
            public Renderer Shaft;
            public Vector3[] ShaftRest;
            public SkinnedMeshRenderer BristleSkin;
            public BristleLandmark Landmark;
        }

        public static bool IsRunning => _active != null;
        public static string Run() => Begin();
        public static string Begin() => BeginSession(false);
        /// <summary>Three representative captures only; never starts the full 464-sample run.</summary>
        public static string BeginCorePreview() => BeginSession(true);
        private static string BeginSession(bool corePreviewOnly)
        {
            if (_active != null) return Status();
            if (!Application.isPlaying || EditorApplication.isPaused || SceneManager.GetActiveScene().name != "C2_PlayerV2Validation")
                return "WAIT: unpaused Play Mode in C2_PlayerV2Validation is required.";
            if (DosaV2ClothDiagnostics.IsRunning) return "WAIT: finish the independent Cloth fixture before starting the static IK fixture.";
            var session = new Session(corePreviewOnly); _active = session;
            try { session.Start(); }
            catch (Pending e) { session.Finish("WAIT", e.Message); }
            catch (Exception e) { session.Finish("FAIL", e.ToString()); }
            return Status();
        }
        public static string Status()
        {
            Report report = _active != null ? _active.Result : _last;
            return report == null ? "NOT_STARTED" : JsonUtility.ToJson(new StatusView { status = report.status, error = report.error,
                scope = report.scope,
                directory = report.directory, samples = report.evaluatedSamples, failures = report.failures, maxTipPixels = report.maxTipPixels,
                bakedSamples = report.bakedBristleSamples, maxBakedTipPixels = report.maxBakedTipPixels, gripSurfaceSamples = report.gripSurfaceSamples,
                unprovenGripMargins = report.unprovenGripMargins, maxGripSurfaceDeltaMeters = report.maxPrimaryGripSurfaceDeltaMeters, restored = report.restored });
        }
        public static string Stop() { _active?.Finish("STOPPED", "Stopped before all static IK samples completed."); return Status(); }
        [Serializable] private sealed class StatusView { public string status, error, directory, scope; public int samples, bakedSamples, failures, gripSurfaceSamples, unprovenGripMargins; public float maxTipPixels, maxBakedTipPixels, maxGripSurfaceDeltaMeters; public bool restored; }

        private sealed class Session
        {
            public readonly Report Result = new Report();
            private readonly bool _corePreviewOnly;
            public Session(bool corePreviewOnly)
            {
                _corePreviewOnly = corePreviewOnly; Result.corePreviewOnly = corePreviewOnly;
                if (!corePreviewOnly) return;
                Result.scope = "CORE_PREVIEW_ONLY: exactly three representative direct-IK samples (central, circle153 target, rapid64 target), six world/near images, actual baked bristle tip, socket and bone-length checks. No 464-path replay, animation, Cloth, full skin-contact revalidation or RIG_PASS.";
                Result.timing = "Three sequential ApplyFrame calls at the representative 60/60/30Hz integration steps. The original camera/viewport targets are retained, but their preceding full-path state history is intentionally not replayed. This is a quick visual draft, not a frame-rate or continuity certification.";
                Result.gripContinuity = "NOT_REMEASURED_IN_CORE_PREVIEW: expensive independent triangle-wide grip-reference construction is omitted. Authored finger calibration, actual grip socket attachment and visible skin remain present for manual review. Existing full-run grip evidence is retained in its separate report.";
            }
            private Scene _scene;
            private GameObject _root;
            private Animator _world, _near;
            private Camera _camera, _observer;
            private RenderTexture _target;
            private Mesh _baked;
            private PlayerVisualRig _rig;
            private DosaV2PoseTrajectoryWriter _trajectory;
            private PlayerVisualProfileSO _visual;
            private DrawingPoseProfileSO _drawing;
            private BrushDeformationProfileSO _bristleProfile;
            private BrushState _worldBrush, _nearBrush;
            private GripState[] _gripStates;
            private readonly List<RestTransform> _rest = new List<RestTransform>();
            private readonly List<(Transform a, Transform b, float length)> _boneLengths = new List<(Transform, Transform, float)>();
            private readonly List<Instruction> _instructions = new List<Instruction>();
            private Renderer[] _worldRenderers, _nearRenderers;
            private int _index, _posedFrame = -1, _capturedFrame = -1;
            private bool _finished;
            private Sample _current;
            private double _started;

            public void Start()
            {
                _started = Time.realtimeSinceStartupAsDouble;
                Result.startedAtUtc = DateTimeOffset.UtcNow.ToString("O"); Result.originalScene = SceneManager.GetActiveScene().path;
                Result.directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2", (_corePreviewOnly ? "core-drawing-preview-" : "static-drawing-") + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")));
                Directory.CreateDirectory(Result.directory);
                _scene = SceneManager.CreateScene("DosaV2StaticIK_" + Guid.NewGuid().ToString("N"));
                _root = new GameObject("DosaV2StaticIKFixture"); SceneManager.MoveGameObjectToScene(_root, _scene);
                _root.transform.position = new Vector3(50f, 0f, 50f);
                _world = Model(DosaV2PlayerBuilder.WorldModel, "WorldBody").GetComponent<Animator>();
                _near = Model(DosaV2PlayerBuilder.ArmsModel, "NearArms").GetComponent<Animator>();
                Need(_world != null && _near != null && new[] { _world, _near }.All(a => a.avatar != null && a.avatar.isValid && a.isHuman), "Fresh valid V2 Humanoid world/near imports are required.");
                foreach (var animator in new[] { _world, _near })
                {
                    Need(animator.runtimeAnimatorController == null, "An imported production controller is not permitted.");
                    animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    foreach (var transform in animator.GetComponentsInChildren<Transform>(true))
                        _rest.Add(new RestTransform { Transform = transform, Position = transform.localPosition, Rotation = transform.localRotation, Scale = transform.localScale });
                    foreach (var pair in new[] { (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm), (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
                        (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm), (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
                        (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg), (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot) })
                    {
                        Transform a = animator.GetBoneTransform(pair.Item1), b = animator.GetBoneTransform(pair.Item2);
                        Need(a != null && b != null, "Required limb mapping is absent."); _boneLengths.Add((a, b, Vector3.Distance(a.position, b.position)));
                    }
                    Need(Find(animator.transform, "RightBrushGrip") != null, "Authored RightBrushGrip is required; no fallback hand offset is inferred.");
                }
                _visual = CloneProfile<PlayerVisualProfileSO>(DosaV2PlayerBuilder.VisualProfilePath); _visual.Controller = null; _visual.FeetWeight = 0f;
                _drawing = CloneProfile<DrawingPoseProfileSO>(DosaV2PlayerBuilder.DrawingProfilePath);
                _bristleProfile = CloneProfile<BrushDeformationProfileSO>(DosaV2PlayerBuilder.BrushProfilePath);
                string calibration = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/PlayerV2/Calibration/unity-grip-offsets.json"));
                Need(File.Exists(calibration), "Actual imported-hand quaternion calibration is required."); AddSource(calibration);
                var offsets = JsonUtility.FromJson<Calibration>(File.ReadAllText(calibration));
                string[] expected = new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" }.SelectMany(f => Enumerable.Range(1, 3).Select(j => "RightHand" + f + j)).ToArray();
                var rightOffsets = offsets?.offsets?.Where(o => o.name != null && o.name.StartsWith("RightHand", StringComparison.Ordinal)).ToArray();
                Need(rightOffsets != null && rightOffsets.Length == 15 && rightOffsets.Select(o => o.name).SequenceEqual(expected), "Calibration must contain all 15 named right finger offsets in runtime order.");
                _drawing.CalibratedRightFingerOffsets = rightOffsets.Select(o => o.offset).ToArray();
                Need(_drawing.CalibratedRightFingerOffsets.All(q => Finite(q) && Mathf.Abs(Quaternion.Dot(q, q) - 1f) < .001f), "Calibration includes an invalid quaternion.");
                _baked = new Mesh { name = "StaticIKShaftMeasurement_Temporary" };
                _worldBrush = Brush("WorldBrush"); _nearBrush = Brush("NearBrush");
                if (_corePreviewOnly)
                {
                    _gripStates = Array.Empty<GripState>();
                    foreach (string path in new[] { DosaV2PlayerBuilder.WorldModel, DosaV2PlayerBuilder.ArmsModel, DosaV2PlayerBuilder.BrushModel,
                        "Assets/_Project/Scripts/Presentation/PlayerVisualRig.cs", "Assets/_Project/Scripts/Presentation/PlayerArmRollSolver.cs",
                        "Assets/_Project/Scripts/Editor/Validation/DosaV2DrawingStaticValidation.cs" }) AddSource(ProjectFile(path));
                }
                else BuildGripReferences(offsets);
                Result.initialGripToTipMeters = Vector3.Distance(_nearBrush.Grip.position, _nearBrush.Tip.position);
                Result.initialNearArcMeters = _nearBrush.Arc; Result.initialWorldArcMeters = _worldBrush.Arc;
                _drawing.BrushLength = Result.initialGripToTipMeters;
                // The 0.9m V2 brush needs a deeper display plane than the old
                // 0.42m brush; otherwise the shaft places the hand behind the eye.
                _drawing.PreferredTipDepth = 1.05f; _drawing.MinTipDepth = .82f; _drawing.MaxTipDepth = 1.65f;
                _drawing.WorldPlaneDistance = 1.02f; _visual.RestTipDepth = 1.05f;
                AddSource(ProjectFile(DosaV2PlayerBuilder.ValidationPrefab));
                Need(_drawing.BrushLength > .1f && _drawing.BrushLength < 1.5f, "Actual imported grip-to-tip length is invalid.");
                _camera = Camera("NearStaticIKCamera"); _observer = Camera("WorldStaticIKCamera");
                _target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32); _target.Create();
                _camera.targetTexture = _target; _camera.aspect = 16f / 9f; _camera.fieldOfView = 60f; _camera.nearClipPlane = _visual.NearClipPlane;
                _observer.targetTexture = _target; _observer.aspect = 16f / 9f; _observer.fieldOfView = 38f; _observer.nearClipPlane = .02f;
                _worldRenderers = _world.GetComponentsInChildren<Renderer>(true).Concat(_worldBrush.Root.GetComponentsInChildren<Renderer>(true)).ToArray();
                _nearRenderers = _near.GetComponentsInChildren<Renderer>(true).Concat(_nearBrush.Root.GetComponentsInChildren<Renderer>(true)).ToArray();
                foreach (var transform in _near.GetComponentsInChildren<Transform>(true).Concat(_nearBrush.Root.GetComponentsInChildren<Transform>(true))) transform.gameObject.layer = 31;
                _camera.cullingMask = 1 << 31; _observer.cullingMask = ~(1 << 31);
                foreach (var renderer in _root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { renderer.updateWhenOffscreen = true; renderer.forceMatrixRecalculationPerRender = true; }
                var light = new GameObject("StaticIKEvidenceLight").AddComponent<Light>(); light.transform.SetParent(_root.transform, false);
                light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
                _rig = _root.AddComponent<PlayerVisualRig>(); _rig.Configure(_world, _near, _worldBrush.Root, _nearBrush.Root, _visual, _drawing);
                Need(!_drawing.EnableArmRollRedistribution || (_rig.IsArmRollBound(false) && _rig.IsArmRollBound(true)), "Enabled V2 arm roll requires both actual imported bind-pose chains.");
                _trajectory = new DosaV2PoseTrajectoryWriter(Result.directory, _world, _near);
                Need(_world.runtimeAnimatorController == null && _near.runtimeAnimatorController == null, "Visual binding must keep both controllers empty.");
                BuildInstructions();
                EditorApplication.playModeStateChanged += PlayChanged; AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                Need(Insert(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate), new PlayerLoopSystem { type = typeof(PoseStage), updateDelegate = Evaluate }), "Late pose anchor is unavailable.");
                Need(Insert(ref loop, typeof(UnityEngine.PlayerLoop.PostLateUpdate.UpdateAllSkinnedMeshes), new PlayerLoopSystem { type = typeof(CaptureStage), updateDelegate = CaptureAndAdvance }), "Final skin-matrix capture anchor is unavailable.");
                PlayerLoop.SetPlayerLoop(loop); Result.status = "RUNNING"; Save();
            }

            private GameObject Model(string path, string name)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path); Need(source != null, "Missing fresh import: " + path); AddSource(ProjectFile(path));
                Need(!AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Any(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)), "V2 imports must contain no production animation clips.");
                var copy = Object.Instantiate(source, _root.transform, false); copy.name = name;
                Need(copy.GetComponentsInChildren<Cloth>(true).Length == 0 && copy.GetComponentsInChildren<PlayerSecondaryMotionRig>(true).Length == 0, "Static IK uses fresh imports without Cloth/secondary simulation.");
                var review = AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.ValidationPrefab);
                Need(review != null, "The mapped-material V2 static review prefab is required.");
                foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
                {
                    var matches = review.GetComponentsInChildren<Renderer>(true).Where(r => r.name == renderer.name).ToArray();
                    Need(matches.Length > 0 && matches.All(r => r.sharedMaterials.SequenceEqual(matches[0].sharedMaterials)), renderer.name + ": material mapping is missing or ambiguous.");
                    renderer.sharedMaterials = matches[0].sharedMaterials;
                }
                return copy;
            }
            private T CloneProfile<T>(string path) where T : ScriptableObject
            { var source = AssetDatabase.LoadAssetAtPath<T>(path); Need(source != null, "Missing profile: " + path); AddSource(ProjectFile(path)); return Object.Instantiate(source); }
            private BrushState Brush(string name)
            {
                var root = Model(DosaV2PlayerBuilder.BrushModel, name);
                var state = new BrushState { Root = root.transform, Grip = Find(root.transform, "GripSocket"), Tip = Find(root.transform, "TipSocket"),
                    Chain = Enumerable.Range(1, 6).Select(i => Find(root.transform, "Bristle_" + i.ToString("00"))).ToArray() };
                var skin = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).SingleOrDefault(r => r.name == "DosaBrushV2_Bristles");
                Need(state.Grip != null && state.Tip != null && state.Chain.All(t => t != null) && skin != null, "Authored six-bone brush, sockets and bristle skin are required.");
                state.Rig = root.AddComponent<BrushBristleRig>(); Need(state.Rig.Configure(_bristleProfile, state.Grip, state.Tip, state.Chain, skin), "New brush binding: " + state.Rig.BindingError);
                state.Segments = Enumerable.Range(0, 6).Select(i => Vector3.Distance(state.Chain[i].position, i < 5 ? state.Chain[i + 1].position : state.Tip.position)).ToArray(); state.Arc = state.Segments.Sum();
                state.Shaft = root.GetComponentsInChildren<Renderer>(true).SingleOrDefault(r => r != skin);
                Need(state.Shaft != null, "Exactly one actual rigid shaft renderer is required for shaft-geometry preservation measurement.");
                state.ShaftRest = ShaftVertices(state); Need(state.ShaftRest.Length > 0, "Shaft geometry is empty.");
                state.BristleSkin = skin; state.Landmark = SelectBristleLandmark(state, name); Result.bristleLandmarks.Add(state.Landmark);
                return state;
            }
            private BristleLandmark SelectBristleLandmark(BrushState brush, string representation)
            {
                var skin = brush.BristleSkin; Mesh source = skin.sharedMesh;
                Need(source != null && source.isReadable, "Readable authored bristle mesh is required for the actual endpoint landmark.");
                Need(brush.Rig.SplayAvailable, "The imported actual bristle renderer must contain the authored BristleSplay shape.");
                skin.BakeMesh(_baked, false); Vector3[] vertices = _baked.vertices;
                Need(vertices.Length == source.vertexCount && vertices.Length > 0, "Rest BakeMesh must preserve source vertex indexing.");
                Matrix4x4 matrix = brush.Root.worldToLocalMatrix * skin.localToWorldMatrix;
                Vector3 origin = brush.Root.InverseTransformPoint(brush.Chain[0].position);
                Vector3 socket = brush.Root.InverseTransformPoint(brush.Tip.position); Vector3 axis = (socket - origin).normalized;
                Need(Finite(axis) && axis.sqrMagnitude > .99f, "Bristle root-to-tip rest axis is invalid.");
                Vector3[] inBrush = vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                int best = SelectDistalRestVertex(inBrush, origin, axis, out float projection);
                Need(best >= 0 && projection > .01f, "No finite distal bristle source landmark exists.");
                var landmark = new BristleLandmark { representation = representation, renderer = skin.name, meshPath = AssetDatabase.GetAssetPath(source),
                    sourceVertexIndex = best, sourceVertexCount = vertices.Length, restPointInBrush = inBrush[best], restSocketInBrush = socket,
                    axisInBrush = axis, distalProjectionMeters = projection, restMeshSocketGapMeters = Vector3.Distance(inBrush[best], socket) };
                using (var counts = source.GetBonesPerVertex()) using (var weights = source.GetAllBoneWeights())
                {
                    Need(counts.Length == source.vertexCount, "Full source bristle weight counts are missing."); int offset = 0;
                    for (int i = 0; i < best; i++) offset += counts[i]; float total = 0f;
                    for (int i = 0; i < counts[best]; i++)
                    {
                        Need(offset + i < weights.Length, "Distal source landmark weights are truncated."); var weight = weights[offset + i];
                        Need(weight.boneIndex >= 0 && weight.boneIndex < skin.bones.Length && skin.bones[weight.boneIndex] != null && Finite(weight.weight) && weight.weight >= 0f, "Distal source landmark contains an invalid influence.");
                        landmark.weights.Add(new LandmarkWeight { bone = skin.bones[weight.boneIndex].name, boneIndex = weight.boneIndex, weight = weight.weight }); total += weight.weight;
                    }
                    Need(Mathf.Abs(total - 1f) <= .0001f && landmark.weights.Count > 0, "Distal source landmark weights must sum to one.");
                }
                return landmark;
            }
            private Camera Camera(string name)
            { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, _scene); var camera = obj.AddComponent<Camera>(); camera.enabled = false; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.23f, .25f, .28f); camera.farClipPlane = 20f; return camera; }
            private void BuildGripReferences(Calibration calibration)
            {
                string[] names = new[] { "Right", "Left" }.SelectMany(side => new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" }
                    .SelectMany(finger => Enumerable.Range(1, 3).Select(j => side + "Hand" + finger + j))).ToArray();
                Need(calibration?.offsets != null && calibration.offsets.Length == names.Length && calibration.offsets.Select(o => o.name).SequenceEqual(names), "The independent grip reference requires the complete named right/left calibration.");
                var file = new GripReferenceFile(); var states = new List<GripState>();
                foreach (var item in new[] { ("world", DosaV2PlayerBuilder.WorldModel, _world, _worldBrush), ("near", DosaV2PlayerBuilder.ArmsModel, _near, _nearBrush) })
                {
                    GameObject model = null, prop = null;
                    try
                    {
                        model = Model(item.Item2, "IndependentGripReference_" + item.Item1);
                        prop = Model(DosaV2PlayerBuilder.BrushModel, "IndependentGripReferenceBrush_" + item.Item1);
                        var animator = model.GetComponent<Animator>(); Need(animator != null && animator.runtimeAnimatorController == null, "The independent reference must not evaluate production animation.");
                        foreach (var offset in calibration.offsets)
                        { var bone = Find(model.transform, offset.name); Need(bone != null && Finite(offset.offset) && Mathf.Abs(Quaternion.Dot(offset.offset, offset.offset) - 1f) < .001f, "Independent calibration bone/normalized quaternion missing: " + offset.name); bone.localRotation *= offset.offset; }
                        new PlayerHandGripCorrectives(model.GetComponentsInChildren<Renderer>(true)).Apply(1f, 1f);
                        Transform handGrip = Find(model.transform, "RightBrushGrip"), grip = Find(prop.transform, "GripSocket"), tip = Find(prop.transform, "TipSocket");
                        Need(handGrip != null && grip != null && tip != null, "Independent reference requires the authored hand/brush sockets.");
                        Vector3 localGrip = prop.transform.InverseTransformPoint(grip.position);
                        Quaternion localRotation = Quaternion.Inverse(prop.transform.rotation) * grip.rotation;
                        Vector3 axisInGrip = grip.InverseTransformDirection(tip.position - grip.position).normalized;
                        prop.transform.rotation = handGrip.rotation * Quaternion.FromToRotation(axisInGrip, Vector3.up) * Quaternion.Inverse(localRotation);
                        prop.transform.position = handGrip.position - prop.transform.TransformVector(localGrip);
                        var hand = SingleHand(model.transform); var actualHand = SingleHand(item.Item3.transform);
                        Need(hand.sharedMesh == actualHand.sharedMesh && DeformationHash(hand) == DeformationHash(actualHand), "Independent/current hand source indexing, full weights, bindposes or shapes differ; cross-representation remapping is forbidden.");
                        Renderer shaft = prop.GetComponentsInChildren<Renderer>(true).SingleOrDefault(r => r.name == "DosaBrushV2_Handle");
                        Need(shaft != null, "Independent actual shaft renderer is missing or ambiguous.");
                        Vector3[] handVertices = BakedInFrame(hand, grip), shaftVertices = BakedInFrame(shaft, grip);
                        Mesh handMesh = hand.sharedMesh, shaftMesh = SourceMesh(shaft);
                        Need(handVertices.Length == handMesh.vertexCount && shaftVertices.Length == shaftMesh.vertexCount, "Reference BakeMesh/source indexing mismatch.");
                        Need(DeformationHash(shaft) == DeformationHash(item.Item4.Shaft), "Independent/current brush shaft geometry differs.");
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(handMesh, out string guid, out long localId);
                        var reference = new GripReferenceRecord { representation = item.Item1, modelPath = item.Item2, modelSha256 = Sha(ProjectFile(item.Item2)),
                            meshGuid = guid, meshLocalId = localId, deformationSha256 = DeformationHash(hand), shaftDeformationSha256 = DeformationHash(shaft),
                            sourceVertexCount = handMesh.vertexCount, sourceTriangleCount = handMesh.triangles.Length / 3, handInGrip = handVertices, shaftInGrip = shaftVertices };
                        var tree = new ShaftSurface(shaftVertices, shaftMesh.triangles);
                        string[] labels = DominantBoneNames(hand); int[] triangles = handMesh.triangles;
                        string[] primary = { "Thumb", "Index", "Middle" };
                        var fingers = primary.Select(f => new GripFingerReference { finger = f, sourceVertices = Enumerable.Range(0, labels.Length)
                            .Where(i => labels[i].StartsWith("RightHand" + f, StringComparison.Ordinal)).ToArray(), baselineMinimumSignedLowerMeters = float.PositiveInfinity }).ToArray();
                        foreach (var finger in fingers)
                        {
                            Need(finger.sourceVertices.Length > 0, "No actual primary-finger vertices: " + finger.finger);
                            float nearest = float.PositiveInfinity; int witness = -1;
                            foreach (int i in finger.sourceVertices) { float distance = tree.Unsigned(handVertices[i]); if (distance < nearest) { nearest = distance; witness = i; } }
                            finger.witnessSourceVertex = witness; finger.witnessGapUpperMeters = nearest + ContactNumericalGuard;
                            Need(finger.witnessGapUpperMeters <= .0015f, "Independent reference has no verified <=1.5mm original skin witness for " + finger.finger);
                        }
                        var retained = new List<GripTriangleReference>();
                        for (int t = 0; t < triangles.Length; t += 3)
                        {
                            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2], mask = 0;
                            for (int f = 0; f < 3; f++)
                                if (new[] { a, b, c }.Any(i => labels[i].StartsWith("RightHand" + primary[f], StringComparison.Ordinal))) mask |= 1 << f;
                            if (mask == 0) continue;
                            // Every barycentric point of the original triangle is covered by this
                            // adaptive signed-distance Lipschitz lower bound, not sparse vertices alone.
                            float lower = tree.TriangleSignedLowerBound(handVertices[a], handVertices[b], handVertices[c]);
                            retained.Add(new GripTriangleReference { sourceTriangle = t / 3, a = a, b = b, c = c, primaryMask = mask, signedLowerMeters = lower });
                            for (int f = 0; f < 3; f++) if ((mask & (1 << f)) != 0) fingers[f].baselineMinimumSignedLowerMeters = Mathf.Min(fingers[f].baselineMinimumSignedLowerMeters, lower);
                        }
                        foreach (var finger in fingers) Need(Finite(finger.baselineMinimumSignedLowerMeters) && finger.baselineMinimumSignedLowerMeters >= -.0005f, "Independent primary reference penetration could not be bounded within 0.5mm: " + finger.finger);
                        reference.fingers = fingers; reference.triangles = retained.ToArray(); reference.signedDistanceQueries = tree.Queries;
                        file.representations.Add(reference); states.Add(new GripState { Hand = actualHand, SourceMesh = actualHand.sharedMesh, Brush = item.Item4, Reference = reference, Delta = new float[handVertices.Length] });
                    }
                    finally { if (model != null) Object.DestroyImmediate(model); if (prop != null) Object.DestroyImmediate(prop); }
                }
                _gripStates = states.ToArray(); string path = Path.Combine(Result.directory, "grip-continuity-reference.json");
                foreach (string source in new[] { DosaV2PlayerBuilder.WorldModel, DosaV2PlayerBuilder.ArmsModel, DosaV2PlayerBuilder.BrushModel }) AddSource(ProjectFile(source) + ".meta");
                AddSource(ProjectFile("Assets/_Project/Scripts/Editor/Validation/DosaV2StaticGripInspection.cs"));
                AddSource(ProjectFile("Assets/_Project/Scripts/Presentation/PlayerHandGripCorrectives.cs"));
                AddSource(ProjectFile("Assets/_Project/Scripts/Presentation/PlayerVisualRig.cs"));
                AddSource(ProjectFile("Assets/_Project/Scripts/Presentation/PlayerArmRollSolver.cs"));
                AddSource(ProjectFile("Assets/_Project/Scripts/Editor/Validation/DosaV2DrawingStaticValidation.cs"));
                file.sources = Result.sources.ToArray(); File.WriteAllText(path, JsonUtility.ToJson(file, true)); Result.gripReference = new Source { path = path, sha256 = Sha(path) };
            }
            private static SkinnedMeshRenderer SingleHand(Transform root)
            {
                var hands = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name == "DosaV2_Hands").ToArray();
                Need(hands.Length == 1, "Exactly one named hand skin is required in each representation."); return hands[0];
            }
            private Vector3[] BakedInFrame(Renderer renderer, Transform frame)
            {
                Vector3[] vertices;
                if (renderer is SkinnedMeshRenderer skin) { skin.BakeMesh(_baked, false); vertices = _baked.vertices; }
                else vertices = SourceMesh(renderer).vertices;
                Matrix4x4 matrix = Matrix4x4.TRS(frame.position, frame.rotation, Vector3.one).inverse * renderer.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++) { vertices[i] = matrix.MultiplyPoint3x4(vertices[i]); Need(Finite(vertices[i]), "Nonfinite actual baked contact surface."); }
                return vertices;
            }
            private void MeasureGripContinuity()
            {
                Need(_gripStates != null && _gripStates.Length == 2 && Result.gripReference != null, "Independent near/world skin references were not constructed.");
                foreach (var state in _gripStates)
                {
                    Need(state.Hand.sharedMesh == state.SourceMesh, "Current hand source mesh identity changed during IK.");
                    Vector3[] actual = BakedInFrame(state.Hand, state.Brush.Grip), shaft = BakedInFrame(state.Brush.Shaft, state.Brush.Grip);
                    var reference = state.Reference;
                    Need(actual.Length == reference.handInGrip.Length && shaft.Length == reference.shaftInGrip.Length, "Current/reference source vertex correspondence changed; no sample may be skipped.");
                    float shaftDelta = 0f;
                    for (int i = 0; i < shaft.Length; i++) shaftDelta = Mathf.Max(shaftDelta, Vector3.Distance(shaft[i], reference.shaftInGrip[i]));
                    for (int i = 0; i < actual.Length; i++) state.Delta[i] = Vector3.Distance(actual[i], reference.handInGrip[i]);
                    for (int f = 0; f < reference.fingers.Length; f++)
                    {
                        var finger = reference.fingers[f]; float maxPrimary = 0f, maxCorners = 0f, lower = float.PositiveInfinity; int triangles = 0;
                        foreach (int i in finger.sourceVertices) maxPrimary = Mathf.Max(maxPrimary, state.Delta[i]);
                        foreach (var tri in reference.triangles)
                        {
                            if ((tri.primaryMask & (1 << f)) == 0) continue; triangles++;
                            float delta = Mathf.Max(state.Delta[tri.a], Mathf.Max(state.Delta[tri.b], state.Delta[tri.c])); maxCorners = Mathf.Max(maxCorners, delta);
                            lower = Mathf.Min(lower, tri.signedLowerMeters - delta - shaftDelta);
                        }
                        float gap = finger.witnessGapUpperMeters + state.Delta[finger.witnessSourceVertex] + shaftDelta;
                        var row = new GripSurfaceSample { representation = reference.representation, finger = finger.finger, checkedVertices = finger.sourceVertices.Length,
                            checkedTriangles = triangles, witnessSourceVertex = finger.witnessSourceVertex, maxPrimaryVertexDeltaMeters = maxPrimary,
                            maxIncidentTriangleCornerDeltaMeters = maxCorners, shaftDeltaMeters = shaftDelta, contactGapUpperMeters = gap, penetrationUpperMeters = Mathf.Max(0f, -lower),
                            remainingGapMarginMeters = .0015f - gap, remainingPenetrationMarginMeters = .0005f + lower };
                        row.marginProven = triangles > 0 && Finite(gap) && Finite(lower) && gap <= .0015f && lower >= -.0005f;
                        _current.gripSurfaces.Add(row); Result.gripSurfaceSamples++;
                        if (!row.marginProven) Result.unprovenGripMargins++;
                        Check(row.marginProven, reference.representation + "/" + finger.finger + ": actual baked skin/shaft movement exhausted the independently measured 1.5mm/0.5mm margin; contact is unproven, not an asserted observed collision.");
                        Result.maxPrimaryGripSurfaceDeltaMeters = Mathf.Max(Result.maxPrimaryGripSurfaceDeltaMeters, maxPrimary);
                        Result.maxGripContactGapUpperMeters = Mathf.Max(Result.maxGripContactGapUpperMeters, gap);
                        Result.maxGripPenetrationUpperMeters = Mathf.Max(Result.maxGripPenetrationUpperMeters, row.penetrationUpperMeters);
                    }
                }
                _current.gripSurfacesMeasured = true;
            }
            private void BuildInstructions()
            {
                if (_corePreviewOnly)
                {
                    _instructions.Add(new Instruction { Pattern = "central", Point = new Vector2(.5f, .5f), Stroking = true, Capture = true, Hz = 60, ScaledRatio = 1f, Ordinal = 120 });
                    _instructions.Add(new Instruction { Pattern = "circle", Point = new Vector2(.5f, .92f), Stroking = true, Capture = true, Hz = 60, ScaledRatio = 1f, Ordinal = 153 });
                    _instructions.Add(new Instruction { Pattern = "rapid_turn", Point = new Vector2(.02f, .95f), Stroking = true, Capture = true, Hz = 30, ScaledRatio = 1f, Ordinal = 64, Pitch = -80f, Yaw = 74f });
                    return;
                }
                foreach (var condition in new[] { (30, 1f), (60, 1f), (120, 1f), (60, .25f) })
                {
                    for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) Add("grid", new Vector2(x * .5f, y * .5f), true, (x == 1 && y == 1));
                    for (int i = 0; i < 20; i++) Add("line", new Vector2(.05f + .9f * i / 19f, .5f), true, i == 10);
                    for (int i = 0; i <= 32; i++) { float t = i * Mathf.PI * 2f / 32f; Add("circle", new Vector2(.5f + .42f * Mathf.Cos(t), .5f + .42f * Mathf.Sin(t)), true, i == 8); }
                    for (int i = 0; i < 20; i++) Add("rapid_turn", new Vector2(i % 2 == 0 ? .02f : .98f, i % 4 < 2 ? .05f : .95f), true, i == 9, i % 2 == 0 ? -80f : 80f, i * 37f);
                    for (int i = 0; i < 24; i++) Add("multi_stroke", new Vector2(.1f + .8f * (i % 6) / 5f, .2f + .6f * (i / 6) / 3f), i % 6 != 0, i == 12);
                    for (int i = 0; i < 10; i++) Add("pen_up_recovery", new Vector2(.5f, .5f), false, i == 9);
                    void Add(string pattern, Vector2 point, bool stroke, bool capture, float pitch = 0f, float yaw = 0f)
                    { _instructions.Add(new Instruction { Pattern = pattern, Point = point, Stroking = stroke, Capture = capture && condition.Item1 == 60 && condition.Item2 == 1f, Hz = condition.Item1, ScaledRatio = condition.Item2, Ordinal = _instructions.Count, Pitch = pitch, Yaw = yaw }); }
                }
            }
            private void Evaluate()
            {
                if (_finished || _posedFrame == Time.frameCount) return; _posedFrame = Time.frameCount;
                try
                {
                    Need(Time.realtimeSinceStartupAsDouble - _started < 180d, "Static IK fixture exceeded the 180s wall-time limit.");
                    var instruction = _instructions[_index]; foreach (var rest in _rest) rest.Restore();
                    // Explicit static lowered-arm diagnostic pose, without clips.
                    foreach (var animator in new[] { _world, _near })
                    {
                        var left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                        var right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                        left.rotation = Quaternion.AngleAxis(65f, animator.transform.forward) * left.rotation;
                        right.rotation = Quaternion.AngleAxis(-65f, animator.transform.forward) * right.rotation;
                    }
                    _camera.transform.SetPositionAndRotation(_root.transform.position + new Vector3(0f, 1.65f, -.15f), Quaternion.Euler(instruction.Pitch, instruction.Yaw, 0f));
                    Vector2 screen = _camera.ViewportToScreenPoint(instruction.Point); float dt = 1f / instruction.Hz;
                    var frame = new PlayerVisualFrame { Camera = _camera, HasPointer = true, PointerScreen = screen, Drawing = true, Stroking = instruction.Stroking, Combat = true, Grounded = false, WorldBodyVisible = true };
                    _rig.ApplyFrame(frame, dt, dt * instruction.ScaledRatio);
                    var d = _rig.Diagnostics; Vector3 actual = _nearBrush.Tip.position; Vector3 projected = _camera.WorldToScreenPoint(actual);
                    _current = new Sample { pattern = instruction.Pattern, ordinal = instruction.Ordinal, requestedHz = instruction.Hz, unscaledStep = dt, scaledStep = dt * instruction.ScaledRatio,
                        pitch = instruction.Pitch, yaw = instruction.Yaw, viewport = instruction.Point, stroking = instruction.Stroking, reachable = d.TipReachable, flexible = d.FlexibleBristles,
                        actualTipWorld = actual, tipPixels = Vector2.Distance(projected, screen), worldTipScreenPixels = Vector2.Distance(_camera.WorldToScreenPoint(_worldBrush.Tip.position), screen),
                        nearGripMeters = Vector3.Distance(Find(_near.transform, "RightBrushGrip").position, _nearBrush.Grip.position),
                        worldGripMeters = Vector3.Distance(Find(_world.transform, "RightBrushGrip").position, _worldBrush.Grip.position), bendDegrees = d.BristleBendDegrees };
                    _current.armRollEnabled = _drawing.EnableArmRollRedistribution;
                    _current.nearArmRollBound = _rig.IsArmRollBound(true); _current.worldArmRollBound = _rig.IsArmRollBound(false);
                    _current.nearArmRoll = _rig.ArmRollDiagnostics(true); _current.worldArmRoll = _rig.ArmRollDiagnostics(false);
                    Check(!_current.armRollEnabled || (_current.nearArmRollBound && _current.worldArmRollBound && _current.nearArmRoll.Applied), "Enabled V2 arm roll did not execute on the actual near representation.");
                    _current.finite = Finite(actual) && Finite(projected) && projected.z > 0f && Finite(_worldBrush.Tip.position)
                        && Finite(_current.nearGripMeters) && Finite(_current.worldGripMeters) && _rest.All(r => Finite(r.Transform.position) && Finite(r.Transform.rotation));
                    MeasureBrush(_nearBrush, _current, true); MeasureBrush(_worldBrush, _current, false);
                    _current.maxBoneLengthDeltaMeters = _boneLengths.Max(pair => Mathf.Abs(Vector3.Distance(pair.a.position, pair.b.position) - pair.length));
                    Check(_current.finite && _current.flexible && _current.reachable && _current.tipPixels <= 2f, "Flexible actual near tip is nonfinite, unreachable or exceeds 2px.");
                    Check(_current.nearGripMeters <= .001f && _current.worldGripMeters <= .001f, "Actual near/world grip sockets separate by more than 1mm.");
                    Check(_current.maxArcLengthDeltaMeters <= .0001f && _current.maxSegmentLengthDeltaMeters <= .0001f && _current.maxShaftVertexDeltaMeters <= .0001f, "Rigid shaft or bristle centerline length changed by more than 0.1mm.");
                    Check(_current.maxBoneLengthDeltaMeters <= .001f, "Display IK changed a measured limb length by more than 1mm.");
                    Check(_world.runtimeAnimatorController == null && _near.runtimeAnimatorController == null, "Production controller was attached during static evaluation.");
                    Result.maxTipPixels = Mathf.Max(Result.maxTipPixels, _current.tipPixels); Result.maxNearGripMeters = Mathf.Max(Result.maxNearGripMeters, _current.nearGripMeters); Result.maxWorldGripMeters = Mathf.Max(Result.maxWorldGripMeters, _current.worldGripMeters);
                    Result.maxArcLengthDeltaMeters = Mathf.Max(Result.maxArcLengthDeltaMeters, _current.maxArcLengthDeltaMeters); Result.maxSegmentLengthDeltaMeters = Mathf.Max(Result.maxSegmentLengthDeltaMeters, _current.maxSegmentLengthDeltaMeters);
                    Result.maxShaftVertexDeltaMeters = Mathf.Max(Result.maxShaftVertexDeltaMeters, _current.maxShaftVertexDeltaMeters); Result.maxBoneLengthDeltaMeters = Mathf.Max(Result.maxBoneLengthDeltaMeters, _current.maxBoneLengthDeltaMeters);
                    Result.samples.Add(_current); Result.evaluatedSamples++;
                }
                catch (Exception e) { Finish("FAIL", e.GetBaseException().Message); }
            }
            private void MeasureBrush(BrushState state, Sample sample, bool near)
            {
                float arc = 0f;
                for (int i = 0; i < 6; i++)
                { float segment = Vector3.Distance(state.Chain[i].position, i < 5 ? state.Chain[i + 1].position : state.Tip.position); sample.finite &= Finite(segment) && Finite(state.Chain[i].rotation); arc += segment; sample.maxSegmentLengthDeltaMeters = Mathf.Max(sample.maxSegmentLengthDeltaMeters, Mathf.Abs(segment - state.Segments[i])); }
                if (near) sample.nearArcMeters = arc; else sample.worldArcMeters = arc;
                sample.maxArcLengthDeltaMeters = Mathf.Max(sample.maxArcLengthDeltaMeters, Mathf.Abs(arc - state.Arc));
                Vector3[] vertices = ShaftVertices(state); Need(vertices.Length == state.ShaftRest.Length, "Actual shaft vertex count changed.");
                for (int i = 0; i < vertices.Length; i++) { sample.finite &= Finite(vertices[i]); sample.maxShaftVertexDeltaMeters = Mathf.Max(sample.maxShaftVertexDeltaMeters, Vector3.Distance(vertices[i], state.ShaftRest[i])); }
            }
            private Vector3[] ShaftVertices(BrushState state)
            {
                Vector3[] vertices;
                if (state.Shaft is SkinnedMeshRenderer skin) { skin.BakeMesh(_baked, false); vertices = _baked.vertices; }
                else { var filter = state.Shaft.GetComponent<MeshFilter>(); Need(filter != null && filter.sharedMesh != null && filter.sharedMesh.isReadable, "Readable shaft mesh is required."); vertices = filter.sharedMesh.vertices; }
                Matrix4x4 matrix = state.Root.worldToLocalMatrix * state.Shaft.localToWorldMatrix; return vertices.Select(matrix.MultiplyPoint3x4).ToArray();
            }
            private void Check(bool success, string message)
            { if (success) return; Result.failures++; if (Result.failureDetails.Count < 64) Result.failureDetails.Add("Sample " + _instructions[_index].Ordinal + " " + _instructions[_index].Pattern + ": " + message); }
            private void CaptureAndAdvance()
            {
                if (_finished || _capturedFrame == Time.frameCount || _posedFrame != Time.frameCount) return; _capturedFrame = Time.frameCount;
                try
                {
                    MeasureBakedBristleTips();
                    if (!_corePreviewOnly) MeasureGripContinuity();
                    _trajectory.Append(_current.ordinal);
                    if (_instructions[_index].Capture || _current.ordinal == 64) CapturePair();
                    if (!_corePreviewOnly && _instructions[_index].Capture && _current.pattern == "circle") CaptureBristleCloseups();
                    _index++;
                    if (_index >= _instructions.Count)
                    {
                        if (Result.bakedBristleSamples != Result.evaluatedSamples || Result.evaluatedSamples != _instructions.Count)
                        { Result.failures++; Result.failureDetails.Add("Not every static IK sample has a final actual baked bristle endpoint measurement."); }
                        if (!_corePreviewOnly && (Result.maxNearSplayWeight <= 0f || Result.maxWorldSplayWeight <= 0f))
                        { Result.failures++; Result.failureDetails.Add("The path did not exercise actual near/world BristleSplay deformation."); }
                        if (!_corePreviewOnly && Result.gripSurfaceSamples != Result.evaluatedSamples * 6)
                        { Result.failures++; Result.failureDetails.Add("Not every static IK sample measured all three primary fingers in both actual skinned representations."); }
                        foreach (var state in _gripStates)
                            if (DeformationHash(state.Hand) != state.Reference.deformationSha256 || DeformationHash(state.Brush.Shaft) != state.Reference.shaftDeformationSha256)
                            { Result.failures++; Result.failureDetails.Add("Source hand/shaft deformation data changed during the fixture."); }
                        Finish(_corePreviewOnly ? (Result.failures == 0 ? "COMPLETE_CORE_PREVIEW_ONLY" : "FAIL_CORE_PREVIEW_ONLY")
                            : (Result.failures == 0 ? "PASS_STATIC_IK_ONLY" : "FAIL_STATIC_IK"), null);
                    }
                }
                catch (Exception e) { Finish("FAIL", e.GetBaseException().Message); }
            }
            private void MeasureBakedBristleTips()
            {
                // This callback runs after the actual skin-matrix update, not the preceding IK step.
                // The landmark is fixed in source vertex space; a new bounds/extreme search on a bent
                // brush could jump to its side and is deliberately not used as the moving endpoint.
                Vector3 near = ActualBakedLandmark(_nearBrush), world = ActualBakedLandmark(_worldBrush);
                Vector3 projected = _camera.WorldToScreenPoint(near); Vector2 target = _camera.ViewportToScreenPoint(_current.viewport);
                _current.nearBakedBristleTipWorld = near; _current.worldBakedBristleTipWorld = world;
                _current.nearBakedTipPixels = Vector2.Distance(projected, target);
                _current.worldBakedTipScreenPixels = Vector2.Distance(_camera.WorldToScreenPoint(world), target);
                _current.nearMeshSocketGapMeters = Vector3.Distance(near, _nearBrush.Tip.position);
                _current.worldMeshSocketGapMeters = Vector3.Distance(world, _worldBrush.Tip.position);
                int splay = _nearBrush.BristleSkin.sharedMesh.GetBlendShapeIndex("BristleSplay");
                _current.nearSplayWeight = splay >= 0 ? _nearBrush.BristleSkin.GetBlendShapeWeight(splay) : 0f;
                int worldSplay = _worldBrush.BristleSkin.sharedMesh.GetBlendShapeIndex("BristleSplay");
                _current.worldSplayWeight = worldSplay >= 0 ? _worldBrush.BristleSkin.GetBlendShapeWeight(worldSplay) : 0f;
                _current.bakedBristleMeasured = true; _current.finite &= Finite(near) && Finite(world) && Finite(projected);
                Check(_current.finite && projected.z > 0f && _current.nearBakedTipPixels <= 2f, "Actual baked bristle source endpoint is nonfinite, behind camera or exceeds 2px; TipSocket alone is insufficient.");
                Result.bakedBristleSamples++;
                Result.maxBakedTipPixels = Mathf.Max(Result.maxBakedTipPixels, _current.nearBakedTipPixels);
                Result.maxNearMeshSocketGapMeters = Mathf.Max(Result.maxNearMeshSocketGapMeters, _current.nearMeshSocketGapMeters);
                Result.maxWorldMeshSocketGapMeters = Mathf.Max(Result.maxWorldMeshSocketGapMeters, _current.worldMeshSocketGapMeters);
                Result.maxNearSplayWeight = Mathf.Max(Result.maxNearSplayWeight, _current.nearSplayWeight);
                Result.maxWorldSplayWeight = Mathf.Max(Result.maxWorldSplayWeight, _current.worldSplayWeight);
            }
            private Vector3 ActualBakedLandmark(BrushState brush)
            {
                brush.BristleSkin.BakeMesh(_baked, false); Vector3[] vertices = _baked.vertices;
                Need(vertices.Length == brush.Landmark.sourceVertexCount && brush.Landmark.sourceVertexIndex < vertices.Length, "Actual bristle BakeMesh changed source landmark indexing.");
                return brush.BristleSkin.transform.TransformPoint(vertices[brush.Landmark.sourceVertexIndex]);
            }
            private void CaptureBristleCloseups()
            {
                Vector3 oldPosition = _observer.transform.position; Quaternion oldRotation = _observer.transform.rotation;
                int mask = _observer.cullingMask; float fov = _observer.fieldOfView, clip = _observer.nearClipPlane;
                try
                {
                    _observer.cullingMask = 1 << 31; _observer.fieldOfView = 32f; _observer.nearClipPlane = .005f;
                    Vector3 center = Vector3.Lerp(_nearBrush.Grip.position, _current.nearBakedBristleTipWorld, .5f);
                    foreach (var view in new[] { ("front", _nearBrush.Grip.forward), ("side", _nearBrush.Grip.right),
                        ("oblique", (_nearBrush.Grip.forward + _nearBrush.Grip.right).normalized) })
                    {
                        _observer.transform.position = center + view.Item2 * 1.45f; _observer.transform.LookAt(center, _nearBrush.Grip.up);
                        Capture(_observer, _current.ordinal.ToString("000") + "_circle_bristle_" + view.Item1 + ".png");
                    }
                }
                finally { _observer.transform.SetPositionAndRotation(oldPosition, oldRotation); _observer.cullingMask = mask; _observer.fieldOfView = fov; _observer.nearClipPlane = clip; }
            }
            private void CapturePair()
            {
                if (_current.ordinal == 153 || _current.ordinal == 64)
                    DosaV2WristCaptureSnapshot.Write(Result.directory, _current.ordinal.ToString("000") + "_" + _current.pattern, _near, _camera);
                Capture(_camera, _current.ordinal.ToString("000") + "_" + _current.pattern + "_near.png");
                var all = _worldRenderers.Concat(_nearRenderers).ToArray(); var enabled = all.Select(r => r.enabled).ToArray(); var shadows = all.Select(r => r.shadowCastingMode).ToArray();
                try
                {
                    foreach (var renderer in _worldRenderers) { renderer.enabled = true; renderer.shadowCastingMode = ShadowCastingMode.On; }
                    foreach (var renderer in _nearRenderers) renderer.enabled = false;
                    _observer.transform.position = _root.transform.position + new Vector3(2f, 1.55f, 3f); _observer.transform.LookAt(_root.transform.position + new Vector3(0f, 1f, .15f));
                    Capture(_observer, _current.ordinal.ToString("000") + "_" + _current.pattern + "_world.png");
                }
                finally { for (int i = 0; i < all.Length; i++) { all[i].enabled = enabled[i]; all[i].shadowCastingMode = shadows[i]; } }
            }
            private void Capture(Camera camera, string name)
            {
                var previous = RenderTexture.active; var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                try
                { RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = _target }); RenderTexture.active = _target; pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply(); string path = Path.Combine(Result.directory, name); File.WriteAllBytes(path, pixels.EncodeToPNG()); Result.images.Add(path); }
                finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
            }
            private void AddSource(string path) { if (Result.sources.All(s => s.path != path)) Result.sources.Add(new Source { path = path, sha256 = Sha(path) }); }
            private void PlayChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Finish("STOPPED", "Play Mode exited."); }
            private void BeforeReload() => Finish("STOPPED", "Assembly reload.");
            public void Finish(string status, string error)
            {
                if (_finished) return; _finished = true; Result.status = status; Result.error = error;
                EditorApplication.playModeStateChanged -= PlayChanged; AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
                try
                {
                    var loop = PlayerLoop.GetCurrentPlayerLoop(); if (Remove(ref loop)) PlayerLoop.SetPlayerLoop(loop);
                    _trajectory?.Complete(status == "PASS_STATIC_IK_ONLY" || status == "FAIL_STATIC_IK"
                        || status == "COMPLETE_CORE_PREVIEW_ONLY" || status == "FAIL_CORE_PREVIEW_ONLY");
                    if (_camera != null) _camera.targetTexture = null; if (_observer != null) _observer.targetTexture = null;
                    if (_root != null) Object.DestroyImmediate(_root); Result.fixtureDestroyed = _root == null;
                    foreach (var temporary in new Object[] { _visual, _drawing, _bristleProfile, _baked }) if (temporary != null) Object.DestroyImmediate(temporary);
                    if (_target != null) { _target.Release(); Object.DestroyImmediate(_target); }
                    if (_scene.IsValid() && _scene.isLoaded && Application.isPlaying)
                    { var operation = SceneManager.UnloadSceneAsync(_scene); if (operation != null) { Result.status = "RESTORING"; Save(); operation.completed += _ => { Result.sceneUnloaded = true; Complete(status); }; return; } }
                    Result.sceneUnloaded = !_scene.IsValid() || !_scene.isLoaded; Complete(status);
                }
                catch (Exception e) { Result.error = (Result.error ?? "") + " RESTORE: " + e.Message; Complete("FAIL"); }
            }
            private void Complete(string status) { Result.status = status; Result.restored = Result.fixtureDestroyed && Result.sceneUnloaded; _last = Result; _active = null; Save(); }
            private void Save()
            { if (string.IsNullOrEmpty(Result.directory)) return; string json = JsonUtility.ToJson(Result, true); File.WriteAllText(Path.Combine(Result.directory, "report.json"), json); File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,
                "../Screenshots/PlayerDosaV2/" + (_corePreviewOnly ? "core-drawing-preview.json" : "static-drawing-validation.json"))), json); }
        }
        private static Mesh SourceMesh(Renderer renderer)
        {
            Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            Need(mesh != null && mesh.isReadable, "Readable actual source geometry is required for contact identity."); return mesh;
        }
        private static string[] DominantBoneNames(SkinnedMeshRenderer skin)
        {
            Mesh mesh = SourceMesh(skin); Transform[] bones = skin.bones; var result = new string[mesh.vertexCount];
            using (var counts = mesh.GetBonesPerVertex()) using (var weights = mesh.GetAllBoneWeights())
            {
                Need(counts.Length == result.Length, "Full contact skin influence counts are absent."); int offset = 0;
                for (int v = 0; v < result.Length; v++)
                {
                    Need(counts[v] > 0 && counts[v] <= 4, "Contact identity requires the full approved 1..4 influence stream.");
                    float total = 0f, maximum = -1f; int strongest = -1;
                    for (int j = 0; j < counts[v]; j++)
                    {
                        Need(offset < weights.Length, "Truncated contact influence stream."); var w = weights[offset++];
                        Need(Finite(w.weight) && w.weight >= 0f && w.boneIndex >= 0 && w.boneIndex < bones.Length && bones[w.boneIndex] != null, "Invalid contact influence or mapped source bone.");
                        total += w.weight; if (w.weight > maximum) { maximum = w.weight; strongest = w.boneIndex; }
                    }
                    Need(Mathf.Abs(total - 1f) <= .0001f && strongest >= 0, "Actual primary skin weights do not sum to one.");
                    string name = bones[strongest].name; int colon = name.LastIndexOf(':'); result[v] = colon >= 0 ? name.Substring(colon + 1) : name;
                }
                Need(offset == weights.Length, "Unused trailing contact influences prevent exact identity.");
            }
            return result;
        }
        private static string DeformationHash(Renderer renderer)
        {
            Mesh mesh = SourceMesh(renderer);
            using (var buffer = new MemoryStream()) using (var writer = new BinaryWriter(buffer))
            {
                writer.Write("DOSA_CONTACT_DEFORMATION_V1"); Vector3[] vertices = mesh.vertices; writer.Write(vertices.Length);
                foreach (var v in vertices) WriteVector(writer, v);
                int[] triangles = mesh.triangles; writer.Write(triangles.Length); foreach (int index in triangles) writer.Write(index);
                Matrix4x4[] poses = mesh.bindposes; writer.Write(poses.Length);
                foreach (var pose in poses) for (int i = 0; i < 16; i++) { Need(Finite(pose[i]), "Nonfinite source bindpose."); writer.Write(pose[i]); }
                if (renderer is SkinnedMeshRenderer skin)
                {
                    DominantBoneNames(skin); Transform[] bones = skin.bones; writer.Write(bones.Length);
                    foreach (var bone in bones) writer.Write(bone == null ? "<NULL>" : bone.name);
                    using (var counts = mesh.GetBonesPerVertex()) using (var weights = mesh.GetAllBoneWeights())
                    {
                        writer.Write(counts.Length); for (int i = 0; i < counts.Length; i++) writer.Write(counts[i]);
                        writer.Write(weights.Length); for (int i = 0; i < weights.Length; i++) { writer.Write(weights[i].boneIndex); writer.Write(weights[i].weight); }
                    }
                }
                else writer.Write(-1);
                writer.Write(mesh.blendShapeCount); var delta = new Vector3[vertices.Length];
                for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                {
                    writer.Write(mesh.GetBlendShapeName(shape)); int frames = mesh.GetBlendShapeFrameCount(shape); writer.Write(frames);
                    for (int frame = 0; frame < frames; frame++)
                    {
                        writer.Write(mesh.GetBlendShapeFrameWeight(shape, frame)); mesh.GetBlendShapeFrameVertices(shape, frame, delta, null, null);
                        foreach (var v in delta) WriteVector(writer, v);
                    }
                }
                writer.Flush(); buffer.Position = 0;
                using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(buffer)).Replace("-", "").ToLowerInvariant();
            }
        }
        private static void WriteVector(BinaryWriter writer, Vector3 v)
        { Need(Finite(v), "Nonfinite source deformation data."); writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }

        /// <summary>Baseline-only full triangle evidence; never assumes a cylinder or substitutes a grip socket.</summary>
        private sealed class ShaftSurface
        {
            private struct Node { public Vector3 Min, Max; public int Start, Count, Left, Right; }
            private readonly Vector3[] _vertices;
            private readonly int[] _triangles, _order;
            private readonly List<Node> _nodes = new List<Node>();
            public int Queries { get; private set; }
            private static readonly Vector3 RayA = new Vector3(.713139f, .331927f, .617223f);
            private static readonly Vector3 RayB = new Vector3(-.271829f, .843137f, .466919f);
            public ShaftSurface(Vector3[] vertices, int[] triangles)
            {
                Need(vertices != null && vertices.Length >= 4 && vertices.All(Finite) && triangles != null && triangles.Length >= 12 && triangles.Length % 3 == 0,
                    "Actual shaft triangle geometry is incomplete.");
                _vertices = vertices; _triangles = triangles; _order = Enumerable.Range(0, triangles.Length / 3).ToArray();
                var unique = new Dictionary<Vector3, int>(); var representatives = new int[vertices.Length];
                for (int i = 0; i < vertices.Length; i++) { if (!unique.TryGetValue(vertices[i], out int index)) { index = unique.Count; unique.Add(vertices[i], index); } representatives[i] = index; }
                var edges = new Dictionary<(int, int), int>();
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    Need(a >= 0 && a < vertices.Length && b >= 0 && b < vertices.Length && c >= 0 && c < vertices.Length, "Invalid actual shaft triangle index.");
                    Need(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).sqrMagnitude > 1e-24f, "Degenerate shaft triangle prevents a finite signed surface.");
                    foreach (var pair in new[] { (representatives[a], representatives[b]), (representatives[b], representatives[c]), (representatives[c], representatives[a]) })
                    { var key = pair.Item1 < pair.Item2 ? pair : (pair.Item2, pair.Item1); edges.TryGetValue(key, out int count); edges[key] = count + 1; }
                }
                Need(edges.Values.All(n => n == 2), "Actual shaft is not closed after exact-position seam welding; contact sign cannot be assumed.");
                Build(0, _order.Length);
            }
            private int Build(int start, int count)
            {
                int index = _nodes.Count; var node = new Node { Start = start, Count = count, Left = -1, Right = -1,
                    Min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity), Max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity) };
                for (int i = start; i < start + count; i++) for (int j = 0; j < 3; j++)
                { Vector3 v = _vertices[_triangles[_order[i] * 3 + j]]; node.Min = Vector3.Min(node.Min, v); node.Max = Vector3.Max(node.Max, v); }
                _nodes.Add(node);
                if (count > 8)
                {
                    Vector3 span = node.Max - node.Min; int axis = span.x >= span.y && span.x >= span.z ? 0 : span.y >= span.z ? 1 : 2;
                    Array.Sort(_order, start, count, Comparer<int>.Create((a, b) => Center(a, axis).CompareTo(Center(b, axis))));
                    node.Left = Build(start, count / 2); node.Right = Build(start + count / 2, count - count / 2); _nodes[index] = node;
                }
                return index;
            }
            private float Center(int t, int axis) => (_vertices[_triangles[t * 3]][axis] + _vertices[_triangles[t * 3 + 1]][axis] + _vertices[_triangles[t * 3 + 2]][axis]) / 3f;
            public float Unsigned(Vector3 point)
            { Need(Finite(point), "Nonfinite actual contact query."); float squared = float.PositiveInfinity; Nearest(0, point, ref squared); return (float)Math.Sqrt(squared); }
            public float Signed(Vector3 point)
            {
                Need(++Queries <= 4000000, "Contact baseline refinement exhausted its explicit query budget; no contact proof produced.");
                float distance = Unsigned(point); if (distance <= ContactNumericalGuard * .25f) return -distance;
                int a = 0, b = 0; bool edgeA = false, edgeB = false; CountRay(0, point, RayA, ref a, ref edgeA); CountRay(0, point, RayB, ref b, ref edgeB);
                // An ambiguous edge hit/disagreement can only reduce the lower bound, never fabricate outside clearance.
                return edgeA || edgeB || (a & 1) != (b & 1) || (a & 1) != 0 ? -distance : distance;
            }
            public float TriangleSignedLowerBound(Vector3 a, Vector3 b, Vector3 c) => TriangleBound(a, b, c, 0);
            private float TriangleBound(Vector3 a, Vector3 b, Vector3 c, int depth)
            {
                Vector3 center = (a + b + c) / 3f;
                float radius = Mathf.Max(Vector3.Distance(center, a), Mathf.Max(Vector3.Distance(center, b), Vector3.Distance(center, c)));
                float signed = Signed(center), lower = signed - radius - ContactNumericalGuard;
                // An already nonpassing centroid needs no extra refinement to make a failure stronger.
                // This still returns the valid whole-triangle lower bound, never a centroid-only bound.
                if (lower >= -.000025f || radius <= .00001f || signed - ContactNumericalGuard < -.0005f) return lower;
                Need(depth < 16, "Full contact triangle bound needs more refinement; unresolved evidence cannot pass.");
                Vector3 ab = (a + b) * .5f, bc = (b + c) * .5f, ca = (c + a) * .5f;
                return Mathf.Min(Mathf.Min(TriangleBound(a, ab, ca, depth + 1), TriangleBound(ab, b, bc, depth + 1)),
                    Mathf.Min(TriangleBound(ca, bc, c, depth + 1), TriangleBound(ab, bc, ca, depth + 1)));
            }
            private static float BoxDistance(Vector3 p, Node n)
            {
                float x = Mathf.Max(n.Min.x - p.x, Mathf.Max(0f, p.x - n.Max.x));
                float y = Mathf.Max(n.Min.y - p.y, Mathf.Max(0f, p.y - n.Max.y));
                float z = Mathf.Max(n.Min.z - p.z, Mathf.Max(0f, p.z - n.Max.z)); return x * x + y * y + z * z;
            }
            private void Nearest(int index, Vector3 point, ref float best)
            {
                Node n = _nodes[index]; if (BoxDistance(point, n) > best) return;
                if (n.Left >= 0)
                {
                    int first = BoxDistance(point, _nodes[n.Left]) <= BoxDistance(point, _nodes[n.Right]) ? n.Left : n.Right;
                    Nearest(first, point, ref best); Nearest(first == n.Left ? n.Right : n.Left, point, ref best); return;
                }
                for (int i = n.Start; i < n.Start + n.Count; i++)
                {
                    int t = _order[i] * 3; Vector3 q = ClosestPoint(point, _vertices[_triangles[t]], _vertices[_triangles[t + 1]], _vertices[_triangles[t + 2]]);
                    best = Mathf.Min(best, (point - q).sqrMagnitude);
                }
            }
            private void CountRay(int index, Vector3 origin, Vector3 direction, ref int hits, ref bool ambiguous)
            {
                Node n = _nodes[index]; double minimum = 0, maximum = double.PositiveInfinity;
                for (int axis = 0; axis < 3; axis++)
                {
                    double a = (n.Min[axis] - (double)origin[axis] - 1e-9) / direction[axis], b = (n.Max[axis] - (double)origin[axis] + 1e-9) / direction[axis];
                    if (a > b) { double swap = a; a = b; b = swap; } minimum = Math.Max(minimum, a); maximum = Math.Min(maximum, b);
                }
                if (maximum < minimum) return;
                if (n.Left >= 0) { CountRay(n.Left, origin, direction, ref hits, ref ambiguous); CountRay(n.Right, origin, direction, ref hits, ref ambiguous); return; }
                for (int i = n.Start; i < n.Start + n.Count; i++)
                {
                    int t = _order[i] * 3; Vector3 a = _vertices[_triangles[t]], b = _vertices[_triangles[t + 1]], c = _vertices[_triangles[t + 2]];
                    double e1x = b.x - (double)a.x, e1y = b.y - (double)a.y, e1z = b.z - (double)a.z;
                    double e2x = c.x - (double)a.x, e2y = c.y - (double)a.y, e2z = c.z - (double)a.z;
                    double px = direction.y * e2z - direction.z * e2y, py = direction.z * e2x - direction.x * e2z, pz = direction.x * e2y - direction.y * e2x;
                    double det = e1x * px + e1y * py + e1z * pz; if (Math.Abs(det) < 1e-20) continue;
                    double sx = origin.x - (double)a.x, sy = origin.y - (double)a.y, sz = origin.z - (double)a.z;
                    double u = (sx * px + sy * py + sz * pz) / det; if (u < -1e-10 || u > 1 + 1e-10) continue;
                    double qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
                    double v = (direction.x * qx + direction.y * qy + direction.z * qz) / det; if (v < -1e-10 || u + v > 1 + 1e-10) continue;
                    double distance = (e2x * qx + e2y * qy + e2z * qz) / det; if (distance <= 1e-10) continue;
                    if (u <= 1e-9 || v <= 1e-9 || 1 - u - v <= 1e-9) ambiguous = true; hits++;
                }
            }
            private static Vector3 ClosestPoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 ab = b - a, ac = c - a, ap = p - a; float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
                if (d1 <= 0f && d2 <= 0f) return a;
                Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp); if (d3 >= 0f && d4 <= d3) return b;
                float vc = d1 * d4 - d3 * d2; if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
                Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp); if (d6 >= 0f && d5 <= d6) return c;
                float vb = d5 * d2 - d1 * d6; if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
                float va = d3 * d6 - d5 * d4; if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
                float inverse = 1f / (va + vb + vc); return a + ab * (vb * inverse) + ac * (vc * inverse);
            }
        }
        /// <summary>Pure rest-space landmark selection; ties keep the first source index, never a socket surrogate.</summary>
        public static int SelectDistalRestVertex(Vector3[] vertices, Vector3 origin, Vector3 axis, out float maximumProjection)
        {
            maximumProjection = float.NegativeInfinity;
            if (vertices == null || vertices.Length == 0 || !Finite(origin) || !Finite(axis) || axis.sqrMagnitude < .000001f) return -1;
            axis.Normalize(); int best = -1;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!Finite(vertices[i])) return -1;
                float projection = Vector3.Dot(vertices[i] - origin, axis);
                if (projection > maximumProjection) { maximumProjection = projection; best = i; }
            }
            return best;
        }
        private static bool Insert(ref PlayerLoopSystem node, Type anchor, PlayerLoopSystem stage)
        {
            if (node.subSystemList == null) return false; var children = (PlayerLoopSystem[])node.subSystemList.Clone();
            for (int i = 0; i < children.Length; i++) { if (children[i].type == anchor) { var list = new List<PlayerLoopSystem>(children); list.Insert(i + 1, stage); node.subSystemList = list.ToArray(); return true; } if (Insert(ref children[i], anchor, stage)) { node.subSystemList = children; return true; } } return false;
        }
        private static bool Remove(ref PlayerLoopSystem node)
        {
            if (node.subSystemList == null) return false; bool changed = false; var children = new List<PlayerLoopSystem>();
            foreach (var child in node.subSystemList) { if (child.type == typeof(PoseStage) || child.type == typeof(CaptureStage)) { changed = true; continue; } var copy = child; changed |= Remove(ref copy); children.Add(copy); } if (changed) node.subSystemList = children.ToArray(); return changed;
        }
        private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == name || t.name.EndsWith(":" + name, StringComparison.Ordinal));
        private static string ProjectFile(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        private static string Sha(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Finite(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w);
        private static void Need(bool value, string reason) { if (!value) throw new Pending(reason); }
    }
}
