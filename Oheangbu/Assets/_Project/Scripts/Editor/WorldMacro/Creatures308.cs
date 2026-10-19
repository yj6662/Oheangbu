using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // bridges to Content308's private scene helpers (same wiring as Content308.Populate clones)
 public static partial class Content308
 {
  internal static IEnumerable<T> CrSaved308<T>(Scene scene)where T:Component=>Saved308<T>(scene);
  internal static bool CrSetWired308(WorldMacroPlaytestSession session,EnemyVitals vit,bool add)=>SetWired308(session,vit,add);
  internal static bool CrWired308(WorldMacroPlaytestSession session,EnemyVitals vit)=>Wired308(session,vit);
 }

 // #308 Creatures308 - free Asset Store creature packs as field enemy bodies + four field bosses [TEST]. User request 2026-10-12.
 // Every number, name, clip and path is data: Tools/Unity/Stage308_creatures/creatures308.json (0 = "use the measured value").
 // Where they stand: Tools/Unity/Stage308_creatures/plan_creatures308.py -> Art/World/Compact/Rebuild/Creatures308/plan.json.
 //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.Creatures308 Run "<command>"
 //   status          counts (read only)
 //   assets          every model / clip under Assets/Store308/Creatures308 (read only)
 //   inspect:<id>    components of the scene actor with that encounter Id (read only)
 //   clips           per species: body size after scaling, every role clip (length, legacy, drift), MEASURED attack contact phase
 //                   (furthest forward reach of any skin bone) and MEASURED stance-foot speed of walk / run. Read only (preview scene).
 //   bodies:build[:<species>]   materials (ink-toned albedo copy, the Folklore298 shader, no emission), made-up clips for roles a pack
 //                   lacks, attack + vitals profile assets, one actor prefab per species under <prefabDir>. The actor is a copy of the
 //                   species' template actor in the play scene with its visual swapped; scene references are empty in the prefab and
 //                   filled again from the template when placed. Writes measured.json next to the data file.
 //   stills[:<species>]         one sheet per body (idle, walk mid-stride, attack contact, death end, next to a reference capsule)
 //   place | revert  the planned groups + bosses under root <root>: content rows (ordinary RespawnOnRest true, bosses false), actors,
 //                   session.Actors, CombatLoopWiring. revert removes exactly the rows / actors with this tool's id prefixes. Both back
 //                   up the scene and the content asset first (Tools/Unity/Stage308_creatures/Backup). Idempotent.
 //   check           the placed actors (NavMesh, rig, materials, collider, wiring, ids, boss rules, clearances) + Populate308's still whole
 //   shots           edit-mode stills of each boss and one actor per group (plain camera, no post) -> <stillDir>/place_*.png
 // Queue-safe: no dialog, refusals are strings. Writes need W_Demo_Main active, Edit mode, compiled, not dirty.
 public static class Creatures308
 {
  const string StoreRoot="Assets/Store308/Creatures308";
  [Serializable] sealed class Look{public string reference="";public float saturation,value,smoothness,eyeSmoothness,eyeTriangleShare,boundsScale;public int textureSize,normalMaxSize;public float[] eyeColor=Array.Empty<float>();}
  [Serializable] sealed class Proc{public float flinchSeconds,flinchBack,flinchPitchDeg,dazeSeconds,dazeRollDeg,dazePitchDeg,dazeDrop,toppleSeconds,toppleRollDeg,toppleHoldSeconds;}
  [Serializable] sealed class Fit{public float widthShare,lengthShare,heightShare,radiusMin,heightMin,peakMin,peakMax,plantedBand,preferredNoseGap,preferredBelowRange,driftWarnShare,noseReach,patrolWalkFactor,patrolMin,runOverPatrol,runThresholdShare;}
  [Serializable] sealed class StillCfg{public int width,height;public float fov,reference_m,distanceLengths;}
  [Serializable] sealed class PlaceCfg{public string populate="";public float encounter_clear_m,spread_max_m,boss_encounter_clear_m,boss_nav_search_m,activation,boss_detection,boss_leash;}
  [Serializable] sealed class Sp
  {
   public string key="",pack="",model="",keep="",avatar="",albedo="",normal="",idle="",walk="",run="",attack="",hit="",stun="",death="",template="",audio="",attackFrom="",vitalsFrom="",displayName="";
   public bool merge,loopStun,isBoss;
   public float length_m,yaw_deg,attackPeak01,walkMps,runMps,runThresholdMps,scaleTemplate,telegraph,recovery,damage,range,rangeMargin,arc,cooldownMin,cooldownMax,hp,speed,detection,leash,capsuleRadius,capsuleHeight,saturation,value;
   public float defence;   // #308 WP-16 (D308-114): TEST share of a hit the body takes off (0 = none; the WP-15 cap still applies in play)
  }
  [Serializable] sealed class BossRow{public string id="",name="",species="",realm="";}
  [Serializable] sealed class Cfg
  {
   public string version="",content="",prefix="",boss_prefix="",root="",prefabDir="",stillDir="";
   public Look look=new Look();public Proc proc=new Proc();public Fit fit=new Fit();public StillCfg still=new StillCfg();public PlaceCfg place=new PlaceCfg();
   public Sp[] species=Array.Empty<Sp>();public BossRow[] bosses=Array.Empty<BossRow>();
  }
  [Serializable] sealed class PlanEnemy{public string id="",name="",contentId="",species="",realm="",group="";public float[] feet=Array.Empty<float>(),patrol2=Array.Empty<float>();}
  [Serializable] sealed class PlanBoss{public string id="",name="",species="",realm="",relaxed="";public float yaw_deg;public float[] feet=Array.Empty<float>();}
  [Serializable] sealed class Plan{public string version="";public float nav_radius_m=2f;public PlanEnemy[] enemies=Array.Empty<PlanEnemy>();public PlanBoss[] bosses=Array.Empty<PlanBoss>();}
  [Serializable] sealed class PopPatrol{public float walk_speed_mps=1.7f,pause_min_s=1.5f,pause_max_s=4f;}
  [Serializable] sealed class PopPlace{public float house_clear_m=28f;}
  [Serializable] sealed class PopCfg{public PopPatrol patrol=new PopPatrol();public PopPlace place=new PopPlace();}
  [Serializable] sealed class PopPlanEnemy{public string id="";}
  [Serializable] sealed class PopPlan{public PopPlanEnemy[] enemies=Array.Empty<PopPlanEnemy>();}
  [Serializable] sealed class House{public float x,z;}
  [Serializable] sealed class Houses{public House[] houses=Array.Empty<House>();}
  [Serializable] sealed class Measured
  {
   public string key="",feet="",note="";
   public float scale,length,width,height,attackPeak01,attackReach,walkMps,runMps,walkDrift,runDrift,capsuleRadius,capsuleHeight,range,preferred,deathSeconds;
  }
  [Serializable] sealed class MeasuredFile{public string utc="";public List<Measured> rows=new List<Measured>();}
  sealed class Refuse:Exception{public Refuse(string m):base(m){}}

  static string Stage=>Path.Combine(Harness303.RepoRoot,"Tools","Unity","Stage308_creatures");
  static string PlanFile=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","Creatures308","plan.json");
  static string MeasuredPath=>Path.Combine(Stage,"measured.json");
  static Cfg Data(){string f=Path.Combine(Stage,"creatures308.json");if(!File.Exists(f))throw new Refuse("no "+f);return JsonUtility.FromJson<Cfg>(File.ReadAllText(f));}
  static Plan PlanData(){if(!File.Exists(PlanFile))throw new Refuse("no "+PlanFile+" (run python Tools/Unity/Stage308_creatures/plan_creatures308.py)");return JsonUtility.FromJson<Plan>(File.ReadAllText(PlanFile));}
  static PopCfg PopData(Cfg c){string f=Path.Combine(Harness303.RepoRoot,c.place.populate);return File.Exists(f)?JsonUtility.FromJson<PopCfg>(File.ReadAllText(f)):new PopCfg();}
  static MeasuredFile ReadMeasured()=>File.Exists(MeasuredPath)?JsonUtility.FromJson<MeasuredFile>(File.ReadAllText(MeasuredPath)):new MeasuredFile();
  static string F(float v,string f="0.###")=>v.ToString(f,CultureInfo.InvariantCulture);
  static string V(Vector3 v)=>"("+F(v.x)+","+F(v.y)+","+F(v.z)+")";
  static Vector3 P(float[] a)=>new Vector3(a[0],a[1],a[2]);
  static string PathOf(Transform t,Transform root){var sb=new StringBuilder(t.name);for(var p=t.parent;p!=null&&p!=root;p=p.parent)sb.Insert(0,p.name+"/");return sb.ToString();}
  static string PrefabPath(Cfg c,string key)=>c.prefabDir+"/PF_Creature308_"+key+".prefab";
  static bool Own(Cfg c,string id)=>id!=null&&(id.StartsWith(c.prefix,StringComparison.Ordinal)||id.StartsWith(c.boss_prefix,StringComparison.Ordinal));
  static void Folder(string assetDir)
  {
   if(AssetDatabase.IsValidFolder(assetDir))return;string parent=Path.GetDirectoryName(assetDir).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(assetDir));
  }

  public static string Run(string command)
  {
   string c=(command??"").Trim();
   try
   {
    if(c=="assets")return Assets();
    if(c.StartsWith("inspect:",StringComparison.Ordinal))return Inspect(c.Substring(8));
    if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Edit mode only (Play is running)";
    if(EditorApplication.isCompiling||EditorUtility.scriptCompilationFailed)return "REFUSED scripts are compiling or failed to compile";
    var cfg=Data();var scene=SceneManager.GetActiveScene();bool dirtyBefore=scene.isDirty;int rootsBefore=scene.rootCount;string result;
    if(c=="clips")result=Clips(cfg);
    else if(c=="stills"||c.StartsWith("stills:",StringComparison.Ordinal))result=Stills(cfg,c.Length>7?c.Substring(7):"");
    else
    {
     if(scene.name!="W_Demo_Main")return "REFUSED open W_Demo_Main first (active scene is "+scene.name+")";
     var session=Content308.CrSaved308<WorldMacroPlaytestSession>(scene).FirstOrDefault();if(session==null)return "REFUSED no WorldMacroPlaytestSession in the play scene";
     var content=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(cfg.content);if(content==null||session.Content!=content)return "REFUSED the session does not read "+cfg.content;
     if(c=="status")return Status(cfg,scene,session,content);
     if(c=="check")return Check(cfg,scene,session,content);
     if(c=="shots")return Shots(cfg,scene);
     if(scene.isDirty)return "REFUSED the play scene has unsaved changes";
     if(c=="bodies:build"||c.StartsWith("bodies:build:",StringComparison.Ordinal))result=Build(cfg,scene,c.Length>13?c.Substring(13):"");
     else if(c=="place"||c=="revert")return PlaceOrRevert(cfg,scene,session,content,c=="place");
     else return "REFUSED unknown Creatures308 command '"+c+"' (status | assets | inspect:<id> | clips | bodies:build[:<species>] | stills[:<species>] | place | revert | check | shots)";
    }
    scene=SceneManager.GetActiveScene();
    if(scene.isDirty!=dirtyBefore||scene.rootCount!=rootsBefore)result+="\nWARNING the open scene changed during a command that must not touch it (dirty "+dirtyBefore+" -> "+scene.isDirty+", roots "+rootsBefore+" -> "+scene.rootCount+")";
    else result+="\nopen scene unchanged ("+scene.name+", dirty "+scene.isDirty+", roots "+scene.rootCount+")";
    return result;
   }
   catch(Refuse r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
  }

  // ---------- preview scene workspace: nothing is left in the open scene ----------

  sealed class Work:IDisposable
  {
   public readonly Scene Scene=EditorSceneManager.NewPreviewScene();readonly List<GameObject> roots=new List<GameObject>();readonly List<Object> temp=new List<Object>();
   public GameObject Empty(string name){var go=EditorUtility.CreateGameObjectWithHideFlags(name,HideFlags.HideAndDontSave);return Adopt(go);}
   public GameObject Adopt(GameObject go)
   {
    go.hideFlags=HideFlags.HideAndDontSave;SceneManager.MoveGameObjectToScene(go,Scene);
    if(go.scene!=Scene){Object.DestroyImmediate(go);throw new Exception("could not move a work object into the preview scene");}
    foreach(var t in go.GetComponentsInChildren<Transform>(true))t.gameObject.hideFlags=HideFlags.None;
    roots.Add(go);return go;
   }
   public T Temp<T>(T o)where T:Object{temp.Add(o);return o;}
   public void Dispose(){foreach(var r in roots)if(r!=null)Object.DestroyImmediate(r);foreach(var o in temp)if(o!=null)Object.DestroyImmediate(o);EditorSceneManager.ClosePreviewScene(Scene);}
  }

  // ---------- read-only surveys ----------

  static string Assets()
  {
   var sb=new StringBuilder("Creatures308 assets\n");
   foreach(string guid in AssetDatabase.FindAssets("t:Model",new[]{StoreRoot}))
   {
    string path=AssetDatabase.GUIDToAssetPath(guid);var imp=AssetImporter.GetAtPath(path) as ModelImporter;var go=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(go==null)continue;
    var all=AssetDatabase.LoadAllAssetsAtPath(path);var clips=all.OfType<AnimationClip>().Where(k=>!k.name.StartsWith("__preview__",StringComparison.Ordinal)).ToArray();var avatar=all.OfType<Avatar>().FirstOrDefault();
    sb.Append(path).Append(" | type ").Append(imp!=null?imp.animationType.ToString():"?").Append(" scale ").Append(imp!=null?F(imp.globalScale):"?").Append(" avatar ").Append(avatar!=null?(avatar.isValid?"valid":"invalid")+(avatar.isHuman?" human":""):"none")
     .Append(imp!=null&&imp.optimizeGameObjects?" OPTIMIZED":"").Append('\n');
    foreach(var r in go.GetComponentsInChildren<Renderer>(true))
    {
     var smr=r as SkinnedMeshRenderer;var mesh=smr!=null?smr.sharedMesh:r.GetComponent<MeshFilter>()!=null?r.GetComponent<MeshFilter>().sharedMesh:null;
     sb.Append("   R ").Append(PathOf(r.transform,go.transform)).Append(smr!=null?" skinned bones "+smr.bones.Length+" root "+(smr.rootBone!=null?smr.rootBone.name:"-"):" static").Append(" verts ").Append(mesh!=null?mesh.vertexCount:0).Append(" sub ").Append(mesh!=null?mesh.subMeshCount:0)
      .Append(" bounds c").Append(V(r.bounds.center)).Append(" s").Append(V(r.bounds.size)).Append('\n');
    }
    sb.Append("   children ").Append(string.Join(", ",go.transform.Cast<Transform>().Select(t=>t.name+(t.childCount>0?"("+t.GetComponentsInChildren<Transform>(true).Length+")":"")))).Append('\n');
    foreach(var k in clips)sb.Append("   C ").Append(k.name).Append(" len ").Append(F(k.length)).Append(" fps ").Append(F(k.frameRate)).Append(k.legacy?" LEGACY":"").Append(k.isHumanMotion?" HUMAN":"").Append(k.hasRootCurves?" rootCurves":"").Append(k.hasGenericRootTransform?" genericRoot":"").Append('\n');
   }
   return sb.ToString();
  }

  static PrologueEncounter FindActor(Scene scene,string id)=>Content308.CrSaved308<PrologueEncounter>(scene).FirstOrDefault(a=>a.Id==id);

  static string Inspect(string id)
  {
   var scene=SceneManager.GetActiveScene();var sb=new StringBuilder("Creatures308 inspect "+id+" in "+scene.name+"\n");
   var actor=FindActor(scene,id);if(actor==null)return "REFUSED no actor "+id;
   sb.Append("actor ").Append(PathOf(actor.transform,null)).Append(" pos ").Append(V(actor.transform.position)).Append(" scale ").Append(V(actor.transform.lossyScale)).Append(" layer ").Append(LayerMask.LayerToName(actor.gameObject.layer)).Append(" active ").Append(actor.gameObject.activeSelf).Append('\n');
   foreach(var t in actor.GetComponentsInChildren<Transform>(true))
   {
    var comps=t.GetComponents<Component>().Where(k=>!(k is Transform)).ToArray();if(comps.Length==0&&t!=actor.transform&&t.parent!=actor.transform)continue;
    sb.Append(" T ").Append(PathOf(t,actor.transform)).Append(" lp ").Append(V(t.localPosition)).Append(" ls ").Append(V(t.localScale)).Append(" layer ").Append(LayerMask.LayerToName(t.gameObject.layer)).Append(t.gameObject.activeSelf?"":" OFF").Append('\n');
    foreach(var k in comps)
    {
     if(k==null){sb.Append("    (missing script)\n");continue;}
     sb.Append("    ").Append(k.GetType().Name);if(k is Behaviour b&&!b.enabled)sb.Append(" (disabled)");
     if(t==actor.transform||k is Collider||k is NavMeshAgent||k is Animator||k is Renderer)
     {
      var so=new SerializedObject(k);var p=so.GetIterator();bool enter=true;int n=0;
      while(p.NextVisible(enter)&&n<70)
      {
       enter=false;if(p.name=="m_Script")continue;n++;
       string val=p.propertyType==SerializedPropertyType.ObjectReference?(p.objectReferenceValue==null?"null":p.objectReferenceValue.name+"<"+p.objectReferenceValue.GetType().Name+">")
        :p.propertyType==SerializedPropertyType.Float?F(p.floatValue):p.propertyType==SerializedPropertyType.Integer?p.intValue.ToString():p.propertyType==SerializedPropertyType.Boolean?p.boolValue.ToString():p.propertyType==SerializedPropertyType.String?"'"+p.stringValue+"'"
        :p.propertyType==SerializedPropertyType.Vector3?V(p.vector3Value):p.propertyType==SerializedPropertyType.Enum?p.enumValueIndex.ToString():p.isArray?"["+p.arraySize+"]":p.propertyType.ToString();
       sb.Append("\n       ").Append(p.name).Append(" = ").Append(val);
      }
     }
     sb.Append('\n');
    }
   }
   return sb.ToString();
  }

  // ---------- body: the pack model scaled to its target length, grounded, centred, facing +Z of the wrapper ----------

  sealed class Body
  {
   public GameObject wrapper,model;public Animator animator;public SkinnedMeshRenderer[] skins=Array.Empty<SkinnedMeshRenderer>();public Transform[] bones=Array.Empty<Transform>();public Transform bodyRoot,hips;
   public float scale,length,width,height;
  }

  static AnimationClip Clip(string spec)
  {
   if(string.IsNullOrEmpty(spec)||spec.StartsWith("proc:",StringComparison.Ordinal))return null;
   int at=spec.IndexOf('#');string path=at<0?spec:spec.Substring(0,at),name=at<0?"":spec.Substring(at+1);
   var clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(k=>!k.name.StartsWith("__preview__",StringComparison.Ordinal)).ToArray();
   var clip=name.Length==0?clips.FirstOrDefault():clips.FirstOrDefault(k=>k.name==name);
   if(clip==null)throw new Refuse("clip '"+name+"' not in "+path+" (has: "+string.Join(", ",clips.Select(k=>k.name))+")");
   return clip;
  }

  static Vector3[] SkinWorld(SkinnedMeshRenderer smr)
  {
   var mesh=smr.sharedMesh;var v=mesh.vertices;var bw=mesh.boneWeights;var bind=mesh.bindposes;var bones=smr.bones;var o=new Vector3[v.Length];
   if(bw.Length!=v.Length||bones.Length==0||bind.Length!=bones.Length){var m=smr.transform.localToWorldMatrix;for(int i=0;i<v.Length;i++)o[i]=m.MultiplyPoint3x4(v[i]);return o;}
   var mats=new Matrix4x4[bones.Length];for(int i=0;i<bones.Length;i++)mats[i]=bones[i]!=null?bones[i].localToWorldMatrix*bind[i]:smr.transform.localToWorldMatrix;
   for(int i=0;i<v.Length;i++)
   {
    var w=bw[i];Vector3 p=Vector3.zero;
    if(w.weight0>0)p+=mats[w.boneIndex0].MultiplyPoint3x4(v[i])*w.weight0;if(w.weight1>0)p+=mats[w.boneIndex1].MultiplyPoint3x4(v[i])*w.weight1;
    if(w.weight2>0)p+=mats[w.boneIndex2].MultiplyPoint3x4(v[i])*w.weight2;if(w.weight3>0)p+=mats[w.boneIndex3].MultiplyPoint3x4(v[i])*w.weight3;
    o[i]=p;
   }
   return o;
  }

  static Bounds Measure(Body b)
  {
   bool any=false;Bounds r=default;var w=b.wrapper.transform;
   foreach(var s in b.skins)foreach(var p in SkinWorld(s)){var q=w.InverseTransformPoint(p);if(!any){r=new Bounds(q,Vector3.zero);any=true;}else r.Encapsulate(q);}
   if(!any)throw new Exception("no skinned surface to measure");return r;
  }

  static void Pose(Animator a,AnimationClip clip,float t)
  {
   var keep=a.cullingMode;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;
   try{CompactFolklore298.SamplePoseGraph298(a,clip,t);}finally{a.cullingMode=keep;}
  }

  static SkinnedMeshRenderer MergeSkins(GameObject model,SkinnedMeshRenderer[] skins,string meshAsset)
  {
   var bones=new List<Transform>();var binds=new List<Matrix4x4>();var verts=new List<Vector3>();var normals=new List<Vector3>();var uvs=new List<Vector2>();var weights=new List<BoneWeight>();var tris=new List<int>();
   bool Same(Matrix4x4 a,Matrix4x4 b){for(int i=0;i<16;i++)if(Mathf.Abs(a[i]-b[i])>1e-4f)return false;return true;}
   foreach(var smr in skins)
   {
    var mesh=smr.sharedMesh;var M=model.transform.worldToLocalMatrix*smr.transform.localToWorldMatrix;var inv=M.inverse;var sb=smr.bones;var mb=mesh.bindposes;var map=new int[sb.Length];
    for(int b=0;b<sb.Length;b++)
    {
     var bp=mb[b]*inv;int at=-1;for(int j=0;j<bones.Count;j++)if(bones[j]==sb[b]&&Same(binds[j],bp)){at=j;break;}
     if(at<0){at=bones.Count;bones.Add(sb[b]);binds.Add(bp);}
     map[b]=at;
    }
    int start=verts.Count;var v=mesh.vertices;var n=mesh.normals;var uv=mesh.uv;var bw=mesh.boneWeights;
    for(int i=0;i<v.Length;i++)
    {
     verts.Add(M.MultiplyPoint3x4(v[i]));normals.Add(n.Length==v.Length?M.MultiplyVector(n[i]).normalized:Vector3.up);uvs.Add(uv.Length==v.Length?uv[i]:Vector2.zero);
     var w=bw.Length==v.Length?bw[i]:new BoneWeight{weight0=1};
     weights.Add(new BoneWeight{boneIndex0=map[w.boneIndex0],weight0=w.weight0,boneIndex1=w.weight1>0?map[w.boneIndex1]:0,weight1=w.weight1,boneIndex2=w.weight2>0?map[w.boneIndex2]:0,weight2=w.weight2,boneIndex3=w.weight3>0?map[w.boneIndex3]:0,weight3=w.weight3});
    }
    for(int s=0;s<mesh.subMeshCount;s++)foreach(int t in mesh.GetTriangles(s))tris.Add(start+t);
   }
   var merged=new Mesh{name=Path.GetFileNameWithoutExtension(meshAsset),indexFormat=verts.Count>65000?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16};
   merged.SetVertices(verts);merged.SetNormals(normals);merged.SetUVs(0,uvs);merged.boneWeights=weights.ToArray();merged.bindposes=binds.ToArray();merged.SetTriangles(tris,0);merged.RecalculateBounds();merged.RecalculateTangents();
   Mesh use=merged;
   if(!string.IsNullOrEmpty(meshAsset))
   {
    var old=AssetDatabase.LoadAssetAtPath<Mesh>(meshAsset);
    if(old!=null){string keepName=old.name;EditorUtility.CopySerialized(merged,old);old.name=keepName;EditorUtility.SetDirty(old);AssetDatabase.SaveAssetIfDirty(old);Object.DestroyImmediate(merged);use=old;}
    else{Folder(Path.GetDirectoryName(meshAsset).Replace('\\','/'));AssetDatabase.CreateAsset(merged,meshAsset);}
   }
   var go=new GameObject("Body");go.transform.SetParent(model.transform,false);var res=go.AddComponent<SkinnedMeshRenderer>();
   res.sharedMesh=use;res.bones=bones.ToArray();res.rootBone=skins[0].rootBone!=null?skins[0].rootBone:bones[0];res.sharedMaterials=new Material[1];
   var parents=skins.Select(s=>s.transform.parent).Where(p=>p!=null&&p!=model.transform).Distinct().ToArray();
   foreach(var s in skins)Object.DestroyImmediate(s.gameObject);
   foreach(var p in parents)if(p!=null&&p.childCount==0&&p.GetComponents<Component>().Length==1)Object.DestroyImmediate(p.gameObject);
   return res;
  }

  static Body BuildVisual(Cfg cfg,Sp s,Work w,bool saveAssets,out bool mergedInMemory)
  {
   mergedInMemory=false;
   var imp=AssetImporter.GetAtPath(s.model) as ModelImporter;if(imp==null)throw new Refuse(s.key+": no model at "+s.model);
   if(imp.optimizeGameObjects){if(!saveAssets)throw new Refuse(s.key+": "+s.model+" is imported with optimized (hidden) bones; bodies:build turns that off");imp.optimizeGameObjects=false;imp.SaveAndReimport();}
   var asset=AssetDatabase.LoadAssetAtPath<GameObject>(s.model);if(asset==null)throw new Refuse(s.key+": model did not import "+s.model);
   var b=new Body{wrapper=w.Empty("Creature308_Visual")};
   b.model=Object.Instantiate(asset,b.wrapper.transform);b.model.name="Model";b.model.SetActive(true);
   b.model.transform.localPosition=Vector3.zero;b.model.transform.localRotation=Quaternion.Euler(0,s.yaw_deg,0)*asset.transform.localRotation;
   foreach(var t in b.model.GetComponentsInChildren<Transform>(true))t.gameObject.hideFlags=HideFlags.None;
   var keep=string.IsNullOrEmpty(s.keep)?null:new Regex(s.keep);
   foreach(var r in b.model.GetComponentsInChildren<Renderer>(true).ToArray())
   {
    if(r==null)continue;
    if(keep!=null&&!keep.IsMatch(PathOf(r.transform,b.model.transform))){var parent=r.transform.parent;Object.DestroyImmediate(r.gameObject);if(parent!=null&&parent!=b.model.transform&&parent.childCount==0&&parent.GetComponents<Component>().Length==1)Object.DestroyImmediate(parent.gameObject);}
    else{for(var p=r.transform;p!=null&&p!=b.wrapper.transform;p=p.parent)p.gameObject.SetActive(true);r.enabled=true;}
   }
   foreach(var extra in b.model.GetComponentsInChildren<Component>(true).Where(k=>k is Camera||k is Light||k is Collider||k is Rigidbody||k is Animation||k is LODGroup).ToArray())Object.DestroyImmediate(extra);
   b.skins=b.model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(k=>k.sharedMesh!=null).ToArray();
   if(b.skins.Length==0)throw new Refuse(s.key+": no skinned renderer left after keep '"+s.keep+"'");
   if(s.merge&&b.skins.Length>1){b.skins=new[]{MergeSkins(b.model,b.skins,saveAssets?cfg.prefabDir+"/Meshes/Mesh_"+s.pack+"_merged.asset":"")};mergedInMemory=!saveAssets;}
   b.bones=b.skins.SelectMany(k=>k.bones).Where(k=>k!=null).Distinct().ToArray();if(b.bones.Length==0)throw new Refuse(s.key+": the skin has no bones");
   b.hips=b.skins[0].rootBone!=null?b.skins[0].rootBone:b.bones[0];
   b.bodyRoot=b.bones[0];while(b.bodyRoot.parent!=null&&b.bodyRoot.parent!=b.model.transform)b.bodyRoot=b.bodyRoot.parent;
   b.animator=b.model.GetComponent<Animator>();if(b.animator==null)b.animator=b.model.AddComponent<Animator>();
   b.animator.avatar=s.avatar=="none"?null:AssetDatabase.LoadAllAssetsAtPath(s.model).OfType<Avatar>().FirstOrDefault(a=>a.isValid);
   b.animator.runtimeAnimatorController=null;b.animator.applyRootMotion=false;b.animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;b.animator.enabled=true;
   var idle=Clip(s.idle)??throw new Refuse(s.key+": idle clip missing");
   Pose(b.animator,idle,0);var m=Measure(b);if(m.size.z<1e-4f)throw new Refuse(s.key+": zero body length");
   b.scale=s.length_m/m.size.z;b.model.transform.localScale=b.model.transform.localScale*b.scale;
   Pose(b.animator,idle,0);m=Measure(b);b.model.transform.localPosition+=new Vector3(-m.center.x,-m.min.y,-m.center.z);
   Pose(b.animator,idle,0);m=Measure(b);b.length=m.size.z;b.width=m.size.x;b.height=m.max.y;
   foreach(var k in b.skins)
   {
    var anchor=k.rootBone!=null?k.rootBone:k.transform;bool any=false;Bounds lb=default;
    foreach(var p in SkinWorld(k)){var q=anchor.InverseTransformPoint(p);if(!any){lb=new Bounds(q,Vector3.zero);any=true;}else lb.Encapsulate(q);}
    lb.extents=Vector3.one*(lb.extents.magnitude*cfg.look.boundsScale);k.updateWhenOffscreen=false;k.localBounds=lb;
   }
   foreach(var t in b.wrapper.GetComponentsInChildren<Transform>(true))t.gameObject.layer=0;
   return b;
  }

  // furthest forward reach of any skin bone over the clip -> contact phase and reach (wrapper space: forward = +Z, origin = body centre on the ground)
  static void MeasureAttack(Body b,AnimationClip clip,Fit fit,out float peak01,out float reach,out string who)
  {
   int n=Mathf.Max(24,Mathf.CeilToInt(clip.length*60f));reach=float.NegativeInfinity;int at=0;who="";var w=b.wrapper.transform;
   for(int i=0;i<=n;i++){Pose(b.animator,clip,clip.length*i/n);foreach(var bone in b.bones){float z=w.InverseTransformPoint(bone.position).z;if(z>reach){reach=z;at=i;who=bone.name;}}}
   peak01=Mathf.Clamp((float)at/n,fit.peakMin,fit.peakMax);
  }

  // stance-foot speed: the (up to four) lowest, well separated bones; while one is in its ground band its backward speed is the body's forward speed
  static float MeasureGait(Body b,AnimationClip clip,Fit fit,out string detail,out float drift)
  {
   int n=Mathf.Max(24,Mathf.CeilToInt(clip.length*60f));float dt=clip.length/n;var w=b.wrapper.transform;var pos=new Vector3[b.bones.Length,n+1];var hip=new Vector3[n+1];
   for(int i=0;i<=n;i++){Pose(b.animator,clip,clip.length*i/n);for(int k=0;k<b.bones.Length;k++)pos[k,i]=w.InverseTransformPoint(b.bones[k].position);hip[i]=w.InverseTransformPoint(b.hips.position);}
   var dh=hip[n]-hip[0];dh.y=0;drift=dh.magnitude;
   var low=new float[b.bones.Length];for(int k=0;k<b.bones.Length;k++){low[k]=float.PositiveInfinity;for(int i=0;i<=n;i++)low[k]=Mathf.Min(low[k],pos[k,i].y);}
   var order=Enumerable.Range(0,b.bones.Length).OrderBy(k=>low[k]).ToArray();var feet=new List<int>();
   foreach(int k in order)
   {
    if(feet.Count>=4||low[k]>low[order[0]]+b.height*.12f)break;
    if(feet.All(f=>{var d=pos[f,0]-pos[k,0];d.y=0;return d.magnitude>b.length*.12f;}))feet.Add(k);
   }
   float band=b.height*fit.plantedBand;var per=new List<float>();int count=0;      // median over the feet: one tail tip or dragging toe among them does not pull the figure
   foreach(int f in feet)
   {
    double sum=0;int c=0;for(int i=1;i<n;i++)if(pos[f,i-1].y<=low[f]+band&&pos[f,i].y<=low[f]+band&&pos[f,i+1].y<=low[f]+band){sum+=-(pos[f,i+1].z-pos[f,i-1].z)/(2*dt);c++;}
    if(c>=3){per.Add((float)(sum/c));count+=c;}
   }
   per.Sort();detail=string.Join("+",feet.Select(f=>b.bones[f].name))+" ("+count+" planted samples, per foot "+string.Join("/",per.Select(v=>F(v,"0.##")))+")";
   float planted=per.Count==0?0f:per.Count%2==1?per[per.Count/2]:(per[per.Count/2-1]+per[per.Count/2])*.5f;
   // a crawler never lifts its feet out of the band (every sample "planted", mean zero): stride length over the time the foot travels backward instead
   var back=new List<float>();
   foreach(int f in feet)
   {
    float zmin=float.PositiveInfinity,zmax=float.NegativeInfinity;int frames=0;for(int i=0;i<=n;i++){zmin=Mathf.Min(zmin,pos[f,i].z);zmax=Mathf.Max(zmax,pos[f,i].z);if(i>0&&pos[f,i].z<pos[f,i-1].z)frames++;}
    if(frames>0)back.Add((zmax-zmin)/(frames*dt));
   }
   back.Sort();float stride=back.Count==0?0f:back.Count%2==1?back[back.Count/2]:(back[back.Count/2-1]+back[back.Count/2])*.5f;
   detail+=", stride/back-time "+F(stride,"0.##");
   return planted>=stride*.3f?planted:stride;
  }

  // ---------- clips (read only) ----------

  static string Clips(Cfg cfg)
  {
   var sb=new StringBuilder("Creatures308 clips (measured in a preview scene; nothing written)\n");
   foreach(var s in cfg.species)
   {
    if(s.scaleTemplate>0){sb.Append(s.key).Append(": scaled copy of ").Append(s.template).Append(" x").Append(F(s.scaleTemplate)).Append(" (its own clips)\n");continue;}
    try
    {
     using(var w=new Work())
     {
      var b=BuildVisual(cfg,s,w,false,out _);
      sb.Append(s.key).Append(": scale ").Append(F(b.scale,"0.####")).Append(" L ").Append(F(b.length)).Append(" W ").Append(F(b.width)).Append(" H ").Append(F(b.height)).Append(" bones ").Append(b.bones.Length).Append(" skins ").Append(b.skins.Length)
       .Append(" verts ").Append(b.skins.Sum(k=>k.sharedMesh.vertexCount)).Append(" bodyRoot ").Append(b.bodyRoot.name).Append(" hips ").Append(b.hips.name).Append('\n');
      foreach(var (role,spec) in new[]{("idle",s.idle),("walk",s.walk),("run",s.run),("attack",s.attack),("hit",s.hit),("stun",s.stun),("death",s.death)})
      {
       if(string.IsNullOrEmpty(spec)){sb.Append("   ").Append(role).Append(": -\n");continue;}
       if(spec.StartsWith("proc:",StringComparison.Ordinal)){sb.Append("   ").Append(role).Append(": ").Append(spec).Append(" (made by bodies:build)\n");continue;}
       var clip=Clip(spec);sb.Append("   ").Append(role).Append(": ").Append(clip.name).Append(" len ").Append(F(clip.length)).Append(clip.legacy?" LEGACY":"").Append(clip.isHumanMotion?" HUMAN":"");
       Pose(b.animator,clip,0);var a=Measure(b);Pose(b.animator,clip,clip.length*.5f);var mid=Measure(b);
       sb.Append(" pose0 min.y ").Append(F(a.min.y)).Append(" mid size ").Append(V(mid.size));
       if(role=="walk"||role=="run"){float v=MeasureGait(b,clip,cfg.fit,out string d,out float drift);sb.Append(" | stance speed ").Append(F(v)).Append(" m/s feet ").Append(d).Append(" drift ").Append(F(drift)).Append(drift>b.length*cfg.fit.driftWarnShare?" DRIFT":"");}
       if(role=="attack"){MeasureAttack(b,clip,cfg.fit,out float peak,out float reach,out string who);sb.Append(" | contact phase ").Append(F(peak)).Append(" reach ").Append(F(reach)).Append(" m (").Append(who).Append("), nose at rest ").Append(F(b.length*.5f));}
       sb.Append('\n');
      }
     }
    }
    catch(Refuse r){sb.Append(s.key).Append(": REFUSED ").Append(r.Message).Append('\n');}
   }
   return sb.ToString();
  }

  // ---------- made-up clips: the idle pose with only the body root moving ----------

  static AnimationClip MakeProc(Cfg cfg,Sp s,Body b,string kind,AnimationClip idle)
  {
   var p=cfg.proc;Pose(b.animator,idle,0);var model=b.model.transform;var w=b.wrapper.transform;var B=b.bodyRoot;
   Vector3 pivot=model.InverseTransformPoint(w.position),fwd=model.InverseTransformDirection(w.forward).normalized,up=model.InverseTransformDirection(w.up).normalized,right=model.InverseTransformDirection(w.right).normalized;
   float unit=1f/Mathf.Max(1e-6f,model.lossyScale.x);float L=b.length*unit,Wd=b.width*unit,H=b.height*unit;
   var times=new List<float>();var rots=new List<Quaternion>();var moves=new List<Vector3>();float T;bool loop=false;
   if(kind=="flinch"){T=p.flinchSeconds;foreach(var (u,a) in new[]{(0f,0f),(.22f,1f),(.5f,.55f),(1f,0f)}){times.Add(u*T);rots.Add(Quaternion.AngleAxis(-p.flinchPitchDeg*a,right));moves.Add(-fwd*(p.flinchBack*L*a));}}
   else if(kind=="daze"){T=p.dazeSeconds;loop=true;for(int i=0;i<=8;i++){float u=i/8f;times.Add(u*T);rots.Add(Quaternion.AngleAxis(p.dazeRollDeg*Mathf.Sin(u*Mathf.PI*2),fwd)*Quaternion.AngleAxis(p.dazePitchDeg,right));moves.Add(-up*(p.dazeDrop*H));}}
   else if(kind=="topple")
   {
    T=p.toppleSeconds+p.toppleHoldSeconds;
    for(int i=0;i<=12;i++){float u=i/12f,th=p.toppleRollDeg*u*u;times.Add(u*p.toppleSeconds);rots.Add(Quaternion.AngleAxis(th,fwd));moves.Add(up*(Wd*.5f*Mathf.Sin(th*Mathf.Deg2Rad)));}
    times.Add(T);rots.Add(rots[rots.Count-1]);moves.Add(moves[moves.Count-1]);
   }
   else throw new Refuse(s.key+": unknown made-up clip '"+kind+"' (flinch | daze | topple)");
   var bindings=new List<EditorCurveBinding>();var curves=new List<AnimationCurve>();
   void Add(string path,string prop,Func<int,float> value,int keys){var c=new AnimationCurve();for(int i=0;i<keys;i++)c.AddKey(new Keyframe(keys==2?(i==0?0:T):times[i],value(i)));bindings.Add(EditorCurveBinding.FloatCurve(path,typeof(Transform),prop));curves.Add(c);}
   foreach(var t in model.GetComponentsInChildren<Transform>(true))
   {
    if(t==model)continue;string path=AnimationUtility.CalculateTransformPath(t,model);
    if(t==B)
    {
     var lp0=t.localPosition;var lr0=t.localRotation;var pos=new Vector3[times.Count];var rot=new Quaternion[times.Count];
     for(int i=0;i<times.Count;i++){pos[i]=rots[i]*(lp0-pivot)+pivot+moves[i];rot[i]=rots[i]*lr0;if(i>0&&Quaternion.Dot(rot[i],rot[i-1])<0)rot[i]=new Quaternion(-rot[i].x,-rot[i].y,-rot[i].z,-rot[i].w);}
     Add(path,"m_LocalPosition.x",i=>pos[i].x,times.Count);Add(path,"m_LocalPosition.y",i=>pos[i].y,times.Count);Add(path,"m_LocalPosition.z",i=>pos[i].z,times.Count);
     Add(path,"m_LocalRotation.x",i=>rot[i].x,times.Count);Add(path,"m_LocalRotation.y",i=>rot[i].y,times.Count);Add(path,"m_LocalRotation.z",i=>rot[i].z,times.Count);Add(path,"m_LocalRotation.w",i=>rot[i].w,times.Count);
    }
    else
    {
     var lp=t.localPosition;var lr=t.localRotation;
     Add(path,"m_LocalPosition.x",i=>lp.x,2);Add(path,"m_LocalPosition.y",i=>lp.y,2);Add(path,"m_LocalPosition.z",i=>lp.z,2);
     Add(path,"m_LocalRotation.x",i=>lr.x,2);Add(path,"m_LocalRotation.y",i=>lr.y,2);Add(path,"m_LocalRotation.z",i=>lr.z,2);Add(path,"m_LocalRotation.w",i=>lr.w,2);
    }
   }
   string file=cfg.prefabDir+"/Clips/"+s.pack+"_"+kind+".anim";Folder(cfg.prefabDir+"/Clips");
   var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(file);
   if(clip==null){clip=new AnimationClip{name=s.pack+"_"+kind,frameRate=30};AssetDatabase.CreateAsset(clip,file);}else clip.ClearCurves();
   AnimationUtility.SetEditorCurves(clip,bindings.ToArray(),curves.ToArray());
   var st=AnimationUtility.GetAnimationClipSettings(clip);st.loopTime=loop;AnimationUtility.SetAnimationClipSettings(clip,st);
   EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip);
   return clip;
  }

  // ---------- materials: the Folklore298 shader, an ink-toned copy of the pack albedo, no emission ----------

  static Texture2D InkTexture(Cfg cfg,Sp s,Work w)
  {
   var src=AssetDatabase.LoadAssetAtPath<Texture2D>(s.albedo);if(src==null)throw new Refuse(s.key+": albedo missing "+s.albedo);
   float sat=s.saturation>0?s.saturation:cfg.look.saturation,val=s.value>0?s.value:cfg.look.value;
   float k=Mathf.Min(1f,(float)cfg.look.textureSize/Mathf.Max(src.width,src.height));int W=Mathf.Max(1,Mathf.RoundToInt(src.width*k)),H=Mathf.Max(1,Mathf.RoundToInt(src.height*k));
   var rt=RenderTexture.GetTemporary(W,H,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var prev=RenderTexture.active;var tex=w.Temp(new Texture2D(W,H,TextureFormat.RGBA32,false));
   try{Graphics.Blit(src,rt);RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,W,H),0,0);tex.Apply();}finally{RenderTexture.active=prev;RenderTexture.ReleaseTemporary(rt);}
   var px=tex.GetPixels32();
   for(int i=0;i<px.Length;i++)
   {
    float r=px[i].r,g=px[i].g,b=px[i].b,l=.299f*r+.587f*g+.114f*b;
    px[i]=new Color32((byte)Mathf.Clamp((l+(r-l)*sat)*val,0,255),(byte)Mathf.Clamp((l+(g-l)*sat)*val,0,255),(byte)Mathf.Clamp((l+(b-l)*sat)*val,0,255),255);
   }
   tex.SetPixels32(px);tex.Apply();
   string file=cfg.prefabDir+"/Textures/T_"+s.key+"_ink.png";Folder(cfg.prefabDir+"/Textures");
   File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"..",file)),tex.EncodeToPNG());AssetDatabase.ImportAsset(file,ImportAssetOptions.ForceUpdate);
   var ti=AssetImporter.GetAtPath(file) as TextureImporter;
   if(ti!=null&&(ti.maxTextureSize!=cfg.look.textureSize||!ti.sRGBTexture||ti.filterMode!=src.filterMode||ti.mipmapEnabled!=(src.mipmapCount>1)))
   {ti.maxTextureSize=cfg.look.textureSize;ti.sRGBTexture=true;ti.filterMode=src.filterMode;ti.mipmapEnabled=src.mipmapCount>1;ti.SaveAndReimport();}
   return AssetDatabase.LoadAssetAtPath<Texture2D>(file);
  }

  static Material Mat(Cfg cfg,string file,Shader shader)
  {
   var m=AssetDatabase.LoadAssetAtPath<Material>(file);
   if(m==null){Folder(cfg.prefabDir+"/Materials");m=new Material(shader);AssetDatabase.CreateAsset(m,file);}else if(m.shader!=shader)m.shader=shader;
   return m;
  }

  static void Materials(Cfg cfg,Sp s,Body b,Work w)
  {
   var reference=AssetDatabase.LoadAssetAtPath<Material>(cfg.look.reference);if(reference==null||reference.shader==null)throw new Refuse("look.reference material missing "+cfg.look.reference);
   var albedo=InkTexture(cfg,s,w);Texture2D normal=null;
   if(!string.IsNullOrEmpty(s.normal))
   {
    var ti=AssetImporter.GetAtPath(s.normal) as TextureImporter;if(ti==null)throw new Refuse(s.key+": normal map missing "+s.normal);
    if(ti.textureType!=TextureImporterType.NormalMap||ti.maxTextureSize>cfg.look.normalMaxSize){ti.textureType=TextureImporterType.NormalMap;ti.maxTextureSize=Mathf.Min(ti.maxTextureSize,cfg.look.normalMaxSize);ti.SaveAndReimport();}
    normal=AssetDatabase.LoadAssetAtPath<Texture2D>(s.normal);
   }
   var body=Mat(cfg,cfg.prefabDir+"/Materials/M_"+s.key+"_body.mat",reference.shader);
   body.SetTexture("_BaseMap",albedo);if(body.HasProperty("_MainTex"))body.SetTexture("_MainTex",albedo);body.SetColor("_BaseColor",Color.white);
   if(body.HasProperty("_Smoothness"))body.SetFloat("_Smoothness",cfg.look.smoothness);if(body.HasProperty("_Metallic"))body.SetFloat("_Metallic",0);
   if(body.HasProperty("_BumpMap")){body.SetTexture("_BumpMap",normal);if(normal!=null)body.EnableKeyword("_NORMALMAP");else body.DisableKeyword("_NORMALMAP");}
   body.DisableKeyword("_EMISSION");body.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;if(body.HasProperty("_EmissionColor"))body.SetColor("_EmissionColor",Color.black);
   EditorUtility.SetDirty(body);AssetDatabase.SaveAssetIfDirty(body);
   Material eye=null;
   foreach(var k in b.skins)
   {
    var mesh=k.sharedMesh;int total=0;var counts=new int[mesh.subMeshCount];for(int i=0;i<counts.Length;i++){counts[i]=(int)mesh.GetIndexCount(i);total+=counts[i];}
    var mats=new Material[mesh.subMeshCount];
    for(int i=0;i<mats.Length;i++)
    {
     bool isEye=mats.Length>1&&counts[i]<total*cfg.look.eyeTriangleShare;
     if(isEye&&eye==null)
     {
      eye=Mat(cfg,cfg.prefabDir+"/Materials/M_"+s.key+"_eye.mat",reference.shader);var e=cfg.look.eyeColor;eye.SetTexture("_BaseMap",null);eye.SetColor("_BaseColor",new Color(e[0],e[1],e[2],1));
      if(eye.HasProperty("_Smoothness"))eye.SetFloat("_Smoothness",cfg.look.eyeSmoothness);if(eye.HasProperty("_Metallic"))eye.SetFloat("_Metallic",0);eye.DisableKeyword("_NORMALMAP");eye.DisableKeyword("_EMISSION");
      eye.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;EditorUtility.SetDirty(eye);AssetDatabase.SaveAssetIfDirty(eye);
     }
     mats[i]=isEye?eye:body;
    }
    k.sharedMaterials=mats;
   }
  }

  // ---------- profiles ----------

  static EnemyAttackProfileSO AttackProfile(Cfg cfg,Sp s,float range,float preferred)
  {
   string file=cfg.prefabDir+"/Data/Attack308_"+s.key+".asset";Folder(cfg.prefabDir+"/Data");var a=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(file);
   if(a==null){a=ScriptableObject.CreateInstance<EnemyAttackProfileSO>();AssetDatabase.CreateAsset(a,file);}
   if(!string.IsNullOrEmpty(s.attackFrom)){var from=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(s.attackFrom);if(from==null)throw new Refuse(s.key+": attackFrom missing "+s.attackFrom);EditorUtility.CopySerialized(from,a);a.name="Attack308_"+s.key;}
   else a.ApplyDefaults(EnemyArchetype.NeutralMelee);
   a.Telegraph=s.telegraph;a.Recovery=s.recovery;a.Damage=s.damage;a.Range=range;a.ArcDegrees=s.arc;a.CooldownRange=new Vector2(s.cooldownMin,s.cooldownMax);a.MovementSpeedHint=s.speed;a.PreferredDistanceHint=preferred;
   if(!a.TryValidate(out string error))throw new Refuse(s.key+": attack profile invalid - "+error);
   EditorUtility.SetDirty(a);AssetDatabase.SaveAssetIfDirty(a);return a;
  }

  static EnemyVitalsProfileSO VitalsProfile(Cfg cfg,Sp s)
  {
   string file=cfg.prefabDir+"/Data/Vitals308_"+s.key+".asset";Folder(cfg.prefabDir+"/Data");var v=AssetDatabase.LoadAssetAtPath<EnemyVitalsProfileSO>(file);
   if(v==null){v=ScriptableObject.CreateInstance<EnemyVitalsProfileSO>();AssetDatabase.CreateAsset(v,file);}
   var from=AssetDatabase.LoadAssetAtPath<EnemyVitalsProfileSO>(s.vitalsFrom);if(from==null)throw new Refuse(s.key+": vitalsFrom missing "+s.vitalsFrom);
   EditorUtility.CopySerialized(from,v);v.name="Vitals308_"+s.key;
   var so=new SerializedObject(v);so.FindProperty("_maxHp").floatValue=s.hp;so.FindProperty("_displayName").stringValue=s.displayName??"";so.FindProperty("_isBoss").boolValue=s.isBoss;so.FindProperty("_showLockOnBar").boolValue=!s.isBoss;
   so.FindProperty("_defence").floatValue=Mathf.Clamp01(float.IsNaN(s.defence)?0f:s.defence);   // WP-16: the sheet decides, not the copied tier
   so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(v);AssetDatabase.SaveAssetIfDirty(v);return v;
  }

  static void SetRef(Object target,string property,Object value)
  {
   var so=new SerializedObject(target);var p=so.FindProperty(property);if(p==null)throw new Exception(target.GetType().Name+"."+property+" not found");p.objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(target);
  }

  // ---------- bodies:build ----------

  static string Build(Cfg cfg,Scene scene,string only)
  {
   var sb=new StringBuilder("Creatures308 bodies:build\n");var pop=PopData(cfg);var file=ReadMeasured();int built=0;
   Folder(cfg.prefabDir);
   foreach(var s in cfg.species)
   {
    if(only.Length>0&&s.key!=only)continue;
    try
    {
     var template=FindActor(scene,s.template);if(template==null)throw new Refuse(s.key+": template actor "+s.template+" is not in the scene");
     using(var w=new Work())
     {
      var actor=w.Adopt(Object.Instantiate(template.gameObject));actor.name="PF_Creature308_"+s.key;actor.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);actor.SetActive(true);
      var enc=actor.GetComponent<PrologueEncounter>();var rig=actor.GetComponent<EnemyRigMotion298>();var ctrl=actor.GetComponent<EnemyController>();var vit=actor.GetComponent<EnemyVitals>();
      var agent=actor.GetComponent<NavMeshAgent>();var capsule=actor.GetComponent<CapsuleCollider>();
      if(enc==null||rig==null||ctrl==null||vit==null||agent==null||capsule==null)throw new Refuse(s.key+": template "+s.template+" lacks encounter / rig / controller / vitals / agent / capsule");
      var row=new Measured{key=s.key};float range,preferred,patrolSpeed=pop.patrol.walk_speed_mps;
      if(s.scaleTemplate>0)
      {
       // the existing body, bigger: the visual child scales, the root stays 1 (agent and capsule numbers scale by hand)
       float k=s.scaleTemplate;var visual=rig.Animator!=null?rig.Animator.transform:null;if(visual==null)throw new Refuse(s.key+": template rig has no animator");
       while(visual.parent!=actor.transform)visual=visual.parent;
       visual.localScale=visual.localScale*k;visual.localPosition=visual.localPosition*k;capsule.radius*=k;capsule.height*=k;capsule.center*=k;agent.radius*=k;agent.height*=k;agent.baseOffset*=k;
       rig.WalkMetresPerSecond*=k;if(rig.Run!=null)rig.RunMetresPerSecond*=k;
       var feet=actor.GetComponent<EnemyFootPlacement298>();
       if(feet!=null&&feet.Legs!=null&&feet.Legs.Length==4&&rig.Idle!=null)
       {
        Pose(rig.Animator,rig.Idle,0);
        var chains=feet.Legs.Select(l=>new EnemyFootPlacement298.LegBinding{Name=l.Name,Upper=l.Upper,Lower=l.Lower,Foot=l.Foot,HasSoleOverride=l.HasSoleOverride,SoleLocalOverride=l.SoleLocalOverride,SoleNormalLocalOverride=l.SoleNormalLocalOverride}).ToArray();
        feet.Configure(actor.transform,feet.ModelRoot,feet.BodyRoot,feet.Skins,chains,feet.GroundMask);
        if(!feet.Configured){row.note="foot placement did not re-calibrate at x"+F(k)+" ("+feet.LastStatus+") - removed on this body";rig.FootPlacement=null;Object.DestroyImmediate(feet);}
        else row.note="foot placement re-calibrated at x"+F(k);
       }
       range=s.range>0?s.range:2.8f*k;var from=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(s.attackFrom);
       preferred=Mathf.Min(range-cfg.fit.preferredBelowRange,(from!=null?from.PreferredDistanceHint:enc.PreferredDistance)*k);
       row.scale=k;row.capsuleRadius=capsule.radius;row.capsuleHeight=capsule.height;row.walkMps=rig.WalkMetresPerSecond;row.attackPeak01=rig.AttackPeak01;row.deathSeconds=rig.Death!=null?rig.Death.length:0;
       var skin=actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault();if(skin!=null&&skin.sharedMesh!=null){Bounds sbn=default;bool any=false;foreach(var q in SkinWorld(skin)){if(!any){sbn=new Bounds(q,Vector3.zero);any=true;}else sbn.Encapsulate(q);}row.length=sbn.size.z;row.width=sbn.size.x;row.height=sbn.size.y;}
      }
      else
      {
       foreach(var child in actor.transform.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
       var oldFeet=actor.GetComponent<EnemyFootPlacement298>();if(oldFeet!=null)Object.DestroyImmediate(oldFeet);
       foreach(var gone in actor.GetComponents<Component>().Where(x=>x is EnemyOrganSet||x is Oheangbu.App.Demo.EnemyElementTelegraph||x is FolkloreCullingBounds298||x is FolkloreReadability298).ToArray())Object.DestroyImmediate(gone);
       var b=BuildVisual(cfg,s,w,true,out _);Materials(cfg,s,b,w);
       var idle=Clip(s.idle);
       AnimationClip Role(string spec)=>string.IsNullOrEmpty(spec)?null:spec.StartsWith("proc:",StringComparison.Ordinal)?MakeProc(cfg,s,b,spec.Substring(5),idle):Clip(spec);
       var walk=Role(s.walk);var run=Role(s.run);var attack=Role(s.attack);var hit=Role(s.hit);var stun=Role(s.stun);var death=Role(s.death);
       if(walk==null||attack==null||hit==null||stun==null||death==null)throw new Refuse(s.key+": a role clip is missing (idle walk attack hit stun death are all needed)");
       foreach(var c in new[]{idle,walk,attack,hit,stun,death})if(c.legacy||c.length<=0)throw new Refuse(s.key+": clip "+c.name+" is legacy or empty");
       MeasureAttack(b,attack,cfg.fit,out float peak,out float reach,out string who);
       float walkV=MeasureGait(b,walk,cfg.fit,out string feetNames,out float walkDrift);float runV=0,runDrift=0;if(run!=null)runV=MeasureGait(b,run,cfg.fit,out _,out runDrift);
       Pose(b.animator,idle,0);
       float h=s.capsuleHeight>0?s.capsuleHeight:Mathf.Max(cfg.fit.heightMin,b.height*cfg.fit.heightShare);
       float r=s.capsuleRadius>0?s.capsuleRadius:Mathf.Clamp(Mathf.Max(b.width*cfg.fit.widthShare,b.length*cfg.fit.lengthShare),cfg.fit.radiusMin,h*.5f);
       capsule.direction=1;capsule.radius=r;capsule.height=h;capsule.center=Vector3.zero;agent.radius=r;agent.height=h;agent.baseOffset=h*.5f;
       b.wrapper.transform.SetParent(actor.transform,false);b.wrapper.transform.localPosition=Vector3.down*agent.baseOffset;b.wrapper.transform.localRotation=Quaternion.identity;
       b.animator.cullingMode=AnimatorCullingMode.CullUpdateTransforms;
       rig.Animator=b.animator;rig.Enemy=ctrl;rig.Vitals=vit;rig.General=null;rig.SerpentFollow=null;rig.FootPlacement=null;
       rig.Idle=idle;rig.Walk=walk;rig.Attack=attack;rig.Hit=hit;rig.Stun=stun;rig.Death=death;rig.Sweep=null;rig.Wave=null;
       rig.MoveProfiles=Array.Empty<EnemyAttackProfileSO>();rig.MoveClips=Array.Empty<AnimationClip>();rig.MovePeaks01=Array.Empty<float>();
       rig.AttackPeak01=s.attackPeak01>0?s.attackPeak01:peak;rig.WalkMetresPerSecond=s.walkMps>0?s.walkMps:Mathf.Max(.1f,walkV);
       // patrol = the walk clip a little brisk; run = from a little above patrol / a share of the run clip's own speed (data), only if the chase speed gets there
       patrolSpeed=Mathf.Clamp(rig.WalkMetresPerSecond*cfg.fit.patrolWalkFactor,cfg.fit.patrolMin,pop.patrol.walk_speed_mps);float runRate=s.runMps>0?s.runMps:runV;
       float threshold=s.runThresholdMps>0?s.runThresholdMps:s.runThresholdMps<0?0f:Mathf.Max(patrolSpeed*cfg.fit.runOverPatrol,runRate*cfg.fit.runThresholdShare);
       bool useRun=run!=null&&runRate>.1f&&threshold>0&&threshold<s.speed;
       rig.Run=useRun?run:null;rig.RunThresholdMetresPerSecond=useRun?threshold:0;rig.RunMetresPerSecond=useRun?runRate:3f;rig.RunProfile=null;rig.StrikeRead=null;rig.RelaxProfile=null;rig.LoopStun=s.loopStun;
       range=s.range>0?s.range:Mathf.Max(reach+s.rangeMargin,b.length*.5f+cfg.fit.noseReach);preferred=Mathf.Max(.3f,Mathf.Min(range-cfg.fit.preferredBelowRange,b.length*.5f+cfg.fit.preferredNoseGap));
       SetRef(ctrl,"_renderer",b.skins[0]);
       enc.DeathVisualSeconds=death.length+.1f;
       row.scale=b.scale;row.length=b.length;row.width=b.width;row.height=b.height;row.attackPeak01=rig.AttackPeak01;row.attackReach=reach;row.walkMps=rig.WalkMetresPerSecond;row.runMps=useRun?rig.RunMetresPerSecond:0;row.walkDrift=walkDrift;row.runDrift=runDrift;
       row.capsuleRadius=r;row.capsuleHeight=h;row.feet=feetNames;row.deathSeconds=death.length;
       row.note="contact bone "+who+(walkDrift>b.length*cfg.fit.driftWarnShare?"; WALK DRIFTS "+F(walkDrift)+" m per cycle":"")+(run!=null&&runDrift>b.length*cfg.fit.driftWarnShare?"; RUN DRIFTS "+F(runDrift)+" m per cycle":"")+(run!=null&&!useRun?"; run clip unused":useRun?"; runs above "+F(threshold)+" m/s":"")+"; patrol "+F(patrolSpeed)+" m/s";
      }
      var attackAsset=AttackProfile(cfg,s,range,preferred);var vitalsAsset=VitalsProfile(cfg,s);
      SetRef(ctrl,"_attackProfile",attackAsset);SetRef(vit,"_profile",vitalsAsset);
      if(!string.IsNullOrEmpty(s.audio)){var em=actor.GetComponent<EnemyAudioEmitter298>();var prof=AssetDatabase.LoadAssetAtPath<EnemyAudioProfile298>(s.audio);if(em!=null&&prof!=null)em.Profile=prof;}
      enc.Id="creature308/"+s.key;enc.Brain=null;enc.PatrolPoints=Array.Empty<Vector3>();enc.Ranged=false;enc.DetectionRange=s.detection;enc.Leash=s.leash;enc.Speed=s.speed;enc.PreferredDistance=preferred;
      enc.PatrolSpeedScale=Mathf.Clamp(patrolSpeed/Mathf.Max(.1f,s.speed),.1f,1f);enc.PatrolPauseMin=pop.patrol.pause_min_s;enc.PatrolPauseMax=pop.patrol.pause_max_s;
      row.range=range;row.preferred=preferred;
      if(!rig.IsConfigured)throw new Refuse(s.key+": the rig is not configured after wiring");
      string path=PrefabPath(cfg,s.key);PrefabUtility.SaveAsPrefabAsset(actor,path,out bool ok);if(!ok)throw new Exception(s.key+": prefab was not saved "+path);
      file.rows.RemoveAll(m=>m.key==s.key);file.rows.Add(row);built++;
      sb.Append("  ").Append(s.key).Append(": ").Append(path).Append(" | L ").Append(F(row.length)).Append(" W ").Append(F(row.width)).Append(" H ").Append(F(row.height)).Append(" scale ").Append(F(row.scale,"0.####")).Append(" capsule r ").Append(F(row.capsuleRadius)).Append(" h ").Append(F(row.capsuleHeight))
       .Append(" | peak ").Append(F(row.attackPeak01)).Append(" reach ").Append(F(row.attackReach)).Append(" range ").Append(F(range)).Append(" preferred ").Append(F(preferred)).Append(" | walk ").Append(F(row.walkMps)).Append(" run ").Append(F(row.runMps)).Append(" m/s | ").Append(row.feet).Append(" | ").Append(row.note).Append('\n');
     }
    }
    catch(Refuse r){sb.Append("  REFUSED ").Append(r.Message).Append('\n');}
   }
   file.utc=DateTime.UtcNow.ToString("O");File.WriteAllText(MeasuredPath,JsonUtility.ToJson(file,true));
   return sb.Append(built).Append(" bodies built, measurements -> ").Append(MeasuredPath).ToString();
  }

  // ---------- stills: one sheet per body ----------

  static string Capture(Camera cam,int width,int height,string file,Work w)
  {
   var rt=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var prev=RenderTexture.active;var tex=new Texture2D(width,height,TextureFormat.RGB24,false);
   try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllBytes(file,tex.EncodeToPNG());}
   finally{cam.targetTexture=null;RenderTexture.active=prev;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(tex);}
   return file;
  }

  static string Stills(Cfg cfg,string only)
  {
   var sb=new StringBuilder("Creatures308 stills\n");var st=cfg.still;string dir=Path.Combine(Harness303.RepoRoot,cfg.stillDir);
   foreach(var s in cfg.species)
   {
    if(only.Length>0&&s.key!=only)continue;
    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(cfg,s.key));if(prefab==null){sb.Append("  ").Append(s.key).Append(": no prefab (bodies:build first)\n");continue;}
    using(var w=new Work())
    {
     var first=prefab.GetComponent<EnemyRigMotion298>();var agent0=prefab.GetComponent<NavMeshAgent>();var skin0=prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).First();
     var m=ReadMeasured().rows.FirstOrDefault(r=>r.key==s.key);float L=m!=null&&m.length>0?m.length:2f,H=m!=null&&m.height>0?m.height:1f;float gap=L+.7f;
     var poses=new (string name,AnimationClip clip,float t)[]{("idle",first.Idle,0),("walk",first.Walk,first.Walk.length*.5f),("attack",first.Attack,first.Attack.length*first.AttackPeak01),("stun",first.Stun,first.Stun.length*.6f),("death",first.Death,first.Death.length)};
     for(int i=0;i<poses.Length;i++)
     {
      var go=w.Adopt(Object.Instantiate(prefab));go.transform.SetPositionAndRotation(new Vector3(i*gap,agent0.baseOffset,0),Quaternion.Euler(0,90,0));
      var rig=go.GetComponent<EnemyRigMotion298>();Pose(rig.Animator,poses[i].clip,poses[i].t);
     }
     var shader=skin0.sharedMaterial.shader;
     var ground=w.Adopt(GameObject.CreatePrimitive(PrimitiveType.Plane));ground.transform.position=new Vector3(gap*2,0,0);ground.transform.localScale=Vector3.one*20;
     var gm=w.Temp(new Material(shader));gm.SetColor("_BaseColor",new Color(.78f,.75f,.69f));ground.GetComponent<Renderer>().sharedMaterial=gm;
     var person=w.Adopt(GameObject.CreatePrimitive(PrimitiveType.Capsule));person.transform.position=new Vector3(-gap*.5f-.4f,st.reference_m*.5f,0);person.transform.localScale=new Vector3(.45f,st.reference_m*.5f,.45f);
     var pm=w.Temp(new Material(shader));pm.SetColor("_BaseColor",new Color(.75f,.2f,.15f));person.GetComponent<Renderer>().sharedMaterial=pm;
     var lightGo=w.Empty("Light");var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.shadows=LightShadows.Soft;lightGo.transform.rotation=Quaternion.Euler(42,25,0);
     var camGo=w.Empty("Camera");var cam=camGo.AddComponent<Camera>();cam.scene=w.Scene;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.9f,.88f,.83f);cam.fieldOfView=st.fov;cam.nearClipPlane=.1f;cam.farClipPlane=400;cam.enabled=false;
     float left=-gap*.5f-1f,right=(poses.Length-1)*gap+L*.5f+.6f;var target=new Vector3((left+right)*.5f,Mathf.Max(H,st.reference_m)*.45f,0);
     float hfov=2*Mathf.Atan(Mathf.Tan(st.fov*Mathf.Deg2Rad*.5f)*st.width/st.height);float dist=(right-left)*.5f/Mathf.Tan(hfov*.5f)*1.06f;
     camGo.transform.position=target+new Vector3(0,dist*.2f,-dist);camGo.transform.LookAt(target);
     string file=Capture(cam,st.width,st.height,Path.Combine(dir,s.key+".png"),w);
     sb.Append("  ").Append(s.key).Append(": ").Append(file).Append(" (left to right: 1.8 m reference, idle, walk mid, attack contact, stun, death end)\n");
    }
   }
   return sb.ToString().TrimEnd();
  }

  static string Shots(Cfg cfg,Scene scene)
  {
   var sb=new StringBuilder("Creatures308 shots\n");var st=cfg.still;string dir=Path.Combine(Harness303.RepoRoot,cfg.stillDir);bool dirty=scene.isDirty;
   var actors=Content308.CrSaved308<PrologueEncounter>(scene).Where(a=>Own(cfg,a.Id)).ToArray();
   var pick=actors.Where(a=>a.Id.StartsWith(cfg.boss_prefix,StringComparison.Ordinal)).Concat(actors.Where(a=>a.Id.EndsWith("_0_0",StringComparison.Ordinal))).ToArray();
   var camGo=EditorUtility.CreateGameObjectWithHideFlags("Creatures308_ShotCamera",HideFlags.HideAndDontSave);
   try
   {
    var cam=camGo.AddComponent<Camera>();cam.fieldOfView=38;cam.nearClipPlane=.2f;cam.farClipPlane=1500;cam.enabled=false;cam.clearFlags=CameraClearFlags.Skybox;
    foreach(var a in pick)
    {
     var agent=a.GetComponent<NavMeshAgent>();var feet=a.transform.position-Vector3.up*agent.baseOffset;float size=Mathf.Max(2f,agent.height*1.4f,agent.radius*5f);
     var dirv=Quaternion.AngleAxis(35,Vector3.up)*a.transform.forward;camGo.transform.position=feet+dirv*(size*2.6f)+Vector3.up*(size*.9f);camGo.transform.LookAt(feet+Vector3.up*agent.height*.5f);
     string file=Capture(cam,st.width,st.height,Path.Combine(dir,"place_"+a.Id.Replace('/','_')+".png"),null);sb.Append("  ").Append(a.Id).Append(" at ").Append(V(feet)).Append(" -> ").Append(file).Append('\n');
    }
   }
   finally{Object.DestroyImmediate(camGo);}
   if(scene.isDirty!=dirty)sb.Append("WARNING scene dirty flag changed\n");
   return sb.ToString().TrimEnd();
  }

  // ---------- place / revert ----------

  // scene references the prefab could not keep (Player, PlayerVitals, Soundscape ...) are read off the template actor standing in the scene
  static int Rewire(GameObject clone,GameObject template)
  {
   int n=0;
   foreach(var c in clone.GetComponentsInChildren<Component>(true))
   {
    if(c==null||c is Transform)continue;
    var twin=template.transform.Find(c.transform==clone.transform?"":PathOf(c.transform,clone.transform));if(c.transform==clone.transform)twin=template.transform;if(twin==null)continue;
    var same=twin.GetComponents(c.GetType());int index=Array.IndexOf(c.GetComponents(c.GetType()),c);if(index<0||index>=same.Length||same[index]==null)continue;
    var so=new SerializedObject(c);var st=new SerializedObject(same[index]);var p=st.GetIterator();bool changed=false;
    while(p.Next(true))
    {
     if(p.propertyType!=SerializedPropertyType.ObjectReference||p.objectReferenceValue==null)continue;
     var v=p.objectReferenceValue;var tr=v is Component cc?cc.transform:v is GameObject gg?gg.transform:null;if(tr==null||!tr.gameObject.scene.IsValid()||tr.IsChildOf(template.transform))continue;
     var q=so.FindProperty(p.propertyPath);if(q==null||q.propertyType!=SerializedPropertyType.ObjectReference||q.objectReferenceValue!=null)continue;
     q.objectReferenceValue=v;changed=true;n++;
    }
    if(changed){so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(c);}
   }
   return n;
  }

  static string Remove(Cfg cfg,Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content)
  {
   int actors=0;
   foreach(var a in (session.Actors??Array.Empty<PrologueEncounter>()).Where(a=>a!=null&&Own(cfg,a.Id)).ToArray()){Content308.CrSetWired308(session,a.GetComponent<EnemyVitals>(),false);actors++;}
   session.Actors=(session.Actors??Array.Empty<PrologueEncounter>()).Where(a=>a!=null&&!Own(cfg,a.Id)).ToArray();
   var old=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==cfg.root);if(old!=null)Object.DestroyImmediate(old);
   var keep=(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Where(e=>e!=null&&!Own(cfg,e.Id)).ToArray();int rows=(content.Encounters?.Length??0)-keep.Length;content.Encounters=keep;
   return actors+" actors, "+rows+" content rows"+(old!=null?", root":"");
  }

  static string PlaceOrRevert(Cfg cfg,Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content,bool place)
  {
   var plan=place?PlanData():null;
   string dir=Path.Combine(Stage,"Backup",DateTime.Now.ToString("yyyyMMddTHHmmss")+"_"+(place?"place":"revert"));Directory.CreateDirectory(dir);
   File.Copy(Path.GetFullPath(scene.path),Path.Combine(dir,Path.GetFileName(scene.path)),true);File.Copy(Path.GetFullPath(cfg.content),Path.Combine(dir,Path.GetFileName(cfg.content)),true);
   var sb=new StringBuilder("Creatures308 "+(place?"place":"revert")+", backup "+dir);
   sb.Append("\n  removed first: ").Append(Remove(cfg,scene,session,content));
   if(place)
   {
    var root=new GameObject(cfg.root);SceneManager.MoveGameObjectToScene(root,scene);
    var actorsHome=new GameObject("Actors").transform;actorsHome.SetParent(root.transform,false);var bossHome=new GameObject("Bosses").transform;bossHome.SetParent(root.transform,false);
    var rows=new List<WorldMacroPlaytestSO.Encounter>(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>());var added=new List<PrologueEncounter>();var left=new List<string>();var per=new Dictionary<string,int>();int rewired=0;
    PrologueEncounter Put(string id,string name,string contentId,string speciesKey,Vector3 feet,Vector3[] patrol,float yaw,bool boss,Transform home)
    {
     var s=cfg.species.FirstOrDefault(k=>k.key==speciesKey);var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(cfg,speciesKey));
     var template=s!=null?FindActor(scene,s.template):null;
     if(s==null||prefab==null||template==null){left.Add(id+" ("+(s==null?"unknown species "+speciesKey:prefab==null?"no prefab for "+speciesKey+" - bodies:build first":"template "+s.template+" not in the scene")+")");return null;}
     var agent=prefab.GetComponent<NavMeshAgent>();
     var go=Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(home,true);go.name=id.Substring(id.IndexOf('/')+1)+" ["+name+"]";
     go.transform.SetPositionAndRotation(feet+Vector3.up*agent.baseOffset,Quaternion.Euler(0,yaw,0));go.SetActive(true);
     rewired+=Rewire(go,template.gameObject);
     var actor=go.GetComponent<PrologueEncounter>();actor.Id=id;actor.Brain=null;actor.PatrolPoints=patrol;
     actor.DetectionRange=boss?cfg.place.boss_detection:s.detection;actor.Leash=boss?cfg.place.boss_leash:s.leash;actor.Speed=s.speed;
     rows.Add(new WorldMacroPlaytestSO.Encounter{Id=id,ContentId=contentId,Feet=feet,Patrol=patrol,Ranged=false,RespawnOnRest=!boss,Detection=actor.DetectionRange,Leash=actor.Leash,Speed=s.speed,Activation=cfg.place.activation});
     var rig=go.GetComponent<EnemyRigMotion298>();if(rig!=null&&rig.Animator!=null&&rig.Idle!=null)Pose(rig.Animator,rig.Idle,0);   // stands in its idle pose in Edit mode instead of the bind pose
     added.Add(actor);Content308.CrSetWired308(session,go.GetComponent<EnemyVitals>(),true);return actor;
    }
    foreach(var e in plan.enemies)
    {
     if(!NavMesh.SamplePosition(P(e.feet),out var hit,plan.nav_radius_m,NavMesh.AllAreas)){left.Add(e.id+" (no NavMesh within "+plan.nav_radius_m+" m)");continue;}
     var feet=hit.position;var p2=NavMesh.SamplePosition(P(e.patrol2),out var hit2,plan.nav_radius_m*2f,NavMesh.AllAreas)?hit2.position:feet;
     if(Put(e.id,e.name,e.contentId,e.species,feet,new[]{feet,p2},Mathf.Atan2(p2.x-feet.x,p2.z-feet.z)*Mathf.Rad2Deg,false,actorsHome)!=null)per[e.realm]=per.TryGetValue(e.realm,out int n)?n+1:1;
    }
    foreach(var b in plan.bosses)
    {
     var want=P(b.feet);bool found=NavMesh.SamplePosition(want,out var hit,plan.nav_radius_m,NavMesh.AllAreas);
     for(float ring=3f;!found&&ring<=cfg.place.boss_nav_search_m;ring+=3f)for(int k=0;k<16&&!found;k++){float a=k*Mathf.PI/8f;found=NavMesh.SamplePosition(want+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*ring,out hit,plan.nav_radius_m,NavMesh.AllAreas);}
     if(!found){left.Add(b.id+" (no NavMesh within "+cfg.place.boss_nav_search_m+" m of "+V(want)+")");continue;}
     if(Put(b.id,b.name,b.id,b.species,hit.position,new[]{hit.position},b.yaw_deg,true,bossHome)!=null)sb.Append("\n  boss ").Append(b.id).Append(" [").Append(b.name).Append("] feet ").Append(V(hit.position)).Append(" (planned ").Append(V(want)).Append(", moved ").Append(F(Vector3.Distance(hit.position,want))).Append(" m, rule ").Append(b.relaxed).Append(")");
    }
    content.Encounters=rows.ToArray();session.Actors=(session.Actors??Array.Empty<PrologueEncounter>()).Concat(added).ToArray();
    sb.Append("\n  placed ").Append(added.Count).Append(" of ").Append(plan.enemies.Length+plan.bosses.Length).Append(" (").Append(string.Join(", ",per.OrderBy(k=>k.Key).Select(k=>k.Key+" "+k.Value))).Append(" + bosses ").Append(added.Count(a=>a.Id.StartsWith(cfg.boss_prefix,StringComparison.Ordinal))).Append("), scene references refilled ").Append(rewired);
    foreach(string s in left)sb.Append("\n  LEFT OUT ").Append(s);
   }
   EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);EditorUtility.SetDirty(session);
   EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))return "FAILED the play scene was not saved";
   return sb.Append('\n').Append(Check(cfg,scene,session,content)).ToString();
  }

  static string Status(Cfg cfg,Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content)
  {
   var rows=(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Where(e=>e!=null&&Own(cfg,e.Id)).ToArray();var actors=(session.Actors??Array.Empty<PrologueEncounter>()).Where(a=>a!=null&&Own(cfg,a.Id)).ToArray();
   var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==cfg.root);
   return "Creatures308 status: content rows "+rows.Length+" (bosses "+rows.Count(r=>r.Id.StartsWith(cfg.boss_prefix,StringComparison.Ordinal))+"), session actors "+actors.Length+", root "+(root!=null?"present":"absent")
    +", prefabs "+cfg.species.Count(s=>AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(cfg,s.key))!=null)+" of "+cfg.species.Length+", plan "+(File.Exists(PlanFile)?"present":"absent")+", scene dirty "+scene.isDirty;
  }

  static string Check(Cfg cfg,Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content)
  {
   var sb=new StringBuilder("Creatures308 check\n");int pass=0,fail=0;void C(bool ok,string text){sb.Append(ok?"  [PASS] ":"  [FAIL] ").Append(text).Append('\n');if(ok)pass++;else fail++;}
   var allRows=content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>();var rows=allRows.Where(e=>e!=null&&Own(cfg,e.Id)).ToArray();
   var actors=Content308.CrSaved308<PrologueEncounter>(scene).Where(a=>Own(cfg,a.Id)).ToArray();var all=session.Actors??Array.Empty<PrologueEncounter>();
   string Names(IEnumerable<PrologueEncounter> list){var a=list.Select(x=>x.Id).ToArray();return a.Length==0?"none":string.Join(", ",a.Take(6))+(a.Length>6?" (+"+(a.Length-6)+")":"");}
   var plan=File.Exists(PlanFile)?PlanData():new Plan();
   C(rows.Length>0&&rows.Length==plan.enemies.Length+plan.bosses.Length,"every planned actor has a content row ("+rows.Length+" rows, plan "+plan.enemies.Length+" + "+plan.bosses.Length+" bosses)");
   C(rows.Select(r=>r.Id).Distinct().Count()==rows.Length&&rows.All(r=>actors.Count(a=>a.Id==r.Id)==1)&&actors.All(a=>rows.Any(r=>r.Id==a.Id)),"one actor per row and no actor without a row ("+rows.Length+" rows, "+actors.Length+" actors)");
   C(allRows.Where(r=>r!=null).Select(r=>r.Id).Distinct().Count()==allRows.Count(r=>r!=null),"no duplicate encounter id in the content asset ("+allRows.Length+" rows)");
   C(all.All(a=>a!=null)&&all.Select(a=>a.Id).Distinct().Count()==all.Length,"session.Actors has no hole and no duplicate id ("+all.Length+")");
   var unlinked=actors.Where(a=>!all.Contains(a)||!Content308.CrWired308(session,a.GetComponent<EnemyVitals>())).ToArray();C(unlinked.Length==0,"every actor is in session.Actors and wired for combat - not: "+Names(unlinked));
   var off=actors.Where(a=>{var ag=a.GetComponent<NavMeshAgent>();return ag==null||a.GetComponent<EnemyVitals>()==null||!NavMesh.SamplePosition(a.transform.position-Vector3.up*ag.baseOffset,out _,2f,NavMesh.AllAreas);}).ToArray();
   C(off.Length==0,"agent, vitals and loaded NavMesh within 2 m (the session's start rule) - off: "+Names(off));
   var floating=actors.Where(a=>{var ag=a.GetComponent<NavMeshAgent>();return ag!=null&&NavMesh.SamplePosition(a.transform.position-Vector3.up*ag.baseOffset,out var h,2f,NavMesh.AllAreas)&&Mathf.Abs(h.position.y-(a.transform.position.y-ag.baseOffset))>.15f;}).ToArray();
   C(floating.Length==0,"feet within 0.15 m of the NavMesh height - not: "+Names(floating));
   var unrigged=actors.Where(a=>{var r=a.GetComponent<EnemyRigMotion298>();return r==null||!r.IsConfigured||r.Animator==null||!r.Animator.transform.IsChildOf(a.transform)||r.Enemy!=a.GetComponent<EnemyController>()||r.Vitals!=a.GetComponent<EnemyVitals>();}).ToArray();
   C(unrigged.Length==0,"EnemyRigMotion298 configured on its own animator, controller and vitals (six non-legacy clips, peak inside 0..1) - not: "+Names(unrigged));
   var badMat=actors.Where(a=>{var rs=a.GetComponentsInChildren<Renderer>(true).Where(r=>!(r is ParticleSystemRenderer)).ToArray();return rs.Length==0||rs.Any(r=>r.sharedMaterials.Length==0||r.sharedMaterials.Any(m=>m==null||m.shader==null||!m.shader.isSupported||m.shader.name=="Hidden/InternalErrorShader"||m.shader.name=="Standard"||m.shader.name.StartsWith("Legacy Shaders",StringComparison.Ordinal)));}).ToArray();
   C(badMat.Length==0,"every renderer has materials on a supported pipeline shader (nothing pink) - not: "+Names(badMat));
   var glow=actors.Where(a=>!a.Id.StartsWith(cfg.boss_prefix+"cheolong",StringComparison.Ordinal)&&a.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r=>r.sharedMaterials.Any(m=>m!=null&&m.IsKeywordEnabled("_EMISSION")))).ToArray();
   C(glow.Length==0,"no emissive body material on the pack bodies - glowing: "+Names(glow));
   var noCol=actors.Where(a=>{var c=a.GetComponent<CapsuleCollider>();return c==null||!c.enabled||c.radius<=0||c.height<=0;}).ToArray();C(noCol.Length==0,"capsule collider present - not: "+Names(noCol));
   var noRef=actors.Where(a=>
   {
    var ctrl=a.GetComponent<EnemyController>();if(a.Player==null||ctrl==null)return true;var so=new SerializedObject(ctrl);
    return new[]{"_config","_vitals","_player","_playerVitals","_renderer","_attackProfile"}.Any(n=>{var p=so.FindProperty(n);return p==null||p.objectReferenceValue==null;})||new SerializedObject(a.GetComponent<EnemyVitals>()).FindProperty("_profile").objectReferenceValue==null;
   }).ToArray();
   C(noRef.Length==0,"scene references refilled (player, player vitals, config) and profiles set - not: "+Names(noRef));
   var badProfile=actors.Where(a=>{var p=new SerializedObject(a.GetComponent<EnemyController>()).FindProperty("_attackProfile").objectReferenceValue as EnemyAttackProfileSO;return p==null||!p.TryValidate(out _)||a.PreferredDistance>p.Range;}).ToArray();
   C(badProfile.Length==0,"attack profiles valid, preferred distance inside the range - not: "+Names(badProfile));
   var bossActors=actors.Where(a=>a.Id.StartsWith(cfg.boss_prefix,StringComparison.Ordinal)).ToArray();
   C(cfg.bosses.All(b=>bossActors.Count(a=>a.Id==b.id)==1)&&bossActors.Length==cfg.bosses.Length,"the "+cfg.bosses.Length+" field bosses are there by their exact ids ("+string.Join(", ",bossActors.Select(a=>a.Id))+")");
   var bossBad=bossActors.Where(a=>{var v=a.GetComponent<EnemyVitals>();var r=rows.FirstOrDefault(x=>x.Id==a.Id);return v==null||!v.IsBoss||string.IsNullOrEmpty(v.DisplayName)||r==null||r.RespawnOnRest||a.PatrolPoints==null||a.PatrolPoints.Length!=1;}).ToArray();
   C(bossBad.Length==0,"bosses: boss vitals with a name, RespawnOnRest false, one patrol point - not: "+Names(bossBad));
   var drops=content.BossDrops??Array.Empty<WorldMacroPlaytestSO.BossDrop308>();var noDrop=cfg.bosses.Where(b=>!drops.Any(d=>d!=null&&d.BossId==b.id&&!string.IsNullOrEmpty(d.ItemId))).Select(b=>b.id).ToArray();
   C(noDrop.Length==0,"every boss id has a BossDrops row - none for: "+(noDrop.Length==0?"none":string.Join(", ",noDrop)));
   var ordinary=rows.Where(r=>!r.Id.StartsWith(cfg.boss_prefix,StringComparison.Ordinal)).ToArray();C(ordinary.All(r=>r.RespawnOnRest),"ordinary rows respawn on rest ("+ordinary.Length+")");
   var others=allRows.Where(r=>r!=null&&!Own(cfg,r.Id)).ToArray();float Near(Vector3 p){float d=float.PositiveInfinity;foreach(var o in others){var v=o.Feet-p;v.y=0;d=Mathf.Min(d,v.magnitude);}return d;}
   float minOrdinary=ordinary.Length>0?ordinary.Min(r=>Near(r.Feet)):0,needOrdinary=cfg.place.encounter_clear_m-cfg.place.spread_max_m-plan.nav_radius_m;
   C(ordinary.Length>0&&minOrdinary>=needOrdinary,"ordinary actors keep "+F(needOrdinary)+" m from every other encounter (closest "+F(minOrdinary,"0.#")+" m)");
   var bossRows=rows.Where(r=>r.Id.StartsWith(cfg.boss_prefix,StringComparison.Ordinal)).ToArray();float minBoss=bossRows.Length>0?bossRows.Min(r=>Near(r.Feet)):0;
   C(bossRows.Length>0&&minBoss>=cfg.place.boss_encounter_clear_m-cfg.place.boss_nav_search_m,"bosses keep "+F(cfg.place.boss_encounter_clear_m-cfg.place.boss_nav_search_m)+" m from every other encounter (closest "+F(minBoss,"0.#")+" m)");
   string hf=Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","Hamlet308","layout.json");
   if(File.Exists(hf))
   {
    var houses=JsonUtility.FromJson<Houses>(File.ReadAllText(hf)).houses;float need=PopData(cfg).place.house_clear_m-cfg.place.spread_max_m-plan.nav_radius_m;float minHouse=float.PositiveInfinity;
    foreach(var r in rows)foreach(var h in houses)minHouse=Mathf.Min(minHouse,Vector2.Distance(new Vector2(r.Feet.x,r.Feet.z),new Vector2(h.x,h.z)));
    C(minHouse>=need,"keeps "+F(need)+" m from the new houses (closest "+F(minHouse,"0.#")+" m, "+houses.Length+" houses)");
   }
   // Populate308 still whole
   var popRows=allRows.Where(e=>e!=null&&e.Id!=null&&e.Id.StartsWith("field308/",StringComparison.Ordinal)).ToArray();var popActors=Content308.CrSaved308<PrologueEncounter>(scene).Where(a=>a.Id!=null&&a.Id.StartsWith("field308/",StringComparison.Ordinal)).ToArray();
   string pf=Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","Populate308","plan.json");int planned=File.Exists(pf)?JsonUtility.FromJson<PopPlan>(File.ReadAllText(pf)).enemies.Length:-1;
   C(popRows.Length==popActors.Length&&popRows.All(r=>popActors.Count(a=>a.Id==r.Id)==1)&&popActors.All(a=>all.Contains(a))&&(planned<0||popRows.Length<=planned)&&popRows.Length>0,"Populate308's field308/ actors are whole ("+popRows.Length+" rows, "+popActors.Length+" actors, its plan "+planned+")");
   return sb.Append(pass).Append(" checks, ").Append(fail).Append(" failed").ToString();
  }
 }
}
