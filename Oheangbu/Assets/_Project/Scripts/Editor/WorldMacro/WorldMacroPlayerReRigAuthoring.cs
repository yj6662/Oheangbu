using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Explicit commands only. Import/inspect never install the candidate or start animation.</summary>
    public static partial class WorldMacroPlayerReRigAuthoring
    {
        public const string Folder = "Assets/_Project/Art/Characters/PlaytestReRig";
        public const string ModelPath = Folder + "/Models/Player_C02_ReRig.fbx";
        public const string PrefabPath = Folder + "/PF_Player_C02_ReRig.prefab";
        public const string ProfilePath = Folder + "/PlayerAppearance_C02_ReRig.asset";
        public const string GestureProfilePath = Folder + "/PlayerGesture_C02_ReRig.asset";
        public const string RootName = "Playtest_PlayerAppearance_C02_ReRig";
        public static string SourceDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/PlayerPhase1/PlaytestReRig/Exports"));
        public static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/PlayerPhase1/PlaytestReRig/Unity"));
        public static string SourceFbx => Path.Combine(SourceDirectory, "Player_C02_ReRig.fbx");

        [Serializable] public sealed class BoneRow
        {
            public string name, parent, path, human;
            public Vector3 localPosition, localScale, modelPosition;
            public Quaternion localRotation, modelRotation;
        }
        [Serializable] public sealed class MeshRow
        {
            public string name; public int vertices, triangles, maxWeights, unweighted;
            public int nonFiniteWeights, negativeWeights; public float maxWeightSumError;
            public string[] materials;
        }
        [Serializable] public sealed class Inspection
        {
            public string status, unityVersion, sourceFbxSha256, model, scene;
            public bool editorPlaying, editorCompiling, avatarValid, avatarHuman;
            public int fingerBonesMapped, triangles; public Bounds neutralBounds;
            public float targetScale; public BoneRow[] transforms; public MeshRow[] meshes;
            public string[] limitations = { "Rest/import inspection is not a grip or visual acceptance. No production motion or player installation is performed." };
        }

        public static string Execute(string argument)
        {
            string command = (argument ?? "inspect").Trim().ToLowerInvariant();
            switch (command)
            {
                case "status": return JsonUtility.ToJson(new Inspection { status = "EDITOR_AVAILABLE", unityVersion = Application.unityVersion,
                    editorPlaying = EditorApplication.isPlaying, editorCompiling = EditorApplication.isCompiling,
                    scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path, model = ModelPath }, true);
                case "import": return ImportCandidate();
                case "inspect": return InspectCandidate();
                default: return ExecuteInstallation(command);
            }
        }

        private static string ImportCandidate()
        {
            Need(!EditorApplication.isPlaying, "Import requires Edit mode.");
            Need(File.Exists(SourceFbx), "Missing candidate export: " + SourceFbx);
            EnsureFolder(Folder + "/Models");
            File.Copy(SourceFbx, ModelPath, true);
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.isReadable = true;
            importer.importAnimation = false;
            importer.importCameras = false; importer.importLights = false;
            importer.optimizeGameObjects = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.importBlendShapes = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.skinWeights = ModelImporterSkinWeights.Custom;
            importer.maxBonesPerVertex = 5;
            importer.minBoneWeight = .001f;
            importer.SaveAndReimport();

            var reference = (ModelImporter)AssetImporter.GetAtPath(WorldMacroPlayerAppearanceAuthoring.SourceModelPath);
            Need(reference != null, "Existing C02 Humanoid mapping is missing.");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var human = reference.humanDescription;
            var mappings = human.human.ToList();
            foreach (string side in new[] { "Left", "Right" })
                foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
                    for (int segment = 1; segment <= 3; segment++)
                    {
                        string native = side + "Hand" + finger + segment;
                        string mapped = side + " " + (finger == "Pinky" ? "Little" : finger) + " "
                            + new[] { "Proximal", "Intermediate", "Distal" }[segment - 1];
                        mappings.RemoveAll(bone => bone.humanName == mapped);
                        if (source.GetComponentsInChildren<Transform>(true).Any(t => t.name == native))
                            mappings.Add(new HumanBone { boneName = native, humanName = mapped, limit = new HumanLimit { useDefaultValues = true } });
                    }
            human.human = mappings.ToArray();
            human.skeleton = source.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            importer.humanDescription = human;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
            return InspectCandidate();
        }

        public static string InspectCandidate()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Need(source != null, "Import the rerig candidate first.");
            GameObject instance = Object.Instantiate(source);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                var animator = instance.GetComponent<Animator>();
                if (animator != null) { animator.runtimeAnimatorController = null; animator.enabled = false; }
                var rows = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(ReadMesh).ToArray();
                Bounds bounds = NeutralBounds(instance);
                var human = ((ModelImporter)AssetImporter.GetAtPath(ModelPath)).humanDescription.human;
                var transforms = instance.GetComponentsInChildren<Transform>(true).Select(t => new BoneRow
                {
                    name = t.name, parent = t.parent != null ? t.parent.name : null,
                    path = AnimationUtility.CalculateTransformPath(t, instance.transform),
                    localPosition = t.localPosition, localRotation = t.localRotation, localScale = t.localScale,
                    modelPosition = instance.transform.InverseTransformPoint(t.position),
                    modelRotation = Quaternion.Inverse(instance.transform.rotation) * t.rotation,
                    human = human.FirstOrDefault(b => b.boneName == t.name).humanName
                }).ToArray();
                int fingerCount = human.Count(b => b.humanName.Contains("Proximal") || b.humanName.Contains("Intermediate") || b.humanName.Contains("Distal"));
                bool good = animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
                var report = new Inspection
                {
                    status = good && fingerCount == 30 ? "PASS_REST_IMPORT_ONLY" : "FINDINGS", unityVersion = Application.unityVersion,
                    sourceFbxSha256 = Sha(ModelPath), model = ModelPath, scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
                    editorPlaying = EditorApplication.isPlaying, editorCompiling = EditorApplication.isCompiling,
                    avatarValid = animator != null && animator.avatar != null && animator.avatar.isValid,
                    avatarHuman = animator != null && animator.avatar != null && animator.avatar.isHuman,
                    fingerBonesMapped = fingerCount, triangles = rows.Sum(r => r.triangles), neutralBounds = bounds,
                    targetScale = 1.75f / Mathf.Max(.001f, bounds.size.y), transforms = transforms, meshes = rows
                };
                string json = JsonUtility.ToJson(report, true);
                Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output, "import_inspection.json"), json);
                return json;
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static MeshRow ReadMesh(SkinnedMeshRenderer skin)
        {
            Mesh mesh = skin.sharedMesh;
            var row = new MeshRow { name = skin.name, vertices = mesh.vertexCount,
                triangles = (int)Enumerable.Range(0, mesh.subMeshCount).Sum(i => (long)mesh.GetIndexCount(i)) / 3,
                materials = skin.sharedMaterials.Select(m => m == null ? "MISSING" : AssetDatabase.GetAssetPath(m)).ToArray() };
            using (var counts = mesh.GetBonesPerVertex())
            using (var weights = mesh.GetAllBoneWeights())
            {
                int offset = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    row.maxWeights = Mathf.Max(row.maxWeights, counts[i]); if (counts[i] == 0) row.unweighted++;
                    float sum = 0f;
                    for (int j = 0; j < counts[i]; j++)
                    {
                        float weight = weights[offset++].weight;
                        if (float.IsNaN(weight) || float.IsInfinity(weight)) row.nonFiniteWeights++;
                        if (weight < 0) row.negativeWeights++; sum += weight;
                    }
                    row.maxWeightSumError = Mathf.Max(row.maxWeightSumError, Mathf.Abs(1f - sum));
                }
            }
            return row;
        }

        internal static Bounds NeutralBounds(GameObject root)
        {
            bool first = true; Bounds bounds = default;
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Matrix4x4 matrix = root.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                foreach (Vector3 p in skin.sharedMesh.vertices)
                { Vector3 point = matrix.MultiplyPoint3x4(p); if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point); }
            }
            Need(!first, "Candidate has no readable skin geometry."); return bounds;
        }
        internal static string Sha(string path)
        { using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        internal static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        internal static void EnsureFolder(string path)
        { if (AssetDatabase.IsValidFolder(path)) return; string parent = path.Substring(0, path.LastIndexOf('/')); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1)); }
    }
}
