using UnityEngine;
using UnityEditor;
using Oheangbu.Data.World;
using Oheangbu.BrushRender;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Macro-owned pigment map. Geography blends softly; no lights or gameplay volumes.</summary>
    public static class WorldMacroPalette
    {
        public static Texture2D Build(WorldMacroSheetSO sheet)
        {
            var palette=AssetDatabase.LoadAssetAtPath<ElementPaletteSO>(DevSceneKit.PalettePath);
            const int w=256,h=384;var pixels=new Color[w*h];
            var colours=new Color[sheet.Regions.Length];
            for(int i=0;i<colours.Length;i++)
            {
                char initial=sheet.Regions[i].Realm switch {
                    RealmId.Cheongrim=>'ㄱ',RealmId.Jeokro=>'ㄴ',RealmId.Cheolong=>'ㅅ',RealmId.Hyeongang=>'ㅇ',_=>'ㅁ'};
                colours[i]=palette.GetBaseColor(initial).linear;
            }
            for(int z=0;z<h;z++)for(int x=0;x<w;x++)
            {
                float px=Mathf.Lerp(sheet.BoundsMin.x,sheet.BoundsMax.x,x/(float)(w-1));
                float pz=Mathf.Lerp(sheet.BoundsMin.y,sheet.BoundsMax.y,z/(float)(h-1));
                // Uneven weathering at broad ecotones, rather than polygon-shaped colour borders.
                float warp=(Mathf.PerlinNoise(px/470f+31,pz/470f+19)-.5f)*130f;
                Color sum=Color.clear;float total=0,best=float.MinValue;int nearest=0;
                for(int i=0;i<sheet.Regions.Length;i++)
                {
                    float d=SignedDistance(sheet.Regions[i].Polygon,px,pz);
                    if(d>best){best=d;nearest=i;}
                    float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-280,280,d+warp));
                    total+=weight;sum+=colours[i]*weight;
                }
                var c=total>.0001f?sum/total:colours[nearest];c.a=1;pixels[z*w+x]=c;
            }
            string path=WorldMacroBuilder.Folder+"/RealmPigment.asset";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);bool fresh=texture==null;
            if(fresh)texture=new Texture2D(w,h,TextureFormat.RGBA32,false,true);
            else texture.Reinitialize(w,h,TextureFormat.RGBA32,false);
            texture.name="Realm soil pigment - non emissive";texture.SetPixels(pixels);texture.Apply();
            texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
            if(fresh)AssetDatabase.CreateAsset(texture,path);else EditorUtility.SetDirty(texture);
            return texture;
        }
        static float SignedDistance(Vector2[] polygon,float x,float z)
        {
            bool inside=false;float nearest=float.MaxValue;
            for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
            {
                var a=polygon[j];var b=polygon[i];
                if((a.y>z)!=(b.y>z)&&x<(b.x-a.x)*(z-a.y)/(b.y-a.y)+a.x)inside=!inside;
                float d=WorldMacroTerrain.SegmentDistance(x,z,new Vector3(a.x,0,a.y),new Vector3(b.x,0,b.y),out _);
                nearest=Mathf.Min(nearest,d);
            }
            return inside?nearest:-nearest;
        }
    }
}
