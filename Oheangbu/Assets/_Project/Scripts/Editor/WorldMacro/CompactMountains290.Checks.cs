using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable]sealed class Checks290{public string scope="Candidate Edit-mode physics samples and detached storage fixtures; not manual gameplay or performance/art approval";public List<string> passed=new List<string>(),failed=new List<string>();}
  static string Check290()
  {
   if(SceneManager.GetActiveScene().path!=Scene290)throw new Exception("Candidate required");
   var report=new Checks290();void Check(bool pass,string text){(pass?report.passed:report.failed).Add(text);}
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var layout=s.MountainLayout;
   Check(s.Content.SaveSlot=="world-compact-mountains-290","Isolated candidate save slot");
   Check(WorldMacroProgress.CurrentVersion==8,"Save schema remains v8");
   Check(layout.Mountains.Length==5&&layout.Mountains.Count(m=>m.MainStory)==2,"Five high mountains / two main-story mountains");
   Physics.SyncTransforms();
   foreach(var m in layout.Mountains)
   {
    Check(Length290(m.MainPath)>=800&&Length290(m.MainPath)<=1200,m.Id+": 800–1200 m authored route");
    Check(m.Summit.y-m.Foot.y>=120&&m.Summit.y-m.Foot.y<=240,m.Id+": relative elevation range");
    int missing=0,blocked=0;var examples=new List<string>();var blockNames=new Dictionary<string,int>();
    foreach(var path in new[]{m.MainPath,m.TemplePath,m.ReturnPath})for(int i=4;i<path.Length-4;i+=4)
    {
     var p=Vector3.Lerp(path[i],path[i-1],.14f);var hits=Physics.RaycastAll(p+Vector3.up*.8f,Vector3.down,1.2f,~0,QueryTriggerInteraction.Ignore);
     bool support=hits.Any(h=>h.collider.gameObject.scene==s.gameObject.scene&&h.normal.y>.5f&&Mathf.Abs(h.point.y-p.y)<.22f);
     if(!support){missing++;if(examples.Count<5)examples.Add(p.ToString("F2"));}
     var overlaps=Physics.OverlapCapsule(p+Vector3.up*.5f,p+Vector3.up*1.45f,.29f,~0,QueryTriggerInteraction.Ignore);
     var bad=overlaps.Where(c=>!c.isTrigger&&c.gameObject.scene==s.gameObject.scene&&!c.transform.IsChildOf(s.Walker.transform)).ToArray();
     if(bad.Length>0){blocked++;foreach(var c in bad){string key=c.name+"@"+p.ToString("F1");if(c.name.StartsWith("Terrain_")&&c.Raycast(new Ray(p+Vector3.up*100,Vector3.down),out var under,150)){key+=" meshDelta="+(under.point.y-p.y).ToString("F2");var data=AssetDatabase.LoadAssetAtPath<TerrainData>(A290+"/"+m.Id+"/Height.asset");var b=c.bounds;key+=" heightDelta="+(data.GetInterpolatedHeight((p.x-b.min.x)/b.size.x,(p.z-b.min.z)/b.size.z)-200-p.y).ToString("F2");}blockNames[key]=blockNames.TryGetValue(key,out int n)?n+1:1;}}
    }
    Check(missing==0,m.Id+": route support missing="+missing+" examples="+string.Join(";",examples));
    Check(blocked==0,m.Id+": head/body overlap samples="+blocked+" "+string.Join(",",blockNames.OrderByDescending(k=>k.Value).Take(5).Select(k=>k.Key+":"+k.Value)));
    Check(m.VehicleExclusions.Any(v=>v.Contains(m.Summit))&&m.VehicleExclusions.Any(v=>v.Contains(m.Temple)),m.Id+": summit/temple vehicle policy present");
    var site=Object.FindObjectsByType<GukLiftSite>(FindObjectsSortMode.None).Single(v=>v.Id==m.Id+"_guk");
    Check(site.Valid,m.Id+": valid multi-site Guk definition");
    int ceiling=0;for(float h=.4f;h<site.Height+1.8f;h+=.3f)
     if(Physics.CheckSphere(site.Lower.position+Vector3.up*h,.3f,~0,QueryTriggerInteraction.Ignore))ceiling++;
    Check(ceiling==0,m.Id+": Guk shaft collision samples="+ceiling);
    Check(Physics.Raycast(site.Upper.position+Vector3.up*.3f,Vector3.down,out var landing,.5f)&&Mathf.Abs(landing.point.y-site.Upper.position.y)<.16f,m.Id+": landing support");
   }
   var volume=new CompactWorldLayoutSO.VehicleExclusion{Centre=new Vector3(0,50,0),Size=new Vector3(10,12,10)};
   Check(CompactMountainAccess.Intersects(volume,new Vector3(-20,50,0),new Vector3(20,50,0),0),"Swept vehicle cannot tunnel across mountain volume");
   Check(!CompactMountainAccess.Intersects(volume,new Vector3(-20,0,0),new Vector3(20,0,0),0),"Valley below mountain volume remains allowed");
   TransactionChecks290(report);
   File.WriteAllText(O290+"/checks.json",JsonUtility.ToJson(report,true));return JsonUtility.ToJson(report,true);
  }
  static void TransactionChecks290(Checks290 report)
  {
   void Check(bool pass,string text){if(!pass)throw new Exception(text);report.passed.Add(text);}
   GameObject host=null;string directory=Path.Combine(Path.GetTempPath(),"OheangbuMountain290_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
   WorldMacroPlaytestSession session=null;const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
   void Set(string name,object value)=>typeof(WorldMacroPlaytestSession).GetField(name,flags).SetValue(session,value);
   bool Commit(WorldMacroProgress p){object[] a={p,null,false};return (bool)typeof(WorldMacroPlaytestSession).GetMethod("TryCommitInteraction",flags).Invoke(session,a);}
   try
   {
    var source=WorldMacroProgress.CreateNew("mountain290-fixture",new Vector3(10,20,30),0);source.ledger.currency=23;
    var action=new CompactWorldLayoutSO.MountainAction{Id="mountain_fixture_temple",Reward=160,RecordId="record.mountain_cheongrim_temple",RequiredCompleted=new[]{"device"}};
    Check(!CompactMountainProgression.TryPrepare(source,action,out _,out _),"Locked temple cannot claim completion");
    source.ledger.completed.Add("device");source.ledger.completed.Add("guk_high_reward");source.ui.DiscoverMarker("prior_marker");
    string before=JsonUtility.ToJson(source);
    Check(CompactMountainProgression.TryPrepare(source,action,out var proposal,out _)&&proposal.ledger.currency==183,"One temple reward prepared");
    Check(JsonUtility.ToJson(source)==before,"Proposal leaves live state unchanged");
    host=new GameObject("Mountain290_transaction_fixture"){hideFlags=HideFlags.HideAndDontSave};host.SetActive(false);session=host.AddComponent<WorldMacroPlaytestSession>();session.Actors=Array.Empty<PrologueEncounter>();
    Set("ready",true);Set("lastSafe",source.ledger.position);typeof(WorldMacroPlaytestSession).GetProperty("Progress").SetValue(session,source);
    string blocker=Path.Combine(directory,"blocked");File.WriteAllText(blocker,"intentional write failure");
    Set("store",new AtomicJsonStore<WorldMacroProgress>(Path.Combine(blocker,"save.json"),WorldMacroProgress.Valid));
    Check(!Commit(proposal)&&ReferenceEquals(session.Progress,source)&&JsonUtility.ToJson(source)==before,"Real disk failure publishes no currency/fact/gate change");
    string path=Path.Combine(directory,"save.json");Set("store",new AtomicJsonStore<WorldMacroProgress>(path,WorldMacroProgress.Valid));
    Check(Commit(proposal),"Retry commits full progress");var loaded=new AtomicJsonStore<WorldMacroProgress>(path,WorldMacroProgress.Valid).Load();
    Check(loaded.ledger.currency==183&&loaded.ledger.completed.Contains(action.Id)&&loaded.ledger.completed.Contains("guk_high_reward")&&loaded.ui.discoveredMarkers.Contains("prior_marker"),"Reload preserves temple reward and independent legacy Guk/discovery state");
    Check(!CompactMountainProgression.TryPrepare(loaded,action,out _,out _),"Reloaded duplicate claim rejected");
    var summit=new CompactWorldLayoutSO.MountainAction{Id="mountain_fixture_summit",Reward=120};
    Check(CompactMountainProgression.TryPrepare(loaded,summit,out var summitProposal,out _)&&summitProposal.ledger.currency==303,"Walking summit reward needs no Guk proof and is independent of temple");
    loaded.ledger.currency=int.MaxValue-10;Check(!CompactMountainProgression.TryPrepare(loaded,summit,out _,out _),"Overflow cannot consume reward");
   }
   catch(Exception e){report.failed.Add("Transaction fixture: "+e);}
   finally
   {
    if(session!=null)Set("ready",false);if(host!=null)Object.DestroyImmediate(host);
    // Delete only this explicitly generated child of the system temporary directory.
    if(Path.GetDirectoryName(Path.GetFullPath(directory))==Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)&&Path.GetFileName(directory).StartsWith("OheangbuMountain290_"))Directory.Delete(directory,true);
   }
  }
  static string GukCandidate290()
  {
   var scene=SceneManager.GetActiveScene();if(scene.path!=Scene290)throw new Exception("Candidate only");
   var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var sites=Object.FindObjectsByType<GukLiftSite>(FindObjectsSortMode.None);var rows=new List<string>{"Actual candidate colliders, copied player dimensions, field service; Edit-mode fixture, not manual input."};
   foreach(var site in sites)typeof(GukLiftSite).GetMethod("OnEnable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(site,null);
   var host=new GameObject("Mountain290_GukSceneFixture");SceneManager.MoveGameObjectToScene(host,scene);host.SetActive(false);
   var body=host.AddComponent<CharacterController>();var source=session.Walker.Body;body.height=source.height;body.center=source.center;body.radius=source.radius;body.skinWidth=source.skinWidth;body.stepOffset=source.stepOffset;body.slopeLimit=source.slopeLimit;
   var config=ScriptableObject.CreateInstance<Oheangbu.Combat.CombatConfigSO>();var player=host.AddComponent<Oheangbu.Combat.PlayerVitals>();typeof(Oheangbu.Combat.PlayerVitals).GetField("_config",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(player,config);player.Restore();
   var service=host.AddComponent<FieldSpellService>();service.Configure(body,null,player,session.DemoWoodLiftProfile,()=>true,()=>false,()=>false);
   service.PermanentSupportAllowed=(p,c)=>session.Traversal==null||session.Traversal.IsPermanentDrySupport(p,c);service.DryPlacementAllowed=p=>session.Traversal==null||session.Traversal.IsDry(p);
   try{foreach(var site in sites){body.enabled=false;host.transform.position=site.Lower.position+Vector3.up*(body.height*.5f-body.center.y+.02f);host.SetActive(true);body.enabled=true;Physics.SyncTransforms();
    var cast=new Oheangbu.Spellcraft.SpellCast('국',Oheangbu.Spellcraft.SpellKind.Field,Oheangbu.Core.Domain.Element.Wood,0,default,1);
    if(!service.TryPrepare(cast,out var why)||!service.CommitPrepared()){rows.Add("FAIL "+site.Id+": "+why+" "+service.LastFailure);
     var hits=Physics.RaycastAll(site.Upper.position+Vector3.up*.25f,Vector3.down,.6f);rows.Add("UPPER floor="+string.Join(",",hits.Select(h=>h.collider.name+":"+(h.point.y-site.Upper.position.y).ToString("F3")+" n="+h.normal.ToString("F2"))));
     var rr=body.radius+.025f;rows.Add("UPPER overlaps="+string.Join(",",Physics.OverlapCapsule(site.Upper.position+Vector3.up*(rr+.04f),site.Upper.position+Vector3.up*(body.height-rr+.04f),rr).Select(c=>c.name)));
     service.CancelPrepared();continue;}
    for(int i=0;i<360;i++)service.Tick(1f/60);bool rose=Mathf.Abs(service.CurrentHeight-site.Height)<.05f&&service.PassengerSupported;
    rows.Add((rose?"PASS ":"FAIL ")+site.Id+" ascent "+service.CurrentHeight.ToString("F2")+"/"+site.Height.ToString("F2")+" "+service.LastFailure);
    service.RequestRelease();for(int i=0;i<420&&service.HasPlatform;i++)service.Tick(1f/60);
    rows.Add((!service.HasPlatform?"PASS ":"FAIL ")+site.Id+" descent / release");
   }}finally{player.TakeDamage(float.MaxValue);Object.DestroyImmediate(host);Object.DestroyImmediate(config);foreach(var site in sites)typeof(GukLiftSite).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(site,null);}
   string report=string.Join("\n",rows);File.WriteAllText(O290+"/guk-candidate.txt",report);return report;
  }
  static string Capture290()
  {
   if(SceneManager.GetActiveScene().path!=Scene290)throw new Exception("Candidate required");
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var m=s.MountainLayout.Mountains[0];
   var source=Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None).First(c=>c.gameObject.scene==s.gameObject.scene&&c.cameraType==CameraType.Game);
   var go=new GameObject("Mountain290_review_camera");var camera=go.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
   EditorUtility.CopySerialized(source.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   var eyes=new[]{m.Foot+new Vector3(120,170,-140),m.MainPath[m.MainPath.Length/3]+Vector3.up*1.7f,m.Temple+new Vector3(0,8,-25),m.Summit+new Vector3(0,2,-12)};
   var targets=new[]{m.MainPath[m.MainPath.Length/2],m.MainPath[m.MainPath.Length/3+14]+Vector3.up,m.Temple+Vector3.up*2,m.Summit+Vector3.up};
   var art=Object.FindFirstObjectByType<CompactRebuildArtRenderer>();var oldObserver=art.Observer;art.Observer=camera;
   var rt=new RenderTexture(1600,900,24);var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);var prior=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;
   try{ShaderUtil.allowAsyncCompilation=false;camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=60;
    for(int i=0;i<eyes.Length;i++){camera.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(targets[i]-eyes[i]));camera.Render();camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();File.WriteAllBytes(O290+"/view-"+i+".png",tex.EncodeToPNG());}}
   finally{art.Observer=oldObserver;ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=prior;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}
   return "Captured four candidate Edit-mode views; original camera/renderer/volume retained";
  }
  static string Walk290()
  {
   if(SceneManager.GetActiveScene().path!=Scene290)throw new Exception("Candidate required");
   var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var source=session.Walker.Body;var m=session.MountainLayout.Mountains[0];
   var go=new GameObject("Mountain290_actual_controller_fixture"){hideFlags=HideFlags.HideAndDontSave};go.SetActive(false);var cc=go.AddComponent<CharacterController>();
   cc.height=source.height;cc.center=source.center;cc.radius=source.radius;cc.skinWidth=source.skinWidth;cc.stepOffset=source.stepOffset;cc.slopeLimit=source.slopeLimit;
   var result=new List<string>{"Automated Edit-mode movement with actual player collider dimensions, slope limit and step offset. No input/manual/performance approval."};
   try
   {
    foreach(var route in new[]{(Name:"Cheongrim ascent",Path:m.MainPath),(Name:"Cheongrim descent",Path:m.MainPath.Reverse().ToArray()),(Name:"Hidden branch",Path:m.TemplePath)})
    {
     cc.enabled=false;go.transform.position=route.Path[0]+Vector3.up*(cc.height*.5f-cc.center.y+.06f);go.SetActive(true);cc.enabled=true;Physics.SyncTransforms();
     int target=1,stalled=0,ticks=0;float nearest=float.PositiveInfinity;string failure=null;
     while(target<route.Path.Length&&ticks++<60000)
     {
      var feet=(cc.transform.TransformPoint(cc.center)-Vector3.up*(cc.height*.5f));var delta=route.Path[target]-feet;float y=delta.y;delta.y=0;
      if(delta.magnitude<.18f&&Mathf.Abs(y)<1.2f){target++;nearest=float.PositiveInfinity;stalled=0;continue;}
      if(delta.magnitude<nearest-.015f){nearest=delta.magnitude;stalled=0;}else stalled++;
      if(stalled>240||feet.y<route.Path[target].y-3){failure="at "+feet.ToString("F2")+" target="+target+"/"+route.Path.Length+" height error="+y.ToString("F2");break;}
      var motion=delta.normalized*Mathf.Min(4.5f/60,delta.magnitude);motion.y=-.10f;cc.Move(motion);
     }
     result.Add(route.Name+": "+(failure==null&&target==route.Path.Length?"PASS":"FAIL "+failure)+"; simulated seconds="+(ticks/60f).ToString("F1"));
    }
   }
   finally{Object.DestroyImmediate(go);}
   string text=string.Join("\n",result);File.WriteAllText(O290+"/walk-fixture.txt",text);return text;
  }
 }
}
