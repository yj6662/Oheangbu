using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // Scene pass operations. Every function runs twice with the same code: Dry (decide and report, change nothing) and for real.
 // A row is written only when the scene differs from the wanted state, so a second apply writes nothing.
 // Rows on objects that already existed (actors, the logger body, session lists, dead lift sites) carry their before-values;
 // rows on objects this pass created are "create" (the revert destroys them) or "note" (no revert needed).
 public static partial class Content308
 {
  // ---------- rests: 성황당 ×3 + 사냥꾼 주막 (visual only: the content asset is never written here) ----------

  static void RestOps308(Pass308 k)
  {
   float maxDy=Num308(Req308(k.Data,"ground"),"max_dy_vs_content_m");
   foreach(var r in Arr308(k.Data,"rests"))
   {
    string id=Str308(r,"id");k.Group=SceneGroup308(r,id);
    if(!Flag308(r,"enabled")){k.Say("rest "+id+": off in data");continue;}
    // T7: a row that names a relayout group is placed only while that group is on (and only with the relayout data present):
    // a switched-off group must not get its altar and candles back from the next scene-apply
    string held=SceneRowHeld308(r);if(held!=null){k.Say("rest "+id+": "+held);continue;}
    var point=Point308(k.Content,id);var cp=Checkpoint308(k.Content,id);
    if(point==null||cp==null){k.Warn("rest "+id+": point / checkpoint not in this content (run content-apply first); skipped");continue;}
    // T5: the altar of an escort rest stands only where its rests_escort row puts the content point (never on the old bench place)
    string away=EscortRestAway308(id,point,cp);if(away!=null){k.Warn("rest "+id+": "+away+"; the altar is not placed");continue;}
    if(k.Blocked.Contains(id)){k.Say("BLOCKED rest "+id+": inside a keep-out; not placed");continue;}
    if(!Ground308(point.Position.x,point.Position.z,out var g,out float slope)){k.Warn("rest "+id+": no ground under "+V308(point.Position)+"; skipped");continue;}
    float dy=Mathf.Abs(g.y-point.Position.y);
    if(dy>maxDy)k.Warn("rest "+id+": content Y "+F308(point.Position.y)+" vs ground "+F308(g.y)+" (|dy| "+F308(dy)+" > "+F308(maxDy)+"): the content is stale here - content-revert + content-apply this scene, then run the scene pass again");
    // front = toward the respawn feet (the feet stand on the road side of the shrine)
    float yaw=Mathf.Repeat(Harness303.YawTo(g,cp.Feet),360f);
    k.Say("rest "+id+": ground "+V308(g)+" slope "+F308(slope,"F1")+"°, |dy| vs content "+F308(dy)+", front yaw "+F308(yaw,"F1"));
    var holder=Holder308(k,"Rests",id,g,yaw);string holderPath=SceneRootDefault308+"/Rests/"+id;
    Transform pointPart=null;
    foreach(var part in Arr308(r,"parts"))
    {
     string name=Str308(part,"name");var off=Req308(part,"offset");
     var at=g+Quaternion.Euler(0,yaw,0)*new Vector3(At308(off,0),0,At308(off,2));
     if(Flag308(part,"ground")){if(Ground308(at.x,at.z,out var pg,out _))at.y=pg.y;else k.Warn("rest "+id+"/"+name+": no ground under the part; the rest point's height is used");}
     at.y+=At308(off,1);
     var placed=Place308(k,holder,holderPath,name,part,at,yaw+Num308(part,"yaw"),"rest "+id+"/"+name);
     if(Flag308(part,"point"))pointPart=placed;
     FootprintVeg308(k,"rest "+id+"/"+name,at);
    }
    if(holder==null){k.Say("rest "+id+": content point marker, light rule and InteractionVisuals are set with the create");continue;}
    RestRules308(k,r,id,holder,pointPart);
   }
  }

  // marker (WorldMacroContentPoint), the ART-INK light rule (candles / one lantern only, no shadows) and the focus renderers
  static void RestRules308(Pass308 k,JToken r,string id,Transform holder,Transform pointPart)
  {
   string path=SceneRootDefault308+"/Rests/"+id;
   if(pointPart!=null)
   {
    var cp=pointPart.GetComponent<WorldMacroContentPoint>();
    if(cp==null||cp.Id!=id||cp.Visual!=pointPart)
    {
     k.Do(new SceneOp308{op="note",path=path,id=id,detail="content point marker "+id+" on "+path+"/"+pointPart.name});
     if(!k.Dry){if(cp==null)cp=pointPart.gameObject.AddComponent<WorldMacroContentPoint>();cp.Id=id;cp.Visual=pointPart;}
    }
   }
   int max=Mathf.RoundToInt(Num308(r,"lights_max"));float range=Num308(r,"light_range_m");
   var lights=holder.GetComponentsInChildren<Light>(true);int on=0;var fixes=new List<string>();
   foreach(var l in lights)
   {
    bool keep=on<max;if(l.enabled&&keep)on++;
    if(l.enabled&&!keep){fixes.Add(l.name+" off");if(!k.Dry)l.enabled=false;}
    if(l.shadows!=LightShadows.None){fixes.Add(l.name+" shadows off");if(!k.Dry)l.shadows=LightShadows.None;}
    if(l.range>range+1e-3f){fixes.Add(l.name+" range "+F308(l.range,"F1")+" -> "+F308(range,"F1"));if(!k.Dry)l.range=range;}
   }
   if(fixes.Count>0)k.Do(new SceneOp308{op="note",path=path,id=id,detail="light rule "+id+" (max "+max+", no shadows, range ≤ "+F308(range,"F1")+"): "+string.Join(", ",fixes)});
   else k.Say("ok lights "+id+": "+lights.Count(l=>l.enabled)+" on of "+lights.Length);
   var renderers=holder.GetComponentsInChildren<MeshRenderer>(true).Where(m=>m.GetComponent<MeshFilter>()!=null).Cast<Renderer>().ToArray();
   VisualOp308(k,id,renderers);
  }

  static void VisualOp308(Pass308 k,string id,Renderer[] renderers)
  {
   var all=k.Session.InteractionVisuals??Array.Empty<WorldMacroPlaytestSession.InteractionVisual>();
   var have=Array.Find(all,v=>v!=null&&v.Id==id);
   if(have!=null)
   {
    bool same=(have.Renderers??Array.Empty<Renderer>()).Where(x=>x!=null).ToHashSet().SetEquals(renderers);
    k.Say((same?"ok":"kept (another writer's)")+" InteractionVisuals["+id+"]: "+(have.Renderers?.Length??0)+" renderer(s)");return;
   }
   k.Do(new SceneOp308{op="visual-add",id=id,detail="InteractionVisuals["+id+"] += "+renderers.Length+" renderer(s)"});
   if(!k.Dry)k.Session.InteractionVisuals=all.Concat(new[]{new WorldMacroPlaytestSession.InteractionVisual{Id=id,Renderers=renderers}}).ToArray();
  }

  // sheet vegetation is drawn from data (CompactRebuildArtRenderer): a building placed on it is not cleared by this pass
  static void FootprintVeg308(Pass308 k,string what,Vector3 at)
  {
   float r=Num308(Req308(k.Data,"ground"),"footprint_veg_warn_m");int n=0;
   foreach(var art in Saved308<CompactRebuildArtRenderer>(k.Scene))
   {
    var rows=art.Sheet!=null?art.Sheet.FixedPlacements:null;if(rows==null)continue;
    foreach(var p in rows)if(Harness303.Flat(p.Position,at)<=r)n++;
   }
   if(n>0)k.Warn(what+": "+n+" baked vegetation placement(s) within "+F308(r,"F0")+" m of "+V308(at)+" (sheet rows are not cleared by this pass)");
  }

  // ---------- npc bodies: the 사냥꾼 주막 keeper (clone) and the logger (move) ----------

  static void NpcOps308(Pass308 k)
  {
   k.Group="";var d=k.Data["npcs"];if(d==null)return;float dyTol=Num308(Req308(k.Data,"ground"),"actor_max_dy_m");
   foreach(var n in Arr308(d,"clones"))
   {
    string id=Str308(n,"id");k.Group=SceneGroup308(n,id);if(!Flag308(n,"enabled")){k.Say("npc "+id+": off in data");continue;}
    var point=Point308(k.Content,id);if(point==null){k.Warn("npc "+id+": point not in this content; skipped");continue;}
    if(k.Blocked.Contains(id)){k.Say("BLOCKED npc "+id+": inside a keep-out; not placed");continue;}
    if(!Ground308(point.Position.x,point.Position.z,out var g,out _)){k.Warn("npc "+id+": no ground under "+V308(point.Position)+"; skipped");continue;}
    string face=Opt308(n,"face_checkpoint");var cp=face!=null?Checkpoint308(k.Content,face):null;
    float yaw=cp!=null?Mathf.Repeat(Harness303.YawTo(g,cp.Feet),360f):0f;
    // the body carries a solid capsule: on (or right beside) a respawn spot the player would rest / respawn inside it.
    // The body always follows its content point (the interaction is tested against the point), so the cure is to move the point.
    float feetClear=Num308(d,"feet_clear_m");
    foreach(var spot in k.Content.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>())
    {
     if(spot==null||!spot.IsConfigured)continue;float gap=Harness303.Flat(spot.Feet,g);
     if(gap<feetClear)k.Warn("npc "+id+": its content point is "+F308(gap)+" m from the respawn feet of "+spot.Id+" (< "+F308(feetClear)+" m, npcs.feet_clear_m): the body is placed on the point as the content says, "+
      "but scene-check will FAIL 'respawn feet clear' until the content point is moved (Content308.Content point spec, then content-apply and scene-apply again)");
    }
    var group=Group308(k,"Npcs");
    var body=Place308(k,group,SceneRootDefault308+"/Npcs",id,n,g,yaw,"npc "+id);
    if(body==null){k.Say("npc "+id+": marker, job point id and InteractionVisuals are set with the create");continue;}
    var marker=body.GetComponent<WorldMacroContentPoint>();var jobs=body.GetComponentsInChildren<NpcJobActor>(true);
    if(marker==null||marker.Id!=id||jobs.Any(j=>j.PointId!=id))
    {
     k.Do(new SceneOp308{op="note",id=id,detail="npc "+id+": content point marker + "+jobs.Length+" NpcJobActor.PointId"});
     if(!k.Dry)
     {
      if(marker==null)marker=body.gameObject.AddComponent<WorldMacroContentPoint>();marker.Id=id;marker.Visual=body;
      foreach(var j in jobs){j.PointId=id;j.Seed=Animator.StringToHash(id);EditorUtility.SetDirty(j);}
     }
    }
    // focus renderers: the same relative ones the source's entry names, else every body renderer
    Renderer[] renderers=null;string sourcePoint=Opt308(n,"source_point");
    if(Source308(k,n,"npc "+id,out var src,out _)&&src!=null&&sourcePoint!=null)
    {
     var entry=Array.Find(k.Session.InteractionVisuals??Array.Empty<WorldMacroPlaytestSession.InteractionVisual>(),v=>v!=null&&v.Id==sourcePoint);
     if(entry?.Renderers!=null)renderers=entry.Renderers.Where(x=>x!=null&&x.transform.IsChildOf(src)).Select(x=>body.Find(Rel308(src,x.transform))).Where(t=>t!=null).Select(t=>t.GetComponent<Renderer>()).Where(x=>x!=null).ToArray();
    }
    if(renderers==null||renderers.Length==0)renderers=body.GetComponentsInChildren<Renderer>(true);
    VisualOp308(k,id,renderers);
   }
   foreach(var n in Arr308(d,"moves"))
   {
    string id=Str308(n,"id");k.Group=SceneGroup308(n,id);if(!Flag308(n,"enabled")){k.Say("npc "+id+": off in data");continue;}
    var point=Point308(k.Content,id);if(point==null){k.Warn("npc "+id+": point not in this content; skipped");continue;}
    var bodies=Saved308<WorldMacroContentPoint>(k.Scene).Where(c=>c.Id==id&&!Own308(c.transform)&&c.gameObject.activeInHierarchy).ToArray();
    if(bodies.Length!=1){k.Warn("npc "+id+": "+bodies.Length+" active body marker(s) in this scene; skipped");continue;}
    var t=bodies[0].transform;
    if(PostLedger308.UnderProtectedTree(t)){k.Warn("npc "+id+": under a protected tree ("+Harness303.PathOf(t)+"); left alone");continue;}
    if(k.Blocked.Contains(id)){k.Say("BLOCKED npc "+id+": inside a keep-out; not moved");continue;}
    if(!Ground308(point.Position.x,point.Position.z,out var g,out _)){k.Warn("npc "+id+": no ground under "+V308(point.Position)+"; skipped");continue;}
    // T4: a row yaw (the scene row's, else the relayout point_moves row's) turns the body too; no yaw = position only, as before
    float? yaw=SceneYaw308(n,id);bool yawOk=yaw==null||Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y,yaw.Value))<=k.YawTol;
    if(Harness303.Flat(t.position,g)<=k.PoseTol&&Mathf.Abs(t.position.y-g.y)<=dyTol&&yawOk){k.Say("ok npc "+id+" at "+V308(t.position)+(yaw!=null?" yaw "+F308(t.eulerAngles.y,"F1"):""));continue;}
    k.Do(new SceneOp308{op="trs",field="point",id=id,path=Harness303.PathOf(t),key=BuildingAudit308.KeyOf(t),pos=t.localPosition,euler=t.localEulerAngles,scale=t.localScale,hasAfter=true,posAfter=g,
     detail="move npc "+id+" "+V308(t.position)+" -> "+V308(g)+" ("+F308(Vector3.Distance(t.position,g),"F1")+" m)"+(yaw!=null&&!yawOk?", yaw "+F308(t.eulerAngles.y,"F1")+" -> "+F308(yaw.Value,"F1"):"")});
    if(!k.Dry){t.position=g;if(yaw!=null){var e0=t.eulerAngles;t.rotation=Quaternion.Euler(e0.x,yaw.Value,e0.z);}}
   }
  }
  static string Rel308(Transform root,Transform t)
  {
   var parts=new List<string>();for(var p=t;p!=null&&p!=root;p=p.parent)parts.Insert(0,p.name);
   return string.Join("/",parts);
  }

  // ---------- encounters: dokkaebi / agwi moves, forest beast clones ----------

  static void ActorOps308(Pass308 k)
  {
   k.Group="";var d=k.Data["actors"];if(d==null)return;var g=Req308(k.Data,"ground");float dyTol=Num308(g,"actor_max_dy_m"),navR=Num308(g,"nav_radius_m");
   var actors=Saved308<PrologueEncounter>(k.Scene).ToArray();
   foreach(var m in Arr308(d,"moves"))
   {
    string id=Str308(m,"id");k.Group=SceneGroup308(m,id);if(!Flag308(m,"enabled")){k.Say("encounter "+id+": off in data");continue;}
    string held=SceneRowHeld308(m);if(held!=null){k.Say("encounter "+id+": "+held);continue;}
    var e=Encounter308(k.Content,id);var a=actors.Where(x=>x.Id==id).ToArray();
    if(e==null||a.Length==0){k.Say("encounter "+id+": "+(e==null?"not in this content":"no actor in this scene")+"; skipped");continue;}
    if(a.Length>1){k.Warn("encounter "+id+": "+a.Length+" actors with this id; skipped");continue;}
    if(PostLedger308.UnderProtectedTree(a[0].transform)){k.Warn("encounter "+id+": under a protected tree; left alone");continue;}
    if(k.Blocked.Contains(id)){k.Say("BLOCKED encounter "+id+": inside a keep-out; not moved");continue;}
    ActorPose308(k,a[0],e,dyTol,navR,false,SceneYaw308(m,id));
   }
   foreach(var c in Arr308(d,"clones"))
   {
    string id=Str308(c,"id");k.Group=SceneGroup308(c,id);if(!Flag308(c,"enabled")){k.Say("encounter "+id+": off in data");continue;}
    var e=Encounter308(k.Content,id);if(e==null){k.Warn("encounter "+id+": not in this content (run content-apply first); skipped");continue;}
    if(k.Blocked.Contains(id)){k.Say("BLOCKED encounter "+id+": inside a keep-out; not placed");continue;}
    var have=actors.Where(x=>x.Id==id).ToArray();
    if(have.Length>1){k.Warn("encounter "+id+": "+have.Length+" actors with this id; skipped");continue;}
    PrologueEncounter actor=have.Length==1?have[0]:null;
    if(actor==null)
    {
     string templateId=Str308(c,"template");var template=actors.FirstOrDefault(x=>x.Id==templateId);
     if(template==null){k.Warn("encounter "+id+": template actor "+templateId+" not in this scene; skipped");continue;}
     var agent0=template.GetComponent<NavMeshAgent>();var at=e.Feet+Vector3.up*(agent0!=null?agent0.baseOffset:0f);
     string name=id.Replace('/','_');string path=SceneRootDefault308+"/Actors/"+name;var group=Group308(k,"Actors");
     k.Do(new SceneOp308{op="create",path=path,id=id,detail="clone "+templateId+" -> "+path+" ("+id+") at "+V308(at)});
     k.Do(new SceneOp308{op="actor-add",id=id,detail="session.Actors += "+id});
     k.Do(new SceneOp308{op="wired-add",id=id,detail="CombatLoopWiring._enemies += "+id});
     if(k.Dry)continue;
     var go=Object.Instantiate(template.gameObject);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,k.Scene);
     go.transform.SetParent(group,true);go.name=name;go.transform.localScale=template.transform.lossyScale;
     go.transform.SetPositionAndRotation(at,template.transform.rotation);
     actor=go.GetComponent<PrologueEncounter>();actor.Id=id;actor.Ranged=e.Ranged;actor.DetectionRange=e.Detection;actor.Leash=e.Leash;actor.Speed=e.Speed;
     actor.PatrolPoints=(e.Patrol!=null&&e.Patrol.Length>0?e.Patrol:new[]{e.Feet}).ToArray();
     k.Session.Actors=(k.Session.Actors??Array.Empty<PrologueEncounter>()).Concat(new[]{actor}).ToArray();
     SetWired308(k.Session,go.GetComponent<EnemyVitals>(),true);
     NavNote308(k,actor,e,navR);
     continue;
    }
    // already in the scene: its pose follows the content; the two session links are repaired when one is missing
    ActorPose308(k,actor,e,dyTol,navR,Own308(actor.transform));
    if(!(k.Session.Actors??Array.Empty<PrologueEncounter>()).Contains(actor))
    {
     k.Do(new SceneOp308{op="actor-add",id=id,detail="session.Actors += "+id});
     if(!k.Dry)k.Session.Actors=(k.Session.Actors??Array.Empty<PrologueEncounter>()).Concat(new[]{actor}).ToArray();
    }
    var vit=actor.GetComponent<EnemyVitals>();
    if(vit!=null&&!Wired308(k.Session,vit))
    {
     k.Do(new SceneOp308{op="wired-add",id=id,detail="CombatLoopWiring._enemies += "+id});
     if(!k.Dry)SetWired308(k.Session,vit,true);
    }
   }
  }

  // an actor's root pivot stands baseOffset above its content feet (CompactFolklore298.Candidate: position = feet + up * baseOffset)
  // yaw (T4): the row's facing; null = the pose is position only, as before
  static void ActorPose308(Pass308 k,PrologueEncounter a,WorldMacroPlaytestSO.Encounter e,float dyTol,float navR,bool own,float? yaw=null)
  {
   var t=a.transform;var agent=a.GetComponent<NavMeshAgent>();var want=e.Feet+Vector3.up*(agent!=null?agent.baseOffset:0f);
   bool yawOk=yaw==null||Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y,yaw.Value))<=k.YawTol;
   if(Harness303.Flat(t.position,want)>k.PoseTol||Mathf.Abs(t.position.y-want.y)>dyTol||!yawOk)
   {
    // an own (created) actor's move is a "note": the whole-ledger revert destroys the clone; field reseat lets a group revert put it back
    k.Do(new SceneOp308{op=own?"note":"trs",field=own?"reseat":"encounter",id=a.Id,path=Harness303.PathOf(t),key=BuildingAudit308.KeyOf(t),pos=t.localPosition,euler=t.localEulerAngles,scale=t.localScale,hasAfter=true,posAfter=want,
     detail="move "+a.Id+" "+V308(t.position)+" -> "+V308(want)+" ("+F308(Vector3.Distance(t.position,want),"F1")+" m; feet "+V308(e.Feet)+")"+(yaw!=null&&!yawOk?", yaw "+F308(t.eulerAngles.y,"F1")+" -> "+F308(yaw.Value,"F1"):"")});
    if(!k.Dry){t.position=want;if(yaw!=null){var e0=t.eulerAngles;t.rotation=Quaternion.Euler(e0.x,yaw.Value,e0.z);}}
   }
   else k.Say("ok "+a.Id+" at "+V308(t.position)+(yaw!=null?" yaw "+F308(t.eulerAngles.y,"F1"):""));
   var patrol=e.Patrol??Array.Empty<Vector3>();
   if(patrol.Length>0&&!(a.PatrolPoints??Array.Empty<Vector3>()).SequenceEqual(patrol))
   {
    k.Do(new SceneOp308{op=own?"note":"patrol",id=a.Id,points=(a.PatrolPoints??Array.Empty<Vector3>()).ToArray(),detail="patrol "+a.Id+": "+(a.PatrolPoints?.Length??0)+" point(s) -> the content's "+patrol.Length});
    if(!k.Dry){a.PatrolPoints=patrol.ToArray();EditorUtility.SetDirty(a);}
   }
   NavNote308(k,a,e,navR);
  }
  static void NavNote308(Pass308 k,PrologueEncounter a,WorldMacroPlaytestSO.Encounter e,float navR)
  {
   var agent=a.GetComponent<NavMeshAgent>();int mask=agent!=null?agent.areaMask:NavMesh.AllAreas;
   if(!NavMesh.SamplePosition(e.Feet,out _,navR,mask))k.Warn(a.Id+": no NavMesh within "+F308(navR,"F1")+" m of the feet "+V308(e.Feet)+" (the session throws at start without it; scene-check after the shared bake decides)");
  }

  // ---------- dead 국 lift sites (D308-9c Q4) ----------

  static void LiftRetireOps308(Pass308 k)
  {
   k.Group="";var d=k.Data["lift_retire"];if(d==null||!Flag308(d,"enabled")){k.Say("lift_retire: off in data");return;}
   string mode=Str308(d,"mode");if(mode!="disable"&&mode!="remove-component")throw new Refuse308("lift_retire.mode must be 'disable' or 'remove-component' (is '"+mode+"')");
   bool requireInactive=Flag308(d,"require_inactive");var sites=Saved308<GukLiftSite>(k.Scene).ToArray();
   foreach(var token in Arr308(d,"ids"))
   {
    string id=token.Value<string>();var hit=sites.Where(s=>s.Id==id).ToArray();
    if(hit.Length==0){k.Say("ok lift "+id+": no GukLiftSite component in this scene");continue;}
    foreach(var site in hit)
    {
     string path=Harness303.PathOf(site.transform);
     if(PostLedger308.UnderProtectedTree(site.transform)){k.Warn("lift "+id+": under a protected tree ("+path+"); left alone");continue;}
     if(requireInactive&&site.isActiveAndEnabled){k.Warn("lift "+id+" at "+path+" is ACTIVE: not retired (require_inactive) - a live gate is a design decision, not cleanup");continue;}
     if(FindAll308(k.Scene,path).Count!=1){k.Warn("lift "+id+": path "+path+" is not unique; left alone (the revert could not find it again)");continue;}
     string before=(site.enabled?"1":"0")+"|"+(site.Lower!=null?Harness303.PathOf(site.Lower):"")+"|"+(site.Upper!=null?Harness303.PathOf(site.Upper):"");
     if(mode=="disable")
     {
      if(!site.enabled){k.Say("ok lift "+id+": component already disabled");continue;}
      k.Do(new SceneOp308{op="lift-retire",field="disable",path=path,id=id,before=before,f0=site.CastRadius,f1=site.RiseSeconds,detail="disable GukLiftSite "+id+" at "+path});
      if(!k.Dry){site.enabled=false;EditorUtility.SetDirty(site);}
     }
     else
     {
      k.Do(new SceneOp308{op="lift-retire",field="remove-component",path=path,id=id,before=before,f0=site.CastRadius,f1=site.RiseSeconds,
       pos=site.Lower!=null?site.Lower.position:Vector3.zero,euler=site.Upper!=null?site.Upper.position:Vector3.zero,
       detail="remove GukLiftSite "+id+" from "+path+" (object, Lower / Upper and geometry stay; height "+F308(site.Height)+" m)"});
      if(!k.Dry)Object.DestroyImmediate(site);
     }
    }
   }
  }

  // ---------- 금표 암릉 국 바위턱: the site (nodes + root bed) on a column the real colliders offer ----------

  sealed class LiftCand308{public Vector3 Lower,Upper;public float Height,Score,RunDist,EnemyClear;public string Reject="";}

  // walkable usable hits at (x,z), highest first (the scene pass's own root and previews never count)
  static List<Vector3> Tops308(float x,float z)
  {
   var list=new List<Vector3>();
   foreach(var h in Physics.RaycastAll(new Vector3(x,2000f,z),Vector3.down,4000f,~0,QueryTriggerInteraction.Ignore))if(Usable308(h))list.Add(h.point);
   list.Sort((a,b)=>b.y.CompareTo(a.y));return list;
  }
  static List<Vector3> Homes308(Pass308 k)
  {
   var homes=(k.Content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Where(e=>e!=null).Select(e=>e.Feet).ToList();
   homes.AddRange(Saved308<PrologueEncounter>(k.Scene).Select(a=>a.transform.position));
   return homes;
  }
  static void Judge308(Pass308 k,JToken d,LiftCand308 c,List<Vector3> homes)
  {
   float enemy=Num308(d,"enemy_clear_m"),shell=Num308(d,"shell_clear_m"),hMin=At308(d["height_m"],0),hMax=At308(d["height_m"],1);
   c.EnemyClear=homes.Count==0?float.PositiveInfinity:homes.Min(h=>Vector3.Distance(h,c.Lower));
   if(c.EnemyClear<enemy)c.Reject="an encounter home is "+F308(c.EnemyClear,"F0")+" m away (< "+F308(enemy,"F0")+": a chasing enemy cancels 국)";
   var shells=new HashSet<string>(Arr308(d,"shell_roots").Select(t=>t.Value<string>()));
   foreach(var col in Physics.OverlapSphere(c.Lower,shell,~0,QueryTriggerInteraction.Ignore))
    if(!Preview308(col.transform)&&shells.Contains(col.transform.root.name)){c.Reject="a shell collider ("+Harness303.PathOf(col.transform)+") is within "+F308(shell,"F0")+" m";break;}
   c.RunDist=PathDistance308(k.Content,c.Lower);
   c.Score=-Mathf.Abs(c.Height-(hMin+hMax)*.5f)-c.RunDist;
  }
  static LiftCand308 SurveyLift308(Pass308 k,JToken d,List<string> rows)
  {
   float hMin=At308(d["height_m"],0),hMax=At308(d["height_m"],1),maxXz=Num308(d,"max_xz_m"),maxSlope=Num308(d,"lower_max_slope_deg");
   var homes=Homes308(k);
   if(d["lower_xz"] is JArray lo&&d["upper_xz"] is JArray up)
   {
    if(!Ground308(At308(lo,0),At308(lo,1),out var foot,out float slope)){rows.Add("pinned column: no ground under lower_xz");return null;}
    var tops=Tops308(At308(up,0),At308(up,1));if(tops.Count==0){rows.Add("pinned column: nothing to stand on under upper_xz");return null;}
    var pinned=new LiftCand308{Lower=foot,Upper=tops[0],Height=tops[0].y-foot.y};Judge308(k,d,pinned,homes);
    rows.Add("pinned column: Lower "+V308(foot)+" (slope "+F308(slope,"F1")+"°) Upper "+V308(tops[0])+" height "+F308(pinned.Height)+" m, XZ "+F308(Harness303.Flat(foot,tops[0]))+" m"+(pinned.Reject.Length>0?" REJECTED: "+pinned.Reject:""));
    return pinned.Reject.Length>0?null:pinned;
   }
   var win=Req308(d,"window");float x0=At308(win["x"],0),x1=At308(win["x"],1),z0=At308(win["z"],0),z1=At308(win["z"],1),step=Num308(d,"survey_step_m");
   // sampling of the search (data): `survey_rings` rings of `survey_bearings` probes out to survey_reach_frac * max_xz_m around each foot
   int rings=Mathf.RoundToInt(Num308(d,"survey_rings")),bearings=Mathf.RoundToInt(Num308(d,"survey_bearings"));float reach=Num308(d,"survey_reach_frac"),shaftEps=Num308(d,"shaft_clear_eps_m");
   if(step<=0f||rings<1||bearings<1||reach<=0f||reach>1f)throw new Refuse308("scene data: lift.survey_step_m > 0, survey_rings ≥ 1, survey_bearings ≥ 1 and 0 < survey_reach_frac ≤ 1 are required");
   int cells=0,standable=0,columns=0,rejected=0;float bestRise=float.NegativeInfinity;Vector3 bestRiseAt=default;LiftCand308 best=null;string lastReject="";
   for(float x=x0;x<=x1+1e-3f;x+=step)
    for(float z=z0;z<=z1+1e-3f;z+=step)
    {
     cells++;
     if(!Ground308(x,z,out var foot,out float slope)||slope>maxSlope)continue;
     var here=Tops308(x,z);if(here.Count>0&&here[0].y>foot.y+shaftEps)continue;   // something walkable above the foot: the shaft is not open
     standable++;
     for(int ring=1;ring<=rings;ring++)
      for(int b=0;b<bearings;b++)
      {
       float r=maxXz*reach*ring/rings,a=b*(360f/bearings)*Mathf.Deg2Rad;var tops=Tops308(x+Mathf.Cos(a)*r,z+Mathf.Sin(a)*r);if(tops.Count==0)continue;
       float rise=tops[0].y-foot.y;if(rise>bestRise){bestRise=rise;bestRiseAt=foot;}
       if(rise<hMin||rise>hMax)continue;
       columns++;var c=new LiftCand308{Lower=foot,Upper=tops[0],Height=rise};Judge308(k,d,c,homes);
       if(c.Reject.Length>0){rejected++;lastReject=c.Reject;continue;}
       if(best==null||c.Score>best.Score)best=c;
      }
    }
   rows.Add("survey window x "+F308(x0,"F0")+".."+F308(x1,"F0")+" z "+F308(z0,"F0")+".."+F308(z1,"F0")+" step "+F308(step,"F2")+" m: "+cells+" cells, "+standable+" standable (slope ≤ "+F308(maxSlope,"F0")+"°), "+
    columns+" column(s) with a top "+F308(hMin,"F0")+"-"+F308(hMax,"F0")+" m up within "+F308(maxXz,"F1")+" m, "+rejected+" rejected"+(rejected>0?" (last: "+lastReject+")":""));
   rows.Add("largest rise within "+F308(maxXz,"F1")+" m of a standable foot: "+(float.IsNegativeInfinity(bestRise)?"-":F308(bestRise)+" m at "+V308(bestRiseAt)));
   if(best!=null)rows.Add("best column: Lower "+V308(best.Lower)+" Upper "+V308(best.Upper)+" height "+F308(best.Height)+" m, XZ "+F308(Harness303.Flat(best.Lower,best.Upper))+" m, "+F308(best.RunDist,"F1")+" m off the run, nearest encounter home "+F308(best.EnemyClear,"F0")+" m");
   return best;
  }

  static void LiftSiteOps308(Pass308 k)
  {
   var d=k.Data["lift"];if(d==null||!Flag308(d,"enabled")){k.Say("lift: off in data");return;}
   string id=Str308(d,"id"),name="Guk_"+id,path=SceneRootDefault308+"/Lifts/"+name;
   float lift=Num308(d,"node_lift_m"),tol=Num308(d,"support_tol_m");
   var existing=Saved308<GukLiftSite>(k.Scene).Where(s=>s.Id==id).ToArray();
   if(existing.Length>0)
   {
    foreach(var s in existing)
    {
     bool lowerOk=s.Lower!=null&&Ground308(s.Lower.position.x,s.Lower.position.z,out var lg,out _)&&Mathf.Abs(s.Lower.position.y-lift-lg.y)<=tol;
     bool upperOk=s.Upper!=null&&Tops308(s.Upper.position.x,s.Upper.position.z).Any(p=>Mathf.Abs(s.Upper.position.y-lift-p.y)<=tol);
     if(s.Valid&&lowerOk&&upperOk)k.Say("ok lift "+id+" at "+Harness303.PathOf(s.transform)+": height "+F308(s.Height)+" m, Valid");
     else k.Warn("lift "+id+" at "+Harness303.PathOf(s.transform)+": Valid "+s.Valid+", lower support "+lowerOk+", upper support "+upperOk+" - the ground changed under it: scene-revert, then scene-apply (a site is never moved silently)");
    }
    return;
   }
   if(k.Blocked.Contains(id)){k.Say("BLOCKED lift "+id+": inside a keep-out; not authored");return;}
   var rows=new List<string>();var c=SurveyLift308(k,d,rows);foreach(var r in rows)k.Say(r);
   float vMin=At308(d["valid_m"],0),vMax=At308(d["valid_m"],1),maxXz=Num308(d,"max_xz_m");
   if(c==null){k.Hold("lift "+id+": no usable column, so nothing is authored. The 바위 띠 has to exist first (cliff kit); then run scene-apply again (or pin lower_xz / upper_xz)");return;}
   if(c.Height<vMin||c.Height>vMax||Harness303.Flat(c.Lower,c.Upper)>maxXz){k.Hold("lift "+id+": column outside GukLiftSite.Valid (height "+F308(c.Height)+", XZ "+F308(Harness303.Flat(c.Lower,c.Upper))+"); nothing authored");return;}
   var group=Group308(k,"Lifts");
   k.Do(new SceneOp308{op="create",path=path,id=id,detail="create "+path+": Lower "+V308(c.Lower)+" Upper "+V308(c.Upper)+" height "+F308(c.Height)+" m"});
   if(k.Dry)return;
   var root=new GameObject(name);root.transform.SetParent(group,false);
   var site=root.AddComponent<GukLiftSite>();site.Id=id;site.CastRadius=Num308(d,"cast_radius");site.RiseSeconds=Num308(d,"rise_seconds");
   site.Lower=new GameObject("Lower").transform;site.Lower.SetParent(root.transform,false);site.Lower.position=c.Lower+Vector3.up*lift;
   site.Upper=new GameObject("Upper").transform;site.Upper.SetParent(root.transform,false);site.Upper.position=c.Upper+Vector3.up*lift;
   // the root bed is the "국 here" sign (never on an outer-edge cliff, D305); its top is the Lower node
   var bed=Req308(d,"root_bed");var size=new Vector3(At308(bed["size"],0),At308(bed["size"],1),At308(bed["size"],2));
   var box=GameObject.CreatePrimitive(PrimitiveType.Cube);box.name="PermanentRootBed";box.transform.SetParent(root.transform,false);
   box.transform.position=c.Lower+Vector3.up*(lift-size.y*.5f);box.transform.localScale=size;
   var from=FindAll308(k.Scene,Str308(bed,"material_from")).FirstOrDefault();var mr=from!=null?from.GetComponent<Renderer>():null;
   if(mr!=null&&mr.sharedMaterial!=null)box.GetComponent<Renderer>().sharedMaterial=mr.sharedMaterial;
   else k.Warn("lift "+id+": root bed material source "+Str308(bed,"material_from")+" not found; the default material stays (fix before a look check)");
   if(!site.Valid)throw new Refuse308("lift "+id+" is not Valid after authoring (height "+F308(site.Height)+", cast radius "+F308(site.CastRadius)+", rise "+F308(site.RiseSeconds)+" s)");
  }

  // ---------- props (data rows; proposals stay off) ----------

  static void PropOps308(Pass308 k)
  {
   k.Group="";float clear=Num308(Req308(k.Data,"ground"),"corridor_clear_m");
   foreach(var p in Arr308(k.Data,"props"))
   {
    string id=Str308(p,"id");k.Group=SceneGroup308(p,Opt308(p,"anchor")??id);
    // a relayout row without the relayout data file is neither placed nor taken out (no file = the #308 behaviour)
    if(SceneRowNoData308(p)){k.Say("prop "+id+": row of relayout group "+Opt308(p,"group")+" and no relayout data file; left as it is");continue;}
    if(!Flag308(p,"enabled")||SceneGroupOff308(p)){PropOff308(k,p,id);continue;}
    string anchor=Str308(p,"anchor");var point=Point308(k.Content,anchor);
    if(point==null){k.Warn("prop "+id+": anchor point "+anchor+" not in this content; skipped");continue;}
    if(k.Blocked.Contains(anchor)){k.Say("BLOCKED prop "+id+": its anchor "+anchor+" is inside a keep-out; not placed");continue;}
    var off=Req308(p,"offset");var at=point.Position+new Vector3(At308(off,0),0,At308(off,1));
    if(Flag308(p,"ground")){if(!Ground308(at.x,at.z,out var g,out _)){k.Warn("prop "+id+": no ground under "+V308(at)+"; skipped");continue;}at.y=g.y;}
    at.y+=Num308(p,"y_offset");
    float d=PathDistance308(k.Content,at);
    if(Flag308(p,"solid")&&d<clear){k.Warn("prop "+id+": "+F308(d,"F1")+" m from a route corridor centreline (< "+F308(clear,"F1")+" m); not placed");continue;}
    // canon (발광 상한): a row marked no_light is placed only from a source that carries no Light and no emissive material
    if(Flag308(p,"no_light"))
    {
     if(!Source308(k,p,"prop "+id,out var glowSrc,out var glowAsset))continue;   // the missing source is already reported
     string glow=Glow308(glowSrc!=null?glowSrc.gameObject:glowAsset);
     if(glow!=null){k.Warn("prop "+id+": its source carries "+glow+" and the row is no_light (only shrine candles may be new sustained lights); not placed");continue;}
    }
    var group=Group308(k,"Props");
    Place308(k,group,SceneRootDefault308+"/Props",id,p,at,Num308(p,"yaw"),"prop "+id);
   }
   k.Group="";
  }

  // ---------- escort_start node seat (proposal; off in data) ----------

  static void EscortSeatOps308(Pass308 k)
  {
   k.Group="";var d=k.Data["escort_seat"];if(d==null)return;
   var stops=FindAll308(k.Scene,Str308(d,"stop"));
   if(stops.Count!=1){k.Say("escort stop "+Str308(d,"stop")+" found "+stops.Count+" time(s); skipped");return;}
   var root=stops[0];var point=Point308(k.Content,Str308(d,"point"));float probe=Num308(d,"probe_m"),tol=Num308(d,"tol_m");bool on=Flag308(d,"enabled");
   var interaction=root.Find("Interaction");
   float miss=point!=null&&interaction!=null?Vector3.Distance(interaction.position,point.Position):float.PositiveInfinity;
   bool moved=miss<=k.PoseTol;
   k.Say("escort_start root "+V308(root.position)+"; Interaction to the content point "+(float.IsPositiveInfinity(miss)?"n/a":F308(miss,"F2")+" m")+(moved?"":" -> Escort303Fix move-start has not run on this scene (the seat needs the moved stop)"));
   var listed=new HashSet<string>(Arr308(d,"nodes").Select(t=>t.Value<string>()));
   foreach(Transform n in root)
   {
    if(!Ground308(n.position.x,n.position.z,out var g,out _)){k.Say("  node "+n.name+" "+V308(n.position)+": no ground under it");continue;}
    float gap=n.position.y-g.y;bool isListed=listed.Contains(n.name);
    k.Say("  node "+n.name+" "+V308(n.position)+": ground "+F308(g.y)+", gap "+F308(gap)+" m"+(!isListed?" (never seated: must equal the content)":Mathf.Abs(gap)<=tol?" (within "+F308(tol)+")":" (seat candidate)"));
    if(!on||!moved||!isListed||Mathf.Abs(gap)<=tol)continue;
    if(Mathf.Abs(gap)>probe){k.Warn("  node "+n.name+": gap beyond the "+F308(probe,"F1")+" m probe; not seated");continue;}
    if(PostLedger308.UnderProtectedTree(n)){k.Warn("  node "+n.name+": under a protected tree; left alone");continue;}
    k.Do(new SceneOp308{op="trs",path=Harness303.PathOf(n),key=BuildingAudit308.KeyOf(n),pos=n.localPosition,euler=n.localEulerAngles,scale=n.localScale,detail="seat escort node "+n.name+" y "+F308(n.position.y)+" -> "+F308(g.y)});
    if(!k.Dry)n.position=new Vector3(n.position.x,g.y,n.position.z);
   }
   if(!on)k.Say("escort seat is off in data (proposal): nothing is written");
  }
 }
}
