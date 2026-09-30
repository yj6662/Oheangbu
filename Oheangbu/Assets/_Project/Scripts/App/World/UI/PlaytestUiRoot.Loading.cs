using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.World.UI
{
    public sealed partial class PlaytestUiRoot
    {
        // #297: one quiet diegetic line on the loading screen (technical stage names are not player-facing)
        const string LoadingLine297 = "먹을 가는 중";
        public WorldLoadingProfile270 LoadingProfile;
        public bool LoadingInProgress { get; private set; }

        static AsyncOperation BeginLoadingScene(string name, LoadSceneMode mode, out string error)
        {
            error = null;
            try { return SceneManager.LoadSceneAsync(name, mode); }
            catch (Exception exception) { error = exception.Message; return null; }
        }

        IEnumerator LoadWithRegionScreen()
        {
            if (!Application.CanStreamedLevelBeLoaded(PlaySceneName) || !Application.CanStreamedLevelBeLoaded(LoadingProfile.LoadingScene))
            { LastError = "로딩 또는 플레이 씬 연결을 확인해 주세요."; Menu304ShowNotice(LastError,UiNoticeKind304.Error,8f); yield break; }
            Vector3 position = Content.StartFeet;
            var saved = WorldMacroSaveSlot.Inspect(Application.persistentDataPath, ActiveSlotName).Progress;
            if (saved != null && saved.ledger.hasPosition && saved.terrainRevision == Content.TerrainRevision) position = saved.ledger.position;
            Busy = true; LoadingInProgress = true; Gate.Block(); DismissConfirmation(); Settings.Revert();
            WorldLoadingScreen270.RequestedPosition = position;
            Time.timeScale = 0;
            var load = BeginLoadingScene(LoadingProfile.LoadingScene, LoadSceneMode.Single, out var error);
            if (load == null) { LoadingInProgress = false; Busy = false; Time.timeScale = 1; Menu304ShowNotice(error,UiNoticeKind304.Error,8f); yield break; }
            while (!load.isDone) yield return null;
            var screen = FindFirstObjectByType<WorldLoadingScreen270>();
            if (screen == null)
            {
                // Keep a recoverable overlay even if someone removed the loading-scene component.
                var host = new GameObject("RecoveredLoadingScreen"); screen = host.AddComponent<WorldLoadingScreen270>(); screen.Profile = LoadingProfile; screen.Build();
            }
            // #304 loading band: region name (from the map data) + where the journey resumes
            screen.SetJourney(Flow304JourneyMeta(saved)); screen.Select(position); screen.SetProgress(.04f, LoadingLine297);
            yield return null; // Present the illustration before requesting the large world scene.
            load = BeginLoadingScene(PlaySceneName, LoadSceneMode.Additive, out error);
            if (load == null) { LoadingFailed(screen, "지역을 불러오지 못했습니다."); yield break; }
            load.allowSceneActivation = false;
            while (load.progress < .9f) { screen.SetProgress(.04f + .62f * load.progress / .9f, LoadingLine297); yield return null; }
            screen.SetProgress(.68f, LoadingLine297); load.allowSceneActivation = true;
            while (!load.isDone) yield return null;
            var world = SceneManager.GetSceneByName(PlaySceneName);
            if (!world.IsValid() || !world.isLoaded) { LoadingFailed(screen, "플레이 씬을 확인하지 못했습니다."); yield break; }
            SceneManager.SetActiveScene(world);
            double deadline = Time.realtimeSinceStartupAsDouble + LoadingProfile.InitializationTimeout;
            WorldMacroPlaytestSession loaded = null;
            while (loaded == null || !loaded.InitializationComplete)
            {
                loaded = FindFirstObjectByType<WorldMacroPlaytestSession>();
                Gate.Block();
                if (Time.realtimeSinceStartupAsDouble > deadline) { LoadingFailed(screen, "캐릭터·세계 초기화를 완료하지 못했습니다."); yield break; }
                yield return null;
            }
            screen.Select(loaded.Progress.ledger.position);
            // Build the actual HUD/map while input and gameplay time remain blocked.
            Busy = false; bound = false; currentSceneHandle = -1;
            try { BindScene(); } catch (Exception exception) { error = exception.Message; }
            Busy = true; Gate.Block();
            if (error != null || !bound) { LoadingFailed(screen, "화면 구성을 완료하지 못했습니다."); yield break; }
            var loadingScene = screen.gameObject.scene;
            SceneManager.MoveGameObjectToScene(screen.gameObject, world);
            if (loadingScene != world) { var unload = SceneManager.UnloadSceneAsync(loadingScene); if (unload != null) yield return unload; }
            screen.ObservedCamera = loaded.Walker.ViewCamera;
            if (screen.ObservedCamera == null || !screen.ObservedCamera.isActiveAndEnabled)
            { LoadingFailed(screen, "플레이 카메라가 준비되지 않았습니다."); yield break; }
            screen.SetProgress(.78f, LoadingLine297);
            var readiness = new WorldLoadingReadiness270();
            int previousRender = screen.RenderedFrames;
            bool focused = Application.isFocused;
            deadline = Time.realtimeSinceStartupAsDouble + LoadingProfile.WarmupTimeout;
            while (!readiness.Ready)
            {
                yield return null;
                if (!Application.isFocused || !focused)
                {
                    readiness.Sample(0, false, false);
                    focused = Application.isFocused;
                    deadline = Time.realtimeSinceStartupAsDouble + LoadingProfile.WarmupTimeout;
                    previousRender = screen.RenderedFrames;
                    screen.SetProgress(.78f, LoadingLine297);
                    continue;
                }
                bool textures = Texture.streamingTexturePendingLoadCount == 0 && Texture.streamingTextureLoadingCount == 0;
                readiness.Sample(Time.unscaledDeltaTime, screen.RenderedFrames > previousRender, textures);
                previousRender = screen.RenderedFrames;
                screen.SetProgress(.78f + .20f * readiness.Progress, LoadingLine297);
                if (Time.realtimeSinceStartupAsDouble > deadline)
                { LoadingFailed(screen, "그래픽 준비가 지연되고 있습니다."); yield break; }
            }
            screen.SetProgress(1, LoadingLine297); yield return null;
            for (float t = 0; t < .35f; t += Time.unscaledDeltaTime)
            { screen.Fade.alpha = 1 - t / .35f; yield return null; }
            Destroy(screen.gameObject);
            LoadingInProgress = false; Busy = false; Time.timeScale = 1;
            Gate.ReleaseWhenNeutral(); Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            TryShowOpeningIntroduction();
        }

        /// <summary>#304 loading.png meta after the region name: "&lt;checkpoint&gt;에서 이어서" for a saved journey, "새 여정" for a new one.</summary>
        string Flow304JourneyMeta(WorldMacroProgress saved)
        {
            if (saved == null || saved.ledger == null) return "새 여정";
            string place = WorldMacroCheckpointRules.Label(Content, saved, saved.ledger.checkpoint);
            return string.IsNullOrWhiteSpace(place) ? "" : place + "에서 이어서";
        }

        void LoadingFailed(WorldLoadingScreen270 screen, string message)
        {
            LastError = message; Busy = true; LoadingInProgress = true; Time.timeScale = 0; Gate.Block();
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            screen.Fail(message,
                () => { Busy = false; StartCoroutine(LoadWithRegionScreen()); },
                () => StartCoroutine(LoadingBackToLobby()));
        }

        IEnumerator LoadingBackToLobby()
        {
            var load = BeginLoadingScene(TitleSceneName, LoadSceneMode.Single, out var error);
            if (load == null) { LastError = error; yield break; }
            UnhookSession();
            while (!load.isDone) yield return null;
            LoadingInProgress = false; Busy = false; bound = false; currentSceneHandle = -1; Time.timeScale = 1;
        }
    }
}
