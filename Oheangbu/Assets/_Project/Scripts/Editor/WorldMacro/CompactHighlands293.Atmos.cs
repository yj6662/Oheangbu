using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  // #293 atmosphere pass (candidate materials only): canopy masses on the ground + realm air and valley mist.
  // Distant trees read as dots on paper because nothing ties the ground to where the forest stands; the canopy
  // density baked from the placements lets the ground settle into the forest's ink tone. Originals are recorded
  // once to Highlands293/atmos-original.json for "atmos293-revert". Values are TEST.
  [Serializable] sealed class AtmosRecord293 {public string fogShader;public Color fogColor;public Vector4 fogRange;public float canopy;}
  const string RealmFogShader293="Oheangbu/Reworld292/RealmFog293";

  static string Atmos293(bool revert)
  {
   var fog=AssetDatabase.LoadAssetAtPath<Material>(A292+"/Materials/Fog292.mat");var ground=AssetDatabase.LoadAssetAtPath<Material>(A292+"/Materials/KoreanGround.mat");
   if(fog==null||ground==null)throw new Exception("Candidate fog/ground material missing");
   string file=O293+"/atmos-original.json";
   if(!File.Exists(file))
    File.WriteAllText(file,JsonUtility.ToJson(new AtmosRecord293{fogShader=fog.shader.name,fogColor=fog.GetColor("_FogColor"),fogRange=fog.GetVector("_FogRange"),canopy=ground.HasProperty("_CanopyStrength293")?ground.GetFloat("_CanopyStrength293"):0},true));
   var rec=JsonUtility.FromJson<AtmosRecord293>(File.ReadAllText(file));
   if(revert)
   {
    var original=Shader.Find(rec.fogShader);if(original!=null)fog.shader=original;
    fog.SetColor("_FogColor",rec.fogColor);fog.SetVector("_FogRange",rec.fogRange);ground.SetFloat("_CanopyStrength293",rec.canopy);
    EditorUtility.SetDirty(fog);EditorUtility.SetDirty(ground);AssetDatabase.SaveAssets();
    return "Reverted candidate fog ("+rec.fogShader+") and canopy ground strength to the recorded originals";
   }
   string canopy=Canopy293();
   var shader=Shader.Find(RealmFogShader293);if(shader==null)throw new Exception("RealmFog293 shader missing");
   fog.shader=shader;
   fog.SetColor("_FogColor",new Color(.74f,.77f,.76f));fog.SetVector("_FogRange",new Vector4(380,2600,0,0));
   fog.SetTexture("_Realm293",AssetDatabase.LoadAssetAtPath<Texture2D>(A292+"/Surface/realm293.png"));
   fog.SetVector("_RealmFog293",new Vector4(250,1400,.30f,.15f));
   fog.SetVector("_Mist293",new Vector4(95,38,.0015f,70));fog.SetColor("_MistColor293",new Color(.82f,.84f,.84f,.85f));
   ground.SetTexture("_Canopy293",Texture292("canopy293.png",true));ground.SetFloat("_CanopyStrength293",.9f);
   ground.SetVector("_CanopyTint293",new Vector4(.34f,.43f,.31f,0));ground.SetVector("_CanopyRange293",new Vector4(30,220,.3f,0));
   ground.SetVector("_CanopyForest293",new Vector4(.90f,1.03f,.93f,.65f));
   EditorUtility.SetDirty(fog);EditorUtility.SetDirty(ground);AssetDatabase.SaveAssets();
   return "Candidate atmosphere: "+canopy+"; realm air + valley mist fog ("+RealmFogShader293+"). Revert: atmos293-revert";
  }

  // 4m canopy density over the 4x6km world from every tree placement: r = stands (gaussian σ≈8.5m),
  // g = forested ground at hillside scale (three box passes, σ≈34m). Area-weighted splat of each canopy disc.
  static string Canopy293()
  {
   const int W=1000,Hh=1500;const float cell=4;
   var cover=new float[W*Hh];int trees=0;
   void Add(int x,int z,float value){if(x>=0&&z>=0&&x<W&&z<Hh)cover[z*W+x]+=value;}
   var sheets=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).Select(a=>a.Sheet).Where(s=>s!=null).Distinct();
   foreach(var sheet in sheets)
   {
    var lookup=sheet.Prototypes.Where(p=>p.Category==WorldMacroDressingSheetSO.Kind.Tree).GroupBy(p=>p.Id).ToDictionary(g=>g.Key,g=>g.First());
    foreach(var p in sheet.FixedPlacements)
    {
     if(!lookup.TryGetValue(p.PrototypeId,out var proto))continue;
     float r=Mathf.Max(proto.Size.x,proto.Size.z)*p.Scale*.5f,area=Mathf.PI*r*r/(cell*cell);
     float fx=p.Position.x/cell-.5f,fz=p.Position.z/cell-.5f;int x0=Mathf.FloorToInt(fx),z0=Mathf.FloorToInt(fz);float tx=fx-x0,tz=fz-z0;
     Add(x0,z0,area*(1-tx)*(1-tz));Add(x0+1,z0,area*tx*(1-tz));Add(x0,z0+1,area*(1-tx)*tz);Add(x0+1,z0+1,area*tx*tz);trees++;
    }
   }
   float[] kernel={.037f,.111f,.217f,.271f,.217f,.111f,.037f};var temp=new float[W*Hh];
   for(int pass=0;pass<2;pass++)
   {
    for(int z=0;z<Hh;z++)for(int x=0;x<W;x++){float v=0;for(int k=0;k<7;k++)v+=kernel[k]*cover[z*W+Mathf.Clamp(x+k-3,0,W-1)];temp[z*W+x]=v;}
    for(int z=0;z<Hh;z++)for(int x=0;x<W;x++){float v=0;for(int k=0;k<7;k++)v+=kernel[k]*temp[Mathf.Clamp(z+k-3,0,Hh-1)*W+x];cover[z*W+x]=v;}
   }
   var region=(float[])cover.Clone();const int R=8;
   for(int pass=0;pass<3;pass++)
   {
    for(int z=0;z<Hh;z++){float run=0;for(int x=-R;x<=R;x++)run+=region[z*W+Mathf.Clamp(x,0,W-1)];for(int x=0;x<W;x++){temp[z*W+x]=run/(2*R+1);run+=region[z*W+Mathf.Min(x+R+1,W-1)]-region[z*W+Mathf.Max(x-R,0)];}}
    for(int x=0;x<W;x++){float run=0;for(int z=-R;z<=R;z++)run+=temp[Mathf.Clamp(z,0,Hh-1)*W+x];for(int z=0;z<Hh;z++){region[z*W+x]=run/(2*R+1);run+=temp[Mathf.Min(z+R+1,Hh-1)*W+x]-temp[Mathf.Max(z-R,0)*W+x];}}
   }
   // normalise so dense stands reach full density (97th percentile of covered cells)
   float Percentile(float[] values,float q){var v=values.Where(a=>a>.005f).OrderBy(a=>a).ToArray();return v.Length>0?v[(int)(v.Length*q)]:1;}
   float full=Percentile(cover,.97f),forest=Percentile(region,.90f);
   var pixels=new Color32[W*Hh];float sum=0,sumForest=0;
   for(int i=0;i<pixels.Length;i++)
   {
    float d=Mathf.Pow(Mathf.Clamp01(cover[i]/full),.8f),f=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,.75f,region[i]/forest));sum+=d;sumForest+=f;
    pixels[i]=new Color32((byte)Mathf.RoundToInt(d*255),(byte)Mathf.RoundToInt(f*255),0,255);
   }
   var tex=new Texture2D(W,Hh,TextureFormat.RGBA32,false,true);tex.SetPixels32(pixels);
   File.WriteAllBytes(A292+"/Surface/canopy293.png",tex.EncodeToPNG());Object.DestroyImmediate(tex);
   return "canopy293.png from "+trees+" trees (full density at cover "+full.ToString("0.000")+", mean stand density "+(sum/pixels.Length).ToString("0.000")+", mean forested ground "+(sumForest/pixels.Length).ToString("0.000")+")";
  }
 }
}
