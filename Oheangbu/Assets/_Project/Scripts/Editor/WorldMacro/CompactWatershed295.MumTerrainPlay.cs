using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;
using UnityEditor;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class MumBridge295PlayChecks
 {
  [Serializable] sealed class TerrainSite295
  {public string id="",realm="";public Vector3 start=Vector3.zero,end=Vector3.zero;}
  [Serializable] sealed class TerrainSites295
  {public string terrainRevision="",sourceHeightSha256="";public float profileMaximumBankEmbedApproach=0f;public TerrainSite295[] sites=Array.Empty<TerrainSite295>(),boundedJoinCandidates=Array.Empty<TerrainSite295>(),rejectedCandidates=Array.Empty<TerrainSite295>();}
  [Serializable] sealed class TerrainStep295
  {public int frame,leg;public Vector3 feet;public float progress,lateral,deckError;public bool bridgeSupport;}
  [Serializable] sealed class TerrainHazardStep295
  {public string cause;public int frame;public Vector3 feet;public float time,immersion,verticalVelocity;public bool drowning,grounded;}
  [Serializable] sealed class TerrainCapture295
  {public string capturedUtc,scene,site,sourceHeightSha256,view;public Vector3 eye,target,playerFeet,bridgeStart,bridgeEnd;public float fieldOfView,span,nearClip,farClip;public bool normalMumUnlocked;public int cullingMask,terrainRenderers,terrainInFrustum,artInstances;}
  sealed partial class State
  {
   public bool terrainMode,hyeongangMode;
   public TerrainSites295 terrainSites;
   public int terrainSiteIndex=-1,terrainLeg,terrainSupportedSamples,terrainSteps;
   public float terrainMaxLateral,terrainMaxDeckError,terrainWalkSeconds;
   public Vector3 terrainStart,terrainEnd;
   public string terrainAcceptedSite;
   public List<string> terrainAttempts=new List<string>();
   public List<TerrainStep295> terrainWalk=new List<TerrainStep295>();
   public List<TerrainHazardStep295> terrainHazards=new List<TerrainHazardStep295>();
   public int terrainHazardDeathCount;
   public string terrainHazardCause="";
   public float terrainHazardStarted,terrainHazardDrowningAt=-1,terrainHazardDeathAt,terrainHazardMinimumVelocity;
   public Vector3 terrainHazardStart,terrainHazardDeathFeet,terrainHazardSafe;
  }
  static MumBridgeBody terrainBridge;
  static PlayerVitals terrainHealth;
  static TerrainSites295 ReadTerrainSites(WorldMacroPlaytestSession session,bool hyeongang=false)
  {
   string file=Path.GetFullPath(Path.Combine(Folder,hyeongang?"../Analysis/mum-hyeongang-sites.json":"../Analysis/mum-sites.json"));
   if(!File.Exists(file))throw new FileNotFoundException("Generate the real bank shortlist before terrain Play verification.",file);
   var sites=JsonUtility.FromJson<TerrainSites295>(File.ReadAllText(file));
   if(sites==null)throw new InvalidDataException("Mum bank shortlist is invalid.");
   if(sites.profileMaximumBankEmbedApproach>session.MumBridgeProfile.MaximumBankEmbedApproach+.001f)
    throw new InvalidOperationException("The candidate Mum profile has not yet received the recorded bank-approach setting.");
   if((sites.sites?.Length??0)==0)sites.sites=(sites.boundedJoinCandidates?.Length??0)>0?sites.boundedJoinCandidates:sites.rejectedCandidates??Array.Empty<TerrainSite295>();
   if(sites.sites.Length==0)throw new InvalidOperationException("No physical bank candidates are recorded. No synthetic bank fallback is allowed in this test.");
   if(session.MountainLayout?.FinalSurface==null)throw new InvalidOperationException("Candidate final terrain is missing.");
   using var sha=SHA256.Create();string actual=BitConverter.ToString(sha.ComputeHash(session.MountainLayout.FinalSurface.bytes)).Replace("-","");
   if(!string.Equals(actual,sites.sourceHeightSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Mum bank shortlist height hash differs from the actual candidate surface. Rebuild or regenerate it first.");
   foreach(var site in sites.sites)
    if(site==null||string.IsNullOrWhiteSpace(site.id)||!FiniteTerrain(site.start)||!FiniteTerrain(site.end))throw new InvalidDataException("Mum bank shortlist contains an invalid endpoint.");
   if(hyeongang&&sites.sites.Any(site=>site.realm!="Hyeongang"))throw new InvalidDataException("The separate Hyeongang verification accepts only explicitly recorded Hyeongang candidates.");
   return sites;
  }
  static bool FiniteTerrain(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
  static void BeginTerrain(WorldMacroPlaytestSession session)
  {
   terrainBridge=null;session.CombatActive=false;session.Cull();
   SelectRest(session);
   Check(session.Interact(run.restId),"Real terrain fixture records an actual dry rest checkpoint in the isolated store.");
   Check(!session.HasMumBridge&&!session.MumBridges.IsUnlocked,"Rest does not grant the normal late-game Mum proof.");
   NextTerrainSite(session,"Beginning real bank candidates.");
  }
  static void NextTerrainSite(WorldMacroPlaytestSession session,string reason)
  {
   if(run.terrainSiteIndex>=0)run.terrainAttempts.Add(run.terrainSites.sites[run.terrainSiteIndex].id+": "+reason);
   session.MumBridges.CancelPrepared();session.SetMum295TestUnlock(false);
   run.terrainSiteIndex++;
   if(run.terrainSiteIndex>=run.terrainSites.sites.Length)
    throw new InvalidOperationException("No real candidate passed runtime placement. "+string.Join(" | ",run.terrainAttempts));
   var site=run.terrainSites.sites[run.terrainSiteIndex];
   PlaceBody(session,site.start+Vector3.up*.06f);Advance(200,"REAL_BANK_GROUNDING: "+site.id);
  }
  static void TickTerrain(WorldMacroPlaytestSession session)
  {
   var service=session.MumBridges;var site=run.terrainSites.sites[run.terrainSiteIndex];
   switch(run.phase)
   {
    case 200:
     if(!Settled())return;
     if(!session.Walker.Motor.IsLocomotionGrounded)
     {if(Settled(4))NextTerrainSite(session,"Actual motor did not ground at recorded dry endpoint.");return;}
     string nearRealm=session.MountainLayout.RealmAt(new Vector2(site.start.x,site.start.z))?.Id;
     string farRealm=session.MountainLayout.RealmAt(new Vector2(site.end.x,site.end.z))?.Id;
     if(string.IsNullOrEmpty(nearRealm)||nearRealm!=farRealm||(!string.IsNullOrEmpty(site.realm)&&site.realm!=nearRealm))
     {NextTerrainSite(session,"Both real banks must retain the recorded authored realm.");return;}
     var cast=new SpellCast('뭄',SpellKind.Field,Element.Earth,0,default,1);
     Check(!service.TryPrepareTo(cast,site.end,out _)&&service.ActiveCount==0&&!service.HasPrepared,
      "Real bank placement remains locked before isolated Mum test unlock: "+site.id);
     string before=JsonUtility.ToJson(session.Progress);session.SetMum295TestUnlock(true);
     Check(!session.HasMumBridge&&JsonUtility.ToJson(session.Progress)==before,"Terrain test unlock changes no normal progression proof.");
     if(!service.TryPrepareTo(cast,site.end,out string failure)){NextTerrainSite(session,"Runtime placement rejected: "+failure);return;}
     if(!service.CommitPrepared()){NextTerrainSite(session,"Runtime commit revalidation rejected the bank pair.");return;}
     terrainBridge=service.Bridges[0];run.terrainAcceptedSite=site.id;run.terrainStart=terrainBridge.Plan.Start;run.terrainEnd=terrainBridge.Plan.End;
     run.terrainAttempts.Add(site.id+": runtime accepted genuine dry terrain banks; span="+terrainBridge.Plan.Span);
     Check(GameObject.Find(BanksName)==null,"Terrain-only verification creates no synthetic bank/support objects.");
     Record("real-bank-accepted",session,site.id+"; no terrain, bank collider or anchor was authored by the fixture.");
     Advance(201,"REAL_BANK_FORMATION");return;
    case 201:
     if(!Complete(session))return;
     Check(terrainBridge.Support.sharedMesh==terrainBridge.Mesh,"Actual terrain bridge collision uses the completed visible mesh.");
     Check(session.TrySafeFeet(site.start,out _)&&session.TrySafeFeet(site.end,out _),"Actual session finds permanent dry support at both riverbank endpoints.");
     CaptureTerrainBridge(session,false);
     run.terrainLeg=0;run.terrainWalkSeconds=0;Advance(202,"CROSSING_REAL_RIVER_OUTBOUND");return;
    case 202:
     WalkTerrain(session);return;
    case 203:
     if(!Settled())return;
     Check(session.CanInteract(run.restId),"Actual rest interaction is reachable after the physical out-and-back river crossing.");
     Check(service.ActiveCount==1,"Real terrain bridge remains until the actual rest transaction.");
     Check(session.Interact(run.restId),"Actual rest commits after the real terrain crossing.");
     Check(service.ActiveCount==0&&(terrainBridge==null||terrainBridge.Support==null||!terrainBridge.Support.enabled),"Actual rest removes the real terrain bridge and its collision.");
     session.SetMum295TestUnlock(false);
     Check(!service.IsUnlocked&&!session.HasMumBridge,"Normal late-game progression stays locked after the real terrain test.");
     Check(session.SaveNow(out string error),"Real-terrain test progression saves only in the isolated store: "+error);
     Record("real-bank-rest-cleanup",session,"Physical crossing completed twice; normal rest removed generated collision.");
     PlaceBody(session,run.checkpoint);Advance(204,"SETTLING_REAL_HAZARD_CHECKPOINT");return;
    case 204:
     if(!Settled()||!session.Walker.Motor.IsLocomotionGrounded)return;
     Check(Mathf.Abs(session.Traversal.Rules.MaximumWadingDepth-.55f)<.001f&&Mathf.Abs(session.Traversal.Rules.DrowningSeconds-.65f)<.001f&&Mathf.Abs(session.Traversal.Rules.FatalFallHeight-6f)<.001f,
      "Actual candidate uses unchanged0.55m deep-water,0.65s drowning and6m fatal-fall thresholds.");
     if(!FindTerrainDrowningBed(session,out var bed,out float water))throw new InvalidOperationException("Actual accepted river span has no unobstructed riverbed deep enough for drowning verification.");
     PrepareTerrainHazard(session,"DeepWater",73,bed+Vector3.up*.04f);
     Record("real-river-drowning-setup",session,"Actual riverbed/water; waterY="+water+"; no synthetic water, collider or direct damage call.");
     Advance(205,"WAITING_FOR_LIVE_RIVER_DROWNING");return;
    case 205:
     SampleTerrainHazard(session,"DeepWater");
     if(!TerrainHazardRecovered(session,"DeepWater",73))return;
     Check(run.terrainHazardDrowningAt>=0&&run.terrainHazardDeathAt-run.terrainHazardDrowningAt>=session.Traversal.Rules.DrowningSeconds-.1f,
      "Actual traversal countdown preceded deep-water death; observed seconds="+(run.terrainHazardDeathAt-run.terrainHazardDrowningAt));
     Record("real-river-drowning-recovered",session,"Real immersion caused DeepWater death and normal checkpoint/currency recovery.");
     PlaceBody(session,site.start+Vector3.up*.06f);Advance(206,"SETTLING_REAL_FALL_BANK");return;
    case 206:
     if(!Settled()||!session.Walker.Motor.IsLocomotionGrounded)return;
     Check(session.TrySafeFeet(Feet(session.Walker.Body),out var fallBank),"Actual fall verification is above a permanent dry riverbank.");
     PrepareTerrainHazard(session,"Fall",91,fallBank+Vector3.up*8f);
     Record("real-dry-bank-fall-setup",session,"Airborne8m setup over unchanged dry bank; subsequent movement is live PlayerMotor gravity only.");
     Advance(207,"WAITING_FOR_LIVE_MOTOR_FATAL_FALL");return;
    case 207:
     SampleTerrainHazard(session,"Fall");
     if(!TerrainHazardRecovered(session,"Fall",91))return;
     Check(run.terrainHazardStart.y-run.terrainHazardDeathFeet.y>=session.Traversal.Rules.FatalFallHeight&&run.terrainHazardMinimumVelocity<-.5f,
      "Live motor gravity caused at least6m descent before Fall death; drop="+(run.terrainHazardStart.y-run.terrainHazardDeathFeet.y)+"; minimum velocity="+run.terrainHazardMinimumVelocity);
     Check(!session.HasMumBridge&&!service.IsUnlocked,"Real hazard recovery grants no normal Mum proof or test unlock.");
     Record("real-dry-bank-fall-recovered",session,"Actual gravity/height threshold triggered Fall death, durable dry drop and normal checkpoint recovery.");
     run.finished=true;Stop(false);return;
   }
  }
  static bool FindTerrainDrowningBed(WorldMacroPlaytestSession session,out Vector3 bed,out float water)
  {
   bed=default;water=0;
   foreach(float fraction in new[]{.5f,.4f,.6f,.3f,.7f})
   {
    var p=Vector3.Lerp(run.terrainStart,run.terrainEnd,fraction);
    if(!session.Traversal.TryWaterHeight(p,out float y))continue;
    var ray=new Ray(new Vector3(p.x,y+1,p.z),Vector3.down);
    var hits=Physics.RaycastAll(ray,40,~0,QueryTriggerInteraction.Ignore);
    Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
    foreach(var hit in hits)
    {
     if(hit.collider.transform.IsChildOf(session.Walker.Body.transform))continue;
     if(hit.collider.attachedRigidbody!=null||hit.collider.GetComponentInParent<WorldTemporarySupport>()!=null||hit.normal.y<.7f||y-hit.point.y<.9f)break;
     bed=hit.point;water=y;return true;
    }
   }
   return false;
  }
  static void PrepareTerrainHazard(WorldMacroPlaytestSession session,string cause,int coins,Vector3 initialFeet)
  {
   UnbindTerrainHealth();terrainHealth=session.Walker.Body.GetComponent<PlayerVitals>();terrainHealth.HpChanged+=TerrainHealthChanged;
   run.terrainHazardDeathCount=0;run.terrainHazardCause="";run.terrainHazardDrowningAt=-1;run.terrainHazardDeathAt=0;run.terrainHazardMinimumVelocity=0;
   run.terrainHazardSafe=session.LastSafeFeet;run.terrainHazardStart=initialFeet;run.terrainHazardStarted=Time.time;
   Check(session.TrySafeFeet(run.terrainHazardSafe,out _),cause+": last-safe drop anchor is actual permanent dry terrain.");
   session.Progress.ledger.currency=coins;
   Check(session.SaveNow(out string error),cause+": pre-hazard currency sentinel saved in isolated store: "+error);
   PlaceBody(session,initialFeet);
  }
  static void UnbindTerrainHealth()
  {if(terrainHealth!=null)terrainHealth.HpChanged-=TerrainHealthChanged;terrainHealth=null;}
  static void TerrainHealthChanged()
  {
   if(run?.active!=true||!run.terrainMode||terrainHealth==null||terrainHealth.Hp01>0)return;
   var session=Session;if(session==null)return;
   run.terrainHazardDeathCount++;run.terrainHazardCause=terrainHealth.LastEnvironmentDeath.ToString();
   run.terrainHazardDeathFeet=Feet(session.Walker.Body);run.terrainHazardDeathAt=Time.time;
   run.terrainHazardMinimumVelocity=Mathf.Min(run.terrainHazardMinimumVelocity,session.Walker.Motor.VerticalVelocity);
   run.terrainHazards.Add(new TerrainHazardStep295{cause="DEATH:"+run.terrainHazardCause,frame=Time.frameCount,time=Time.time,
    feet=run.terrainHazardDeathFeet,immersion=session.Traversal.Immersion(run.terrainHazardDeathFeet),verticalVelocity=session.Walker.Motor.VerticalVelocity,
    drowning=session.IsDrowning,grounded=session.Walker.Motor.IsLocomotionGrounded});Persist();
  }
  static void SampleTerrainHazard(WorldMacroPlaytestSession session,string cause)
  {
   if(run.terrainHazardDeathCount>0)return;
   if(session.IsDrowning&&run.terrainHazardDrowningAt<0)run.terrainHazardDrowningAt=Time.time;
   run.terrainHazardMinimumVelocity=Mathf.Min(run.terrainHazardMinimumVelocity,session.Walker.Motor.VerticalVelocity);
   run.terrainHazards.Add(new TerrainHazardStep295{cause=cause,frame=Time.frameCount,time=Time.time,feet=Feet(session.Walker.Body),
    immersion=session.CurrentImmersion,verticalVelocity=session.Walker.Motor.VerticalVelocity,drowning=session.IsDrowning,grounded=session.Walker.Motor.IsLocomotionGrounded});
   if(Time.time-run.terrainHazardStarted>12)throw new InvalidOperationException(cause+" did not trigger/recover through the live traversal system within12s.");
  }
  static bool TerrainHazardRecovered(WorldMacroPlaytestSession session,string cause,int coins)
  {
   if(run.terrainHazardDeathCount==0)return false;
   Check(run.terrainHazardDeathCount==1&&run.terrainHazardCause==cause,cause+": actual HP event records exactly one expected environmental death.");
   if(terrainHealth.Hp01<1||session.Walker.Motor.EnvironmentalInputBlocked)return false;
   Check(Vector3.Distance(Feet(session.Walker.Body),session.Progress.ledger.checkpointPosition)<.4f,
    cause+": live recovery returns the player to its saved actual checkpoint.");
   var saved=ReadSaved();
   Check(saved.ledger.currency==0&&saved.ledger.dropCurrency==coins&&Vector3.Distance(saved.ledger.dropPosition,run.terrainHazardSafe)<.1f,
    cause+": recovery durably records one currency drop on prior permanent dry support.");
   Check(session.MumBridges.ActiveCount==0,cause+": recovered world contains no leftover generated bridge.");
   UnbindTerrainHealth();return true;
  }
  static void WalkTerrain(WorldMacroPlaytestSession session)
  {
   var body=session.Walker.Body;var health=body.GetComponent<PlayerVitals>();
   if(terrainBridge==null||!terrainBridge.Support.enabled||session.MumBridges.ActiveCount!=1)throw new InvalidOperationException("Generated support disappeared during the real river crossing.");
   if(health.Hp01<=0||session.IsDrowning)throw new InvalidOperationException("Player died or started drowning during actual bridge crossing.");
   var forward=Vector3.ProjectOnPlane(run.terrainEnd-run.terrainStart,Vector3.up).normalized;
   float span=Vector3.ProjectOnPlane(run.terrainEnd-run.terrainStart,Vector3.up).magnitude;
   Vector3 target=run.terrainLeg==0?run.terrainEnd+forward*.6f:run.terrainStart-forward*.6f;
   var feet=Feet(body);var horizontal=Vector3.ProjectOnPlane(target-feet,Vector3.up);
   float dt=Mathf.Clamp(Time.deltaTime,.001f,.05f);run.terrainWalkSeconds+=dt;
   if(run.terrainWalkSeconds>60)throw new InvalidOperationException("Actual river crossing exceeded60 movement seconds.");
   body.Move(Vector3.ClampMagnitude(horizontal,2.6f*dt)+Vector3.down*(1.5f*dt));
   feet=Feet(body);float progress=Vector3.Dot(Vector3.ProjectOnPlane(feet-run.terrainStart,Vector3.up),forward);
   float lateral=Mathf.Abs(Vector3.Dot(feet-run.terrainStart,Vector3.Cross(Vector3.up,forward)));
   float deckY=Mathf.Lerp(run.terrainStart.y,run.terrainEnd.y,Mathf.Clamp01(progress/span));
   float error=Mathf.Abs(feet.y-deckY);bool interior=progress>.8f&&progress<span-.8f;
   bool support=terrainBridge.Support.Raycast(new Ray(feet+Vector3.up*.16f,Vector3.down),out _,.5f);
   if(interior&&support)run.terrainSupportedSamples++;
   if(interior&&(feet.y<deckY-.2f||lateral>1f))throw new InvalidOperationException("Real player left the bridge deck: height/lateral mismatch at "+feet);
   run.terrainMaxLateral=Mathf.Max(run.terrainMaxLateral,lateral);run.terrainMaxDeckError=Mathf.Max(run.terrainMaxDeckError,error);run.terrainSteps++;
   run.terrainWalk.Add(new TerrainStep295{frame=Time.frameCount,leg=run.terrainLeg,feet=feet,progress=progress,lateral=lateral,deckError=error,bridgeSupport=support});
   if(run.terrainSteps%30==0)Persist();
   if(Vector3.ProjectOnPlane(target-feet,Vector3.up).magnitude>.12f)return;
   Check(session.TrySafeFeet(feet,out _),"Physical river crossing leg "+run.terrainLeg+" ends on permanent dry bank support.");
   Check(run.terrainSupportedSamples>=3,"Actual CharacterController crossed generated bridge collision during leg "+run.terrainLeg+".");
   Record(run.terrainLeg==0?"real-bank-far-arrival":"real-bank-return-arrival",session,"site="+run.terrainAcceptedSite+"; movement samples="+run.terrainSteps+"; bridge ray supports="+run.terrainSupportedSamples);
   if(run.terrainLeg==0)
   {CaptureTerrainBridge(session,true);run.terrainLeg=1;run.terrainSupportedSamples=0;run.terrainWalkSeconds=0;run.status="CROSSING_REAL_RIVER_RETURN";Persist();return;}
   // Setup movement to the existing shelter happens only after both collision-resolved crossings have completed.
   PlaceBody(session,run.approach);Advance(203,"REAL_RIVER_REST_CLEANUP");
  }
  static void CaptureTerrainBridge(WorldMacroPlaytestSession session,bool farBank)
  {
   var source=session.Walker.ViewCamera;if(source==null)throw new InvalidOperationException("Live player view camera is missing for terrain evidence.");
   string file=Path.Combine(Folder,TerrainPrefix+(farBank?"-return.png":"-placement.png"));
   var feet=Feet(session.Walker.Body);float eyeHeight=Mathf.Clamp(source.transform.position.y-feet.y,1.4f,1.8f);
   var eye=feet+Vector3.up*eyeHeight;var target=Vector3.Lerp(run.terrainStart,run.terrainEnd,farBank?.25f:.75f)+Vector3.up*.12f;
   var previous=RenderTexture.active;bool previousAsync=ShaderUtil.allowAsyncCompilation;
   var arts=UnityEngine.Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None).Where(a=>a.gameObject.scene==session.gameObject.scene).ToArray();
   var observers=arts.Select(a=>a.Observer).ToArray();
   var dressings=UnityEngine.Object.FindObjectsByType<Oheangbu.App.World.Dressing.WorldMacroDressingRenderer>(FindObjectsSortMode.None).Where(a=>a.gameObject.scene==session.gameObject.scene).ToArray();
   var dressingObservers=dressings.Select(a=>a.Observer).ToArray();
   GameObject cameraObject=null;Camera camera=null;RenderTexture targetTexture=null;Texture2D pixels=null;
   try
   {
    cameraObject=new GameObject("Mum295_RuntimeProofCamera"){hideFlags=HideFlags.HideAndDontSave};
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,session.gameObject.scene);
    camera=cameraObject.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
    camera.ResetWorldToCameraMatrix();camera.ResetCullingMatrix();camera.ResetProjectionMatrix();camera.aspect=16f/9;
    var sourceSky=source.GetComponent<Skybox>();if(sourceSky!=null){var sky=cameraObject.AddComponent<Skybox>();sky.material=sourceSky.material;sky.enabled=sourceSky.enabled;}
    var sourceData=source.GetComponents<Component>().FirstOrDefault(c=>c!=null&&c.GetType().FullName=="UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
    if(sourceData!=null)
    {
     var copy=cameraObject.AddComponent(sourceData.GetType());EditorUtility.CopySerialized(sourceData,copy);
     // The copied camera owns a fresh overlay list; it must not render the user's UI stack into world evidence.
     if(sourceData.GetType().GetProperty("cameraStack")?.GetValue(copy) is System.Collections.IList stack)stack.Clear();
    }
    foreach(var art in arts)art.Observer=camera;foreach(var dressing in dressings)dressing.Observer=camera;
    ShaderUtil.allowAsyncCompilation=false;
    targetTexture=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);targetTexture.Create();camera.targetTexture=targetTexture;
    camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye,Vector3.up));camera.Render();camera.Render();
    pixels=new Texture2D(1920,1080,TextureFormat.RGB24,false);RenderTexture.active=targetTexture;
    pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();File.WriteAllBytes(file,pixels.EncodeToPNG());
    var terrain=session.gameObject.scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Reworld292_Terrain");
    var terrainRenderers=terrain!=null?terrain.GetComponentsInChildren<Renderer>().Where(r=>r.enabled&&!r.forceRenderingOff&&(camera.cullingMask&(1<<r.gameObject.layer))!=0).ToArray():Array.Empty<Renderer>();
    var planes=GeometryUtility.CalculateFrustumPlanes(camera);
    File.WriteAllText(file+".json",JsonUtility.ToJson(new TerrainCapture295{capturedUtc=DateTime.UtcNow.ToString("o"),scene=session.gameObject.scene.path,
     site=run.terrainAcceptedSite,sourceHeightSha256=run.terrainSites.sourceHeightSha256,view=farBank?"Far dry bank, looking back before return crossing":"Near dry bank, completed actual placement",
     eye=eye,target=target,playerFeet=feet,bridgeStart=run.terrainStart,bridgeEnd=run.terrainEnd,fieldOfView=camera.fieldOfView,span=terrainBridge.Plan.Span,normalMumUnlocked=session.HasMumBridge,
     nearClip=camera.nearClipPlane,farClip=camera.farClipPlane,cullingMask=camera.cullingMask,terrainRenderers=terrainRenderers.Length,terrainInFrustum=terrainRenderers.Count(r=>GeometryUtility.TestPlanesAABB(planes,r.bounds)),artInstances=arts.Sum(a=>a.VisibleInstances)},true));
    Record(farBank?"real-bank-return-capture":"real-bank-placement-capture",session,file);
   }
   finally
   {
    for(int i=0;i<arts.Length;i++)if(arts[i]!=null)arts[i].Observer=observers[i];
    for(int i=0;i<dressings.Length;i++)if(dressings[i]!=null)dressings[i].Observer=dressingObservers[i];
    ShaderUtil.allowAsyncCompilation=previousAsync;RenderTexture.active=previous;if(camera!=null)camera.targetTexture=null;
    if(cameraObject!=null)UnityEngine.Object.DestroyImmediate(cameraObject);
    if(targetTexture!=null){targetTexture.Release();UnityEngine.Object.DestroyImmediate(targetTexture);}
    if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);
   }
  }
 }
}
