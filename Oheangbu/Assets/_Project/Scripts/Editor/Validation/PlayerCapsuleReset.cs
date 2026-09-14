using System;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static class PlayerCapsuleReset
    {
        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Saved Edit mode required.");
            string backup = Path.GetFullPath("../Art/PlayerReset/" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(backup);
            File.Copy(DevSceneKit.RigPrefabPath, backup + "/PlayerRig.prefab");
            File.Copy(CodexWorldSceneBuilder.ScenePath, backup + "/C2_CodexWorld.unity");
            var shared = PrefabUtility.LoadPrefabContents(DevSceneKit.RigPrefabPath);
            try { Reset(shared); PrefabUtility.SaveAsPrefabAsset(shared, DevSceneKit.RigPrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(shared); }
            var scene = EditorSceneManager.OpenScene(CodexWorldSceneBuilder.ScenePath, OpenSceneMode.Single);
            var motor = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerMotor>(true)).Single();
            Reset(motor.transform.root.gameObject);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            string result = "CAPSULE_RESET\nBackup=" + backup + "\n" + Audit();
            File.WriteAllText(backup + "/reset.txt", result); return result;
        }
        static void Record(Object o)
        {
            EditorUtility.SetDirty(o);
            if (PrefabUtility.IsPartOfPrefabInstance(o)) PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        }
        static void Reset(GameObject root)
        {
            var motor = root.GetComponentInChildren<PlayerMotor>(true);
            foreach (var driver in root.GetComponentsInChildren<PlayerVisualDriver>(true)) Object.DestroyImmediate(driver);
            foreach (var rig in root.GetComponentsInChildren<PlayerVisualRig>(true)) Object.DestroyImmediate(rig.gameObject);
            var body = motor.transform.Find("Body");
            if (body == null) throw new InvalidOperationException("Original capsule Body missing.");
            body.gameObject.SetActive(true); Record(body.gameObject);
            var renderer = body.GetComponent<Renderer>();
            renderer.enabled = true; renderer.forceRenderingOff = false; renderer.shadowCastingMode = ShadowCastingMode.On; Record(renderer);
            var cameraRig = root.GetComponentInChildren<CameraRigController>(true);
            cameraRig.SetBodyRenderers(new[] { renderer }); cameraRig.SetDrawingPresentationActive(false); Record(cameraRig);
            var main = root.GetComponentsInChildren<Camera>(true).Single(c => c.CompareTag("MainCamera"));
            var data = main.GetUniversalAdditionalCameraData();
            data.cameraStack.RemoveAll(c => c == null || c.name == "DosaArmsCamera"); Record(data);
            foreach (var camera in root.GetComponentsInChildren<Camera>(true).Where(c => c.name == "DosaArmsCamera").ToArray())
                Object.DestroyImmediate(camera.gameObject);
            // Keep the pre-character drawing/harvest anchor without displaying a replacement brush.
            var brush = motor.transform.Find("BrushProp");
            if (brush != null) { brush.gameObject.SetActive(false); Record(brush.gameObject); }
            foreach (var effect in root.GetComponentsInChildren<HarvestInkStreamEffect>(true))
            {
                var so = new SerializedObject(effect);
                so.FindProperty("_sinkAnchor").objectReferenceValue = brush != null ? brush : main.transform;
                so.ApplyModifiedPropertiesWithoutUndo(); Record(effect);
            }
        }
        public static string Audit()
        {
            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            var root = motor.transform.root.gameObject;
            return "Visual rigs=" + root.GetComponentsInChildren<PlayerVisualRig>(true).Length
                + ", drivers=" + root.GetComponentsInChildren<PlayerVisualDriver>(true).Length
                + ", cloth=" + root.GetComponentsInChildren<Cloth>(true).Length
                + ", animators=" + root.GetComponentsInChildren<Animator>(true).Length
                + ", skinned meshes=" + root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length
                + ", capsule active=" + motor.transform.Find("Body").gameObject.activeSelf;
        }
    }
}
