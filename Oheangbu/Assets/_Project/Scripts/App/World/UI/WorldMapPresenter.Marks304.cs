using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    // #304 marks on the printed window (DESIGN §5.13, map.png): cinnabar only for the current position and the heard
    // objective; everything else ink with a sheet rim (한지 테). Labels are TMP MapLabel21 / MapRegion44 (Ink_UnderPaper).
    // D14: a variant is the same symbol + an outer ink ring (the rest where you wake); 남긴 통보 uses the D04 coin symbol.
    // Also the IMapMarkerSource304 read-only view for the HUD bearing line.
    public sealed partial class WorldMapPresenter : IMapMarkerSource304
    {
        sealed class MarkerView
        {
            public WorldMapMarkerSpec Spec;
            public RectTransform Full;
            public GameObject Variant;
            public TMP_Text FullLabel;
            public float Size;
            /// <summary>The place part of the label ("청림 · 금표 주막" -> "금표 주막"); the realm goes to the card meta.</summary>
            public string Name;
        }

        readonly List<MarkerView> markers = new List<MarkerView>();
        readonly List<RectTransform> scaledMarks = new List<RectTransform>();
        RectTransform fullPlayer, fullCheckpoint, fullDrop, fullPin, knownArea;
        RectTransform knownFill;
        float knownFillFraction = .83f;
        TMP_Text fullCheckpointLabel, fullDropLabel, fullPinLabel, knownAreaLabel;
        string knownLabelFor, knownLabelText = "", pinLabelShown;
        bool knownLabelCached;

        void BuildMarks304()
        {
            var s = style; var m = mapStyle;
            // objective region first: it lies under every other mark (D47: open brush circle, or the dashed ring of map.png)
            knownArea = V.Rect("KnownDestinationArea", fullMarkers, 0, 0, m.ObjectiveMinSize, m.ObjectiveMinSize);
            knownArea.pivot = new Vector2(.5f, .5f);
            bool open = m.Objective == MapObjectiveMark304.OpenBrush && s.Sprites.Enso != null;
            float fillFraction = knownFillFraction = open ? .7f : .83f;
            knownFill = V.Rect("Fill", knownArea, 0, 0, 0, 0);
            knownFill.anchorMin = Vector2.one * (.5f - fillFraction * .5f); knownFill.anchorMax = Vector2.one * (.5f + fillFraction * .5f);
            knownFill.offsetMin = knownFill.offsetMax = Vector2.zero;
            V.Image(knownFill, UiStyle304SO.A(s.Cinnabar, .07f), s.Sprites.Disc);
            var ring = V.Image(V.Stretch("Ring", knownArea), s.Cinnabar, open ? s.Sprites.Enso : s.Sprites.RingDashed);
            if (ring.sprite == null) { ring.sprite = s.Sprites.Disc; ring.color = UiStyle304SO.A(s.Cinnabar, .22f); }
            knownArea.gameObject.SetActive(false);
            knownAreaLabel = CreateFullLabel("KnownDestination", "들은 목적 권역");

            // realm names (MapRegion44, .12em)
            if (data.HasIllustration && data.Locations != null)
                foreach (var region in data.Locations.Entries)
                {
                    if (region == null || region.Priority != 0) continue;
                    AddRegionLabel(region.Id, region.Name, region.Polygon);
                }
            if (data.HasIllustration && data.Locations == null && sheet.Regions != null)
                foreach (var region in sheet.Regions) if (region != null) AddRegionLabel(region.Id, region.Label, region.Polygon);

            // discovered places from the baked data
            foreach (WorldMapMarkerSpec spec in data.Markers ?? Array.Empty<WorldMapMarkerSpec>())
            {
                if (spec == null) continue;
                float size = MarkSize(spec.Kind);
                var root = CreateMark(spec.Id, PictureFor(spec.Kind), s.Ink, size, true);
                GameObject variant = null;
                if (KindOf(spec.Kind) == MapMarkerKind304.Rest) { variant = CreateVariantRing(root, size); variant.SetActive(false); }
                string name = ShortLabel(spec.Label);
                markers.Add(new MarkerView { Spec = spec, Full = root, Variant = variant, FullLabel = CreateFullLabel(spec.Id, name), Size = size, Name = name });
            }

            // the rest you wake at when it is not one of the baked rests (variant ring always on)
            fullCheckpoint = CreateMark("Checkpoint", PictureFor(WorldMapMarkerKind.Rest), s.Ink, m.RestSize, true);
            CreateVariantRing(fullCheckpoint, m.RestSize);
            fullCheckpointLabel = CreateFullLabel("Checkpoint", "깨어날 곳");

            // 남긴 통보: D04 coin (ink) on a sheet disc (2 px rim)
            fullDrop = V.Rect("Marker_Drop", fullMarkers, 0, 0, m.CoinSize, m.CoinSize);
            fullDrop.anchorMin = fullDrop.anchorMax = fullDrop.pivot = new Vector2(.5f, .5f);
            var dropRim = V.Image(V.Stretch("Rim", fullDrop, -2f), s.Sheet, s.Sprites.Disc); dropRim.preserveAspect = true;
            var coin = V.Image(V.Stretch("Icon", fullDrop), s.Ink, Pick(mapStyle.Coin, s.Sprites.Disc)); coin.preserveAspect = true;
            fullDrop.gameObject.SetActive(false); scaledMarks.Add(fullDrop);
            fullDropLabel = CreateFullLabel("Drop", MapKindName(MapMarkerKind304.Coin));

            // 내 표식: two ink strokes crossing (stroke_short, DESIGN: ✦ is gone)
            fullPin = V.Rect("Marker_Pin", fullMarkers, 0, 0, m.PinSize, m.PinSize);
            fullPin.anchorMin = fullPin.anchorMax = fullPin.pivot = new Vector2(.5f, .5f);
            float len = m.PinSize * 4f / 3f, x0 = (m.PinSize - len) * .5f, y0 = m.PinSize * .5f - 5f;
            V.Brush(s, fullPin, "StrokeA", StrokeClass304.Short, s.Ink, x0, y0, len, 10f, 1f, 45f);
            V.Brush(s, fullPin, "StrokeB", StrokeClass304.Short, s.Ink, x0, y0, len, 10f, 1f, -45f);
            fullPin.gameObject.SetActive(false); scaledMarks.Add(fullPin);
            fullPinLabel = CreateFullLabel("Pin", "표식");

            // 현재 위치 last: cinnabar arrow on a 1.1x sheet copy (DESIGN §5.13 주사 촉(한지 테))
            fullPlayer = V.Rect("Marker_Player", fullMarkers, 0, 0, m.PlayerSize, m.PlayerSize);
            fullPlayer.anchorMin = fullPlayer.anchorMax = fullPlayer.pivot = new Vector2(.5f, .5f);
            if (s.Sprites.Arrow != null)
            {
                var rim = V.Rect("Rim", fullPlayer, 0, 0, 0, 0); rim.anchorMin = Vector2.one * -.05f; rim.anchorMax = Vector2.one * 1.05f; rim.offsetMin = rim.offsetMax = Vector2.zero;
                V.Image(rim, s.Sheet, s.Sprites.Arrow).preserveAspect = true;
                V.Image(V.Stretch("Icon", fullPlayer), s.Cinnabar, s.Sprites.Arrow).preserveAspect = true;
            }
            else { var arrow = V.Stretch("Icon", fullPlayer).gameObject.AddComponent<WorldMapHeadingGraphic>(); arrow.color = s.Cinnabar; arrow.raycastTarget = false; }
            scaledMarks.Add(fullPlayer);
            fullPlayer.SetAsLastSibling();
            ScaleMarks304(pageScale);   // the first layout ran before the marks existed
        }

        void AddRegionLabel(string id, string name, Vector2[] polygon)
        {
            var title = V.Label(style, fullLabels, "Label_Region_" + id, name, UiType304.MapRegion44, style.Ink, 0, 0);
            var r = title.rectTransform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
            title.alignment = TextAlignmentOptions.Center;
            title.gameObject.SetActive(false);
            Vector2 centre = Vector2.zero; int n = 0;
            if (polygon != null) foreach (var p in polygon) { centre += p; n++; }
            regionLabels.Add(title); regionCentres.Add(n > 0 ? centre / n : Vector2.zero);
            regionPolygons304.Add(polygon != null && polygon.Length >= 3 ? polygon : null);
        }

        // ------------------------------------------------------------------ realm names (#304 QA)
        // map.png puts a realm name at its centre. At the default view a neighbouring realm's centre can sit on the print edge
        // (황경 at x≈628 of the 590 px window edge): the name was cut by the window and covered by the title slip. A realm
        // name now stays whole inside the window, clear of what is printed on the paper (MapTitle slip, 북); it moves the
        // least distance to get there and is dropped when the moved name would no longer stand on its own realm.
        readonly List<Vector2[]> regionPolygons304 = new List<Vector2[]>();
        readonly Rect[] labelObstacles304 = new Rect[3];
        readonly Vector3[] corners304 = new Vector3[4];
        readonly Dictionary<TMP_Text, float> measuredLabels304 = new Dictionary<TMP_Text, float>();   // label -> fontSize it was measured at

        /// <summary>#304 QA2: the marks are built under the still inactive FullRoot, and TMP measures a component that has not
        /// awoken at a tenth of its size (TextMeshProUGUI sets m_isOrthographic in Awake; before it CalculatePreferredValues
        /// scales by .1). Every realm name therefore carried a ~10x5 px sizeDelta, and the fit below cleared the title slip by
        /// the margin only: 황경 stopped at x≈704 with its first syllable still under the slip (after2/map_revealed.png).
        /// Measured once the label can wake (its map is open), then cached; the text is set once in AddRegionLabel.</summary>
        void MeasureAwake304(TMP_Text label)
        {
            if (label == null || (measuredLabels304.TryGetValue(label, out float at) && Mathf.Abs(at - label.fontSize) < .01f) || !label.transform.parent.gameObject.activeInHierarchy) return;   // re-measure after a text-scale change
            if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);   // wakes the TMP component; the caller sets the final state
            Vector2 pref = label.GetPreferredValues(label.text);
            if (pref.x < 1f || pref.y < 1f) return;
            label.rectTransform.sizeDelta = new Vector2(Mathf.Ceil(pref.x), Mathf.Ceil(pref.y));
            measuredLabels304[label] = label.fontSize;
        }

        void PlaceRegionLabels304(bool interior)
        {
            if (regionLabels.Count == 0 || foldMap == null) return;
            int obstacles = 0;
            AddObstacle304(titleSlip304, ref obstacles);
            AddObstacle304(northLabel304, ref obstacles);
            AddObstacle304(northLine304, ref obstacles);
            Rect window = foldMap.rect;
            float margin = Mathf.Max(0f, mapStyle.RegionLabelMarginPx) * pageScale;
            for (int i = 0; i < regionLabels.Count; i++)
            {
                var label = regionLabels[i];
                Vector2 n = projection.WorldToNormalized(regionCentres[i]);
                Vector2 v = new Vector2((n.x - fullUv.x) / fullUv.width, (n.y - fullUv.y) / fullUv.height);
                bool visible = !interior && targetExpanded && fold >= .995f && v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1;
                if (visible)
                {
                    MeasureAwake304(label);
                    Vector2 half = label.rectTransform.sizeDelta * (pageScale * .5f);
                    Vector2 at = new Vector2(Mathf.Lerp(window.xMin, window.xMax, v.x), Mathf.Lerp(window.yMin, window.yMax, v.y));
                    Vector2 fitted = at;
                    visible = FitLabel304(ref fitted, half, window, margin, obstacles);
                    if (visible && (fitted - at).sqrMagnitude > .25f)
                    {
                        v = new Vector2(Mathf.InverseLerp(window.xMin, window.xMax, fitted.x), Mathf.InverseLerp(window.yMin, window.yMax, fitted.y));
                        Vector2 world = projection.NormalizedToWorld(fullUv.min + Vector2.Scale(v, fullUv.size));
                        var polygon = i < regionPolygons304.Count ? regionPolygons304[i] : null;
                        visible = polygon == null || WorldMapDiscoveryGrid.Contains(polygon, world);
                    }
                }
                if (label.gameObject.activeSelf != visible) label.gameObject.SetActive(visible);
                if (!visible) continue;
                RectTransform rect = label.rectTransform;
                rect.anchorMin = rect.anchorMax = v; rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero;
                if (label.alignment != TextAlignmentOptions.Center) label.alignment = TextAlignmentOptions.Center;
            }
        }

        void AddObstacle304(RectTransform r, ref int count)
        {
            if (r == null || !r.gameObject.activeInHierarchy || count >= labelObstacles304.Length) return;
            r.GetWorldCorners(corners304);
            Vector2 a = foldMap.InverseTransformPoint(corners304[0]), b = foldMap.InverseTransformPoint(corners304[2]);
            labelObstacles304[count++] = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>Moves a label centre (print-window local px, half = half its size) the least distance so the whole label is
        /// inside the window (less the margin) and clear of every obstacle. False when there is no such place.</summary>
        bool FitLabel304(ref Vector2 centre, Vector2 half, Rect window, float margin, int obstacles)
        {
            Rect inner = Rect.MinMaxRect(window.xMin + margin + half.x, window.yMin + margin + half.y, window.xMax - margin - half.x, window.yMax - margin - half.y);
            if (inner.xMax < inner.xMin || inner.yMax < inner.yMin) return false;
            centre = new Vector2(Mathf.Clamp(centre.x, inner.xMin, inner.xMax), Mathf.Clamp(centre.y, inner.yMin, inner.yMax));
            for (int pass = 0; pass < 2; pass++)   // a push out of one obstacle may land in the other
            {
                bool clear = true;
                for (int k = 0; k < obstacles; k++)
                {
                    Rect o = Grow304(labelObstacles304[k], margin + half.x, margin + half.y);
                    if (!Inside304(o, centre, -.01f)) continue;
                    clear = false;
                    Vector2 best = centre; float bestD = float.MaxValue;
                    TryCandidate304(new Vector2(o.xMax, centre.y), centre, inner, obstacles, margin, half, ref best, ref bestD);
                    TryCandidate304(new Vector2(o.xMin, centre.y), centre, inner, obstacles, margin, half, ref best, ref bestD);
                    TryCandidate304(new Vector2(centre.x, o.yMin), centre, inner, obstacles, margin, half, ref best, ref bestD);
                    TryCandidate304(new Vector2(centre.x, o.yMax), centre, inner, obstacles, margin, half, ref best, ref bestD);
                    if (bestD == float.MaxValue) return false;
                    centre = best;
                }
                if (clear) return true;
            }
            for (int k = 0; k < obstacles; k++)
                if (Inside304(Grow304(labelObstacles304[k], margin + half.x, margin + half.y), centre, -.01f)) return false;
            return true;
        }

        void TryCandidate304(Vector2 c, Vector2 from, Rect inner, int obstacles, float margin, Vector2 half, ref Vector2 best, ref float bestD)
        {
            if (!Inside304(inner, c, .01f)) return;
            for (int k = 0; k < obstacles; k++)
                if (Inside304(Grow304(labelObstacles304[k], margin + half.x, margin + half.y), c, -.01f)) return;
            float d = (c - from).sqrMagnitude;
            if (d < bestD) { bestD = d; best = c; }
        }

        static Rect Grow304(Rect r, float x, float y) => Rect.MinMaxRect(r.xMin - x, r.yMin - y, r.xMax + x, r.yMax + y);

        /// <summary>Point in rect; tolerance &gt; 0 widens it (edges count), &lt; 0 narrows it (touching an edge is outside).</summary>
        static bool Inside304(Rect r, Vector2 p, float tolerance)
            => p.x >= r.xMin - tolerance && p.x <= r.xMax + tolerance && p.y >= r.yMin - tolerance && p.y <= r.yMax + tolerance;

        RectTransform CreateMark(string name, Sprite sprite, Color color, float size, bool sheetRim)
        {
            var root = V.Rect("Marker_" + name, fullMarkers, 0, 0, size, size);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            if (sheetRim && sprite != null)
            {
                // 한지 테: a sheet copy 1.14x behind the ink drawing (the mockup's 1.5 px #DFDBD0 halo)
                var rim = V.Rect("Rim", root, 0, 0, 0, 0); rim.anchorMin = Vector2.one * -.07f; rim.anchorMax = Vector2.one * 1.07f; rim.offsetMin = rim.offsetMax = Vector2.zero;
                V.Image(rim, UiStyle304SO.A(style.Sheet, .92f), sprite).preserveAspect = true;
            }
            V.Image(V.Stretch("Icon", root), color, sprite).preserveAspect = true;
            root.gameObject.SetActive(false);
            scaledMarks.Add(root);
            return root;
        }

        GameObject CreateVariantRing(RectTransform root, float size)
        {
            float k = Mathf.Max(1f, mapStyle.VariantRingScale);
            var ring = V.Rect("VariantRing", root, 0, 0, 0, 0);
            ring.anchorMin = Vector2.one * (.5f - k * .5f); ring.anchorMax = Vector2.one * (.5f + k * .5f); ring.offsetMin = ring.offsetMax = Vector2.zero;
            ring.SetAsFirstSibling();
            var img = V.Image(ring, style.Ink, Pick(mapStyle.VariantRing, style.Sprites.RingDashed, style.Sprites.Disc));
            if (mapStyle.VariantRing == null && style.Sprites.RingDashed == null) img.color = UiStyle304SO.A(style.Ink, .25f);
            return ring.gameObject;
        }

        TMP_Text CreateFullLabel(string name, string value)
        {
            var label = V.Label(style, fullLabels, "Label_" + name, value, UiType304.MapLabel21, style.Ink, 0, 0);
            MapLabelFace304(label);
            var r = label.rectTransform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
            label.gameObject.SetActive(false);
            return label;
        }

        /// <summary>#304 QA: place names on the sheet in the Serif 800 face. MapLabel21 is "Serif 700 21" (DESIGN §5.13), but the
        /// foundation resolves Serif700 to Noto Serif KR 600 (no static 700), and at 21 px with the sheet underlay that face
        /// printed about 2.5x less ink per syllable than map.png's 700 (thin grey names). Size, tracking and the Ink_UnderPaper
        /// preset stay the role's; without the 800 face or its preset material the role face is kept.</summary>
        void MapLabelFace304(TMP_Text label)
        {
            if (label == null || style == null) return;
            var heavy = style.FontFor(UiFont304.Serif800);
            if (heavy == null || heavy == label.font) return;
            var material = style.TmpMaterial(heavy, TmpPreset304.Ink_UnderPaper);
            if (material == null) return;
            label.font = heavy;
            label.fontSharedMaterial = material;
            var role = style.Role(UiType304.MapLabel21);
            if (role != null) label.lineSpacing = role.TmpLineSpacing(heavy);
            if (!string.IsNullOrEmpty(label.text))
            {
                var pref = label.GetPreferredValues(label.text);
                label.rectTransform.sizeDelta = new Vector2(Mathf.Ceil(pref.x), Mathf.Ceil(pref.y));
            }
        }

        /// <summary>Baked labels carry their realm ("청림 · 금표 주막"); the map, the list and the HUD show the place only
        /// (map.png), the realm is on the detail card's meta line.</summary>
        static string ShortLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return label ?? "";
            int i = label.LastIndexOf(" · ", StringComparison.Ordinal);
            return i >= 0 && i + 3 < label.Length ? label.Substring(i + 3) : label;
        }

        static void SetLabelText(TMP_Text label, string value)
        {
            if (label == null || label.text == value) return;
            label.text = value ?? "";
            var pref = label.GetPreferredValues(label.text);
            label.rectTransform.sizeDelta = new Vector2(Mathf.Ceil(pref.x), Mathf.Ceil(pref.y));
        }

        void ScaleMarks304(float k)
        {
            var scale = new Vector3(k, k, 1f);
            foreach (var r in scaledMarks) if (r != null) r.localScale = scale;
            if (fullLabels != null) foreach (Transform t in fullLabels) t.localScale = scale;
        }

        string MapKindName(MapMarkerKind304 kind) => mapStyle.Kind(kind).Name;

        /// <summary>The baked rest the checkpoint belongs to (D14 variant), or null when the checkpoint stands alone.</summary>
        MarkerView CheckpointRest(Vector2 checkpoint)
        {
            float snap = mapStyle.CheckpointSnapMetres * mapStyle.CheckpointSnapMetres;
            MarkerView best = null; float bestD = float.MaxValue;
            foreach (var marker in markers)
            {
                if (KindOf(marker.Spec.Kind) != MapMarkerKind304.Rest) continue;
                float d = (marker.Spec.WorldXZ - checkpoint).sqrMagnitude;
                if (d <= snap && d < bestD) { best = marker; bestD = d; }
            }
            return best;
        }

        bool MarkerKnown(MarkerView marker)
            => session.Progress != null && session.Progress.ui != null &&
               (marker.Spec.InitiallyDiscovered || (session.Progress.ui.discoveredMarkers != null && session.Progress.ui.discoveredMarkers.Contains(marker.Spec.Id)));

        string KnownLabel(Oheangbu.Data.Demo.DemoCampaignProfile.Stage stage)
        {
            if (stage == null) return "";
            if (!knownLabelCached || !ReferenceEquals(knownLabelFor, stage.DestinationLabel))
            { knownLabelCached = true; knownLabelFor = stage.DestinationLabel; knownLabelText = string.IsNullOrEmpty(stage.DestinationLabel) ? MapKindName(MapMarkerKind304.Objective) : stage.DestinationLabel + " 일대"; }
            return knownLabelText;
        }

        void PlaceMarks304(Vector3 current, bool interior)
        {
            if (fullPlayer == null) return;
            bool labels = fullUv.width < mapStyle.LabelMaxViewWidth;
            Vector2 currentXZ = new Vector2(current.x, current.z);
            PlaceFull(fullPlayer, currentXZ, true);
            float yaw = 0f;
            if (session.Walker != null)
            {
                if (session.Walker.Seated && session.Walker.ViewCamera != null) yaw = session.Walker.ViewCamera.transform.eulerAngles.y;
                else if (session.Walker.Body != null) yaw = session.Walker.Body.transform.eulerAngles.y;
            }
            fullPlayer.localEulerAngles = new Vector3(0, 0, -yaw);

            bool ready = session.Progress != null && session.Progress.ui != null;
            Vector2 checkpoint = ready ? WorldMapGeometry.XZ(session.Progress.ledger.checkpointPosition) : default;
            MarkerView checkpointRest = ready && !interior ? CheckpointRest(checkpoint) : null;
            if (checkpointRest != null && !MarkerKnown(checkpointRest)) checkpointRest = null;
            bool checkpointAlone = ready && !interior && checkpointRest == null && (checkpoint - currentXZ).sqrMagnitude > 144f;
            PlaceFull(fullCheckpoint, checkpoint, checkpointAlone);
            PlaceFullLabel(fullCheckpointLabel, checkpoint, checkpointAlone && labels, new Vector2(mapStyle.RestSize * .5f + 6f, 0f));

            bool hasDrop = ready && session.Progress.ledger.dropCurrency > 0;
            Vector2 drop = hasDrop ? WorldMapGeometry.XZ(session.Progress.ledger.dropPosition) : default;
            PlaceFull(fullDrop, drop, hasDrop && !interior);
            PlaceFullLabel(fullDropLabel, drop, hasDrop && !interior && labels, new Vector2(mapStyle.CoinSize * .5f + 6f, 0f));

            bool hasPin = ready && session.Progress.ui.pin.active;
            Vector2 pin = hasPin ? session.Progress.ui.pin.worldXZ : default;
            if (hasPin && !ReferenceEquals(pinLabelShown, session.Progress.ui.pin.label))
            { pinLabelShown = session.Progress.ui.pin.label; SetLabelText(fullPinLabel, string.IsNullOrWhiteSpace(pinLabelShown) ? "표식" : pinLabelShown); }
            PlaceFull(fullPin, pin, hasPin && !interior);
            PlaceFullLabel(fullPinLabel, pin, hasPin && !interior && labels, new Vector2(mapStyle.PinSize * .5f + 6f, 0f));

            var destination = session.KnownDestination;
            bool showDestination = destination != null && !interior;
            var destinationXZ = showDestination ? new Vector2(destination.Destination.x, destination.Destination.z) : Vector2.zero;
            PlaceFull(knownArea, destinationXZ, showDestination);
            float ringSize = 0f;
            if (showDestination)
            {
                float worldW = data.BoundsMax.x - data.BoundsMin.x, worldH = data.BoundsMax.y - data.BoundsMin.y;
                float w = 2f * destination.DestinationRadius / worldW / fullUv.width * fullMarkers.rect.width;
                float h = 2f * destination.DestinationRadius / worldH / fullUv.height * fullMarkers.rect.height;
                ringSize = Mathf.Max(mapStyle.ObjectiveMinSize * pageScale, Mathf.Min(w, h) / Mathf.Max(.1f, knownFillFraction));
                knownArea.sizeDelta = new Vector2(ringSize, ringSize);
                SetLabelText(knownAreaLabel, KnownLabel(destination));
            }
            PlaceCentredLabel(knownAreaLabel, destinationXZ, showDestination, -(ringSize * .5f + 14f * pageScale));

            foreach (MarkerView marker in markers)
            {
                bool known = !interior && MarkerKnown(marker);
                if (marker.Variant != null && marker.Variant.activeSelf != (marker == checkpointRest)) marker.Variant.SetActive(marker == checkpointRest);
                PlaceFull(marker.Full, marker.Spec.WorldXZ, known);
                PlaceFullLabel(marker.FullLabel, marker.Spec.WorldXZ, known && labels, new Vector2(marker.Size * .5f + 5f, 0f));
            }
            PlaceRegionLabels304(interior);
        }

        /// <summary>#304 detail for a CollectMarkers entry (MapMarker304 has no sub-kind): the baked kind of the place at that
        /// spot (Place / Settlement / Gate / Mountain / Rest), so the bearing line can pick its D13 pictogram without guessing
        /// from the label. False for entries that are not baked places (objective, coin, pin, a stand-alone checkpoint).</summary>
        public bool TryDetailKind304(MapMarker304 marker, out WorldMapMarkerKind kind)
        {
            var xz = new Vector2(marker.World.x, marker.World.z);
            foreach (var m in markers)
                if ((m.Spec.WorldXZ - xz).sqrMagnitude < .25f) { kind = m.Spec.Kind; return true; }
            kind = WorldMapMarkerKind.Place; return false;
        }

        // ------------------------------------------------------------------ IMapMarkerSource304 (HUD bearing line)
        /// <summary>True inside caves and interiors (the bearing line switches polarity).</summary>
        public bool IsIndoor => initialized && data != null && data.ZoneAt(CurrentWorld()) != null;

        /// <summary>Appends what the player knows: discovered places and rests (the checkpoint rest when it stands alone),
        /// the heard objective, the dropped coins and the pin. y = the player's height (bearings are horizontal).
        /// Allocation-free for the per-frame HUD.</summary>
        public int CollectMarkers(List<MapMarker304> into)
        {
            if (into == null || !initialized || session == null || session.Progress == null || session.Progress.ui == null) return 0;
            int before = into.Count;
            Vector3 here = CurrentWorld();
            float y = here.y;
            foreach (var marker in markers)
                if (MarkerKnown(marker))
                    into.Add(new MapMarker304 { Kind = KindOf(marker.Spec.Kind), World = new Vector3(marker.Spec.WorldXZ.x, y, marker.Spec.WorldXZ.y), Label = marker.Name });
            var ledger = session.Progress.ledger;
            if (ledger != null)
            {
                Vector2 checkpoint = WorldMapGeometry.XZ(ledger.checkpointPosition);
                var rest = CheckpointRest(checkpoint);
                if (rest == null || !MarkerKnown(rest))
                    into.Add(new MapMarker304 { Kind = MapMarkerKind304.Rest, World = new Vector3(checkpoint.x, y, checkpoint.y), Label = fullCheckpointLabel != null ? fullCheckpointLabel.text : "" });
                if (ledger.dropCurrency > 0)
                    into.Add(new MapMarker304 { Kind = MapMarkerKind304.Coin, World = new Vector3(ledger.dropPosition.x, y, ledger.dropPosition.z), Label = MapKindName(MapMarkerKind304.Coin) });
            }
            var pin = session.Progress.ui.pin;
            if (pin != null && pin.active)
                into.Add(new MapMarker304 { Kind = MapMarkerKind304.Pin, World = new Vector3(pin.worldXZ.x, y, pin.worldXZ.y), Label = string.IsNullOrWhiteSpace(pin.label) ? "표식" : pin.label });
            var destination = session.KnownDestination;
            if (destination != null)
                into.Add(new MapMarker304 { Kind = MapMarkerKind304.Objective, World = destination.Destination, Label = KnownLabel(destination) });
            return into.Count - before;
        }
    }
}
