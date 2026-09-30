using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>On the V.Veil RawImage: when the canvas is taller than the scaled 1920x1080 page (16:10, 4:3, UI 배율 changes)
    /// the veil is stretched vertically to cover the page's parent, centred on the page, so no bare world band shows above
    /// or below a menu. Horizontal coverage comes from PlaytestUiView.VeilLead. Does nothing unless the parent is a Page304
    /// (UiPageFit304). No statics.</summary>
    [DisallowMultipleComponent]
    public sealed class UiVeilFit304 : MonoBehaviour
    {
        void OnEnable() { Fit(); }
        void LateUpdate() { Fit(); }

        void Fit()
        {
            var r = transform as RectTransform;
            var page = r != null ? r.parent as RectTransform : null;
            if (page == null || page.GetComponent<UiPageFit304>() == null) return;
            var outer = page.parent as RectTransform; if (outer == null) return;
            float k = Mathf.Abs(page.localScale.y); if (k < 1e-4f) return;
            float need = Mathf.Max(UiPageFit304.Height, outer.rect.height / k + 2f);   // +2: never a hairline gap
            if (Mathf.Abs(r.sizeDelta.y - need) < .5f) return;
            r.sizeDelta = new Vector2(r.sizeDelta.x, need);
            r.anchoredPosition = new Vector2(r.anchoredPosition.x, (need - UiPageFit304.Height) * .5f);   // top = page y (1080 - need) / 2
        }
    }
}
