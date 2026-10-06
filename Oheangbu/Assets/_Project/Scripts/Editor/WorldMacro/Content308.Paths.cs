using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 // D308-16 D01 / T2 [TEST]: MainPath and BranchPath taken again from the scene's own layout roads.
 // MainPath had drifted off the roads it stands for (rigid moves, re-laid roads); the content pass (rest feet direction), the
 // props corridor check, AC-C12 / AC-C18 and the map's road line all read it, so this runs BEFORE content-apply.
 // Data (content308_relayout.json paths{}): main_legs[] / branch_chain[] = {route, reverse, tail_m}; step_m; max_off_road_m;
 // branch_replace_within_m; join_dedupe_m; group.
 // D308-16 fix F6: a path height is the TERRAIN HEIGHT FIELD of the scene's layout (CompactWorldLayoutSO.FinalSurface), never a
 // physics ray: the ray rule (Ground308) counts every object named *Surface* / *Terrain* as terrain, and the south gate's roof
 // pieces are named Surface_n - the first regeneration put seven MainPath points 20-24 m up on that roof. A built deck the path
 // really runs over is LISTED in data (paths.decks[]: id, x[a,b], z[a,b], window_m, evidence): only inside such a box the walkable
 // support within window_m over the field is used. paths-check judges every point against the field again (max_above_terrain_m,
 // max_below_terrain_m; paths.underground[] lists the runs that are under the field on purpose).
 //   paths-dry:<scene>     the new arrays measured against the layout roads; nothing is written
 //   paths-regen:<scene>   writes the scene's content asset through the content ledger (content308_<scene>): the previous arrays are
 //                         the ledger row's before-value (kind mainpath / branchpath), so content-revert --group <paths group> puts
 //                         them back; a second run writes nothing; a MainPath point farther than max_off_road_m from every layout
 //                         road refuses the write
 //   paths-check:<scene>   reads the assets only (no scene is opened): off-road count, runs, the branch roads
 public static partial class Content308
 {
  static string PathsRegen308(string scenePath,bool dry)
  {
   var d=Relayout308();if(d==null)throw new Refuse308("paths need "+RelayoutFile308+" (paths.main_legs / paths.branch_chain); without it MainPath / BranchPath are left as they are");
   var pd=Req308(d.Data,"paths");
   if(!d.On(pd))throw new Refuse308("paths are off in the relayout data (enabled false, or the group "+GroupKey308(pd)+" is in groups_off)");
   var scene=Open308(scenePath);var session=Session308(scene);var t=Targets308[scene.path];
   var content=Load308<WorldMacroPlaytestSO>(t.content);var layout=Load308<CompactWorldLayoutSO>(t.layout);
   if(session.Content!=content)throw new Refuse308(scene.path+" session Content is "+AssetDatabase.GetAssetPath(session.Content)+", expected "+t.content);
   PrepareGround308(scene,session);
   float step=Num308(pd,"step_m"),maxOff=Num308(pd,"max_off_road_m"),dedupe=Num308(pd,"join_dedupe_m"),within=Num308(pd,"branch_replace_within_m");
   var notes=new List<string>();
   var main=BuildPath308(layout,pd,Arr308(pd,"main_legs"),step,dedupe,notes,"MainPath",out _);
   var chain=BuildPath308(layout,pd,Arr308(pd,"branch_chain"),step,dedupe,notes,"BranchPath chain",out var chainRoads);
   var branch=MergeBranch308(content.BranchPath??Array.Empty<Vector3>(),chain,chainRoads,within,notes);
   var oldMain=content.MainPath??Array.Empty<Vector3>();var oldBranch=content.BranchPath??Array.Empty<Vector3>();
   int offBefore=OffRoad308(oldMain,layout,maxOff,out float worstBefore),offAfter=OffRoad308(main,layout,maxOff,out float worstAfter);
   var sb=new StringBuilder((dry?"paths-dry ":"paths-regen ")+scene.path+"\n");
   sb.AppendLine("  relayout data sha "+Short308(d.Sha)+", layout "+t.layout);
   sb.AppendLine("  MainPath "+oldMain.Length+" points ("+F308(PathLength308(oldMain),"F0")+" m, "+Runs308(oldMain)+" run(s)) -> "+main.Length+" points ("+F308(PathLength308(main),"F0")+" m, "+Runs308(main)+" run(s)), step "+F308(step,"F1")+" m");
   sb.AppendLine("  MainPath points farther than "+F308(maxOff,"F0")+" m from a layout road: "+offBefore+" (worst "+F308(worstBefore,"F1")+" m) -> "+offAfter+" (worst "+F308(worstAfter,"F1")+" m)");
   sb.AppendLine("  BranchPath "+oldBranch.Length+" points ("+Runs308(oldBranch)+" run(s)) -> "+branch.Length+" points ("+Runs308(branch)+" run(s))");
   foreach(var n in notes)sb.AppendLine("  note "+n);
   // F6: the new arrays against the height field (the same numbers paths-check prints)
   if(Has308(pd,"max_above_terrain_m"))
   {
    var surface=Surface308(layout);
    foreach(var (name,arr,old) in new[]{("MainPath",main,oldMain),("BranchPath",branch,oldBranch)})
    {
     var hb=HeightReport308(old,surface,pd);var ha=HeightReport308(arr,surface,pd);
     sb.AppendLine("  "+name+" y - height field: above the limit "+hb.above+" -> "+ha.above+" (worst +"+F308(hb.worstAbove)+" -> +"+F308(ha.worstAbove)+" m), below the limit "+hb.below+" -> "+ha.below+" (worst -"+F308(hb.worstBelow)+" -> -"+F308(ha.worstBelow)+" m; in a listed underground run "+ha.inRun+")");
     if(ha.above>0)throw new Refuse308("the regenerated "+name+" has "+ha.above+" point(s) more than "+F308(Num308(pd,"max_above_terrain_m"))+" m above the height field outside every listed deck (first "+ha.first+"). Nothing written\n"+sb);
    }
   }
   if(offAfter>0)throw new Refuse308("the regenerated MainPath still has "+offAfter+" point(s) farther than "+F308(maxOff,"F0")+" m from every layout road (worst "+F308(worstAfter,"F1")+" m): the leg list and the layout disagree. Nothing written\n"+sb);
   bool sameMain=oldMain.SequenceEqual(main),sameBranch=oldBranch.SequenceEqual(branch);
   string file=Path.Combine(Out308,"paths308_"+Key308(scene.path)+(dry?"-dry":"-last")+".txt");
   if(dry||(sameMain&&sameBranch))
   {
    sb.AppendLine(sameMain&&sameBranch?"  변경 없음 (the arrays already are these; nothing written)":"  would write: "+(sameMain?"":"MainPath ")+(sameBranch?"":"BranchPath"));
    if(scene.isDirty)sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
    Directory.CreateDirectory(Out308);File.WriteAllText(file,sb.ToString());return sb.Append("  report "+file).ToString();
   }
   string key="content308_"+Key308(scene.path);
   var ledger=ReadLedger308(key)??new Ledger308{kind="content",scene=scene.path,created=DateTime.UtcNow.ToString("O")};
   Write308 w=null;
   try
   {
    w=Edit308(content,BackupDir308(),ledger,()=>
    {
     var ch=new List<string>();
     if(!sameMain){ch.Add("MainPath: "+oldMain.Length+" -> "+main.Length+" points from the layout roads (off-road "+offBefore+" -> "+offAfter+")");content.MainPath=main;}
     if(!sameBranch){ch.Add("BranchPath: "+oldBranch.Length+" -> "+branch.Length+" points, runs "+Runs308(oldBranch)+" -> "+Runs308(branch));content.BranchPath=branch;}
     return ch;
    });
   }
   finally{if(w!=null)WriteLedger308(key,ledger);}
   if(w==null)sb.AppendLine("  변경 없음 (nothing written)");
   else
   {
    sb.AppendLine("  wrote "+w.asset+" (backup "+w.backup+")");foreach(var ch in w.changes)sb.AppendLine("    "+ch);
    sb.AppendLine("  ledger "+LedgerFile308(key)+": rows "+string.Join(", ",w.rows.Select(r=>r.kind+(r.group.Length>0?" ["+r.group+"]":"")))+" (the previous arrays are their before-values)");
   }
   Directory.CreateDirectory(Out308);File.WriteAllText(file,sb.ToString());
   return sb.Append("  report "+file).ToString();
  }

  static string PathsCheck308(string scenePath)
  {
   scenePath=Scene308(scenePath);var t=Targets308[scenePath];
   var content=Load308<WorldMacroPlaytestSO>(t.content);var layout=Load308<CompactWorldLayoutSO>(t.layout);
   var d=Relayout308();var pd=d?.Data["paths"];
   float maxOff=pd!=null?Num308(pd,"max_off_road_m"):float.NaN;
   var k=new Check308();var main=content.MainPath??Array.Empty<Vector3>();var branch=content.BranchPath??Array.Empty<Vector3>();
   k.I("content "+t.content+", layout "+t.layout+(d!=null?", relayout data sha "+Short308(d.Sha):", no relayout data"));
   k.I("MainPath "+main.Length+" points, "+F308(PathLength308(main),"F0")+" m, "+Runs308(main)+" run(s); BranchPath "+branch.Length+" points, "+Runs308(branch)+" run(s)");
   if(float.IsNaN(maxOff)){k.I("no paths.max_off_road_m (no relayout data): the off-road count is not judged");return k.Done("checks-paths308_"+Key308(scenePath)+".txt");}
   int off=OffRoad308(main,layout,maxOff,out float worst);
   k.C(off==0,"D01 MainPath points farther than "+F308(maxOff,"F0")+" m from a layout road: "+off+" of "+main.Length+" (worst "+F308(worst,"F1")+" m)");
   foreach(var leg in Arr308(pd,"branch_chain"))
   {
    string id=Str308(leg,"route");var route=Array.Find(layout.Routes??Array.Empty<CompactWorldLayoutSO.Route>(),r=>r!=null&&r.Id==id);
    bool has=route!=null&&route.Bends!=null&&route.Bends.Length>=2&&new[]{route.Bends[0],route.Bends[route.Bends.Length-1]}.All(b=>branch.Any(q=>Vector2.Distance(new Vector2(q.x,q.z),b)<EndMatch308()));
    k.C(has,"D01 BranchPath holds the road "+id+" (both ends)");
   }
   foreach(var leg in Arr308(pd,"main_legs"))
   {
    string id=Str308(leg,"route");var route=Array.Find(layout.Routes??Array.Empty<CompactWorldLayoutSO.Route>(),r=>r!=null&&r.Id==id);
    k.C(route!=null&&route.Bends!=null&&route.Bends.Length>=2,"D01 layout road "+id+" (a MainPath leg) exists");
   }
   // F6: heights against the layout's height field (no scene is opened). A point over the field by more than max_above_terrain_m
   // must lie in a listed deck, a point under it by more than max_below_terrain_m in a listed underground run.
   if(Has308(pd,"max_above_terrain_m"))
   {
    CompactWorldSurface surface=null;
    try{surface=Surface308(layout);}catch(Refuse308 e){k.C(false,"F6 height field of the layout: "+e.Message);}
    if(surface!=null)
    {
     string hf=AssetDatabase.GetAssetPath(layout.FinalSurface);
     k.I("F6 height field "+hf+" sha "+Short308(Harness303.Sha(Harness303.Abs(hf)))+"; decks listed "+Arr308(pd,"decks").Count()+", underground runs listed "+Arr308(pd,"underground").Count());
     foreach(var (name,arr) in new[]{("MainPath",main),("BranchPath",branch)})
     {
      var h=HeightReport308(arr,surface,pd);
      k.C(h.above==0,"F6 "+name+" points more than "+F308(Num308(pd,"max_above_terrain_m"))+" m ABOVE the height field outside the listed decks: "+h.above+" of "+arr.Length+" (worst +"+F308(h.worstAbove)+" m"+(h.above>0?"; first "+h.first:"")+"; on a listed deck "+h.onDeck+")");
      k.C(h.below==0,"F6 "+name+" points more than "+F308(Num308(pd,"max_below_terrain_m"))+" m BELOW the height field outside the listed underground runs: "+h.below+" of "+arr.Length+" (worst -"+F308(h.worstBelow)+" m"+(h.below>0?"; first "+h.firstBelow:"")+"; in a listed run "+h.inRun+")");
     }
    }
   }
   else k.C(false,"F6 the relayout data has no paths.max_above_terrain_m: path heights would not be judged (decision F6: no path point more than 1.0 m above the terrain unless on a listed deck)");
   // F6 (review): a deck / an underground run is an exception with its evidence written down - a row without it is not accepted
   foreach(var (list,what) in new[]{("decks","deck"),("underground","underground run")})
    foreach(var row in Arr308(pd,list))
     k.C(!string.IsNullOrWhiteSpace(Opt308(row,"evidence")),"F6 listed "+what+" "+Opt308(row,"id")+" carries its evidence");
   return k.Done("checks-paths308_"+Key308(scenePath)+".txt");
  }

  // legs in order on the physical ground; a leg's first point is dropped when it repeats the point before it
  static Vector3[] BuildPath308(CompactWorldLayoutSO layout,JToken pd,IEnumerable<JToken> legs,float step,float dedupe,List<string> notes,string what,out List<Vector2[]> roads)
  {
   if(step<=0f)throw new Refuse308("relayout paths.step_m must be > 0");
   roads=new List<Vector2[]>();var xz=new List<Vector2>();var missing=new List<string>();int count=0;
   foreach(var leg in legs)
   {
    string id=Str308(leg,"route");var route=Array.Find(layout.Routes??Array.Empty<CompactWorldLayoutSO.Route>(),r=>r!=null&&r.Id==id);
    if(route==null||route.Bends==null||route.Bends.Length<2){missing.Add(id);continue;}
    var poly=route.Bends.ToArray();if(Flag308(leg,"reverse"))Array.Reverse(poly);
    if(Has308(leg,"tail_m"))poly=Tail308(poly,Num308(leg,"tail_m"));
    roads.Add(poly);count++;int before=xz.Count;
    foreach(var p in Resample308(poly,step)){if(xz.Count>0&&Vector2.Distance(xz[xz.Count-1],p)<dedupe)continue;xz.Add(p);}
    notes.Add(what+" leg "+id+(Flag308(leg,"reverse")?" (reversed)":"")+(Has308(leg,"tail_m")?" (last "+F308(Num308(leg,"tail_m"),"F0")+" m)":"")+": "+F308(Length308(poly),"F0")+" m, "+(xz.Count-before)+" points");
   }
   if(missing.Count>0)throw new Refuse308(what+": the layout "+AssetDatabase.GetAssetPath(layout)+" has no road (with at least 2 bends) "+string.Join(", ",missing)+". Nothing written");
   if(count==0)throw new Refuse308(what+": no legs in the relayout data");
   // F6: y = the layout's height field. Inside a listed deck box the walkable physics support within that deck's window over the
   // field is used instead (and counted); everywhere else physics is not asked at all.
   var surface=Surface308(layout);var o=new List<Vector3>();int onDeck=0;
   foreach(var p in xz)
   {
    float y=surface.Sample(p.x,p.y);var deck=DeckAt308(pd,p);
    if(deck!=null&&Ground308(p.x,p.y,out var g,out _)&&g.y>=y-.05f&&g.y<=y+Num308(deck,"window_m")){y=g.y;onDeck++;}
    o.Add(new Vector3(p.x,y,p.y));
   }
   notes.Add(what+": heights from the height field "+AssetDatabase.GetAssetPath(layout.FinalSurface)+(onDeck>0?"; "+onDeck+" point(s) on a listed deck took the physics support":""));
   return o.ToArray();
  }
  // ---------- F6: the height field and the two lists (decks / underground runs) ----------
  static CompactWorldSurface Surface308(CompactWorldLayoutSO layout)
  {
   if(layout==null||layout.FinalSurface==null)throw new Refuse308("the layout "+(layout!=null?AssetDatabase.GetAssetPath(layout):"(none)")+" has no FinalSurface (height field): path heights cannot be taken");
   try{return new CompactWorldSurface(layout);}
   catch(Exception e){throw new Refuse308("the height field of "+AssetDatabase.GetAssetPath(layout)+" does not load ("+e.Message+")");}
  }
  static bool InBox308(JToken row,Vector2 p)
  {
   var x=row["x"] as JArray;var z=row["z"] as JArray;if(x==null||z==null||x.Count<2||z.Count<2)return false;
   return p.x>=At308(x,0)&&p.x<=At308(x,1)&&p.y>=At308(z,0)&&p.y<=At308(z,1);
  }
  static JToken DeckAt308(JToken pd,Vector2 p)=>Arr308(pd,"decks").FirstOrDefault(d=>InBox308(d,p));
  sealed class HeightReport308C{public int above,below,onDeck,inRun;public float worstAbove,worstBelow;public string first="",firstBelow="";}
  static HeightReport308C HeightReport308(Vector3[] path,CompactWorldSurface surface,JToken pd)
  {
   var r=new HeightReport308C();float maxUp=Num308(pd,"max_above_terrain_m"),maxDown=Num308(pd,"max_below_terrain_m");
   for(int i=0;i<path.Length;i++)
   {
    var q=path[i];var p=new Vector2(q.x,q.z);float d=q.y-surface.Sample(q.x,q.z);
    if(d>maxUp)
    {
     var deck=DeckAt308(pd,p);
     if(deck!=null&&d<=Num308(deck,"window_m")){r.onDeck++;continue;}
     if(d>r.worstAbove)r.worstAbove=d;
     if(r.above++==0)r.first="["+i+"] "+V308(q)+" +"+F308(d)+" m";
    }
    else if(-d>maxDown)
    {
     if(Arr308(pd,"underground").Any(u=>InBox308(u,p))){r.inRun++;continue;}
     if(-d>r.worstBelow)r.worstBelow=-d;
     if(r.below++==0)r.firstBelow="["+i+"] "+V308(q)+" -"+F308(-d)+" m";
    }
    else{if(d>r.worstAbove)r.worstAbove=d;if(-d>r.worstBelow)r.worstBelow=-d;}
   }
   return r;
  }
  // the last `metres` of a polyline (its first point is interpolated)
  static Vector2[] Tail308(Vector2[] poly,float metres)
  {
   var o=new List<Vector2>{poly[poly.Length-1]};float left=metres;
   for(int i=poly.Length-1;i>0&&left>0f;i--)
   {
    float seg=Vector2.Distance(poly[i],poly[i-1]);
    if(seg>=left){o.Add(Vector2.Lerp(poly[i],poly[i-1],seg<1e-5f?0f:left/seg));left=0f;}
    else{o.Add(poly[i-1]);left-=seg;}
   }
   o.Reverse();return o.Count>=2?o.ToArray():poly;
  }
  // BranchPath = the old runs that do not lie on the chain's roads (kept in order), then the chain as one run. A run with a point
  // within `within` metres of a chain road is the earlier #308 run / an earlier chain: it is replaced.
  static Vector3[] MergeBranch308(Vector3[] old,Vector3[] chain,List<Vector2[]> roads,float within,List<string> notes)
  {
   var o=new List<Vector3>();var run=new List<Vector3>();int kept=0,dropped=0;
   void Close()
   {
    if(run.Count==0)return;
    bool onChain=run.Any(p=>roads.Any(poly=>PolyDistance308(new Vector2(p.x,p.z),poly)<=within));
    if(onChain)dropped++;else{o.AddRange(run);kept++;}
    run.Clear();
   }
   for(int i=0;i<old.Length;i++){if(i>0&&Harness303.Flat(old[i-1],old[i])>RunGap308)Close();run.Add(old[i]);}
   Close();
   if(o.Count>0&&chain.Length>0&&Harness303.Flat(o[o.Count-1],chain[0])<=RunGap308)notes.Add("WARN the branch chain starts within "+F308(RunGap308,"F0")+" m of the kept run before it (it merges into that run)");
   o.AddRange(chain);
   notes.Add("BranchPath: "+kept+" old run(s) kept, "+dropped+" replaced by the chain ("+chain.Length+" points)");
   return o.ToArray();
  }
  static float PolyDistance308(Vector2 p,Vector2[] poly)
  {
   float best=float.PositiveInfinity;for(int i=1;i<poly.Length;i++)best=Mathf.Min(best,SegDist308(p,poly[i-1],poly[i]));return best;
  }
  static float PathLength308(Vector3[] path)
  {
   float l=0;for(int i=1;i<path.Length;i++){float d=Harness303.Flat(path[i-1],path[i]);if(d<=RunGap308)l+=d;}return l;
  }
  // number of path points farther than `max` from every layout road centre line (roads with at least 2 bends)
  static int OffRoad308(Vector3[] path,CompactWorldLayoutSO layout,float max,out float worst)
  {
   worst=0f;if(path==null||path.Length==0||layout==null)return 0;
   var roads=(layout.Routes??Array.Empty<CompactWorldLayoutSO.Route>()).Where(r=>r!=null&&r.Bends!=null&&r.Bends.Length>=2).Select(r=>r.Bends).ToArray();
   var boxes=roads.Select(b=>(min:new Vector2(b.Min(v=>v.x),b.Min(v=>v.y)),max:new Vector2(b.Max(v=>v.x),b.Max(v=>v.y)))).ToArray();
   int off=0;
   foreach(var q in path)
   {
    var p=new Vector2(q.x,q.z);float best=float.PositiveInfinity;
    for(int i=0;i<roads.Length;i++)
    {
     // a road whose box is farther than the best so far cannot be nearer
     float bx=Mathf.Max(boxes[i].min.x-p.x,0f,p.x-boxes[i].max.x),bz=Mathf.Max(boxes[i].min.y-p.y,0f,p.y-boxes[i].max.y);
     if(bx*bx+bz*bz>=best*best)continue;
     best=Mathf.Min(best,PolyDistance308(p,roads[i]));
    }
    if(best>max)off++;if(best>worst)worst=best;
   }
   return off;
  }
 }
}
