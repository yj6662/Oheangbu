using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.SpellVFX120;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class WoodDeerBuild
 {
  public const string Folder="Assets/_Project/Art/SpellVFX120/WoodDeer";
  public static string Output=>Path.Combine(Vfx120Editor.Output,"WoodDeer");
  public static Vfx120Profile Profile=>FixedWardBuild.Profile("곰");
  static Material Material(string name,Color tint,bool bark=false,bool leaf=false)
  {
   string path=Folder+"/"+name+".mat";var shader=Shader.Find("Oheangbu/WoodDeer");if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Wood deer shader compile failure");
   var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}mat.SetColor("_BaseColor",tint);mat.SetFloat("_Leaf",leaf?1:0);
   if(bark){mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/T_WoodDeer_BaseColor.png"));mat.SetTexture("_NormalMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/T_WoodDeer_Normal.png"));}EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);return mat;
  }
  public static string Build()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stopped editor required");
   var p=Profile;if(!File.Exists(Folder+"/Baseline_Gom.asset"))AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(p),Folder+"/Baseline_Gom.asset");
   var importer=(ModelImporter)AssetImporter.GetAtPath(Folder+"/SM_WoodDeer.fbx");importer.isReadable=true;importer.importAnimation=false;importer.animationType=ModelImporterAnimationType.None;importer.importNormals=ModelImporterNormals.Calculate;importer.normalSmoothingAngle=75;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.SaveAndReimport();
   foreach(string kind in new[]{"BaseColor","Normal"}){var t=(TextureImporter)AssetImporter.GetAtPath(Folder+"/Textures/T_WoodDeer_"+kind+".png");t.textureType=kind=="Normal"?TextureImporterType.NormalMap:TextureImporterType.Default;t.maxTextureSize=2048;t.SaveAndReimport();}
   var body=Material("M_Bark",new Color(.72f,.62f,.48f),true);var roots=Material("M_Root",new Color(.28f,.22f,.15f));var leaves=Material("M_Leaf",new Color(.3f,.43f,.13f),false,true);
   var source=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/SM_WoodDeer.fbx");var model=Object.Instantiate(source);model.name="PF_WoodDeer_Static";ulong tris=0;
   try
   {
    foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>()){renderer.sharedMaterial=renderer.name.Contains("Body")?body:renderer.name.Contains("Leaves")?leaves:roots;var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;for(int sub=0;sub<mesh.subMeshCount;sub++)tris+=mesh.GetIndexCount(sub)/3;}
    if(tris>18000||tris<12543)throw new Exception("Export triangle budget violation: "+tris);
    // Derive four hoof positions from the imported mesh, using actual Unity coordinates.
    var filter=model.GetComponentsInChildren<MeshFilter>().First(x=>x.name.Contains("Body"));var v=filter.sharedMesh.vertices.Select(x=>filter.transform.TransformPoint(x)).ToArray();float min=v.Min(x=>x.y);var points=v.Where(x=>x.y<min+.105f).ToArray();var centers=new Vector3[4];centers[0]=points[0];
    for(int c=1;c<4;c++)centers[c]=points.OrderByDescending(x=>centers.Take(c).Min(y=>(x-y).sqrMagnitude)).First();
    for(int step=0;step<16;step++){var sums=new Vector3[4];var counts=new int[4];foreach(var point in points){int k=Enumerable.Range(0,4).OrderBy(j=>(point-centers[j]).sqrMagnitude).First();sums[k]+=point;counts[k]++;}for(int i=0;i<4;i++)if(counts[i]>0)centers[i]=sums[i]/counts[i];}
    for(int i=0;i<4;i++){int index=i;float hoofMin=points.Where(point=>Enumerable.Range(0,4).OrderBy(j=>(point-centers[j]).sqrMagnitude).First()==index).Min(point=>point.y);var go=new GameObject("Foot_"+i);go.transform.SetParent(model.transform,false);go.transform.position=new Vector3(centers[i].x,hoofMin,centers[i].z);}
    p.WoodDeerPrefab=PrefabUtility.SaveAsPrefabAsset(model,Folder+"/PF_WoodDeer_Static.prefab");
   }finally{Object.DestroyImmediate(model);}
   // Own copy of the KTP bottom hierarchy with a finite diagnostic envelope.
   var seal=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120Editor.AssetRoot+"/FixedWards/Pattern_0.prefab"));
   try{foreach(var ps in seal.GetComponentsInChildren<ParticleSystem>()){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;m.duration=4.6f;m.startLifetime=4.6f;var color=ps.colorOverLifetime;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.4f,.075f),new GradientAlphaKey(.18f,.28f),new GradientAlphaKey(.18f,.825f),new GradientAlphaKey(0,1)});color.color=g;}p.WoodDeerSeal=PrefabUtility.SaveAsPrefabAsset(seal,Folder+"/KTP_Gom_Seal.prefab");}finally{Object.DestroyImmediate(seal);}
   var debris=new GameObject("WoodDeerDissolve");try
   {
    KtpOffsetGuardBuild.Debris(debris,Folder,"LeafRelease",35,new Color(.26f,.39f,.12f),0,.7f,new Vector3(.045f,.08f,.018f),.12f);
    KtpOffsetGuardBuild.Debris(debris,Folder,"WoodRelease",28,new Color(.3f,.23f,.16f),.08f,.6f,new Vector3(.018f,.055f,.012f),.25f);
    foreach(var ps in debris.GetComponentsInChildren<ParticleSystem>()){var main=ps.main;main.startSpeed=new ParticleSystem.MinMaxCurve(.15f,.55f);main.playOnAwake=false;var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(.65f,1.25f,1.2f);shape.position=new Vector3(0,1.3f,0);}
    p.WoodDeerDebris=PrefabUtility.SaveAsPrefabAsset(debris,Folder+"/PF_Gom_Dissolve.prefab");
   }finally{Object.DestroyImmediate(debris);}
   p.WoodDeerPresentation=true;EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"unity_model.json"),"{\"triangles\":"+tris+",\"modelMaterials\":3,\"rigged\":false,\"reviewDuration\":4.6,\"gameplayDurationChanged\":false}");return "WOOD_DEER_BUILT tris="+tris;
  }
 }
}
