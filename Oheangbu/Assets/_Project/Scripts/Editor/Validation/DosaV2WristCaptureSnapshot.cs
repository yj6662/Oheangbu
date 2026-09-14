using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Read-only snapshot of a disposable static fixture after skin update.</summary>
    public static class DosaV2WristCaptureSnapshot
    {
        [Serializable] private sealed class Bone
        { public string name; public float[] localToWorld, localMatrix; }
        [Serializable] private sealed class Influence
        { public int[] boneIndices; public float[] weights; }
        [Serializable] private sealed class Submesh
        { public string material; public int[] triangles; }
        [Serializable] private sealed class Shape
        { public string name; public float weight; }
        [Serializable] private sealed class Skin
        {
            public string name, assetPath, assetSha256, rootBone;
            public int sourceVertexCount;
            public float[] localToWorld;
            public Vector3[] sourceLocalVertices, bakedLocalVertices;
            public Bone[] bones;
            public MatrixRow[] bindposes;
            public Influence[] sourceWeights;
            public Submesh[] submeshes;
            public Shape[] shapes;
        }
        [Serializable] private sealed class MatrixRow { public float[] rowMajor; }
        [Serializable] private sealed class Snapshot
        {
            public string label, utc, timing = "CapturePair entry after UpdateAllSkinnedMeshes; no pose, material, visibility or scene mutation.",
                matrixConvention = "All16 arrays are row-major m00,m01,m02,m03,m10...m33. Unity left-handed world, meters. Transform column vector p by M*p. BakeMesh(false) vertices are renderer-local; use renderer.localToWorld for world coordinates.",
                scope = "Actual right wrist, wrapping and internal coverage only. This records evidence; it does not assert visual clearance or RIG_PASS.";
            public float[] nearRootLocalToWorld, cameraLocalToWorld, cameraWorldToCamera, cameraProjection;
            public int cameraPixelWidth, cameraPixelHeight;
            public Bone[] keyBones;
            public Skin[] skins;
        }
        public static string Write(string directory, string label, Animator near, Camera camera, string timingOverride = null)
        {
            if (near == null || camera == null) throw new ArgumentNullException("near/camera");
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            var transforms = near.GetComponentsInChildren<Transform>(true);
            var snapshot = new Snapshot
            {
                label = label, utc = DateTime.UtcNow.ToString("O"),
                nearRootLocalToWorld = Matrix(near.transform.localToWorldMatrix),
                cameraLocalToWorld = Matrix(camera.transform.localToWorldMatrix),
                cameraWorldToCamera = Matrix(camera.worldToCameraMatrix), cameraProjection = Matrix(camera.projectionMatrix),
                cameraPixelWidth = camera.pixelWidth, cameraPixelHeight = camera.pixelHeight,
                keyBones = new[] { "RightForeArm", "RightHand", "RightBrushGrip" }
                    .Select(name => transforms.Single(t => t.name == name)).Select(CaptureBone).ToArray(),
                skins = near.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(s => s.name == "DosaV2_Hands" || s.name == "DosaV2_SleeveInner_R" || s.name == "DosaV2_ArmLining_Right")
                    .Select(CaptureSkin).ToArray()
            };
            if (timingOverride != null) snapshot.timing = timingOverride;
            if (snapshot.skins.Length != 3) throw new InvalidOperationException("Expected Hands, SleeveInner_R and ArmLining_Right exactly once.");
            string path = Path.Combine(directory, label + "_wrist_snapshot.json");
            File.WriteAllText(path, JsonUtility.ToJson(snapshot, true)); return path;
        }
        public static string ExportImportedWorld()
        {
            GameObject instance = null, cameraObject = null;
            try
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.WorldModel);
                if (model == null) throw new InvalidOperationException("Missing imported world source.");
                instance = Object.Instantiate(model); instance.hideFlags = HideFlags.HideAndDontSave;
                var animator = instance.GetComponent<Animator>();
                if (animator == null || animator.runtimeAnimatorController != null) throw new InvalidOperationException("Controller-free imported world source required.");
                cameraObject = new GameObject("WorldImportSourceSnapshotCamera") { hideFlags = HideFlags.HideAndDontSave };
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2"));
                return Write(directory, "imported_world_" + Hash(DosaV2PlayerBuilder.WorldModel).Substring(0, 12), animator, camera,
                    "Fresh hidden imported WORLD source at rest, no controller or animation evaluation. Read-only bind/source evidence; not a trajectory pose. The legacy nearRootLocalToWorld field contains this world-model root. Camera is an unrendered placeholder.");
            }
            finally { if (instance != null) Object.DestroyImmediate(instance); if (cameraObject != null) Object.DestroyImmediate(cameraObject); }
        }
        private static Bone CaptureBone(Transform t)
        { return new Bone { name = t.name, localToWorld = Matrix(t.localToWorldMatrix), localMatrix = Matrix(Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale)) }; }
        private static Skin CaptureSkin(SkinnedMeshRenderer skin)
        {
            var source = skin.sharedMesh; if (source == null) throw new InvalidOperationException("Missing source mesh: " + skin.name);
            var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                skin.BakeMesh(baked, false);
                if (baked.vertexCount != source.vertexCount) throw new InvalidOperationException("BakeMesh index count changed: " + skin.name);
                string path = AssetDatabase.GetAssetPath(source);
                string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
                return new Skin
                {
                    name = skin.name, assetPath = path, assetSha256 = File.Exists(absolutePath) ? Hash(absolutePath) : "",
                    rootBone = skin.rootBone != null ? skin.rootBone.name : "", sourceVertexCount = source.vertexCount,
                    localToWorld = Matrix(skin.transform.localToWorldMatrix), sourceLocalVertices = source.vertices,
                    bakedLocalVertices = baked.vertices, bones = skin.bones.Select(CaptureBone).ToArray(),
                    bindposes = source.bindposes.Select(m => new MatrixRow { rowMajor = Matrix(m) }).ToArray(),
                    sourceWeights = source.boneWeights.Select(w => new Influence
                    {
                        boneIndices = new[] { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 },
                        weights = new[] { w.weight0, w.weight1, w.weight2, w.weight3 }
                    }).ToArray(),
                    submeshes = Enumerable.Range(0, source.subMeshCount).Select(i => new Submesh
                    { material = i < skin.sharedMaterials.Length && skin.sharedMaterials[i] != null ? skin.sharedMaterials[i].name : "", triangles = source.GetTriangles(i) }).ToArray(),
                    shapes = Enumerable.Range(0, source.blendShapeCount).Select(i => new Shape
                    { name = source.GetBlendShapeName(i), weight = skin.GetBlendShapeWeight(i) }).ToArray()
                };
            }
            finally { Object.DestroyImmediate(baked); }
        }
        private static float[] Matrix(Matrix4x4 m)
        { var values = new float[16]; for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) values[row * 4 + col] = m[row, col]; return values; }
        private static string Hash(string path)
        { using (var algorithm = SHA256.Create()) using (var input = File.OpenRead(path)) return BitConverter.ToString(algorithm.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }
    }
}
