using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  // A material-only correction for changed terrain and the lowland lake shore. Baseline assets stay immutable.
  static void ToneGround295(CompactWorldSurface oldField,CompactWorldSurface field,CompactHydrologySO hydro,Material ground,List<string> report)
  {
   const int width=1000,height=1500;const float cell=4;
   if(ground==null||AssetDatabase.GetAssetPath(ground)!=A295+"/Materials/KoreanGround.mat")
    throw new InvalidOperationException("295 terrain tone requires the private candidate ground material.");
   if(oldField.Width!=1001||oldField.Height!=1501||oldField.Cell!=cell||field.Width!=1001||field.Height!=1501||field.Cell!=cell||
    hydro==null||hydro.Width!=1001||hydro.Height!=1501||hydro.Cell!=cell||hydro.ProtectedMask==null)
    throw new InvalidOperationException("295 terrain tone requires matching verified 4m terrain and protection fields.");
   var protection=hydro.ProtectedMask.bytes;
   if(protection.Length!=hydro.Width*hydro.Height)throw new InvalidOperationException("295 terrain tone protected mask length mismatch.");

   string sourcePath=A292+"/Shaders/KoreanInkGround.shader",shaderPath=A295+"/Shaders/KoreanInkGround295.shader";
   string surfaceSourcePath=A292+"/Shaders/KoreanSurface292.hlsl",surfacePath=A295+"/Shaders/KoreanSurface295.hlsl";
   string shader=File.ReadAllText(sourcePath);
   string newline=shader.Contains("\r\n")?"\r\n":"\n";
   string ReplaceOnce(string value,string find,string replacement)
   {
    int at=value.IndexOf(find,StringComparison.Ordinal);
    if(at<0||value.IndexOf(find,at+find.Length,StringComparison.Ordinal)>=0)
     throw new InvalidOperationException("295 private shader source anchor changed: "+find);
    return value.Substring(0,at)+replacement+value.Substring(at+find.Length);
   }
   shader=ReplaceOnce(shader,"Shader \"Oheangbu/Reworld292/KoreanInkGround\"","Shader \"Oheangbu/Watershed295/KoreanInkGround\"");
   shader=ReplaceOnce(shader,"        _BankStrength294(",
    "        _WatershedToneMask295(\"Changed terrain and local lake shore tone\",2D)=\"black\"{}"+newline+"        _BankStrength294(");
   shader=ReplaceOnce(shader,"        TEXTURE2D(_StrataField276);",
    "        TEXTURE2D(_WatershedToneMask295); // Reuses sampler_GroundPathMask; no additional sampler."+newline+"        TEXTURE2D(_StrataField276);");
   shader=ReplaceOnce(shader,"#include \"Assets/_Project/Art/World/Reworld292/Shaders/KoreanSurface292.hlsl\"",
    "#include \"Assets/_Project/Art/World/Watershed295/Shaders/KoreanSurface295.hlsl\"");
   string surface=File.ReadAllText(surfaceSourcePath);
   string surfaceNewline=surface.Contains("\r\n")?"\r\n":"\n";
   surface=ReplaceOnce(surface,"    float resolve=1-smoothstep(35,180,distanceWS);",
    "    float resolve=1-smoothstep(35,180,distanceWS);"+surfaceNewline+
    "    // Retain subdued existing soil/rock albedo on the candidate's authored lowland mask."+surfaceNewline+
    "    // No additional material samples or normals; exact baseline resolve where the mask is zero."+surfaceNewline+
    "    float2 watershedDetailUV295=p.xz/float2(4000,6000);"+surfaceNewline+
    "    float watershedDetailMask295=SAMPLE_TEXTURE2D_LOD(_WatershedToneMask295,sampler_GroundPathMask,saturate(watershedDetailUV295),0).r;"+surfaceNewline+
    "    watershedDetailMask295*=step(0,watershedDetailUV295.x)*step(watershedDetailUV295.x,1)*step(0,watershedDetailUV295.y)*step(watershedDetailUV295.y,1);"+surfaceNewline+
    "    resolve=max(resolve,.30*watershedDetailMask295*(1-smoothstep(500,750,distanceWS)));");
   shader=ReplaceOnce(shader,"                color=InkChroma268(color,p);",
    "                // Candidate changed banks and nearby dry lake shores; protection is encoded as exact zero."+newline+
    "                float2 watershedUV295=p.xz/float2(4000,6000);"+newline+
    "                float watershedMask295=SAMPLE_TEXTURE2D_LOD(_WatershedToneMask295,sampler_GroundPathMask,saturate(watershedUV295),0).r;"+newline+
    "                watershedMask295*=step(0,watershedUV295.x)*step(watershedUV295.x,1)*step(0,watershedUV295.y)*step(watershedUV295.y,1);"+newline+
    "                float3 watershedTone295=float3(.90,.97,.94)*lerp(.86,.64,saturate((1-n.y)*2));"+newline+
    "                color*=lerp(float3(1,1,1),watershedTone295,watershedMask295);"+newline+
    "                color=InkChroma268(color,p);");
   DevSceneKit.EnsureFolder(A295+"/Shaders");DevSceneKit.EnsureFolder(A295+"/Dressing");
   bool surfaceChanged=WriteToneBytes295(surfacePath,new UTF8Encoding(false).GetBytes(surface));
   if(surfaceChanged)AssetDatabase.ImportAsset(surfacePath,ImportAssetOptions.ForceUpdate);
   bool shaderChanged=WriteToneBytes295(shaderPath,new UTF8Encoding(false).GetBytes(shader));
   if(shaderChanged||surfaceChanged||AssetDatabase.LoadAssetAtPath<Shader>(shaderPath)==null)AssetDatabase.ImportAsset(shaderPath,ImportAssetOptions.ForceUpdate);

   // Expand each shoreline sample into only the 64m cells within the search radius.
   // A pixel inspects the small local list instead of searching every shore sample.
   const float shoreRadius=220,shoreCell=64;
   var shoreCells=new Dictionary<Vector2Int,List<Vector2>>();
   foreach(var point in hydro.Lake.ShorePoints)
   {
    var p=new Vector2(point.x,point.z);
    for(int z=Mathf.FloorToInt((p.y-shoreRadius)/shoreCell);z<=Mathf.FloorToInt((p.y+shoreRadius)/shoreCell);z++)
    for(int x=Mathf.FloorToInt((p.x-shoreRadius)/shoreCell);x<=Mathf.FloorToInt((p.x+shoreRadius)/shoreCell);x++)
    {
     float dx=Mathf.Max(x*shoreCell-p.x,p.x-(x+1)*shoreCell,0),dz=Mathf.Max(z*shoreCell-p.y,p.y-(z+1)*shoreCell,0);
     if(dx*dx+dz*dz>shoreRadius*shoreRadius)continue;
     var key=new Vector2Int(x,z);if(!shoreCells.TryGetValue(key,out var list))shoreCells[key]=list=new List<Vector2>();list.Add(p);
    }
   }
   var waterBytes=hydro.WaterLevels!=null?hydro.WaterLevels.bytes:null;
   if(waterBytes==null||waterBytes.Length!=hydro.Width*hydro.Height*4)throw new InvalidOperationException("295 lake shore tone requires the verified water height field.");
   var water=new float[hydro.Width*hydro.Height];Buffer.BlockCopy(waterBytes,0,water,0,waterBytes.Length);
   var pixels=new Color32[width*height];int changed=0,protectedPixels=0,shorePixels=0,shoreOnlyPixels=0;float weightSum=0;
   for(int z=0;z<height;z++)for(int x=0;x<width;x++)
   {
    float wx=(x+.5f)*cell,wz=(z+.5f)*cell;
    float y=field.Sample(wx,wz),delta=Mathf.Abs(y-oldField.Sample(wx,wz));
    float deltaWeight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(2,10,delta)),shoreWeight=0;
    // Four-metre mask pixel centres coincide with the centre of a terrain cell.
    // Taking the highest neighbouring candidate plane conservatively excludes all wet shore edges.
    int at=z*hydro.Width+x;
    float waterY=Mathf.Max(water[at],water[at+1],water[at+hydro.Width],water[at+hydro.Width+1]);
    if(y<=hydro.Lake.Level+160&&(waterY<-9000||y>=waterY+.01f)&&
     shoreCells.TryGetValue(new Vector2Int(Mathf.FloorToInt(wx/shoreCell),Mathf.FloorToInt(wz/shoreCell)),out var nearby))
    {
     var p=new Vector2(wx,wz);float nearest=shoreRadius*shoreRadius;
     foreach(var point in nearby)nearest=Mathf.Min(nearest,(p-point).sqrMagnitude);
     shoreWeight=.65f*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(80,shoreRadius,Mathf.Sqrt(nearest))));
    }
    float weight=Mathf.Max(deltaWeight,shoreWeight);
    if(weight>0)
    {
     // Four metres beyond protected height nodes plus two metres for the bilinear mask footprint.
     const float guard=6;
     int x0=Mathf.Max(0,Mathf.CeilToInt((wx-guard)/cell)),x1=Mathf.Min(hydro.Width-1,Mathf.FloorToInt((wx+guard)/cell));
     int z0=Mathf.Max(0,Mathf.CeilToInt((wz-guard)/cell)),z1=Mathf.Min(hydro.Height-1,Mathf.FloorToInt((wz+guard)/cell));
     bool blocked=false;
     for(int pz=z0;pz<=z1&&!blocked;pz++)for(int px=x0;px<=x1;px++)if(protection[pz*hydro.Width+px]!=0){blocked=true;break;}
     if(blocked){weight=0;shoreWeight=0;protectedPixels++;}
    }
    byte value=(byte)Mathf.RoundToInt(weight*255);pixels[z*width+x]=new Color32(value,0,0,255);
    if(value>0){changed++;weightSum+=value/255f;if(shoreWeight>0){shorePixels++;if(deltaWeight<=0)shoreOnlyPixels++;}}
   }
   string maskPath=A295+"/Dressing/WatershedToneMask295.png";
   var texture=new Texture2D(width,height,TextureFormat.RGBA32,false,true);texture.SetPixels32(pixels);texture.Apply(false,false);
   byte[] png=texture.EncodeToPNG();Object.DestroyImmediate(texture);bool maskChanged=WriteToneBytes295(maskPath,png);
   if(maskChanged||AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath)==null)AssetDatabase.ImportAsset(maskPath,ImportAssetOptions.ForceUpdate);
   var importer=(TextureImporter)AssetImporter.GetAtPath(maskPath);
   if(importer.textureType!=TextureImporterType.Default||importer.sRGBTexture||importer.mipmapEnabled||
    importer.wrapMode!=TextureWrapMode.Clamp||importer.filterMode!=FilterMode.Bilinear||importer.npotScale!=TextureImporterNPOTScale.None||
    importer.maxTextureSize!=2048||importer.textureCompression!=TextureImporterCompression.Uncompressed)
   {
    importer.textureType=TextureImporterType.Default;importer.sRGBTexture=false;importer.mipmapEnabled=false;
    importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;importer.npotScale=TextureImporterNPOTScale.None;
    importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
   }
   var privateShader=AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
   if(privateShader==null||ShaderUtil.ShaderHasError(privateShader))throw new InvalidOperationException("295 private ground shader import failed; inspect its Unity compiler messages.");
   ground.shader=privateShader;ground.SetTexture("_WatershedToneMask295",AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath));EditorUtility.SetDirty(ground);
   report.Add("Private terrain tone: affected mask pixels="+changed+" / "+pixels.Length+", protected guard pixels excluded="+protectedPixels+", mean weight="+(weightSum/pixels.Length).ToString("F5")+", local dry shore pixels="+shorePixels+" (outside height-change mask="+shoreOnlyPixels+"). Scope=max(height delta2..10m, dry lake shore weight0.65 fading80..220m below lake+160m); outside both scopes/protection stays zero. Source assets/defaults retained; private surface include changes only the masked albedo resolve; shader bytes changed="+shaderChanged+", mask bytes changed="+maskChanged+". Visual review pending.");
   report.Add("Private lowland material detail: existing soil/rock albedo resolve retains at most 0.30 * tone mask through 500m, fades to baseline by 750m; unchanged near resolve, normals, samplers and mask-zero regions. Private surface include bytes changed="+surfaceChanged+".");
  }
  static bool WriteToneBytes295(string path,byte[] bytes)
  {
   if(File.Exists(path)&&File.ReadAllBytes(path).SequenceEqual(bytes))return false;
   File.WriteAllBytes(path,bytes);return true;
  }
 }
}
