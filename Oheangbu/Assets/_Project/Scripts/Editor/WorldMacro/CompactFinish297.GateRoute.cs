using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 gate route fix — Art/World/Compact/Rebuild/Finish297/Capital/EXIT2_FIX_PLAN.md (plan), EXIT2_FIX_IMPL.md (commands).
 // A #296 Generated route that passes a capital gate aslant is bent onto the gate normal near that gate only, with the
 // RedirectGateRoute296 curve and three #297 differences: a 30/30 m window, straight-part heights max(sill, ground+.025)
 // per point (a linear lerp lifts capital_exit_2's plane to 74.42 over the 74.00 sill), and the least-lateral crossing.
 // It is always recomputed from the recorded original (idempotent, revertable). Apply rewrites only that routes.json
 // entry (every other byte verified identical), this scene's layout Bends / map line / route geography, and rebuilds that
 // gate's paving as #297-private meshes (A297/GateRoute/Meshes) under the Finish297_GateRoute root; the #296 paving object
 // is recorded and deactivated, A296/Meshes is never written. NavMesh is not baked (on hold): dry/check report the gap.
 //   gate-route:<gate>[|out=30|in=30|k=8|clear=<m>|nav-dy=0.4|allow-dressing][|dry]
 //   gate-route-scene:<scene path>[|gate=<id>][|options as above]
 //   gate-route-revert[:<scene path>][|gate=<id>]
 //   gate-route-check[:<scene path>][|gate=<id>]
 //   gate-route-status
 // Edit mode only (never touches a live/real save), no dialogs, backups under <GateRoute>/Before, targeted saves only
 // (AssetDatabase.SaveAssets is not called, so other sessions' unsaved assets are not flushed).
 public static partial class CompactRebuildAuthoring
 {
  const string GateRouteDir297=K297+"/Capital/GateRoute";
  const string GateRouteMeshes297=A297+"/GateRoute/Meshes";
  const string GateRouteRoot297="Finish297_GateRoute";
  const string GateRouteRoutesKey297="Architecture296/Generated/routes.json";
  const string GateRouteMainScene297="Assets/_Project/Scenes/World/W_Demo_Main.unity";
  static readonly string[] GateRouteScenes297={Scene296,CompactFolklore298.ScenePath,GateRouteMainScene297};
  // never written by any gate-route command (protect_architecture296 / protect_finish297 GUARD + project settings)
  static readonly string[] GateRouteProtected297={"Assets/_Project/Art/World/Watershed295/","Assets/_Project/Art/World/Reworld292/",
   "Assets/_Project/Art/World/MountainTrail285/","Assets/_Project/Scenes/World/W_Demo_Compact.unity","Assets/_Project/Art/World/WorldCompact/Data/03_Content.asset",
   A296+"/Meshes/",A296+"/Shaders/",A296+"/Textures/","ProjectSettings/"};

  [Serializable] class GateRouteAsset297{public string Path,Kind,Route;public bool Present;public Vector2[] Points2;public Vector3[] Points3;}
  [Serializable] class GateRouteOriginal297
  {
   public string Gate,Route,RecordedUtc,RecordedScene,RoutesSha256,PointsHash,Block;public int Count;
   public string AppliedHash,AppliedOptions;
   public GateRouteAsset297[] Assets=Array.Empty<GateRouteAsset297>();
  }
  [Serializable] class GateRouteOriginals297{public string Revision="gate-route-297";public GateRouteOriginal297[] Entries=Array.Empty<GateRouteOriginal297>();}
  [Serializable] class GateRouteSceneRecord297{public string Gate,PavingPath,RecordedUtc;public bool PavingFound,PavingActive;}
  [Serializable] class GateRouteSceneRecords297{public string Scene;public GateRouteSceneRecord297[] Entries=Array.Empty<GateRouteSceneRecord297>();}
  [Serializable] class GateRouteLedgerEntry297
  {
   public string Gate,Route,Utc,Action,Options,PointsHash,FieldHash,Backup,Paving;public bool Applied;
   public int First,Last,OldCount,NewCount,ReplacementCount,Renderers,Colliders;public float PavingTopAtCentre,ThresholdY;
   public string[] Assets=Array.Empty<string>(),Meshes=Array.Empty<string>();
  }
  [Serializable] class GateRouteLedger297{public string Scene;public GateRouteLedgerEntry297[] Entries=Array.Empty<GateRouteLedgerEntry297>();}

  sealed class GateRouteArgs297
  {
   public string Head="",Gate="capital_exit_2";public float Out=30,In=30,K=8,Clear=-1,NavDy=.4f;public bool Dry,AllowDressing;
   public string Options=>"out="+Out.ToString("0.###",CultureInfo.InvariantCulture)+"|in="+In.ToString("0.###",CultureInfo.InvariantCulture)+"|k="+K.ToString("0.###",CultureInfo.InvariantCulture);
   public static GateRouteArgs297 Parse(string argument)
   {
    var a=new GateRouteArgs297();var parts=(argument??"").Split('|');a.Head=parts[0].Trim();
    foreach(var raw in parts.Skip(1))
    {
     string t=raw.Trim();if(t.Length==0)continue;int eq=t.IndexOf('=');string key=eq<0?t:t.Substring(0,eq),value=eq<0?"":t.Substring(eq+1).Trim();
     float Number(){if(!float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out float f))throw new ArgumentException("gate-route: bad number in '"+t+"'");return f;}
     switch(key)
     {
      case "dry":a.Dry=true;break;
      case "allow-dressing":a.AllowDressing=true;break;
      case "gate":if(value.Length==0)throw new ArgumentException("gate-route: empty gate=");a.Gate=value;break;
      case "out":a.Out=Number();break;
      case "in":a.In=Number();break;
      case "k":a.K=Number();break;
      case "clear":a.Clear=Number();break;
      case "nav-dy":a.NavDy=Number();break;
      default:throw new ArgumentException("gate-route: unknown option '"+t+"' (out= in= k= clear= nav-dy= gate= dry allow-dressing)");
     }
    }
    if(a.Out<10||a.Out>80||a.In<10||a.In>80)throw new ArgumentException("gate-route: out/in windows must be 10..80 m (plan default 30/30; 55 m lifts the outer curve 2.9 m off the ground)");
    if(a.K<7||a.K>12)throw new ArgumentException("gate-route: k must be 7..12 m (the straight part covers the 13.4 m deep gatehouse and the wall band)");
    if(a.NavDy<=0)throw new ArgumentException("gate-route: nav-dy must be positive");
    return a;
   }
  }
  sealed class GateRouteSolve297
  {
   public Vector3[] Old,New,Replacement;public int First=-1,Last=-1,Crossing=-1,CrossingsTotal,CrossingsNear,NewCrossingsNear,BandPoints;
   public float CrossingLateral,BandLateral,PlaneY=float.NaN,MaxGrade,OldGrade,MaxAbove,MaxDeviation,MaxCurvature,OldLength,NewLength;
   public Vector3 AboveAt,DeviationAt,CurvatureAt;public bool EndpointsKept;
   public readonly List<string> Refusals=new List<string>();
  }
  sealed class GateRoutePlan297
  {
   public GateRouteArgs297 Args;public Scene Scene;public string Key,RouteId,RoutesText,NewText,NewHash,FieldHash,CurrentHash,CurrentState;
   public float HalfWidth,Clearance;public WorldMacroPlaytestSession Session;public CompactWorldSurface Field;public GateAperture296 Gate;
   public Route292 Current,NewRoute;public GateRouteOriginals297 Originals;public GateRouteOriginal297 Original;public bool RecordOriginal;
   public GateRouteSolve297 Solve;public List<(Object asset,string kind)> Targets=new List<(Object,string)>();
   public List<(string key,GateRouteLedger297 ledger)> Ledgers=new List<(string,GateRouteLedger297)>();
   public readonly List<string> Report=new List<string>(),Refusals=new List<string>(),Warnings=new List<string>();
  }
  struct GateCrossing297{public int Index;public Vector3 At;public float Lateral,CentreDistance;}
  sealed class GateRouteNear297{public string Sheet,Id,Prototype,Category;public Vector3 Position;public float Distance;}

  // ---------- entry points ----------
  static string GateRoute297(string argument)
  {
   var o=GateRouteArgs297.Parse(argument);if(o.Head.Length>0)o.Gate=o.Head;return GateRouteRun297(o);
  }
  static string GateRouteScene297(string argument)
  {
   var o=GateRouteArgs297.Parse(argument);
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("gate-route-scene: Edit mode only");
   if(!GateRouteScenes297.Contains(o.Head))return "refused: gate-route-scene targets only "+string.Join(", ",GateRouteScenes297)+" (got '"+o.Head+"')";
   if(SceneManager.GetActiveScene().path!=o.Head){RequireClean292();EditorSceneManager.OpenScene(o.Head,OpenSceneMode.Single);}
   return GateRouteRun297(o);
  }
  static string GateRouteRun297(GateRouteArgs297 o)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("gate-route: Edit mode only (never runs against a live save)");
   var scene=SceneManager.GetActiveScene();
   if(!GateRouteScenes297.Contains(scene.path))return "refused: gate-route runs only in "+string.Join(", ",GateRouteScenes297)+" (active "+scene.path+")";
   if(!o.Dry)RequireClean292();
   Directory.CreateDirectory(GateRouteDir297);
   var p=PlanGateRoute297(o);
   var lines=new List<string>{"gate-route "+(o.Dry?"DRY ":"")+o.Gate+" route "+(p.RouteId??"?")+" scene "+scene.path+" options "+o.Options+(o.AllowDressing?"|allow-dressing":"")+" "+DateTime.UtcNow.ToString("O")};
   lines.AddRange(p.Report);
   foreach(var w in p.Warnings)lines.Add("WARN "+w);
   foreach(var r in p.Refusals)lines.Add("REFUSED "+r);
   if(p.Refusals.Count>0||o.Dry)
   {
    if(o.Dry)lines.Add(p.Refusals.Count>0?"dry run: apply would be REFUSED (nothing written)":"dry run: apply would pass these checks (nothing written)");
    string text=string.Join("\n",lines);File.WriteAllText(GateRouteDir297+"/"+(o.Dry?"dry-":"refused-")+p.Key+".txt",text);
    return (p.Refusals.Count>0&&!o.Dry?"refused: ":"")+text;
   }
   lines.AddRange(GateRouteApply297(p));
   string result=string.Join("\n",lines);File.WriteAllText(GateRouteDir297+"/apply-"+p.Key+".txt",result);return result;
  }

  // ---------- plan (read-only: every check runs before anything is written) ----------
  static GateRoutePlan297 PlanGateRoute297(GateRouteArgs297 o)
  {
   var p=new GateRoutePlan297{Args=o,Scene=SceneManager.GetActiveScene()};p.Key=GateRouteKey297(p.Scene.path);
   p.Session=Session292();var layout=p.Session.MountainLayout;if(layout==null)throw new InvalidOperationException("gate-route: the session has no MountainLayout");
   p.Field=new CompactWorldSurface(layout);p.FieldHash=GateRouteBytesHash297(layout.FinalSurface!=null?layout.FinalSurface.bytes:null);
   p.Gate=GateRouteAperture297(o.Gate);p.Ledgers=GateRouteLedgers297();
   if(p.Gate.Routes==null||p.Gate.Routes.Length!=1){p.Refusals.Add("gate "+o.Gate+" carries "+(p.Gate.Routes?.Length??0)+" routes ("+string.Join(",",p.Gate.Routes??Array.Empty<string>())+"); only single-route gates are redirected (merged routes were already straightened by #296)");return p;}
   p.RouteId=p.Gate.Routes[0];
   string routesPath=G296+"/routes.json";p.RoutesText=File.ReadAllText(routesPath);var routes=JsonUtility.FromJson<Routes292>(p.RoutesText);
   p.Current=routes?.routes?.FirstOrDefault(r=>r.id==p.RouteId);
   if(p.Current==null){p.Refusals.Add(routesPath+" has no route "+p.RouteId);return p;}
   p.CurrentHash=GateRouteHash297(p.Current.points);
   p.Originals=GateRouteLoad297<GateRouteOriginals297>(GateRouteOriginalFile297);
   p.Original=(p.Originals.Entries??Array.Empty<GateRouteOriginal297>()).FirstOrDefault(e=>e.Gate==o.Gate&&e.Route==p.RouteId);
   if(p.Original==null)
   {
    if(p.Ledgers.Any(l=>(l.ledger.Entries??Array.Empty<GateRouteLedgerEntry297>()).Any(e=>e.Gate==o.Gate)))
    {p.Refusals.Add(GateRouteOriginalFile297+" has no original for "+o.Gate+" although scene ledgers exist: the record was lost; restore it from a Before/ backup");return p;}
    if(GateRouteStraight297(p.Current.points,p.Gate,o.K))
    {p.Refusals.Add("the current "+p.RouteId+" already runs straight through "+o.Gate+" and no original is recorded: refusing to record a straightened line as the original");return p;}
    var span=GateRouteBlock297(p.RoutesText,p.RouteId);
    p.Original=new GateRouteOriginal297{Gate=o.Gate,Route=p.RouteId,RecordedUtc=DateTime.UtcNow.ToString("O"),RecordedScene=p.Scene.path,RoutesSha256=GateRouteFileSha297(routesPath),
     PointsHash=p.CurrentHash,Block=p.RoutesText.Substring(span.start,span.end-span.start),Count=p.Current.points.Length};
    p.RecordOriginal=true;p.CurrentState="original (first run: recorded "+(o.Dry?"on apply":"now")+")";
   }
   else
   {
    var recorded=JsonUtility.FromJson<Route292>(p.Original.Block??"{}");
    if(recorded==null||recorded.id!=p.RouteId||recorded.points==null||GateRouteHash297(recorded.points)!=p.Original.PointsHash)
    {p.Refusals.Add(GateRouteOriginalFile297+" entry for "+o.Gate+" is corrupt (block does not parse back to its hash)");return p;}
    p.CurrentState=p.CurrentHash==p.Original.PointsHash?"original":p.CurrentHash==p.Original.AppliedHash?"gate-route output ("+p.Original.AppliedOptions+")":"other";
    if(p.CurrentState=="other")
    {p.Refusals.Add("routes.json entry "+p.RouteId+" (hash "+p.CurrentHash+") is neither the recorded original ("+p.Original.PointsHash+") nor the last gate-route output ("+(p.Original.AppliedHash??"none")+"): it was changed elsewhere (e.g. #296 gates re-run). Inspect before re-recording.");return p;}
   }
   var original=JsonUtility.FromJson<Route292>(p.Original.Block);
   p.Report.Add("original: "+(p.RecordOriginal?"not yet recorded":"recorded "+p.Original.RecordedUtc+" in "+p.Original.RecordedScene)+"; "+original.points.Length+" points hash "+p.Original.PointsHash+"; current routes.json entry = "+p.CurrentState);
   p.Report.Add("field: FinalSurface sha "+p.FieldHash+" (the three scenes must agree)");
   // compute from the original, never from the current entry
   var s=RedirectGateRoute297(original.points,p.Gate,p.Field,o);p.Solve=s;p.Refusals.AddRange(s.Refusals);
   p.Report.AddRange(GateRouteSolveReport297(s,p.Gate,o));
   if(s.New==null)return p;
   p.NewRoute=new Route292{id=original.id,width=original.width,vehicle=original.vehicle,points=s.New};p.NewHash=GateRouteHash297(s.New);
   // routes.json: only this entry changes; every other byte is verified identical (no whole-file reserialisation)
   var edited=JsonUtility.FromJson<Routes292>(p.RoutesText);var target=edited.routes.First(r=>r.id==p.RouteId);target.points=s.New;target.width=original.width;target.vehicle=original.vehicle;
   p.NewText=GateRouteReplaceBlock297(p.RoutesText,p.RouteId,GateRouteSerializedBlock297(edited,p.RouteId));
   int spliceRefusals=p.Refusals.Count;GateRouteVerifySplice297(p.RoutesText,p.NewText,p.NewRoute,p.Refusals);
   p.Report.Add("routes.json: "+(p.Refusals.Count==spliceRefusals?"every byte outside "+p.RouteId+" identical":"splice verification FAILED")+"; "+(p.NewText==p.RoutesText?"no change (already this output)":p.RoutesText.Length+" -> "+p.NewText.Length+" chars")+"; new points hash "+p.NewHash);
   GateRouteBridge297(p.RouteId,s,p.Refusals,p.Report);
   // dressing on the new section: refuse below paving half-width + .5 m (plan 3.2); the plan expects the nearest at >= 4.5 m
   p.HalfWidth=.5f*Mathf.Min(p.Gate.Width-.8f,Mathf.Max(2.6f,original.width));p.Clearance=o.Clear>0?o.Clear:p.HalfWidth+.5f;
   var near=GateRouteDressing297(s.Replacement,Mathf.Max(12f,p.Clearance+1f));
   var blocking=near.Where(n=>n.Distance<p.Clearance).ToList();
   p.Report.Add("dressing (fixed placements of active art/dressing sheets) near the new section: "+near.Count+" within "+Mathf.Max(12f,p.Clearance+1f).ToString("F1",CultureInfo.InvariantCulture)+" m; clearance limit "+p.Clearance.ToString("F2",CultureInfo.InvariantCulture)+" m (paving half-width "+p.HalfWidth.ToString("F2",CultureInfo.InvariantCulture)+" + .5)");
   foreach(var n in near.Take(6))p.Report.Add("  "+n.Distance.ToString("F2",CultureInfo.InvariantCulture)+" m "+n.Id+" ("+n.Prototype+", "+n.Category+") at "+n.Position.ToString("F2")+" in "+n.Sheet);
   if(near.Count>0&&near[0].Distance<4.5f)p.Warnings.Add("nearest placement "+near[0].Id+" at "+near[0].Distance.ToString("F2",CultureInfo.InvariantCulture)+" m (< 4.5 m expected by the plan: 292_tree_2222_3470 at 4.54 m for 30/30)");
   if(blocking.Count>0)
   {
    string list=string.Join(", ",blocking.Select(n=>n.Id+"@"+n.Distance.ToString("F2",CultureInfo.InvariantCulture)));
    if(o.AllowDressing)p.Warnings.Add("allow-dressing: placements inside the clearance are left in place (no sheet edit): "+list);
    else p.Refusals.Add(blocking.Count+" dressing placements within "+p.Clearance.ToString("F2",CultureInfo.InvariantCulture)+" m of the new section: "+list+" (move/remove them first, widen the window choice, or pass allow-dressing)");
   }
   int grassNew=GateRouteGrass297(s.Replacement,p.HalfWidth,out int grassFields),grassOld=GateRouteGrass297(s.Old.Skip(s.First).Take(s.Last-s.First+1).ToArray(),p.HalfWidth,out _);
   p.Report.Add("grass seeds within the paving half-width: new section "+grassNew+" / old window "+grassOld+" ("+grassFields+" active grass fields; reported only, never cleared here)");
   p.Report.AddRange(GateRouteNavGap297(GateRouteWindow297(s.New,p.Gate.Centre,60),o.NavDy,false,"nav gap, planned new line vs the current NavMesh (+-60 m paving extent)"));
   p.Targets=GateRouteTargets297(p.Session,p.RouteId,p.Refusals,p.Report);
   foreach(var (asset,kind) in p.Targets)
   {
    var now=GateRouteSnapshot297(asset,kind,p.RouteId);var ref2=kind=="layout"?original.points.Skip(1).Take(Math.Max(0,original.points.Length-2)).Select(q=>new Vector2(q.x,q.z)).ToArray():null;
    string matches=kind=="geography"?(GateRouteSame297(now.Points3,original.points)?"= original":GateRouteSame297(now.Points3,s.New)?"= new":"differs from both"):
     kind=="layout"?(GateRouteSame297(now.Points2,ref2)?"= original":GateRouteSame297(now.Points2,s.New.Skip(1).Take(s.New.Length-2).Select(q=>new Vector2(q.x,q.z)).ToArray())?"= new":"differs from both"):
     (GateRouteSame297(now.Points2,original.points.Select(q=>new Vector2(q.x,q.z)).ToArray())?"= original":GateRouteSame297(now.Points2,s.New.Select(q=>new Vector2(q.x,q.z)).ToArray())?"= new":"differs from both");
    p.Report.Add("target "+kind+" "+now.Path+": "+(now.Points2?.Length??now.Points3?.Length??0)+" points ("+matches+")");
    if(matches=="differs from both")p.Warnings.Add(kind+" "+now.Path+" line "+p.RouteId+" differs from the recorded original route; it is replaced by the full new line (as ReplaceBridgeRoute296 does) and restored verbatim on revert");
   }
   // the three scenes share routes.json / map / geography / meshes: their outputs must agree
   foreach(var (key,ledger) in p.Ledgers.Where(l=>l.key!=p.Key))
    foreach(var e in (ledger.Entries??Array.Empty<GateRouteLedgerEntry297>()).Where(e=>e.Gate==o.Gate&&e.Applied))
    {
     if(e.PointsHash==p.NewHash)p.Report.Add("scene "+key+": applied with the same points ("+e.Options+")");
     else if(e.Options==o.Options)p.Refusals.Add("scene "+key+" applied "+e.Options+" with points "+e.PointsHash+" but this scene computes "+p.NewHash+" with the same options: the scenes' FinalSurface differs ("+e.FieldHash+" vs "+p.FieldHash+")");
     else p.Warnings.Add("scene "+key+" still carries paving for "+e.Options+"; re-run gate-route-scene:"+GateRouteScenePathFor297(key)+"|"+o.Options+" so all scenes match");
    }
   p.Report.Add("writes on apply: "+G296+"/routes.json (entry "+p.RouteId+")"+string.Concat(p.Targets.Select(t=>", "+AssetDatabase.GetAssetPath(t.asset)))+", "+GateRouteMeshes297+"/gate296_"+o.Gate+"_approaches_*.asset, "+A296+"/Materials/Crossings (existing paving materials), "+p.Scene.path+" (root "+GateRouteRoot297+", #296 "+o.Gate+"_RouteApproaches deactivated), records in "+GateRouteDir297);
   return p;
  }

  // ---------- apply ----------
  static List<string> GateRouteApply297(GateRoutePlan297 p)
  {
   var o=p.Args;var scene=p.Scene;var lines=new List<string>();string pavingName=p.Gate.Id+"_RouteApproaches";string routesPath=G296+"/routes.json";
   foreach(var (asset,_) in p.Targets)GateRouteWritable297(AssetDatabase.GetAssetPath(asset));
   GateRouteWritable297(scene.path);
   // 1 backups of everything this run may overwrite
   string meshAbs=GateRouteAbs297(GateRouteMeshes297);
   var meshes=Directory.Exists(meshAbs)?Directory.GetFiles(meshAbs,"*.asset").Select(f=>GateRouteMeshes297+"/"+Path.GetFileName(f)).ToArray():Array.Empty<string>();
   string backup=GateRouteBackup297(p.Key,"apply",new[]{scene.path}.Concat(p.Targets.Select(t=>AssetDatabase.GetAssetPath(t.asset))).Concat(meshes).Concat(GateRouteMaterialPaths297()),
    new[]{routesPath,GateRouteOriginalFile297,GateRouteSceneFile297(p.Key),GateRouteLedgerFile297(p.Key)});
   lines.Add("backup "+backup);
   // 2 records before any change: route original (once), each touched asset's original value (once), this scene's #296 paving state (once)
   if(p.RecordOriginal)p.Originals.Entries=(p.Originals.Entries??Array.Empty<GateRouteOriginal297>()).Concat(new[]{p.Original}).ToArray();
   foreach(var (asset,kind) in p.Targets)
   {
    string path=AssetDatabase.GetAssetPath(asset);
    if((p.Original.Assets??Array.Empty<GateRouteAsset297>()).Any(a=>a.Path==path&&a.Route==p.RouteId))continue;
    p.Original.Assets=(p.Original.Assets??Array.Empty<GateRouteAsset297>()).Concat(new[]{GateRouteSnapshot297(asset,kind,p.RouteId)}).ToArray();lines.Add("recorded original "+kind+" "+path);
   }
   GateRouteSave297(GateRouteOriginalFile297,p.Originals);
   var sceneRecords=GateRouteLoad297<GateRouteSceneRecords297>(GateRouteSceneFile297(p.Key));sceneRecords.Scene=scene.path;
   var old296=Root296("Architecture296_Gates/CapitalPerimeter/"+pavingName);
   if(!(sceneRecords.Entries??Array.Empty<GateRouteSceneRecord297>()).Any(e=>e.Gate==p.Gate.Id))
   {
    sceneRecords.Entries=(sceneRecords.Entries??Array.Empty<GateRouteSceneRecord297>()).Concat(new[]{new GateRouteSceneRecord297{Gate=p.Gate.Id,PavingPath="Architecture296_Gates/CapitalPerimeter/"+pavingName,
     PavingFound=old296!=null,PavingActive=old296!=null&&old296.activeSelf,RecordedUtc=DateTime.UtcNow.ToString("O")}}).ToArray();
    GateRouteSave297(GateRouteSceneFile297(p.Key),sceneRecords);
   }
   if(old296==null)lines.Add("WARN #296 paving "+pavingName+" not found in this scene (nothing to deactivate)");
   // paving (scene) -> data assets (memory) -> routes.json (disk) -> targeted asset saves -> scene save; a failure at any
   // stage restores the data values and routes.json text and reopens the saved scene (discarding the unsaved paving)
   int stage=0;GateRouteAsset297[] before=null;Transform holder=null;
   try
   {
    holder=GateRoutePaving297(p,pavingName,old296);stage=1;
    before=p.Targets.Select(t=>GateRouteSnapshot297(t.asset,t.kind,p.RouteId)).ToArray();stage=2;
    foreach(var (asset,kind) in p.Targets)GateRouteAssignNew297(asset,kind,p.RouteId,p.Solve.New);
    if(p.NewText!=p.RoutesText){stage=3;GateRouteWriteText297(routesPath,p.NewText);}
    stage=4;
    foreach(var a in GateRouteSaveSet297(p.Targets.Select(t=>t.asset),holder))AssetDatabase.SaveAssetIfDirty(a);
    stage=5;EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("gate-route: scene save failed "+scene.path);
    stage=6;
   }
   catch(Exception)
   {
    if(stage>=2&&before!=null){for(int i=0;i<p.Targets.Count;i++)GateRouteAssignRecorded297(p.Targets[i].asset,before[i]);foreach(var t in p.Targets)AssetDatabase.SaveAssetIfDirty(t.asset);}
    if(stage>=3)GateRouteWriteText297(routesPath,p.RoutesText);
    // discard the unsaved scene edits (paving root, #296 deactivation); private meshes already rewritten stay backed up in the backup folder
    if(stage<6)EditorSceneManager.OpenScene(scene.path,OpenSceneMode.Single);
    throw;
   }
   // 8 receipts
   float top=GateRoutePavingTop297(holder,p.Gate.Centre);
   var meshPaths=GateRouteMeshAssets297(holder).Select(AssetDatabase.GetAssetPath).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
   int renderers=holder.GetComponentsInChildren<MeshRenderer>(true).Length,colliders=holder.GetComponentsInChildren<MeshCollider>(true).Length;
   lines.Add("paving "+GateRouteRoot297+"/"+pavingName+": renderers "+renderers+" mesh colliders "+colliders+" meshes "+meshPaths.Length+" ("+GateRouteMeshes297+"); #296 "+pavingName+(old296!=null?" deactivated":" absent"));
   lines.Add("paving top at the gate centre "+(float.IsNaN(top)?"none":top.ToString("F3",CultureInfo.InvariantCulture))+" (sill "+p.Gate.Centre.y.ToString("F3",CultureInfo.InvariantCulture)+(float.IsNaN(top)||top-p.Gate.Centre.y>.08f?" — WARN more than .08 m above the sill: the leaves may sweep it":"")+")");
   lines.Add("routes.json "+(p.NewText!=p.RoutesText?"entry "+p.RouteId+" rewritten":"unchanged (already this output)")+"; data assets "+p.Targets.Count+" updated; scene saved (targeted saves; AssetDatabase.SaveAssets not called)");
   p.Original.AppliedHash=p.NewHash;p.Original.AppliedOptions=o.Options;GateRouteSave297(GateRouteOriginalFile297,p.Originals);
   var ledger=GateRouteLoad297<GateRouteLedger297>(GateRouteLedgerFile297(p.Key));ledger.Scene=scene.path;
   var entry=new GateRouteLedgerEntry297{Gate=p.Gate.Id,Route=p.RouteId,Utc=DateTime.UtcNow.ToString("O"),Action="apply",Options=o.Options,PointsHash=p.NewHash,FieldHash=p.FieldHash,Backup=backup,
    Paving=GateRouteRoot297+"/"+pavingName,Applied=true,First=p.Solve.First,Last=p.Solve.Last,OldCount=p.Solve.Old.Length,NewCount=p.Solve.New.Length,ReplacementCount=p.Solve.Replacement.Length,
    Renderers=renderers,Colliders=colliders,PavingTopAtCentre=top,ThresholdY=p.Solve.PlaneY,
    Assets=new[]{GateRouteRoutesKey297}.Concat(p.Targets.Select(t=>AssetDatabase.GetAssetPath(t.asset))).Distinct().ToArray(),Meshes=meshPaths};
   ledger.Entries=(ledger.Entries??Array.Empty<GateRouteLedgerEntry297>()).Where(e=>e.Gate!=p.Gate.Id).Concat(new[]{entry}).ToArray();
   GateRouteSave297(GateRouteLedgerFile297(p.Key),ledger);
   lines.Add("ledger "+GateRouteLedgerFile297(p.Key)+" points "+p.NewHash+" (must match across the three scenes)");
   lines.Add("NavMesh NOT baked (on hold). Next: python Tools/Art/capital297_wall.py, then gate-route-check:"+scene.path+"; the nav gap above is what a later CompactArchitecture296 nav would close.");
   return lines;
  }
  static Transform GateRoutePaving297(GateRoutePlan297 p,string pavingName,GameObject old296)
  {
   var root=p.Scene.GetRootGameObjects().FirstOrDefault(g=>g.name==GateRouteRoot297);
   if(root==null)root=new GameObject(GateRouteRoot297);
   foreach(var stale in root.transform.Cast<Transform>().Where(t=>t.name==pavingName).ToArray())Object.DestroyImmediate(stale.gameObject);
   BuildGateApproaches296(root.transform,p.Gate,new Routes292{routes=new[]{p.NewRoute}},p.Field,60,false,GateRouteMeshes297);
   var holder=root.transform.Find(pavingName);
   if(holder==null)throw new InvalidOperationException("gate-route: the #296 paving builder produced no geometry for "+p.Gate.Id);
   // tripwire: never let a gate-route paving reference (and therefore save) a mesh outside the #297-private folder
   foreach(var mesh in GateRouteMeshAssets297(holder))
   {
    string path=AssetDatabase.GetAssetPath(mesh);
    if(!path.StartsWith(GateRouteMeshes297+"/",StringComparison.Ordinal))throw new InvalidOperationException("gate-route: paving mesh outside "+GateRouteMeshes297+": "+path);
   }
   if(old296!=null)old296.SetActive(false);
   Physics.SyncTransforms();return holder;
  }
  static IEnumerable<Mesh> GateRouteMeshAssets297(Transform holder)=>holder.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh)
   .Concat(holder.GetComponentsInChildren<MeshCollider>(true).Select(c=>c.sharedMesh)).Where(m=>m!=null).Distinct();
  // exactly the assets this command changed: data assets, the paving meshes and the paving materials (all checked writable)
  static List<Object> GateRouteSaveSet297(IEnumerable<Object> data,Transform holder)
  {
   var set=new List<Object>();
   foreach(var a in data.Concat(holder!=null?GateRouteMeshAssets297(holder).Cast<Object>():Enumerable.Empty<Object>())
    .Concat(holder!=null?holder.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Cast<Object>():Enumerable.Empty<Object>()).Distinct())
   {
    string path=AssetDatabase.GetAssetPath(a);if(string.IsNullOrEmpty(path))continue;GateRouteWritable297(path);set.Add(a);
   }
   return set;
  }

  // ---------- revert ----------
  static string GateRouteRevert297(string argument)
  {
   var o=GateRouteArgs297.Parse(argument);
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("gate-route-revert: Edit mode only");
   string scenePath=o.Head.Length>0?o.Head:SceneManager.GetActiveScene().path;
   if(!GateRouteScenes297.Contains(scenePath))return "refused: gate-route-revert targets only "+string.Join(", ",GateRouteScenes297)+" (got '"+scenePath+"')";
   RequireClean292();if(SceneManager.GetActiveScene().path!=scenePath)EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Single);
   var scene=SceneManager.GetActiveScene();string key=GateRouteKey297(scene.path);Directory.CreateDirectory(GateRouteDir297);
   var gate=GateRouteAperture297(o.Gate);if(gate.Routes==null||gate.Routes.Length!=1)return "refused: gate "+o.Gate+" is not a single-route gate";
   string routeId=gate.Routes[0],pavingName=gate.Id+"_RouteApproaches",routesPath=G296+"/routes.json";
   var originals=GateRouteLoad297<GateRouteOriginals297>(GateRouteOriginalFile297);
   var original=(originals.Entries??Array.Empty<GateRouteOriginal297>()).FirstOrDefault(e=>e.Gate==o.Gate&&e.Route==routeId);
   if(original==null)return "refused: no recorded original for "+o.Gate+" in "+GateRouteOriginalFile297+" (nothing was applied)";
   var recordedRoute=JsonUtility.FromJson<Route292>(original.Block);
   var lines=new List<string>{"gate-route-revert "+o.Gate+" route "+routeId+" scene "+scene.path+" "+DateTime.UtcNow.ToString("O")};
   var ledgers=GateRouteLedgers297();var ledger=ledgers.Where(l=>l.key==key).Select(l=>l.ledger).FirstOrDefault()??new GateRouteLedger297{Scene=scene.path};
   var mine=(ledger.Entries??Array.Empty<GateRouteLedgerEntry297>()).FirstOrDefault(e=>e.Gate==o.Gate);
   var others=ledgers.Where(l=>l.key!=key).SelectMany(l=>(l.ledger.Entries??Array.Empty<GateRouteLedgerEntry297>()).Where(e=>e.Gate==o.Gate&&e.Applied).Select(e=>(l.key,e))).ToList();
   // shared data stays on the new line while another scene still carries the new paving (reference counted by ledger)
   var held=new HashSet<string>(others.SelectMany(x=>x.e.Assets??Array.Empty<string>()));
   foreach(var x in others)lines.Add("scene "+x.key+" still applied ("+x.e.Options+"): its shared data is kept");
   var session=Session292();var targetRefusals=new List<string>();var targets=GateRouteTargets297(session,routeId,targetRefusals,lines);
   var restore=targets.Where(t=>!held.Contains(AssetDatabase.GetAssetPath(t.asset))).Select(t=>(t.asset,t.kind,rec:(original.Assets??Array.Empty<GateRouteAsset297>()).FirstOrDefault(a=>a.Path==AssetDatabase.GetAssetPath(t.asset)&&a.Route==routeId))).Where(t=>t.rec!=null).ToList();
   foreach(var t in targets.Where(t=>held.Contains(AssetDatabase.GetAssetPath(t.asset))))lines.Add("kept (shared with an applied scene) "+t.kind+" "+AssetDatabase.GetAssetPath(t.asset));
   foreach(var t in targets.Where(t=>!held.Contains(AssetDatabase.GetAssetPath(t.asset))&&!(original.Assets??Array.Empty<GateRouteAsset297>()).Any(a=>a.Path==AssetDatabase.GetAssetPath(t.asset)&&a.Route==routeId)))
    lines.Add("no recorded original for "+t.kind+" "+AssetDatabase.GetAssetPath(t.asset)+" (never touched by gate-route): left unchanged");
   foreach(var t in restore)GateRouteWritable297(AssetDatabase.GetAssetPath(t.asset));
   // routes.json
   string text=File.ReadAllText(routesPath);var current=JsonUtility.FromJson<Routes292>(text)?.routes?.FirstOrDefault(r=>r.id==routeId);
   string currentHash=current!=null?GateRouteHash297(current.points):"missing";bool restoreRoutes=!held.Contains(GateRouteRoutesKey297);string newText=text;
   if(restoreRoutes)
   {
    if(currentHash!=original.PointsHash&&currentHash!=original.AppliedHash)return "refused: routes.json entry "+routeId+" (hash "+currentHash+") is neither the original nor the gate-route output; nothing reverted";
    if(currentHash!=original.PointsHash)
    {
     newText=GateRouteReplaceBlock297(text,routeId,original.Block);var refusals=new List<string>();GateRouteVerifySplice297(text,newText,recordedRoute,refusals);
     if(refusals.Count>0)return "refused: "+string.Join("; ",refusals);
    }
   }
   else lines.Add("routes.json kept on the new line (another scene is applied)");
   // backups, then scene-local restore -> data -> routes.json -> saves
   string backup=GateRouteBackup297(key,"revert",new[]{scene.path}.Concat(restore.Select(t=>AssetDatabase.GetAssetPath(t.asset))),new[]{routesPath,GateRouteOriginalFile297,GateRouteSceneFile297(key),GateRouteLedgerFile297(key)});
   lines.Add("backup "+backup);
   var sceneRecord=(GateRouteLoad297<GateRouteSceneRecords297>(GateRouteSceneFile297(key)).Entries??Array.Empty<GateRouteSceneRecord297>()).FirstOrDefault(e=>e.Gate==o.Gate);
   int stage=0;GateRouteAsset297[] before=null;
   try
   {
    var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==GateRouteRoot297);int removed=0;
    if(root!=null){foreach(var stale in root.transform.Cast<Transform>().Where(t=>t.name==pavingName).ToArray()){Object.DestroyImmediate(stale.gameObject);removed++;}if(root.transform.childCount==0)Object.DestroyImmediate(root);}
    lines.Add("removed "+removed+" "+GateRouteRoot297+"/"+pavingName);
    var old296=Root296("Architecture296_Gates/CapitalPerimeter/"+pavingName);
    if(old296!=null&&sceneRecord!=null&&sceneRecord.PavingFound){old296.SetActive(sceneRecord.PavingActive);lines.Add("#296 "+pavingName+" activeSelf restored to "+sceneRecord.PavingActive);}
    else if(old296!=null)lines.Add("no scene record for #296 "+pavingName+" (gate-route never deactivated it here): activeSelf "+old296.activeSelf+" left as is");
    Physics.SyncTransforms();stage=1;
    before=restore.Select(t=>GateRouteSnapshot297(t.asset,t.kind,routeId)).ToArray();stage=2;
    foreach(var t in restore){GateRouteAssignRecorded297(t.asset,t.rec);lines.Add("restored "+t.kind+" "+AssetDatabase.GetAssetPath(t.asset));}
    if(newText!=text){stage=3;GateRouteWriteText297(routesPath,newText);lines.Add("routes.json entry "+routeId+" restored byte-for-byte from the recorded original");}
    else if(restoreRoutes)lines.Add("routes.json entry "+routeId+" already the original");
    stage=4;foreach(var t in restore){GateRouteWritable297(AssetDatabase.GetAssetPath(t.asset));AssetDatabase.SaveAssetIfDirty(t.asset);}
    stage=5;EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("gate-route-revert: scene save failed");
    stage=6;
   }
   catch(Exception)
   {
    if(stage>=2&&before!=null){for(int i=0;i<restore.Count;i++)GateRouteAssignRecorded297(restore[i].asset,before[i]);foreach(var t in restore)AssetDatabase.SaveAssetIfDirty(t.asset);}
    if(stage>=3)GateRouteWriteText297(routesPath,text);
    if(stage<6)EditorSceneManager.OpenScene(scene.path,OpenSceneMode.Single);
    throw;
   }
   var entry=mine??new GateRouteLedgerEntry297{Gate=o.Gate,Route=routeId};entry.Applied=false;entry.Action="revert";entry.Utc=DateTime.UtcNow.ToString("O");entry.Backup=backup;
   ledger.Scene=scene.path;ledger.Entries=(ledger.Entries??Array.Empty<GateRouteLedgerEntry297>()).Where(e=>e.Gate!=o.Gate).Concat(new[]{entry}).ToArray();GateRouteSave297(GateRouteLedgerFile297(key),ledger);
   if(others.Count==0){original.AppliedHash=null;original.AppliedOptions=null;GateRouteSave297(GateRouteOriginalFile297,originals);lines.Add("no scene remains applied: shared data back on the original; the original record is kept for the next apply");}
   lines.Add("private meshes in "+GateRouteMeshes297+" are left on disk (unreferenced once every scene is reverted); NavMesh untouched");
   string result=string.Join("\n",lines);File.WriteAllText(GateRouteDir297+"/revert-"+key+".txt",result);return result;
  }

  // ---------- check (never saves; reopens the saved scene if probes left it dirty) ----------
  static string GateRouteCheck297(string argument)
  {
   var o=GateRouteArgs297.Parse(argument);
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("gate-route-check: Edit mode only");
   string scenePath=o.Head.Length>0?o.Head:SceneManager.GetActiveScene().path;
   if(!GateRouteScenes297.Contains(scenePath))return "refused: gate-route-check targets only "+string.Join(", ",GateRouteScenes297)+" (got '"+scenePath+"')";
   RequireClean292();if(SceneManager.GetActiveScene().path!=scenePath)EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Single);
   var scene=SceneManager.GetActiveScene();string key=GateRouteKey297(scene.path);Directory.CreateDirectory(GateRouteDir297);
   var gate=GateRouteAperture297(o.Gate);if(gate.Routes==null||gate.Routes.Length!=1)return "refused: gate "+o.Gate+" is not a single-route gate";
   string routeId=gate.Routes[0];
   // previous outputs are copied before they are overwritten
   var previous=new[]{K297+"/Capital/walk-"+key+".txt",K297+"/Capital/walk-open-"+key+".txt",GateRouteDir297+"/check-"+key+".txt"};
   if(previous.Any(File.Exists))GateRouteBackup297(key,"check",Array.Empty<string>(),previous);
   string text=null;bool reopened=false;
   try{text=GateRouteCheckBody297(scene,key,gate,routeId,o);}
   finally{if(SceneManager.GetActiveScene().isDirty){EditorSceneManager.OpenScene(scene.path,OpenSceneMode.Single);reopened=true;}}
   return text+(reopened?"\nINFO the probes marked the scene dirty; the saved scene was reopened (nothing saved)":"");
  }
  static string GateRouteCheckBody297(Scene scene,string key,GateAperture296 gate,string routeId,GateRouteArgs297 o)
  {
   var lines=new List<string>{"gate-route-check "+o.Gate+" route "+routeId+" scene "+scene.path+" "+DateTime.UtcNow.ToString("O")+" — automated Edit-mode checks, not manual play"};
   lines.AddRange(GateRouteState297(scene,key,gate,routeId));
   lines.Add(GateRouteUnityJsonState297(gate));
   var generated=JsonUtility.FromJson<Routes292>(File.ReadAllText(G296+"/routes.json")).routes.FirstOrDefault(r=>r.id==routeId);
   // (a) saved closed state: *_through is meant to stop at the leaf; a CapitalWall_* contact is the defect
   string closed=WalkCompound297("Capital","walk-"+key+".txt");lines.AddRange(GateRouteWalkSummary297("closed",closed,gate.Id));
   if(generated!=null)lines.AddRange(GateRouteWalkSummary297("closed generated",string.Join("\n",GateRouteWalkGenerated297(gate,generated,"closed")),gate.Id));
   // (b) open leaves (GateProbeScope296) without encounter actors / the resting player body (WalkCave297 rule)
   string stamp=GateStateStamp296();var session=Session292();
   var actors=(session.Actors!=null?session.Actors.Where(a=>a!=null).SelectMany(a=>a.GetComponentsInChildren<Collider>(true)):Enumerable.Empty<Collider>())
    .Concat(session.Walker!=null&&session.Walker.Body!=null?session.Walker.Body.GetComponentsInChildren<Collider>(true):Enumerable.Empty<Collider>()).Where(c=>c!=null&&c.enabled).Distinct().ToArray();
   string open=null;List<string> openGenerated=null;
   foreach(var c in actors)c.enabled=false;Physics.SyncTransforms();
   try
   {
    using(new GateProbeScope296())
    {
     open=WalkCompound297("Capital","walk-open-"+key+".txt");
     if(generated!=null)openGenerated=GateRouteWalkGenerated297(gate,generated,"open");
    }
   }
   finally{foreach(var c in actors)if(c!=null)c.enabled=true;Physics.SyncTransforms();}
   string after=GateStateStamp296();
   lines.Add("encounter actor + resting body colliders disabled for the open walk: "+actors.Length);
   lines.AddRange(GateRouteWalkSummary297("open",open,gate.Id));
   if(openGenerated!=null)lines.AddRange(GateRouteWalkSummary297("open generated",string.Join("\n",openGenerated),gate.Id));
   lines.Add((stamp==after?"PASS":"FAIL")+" gate state stamp identical before/after the open probe ("+stamp+" / "+after+")");
   // (c) NavMesh (bake on hold): the #297 route-nav sampler plus the height gap on the paving
   string nav=RouteNav297(routeId);lines.Add("route-nav "+nav.Split('\n')[0]);
   if(generated!=null)lines.AddRange(GateRouteNavGap297(GateRouteWindow297(generated.points,gate.Centre,60),o.NavDy,true,"nav gap, paving top vs the current NavMesh (+-60 m)"));
   lines.Add("expected after the fix: closed capital_exit_2_through stops at VictoryGate296 (no CapitalWall_* contact), capital_exit_2_outside/inside_to_leaf 4/4 PASS; open *_through PASS; nav-gap samples above nav-dy remain until the (on-hold) NavMesh re-bake");
   string text=string.Join("\n",lines);File.WriteAllText(GateRouteDir297+"/check-"+key+".txt",text);
   return text;
  }
  static string GateRouteStatus297()
  {
   var lines=new List<string>{"gate-route status "+DateTime.UtcNow.ToString("O")};
   var originals=GateRouteLoad297<GateRouteOriginals297>(GateRouteOriginalFile297);string text=File.Exists(G296+"/routes.json")?File.ReadAllText(G296+"/routes.json"):null;
   var routes=text!=null?JsonUtility.FromJson<Routes292>(text):null;
   foreach(var e in originals.Entries??Array.Empty<GateRouteOriginal297>())
   {
    var current=routes?.routes?.FirstOrDefault(r=>r.id==e.Route);string h=current!=null?GateRouteHash297(current.points):"missing";
    lines.Add("original "+e.Gate+"/"+e.Route+" recorded "+e.RecordedUtc+" ("+e.RecordedScene+") points "+e.Count+" hash "+e.PointsHash+"; applied "+(e.AppliedHash??"none")+" "+(e.AppliedOptions??"")+"; routes.json now "+h+" = "+(h==e.PointsHash?"original":h==e.AppliedHash?"gate-route output":"OTHER")+"; recorded assets "+(e.Assets?.Length??0));
   }
   if((originals.Entries?.Length??0)==0)lines.Add("no original recorded (gate-route never applied)");
   foreach(var (key,ledger) in GateRouteLedgers297())foreach(var e in ledger.Entries??Array.Empty<GateRouteLedgerEntry297>())
    lines.Add("scene "+key+": "+e.Gate+" "+(e.Applied?"APPLIED":"reverted")+" "+e.Action+" "+e.Utc+" "+e.Options+" points "+e.PointsHash+" field "+e.FieldHash+" paving top "+e.PavingTopAtCentre.ToString("F3",CultureInfo.InvariantCulture));
   var scene=SceneManager.GetActiveScene();var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==GateRouteRoot297);
   lines.Add("active scene "+scene.path+(scene.isDirty?" (dirty)":"")+": "+GateRouteRoot297+" "+(root!=null?"children "+string.Join(",",root.transform.Cast<Transform>().Select(t=>t.name+(t.gameObject.activeSelf?"":"[inactive]"))):"absent"));
   return string.Join("\n",lines);
  }

  // ---------- route geometry ----------
  static GateRouteSolve297 RedirectGateRoute297(Vector3[] points,GateAperture296 gate,CompactWorldSurface field,GateRouteArgs297 o)
  {
   var s=new GateRouteSolve297{Old=points};
   var inward=Flat297(gate.Inward).normalized;var tangent=Vector3.Cross(Vector3.up,inward);
   float Side(Vector3 p)=>Vector3.Dot(Flat297(p-gate.Centre),inward);
   float Lat(Vector3 p)=>Vector3.Dot(Flat297(p-gate.Centre),tangent);
   var crossings=GatePlaneCrossings297(points,gate);s.CrossingsTotal=crossings.Count;s.CrossingsNear=crossings.Count(c=>c.CentreDistance<=60);
   if(s.CrossingsNear!=1)s.Refusals.Add("the route crosses the "+gate.Id+" plane "+s.CrossingsNear+" times within 60 m of the gate centre (need exactly 1)");
   if(crossings.Count==0){s.Refusals.Add("the route never crosses the "+gate.Id+" plane");return s;}
   // least lateral offset from the gate centre, not the first sign change: a long route may cross the infinite plane elsewhere
   var best=crossings.OrderBy(c=>c.Lateral).First();s.Crossing=best.Index;s.CrossingLateral=best.Lateral;
   if(best.Lateral>gate.Width*.5f){s.Refusals.Add("nearest plane crossing is "+best.Lateral.ToString("F2",CultureInfo.InvariantCulture)+" m beside the gate centre (> half opening "+(gate.Width*.5f).ToString("F2",CultureInfo.InvariantCulture)+")");return s;}
   int crossing=best.Index;bool entering=Side(points[crossing])>Side(points[crossing-1]);
   // out = the outside half (Side < 0), in = the inside half, whatever the route's index direction
   float back=entering?o.Out:o.In,ahead=entering?o.In:o.Out;
   int first=crossing-1,last=crossing;float d=0;
   while(first>0&&d<back){d+=Vector3.Distance(points[first],points[first-1]);first--;}
   d=0;while(last<points.Length-1&&d<ahead){d+=Vector3.Distance(points[last],points[last+1]);last++;}
   var forward=inward*Mathf.Sign(Side(points[last])-Side(points[first]));
   var before=gate.Centre-forward*o.K;var after=gate.Centre+forward*o.K;
   before.y=Mathf.Max(gate.Centre.y,field.Sample(before.x,before.z)+.025f);after.y=Mathf.Max(gate.Centre.y,field.Sample(after.x,after.z)+.025f);
   var rep=new List<Vector3>();
   AppendGateCurve296(rep,points[first],points[Mathf.Min(first+1,points.Length-1)]-points[first],before,forward,field);
   // straight part on the gate normal: each point at max(sill, ground+.025). A linear before->after lerp would lift the
   // gate-plane height (74.42 against capital_exit_2's 74.00 sill) and the closing leaves would sweep raised paving.
   for(int i=1;i<=32;i++){var q=Vector3.Lerp(before,after,i/32f);q.y=Mathf.Max(gate.Centre.y,field.Sample(q.x,q.z)+.025f);rep.Add(q);}
   AppendGateCurve296(rep,after,forward,points[last],points[last]-points[Mathf.Max(0,last-1)],field);
   s.First=first;s.Last=last;s.Replacement=rep.ToArray();s.New=points.Take(first).Concat(rep).Concat(points.Skip(last+1)).ToArray();
   // measurements and refusals (plan 3.2)
   var band=s.Replacement.Where(q=>Mathf.Abs(Side(q))<=o.K+1e-4f&&Mathf.Abs(Lat(q))<=gate.Width).ToArray();s.BandPoints=band.Length;
   s.BandLateral=band.Length>0?band.Max(q=>Mathf.Abs(Lat(q))):float.PositiveInfinity;
   if(band.Length<8||s.BandLateral>.05f)s.Refusals.Add("lateral offset inside |sd| <= "+o.K.ToString("F1",CultureInfo.InvariantCulture)+" m is "+s.BandLateral.ToString("F3",CultureInfo.InvariantCulture)+" m over "+band.Length+" points (limit .05 m over >= 8 points)");
   for(int i=1;i<s.Replacement.Length;i++){float a=Side(s.Replacement[i-1]),b=Side(s.Replacement[i]);if(a*b<=0&&a!=b){s.PlaneY=Mathf.Lerp(s.Replacement[i-1].y,s.Replacement[i].y,a/(a-b));break;}}
   if(float.IsNaN(s.PlaneY)||Mathf.Abs(s.PlaneY-gate.Centre.y)>.05f)s.Refusals.Add("new line height at the gate plane "+(float.IsNaN(s.PlaneY)?"none":s.PlaneY.ToString("F3",CultureInfo.InvariantCulture))+" vs sill "+gate.Centre.y.ToString("F3",CultureInfo.InvariantCulture)+" (limit +-.05 m)");
   s.NewCrossingsNear=GatePlaneCrossings297(s.New,gate).Count(c=>c.CentreDistance<=60);
   if(s.NewCrossingsNear!=1)s.Refusals.Add("the new line crosses the gate plane "+s.NewCrossingsNear+" times within 60 m (need 1)");
   s.EndpointsKept=GateRouteSame297(s.New[0],points[0])&&GateRouteSame297(s.New[s.New.Length-1],points[points.Length-1]);
   if(!s.EndpointsKept)s.Refusals.Add("route endpoints would change (the window reaches an endpoint)");
   float Grade(IList<Vector3> l){float g=0;for(int i=1;i<l.Count;i++){float xz=Flat297(l[i]-l[i-1]).magnitude;if(xz>1e-3f)g=Mathf.Max(g,Mathf.Abs(l[i].y-l[i-1].y)/xz);}return g;}
   s.MaxGrade=Grade(s.Replacement);s.OldGrade=Grade(points.Skip(first).Take(last-first+1).ToArray());
   if(s.MaxGrade>.30f)s.Refusals.Add("max grade of the new section "+s.MaxGrade.ToString("F3",CultureInfo.InvariantCulture)+" > .30");
   s.MaxAbove=float.MinValue;s.MaxDeviation=0;s.MaxCurvature=0;
   foreach(var q in s.Replacement)
   {
    float up=q.y-field.Sample(q.x,q.z);if(up>s.MaxAbove){s.MaxAbove=up;s.AboveAt=q;}
    float dev=GateRouteXZ297(q,points);if(dev>s.MaxDeviation){s.MaxDeviation=dev;s.DeviationAt=q;}
   }
   for(int i=1;i<s.Replacement.Length-1;i++)
   {
    var d1=Flat297(s.Replacement[i]-s.Replacement[i-1]);var d2=Flat297(s.Replacement[i+1]-s.Replacement[i]);if(d1.magnitude<1e-3f||d2.magnitude<1e-3f)continue;
    float perMetre=Vector3.Angle(d1,d2)/((d1.magnitude+d2.magnitude)*.5f);if(perMetre>s.MaxCurvature){s.MaxCurvature=perMetre;s.CurvatureAt=s.Replacement[i];}
   }
   s.OldLength=BridgeLength296(points);s.NewLength=BridgeLength296(s.New);
   return s;
  }
  static IEnumerable<string> GateRouteSolveReport297(GateRouteSolve297 s,GateAperture296 gate,GateRouteArgs297 o)
  {
   string F(float v,int n=2)=>v.ToString("F"+n,CultureInfo.InvariantCulture);
   yield return "gate "+gate.Id+" centre "+gate.Centre.ToString("F2")+" sill "+F(gate.Centre.y,3)+" inward "+gate.Inward.ToString("F3")+" opening "+F(gate.Width)+" m";
   yield return "plane crossings: "+s.CrossingsTotal+" on the whole route, "+s.CrossingsNear+" within 60 m; chosen segment idx "+s.Crossing+" lateral "+F(s.CrossingLateral)+" m";
   if(s.New==null)yield break;
   yield return "window out "+F(o.Out,1)+" / in "+F(o.In,1)+" m, k "+F(o.K,1)+": idx "+s.First+".."+s.Last+" "+s.Old[s.First].ToString("F2")+" -> "+s.Old[s.Last].ToString("F2")+"; "+(s.Last-s.First+1)+" points -> "+s.Replacement.Length+"; route "+s.Old.Length+" -> "+s.New.Length+" points (plan 30/30: idx 299-314, 16 -> 143, 721 -> 848)";
   yield return "length "+F(s.OldLength)+" -> "+F(s.NewLength)+" m ("+(s.NewLength-s.OldLength).ToString("+0.00;-0.00",CultureInfo.InvariantCulture)+"; plan +7.29)";
   yield return "|sd| <= k lateral max "+F(s.BandLateral,4)+" m over "+s.BandPoints+" points (limit .05; plan 0.000)";
   yield return "height at the gate plane "+(float.IsNaN(s.PlaneY)?"none":F(s.PlaneY,3))+" vs sill "+F(gate.Centre.y,3)+" (limit +-.05; plan 74.025 / 74.000)";
   yield return "max grade "+F(s.MaxGrade,3)+" (limit .30; old window "+F(s.OldGrade,3)+"; plan .18)";
   yield return "max above ground "+F(s.MaxAbove)+" m at "+s.AboveAt.ToString("F1")+" (masonry fill under the paving; plan .80)";
   yield return "max deviation from the old line "+F(s.MaxDeviation)+" m at "+s.DeviationAt.ToString("F1")+" (plan 9.86)";
   yield return "max curvature "+F(s.MaxCurvature,1)+" deg/m (radius ~"+(s.MaxCurvature>0?F(57.29578f/s.MaxCurvature,1):"inf")+" m) at "+s.CurvatureAt.ToString("F1")+" (plan 11.9 deg/m ~ r 4.8 m; 30/45 gives 7.0)";
   yield return "endpoints unchanged "+(s.EndpointsKept?"yes":"NO")+"; new line crossings within 60 m "+s.NewCrossingsNear;
  }
  static List<GateCrossing297> GatePlaneCrossings297(Vector3[] points,GateAperture296 gate)
  {
   var inward=Flat297(gate.Inward).normalized;var tangent=Vector3.Cross(Vector3.up,inward);var list=new List<GateCrossing297>();
   for(int i=1;i<points.Length;i++)
   {
    float a=Vector3.Dot(Flat297(points[i-1]-gate.Centre),inward),b=Vector3.Dot(Flat297(points[i]-gate.Centre),inward);
    if(a*b>0||a==b)continue;var at=Vector3.Lerp(points[i-1],points[i],a/(a-b));
    if(list.Count>0&&Flat297(list[list.Count-1].At-at).magnitude<.01f)continue;   // a vertex lying on the plane counts once
    var off=Flat297(at-gate.Centre);list.Add(new GateCrossing297{Index=i,At=at,Lateral=Mathf.Abs(Vector3.Dot(off,tangent)),CentreDistance=off.magnitude});
   }
   return list;
  }
  // true when the points near the gate already lie on its normal (the command then refuses to record them as the original)
  static bool GateRouteStraight297(Vector3[] points,GateAperture296 gate,float k)
  {
   var inward=Flat297(gate.Inward).normalized;var tangent=Vector3.Cross(Vector3.up,inward);int n=0;
   foreach(var p in points)
   {
    var off=Flat297(p-gate.Centre);float lat=Mathf.Abs(Vector3.Dot(off,tangent));
    if(Mathf.Abs(Vector3.Dot(off,inward))>k||lat>gate.Width)continue;n++;if(lat>=.05f)return false;
   }
   return n>=8;
  }
  static Vector3[] GateRouteWindow297(Vector3[] points,Vector3 centre,float metres)
  {
   int near=NearestBridgePoint296(points,centre),first=near,last=near;float d=0;
   while(first>0&&d<metres){d+=Vector3.Distance(points[first],points[first-1]);first--;}
   d=0;while(last<points.Length-1&&d<metres){d+=Vector3.Distance(points[last],points[last+1]);last++;}
   return points.Skip(first).Take(last-first+1).ToArray();
  }
  static void GateRouteBridge297(string routeId,GateRouteSolve297 s,List<string> refusals,List<string> report)
  {
   string path=G296+"/crossings.json";
   if(!File.Exists(path)){report.Add("bridge: no "+path+"; no exported bridge profile to protect");return;}
   var bridges=(JsonUtility.FromJson<Crossings295>(File.ReadAllText(path)).Crossings??Array.Empty<Crossing295>()).Where(c=>c.RouteId==routeId&&c.Points!=null&&c.Points.Length>1).ToArray();
   if(bridges.Length==0){report.Add("bridge: "+routeId+" has no exported crossing");return;}
   foreach(var c in bridges)
   {
    var onBridge=Enumerable.Range(0,s.Old.Length).Where(i=>c.Points.Any(q=>new Vector2(q.x-s.Old[i].x,q.z-s.Old[i].z).sqrMagnitude<.0001f)).ToArray();
    float gap=s.Replacement.Min(q=>GateRouteXZ297(q,c.Points));
    bool overlap=onBridge.Any(i=>i>=s.First&&i<=s.Last)||gap<1f;
    report.Add("bridge "+c.Id+": route idx "+(onBridge.Length>0?onBridge.First()+".."+onBridge.Last():"none")+", new section "+gap.ToString("F1",CultureInfo.InvariantCulture)+" m from the bridge profile"+(overlap?" — OVERLAP":""));
    if(overlap)refusals.Add("the replaced section overlaps the exported bridge profile "+c.Id+" (Generated/crossings.json)");
   }
  }
  static List<GateRouteNear297> GateRouteDressing297(Vector3[] line,float radius)
  {
   var result=new List<GateRouteNear297>();if(line==null||line.Length==0)return result;
   float minX=line.Min(p=>p.x)-radius,maxX=line.Max(p=>p.x)+radius,minZ=line.Min(p=>p.z)-radius,maxZ=line.Max(p=>p.z)+radius;
   var sheets=Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None).Where(r=>r.isActiveAndEnabled&&r.Sheet!=null).Select(r=>r.Sheet)
    .Concat(Object.FindObjectsByType<Oheangbu.App.World.Dressing.WorldMacroDressingRenderer>(FindObjectsSortMode.None).Where(r=>r.isActiveAndEnabled&&r.Sheet!=null).Select(r=>r.Sheet)).Distinct();
   foreach(var sheet in sheets)
   {
    var protos=new Dictionary<string,WorldMacroDressingSheetSO.Prototype>();
    foreach(var proto in sheet.Prototypes??Array.Empty<WorldMacroDressingSheetSO.Prototype>())if(proto!=null&&proto.Id!=null&&!protos.ContainsKey(proto.Id))protos[proto.Id]=proto;
    foreach(var f in sheet.FixedPlacements??Array.Empty<WorldMacroDressingSheetSO.FixedPlacement>())
    {
     if(f==null||f.Position.x<minX||f.Position.x>maxX||f.Position.z<minZ||f.Position.z>maxZ)continue;float d=GateRouteXZ297(f.Position,line);if(d>=radius)continue;
     WorldMacroDressingSheetSO.Prototype pr=null;if(f.PrototypeId!=null)protos.TryGetValue(f.PrototypeId,out pr);
     result.Add(new GateRouteNear297{Sheet=AssetDatabase.GetAssetPath(sheet),Id=f.Id,Prototype=f.PrototypeId,Category=pr!=null?pr.Category+" trunk r "+(pr.Radius*f.Scale).ToString("F2",CultureInfo.InvariantCulture):"?",Position=f.Position,Distance=d});
    }
   }
   return result.OrderBy(n=>n.Distance).ToList();
  }
  static int GateRouteGrass297(Vector3[] line,float halfWidth,out int fields)
  {
   int count=0;fields=0;if(line==null||line.Length==0)return 0;
   var min=new Vector3(line.Min(p=>p.x)-halfWidth,0,line.Min(p=>p.z)-halfWidth);var max=new Vector3(line.Max(p=>p.x)+halfWidth,0,line.Max(p=>p.z)+halfWidth);
   foreach(var g in Object.FindObjectsByType<CompactGrassRenderer266>(FindObjectsSortMode.None))
   {
    if(!g.isActiveAndEnabled||g.Field==null||g.Field.Cells==null)continue;fields++;var a=g.Field.Coordinate(min);var b=g.Field.Coordinate(max);
    for(int z=a.y;z<=b.y;z++)for(int x=a.x;x<=b.x;x++)
    {
     int i=g.Field.Index(x,z);if(i<0||i>=g.Field.Cells.Length||g.Field.Cells[i]?.Seeds==null)continue;
     foreach(var seed in g.Field.Cells[i].Seeds)if(GateRouteXZ297(seed.Position,line)<halfWidth)count++;
    }
   }
   return count;
  }
  // samples every 1.5 m: physical=false compares the planned line height, physical=true the highest walkable collider (paving)
  static List<string> GateRouteNavGap297(Vector3[] line,float dyLimit,bool physical,string label)
  {
   var result=new List<string>();int n=0,noSupport=0,noNav=0,big=0;float worst=0;Vector3 worstAt=default;var rows=new List<(float size,string text)>();
   for(int i=1;i<line.Length;i++)
   {
    int k=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(line[i-1],line[i])/1.5f));
    for(int j=0;j<k;j++)
    {
     var p=Vector3.Lerp(line[i-1],line[i],j/(float)k);n++;var q=p;
     if(physical)
     {
      var hits=Physics.RaycastAll(p+Vector3.up*2.5f,Vector3.down,7,~0,QueryTriggerInteraction.Ignore).Where(h=>h.normal.y>.5f&&!(h.collider is CharacterController)).OrderByDescending(h=>h.point.y).ToArray();
      if(hits.Length==0){noSupport++;continue;}q=hits[0].point;
     }
     if(!NavMesh.SamplePosition(q,out var hit,1.2f,NavMesh.AllAreas)){noNav++;rows.Add((99f,"no NavMesh within 1.2 m at "+q.ToString("F2")));continue;}
     float dy=hit.position.y-q.y;
     if(Mathf.Abs(dy)>dyLimit){big++;rows.Add((Mathf.Abs(dy),"NavMesh "+dy.ToString("+0.00;-0.00",CultureInfo.InvariantCulture)+" m at "+q.ToString("F2")));}
     if(Mathf.Abs(dy)>Mathf.Abs(worst)){worst=dy;worstAt=q;}
    }
   }
   result.Add(label+": samples "+n+", no physical support "+noSupport+", no NavMesh within 1.2 m "+noNav+", |NavMesh - "+(physical?"paving":"planned line")+"| > "+dyLimit.ToString("F2",CultureInfo.InvariantCulture)+" m "+big+", worst "+worst.ToString("+0.00;-0.00",CultureInfo.InvariantCulture)+" m at "+worstAt.ToString("F1"));
   foreach(var r in rows.OrderByDescending(r=>r.size).Take(8))result.Add("  "+r.text);
   if(noNav+big>0)result.Add("  NavMesh bake is on hold: these samples are the gap a later CompactArchitecture296 nav re-bake closes (summon placement rejects NavMesh/ground gaps > .4 m)");
   return result;
  }
  static float GateRoutePavingTop297(Transform holder,Vector3 at)
  {
   float top=float.NaN;if(holder==null)return top;
   foreach(var h in Physics.RaycastAll(at+Vector3.up*4,Vector3.down,10,~0,QueryTriggerInteraction.Ignore))if(h.collider.transform.IsChildOf(holder)&&(float.IsNaN(top)||h.point.y>top))top=h.point.y;
   return top;
  }

  // ---------- check helpers ----------
  static List<string> GateRouteState297(Scene scene,string key,GateAperture296 gate,string routeId)
  {
   var lines=new List<string>();var original=(GateRouteLoad297<GateRouteOriginals297>(GateRouteOriginalFile297).Entries??Array.Empty<GateRouteOriginal297>()).FirstOrDefault(e=>e.Gate==gate.Id&&e.Route==routeId);
   var current=JsonUtility.FromJson<Routes292>(File.ReadAllText(G296+"/routes.json")).routes.FirstOrDefault(r=>r.id==routeId);string h=current!=null?GateRouteHash297(current.points):"missing";
   lines.Add("routes.json "+routeId+" "+h+" = "+(original==null?"(no gate-route record)":h==original.PointsHash?"ORIGINAL (not fixed)":h==original.AppliedHash?"gate-route output "+original.AppliedOptions:"OTHER"));
   var ledger=(GateRouteLoad297<GateRouteLedger297>(GateRouteLedgerFile297(key)).Entries??Array.Empty<GateRouteLedgerEntry297>()).FirstOrDefault(e=>e.Gate==gate.Id);
   lines.Add("ledger "+(ledger==null?"none":(ledger.Applied?"applied ":"reverted ")+ledger.Utc+" "+ledger.Options+" points "+ledger.PointsHash+(ledger.Applied&&ledger.PointsHash!=h?" — MISMATCH with routes.json":"")));
   var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==GateRouteRoot297);var holder=root!=null?root.transform.Find(gate.Id+"_RouteApproaches"):null;
   var old296=Root296("Architecture296_Gates/CapitalPerimeter/"+gate.Id+"_RouteApproaches");
   lines.Add("paving: "+GateRouteRoot297+"/"+gate.Id+"_RouteApproaches "+(holder==null?"absent":holder.gameObject.activeInHierarchy?"active":"inactive")+"; #296 "+gate.Id+"_RouteApproaches "+(old296==null?"absent":old296.activeSelf?"ACTIVE":"inactive")+
    (holder!=null&&old296!=null&&old296.activeSelf?" — WARN both pavings active":""));
   if(holder!=null){float top=GateRoutePavingTop297(holder,gate.Centre);lines.Add("paving top at the gate centre "+(float.IsNaN(top)?"none":top.ToString("F3",CultureInfo.InvariantCulture))+" (sill "+gate.Centre.y.ToString("F3",CultureInfo.InvariantCulture)+")");}
   return lines;
  }
  // Capital/unity.json routes come from Tools/Art/capital297_wall.py; flag them when they still cross the gate aslant
  static string GateRouteUnityJsonState297(GateAperture296 gate)
  {
   KitCompound297 data;try{data=CompoundData297("Capital");}catch(Exception e){return "WARN Capital/unity.json unreadable: "+e.Message;}
   var r=(data.routes??Array.Empty<KitRoute297>()).FirstOrDefault(x=>x.id==gate.Id+"_through");if(r==null||r.points==null||r.points.Length<6)return "WARN Capital/unity.json has no "+gate.Id+"_through route";
   var pts=Enumerable.Range(0,r.points.Length/3).Select(i=>V297(r.points,3*i)).ToArray();var crossing=GatePlaneCrossings297(pts,gate).OrderBy(c=>c.Lateral).FirstOrDefault();
   if(crossing.Index<=0)return "WARN Capital/unity.json "+gate.Id+"_through never crosses the gate plane";
   float angle=Vector3.Angle(Flat297(pts[crossing.Index]-pts[crossing.Index-1]),Flat297(gate.Inward));angle=Mathf.Min(angle,180-angle);
   return (angle>5||crossing.Lateral>.3f?"WARN ":"")+"Capital/unity.json "+gate.Id+"_through crosses the gate at "+angle.ToString("F1",CultureInfo.InvariantCulture)+" deg, lateral "+crossing.Lateral.ToString("F2",CultureInfo.InvariantCulture)+" m"+
    (angle>5||crossing.Lateral>.3f?" — predates the fix: run python Tools/Art/capital297_wall.py before trusting walk results":"");
  }
  static List<string> GateRouteWalkGenerated297(GateAperture296 gate,Route292 route,string label)
  {
   var window=GateRouteWindow297(route.points,gate.Centre,40);if(window.Length<2)return new List<string>{label+" generated "+route.id+": window too short"};
   var dense=new List<Vector3>();for(int i=1;i<window.Length;i++){int k=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(window[i-1],window[i])));for(int j=0;j<k;j++)dense.Add(Vector3.Lerp(window[i-1],window[i],j/(float)k));}dense.Add(window[window.Length-1]);
   for(int i=0;i<dense.Count;i++){var hits=Physics.RaycastAll(dense[i]+Vector3.up*1.2f,Vector3.down,4.2f,~0,QueryTriggerInteraction.Ignore).Where(h=>h.normal.y>.5f&&!(h.collider is CharacterController)).OrderByDescending(h=>h.point.y).ToArray();if(hits.Length>0)dense[i]=hits[0].point;}
   var path=dense.ToArray();
   return WalkPaths297(new[]{(route.id+"@"+gate.Id+" +-40m forward",path),(route.id+"@"+gate.Id+" +-40m reverse",path.Reverse().ToArray())});
  }
  static IEnumerable<string> GateRouteWalkSummary297(string label,string text,string gateId)
  {
   var lines=(text??"").Split('\n');
   yield return label+" walk: PASS "+lines.Count(l=>l.Contains(": PASS"))+" FAIL "+lines.Count(l=>l.Contains(": FAIL"));
   foreach(var l in lines.Where(l=>l.Contains(": FAIL")&&l.Contains("CapitalWall_")))yield return "FAIL wall contact ("+label+"): "+l;
   foreach(var l in lines.Where(l=>(l.StartsWith(gateId)||label.Contains("generated")&&l.Contains("@"))&&l.Contains(": ")))
    yield return "  "+l+(l.Contains(": FAIL")&&l.Contains("VictoryGate")&&label.StartsWith("closed")?"  [stops at the closed leaf: intended]":"");
  }

  // ---------- data assets ----------
  static List<(Object asset,string kind)> GateRouteTargets297(WorldMacroPlaytestSession session,string routeId,List<string> refusals,List<string> report)
  {
   var list=new List<(Object,string)>();var layout=session.MountainLayout;
   if(layout!=null&&layout.Routes!=null&&layout.Routes.Any(r=>r!=null&&r.Id==routeId))list.Add((layout,"layout"));
   else refusals.Add(routeId+" is not a route of "+AssetDatabase.GetAssetPath(layout)+" (mountain-bound routes are out of scope)");
   foreach(var map in session.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Select(u=>u.MapData).Where(m=>m!=null).Distinct())
    if(map.Lines!=null&&map.Lines.Any(l=>l!=null&&l.Id==routeId))list.Add((map,"map"));
    else report.Add("map "+AssetDatabase.GetAssetPath(map)+": no line "+routeId+" (left unchanged)");
   var geography=session.DemoEscortSummon!=null?session.DemoEscortSummon.WorldSheet:null;
   if(geography!=null&&geography.Routes!=null&&geography.Routes.Any(r=>r!=null&&r.Id==routeId))list.Add((geography,"geography"));
   else report.Add("route geography: "+(geography==null?"none bound":AssetDatabase.GetAssetPath(geography)+" has no "+routeId)+" (left unchanged)");
   foreach(var (asset,_) in list){try{GateRouteWritable297(AssetDatabase.GetAssetPath(asset));}catch(Exception e){refusals.Add(e.Message);}}
   return list;
  }
  static GateRouteAsset297 GateRouteSnapshot297(Object asset,string kind,string routeId)
  {
   var r=new GateRouteAsset297{Path=AssetDatabase.GetAssetPath(asset),Kind=kind,Route=routeId};
   switch(asset)
   {
    case CompactWorldLayoutSO layout:{var x=layout.Routes.FirstOrDefault(q=>q!=null&&q.Id==routeId);r.Present=x!=null;r.Points2=x?.Bends!=null?(Vector2[])x.Bends.Clone():Array.Empty<Vector2>();break;}
    case WorldMapBakedDataSO map:{var x=map.Lines.FirstOrDefault(q=>q!=null&&q.Id==routeId);r.Present=x!=null;r.Points2=x?.Points!=null?(Vector2[])x.Points.Clone():Array.Empty<Vector2>();break;}
    case WorldMacroSheetSO sheet:{var x=sheet.Routes.FirstOrDefault(q=>q!=null&&q.Id==routeId);r.Present=x!=null;r.Points3=x?.Points!=null?(Vector3[])x.Points.Clone():Array.Empty<Vector3>();break;}
    default:throw new InvalidOperationException("gate-route: unsupported data asset "+r.Path);
   }
   return r;
  }
  // same propagation as the non-vehicle branch of ReplaceBridgeRoute296, without its G295 splice and A296-only guards
  static void GateRouteAssignNew297(Object asset,string kind,string routeId,Vector3[] points)
  {
   var xz=points.Select(p=>new Vector2(p.x,p.z)).ToArray();
   GateRouteAssign297(asset,routeId,kind=="layout"?xz.Skip(1).Take(Math.Max(0,xz.Length-2)).ToArray():xz,(Vector3[])points.Clone());
  }
  static void GateRouteAssignRecorded297(Object asset,GateRouteAsset297 record)
  {
   if(record==null||!record.Present)return;GateRouteAssign297(asset,record.Route,record.Points2!=null?(Vector2[])record.Points2.Clone():null,record.Points3!=null?(Vector3[])record.Points3.Clone():null);
  }
  static void GateRouteAssign297(Object asset,string routeId,Vector2[] points2,Vector3[] points3)
  {
   switch(asset)
   {
    case CompactWorldLayoutSO layout:{var x=layout.Routes.First(q=>q!=null&&q.Id==routeId);x.Bends=points2??Array.Empty<Vector2>();break;}
    case WorldMapBakedDataSO map:{var x=map.Lines.First(q=>q!=null&&q.Id==routeId);x.Points=points2??Array.Empty<Vector2>();break;}
    case WorldMacroSheetSO sheet:{var x=sheet.Routes.First(q=>q!=null&&q.Id==routeId);x.Points=points3??Array.Empty<Vector3>();break;}
    default:throw new InvalidOperationException("gate-route: unsupported data asset "+AssetDatabase.GetAssetPath(asset));
   }
   EditorUtility.SetDirty(asset);
  }

  // ---------- routes.json splice ----------
  // top-level objects of the "routes" array, as [start,end) character spans (strings honoured)
  static List<(int start,int end)> GateRouteBlocks297(string text)
  {
   int key=text.IndexOf("\"routes\"",StringComparison.Ordinal);if(key<0)throw new InvalidOperationException("gate-route: no \"routes\" array");
   int open=text.IndexOf('[',key);if(open<0)throw new InvalidOperationException("gate-route: malformed routes array");
   var blocks=new List<(int,int)>();int depth=0,start=-1;bool inString=false,escape=false;
   for(int i=open+1;i<text.Length;i++)
   {
    char c=text[i];
    if(inString){if(escape)escape=false;else if(c=='\\')escape=true;else if(c=='"')inString=false;continue;}
    if(c=='"'){inString=true;continue;}
    if(c=='{'||c=='['){if(depth==0&&c=='{')start=i;depth++;}
    else if(c=='}'||c==']'){if(depth==0)break;depth--;if(depth==0&&c=='}')blocks.Add((start,i+1));}
   }
   return blocks;
  }
  static string GateRouteBlockId297(string text,(int start,int end) block)=>JsonUtility.FromJson<Route292>(text.Substring(block.start,block.end-block.start))?.id;
  static (int start,int end) GateRouteBlock297(string text,string routeId)
  {
   var hits=GateRouteBlocks297(text).Where(b=>GateRouteBlockId297(text,b)==routeId).ToArray();
   if(hits.Length!=1)throw new InvalidOperationException("gate-route: routes.json holds "+hits.Length+" entries for "+routeId);return hits[0];
  }
  static string GateRouteReplaceBlock297(string text,string routeId,string block){var span=GateRouteBlock297(text,routeId);return text.Substring(0,span.start)+block+text.Substring(span.end);}
  // the entry as JsonUtility writes it inside the whole file (same indentation and number format as #296 Gates296/Crossings296)
  static string GateRouteSerializedBlock297(Routes292 routes,string routeId){string full=JsonUtility.ToJson(routes,true);var span=GateRouteBlock297(full,routeId);return full.Substring(span.start,span.end-span.start);}
  static void GateRouteVerifySplice297(string oldText,string newText,Route292 expected,List<string> refusals)
  {
   var a=GateRouteBlocks297(oldText);var b=GateRouteBlocks297(newText);
   if(a.Count!=b.Count){refusals.Add("routes.json entry count would change "+a.Count+" -> "+b.Count);return;}
   int ia=a.FindIndex(x=>GateRouteBlockId297(oldText,x)==expected.id),ib=b.FindIndex(x=>GateRouteBlockId297(newText,x)==expected.id);
   if(ia<0||ia!=ib){refusals.Add("routes.json entry "+expected.id+" would move");return;}
   if(!string.Equals(oldText.Remove(a[ia].start,a[ia].end-a[ia].start),newText.Remove(b[ib].start,b[ib].end-b[ib].start),StringComparison.Ordinal))
    refusals.Add("routes.json bytes outside "+expected.id+" would change");
   var parsed=JsonUtility.FromJson<Route292>(newText.Substring(b[ib].start,b[ib].end-b[ib].start));
   if(parsed==null||parsed.id!=expected.id||parsed.width!=expected.width||parsed.vehicle!=expected.vehicle||!GateRouteSame297(parsed.points,expected.points))
    refusals.Add("the rewritten entry "+expected.id+" does not parse back to the computed points");
   var all=JsonUtility.FromJson<Routes292>(newText);if(all?.routes==null||all.routes.Length!=a.Count)refusals.Add("the rewritten routes.json does not parse");
  }

  // ---------- small helpers ----------
  static Vector3 Flat297(Vector3 v){v.y=0;return v;}
  static float GateRouteXZ297(Vector3 p,Vector3[] line)
  {
   var q=new Vector2(p.x,p.z);if(line.Length==1)return Vector2.Distance(q,new Vector2(line[0].x,line[0].z));float best=float.MaxValue;
   for(int i=1;i<line.Length;i++){var a=new Vector2(line[i-1].x,line[i-1].z);var d=new Vector2(line[i].x,line[i].z)-a;float t=Mathf.Clamp01(Vector2.Dot(q-a,d)/Mathf.Max(1e-9f,d.sqrMagnitude));best=Mathf.Min(best,(a+d*t-q).sqrMagnitude);}
   return Mathf.Sqrt(best);
  }
  static bool GateRouteSame297(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
  static bool GateRouteSame297(Vector3[] a,Vector3[] b)
  {
   if(a==null||b==null||a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(!GateRouteSame297(a[i],b[i]))return false;return true;
  }
  static bool GateRouteSame297(Vector2[] a,Vector2[] b)
  {
   if(a==null||b==null||a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i].x!=b[i].x||a[i].y!=b[i].y)return false;return true;
  }
  static string GateRouteHex297(byte[] hash)=>BitConverter.ToString(hash).Replace("-","").ToLowerInvariant();
  static string GateRouteHash297(Vector3[] points)
  {
   if(points==null)return "null";var bytes=new byte[points.Length*12];
   for(int i=0;i<points.Length;i++){Buffer.BlockCopy(BitConverter.GetBytes(points[i].x),0,bytes,i*12,4);Buffer.BlockCopy(BitConverter.GetBytes(points[i].y),0,bytes,i*12+4,4);Buffer.BlockCopy(BitConverter.GetBytes(points[i].z),0,bytes,i*12+8,4);}
   using var sha=SHA256.Create();return GateRouteHex297(sha.ComputeHash(bytes)).Substring(0,16);
  }
  static string GateRouteBytesHash297(byte[] bytes){if(bytes==null)return "none";using var sha=SHA256.Create();return GateRouteHex297(sha.ComputeHash(bytes)).Substring(0,16);}
  static string GateRouteFileSha297(string path){using var sha=SHA256.Create();using var stream=File.OpenRead(path);return GateRouteHex297(sha.ComputeHash(stream));}
  static string GateRouteKey297(string scenePath)=>Path.GetFileNameWithoutExtension(scenePath);
  static string GateRouteScenePathFor297(string key)=>GateRouteScenes297.FirstOrDefault(s=>GateRouteKey297(s)==key)??key;
  static string GateRouteAbs297(string assetPath)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",assetPath));
  static string GateRouteOriginalFile297=>GateRouteDir297+"/original-routes.json";
  static string GateRouteSceneFile297(string key)=>GateRouteDir297+"/original-"+key+".json";
  static string GateRouteLedgerFile297(string key)=>GateRouteDir297+"/gate-route-"+key+".json";
  static T GateRouteLoad297<T>(string file) where T:class,new()=>File.Exists(file)?(JsonUtility.FromJson<T>(File.ReadAllText(file))??new T()):new T();
  static void GateRouteSave297(string file,object value){Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,JsonUtility.ToJson(value,true));}
  static void GateRouteWriteText297(string file,string text)
  {
   // same encoding as File.WriteAllText in Gates296/Crossings296 (UTF-8 without BOM); write-then-replace
   string temp=file+".gate-route.tmp";File.WriteAllText(temp,text,new UTF8Encoding(false));File.Copy(temp,file,true);File.Delete(temp);
  }
  static List<(string key,GateRouteLedger297 ledger)> GateRouteLedgers297()
  {
   var list=new List<(string,GateRouteLedger297)>();if(!Directory.Exists(GateRouteDir297))return list;
   foreach(var f in Directory.GetFiles(GateRouteDir297,"gate-route-*.json").OrderBy(x=>x,StringComparer.Ordinal))
   {
    string name=Path.GetFileNameWithoutExtension(f);list.Add((name.Substring("gate-route-".Length),GateRouteLoad297<GateRouteLedger297>(f)));
   }
   return list;
  }
  static GateAperture296 GateRouteAperture297(string gateId)
  {
   var receipt=JsonUtility.FromJson<GateReceipt296>(File.ReadAllText(O296+"/gates.json"));
   var loop=(receipt.Loops??Array.Empty<GateLoop296>()).FirstOrDefault(l=>l.Id=="capital296")??throw new InvalidOperationException("gate-route: capital296 loop missing in "+O296+"/gates.json");
   return (loop.Gates??Array.Empty<GateAperture296>()).FirstOrDefault(g=>g.Id==gateId)??throw new ArgumentException("gate-route: no capital296 gate "+gateId+" (have "+string.Join(",",(loop.Gates??Array.Empty<GateAperture296>()).Select(g=>g.Id))+")");
  }
  static void GateRouteWritable297(string assetPath)
  {
   if(string.IsNullOrEmpty(assetPath))throw new InvalidOperationException("gate-route: refusing to write an asset without a path");
   string p=assetPath.Replace('\\','/');
   foreach(var guard in GateRouteProtected297)
    if(p.StartsWith(guard,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("gate-route: refusing to write protected "+p);
  }
  // the private #296 crossing materials the paving builder refreshes (CrossingMaterial296 of the stone floor and rampart sources)
  static IEnumerable<string> GateRouteMaterialPaths297()
  {
   foreach(var source in new[]{StoneFloor296,Rampart296}.SelectMany(path=>CrossingSource(path).Parts.Select(x=>x.material)).Distinct())
    if(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long localId))yield return A296+"/Materials/Crossings/"+guid+"_"+localId+".mat";
  }
  // copies each existing asset (+ .meta) under its Assets/ path and each file flat into Before/<scene>-<label>-<stamp>/
  static string GateRouteBackup297(string key,string label,IEnumerable<string> assetPaths,IEnumerable<string> files)
  {
   string dir=GateRouteDir297+"/Before/"+key+"-"+label+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture);Directory.CreateDirectory(dir);var manifest=new List<string>();
   foreach(var asset in assetPaths.Where(x=>!string.IsNullOrEmpty(x)).Distinct())
    foreach(var suffix in new[]{"",".meta"})
    {
     string source=GateRouteAbs297(asset+suffix);if(!File.Exists(source))continue;string dest=Path.Combine(dir,asset+suffix);Directory.CreateDirectory(Path.GetDirectoryName(dest));
     File.Copy(source,dest,true);manifest.Add(asset+suffix+" -> "+dest);
    }
   foreach(var file in files.Where(x=>!string.IsNullOrEmpty(x)).Distinct())
   {
    if(!File.Exists(file))continue;string dest=Path.Combine(dir,"files",Path.GetFileName(file));Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(file,dest,true);manifest.Add(file+" -> "+dest);
   }
   File.WriteAllLines(Path.Combine(dir,"manifest.txt"),manifest);return dir;
  }
 }
}
