using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 opening mine portal (SPEC-WORLD-FINISH-297 §2). After the relocation the V4 open trench lies under the new hillside,
 // so from inside the player saw sky through back-face-culled terrain. Tools/Art/cave297_portal.py builds a roofed gallery,
 // floor, quarried portal face, 갱목 sets and blast rubble (Finish297/Cave/Portal). Here: the V4 interior, its reversed outer
 // collision and the floor are clipped beyond the clip plane into private mesh copies, the V4 approach soil retires, and the
 // portal meshes are placed under `mine` (world position kept). Originals are recorded once; cave-portal-revert restores them.
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] class PortalMesh297{public string name;public bool collider;}
  [Serializable] class Portal297{public float[] origin,axisA,axisD;public float floor,clipS,floorClipS,faceS,clipHalfWidth,clipMaxY,cell;public int[] holeCells;public PortalMesh297[] meshes;public string[] retire;}
  [Serializable] class PortalOriginal297{public string[] filters,filterMeshes,colliders,colliderMeshes,retired,holeFilters,holeMeshes,materialRenderers,materialLists;public bool[] retiredActive;}
  [Serializable] class PortalPlacements297{public WorldMacroDressingSheetSO.FixedPlacement[] removed;}
  const string Interior297="mine/Playtest_NaturalCave/Natural_Cave_Interior",CaveFloor297="mine/Playtest_NaturalCave/Natural_Cave_Floor",PortalRoot297="Finish297_CavePortal";

  static string CavePortal297(bool revert)
  {
   RequireClean292();var scene=SceneManager.GetActiveScene();var roots=scene.GetRootGameObjects();var mine=roots.Single(g=>g.name=="mine");
   var all=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();Transform Find(string p)=>all.FirstOrDefault(t=>HierarchyPath(t)==p);
   string dir=K297+"/Cave/Portal",file=dir+"/portal-original.json";
   var interior=Find(Interior297);var floor=Find(CaveFloor297);var outer=interior!=null?interior.Find("Cave_OuterCollision237"):null;
   if(interior==null||floor==null)throw new Exception("V4 cave interior/floor missing");
   if(revert)
   {
    var o=JsonUtility.FromJson<PortalOriginal297>(File.ReadAllText(file));
    for(int i=0;i<o.filters.Length;i++){var t=Find(o.filters[i]);if(t!=null)t.GetComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(o.filterMeshes[i]);}
    for(int i=0;i<o.colliders.Length;i++){var t=Find(o.colliders[i]);var c=t!=null?t.GetComponent<MeshCollider>():null;if(c!=null){c.sharedMesh=null;c.sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(o.colliderMeshes[i]);}}
    for(int i=0;i<o.retired.Length;i++){var t=Find(o.retired[i]);if(t!=null)t.gameObject.SetActive(o.retiredActive[i]);}
    for(int i=0;i<(o.materialRenderers?.Length??0);i++){var t=Find(o.materialRenderers[i]);var r=t!=null?t.GetComponent<MeshRenderer>():null;if(r!=null)r.sharedMaterials=o.materialLists[i].Split('|').Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();}
    for(int i=0;i<(o.holeFilters?.Length??0);i++){var t=Find(o.holeFilters[i]);if(t==null)continue;var m=AssetDatabase.LoadAssetAtPath<Mesh>(o.holeMeshes[i]);t.GetComponent<MeshFilter>().sharedMesh=m;var col=t.GetComponentInParent<MeshCollider>();if(col!=null){col.sharedMesh=null;col.sharedMesh=m;}}
    string pf=dir+"/portal-placements.json";
    if(File.Exists(pf)){var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single().Art;var back=JsonUtility.FromJson<PortalPlacements297>(File.ReadAllText(pf)).removed;
     art.FixedPlacements=art.FixedPlacements.Concat(back.Where(b=>!art.FixedPlacements.Any(f=>f.Id==b.Id&&f.Position==b.Position))).ToArray();EditorUtility.SetDirty(art);File.Delete(pf);}
    foreach(var t in new[]{interior,floor}){var ch=t.Find(Chunks297);if(ch!=null)Object.DestroyImmediate(ch.gameObject);t.GetComponent<MeshRenderer>().enabled=true;}
    var built=mine.transform.Find(PortalRoot297);if(built!=null)Object.DestroyImmediate(built.gameObject);
    Physics.SyncTransforms();Save292();return "cave portal removed; V4 interior/floor meshes restored";
   }
   var d=JsonUtility.FromJson<Portal297>(File.ReadAllText(dir+"/unity.json"));
   var clipped=new[]{interior,floor,outer}.Where(t=>t!=null).ToArray();
   if(!File.Exists(file))
   {
    var fs=clipped.Where(t=>t.GetComponent<MeshFilter>()!=null).ToArray();var cs=clipped.Where(t=>t.GetComponent<MeshCollider>()!=null).ToArray();
    var ret=d.retire.Select(Find).ToArray();
    File.WriteAllText(file,JsonUtility.ToJson(new PortalOriginal297{filters=fs.Select(HierarchyPath).ToArray(),filterMeshes=fs.Select(t=>AssetDatabase.GetAssetPath(t.GetComponent<MeshFilter>().sharedMesh)).ToArray(),
     colliders=cs.Select(HierarchyPath).ToArray(),colliderMeshes=cs.Select(t=>AssetDatabase.GetAssetPath(t.GetComponent<MeshCollider>().sharedMesh)).ToArray(),
     retired=d.retire,retiredActive=ret.Select(t=>t!=null&&t.gameObject.activeSelf).ToArray()},true));
   }
   var orig=JsonUtility.FromJson<PortalOriginal297>(File.ReadAllText(file));
   Mesh Source(Transform t,bool collider){string path=HierarchyPath(t);int i=Array.IndexOf(collider?orig.colliders:orig.filters,path);return i<0?null:AssetDatabase.LoadAssetAtPath<Mesh>((collider?orig.colliderMeshes:orig.filterMeshes)[i]);}
   var A=new Vector2(d.axisA[0],d.axisA[1]);var D=new Vector2(d.axisD[0],d.axisD[1]).normalized;var Rt=new Vector2(D.y,-D.x);
   var report=new List<string>();DevSceneKit.EnsureFolder(A297+"/Meshes/Cave");DevSceneKit.EnsureFolder(A297+"/Meshes/Cave/Chunks");
   // 1. clip the V4 meshes beyond the plane (only the trench: lateral and height limited) — always from the recorded originals
   // orient: +1 faces/normals toward the trench axis (render + inner collision), -1 away (reversed outer collision), 0 as is.
   // The V4 tube's last ~15 m (the old open bell mouth) is wound and shaded outward-facing; once roofed it must face in.
   float orientFrom=d.clipS-18;
   Mesh Clip(Mesh src,Transform t,float clipS,string asset,int orient)
   {
    var m=Object.Instantiate(src);m.name=Path.GetFileNameWithoutExtension(asset);var v=src.vertices;var nrm=src.normals;int removed=0,flipped=0,renormal=0;
    Vector3 ToAxis(Vector3 w){var q=new Vector2(w.x,w.z)-A;float s=Vector2.Dot(q,D);var p=A+D*s;return new Vector3(p.x,d.floor+2.2f,p.y)-w;}
    bool InOrient(Vector3 w){var q=new Vector2(w.x,w.z)-A;float s=Vector2.Dot(q,D);return orient!=0&&s>orientFrom&&s<=clipS+.5f&&Mathf.Abs(Vector2.Dot(q,Rt))<10&&w.y>d.floor-4&&w.y<d.floor+10;}
    for(int s=0;s<m.subMeshCount;s++)
    {
     var tri=m.GetTriangles(s);var keep=new List<int>(tri.Length);
     for(int i=0;i<tri.Length;i+=3)
     {
      var c=t.TransformPoint((v[tri[i]]+v[tri[i+1]]+v[tri[i+2]])/3);var q=new Vector2(c.x,c.z)-A;
      if(Vector2.Dot(q,D)>clipS&&Mathf.Abs(Vector2.Dot(q,Rt))<d.clipHalfWidth&&c.y<d.clipMaxY){removed++;continue;}
      int a0=tri[i],a1=tri[i+1],a2=tri[i+2];
      if(InOrient(c))
      {
       var fn=Vector3.Cross(t.TransformPoint(v[a1])-t.TransformPoint(v[a0]),t.TransformPoint(v[a2])-t.TransformPoint(v[a0]));
       if(Vector3.Dot(fn,ToAxis(c))*orient<0){var x=a1;a1=a2;a2=x;flipped++;}
      }
      keep.Add(a0);keep.Add(a1);keep.Add(a2);
     }
     m.SetTriangles(keep,s,false);
    }
    if(orient>0&&nrm!=null&&nrm.Length==v.Length)
    {
     for(int i=0;i<v.Length;i++){var w=t.TransformPoint(v[i]);if(!InOrient(w))continue;if(Vector3.Dot(t.TransformDirection(nrm[i]),ToAxis(w))<0){nrm[i]=-nrm[i];renormal++;}}
     m.normals=nrm;
    }
    m.RecalculateBounds();report.Add(t.name+": clipped "+removed+" triangles, flipped "+flipped+" faces / "+renormal+" normals toward the axis");return ArtMesh(m,asset);
   }
   foreach(var t in clipped)
   {
    float s=t==floor?d.floorClipS:d.clipS;var mf=t.GetComponent<MeshFilter>();var mc=t.GetComponent<MeshCollider>();
    int orient=t==interior?1:t==outer?-1:0;
    if(mf!=null){var src=Source(t,false);if(src!=null)mf.sharedMesh=Clip(src,t,s,A297+"/Meshes/Cave/"+t.name+"_297.asset",orient);}
    if(mc!=null)
    {
     var src=Source(t,true);if(src==null)continue;
     var same=mf!=null&&Source(t,false)==src;var mesh=same?mf.sharedMesh:Clip(src,t,s,A297+"/Meshes/Cave/"+t.name+"_Collision_297.asset",orient);
     mc.sharedMesh=null;mc.sharedMesh=mesh;
    }
   }
   // the V4 mouth submeshes (daylit EntranceRock, terrain ground on the old apron floor) are now deep inside the gallery
   var mrs=(orig.materialRenderers??Array.Empty<string>()).ToList();var ml=(orig.materialLists??Array.Empty<string>()).ToList();
   foreach(var t in new[]{interior,floor})
   {
    var r=t.GetComponent<MeshRenderer>();string path=HierarchyPath(t);
    if(!mrs.Contains(path)){mrs.Add(path);ml.Add(string.Join("|",r.sharedMaterials.Select(AssetDatabase.GetAssetPath)));}
    var first=AssetDatabase.LoadAssetAtPath<Material>(ml[mrs.IndexOf(path)].Split('|')[0]);r.sharedMaterials=r.sharedMaterials.Select(_=>first).ToArray();
    report.Add(t.name+": all submeshes -> "+first.name);
   }
   orig.materialRenderers=mrs.ToArray();orig.materialLists=ml.ToArray();File.WriteAllText(file,JsonUtility.ToJson(orig,true));
   foreach(var p in d.retire){var t=Find(p);if(t!=null){t.gameObject.SetActive(false);report.Add("retired "+p);}}
   // 2. portal meshes under mine (world-aligned around origin)
   var old=mine.transform.Find(PortalRoot297);if(old!=null)Object.DestroyImmediate(old.gameObject);
   var root=new GameObject(PortalRoot297);root.transform.SetParent(mine.transform,false);root.transform.SetPositionAndRotation(new Vector3(d.origin[0],d.origin[1],d.origin[2]),Quaternion.identity);
   var rock=interior.GetComponent<MeshRenderer>().sharedMaterials[0];var soil=floor.GetComponent<MeshRenderer>().sharedMaterials[0];
   var loose=roots.SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).Select(r=>r.sharedMaterial).FirstOrDefault(m=>m!=null&&m.name.EndsWith("_LooseRock"))??rock;
   // outside the portal: the realm granite of the #296 highland cliffs and the terrain tile's own ground material
   var sceneMats=roots.SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();
   var granite=sceneMats.FirstOrDefault(m=>m.name.EndsWith("_Cheongrim_Granite"))??rock;
   var tileAt=roots.FirstOrDefault(g=>g.name=="Reworld292_Terrain")?.transform.Cast<Transform>().FirstOrDefault(t=>new Rect(t.position.x,t.position.z,500,500).Contains(new Vector2(d.origin[0],d.origin[2])));
   var ground=tileAt!=null?tileAt.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r=>r.name=="LOD0")?.sharedMaterial:null;
   Material Slot(string slot)=>slot=="cave_rock"?rock:slot=="cave_floor"?soil:slot=="rock_loose"?loose:slot=="cliff_rock"?granite:slot=="terrain"?(ground??soil):KitMaterial297(slot);
   foreach(var pm in d.meshes)
   {
    var (mesh,slots)=LoadKitMesh297(dir+"/Meshes/"+pm.name+".json",A297+"/Meshes/Cave/"+pm.name+".asset");
    var go=new GameObject(pm.name);go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
    var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterials=slots.Select(Slot).ToArray();
    if(pm.collider)go.AddComponent<MeshCollider>().sharedMesh=mesh;
    report.Add(pm.name+": "+mesh.triangles.Length/3+" tris ["+string.Join(",",slots)+"]"+(pm.collider?" +collider":""));
   }
   // 3. terrain hole cells: private LOD0 tile meshes + their colliders, always cut from the recorded pre-hole mesh
   var cells=new HashSet<(int,int)>();for(int i=0;i+1<(d.holeCells?.Length??0);i+=2)cells.Add((d.holeCells[i],d.holeCells[i+1]));
   var terrainRoot=roots.FirstOrDefault(g=>g.name=="Reworld292_Terrain");
   if(cells.Count>0&&terrainRoot!=null)
   {
    var hf=(orig.holeFilters??Array.Empty<string>()).ToList();var hm=(orig.holeMeshes??Array.Empty<string>()).ToList();
    foreach(Transform tile in terrainRoot.transform)
    {
     var lod0=tile.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(x=>x.name=="LOD0");if(lod0==null)continue;
     var box=new Rect(tile.position.x,tile.position.z,500,500);if(!cells.Any(c=>box.Overlaps(new Rect(c.Item1*d.cell,c.Item2*d.cell,d.cell,d.cell))))continue;
     string path=HierarchyPath(lod0.transform);int oi=hf.IndexOf(path);
     if(oi<0){hf.Add(path);hm.Add(AssetDatabase.GetAssetPath(lod0.sharedMesh));oi=hf.Count-1;}
     var src=AssetDatabase.LoadAssetAtPath<Mesh>(hm[oi]);var m=Object.Instantiate(src);var v=src.vertices;int cut=0;
     var tri=m.triangles;var keep=new List<int>(tri.Length);
     for(int i=0;i<tri.Length;i+=3){var c=lod0.transform.TransformPoint((v[tri[i]]+v[tri[i+1]]+v[tri[i+2]])/3);if(cells.Contains((Mathf.FloorToInt(c.x/d.cell),Mathf.FloorToInt(c.z/d.cell)))){cut++;continue;}keep.Add(tri[i]);keep.Add(tri[i+1]);keep.Add(tri[i+2]);}
     m.subMeshCount=1;m.SetTriangles(keep,0,true);m.name=tile.name+"_LOD0_hole297";
     var holed=ArtMesh(m,A297+"/Meshes/Terrain/"+tile.name+"_LOD0_hole297.asset");lod0.sharedMesh=holed;
     var col=tile.GetComponent<MeshCollider>();if(col!=null){col.sharedMesh=null;col.sharedMesh=holed;}
     report.Add("terrain "+tile.name+": hole "+cut+" triangles in "+cells.Count+" cells");
    }
    orig.holeFilters=hf.ToArray();orig.holeMeshes=hm.ToArray();File.WriteAllText(file,JsonUtility.ToJson(orig,true));
    // vegetation standing on the hole (plants would float over the portal hood); removed ones are kept for the revert
    var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single().Art;string pf=dir+"/portal-placements.json";
    var removed=File.Exists(pf)?JsonUtility.FromJson<PortalPlacements297>(File.ReadAllText(pf)).removed.ToList():new List<WorldMacroDressingSheetSO.FixedPlacement>();
    bool Near(Vector3 p){for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)if(cells.Contains((Mathf.FloorToInt((p.x+dx*1.5f)/d.cell),Mathf.FloorToInt((p.z+dz*1.5f)/d.cell))))return true;return false;}
    var gone=art.FixedPlacements.Where(pl=>Near(pl.Position)&&art.Prototypes.First(q=>q.Id==pl.PrototypeId).Category!=WorldMacroDressingSheetSO.Kind.Prop).ToArray();
    if(gone.Length>0){removed.AddRange(gone);art.FixedPlacements=art.FixedPlacements.Except(gone).ToArray();EditorUtility.SetDirty(art);File.WriteAllText(pf,JsonUtility.ToJson(new PortalPlacements297{removed=removed.ToArray()},true));}
    foreach(var r in roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true))){r.enabled=false;r.enabled=true;}
    report.Add("vegetation removed on the hole: "+gone.Length);
   }
   // 4. forward rendering lights at most 4 additional lights per renderer: the V4 interior and floor (one renderer each over
   //    150 x 185 m) render as 16 m grid chunks so each piece gets its nearby lanterns/ore glows; collision stays whole
   foreach(var t in new[]{interior,floor})report.Add(Chunk297(t,16f));
   Physics.SyncTransforms();Save292();
   string text=string.Join("\n",report);File.WriteAllText(dir+"/portal.txt",text);return text;
  }
  const string Chunks297="Chunks297";
  static string Chunk297(Transform t,float cell)
  {
   var old=t.Find(Chunks297);if(old!=null)Object.DestroyImmediate(old.gameObject);
   var mf=t.GetComponent<MeshFilter>();var mr=t.GetComponent<MeshRenderer>();var mesh=mf.sharedMesh;var v=mesh.vertices;var n=mesh.normals;var uv=mesh.uv;
   var cells=new Dictionary<(int,int),List<int>[]>();
   for(int s=0;s<mesh.subMeshCount;s++)
   {
    var tri=mesh.GetTriangles(s);
    for(int i=0;i<tri.Length;i+=3)
    {
     var c=t.TransformPoint((v[tri[i]]+v[tri[i+1]]+v[tri[i+2]])/3);var key=(Mathf.FloorToInt(c.x/cell),Mathf.FloorToInt(c.z/cell));
     if(!cells.TryGetValue(key,out var lists)){lists=Enumerable.Range(0,mesh.subMeshCount).Select(_=>new List<int>()).ToArray();cells[key]=lists;}
     lists[s].Add(tri[i]);lists[s].Add(tri[i+1]);lists[s].Add(tri[i+2]);
    }
   }
   var root=new GameObject(Chunks297).transform;root.SetParent(t,false);int made=0;
   foreach(var kv in cells)
   {
    // compact vertex set per chunk
    var map=new Dictionary<int,int>();var cv=new List<Vector3>();var cn=new List<Vector3>();var cu=new List<Vector2>();var subs=new List<int[]>();
    foreach(var list in kv.Value){var o=new int[list.Count];for(int i=0;i<list.Count;i++){int k=list[i];if(!map.TryGetValue(k,out int j)){j=cv.Count;map[k]=j;cv.Add(v[k]);if(n!=null&&n.Length==v.Length)cn.Add(n[k]);if(uv!=null&&uv.Length==v.Length)cu.Add(uv[k]);}o[i]=j;}subs.Add(o);}
    var m=new Mesh{name=t.name+"_chunk_"+kv.Key.Item1+"_"+kv.Key.Item2,indexFormat=cv.Count>65000?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16};
    m.SetVertices(cv);if(cn.Count==cv.Count)m.SetNormals(cn);if(cu.Count==cv.Count)m.SetUVs(0,cu);m.subMeshCount=subs.Count;for(int s=0;s<subs.Count;s++)m.SetTriangles(subs[s],s,false);m.RecalculateBounds();
    m=ArtMesh(m,A297+"/Meshes/Cave/Chunks/"+m.name+".asset");
    var go=new GameObject(m.name);go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=m;var r=go.AddComponent<MeshRenderer>();
    r.sharedMaterials=mr.sharedMaterials;r.shadowCastingMode=mr.shadowCastingMode;r.receiveShadows=mr.receiveShadows;made++;
   }
   mr.enabled=false;return t.name+": "+made+" render chunks of "+cell+" m (collider stays on the whole mesh)";
  }
 }
}
