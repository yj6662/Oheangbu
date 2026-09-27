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
        public bool BrushStyle;
        public bool PaintedRelief;
        public float ViewWorldHeight=128;

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
                if(PaintedRelief&&(line.Kind==WorldMapLineKind.Road||line.Kind==WorldMapLineKind.Trail||line.Kind==WorldMapLineKind.DetailFill)){
                    float width=line.Kind==WorldMapLineKind.DetailFill?Mathf.Clamp(8/ViewWorldHeight*rectTransform.rect.height,3,40):3.5f;
                    AddPath(vh,line.Points,new Color32(113,105,80,175),width+1.7f);
                    AddPath(vh,line.Points,new Color32(220,210,175,250),width);
                }else AddPath(vh, line.Points, LineColor(line.Kind), line.PixelWidth * widthScale);
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
                if(PaintedRelief){
                    Vector2 n=new Vector2(-d.y,d.x).normalized;
                    int steps=Mathf.Max(1,Mathf.CeilToInt(d.magnitude/3));
                    for(int j=0;j<=steps;j++){
                        float t=(float)j/steps;Vector2 p=Vector2.Lerp(a,b,t);
                        float pressure=.92f+.12f*Mathf.PerlinNoise((points[i-1].x+(points[i].x-points[i-1].x)*t)*.19f,(points[i-1].y+(points[i].y-points[i-1].y)*t)*.19f);
                        float radius=thickness*.5f*pressure;int k=vh.currentVertCount;
                        vh.AddVert(p,tint,WorldUv(p,r));
                        for(int v=0;v<=10;v++){float angle=v*Mathf.PI*.2f;var q=p+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;vh.AddVert(q,tint,WorldUv(q,r));if(v>0)vh.AddTriangle(k,k+v,k+v+1);}
                    }continue;
                }
                if(BrushStyle&&!PaintedRelief){
                    Vector2 n=new Vector2(-d.y,d.x).normalized;
                    // World-anchored pressure; clipping or camera motion cannot reseed the brush.
                    float pressure=.8f+.35f*Mathf.PerlinNoise(points[i-1].x*.05f,points[i-1].y*.05f);
                    for(int strand=0;strand<3;strand++){
                        float offset=(strand-1)*thickness*.56f;
                        Vector2 aa=a+n*offset,bb=b+n*offset;
                        float wa=thickness*pressure*(strand==1?.56f:.22f),wb=wa*(.65f+.3f*Mathf.Sin(points[i].x*.07f));
                        Color c=tint;c.a*=strand==1?.70f:.27f;
                        int k=vh.currentVertCount;
                        vh.AddVert(aa-n*wa,c,WorldUv(aa-n*wa,r));vh.AddVert(aa+n*wa,c,WorldUv(aa+n*wa,r));
                        vh.AddVert(bb+n*wb,c,WorldUv(bb+n*wb,r));vh.AddVert(bb-n*wb,c,WorldUv(bb-n*wb,r));
                        vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k,k+2,k+3);
                    }continue;
                }
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
