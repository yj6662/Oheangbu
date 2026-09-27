using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string ContactOut265=Output+"/Contact265";
  static string ContactFolder265=>Path.GetDirectoryName(FrontageScene249().path).Replace('\\','/')+"/Contact265";
  static T ContactAsset265<T>(string name,Func<T> create)where T:Object{Directory.CreateDirectory(ContactFolder265);string p=ContactFolder265+"/"+name+".asset";var a=AssetDatabase.LoadAssetAtPath<T>(p);if(a==null){a=create();AssetDatabase.CreateAsset(a,p);}return a;}
  public static string Contact265(string command)
  {
   var scene=FrontageScene249();Directory.CreateDirectory(ContactOut265);
   if(command=="ruins")return Ruins265();
   if(command=="checks")return ContactChecks265();
   if(command=="bend-capture")return ContactBendCapture265();
   if(command!="build")throw new ArgumentException(command);
   if(scene.isDirty)throw new Exception("Clean candidate required");
   var roots=scene.GetRootGameObjects();var s=VillageSession();var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();
   var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>()).Single();
   var sheet=ContactAsset265("Placements",()=>Object.Instantiate(art.Sheet));
   // Clone only soft materials, leaving the canonical renderer and tree materials untouched.
   foreach(var p in sheet.Prototypes.Where(p=>p.Category==Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Grass||p.Category==Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Shrub))
    for(int l=0;l<p.Lods.Length;l++)for(int i=0;i<p.Lods[l].Parts.Length;i++){var part=p.Lods[l].Parts[i];var src=part.Material;var m=ContactAsset265(p.Id+"_"+l+"_"+i,()=>new Material(src));if(m.HasProperty("_ContactSoft265"))m.SetFloat("_ContactSoft265",1);EditorUtility.SetDirty(m);part.Material=m;}
   art.Sheet=sheet;manifest.Art=sheet;art.Invalidate();
   var hub=roots.SelectMany(g=>g.GetComponentsInChildren<CompactEnvironmentContact265>()).SingleOrDefault();if(hub==null)hub=new GameObject("EnvironmentContact265").AddComponent<CompactEnvironmentContact265>();
   hub.Session=s;hub.Art=art;art.Contacts=hub;hub.Sound=roots.SelectMany(g=>g.GetComponentsInChildren<CompactSoundscape255>()).Single();hub.Vehicle=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPalanquinController>(true)).SingleOrDefault();
   var palette=ContactAsset265("SoundPalette",()=>Object.Instantiate(hub.Sound.Palette));var entries=palette.Entries.Where(e=>!e.Id.EndsWith("265")).ToList();
   foreach(var pair in new[]{("contact_leaf265","step_grass",.32f),("contact_wood265","step_wood",.45f),("contact_stone265","step_stone",.40f)}){
    var cue=JsonUtility.FromJson<WorldMacroPlaytestAudioProfileSO.Cue>(JsonUtility.ToJson(palette.Find(pair.Item2)));cue.Volume=pair.Item3;cue.Cooldown=.38f;cue.MaxConcurrent=2;cue.SpatialBlend=1;entries.Add(new CompactSoundPalette255.Entry{Id=pair.Item1,Cue=cue});}
   palette.Entries=entries.ToArray();hub.Sound.Palette=palette;EditorUtility.SetDirty(palette);EditorUtility.SetDirty(hub.Sound);
   var rock=sheet.Prototypes.First(p=>p.Id=="Cheongrim_SM_Rock_K").Lods.Last().Parts[0];hub.ChipMesh=rock.Mesh;hub.ChipMaterial=rock.Material;
   var scopes=roots.Where(g=>g.name==DetailRoot261||g.name==PropsRoot264).ToArray();var before=scopes.SelectMany(g=>g.GetComponentsInChildren<MeshCollider>()).Where(c=>c.transform.parent==null||c.transform.parent.name!="RuinFabric265").ToArray();
   var times=new List<string>();
   double Probe(Collider[] colliders){var watch=Stopwatch.StartNew();for(int repeat=0;repeat<64;repeat++)foreach(var c in colliders){var b=c.bounds;c.Raycast(new Ray(b.center+Vector3.up*(b.extents.y+1),Vector3.down),out _,b.size.y+3);}watch.Stop();return watch.Elapsed.TotalMilliseconds;}
   var sample=before.Take(128).Cast<Collider>().ToArray();Probe(sample);times.Add("Warm 64 x "+sample.Length+" direct Collider.Raycast baseline ms="+Probe(sample).ToString("F3"));
   long triangles=before.Sum(c=>c.sharedMesh==null?0:(long)c.sharedMesh.triangles.Length/3);var replaced=new List<Collider>();
   foreach(var c in before){if(c.sharedMesh==null)continue;var b=c.sharedMesh.bounds;var go=c.gameObject;bool enabled=c.enabled;bool trigger=c.isTrigger;var mat=c.sharedMaterial;
    var box=go.AddComponent<BoxCollider>();box.center=b.center;box.size=new Vector3(Mathf.Max(.04f,b.size.x*.90f),Mathf.Max(.04f,b.size.y*.94f),Mathf.Max(.04f,b.size.z*.90f));box.enabled=enabled;box.isTrigger=trigger;box.sharedMaterial=mat;Object.DestroyImmediate(c);replaced.Add(box);}
   Physics.SyncTransforms();var after=replaced.Take(128).ToArray();Probe(after);times.Add("Warm 64 x "+after.Length+" direct Collider.Raycast simplified ms="+Probe(after).ToString("F3"));
   times.Add("Replaced MeshColliders="+replaced.Count+" sourceTriangles="+triangles);times.Add("Microbenchmark of direct collider queries only; no full physics frame, GPU timing, or 120fps claim. All blockers stay active; distance pooling deferred pending gameplay profiling.");if(before.Length>0)File.WriteAllLines(ContactOut265+"/collision-cost.txt",times);
   var placed=sheet.FixedPlacements.ToDictionary(p=>p.Id,p=>p.PrototypeId);
   bool IsWood(Collider c){if(c is CapsuleCollider)return true;for(var t=c.transform;t!=null;t=t.parent){string n=(placed.TryGetValue(t.name,out string id)?id:t.name).ToLowerInvariant();if(new[]{"wood","timber","plank","beam","roof","rafter","frame","cabin","lattice","chest","log","trestle","post"}.Any(n.Contains))return true;}return false;}
   hub.Surfaces=scopes.SelectMany(g=>g.GetComponentsInChildren<Collider>()).Where(c=>!c.isTrigger).Select(c=>new CompactEnvironmentContact265.Surface{Collider=c,Wood=IsWood(c)}).ToArray();
   var relay=s.Walker.Body.GetComponent<CompactContactRelay265>();if(relay==null)relay=s.Walker.Body.gameObject.AddComponent<CompactContactRelay265>();relay.Hub=hub;EditorUtility.SetDirty(relay);
   if(hub.Vehicle!=null){relay=hub.Vehicle.GetComponent<CompactContactRelay265>();if(relay==null)relay=hub.Vehicle.gameObject.AddComponent<CompactContactRelay265>();relay.Hub=hub;EditorUtility.SetDirty(relay);}
   foreach(var o in new Object[]{hub,art,manifest,sheet})EditorUtility.SetDirty(o);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return string.Join("\n",times)+"\nRegistered contact surfaces="+hub.Surfaces.Length;
  }
 }
}
