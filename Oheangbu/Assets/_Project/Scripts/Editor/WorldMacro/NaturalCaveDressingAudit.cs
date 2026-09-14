using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object=UnityEngine.Object;
using Oheangbu.App.World.Dressing;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static class NaturalCaveDressingAudit
 {
  static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  static object Get(object o,string n)=>o.GetType().GetField(n,Flags)?.GetValue(o);
  public static string Execute(string command)
  {
   var renderer=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();var root=GameObject.Find(WorldMacroNaturalCave.RootName).transform;
   if(command=="all-rock-bounds")
   {
    var probe=new GameObject("Temporary_Rock_Bounds"){hideFlags=HideFlags.HideAndDontSave};
    try{var camera=probe.AddComponent<Camera>();camera.CopyFrom(Object.FindFirstObjectByType<Oheangbu.App.World.WorldMacroReviewController>().GetComponent<Camera>());camera.enabled=false;camera.aspect=16f/9;
    var p=root.TransformPoint(new Vector3(-88,1.65f,-4));var t=root.TransformPoint(new Vector3(-60,4,7));camera.transform.SetPositionAndRotation(p,Quaternion.LookRotation(t-p));var r=camera.ViewportPointToRay(new Vector3(1222f/1920,1-603f/1080,0));
    var results=new List<string>{"ray="+r};foreach(var mesh in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(m=>m.enabled).OrderBy(m=>Vector3.Distance(m.bounds.center,p)))if(mesh.bounds.IntersectRay(r,out var distance))results.Add("distance="+distance+" name="+mesh.name+" parent="+mesh.transform.parent?.name+" position="+mesh.transform.position+" bounds="+mesh.bounds+" mesh="+UnityEditor.AssetDatabase.GetAssetPath(mesh.GetComponent<MeshFilter>()?.sharedMesh));
    var probes=new List<GameObject>();bool back=Physics.queriesHitBackfaces;
    try{Physics.queriesHitBackfaces=true;foreach(var mesh in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(m=>m.enabled&&m.bounds.IntersectRay(r))){var mf=mesh.GetComponent<MeshFilter>();if(mf==null||mf.sharedMesh==null)continue;var temp=new GameObject("RockRay_"+mesh.name){hideFlags=HideFlags.HideAndDontSave};temp.transform.SetPositionAndRotation(mesh.transform.position,mesh.transform.rotation);temp.transform.localScale=mesh.transform.lossyScale;temp.AddComponent<MeshCollider>().sharedMesh=mf.sharedMesh;probes.Add(temp);}Physics.SyncTransforms();foreach(var h in Physics.RaycastAll(r,10000,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))results.Add("HIT "+h.collider.name+" distance="+h.distance+" triangle="+h.triangleIndex+" local="+root.InverseTransformPoint(h.point));}
    finally{foreach(var temp in probes)Object.DestroyImmediate(temp);Physics.queriesHitBackfaces=back;}
    File.WriteAllLines(WorldMacroNaturalCave.Output+"/all_rock_bounds.txt",results);return string.Join("\n",results);}
    finally{Object.DestroyImmediate(probe);}
   }
   if(command=="capture-disabled"||command=="capture-context-hidden"||command.StartsWith("capture-hide:"))
   {
    var pos=root.TransformPoint(new Vector3(-88,1.65f,-4));var target=root.TransformPoint(new Vector3(-60,4,7));
    var forest=GameObject.Find("WorldMacro_AuthoredGeography")?.transform.Find("04_ForestExtent_PlaceholderClusters");
    var actors=Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Where(r=>r.enabled).ToArray();
    string[] names=command.StartsWith("capture-hide:")?command.Substring(13).Split(','):new[]{"Context_040_RenderOnly"};
    var hidden=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>r.enabled&&names.Contains(r.name)).ToArray();var label=command.Replace(':','_');
    try{foreach(var r in actors)r.enabled=false;if(forest!=null)forest.name="Diagnostic_Forest_Hidden";if(command!="capture-disabled")foreach(var r in hidden)r.enabled=false;var file=WorldMacroDressingProbe.Capture("NaturalCave_"+label,false,pos.x,pos.y,pos.z,target.x,target.y,target.z);File.Copy(file,WorldMacroNaturalCave.Output+"/"+label+".png",true);return file;}
    finally{foreach(var r in actors)r.enabled=true;foreach(var r in hidden)r.enabled=true;if(forest!=null)forest.name="04_ForestExtent_PlaceholderClusters";}
   }
   var origin=root.TransformPoint(new Vector3(-88,1.65f,-4));var ray=new Ray(origin,new Vector3(.91310761f,.01031270f,-.40758820f));var timer=System.Diagnostics.Stopwatch.StartNew();do{renderer.PrepareView(origin,4096,()=>{if(Prologue.PrologueAudit.CommitRatio()>.85f)throw new Exception("Commit guard");});}while(renderer.PendingChunks>0&&timer.Elapsed.TotalSeconds<6);
   var seen=new HashSet<long>();var lines=new List<string>();
   void Item(object item){long id=(long)Get(item,"Id");if(!seen.Add(id))return;int index=(int)Get(item,"Prototype");var proto=renderer.Sheet.Prototypes[index];if(proto.Category!=Sheet.Kind.Rock)return;var pos=(Vector3)Get(item,"Position");float scale=(float)Get(item,"Scale");float along=Vector3.Dot(pos-origin,ray.direction);float distance=Vector3.Cross(pos-origin,ray.direction).magnitude;float radius=proto.Size.magnitude*scale*.5f;if(along>0&&along<500&&distance<radius+3){bool excluded=renderer.Sheet.PreservedAreas.Any(a=>Sheet.Excludes(a,pos,proto.Category));lines.Add("id="+id+" proto="+proto.Id+" local="+root.InverseTransformPoint(pos)+" scale="+scale+" rayAlong="+along+" rayDistance="+distance+" radius="+radius+" excluded="+excluded);}}
   foreach(DictionaryEntry e in (IDictionary)Get(renderer,"streamChunks"))foreach(var item in (IEnumerable)Get(e.Value,"Items"))Item(item);
   foreach(DictionaryEntry e in (IDictionary)Get(renderer,"live"))foreach(string field in new[]{"Near","Grass","Far","Fixed","Cover"})if(Get(e.Value,field) is IEnumerable items)foreach(var item in items)Item(item);
   File.WriteAllLines(WorldMacroNaturalCave.Output+"/dressing_rock_audit.txt",lines);return string.Join("\n",lines);
  }
 }
}
