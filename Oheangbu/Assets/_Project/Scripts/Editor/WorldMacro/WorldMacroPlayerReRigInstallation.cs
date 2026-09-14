using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data;
using Oheangbu.Drawing;
using Oheangbu.Presentation;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroPlayerReRigAuthoring
    {
        private const string BrushSource = "Assets/_Project/Art/Characters/DosaV2/Models/SM_DosaBrushV2.fbx";
        private const string BrushProfileSource = "Assets/_Project/Data/PlayerV2/BrushDeformation_DosaV2.asset";
        public static readonly string[] OwnedMaterialPaths =
        {
            Folder + "/Materials/M_C02_ReRig_0.mat", Folder + "/Materials/M_C02_ReRig_1.mat",
            Folder + "/Materials/M_Brush_ReRig.mat", Folder + "/Materials/M_BrushBristles_ReRig.mat"
        };
        [Serializable] private sealed class RigGate
        {
            public string status, scope, sourceFbxSha256;
            public bool staticNumericsPass, staticVisualPass, unityImportPass;
        }
        [Serializable] private sealed class InstallReport
        {
            public string status, model, prefab, profile, gestureProfile, scene, sourceFbxSha256, gateScope;
            public int fingerPoses, nearTriangles, bodyTriangles, brushTriangles;
            public bool rootMotionOff, nearHasNoAnimator, existingSourcePreserved;
            public string[] limitations = { "Hand/upper-body acceptance is separate from whole-clothing penetration and lower-body art approval.",
                "Near-arm cap and transitions require current-runtime screenshots. This command does not perform user walking or build a player." };
        }

        private static string ExecuteInstallation(string command)
        {
            switch (command)
            {
                case "prepare": return PrepareAndInstall(false);
                case "install": return PrepareAndInstall(true);
                case "runtime": return RuntimeInspection();
                case "math": return ValidateGestureMath();
                case "skinning": return ValidateSkinningLease();
                case "contact": return ValidateCurrentSkinContact();
                case "gait": return ValidateIsolatedGait();
                case "gaitfeet": return ValidateIsolatedGait(true);
                case "footsetup": return InstallFootPlacementOnly();
                case "runtimecheck": return RuntimeGestureQa(null);
                default:
                    if (command.StartsWith("runtimecapture:", StringComparison.Ordinal)) return RuntimeGestureQa(command.Substring(15));
                    if (command.StartsWith("capture:", StringComparison.Ordinal)) return CaptureCandidate(command.Substring(8));
                    throw new ArgumentException("Expected status, import, inspect, prepare, install, runtime, or capture:front/side/hands/shoulder.");
            }
        }

        private static RigGate RequireGate()
        {
            string path = Path.Combine(SourceDirectory, "rig_gate.json");
            Need(File.Exists(path), "A current hand-rig gate report is required before preparing/installing production movement.");
            var gate = JsonUtility.FromJson<RigGate>(File.ReadAllText(path));
            Need(gate != null && (gate.status == "HAND_RIG_PASS" || gate.status == "RIG_PASS")
                && gate.scope == "hands_and_upper_body" && gate.staticNumericsPass && gate.staticVisualPass && gate.unityImportPass,
                "The current hand/upper-body numerical, rendered static-pose, and Unity import checks must pass first.");
            Need(string.Equals(gate.sourceFbxSha256, Sha(SourceFbx), StringComparison.OrdinalIgnoreCase)
                && string.Equals(gate.sourceFbxSha256, Sha(ModelPath), StringComparison.OrdinalIgnoreCase),
                "Rig gate does not describe this exact exported/imported FBX. Re-run relevant checks.");
            return gate;
        }

        public static bool HasCurrentRigGate(out string detail)
        {
            try
            {
                var gate = RequireGate();
                detail = gate.status + ": " + gate.scope + "; exact source/imported FBX SHA256=" + gate.sourceFbxSha256;
                return true;
            }
            catch (Exception exception) { detail = exception.Message; return false; }
        }

        private static string PrepareAndInstall(bool installScene)
        {
            Need(!EditorApplication.isPlaying, "Preparation/installation requires Edit mode.");
            if (installScene) Need(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == WorldMacroPlaytestAuthoring.ScenePath,
                "Open the existing playtest scene; the rerig command never generates or opens a world scene.");
            RigGate gate = RequireGate();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Need(source != null, "Candidate has not been imported.");
            var inspection = JsonUtility.FromJson<Inspection>(InspectCandidate());
            Need(inspection.avatarValid && inspection.avatarHuman && inspection.fingerBonesMapped == 30,
                "Candidate must expose a valid Humanoid with all 30 finger segments.");
            Need(inspection.triangles <= 60000, "Body, hands and lids exceed the current 60,000 triangle cap.");
            Need(inspection.meshes.All(r => r.unweighted == 0 && r.nonFiniteWeights == 0 && r.negativeWeights == 0 && r.maxWeightSumError <= .0001f),
                "The imported skin contains invalid assignments or sums.");
            foreach (string sub in new[] { "/Materials", "/Near", "/Brush" }) EnsureFolder(Folder + sub);
            var oldProfile = AssetDatabase.LoadAssetAtPath<WorldMacroPlayerAppearanceProfile>(WorldMacroPlayerAppearanceAuthoring.ProfilePath);
            Need(oldProfile != null && oldProfile.Controller != null, "Existing verified gait profile is missing.");
            var profile = CopyAsset(oldProfile, ProfilePath);
            profile.name = "PlayerAppearance_C02_ReRig";
            profile.ModelHeight = 1.75f;
            profile.UniformScale = inspection.targetScale;
            profile.LocalOffset = Vector3.up * (-inspection.neutralBounds.min.y * inspection.targetScale);
            profile.DrawingArmWeight = 0f;
            EditorUtility.SetDirty(profile);

            var root = Object.Instantiate(source);
            root.name = "PF_Player_C02_ReRig";
            try
            {
                root.transform.SetPositionAndRotation(profile.LocalOffset, Quaternion.identity);
                root.transform.localScale = Vector3.one * profile.UniformScale;
                var animator = root.GetComponent<Animator>();
                animator.runtimeAnimatorController = profile.Controller;
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                ApplyPlayerMaterials(root);
                var gestureProfile = BuildGestureProfile(root);
                StripMarkers(root);
                GameObject near = BuildNearArm(source, root.transform);
                Transform worldBrush = BuildBrush("C02_WorldBrush", root.transform);
                Transform nearBrush = BuildBrush("C02_NearBrush", root.transform);
                var gesture = root.AddComponent<WorldMacroPlayerGestureRig>();
                gesture.ConfigureWorldBrush(worldBrush, Find(worldBrush, "GripSocket"), Find(worldBrush, "TipSocket"));
                gesture.ConfigureNearArm(near.transform, nearBrush, Find(nearBrush, "GripSocket"), Find(nearBrush, "TipSocket"),
                    near.GetComponentsInChildren<Renderer>(true));
                gesture.Configure(gestureProfile, animator, null, null, null, null, null, null, null, null);
                Need(gesture.TryBind() && gesture.Diagnostics.CalibratedFingers == 30 && gesture.Diagnostics.MissingFingers == 0,
                    "New gesture rig failed its authored bind.");
                var renderers = WorldRenderers(root, near.transform, nearBrush);
                var appearance = root.AddComponent<WorldMacroPlayerAppearance>();
                appearance.Configure(profile, animator, null, null, null, null, null, renderers);
                appearance.ConfigureGestureRig(gesture);
                ConfigureFootPlacement(root, animator, null, null, false);
                Need(PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) != null, "Could not save the derivative player prefab.");
                AssetDatabase.SaveAssets();
                if (installScene) InstallScene(profile);
                var report = new InstallReport
                {
                    status = installScene ? "INSTALLED_HAND_RIG_DERIVATIVE" : "PREPARED_HAND_RIG_DERIVATIVE", model = ModelPath,
                    prefab = PrefabPath, profile = ProfilePath, gestureProfile = GestureProfilePath,
                    scene = installScene ? WorldMacroPlaytestAuthoring.ScenePath : "NOT_MODIFIED", sourceFbxSha256 = Sha(ModelPath), gateScope = gate.scope,
                    fingerPoses = gestureProfile.Fingers.Length, nearTriangles = TriangleCount(near), bodyTriangles = inspection.triangles,
                    brushTriangles = TriangleCount(worldBrush.gameObject), rootMotionOff = !animator.applyRootMotion,
                    nearHasNoAnimator = near.GetComponentsInChildren<Animator>(true).Length == 0, existingSourcePreserved = true
                };
                Directory.CreateDirectory(Output); string json = JsonUtility.ToJson(report, true);
                File.WriteAllText(Path.Combine(Output, installScene ? "installation.json" : "preparation.json"), json);
                File.WriteAllText(Path.Combine(Output, "gesture_calibration.json"), JsonUtility.ToJson(gestureProfile, true));
                return json;
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static T CopyAsset<T>(T original, string path) where T : Object
        {
            Need(original != null, "Missing source asset for derivative: " + path);
            var copy = AssetDatabase.LoadAssetAtPath<T>(path);
            if (copy == null) { copy = Object.Instantiate(original); AssetDatabase.CreateAsset(copy, path); }
            else EditorUtility.CopySerialized(original, copy);
            return copy;
        }

        private static WorldMacroPlayerGestureProfile BuildGestureProfile(GameObject root)
        {
            var profile = AssetDatabase.LoadAssetAtPath<WorldMacroPlayerGestureProfile>(GestureProfilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<WorldMacroPlayerGestureProfile>(); AssetDatabase.CreateAsset(profile, GestureProfilePath); }
            profile.name = "PlayerGesture_C02_ReRig";
            var fingers = new List<WorldMacroPlayerGestureProfile.FingerPose>(30);
            foreach (string side in new[] { "Left", "Right" })
                foreach (string digit in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
                    for (int i = 1; i <= 3; i++)
                    {
                        string name = side + "Hand" + digit + i;
                        var bone = Find(root.transform, name); var grip = Find(root.transform, "POSE_Grip_" + name);
                        var relax = Find(root.transform, "POSE_Relax_" + name);
                        Need(bone != null && grip != null && relax != null, "Missing imported finger calibration: " + name);
                        Need(grip.parent == bone && relax.parent == bone, "Finger calibration must be a direct child of " + name);
                        Need(WorldMacroPlayerGestureProfile.IsUsableRotation(grip.localRotation) && WorldMacroPlayerGestureProfile.IsUsableRotation(relax.localRotation),
                            "Non-finite finger calibration quaternion: " + name);
                        fingers.Add(new WorldMacroPlayerGestureProfile.FingerPose
                        {
                            BoneName = name, UseExplicitRestPose = true, RestLocalRotation = bone.localRotation,
                            CarryOffset = side == "Right" ? grip.localRotation : relax.localRotation,
                            DrawingOffset = side == "Right" ? grip.localRotation : relax.localRotation,
                            HarvestOffset = side == "Right" ? grip.localRotation : relax.localRotation,
                            ReleasedOffset = relax.localRotation
                        });
                    }
            var hand = Find(root.transform, "RightHand"); var reference = Find(root.transform, "GripReference_R");
            Need(hand != null && reference != null, "Missing fitted GripReference_R.");
            profile.Fingers = fingers.ToArray();
            profile.RightHandGripPosition = hand.InverseTransformPoint(reference.position);
            profile.RightHandGripRotation = Quaternion.Inverse(hand.rotation) * reference.rotation;
            profile.ChestName = "Spine";
            EditorUtility.SetDirty(profile); return profile;
        }

        private static void ApplyPlayerMaterials(GameObject root)
        {
            var materials = WorldMacroPlayerAppearanceAuthoring.OwnedMaterialPaths.Select((path, index) =>
            {
                var original = AssetDatabase.LoadAssetAtPath<Material>(path); Need(original != null, "Missing verified C02 material: " + path);
                return CopyAsset(original, Folder + "/Materials/M_C02_ReRig_" + index + ".mat");
            }).ToArray();
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.sharedMaterials = Enumerable.Repeat(materials[skin.name.StartsWith("FaceLid", StringComparison.Ordinal) ? 1 : 0], skin.sharedMesh.subMeshCount).ToArray();
                skin.quality = SkinQuality.Auto; skin.updateWhenOffscreen = true;
            }
        }

        private static GameObject BuildNearArm(GameObject source, Transform parent)
        {
            GameObject near = Object.Instantiate(source, parent);
            near.name = "C02_NearArm";
            near.transform.localPosition = Vector3.zero; near.transform.localRotation = Quaternion.identity; near.transform.localScale = Vector3.one;
            foreach (var animator in near.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
            foreach (var skin in near.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh subset = ExtractRightArm(skin);
                if (subset == null) { Object.DestroyImmediate(skin); continue; }
                string path = Folder + "/Near/SM_Near_" + skin.name + ".asset";
                Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null) AssetDatabase.CreateAsset(subset, path);
                else { EditorUtility.CopySerialized(subset, existing); Object.DestroyImmediate(subset); subset = existing; }
                skin.sharedMesh = subset; skin.shadowCastingMode = ShadowCastingMode.Off; skin.receiveShadows = false; skin.updateWhenOffscreen = true;
            }
            foreach (var renderer in near.GetComponentsInChildren<MeshRenderer>(true)) Object.DestroyImmediate(renderer);
            StripMarkers(near); ApplyPlayerMaterials(near);
            Need(near.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0, "Right arm extraction produced no visible sleeve/hand triangles.");
            Need(TriangleCount(near) <= 26000, "Near-arm skin exceeds the 26,000 triangle budget.");
            foreach (var renderer in near.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            return near;
        }

        private static Mesh ExtractRightArm(SkinnedMeshRenderer skin)
        {
            Mesh source = skin.sharedMesh; var bones = skin.bones;
            var sourceCounts = source.GetBonesPerVertex(); var sourceWeights = source.GetAllBoneWeights();
            try
            {
                bool[] selected = new bool[source.vertexCount]; int[] weightStart = new int[source.vertexCount]; int offset = 0;
                for (int i = 0; i < source.vertexCount; i++)
                {
                    weightStart[i] = offset; float right = 0f;
                    for (int j = 0; j < sourceCounts[i]; j++)
                    {
                        var w = sourceWeights[offset++]; string name = w.boneIndex < bones.Length && bones[w.boneIndex] != null ? bones[w.boneIndex].name : "";
                        if (name == "RightArm" || name == "RightForeArm" || name.StartsWith("RightHand", StringComparison.Ordinal)) right += w.weight;
                    }
                    selected[i] = right >= .65f;
                }
                var remap = new Dictionary<int, int>(); var ids = new List<int>(); var submeshes = new List<int[]>();
                for (int sub = 0; sub < source.subMeshCount; sub++)
                {
                    int[] triangles = source.GetTriangles(sub); var retained = new List<int>();
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        if (!selected[triangles[i]] || !selected[triangles[i + 1]] || !selected[triangles[i + 2]]) continue;
                        for (int j = 0; j < 3; j++)
                        { int old = triangles[i + j]; if (!remap.TryGetValue(old, out int next)) { next = ids.Count; remap.Add(old, next); ids.Add(old); } retained.Add(next); }
                    }
                    submeshes.Add(retained.ToArray());
                }
                if (ids.Count == 0) return null;
                var mesh = new Mesh { name = "Near_" + skin.name, indexFormat = ids.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                Vector3[] vertices = source.vertices; mesh.vertices = ids.Select(i => vertices[i]).ToArray();
                Vector3[] normals = source.normals; if (normals.Length == source.vertexCount) mesh.normals = ids.Select(i => normals[i]).ToArray();
                Vector4[] tangents = source.tangents; if (tangents.Length == source.vertexCount) mesh.tangents = ids.Select(i => tangents[i]).ToArray();
                Color[] colors = source.colors; if (colors.Length == source.vertexCount) mesh.colors = ids.Select(i => colors[i]).ToArray();
                for (int channel = 0; channel < 8; channel++)
                { var uv = new List<Vector4>(); source.GetUVs(channel, uv); if (uv.Count == source.vertexCount) mesh.SetUVs(channel, ids.Select(i => uv[i]).ToList()); }
                mesh.bindposes = source.bindposes;
                var counts = new NativeArray<byte>(ids.Count, Allocator.Temp); var weights = new List<BoneWeight1>();
                for (int i = 0; i < ids.Count; i++)
                { int old = ids[i]; counts[i] = sourceCounts[old]; for (int j = 0; j < sourceCounts[old]; j++) weights.Add(sourceWeights[weightStart[old] + j]); }
                using (var all = new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Temp)) mesh.SetBoneWeights(counts, all);
                counts.Dispose();
                mesh.subMeshCount = submeshes.Count; for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i);
                mesh.RecalculateBounds(); return mesh;
            }
            finally { sourceCounts.Dispose(); sourceWeights.Dispose(); }
        }

        private static Transform BuildBrush(string name, Transform parent)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(BrushSource); Need(asset != null, "Existing brush model is missing.");
            GameObject brush = Object.Instantiate(asset, parent); brush.name = name;
            Vector3 parentScale = parent.lossyScale;
            Need(Mathf.Abs(parentScale.x - parentScale.y) < .00001f && Mathf.Abs(parentScale.y - parentScale.z) < .00001f && parentScale.x > 0,
                "Brush requires a positive uniform body scale.");
            brush.transform.localScale = Vector3.one / parentScale.x;
            foreach (var animator in brush.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
            var handleMaterial = CopyAsset(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Characters/DosaV2/Materials/M_DosaBrushV2.mat"), Folder + "/Materials/M_Brush_ReRig.mat");
            var bristleMaterial = CopyAsset(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Characters/DosaV2/Materials/M_DosaBrushV2_Bristles.mat"), Folder + "/Materials/M_BrushBristles_ReRig.mat");
            Shader brushShader = Shader.Find("Oheangbu/WorldMacroTexturedSurface");
            Need(brushShader != null, "Approved local brush lighting shader is missing.");
            foreach (var material in new[] { handleMaterial, bristleMaterial })
            {
                Texture baseMap = material.GetTexture("_BaseMap"), normalMap = material.GetTexture("_BumpMap");
                Vector2 tiling = material.GetTextureScale("_BaseMap"), offset = material.GetTextureOffset("_BaseMap");
                material.shader = brushShader; material.SetTexture("_BaseMap", baseMap); material.SetTexture("_BumpMap", normalMap);
                material.SetTextureScale("_BaseMap", tiling); material.SetTextureOffset("_BaseMap", offset);
                material.SetColor("_BaseColor", Color.white); material.SetFloat("_Saturation", .78f);
                material.SetFloat("_AmbientFloor", .48f); material.SetFloat("_LightResponse", .75f); material.SetFloat("_BumpScale", .7f);
                material.SetFloat("_Cull", material == bristleMaterial ? 0f : 2f);
                material.shaderKeywords = Array.Empty<string>(); material.renderQueue = -1; material.enableInstancing = true;
                EditorUtility.SetDirty(material);
            }
            foreach (var renderer in brush.GetComponentsInChildren<Renderer>(true))
            { renderer.sharedMaterials = Enumerable.Repeat(renderer.name.IndexOf("Bristle", StringComparison.OrdinalIgnoreCase) >= 0 ? bristleMaterial : handleMaterial, renderer.sharedMaterials.Length).ToArray(); }
            Need(Find(brush.transform, "GripSocket") != null && Find(brush.transform, "TipSocket") != null, "The existing brush must expose real grip and tip sockets.");
            var deformationSource = AssetDatabase.LoadAssetAtPath<BrushDeformationProfileSO>(BrushProfileSource);
            var deformation = CopyAsset(deformationSource, Folder + "/Brush/BrushDeformation_ReRig.asset");
            Transform[] bristleBones = Enumerable.Range(1, 6).Select(i => Find(brush.transform, "Bristle_" + i.ToString("00"))).ToArray();
            Need(bristleBones.All(b => b != null), "The approved six-bone brush chain is incomplete.");
            var bristleSkin = brush.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name.IndexOf("Bristle", StringComparison.OrdinalIgnoreCase) >= 0);
            var bristles = brush.AddComponent<BrushBristleRig>();
            Need(bristles.Configure(deformation, Find(brush.transform, "GripSocket"), Find(brush.transform, "TipSocket"), bristleBones, bristleSkin),
                "Brush bristle binding failed: " + bristles.BindingError);
            return brush.transform;
        }

        private static void InstallScene(WorldMacroPlayerAppearanceProfile profile)
        {
            var walker = Object.FindFirstObjectByType<WorldMacroCombatWalker>();
            Need(walker != null && walker.Body != null, "Existing combat walker/body is missing.");
            Transform body = walker.Body.transform;
            var old = body.GetComponentsInChildren<WorldMacroPlayerAppearance>(true)
                .Where(a => a.transform.parent == body && (a.name == RootName || a.name == WorldMacroPlayerAppearanceAuthoring.SceneRootName)).ToArray();
            Need(old.Length <= 1, "Multiple active/old appearances require manual diagnosis before replacement.");
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Install validated C02 hand rig");
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), body);
                Need(instance != null, "Could not instantiate the prepared player prefab.");
                Undo.RegisterCreatedObjectUndo(instance, "Install validated C02 hand rig");
                instance.name = RootName; instance.transform.localPosition = profile.LocalOffset;
                instance.transform.localRotation = Quaternion.identity; instance.transform.localScale = Vector3.one * profile.UniformScale;
                var near = Find(instance.transform, "C02_NearArm"); var nearBrush = Find(instance.transform, "C02_NearBrush");
                var renderers = WorldRenderers(instance, near, nearBrush);
                var animator = instance.GetComponent<Animator>(); var appearance = instance.GetComponent<WorldMacroPlayerAppearance>();
                var gesture = instance.GetComponent<WorldMacroPlayerGestureRig>();
                var feed = body.root.GetComponentInChildren<BrushStrokeFeedAdapter>(true);
                var harvest = body.GetComponent<HarvestAction>(); var effect = body.root.GetComponentInChildren<HarvestInkStreamEffect>(true);
                Need(feed != null && walker.Drawing != null && walker.Motor != null && walker.CameraRig != null, "Existing player dependencies are incomplete.");
                gesture.Configure(AssetDatabase.LoadAssetAtPath<WorldMacroPlayerGestureProfile>(GestureProfilePath), animator,
                    walker.Motor, walker.Body, walker.Drawing, feed, walker.CameraRig, walker, harvest, effect);
                Need(gesture.TryBind() && gesture.Diagnostics.CalibratedFingers == 30, "Installed candidate gesture bind failed.");
                appearance.Configure(profile, animator, walker.Body, walker.Motor, walker.Drawing, walker.CameraRig, feed, renderers);
                appearance.ConfigureGestureRig(gesture);
                ConfigureFootPlacement(instance, animator, walker.Body, walker, false);
                Undo.RecordObject(walker, "Replace player renderer ownership");
                Undo.RecordObject(walker.CameraRig, "Replace camera renderer ownership");
                foreach (var previous in old) Undo.DestroyObjectImmediate(previous.gameObject);
                walker.Visuals = renderers; walker.CameraRig.SetBodyRenderers(renderers);
                var primitive = body.Find("Body")?.GetComponent<Renderer>();
                if (primitive != null)
                {
                    Undo.RecordObject(primitive, "Hide capsule presentation");
                    primitive.enabled = false; primitive.shadowCastingMode = ShadowCastingMode.Off;
                }
                EditorUtility.SetDirty(walker); EditorUtility.SetDirty(walker.CameraRig); EditorUtility.SetDirty(appearance); EditorUtility.SetDirty(gesture);
                Undo.FlushUndoRecordObjects();
                EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                Need(EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene()),
                    "Saving the player installation failed; the previous scene appearance will be restored.");
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        internal static Transform Find(Transform root, string name) => root != null ? root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name) : null;
        internal static int TriangleCount(GameObject root) => root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(s => s.sharedMesh.triangles.Length / 3)
            + root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).Sum(f => f.sharedMesh.triangles.Length / 3);
        private static Renderer[] WorldRenderers(GameObject root, Transform near, Transform nearBrush) => root.GetComponentsInChildren<Renderer>(true)
            .Where(r => (near == null || !r.transform.IsChildOf(near)) && (nearBrush == null || !r.transform.IsChildOf(nearBrush))).ToArray();
        private static void StripMarkers(GameObject root)
        { foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("POSE_", StringComparison.Ordinal) || t.name == "GripReference_R").ToArray()) Object.DestroyImmediate(t.gameObject); }
    }
}
