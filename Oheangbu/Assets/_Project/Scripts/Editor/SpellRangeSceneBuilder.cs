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
    // [SPEC-DEV-SPELL-RANGE §2] 사격장 배치 빌더 — C1_SpellRange(C1_CombatLoop 복제본)에 과녁 6·실적 위치·
    // 배선·디렉터를 결정적으로 구성한다. 재실행 가능(기존 과녁·디렉터를 지우고 다시 만든다).
    // 배치표는 이 파일이 단일 출처 — Spec §2 표와 같이 움직인다.
    public static class SpellRangeSceneBuilder
    {
        private const string SceneName = "C1_SpellRange";
        private const string DummyConfigPath = "Assets/_Project/Data/Configs/CombatConfig_RangeDummy.asset";
        private const string DefaultConfigPath = "Assets/_Project/Data/Configs/CombatConfig_Default.asset";
        private const string SpellBookPath = "Assets/_Project/Data/Configs/SpellBook_Proto.asset";

        private static readonly Vector3 Origin = new Vector3(0f, 1.1f, -6f); // 플레이어 시작점(캡슐 중심)
        private static readonly Vector3 LiveEnemyPosition = new Vector3(-7f, 1.1f, -3f);

        private struct DummySpec
        {
            public string Name;
            public float Angle; // 시작점 전방(+Z) 기준 좌(−)/우(+) 도
            public float Distance;
        }

        private static readonly DummySpec[] Dummies =
        {
            new DummySpec { Name = "D1", Angle = 0f, Distance = 6f },
            new DummySpec { Name = "D2", Angle = -30f, Distance = 7f },
            new DummySpec { Name = "D3", Angle = 30f, Distance = 7f },
            new DummySpec { Name = "D4", Angle = -55f, Distance = 7f },
            new DummySpec { Name = "D5", Angle = 45f, Distance = 6f },
            new DummySpec { Name = "D6", Angle = 15f, Distance = 12f },
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
            GameObject player = GameObject.Find("Player");
            GameObject systems = GameObject.Find("CombatSystems");
            if (enemy == null || player == null || systems == null) return "루트(Enemy/Player/CombatSystems) 누락";

            var dummyConfig = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DummyConfigPath);
            var defaultConfig = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DefaultConfigPath);
            var spellBook = AssetDatabase.LoadAssetAtPath<SpellBookSO>(SpellBookPath);
            if (dummyConfig == null || defaultConfig == null || spellBook == null) return "설정 에셋 누락";

            var wiring = systems.GetComponent<CombatLoopWiring>();
            if (wiring == null) return "CombatLoopWiring 누락";
            var wiringSo = new SerializedObject(wiring);
            var letterChannel = wiringSo.FindProperty("_letterDrawn").objectReferenceValue;

            // 재실행: 이전 산출물 제거
            foreach (var spec in Dummies)
            {
                var old = GameObject.Find(spec.Name);
                if (old != null) Object.DestroyImmediate(old);
            }
            var oldDirector = GameObject.Find("RangeDirector");
            if (oldDirector != null) Object.DestroyImmediate(oldDirector);

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
                var vitalsSo = new SerializedObject(vitals);
                vitalsSo.FindProperty("_config").objectReferenceValue = dummyConfig;
                vitalsSo.ApplyModifiedPropertiesWithoutUndo();

                var dummy = go.AddComponent<SpellRangeDummy>();
                var dummySo = new SerializedObject(dummy);
                dummySo.FindProperty("_vitals").objectReferenceValue = vitals;
                dummySo.FindProperty("_renderer").objectReferenceValue = go.GetComponent<Renderer>();
                dummySo.FindProperty("_config").objectReferenceValue = dummyConfig;
                dummySo.ApplyModifiedPropertiesWithoutUndo();

                dummies.Add(dummy);
                enemies.Add(vitals);
            }
            enemies.Add(enemy.GetComponent<EnemyVitals>());

            var enemiesProp = wiringSo.FindProperty("_enemies");
            enemiesProp.arraySize = enemies.Count;
            for (int i = 0; i < enemies.Count; i++)
            {
                enemiesProp.GetArrayElementAtIndex(i).objectReferenceValue = enemies[i];
            }
            wiringSo.ApplyModifiedPropertiesWithoutUndo();

            var directorGo = new GameObject("RangeDirector");
            var director = directorGo.AddComponent<SpellRangeDirector>();
            var directorSo = new SerializedObject(director);
            directorSo.FindProperty("_letterDrawn").objectReferenceValue = letterChannel;
            directorSo.FindProperty("_spellBook").objectReferenceValue = spellBook;
            directorSo.FindProperty("_config").objectReferenceValue = defaultConfig;
            directorSo.FindProperty("_player").objectReferenceValue = player.transform;
            var dummiesProp = directorSo.FindProperty("_dummies");
            dummiesProp.arraySize = dummies.Count;
            for (int i = 0; i < dummies.Count; i++)
            {
                dummiesProp.GetArrayElementAtIndex(i).objectReferenceValue = dummies[i];
            }
            directorSo.FindProperty("_liveEnemy").objectReferenceValue = enemy.GetComponent<EnemyController>();
            directorSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            return $"완료 — 과녁 {dummies.Count}·_enemies {enemies.Count}·저장 {saved}";
        }

        // 가상 시전(검증 보조·에디터 전용) — 작도 없이 글자 채널을 올린다. 원자료는 중간값(형 1.0·세 2.0s).
        // 인식 계층을 우회하는 것이지 대체하는 것이 아니다 — 판정·표현 계층의 원격 검증에만 쓴다.
        private const string LetterChannelGuid = "c5b5038e37bdeff4abbb1ae079f460fb";

        public static string FireLetter(string letter)
        {
            if (!Application.isPlaying) return "플레이 모드 아님";
            if (string.IsNullOrEmpty(letter)) return "글자 없음";
            var channel = AssetDatabase.LoadAssetAtPath<DrawnLetterEventChannelSO>(
                AssetDatabase.GUIDToAssetPath(LetterChannelGuid));
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
