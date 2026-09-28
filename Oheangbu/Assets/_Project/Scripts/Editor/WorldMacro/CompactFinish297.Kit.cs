using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEngine.SceneManagement;
using Oheangbu.EditorTools;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 kit: JSON meshes from Tools/Art/hanok297*.py -> candidate meshes/materials, compounds from <compound>/unity.json.
 // Every compound lives under one root (rebuilt from scratch), hidden #296 objects are recorded once and restorable.
 public static partial class CompactRebuildAuthoring
 {
  const string A297="Assets/_Project/Art/World/Finish297";
  const string K297="../Art/World/Compact/Rebuild/Finish297";
  [Serializable] class KitSub297{public string m;public int[] t;}
  [Serializable] class KitMesh297{public string name;public float[] v,n,uv;public KitSub297[] sub;}
  [Serializable] class KitEntry297{public string name,kind;public bool world;public float[] position;public float yaw;}
  [Serializable] class KitMarker297{public string id,kind,checkpoint;public float[] position;}
  [Serializable] class KitGate297{public string name,barred;public float[] position;public float yaw,width,height;public bool shortcut;}
  [Serializable] class KitRoute297{public string id;public float[] points;}
  [Serializable] class KitOp297{public string type;public float[] centre,half;public float yaw,top,blend;}
  [Serializable] class KitClip297{public string path;public float[] polygon;}
  [Serializable] class KitClipped297{public string[] filters,filterMeshes,colliders,colliderMeshes;}
  [Serializable] class KitCompound297{public string compound,root;public KitEntry297[] meshes;public string[] hide;public KitClip297[] clip;public KitMarker297[] markers;public KitGate297[] gates;public KitRoute297[] routes;public KitOp297[] terrainOps;public KitDoor297[] doors;public KitRest297 rest;}
  [Serializable] class KitHidden297{public string[] paths;public bool[] active;}

  static readonly string HW297="Assets/HwaseongHaenggung/Textures/",FG297="Assets/HwaseongForteressGate/Textures/",T297=A297+"/Textures/";
  // slot: base colour, normal map, tint, smoothness. UVs are already in texture repeats (metres / generator uvscale).
  static readonly Dictionary<string,(string bc,string n,Color tint,float smooth)> Slots297=new Dictionary<string,(string,string,Color,float)>{
   {"tile",(T297+"T_RoofTile297_BC.png",T297+"T_RoofTile297_N.png",Color.white,.34f)},
   {"tile_dark",(T297+"T_RoofTile297_BC.png",null,new Color(.55f,.55f,.58f),.30f)},
   {"ridge",(HW297+"T_KoreanWall_1_BC.png",HW297+"T_KoreanWall_1_N.png",new Color(.90f,.90f,.88f),.08f)},
   {"dancheong_beam",(FG297+"T_Crossbeam_BC.png",FG297+"T_Crossbeam_N.png",Color.white,.18f)},
   {"dancheong_bracket",(FG297+"T_ComplexBracket_BC.png",FG297+"T_ComplexBracket_N.png",Color.white,.18f)},
   {"wood_red",(HW297+"T_KoreanWood_2_BC.png",HW297+"T_KoreanWood_2_N.png",Color.white,.22f)},
   {"wood_dark",(HW297+"T_Koreanwood_1_BC.png",HW297+"T_Koreanwood_1_N.png",Color.white,.16f)},
   {"wood_board",(HW297+"T_Koreanwood_1_BC.png",HW297+"T_Koreanwood_1_N.png",new Color(.85f,.80f,.75f),.12f)},
   {"fascia",(HW297+"T_KoreanWood_2_BC.png",null,Color.white,.2f)},
   {"gable",(FG297+"T_B_RoofBoard_001_BC.png",FG297+"T_B_RoofBoard_N.png",Color.white,.15f)},
   {"plaster",(HW297+"T_KoreanWall_1_BC.png",HW297+"T_KoreanWall_1_N.png",Color.white,.06f)},
   {"stone_dressed",(FG297+"T_Fortification_001_BC.png",FG297+"T_Fortification_001_N.png",Color.white,.10f)},
   {"stone_rough",(FG297+"T_CW_001_BC.png",FG297+"T_CW_001_N.png",Color.white,.08f)},
   {"stone_plain",(HW297+"T_KoreanStone_1_BC.png",HW297+"T_KoreanStone_1_N.png",Color.white,.10f)},
   {"lattice",(T297+"T_DoorTtisal297_BC.png",T297+"T_DoorTtisal297_N.png",Color.white,.10f)},
   {"lattice_red",(T297+"T_DoorTtisalRed297_BC.png",T297+"T_DoorTtisal297_N.png",Color.white,.12f)},
   {"window_grid",(T297+"T_WindowGrid297_BC.png",T297+"T_WindowGrid297_N.png",Color.white,.10f)},
   {"soffit",(T297+"T_Soffit297_BC.png",T297+"T_Soffit297_N.png",Color.white,.12f)},
   {"floor_wood",(HW297+"T_Koreanwood_1_BC.png",null,new Color(.9f,.85f,.8f),.18f)},
   {"floor_brick",(HW297+"T_KoreanTile_1_BC.png",HW297+"T_KoreanTile_1_N.png",Color.white,.12f)},
   {"ceiling",(HW297+"T_Ceiling_BC.png",null,Color.white,.12f)},
   {"brick_dark",(FG297+"T_B_CastleWall_001_BC.png",FG297+"T_B_CastleWall_001_N.png",Color.white,.10f)},
   {"paving",(HW297+"T_KoreanTile_3_BC.png",HW297+"T_KoreanTile_3_N.png",Color.white,.14f)},
   {"iron",(null,null,new Color(.15f,.14f,.13f),.32f)},
   {"fieldstone",(HW297+"T_KoreanStone_1_BC.png",HW297+"T_KoreanStone_1_N.png",new Color(.66f,.66f,.66f),.06f)},
   {"candle",(null,null,new Color(.84f,.80f,.70f),.25f)},
   {"flame",(null,null,new Color(1f,.80f,.52f),0f)},
  };

  static float[] F297(float[] a)=>a??Array.Empty<float>();
  static Vector3 V297(float[] a,int i=0)=>new Vector3(a[i],a[i+1],a[i+2]);

  // copy the staged procedural textures into the candidate folder with the right importer settings (idempotent)
  static string KitTextures297()
  {
   DevSceneKit.EnsureFolder(A297+"/Textures");int n=0;
   foreach(string f in Directory.GetFiles(K297+"/Stage/Textures","*.png"))
   {
    string dst=T297+Path.GetFileName(f);bool changed=!File.Exists(dst)||!File.ReadAllBytes(dst).SequenceEqual(File.ReadAllBytes(f));
    if(changed){File.Copy(f,dst,true);AssetDatabase.ImportAsset(dst,ImportAssetOptions.ForceUpdate);}
    var ti=(TextureImporter)AssetImporter.GetAtPath(dst);bool normal=dst.EndsWith("_N.png");
    var type=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
    if(ti.textureType!=type||ti.sRGBTexture==normal||ti.anisoLevel!=4||ti.maxTextureSize!=2048)
    {ti.textureType=type;ti.sRGBTexture=!normal;ti.anisoLevel=4;ti.maxTextureSize=2048;ti.wrapMode=TextureWrapMode.Repeat;ti.mipmapEnabled=true;ti.SaveAndReimport();}
    n++;
   }
   return "kit textures="+n;
  }

  static Material cliffRock297;
  static Material KitMaterial297(string slot)
  {
   // cliff faces reuse the candidate's own Cheongrim granite (same as the cave portal's cliff_rock slot)
   if(slot=="cliff_rock")
   {
    if(cliffRock297==null)cliffRock297=AssetDatabase.FindAssets("Cheongrim_Granite t:Material").Select(AssetDatabase.GUIDToAssetPath).Where(q=>q.Contains("Architecture296/Materials")).Select(AssetDatabase.LoadAssetAtPath<Material>).FirstOrDefault(m=>m!=null);
    if(cliffRock297!=null)return cliffRock297;
   }
   string path=A297+"/Materials/"+slot+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(!Slots297.TryGetValue(slot,out var s))s=(null,null,new Color(.5f,.5f,.5f),.1f);
   if(m==null){DevSceneKit.EnsureFolder(A297+"/Materials");m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=slot};AssetDatabase.CreateAsset(m,path);}
   var bc=s.bc!=null?AssetDatabase.LoadAssetAtPath<Texture2D>(s.bc):null;var nm=s.n!=null?AssetDatabase.LoadAssetAtPath<Texture2D>(s.n):null;
   m.SetTexture("_BaseMap",bc);m.SetColor("_BaseColor",s.tint);m.SetFloat("_Smoothness",s.smooth);m.SetFloat("_Metallic",0);
   if(nm!=null){m.SetTexture("_BumpMap",nm);m.SetFloat("_BumpScale",1);m.EnableKeyword("_NORMALMAP");}else{m.SetTexture("_BumpMap",null);m.DisableKeyword("_NORMALMAP");}
   m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
  }

  static (Mesh mesh,string[] slots) LoadKitMesh297(string json,string asset)
  {
   var d=JsonUtility.FromJson<KitMesh297>(File.ReadAllText(json));int count=F297(d.v).Length/3;
   var v=new Vector3[count];var n=new Vector3[count];var uv=new Vector2[count];
   for(int i=0;i<count;i++){v[i]=new Vector3(d.v[3*i],d.v[3*i+1],d.v[3*i+2]);n[i]=d.n!=null&&d.n.Length>=3*count?new Vector3(d.n[3*i],d.n[3*i+1],d.n[3*i+2]):Vector3.up;uv[i]=d.uv!=null&&d.uv.Length>=2*count?new Vector2(d.uv[2*i],d.uv[2*i+1]):Vector2.zero;}
   var subs=(d.sub??Array.Empty<KitSub297>()).Where(s=>s.t!=null&&s.t.Length>0).ToArray();
   var mesh=new Mesh{name=Path.GetFileNameWithoutExtension(asset),indexFormat=count>65000?IndexFormat.UInt32:IndexFormat.UInt16};
   mesh.vertices=v;mesh.normals=n;mesh.uv=uv;mesh.subMeshCount=Mathf.Max(1,subs.Length);
   for(int i=0;i<subs.Length;i++)mesh.SetTriangles(subs[i].t,i,false);
   mesh.RecalculateBounds();if(subs.Length>0)mesh.RecalculateTangents();
   DevSceneKit.EnsureFolder(Path.GetDirectoryName(asset).Replace('\\','/'));
   return (ArtMesh(mesh,asset),subs.Select(s=>s.m).ToArray());
  }

  static KitCompound297 CompoundData297(string name)=>JsonUtility.FromJson<KitCompound297>(File.ReadAllText(K297+"/"+name+"/unity.json"));

  static string Compound297(string name)
  {
   RequireClean292();var data=CompoundData297(name);string src=K297+"/"+name+"/Meshes/";string meshes=A297+"/"+name+"/Meshes/";
   var report=new List<string>{KitTextures297()};
   foreach(var slot in Slots297.Keys)KitMaterial297(slot);
   // hide the #296 objects this compound replaces (states recorded once, restorable)
   string hiddenFile=K297+"/"+name+"/hidden.json";var hidden=File.Exists(hiddenFile)?JsonUtility.FromJson<KitHidden297>(File.ReadAllText(hiddenFile)):null;
   var paths=data.hide??Array.Empty<string>();var found=paths.Select(p=>GameObject.Find(p)).ToArray();
   if(hidden==null){hidden=new KitHidden297{paths=paths,active=found.Select(g=>g!=null&&g.activeSelf).ToArray()};File.WriteAllText(hiddenFile,JsonUtility.ToJson(hidden,true));}
   foreach(var g in found)if(g!=null)g.SetActive(false);
   report.Add("hidden="+found.Count(g=>g!=null)+"/"+paths.Length);
   if(data.clip!=null&&data.clip.Length>0)report.Add(ClipCompound297(name,data.clip));
   var old=GameObject.Find(data.root);if(old!=null)Object.DestroyImmediate(old);
   var root=new GameObject(data.root);long tris=0;int objects=0;
   foreach(var e in data.meshes)
   {
    var go=new GameObject(e.name);go.transform.SetParent(root.transform,false);
    if(!e.world&&e.position!=null&&e.position.Length>=3){go.transform.position=V297(e.position);go.transform.rotation=Quaternion.Euler(0,e.yaw,0);}
    var lods=new List<LOD>();float[] cut={.16f,.045f,.007f};
    for(int lod=0;lod<3;lod++)
    {
     string json=src+e.name+"_LOD"+lod+".json";if(!File.Exists(json))continue;
     var (mesh,slots)=LoadKitMesh297(json,meshes+e.name+"_LOD"+lod+".asset");
     var part=new GameObject("LOD"+lod);part.transform.SetParent(go.transform,false);
     part.AddComponent<MeshFilter>().sharedMesh=mesh;var r=part.AddComponent<MeshRenderer>();
     r.sharedMaterials=slots.Select(KitMaterial297).ToArray();r.shadowCastingMode=lod<2?ShadowCastingMode.On:ShadowCastingMode.Off;
     if(lod==0)for(int s=0;s<mesh.subMeshCount;s++)tris+=mesh.GetIndexCount(s)/3;
     lods.Add(new LOD(cut[lod],new Renderer[]{r}));
    }
    if(lods.Count>0){var group=go.AddComponent<LODGroup>();group.SetLODs(lods.ToArray());group.RecalculateBounds();}
    string cj=src+e.name+"_Collision.json";
    if(File.Exists(cj))
    {
     var (cm,_)=LoadKitMesh297(cj,meshes+e.name+"_Collision.asset");var c=new GameObject("Collision");c.transform.SetParent(go.transform,false);
     c.AddComponent<MeshCollider>().sharedMesh=cm;
    }
    GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
    foreach(Transform t in go.transform)GameObjectUtility.SetStaticEditorFlags(t.gameObject,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
    objects++;
   }
   report.Add(CompoundInteractions297(name,data,root));
   var markers=new GameObject("Markers").transform;markers.SetParent(root.transform,false);
   foreach(var m in data.markers??Array.Empty<KitMarker297>()){var t=new GameObject(m.kind+"_"+m.id).transform;t.SetParent(markers,false);t.position=V297(m.position);}
   Physics.SyncTransforms();Save292();
   report.Add("objects="+objects+" LOD0 tris="+tris+" markers="+(data.markers?.Length??0));
   string text=string.Join("\n",report);File.WriteAllText(K297+"/"+name+"/build.txt",text);return text;
  }

  static string CompoundRemove297(string name)
  {
   RequireClean292();var data=CompoundData297(name);var root=GameObject.Find(data.root);if(root!=null)Object.DestroyImmediate(root);
   string contentReport=CompoundContentRemove297(name);
   string hiddenFile=K297+"/"+name+"/hidden.json";int restored=0;
   if(File.Exists(hiddenFile))
   {
    var hidden=JsonUtility.FromJson<KitHidden297>(File.ReadAllText(hiddenFile));
    var all=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
    for(int i=0;i<hidden.paths.Length;i++){var t=all.FirstOrDefault(x=>HierarchyPath(x)==hidden.paths[i]);if(t!=null){t.gameObject.SetActive(hidden.active[i]);restored++;}}
   }
   string clipFile=K297+"/"+name+"/clipped.json";
   if(File.Exists(clipFile))
   {
    var c=JsonUtility.FromJson<KitClipped297>(File.ReadAllText(clipFile));var all=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
    for(int i=0;i<c.filters.Length;i++){var t=all.FirstOrDefault(x=>HierarchyPath(x)==c.filters[i]);if(t!=null){t.GetComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(c.filterMeshes[i]);restored++;}}
    for(int i=0;i<c.colliders.Length;i++){var t=all.FirstOrDefault(x=>HierarchyPath(x)==c.colliders[i]);var mc=t!=null?t.GetComponent<MeshCollider>():null;if(mc!=null){mc.sharedMesh=null;mc.sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(c.colliderMeshes[i]);}}
   }
   Save292();return "removed "+data.root+"; restored "+restored+"; "+contentReport;
  }
  // clip: triangles of existing (#296) meshes whose centroid lies inside the compound polygon are dropped from private copies
  // (renderers and mesh colliders); the source mesh assets are never edited. Originals recorded once in clipped.json.
  static string ClipCompound297(string name,KitClip297[] clips)
  {
   string file=K297+"/"+name+"/clipped.json";var all=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
   var rec=File.Exists(file)?JsonUtility.FromJson<KitClipped297>(File.ReadAllText(file)):new KitClipped297{filters=new string[0],filterMeshes=new string[0],colliders=new string[0],colliderMeshes=new string[0]};
   var ff=rec.filters.ToList();var fm=rec.filterMeshes.ToList();var cf=rec.colliders.ToList();var cm=rec.colliderMeshes.ToList();int removed=0,objects=0;
   DevSceneKit.EnsureFolder(A297+"/"+name+"/Clipped");
   foreach(var clip in clips)
   {
    var root=all.FirstOrDefault(x=>HierarchyPath(x)==clip.path);if(root==null)continue;
    var poly=Enumerable.Range(0,clip.polygon.Length/2).Select(i=>new Vector2(clip.polygon[2*i],clip.polygon[2*i+1])).ToArray();
    bool Inside(Vector3 w){bool c=false;for(int i=0,j=poly.Length-1;i<poly.Length;j=i++){var a=poly[i];var b=poly[j];if(((a.y>w.z)!=(b.y>w.z))&&(w.x<(b.x-a.x)*(w.z-a.y)/(b.y-a.y)+a.x))c=!c;}return c;}
    Mesh Cut(Mesh src,Transform t,string asset)
    {
     var m=Object.Instantiate(src);var v=src.vertices;
     for(int s=0;s<m.subMeshCount;s++){var tri=m.GetTriangles(s);var keep=new List<int>(tri.Length);for(int i=0;i<tri.Length;i+=3){if(Inside(t.TransformPoint((v[tri[i]]+v[tri[i+1]]+v[tri[i+2]])/3))){removed++;continue;}keep.Add(tri[i]);keep.Add(tri[i+1]);keep.Add(tri[i+2]);}m.SetTriangles(keep,s,false);}
     m.RecalculateBounds();return ArtMesh(m,asset);
    }
    foreach(var mf in root.GetComponentsInChildren<MeshFilter>(true))
    {
     string path=HierarchyPath(mf.transform);int k=ff.IndexOf(path);if(k<0){ff.Add(path);fm.Add(AssetDatabase.GetAssetPath(mf.sharedMesh));k=ff.Count-1;}
     mf.sharedMesh=Cut(AssetDatabase.LoadAssetAtPath<Mesh>(fm[k]),mf.transform,A297+"/"+name+"/Clipped/"+mf.name+"_"+k+".asset");objects++;
    }
    foreach(var mc in root.GetComponentsInChildren<MeshCollider>(true))
    {
     string path=HierarchyPath(mc.transform);int k=cf.IndexOf(path);if(k<0){cf.Add(path);cm.Add(AssetDatabase.GetAssetPath(mc.sharedMesh));k=cf.Count-1;}
     var cut=Cut(AssetDatabase.LoadAssetAtPath<Mesh>(cm[k]),mc.transform,A297+"/"+name+"/Clipped/"+mc.name+"_collider_"+k+".asset");mc.sharedMesh=null;mc.sharedMesh=cut;objects++;
    }
   }
   File.WriteAllText(file,JsonUtility.ToJson(new KitClipped297{filters=ff.ToArray(),filterMeshes=fm.ToArray(),colliders=cf.ToArray(),colliderMeshes=cm.ToArray()},true));
   return "clipped "+objects+" meshes, "+removed+" triangles inside the compound";
  }

  // Automated movement with the actual player collider along every compound route, both directions.
  // walk-cave — the actual player collider walks the new-game route: content MainPath from the start (relocated mine) through
  // the #297 portal gallery to the Geumpyo inn and back. Points are densified to 1.2 m and dropped onto physical support first.
  static string WalkCave297()
  {
   var session=Session292();var content=session.Content;var main=content.MainPath;
   var rel=JsonUtility.FromJson<CaveRelocation297>(File.ReadAllText(K297+"/Cave/relocation.json"));var innEnd=V297(rel.outsidePath,rel.outsidePath.Length-3);
   int end=JoinIndex297(main,innEnd,120);var route=main.Take(end+1).ToArray();
   var dense=new List<Vector3>();for(int i=1;i<route.Length;i++){int k=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(route[i-1],route[i])/1.2f));for(int j=0;j<k;j++)dense.Add(Vector3.Lerp(route[i-1],route[i],j/(float)k));}dense.Add(route.Last());
   var result=new List<string>{"walk-cave: content MainPath[0.."+end+"] start->Geumpyo inn, "+dense.Count+" points; automated Edit-mode CharacterController (actual player collider). Not manual play."};
   // encounter actors (creatures) are gameplay, not level geometry: their colliders sit out the walk
   var actorColliders=session.Actors.Where(a=>a!=null).SelectMany(a=>a.GetComponentsInChildren<Collider>(true)).Concat(session.Walker.Body.GetComponentsInChildren<Collider>(true)).Where(c=>c.enabled).Distinct().ToArray();
   foreach(var c in actorColliders)c.enabled=false;Physics.SyncTransforms();
   try
   {
    int missing=0;var supported=new Vector3[dense.Count];
    for(int i=0;i<dense.Count;i++)
    {
     var hits=Physics.RaycastAll(dense[i]+Vector3.up*2.5f,Vector3.down,7,~0,QueryTriggerInteraction.Ignore).Where(h=>h.normal.y>.35f&&h.collider.transform.root.name!="Macro_CombatPlayerRig").OrderByDescending(h=>h.point.y).ToArray();
     if(hits.Length==0){missing++;supported[i]=dense[i];continue;}supported[i]=hits[0].point;
    }
    if(missing>0)result.Add("WARN "+missing+" route points without physical support within 2.5 m above / 4.5 m below");
    result.Add("encounter actor + resting player body colliders disabled during the walk: "+actorColliders.Length);
    result.AddRange(WalkPaths297(new[]{("start->inn",supported),("inn->start",supported.Reverse().ToArray())}));
   }
   finally{foreach(var c in actorColliders)c.enabled=true;Physics.SyncTransforms();}
   string text=string.Join("\n",result);File.WriteAllText(K297+"/Cave/walk.txt",text);return text;
  }
  static List<string> WalkPaths297(IEnumerable<(string,Vector3[])> routes)
  {
   var source=Session292().Walker.Body;var output=new List<string>();
   var go=new GameObject("Finish297_controller_fixture"){hideFlags=HideFlags.HideAndDontSave};go.SetActive(false);var cc=go.AddComponent<CharacterController>();
   cc.height=source.height;cc.center=source.center;cc.radius=source.radius;cc.skinWidth=source.skinWidth;cc.stepOffset=source.stepOffset;cc.slopeLimit=source.slopeLimit;
   output.Add("collider height="+cc.height+" radius="+cc.radius+" step="+cc.stepOffset+" slope="+cc.slopeLimit);
   try
   {
    foreach(var (rid,path) in routes)
    {
     cc.enabled=false;go.transform.position=path[0]+Vector3.up*(cc.height*.5f-cc.center.y+.3f);go.SetActive(true);cc.enabled=true;Physics.SyncTransforms();
     int target=1,stalled=0,ticks=0;float nearest=float.PositiveInfinity;string failure=null;
     while(target<path.Length&&ticks++<200000)
     {
      var feet=cc.transform.TransformPoint(cc.center)-Vector3.up*(cc.height*.5f);var delta=path[target]-feet;float y=delta.y;delta.y=0;
      if(delta.magnitude<.25f&&Mathf.Abs(y)<1.6f){target++;nearest=float.PositiveInfinity;stalled=0;continue;}
      if(delta.magnitude<nearest-.015f){nearest=delta.magnitude;stalled=0;}else stalled++;
      if(stalled>300||feet.y<Mathf.Min(path[target-1].y,path[target].y)-4)
      {
       var dir=new Vector3(delta.x,0,delta.z).normalized;
       var around=Physics.OverlapCapsule(feet+Vector3.up*(cc.radius+.05f),feet+Vector3.up*(cc.height-cc.radius),cc.radius+.12f).Where(c=>c!=cc).Select(c=>c.name+"@"+c.transform.parent?.name).Distinct();
       string ahead=Physics.Raycast(feet+Vector3.up*.35f,dir,out var h,1.2f)?h.collider.name+"@"+h.collider.transform.parent?.name+" d="+h.distance.ToString("F2")+" nY="+h.normal.y.ToString("F2"):"none";
       failure="at "+feet.ToString("F2")+" target="+target+"/"+path.Length+" dy="+y.ToString("F2")+" around=["+string.Join(",",around)+"] ahead="+ahead;break;
      }
      var motion=delta.normalized*Mathf.Min(4.5f/60,delta.magnitude);motion.y=-.12f;cc.Move(motion);
     }
     output.Add(rid+": "+(failure==null&&target==path.Length?"PASS":"FAIL "+failure)+"; simulated seconds="+(ticks/60f).ToString("F1"));
    }
   }
   finally{Object.DestroyImmediate(go);}
   return output;
  }
  static string WalkCompound297(string name)
  {
   var data=CompoundData297(name);var source=Session292().Walker.Body;
   var go=new GameObject("Finish297_controller_fixture"){hideFlags=HideFlags.HideAndDontSave};go.SetActive(false);var cc=go.AddComponent<CharacterController>();
   cc.height=source.height;cc.center=source.center;cc.radius=source.radius;cc.skinWidth=source.skinWidth;cc.stepOffset=source.stepOffset;cc.slopeLimit=source.slopeLimit;
   var result=new List<string>{"Automated Edit-mode movement with the actual player collider (height="+cc.height+" radius="+cc.radius+" step="+cc.stepOffset+" slope="+cc.slopeLimit+"). Not manual play."};
   var routes=new List<(string,Vector3[])>();
   foreach(var r in data.routes??Array.Empty<KitRoute297>())
   {
    var p=Enumerable.Range(0,r.points.Length/3).Select(i=>V297(r.points,3*i)).ToArray();if(p.Length<2)continue;
    // densify to 1 m and drop every point onto the highest walkable collider within 1.2 m above / 3 m below it
    var dense=new List<Vector3>();for(int i=1;i<p.Length;i++){int k=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(p[i-1],p[i])));for(int j=0;j<k;j++)dense.Add(Vector3.Lerp(p[i-1],p[i],j/(float)k));}dense.Add(p.Last());
    for(int i=0;i<dense.Count;i++){var hits=Physics.RaycastAll(dense[i]+Vector3.up*1.2f,Vector3.down,4.2f,~0,QueryTriggerInteraction.Ignore).Where(h=>h.normal.y>.5f&&!(h.collider is CharacterController)).OrderByDescending(h=>h.point.y).ToArray();if(hits.Length>0)dense[i]=hits[0].point;}
    routes.Add((r.id+" forward",dense.ToArray()));routes.Add((r.id+" reverse",Enumerable.Reverse(dense).ToArray()));
   }
   try
   {
    foreach(var (rid,path) in routes)
    {
     cc.enabled=false;go.transform.position=path[0]+Vector3.up*(cc.height*.5f-cc.center.y+.3f);go.SetActive(true);cc.enabled=true;Physics.SyncTransforms();
     int target=1,stalled=0,ticks=0;float nearest=float.PositiveInfinity;string failure=null;
     while(target<path.Length&&ticks++<120000)
     {
      var feet=cc.transform.TransformPoint(cc.center)-Vector3.up*(cc.height*.5f);var delta=path[target]-feet;float y=delta.y;delta.y=0;
      if(delta.magnitude<.25f&&Mathf.Abs(y)<1.6f){target++;nearest=float.PositiveInfinity;stalled=0;continue;}
      if(delta.magnitude<nearest-.015f){nearest=delta.magnitude;stalled=0;}else stalled++;
      if(stalled>300||feet.y<Mathf.Min(path[target-1].y,path[target].y)-4)
      {
       var dir=new Vector3(delta.x,0,delta.z).normalized;
       var around=Physics.OverlapCapsule(feet+Vector3.up*(cc.radius+.05f),feet+Vector3.up*(cc.height-cc.radius),cc.radius+.12f).Where(c=>c!=cc).Select(c=>c.name+"@"+c.transform.parent?.name).Distinct();
       string ahead=Physics.Raycast(feet+Vector3.up*.35f,dir,out var h,1.2f)?h.collider.name+"@"+h.collider.transform.parent?.name+" d="+h.distance.ToString("F2")+" nY="+h.normal.y.ToString("F2"):"none";
       failure="at "+feet.ToString("F2")+" target="+target+"/"+path.Length+" dy="+y.ToString("F2")+" around=["+string.Join(",",around)+"] ahead="+ahead;break;
      }
      var motion=delta.normalized*Mathf.Min(4.5f/60,delta.magnitude);motion.y=-.12f;cc.Move(motion);
     }
     result.Add(rid+": "+(failure==null&&target==path.Length?"PASS":"FAIL "+failure)+"; simulated seconds="+(ticks/60f).ToString("F1"));
    }
    result.AddRange(DoorClosedChecks297(data,cc,go));
   }
   finally{Object.DestroyImmediate(go);}
   string text=string.Join("\n",result);File.WriteAllText(K297+"/"+name+"/walk.txt",text);return text;
  }
 }
}
