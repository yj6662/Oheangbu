using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string PropsOut264=Output+"/RoadProps264", PropsRoot264="CheongrimRoadStories264";
  [Serializable] public class PropSite264 { public string id,theme; public Vector3 center,road; public float yaw; public Vector3 size; }
  [Serializable] public class PropLedger264 { public string scene; public PropSite264[] sites; }
  static Vector3[][] PropPaths264()=>DetailPaths261().Concat(JsonUtility.FromJson<Routes263>(File.ReadAllText(Output263+"/routes.json")).routes.Select(r=>r.points)).ToArray();
  public static string RoadProps264(string command)
  {
   var scene=FrontageScene249();Directory.CreateDirectory(PropsOut264);
   var roots=scene.GetRootGameObjects();var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();
   var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>()).Single();
   var ground=FinalSurface(scene);var paths=PropPaths264();
   if(command=="inventory")return string.Join("\n",paths.Select(p=>p.Length+" "+p[0]+" -> "+p.Last()))+"\nCOLLIDERS\n"+string.Join("\n",roots.Where(g=>g.name!="Compact_Rebuild_Terrain").SelectMany(g=>g.GetComponentsInChildren<Collider>()).Where(c=>!c.isTrigger&&c.bounds.Intersects(new Bounds(new Vector3(3400,ground(3400,1990).point.y,1990),new Vector3(190,20,190)))).Select(c=>c.name+" "+c.bounds));
   if(command=="capture")return CaptureProps264();
   if(command=="checks")return CheckProps264();
   if(command!="build")throw new ArgumentException(command);
   if(scene.isDirty)throw new Exception("Clean candidate required");
   string folder=Path.GetDirectoryName(scene.path).Replace('\\','/')+"/RoadProps264";Directory.CreateDirectory(folder);
   string backup=PropsOut264+"/Before/"+DateTime.UtcNow.ToString("yyyyMMddHHmmss");Directory.CreateDirectory(backup);
   var files=new[]{scene.path,AssetDatabase.GetAssetPath(art.Sheet),Path.GetDirectoryName(scene.path)+"/Navigation.asset"};
   foreach(var f in files)foreach(var suffix in new[]{"",".meta"})if(File.Exists(f+suffix))File.Copy(f+suffix,backup+"/"+Path.GetFileName(f)+suffix,true);
   File.WriteAllLines(backup+"/paths.txt",files);
   var old=roots.SingleOrDefault(g=>g.name==PropsRoot264);if(old!=null)Object.DestroyImmediate(old);
   var root=new GameObject(PropsRoot264).transform;
   T Asset<T>(string name,Func<T> make)where T:Object{var path=folder+"/"+name+".asset";var value=AssetDatabase.LoadAssetAtPath<T>(path);if(value==null){value=make();AssetDatabase.CreateAsset(value,path);}return value;}
   var source=Asset("SourcePlacements",()=>Object.Instantiate(art.Sheet));var sheet=Asset("Placements",()=>Object.Instantiate(source));EditorUtility.CopySerialized(source,sheet);
   var wood=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/ThatchedInn/Materials/house_Re_Wood_01_Muted.mat");
   Material Mat(string name,Color color){var m=Asset(name,()=>new Material(Shader.Find("Universal Render Pipeline/Lit")));m.color=color;m.SetFloat("_Smoothness",.08f);EditorUtility.SetDirty(m);return m;}
   var iron=Mat("PittedIron",new Color(.18f,.19f,.17f));var ore=Mat("CorruptedMineral",new Color(.16f,.26f,.22f));var cord=Mat("Hemp",new Color(.40f,.36f,.27f));
   var stone=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/ThatchedInn/Materials/house_Re_Stone_Muted.mat");
   var roofMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/ThatchedInn/Materials/house_Re_Roof_01_Muted.mat");
   var worn=Asset("WeatheredWood",()=>new Material(wood));if(worn.HasProperty("_BaseColor"))worn.SetColor("_BaseColor",new Color(.47f,.46f,.40f));EditorUtility.SetDirty(worn);
   Bounds BoundsOf(Transform t){var rr=t.GetComponentsInChildren<Renderer>();var b=rr[0].bounds;foreach(var r in rr)b.Encapsulate(r.bounds);return b;}
   Transform Ground(Transform parent,string name,Vector3 local){var t=new GameObject(name).transform;t.SetParent(parent,false);var p=parent.TransformPoint(local);p.y=ground(p.x,p.z).point.y;t.position=p;return t;}
   GameObject Box(Transform p,string n,Vector3 at,Vector3 size,Material m,bool solid=true){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=n;g.transform.SetParent(p,false);g.transform.localPosition=at;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=m;if(!solid)Object.DestroyImmediate(g.GetComponent<Collider>());return g;}
   void Beam(Transform p,string n,Vector3 a,Vector3 b,float w,Material m,bool solid=true){var g=Box(p,n,(a+b)*.5f,new Vector3(w,(b-a).magnitude,w),m,solid);g.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);}
   // Copy meshes only: the stranded vehicle must never retain summon, wheel, audio or interaction scripts.
   Transform Model(Transform parent,string name,string path,Vector3 local,Vector3 size,Vector3 rotation,bool solid=true){
    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(prefab==null)throw new Exception(path);
    var t=new GameObject(name).transform;t.SetParent(parent,false);
    foreach(var f in prefab.GetComponentsInChildren<MeshFilter>(true)){if(f.sharedMesh==null)continue;var r=f.GetComponent<MeshRenderer>();if(r==null)continue;
     var g=new GameObject(f.name,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(t,false);var mat=prefab.transform.worldToLocalMatrix*f.transform.localToWorldMatrix;g.transform.localPosition=mat.GetColumn(3);g.transform.localRotation=mat.rotation;g.transform.localScale=mat.lossyScale;g.GetComponent<MeshFilter>().sharedMesh=f.sharedMesh;g.GetComponent<MeshRenderer>().sharedMaterials=r.sharedMaterials;
     if(solid)g.AddComponent<MeshCollider>().sharedMesh=f.sharedMesh;
    }
    var b=BoundsOf(t);t.localScale=new Vector3(size.x/Mathf.Max(.01f,b.size.x),size.y/Mathf.Max(.01f,b.size.y),size.z/Mathf.Max(.01f,b.size.z));t.localRotation=Quaternion.Euler(rotation);b=BoundsOf(t);var p=parent.TransformPoint(local);p.y=ground(p.x,p.z).point.y;t.position+=p-new Vector3(b.center.x,b.min.y,b.center.z);return t;
   }
   Transform Prop(Transform p,string n,string asset,Vector3 local,Vector3 size,Vector3 rot){var t=Model(p,n,"Assets/HwaseongHaenggung/Prefabs/"+asset+".prefab",local,size,rot);var m=asset.Contains("Roof")?roofMaterial:asset.Contains("Stone")||asset.Contains("chimney")?stone:worn;foreach(var r in t.GetComponentsInChildren<Renderer>())r.sharedMaterials=r.sharedMaterials.Select(_=>m).ToArray();return t;}
   void Pick(Transform p,Vector3 local,int seed){var t=Ground(p,"AbandonedPick"+seed,local);t.localRotation=Quaternion.Euler(0,seed*39,0);Beam(t,"SplitHandle",new Vector3(-.55f,.10f,0),new Vector3(.55f,.18f,0),.055f,worn,false);Beam(t,"IronHead",new Vector3(.40f,.21f,-.31f),new Vector3(.44f,.18f,.28f),.075f,iron,false);
    for(int j=0;j<5;j++){var g=GameObject.CreatePrimitive(PrimitiveType.Sphere);g.name="MineralAccretion";g.transform.SetParent(t,false);g.transform.localPosition=new Vector3(.39f+(j%2)*.035f,.20f,j*.06f-.12f);g.transform.localScale=new Vector3(.11f,.07f,.1f);g.GetComponent<Renderer>().sharedMaterial=ore;Object.DestroyImmediate(g.GetComponent<Collider>());}}
   void Crates(Transform t){Prop(t,"OpenedOreChest","SM_M_WoodenBox",new Vector3(2,0,1),new Vector3(1.1f,.7f,.8f),new Vector3(0,18,0));Prop(t,"SpilledChest","SM_M_WoodenBox",new Vector3(2.8f,0,1.8f),new Vector3(.8f,.5f,.6f),new Vector3(0,-24,25));}
   var entries=new List<PropSite264>();
   var targets=new[]{new Vector3(3300,0,2110),new Vector3(3200,0,2230),new Vector3(2980,0,2330),new Vector3(2770,0,2260),new Vector3(3300,0,2700),new Vector3(3460,0,2770)};
   string[] names={"OreSorting264","PalanquinWreck264","TimberLayby264","AbandonedHome264","TaintedTools264","CollapsedShelter264"};
   string[] themes={"폐광 반출장","버려진 가마 자동차","주막길 목재 적치장","숲길 빈집터","물든 벌목·채광 공구","뿌리 굴 앞 무너진 작업막"};
   var existing=roots.Where(g=>g!=null&&g.name!=PropsRoot264&&g.name!="Compact_Rebuild_Terrain").SelectMany(g=>g.GetComponentsInChildren<Collider>()).Where(c=>!c.isTrigger).Select(c=>c.bounds).ToArray();
   for(int site=0;site<targets.Length;site++){
    // Search actual route shoulders; reject steep ground and existing built structures.
    float best=float.MaxValue;Vector3 center=default,road=default,forward=default;
    foreach(var path in paths)for(int j=1;j<path.Length;j++){
     var a=path[j-1];var b=path[j];a.y=b.y=0;var delta=b-a;float u=Mathf.Clamp01(Vector3.Dot(targets[site]-a,delta)/Mathf.Max(.001f,delta.sqrMagnitude));var near=a+delta*u;
     if(Vector3.Distance(near,targets[site])>95)continue;var tangent=delta.normalized;var side=Vector3.Cross(tangent,Vector3.up);
     foreach(float offset in new[]{12f,-12f,17f,-17f,23f,-23f}){
      var p=near+side*offset;var hit=ground(p.x,p.z);if(hit.collider==null||hit.normal.y<.93f)continue;p=hit.point;
      if(paths.Any(line=>FlatPathDistance(p,line)<9))continue;
      var bounds=new Bounds(p+Vector3.up*2,new Vector3(12,4,12));if(existing.Any(x=>x.Intersects(bounds)))continue;
      float slope=0;foreach(float x in new[]{-5f,5f})foreach(float z in new[]{-5f,5f})slope=Mathf.Max(slope,Mathf.Abs(ground(p.x+x,p.z+z).point.y-p.y));
      if(slope>1.8f)continue;float score=Vector3.Distance(near,targets[site])+Mathf.Abs(offset)*.3f+slope*20;if(score>=best)continue;best=score;center=p;road=ground(near.x,near.z).point;forward=(road-p).normalized;forward.y=0;
     }
    }
    if(best==float.MaxValue)throw new Exception("No safe shoulder: "+names[site]);
    var t=new GameObject(names[site]).transform;t.SetParent(root,false);t.SetPositionAndRotation(center,Quaternion.LookRotation(forward));
    if(site==1){var car=Model(t,"InertPalanquinWreck","Assets/_Project/Art/World/WorldMacro/Palanquin/Prefabs/MagicPalanquin_TEST.prefab",Vector3.zero,new Vector3(2.7f,2.9f,4.7f),new Vector3(5,28,18));
     var wheel=car.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f=>f.name=="Wheel");if(wheel!=null){wheel.transform.SetParent(t,true);wheel.name="DetachedWheel";wheel.transform.position+=t.right*2.8f;wheel.transform.rotation=Quaternion.Euler(75,20,0);var wb=wheel.GetComponent<Renderer>().bounds;wheel.transform.position+=Vector3.up*(ground(wb.center.x,wb.center.z).point.y-wb.min.y-.06f);}
     var cb=BoundsOf(car);car.position+=Vector3.up*(ground(cb.center.x,cb.center.z).point.y-cb.min.y-.23f);
     Beam(t,"SnappedTowBeam",new Vector3(-2.5f,.12f,1.2f),new Vector3(-1.0f,.28f,3.3f),.14f,worn);Crates(t);
    }else if(site==3||site==5){
     Prop(t,"BrokenStoneFooting","SM_W_Stone_1",new Vector3(0,0,-2.5f),new Vector3(6,.6f,.65f),Vector3.zero);
     Prop(t,"RemainingSideFooting","SM_W_Stone_1",new Vector3(-2.8f,0,0),new Vector3(4.5f,.5f,.6f),new Vector3(0,90,0));
     foreach(float x in new[]{-2.6f,2.6f})foreach(float z in new[]{-2f,2f}){var foot=Ground(t,"WeatheredPost",new Vector3(x,0,z));float h=x<0?2.8f:1.5f;Beam(foot,"StandingTimber",Vector3.zero,new Vector3(.12f,h,0),.18f,worn);}
     Prop(t,"FallenLattice","SM_M_LatticeSmall",new Vector3(2,0,0),new Vector3(1.5f,1.7f,.10f),new Vector3(68,12,20));
     Prop(t,"SurvivingRoofFragment","SM_R_RoofOuter_1",new Vector3(-1.2f,0,-1),new Vector3(3.5f,.7f,2.1f),new Vector3(20,6,-17));
     Vector3 OnFloor(Vector3 local,float lift){var p=t.TransformPoint(local);p.y=ground(p.x,p.z).point.y+lift;return t.InverseTransformPoint(p);}
     for(int i=0;i<9;i++)Beam(t,"CollapsedRafter"+i,OnFloor(new Vector3(-2+i*.45f,0,-1.7f),.06f),OnFloor(new Vector3(-1.3f+i*.3f,0,1.4f+i%3*.3f),.06f),.09f,worn);
     var rearPosts=t.Cast<Transform>().Where(p=>p.name=="WeatheredPost").Where(p=>t.InverseTransformPoint(p.position).z<0).OrderBy(p=>t.InverseTransformPoint(p.position).x).ToArray();
     var leftTop=rearPosts[0].GetComponentsInChildren<Renderer>()[0].bounds;var rightTop=rearPosts[1].GetComponentsInChildren<Renderer>()[0].bounds;
     Beam(t,"RemainingRearLintel",t.InverseTransformPoint(new Vector3(leftTop.center.x,leftTop.max.y-.15f,leftTop.center.z)),t.InverseTransformPoint(new Vector3(rightTop.center.x,rightTop.max.y-.15f,rightTop.center.z)),.2f,worn);
     for(int i=0;i<7;i++){var wall=Ground(t,"BrokenWallCourse"+i,new Vector3(-2.4f+i*.75f,0,-2.25f));float h=.5f+Mathf.Abs(Mathf.Sin(i*1.7f))*.9f;Box(wall,"StoneInfill",new Vector3(0,h*.5f,0),new Vector3(.72f,h,.38f),stone);}
     for(int i=0;i<18;i++){float angle=i*2.39f;var chip=Ground(t,"MasonryRubble"+i,new Vector3(Mathf.Sin(angle)*(1.2f+i%4*.7f),0,Mathf.Cos(angle)*(1+i%3*.8f)));Box(chip,"BrokenFootingStone",new Vector3(0,.09f,0),new Vector3(.18f+i%3*.08f,.16f,.3f),stone,false).transform.localRotation=Quaternion.Euler(i*13,i*37,12);}
     if(site==3)Prop(t,"OldHearthChimney","SM_M_brickchimney_1_1",new Vector3(3,0,-2),new Vector3(.8f,1.8f,.8f),new Vector3(0,14,0));else{Pick(t,new Vector3(1,0,2),8);Crates(t);}
    }else{
     var bench=Ground(t,"SortingTrestle",Vector3.zero);Box(bench,"ScoredPlankTop",new Vector3(0,.8f,0),new Vector3(3.1f,.14f,.9f),worn);
     foreach(float x in new[]{-1.2f,1.2f})foreach(float z in new[]{-.32f,.32f})Beam(bench,"Trestle",new Vector3(x,0,z),new Vector3(x,.78f,z),.13f,worn);
     Prop(t,"RoughLogs","SM_M_WoodLog",new Vector3(-2.5f,0,-1.3f),new Vector3(2.5f,.7f,1.2f),new Vector3(0,25,0));Crates(t);
     if(site!=2){for(int i=0;i<3;i++)Pick(t,new Vector3(-1+i*.65f,0,1.6f),i+site*3);for(int i=0;i<7;i++){var chunk=Ground(t,"OreFragment"+i,new Vector3(2+(i%3)*.24f,0,2.5f+(i/3)*.3f));Box(chunk,"FracturedOre",new Vector3(0,.1f,0),new Vector3(.18f,.17f,.25f),ore,false).transform.localRotation=Quaternion.Euler(i*21,i*33,15);}}
     else{var stack=Ground(t,"BoardStack",new Vector3(-2.4f,0,-1.2f));for(int i=0;i<7;i++)Box(stack,"SeasoningBoard"+i,new Vector3(0,.15f+i*.11f,0),new Vector3(3.2f,.09f,.36f),wood,false).transform.localRotation=Quaternion.Euler(0,25,0);}
    }
    if(site==3||site==5)BuryRuin264(t,source,ground);
    var bb=BoundsOf(t);entries.Add(new PropSite264{id=names[site],theme=themes[site],center=center,road=road,yaw=t.eulerAngles.y,size=bb.size});
   }
   var clusters=source.StoryClusters.ToList();var preserves=source.PreservedAreas.ToList();
   foreach(var e in entries){clusters.Add(new Sheet.StoryCluster{Id=e.id,AnchorId=e.id,Theme=e.theme,Realm=RealmId.Cheongrim,Centre=e.center,Radius=9,PlacementNote="RoadProps264: inert environmental story, no reward or interaction."});preserves.Add(new Sheet.PreserveArea{Id=e.id,Centre=e.center,HalfSize=new Vector2(7,7),Yaw=e.yaw,ExcludeProcedural=true});preserves.Add(new Sheet.PreserveArea{Id=e.id+"_approach",Centre=(e.center+e.road)*.5f,HalfSize=new Vector2(3,Vector3.Distance(e.center,e.road)*.5f+1),Yaw=e.yaw,ExcludeProcedural=true});}
   sheet.StoryClusters=clusters.ToArray();sheet.PreservedAreas=preserves.ToArray();sheet.FixedPlacements=source.FixedPlacements.Where(p=>entries.All(e=>Vector2.Distance(new Vector2(p.Position.x,p.Position.z),new Vector2(e.center.x,e.center.z))>9&&FlatPathDistance(p.Position,new[]{e.road,e.center})>3.5f)).ToArray();
   art.Sheet=sheet;manifest.Art=sheet;art.Invalidate();EditorUtility.SetDirty(sheet);EditorUtility.SetDirty(art);EditorUtility.SetDirty(manifest);
   File.WriteAllText(PropsOut264+"/placements.json",JsonUtility.ToJson(new PropLedger264{scene=scene.path,sites=entries.ToArray()},true));
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   if(scene.GetRootGameObjects().Any(g=>g.GetComponent<CompactEnvironmentContact265>()!=null)){Ruins265();Contact265("build");}
   return "Six authored roadside stories saved: "+string.Join(", ",entries.Select(e=>e.theme));
  }
 }
}
