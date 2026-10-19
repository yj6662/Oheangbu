using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 // SPEC-FIELD-FIRST-HOUR-308 C1 (read only): the ends of the side paths of one realm and what stands at each end.
 //   An end = a layout place that only one route touches (the road graph of CompactWorldLayoutSO), or an end of a run of the
 //   content BranchPath that no such place covers. Per end: the routes there, content points within the radius (reward =
 //   currency / fragment, info = conversation / stele / notice / preview, scene = evidence), the place's own interactions,
 //   scene objects by name (ends.scene_words), and the nearest field enemies (gatekeeper candidates). Verdict per end.
 //   Report file Art/Playtest306/Backups/FieldFirst308/ends-<utc>.txt.
 public static partial class FieldFirst308
 {
  sealed class EndRow{public string id,label,how;public Vector2 xz;public List<string> routes=new List<string>();}
  static string Ends()
  {
   var e=Data()["ends"]??throw new Refused("field117.json: ends missing (python Tools/Art/FieldFirst308/field117_build.py)");
   var layout=Load<CompactWorldLayoutSO>(S(e,"layout"));var content=Load<WorldMacroPlaytestSO>(S(Data(),"content"));
   string realm=S(e,"realm");float radius=e["radius_m"].Value<float>(),runGap=e["run_gap_m"].Value<float>(),enemyNear=e["enemy_near_m"].Value<float>();
   var prefixes=((e["enemy_prefixes"] as JArray)??new JArray()).Select(x=>x.Value<string>()).ToArray();
   var words=(e["scene_words"] as JObject)??new JObject();
   var places=(layout.Places??Array.Empty<CompactWorldLayoutSO.Place>()).Where(p=>p!=null).ToArray();
   var mine=places.Where(p=>string.Equals(p.Realm,realm,StringComparison.OrdinalIgnoreCase)).ToArray();
   var degree=new Dictionary<string,List<CompactWorldLayoutSO.Route>>();
   foreach(var r in layout.Routes??Array.Empty<CompactWorldLayoutSO.Route>())
    if(r!=null)foreach(var id in new[]{r.From,r.To})if(!string.IsNullOrEmpty(id)){if(!degree.TryGetValue(id,out var l))degree[id]=l=new List<CompactWorldLayoutSO.Route>();l.Add(r);}
   var ends=new List<EndRow>();var skipped=new List<string>();
   float mouth=e["mouth_m"].Value<float>(),loop=e["loop_m"].Value<float>();
   var byId=places.GroupBy(p=>p.Id).ToDictionary(g=>g.Key,g=>g.First());
   // route polylines (xz): From place, bends, To place
   Vector2[] Line(CompactWorldLayoutSO.Route r)
   {
    var l=new List<Vector2>();if(r.From!=null&&byId.TryGetValue(r.From,out var f))l.Add(f.XZ);
    l.AddRange(r.Bends??Array.Empty<Vector2>());if(r.To!=null&&byId.TryGetValue(r.To,out var t))l.Add(t.XZ);return l.ToArray();
   }
   var lines=(layout.Routes??Array.Empty<CompactWorldLayoutSO.Route>()).Where(r=>r!=null).Select(r=>(r,pts:Line(r))).ToArray();
   var mainXZ=(content.MainPath??Array.Empty<Vector3>()).Select(v=>new Vector2(v.x,v.z)).ToArray();
   // a dead end that touches the main path or another route is a mouth (the side path starts there), not an end
   string Mouth(Vector2 q,CompactWorldLayoutSO.Route own)
   {
    float dm=PolyDist(mainXZ,q);if(dm<=mouth)return "MainPath "+dm.ToString("F0")+" m";
    foreach(var (r,pts) in lines)if(r!=own){float d=PolyDist(pts,q);if(d<=mouth)return "경로 "+r.Id+" "+d.ToString("F0")+" m";}
    return null;
   }
   foreach(var p in mine)
   {
    degree.TryGetValue(p.Id,out var rs);int n=rs?.Count??0;
    if(n==0){skipped.Add(p.Id+" (경로 0 — 길 밖 자리)");continue;}
    if(n!=1)continue;
    if(rs[0].Role==CompactRouteRole.Main){skipped.Add(p.Id+" (본길 "+rs[0].Id+"의 끝)");continue;}
    var m=Mouth(p.XZ,rs[0]);if(m!=null){skipped.Add(p.Id+" (갈래 입구 — "+m+")");continue;}
    ends.Add(new EndRow{id=p.Id,label=p.Label,xz=p.XZ,how="길 끝",routes=rs.Select(r=>r.Id+"/"+r.Role).ToList()});
   }
   // BranchPath runs: a new run starts where two neighbours are more than run_gap apart (Content308 Runs308 rule)
   var bp=content.BranchPath??Array.Empty<Vector3>();var runs=new List<(Vector3 a,Vector3 b)>();int start=0;
   for(int i=1;i<=bp.Length;i++)
    if(i==bp.Length||new Vector2(bp[i].x-bp[i-1].x,bp[i].z-bp[i-1].z).magnitude>runGap){if(i-1>start)runs.Add((bp[start],bp[i-1]));start=i;}
   int ri=0;
   foreach(var (a,b) in runs)
   {
    ri++;
    if(new Vector2(a.x-b.x,a.z-b.z).magnitude<=loop){skipped.Add("BranchPath "+ri+"번 줄 (고리 — 두 끝 "+new Vector2(a.x-b.x,a.z-b.z).magnitude.ToString("F0")+" m)");continue;}
    foreach(var q in new[]{a,b})
    {
     var xz=new Vector2(q.x,q.z);
     if(!InRealm(xz,mine,e))continue;
     if(ends.Any(x=>Vector2.Distance(x.xz,xz)<=radius))continue;
     var m=Mouth(xz,null);if(m!=null){skipped.Add("BranchPath "+ri+"번 줄 끝 ("+xz.x.ToString("F0")+", "+xz.y.ToString("F0")+") 갈래 입구 — "+m);continue;}
     ends.Add(new EndRow{id="branch_run"+ri+(q==a?"_a":"_b"),label="BranchPath "+ri+"번 줄 끝",xz=xz,how="BranchPath 줄 끝"});
    }
   }
   var points=content.Points??Array.Empty<PrologueContentSO.Point>();
   var encounters=(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Where(x=>x!=null).ToArray();
   var enemies=encounters.Where(x=>prefixes.Any(px=>(x.Id??"").StartsWith(px,StringComparison.Ordinal))).ToArray();
   // scene objects by name (roots and their direct children; open the realms first)
   var named=new List<(string name,string kind,Vector3 at)>();
   for(int si=0;si<UnityEngine.SceneManagement.SceneManager.sceneCount;si++)
   {
    var sc=UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);if(!sc.isLoaded)continue;
    foreach(var g in sc.GetRootGameObjects())foreach(var t in g.GetComponentsInChildren<Transform>(false))
     foreach(var kv in words)foreach(var w in (kv.Value as JArray)??new JArray())
      if(t.name.IndexOf(w.Value<string>(),StringComparison.OrdinalIgnoreCase)>=0){named.Add((t.name,kv.Key,t.position));goto nextT;}
    nextT:;
   }
   var sb=new StringBuilder("field308-ends realm "+realm+" (places "+mine.Length+", ends "+ends.Count+", radius "+radius.ToString("F0")+" m, field enemies "+enemies.Length+", named scene objects "+named.Count+")\n");
   int empty=0;var rows=new List<string>();
   foreach(var end in ends.OrderBy(x=>x.xz.y))
   {
    var have=new List<string>();var kinds=new HashSet<string>();
    foreach(var p in points)
    {
     if(p==null)continue;float d=Vector2.Distance(new Vector2(p.Position.x,p.Position.z),end.xz);if(d>radius)continue;
     string k=PointKind(p);kinds.Add(k);have.Add(k+" "+p.Id+(p.Currency>0?" "+p.Currency+"통보":"")+" "+d.ToString("F0")+" m");
    }
    var place=places.FirstOrDefault(p=>p.Id==end.id);
    if(place!=null)foreach(var iid in (place.InteractionIds??Array.Empty<string>()).Concat(new[]{place.InteractionId}).Where(x=>!string.IsNullOrEmpty(x)).Distinct())
     if(!have.Any(h=>h.Contains(" "+iid+" ")))
     {
      var p=points.FirstOrDefault(x=>x!=null&&x.Id==iid);
      if(p==null)have.Add("(상호작용 "+iid+" — 콘텐츠 점 없음)");
      else{float d=Vector2.Distance(new Vector2(p.Position.x,p.Position.z),end.xz);have.Add("(상호작용 "+iid+" "+PointKind(p)+" "+d.ToString("F0")+" m — 반경 밖)");}
     }
    foreach(var o in named){float d=Vector2.Distance(new Vector2(o.at.x,o.at.z),end.xz);if(d<=radius){kinds.Add(o.kind);have.Add(o.kind+" [씬] "+o.name+" "+d.ToString("F0")+" m");}}
    bool ok=kinds.Contains("보상")||kinds.Contains("정보")||kinds.Contains("서사");if(!ok)empty++;
    var near=enemies.Select(x=>(x.Id,d:Vector2.Distance(new Vector2(x.Feet.x,x.Feet.z),end.xz))).OrderBy(x=>x.d).Take(2).ToArray();
    rows.Add(" "+(ok?"O":"X")+" | "+end.id+" ("+end.label+") | "+end.xz.x.ToString("F0")+", "+end.xz.y.ToString("F0")+" | "+end.how+(end.routes.Count>0?" "+string.Join(",",end.routes):"")
     +"\n     있음: "+(have.Count==0?"없음":string.Join(" · ",have.Distinct().Take(6)))
     +"\n     가까운 적: "+(near.Length==0?"없음":string.Join(" · ",near.Select(x=>x.Id+" "+x.d.ToString("F0")+" m"+(x.d<=enemyNear?"":"")))));
   }
   sb.Insert(0,"ends without reward / info / scene: "+empty+" / "+ends.Count+"\n");
   foreach(var r in rows)sb.AppendLine(r);
   sb.AppendLine(" 끝이 아닌 것: "+string.Join(" · ",skipped));
   // the field enemies of the realm and their nearest end (gatekeeper picture)
   sb.AppendLine(" field enemies -> nearest end:");
   foreach(var x in enemies.Where(x=>InRealm(new Vector2(x.Feet.x,x.Feet.z),mine,e)).OrderBy(x=>x.Id))
   {
    var f=new Vector2(x.Feet.x,x.Feet.z);var best=ends.OrderBy(t=>Vector2.Distance(t.xz,f)).FirstOrDefault();
    sb.AppendLine("  "+x.Id+" ("+f.x.ToString("F0")+", "+f.y.ToString("F0")+") -> "+(best==null?"-":best.id+" "+Vector2.Distance(best.xz,f).ToString("F0")+" m"));
   }
   string text=sb.ToString().TrimEnd();
   Directory.CreateDirectory(BackupRoot);File.WriteAllText(Path.Combine(BackupRoot,"ends-"+PostLedger308.Utc()+".txt"),text);
   return text;
  }
  // field308-flat:x,z[,r=40][,step=4][,slope=14][,clear=1.5] (read only, open scenes): ground cells around (x, z) whose slope is
  // under the limit and whose capsule of radius clear (0.3 - 2.3 m over the ground) touches no collider; nearest 16 come back
  static string Flat(string[] a)
  {
   float F(int i,float d)=>a.Length>i&&a[i].Length>0?float.Parse(a[i],System.Globalization.CultureInfo.InvariantCulture):d;
   if(a.Length<2)throw new Refused("field308-flat:x,z[,r][,step][,slope][,clear]");
   float cx=F(0,0),cz=F(1,0),r=F(2,40),step=F(3,4),slope=F(4,14),clear=F(5,1.5f);
   if(r<=0||r>300||step<1)throw new Refused("r 0 .. 300 m, step >= 1 m");
   Physics.SyncTransforms();var pass=new List<(float d,Vector3 p,float s)>();int cells=0,steep=0,blocked=0,none=0;
   for(float dx=-r;dx<=r+1e-3f;dx+=step)for(float dz=-r;dz<=r+1e-3f;dz+=step)
   {
    float d=Mathf.Sqrt(dx*dx+dz*dz);if(d>r)continue;cells++;
    RaycastHit best=default;bool got=false;
    foreach(var h in Physics.RaycastAll(new Vector3(cx+dx,3000f,cz+dz),Vector3.down,6000f,~0,QueryTriggerInteraction.Ignore))
     if(!(h.collider is CharacterController)&&(!got||h.point.y>best.point.y)){best=h;got=true;}
    if(!got){none++;continue;}
    float sl=Vector3.Angle(best.normal,Vector3.up);if(sl>slope){steep++;continue;}
    var p0=best.point+Vector3.up*(.3f+clear);var p1=best.point+Vector3.up*2.3f;
    if(Physics.CheckCapsule(p0,p1,clear,~0,QueryTriggerInteraction.Ignore)){blocked++;continue;}
    pass.Add((d,best.point,sl));
   }
   var sb=new StringBuilder("field308-flat ("+cx.ToString("F0")+", "+cz.ToString("F0")+") r "+r.ToString("F0")+" step "+step.ToString("F0")+" slope <= "+slope.ToString("F0")+"° clear "+clear.ToString("F1")+" m: cells "+cells+", pass "+pass.Count+", steep "+steep+", blocked "+blocked+", no ground "+none+"\n");
   foreach(var q in pass.OrderBy(q=>q.d).Take(16))sb.AppendLine("  ("+q.p.x.ToString("F1")+", "+q.p.z.ToString("F1")+") y "+q.p.y.ToString("F2")+" d "+q.d.ToString("F0")+" slope "+q.s.ToString("F1")+"°");
   return sb.ToString().TrimEnd();
  }
  // field308-nav:x,z[;x,z...] (read only): NavMesh.SamplePosition within 2 m of the physical ground at each point
  static string Nav(string arg)
  {
   var sb=new StringBuilder("field308-nav (sample radius 2 m)\n");int ok=0,n=0;
   foreach(var pair in (arg??"").Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries))
   {
    var f=pair.Split(',').Select(x=>float.Parse(x,System.Globalization.CultureInfo.InvariantCulture)).ToArray();n++;
    var at=new Vector3(f[0],GroundY(f[0],f[1]),f[1]);
    bool hit=UnityEngine.AI.NavMesh.SamplePosition(at,out var h,2f,UnityEngine.AI.NavMesh.AllAreas);if(hit)ok++;
    sb.AppendLine("  ("+f[0].ToString("F1")+", "+f[1].ToString("F1")+") "+(hit?"O navmesh "+Vector3.Distance(h.position,at).ToString("F2")+" m":"X no navmesh within 2 m"));
   }
   return sb.Insert(0,"navmesh "+ok+" / "+n+"\n").ToString().TrimEnd();
  }
  static float PolyDist(Vector2[] pts,Vector2 q)
  {
   if(pts.Length==0)return float.PositiveInfinity;if(pts.Length==1)return Vector2.Distance(pts[0],q);
   float best=float.PositiveInfinity;
   for(int i=0;i+1<pts.Length;i++){var ab=pts[i+1]-pts[i];float l2=ab.sqrMagnitude;float t=l2<1e-6f?0:Mathf.Clamp01(Vector2.Dot(q-pts[i],ab)/l2);best=Mathf.Min(best,Vector2.Distance(q,pts[i]+ab*t));}
   return best;
  }
  static string PointKind(PrologueContentSO.Point p)
  {
   string id=p.Id??"";
   if(p.Currency>0||p.Kind==PrologueInteractionKind.Currency||id.Contains("fragment"))return "보상";
   if(p.Kind==PrologueInteractionKind.Conversation||p.Kind==PrologueInteractionKind.Preview||id.Contains("stele")||id.Contains("notice"))return "정보";
   if(p.Kind==PrologueInteractionKind.Evidence)return "서사";
   return "쉼터";
  }
  // inside the realm = within bbox_pad_m of the bounding box of the realm's places
  static bool InRealm(Vector2 q,CompactWorldLayoutSO.Place[] mine,JToken e)
  {
   if(mine.Length==0)return false;float pad=e["bbox_pad_m"].Value<float>();
   return q.x>=mine.Min(p=>p.XZ.x)-pad&&q.x<=mine.Max(p=>p.XZ.x)+pad&&q.y>=mine.Min(p=>p.XZ.y)-pad&&q.y<=mine.Max(p=>p.XZ.y)+pad;
  }
 }
}
