using TMPro;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    // D308-25 (map 5, text diet): the name tag of the pointed mark. The sheet prints no place name by itself; the one mark the
    // pointer is on shows its own label on the sheet (the hanji plate beside the icon with a notation bundle, the plain label
    // without one). The tooltip that followed the pointer (PlaceHoverName) is gone: it was a second surface for the same name.
    //   pick   = the nearest centre among the marks the sheet draws right now, within MapStyle304SO.HoverRadiusPx of the
    //            pointer; distances within HoverTiePx are a tie and the higher name priority wins (the plate ranking of
    //            WorldMapPresenter.Map308.cs: the rest you wake at, rests, villages ... the dropped coins, the pin).
    //            A mark covered by the title slip or the north mark is not on show and is not picked. The heard objective's
    //            name shows when the pointer is inside its ring and no mark is picked. The current position has no name.
    //   #214   = only drawn marks are candidates, so discovery and the cave rules stay the single gate for every name.
    // The pick is recomputed after every placement (view change, wheel, R / T) and on pointer events; the labels are placed
    // again only when the pick changes. No tag while a button is held (WorldMapInputSurface hides it), outside the print
    // window, while the sheet folds, or when the page closes.
    public sealed partial class WorldMapPresenter
    {
        Vector2 mapPointer;
        Camera mapPointerCamera;
        bool mapPointerInside;
        TMP_Text pickedLabel308;                 // the label whose tag shows; null = no tag
        Vector3 marksCurrent308;                 // PlaceMarks304's last arguments (a new pick places the labels again)
        bool marksInterior308;

        bool Picked308(TMP_Text label) => label != null && ReferenceEquals(label, pickedLabel308);

        internal void MapPointer(Vector2 screen, Camera eventCamera)
        {
            mapPointer = screen;
            mapPointerCamera = eventCamera;
            mapPointerInside = true;
            RefreshMapHover();
        }

        /// <summary>Pointer left, a button went down, the page closes or hides: no tag. Touches only the picked label and its
        /// plate (it also runs from the input surface's OnDisable while the page is torn down).</summary>
        internal void HideMapHover()
        {
            mapPointerInside = false;
            var label = pickedLabel308; pickedLabel308 = null;
            if (label == null) return;
            if (label.gameObject.activeSelf) label.gameObject.SetActive(false);
            if (labelSlotOf308.TryGetValue(label, out var slot) && slot.Plate != null && slot.Plate.gameObject.activeSelf) slot.Plate.gameObject.SetActive(false);
        }

        /// <summary>After the marks were placed (ApplyUvAndMarkers) and on pointer moves.</summary>
        void RefreshMapHover()
        {
            TMP_Text pick = PickLabel308();
            if (ReferenceEquals(pick, pickedLabel308)) return;
            pickedLabel308 = pick;
            if (initialized && fullPlayer != null) PlaceMarks304(marksCurrent308, marksInterior308);
        }

        TMP_Text PickLabel308()
        {
            if (!mapPointerInside || !Visible || !targetExpanded || fold < .999f || foldMap == null || FullRoot == null || !FullRoot.gameObject.activeInHierarchy ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(foldMap, mapPointer, mapPointerCamera, out Vector2 at) || !Inside304(foldMap.rect, at, 0f)) return null;
            float k = Mathf.Max(.01f, pageScale);
            float radius = Mathf.Max(1f, mapStyle.HoverRadiusPx) * k, tie = Mathf.Max(0f, mapStyle.HoverTiePx) * k;
            int covers = 0;
            AddObstacle304(titleSlip304, ref covers);
            AddObstacle304(northLabel304, ref covers);
            AddObstacle304(northLine304, ref covers);
            TMP_Text best = null; float bestD = float.MaxValue; int bestRank = int.MaxValue;
            for (int i = 0; i < markers.Count; i++)
                Consider308(markers[i].Full, markers[i].FullLabel, LabelPriority308(markers[i]), at, radius, tie, covers, ref best, ref bestD, ref bestRank);
            Consider308(fullCheckpoint, fullCheckpointLabel, RankCheckpoint308, at, radius, tie, covers, ref best, ref bestD, ref bestRank);
            Consider308(fullDrop, fullDropLabel, RankDrop308, at, radius, tie, covers, ref best, ref bestD, ref bestRank);
            Consider308(fullPin, fullPinLabel, RankPin308, at, radius, tie, covers, ref best, ref bestD, ref bestRank);
            if (best != null) return best;
            // the heard objective: the lowest rank - only inside its ring and with no mark picked
            if (knownArea == null || knownAreaLabel == null || !knownArea.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(knownAreaLabel.text)) return null;
            Vector2 centre = foldMap.InverseTransformPoint(knownArea.position);
            float ring = knownArea.rect.width * .5f;
            return (centre - at).sqrMagnitude <= ring * ring ? knownAreaLabel : null;
        }

        void Consider308(RectTransform mark, TMP_Text label, int rank, Vector2 at, float radius, float tie, int covers, ref TMP_Text best, ref float bestD, ref int bestRank)
        {
            // Only a mark the sheet draws right now is a candidate: discovery and cave rules stay authoritative.
            if (mark == null || label == null || !mark.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(label.text)) return;
            Vector2 centre = foldMap.InverseTransformPoint(mark.position);
            float d = (centre - at).magnitude;
            if (d > radius) return;
            for (int i = 0; i < covers; i++) if (Inside304(labelObstacles304[i], centre, 0f)) return;   // under the title slip / the north mark: not on show
            bool tied = best != null && Mathf.Abs(d - bestD) <= tie;
            if (best == null || (!tied && d < bestD) || (tied && (rank < bestRank || (rank == bestRank && d < bestD)))) { best = label; bestD = d; bestRank = rank; }
        }

        // ------------------------------------------------------------------ Edit-mode preview (MapOverhaul308 preview / page): no Play, nothing saved
        /// <summary>The pointer at a point of the print window (its own px, origin at its centre), through the same path as a
        /// real pointer move. No-op in Play.</summary>
        public void PreviewPointer308(Vector2 windowPoint)
        {
            if (Application.isPlaying || !initialized || foldMap == null) return;
            var canvas = FullRoot.GetComponentInParent<Canvas>();
            Camera camera = canvas != null ? canvas.rootCanvas.worldCamera : null;
            MapPointer(RectTransformUtility.WorldToScreenPoint(camera, foldMap.TransformPoint(windowPoint)), camera);
        }

        /// <summary>Pointer out / button down. No-op in Play.</summary>
        public void PreviewPointerOff308() { if (!Application.isPlaying) HideMapHover(); }

        /// <summary>The text of the tag that shows now ("" = none).</summary>
        public string PreviewTag308 => pickedLabel308 != null ? pickedLabel308.text : "";

        /// <summary>The centre of a drawn mark in print-window px: a baked marker id, or Checkpoint / Drop / Pin / Objective.
        /// False = the sheet does not draw that mark now.</summary>
        public bool PreviewMarkPoint308(string id, out Vector2 windowPoint)
        {
            windowPoint = default;
            if (!initialized || foldMap == null || string.IsNullOrEmpty(id)) return false;
            RectTransform mark = id == "Checkpoint" ? fullCheckpoint : id == "Drop" ? fullDrop : id == "Pin" ? fullPin : id == "Objective" ? knownArea : null;
            if (mark == null) for (int i = 0; i < markers.Count && mark == null; i++) if (markers[i].Spec.Id == id) mark = markers[i].Full;
            if (mark == null || !mark.gameObject.activeInHierarchy) return false;
            windowPoint = foldMap.InverseTransformPoint(mark.position);
            return true;
        }

        /// <summary>One wheel notch (scroll &gt; 0 = in) at a point of the print window. No-op in Play.</summary>
        public void PreviewZoom308(float scroll, Vector2 windowPoint)
        {
            if (Application.isPlaying || !initialized || !targetExpanded || fold < .999f || Mathf.Abs(scroll) < .01f) return;
            ZoomAt(scroll, windowPoint);
        }

        /// <summary>A drag by window px. No-op in Play.</summary>
        public void PreviewPan308(Vector2 windowDelta) { if (!Application.isPlaying && initialized) Pan(windowDelta); }

        /// <summary>The view (world uv rect) and whether the window has the whole-world shape.</summary>
        public Rect PreviewView308 => fullUv;
        public bool PreviewWholeLayout308 => wholeWorldLayout;
        public float PreviewPageScale308 => pageScale;
    }
}
