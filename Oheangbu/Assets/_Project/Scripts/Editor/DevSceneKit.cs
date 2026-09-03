using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // [SPEC-DEV-TEST-HUB] 개발 씬 조립 공용 도구 — 씬 템플릿·리그 프리팹 인스턴스·seam 배선·그레이박스 소품·재질·Build Settings.
    // 배치표는 각 빌더가 들고, 여기는 「만드는 법」만. 소품 재질은 Emission 0(ART-INK 광원 3등급 — 소품은 명도 대비만).
    public static class DevSceneKit
    {
        public static readonly Vector3 Spawn = new Vector3(0f, 1.1f, -6f); // 모든 개발 씬 공통 시작점(캡슐 중심)·전방 +Z
        public static readonly Vector3 ReturnPortalPosition = new Vector3(3f, 0f, -9f); // 시작점 뒤오른쪽 — 사격 cone 밖

        public const string RigPrefabPath = "Assets/_Project/Prefabs/Rig/PlayerRig.prefab";
        public const string HubScenePath = "Assets/_Project/Scenes/Dev/C1_TestHub.unity";
        public const string EffectLabScenePath = "Assets/_Project/Scenes/Dev/C1_EffectLab.unity";
        public const string ParryRangeScenePath = "Assets/_Project/Scenes/Dev/C1_ParryRange.unity";
        public const string SpellRangeScenePath = "Assets/_Project/Scenes/Dev/C1_SpellRange.unity";
        public const string CombatLoopScenePath = "Assets/_Project/Scenes/Dev/C1_CombatLoop.unity";
        public const string DefaultConfigPath = "Assets/_Project/Data/Configs/CombatConfig_Default.asset";
        public const string RangeDummyConfigPath = "Assets/_Project/Data/Configs/CombatConfig_RangeDummy.asset";
        public const string ParryRangeConfigPath = "Assets/_Project/Data/Configs/CombatConfig_ParryRange.asset";
        public const string SpellBookPath = "Assets/_Project/Data/Configs/SpellBook_Proto.asset";
        public const string PalettePath = "Assets/_Project/Data/Configs/ElementPalette_Test.asset";
        public const string InkLookProfilePath = "Assets/_Project/Data/Configs/VP_InkLook.asset";
        public const string DrawModeChannelGuid = "6bd2b9d9cf21b424bb4640370dc081d9"; // EC_DrawingMode
        public const string LetterChannelGuid = "c5b5038e37bdeff4abbb1ae079f460fb";   // EC_DrawnLetter
        private const string MaterialFolder = "Assets/_Project/Art/Materials";

        // ---- 에셋 ----

        public static T LoadByGuid<T>(string guid) where T : Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
        }

        public static void EnsureFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        public static Material EnsureMaterial(string fileName, Color color)
        {
            string path = $"{MaterialFolder}/{fileName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            EnsureFolder(MaterialFolder);
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.1f);
            material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        public static Material InkMaterial() => EnsureMaterial("M_DevInk", DevLabel.Ink);
        public static Material StoneMaterial() => EnsureMaterial("M_DevStone", new Color(0.45f, 0.43f, 0.41f));

        // 속성색 캡 — 팔레트가 단일 출처(색=의미)
        public static Material ElementMaterial(Element element)
        {
            var palette = AssetDatabase.LoadAssetAtPath<ElementPaletteSO>(PalettePath);
            Color color = palette != null ? palette.GetBaseColor(DevElement.Initial(element)) : Color.gray;
            return EnsureMaterial($"M_DevElem_{element}", color); // 파일명은 ASCII(Wood…) — 도구 호환
        }

        // 패링장 전용 설정 — Default 복제 + 근접 도달 불가(원거리 전용) + 불사. 밸런스 정본 아님(§9-5)
        public static CombatConfigSO EnsureParryConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(ParryRangeConfigPath);
            if (config == null)
            {
                AssetDatabase.CopyAsset(DefaultConfigPath, ParryRangeConfigPath);
                config = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(ParryRangeConfigPath);
            }
            var so = new SerializedObject(config);
            so.FindProperty("_enemyMeleePreferRange").floatValue = 0.5f;
            so.FindProperty("_enemyMaxHp").floatValue = 100000f;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            // 저장이 에셋을 재임포트하면 위 참조는 「죽은 객체」(fake null)가 되어 씬에 fileID 0으로 박힌다 — 반드시 다시 로드
            return AssetDatabase.LoadAssetAtPath<CombatConfigSO>(ParryRangeConfigPath);
        }

        public static void RegisterBuildScenes(params string[] paths)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int insertAt = 0;
            foreach (var path in paths)
            {
                int existing = list.FindIndex(s => s.path == path);
                if (existing >= 0) list.RemoveAt(existing);
                list.Insert(insertAt++, new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets(); // ProjectSettings/EditorBuildSettings.asset을 즉시 디스크에
        }

        // ---- 씬 템플릿 ----

        public static Scene NewEmptyScene()
        {
            EditorSceneManager.SaveOpenScenes();
            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // Directional Light · Ground(Plane ×3 = 30×30m) · GlobalVolume_InkLook — C1_CombatLoop와 같은 그레이박스 바닥
        public static void AddSceneBase()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.957f, 0.839f);
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.Euler(50f, -30f, 0f));

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(3f, 1f, 3f);

            var volumeGo = new GameObject("GlobalVolume_InkLook");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(InkLookProfilePath);
        }

        public static GameObject InstantiateRig(Scene scene, Vector3 position, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (prefab == null) throw new System.InvalidOperationException("PlayerRig 프리팹 없음 — 먼저 추출(허브 1)");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            rig.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return rig;
        }

        public static bool FindRigParts(out GameObject player, out GameObject systems)
        {
            player = GameObject.Find("Player");
            systems = GameObject.Find("CombatSystems");
            return player != null && systems != null;
        }

        // seam 3필드 — 리그 인스턴스에 허용된 유일한 오버라이드(§3). 락온 후보는 배선부가 이 집합에서 넘긴다(#139)
        public static void WireRigSeam(EnemyController enemy, EnemyVitals enemyVitals, IList<EnemyVitals> enemies)
        {
            if (!FindRigParts(out var player, out var systems)) throw new System.InvalidOperationException("리그(Player/CombatSystems) 없음");
            var so = new SerializedObject(systems.GetComponent<CombatLoopWiring>());
            so.FindProperty("_enemy").objectReferenceValue = enemy;
            so.FindProperty("_enemyVitals").objectReferenceValue = enemyVitals;
            SetObjectArray(so, "_enemies", enemies);
            so.ApplyModifiedPropertiesWithoutUndo();
            PruneRigOverrides(player);
        }

        // 프리팹에서 사라진 필드의 잔존 오버라이드(예: 구 LockOn._target) 제거
        public static void PruneRigOverrides(GameObject anyRigPart)
        {
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(anyRigPart);
            if (root != null) PrefabUtility.RemoveUnusedOverrides(new[] { root }, InteractionMode.AutomatedAction);
        }

        public static string AssertSingleRig()
        {
            int wirings = Object.FindObjectsByType<CombatLoopWiring>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int cameras = GameObject.FindGameObjectsWithTag("MainCamera").Length;
            int listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            return wirings == 1 && cameras == 1 && listeners == 1
                ? "리그 단일 확인"
                : $"⚠ 리그 중복/누락 — wiring {wirings}·MainCamera {cameras}·AudioListener {listeners}";
        }

        public static void DestroyByName(params string[] names)
        {
            foreach (var name in names)
            {
                var go = GameObject.Find(name);
                while (go != null)
                {
                    Object.DestroyImmediate(go);
                    go = GameObject.Find(name);
                }
            }
        }

        // ---- 소품 ----

        public static Quaternion FaceRotation(Vector3 from, Vector3 target)
        {
            Vector3 dir = target - from;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : Quaternion.identity;
        }

        public static GameObject CreateDummy(string name, Vector3 position, string caption)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.position = position;
            var vitals = go.AddComponent<EnemyVitals>();
            SetRef(vitals, "_config", AssetDatabase.LoadAssetAtPath<CombatConfigSO>(RangeDummyConfigPath));
            var dummy = go.AddComponent<SpellRangeDummy>();
            var so = new SerializedObject(dummy);
            so.FindProperty("_vitals").objectReferenceValue = vitals;
            so.FindProperty("_renderer").objectReferenceValue = go.GetComponent<Renderer>();
            so.FindProperty("_config").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(RangeDummyConfigPath);
            so.FindProperty("_caption").stringValue = caption;
            so.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        public static GameObject CreateEnemy(string name, Vector3 position, CombatConfigSO config, Element element,
            Transform player, PlayerVitals playerVitals, bool startEnabled)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.position = position;
            var vitals = go.AddComponent<EnemyVitals>();
            SetRef(vitals, "_config", config);
            var controller = go.AddComponent<EnemyController>();
            var so = new SerializedObject(controller);
            so.FindProperty("_config").objectReferenceValue = config;
            so.FindProperty("_vitals").objectReferenceValue = vitals;
            so.FindProperty("_player").objectReferenceValue = player;
            so.FindProperty("_playerVitals").objectReferenceValue = playerVitals;
            so.FindProperty("_renderer").objectReferenceValue = go.GetComponent<Renderer>();
            so.FindProperty("_rangedElement").intValue = (int)element;
            so.ApplyModifiedPropertiesWithoutUndo();
            controller.enabled = startEnabled;
            return go;
        }

        // 포탈 = 석판 + 먹빛 기둥(뒤). 앞면(+Z)이 플레이어 쪽
        public static DevScenePortal CreatePortal(string name, Vector3 position, Vector3 faceTarget,
            string title, string[] description, string scenePath, string hint)
        {
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(position, FaceRotation(position, faceTarget));
            AddCube(root.transform, "Slab", new Vector3(0f, 0.1f, 0f), new Vector3(1.2f, 0.2f, 1.2f), StoneMaterial());
            AddCube(root.transform, "Pillar", new Vector3(0f, 1.0f, -0.45f), new Vector3(0.25f, 1.6f, 0.25f), InkMaterial());
            var portal = root.AddComponent<DevScenePortal>();
            var so = new SerializedObject(portal);
            so.FindProperty("_title").stringValue = title;
            SetStringArray(so, "_description", description);
            so.FindProperty("_scenePath").stringValue = scenePath;
            so.FindProperty("_hint").stringValue = hint;
            so.ApplyModifiedPropertiesWithoutUndo();
            return portal;
        }

        public static DevScenePortal AddReturnPortal()
        {
            return CreatePortal("Portal_Return", ReturnPortalPosition, Spawn, "허브로 귀환",
                new[] { "C1_TestHub로 돌아간다" }, HubScenePath, "F: 귀환");
        }

        // 선택대 = 낮은 돌단 + (선택) 속성색 캡
        public static DevEnemyWakePedestal CreatePedestal(string name, Vector3 position, Vector3 faceTarget,
            string title, string[] description, EnemyController enemy, bool awakeAtStart, Material capMaterial)
        {
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(position, FaceRotation(position, faceTarget));
            AddCube(root.transform, "Block", new Vector3(0f, 0.25f, 0f), new Vector3(0.6f, 0.5f, 0.6f), StoneMaterial());
            if (capMaterial != null)
            {
                AddCube(root.transform, "Cap", new Vector3(0f, 0.53f, 0f), new Vector3(0.5f, 0.06f, 0.5f), capMaterial);
            }
            var pedestal = root.AddComponent<DevEnemyWakePedestal>();
            var so = new SerializedObject(pedestal);
            so.FindProperty("_title").stringValue = title;
            SetStringArray(so, "_description", description);
            so.FindProperty("_radius").floatValue = 1.2f;
            so.FindProperty("_titleHeight").floatValue = 1.6f;
            so.FindProperty("_titleSize").floatValue = 0.035f; // 선택대는 발치(1.5m) — 포탈 제목 크기면 화면을 덮는다
            so.FindProperty("_descriptionHeight").floatValue = 1.3f;
            so.FindProperty("_enemy").objectReferenceValue = enemy;
            so.FindProperty("_awakeAtStart").boolValue = awakeAtStart;
            so.ApplyModifiedPropertiesWithoutUndo();
            return pedestal;
        }

        // 판 = 먹빛 석판(width×height×0.15, 바닥에 세움), 글은 앞면(+Z) 중앙
        public static DevInfoBoard CreateBoard(string name, Vector3 position, Vector3 faceTarget, string text, float characterSize,
            float width = 4.2f, float height = 1.6f)
        {
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(position, FaceRotation(position, faceTarget));
            AddCube(root.transform, "Slab", new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, 0.15f), InkMaterial());
            var board = root.AddComponent<DevInfoBoard>();
            var so = new SerializedObject(board);
            so.FindProperty("_text").stringValue = text;
            so.FindProperty("_characterSize").floatValue = characterSize;
            so.FindProperty("_localOffset").vector3Value = new Vector3(0f, height * 0.5f, 0.09f);
            so.ApplyModifiedPropertiesWithoutUndo();
            return board;
        }

        public static DevInteractor AddInteractor(GameObject host, Transform player, IList<DevInteractable> interactables)
        {
            var interactor = host.AddComponent<DevInteractor>();
            var so = new SerializedObject(interactor);
            so.FindProperty("_player").objectReferenceValue = player;
            so.FindProperty("_drawModeChanged").objectReferenceValue = LoadByGuid<BoolEventChannelSO>(DrawModeChannelGuid);
            SetObjectArray(so, "_interactables", interactables);
            so.ApplyModifiedPropertiesWithoutUndo();
            return interactor;
        }

        private static GameObject AddCube(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        // ---- SerializedObject 보조 ----

        public static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetObjectArray<T>(SerializedObject so, string field, IList<T> items) where T : Object
        {
            var prop = so.FindProperty(field);
            prop.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
        }

        public static void SetStringArray(SerializedObject so, string field, IList<string> items)
        {
            var prop = so.FindProperty(field);
            prop.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                prop.GetArrayElementAtIndex(i).stringValue = items[i];
            }
        }
    }
}
