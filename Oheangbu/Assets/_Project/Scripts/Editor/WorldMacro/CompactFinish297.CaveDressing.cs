using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 opening mine interior (SPEC-WORLD-FINISH-297 §2): 갱목 sets and the haul track from Tools/Art/cave297_dressing.py
 // (mine-local = V4 geometry frame), ore seams (광맥 — the prologue colour guide, DECISIONS #100/#152: InkLightSource band
 // + point light, LDR) fitted to the real walls by ray, and a daylight spill inside the portal. Everything lives under
 // mine/Finish297_CaveDressing and is rebuilt from the manifest on every run; cave-dressing-revert removes it.
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] class DressOre297{public float[] origin,dir;public float length,tilt;}
  [Serializable] class DressLight297{public string name;public float[] local,color;public float intensity,range;}
  [Serializable] class Dressing297{public string parent;public DressOre297[] ore;public DressLight297[] lights;}
  const string DressRoot297="Finish297_CaveDressing";

  static string CaveDressing297(bool revert)
  {
   RequireClean292();var roots=SceneManager.GetActiveScene().GetRootGameObjects();var mine=roots.Single(g=>g.name=="mine");
   var old=mine.transform.Find(DressRoot297);if(old!=null)Object.DestroyImmediate(old.gameObject);
   if(revert){Save292();return "cave dressing removed";}
   string dir=K297+"/Cave/Dressing";var d=JsonUtility.FromJson<Dressing297>(File.ReadAllText(dir+"/dressing.json"));var report=new List<string>();
   var root=new GameObject(DressRoot297).transform;root.SetParent(mine.transform,false);
   var mats=roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();
   var iron=mats.FirstOrDefault(m=>m.name.EndsWith("_WornIron"));var vein=mats.FirstOrDefault(m=>m.name.EndsWith("_Ore_Vein"));var loose=mats.FirstOrDefault(m=>m.name.EndsWith("_LooseRock"));
   Material Slot(string s)=>s=="iron"&&iron!=null?iron:s=="rock_loose"&&loose!=null?loose:KitMaterial297(s);
   // 1. timbers + track: 3 LODs, timber collision
   foreach(var name in new[]{"CaveTimbers","CaveTrack","CaveRubble"})
   {
    var go=new GameObject(name);go.transform.SetParent(root,false);var lods=new List<LOD>();float[] cut={.10f,.025f,.004f};
    for(int l=0;l<3;l++)
    {
     if(!File.Exists(dir+"/Meshes/"+name+"_LOD"+l+".json"))continue;
     var (mesh,slots)=LoadKitMesh297(dir+"/Meshes/"+name+"_LOD"+l+".json",A297+"/Meshes/Cave/"+name+"_LOD"+l+".asset");
     var child=new GameObject(name+"_LOD"+l);child.transform.SetParent(go.transform,false);child.AddComponent<MeshFilter>().sharedMesh=mesh;
     var r=child.AddComponent<MeshRenderer>();r.sharedMaterials=slots.Select(Slot).ToArray();lods.Add(new LOD(cut[l],new Renderer[]{r}));
     if(l==0)report.Add(name+": "+mesh.triangles.Length/3+" tris ["+string.Join(",",slots)+"]");
    }
    var group=go.AddComponent<LODGroup>();group.SetLODs(lods.ToArray());group.RecalculateBounds();
    string colJson=dir+"/Meshes/"+name+"_Collision.json";
    if(File.Exists(colJson)){var (col,_)=LoadKitMesh297(colJson,A297+"/Meshes/Cave/"+name+"_Collision.asset");var c=new GameObject(name+"_Collision");c.transform.SetParent(go.transform,false);c.AddComponent<MeshCollider>().sharedMesh=col;}
   }
   // 2. ore seams fitted to the walls: a strip along the wall (tilted), 3 cm proud, InkLightSource band + a point light
   int seams=0;bool back=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=true;
   try
   {
    var oreRoot=new GameObject("Ore_Seams297").transform;oreRoot.SetParent(root,false);
    for(int k=0;k<(d.ore?.Length??0);k++)
    {
     var o=d.ore[k];var origin=mine.transform.TransformPoint(new Vector3(o.origin[0],o.origin[1],o.origin[2]));
     var dirW=mine.transform.TransformDirection(new Vector3(o.dir[0],0,o.dir[1])).normalized;var along=Vector3.Cross(Vector3.up,dirW).normalized;
     const int M=16;var verts=new List<Vector3>();var uvs=new List<Vector2>();var tris=new List<int>();Vector3 sum=Vector3.zero,nsum=Vector3.zero;int hits=0;
     // main vein + a thinner sister vein above it (offset along), each a ray-fitted strip on the rock
     for(int strand=0;strand<2;strand++){int strandStart=verts.Count;float lift=strand*.62f,shift=strand*.55f,scale=strand==0?1f:.55f,len=o.length*(strand==0?1f:.62f);
     for(int i=0;i<=M;i++)
     {
      float a=(i/(float)M-.5f)*len+shift;var start=origin+along*a+Vector3.up*(lift+a*Mathf.Tan(o.tilt*Mathf.Deg2Rad)+.12f*Mathf.Sin(i*1.7f+k+strand));
      if(!Physics.Raycast(start,dirW,out var h,9,~0,QueryTriggerInteraction.Ignore)||!h.collider.transform.IsChildOf(mine.transform)){if(verts.Count>0)break;continue;}
      var n=h.normal;if(Vector3.Dot(n,-dirW)<0)n=-n;var across=(Vector3.up-n*Vector3.Dot(Vector3.up,n)).normalized;float w=(.42f+.12f*Mathf.Sin(i*2.3f+k*.7f))*scale;
      int b=verts.Count;verts.Add(h.point+n*.03f-across*w);verts.Add(h.point+n*.03f+across*w);uvs.Add(new Vector2(a,0));uvs.Add(new Vector2(a,1));
      if(b-strandStart>=2){tris.AddRange(new[]{b-2,b,b-1,b-1,b,b+1});}
      if(strand==0){sum+=h.point;nsum+=n;hits++;}
     }}
     if(hits<4){report.Add("ore "+k+": wall not found ("+hits+" hits)");continue;}
     var mesh=new Mesh{name="Ore_Seam297_"+k};mesh.SetVertices(verts);mesh.SetUVs(0,uvs);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
     mesh=ArtMesh(mesh,A297+"/Meshes/Cave/Ore_Seam297_"+k+".asset");
     var go=new GameObject("Ore_Seam297_"+k);go.transform.SetParent(oreRoot,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterial=vein;mr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
     var lamp=new GameObject("Ore_Glow297_"+k);lamp.transform.SetParent(oreRoot,false);lamp.transform.position=sum/hits+(nsum/hits).normalized*.7f;
     var light=lamp.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(.50f,.61f,.45f);light.intensity=3.2f;light.range=10f;light.shadows=LightShadows.None;seams++;
    }
   }
   finally{Physics.queriesHitBackfaces=back;}
   report.Add("ore seams: "+seams+"/"+(d.ore?.Length??0)+" (material "+(vein!=null?vein.name:"MISSING")+")");
   // 3. daylight spill inside the portal
   foreach(var l in d.lights??Array.Empty<DressLight297>())
   {
    var go=new GameObject(l.name);go.transform.SetParent(root,false);go.transform.localPosition=new Vector3(l.local[0],l.local[1],l.local[2]);
    var light=go.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(l.color[0],l.color[1],l.color[2]);light.intensity=l.intensity;light.range=l.range;light.shadows=LightShadows.None;
   }
   report.Add("lights: "+(d.lights?.Length??0));
   Physics.SyncTransforms();Save292();
   string text=string.Join("\n",report);File.WriteAllText(dir+"/dressing.txt",text);return text;
  }
 }
}
