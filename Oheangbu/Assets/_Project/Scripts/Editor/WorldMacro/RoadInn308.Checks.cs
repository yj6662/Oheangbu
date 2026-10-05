using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Oheangbu.App.World;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 // RoadInn308 read-only half: check:<scene> (EXT_BUILD_BRIEF 5절 AC-I1..I5, I8 - the data and scene parts; Play parts are not
 // here; AC-I9 = where the lantern hangs, RoadInn308.Place.cs, relayout fix 2b) and probe:<scene> (QI1, QI2, QI3, QI5, and AC-I9
 // for the data's lantern place before it is written). Nothing is written and no scene is saved. A check that holds is "ok", one that
 // does not is "FAIL"; the verdict words IMPLEMENTED / VALIDATED belong to the Spec, not to this report.
 public static partial class RoadInn308
 {
  sealed class Checks
  {
   public readonly StringBuilder Sb=new StringBuilder();public int Fails,Oks;
   public void C(bool ok,string what){if(ok)Oks++;else Fails++;Sb.AppendLine("  "+(ok?"ok   ":"FAIL ")+what);}
   public void I(string what)=>Sb.AppendLine("  info "+what);
  }
  static float SegDist(Vector2 p,Vector2 a,Vector2 b){var ab=b-a;float l=ab.sqrMagnitude;float t=l<1e-6f?0:Mathf.Clamp01(Vector2.Dot(p-a,ab)/l);return Vector2.Distance(p,a+ab*t);}
  // XZ distance to the MainPath centre line (a jump over road_run_gap_m starts a new run)
  static float RoadDistance(Cfg cfg,WorldMacroPlaytestSO c,Vector3 at)
  {
   float best=float.PositiveInfinity,gap=cfg.R("road_run_gap_m");var p=new Vector2(at.x,at.z);var path=c.MainPath??Array.Empty<Vector3>();
   for(int i=1;i<path.Length;i++){var a=new Vector2(path[i-1].x,path[i-1].z);var b=new Vector2(path[i].x,path[i].z);if(Vector2.Distance(a,b)<=gap)best=Mathf.Min(best,SegDist(p,a,b));}
   return best;
  }
  // colliders a standing player capsule at `feet` would overlap (the player's own body never counts; the keeper's does)
  static string[] CapsuleHits(Cfg cfg,WorldMacroPlaytestSession session,Vector3 feet)
  {
   float r=cfg.R("feet_probe_radius_m"),h=cfg.R("feet_probe_height_m");
   var own=session.Walker!=null&&session.Walker.Body!=null?session.Walker.Body.transform:null;
   return Physics.OverlapCapsule(feet+Vector3.up*(r+.1f),feet+Vector3.up*(h-r),r,~0,QueryTriggerInteraction.Ignore)
    .Where(c=>c!=null&&!(c is CharacterController)&&(own==null||!c.transform.IsChildOf(own))&&!Preview(c.transform)).Select(c=>BuildingAudit308.KeyOf(c.transform)).Distinct().ToArray();
  }

  static string Check(string sceneArg)
  {
   var cfg=Load();var t=TargetOf(cfg,sceneArg);PreviewGuard(cfg,t,false);
   var scene=Open(t);var session=Session(scene);var c=Content(t,session);var k=new Checks();string id=cfg.RestId;var rest=cfg.Rest;var L=cfg.Lantern;
   var head=new StringBuilder("check "+t.Scene+"\n  data "+DataFile+" sha "+Short(cfg.Sha)+"\n");
   string skipWhy=SkipReason(cfg,scene,c);
   if(skipWhy!=null){head.AppendLine("  skipped (not in this scene): "+skipWhy);return Report("roadinn308_check_"+Key(t)+".txt",head);}
   PrepareGround(cfg,scene,session);
   var point=Point(c,id);var cp=Checkpoint(c,id);var keeper=Point(c,cfg.KeeperId);var feetRow=Req(rest,"feet");float tol=cfg.R("pose_tol_m");
   // AC-I1 content rest
   k.C(point!=null&&point.Kind==PrologueInteractionKind.Rest&&Mathf.Abs(point.Radius-Num(rest,"radius"))<1e-3f&&point.Prompt==Str(rest,"prompt")&&point.Text==Str(rest,"text"),
    "AC-I1 Points["+id+"]: Rest, radius "+(point!=null?F(point.Radius,"F1"):"-")+", prompt '"+(point?.Prompt??"")+"'");
   k.C(cp!=null&&cp.IsConfigured&&cp.Label==Str(rest,"label")&&cp.Shop==Flag(rest,"shop"),"AC-I1 Checkpoints["+id+"]: label '"+(cp?.Label??"")+"' (the map / load-screen name), shop "+(cp!=null&&cp.Shop?1:0));
   if(point!=null)k.C(Harness303.Flat(point.Position,new Vector3(Num(rest,"x"),0,Num(rest,"z")))<=tol+cfg.R("pin_keep_m"),"AC-I1 rest point on its pinned place "+V(point.Position));
   if(cp!=null)k.C(Harness303.Flat(cp.Feet,new Vector3(Num(feetRow,"x"),0,Num(feetRow,"z")))<=tol+cfg.R("pin_keep_m")&&Mathf.Abs(Mathf.DeltaAngle(cp.Yaw,Num(feetRow,"yaw")))<=cfg.R("yaw_tol_deg"),
    "AC-I1 respawn feet "+V(cp.Feet)+" yaw "+F(cp.Yaw,"F1")+" as the row says");
   if(point!=null&&Ground(point.Position.x,point.Position.z,out var gp,out float slopeP))
    k.C(Mathf.Abs(gp.y-point.Position.y)<=cfg.R("y_tol_m")&&slopeP<=Num(rest,"max_slope_deg"),"QI1 rest point on the physical ground (|dy| "+F(Mathf.Abs(gp.y-point.Position.y))+", slope "+F(slopeP,"F1")+"°)");
   if(cp!=null&&Ground(cp.Feet.x,cp.Feet.z,out var gf,out float slopeF))
    k.C(Mathf.Abs(gf.y-cp.Feet.y)<=cfg.R("y_tol_m"),"QI1 respawn feet on the physical ground (|dy| "+F(Mathf.Abs(gf.y-cp.Feet.y))+", slope "+F(slopeF,"F1")+"°)");
   // AC-I2 keeper rows, text rules
   var bad=new List<string>();var want=Services(cfg,bad);
   k.C(bad.Count==0,"text rules: lines and row names within the length limit, no banned word"+(bad.Count>0?" ("+string.Join("; ",bad)+")":""));
   k.C(JsonUtility.ToJson(new ServiceBox{v=keeper.Services??Array.Empty<PrologueContentSO.PointService306>()})==JsonUtility.ToJson(new ServiceBox{v=want}),
    "AC-I2 "+cfg.KeeperId+".Services = the data's "+want.Length+" row(s) ("+string.Join(", ",(keeper.Services??Array.Empty<PrologueContentSO.PointService306>()).Select(s=>s.Kind+(string.IsNullOrEmpty(s.Label)?"":" '"+s.Label+"'")))+"); they are appended in all four commission states");
   k.C((keeper.Services??Array.Empty<PrologueContentSO.PointService306>()).Any(s=>s.Kind==PrologueContentSO.PointServiceKind306.Rest&&s.Target==id)&&point!=null,"AC-I2 the 쉬기 row targets an existing Rest point ("+id+")");
   // AC-I3 distances
   if(cp!=null&&point!=null)
   {
    float toKeeper=Harness303.Flat(cp.Feet,keeper.Position),road=RoadDistance(cfg,c,cp.Feet),far=Harness303.Flat(point.Position,keeper.Position)+Num(cfg.Keeper,"talk_radius_m");
    k.C(toKeeper>=cfg.R("feet_keeper_min_m"),"AC-I3 respawn feet to the keeper "+F(toKeeper)+" m (≥ "+F(cfg.R("feet_keeper_min_m"),"F1")+")");
    k.C(road>=cfg.R("feet_road_min_m"),"AC-I3 respawn feet to the MainPath centre "+F(road)+" m (≥ "+F(cfg.R("feet_road_min_m"),"F1")+")");
    k.C(far<=point.Radius,"AC-I3 keeper talk circle within the rest radius ("+F(far)+" ≤ "+F(point.Radius,"F1")+": 정비 opens anywhere in the talk circle)");
    var hits=CapsuleHits(cfg,session,cp.Feet);
    k.C(hits.Length==0,"AC-I4 respawn feet clear of every collider (capsule r "+F(cfg.R("feet_probe_radius_m"))+" h "+F(cfg.R("feet_probe_height_m"),"F1")+")"+(hits.Length>0?" - inside: "+string.Join(", ",hits):""));
   }
   // AC-I4 scene compound
   var root=Root(scene,cfg.RootName);var holder=root!=null?root.Find(id):null;var lantern=holder!=null?holder.Find(Str(L,"name")):null;
   k.C(lantern!=null,"AC-I4 "+cfg.RootName+"/"+id+"/"+Str(L,"name")+" in the scene");
   if(lantern!=null)
   {
    if(point!=null)k.C(Harness303.Flat(holder.position,point.Position)<=tol,"AC-I4 holder on the content point");
    var markers=root.GetComponentsInChildren<WorldMacroContentPoint>(true);k.C(markers.Length==1&&markers[0].Id==id&&markers[0].Visual==lantern,"AC-I4 one content point marker ("+id+")");
    var ld=Req(L,"light");int max=Mathf.RoundToInt(Num(ld,"max"));
    var lights=root.GetComponentsInChildren<Light>(true).Where(l=>l.enabled&&l.gameObject.activeInHierarchy).ToArray();
    k.C(lights.Length==max&&lights.All(l=>l.type.ToString()==Str(ld,"type")&&l.shadows==LightShadows.None&&l.range<=Num(ld,"range_m")+1e-3f&&l.intensity<=Num(ld,"intensity")+1e-3f),
     "AC-I4 / AC-I8 new sustained lights "+lights.Length+" (= "+max+"): "+string.Join(", ",lights.Select(l=>l.type+" range "+F(l.range,"F1")+" intensity "+F(l.intensity,"F1")+" shadows "+l.shadows)));
    k.C(root.GetComponentsInChildren<Collider>(true).Length==0,"AC-I4 no collider under "+cfg.RootName);
    var vis=Array.Find(session.InteractionVisuals??Array.Empty<WorldMacroPlaytestSession.InteractionVisual>(),v=>v!=null&&v.Id==id);
    k.C(vis!=null&&(vis.Renderers??Array.Empty<Renderer>()).Any(x=>x!=null),"AC-I4 InteractionVisuals["+id+"] with a renderer ("+(vis?.Renderers?.Count(x=>x!=null)??0)+")");
    // no new material, no emission of its own: every material of the clone is one the source uses
    try
    {
     var src=Source(cfg,scene);var srcMats=new HashSet<Material>(src.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials));
     var mine=lantern.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
     k.C(mine.All(m=>srcMats.Contains(m)),"AC-I8 materials of the clone are the source's ("+mine.Length+" material(s), new 0)");
    }
    catch(Refuse r){k.I("material comparison skipped: "+r.Message);}
    var off=Req(L,"offset_from_point");
    if(point!=null)k.C(Vector3.Distance(lantern.position,holder.position+new Vector3(At(off,0),At(off,1),At(off,2)))<=tol,"XI3 lantern at the data offset "+V(lantern.position));
    // AC-I9 (relayout fix 2b): the place itself - outside every collider by the clearance, hung in the roof, off the door, seen from the road
    PlaceLines(cfg,scene,lantern,Vector3.zero,k.C,k.I);
    foreach(var (name,at) in new[]{("keeper",keeper.Position),("respawn feet",cp!=null?cp.Feet:lantern.position),("rest point",point!=null?point.Position:lantern.position)})
     k.I("lantern to "+name+" "+F(Vector3.Distance(lantern.position,at))+" m (light range "+F(Num(ld,"range_m"),"F1")+")");
   }
   // AC-I5 the keeper's commission and the rest of the keeper point: what the first content write recorded must still hold
   var ledger=ReadLedger("content_"+Key(t));var first=ledger?.writes.FirstOrDefault(w=>w!=null&&!string.IsNullOrEmpty(w.guard));
   string guardNow=BuildingAudit308.ShaText(Guard(cfg,c));
   if(first==null)k.I("AC-I5 no content ledger yet: commission guard sha now "+Short(guardNow));
   else k.C(first.guard==guardNow,"AC-I5 "+Str(cfg.Keeper,"commission")+" and the keeper point (other than Services) identical to before the first write (guard sha "+Short(guardNow)+")");
   // XI4
   var seal=AssetDatabase.LoadAssetAtPath<WorldSealProfileSO>(Str(cfg.J["seal"],"profile"));
   k.C(seal!=null&&(seal.EaRestIds??Array.Empty<string>()).Contains(id),"XI4 seal profile EaRestIds contains "+id);
   k.I("not checked here (Play / other tools): Roadside303 play-test 18/18, campaign-sig equality (AC-I6), escort regression (AC-I7), EventWash308 AC-W7, the rest greeting on screen");
   head.Append(k.Sb).AppendLine("  "+k.Oks+" ok, "+k.Fails+" FAIL");
   if(scene.isDirty)head.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
   return Report("roadinn308_check_"+Key(t)+".txt",head);
  }

  // ---------- probes (EXT_BUILD_BRIEF 5절 QI1, QI2, QI3, QI5) ----------

  static string Probe(string sceneArg)
  {
   var cfg=Load();var t=TargetOf(cfg,sceneArg);PreviewGuard(cfg,t,false);
   var scene=Open(t);var session=Session(scene);var c=Content(t,session);var rest=cfg.Rest;var L=cfg.Lantern;var feetRow=Req(rest,"feet");var pr=Req(cfg.J,"probes");
   var sb=new StringBuilder("probe "+t.Scene+" (read-only)\n  data "+DataFile+" sha "+Short(cfg.Sha)+"\n");
   string skipWhy=SkipReason(cfg,scene,c);
   if(skipWhy!=null){sb.AppendLine("  skipped (not in this scene): "+skipWhy);return Report("roadinn308_probe_"+Key(t)+".txt",sb);}
   PrepareGround(cfg,scene,session);
   // QI1
   if(!Ground(Num(rest,"x"),Num(rest,"z"),out var p,out float slopeP))throw new Refuse("no ground under the rest point");
   if(!Ground(Num(feetRow,"x"),Num(feetRow,"z"),out var f,out float slopeF))throw new Refuse("no ground under the respawn feet");
   sb.AppendLine("  QI1 rest point ground "+V(p)+" slope "+F(slopeP,"F1")+"° (offline y "+F(Num(rest,"y_offline"))+", Δ "+F(p.y-Num(rest,"y_offline"))+")");
   var hits=CapsuleHits(cfg,session,f);
   sb.AppendLine("  QI1 respawn feet ground "+V(f)+" slope "+F(slopeF,"F1")+"° (offline y "+F(Num(feetRow,"y_offline"))+", Δ "+F(f.y-Num(feetRow,"y_offline"))+"); capsule overlaps "+hits.Length+(hits.Length>0?": "+string.Join(", ",hits):""));
   // QI5: the save-resume ring around the feet
   int n=Mathf.RoundToInt(cfg.R("resume_ring_n")),free=0;float ringR=cfg.R("resume_ring_m"),nav=cfg.R("nav_sample_m");
   for(int i=0;i<n;i++)
   {
    float a=(Num(feetRow,"yaw")+i*360f/n)*Mathf.Deg2Rad;var q=new Vector3(f.x+Mathf.Sin(a)*ringR,0,f.z+Mathf.Cos(a)*ringR);
    bool ground=Ground(q.x,q.z,out var g,out float s);bool onNav=ground&&NavMesh.SamplePosition(g,out _,nav,NavMesh.AllAreas);var ov=ground?CapsuleHits(cfg,session,g):Array.Empty<string>();
    if(ground&&onNav&&ov.Length==0)free++;
    sb.AppendLine("  QI5 ring "+i+" "+(ground?V(g)+" slope "+F(s,"F1")+"°":"no ground")+" NavMesh("+F(nav,"F1")+" m) "+onNav+" overlaps "+ov.Length+(ov.Length>0?" ("+string.Join(", ",ov)+")":""));
   }
   sb.AppendLine("  QI5 "+free+" of "+n+" ring spots usable (≥ 1 needed; 0 = the save resume cannot find a safe place: move the feet toward the road)");
   // QI2: what hangs over the lantern place
   var off=Req(L,"offset_from_point");var at=p+new Vector3(At(off,0),At(off,1),At(off,2));float up=cfg.R("hang_probe_up_m");
   var root=Root(scene,cfg.RootName);var lantern=root!=null?root.Find(cfg.RestId+"/"+Str(L,"name")):null;
   float top=at.y;
   if(lantern!=null){var rs=lantern.GetComponentsInChildren<Renderer>(true);if(rs.Length>0)top=rs.Max(r=>r.bounds.max.y);}
   var from=new Vector3(at.x,top,at.z);
   if(Physics.Raycast(from,Vector3.up,out var roof,up,~0,QueryTriggerInteraction.Ignore))
    sb.AppendLine("  QI2 lantern "+(lantern!=null?"top":"target")+" y "+F(top)+": collider above at "+F(roof.distance)+" m ("+BuildingAudit308.KeyOf(roof.collider.transform)+"); wanted gap 0–"+F(cfg.R("hang_gap_max_m"))+" m, else lantern.offset_from_point[1]");
   else
   {
    // a roof without a collider: the lowest renderer bounds over this XZ is only a hint (a thatch roof's bounds are a box)
    var over=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(false)).Where(r=>!Own(cfg,r.transform)&&r.bounds.min.x<=at.x&&r.bounds.max.x>=at.x&&r.bounds.min.z<=at.z&&r.bounds.max.z>=at.z&&r.bounds.max.y>top&&r.bounds.size.y<12f)
     .OrderBy(r=>r.bounds.min.y).Take(3).Select(r=>BuildingAudit308.KeyOf(r.transform)+" y "+F(r.bounds.min.y)+".."+F(r.bounds.max.y));
    sb.AppendLine("  QI2 lantern "+(lantern!=null?"top":"target")+" y "+F(top)+": no collider within "+F(up,"F1")+" m above (renderer-only roof); renderers over this XZ: "+string.Join("; ",over)+" - judge the hang in still SI4");
   }
   // QI3: eye -> lantern along the MainPath (colliders only)
   float eye=Num(pr,"eye_m"),step=Num(pr,"sight_step_m"),maxD=Num(pr,"sight_max_m");var path=c.MainPath??Array.Empty<Vector3>();int clear=0,seen=0;float along=0,next=0;var list=new List<string>();
   for(int i=1;i<path.Length;i++)
   {
    float seg=Harness303.Flat(path[i-1],path[i]);if(seg>cfg.R("road_run_gap_m")){along=0;next=0;continue;}
    while(next<=along+seg)
    {
     var q=Vector3.Lerp(path[i-1],path[i],seg<1e-4f?0:(next-along)/seg);next+=step;
     float d=Vector3.Distance(q,at);if(d>maxD||!Ground(q.x,q.z,out var g,out _))continue;
     seen++;var e=g+Vector3.up*eye;bool blocked=Physics.Linecast(e,at,out var hit,~0,QueryTriggerInteraction.Ignore)&&!Own(cfg,hit.collider.transform);
     if(!blocked){clear++;list.Add(F(d,"F0")+" m");}
    }
    along+=seg;
   }
   sb.AppendLine("  QI3 MainPath samples within "+F(maxD,"F0")+" m: "+seen+", clear line to the lantern (colliders only): "+clear+" (design floor "+Mathf.RoundToInt(Num(pr,"sight_min_clear"))+" in front of the eave) at "+string.Join(", ",list));
   sb.AppendLine("  note QI4 (grass within 2 m of P / F) is ContentSeat308's grass pass; roofs without colliders do not block QI3 - AC-I9 below reads the drawn meshes");
   // AC-I9 for the place the DATA gives (read-only, before any write): the lantern's drawn shape carried to that place
   if(lantern==null)sb.AppendLine("  AC-I9: no lantern in the scene yet (scene-apply creates it; check:<scene> then measures its place)");
   else
   {
    var place=new Checks();PlaceLines(cfg,scene,lantern,at-lantern.position,place.C,place.I);
    sb.AppendLine("  AC-I9 for the data's lantern place "+V(at)+(Vector3.Distance(at,lantern.position)>cfg.R("pose_tol_m")?" - the lantern now stands at "+V(lantern.position)+" ("+F(Vector3.Distance(at,lantern.position))+" m away: scene-apply re-seats it)":" (= where the lantern stands)")+": "+place.Oks+" ok, "+place.Fails+" FAIL");
    sb.Append(place.Sb);
   }
   if(scene.isDirty)sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
   return Report("roadinn308_probe_"+Key(t)+".txt",sb);
  }
 }
}
