using System;
using Oheangbu.App.World.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World
{
    // #303 death presentation (SPEC-PLAYER-FEEL-300 H): the body falls (Reaction303 Death), an ink veil closes, the session's
    // own respawn runs under the veil (same drop / checkpoint / restore / save as before), the veil lifts on the get-up.
    // Text-free, like the doorway rest. Input is gate-blocked for the whole transition. Quitting or disabling mid-fall
    // completes the durable respawn at once, so no death is lost or undone.
    // #304 (DESIGN §5.15, IMPLEMENTATION §7.14): the Shade is the 먹장막 #0F0F0E revealed with UI/InkReveal in Edges mode (it
    // bleeds in from the screen edges and clears from the centre); the Profile timings drive the reveal value unchanged.
    // Reduced motion (or a style without the InkReveal material) = the same curve as a plain alpha fade. After the veil lifts,
    // the wake-up line the respawn wrote is raised once on the UiNoticeChannelSO (Kind Wake) instead of sitting in the prompt.
    [DisallowMultipleComponent]
    public sealed class WorldMacroPlayerDeath303 : MonoBehaviour
    {
        public WorldMacroPlaytestSession Session;
        public WorldMacroPlayerReaction303 Reaction;
        public PlayerReaction303Profile Profile;
        public bool IsActive { get; private set; }
        public float Elapsed { get; private set; }
        public int Completed { get; private set; }
        /// <summary>Veil coverage 0..1 (the Profile curve; with InkReveal it is the reveal value, otherwise the alpha).</summary>
        public float VeilAlpha => veil != null ? coverage : 0f;
        public bool CanPresent => isActiveAndEnabled && Session != null && Reaction != null && Profile != null && PlaytestUiRoot.Instance != null;
        /// <summary>The respawn feedback line routed to the notice channel (kept off the HUD prompt by the presenter).</summary>
        public string RoutedWakeLine => routedLine;

        Image veil; InkRevealEffect fx; UiStyle304SO style; GameplayUiGate gate; bool ownsGate, respawned, gettingUp; Action respawn;
        float coverage, routedUntil; string feedbackBefore, routedLine; bool wakePending;

        public void Begin(Action onRespawn)
        {
            respawn = onRespawn; Elapsed = 0f; respawned = false; gettingUp = false; IsActive = true;
            routedLine = null; wakePending = false;
            EnsureVeil();
            gate = PlaytestUiRoot.Instance.Gate; if (gate != null) { gate.Block(); ownsGate = true; }
            if (Session.Walker != null && Session.Walker.Motor != null) Session.Walker.Motor.ResetMotion();
            Reaction.BeginDeath();
        }

        public void CompleteNow()
        {
            if (!IsActive) return;
            if (!respawned) { respawned = true; RunRespawn(); Reaction.Clear(); }
            Finish();
        }

        /// <summary>True for the wake-up line this presentation routed to the notice channel.</summary>
        public bool IsRoutedWakeLine(string text) => !string.IsNullOrEmpty(routedLine) && text == routedLine && Time.unscaledTime < routedUntil;

        void RunRespawn()
        {
            feedbackBefore = Session != null ? Session.LastFeedback : null;
            respawn?.Invoke();
            string after = Session != null ? Session.LastFeedback : null;
            // the session keeps LastFeedback 7 s; the suppression outlives it a little and then lets go
            if (!string.IsNullOrEmpty(after) && after != feedbackBefore) { routedLine = after; routedUntil = Time.unscaledTime + 10f; wakePending = true; }
        }

        void EnsureVeil()
        {
            if (veil != null) return;
            var root = new GameObject("DeathTransition303", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 150;
            var image = new GameObject("Shade", typeof(RectTransform), typeof(Image)); image.transform.SetParent(root.transform, false);
            var rect = (RectTransform)image.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            veil = image.GetComponent<Image>(); veil.raycastTarget = false;
            style = PlaytestUiRoot.Instance != null ? PlaytestUiView.Style(PlaytestUiRoot.Instance.Theme) : UiStyle304SO.Fallback;
            PlaytestUiView.EnsureCanvasChannels(canvas);
            fx = InkRevealEffect.On(veil, style, InkRevealMode.Edges, 0f);
            Paint(0f);
        }

        bool ShaderReveal => fx != null && style != null && style.Materials.InkReveal != null && veil.material == style.Materials.InkReveal && !UiTween304.ReducedMotion;

        void Paint(float a)
        {
            coverage = Mathf.Clamp01(a);
            Color c = style != null && !UiStyle304SO.IsFallback(style) ? style.Veil : Profile.VeilColor;
            if (ShaderReveal) { fx.Reveal = coverage; c.a = coverage > 0f ? 1f : 0f; }
            else { if (fx != null) fx.Reveal = 1f; c.a = coverage; }
            veil.color = c;
        }

        void Update()
        {
            if (!IsActive) return;
            Elapsed += Mathf.Min(Time.unscaledDeltaTime, .1f);
            var p = Profile;
            float a = Elapsed < p.VeilOutStart
                ? Mathf.SmoothStep(0f, 1f, (Elapsed - p.VeilStart) / Mathf.Max(.01f, p.VeilFull - p.VeilStart))
                : 1f - Mathf.SmoothStep(0f, 1f, (Elapsed - p.VeilOutStart) / Mathf.Max(.01f, p.VeilOutSeconds));
            Paint(a);
            if (!respawned && Elapsed >= p.RespawnAt)
            {
                respawned = true; RunRespawn();
                Reaction.BeginGetUp(); gettingUp = true;
            }
            if (gettingUp && Elapsed >= p.VeilOutStart + p.VeilOutSeconds && Reaction.GetUpProgress >= p.GetUpRelease) Finish();
        }

        void Finish()
        {
            if (veil != null) Paint(0f);
            if (IsActive) Completed++;
            IsActive = false;
            if (ownsGate && gate != null) gate.ReleaseWhenNeutral();
            ownsGate = false;
            RaiseWake();
        }

        // "마지막 쉼터에서 눈을 떴다. 남긴 통보를 되찾을 수 있다." -> title = first sentence, source = the rest (DESIGN §5.15).
        void RaiseWake()
        {
            if (!wakePending) return;
            wakePending = false;
            var s = style != null ? style : (PlaytestUiRoot.Instance != null ? PlaytestUiView.Style(PlaytestUiRoot.Instance.Theme) : null);
            var channel = s != null ? s.Notices : null;
            if (channel == null || string.IsNullOrEmpty(routedLine)) { routedLine = null; return; }   // no channel: the prompt keeps the line
            string line = routedLine.Trim();
            int cut = line.IndexOf(". ", StringComparison.Ordinal);
            string title = cut > 0 ? line.Substring(0, cut + 1) : line;
            string source = cut > 0 ? line.Substring(cut + 2).Trim() : null;
            channel.Raise(UiNoticeKind304.Wake, title, source);
        }

        void OnDisable() { CompleteNow(); }
    }
}
