using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 광맥 노두 4회차 (SPEC-FIELD-FIRST-HOUR-308 A3 §8.11, D308-117) [TEST]. The third-try boulder as ONE prefab for Places308 rows
 // along the 폐광 -> 금표 주막 road. Data: Art/World/Compact/Rebuild/CliffBoundary308/ore308_trail.json (source row, roles, path, name).
 //   trail-prefab[:dry]   builds the prefab from the source row's baked meshes / materials (the same sha and material checks as
 //                        scene-apply); one child per part and LOD at identity pose, a LODGroup of two levels, no Light, no Collider.
 //                        Built in a preview scene, so nothing is left in the open scene. A second run that would build the same
 //                        prefab reports 변경 없음. Ledger Art/Playtest308/Pacing/ledger_ore308_trail.json.
 //   trail-revert         copies the prefab to Art/Playtest308/Pacing/Backups and takes it out (refused while an open scene holds a copy)
 // ore308.json and the T3 assets are only read.
 public static partial class OreOutcrop308
 {
  static string TrailFile=>Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","CliffBoundary308","ore308_trail.json");
  static string TrailLedgerFile=>Path.Combine(OutDir,"ledger_ore308_trail.json");
  [Serializable] sealed class TrailLedger{public string kind="ore308-trail",prefab="",sha="",utc="",backup="";public bool reverted;}

  static JObject TrailData()
  {
   if(!File.Exists(TrailFile))throw new Refuse("data missing: "+TrailFile);
   try{return JObject.Parse(File.ReadAllText(TrailFile));}catch(Exception e){throw new Refuse("data does not parse: "+TrailFile+" ("+e.Message+")");}
  }
  static string TrailPrefabPath(JObject tj)
  {
   string p=Str(tj,"prefab");
   if(!p.StartsWith("Assets/_Project/",StringComparison.Ordinal)||!p.EndsWith(".prefab",StringComparison.Ordinal)||p.Contains(".."))throw new Refuse("ore308_trail.json: prefab must be an Assets/_Project/...prefab path");
   if(Harness303.IsProtected(p)||PostLedger308.IsProtectedPath(p))throw new Refuse("ore308_trail.json: prefab path is in a protected tree");
   return p;
  }

  // the parts of the source row, with the scene-apply checks (pack sha, counts, box; material copies; emissive flag)
  static List<Part2> TrailParts(Cfg cfg,JObject tj,out JToken o)
  {
   string id=Str(tj,"source_row");o=Arr(cfg.J,"outcrops").FirstOrDefault(x=>Opt(x,"id")==id);
   if(o==null||!IsSplit(o))throw new Refuse("ore308.json: no split_vein row '"+id+"'");
   var roles=Arr(tj,"roles").Select(x=>x.Value<string>()).ToList();if(roles.Count==0)throw new Refuse("ore308_trail.json: roles[] is empty");
   var mats=Req(o,"materials");var refs=Packs(o);var list=new List<Part2>();var src=o;
   foreach(var p in Arr(o,"parts"))
   {
    if(!roles.Contains(Str(p,"role")))continue;
    var part=new Part2{Name=Str(p,"name"),Role=Str(p,"role"),Shadows=Flag(p,"shadows"),Emissive=Flag(p,"emissive"),Row=p};
    Mesh Load2(string lod)
    {
     string f=Str(Req(p,"lods"),lod),path=AssetOf(src,f);var m=AssetDatabase.LoadAssetAtPath<Mesh>(path);var r=refs.First(q=>q.File==f);
     if(m==null)throw new Refuse("mesh asset missing: "+path+" - the third try's bake is gone. Nothing written");
     string bad=StaleMesh(cfg,src,m,r);if(bad!=null)throw new Refuse(path+" is not the mesh of pack "+f+" ("+bad+"). Nothing written");return m;
    }
    part.L0=Load2("LOD0");part.L1=Load2("LOD1");
    part.Mats=Arr(p,"materials").Select(s=>
    {
     var row=Req(mats,Str(s,"slot"));Material m;
     if(IsBaked(row))
     {
      string mp=MatAssetOf(src,row);m=AssetDatabase.LoadAssetAtPath<Material>(mp);
      if(m==null)throw new Refuse("material asset missing: "+mp+". Nothing written");
      string badM=MatMismatch(src,row,m);if(badM!=null)throw new Refuse(mp+" is not the copy the data asks for ("+badM+"). Nothing written");
     }
     else
     {
      if(Str(row,"guid")!=Str(s,"guid"))throw new Refuse("part "+part.Name+": slot "+Str(s,"slot")+" guid is not materials."+Str(s,"slot")+".guid");
      m=Asset<Material>(Str(s,"guid"),"material "+Str(s,"slot"));
     }
     if(m.shader==null||m.shader.name!=Str(row,"shader"))throw new Refuse("material "+m.name+" shader is "+(m.shader!=null?m.shader.name:"null")+", expected "+Str(row,"shader"));return m;
    }).ToArray();
    if(part.Mats.Length!=part.L0.subMeshCount||part.L1.subMeshCount<1||part.L1.subMeshCount>part.Mats.Length)throw new Refuse("part "+part.Name+": "+part.Mats.Length+" material slot(s) for "+part.L0.subMeshCount+" / "+part.L1.subMeshCount+" submesh(es)");
    if(part.Mats.Any(Glows)!=part.Emissive||part.Mats.Any(Glows)!=part.Mats.All(Glows))throw new Refuse("part "+part.Name+": emissive flag "+part.Emissive+" does not agree with its materials");
    list.Add(part);
   }
   foreach(var r in roles)if(list.Count(q=>q.Role==r)!=1)throw new Refuse("ore308.json "+id+": exactly one part with role '"+r+"' is needed");
   return list;
  }
  static string TrailChild(string name,Part2 p,int lod)=>name+"_"+p.Role+(p.L0==p.L1?"":"_LOD"+lod);

  // null = the prefab on disk is what trail-prefab builds
  static string TrailDiffers(GameObject pf,string name,List<Part2> parts,float lod1)
  {
   if(pf.name!=name)return "root name "+pf.name;
   if(pf.transform.localPosition!=Vector3.zero||pf.transform.localRotation!=Quaternion.identity||pf.transform.localScale!=Vector3.one)return "root pose";
   if(pf.GetComponentsInChildren<Light>(true).Length>0||pf.GetComponentsInChildren<Collider>(true).Length>0)return "a Light or Collider";
   var g=pf.GetComponent<LODGroup>();if(g==null)return "no LODGroup";var lods=g.GetLODs();if(lods.Length!=2||Mathf.Abs(lods[0].screenRelativeTransitionHeight-lod1)>1e-4f)return "LOD levels";
   int kids=0;
   foreach(var p in parts)for(int lod=0;lod<(p.L0==p.L1?1:2);lod++)
   {
    kids++;var t=pf.transform.Find(TrailChild(name,p,lod));if(t==null)return "child "+TrailChild(name,p,lod)+" missing";
    var mf=t.GetComponent<MeshFilter>();var mr=t.GetComponent<MeshRenderer>();var want=lod==0?p.Mats:p.Mats.Take(p.L1.subMeshCount).ToArray();
    if(mf==null||mr==null||mf.sharedMesh!=(lod==0?p.L0:p.L1)||!mr.sharedMaterials.SequenceEqual(want))return "child "+t.name+" mesh / materials";
    if(t.localPosition!=Vector3.zero||t.localRotation!=Quaternion.identity||t.localScale!=Vector3.one)return "child "+t.name+" pose";
   }
   return pf.transform.childCount==kids?null:"child count "+pf.transform.childCount+" != "+kids;
  }

  static string TrailPrefab(bool dry)
  {
   if(EditorApplication.isCompiling)return "REFUSED scripts are compiling";
   var cfg=Load();var tj=TrailData();string path=TrailPrefabPath(tj),name=Str(tj,"name");
   if(name.IndexOf("Ore_Vein",StringComparison.Ordinal)<0)throw new Refuse("ore308_trail.json: name must hold 'Ore_Vein' (EventWash308 AllowedGlow)");
   var parts=TrailParts(cfg,tj,out var o);float lod1=Num(Req(o,"lod"),"lod1_relative_height");
   int rends=parts.Sum(p=>p.L0==p.L1?1:2);
   var sb=new StringBuilder("OreOutcrop308 trail-prefab"+(dry?" (dry)":"")+" (ore308.json "+Short(cfg.Sha)+", source "+Str(tj,"source_row")+")\n");
   sb.AppendLine("  "+path+": "+rends+" renderer(s) of "+parts.Count+" part(s) ["+string.Join(", ",parts.Select(p=>p.Role+(p.Emissive?" (glow)":"")))+"], LODGroup 2 levels (LOD1 under "+F(lod1,"F3")+"), 0 Light, 0 Collider");
   var have=AssetDatabase.LoadAssetAtPath<GameObject>(path);
   if(have!=null)
   {
    string diff=TrailDiffers(have,name,parts,lod1);
    if(diff==null)return sb.Append("  변경 없음 (the prefab on disk is this build)").ToString();
    throw new Refuse(path+" exists and differs ("+diff+") - trail-revert first. Nothing written");
   }
   if(dry)return sb.Append("  dry: nothing written").ToString();
   Directory.CreateDirectory(Path.Combine(Harness303.RepoRoot,"Oheangbu",Path.GetDirectoryName(path)));
   var stage=EditorSceneManager.NewPreviewScene();GameObject root=null;
   try
   {
    root=new GameObject(name);SceneManager.MoveGameObjectToScene(root,stage);var lod0=new List<Renderer>();var lodB=new List<Renderer>();
    foreach(var p in parts)for(int lod=0;lod<(p.L0==p.L1?1:2);lod++)
    {
     var go=new GameObject(TrailChild(name,p,lod));go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=lod==0?p.L0:p.L1;
     var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterials=lod==0?p.Mats:p.Mats.Take(p.L1.subMeshCount).ToArray();
     mr.shadowCastingMode=p.Shadows?ShadowCastingMode.On:ShadowCastingMode.Off;if(p.Emissive)mr.receiveShadows=false;
     if(p.L0==p.L1){lod0.Add(mr);lodB.Add(mr);}else(lod==0?lod0:lodB).Add(mr);
    }
    var group=root.AddComponent<LODGroup>();group.fadeMode=LODFadeMode.None;
    group.SetLODs(new[]{new LOD(lod1,lod0.ToArray()),new LOD(0f,lodB.ToArray())});group.RecalculateBounds();
    var saved=PrefabUtility.SaveAsPrefabAsset(root,path,out bool ok);
    if(!ok||saved==null)return "FAILED SaveAsPrefabAsset returned false for "+path;
   }
   finally{if(root!=null)Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(stage);}
   string abs=Path.Combine(Harness303.RepoRoot,"Oheangbu",path.Replace('/',Path.DirectorySeparatorChar));
   var led=new TrailLedger{prefab=path,sha=Harness303.Sha(abs),utc=DateTime.UtcNow.ToString("O")};
   Directory.CreateDirectory(OutDir);File.WriteAllText(TrailLedgerFile,JsonUtility.ToJson(led,true));
   string check=TrailDiffers(AssetDatabase.LoadAssetAtPath<GameObject>(path),name,parts,lod1);
   return sb.Append("  written: prefab sha "+Short(led.sha)+"; re-read "+(check==null?"= this build":"DIFFERS ("+check+")")+"; ledger "+TrailLedgerFile).ToString();
  }

  static string TrailRevert()
  {
   var tj=TrailData();string path=TrailPrefabPath(tj);var pf=AssetDatabase.LoadAssetAtPath<GameObject>(path);
   if(pf==null)return "OreOutcrop308 trail-revert: nothing at "+path;
   for(int i=0;i<SceneManager.sceneCount;i++)foreach(var g in SceneManager.GetSceneAt(i).GetRootGameObjects())
    foreach(var t in g.GetComponentsInChildren<Transform>(true))if(PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject)==pf)throw new Refuse("scene "+SceneManager.GetSceneAt(i).path+" still holds a copy ("+t.name+") - Places308 scene-revert first. Nothing taken out");
   string abs=Path.Combine(Harness303.RepoRoot,"Oheangbu",path.Replace('/',Path.DirectorySeparatorChar));
   string dir=Path.Combine(OutDir,"Backups","trail-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));Directory.CreateDirectory(dir);
   File.Copy(abs,Path.Combine(dir,Path.GetFileName(abs)));if(File.Exists(abs+".meta"))File.Copy(abs+".meta",Path.Combine(dir,Path.GetFileName(abs)+".meta"));
   if(!AssetDatabase.DeleteAsset(path))return "FAILED DeleteAsset "+path+" (backup "+dir+")";
   var led=File.Exists(TrailLedgerFile)?JsonUtility.FromJson<TrailLedger>(File.ReadAllText(TrailLedgerFile)):new TrailLedger{prefab=path};
   led.reverted=true;led.backup=dir;File.WriteAllText(TrailLedgerFile,JsonUtility.ToJson(led,true));
   return "OreOutcrop308 trail-revert: took out "+path+"; backup "+dir;
  }
 }
}
