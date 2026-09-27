using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
using Dress295=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class WaterMesh295{public string Id;public Vector3[] Vertices;public int[] Triangles;}
  [Serializable] sealed class Bank295{public Vector3 Position;public string Type;public float Flow;public int Side;}
  [Serializable] sealed class BankDocument295{public Bank295[] Banks;}
  static GameObject Root295(string name)=>Session292().gameObject.scene.GetRootGameObjects().FirstOrDefault(g=>g.name==name);
  static void Water295(CompactWorldSurface field,CompactHydrologySO hydro,List<string> report)
  {
   foreach(string name in new[]{"Reworld292_Water","River294"}){var old=Root295(name);if(old!=null)old.SetActive(false);}
   var previous=Root295("Watershed295_Water");if(previous!=null)Object.DestroyImmediate(previous);
   var root=new GameObject("Watershed295_Water");
   var material=Asset295("Materials/Water.mat",()=>new Material(AssetDatabase.LoadAssetAtPath<Material>(A292+"/River294/Materials/River.mat")));
   material.SetColor("_Deep",new Color(.045f,.09f,.095f));material.SetColor("_Sky",new Color(.38f,.46f,.46f));material.SetColor("_Shallow",new Color(.24f,.28f,.23f));EditorUtility.SetDirty(material);
   var triangles=new List<WorldTerrainQuery.WaterTriangle>();
   var channels=new Dictionary<Vector2Int,List<(CompactHydrologySO.WaterRow a,CompactHydrologySO.WaterRow b)>>();
   foreach(var reach in hydro.Reaches)for(int i=1;i<reach.Rows.Length;i++)
   {
    var a=reach.Rows[i-1];var b=reach.Rows[i];float margin=Mathf.Max(a.LeftWidth,a.RightWidth,b.LeftWidth,b.RightWidth)+8;
    for(int x=Mathf.FloorToInt((Mathf.Min(a.Position.x,b.Position.x)-margin)/128);x<=Mathf.FloorToInt((Mathf.Max(a.Position.x,b.Position.x)+margin)/128);x++)
    for(int z=Mathf.FloorToInt((Mathf.Min(a.Position.z,b.Position.z)-margin)/128);z<=Mathf.FloorToInt((Mathf.Max(a.Position.z,b.Position.z)+margin)/128);z++)
    {var key=new Vector2Int(x,z);if(!channels.TryGetValue(key,out var list))channels[key]=list=new List<(CompactHydrologySO.WaterRow,CompactHydrologySO.WaterRow)>();list.Add((a,b));}
   }
   var files=hydro.ChunkFiles!=null&&hydro.ChunkFiles.Length>0?hydro.ChunkFiles:Directory.GetFiles(G295+"/Water","*.json").Select(p=>p.Substring(G295.Length+1)).ToArray();
   foreach(string file in files)
   {
    var data=JsonUtility.FromJson<WaterMesh295>(File.ReadAllText(G295+"/"+file));
    if(data.Vertices==null||data.Triangles==null||data.Triangles.Length==0)continue;
    string id=string.IsNullOrEmpty(data.Id)?Path.GetFileNameWithoutExtension(file):data.Id;
    var mesh=Asset295("WaterMeshes/"+id+".asset",()=>new Mesh());mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;
    mesh.vertices=data.Vertices;mesh.triangles=data.Triangles;
    var uv=new Vector2[data.Vertices.Length];var flow=new Vector2[uv.Length];var colors=new Color[uv.Length];
    for(int i=0;i<uv.Length;i++)
    {
     var p=data.Vertices[i];float depth=Mathf.Max(0,p.y-field.Sample(p.x,p.z));
     bool lake=Mathf.Abs(p.y-hydro.Lake.Level)<.005f&&p.z>4400;
     uv[i]=new Vector2(p.x,p.z);flow[i]=new Vector2(lake?.15f:1,lake?0:.02f);
     if(!lake&&channels.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x/128),Mathf.FloorToInt(p.z/128)),out var local))
     {
      float nearest=float.PositiveInfinity;var at=new Vector2(p.x,p.z);
      foreach(var pair in local)
      {
       var a=pair.a.Position;var b=pair.b.Position;var start=new Vector2(a.x,a.z);var delta=new Vector2(b.x-a.x,b.z-a.z);float length=delta.magnitude;if(length<.001f)continue;
       var direction=delta/length;float along=Mathf.Clamp(Vector2.Dot(at-start,direction),0,length);float distance=(at-start-direction*along).sqrMagnitude;
       if(distance>=nearest)continue;nearest=distance;
       uv[i]=new Vector2(Vector2.Dot(at-start,new Vector2(direction.y,-direction.x)),pair.a.Distance+along);
       flow[i]=new Vector2(Mathf.Lerp(pair.a.Flow,pair.b.Flow,along/length),Mathf.Abs(b.y-a.y)/length);
      }
     }
     colors[i]=new Color(Mathf.Clamp01(depth/1.5f),0,Mathf.Clamp01(depth*3),1);
    }
    mesh.uv=uv;mesh.uv2=flow;mesh.colors=colors;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
    var go=MeshObject278(id,mesh,material,root.transform,false);go.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
    for(int i=0;i<data.Triangles.Length;i+=3)triangles.Add(new WorldTerrainQuery.WaterTriangle{A=data.Vertices[data.Triangles[i]],B=data.Vertices[data.Triangles[i+1]],C=data.Vertices[data.Triangles[i+2]]});
   }
   foreach(var query in Session292().gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldTerrainQuery>(true)))
   {query.Water=triangles.ToArray();query.Reindex();EditorUtility.SetDirty(query);}
   report.Add("Water chunks="+files.Length+", exact render/query triangles="+triangles.Count+", lake level="+hydro.Lake.Level.ToString("F2"));
  }
  static void Population295(CompactWorldSurface oldField,CompactWorldSurface field,CompactHydrologySO hydro,Material ground,List<string> report)
  {
   var session=Session292();var roots=session.gameObject.scene.GetRootGameObjects();
   var query=roots.SelectMany(g=>g.GetComponentsInChildren<WorldTerrainQuery>(true)).First();
   var precinct=Root295("Watershed295_SunkenCapital");
   bool WithinPrecinct(Vector3 p)
   {if(precinct==null)return false;var q=precinct.transform.InverseTransformPoint(p);return q.x>-78&&q.x<76&&q.z>-80&&q.z<76;}
   var source=AssetDatabase.LoadAssetAtPath<Dress295>(A292+"/Data/41ed54300d9ef754a8e74defc2ab189a_MountainVegetation.asset");
   var renderer=Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None).First(a=>a.Sheet!=null&&a.Sheet.FixedPlacements.Length>50000);
   var sheet=Asset295("Dressing/DryLandscape.asset",()=>ScriptableObject.CreateInstance<Dress295>());EditorUtility.CopySerialized(source,sheet);
   var vegetation=new HashSet<string>(source.Prototypes.Where(p=>p.Category==Dress295.Kind.Tree||p.Category==Dress295.Kind.Shrub||p.Category==Dress295.Kind.Grass).Select(p=>p.Id));
   var trees=new HashSet<string>(source.Prototypes.Where(p=>p.Category==Dress295.Kind.Tree).Select(p=>p.Id));
   var prototypes=source.Prototypes.ToDictionary(p=>p.Id);
   var instances=new List<Dress295.FixedPlacement>();int removed=0;
   foreach(var sourceItem in source.FixedPlacements)
   {
    var p=sourceItem.Position;float y=field.Sample(p.x,p.z),delta=y-oldField.Sample(p.x,p.z);
    bool changed=Mathf.Abs(delta)>.15f;bool nearWater=query.TryWaterHeight(new Vector3(p.x,y,p.z),out float water);
    if(vegetation.Contains(sourceItem.PrototypeId)&&(WithinPrecinct(p)||(nearWater&&water>y+.08f)||changed&&field.Normal(p.x,p.z).y<(trees.Contains(sourceItem.PrototypeId)?.87f:.68f))){removed++;continue;}
    var placed=p+Vector3.up*delta;var prototype=prototypes[sourceItem.PrototypeId];
    if(changed&&prototype.Category==Dress295.Kind.Rock&&prototype.GroundPoints!=null&&prototype.GroundPoints.Length>0)
    {
     var matrix=Matrix4x4.TRS(placed,Quaternion.Euler(sourceItem.Euler),Vector3.one*sourceItem.Scale);
     var offsets=prototype.GroundPoints.Select(q=>matrix.MultiplyPoint3x4(q)).Select(q=>field.Sample(q.x,q.z)-q.y).OrderBy(v=>v).ToArray();
     placed.y+=offsets[offsets.Length/2]-.12f*sourceItem.Scale;
    }
    instances.Add(new Dress295.FixedPlacement{Id=sourceItem.Id,ClusterId=sourceItem.ClusterId,PrototypeId=sourceItem.PrototypeId,Position=placed,Euler=sourceItem.Euler,Scale=sourceItem.Scale});
   }
   EnhanceDressing295(oldField,field,hydro,query,source,instances,ground,WithinPrecinct,report);
   sheet.FixedPlacements=instances.ToArray();EditorUtility.SetDirty(sheet);renderer.Sheet=sheet;renderer.Invalidate();
   var previous=Root295("Watershed295_Banks");if(previous!=null)Object.DestroyImmediate(previous);
   var bankRoot=new GameObject("Watershed295_Banks");var banks=Asset295("Dressing/Banks.asset",()=>ScriptableObject.CreateInstance<Dress295>());
   var bankSource=AssetDatabase.LoadAssetAtPath<Dress295>(A292+"/River294/Dressing.asset");EditorUtility.CopySerialized(bankSource,banks);
   var records=JsonUtility.FromJson<BankDocument295>(File.ReadAllText(G295+"/hydro.json")).Banks??Array.Empty<Bank295>();
   var placements=new List<Dress295.FixedPlacement>();var random=new System.Random(295);
   const int w=2001,h=3001;var pixels=new Color32[w*h];
   float R(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
   bool Clear(Vector2 p)=>!WithinPrecinct(new Vector3(p.x,0,p.y))&&RiverClear294(session.MountainLayout,p,3)&&session.MountainLayout.SurfaceDistribution.GetPixelBilinear(p.x/4000,p.y/6000).a<.05f;
   void Add(int proto,Vector2 p,float scale,float sink)
   {
    float y=field.Sample(p.x,p.y);if(!Clear(p)||field.Normal(p.x,p.y).y<.58f||query.TryWaterHeight(new Vector3(p.x,y,p.y),out float water)&&water-y>.22f)return;
    placements.Add(new Dress295.FixedPlacement{Id="watershed295_bank_"+placements.Count,PrototypeId=banks.Prototypes[proto].Id,Position=new Vector3(p.x,y-sink,p.y),Euler=new Vector3(0,R(0,360),0),Scale=scale});
   }
   foreach(var record in records)
   {
    // Lake raster shoreline samples are usually still submerged. Their actual dry edge is dressed below.
    if(record.Flow<=.001f&&Mathf.Abs(record.Position.y-hydro.Lake.Level)<.05f)continue;
    var point=new Vector2(record.Position.x,record.Position.z);string kind=(record.Type??"").ToLowerInvariant();
    bool rock=kind.Contains("rock")||kind.Contains("cliff")||kind.Contains("outer"),mud=kind.Contains("mud")||kind.Contains("bay")||kind.Contains("marsh");
    float spread=rock?2.5f:mud?7:5;
    for(int z=Mathf.Max(0,(int)((point.y-spread)/2));z<=Mathf.Min(h-1,(int)((point.y+spread)/2));z++)
    for(int x=Mathf.Max(0,(int)((point.x-spread)/2));x<=Mathf.Min(w-1,(int)((point.x+spread)/2));x++)
    {
     var p=new Vector2(x*2,z*2);if(!Clear(p))continue;float a=1-Mathf.SmoothStep(.1f,1,Vector2.Distance(p,point)/spread);
     byte strength=(byte)Mathf.RoundToInt(a*255);int index=z*w+x;
     if(strength>pixels[index].r)pixels[index]=new Color32(strength,(byte)(strength*(mud?.85f:.2f)),0,255);
    }
    // Large quiet spaces separate clumps. Placement depends on bank type rather than equal river distance.
    float clump=Mathf.PerlinNoise(point.x*.013f+21,point.y*.013f+8);if(clump<.53f||random.NextDouble()>.38)continue;
    int count=rock?random.Next(2,5):mud?random.Next(4,10):random.Next(1,3);
    for(int k=0;k<count;k++)
    {
     var p=point+new Vector2(R(-spread,spread),R(-spread,spread));
     if(rock)Add(random.Next(3),p,R(.3f,1.1f),.12f);
     else if(mud)Add(3+random.Next(3),p,R(.5f,.95f),.03f);
     else if(random.NextDouble()<.45)Add(random.Next(3),p,R(.18f,.45f),.08f);
    }
    if(mud&&random.NextDouble()<.035)Add(6,point+new Vector2(R(-9,9),R(-9,9)),R(.7f,1),.06f);
   }
   DressLakeShore295(field,hydro,query,banks,placements,pixels,w,h,Clear,report);
   banks.FixedPlacements=placements.ToArray();EditorUtility.SetDirty(banks);
   var art=bankRoot.AddComponent<CompactRebuildArtRenderer>();art.Sheet=banks;art.Observer=renderer.Observer;art.Contacts=renderer.Contacts;art.Invalidate();
   UpdateCanopyDressing295(oldField,field,hydro,source,instances,ground,report);
   ToneGround295(oldField,field,hydro,ground,report);
   var mask=Asset295("Dressing/BankMask.asset",()=>new Texture2D(w,h,TextureFormat.RGBA32,true,true));mask.SetPixels32(pixels);mask.Apply(true,false);mask.wrapMode=TextureWrapMode.Clamp;mask.filterMode=FilterMode.Bilinear;EditorUtility.SetDirty(mask);
   ground.SetTexture("_BankMask294",mask);ground.SetFloat("_BankStrength294",1);EditorUtility.SetDirty(ground);
   int grassCount=0,grassRemoved=0;
   var grassSources=AssetDatabase.FindAssets("t:CompactGrassField266",new[]{A292}).Select(g=>AssetDatabase.LoadAssetAtPath<CompactGrassField266>(AssetDatabase.GUIDToAssetPath(g))).Where(g=>g!=null).ToArray();
   foreach(var grass in roots.Where(g=>g!=null).SelectMany(g=>g.GetComponentsInChildren<CompactGrassRenderer266>(true)).Where(g=>g.Field!=null))
   {
    string copiedName=Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(grass.Field));
    var original=grassSources.FirstOrDefault(g=>g.name==copiedName||copiedName.EndsWith("_"+g.name,StringComparison.Ordinal));
    if(original==null&&copiedName.Length>32)original=AssetDatabase.LoadAssetAtPath<CompactGrassField266>(AssetDatabase.GUIDToAssetPath(copiedName.Substring(0,32)));
    if(original==null||!AssetDatabase.GetAssetPath(original).StartsWith(A292+"/"))throw new Exception("Immutable grass source missing for "+grass.Field.name);
    var copy=Asset295("Dressing/Grass_"+original.name+".asset",()=>ScriptableObject.CreateInstance<CompactGrassField266>());EditorUtility.CopySerialized(original,copy);copy.Count=0;
    foreach(var cell in copy.Cells)
    {
     if(cell==null)continue;var seeds=new List<CompactGrassField266.Seed>();
     foreach(var seed in cell.Seeds)
     {
      var p=seed.Position;float y=field.Sample(p.x,p.z),delta=y-oldField.Sample(p.x,p.z);var normal=field.Normal(p.x,p.z);
      if(WithinPrecinct(p)||query.TryWaterHeight(new Vector3(p.x,y,p.z),out float water)&&water>y-.06f||Mathf.Abs(delta)>.15f&&normal.y<.75f){grassRemoved++;continue;}
      seeds.Add(new CompactGrassField266.Seed{Position=p+Vector3.up*delta,NormalXZ=new Vector2(normal.x,normal.z)});
     }
     cell.Seeds=seeds.ToArray();copy.Count+=seeds.Count;
     if(seeds.Count>0){var bounds=new Bounds(seeds[0].Position,Vector3.one*2);foreach(var seed in seeds)bounds.Encapsulate(seed.Position+Vector3.up);bounds.Expand(2);cell.Bounds=bounds;}
    }
    grassCount+=copy.Count;grass.Field=copy;grass.Invalidate();EditorUtility.SetDirty(copy);EditorUtility.SetDirty(grass);
   }
   report.Add("Private ground grass seeds="+grassCount+", submerged/steep omitted="+grassRemoved);
   report.Add("Landscape reprojected="+instances.Count+", submerged/steep vegetation removed="+removed+", geomorphic bank samples="+records.Length+", clustered bank props="+placements.Count);
  }
 }
}
