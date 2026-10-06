using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 post/sky track (SPEC-REGION-SKY-308, SPEC-EVENT-WASH-308): shared helpers of the RegionSky308 / EventWash308 queue commands.
 // Scene order = the promotion chain (#296 candidate -> #298 candidate -> W_Demo_Main). Protected trees and W_Demo_Compact /
 // 03_Content are never opened. Refusals are exceptions turned into "refused: ..." strings by the command entry points.
 internal static class PostLedger308
 {
  internal const string Arch296="Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity";
  internal const string Folk298="Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity";
  internal const string Main="Assets/_Project/Scenes/World/W_Demo_Main.unity";
  internal static readonly string[] Scenes={Arch296,Folk298,Main};
  static readonly string[] ProtectedRoots={"Watershed295","Reworld292","MountainTrail285"};

  internal sealed class Refused:Exception{public Refused(string m):base(m){}}

  internal static string Short(string scene)=>scene==Arch296?"arch296":scene==Folk298?"folk298":scene==Main?"main":Path.GetFileNameWithoutExtension(scene);

  /// <summary>Scenes selected by an optional scene=arch296|folk298|main|&lt;path&gt; option (all three in order when absent).</summary>
  internal static string[] Select(Dictionary<string,string> options)
  {
   if(options==null||!options.TryGetValue("scene",out var token)||string.IsNullOrEmpty(token))return Scenes;
   var hit=Scenes.FirstOrDefault(s=>string.Equals(Short(s),token,StringComparison.OrdinalIgnoreCase)||s==token);
   if(hit==null)throw new Refused("scene '"+token+"' is not one of "+string.Join(", ",Scenes.Select(Short)));
   return new[]{hit};
  }

  /// <summary>key=value tokens after the command words (also bare words as key=""), split on ':'.</summary>
  internal static Dictionary<string,string> Options(IEnumerable<string> parts)
  {
   var d=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(var p in parts){if(string.IsNullOrWhiteSpace(p))continue;int at=p.IndexOf('=');if(at<0)d[p.Trim()]="";else d[p.Substring(0,at).Trim()]=p.Substring(at+1).Trim();}
   return d;
  }

  /// <summary>Edit mode, no compile/import, no unsaved scene.</summary>
  internal static void RequireEditable()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Refused("Edit mode only (Play is running)");
   if(EditorApplication.isCompiling||EditorApplication.isUpdating)throw new Refused("the editor is compiling or importing");
   for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);if(s.isDirty)throw new Refused("scene "+s.path+" has unsaved changes");}
  }

  /// <summary>Opens a target scene Single (or keeps it when it is already the only active one). Requires RequireEditable first.</summary>
  internal static Scene Open(string path)
  {
   if(!Scenes.Contains(path))throw new Refused("not a #308 target scene: "+path);
   if(IsProtectedPath(path))throw new Refused("protected path "+path);
   var active=SceneManager.GetActiveScene();
   if(active.path==path&&SceneManager.sceneCount==1)return active;
   for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);if(s.isDirty)throw new Refused("scene "+s.path+" has unsaved changes");}
   return EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
  }

  internal static bool IsProtectedPath(string path)
  {
   if(string.IsNullOrEmpty(path))return false;
   if(ProtectedRoots.Any(r=>path.Contains("/"+r+"/")||path.Contains("/"+r+"_")))return true;
   return path.EndsWith("/W_Demo_Compact.unity",StringComparison.Ordinal)||path.EndsWith("/03_Content.asset",StringComparison.Ordinal);
  }

  /// <summary>True for objects under a protected tree root (Watershed295*, Reworld292*, MountainTrail285*).</summary>
  internal static bool UnderProtectedTree(Transform t)
  {
   for(var p=t;p!=null;p=p.parent)foreach(var r in ProtectedRoots)if(p.name.StartsWith(r,StringComparison.Ordinal))return true;
   return false;
  }

  internal static string Utc()=>DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
  /// <summary>Absolute path of a repository path (outside Assets), e.g. "Art/World/RegionSky308".</summary>
  internal static string RepoPath(string relative)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../..",relative));
  internal static string Abs(string assetPath)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",assetPath));

  /// <summary>Copies a project file (scene or asset) into backupDir/&lt;utc&gt;/ before a write; returns the copy.</summary>
  internal static string Backup(string backupDir,string utc,string assetPath)
  {
   string dir=Path.Combine(backupDir,utc);Directory.CreateDirectory(dir);
   string to=Path.Combine(dir,Path.GetFileName(assetPath));
   File.Copy(Abs(assetPath),to,true);
   if(File.Exists(Abs(assetPath)+".meta"))File.Copy(Abs(assetPath)+".meta",to+".meta",true);
   return to;
  }

  internal static string PathOf(Transform t){var sb=new StringBuilder(t.name);for(var p=t.parent;p!=null;p=p.parent)sb.Insert(0,p.name+"/");return sb.ToString();}

  internal static Transform Find(Scene scene,string path)
  {
   if(string.IsNullOrEmpty(path))return null;
   var parts=path.Split('/');
   foreach(var root in scene.GetRootGameObjects())
   {
    if(root.name!=parts[0])continue;
    var t=root.transform;
    for(int i=1;i<parts.Length&&t!=null;i++){Transform next=null;foreach(Transform c in t)if(c.name==parts[i]){next=c;break;}t=next;}
    if(t!=null)return t;
   }
   return null;
  }

  internal static IEnumerable<T> All<T>(Scene scene) where T:Component=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true));

  internal static string AssetRef(UnityEngine.Object o)=>o==null?"":AssetDatabase.GetAssetPath(o);
  internal static T Load<T>(string path) where T:UnityEngine.Object=>string.IsNullOrEmpty(path)?null:AssetDatabase.LoadAssetAtPath<T>(path);

  internal static void SaveScene(Scene scene)
  {
   EditorSceneManager.MarkSceneDirty(scene);
   if(!EditorSceneManager.SaveScene(scene))throw new Exception("SaveScene failed: "+scene.path);
  }

  internal static string F(float v)=>v.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
 }
}
