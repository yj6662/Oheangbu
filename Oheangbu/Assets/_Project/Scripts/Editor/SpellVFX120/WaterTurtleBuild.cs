using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.SpellVFX120;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class WaterTurtleBuild
 {
  public const string Folder="Assets/_Project/Art/SpellVFX120/WaterTurtle";
  public static string Output=>Path.Combine(Vfx120Editor.Output,"WaterTurtle");
  public static Vfx120Profile Profile=>FixedWardBuild.Profile("옴");
  static Material Material(string name,Color tint,bool bark=false,bool leaf=false)
  {
   string path=Folder+"/"+name+".mat";var shader=Shader.Find("Oheangbu/WaterTurtle");if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Water turtle shader compile failure");
   var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}mat.SetColor("_BaseColor",tint);mat.SetFloat("_Leaf",leaf?1:0);
   if(bark){mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/T_WaterTurtle_BaseColor.png"));mat.SetTexture("_NormalMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/T_WaterTurtle_Normal.png"));}EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);return mat;
  }
  public static string Build()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stopped editor required");
   var p=Profile;if(!File.Exists(Folder+"/Baseline_Previous.asset"))AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(p),Folder+"/Baseline_Previous.asset");
   var importer=(ModelImporter)AssetImporter.GetAtPath(Folder+"/SM_WaterTurtle.fbx");importer.isReadable=true;importer.importAnimation=false;importer.animationType=ModelImporterAnimationType.None;importer.importNormals=ModelImporterNormals.Calculate;importer.normalSmoothingAngle=75;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.SaveAndReimport();
   foreach(string kind in new[]{"BaseColor","Normal"}){var t=(TextureImporter)AssetImporter.GetAtPath(Folder+"/Textures/T_WaterTurtle_"+kind+".png");t.textureType=kind=="Normal"?TextureImporterType.NormalMap:TextureImporterType.Default;t.maxTextureSize=2048;t.SaveAndReimport();}
   var body=Material("M_TurtleJade",Color.white,true);
   var source=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/SM_WaterTurtle.fbx");var model=new GameObject("PF_WaterTurtle_Static");var imported=Object.Instantiate(source,model.transform);imported.name="WaterTurtle_Body";ulong tris=0;
   try
   {
    foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>()){renderer.sharedMaterial=body;var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;for(int sub=0;sub<mesh.subMeshCount;sub++)tris+=mesh.GetIndexCount(sub)/3;}
    if(tris>22000||tris<1000)throw new Exception("Export triangle budget violation: "+tris);
    // Derive four planted foot positions from the imported mesh, using actual Unity coordinates.
    var filter=model.GetComponentsInChildren<MeshFilter>().First(x=>x.name.Contains("Body"));var v=filter.sharedMesh.vertices.Select(x=>filter.transform.TransformPoint(x)).ToArray();float min=v.Min(x=>x.y);var points=v.Where(x=>x.y<min+.105f).ToArray();var centers=new Vector3[4];centers[0]=points[0];
    for(int c=1;c<4;c++)centers[c]=points.OrderByDescending(x=>centers.Take(c).Min(y=>(x-y).sqrMagnitude)).First();
    for(int step=0;step<16;step++){var sums=new Vector3[4];var counts=new int[4];foreach(var point in points){int k=Enumerable.Range(0,4).OrderBy(j=>(point-centers[j]).sqrMagnitude).First();sums[k]+=point;counts[k]++;}for(int i=0;i<4;i++)if(counts[i]>0)centers[i]=sums[i]/counts[i];}
    for(int i=0;i<4;i++){int index=i;float hoofMin=points.Where(point=>Enumerable.Range(0,4).OrderBy(j=>(point-centers[j]).sqrMagnitude).First()==index).Min(point=>point.y);var go=new GameObject("Foot_"+i);go.transform.SetParent(model.transform,false);go.transform.position=points.Where(point=>Enumerable.Range(0,4).OrderBy(j=>(point-centers[j]).sqrMagnitude).First()==index).OrderBy(point=>point.y).First();}
    p.WaterTurtlePrefab=PrefabUtility.SaveAsPrefabAsset(model,Folder+"/PF_WaterTurtle_Static.prefab");
   }finally{Object.DestroyImmediate(model);}
   // Own copy of the KTP bottom hierarchy with a finite diagnostic envelope.
   var seal=Object.Instantiate(KtpQuickCastBuild.Pattern(Folder,4,true));
   try{foreach(var ps in seal.GetComponentsInChildren<ParticleSystem>()){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;m.duration=4.6f;m.startLifetime=4.6f;var color=ps.colorOverLifetime;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.55f,.075f),new GradientAlphaKey(.3f,.28f),new GradientAlphaKey(.3f,.825f),new GradientAlphaKey(0,1)});color.color=g;}p.WaterTurtleSeal=PrefabUtility.SaveAsPrefabAsset(seal,Folder+"/KTP_Om_Seal.prefab");}finally{Object.DestroyImmediate(seal);}
   var debris=new GameObject("WaterTurtleDissolve");try
   {
    KtpOffsetGuardBuild.Debris(debris,Folder,"FormationDroplets",28,new Color(.38f,.64f,.69f),0,.75f,new Vector3(.07f,.085f,.06f),-.15f);
    KtpOffsetGuardBuild.Debris(debris,Folder,"DissolveWater",42,new Color(.38f,.63f,.7f),0,.65f,new Vector3(.08f,.1f,.07f),.65f);
    // Rounded droplet mesh, shared by both short water bursts.
    var droplet=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/DebrisShard.asset");
    var verts=new System.Collections.Generic.List<Vector3>();var indices=new System.Collections.Generic.List<int>();
    const int rings=6, segments=8;
    for(int y=0;y<=rings;y++){float phi=Mathf.PI*y/rings;for(int x=0;x<=segments;x++){float theta=2*Mathf.PI*x/segments;verts.Add(new Vector3(Mathf.Sin(phi)*Mathf.Cos(theta),Mathf.Cos(phi),Mathf.Sin(phi)*Mathf.Sin(theta))*.5f);}}
    for(int y=0;y<rings;y++)for(int x=0;x<segments;x++){int a=y*(segments+1)+x,b=a+segments+1;indices.AddRange(new[]{a,b,a+1,a+1,b,b+1});}
    droplet.Clear();droplet.SetVertices(verts);droplet.SetTriangles(indices,0);droplet.RecalculateNormals();droplet.RecalculateBounds();EditorUtility.SetDirty(droplet);AssetDatabase.SaveAssetIfDirty(droplet);
    foreach(var ps in debris.GetComponentsInChildren<ParticleSystem>())
    {
     var main=ps.main;main.startSpeed=new ParticleSystem.MinMaxCurve(.3f,.65f);main.playOnAwake=false;
     var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(2.2f,1.1f,2.6f);shape.position=new Vector3(0,.85f,0);
     if(ps.name=="FormationDroplets"){shape.position=new Vector3(0,.06f,0);shape.scale=new Vector3(2.4f,.08f,3f);}
    }
    foreach(string name in new[]{"FormationMist","DissolveMist"})
    {
     var go=new GameObject(name);go.transform.SetParent(debris.transform,false);var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
     var main=ps.main;main.playOnAwake=false;main.loop=false;main.duration=.7f;main.startLifetime=.6f;main.startSpeed=new ParticleSystem.MinMaxCurve(.15f,.35f);main.startSize=new ParticleSystem.MinMaxCurve(.22f,.4f);main.startColor=new Color(.60f,.79f,.81f,.22f);main.maxParticles=18;
     var em=ps.emission;em.rateOverTime=0;em.SetBursts(new[]{new ParticleSystem.Burst(0,18)});var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(2.6f,.12f,3.2f);shape.position=new Vector3(0,.08f,0);
     var colors=ps.colorOverLifetime;colors.enabled=true;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.5f,.15f),new GradientAlphaKey(0,1)});colors.color=g;
     ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/FireBolt/M_Smoke.mat");
    }
    p.WaterTurtleDebris=PrefabUtility.SaveAsPrefabAsset(debris,Folder+"/PF_Om_Dissolve.prefab");
   }finally{Object.DestroyImmediate(debris);}
   p.StoneDokkaebiPresentation=false;p.WaterTurtlePresentation=true;EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"unity_model.json"),"{\"triangles\":"+tris+",\"modelMaterials\":1,\"rigged\":false,\"reviewDuration\":4.6,\"gameplayDurationChanged\":false}");return "WATERTURTLE_BUILT tris="+tris;
  }
 }
}
