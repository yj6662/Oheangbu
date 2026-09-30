using UnityEngine;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 락온 일원상 (DESIGN §5.14, REFERENCE D02 two stages). Idle: enso 52 px α.45 so the target's silhouette and
    /// wind-up stay readable. Once groggy starts to build: full size (LockOn.EnsoSize 80) α1 and the inner 38 px disc (ink α.6)
    /// fills bottom-up. Full groggy: the enso's gap closes (enso_closed sprite) and it pops 1.12 -> 1 once (EnsoPopMs); reduced
    /// motion = no pop, instant stage changes. Paper enso_rim behind the cinnabar enso. The GameObject is "Reticle" (harness path
    /// HUD_Canvas/Reticle); HudController moves it and toggles it, this component only draws its state.</summary>
    public sealed class HudLockOn304 : MonoBehaviour
    {
        UiStyle304SO s;
        HudTokens304 k;
        RectTransform root, groggyRect;
        CanvasGroup group;
        Image rim, enso, groggy;
        Sprite ensoOpen, rimOpen;
        float groggy01, size, alpha, pulse, popAge = -1f;
        bool full, primed;

        public float Size => size;
        public float Alpha => alpha;
        public bool GapClosed => full;
        public Image Enso => enso;
        public Image GroggyFill => groggy;

        /// <summary>Builds "Reticle" (centre pivot) with EnsoRim, OneStrokeRing (the enso) and GroggyInkFill under `parent`.
        /// fallbackRing / fallbackDisc are used when the style has no enso / disc sprite (setup not run).</summary>
        public static HudLockOn304 Create(UiStyle304SO s, HudTokens304 k, Transform parent, Sprite fallbackRing, Sprite fallbackDisc)
        {
            s = s != null ? s : UiStyle304SO.Fallback; k = k != null ? k : HudTokens304.Load();
            var root = V.Rect("Reticle", parent, 0f, 0f, s.LockOn.EnsoSize, s.LockOn.EnsoSize);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            var lockOn = root.gameObject.AddComponent<HudLockOn304>();
            lockOn.s = s; lockOn.k = k; lockOn.root = root;
            lockOn.group = root.gameObject.AddComponent<CanvasGroup>();
            lockOn.group.blocksRaycasts = false; lockOn.group.interactable = false;
            // groggy disc first (inside the ring), then paper rim, then the cinnabar enso on top
            var disc = s.Sprites.Disc != null ? s.Sprites.Disc : fallbackDisc;
            // QA1: ink α.6 at its sRGB (mockup) weight in the Linear project. QA2: on the shared UI/InkReveal material (fully drawn)
            // so the foundation remap does it; the plain image lost the QA1 compensation when HudTokens304.LinearInkGamma went to 1.
            lockOn.groggy = V.Image(Centred("GroggyInkFill", root, s.LockOn.GroggySize), UiStyle304SO.A(s.Ink, 0f), disc);
            InkRevealEffect.On(lockOn.groggy, s, InkRevealMode.Bleed, 1f);
            lockOn.groggy.color = UiStyle304SO.A(s.Ink, k.InkAlphaFor(lockOn.groggy.material, s.LockOn.GroggyAlpha));
            lockOn.groggy.type = Image.Type.Filled; lockOn.groggy.fillMethod = Image.FillMethod.Vertical;
            lockOn.groggy.fillOrigin = (int)Image.OriginVertical.Bottom; lockOn.groggy.fillAmount = 0f;
            lockOn.groggyRect = lockOn.groggy.rectTransform;
            lockOn.rimOpen = s.Sprites.EnsoRim;
            if (lockOn.rimOpen != null)
            {
                lockOn.rim = V.Image(V.Stretch("EnsoRim", root), s.Paper, lockOn.rimOpen);
                lockOn.rim.preserveAspect = true;
            }
            lockOn.ensoOpen = s.Sprites.Enso != null ? s.Sprites.Enso : fallbackRing;
            lockOn.enso = V.Image(V.Stretch("OneStrokeRing", root), s.Sprites.Enso != null ? s.Cinnabar : s.Ink, lockOn.ensoOpen);
            lockOn.enso.preserveAspect = true;
            lockOn.size = k.IdleEnsoSize; lockOn.alpha = k.IdleAlpha;
            lockOn.Apply(1f);
            return lockOn;
        }

        static RectTransform Centred(string name, Transform parent, float size)
        {
            var r = V.Rect(name, parent, 0f, 0f, size, size);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f); r.anchoredPosition = Vector2.zero;
            return r;
        }

        public void SetGroggy01(float value)
        {
            groggy01 = Mathf.Clamp01(value);
            if (groggy != null) groggy.fillAmount = groggy01;
        }

        /// <summary>Parry response (CombatLoopWiring.PulseReticle): a short scale kick, off under reduced motion.</summary>
        public void Pulse() { if (!UiTween304.ReducedMotion) pulse = 1f; }

        /// <summary>Called when the lock-on target changes or appears: jump to the current stage instead of tweening from an old one.</summary>
        public void Snap() { primed = false; }

        void OnEnable() { primed = false; }

        void LateUpdate()
        {
            bool reduced = UiTween304.ReducedMotion;
            bool engaged = groggy01 > .001f;
            bool nowFull = groggy01 >= .999f;
            float targetSize = engaged ? s.LockOn.EnsoSize : k.IdleEnsoSize;
            float targetAlpha = engaged ? 1f : k.IdleAlpha;
            float dt = Time.unscaledDeltaTime, sec = Mathf.Max(.001f, s.Motion.EnsoPopMs / 1000f);
            if (!primed || reduced) { size = targetSize; alpha = targetAlpha; }
            else
            {
                size = Mathf.MoveTowards(size, targetSize, Mathf.Abs(s.LockOn.EnsoSize - k.IdleEnsoSize) / sec * dt);
                alpha = Mathf.MoveTowards(alpha, targetAlpha, Mathf.Abs(1f - k.IdleAlpha) / sec * dt);
            }
            if (nowFull != full)
            {
                full = nowFull;
                if (enso != null) enso.sprite = full && k.EnsoClosed != null ? k.EnsoClosed : ensoOpen;
                if (rim != null) rim.sprite = full && k.EnsoClosedRim != null ? k.EnsoClosedRim : rimOpen;
                popAge = full && primed && !reduced ? 0f : -1f;
            }
            primed = true;
            float pop = 1f;
            if (popAge >= 0f)
            {
                popAge += dt;
                float t = popAge / sec;
                if (t >= 1f) popAge = -1f;
                else pop = Mathf.LerpUnclamped(s.Motion.EnsoPopScale, 1f, UiEase304.Eval(s.Motion.Lift, t));
            }
            pulse = Mathf.Max(0f, pulse - dt * 4f);
            Apply(pop * (1f + k.PulseScale * pulse * pulse));
        }

        void Apply(float scale)
        {
            root.sizeDelta = new Vector2(size, size);
            float g = size / Mathf.Max(1f, s.LockOn.EnsoSize) * s.LockOn.GroggySize;
            groggyRect.sizeDelta = new Vector2(g, g);
            group.alpha = alpha;
            root.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
