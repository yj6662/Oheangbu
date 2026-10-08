using System;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    // #308 그림 시네마틱 — uGUI 표현기 [TEST, SPEC-CINEMATIC-STILLS-308 7.3, D308-26 / D308-28].
    // 자체 ScreenSpaceOverlay 캔버스(자료의 SortingOrder: 암막 150 과 로딩 32000 사이), 종이 바탕 Image 하나 + RawImage 둘. 글자 구성요소 0.
    // 시계는 unscaled 이고 순수 논리(CinematicStillsTimeline)가 장 · 창 · 알파 · 소리 세기를 낸다. 코루틴 0, 정적 칸 0.
    // 세계 시간: Freeze = Pause 면 PauseCoordinator.Begin / End 로 멈춘다(작도 취소 · 게이트 · 차 입력 · timeScale · 커서를 한 곳이 맡는다).
    // HUD 캔버스는 건드리지 않는다 — 불투명 층이 덮는다(검토 S4 의 둘째 길: 되돌릴 것이 없으면 어긋날 것도 없다).
    // 끝낼 때의 순서(S4): 소리 멈춤 -> 층 내림 -> Pause.End(시간 · 게이트 · 커서) -> 그림 내림 -> 그 뒤에 Finished.
    // 그림 · 카탈로그 · 소리가 없으면 아무 일도 하지 않는다(S5): 멈춤 0 · 신호 0 · 예외 0.
    [DisallowMultipleComponent]
    public sealed class CinematicStillsPresenter : MonoBehaviour, ICinematicStillsPlayer
    {
        /// <summary>Harness slots (a non-empty save suffix) show nothing unless the check sets this. A new presenter is built at
        /// every scene bind, so it is false again after each load. Builds: the command line `--cinematic-test`.</summary>
        public bool AllowInHarness { get; set; }
        public bool IsPlaying => timeline != null;
        public string PlayingId => playing != null ? playing.Id : "";
        public int StartedCount { get; private set; }
        public int FinishedCount { get; private set; }
        public int RefusedCount { get; private set; }
        public Transform LayerRoot => canvas != null ? canvas.transform : null;
        public CinematicStillsTimeline Timeline => timeline;

        CinematicStillsCatalogSO catalog; PauseCoordinator pause; ICinematicStillsHost host;
        Func<string, AudioClip> clipFor; Func<float> gainScale; Func<bool> reducedMotion; AudioMixerGroup mixerGroup;
        Canvas canvas; CanvasGroup group; RawImage back, front;
        AudioSource[] voices = Array.Empty<AudioSource>(); string[] voiceIds = Array.Empty<string>();
        string loadedId, pendingId, harnessPreloadId; float pendingDeadline; ResourceRequest[] requests; Texture2D[] pictures;
        readonly HashSet<string> missing = new HashSet<string>();
        readonly List<CinematicStillsCatalogSO.Cue> oneShots = new List<CinematicStillsCatalogSO.Cue>();
        CinematicStillsTimeline timeline; CinematicStillsCatalogSO.Sequence playing; bool beganPause, subscribed, audioPaused;

        public void Initialize(CinematicStillsCatalogSO catalog, PauseCoordinator pause, ICinematicStillsHost host,
            Func<string, AudioClip> clipFor, Func<float> gainScale, AudioMixerGroup mixerGroup, Func<bool> reducedMotion)
        {
            Unsubscribe();
            this.catalog = catalog; this.pause = pause; this.host = host; this.clipFor = clipFor; this.gainScale = gainScale; this.mixerGroup = mixerGroup; this.reducedMotion = reducedMotion;
            if (catalog == null || catalog.Style == null) return;
#if !UNITY_EDITOR
            foreach (string arg in Environment.GetCommandLineArgs()) if (arg == CinematicStillsCatalogSO.HarnessArgument) AllowInHarness = true;
#endif
            BuildLayer();
            if (isActiveAndEnabled) Subscribe();
        }

        void OnEnable() { Subscribe(); }
        void OnDisable() { Unsubscribe(); if (IsPlaying) End(); pendingId = null; Unload(); }

        void Subscribe()
        {
            if (subscribed || catalog == null || catalog.Requested == null) return;
            catalog.Requested.Subscribe(OnRequested); subscribed = true;
        }
        void Unsubscribe()
        {
            if (!subscribed) return;
            if (catalog != null && catalog.Requested != null) catalog.Requested.Unsubscribe(OnRequested);
            subscribed = false;
        }

        void BuildLayer()
        {
            if (canvas != null) return;
            var go = new GameObject("CinematicStills308", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = catalog.Style.SortingOrder; canvas.enabled = false;
            group = go.GetComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;
            var paper = Child("Paper", go.transform).AddComponent<Image>(); paper.color = catalog.Style.Paper; paper.raycastTarget = false;
            back = Child("StillBack", go.transform).AddComponent<RawImage>(); back.raycastTarget = false; back.enabled = false;
            front = Child("StillFront", go.transform).AddComponent<RawImage>(); front.raycastTarget = false; front.enabled = false;
            int count = catalog.Rules != null ? Mathf.Max(1, catalog.Rules.MaxVoices) : 1;
            voices = new AudioSource[count]; voiceIds = new string[count];
            for (int i = 0; i < count; i++)
            {
                var source = new GameObject("CinematicVoice" + i).AddComponent<AudioSource>(); source.transform.SetParent(transform, false);
                source.playOnAwake = false; source.spatialBlend = 0f; source.ignoreListenerPause = true; source.outputAudioMixerGroup = mixerGroup;
                voices[i] = source;
            }
        }

        static GameObject Child(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)); var r = (RectTransform)go.transform; r.SetParent(parent, false);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            return go;
        }

        /// <summary>The check's direct play has no trigger to load ahead for it: ask for the pictures first, request a moment later.</summary>
        public void PreloadForHarness(string sequenceId) { harnessPreloadId = sequenceId; }
        public bool PicturesReady(string sequenceId) => loadedId == sequenceId && pictures != null;

        public bool PictureMissing(string sequenceId) => !string.IsNullOrEmpty(sequenceId) && missing.Contains(sequenceId);

        void OnRequested(string id)
        {
            if (!isActiveAndEnabled || catalog == null || host == null || canvas == null || IsPlaying || pendingId != null) { RefusedCount++; return; }
            var sequence = catalog.Find(id);
            // the one rule (disabled, seen, enrolment, harness slot, missing picture) lives with the host; a second request is refused here
            if (sequence == null || host.CinematicVerdict308(id) != CinematicVerdict.Ok) { RefusedCount++; return; }
            if (sequence.Freeze == CinematicFreeze.Pause && (pause == null || pause.IsPaused)) { RefusedCount++; return; }   // a menu owns the pause
            EnsureLoad(id); pendingId = id; pendingDeadline = Time.unscaledTime + catalog.Style.LoadTimeoutSeconds;
            TryBeginPending();
        }

        void Update()
        {
            if (timeline != null) { TickPlaying(); return; }
            if (pendingId != null)
            {
                TryBeginPending();
                if (pendingId != null && Time.unscaledTime > pendingDeadline) { pendingId = null; RefusedCount++; }   // not ready in time: dropped, not seen
                return;
            }
            string want = host != null ? host.CinematicPreloadId308 : null;
            if (string.IsNullOrEmpty(want)) want = harnessPreloadId;
            if (!string.IsNullOrEmpty(want)) { EnsureLoad(want); PollLoad(); }
            else if (loadedId != null) Unload();
        }

        void EnsureLoad(string id)
        {
            if (loadedId == id || missing.Contains(id)) return;
            Unload();
            var sequence = catalog.Find(id); if (sequence == null || sequence.Stills == null || sequence.Stills.Length == 0) return;
            requests = new ResourceRequest[sequence.Stills.Length];
            for (int i = 0; i < requests.Length; i++) requests[i] = Resources.LoadAsync<Texture2D>(sequence.Stills[i].ResourcePath);
            loadedId = id; pictures = null;
        }

        bool PollLoad()
        {
            if (pictures != null) return true;
            if (requests == null) return false;
            foreach (var request in requests) if (request == null || !request.isDone) return false;
            var loaded = new Texture2D[requests.Length]; bool whole = true;
            for (int i = 0; i < loaded.Length; i++) { loaded[i] = requests[i].asset as Texture2D; if (loaded[i] == null) whole = false; }
            requests = null;
            if (!whole) { missing.Add(loadedId); foreach (var t in loaded) if (t != null) Resources.UnloadAsset(t); loadedId = null; return false; }
            pictures = loaded; return true;
        }

        void Unload()
        {
            if (front != null) front.texture = null;
            if (back != null) back.texture = null;
            if (pictures != null) foreach (var t in pictures) if (t != null) Resources.UnloadAsset(t);
            pictures = null; requests = null; loadedId = null;
        }

        void TryBeginPending()
        {
            if (loadedId != pendingId) { pendingId = null; RefusedCount++; return; }   // the picture is known to be missing
            if (!PollLoad()) { if (loadedId == null) { pendingId = null; RefusedCount++; } return; }
            var sequence = catalog.Find(pendingId); pendingId = null;
            if (sequence == null) return;
            if (sequence.Freeze == CinematicFreeze.Pause && (pause == null || pause.IsPaused)) { RefusedCount++; return; }
            Begin(sequence);
        }

        void Begin(CinematicStillsCatalogSO.Sequence sequence)
        {
            playing = sequence; harnessPreloadId = null;
            if (sequence.Freeze == CinematicFreeze.Pause)
            {
                pause.Begin(); beganPause = true;
                Cursor.visible = false;   // Pause.Begin shows the cursor (TE-9); Pause.End restores what was there before
            }
            timeline = new CinematicStillsTimeline(sequence, catalog.Style, reducedMotion != null && reducedMotion());
            canvas.sortingOrder = catalog.Style.SortingOrder; canvas.enabled = true; front.enabled = true; audioPaused = false;
            ApplyFrame(); ApplyAudio(true);
            StartedCount++;
            if (catalog.Started != null) catalog.Started.Raise(sequence.Id);   // the session writes "seen" here
        }

        void TickPlaying()
        {
            bool focused = Application.isFocused;   // in the background the clock and the sound stand still (rest-veil precedent)
            if (focused && SkipPressed()) timeline.TrySkip();
            timeline.Advance(focused ? Time.unscaledDeltaTime : 0f);
            ApplyFrame(); ApplyAudio(focused);
            if (timeline.State == CinematicStillsTimeline.Phase.Done) End();
        }

        bool SkipPressed()
        {
            var keyboard = Keyboard.current; var mouse = Mouse.current; var pad = Gamepad.current;
            foreach (var key in catalog.Style.SkipKeys)
                switch (key)
                {
                    case CinematicSkipKey.F: if (keyboard != null && keyboard.fKey.wasPressedThisFrame) return true; break;
                    case CinematicSkipKey.Space: if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) return true; break;
                    case CinematicSkipKey.Enter: if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) return true; break;
                    case CinematicSkipKey.Escape: if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) return true; break;
                    case CinematicSkipKey.MouseLeft: if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true; break;
                    case CinematicSkipKey.PadSouth: if (pad != null && pad.buttonSouth.wasPressedThisFrame) return true; break;
                    case CinematicSkipKey.PadEast: if (pad != null && pad.buttonEast.wasPressedThisFrame) return true; break;
                    case CinematicSkipKey.PadStart: if (pad != null && pad.startButton.wasPressedThisFrame) return true; break;
                }
            return false;
        }

        void ApplyFrame()
        {
            if (pictures == null || timeline == null) return;
            float view = Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f;
            var first = pictures[0]; float source = first != null && first.height > 0 ? first.width / (float)first.height : 16f / 9f;
            var frame = timeline.Evaluate(view, source);
            group.alpha = frame.LayerAlpha;
            front.texture = pictures[frame.Front]; front.uvRect = frame.FrontUv; front.color = new Color(1f, 1f, 1f, frame.FrontAlpha);
            back.enabled = frame.Back >= 0;
            if (frame.Back >= 0) { back.texture = pictures[frame.Back]; back.uvRect = frame.BackUv; }
        }

        void ApplyAudio(bool focused)
        {
            if (!focused) { if (!audioPaused) { foreach (var v in voices) if (v != null) v.Pause(); audioPaused = true; } return; }
            if (audioPaused) { foreach (var v in voices) if (v != null) v.UnPause(); audioPaused = false; }
            float scale = gainScale != null ? Mathf.Clamp01(gainScale()) : 1f;
            foreach (string id in timeline.LoopIds)
            {
                float gain = timeline.VoiceGain(id); int slot = Array.IndexOf(voiceIds, id);
                if (gain <= 0f) { if (slot >= 0) { voices[slot].Stop(); voiceIds[slot] = null; } continue; }
                if (slot < 0)
                {
                    slot = Array.IndexOf(voiceIds, null); var clip = slot >= 0 && clipFor != null ? clipFor(id) : null;
                    if (clip == null) continue;   // no free voice or no such sound: silent, the stills go on
                    voiceIds[slot] = id; voices[slot].clip = clip; voices[slot].loop = true; voices[slot].volume = gain * scale; voices[slot].Play();
                }
                else voices[slot].volume = gain * scale;
            }
            oneShots.Clear(); timeline.CollectOneShots(oneShots);
            foreach (var cue in oneShots)
            {
                var clip = clipFor != null ? clipFor(cue.Id) : null;
                if (clip != null && voices.Length > 0) voices[0].PlayOneShot(clip, cue.Gain * scale);
            }
        }

        void End()
        {
            string id = playing != null ? playing.Id : "";
            for (int i = 0; i < voices.Length; i++) { if (voices[i] != null) { voices[i].Stop(); voices[i].clip = null; } voiceIds[i] = null; }
            if (canvas != null) canvas.enabled = false;
            if (group != null) group.alpha = 0f;
            timeline = null; playing = null;
            if (beganPause) { beganPause = false; if (pause != null) pause.End(); }   // time, gate (released when the keys are neutral), cursor
            Unload();
            FinishedCount++;
            if (catalog != null && catalog.Finished != null) catalog.Finished.Raise(id);   // S4: only after everything is back
        }
    }

    public sealed partial class PlaytestUiRoot
    {
        CinematicStillsPresenter cinematic308;
        /// <summary>The stills presenter of this scene bind (null in the title, or when the catalogue asset does not exist).</summary>
        public CinematicStillsPresenter Cinematic308 => cinematic308;
        bool CinematicPlaying308 => cinematic308 != null && cinematic308.IsPlaying;

        // TE-1: built here like the map presenter (the UI root is outside the LifetimeScope). No scene object, no new static.
        void Cinematic308Bind()
        {
            Cinematic308Unbind();
            var catalog = Resources.Load<CinematicStillsCatalogSO>(CinematicStillsCatalogSO.ResourcePath);
            if (catalog == null || Session == null) return;   // a clone without the (untracked) asset: nothing is built, nothing changes
            var go = new GameObject("CinematicStillsPresenter308"); go.transform.SetParent(transform, false);
            cinematic308 = go.AddComponent<CinematicStillsPresenter>();
            var mix = Theme != null ? Theme.AudioMix : null;
            cinematic308.Initialize(catalog, Pause, Session, Cinematic308Clip, Cinematic308Gain, mix != null && mix.IsReady ? mix.Ui : null, Cinematic308ReducedMotion);
            Session.BindCinematicPlayer308(cinematic308);
        }
        void Cinematic308Unbind()
        {
            if (Session != null) Session.BindCinematicPlayer308(null);
            if (cinematic308 != null) { Destroy(cinematic308.gameObject); cinematic308 = null; }
        }
        AudioClip Cinematic308Clip(string id)
        {
            var palette = Theme != null ? Theme.SoundPalette : null; var cue = palette != null ? palette.Find(id) : null;
            return cue != null ? cue.Clip : null;
        }
        float Cinematic308Gain()
        {
            var mix = Theme != null ? Theme.AudioMix : null;
            return mix != null && mix.IsReady || Settings == null ? 1f : Mathf.Clamp01(Settings.Current.MasterVolume) * Mathf.Clamp01(Settings.Current.UiVolume);
        }
        bool Cinematic308ReducedMotion() => Settings != null && Settings.Current.ReducedMotion;
    }
}
