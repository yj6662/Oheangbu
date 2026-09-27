using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class ReproductionRow296 {public string Id,Hash;}
  [Serializable] sealed class ReproductionStamp296 {public string Scene,Scope;public ReproductionRow296[] Rows;}
  static string Reproduction296(string action)
  {
   RequireClean292();if(SceneManager.GetActiveScene().path!=Scene296)throw new Exception("Saved296 scene required");
   var rows=new List<ReproductionRow296>();
   void Add(string id,string value)=>rows.Add(new ReproductionRow296{Id=id,Hash=HashArchitecture296(Encoding.UTF8.GetBytes(value))});
   var sceneRoots=SceneManager.GetActiveScene().GetRootGameObjects();
   var legacy=sceneRoots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>t.name=="Architecture296_LegacySurface"&&!t.root.name.StartsWith("Architecture296_",StringComparison.Ordinal)).ToArray();
   var roots=sceneRoots.Where(g=>g.name.StartsWith("Architecture296_",StringComparison.Ordinal)&&!g.name.Contains("Navigation"))
    .Concat(legacy.Select(t=>t.gameObject)).Concat(Components295<Oheangbu.App.Demo.SouthGateDoorPresentation>().Select(g=>g.transform.root.gameObject))
    .Distinct().OrderBy(g=>ReproductionPath296(g.transform),StringComparer.Ordinal).ToArray();
   foreach(var actor in Session292().Actors.Where(a=>a!=null))
    Add("actor-placement:"+actor.Id+":"+ReproductionPath296(actor.transform),JsonUtility.ToJson(new ReproductionTransform296{Position=actor.transform.localPosition,Rotation=actor.transform.localRotation,Scale=actor.transform.localScale,Active=actor.gameObject.activeSelf}));
   foreach(var child in legacy)
   {
    var parent=child.parent;var source=parent.GetComponent<Renderer>();
    Add("legacy-source:"+ReproductionPath296(parent),JsonUtility.ToJson(new ReproductionTransform296{Position=parent.localPosition,Rotation=parent.localRotation,Scale=parent.localScale,Active=parent.gameObject.activeSelf})+"|renderer="+(source!=null&&source.enabled));
   }
   var meshPaths=new HashSet<string>();var materialPaths=new HashSet<string>();
   foreach(var root in roots)foreach(var t in root.GetComponentsInChildren<Transform>(true).OrderBy(t=>ReproductionPath296(t),StringComparer.Ordinal))
   {
    string id=ReproductionPath296(t);Add("transform:"+id,JsonUtility.ToJson(new ReproductionTransform296{Position=t.localPosition,Rotation=t.localRotation,Scale=t.localScale,Active=t.gameObject.activeSelf}));
    var filter=t.GetComponent<MeshFilter>();if(filter!=null&&filter.sharedMesh!=null){string path=AssetDatabase.GetAssetPath(filter.sharedMesh);Add("mesh-binding:"+id,path);meshPaths.Add(path);}
    var renderer=t.GetComponent<Renderer>();if(renderer!=null){var paths=renderer.sharedMaterials.Select(AssetDatabase.GetAssetPath).ToArray();Add("materials:"+id,string.Join("|",paths));Add("renderer-enabled:"+id,renderer.enabled.ToString());foreach(var p in paths)materialPaths.Add(p);}
    var group=t.GetComponent<LODGroup>();if(group!=null)Add("lod:"+id,JsonUtility.ToJson(new ReproductionLod296{Size=group.size,Centre=group.localReferencePoint,Fade=(int)group.fadeMode,Animate=group.animateCrossFading,Levels=group.GetLODs().Select(l=>new ReproductionLodLevel296{Height=l.screenRelativeTransitionHeight,Width=l.fadeTransitionWidth,Renderers=l.renderers.Select(r=>r==null?"null":ReproductionPath296(r.transform)).ToArray()}).ToArray()}));
    int ci=0;foreach(var collider in t.GetComponents<Collider>())
    {
     var c=new ReproductionCollider296{Type=collider.GetType().Name,Enabled=collider.enabled,Trigger=collider.isTrigger,Layer=collider.gameObject.layer,Material=AssetDatabase.GetAssetPath(collider.sharedMaterial)};
     if(collider is MeshCollider m){c.Mesh=AssetDatabase.GetAssetPath(m.sharedMesh);if(m.sharedMesh!=null)meshPaths.Add(c.Mesh);c.Convex=m.convex;c.Cooking=(int)m.cookingOptions;}
     else if(collider is BoxCollider box){c.Centre=box.center;c.Size=box.size;}
     else if(collider is SphereCollider sphere){c.Centre=sphere.center;c.Radius=sphere.radius;}
     else if(collider is CapsuleCollider capsule){c.Centre=capsule.center;c.Radius=capsule.radius;c.Height=capsule.height;c.Direction=capsule.direction;}
     Add("collider:"+id+":"+ci++,JsonUtility.ToJson(c));
    }
   }
   foreach(string path in meshPaths.Concat(materialPaths).Where(p=>p.StartsWith(A296+"/",StringComparison.Ordinal)).Distinct().OrderBy(p=>p,StringComparer.Ordinal))
    rows.Add(new ReproductionRow296{Id="asset:"+path,Hash=HashArchitecture296(File.ReadAllBytes(path))});
   var sheet=Sheet296();
   foreach(var arena in sheet.Arenas.OrderBy(a=>a.Id,StringComparer.Ordinal))Add("arena:"+arena.Id,JsonUtility.ToJson(arena));
   foreach(var structure in sheet.Structures.OrderBy(a=>a.Id,StringComparer.Ordinal))Add("structure:"+structure.Id,JsonUtility.ToJson(structure));
   foreach(var perimeter in sheet.Perimeters.OrderBy(a=>a.Id,StringComparer.Ordinal))Add("perimeter:"+perimeter.Id,JsonUtility.ToJson(perimeter));
   Add("candidate-height",HashArchitecture296(Session292().MountainLayout.FinalSurface.bytes));
   Add("candidate-water",HashArchitecture296(Session292().MountainLayout.Hydrology.WaterLevels.bytes));
   foreach(string file in new[]{"routes.json","crossings.json"})
    rows.Add(new ReproductionRow296{Id="generated-control:"+file,Hash=HashArchitecture296(File.ReadAllBytes(G296+"/"+file))});
   foreach(var data in new UnityEngine.Object[]{Session292().MountainLayout,Session292().Content})
   {string file=AssetDatabase.GetAssetPath(data);rows.Add(new ReproductionRow296{Id="candidate-data:"+file,Hash=HashArchitecture296(File.ReadAllBytes(file))});}
   // Include terrain/landscape materials, map/arrival catalogs and support
   // profiles outside the generated architecture hierarchy. Navigation is a
   // separately baked artifact and is not regenerated by the build command.
   foreach(string path in Directory.GetFiles(A296,"*",SearchOption.AllDirectories).Select(p=>p.Replace('\\','/')).OrderBy(p=>p,StringComparer.Ordinal))
   {
    string extension=Path.GetExtension(path).ToLowerInvariant();
    if(!new[]{".asset",".mat",".shader",".hlsl",".png",".bytes"}.Contains(extension))continue;
    if(extension==".asset"&&AssetDatabase.GetMainAssetTypeAtPath(path)==typeof(UnityEngine.AI.NavMeshData))continue;
    using(var stream=File.OpenRead(path))using(var hash=System.Security.Cryptography.SHA256.Create())
     rows.Add(new ReproductionRow296{Id="candidate-file:"+path,Hash=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant()});
   }
   var stamp=new ReproductionStamp296{Scene=Scene296,Scope="Generated296 hierarchy/transforms/LOD/colliders plus all private data, materials, meshes, textures and shader files, legacy render replacements, route controls and unchanged terrain fields. Separately baked NavMeshData, scene serialization IDs and session instance IDs excluded.",Rows=rows.OrderBy(r=>r.Id,StringComparer.Ordinal).ToArray()};
   string baseline=O296+"/reproduction-baseline.json";
   if(action=="record"){File.WriteAllText(baseline,JsonUtility.ToJson(stamp,true));return "Recorded "+rows.Count+" deterministic output entries";}
   if(action!="verify")throw new ArgumentException(action);
   var before=JsonUtility.FromJson<ReproductionStamp296>(File.ReadAllText(baseline));
   var a=before.Rows.ToDictionary(r=>r.Id,r=>r.Hash);var b=stamp.Rows.ToDictionary(r=>r.Id,r=>r.Hash);
   var changed=a.Keys.Union(b.Keys).Where(k=>!a.TryGetValue(k,out var x)||!b.TryGetValue(k,out var y)||x!=y).OrderBy(k=>k,StringComparer.Ordinal).ToArray();
   string result=(changed.Length==0?"PASS":"FAIL")+" deterministic regeneration: entries="+b.Count+" changed/missing/added="+changed.Length+"\n"+string.Join("\n",changed);
   File.WriteAllText(O296+"/reproduction-check.txt",result);File.WriteAllText(O296+"/reproduction-current.json",JsonUtility.ToJson(stamp,true));return result;
  }
  [Serializable] sealed class ReproductionTransform296 {public Vector3 Position,Scale;public Quaternion Rotation;public bool Active;}
  [Serializable] sealed class ReproductionLod296 {public float Size;public Vector3 Centre;public int Fade;public bool Animate;public ReproductionLodLevel296[] Levels;}
  [Serializable] sealed class ReproductionLodLevel296 {public float Height,Width;public string[] Renderers;}
  static string ReproductionPath296(Transform t)
  {
   string value=t.name+(t.parent!=null?"["+t.GetSiblingIndex()+"]":"");
   while(t.parent!=null){t=t.parent;value=t.name+(t.parent!=null?"["+t.GetSiblingIndex()+"]":"")+"/"+value;}
   return value;
  }
  [Serializable] sealed class ReproductionCollider296 {public string Type,Mesh,Material;public bool Enabled,Trigger,Convex;public Vector3 Centre,Size;public float Radius,Height;public int Layer,Cooking,Direction;}
 }
}
