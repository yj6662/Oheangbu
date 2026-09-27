using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string RefreshInteriorPortals296()
  {
   RequireClean292();if(EditorApplication.isPlayingOrWillChangePlaymode||Session292().gameObject.scene.path!=Scene296)throw new InvalidOperationException("Interior portal refresh requires the saved296 Edit candidate.");
   var sheet=Sheet296();var field=new CompactWorldSurface(Session292().MountainLayout);var report=new List<string>();var ledger=JsonUtility.FromJson<VenuePlacementLedger296>(File.ReadAllText(O296+"/venue-placements.json"));
   foreach(var arena in sheet.Arenas.Where(a=>a.Interior))
   {
    var root=Root296(arena.SceneRoot).transform;var group=root.GetComponent<LODGroup>();if(group!=null)Object.DestroyImmediate(group);
    foreach(var child in root.Cast<Transform>().Where(t=>t.name=="Physical_SourceFaces"||t.name=="SecondEave_OutsideClearFloor"||t.name.StartsWith("L0_",StringComparison.Ordinal)||t.name.StartsWith("L1_",StringComparison.Ordinal)||t.name.StartsWith("L2_",StringComparison.Ordinal)).ToArray())Object.DestroyImmediate(child.gameObject);
    var plan=new VenuePlan296{Id=arena.Id,Realm=arena.Realm,Place=arena.PlaceId,Clear=arena.ClearSize,Yaw=arena.Yaw,Centre=arena.Centre,Height=arena.ClearHeight,Interior=true};
    var batch=new VenueBatch296(plan.Id,root){StoneStyle=plan.Realm};TerraceVenue296(plan,root,field,batch);InteriorVenue296(plan,root,batch,report,true);batch.Save();
    var receipt=ledger.Venues.First(v=>v.Id==arena.Id);receipt.SourcePieces=batch.Pieces;receipt.NearTriangles=batch.NearTriangles;receipt.Sources=batch.Sources.OrderBy(v=>v,StringComparer.Ordinal).ToArray();
    report.Add(arena.Id+" four axial portals: central posts removed, jamb posts at+/-6.6m, nominal11.6m clear between1.6m bases; main roof and parent transform retained; body pieces="+batch.Pieces+" triangles="+batch.NearTriangles);
   }
   File.WriteAllText(O296+"/venue-placements.json",JsonUtility.ToJson(ledger,true));Physics.SyncTransforms();Save292();report.AddRange(InteriorEntrancesQa296());File.WriteAllLines(O296+"/interior-portals-refresh.txt",report);return string.Join("\n",report);
  }
  static List<string> InteriorEntrancesQa296()
  {
   var session=Session292();var source=session.Walker.Body;var lines=new List<string>{"INFO interior entrance tests use direct actual-player CharacterController movement; no NavMesh detour, actor reset or gate mutation."};
   var go=new GameObject("Architecture296_interior_entrance_probe"){hideFlags=HideFlags.HideAndDontSave};go.SetActive(false);var cc=go.AddComponent<CharacterController>();cc.height=source.height;cc.center=source.center;cc.radius=source.radius;cc.skinWidth=source.skinWidth;cc.stepOffset=source.stepOffset;cc.slopeLimit=source.slopeLimit;
   lines.Add("INFO portal capsule height="+cc.height+" radius="+cc.radius+" skin="+cc.skinWidth+" step="+cc.stepOffset+"; eight metres per leg, offsets -1.5/0/+1.5m, both directions.");
   try
   {
    foreach(var arena in Sheet296().Arenas.Where(a=>a.Interior))
    {
     var root=Root296(arena.SceneRoot).transform;float floor=root.position.y+.02f;
     foreach(int axis in new[]{0,1})foreach(int side in new[]{-1,1})
     {
      float half=(axis==0?arena.ClearSize.x:arena.ClearSize.y)*.5f+5;var local=axis==0?new Vector3(side*half,.22f,0):new Vector3(0,.22f,side*half);var origin=root.TransformPoint(local);var along=root.TransformDirection(axis==0?Vector3.forward:Vector3.right);float clearance=0;bool walls=true;
      foreach(int acrossSide in new[]{-1,1}){var hits=Physics.RaycastAll(origin,along*acrossSide,12,~0,QueryTriggerInteraction.Ignore).Where(h=>h.collider.transform.IsChildOf(root)).OrderBy(h=>h.distance).ToArray();if(hits.Length==0){walls=false;break;}clearance+=hits[0].distance;}
      lines.Add((walls&&clearance>=11.5f?"PASS ":"FAIL ")+arena.Id+" portal clear width "+(axis==0?"x":"z")+side+" at stone bases="+clearance.ToString("F3")+"m; nominal11.6m");
     }
     foreach(int axis in new[]{0,1})foreach(int side in new[]{-1,1})foreach(float offset in new[]{-1.5f,0,1.5f})foreach(bool reverse in new[]{false,true})
     {
      float half=(axis==0?arena.ClearSize.x:arena.ClearSize.y)*.5f+5;
      var outer=axis==0?new Vector3(side*(half+2.4f),.02f,offset):new Vector3(offset,.02f,side*(half+2.4f));var inner=axis==0?new Vector3(side*(half-5.6f),.02f,offset):new Vector3(offset,.02f,side*(half-5.6f));
      var start=root.TransformPoint(reverse?inner:outer);var end=root.TransformPoint(reverse?outer:inner);var forward=(end-start).normalized;var across=Vector3.Cross(Vector3.up,forward);string id=arena.Id+" portal "+(axis==0?"x":"z")+side+" offset="+offset+" "+(reverse?"outbound":"inbound");
      cc.enabled=false;go.SetActive(true);go.transform.position=start+Vector3.up*(cc.height*.5f-cc.center.y+.06f);cc.enabled=true;Physics.SyncTransforms();string failure=null;float best=float.PositiveInfinity,maxDrift=0,minFeet=float.PositiveInfinity,maxFeet=float.NegativeInfinity;int stalled=0,moves=0;
      for(;moves<420;moves++)
      {
       var feet=cc.transform.TransformPoint(cc.center)-Vector3.up*cc.height*.5f;minFeet=Mathf.Min(minFeet,feet.y);maxFeet=Mathf.Max(maxFeet,feet.y);var delta=end-feet;delta.y=0;maxDrift=Mathf.Max(maxDrift,Mathf.Abs(Vector3.Dot(feet-start,across)));
       if(feet.y<floor-.18f){failure="fall below rendered floor at "+feet.ToString("F3");break;}
       if(feet.y>floor+.30f){failure="unexpected raised obstacle at "+feet.ToString("F3");break;}
       if(maxDrift>.22f){failure="capsule pushed out of direct entry lane at "+feet.ToString("F3");break;}
       if(moves%4==0)
       {
        var hits=Physics.RaycastAll(feet+Vector3.up*.25f,Vector3.down,.60f,~0,QueryTriggerInteraction.Ignore).Where(h=>h.collider!=cc&&h.collider.transform.IsChildOf(root)&&h.normal.y>.8f).ToArray();
        if(!hits.Any(h=>Mathf.Abs(h.point.y-floor)<.08f)){failure="missing physical floor at "+feet.ToString("F3");break;}
       }
       if(delta.magnitude<.12f)break;
       if(delta.magnitude<best-.01f){best=delta.magnitude;stalled=0;}else stalled++;
       if(stalled>45){failure="blocked direct opening at "+feet.ToString("F3");break;}
       var motion=Vector3.ClampMagnitude(delta,.075f);motion.y=-.10f;cc.Move(motion);
      }
      if(moves>=420)failure="movement budget exceeded";lines.Add((failure==null?"PASS ":"FAIL ")+id+" moves="+moves+" lateral="+maxDrift.ToString("F3")+" feet="+minFeet.ToString("F3")+".."+maxFeet.ToString("F3")+(failure==null?"": " "+failure));
      if(failure!=null)
      {
       var feet=cc.transform.TransformPoint(cc.center)-Vector3.up*cc.height*.5f;var a=feet+Vector3.up*(cc.radius+.06f);var b=feet+Vector3.up*(cc.height-cc.radius+.06f);
       foreach(var hit in Physics.CapsuleCastAll(a,b,cc.radius,forward,1.5f,~0,QueryTriggerInteraction.Ignore).Where(h=>h.collider!=cc).OrderBy(h=>h.distance).Take(5))lines.Add("  PORTAL_CONTACT "+ScenePathVenue296(hit.collider.transform)+" at="+hit.point.ToString("F3")+" normal="+hit.normal.ToString("F3"));
      }
      cc.enabled=false;go.SetActive(false);
     }
    }
   }
   finally{Object.DestroyImmediate(go);}
   File.WriteAllLines(O296+"/interior-entrances.txt",lines);return lines;
  }
 }
}
