using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    public sealed partial class WorldMapPresenter
    {
        Text mapHover;
        Vector2 mapPointer;
        Camera mapPointerCamera;
        bool mapPointerInside;

        void BuildMapHover()
        {
            // Overlay coordinates keep the type size independent of the geographic zoom.
            mapHover = PlaytestUiView.Text(FullRoot, "PlaceHoverName", "", Font, 20,
                dependencies.Paper, 0, 0, 300, 32, TextAnchor.MiddleLeft);
            mapHover.raycastTarget = false;
            mapHover.supportRichText = false;
            mapHover.horizontalOverflow = HorizontalWrapMode.Overflow;
            mapHover.verticalOverflow = VerticalWrapMode.Overflow;
            var shadow = mapHover.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color(.09f, .085f, .07f, .95f);
            shadow.effectDistance = new Vector2(1, -1);
            var rect = mapHover.rectTransform;
            rect.anchorMin = rect.anchorMax = FullRoot.pivot;
            rect.pivot = Vector2.zero;
            mapHover.gameObject.SetActive(false);
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
            if (mapHover != null) mapHover.gameObject.SetActive(false);
        }

        void RefreshMapHover()
        {
            if (mapHover == null) return;
            if (!mapPointerInside || !Visible || !targetExpanded || fold < .999f ||
                !FullRoot.gameObject.activeInHierarchy ||
                !RectTransformUtility.RectangleContainsScreenPoint(foldMap, mapPointer, mapPointerCamera))
            { mapHover.gameObject.SetActive(false); return; }

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
            { mapHover.gameObject.SetActive(false); return; }
            mapHover.text = nearest.Spec.Label;
            float width = Mathf.Min(mapHover.preferredWidth + 4, FullRoot.rect.width - 24);
            var rect = mapHover.rectTransform;
            rect.sizeDelta = new Vector2(width, 32);
            Rect bounds = FullRoot.rect;
            rect.anchoredPosition = new Vector2(
                Mathf.Clamp(local.x + 18, bounds.xMin + 12, bounds.xMax - width - 12),
                Mathf.Clamp(local.y + 20, bounds.yMin + 12, bounds.yMax - 44));
            mapHover.gameObject.SetActive(true);
        }
    }
}
