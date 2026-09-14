using System;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    // A cached transparent ink layer, updated only when the map window changes.
    // It carries the same authored paths onto the paper during both folds.
    internal sealed class WorldMapPaperInk : IDisposable
    {
        const int Width = 1024, Height = 768;
        readonly Color32[] pixels = new Color32[Width * Height];
        public Texture2D Texture { get; }
        WorldMapProjection projection;
        Rect view;
        Vector2 pixelScale;

        public WorldMapPaperInk()
        {
            Texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true)
            { name="PaperMapInk_Runtime", filterMode=FilterMode.Bilinear, wrapMode=TextureWrapMode.Clamp, hideFlags=HideFlags.DontSave };
            Texture.SetPixels32(pixels); Texture.Apply(false,false);
        }

        public void Draw(WorldMapLineSpec[] lines, Vector2[] cavePath, WorldMapProjection mapProjection, Rect mapView, Vector2 displaySize)
        {
            projection=mapProjection; view=mapView;
            pixelScale=new Vector2(Width/Mathf.Max(1f,displaySize.x),Height/Mathf.Max(1f,displaySize.y));
            Array.Clear(pixels,0,pixels.Length);
            if(lines!=null)foreach(var line in lines)
            {
                if(line==null||line.Points==null)continue;
                Color32 tint=LineColor(line.Kind);
                float width=Mathf.Max(.8f,line.PixelWidth);
                for(int i=1;i<line.Points.Length;i++)Line(line.Points[i-1],line.Points[i],width,tint);
            }
            if(cavePath!=null)for(int i=1;i<cavePath.Length;i++)Line(cavePath[i-1],cavePath[i],2.3f,new Color32(114,51,34,240));
            Texture.SetPixels32(pixels);Texture.Apply(false,false);
        }

        void Line(Vector2 wa,Vector2 wb,float width,Color32 tint)
        {
            Vector2 na=projection.WorldToNormalized(wa),nb=projection.WorldToNormalized(wb);
            if(Mathf.Max(na.x,nb.x)<view.xMin||Mathf.Min(na.x,nb.x)>view.xMax||Mathf.Max(na.y,nb.y)<view.yMin||Mathf.Min(na.y,nb.y)>view.yMax)return;
            Vector2 a=new Vector2((na.x-view.x)/view.width*Width,(na.y-view.y)/view.height*Height);
            Vector2 b=new Vector2((nb.x-view.x)/view.width*Width,(nb.y-view.y)/view.height*Height);
            Vector2 delta=b-a; float t0=0,t1=1;
            float padding=width*Mathf.Max(pixelScale.x,pixelScale.y);
            if(!Clip(-delta.x,a.x+padding,ref t0,ref t1)||!Clip(delta.x,Width+padding-a.x,ref t0,ref t1)||
               !Clip(-delta.y,a.y+padding,ref t0,ref t1)||!Clip(delta.y,Height+padding-a.y,ref t0,ref t1))return;
            b=a+delta*t1;a+=delta*t0;delta=b-a;
            int steps=Mathf.Clamp(Mathf.CeilToInt(delta.magnitude*1.2f),1,3000);
            float radius=Mathf.Max(.5f,width*.5f);
            Vector2 rasterRadius=pixelScale*(radius+.55f);
            for(int i=0;i<=steps;i++)
            {
                Vector2 p=Vector2.Lerp(a,b,(float)i/steps);
                int minX=Mathf.Max(0,Mathf.FloorToInt(p.x-rasterRadius.x)),maxX=Mathf.Min(Width-1,Mathf.CeilToInt(p.x+rasterRadius.x));
                int minY=Mathf.Max(0,Mathf.FloorToInt(p.y-rasterRadius.y)),maxY=Mathf.Min(Height-1,Mathf.CeilToInt(p.y+rasterRadius.y));
                for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
                {
                    Vector2 distance=new Vector2((x+.5f-p.x)/pixelScale.x,(y+.5f-p.y)/pixelScale.y);
                    float coverage=Mathf.Clamp01(radius+.55f-distance.magnitude);
                    byte alpha=(byte)(tint.a*coverage);int index=y*Width+x;
                    if(alpha>pixels[index].a)pixels[index]=new Color32(tint.r,tint.g,tint.b,alpha);
                }
            }
        }

        static bool Clip(float p,float q,ref float t0,ref float t1)
        {
            if(Mathf.Abs(p)<.000001f)return q>=0;
            float r=q/p;if(p<0){if(r>t1)return false;t0=Mathf.Max(t0,r);}else{if(r<t0)return false;t1=Mathf.Min(t1,r);}return true;
        }
        static Color32 LineColor(WorldMapLineKind kind)
        {
            switch(kind)
            {
                case WorldMapLineKind.River:return new Color32(44,77,84,195);
                case WorldMapLineKind.Road:return new Color32(81,55,34,224);
                case WorldMapLineKind.DetailFill:return new Color32(88,78,60,45);
                case WorldMapLineKind.DetailOutline:return new Color32(60,53,43,210);
                default:return new Color32(55,76,59,198);
            }
        }
        public void Dispose(){if(Texture!=null)UnityEngine.Object.Destroy(Texture);}
    }
}
