using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.SpellVFX120;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class FireHaetaeBuild
 {
  public const string Folder="Assets/_Project/Art/SpellVFX120/FireHaetae";
  public static string Output=>Path.Combine(Vfx120Editor.Output,"FireHaetae");
  public static Vfx120Profile Profile=>FixedWardBuild.Profile("놈");
  static Material Material(string name,Color tint,bool bark=false,bool leaf=false)
  {
   string path=Folder+"/"+name+".mat";var shader=Shader.Find("Oheangbu/FireHaetae");if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Wood deer shader compile failure");
   var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}mat.SetColor("_BaseColor",tint);mat.SetFloat("_Leaf",leaf?1:0);
   if(bark){mat.SetTexture("_EmberMask",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/T_FireHaetae_EmberMask.png"));mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/T_FireHaetae_BaseColor.png"));mat.SetTexture("_NormalMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/T_FireHaetae_Normal.png"));}EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);return mat;
  }
  public static string Build()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stopped editor required");
   var p=Profile;if(!File.Exists(Folder+"/Baseline_Nom.asset"))AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(p),Folder+"/Baseline_Nom.asset");
   var importer=(ModelImporter)AssetImporter.GetAtPath(Folder+"/SM_FireHaetae.fbx");importer.isReadable=true;importer.importAnimation=false;importer.animationType=ModelImporterAnimationType.None;importer.importNormals=ModelImporterNormals.Calculate;importer.normalSmoothingAngle=75;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.SaveAndReimport();
   foreach(string kind in new[]{"BaseColor","Normal","EmberMask"}){var t=(TextureImporter)AssetImporter.GetAtPath(Folder+"/Textures/T_FireHaetae_"+kind+".png");t.textureType=kind=="Normal"?TextureImporterType.NormalMap:TextureImporterType.Default;t.maxTextureSize=2048;t.SaveAndReimport();}
   var body=Material("M_CharcoalEmber",Color.white,true);
   var source=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/SM_FireHaetae.fbx");var model=new GameObject("PF_FireHaetae_Static");var imported=Object.Instantiate(source,model.transform);imported.name="FireHaetae_Body";ulong tris=0;
   try
   {
    foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>()){renderer.sharedMaterial=body;var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;for(int sub=0;sub<mesh.subMeshCount;sub++)tris+=mesh.GetIndexCount(sub)/3;}
    if(tris>18000||tris<12532)throw new Exception("Export triangle budget violation: "+tris);
    // Derive three planted paw positions (the fourth paw is raised in the source pose) from the imported mesh, using actual Unity coordinates.
    var filter=model.GetComponentsInChildren<MeshFilter>().First(x=>x.name.Contains("Body"));var v=filter.sharedMesh.vertices.Select(x=>filter.transform.TransformPoint(x)).ToArray();float min=v.Min(x=>x.y);var points=v.Where(x=>x.y<min+.105f).ToArray();var centers=new Vector3[3];centers[0]=points[0];
    for(int c=1;c<3;c++)centers[c]=points.OrderByDescending(x=>centers.Take(c).Min(y=>(x-y).sqrMagnitude)).First();
    for(int step=0;step<16;step++){var sums=new Vector3[3];var counts=new int[3];foreach(var point in points){int k=Enumerable.Range(0,3).OrderBy(j=>(point-centers[j]).sqrMagnitude).First();sums[k]+=point;counts[k]++;}for(int i=0;i<3;i++)if(counts[i]>0)centers[i]=sums[i]/counts[i];}
    for(int i=0;i<3;i++){int index=i;float hoofMin=points.Where(point=>Enumerable.Range(0,3).OrderBy(j=>(point-centers[j]).sqrMagnitude).First()==index).Min(point=>point.y);var go=new GameObject("Foot_"+i);go.transform.SetParent(model.transform,false);go.transform.position=points.Where(point=>Enumerable.Range(0,3).OrderBy(j=>(point-centers[j]).sqrMagnitude).First()==index).OrderBy(point=>point.y).First();}
    p.FireHaetaePrefab=PrefabUtility.SaveAsPrefabAsset(model,Folder+"/PF_FireHaetae_Static.prefab");
   }finally{Object.DestroyImmediate(model);}
   // Own copy of the KTP bottom hierarchy with a finite diagnostic envelope.
   var seal=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120Editor.AssetRoot+"/FixedWards/Pattern_1.prefab"));
   try{foreach(var ps in seal.GetComponentsInChildren<ParticleSystem>()){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;m.duration=4.6f;m.startLifetime=4.6f;var color=ps.colorOverLifetime;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.4f,.075f),new GradientAlphaKey(.18f,.28f),new GradientAlphaKey(.18f,.825f),new GradientAlphaKey(0,1)});color.color=g;}p.FireHaetaeSeal=PrefabUtility.SaveAsPrefabAsset(seal,Folder+"/KTP_Nom_Seal.prefab");}finally{Object.DestroyImmediate(seal);}
   var debris=new GameObject("FireHaetaeDissolve");try
   {
    KtpOffsetGuardBuild.Debris(debris,Folder,"FormationSparks",36,new Color(1,.28f,.035f),0,.65f,new Vector3(.02f,.04f,.008f),-.1f);
    KtpOffsetGuardBuild.Debris(debris,Folder,"ManeEmbers",16,new Color(.95f,.22f,.025f),0,.5f,new Vector3(.014f,.025f,.006f),-.12f);
    KtpOffsetGuardBuild.Debris(debris,Folder,"DissolveAsh",48,new Color(.22f,.18f,.16f),0,.65f,new Vector3(.025f,.045f,.008f),.08f);
    KtpOffsetGuardBuild.Debris(debris,Folder,"DissolveSparks",28,new Color(1,.24f,.025f),0,.55f,new Vector3(.015f,.03f,.006f),-.15f);
    foreach(var ps in debris.GetComponentsInChildren<ParticleSystem>())
    {
     var main=ps.main;main.startSpeed=new ParticleSystem.MinMaxCurve(.1f,.45f);main.playOnAwake=false;
     var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(.65f,1.1f,1.45f);shape.position=new Vector3(0,.75f,0);
     if(ps.name=="ManeEmbers"){main.loop=true;main.duration=3;var em=ps.emission;em.SetBursts(new ParticleSystem.Burst[0]);em.rateOverTime=8;shape.scale=new Vector3(.42f,.3f,.55f);shape.position=new Vector3(0,1.1f,.65f);}
     if(ps.name=="FormationSparks"){shape.position=new Vector3(0,.06f,0);shape.scale=new Vector3(1.1f,.08f,1.8f);}
    }
    p.FireHaetaeDebris=PrefabUtility.SaveAsPrefabAsset(debris,Folder+"/PF_Nom_Dissolve.prefab");
   }finally{Object.DestroyImmediate(debris);}
   p.FireHaetaePresentation=true;EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"unity_model.json"),"{\"triangles\":"+tris+",\"modelMaterials\":1,\"rigged\":false,\"reviewDuration\":4.6,\"gameplayDurationChanged\":false}");return "FIRE_HAETAE_BUILT tris="+tris;
  }
 }
}
