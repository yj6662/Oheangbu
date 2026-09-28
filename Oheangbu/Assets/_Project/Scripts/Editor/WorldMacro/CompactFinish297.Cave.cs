using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 opening cave (SPEC-WORLD-FINISH-297 §2): the V4 mine sat ~70 m under the #292 terrain, so a new game started in a
 // cave with no way out. One rigid transform (Finish297/Cave/relocation.json, searched against the #295 height field) moves
 // the whole `mine` hierarchy and every datum inside the old cave (start, checkpoint, clue points, encounters/actors, cave
 // part of the main path, map zone). The main path outside the cave now runs from the new mouth along the #296 inn route.
 // Originals are recorded as JSON once and restored by cave-revert. The old buried exterior shell is retired (inactive).
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] class CaveRelocation297{public float[] pivot,sceneTranslation,geometryTranslation,mouth,start,outsidePath;public float unityYaw;public CaveBounds297 caveBounds;}
  [Serializable] class CaveBounds297{public float[] min,max;}
  [Serializable] class CaveOriginal297{public Vector3 minePosition;public Quaternion mineRotation;public string content,map,sheet,locations;public Vector3 body;public Quaternion bodyRotation;
   public string[] actorIds;public Vector3[] actorPositions;public string[] retired;public bool[] retiredActive;public string[] removedPlacements;}

  // cave-dry — what the relocation would touch: data asset paths, data counts inside the old cave, and scene objects there outside `mine`
  static string CaveDry297()
  {
   var scene=SceneManager.GetActiveScene();var session=Session292();var content=session.Content;var roots=scene.GetRootGameObjects();var mine=roots.Single(g=>g.name=="mine");
   var ui=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();
   var rel=JsonUtility.FromJson<CaveRelocation297>(File.ReadAllText(K297+"/Cave/relocation.json"));var lo=V297(rel.caveBounds.min);var hi=V297(rel.caveBounds.max);
   bool Inside(Vector3 p)=>p.x>=lo.x&&p.x<=hi.x&&p.y>=lo.y&&p.y<=hi.y&&p.z>=lo.z&&p.z<=hi.z;
   var sb=new System.Text.StringBuilder();
   sb.AppendLine("content="+AssetDatabase.GetAssetPath(content)+" map="+AssetDatabase.GetAssetPath(ui.MapData)+" sheet="+AssetDatabase.GetAssetPath(ui.WorldSheet)+" scene="+scene.path);
   sb.AppendLine("points in="+content.Points.Count(p=>Inside(p.Position))+" ["+string.Join(",",content.Points.Where(p=>Inside(p.Position)).Select(p=>p.Id))+"]");
   sb.AppendLine("encounters in="+content.Encounters.Count(e=>Inside(e.Feet))+" checkpoints in="+content.Checkpoints.Count(c=>Inside(c.Feet))+" ["+string.Join(",",content.Checkpoints.Where(c=>Inside(c.Feet)).Select(c=>c.Id))+"]");
   sb.AppendLine("start="+content.StartFeet+" inside="+Inside(content.StartFeet)+" mainPath in/total="+content.MainPath.Count(Inside)+"/"+content.MainPath.Length+" branch in="+content.BranchPath.Count(Inside));
   sb.AppendLine("actors in: "+string.Join(",",session.Actors.Where(a=>a!=null&&Inside(a.transform.position)).Select(a=>a.Id+(a.transform.IsChildOf(mine.transform)?"(mine)":""))));
   sb.AppendLine("mainPath head: "+string.Join(" ",content.MainPath.Take(16).Select(p=>p.ToString("F0"))));
   var end=V297(rel.outsidePath,rel.outsidePath.Length-3);int join=JoinIndex297(content.MainPath,end);
   sb.AppendLine("outside end="+end.ToString("F1")+" join index="+join+" at "+content.MainPath[join].ToString("F1")+" d="+Vector3.Distance(content.MainPath[join],end).ToString("F1"));
   var line=ui.MapData.Lines?.FirstOrDefault(l=>l.Id=="mine_inn");if(line!=null)sb.AppendLine("map line mine_inn: "+line.Points.Length+" pts "+line.Points.First()+" .. "+line.Points.Last());
   if(ui.WorldSheet.Routes!=null)foreach(var r in ui.WorldSheet.Routes.Where(r=>r.Points.Any(Inside)||r.Id.Contains("mine")))sb.AppendLine("sheet route "+r.Id+" "+r.From+"->"+r.To+": "+r.Points.Length+" pts "+r.Points.First().ToString("F0")+" .. "+r.Points.Last().ToString("F0"));
   var outside=new SortedDictionary<string,int>();
   foreach(var t in roots.Where(g=>g!=mine).SelectMany(g=>g.GetComponentsInChildren<Transform>(true)))if(Inside(t.position)){var parts=HierarchyPath(t).Split('/');string k=string.Join("/",parts.Take(Math.Min(3,parts.Length)));outside[k]=outside.TryGetValue(k,out int n)?n+1:1;}
   foreach(var r in ui.WorldSheet.Routes.Where(r=>r.Id=="mine_inn"))sb.AppendLine("  "+string.Join(" ",r.Points.Select(q=>q.ToString("F0"))));
   foreach(var z in ui.MapData.Zones??Array.Empty<WorldMapZoneSpec>())if(z.Id.Contains("mine"))sb.AppendLine("zone "+z.Id+" poly="+z.Polygon.Length+" detail="+z.DetailPath.Length+" y="+z.MinimumY+".."+z.MaximumY+" first="+(z.Polygon.Length>0?z.Polygon[0].ToString():"-")+" floorPath="+(z.FloorPath?.Length??0)+" clearance="+z.FloorClearance+" floor0="+(z.FloorPath!=null&&z.FloorPath.Length>0?z.FloorPath[0].ToString("F1"):"-")+" detailLines="+(z.DetailLines?.Length??0)+" illus="+(z.Illustration!=null));
   foreach(var l in ui.MapData.Lines??Array.Empty<WorldMapLineSpec>())if(l.Id.Contains("mine"))sb.AppendLine("line "+l.Id+" "+l.Kind+" "+l.Points.Length+" "+l.Points.First().ToString("F0")+" .. "+l.Points.Last().ToString("F0"));
   var near=new Vector2(3420,1900);
   foreach(var m in ui.MapData.Markers??Array.Empty<WorldMapMarkerSpec>())if(Vector2.Distance(m.WorldXZ,near)<320)sb.AppendLine("marker "+m.Id+" '"+m.Label+"' "+m.Kind+" "+m.WorldXZ.ToString("F0")+" zone="+m.ZoneId);
   var loc=ui.MapData.Locations;sb.AppendLine("locations="+(loc!=null?AssetDatabase.GetAssetPath(loc):"null")+" bounds="+ui.MapData.BoundsMin+".."+ui.MapData.BoundsMax+" illus="+(ui.MapData.Zones.First(z=>z.Id=="mine_interior").Illustration is Texture2D tx?AssetDatabase.GetAssetPath(tx)+" "+tx.width+"x"+tx.height:"-")+" uv="+ui.MapData.Zones.First(z=>z.Id=="mine_interior").IllustrationWorldUv+" rev="+ui.MapData.Zones.First(z=>z.Id=="mine_interior").DiscoveryRevision+" explore="+ui.MapData.Zones.First(z=>z.Id=="mine_interior").ExploreWalkedPassages);
   if(loc!=null)foreach(var e in loc.Entries)if(Vector2.Distance(new Vector2(e.Centre.x,e.Centre.z),near)<320||(e.Polygon!=null&&e.Polygon.Any(q=>Vector2.Distance(q,near)<200)))sb.AppendLine("location "+e.Id+" '"+e.Name+"' c="+e.Centre.ToString("F0")+" r="+e.Radius+" y="+e.MinimumY+".."+e.MaximumY+" poly="+(e.Polygon?.Length??0)+" floor="+(e.FloorPath?.Length??0)+(e.FloorPath!=null&&e.FloorPath.Length>0?" f0="+e.FloorPath[0].ToString("F0"):""));
   sb.AppendLine("non-mine transforms inside the old cave:");foreach(var kv in outside)sb.AppendLine("  "+kv.Value+"x "+kv.Key);
   return sb.ToString();
  }
  // cave-actors — relocated mine actors must stand on the baked NavMesh (the session throws at startup otherwise). The two
  // encounters of the old exterior approach land inside the new hillside after the rigid move; each is snapped to the nearest
  // NavMesh within 12 m, with its content encounter (feet + patrol) moved the same way. cave-revert restores both.
  static string CaveActors297()
  {
   RequireClean292();var session=Session292();var content=session.Content;var report=new List<string>();
   foreach(var a in session.Actors.Where(a=>a!=null&&a.Id.StartsWith("mine_")))
   {
    var agent=a.GetComponent<UnityEngine.AI.NavMeshAgent>();float offset=agent!=null?agent.baseOffset:0;var feet=a.transform.position-Vector3.up*offset;
    if(UnityEngine.AI.NavMesh.SamplePosition(feet,out var near,2f,UnityEngine.AI.NavMesh.AllAreas)){report.Add(a.Id+": on NavMesh ("+Vector3.Distance(near.position,feet).ToString("F2")+" m)");continue;}
    if(!UnityEngine.AI.NavMesh.SamplePosition(feet,out var hit,12f,UnityEngine.AI.NavMesh.AllAreas)){report.Add(a.Id+": NO NavMesh within 12 m at "+feet.ToString("F1"));continue;}
    var delta=hit.position-feet;a.transform.position+=delta;
    if(a.PatrolPoints!=null)a.PatrolPoints=a.PatrolPoints.Select(p=>UnityEngine.AI.NavMesh.SamplePosition(p+delta,out var q,6f,UnityEngine.AI.NavMesh.AllAreas)?q.position:p+delta).ToArray();
    var enc=content.Encounters.OrderBy(e=>Vector3.Distance(e.Feet,feet)).FirstOrDefault();
    if(enc!=null&&Vector3.Distance(enc.Feet,feet)<3f)
    {enc.Feet+=delta;if(enc.Patrol!=null)enc.Patrol=enc.Patrol.Select(p=>UnityEngine.AI.NavMesh.SamplePosition(p+delta,out var q,6f,UnityEngine.AI.NavMesh.AllAreas)?q.position:p+delta).ToArray();report.Add("  encounter "+enc.Id+" moved with it");}
    EditorUtility.SetDirty(a);report.Add(a.Id+": snapped "+feet.ToString("F1")+" -> "+hit.position.ToString("F1")+" (d="+delta.magnitude.ToString("F1")+" m)");
   }
   EditorUtility.SetDirty(content);Physics.SyncTransforms();Save292();
   string text=string.Join("\n",report);File.WriteAllText(K297+"/Cave/actors.txt",text);return text;
  }
  // nearest main-path index to a point (the outside path joins the old route there)
  static int JoinIndex297(Vector3[] path,Vector3 p,int limit=int.MaxValue){int best=0;float d=float.MaxValue;for(int i=0;i<Math.Min(limit,path.Length);i++){float e=(path[i]-p).sqrMagnitude;if(e<d){d=e;best=i;}}return best;}
  // The cave plan texture maps u->world x, v->world z inside IllustrationWorldUv (normalised map bounds). A 90-degree yaw keeps the
  // rectangle axis-aligned, so the plan is resampled losslessly (pixel permutation) into a private copy and its rectangle moves with the cave.
  static string RotateCaveIllustration297(WorldMapZoneSpec zone,WorldMapBakedDataSO map,Func<Vector2,Vector2> m2)
  {
   string source=AssetDatabase.GetAssetPath(zone.Illustration);var old=new Texture2D(2,2,TextureFormat.RGBA32,false);old.LoadImage(File.ReadAllBytes(source));
   int w=old.width,h=old.height;var px=old.GetPixels32();var size=map.BoundsMax-map.BoundsMin;var uv=zone.IllustrationWorldUv;
   Vector2 min=map.BoundsMin+Vector2.Scale(uv.min,size),max=map.BoundsMin+Vector2.Scale(uv.max,size);
   // images of the rectangle's origin and axes decide the pixel permutation
   Vector2 o=m2(min),ax=m2(new Vector2(max.x,min.y))-o,az=m2(new Vector2(min.x,max.y))-o;
   var corners=new[]{m2(min),m2(max),m2(new Vector2(min.x,max.y)),m2(new Vector2(max.x,min.y))};
   Vector2 nmin=new Vector2(corners.Min(c=>c.x),corners.Min(c=>c.y)),nmax=new Vector2(corners.Max(c=>c.x),corners.Max(c=>c.y));
   bool swap=Mathf.Abs(ax.y)>Mathf.Abs(ax.x);int nw=swap?h:w,nh=swap?w:h;var outPx=new Color32[nw*nh];float det=ax.x*az.y-ax.y*az.x;
   for(int j=0;j<nh;j++)for(int i=0;i<nw;i++)
   {
    var q=new Vector2(nmin.x+(i+.5f)/nw*(nmax.x-nmin.x),nmin.y+(j+.5f)/nh*(nmax.y-nmin.y))-o;
    float su=(q.x*az.y-q.y*az.x)/det,sv=(ax.x*q.y-ax.y*q.x)/det;
    int oi=Mathf.Clamp(Mathf.FloorToInt(su*w),0,w-1),oj=Mathf.Clamp(Mathf.FloorToInt(sv*h),0,h-1);outPx[j*nw+i]=px[oj*w+oi];
   }
   var tex=new Texture2D(nw,nh,TextureFormat.RGBA32,false);tex.SetPixels32(outPx);tex.Apply();
   DevSceneKit.EnsureFolder(A297+"/Map");string path=A297+"/Map/"+Path.GetFileNameWithoutExtension(source)+"297.png";File.WriteAllBytes(path,tex.EncodeToPNG());
   Object.DestroyImmediate(tex);Object.DestroyImmediate(old);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
   if(AssetImporter.GetAtPath(source) is TextureImporter si&&AssetImporter.GetAtPath(path) is TextureImporter di)
   {var s=new TextureImporterSettings();si.ReadTextureSettings(s);di.SetTextureSettings(s);di.SetPlatformTextureSettings(si.GetDefaultPlatformTextureSettings());di.maxTextureSize=si.maxTextureSize;di.textureCompression=si.textureCompression;di.SaveAndReimport();}
   zone.Illustration=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
   zone.IllustrationWorldUv=Rect.MinMaxRect((nmin.x-map.BoundsMin.x)/size.x,(nmin.y-map.BoundsMin.y)/size.y,(nmax.x-map.BoundsMin.x)/size.x,(nmax.y-map.BoundsMin.y)/size.y);
   return "cave plan "+w+"x"+h+" -> "+nw+"x"+nh+" "+path+" rect "+nmin.ToString("F1")+".."+nmax.ToString("F1");
  }
  static string Cave297(bool revert)
  {
   RequireClean292();var scene=SceneManager.GetActiveScene();var session=Session292();var content=session.Content;
   var roots=scene.GetRootGameObjects();var mine=roots.Single(g=>g.name=="mine");
   var ui=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();var map=ui.MapData;var sheet=ui.WorldSheet;
   string file=K297+"/Cave/cave-original.json";
   if(revert)
   {
    var o=JsonUtility.FromJson<CaveOriginal297>(File.ReadAllText(file));
    mine.transform.SetPositionAndRotation(o.minePosition,o.mineRotation);
    // data assets come back byte-for-byte from the file backup (JSON object references are only valid within one editor session)
    string backup=K297+"/Cave/backup";
    foreach(var asset in new Object[]{content,map,sheet,map.Locations})
    {if(asset==null)continue;string path=AssetDatabase.GetAssetPath(asset),copy=backup+"/"+Path.GetFileName(path);if(File.Exists(copy)){File.Copy(copy,path,true);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);}}
    var walker=session.Walker.Body;walker.enabled=false;walker.transform.SetPositionAndRotation(o.body,o.bodyRotation);walker.enabled=true;
    for(int i=0;i<o.actorIds.Length;i++){var a=session.Actors.FirstOrDefault(x=>x!=null&&x.Id==o.actorIds[i]);if(a!=null)a.transform.position=o.actorPositions[i];}
    var all=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
    for(int i=0;i<o.retired.Length;i++){var t=all.FirstOrDefault(x=>HierarchyPath(x)==o.retired[i]);if(t!=null)t.gameObject.SetActive(o.retiredActive[i]);}
    Physics.SyncTransforms();Save292();return "Cave restored to the recorded #296 state (vegetation placements removed at the mouth are listed in cave-original.json)";
   }
   var rel=JsonUtility.FromJson<CaveRelocation297>(File.ReadAllText(K297+"/Cave/relocation.json"));
   var pivot=new Vector3(rel.pivot[0],0,rel.pivot[2]);var rot=Quaternion.Euler(0,rel.unityYaw,0);var shift=V297(rel.sceneTranslation);
   Vector3 M(Vector3 p)=>rot*(p-pivot)+pivot+shift;
   var lo=V297(rel.caveBounds.min);var hi=V297(rel.caveBounds.max);
   bool Inside(Vector3 p)=>p.x>=lo.x&&p.x<=hi.x&&p.y>=lo.y&&p.y<=hi.y&&p.z>=lo.z&&p.z<=hi.z;
   if(File.Exists(file))throw new Exception("Cave already relocated (cave-original.json exists); run cave-revert first");
   var retire=new[]{"mine/Cave_ExteriorCover237"};
   var allT=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
   var retiredT=retire.Select(p=>allT.FirstOrDefault(t=>HierarchyPath(t)==p)).ToArray();
   var actors=session.Actors.Where(a=>a!=null&&Inside(a.transform.position)).ToArray();
   var original=new CaveOriginal297{minePosition=mine.transform.position,mineRotation=mine.transform.rotation,content=JsonUtility.ToJson(content),map=JsonUtility.ToJson(map),sheet=JsonUtility.ToJson(sheet),locations=map.Locations!=null?JsonUtility.ToJson(map.Locations):"",
    body=session.Walker.Body.transform.position,bodyRotation=session.Walker.Body.transform.rotation,actorIds=actors.Select(a=>a.Id).ToArray(),actorPositions=actors.Select(a=>a.transform.position).ToArray(),
    retired=retire,retiredActive=retiredT.Select(t=>t!=null&&t.gameObject.activeSelf).ToArray(),removedPlacements=Array.Empty<string>()};
   Directory.CreateDirectory(K297+"/Cave/backup");File.WriteAllText(file,JsonUtility.ToJson(original,true));
   AssetDatabase.SaveAssets();
   foreach(var asset in new Object[]{content,map,sheet,map.Locations})if(asset!=null){string path=AssetDatabase.GetAssetPath(asset);File.Copy(path,K297+"/Cave/backup/"+Path.GetFileName(path),true);}
   var report=new List<string>();
   // 1. the hierarchy (lights, dressing, collision, clue meshes, props) moves as one rigid body
   mine.transform.RotateAround(pivot,Vector3.up,rel.unityYaw);mine.transform.position+=shift;
   foreach(var t in retiredT)if(t!=null)t.gameObject.SetActive(false);
   // 2. data inside the old cave
   int pts=0,enc=0,cps=0;
   foreach(var p in content.Points)if(Inside(p.Position)){p.Position=M(p.Position);pts++;}
   foreach(var e in content.Encounters)if(Inside(e.Feet)){e.Feet=M(e.Feet);if(e.Patrol!=null)e.Patrol=e.Patrol.Select(M).ToArray();enc++;}
   foreach(var c in content.Checkpoints)if(Inside(c.Feet)){c.Feet=M(c.Feet);c.Yaw+=rel.unityYaw;cps++;}
   content.StartFeet=M(content.StartFeet);content.StartYaw+=rel.unityYaw;
   // main path = relocated cave part + mouth apron/inn route (relocation.json) + the rest of the campaign route after the inn
   var outside=Enumerable.Range(0,rel.outsidePath.Length/3).Select(i=>V297(rel.outsidePath,3*i)).ToArray();
   var oldMain=content.MainPath;var cavePart=oldMain.Where(Inside).Select(M).ToList();int join=JoinIndex297(oldMain,outside[outside.Length-1],64);
   if(Vector3.Distance(oldMain[join],outside[outside.Length-1])>3)throw new Exception("outside path does not end on the main route (d="+Vector3.Distance(oldMain[join],outside[outside.Length-1])+")");
   content.MainPath=cavePart.Concat(outside).Concat(oldMain.Skip(join+1)).ToArray();content.BranchPath=content.BranchPath.Select(p=>Inside(p)?M(p):p).ToArray();
   EditorUtility.SetDirty(content);
   foreach(var a in actors){if(a.transform.IsChildOf(mine.transform))continue;a.transform.position=M(a.transform.position);}
   foreach(var a in actors)if(a.PatrolPoints!=null)a.PatrolPoints=a.PatrolPoints.Select(p=>Inside(p)?M(p):p).ToArray();
   foreach(var cp in roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroContentPoint>(true)))if(!cp.transform.IsChildOf(mine.transform)&&Inside(cp.transform.position))cp.transform.position=M(cp.transform.position);
   report.Add("moved points="+pts+" encounters="+enc+" checkpoints="+cps+" actors="+actors.Length+" start="+content.StartFeet.ToString("F2")+" yaw="+content.StartYaw+"; main path "+oldMain.Length+" -> "+content.MainPath.Length+" (cave "+cavePart.Count+" + outside "+outside.Length+" + route after index "+join+")");
   // 3. map + world sheet route. XZ inside the old cave rectangle moves with the cave; the baked zone Y band was authored in
   //    the V4 geometry frame (floor 135.8, scene mine root at -11.2), so it is converted to the scene frame here.
   bool InsideXZ(Vector2 v)=>v.x>=lo.x&&v.x<=hi.x&&v.y>=lo.z&&v.y<=hi.z;
   Vector2 M2(Vector2 v){var q=M(new Vector3(v.x,(lo.y+hi.y)*.5f,v.y));return new Vector2(q.x,q.z);}
   Vector2 XZ(Vector3 v)=>new Vector2(v.x,v.z);
   float geometryY=rel.geometryTranslation!=null&&rel.geometryTranslation.Length==3?rel.geometryTranslation[1]:shift.y;
   foreach(var z in map.Zones??Array.Empty<WorldMapZoneSpec>())if(z.Id=="mine_interior")
   {
    z.Polygon=z.Polygon.Select(M2).ToArray();z.DetailPath=z.DetailPath.Select(M2).ToArray();
    if(z.DetailLines!=null)foreach(var l in z.DetailLines)l.Points=l.Points.Select(M2).ToArray();
    if(z.FloorPath!=null&&z.FloorPath.Length>0)z.FloorPath=z.FloorPath.Select(M).ToArray();
    z.MinimumY+=geometryY;z.MaximumY+=geometryY;z.DiscoveryRevision=(z.DiscoveryRevision??"")+"-r297";
    if(z.Illustration!=null)report.Add(RotateCaveIllustration297(z,map,M2));
   }
   // map trails: mine -> overlook now ends where the mouth apron meets the #296 inn trail
   var innTrail=map.Lines?.FirstOrDefault(l=>l.Id=="mine_overlook__geumpyo_inn");
   float TrailDistance(Vector2 q){float best=float.MaxValue;for(int i=1;i<innTrail.Points.Length;i++){var a0=innTrail.Points[i-1];var d=innTrail.Points[i]-a0;float s=d.sqrMagnitude>0?Mathf.Clamp01(Vector2.Dot(q-a0,d)/d.sqrMagnitude):0;best=Mathf.Min(best,Vector2.Distance(q,a0+d*s));}return best;}
   var mineTrail=map.Lines?.FirstOrDefault(l=>l.Id=="mine__mine_overlook");
   if(mineTrail!=null&&innTrail!=null)
   {
    var apron=new List<Vector2>();foreach(var q in outside.Select(XZ)){apron.Add(q);if(apron.Count>1&&TrailDistance(q)<2.5f)break;}
    mineTrail.Points=mineTrail.Points.Where(InsideXZ).Select(M2).Concat(apron).ToArray();report.Add("map trail mine__mine_overlook="+mineTrail.Points.Length+" pts (apron "+apron.Count+")");
   }
   foreach(var m in map.Markers??Array.Empty<WorldMapMarkerSpec>())if(InsideXZ(m.WorldXZ)){m.WorldXZ=M2(m.WorldXZ);report.Add("marker "+m.Id+" -> "+m.WorldXZ.ToString("F0"));}
   if(map.Locations!=null)
   {
    foreach(var e in map.Locations.Entries)if(Inside(e.Centre))
    {e.Centre=M(e.Centre);e.Polygon=e.Polygon?.Select(M2).ToArray();e.FloorPath=e.FloorPath?.Select(M).ToArray();e.MinimumY+=shift.y;e.MaximumY+=shift.y;report.Add("location "+e.Id+" -> "+e.Centre.ToString("F1")+" y "+e.MinimumY.ToString("F1")+".."+e.MaximumY.ToString("F1"));}
    EditorUtility.SetDirty(map.Locations);
   }
   var route=sheet?.Routes?.FirstOrDefault(r=>r.Id=="mine_inn");
   if(route!=null){route.Points=route.Points.Where(Inside).Select(q=>M(q)).Concat(outside).ToArray();EditorUtility.SetDirty(sheet);report.Add("sheet route mine_inn="+route.Points.Length+" pts");}
   EditorUtility.SetDirty(map);
   // 4. the player starts in the relocated mine
   var body=session.Walker.Body;body.enabled=false;body.transform.SetPositionAndRotation(content.StartFeet,Quaternion.Euler(0,content.StartYaw,0));body.enabled=true;
   Physics.SyncTransforms();Save292();
   report.Add("mine root="+mine.transform.position.ToString("F2")+" rot="+mine.transform.eulerAngles.y.ToString("F1")+"; retired="+string.Join(",",retire)+"; main path points="+content.MainPath.Length);
   string text=string.Join("\n",report);File.WriteAllText(K297+"/Cave/relocate.txt",text);return text;
  }
 }
}
