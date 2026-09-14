using System;
using System.Collections.Generic;
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
    /// <summary>Preview-only normal isolation. No source mesh, importer, material or scene writes.</summary>
    public static class UnityEditorValidationV2HandShadingAudit
    {
        private static Session _active;
        private static JObject _last;
        public static string Begin()
        {
            if (_active != null) return Status();
            if (Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
                return "WAIT: stable Edit Mode required";
            _active = new Session();
            try { _active.Start(); } catch (Exception e) { _active.Finish(e); }
            return Status();
        }
        public static string Status() => (_active?.Report ?? _last ?? new JObject { ["status"] = "NOT_STARTED" }).ToString(Formatting.None);
        public static string Stop() { _active?.Finish(new OperationCanceledException("Stopped")); return Status(); }

        private sealed class Session
        {
            public readonly JObject Report = new JObject { ["status"] = "INITIALIZING", ["rigGate"] = "NOT_GRANTED",
                ["restored"] = false, ["measurements"] = new JArray(), ["renders"] = new JArray(),
                ["panelOrder"] = new JArray("top-left: original SMR + PBR", "top-right: same SMR, bump disabled",
                    "bottom-left: same baked positions, Unity RecalculateNormals, bump disabled",
                    "bottom-right: same baked positions, 1um positional groups / area-weighted normals, bump disabled"),
                ["scope"] = "Hand shading isolation only; no geometry welding, animation, source changes or rig approval. The 1um groups change normals only." };
            private Scene _scene;
            private GameObject _root, _bakeObject;
            private SkinnedMeshRenderer _hand;
            private MeshRenderer _bakeRenderer;
            private MeshFilter _bakeFilter;
            private Camera _camera;
            private RenderTexture _target;
            private Material _pbr, _flat;
            private readonly List<Mesh> _meshes = new List<Mesh>();
            private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
            private readonly Dictionary<string, Quaternion> _restRotation = new Dictionary<string, Quaternion>();
            private Dictionary<string, Matrix4x4> _rest;
            private JObject[] _poses;
            private PlayerHandGripCorrectives _correctives;
            private string _directory, _source, _sourceHash, _definitionPath, _definitionHash;
            private int _index;
            private bool _oldAsync, _savedAsync, _finished;
            private const int Width = 768, Height = 768;

            public void Start()
            {
                string repository = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
                _directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2/hand-shading-" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")));
                Directory.CreateDirectory(_directory); Report["directory"] = _directory;
                _definitionPath = Path.Combine(repository, "Art/PlayerV2/Validation/static-pose-definitions.json");
                var definitions = JObject.Parse(File.ReadAllText(_definitionPath));
                _definitionHash = Hash(_definitionPath);
                var all = ((JArray)definitions["poses"]).Cast<JObject>().ToArray();
                _poses = new[] { "rest", "grip_down", "grip_up" }.Select(id => all.Single(p => (string)p["id"] == id)).ToArray();
                _rest = ReadMatrices(_poses[0]);
                _source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", DosaV2PlayerBuilder.ArmsModel));
                _sourceHash = Hash(_source); Report["sourceAsset"] = DosaV2PlayerBuilder.ArmsModel; Report["sourceSha256"] = _sourceHash;
                Report["definitionsSha256"] = _definitionHash;
                var importer = (ModelImporter)AssetImporter.GetAtPath(DosaV2PlayerBuilder.ArmsModel);
                Report["importNormals"] = importer.importNormals.ToString(); Report["importBlendShapeNormals"] = importer.importBlendShapeNormals.ToString();
                Report["normalSmoothingAngle"] = importer.normalSmoothingAngle;
                _oldAsync = ShaderUtil.allowAsyncCompilation; _savedAsync = true; ShaderUtil.allowAsyncCompilation = false;
                _scene = EditorSceneManager.NewPreviewScene();
                _root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.ArmsModel), _scene);
                _root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var animator in _root.GetComponentsInChildren<Animator>()) animator.enabled = false;
                foreach (var t in _root.GetComponentsInChildren<Transform>(true))
                {
                    string name = Normalize(t.name);
                    if (_rest.ContainsKey(name)) { _bones.Add(name, t); _restRotation.Add(name, t.rotation); }
                }
                if (_bones.Count != _rest.Count) throw new InvalidDataException("Pose/FBX bone inventory differs");
                float restError = _bones.Max(p => Vector3.Distance(p.Value.position, UnityEditorValidationV2StaticPoseAudit.BlenderPoint(_rest[p.Key].GetColumn(3))));
                if (restError > .0001f) throw new InvalidDataException("FBX/rest basis mismatch: " + restError);
                Report["restHeadErrorMeters"] = restError;
                foreach (var renderer in _root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                _hand = _root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "DosaV2_Hands");
                _hand.enabled = true; _hand.updateWhenOffscreen = true; _hand.forceMatrixRecalculationPerRender = true; _hand.quality = SkinQuality.Bone4;
                var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Characters/DosaV2/Materials/M_DosaV2_HandsSkin.mat");
                if (material == null) throw new InvalidDataException("Missing actual skin material");
                _pbr = new Material(material); _flat = new Material(material);
                _flat.SetTexture("_BumpMap", null); _flat.SetFloat("_BumpScale", 0f); _flat.DisableKeyword("_NORMALMAP");
                _hand.sharedMaterial = _pbr;
                _correctives = new PlayerHandGripCorrectives(new[] { _hand });
                _bakeObject = new GameObject("HandNormalsOnly"); _bakeObject.transform.SetParent(_hand.transform, false);
                _bakeFilter = _bakeObject.AddComponent<MeshFilter>(); _bakeRenderer = _bakeObject.AddComponent<MeshRenderer>(); _bakeRenderer.sharedMaterial = _flat;
                _bakeRenderer.enabled = false;
                Report["vertexCount"] = _hand.sharedMesh.vertexCount;
                Report["triangles"] = _hand.sharedMesh.triangles.Length / 3;
                Report["blendShapes"] = new JArray(Enumerable.Range(0, _hand.sharedMesh.blendShapeCount).Select(i => _hand.sharedMesh.GetBlendShapeName(i)));
                using (var counts = _hand.sharedMesh.GetBonesPerVertex()) using (var weights = _hand.sharedMesh.GetAllBoneWeights())
                    Report["weights"] = UnityEditorValidationV2StaticPoseAudit.AuditWeightStream(counts.ToArray(), weights.ToArray(), _hand.bones.Length);
                var cameraObject = new GameObject("HandShadingPreviewCamera"); SceneManager.MoveGameObjectToScene(cameraObject, _scene);
                _camera = cameraObject.AddComponent<Camera>(); _camera.enabled = false; _camera.cameraType = CameraType.Preview;
                _camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(_scene);
                if (_camera.overrideSceneCullingMask == 0) throw new InvalidDataException("Missing preview culling mask");
                Report["previewSceneCullingMask"] = _camera.overrideSceneCullingMask.ToString();
                _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = new Color(.19f, .21f, .235f);
                _camera.nearClipPlane = .001f; _camera.farClipPlane = 10f; _camera.orthographic = true;
                _target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 }; _target.Create();
                Light("Key", new Vector3(30, -35, 0), 1.3f); Light("Fill", new Vector3(20, 140, 0), .7f);
                AssemblyReloadEvents.beforeAssemblyReload += OnReload; EditorApplication.playModeStateChanged += OnPlay;
                Report["status"] = "RUNNING"; EditorApplication.update += Tick; Save();
            }

            private void Tick()
            {
                try
                {
                    if (Application.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Editor state changed");
                    var pose = _poses[_index / 2]; string side = _index % 2 == 0 ? "Right" : "Left"; string id = (string)pose["id"];
                    var matrices = ReadMatrices(pose);
                    foreach (var pair in _bones.OrderBy(p => Depth(p.Value)))
                    {
                        var delta = UnityEditorValidationV2StaticPoseAudit.BlenderRotationDelta(_rest[pair.Key], matrices[pair.Key]);
                        pair.Value.SetPositionAndRotation(UnityEditorValidationV2StaticPoseAudit.BlenderPoint(matrices[pair.Key].GetColumn(3)), delta.rotation * _restRotation[pair.Key]);
                    }
                    _correctives.Apply(id == "rest" ? 0 : 1, id == "rest" ? 0 : 1);
                    var baked = new Mesh(); _hand.BakeMesh(baked, false); _meshes.Add(baked);
                    var recalc = Object.Instantiate(baked); _meshes.Add(recalc); recalc.RecalculateNormals();
                    var welded = Object.Instantiate(baked); _meshes.Add(welded); int groups = SmoothPositionNormals(welded);
                    var vertices = baked.vertices;
                    var row = new JObject { ["pose"] = id, ["side"] = side,
                        ["shapeWeights"] = new JArray(Enumerable.Range(0, _hand.sharedMesh.blendShapeCount).Select(i => new JObject { ["name"] = _hand.sharedMesh.GetBlendShapeName(i), ["weight"] = _hand.GetBlendShapeWeight(i) })),
                        ["maxRecalculatedPositionDelta"] = MaxDifference(vertices, recalc.vertices), ["maxWeldSmoothPositionDelta"] = MaxDifference(vertices, welded.vertices),
                        ["normalPositionGroups"] = groups, ["normalDeviationDegrees"] = NormalComparison(baked.normals, welded.normals) };
                    ((JArray)Report["measurements"]).Add(row);
                    foreach (string view in new[] { "whole_hand_front", "index_joint_5cm" })
                    {
                        Vector3 center;
                        if (view == "index_joint_5cm") { center = _bones[side + "HandIndex2"].position; _camera.orthographicSize = .025f; }
                        else
                        {
                            Bounds bounds = new Bounds(_bones[side + "Hand"].position, Vector3.zero);
                            foreach (var pair in _bones.Where(p => p.Key.StartsWith(side + "Hand", StringComparison.Ordinal))) bounds.Encapsulate(pair.Value.position);
                            center = bounds.center; _camera.orthographicSize = Mathf.Max(.09f, bounds.extents.magnitude * 1.1f);
                        }
                        _camera.transform.position = center + Vector3.forward * 1.1f; _camera.transform.LookAt(center, Vector3.up);
                        Capture(id + "_" + side + "_" + view, recalc, welded);
                    }
                    foreach (var mesh in _meshes) Object.DestroyImmediate(mesh); _meshes.Clear(); _bakeFilter.sharedMesh = null;
                    _index++; Report["completedCases"] = _index; Save();
                    if (_index == 6) Finish(null);
                }
                catch (Exception e) { Finish(e); }
            }
            private void Capture(string name, Mesh recalc, Mesh welded)
            {
                var sheet = new Texture2D(Width * 2, Height * 2, TextureFormat.RGB24, false);
                var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false); var previous = RenderTexture.active;
                try
                {
                    for (int panel = 0; panel < 4; panel++)
                    {
                        _hand.enabled = panel < 2; _bakeRenderer.enabled = panel >= 2;
                        _hand.sharedMaterial = panel == 0 ? _pbr : _flat;
                        _bakeFilter.sharedMesh = panel == 2 ? recalc : welded;
                        RenderPipeline.SubmitRenderRequest(_camera, new RenderPipeline.StandardRequest { destination = _target });
                        RenderTexture.active = _target; pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply();
                        sheet.SetPixels((panel % 2) * Width, panel < 2 ? Height : 0, Width, Height, pixels.GetPixels());
                    }
                    sheet.Apply(); string path = Path.Combine(_directory, name + ".png"); File.WriteAllBytes(path, sheet.EncodeToPNG());
                    ((JArray)Report["renders"]).Add(new JObject { ["path"] = path, ["sha256"] = Hash(path), ["panelOrder"] = Report["panelOrder"].DeepClone() });
                }
                finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); Object.DestroyImmediate(sheet); _hand.enabled = true; _bakeRenderer.enabled = false; }
            }
            private static int SmoothPositionNormals(Mesh mesh)
            {
                var vertices = mesh.vertices; var triangles = mesh.triangles; var normals = new Vector3[vertices.Length];
                var groups = new Dictionary<(long, long, long), List<int>>();
                for (int i = 0; i < vertices.Length; i++)
                {
                    var p = vertices[i]; var key = ((long)Math.Round(p.x * 1e6), (long)Math.Round(p.y * 1e6), (long)Math.Round(p.z * 1e6));
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<int>(); list.Add(i);
                }
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2]; Vector3 n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    normals[a] += n; normals[b] += n; normals[c] += n;
                }
                foreach (var group in groups.Values) { Vector3 n = Vector3.zero; foreach (int i in group) n += normals[i]; n.Normalize(); foreach (int i in group) normals[i] = n; }
                mesh.normals = normals; return groups.Count;
            }
            private static JObject NormalComparison(Vector3[] original, Vector3[] corrected)
            {
                double sum = 0; float maximum = 0; int above30 = 0, above90 = 0;
                for (int i = 0; i < original.Length; i++) { float angle = Vector3.Angle(original[i], corrected[i]); sum += angle; maximum = Mathf.Max(maximum, angle); if (angle > 30) above30++; if (angle > 90) above90++; }
                return new JObject { ["mean"] = sum / original.Length, ["max"] = maximum, ["above30"] = above30, ["above90"] = above90 };
            }
            private void Light(string name, Vector3 angles, float intensity)
            {
                var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, _scene); obj.transform.rotation = Quaternion.Euler(angles);
                var light = obj.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = intensity;
            }
            private void OnReload() => Finish(new OperationCanceledException("Assembly reload"));
            private void OnPlay(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingEditMode) Finish(new OperationCanceledException("Play requested")); }
            public void Finish(Exception error)
            {
                if (_finished) return; _finished = true; EditorApplication.update -= Tick;
                AssemblyReloadEvents.beforeAssemblyReload -= OnReload; EditorApplication.playModeStateChanged -= OnPlay;
                try
                {
                    foreach (var mesh in _meshes) if (mesh != null) Object.DestroyImmediate(mesh);
                    if (_pbr != null) Object.DestroyImmediate(_pbr); if (_flat != null) Object.DestroyImmediate(_flat);
                    if (_target != null) { _target.Release(); Object.DestroyImmediate(_target); }
                    if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
                    Report["restored"] = !_scene.IsValid() || !_scene.isLoaded;
                    Report["sourceFilesStable"] = File.Exists(_source) && Hash(_source) == _sourceHash && Hash(_definitionPath) == _definitionHash;
                }
                catch (Exception cleanup) { error = error ?? cleanup; }
                finally { if (_savedAsync) ShaderUtil.allowAsyncCompilation = _oldAsync; }
                Report["status"] = error == null ? "COMPLETE_DIAGNOSTIC_NOT_RIG_PASS" : "FAIL"; Report["error"] = error?.GetBaseException().Message;
                _last = Report; _active = null; Save();
            }
            private void Save() { if (_directory != null) File.WriteAllText(Path.Combine(_directory, "report.json"), Report.ToString(Formatting.Indented)); }
        }
        private static Dictionary<string, Matrix4x4> ReadMatrices(JObject pose)
        {
            var result = new Dictionary<string, Matrix4x4>();
            foreach (var p in ((JObject)pose["boneMatricesRigLocal"]).Properties()) { var m = new Matrix4x4(); for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) m[r, c] = (float)p.Value[r][c]; result.Add(p.Name, m); }
            return result;
        }
        private static int Depth(Transform bone) { int n = 0; while (bone.parent != null) { n++; bone = bone.parent; } return n; }
        private static string Normalize(string name) { int i = Math.Max(name.LastIndexOf(':'), name.LastIndexOf('|')); return i < 0 ? name : name.Substring(i + 1); }
        private static float MaxDifference(Vector3[] a, Vector3[] b) { float max = 0; for (int i = 0; i < a.Length; i++) max = Mathf.Max(max, Vector3.Distance(a[i], b[i])); return max; }
        private static string Hash(string path) { using (var hash = SHA256.Create()) using (var file = File.OpenRead(path)) return string.Concat(hash.ComputeHash(file).Select(b => b.ToString("x2"))); }
    }
}
