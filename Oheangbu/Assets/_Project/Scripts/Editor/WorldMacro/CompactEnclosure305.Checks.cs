using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
using Sheet305=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #305 check-scene / audit-scene: Edit-mode physics only (never saves, never manual play). The walker is a hidden copy of the
 // actual player CharacterController (Spec: h 1.75, r .28, step .3, slope 45); actor and player-body colliders sit out the probes.
 public static partial class CompactRebuildAuthoring
 {
  // rise: 0 walk, 1.05 jump+step, 3.45 Guk 2.4 + jump + step (Spec AC-3)
  static readonly (string name,float rise)[] Variants305={("walk",0f),("jump+step",1.05f),("guk+jump+step",3.45f)};
  const float ProbeEvery305=20f,ProbeStart305=3f,ProbePush305=8f,Reserved305=20f,EndBack305=10f,EndPast305=8f;
  // probe verdicts: PASS = stopped by the shell (touching an Enclosure305 collider or feet reached the player face);
  // FAIL = feet past the shell centre line by more than the radius; INCONCLUSIVE = stopped by something else or stalled first
  enum Verdict305{Pass,Fail,Inconclusive}
  sealed class Tot305{public int pass,fail,inc;public void Add(Verdict305 v){if(v==Verdict305.Pass)pass++;else if(v==Verdict305.Fail)fail++;else inc++;}public override string ToString()=>"PASS "+pass+" FAIL "+fail+" INCONCLUSIVE "+inc;}

  static string Check305(string path)
  {
   var (scene,root)=Active305(path);var session=Session292();var content=session.Content??throw new Refuse305("session has no content in "+path);
   var segs=N6Segments305(out string version,out _);var skip=Skip305(session);
   var body=session.Walker!=null?session.Walker.Body:null;
   var go=new GameObject("Enclosure305_probe_fixture"){hideFlags=HideFlags.HideAndDontSave};go.SetActive(false);var cc=go.AddComponent<CharacterController>();
   var scale=Vector3.one;
   if(body!=null)
   {
    // world-size capsule: the body's local height/radius/centre times its lossyScale (the fixture sits at scale 1)
    scale=body.transform.lossyScale;float sy=Mathf.Abs(scale.y),sr=Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z));
    cc.height=body.height*sy;cc.center=Vector3.Scale(body.center,scale);cc.radius=body.radius*sr;cc.skinWidth=body.skinWidth;cc.stepOffset=body.stepOffset;cc.slopeLimit=body.slopeLimit;
   }
   else{cc.height=1.75f;cc.center=new Vector3(0,.875f,0);cc.radius=.28f;cc.stepOffset=.3f;cc.slopeLimit=45f;}
   float reach=Thick305*.5f+cc.radius+cc.skinWidth+.15f;
   var sb=new StringBuilder();var lines=new List<string>();var totals=Variants305.ToDictionary(v=>v.name,v=>new Tot305());var jtot=Variants305.ToDictionary(v=>v.name,v=>new Tot305());
   var open=Variants305.ToDictionary(v=>v.name,v=>(blocked:0,leak:0));var jlines=new List<string>();var olines=new List<string>();
   sb.AppendLine("#305 check-scene "+path+" | "+DateTime.Now.ToString("s")+" | segments305 "+version);
   sb.AppendLine("Automated Edit-mode CharacterController probes (not manual play). collider "+(body!=null?"copied from the player body (lossyScale "+scale.ToString("F3")+")":"Spec fallback")+": height="+F305(cc.height,"F3")+" radius="+F305(cc.radius,"F3")+" step="+cc.stepOffset+" slope="+cc.slopeLimit+
                 (Mathf.Abs(cc.height-1.75f)>.01f||Mathf.Abs(cc.radius-.28f)>.01f||Mathf.Abs(cc.stepOffset-.3f)>.01f||Mathf.Abs(cc.slopeLimit-45f)>.5f?" (differs from Spec h1.75 r.28 step.3 slope45)":""));
   sb.AppendLine("segment probes: every "+ProbeEvery305+" m, start "+ProbeStart305+" m on the player side 0.1 m above ground (+rise), pushed "+ProbePush305+" m toward the blocked side at 4.5 m/s; rise>0 holds the feet at max(start ground, ground below)+rise (a jump held at its apex).");
   sb.AppendLine("verdict: FAIL = feet past the shell centre polyline by > radius; PASS = not past and (touching an "+Root305+" collider or feet within "+F305(reach)+" m of the centre line = reached the player face);");
   sb.AppendLine("  INCONCLUSIVE = stopped earlier by another collider / slope / stall (the shell was never tested): retried once from 1 m before the face, the retry decides when it is conclusive.");
   sb.AppendLine("joint probes: each pair of segment ends on one line point, start "+ProbeStart305+" m on the kept side along the joint bisector, pushed through the joint and toward each shell end (chain = A + joint strip/crossing + B).");
   sb.AppendLine("open-end probes: each 'open' end, from "+EndBack305+" m back on the player side, around the end "+EndPast305+" m past it, to "+EndBack305+" m back on the blocked side; LEAK = reached the blocked-side target (decision item, BUILD_PLAN 4.1c).");
   var disabled=skip.Where(c=>c!=null&&c.enabled).ToArray();foreach(var c in disabled)c.enabled=false;Physics.SyncTransforms();
   int probes=0,jprobes=0,oprobes=0;var stations=segs.ToDictionary(g=>g,g=>Stations305(g));var joints=Joints305(segs,stations);
   try
   {
    foreach(var g in segs)
    {
     var st=stations[g];float L=st[st.Length-1].s;
     var at=new SortedSet<float>();for(float s=0;s<=L;s+=ProbeEvery305)at.Add(Mathf.Clamp(s,Mathf.Min(1.5f,L*.5f),Mathf.Max(L-1.5f,L*.5f)));at.Add(Mathf.Max(L-1.5f,L*.5f));
     foreach(float s in at)
     {
      int k=0;for(int i=1;i<st.Length;i++)if(Mathf.Abs(st[i].s-s)<Mathf.Abs(st[k].s-s))k=i;
      var s0=st[k];var from=s0.p-s0.nb*ProbeStart305;var retry=s0.q-s0.nb*(Thick305*.5f+1f);
      foreach(var (vn,rise) in Variants305)
      {
       probes++;var v=Probe305(cc,go,st,k,12,from,from+s0.nb*ProbePush305,retry,retry+s0.nb*ProbePush305,rise,root,skip,reach,out string note);totals[vn].Add(v);
       if(v!=Verdict305.Pass)lines.Add((v==Verdict305.Fail?"FAIL ":"INCONCLUSIVE ")+g.id+" s="+F305(s0.s,"F1")+" "+vn+": "+note);
      }
     }
    }
    foreach(var j in joints)
    {
     var c=j.chain;var from=j.p-j.bis*ProbeStart305;var retry=c[j.k].q-c[j.k].nb*(Thick305*.5f+1f);
     var aims=new[]{("through",j.p+j.bis*(ProbePush305-ProbeStart305)),("toward "+j.a.id,c[j.ka].q),("toward "+j.b.id,c[j.kb].q)};
     foreach(var (aim,to) in aims)
     {
      var d=(to-from);d=d.sqrMagnitude>1e-6f?d.normalized:j.bis;var d2=(to-retry);d2=d2.sqrMagnitude>1e-6f?d2.normalized:j.bis;
      foreach(var (vn,rise) in Variants305)
      {
       jprobes++;var v=Probe305(cc,go,c,j.k,c.Length,from,from+d*ProbePush305,retry,retry+d2*ProbePush305,rise,root,skip,reach,out string note);jtot[vn].Add(v);
       if(v!=Verdict305.Pass)jlines.Add((v==Verdict305.Fail?"FAIL ":"INCONCLUSIVE ")+j.name+" ("+j.kind+", angle "+F305(j.angle,"F1")+", gap "+F305(j.gap)+(j.note!=""?", "+j.note:"")+") "+aim+" "+vn+": "+note);
      }
     }
    }
    foreach(var g in segs)for(int e=0;e<2;e++)
    {
     if(g.closed||e>=g.ends.Length||g.ends[e]!="open")continue;
     var o=Toward305(stations[g],e==1,stations[g].Length);var end=o[o.Length-1];int kb=o.Length-1;
     while(kb>0&&Mathf.Abs(o[kb].s-end.s)<EndBack305)kb--;
     var outward=o.Length>1?(end.q-o[o.Length-2].q).normalized:end.t;var E=EndPoint305(g,e==1);var back=o[kb];
     var from=back.p-back.nb*ProbeStart305;var target=back.q+back.nb*ProbeStart305;
     var way=new[]{E-end.nb*ProbeStart305+outward*EndPast305,E+end.nb*ProbeStart305+outward*EndPast305,target};
     foreach(var (vn,rise) in Variants305)
     {
      oprobes++;var r=Walk305(cc,go,from,way,rise,root,skip,f=>Past305(o,kb,f));
      bool leak=r.ground&&Vector2.Distance(new Vector2(r.last.x,r.last.z),target)<1.5f;var t=open[vn];if(leak)t.leak++;else t.blocked++;open[vn]=t;
      olines.Add((leak?"LEAK ":"BLOCKED ")+g.id+" end "+e+" "+vn+": "+r.end+", touching "+r.by+", stopped "+F305(Vector2.Distance(new Vector2(r.last.x,r.last.z),target),"F1")+" m from the blocked target, feet "+r.last.ToString("F1")+r.start);
     }
    }
   }
   finally{Object.DestroyImmediate(go);foreach(var c in disabled)if(c!=null)c.enabled=true;Physics.SyncTransforms();}
   int fails=totals.Values.Sum(t=>t.fail)+jtot.Values.Sum(t=>t.fail),incs=totals.Values.Sum(t=>t.inc)+jtot.Values.Sum(t=>t.inc),leaks=open.Values.Sum(t=>t.leak);
   string verdict=fails>0||leaks>0?"FAIL":incs>0?"INCONCLUSIVE":"PASS";
   sb.AppendLine(verdict+" AC-3 probes: fail "+fails+", inconclusive "+incs+", open-end leaks "+leaks+" (actor/body colliders disabled during probes="+disabled.Length+")");
   sb.AppendLine("segment probe points="+probes/Variants305.Length+" runs="+probes);
   foreach(var kv in totals)sb.AppendLine("  "+kv.Key+": "+kv.Value);
   sb.AppendLine("joints="+joints.Count+" (arc "+joints.Count(j=>j.kind=="arc")+", cross "+joints.Count(j=>j.kind=="cross")+", flush "+joints.Count(j=>j.kind=="flush")+") runs="+jprobes);
   foreach(var kv in jtot)sb.AppendLine("  "+kv.Key+": "+kv.Value);
   sb.AppendLine("open ends="+oprobes/Variants305.Length+" runs="+oprobes);
   foreach(var kv in open)sb.AppendLine("  "+kv.Key+": BLOCKED "+kv.Value.blocked+" LEAK "+kv.Value.leak);
   foreach(var f in lines.Where(x=>x.StartsWith("FAIL")))sb.AppendLine(f);
   foreach(var f in jlines.Where(x=>x.StartsWith("FAIL")))sb.AppendLine(f);
   foreach(var f in olines.Where(x=>x.StartsWith("LEAK")))sb.AppendLine(f);
   foreach(var f in lines.Where(x=>!x.StartsWith("FAIL")))sb.AppendLine(f);
   foreach(var f in jlines.Where(x=>!x.StartsWith("FAIL")))sb.AppendLine(f);
   foreach(var f in olines.Where(x=>!x.StartsWith("LEAK")))sb.AppendLine(f);

   // reserved sites (AC-4 / AC-10): distance to the nearest segment line, FLAG < 20 m
   var items=new List<(string kind,string id,Vector3 at)>();
   var points=(content.Points??Array.Empty<PrologueContentSO.Point>()).Where(p=>p!=null&&p.Id!=null).GroupBy(p=>p.Id).ToDictionary(x=>x.Key,x=>x.First());
   var used=new HashSet<string>();
   foreach(var q in content.Commissions??Array.Empty<WorldMacroPlaytestSO.CommissionSpec>())
   {
    if(q==null)continue;
    if(q.GiverId!=null&&points.TryGetValue(q.GiverId,out var gp)){items.Add(("commission-giver",q.Id+"/"+q.GiverId,gp.Position));used.Add(q.GiverId);}
    if(q.EvidenceId!=null&&points.TryGetValue(q.EvidenceId,out var ep)){items.Add(("commission-evidence",q.Id+"/"+q.EvidenceId,ep.Position));used.Add(q.EvidenceId);}
   }
   foreach(var e in content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>())if(e!=null)items.Add(("encounter",e.Id,e.Feet));
   foreach(var c in content.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>())if(c!=null)items.Add(("checkpoint",c.Id,c.Feet));
   foreach(var p in points.Values)
   {
    if(used.Contains(p.Id))continue;
    string kind=p.Kind==PrologueInteractionKind.Rest?"shrine":p.Id.IndexOf("guk",StringComparison.OrdinalIgnoreCase)>=0?"guk-point":"point";items.Add((kind,p.Id,p.Position));
   }
   foreach(var l in Object.FindObjectsByType<Oheangbu.App.Demo.GukLiftSite>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(x=>x.gameObject.scene==scene))
    items.Add(("guk-lift",l.Id,l.Lower!=null?l.Lower.position:l.transform.position));
   foreach(var r in Object.FindObjectsByType<Oheangbu.App.Demo.DemoGukRevisitSite>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(x=>x.gameObject.scene==scene))
    items.Add(("guk-revisit",r.RewardId,r.LiftPad!=null?r.LiftPad.position:r.transform.position));
   var near=items.Select(it=>{var xz=new Vector2(it.at.x,it.at.z);var best=segs.Select(g=>(g,d:LineDist305(xz,g.pts))).OrderBy(x=>x.d).FirstOrDefault();return (it,seg:best.g?.id??"-",d:best.g!=null?best.d:float.PositiveInfinity);}).OrderBy(x=>x.d).ToList();
   int flagged=near.Count(x=>x.d<Reserved305);
   sb.AppendLine("reserved sites: "+items.Count+" ("+string.Join(", ",items.GroupBy(i=>i.kind).Select(x=>x.Key+" "+x.Count()))+"); FLAG < "+Reserved305+" m: "+flagged+" (points of kind 'point' are listed only when flagged)");
   foreach(var x in near.Where(x=>x.d<60f&&(x.it.kind!="point"||x.d<Reserved305)))
    sb.AppendLine((x.d<Reserved305?"FLAG ":"ok   ")+x.it.kind+" "+x.it.id+" -> "+x.seg+" "+F305(x.d,"F1")+" m at "+x.it.at.ToString("F1"));
   string file=O305+"/check-"+Path.GetFileNameWithoutExtension(path)+".txt";File.WriteAllText(file,sb.ToString());
   return "check "+path+": "+verdict+"; segments "+string.Join(", ",totals.Select(kv=>kv.Key+" "+kv.Value.pass+"/"+kv.Value.fail+"/"+kv.Value.inc))+" (pass/fail/inconclusive); joints "+
          string.Join(", ",jtot.Select(kv=>kv.Key+" "+kv.Value.pass+"/"+kv.Value.fail+"/"+kv.Value.inc))+"; open-end leaks "+leaks+"/"+oprobes+"; reserved-site flags "+flagged+"; "+Path.GetFullPath(file);
  }

  static Vector3 Feet305(CharacterController cc)=>cc.transform.TransformPoint(cc.center)-Vector3.up*(cc.height*.5f);
  // signed distance of xz past the shell centre polyline (toward the blocked side): nearest point on the q polyline within +-win
  // stations of k, sign and size along the interpolated blocked normal there (0 beyond a chain end along its tangent)
  static float Past305(Station305[] st,int k,Vector3 f,int win=12)
  {
   var p=new Vector2(f.x,f.z);int i0=Mathf.Max(0,k-win),i1=Mathf.Min(st.Length-1,k+win);
   if(i1<=i0)return Vector2.Dot(p-st[i0].q,st[i0].nb);
   float bd=float.PositiveInfinity,past=0;
   for(int i=i0;i<i1;i++)
   {
    var a=st[i].q;var d=st[i+1].q-a;float t=d.sqrMagnitude<1e-8f?0:Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude);var c=a+d*t;float dd=(p-c).sqrMagnitude;
    if(dd<bd){bd=dd;var n=Vector2.Lerp(st[i].nb,st[i+1].nb,t);past=Vector2.Dot(p-c,n.sqrMagnitude>1e-8f?n.normalized:st[i].nb);}
   }
   return past;
  }

  // one CC run: feet from startXZ (ground +.1 +rise) through the waypoints at 4.5 m/s. Stops at the last waypoint, a 90-tick stall
  // (no .01 m progress to the current waypoint), a fall or the tick budget. maxPast = the largest past(feet) seen.
  sealed class Walk305R{public bool ground,shell;public float maxPast=float.NegativeInfinity,gy;public Vector3 last;public string end="stalled",by="nothing",start="";}
  static Walk305R Walk305(CharacterController cc,GameObject go,Vector2 startXZ,Vector2[] way,float rise,Transform root,HashSet<Collider> skip,Func<Vector3,float> past)
  {
   var r=new Walk305R();
   if(!Ground305(startXZ.x,startXZ.y,root,skip,out float gy,out _)){r.end="no ground at the start "+startXZ.ToString("F1");r.last=new Vector3(startXZ.x,0,startXZ.y);return r;}
   r.ground=true;r.gy=gy;var feet=new Vector3(startXZ.x,gy+.1f+rise,startXZ.y);
   cc.enabled=false;go.transform.position=feet+Vector3.up*(cc.height*.5f-cc.center.y);go.SetActive(true);cc.enabled=true;Physics.SyncTransforms();
   Collider[] Around(Vector3 f,float pad)=>Physics.OverlapCapsule(f+Vector3.up*(cc.radius+.05f),f+Vector3.up*(cc.height-cc.radius),cc.radius+pad,~0,QueryTriggerInteraction.Ignore).Where(c=>c!=cc&&!skip.Contains(c)).ToArray();
   var inside=Around(feet,-.02f);if(inside.Length>0)r.start=", started inside "+string.Join(",",inside.Take(3).Select(c=>c.name));
   float len=0;var prev=startXZ;foreach(var w in way){len+=Vector2.Distance(prev,w);prev=w;}
   int budget=Mathf.CeilToInt(len/(4.5f/60))+300,wi=0,stall=0;float best=float.PositiveInfinity;
   try
   {
    for(int tick=0;tick<budget;tick++)
    {
     var f=Feet305(cc);r.maxPast=Mathf.Max(r.maxPast,past(f));
     var d=way[wi]-new Vector2(f.x,f.z);
     if(d.magnitude<.15f){if(++wi>=way.Length){r.end="reached the push target";break;}best=float.PositiveInfinity;stall=0;continue;}
     if(d.magnitude<best-.01f){best=d.magnitude;stall=0;}else if(++stall>90)break;
     if(f.y<gy-30f){r.end="fell";break;}
     var m=new Vector3(d.x,0,d.y).normalized*(4.5f/60);
     if(rise>0){float hover=gy+rise;if(Support305(f,1.5f,12f,root,skip,out float below))hover=Mathf.Max(hover,below+rise);m.y=Mathf.Clamp(hover-f.y,-.12f,.12f);}
     else m.y=-.12f;
     cc.Move(m);
    }
    r.last=Feet305(cc);r.maxPast=Mathf.Max(r.maxPast,past(r.last));
    var around=Around(r.last,.15f);r.shell=around.Any(c=>c.transform.IsChildOf(root));
    r.by=r.shell?"shell "+around.First(c=>c.transform.IsChildOf(root)).name:around.Length>0?"other:"+string.Join(",",around.Take(3).Select(c=>c.name+"@"+(c.transform.parent!=null?c.transform.parent.name:"-"))):"nothing";
    return r;
   }
   finally{cc.enabled=false;go.SetActive(false);}
  }
  static Verdict305 Judge305(Walk305R r,CharacterController cc,float reach,out string note)
  {
   note="past="+(r.ground?F305(r.maxPast):"n/a")+" m (limit "+F305(cc.radius)+"), "+r.end+", touching "+r.by+", feet "+r.last.ToString("F2")+", start ground "+(r.ground?F305(r.gy):"n/a")+r.start;
   if(!r.ground)return Verdict305.Inconclusive;
   if(r.maxPast>cc.radius)return Verdict305.Fail;
   if(r.shell||r.maxPast>=-reach)return Verdict305.Pass;
   note=(r.by!="nothing"?"blocked by "+r.by:"stalled at past="+F305(r.maxPast))+" before the shell; "+note;return Verdict305.Inconclusive;
  }
  // push probe with one retry from 1 m before the face when the first run never reached the shell
  static Verdict305 Probe305(CharacterController cc,GameObject go,Station305[] st,int k,int win,Vector2 from,Vector2 to,Vector2 retryFrom,Vector2 retryTo,float rise,Transform root,HashSet<Collider> skip,float reach,out string note)
  {
   var v=Judge305(Walk305(cc,go,from,new[]{to},rise,root,skip,f=>Past305(st,k,f,win)),cc,reach,out note);if(v!=Verdict305.Inconclusive)return v;
   var v2=Judge305(Walk305(cc,go,retryFrom,new[]{retryTo},rise,root,skip,f=>Past305(st,k,f,win)),cc,reach,out string n2);
   if(v2!=Verdict305.Inconclusive){note="retry 1 m before the face: "+n2+" | first run: "+note;return v2;}
   note+=" | retry 1 m before the face: "+n2;return v;
  }
  static string Audit305(string path)
  {
   var (scene,root)=Active305(path);var segs=N6Segments305(out string version,out _);var sb=new StringBuilder();
   sb.AppendLine("#305 audit-scene "+path+" | "+DateTime.Now.ToString("s")+" | segments305 "+version+" | automated Edit-mode audit, not manual play");

   // (a) invisible-wall audit: under the root every collider without a renderer must be a *_col shell; obstacles carry no collider
   var cols=root.GetComponentsInChildren<Collider>(true);var bad=new List<string>();int shells=0,triggers=0;
   foreach(var c in cols)
   {
    bool rendered=c.GetComponent<Renderer>()!=null;if(c.isTrigger)triggers++;
    if(!rendered&&c.name.EndsWith("_col"))shells++;
    else if(!rendered)bad.Add(HierarchyPath(c.transform)+" ("+c.GetType().Name+")");
   }
   var obs=root.GetComponentsInChildren<NavMeshObstacle>(true);int obsWithCollider=obs.Count(o=>o.GetComponent<Collider>()!=null);
   int obsBad=obs.Count(o=>!o.carving||!o.carveOnlyStationary||(o.shape!=NavMeshObstacleShape.Box&&!(o.shape==NavMeshObstacleShape.Capsule&&o.name.StartsWith("E305_P")))||(o.shape==NavMeshObstacleShape.Box&&o.size.z>BoxLen305+.01f));   // joint plugs carve a capsule
   sb.AppendLine((bad.Count==0&&obsWithCollider==0?"PASS":"FAIL")+" (a) colliders under "+Root305+"="+cols.Length+", renderer-less *_col shells="+shells+", other renderer-less="+bad.Count+", triggers="+triggers+
                 "; carve boxes="+obs.Length+" (with a collider "+obsWithCollider+", misconfigured "+obsBad+")");
   foreach(var b in bad.Take(200))sb.AppendLine("  renderer-less collider not named *_col: "+b);

   // (b) cover: every 1.6 m along each shell a tree/shrub placement 0-2.6 m in front of its player face (any sheet in the scene)
   var veg=new Grid305(8f);int sheets=0;
   foreach(var a in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(a=>a.Sheet!=null&&a.gameObject.scene==scene&&a.isActiveAndEnabled))
   {
    sheets++;var cat=a.Sheet.Prototypes.Where(p=>p!=null&&p.Id!=null).GroupBy(p=>p.Id).ToDictionary(x=>x.Key,x=>x.First().Category);
    foreach(var f in a.Sheet.FixedPlacements)if(f!=null&&f.PrototypeId!=null&&cat.TryGetValue(f.PrototypeId,out var c)&&(c==Sheet305.Kind.Tree||c==Sheet305.Kind.Shrub))veg.Add(f.Position);
   }
   int samples=0,gapSamples=0;var gaps=new List<string>();
   foreach(var g in segs)
   {
    var st=Stations305(g);var cum=new float[st.Length];for(int i=1;i<st.Length;i++)cum[i]=cum[i-1]+Vector2.Distance(st[i-1].q,st[i].q);
    float L=cum[cum.Length-1],gapStart=-1;int j=1;
    for(float s=0;s<=L+1e-3f;s+=1.6f)
    {
     while(j<st.Length-1&&cum[j]<s)j++;float t=Mathf.Clamp01((s-cum[j-1])/Mathf.Max(1e-5f,cum[j]-cum[j-1]));
     // measured from the player face qf = q + np*thick/2 (not the centre line): 0..2.6 m in front of the face
     var q=Vector2.Lerp(st[j-1].q,st[j].q,t);var np=-(Vector2.Lerp(st[j-1].nb,st[j].nb,t)).normalized;var qf=q+np*(Thick305*.5f);samples++;
     bool covered=veg.Near(qf,2.6f).Any(v=>Vector2.Dot(new Vector2(v.x,v.z)-qf,np)>=0);
     if(!covered){gapSamples++;if(gapStart<0)gapStart=s;}
     else if(gapStart>=0){gaps.Add(g.id+" s="+F305(gapStart,"F1")+".."+F305(s,"F1")+" ("+F305(s-gapStart,"F1")+" m) at "+q.ToString("F1"));gapStart=-1;}
    }
    if(gapStart>=0)gaps.Add(g.id+" s="+F305(gapStart,"F1")+"..end ("+F305(L-gapStart,"F1")+" m)");
   }
   sb.AppendLine((gapSamples==0?"PASS":"FAIL")+" (b) cover: "+samples+" samples every 1.6 m, uncovered "+gapSamples+" in "+gaps.Count+" gaps (tree/shrub placements from "+sheets+" active sheets, "+veg.Count+" indexed; 0-2.6 m in front of the player face, i.e. centre line + "+F305(Thick305*.5f)+" m)");
   foreach(var g in gaps.Take(400))sb.AppendLine("  gap "+g);
   if(gaps.Count>400)sb.AppendLine("  ... "+(gaps.Count-400)+" more gaps");

   // (d) NavMesh: after the re-bake no walkable NavMesh may sit inside a shell (agents would path straight through the thicket)
   int navSamples=0,navInside=0;var navHits=new List<string>();
   foreach(var g in segs)
   {
    var st=Stations305(g);
    for(int k=0;k<st.Length;k+=2)
    {
     if(!Ground305(st[k].q.x,st[k].q.y,root,new HashSet<Collider>(),out float gy,out _))continue;navSamples++;
     if(NavMesh.SamplePosition(new Vector3(st[k].q.x,gy,st[k].q.y),out var nh,.6f,NavMesh.AllAreas)&&new Vector2(nh.position.x-st[k].q.x,nh.position.z-st[k].q.y).magnitude<Thick305*.5f+.05f)
     {navInside++;if(navHits.Count<40)navHits.Add(g.id+" station "+k+" at "+st[k].q.ToString("F1"));}
    }
   }
   sb.AppendLine((navInside==0?"PASS":"FAIL")+" (d) NavMesh inside the shells: "+navInside+"/"+navSamples+" station samples (NavMesh.SamplePosition <= .6 m landing within the shell thickness)");
   foreach(var h in navHits)sb.AppendLine("  navmesh inside shell: "+h);
   // (c) report only: enabled non-trigger colliders without a renderer on the same object elsewhere in the scene
   var others=scene.GetRootGameObjects().Where(r=>r.name!=Root305).SelectMany(r=>r.GetComponentsInChildren<Collider>(true))
    .Where(c=>c.enabled&&c.gameObject.activeInHierarchy&&!c.isTrigger&&!(c is CharacterController)&&c.GetComponent<Renderer>()==null).ToArray();
   int terrains=others.Count(c=>c is TerrainCollider);
   sb.AppendLine("(c) report: renderer-less active non-trigger colliders outside "+Root305+"="+others.Length+" (terrain colliders "+terrains+"); by root:");
   foreach(var grp in others.Where(c=>!(c is TerrainCollider)).GroupBy(c=>c.transform.root.name).OrderByDescending(x=>x.Count()).Take(30))sb.AppendLine("  "+grp.Key+": "+grp.Count());
   string file=O305+"/audit-"+Path.GetFileNameWithoutExtension(path)+".txt";File.WriteAllText(file,sb.ToString());
   return "audit "+path+": (a) "+(bad.Count==0&&obsWithCollider==0?"PASS":"FAIL")+" shells "+shells+" other renderer-less "+bad.Count+"; (b) "+(gapSamples==0?"PASS":"FAIL")+" gaps "+gaps.Count+" ("+gapSamples+"/"+samples+" samples); (c) elsewhere "+others.Length+"; (d) "+(navInside==0?"PASS":"FAIL")+" navmesh inside shells "+navInside+"/"+navSamples+"; "+Path.GetFullPath(file);
  }
 }
}
