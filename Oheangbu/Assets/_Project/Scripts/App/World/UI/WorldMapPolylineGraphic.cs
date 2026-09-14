using System;
using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class WorldMapHeadingGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var r=rectTransform.rect;
            AddArrow(vh, r, new Color(.99f,.97f,.9f,1), 1f);
            AddArrow(vh, r, color, .72f);
        }
        static void AddArrow(VertexHelper vh, Rect r, Color tint, float scale)
        {
            int start=vh.currentVertCount; Vector2 c=r.center;
            vh.AddVert(c+new Vector2(0,r.height*.5f)*scale,tint,Vector2.zero);
            vh.AddVert(c+new Vector2(r.width*.5f,-r.height*.5f)*scale,tint,Vector2.zero);
            vh.AddVert(c+new Vector2(0,-r.height*.16f)*scale,tint,Vector2.zero);
            vh.AddVert(c+new Vector2(-r.width*.5f,-r.height*.5f)*scale,tint,Vector2.zero);
            vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class WorldMapPolylineGraphic : MaskableGraphic
    {
        static readonly WorldMapLineSpec[] NoLines = Array.Empty<WorldMapLineSpec>();

        WorldMapLineSpec[] lines = NoLines;
        Vector2[] singlePath = Array.Empty<Vector2>();
        WorldMapProjection projection;
        Rect view;
        int panelIndex;
        int panelCount = 1;
        float widthScale = 1f;
        bool single;

        public void SetPaths(WorldMapLineSpec[] source, WorldMapProjection mapProjection, Rect mapView,
            int sliceIndex = 0, int sliceCount = 1, float scale = 1f)
        {
            source ??= NoLines;
            sliceCount = Mathf.Max(1, sliceCount);
            if (!single && ReferenceEquals(lines, source) && Same(view, mapView) && panelIndex == sliceIndex &&
                panelCount == sliceCount && Mathf.Approximately(widthScale, scale)) return;
            lines = source; projection = mapProjection; view = mapView; panelIndex = sliceIndex;
            panelCount = sliceCount; widthScale = scale; single = false; SetVerticesDirty();
        }

        public void SetPath(Vector2[] worldPath, WorldMapProjection mapProjection, Rect mapView, float width,
            int sliceIndex = 0, int sliceCount = 1)
        {
            worldPath ??= Array.Empty<Vector2>();
            sliceCount = Mathf.Max(1, sliceCount);
            if (single && ReferenceEquals(singlePath, worldPath) && Same(view, mapView) && panelIndex == sliceIndex &&
                panelCount == sliceCount && Mathf.Approximately(widthScale, width)) return;
            singlePath = worldPath; projection = mapProjection; view = mapView; panelIndex = sliceIndex;
            panelCount = sliceCount; widthScale = width; single = true; SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (view.width <= 0 || view.height <= 0) return;
            if (single)
            {
                AddPath(vh, singlePath, color, widthScale);
                return;
            }
            for (int i = 0; i < lines.Length; i++)
            {
                WorldMapLineSpec line = lines[i];
                if (line == null) continue;
                AddPath(vh, line.Points, LineColor(line.Kind), line.PixelWidth * widthScale);
            }
        }

        void AddPath(VertexHelper vh, Vector2[] points, Color32 tint, float thickness)
        {
            if (points == null || points.Length < 2) return;
            Rect r = rectTransform.rect;
            for (int i = 1; i < points.Length; i++)
            {
                Vector2 na = projection.WorldToNormalized(points[i - 1]);
                Vector2 nb = projection.WorldToNormalized(points[i]);
                if (Mathf.Max(na.x, nb.x) < view.xMin || Mathf.Min(na.x, nb.x) > view.xMax ||
                    Mathf.Max(na.y, nb.y) < view.yMin || Mathf.Min(na.y, nb.y) > view.yMax) continue;
                Vector2 a = InPanel(na, r), b = InPanel(nb, r);
                if (!ClipToRect(ref a, ref b, r)) continue;
                Vector2 d = b - a; if (d.sqrMagnitude < .001f) continue;
                Vector2 normal = new Vector2(-d.y, d.x).normalized * thickness * .5f;
                int start = vh.currentVertCount;
                vh.AddVert(a - normal, tint, WorldUv(a-normal,r)); vh.AddVert(a + normal, tint, WorldUv(a+normal,r));
                vh.AddVert(b + normal, tint, WorldUv(b+normal,r)); vh.AddVert(b - normal, tint, WorldUv(b-normal,r));
                vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
            }
        }

        Vector2 WorldUv(Vector2 p, Rect r) => new Vector2(
            view.x + ((p.x-r.xMin)/r.width+panelIndex)/panelCount*view.width,
            view.y + (p.y-r.yMin)/r.height*view.height);

        static bool ClipToRect(ref Vector2 a, ref Vector2 b, Rect rect)
        {
            Vector2 delta = b - a; float first = 0f, last = 1f;
            if (!Clip(-delta.x, a.x - rect.xMin, ref first, ref last) ||
                !Clip(delta.x, rect.xMax - a.x, ref first, ref last) ||
                !Clip(-delta.y, a.y - rect.yMin, ref first, ref last) ||
                !Clip(delta.y, rect.yMax - a.y, ref first, ref last)) return false;
            Vector2 origin = a;
            a = origin + delta * first; b = origin + delta * last;
            return true;
        }

        static bool Clip(float denominator, float numerator, ref float first, ref float last)
        {
            if (Mathf.Abs(denominator) < .000001f) return numerator >= 0f;
            float ratio = numerator / denominator;
            if (denominator < 0f)
            {
                if (ratio > last) return false;
                if (ratio > first) first = ratio;
            }
            else
            {
                if (ratio < first) return false;
                if (ratio < last) last = ratio;
            }
            return true;
        }

        Vector2 InPanel(Vector2 normalized, Rect rect)
        {
            float x = (normalized.x - view.x) / view.width * panelCount - panelIndex;
            float y = (normalized.y - view.y) / view.height;
            return new Vector2(rect.xMin + x * rect.width, rect.yMin + y * rect.height);
        }

        static Color32 LineColor(WorldMapLineKind kind)
        {
            switch (kind)
            {
                case WorldMapLineKind.River: return new Color32(61, 104, 111, 238);
                case WorldMapLineKind.Road: return new Color32(89, 63, 42, 245);
                case WorldMapLineKind.DetailFill: return new Color32(88, 78, 60, 52);
                case WorldMapLineKind.DetailOutline: return new Color32(60, 53, 43, 210);
                default: return new Color32(55, 76, 59, 236);
            }
        }

        static bool Same(Rect a, Rect b)
            => Mathf.Abs(a.x - b.x) < .000001f && Mathf.Abs(a.y - b.y) < .000001f &&
               Mathf.Abs(a.width - b.width) < .000001f && Mathf.Abs(a.height - b.height) < .000001f;
    }
}
