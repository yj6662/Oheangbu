using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 1b′ 남문 전 봉인 — 고개 관문 성벽 (D308-3b, replaces the D308-3 palisade) placement + save-relocation profile
 // (SPEC-WORLD-ENCLOSURE-305 §1b′) [TEST]. Queue-safe: Run(string), refusals "REFUSED ...", never a dialog. Seal runs (segments305 v305.2)
 // stay the Enclosure305 build; this class owns only the Seal308 root, its gate wiring and WorldSealProfile308.
 //   status
 //   seal-scene:<path>    refuse (Play / not a target / dirty scene / protected / manifest checks not PASS / emissive material) -> scene backup
 //                        (Enclosure305/Out/Before/Seal308/<UTC>/) -> import Out/Seal308/Meshes (seal308_wall.py, KitMesh JSON) as mesh assets
 //                        -> rebuild root Seal308 (Wall/P308_W0, P308_W1; Gate/P308_gatehouse; Gate/P308_leaves + WorldSealGate308) from
 //                        Out/Seal308/unity.json -> WorldSealProfile308.asset from beyond308.json -> session.SealProfile -> save only the assets
 //                        made here (SaveAssetIfDirty) and the scene -> ledger Out/seal-<scene>.txt (input hashes, physics vs offline ground)
 //   unseal-scene:<path>  remove the root, clear SealProfile, save the scene (assets kept)
 //   seal-check:<path>    read-only: gate wiring + authored-open state, wall / 치 / gate tops vs the physics player-side ground (D305 >= 4.0 m,
 //                        §1b′ +5.5 m), closed leaves block walk / jump / 국+jump rays, open leaves leave the road clear, collision inside the
 //                        visible mass, renderer-less colliders only *_col, glow / lights / prompts, rule samples -> Out/seal-check-<scene>.txt
 // Values live in the offline manifest (Out/Seal308/unity.json, from Out/Seal308/wall308.json); nothing tunable is coded here. The
 // blockers, the carve and the open-leaf colliders are authored DISABLED (#297 shortcut-door practice): an edit-time NavMesh bake sees the
 // pass open (Spec: bake with the gate open) and WorldSealGate308 closes it in Play until the south-gate fact is recorded.
 public static class Seal308
 {
  static readonly string[] Scenes={"Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity","Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity","Assets/_Project/Scenes/World/W_Demo_Main.unity"};
  static readonly string[] Contents={"Assets/_Project/Art/World/Architecture296/Data/a3cb428dc79f23e45a4caf7729b52193_7a0b9698e5728bd4ea84bd5116570b9f_2b30a7723e5289c47a4fd3cde0d788cf_Content.asset",
   "Assets/_Project/Art/Characters/Folklore298/Data/WorldContent298.asset","Assets/_Project/Scenes/World/Main/WorldContent_Main.asset"};
  const string Root="Seal308",AssetDir="Assets/_Project/Art/World/Enclosure305/Seal308",ProfilePath=AssetDir+"/WorldSealProfile308.asset";
  static string E305=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","Enclosure305");
  static string OutDir=>Path.Combine(E305,"Out","Seal308");
  static string ManifestJson=>Path.Combine(OutDir,"unity.json");
  static string MeshDir=>Path.Combine(OutDir,"Meshes");
  // review fix: only the wall's own closure proof (seal308_wall_closure.py). The D308-3 palisade proof (Enclosure305/Out/beyond308.json)
  // was cut on another footprint and is no longer a fallback; without this file seal-scene refuses (a wall without the save-relocation
  // profile would strand a save that is already beyond the pass - missing data must leave the scene as it is today).
  static string BeyondJson=>Path.Combine(OutDir,"closure","beyond308.json");
  sealed class Refuse:Exception{public Refuse(string m):base(m){}}
  static string F(float v,string f="F2")=>v.ToString(f,CultureInfo.InvariantCulture);

  public static string Run(string command)
  {
   string c=(command??"").Trim();
   try
   {
    if(c=="status")return Status();
    if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Edit mode only (Play is running)";
    if(c.StartsWith("seal-scene:",StringComparison.Ordinal))return SealScene(c.Substring(11).Trim());
    if(c.StartsWith("unseal-scene:",StringComparison.Ordinal))return UnsealScene(c.Substring(13).Trim());
    if(c.StartsWith("seal-check:",StringComparison.Ordinal))return SealCheck(c.Substring(11).Trim());
   }
   catch(Refuse r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
   return "REFUSED unknown Seal308 command '"+c+"' (status | seal-scene:<path> | unseal-scene:<path> | seal-check:<path>)";
  }

  // ---------- manifest (Out/Seal308/unity.json, JsonUtility; extra keys are ignored) ----------

  [Serializable] sealed class Mat308{public string slot="",path="";}
  [Serializable] sealed class Piece308{public string name="",kind="",parent="",collider="",note="";public float[] position=Array.Empty<float>();public float yaw;public int lods;public bool isStatic;}
  [Serializable] sealed class Leaf308{public string name="",collider="";public float[] hinge=Array.Empty<float>();public int lods;public int openSign;}
  [Serializable] sealed class Obstacle308{public string name="";public float[] center=Array.Empty<float>(),size=Array.Empty<float>();}
  [Serializable] sealed class Leaves308{public string name="",parent="",blocker="",blockerMesh="";public float[] position=Array.Empty<float>();public float yaw,openDegrees,duration;public Leaf308 left,right;public Obstacle308 obstacle;}
  [Serializable] sealed class Frame308{public float[] a=Array.Empty<float>(),t=Array.Empty<float>(),n=Array.Empty<float>();public float length,centreU,walkHalf,innerProbeU,playerFromU,playerToU,along,sMin,sMax,gateS,gateS0,gateS1,passageHalf,gateDepth,floorInner,towerUMin,towerUMax;}
  [Serializable] sealed class Station308{public float s,x,z,ground,top,probeX,probeZ,playerGround;public string kind="";}
  [Serializable] sealed class Manifest308
  {
   public string version="",kind="",root="",generated="",sourceHash="",requiredFact="",meshDir="",assetDir="",checksAll="";
   public float minBlock=4f,colAbove=5.5f;public float[] lodScreenHeights=Array.Empty<float>(),probeHeights=Array.Empty<float>();
   public Mat308[] materials=Array.Empty<Mat308>();public Piece308[] pieces=Array.Empty<Piece308>();public Leaves308 leaves;public Frame308 frame;public Station308[] stations=Array.Empty<Station308>();
  }
  static Manifest308 ReadManifest(out string hash)
  {
   if(!File.Exists(ManifestJson))throw new Refuse("manifest missing "+ManifestJson+" (run python Tools/Art/seal308_wall.py)");
   hash=Harness303.Sha(ManifestJson);var m=JsonUtility.FromJson<Manifest308>(File.ReadAllText(ManifestJson));
   if(m==null||m.kind!="wall")throw new Refuse("unity.json is not the seal wall manifest (kind '"+(m!=null?m.kind:"null")+"'); the palisade v1 outputs live in Out/Seal308/_palisade_v1");
   if(m.root!=Root)throw new Refuse("manifest root '"+m.root+"' != "+Root);
   if(m.pieces==null||m.pieces.Length==0||m.leaves==null||m.leaves.left==null||m.leaves.right==null||m.leaves.obstacle==null||m.frame==null)throw new Refuse("manifest incomplete (pieces / leaves / frame)");
   if(m.leaves.left.openSign!=-1||m.leaves.right.openSign!=1)throw new Refuse("manifest leaf open signs must be left -1 / right +1 (WorldSealGate308 convention)");
   if(m.lodScreenHeights==null||m.lodScreenHeights.Length<3)throw new Refuse("manifest lodScreenHeights needs 3 values");
   return m;
  }
  static Vector3 V(float[] a)=>a!=null&&a.Length>=3?new Vector3(a[0],a[1],a[2]):throw new Refuse("bad vector in the manifest");

  // ---------- meshes / materials ----------

  [Serializable] sealed class KitSub308{public string m="";public int[] t=Array.Empty<int>();}
  [Serializable] sealed class KitMesh308{public string name="";public float[] v=Array.Empty<float>(),n=Array.Empty<float>(),uv=Array.Empty<float>();public KitSub308[] sub=Array.Empty<KitSub308>();}
  static (Mesh mesh,string[] slots) LoadMesh(string file,string assetName,bool collision)
  {
   string path=Path.Combine(MeshDir,file);if(!File.Exists(path))throw new Refuse("mesh missing "+path);
   var d=JsonUtility.FromJson<KitMesh308>(File.ReadAllText(path));int count=(d.v??Array.Empty<float>()).Length/3;if(count<3)throw new Refuse("empty mesh "+file);
   var v=new Vector3[count];var n=new Vector3[count];var uv=new Vector2[count];
   for(int i=0;i<count;i++)
   {
    v[i]=new Vector3(d.v[3*i],d.v[3*i+1],d.v[3*i+2]);
    n[i]=d.n!=null&&d.n.Length>=3*count?new Vector3(d.n[3*i],d.n[3*i+1],d.n[3*i+2]):Vector3.up;
    uv[i]=d.uv!=null&&d.uv.Length>=2*count?new Vector2(d.uv[2*i],d.uv[2*i+1]):Vector2.zero;
   }
   var subs=(d.sub??Array.Empty<KitSub308>()).Where(s=>s.t!=null&&s.t.Length>0).ToArray();if(subs.Length==0)throw new Refuse("mesh without triangles "+file);
   var mesh=new Mesh{name=assetName,indexFormat=count>65000?IndexFormat.UInt32:IndexFormat.UInt16};
   if(collision)
   {
    // one submesh: MeshCollider uses every submesh, but a single list keeps the asset small
    mesh.SetVertices(v);mesh.subMeshCount=1;mesh.SetTriangles(subs.SelectMany(s=>s.t).ToArray(),0,false);mesh.RecalculateBounds();mesh.RecalculateNormals();
    return (mesh,new string[0]);
   }
   mesh.SetVertices(v);mesh.SetNormals(n);mesh.SetUVs(0,uv);mesh.subMeshCount=subs.Length;
   for(int i=0;i<subs.Length;i++)mesh.SetTriangles(subs[i].t,i,false);
   mesh.RecalculateBounds();mesh.RecalculateTangents();
   return (mesh,subs.Select(s=>s.m).ToArray());
  }
  static Mesh SaveMesh(Mesh m,string name)
  {
   EnsureFolder(AssetDir);string path=AssetDir+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(existing==null){AssetDatabase.CreateAsset(m,path);AssetDatabase.SaveAssetIfDirty(m);return m;}
   EditorUtility.CopySerialized(m,existing);existing.name=name;EditorUtility.SetDirty(existing);AssetDatabase.SaveAssetIfDirty(existing);Object.DestroyImmediate(m);return existing;
  }
  static void EnsureFolder(string folder)
  {
   if(AssetDatabase.IsValidFolder(folder))return;string parent=Path.GetDirectoryName(folder).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(folder));
  }
  static bool Glows(Material m)=>m!=null&&(m.IsKeywordEnabled("_EMISSION")||(m.HasProperty("_EmissionColor")&&m.GetColor("_EmissionColor").maxColorComponent>0f));
  static Dictionary<string,Material> Materials(Manifest308 man)
  {
   var d=new Dictionary<string,Material>();
   foreach(var e in man.materials??Array.Empty<Mat308>())
   {
    var m=AssetDatabase.LoadAssetAtPath<Material>(e.path);if(m==null)throw new Refuse("material missing "+e.path+" (slot "+e.slot+")");
    if(Glows(m))throw new Refuse("material "+e.path+" has emission (ART-INK: the seal wall never glows)");
    d[e.slot]=m;
   }
   return d;
  }

  // ---------- scene / ground ----------

  static Scene Open(string path)
  {
   path=(path??"").Trim().Replace('\\','/');
   if(Harness303.IsProtected(path))throw new Refuse("protected path "+path);
   if(!Scenes.Contains(path))throw new Refuse("not a #308 seal target: "+path+" (allowed: "+string.Join(", ",Scenes)+")");
   for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);if(s.isDirty)throw new Refuse("scene "+s.path+" has unsaved changes - save or discard them first");}
   var active=SceneManager.GetActiveScene();
   return active.path==path&&SceneManager.sceneCount==1?active:EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
  }
  static WorldMacroPlaytestSession SessionOf(Scene s)
  {
   var all=s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).ToArray();
   if(all.Length!=1)throw new Refuse(s.path+" has "+all.Length+" WorldMacroPlaytestSession components (expected 1)");
   return all[0];
  }
  static readonly HashSet<Collider> skip=new HashSet<Collider>();
  static readonly List<Transform> ignore=new List<Transform>();
  static void PrepareGround(Scene s,WorldMacroPlaytestSession session)
  {
   skip.Clear();ignore.Clear();
   foreach(var a in session.Actors??Array.Empty<Oheangbu.App.Prologue.PrologueEncounter>())if(a!=null)foreach(var c in a.GetComponentsInChildren<Collider>(true))skip.Add(c);
   if(session.Walker!=null&&session.Walker.Body!=null)foreach(var c in session.Walker.Body.GetComponentsInChildren<Collider>(true))skip.Add(c);
   foreach(var g in s.GetRootGameObjects())if(g.name=="Enclosure305"||g.name==Root)ignore.Add(g.transform);
   Physics.SyncTransforms();
  }
  static bool Usable(RaycastHit h)
  {
   var c=h.collider;if(c==null||h.normal.y<=.5f||c is CharacterController||skip.Contains(c))return false;
   foreach(var r in ignore)if(r!=null&&c.transform.IsChildOf(r))return false;return true;
  }
  static bool IsTerrain(Collider c){if(c is TerrainCollider)return true;for(var t=c.transform;t!=null;t=t.parent){var n=t.name;if(n.Contains("Terrain")||n.Contains("Surface"))return true;}return false;}
  // highest walkable support within 1.5 m over the terrain (decks, rocks), else the highest walkable hit; NaN = nothing
  static float Ground(float x,float z)
  {
   float baseY=float.NegativeInfinity;var hits=Physics.RaycastAll(new Vector3(x,2000f,z),Vector3.down,4000f,~0,QueryTriggerInteraction.Ignore);
   foreach(var h in hits)if(Usable(h)&&IsTerrain(h.collider)&&h.point.y>baseY)baseY=h.point.y;
   if(float.IsNegativeInfinity(baseY))foreach(var h in hits)if(Usable(h)&&h.point.y>baseY)baseY=h.point.y;
   if(float.IsNegativeInfinity(baseY))return float.NaN;
   float top=baseY;foreach(var h in hits)if(Usable(h)&&h.point.y>top&&h.point.y<=baseY+1.5f)top=h.point.y;
   return top;
  }
  // frame helpers (world xz from s along A->B and u towards 상경 가도)
  static Vector2 FA(Frame308 f)=>new Vector2(f.a[0],f.a[f.a.Length>=3?2:1]);
  static Vector2 FT(Frame308 f)=>new Vector2(f.t[0],f.t[f.t.Length>=3?2:1]);
  static Vector2 FN(Frame308 f)=>new Vector2(f.n[0],f.n[f.n.Length>=3?2:1]);
  static Vector2 W(Frame308 f,float s,float u)=>FA(f)+FT(f)*s+FN(f)*u;
  // highest physics ground over the player-side band [playerFromU, playerToU] and +-along at s; NaN = no ground
  static float PlayerGround(Frame308 f,float s)
  {
   float hi=float.NaN;
   for(float a=-f.along;a<=f.along+1e-3f;a+=f.along>0?f.along:1f)
    for(float u=f.playerFromU;u<=f.playerToU+1e-3f;u+=1f){var p=W(f,s+a,u);float g=Ground(p.x,p.y);if(!float.IsNaN(g)&&(float.IsNaN(hi)||g>hi))hi=g;}
   return hi;
  }

  // ---------- seal-scene ----------

  static string SealScene(string path)
  {
   var man=ReadManifest(out string manHash);
   if(man.checksAll!="PASS")throw new Refuse("offline checks in unity.json are '"+man.checksAll+"' (see Out/Seal308/wall308.txt) - fix the ledger and rerun seal308_wall.py");
   if(man.requiredFact!=WorldMacroPlaytestSession.SouthGateOpenedId)throw new Refuse("manifest requiredFact '"+man.requiredFact+"' != WorldMacroPlaytestSession.SouthGateOpenedId");
   var mats=Materials(man);
   var scene=Open(path);var session=SessionOf(scene);
   var notes=new List<string>{"manifest "+ManifestJson+" sha "+manHash.Substring(0,12)+" (generated "+man.generated+", source "+man.sourceHash+")"};
   // the profile first: a missing or bad beyond308.json refuses before the scene is touched
   string profileNote=BuildProfile(session,out var profile);
   // import every mesh before the scene changes (a missing file refuses here)
   float[] cuts=man.lodScreenHeights;
   var lodMeshes=new Dictionary<string,(Mesh mesh,string[] slots)>();var colMeshes=new Dictionary<string,Mesh>();
   void Lods(string baseName,int lods){for(int k=0;k<lods;k++){string n=baseName+"_LOD"+k;var (m,s)=LoadMesh(n+".json",n,false);foreach(var slot in s)if(!mats.ContainsKey(slot))throw new Refuse("mesh "+n+" uses slot '"+slot+"' that the manifest has no material for");lodMeshes[n]=(SaveMesh(m,n),s);}}
   void Col(string baseName){string n=baseName+"_Collision";var (m,_)=LoadMesh(n+".json",n,true);colMeshes[n]=SaveMesh(m,n);}
   foreach(var p in man.pieces){Lods(p.name,p.lods);Col(p.name);}
   foreach(var l in new[]{man.leaves.left,man.leaves.right}){Lods(l.name,l.lods);Col(l.name);}
   Col(man.leaves.blockerMesh.EndsWith("_Collision",StringComparison.Ordinal)?man.leaves.blockerMesh.Substring(0,man.leaves.blockerMesh.Length-"_Collision".Length):man.leaves.blockerMesh);
   // backup the scene file before any change
   string stamp=DateTime.UtcNow.ToString("yyyyMMddTHHmmss",CultureInfo.InvariantCulture);string before=Path.Combine(E305,"Out","Before","Seal308",stamp);Directory.CreateDirectory(before);
   File.Copy(Harness303.Abs(scene.path),Path.Combine(before,Path.GetFileName(scene.path)),true);
   int old=0;foreach(var g in scene.GetRootGameObjects().Where(g=>g.name==Root).ToArray()){old++;Object.DestroyImmediate(g);}
   if(old>0)notes.Add("removed "+old+" previous "+Root+" root(s) (palisade v1 or an earlier wall build)");
   var root=new GameObject(Root);SceneManager.MoveGameObjectToScene(root,scene);root.layer=0;
   var groups=new Dictionary<string,Transform>();
   Transform Group(string name){if(string.IsNullOrEmpty(name))return root.transform;if(groups.TryGetValue(name,out var t))return t;var g=new GameObject(name);g.layer=0;g.transform.SetParent(root.transform,false);groups[name]=g.transform;return g.transform;}
   const StaticEditorFlags staticFlags=StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic;
   // LOD renderers under `host` (LODGroup on host when lods > 1)
   void Renderers(GameObject host,string baseName,int lods,bool isStatic)
   {
    var list=new List<LOD>();
    for(int k=0;k<lods;k++)
    {
     var (mesh,slots)=lodMeshes[baseName+"_LOD"+k];
     var child=new GameObject(baseName+"_LOD"+k);child.layer=0;child.transform.SetParent(host.transform,false);
     child.AddComponent<MeshFilter>().sharedMesh=mesh;var r=child.AddComponent<MeshRenderer>();r.sharedMaterials=slots.Select(s=>mats[s]).ToArray();
     r.shadowCastingMode=ShadowCastingMode.On;r.lightProbeUsage=LightProbeUsage.BlendProbes;
     if(isStatic)GameObjectUtility.SetStaticEditorFlags(child,staticFlags);
     list.Add(new LOD(cuts[Mathf.Min(k,cuts.Length-1)],new Renderer[]{r}));
    }
    if(lods>1){var lg=host.AddComponent<LODGroup>();lg.SetLODs(list.ToArray());lg.RecalculateBounds();}
   }
   MeshCollider ColChild(Transform host,string name,string meshName,bool enabled)
   {
    var go=new GameObject(name);go.layer=0;go.transform.SetParent(host,false);
    var mc=go.AddComponent<MeshCollider>();mc.sharedMesh=colMeshes[meshName];mc.convex=false;mc.enabled=enabled;return mc;
   }
   foreach(var p in man.pieces)
   {
    var go=new GameObject(p.name);go.layer=0;go.transform.SetParent(Group(p.parent),false);go.transform.SetPositionAndRotation(V(p.position),Quaternion.Euler(0,p.yaw,0));
    Renderers(go,p.name,p.lods,p.isStatic);ColChild(go.transform,p.collider,p.name+"_Collision",true);
    if(p.isStatic)GameObjectUtility.SetStaticEditorFlags(go,staticFlags);
    // review fix: the wall walk, 치 and gate tops are unreachable; NotWalkable (area 1, the #296 bake convention) keeps the
    // PhysicsColliders bake from leaving NavMesh islands on them. The colliders still obstruct the bake.
    var nm=go.AddComponent<Unity.AI.Navigation.NavMeshModifier>();nm.overrideArea=true;nm.area=1;
   }
   // leaves: moving, not static; blockers / carve / open colliders authored disabled
   var lv=man.leaves;var leavesGo=new GameObject(lv.name);leavesGo.layer=0;leavesGo.transform.SetParent(Group(lv.parent),false);
   leavesGo.transform.SetPositionAndRotation(V(lv.position),Quaternion.Euler(0,lv.yaw,0));
   Transform Hinge(Leaf308 l,out MeshCollider open)
   {
    var h=new GameObject(l.name);h.layer=0;h.transform.SetParent(leavesGo.transform,false);h.transform.localPosition=V(l.hinge);h.transform.localRotation=Quaternion.identity;
    Renderers(h,l.name,l.lods,false);open=ColChild(h.transform,l.collider,l.name+"_Collision",false);return h.transform;
   }
   var left=Hinge(lv.left,out var openL);var right=Hinge(lv.right,out var openR);
   var blocker=ColChild(leavesGo.transform,lv.blocker,lv.blockerMesh,false);
   var obGo=new GameObject(lv.obstacle.name);obGo.layer=0;obGo.transform.SetParent(leavesGo.transform,false);
   var ob=obGo.AddComponent<NavMeshObstacle>();ob.shape=NavMeshObstacleShape.Box;ob.center=V(lv.obstacle.center);ob.size=V(lv.obstacle.size);ob.carving=true;ob.carveOnlyStationary=true;ob.enabled=false;
   var gate=leavesGo.AddComponent<WorldSealGate308>();
   gate.Session=session;gate.RequiredCompleted=WorldMacroPlaytestSession.SouthGateOpenedId;gate.LeftLeaf=left;gate.RightLeaf=right;
   gate.OpenDegrees=lv.openDegrees;gate.Duration=Mathf.Max(.1f,lv.duration);
   gate.ClosedBlockers=new Collider[]{blocker};gate.ClosedNavigation=new[]{ob};gate.OpenColliders=new Collider[]{openL,openR};
   notes.Add("gate: WorldSealGate308 on "+Root+"/"+lv.parent+"/"+lv.name+", open "+F(lv.openDegrees,"F0")+" deg in "+F(lv.duration,"F1")+" s, fact "+gate.RequiredCompleted+
             "; authored: blocker / carve / open-leaf colliders disabled (edit-time bake = open pass), leaves closed");
   notes.Add("pieces "+string.Join(", ",man.pieces.Select(p=>p.name+" @"+Harness303.V(V(p.position))))+"; meshes "+(lodMeshes.Count+colMeshes.Count)+" assets in "+AssetDir);
   // physics vs offline ground (Spec Temporary Exception: the offline field is height.bytes, the game stands on the physics ground)
   PrepareGround(scene,session);
   float worstDy=0f,worstMargin=float.MaxValue;string worstAt="";int sampled=0,noGround=0;
   foreach(var st in man.stations??Array.Empty<Station308>())
   {
    float g=Ground(st.x,st.z);if(float.IsNaN(g)){noGround++;continue;}sampled++;
    if(Mathf.Abs(g-st.ground)>Mathf.Abs(worstDy))worstDy=g-st.ground;
    if(st.kind=="gate")continue;
    float pg=PlayerGround(man.frame,st.s);if(float.IsNaN(pg))continue;
    if(st.top-pg<worstMargin){worstMargin=st.top-pg;worstAt=st.kind+" s="+F(st.s,"F1");}
   }
   notes.Add("physics vs offline ground on the centreline: "+sampled+" stations, worst dy "+F(worstDy)+" m"+(noGround>0?", no ground "+noGround:"")+
             "; top - physics player-side ground min "+(worstMargin<float.MaxValue?F(worstMargin)+" m at "+worstAt:"n/a")+" (D305 floor "+F(man.minBlock,"F1")+", rule +"+F(man.colAbove,"F1")+")");
   if(worstMargin<man.minBlock)notes.Add("WARN top - physics player-side ground < D305 floor: rerun seal-check and raise col_above_m in wall308.json");
   if(Mathf.Abs(worstDy)>1f)notes.Add("WARN the physics ground differs from height.bytes by more than 1 m: wall feet / leaf bottoms may show - regenerate from the physics surface");
   // session link
   notes.Add(profileNote);
   var sso=new SerializedObject(session);var prop=sso.FindProperty("SealProfile");
   if(prop!=null){prop.objectReferenceValue=profile;sso.ApplyModifiedPropertiesWithoutUndo();}else notes.Add("WARN session has no SealProfile field (stage the #308 session code first)");
   EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Refuse("SaveScene returned false (backup "+before+")");
   // ledger
   string sceneKey=Path.GetFileNameWithoutExtension(scene.path);var sb=new StringBuilder();
   sb.AppendLine("seal-scene "+scene.path+" "+DateTime.UtcNow.ToString("O")+" (D308-3b 관문 성벽)");
   sb.AppendLine("inputs: unity.json "+manHash+" | beyond308.json "+(File.Exists(BeyondJson)?BeyondJson+" "+Harness303.Sha(BeyondJson):"(missing)"));
   sb.AppendLine("materials "+string.Join(", ",mats.Keys.OrderBy(k=>k,StringComparer.Ordinal))+" | scene backup "+before);
   foreach(var n in notes)sb.AppendLine(n);
   var stale=Directory.Exists(Harness303.Abs(AssetDir))?Directory.GetFiles(Harness303.Abs(AssetDir),"P308_*_W_Demo*.asset").Select(Path.GetFileName).ToArray():Array.Empty<string>();
   if(stale.Length>0)sb.AppendLine("INFO palisade v1 mesh assets still in "+AssetDir+" (no longer referenced by this scene's root): "+string.Join(", ",stale));
   string ledger=Path.Combine(E305,"Out","seal-"+sceneKey+".txt");File.WriteAllText(ledger,sb.ToString());
   return sb.Append("ledger "+ledger).ToString();
  }

  // WorldSealProfile308 from beyond308.json: rings, EA underground boxes, rest ids (EaRestIds default = every rest checkpoint of the three
  // contents whose feet are not beyond). Without beyond308.json the profile is left as it is (or not created).
  static string BuildProfile(WorldMacroPlaytestSession session,out WorldSealProfileSO profile)
  {
   profile=AssetDatabase.LoadAssetAtPath<WorldSealProfileSO>(ProfilePath);string beyond=BeyondJson;
   if(!File.Exists(beyond))throw new Refuse(beyond+" missing - run python Tools/Art/seal308_wall_closure.py first (the wall is never placed without its save-relocation profile; scene untouched)");
   var d=Json.Parse(File.ReadAllText(beyond)) as Dictionary<string,object>;if(d==null)throw new Refuse("beyond308.json is not a JSON object");
   bool created=profile==null;if(created){EnsureFolder(AssetDir);profile=ScriptableObject.CreateInstance<WorldSealProfileSO>();AssetDatabase.CreateAsset(profile,ProfilePath);}
   string prior=EditorJsonUtility.ToJson(profile);
   profile.RequiredFact=WorldMacroPlaytestSession.SouthGateOpenedId;
   object ringsRaw=d.TryGetValue("beyond_rings",out var r0)?r0:d.TryGetValue("rings",out var r1)?r1:d.TryGetValue("beyond",out var r2)?r2:d.TryGetValue("BeyondRings",out var r3)?r3:null;
   var rings=new List<WorldSealProfileSO.Ring>();
   if(ringsRaw is List<object> rl)foreach(var ring in rl)
   {
    var pts=ring is Dictionary<string,object> rd?(rd.TryGetValue("points",out var rp)?rp:rd.TryGetValue("Points",out var rp2)?rp2:null):ring;
    if(pts is List<object> pl){var list=new List<Vector2>();foreach(var q in pl){if(q is List<object> xz&&xz.Count>=2)list.Add(new Vector2(D(xz[0]),D(xz[xz.Count>=3?2:1])));else if(q is Dictionary<string,object> qd)list.Add(new Vector2(D(qd["x"]),D(qd.ContainsKey("z")?qd["z"]:qd["y"])));}
     if(list.Count>=3)rings.Add(new WorldSealProfileSO.Ring{Points=list.ToArray()});}
   }
   if(rings.Count==0)throw new Refuse("beyond308.json has no rings (keys: beyond_rings | rings | beyond | BeyondRings, each [[x,z],...] or {points:[...]})");
   profile.BeyondRings=rings.ToArray();
   object volRaw=d.TryGetValue("ea_underground_volumes",out var v1)?v1:d.TryGetValue("ea_underground",out var v2)?v2:d.TryGetValue("volumes",out var v3)?v3:null;
   var vols=new List<WorldSealProfileSO.Volume>();
   if(volRaw is List<object> vl)foreach(var o in vl)if(o is Dictionary<string,object> vd)
   {
    Vector3 V3(object x)=>x is List<object> l&&l.Count>=3?new Vector3(D(l[0]),D(l[1]),D(l[2])):Vector3.zero;
    var vol=new WorldSealProfileSO.Volume{Id=vd.TryGetValue("id",out var id)?Convert.ToString(id,CultureInfo.InvariantCulture):""};
    if(vd.TryGetValue("min",out var mn)&&vd.TryGetValue("max",out var mx)){var a=V3(mn);var b=V3(mx);vol.Center=(a+b)*.5f;vol.Size=b-a;}
    else{vol.Center=V3(vd.TryGetValue("centre",out var c0)?c0:vd.TryGetValue("center",out var c1)?c1:null);vol.Size=V3(vd.TryGetValue("size",out var s)?s:null);}
    vol.Yaw=vd.TryGetValue("yaw",out var y)&&y is double yd?(float)yd:0f;vols.Add(vol);
   }
   profile.EaUndergroundVolumes=vols.ToArray();
   if(d.TryGetValue("underground_depth_m",out var ud)&&ud is double udd)profile.UndergroundDepth=(float)udd;else if(d.TryGetValue("underground_depth",out var ud2)&&ud2 is double udd2)profile.UndergroundDepth=(float)udd2;
   if(d.TryGetValue("fallback_rest_ids",out var fr)&&fr is List<object> frl&&frl.Count>0)profile.FallbackRestIds=frl.Select(x=>Convert.ToString(x,CultureInfo.InvariantCulture)).ToArray();
   if(d.TryGetValue("move_drops",out var md)&&md is bool mdb)profile.MoveDrops=mdb;
   if(d.TryGetValue("move_vehicle",out var mv)&&mv is bool mvb)profile.MoveVehicle=mvb;
   if(d.TryGetValue("ea_rest_ids",out var er)&&er is List<object> erl&&erl.Count>0)profile.EaRestIds=erl.Select(x=>Convert.ToString(x,CultureInfo.InvariantCulture)).ToArray();
   else
   {
    var ids=new List<string>();
    foreach(var path in Contents){var c=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(path);if(c==null)continue;
     foreach(var cp in c.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>())
      if(cp!=null&&cp.IsConfigured&&!ids.Contains(cp.Id)&&!WorldSealRules308.InsideRings(profile,cp.Feet)&&
         Array.Exists(c.Points??Array.Empty<PrologueContentSO.Point>(),p=>p!=null&&p.Id==cp.Id&&p.Kind==PrologueInteractionKind.Rest))ids.Add(cp.Id);}   // rests only (not the escort station)
    profile.EaRestIds=ids.ToArray();
   }
   profile.SourceHash=Harness303.Sha(beyond);
   if(EditorJsonUtility.ToJson(profile)!=prior||created){EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);}
   return "profile "+ProfilePath+(created?" created":"")+" from "+beyond+": rings "+profile.BeyondRings.Length+" ("+profile.BeyondRings.Sum(x=>x.Points.Length)+" points), EA mine boxes "+profile.EaUndergroundVolumes.Length+
          ", EA rests "+profile.EaRestIds.Length+" ["+string.Join(",",profile.EaRestIds)+"], fallback ["+string.Join(",",profile.FallbackRestIds)+"]";
  }
  static float D(object o)=>Convert.ToSingle(o,CultureInfo.InvariantCulture);

  // minimal JSON reader for beyond308.json (nested arrays): object -> Dictionary, array -> List, number -> double
  sealed class Json
  {
   readonly string s;int i;Json(string s){this.s=s;}
   public static object Parse(string text){var j=new Json(text.TrimStart('\uFEFF'));return j.Value();}
   void Ws(){while(i<s.Length&&char.IsWhiteSpace(s[i]))i++;}
   bool Word(string w){if(string.CompareOrdinal(s,i,w,0,w.Length)!=0)return false;i+=w.Length;return true;}
   object Value()
   {
    Ws();char c=s[i];
    if(c=='{'){i++;var d=new Dictionary<string,object>();Ws();if(s[i]=='}'){i++;return d;}
     while(true){Ws();var k=Str();Ws();if(s[i++]!=':')throw new FormatException("':' expected at "+i);d[k]=Value();Ws();char e=s[i++];if(e=='}')return d;if(e!=',')throw new FormatException("',' expected at "+i);}}
    if(c=='['){i++;var l=new List<object>();Ws();if(s[i]==']'){i++;return l;}
     while(true){l.Add(Value());Ws();char e=s[i++];if(e==']')return l;if(e!=',')throw new FormatException("',' expected at "+i);}}
    if(c=='"')return Str();
    if(Word("true"))return true;if(Word("false"))return false;if(Word("null"))return null;
    int st=i;while(i<s.Length&&"+-0123456789.eE".IndexOf(s[i])>=0)i++;
    if(st==i)throw new FormatException("bad JSON value at "+i);
    return double.Parse(s.Substring(st,i-st),NumberStyles.Float,CultureInfo.InvariantCulture);
   }
   string Str()
   {
    i++;var sb=new StringBuilder();
    while(s[i]!='"'){char c=s[i++];if(c!='\\'){sb.Append(c);continue;}char e=s[i++];
     switch(e){case 'n':sb.Append('\n');break;case 't':sb.Append('\t');break;case 'r':sb.Append('\r');break;case 'b':sb.Append('\b');break;case 'f':sb.Append('\f');break;
      case 'u':sb.Append((char)Convert.ToInt32(s.Substring(i,4),16));i+=4;break;default:sb.Append(e);break;}}
    i++;return sb.ToString();
   }
  }

  // ---------- unseal ----------

  static string UnsealScene(string path)
  {
   var scene=Open(path);var session=SessionOf(scene);
   var roots=scene.GetRootGameObjects().Where(g=>g.name==Root).ToArray();
   var sso=new SerializedObject(session);var prop=sso.FindProperty("SealProfile");bool linked=prop!=null&&prop.objectReferenceValue!=null;
   if(roots.Length==0&&!linked)return "nothing to unseal in "+scene.path+" (scene not saved)";
   string stamp=DateTime.UtcNow.ToString("yyyyMMddTHHmmss",CultureInfo.InvariantCulture);string before=Path.Combine(E305,"Out","Before","Seal308",stamp);Directory.CreateDirectory(before);
   File.Copy(Harness303.Abs(scene.path),Path.Combine(before,Path.GetFileName(scene.path)),true);
   foreach(var g in roots)Object.DestroyImmediate(g);
   if(linked){prop.objectReferenceValue=null;sso.ApplyModifiedPropertiesWithoutUndo();}
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   string ledger=Path.Combine(E305,"Out","seal-"+Path.GetFileNameWithoutExtension(scene.path)+".txt");
   File.AppendAllText(ledger,"\nunseal-scene "+DateTime.UtcNow.ToString("O")+": removed "+roots.Length+" root(s), SealProfile "+(linked?"cleared":"was empty")+", backup "+before+"\n");
   return "unsealed "+scene.path+": removed "+roots.Length+" "+Root+" root(s), SealProfile "+(linked?"cleared":"was empty")+"; meshes and WorldSealProfile308 kept; backup "+before;
  }

  // ---------- seal-check (nothing saved; every pose / flag changed for a probe is restored in finally) ----------

  sealed class GateProbe308:IDisposable
  {
   readonly WorldSealGate308 g;readonly bool enabled;readonly Quaternion l,r;readonly bool[] blockers,nav,open;
   public GateProbe308(WorldSealGate308 gate,bool openPose)
   {
    g=gate;enabled=g.enabled;l=g.LeftLeaf.localRotation;r=g.RightLeaf.localRotation;
    blockers=(g.ClosedBlockers??Array.Empty<Collider>()).Select(c=>c!=null&&c.enabled).ToArray();nav=(g.ClosedNavigation??Array.Empty<NavMeshObstacle>()).Select(o=>o!=null&&o.enabled).ToArray();
    open=(g.OpenColliders??Array.Empty<Collider>()).Select(c=>c!=null&&c.enabled).ToArray();
    g.enabled=false;g.SetProbePose(openPose);Physics.SyncTransforms();
   }
   public void Dispose()
   {
    g.LeftLeaf.localRotation=l;g.RightLeaf.localRotation=r;
    for(int i=0;i<blockers.Length;i++)if(g.ClosedBlockers[i]!=null)g.ClosedBlockers[i].enabled=blockers[i];
    for(int i=0;i<nav.Length;i++)if(g.ClosedNavigation[i]!=null)g.ClosedNavigation[i].enabled=nav[i];
    for(int i=0;i<open.Length;i++)if(g.OpenColliders[i]!=null)g.OpenColliders[i].enabled=open[i];
    g.ResyncAfterProbe();g.enabled=enabled;Physics.SyncTransforms();
   }
  }
  static bool HitsRoot(Vector3 from,Vector3 to,Transform root,float radius=0f)
  {
   var d=to-from;float len=d.magnitude;if(len<1e-4f)return false;d/=len;
   var hits=radius>0f?Physics.SphereCastAll(from,radius,d,len,~0,QueryTriggerInteraction.Ignore):Physics.RaycastAll(from,d,len,~0,QueryTriggerInteraction.Ignore);
   return hits.Any(h=>h.collider!=null&&h.collider.transform.IsChildOf(root));
  }
  static Bounds? RendererBounds(Transform t)
  {
   var rs=t.GetComponentsInChildren<Renderer>(true).Where(r=>r.name.EndsWith("_LOD0",StringComparison.Ordinal)).ToArray();if(rs.Length==0)return null;
   var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;
  }
  static bool Inside(Bounds inner,Bounds outer,float tol){var o=outer;o.Expand(tol*2f);return o.Contains(inner.min)&&o.Contains(inner.max);}

  static string SealCheck(string path)
  {
   var scene=Open(path);var session=SessionOf(scene);var man=ReadManifest(out string manHash);var f=man.frame;
   var sb=new StringBuilder("seal-check "+scene.path+" "+DateTime.UtcNow.ToString("O")+" (D308-3b 관문 성벽, manifest "+manHash.Substring(0,12)+")\n");int pass=0,fail=0;
   void C(bool ok,string what){sb.AppendLine((ok?"PASS ":"FAIL ")+what);if(ok)pass++;else fail++;}
   void I(string what)=>sb.AppendLine("INFO "+what);
   var rootGo=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==Root);
   C(rootGo!=null,"root "+Root+" present");
   if(rootGo!=null)
   {
    var root=rootGo.transform;PrepareGround(scene,session);
    C(rootGo.GetComponentsInChildren<CompactMountainGate>(true).Length==0,"no palisade CompactMountainGate left under "+Root+" (D308-3b replaces it)");
    var gates=rootGo.GetComponentsInChildren<WorldSealGate308>(true);C(gates.Length==1,"one WorldSealGate308 (found "+gates.Length+")");
    var g=gates.FirstOrDefault();
    if(g!=null)
    {
     int nulls=(g.Session==null?1:0)+(g.LeftLeaf==null?1:0)+(g.RightLeaf==null?1:0)+(string.IsNullOrEmpty(g.RequiredCompleted)?1:0)
              +(g.ClosedBlockers==null||g.ClosedBlockers.Length==0?1:g.ClosedBlockers.Count(c=>c==null))+(g.ClosedNavigation==null||g.ClosedNavigation.Length==0?1:g.ClosedNavigation.Count(o=>o==null))
              +(g.OpenColliders==null||g.OpenColliders.Length!=2?1:g.OpenColliders.Count(c=>c==null));
     C(nulls==0,"AC-S7 gate fields wired (nulls "+nulls+")");
     C(g.Session==session,"gate Session = the scene session");
     C(g.RequiredCompleted==WorldMacroPlaytestSession.SouthGateOpenedId,"gate RequiredCompleted = "+WorldMacroPlaytestSession.SouthGateOpenedId);
     bool authoredOpen=(g.ClosedBlockers??Array.Empty<Collider>()).All(c=>c!=null&&!c.enabled)&&(g.ClosedNavigation??Array.Empty<NavMeshObstacle>()).All(o=>o!=null&&!o.enabled)&&(g.OpenColliders??Array.Empty<Collider>()).All(c=>c!=null&&!c.enabled);
     C(authoredOpen,"AC-S8 authored state: blocker, carve and open-leaf colliders disabled (edit-time bake sees the pass open; WorldSealGate308 closes it in Play)");
     C(g.LeftLeaf!=null&&g.RightLeaf!=null&&Quaternion.Angle(g.LeftLeaf.localRotation,Quaternion.identity)<.5f&&Quaternion.Angle(g.RightLeaf.localRotation,Quaternion.identity)<.5f,"leaves saved closed (identity hinge rotation)");
    }
    // AC-S5 heights: top of the Seal308 colliders just inside the inner face vs the physics player-side ground (gate span: block top)
    var cols=rootGo.GetComponentsInChildren<Collider>(true).Where(c=>c.enabled).ToArray();
    float worst=float.MaxValue;string worstAt="";int samples=0,missing=0;
    for(float s=f.sMin+.5f;s<=f.sMax-.5f;s+=1f)
    {
     bool passage=Mathf.Abs(s-f.gateS)<f.passageHalf+.3f;
     var p=W(f,s,passage?f.centreU:f.innerProbeU);var ray=new Ray(new Vector3(p.x,2000f,p.y),Vector3.down);float top=float.NegativeInfinity;
     foreach(var c in cols)if(c.Raycast(ray,out var hit,4000f)&&hit.point.y>top)top=hit.point.y;
     if(float.IsNegativeInfinity(top)){missing++;continue;}
     float pg=PlayerGround(f,s);if(float.IsNaN(pg))continue;samples++;
     if(top-pg<worst){worst=top-pg;worstAt="s="+F(s,"F1")+(passage?" (gate block over the passage)":"");}
    }
    C(samples>0&&worst>=man.minBlock,"AC-S5 Seal308 top - physics player-side ground >= D305 floor "+F(man.minBlock,"F1")+" m (worst "+F(worst)+" m at "+worstAt+", "+samples+" samples)");
    I("§1b′ rule (+"+F(man.colAbove,"F1")+" m over the band) holds offline by construction; worst physics margin above is the in-scene figure"+(worst<man.colAbove?" - BELOW the rule: raise col_above_m / regenerate":"")+(missing>0?"; "+missing+" samples hit no collider":""));
    // AC-S5 / AC-S7 closed leaves: walk / jump / 국+jump rays from the player side through the passage hit Seal308
    var lv=man.leaves;var leavesT=g!=null?g.transform:null;float[] hts=man.probeHeights!=null&&man.probeHeights.Length>0?man.probeHeights:new[]{.3f,1f,1.6f,3.45f};
    if(g==null||!g.IsConfigured)C(false,"AC-S5 / AC-S7 leaf probes skipped: the gate is missing or not configured");
    else
    {
     int closedMiss=0,closedRays=0;var missAt=new List<string>();
     using(new GateProbe308(g,false))
     {
      foreach(float x in new[]{-f.passageHalf+.4f,-1.5f,0f,1.5f,f.passageHalf-.4f})
       foreach(float h in hts)
       {
        // heights over the floor at the leaf line (the road falls northwards, so the player-side ground is lower than this)
        var a=leavesT.TransformPoint(new Vector3(x,0,f.gateDepth*.5f+6f));var b=leavesT.TransformPoint(new Vector3(x,0,-f.gateDepth*.5f-3f));
        var m=leavesT.TransformPoint(new Vector3(x,0,f.gateDepth*.5f+.1f));float gy=Ground(m.x,m.z);if(float.IsNaN(gy))gy=f.floorInner;a.y=gy+h;b.y=gy+h;closedRays++;
        if(!HitsRoot(a,b,root)){closedMiss++;missAt.Add("x "+F(x,"F1")+" h "+F(h,"F2"));}
       }
     }
     C(closedMiss==0,"AC-S5 closed leaves: "+closedRays+" walk / jump / 국+jump rays through the passage all hit Seal308"+(closedMiss>0?" - misses "+string.Join("; ",missAt):""));
     // open: capsule-wide sweeps along the road through the passage hit nothing of Seal308 (leaves at the sides, no ghost leaf on the road)
     int openHits=0,openSweeps=0;var hitAt=new List<string>();
     using(new GateProbe308(g,true))
     {
      foreach(float x in new[]{-2.5f,0f,2.5f})
       foreach(float h in new[]{.6f,1.0f,1.4f})
        for(float z=f.gateDepth*.5f+7f;z>-f.gateDepth*.5f-2f;z-=1f)
        {
         var a=leavesT.TransformPoint(new Vector3(x,0,z));var b=leavesT.TransformPoint(new Vector3(x,0,z-1f));
         float ga=Ground(a.x,a.z),gb=Ground(b.x,b.z);if(float.IsNaN(ga)||float.IsNaN(gb))continue;a.y=ga+h;b.y=gb+h;openSweeps++;
         if(HitsRoot(a,b,root,.28f)){openHits++;hitAt.Add("x "+F(x,"F1")+" z "+F(z,"F1")+" h "+F(h,"F1"));}
        }
     }
     C(openHits==0,"AC-S7 open leaves: "+openSweeps+" capsule sweeps (r .28) along the road through the passage touch no Seal308 collider"+(openHits>0?" - hits "+string.Join("; ",hitAt.Take(8)):""));
     // AC-S6 (review fix): the leaf bottoms were cut to the offline height.bytes (- .05 m); on the physics ground they must still meet
     // the surface, closed and open, or a daylight gap shows under a leaf (and, closed, the blocker below it is an invisible strip).
     // Each leaf's own outline collider stands in for its visible mass: a horizontal ray 6 cm over the physics ground must hit it.
     int gapRays=0,gaps=0;var gapAt=new List<string>();
     foreach(bool openPose in new[]{false,true})
      using(new GateProbe308(g,openPose))
      {
       foreach(var c in g.ClosedBlockers??Array.Empty<Collider>())if(c!=null)c.enabled=false;   // restored by the probe's Dispose
       foreach(var c in g.OpenColliders??Array.Empty<Collider>())if(c!=null)c.enabled=true;
       Physics.SyncTransforms();
       foreach(var (leaf,sign) in new[]{(g.LeftLeaf,lv.left.openSign),(g.RightLeaf,lv.right.openSign)})
       {
        float reach=lv.left.hinge!=null&&lv.left.hinge.Length>=3?Mathf.Abs(lv.left.hinge[0]):f.passageHalf;   // leaf width ~ hinge |x|
        for(float r=.3f;r<=reach-.3f+1e-3f;r+=.5f)
        {
         var p=leaf.TransformPoint(new Vector3(-sign*r,0f,0f));float gy=Ground(p.x,p.z);if(float.IsNaN(gy))continue;
         var nrm=leaf.TransformDirection(Vector3.forward);nrm.y=0f;nrm.Normalize();
         var a=new Vector3(p.x,gy+.06f,p.z)+nrm*.6f;var b=new Vector3(p.x,gy+.06f,p.z)-nrm*.6f;gapRays++;
         if(!HitsRoot(a,b,leaf)){gaps++;gapAt.Add((openPose?"open ":"closed ")+leaf.name+" r "+F(r,"F1"));}
        }
       }
      }
     C(gaps==0,"AC-S6 leaf bottoms meet the physics ground closed and open ("+gapRays+" rays 6 cm over the ground hit the leaf)"+(gaps>0?" - gaps "+string.Join("; ",gapAt.Take(8))+" (regenerate the leaves from the physics ground)":""));
    }
    // AC-S6 collision inside the visible mass (renderer-less colliders only *_col; each inside its piece's LOD0 bounds)
    var bare=rootGo.GetComponentsInChildren<Collider>(true).Where(c=>c.GetComponent<Renderer>()==null&&!c.name.EndsWith("_col",StringComparison.Ordinal)).ToArray();
    C(bare.Length==0,"AC-S6 renderer-less colliders are only *_col ("+bare.Length+" others"+(bare.Length>0?": "+string.Join(", ",bare.Select(c=>c.name)):"")+")");
    var outside=new List<string>();
    foreach(var mc in rootGo.GetComponentsInChildren<MeshCollider>(true).Where(c=>c.name.EndsWith("_col",StringComparison.Ordinal)))
    {
     if(mc.sharedMesh==null){outside.Add(mc.name+" (no mesh)");continue;}
     var host=mc.transform.parent;Bounds? vis;
     if(lv!=null&&mc.name==lv.blocker&&g!=null){var bl=RendererBounds(g.LeftLeaf);var br=RendererBounds(g.RightLeaf);vis=bl.HasValue&&br.HasValue?(Bounds?)Enc(bl.Value,br.Value):null;}
     else vis=RendererBounds(host);
     var cb=TransformBounds(mc.transform,mc.sharedMesh.bounds);
     if(!vis.HasValue||!Inside(cb,vis.Value,.1f))outside.Add(mc.name);
    }
    C(outside.Count==0,"AC-S6 every *_col lies inside its visible mass (LOD0 renderer bounds, 0.1 m)"+(outside.Count>0?" - outside: "+string.Join(", ",outside):""));
    var walkableTops=man.pieces.Select(pc=>root.Find((string.IsNullOrEmpty(pc.parent)?"":pc.parent+"/")+pc.name)).Where(t=>t==null||!t.GetComponents<Unity.AI.Navigation.NavMeshModifier>().Any(m=>m.overrideArea&&m.area==1)).Count();
    C(walkableTops==0,"AC-S8 wall / 치 / gate tops NotWalkable for the bake (NavMeshModifier area 1 on each static piece; "+walkableTops+" missing)");
    C(rootGo.GetComponentsInChildren<Light>(true).Length==0,"AC-S11 no lights under "+Root);
    var glowing=rootGo.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(Glows).Select(m=>m.name).Distinct().ToArray();
    C(glowing.Length==0,"AC-S11 no emissive material ("+string.Join(", ",glowing)+")");
    var prompts=rootGo.GetComponentsInChildren<Component>(true).Where(c=>c!=null&&(c is WorldMacroContentPoint||c.GetType().Name=="Canvas"||c.GetType().Name.StartsWith("TextMeshPro",StringComparison.Ordinal))).ToArray();
    C(prompts.Length==0,"AC-S7 no interaction point / prompt / text under "+Root+" ("+prompts.Length+")");
    // distances: 국 lift sites (>= 20 m from the wall line, end 치 included), content points and the escort path (info)
    var A=W(f,f.sMin,f.centreU);var B=W(f,f.sMax,f.centreU);var ab=B-A;
    float LineDist(Vector3 p){var q=new Vector2(p.x,p.z);float t=Mathf.Clamp01(Vector2.Dot(q-A,ab)/ab.sqrMagnitude);return Vector2.Distance(q,A+ab*t);}
    var lifts=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Oheangbu.App.Demo.GukLiftSite>(true)).ToArray();
    float liftMin=lifts.Length==0?float.PositiveInfinity:lifts.Where(l=>l.Lower!=null).Select(l=>LineDist(l.Lower.position)).DefaultIfEmpty(float.PositiveInfinity).Min();
    C(liftMin>=20f,"AC-S5 nearest 국 lift site "+(float.IsPositiveInfinity(liftMin)?"none":F(liftMin,"F0")+" m")+" >= 20 m");
    var content=session.Content;
    if(content!=null)
    {
     var nearPoint=(content.Points??Array.Empty<PrologueContentSO.Point>()).Where(p=>p!=null).OrderBy(p=>LineDist(p.Position)).FirstOrDefault();
     if(nearPoint!=null)I("nearest content point "+nearPoint.Id+" "+F(LineDist(nearPoint.Position),"F0")+" m");
     float nearPath=(content.MainPath??Array.Empty<Vector3>()).Select(LineDist).DefaultIfEmpty(float.PositiveInfinity).Min();I("nearest MainPath point "+F(nearPath,"F0")+" m (Spec: ~307 m to the escort road)");
    }
    I("closed-state NavMesh path query and real CC walk / jump / 국 probes need Play (carving is runtime only): Seal308Checks / Enclosure305 check-scene / real input");
   }
   // relocation rule samples on the linked profile
   var profile=session.SealProfile;
   if(profile==null)C(false,"AC-S10 session.SealProfile linked");
   else
   {
    C(profile.IsConfigured&&profile.RequiredFact==WorldMacroPlaytestSession.SouthGateOpenedId,"AC-S10 profile configured (fact "+profile.RequiredFact+", rings "+profile.BeyondRings.Length+")");
    var content=session.Content;
    if(content!=null)
    {
     var progress=WorldMacroProgress.CreateNew(content.TerrainRevision,content.StartFeet,content.StartYaw);
     int eaBad=0;foreach(var id in profile.EaRestIds){if(WorldMacroCheckpointRules.TryResolve(content,progress,id,out var cp)&&WorldSealRules308.InsideRings(profile,cp.Feet))eaBad++;}
     C(eaBad==0,"AC-S10 no EA rest lies beyond ("+eaBad+")");
     C(profile.FallbackRestIds.Any(id=>WorldMacroCheckpointRules.TryResolve(content,progress,id,out _)),"AC-S10 a fallback rest resolves ["+string.Join(",",profile.FallbackRestIds)+"]");
     var agwi=Array.Find(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>(),e=>e!=null&&e.Id=="folklore298/agwi");
     if(agwi!=null)C(!WorldSealRules308.InsideRings(profile,agwi.Feet),"Exit Blocking (Spec 308): agwi's place "+Harness303.V(agwi.Feet)+" is inside the EA (not beyond)");
     var beyondPoints=(content.Points??Array.Empty<PrologueContentSO.Point>()).Where(p=>p!=null&&WorldSealRules308.InsideRings(profile,p.Position)).Select(p=>p.Id).ToArray();
     var beyondEnc=(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Where(e=>e!=null&&WorldSealRules308.InsideRings(profile,e.Feet)).Select(e=>e.Id).ToArray();
     I("beyond the seal: points "+beyondPoints.Length+" ["+string.Join(",",beyondPoints)+"], encounters "+beyondEnc.Length+" ["+string.Join(",",beyondEnc)+"]");
    }
   }
   string head="PASS "+pass+" / FAIL "+fail;
   string file=Path.Combine(E305,"Out","seal-check-"+Path.GetFileNameWithoutExtension(scene.path)+".txt");Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,head+"\n"+sb);
   return head+" -> "+file+"\n"+sb;
  }
  static Bounds Enc(Bounds a,Bounds b){a.Encapsulate(b);return a;}
  static Bounds TransformBounds(Transform t,Bounds local)
  {
   var c=local.center;var e=local.extents;var b=new Bounds(t.TransformPoint(c),Vector3.zero);
   for(int i=0;i<8;i++)b.Encapsulate(t.TransformPoint(c+Vector3.Scale(e,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
   return b;
  }

  // ---------- status ----------

  static string Status()
  {
   var sb=new StringBuilder("Seal308 status (D308-3b 관문 성벽, play="+EditorApplication.isPlaying+")\n");
   if(File.Exists(ManifestJson))
   {
    var m=JsonUtility.FromJson<Manifest308>(File.ReadAllText(ManifestJson));
    sb.AppendLine("  unity.json "+(m!=null?m.kind+" v"+m.version+" generated "+m.generated+" checks "+m.checksAll:"unreadable")+" sha "+Harness303.Sha(ManifestJson).Substring(0,12));
   }
   else sb.AppendLine("  unity.json MISSING (python Tools/Art/seal308_wall.py)");
   sb.AppendLine("  beyond308.json "+(File.Exists(BeyondJson)?BeyondJson+" sha "+Harness303.Sha(BeyondJson).Substring(0,12):"MISSING "+BeyondJson+" (seal-scene refuses; python Tools/Art/seal308_wall_closure.py)"));
   var p=AssetDatabase.LoadAssetAtPath<WorldSealProfileSO>(ProfilePath);sb.AppendLine("  "+ProfilePath+": "+(p==null?"absent":"rings "+p.BeyondRings.Length+", EA rests "+p.EaRestIds.Length));
   var stale=Directory.Exists(Harness303.Abs(AssetDir))?Directory.GetFiles(Harness303.Abs(AssetDir),"P308_*_W_Demo*.asset").Length:0;
   if(stale>0)sb.AppendLine("  palisade v1 mesh assets still present: "+stale+" (P308_*_<scene>.asset; unreferenced once every target scene is resealed)");
   var s=SceneManager.GetActiveScene();var root=s.GetRootGameObjects().FirstOrDefault(g=>g.name==Root);
   string kind=root==null?"no "+Root+" root":root.GetComponentInChildren<WorldSealGate308>(true)!=null?Root+" root = wall + gate":root.GetComponentInChildren<CompactMountainGate>(true)!=null?Root+" root = PALISADE v1 (reseal)":Root+" root (unknown content)";
   sb.Append("  active "+s.path+(s.isDirty?" (dirty)":"")+(Scenes.Contains(s.path)?" [target]":" [not a target]")+": "+kind);
   return sb.ToString();
  }
 }
}
