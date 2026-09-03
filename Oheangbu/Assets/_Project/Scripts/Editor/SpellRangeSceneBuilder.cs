using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    // [SPEC-DEV-SPELL-RANGE §2] 사격장 배치 빌더 — C1_SpellRange에 과녁 6·실적 위치·배선·디렉터를 결정적으로 구성한다.
    // 재실행 가능(기존 과녁·디렉터·선택대·귀환 포탈을 지우고 다시 만든다). 배치표는 이 파일이 단일 출처 — Spec §2 표와 같이 움직인다.
    // 리그가 PlayerRig 프리팹 인스턴스여도 그대로 동작한다(TEST-HUB §3 — _enemies는 인스턴스 오버라이드).
    public static class SpellRangeSceneBuilder
    {
        private const string SceneName = "C1_SpellRange";
        private const string DummyConfigPath = DevSceneKit.RangeDummyConfigPath;
        private const string DefaultConfigPath = DevSceneKit.DefaultConfigPath;
        private const string SpellBookPath = DevSceneKit.SpellBookPath;

        private static readonly Vector3 Origin = DevSceneKit.Spawn; // 플레이어 시작점(캡슐 중심)
        private static readonly Vector3 LiveEnemyPosition = new Vector3(-7f, 1.1f, -3f);
        private static readonly Vector3 PedestalPosition = new Vector3(-4f, 0f, -6.5f);

        private struct DummySpec
        {
            public string Name;
            public float Angle; // 시작점 전방(+Z) 기준 좌(−)/우(+) 도
            public float Distance;
        }

        // 2026-09-03 재배치(예준: 간격 벌리기 + 멀리) — cone 40°/10m 기준 IN 3(D1·D2·D3) / OUT 3(D4·D5=각 밖, D6=사거리 밖)
        private static readonly DummySpec[] Dummies =
        {
            new DummySpec { Name = "D1", Angle = 0f, Distance = 8f },
            new DummySpec { Name = "D2", Angle = -30f, Distance = 9f },
            new DummySpec { Name = "D3", Angle = 30f, Distance = 9f },
            new DummySpec { Name = "D4", Angle = -60f, Distance = 8f },
            new DummySpec { Name = "D5", Angle = 60f, Distance = 8f },
            new DummySpec { Name = "D6", Angle = 20f, Distance = 15f },
        };

        [MenuItem("Oheangbu/Dev/사격장 배치 재구성 (C1_SpellRange)")]
        private static void BuildFromMenu()
        {
            Debug.Log("[SpellRangeSceneBuilder] " + Build());
        }

        public static string Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName) return $"활성 씬이 {SceneName}이 아님: {scene.name}";

            GameObject enemy = GameObject.Find("Enemy");
            if (!DevSceneKit.FindRigParts(out var player, out var systems) || enemy == null) return "루트(Enemy/Player/CombatSystems) 누락";

            var dummyConfig = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DummyConfigPath);
            var defaultConfig = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DefaultConfigPath);
            var spellBook = AssetDatabase.LoadAssetAtPath<SpellBookSO>(SpellBookPath);
            if (dummyConfig == null || defaultConfig == null || spellBook == null) return "설정 에셋 누락";

            var wiring = systems.GetComponent<CombatLoopWiring>();
            if (wiring == null) return "CombatLoopWiring 누락";
            var wiringSo = new SerializedObject(wiring);
            var letterChannel = wiringSo.FindProperty("_letterDrawn").objectReferenceValue;

            // 재실행: 이전 산출물 제거
            foreach (var spec in Dummies) DevSceneKit.DestroyByName(spec.Name);
            DevSceneKit.DestroyByName("RangeDirector", "Pedestal_Live", "Portal_Return");

            enemy.transform.position = LiveEnemyPosition;

            var dummies = new List<SpellRangeDummy>();
            var enemies = new List<EnemyVitals>();
            foreach (var spec in Dummies)
            {
                var go = Object.Instantiate(enemy);
                go.name = spec.Name;
                Vector3 dir = Quaternion.AngleAxis(spec.Angle, Vector3.up) * Vector3.forward;
                go.transform.SetPositionAndRotation(Origin + dir * spec.Distance, Quaternion.identity);

                var controller = go.GetComponent<EnemyController>();
                if (controller != null) Object.DestroyImmediate(controller); // 과녁은 반격하지 않는다

                var vitals = go.GetComponent<EnemyVitals>();
                DevSceneKit.SetRef(vitals, "_config", dummyConfig);

                var dummy = go.AddComponent<SpellRangeDummy>();
                var dummySo = new SerializedObject(dummy);
                dummySo.FindProperty("_vitals").objectReferenceValue = vitals;
                dummySo.FindProperty("_renderer").objectReferenceValue = go.GetComponent<Renderer>();
                dummySo.FindProperty("_config").objectReferenceValue = dummyConfig;
                dummySo.FindProperty("_caption").stringValue = spec.Name;
                dummySo.ApplyModifiedPropertiesWithoutUndo();

                dummies.Add(dummy);
                enemies.Add(vitals);
            }
            enemies.Add(enemy.GetComponent<EnemyVitals>());

            wiringSo = new SerializedObject(wiring);
            DevSceneKit.SetObjectArray(wiringSo, "_enemies", enemies);
            wiringSo.ApplyModifiedPropertiesWithoutUndo();

            var directorGo = new GameObject("RangeDirector");
            var director = directorGo.AddComponent<SpellRangeDirector>();
            var directorSo = new SerializedObject(director);
            directorSo.FindProperty("_letterDrawn").objectReferenceValue = letterChannel;
            directorSo.FindProperty("_spellBook").objectReferenceValue = spellBook;
            directorSo.FindProperty("_config").objectReferenceValue = defaultConfig;
            directorSo.FindProperty("_player").objectReferenceValue = player.transform;
            DevSceneKit.SetObjectArray(directorSo, "_dummies", dummies);
            directorSo.ApplyModifiedPropertiesWithoutUndo();

            // TEST-HUB 편입 — 실적 선택대·귀환 포탈·인터랙터(F/R 단일 지점)
            var liveController = enemy.GetComponent<EnemyController>();
            var pedestal = DevSceneKit.CreatePedestal("Pedestal_Live", PedestalPosition, new Vector3(Origin.x, 0f, Origin.z),
                "실적 선택대", new[] { "실적(화) AI — 패링·갈무리·그로기 검증 때만 깨운다" },
                liveController, false, DevSceneKit.ElementMaterial(liveController != null ? liveController.RangedElement : Element.Fire));
            var returnPortal = DevSceneKit.AddReturnPortal();
            DevSceneKit.AddInteractor(directorGo, player.transform, new List<DevInteractable> { pedestal, returnPortal });
            DevSceneKit.PruneRigOverrides(player);

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            return $"완료 — 과녁 {dummies.Count}·_enemies {enemies.Count}·저장 {saved}";
        }

        // 가상 시전(검증 보조·에디터 전용) — 작도 없이 글자 채널을 올린다. 원자료는 중간값(형 1.0·세 2.0s).
        // 인식 계층을 우회하는 것이지 대체하는 것이 아니다 — 판정·표현 계층의 원격 검증에만 쓴다.
        public static string FireLetter(string letter)
        {
            if (!Application.isPlaying) return "플레이 모드 아님";
            if (string.IsNullOrEmpty(letter)) return "글자 없음";
            var channel = DevSceneKit.LoadByGuid<DrawnLetterEventChannelSO>(DevSceneKit.LetterChannelGuid);
            if (channel == null) return "글자 채널 없음";

            char c = letter[0];
            int code = c - 0xAC00;
            if (code < 0 || code >= 11172) return "한글 음절 아님";
            Jamo? initial = MapInitial(code / 588);
            Jamo? medial = MapMedial(code % 588 / 28);
            if (initial == null || medial == null) return "인식 자모 9종 밖";
            Jamo? final = MapFinal(code % 28);

            channel.Raise(new DrawnLetter(c, initial.Value, medial.Value, final, 1.0f, 1.0f, 2.0f, 2.5f, 3));
            return $"가상 시전 '{c}'";
        }

        // 시작 자세 리셋 + 가상 시전(같은 프레임) — 원격 검증의 결정성: 마우스 룩 드리프트를 배제한다.
        // 피치는 PlayerMotor 비직렬 필드라 리플렉션으로 0을 쓴다(에디터 검증 보조 한정)
        public static string FireLetterFromPose(string letter, float yaw)
        {
            if (!Application.isPlaying) return "플레이 모드 아님";
            var player = GameObject.Find("Player");
            if (player == null) return "Player 없음";
            player.transform.SetPositionAndRotation(Origin, Quaternion.Euler(0f, yaw, 0f));
            var motor = player.GetComponent<PlayerMotor>();
            if (motor != null)
            {
                var pitch = typeof(PlayerMotor).GetField("_pitch", BindingFlags.Instance | BindingFlags.NonPublic);
                pitch?.SetValue(motor, 0f);
            }
            Physics.SyncTransforms();
            return FireLetter(letter) + $" @ yaw {yaw:0}°";
        }

        // 플레이 중 플레이어 이동(검증 보조) — 근접 표시·상호작용 반경 검증용. 캡슐 중심 높이 고정·피치 0
        public static string MovePlayer(float x, float z, float yaw)
        {
            if (!Application.isPlaying) return "플레이 모드 아님";
            var player = GameObject.Find("Player");
            if (player == null) return "Player 없음";
            player.transform.SetPositionAndRotation(new Vector3(x, Origin.y, z), Quaternion.Euler(0f, yaw, 0f));
            var motor = player.GetComponent<PlayerMotor>();
            if (motor != null)
            {
                var pitch = typeof(PlayerMotor).GetField("_pitch", BindingFlags.Instance | BindingFlags.NonPublic);
                pitch?.SetValue(motor, 0f);
            }
            Physics.SyncTransforms();
            return $"이동 ({x:0.##}, {z:0.##}) yaw {yaw:0}°";
        }

        // 플레이 중 락온 토글(검증 보조) — 현재 자세에서 LockOn 선정 결과를 이름으로 돌려준다
        public static string ToggleLockOn()
        {
            if (!Application.isPlaying) return "플레이 모드 아님";
            var player = GameObject.Find("Player");
            var lockOn = player != null ? player.GetComponent<LockOn>() : null;
            if (lockOn == null) return "LockOn 없음";
            lockOn.Toggle();
            return lockOn.Target != null ? $"락온: {lockOn.Target.name}" : "락온 없음(해제 또는 후보 없음)";
        }

        // 플레이 중 상호작용 원격 실행(검증 보조) — 이름으로 찾은 상호작용 대상의 Interact()
        public static string InteractWith(string objectName)
        {
            if (!Application.isPlaying) return "플레이 모드 아님";
            var go = GameObject.Find(objectName);
            var interactable = go != null ? go.GetComponent<DevInteractable>() : null;
            if (interactable == null) return $"상호작용 대상 없음: {objectName}";
            interactable.Interact();
            return $"상호작용 실행: {objectName}";
        }

        private static Jamo? MapInitial(int index)
        {
            switch (index)
            {
                case 0: return Jamo.Giyeok;
                case 2: return Jamo.Nieun;
                case 6: return Jamo.Mieum;
                case 9: return Jamo.Siot;
                case 11: return Jamo.Ieung;
                default: return null;
            }
        }

        private static Jamo? MapMedial(int index)
        {
            switch (index)
            {
                case 0: return Jamo.A;
                case 4: return Jamo.Eo;
                case 8: return Jamo.O;
                case 13: return Jamo.U;
                default: return null;
            }
        }

        private static Jamo? MapFinal(int index)
        {
            switch (index)
            {
                case 1: return Jamo.Giyeok;
                case 4: return Jamo.Nieun;
                case 16: return Jamo.Mieum;
                case 19: return Jamo.Siot;
                case 21: return Jamo.Ieung;
                default: return null;
            }
        }
    }
}
