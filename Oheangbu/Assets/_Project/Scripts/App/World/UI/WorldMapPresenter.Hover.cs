using TMPro;
using UnityEngine;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    public sealed partial class WorldMapPresenter
    {
        // #304 (IMPLEMENTATION §7.10): PlaceHoverName = TMP MapLabel21 Ink_UnderPaper on a paper wet_s underlay (h56).
        const float HoverH = 56f, HoverPadX = 30f;
        RectTransform mapHoverRoot;
        Image mapHoverUnderlay;
        TMP_Text mapHover;
        Vector2 mapPointer;
        Camera mapPointerCamera;
        bool mapPointerInside;

        void BuildMapHover()
        {
            // Overlay coordinates keep the type size independent of the geographic zoom.
            mapHoverRoot = V.Rect("PlaceHoverName", FullRoot, 0, 0, 300, HoverH);
            mapHoverRoot.anchorMin = mapHoverRoot.anchorMax = FullRoot.pivot;
            mapHoverRoot.pivot = Vector2.zero;
            mapHoverUnderlay = V.Stroke(style, mapHoverRoot, "Underlay", StrokeClass304.WetS, style.Paper, 0f, 0f, HoverH, 200f);
            mapHover = V.Label(style, mapHoverRoot, "Label", "", UiType304.MapLabel21, style.Ink, HoverPadX, 0f, 0f, HoverH, TextAlignmentOptions.Left);
            mapHover.richText = false;
            MapLabelFace304(mapHover);   // #304 QA: same face as the place names on the sheet (the text is empty here: size kept)            mapHoverRoot.gameObject.SetActive(false);
        }

        internal void MapPointer(Vector2 screen, Camera eventCamera)
        {
            mapPointer = screen;
            mapPointerCamera = eventCamera;
            mapPointerInside = true;
            RefreshMapHover();
        }

        internal void HideMapHover()
        {
            mapPointerInside = false;
            if (mapHoverRoot != null) mapHoverRoot.gameObject.SetActive(false);
        }

        void RefreshMapHover()
        {
            if (mapHoverRoot == null) return;
            if (!mapPointerInside || !Visible || !targetExpanded || fold < .999f ||
                !FullRoot.gameObject.activeInHierarchy ||
                !RectTransformUtility.RectangleContainsScreenPoint(foldMap, mapPointer, mapPointerCamera))
            { mapHoverRoot.gameObject.SetActive(false); return; }

            MarkerView nearest = null;
            float distance = float.MaxValue;
            // Only the currently rendered markers are eligible: discovery and cave rules stay authoritative.
            foreach (var marker in markers)
            {
                if (!marker.Full.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(marker.Spec.Label) ||
                    !RectTransformUtility.RectangleContainsScreenPoint(marker.Full, mapPointer, mapPointerCamera)) continue;
                float d = (RectTransformUtility.WorldToScreenPoint(mapPointerCamera, marker.Full.position) - mapPointer).sqrMagnitude;
                if (d < distance) { nearest = marker; distance = d; }
            }
            if (nearest == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    FullRoot, mapPointer, mapPointerCamera, out Vector2 local))
            { mapHoverRoot.gameObject.SetActive(false); return; }
            // #304 QA2: active before measuring. The name label was built under the inactive FullRoot and TMP measures a
            // component that has not awoken at a tenth of its size, so the first name shown got a ~1/10 underlay.
            if (!mapHoverRoot.gameObject.activeSelf) mapHoverRoot.gameObject.SetActive(true);
            if (mapHover.text != nearest.Name)
            {
                mapHover.text = nearest.Name;
                float textW = Mathf.Ceil(mapHover.GetPreferredValues(mapHover.text).x);
                mapHover.rectTransform.sizeDelta = new Vector2(textW, HoverH);
                float underW = V.StrokeWidth(style, StrokeClass304.WetS, 0f, HoverH, HoverPadX + textW + 18f);
                mapHoverUnderlay.rectTransform.sizeDelta = new Vector2(underW, HoverH);
                mapHoverUnderlay.rectTransform.anchoredPosition = new Vector2(underW * .5f, -HoverH * .5f);
                mapHoverRoot.sizeDelta = new Vector2(HoverPadX + textW + 18f, HoverH);
            }
            float k = pageScale;
            mapHoverRoot.localScale = new Vector3(k, k, 1f);
            float width = mapHoverRoot.sizeDelta.x * k, height = HoverH * k;
            Rect bounds = FullRoot.rect;
            mapHoverRoot.anchoredPosition = new Vector2(
                Mathf.Clamp(local.x + 18, bounds.xMin + 12, bounds.xMax - width - 12),
                Mathf.Clamp(local.y + 20, bounds.yMin + 12, bounds.yMax - height - 12));
            mapHoverRoot.gameObject.SetActive(true);
        }
    }
}
