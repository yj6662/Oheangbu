using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string GroundShader296="Oheangbu/Architecture296/KoreanInkGround";
  const string NaturalShader296="Oheangbu/Architecture296/HighlandNatural";
  static readonly string[] NormalColourProperties296={"_FloorMap293","_FloorMapJ293","_FloorMapC293","_FloorMapH293","_FloorMapW293","_BankGravel294","_BankMud294"};
  static readonly string[] NormalProperties296={"_FloorNormal296","_FloorNormalJ296","_FloorNormalC296","_FloorNormalH296","_FloorNormalW296","_BankGravelNormal296","_BankMudNormal296"};
  [Serializable] sealed class SurfaceMaterialRecord296
  {
   public string Path,Name,BeforeShader,AfterShader;
   public string[] NormalSources;
  }
  [Serializable] sealed class SurfaceSourceRecord296 { public string Path,Sha256; }
  [Serializable] sealed class SurfacePassRecord296 { public string Material,Shader,Pass,Variant;public bool Compiled;public string[] Errors; }
  [Serializable] sealed class SurfaceCompileReceipt296 { public bool passed;public string scope="Synchronous Unity compilation of all passes for the selected candidate material keyword/instancing variants; other platform/keyword combinations and actual frames remain separate.";public SurfacePassRecord296[] checks; }
  [Serializable] sealed class SurfaceReport296
  {
   public int Version=1,GroundMaterials,NaturalHighlandMaterials,PreservedHighlandConstructionMaterials;
   public string Contract="Candidate material sampling only. Geometry, water, terrain fields, original shaders/textures, realm colour and renderer culling unchanged.";
   public string Sampling="Three world-anchored stochastic samples, original metre scale, explicit derivatives, matching normal transforms; bounded variance correction around scan mean (not exact histogram preservation).";
   public SurfaceMaterialRecord296[] Materials;
   public SurfaceSourceRecord296[] ProtectedSources;
  }
  static string SurfaceSha296(string path)
  {
   using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
  }
  static bool IsGroundSurface296(Material m)
  {
   string shader=m.shader!=null?m.shader.name:"";
   return shader=="Oheangbu/Watershed295/KoreanInkGround"||shader==GroundShader296;
  }
  static bool IsNaturalHighland296(Material m)
  {
   string shader=m.shader!=null?m.shader.name:"";
   if(shader!="Oheangbu/Reworld293/HighlandGranite"&&shader!=NaturalShader296)return false;
   string name=m.name;
   // Explicit natural material roles. The legacy GUID is Granite.mat, not Worn_steps.mat.
   return name.EndsWith("_Soil",StringComparison.OrdinalIgnoreCase)||
    name.EndsWith("_Granite",StringComparison.OrdinalIgnoreCase)||
    name.EndsWith("_a7b82346e19fca2458dc32d4ea829248",StringComparison.OrdinalIgnoreCase);
  }
  static Texture2D NormalForSurface296(Material material,string colourProperty)
  {
   var colour=material.GetTexture(colourProperty);
   string path=AssetDatabase.GetAssetPath(colour);
   if(string.IsNullOrEmpty(path)||!path.Contains("_diff_"))
    throw new InvalidOperationException("296 natural normal requires a paired CC0 colour scan: "+material.name+" / "+colourProperty+" / "+path);
   string normalPath=path.Replace("_diff_","_nor_gl_");
   var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
   var importer=AssetImporter.GetAtPath(normalPath) as TextureImporter;
   if(normal==null||importer==null||importer.textureType!=TextureImporterType.NormalMap)
    throw new InvalidOperationException("296 paired normal is missing or not imported as a normal map: "+normalPath);
   return normal; // Read-only reference; never change the source importer.
  }
  static void CompileSurfacePasses296(Material[] materials,Shader groundShader,Shader naturalShader,Dictionary<Material,Texture2D[]> normals)
  {
   var records=new List<SurfacePassRecord296>();var variants=new HashSet<string>(StringComparer.Ordinal);
   foreach(var material in materials.Where(m=>IsGroundSurface296(m)||IsNaturalHighland296(m)))
   {
    bool ground=IsGroundSurface296(material);var shader=ground?groundShader:naturalShader;
    string variant=string.Join("|",material.shaderKeywords.OrderBy(k=>k,StringComparer.Ordinal))+";instancing="+material.enableInstancing;
    if(!variants.Add(shader.name+":"+variant))continue;
    var probe=new Material(material){shader=shader,hideFlags=HideFlags.HideAndDontSave};
    try
    {
     if(ground)for(int k=0;k<NormalProperties296.Length;k++)probe.SetTexture(NormalProperties296[k],normals[material][k]);
     for(int pass=0;pass<probe.passCount;pass++)
     {
      ShaderUtil.CompilePass(probe,pass,true);
      var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").Select(m=>m.message).Distinct().ToArray();
      records.Add(new SurfacePassRecord296{Material=AssetDatabase.GetAssetPath(material),Shader=shader.name,Pass=probe.GetPassName(pass),Variant=variant,Compiled=ShaderUtil.IsPassCompiled(probe,pass)&&errors.Length==0,Errors=errors});
     }
    }
    finally{UnityEngine.Object.DestroyImmediate(probe);}
   }
   var result=new SurfaceCompileReceipt296{passed=records.Count>0&&records.All(r=>r.Compiled),checks=records.ToArray()};
   Directory.CreateDirectory(O296+"/Surface");File.WriteAllText(O296+"/Surface/shader-preflight.json",JsonUtility.ToJson(result,true));
   if(!result.passed)throw new InvalidOperationException("296 candidate shader pass preflight failed before material mutation. See Surface/shader-preflight.json.");
  }
  static void Surface296(List<string> report)
  {
   if(report==null)throw new ArgumentNullException(nameof(report));
   string materialsFolder=A296+"/Materials";
   if(!AssetDatabase.IsValidFolder(materialsFolder))throw new InvalidOperationException("Run candidate prepare before Surface296; its private material copies are required.");
   string[] originals={
    A295+"/Shaders/KoreanInkGround295.shader",A295+"/Shaders/KoreanSurface295.hlsl",
    A292+"/Shaders/HighlandGranite293.shader",A292+"/Shaders/HighlandSurface293.hlsl"};
   var protectedSources=originals.Select(p=>new SurfaceSourceRecord296{Path=p,Sha256=SurfaceSha296(p)}).ToArray();
   string[] helpers={"Stochastic296.hlsl","KoreanSurface296.hlsl","HighlandSurface296.hlsl","KoreanInkGround296.shader","HighlandGranite296.shader"};
   foreach(string name in helpers)
   {
    string path=A296+"/Shaders/"+name;
    if(!File.Exists(path))throw new FileNotFoundException("Missing private #296 shader source. Run Tools/Art/build_architecture296_surface.py first.",path);
    AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
   }
   var groundShader=AssetDatabase.LoadAssetAtPath<Shader>(A296+"/Shaders/KoreanInkGround296.shader");
   var naturalShader=AssetDatabase.LoadAssetAtPath<Shader>(A296+"/Shaders/HighlandGranite296.shader");
   if(groundShader==null||naturalShader==null)
    throw new InvalidOperationException("296 private natural surface shader failed Unity import; inspect compiler messages before material assignment.");
   var materials=AssetDatabase.FindAssets("t:Material",new[]{materialsFolder}).Select(AssetDatabase.GUIDToAssetPath)
    .OrderBy(p=>p,StringComparer.Ordinal).Select(AssetDatabase.LoadAssetAtPath<Material>).Where(m=>m!=null).ToArray();
   var grounds=materials.Where(IsGroundSurface296).ToArray();
   if(grounds.Length==0)throw new InvalidOperationException("296 private ground material was not found; the #295 shader remap contract changed.");
   // Validate all paired source normals before mutating any material.
   var normals=grounds.ToDictionary(m=>m,m=>NormalColourProperties296.Select(p=>NormalForSurface296(m,p)).ToArray());
   CompileSurfacePasses296(materials,groundShader,naturalShader,normals);
   var records=new List<SurfaceMaterialRecord296>();int naturalCount=0,preserved=0;
   foreach(var material in materials)
   {
    string path=AssetDatabase.GetAssetPath(material),before=material.shader!=null?material.shader.name:"";
    if(!path.StartsWith(materialsFolder+"/",StringComparison.Ordinal))throw new InvalidOperationException("296 material escaped private folder: "+path);
    string[] sources=Array.Empty<string>();
    if(IsGroundSurface296(material))
    {
     material.shader=groundShader;
     var paired=normals[material];sources=paired.Select(AssetDatabase.GetAssetPath).ToArray();
     for(int k=0;k<NormalProperties296.Length;k++)material.SetTexture(NormalProperties296[k],paired[k]);
    }
    else if(IsNaturalHighland296(material)){material.shader=naturalShader;naturalCount++;}
    else
    {
     if(before=="Oheangbu/Reworld293/HighlandGranite")preserved++;
     continue; // Ordered paving, dressed masonry, stairs, roofs and props retain their shader.
    }
    EditorUtility.SetDirty(material);
    records.Add(new SurfaceMaterialRecord296{Path=path,Name=material.name,BeforeShader=before,AfterShader=material.shader.name,NormalSources=sources});
   }
   foreach(var source in protectedSources)
    if(SurfaceSha296(source.Path)!=source.Sha256)throw new InvalidOperationException("296 surface pass changed a protected source: "+source.Path);
   var result=new SurfaceReport296{GroundMaterials=grounds.Length,NaturalHighlandMaterials=naturalCount,
    PreservedHighlandConstructionMaterials=preserved,Materials=records.ToArray(),ProtectedSources=protectedSources};
   Directory.CreateDirectory(O296+"/Surface");
   File.WriteAllText(O296+"/Surface/surface-materials.json",JsonUtility.ToJson(result,true));
   report.Add("Natural surface296: private ground="+grounds.Length+", natural highland="+naturalCount+", constructed highland materials preserved="+preserved+". Existing source textures, realm tint/distance/masks and all geometry retained. Three-sample world-space variation with bounded scan-mean contrast; paired floor/bank normal maps share their colour coordinates. Ground uses2 inline sampler states (repeat/linear/aniso8 and clamp/linear), highland5. Visual/GPU validation pending.");
  }
 }
}
