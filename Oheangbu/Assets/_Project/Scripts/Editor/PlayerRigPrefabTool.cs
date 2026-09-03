using System.Collections.Generic;
using System.Text;
using Oheangbu.App;
using Oheangbu.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // [SPEC-DEV-TEST-HUB §3] 플레이어 리그 프리팹 — C1_CombatLoop의 손조립 리그(Player 서브트리 + CombatSystems)를
    // PlayerRig 프리팹으로 추출하고, 다른 개발 씬의 복제 리그를 프리팹 인스턴스로 바꾼다.
    // seam 4필드(LockOn._target · Wiring._enemy/_enemyVitals/_enemies)만 씬 오버라이드로 남긴다 — 점검 메뉴가 감시.
    public static class PlayerRigPrefabTool
    {
        private const string RigRootName = "PlayerRig";

        [MenuItem("Oheangbu/Dev/허브 1. PlayerRig 프리팹 추출 (C1_CombatLoop)")]
        private static void ExtractMenu()
        {
            Debug.Log("[PlayerRigPrefabTool] " + Extract());
        }

        [MenuItem("Oheangbu/Dev/허브 2. 리그 교체 (C1_SpellRange)")]
        private static void ReplaceRigMenu()
        {
            Debug.Log("[PlayerRigPrefabTool] " + ReplaceRig());
        }

        [MenuItem("Oheangbu/Dev/리그 오버라이드 점검 (현재 씬)")]
        private static void CheckOverridesMenu()
        {
            Debug.Log("[PlayerRigPrefabTool] " + CheckOverrides());
        }

        public static string Extract()
        {
            if (Application.isPlaying) return "플레이 중에는 추출하지 않는다(HUD_Canvas 등 런타임 산출물 혼입)";
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != "C1_CombatLoop") return $"활성 씬이 C1_CombatLoop가 아님: {scene.name}";
            if (!DevSceneKit.FindRigParts(out var player, out var systems)) return "Player/CombatSystems 없음";
            if (PrefabUtility.IsPartOfPrefabInstance(player)) return "이미 프리팹 인스턴스 — 추출 불요";
            if (player.transform.parent != null || systems.transform.parent != null) return "Player/CombatSystems가 루트가 아님";

            var wiring = systems.GetComponent<CombatLoopWiring>();
            if (wiring == null) return "CombatLoopWiring 없음";

            // seam 스냅숏 → null(에셋에 씬 참조가 남지 않게 결정적으로) → 저장 후 인스턴스에 재적용
            var wiringSo = new SerializedObject(wiring);
            Object enemy = wiringSo.FindProperty("_enemy").objectReferenceValue;
            Object enemyVitals = wiringSo.FindProperty("_enemyVitals").objectReferenceValue;
            var enemies = new List<Object>();
            var enemiesProp = wiringSo.FindProperty("_enemies");
            for (int i = 0; i < enemiesProp.arraySize; i++) enemies.Add(enemiesProp.GetArrayElementAtIndex(i).objectReferenceValue);

            wiringSo.FindProperty("_enemy").objectReferenceValue = null;
            wiringSo.FindProperty("_enemyVitals").objectReferenceValue = null;
            enemiesProp.arraySize = 0;
            wiringSo.ApplyModifiedPropertiesWithoutUndo();

            var root = new GameObject(RigRootName);
            root.transform.SetPositionAndRotation(player.transform.position, player.transform.rotation);
            player.transform.SetParent(root.transform, true); // 로컬 identity
            systems.transform.SetParent(root.transform, false);
            systems.transform.localPosition = Vector3.zero;
            systems.transform.localRotation = Quaternion.identity;

            DevSceneKit.EnsureFolder("Assets/_Project/Prefabs/Rig");
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, DevSceneKit.RigPrefabPath, InteractionMode.AutomatedAction, out bool saved);
            if (!saved) return "프리팹 저장 실패";

            wiringSo = new SerializedObject(wiring);
            wiringSo.FindProperty("_enemy").objectReferenceValue = enemy;
            wiringSo.FindProperty("_enemyVitals").objectReferenceValue = enemyVitals;
            DevSceneKit.SetObjectArray(wiringSo, "_enemies", enemies);
            wiringSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"추출 완료 — {DevSceneKit.RigPrefabPath} · {CheckAssetSeam()} · {CheckOverrides()} · {DevSceneKit.AssertSingleRig()}";
        }

        // C1_SpellRange: 손조립 복제 리그를 지우고 프리팹 인스턴스로 — 이후 SpellRangeSceneBuilder.Build가 과녁·배선을 다시 만든다
        public static string ReplaceRig()
        {
            if (Application.isPlaying) return "플레이 중 거부";
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != "C1_SpellRange") return $"활성 씬이 C1_SpellRange가 아님: {scene.name}";
            if (!DevSceneKit.FindRigParts(out var player, out var systems)) return "Player/CombatSystems 없음";
            if (PrefabUtility.IsPartOfPrefabInstance(player)) return "이미 프리팹 인스턴스 — 교체 불요(Build만 재실행)";
            var enemy = GameObject.Find("Enemy");
            if (enemy == null) return "Enemy 없음";

            Vector3 position = player.transform.position;
            float yaw = player.transform.eulerAngles.y;
            Object.DestroyImmediate(player);   // Main Camera·DrawingRig 등 서브트리째
            Object.DestroyImmediate(systems);

            DevSceneKit.InstantiateRig(scene, position, yaw);
            var vitals = enemy.GetComponent<EnemyVitals>();
            DevSceneKit.WireRigSeam(enemy.GetComponent<EnemyController>(), vitals, new List<EnemyVitals>());

            string build = SpellRangeSceneBuilder.Build(); // 과녁·디렉터·_enemies·선택대·귀환 포탈 + 저장
            return $"리그 교체 완료 — {build} · {CheckOverrides()} · {DevSceneKit.AssertSingleRig()}";
        }

        public static string CheckAssetSeam()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(DevSceneKit.RigPrefabPath);
            if (asset == null) return "프리팹 에셋 없음";
            var wiring = asset.GetComponentInChildren<CombatLoopWiring>(true);
            if (wiring == null) return "에셋에 Wiring 없음";
            var wiringSo = new SerializedObject(wiring);
            bool clean = wiringSo.FindProperty("_enemy").objectReferenceValue == null
                && wiringSo.FindProperty("_enemyVitals").objectReferenceValue == null
                && wiringSo.FindProperty("_enemies").arraySize == 0;
            return clean ? "에셋 seam null 확인" : "⚠ 에셋에 seam 참조가 남음";
        }

        // 리그 인스턴스의 오버라이드가 허용 집합(seam 4필드 + 루트 트랜스폼) 안인지
        public static string CheckOverrides()
        {
            var sb = new StringBuilder();
            int rigs = 0;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(root)) continue;
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) != DevSceneKit.RigPrefabPath) continue;
                rigs++;
                var modifications = PrefabUtility.GetPropertyModifications(root);
                if (modifications == null) continue;
                foreach (var modification in modifications)
                {
                    if (IsAllowed(modification)) continue;
                    string owner = modification.target != null ? modification.target.GetType().Name : "?";
                    sb.Append(' ').Append(owner).Append('.').Append(modification.propertyPath);
                }
            }
            if (rigs == 0) return "리그 인스턴스 없음";
            return sb.Length == 0 ? $"오버라이드 점검 통과(리그 {rigs})" : "⚠ 허용 밖 오버라이드:" + sb;
        }

        private static bool IsAllowed(PropertyModification modification)
        {
            var target = modification.target;
            string path = modification.propertyPath;
            if (target is Transform transform && transform.gameObject.name == RigRootName) return true;
            if (target is GameObject go && go.name == RigRootName) return true;
            if (target is CombatLoopWiring) return path.StartsWith("_enemy") || path.StartsWith("_enemies") || path.StartsWith("_enemyVitals");
            return false;
        }
    }
}
