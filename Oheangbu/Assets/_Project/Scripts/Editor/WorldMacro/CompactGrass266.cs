using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string GrassOut266=Output+"/Grass266";
  static string GrassFolder266=>Path.GetDirectoryName(FrontageScene249().path).Replace('\\','/')+"/Grass266";
  [Serializable] sealed class GrassRoutes266 { public GrassRoute266[] routes; }
  [Serializable] sealed class GrassRoute266 { public string id; public Vector3[] points; }
  sealed class GrassMask266
  {
   public Bounds Bounds;public Vector2 A,B;public float Radius;public bool Segment;
   public bool Contains(Vector3 p){if(!Segment)return Bounds.Contains(p);var v=new Vector2(p.x,p.z);var d=B-A;float t=d.sqrMagnitude<.001f?0:Mathf.Clamp01(Vector2.Dot(v-A,d)/d.sqrMagnitude);return (v-A-d*t).sqrMagnitude<Radius*Radius;}
  }
  static List<GrassMask266> GrassMasks266()
  {
   var scene=FrontageScene249();var roots=scene.GetRootGameObjects();var session=VillageSession();var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var layout=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>()).Single().Layout;
   var masks=new List<GrassMask266>();
   void PathMask(Vector3[] points,float radius){for(int i=1;i<points.Length;i++){var a=points[i-1];var b=points[i];var bounds=new Bounds((a+b)*.5f,new Vector3(Mathf.Abs(a.x-b.x)+radius*2,8000,Mathf.Abs(a.z-b.z)+radius*2));bounds.center=new Vector3(bounds.center.x,0,bounds.center.z);masks.Add(new GrassMask266{Bounds=bounds,A=new Vector2(a.x,a.z),B=new Vector2(b.x,b.z),Radius=radius,Segment=true});}}
   foreach(var path in DetailPaths261())PathMask(path,4.7f);
   foreach(var route in layout.Routes){var from=layout.Places.FirstOrDefault(p=>p.Id==route.From);var to=layout.Places.FirstOrDefault(p=>p.Id==route.To);if(from==null||to==null)continue;var pts=new List<Vector2>{from.XZ};pts.AddRange(route.Bends);pts.Add(to.XZ);PathMask(pts.Select(p=>new Vector3(p.x,0,p.y)).ToArray(),route.Width*.5f+2.1f);}
   if(File.Exists(Output+"/Sinmok263/routes.json"))foreach(var route in JsonUtility.FromJson<GrassRoutes266>(File.ReadAllText(Output+"/Sinmok263/routes.json")).routes)PathMask(route.points,4.5f);
   foreach(var p in art.Sheet.Passages)PathMask(new[]{p.A,p.B},p.Width*.5f+2.1f);
   PathMask(layout.River.Select(p=>new Vector3(p.x,0,p.y)).ToArray(),layout.RiverWidth*.5f+3);
   foreach(var river in art.Sheet.Geography.Rivers)PathMask(river.Points,river.Width*.5f+2.5f);
   foreach(var p in session.Content.Points)masks.Add(new GrassMask266{Bounds=new Bounds(p.Position, new Vector3(Mathf.Clamp(p.Radius,4,12)*2+4,12,Mathf.Clamp(p.Radius,4,12)*2+4))});
   foreach(var e in session.Content.Encounters)masks.Add(new GrassMask266{Bounds=new Bounds(e.Feet,new Vector3(14,12,14))});
   foreach(var area in art.Sheet.PreservedAreas){if(!area.ExcludeProcedural||(area.AffectedKinds&(1<<(int)Sheet.Kind.Grass))==0)continue;float angle=area.Yaw*Mathf.Deg2Rad;var half=new Vector2(Mathf.Abs(Mathf.Cos(angle))*area.HalfSize.x+Mathf.Abs(Mathf.Sin(angle))*area.HalfSize.y,Mathf.Abs(Mathf.Sin(angle))*area.HalfSize.x+Mathf.Abs(Mathf.Cos(angle))*area.HalfSize.y);var c=area.Centre;float h=8000;if(area.LimitHeight){h=area.MaximumY-area.MinimumY;c.y=(area.MinimumY+area.MaximumY)*.5f;}masks.Add(new GrassMask266{Bounds=new Bounds(c,new Vector3(half.x*2+4,h,half.y*2+4))});}
   foreach(var c in roots.SelectMany(g=>g.GetComponentsInChildren<Collider>())){
    if(c.isTrigger||!c.enabled||c.name.StartsWith("Terrain_")||c is CharacterController||c.attachedRigidbody!=null)continue;
    var b=c.bounds;if(b.size.x>180||b.size.z>180)continue;b.Expand(new Vector3(3.6f,2,3.6f));masks.Add(new GrassMask266{Bounds=b});
   }
   return masks;
  }
  static T GrassAsset266<T>(string name,Func<T> make)where T:Object {Directory.CreateDirectory(GrassFolder266);string p=GrassFolder266+"/"+name+".asset";var a=AssetDatabase.LoadAssetAtPath<T>(p);if(a==null){a=make();AssetDatabase.CreateAsset(a,p);}return a;}
  static Mesh GrassMesh266(int blades)
  {
   var v=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();var random=new System.Random(266);
   float R(float a,float b)=>(float)(a+(b-a)*random.NextDouble());
   for(int i=0;i<blades;i++){
    float angle=R(0,6.28318f),r=Mathf.Sqrt(R(0,1))*1.68f;var root=new Vector3(Mathf.Cos(angle)*r,-.025f,Mathf.Sin(angle)*r);var side=new Vector3(Mathf.Cos(angle+1),0,Mathf.Sin(angle+1))*R(.012f,.029f);var bend=new Vector3(R(-.18f,.18f),0,R(-.18f,.18f));float h=R(.20f,.48f);int at=v.Count;
    v.Add(root-side);v.Add(root+side);v.Add(root+Vector3.up*h*.6f+bend*.3f-side*.5f);v.Add(root+Vector3.up*h*.6f+bend*.3f+side*.5f);v.Add(root+Vector3.up*h+bend);
    uv.Add(new Vector2(0,0));uv.Add(new Vector2(1,0));uv.Add(new Vector2(0,.6f));uv.Add(new Vector2(1,.6f));uv.Add(new Vector2(.5f,1));tri.AddRange(new[]{at,at+2,at+1,at+1,at+2,at+3,at+2,at+4,at+3});
   }
   var mesh=new Mesh{name="Rooted grass cluster "+blades};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tri,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
  }
  public static string Grass266(string command)
  {
   var scene=FrontageScene249();Directory.CreateDirectory(GrassOut266);
   if(command=="checks")return GrassChecks266();
   if(command=="capture")return GrassCapture266();
   if(command!="build")throw new ArgumentException(command);if(scene.isDirty)throw new Exception("Clean candidate required");
   var roots=scene.GetRootGameObjects();var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>()).Single();
   var field=GrassAsset266("Field",()=>ScriptableObject.CreateInstance<CompactGrassField266>());
   field.Origin=Vector2.zero;field.Columns=Mathf.CeilToInt(manifest.Layout.Extent.x/32);field.Rows=Mathf.CeilToInt(manifest.Layout.Extent.y/32);field.Cells=new CompactGrassField266.Cell[field.Columns*field.Rows];
   var terrain=roots.SelectMany(g=>g.GetComponentsInChildren<MeshCollider>()).Where(c=>c.name.StartsWith("Terrain_")||c.name=="Cave_ExteriorCover237"||c.name=="Approach_Bank240").ToArray();var terrainBounds=terrain.Select(c=>c.bounds).ToArray();var masks=GrassMasks266();
   // Spatially bucket masks once; generation and runtime never search the whole world per blade.
   var maskCells=new List<GrassMask266>[field.Cells.Length];foreach(var mask in masks){var low=field.Coordinate(mask.Bounds.min);var high=field.Coordinate(mask.Bounds.max);for(int z=Mathf.Max(0,low.y);z<=Mathf.Min(field.Rows-1,high.y);z++)for(int x=Mathf.Max(0,low.x);x<=Mathf.Min(field.Columns-1,high.x);x++){int id=field.Index(x,z);if(maskCells[id]==null)maskCells[id]=new List<GrassMask266>();maskCells[id].Add(mask);}}
   int skipped=0,steep=0,total=0;var watch=System.Diagnostics.Stopwatch.StartNew();
   for(int z=0;z<field.Rows;z++)for(int x=0;x<field.Columns;x++){
    int id=field.Index(x,z);var centre=new Vector3(x*32+16,0,z*32+16);var bounds=new Bounds(centre,new Vector3(32,8000,32));var cols=new List<MeshCollider>();for(int j=0;j<terrain.Length;j++)if(bounds.Intersects(terrainBounds[j]))cols.Add(terrain[j]);
    var seeds=new List<CompactGrassField266.Seed>(100);var localMasks=maskCells[id];
    bool denseCell=field.DenseCheongrim&&field.DensePolygon.Length>=3&&field.DensePolygon.Max(p=>p.x)>=x*32&&field.DensePolygon.Min(p=>p.x)<(x+1)*32&&field.DensePolygon.Max(p=>p.y)>=z*32&&field.DensePolygon.Min(p=>p.y)<(z+1)*32;
    for(int pass=0;pass<(denseCell?2:1);pass++){
    int divisions=pass==0?10:20;float spacing=32f/divisions;
    for(int iz=0;iz<divisions;iz++)for(int ix=0;ix<divisions;ix++){
     uint hash=Sheet.Hash(x*divisions+ix,z*divisions+iz,pass==0?266:267);float px=x*32+(ix+.5f)*spacing+(Sheet.Unit(hash)-.5f)*(pass==0?1.8f:.9f),pz=z*32+(iz+.5f)*spacing+(Sheet.Unit(hash*1664525u)-.5f)*(pass==0?1.8f:.9f);
     bool dense=field.DenseCheongrim&&Oheangbu.App.World.UI.WorldMapDiscoveryGrid.Contains(field.DensePolygon,new Vector2(px,pz));
     if(pass==0&&dense||pass==1&&!dense)continue;
     if(px>=manifest.Layout.Extent.x||pz>=manifest.Layout.Extent.y)continue;
     var ray=new Ray(new Vector3(px,2000,pz),Vector3.down);bool found=false;RaycastHit hit=default;
     foreach(var c in cols)if(c.Raycast(ray,out var sample,4000)&&(!found||sample.point.y>hit.point.y)){hit=sample;found=true;}
     if(!found)continue;if(hit.normal.y<.79f){steep++;continue;}
     if(localMasks!=null&&localMasks.Any(m=>m.Contains(hit.point))){skipped++;continue;}
     float patch=Mathf.PerlinNoise(px*.037f+21,pz*.037f+13);if(!dense&&Sheet.Unit(hash*22695477u)>Mathf.Lerp(.66f,.98f,patch))continue;
     seeds.Add(new CompactGrassField266.Seed{Position=hit.point,NormalXZ=new Vector2(hit.normal.x,hit.normal.z)});
    }
    }
    if(seeds.Count>0){var b=new Bounds(seeds[0].Position,Vector3.zero);foreach(var seed in seeds)b.Encapsulate(seed.Position);b.Expand(new Vector3(5,2,5));field.Cells[id]=new CompactGrassField266.Cell{Bounds=b,Seeds=seeds.ToArray()};total+=seeds.Count;}
   }
   field.Count=total;field.NearMesh=GrassAsset266("NearFineMesh",()=>GrassMesh266(128));field.FarMesh=GrassAsset266("FarFineMesh",()=>GrassMesh266(48));
   var source=art.Sheet.Prototypes.First(p=>p.Id=="Cheongrim_LowGroundFill").Lods[0].Parts[0].Material;
   field.Material=GrassAsset266("Material",()=>new Material(source));var material=field.Material;material.shader=Shader.Find("Oheangbu/CompactNaturalVegetation");material.enableInstancing=true;
   var pigment=GrassAsset266("BladePigment",()=>{var t=new Texture2D(32,64,TextureFormat.RGB24,true,true);for(int y=0;y<64;y++)for(int x=0;x<32;x++){float ridge=1-Mathf.Abs(x/31f*2-1);float value=Mathf.Lerp(.46f,1,y/63f)*(.75f+ridge*.25f);t.SetPixel(x,y,new Color(value*.94f,value,value*.82f));}t.Apply();t.wrapMode=TextureWrapMode.Clamp;t.filterMode=FilterMode.Bilinear;return t;});
   material.SetTexture("_BaseMap",pigment);material.SetFloat("_SimpleLighting",0);material.SetFloat("_BumpScale",0);material.SetColor("_BaseColor",new Color(.32f,.35f,.23f));material.SetFloat("_AlphaClip",0);material.SetFloat("_Billboard",0);material.SetFloat("_Cull",0);material.SetFloat("_WindAmplitude",.035f);material.SetFloat("_LeafFlutter",0);material.SetFloat("_Height",.5f);material.SetVector("_WindAnchor",Vector4.zero);material.SetFloat("_ContactSoft265",1);material.SetFloat("_AmbientFloor",.65f);material.SetFloat("_LightResponse",.35f);material.SetFloat("_Saturation",.35f);material.SetFloat("_PaintedFoliage",0);
   // #307 (user 2026-10-01 "근거리 땅에 검은색 입자"): blades stay out of the depth/normals prepass, so InkWash297 does not ring every
   // sub-pixel blade with a depth/normal edge (the black specks) and SSAO/slope ink see the ground under them
   material.SetShaderPassEnabled("DepthOnly",false);material.SetShaderPassEnabled("DepthNormals",false);material.SetFloat("_ClusterFade307",1);
   // #307 black specks, root cause (engine A/B 2026-10-01): far blades are sub-pixel and pointed, so they broke into 1-2 px dark dots;
   // a 1.5 px width floor keeps them continuous and 8-30 m blades take the ground albedo (small-dark-dot density 51-61 -> 0.3-2.8 / 10k px)
   material.SetFloat("_BladePixelFloor307",1.5f);material.SetVector("_BladeFarTone307",new Vector4(8,30,.85f,0));material.SetColor("_BladeFarColour307",new Color(.55f,.51f,.43f,1));
   var renderer=roots.SelectMany(g=>g.GetComponentsInChildren<CompactGrassRenderer266>()).SingleOrDefault();if(renderer==null)renderer=new GameObject("WorldGrass266").AddComponent<CompactGrassRenderer266>();renderer.Field=field;renderer.Art=art;renderer.Invalidate();art.Contacts.GrassField=field;
   foreach(var o in new Object[]{field,material,renderer,art.Contacts})EditorUtility.SetDirty(o);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   string result="Seeds="+total+" cells="+field.Cells.Count(c=>c!=null)+" road/building/water exclusions="+skipped+" steep rejected="+steep+" generation seconds="+watch.Elapsed.TotalSeconds.ToString("F1")+". Render only; no colliders, NavMesh, map discovery, terrain or save changes.";
   File.WriteAllText(GrassOut266+"/build.txt",result);return result;
  }
 }
}
