using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 relayout fix 2, item L1 (2026-10-05): the LOOK pass of the 금표 암릉 band. Only the visible rocks (children of Amneung308/Rocks)
 // are replaced: 25 equal Massif pieces -> a jointed outcrop of several meshes, sizes, leans, with tails and talus. The 20 core
 // BoxColliders, the stone, the pads and the 국 site are NOT touched - they are in the NavMesh bake; apply and revert both prove it
 // (bit digest of Core and Stone before = after, else nothing is saved). No collider on any rock: no re-bake.
 // Data: Art/World/Compact/Rebuild/CliffBoundary308/amneung308_look.json (stage Tools/Unity/Stage308_relayout_fix2/amneung/Data,
 // derived by amneung308_look_derive.py, proven offline by Tools/Art/amneung308_look_dry.py) [TEST values].
 //   rocks-status
 //   rocks-plan:<scene>      dry: what rocks-apply would do; nothing is changed
 //   rocks-apply:<scene>     scene byte backup -> old rocks out, new rocks in -> ledger (group G10r_amneung_rocks) -> only that scene saved
 //   rocks-revert:<scene> [--force]   new rocks out, the 25 base rocks rebuilt from amneung308.json (same names, same transforms)
 // verify:<scene> (Amneung308.Verify.cs) judges V2 / V3 / V10 on the look rocks while this ledger is applied.
 // Ledger: Art/Playtest308/Pacing/ledger_amneung308_rocks_<scene>.json. The base ledger (G10_amneung) is not rewritten; base revert
 // refuses while the look rocks stand (they are not its objects) - run rocks-revert first.
 public static partial class Amneung308
 {
  static string LookDataFile=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308","amneung308_look.json");
  static string LookLedgerFile(string alias)=>Path.Combine(OutDir,"ledger_amneung308_rocks_"+alias+".json");

  [Serializable] sealed class LookMeshD{public string key="",kind="",lod0="",lod1="";}
  [Serializable] sealed class LookRockD{public string id="",role="",mesh="";public float x,y,z,ground_y;public float[] q=Array.Empty<float>(),scale=Array.Empty<float>();}
  [Serializable] sealed class LookBaseD{public string file="",sha256="",version="",cores_sha256="",stone_sha256="";public int rocks;}
  [Serializable] sealed class LookChecksD{public float v10_low_rock_m,face_h_m,trs_tol_m,rot_tol_deg,raster_cell_m,sunk_under_m;}
  [Serializable] sealed class LookD
  {
   public string version="",status="",root="",holder="",group="",material="";
   public float[] lod_screen=Array.Empty<float>();
   public LookBaseD @base=new LookBaseD();public LookMeshD[] meshes=Array.Empty<LookMeshD>();public LookRockD[] rocks=Array.Empty<LookRockD>();public LookChecksD checks=new LookChecksD();
   [NonSerialized] public string Sha="";
  }

  static LookD LoadLook()
  {
   string f=LookDataFile;if(!File.Exists(f))throw new Refuse("look data missing: "+f+" (deploy Tools/Unity/Stage308_relayout_fix2/amneung/Data/amneung308_look.json there)");
   LookD l;
   try{l=JsonUtility.FromJson<LookD>(File.ReadAllText(f).TrimStart('﻿'));}
   catch(Exception e){throw new Refuse("look data does not parse: "+f+" ("+e.Message+")");}
   if(l==null||string.IsNullOrEmpty(l.root)||string.IsNullOrEmpty(l.holder)||string.IsNullOrEmpty(l.group)||string.IsNullOrEmpty(l.material))throw new Refuse("look data: root / holder / group / material missing in "+f);
   if(l.rocks.Length==0||l.meshes.Length==0||l.lod_screen.Length!=2)throw new Refuse("look data: rocks / meshes / lod_screen incomplete in "+f);
   if(!(l.lod_screen[1]>0f&&l.lod_screen[1]<l.lod_screen[0]&&l.lod_screen[0]<1f))throw new Refuse("look data: lod_screen must be 0 < far < near < 1, not "+l.lod_screen[0]+" / "+l.lod_screen[1]);
   if(l.meshes.Select(m=>m.key).Distinct().Count()!=l.meshes.Length||l.rocks.Select(r=>r.id).Distinct().Count()!=l.rocks.Length)throw new Refuse("look data: mesh keys / rock ids are not unique");
   foreach(var r in l.rocks)
   {
    if(r.q==null||r.q.Length!=4||r.scale==null||r.scale.Length!=3||r.scale.Any(v=>v<=0))throw new Refuse("look data: rock "+r.id+" needs q [x, y, z, w] and scale [x, y, z] > 0");
    float n=Mathf.Sqrt(r.q[0]*r.q[0]+r.q[1]*r.q[1]+r.q[2]*r.q[2]+r.q[3]*r.q[3]);if(Mathf.Abs(n-1f)>1e-3f)throw new Refuse("look data: rock "+r.id+" quaternion is not unit ("+F(n,"F4")+")");
    if(!l.meshes.Any(m=>m.key==r.mesh))throw new Refuse("look data: rock "+r.id+" names the unknown mesh '"+r.mesh+"'");
   }
   var k=l.checks;if(k.trs_tol_m<=0||k.rot_tol_deg<=0||k.raster_cell_m<=0||k.v10_low_rock_m<=0||k.face_h_m<=0)throw new Refuse("look data: checks{} is incomplete (trs_tol_m, rot_tol_deg, raster_cell_m, v10_low_rock_m, face_h_m)");
   foreach(var m in l.meshes)foreach(var p in new[]{m.lod0,m.lod1})if(PostLedger308.IsProtectedPath(p)||Harness303.IsProtected(p))throw new Refuse("look data: mesh "+p+" is under a protected tree");
   l.Sha=Harness303.Sha(f);return l;
  }

  static bool LookActive(string alias){string f=LookLedgerFile(alias);if(!File.Exists(f))return false;var l=JsonUtility.FromJson<Ledger>(File.ReadAllText(f));return l!=null&&l.state=="applied";}
  static Ledger ReadLookLedger(string alias){string f=LookLedgerFile(alias);return File.Exists(f)?JsonUtility.FromJson<Ledger>(File.ReadAllText(f)):null;}
  static void WriteLookLedger(string alias,Ledger l){Directory.CreateDirectory(OutDir);File.WriteAllText(LookLedgerFile(alias),JsonUtility.ToJson(l,true),new UTF8Encoding(false));}
  static Quaternion LookRot(LookRockD r)=>Quaternion.Normalize(new Quaternion(r.q[0],r.q[1],r.q[2],r.q[3]));

  /// <summary>null = not a look command (Run goes on).</summary>
  static string LookRun(string c,bool force)
  {
   if(c=="rocks-status")return LookStatus();
   if(c.StartsWith("rocks-plan:",StringComparison.Ordinal))return LookPlan(ScenePath(c.Substring(11)));
   if(c.StartsWith("rocks-apply:",StringComparison.Ordinal))return LookApply(ScenePath(c.Substring(12)));
   if(c.StartsWith("rocks-revert:",StringComparison.Ordinal))return Adopt308.RevertNote(AdoptedLive(ReadLookLedger(PostLedger308.Short(ScenePath(c.Substring(13))))))+LookRevert(ScenePath(c.Substring(13)),force);
   return null;
  }

  static string LookStatus()
  {
   var sb=new StringBuilder("Amneung308 rocks-status (play="+EditorApplication.isPlaying+")\n");
   try{var l=LoadLook();sb.AppendLine("  look data "+LookDataFile+" sha "+Short(l.Sha)+" version "+l.version+": rocks "+l.rocks.Length+" ("+string.Join(", ",l.rocks.GroupBy(r=>r.role).Select(g=>g.Key+" "+g.Count()))+"), meshes "+l.meshes.Length+", group "+l.group+", base data "+Short(l.@base.sha256));}
   catch(Refuse r){sb.AppendLine("  look data: "+r.Message);}
   foreach(var s in PostLedger308.Scenes)
   {
    string alias=PostLedger308.Short(s);var l=ReadLookLedger(alias);var b=ReadLedger(alias);
    sb.AppendLine("  "+alias+": base ledger "+(b==null?"none":b.state+" data "+Short(b.dataSha))+"; rocks ledger "+(l==null?"none":l.state+" "+l.utc+" data "+Short(l.dataSha)+" rows "+l.rows.Count+Adopt308.Mark(AdoptedLive(l))));
   }
   return sb.ToString().TrimEnd();
  }

  // bit-exact digest of everything the look pass must not change: every transform and BoxCollider under Core and Stone
  static string FrozenDigest(Transform root)
  {
   var sb=new StringBuilder();
   void Bits(float v)=>sb.Append(BitConverter.ToInt32(BitConverter.GetBytes(v),0).ToString("x8",IC));
   void V3(Vector3 v){Bits(v.x);Bits(v.y);Bits(v.z);}
   foreach(var name in new[]{"Core","Stone"})
   {
    var holder=root.Find(name);sb.Append('|').Append(name).Append(holder==null?"-":"+");if(holder==null)continue;
    foreach(var t in holder.GetComponentsInChildren<Transform>(true))
    {
     sb.Append('/').Append(t.name).Append(t.gameObject.activeSelf?'a':'i');V3(t.localPosition);var q=t.localRotation;Bits(q.x);Bits(q.y);Bits(q.z);Bits(q.w);V3(t.localScale);
     foreach(var col in t.GetComponents<Collider>())
     {
      sb.Append(col.GetType().Name).Append(col.enabled?'e':'d').Append(col.isTrigger?'t':'s');
      if(col is BoxCollider b){V3(b.center);V3(b.size);}
      else{V3(col.bounds.center);V3(col.bounds.size);}
     }
    }
   }
   using(var sha=System.Security.Cryptography.SHA256.Create())return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())).Select(x=>x.ToString("x2",IC)));
  }

  sealed class LookSurvey
  {
   public readonly List<string> Lines=new List<string>(),Blocks=new List<string>();
   public readonly Dictionary<string,Mesh> Lod0=new Dictionary<string,Mesh>(),Lod1=new Dictionary<string,Mesh>();public Material Mat;
   public Transform Root,Holder;public List<Transform> Present=new List<Transform>();public bool PresentIsBase,PresentIsLook;
  }

  static LookSurvey SurveyLook(Scene scene,DataD d,LookD l,string alias)
  {
   var p=new LookSurvey();
   if(l.root!=d.root)p.Blocks.Add("look root '"+l.root+"' is not the base root '"+d.root+"'");
   if(!string.Equals(d.Sha,l.@base.sha256,StringComparison.OrdinalIgnoreCase))p.Blocks.Add("the deployed base data amneung308.json (sha "+Short(d.Sha)+") is not the one the look was derived from ("+Short(l.@base.sha256)+"): derive the look again");
   if(d.rocks.Length!=l.@base.rocks)p.Blocks.Add("base data has "+d.rocks.Length+" rocks, the look expects "+l.@base.rocks);
   p.Mat=AssetDatabase.LoadAssetAtPath<Material>(l.material);
   if(p.Mat==null)p.Blocks.Add("rock material missing: "+l.material);else if(Emissive(p.Mat))p.Blocks.Add("rock material "+l.material+" is emissive");
   long tris=0;
   foreach(var m in l.meshes)
   {
    var a=AssetDatabase.LoadAssetAtPath<Mesh>(m.lod0);var b=AssetDatabase.LoadAssetAtPath<Mesh>(m.lod1);
    if(a==null)p.Blocks.Add("mesh missing: "+m.lod0);else p.Lod0[m.key]=a;
    if(b==null)p.Blocks.Add("mesh missing: "+m.lod1);else p.Lod1[m.key]=b;
    if(a!=null)tris+=(long)(a.triangles.Length/3)*l.rocks.Count(r=>r.mesh==m.key);
   }
   int rootCount;var root=RootOf(scene,d.root,out rootCount);
   if(rootCount!=1){p.Blocks.Add("root "+d.root+" found "+rootCount+" time(s) (the base band must be applied first: Amneung308 apply:"+alias+")");return p;}
   p.Root=root.transform;p.Holder=root.transform.Find(l.holder);
   if(p.Holder==null){p.Blocks.Add("holder "+d.root+"/"+l.holder+" is missing");return p;}
   foreach(Transform t in p.Holder)p.Present.Add(t);
   var names=new HashSet<string>(p.Present.Select(t=>t.name));
   p.PresentIsBase=p.Present.Count==d.rocks.Length&&d.rocks.All(r=>names.Contains(r.id));
   p.PresentIsLook=p.Present.Count==l.rocks.Length&&l.rocks.All(r=>names.Contains(r.id));
   int cores=root.transform.Find("Core")!=null?root.transform.Find("Core").GetComponentsInChildren<BoxCollider>(true).Length:0;
   p.Lines.Add("holder "+d.root+"/"+l.holder+": "+p.Present.Count+" object(s) now ("+(p.PresentIsBase?"the "+d.rocks.Length+" base rocks":p.PresentIsLook?"the "+l.rocks.Length+" look rocks":"NEITHER the base set nor the look set")+"); Core "+cores+" BoxColliders (not touched), frozen digest "+Short(FrozenDigest(root.transform)));
   p.Lines.Add("look: "+l.rocks.Length+" rocks ("+string.Join(", ",l.rocks.GroupBy(r=>r.role).Select(g=>g.Key+" "+g.Count()))+"), "+l.meshes.Length+" meshes ("+string.Join(", ",l.meshes.Select(m=>m.key+" x "+l.rocks.Count(r=>r.mesh==m.key)))+"), LOD0 triangles "+tris+", no collider, material "+l.material);
   return p;
  }

  static string LookPlan(string scenePath)
  {
   PostLedger308.RequireEditable();var d=Load();var l=LoadLook();var scene=PostLedger308.Open(scenePath);string alias=PostLedger308.Short(scenePath);
   var p=SurveyLook(scene,d,l,alias);var led=ReadLookLedger(alias);var b=ReadLedger(alias);
   var sb=new StringBuilder("Amneung308 rocks-plan "+scenePath+" (nothing is changed)\n  look data sha "+Short(l.Sha)+" version "+l.version+", group "+l.group+"; base data sha "+Short(d.Sha)+"\n");
   sb.AppendLine("  base ledger: "+(b==null?"none":b.state+" data "+Short(b.dataSha))+"; rocks ledger: "+(led==null?"none":led.state+" "+led.utc+" data "+Short(led.dataSha)+(led.dataSha==l.Sha?" (= this data)":" (OTHER data: rocks-revert first)")));
   foreach(var x in p.Lines)sb.AppendLine("  "+x);
   foreach(var x in p.Blocks)sb.AppendLine("  BLOCK "+x);
   bool same=led!=null&&led.state=="applied"&&led.dataSha==l.Sha&&p.PresentIsLook;
   if(same)sb.AppendLine("  -> rocks-apply:"+alias+" would change nothing (already applied from this data)");
   else if(p.Blocks.Count>0||b==null||b.state!="applied"||!p.PresentIsBase)sb.AppendLine("  -> rocks-apply:"+alias+" would REFUSE"+(b==null||b.state!="applied"?" (the base ledger is not 'applied')":!p.PresentIsBase&&p.Blocks.Count==0?" (the holder does not hold the base rocks)":""));
   else sb.AppendLine("  -> rocks-apply:"+alias+" would remove "+d.rocks.Length+" rocks and create "+l.rocks.Length+" (x2 LOD children each); 0 collider, 0 Light, 0 text; Core / Stone untouched");
   Directory.CreateDirectory(OutDir);File.WriteAllText(Path.Combine(OutDir,"amneung308-rocks-"+alias+"-plan.txt"),sb.ToString(),new UTF8Encoding(false));
   return sb.ToString().TrimEnd();
  }

  static GameObject BuildRock(Transform holder,string id,Vector3 pos,Quaternion rot,Vector3 scale,Mesh lod0,Mesh lod1,Material mat,float[] screen)
  {
   var go=new GameObject(id);go.transform.SetParent(holder,false);
   go.transform.SetPositionAndRotation(pos,rot);go.transform.localScale=scale;
   var renderers=new Renderer[2];
   for(int lod=0;lod<2;lod++)
   {
    var child=new GameObject("LOD"+lod);child.transform.SetParent(go.transform,false);
    child.AddComponent<MeshFilter>().sharedMesh=lod==0?lod0:lod1;
    var mr=child.AddComponent<MeshRenderer>();mr.sharedMaterial=mat;mr.shadowCastingMode=lod==0?UnityEngine.Rendering.ShadowCastingMode.On:UnityEngine.Rendering.ShadowCastingMode.Off;renderers[lod]=mr;
   }
   var group=go.AddComponent<LODGroup>();group.SetLODs(new[]{new LOD(screen[0],new[]{renderers[0]}),new LOD(screen[1],new[]{renderers[1]})});group.RecalculateBounds();
   return go;
  }

  static void GuardRocks(Transform holder)
  {
   int cols=holder.GetComponentsInChildren<Collider>(true).Length,lights=holder.GetComponentsInChildren<Light>(true).Length;
   var glow=holder.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&Emissive(m)).Select(m=>m.name).Distinct().ToArray();
   var texts=holder.GetComponentsInChildren<Component>(true).Where(c=>c!=null&&TextType(c.GetType())).Select(c=>c.GetType().Name).Distinct().ToArray();
   int scripts=holder.GetComponentsInChildren<MonoBehaviour>(true).Length;
   if(cols>0||lights>0||glow.Length>0||texts.Length>0||scripts>0)throw new Refuse("the rocks would carry "+cols+" collider(s), "+lights+" Light(s), "+scripts+" script(s), emissive ["+string.Join(", ",glow)+"], text ["+string.Join(", ",texts)+"] (nothing written)");
  }

  static void Discard(string scenePath){try{EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Single);}catch(Exception){}}

  static string LookApply(string scenePath)
  {
   PostLedger308.RequireEditable();var d=Load();var l=LoadLook();var scene=PostLedger308.Open(scenePath);string alias=PostLedger308.Short(scenePath);
   var old=ReadLookLedger(alias);var p=SurveyLook(scene,d,l,alias);
   if(old!=null&&old.state=="applied")
   {
    if(old.dataSha==l.Sha&&p.PresentIsLook)return "Amneung308 rocks-apply "+scenePath+"\n  변경 없음 ("+l.rocks.Length+" look rocks already built from this data, ledger "+old.utc+"); run verify:"+alias;
    throw new Refuse("rocks ledger "+LookLedgerFile(alias)+" says applied ("+old.utc+", data "+Short(old.dataSha)+") but "+(p.PresentIsLook?"the look data changed to "+Short(l.Sha):"the holder does not hold that set")+": run rocks-revert:"+alias+" first (nothing written)");
   }
   if(p.Blocks.Count>0)throw new Refuse("plan blocks ("+p.Blocks.Count+"): "+string.Join("; ",p.Blocks)+" (nothing written)");
   var b=ReadLedger(alias);
   if(b==null||b.state!="applied")throw new Refuse("the base ledger "+LedgerFile(alias)+" is not 'applied': the band must stand before its rocks are replaced (nothing written)");
   if(b.dataSha!=d.Sha)throw new Refuse("the base ledger was applied from data "+Short(b.dataSha)+", the deployed base data is "+Short(d.Sha)+" (nothing written)");
   if(!p.PresentIsBase)throw new Refuse(d.root+"/"+l.holder+" does not hold exactly the "+d.rocks.Length+" base rocks ("+p.Present.Count+" object(s)): not touched (nothing written)");
   var ck=d.checks;
   foreach(var r in d.rocks)
   {
    var t=p.Holder.Find(r.id);
    if(Vector3.Distance(t.position,new Vector3(r.x,r.y,r.z))>ck.trs_tol_m||Vector3.Distance(t.lossyScale,new Vector3(r.scale[0],r.scale[1],r.scale[2]))>ck.trs_tol_m)throw new Refuse("base rock "+r.id+" is not at its data transform ("+Trs(t)+"): moved by hand - not replaced (nothing written)");
    if(t.GetComponentsInChildren<Transform>(true).Length!=3)throw new Refuse("base rock "+r.id+" carries objects this tool did not make (nothing written)");
   }
   string before=FrozenDigest(p.Root);
   string utc=DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",IC);
   var led=new Ledger{kind="amneung308-rocks",scene=scenePath,state="applying",utc=utc,dataSha=l.Sha,dataVersion=l.version,root=d.root+"/"+l.holder,sceneShaBefore=Harness303.Sha(Harness303.Abs(scenePath))};
   led.notes.Add("base data "+d.Sha);led.notes.Add("frozen digest (Core + Stone) "+before);
   led.sceneBackup=PostLedger308.Backup(Path.Combine(OutDir,"Backups"),utc,scenePath);
   try
   {
    foreach(var t in p.Present)led.rows.Add(new Row{group=l.group,kind="rock-removed",key=BuildingAudit308.KeyOf(t),before=Trs(t),after=""});
    foreach(var t in p.Present)Object.DestroyImmediate(t.gameObject);
    foreach(var r in l.rocks)
    {
     var go=BuildRock(p.Holder,r.id,new Vector3(r.x,r.y,r.z),LookRot(r),new Vector3(r.scale[0],r.scale[1],r.scale[2]),p.Lod0[r.mesh],p.Lod1[r.mesh],p.Mat,l.lod_screen);
     led.rows.Add(new Row{group=l.group,kind="rock",key=BuildingAudit308.KeyOf(go.transform),before="",after=Trs(go.transform)+"|"+r.mesh});
    }
    GuardRocks(p.Holder);
    string after=FrozenDigest(p.Root);
    if(after!=before)throw new Refuse("Core / Stone digest changed during the build ("+Short(before)+" -> "+Short(after)+"): nothing saved");
    WriteLookLedger(alias,led);                 // journal first
    PostLedger308.SaveScene(scene);
    led.state="applied";led.sceneShaAfter=Harness303.Sha(Harness303.Abs(scenePath));WriteLookLedger(alias,led);
   }
   catch
   {
    Discard(scenePath);                         // the scene on disk is the state before this command (or the saved new state with an 'applying' journal)
    if(File.Exists(LookLedgerFile(alias))&&ReadLookLedger(alias)?.state=="applying"){led.state="failed";WriteLookLedger(alias,led);}
    throw;
   }
   var sb=new StringBuilder("Amneung308 rocks-apply "+scenePath+"\n  removed "+led.rows.Count(r=>r.kind=="rock-removed")+" base rocks, built "+led.rows.Count(r=>r.kind=="rock")+" look rocks under "+d.root+"/"+l.holder+" (group "+l.group+")\n");
   sb.AppendLine("  Core / Stone frozen digest "+Short(before)+" before = after (20 core BoxColliders, stone, pads, site: not touched; no collider added: NO NavMesh bake)");
   sb.AppendLine("  scene backup "+led.sceneBackup+" (sha "+Short(led.sceneShaBefore)+" -> "+Short(led.sceneShaAfter)+")");
   sb.AppendLine("  ledger "+LookLedgerFile(alias));
   sb.AppendLine("  next: verify:"+alias+" (V2 / V3 / V10 are judged on the look rocks now)");
   File.WriteAllText(Path.Combine(OutDir,"amneung308-rocks-"+alias+"-apply.txt"),sb.ToString(),new UTF8Encoding(false));
   return sb.ToString().TrimEnd();
  }

  static string LookRevert(string scenePath,bool force)
  {
   PostLedger308.RequireEditable();string alias=PostLedger308.Short(scenePath);
   var led=ReadLookLedger(alias);if(led==null)throw new Refuse("no rocks ledger "+LookLedgerFile(alias)+" (nothing applied, or already reverted)");
   if(led.state!="applied"&&led.state!="applying"&&led.state!="failed")throw new Refuse("rocks ledger "+LookLedgerFile(alias)+" is in state '"+led.state+"'");
   var d=Load();
   if(!led.notes.Contains("base data "+d.Sha))throw new Refuse("the deployed base data (sha "+Short(d.Sha)+") is not the one this ledger replaced the rocks of: the 25 base rocks cannot be rebuilt from it (use the scene backup "+led.sceneBackup+")");
   var scene=PostLedger308.Open(scenePath);int rootCount;var root=RootOf(scene,d.root,out rootCount);
   if(rootCount!=1)throw new Refuse("root "+d.root+" found "+rootCount+" time(s) (nothing written)");
   string holderName=led.root.Substring(led.root.IndexOf('/')+1);var holder=root.transform.Find(holderName);
   if(holder==null)throw new Refuse("holder "+led.root+" is missing (nothing written)");
   var lod0=AssetDatabase.LoadAssetAtPath<Mesh>(d.assets.rock_lod0);var lod1=AssetDatabase.LoadAssetAtPath<Mesh>(d.assets.rock_lod1);var mat=AssetDatabase.LoadAssetAtPath<Material>(d.assets.rock_material);
   if(lod0==null||lod1==null||mat==null)throw new Refuse("base rock mesh / material missing (nothing written)");
   var known=new HashSet<string>(led.rows.Where(r=>r.kind=="rock").Select(r=>r.key));var mine=new List<Transform>();var extra=new List<string>();var baseLeft=new List<Transform>();
   foreach(Transform t in holder){if(known.Contains(BuildingAudit308.KeyOf(t)))mine.Add(t);else if(d.rocks.Any(r=>r.id==t.name))baseLeft.Add(t);else extra.Add(Harness303.PathOf(t));}
   var sb=new StringBuilder("Amneung308 rocks-revert "+scenePath+"\n");
   if(extra.Count>0&&!force)throw new Refuse(extra.Count+" object(s) under "+led.root+" are not in the rocks ledger ("+string.Join(", ",extra.Take(5))+"): move them out, or append ' --force' to leave them where they are (nothing written)");
   foreach(var e in extra)sb.AppendLine("  WARN forced: left in place: "+e);
   string before=FrozenDigest(root.transform);
   string utc=DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff",IC);string backup=PostLedger308.Backup(Path.Combine(OutDir,"Backups"),utc,scenePath);
   try
   {
    foreach(var t in mine)Object.DestroyImmediate(t.gameObject);
    int built=0;
    foreach(var r in d.rocks)
    {
     if(baseLeft.Any(t=>t.name==r.id))continue;       // an interrupted apply may have left some
     BuildRock(holder,r.id,new Vector3(r.x,r.y,r.z),Quaternion.Euler(0f,r.yaw,0f),new Vector3(r.scale[0],r.scale[1],r.scale[2]),lod0,lod1,mat,d.assets.lod_screen);built++;
    }
    if(extra.Count==0)GuardRocks(holder);
    string after=FrozenDigest(root.transform);
    if(after!=before)throw new Refuse("Core / Stone digest changed during the revert ("+Short(before)+" -> "+Short(after)+"): nothing saved");
    PostLedger308.SaveScene(scene);
    sb.AppendLine("  removed "+mine.Count+" look rocks, rebuilt "+built+" of "+d.rocks.Length+" base rocks from amneung308.json (sha "+Short(d.Sha)+"); Core / Stone digest "+Short(before)+" before = after; backup of the state before this revert "+backup);
   }
   catch{Discard(scenePath);throw;}
   led.state="reverted";led.revertedUtc=PostLedger308.Utc();WriteLookLedger(alias,led);
   string archived=LookLedgerFile(alias)+".reverted-"+Harness303.UtcStamp();File.Move(LookLedgerFile(alias),archived);
   return sb.Append("  ledger archived "+archived+" (the pre-apply scene bytes stay at "+led.sceneBackup+")").ToString();
  }

  // ---------- verify (called by Verify while the rocks ledger is applied) ----------
  sealed class LookBuilt{public LookRockD Data;public Transform T;public Mesh Lod0,Lod1;}

  // V2 / V3 of the look rocks: data transform, LODGroup with the mesh pair of the rock's key, the granite material, no collider, lowest point under the ground
  static List<LookBuilt> LookVerifyRocks(Tally k,DataD d,string alias,Transform root,Transform rocksT)
  {
   var built=new List<LookBuilt>();LookD l;
   try{l=LoadLook();}catch(Refuse r){k.Id(false,"V2 look data: "+r.Message);return built;}
   var led=ReadLookLedger(alias);
   k.I("look rocks: data sha "+Short(l.Sha)+" version "+l.version+"; rocks ledger "+(led==null?"none":led.state+" "+led.utc+" data "+Short(led.dataSha)+(led.dataSha==l.Sha?"":" (NOT this data)"))+"; base data "+(string.Equals(d.Sha,l.@base.sha256,StringComparison.OrdinalIgnoreCase)?"= the one the look was derived from":"NOT the one the look was derived from"));
   var mat=AssetDatabase.LoadAssetAtPath<Material>(l.material);var ck=l.checks;int bad=0;float worst=float.MaxValue;string worstId="";int noGround=0;
   var m0=l.meshes.ToDictionary(m=>m.key,m=>AssetDatabase.LoadAssetAtPath<Mesh>(m.lod0));var m1=l.meshes.ToDictionary(m=>m.key,m=>AssetDatabase.LoadAssetAtPath<Mesh>(m.lod1));
   foreach(var r in l.rocks)
   {
    var t=rocksT!=null?rocksT.Find(r.id):null;
    if(t==null){bad++;k.Text.AppendLine("     rock "+r.id+" missing");continue;}
    bool trs=Vector3.Distance(t.position,new Vector3(r.x,r.y,r.z))<=ck.trs_tol_m&&Quaternion.Angle(t.rotation,LookRot(r))<=ck.rot_tol_deg&&Vector3.Distance(t.localScale,new Vector3(r.scale[0],r.scale[1],r.scale[2]))<=ck.trs_tol_m;
    var group=t.GetComponent<LODGroup>();var lods=group!=null?group.GetLODs():new LOD[0];
    var f0=t.Find("LOD0")?.GetComponent<MeshFilter>();var f1=t.Find("LOD1")?.GetComponent<MeshFilter>();
    bool look=lods.Length==2&&f0!=null&&f1!=null&&m0[r.mesh]!=null&&f0.sharedMesh==m0[r.mesh]&&f1.sharedMesh==m1[r.mesh]&&mat!=null&&t.GetComponentsInChildren<MeshRenderer>(true).All(x=>x.sharedMaterial==mat);
    if(!trs||!look){bad++;k.Text.AppendLine("     rock "+r.id+": transform "+(trs?"ok":"OFF "+Trs(t))+", look "+(look?"ok":"OFF (LODs "+lods.Length+")"));continue;}
    built.Add(new LookBuilt{Data=r,T=t,Lod0=m0[r.mesh],Lod1=m1[r.mesh]});
    // the lowest point of the rock against the physical ground under it
    var verts=m1[r.mesh].vertices;var mx=t.localToWorldMatrix;Vector3 low=mx.MultiplyPoint3x4(verts[0]);
    for(int i=1;i<verts.Length;i++){var w=mx.MultiplyPoint3x4(verts[i]);if(w.y<low.y)low=w;}
    if(!Ground(low.x,low.z,root,out float gy)){noGround++;continue;}
    if(gy-low.y<worst){worst=gy-low.y;worstId=r.id;}
   }
   int extra=rocksT!=null?rocksT.childCount-built.Count:0;
   k.Id(bad==0&&built.Count==l.rocks.Length&&extra==0,"V2 look rocks "+built.Count+" of "+l.rocks.Length+" at their data transform (tol "+F(ck.trs_tol_m)+" m / "+F(ck.rot_tol_deg)+" deg), LODGroup with the mesh pair of their key ("+string.Join(", ",l.meshes.Select(m=>m.key+" x "+l.rocks.Count(r=>r.mesh==m.key)))+") and the granite material ("+bad+" off, "+extra+" other object(s) in the holder)");
   int cols=rocksT!=null?rocksT.GetComponentsInChildren<Collider>(true).Length:0;
   k.Id(cols==0,"V2 visible rocks carry no collider (found "+cols+"): nothing of the look pass is in the NavMesh bake");
   k.C(noGround==0&&worst>=ck.sunk_under_m,"V3 every look rock's lowest point is under the physical ground below it: min "+F(worst)+" m at "+worstId+" (need "+F(ck.sunk_under_m)+"; "+noGround+" without ground)");
   return built;
  }

  // V10 on the real meshes of the look rocks (LOD1 as before, and LOD0 = what is seen from near)
  static void LookVerifyFit(Tally k,DataD d,Transform root,List<(string id,float x,float z,float yaw,float len,float thick)> boxes,List<LookBuilt> built)
  {
   var ck=d.checks;
   if(built.Count==0||boxes.Count!=d.cores.Length){k.C(false,"V10 core-fit not judged (look rocks or cores missing)");return;}
   // ground under every core cell and face point once
   var cells=new List<(float x,float z,float gy,float coreTop)>();var faces=new List<(float x,float z,float gy)>();float cell=ck.raster_cell_m;
   for(int ci=0;ci<boxes.Count;ci++)
   {
    var bx=boxes[ci];float coreTop=d.cores[ci].top_y;
    for(float u=-bx.len*.5f+cell*.5f;u<bx.len*.5f;u+=cell)
     for(float v=-bx.thick*.5f+cell*.5f;v<bx.thick*.5f;v+=cell){var w=ToWorld(u,v,bx.x,bx.z,bx.yaw);if(Ground(w.x,w.y,root,out float gy))cells.Add((w.x,w.y,gy,coreTop));}
    for(int side=-1;side<=1;side+=2)
     for(float u=-bx.len*.5f;u<=bx.len*.5f+1e-3f;u+=cell){var w=ToWorld(u,side*bx.thick*.5f,bx.x,bx.z,bx.yaw);if(Ground(w.x,w.y,root,out float gy))faces.Add((w.x,w.y,gy));}
   }
   for(int lod=1;lod>=0;lod--)
   {
    var cellTop=new float[cells.Count];var faceTop=new float[faces.Count];
    for(int i=0;i<cellTop.Length;i++)cellTop[i]=float.NegativeInfinity;for(int i=0;i<faceTop.Length;i++)faceTop[i]=float.NegativeInfinity;
    foreach(var b in built)
    {
     var mesh=lod==0?b.Lod0:b.Lod1;var verts=mesh.vertices;var idx=mesh.triangles;var m=b.T.localToWorldMatrix;
     var wv=new Vector3[verts.Length];float x0=float.MaxValue,x1=float.MinValue,z0=float.MaxValue,z1=float.MinValue;
     for(int i=0;i<verts.Length;i++){var w=m.MultiplyPoint3x4(verts[i]);wv[i]=w;if(w.x<x0)x0=w.x;if(w.x>x1)x1=w.x;if(w.z<z0)z0=w.z;if(w.z>z1)z1=w.z;}
     var mineC=new List<int>();var mineF=new List<int>();
     for(int i=0;i<cells.Count;i++)if(cells[i].x>=x0&&cells[i].x<=x1&&cells[i].z>=z0&&cells[i].z<=z1)mineC.Add(i);
     for(int i=0;i<faces.Count;i++)if(faces[i].x>=x0&&faces[i].x<=x1&&faces[i].z>=z0&&faces[i].z<=z1)mineF.Add(i);
     if(mineC.Count==0&&mineF.Count==0)continue;
     for(int t=0;t+2<idx.Length;t+=3)
     {
      Vector3 a=wv[idx[t]],bb=wv[idx[t+1]],c=wv[idx[t+2]];
      float tx0=Mathf.Min(a.x,Mathf.Min(bb.x,c.x)),tx1=Mathf.Max(a.x,Mathf.Max(bb.x,c.x)),tz0=Mathf.Min(a.z,Mathf.Min(bb.z,c.z)),tz1=Mathf.Max(a.z,Mathf.Max(bb.z,c.z));
      float den=(bb.z-c.z)*(a.x-c.x)+(c.x-bb.x)*(a.z-c.z);if(Mathf.Abs(den)<1e-9f)continue;
      void Hit(float x,float z,ref float top)
      {
       if(x<tx0||x>tx1||z<tz0||z>tz1)return;
       float l1=((bb.z-c.z)*(x-c.x)+(c.x-bb.x)*(z-c.z))/den,l2=((c.z-a.z)*(x-c.x)+(a.x-c.x)*(z-c.z))/den,l3=1f-l1-l2;
       if(l1<-1e-5f||l2<-1e-5f||l3<-1e-5f)return;
       float y=l1*a.y+l2*bb.y+l3*c.y;if(y>top)top=y;
      }
      foreach(int i in mineC)Hit(cells[i].x,cells[i].z,ref cellTop[i]);
      foreach(int i in mineF)Hit(faces[i].x,faces[i].z,ref faceTop[i]);
     }
    }
    int low=0,bare=0;float slack=float.MaxValue;
    for(int i=0;i<cells.Count;i++)
    {
     float rt=cellTop[i];if(rt<cells[i].coreTop-1e-3f&&rt<cells[i].gy+ck.fit_low_rock_m)low++;
     float s=(float.IsNegativeInfinity(rt)?cells[i].gy-9f:rt)-Mathf.Min(cells[i].coreTop,cells[i].gy+ck.fit_low_rock_m);if(s<slack)slack=s;
    }
    for(int i=0;i<faces.Count;i++)if(!(faceTop[i]>=faces[i].gy+ck.fit_face_h_m))bare++;
    string name=lod==1?"V10":"V10 (LOD0)";
    k.C(low<=ck.fit_low_cells_max,name+" core-fit, look rocks: core cells where the rock is lower than the core AND lower than ground + "+F(ck.fit_low_rock_m)+" m: "+low+" of "+cells.Count+" (limit "+ck.fit_low_cells_max+"; smallest slack "+F(slack)+" m - compare the offline dry line C1m)");
    k.C(bare<=ck.fit_bare_points_max,name+" core-fit, look rocks: core face points with no rock "+F(ck.fit_face_h_m,"F1")+" m above the ground: "+bare+" of "+faces.Count+" (limit "+ck.fit_bare_points_max+")");
   }
  }
 }
}
