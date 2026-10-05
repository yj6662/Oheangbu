using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // 가도의 토 잡몹 3체 - the scene half (relayout308_ext.json XE2-XE4 scene, XE5 profile wiring, XE6 crystal shape) [TEST].
 // One plan, one save per scene, its own ledger (Art/Playtest308/Pacing/ledger_earth308_scene_<scene>.json + a byte backup of the
 // scene). XE5 and XE6 are written in the same save as the clone: an earth profile on a body that still carries the neutral crystal
 // would fail OrganSurface308 check ("crystal shape = actor element", SPEC-TELEGRAPH-ORGAN-308 D308-4e), so the two never part.
 // Objects are found by encounter id / BuildingAudit308.KeyOf, never by a bare name path. Nothing here adds a Light, an emissive
 // material, a collider, a HUD element or a text.
 //   earth-enc   PrologueEncounter fields of the clone (Ranged, DetectionRange, Leash, Speed, PreferredDistance, PatrolPoints)
 //   earth-ref   an object reference field (EnemyController._attackProfile, EnemyVitals._profile); before = the asset path it held
 //   earth-mesh  MeshFilter.sharedMesh of the body's Organ308_maseok_shard (same pivot frame and scale: only the shape changes)
 public static partial class Content308
 {
  // ---------- the plan ----------

  static void EarthPlan308(Pass308 k,EarthSet308 d)
  {
   k.Group=d.Group;
   float dyTol=Num308(Req308(k.Data,"ground"),"actor_max_dy_m");
   // no "vitals" in the data = every actor keeps the EnemyVitals profile it has (wood family)
   EnemyVitalsProfileSO vitals=null;
   if(d.Vitals.Length>0)
   {
    if(Harness303.IsProtected(d.Vitals))throw new Refuse308("protected path "+d.Vitals);
    vitals=AssetDatabase.LoadAssetAtPath<EnemyVitalsProfileSO>(d.Vitals);
    if(vitals==null)throw new Refuse308("missing vitals profile "+d.Vitals);
   }
   var doc=OrganSurface308.LoadShardDoc(out string docNote);
   string sceneNote=Opt308(d.Data["scene_notes"],PostLedger308.Short(k.Scene.path));if(sceneNote!=null)k.Say("note "+sceneNote);
   var actors=Saved308<PrologueEncounter>(k.Scene).ToArray();
   foreach(var r in d.RowsOf(k.Scene.path))
   {
    k.Group=d.Group;k.Say("-- "+r.Id+" ("+r.Op+")");
    var e=Encounter308(k.Content,r.Id);
    if(e==null){k.Hold(r.Id+": not in this content - run "+family308.Entry+" content-apply:"+PostLedger308.Short(k.Scene.path)+" first");continue;}
    var attack=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(d.AttackPath(r));
    if(attack==null){k.Hold(r.Id+": attack profile "+d.AttackPath(r)+" missing - run "+family308.Entry+" profiles first");continue;}
    var have=actors.Where(x=>x.Id==r.Id).ToArray();
    if(have.Length>1){k.Warn(r.Id+": "+have.Length+" actors with this id; skipped");continue;}
    var template=actors.FirstOrDefault(x=>x.Id==r.Template);
    var actor=have.Length==1?have[0]:null;bool created=false;
    if(actor!=null&&((!r.Existing&&!Own308(actor.transform))||PostLedger308.UnderProtectedTree(actor.transform))){k.Warn(r.Id+": an actor with this id stands outside "+SceneRootDefault308+" ("+Harness303.PathOf(actor.transform)+"); left alone");continue;}
    if(r.Existing)
    {
     // D308-18 답 5: the row names an actor that already stands in the scene. Nothing is created, moved or re-linked: only the profile
     // reference, the crystal shape (and, when the data asks, the organ sphere) change. Its ranges, leash, respawn and place stay.
     if(actor==null){k.Warn(r.Id+": no actor with this id in this scene; row skipped here");continue;}
     if(!(k.Session.Actors??Array.Empty<PrologueEncounter>()).Contains(actor))k.Warn(r.Id+": the actor is not in session.Actors (left as it is)");
     k.Say("existing "+r.Id+" = "+Harness303.PathOf(actor.transform)+" at "+V308(actor.transform.position)+" (not moved)");
    }
    else if(actor==null)
    {
     if(template==null){k.Say(r.Id+": template actor "+r.Template+" is not in this scene; row skipped here (note)");continue;}
     if(k.Blocked.Contains(r.Id)){k.Say("BLOCKED "+r.Id+": inside a keep-out; not placed");continue;}
     var agent0=template.GetComponent<NavMeshAgent>();var at=e.Feet+Vector3.up*(agent0!=null?agent0.baseOffset:0f);
     string name=r.Id.Replace('/','_');string path=SceneRootDefault308+"/Actors/"+name;var holder=Group308(k,"Actors");
     k.Do(new SceneOp308{op="create",path=path,id=r.Id,detail="clone "+r.Template+" -> "+path+" ("+r.Id+") at "+V308(at)+" yaw "+F308(r.Yaw,"F1")+" scale ×"+F308(r.Scale)});
     k.Do(new SceneOp308{op="actor-add",id=r.Id,detail="session.Actors += "+r.Id});
     k.Do(new SceneOp308{op="wired-add",id=r.Id,detail="CombatLoopWiring._enemies += "+r.Id});
     created=true;
     if(!k.Dry)
     {
      var go=Object.Instantiate(template.gameObject);SceneManager.MoveGameObjectToScene(go,k.Scene);
      go.transform.SetParent(holder,true);go.name=name;go.transform.localScale=template.transform.lossyScale*r.Scale;
      var te=template.transform.eulerAngles;go.transform.SetPositionAndRotation(at,Quaternion.Euler(te.x,r.Yaw,te.z));
      actor=go.GetComponent<PrologueEncounter>();actor.Id=r.Id;
      k.Session.Actors=(k.Session.Actors??Array.Empty<PrologueEncounter>()).Concat(new[]{actor}).ToArray();
      SetWired308(k.Session,go.GetComponent<EnemyVitals>(),true);
     }
    }
    else
    {
     // already placed: pose, facing and scale follow the content / the row; the two session links are repaired when one is missing
     var t=actor.transform;var agent=actor.GetComponent<NavMeshAgent>();var want=e.Feet+Vector3.up*(agent!=null?agent.baseOffset:0f);
     var wantScale=template!=null?template.transform.lossyScale*r.Scale:t.localScale;
     bool poseOk=Harness303.Flat(t.position,want)<=k.PoseTol&&Mathf.Abs(t.position.y-want.y)<=dyTol;
     bool yawOk=Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y,r.Yaw))<=k.YawTol;bool scaleOk=(t.localScale-wantScale).magnitude<=d.ScaleTol;
     if(!poseOk||!yawOk||!scaleOk)
     {
      k.Do(new SceneOp308{op="note",field="reseat",id=r.Id,path=Harness303.PathOf(t),key=BuildingAudit308.KeyOf(t),pos=t.localPosition,euler=t.localEulerAngles,scale=t.localScale,hasAfter=true,posAfter=want,
       detail="re-seat "+r.Id+" "+V308(t.position)+" -> "+V308(want)+(yawOk?"":", yaw "+F308(t.eulerAngles.y,"F1")+" -> "+F308(r.Yaw,"F1"))+(scaleOk?"":", scale "+V308(t.localScale)+" -> "+V308(wantScale))});
      if(!k.Dry){var e0=t.eulerAngles;t.SetPositionAndRotation(want,Quaternion.Euler(e0.x,r.Yaw,e0.z));t.localScale=wantScale;}
     }
     else k.Say("ok "+r.Id+" at "+V308(t.position)+" yaw "+F308(t.eulerAngles.y,"F1"));
     if(!(k.Session.Actors??Array.Empty<PrologueEncounter>()).Contains(actor))
     {
      k.Do(new SceneOp308{op="actor-add",id=r.Id,detail="session.Actors += "+r.Id});
      if(!k.Dry)k.Session.Actors=(k.Session.Actors??Array.Empty<PrologueEncounter>()).Concat(new[]{actor}).ToArray();
     }
     var vit=actor.GetComponent<EnemyVitals>();
     if(vit!=null&&!Wired308(k.Session,vit))
     {
      k.Do(new SceneOp308{op="wired-add",id=r.Id,detail="CombatLoopWiring._enemies += "+r.Id});
      if(!k.Dry)SetWired308(k.Session,vit,true);
     }
    }
    // values are read from the clone, or from the template while the clone does not exist yet (a dry pass): what the clone starts with
    var subject=actor!=null?actor:template;bool live=actor!=null&&!k.Dry;
    if(!r.Existing||!float.IsNaN(r.PreferredDistance))EarthEncounterFields308(k,r,e,subject,live);
    EarthRef308(k,r.Id,subject.GetComponent<EnemyController>(),"EnemyController","_attackProfile",attack,live);
    if(vitals!=null)EarthRef308(k,r.Id,subject.GetComponent<EnemyVitals>(),"EnemyVitals","_profile",vitals,live);
    EarthOrgan308(k,d,r,subject,live,doc,docNote);
    if(!created&&!r.Existing){var agent=subject.GetComponent<NavMeshAgent>();int mask=agent!=null?agent.areaMask:NavMesh.AllAreas;
     if(!NavMesh.SamplePosition(e.Feet,out _,d.NavRadius,mask))k.Warn(r.Id+": no NavMesh within "+F308(d.NavRadius,"F0")+" m of the feet "+V308(e.Feet)+" (the session throws at start without it; scene-check after the shared bake decides)");}
   }
   // KE6 switch (review 2, finding 3): off in code, on in this scene only by the data. One ledger op, undone by scene-revert.
   k.Group=d.Group;
   if(family308!=EarthFamily308&&!File.Exists(LedgerFile308(EarthFamily308.Name+"308_scene_"+Key308(k.Scene.path))))
    k.Warn("no earth scene ledger for this scene: run Content308.Earth with scene-apply:"+PostLedger308.Short(k.Scene.path)+" BEFORE this pass (the earth clones copy mine_beast/1; cloned after this pass they would start with the wood profile and the grown sphere)");
   if(d.Data["runtime"]==null)k.Say("session.SeatedEnemiesPassThrough308 = "+k.Session.SeatedEnemiesPassThrough308+" (not named by this data; left as it is)");
   else if(EarthSeatFlag308(d)&&!k.Session.SeatedEnemiesPassThrough308)
   {
    k.Do(new SceneOp308{op="earth-flag",field="SeatedEnemiesPassThrough308",before="0",detail="session.SeatedEnemiesPassThrough308 false -> true (KE6: a switched-off ordinary enemy is not a solid capsule while the player rides; bosses and triggers untouched)"});
    if(!k.Dry)k.Session.SeatedEnemiesPassThrough308=true;
   }
   else k.Say("ok session.SeatedEnemiesPassThrough308 = "+k.Session.SeatedEnemiesPassThrough308+" (data runtime.seated_pass_through "+EarthSeatFlag308(d)+")");
   k.Group="";
  }

  static string R308(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
  static bool EarthSeatFlag308(EarthSet308 d)=>(bool?)d.Data["runtime"]?["seated_pass_through"]==true;

  static void EarthEncounterFields308(Pass308 k,EarthRow308 r,WorldMacroPlaytestSO.Encounter e,PrologueEncounter subject,bool live)
  {
   var patrol=e.Patrol!=null&&e.Patrol.Length>0?e.Patrol:new[]{e.Feet};
   var now=subject.PatrolPoints??Array.Empty<Vector3>();
   bool same=subject.Ranged==e.Ranged&&Mathf.Approximately(subject.DetectionRange,e.Detection)&&Mathf.Approximately(subject.Leash,e.Leash)&&Mathf.Approximately(subject.Speed,e.Speed)
    &&Mathf.Approximately(subject.PreferredDistance,r.PreferredDistance)&&now.SequenceEqual(patrol);
   if(same){k.Say("ok "+r.Id+" encounter fields (Detection "+F308(subject.DetectionRange,"F0")+" · Leash "+F308(subject.Leash,"F0")+" · Speed "+F308(subject.Speed,"F1")+" · PreferredDistance "+F308(subject.PreferredDistance,"F1")+")");return;}
   k.Do(new SceneOp308{op="earth-enc",id=r.Id,field="PrologueEncounter",
    before=(subject.Ranged?"1":"0")+"|"+R308(subject.DetectionRange)+"|"+R308(subject.Leash)+"|"+R308(subject.Speed)+"|"+R308(subject.PreferredDistance),points=now.ToArray(),
    detail=r.Id+" PrologueEncounter: Ranged "+(subject.Ranged?1:0)+" -> "+(e.Ranged?1:0)+", Detection "+F308(subject.DetectionRange,"F0")+" -> "+F308(e.Detection,"F0")+", Leash "+F308(subject.Leash,"F0")+" -> "+F308(e.Leash,"F0")+
     ", Speed "+F308(subject.Speed,"F1")+" -> "+F308(e.Speed,"F1")+", PreferredDistance "+F308(subject.PreferredDistance,"F1")+" -> "+F308(r.PreferredDistance,"F1")+", patrol "+now.Length+" -> "+patrol.Length+" point(s)"});
   if(!live)return;
   subject.Ranged=e.Ranged;subject.DetectionRange=e.Detection;subject.Leash=e.Leash;subject.Speed=e.Speed;subject.PreferredDistance=r.PreferredDistance;subject.PatrolPoints=patrol.ToArray();
   EditorUtility.SetDirty(subject);
  }

  static void EarthRef308(Pass308 k,string id,Component owner,string ownerName,string field,Object want,bool live)
  {
   if(owner==null){k.Warn(id+": no "+ownerName+" on the actor; "+field+" not wired");return;}
   var so=new SerializedObject(owner);var p=so.FindProperty(field);
   if(p==null||p.propertyType!=SerializedPropertyType.ObjectReference){k.Warn(id+": "+ownerName+"."+field+" is not an object reference field; not wired");return;}
   if(p.objectReferenceValue==want){k.Say("ok "+id+" "+ownerName+"."+field+" = "+want.name);return;}
   string before=p.objectReferenceValue!=null?AssetDatabase.GetAssetPath(p.objectReferenceValue):"";
   k.Do(new SceneOp308{op="earth-ref",id=id,field=ownerName+"."+field,before=before,path=AssetDatabase.GetAssetPath(want),detail=id+" "+ownerName+"."+field+": "+(before.Length==0?"(none)":before)+" -> "+AssetDatabase.GetAssetPath(want)});
   if(!live)return;
   p.objectReferenceValue=want;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(owner);
  }

  static Mesh EarthShape308(string element,out string fbx)
  {
   fbx=OrganSurface308.ShardDir+"/"+OrganSurface308.MeshNameFor(element)+".fbx";
   if(Harness303.IsProtected(fbx)||AssetDatabase.LoadMainAssetAtPath(fbx)==null)return null;
   return AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>().OrderByDescending(m=>m.vertexCount).FirstOrDefault();
  }
  static Transform EarthCrystal308(PrologueEncounter actor,out int count)
  {
   string name=OrganSurface308.ShardPartName;
   var parts=actor.GetComponentsInChildren<Transform>(true).Where(t=>t.name==name).ToArray();
   count=parts.Length;return parts.Length==1?parts[0]:null;
  }

  // XE6: the crystal of a reused body takes the shape of THIS actor's element. The six shapes share one frame (pivot = where the
  // axis meets the body, +Y = the axis, the same root and scale k), so the part's transform, material, organ entry, contamination and
  // readability target stay exactly as the species prefab authored them; only MeshFilter.sharedMesh changes.
  static void EarthOrgan308(Pass308 k,EarthSet308 d,EarthRow308 r,PrologueEncounter subject,bool live,OrganSurface308.ShardDoc doc,string docNote)
  {
   var part=EarthCrystal308(subject,out int count);
   if(count==0&&r.Existing){k.Hold(r.Id+": no "+OrganSurface308.ShardPartName+" under the body - an elemental profile with no crystal to read in the telegraph window is not saved");return;}
   if(count==0){k.Say(r.Id+": no "+OrganSurface308.ShardPartName+" under the body in this scene (the reused body carries no crystal here); XE6 skipped for this row (note)");return;}
   if(r.Existing&&subject.GetComponentInChildren<EnemyElementTelegraph>(true)==null){k.Hold(r.Id+": no EnemyElementTelegraph on the actor - the element would never be shown in the telegraph window; nothing is written for this scene");return;}
   // review 2, finding 2: a body that HAS a crystal which cannot take the earth shape must not be saved with an earth profile
   // ("crystal shape = actor element"). Each such case is PENDING: scene-apply then writes nothing for this scene.
   if(part==null){k.Hold(r.Id+": "+count+" objects named "+OrganSurface308.ShardPartName+" under the body - which one is the crystal is not decidable; nothing is written for this scene");return;}
   var mf=part.GetComponent<MeshFilter>();
   if(mf==null||mf.sharedMesh==null||!OrganSurface308.IsShardMeshName(mf.sharedMesh.name)){k.Hold(r.Id+": the crystal part does not carry an Organ308 shard shape (mesh "+(mf!=null&&mf.sharedMesh!=null?mf.sharedMesh.name:"-")+"); nothing is written for this scene");return;}
   if(Array.IndexOf(OrganSurface308.ShardElements,d.OrganElement)<0)throw new Refuse308(family308.Name+" data: organ.element '"+d.OrganElement+"' is not one of "+string.Join(", ",OrganSurface308.ShardElements));
   var row=doc!=null&&doc.placements!=null?doc.placements.FirstOrDefault(p=>p!=null&&p.owner==d.OrganBody):null;
   if(row==null){k.Hold(r.Id+": no placement row for "+d.OrganBody+" ("+docNote+"); nothing is written for this scene");return;}
   if(part.parent==null||part.parent.name!=row.bone){k.Hold(r.Id+": the crystal sits under '"+(part.parent!=null?part.parent.name:"-")+"' but the "+d.OrganBody+" row names bone "+row.bone+" - this body is not "+d.OrganBody+"; nothing is written for this scene (take this scene's alias out of encounters[].scenes in "+EarthFile308+", or give the row a template whose body is "+d.OrganBody+")");return;}
   var want=EarthShape308(d.OrganElement,out string fbx);
   if(want==null){k.Hold(r.Id+": crystal shape "+fbx+" not found");return;}
   var ls=part.lossyScale;float kNow=(Mathf.Abs(ls.x)+Mathf.Abs(ls.y)+Mathf.Abs(ls.z))/3f;
   if(Mathf.Max(Mathf.Abs(ls.x),Mathf.Max(Mathf.Abs(ls.y),Mathf.Abs(ls.z)))-Mathf.Min(Mathf.Abs(ls.x),Mathf.Min(Mathf.Abs(ls.y),Mathf.Abs(ls.z)))>d.OrganKTol*Mathf.Max(1f,kNow))
    k.Warn(r.Id+": the crystal's world scale is not uniform "+V308(ls)+" (the shape would be sheared)");
   // Part organ: Radius >= the part's bounding-sphere radius, else the shader sphere cuts the part (SPEC-TELEGRAPH-ORGAN-308)
   var organ=subject.GetComponentsInChildren<EnemyOrganSet>(true).SelectMany(s=>s.Organs??Array.Empty<EnemyOrganSet.Organ>()).FirstOrDefault(o=>o!=null&&o.TargetRenderer!=null&&o.TargetRenderer.transform==part);
   float need=.5f*want.bounds.size.magnitude*d.OrganK;
   if(organ==null&&r.Existing){k.Hold(r.Id+": no EnemyOrganSet organ targets the crystal (it would never light in the telegraph window); nothing is written for this scene");return;}
   if(organ==null)k.Warn(r.Id+": no EnemyOrganSet organ targets the crystal (it would never light in the telegraph window)");
   else if(d.GrowRadius)EarthGrow308(k,r,subject,part,want,organ,live);
   else if(organ.Radius<need)k.Warn(r.Id+": organ Radius "+F308(organ.Radius,"F3")+" m < the "+d.OrganElement+" shape's bounding sphere "+F308(need,"F3")+" m at k "+F308(d.OrganK,"F3")+" (the overlay sphere would cut the part)");
   if(mf.sharedMesh==want){k.Say("ok "+r.Id+" crystal shape "+want.name+" (world scale "+F308(kNow,"F3")+"; species k "+F308(d.OrganK,"F3")+")");return;}
   k.Do(new SceneOp308{op="earth-mesh",id=r.Id,field="MeshFilter.sharedMesh",path=Harness303.PathOf(part),key=BuildingAudit308.KeyOf(part),before=AssetDatabase.GetAssetPath(mf.sharedMesh)+"|"+mf.sharedMesh.name,
    detail=r.Id+" crystal shape "+mf.sharedMesh.name+" -> "+want.name+" ("+d.OrganElement+"; same pivot frame, world scale "+F308(kNow,"F3")+"; organ Radius "+(organ!=null?F308(organ.Radius,"F3"):"-")+" ≥ "+F308(need,"F3")+")"});
   if(!live)return;
   mf.sharedMesh=want;EditorUtility.SetDirty(mf);
   if(PrefabUtility.IsPartOfPrefabInstance(mf))PrefabUtility.RecordPrefabInstancePropertyModifications(mf);
  }

  // farthest vertex of the shape (at the part's place) from the organ centre, in metres
  static float EarthReach308(Transform part,Mesh shape,EnemyOrganSet set,EnemyOrganSet.Organ organ)
  {
   var anchor=organ.Anchor!=null?organ.Anchor:set.transform;Vector3 c=anchor.TransformPoint(organ.LocalOffset);float reach=0f;
   var verts=shape.vertices;
   if(verts!=null&&verts.Length>0){foreach(var v in verts)reach=Mathf.Max(reach,(part.TransformPoint(v)-c).magnitude);return reach;}
   var b=shape.bounds;
   for(int i=0;i<8;i++){var corner=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1f:1f,(i&2)==0?-1f:1f,(i&4)==0?-1f:1f));reach=Mathf.Max(reach,(part.TransformPoint(corner)-c).magnitude);}
   return reach;
  }
  static float EarthSphere308(float reach)=>Mathf.Ceil(reach*1.05f*1000f)/1000f;
  static EnemyOrganSet EarthOrganSet308(PrologueEncounter subject,EnemyOrganSet.Organ organ,out int index)
  {
   index=-1;
   foreach(var s in subject.GetComponentsInChildren<EnemyOrganSet>(true)){int i=Array.IndexOf(s.Organs??Array.Empty<EnemyOrganSet.Organ>(),organ);if(i>=0){index=i;return s;}}
   return null;
  }

  // organ.grow_radius (wood): a Part organ's sphere covers the whole part (the rule of OrganSurface308.ComputeSurface: Radius >= reach
  // x 1.05). A shape that reaches further than the species shape grows the sphere; it never shrinks, and nothing else of the organ entry
  // (anchor, offset, surface point, contamination) changes. Written through SerializedObject: a prefab instance keeps it as an override.
  static void EarthGrow308(Pass308 k,EarthRow308 r,PrologueEncounter subject,Transform part,Mesh shape,EnemyOrganSet.Organ organ,bool live)
  {
   var set=EarthOrganSet308(subject,organ,out int index);if(set==null){k.Hold(r.Id+": the organ entry is not in an EnemyOrganSet of this actor; sphere not judged, nothing is written for this scene");return;}
   float reach=EarthReach308(part,shape,set,organ),want=EarthSphere308(reach);
   if(organ.Radius>=want-.0005f){k.Say("ok "+r.Id+" organ sphere "+F308(organ.Radius,"F3")+" m covers the "+shape.name+" shape (reach "+F308(reach,"F3")+" m)");return;}
   k.Do(new SceneOp308{op="earth-radius",id=r.Id,field="EnemyOrganSet.Organs["+index+"].Radius",before=R308(organ.Radius),
    detail=r.Id+" organ sphere "+F308(organ.Radius,"F3")+" -> "+F308(want,"F3")+" m (the "+shape.name+" shape reaches "+F308(reach,"F3")+" m from the organ centre; x 1.05)"});
   if(!live)return;
   var so=new SerializedObject(set);var p=so.FindProperty("Organs").GetArrayElementAtIndex(index).FindPropertyRelative("Radius");
   p.floatValue=want;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(set);
  }

  // ---------- dry / apply ----------

  static string EarthScene308(string scenePath,bool dry)
  {
   var d=EarthData308();if(!dry)d.NeedOn();
   // pass 1 never changes anything: it decides whether there is something to write at all
   var plan=Begin308(scenePath,true,out string dataSha);Keepout308(plan,true);EarthPlan308(plan,d);
   string key=EarthSceneKey308(plan.Scene.path);
   var head=new StringBuilder((dry?family308.Name+" scene-dry ":family308.Name+" scene-apply ")+plan.Scene.path+"\n  "+family308.Name+" data "+EarthFile308+" sha "+Short308(d.Sha)+", group "+d.Group+"; scene data sha "+Short308(dataSha)+"\n");
   int real=plan.Ops.Count;
   if(dry||real==0||plan.Pending>0)
   {
    foreach(var l in plan.Lines)head.AppendLine("  "+l);
    if(!dry&&plan.Pending>0)head.AppendLine("  REFUSED "+plan.Pending+" PENDING row(s): the clone, its profiles and its crystal are written together or not at all - nothing written");
    else head.AppendLine(real==0?"  변경 없음 (nothing to write; scene not saved)":"  "+real+" op(s) would be written; WARN "+plan.Warns+", PENDING "+plan.Pending+", keep-out blocked "+plan.Blocked.Count);
    if(plan.Scene.isDirty)head.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
    return SceneOut308(key+(dry?"-dry":"-last")+".txt",head);
   }
   // a scene save while a look preview is up would write the renderers that preview switched off
   PreviewGuard308(plan.Data,plan.Scene.path,true);
   string abs=Harness303.Abs(plan.Scene.path);string backupDir=BackupDir308();Directory.CreateDirectory(backupDir);
   string copy=Path.Combine(backupDir,Path.GetFileName(plan.Scene.path));File.Copy(abs,copy,true);
   var write=new SceneWrite308{utc=DateTime.UtcNow.ToString("O"),backup=copy,shaBefore=Harness303.Sha(abs),dataSha=d.Sha,planSha=dataSha};
   var ledger=ReadSceneLedger308(key)??new SceneLedger308{kind=family308.Name+"-scene",scene=plan.Scene.path,created=DateTime.UtcNow.ToString("O")};
   var pass=Begin308(scenePath,false,out _);
   try{Keepout308(pass,true);EarthPlan308(pass,d);}
   catch(Exception e)
   {
    // a half-applied plan must not stay in memory: reload the scene from disk (nothing was saved)
    EditorSceneManager.OpenScene(pass.Scene.path,OpenSceneMode.Single);
    return "FAILED "+family308.Name+" scene-apply "+pass.Scene.path+" (scene reloaded from disk, nothing saved; backup "+copy+")\n"+(e is Refuse308?"REFUSED "+e.Message:e.ToString());
   }
   // journal first (review 2, finding 6): the ops stand in the ledger BEFORE the scene is saved (empty shaAfter = save not confirmed);
   // a crash between the two leaves a ledger that Earth scene-revert can still undo.
   write.ops=pass.Ops;ledger.writes.Add(write);
   try{Directory.CreateDirectory(Out308);File.WriteAllText(LedgerFile308(key),JsonUtility.ToJson(ledger,true));}
   catch(Exception e)
   {
    EditorSceneManager.OpenScene(pass.Scene.path,OpenSceneMode.Single);
    return "FAILED the ledger "+LedgerFile308(key)+" could not be written ("+e.Message+"); scene reloaded from disk, nothing saved";
   }
   EditorUtility.SetDirty(pass.Session);EditorSceneManager.MarkSceneDirty(pass.Scene);
   if(!EditorSceneManager.SaveScene(pass.Scene))
   {
    EditorSceneManager.OpenScene(pass.Scene.path,OpenSceneMode.Single);
    ledger.writes.Remove(write);
    try{if(ledger.writes.Count==0)File.Delete(LedgerFile308(key));else File.WriteAllText(LedgerFile308(key),JsonUtility.ToJson(ledger,true));}catch(Exception){}
    return "FAILED SaveScene returned false for "+pass.Scene.path+" (scene reloaded from disk, nothing saved, the journal row was taken out again; backup "+copy+")";
   }
   write.shaAfter=Harness303.Sha(abs);
   try{File.WriteAllText(LedgerFile308(key),JsonUtility.ToJson(ledger,true));}
   catch(Exception e){head.AppendLine("  WARN the scene was saved and its ops are in the ledger, but shaAfter could not be recorded ("+e.Message+")");}
   foreach(var l in pass.Lines)head.AppendLine("  "+l);
   head.AppendLine("  saved: "+pass.Ops.Count+" op(s); WARN "+pass.Warns+", keep-out blocked "+pass.Blocked.Count);
   head.AppendLine("  scene sha "+Short308(write.shaBefore)+" -> "+Short308(write.shaAfter)+"; backup "+copy);
   head.AppendLine("  ledger "+LedgerFile308(key));
   head.AppendLine("  next: the shared NavMesh bake (if the feet have no NavMesh) -> "+family308.Entry+" scene-check:"+PostLedger308.Short(pass.Scene.path)+" -> OrganSurface308 check");
   return SceneOut308(key+"-last.txt",head);
  }

  // ---------- revert ----------

  // the actor of a ledger row: the one under the tool's own root, else (an existing actor of the wood family) the only one with this id
  static PrologueEncounter EarthActor308(Scene scene,string id)
  {
   var all=Saved308<PrologueEncounter>(scene).Where(x=>x.Id==id&&!PostLedger308.UnderProtectedTree(x.transform)).ToArray();
   return all.FirstOrDefault(x=>Own308(x.transform))??(all.Length==1?all[0]:null);
  }

  // null = undone; else why not
  static string EarthUndo308(Scene scene,WorldMacroPlaytestSession session,SceneOp308 op)
  {
   switch(op.op)
   {
    case "earth-enc":
    {
     var a=EarthActor308(scene,op.id);if(a==null)return "actor not found";
     var parts=(op.before??"").Split('|');if(parts.Length<5)return "ledger row has no before-values";
     float P(int i)=>float.Parse(parts[i],CultureInfo.InvariantCulture);
     a.Ranged=parts[0]=="1";a.DetectionRange=P(1);a.Leash=P(2);a.Speed=P(3);a.PreferredDistance=P(4);a.PatrolPoints=(op.points??Array.Empty<Vector3>()).ToArray();
     EditorUtility.SetDirty(a);return null;
    }
    case "earth-ref":
    {
     var a=EarthActor308(scene,op.id);if(a==null)return "actor not found";
     int dot=op.field.IndexOf('.');if(dot<=0)return "ledger row has no owner.field";
     string ownerName=op.field.Substring(0,dot),field=op.field.Substring(dot+1);
     Component owner=ownerName=="EnemyController"?(Component)a.GetComponent<EnemyController>():ownerName=="EnemyVitals"?a.GetComponent<EnemyVitals>():null;
     if(owner==null)return "no "+ownerName+" on the actor";
     var so=new SerializedObject(owner);var p=so.FindProperty(field);if(p==null)return "field "+field+" not found";
     // rows written since D308-18 carry the asset they wrote in path: a field that holds something else now was changed by a later
     // pass and is left alone (rows of the earth ledgers written before have no path = restored as before)
     if(!string.IsNullOrEmpty(op.path)){var now=p.objectReferenceValue;string nowPath=now!=null?AssetDatabase.GetAssetPath(now):"";if(nowPath!=op.path)return "skipped: the field now holds "+(nowPath.Length==0?"(none)":nowPath)+", not what this pass wrote ("+op.path+")";}
     Object before=null;
     if(!string.IsNullOrEmpty(op.before)){before=AssetDatabase.LoadAssetAtPath<ScriptableObject>(op.before);if(before==null)return "the asset it held before is gone ("+op.before+")";}
     p.objectReferenceValue=before;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(owner);return null;
    }
    case "earth-mesh":
    {
     var a=EarthActor308(scene,op.id);if(a==null)return "actor not found";
     var part=EarthCrystal308(a,out int count);if(part==null)return count+" crystal part(s) under the actor";
     var mf=part.GetComponent<MeshFilter>();if(mf==null)return "no MeshFilter on the crystal";
     int bar=(op.before??"").LastIndexOf('|');if(bar<=0)return "ledger row has no before-mesh";
     string asset=op.before.Substring(0,bar),meshName=op.before.Substring(bar+1);
     var mesh=AssetDatabase.LoadAllAssetsAtPath(asset).OfType<Mesh>().FirstOrDefault(m=>m.name==meshName);if(mesh==null)return "mesh "+meshName+" not found in "+asset;
     mf.sharedMesh=mesh;EditorUtility.SetDirty(mf);
     if(PrefabUtility.IsPartOfPrefabInstance(mf))PrefabUtility.RecordPrefabInstancePropertyModifications(mf);
     return null;
    }
    case "earth-radius":
    {
     var a=EarthActor308(scene,op.id);if(a==null)return "actor not found";
     var part=EarthCrystal308(a,out int count);if(part==null)return count+" crystal part(s) under the actor";
     foreach(var set in a.GetComponentsInChildren<EnemyOrganSet>(true))
     {
      int i=Array.FindIndex(set.Organs??Array.Empty<EnemyOrganSet.Organ>(),o=>o!=null&&o.TargetRenderer!=null&&o.TargetRenderer.transform==part);if(i<0)continue;
      var so=new SerializedObject(set);so.FindProperty("Organs").GetArrayElementAtIndex(i).FindPropertyRelative("Radius").floatValue=float.Parse(op.before,CultureInfo.InvariantCulture);
      so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(set);return null;
     }
     return "no organ targets the crystal";
    }
    case "earth-flag":
    {
     if(op.field!="SeatedEnemiesPassThrough308")return "unknown session flag "+op.field;
     session.SeatedEnemiesPassThrough308=op.before=="1";EditorUtility.SetDirty(session);return null;
    }
    default:return Undo308(scene,session,op,true);
   }
  }

  static string EarthSceneRevert308(string scenePath)
  {
   scenePath=Scene308(scenePath);string key=EarthSceneKey308(scenePath);
   var ledger=ReadSceneLedger308(key);if(ledger==null)throw new Refuse308("no ledger "+LedgerFile308(key)+" (nothing applied, or already reverted)");
   // the revert saves the scene: never while a look preview has real renderers switched off
   PreviewGuard308(SceneData308(out _),scenePath,true);
   var scene=Open308(scenePath);var session=Session308(scene);string abs=Harness303.Abs(scene.path);
   var sb=new StringBuilder(family308.Name+" scene-revert "+scene.path+"\n");
   var last=ledger.writes.LastOrDefault();
   if(last!=null&&!string.Equals(Harness303.Sha(abs),last.shaAfter,StringComparison.OrdinalIgnoreCase))sb.AppendLine("  note the scene changed after this ledger's last write (another ledger wrote it): only this ledger's ops are undone");
   string backupDir=BackupDir308();Directory.CreateDirectory(backupDir);string copy=Path.Combine(backupDir,Path.GetFileName(scene.path));File.Copy(abs,copy,true);
   int undone=0,skipped=0;
   try
   {
    for(int w=ledger.writes.Count-1;w>=0;w--)
     for(int i=ledger.writes[w].ops.Count-1;i>=0;i--)
     {
      var op=ledger.writes[w].ops[i];if(op.undone)continue;
      string r=EarthUndo308(scene,session,op);
      if(r==null)undone++;else{skipped++;sb.AppendLine("  skipped "+op.op+" "+(string.IsNullOrEmpty(op.id)?op.path:op.id)+(op.field.Length>0?" ("+op.field+")":"")+": "+r);}
     }
   }
   catch(Exception e)
   {
    EditorSceneManager.OpenScene(scene.path,OpenSceneMode.Single);
    return "FAILED "+family308.Name+" scene-revert "+scene.path+" (scene reloaded from disk, nothing saved, ledger kept)\n"+e;
   }
   EditorUtility.SetDirty(session);EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene))
   {
    EditorSceneManager.OpenScene(scene.path,OpenSceneMode.Single);
    return "FAILED SaveScene returned false for "+scene.path+" (scene reloaded from disk, nothing saved, ledger kept; backup "+copy+")";
   }
   string archived=LedgerFile308(key)+".reverted-"+Harness303.UtcStamp();File.Move(LedgerFile308(key),archived);
   sb.AppendLine("  undone "+undone+" op(s), skipped "+skipped+"; scene saved (backup of the state before the revert: "+copy+")");
   sb.AppendLine("  first-apply backup (bytes before any earth scene op): "+(ledger.writes.Count>0?ledger.writes[0].backup:"-"));
   sb.AppendLine("  ledger archived "+archived+". The content half is "+family308.Entry+" content-revert:"+PostLedger308.Short(scene.path));
   return SceneOut308(key+"-revert.txt",sb);
  }

  // ---------- scene-check (read only): AC-E1 .. AC-E6 scene half, QE1, QE4 ----------

  static string EarthSceneCheck308(string scenePath)
  {
   var d=EarthData308();var k=Begin308(scenePath,true,out _);var c=new Check308();string tag=Key308(k.Scene.path)+" ";
   c.I(family308.Name+" data sha "+Short308(d.Sha)+", group "+d.Group);
   string sceneNote=Opt308(d.Data["scene_notes"],PostLedger308.Short(k.Scene.path));if(sceneNote!=null)c.I(tag+sceneNote);
   EarthProfileChecks308(c,d);
   EarthContentChecks308(c,tag,d,k.Scene.path,k.Content);
   EarthRoadChecks308(c,tag,d,k.Scene.path,k.Content,k.Layout);
   var vitals=d.Vitals.Length>0?AssetDatabase.LoadAssetAtPath<EnemyVitalsProfileSO>(d.Vitals):null;
   var actors=Saved308<PrologueEncounter>(k.Scene).ToArray();var fields=k.Session.VehicleBossFields308;
   var shape=EarthShape308(d.OrganElement,out string fbx);
   foreach(var r in d.RowsOf(k.Scene.path))
   {
    var e=Encounter308(k.Content,r.Id);var mine=actors.Where(x=>x.Id==r.Id).ToArray();
    if(e==null){c.C(false,tag+r.Id+": not in this content");continue;}
    if(mine.Length==0&&r.Existing){c.C(false,tag+"AC-E2 "+r.Id+": the existing actor is not in this scene");continue;}
    if(mine.Length==0&&!actors.Any(x=>x.Id==r.Template)){c.I(tag+r.Id+": template actor "+r.Template+" is not in this scene; row skipped here");continue;}
    c.C(mine.Length==1&&(r.Existing||Own308(mine[0].transform))&&!PostLedger308.UnderProtectedTree(mine[0].transform),tag+"AC-E2 "+r.Id+": one actor"+(r.Existing?" (existing, outside every protected tree)":" under "+SceneRootDefault308)+" ("+mine.Length+")");
    if(mine.Length!=1)continue;var a=mine[0];
    var vit=a.GetComponent<EnemyVitals>();var enemy=a.GetComponent<EnemyController>();var agent=a.GetComponent<NavMeshAgent>();
    c.C((k.Session.Actors??Array.Empty<PrologueEncounter>()).Contains(a)&&Wired308(k.Session,vit),tag+"AC-E2 "+r.Id+": in session.Actors and CombatLoopWiring._enemies");
    var attackNow=enemy!=null?new SerializedObject(enemy).FindProperty("_attackProfile")?.objectReferenceValue:null;
    var vitNow=vit!=null?new SerializedObject(vit).FindProperty("_profile")?.objectReferenceValue:null;
    c.C(attackNow!=null&&AssetDatabase.GetAssetPath(attackNow)==d.AttackPath(r),tag+"AC-E2 "+r.Id+": _attackProfile = "+(attackNow!=null?attackNow.name:"(none)")+" (row "+r.Attack+")");
    if(vitals==null)c.I(tag+r.Id+": EnemyVitals._profile kept = "+(vitNow!=null?vitNow.name:"(none)"));
    else c.C(vitNow!=null&&vitNow==vitals,tag+"AC-E2 "+r.Id+": EnemyVitals._profile = "+(vitNow!=null?vitNow.name:"(none)")+" ("+Path.GetFileNameWithoutExtension(d.Vitals)+")");
    if(r.Existing&&float.IsNaN(r.PreferredDistance))c.I(tag+r.Id+": PrologueEncounter fields not written by this pass (PreferredDistance "+F308(a.PreferredDistance,"F1")+" · Speed "+F308(a.Speed,"F1")+" · Detection "+F308(a.DetectionRange,"F0")+" · Leash "+F308(a.Leash,"F0")+" · Ranged "+(a.Ranged?1:0)+")");
    else c.C(Mathf.Approximately(a.PreferredDistance,r.PreferredDistance)&&Mathf.Approximately(a.Speed,e.Speed)&&Mathf.Approximately(a.DetectionRange,e.Detection)&&Mathf.Approximately(a.Leash,e.Leash)&&a.Ranged==e.Ranged&&(a.PatrolPoints??Array.Empty<Vector3>()).SequenceEqual(e.Patrol??Array.Empty<Vector3>()),
     tag+"AC-E2 "+r.Id+": PreferredDistance "+F308(a.PreferredDistance,"F1")+" · Speed "+F308(a.Speed,"F1")+" · Detection "+F308(a.DetectionRange,"F0")+" · Leash "+F308(a.Leash,"F0")+" · patrol = the content's");
    var want=e.Feet+Vector3.up*(agent!=null?agent.baseOffset:0f);
    if(!r.Existing)c.C(Vector3.Distance(a.transform.position,want)<=Mathf.Max(k.PoseTol,.05f)&&Mathf.Abs(Mathf.DeltaAngle(a.transform.eulerAngles.y,r.Yaw))<=Mathf.Max(k.YawTol,.5f),tag+r.Id+": actor on its content feet ("+F308(Vector3.Distance(a.transform.position,want),"F3")+" m), yaw "+F308(a.transform.eulerAngles.y,"F1")+" (row "+F308(r.Yaw,"F1")+")");
    // QE1: physical ground and NavMesh under the feet and the second patrol point
    if(!r.Existing)foreach(var (name,p) in new[]{("feet",e.Feet),("patrol2",e.Patrol!=null&&e.Patrol.Length>0?e.Patrol[e.Patrol.Length-1]:e.Feet)})
    {
     bool ground=Ground308(p.x,p.z,out var g,out float slope);
     c.C(ground&&Mathf.Abs(g.y-p.y)<=d.GapMax,tag+"QE1 "+r.Id+" "+name+": |content y − physical ground| "+(ground?F308(Mathf.Abs(g.y-p.y)):"-")+" ≤ "+F308(d.GapMax)+" (slope "+F308(slope,"F1")+"°)");
     c.C(NavMesh.SamplePosition(p,out var hit,d.NavRadius,agent!=null?agent.areaMask:NavMesh.AllAreas),tag+"QE1 "+r.Id+" "+name+": NavMesh within "+F308(d.NavRadius,"F0")+" m"+(hit.hit?" ("+F308(Vector3.Distance(hit.position,p))+" m)":" - none (bake first)"));
    }
    // AC-E4: outside every vehicle boss field (the field boss's arena)
    if(fields!=null&&fields.Fields!=null)foreach(var f in fields.Fields)
    {
     if(f==null||!f.Enabled)continue;var centre=k.Session.BossFieldCentre308(f);float fd=Harness303.Flat(centre,e.Feet);
     if(fd<f.Radius+60f)c.C(fd>f.Radius,tag+"AC-E4 "+r.Id+" outside the boss field "+f.Id+" ("+F308(fd,"F1")+" m > radius "+F308(f.Radius,"F0")+")");
    }
    // AC-E6 / QE4: crystal shape, organ components, nothing that glows
    var part=EarthCrystal308(a,out int count);var mf=part!=null?part.GetComponent<MeshFilter>():null;
    if(count==0)c.I(tag+"AC-E6 "+r.Id+": no crystal part under the body in this scene (XE6 skipped; OrganSurface308 check does not judge it)");
    else c.C(mf!=null&&shape!=null&&mf.sharedMesh==shape,tag+"AC-E6 "+r.Id+": crystal shape "+(mf!=null&&mf.sharedMesh!=null?mf.sharedMesh.name:"-")+" = "+OrganSurface308.MeshNameFor(d.OrganElement)+" (actor element "+(attackNow is EnemyAttackProfileSO ap?OrganSurface308.ProfileElement(ap):"-")+")");
    // review 2, finding 2: the shape is judged against the element of the profile the actor really carries, not only against the data
    {string live=attackNow is EnemyAttackProfileSO lp?OrganSurface308.ProfileElement(lp):null;
     if(count>0)c.C(live!=null&&live==d.OrganElement,tag+"AC-E6 "+r.Id+": crystal element "+d.OrganElement+" = the element of the wired profile ("+(live??"-")+")");}
    if(d.GrowRadius&&part!=null&&shape!=null)
    {
     var organ=a.GetComponentsInChildren<EnemyOrganSet>(true).SelectMany(s=>s.Organs??Array.Empty<EnemyOrganSet.Organ>()).FirstOrDefault(o=>o!=null&&o.TargetRenderer!=null&&o.TargetRenderer.transform==part);
     var set=organ!=null?EarthOrganSet308(a,organ,out _):null;
     if(set==null)c.C(false,tag+"AC-E6 "+r.Id+": no EnemyOrganSet organ targets the crystal");
     else{float reach=EarthReach308(part,shape,set,organ);c.C(organ.Radius>=EarthSphere308(reach)-.0005f,tag+"AC-E6 "+r.Id+": organ sphere "+F308(organ.Radius,"F3")+" m ≥ reach "+F308(reach,"F3")+" x 1.05 (the lit part is not cut)");}
    }
    c.C(a.GetComponentInChildren<EnemyOrganSet>(true)!=null&&a.GetComponentInChildren<EnemyElementTelegraph>(true)!=null,tag+"QE4 "+r.Id+": EnemyOrganSet + EnemyElementTelegraph on the clone");
    string glow=Glow308(a.gameObject);
    c.C(glow==null,tag+"AC-E6 발광 상한 "+r.Id+": no Light / no emissive material"+(glow!=null?" (has "+glow+")":""));
   }
   if(d.Data["runtime"]!=null)c.C(k.Session.SeatedEnemiesPassThrough308==EarthSeatFlag308(d),tag+"KE6 session.SeatedEnemiesPassThrough308 = "+k.Session.SeatedEnemiesPassThrough308+" (data runtime.seated_pass_through "+EarthSeatFlag308(d)+")");
   // idempotence: a second apply would write nothing
   var again=Begin308(scenePath,true,out _);Keepout308(again,true);EarthPlan308(again,d);
   c.C(again.Ops.Count==0&&again.Pending==0,tag+"a second "+family308.Entry+" scene-apply would write nothing (ops "+again.Ops.Count+", PENDING "+again.Pending+", WARN "+again.Warns+")");
   foreach(var l in again.Lines.Where(x=>x.StartsWith("WARN ",StringComparison.Ordinal)))c.I(tag+l);
   var ledger=ReadSceneLedger308(EarthSceneKey308(k.Scene.path));
   c.I(tag+"scene ledger: "+(ledger==null?"none":ledger.writes.Count+" write(s), "+ledger.writes.SelectMany(w=>w.ops).Count(o=>o!=null&&!o.undone)+" live op(s)"));
   c.I(Opt308(d.Data,"play_note")??"Play only (not judged here): AC-E7 riding past / boarding mid-fight (KE6), AC-E8 probe-organ LitOrganCount + '거' parry, AC-E9 reward; then OrganSurface308 check with this scene open");
   if(k.Scene.isDirty)c.I("WARN the scene is dirty after a read-only check (bug): reload it");
   return c.Done(EarthSceneKey308(k.Scene.path)+"-check.txt");
  }
 }
}
