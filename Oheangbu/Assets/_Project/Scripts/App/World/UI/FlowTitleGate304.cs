using System;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>On the title card (#304 title): keeps its CanvasGroup out of keyboard / pad navigation while something covers the
    /// title (a menu page opened from the title, a confirm dialog, the journey loading overlay). Uses FocusMark304.Suspend, so the
    /// rows keep their default look (a suspended row is not "disabled") and a selection inside the card is released.
    /// #304 QA: while a full menu page (설정 / 조작) is open over the title the card is also faded out (alpha -> 0 over the veil's
    /// wipe time, raycasts off): the page veil is α.95 in linear blending, so the lockup, the menu rows and the save seal used to
    /// ghost through the page's sub-tab column (after_lobby/title_options.png). A confirm dialog does NOT hide it (confirm.png:
    /// the card keeps its default look under the α.66 dim). The predicates are supplied by PlaytestUiRoot (Page, confirmation,
    /// Busy). No statics.</summary>
    [DisallowMultipleComponent]
    public sealed class FlowTitleGate304 : MonoBehaviour
    {
        CanvasGroup group;
        Func<bool> allow, hide;
        float fadeSeconds;
        bool hidden;   // the gate owns the card's alpha until it is fully back (a BleedIn may not run then)
        float shown = 1f;

        public CanvasGroup Group => group;
        /// <summary>True while a page covers the title and the gate holds the card faded out (or fading back in).</summary>
        public bool Hidden => hidden;

        public void Bind(CanvasGroup target, Func<bool> allowWhen) => Bind(target, allowWhen, null, 0f);

        /// <summary>hideWhen = the card is covered by a full page (fade to 0 over fadeSeconds of unscaled time, 0 = at once).</summary>
        public void Bind(CanvasGroup target, Func<bool> allowWhen, Func<bool> hideWhen, float fadeSeconds)
        {
            group = target; allow = allowWhen; hide = hideWhen; this.fadeSeconds = Mathf.Max(0f, fadeSeconds);
            Refresh(0f, true);
        }

        void LateUpdate() { Refresh(Time.unscaledDeltaTime, false); }

        void Refresh(float unscaledDelta, bool instant)
        {
            if (group == null) return;
            if (allow != null)
            {
                bool on = allow();
                if (group.interactable != on)
                {
                    if (on) group.interactable = true;
                    else FocusMark304.Suspend(group, true);
                }
            }
            bool covered = hide != null && hide();
            if (!covered && !hidden) return;   // the card's own tweens (BleedIn) own the alpha
            bool now = instant || fadeSeconds <= 0f || !Application.isPlaying;
            float step = now ? 1f : unscaledDelta / fadeSeconds;
            if (covered)
            {
                if (!hidden) { hidden = true; shown = group.alpha; }
                if (group.blocksRaycasts) group.blocksRaycasts = false;
                // the gate's own value, written every LateUpdate while covered: a BleedIn still running from the first build
                // (or its last apply(1)) cannot flash the card back for a frame
                shown = Mathf.MoveTowards(shown, 0f, step);
                group.alpha = shown;
                return;
            }
            if (!group.blocksRaycasts) group.blocksRaycasts = true;
            shown = Mathf.MoveTowards(shown, 1f, step);
            group.alpha = shown;
            if (shown >= 1f) hidden = false;
        }
    }
}
