using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.Data.World;
using Oheangbu.App.World;
using Object=UnityEngine.Object;
using Sheet293=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  // Site-specific highland sections (#293 variants). Geometry comes from
  // Tools/Art/highland_sections293.py (route, band, apron, designed terrain) → Tools/Blender/build_highland293.py (meshes).
  const string V293="../Art/World/Compact/Rebuild/Highlands293/Variants",S293=A292+"/Highlands293/Sections";
  static bool raw293;
  static readonly string[] RealmOrder293={"Cheongrim","Jeokro","Cheolong","Hyeongang","Hwanggyeong"};
  [Serializable] sealed class Stones293 {public string set;public Stone293[] stones;}
  [Serializable] sealed class Stone293 {public string kind;public int v,tone;public Vector3 p,size;public float yaw,pitch;}
  [Serializable] sealed class Meta293 {public string id,kind,realm,mountain;public int upSign;public float s0,s1,length;}

  static string[] SectionIds293(){var packet=JsonUtility.FromJson<HighlandPacket293>(File.ReadAllText(O293+"/modules.json"));return packet.Sections??Array.Empty<string>();}
  static Meta293 SectionMeta293(string id)=>JsonUtility.FromJson<Meta293>(File.ReadAllText(V293+"/"+id+"/meta.json"));
  static MountainTrailProfile SectionProfile293(string id)=>AssetDatabase.LoadAssetAtPath<MountainTrailProfile>(S293+"/"+id+"/Profile.asset");

  static Mesh Json293(string json,string asset)
  {
   var d=JsonUtility.FromJson<MeshAscent279>(File.ReadAllText(json));
   var mesh=new Mesh{name=Path.GetFileNameWithoutExtension(asset),indexFormat=IndexFormat.UInt32};
   mesh.vertices=d.vertices;mesh.normals=d.normals;mesh.uv=d.uv;mesh.triangles=d.triangles;mesh.RecalculateBounds();mesh.RecalculateTangents();
   string dir=Path.GetDirectoryName(asset).Replace('\\','/');if(!Directory.Exists(dir))DevSceneKit.EnsureFolder(dir);
   return ArtMesh(mesh,asset);
  }

  // Candidate copies of the accepted 285 surfaces; shared 285 materials/shaders are never edited.
  // CC0 Poly Haven sets (Highlands293/sources.json): rocky_trail for decomposed-granite paths, mossy_rock lichen on up-facing rock.
  static Texture2D Tex293(string slug,string kind)=>AssetDatabase.LoadAssetAtPath<Texture2D>(A292+"/Highlands293/Textures/"+slug+"_"+kind+"_2k.jpg");
  static void HighlandTextures293()
  {
   foreach(string file in Directory.GetFiles(A292+"/Highlands293/Textures","*.jpg"))
   {
    string path=file.Replace('\\','/');var ti=(TextureImporter)AssetImporter.GetAtPath(path);if(ti==null)continue;
    bool normal=path.Contains("_nor_gl_"),linear=normal||path.Contains("_arm_");var type=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
    if(ti.textureType==type&&ti.sRGBTexture==!linear&&ti.maxTextureSize==2048&&ti.anisoLevel==8)continue;
    ti.textureType=type;ti.sRGBTexture=!linear;ti.maxTextureSize=2048;ti.mipmapEnabled=true;ti.anisoLevel=8;ti.wrapMode=TextureWrapMode.Repeat;ti.SaveAndReimport();
   }
  }
  static Material SectionMaterial293(string file,string key,Color tint,Color colour,string slug=null,float scale=0,float chroma=.12f,float moss=0,Vector2? value=null)
  {
   var source=AssetDatabase.LoadAssetAtPath<Material>(A285+"/Materials/"+file);
   var m=Asset292("Highlands293/Sections/Materials/"+key+".mat",()=>new Material(source));
   m.shader=AssetDatabase.LoadAssetAtPath<Shader>(A292+"/Shaders/HighlandGranite293.shader");
   foreach(string t in new[]{"_BaseMap","_BumpMap","_MaskMap"})if(source.HasProperty(t))m.SetTexture(t,source.GetTexture(t));
   foreach(string f in new[]{"_WorldScale","_SurfaceLift"})if(source.HasProperty(f))m.SetFloat(f,source.GetFloat(f));
   if(slug!=null){m.SetTexture("_BaseMap",Tex293(slug,"diff"));m.SetTexture("_BumpMap",Tex293(slug,"nor_gl"));m.SetTexture("_MaskMap",Tex293(slug,"arm"));m.SetFloat("_WorldScale",scale);}
   m.SetFloat("_Chroma",chroma);m.SetVector("_NearRange",new Vector4(14,110,0,0));
   m.SetFloat("_MossAmount",moss);if(moss>0){m.SetTexture("_MossMap",Tex293("mossy_rock","diff"));m.SetTexture("_MossBump",Tex293("mossy_rock","nor_gl"));m.SetFloat("_MossScale",.42f);m.SetVector("_MossUp",new Vector4(.42f,.85f,0,0));}
   m.SetColor("_BaseColor",Color.Lerp(colour,tint,.2f));m.SetFloat("_AmbientFloor",.36f);
   m.SetColor("_FarTone",Color.Lerp(new Color(.76f,.74f,.68f),tint,.18f));m.SetVector("_FarRange",new Vector4(60,420,0,0));m.SetFloat("_FarStrength",.42f);
   // scan value normalisation (Step 2 realms; off = the Step 1 look): x target linear luminance, y local contrast
   var v=value??new Vector2(0,1);m.SetVector("_Value293",new Vector4(v.x,v.y,0,0));
   m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
  }
  static Material TimberMaterial293(string part,string key,Color tint,Color? colour=null)
  {
   var source=AssetDatabase.LoadAssetAtPath<Material>(A285+"/Materials/Pier289_"+part+".mat");
   var m=Asset292("Highlands293/Sections/Materials/"+key+".mat",()=>new Material(source));
   m.SetColor("_BaseColor",Color.Lerp(colour??new Color(.90f,.86f,.80f),tint,.15f));m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
  }
  static GameObject Lod293(string name,Transform parent,Mesh[] lods,Material material,float[] heights,bool collide)
  {
   var go=new GameObject(name);go.transform.SetParent(parent,false);var list=new List<LOD>();
   for(int i=0;i<lods.Length;i++){var child=MeshObject278(name+"_LOD"+i,lods[i],material,go.transform,false);list.Add(new LOD(heights[i],new[]{child.GetComponent<Renderer>()}));}
   var group=go.AddComponent<LODGroup>();group.SetLODs(list.ToArray());group.RecalculateBounds();
   if(collide)go.AddComponent<MeshCollider>().sharedMesh=lods[lods.Length-1];
   return go;
  }

  static List<string> Sections293(Transform root,CompactWorldLayoutSO layout)
  {
   var rows=new List<string>();var ids=SectionIds293();if(ids.Length==0)return rows;
   var boulder=AssetDatabase.LoadAssetAtPath<Mesh>(A285+"/Meshes/Scanned_boulder_LOD1.asset");
   var planks=Enumerable.Range(0,10).Select(i=>AssetDatabase.LoadAssetAtPath<Mesh>(A285+"/Meshes/Pier289_planks_"+i+".asset")).ToArray();
   var poles=Enumerable.Range(0,3).Select(i=>AssetDatabase.LoadAssetAtPath<Mesh>(A285+"/Meshes/Pier289_poles_"+i+".asset")).ToArray();
   HighlandTextures293();
   stoneSets293.Clear();outcrops293.Clear();  // stone sets and crest scans load per realm on first use
   Mesh[] MeshyLods(string name)=>Enumerable.Range(0,3).Select(l=>Json293(V293+"/Meshy/"+name+"_LOD"+l+".json",S293+"/Meshy/"+name+"_LOD"+l+".asset")).ToArray();
   var meshyDome=MeshyLods("CheongrimSheetingOutcrop");var meshyShelf=MeshyLods("PineViewpointRock");
   var groups=new Dictionary<string,Transform>();int index=0;
   foreach(string id in ids)
   {
    string folder=V293+"/"+id;var meta=SectionMeta293(id);
    var realm=layout.Realms.First(r=>r.Id.Equals(meta.realm,StringComparison.OrdinalIgnoreCase));
    if(!groups.TryGetValue(meta.mountain,out var group)){group=new GameObject(meta.mountain).transform;group.SetParent(root,false);groups[meta.mountain]=group;}
    var profile=Asset292("Highlands293/Sections/"+id+"/Profile.asset",()=>ScriptableObject.CreateInstance<MountainTrailProfile>());
    JsonUtility.FromJsonOverwrite(File.ReadAllText(folder+"/profile.json"),profile);EditorUtility.SetDirty(profile);
    var sec=new GameObject(id).transform;sec.SetParent(group,false);
    string A(string n)=>S293+"/"+id+"/"+n+".asset";
    var look=LookFor293(meta.realm);var mats=RealmMaterials293(meta.realm,realm.Tint,look);
    Material rock=mats.rock,soil=mats.soil,worn=mats.stone,repair=mats.repair,wallStone=mats.wall,propTimber=mats.timber;
    Lod293("Band",sec,Enumerable.Range(0,3).Select(l=>Json293(folder+"/Meshes/Band_LOD"+l+".json",A("Band_LOD"+l))).ToArray(),rock,new[]{.30f,.09f,.002f},true);
    Lod293("Apron",sec,Enumerable.Range(0,3).Select(l=>Json293(folder+"/Meshes/Apron_LOD"+l+".json",A("Apron_LOD"+l))).ToArray(),rock,new[]{.22f,.06f,.002f},true);
    foreach(string file in Directory.GetFiles(folder+"/Meshes","Trail_*.json").OrderBy(f=>f))
    {string n=Path.GetFileNameWithoutExtension(file);var go=MeshObject278(n,Json293(file,A(n)),soil,sec,false);go.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;}
    foreach(string file in Directory.GetFiles(folder+"/Meshes","Collision_*.json").OrderBy(f=>f))
    {string n=Path.GetFileNameWithoutExtension(file);var go=new GameObject("Trail_"+n);go.transform.SetParent(sec,false);go.AddComponent<MeshCollider>().sharedMesh=Json293(file,A(n));}
    // set step stones and paving in ~12m chunks (two LODs, no per-stone collision: the stepped collider carries walking)
    var stoneData=JsonUtility.FromJson<Stones293>(File.ReadAllText(folder+"/stones.json"));var stones=stoneData.stones;
    var stoneLod=StoneSet293(string.IsNullOrEmpty(stoneData.set)?"natural":stoneData.set);
    var chunks=new SortedDictionary<int,List<Stone293>>();  // key = 12m chunk * 2 + repair tone
    foreach(var st in stones)
    {
     int best=0;float bd=float.MaxValue;for(int i=0;i<profile.points.Length;i+=4){float dd=(profile.points[i]-st.p).sqrMagnitude;if(dd<bd){bd=dd;best=i;}}
     int c=(int)(profile.distances[best]/12)*2+(st.tone>0?1:0);if(!chunks.TryGetValue(c,out var list))chunks[c]=list=new List<Stone293>();list.Add(st);
    }
    foreach(var kv in chunks)
    {
     bool mended=kv.Key%2==1;string chunk=(mended?"Repair_":"Stones_")+(kv.Key/2);var lods=new Mesh[2];
     for(int l=0;l<2;l++)
     {
      var combine=kv.Value.Select(st=>new CombineInstance{mesh=stoneLod[st.v,l],transform=Matrix4x4.TRS(st.p,Quaternion.Euler(st.pitch,st.yaw,0),st.size)}).ToArray();
      var m=new Mesh{indexFormat=IndexFormat.UInt32};m.CombineMeshes(combine,true,true);m.RecalculateBounds();lods[l]=ArtMesh(m,A(chunk+"_LOD"+l));
     }
     Lod293(chunk,sec,lods,mended?repair:worn,new[]{.06f,.008f},false);
    }
    bool walkway=profile.bridgeEnd>profile.bridgeStart+.5f;
    if(walkway)
    {
     Walkway293(sec,profile,meta.upSign,planks,poles,TimberMaterial293("planks",meta.realm+"_Planks",realm.Tint,look.Timber),TimberMaterial293("poles",meta.realm+"_Poles",realm.Tint,look.Timber));
     if(look.Iron)IronStraps293(sec.Find("Timber_walkway"),IronMaterial293());  // iron-strapped walkway (Cheolong)
    }
    Physics.SyncTransforms();
    // valley-side ledge: partly buried scanned boulders (simple boxes), gaps left as plant pockets
    var rng=new System.Random(2930+index);float E(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());int ledge=0;
    for(float s=2.5f;s<profile.length-2.5f;s+=E(1.7f,3.9f))
    {
     if(walkway&&s>profile.bridgeStart-1&&s<profile.bridgeEnd+1)continue;
     var valley=-profile.Right(s)*meta.upSign;var p=profile.At(s)+valley*(profile.Width(s)*.5f+E(.58f,.9f));
     float ground=p.y-.4f;var hits=Physics.RaycastAll(p+Vector3.up*2,Vector3.down,10).Where(h=>h.collider.name=="Apron"||h.collider.transform.root.name=="Reworld292_Terrain").OrderBy(h=>h.distance).ToArray();
     if(hits.Length>0)ground=hits[0].point.y;
     float scale=E(.62f,1.05f);var go=MeshObject278("Edge_bedrock",boulder,rock,sec,false);ledge++;
     go.transform.position=new Vector3(p.x,Mathf.Min(p.y-E(.36f,.55f),ground-E(.08f,.25f)),p.z);
     go.transform.rotation=Quaternion.Euler(E(-14,14),E(0,360),E(-9,9));go.transform.localScale=new Vector3(scale*E(.9f,1.25f),scale*E(.8f,1.1f),scale*E(.9f,1.3f));
     var box=go.AddComponent<BoxCollider>();box.center=boulder.bounds.center;box.size=boulder.bounds.size*.88f;
     // keep the rotated box outside the walking corridor (inner face ≥ 5cm beyond the trail edge)
     float inner=float.MaxValue;var centre=profile.At(s);
     for(int c=0;c<8;c++){var corner=go.transform.TransformPoint(box.center+Vector3.Scale(box.size*.5f,new Vector3((c&1)==0?-1:1,(c&2)==0?-1:1,(c&4)==0?-1:1)));inner=Mathf.Min(inner,Vector3.Dot(corner-centre,valley));}
     float need=profile.Width(s)*.5f+.05f-inner;if(need>0)go.transform.position+=valley*need;
    }
    // crest masses: real scanned cliffs set into the ground above the band, breaking its smooth top
    int masses=0;
    for(float s=E(4,9);s<profile.length-4;s+=E(11,19))
    {
     var up=profile.Right(s)*meta.upSign;var at=profile.At(s);Vector3? crest=null;
     for(float u=3;u<18;u+=.75f){var from=at+up*u;var hit=Physics.RaycastAll(new Vector3(from.x,at.y+60,from.z),Vector3.down,90).Where(h=>h.collider.name=="Band").OrderBy(h=>h.distance).FirstOrDefault();if(hit.collider!=null&&(!crest.HasValue||hit.point.y>crest.Value.y))crest=hit.point;}
     if(!crest.HasValue||crest.Value.y<at.y+2.5f)continue;
     int pick=rng.Next(look.Outcrops.Length);var lods=Outcrop293(look.Outcrops[pick]);float height=lods[0].bounds.size.y;float target=E(3.5f,7f);float scale=target/Mathf.Max(.5f,height);
     var go=Lod293("Crest_mass",sec,lods,rock,new[]{.16f,.04f,.004f},true);masses++;
     go.transform.position=crest.Value+up*E(1.5f,4f)-Vector3.up*target*E(.35f,.5f);
     go.transform.rotation=Quaternion.LookRotation(-up,Vector3.up)*Quaternion.Euler(E(-6,6),E(-35,35),E(-5,5));go.transform.localScale=Vector3.one*scale*E(.9f,1.15f);
     if(look.Tors&&rng.NextDouble()<.45)  // Inwangsan tors: a second rounded block resting on the first
     {
      var top=Outcrop293(look.Outcrops[rng.Next(look.Outcrops.Length)]);float h2=target*E(.45f,.7f);
      var cap=Lod293("Crest_tor",sec,top,rock,new[]{.12f,.03f,.004f},true);cap.transform.rotation=Quaternion.Euler(E(-8,8),E(0,360),E(-8,8));
      cap.transform.localScale=Vector3.one*h2/Mathf.Max(.5f,top[0].bounds.size.y);cap.transform.position=go.transform.position+Vector3.up*(go.transform.localScale.y*height-h2*.22f)+up*E(-.6f,.6f);masses++;
     }
    }
    // Meshy supplementary masses (user-approved 2026-09-27, Highlands293/Meshy/ledger.json):
    // a granite dome over the band at B's bend, and a mostly buried shelf rock under C's pine viewpoint
    string meshyNote="";
    bool host=meta.kind=="B"||(meta.kind=="A"&&!ids.Any(x=>x!=id&&SectionMeta293(x).mountain==meta.mountain&&SectionMeta293(x).kind=="B"));
    if(meta.realm!="Cheongrim"&&look.Meshy!=null&&host)  // one realm Meshy mass per mountain (Cheolong has no B: on A)
    {
     float s=profile.length*.52f;var up=profile.Right(s)*meta.upSign;var at=profile.At(s);Vector3? crest=null;
     for(float u=3;u<18;u+=.75f){var from=at+up*u;var hit=Physics.RaycastAll(new Vector3(from.x,at.y+60,from.z),Vector3.down,90).Where(h=>h.collider.name=="Band").OrderBy(h=>h.distance).FirstOrDefault();if(hit.collider!=null&&(!crest.HasValue||hit.point.y>crest.Value.y))crest=hit.point;}
     if(crest.HasValue)
     {
      float height=look.MeshyHeight*E(.9f,1.1f);var lods=MeshyLods(look.Meshy);var go=Lod293("Meshy_"+look.Meshy,sec,lods,rock,new[]{.14f,.035f,.004f},true);
      // the buried flank stays clear of the corridor: the mass sits at least its own horizontal radius behind the trail edge
      var b=lods[0].bounds;float radius=new Vector2(b.extents.x,b.extents.z).magnitude*height;
      float crestU=Vector3.Dot(crest.Value-at,up);float centreU=Mathf.Max(crestU+E(1.5f,3f),profile.Width(s)*.5f+radius+1.2f);
      go.transform.position=new Vector3(at.x,crest.Value.y,at.z)+up*centreU-Vector3.up*height*.35f;go.transform.rotation=Quaternion.Euler(E(-4,4),E(0,360),E(-4,4));go.transform.localScale=Vector3.one*height;meshyNote="; meshy "+look.Meshy;
     }
    }
    if(meta.kind=="B"&&meta.realm=="Cheongrim")
    {
     float s=profile.length*.52f;var up=profile.Right(s)*meta.upSign;var at=profile.At(s);Vector3? crest=null;
     for(float u=3;u<18;u+=.75f){var from=at+up*u;var hit=Physics.RaycastAll(new Vector3(from.x,at.y+60,from.z),Vector3.down,90).Where(h=>h.collider.name=="Band").OrderBy(h=>h.distance).FirstOrDefault();if(hit.collider!=null&&(!crest.HasValue||hit.point.y>crest.Value.y))crest=hit.point;}
     if(crest.HasValue)
     {
      float height=E(7.5f,9f);var go=Lod293("Meshy_granite_dome",sec,meshyDome,rock,new[]{.14f,.035f,.004f},true);
      // keep the dome's buried flank clear of the walking corridor (horizontal radius ≈ 0.8 × height)
      float crestU=Vector3.Dot(crest.Value-at,up);float centreU=Mathf.Max(crestU+E(2.5f,4f),profile.Width(s)*.5f+.85f*.8f*height+1.2f);
      go.transform.position=new Vector3(at.x,crest.Value.y,at.z)+up*centreU-Vector3.up*height*.42f;go.transform.rotation=Quaternion.Euler(E(-4,4),E(0,360),E(-4,4));go.transform.localScale=Vector3.one*height;meshyNote="; meshy dome";
     }
    }
    if(meta.kind=="C")
    {
     float s=profile.length-3.2f;var valley=-profile.Right(s)*meta.upSign;var at=profile.At(s);float height=2.4f;
     var go=Lod293("Meshy_viewpoint_shelf",sec,meshyShelf,rock,new[]{.12f,.03f,.004f},true);
     go.transform.rotation=Quaternion.LookRotation(valley,Vector3.up);go.transform.localScale=Vector3.one*height;
     go.transform.position=at+valley*(profile.Width(s)*.5f+.9f)+Vector3.up*(.22f-height);
     Physics.SyncTransforms();var b=meshyShelf[0].bounds;float inner=float.MaxValue;
     for(int c=0;c<8;c++){var corner=go.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((c&1)==0?-1:1,(c&2)==0?-1:1,(c&4)==0?-1:1)));if(corner.y>at.y-.3f)inner=Mathf.Min(inner,Vector3.Dot(corner-at,valley));}
     float need=profile.Width(s)*.5f+.05f-inner;if(need>0&&inner<float.MaxValue)go.transform.position+=valley*need;meshyNote="; meshy viewpoint shelf";
    }
    meshyNote+=RealmProps293(sec,profile,meta,look,worn,wallStone,propTimber,rng);
    rows.Add(id+": band/apron 3 LOD + collision; crest masses="+masses+meshyNote+"; trail parts="+Directory.GetFiles(folder+"/Meshes","Trail_*.json").Length+"; stones="+stones.Length+" in "+chunks.Count+" chunks; ledge boulders="+ledge+"; walkway="+walkway);
    index++;
   }
   return rows;
  }

  static void Walkway293(Transform parent,MountainTrailProfile profile,int upSign,Mesh[] planks,Mesh[] poles,Material wood,Material pole)
  {
   // 289 timber walkway rebuilt on the section profile: variable CC0 planks, braces into the gully, valley-side rail.
   var bridge=new GameObject("Timber_walkway").transform;bridge.SetParent(parent,false);int poleIndex=0,plank=0;
   var rng=new System.Random(2933);float T(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
   Vector3 Up(float s)=>profile.Right(s)*upSign;
   void Log(string name,Vector3 a,Vector3 b,float radius,bool collide)
   {
    var go=MeshObject278(name,poles[poleIndex++%poles.Length],pole,bridge,false);go.transform.position=(a+b)*.5f;
    go.transform.rotation=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);go.transform.localScale=new Vector3(radius*2,(b-a).magnitude,radius*2);
    if(collide){var c=go.AddComponent<CapsuleCollider>();c.direction=1;c.radius=.45f;c.height=1;}
    var lod=go.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.006f,new[]{go.GetComponent<Renderer>()})});lod.RecalculateBounds();
   }
   // plank loop bounded by count and a finite remainder: float spans must never shrink below precision
   int guard=0;float edge=profile.bridgeStart;GameObject last=null;
   for(float s=profile.bridgeStart;profile.bridgeEnd-s>.03f&&guard++<400;)
   {
    float span=Mathf.Min(T(.16f,.29f),profile.bridgeEnd-s),mid=s+span*.5f;var p=profile.At(mid);
    var go=MeshObject278("Timber_tread",planks[plank++%planks.Length],wood,bridge,false);
    go.transform.position=p-Vector3.up*(.061f+T(-.008f,.008f))+Up(mid)*T(-.08f,.08f);
    go.transform.rotation=Quaternion.LookRotation(Vector3.Cross(profile.Right(mid),Vector3.up))*Quaternion.Euler(T(-1.8f,1.8f),T(-.7f,.7f),T(-.5f,.5f));
    go.transform.localScale=new Vector3(profile.Width(mid)+T(.16f,.43f),T(.10f,.14f),span+.04f);
    var box=go.AddComponent<BoxCollider>();box.center=Vector3.zero;box.size=new Vector3(.98f,.94f,1);s+=span;last=go;edge=s;
   }
   // the loop leaves ≤3cm before the far end: stretch the last tread over it (and 5cm onto the trail) so no ray falls between
   if(last!=null&&profile.bridgeEnd-edge>0)
   {
    float extra=profile.bridgeEnd-edge+.05f;var dir=(profile.At(profile.bridgeEnd)-profile.At(profile.bridgeEnd-.3f)).normalized;
    last.transform.localScale+=new Vector3(0,0,extra);last.transform.position+=dir*extra*.5f;
   }
   for(float s=profile.bridgeStart;s<=profile.bridgeEnd;s+=1.2f)
   {var p=profile.At(s);var up=Up(s);Log("Braced_support",p+up*1.25f-Vector3.up*2.8f,p-up*.88f-Vector3.up*.23f,.11f,false);Log("Cross_bearer",p+up*1.2f-Vector3.up*.23f,p-up*1.15f-Vector3.up*.23f,.10f,false);}
   foreach(float offset in new[]{-.75f,.75f})for(float s=profile.bridgeStart;s<profile.bridgeEnd;s+=1.0f)
   {float end=Mathf.Min(profile.bridgeEnd,s+1.08f);Log("Longitudinal_bearer",profile.At(s)+Up(s)*offset-Vector3.up*.15f,profile.At(end)+Up(end)*offset-Vector3.up*.15f,.08f,false);}
   Vector3? previous=null;
   float railEnd=profile.bridgeEnd+.4f;
   for(float s=profile.bridgeStart-.4f;guard++<800;s=Mathf.Min(railEnd,s+T(1.05f,1.65f)))
   {
    var p=profile.At(s)-Up(s)*(profile.Width(s)*.5f+T(.06f,.17f));
    var tip=p+Vector3.up*T(.87f,1.09f)-Up(s)*T(-.045f,.11f)+Vector3.forward*T(-.09f,.09f);
    Log("Rail_post",p-Vector3.up*T(.18f,.32f),tip+Vector3.up*T(.06f,.13f),T(.055f,.086f),true);
    if(previous.HasValue){var bend=Vector3.Lerp(previous.Value,tip,.52f)-Vector3.up*T(.025f,.07f);Log("Handrail",previous.Value,bend,T(.045f,.062f),true);Log("Handrail",bend,tip,T(.044f,.06f),true);}
    previous=tip;if(s>=railEnd-.001f)break;
   }
  }

  // Plants and loose rocks for the sections, added to the candidate sheet so the realm renderer draws them.
  static string DressSections293()
  {
   var session=Session292();var root=GameObject.Find("Highlands293");if(root==null)throw new Exception("Assemble highlands first");
   var art=session.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).First(a=>a.Sheet!=null&&a.Sheet.name.EndsWith("_MountainVegetation"));
   var sheet=art.Sheet;if(!AssetDatabase.GetAssetPath(sheet).StartsWith(A292+"/"))throw new Exception("Shared vegetation cannot be changed");
   var keep=sheet.FixedPlacements.Where(p=>!p.Id.StartsWith("293s_")).ToList();int added=0,index=0;var rows=new List<string>();
   Physics.SyncTransforms();
   foreach(string id in SectionIds293())
   {
    var profile=SectionProfile293(id);var meta=SectionMeta293(id);var sec=root.GetComponentsInChildren<Transform>(true).First(t=>t.name==id);
    string R=meta.realm;string P(string n)=>sheet.Prototypes.Any(q=>q.Id==R+n)?R+n:null;
    string pine=P("_SM_PinusDensiflora_Spring_2"),grass=P("_SM_Grass"),fern=P("_SM_Deparia_1"),broad=P("_SM_Deparia_3"),fill=P("_LowGroundFill"),rockK=P("_SM_Rock_K"),rockL=P("_SM_Rock_L");
    // realm flora (TEST): Jeokro dry shrubs and bare chinaberry, Cheolong sparse shrubs, Hyeongang reeds and willows, Hwanggyeong restrained
    float density=1,treeChance=1;string second=null,reed=null;
    switch(R)
    {
     case "Jeokro":fern=P("_SM_YMC_Bush_01_Small")??fern;broad=P("_SM_YMC_Grass_01_H")??broad;second=P("_SM_MeliaAzedarach_Winter_1");density=.55f;treeChance=.6f;break;
     case "Cheolong":fern=P("_SM_YMC_Bush_01_Small")??fern;broad=P("_SM_Grass_Haenggung")??broad;second=P("_SM_MeliaAzedarach_Winter_1");density=.6f;treeChance=.55f;break;
     case "Hyeongang":reed=P("_SM_PhragmitesAustralis_1");second=P("_SM_Salixpierotii_Summer_1");density=1.3f;break;
     case "Hwanggyeong":fern=P("_SM_Bush")??fern;broad=P("_SM_Grass")??broad;density=.5f;treeChance=.5f;break;
    }
    var rng=new System.Random(2940+index);float C(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());int before=added;
    void Add(string proto,Vector3 p,float scale,string tag){if(proto==null)return;keep.Add(new Sheet293.FixedPlacement{Id="293s_"+id+"_"+tag+"_"+added++,PrototypeId=proto,Position=p,Euler=new Vector3(0,C(0,360),0),Scale=scale,Preserve=true});}
    bool Mine(Collider c)=>c.transform.IsChildOf(sec)||c.transform.root.name=="Reworld292_Terrain";
    bool Ground(Vector3 from,float depth,out RaycastHit hit){var all=Physics.RaycastAll(from,Vector3.down,depth).Where(h=>Mine(h.collider)).OrderBy(h=>h.distance).ToArray();hit=all.FirstOrDefault();return all.Length>0;}
    bool OnWalkway(float s)=>profile.bridgeEnd>profile.bridgeStart&&s>profile.bridgeStart-1.2f&&s<profile.bridgeEnd+1.2f;
    // 1. habitat clusters: valley ledge pockets and the shaded foot of the band; bare rock intervals stay between
    for(float centre=3;centre<profile.length-3;centre+=C(4.5f,9.5f))
    {
     bool shade=rng.NextDouble()<.38;int count=(int)(rng.Next(7,17)*density);float spread=C(.65f,1.8f);
     for(int k=0;k<count;k++)
     {
      float s=Mathf.Clamp(centre+C(-spread,spread),1,profile.length-1);if(OnWalkway(s))continue;
      var dir=profile.Right(s)*meta.upSign;float w=profile.Width(s);
      var p=profile.At(s)+(shade?dir*(w*.5f-.09f):-dir*(w*.5f+C(.2f,1.5f)));
      if(!Ground(p+Vector3.up*2,6,out var hit)||hit.normal.y<.45f)continue;
      Add(shade?(k%3==0?broad:fern):(k%4==0?fill:k%7==0?broad:grass),hit.point-Vector3.up*.035f,C(.40f,.85f),"hab");
     }
    }
    // 2. pines rooted on band ledges and above the crest
    for(float s=4;s<profile.length-4;s+=C(3.5f,7f))
    {
     var dir=profile.Right(s)*meta.upSign;var at=profile.At(s);
     for(float u=2.5f;u<22;u+=C(2.5f,5.5f))
     {
      var p=at+dir*u;if(!Ground(new Vector3(p.x,at.y+40,p.z),60,out var hit))continue;
      if(hit.normal.y<.55f||hit.point.y<at.y+2.2f)continue;
      bool onRock=hit.collider.name=="Band";if(rng.NextDouble()>(onRock?.35:.22)*treeChance)continue;
      Add(second!=null&&!onRock&&rng.NextDouble()<.5?second:pine,hit.point-Vector3.up*.12f,onRock?C(.42f,.78f):C(.65f,1.05f),onRock?"ledgepine":"crestpine");
     }
    }
    // 3. ferns on the apron and a few loose rocks at the band foot
    for(int k=0;k<14;k++){float s=C(4,profile.length-4);if(OnWalkway(s))continue;var dir=profile.Right(s)*meta.upSign;var p=profile.At(s)-dir*(profile.Width(s)*.5f+C(2.2f,6f));if(Ground(p+Vector3.up*4,12,out var hit)&&hit.normal.y>.4f)Add(reed!=null&&k%2==0?reed:(k%3==0?broad:fern),hit.point-Vector3.up*.05f,C(.45f,.9f),"apron");}
    for(int k=0;k<10;k++){float s=C(3,profile.length-3);if(OnWalkway(s))continue;var dir=profile.Right(s)*meta.upSign;var p=profile.At(s)+dir*(profile.Width(s)*.5f+C(.25f,.9f));if(Ground(p+Vector3.up*3,6,out var hit))Add(k%2==0?rockK:rockL,hit.point-Vector3.up*.12f,C(.25f,.55f),"footrock");}
    // 4. type C: the pine viewpoint at the valley edge of the widened shelf
    if(meta.kind=="C"){float s=profile.length-3.2f;var dir=profile.Right(s)*meta.upSign;var p=profile.At(s)-dir*(profile.Width(s)*.5f+.35f);if(Ground(p+Vector3.up*3,8,out var hit)){Add(R=="Jeokro"&&second!=null?second:pine,hit.point-Vector3.up*.15f,1.05f,"viewpine");Add(rockK,hit.point-Vector3.up*.35f,.9f,"viewrock");}}
    // 5a. Jeokro: scree — broken rock spilled over the apron and the slope below it (the realm's weathered-block talus)
    if(R=="Jeokro")for(int k=0;k<42;k++){float s=C(3,profile.length-3);if(OnWalkway(s))continue;var dir=profile.Right(s)*meta.upSign;var p=profile.At(s)-dir*(profile.Width(s)*.5f+C(.9f,9f));if(Ground(p+Vector3.up*5,14,out var hit)&&hit.normal.y>.35f)Add(k%3==0?rockL:rockK,hit.point-Vector3.up*C(.05f,.16f),C(.22f,.75f),"scree");}
    // 5. Hyeongang: willows on the wet valley side below the apron
    if(R=="Hyeongang"&&second!=null)for(int k=0;k<6;k++){float s=C(4,profile.length-4);if(OnWalkway(s))continue;var dir=profile.Right(s)*meta.upSign;var p=profile.At(s)-dir*(profile.Width(s)*.5f+C(7f,14f));if(Ground(p+Vector3.up*8,24,out var hit)&&hit.normal.y>.6f)Add(second,hit.point-Vector3.up*.1f,C(.7f,1.05f),"willow");}
    rows.Add(id+" plants/rocks="+(added-before));index++;
   }
   sheet.FixedPlacements=keep.ToArray();art.Invalidate();EditorUtility.SetDirty(sheet);Save292();
   return string.Join("\n",rows)+"\nsection placements="+added+" (candidate sheet only)";
  }

  static string CheckSections293()
  {
   var root=GameObject.Find("Highlands293");if(root==null)throw new Exception("Assemble highlands first");
   var rows=new List<string>();void C(bool ok,string s)=>rows.Add((ok?"PASS ":"FAIL ")+s);Physics.SyncTransforms();
   foreach(string id in SectionIds293())
   {
    var profile=SectionProfile293(id);var meta=SectionMeta293(id);var sec=root.GetComponentsInChildren<Transform>(true).First(t=>t.name==id);
    int hits=0,total=0,blocked=0;float error=0,stairError=0;string worst="",stairWorst="";var blockers=new List<string>();var misses=new List<string>();
    for(int i=4;i<profile.points.Length-4;i+=2)
    {
     var point=profile.points[i];
     foreach(float offset in new[]{0f,-.6f,.6f})
     {
      total++;var at=point+profile.Right(profile.distances[i])*offset*Mathf.Min(1,profile.widths[i]/1.8f);
      var all=Physics.RaycastAll(at+Vector3.up*.8f,Vector3.down,1.7f).Where(x=>!(x.collider is CharacterController)).ToArray();
      if(all.Length>0)
      {
       hits++;var top=all.OrderByDescending(x=>x.point.y).First();float e=Mathf.Abs(top.point.y-point.y);
       string where="s="+profile.distances[i].ToString("F2")+" offset="+offset+" collider="+top.collider.name+" dy="+(top.point.y-point.y).ToString("F3");
       // skewed natural risers shift the next step along the path at the sides: judged against riser height separately
       if(profile.types[i]==1){if(e>stairError){stairError=e;stairWorst=where;}}else if(e>error){error=e;worst=where;}
      }
      else if(misses.Count<6)misses.Add("s="+profile.distances[i].ToString("F2")+" offset="+offset+" type="+profile.types[i]);
     }
     var obstacles=Physics.OverlapCapsule(point+Vector3.up*.45f,point+Vector3.up*1.5f,.26f).Where(x=>!(x is CharacterController)&&!x.name.StartsWith("Trail_Collision")&&x.name!="Timber_tread").ToArray();
     if(obstacles.Length>0){blocked++;if(blockers.Count<6)blockers.Add(i+":"+string.Join(",",obstacles.Select(x=>x.name)));}
    }
    C(hits==total,id+" centre/left/right ground rays="+hits+"/"+total+(misses.Count>0?" misses: "+string.Join("; ",misses):""));
    C(error<.24f,id+" maximum walking-surface discrepancy off the stairs="+error.ToString("F3")+"m ("+worst+")");
    C(stairError<.28f,id+" maximum discrepancy on skewed stairs="+stairError.ToString("F3")+"m ≤ riser 0.195+0.085 ("+stairWorst+")");
    C(blocked==0,id+" standing capsule obstructions="+blocked+(blockers.Count>0?" "+string.Join(" ",blockers):""));
    int seams=0,seamHits=0;bool walkway=profile.bridgeEnd>profile.bridgeStart+.5f;
    for(float s=2;s<profile.length-2;s+=.5f)
    {if(walkway&&s>=profile.bridgeStart-.5f&&s<=profile.bridgeEnd+.5f)continue;seams++;var at=profile.At(s)+profile.Right(s)*meta.upSign*(profile.Width(s)*.5f-.08f);if(Physics.RaycastAll(at+Vector3.up*.4f,Vector3.down,.8f).Any(h=>h.collider.name.StartsWith("Trail_Collision")))seamHits++;}
    C(seams==seamHits,id+" uphill trail edge supported="+seamHits+"/"+seams);
    var renderers=sec.GetComponentsInChildren<Renderer>(true);
    C(renderers.All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader!=null&&m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader))),id+" renderer materials valid and shader-error free ("+renderers.Length+" renderers)");
    C(sec.GetComponentsInChildren<MeshFilter>(true).All(f=>f.sharedMesh!=null&&f.sharedMesh.vertexCount>0),id+" mesh assets valid");
    if(walkway)C(sec.GetComponentsInChildren<Collider>().Count(c=>c.name=="Timber_tread"||c.name=="Handrail"||c.name=="Rail_post")>20,id+" walkway has tread/rail collision");
   }
   // candidate ground / screen atmosphere shaders (realm floors, canopy, realm fog)
   foreach(string path in new[]{A292+"/Materials/KoreanGround.mat",A292+"/Materials/Fog292.mat"})
   {var m=AssetDatabase.LoadAssetAtPath<Material>(path);C(m!=null&&m.shader!=null&&m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader),Path.GetFileName(path)+" shader "+(m!=null&&m.shader!=null?m.shader.name:"missing")+" compiles without errors");}
   File.WriteAllText(O293+"/sections-checks.txt",string.Join("\n",rows));return string.Join("\n",rows);
  }

  // Edit-mode movement with the actual player collider (Walk290 pattern): sections both ways at ±0.38m, then the whole main path.
  static string WalkSections293()
  {
   var session=Session292();var source=session.Walker.Body;
   var mountains=SectionIds293().Select(x=>SectionMeta293(x).mountain).Distinct().Select(id=>session.MountainLayout.Mountains.First(x=>x.Id==id)).ToArray();
   var go=new GameObject("Highlands293_actual_controller_fixture"){hideFlags=HideFlags.HideAndDontSave};go.SetActive(false);var cc=go.AddComponent<CharacterController>();
   cc.height=source.height;cc.center=source.center;cc.radius=source.radius;cc.skinWidth=source.skinWidth;cc.stepOffset=source.stepOffset;cc.slopeLimit=source.slopeLimit;
   var result=new List<string>{"Automated Edit-mode movement with the actual player collider (height="+cc.height+" radius="+cc.radius+" step="+cc.stepOffset+" slope="+cc.slopeLimit+"). No input/manual/performance approval."};
   var routes=new List<(string Name,Vector3[] Path)>();
   foreach(string id in SectionIds293())
   {
    var p=SectionProfile293(id);
    foreach(float offset in new[]{.38f,-.38f})
    {
     var path=Enumerable.Range(0,p.points.Length).Where(i=>i%3==0).Select(i=>p.points[i]+p.Right(p.distances[i])*offset*Mathf.Min(1,p.widths[i]/1.8f)).ToArray();
     routes.Add((id+" ascent "+offset.ToString("+0.00;-0.00"),path));routes.Add((id+" descent "+offset.ToString("+0.00;-0.00"),path.Reverse().ToArray()));
    }
   }
   foreach(var m in mountains)  // every sectioned mountain's whole main path (Jeokro/Cheolong are main-line routes)
   {string name=char.ToUpperInvariant(m.Id[9])+m.Id.Substring(10);routes.Add((name+" main path ascent",m.MainPath));routes.Add((name+" main path descent",m.MainPath.Reverse().ToArray()));}
   try
   {
    foreach(var route in routes)
    {
     cc.enabled=false;go.transform.position=route.Path[0]+Vector3.up*(cc.height*.5f-cc.center.y+.06f);go.SetActive(true);cc.enabled=true;Physics.SyncTransforms();
     int target=1,stalled=0,ticks=0;float nearest=float.PositiveInfinity;string failure=null;
     while(target<route.Path.Length&&ticks++<90000)
     {
      var feet=cc.transform.TransformPoint(cc.center)-Vector3.up*(cc.height*.5f);var delta=route.Path[target]-feet;float y=delta.y;delta.y=0;
      if(delta.magnitude<.18f&&Mathf.Abs(y)<1.2f){target++;nearest=float.PositiveInfinity;stalled=0;continue;}
      if(delta.magnitude<nearest-.015f){nearest=delta.magnitude;stalled=0;}else stalled++;
      // a fall is judged against the lower of the neighbouring route points (sparse original points can rise >3m apart)
      if(stalled>240||feet.y<Mathf.Min(route.Path[target-1].y,route.Path[target].y)-3)
      {
       // what stops the walker: overlapping colliders around the capsule and the first thing ahead at knee height
       var dir=new Vector3(delta.x,0,delta.z).normalized;var around=Physics.OverlapCapsule(feet+Vector3.up*(cc.radius+.05f),feet+Vector3.up*(cc.height-cc.radius),cc.radius+.12f).Where(c=>c!=cc).Select(c=>c.name+"@"+c.transform.parent?.name).Distinct();
       string ahead=Physics.Raycast(feet+Vector3.up*.35f,dir,out var h,1.2f)?h.collider.name+"@"+h.collider.transform.parent?.name+" dist="+h.distance.ToString("F2")+" normalY="+h.normal.y.ToString("F2"):"none";
       failure="at "+feet.ToString("F2")+" target="+target+"/"+route.Path.Length+" height error="+y.ToString("F2")+" around=["+string.Join(",",around)+"] ahead="+ahead;break;
      }
      var motion=delta.normalized*Mathf.Min(4.5f/60,delta.magnitude);motion.y=-.10f;cc.Move(motion);
     }
     result.Add(route.Name+": "+(failure==null&&target==route.Path.Length?"PASS":"FAIL "+failure)+"; simulated seconds="+(ticks/60f).ToString("F1"));
    }
   }
   finally{Object.DestroyImmediate(go);}
   string text=string.Join("\n",result);File.WriteAllText(O293+"/walk-fixture.txt",text);return text;
  }

  // Step-by-step trace of the actual player collider between two main-path points (diagnosis only).
  static string ProbeMain293(string arg)
  {
   var session=Session292();var source=session.Walker.Body;var m=session.MountainLayout.Mountains.First(x=>x.Id=="mountain_cheongrim");
   var ab=arg.Split(',').Select(int.Parse).ToArray();Vector3 a=m.MainPath[ab[0]],b=m.MainPath[ab[1]];
   var go=new GameObject("Highlands293_probe"){hideFlags=HideFlags.HideAndDontSave};go.SetActive(false);var cc=go.AddComponent<CharacterController>();
   cc.height=source.height;cc.center=source.center;cc.radius=source.radius;cc.skinWidth=source.skinWidth;cc.stepOffset=source.stepOffset;cc.slopeLimit=source.slopeLimit;
   var rows=new List<string>{"from "+a.ToString("F2")+" to "+b.ToString("F2")};
   try
   {
    go.transform.position=a+Vector3.up*(cc.height*.5f-cc.center.y+.06f);go.SetActive(true);Physics.SyncTransforms();
    for(int i=0;i<120;i++)
    {
     var feet=cc.transform.TransformPoint(cc.center)-Vector3.up*(cc.height*.5f);var d=b-feet;d.y=0;if(d.magnitude<.18f){rows.Add("reached at tick "+i);break;}
     var motion=d.normalized*Mathf.Min(4.5f/60,d.magnitude);motion.y=-.10f;var flags=cc.Move(motion);
     if(i%6==0){var down=Physics.RaycastAll(feet+Vector3.up*.5f,Vector3.down,2f).OrderBy(h=>h.distance).Select(h=>h.collider.name+":"+h.point.y.ToString("F2")+" n="+h.normal.y.ToString("F2"));
      rows.Add(i+" feet="+feet.ToString("F2")+" flags="+flags+" grounded="+cc.isGrounded+" under=["+string.Join(",",down)+"]");}
    }
   }
   finally{Object.DestroyImmediate(go);}
   return string.Join("\n",rows);
  }

  // arg "<section>:<view>[:raw|:clay]" — 0 up the trail, 1 down, 2 across from the valley, 3 high oblique, 4 stairs close-up, 5 whole mountain
  static string CaptureSections293(string arg)
  {
   var parts=arg.Split(':');int si=int.Parse(parts[0]),view=int.Parse(parts[1]);string mode=parts.Length>2?parts[2]:"";
   var ids=SectionIds293();string id=ids[si];var p=SectionProfile293(id);var meta=SectionMeta293(id);float L=p.length;
   Vector3 Up(float s)=>p.Right(s)*meta.upSign;Vector3 Fw(float s)=>(p.At(Mathf.Min(L,s+1))-p.At(Mathf.Max(0,s-1))).normalized;
   int firstFlight=Array.FindIndex(p.types,t=>t==1);float sf=firstFlight>=0?p.distances[firstFlight]+3:L*.4f;
   Vector3 eye,target;
   switch(view)
   {
    case 0:eye=p.At(3)+Vector3.up*1.65f;target=p.At(19)+Vector3.up*1.9f;break;
    case 1:eye=p.At(L-3)+Vector3.up*1.65f;target=p.At(L-19)+Vector3.up*.9f;break;
    case 2:{float s=L*.5f;eye=p.At(s)-Up(s)*34+Vector3.up*7-Fw(s)*6;target=p.At(s)+Up(s)*3+Vector3.up*4;break;}
    case 3:{float s=L*.5f;eye=p.At(s)-Up(s)*70-Fw(s)*55+Vector3.up*48;target=p.At(s)+Vector3.up*3;break;}
    case 4:eye=p.At(sf-3.2f)+Up(sf)*.25f+Vector3.up*1.35f;target=p.At(sf+3.5f)+Vector3.up*.45f;break;
    default:{var own=ids.Where(x=>SectionMeta293(x).mountain==meta.mountain).ToArray();var first=SectionProfile293(own[0]);var last=SectionProfile293(own[own.Length-1]);var mid=(first.At(0)+last.At(last.length))*.5f;var dir=(last.At(last.length)-first.At(0));dir.y=0;dir.Normalize();var side=new Vector3(dir.z,0,-dir.x)*-meta.upSign;eye=mid+side*260-dir*60+Vector3.up*120;target=mid+Vector3.up*10;break;}
   }
   var saved=new Dictionary<Renderer,Material[]>();Material neutral=null;
   if(mode=="clay")
   {
    neutral=new Material(AssetDatabase.LoadAssetAtPath<Shader>(A292+"/Shaders/HighlandGranite293.shader"));neutral.SetColor("_BaseColor",new Color(.62f,.62f,.62f));neutral.SetTexture("_BaseMap",Texture2D.whiteTexture);neutral.SetTexture("_MaskMap",Texture2D.whiteTexture);neutral.SetFloat("_FarStrength",0);
    var root=GameObject.Find("Highlands293");foreach(var r in root.GetComponentsInChildren<MeshRenderer>()){saved[r]=r.sharedMaterials;r.sharedMaterials=r.sharedMaterials.Select(_=>neutral).ToArray();}
   }
   string dir2=O293+"/Sections";Directory.CreateDirectory(dir2);
   eye293=eye;target293=target;output293=dir2+"/"+id+"-"+view+(mode!=""?"-"+mode:"")+".png";raw293=mode!="";
   try{Capture292(6);return output293;}
   finally{eye293=null;target293=null;output293=null;raw293=false;foreach(var kv in saved)kv.Key.sharedMaterials=kv.Value;if(neutral!=null)Object.DestroyImmediate(neutral);}
  }
 }
}
