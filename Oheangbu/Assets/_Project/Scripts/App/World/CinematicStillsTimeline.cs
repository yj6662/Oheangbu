using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 그림 시네마틱 — 순수 논리(MonoBehaviour 아님, 정적 가변 칸 0). SPEC-CINEMATIC-STILLS-308 7.2.
    // 시계(초, unscaled)를 받아 보이는 장 · 창 · 알파 · 소리 세기를 낸다. 수치는 전부 자료(CinematicStillsCatalogSO)에서 온다.
    // 벽시계 길이 = 장들의 Seconds 합. 층의 들고 나감(FadeIn / FadeOut)은 첫 장의 앞 · 끝 장의 뒤 안에 든다(더해지지 않는다).
    // 장 i (i > 0) 는 제 시작부터 CrossFadeSeconds 동안 앞 장 위로 번져 든다. 검은 암전은 없다.
    public sealed class CinematicStillsTimeline
    {
        public enum Phase { Playing, SkipFading, Done }

        public struct Frame
        {
            public float LayerAlpha;
            public int Front, Back;        // Back = -1: 앞 장 하나만
            public float FrontAlpha;
            public Rect FrontUv, BackUv;   // RawImage.uvRect (왼쪽 아래 원점)
        }

        readonly CinematicStillsCatalogSO.Sequence sequence;
        readonly CinematicStillsCatalogSO.Tokens tokens;
        readonly bool reducedMotion;
        readonly float[] starts;
        readonly string[] loopIds;
        float skipAt, skipAlpha, previousClock = -1f;

        public float Clock { get; private set; }
        public float Duration { get; }
        public Phase State { get; private set; }
        public bool Skipped { get; private set; }
        public IReadOnlyList<string> LoopIds => loopIds;

        public CinematicStillsTimeline(CinematicStillsCatalogSO.Sequence sequence, CinematicStillsCatalogSO.Tokens tokens, bool reducedMotion)
        {
            this.sequence = sequence; this.tokens = tokens; this.reducedMotion = reducedMotion;
            int n = sequence.Stills.Length; starts = new float[n]; float t = 0f;
            var ids = new List<string>();
            for (int i = 0; i < n; i++)
            {
                starts[i] = t; t += sequence.Stills[i].Seconds;
                foreach (var cue in sequence.Stills[i].Cues) if (cue != null && cue.Loop && !string.IsNullOrEmpty(cue.Id) && !ids.Contains(cue.Id)) ids.Add(cue.Id);
            }
            Duration = t; loopIds = ids.ToArray(); State = n > 0 && Duration > 0f ? Phase.Playing : Phase.Done;
        }

        float FadeIn => reducedMotion ? tokens.ReducedMotionFadeSeconds : tokens.FadeInSeconds;
        float FadeOut => reducedMotion ? tokens.ReducedMotionFadeSeconds : tokens.FadeOutSeconds;
        float SkipFade => reducedMotion ? Mathf.Min(tokens.SkipFadeSeconds, tokens.ReducedMotionFadeSeconds) : tokens.SkipFadeSeconds;
        float CrossFade(int still) => reducedMotion ? tokens.ReducedMotionFadeSeconds : sequence.Stills[still].CrossFadeSeconds;

        /// <summary>dt = unscaled seconds since the last call (0 while the window is in the background). One step never exceeds
        /// Tokens.MaxFrameSeconds: a hitch must not eat a still.</summary>
        public void Advance(float dt)
        {
            if (State == Phase.Done) return;
            previousClock = Clock;
            Clock += Mathf.Clamp(dt, 0f, tokens.MaxFrameSeconds);
            if (State == Phase.SkipFading && Clock - skipAt >= SkipFade) State = Phase.Done;
            else if (Clock >= Duration) State = Phase.Done;
        }

        /// <summary>A skip key. Inside the guard it is dropped (not remembered). After it the layer is gone within SkipFadeSeconds.</summary>
        public bool TrySkip()
        {
            if (State != Phase.Playing || Clock < tokens.GuardSeconds) return false;
            skipAlpha = PlayingAlpha(Clock); skipAt = Clock; State = Phase.SkipFading; Skipped = true;
            return true;
        }

        float PlayingAlpha(float t)
        {
            float a = 1f;
            if (FadeIn > 0f) a = Mathf.Min(a, t / FadeIn);
            if (FadeOut > 0f) a = Mathf.Min(a, (Duration - t) / FadeOut);
            return Mathf.Clamp01(a);
        }

        float SkipFactor => State == Phase.SkipFading ? (SkipFade > 0f ? Mathf.Clamp01(1f - (Clock - skipAt) / SkipFade) : 0f) : State == Phase.Done && Skipped ? 0f : 1f;

        public float LayerAlpha => State == Phase.Done ? 0f : State == Phase.SkipFading ? skipAlpha * SkipFactor : PlayingAlpha(Clock);

        public int StillAt(float t)
        {
            int index = 0;
            for (int i = 1; i < starts.Length; i++) if (t >= starts[i]) index = i;
            return index;
        }

        /// <summary>The window of still `index` at clock t, in picture space (0..1, origin top-left), before the screen-aspect crop.</summary>
        public Rect Window(int index, float t)
        {
            var still = sequence.Stills[index];
            if (reducedMotion) return still.Rect1;
            float u = still.Seconds > 0f ? Mathf.Clamp01((t - starts[index]) / still.Seconds) : 1f;
            if (still.Ease == CinematicEase.InOutSine) u = .5f - .5f * Mathf.Cos(Mathf.PI * u);
            Rect a = still.Rect0, b = still.Rect1;
            return new Rect(Mathf.Lerp(a.x, b.x, u), Mathf.Lerp(a.y, b.y, u), Mathf.Lerp(a.width, b.width, u), Mathf.Lerp(a.height, b.height, u));
        }

        public Frame Evaluate(float viewAspect, float sourceAspect)
        {
            var frame = new Frame { LayerAlpha = LayerAlpha, Back = -1, FrontAlpha = 1f };
            if (starts.Length == 0) return frame;
            float t = Mathf.Min(Clock, Duration);
            int i = StillAt(t); frame.Front = i;
            frame.FrontUv = ToUv(Fit(Window(i, t), sequence.Stills[i].Focus, sourceAspect, viewAspect));
            if (i > 0)
            {
                float fade = CrossFade(i);
                frame.FrontAlpha = fade > 0f ? Mathf.Clamp01((t - starts[i]) / fade) : 1f;
                if (frame.FrontAlpha < 1f)
                {
                    frame.Back = i - 1;
                    frame.BackUv = ToUv(Fit(Window(i - 1, t), sequence.Stills[i - 1].Focus, sourceAspect, viewAspect));
                }
            }
            return frame;
        }

        /// <summary>Gain of the looped voice `id` now: the loudest cue of that id (so the same id on neighbouring stills joins without a gap).</summary>
        public float VoiceGain(string id)
        {
            if (State == Phase.Done) return 0f;
            float best = 0f;
            for (int i = 0; i < starts.Length; i++)
                foreach (var cue in sequence.Stills[i].Cues)
                {
                    if (cue == null || !cue.Loop || cue.Id != id) continue;
                    float a = starts[i] + cue.AtSeconds, b = starts[i] + sequence.Stills[i].Seconds;
                    if (Clock < a || Clock >= b) continue;
                    float g = cue.Gain;
                    if (cue.FadeInSeconds > 0f) g *= Mathf.Clamp01((Clock - a) / cue.FadeInSeconds);
                    if (cue.FadeOutSeconds > 0f) g *= Mathf.Clamp01((b - Clock) / cue.FadeOutSeconds);
                    if (g > best) best = g;
                }
            return best * SkipFactor;
        }

        /// <summary>One-shot cues whose start lies in the step just advanced. Nothing after a skip.</summary>
        public void CollectOneShots(List<CinematicStillsCatalogSO.Cue> into)
        {
            if (State != Phase.Playing) return;
            for (int i = 0; i < starts.Length; i++)
                foreach (var cue in sequence.Stills[i].Cues)
                {
                    if (cue == null || cue.Loop) continue;
                    float a = starts[i] + cue.AtSeconds;
                    if (a > previousClock && a <= Clock) into.Add(cue);
                }
        }

        /// <summary>Crops a picture-space window to the screen's aspect around the focus: a wider screen loses top and bottom, a
        /// narrower one loses the sides. The window never grows, so the result stays inside the picture.</summary>
        public static Rect Fit(Rect window, Vector2 focus, float sourceAspect, float viewAspect)
        {
            if (window.height <= 0f || window.width <= 0f || sourceAspect <= 0f || viewAspect <= 0f) return window;
            float windowAspect = sourceAspect * window.width / window.height;
            if (viewAspect > windowAspect)
            {
                float h = window.height * windowAspect / viewAspect;
                float y = Mathf.Clamp(focus.y - h * .5f, window.y, window.y + window.height - h);
                return new Rect(window.x, y, window.width, h);
            }
            float w = window.width * viewAspect / windowAspect;
            float x = Mathf.Clamp(focus.x - w * .5f, window.x, window.x + window.width - w);
            return new Rect(x, window.y, w, window.height);
        }

        public static Rect ToUv(Rect picture) => new Rect(picture.x, 1f - picture.y - picture.height, picture.width, picture.height);
    }

    // AC-CS.1 · .3 · .4 · .5 · .7 · .20 과 8절 예산의 자료 검사. 기준 값은 자료의 Limits 다. 편집 모드 검사와 오프라인 시험이 같이 쓴다.
    public static class CinematicStillsRules
    {
        /// <summary>BC7 (8 bpp, 4 x 4 blocks), no mips, after the importer's max size [I: PC = BC7].</summary>
        public static long EstimatedBytes(int width, int height, int maxSize)
        {
            if (width <= 0 || height <= 0) return 0;
            int big = Mathf.Max(width, height);
            if (maxSize > 0 && big > maxSize) { width = Mathf.Max(1, Mathf.RoundToInt(width * (float)maxSize / big)); height = Mathf.Max(1, Mathf.RoundToInt(height * (float)maxSize / big)); }
            return (long)((width + 3) / 4) * ((height + 3) / 4) * 16;
        }

        public static int Validate(CinematicStillsCatalogSO catalog, List<string> problems, bool requireChannels)
        {
            int before = problems.Count;
            if (catalog == null) { problems.Add("catalogue is null"); return 1; }
            var k = catalog.Style; var r = catalog.Rules;
            if (k == null || r == null) { problems.Add("catalogue has no Style / Rules"); return problems.Count - before; }
            if (requireChannels && (catalog.Requested == null || catalog.Started == null || catalog.Finished == null)) problems.Add("channels: Requested / Started / Finished must all be set");
            if (requireChannels && catalog.Requested != null && (catalog.Requested == catalog.Started || catalog.Started == catalog.Finished || catalog.Requested == catalog.Finished)) problems.Add("channels: the three must be different assets");
            if (k.GuardSeconds < r.MinGuardSeconds || k.GuardSeconds > r.MaxGuardSeconds) problems.Add("tokens: GuardSeconds " + k.GuardSeconds + " outside " + r.MinGuardSeconds + " - " + r.MaxGuardSeconds);
            if (k.SkipFadeSeconds <= 0f || k.SkipFadeSeconds > r.MaxSkipFadeSeconds) problems.Add("tokens: SkipFadeSeconds " + k.SkipFadeSeconds + " must be in (0, " + r.MaxSkipFadeSeconds + "]");
            if (k.SkipKeys == null || k.SkipKeys.Length == 0) problems.Add("tokens: no skip key");
            if (k.SourceWidth <= 0 || k.SourceHeight <= 0) problems.Add("tokens: source size");
            if (k.LoadTimeoutSeconds <= 0f || k.RequestTimeoutSeconds <= k.LoadTimeoutSeconds) problems.Add("tokens: RequestTimeoutSeconds must exceed LoadTimeoutSeconds > 0");
            if (k.MaxFrameSeconds <= 0f || k.ReducedMotionFadeSeconds <= 0f || k.FadeInSeconds < 0f || k.FadeOutSeconds < 0f) problems.Add("tokens: fades / MaxFrameSeconds");
            var ids = new HashSet<string>(); long total = 0;
            long perStill = EstimatedBytes(k.SourceWidth, k.SourceHeight, r.MaxTextureSize);
            if (perStill > r.MaxStillBytes) problems.Add("budget: one still " + perStill + " B > " + r.MaxStillBytes);
            foreach (var s in catalog.Sequences ?? System.Array.Empty<CinematicStillsCatalogSO.Sequence>())
            {
                if (s == null || string.IsNullOrEmpty(s.Id)) { problems.Add("sequence without an id"); continue; }
                if (!ids.Add(s.Id)) problems.Add(s.Id + ": id used twice");
                int n = s.Stills != null ? s.Stills.Length : 0; total += perStill * n;
                if (s.Disabled) continue;
                ValidateSequence(s, k, r, problems);
            }
            if (total > r.MaxTotalBytes) problems.Add("budget: all stills " + total + " B > " + r.MaxTotalBytes);
            return problems.Count - before;
        }

        static void ValidateSequence(CinematicStillsCatalogSO.Sequence s, CinematicStillsCatalogSO.Tokens k, CinematicStillsCatalogSO.Limits r, List<string> problems)
        {
            string p = s.Id + ": "; int n = s.Stills != null ? s.Stills.Length : 0; float tol = r.RectTolerance;
            if (n < r.MinStills || n > r.MaxStills) { problems.Add(p + n + " stills, need " + r.MinStills + " - " + r.MaxStills); if (n == 0) return; }
            float total = s.TotalSeconds;
            if (total < r.MinTotalSeconds - tol || total > r.MaxTotalSeconds + tol) problems.Add(p + "total " + total + " s outside " + r.MinTotalSeconds + " - " + r.MaxTotalSeconds);
            if (EstimatedPerSequence(k, r, n) > r.MaxSequenceBytes) problems.Add(p + "budget: sequence > " + r.MaxSequenceBytes + " B");
            var t = s.Trigger;
            if (t == null || t.Kind == CinematicTriggerKind.None) problems.Add(p + "no trigger kind");
            else if (t.Kind == CinematicTriggerKind.PlaneCrossing)
            {
                if (Mathf.Abs(t.OutwardNormalXZ.magnitude - 1f) > r.NormalLengthTolerance) problems.Add(p + "trigger normal length " + t.OutwardNormalXZ.magnitude);
                if (t.HalfWidth <= 0f) problems.Add(p + "trigger HalfWidth");
                if (t.FeetYMin >= t.FeetYMax) problems.Add(p + "trigger feet band");
                if (t.PendingRadius <= 0f || t.PreloadRadius < t.PendingRadius) problems.Add(p + "trigger radii (PreloadRadius >= PendingRadius > 0)");
                if (t.MaxCrossingStep <= 0f) problems.Add(p + "trigger MaxCrossingStep");
            }
            else if ((t.Kind == CinematicTriggerKind.EncounterDetected || t.Kind == CinematicTriggerKind.EncounterDefeated) && string.IsNullOrEmpty(t.EncounterId)) problems.Add(p + "trigger EncounterId");
            if (k.FadeInSeconds > s.Stills[0].Seconds || k.FadeOutSeconds > s.Stills[n - 1].Seconds) problems.Add(p + "layer fades longer than the first / last still");
            float sourceAspect = k.SourceHeight > 0 ? k.SourceWidth / (float)k.SourceHeight : 1f;
            var stillIds = new HashSet<string>();
            for (int i = 0; i < n; i++)
            {
                var still = s.Stills[i]; if (still == null) { problems.Add(p + "still " + i + " is null"); continue; }
                string q = p + (string.IsNullOrEmpty(still.Id) ? "still " + i : still.Id) + ": ";
                if (string.IsNullOrEmpty(still.Id) || !stillIds.Add(still.Id)) problems.Add(q + "still id missing or repeated");
                if (string.IsNullOrEmpty(still.ResourcePath)) problems.Add(q + "no ResourcePath");
                if (still.Seconds < r.MinStillSeconds - tol) problems.Add(q + still.Seconds + " s < " + r.MinStillSeconds);
                if (i > 0 && (still.CrossFadeSeconds < r.MinCrossFadeSeconds - tol || still.CrossFadeSeconds > r.MaxCrossFadeSeconds + tol || still.CrossFadeSeconds >= still.Seconds)) problems.Add(q + "cross-fade " + still.CrossFadeSeconds);
                Rect a = still.Rect0, b = still.Rect1;
                foreach (var w in new[] { a, b })
                {
                    if (w.width <= 0f || w.x < -tol || w.y < -tol || w.x + w.width > 1f + tol || w.y + w.height > 1f + tol) problems.Add(q + "window leaves the picture " + w);
                    if (Mathf.Abs(w.width - w.height) > tol) problems.Add(q + "window is not the source's aspect " + w);
                }
                Vector2 ca = a.center, cb = b.center;
                float pan = Mathf.Max(Mathf.Abs(cb.x - ca.x), Mathf.Abs(cb.y - ca.y));
                if (pan > r.MaxPanFractionOfSource + tol) problems.Add(q + "pan " + pan + " > " + r.MaxPanFractionOfSource);
                float push = a.width > 0f && b.width > 0f ? Mathf.Max(a.width / b.width, b.width / a.width) : float.PositiveInfinity;
                if (push > r.MaxPush + tol) problems.Add(q + "push " + push + " > " + r.MaxPush);
                float narrow = Mathf.Min(a.width, b.width);
                if (still.Seconds > 0f && narrow > 0f && Mathf.Abs(cb.x - ca.x) / narrow / still.Seconds > r.MaxPanScreenFractionPerSecond + tol) problems.Add(q + "pan speed");
                if (s.NoLateralMotion && (Mathf.Abs(ca.x - .5f) > tol || Mathf.Abs(cb.x - .5f) > tol || Mathf.Abs(still.Focus.x - .5f) > tol))
                    problems.Add(q + "D308-28: a window or the focus is off the vertical centre axis (the still would lean to one side)");
                if (Mathf.Abs(still.Focus.x - cb.x) > b.width * r.FocusCentreWidth * .5f + tol || Mathf.Abs(still.Focus.y - cb.y) > b.height * r.FocusCentreHeight * .5f + tol)
                    problems.Add(q + "focus outside the centre of the end window");
                foreach (float aspect in r.CropAspects ?? System.Array.Empty<float>())
                {
                    Rect c = CinematicStillsTimeline.Fit(b, still.Focus, sourceAspect, aspect);
                    float fx = c.width > 0f ? (still.Focus.x - c.x) / c.width : -1f, fy = c.height > 0f ? (still.Focus.y - c.y) / c.height : -1f;
                    if (fx < r.CropEdgeMargin - tol || fx > 1f - r.CropEdgeMargin + tol || fy < r.CropEdgeMargin - tol || fy > 1f - r.CropEdgeMargin + tol) problems.Add(q + "focus near the edge at aspect " + aspect);
                }
                foreach (var limit in r.Stretch ?? System.Array.Empty<CinematicStillsCatalogSO.StretchLimit>())
                {
                    float stretch = b.width > 0f ? limit.ScreenWidth / (k.SourceWidth * b.width) : float.PositiveInfinity;
                    if (stretch > limit.MaxStretch + tol) problems.Add(q + "stretch " + stretch + " at " + limit.ScreenWidth + " px > " + limit.MaxStretch);
                }
                var loops = new HashSet<string>();
                foreach (var cue in still.Cues ?? System.Array.Empty<CinematicStillsCatalogSO.Cue>())
                {
                    if (cue == null || string.IsNullOrEmpty(cue.Id)) { problems.Add(q + "cue without an id"); continue; }
                    if (cue.AtSeconds < 0f || cue.AtSeconds >= still.Seconds) problems.Add(q + "cue " + cue.Id + " starts outside its still");
                    if (cue.Gain <= 0f || cue.Gain > r.MaxCueGain + tol) problems.Add(q + "cue " + cue.Id + " gain " + cue.Gain);
                    if (cue.FadeInSeconds < 0f || cue.FadeOutSeconds < 0f || cue.AtSeconds + cue.FadeInSeconds + cue.FadeOutSeconds > still.Seconds + tol) problems.Add(q + "cue " + cue.Id + " fades leave its still");
                    if (cue.Loop) loops.Add(cue.Id);
                }
                if (i + 1 < n && s.Stills[i + 1] != null) foreach (var cue in s.Stills[i + 1].Cues ?? System.Array.Empty<CinematicStillsCatalogSO.Cue>()) if (cue != null && cue.Loop && !string.IsNullOrEmpty(cue.Id)) loops.Add(cue.Id);
                if (loops.Count > r.MaxVoices) problems.Add(q + loops.Count + " looped voices across this still and the next > " + r.MaxVoices);
            }
        }

        static long EstimatedPerSequence(CinematicStillsCatalogSO.Tokens k, CinematicStillsCatalogSO.Limits r, int stills) => EstimatedBytes(k.SourceWidth, k.SourceHeight, r.MaxTextureSize) * stills;
    }
}
