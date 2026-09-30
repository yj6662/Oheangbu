using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    [InitializeOnLoad]
    public static class CompactLoadingStartup270
    {
        public const string Assets = "Assets/_Project/Art/UI/Loading270";
        public const string Lobby = Assets + "/W_Compact_Lobby.unity";
        public const string Loading = Assets + "/W_Compact_Loading.unity";
        static CompactLoadingStartup270() { EditorApplication.delayCall += ApplyDefault; }
        public static void ApplyDefault()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !SessionState.GetBool("Compact270.DirectPlay", false))
            {
                var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Lobby);
                if (scene != null) EditorSceneManager.playModeStartScene = scene;
            }
        }
        [MenuItem("Oheangbu/Play 시작/로비에서 시작 (기본)")]
        public static void UseLobby() { SessionState.SetBool("Compact270.DirectPlay", false); ApplyDefault(); }
        [MenuItem("Oheangbu/Play 시작/현재 씬에서 시작 (검사 도구용)")]
        public static void UseCurrent() { SessionState.SetBool("Compact270.DirectPlay", true); EditorSceneManager.playModeStartScene = null; }
    }

    public static partial class CompactRebuildAuthoring
    {
        public static string Loading270(string command)
        {
            if (command == "apply") return ApplyLoading270();
            if (command == "check") return CheckLoading270();
            throw new ArgumentException(command);
        }
        static string ApplyLoading270()
        {
            var candidate = FrontageScene249();
            if (candidate.isDirty) throw new Exception("Save the candidate before installing its lobby route.");
            var ui = candidate.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();
            const string folder = CompactLoadingStartup270.Assets;
            string[] names = { "cheongrim", "mine", "hwanggyeong", "jeokro", "cheolong", "hyeongang" };
            foreach (var name in names)
            {
                var path = folder + "/" + name + ".png";
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer == null) throw new Exception("Missing generated illustration: " + path);
                importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true;
                importer.mipmapEnabled = false; importer.streamingMipmaps = false; importer.isReadable = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 2048; importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            string profilePath = folder + "/RegionLoading.asset";
            var profile = AssetDatabase.LoadAssetAtPath<WorldLoadingProfile270>(profilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<WorldLoadingProfile270>(); AssetDatabase.CreateAsset(profile, profilePath); }
            profile.Map = ui.MapData; profile.Font = ui.Theme.Font;
            profile.Fallback = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/cheongrim.png");
            var images = names.Where(n => n != "mine").Select(n => new WorldLoadingProfile270.Illustration
            { LocationId = "realm_" + n, Image = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + n + ".png") }).ToList();
            foreach (var zone in ui.MapData.Zones ?? Array.Empty<WorldMapZoneSpec>())
                images.Add(new WorldLoadingProfile270.Illustration { LocationId = zone.Id, Image = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/mine.png") });
            profile.Illustrations = images.ToArray(); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
            const string lobbyPath = CompactLoadingStartup270.Lobby, loadingPath = CompactLoadingStartup270.Loading;
            if (!File.Exists(lobbyPath) && !AssetDatabase.CopyAsset("Assets/_Project/Scenes/World/W_Demo_Compact_Title.unity", lobbyPath)) throw new Exception("Lobby copy failed");
            var lobby = EditorSceneManager.OpenScene(lobbyPath, OpenSceneMode.Additive);
            try
            {
                var target = lobby.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();
                target.Content = ui.Content; target.WorldSheet = ui.WorldSheet; target.MapData = ui.MapData;
                target.Theme = ui.Theme; target.InputActions = ui.InputActions; target.RuntimeState = ui.RuntimeState;
                target.PlaySceneName = candidate.name; target.TitleSceneName = "W_Compact_Lobby"; target.LoadingProfile = profile;
                EditorUtility.SetDirty(target); EditorSceneManager.MarkSceneDirty(lobby); EditorSceneManager.SaveScene(lobby);
            }
            finally { EditorSceneManager.CloseScene(lobby, true); SceneManager.SetActiveScene(candidate); }
            var loading = File.Exists(loadingPath) ? EditorSceneManager.OpenScene(loadingPath, OpenSceneMode.Additive) : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var screen = loading.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldLoadingScreen270>(true)).FirstOrDefault();
                if (screen == null)
                {
                    var host = new GameObject("Region Loading"); SceneManager.MoveGameObjectToScene(host, loading); screen = host.AddComponent<WorldLoadingScreen270>();
                    var cameraObject = new GameObject("Loading Camera"); SceneManager.MoveGameObjectToScene(cameraObject, loading);
                    var camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.cullingMask = 0; camera.depth = -100;
                }
                screen.Profile = profile; EditorUtility.SetDirty(screen); EditorSceneManager.MarkSceneDirty(loading); EditorSceneManager.SaveScene(loading, loadingPath);
            }
            finally { EditorSceneManager.CloseScene(loading, true); SceneManager.SetActiveScene(candidate); }
            ui.LoadingProfile = profile; ui.TitleSceneName = "W_Compact_Lobby"; ui.PlaySceneName = candidate.name;
            EditorUtility.SetDirty(ui); EditorSceneManager.MarkSceneDirty(candidate); EditorSceneManager.SaveScene(candidate);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != lobbyPath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(lobbyPath, true));
            foreach (var path in new[] { loadingPath, candidate.path })
            {
                var existing = scenes.FirstOrDefault(s => s.path == path);
                if (existing == null) scenes.Add(new EditorBuildSettingsScene(path, true)); else existing.enabled = true;
            }
            EditorBuildSettings.scenes = scenes.ToArray(); CompactLoadingStartup270.UseLobby();
            return "Installed candidate lobby → region loading → " + candidate.name + "; Editor Play and build start at lobby; original compact title/content/save remain separate.";
        }

        static string CheckLoading270()
        {
            Directory.CreateDirectory(Output + "/Loading270");
            var checks = new List<string>();
            void Check(bool pass, string name) { checks.Add((pass ? "PASS " : "FAIL ") + name); if (!pass) throw new Exception(name); }
            void Skip(string name, string why) { checks.Add("SKIPPED " + name + " (" + why + ")"); }
            var candidate = FrontageScene249();
            var ui = candidate.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();
            var profile = ui.LoadingProfile; Check(profile != null && profile.Map == ui.MapData, "candidate loading profile uses actual location/map data");
            Check(EditorBuildSettings.scenes[0].path == CompactLoadingStartup270.Lobby && AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == CompactLoadingStartup270.Lobby, "build and Editor Play start in new lobby");
            Check(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == candidate.path) && EditorBuildSettings.scenes.Any(s => s.enabled && s.path == CompactLoadingStartup270.Loading), "loading and candidate world are loadable build scenes");
            var lobby = EditorSceneManager.OpenScene(CompactLoadingStartup270.Lobby, OpenSceneMode.Additive);
            try
            {
                var binding = lobby.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();
                Check(binding.Content == ui.Content && binding.PlaySceneName == candidate.name && binding.LoadingProfile == profile && binding.TitleSceneName == ui.TitleSceneName, "lobby uses candidate content, save slot, world and return route");
            }
            finally { EditorSceneManager.CloseScene(lobby, true); SceneManager.SetActiveScene(candidate); }
            foreach (var region in profile.Map.Locations.Entries.Where(e => e.Priority == 0))
            {
                var center = region.Polygon.Aggregate(Vector2.zero, (a, b) => a + b) / region.Polygon.Length;
                var p = new Vector3(center.x, 100, center.y);
                Check(profile.Select(p) == profile.Illustrations.Single(i => i.LocationId == region.Id).Image, "illustration resolves " + region.Name);
            }
            Check(profile.Select(ui.Content.StartFeet).name == "mine", "new game selects underground mine illustration");
            var ready = new WorldLoadingReadiness270();
            for (int i = 0; i < 100; i++) ready.Sample(1f / 60, false, true);
            Check(!ready.Ready, "no release without actual rendered camera frames");
            for (int i = 0; i < 100; i++) ready.Sample(1f / 60, true, false);
            Check(!ready.Ready, "pending texture loads prevent release");
            for (int i = 0; i < 15; i++) ready.Sample(1f / 60, true, true);
            ready.Sample(.4f, true, true); Check(!ready.Ready && ready.Progress == 0, "render hitch resets quiet period");
            for (int i = 0; i < 100; i++) ready.Sample(1f / 60, true, true);
            Check(ready.Ready, "rendered frames plus texture-ready stable interval allows release");
            ready.Sample(1f / 60, true, false); Check(!ready.Ready, "late texture demand revokes readiness");
            var preview = EditorSceneManager.NewPreviewScene();
            var host = new GameObject("Loading screen fixture"); SceneManager.MoveGameObjectToScene(host, preview);
            var screen = host.AddComponent<WorldLoadingScreen270>(); screen.Profile = profile;
            screen.Style304 = UiStyle304SO.Resolve(ui.Theme);   // #304: real TMP fonts in the fixture capture (no PlaytestUiRoot.Instance here)
            screen.Build();
            var cameraHost = new GameObject("Loading review camera"); SceneManager.MoveGameObjectToScene(cameraHost, preview); var camera = cameraHost.AddComponent<Camera>(); camera.scene = preview; camera.enabled = false; camera.cullingMask = 1 << 31;
            var canvas = screen.GetComponentInChildren<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            var previous = RenderTexture.active;
            try
            {
                foreach (var t in host.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                // #304 (IMPLEMENTATION §7.12): the rotating yin-yang is retired; the loading screen's liveness is its progress line.
                CheckProgressSignal304(screen, host, Check, Skip);
                screen.SetProgress(.7f, "배치 및 저장 복원 중"); screen.SetProgress(.4f, "텍스처 선명도 준비 중");
                Check(Mathf.Approximately(screen.Value, .7f) && screen.Stage == "텍스처 선명도 준비 중", "progress is monotonic while operation text updates");
                foreach (var sample in new[] { ("mine", 1920, 1080, ui.Content.StartFeet), ("cheongrim", 1920, 1080, new Vector3(3110, 100, 2250)), ("wide", 2560, 1080, new Vector3(3110, 100, 2250)), ("small", 1280, 720, ui.Content.StartFeet) })
                {
                    var rt = new RenderTexture(sample.Item2, sample.Item3, 24); var texture = new Texture2D(sample.Item2, sample.Item3, TextureFormat.RGB24, false);
                    try
                    {
                        camera.targetTexture = rt; camera.aspect = sample.Item2 / (float)sample.Item3; screen.Select(sample.Item4);
                        Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = rt; texture.ReadPixels(new Rect(0, 0, sample.Item2, sample.Item3), 0, 0); texture.Apply();
                        File.WriteAllBytes(Output + "/Loading270/" + sample.Item1 + ".png", texture.EncodeToPNG());
                        var imageRect = screen.Illustration.rectTransform.rect; var pageRect = ((RectTransform)screen.Illustration.transform.parent).rect;
                        Check(imageRect.width >= pageRect.width - 1 && imageRect.height >= pageRect.height - 1, "full-bleed illustration covers viewport " + sample.Item1);
                    }
                    finally { camera.targetTexture = null; RenderTexture.active = previous; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(texture); }
                }
                int retry = 0, back = 0; screen.Fail("초기화 지연", () => retry++, () => back++);
                var buttons = host.GetComponentsInChildren<Button>(); foreach (var button in buttons) button.onClick.Invoke();
                Check(retry == 1 && back == 1 && screen.Value < 1, "failure offers retry and lobby without claiming completed progress");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            File.WriteAllLines(Output + "/Loading270/checks.txt", checks);
            return string.Join("\n", checks) + "\nEdit-mode configuration/readiness fixtures + offscreen UI captures, not actual full-world load timing.";
        }

        /// <summary>#304 loading liveness (replaces "spinner rotates"): the public progress signal <c>Progress01</c> never falls,
        /// never claims more than the readiness value, and moves; the <c>ProgressLine304</c> child draws it; the yin-yang spinner is
        /// gone; a stalled load changes its stage line after 2 s when the screen exposes a public tick. Reached by name so this
        /// compiles before the flow rewrite lands: a missing member is SKIPPED, not FAIL. Leaves Value at most .6 so the
        /// legacy monotonic check that follows (.7 then .4 -> .7) still holds.</summary>
        static void CheckProgressSignal304(WorldLoadingScreen270 screen, GameObject host, Action<bool, string> check, Action<string, string> skip)
        {
            const string signal = "Progress01";
            string[] tickNames = { "Advance304", "AdvanceProgress304", "Tick304" };
            var tick = HarnessUiRules304.FindMethod(screen.GetType(), tickNames, new[] { typeof(float) });
            float Read() => HarnessUiRules304.TryFloat(screen, signal, out float p) ? p : float.NaN;
            void Tick(float seconds, int steps) { if (tick == null) return; for (int i = 0; i < steps; i++) tick.Invoke(screen, new object[] { seconds / steps }); }

            if (!HarnessUiRules304.HasMember(screen, signal))
            {
                skip("progress signal rises monotonically", "WorldLoadingScreen270." + signal + " is not exposed yet");
                skip("rotating spinner retired", "flow #304 loading screen not landed (no " + signal + ")");
            }
            else
            {
                float last = Read(), claimed = 0f; bool monotonic = !float.IsNaN(last) && last >= -1e-4f; var trace = new List<string> { last.ToString("0.00") };
                foreach (float v in new[] { .1f, .35f, .2f, .6f, .6f, .45f })
                {
                    screen.SetProgress(v, "단계 " + v.ToString("0.00")); Tick(.1f, 1);
                    float p = Read(); trace.Add(p.ToString("0.00"));
                    monotonic &= !float.IsNaN(p) && p >= last - 1e-4f && p <= screen.Value + 1e-4f && p <= 1f + 1e-4f;
                    last = Mathf.Max(last, p); claimed = Mathf.Max(claimed, p);
                }
                check(monotonic, "progress signal " + signal + " never falls and never exceeds readiness (" + string.Join(" ", trace) + ")");
                if (claimed <= 0f && tick != null) { Tick(1f, 10); claimed = Read(); }
                if (claimed > 0f) check(true, "progress signal moves with readiness (" + claimed.ToString("0.00") + " of " + screen.Value.ToString("0.00") + ")");
                else skip("progress signal moves with readiness", signal + " stays 0 without a frame and no public tick(float) exists");

                var spinner = HarnessUiRules304.Member(screen, "Spinner") as Component;
                bool yinYang = host.GetComponentsInChildren<Component>(true).Any(c => c != null && c.GetType().Name == "LoadingYinYang270");
                check(spinner == null && !yinYang, "rotating yin-yang spinner retired (progress line replaces it)");
            }

            // #304 integration: the flow screen names its line "ProgressLine" (FlowProgressLine304); either name counts
            Transform line = host.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "ProgressLine304")
                ?? host.GetComponentsInChildren<FlowProgressLine304>(true).Select(l => l.transform).FirstOrDefault();
            if (line == null) skip("ProgressLine304 draws the progress", "no child named ProgressLine304 yet");
            else
            {
                check(line.GetComponentsInChildren<Graphic>(true).Any(g => g.GetComponent<CanvasRenderer>() != null), "ProgressLine304 has a renderer");
                float shown = float.NaN; string how = null;
                var meter = line.GetComponentInChildren<InkMeter304>(true);
                if (meter != null) { shown = meter.Value01; how = "InkMeter304.Value01"; }
                else
                {
                    var graphic = line.GetComponentInChildren<InkMeterGraphic>(true);
                    if (graphic != null) { shown = graphic.Fill; how = "InkMeterGraphic.Fill"; }
                    else
                    {
                        var filled = line.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.type == Image.Type.Filled);
                        var flowLine = line.GetComponent<FlowProgressLine304>();
                        if (filled != null) { shown = filled.fillAmount; how = "Image.fillAmount"; }
                        else if (flowLine != null) { shown = flowLine.Value; how = "FlowProgressLine304.Value"; }
                    }
                }
                float p = Read();
                if (how == null || float.IsNaN(p)) skip("ProgressLine304 shows the signal", how == null ? "no InkMeter304 / InkMeterGraphic / filled Image to measure" : "no " + signal);
                else check(Mathf.Abs(shown - p) <= .02f, "ProgressLine304 shows the signal (" + how + "=" + shown.ToString("0.00") + ", " + signal + "=" + p.ToString("0.00") + ")");
            }

            if (tick == null) skip("stalled load rotates the stage line after 2 s", "no public tick(float) among " + string.Join("/", tickNames));
            else
            {
                // #304 integration: the flow screen rotates its D20 waiting line (WaitingLine) on a stall and keeps the stage text
                string Line() => HarnessUiRules304.Member(screen, "WaitingLine") as string ?? "";
                string before = screen.Stage, beforeLine = Line(); Tick(2.1f, 21);
                check(screen.Stage != before || Line() != beforeLine, "stalled load rotates the stage / waiting line after 2 s (" + before + " | " + beforeLine + " -> " + screen.Stage + " | " + Line() + ")");
            }
        }
    }
}
