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
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string Dress292()
  {
   var session=Session292();var layout=session.MountainLayout;var surface=new CompactWorldSurface(layout);var mask=layout.SurfaceDistribution;
   var roots=session.gameObject.scene.GetRootGameObjects();var rows=new List<string>();
   Color Mask(float x,float z)=>mask.GetPixelBilinear(x/4000,z/6000);
   var done=new HashSet<WorldMacroDressingSheetSO>();
   foreach(var art in roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)))
   {
    var sheet=art.Sheet;if(sheet==null||!done.Add(sheet))continue;
    if(!AssetDatabase.GetAssetPath(sheet).StartsWith(A292+"/"))throw new Exception("Shared vegetation cannot be changed");
    var regional=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>("Assets/_Project/Art/World/WorldMacro/Playtest/VegetationPolish/Dressing_Dense.asset");
    if(regional!=null)
    {
     // Source prototypes carry Korean species and LOD geometry. Copy their data;
     // shared meshes/textures remain read-only and every material is private.
     foreach(var prototype in regional.Prototypes.Where(p=>p.Realm!=RealmId.Cheongrim&&!sheet.Prototypes.Any(q=>q.Id==p.Id)))
     {var copy=JsonUtility.FromJson<WorldMacroDressingSheetSO.Prototype>(JsonUtility.ToJson(prototype));sheet.Prototypes=sheet.Prototypes.Concat(new[]{copy}).ToArray();}
     foreach(var prototype in sheet.Prototypes)foreach(var level in prototype.Lods)foreach(var part in level.Parts)
     {if(part.Material==null||AssetDatabase.GetAssetPath(part.Material).StartsWith(A292+"/"))continue;var source=part.Material;string id=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));part.Material=Asset292("Materials/Vegetation_"+id+".mat",()=>new Material(source));part.Material.enableInstancing=true;}
    }
    sheet.TreeNear=110;sheet.TreeMiddle=300;
    var keep=new List<WorldMacroDressingSheetSO.FixedPlacement>();
    foreach(var p in sheet.FixedPlacements)
    {
     if(p.Id.StartsWith("293s_")){keep.Add(p);continue;}  // highland section plants are placed on their own meshes by sections293-dress
     var prototype=sheet.Prototypes.FirstOrDefault(q=>q.Id==p.PrototypeId);if(prototype==null)continue;
     var data=Mask(p.Position.x,p.Position.z);bool natural=prototype.Category!=WorldMacroDressingSheetSO.Kind.Prop;
     if(natural&&(data.a>.15f||data.r>.8f&&prototype.Category!=WorldMacroDressingSheetSO.Kind.Rock))continue;
     p.Position.y=surface.Sample(p.Position.x,p.Position.z)-(prototype.Category==WorldMacroDressingSheetSO.Kind.Rock?.13f:0);keep.Add(p);
    }
    // Only the primary sheet grows new canopy. Secondary sheets keep their own role.
    if(done.Count==1)
    {
     keep.RemoveAll(p=>p.Id.StartsWith("292_"));
     var trees=sheet.Prototypes.Where(p=>p.Category==WorldMacroDressingSheetSO.Kind.Tree&&p.Lods.Length>=2).ToArray();
     var rocks=sheet.Prototypes.Where(p=>p.Category==WorldMacroDressingSheetSO.Kind.Rock&&p.Lods.Length>0).ToArray();
     var random=new System.Random(292);
     for(float z=14;z<5990;z+=12)for(float x=14;x<3990;x+=12)
     {
      float px=x+(float)random.NextDouble()*9-4,pz=z+(float)random.NextDouble()*9-4;var m=Mask(px,pz);var n=surface.Normal(px,pz);
      if(m.a>.08f||n.y<.60f||m.r>.65f||layout.Places.Any(p=>Vector2.Distance(p.XZ,new Vector2(px,pz))<Mathf.Max(15,p.GroundRadius)))continue;
      float clump=Mathf.SmoothStep(0,1,Mathf.PerlinNoise(px*.006f,pz*.006f));if(random.NextDouble()>clump*1.25f)continue;
      var realmPlace=layout.Places.Where(p=>!string.IsNullOrEmpty(p.Realm)).OrderBy(p=>(p.XZ-new Vector2(px,pz)).sqrMagnitude).First();
      var region=layout.RealmAt(new Vector2(px,pz));Enum.TryParse<RealmId>(region!=null?region.Id:realmPlace.Realm,true,out var realm);
      if(region!=null&&random.NextDouble()>region.TreeDensity)continue;
      var choices=trees.Where(p=>p.Realm==realm&&surface.Sample(px,pz)<p.MaximumAltitude&&Vector3.Angle(n,Vector3.up)<p.MaximumSlope).ToArray();if(choices.Length==0)continue;var proto=choices[random.Next(choices.Length)];
      keep.Add(new WorldMacroDressingSheetSO.FixedPlacement{Id="292_tree_"+(int)x+"_"+(int)z,PrototypeId=proto.Id,Position=new Vector3(px,surface.Sample(px,pz),pz),Euler=new Vector3(0,(float)random.NextDouble()*360,0),Scale=Mathf.Lerp(.75f,1.18f,(float)random.NextDouble()),Preserve=true});
      if(rocks.Length>0&&m.g>.18f&&random.NextDouble()<.25){proto=rocks[random.Next(rocks.Length)];keep.Add(new WorldMacroDressingSheetSO.FixedPlacement{Id="292_talus_"+(int)x+"_"+(int)z,PrototypeId=proto.Id,Position=new Vector3(px+4,surface.Sample(px+4,pz)-.25f,pz),Euler=new Vector3(0,(float)random.NextDouble()*360,0),Scale=.7f+(float)random.NextDouble()*.6f});}
     }
     // #293: Cheongrim is the forest realm; two offset passes with their own random stream add clumped
     // canopy masses and clearings there only. Other realms keep the placements above unchanged.
     var forest=new System.Random(2931);
     foreach(var offset in new[]{new Vector2(6,6),new Vector2(6,0)})
     for(float z=14+offset.y;z<5990;z+=12)for(float x=14+offset.x;x<3990;x+=12)
     {
      float px=x+(float)forest.NextDouble()*8-4,pz=z+(float)forest.NextDouble()*8-4;var region=layout.RealmAt(new Vector2(px,pz));
      if(region==null||!region.Id.Equals("Cheongrim",StringComparison.OrdinalIgnoreCase))continue;
      var m=Mask(px,pz);var n=surface.Normal(px,pz);
      if(m.a>.08f||n.y<.60f||m.r>.65f||layout.Places.Any(p=>Vector2.Distance(p.XZ,new Vector2(px,pz))<Mathf.Max(15,p.GroundRadius)))continue;
      float clump=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.32f,.68f,Mathf.PerlinNoise(px*.009f+3.1f,pz*.009f+7.7f)));if(forest.NextDouble()>clump)continue;
      var choices=trees.Where(p=>p.Realm==RealmId.Cheongrim&&surface.Sample(px,pz)<p.MaximumAltitude&&Vector3.Angle(n,Vector3.up)<p.MaximumSlope).ToArray();if(choices.Length==0)continue;
      var proto=choices[forest.Next(choices.Length)];
      keep.Add(new WorldMacroDressingSheetSO.FixedPlacement{Id="292_forest_"+(int)x+"_"+(int)z,PrototypeId=proto.Id,Position=new Vector3(px,surface.Sample(px,pz),pz),Euler=new Vector3(0,(float)forest.NextDouble()*360,0),Scale=Mathf.Lerp(.72f,1.2f,(float)forest.NextDouble()),Preserve=true});
     }
    }
    sheet.FixedPlacements=keep.ToArray();sheet.SurfaceDeformation=null;
    foreach(var cell in sheet.Cells)
    {
     if(cell==null)continue;float ox=cell.X*sheet.CellSize,oz=cell.Z*sheet.CellSize;
     if(cell.Heights!=null){int side=Mathf.RoundToInt(Mathf.Sqrt(cell.Heights.Length));for(int z=0;z<side;z++)for(int x=0;x<side;x++)cell.Heights[z*side+x]=surface.Sample(ox+x*sheet.CellSize/(side-1f),oz+z*sheet.CellSize/(side-1f));}
     if(cell.FineHeights!=null&&cell.FineHeights.Length>0){int side=Mathf.RoundToInt(Mathf.Sqrt(cell.FineHeights.Length));for(int z=0;z<side;z++)for(int x=0;x<side;x++)cell.FineHeights[z*side+x]=surface.Sample(ox+x*sheet.CellSize/(side-1f),oz+z*sheet.CellSize/(side-1f));}
     cell.Centre.y=surface.Sample(cell.Centre.x,cell.Centre.z);if(cell.Heights!=null){cell.MinHeight=cell.Heights.Min();cell.MaxHeight=cell.Heights.Max();}
     if(cell.Habitat!=null){int side=Mathf.RoundToInt(Mathf.Sqrt(cell.Habitat.Length));for(int z=0;z<side;z++)for(int x=0;x<side;x++){float wx=ox+(x+.5f)*sheet.CellSize/side,wz=oz+(z+.5f)*sheet.CellSize/side;var m=Mask(wx,wz);int i=z*side+x;if(m.a>.08f||m.b>.88f)cell.Habitat[i]=0;else if(m.r>.65f)cell.Habitat[i]=(byte)(cell.Habitat[i]&67);}}
    }
    art.Invalidate();EditorUtility.SetDirty(sheet);rows.Add(sheet.name+" placements="+keep.Count);
   }
   foreach(var grass in roots.SelectMany(g=>g.GetComponentsInChildren<CompactGrassRenderer266>(true)))
   {
    var field=grass.Field;if(field==null)continue;if(!AssetDatabase.GetAssetPath(field).StartsWith(A292+"/"))throw new Exception("Shared grass field");int count=0;
    foreach(var cell in field.Cells)
    {
     if(cell==null)continue;var seeds=new List<CompactGrassField266.Seed>();
     foreach(var seed in cell.Seeds){var s=seed;var m=Mask(s.Position.x,s.Position.z);if(m.a>.1f||m.r>.65f||m.b>.85f)continue;
      s.Position.y=surface.Sample(s.Position.x,s.Position.z);var n=surface.Normal(s.Position.x,s.Position.z);s.NormalXZ=new Vector2(n.x,n.z);seeds.Add(s);}
     cell.Seeds=seeds.ToArray();count+=seeds.Count;if(seeds.Count>0){var b=new Bounds(seeds[0].Position,Vector3.one);foreach(var s in seeds)b.Encapsulate(s.Position+Vector3.up);cell.Bounds=b;}
    }
    field.Count=count;grass.Invalidate();EditorUtility.SetDirty(field);rows.Add("Grass grounded/excluded="+count);
   }
   Save292();File.WriteAllText(O292+"/dressing.txt",string.Join("\n",rows));return string.Join("\n",rows);
  }
  static string Temples292()
  {
   var session=Session292();var layout=session.MountainLayout;var surface=new CompactWorldSurface(layout);
   var previous=GameObject.Find("Reworld292_Sansa");if(previous!=null)Object.DestroyImmediate(previous);
   var root=new GameObject("Reworld292_Sansa");var rows=new List<string>();
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/JejumokGwana/Prefabs/Buildings/Honhwagak.prefab");if(prefab==null)throw new Exception("Korean source architecture is missing");
   var wood=AssetDatabase.LoadAssetAtPath<Material>(A285+"/Materials/Pier289_planks.mat");
   for(int i=0;i<layout.Mountains.Length;i++)
   {
    var m=layout.Mountains[i];var temple=new GameObject(m.Id+"_sansa");temple.transform.SetParent(root.transform,false);temple.transform.position=m.Temple;
    var hall=(GameObject)PrefabUtility.InstantiatePrefab(prefab,session.gameObject.scene);hall.transform.SetParent(temple.transform,false);hall.transform.localPosition=new Vector3(0,0,11);hall.transform.localRotation=Quaternion.Euler(0,180+(i%2)*12,0);hall.name="KoreanHall_SourceAdaptation";
    var bounds=new Bounds();bool first=true;
    foreach(var r in hall.GetComponentsInChildren<Renderer>()){if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);r.sharedMaterials=r.sharedMaterials.Select(mat=>{
      if(mat==null)return null;string id=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mat));var copy=Asset292("Materials/Sansa_"+id+".mat",()=>new Material(mat));
      copy.shader=Shader.Find("Oheangbu/Reworld292/KoreanArchitecture");copy.SetFloat("_Saturation",.28f);copy.SetFloat("_WashStrength",0);copy.SetFloat("_FadeOutStart",2000);copy.SetFloat("_FadeOutEnd",2500);copy.SetFloat("_AmbientFloor",.4f);copy.SetFloat("_LightResponse",.7f);
      if(mat.HasProperty("_BaseMap"))copy.SetTexture("_BaseMap",mat.GetTexture("_BaseMap"));else if(mat.HasProperty("_MainTex"))copy.SetTexture("_BaseMap",mat.GetTexture("_MainTex"));
      if(mat.HasProperty("_BumpMap"))copy.SetTexture("_BumpMap",mat.GetTexture("_BumpMap"));copy.SetColor("_BaseColor",Color.white);EditorUtility.SetDirty(copy);return copy;
     }).ToArray();}
    hall.transform.position+=new Vector3(m.Temple.x-bounds.center.x,m.Temple.y-bounds.min.y-.1f,m.Temple.z+11-bounds.center.z);
    // Actual structural base adapts to the slope; no sixty-metre circular platform.
    var stone=GroundMaterial292(layout);
    for(int k=0;k<9;k++){var p=m.Temple+new Vector3(-9+k*2.25f,0,4);float bottom=surface.Sample(p.x,p.z);float top=m.Temple.y+.15f;if(top>bottom+.1f)Box290("FoundationStone",temple.transform,new Vector3(p.x,(bottom+top)*.5f,p.z),new Vector3(2.3f,top-bottom,2),stone);}
    if(i==0||i==3)
    {
     var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/CodexWorld/ThatchedInn/ThatchedInn.prefab");var house=(GameObject)PrefabUtility.InstantiatePrefab(source,session.gameObject.scene);house.name="Yosa_LivingQuarters";house.transform.SetParent(temple.transform,false);var p=m.Temple+new Vector3(i==0?-21:23,0,-3);p.y=surface.Sample(p.x,p.z);house.transform.position=p;house.transform.rotation=Quaternion.Euler(0,i==0?80:-80,0);
    }
    var bell=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Korea_TreasureProps/Prefabs/SM_028_Buddhist_Temple_Bell.prefab");
    if(bell!=null){var go=(GameObject)PrefabUtility.InstantiatePrefab(bell,session.gameObject.scene);go.transform.SetParent(temple.transform,false);go.name="KoreanTempleBell";var rr=go.GetComponentsInChildren<Renderer>();if(rr.Length>0){var b=rr[0].bounds;foreach(var r in rr.Skip(1))b.Encapsulate(r.bounds);float size=1.25f/Mathf.Max(.01f,b.size.y);go.transform.localScale*=size;}go.transform.position=m.Temple+new Vector3(12,1.9f,-3);}
    var offsets=new Dictionary<string,Vector3>{{"brace",new Vector3(-8,0,-5)},{"winch",new Vector3(8,0,-3)},{"archive_open",new Vector3(0,0,3)},{"temple",new Vector3(-3,0,4)}};
    var oldContent=session.gameObject.scene.GetRootGameObjects().First(g=>g.name=="Compact_MountainContent_290");
    foreach(var point in session.Content.Points.Where(p=>p.Id.StartsWith(m.Id+"_")))
    {
     if(point.Id==m.SummitRewardId)point.Position=m.Summit+Vector3.right*2;else if(point.Id.EndsWith("_rest"))point.Position=m.Summit+Vector3.back*4;
     else if(offsets.TryGetValue(point.Id.Substring(m.Id.Length+1),out var offset))point.Position=m.Temple+offset;
     point.Position.y=surface.Sample(point.Position.x,point.Position.z);
     var sourcePoint=oldContent.GetComponentsInChildren<WorldMacroContentPoint>(true).FirstOrDefault(cp=>cp.Id==point.Id);
     if(sourcePoint!=null){var clone=Object.Instantiate(sourcePoint.gameObject,temple.transform);clone.name=point.Id;clone.transform.position=point.Position;clone.SetActive(true);
      foreach(var visual in session.InteractionVisuals.Where(v=>v.Id==point.Id))visual.Renderers=clone.GetComponentsInChildren<Renderer>();}
     foreach(var checkpoint in session.Content.Checkpoints.Where(c=>c.Id==point.Id))checkpoint.Feet=point.Position+Vector3.back*2;
    }
    // Preserve the existing logical action IDs. A fresh model does not count as a completed dungeon.
    rows.Add(m.Id+": source hall adapted; source triangles="+hall.GetComponentsInChildren<MeshFilter>().Where(f=>f.sharedMesh!=null).Sum(f=>f.sharedMesh.triangles.Length/3)+"; architectural refinement/LOD/gameplay courtyard verification pending");
   }
   EditorUtility.SetDirty(session.Content);Save292();File.WriteAllText(O292+"/sansa.txt",string.Join("\n",rows));return string.Join("\n",rows);
  }
 }
}
