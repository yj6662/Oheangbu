using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 finish pass on the #296 candidate. Inspection commands never modify the scene.
 public static class CompactFinish297 { public static string Run(string command)=>CompactRebuildAuthoring.Finish297(command); }
 public static partial class CompactRebuildAuthoring
 {
  const string O297="../Art/World/Compact/Rebuild/Finish297";
  public static string Finish297(string command)
  {
   Directory.CreateDirectory(O297);
   if(command.StartsWith("view:"))return View297(command.Substring(5));
   if(command.StartsWith("probe:"))return Probe297(command.Substring(6));
   if(command.StartsWith("near:"))return Near297(command.Substring(5));
   if(command=="lights")return Lights297();
   if(command.StartsWith("ambient-probe"))return AmbientProbe297(command.EndsWith(":update"));
   if(command=="ambient-apply"){foreach(var d in Object.FindObjectsByType<Oheangbu.App.WorldLookDriver>(FindObjectsSortMode.None))d.Apply();return AmbientProbe297(false);}
   if(command.StartsWith("feature297:"))return Feature297(command.Substring(11));
   if(command.StartsWith("camera-renderer:"))return CameraRenderer297(int.Parse(command.Substring(16)));
   if(command.StartsWith("shaders-near:"))return ShadersNear297(command.Substring(13));
   if(command.StartsWith("pick:"))return Pick297(command.Substring(5));
   if(command.StartsWith("rpick:"))return RayPick297(command.Substring(6));
   if(command.StartsWith("renderer:"))return Renderer297(command.Substring(9));
   if(command.StartsWith("groups:"))return Groups297(command.Substring(7));
   if(command.StartsWith("route-nav:"))return RouteNav297(command.Substring(10));
   if(command.StartsWith("urp:"))return Urp297(command.Substring(4));
   if(command.StartsWith("ink-tune:"))return InkTune297(command.Substring(9));
   if(command.StartsWith("export-mesh:"))return ExportMesh297Cmd(command.Substring(12));
   if(command.StartsWith("tree:"))return Tree297(command.Substring(5));
   if(command.StartsWith("ceiling:"))return Ceiling297(command.Substring(8));
   if(command.StartsWith("heights:"))return Heights297(command.Substring(8));
   if(command.StartsWith("section:"))return Section297(command.Substring(8));
   if(command.StartsWith("profile:"))return Profile297(command.Substring(8));
   if(command=="floaters"||command.StartsWith("floaters:"))return Floaters297(command.Length>9?float.Parse(command.Substring(9),CultureInfo.InvariantCulture):2f);
   if(command.StartsWith("material-survey:"))return MaterialSurvey297(command.Substring(16));
   if(command=="kit-textures")return KitTextures297();
   if(command=="lighting")return Lighting297(false);
   if(command=="lighting-revert")return Lighting297(true);
   if(command=="shading")return Shading297(false);
   if(command=="shading-revert")return Shading297(true);
   if(command=="attraction")return Attraction297(false);
   if(command=="attraction-revert")return Attraction297(true);
   if(command.StartsWith("mat-tune:"))return MatTune297(command.Substring(9));
   if(command=="cave-dry")return CaveDry297();
   if(command=="cave-actors")return CaveActors297();
   if(command=="cave")return Cave297(false);
   if(command=="cave-revert")return Cave297(true);
   if(command=="cave-portal")return CavePortal297(false);
   if(command=="cave-dressing")return CaveDressing297(false);
   if(command=="cave-dressing-revert")return CaveDressing297(true);
   if(command=="cave-portal-revert")return CavePortal297(true);
   if(command=="surface")return Surface297(false);
   if(command=="surface-revert")return Surface297(true);
   if(command.StartsWith("compound:"))return Compound297(command.Substring(9));
   if(command.StartsWith("compound-remove:"))return CompoundRemove297(command.Substring(16));
   if(command.StartsWith("walk-compound:"))return WalkCompound297(command.Substring(14));
   if(command=="main-start")return Finish297MainStart.Run("start");
   if(command=="main-status")return Finish297MainStart.Run("status");
   if(command=="merge298-prepare")return Merge298Prepare297();
   if(command=="main-create")return MainCreate297();
   if(command=="main-recreate")return MainCreate297(true);
   if(command=="lobby-connect")return LobbyConnect297(false);
   if(command=="lobby-revert")return LobbyConnect297(true);
   if(command=="review-play-status"||command=="review-play:status")return Finish297ReviewPlay.Status();
   if(command.StartsWith("review-play:start-mum:"))return Finish297ReviewPlay.Run(command.Substring(12));
   if(command.StartsWith("review-play:start:"))return Finish297ReviewPlay.Start(command.Substring(18),false);
   if(command.StartsWith("review-play:"))return Finish297ReviewPlay.Start(command.Substring(12),false);
   if(command=="play-stop"){EditorApplication.isPlaying=false;return "stopping Play";}
   if(command.StartsWith("doors-pose:"))return DoorsPose297(float.Parse(command.Substring(11),System.Globalization.CultureInfo.InvariantCulture));
   if(command=="walk-cave")return WalkCave297();
   if(command=="runtime-start")return Runtime297("start");
   if(command=="runtime-status")return Runtime297("status");
   if(command=="legacy"||command=="legacy:dry"||command=="legacy-revert")return Legacy297(command=="legacy"?"apply":command=="legacy:dry"?"dry":"revert");
   // gate route fix (CompactFinish297.GateRoute.cs, Finish297/Capital/EXIT2_FIX_IMPL.md)
   if(command.StartsWith("gate-route-scene:"))return GateRouteScene297(command.Substring(17));
   if(command=="gate-route-revert"||command.StartsWith("gate-route-revert:")||command.StartsWith("gate-route-revert|"))return GateRouteRevert297(command.Substring(17).TrimStart(':'));
   if(command=="gate-route-check"||command.StartsWith("gate-route-check:")||command.StartsWith("gate-route-check|"))return GateRouteCheck297(command.Substring(16).TrimStart(':'));
   if(command=="gate-route-status")return GateRouteStatus297();
   if(command.StartsWith("gate-route:"))return GateRoute297(command.Substring(11));
   throw new ArgumentException(command);
  }
  static Vector3 Vec297(string text){var v=text.Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray();return new Vector3(v[0],v[1],v[2]);}
  // view:<name>:<eye x,y,z>:<target x,y,z>[:raw][:hide=<path>|<path>] — same renderer/post/fog as the review captures.
  // hide= temporarily deactivates scene objects by hierarchy path (inspection only; restored before returning).
  static string View297(string argument)
  {
   RequireClean292();var a=argument.Split(':');string folder=O297+"/Views";Directory.CreateDirectory(folder);string output=folder+"/"+a[0]+".png";
   var hidden=new List<GameObject>();
   var hide=a.Skip(3).FirstOrDefault(s=>s.StartsWith("hide="));
   if(hide!=null){var all=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
    foreach(var p in hide.Substring(5).Split('|')){var t=all.FirstOrDefault(x=>Path297(x)==p);if(t!=null&&t.gameObject.activeSelf){t.gameObject.SetActive(false);hidden.Add(t.gameObject);}}}
   // captures always render at the shipping quality level (PC); a previous Play may leave the editor on Mobile (0.8 scale, 1 cascade)
   int priorQuality=QualitySettings.GetQualityLevel();int pc=Array.IndexOf(QualitySettings.names,"PC");if(pc>=0&&pc!=priorQuality)QualitySettings.SetQualityLevel(pc,true);
   try{eye293=Vec297(a[1]);target293=Vec297(a[2]);output293=output;raw293=a.Skip(3).Contains("raw");Capture292(6);return output;}
   finally{eye293=null;target293=null;output293=null;raw293=false;foreach(var g in hidden)g.SetActive(true);if(QualitySettings.GetQualityLevel()!=priorQuality)QualitySettings.SetQualityLevel(priorQuality,true);}
  }
  // probe:<x>,<z> — every collider hit on a vertical ray, top to bottom.
  static string Probe297(string argument)
  {
   var v=argument.Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray();
   var hits=Physics.RaycastAll(new Vector3(v[0],3000,v[1]),Vector3.down,4000,~0,QueryTriggerInteraction.Ignore).OrderByDescending(h=>h.point.y);
   return string.Join("\n",hits.Select(h=>h.point.y.ToString("F2",CultureInfo.InvariantCulture)+" "+Path297(h.collider.transform)));
  }
  static string Path297(Transform t){var names=new List<string>();for(;t!=null;t=t.parent)names.Add(t.name);names.Reverse();return string.Join("/",names);}
  // near:<x>,<y>,<z>,<radius> — renderers whose bounds touch the sphere, grouped by their first three path levels.
  static string Near297(string argument)
  {
   var v=argument.Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray();var c=new Vector3(v[0],v[1],v[2]);float r=v[3];
   var groups=new Dictionary<string,(int count,long tris,Bounds b,HashSet<string> mats,HashSet<string> shaders)>();
   foreach(var ren in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
   {
    if(!ren.enabled||!ren.gameObject.activeInHierarchy)continue;var b=ren.bounds;if(b.SqrDistance(c)>r*r)continue;
    var parts=Path297(ren.transform).Split('/');string key=string.Join("/",parts.Take(Math.Min(3,parts.Length)));
    long tris=0;var mf=ren.GetComponent<MeshFilter>();if(mf!=null&&mf.sharedMesh!=null)for(int s=0;s<mf.sharedMesh.subMeshCount;s++)tris+=(long)mf.sharedMesh.GetIndexCount(s)/3;
    if(!groups.TryGetValue(key,out var g))g=(0,0,b,new HashSet<string>(),new HashSet<string>());g.b.Encapsulate(b);g.count++;g.tris+=tris;
    foreach(var m in ren.sharedMaterials)if(m!=null){g.mats.Add(m.name);if(m.shader!=null)g.shaders.Add(m.shader.name);}groups[key]=g;
   }
   var sb=new StringBuilder();
   foreach(var kv in groups.OrderBy(k=>k.Key))
    sb.AppendLine(kv.Key+" | renderers "+kv.Value.count+" tris "+kv.Value.tris+" | min "+kv.Value.b.min.ToString("F1")+" max "+kv.Value.b.max.ToString("F1")+" | shaders "+string.Join(",",kv.Value.shaders.Take(6))+" | mats "+string.Join(",",kv.Value.mats.Take(8)));
   File.WriteAllText(O297+"/near-"+string.Join("_",v.Select(x=>((int)x).ToString()))+".txt",sb.ToString());return sb.ToString();
  }
  // lights — every light, the environment lighting and every volume override in the open scene.
  static string Lights297()
  {
   var sb=new StringBuilder();var scene=SceneManager.GetActiveScene();sb.AppendLine("scene "+scene.path);
   sb.AppendLine("ambient mode="+RenderSettings.ambientMode+" sky="+RenderSettings.ambientSkyColor+" equator="+RenderSettings.ambientEquatorColor+" ground="+RenderSettings.ambientGroundColor+" intensity="+RenderSettings.ambientIntensity);
   sb.AppendLine("reflection mode="+RenderSettings.defaultReflectionMode+" intensity="+RenderSettings.reflectionIntensity+" skybox="+(RenderSettings.skybox!=null?RenderSettings.skybox.name+"/"+RenderSettings.skybox.shader.name:"none")+" sun="+(RenderSettings.sun!=null?Path297(RenderSettings.sun.transform):"none"));
   sb.AppendLine("fog="+RenderSettings.fog+" mode="+RenderSettings.fogMode+" color="+RenderSettings.fogColor+" density="+RenderSettings.fogDensity+" start="+RenderSettings.fogStartDistance+" end="+RenderSettings.fogEndDistance);
   var lights=Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(l=>l.gameObject.scene==scene).ToArray();
   sb.AppendLine("lights "+lights.Length+" (active "+lights.Count(l=>l.isActiveAndEnabled)+")");
   foreach(var g in lights.GroupBy(l=>l.type))sb.AppendLine("  "+g.Key+": "+g.Count()+" active "+g.Count(l=>l.isActiveAndEnabled)+" shadows "+g.Count(l=>l.isActiveAndEnabled&&l.shadows!=LightShadows.None));
   foreach(var l in lights.Where(l=>l.type==LightType.Directional||l.isActiveAndEnabled).OrderBy(l=>l.type).Take(80))
    sb.AppendLine("  "+(l.isActiveAndEnabled?"+":"-")+" "+l.type+" "+Path297(l.transform)+" pos "+l.transform.position.ToString("F1")+" euler "+l.transform.eulerAngles.ToString("F1")+" I="+l.intensity.ToString("F2")+" col="+l.color+" range="+l.range.ToString("F1")+" shadows="+l.shadows+" mode="+l.lightmapBakeType+" temp="+(l.useColorTemperature?l.colorTemperature.ToString("F0"):"off"));
   foreach(var vol in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(v=>v.gameObject.scene==scene))
   {
    sb.AppendLine("volume "+Path297(vol.transform)+" active="+vol.isActiveAndEnabled+" global="+vol.isGlobal+" priority="+vol.priority+" weight="+vol.weight+" profile="+(vol.sharedProfile!=null?AssetDatabase.GetAssetPath(vol.sharedProfile):"none"));
    if(vol.sharedProfile==null)continue;
    foreach(var comp in vol.sharedProfile.components)
    {
     var over=comp.parameters.Select((p,i)=>(p,i)).Where(x=>x.p.overrideState).Select(x=>{var f=comp.GetType().GetFields().FirstOrDefault(fi=>ReferenceEquals(fi.GetValue(comp),x.p));return (f!=null?f.Name:"p"+x.i)+"="+Value297(x.p);});
     sb.AppendLine("  "+(comp.active?"+":"-")+" "+comp.GetType().Name+": "+string.Join(", ",over));
    }
   }
   File.WriteAllText(O297+"/lights.txt",sb.ToString());return sb.ToString();
  }
  static string Value297(VolumeParameter p){var prop=p.GetType().GetProperty("value");var v=prop!=null?prop.GetValue(p):null;return v is Object o?(o!=null?o.name:"null"):(v!=null?Convert.ToString(v,CultureInfo.InvariantCulture):"null");}
 }
}
