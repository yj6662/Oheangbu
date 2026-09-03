using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Oheangbu.App
{
    // [SPEC-DEV-TEST-HUB] 개발 씬 전환 — 포탈 진입·귀환·재시작이 전부 여기를 지난다.
    // 작도 감속(timeScale 0.35)·커서 상태를 씬 경계에서 초기화한다(다음 씬의 PlayerMotor가 다시 잠근다).
    // 에디터=LoadSceneInPlayMode(Build Settings 무관) / 빌드=SceneManager(개발 씬 등재 전제 — Spec §9-2)
    public static class DevSceneFlow
    {
        public static void Load(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
            {
                Debug.LogWarning("[Hub] 대상 씬 미지정");
                return;
            }
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(scenePath, LoadSceneMode.Single);
#endif
        }

        public static void Restart()
        {
            Load(SceneManager.GetActiveScene().path);
        }
    }
}
