using System;
using System.Linq;
using Oheangbu.App.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroPlayerReRigAuthoring
    {
        private static WorldMacroPlayerFootPlacement ConfigureFootPlacement(GameObject root, Animator animator,
            CharacterController controller, WorldMacroCombatWalker walker, bool undo)
        {
            var component = root.GetComponent<WorldMacroPlayerFootPlacement>();
            if (component == null) component = undo ? Undo.AddComponent<WorldMacroPlayerFootPlacement>(root) : root.AddComponent<WorldMacroPlayerFootPlacement>();
            else if (undo) Undo.RecordObject(component, "Configure derivative foot clearance");
            Transform near = Find(root.transform, "C02_NearArm"), worldBrush = Find(root.transform, "C02_WorldBrush"), nearBrush = Find(root.transform, "C02_NearBrush");
            var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s =>
                (near == null || !s.transform.IsChildOf(near)) && (worldBrush == null || !s.transform.IsChildOf(worldBrush))
                && (nearBrush == null || !s.transform.IsChildOf(nearBrush))).ToArray();
            component.Configure(animator, controller, walker, skins);
            Need(component.IsBound, "Foot clearance binding failed: " + component.BindingError);
            EditorUtility.SetDirty(component);
            return component;
        }

        private static string InstallFootPlacementOnly()
        {
            Need(!EditorApplication.isPlaying && !EditorApplication.isCompiling, "Foot setup requires stopped, compiled Editor state.");
            RequireGate();
            Need(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == WorldMacroPlaytestAuthoring.ScenePath,
                "Only the existing WorldMacro playtest scene may receive this component.");
            var walker = Object.FindFirstObjectByType<WorldMacroCombatWalker>();
            Need(walker != null && walker.Body != null, "The existing playtest walker is required.");
            Transform root = walker.Body.transform.Find(RootName);
            Need(root != null && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root.gameObject) == PrefabPath,
                "The current validated derivative player prefab must already be installed.");
            GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ConfigureFootPlacement(prefab, prefab.GetComponent<Animator>(), null, null, false);
                Need(PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath) != null, "Could not save derivative foot clearance on the prefab.");
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }

            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Attach derivative foot clearance");
            try
            {
                var foot = ConfigureFootPlacement(root.gameObject, root.GetComponent<Animator>(), walker.Body, walker, true);
                PrefabUtility.RecordPrefabInstancePropertyModifications(foot);
                Undo.FlushUndoRecordObjects();
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(scene);
                Need(EditorSceneManager.SaveScene(scene), "Scene save failed; the scene component change will be reverted. The separately prepared prefab remains available.");
                Undo.CollapseUndoOperations(group);
                return JsonUtility.ToJson(new FootSetupReport { status = "INSTALLED_DERIVATIVE_FOOT_CLEARANCE", prefab = PrefabPath,
                    scene = scene.path, leftSoleSamples = foot.Diagnostics.LeftSoleSamples, rightSoleSamples = foot.Diagnostics.RightSoleSamples }, true);
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
        }

        [Serializable] private sealed class FootSetupReport
        {
            public string status, prefab, scene;
            public int leftSoleSamples, rightSoleSamples;
            public string scope = "Existing derivative only. Penetrating sole clearance before the upper-body gesture pass; no motion clip, speed, controller, body scale or gesture-profile changes. Actual contact still requires validation.";
        }
    }
}
