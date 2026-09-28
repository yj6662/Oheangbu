using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 presentation fixes from the S6 review (candidate scene; recorded once, revertible):
 //  - RootCaveAndSinmok263: whole sub-dungeon floats above the #292 terrain (not re-placed) -> inactive
 //  - mine_fire/0: ranged mode fires a default-material primitive sphere -> melee (attack mode 1)
 //  - cave Work_Lantern lights outshine the ore veins (critical-path attraction must lead) -> intensity cap
 //  - magic-stone car: persistent cyan running emission (stone glow may only flash) -> RunningEmission 0
 // Queue: CompactRebuildAuthoring DemoFixes297 dry|apply|revert
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] class DemoFixRecord297{public string[] inactive=new string[0];public string[] lights=new string[0];public float[] intensities=new float[0];public string[] hiddenRenderers=new string[0];public string fireActor="";public int fireMode=-1;public string carProfile="";public float carEmission=-1;}
  const float WorkLanternCap297=1.6f;
  public static string DemoFixes297(string arg)
  {
   string file=K297+"/DemoFixes/original.json";var s=Session292();var scene=SceneManager.GetActiveScene();var lines=new List<string>{"scene="+scene.path};
   var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
   var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="RootCaveAndSinmok263");
   var actorsUnderRoot=root==null?new string[0]:s.Actors.Where(a=>a!=null&&a.transform.IsChildOf(root.transform)).Select(a=>a.Id).ToArray();
   var fire=s.Actors.FirstOrDefault(a=>a!=null&&a.Id=="mine_fire/0");var fireCtl=fire!=null?fire.GetComponent<Oheangbu.Combat.EnemyController>():null;
   var lamps=all.Where(t=>t.name.StartsWith("Work_Lantern")).SelectMany(t=>t.GetComponentsInChildren<Light>(true)).Distinct().ToArray();
   var car=Object.FindObjectsByType<MagicStoneCarDriveVfx>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(c=>c.Profile).FirstOrDefault(p=>p!=null);
   SerializedProperty CarEmission(SerializedObject so)=>so.FindProperty("RunningEmission");
   if(arg=="dry")
   {
    lines.Add("root cave: "+(root!=null?"found, active="+root.activeSelf+", actors under it=["+string.Join(",",actorsUnderRoot)+"]":"missing"));
    lines.Add("mine_fire/0: "+(fireCtl!=null?"attack mode="+new SerializedObject(fireCtl).FindProperty("_attackMode")?.intValue+" profile="+(fireCtl.AttackProfile!=null?AssetDatabase.GetAssetPath(fireCtl.AttackProfile)+" mode="+fireCtl.AttackProfile.Mode:"none")+" effective="+fireCtl.EffectiveAttackMode:"missing"));
    lines.Add("work lanterns: "+lamps.Length+" lights, intensities ["+string.Join(",",lamps.Select(l=>l.intensity.ToString("F1")))+"]");
    lines.Add("car profile: "+(car!=null?AssetDatabase.GetAssetPath(car)+" RunningEmission="+CarEmission(new SerializedObject(car))?.floatValue:"missing"));
    return string.Join("\n",lines);
   }
   if(arg=="revert")
   {
    var o=JsonUtility.FromJson<DemoFixRecord297>(File.ReadAllText(file));
    foreach(var p in o.inactive){var t=all.FirstOrDefault(x=>HierarchyPath(x)==p);if(t!=null)t.gameObject.SetActive(true);}
    foreach(var h in o.hiddenRenderers??new string[0]){var path=h.Substring(0,h.Length-2);var t=all.FirstOrDefault(x=>HierarchyPath(x)==path);if(t==null)continue;if(h.EndsWith("#R")){var r=t.GetComponent<Renderer>();if(r!=null)r.enabled=true;}else{var l=t.GetComponent<Light>();if(l!=null)l.enabled=true;}}
    for(int i=0;i<o.lights.Length;i++){var t=all.FirstOrDefault(x=>HierarchyPath(x)==o.lights[i]);var l=t!=null?t.GetComponent<Light>():null;if(l!=null){l.intensity=o.intensities[i];EditorUtility.SetDirty(l);}}
    var fa=s.Actors.FirstOrDefault(a=>a!=null&&a.Id==o.fireActor);if(fa!=null&&o.fireMode>=0){var so=new SerializedObject(fa.GetComponent<Oheangbu.Combat.EnemyController>());so.FindProperty("_attackMode").intValue=o.fireMode;so.ApplyModifiedPropertiesWithoutUndo();}
    var cp=AssetDatabase.LoadAssetAtPath<ScriptableObject>(o.carProfile);if(cp!=null&&o.carEmission>=0){var so=new SerializedObject(cp);CarEmission(so).floatValue=o.carEmission;so.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssetIfDirty(cp);}
    Save292();return "demo fixes reverted";
   }
   if(arg!="apply")throw new ArgumentException(arg);
   RequireClean292();
   var rec=File.Exists(file)?JsonUtility.FromJson<DemoFixRecord297>(File.ReadAllText(file)):null;bool first=rec==null;if(first)rec=new DemoFixRecord297();
   if(root!=null)
   {
    // the session binds every actor to the NavMesh at start (an inactive sinmok263 throws), so the floating sub-dungeon
    // keeps its colliders and actor and only stops rendering/lighting (it is unreachable above the #292 terrain)
    if(!root.activeSelf){root.SetActive(true);lines.Add("root cave re-activated (inactive actor broke session start)");}
    var rs=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled).ToArray();var ls=root.GetComponentsInChildren<Light>(true).Where(l=>l.enabled).ToArray();
    rec.hiddenRenderers=rec.hiddenRenderers.Concat(rs.Select(r=>HierarchyPath(r.transform)+"#R").Concat(ls.Select(l=>HierarchyPath(l.transform)+"#L"))).Distinct().ToArray();
    foreach(var r in rs){r.enabled=false;EditorUtility.SetDirty(r);}foreach(var l in ls){l.enabled=false;EditorUtility.SetDirty(l);}
    lines.Add("root cave visuals hidden: "+rs.Length+" renderers, "+ls.Length+" lights (actors kept: "+string.Join(",",actorsUnderRoot)+")");
   }
   if(fireCtl!=null)
   {
    var so=new SerializedObject(fireCtl);var mode=so.FindProperty("_attackMode");
    if(mode!=null){if(first){rec.fireActor=fire.Id;rec.fireMode=mode.intValue;}mode.intValue=1;so.ApplyModifiedPropertiesWithoutUndo();lines.Add("mine_fire/0 attack mode -> 1 (melee)");}
   }
   if(first){rec.lights=lamps.Select(l=>HierarchyPath(l.transform)).ToArray();rec.intensities=lamps.Select(l=>l.intensity).ToArray();}
   foreach(var l in lamps){l.intensity=Mathf.Min(l.intensity,WorkLanternCap297);EditorUtility.SetDirty(l);}lines.Add("work lanterns capped at "+WorkLanternCap297+" ("+lamps.Length+")");
   if(car!=null)
   {
    var so=new SerializedObject(car);var e=CarEmission(so);
    if(e!=null){if(first){rec.carProfile=AssetDatabase.GetAssetPath(car);rec.carEmission=e.floatValue;}e.floatValue=0;so.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssetIfDirty(car);lines.Add("car running emission -> 0 ("+AssetDatabase.GetAssetPath(car)+")");}
   }
   Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,JsonUtility.ToJson(rec,true));
   Save292();return string.Join("\n",lines);
  }
 }
}
