using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string Checks263()
  {
   var scene=FrontageScene249();var s=VillageSession();var roots=scene.GetRootGameObjects();var root=roots.Single(g=>g.name==Root263);var report=new List<string>();
   void C(bool ok,string title){report.Add((ok?"PASS ":"FAIL ")+title);}
   var ui=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();var map=ui.MapData;var cat=map.Locations;
   C(cat!=null&&cat.Entries.Length>=24,"24 or more authored arrival areas");
   C(cat.DisplayName(cat.Entries.Single(e=>e.Id=="root_cave"))=="청림 · 뿌리 굴","realm and minor site combined");
   C(cat.DisplayName(cat.Entries.Single(e=>e.Id=="realm_cheongrim"))=="청림","major realm name stays singular");
   C(cat.Entries.Where(e=>e.Priority>0).All(e=>cat.DisplayName(e).Contains(" · ")),"all minor names have a realm");
   C(map.Markers.Where(m=>m.Id=="root_cave"||m.Id=="old_tree").All(m=>m.RequiresArrival&&m.Label.StartsWith("청림 · ")),"new map labels discovered on arrival");
   var cave=map.Zones.Single(z=>z.Id=="root_cave");var floor=cave.FloorPath;
   C(cave.ExploreWalkedPassages&&cave.Illustration!=null,"walked passages and authored cave map connected");
   C(cave.Contains(floor[10]+Vector3.up*.2f)&&!cave.Contains(floor[10]+Vector3.up*9),"sloping cave floor excludes roof");
   C(root.GetComponentsInChildren<CompactCaveAmbience>(true).All(a=>a.ZoneId=="root_cave"),"cave audio bound to new volume");
   var caveRockBounds=root.transform.Cast<Transform>().Where(t=>t.name.StartsWith("GalleryTalus")||t.name.StartsWith("Mouth")).SelectMany(t=>t.GetComponentsInChildren<Renderer>()).Select(r=>r.bounds).ToArray();
   C(floor.All(p=>caveRockBounds.All(b=>!b.Intersects(new Bounds(p+Vector3.up*1.9f,new Vector3(4.8f,3.2f,4.8f))))),"external talus does not intrude into gallery walking/camera envelope");
   var a=s.Actors.Single(e=>e.Id==WorldMacroPlaytestSession.SinmokId);var rig=a.GetComponent<SinmokRigAnimation>();var skins=a.GetComponentsInChildren<SkinnedMeshRenderer>();
   C(skins.Length>=17&&skins.All(r=>r.bones.Length>=25&&r.sharedMesh.boneWeights.Length==r.sharedMesh.vertexCount),"separate skinned trunk roots branches and crown twigs");
   C(skins.All(r=>r.sharedMesh.boneWeights.All(w=>Mathf.Abs(w.weight0+w.weight1-1)<.0001f)),"normalised skin weights");
   C(rig.Idle!=null&&rig.Attacks.Length==4&&rig.Attacks.All(c=>c!=null&&c.length>.9f),"idle plus four authored attack clips");
   var preview=EditorSceneManager.NewPreviewScene();var clone=Object.Instantiate(a.gameObject);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone,preview);
   try{
    var cr=clone.GetComponent<SinmokRigAnimation>();var branch=clone.GetComponentsInChildren<Transform>().Single(t=>t.name=="BranchBase0");var skin=clone.GetComponentsInChildren<SkinnedMeshRenderer>().Single(t=>t.name=="BranchPart0");
    cr.Animator.Rebind();cr.Animator.enabled=false;var mesh=new Mesh();skin.BakeMesh(mesh);var before=mesh.vertices;var rot=branch.localRotation;cr.Attacks[0].SampleAnimation(clone,.5f);skin.BakeMesh(mesh);var after=mesh.vertices;
    C(Quaternion.Angle(rot,branch.localRotation)>20,"slam clip rotates real branch bone");
    C(before.Zip(after,(x,y)=>Vector3.Distance(x,y)).Max()>.5f,"weighted branch geometry deforms with bone");Object.DestroyImmediate(mesh);
   }finally{Object.DestroyImmediate(clone);EditorSceneManager.ClosePreviewScene(preview);}
   C(a.GetComponent<CheongryongCombatController>().Profile.TryValidate(out _),"independent Sinmok TEST attack profile valid");
   C(s.Content.Encounters.Single(e=>e.Id==a.Id).RespawnOnRest==false,"boss permanent defeat policy");
   C(a.GetComponent<CheongryongGrowthController>()==null,"no accidental dragon full-heal mechanic");
   C(s.Content.Campaign.IsValid,"campaign dependencies valid");
   var state=WorldMacroProgress.CreateNew(s.Content.SaveSlot,s.Content.StartFeet,0);state.campaign.CampaignId=s.Content.Campaign.CampaignId;state.ledger.currency=123;
   C(WorldMacroPlaytestSession.TryPrepareSinmokDefeat(state,s.Content.Campaign,out var proposal,out _),"uncontracted first Sinmok defeat prepares");
   C(proposal!=null&&proposal.ledger.currency==383&&state.ledger.currency==123&&!state.defeated.Contains(a.Id),"TEST260 reward clone leaves source untouched");
   C(proposal!=null&&proposal.campaign.Facts.Count(f=>f=="defeated:"+a.Id)==1&&!proposal.ledger.completed.Contains(WorldMacroPlaytestSession.GiyeokUnlockId),"one permanent fact; dragon unlock not granted");
   C(!WorldMacroPlaytestSession.TryPrepareSinmokDefeat(proposal,s.Content.Campaign,out _,out _),"duplicate defeat cannot reward again");
   var dir=Output263+"/FixtureSaves/"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(dir);string file=dir+"/state.json";var store=new AtomicJsonStore<WorldMacroProgress>(file,WorldMacroProgress.Valid);store.Save(state);bool failed=false;
   using(var locked=new FileStream(file+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None)){try{store.Save(proposal);}catch(IOException){failed=true;}}
   C(failed&&JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(file)).ledger.currency==123,"actual atomic write failure retains currency and alive save");store.Save(proposal);
   var loaded=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(file));C(loaded.defeated.Contains(a.Id)&&loaded.ledger.currency==383,"retry and reload preserve one reward");
   C(s.Content.SaveSlot=="world-demo-compact-cave-v4"&&WorldMacroProgress.CurrentVersion==8,"slot and version preserved");
   var sky=root.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldRealmAtmosphere>(true)).Single();C(sky.Catalog==cat&&sky.View==s.Walker.ViewCamera&&sky.TintStrength<=.06f,"camera-local subtle sky uses same catalog");
   C(WorldRealmAtmosphere.Tint(Color.gray,Color.red,0)==Color.gray,"zero sky tint keeps original");
   C(!ShaderUtil.ShaderHasError(Shader.Find("Oheangbu/Compact/MountainMist263")),"mountain mist shader compiles");
   C(root.GetComponentsInChildren<Renderer>(true).All(r=>r.sharedMaterials.All(m=>m!=null&&!ShaderUtil.ShaderHasError(m.shader))),"new geometry material shader references valid");
   var routes=JsonUtility.FromJson<Routes263>(File.ReadAllText(Output263+"/routes.json"));Physics.SyncTransforms();
   foreach(var route in routes.routes){bool connected=true;float length=0;var navPath=new NavMeshPath();var last=route.points[0];foreach(var p in route.points.Where((_,i)=>i%12==0).Concat(new[]{route.points.Last()})){
    if(!NavMesh.SamplePosition(last,out var start,2,NavMesh.AllAreas)||!NavMesh.SamplePosition(p,out var end,2,NavMesh.AllAreas)||!NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,navPath)||navPath.status!=NavMeshPathStatus.PathComplete){connected=false;report.Add("DETAIL disconnected "+route.id+" "+last+" -> "+p);break;}
    for(int i=1;i<navPath.corners.Length;i++)length+=Vector3.Distance(navPath.corners[i-1],navPath.corners[i]);last=p;}
    C(connected,"new NavMesh path "+route.id+" sampled length="+length.ToString("F1"));}
   File.WriteAllLines(Output263+"/checks.txt",report);return string.Join("\n",report);
  }
  static string Capture263()
  {
   var scene=FrontageScene249();var s=VillageSession();var roots=scene.GetRootGameObjects();var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var observer=art.Observer;
   var go=new GameObject("263 offscreen camera"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();cam.CopyFrom(s.Walker.ViewCamera);cam.enabled=false;EditorUtility.CopySerialized(s.Walker.ViewCamera.GetUniversalAdditionalCameraData(),cam.GetUniversalAdditionalCameraData());
   var sky=go.AddComponent<Skybox>();sky.material=new Material(AssetDatabase.LoadAssetAtPath<Material>(Folder263+"/Sky.asset"));
   var fog=AssetDatabase.LoadAssetAtPath<Material>(Folder263+"/MountainMist.asset");var ground=FinalSurface(scene);var rt=new RenderTexture(1920,1080,24);var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);var prior=RenderTexture.active;
   var tree=s.Actors.Single(a=>a.Id==WorldMacroPlaytestSession.SinmokId);var rig=tree.GetComponent<SinmokRigAnimation>();var transforms=tree.GetComponentsInChildren<Transform>();var rotations=transforms.Select(t=>t.localRotation).ToArray();
   var canvasObject=new GameObject("263 name preview",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler)){hideFlags=HideFlags.HideAndDontSave};var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=cam;canvas.planeDistance=.8f;canvas.sortingOrder=55;var scale=canvasObject.GetComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1920,1080);
   var arrival=roots.SelectMany(g=>g.GetComponentsInChildren<WorldLocationArrival>()).Single();var label=WorldLocationArrival.CreateName(canvasObject.transform,arrival.Theme.Font);
   try{art.Observer=cam;cam.targetTexture=rt;cam.aspect=16f/9;cam.fieldOfView=60;cam.clearFlags=CameraClearFlags.Skybox;
    for(int i=0;i<7;i++){
    foreach(var pair in transforms.Select((t,n)=>(t,n)))pair.t.localRotation=rotations[pair.n];
     Vector3 eye,target;
     if(i<3){var foot=tree.transform.position;eye=ground(foot.x-18,foot.z-26).point+Vector3.up*7;target=foot+Vector3.up*7;label.text="청림 · 신목";if(i==1)rig.Attacks[0].SampleAnimation(tree.gameObject,.5f);if(i==2)rig.Attacks[1].SampleAnimation(tree.gameObject,.9f);}
     else if(i==3){eye=ground(3550,2842).point+Vector3.up*1.8f;target=ground(3556,2863).point+Vector3.up*2;label.text="청림 · 뿌리 굴";}
     else if(i==4){eye=ground(3557,2872).point+Vector3.up*1.7f;target=ground(3544,2885).point+Vector3.up*1.6f;label.text="청림 · 뿌리 굴";}
     else{var foot=tree.transform.position;eye=ground(foot.x-55,foot.z-65).point+Vector3.up*7;target=ground(foot.x+150,foot.z+260).point+Vector3.up*65;label.text="청림";}
     fog.SetFloat("_MistClock",i==6?90:0);cam.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));Canvas.ForceUpdateCanvases();cam.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(Output263+"/view-"+i+".png",image.EncodeToPNG());
    }
   }finally{for(int i=0;i<transforms.Length;i++)transforms[i].localRotation=rotations[i];fog.SetFloat("_MistClock",-1);art.Observer=observer;cam.targetTexture=null;RenderTexture.active=prior;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(sky.material);Object.DestroyImmediate(go);Object.DestroyImmediate(canvasObject);}
   return "Seven offscreen 1080p captures. Sampled animation poses and mist clocks, not live combat/play footage.";
  }
 }
}
