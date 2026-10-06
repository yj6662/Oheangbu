using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 brush strips of one map view (SPEC-MAP-OVERHAUL-308 §6.2): roads, ridges, cliffs, walls, streams, bridges,
    /// ticks and site lines as ONE uGUI graphic on the stroke atlas (UI/MapStroke308), under its own nested Canvas so the other
    /// HUD / page graphics are never re-batched with it. The mesh holds the strips of the 250 m bins the view touches (plus a
    /// margin) in px around `Origin`; it is rebuilt only when the view leaves the loaded bins, the scale changes, or a stroke
    /// state opens (a discovered shortcut). Moving the window is the caller's RectTransform move; newly walked land needs no
    /// rebuild (the shader reads the shared fog texture). No Mask / RectMask2D: the shader clips to the window.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MapStrokes308Graphic : MaskableGraphic
    {
        const AdditionalCanvasShaderChannels Channels = AdditionalCanvasShaderChannels.TexCoord1;

        MapStrokes308Data data;
        MapNotation308SO notation;
        Texture atlas;
        readonly MapStripBuffer308 buffer = new MapStripBuffer308();
        readonly List<MapStrokes308Mesh.Range> ranges = new List<MapStrokes308Mesh.Range>();
        readonly MapStripStyle308[] styles = new MapStripStyle308[MapStrokes308Mesh.StyleSlots];
        Vector2 origin;
        float pxPerMetre = -1f;
        int loadedX0, loadedZ0, loadedX1, loadedZ1, stateRevision = int.MinValue;
        bool loaded;

        /// <summary>World point (m) that sits at this graphic's pivot.</summary>
        public Vector2 Origin => origin;
        public float PixelsPerMetre => pxPerMetre;
        public int Band { get; private set; } = -1;
        /// <summary>Mesh rebuilds since Bind (AC-P1 reads it).</summary>
        public int Uploads { get; private set; }
        public int VertexCount => buffer.VertexCount;
        public int StripCount => buffer.Strips;
        public int DroppedStrips => buffer.DroppedStrips;
        public double LastBuildMilliseconds { get; private set; }
        public double MeanBuildMilliseconds { get; private set; }
        public int LoadedBins => loaded ? (loadedX1 - loadedX0 + 1) * (loadedZ1 - loadedZ0 + 1) : 0;

        public override Texture mainTexture => atlas != null ? atlas : s_WhiteTexture;

        /// <summary>A rect with its own nested Canvas (no override sorting: it draws in hierarchy order) and the strip graphic
        /// under a pivot rect: the caller turns `pivot` (follow view) and moves the graphic (window travel).</summary>
        public static MapStrokes308Graphic CreateHost(string name, Transform parent, out RectTransform host, out RectTransform pivot)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            host = (RectTransform)go.transform;
            host.anchorMin = host.anchorMax = host.pivot = new Vector2(.5f, .5f);
            host.anchoredPosition = Vector2.zero; host.sizeDelta = Vector2.zero;
            var canvas = go.GetComponent<Canvas>();
            canvas.overrideSorting = false;
            canvas.additionalShaderChannels |= Channels;
            pivot = Child("Pivot", host);
            var strips = Child("Strips", pivot).gameObject.AddComponent<MapStrokes308Graphic>();
            strips.raycastTarget = false;
            return strips;
        }

        static RectTransform Child(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = Vector2.zero; r.sizeDelta = Vector2.zero;
            return r;
        }

        public void Bind(MapStrokes308Data strokes, MapNotation308SO bundle, Material strokeMaterial)
        {
            data = strokes; notation = bundle;
            atlas = bundle != null ? bundle.StrokeAtlas : null;
            material = strokeMaterial;
            raycastTarget = false;
            loaded = false; pxPerMetre = -1f; stateRevision = int.MinValue; Band = -1;
            buffer.Clear();
            EnsureChannels();
            SetAllDirty();
        }

        protected override void OnEnable() { base.OnEnable(); EnsureChannels(); }
        protected override void OnCanvasHierarchyChanged() { base.OnCanvasHierarchyChanged(); EnsureChannels(); }

        void EnsureChannels()
        {
            Canvas owner = canvas;
            if (owner == null) return;
            if ((owner.additionalShaderChannels & Channels) != Channels) owner.additionalShaderChannels |= Channels;
            Canvas root = owner.rootCanvas;
            if (root != null && root != owner && (root.additionalShaderChannels & Channels) != Channels) root.additionalShaderChannels |= Channels;
        }

        /// <summary>Makes sure the strips of `view` (world metres) are in the mesh at `pixelsPerMetre`. True when the mesh was
        /// rebuilt: then Origin has moved and the caller must place the graphic again. `margin` grows the loaded rectangle so a
        /// rebuild is needed only after the view has travelled past it. `extras` are run-time trail polylines (discovered
        /// shortcuts the bake does not carry), drawn as the Trail class.</summary>
        public bool Show(Rect view, float margin, float pixelsPerMetre, bool[] stateOpen, int statesRevision, IReadOnlyList<Vector2[]> extras)
        {
            if (data == null || notation == null || pixelsPerMetre <= 0f) return false;
            data.BinRange(view.xMin, view.yMin, view.xMax, view.yMax, out int x0, out int z0, out int x1, out int z1);
            bool inside = loaded && x0 >= loadedX0 && x1 <= loadedX1 && z0 >= loadedZ0 && z1 <= loadedZ1;
            if (inside && Mathf.Abs(pixelsPerMetre - pxPerMetre) <= pxPerMetre * 1e-4f && statesRevision == stateRevision) return false;

            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            data.BinRange(view.xMin - margin, view.yMin - margin, view.xMax + margin, view.yMax + margin, out loadedX0, out loadedZ0, out loadedX1, out loadedZ1);
            origin = view.center; pxPerMetre = pixelsPerMetre; stateRevision = statesRevision; loaded = true;
            Band = notation.Band(1f / pixelsPerMetre);
            Resolve(notation, Band, pxPerMetre, styles);
            bool whole = loadedX0 == 0 && loadedZ0 == 0 && loadedX1 == data.BinsX - 1 && loadedZ1 == data.BinsZ - 1;
            if (whole) MapStrokes308Mesh.CollectAll(data, ranges);
            else MapStrokes308Mesh.CollectBins(data, loadedX0, loadedZ0, loadedX1, loadedZ1, ranges);
            int limit = Mathf.Clamp(notation.MaxStripVertices, 1024, 64000);
            buffer.Clear();
            // the whole-world band draws band + ink as one layer (the paper margin is under half a px there)
            MapStrokes308Mesh.Build(data, ranges, styles, stateOpen, origin, pxPerMetre, limit, notation.PaperPassFirst && Band < 3, buffer);
            if (extras != null)
            {
                var trail = styles[MapStrokes308Mesh.StyleIndex((int)MapStrokeClass308.Trail, 0)];
                for (int i = 0; i < extras.Count; i++) MapStrokes308Mesh.AppendPolyline(data, extras[i], trail, origin, pxPerMetre, limit, buffer);
            }
            SetVerticesDirty();
            Uploads++;
            LastBuildMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            MeanBuildMilliseconds += (LastBuildMilliseconds - MeanBuildMilliseconds) / Uploads;
            return true;
        }

        /// <summary>Forgets the loaded bins (the next Show rebuilds).</summary>
        public void Invalidate() { loaded = false; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (data == null || buffer.VertexCount == 0) return;
            var v = UIVertex.simpleVert;
            for (int i = 0; i < buffer.VertexCount; i++)
            {
                v.position = buffer.Positions[i]; v.color = buffer.Colors[i]; v.uv0 = buffer.Uv0[i]; v.uv1 = buffer.Uv1[i];
                vh.AddVert(v);
            }
            for (int i = 0; i + 2 < buffer.IndexCount; i += 3) vh.AddTriangle(buffer.Indices[i], buffer.Indices[i + 1], buffer.Indices[i + 2]);
        }

        /// <summary>The stroke table of the notation resolved for one zoom band into the builder slots (class x rank).</summary>
        public static void Resolve(MapNotation308SO n, int band, float pxPerMetre, MapStripStyle308[] into)
        {
            int rows = Mathf.Max(1, n.AtlasRows);
            float rowPx = Mathf.Max(8f, n.AtlasRowPx), pad = Mathf.Clamp(n.AtlasRowPad, 0f, rowPx * .4f);
            float span = Mathf.Max(1f, n.AtlasInkSpan.y - n.AtlasInkSpan.x), height = rows * rowPx;
            for (int cls = 0; cls < 16; cls++)
                for (int rank = 0; rank < 8; rank++)
                {
                    var slot = default(MapStripStyle308);
                    var s = n.Stroke(cls, rank, out float scale);
                    float width = s != null ? s.Width(band) * scale : 0f;
                    if (s != null && width > 0f)
                    {
                        int row = Mathf.Clamp(Mathf.RoundToInt(MapStrokeStyle308.At(s.AtlasRow, band)), 0, rows - 1);
                        var spec = n.AtlasRow(row);
                        float axis = spec != null ? Mathf.Clamp(spec.AxisV, pad, rowPx - pad) : rowPx * .5f;
                        float pxPerTexel = width / span;                 // the class width covers the ink span of the row
                        slot.Draw = true;
                        slot.TopPx = (axis - pad) * pxPerTexel;
                        slot.BottomPx = (rowPx - pad - axis) * pxPerTexel;
                        slot.InkHalfPx = width * .5f;
                        slot.KnockPx = Mathf.Max(0f, s.KnockoutPx);
                        slot.Knock = s.KnockoutPx > 0f && (spec == null || spec.Knockout);
                        slot.VTop = 1f - (row * rowPx + pad) / height;          // texture v runs up, row 0 is the top row
                        slot.VBottom = 1f - ((row + 1) * rowPx - pad) / height;
                        // one repeat = 1024 texels at the scale the ink span is drawn at, stretched by the row's U aspect (px), in metres
                        // at the live scale: the grain keeps its px size while zooming. A row without the spec keeps the baked metres.
                        slot.TileMetres = spec != null ? 1024f * pxPerTexel * Mathf.Max(0f, spec.UAspect) / Mathf.Max(1e-4f, pxPerMetre)
                            : Mathf.Max(0f, MapStrokeStyle308.At(s.TileMetres, band));
                        slot.InkAlpha = Mathf.Clamp(s.InkAlpha, .02f, 1f);
                        slot.CapScale = Mathf.Clamp(s.CapScale, .05f, 1f);
                        slot.CapPx = width * Mathf.Max(0f, n.CapLength);
                        slot.PhysicalBelowPx = Mathf.Max(0f, s.PhysicalWidthBelowPx);
                    }
                    into[MapStrokes308Mesh.StyleIndex(cls, rank)] = slot;
                }
        }
    }

    /// <summary>Legend sample of one stroke class (SPEC 4 "the sample is drawn from the atlas / strip itself"): a straight piece of
    /// the class row of the stroke atlas at the px width of the class, through the same shader as the strips of the map (a material
    /// with fog and window clipping off), so the legend cannot drift from the map. `under` = a class drawn first (the bridge
    /// sample lies on a great road).</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MapStrokeSwatch308Graphic : MaskableGraphic
    {
        Texture atlas;
        MapStripStyle308 style, below;
        float metresPerPx = 1f;

        public override Texture mainTexture => atlas != null ? atlas : s_WhiteTexture;

        public void Configure(MapNotation308SO n, int cls, int rank, int underClass, int band, float sampleMetresPerPx, Material legendMaterial)
        {
            var slots = new MapStripStyle308[MapStrokes308Mesh.StyleSlots];
            MapStrokes308Graphic.Resolve(n, band, 1f / Mathf.Max(.01f, sampleMetresPerPx), slots);
            style = slots[MapStrokes308Mesh.StyleIndex(cls, rank)];
            if (!style.Draw && rank != 0) style = slots[MapStrokes308Mesh.StyleIndex(cls, 0)];
            below = underClass > 0 ? slots[MapStrokes308Mesh.StyleIndex(underClass, 0)] : default;
            atlas = n.StrokeAtlas; metresPerPx = Mathf.Max(.01f, sampleMetresPerPx);
            material = legendMaterial; raycastTarget = false;
            var owner = canvas;
            if (owner != null) owner.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            SetAllDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0f || r.height <= 0f) return;
            if (below.Draw) Piece(vh, below, r, r.xMin, r.xMax);
            if (!style.Draw) return;
            // a stretched row (the bridge) is one short piece in the middle; a repeating row runs across the sample
            if (style.TileMetres <= 0f) Piece(vh, style, r, r.center.x - r.width * .22f, r.center.x + r.width * .22f);
            else Piece(vh, style, r, r.xMin, r.xMax);
        }

        void Piece(VertexHelper vh, MapStripStyle308 st, Rect r, float x0, float x1)
        {
            // travel direction = +x with the row top on the left of the stroke (up): teeth and hachures point up in the sample
            float limit = r.height * .5f, top = Mathf.Min(st.TopPx, limit), bottom = Mathf.Min(st.BottomPx, limit), cy = r.center.y;
            float u1 = st.TileMetres > 0f ? (x1 - x0) * metresPerPx / st.TileMetres : 1f;
            float layer = st.Knock ? 1f : 0f;
            int start = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(st.InkAlpha * 255f), 1, 255));
            v.uv1 = new Vector4(.5f, .5f, st.InkAlpha, layer);
            v.position = new Vector3(x0, cy + top, 0f); v.uv0 = new Vector4(0f, st.VTop, 0f, 0f); vh.AddVert(v);
            v.position = new Vector3(x0, cy - bottom, 0f); v.uv0 = new Vector4(0f, st.VBottom, 0f, 0f); vh.AddVert(v);
            v.position = new Vector3(x1, cy + top, 0f); v.uv0 = new Vector4(u1, st.VTop, 0f, 0f); vh.AddVert(v);
            v.position = new Vector3(x1, cy - bottom, 0f); v.uv0 = new Vector4(u1, st.VBottom, 0f, 0f); vh.AddVert(v);
            vh.AddTriangle(start, start + 1, start + 3); vh.AddTriangle(start, start + 3, start + 2);
        }
    }
}
