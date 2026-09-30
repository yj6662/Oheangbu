using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
using Sheet306=Oheangbu.Data.World.WorldMacroDressingSheetSO;
using Foot306=Oheangbu.App.World.CompactNaturalSolids.Footprint306;

namespace Oheangbu.EditorTools.WorldMacro
{
 // PLAN §2-6 change 7 (SPEC-PLAYTEST-306 #6, D306): natural solids on or beside a walking route. Queue-safe: refusals come back as strings, never a dialog.
 // Routes = routes.json polylines (their width) + the active session content MainPath/BranchPath (PathWidth306). Candidates = the pool's own
 // rules (CompactNaturalSolids.Footprints306) over the art sheets of the active #306 scene; nothing here creates a collider.
 //   scan[:move]     -> Art/Playtest306/Checks/path-solids.txt (id, sheet, protected, position, gap to the corridor edge, route, proposed action)
 //   apply[:move]    the same plan, then: unprotected sheet -> sideways move (Position only: prototype/euler/scale kept, pivot onto terrain);
 //                   protected (Watershed295/Reworld292/MountainTrail285) or no valid spot -> Profile.SkipPlacements entry (sheet untouched);
 //                   Forest305 (Enclosure305 N6 band, blocks by design) -> skip only when the route point is inside the play area, never moved.
 //                   Audited 296 copies (CandidatePath in Architecture296/Landscape/clearance.json: transforms must equal the protected source
 //                   minus the ledger removals, audit_architecture296_landscape.py) -> skip unless ':move' (then that audit check fails until
 //                   the ledger records the moves). The 296 landscape ledger removes the ones whose collider centre is inside a route
 //                   ('generated route passage'); of the rest, a footprint wholly outside the corridor (gap >= 0) keeps its collider in
 //                   place and an edge overlap stays a skip. A tree within CoverBand of an N6 line is never moved (Enclosure305 check (b) cover).
 //                   Backups (sheet/profile files) + manifest.json under Art/Playtest306/Backups/NaturalSolids306_<stamp>/
 //   revert[:<dir>]  undo the latest (or the named) un-reverted manifest: each placement still at its applied spot goes back, added skips removed
 // The three #306 scenes share these sheets and the profile: apply once, in any of them.
 public static class NaturalSolids306
 {
  const string Routes="../Art/World/Compact/Rebuild/Architecture296/Generated/routes.json",Segments="../Art/World/Compact/Rebuild/Enclosure305/segments305.json";
  const string Report="../Art/Playtest306/Checks/path-solids.txt",Backups="../Art/Playtest306/Backups",Stem="NaturalSolids306_";
  const string ProfilePath="Assets/_Project/Data/World/NaturalSolidProfile306.asset",Owned="Assets/_Project/Art/World/Enclosure305/";
  const string Ledger="../Art/World/Compact/Rebuild/Architecture296/Landscape/clearance.json";
  static readonly string[] Protected={"Assets/_Project/Art/World/Watershed295/","Assets/_Project/Art/World/Reworld292/","Assets/_Project/Art/World/MountainTrail285/"};
  static readonly string[] Scenes={"Assets/_Project/Scenes/World/W_Demo_Main.unity","Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity","Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity"};
  // TEST: report band past the corridor edge; clearance a move leaves (collider edge to corridor edge); edge gaps tried in order; max sideways shift;
  // route/pivot height window; content path width and teleport cut; moved pivot: terrain height window, slope (normal.y), spacing to other solids;
  // N6 line distance beyond which a route point counts as interior; N6 line distance within which a tree counts as shell cover (check (b): 0.7 + 2.6 m)
  const float Near=1.5f,Clear=.6f,MaxShift=8f,RouteDy=5f,PathWidth=2f,PathBreak=40f,GroundDy=2.5f,Slope=.6f,Spacing=.3f,PlayBand=40f,CoverBand=4.5f,Cell=16f;
  static readonly float[] Targets={1.6f,1f,Clear};
  static readonly CultureInfo IC=CultureInfo.InvariantCulture;

  public static string Run(string command)
  {
   command=(command??"").Trim();
   try
   {
    if(command=="scan"||command=="scan:move")return Plan(false,command.EndsWith(":move",StringComparison.Ordinal));
    if(command=="apply"||command=="apply:move")return Plan(true,command.EndsWith(":move",StringComparison.Ordinal));
    if(command=="revert")return Revert(null);
    if(command.StartsWith("revert:",StringComparison.Ordinal))return Revert(command.Substring(7).Trim());
   }
   catch(Refuse r){return "REFUSED "+r.Message;}
   catch(Exception e){Debug.LogException(e);return "ERROR NaturalSolids306 "+command+": "+e.GetType().Name+": "+e.Message+" (apply writes the manifest before saving: revert undoes whatever was saved)";}
   return "REFUSED unknown NaturalSolids306 command '"+command+"' (scan[:move] | apply[:move] | revert[:<backup dir name>])";
  }
  sealed class Refuse:Exception{public Refuse(string m):base(m){}}

  // ---------- inputs ----------

  sealed class Route{public string id;public float half;public Vector3[] pts;}
  [Serializable] class RoutePoint{public float x,y,z;}
  [Serializable] class RouteIn{public string id;public float width;public bool vehicle;public RoutePoint[] points;}
  [Serializable] class RoutesIn{public RouteIn[] routes;}
  sealed class Line{public Vector2[] pts;public int block;public bool closed;}
  struct Seg{public int r;public Vector2 a,b;public float ya,yb;}
  sealed class Item{public Sheet306 sheet;public string path;public Foot306 f;public float gap,centre;public string route,action="";public bool prot,owned,audited,inside,move,skip;public Vector3 to;public float toGap;}

  static List<Route> LoadRoutes(WorldMacroPlaytestSO content)
  {
   if(!File.Exists(Routes))throw new Refuse("routes missing: "+Path.GetFullPath(Routes));
   var list=new List<Route>();var json=JsonUtility.FromJson<RoutesIn>(File.ReadAllText(Routes).TrimStart((char)0xFEFF));
   foreach(var r in json?.routes??Array.Empty<RouteIn>())if(r?.points!=null&&r.points.Length>=2)list.Add(new Route{id=r.id,half=Mathf.Max(0,r.width)*.5f,pts=r.points.Select(p=>new Vector3(p.x,p.y,p.z)).ToArray()});
   // content paths: cut at jumps (teleports / joined polylines), each run its own route
   void AddPath(string id,Vector3[] p)
   {
    if(p==null)return;int start=0,part=0;
    for(int i=1;i<=p.Length;i++)if(i==p.Length||new Vector2(p[i].x-p[i-1].x,p[i].z-p[i-1].z).magnitude>PathBreak){if(i-start>=2)list.Add(new Route{id=id+"#"+part++,half=PathWidth*.5f,pts=p.Skip(start).Take(i-start).ToArray()});start=i;}
   }
   if(content!=null){AddPath("content_main_path",content.MainPath);AddPath("content_branch_path",content.BranchPath);}
   return list;
  }
  // Enclosure305 N6 lines (blocked side = block * right normal of the travel direction, as Stations305)
  static List<Line> LoadLines()
  {
   var list=new List<Line>();if(!File.Exists(Segments))return list;
   string s=File.ReadAllText(Segments);int at=s.IndexOf("\"segments\": [",StringComparison.Ordinal);if(at<0)at=s.IndexOf("\"segments\":[",StringComparison.Ordinal);if(at<0)return list;
   var ids=Regex.Matches(s.Substring(at),"\"id\"\\s*:").Cast<Match>().Select(m=>m.Index+at).ToList();ids.Add(s.Length);
   for(int n=0;n+1<ids.Count;n++)
   {
    string b=s.Substring(ids[n],ids[n+1]-ids[n]);if(!Regex.IsMatch(b,"\"kit\"\\s*:\\s*\"N6\""))continue;
    int p=b.IndexOf("\"points\"",StringComparison.Ordinal);if(p<0)continue;int q=b.IndexOf('[',p),depth=0,e=q;for(;e<b.Length;e++){if(b[e]=='[')depth++;else if(b[e]==']'&&--depth==0)break;}
    var pts=Regex.Matches(b.Substring(q+1,Math.Max(0,e-q-1)),@"\[\s*(-?[\d.eE+]+)\s*,\s*(-?[\d.eE+]+)\s*\]").Cast<Match>().Select(m=>new Vector2(float.Parse(m.Groups[1].Value,IC),float.Parse(m.Groups[2].Value,IC))).ToArray();
    var bm=Regex.Match(b,"\"block\"\\s*:\\s*(-?[\\d.]+)");
    if(pts.Length>=2)list.Add(new Line{pts=pts,block=bm.Success&&double.Parse(bm.Groups[1].Value,IC)<0?-1:1,closed=Regex.IsMatch(b,"\"closed\"\\s*:\\s*true")});
   }
   return list;
  }
  // inside the play area = on the player side of the nearest N6 line, or farther than PlayBand from every line
  static bool Inside(List<Line> lines,Vector2 q)=>Nearest(lines,q,out float side)>PlayBand||side<=0;
  // flat distance to the nearest N6 line; side > 0 = on its blocked side
  static float Nearest(List<Line> lines,Vector2 q,out float side)
  {
   float best=float.PositiveInfinity;side=-1;
   foreach(var l in lines)
   {
    var P=l.closed&&l.pts.Length>2?l.pts.Concat(new[]{l.pts[0]}).ToArray():l.pts;
    for(int i=0;i+1<P.Length;i++)
    {
     var d=P[i+1]-P[i];float t=d.sqrMagnitude<1e-8f?0:Mathf.Clamp01(Vector2.Dot(q-P[i],d)/d.sqrMagnitude);var c=P[i]+d*t;float dist=Vector2.Distance(q,c);
     if(dist<best&&d.sqrMagnitude>1e-8f){best=dist;var dir=d.normalized;side=Vector2.Dot(q-c,new Vector2(dir.y,-dir.x)*l.block);}
    }
   }
   return best;
  }
  // CandidatePath of every sheet the Architecture296 landscape ledger audits against its immutable source
  static HashSet<string> LoadAudited()=>File.Exists(Ledger)?new HashSet<string>(Regex.Matches(File.ReadAllText(Ledger),@"""CandidatePath""\s*:\s*""([^""]+)""").Cast<Match>().Select(m=>m.Groups[1].Value)):new HashSet<string>();

  // ---------- geometry ----------

  static Vector2 XZ(Vector3 v)=>new Vector2(v.x,v.z);
  static long Key(int x,int z)=>((long)x<<32)^(uint)z;
  static Vector2 Closest(Vector2 p,Vector2 a,Vector2 b){var d=b-a;float t=d.sqrMagnitude<1e-8f?0:Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude);return a+d*t;}
  // world xz -> box local (Quaternion.Euler(0,yaw,0) maps local x to (cos,-sin))
  static Vector2 Local(Vector2 v,float yaw){float r=yaw*Mathf.Deg2Rad,c=Mathf.Cos(r),s=Mathf.Sin(r);return new Vector2(v.x*c-v.y*s,v.x*s+v.y*c);}
  static float BoxPoint(Vector2 p,Vector2 h){float dx=Mathf.Max(0,Mathf.Abs(p.x)-h.x),dy=Mathf.Max(0,Mathf.Abs(p.y)-h.y);return Mathf.Sqrt(dx*dx+dy*dy);}
  // flat distance footprint -> segment (0 when they touch)
  static float Distance(in Foot306 f,Vector2 a,Vector2 b)
  {
   var c=XZ(f.Centre);if(!f.Box)return Mathf.Max(0,Vector2.Distance(c,Closest(c,a,b))-f.Radius);
   Vector2 la=Local(a-c,f.Yaw),lb=Local(b-c,f.Yaw),h=f.HalfSize;
   // Liang-Barsky: does the segment enter the rectangle?
   float t0=0,t1=1;var d=lb-la;bool hit=true;
   for(int k=0;k<4&&hit;k++){float p=k==0?-d.x:k==1?d.x:k==2?-d.y:d.y,q=k==0?la.x+h.x:k==1?h.x-la.x:k==2?la.y+h.y:h.y-la.y;
    if(Mathf.Abs(p)<1e-9f){if(q<0)hit=false;}else{float t=q/p;if(p<0){if(t>t1)hit=false;else if(t>t0)t0=t;}else{if(t<t0)hit=false;else if(t<t1)t1=t;}}}
   if(hit)return 0;
   float m=Mathf.Min(BoxPoint(la,h),BoxPoint(lb,h));
   for(int k=0;k<4;k++){var corner=new Vector2((k&1)==0?-h.x:h.x,(k&2)==0?-h.y:h.y);m=Mathf.Min(m,Vector2.Distance(corner,Closest(corner,la,lb)));}
   return m;
  }
  // footprint extent along a flat unit direction
  static float Extent(in Foot306 f,Vector2 n){if(!f.Box)return f.Radius;var l=Local(n,f.Yaw);return Mathf.Abs(l.x)*f.HalfSize.x+Mathf.Abs(l.y)*f.HalfSize.y;}

  sealed class RouteGrid
  {
   public readonly List<Route> routes;readonly Dictionary<long,List<Seg>> cells=new Dictionary<long,List<Seg>>();
   public RouteGrid(List<Route> routes,float pad)
   {
    this.routes=routes;
    for(int r=0;r<routes.Count;r++){var R=routes[r];float p=R.half+pad;for(int k=0;k+1<R.pts.Length;k++)
    {
     var s=new Seg{r=r,a=XZ(R.pts[k]),b=XZ(R.pts[k+1]),ya=R.pts[k].y,yb=R.pts[k+1].y};
     for(int x=Mathf.FloorToInt((Mathf.Min(s.a.x,s.b.x)-p)/Cell);x<=Mathf.FloorToInt((Mathf.Max(s.a.x,s.b.x)+p)/Cell);x++)
      for(int z=Mathf.FloorToInt((Mathf.Min(s.a.y,s.b.y)-p)/Cell);z<=Mathf.FloorToInt((Mathf.Max(s.a.y,s.b.y)+p)/Cell);z++)
      {long key=Key(x,z);if(!cells.TryGetValue(key,out var l))cells.Add(key,l=new List<Seg>());l.Add(s);}
    }}
   }
   // smallest gap between the footprint and a corridor edge (negative = inside); route id, centre distance, closest centreline point, corridor half width
   public float Gap(in Foot306 f,out string id,out float centre,out Vector2 q,out float half)
   {
    float best=float.PositiveInfinity;id=null;centre=0;q=default;half=0;var c=XZ(f.Centre);
    if(!cells.TryGetValue(Key(Mathf.FloorToInt(c.x/Cell),Mathf.FloorToInt(c.y/Cell)),out var l))return best;
    foreach(var s in l)
    {
     var d=s.b-s.a;float t=d.sqrMagnitude<1e-8f?0:Mathf.Clamp01(Vector2.Dot(c-s.a,d)/d.sqrMagnitude);if(Mathf.Abs(Mathf.Lerp(s.ya,s.yb,t)-f.Pivot.y)>RouteDy)continue;
     var R=routes[s.r];float g=Distance(f,s.a,s.b)-R.half;
     if(g<best){best=g;id=R.id;q=s.a+d*t;centre=Vector2.Distance(c,q);half=R.half;}
    }
    return best;
   }
  }
  sealed class SolidGrid
  {
   readonly Dictionary<long,List<(int u,int i)>> cells=new Dictionary<long,List<(int,int)>>();readonly List<List<Foot306>> units;
   readonly HashSet<(int,int)> moved=new HashSet<(int,int)>();readonly List<Foot306> placed=new List<Foot306>();
   public SolidGrid(List<List<Foot306>> units)
   {
    this.units=units;
    for(int u=0;u<units.Count;u++)for(int i=0;i<units[u].Count;i++){var c=units[u][i].Centre;long key=Key(Mathf.FloorToInt(c.x/Cell),Mathf.FloorToInt(c.z/Cell));if(!cells.TryGetValue(key,out var l))cells.Add(key,l=new List<(int,int)>());l.Add((u,i));}
   }
   public void Move(int u,int i,Foot306 to){moved.Add((u,i));placed.Add(to);}
   // another solid closer than the sum of radii + Spacing (circles; a box counts with its half diagonal)
   public bool Crowded(in Foot306 f,int u,int i,out string by)
   {
    by=null;var c=XZ(f.Centre);int x0=Mathf.FloorToInt(c.x/Cell),z0=Mathf.FloorToInt(c.y/Cell);
    for(int x=x0-1;x<=x0+1;x++)for(int z=z0-1;z<=z0+1;z++)if(cells.TryGetValue(Key(x,z),out var l))foreach(var k in l)
    {
     if(k.u==u&&k.i==i||moved.Contains(k))continue;var o=units[k.u][k.i];if(Mathf.Abs(o.Pivot.y-f.Pivot.y)>RouteDy)continue;
     if(Vector2.Distance(c,XZ(o.Centre))<f.Radius+o.Radius+Spacing){by=o.Id;return true;}
    }
    foreach(var o in placed)if(Vector2.Distance(c,XZ(o.Centre))<f.Radius+o.Radius+Spacing){by=o.Id;return true;}
    return false;
   }
  }

  // terrain = TerrainCollider or a Terrain*/ *Surface* object or parent (the #305 Ground rule)
  static bool Terrain(Collider c)
  {
   if(c is TerrainCollider)return true;
   for(var t=c.transform;t!=null;t=t.parent){var n=t.name;if(n.Contains("Terrain")||n.Contains("Surface"))return true;}
   return false;
  }
  static bool Ignored(Collider c,int solidLayer)=>c==null||c is CharacterController||c.gameObject.layer==solidLayer;
  // terrain y under xz nearest to near.y (within GroundDy, walkable slope); column above it must be free up to `clear`
  static bool Ground(Vector2 xz,float nearY,float clear,int solidLayer,out float y,out string why)
  {
   y=0;why=null;float best=float.PositiveInfinity;var hits=Physics.RaycastAll(new Vector3(xz.x,nearY+40,xz.y),Vector3.down,80,~0,QueryTriggerInteraction.Ignore);
   foreach(var h in hits)if(!Ignored(h.collider,solidLayer)&&Terrain(h.collider)&&h.normal.y>=Slope&&Mathf.Abs(h.point.y-nearY)<=GroundDy&&Mathf.Abs(h.point.y-nearY)<Mathf.Abs(best-nearY))best=h.point.y;
   if(float.IsPositiveInfinity(best)){why="no walkable terrain within "+GroundDy.ToString("F1",IC)+" m";return false;}
   foreach(var h in hits)if(!Ignored(h.collider,solidLayer)&&h.point.y>best+.05f&&h.point.y<best+clear){why="column taken by "+h.collider.name;return false;}
   y=best;return true;
  }
  static bool Free(in Foot306 f,float groundY,int solidLayer,out string why)
  {
   why=null;float top=Mathf.Clamp(f.Top,.5f,3f);Collider[] hits;
   if(f.Box)hits=Physics.OverlapBox(new Vector3(f.Centre.x,groundY+.25f+(top-.25f)*.5f,f.Centre.z),new Vector3(f.HalfSize.x,Mathf.Max(.05f,(top-.25f)*.5f),f.HalfSize.y),Quaternion.Euler(0,f.Yaw,0),~0,QueryTriggerInteraction.Ignore);
   else hits=Physics.OverlapCapsule(new Vector3(f.Centre.x,groundY+.25f+f.Radius,f.Centre.z),new Vector3(f.Centre.x,groundY+Mathf.Max(.25f+f.Radius,top),f.Centre.z),f.Radius,~0,QueryTriggerInteraction.Ignore);
   foreach(var c in hits)if(!Ignored(c,solidLayer)&&!Terrain(c)){why="overlaps "+c.name;return false;}
   return true;
  }

  // ---------- plan / apply ----------

  static string Plan(bool apply,bool allowMove)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Refuse("Edit mode only (Play is running)");
   var scene=SceneManager.GetActiveScene();if(!Scenes.Contains(scene.path))throw new Refuse("active scene "+scene.path+" is not a #306 scene ("+string.Join(", ",Scenes)+")");
   var pool=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactNaturalSolids>(true)).FirstOrDefault();
   var profile=pool!=null&&pool.Profile!=null?pool.Profile:AssetDatabase.LoadAssetAtPath<NaturalSolidProfileSO>(ProfilePath);
   if(profile==null&&apply)throw new Refuse("no natural solid profile (run Playtest306Setup solids-scene:<scene> first)");
   bool temp=profile==null;if(temp)profile=ScriptableObject.CreateInstance<NaturalSolidProfileSO>();
   try{return PlanWith(apply,allowMove,scene,pool,profile,temp);}
   finally{if(temp)Object.DestroyImmediate(profile);}
  }
  static string PlanWith(bool apply,bool allowMove,Scene scene,CompactNaturalSolids pool,NaturalSolidProfileSO profile,bool temp)
  {
   var session=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).FirstOrDefault();
   var arts=pool!=null&&pool.Sources!=null&&pool.Sources.Any(r=>r!=null)?pool.Sources.Where(r=>r!=null).ToArray():scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).ToArray();
   var sheets=arts.Where(r=>r.Sheet!=null).Select(r=>r.Sheet).Distinct().ToList();if(sheets.Count==0)throw new Refuse("no CompactRebuildArtRenderer sheet in "+scene.path);
   var routes=LoadRoutes(session!=null?session.Content:null);var lines=LoadLines();var audit=LoadAudited();
   int solidLayer=LayerMask.NameToLayer(profile.LayerName);
   var units=sheets.Select(s=>{var l=new List<Foot306>();CompactNaturalSolids.Footprints306(s,profile,l);return l;}).ToList();
   float maxR=units.SelectMany(u=>u).Select(f=>f.Radius).DefaultIfEmpty(0).Max();
   var grid=new RouteGrid(routes,Near+maxR+1);var solids=new SolidGrid(units);Physics.SyncTransforms();
   var items=new List<Item>();int candidates=0;
   for(int u=0;u<units.Count;u++)
   {
    var sheet=sheets[u];string path=AssetDatabase.GetAssetPath(sheet);bool prot=Protected.Any(p=>path.StartsWith(p,StringComparison.Ordinal)),owned=path.StartsWith(Owned,StringComparison.Ordinal),audited=audit.Contains(path);
    for(int i=0;i<units[u].Count;i++)
    {
     candidates++;var f=units[u][i];float gap=grid.Gap(f,out string id,out float centre,out Vector2 q,out float half);if(!(gap<=Near))continue;
     var it=new Item{sheet=sheet,path=path,f=f,gap=gap,centre=centre,route=id,prot=prot,owned=owned,audited=audited,inside=Inside(lines,q)};items.Add(it);
     if(f.Listed){it.action="skip (listed)";continue;}
     if(owned&&!it.inside){it.action="ignore (N6 forest outside the play area: blocks by design)";continue;}
     if(gap>=Clear){it.action="keep (clear "+gap.ToString("F2",IC)+" m)";continue;}
     if(prot||owned){it.skip=true;it.action=prot?"skip (protected sheet)":"skip (Forest305: Enclosure305 owns the sheet)";continue;}
     // an audited 296 copy that does not reach the corridor keeps its collider where it is (it cannot be moved without ':move')
     if(audited&&!allowMove&&gap>=0){it.action="keep (audited 296 copy outside the corridor, gap "+gap.ToString("F2",IC)+" m)";continue;}
     if(f.Tree&&Nearest(lines,XZ(f.Pivot),out _)<CoverBand){it.skip=true;it.action="skip (N6 cover tree within "+CoverBand.ToString("F1",IC)+" m of a shell line: Enclosure305 check (b))";continue;}
     // sideways out of the corridor: own side first, then across; wider gaps first
     var c=XZ(f.Centre);var n=c-q;if(n.sqrMagnitude<.0025f){n=Vector2.right;var R=grid.routes.FirstOrDefault(r=>r.id==id);if(R!=null){float bd=float.PositiveInfinity;for(int k=0;k+1<R.pts.Length;k++){var a=XZ(R.pts[k]);var b=XZ(R.pts[k+1]);float d=Vector2.Distance(c,Closest(c,a,b));if(d<bd&&(b-a).sqrMagnitude>1e-6f){bd=d;var t=(b-a).normalized;n=new Vector2(t.y,-t.x);}}}}
     n.Normalize();string why="no side within "+MaxShift.ToString("F0",IC)+" m";
     // keep the pivot's own sink/lift over its terrain (bakes sit props into the ground)
     float g0y=f.Pivot.y,lift=Ground(XZ(f.Pivot),f.Pivot.y,.05f,solidLayer,out float y0,out _)?Mathf.Clamp(f.Pivot.y-y0,-1,.5f):0;
     bool found=false;Foot306 dest=default;float destGap=0,destShift=0;
     foreach(float sign in new[]{1f,-1f})
     {
      if(found)break;var nn=n*sign;
      foreach(float target in Targets)
      {
       var shift=q+nn*(half+Extent(f,nn)+target)-c;if(shift.magnitude>MaxShift){why="shift "+shift.magnitude.ToString("F1",IC)+" m > "+MaxShift.ToString("F0",IC);continue;}
       var g=f;var d3=new Vector3(shift.x,0,shift.y);g.Pivot+=d3;g.Centre+=d3;
       float ng=grid.Gap(g,out _,out _,out _,out _);if(ng<Clear-.01f){why="lands in another corridor";continue;}
       if(!Ground(XZ(g.Pivot),g0y,Mathf.Max(2,f.Top),solidLayer,out float gy,out why))continue;
       if(!Free(g,gy,solidLayer,out why))continue;
       if(solids.Crowded(g,u,i,out string by)){why="crowds "+by;continue;}
       float dy=gy+lift-g.Pivot.y;g.Pivot.y+=dy;g.Centre.y+=dy;
       found=true;dest=g;destGap=ng;destShift=shift.magnitude;break;
      }
     }
     string spot=found?destShift.ToString("F2",IC)+" m -> "+V(dest.Pivot)+" (gap "+(float.IsPositiveInfinity(destGap)?"clear":destGap.ToString("F2",IC)+" m")+")":null;
     // an audited 296 copy keeps the source transforms (landscape audit) unless the caller opted into moves
     if(found&&audited&&!allowMove){it.skip=true;it.action="skip (audited 296 copy; ':move' would move "+spot+")";}
     else if(found){it.move=true;it.to=dest.Pivot;it.toGap=destGap;solids.Move(u,i,dest);it.action="move "+spot;}
     else{it.skip=true;it.action="skip ("+why+")";}
    }
   }
   string backup=null;int moved=0,skipped=0;
   if(apply&&items.Any(x=>x.move||x.skip))Apply(items,profile,scene,arts,out backup,out moved,out skipped);
   return Write(items,sheets,candidates,routes.Count,profile,temp,scene.path,apply,allowMove,audit,backup,moved,skipped);
  }

  [Serializable] class Move{public string sheet,id,route;public int index;public Vector3 from,to;}
  [Serializable] class Skip{public string sheet,id,route,reason;public int index;}
  [Serializable] class Manifest{public string stamp,scene,profile;public string[] files;public Move[] moves;public Skip[] skips;}

  static void Apply(List<Item> items,NaturalSolidProfileSO profile,Scene scene,CompactRebuildArtRenderer[] arts,out string dir,out int moved,out int skipped)
  {
   var edit=items.Where(x=>x.move).Select(x=>x.sheet).Distinct().ToList();
   foreach(var s in edit)if(EditorUtility.IsDirty(s))throw new Refuse("sheet "+AssetDatabase.GetAssetPath(s)+" has unsaved changes (save or reload it first)");
   if(EditorUtility.IsDirty(profile))throw new Refuse("profile "+AssetDatabase.GetAssetPath(profile)+" has unsaved changes");
   foreach(var x in items.Where(x=>x.move))if(x.prot||x.owned)throw new Refuse("internal: move planned on a protected/owned sheet "+x.path);
   string stamp=DateTime.Now.ToString("yyyyMMdd'T'HHmmss",IC);dir=Path.Combine(Backups,Stem+stamp);for(int k=2;Directory.Exists(dir);k++)dir=Path.Combine(Backups,Stem+stamp+"_"+k);Directory.CreateDirectory(dir);
   var files=new List<string>();
   foreach(var p in edit.Select(s=>AssetDatabase.GetAssetPath(s)).Append(AssetDatabase.GetAssetPath(profile)))
    foreach(var suffix in new[]{"",".meta"})if(File.Exists(p+suffix)){string to=Path.Combine(dir,files.Count+"_"+Path.GetFileName(p)+suffix);File.Copy(p+suffix,to,true);files.Add(p+suffix+" -> "+Path.GetFileName(to));}
   var moves=new List<Move>();var skips=new List<Skip>();
   foreach(var x in items.Where(x=>x.move))
   {
    var fp=x.sheet.FixedPlacements[x.f.Placement];if(fp==null||fp.Id!=x.f.Id)throw new Refuse("placement order changed under "+x.f.Id);
    moves.Add(new Move{sheet=x.path,id=x.f.Id,index=x.f.Placement,from=fp.Position,to=x.to,route=x.route});fp.Position=x.to;x.action="moved: "+x.action.Substring(5);
   }
   var list=(profile.SkipPlacements??Array.Empty<NaturalSolidProfileSO.SkipPlacement>()).ToList();
   foreach(var x in items.Where(x=>x.skip))
   {
    if(list.Any(e=>e!=null&&e.Sheet==x.sheet&&e.PlacementId==x.f.Id))continue;
    string reason=x.action.StartsWith("skip (",StringComparison.Ordinal)?x.action.Substring(6).TrimEnd(')'):x.action;
    list.Add(new NaturalSolidProfileSO.SkipPlacement{Sheet=x.sheet,PlacementId=x.f.Id,Index=x.f.Placement,RouteId=x.route,Reason=reason});
    skips.Add(new Skip{sheet=x.path,id=x.f.Id,index=x.f.Placement,route=x.route,reason=reason});x.action="skipped: "+reason;
   }
   // manifest first: a failed save still leaves the record revert needs
   File.WriteAllText(Path.Combine(dir,"manifest.json"),JsonUtility.ToJson(new Manifest{stamp=stamp,scene=scene.path,profile=AssetDatabase.GetAssetPath(profile),files=files.ToArray(),moves=moves.ToArray(),skips=skips.ToArray()},true));
   profile.SkipPlacements=list.ToArray();
   foreach(var s in edit){EditorUtility.SetDirty(s);AssetDatabase.SaveAssetIfDirty(s);}
   if(skips.Count>0){EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);}
   foreach(var r in arts)if(r!=null)r.Invalidate();
   moved=moves.Count;skipped=skips.Count;
  }

  static string Revert(string name)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Refuse("Edit mode only (Play is running)");
   if(!Directory.Exists(Backups))throw new Refuse("no backups under "+Path.GetFullPath(Backups));
   string dir=name!=null?Path.Combine(Backups,name):Directory.GetDirectories(Backups,Stem+"*").Where(d=>File.Exists(Path.Combine(d,"manifest.json"))&&!File.Exists(Path.Combine(d,"reverted.txt"))).OrderByDescending(d=>d,StringComparer.Ordinal).FirstOrDefault();
   if(dir==null||!File.Exists(Path.Combine(dir,"manifest.json")))throw new Refuse("no un-reverted "+Stem+"* manifest"+(name!=null?" named "+name:""));
   if(File.Exists(Path.Combine(dir,"reverted.txt")))throw new Refuse(Path.GetFileName(dir)+" was already reverted");
   var m=JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(dir,"manifest.json")));
   var sb=new StringBuilder();int back=0,conflict=0,removed=0;var dirty=new HashSet<Object>();
   // an unsaved edit would be saved along with the revert: refuse, as apply does
   foreach(var a in (m.moves??Array.Empty<Move>()).Select(x=>x.sheet).Append(m.profile).Distinct()){var o=string.IsNullOrEmpty(a)?null:AssetDatabase.LoadMainAssetAtPath(a);if(o!=null&&EditorUtility.IsDirty(o))throw new Refuse(a+" has unsaved changes (save or reload it first)");}
   foreach(var mv in m.moves??Array.Empty<Move>())
   {
    var sheet=AssetDatabase.LoadAssetAtPath<Sheet306>(mv.sheet);if(sheet==null){conflict++;sb.AppendLine("missing sheet "+mv.sheet);continue;}
    var fps=sheet.FixedPlacements??Array.Empty<Sheet306.FixedPlacement>();int i=mv.index>=0&&mv.index<fps.Length&&fps[mv.index]?.Id==mv.id?mv.index:Array.FindIndex(fps,x=>x!=null&&x.Id==mv.id);
    if(i<0){conflict++;sb.AppendLine("missing "+mv.id+" in "+mv.sheet);continue;}
    if(Vector3.Distance(fps[i].Position,mv.to)>.01f){conflict++;sb.AppendLine("left "+mv.id+": now "+V(fps[i].Position)+", applied "+V(mv.to)+" (changed since)");continue;}
    fps[i].Position=mv.from;dirty.Add(sheet);back++;
   }
   var profile=AssetDatabase.LoadAssetAtPath<NaturalSolidProfileSO>(m.profile);
   if(profile!=null&&m.skips!=null&&m.skips.Length>0)
   {
    var list=(profile.SkipPlacements??Array.Empty<NaturalSolidProfileSO.SkipPlacement>()).ToList();
    foreach(var sk in m.skips)removed+=list.RemoveAll(e=>e!=null&&e.PlacementId==sk.id&&e.Sheet!=null&&AssetDatabase.GetAssetPath(e.Sheet)==sk.sheet);
    profile.SkipPlacements=list.ToArray();dirty.Add(profile);
   }
   else if(m.skips!=null&&m.skips.Length>0){conflict++;sb.AppendLine("missing profile "+m.profile);}
   foreach(var o in dirty){EditorUtility.SetDirty(o);AssetDatabase.SaveAssetIfDirty(o);}
   foreach(var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None))r.Invalidate();
   File.WriteAllText(Path.Combine(dir,"reverted.txt"),DateTime.Now.ToString("s",IC)+" back "+back+" skips removed "+removed+" conflicts "+conflict+"\n"+sb);
   return "revert "+Path.GetFileName(dir)+": placements back "+back+"/"+(m.moves?.Length??0)+", skip entries removed "+removed+"/"+(m.skips?.Length??0)+", conflicts "+conflict+
          (conflict>0?" (file copies in "+Path.GetFullPath(dir)+")\n"+sb:"")+"; run scan to confirm";
  }

  // ---------- report ----------

  static string V(Vector3 v)=>"("+v.x.ToString("F2",IC)+", "+v.y.ToString("F2",IC)+", "+v.z.ToString("F2",IC)+")";
  static string Write(List<Item> items,List<Sheet306> sheets,int candidates,int routes,NaturalSolidProfileSO profile,bool temp,string scene,bool apply,bool allowMove,HashSet<string> audit,string backup,int moved,int skipped)
  {
   var sb=new StringBuilder();
   sb.AppendLine("# NaturalSolids306 "+(apply?"apply":"scan")+(allowMove?":move":"")+" "+DateTime.Now.ToString("s",IC)+" scene "+scene);
   sb.AppendLine("# sheets: "+string.Join(", ",sheets.Select(s=>AssetDatabase.GetAssetPath(s))));
   sb.AppendLine("# audited 296 copies ("+Path.GetFullPath(Ledger)+"): "+(audit.Count==0?"none":string.Join(", ",audit))+(allowMove?" -> ':move': moved (audit_architecture296_landscape 'retained ... transforms unchanged' then FAILS for them)":" -> outside the corridor keep, overlapping it skip (run with ':move' to move them)"));
   sb.AppendLine("# profile "+(temp?"(defaults: no profile asset)":AssetDatabase.GetAssetPath(profile))+", listed skips "+(profile.SkipPlacements?.Length??0)+"; candidates "+candidates+"; routes "+routes+" (routes.json + content path runs)");
   sb.AppendLine("# gap = collider edge to corridor edge (route width/2; content paths "+PathWidth.ToString("F1",IC)+" m wide), negative = inside the corridor; centre = collider centre to centreline");
   sb.AppendLine("# TEST: band "+Near+" m, clearance "+Clear+" m, max shift "+MaxShift+" m, route dy "+RouteDy+" m, ground dy "+GroundDy+" m, slope n.y>="+Slope+", spacing "+Spacing+" m");
   int on=items.Count(x=>x.gap<=0);sb.AppendLine("# on a route "+on+", within "+Near+" m "+(items.Count-on)+"; protected "+items.Count(x=>x.prot)+", Forest305 "+items.Count(x=>x.owned)+(backup!=null?"; applied: moved "+moved+", skip entries "+skipped+", backup "+Path.GetFullPath(backup):""));
   foreach(var g in items.GroupBy(x=>x.action.Split(' ')[0].TrimEnd(':')).OrderBy(g=>g.Key,StringComparer.Ordinal))sb.AppendLine("#   "+g.Key+" "+g.Count());
   sb.AppendLine("id\tsheet\tprotected\tposition\tgap_m\tcentre_m\troute\taction");
   foreach(var x in items.OrderBy(x=>x.gap))
    sb.AppendLine(x.f.Id+"\t"+Path.GetFileNameWithoutExtension(x.path)+"\t"+(x.prot?"yes":x.owned?"owned":"no")+"\t"+V(x.f.Pivot)+"\t"+x.gap.ToString("F2",IC)+"\t"+x.centre.ToString("F2",IC)+"\t"+x.route+"\t"+x.action);
   Directory.CreateDirectory(Path.GetDirectoryName(Report));File.WriteAllText(Report,sb.ToString());
   int pending=items.Count(x=>x.gap<Clear&&!x.f.Listed&&!(x.owned&&!x.inside)&&!x.action.StartsWith("keep",StringComparison.Ordinal));
   return (apply?"apply":"scan")+(allowMove?":move":"")+" "+scene+": candidates "+candidates+", flagged "+items.Count+" (on route "+on+"), "+
          string.Join(", ",items.GroupBy(x=>x.action.Split(' ')[0].TrimEnd(':')).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>g.Key+" "+g.Count()))+
          (apply?"; moved "+moved+", skip entries "+skipped+(backup!=null?", backup "+Path.GetFullPath(backup):""):"; unresolved "+pending)+"; "+Path.GetFullPath(Report);
  }
 }
}
