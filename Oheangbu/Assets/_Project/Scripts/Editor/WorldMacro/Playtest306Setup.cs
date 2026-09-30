using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #306 batch-1 editor steps (SPEC-PLAYTEST-306, PLAN §5 ①): layer 12 NatureSolid and the natural-solid pool per scene.
 // Queue-safe: refusals come back as strings, never a dialog. Scenes = main + the two candidates it is promoted from.
 //   layer | solids-scene:<scene path> | status | mat-get:<path>|<prop> | mat-float:<path>|<prop>|<value> (A/B, not saved)
 public static class Playtest306Setup
 {
  const string Profile="Assets/_Project/Data/World/NaturalSolidProfile306.asset";const string RootName="NaturalSolids306";
  static readonly string[] Scenes={"Assets/_Project/Scenes/World/W_Demo_Main.unity","Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity","Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity"};
  public static string Run(string c)
  {
   c=(c??"").Trim();
   if(c=="layer")return Layer();
   if(c.StartsWith("solids-scene:",StringComparison.Ordinal))return SolidsScene(c.Substring(13).Trim());
   if(c=="status")return Status();
   if(c=="play-probe")return PlayProbe();
   if(c.StartsWith("nav-route:",StringComparison.Ordinal))return NavRoute(c.Substring(10).Trim());
   if(c.StartsWith("mat-get:",StringComparison.Ordinal)){var a=c.Substring(8).Split('|');var m=Mat(a[0]);return m==null?"REFUSED no material "+a[0]:a[1]+"="+m.GetFloat(a[1]).ToString("R",System.Globalization.CultureInfo.InvariantCulture);}
   // A/B only: sets a float in memory (not saved); the caller restores the prior value it gets back
   if(c.StartsWith("mat-float:",StringComparison.Ordinal)){var a=c.Substring(10).Split('|');var m=Mat(a[0]);if(m==null)return "REFUSED no material "+a[0];float prior=m.GetFloat(a[1]);m.SetFloat(a[1],float.Parse(a[2],System.Globalization.CultureInfo.InvariantCulture));return a[1]+" "+prior.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" -> "+m.GetFloat(a[1]).ToString("R",System.Globalization.CultureInfo.InvariantCulture);}
   return "REFUSED unknown command "+c;
  }
  static string Layer()
  {
   var tm=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);var p=tm.FindProperty("layers").GetArrayElementAtIndex(12);
   if(p.stringValue=="NatureSolid")return "layer 12 already NatureSolid";
   if(!string.IsNullOrEmpty(p.stringValue))return "REFUSED layer 12 is taken by '"+p.stringValue+"'";
   p.stringValue="NatureSolid";tm.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
   return "layer 12 = NatureSolid (collision matrix untouched: layer 12 collides with everything)";
  }
  static string SolidsScene(string path)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Play mode";
   if(!Scenes.Contains(path))return "REFUSED scene not in the #306 list: "+path;
   for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)return "REFUSED dirty scene "+SceneManager.GetSceneAt(i).path;
   if(LayerMask.NameToLayer("NatureSolid")!=12)return "REFUSED run 'layer' first";
   var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
   var prof=AssetDatabase.LoadAssetAtPath<NaturalSolidProfileSO>(Profile);
   if(prof==null){Directory.CreateDirectory(Path.GetDirectoryName(Profile));prof=ScriptableObject.CreateInstance<NaturalSolidProfileSO>();prof.name="NaturalSolidProfile306";AssetDatabase.CreateAsset(prof,Profile);AssetDatabase.SaveAssetIfDirty(prof);}
   foreach(var g in scene.GetRootGameObjects().Where(g=>g.name==RootName).ToArray())Object.DestroyImmediate(g);
   var contacts=Object.FindObjectsByType<CompactEnvironmentContact265>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(x=>x.gameObject.scene==scene);
   var go=new GameObject(RootName);var ns=go.AddComponent<CompactNaturalSolids>();ns.Profile=prof;ns.Contacts=contacts;ns.DiscoverInScene=true;
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return RootName+" in "+path+": profile "+Profile+", contacts "+(contacts!=null?contacts.name:"none (walker/vehicle found at runtime)")+", discover sheets in scene";
  }
  // nav-route:<id>: walk a routes.json polyline every 3 m, drop onto physics ground, NavMesh.SamplePosition(1.2 m); report misses
  // with the colliders within 2 m (diagnostic for the 41 required-route check)
  static string NavRoute(string id)
  {
   string file=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/World/Compact/Rebuild/Architecture296/Generated/routes.json"));
   string json=File.ReadAllText(file);int at=json.IndexOf("\""+id+"\"",StringComparison.Ordinal);if(at<0)return "REFUSED no route "+id;
   int ps=json.IndexOf("[",json.IndexOf("oints",at,StringComparison.Ordinal),StringComparison.Ordinal);int depth=0,pe=ps;for(;pe<json.Length;pe++){if(json[pe]=='[')depth++;else if(json[pe]==']'&&--depth==0)break;}
   var nums=System.Text.RegularExpressions.Regex.Matches(json.Substring(ps,pe-ps),@"-?\d+(\.\d+)?(e-?\d+)?").Cast<System.Text.RegularExpressions.Match>().Select(m=>float.Parse(m.Value,System.Globalization.CultureInfo.InvariantCulture)).ToList();
   var pts=new System.Collections.Generic.List<Vector3>();for(int i=0;i+2<nums.Count;i+=3)pts.Add(new Vector3(nums[i],nums[i+1],nums[i+2]));
   var sb=new System.Text.StringBuilder();int samples=0,miss=0;
   for(int k=0;k+1<pts.Count;k++){float L=Vector3.Distance(pts[k],pts[k+1]);int n=Mathf.Max(1,Mathf.CeilToInt(L/3f));for(int j=0;j<n;j++){var q=Vector3.Lerp(pts[k],pts[k+1],j/(float)n);samples++;
    if(!Physics.Raycast(q+Vector3.up*30,Vector3.down,out var hit,80,~0,QueryTriggerInteraction.Ignore)){sb.AppendLine("no ground at "+q.ToString("F1"));miss++;continue;}
    if(UnityEngine.AI.NavMesh.SamplePosition(hit.point,out var nh,1.2f,UnityEngine.AI.NavMesh.AllAreas))continue;
    miss++;var near=Physics.OverlapSphere(hit.point,2f,~0,QueryTriggerInteraction.Ignore).Select(x=>x.name+"@"+(x.transform.parent!=null?x.transform.parent.name:"-")).Distinct().Take(6);
    sb.AppendLine("no NavMesh within 1.2 m at "+hit.point.ToString("F1")+" ground by "+hit.collider.name+" | near: "+string.Join(", ",near));}}
   return "route "+id+" points "+pts.Count+" samples "+samples+" misses "+miss+"\n"+sb;
  }
  // play-probe: why a lobby -> main load has not finished (focus gates the loading warm-up; see PlaytestUiRoot.Loading)
  static string PlayProbe()
  {
   var ui=Oheangbu.App.World.UI.PlaytestUiRoot.Instance;var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
   return "playing="+EditorApplication.isPlaying+" focused="+Application.isFocused+" runInBackground="+Application.runInBackground+" active="+SceneManager.GetActiveScene().name+" scenes="+SceneManager.sceneCount
    +" ui="+(ui!=null)+(ui!=null?" loading="+ui.LoadingInProgress+" busy="+ui.Busy+" title="+ui.IsTitle:"")+" session="+(s!=null)+(s!=null?" init="+s.InitializationComplete+" scene="+s.gameObject.scene.name:"")+" timeScale="+Time.timeScale;
  }
  static Material Mat(string path)=>AssetDatabase.LoadAssetAtPath<Material>(path);
  static string Status()
  {
   var s=SceneManager.GetActiveScene();var r=s.GetRootGameObjects().FirstOrDefault(g=>g.name==RootName);
   return "layer12="+LayerMask.LayerToName(12)+" profile="+(AssetDatabase.LoadAssetAtPath<NaturalSolidProfileSO>(Profile)!=null)+" scene="+s.path+" root="+(r!=null)+(r!=null&&EditorApplication.isPlaying?" "+Describe(r.GetComponent<CompactNaturalSolids>()):"");
  }
  static string Describe(CompactNaturalSolids n)=>n==null?"no component":"ready="+n.Ready+" sheets="+n.SheetCount+" candidates="+n.Candidates+" active="+n.Active+" peak="+n.PeakActive+" pool="+n.PoolSize+" queries="+n.Queries+" lastMs="+n.LastMs.ToString("F3")+" peakMs="+n.PeakMs.ToString("F3")+" prepareMs="+n.PrepareMs.ToString("F1")+" backlog="+n.Backlog+" layerMissing="+n.LayerMissing;
 }
}
