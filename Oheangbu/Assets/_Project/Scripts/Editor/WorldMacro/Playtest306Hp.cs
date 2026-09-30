using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #306 #8 enemy HP tiers (SPEC-PLAYTEST-306 AC-8a, TEST): profile assets + per-scene assignment of EnemyVitals._profile.
 // Queue-safe (refusals are strings). Scenes = main + the two candidates; the protected W_Demo_Compact is never opened.
 //   hp-list[:<scene>] | hp-profiles | hp-assign:<scene> | hp-revert:<scene>
 // Tier rule (by encounter id): mine_* = Mine 24, village_road_raider_* = Road 36, bosses (config copy HP, name, IsBoss) by id,
 // demo_growth_lesson keeps its config HP (ConfigHp), everything else = Field 60. The assignment is recorded per scene so revert restores the previous reference exactly.
 public static class Playtest306Hp
 {
  const string Dir="Assets/_Project/Data/Configs/EnemyTiers";
  static readonly string[] Scenes={"Assets/_Project/Scenes/World/W_Demo_Main.unity","Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity","Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity"};
  // ConfigHp: MaxHp 0 = keeps the encounter's own config copy (demo_growth_lesson 180), lock-on stroke, no boss bar and no invented name
  static readonly (string file,float hp,string name,bool boss)[] Tiers={("EnemyVitals_Mine",24f,"",false),("EnemyVitals_Road",36f,"",false),("EnemyVitals_Field",60f,"",false),("EnemyVitals_ConfigHp",0f,"",false)};
  static readonly string[] ConfigHpIds={"demo_growth_lesson"};
  // boss id -> bar name (MaxHp 0 = keep the boss's own config copy)
  static readonly (string id,string name)[] Bosses={("cheongryong","청룡"),("south_gate_general","남문 장수"),("sinmok263","신목")};
  static string Record(string scene)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/Playtest306/Backups/Hp306_"+Path.GetFileNameWithoutExtension(scene)+".json"));
  [Serializable] class Rec{public List<string> paths=new List<string>();public List<string> before=new List<string>();}

  public static string Run(string c)
  {
   c=(c??"").Trim();
   if(EditorApplication.isPlayingOrWillChangePlaymode&&!c.StartsWith("hp-list",StringComparison.Ordinal))return "REFUSED Play mode";
   if(c=="hp-list")return List(SceneManager.GetActiveScene());
   if(c.StartsWith("hp-list:",StringComparison.Ordinal)){var s=Open(c.Substring(8).Trim(),out string e);return s.IsValid()?List(s):e;}
   if(c=="hp-profiles")return Profiles();
   if(c.StartsWith("hp-assign:",StringComparison.Ordinal))return Assign(c.Substring(10).Trim(),false);
   if(c.StartsWith("hp-revert:",StringComparison.Ordinal))return Assign(c.Substring(10).Trim(),true);
   return "REFUSED unknown command "+c+" (hp-list[:<scene>] | hp-profiles | hp-assign:<scene> | hp-revert:<scene>)";
  }

  static Scene Open(string path,out string error)
  {
   error="";
   if(!Scenes.Contains(path)){error="REFUSED scene not in the #306 list: "+path;return default;}
   if(SceneManager.GetActiveScene().path==path)return SceneManager.GetActiveScene();
   for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty){error="REFUSED dirty scene "+SceneManager.GetSceneAt(i).path;return default;}
   return EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
  }

  static string IdOf(EnemyVitals v){var e=v.GetComponentInParent<PrologueEncounter>(true);return e!=null?e.Id??"":"";}
  static string PathOf(Transform t){var sb=new StringBuilder(t.name);for(var p=t.parent;p!=null;p=p.parent)sb.Insert(0,p.name+"/");return sb.ToString();}
  static EnemyVitals[] All(Scene s)=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<EnemyVitals>(true)).ToArray();
  static string TierFor(string id)
  {
   if(Bosses.Any(b=>b.id==id))return "EnemyVitals_Boss_"+id;
   if(ConfigHpIds.Contains(id))return "EnemyVitals_ConfigHp";
   if(id.StartsWith("mine_",StringComparison.Ordinal))return "EnemyVitals_Mine";
   if(id.StartsWith("village_road_raider",StringComparison.Ordinal))return "EnemyVitals_Road";
   return "EnemyVitals_Field";
  }

  static string List(Scene s)
  {
   var sb=new StringBuilder(s.path+"\n");
   foreach(var v in All(s))
   {
    var so=new SerializedObject(v);var cfg=so.FindProperty("_config").objectReferenceValue as CombatConfigSO;var prof=so.FindProperty("_profile").objectReferenceValue as EnemyVitalsProfileSO;
    string id=IdOf(v);
    sb.AppendLine("  "+(id.Length>0?id:"(no encounter)")+" | "+PathOf(v.transform)+" | config "+(cfg!=null?cfg.name+" "+cfg.EnemyMaxHp:"-")+" | profile "+(prof!=null?prof.name:"-")+" -> "+TierFor(id)+" | MaxHp now "+v.MaxHp+(v.gameObject.activeInHierarchy?"":" (inactive)"));
   }
   return sb.ToString();
  }

  static string Profiles()
  {
   if(!AssetDatabase.IsValidFolder(Dir)){Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath,"..",Dir)));AssetDatabase.Refresh();}
   var sb=new StringBuilder();
   foreach(var t in Tiers)sb.AppendLine(Make(t.file,t.hp,t.name,t.boss));
   foreach(var b in Bosses)sb.AppendLine(Make("EnemyVitals_Boss_"+b.id,0f,b.name,true));
   AssetDatabase.SaveAssets();return sb.ToString();
  }

  static string Make(string file,float hp,string name,bool boss)
  {
   string path=Dir+"/"+file+".asset";var p=AssetDatabase.LoadAssetAtPath<EnemyVitalsProfileSO>(path);bool created=p==null;
   if(created){p=ScriptableObject.CreateInstance<EnemyVitalsProfileSO>();AssetDatabase.CreateAsset(p,path);}
   var so=new SerializedObject(p);so.FindProperty("_maxHp").floatValue=hp;so.FindProperty("_displayName").stringValue=name;so.FindProperty("_isBoss").boolValue=boss;so.FindProperty("_showLockOnBar").boolValue=!boss;
   so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(p);
   return (created?"created ":"updated ")+path+" MaxHp "+(hp>0?hp.ToString():"config")+(boss?" boss '"+name+"'":"");
  }

  static string Assign(string path,bool revert)
  {
   var s=Open(path,out string e);if(!s.IsValid())return e;
   string recPath=Record(path);var sb=new StringBuilder(path+(revert?" (revert)\n":" (assign)\n"));
   if(revert)
   {
    if(!File.Exists(recPath))return "REFUSED no record "+recPath;
    var r=JsonUtility.FromJson<Rec>(File.ReadAllText(recPath));var all=All(s);int n=0;
    for(int i=0;i<r.paths.Count;i++){var v=all.FirstOrDefault(x=>PathOf(x.transform)==r.paths[i]);if(v==null){sb.AppendLine("  missing "+r.paths[i]);continue;}
     var so=new SerializedObject(v);so.FindProperty("_profile").objectReferenceValue=string.IsNullOrEmpty(r.before[i])?null:AssetDatabase.LoadAssetAtPath<EnemyVitalsProfileSO>(r.before[i]);so.ApplyModifiedPropertiesWithoutUndo();n++;}
    EditorSceneManager.MarkSceneDirty(s);EditorSceneManager.SaveScene(s);File.Delete(recPath);return sb.Append("  restored "+n).ToString();
   }
   if(File.Exists(recPath))return "REFUSED already assigned (record "+recPath+"); run hp-revert:"+path+" first";
   var rec=new Rec();
   foreach(var v in All(s))
   {
    string id=IdOf(v);if(id.Length==0){sb.AppendLine("  skip (no encounter) "+PathOf(v.transform));continue;}
    var prof=AssetDatabase.LoadAssetAtPath<EnemyVitalsProfileSO>(Dir+"/"+TierFor(id)+".asset");if(prof==null)return "REFUSED missing "+Dir+"/"+TierFor(id)+".asset (run hp-profiles)";
    var so=new SerializedObject(v);var prop=so.FindProperty("_profile");
    rec.paths.Add(PathOf(v.transform));rec.before.Add(prop.objectReferenceValue!=null?AssetDatabase.GetAssetPath(prop.objectReferenceValue):"");
    prop.objectReferenceValue=prof;so.ApplyModifiedPropertiesWithoutUndo();
    sb.AppendLine("  "+id+" -> "+prof.name+" (MaxHp "+v.MaxHp+")");
   }
   Directory.CreateDirectory(Path.GetDirectoryName(recPath));File.WriteAllText(recPath,JsonUtility.ToJson(rec,true));
   EditorSceneManager.MarkSceneDirty(s);EditorSceneManager.SaveScene(s);
   return sb.Append("  assigned "+rec.paths.Count+", record "+recPath).ToString();
  }
 }
}
