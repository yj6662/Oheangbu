using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.SpellVFX120;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class MetalTigerBuild
 {
  public const string Folder="Assets/_Project/Art/SpellVFX120/MetalTiger";
  public static string Output=>Path.Combine(Vfx120Editor.Output,"MetalTiger");
  public static Vfx120Profile Profile=>FixedWardBuild.Profile("솜");
  static Material Material(string name,Color tint,bool bark=false,bool leaf=false)
  {
   string path=Folder+"/"+name+".mat";var shader=Shader.Find("Oheangbu/MetalTiger");if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Wood deer shader compile failure");
   var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}mat.SetColor("_BaseColor",tint);mat.SetFloat("_Leaf",leaf?1:0);
   if(bark){mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/T_MetalTiger_BaseColor.png"));mat.SetTexture("_NormalMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/T_MetalTiger_Normal.png"));}EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);return mat;
  }
  public static string Build()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stopped editor required");
   var p=Profile;if(!File.Exists(Folder+"/Baseline_Som.asset"))AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(p),Folder+"/Baseline_Som.asset");
   var importer=(ModelImporter)AssetImporter.GetAtPath(Folder+"/SM_MetalTiger.fbx");importer.isReadable=true;importer.importAnimation=false;importer.animationType=ModelImporterAnimationType.None;importer.importNormals=ModelImporterNormals.Calculate;importer.normalSmoothingAngle=75;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.SaveAndReimport();
   foreach(string kind in new[]{"BaseColor","Normal"}){var t=(TextureImporter)AssetImporter.GetAtPath(Folder+"/Textures/T_MetalTiger_"+kind+".png");t.textureType=kind=="Normal"?TextureImporterType.NormalMap:TextureImporterType.Default;t.maxTextureSize=2048;t.SaveAndReimport();}
   var body=Material("M_BrushedSilver",Color.white,true);
   var source=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/SM_MetalTiger.fbx");var model=new GameObject("PF_MetalTiger_Static");var imported=Object.Instantiate(source,model.transform);imported.name="MetalTiger_Body";ulong tris=0;
   try
   {
    foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>()){renderer.sharedMaterial=body;var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;for(int sub=0;sub<mesh.subMeshCount;sub++)tris+=mesh.GetIndexCount(sub)/3;}
    if(tris>18000||tris<12414)throw new Exception("Export triangle budget violation: "+tris);
    // Derive four planted paw positions from the imported mesh, using actual Unity coordinates.
    var filter=model.GetComponentsInChildren<MeshFilter>().First(x=>x.name.Contains("Body"));var v=filter.sharedMesh.vertices.Select(x=>filter.transform.TransformPoint(x)).ToArray();float min=v.Min(x=>x.y);var points=v.Where(x=>x.y<min+.105f).ToArray();var centers=new Vector3[4];centers[0]=points[0];
    for(int c=1;c<4;c++)centers[c]=points.OrderByDescending(x=>centers.Take(c).Min(y=>(x-y).sqrMagnitude)).First();
    for(int step=0;step<16;step++){var sums=new Vector3[4];var counts=new int[4];foreach(var point in points){int k=Enumerable.Range(0,4).OrderBy(j=>(point-centers[j]).sqrMagnitude).First();sums[k]+=point;counts[k]++;}for(int i=0;i<4;i++)if(counts[i]>0)centers[i]=sums[i]/counts[i];}
    for(int i=0;i<4;i++){int index=i;float hoofMin=points.Where(point=>Enumerable.Range(0,4).OrderBy(j=>(point-centers[j]).sqrMagnitude).First()==index).Min(point=>point.y);var go=new GameObject("Foot_"+i);go.transform.SetParent(model.transform,false);go.transform.position=points.Where(point=>Enumerable.Range(0,4).OrderBy(j=>(point-centers[j]).sqrMagnitude).First()==index).OrderBy(point=>point.y).First();}
    p.MetalTigerPrefab=PrefabUtility.SaveAsPrefabAsset(model,Folder+"/PF_MetalTiger_Static.prefab");
   }finally{Object.DestroyImmediate(model);}
   // Own copy of the KTP bottom hierarchy with a finite diagnostic envelope.
   var seal=Object.Instantiate(KtpQuickCastBuild.Pattern(Folder,3,true));
   try{foreach(var ps in seal.GetComponentsInChildren<ParticleSystem>()){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;m.duration=4.6f;m.startLifetime=4.6f;var color=ps.colorOverLifetime;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.55f,.075f),new GradientAlphaKey(.3f,.28f),new GradientAlphaKey(.3f,.825f),new GradientAlphaKey(0,1)});color.color=g;}p.MetalTigerSeal=PrefabUtility.SaveAsPrefabAsset(seal,Folder+"/KTP_Som_Seal.prefab");}finally{Object.DestroyImmediate(seal);}
   var debris=new GameObject("MetalTigerDissolve");try
   {
    KtpOffsetGuardBuild.Debris(debris,Folder,"FormationFilings",24,new Color(.68f,.73f,.76f),0,.6f,new Vector3(.014f,.055f,.005f),-.1f);
    KtpOffsetGuardBuild.Debris(debris,Folder,"DissolveShavings",48,new Color(.58f,.64f,.68f),0,.65f,new Vector3(.012f,.065f,.005f),.22f);
    KtpOffsetGuardBuild.Debris(debris,Folder,"DissolveGlints",18,new Color(.95f,.98f,1f),0,.3f,new Vector3(.014f,.028f,.004f),.05f);
    foreach(var ps in debris.GetComponentsInChildren<ParticleSystem>())
    {
     var main=ps.main;main.startSpeed=new ParticleSystem.MinMaxCurve(.18f,.6f);main.playOnAwake=false;
     var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(.8f,1.1f,2.2f);shape.position=new Vector3(0,.75f,0);
     if(ps.name=="FormationFilings"){shape.position=new Vector3(0,.05f,0);shape.scale=new Vector3(1.1f,.08f,2.5f);}
    }
    p.MetalTigerDebris=PrefabUtility.SaveAsPrefabAsset(debris,Folder+"/PF_Som_Dissolve.prefab");
   }finally{Object.DestroyImmediate(debris);}
   p.MetalTigerPresentation=true;EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"unity_model.json"),"{\"triangles\":"+tris+",\"modelMaterials\":1,\"rigged\":false,\"reviewDuration\":4.6,\"gameplayDurationChanged\":false}");return "METAL_TIGER_BUILT tris="+tris;
  }
 }
}
