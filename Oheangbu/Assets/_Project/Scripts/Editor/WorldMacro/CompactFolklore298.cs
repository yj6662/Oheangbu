using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Candidate-only authoring. Import is allowed before approval for inspection;
    // replacing an actor requires the complete, approved rig and motion contract.
    public static partial class CompactFolklore298
    {
        public const string AssetRoot = "Assets/_Project/Art/Characters/Folklore298";
        public const string ScenePath = AssetRoot + "/W_Demo_Compact_Folklore298.unity";
        public const string SourceScene = "Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity";
        public static string OutputRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Characters/Folklore298"));
        public static string RepositoryRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        [Serializable] public sealed class Manifest { public ModelRow[] rows = Array.Empty<ModelRow>(); }
        [Serializable] public sealed class ModelRow
        {
            public string id, displayName, model, rigType = "Generic", rootBone, sourceEvidence;
            public bool approved;
            public float targetHeight, targetLength, yaw;
            public string lengthAxis = "z";
            public ClipRow[] clips = Array.Empty<ClipRow>();
            public TextureRow[] textures = Array.Empty<TextureRow>();
            public string placeId, realm, templateId = "mine_beast/0";
            public Vector3 preferredFeet;
            public float searchRadius = 50, speed = 2.2f, detection = 16, leash = 28, walkMetresPerSecond = 1.5f;
            public float damage, range, telegraph, recovery, arcDegrees, cooldownMin, cooldownMax, preferredDistance, capsuleRadius, capsuleHeight;
        }
        [Serializable] public sealed class ClipRow { public string role, path, name; public float peak01 = .5f; public bool loop; }
        [Serializable] public sealed class TextureRow { public int slot; public string materialName, baseColor, normal, roughness, metallic; }
        [Serializable] public sealed class ImportReceipt
        {
            public string id, model, prefab, rigType, status;
            public int renderers, skinnedRenderers, bones, vertices, triangles, materials;
            public Bounds bounds, physicalBounds;
            public string[] clips, errors;
        }
        [Serializable] sealed class ReceiptList { public string timestamp; public ImportReceipt[] rows; }
        public static Manifest ReadManifest()
        {
            string path = Path.Combine(OutputRoot, "import-manifest.json");
            if (!File.Exists(path)) throw new FileNotFoundException("Rig/clip manifest is required before import", path);
            var value = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            if (value?.rows == null || value.rows.Length == 0) throw new Exception("Empty Folklore298 manifest");
            if (value.rows.Any(r => r == null || string.IsNullOrWhiteSpace(r.id) || r.id.Any(c => !char.IsLetterOrDigit(c) && c != '_')) || value.rows.GroupBy(r => r.id).Any(g => g.Count() != 1))
                throw new Exception("Unique simple model IDs required");
            return value;
        }
        public static string PrefabPath(string id) => AssetRoot + "/Prefabs/PF_" + id + ".prefab";
        public static string Run(string argument)
        {
            RequireEdit();
            if (argument == "import" || argument.StartsWith("import:")) return Import(argument.Contains(":") ? argument.Substring(7) : null);
            if (argument == "validate") return Validate();
            if (argument == "build") return BuildCandidate();
            if (argument == "status") return "scene=" + SceneManager.GetActiveScene().path + " dirty=" + SceneManager.GetActiveScene().isDirty;
            throw new ArgumentException("Expected import[:id], validate, build or status: " + argument);
        }
        static void RequireEdit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Folklore298 requires Edit mode");
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        public static string SourceAbsolute(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new Exception("Empty source path");
            string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(RepositoryRoot, path));
            string root = Path.GetFullPath(OutputRoot) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new Exception("Source must be inside Folklore298: " + path);
            return full;
        }
        static string CopyInput(string source, string id, string category)
        {
            string full = SourceAbsolute(source);
            if (!File.Exists(full)) throw new FileNotFoundException("Generation/rig output not ready", full);
            string folder = AssetRoot + "/" + category + "/" + id; Folder(folder);
            // Include source parent so walking and independent action exports cannot collide.
            string stem = new DirectoryInfo(Path.GetDirectoryName(full)).Name + "_" + Path.GetFileName(full);
            string destination = folder + "/" + stem;
            if (!File.Exists(destination) || !File.ReadAllBytes(full).SequenceEqual(File.ReadAllBytes(destination))) File.Copy(full, destination, true);
            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
            return destination;
        }
        static string ImportedInput(string source, string id, string category)
        {
            string full = SourceAbsolute(source);
            return AssetRoot + "/" + category + "/" + id + "/" + new DirectoryInfo(Path.GetDirectoryName(full)).Name + "_" + Path.GetFileName(full);
        }
        static void ModelSettings(string path, ModelRow row, bool animationOnly, Avatar avatar = null)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter ?? throw new Exception("Not an FBX model: " + path);
            importer.isReadable = true; importer.importAnimation = true;
            importer.importCameras = false; importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = row.rigType == "Humanoid" ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
            importer.avatarSetup = animationOnly && avatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            if (animationOnly && avatar != null) importer.sourceAvatar = avatar;
            if (!string.IsNullOrWhiteSpace(row.rootBone) && row.rigType != "Humanoid") importer.motionNodeName = row.rootBone;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            if (row.rigType == "Humanoid" && !(animationOnly && avatar != null)) ConfigureHuman(importer, path);
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                var role = row.clips.FirstOrDefault(c => c.name == clip.name || clip.name.EndsWith("|" + c.name, StringComparison.Ordinal));
                clip.loopTime = role != null && (role.loop || role.role == "idle" || role.role == "walk");
                clip.lockRootHeightY = true; clip.lockRootPositionXZ = true; clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
                clip.events = Array.Empty<AnimationEvent>();
            }
            importer.clipAnimations = clips; importer.SaveAndReimport();
        }
        static void ConfigureHuman(ModelImporter importer, string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) return;
            var transforms = model.GetComponentsInChildren<Transform>(true);
            var map = new Dictionary<string, string> {
                {"Hips","Hips"},{"Spine","Spine02"},{"Chest","Spine01"},{"UpperChest","Spine"},{"Neck","neck"},{"Head","Head"},
                {"LeftShoulder","LeftShoulder"},{"LeftUpperArm","LeftArm"},{"LeftLowerArm","LeftForeArm"},{"LeftHand","LeftHand"},
                {"RightShoulder","RightShoulder"},{"RightUpperArm","RightArm"},{"RightLowerArm","RightForeArm"},{"RightHand","RightHand"},
                {"LeftUpperLeg","LeftUpLeg"},{"LeftLowerLeg","LeftLeg"},{"LeftFoot","LeftFoot"},{"LeftToes","LeftToeBase"},
                {"RightUpperLeg","RightUpLeg"},{"RightLowerLeg","RightLeg"},{"RightFoot","RightFoot"},{"RightToes","RightToeBase"}};
            if (!map.Values.All(name => transforms.Count(t => t.name == name) == 1)) throw new Exception("Meshy24 Humanoid bone map incomplete: " + path);
            var description = importer.humanDescription;
            description.human = map.Select(pair => new HumanBone { humanName = pair.Key, boneName = pair.Value, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
            description.skeleton = transforms.Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            description.upperArmTwist = .5f; description.lowerArmTwist = .5f; description.upperLegTwist = .5f; description.lowerLegTwist = .5f;
            description.armStretch = .05f; description.legStretch = .05f; description.feetSpacing = 0; description.hasTranslationDoF = false;
            importer.humanDescription = description;
        }
        static Texture2D ImportTexture(string source, string id, bool normal)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;
            string path = CopyInput(source, id, "Textures");
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal; importer.maxTextureSize = 4096;
            importer.mipmapEnabled = true; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Material ImportMaterial(ModelRow row, TextureRow texture)
        {
            string folder = AssetRoot + "/Materials/" + row.id; Folder(folder);
            string path = folder + "/M_" + row.id + "_" + texture.slot + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? throw new Exception("URP Lit unavailable");
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.SetTexture("_BaseMap", ImportTexture(texture.baseColor, row.id, false));
            material.SetColor("_BaseColor", Color.white); material.SetFloat("_Smoothness", .15f); material.SetFloat("_Metallic", 0);
            var normal = ImportTexture(texture.normal, row.id, true); material.SetTexture("_BumpMap", normal);
            if (normal != null) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");
            if (!string.IsNullOrWhiteSpace(texture.roughness) || !string.IsNullOrWhiteSpace(texture.metallic))
            {
                var packed = PackMetallicSmoothness(row.id, texture);
                material.SetTexture("_MetallicGlossMap", packed); material.EnableKeyword("_METALLICSPECGLOSSMAP"); material.SetFloat("_Smoothness", 1);
            }
            material.SetFloat("_BumpScale", .7f); EditorUtility.SetDirty(material); return material;
        }
        static Texture2D PackMetallicSmoothness(string id, TextureRow source)
        {
            Texture2D Read(string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                var result = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (!result.LoadImage(File.ReadAllBytes(SourceAbsolute(path)), false)) { Object.DestroyImmediate(result); throw new Exception("Unreadable PBR texture " + path); }
                return result;
            }
            Texture2D roughness = null, metallic = null, packed = null;
            try
            {
                roughness = Read(source.roughness); metallic = Read(source.metallic);
                int width = Math.Max(roughness != null ? roughness.width : 0, metallic != null ? metallic.width : 0), height = Math.Max(roughness != null ? roughness.height : 0, metallic != null ? metallic.height : 0);
                if ((roughness != null && (roughness.width != width || roughness.height != height)) || (metallic != null && (metallic.width != width || metallic.height != height))) throw new Exception("PBR channels have different dimensions; do not resample silently");
                var r = roughness != null ? roughness.GetPixels32() : null; var m = metallic != null ? metallic.GetPixels32() : null; var pixels = new Color32[width * height];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(m != null ? m[i].r : (byte)0, 0, 0, r != null ? (byte)(255 - r[i].r) : (byte)38);
                packed = new Texture2D(width, height, TextureFormat.RGBA32, false, true); packed.SetPixels32(pixels); packed.Apply(false, false);
                string folder = AssetRoot + "/Textures/" + id; Folder(folder); string destination = folder + "/T_" + source.slot + "_MetallicSmoothness.png";
                File.WriteAllBytes(destination, packed.EncodeToPNG()); AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(destination); importer.sRGBTexture = false; importer.maxTextureSize = 4096; importer.mipmapEnabled = true; importer.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(destination);
            }
            finally { if (roughness != null) Object.DestroyImmediate(roughness); if (metallic != null) Object.DestroyImmediate(metallic); if (packed != null) Object.DestroyImmediate(packed); }
        }
        public static AnimationClip Clip(ModelRow row, string role, bool required = true)
        {
            var spec = row.clips.FirstOrDefault(c => c.role == role);
            if (spec == null) { if (required) throw new Exception(row.id + " missing clip role " + role); return null; }
            string source = string.IsNullOrEmpty(spec.path) ? row.model : spec.path;
            string path = ImportedInput(source, row.id, source == row.model ? "Models" : "Animations");
            var all = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__", StringComparison.Ordinal)).ToArray();
            var matches = all.Where(c => c.name == spec.name || c.name.EndsWith("|" + spec.name, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1) { if (required) throw new Exception(row.id + "/" + role + " exact clip not unique: " + spec.name + " available=" + string.Join(",", all.Select(c => c.name))); return null; }
            if (matches[0].legacy || matches[0].length <= 0) throw new Exception(row.id + " invalid clip " + role);
            return matches[0];
        }
        public static string Import(string onlyId = null)
        {
            RequireEdit(); var manifest = ReadManifest(); var receipts = new List<ImportReceipt>();
            foreach (var row in manifest.rows.Where(r => onlyId == null || r.id == onlyId))
            {
                string path = CopyInput(row.model, row.id, "Models"); ModelSettings(path, row, false);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new Exception("No model at " + path);
                var avatar = model.GetComponent<Animator>()?.avatar;
                foreach (string motion in row.clips.Select(c => c.path).Where(s => !string.IsNullOrWhiteSpace(s) && s != row.model).Distinct())
                    ModelSettings(CopyInput(motion, row.id, "Animations"), row, true, avatar);
                var materials = row.textures.OrderBy(t => t.slot).Select(t => ImportMaterial(row, t)).ToArray();
                if (materials.Length == 0) throw new Exception(row.id + " has no explicit source texture mapping");
                var host = new GameObject("PF_" + row.id); host.SetActive(false);
                try
                {
                    var visual = Object.Instantiate(model, host.transform); visual.name = "Visual";
                    visual.transform.localRotation = Quaternion.Euler(0, row.yaw, 0);
                    foreach (var c in visual.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                    foreach (var b in visual.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(b);
                    var renderers = visual.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0) throw new Exception(row.id + " contains no renderers");
                    foreach (var renderer in renderers)
                    {
                        var original = renderer.sharedMaterials; var mapped = new Material[original.Length];
                        for (int i = 0; i < mapped.Length; i++)
                        {
                            int slot = i;
                            var named = row.textures.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.materialName) && original[i] != null && t.materialName == original[i].name);
                            if (named != null) slot = named.slot;
                            if (slot < 0 || slot >= materials.Length) throw new Exception(row.id + " unresolved material slot " + slot);
                            mapped[i] = materials[slot];
                        }
                        renderer.sharedMaterials = mapped;
                    }
                    var poseAnimator = visual.GetComponentInChildren<Animator>(true);
                    if (poseAnimator == null) throw new Exception("No animator before physical normalization");
                    SamplePoseGraph298(poseAnimator, Clip(row, "idle"), 0);
                    Bounds initial = PhysicalBounds(host);
                    float target = row.targetLength > 0 ? row.targetLength : row.targetHeight;
                    float extent = row.targetLength > 0 ? (row.lengthAxis == "x" ? initial.size.x : initial.size.z) : initial.size.y;
                    if (target > 0 && extent > .001f) visual.transform.localScale *= target / extent;
                    Bounds scaled = PhysicalBounds(host); visual.transform.position -= Vector3.up * scaled.min.y;
                    var actual = PhysicalBounds(host);
                    float measured = row.targetLength > 0 ? (row.lengthAxis == "x" ? actual.size.x : actual.size.z) : actual.size.y;
                    if (Mathf.Abs(actual.min.y) > .002f || target > 0 && Mathf.Abs(measured - target) > .003f) throw new Exception(row.id + " physical normalization mismatch: " + actual);
                    foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { skin.updateWhenOffscreen = false; var bounds = skin.localBounds; bounds.Expand(Mathf.Max(.5f, bounds.size.magnitude * .2f)); skin.localBounds = bounds; }
                    foreach (var animator in host.GetComponentsInChildren<Animator>(true)) { animator.applyRootMotion = false; animator.runtimeAnimatorController = null; animator.fireEvents = false; }
                    host.SetActive(true); Folder(AssetRoot + "/Prefabs");
                    PrefabUtility.SaveAsPrefabAsset(host, PrefabPath(row.id));
                    receipts.Add(Inspect(row, host, path, false));
                }
                finally { Object.DestroyImmediate(host); }
            }
            if (receipts.Count == 0) throw new Exception("No manifest row matches " + onlyId);
            AssetDatabase.SaveAssets(); Directory.CreateDirectory(Path.Combine(OutputRoot, "Import"));
            File.WriteAllText(Path.Combine(OutputRoot, "Import", onlyId == null ? "import.json" : "import-" + onlyId + ".json"), JsonUtility.ToJson(new ReceiptList { timestamp = DateTime.UtcNow.ToString("O"), rows = receipts.ToArray() }, true));
            return "Imported " + receipts.Count + " candidate presentation prefabs; no scene actor replaced. " + Path.Combine(OutputRoot, "Import");
        }
        public static Bounds VisualBounds(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(true); if (renderers.Length == 0) throw new Exception("Empty visual");
            var bounds = renderers[0].bounds; foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds); return bounds;
        }
        // A renderer bound is conservative culling data, not body dimensions. Work
        // directly in world space: each bind vertex is transformed by its weighted
        // bone world matrix and bindpose. This applies ancestor scale exactly once,
        // without depending on BakeMesh(useScale)'s local-space convention.
        public static Bounds PhysicalBounds(GameObject model)
        {
            bool found = false; var result = new Bounds();
            void Include(Vector3 point) { if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) throw new Exception("Nonfinite physical vertex"); if (!found) { result = new Bounds(point, Vector3.zero); found = true; } else result.Encapsulate(point); }
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                foreach (var point in PhysicalSkinVertices298(skin)) Include(point);
            }
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true)) if (filter.sharedMesh != null) foreach (var v in filter.sharedMesh.vertices) Include(filter.transform.TransformPoint(v));
            if (!found) throw new Exception("No physical surface"); return result;
        }
        public static void SamplePoseGraph298(Animator animator, AnimationClip clip, float time)
        {
            animator.applyRootMotion = false; animator.runtimeAnimatorController = null; animator.fireEvents = false;
            var graph = PlayableGraph.Create("Folklore298 physical normalization"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            try
            {
                var node = AnimationClipPlayable.Create(graph, clip); node.SetSpeed(0);
                AnimationPlayableOutput.Create(graph, "Authored pose", animator).SetSourcePlayable(node);
                graph.Play(); node.SetTime(time); graph.Evaluate(0);
            }
            finally { graph.Destroy(); }
        }
        public static Vector3[] PhysicalSkinVertices298(SkinnedMeshRenderer skin)
        {
            var mesh = skin.sharedMesh ?? throw new Exception("Missing skin mesh");
            for (int i = 0; i < mesh.blendShapeCount; i++) if (Mathf.Abs(skin.GetBlendShapeWeight(i)) > .0001f) throw new Exception("Physical298 helper requires zero source blendshape weights");
            var vertices = mesh.vertices; var weights = mesh.boneWeights; var bind = mesh.bindposes;
            if (weights.Length != vertices.Length || bind.Length != skin.bones.Length || skin.bones.Any(b => b == null)) throw new Exception("Incomplete physical skin " + skin.name);
            var matrices = skin.bones.Select((bone, i) => bone.localToWorldMatrix * bind[i]).ToArray();
            var result = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var w = weights[i]; if (Mathf.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1) > .002f) throw new Exception("Physical skin weights are not normalized/max4 contract failed");
                Vector3 point = Vector3.zero;
                void Add(int index, float weight) { if (weight > 0) { if (index < 0 || index >= matrices.Length) throw new Exception("Physical skin bone index"); point += matrices[index].MultiplyPoint3x4(vertices[i]) * weight; } }
                Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3); result[i] = point;
            }
            return result;
        }
        // Caller owns and destroys this CPU snapshot. Mesh lives in the renderer's
        // local coordinates and retains source UV/submeshes/material ordering.
        public static Mesh BakePhysicalMesh298(SkinnedMeshRenderer skin)
        {
            var source = skin.sharedMesh; var result = Object.Instantiate(source); result.name = source.name + "_PhysicalSnapshot298";
            result.vertices = PhysicalSkinVertices298(skin).Select(skin.transform.InverseTransformPoint).ToArray();
            var weights = source.boneWeights; var normals = source.normals; var tangents = source.tangents;
            var matrices = skin.bones.Select((bone, i) => bone.localToWorldMatrix * source.bindposes[i]).ToArray();
            var normalMatrices = matrices.Select(m => m.inverse.transpose).ToArray();
            Vector3 Weighted(int i, Vector3 v, Matrix4x4[] ms)
            {
                var w = weights[i]; Vector3 sum = Vector3.zero;
                void Add(int bone, float weight) { if (weight > 0) sum += ms[bone].MultiplyVector(v) * weight; }
                Add(w.boneIndex0,w.weight0); Add(w.boneIndex1,w.weight1); Add(w.boneIndex2,w.weight2); Add(w.boneIndex3,w.weight3); return sum;
            }
            if (normals.Length == weights.Length) result.normals = normals.Select((n,i) => skin.transform.localToWorldMatrix.transpose.MultiplyVector(Weighted(i,n,normalMatrices)).normalized).ToArray();
            else result.RecalculateNormals();
            if (tangents.Length == weights.Length) result.tangents = tangents.Select((t,i) => { var p = skin.transform.worldToLocalMatrix.MultiplyVector(Weighted(i,new Vector3(t.x,t.y,t.z),matrices)).normalized; return new Vector4(p.x,p.y,p.z,t.w); }).ToArray();
            result.RecalculateBounds(); result.UploadMeshData(false); return result;
        }
        static ImportReceipt Inspect(ModelRow row, GameObject model, string path, bool requireReady)
        {
            var errors = new List<string>(); var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var meshes = skins.Select(s => s.sharedMesh).Concat(model.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh)).Where(m => m != null).ToArray();
            if (meshes.Length == 0) errors.Add("No mesh");
            if (skins.Length == 0) errors.Add("No skinned renderer");
            foreach (var s in skins) if (s.bones.Length == 0 || s.bones.Any(b => b == null) || s.sharedMesh == null || s.sharedMesh.bindposes.Length != s.bones.Length) errors.Add("Invalid skin " + s.name);
            var animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isValid) errors.Add("Invalid Animator/avatar");
            if (row.rigType == "Humanoid" && (animator == null || !animator.isHuman)) errors.Add("Expected Humanoid");
            string[] roles = row.id == "cheongryong" ? new[] { "idle", "attack", "tail", "hit", "stun", "death" } : new[] { "idle", "walk", "attack", "hit", "stun", "death" };
            foreach (string role in roles) { try { Clip(row, role); } catch (Exception e) { errors.Add(e.Message); } }
            if (row.id == "south_gate_general") foreach (string role in new[] { "sweep", "wave" }) { try { Clip(row, role); } catch (Exception e) { errors.Add(e.Message); } }
            if (row.id == "cheongryong" || row.id == "imugi")
            {
                var bones = model.GetComponentsInChildren<Transform>(true);
                var previous = bones.SingleOrDefault(b => b.name == "Head");
                if (previous == null) errors.Add("Missing Head");
                for (int i = 1; i <= 24; i++) { var next = bones.SingleOrDefault(b => b.name == "Body_" + i.ToString("00")); if (next == null || next.parent != previous) errors.Add("Body chain " + i); previous = next; }
                foreach (var name in new[] { "MouthOrigin", "TailTip" }) if (bones.Count(b => b.name == name) != 1) errors.Add("Missing/duplicate " + name);
            }
            if (requireReady && !row.approved) errors.Add("Rig/visual quality approval not recorded in manifest");
            foreach (var mesh in meshes) if (mesh.vertices.Any(v => !float.IsFinite(v.x) || !float.IsFinite(v.y) || !float.IsFinite(v.z))) errors.Add("Nonfinite mesh " + mesh.name);
            var receipt = new ImportReceipt { id = row.id, model = path, prefab = PrefabPath(row.id), rigType = row.rigType, status = errors.Count == 0 ? "PASS" : "NOT_READY", errors = errors.ToArray(), renderers = model.GetComponentsInChildren<Renderer>(true).Length, skinnedRenderers = skins.Length, bones = skins.SelectMany(s => s.bones).Where(b => b != null).Distinct().Count(), vertices = meshes.Sum(m => m.vertexCount), triangles = meshes.Sum(m => m.triangles.Length / 3), materials = model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct().Count(), bounds = VisualBounds(model), physicalBounds = PhysicalBounds(model), clips = row.clips.Select(c => c.role + ":" + c.name).ToArray() };
            return receipt;
        }
        public static string Validate()
        {
            RequireEdit(); var receipts = new List<ImportReceipt>();
            foreach (var row in ReadManifest().rows)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(row.id)) ?? throw new Exception("Import first: " + row.id);
                receipts.Add(Inspect(row, prefab, ImportedInput(row.model, row.id, "Models"), true));
            }
            Directory.CreateDirectory(Path.Combine(OutputRoot, "Import"));
            string path = Path.Combine(OutputRoot, "Import/validation.json");
            File.WriteAllText(path, JsonUtility.ToJson(new ReceiptList { timestamp = DateTime.UtcNow.ToString("O"), rows = receipts.ToArray() }, true));
            int failed = receipts.Count(r => r.status != "PASS");
            return "Validated " + receipts.Count + " families, ready=" + (receipts.Count - failed) + " notReady=" + failed + "; " + path;
        }
    }
}

