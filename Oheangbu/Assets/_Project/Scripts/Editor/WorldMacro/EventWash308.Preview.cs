using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 권역 담채 edit-mode preview and capture poses (SPEC-EVENT-WASH-308 D308-6b, AC-W15–W17).
 //   wash308-preview[:location=<id>|:all][:kind=volume|surface][:eye=x,y,z][:scene=..][:off]
 //        Builds the planner from the scene's sheet / catalog / palette / campaign, forces the chosen zones available, snaps the
 //        fades and writes the globals exactly as EventWashDriver308 would (EventWashGlobals308) for the eye (:eye or the SceneView
 //        camera). Globals only: no material, scene or asset is written. Refused in Play (the driver owns the globals there). A
 //        capture pair is render on / render off; always finish with :off (count, volume split and the debug view back to 0).
 //   wash308-views[:location=<id>]
 //        Capture poses per baked progress volume, chosen on the offline height field (no scene opened): 4 far (best above-ridge
 //        share on the 1/1.5/2 km rings, azimuths ≥ 45° apart, eye = ground + 1.7 m, yaw to the place, pitch to the volume middle),
 //        1 valley (share 0, the lowest eye: the opposite case), 3 near (place centre, level / +30° / −10°), 2 near30 (30 m out on
 //        the best far viewer's side, looking back at the place, level / +30°).
 //        -> Art/Playtest306/Checks/wash308-views.json (wash308-views-<id>.json with :location). Buildings and trees are not in
 //        the field: a near pose inside a house is moved by the capture step, not here.
 public static partial class EventWash308
 {
  static string Preview(Dictionary<string,string> o)
  {
   var globals=new EventWashGlobals308();
   if(o.ContainsKey("off"))
   {
    globals.Off(true);SceneView.RepaintAll();
    return "wash308-preview:off — _OhEventWashCount 0, _OhEventWashVolA 0, _OhInkWashDebug308 0 (AC-W15)";
   }
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new PostLedger308.Refused("Edit mode only (in Play the driver owns the globals)");
   if(EditorApplication.isCompiling||EditorApplication.isUpdating)throw new PostLedger308.Refused("the editor is compiling or importing");
   var sheet=AssetDatabase.LoadAssetAtPath<EventWashSheetSO>(SheetPath)??throw new PostLedger308.Refused("EventWashSheet308 missing ("+SheetPath+")");

   // the active target scene as it is (nothing is written to it); otherwise open the requested one (Main by default)
   string want=o.TryGetValue("scene",out var sc)&&sc.Length>0?PostLedger308.Select(o)[0]:null;
   var active=SceneManager.GetActiveScene();Scene scene;
   if(want==null?PostLedger308.Scenes.Contains(active.path):active.path==want)scene=active;
   else{PostLedger308.RequireEditable();scene=PostLedger308.Open(want??PostLedger308.Main);}
   var (session,catalog,palette)=SceneRefs(scene);
   var campaign=session.Content!=null?session.Content.Campaign:null;
   if(campaign==null)throw new PostLedger308.Refused("the scene's session has no campaign");

   var planner=new EventWashPlanner308(sheet,catalog,palette,campaign);
   string location=o.TryGetValue("location",out var loc)&&loc.Length>0&&!o.ContainsKey("all")?loc:null;
   string kind=o.TryGetValue("kind",out var k)?k.Trim().ToLowerInvariant():"";
   if(kind!=""&&kind!="volume"&&kind!="surface")throw new PostLedger308.Refused("kind must be volume or surface, not '"+kind+"'");
   planner.ForceAvailable(false);int forced=0;
   for(int i=0;i<planner.Count;i++)
   {
    var z=planner[i];
    bool on=(location==null||z.LocationId==location)&&(kind==""||kind=="volume"==(z.Kind==EventWashPlanner308.Kind.Volume));
    z.Available=on;if(on)forced++;
   }
   if(forced==0)
   {
    var have=Enumerable.Range(0,planner.Count).Select(i=>planner[i].LocationId+"("+planner[i].Kind+")").Distinct();
    throw new PostLedger308.Refused("no zone matches location="+(location??"*")+" kind="+(kind==""?"*":kind)+"; zones: "+string.Join(", ",have)+
                                    (planner.Skipped.Count>0?"; skipped: "+string.Join("; ",planner.Skipped):""));
   }
   planner.Step(0f,true);

   Vector3 eye;string eyeFrom;
   if(o.TryGetValue("eye",out var e)&&e.Length>0)
   {
    var xyz=e.Split(',');
    if(xyz.Length!=3||!float.TryParse(xyz[0],NumberStyles.Float,CultureInfo.InvariantCulture,out float ex)||!float.TryParse(xyz[1],NumberStyles.Float,CultureInfo.InvariantCulture,out float ey)||!float.TryParse(xyz[2],NumberStyles.Float,CultureInfo.InvariantCulture,out float ez))
     throw new PostLedger308.Refused("eye=x,y,z expected, got '"+e+"'");
    eye=new Vector3(ex,ey,ez);eyeFrom=":eye";
   }
   else if(SceneView.lastActiveSceneView!=null&&SceneView.lastActiveSceneView.camera!=null){eye=SceneView.lastActiveSceneView.camera.transform.position;eyeFrom="SceneView camera";}
   else throw new PostLedger308.Refused("no SceneView camera: pass :eye=x,y,z");

   var zone=new Vector4[EventWashPlanner308.ShaderZones];var colour=new Vector4[EventWashPlanner308.ShaderZones];
   var band=new Vector4[EventWashPlanner308.ShaderZones];var volume=new Vector4[EventWashPlanner308.ShaderZones];
   int count=planner.Write(eye,zone,colour,band,volume,out int split);
   globals.Write(sheet,count,split,zone,colour,band,volume);
   SceneView.RepaintAll();

   var sb=new StringBuilder("wash308-preview "+PostLedger308.Short(scene.path)+" eye "+eye.ToString("F1")+" ("+eyeFrom+"), forced "+forced+" zone(s)"+(location!=null?" at "+location:"")+(kind!=""?" kind "+kind:"")+
                            " -> count "+count+", volume split "+split+"\n");
   for(int s=0;s<count;s++)
   {
    var z=planner[planner.SelectedZone(s)];
    sb.AppendLine("  slot "+s+" "+(s<split?"VOLUME ":"surface ")+z.StageId+" @"+z.LocationId+" ("+z.Meaning+") zone "+V(zone[s])+" colour "+V(colour[s])+(s<split?" volume "+V(volume[s]):" band "+V(band[s])));
   }
   if(planner.Skipped.Count>0)sb.AppendLine("  skipped: "+string.Join("; ",planner.Skipped));
   return sb.Append("  globals only (no material/scene/asset). Finish with wash308-preview:off (AC-W15).").ToString();
  }

  static string V(Vector4 v)=>"("+string.Join(", ",new[]{v.x,v.y,v.z,v.w}.Select(x=>x.ToString("0.####",CultureInfo.InvariantCulture)))+")";

  [Serializable] sealed class View308{public string location="",stages="",kind="",pose="";public float ring,azimuth,share,horizonY;public Vector3 position,euler;public float volumeCentreY,volumeHalfHeight,volumeRadius;}
  [Serializable] sealed class ViewFile308{public string version="D308-6b",utc="",sheet="",heightField="",catalog="",note="";public float eyeHeight;public List<View308> views=new List<View308>();}

  static float Separation(float a,float b){float d=Mathf.Abs(Mathf.DeltaAngle(a,b));return d;}

  static string Views(Dictionary<string,string> o)
  {
   var sheet=AssetDatabase.LoadAssetAtPath<EventWashSheetSO>(SheetPath)??throw new PostLedger308.Refused("EventWashSheet308 missing ("+SheetPath+")");
   string catalogPath=AssetDatabase.GUIDToAssetPath(ExpectedCatalogGuid);
   var catalog=AssetDatabase.LoadAssetAtPath<WorldLocationCatalog>(catalogPath)??throw new PostLedger308.Refused("Main catalog (guid "+ExpectedCatalogGuid+") missing");
   var field=Field308.Load();
   string only=o.TryGetValue("location",out var l)&&l.Length>0?l:null;
   var groups=(sheet.Entries??new EventWashSheetSO.Entry[0]).Where(x=>EventWashPlanner308.Baked(x)&&(only==null||x.LocationId==only)).GroupBy(x=>x.LocationId).ToArray();
   if(groups.Length==0)throw new PostLedger308.Refused("no baked progress volume"+(only!=null?" at "+only:"")+" in the sheet (run wash308-sheet:dry, check, then wash308-sheet)");
   var file=new ViewFile308{utc=PostLedger308.Utc(),sheet=SheetPath,heightField=HeightFieldPath,catalog=catalogPath,eyeHeight=ExposureEye,
    note="euler = Unity (x = pitch, negative looks up; y = yaw). Eye = height field + "+ExposureEye.ToString(CultureInfo.InvariantCulture)+" m (no buildings/trees). share = visible part of the volume's vertical density above the terrain horizon."};
   var sb=new StringBuilder("wash308-views "+(only??"all")+"\n");
   foreach(var g in groups)
   {
    var place=catalog.Entries.FirstOrDefault(x=>x!=null&&x.Id==g.Key);
    if(place==null){sb.AppendLine("  "+g.Key+": not in the catalog, skipped");continue;}
    var first=g.First();float yc=.5f*(first.VolumeBaseY+first.VolumeTopY),hh=.5f*(first.VolumeTopY-first.VolumeBaseY),R=first.VolumeRadius;
    float cx=place.Centre.x,cz=place.Centre.z;string stages=string.Join(",",g.Select(x=>x.StageId));
    View308 Make(string kind,string pose,float ring,float az,float share,float hy,Vector3 pos,float yaw,float pitchUp)=>
     new View308{location=place.Id,stages=stages,kind=kind,pose=pose,ring=ring,azimuth=az,share=share,horizonY=hy,position=pos,euler=new Vector3(-pitchUp,yaw,0f),volumeCentreY=yc,volumeHalfHeight=hh,volumeRadius=R};
    float YawTo(Vector3 from)=>Mathf.Atan2(cx-from.x,cz-from.z)*Mathf.Rad2Deg;

    var all=ExposureRings.SelectMany(d=>Sights(field,cx,cz,d,yc,hh)).ToList();
    // far: best share first (then the lower horizon), azimuths spread (45°, relaxed when the ring set is narrow)
    var ranked=all.Where(x=>x.share>0f).OrderByDescending(x=>x.share).ThenBy(x=>x.horizonY-x.eye.y).ToList();
    var far=new List<Sight308>();
    foreach(float gap in new[]{45f,20f,0f})
    {
     foreach(var s in ranked){if(far.Count>=4)break;if(far.Any(x=>x.azimuth==s.azimuth&&x.distance==s.distance))continue;if(far.All(x=>Separation(x.azimuth,s.azimuth)>=gap))far.Add(s);}
     if(far.Count>=4)break;
    }
    int n=0;
    foreach(var s in far)
    {
     float pitch=Mathf.Atan2(yc-s.eye.y,s.distance)*Mathf.Rad2Deg;
     file.views.Add(Make("far","far"+(++n),s.distance,s.azimuth,s.share,s.horizonY,s.eye,YawTo(s.eye),pitch));
    }
    // valley (opposite case): share 0, the lowest eye
    var valley=all.Where(x=>x.share<=0f).OrderBy(x=>x.eye.y).ThenBy(x=>x.distance).Cast<Sight308?>().FirstOrDefault();
    if(valley.HasValue){var s=valley.Value;file.views.Add(Make("valley","valley",s.distance,s.azimuth,0f,s.horizonY,s.eye,YawTo(s.eye),Mathf.Atan2(yc-s.eye.y,s.distance)*Mathf.Rad2Deg));}
    // near: place centre at eye height, facing the best far viewer's side (the open direction)
    var centreEye=new Vector3(cx,field.At(cx,cz)+ExposureEye,cz);float yaw=far.Count>0?far[0].azimuth:0f;
    file.views.Add(Make("near","near_level",0f,yaw,0f,0f,centreEye,yaw,0f));
    file.views.Add(Make("near","near_up30",0f,yaw,0f,0f,centreEye,yaw,30f));
    file.views.Add(Make("near","near_down10",0f,yaw,0f,0f,centreEye,yaw,-10f));
    // near30: 30 m out on the open side, looking back at the place (level / +30°): "looking straight at the place up close"
    {
     float a=yaw*Mathf.Deg2Rad;float nx=cx+30f*Mathf.Sin(a),nz=cz+30f*Mathf.Cos(a);
     var near30=new Vector3(nx,(Field308.Inside(nx,nz)?field.At(nx,nz):field.At(cx,cz))+ExposureEye,nz);float back=YawTo(near30);
     file.views.Add(Make("near","near30_level",30f,yaw,0f,0f,near30,back,0f));
     file.views.Add(Make("near","near30_up30",30f,yaw,0f,0f,near30,back,30f));
    }
    sb.AppendLine("  "+place.Id+" ["+stages+"] volume yc "+F1(yc)+" Hh "+F1(hh)+" R "+F1(R)+": far "+string.Join(", ",far.Select(s=>(s.distance/1000f).ToString("0.0",CultureInfo.InvariantCulture)+" km "+s.azimuth.ToString("0",CultureInfo.InvariantCulture)+"° share "+Pct(s.share)+"%"))+
                  (far.Count<4?" (only "+far.Count+" with share > 0)":"")+" | valley "+(valley.HasValue?(valley.Value.distance/1000f).ToString("0.0",CultureInfo.InvariantCulture)+" km "+valley.Value.azimuth.ToString("0",CultureInfo.InvariantCulture)+"° eye y "+F1(valley.Value.eye.y):"none (every ground eye sees it)")+
                  " | near "+centreEye.ToString("F1")+" yaw "+yaw.ToString("0",CultureInfo.InvariantCulture));
   }
   string name=only==null?"wash308-views.json":"wash308-views-"+only+".json";
   string path=Path.Combine(ChecksDir,name);File.WriteAllText(path,JsonUtility.ToJson(file,true));
   return sb.Append("  -> "+path+" ("+file.views.Count+" poses)").ToString();
  }
 }
}
