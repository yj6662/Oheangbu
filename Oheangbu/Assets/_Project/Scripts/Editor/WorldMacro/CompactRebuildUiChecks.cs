using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Combat;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
 static Oheangbu.Data.World.WorldMacroSheetSO callFixtureSheet,callOriginalSheet;
 static string UiRuntime(string command){
 if(command.StartsWith("ui-cave-explore-"))return CaveExploreCheck(command);
 var s=ArtSession();var ui=PlaytestUiRoot.Instance;var hud=UnityEngine.Object.FindFirstObjectByType<Oheangbu.App.HudController>();
 var call=UnityEngine.Object.FindFirstObjectByType<WorldMacroPalanquinSummon>();var lines=new List<string>();
 void Check(bool ok,string label)=>lines.Add((ok?"PASS ":"FAIL ")+label);
 if(command.StartsWith("ui-capture:")){string name=command.Substring(11);if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^[a-z0-9_-]+$"))throw new Exception("Invalid image name");Directory.CreateDirectory(Output+"/UICaptures");ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output+"/UICaptures/"+name+".png"));return "Game capture queued: "+name;}
 if(command.StartsWith("ui-yaw:")){s.Teleport(s.Content.StartFeet,float.Parse(command.Substring(7)));return "Start pose yaw changed for inspection";}
 if(command=="ui-cave-rays"){
 bool old=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=true;
 try{for(float x=.54f;x<=.64f;x+=.02f)for(float y=.5f;y<=.68f;y+=.06f){
 var ray=s.Walker.ViewCamera.ViewportPointToRay(new Vector3(x,y,0));
 lines.Add(x+","+y+": "+string.Join("; ",Physics.RaycastAll(ray,80,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance).Take(5).Select(h=>h.collider.name+" "+h.point+" mesh="+AssetDatabase.GetAssetPath(h.collider.GetComponent<MeshFilter>()?.sharedMesh)+" air="+CaveAir(h.point,JsonUtility.FromJson<CaveLayoutData>(File.ReadAllText(Output+"/CaveV4/geometry.json"))))));}
 }finally{Physics.queriesHitBackfaces=old;}
 }
 if(command.StartsWith("ui-cave-view:")){
 var data=JsonUtility.FromJson<CaveLayoutData>(File.ReadAllText(Output+"/CaveV4/geometry.json"));
 int index=int.Parse(command.Substring(13));var points=data.main;ui.CloseMenu();foreach(var a in s.Actors)a.gameObject.SetActive(false);
 var target=points[Mathf.Min(index+1,points.Length-1)];var from=points[index];var delta=target-from;
 s.Teleport(from,Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg);return "V4 gallery view "+index;
 }
 if(command=="ui-panel-audit"){
 Check(ui.Page=="일시정지","pause page open");
 Check(ui.GetComponentsInChildren<Transform>(false).Any(t=>t.name=="Folio"),"panel restored");
 Check(!ui.GetComponentsInChildren<Transform>(false).Any(t=>t.name=="PauseSymbols"),"icon strip removed");
 var labels=ui.GetComponentsInChildren<Text>(false).Where(t=>t.enabled).Select(t=>t.text).ToArray();
 Check(labels.Contains("계속하기")&&labels.Contains("타이틀로")&&labels.Contains("설정"),"plain functional menu labels");
 Check(!labels.Any(t=>t.Contains("여정")||t.Contains("五 行 符")),"no ornamental menu copy");
 }
 if(command=="ui-cave-audit"){
 Check(s.Content.SaveSlot=="world-demo-compact-cave-v4","private v4 save slot");
 Check(GameObject.Find("Cave_Intro_Outcrop")==null,"rejected baffle removed");
 Check(s.Content.BranchPath.Length>=4&&ui.MapData.Zones[0].DetailLines.Length==3,"branch loop and tool bay share map ledger");
 foreach(string id in new[]{"mine_inquiry","worker_satchel","mine_tool_marks"}){
 var point=s.Content.Points.Single(p=>p.Id==id);var path=new NavMeshPath();
 bool start=NavMesh.SamplePosition(s.Content.StartFeet,out var a,3,NavMesh.AllAreas),end=NavMesh.SamplePosition(point.Position,out var b,3,NavMesh.AllAreas);
 Check(start&&end&&NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete,"reachable "+id);
 }
 }
 if(command=="ui-focus-map-audit"){
 Check(s.InteractionVisuals.Length==s.Content.Points.Length&&s.InteractionVisuals.All(v=>v.Renderers!=null&&v.Renderers.Length>0),"all seven authored interaction targets have explicit visual bindings");
 Check(s.InteractionVisuals.All(v=>v.Renderers.All(r=>r!=null&&r.sharedMaterials.All(m=>m!=null&&!m.shader.name.Contains("FocusOutline")))),"original target materials preserved");
 Check(s.InteractionVisuals.All(v=>v.Renderers.All(r=>!r.isPartOfStaticBatch)),"focused meshes remain separate from static scenery batches");
 Check(hud.Skin.InteractionLetterOffset<=.2f,"small object-relative F gap");
 Check(ui.MapData.PaintedRelief&&ui.MapData.RegionTiles.Length==1&&ui.MapData.RegionTiles[0].Texture.width>=2800,"dedicated high-resolution regional map");
 var detail=ui.Map.MiniRoot.GetComponentsInChildren<Transform>(true).First(t=>t.name=="CaveDetail");Check(!detail.gameObject.activeSelf,"red cave centerline disabled");
 foreach(var path in new[]{"Interaction/FocusOutline","Interaction/FocusMask","Interaction/WorldLetter","WorldMap/MiniPaper","WorldMap/PaperMapSurface"}){
 var shader=Resources.Load<Shader>(path);Check(shader!=null&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"shader valid "+path);}
 }
 if(command=="ui-survey-audit"){
 Check(ui.MapData.ExploredMap!=null&&ui.MapData.ExploredMap.width>=3000,"surveyed atlas available at whole-map scale");
 Check(ui.MapData.Zones.All(z=>z.Illustration!=null&&z.IllustrationWorldUv.width>0),"cave map owns rock-cut illustration");
 var skin=hud.Skin;Check(skin.InteractionLetterOffset<.2f,"old F configure cannot restore high floating prompt");
 var b=s.InteractionVisuals.Single(v=>v.Id=="geumpyo_inn");Check(b.Renderers.Length>0&&b.Renderers.All(r=>r.name=="house_Re_Door"||r.transform.GetComponentsInParent<Transform>().Any(t=>t.name=="house_Re_Door")),"rest outlines actual inn door");
 var g=new WorldMapDiscoveryGrid(ui.MapData.BoundsMin,ui.MapData.BoundsMax,ui.MapData.Outline);var feet=s.Content.InnCheckpointFeet;var pos=new Vector2(feet.x,feet.z);g.Reveal(pos);var bytes=g.Export();
 var reload=new WorldMapDiscoveryGrid(ui.MapData.BoundsMin,ui.MapData.BoundsMax,ui.MapData.Outline,bytes);
 Check(reload.IsDiscovered(pos),"discovered detail survives serialized discovery bytes fixture");
 Check(!reload.IsDiscovered(new Vector2(3500,5000)),"unvisited region remains undiscovered fixture");
 foreach(var path in new[]{"Interaction/CompactCaveRock","WorldMap/PaperMapSurface","WorldMap/MiniPaper"}){var sh=Resources.Load<Shader>(path);Check(sh!=null&&sh.isSupported&&!ShaderUtil.ShaderHasError(sh),"compiled shader "+path);}
 }
 if(command=="ui-survey-persisted"){
 var grid=new WorldMapDiscoveryGrid(ui.MapData.BoundsMin,ui.MapData.BoundsMax,ui.MapData.Outline);
 bool decoded=WorldMapDiscoveryGrid.TryDecode(s.Progress.ui.discoveredCells,grid.ByteCount,out var bytes);Check(decoded,"actual loaded progress has discovery grid");
 if(decoded){grid=new WorldMapDiscoveryGrid(ui.MapData.BoundsMin,ui.MapData.BoundsMax,ui.MapData.Outline,bytes);var inn=s.Content.InnCheckpointFeet;var mine=s.Content.StartFeet;Check(grid.IsDiscovered(new Vector2(inn.x,inn.z))&&grid.IsDiscovered(new Vector2(mine.x,mine.z)),"actual progress retains mine and inn detail");Check(!grid.IsDiscovered(new Vector2(3500,5000)),"actual progress leaves unvisited north hidden");}
 Check(s.Progress.ledger.checkpoint=="geumpyo_inn","committed door rest checkpoint restored");
 }
 if(command=="ui-survey-door-pose"){
 ui.CloseMenu();foreach(var a in s.Actors)a.gameObject.SetActive(false);var p=s.Content.Points.Single(p=>p.Id=="geumpyo_inn");var from=s.Content.InnCheckpointFeet;var d=p.Position-from;d.y=0;var feet=p.Position-d.normalized*1.2f;
 var floorHits=Physics.RaycastAll(feet+Vector3.up*3,Vector3.down,6,~0,QueryTriggerInteraction.Ignore).Where(h=>h.collider!=s.Walker.Body&&h.normal.y>.65f&&h.point.y<p.Position.y+1.3f&&h.point.y>p.Position.y-.9f).OrderByDescending(h=>h.point.y).ToArray();
 if(floorHits.Length==0)throw new Exception("No supported door approach");feet=floorHits[0].point+Vector3.up*.08f;
 s.Teleport(feet,Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg);typeof(PlayerMotor).GetField("_pitch",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(s.Walker.Motor,-8f);return "Door approach pose="+feet+" target="+p.Position;
 }
 if(command=="ui-look-down"){
 typeof(PlayerMotor).GetField("_pitch",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(s.Walker.Motor,35f);return "Pitch 35 degrees after settling";
 }
 if(command=="ui-focus-diagnostic"){
 lines.Add("focus="+s.FocusedId+" feet="+s.Walker.Body.transform.position+" camera="+s.Walker.ViewCamera.transform.position+" rot="+s.Walker.ViewCamera.transform.eulerAngles+" hp="+s.Walker.Body.GetComponent<PlayerVitals>().Hp01+" gate="+s.GameplayInputBlocked+" motor="+s.Walker.Motor.enabled+" time="+Time.timeScale+" pitch="+typeof(PlayerMotor).GetField("_pitch",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(s.Walker.Motor));
 if(s.TryGetFocusedInteractionBounds(out var b)){
 var anchor=new Vector3(b.center.x,b.max.y+hud.Skin.InteractionLetterOffset+hud.Skin.InteractionLetterHeight*.5f,b.center.z);var delta=anchor-s.Walker.ViewCamera.transform.position;
 foreach(var r in s.FocusedRenderers){lines.Add("source="+r.name+" bounds="+r.bounds+" static="+r.isPartOfStaticBatch+" transform="+r.transform.position+" mat="+r.sharedMaterial.shader.name);
 foreach(var shell in r.GetComponentsInChildren<Renderer>().Where(x=>x.name=="FocusedOutline"))lines.Add("shell "+shell.sharedMaterial.shader.name+" queue="+shell.sharedMaterial.renderQueue+" active="+shell.enabled+" bounds="+shell.bounds+" center="+(shell.sharedMaterial.HasProperty("_CenterWS")?shell.sharedMaterial.GetVector("_CenterWS").ToString():"mask"));}
 lines.Add("bounds="+b+" anchor="+anchor+" viewport="+s.Walker.ViewCamera.WorldToViewportPoint(anchor));
 foreach(var h in Physics.RaycastAll(s.Walker.ViewCamera.transform.position,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))lines.Add("ray="+h.collider.name+" root="+h.transform.root.name+" point="+h.point+" playerRoot="+s.Walker.Body.transform.root.name);
 }
 }
 if(command.StartsWith("ui-focus-pose:")){
 ui.CloseMenu();foreach(var a in s.Actors)a.gameObject.SetActive(false);
 string id=command.Substring(14);var p=s.Content.Points.Single(p=>p.Id==id);
 var offset=id=="worker_satchel"?new Vector3(1.4f,0,0):new Vector3(1.8f,0,-1.1f);
 var d=-offset;s.Teleport(p.Position+offset,Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg);
 typeof(PlayerMotor).GetField("_pitch",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(s.Walker.Motor,30f);
 return "Normal shoulder camera pitched down at "+id;
 }
 if(command=="ui-map-current"){ui.OpenPage("지도");ui.Map.FocusCurrent();return "Current region map";}
 if(command=="ui-focus-hidden"){
 var presenter=UnityEngine.Object.FindFirstObjectByType<WorldMacroPlaytestHudPresenter>();
 Check(presenter.OutlineCount==0,"outline removed when interaction unavailable");
 var c=Resources.FindObjectsOfTypeAll<Canvas>().FirstOrDefault(c=>c.gameObject.scene.IsValid()&&c.name=="WorldInteractionPrompt");
 Check(c==null||!c.gameObject.activeInHierarchy,"F removed with focus");
 }
 if(command=="ui-map-whole"){ui.OpenPage("지도");ui.Map.ShowWholeWorld();return "Whole illustrated map";}
 if(command=="ui-f-inspect"){
 var prompt=Resources.FindObjectsOfTypeAll<Canvas>().FirstOrDefault(c=>c.gameObject.scene.IsValid()&&c.name=="WorldInteractionPrompt");
 bool available=s.TryGetFocusedInteractionPosition(out var point);
 Check(!hud.InteractionVisible,"old screen-space interaction panel hidden");
 Check(prompt!=null&&prompt.renderMode==RenderMode.WorldSpace,"interaction canvas uses World Space");
 if(prompt!=null){
 var letters=prompt.GetComponentsInChildren<Text>(true);
 lines.Add("INFO focus="+s.FocusedId+" world="+prompt.transform.position+" scale="+prompt.transform.lossyScale+" parent="+prompt.transform.parent.name+" rootCanvas="+prompt.rootCanvas.name+" enabled="+prompt.enabled+" camera="+s.Walker.ViewCamera.name+" viewport="+s.Walker.ViewCamera.WorldToViewportPoint(prompt.transform.position));
 foreach(var t in letters)lines.Add("INFO text active="+t.isActiveAndEnabled+" color="+t.color+" font="+t.font+" verts="+t.cachedTextGenerator.vertexCount+" rect="+t.rectTransform.rect+" local="+t.transform.localPosition+" cull="+t.canvasRenderer.cull+" shader="+t.material.shader.name);

 Check(letters.Length==1&&letters[0].text=="F"&&!letters[0].raycastTarget,"only a non-intercepting F letter");
 Check(prompt.GetComponentsInChildren<Image>(true).Length==0,"no paper, icon, or background");
 Check(prompt.gameObject.activeInHierarchy==available,"prompt visibility agrees with actual interaction availability at test pose");
 if(available){s.TryGetFocusedInteractionBounds(out var bounds);Check(Vector3.Distance(prompt.transform.position,new Vector3(bounds.center.x,bounds.max.y+hud.Skin.InteractionLetterOffset+hud.Skin.InteractionLetterHeight*.5f,bounds.center.z))<.01f,"F directly above actual bounds");Check(s.GetComponentInChildren<WorldMacroPlaytestHudPresenter>()?.OutlineCount>0||UnityEngine.Object.FindFirstObjectByType<WorldMacroPlaytestHudPresenter>().OutlineCount>0,"focused object outline active");Check(Quaternion.Angle(prompt.transform.rotation,s.Walker.ViewCamera.transform.rotation)<.1f,"F faces gameplay camera");}
 }
 }
 if(command=="ui-f-near"){
 ui.CloseMenu();foreach(var a in s.Actors)a.gameObject.SetActive(false);
 var point=s.Content.Points.Single(p=>p.Id=="worker_satchel");
 s.Teleport(point.Position+Vector3.right*1.4f,270);
 return "Near satchel; wait a frame for focus and camera";
 }
 if(command=="ui-narrative"){
 ui.CloseMenu();s.Teleport(s.Content.Points.Single(p=>p.Id=="worker_satchel").Position+Vector3.right*1.2f,270);
 bool ok=s.Interact("worker_satchel");return "Evidence opened="+ok+" page="+ui.Page;
 }
 if(command.StartsWith("ui-page:")){string page=command.Substring(8);ui.OpenPage(page);return "Opened "+page;}
 if(command=="ui-start-view"){ui.CloseMenu();foreach(var a in s.Actors)a.gameObject.SetActive(false);s.Teleport(s.Content.StartFeet,s.Content.StartYaw);return "Actual player start view, enemies disabled for inspection";}
 if(command=="ui-inn-view"){ui.CloseMenu();foreach(var a in s.Actors)a.gameObject.SetActive(false);s.Teleport(s.Content.Points.Single(p=>p.Id=="geumpyo_inn").Position+Vector3.back*1.5f,0);return "Inn interaction pose; wait for ground probe";}
 if(command=="ui-success-fixture")return VehicleCheck250("prepare");
 if(command=="ui-success-check")return VehicleCheck250("verify");
 if(command=="ui-call"){ui.CloseMenu();bool began=call.TryBeginShortcut(out var message);if(!began)call.TryPresentDeclinedCall();SessionState.SetInt("CompactUI.Calls",call.SuccessfulCalls);SessionState.SetBool("CompactUI.CallExpected",began&&call.TryFindPlacement(s.Walker.Body.transform.position,s.Walker.Body.transform.forward,out _,out _));if(call.Calling){int frame=0;double begin=EditorApplication.timeSinceStartup;UnityEditor.EditorApplication.CallbackFunction capture=null;
 capture=()=>{if(!EditorApplication.isPlaying||EditorApplication.timeSinceStartup-begin>3){EditorApplication.update-=capture;return;}
 if(call.Calling&&call.GestureProgress>=(frame+1)*.23f&&frame<3){Directory.CreateDirectory(Output+"/UICaptures");ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output+"/UICaptures/call_"+frame+".png"));frame++;}};EditorApplication.update+=capture;}
 return "Call gesture="+call.Calling+" accepted="+began+" diagnostic="+message;}
 if(command=="ui-call-check"){
 Check(!call.Calling,"pendant gesture completed");Check(call.SuccessfulCalls==SessionState.GetInt("CompactUI.Calls",-1)+(SessionState.GetBool("CompactUI.CallExpected",false)?1:0),"front clearance, not road tagging, controls call outcome");
 Check(!ui.Gate.InputBlocked,"call releases input gate");Check(!call.TemporaryPendant.gameObject.activeSelf,"pendant stowed after call");
 Check(!ui.GetComponentsInChildren<Text>(false).Any(t=>t.enabled&&!string.IsNullOrWhiteSpace(t.text)),"no operational UI text after call");}
 if(command=="ui-rest-fail"){
 int events=0;Action<Oheangbu.Data.World.PrologueInteractionKind,Vector3> handler=(kind,point)=>{if(kind==Oheangbu.Data.World.PrologueInteractionKind.Rest)events++;};s.InteractionResolved+=handler;
 try{var before=s.Progress.ledger.checkpoint;var facts=s.Progress.campaign.Facts.ToArray();
 string path=Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json.tmp");
 using(var locked=new FileStream(path,FileMode.Create,FileAccess.ReadWrite,FileShare.None)){
 Check(s.CanInteract("geumpyo_inn"),"failed rest fixture is physically reachable");Check(!s.Interact("geumpyo_inn"),"locked save rejects rest");
 Check(events==0&&!s.Walker.Motor.SitRequested,"failed rest emits no success pose or event");Check(s.Progress.ledger.checkpoint==before&&facts.All(f=>s.Progress.campaign.Facts.Contains(f)),"failed rest preserves checkpoint and facts");}
 Check(s.SaveNow(out _),"save retry recovers after lock release");
 }finally{s.InteractionResolved-=handler;}}
 if(command=="ui-rest"){
 Check(s.CanInteract("geumpyo_inn"),"inn interaction reachable");Check(s.Interact("geumpyo_inn"),"rest committed");
 Check(s.Walker.Motor.SitRequested,"committed rest requests ordinary seated posture");Check(ui.Page=="","rest does not open a text menu");Check(s.Progress.ledger.checkpoint=="geumpyo_inn","rest checkpoint persisted");}
 if(command=="ui-audit"){
 Check(hud.Icons!=null&&ui.Theme.Icons==hud.Icons,"candidate UI profile shared");
 Check(hud.GetComponentsInChildren<Text>(false).All(t=>!t.enabled||string.IsNullOrWhiteSpace(t.text)||(t.canvas.renderMode==RenderMode.WorldSpace&&t.text=="F")),"HUD only permits the world-space interaction F");
 Check(ui.Map.MiniRoot.GetComponentsInChildren<Text>(true).All(t=>!t.enabled),"minimap labels replaced");
 var hp=hud.GetComponentsInChildren<Image>(true).Single(i=>i.name=="HP_BrushStroke");var ink=hud.GetComponentsInChildren<Image>(true).Single(i=>i.name=="Ink_BrushBar");
 Check(hp.color==hud.Icons.Health&&ink.color==hud.Icons.Ink&&hp.color!=ink.color,"health dark red and ink blue black");
 Check(ui.MapData.ZoneAt(s.Content.StartFeet)!=null,"new start uses cave map");Check(s.Content.SaveSlot=="world-demo-compact-cave-v4","separate cave revision save");
 Check(ui.MapData.Markers.Any(m=>m.Kind==WorldMapMarkerKind.Mountain)&&!s.Progress.ui.discoveredMarkers.Contains("geumpyo_inn"),"mountain symbols exist; unseen inn not revealed");
 var eye=s.Walker.ViewCamera.transform.position;var rays=0;
 for(int yaw=-30;yaw<=30;yaw+=10)for(int pitch=-10;pitch<=20;pitch+=10){var dir=Quaternion.Euler(pitch,s.Content.StartYaw+yaw,0)*Vector3.forward;if(Physics.RaycastAll(eye,dir,180,~0,QueryTriggerInteraction.Ignore).Any(h=>h.collider.transform.root.name=="mine"))rays++;}
 Check(rays==28,"start forward view fully occluded by mine geometry (28 rays)");
 var body=s.Walker.Body;bool supported=Physics.RaycastAll(body.transform.position+Vector3.up*.4f,Vector3.down,1).Any(h=>h.collider!=body);Check(supported,"new start has supporting ground");
 Check(call.NaturalPresentation&&call.Soundscape!=null,"vehicle natural presentation and softened audio wired");
 }
 string result=command+"\n"+string.Join("\n",lines);File.AppendAllText(Output+"/ui_runtime_checks.txt",result+"\n");return result;
 }
 }
}
