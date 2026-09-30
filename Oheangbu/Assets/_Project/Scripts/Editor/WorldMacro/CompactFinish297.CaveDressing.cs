using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #306 working-mine layer (PLAN Art/Playtest306/PLAN.md §2-7, Track A; all values TEST). Queue:
 //   Oheangbu.EditorTools.WorldMacro.CompactMine306 Run dry|apply|check|revert[:<scene path>]
 // Refusals come back as strings (never a dialog). Without a scene path the active scene is used.
 public static class CompactMine306 { public static string Run(string command)=>CompactRebuildAuthoring.Mine306(command); }

 // #297 opening mine interior (SPEC-WORLD-FINISH-297 §2): 갱목 sets and the haul track from Tools/Art/cave297_dressing.py
 // (mine-local = V4 geometry frame), ore seams (광맥 — the prologue colour guide, DECISIONS #100/#152: InkLightSource band
 // + point light, LDR) fitted to the real walls by ray, and a daylight spill inside the portal. Everything lives under
 // mine/Finish297_CaveDressing and is rebuilt from the manifest on every run; cave-dressing-revert removes it.
 // #306: seam rays skip dressing/#306 colliders (posts, packwalls) and fit only the rock.
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
   Physics.SyncTransforms();
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
      if(!RockRay306(mine.transform,start,dirW,9,out var h)){if(verts.Count>0)break;continue;}
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

  // rock = a large static mesh/terrain collider of the mine (V4 interior, portal gallery/face), never dressing or #306 pieces
  static bool Rock306(Collider c,Transform mine)
  {
   if(c==null||c.isTrigger||!(c is MeshCollider||c is TerrainCollider)||c.bounds.size.magnitude<12)return false;
   for(var t=c.transform;t!=null;t=t.parent)if(t.name==DressRoot297||t.name==Root306||t.name==Yard306)return false;
   return mine==null||c.transform.IsChildOf(mine);
  }
  static bool RockRay306(Transform mine,Vector3 from,Vector3 dir,float reach,out RaycastHit hit)
  {
   foreach(var h in Physics.RaycastAll(from,dir,reach,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))if(Rock306(h.collider,mine)){hit=h;return true;}
   hit=default;return false;
  }

  // ======================================================================================================== #306
  [Serializable] class M306Chunk{public string name,layer,collision;public string[] lods;}
  [Serializable] class M306Cart{public string mesh,snap,collider,note;public float[] local,euler;}
  [Serializable] class M306Single{public string mesh,collider;public float[] local;}
  [Serializable] class M306Patch{public string kind;public float[] local;public float radius;public int seed;}
  [Serializable] class M306Line{public string frame;public float[] points;}
  [Serializable] class M306Strip{public float[] origin,dir;public float length,tilt,width;public int strands;}
  [Serializable] class M306Picks{public float[] origin,dir;public float width,height,angle;public int seed;}
  [Serializable] class M306Prop{public string prefab,frame,snap,note;public float[] local,world,euler;public float size;public bool strip;}
  [Serializable] class M306Crystals{public string[] variants;public int perSeam;}
  [Serializable] class M306YardMesh{public string mesh,collider,note;public float[] world;public float yaw,pitch;}
  [Serializable] class M306Heap{public string mesh,collision;public float[] samples;}
  [Serializable] class M306Track{public float[] points;public float gauge;}
  [Serializable] class M306Yard{public float[] origin;public M306Heap heap;public M306YardMesh[] meshes;public M306Prop[] props;public M306Track tipTrack;}
  [Serializable] class M306Sound{public string clip,frame;public float[] local,world;public float level,radius;public bool intermittent;}
  [Serializable] class M306Lamps{public string prefix;public int[] keep;}
  [Serializable] class M306Manifest{public string parent,status;public M306Chunk[] chunks;public M306Cart[] carts;public M306Single[] singles;public M306Patch[] patches;public M306Line[] fuses;public M306Strip[] coal;public M306Picks[] picks;
   public M306Prop[] props;public M306Crystals crystals;public M306Yard yard;public M306Sound[] sounds;public M306Lamps lamps;public float[] walk;public float floor;public string[] warnings;}
  [Serializable] class M306Original{public List<string> renderers=new List<string>(),materials=new List<string>(),lights=new List<string>();public List<bool> lightEnabled=new List<bool>();}
  const string M306="../Art/World/Compact/Rebuild/Mine306",A306="Assets/_Project/Art/World/Mine306",Root306="Mine306_Interior",Yard306="Mine306_Yard";
  static readonly string[] Scenes306={"Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity","Assets/_Project/Scenes/World/W_Demo_Main.unity","Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity"};
  // originals per scene: the three scenes share Mine306/, so one scene's revert must not delete another scene's record
  static string Original306=>M306+"/original_"+SceneManager.GetActiveScene().name+".json";
  static readonly Dictionary<string,(float cut0,float cut1,float cut2)> Cuts306=new Dictionary<string,(float,float,float)>{{"Packwall",(.25f,.02f,0)},{"Timbers",(.2f,.06f,.01f)}};

  public static string Mine306(string command)
  {
   command=(command??"").Trim();int colon=command.IndexOf(':');string verb=colon<0?command:command.Substring(0,colon),scene=colon<0?"":command.Substring(colon+1).Trim();
   if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Play mode";
   if(verb!="dry"&&verb!="apply"&&verb!="check"&&verb!="revert")return "REFUSED unknown command "+command+" (dry|apply|check|revert[:<scene>])";
   if(scene.Length>0)
   {
    if(!Scenes306.Contains(scene))return "REFUSED scene not in the #306 mine list: "+scene;
    if(SceneManager.GetActiveScene().path!=scene)
    {
     for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)return "REFUSED dirty scene "+SceneManager.GetSceneAt(i).path;
     EditorSceneManager.OpenScene(scene,OpenSceneMode.Single);
    }
   }
   // every verb runs only on a listed #306 scene (the protected W_Demo_Compact and other trees are never touched, even as the active scene)
   if(!Scenes306.Contains(SceneManager.GetActiveScene().path))return "REFUSED active scene not in the #306 mine list: "+SceneManager.GetActiveScene().path+" (pass :<scene>)";
   if((verb=="apply"||verb=="revert")&&SceneManager.GetActiveScene().isDirty)return "REFUSED dirty scene "+SceneManager.GetActiveScene().path;
   var mine=SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name=="mine");
   if(mine==null)return "REFUSED no `mine` root in "+SceneManager.GetActiveScene().path;
   if(!File.Exists(M306+"/mine306.json"))return "REFUSED run python Tools/Art/cave297_mine306.py first";
   var m=JsonUtility.FromJson<M306Manifest>(File.ReadAllText(M306+"/mine306.json"));
   string text=verb=="dry"?Dry306(mine.transform,m):verb=="apply"?Apply306(mine.transform,m):verb=="check"?Check306(mine.transform,m):Revert306(mine.transform);
   string report="scene "+SceneManager.GetActiveScene().path+"\n"+text;File.WriteAllText(M306+"/"+verb+".txt",report);File.WriteAllText(M306+"/"+verb+"_"+SceneManager.GetActiveScene().name+".txt",report);return text;
  }

  // -- materials -------------------------------------------------------------------------------------------------------
  static Material Mat306(string name,Color c,float smooth,string bc=null,string nm=null)
  {
   DevSceneKit.EnsureFolder(A306+"/Materials");string path=A306+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};AssetDatabase.CreateAsset(m,path);}
   var t=bc!=null?AssetDatabase.LoadAssetAtPath<Texture2D>(bc):null;var n=nm!=null?AssetDatabase.LoadAssetAtPath<Texture2D>(nm):null;
   m.SetTexture("_BaseMap",t);m.SetColor("_BaseColor",c);m.SetFloat("_Smoothness",smooth);m.SetFloat("_Metallic",0);
   if(n!=null){m.SetTexture("_BumpMap",n);m.SetFloat("_BumpScale",1);m.EnableKeyword("_NORMALMAP");}else{m.SetTexture("_BumpMap",null);m.DisableKeyword("_NORMALMAP");}
   m.DisableKeyword("_EMISSION");m.SetColor("_EmissionColor",Color.black);m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
  }
  const string Heart306="Assets/HwaseongForteressGate/Textures/T_Heartwood_";   // grey weathered wood (갱구 틀이 사당 문처럼 붉게 읽히던 문제)
  class Mats306{public Material timber,board,coal,dust,pick,oreLoose,water,rope,straw,paper,ink,iron,loose,vein;
   public Material Slot(string s){switch(s){case "wood_dark":return timber;case "wood_board":return board;case "coal":return coal;case "ore_loose":return oreLoose;case "rope":return rope;case "straw":return straw;
    case "paper":return paper;case "ink":return ink;case "iron":return iron;case "rock_loose":return loose;case "vein":return vein??oreLoose;default:return KitMaterial297(s);}}}
  static Mats306 Materials306()
  {
   var mats=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).SelectMany(r=>r.sharedMaterials).Where(x=>x!=null).Distinct().ToArray();
   return new Mats306{timber=Mat306("Mine306_Timber",new Color(.86f,.84f,.82f),.06f,Heart306+"BC.png",Heart306+"N.png"),board=Mat306("Mine306_TimberBoard",new Color(.95f,.93f,.90f),.05f,Heart306+"BC.png",Heart306+"N.png"),
    coal=Mat306("Mine306_Coal",new Color(.035f,.035f,.04f),.55f),dust=Mat306("Mine306_CoalDust",new Color(.05f,.05f,.055f),.1f),pick=Mat306("Mine306_PickMark",new Color(.06f,.058f,.055f),.02f),
    oreLoose=Mat306("Mine306_OreLoose",new Color(.2f,.25f,.23f),.3f),water=Mat306("Mine306_Water",new Color(.02f,.03f,.035f),.93f),rope=Mat306("Mine306_Rope",new Color(.42f,.36f,.24f),.05f),
    straw=Mat306("Mine306_Straw",new Color(.45f,.39f,.26f),.04f),paper=Mat306("Mine306_Paper",new Color(.80f,.78f,.70f),.05f),ink=Mat306("Mine306_Ink",new Color(.04f,.04f,.045f),.08f),
    iron=mats.FirstOrDefault(x=>x.name.EndsWith("_WornIron"))??KitMaterial297("iron"),loose=mats.FirstOrDefault(x=>x.name.EndsWith("_LooseRock"))??KitMaterial297("stone_rough"),vein=mats.FirstOrDefault(x=>x.name.EndsWith("_Ore_Vein"))};
  }

  // -- helpers -------------------------------------------------------------------------------------------------------
  static Vector3 P306(float[] a,int i=0)=>new Vector3(a[i],a[i+1],a[i+2]);
  static Bounds Bounds306(GameObject g){var rs=g.GetComponentsInChildren<Renderer>().Where(r=>r.enabled&&!(r is ParticleSystemRenderer)).ToArray();if(rs.Length==0)return new Bounds(g.transform.position,Vector3.zero);var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;}
  // ground under a point: floor/terrain meshes (large static colliders) plus, in the yard, our spoil heap
  static bool Ground306(Transform mine,Vector3 from,float reach,out RaycastHit hit,Transform extra=null)
  {
   foreach(var h in Physics.RaycastAll(from,Vector3.down,reach,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
    if((extra!=null&&h.collider.transform.IsChildOf(extra))||Rock306(h.collider,null)&&!Under306(h.collider.transform)){hit=h;return true;}
   hit=default;return false;
  }
  static bool Under306(Transform t){for(;t!=null;t=t.parent)if(t.name==DressRoot297||t.name==Root306||t.name==Yard306)return true;return false;}
  static GameObject Lod306(string name,Transform parent,string[] files,Mats306 mats,(float,float,float) cuts,bool shadows=true)
  {
   var go=new GameObject(name);go.transform.SetParent(parent,false);var lods=new List<LOD>();float[] cut={cuts.Item1,cuts.Item2,cuts.Item3};
   for(int l=0;l<files.Length;l++)
   {
    var (mesh,slots)=LoadKitMesh297(M306+"/Meshes/"+files[l]+".json",A306+"/Meshes/"+files[l]+".asset");
    var c=new GameObject(files[l]);c.transform.SetParent(go.transform,false);c.AddComponent<MeshFilter>().sharedMesh=mesh;var r=c.AddComponent<MeshRenderer>();
    r.sharedMaterials=slots.Select(mats.Slot).ToArray();r.shadowCastingMode=shadows&&l==0?ShadowCastingMode.On:ShadowCastingMode.Off;lods.Add(new LOD(Mathf.Max(cut[Math.Min(l,2)],.001f),new[]{(Renderer)r}));
   }
   if(files.Length>1){var g=go.AddComponent<LODGroup>();g.SetLODs(lods.ToArray());g.RecalculateBounds();}
   return go;
  }
  static void BoxFromMesh306(GameObject go){var mf=go.GetComponentInChildren<MeshFilter>();if(mf==null)return;var b=mf.sharedMesh.bounds;var bc=go.AddComponent<BoxCollider>();bc.center=b.center;bc.size=b.size;}
  static float SnapBottom306(Transform mine,GameObject go,Transform extra=null)
  {
   Physics.SyncTransforms();var b=Bounds306(go);
   if(!Ground306(mine,new Vector3(b.center.x,b.max.y+1f,b.center.z),b.size.y+6f,out var h,extra))return float.NaN;
   float d=h.point.y-b.min.y+.005f;go.transform.position+=Vector3.up*d;return d;
  }
  static string V306(Vector3 v)=>v.x.ToString("F1",CultureInfo.InvariantCulture)+","+v.y.ToString("F1",CultureInfo.InvariantCulture)+","+v.z.ToString("F1",CultureInfo.InvariantCulture);
  static GameObject MeshObject306(string name,Transform parent,List<Vector3> v,List<int> t,Material mat,string asset,Transform space=null)
  {
   var mesh=new Mesh{name=name,indexFormat=v.Count>65000?IndexFormat.UInt32:IndexFormat.UInt16};
   if(space!=null)v=v.Select(space.InverseTransformPoint).ToList();
   mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.SetUVs(0,v.Select(p=>new Vector2(p.x+p.y*.3f,p.z+p.y*.7f)).ToList());mesh.RecalculateNormals();mesh.RecalculateBounds();
   DevSceneKit.EnsureFolder(A306+"/Meshes");mesh=ArtMesh(mesh,A306+"/Meshes/"+asset+".asset");
   var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;return go;
  }
  static void Quad306(List<Vector3> v,List<int> t,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 up)
  {
   int i=v.Count;v.Add(a);v.Add(b);v.Add(c);v.Add(d);
   if(Vector3.Dot(Vector3.Cross(b-a,c-a),up)>=0)t.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});else t.AddRange(new[]{i,i+2,i+1,i,i+3,i+2});   // Unity: clockwise = front
  }

  // ray-fitted strip along a wall (coal seams): like the #297 ore seams, several strands, tapered ends
  static int FitStrip306(Transform mine,M306Strip s,int seed,List<Vector3> verts,List<int> tris)
  {
   var origin=mine.TransformPoint(P306(s.origin));var dirW=mine.TransformDirection(new Vector3(s.dir[0],0,s.dir[1])).normalized;var along=Vector3.Cross(Vector3.up,dirW).normalized;
   const int M=22;int hits=0;var rnd=new System.Random(seed);
   for(int strand=0;strand<Math.Max(1,s.strands);strand++)
   {
    int start=verts.Count;float lift=strand==0?0:((strand&1)==1?1:-1)*s.width*(1.25f+.35f*strand),scale=strand==0?1f:.45f,len=s.length*(strand==0?1f:.55f),shift=strand==0?0:(float)(rnd.NextDouble()-.5)*s.length*.45f;
    for(int i=0;i<=M;i++)
    {
     float a=(i/(float)M-.5f)*len+shift;var from=origin+along*a+Vector3.up*(lift+a*Mathf.Tan(s.tilt*Mathf.Deg2Rad)+.05f*Mathf.Sin(i*1.9f+seed+strand));
     if(!RockRay306(mine,from,dirW,9,out var h)){if(verts.Count>start)break;continue;}
     var n=h.normal;if(Vector3.Dot(n,-dirW)<0)n=-n;var across=(Vector3.up-n*Vector3.Dot(Vector3.up,n)).normalized;
     float taper=Mathf.Min(1f,.15f+4f*i/M,.15f+4f*(M-i)/M),w=s.width*.5f*scale*taper*(1+.35f*Mathf.Sin(i*2.3f+seed*.7f)+.2f*Mathf.Sin(i*5.1f+strand));
     int b=verts.Count;verts.Add(h.point+n*.02f-across*w);verts.Add(h.point+n*.02f+across*w);if(strand==0)hits++;
     if(b-start>=2)tris.AddRange(Vector3.Dot(Vector3.Cross(verts[b]-verts[b-2],verts[b-1]-verts[b-2]),n)>=0?new[]{b-2,b,b-1,b-1,b,b+1}:new[]{b-2,b-1,b,b-1,b+1,b});   // face the gallery
    }
   }
   return hits;
  }
  // pick-mark hatching (정 자국): short chisel strokes in rows on the rock, herringbone blocks
  static int Picks306(Transform mine,M306Picks p,List<Vector3> v,List<int> t)
  {
   var origin=mine.TransformPoint(P306(p.origin));var dirW=mine.TransformDirection(new Vector3(p.dir[0],0,p.dir[1])).normalized;var along=Vector3.Cross(Vector3.up,dirW).normalized;
   var rnd=new System.Random(p.seed);float R()=>(float)rnd.NextDouble();int made=0,row=0;
   for(float y=0;y<=p.height;y+=.17f,row++)
    for(float x=-p.width/2+R()*.1f;x<=p.width/2;x+=.12f+R()*.05f)
    {
     if(R()<.18f)continue;
     if(!RockRay306(mine,origin+along*x+Vector3.up*y,dirW,9,out var h))continue;
     var n=h.normal;if(Vector3.Dot(n,-dirW)<0)n=-n;if(Vector3.Dot(n,-dirW)<.3f)continue;
     var a1=(along-n*Vector3.Dot(along,n)).normalized;var a2=Vector3.Cross(n,a1);
     float ang=(p.angle*((row/2)%2==0?1:-1)+(R()-.5f)*16f)*Mathf.Deg2Rad,len=.22f+R()*.12f,w=.022f+R()*.014f;
     var d=a1*Mathf.Cos(ang)+a2*Mathf.Sin(ang);var q=Vector3.Cross(n,d);var c=h.point+n*.012f;
     Quad306(v,t,c-d*len/2-q*w,c+d*len/2-q*w*.4f,c+d*len/2+q*w*.4f,c-d*len/2+q*w,n);made++;
    }
   return made;
  }
  // floor patch (black magic-stone dust / puddle): noisy disc draped on the floor by ray
  static bool Patch306(Transform mine,Vector3 c,float radius,int seed,float lift,List<Vector3> v,List<int> t,Transform extra=null)
  {
   const int N=18;var ring=new List<Vector3>();if(!Ground306(mine,c+Vector3.up*1.5f,4f,out var hc,extra))return false;
   for(int i=0;i<N;i++)
   {
    float a=i*Mathf.PI*2/N,r=radius*(1+.22f*Mathf.Sin(i*1.7f+seed)+.12f*Mathf.Sin(i*3.1f+seed*2)),e=(i%2==0?1f:.8f);
    var p=c+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*r*e;ring.Add(Ground306(mine,p+Vector3.up*1.5f,4f,out var h,extra)?h.point+Vector3.up*lift*.4f:new Vector3(p.x,hc.point.y,p.z));
   }
   int b=v.Count;v.Add(hc.point+Vector3.up*lift);v.AddRange(ring);
   for(int i=0;i<N;i++)t.AddRange(new[]{b,b+1+(i+1)%N,b+1+i});
   return true;
  }
  // ribbon on the floor (burnt fuse)
  static int Ribbon306(Transform mine,List<Vector3> pts,float width,List<Vector3> v,List<int> t)
  {
   var g=new List<Vector3>();foreach(var p in pts)if(Ground306(mine,p+Vector3.up*1.5f,5f,out var h))g.Add(h.point+Vector3.up*.012f);
   for(int i=0;i+1<g.Count;i++){var d=(g[i+1]-g[i]);d.y=0;var n=Vector3.Cross(Vector3.up,d.normalized)*width/2;Quad306(v,t,g[i]-n,g[i+1]-n,g[i+1]+n,g[i]+n,Vector3.up);}
   return g.Count;
  }
  static void Box306(List<Vector3> v,List<int> t,Vector3 c,Vector3 x,Vector3 y,Vector3 z)   // x,y,z = half axes
  {
   Vector3 P(int i,int j,int k)=>c+x*i+y*j+z*k;
   Quad306(v,t,P(-1,1,-1),P(1,1,-1),P(1,1,1),P(-1,1,1),y);Quad306(v,t,P(-1,-1,-1),P(-1,1,-1),P(-1,1,1),P(-1,-1,1),-x);Quad306(v,t,P(1,-1,-1),P(1,-1,1),P(1,1,1),P(1,1,-1),x);
   Quad306(v,t,P(-1,-1,-1),P(1,-1,-1),P(1,1,-1),P(-1,1,-1),-z);Quad306(v,t,P(-1,-1,1),P(-1,1,1),P(1,1,1),P(1,-1,1),z);
  }

  // -- dry ------------------------------------------------------------------------------------------------------------
  static string Dry306(Transform mine,M306Manifest m)
  {
   var sb=new List<string>{"#306 mine dry run (no changes). manifest status="+m.status+" chunks="+m.chunks.Length+" carts="+m.carts.Length+" props="+m.props.Length+" coal="+m.coal.Length+" picks="+m.picks.Length+" patches="+m.patches.Length+" fuses="+m.fuses.Length+" yard meshes="+m.yard.meshes.Length+" yard props="+m.yard.props.Length+" sounds="+m.sounds.Length};
   foreach(var p in m.props.Concat(m.yard.props).Select(p=>p.prefab).Distinct())sb.Add((AssetDatabase.LoadAssetAtPath<GameObject>(p)!=null?"  ok  ":"  MISSING ")+p);
   foreach(var f in new[]{Heart306+"BC.png",Heart306+"N.png"})sb.Add((AssetDatabase.LoadAssetAtPath<Texture2D>(f)!=null?"  ok  ":"  MISSING ")+f);
   var missing=m.chunks.SelectMany(c=>c.lods.Concat(c.collision!=null&&c.collision.Length>0?new[]{c.collision}:new string[0])).Where(f=>!File.Exists(M306+"/Meshes/"+f+".json")).ToArray();
   sb.Add("mesh json missing: "+missing.Length+(missing.Length>0?" e.g. "+missing[0]:""));
   sb.Add("existing roots: interior="+(mine.Find(Root306)!=null)+" yard="+(mine.Find(Yard306)!=null)+" #297 dressing="+(mine.Find(DressRoot297)!=null)+" "+Path.GetFileName(Original306)+"="+File.Exists(Original306));
   var seams=mine.Find(DressRoot297+"/Ore_Seams297");sb.Add("#297 ore seams: "+(seams!=null?seams.Cast<Transform>().Count(t=>t.name.StartsWith("Ore_Seam297_")):0));
   var lamps=Lamps306(mine,m.lamps);sb.Add("lanterns "+m.lamps.prefix+"*: "+lamps.Count+" (keep "+string.Join(",",m.lamps.keep)+") lights="+lamps.Sum(l=>l.Value.GetComponentsInChildren<Light>(true).Length)+" emissive mats="+lamps.SelectMany(l=>l.Value.GetComponentsInChildren<Renderer>(true)).SelectMany(r=>r.sharedMaterials).Where(x=>x!=null&&x.IsKeywordEnabled("_EMISSION")).Distinct().Count());
   var ui=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).FirstOrDefault();
   foreach(var s in m.sounds.Where(s=>s.frame=="world"))sb.Add("sound "+s.clip+" zone="+(ui!=null&&ui.MapData!=null?ui.MapData.ZoneAt(P306(s.world))?.Id??"none (outdoor: skipped)":"no map"));
   foreach(var w in m.warnings??new string[0])sb.Add("generator warning: "+w);
   return string.Join("\n",sb);
  }
  static Dictionary<int,Transform> Lamps306(Transform mine,M306Lamps l)
  {
   var d=new Dictionary<int,Transform>();
   foreach(var t in mine.GetComponentsInChildren<Transform>(true))if(t.name.StartsWith(l.prefix)&&int.TryParse(t.name.Substring(l.prefix.Length),out int i)&&!d.ContainsKey(i))d[i]=t;
   return d;
  }

  // -- apply ----------------------------------------------------------------------------------------------------------
  static string Apply306(Transform mine,M306Manifest m)
  {
   var rep=new List<string>();var mats=Materials306();
   foreach(var n in new[]{Root306,Yard306}){var o=mine.Find(n);if(o!=null)Object.DestroyImmediate(o.gameObject);}
   Physics.SyncTransforms();
   var root=new GameObject(Root306).transform;root.SetParent(mine,false);
   bool back=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=true;
   try
   {
    // 1. packwalls + timber sets (chunked, LOD, collision)
    var chunks=new GameObject("Chunks").transform;chunks.SetParent(root,false);int cols=0;
    foreach(var c in m.chunks)
    {
     var go=Lod306(c.name,chunks,c.lods,mats,Cuts306.TryGetValue(c.layer,out var cut)?cut:(.2f,.05f,.01f));
     if(!string.IsNullOrEmpty(c.collision)){var (cm,_)=LoadKitMesh297(M306+"/Meshes/"+c.collision+".json",A306+"/Meshes/"+c.collision+".asset");var cg=new GameObject("Collision");cg.transform.SetParent(go.transform,false);cg.AddComponent<MeshCollider>().sharedMesh=cm;cols++;}
    }
    rep.Add("chunks "+m.chunks.Length+" (collision "+cols+"): packwall "+m.chunks.Count(c=>c.layer=="Packwall")+", timbers "+m.chunks.Count(c=>c.layer=="Timbers"));
    foreach(var s in m.singles){var go=Lod306(s.mesh,root,new[]{s.mesh+"_LOD0"},mats,(.01f,0,0));go.transform.localPosition=P306(s.local);if(s.collider=="box")BoxFromMesh306(go);}
    Physics.SyncTransforms();
    // 2. carts
    var carts=new GameObject("Carts").transform;carts.SetParent(root,false);
    foreach(var c in m.carts)
    {
     var go=Lod306(c.mesh,carts,new[]{c.mesh+"_LOD0",c.mesh+"_LOD1"},mats,(.08f,.01f,0));go.transform.localPosition=P306(c.local);go.transform.localEulerAngles=P306(c.euler);
     float d=c.snap=="ground"?SnapBottom306(mine,go):0;if(c.collider=="box")BoxFromMesh306(go);
     rep.Add("cart "+c.mesh+" "+c.note+" at "+V306(go.transform.position)+(c.snap=="ground"?" snap "+(float.IsNaN(d)?"NO FLOOR":d.ToString("F2")):""));
    }
    // 3. floor patches + fuses
    {
     var dv=new List<Vector3>();var dt=new List<int>();var wv=new List<Vector3>();var wt=new List<int>();int ok=0;
     foreach(var p in m.patches)ok+=Patch306(mine,mine.TransformPoint(P306(p.local)),p.radius,p.seed,p.kind=="water"?.022f:.014f,p.kind=="water"?wv:dv,p.kind=="water"?wt:dt)?1:0;
     if(dv.Count>0)MeshObject306("Mine306_CoalDust",root,dv,dt,mats.dust,"Mine306_CoalDust",root);if(wv.Count>0)MeshObject306("Mine306_Puddles",root,wv,wt,mats.water,"Mine306_Puddles",root);
     var fv=new List<Vector3>();var ft=new List<int>();int fp=0;
     foreach(var f in m.fuses.Where(f=>f.frame=="mine")){var pts=new List<Vector3>();for(int i=0;i+2<f.points.Length;i+=3)pts.Add(mine.TransformPoint(P306(f.points,i)));fp+=Ribbon306(mine,pts,.028f,fv,ft);}
     if(fv.Count>0)MeshObject306("Mine306_Fuses",root,fv,ft,mats.ink,"Mine306_Fuses",root);
     rep.Add("floor patches "+ok+"/"+m.patches.Length+", fuse points on floor "+fp);
    }
    // 4. coal seams + pick marks (ray-fitted to the rock only)
    {
     var cv=new List<Vector3>();var ct=new List<int>();int k=0,fitted=0;
     foreach(var s in m.coal)fitted+=FitStrip306(mine,s,31+k++,cv,ct)>=4?1:0;
     if(cv.Count>0)MeshObject306("Mine306_CoalSeams",root,cv,ct,mats.coal,"Mine306_CoalSeams",root);
     var pv=new List<Vector3>();var pt=new List<int>();int strokes=0;foreach(var p in m.picks)strokes+=Picks306(mine,p,pv,pt);
     if(pv.Count>0)MeshObject306("Mine306_PickMarks",root,pv,pt,mats.pick,"Mine306_PickMarks",root);
     rep.Add("coal seams "+fitted+"/"+m.coal.Length+", pick strokes "+strokes+" in "+m.picks.Length+" clusters");
    }
    // 5. ore crystal clusters on the #297 seams (the in-wall vein is the only thing that glows; the vein material is reused)
    {
     var seams=mine.Find(DressRoot297+"/Ore_Seams297");int made=0;var cr=new GameObject("OreCrystals").transform;cr.SetParent(root,false);var rnd=new System.Random(306);
     if(seams==null)rep.Add("ore crystals: NO #297 seams (run finish297 cave-dressing first)");
     else foreach(Transform s in seams)
     {
      if(!s.name.StartsWith("Ore_Seam297_"))continue;var glow=seams.Find(s.name.Replace("Seam","Glow"));var mf=s.GetComponent<MeshFilter>();if(glow==null||mf==null)continue;
      var wv=mf.sharedMesh.vertices.Select(s.TransformPoint).ToArray();var c=wv.Aggregate(Vector3.zero,(a,b)=>a+b)/wv.Length;var n=(glow.position-c).normalized;
      var far=wv.OrderByDescending(p=>(p-c).sqrMagnitude).First();
      for(int i=0;i<Math.Max(1,m.crystals.perSeam);i++)
      {
       var at=i==0?c:Vector3.Lerp(c,far,.62f);if(!RockRay306(mine,at+n*.8f,-n,2f,out var h))continue;
       string v=m.crystals.variants[(made+i)%m.crystals.variants.Length];var go=Lod306(s.name.Replace("Ore_Seam297","Crystals")+"_"+i,cr,new[]{v+"_LOD0"},mats,(.01f,0,0),false);
       go.transform.SetPositionAndRotation(h.point,Quaternion.LookRotation(h.normal*(Vector3.Dot(h.normal,n)<0?-1:1),Vector3.up)*Quaternion.Euler(0,0,(float)rnd.NextDouble()*360));go.transform.localScale=Vector3.one*(.85f+(float)rnd.NextDouble()*.4f);made++;
      }
     }
     rep.Add("ore crystal clusters "+made+(mats.vein==null?" (vein material MISSING: ore_loose used)":""));
    }
    // 6. props (prefabs)
    var propRoot=new GameObject("Props").transform;propRoot.SetParent(root,false);
    foreach(var p in m.props)rep.Add(Prop306(mine,p,propRoot,null));
    // 7. yard (world-aligned root at the portal origin; ground by ray)
    rep.AddRange(Yard306Build(mine,m,mats));
    // 8. sounds
    rep.AddRange(Sounds306(mine,m,root));
    // 9. materials (grey timber for #297 dressing/portal/lantern uprights) + lamps (only the kept lanterns lit, no emissive meshes)
    rep.AddRange(Swap306(mine,m,mats));
   }
   finally{Physics.queriesHitBackfaces=back;}
   Physics.SyncTransforms();Save292();
   rep.Add("saved "+SceneManager.GetActiveScene().path+" — NavMesh NOT re-baked (packwalls, carts, heap change the walkable area: bake after `check`)");
   return string.Join("\n",rep);
  }

  static string Prop306(Transform mine,M306Prop p,Transform parent,Transform extra)
  {
   var pf=AssetDatabase.LoadAssetAtPath<GameObject>(p.prefab);if(pf==null)return "prop MISSING "+p.prefab;
   var go=(GameObject)PrefabUtility.InstantiatePrefab(pf,parent);string note="";
   if(p.frame=="world"){go.transform.position=P306(p.world);go.transform.rotation=Quaternion.Euler(P306(p.euler));}
   else{go.transform.position=mine.TransformPoint(P306(p.local));go.transform.rotation=mine.rotation*Quaternion.Euler(P306(p.euler));}
   if(p.strip)
   {
    PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
    foreach(var r in go.GetComponentsInChildren<ParticleSystemRenderer>(true))Object.DestroyImmediate(r);foreach(var s in go.GetComponentsInChildren<ParticleSystem>(true))Object.DestroyImmediate(s);
    int lights=0;foreach(var l in go.GetComponentsInChildren<Light>(true)){Object.DestroyImmediate(l);lights++;}
    int glow=0;foreach(var r in go.GetComponentsInChildren<Renderer>(true))if(r.sharedMaterials.Any(x=>x!=null&&x.IsKeywordEnabled("_EMISSION"))){r.enabled=false;glow++;}
    note+=" stripped lights "+lights+" emissive renderers off "+glow;
   }
   Physics.SyncTransforms();var b=Bounds306(go);float ext=Mathf.Max(b.size.x,b.size.y,b.size.z);note+=" native "+V306(b.size);
   if(p.size>0&&ext>1e-3f&&(ext<p.size/2.5f||ext>p.size*2.5f)){go.transform.localScale*=p.size/ext;note+=" rescaled x"+(p.size/ext).ToString("F2");}
   if(p.snap=="ground"){float d=SnapBottom306(mine,go,extra);note+=float.IsNaN(d)?" NO GROUND":" snap "+d.ToString("F2");}
   else if(p.snap=="center"){Physics.SyncTransforms();var c=Bounds306(go).center;go.transform.position+=(p.frame=="world"?P306(p.world):mine.TransformPoint(P306(p.local)))-c;}
   return "prop "+Path.GetFileNameWithoutExtension(p.prefab)+" ("+p.note+") at "+V306(go.transform.position)+note;
  }

  static IEnumerable<string> Yard306Build(Transform mine,M306Manifest m,Mats306 mats)
  {
   var rep=new List<string>();var y=m.yard;var yo=P306(y.origin);
   var yard=new GameObject(Yard306).transform;yard.SetParent(mine,false);yard.SetPositionAndRotation(yo,Quaternion.identity);
   // heap: offline terrain (height.bytes) vs the scene ground at the footprint samples -> vertical correction
   var diffs=new List<float>();
   for(int i=0;i+2<y.heap.samples.Length;i+=3){var s=new Vector3(y.heap.samples[i],y.heap.samples[i+2],y.heap.samples[i+1]);if(Ground306(mine,s+Vector3.up*25,60,out var h))diffs.Add(h.point.y-s.y);}
   float off=diffs.Count>0?diffs.Average():0;if(Mathf.Abs(off)<.05f)off=0;
   var heap=Lod306("SpoilHeap",yard,new[]{y.heap.mesh+"_LOD0"},mats,(.01f,0,0));heap.transform.localPosition=Vector3.up*off;
   var (hc,_)=LoadKitMesh297(M306+"/Meshes/"+y.heap.collision+".json",A306+"/Meshes/"+y.heap.collision+".asset");var hcol=new GameObject("Collision");hcol.transform.SetParent(heap.transform,false);hcol.AddComponent<MeshCollider>().sharedMesh=hc;
   rep.Add("spoil heap: ground vs offline terrain at "+diffs.Count+" samples ["+string.Join(",",diffs.Select(d=>d.ToString("F2")))+"] -> offset "+off.ToString("F2"));
   Physics.SyncTransforms();
   // tip track (grounded on terrain/heap)
   {
    var pts=new List<Vector3>();for(int i=0;i+2<y.tipTrack.points.Length;i+=3)pts.Add(P306(y.tipTrack.points,i));
    var dense=new List<Vector3>();for(int i=0;i+1<pts.Count;i++){float L=Vector3.Distance(pts[i],pts[i+1]);int k=Mathf.Max(1,Mathf.RoundToInt(L/.8f));for(int j=0;j<k;j++)dense.Add(Vector3.Lerp(pts[i],pts[i+1],j/(float)k));}dense.Add(pts[pts.Count-1]);
    var g=new List<Vector3>();foreach(var p in dense)if(Ground306(mine,p+Vector3.up*6,15,out var h,heap.transform))g.Add(h.point);
    var rv=new List<Vector3>();var rt=new List<int>();var sv=new List<Vector3>();var st=new List<int>();float half=y.tipTrack.gauge/2;
    for(int i=0;i<g.Count;i++)
    {
     var d=(i+1<g.Count?g[i+1]-g[i]:g[i]-g[i-1]);var f=new Vector3(d.x,0,d.z).normalized;var n=Vector3.Cross(Vector3.up,f);
     Box306(sv,st,g[i]+Vector3.up*.05f,n*.75f,Vector3.up*.05f,f*.11f);
     if(i+1<g.Count)foreach(int sgn in new[]{-1,1}){var a=g[i]+n*sgn*half+Vector3.up*.19f;var b=g[i+1]+n*sgn*half+Vector3.up*.19f;var mid=(a+b)/2;var ax=b-a;Box306(rv,rt,mid,n*.035f,Vector3.up*.045f,ax/2);}
    }
    var rails=MeshObject306("TipTrack_Rails",yard,rv,rt,mats.iron,"TipTrack_Rails",yard);var sl=MeshObject306("TipTrack_Sleepers",yard,sv,st,mats.board,"TipTrack_Sleepers",yard);
    rep.Add("tip track: "+g.Count+"/"+dense.Count+" points grounded");
   }
   // object meshes + prefabs
   foreach(var o in y.meshes)
   {
    var files=File.Exists(M306+"/Meshes/"+o.mesh+"_LOD1.json")?new[]{o.mesh+"_LOD0",o.mesh+"_LOD1"}:new[]{o.mesh+"_LOD0"};
    var go=Lod306(o.mesh,yard,files,mats,(.05f,.01f,0));var w=P306(o.world);
    go.transform.SetPositionAndRotation(Ground306(mine,w+Vector3.up*8,20,out var h,heap.transform)?h.point:w,Quaternion.Euler(o.pitch,o.yaw,0));
    string note="";if(o.pitch!=0){float d=SnapBottom306(mine,go,heap.transform);note=" snap "+d.ToString("F2");}
    if(o.collider=="box")BoxFromMesh306(go);rep.Add("yard "+o.mesh+" ("+o.note+") at "+V306(go.transform.position)+note);
   }
   Physics.SyncTransforms();
   foreach(var p in y.props)rep.Add(Prop306(mine,p,yard,heap.transform));
   var fv=new List<Vector3>();var ft=new List<int>();int fp=0;
   foreach(var f in m.fuses.Where(f=>f.frame=="world")){var pts=new List<Vector3>();for(int i=0;i+2<f.points.Length;i+=3)pts.Add(P306(f.points,i));fp+=Ribbon306(mine,pts,.028f,fv,ft);}
   if(fv.Count>0)MeshObject306("Yard_Fuses",yard,fv,ft,mats.ink,"Yard_Fuses",yard);rep.Add("yard fuse points on ground "+fp);
   return rep;
  }

  static IEnumerable<string> Sounds306(Transform mine,M306Manifest m,Transform root)
  {
   var rep=new List<string>();var roots=SceneManager.GetActiveScene().GetRootGameObjects();
   var ui=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).FirstOrDefault();var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).FirstOrDefault();
   var mix=AssetDatabase.FindAssets("t:WorldMacroAudioMixProfileSO").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<WorldMacroAudioMixProfileSO>).FirstOrDefault(x=>x!=null&&x.Sfx!=null);
   if(ui==null||session==null||mix==null){rep.Add("sounds SKIPPED: ui="+(ui!=null)+" session="+(session!=null)+" mix="+(mix!=null));return rep;}
   var sr=new GameObject("Sounds").transform;sr.SetParent(root,false);DevSceneKit.EnsureFolder(A306+"/Audio");
   foreach(var s in m.sounds)
   {
    var pos=s.frame=="world"?P306(s.world):mine.TransformPoint(P306(s.local));var zone=ui.MapData!=null?ui.MapData.ZoneAt(pos):null;
    if(s.frame=="world"&&zone==null){rep.Add("sound "+s.clip+" SKIPPED: outdoors (CompactCaveAmbience only plays inside a map zone)");continue;}
    string src="../Art/Audio/Cave235/"+s.clip+".wav",dst=A306+"/Audio/"+s.clip+".wav";if(!File.Exists(src)){rep.Add("sound "+s.clip+" MISSING (python Tools/Art/build_compact_cave_audio.py)");continue;}
    bool changed=!File.Exists(dst)||!File.ReadAllBytes(dst).SequenceEqual(File.ReadAllBytes(src));if(changed){File.Copy(src,dst,true);AssetDatabase.ImportAsset(dst);}
    var imp=AssetImporter.GetAtPath(dst) as AudioImporter;if(imp==null){AssetDatabase.ImportAsset(dst);imp=AssetImporter.GetAtPath(dst) as AudioImporter;}
    if(imp==null){rep.Add("sound "+s.clip+" NOT IMPORTED "+dst);continue;}
    if(!imp.forceToMono){imp.forceToMono=true;var ss=imp.defaultSampleSettings;ss.loadType=AudioClipLoadType.CompressedInMemory;ss.compressionFormat=AudioCompressionFormat.Vorbis;ss.quality=.7f;imp.defaultSampleSettings=ss;imp.SaveAndReimport();}
    var g=new GameObject(s.clip);g.transform.SetParent(sr,false);g.transform.position=pos;var a=g.AddComponent<AudioSource>();a.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(dst);a.playOnAwake=false;a.volume=0;a.outputAudioMixerGroup=mix.Sfx;
    g.AddComponent<AudioLowPassFilter>().cutoffFrequency=2600;var amb=g.AddComponent<CompactCaveAmbience>();amb.Session=session;amb.Map=ui.MapData;amb.Mix=mix;amb.Level=s.level;amb.Radius=s.radius;amb.Intermittent=s.intermittent;if(zone!=null)amb.ZoneId=zone.Id;
    rep.Add("sound "+s.clip+" at "+V306(pos)+" zone="+(zone?.Id??"-")+" level="+s.level+(s.intermittent?" intermittent":""));
   }
   return rep;
  }

  // grey timber on the #297 wood + lantern uprights; lanterns: only `keep` lit, emissive lantern materials -> non-emissive copies.
  // Originals are recorded once per scene in Mine306/original_<scene>.json (merged on later applies) and restored by `revert`.
  static IEnumerable<string> Swap306(Transform mine,M306Manifest m,Mats306 mats)
  {
   var rep=new List<string>();string file=Original306;var o=File.Exists(file)?JsonUtility.FromJson<M306Original>(File.ReadAllText(file)):new M306Original();
   void Record(Renderer r){string p=HierarchyPath(r.transform);if(!o.renderers.Contains(p)){o.renderers.Add(p);o.materials.Add(string.Join("|",r.sharedMaterials.Select(AssetDatabase.GetAssetPath)));}}
   var dark=AssetDatabase.LoadAssetAtPath<Material>(A297+"/Materials/wood_dark.mat");var board=AssetDatabase.LoadAssetAtPath<Material>(A297+"/Materials/wood_board.mat");
   var lamps=Lamps306(mine,m.lamps);
   var upright=new HashSet<Material>(lamps.Values.SelectMany(l=>l.GetComponentsInChildren<Renderer>(true)).Where(r=>r.name=="Upright"||r.name=="LampBracket").SelectMany(r=>r.sharedMaterials).Where(x=>x!=null));
   int swapped=0;var dressT=mine.Find(DressRoot297);
   foreach(var r in mine.GetComponentsInChildren<MeshRenderer>(true))
   {
    if(Under306(r.transform)&&!(dressT!=null&&r.transform.IsChildOf(dressT)))continue;
    var ms=r.sharedMaterials;bool hit=false;
    for(int i=0;i<ms.Length;i++){var x=ms[i];if(x==null)continue;if(x==dark||upright.Contains(x)){ms[i]=mats.timber;hit=true;}else if(x==board){ms[i]=mats.board;hit=true;}}
    if(hit){Record(r);r.sharedMaterials=ms;swapped++;}
   }
   rep.Add("grey timber: "+swapped+" renderers (wood_dark/wood_board/lantern upright -> Mine306_Timber/Board)");
   int off=0,unlit=0;var keep=new HashSet<int>(m.lamps.keep??new int[0]);
   foreach(var kv in lamps)
   {
    foreach(var l in kv.Value.GetComponentsInChildren<Light>(true)){string p=HierarchyPath(l.transform)+"#"+l.GetType().Name;if(!o.lights.Contains(p)){o.lights.Add(p);o.lightEnabled.Add(l.enabled);}bool on=keep.Contains(kv.Key);if(l.enabled!=on){l.enabled=on;EditorUtility.SetDirty(l);}if(!on)off++;}
    foreach(var r in kv.Value.GetComponentsInChildren<Renderer>(true))
    {
     var ms=r.sharedMaterials;bool hit=false;
     for(int i=0;i<ms.Length;i++){var x=ms[i];if(x==null||!x.IsKeywordEnabled("_EMISSION"))continue;string cp=A306+"/Materials/"+x.name+"_NoGlow306.mat";var c=AssetDatabase.LoadAssetAtPath<Material>(cp);
      if(c==null){c=new Material(x){name=x.name+"_NoGlow306"};c.DisableKeyword("_EMISSION");c.SetColor("_EmissionColor",Color.black);c.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;AssetDatabase.CreateAsset(c,cp);}ms[i]=c;hit=true;}
     if(hit){Record(r);r.sharedMaterials=ms;unlit++;}
    }
   }
   rep.Add("lanterns: "+lamps.Count+" found, kept lit ["+string.Join(",",keep)+"], lights off "+off+", emissive lantern renderers made matte "+unlit);
   File.WriteAllText(file,JsonUtility.ToJson(o,true));
   return rep;
  }

  // -- revert ---------------------------------------------------------------------------------------------------------
  static string Revert306(Transform mine)
  {
   var rep=new List<string>();
   foreach(var n in new[]{Root306,Yard306}){var o=mine.Find(n);if(o!=null){Object.DestroyImmediate(o.gameObject);rep.Add("removed "+n);}}
   string file=Original306;
   if(File.Exists(file))
   {
    var o=JsonUtility.FromJson<M306Original>(File.ReadAllText(file));var all=mine.GetComponentsInChildren<Transform>(true).GroupBy(t=>HierarchyPath(t),StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.Ordinal);int r=0,l=0;
    for(int i=0;i<o.renderers.Count;i++)if(all.TryGetValue(o.renderers[i],out var t)&&t.TryGetComponent<Renderer>(out var ren)){ren.sharedMaterials=o.materials[i].Split('|').Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();r++;}
    for(int i=0;i<o.lights.Count;i++){var path=o.lights[i].Split('#')[0];if(all.TryGetValue(path,out var t)&&t.TryGetComponent<Light>(out var li)){li.enabled=o.lightEnabled[i];l++;}}
    File.Delete(file);rep.Add("restored materials on "+r+"/"+o.renderers.Count+" renderers, lights "+l+"/"+o.lights.Count);
   }
   Physics.SyncTransforms();Save292();rep.Add("saved "+SceneManager.GetActiveScene().path);
   return string.Join("\n",rep);
  }

  // -- check (read-only) ----------------------------------------------------------------------------------------------
  static string Check306(Transform mine,M306Manifest m)
  {
   var rep=new List<string>();var root=mine.Find(Root306);if(root==null)return "REFUSED not applied (no "+Root306+")";
   bool back=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=true;
   try
   {
    // walk lane every 2 m of the drift: the span between the first hits left/right of the centre (rock, packwall, posts) is swept
    // by a CC capsule (r .28, h 1.75, step .3; PLAN §2-7) every .1 m on the local floor; the widest free run (+2r) is the lane,
    // so a cart or prop sitting on the centre line counts (a ray starting inside a cart's box never sees it)
    const float cr=.28f,capH=1.75f,capStep=.3f;float floorY=m.floor!=0?m.floor:135.8f;
    var narrow=new List<string>();float min=99;int probes=0;
    for(int i=0;i+3<(m.walk?.Length??0);i+=4)
    {
     var c=mine.TransformPoint(new Vector3(m.walk[i],floorY+.7f,m.walk[i+1]));if(!Ground306(mine,c+Vector3.up*1f,4,out var fl))continue;
     var t=mine.TransformDirection(new Vector3(m.walk[i+2],0,m.walk[i+3])).normalized;var n=Vector3.Cross(Vector3.up,t);
     float L=8,R=8;Collider cL=null,cR=null;
     foreach(float hgt in new[]{.6f,1.5f})
     {
      var o=fl.point+Vector3.up*hgt;
      if(Physics.Raycast(o,-n,out var hl,8,~0,QueryTriggerInteraction.Ignore)&&hl.distance<L){L=hl.distance;cL=hl.collider;}
      if(Physics.Raycast(o,n,out var hr,8,~0,QueryTriggerInteraction.Ignore)&&hr.distance<R){R=hr.distance;cR=hr.collider;}
     }
     float best=0,run=-1;string by="";
     for(float x=-L+cr;x<=R-cr+1e-4f;x+=.1f)
     {
      bool free=Ground306(mine,fl.point+n*x+Vector3.up*1.5f,3f,out var g);Collider k=null;
      if(free){k=Physics.OverlapCapsule(g.point+Vector3.up*(capStep+cr),g.point+Vector3.up*(capH-cr),cr,~0,QueryTriggerInteraction.Ignore).FirstOrDefault(q=>!(q is CharacterController));free=k==null;}
      if(free){run=run<0?0:run+.1f;best=Mathf.Max(best,run+2*cr);}else{run=-1;if(k!=null&&by.Length==0)by=k.name;}
     }
     probes++;if(best<min)min=best;
     if(best<3f)narrow.Add(V306(fl.point)+" lane="+best.ToString("F2")+" span="+(L+R).ToString("F2")+" ("+(cL!=null?cL.name:"-")+" | "+(cR!=null?cR.name:"-")+(by.Length>0?"; first blocker "+by:"")+")");
    }
    rep.Add("walk lane (CC capsule r.28 h1.75 step.3): probes "+probes+", min "+min.ToString("F2")+" m, below 3 m: "+narrow.Count);rep.AddRange(narrow.Take(30).Select(q=>"  "+q));
    // lights touching each #306 chunk (Mobile renderer: 4 additional lights per object; PC renderer is Forward+)
    var lights=Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l=>l.isActiveAndEnabled&&l.type!=LightType.Directional).ToArray();int worst=0;var over=new List<string>();
    foreach(var r in root.GetComponentsInChildren<MeshRenderer>())
    {
     if(!r.name.EndsWith("_LOD0"))continue;var b=r.bounds;int n=lights.Count(l=>b.SqrDistance(l.transform.position)<l.range*l.range);worst=Math.Max(worst,n);if(n>4)over.Add(r.name+"="+n);
    }
    rep.Add("lights per #306 LOD0 renderer: max "+worst+(over.Count>0?", over 4: "+string.Join(" ",over.Take(20)):""));
    // non-#306 props hidden behind a packwall (ray from the prop toward the drift centre hits a packwall collider first)
    var packs=root.GetComponentsInChildren<MeshCollider>().Where(c=>c.transform.parent!=null&&c.transform.parent.name.StartsWith("Packwall")).ToArray();var hidden=new List<string>();
    var dressT=mine.Find(DressRoot297);var centres=new List<Vector3>();for(int i=0;i+3<m.walk.Length;i+=4)centres.Add(mine.TransformPoint(new Vector3(m.walk[i],0,m.walk[i+1])));
    foreach(var r in mine.GetComponentsInChildren<Renderer>())
    {
     if(Under306(r.transform)&&!(dressT!=null&&r.transform.IsChildOf(dressT)))continue;var b=r.bounds;if(b.size.magnitude>4||b.size.magnitude<.05f)continue;
     var cen=centres.OrderBy(q=>(new Vector2(q.x,q.z)-new Vector2(b.center.x,b.center.z)).sqrMagnitude).FirstOrDefault();cen.y=b.center.y;var d=cen-b.center;if(d.magnitude>7||d.magnitude<.5f)continue;
     foreach(var h in Physics.RaycastAll(b.center,d.normalized,d.magnitude,~0,QueryTriggerInteraction.Ignore))if(packs.Contains(h.collider)){hidden.Add(HierarchyPath(r.transform));break;}
    }
    rep.Add("props behind packwalls: "+hidden.Count);rep.AddRange(hidden.Take(40).Select(s=>"  "+s));
    // lanterns + counts
    var lamps=Lamps306(mine,m.lamps);rep.Add("lanterns lit: "+string.Join(",",lamps.Where(kv=>kv.Value.GetComponentsInChildren<Light>(true).Any(l=>l.enabled)).Select(kv=>kv.Key))+" of "+lamps.Count);
    rep.Add("#306 renderers "+root.GetComponentsInChildren<Renderer>().Length+" + yard "+(mine.Find(Yard306)?.GetComponentsInChildren<Renderer>().Length??0)+", colliders "+root.GetComponentsInChildren<Collider>().Length+" + yard "+(mine.Find(Yard306)?.GetComponentsInChildren<Collider>().Length??0));
   }
   finally{Physics.queriesHitBackfaces=back;}
   return string.Join("\n",rep);
  }
 }
}
