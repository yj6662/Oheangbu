using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.SpellVFX120;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class DokkaebiClubBuild
 {
  public const string Folder="Assets/_Project/Art/SpellVFX120/DokkaebiClub";
  public static string Output=>Path.Combine(Vfx120Editor.Output,"DokkaebiClub");
  public static Vfx120Profile Profile=>FixedWardBuild.Profile("몸");
  static Material Material(string name,Color tint,bool bark=false,bool leaf=false)
  {
   string path=Folder+"/"+name+".mat";var shader=Shader.Find("Oheangbu/DokkaebiClub");if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Stone dokkaebi shader compile failure");
   var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}mat.SetColor("_BaseColor",tint);mat.SetFloat("_Leaf",leaf?1:0);
   if(bark){mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/T_DokkaebiClub_BaseColor.png"));mat.SetTexture("_NormalMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/T_DokkaebiClub_Normal.png"));}EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);return mat;
  }
  public static string Build()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stopped editor required");
   var p=Profile;if(!File.Exists(Folder+"/Baseline_Previous.asset"))AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(p),Folder+"/Baseline_Previous.asset");
   var importer=(ModelImporter)AssetImporter.GetAtPath(Folder+"/SM_DokkaebiClub.fbx");importer.isReadable=true;importer.importAnimation=false;importer.animationType=ModelImporterAnimationType.None;importer.importNormals=ModelImporterNormals.Calculate;importer.normalSmoothingAngle=75;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.SaveAndReimport();
   foreach(string kind in new[]{"BaseColor","Normal"}){var t=(TextureImporter)AssetImporter.GetAtPath(Folder+"/Textures/T_DokkaebiClub_"+kind+".png");t.textureType=kind=="Normal"?TextureImporterType.NormalMap:TextureImporterType.Default;t.maxTextureSize=2048;t.SaveAndReimport();}
   var body=Material("M_AgedBronze",Color.white,true);
   var source=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/SM_DokkaebiClub.fbx");var model=new GameObject("PF_DokkaebiClub_Static");var imported=Object.Instantiate(source,model.transform);imported.name="DokkaebiClub_Body";ulong tris=0;
   try
   {
    foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>()){renderer.sharedMaterial=body;var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;for(int sub=0;sub<mesh.subMeshCount;sub++)tris+=mesh.GetIndexCount(sub)/3;}
    if(tris>22000||tris<1000)throw new Exception("Export triangle budget violation: "+tris);
    // Derive two planted foot positions from the imported mesh, using actual Unity coordinates.
    var filter=model.GetComponentsInChildren<MeshFilter>().First(x=>x.name.Contains("Body"));var v=filter.sharedMesh.vertices.Select(x=>filter.transform.TransformPoint(x)).ToArray();float min=v.Min(x=>x.y);var points=v.Where(x=>x.y<min+.105f).ToArray();var centers=new Vector3[3];centers[0]=points[0];
    for(int c=1;c<3;c++)centers[c]=points.OrderByDescending(x=>centers.Take(c).Min(y=>(x-y).sqrMagnitude)).First();
    for(int step=0;step<16;step++){var sums=new Vector3[3];var counts=new int[3];foreach(var point in points){int k=Enumerable.Range(0,3).OrderBy(j=>(point-centers[j]).sqrMagnitude).First();sums[k]+=point;counts[k]++;}for(int i=0;i<3;i++)if(counts[i]>0)centers[i]=sums[i]/counts[i];}
    for(int i=0;i<3;i++){int index=i;float hoofMin=points.Where(point=>Enumerable.Range(0,3).OrderBy(j=>(point-centers[j]).sqrMagnitude).First()==index).Min(point=>point.y);var go=new GameObject("Foot_"+i);go.transform.SetParent(model.transform,false);go.transform.position=points.Where(point=>Enumerable.Range(0,3).OrderBy(j=>(point-centers[j]).sqrMagnitude).First()==index).OrderBy(point=>point.y).First();}
    p.DokkaebiClubPrefab=PrefabUtility.SaveAsPrefabAsset(model,Folder+"/PF_DokkaebiClub_Static.prefab");
   }finally{Object.DestroyImmediate(model);}
   // Own copy of the KTP bottom hierarchy with a finite diagnostic envelope.
   var seal=Object.Instantiate(KtpQuickCastBuild.Pattern(Folder,2,true));
   try{foreach(var ps in seal.GetComponentsInChildren<ParticleSystem>()){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;m.duration=4.6f;m.startLifetime=4.6f;var color=ps.colorOverLifetime;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.55f,.075f),new GradientAlphaKey(.3f,.28f),new GradientAlphaKey(.3f,.825f),new GradientAlphaKey(0,1)});color.color=g;}p.DokkaebiClubSeal=PrefabUtility.SaveAsPrefabAsset(seal,Folder+"/KTP_Mom_Seal.prefab");}finally{Object.DestroyImmediate(seal);}
   var debris=new GameObject("DokkaebiClubDissolve");try
   {
    KtpOffsetGuardBuild.Debris(debris,Folder,"FormationPebbles",28,new Color(.43f,.35f,.25f),0,.75f,new Vector3(.07f,.085f,.06f),-.15f);
    KtpOffsetGuardBuild.Debris(debris,Folder,"DissolveStones",42,new Color(.46f,.39f,.29f),0,.65f,new Vector3(.08f,.1f,.07f),.65f);
    var stone=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/DebrisShard.asset");stone.Clear();stone.vertices=new[]{new Vector3(-.5f,-.4f,-.4f),new Vector3(.4f,-.5f,-.5f),new Vector3(.5f,.4f,-.4f),new Vector3(-.4f,.5f,-.5f),new Vector3(-.4f,-.5f,.5f),new Vector3(.5f,-.4f,.4f),new Vector3(.4f,.5f,.5f),new Vector3(-.5f,.4f,.4f)};stone.triangles=new[]{0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5};stone.RecalculateNormals();stone.RecalculateBounds();EditorUtility.SetDirty(stone);AssetDatabase.SaveAssetIfDirty(stone);
    foreach(var ps in debris.GetComponentsInChildren<ParticleSystem>())
    {
     var main=ps.main;main.startSpeed=new ParticleSystem.MinMaxCurve(.3f,.65f);main.playOnAwake=false;
     var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(1.3f,1.3f,.9f);shape.position=new Vector3(0,.85f,0);
     if(ps.name=="FormationPebbles"){shape.position=new Vector3(0,.06f,0);shape.scale=new Vector3(1.5f,.08f,1.2f);}
    }
    foreach(string name in new[]{"FormationDust","DissolveDust"})
    {
     var go=new GameObject(name);go.transform.SetParent(debris.transform,false);var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
     var main=ps.main;main.playOnAwake=false;main.loop=false;main.duration=.7f;main.startLifetime=.6f;main.startSpeed=new ParticleSystem.MinMaxCurve(.15f,.35f);main.startSize=new ParticleSystem.MinMaxCurve(.22f,.4f);main.startColor=new Color(.47f,.38f,.26f,.3f);main.maxParticles=18;
     var em=ps.emission;em.rateOverTime=0;em.SetBursts(new[]{new ParticleSystem.Burst(0,18)});var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(1.4f,.12f,1f);shape.position=new Vector3(0,.08f,0);
     var colors=ps.colorOverLifetime;colors.enabled=true;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.5f,.15f),new GradientAlphaKey(0,1)});colors.color=g;
     ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/FireBolt/M_Smoke.mat");
    }
    p.DokkaebiClubDebris=PrefabUtility.SaveAsPrefabAsset(debris,Folder+"/PF_Mom_Dissolve.prefab");
   }finally{Object.DestroyImmediate(debris);}
   p.StoneDokkaebiPresentation=false;p.DokkaebiClubPresentation=true;EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"unity_model.json"),"{\"triangles\":"+tris+",\"modelMaterials\":1,\"rigged\":false,\"reviewDuration\":4.6,\"gameplayDurationChanged\":false}");return "DOKKAEBICLUB_BUILT tris="+tris;
  }
 }
}
