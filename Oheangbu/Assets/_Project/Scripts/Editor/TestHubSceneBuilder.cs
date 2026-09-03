using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    // [SPEC-DEV-TEST-HUB §2] 허브·이펙트 실험실·패링장 배치표의 단일 출처. 신설 씬은 전체 재생성(손조립 없음),
    // 기존 씬(C1_CombatLoop)은 귀환 포탈·인터랙터만 덧붙인다. 시작점·귀환 포탈 규약은 DevSceneKit.
    public static class TestHubSceneBuilder
    {
        private static readonly Vector3 SpawnGround = new Vector3(DevSceneKit.Spawn.x, 0f, DevSceneKit.Spawn.z);
        private static readonly Element[] ParryOrder = { Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water }; // 상생 순

        private const string HubBoardText =
            "[테스트 허브]\n" +
            "WASD 이동 · 마우스 시점 · Q(홀드) 작도 · LMB 획 / 갈무리(홀드)\n" +
            "Tab 락온 · LShift 회피 · V 카메라 실험 · F 상호작용 · R 씬 재시작\n" +
            "포탈 2m 안에서 설명이 뜬다 — F로 진입 · 방마다 귀환 포탈(시작점 뒤 오른쪽)";

        [MenuItem("Oheangbu/Dev/허브 3. 허브 씬 구성 (C1_TestHub)")]
        private static void BuildHubMenu() => Debug.Log("[TestHubSceneBuilder] " + BuildHub());

        [MenuItem("Oheangbu/Dev/허브 4. 이펙트 실험실 구성 (C1_EffectLab)")]
        private static void BuildEffectLabMenu() => Debug.Log("[TestHubSceneBuilder] " + BuildEffectLab());

        [MenuItem("Oheangbu/Dev/허브 5. 패링장 구성 (C1_ParryRange)")]
        private static void BuildParryRangeMenu() => Debug.Log("[TestHubSceneBuilder] " + BuildParryRange());

        [MenuItem("Oheangbu/Dev/허브 6. 전투 루프 귀환 포탈 (C1_CombatLoop)")]
        private static void AddCombatLoopReturnMenu() => Debug.Log("[TestHubSceneBuilder] " + AddCombatLoopReturn());

        [MenuItem("Oheangbu/Dev/허브 7. Build Settings 등록")]
        private static void RegisterScenesMenu() => Debug.Log("[TestHubSceneBuilder] " + RegisterScenes());

        // ---- 허브 ----

        public static string BuildHub()
        {
            var scene = DevSceneKit.NewEmptyScene();
            DevSceneKit.AddSceneBase();
            DevSceneKit.InstantiateRig(scene, DevSceneKit.Spawn, 0f);
            DevSceneKit.WireRigSeam(null, null, new List<EnemyVitals>());

            var portals = new List<DevInteractable>
            {
                DevSceneKit.CreatePortal("Portal_EffectLab", Arc(-36f, 7f), SpawnGround, "이펙트 실험실", new[]
                {
                    "불사 과녁 3체 — 공격 글자 10자의 실제 연출을 관찰한다",
                    "Tab 락온(L1 6m) → Q 작도: 가·나·마·사·아(단일) · 고·노·모·소·오(광역)",
                    "과녁 라벨=명중·피해 · 콘솔 [Range]=시전·착탄 시각 · 어휘판에 기대 문법",
                }, DevSceneKit.EffectLabScenePath, "F: 진입"),
                DevSceneKit.CreatePortal("Portal_SpellRange", Arc(-12f, 7f), SpawnGround, "사격장", new[]
                {
                    "과녁 6(cone IN 3·OUT 3 / 8~15m) — 광역 cone 판정·탄속 계측",
                    "노 정면 시전 → 집계 IN/OUT 일치 · 사/아 → D6 착탄 지연 비교",
                    "실적 1체는 선택대(F)로 깨운다 · 콘솔 [Range]",
                }, DevSceneKit.SpellRangeScenePath, "F: 진입"),
                DevSceneKit.CreatePortal("Portal_ParryRange", Arc(12f, 7f), SpawnGround, "패링장", new[]
                {
                    "원거리 적 5체(목·화·토·금·수) — 한 번에 하나만 깨워 패링 표본을 뽑는다",
                    "선택대 F로 깨움 → 텔레그래프 색을 보고 거·너·머·서·어 작도",
                    "판정판에 성공/반성공/실패/블록/피격 기록 · 정답표 게시",
                }, DevSceneKit.ParryRangeScenePath, "F: 진입"),
                DevSceneKit.CreatePortal("Portal_CombatLoop", Arc(36f, 7f), SpawnGround, "전투 루프", new[]
                {
                    "적 1체(화) 종합 루프 — 회피·패링·그로기 만개·갈무리·처치",
                    "SPEC-COMBAT-CORE-LOOP §6 실플레이 7종의 원본 씬(변경 없음)",
                    "귀환 포탈만 추가됨",
                }, DevSceneKit.CombatLoopScenePath, "F: 진입"),
            };
            DevSceneKit.CreateBoard("Board_Hub", new Vector3(-3.5f, 0f, -4f), SpawnGround, HubBoardText, 0.03f, 4.2f, 1.4f);

            DevSceneKit.FindRigParts(out var player, out _);
            DevSceneKit.AddInteractor(new GameObject("HubDirector"), player.transform, portals);
            return Save(scene, DevSceneKit.HubScenePath);
        }

        // ---- 이펙트 실험실 ----

        public static string BuildEffectLab()
        {
            var scene = DevSceneKit.NewEmptyScene();
            DevSceneKit.AddSceneBase();
            DevSceneKit.InstantiateRig(scene, DevSceneKit.Spawn, 0f);
            DevSceneKit.FindRigParts(out var player, out _);

            var l1 = DevSceneKit.CreateDummy("L1", new Vector3(0f, 1.1f, 0f), "L1");          // 정면 6m — 락온 대상
            var l2 = DevSceneKit.CreateDummy("L2", new Vector3(2.49f, 1.1f, 5.74f), "L2");    // 우 12° 12m — 유도·포물선·최속
            var l3 = DevSceneKit.CreateDummy("L3", new Vector3(-3.5f, 1.1f, 0.06f), "L3");    // 좌 30° 7m — cone 2체째
            var dummies = new List<SpellRangeDummy> { l1.GetComponent<SpellRangeDummy>(), l2.GetComponent<SpellRangeDummy>(), l3.GetComponent<SpellRangeDummy>() };
            var vitals = new List<EnemyVitals> { l1.GetComponent<EnemyVitals>(), l2.GetComponent<EnemyVitals>(), l3.GetComponent<EnemyVitals>() };
            DevSceneKit.WireRigSeam(null, vitals[0], vitals);

            var board = DevSceneKit.CreateBoard("Board_Lexicon", new Vector3(-5.5f, 0f, -5f), SpawnGround, "어휘판", 0.03f, 4.6f, 3.0f);
            var lexicon = board.gameObject.AddComponent<EffectLabBoard>();
            var lexiconSo = new SerializedObject(lexicon);
            lexiconSo.FindProperty("_spellBook").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SpellBookSO>(DevSceneKit.SpellBookPath);
            lexiconSo.FindProperty("_config").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DevSceneKit.DefaultConfigPath);
            lexiconSo.FindProperty("_board").objectReferenceValue = board;
            string[,] rows =
            {
                { "가", "뻗는 생목 가시 랜스" }, { "나", "표준 화염탄" }, { "마", "포물선 바위" }, { "사", "최속 송곳" }, { "아", "느린 유도 물방울" },
                { "고", "가시 일제 솟음" }, { "노", "화염 방사" }, { "모", "모래폭풍 직선" }, { "소", "다연발 송곳" }, { "오", "전진 파도" },
            };
            var rowsProp = lexiconSo.FindProperty("_rows");
            rowsProp.arraySize = rows.GetLength(0);
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                var element = rowsProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Letter").stringValue = rows[i, 0];
                element.FindPropertyRelative("Grammar").stringValue = rows[i, 1];
            }
            lexiconSo.ApplyModifiedPropertiesWithoutUndo();

            var directorGo = new GameObject("RangeDirector");
            var director = directorGo.AddComponent<SpellRangeDirector>();
            var directorSo = new SerializedObject(director);
            directorSo.FindProperty("_letterDrawn").objectReferenceValue = DevSceneKit.LoadByGuid<DrawnLetterEventChannelSO>(DevSceneKit.LetterChannelGuid);
            directorSo.FindProperty("_spellBook").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SpellBookSO>(DevSceneKit.SpellBookPath);
            directorSo.FindProperty("_config").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DevSceneKit.DefaultConfigPath);
            directorSo.FindProperty("_player").objectReferenceValue = player.transform;
            DevSceneKit.SetObjectArray(directorSo, "_dummies", dummies);
            directorSo.ApplyModifiedPropertiesWithoutUndo();

            var returnPortal = DevSceneKit.AddReturnPortal();
            DevSceneKit.AddInteractor(directorGo, player.transform, new List<DevInteractable> { returnPortal });
            return Save(scene, DevSceneKit.EffectLabScenePath);
        }

        // ---- 패링장 ----

        public static string BuildParryRange()
        {
            var scene = DevSceneKit.NewEmptyScene();
            DevSceneKit.AddSceneBase();
            DevSceneKit.InstantiateRig(scene, DevSceneKit.Spawn, 0f);
            DevSceneKit.FindRigParts(out var player, out var systems);
            var playerVitals = player.GetComponent<PlayerVitals>();
            // 씬 생성 뒤에 얻는다 — 새 씬 전환이 미참조 에셋을 언로드해 먼저 얻은 참조가 죽은 객체(fileID 0)가 된다
            var config = DevSceneKit.EnsureParryConfig();

            var controllers = new List<EnemyController>();
            var vitals = new List<EnemyVitals>();
            var pedestals = new List<DevEnemyWakePedestal>();
            for (int i = 0; i < ParryOrder.Length; i++)
            {
                var element = ParryOrder[i];
                string elementName = DevElement.Name(element);
                // 적: 8m 호(원거리 패턴 3m<d≤12m), 잠든 채 시작
                var enemy = DevSceneKit.CreateEnemy($"E_{elementName}", Arc(-40f + 20f * i, 8f) + Vector3.up * 1.1f,
                    config, element, player.transform, playerVitals, false);
                var controller = enemy.GetComponent<EnemyController>();
                controllers.Add(controller);
                vitals.Add(enemy.GetComponent<EnemyVitals>());

                // 선택대: 시작점 앞 1.5m 한 줄 — 깨우면서 8m 사격 위치 유지
                Element success = element, fail = element;
                foreach (var guard in ParryOrder)
                {
                    if (ElementRelations.Overcomes(guard, element)) success = guard;
                    else if (ElementRelations.Generates(guard, element)) fail = guard;
                }
                pedestals.Add(DevSceneKit.CreatePedestal($"P_{elementName}", new Vector3(-4f + 2f * i, 0f, -4.5f), SpawnGround,
                    $"{elementName}({DevElement.Initial(element)})",
                    new[] { $"{elementName} 원거리 적 — 정답 {DevElement.GuardLetter(success)}({DevElement.Name(success)}) · 실패 {DevElement.GuardLetter(fail)}({DevElement.Name(fail)})" },
                    controller, false, DevSceneKit.ElementMaterial(element)));
            }
            int center = ParryOrder.Length / 2; // 토 — 단일 배선 대상(패링은 락온 불요·락온 후보는 전수)
            DevSceneKit.WireRigSeam(controllers[center], vitals[center], vitals);

            var answerBoard = DevSceneKit.CreateBoard("Board_Answer", new Vector3(-6.5f, 0f, -3f), SpawnGround, "정답표", 0.03f, 4.6f, 2.6f);
            var outcomeBoard = DevSceneKit.CreateBoard("Board_Outcome", new Vector3(6.5f, 0f, -3f), SpawnGround, "판정판", 0.03f, 4.6f, 2.6f);

            var directorGo = new GameObject("ParryDirector");
            var director = directorGo.AddComponent<ParryRangeDirector>();
            var directorSo = new SerializedObject(director);
            DevSceneKit.SetObjectArray(directorSo, "_pedestals", pedestals);
            directorSo.FindProperty("_wiring").objectReferenceValue = systems.GetComponent<CombatLoopWiring>();
            directorSo.FindProperty("_playerVitals").objectReferenceValue = playerVitals;
            directorSo.FindProperty("_config").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DevSceneKit.DefaultConfigPath);
            directorSo.FindProperty("_palette").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ElementPaletteSO>(DevSceneKit.PalettePath);
            directorSo.FindProperty("_answerBoard").objectReferenceValue = answerBoard;
            directorSo.FindProperty("_outcomeBoard").objectReferenceValue = outcomeBoard;
            directorSo.ApplyModifiedPropertiesWithoutUndo();

            var interactables = new List<DevInteractable>(pedestals) { DevSceneKit.AddReturnPortal() };
            DevSceneKit.AddInteractor(directorGo, player.transform, interactables);
            return Save(scene, DevSceneKit.ParryRangeScenePath);
        }

        // ---- 기존 씬 편입 ----

        public static string AddCombatLoopReturn()
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene(DevSceneKit.CombatLoopScenePath, OpenSceneMode.Single);
            DevSceneKit.DestroyByName("Portal_Return", "DevHarness");
            if (!DevSceneKit.FindRigParts(out var player, out _)) return "리그 없음";
            DevSceneKit.PruneRigOverrides(player);
            var portal = DevSceneKit.AddReturnPortal();
            DevSceneKit.AddInteractor(new GameObject("DevHarness"), player.transform, new List<DevInteractable> { portal });
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"C1_CombatLoop 귀환 포탈 추가 · {DevSceneKit.AssertSingleRig()} · {PlayerRigPrefabTool.CheckOverrides()}";
        }

        public static string RegisterScenes()
        {
            DevSceneKit.RegisterBuildScenes(DevSceneKit.HubScenePath, DevSceneKit.EffectLabScenePath, DevSceneKit.ParryRangeScenePath,
                DevSceneKit.SpellRangeScenePath, DevSceneKit.CombatLoopScenePath);
            return $"Build Settings 등록 — {EditorBuildSettings.scenes.Length}개(허브 첫 번째)";
        }

        // ---- 보조 ----

        private static Vector3 Arc(float angleDeg, float radius)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return SpawnGround + new Vector3(Mathf.Sin(rad) * radius, 0f, Mathf.Cos(rad) * radius);
        }

        private static string Save(Scene scene, string path)
        {
            bool saved = EditorSceneManager.SaveScene(scene, path);
            return $"{System.IO.Path.GetFileNameWithoutExtension(path)} 저장 {saved} · {DevSceneKit.AssertSingleRig()} · {PlayerRigPrefabTool.CheckOverrides()}";
        }
    }
}
