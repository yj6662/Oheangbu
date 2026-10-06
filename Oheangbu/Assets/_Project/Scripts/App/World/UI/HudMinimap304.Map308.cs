using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    // #308 notation on the HUD minimap (SPEC-MAP-OVERHAUL-308 §5, §6): when the bound map carries a bundle
    // (IMapNotationSource308.Notation308) the minimap adds, between the paper and the marks,
    //   MiniStrokes308   nested Canvas > Pivot > Strips: the brush strips (roads, ridges, cliffs, walls, streams) as one graphic.
    //                    The mesh is uploaded only when the window leaves the loaded 250 m bins; walking moves the Strips rect,
    //                    follow view turns the Pivot. No print: outdoors the 512 x 512 ink raster is never made.
    //   MiniFrame308     the lacquer frame (9-slice, MiniFrameWidth px outside the map window; temporary art until the theme kit)
    //   North308         the nacre north piece on the frame's top edge (slides along the frame in follow view; the tip points north)
    // and draws the marks, the pin and the player mark from the icon atlas (one quad each, one material). The paper itself is
    // the same RawImage ("MiniRoot", uvRect 0..1, _MINI_HUD) with _MAP308 on its material. Without a bundle nothing here is
    // built and the #307 minimap runs as before. Budget: paper 1 + strips 1 + frame 1 + icons 1 batches; no per-frame bake, no
    // full-screen pass, no print on a fog reveal (the strip shader reads the shared fog texture).
    public sealed partial class HudMinimap304
    {
        static readonly int WorldUvId = Shader.PropertyToID("_WorldUv"), StrokeViewId = Shader.PropertyToID("_S308View"),
            StrokeTurnId = Shader.PropertyToID("_S308Turn"), StrokeFrameId = Shader.PropertyToID("_S308Frame");

        IMapNotationSource308 src308;
        MapNotation308SO n308;
        MapStrokes308Graphic strips308;
        RectTransform stripsHost308, stripsPivot308, north308, frame308;
        Material strokeMaterial308, iconMaterial308;
        MapGlyph308Graphic playerGlyph308;
        Rect worldWindow308, stripView308;
        int pinCell308 = -1;
        bool window308, outdoor308;
        float previewHeading308 = float.NaN;   // Edit-mode preview only: a heading to draw (Play never sets it)
        float bandRange308 = -1f;              // the range the paper band values were last set for

        /// <summary>The bound map draws the #308 notation.</summary>
        public bool Notation308Active => n308 != null;
        /// <summary>Strip mesh uploads since the map was bound (AC-P1: only when the window leaves the loaded bins).</summary>
        public int StripUploads308 => strips308 != null ? strips308.Uploads : 0;
        public int StripVertices308 => strips308 != null ? strips308.VertexCount : 0;
        public double StripMeanBuildMilliseconds308 => strips308 != null ? strips308.MeanBuildMilliseconds : 0.0;
        public MapStrokes308Graphic Strips308 => strips308;

        bool Outdoor308 => n308 != null && !Interior;
        float FrameWidth308 => n308 != null ? Mathf.Max(0f, n308.MiniFrameWidth) : 0f;
        float MarkInset308 => n308 != null ? n308.MiniEdgePx : m.MarkerRimInset;

        // ------------------------------------------------------------------ bind / build
        void Bind308(WorldMapPresenter next)
        {
            Unbind308();
            src308 = next;
            n308 = src308 != null ? src308.Notation308 : null;
            if (n308 == null) { src308 = null; return; }
            Build308();
            ApplyLook308(material);
        }

        void Build308()
        {
            float w = WindowPx, h = WindowH, fw = FrameWidth308;
            // strips right above the paper
            strokeMaterial308 = src308.CreateStrokeMaterial308();
            if (strokeMaterial308 != null && src308.Strokes308 != null)
            {
                strips308 = MapStrokes308Graphic.CreateHost("MiniStrokes308", root, out stripsHost308, out stripsPivot308);
                stripsHost308.SetSiblingIndex(1);
                strips308.Bind(src308.Strokes308, n308, strokeMaterial308);
                strokeMaterial308.SetVector(StrokeFrameId, new Vector4(w, h, 1f, Mathf.Clamp01(n308.PaperBandAlpha)));
                strokeMaterial308.SetVector(StrokeTurnId, new Vector4(1f, 0f, Aspect, 0f));
            }
            // the frame around the map window
            frame308 = Centred("MiniFrame308", root, w + 2f * fw, h + 2f * fw);
            frame308.SetSiblingIndex(strips308 != null ? 2 : 1);
            var frame = V.Image(frame308, n308.FrameMini != null ? n308.FrameTint : n308.Lacquer, n308.FrameMini);
            // the kit frame is a tiled 9-slice (corners 24 px, edge tile 12 px), centre left open over the map
            if (n308.FrameMini != null && n308.FrameMini.border.sqrMagnitude > 0f) { frame.type = Image.Type.Tiled; frame.fillCenter = false; }
            else if (n308.FrameMini == null) frame308.gameObject.SetActive(false);   // kit not imported: no flat slab over the map

            iconMaterial308 = src308.CreateIconMaterial308();
            // north piece on the frame's top edge: the kit cell (same atlas as the frame: one batch with it), else an atlas glyph
            north308 = Centred("North308", root, n308.MiniNorthPx, n308.MiniNorthPx);
            north308.SetSiblingIndex(frame308.GetSiblingIndex() + 1);
            bool north = false;
            if (n308.NorthPiece != null)
            {
                V.Image(V.Stretch("Piece", north308), Color.white, n308.NorthPiece).preserveAspect = true; north = true;
            }
            else if (iconMaterial308 != null && n308.TryKeyCell(MapNotation308SO.KeyNorth, out int northCell))
            {
                var g = MapGlyph308Graphic.Create("Piece", north308, n308.IconAtlas(n308.MiniNorthPx), iconMaterial308, northCell, n308.Nacre, 0f, n308.MiniNorthPx);
                Fill(g.rectTransform); north = true;
            }
            north308.gameObject.SetActive(north);
            if (north && northRoot != null) northRoot.gameObject.SetActive(false);   // the #307 ink tick steps aside

            // marks, pin and player mark from the atlas
            pinCell308 = -1;
            if (iconMaterial308 != null)
            {
                float rim = Mathf.Clamp01(n308.IconRimAlpha);
                foreach (var mk in marks)
                {
                    mk.Glyph = MapGlyph308Graphic.Create("Glyph", mk.Root, n308.IconAtlas(n308.MiniIconPx), iconMaterial308, 0, s.Ink, rim, n308.MiniIconPx);
                    Fill(mk.Glyph.rectTransform); mk.Glyph.enabled = false;
                }
                if (n308.TryKeyCell(MapNotation308SO.KeyPin, out int pin)) pinCell308 = pin;
                if (n308.TryKeyCell(MapNotation308SO.KeyPlayer, out int player))
                {
                    arrow.sizeDelta = new Vector2(n308.MiniPlayerPx, n308.MiniPlayerPx);
                    for (int i = 0; i < arrow.childCount; i++) arrow.GetChild(i).gameObject.SetActive(false);
                    playerGlyph308 = MapGlyph308Graphic.Create("Glyph", arrow, n308.IconAtlas(n308.MiniPlayerPx), iconMaterial308, player, n308.Cinnabar, rim, n308.MiniPlayerPx);
                    Fill(playerGlyph308.rectTransform);
                }
            }
            float pinScale = n308.MiniPinPx / Mathf.Max(1f, m.PinSize);
            foreach (var pin in pins) pin.localScale = new Vector3(pinScale, pinScale, 1f);
            window308 = false; worldWindow308 = default; stripView308 = default;
        }

        static void Fill(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; r.anchoredPosition = Vector2.zero; }

        /// <summary>#308 look on the minimap paper: the frame sits on the window edge, so the torn fade and the rim ink of #307
        /// are off and the paper is one alpha inside the frame. The colours and terrain values come from the map
        /// (WorldMapPresenter.ApplyPaper308 at CreateMiniMaterial).</summary>
        void ApplyLook308(Material mat)
        {
            if (mat == null || n308 == null) return;
            float alpha = Mathf.Clamp01(n308.MiniPaperAlpha);
            mat.SetVector("_MiniDisc", new Vector4(1f, 1.004f, 0f, Aspect));
            mat.SetVector("_MiniLand", new Vector4(alpha, Mathf.Clamp01(n308.MiniEdgeInk), 0f, 0f));
            var veil = s.Mist; veil.a = alpha; mat.SetColor("_MiniVeil", veil);
        }

        // Destroy in Play; in Edit mode (the MapOverhaul308 preview) objects are destroyed at once
        static void Kill308(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        void Unbind308()
        {
            Kill308(strokeMaterial308); Kill308(iconMaterial308);
            strokeMaterial308 = iconMaterial308 = null;
            if (stripsHost308 != null) Kill308(stripsHost308.gameObject);
            if (frame308 != null) Kill308(frame308.gameObject);
            if (north308 != null) Kill308(north308.gameObject);
            stripsHost308 = stripsPivot308 = frame308 = north308 = null; strips308 = null;
            foreach (var mk in marks)
            {
                if (mk.Glyph != null) { Kill308(mk.Glyph.gameObject); mk.Glyph = null; }
                Legacy308(mk);
            }
            if (playerGlyph308 != null)
            {
                Kill308(playerGlyph308.gameObject); playerGlyph308 = null;
                if (arrow != null)
                {
                    arrow.sizeDelta = new Vector2(m.ArrowSize, m.ArrowSize);
                    for (int i = 0; i < arrow.childCount; i++) arrow.GetChild(i).gameObject.SetActive(true);
                }
            }
            foreach (var pin in pins) if (pin != null) pin.localScale = Vector3.one;
            if (northRoot != null) northRoot.gameObject.SetActive(true);
            src308 = null; n308 = null; pinCell308 = -1; window308 = false; outdoor308 = false; bandRange308 = -1f;
        }

        // ------------------------------------------------------------------ frame
        /// <summary>Outdoors with a bundle: no ink print. The paper's world window follows the player by one material vector
        /// (_WorldUv); zone, cave plan and band values are re-applied only when the range, the revision or follow view change.</summary>
        void Window308(Vector3 here, float range, int revision)
        {
            if (!outdoor308) { outdoor308 = true; window308 = false; }
            view = Widen(source.WindowUv(here, range));
            // a square (in metres) that covers the wide window, and its corners when the map turns
            float cover = follow ? Mathf.Sqrt(1f + Aspect * Aspect) : Mathf.Max(1f, Aspect);
            Rect big = source.WindowUv(here, range * cover);
            if (!window308 || !Mathf.Approximately(range, printedRange) || revision != printedRevision || follow != printedFollow)
            {
                source.ApplyMini(material, big);
                var uv = new Rect((view.x - big.x) / big.width, (view.y - big.y) / big.height, view.width / big.width, view.height / big.height);
                shaderWindow = uv;
                material.SetVector(MapWindowId, new Vector4(-uv.x / uv.width, -uv.y / uv.height, 1f / uv.width, 1f / uv.height));
                printedAt = here; printedRange = range; printedFollow = follow; printedRevision = revision; window308 = true; worldWindow308 = big;
                Prints++;   // counts re-applies here (no raster): the probe's "reprinted after the teleport" still holds
            }
            else if (big != worldWindow308)
            {
                worldWindow308 = big;
                material.SetVector(WorldUvId, new Vector4(big.x, big.y, big.width, big.height));
            }
            inkWindow = big;
        }

        /// <summary>Scale-dependent paper values (pattern period, shore width, rim width) for the range drawn now: outdoors AND in a
        /// cave (28 m: band Z0), set only when the range changes. Without it the cave window kept the outdoor pattern period.</summary>
        void Band308(float range)
        {
            if (n308 == null || material == null || Mathf.Approximately(range, bandRange308)) return;
            bandRange308 = range;
            src308.ApplyMiniBand308(material, range * 2f * Aspect / Mathf.Max(1f, WindowPx), canvas != null ? canvas.scaleFactor : 1f);
        }

        /// <summary>Leaving the #308 outdoor path (a cave): the next legacy print must not trust the cached window.</summary>
        void LeaveWindow308()
        {
            if (!outdoor308) return;
            outdoor308 = false; window308 = false; printedRange = -1f; shaderWindow = default;
        }

        void SyncStrips308(Vector3 here, float range)
        {
            if (strips308 == null) return;
            bool show = Outdoor308;
            if (stripsHost308.gameObject.activeSelf != show) stripsHost308.gameObject.SetActive(show);
            if (!show) return;
            float pxPerMetre = WindowPx / (2f * range * Aspect);
            var xz = new Vector2(here.x, here.z);
            float halfW = range * Aspect, halfH = range;
            if (follow) halfW = halfH = Mathf.Sqrt(halfW * halfW + halfH * halfH);
            var states = src308.StrokeStates308(out int statesRevision);
            strips308.Show(new Rect(xz.x - halfW, xz.y - halfH, 2f * halfW, 2f * halfH), n308.MiniBinMargin, pxPerMetre, states, statesRevision, src308.ExtraStrokes308);
            Vector2 at = (strips308.Origin - xz) * pxPerMetre;
            var rect = strips308.rectTransform;
            if (rect.anchoredPosition != at) rect.anchoredPosition = at;
            if (view != stripView308)
            {
                stripView308 = view;
                strokeMaterial308.SetVector(StrokeViewId, new Vector4(view.x, view.y, 1f / Mathf.Max(1e-7f, view.width), 1f / Mathf.Max(1e-7f, view.height)));
            }
        }

        /// <summary>Follow view: the strips turn with the paper's ink and the north piece slides along the frame's centre line.</summary>
        void Turn308(float deg)
        {
            if (n308 == null) return;
            float rad = deg * Mathf.Deg2Rad, c = Mathf.Cos(rad), sn = Mathf.Sin(rad);
            if (stripsPivot308 != null) stripsPivot308.localEulerAngles = new Vector3(0f, 0f, deg);
            if (strokeMaterial308 != null) strokeMaterial308.SetVector(StrokeTurnId, new Vector4(c, sn, Aspect, 0f));
            if (north308 == null) return;
            var d = new Vector2(-sn, c);
            float ex = WindowPx * .5f + n308.MiniNorthOffset, ey = WindowH * .5f + n308.MiniNorthOffset;
            float e = Mathf.Min(Mathf.Abs(d.x) > 1e-4f ? ex / Mathf.Abs(d.x) : float.MaxValue, Mathf.Abs(d.y) > 1e-4f ? ey / Mathf.Abs(d.y) : float.MaxValue);
            north308.anchoredPosition = d * e;
            north308.localEulerAngles = new Vector3(0f, 0f, deg);
        }

        // ------------------------------------------------------------------ marks
        /// <summary>A mark from the atlas: true when the map has a glyph for it (place by marker id, rest, coin, pin).</summary>
        bool Dress308(Mark view304, MapMarker304 marker)
        {
            if (n308 == null || view304.Glyph == null || !src308.TryMarkerCell308(marker, out int cell)) { Legacy308(view304); return false; }
            float size = marker.Kind == MapMarkerKind304.Pin ? n308.MiniPinPx : marker.Kind == MapMarkerKind304.Coin ? n308.MiniCoinPx : n308.MiniIconPx;
            if (!Mathf.Approximately(view304.Root.sizeDelta.x, size)) view304.Root.sizeDelta = new Vector2(size, size);
            view304.Glyph.Set(n308.IconAtlas(size), cell, s.Ink, Mathf.Clamp01(n308.IconRimAlpha));
            if (!view304.Glyph.enabled) view304.Glyph.enabled = true;
            if (view304.Rim.enabled) view304.Rim.enabled = false;
            if (view304.Core.enabled) view304.Core.enabled = false;
            return true;
        }

        void Legacy308(Mark view304)
        {
            if (view304.Glyph != null && view304.Glyph.enabled) view304.Glyph.enabled = false;
            if (view304.Rim != null && !view304.Rim.enabled) view304.Rim.enabled = true;
            if (view304.Core != null && !view304.Core.enabled) view304.Core.enabled = true;
            if (view304.Root != null && !Mathf.Approximately(view304.Root.sizeDelta.x, m.MarkerSize)) view304.Root.sizeDelta = new Vector2(m.MarkerSize, m.MarkerSize);
        }

        // ------------------------------------------------------------------ editor still (MapOverhaul308 preview; no PlaytestUiRoot)
        /// <summary>One frame of the minimap against `presenter` without the UI root: binds, shows at full alpha and places
        /// everything. For the Edit-mode preview only (nothing is saved; the caller destroys the canvas).</summary>
        public bool PreviewFrame308(WorldMapPresenter presenter, bool followView, float headingDeg)
        {
            Bind(presenter);
            if (source == null || material == null || !source.MiniReady) return false;
            follow = followView; showSetting = true; previewHeading308 = headingDeg;
            TargetAlpha = 1f; group.alpha = 1f;
            Frame(true);
            return true;
        }

        /// <summary>Edit-mode preview teardown (OnDestroy does not run outside Play): the run-time materials, at once.</summary>
        public void ReleasePreview308()
        {
            if (Application.isPlaying) return;
            Unbind308();
            if (map != null) map.material = null;
            Kill308(material); material = null;
            boundMap = null; source = null; markers = null; detail = null;
        }
    }
}
