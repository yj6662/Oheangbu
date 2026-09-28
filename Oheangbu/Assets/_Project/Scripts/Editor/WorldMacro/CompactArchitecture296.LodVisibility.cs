using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class LodVisibilityMesh296
  {
   public string Renderer,Mesh,MeshSHA256,Shader;public int Vertices,Triangles,Submeshes,MaterialSlots;
   public bool Enabled,Active,ForceRenderingOff,CopySerializedFallback;public Bounds WorldBounds;
  }
  [Serializable] sealed class LodVisibilityGroup296
  {public string Path;public int RequestedLevel,ActualLevel,LevelCount;public bool Enabled;public LodVisibilityMesh296[] Meshes;}
  [Serializable] sealed class LodVisibilityShot296
  {
   public int Level,ChangedPixels,ChangeMinX,ChangeMinY,ChangeMaxX,ChangeMaxY;
   public string File,SHA256;public float ChangedRatioToLod0;public bool NearZeroContribution;
   public LodVisibilityGroup296[] Groups;
  }
  [Serializable] sealed class LodVisibilityView296
  {
   public string Id,TargetRoot,BackgroundFile,BackgroundSHA256;public View295 Camera;
   public LodVisibilityShot296[] Shots;
  }
  [Serializable] sealed class LodVisibilityReceipt296
  {
   public string Utc,Folder,SceneSHA256Before,SceneSHA256After,Error;
   public string Scope="Saved296 Edit only. Same seven fixed camera poses, target-hidden reference and forced LOD0/1/2. Changed pixels prove image contribution by the target as a whole; they do not prove visibility of every individual mesh or user art approval. No mesh upload, asset mutation or scene save.";
   public bool SceneFileUnchanged,ActiveStatesRestored,RendererStatesRestored,AutomaticLodRestored,SavedSceneReopened;
   public LodVisibilityView296[] Views;
  }
  sealed class LodVisibilityRendererState296
  {public Renderer Renderer;public bool Enabled,Forced;}
  sealed class LodVisibilityActiveState296
  {public GameObject Object;public bool Active;}

  public static string DiagnoseLodVisibility296()
  {
   var scene=SceneManager.GetActiveScene();
   if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=Scene296||scene.isDirty)
    throw new InvalidOperationException("LOD visibility diagnostic requires the saved clean296 Edit scene");
   string stamp=DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");
   string folder=O296+"/Analysis/LodVisibility/"+stamp;Directory.CreateDirectory(folder);
   var receipt=new LodVisibilityReceipt296{Utc=DateTime.UtcNow.ToString("O"),Folder=folder,SceneSHA256Before=HashArchitecture296(File.ReadAllBytes(Scene296))};
   var views=JsonUtility.FromJson<Views295>(File.ReadAllText(O296+"/cameras.json")).Views;
   var ids=new[]{"bridge-capital_shared_stone_bridge","bridge-hyeongang__temple_bridge","bridge-mountain_hwanggyeong_main_bridge","cheolong-entry","cheolong-eye","hwanggyeong-entry","hwanggyeong-eye"};
   var snapshot=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Select(t=>new LodVisibilityActiveState296{Object=t.gameObject,Active=t.gameObject.activeSelf}).ToArray();
   var rendererSnapshot=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).Select(r=>new LodVisibilityRendererState296{Renderer=r,Enabled=r.enabled,Forced=r.forceRenderingOff}).ToArray();
   var groups=new HashSet<LODGroup>();var groupEnabled=new Dictionary<LODGroup,bool>();var meshHashes=new Dictionary<string,string>();var results=new List<LodVisibilityView296>();
   var oldEye=eye293;var oldTarget=target293;var oldOutput=output293;bool oldRaw=raw293;
   try
   {
    foreach(string id in ids)
    {
     var view=views.Single(v=>v.Id==id);GameObject root;
     if(id.StartsWith("bridge-",StringComparison.Ordinal))root=Root296("Architecture296_Crossings/"+id.Substring(7));
     else
     {
      string realm=id.Substring(0,id.IndexOf('-'));
      var arena=Sheet296().Arenas.Single(a=>string.Equals(a.Realm,realm,StringComparison.OrdinalIgnoreCase));root=Root296(arena.SceneRoot);
     }
     if(root==null||!root.activeInHierarchy)throw new InvalidOperationException("Missing active target "+id);
     var targetGroups=root.GetComponentsInChildren<LODGroup>(true).Where(g=>g.gameObject.activeInHierarchy&&g.enabled).ToArray();
     if(targetGroups.Length==0||targetGroups.Any(g=>g.GetLODs().Length<3))throw new InvalidOperationException("Target needs three active LOD levels: "+id);
     foreach(var group in targetGroups){groups.Add(group);if(!groupEnabled.ContainsKey(group))groupEnabled[group]=group.enabled;}
     var targetRenderers=root.GetComponentsInChildren<Renderer>(true);var enabled=targetRenderers.Select(r=>r.enabled).ToArray();var forced=targetRenderers.Select(r=>r.forceRenderingOff).ToArray();
     var row=new LodVisibilityView296{Id=id,TargetRoot=ScenePathVenue296(root.transform),Camera=view,BackgroundFile=folder+"/"+id+"-hidden.png"};results.Add(row);
     foreach(var renderer in targetRenderers)renderer.forceRenderingOff=true;
     Shot(row.BackgroundFile,view);var background=ReadLodPixels296(row.BackgroundFile);row.BackgroundSHA256=HashArchitecture296(File.ReadAllBytes(row.BackgroundFile));
     for(int i=0;i<targetRenderers.Length;i++){targetRenderers[i].enabled=enabled[i];targetRenderers[i].forceRenderingOff=forced[i];}
     var shots=new List<LodVisibilityShot296>();row.Shots=Array.Empty<LodVisibilityShot296>();
     for(int level=0;level<3;level++)
     {
      foreach(var group in targetGroups)group.ForceLOD(level);
      string file=folder+"/"+id+"-lod"+level+".png";Shot(file,view);
      var item=new LodVisibilityShot296{Level=level,File=file,SHA256=HashArchitecture296(File.ReadAllBytes(file)),ChangeMinX=1920,ChangeMinY=1080,ChangeMaxX=-1,ChangeMaxY=-1,
       Groups=targetGroups.Select(g=>DescribeGroup(g,level)).ToArray()};
      var pixels=ReadLodPixels296(file);if(pixels.Length!=background.Length)throw new Exception("Capture dimensions changed");
      for(int p=0;p<pixels.Length;p++)
      {
       var a=pixels[p];var b=background[p];if(Math.Abs((int)a.r-b.r)+Math.Abs((int)a.g-b.g)+Math.Abs((int)a.b-b.b)<=9)continue;
       item.ChangedPixels++;int x=p%1920,y=p/1920;item.ChangeMinX=Math.Min(item.ChangeMinX,x);item.ChangeMaxX=Math.Max(item.ChangeMaxX,x);item.ChangeMinY=Math.Min(item.ChangeMinY,y);item.ChangeMaxY=Math.Max(item.ChangeMaxY,y);
      }
      item.NearZeroContribution=item.ChangedPixels<32;item.ChangedRatioToLod0=level==0?1:item.ChangedPixels/(float)Math.Max(1,shots[0].ChangedPixels);shots.Add(item);row.Shots=shots.ToArray();
     }
     foreach(var group in targetGroups)group.ForceLOD(-1);
    }
   }
   catch(Exception error){receipt.Error=error.ToString();}
   finally
   {
    foreach(var group in groups)if(group!=null){group.ForceLOD(-1);group.enabled=groupEnabled[group];}
    receipt.AutomaticLodRestored=groups.All(g=>g!=null);
    foreach(var state in rendererSnapshot)if(state.Renderer!=null){state.Renderer.enabled=state.Enabled;state.Renderer.forceRenderingOff=state.Forced;}
    foreach(var state in snapshot)if(state.Object!=null&&state.Object.activeSelf!=state.Active)state.Object.SetActive(state.Active);
    receipt.ActiveStatesRestored=snapshot.All(s=>s.Object!=null&&s.Object.activeSelf==s.Active);
    receipt.RendererStatesRestored=rendererSnapshot.All(s=>s.Renderer!=null&&s.Renderer.enabled==s.Enabled&&s.Renderer.forceRenderingOff==s.Forced);
    eye293=oldEye;target293=oldTarget;output293=oldOutput;raw293=oldRaw;
    receipt.SceneSHA256After=HashArchitecture296(File.ReadAllBytes(Scene296));receipt.SceneFileUnchanged=receipt.SceneSHA256Before==receipt.SceneSHA256After;
    // Discard transient editor state after explicitly restoring the active
    // objects and LOD controls. Never save temporary visibility switches.
    EditorSceneManager.OpenScene(Scene296);receipt.SavedSceneReopened=SceneManager.GetActiveScene().path==Scene296&&!SceneManager.GetActiveScene().isDirty;
    receipt.Views=results.ToArray();File.WriteAllText(folder+"/visibility.json",JsonUtility.ToJson(receipt,true));
    File.WriteAllText(O296+"/Analysis/LodVisibility/latest.json",JsonUtility.ToJson(receipt,true));
   }
   if(!string.IsNullOrEmpty(receipt.Error)||!receipt.SceneFileUnchanged||!receipt.ActiveStatesRestored||!receipt.RendererStatesRestored||!receipt.SavedSceneReopened)
    throw new InvalidOperationException("LOD visibility diagnostic or restoration failed; "+folder+"/visibility.json");
   int count=results.Sum(v=>v.Shots.Length),suspects=results.Sum(v=>v.Shots.Count(s=>s.NearZeroContribution||s.Level>0&&s.ChangedRatioToLod0<.4f));
   return count+" forced-LOD captures + "+results.Count+" hidden references; "+suspects+" low-contribution shots need visual inspection. Saved scene and renderer/active state restored. "+folder+"/visibility.json";

   void Shot(string file,View295 view)
   {eye293=view.Eye;target293=view.Target;output293=file;raw293=false;Capture292(6);}
   LodVisibilityGroup296 DescribeGroup(LODGroup group,int level)
   {
    var lods=group.GetLODs();return new LodVisibilityGroup296{Path=ScenePathVenue296(group.transform),RequestedLevel=level,ActualLevel=level,LevelCount=lods.Length,Enabled=group.enabled,
     Meshes=lods[level].renderers.Select(renderer=>
     {
      var filter=renderer.GetComponent<MeshFilter>();var mesh=filter!=null?filter.sharedMesh:null;string path=mesh!=null?AssetDatabase.GetAssetPath(mesh):"";
      if(!string.IsNullOrEmpty(path)&&!meshHashes.ContainsKey(path))meshHashes[path]=File.Exists(path)?HashArchitecture296(File.ReadAllBytes(path)):"missing-file";
      return new LodVisibilityMesh296{Renderer=ScenePathVenue296(renderer.transform),Mesh=path,MeshSHA256=string.IsNullOrEmpty(path)?"non-asset":meshHashes[path],
       Vertices=mesh!=null?mesh.vertexCount:0,Triangles=mesh!=null?Enumerable.Range(0,mesh.subMeshCount).Sum(i=>(int)mesh.GetIndexCount(i)/3):0,Submeshes=mesh!=null?mesh.subMeshCount:0,
       MaterialSlots=renderer.sharedMaterials.Length,Shader=string.Join(";",renderer.sharedMaterials.Select(m=>m!=null&&m.shader!=null?m.shader.name:"missing")),
       Enabled=renderer.enabled,Active=renderer.gameObject.activeInHierarchy,ForceRenderingOff=renderer.forceRenderingOff,WorldBounds=renderer.bounds,
       CopySerializedFallback=Regex.IsMatch(path,@"_LOD[12]_\d+\.asset$",RegexOptions.CultureInvariant)};
     }).ToArray()};
   }
  }
  static Color32[] ReadLodPixels296(string file)
  {
   var texture=new Texture2D(2,2,TextureFormat.RGB24,false);
   try{if(!texture.LoadImage(File.ReadAllBytes(file),false)||texture.width!=1920||texture.height!=1080)throw new Exception("Invalid LOD image "+file);return texture.GetPixels32();}
   finally{Object.DestroyImmediate(texture);}
  }
 }
}
