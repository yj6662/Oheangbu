using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static void CheckGates296(List<string> report)
  {
   var session=Session292();var scene=session.gameObject.scene;var field=new CompactWorldSurface(session.MountainLayout);
   string path=O296+"/gates.json";if(!File.Exists(path)){report.Add("FAIL missing gate build receipt");return;}
   var receipt=JsonUtility.FromJson<GateReceipt296>(File.ReadAllText(path));
   var gates=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<SouthGateDoorPresentation>(true)).Where(g=>g.gameObject.activeInHierarchy).ToArray();
   var states=gates.Select(g=>(gate:g,requested:g.IsOpenRequested,complete:g.OpenAnimationComplete)).ToArray();
   var facts=session.Content.Campaign.Stages.Select(s=>s.Id+":"+s.Implemented).ToArray();
   bool Barrier(RaycastHit hit)=>hit.collider.gameObject.scene==scene&&(hit.collider.transform.root.name=="Architecture296_Gates"||hit.collider.transform.root.name=="CapitalSouthGate253");
   bool Blocked(Vector3 a,Vector3 b)=>Physics.RaycastAll(a,(b-a).normalized,Vector3.Distance(a,b),~0,QueryTriggerInteraction.Ignore).Any(Barrier);
   bool GradedPassage(GateAperture296 opening,out string detail)
   {
    Vector3 prior=default;detail="";
    for(int i=0;i<=20;i++)
    {
     var p=opening.Centre+opening.Inward*(i*.5f-5);float terrain=field.Sample(p.x,p.z);
     float top=Mathf.Max(terrain,opening.Centre.y)+3;
     var floor=Physics.RaycastAll(new Vector3(p.x,top,p.z),Vector3.down,Mathf.Max(12,top-terrain+4),~0,QueryTriggerInteraction.Ignore)
      .Where(h=>h.collider.gameObject.scene==scene&&h.rigidbody==null&&!(h.collider is CharacterController)&&h.normal.y>=Mathf.Cos(session.Walker.Body.slopeLimit*Mathf.Deg2Rad)).OrderBy(h=>h.distance).FirstOrDefault();
     if(floor.collider==null){detail="missing walkable support at "+p;return false;}
     p=floor.point+Vector3.up*1.1f;
     if(i>0)
     {
      var hit=Physics.RaycastAll(prior,(p-prior).normalized,Vector3.Distance(prior,p),~0,QueryTriggerInteraction.Ignore).FirstOrDefault(Barrier);
      if(hit.collider!=null){detail="body ray blocked by "+Path296(hit.collider.transform)+" at "+hit.point;return false;}
     }
     prior=p;
    }
    detail="twenty body rays follow actual permanent approach support";return true;
   }
   void Check(bool ok,string text)=>report.Add((ok?"PASS ":"FAIL ")+text);
   try
   {
    foreach(var gate in gates)gate.SetOpened(false,true);Physics.SyncTransforms();
    Check(gates.Length>0&&gates.All(g=>g.IsConfigured&&!g.OpenAnimationComplete),"all active capital doors have canonical leaf/blocker bindings and close");
    foreach(var loop in receipt.Loops)
    {
     int count=0,miss=0,gukMiss=0,mumMiss=0;var details=new List<string>();
     for(int edge=0;edge<loop.Points.Length;edge++)
     {
      var a=loop.Points[edge];var b=loop.Points[(edge+1)%loop.Points.Length];a.y=b.y=0;float length=Vector3.Distance(a,b);var along=(b-a).normalized;var inward=Vector3.Cross(along,Vector3.up);
      for(float d=.5f;d<length;d+=2)
      {
       if(loop.Gates.Any(g=>g.Edge==edge&&Mathf.Abs(g.Distance-d)<g.Width*.5f+.5f))continue;
       var centre=a+along*d;var outside=centre-inward*12;var inside=centre+inward*12;
       float low=field.Sample(outside.x,outside.z),high=field.Sample(inside.x,inside.z),ground=field.Sample(centre.x,centre.z);
       float y=Mathf.Max(ground,Mathf.Max(low,high));count++;
       outside.y=inside.y=y+1.0f;
       if(!Blocked(outside,inside)){miss++;if(details.Count<5)details.Add("body "+centre.ToString("F1"));}
       outside.y=inside.y=y+3.4f;
       if(!Blocked(outside,inside)){gukMiss++;if(details.Count<5)details.Add("2.4m lift+head "+centre.ToString("F1"));}
       // A maximum legal Mum deck between the nearby banks has at most a 2m
       // end rise. Its walking/body volume must meet the visible closed wall.
       outside.y=low+1;inside.y=high+1;
       if(Mathf.Abs(low-high)<=2&&!Blocked(outside,inside)){mumMiss++;if(details.Count<5)details.Add("Mum chord "+centre.ToString("F1"));}
      }
     }
     Check(count>0&&miss==0,loop.Id+" closed visible perimeter body rays="+count+" misses="+miss);
     Check(gukMiss==0,loop.Id+" 2.4m Guk rise plus body clearance misses="+gukMiss);
     Check(mumMiss==0,loop.Id+" Mum legal-height chord barrier misses="+mumMiss);
     foreach(string detail in details)report.Add("DETAIL "+detail);
     foreach(var opening in loop.Gates)
     {
      var centre=opening.Centre;var inward=opening.Inward.normalized;
      var from=centre-inward*5+Vector3.up*1.1f;var to=centre+inward*5+Vector3.up*1.1f;
      if(!string.IsNullOrEmpty(opening.OpenFact))Check(Blocked(from,to),opening.Id+" closed threshold blocks walking and straight summon line of sight");
      else {bool clear=GradedPassage(opening,out var detail);Check(clear,opening.Id+" reserved precinct entrance remains open without a new progression lock; "+detail);}
     }
    }
    foreach(var gate in gates)gate.SetOpened(true,true);Physics.SyncTransforms();
    foreach(var opening in receipt.Loops.SelectMany(l=>l.Gates).Where(g=>!string.IsNullOrEmpty(g.OpenFact)))
    {
     var from=opening.Centre-opening.Inward*5+Vector3.up*1.1f;var to=opening.Centre+opening.Inward*5+Vector3.up*1.1f;
     Check(!Blocked(from,to),opening.Id+" opened threshold has no remaining wall or door blocker");
    }
    foreach(var gate in gates)
    {
     var serialized=new SerializedObject(gate);var owner=serialized.FindProperty("_session").objectReferenceValue;
     Check(owner==session,"door "+gate.name+" reads the same existing session victory fact");
     var navigation=serialized.FindProperty("_closedNavigation");Check(navigation.arraySize>0,"door "+gate.name+" has dynamic navigation blocker");
    }
    Check(session.Content.Campaign.Stages.Select(s=>s.Id+":"+s.Implemented).SequenceEqual(facts),"gate geometry check does not enable reserved campaign stages");
    var inactive=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Compact_MountainContent_290");
    Check(inactive==null||!inactive.activeSelf,"legacy mountain archive/return gates remain inactive");
    var bridge=JsonUtility.FromJson<BridgeReceipt296>(File.ReadAllText(O296+"/crossings.json"));
    Check(bridge.Bridges.Length==4&&bridge.Bridges.Sum(b=>b.RouteIds.Length)==5,"four physical bridges own all five route IDs");
    Check(bridge.Bridges.Count(b=>b.RouteIds.Length==2)==1,"capital routes explicitly share one physical bridge");
    var profile=session.MumBridgeProfile;Check(profile!=null&&profile.SurfaceTile!=null&&profile.SurfaceMaterials.Length==profile.SurfaceTile.subMeshCount,"Mum private source tile has complete material slots");
    Check(profile!=null&&profile.MinimumSpan==4&&profile.MaximumSpan==40&&Mathf.Approximately(profile.Width,2.6f)&&profile.MaximumEndHeightDifference==2&&profile.MaximumSlope==8&&Mathf.Approximately(profile.FormationSeconds,1.2f),"Mum appearance preserves approved gameplay dimensions and timing");
    report.Add("INFO gate checks are actual-scene geometry/physics probes; live saved victory, movement and payment are verified by their separate Play fixtures.");
   }
   finally
   {
    foreach(var state in states)if(state.gate!=null)state.gate.SetOpened(state.requested,state.complete||!state.requested);Physics.SyncTransforms();
   }
   File.WriteAllLines(O296+"/gate-checks.txt",report);
  }
 }
}
