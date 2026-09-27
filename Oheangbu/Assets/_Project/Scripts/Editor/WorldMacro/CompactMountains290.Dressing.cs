using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string Dress290()
  {
   var scene=SceneManager.GetActiveScene();if(scene.path!=Scene290)throw new Exception("Candidate only");
   var roots=scene.GetRootGameObjects();var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var layout=s.MountainLayout;
   var art=Object.FindFirstObjectByType<CompactRebuildArtRenderer>();
   var oldDetail=GameObject.Find("Compact_MountainDetails_290");if(oldDetail!=null)Object.DestroyImmediate(oldDetail);
   var detail=new GameObject("Compact_MountainDetails_290");
   foreach(var mountain in layout.Mountains)Boardwalk290(mountain,detail.transform);
   var source=Asset290("Data/VegetationBefore.asset",()=>Object.Instantiate(art.Sheet));
   var sheet=Asset290("Data/MountainVegetation.asset",()=>Object.Instantiate(source));
   var terrain=GameObject.Find("Compact_Mountains_290").GetComponentsInChildren<MeshCollider>().Where(c=>c.name.StartsWith("Terrain_")).ToArray();
   var masks=new HashSet<Vector2Int>();Vector2Int Key(Vector3 p)=>new Vector2Int(Mathf.FloorToInt(p.x/2),Mathf.FloorToInt(p.z/2));
   foreach(var m in layout.Mountains){foreach(var path in new[]{m.MainPath,m.TemplePath,m.ReturnPath})foreach(var p in path){var k=Key(p);for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++)masks.Add(k+new Vector2Int(x,z));}}
   bool Zone(Vector3 p)=>terrain.Any(c=>p.x>=c.bounds.min.x&&p.x<=c.bounds.max.x&&p.z>=c.bounds.min.z&&p.z<=c.bounds.max.z);
   bool Floor(Vector3 p,out RaycastHit hit){hit=default;foreach(var c in terrain)if(p.x>=c.bounds.min.x&&p.x<=c.bounds.max.x&&p.z>=c.bounds.min.z&&p.z<=c.bounds.max.z&&c.Raycast(new Ray(new Vector3(p.x,1800,p.z),Vector3.down),out hit,2200))return true;return false;}
   bool Clear(Vector3 p)=>masks.Contains(Key(p))||layout.Mountains.Any(m=>Vector2.Distance(new Vector2(p.x,p.z),new Vector2(m.Temple.x,m.Temple.z))<34||Vector2.Distance(new Vector2(p.x,p.z),new Vector2(m.Summit.x,m.Summit.z))<20||Vector2.Distance(new Vector2(p.x,p.z),new Vector2(m.LiftBase.x,m.LiftBase.z))<4);
   var placements=new List<WorldMacroDressingSheetSO.FixedPlacement>();var removed=new HashSet<string>();var positions=new Dictionary<string,Vector3>();int moved=0;
   foreach(var old in source.FixedPlacements)
   {
    var p=JsonUtility.FromJson<WorldMacroDressingSheetSO.FixedPlacement>(JsonUtility.ToJson(old));
    if(Zone(p.Position))
    {
     if(Clear(p.Position)||!Floor(p.Position,out var hit)||hit.normal.y<.63f){removed.Add(p.Id);continue;}
     if(Mathf.Abs(p.Position.y-hit.point.y)>.1f){p.Position=hit.point;positions[p.Id]=hit.point;moved++;}
    }
    placements.Add(p);
   }
   // Add small habitat groups on supported soil pockets; keep fissures and approach sightlines empty.
   var species=sheet.Prototypes.Where(p=>p.Id.Contains("Pinus")||p.Id.Contains("Deparia")||p.Id.Contains("UlmusDavidiana")||p.Id.Contains("LowGroundFill")).ToArray();
   var rng=new System.Random(290);float R(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());int added=0;
   foreach(var m in layout.Mountains)for(int i=24;i<m.MainPath.Length-24;i+=13)
   {
    if(rng.NextDouble()<.4)continue;var p=m.MainPath[i];var side=Vector3.Cross(Vector3.up,(m.MainPath[i+1]-m.MainPath[i-1]).normalized);
    var centre=p+side*R(-14,14);for(int j=0;j<4;j++)
    {
     var at=centre+new Vector3(R(-3,3),0,R(-3,3));if(Clear(at)||!Floor(at,out var hit)||hit.normal.y<.66f)continue;
     var available=species.Where(v=>v.Category!=WorldMacroDressingSheetSO.Kind.Tree||hit.normal.y>.79f&&j==0).ToArray();if(available.Length==0)continue;
     var prototype=available[rng.Next(available.Length)];float scale=prototype.Category==WorldMacroDressingSheetSO.Kind.Tree?R(.55f,.9f):R(.5f,.9f);
     placements.Add(new WorldMacroDressingSheetSO.FixedPlacement{Id=m.Id+"_plant_"+i+"_"+j,ClusterId=m.Id,PrototypeId=prototype.Id,Position=hit.point,Euler=new Vector3(0,R(0,360),0),Scale=scale});added++;
    }
   }
   sheet.FixedPlacements=placements.ToArray();art.Sheet=sheet;art.Invalidate();
   var manifest=Object.FindFirstObjectByType<CompactRebuildSceneManifest>();manifest.Art=sheet;
   foreach(var t in roots.Where(g=>g!=null).SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray())
   {if(t==null)continue;if(removed.Contains(t.name))t.gameObject.SetActive(false);else if(positions.TryGetValue(t.name,out var position))t.position=position;}
   var grass=Object.FindFirstObjectByType<CompactGrassRenderer266>();int reprojected=0,culled=0;
   if(grass!=null)
   {
    var grassSource=Asset290("Data/GrassBefore.asset",()=>Object.Instantiate(grass.Field));
    var field=Asset290("Data/MountainGrass.asset",()=>Object.Instantiate(grassSource));
    field.Cells=new CompactGrassField266.Cell[grassSource.Cells.Length];field.Count=0;
    for(int c=0;c<grassSource.Cells.Length;c++)
    {
     var cell=grassSource.Cells[c];if(cell==null)continue;var seeds=new List<CompactGrassField266.Seed>();
     bool changes=terrain.Any(t=>t.bounds.Intersects(new Bounds(new Vector3(cell.Bounds.center.x,t.bounds.center.y,cell.Bounds.center.z),new Vector3(cell.Bounds.size.x,t.bounds.size.y,cell.Bounds.size.z))));
     if(!changes){field.Cells[c]=cell;field.Count+=cell.Seeds.Length;continue;}
     foreach(var old in cell.Seeds){var seed=old;if(Zone(seed.Position))
      {if(Clear(seed.Position)||!Floor(seed.Position,out var hit)||hit.normal.y<.76f){culled++;continue;}seed.Position=hit.point;seed.NormalXZ=new Vector2(hit.normal.x,hit.normal.z);reprojected++;}seeds.Add(seed);}
     var b=cell.Bounds;if(seeds.Count>0){float lo=seeds.Min(v=>v.Position.y),hi=seeds.Max(v=>v.Position.y);b.center=new Vector3(b.center.x,(lo+hi)*.5f,b.center.z);b.size=new Vector3(b.size.x,hi-lo+3,b.size.z);}
     field.Cells[c]=new CompactGrassField266.Cell{Bounds=b,Seeds=seeds.ToArray()};field.Count+=seeds.Count;
    }
    grass.Field=field;grass.Invalidate();EditorUtility.SetDirty(field);EditorUtility.SetDirty(grass);
   }
   foreach(var o in new Object[]{sheet,art,manifest})EditorUtility.SetDirty(o);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   string result=$"Moved fixed vegetation {moved}, removed {removed.Count}, added habitat plants {added}; grass reprojected {reprojected}, culled {culled}. Candidate assets only.";File.WriteAllText(O290+"/dressing.txt",result);return result;
  }
  static void Boardwalk290(CompactWorldLayoutSO.Mountain m,Transform parent)
  {
   var wood=MountainMaterial290(m.Realm,"Pier289_planks");var pole=MountainMaterial290(m.Realm,"Pier289_poles");
   var planks=Enumerable.Range(0,10).Select(i=>Import285("Pier289_planks_"+i)).ToArray();var poles=Enumerable.Range(0,3).Select(i=>Import285("Pier289_poles_"+i)).ToArray();
   var root=new GameObject(m.Id+"_boardwalk");root.transform.SetParent(parent,false);var rng=new System.Random(290+m.Realm.Length);
   float R(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());int start=(int)(m.MainPath.Length*.66f),end=start+15;
   var path=m.MainPath.Skip(start).Take(end-start+1).ToArray();float length=Length290(path);
   Vector3 At(float distance){for(int i=1;i<path.Length;i++){float span=Vector3.Distance(path[i-1],path[i]);if(distance<=span)return Vector3.Lerp(path[i-1],path[i],distance/span);distance-=span;}return path.Last();}
   Vector3 Side(float d){var tangent=At(Mathf.Min(length,d+.1f))-At(Mathf.Max(0,d-.1f));tangent.y=0;return Vector3.Cross(Vector3.up,tangent.normalized);}
   int index=0;for(float d=0;d<length;)
   {
    float span=Mathf.Min(R(.18f,.29f),length-d),mid=d+span*.5f;var p=At(mid);var go=MeshObject278("Reclaimed_plank_"+index,planks[index++%10],wood,root.transform,false);
    go.transform.position=p-Vector3.up*.035f+Side(mid)*R(-.07f,.07f);go.transform.rotation=Quaternion.LookRotation(Vector3.Cross(Side(mid),Vector3.up))*Quaternion.Euler(R(-1,1),R(-1,1),R(-1,1));go.transform.localScale=new Vector3(R(2.8f,3.1f),.1f,span+.035f);d+=span;
   }
   void Beam(Vector3 a,Vector3 b,float diameter){var go=MeshObject278("Timber_support",poles[index++%3],pole,root.transform,false);go.transform.position=(a+b)*.5f;go.transform.rotation=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);go.transform.localScale=new Vector3(diameter,(b-a).magnitude,diameter);}
   Vector3? prior=null;for(float d=0;d<=length;d+=R(1.2f,1.8f))
   {
    var p=At(d);var side=Side(d);var tip=p-side*1.45f+Vector3.up*R(.85f,1.05f);Beam(p-side*1.5f-Vector3.up*.5f,tip,.13f);if(prior.HasValue)Beam(prior.Value,tip,.11f);prior=tip;
    Beam(p+side*1.7f-Vector3.up*2.7f,p-side*1.4f-Vector3.up*.25f,.22f);Beam(p+side*1.4f-Vector3.up*.22f,p-side*1.5f-Vector3.up*.22f,.18f);
   }
   // Continuous original trail collision remains below these reclaimed boards; visual seams cannot snag a controller.
   var group=root.AddComponent<LODGroup>();group.SetLODs(new[]{new LOD(.004f,root.GetComponentsInChildren<Renderer>())});group.RecalculateBounds();
  }
 }
}
