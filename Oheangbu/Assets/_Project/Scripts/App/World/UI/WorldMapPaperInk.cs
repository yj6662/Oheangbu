using System;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    // A cached transparent ink layer, updated only when the map window changes.
    // It carries the same authored paths onto the paper during both folds.
    //
    // #304: (1) the brush pressure noise depends only on the position along a segment, so it is sampled once per stamp
    // instead of once per covered pixel (identical output, a fraction of the Perlin calls). (2) Daedong = the D15
    // 대동여지도 layer: roads as thin straight ink lines with right-angle distance ticks, rivers as uncoloured double lines
    // (ink rims, sheet inside), realm borders as ink dots. Its strokes are thinner than the painted / brush strokes they
    // replace, so the raster work stays under the pre-#304 cost. Default flags (false) keep the original behaviour, which
    // WorldMapPaperReview's ink-width check exercises.
    // #307: the raster size is per instance (the HUD minimap prints 512 x 512 for its ~220 px disc); the default constructor
    // keeps 1024 x 768 for the unfolded sheet and WorldMapPaperReview.
    internal sealed class WorldMapPaperInk : IDisposable
    {
        public const int DefaultWidth = 1024, DefaultHeight = 768;
        readonly int Width, Height;
        readonly Color32[] pixels;
        public Texture2D Texture { get; }
        WorldMapProjection projection;
        Rect view;
        Vector2 pixelScale;
        public bool BrushStyle;
        public bool PaintedRelief;

        // D15 (set by WorldMapPresenter from MapStyle304SO)
        public bool Daedong;
        // #304 tokens (Style304 Ink / Ash / Sheet, set by the presenter); every stroke takes its colour from these
        public Color32 InkColour = new Color32(20, 20, 19, 255), AshColour = new Color32(90, 87, 79, 255), PaperColour = new Color32(223, 219, 208, 255);
        public float RoadWidthPx = 2.2f, TickMetres = 200f, TickMinPx = 16f, TickLengthPx = 7f, TickWidthPx = 1.3f;
        public float RiverDoubleMinPx = 3.2f, RiverDoubleMaxMetresPerPx = 8f, RiverRimPx = 1.2f, RiverSinglePx = 1.6f;
        public float DotPx = 2.2f, DotGapPx = 9f;
        /// <summary>Closed realm outlines (world XZ) drawn dotted when Daedong; consumed by the next Draw.</summary>
        public Vector2[][] BorderPolygons;

        public WorldMapPaperInk() : this(DefaultWidth, DefaultHeight) { }

        public WorldMapPaperInk(int width, int height)
        {
            Width = Mathf.Max(1, width); Height = Mathf.Max(1, height);
            pixels = new Color32[Width * Height];
            Texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true)
            { name = "PaperMapInk_Runtime", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            Texture.SetPixels32(pixels); Texture.Apply(false, false);
        }

        public float projectionWorldHeight = 128;

        public void Draw(WorldMapLineSpec[] lines, Vector2[] cavePath, WorldMapProjection mapProjection, Rect mapView, Vector2 displaySize)
        {
            projection = mapProjection; view = mapView;
            pixelScale = new Vector2(Width / Mathf.Max(1f, displaySize.x), Height / Mathf.Max(1f, displaySize.y));
            float metresPerPx = projectionWorldHeight / Mathf.Max(1f, displaySize.y);
            Array.Clear(pixels, 0, pixels.Length);
            if (lines != null) foreach (var line in lines)
            {
                if (line == null || line.Points == null) continue;
                if (Daedong && (line.Kind == WorldMapLineKind.Road || line.Kind == WorldMapLineKind.Trail)) { DaedongRoad(line.Points, metresPerPx); continue; }
                if (Daedong && line.Kind == WorldMapLineKind.River) { DaedongRiver(line, metresPerPx); continue; }
                Color32 tint = LineColor(line.Kind);
                float width = Mathf.Max(.8f, line.PixelWidth);
                if (PaintedRelief && (line.Kind == WorldMapLineKind.Road || line.Kind == WorldMapLineKind.Trail || line.Kind == WorldMapLineKind.DetailFill))
                {
                    width = Mathf.Clamp((line.Kind == WorldMapLineKind.DetailFill ? 8f : 4.5f) / projectionWorldHeight * Height / pixelScale.y, 2.5f, 40);
                    for (int i = 1; i < line.Points.Length; i++) Line(line.Points[i - 1], line.Points[i], width + 1.7f, WithAlpha(AshColour, 175));
                    for (int i = 1; i < line.Points.Length; i++) Line(line.Points[i - 1], line.Points[i], width, WithAlpha(PaperColour, 250));
                }
                else for (int i = 1; i < line.Points.Length; i++) Line(line.Points[i - 1], line.Points[i], width, tint);
            }
            // cave centre path: ink (cinnabar is only the current position and the heard objective, DESIGN §2.2)
            if (!PaintedRelief && cavePath != null) for (int i = 1; i < cavePath.Length; i++) Line(cavePath[i - 1], cavePath[i], 2.3f, WithAlpha(InkColour, 240));
            if (Daedong && BorderPolygons != null) foreach (var polygon in BorderPolygons) DottedLoop(polygon, metresPerPx);
            Texture.SetPixels32(pixels); Texture.Apply(false, false);
        }

        // ------------------------------------------------------------------ D15 strokes
        void DaedongRoad(Vector2[] points, float metresPerPx)
        {
            Color32 ink = WithAlpha(InkColour, 225);
            for (int i = 1; i < points.Length; i++) Line(points[i - 1], points[i], RoadWidthPx, ink, true);
            // right-angle ticks: one per TickMetres of road, left out entirely when they would crowd (the unit never changes)
            if (TickMetres <= 0f || TickMetres / Mathf.Max(.0001f, metresPerPx) < TickMinPx) return;
            float half = TickLengthPx * .5f * metresPerPx, carried = 0f;
            Color32 tickInk = WithAlpha(InkColour, 215);
            for (int i = 1; i < points.Length; i++)
            {
                Vector2 a = points[i - 1], b = points[i], d = b - a; float len = d.magnitude;
                if (len < .001f) continue;
                Vector2 dir = d / len, normal = new Vector2(-dir.y, dir.x);
                float at = TickMetres - carried;
                if (!SegmentInView(a, b, half)) { carried = (carried + len) % TickMetres; continue; }
                while (at <= len)
                {
                    Vector2 p = a + dir * at;
                    Line(p - normal * half, p + normal * half, TickWidthPx, tickInk, true);
                    at += TickMetres;
                }
                carried = len - (at - TickMetres);
            }
        }

        void DaedongRiver(WorldMapLineSpec line, float metresPerPx)
        {
            // uncoloured water: a double ink line with the sheet inside while zoomed in, a single ink line far out
            float w = Mathf.Max(.8f, line.PixelWidth) * 2.1f;
            Color32 ink = WithAlpha(InkColour, 215);
            var p = line.Points;
            if (w >= RiverDoubleMinPx && metresPerPx < RiverDoubleMaxMetresPerPx)
            {
                for (int i = 1; i < p.Length; i++) Line(p[i - 1], p[i], w + 2f * RiverRimPx, ink, true);
                Color32 inner = WithAlpha(PaperColour, 255);
                for (int i = 1; i < p.Length; i++) Line(p[i - 1], p[i], w, inner, true);
            }
            else for (int i = 1; i < p.Length; i++) Line(p[i - 1], p[i], RiverSinglePx, ink, true);
        }

        void DottedLoop(Vector2[] polygon, float metresPerPx)
        {
            if (polygon == null || polygon.Length < 3 || DotGapPx <= 0f) return;
            float gap = DotGapPx * metresPerPx, carried = 0f;
            Color32 ink = WithAlpha(InkColour, 175);
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length], d = b - a; float len = d.magnitude;
                if (len < .001f) continue;
                Vector2 dir = d / len;
                float at = gap - carried;
                if (!SegmentInView(a, b, gap)) { carried = (carried + len) % gap; continue; }
                while (at <= len) { Dot(a + dir * at, DotPx * .5f, ink); at += gap; }
                carried = len - (at - gap);
            }
        }

        bool SegmentInView(Vector2 wa, Vector2 wb, float marginMetres)
        {
            Vector2 na = projection.WorldToNormalized(wa), nb = projection.WorldToNormalized(wb);
            Vector2 m = projection.WorldToNormalized(projection.BoundsMin + Vector2.one * marginMetres) - projection.WorldToNormalized(projection.BoundsMin);
            return !(Mathf.Max(na.x, nb.x) < view.xMin - m.x || Mathf.Min(na.x, nb.x) > view.xMax + m.x ||
                     Mathf.Max(na.y, nb.y) < view.yMin - m.y || Mathf.Min(na.y, nb.y) > view.yMax + m.y);
        }

        void Dot(Vector2 world, float radius, Color32 tint)
        {
            Vector2 n = projection.WorldToNormalized(world);
            if (n.x < view.xMin || n.x > view.xMax || n.y < view.yMin || n.y > view.yMax) return;
            var p = new Vector2((n.x - view.x) / view.width * Width, (n.y - view.y) / view.height * Height);
            Stamp(p, radius, 1f, tint, false);
        }

        // ------------------------------------------------------------------ raster
        /// <summary>A round-capped line of `width` display px. inkMode (#304 Daedong): exact width, light pressure grain, no
        /// brush hatching; otherwise the original brush / painted-relief behaviour.</summary>
        void Line(Vector2 wa, Vector2 wb, float width, Color32 tint, bool inkMode = false)
        {
            Vector2 na = projection.WorldToNormalized(wa), nb = projection.WorldToNormalized(wb);
            if (Mathf.Max(na.x, nb.x) < view.xMin || Mathf.Min(na.x, nb.x) > view.xMax || Mathf.Max(na.y, nb.y) < view.yMin || Mathf.Min(na.y, nb.y) > view.yMax) return;
            Vector2 a = new Vector2((na.x - view.x) / view.width * Width, (na.y - view.y) / view.height * Height);
            Vector2 b = new Vector2((nb.x - view.x) / view.width * Width, (nb.y - view.y) / view.height * Height);
            Vector2 delta = b - a; float t0 = 0, t1 = 1;
            float padding = width * Mathf.Max(pixelScale.x, pixelScale.y);
            if (!Clip(-delta.x, a.x + padding, ref t0, ref t1) || !Clip(delta.x, Width + padding - a.x, ref t0, ref t1) ||
               !Clip(-delta.y, a.y + padding, ref t0, ref t1) || !Clip(delta.y, Height + padding - a.y, ref t0, ref t1)) return;
            b = a + delta * t1; a += delta * t0; delta = b - a;
            int steps = Mathf.Clamp(Mathf.CeilToInt(delta.magnitude * 1.2f), 1, 3000);
            bool brush = !inkMode && BrushStyle && !PaintedRelief;
            float radius = inkMode ? Mathf.Max(.5f, width * .5f) : Mathf.Max(.5f, width * (brush ? 1.05f : .5f));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector2 p = Vector2.Lerp(a, b, t);
                // pressure varies along the stroke only: one Perlin sample per stamp (was one per covered pixel)
                float pressure = 1f;
                if (inkMode) pressure = .9f + .1f * Mathf.PerlinNoise(Mathf.Lerp(wa.x, wb.x, t) * .06f, Mathf.Lerp(wa.y, wb.y, t) * .06f);
                else if (PaintedRelief) pressure = .92f + .12f * Mathf.PerlinNoise(Mathf.Lerp(wa.x, wb.x, t) * .19f, Mathf.Lerp(wa.y, wb.y, t) * .19f);
                else if (brush) pressure = .65f + .35f * Mathf.PerlinNoise(Mathf.Lerp(wa.x, wb.x, t) * .06f, Mathf.Lerp(wa.y, wb.y, t) * .06f);
                Stamp(p, radius, pressure, tint, brush);
            }
        }

        void Stamp(Vector2 p, float radius, float pressure, Color32 tint, bool hatch)
        {
            Vector2 rasterRadius = pixelScale * (radius + .55f);
            int minX = Mathf.Max(0, Mathf.FloorToInt(p.x - rasterRadius.x)), maxX = Mathf.Min(Width - 1, Mathf.CeilToInt(p.x + rasterRadius.x));
            int minY = Mathf.Max(0, Mathf.FloorToInt(p.y - rasterRadius.y)), maxY = Mathf.Min(Height - 1, Mathf.CeilToInt(p.y + rasterRadius.y));
            float reach = radius * pressure + .55f;
            float inverseX = 1f / pixelScale.x, inverseY = 1f / pixelScale.y;
            for (int y = minY; y <= maxY; y++)
            {
                float dy = (y + .5f - p.y) * inverseY;
                int row = y * Width;
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x + .5f - p.x) * inverseX;
                    float coverage = reach - Mathf.Sqrt(dx * dx + dy * dy);
                    // zero coverage still writes the tint RGB at alpha 0 (as before): bilinear sampling of the stroke edge
                    // then never pulls in black from untouched texels
                    if (coverage < 0f) coverage = 0f; else if (coverage > 1f) coverage = 1f;
                    if (hatch) coverage *= .6f + .3f * Mathf.Abs(Mathf.Sin(dx * 3 + dy * 2));
                    byte alpha = (byte)(tint.a * coverage); int index = row + x;
                    if (alpha >= pixels[index].a) pixels[index] = new Color32(tint.r, tint.g, tint.b, alpha);
                }
            }
        }

        static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Abs(p) < .000001f) return q >= 0;
            float r = q / p; if (p < 0) { if (r > t1) return false; t0 = Mathf.Max(t0, r); } else { if (r < t0) return false; t1 = Mathf.Min(t1, r); }
            return true;
        }

        // alphas as before #304 (they set the stroke weights); hues are the ink tokens instead of teal / brown / green
        Color32 LineColor(WorldMapLineKind kind)
        {
            switch (kind)
            {
                case WorldMapLineKind.River: return WithAlpha(InkColour, 195);
                case WorldMapLineKind.Road: return WithAlpha(InkColour, 224);
                case WorldMapLineKind.DetailFill: return WithAlpha(AshColour, 45);
                case WorldMapLineKind.DetailOutline: return WithAlpha(InkColour, 210);
                default: return WithAlpha(AshColour, 198);
            }
        }

        static Color32 WithAlpha(Color32 c, byte a) => new Color32(c.r, c.g, c.b, a);

        public void Dispose() { if (Texture != null) UnityEngine.Object.Destroy(Texture); }
    }
}
