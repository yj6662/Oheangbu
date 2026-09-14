using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Fresh static import evidence only. Never creates clips, Cloth, controllers or RIG_PASS.</summary>
    public static class UnityEditorValidationV2StaticPoseAudit
    {
        private static Session _active;
        private static JObject _last;
        private const string Definitions = "Art/PlayerV2/Validation/static-pose-definitions.json";
        private const string TextureManifest = "Art/PlayerV2/Staging/texture-manifest.json";
        private static readonly string[] PoseIds = { "rest", "open_hand", "grip_down", "grip_up", "shoulder_45", "shoulder_90", "shoulder_120",
            "elbow_45", "elbow_90", "elbow_120", "forearm_minus90", "forearm_plus90", "wrist_flex", "wrist_extend", "torso_twist",
            "hip_flex", "knee_flex", "ankle_flex", "combined_reach" };
        public static bool IsRunning => _active != null;
        public static string Begin()
        {
            if (_active != null) return Status();
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return "WAIT: stable Edit Mode is required; this audit never enters Play or loads a production scene.";
            var session = new Session(); _active = session;
            try { session.Start(); }
            catch (Exception e) { session.Finish("WAIT", e.GetBaseException().Message); }
            return Status();
        }
        public static string Status()
        {
            var report = _active != null ? _active.Report : _last;
            if (report == null) return "NOT_STARTED";
            return new JObject { ["status"] = report["status"], ["directory"] = report["directory"], ["activePose"] = report["activePose"],
                ["completedPoses"] = ((JArray)report["deformationStatic"]).Count, ["restored"] = report["restored"], ["error"] = report["error"] }.ToString(Formatting.None);
        }
        public static string Stop() { _active?.Finish("STOPPED", "Stopped by caller; incomplete evidence is not certified."); return Status(); }

        private sealed class BoneState
        {
            public Transform Bone;
            public Vector3 LocalPosition, LocalScale, RigPosition;
            public Quaternion LocalRotation, RigRotation;
            public float ParentDistance;
            public void Restore() { Bone.localPosition = LocalPosition; Bone.localRotation = LocalRotation; Bone.localScale = LocalScale; }
        }
        private sealed class Model
        {
            public string Variant, Path;
            public GameObject Root;
            public Animator Animator;
            public Renderer[] Renderers;
            public PlayerHandGripCorrectives HandCorrectives;
            public readonly Dictionary<string, BoneState> Bones = new Dictionary<string, BoneState>(StringComparer.Ordinal);
            public readonly Dictionary<Renderer, Mesh> Meshes = new Dictionary<Renderer, Mesh>();
            public int Vertices, Triangles;
        }
        private sealed class Session
        {
            public readonly JObject Report = new JObject { ["status"] = "INITIALIZING", ["error"] = null, ["activePose"] = null, ["restored"] = false,
                ["mode"] = "DIRECT_BONE_POSES", ["rigGate"] = "NOT_GRANTED", ["productionAnimationCreated"] = false,
                ["structuralHumanoid"] = new JArray(), ["weights"] = new JArray(), ["deformationStatic"] = new JArray(),
                ["inventory"] = new JArray(), ["sources"] = new JObject(), ["renders"] = new JArray(), ["findings"] = new JArray(),
                ["scope"] = "Every source vertex and defined/imported skin bone on fresh world/near model instances; rigid MeshRenderer props included. No Cloth, production Animator controller, animation evaluation, existing scene edits, or gate promotion.",
                ["unmeasured"] = new JArray("invertedJointFaces", "unresolvedSelfIntersections", "unintendedBridgeFaces", "unresolvedBoundaryDefects", "actual skin-to-brush contact", "clothStability"),
                ["matrixContract"] = "C maps Blender rig-local points to Unity (-x,z,-y). Target position=C*posedHead. Target rotation=(C*posedRotation*inverse(restRotation)*inverse(C))*actualImportedRestRotation. All rest heads must first agree within 0.1mm; imported local scale is preserved. Full matrices, not Euler re-interpretation, define each pose.",
                ["measurementLimits"] = new JObject { ["restBasisPositionToleranceMeters"] = .0001, ["posePositionToleranceMeters"] = .0001,
                    ["weightSumTolerance"] = .0001, ["activeInfluenceLimit"] = 4, ["boneLengthDiagnosticToleranceMeters"] = .001,
                    ["rootDiagnosticToleranceMeters"] = .0001, ["degenerateDoubleAreaSquaredThreshold"] = 1e-16,
                    ["meaning"] = "Measurement guardrails fixed before execution; these diagnostics do not replace the separately reviewed acceptance configuration." } };
            private Scene _scene;
            private Camera _camera;
            private RenderTexture _target;
            private Mesh _baked;
            private GameObject _brushRoot;
            private Transform _brushGrip;
            private Quaternion _brushGripLocalRotation;
            private readonly List<Model> _models = new List<Model>();
            private readonly List<(Model model, JObject pose)> _work = new List<(Model, JObject)>();
            private Dictionary<string, Matrix4x4> _rest;
            private JObject _definitions, _materials;
            private string _directory, _repository;
            private int _index;
            private bool _finished, _oldAsyncCompilation, _savedGlobals;
            private readonly Stopwatch _wall = new Stopwatch();

            public void Start()
            {
                _repository = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
                _directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2", "static-poses-" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")));
                Directory.CreateDirectory(_directory); Report["directory"] = _directory;
                Report["startedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"); Report["toolVersion"] = Application.unityVersion;
                Report["originalScene"] = SceneManager.GetActiveScene().path;
                _definitions = JObject.Parse(File.ReadAllText(RepositoryPath(Definitions))); AddSource(Definitions);
                Need((string)_definitions["mode"] == "DIRECT_BONE_POSES" && (int?)_definitions["productionActions"] == 0, "Definitions must be direct diagnostic matrices with zero production Actions.");
                var poses = ((JArray)_definitions["poses"]).Cast<JObject>().ToArray();
                Need(poses.Take(PoseIds.Length).Select(p => (string)p["id"]).SequenceEqual(PoseIds)
                    && poses.Select(p => (string)p["id"]).Distinct().Count() == poses.Length,
                    "The 19 required poses must remain first and every additional diagnostic pose must have a unique ID.");
                _rest = ReadMatrices(poses.Single(p => (string)p["id"] == "rest"));
                Need(_rest.Count > 0 && _rest.ContainsKey("Root"), "Full rest matrices and explicit Root are required.");
                foreach (var pose in poses) Need(ReadMatrices(pose).Keys.OrderBy(v => v).SequenceEqual(_rest.Keys.OrderBy(v => v)), "Every static pose must provide the same complete bone matrix inventory.");
                string source = (string)_definitions["source"];
                string actualSourceHash = File.Exists(RepositoryPath(source)) ? Hash(RepositoryPath(source)) : null;
                Report["definitionSource"] = source; Report["definitionSourceExpectedSha256"] = _definitions["sourceSha256"];
                Report["definitionSourceCurrentSha256"] = actualSourceHash;
                Report["definitionSourceMatches"] = actualSourceHash == (string)_definitions["sourceSha256"];
                if (!(bool)Report["definitionSourceMatches"]) Finding("Pose source hash is stale or absent. Results remain candidate pipeline evidence; regenerate the definitions against the final source before gate submission.");
                else AddSource(source);
                _materials = JObject.Parse(File.ReadAllText(RepositoryPath(TextureManifest))); AddSource(TextureManifest);
                foreach (string projectSetting in new[] { "Oheangbu/ProjectSettings/GraphicsSettings.asset", "Oheangbu/ProjectSettings/QualitySettings.asset",
                    "Oheangbu/Packages/manifest.json", "Oheangbu/Packages/packages-lock.json" })
                    if (File.Exists(RepositoryPath(projectSetting))) AddSource(projectSetting);
                if (GraphicsSettings.currentRenderPipeline != null) AddAsset(AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline));
                _oldAsyncCompilation = ShaderUtil.allowAsyncCompilation; _savedGlobals = true; ShaderUtil.allowAsyncCompilation = false;
                EditorApplication.playModeStateChanged += OnPlay; AssemblyReloadEvents.beforeAssemblyReload += OnReload;
                _scene = EditorSceneManager.NewPreviewScene();
                _models.Add(Prepare("world", DosaV2PlayerBuilder.WorldModel));
                _models.Add(Prepare("near", DosaV2PlayerBuilder.ArmsModel));
                AddAsset(DosaV2PlayerBuilder.BrushModel);
                var brushAsset = AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.BrushModel);
                Need(brushAsset != null, "The actual brush model is required for static grip review.");
                _brushRoot = (GameObject)PrefabUtility.InstantiatePrefab(brushAsset, _scene);
                foreach (var animator in _brushRoot.GetComponentsInChildren<Animator>()) animator.enabled = false;
                _brushGrip = _brushRoot.GetComponentsInChildren<Transform>(true).Single(t => Normalize(t.name) == "GripSocket");
                _brushGripLocalRotation = Quaternion.Inverse(_brushRoot.transform.rotation) * _brushGrip.rotation;
                foreach (var renderer in _brushRoot.GetComponentsInChildren<Renderer>())
                {
                    Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    AssignMaterials(renderer, mesh);
                }
                _brushRoot.SetActive(false);
                foreach (var model in _models) foreach (var pose in poses)
                    if (model.Variant == "world" || !LowerPose((string)pose["id"])) _work.Add((model, pose));
                var cameraObject = new GameObject("StaticPoseEvidenceCamera"); SceneManager.MoveGameObjectToScene(cameraObject, _scene);
                _camera = cameraObject.AddComponent<Camera>(); _camera.enabled = false; _camera.cameraType = CameraType.Preview;
                _camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(_scene);
                Need(_camera.overrideSceneCullingMask != 0, "Preview scene must have an explicit render culling mask.");
                Report["previewSceneCullingMask"] = _camera.overrideSceneCullingMask.ToString();
                _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = new Color(.19f, .21f, .235f, 1f);
                _camera.nearClipPlane = .005f; _camera.farClipPlane = 30f; _camera.fieldOfView = 38f;
                _target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 }; _target.Create();
                _baked = new Mesh { name = "V2StaticPoseMeasuredBake" };
                Light("Key", new Vector3(30f, -35f, 0f), 1.3f); Light("Fill", new Vector3(20f, 140f, 0f), .7f);
                _wall.Start(); Report["status"] = "RUNNING"; EditorApplication.update += Tick; Save();
            }

            private Model Prepare(string variant, string path)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path); Need(asset != null, "Missing imported model: " + path);
                AddAsset(path); int clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Count(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
                Need(clips == 0, "Production/imported animation clips are not permitted in this pre-gate fixture: " + path);
                var model = new Model { Variant = variant, Path = path, Root = (GameObject)PrefabUtility.InstantiatePrefab(asset, _scene) };
                model.Root.name = variant + "_StaticPoseFixture"; model.Root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Need(model.Root.GetComponentsInChildren<Cloth>(true).Length == 0, "Use raw imported models; Cloth must not be present.");
                var animators = model.Root.GetComponentsInChildren<Animator>(true);
                Need(animators.Length == 1 && animators[0].runtimeAnimatorController == null, "Exactly one static Humanoid Animator with no controller is required.");
                model.Animator = animators[0]; model.Animator.enabled = false; model.Animator.applyRootMotion = false;
                Need(model.Animator.avatar != null && model.Animator.avatar.isValid && model.Animator.isHuman, "Imported static Humanoid Avatar is invalid.");
                var transforms = model.Root.GetComponentsInChildren<Transform>(true);
                foreach (string name in _rest.Keys)
                {
                    var matches = transforms.Where(t => Normalize(t.name) == name).ToArray(); Need(matches.Length == 1, "Full matrix bone must map exactly once: " + variant + "/" + name);
                    var bone = matches[0];
                    model.Bones.Add(name, new BoneState { Bone = bone, LocalPosition = bone.localPosition, LocalRotation = bone.localRotation, LocalScale = bone.localScale,
                        RigPosition = model.Root.transform.InverseTransformPoint(bone.position), RigRotation = Quaternion.Inverse(model.Root.transform.rotation) * bone.rotation,
                        ParentDistance = bone.parent != null ? Vector3.Distance(bone.position, bone.parent.position) : 0f });
                }
                float restError = model.Bones.Max(p => Vector3.Distance(p.Value.RigPosition, BlenderPoint(_rest[p.Key].GetColumn(3))));
                Need(Finite(restError) && restError <= .0001f, variant + ": source/import rest heads do not agree; maximum " + restError + "m. Do not guess a new axis/basis.");
                var boneSet = new HashSet<Transform>(model.Bones.Values.Select(b => b.Bone));
                model.Renderers = model.Root.GetComponentsInChildren<Renderer>(true);
                model.HandCorrectives = new PlayerHandGripCorrectives(model.Renderers);
                Need(model.Renderers.Length > 0 && model.Renderers.All(r => r is SkinnedMeshRenderer || r is MeshRenderer), "All renderers must have auditable skinned or rigid mesh geometry.");
                int missingReferences = 0, invalidIndices = 0, nonFinite = 0, degenerate = 0;
                var weightRows = new JArray(); var meshRows = new JArray();
                foreach (var renderer in model.Renderers)
                {
                    Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    Need(mesh != null && mesh.isReadable, renderer.name + ": full readable source geometry is required.");
                    model.Meshes.Add(renderer, mesh); model.Vertices += mesh.vertexCount;
                    int[] triangles = mesh.triangles; model.Triangles += triangles.Length / 3;
                    Vector3[] vertices = mesh.vertices; nonFinite += vertices.Count(v => !Finite(v));
                    invalidIndices += triangles.Count(i => i < 0 || i >= vertices.Length);
                    if (triangles.All(i => i >= 0 && i < vertices.Length)) for (int t = 0; t + 2 < triangles.Length; t += 3)
                        if (Vector3.Cross(vertices[triangles[t + 1]] - vertices[triangles[t]], vertices[triangles[t + 2]] - vertices[triangles[t]]).sqrMagnitude <= 1e-16f) degenerate++;
                    AssignMaterials(renderer, mesh); renderer.enabled = true; renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.gameObject.layer = 0;
                    JObject weights;
                    if (renderer is SkinnedMeshRenderer smr)
                    {
                        smr.updateWhenOffscreen = true; smr.forceMatrixRecalculationPerRender = true; smr.quality = SkinQuality.Bone4;
                        missingReferences += smr.bones.Count(b => b == null); if (smr.rootBone == null) missingReferences++;
                        Need(smr.bones.All(b => b != null && boneSet.Contains(b)), renderer.name + ": imported skin includes an unlisted bone; refresh full pose definitions.");
                        Need(mesh.bindposes.Length == smr.bones.Length, renderer.name + ": bindpose count differs from imported skin bones.");
                        using (var counts = mesh.GetBonesPerVertex()) using (var stream = mesh.GetAllBoneWeights()) weights = AuditWeightStream(counts.ToArray(), stream.ToArray(), smr.bones.Length);
                        Need((int)weights["checkedVertices"] == mesh.vertexCount, renderer.name + ": the full per-vertex influence-count stream is incomplete.");
                        weights["kind"] = "full_skin_stream";
                    }
                    else
                    {
                        Need(renderer.transform.parent != null && boneSet.Contains(renderer.transform.parent), renderer.name + ": rigid prop must retain its authored direct bone parent.");
                        weights = new JObject { ["checkedVertices"] = mesh.vertexCount, ["activeInfluenceLimit"] = 4, ["maxInfluences"] = 1,
                            ["unweightedVertices"] = 0, ["invalidBoneIndices"] = 0, ["negativeWeights"] = 0, ["nonFiniteWeights"] = 0,
                            ["unresolvedTruncatedVertices"] = 0, ["maxWeightSumError"] = 0, ["kind"] = "direct_parent_rigid_transform", ["bone"] = Normalize(renderer.transform.parent.name),
                            ["method"] = "Every rigid vertex uses its single authored parent Transform; no synthetic SMR weights were written." };
                    }
                    weights["renderer"] = renderer.name; weightRows.Add(weights);
                    var materials = new JArray(); foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null || material.shader == null) { missingReferences++; continue; }
                        string materialPath = AssetDatabase.GetAssetPath(material); AddAsset(materialPath);
                        bool valid = material.shader.isSupported && !material.shader.name.Contains("InternalErrorShader");
                        materials.Add(new JObject { ["name"] = material.name, ["path"] = materialPath, ["shader"] = material.shader.name, ["supported"] = valid });
                        if (!valid) Finding(variant + "/" + renderer.name + ": material shader is unsupported or an error shader.");
                    }
                    meshRows.Add(new JObject { ["renderer"] = renderer.name, ["kind"] = renderer.GetType().Name, ["mesh"] = mesh.name,
                        ["meshPath"] = AssetDatabase.GetAssetPath(mesh), ["vertices"] = mesh.vertexCount, ["triangles"] = triangles.Length / 3,
                        ["submeshes"] = mesh.subMeshCount, ["materials"] = materials, ["parent"] = Normalize(renderer.transform.parent?.name ?? "") });
                }
                var mapping = new JArray(); int sideErrors = 0; var missingRequired = new JArray();
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    var id = (HumanBodyBones)i; Transform bone = model.Animator.GetBoneTransform(id);
                    if (bone == null) { if (HumanTrait.RequiredBone(i) || id.ToString().Contains("Thumb") || id.ToString().Contains("Index") || id.ToString().Contains("Middle") || id.ToString().Contains("Ring") || id.ToString().Contains("Little")) missingRequired.Add(id.ToString()); continue; }
                    string name = Normalize(bone.name); mapping.Add(new JObject { ["human"] = id.ToString(), ["bone"] = name });
                    if (id.ToString().StartsWith("Left", StringComparison.Ordinal) && !name.StartsWith("Left", StringComparison.Ordinal) || id.ToString().StartsWith("Right", StringComparison.Ordinal) && !name.StartsWith("Right", StringComparison.Ordinal)) sideErrors++;
                }
                int invalidParentLinks = model.Bones.Values.Count(b => b.Bone.parent == null || !b.Bone.IsChildOf(model.Root.transform));
                var restBounds = BoundsOf(model.Renderers);
                ((JArray)Report["structuralHumanoid"]).Add(new JObject { ["variant"] = variant, ["vertexCount"] = model.Vertices, ["triangleCount"] = model.Triangles,
                    ["boneCount"] = model.Bones.Count, ["mappedHumanoidBones"] = mapping.Count, ["avatarValid"] = model.Animator.avatar.isValid, ["avatarHuman"] = model.Animator.isHuman,
                    ["heightMeters"] = restBounds.size.y, ["metersPerUnit"] = model.Root.transform.lossyScale.x, ["axisConvention"] = "Unity meters/Y-up, conversion (-x,z,-y) checked against every source bone head",
                    ["pivotDescription"] = "Fresh model root at origin; explicit Root bone checked against source rest matrix", ["maxRestBasisPositionErrorMeters"] = restError,
                    ["nonFiniteValues"] = nonFinite, ["invalidIndices"] = invalidIndices, ["missingRequiredBones"] = missingRequired.Count,
                    ["missingRequiredBoneNames"] = missingRequired, ["missingReferences"] = missingReferences, ["invalidParentLinks"] = invalidParentLinks,
                    ["leftRightMappingErrors"] = sideErrors, ["observedDegenerateFaces"] = degenerate, ["unresolvedDegenerateFaces"] = null,
                    ["degenerateReview"] = "PENDING: raw zero-area count is measured; semantic resolution is not inferred.", ["humanoidMapping"] = mapping,
                    ["auxiliaryBoneMap"] = new JArray(model.Bones.Where(p => !mapping.Any(m => (string)m["bone"] == p.Key)).Select(p => new JObject { ["bone"] = p.Key, ["parent"] = Normalize(p.Value.Bone.parent?.name ?? "") })) });
                ((JArray)Report["weights"]).Add(AggregateWeights(variant, weightRows));
                ((JArray)Report["inventory"]).Add(new JObject { ["variant"] = variant, ["asset"] = path, ["renderers"] = meshRows,
                    ["bones"] = new JArray(model.Bones.Select(p => new JObject { ["name"] = p.Key, ["parent"] = Normalize(p.Value.Bone.parent?.name ?? ""),
                        ["restPosition"] = Vec(p.Value.RigPosition), ["restRotation"] = Quat(p.Value.RigRotation), ["localScale"] = Vec(p.Value.LocalScale) })) });
                model.Root.SetActive(false); return model;
            }

            private void Tick()
            {
                if (_finished) return;
                try
                {
                    Need(!Application.isPlaying && !EditorApplication.isCompiling, "Editor state changed during static audit.");
                    Need(_wall.Elapsed.TotalSeconds < 360, "360s wall safety limit reached; unfinished evidence is not certified.");
                    var job = _work[_index]; Report["activePose"] = job.model.Variant + "/" + (string)job.pose["id"];
                    foreach (var model in _models) model.Root.SetActive(model == job.model);
                    foreach (var bone in job.model.Bones.Values) bone.Restore();
                    var pose = ReadMatrices(job.pose); ApplyPose(job.model, pose);
                    var corrective = job.pose["handGripCorrectives"];
                    job.model.HandCorrectives.Apply((float?)corrective?["right"] ?? 0f, (float?)corrective?["left"] ?? 0f);
                    bool gripping = ((string)job.pose["id"]).StartsWith("grip_", StringComparison.Ordinal)
                        || ((string)job.pose["id"]).StartsWith("wrist_", StringComparison.Ordinal)
                        || (string)job.pose["id"] == "combined_reach";
                    _brushRoot.SetActive(gripping);
                    if (gripping)
                    {
                        Transform socket = job.model.Bones["RightBrushGrip"].Bone;
                        _brushRoot.transform.rotation = socket.rotation * Quaternion.Inverse(_brushGripLocalRotation);
                        _brushRoot.transform.position += socket.position - _brushGrip.position;
                    }
                    JObject sample = MeasurePose(job.model, (string)job.pose["id"], pose);
                    ((JArray)Report["deformationStatic"]).Add(sample);
                    CapturePose(job.model, (string)job.pose["id"], sample);
                    _index++; Save();
                    if (_index >= _work.Count) Finish("COMPLETE_MEASURED_CANDIDATE", null);
                }
                catch (Exception e) { Finish("FAIL", e.GetBaseException().Message); }
            }
            private void ApplyPose(Model model, Dictionary<string, Matrix4x4> pose)
            {
                foreach (var pair in model.Bones.OrderBy(p => Depth(p.Value.Bone)))
                {
                    Matrix4x4 delta = BlenderRotationDelta(_rest[pair.Key], pose[pair.Key]);
                    Vector3 position = model.Root.transform.TransformPoint(BlenderPoint(pose[pair.Key].GetColumn(3)));
                    Quaternion rotation = model.Root.transform.rotation * delta.rotation * pair.Value.RigRotation;
                    pair.Value.Bone.SetPositionAndRotation(position, rotation);
                }
            }
            private JObject MeasurePose(Model model, string id, Dictionary<string, Matrix4x4> pose)
            {
                long start = Stopwatch.GetTimestamp(); int checkedVertices = 0, nonFinite = 0, actualDegenerate = 0;
                float maxLengthDelta = 0f, maxPosePositionError = 0f; var meshes = new JArray(); Bounds bounds = default; bool hasBounds = false;
                foreach (var pair in model.Meshes)
                {
                    Vector3[] vertices;
                    if (pair.Key is SkinnedMeshRenderer skin) { _baked.Clear(); skin.BakeMesh(_baked, false); vertices = _baked.vertices; }
                    else vertices = pair.Value.vertices;
                    Need(vertices.Length == pair.Value.vertexCount, pair.Key.name + ": actual baked source vertex count changed.");
                    checkedVertices += vertices.Length;
                    Vector3[] world = new Vector3[vertices.Length]; int meshInvalid = 0;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 point = pair.Key.transform.TransformPoint(vertices[i]); world[i] = point;
                        if (!Finite(point)) { nonFinite++; meshInvalid++; continue; }
                        if (!hasBounds) { bounds = new Bounds(point, Vector3.zero); hasBounds = true; } else bounds.Encapsulate(point);
                    }
                    int[] triangles = pair.Value.triangles; int meshDegenerate = 0;
                    for (int t = 0; t + 2 < triangles.Length; t += 3)
                    {
                        int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                        if (a < 0 || b < 0 || c < 0 || a >= world.Length || b >= world.Length || c >= world.Length) continue;
                        if (Finite(world[a]) && Finite(world[b]) && Finite(world[c]) && Vector3.Cross(world[b] - world[a], world[c] - world[a]).sqrMagnitude <= 1e-16f) meshDegenerate++;
                    }
                    actualDegenerate += meshDegenerate;
                    meshes.Add(new JObject { ["renderer"] = pair.Key.name, ["checkedVertices"] = vertices.Length, ["nonFiniteValues"] = meshInvalid, ["observedDegenerateFaces"] = meshDegenerate });
                }
                foreach (var pair in model.Bones)
                {
                    var bone = pair.Value.Bone;
                    if (!Finite(bone.position) || !Finite(bone.rotation) || !Finite(bone.localScale)) nonFinite++;
                    if (bone.parent != null) maxLengthDelta = Mathf.Max(maxLengthDelta, Mathf.Abs(Vector3.Distance(bone.position, bone.parent.position) - pair.Value.ParentDistance));
                    maxPosePositionError = Mathf.Max(maxPosePositionError, Vector3.Distance(model.Root.transform.InverseTransformPoint(bone.position), BlenderPoint(pose[pair.Key].GetColumn(3))));
                }
                var root = model.Bones["Root"]; float rootDelta = Vector3.Distance(model.Root.transform.InverseTransformPoint(root.Bone.position), root.RigPosition);
                float rootAngle = Quaternion.Angle(Quaternion.Inverse(model.Root.transform.rotation) * root.Bone.rotation, root.RigRotation);
                float rootScaleDelta = Vector3.Distance(root.Bone.localScale, root.LocalScale);
                Need(checkedVertices == model.Vertices, "Full-vertex audit must include every rigid and skinned vertex.");
                bool checkedMetricsPass = nonFinite == 0 && maxLengthDelta <= .001f && rootDelta <= .0001f && maxPosePositionError <= .0001f;
                return new JObject { ["variant"] = model.Variant, ["poseId"] = id, ["checkedVertices"] = checkedVertices, ["checkedBones"] = model.Bones.Count,
                    ["nonFiniteValues"] = nonFinite, ["maxBoneLengthDeltaMeters"] = maxLengthDelta, ["rootDeltaMeters"] = rootDelta,
                    ["rootRotationDeltaDegrees"] = rootAngle, ["rootLocalScaleDelta"] = rootScaleDelta,
                    ["maxPoseHeadPositionErrorMeters"] = maxPosePositionError, ["checkedMetricsWithinDiagnosticLimits"] = checkedMetricsPass,
                    ["invertedJointFaces"] = null, ["unresolvedSelfIntersections"] = null, ["observedDegenerateFaces"] = actualDegenerate,
                    ["unmeasuredStatus"] = "PENDING_INVERSION_AND_SELF_INTERSECTION_REVIEW", ["boundsMin"] = Vec(bounds.min), ["boundsMax"] = Vec(bounds.max),
                    ["actualMeasurementMilliseconds"] = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency,
                    ["meshes"] = meshes, ["renderIds"] = new JArray() };
            }

            private void CapturePose(Model model, string id, JObject sample)
            {
                Bounds full = BoundsOf(model.Renderers); var views = id == "rest" ? new[] { "front", "back", "left", "right", "top", "bottom" } : new[] { "front" };
                foreach (string view in views) Capture(model, id, view, full, sample);
                if (model.Variant == "world" && LowerPose(id))
                {
                    Bounds legs = new Bounds(new Vector3(0f, .52f, 0f), new Vector3(.8f, 1.2f, .8f));
                    Capture(model, id, "leg_side", legs, sample);
                    Capture(model, id, "leg_back_quarter", legs, sample);
                }
                if (model.Variant == "near")
                    foreach (string side in new[] { "Left", "Right" })
                    {
                        var hand = model.Bones[side + "Hand"].Bone; Bounds close = new Bounds(hand.position, new Vector3(.20f, .20f, .20f));
                        foreach (var finger in model.Bones.Where(p => p.Key.StartsWith(side + "Hand", StringComparison.Ordinal))) close.Encapsulate(finger.Value.Bone.position);
                        close.Expand(.07f); Capture(model, id, side.ToLowerInvariant() + "_hand", close, sample);
                    }
            }
            private void Capture(Model model, string pose, string view, Bounds bounds, JObject sample)
            {
                Vector3 direction = view == "leg_side" ? Vector3.right : view == "leg_back_quarter" ? new Vector3(2f, .15f, -3f).normalized
                    : view == "back" ? Vector3.back : view == "left" ? Vector3.left : view == "right" ? Vector3.right : view == "top" ? Vector3.up : view == "bottom" ? Vector3.down : Vector3.forward;
                Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > .9f ? Vector3.forward : Vector3.up;
                float radius = Mathf.Max(.06f, bounds.extents.magnitude);
                float distance = radius / Mathf.Sin(_camera.fieldOfView * Mathf.Deg2Rad * .5f) * 1.1f;
                _camera.transform.position = bounds.center + direction * distance; _camera.transform.LookAt(bounds.center, up);
                var previous = RenderTexture.active; var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                string renderId = model.Variant + "_" + pose + "_" + view;
                try
                {
                    RenderPipeline.SubmitRenderRequest(_camera, new RenderPipeline.StandardRequest { destination = _target });
                    RenderTexture.active = _target; pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply();
                    string path = Path.Combine(_directory, renderId + ".png"); File.WriteAllBytes(path, pixels.EncodeToPNG());
                    ((JArray)Report["renders"]).Add(new JObject { ["id"] = renderId, ["environment"] = "Unity", ["variant"] = model.Variant,
                        ["poseId"] = pose, ["view"] = view, ["path"] = Relative(path), ["sha256"] = Hash(path), ["width"] = 1920, ["height"] = 1080,
                        ["cameraPosition"] = Vec(_camera.transform.position), ["cameraRotation"] = Quat(_camera.transform.rotation), ["fieldOfView"] = _camera.fieldOfView,
                        ["manualReview"] = "PENDING" });
                    ((JArray)sample["renderIds"]).Add(renderId);
                }
                finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
            }
            private void AssignMaterials(Renderer renderer, Mesh mesh)
            {
                var slots = ((JArray)_materials["rendererMaterials"] ?? new JArray()).Cast<JObject>().Where(m => (string)m["renderer"] == renderer.name).ToArray();
                string[] names;
                if (slots.Length == 1) names = ((JArray)slots[0]["materials"]).Values<string>().ToArray();
                else
                {
                    Need(slots.Length == 0, renderer.name + ": duplicate explicit material mappings.");
                    var matches = ((JArray)_materials["materials"]).Cast<JObject>().Where(m => ((JArray)m["rendererNames"] ?? new JArray()).Values<string>().Contains(renderer.name)).ToArray();
                    Need(matches.Length == 1 && mesh.subMeshCount == 1, renderer.name + ": missing explicit original-UV material mapping; no neutral replacement is used.");
                    names = new[] { (string)matches[0]["name"] };
                }
                Need(names.Length == mesh.subMeshCount, renderer.name + ": authored materials differ from submeshes.");
                renderer.sharedMaterials = names.Select(n => AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Characters/DosaV2/Materials/M_" + n + ".mat")
                    ?? throw new InvalidDataException("Missing imported material: " + n)).ToArray();
            }
            private void Light(string name, Vector3 angles, float intensity)
            {
                var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, _scene); obj.transform.rotation = Quaternion.Euler(angles);
                var light = obj.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = intensity; light.shadows = LightShadows.Soft;
            }
            private void AddAsset(string path)
            {
                if (string.IsNullOrEmpty(path)) return;
                foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                {
                    string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", dependency));
                    if (!File.Exists(full)) continue; AddSource(Relative(full));
                    if (File.Exists(full + ".meta")) AddSource(Relative(full + ".meta"));
                }
            }
            private void AddSource(string relative)
            {
                // Many renderer slots share the same 4k textures. Hash each input once
                // at snapshot start, then verify every unique file again on completion.
                var sources = (JObject)Report["sources"];
                if (!sources.ContainsKey(relative)) sources[relative] = Hash(RepositoryPath(relative));
            }
            private void Finding(string value) { ((JArray)Report["findings"]).Add(value); }
            private static bool LowerPose(string id) => id.StartsWith("hip_flex", StringComparison.Ordinal)
                || id.StartsWith("knee_flex", StringComparison.Ordinal) || id.StartsWith("ankle_flex", StringComparison.Ordinal);
            private string Relative(string path) => Path.GetRelativePath(_repository, path).Replace('\\', '/');
            private string RepositoryPath(string relative)
            {
                Need(!string.IsNullOrWhiteSpace(relative) && !Path.IsPathRooted(relative), "Expected a repository-relative source path.");
                string full = Path.GetFullPath(Path.Combine(_repository, relative));
                Need(full.StartsWith(_repository + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Source escapes repository."); return full;
            }
            private void OnPlay(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingEditMode) Finish("STOPPED", "Play was requested during the static audit."); }
            private void OnReload() => Finish("STOPPED", "Assembly reload.");
            public void Finish(string status, string error)
            {
                if (_finished) return; _finished = true; EditorApplication.update -= Tick;
                EditorApplication.playModeStateChanged -= OnPlay; AssemblyReloadEvents.beforeAssemblyReload -= OnReload;
                try
                {
                    if (status == "COMPLETE_MEASURED_CANDIDATE")
                    {
                        var changed = ((JObject)Report["sources"]).Properties().Where(p => !File.Exists(RepositoryPath(p.Name)) || Hash(RepositoryPath(p.Name)) != (string)p.Value).Select(p => p.Name).ToArray();
                        Report["sourceFilesStable"] = changed.Length == 0; Report["changedSourceFiles"] = new JArray(changed);
                        if (changed.Length != 0) { status = "WAIT_SOURCE_CHANGED"; error = "Source/dependency files changed during measurement; do not reuse this snapshot."; }
                    }
                    if (_baked != null) Object.DestroyImmediate(_baked);
                    if (_target != null) { _target.Release(); Object.DestroyImmediate(_target); }
                    if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
                    if (_savedGlobals) ShaderUtil.allowAsyncCompilation = _oldAsyncCompilation;
                    Report["restored"] = !_scene.IsValid() || !_scene.isLoaded;
                }
                catch (Exception e) { status = "FAIL"; error = (error ?? "") + " Cleanup: " + e.GetBaseException().Message; }
                finally { if (_savedGlobals) ShaderUtil.allowAsyncCompilation = _oldAsyncCompilation; }
                Report["status"] = status; Report["error"] = error; Report["actualWallSeconds"] = _wall.Elapsed.TotalSeconds;
                _last = Report; _active = null; Save();
            }
            private void Save()
            {
                if (string.IsNullOrEmpty(_directory)) return; string json = Report.ToString(Formatting.Indented);
                File.WriteAllText(Path.Combine(_directory, "report.json"), json);
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2/static-pose-audit.json")), json);
            }
        }

        public static Vector3 BlenderPoint(Vector3 point) => new Vector3(-point.x, point.z, -point.y);
        public static Matrix4x4 BlenderRotationDelta(Matrix4x4 rest, Matrix4x4 posed)
        {
            Matrix4x4 c = Matrix4x4.zero; c.m00 = -1f; c.m12 = 1f; c.m21 = -1f; c.m33 = 1f;
            // Inputs are explicitly checked orthonormal below. The inverse of a rigid
            // rotation is its transpose, avoiding platform-dependent matrix inversion.
            return MultiplyRotation(MultiplyRotation(MultiplyRotation(c, posed), TransposeRotation(rest)), TransposeRotation(c));
        }
        private static Matrix4x4 MultiplyRotation(Matrix4x4 a, Matrix4x4 b)
        { var result = Matrix4x4.zero; result.m33 = 1f; for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) for (int k = 0; k < 3; k++) result[r, c] += a[r, k] * b[k, c]; return result; }
        private static Matrix4x4 TransposeRotation(Matrix4x4 value)
        { var result = Matrix4x4.zero; result.m33 = 1f; for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) result[r, c] = value[c, r]; return result; }
        public static JObject AuditWeightStream(byte[] counts, BoneWeight1[] weights, int bones)
        {
            int offset = 0, unweighted = 0, invalidBones = 0, negative = 0, nonFinite = 0, truncated = 0, maximum = 0; float maxSumError = 0f;
            foreach (byte count in counts)
            {
                int active = 0; float sum = 0f;
                for (int n = 0; n < count; n++)
                {
                    if (offset >= weights.Length) { truncated++; break; }
                    var weight = weights[offset++];
                    if (!Finite(weight.weight)) { nonFinite++; continue; }
                    if (weight.weight < 0f) negative++; if (weight.boneIndex < 0 || weight.boneIndex >= bones) invalidBones++;
                    if (weight.weight > 0f) active++; sum += weight.weight;
                }
                if (active == 0) unweighted++; if (active > 4) truncated++; maximum = Math.Max(maximum, active); maxSumError = Math.Max(maxSumError, Math.Abs(sum - 1f));
            }
            if (offset != weights.Length) truncated++;
            return new JObject { ["checkedVertices"] = counts.Length, ["activeInfluenceLimit"] = 4, ["maxInfluences"] = maximum, ["unweightedVertices"] = unweighted,
                ["invalidBoneIndices"] = invalidBones, ["negativeWeights"] = negative, ["nonFiniteWeights"] = nonFinite,
                ["unresolvedTruncatedVertices"] = truncated, ["maxWeightSumError"] = maxSumError,
                ["fullStreamEntries"] = weights.Length, ["consumedStreamEntries"] = offset };
        }
        private static JObject AggregateWeights(string variant, JArray rows)
        {
            var result = new JObject { ["variant"] = variant, ["activeInfluenceLimit"] = 4, ["maxInfluences"] = rows.Max(r => (int)r["maxInfluences"]),
                ["maxWeightSumError"] = rows.Max(r => (float)r["maxWeightSumError"]), ["renderers"] = rows };
            foreach (string key in new[] { "checkedVertices", "unweightedVertices", "invalidBoneIndices", "negativeWeights", "nonFiniteWeights", "unresolvedTruncatedVertices" }) result[key] = rows.Sum(r => (int)r[key]);
            return result;
        }
        private static Dictionary<string, Matrix4x4> ReadMatrices(JObject pose)
        {
            var matrices = pose["boneMatricesRigLocal"] as JObject; Need(matrices != null, "Pose lacks full rig-local bone matrices.");
            var result = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
            foreach (var entry in matrices.Properties())
            {
                var rows = entry.Value as JArray; Need(rows != null && rows.Count == 4 && rows.All(r => r is JArray array && array.Count == 4), "Bone matrix must be4x4: " + entry.Name);
                var matrix = Matrix4x4.zero;
                for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) { float v = (float)rows[r][c]; Need(Finite(v), "Nonfinite bone matrix: " + entry.Name); matrix[r, c] = v; }
                Need(Mathf.Abs(matrix.determinant - 1f) <= .001f && Mathf.Abs(matrix.m30) + Mathf.Abs(matrix.m31) + Mathf.Abs(matrix.m32) <= .0001f && Mathf.Abs(matrix.m33 - 1f) <= .0001f,
                    "Static source bone matrix is not a rigid affine pose: " + entry.Name); result.Add(entry.Name, matrix);
                for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++)
                { float dot = 0f; for (int k = 0; k < 3; k++) dot += matrix[k, a] * matrix[k, b]; Need(Math.Abs(dot - (a == b ? 1f : 0f)) <= .001f, "Scaled/sheared source bone matrix cannot use the rigid basis mapping: " + entry.Name); }
            }
            return result;
        }
        private static Bounds BoundsOf(IEnumerable<Renderer> renderers)
        {
            bool any = false; Bounds result = default;
            foreach (var renderer in renderers) { if (!any) { result = renderer.bounds; any = true; } else result.Encapsulate(renderer.bounds); }
            Need(any, "No actual renderer bounds."); return result;
        }
        private static int Depth(Transform bone) { int n = 0; while (bone.parent != null) { n++; bone = bone.parent; } return n; }
        private static string Normalize(string name) { int separator = Math.Max(name.LastIndexOf(':'), name.LastIndexOf('|')); return separator < 0 ? name : name.Substring(separator + 1); }
        private static JObject Vec(Vector3 p) => new JObject { ["x"] = p.x, ["y"] = p.y, ["z"] = p.z };
        private static JObject Quat(Quaternion q) => new JObject { ["x"] = q.x, ["y"] = q.y, ["z"] = q.z, ["w"] = q.w };
        private static string Hash(string path) { using (var sha = SHA256.Create()) using (var file = File.OpenRead(path)) return string.Concat(sha.ComputeHash(file).Select(b => b.ToString("x2"))); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
        private static void Need(bool condition, string reason) { if (!condition) throw new InvalidDataException(reason); }
    }
}
