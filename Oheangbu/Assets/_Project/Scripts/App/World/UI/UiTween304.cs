using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Oheangbu.App.World.UI
{
    /// <summary>Tiny UniTask tween helper for #304 (IMPLEMENTATION §4.4). Unscaled time (works while paused), cancellable,
    /// never throws on cancel (a cancelled tween just stops where it is), stops by itself when its target is destroyed.
    /// Curves come from UiStyle304SO.Motion (Stroke / Lift / Press). No coroutines.
    /// Typical: UiTween304.Reveal(fx, 1, s.Motion.Sec(s.Motion.StrokeMs, UiTween304.ReducedMotion), s.Motion.Stroke, this.GetCancellationTokenOnDestroy()).Forget();</summary>
    public static class UiTween304
    {
        // ------------------------------------------------------------------ reduced motion (reads the existing settings service once per frame)
        static int reducedFrame = -1;
        static bool reducedValue;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { reducedFrame = -1; reducedValue = false; }

        /// <summary>Settings "움직임 줄이기" (UserSettingsData.ReducedMotion). Every wipe then becomes a ReducedMs alpha fade.</summary>
        public static bool ReducedMotion
        {
            get
            {
                if (reducedFrame == Time.frameCount) return reducedValue;
                reducedFrame = Time.frameCount;
                var root = PlaytestUiRoot.Instance;
                reducedValue = root != null && root.Settings != null && root.Settings.Current.ReducedMotion;
                return reducedValue;
            }
        }

        public static CancellationToken Token(Component owner) => owner != null ? owner.GetCancellationTokenOnDestroy() : CancellationToken.None;

        // ------------------------------------------------------------------ core
        /// <summary>apply(eased 0..1) every frame for `seconds` of unscaled time; apply(1) is always the last call unless cancelled.</summary>
        public static UniTask Run(float seconds, AnimationCurve curve, Action<float> apply, CancellationToken ct = default)
            => Loop(seconds, curve, null, apply, ct);

        /// <summary>Unscaled delay. Returns true when cancelled.</summary>
        public static async UniTask<bool> Delay(float seconds, CancellationToken ct = default)
        {
            float t = 0f;
            while (t < seconds)
            {
                if (await UniTask.Yield(PlayerLoopTiming.Update, ct).SuppressCancellationThrow()) return true;
                t += Time.unscaledDeltaTime;
            }
            return ct.IsCancellationRequested;
        }

        static async UniTask Loop(float seconds, AnimationCurve curve, Object guard, Action<float> apply, CancellationToken ct)
        {
            if (apply == null || ct.IsCancellationRequested) return;
            bool guarded = !ReferenceEquals(guard, null);
            if (guarded && guard == null) return;
            if (seconds <= 0f) { apply(1f); return; }
            float t = 0f;
            apply(UiEase304.Eval(curve, 0f));
            while (t < seconds)
            {
                if (await UniTask.Yield(PlayerLoopTiming.Update, ct).SuppressCancellationThrow()) return;
                if (guarded && guard == null) return;
                t += Time.unscaledDeltaTime;
                apply(UiEase304.Eval(curve, t / seconds));
            }
        }

        // ------------------------------------------------------------------ typed helpers (from = current value)
        public static UniTask Alpha(CanvasGroup g, float to, float seconds, AnimationCurve curve = null, CancellationToken ct = default)
        {
            if (g == null) return UniTask.CompletedTask;
            float from = g.alpha;
            return Loop(seconds, curve, g, k => g.alpha = Mathf.LerpUnclamped(from, to, k), ct);
        }

        /// <summary>Graphic colour alpha (keeps rgb).</summary>
        public static UniTask Fade(Graphic g, float toAlpha, float seconds, AnimationCurve curve = null, CancellationToken ct = default)
        {
            if (g == null) return UniTask.CompletedTask;
            float from = g.color.a;
            return Loop(seconds, curve, g, k => { var c = g.color; c.a = Mathf.LerpUnclamped(from, toAlpha, k); g.color = c; }, ct);
        }

        public static UniTask Tint(Graphic g, Color to, float seconds, AnimationCurve curve = null, CancellationToken ct = default)
        {
            if (g == null) return UniTask.CompletedTask;
            Color from = g.color;
            return Loop(seconds, curve, g, k => g.color = Color.LerpUnclamped(from, to, k), ct);
        }

        /// <summary>anchoredPosition.</summary>
        public static UniTask Move(RectTransform r, Vector2 to, float seconds, AnimationCurve curve = null, CancellationToken ct = default)
        {
            if (r == null) return UniTask.CompletedTask;
            Vector2 from = r.anchoredPosition;
            return Loop(seconds, curve, r, k => r.anchoredPosition = Vector2.LerpUnclamped(from, to, k), ct);
        }

        /// <summary>World position (e.g. the 방점 sliding between two rows of different parents).</summary>
        public static UniTask MoveWorld(Transform t, Vector3 to, float seconds, AnimationCurve curve = null, CancellationToken ct = default)
        {
            if (t == null) return UniTask.CompletedTask;
            Vector3 from = t.position;
            return Loop(seconds, curve, t, k => t.position = Vector3.LerpUnclamped(from, to, k), ct);
        }

        /// <summary>sizeDelta (only for things that are not layout, e.g. a meter growing with its max).</summary>
        public static UniTask Size(RectTransform r, Vector2 to, float seconds, AnimationCurve curve = null, CancellationToken ct = default)
        {
            if (r == null) return UniTask.CompletedTask;
            Vector2 from = r.sizeDelta;
            return Loop(seconds, curve, r, k => r.sizeDelta = Vector2.LerpUnclamped(from, to, k), ct);
        }

        public static UniTask Scale(Transform t, Vector3 to, float seconds, AnimationCurve curve = null, CancellationToken ct = default)
        {
            if (t == null) return UniTask.CompletedTask;
            Vector3 from = t.localScale;
            return Loop(seconds, curve, t, k => t.localScale = Vector3.LerpUnclamped(from, to, k), ct);
        }

        /// <summary>localEulerAngles.z in degrees.</summary>
        public static UniTask Rotate(Transform t, float toZ, float seconds, AnimationCurve curve = null, CancellationToken ct = default)
        {
            if (t == null) return UniTask.CompletedTask;
            float from = t.localEulerAngles.z; if (from > 180f) from -= 360f;
            return Loop(seconds, curve, t, k => t.localRotation = Quaternion.Euler(0, 0, Mathf.LerpUnclamped(from, toZ, k)), ct);
        }

        public static UniTask Reveal(InkRevealEffect fx, float to, float seconds, AnimationCurve curve = null, CancellationToken ct = default)
        {
            if (fx == null) return UniTask.CompletedTask;
            float from = fx.Reveal;
            return Loop(seconds, curve, fx, k => fx.Reveal = Mathf.Lerp(from, to, k), ct);
        }

        public static UniTask Fill(InkMeterGraphic m, float to, float seconds, AnimationCurve curve = null, CancellationToken ct = default)
        {
            if (m == null) return UniTask.CompletedTask;
            float from = m.Fill;
            return Loop(seconds, curve, m, k => m.Fill = Mathf.Lerp(from, to, k), ct);
        }

        // ------------------------------------------------------------------ design verbs
        /// <summary>획 긋기: reveal 0 -> 1 with ease.stroke (240 ms, band 320 ms). fromCurrent = continue from the present reveal
        /// (a focus underlay re-focused mid-lift). Reduced motion: drawn at once and the graphic alpha fades 0 -> baseAlpha over
        /// ReducedMs. Pass the graphic's design alpha as baseAlpha when the same graphic is toggled repeatedly (a cancelled fade
        /// must not become the new base); -1 = its current alpha.</summary>
        public static UniTask StrokeIn(InkRevealEffect fx, UiStyle304SO s, bool band = false, CancellationToken ct = default, float baseAlpha = -1f, bool fromCurrent = false)
        {
            if (fx == null || s == null) return UniTask.CompletedTask;
            if (!fromCurrent) fx.Reveal = 0f;
            var g = fx.Target;
            if (ReducedMotion)
            {
                fx.Reveal = 1f; if (g == null) return UniTask.CompletedTask;
                float to = baseAlpha >= 0f ? baseAlpha : g.color.a; SetAlpha(g, 0f);
                return Fade(g, to, s.Motion.Sec(s.Motion.ReducedMs), null, ct);
            }
            if (g != null && baseAlpha >= 0f) SetAlpha(g, baseAlpha);
            return Reveal(fx, 1f, s.Motion.Sec(band ? s.Motion.StrokeBandMs : s.Motion.StrokeMs), s.Motion.Stroke, ct);
        }

        /// <summary>붓 들기: reveal -> 0 with ease.lift (UnderlayOutMs). Reduced motion: alpha fade, then reveal 0 and the
        /// alpha restored to baseAlpha (-1 = the alpha it had when the fade started).</summary>
        public static async UniTask StrokeOut(InkRevealEffect fx, UiStyle304SO s, CancellationToken ct = default, float baseAlpha = -1f)
        {
            if (fx == null || s == null) return;
            if (ReducedMotion)
            {
                var g = fx.Target; if (g == null) { fx.Reveal = 0f; return; }
                float a = baseAlpha >= 0f ? baseAlpha : g.color.a;
                await Fade(g, 0f, s.Motion.Sec(s.Motion.ReducedMs), null, ct);
                if (fx == null || g == null || ct.IsCancellationRequested) return;
                fx.Reveal = 0f; SetAlpha(g, a); return;
            }
            await Reveal(fx, 0f, s.Motion.Sec(s.Motion.UnderlayOutMs), s.Motion.Lift, ct);
        }

        /// <summary>Any InkReveal entrance other than a focus underlay: veil (Motion.VeilMs, Wipe), rest / death shade
        /// (RestInMs / DeathInMs, Edges), arrival card (ArrivalInMs). reveal 0 -> 1 over `ms` with ease.stroke (or `curve`).
        /// Reduced motion: drawn at once and the graphic alpha fades 0 -> baseAlpha over ReducedMs (-1 = its current alpha).</summary>
        public static UniTask RevealIn(InkRevealEffect fx, UiStyle304SO s, int ms, CancellationToken ct = default, float baseAlpha = -1f, AnimationCurve curve = null)
        {
            if (fx == null || s == null) return UniTask.CompletedTask;
            var g = fx.Target;
            if (ReducedMotion)
            {
                fx.Reveal = 1f; if (g == null) return UniTask.CompletedTask;
                float to = baseAlpha >= 0f ? baseAlpha : g.color.a; SetAlpha(g, 0f);
                return Fade(g, to, s.Motion.Sec(s.Motion.ReducedMs), null, ct);
            }
            if (g != null && baseAlpha >= 0f) SetAlpha(g, baseAlpha);
            fx.Reveal = 0f;
            return Reveal(fx, 1f, ms / 1000f, curve ?? s.Motion.Stroke, ct);
        }

        /// <summary>Matching exit: reveal -> 0 over `ms` with ease.lift (or `curve`): shade 걷힘 RestOutMs / DeathOutMs, toast and
        /// arrival 나감 (Bleed, ToastOutMs / ArrivalOutMs). Reduced motion: alpha fade over ReducedMs, then reveal 0 and the alpha
        /// restored to baseAlpha (-1 = the alpha it had when the fade started).</summary>
        public static async UniTask RevealOut(InkRevealEffect fx, UiStyle304SO s, int ms, CancellationToken ct = default, float baseAlpha = -1f, AnimationCurve curve = null)
        {
            if (fx == null || s == null) return;
            if (ReducedMotion)
            {
                var g = fx.Target; if (g == null) { fx.Reveal = 0f; return; }
                float a = baseAlpha >= 0f ? baseAlpha : g.color.a;
                await Fade(g, 0f, s.Motion.Sec(s.Motion.ReducedMs), null, ct);
                if (fx == null || g == null || ct.IsCancellationRequested) return;
                fx.Reveal = 0f; SetAlpha(g, a); return;
            }
            await Reveal(fx, 0f, ms / 1000f, curve ?? s.Motion.Lift, ct);
        }

        public static void SetAlpha(Graphic g, float alpha) { if (g == null) return; var c = g.color; c.a = alpha; g.color = c; }

        /// <summary>내용 번짐: CanvasGroup alpha 0 -> 1 over RevealMs after `order` x RevealGapMs (ease.stroke).</summary>
        public static async UniTask BleedIn(CanvasGroup g, UiStyle304SO s, int order = 0, CancellationToken ct = default)
        {
            if (g == null || s == null) return;
            g.alpha = 0f;
            bool reduced = ReducedMotion;
            if (!reduced && order > 0 && await Delay(order * s.Motion.RevealGapMs / 1000f, ct)) return;
            await Alpha(g, 1f, s.Motion.Sec(s.Motion.RevealMs, reduced), s.Motion.Stroke, ct);
        }
    }

    /// <summary>One animatable property: Restart() cancels the tween that ran before it (so two tweens never fight).</summary>
    public sealed class UiTweenSlot304
    {
        CancellationTokenSource cts;

        public CancellationToken Restart(CancellationToken owner = default)
        {
            Cancel();
            cts = owner.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(owner) : new CancellationTokenSource();
            return cts.Token;
        }

        public CancellationToken Restart(Component owner) => Restart(UiTween304.Token(owner));

        public void Cancel()
        {
            if (cts == null) return;
            try { cts.Cancel(); } finally { cts.Dispose(); cts = null; }
        }
    }
}
