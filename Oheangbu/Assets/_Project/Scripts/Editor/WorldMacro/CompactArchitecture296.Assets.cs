using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string Haeng296="Assets/HwaseongHaenggung/Prefabs/";
  const string Jeju296="Assets/JejumokGwana/Prefabs/";
  const string GateKit296="Assets/HwaseongForteressGate/Prefabs/";
  sealed class VenuePart296 {public Mesh[] Meshes;public Material Material;}
  sealed class VenueModule296 {public string Path,Id;public Bounds Bounds;public VenuePart296[] Parts;}
  static readonly Dictionary<string,VenueModule296> venueModules296=new Dictionary<string,VenueModule296>();
  static float GroundVenue296(CompactWorldSurface field,float x,float z)
  {
   float gx=Mathf.Floor(x/field.Cell)*field.Cell,gz=Mathf.Floor(z/field.Cell)*field.Cell;
   float u=(x-gx)/field.Cell,v=(z-gz)/field.Cell,h00=field.Sample(gx,gz),h10=field.Sample(gx+field.Cell,gz),h01=field.Sample(gx,gz+field.Cell),h11=field.Sample(gx+field.Cell,gz+field.Cell);
   return u+v<=1?h00+(h10-h00)*u+(h01-h00)*v:h11+(h01-h11)*(1-u)+(h10-h11)*(1-v);
  }
  [Serializable] sealed class VenueAssetReceipt296 {public string Source,Guid,Sha256,License;public Vector3 Size;public int[] Triangles;public string[] Materials;}
  [Serializable] sealed class VenueAssetLedger296 {public string Method;public VenueAssetReceipt296[] Sources;}
  static string HashVenue296(string path)
  {using(var h=SHA256.Create())using(var input=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
  static Material VenueMaterial296(Material source)
  {
   if(source==null)throw new Exception("Missing source architecture material; do not substitute a flat material.");
   string path=AssetDatabase.GetAssetPath(source),id=AssetDatabase.AssetPathToGUID(path);
   if(string.IsNullOrEmpty(id))throw new Exception("Architecture source material must be a saved asset: "+source.name);
   var copy=Asset296("Materials/Venues/"+id+".mat",()=>new Material(source));
   // Restore source maps/UV/alpha first, then apply the already reviewed292/295 Korean material
   // response. Raw URP's zero indirect light becomes unreadable under this candidate's ink look.
   copy.CopyPropertiesFromMaterial(source);copy.shader=Shader.Find("Oheangbu/Reworld292/KoreanArchitecture");
   if(copy.shader==null)throw new Exception("Reviewed Korean architecture shader missing.");
   copy.SetFloat("_Saturation",.35f);copy.SetFloat("_AmbientFloor",.46f);copy.SetFloat("_LightResponse",.7f);copy.SetFloat("_WashStrength",0);
   copy.SetFloat("_FadeOutStart",8000);copy.SetFloat("_FadeOutEnd",10000);copy.SetFloat("_WindAmplitude",0);copy.SetFloat("_Leaf279",0);copy.SetFloat("_SimpleLighting",0);
   copy.name="296_"+source.name;copy.enableInstancing=true;
   EditorUtility.SetDirty(copy);return copy;
  }
  static Material CharredVenueMaterial296(Material original)
  {
   string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original));var copy=Asset296("Materials/Venues/Burnt_"+guid+".mat",()=>new Material(original));
   copy.CopyPropertiesFromMaterial(original);copy.SetColor("_BaseColor",new Color(.34f,.28f,.23f,1));copy.SetFloat("_Saturation",.03f);copy.SetFloat("_AmbientFloor",.55f);copy.name="296_Charred_"+original.name;EditorUtility.SetDirty(copy);return copy;
  }
  static VenueModule296 ModuleVenue296(string path)
  {
   if(venueModules296.TryGetValue(path,out var cached))return cached;
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(prefab==null)throw new Exception("Reviewed architectural module missing: "+path);
   var accepted=new HashSet<Renderer>(prefab.GetComponentsInChildren<Renderer>(true));
   foreach(var group in prefab.GetComponentsInChildren<LODGroup>(true))
   {var lods=group.GetLODs();foreach(var lod in lods.Skip(1))foreach(var r in lod.renderers)accepted.Remove(r);}
   var groups=new Dictionary<Material,List<CombineInstance>>();Bounds bounds=default;bool haveBounds=false;
   foreach(var f in prefab.GetComponentsInChildren<MeshFilter>(true))
   {
    var r=f.GetComponent<Renderer>();var m=f.sharedMesh;if(m==null||r==null||!accepted.Contains(r))continue;
    var matrix=prefab.transform.worldToLocalMatrix*f.transform.localToWorldMatrix;
    foreach(var v in m.vertices){var p=matrix.MultiplyPoint3x4(v);if(!haveBounds){bounds=new Bounds(p,Vector3.zero);haveBounds=true;}else bounds.Encapsulate(p);}
    if(r.sharedMaterials.Length<m.subMeshCount||r.sharedMaterials.Take(m.subMeshCount).Any(v=>v==null))throw new Exception("Incomplete material slots: "+path+"/"+f.name);
    for(int sub=0;sub<m.subMeshCount;sub++)
    {var mat=r.sharedMaterials[sub];if(!groups.TryGetValue(mat,out var list)){list=new List<CombineInstance>();groups.Add(mat,list);}list.Add(new CombineInstance{mesh=m,subMeshIndex=sub,transform=matrix});}
   }
   if(!haveBounds||groups.Count==0)throw new Exception("Empty architectural module: "+path);
   string id=AssetDatabase.AssetPathToGUID(path);int index=0;var parts=new List<VenuePart296>();
   foreach(var pair in groups.OrderBy(p=>AssetDatabase.GetAssetPath(p.Key),StringComparer.Ordinal))
   {
    var near=Asset296("Meshes/Modules/"+id+"_"+index+"_0.asset",()=>new Mesh());near.Clear();near.indexFormat=IndexFormat.UInt32;
    near.CombineMeshes(pair.Value.ToArray(),true,true,false);near.RecalculateBounds();EditorUtility.SetDirty(near);
    var meshes=new[]{near,near,near};
    if(near.triangles.Length>15000)for(int level=1;level<3;level++)
    {
     var reduced=ClusterVenueMesh296(near,level==1?.035f:.13f);
     meshes[level]=Asset296("Meshes/Modules/"+id+"_"+index+"_"+level+".asset",()=>new Mesh());
     EditorUtility.CopySerialized(reduced,meshes[level]);Object.DestroyImmediate(reduced);EditorUtility.SetDirty(meshes[level]);
    }
    parts.Add(new VenuePart296{Meshes=meshes,Material=VenueMaterial296(pair.Key)});index++;
   }
   var result=new VenueModule296{Path=path,Id=id,Bounds=bounds,Parts=parts.ToArray()};venueModules296.Add(path,result);return result;
  }
  // Deterministic surface clustering is used only by distant visual LODs. Each material is separate;
  // UV islands and hard normals remain separate. Collision and near detail always use source faces.
  static Mesh ClusterVenueMesh296(Mesh source,float cell)
  {
   var p=source.vertices;var n=source.normals;var uv=source.uv;var tang=source.tangents;
   var table=new Dictionary<(int,int,int,int,int,int,int,int),int>();var remap=new int[p.Length];
   var vp=new List<Vector3>();var vn=new List<Vector3>();var vt=new List<Vector2>();var vg=new List<Vector4>();var counts=new List<int>();
   for(int i=0;i<p.Length;i++)
   {
    var normal=n.Length==p.Length?n[i]:Vector3.up;var tex=uv.Length==p.Length?uv[i]:Vector2.zero;
    var key=(Mathf.RoundToInt(p[i].x/cell),Mathf.RoundToInt(p[i].y/cell),Mathf.RoundToInt(p[i].z/cell),Mathf.RoundToInt(normal.x*4),Mathf.RoundToInt(normal.y*4),Mathf.RoundToInt(normal.z*4),Mathf.RoundToInt(tex.x*32),Mathf.RoundToInt(tex.y*32));
    if(!table.TryGetValue(key,out int k)){k=vp.Count;table.Add(key,k);vp.Add(Vector3.zero);vn.Add(Vector3.zero);vt.Add(Vector2.zero);vg.Add(Vector4.zero);counts.Add(0);}
    remap[i]=k;vp[k]+=p[i];vn[k]+=normal;vt[k]+=tex;if(tang.Length==p.Length)vg[k]+=tang[i];counts[k]++;
   }
   for(int i=0;i<vp.Count;i++){vp[i]/=counts[i];vn[i]=vn[i].normalized;vt[i]/=counts[i];vg[i]/=counts[i];}
   var tri=new List<int>();var seen=new HashSet<(int,int,int)>();var input=source.triangles;
   for(int i=0;i<input.Length;i+=3)
   {
    int a=remap[input[i]],b=remap[input[i+1]],c=remap[input[i+2]];if(a==b||b==c||c==a||Vector3.Cross(vp[b]-vp[a],vp[c]-vp[a]).sqrMagnitude<1e-14f)continue;
    if(!seen.Add((a,b,c)))continue;tri.Add(a);tri.Add(b);tri.Add(c);
   }
   if(tri.Count==0)throw new Exception("LOD clustering removed every triangle from "+source.name);
   var output=new Mesh{name=source.name+"_Cluster",indexFormat=IndexFormat.UInt32};output.SetVertices(vp);output.SetNormals(vn);output.SetUVs(0,vt);output.SetTangents(vg);output.SetTriangles(tri,0);output.RecalculateBounds();return output;
  }
  sealed class VenueBatch296
  {
   readonly string id;readonly Transform parent;
   readonly Dictionary<Material,List<CombineInstance>[]> visuals=new Dictionary<Material,List<CombineInstance>[]>();
   readonly List<CombineInstance> collision=new List<CombineInstance>();
   readonly List<Mesh> temporaryVisuals=new List<Mesh>();
   public bool Charred,CollisionOnly,VisualsOnly;public string StoneStyle,InteriorRealm;public int Pieces,NearTriangles;public readonly HashSet<string> Sources=new HashSet<string>();
   public VenueBatch296(string id,Transform parent){this.id=id;this.parent=parent;}
   public void Add(string path,Vector3 bottomCentre,Vector3 size,float yaw=0,bool solid=true)
   {
    var module=ModuleVenue296(path);var b=module.Bounds;var scale=new Vector3(size.x/Mathf.Max(.00001f,b.size.x),size.y/Mathf.Max(.00001f,b.size.y),size.z/Mathf.Max(.00001f,b.size.z));
    if(b.size.y<.001f)scale.y=1; // Authored single-sided paving stays a surface; its support is separate.
    var matrix=Matrix4x4.TRS(bottomCentre,Quaternion.Euler(0,yaw,0),scale)*Matrix4x4.Translate(new Vector3(-b.center.x,-b.min.y,-b.center.z));
    Add(module,matrix,solid);
   }
   public void Add(VenueModule296 module,Matrix4x4 matrix,bool solid)
   {
    Sources.Add(module.Path);Pieces++;
    foreach(var part in module.Parts)
    {
     bool stone=module.Path.Contains("Floor_stone")||module.Path.Contains("Bastion")||module.Path.Contains("OuterWall");
     var material=stone&&!string.IsNullOrEmpty(StoneStyle)?RegionalStoneVenue296(part.Material,StoneStyle):Charred?CharredVenueMaterial296(part.Material):part.Material;
     if(!CollisionOnly)
     {
      if(!string.IsNullOrEmpty(InteriorRealm))material=InteriorMaterial296(material,module.Path,InteriorRealm);
      if(!visuals.TryGetValue(material,out var lists)){lists=new[]{new List<CombineInstance>(),new List<CombineInstance>(),new List<CombineInstance>()};visuals.Add(material,lists);}
      for(int i=0;i<3;i++)lists[i].Add(new CombineInstance{mesh=part.Meshes[i],subMeshIndex=0,transform=matrix});
      NearTriangles+=part.Meshes[0].triangles.Length/3;
     }
     if(solid&&!VisualsOnly)collision.Add(new CombineInstance{mesh=part.Meshes[0],subMeshIndex=0,transform=matrix});
    }
   }
   public void AddRaw(Mesh mesh,Material material,bool solid)
   {
    Add(new VenueModule296{Path="Candidate authored structural geometry",Parts=new[]{new VenuePart296{Meshes=new[]{mesh,mesh,mesh},Material=material}}},Matrix4x4.identity,solid);
   }
   public void AddTemporaryVisual(Mesh mesh,Material material){temporaryVisuals.Add(mesh);AddRaw(mesh,material,false);}
   public void AddCollision(Mesh mesh,Matrix4x4 matrix){collision.Add(new CombineInstance{mesh=mesh,subMeshIndex=0,transform=matrix});}
   public void Save(bool keepPhysical=false)
   {
    var levels=new List<Renderer>[3]{new List<Renderer>(),new List<Renderer>(),new List<Renderer>()};int mi=0;
    foreach(var pair in visuals.OrderBy(p=>AssetDatabase.GetAssetPath(p.Key),StringComparer.Ordinal))
    {
     for(int level=0;level<3;level++)
     {
      var mesh=Asset296("Meshes/Venues/"+id+"_"+mi+"_L"+level+".asset",()=>new Mesh());mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;mesh.CombineMeshes(pair.Value[level].ToArray(),true,true,false);mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
      var go=new GameObject("L"+level+"_"+mi);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=pair.Key;levels[level].Add(r);go.isStatic=true;
     }mi++;
    }
    if(collision.Count>0&&!keepPhysical)
    {
     var mesh=Asset296("Meshes/Venues/"+id+"_Physical.asset",()=>new Mesh());mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;mesh.CombineMeshes(collision.ToArray(),true,true,false);mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
     var go=new GameObject("Physical_SourceFaces");go.transform.SetParent(parent,false);go.AddComponent<MeshCollider>().sharedMesh=mesh;go.isStatic=true;
    }
    var group=parent.gameObject.AddComponent<LODGroup>();group.SetLODs(new[]{new LOD(.24f,levels[0].ToArray()),new LOD(.065f,levels[1].ToArray()),new LOD(.003f,levels[2].ToArray())});group.RecalculateBounds();
    foreach(var temporary in temporaryVisuals)Object.DestroyImmediate(temporary);temporaryVisuals.Clear();
   }
  }
  static void SaveVenueSources296()
  {
   var sources=venueModules296.Values.OrderBy(m=>m.Path,StringComparer.Ordinal).ToArray();
   File.WriteAllText(O296+"/venue-assets.json",JsonUtility.ToJson(new VenueAssetLedger296{Method="Owned KCISA modules and source maps/UV. Candidate materials use reviewed KoreanArchitecture lighting. Module L0/physical faces retain source geometry; body LOD uses constrained clustering. Large roofs use separately documented exposed-tile bake/QEM and visible low-poly physical shell. Original assets untouched.",Sources=sources.Select(m=>new VenueAssetReceipt296{Source=m.Path,Guid=m.Id,Sha256=HashVenue296(m.Path),License="Previously owned KCISA Unity Asset Store distribution; original Standard Asset Store EULA retained.",Size=m.Bounds.size,Triangles=Enumerable.Range(0,3).Select(i=>m.Parts.Sum(p=>p.Meshes[i].triangles.Length/3)).ToArray(),Materials=m.Parts.Select(p=>AssetDatabase.GetAssetPath(p.Material)).ToArray()}).ToArray()},true));
   var sheet=Sheet296();sheet.Sources=sheet.Sources.Where(s=>!s.Id.StartsWith("venue_",StringComparison.Ordinal)).Concat(sources.Select(m=>new CompactArchitectureSheetSO.Source{Id="venue_"+m.Id,AssetPath=m.Path,Publisher="KCISA / previously owned distribution",Url=m.Path.Contains("Forteress")?"https://assetstore.unity.com/packages/3d/environments/historic/kcisa-hwaseong-fortress-gates-304443":m.Path.Contains("Haenggung")?"https://assetstore.unity.com/packages/3d/environments/historic/kcisa-hwaseong-haenggung-ver-2024-korean-traditional-palace-303896":"https://www.culture.go.kr/datametaverse",License="Original Unity Asset Store EULA retained; not CC0",Use="Korean architecture module; separate candidate meshes/materials",MeshyCredits=0})).ToArray();EditorUtility.SetDirty(sheet);
  }
 }
}
