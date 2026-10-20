using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 아궁이 연기 (SPEC-FIELD-FIRST-HOUR-308 §8.13, D308-117 A4, TEST): the settlement beats of the first hour (주막 · 역참 · 마을 ·
 // 사냥꾼 주막) only show their roofs once the player is nearly there - the sheet forest hides them (field308-sight with sheet trees,
 // 2026-10-20). A short column of cooking smoke over the main roof rises above the crowns: a guide made of the place itself, ink and
 // never light (same M_BeaconSmoke308 material, emission off). No marker, no HUD.
 //   wash308-hearth:probe   read only: the largest renderers within r of each row (what stands there)
 //   wash308-hearth[:dry]   rows of field117.json hearth.rows {id, name, x, z, r}: in the loaded scenes the largest renderer whose
 //        name holds "roof" and whose centre lies within r of (x, z) is the house; one Smoke308 particle column (hearth.column numbers)
 //        sits on its ridge, under the W_Demo_Main root Hearth308 as Hearth308/<id>/Smoke308 (Look308 stills warm it by that name).
 //        Run with W_Demo_Main active and clean (realm scenes may be open); only W_Demo_Main is saved.
 //        Rows already built are left alone. Ledger kind "hearth" (created objects only).
 //   wash308-hearth:revert  deletes the recorded Hearth308/<id> objects (the material stays).
 public static partial class EventWash308
 {
  internal const string HearthRoot="Hearth308";
  static string HearthData=>PostLedger308.RepoPath("Art/World/Compact/Rebuild/FieldFirst308/field117.json");

  static Scene HearthScene(bool write)
  {
   PostLedger308.RequireEditable();
   var main=SceneManager.GetActiveScene();
   if(main.path!=PostLedger308.Main)throw new PostLedger308.Refused("active scene is "+main.path+", not "+PostLedger308.Main);
   if(write)for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);if(s.isDirty)throw new PostLedger308.Refused("scene "+s.path+" has unsaved changes - nothing written");}
   return main;
  }

  // read only: per row the largest renderers within r in every loaded scene (name, scene, footprint, top) - to see what a house is
  static string HearthProbe()
  {
   var data=JObject.Parse(File.ReadAllText(HearthData).TrimStart('﻿'))["hearth"]??throw new PostLedger308.Refused("field117.json: hearth missing");
   var all=new List<Renderer>();
   for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);if(!s.isLoaded)continue;foreach(var g in s.GetRootGameObjects())all.AddRange(g.GetComponentsInChildren<Renderer>(false).Where(r=>r.enabled&&!(r is ParticleSystemRenderer)));}
   var sb=new StringBuilder("wash308-hearth:probe | renderers "+all.Count+"\n");
   // what is built: each column's place, activity, particle count and drawn bounds after a short simulate
   var built=SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name==HearthRoot);
   if(built!=null)foreach(var ps in built.GetComponentsInChildren<ParticleSystem>(true))
   {
    ps.Simulate(30f,true,true);var pr=ps.GetComponent<ParticleSystemRenderer>();
    sb.AppendLine(" built "+PostLedger308.PathOf(ps.transform)+" at "+ps.transform.position.ToString("0.0")+" active "+ps.gameObject.activeInHierarchy+" particles "+ps.particleCount
     +" renderer "+(pr!=null&&pr.enabled)+" bounds c "+(pr!=null?pr.bounds.center.ToString("0.0"):"-")+" size "+(pr!=null?pr.bounds.size.ToString("0.0"):"-")+" layer "+LayerMask.LayerToName(ps.gameObject.layer));
   }
   foreach(var row in (data["rows"] as JArray)??new JArray())
   {
    float x=row["x"].Value<float>(),z=row["z"].Value<float>(),rad=row["r"].Value<float>();
    sb.AppendLine(" "+row["id"]+" ("+row["name"]+") at ("+x+", "+z+") r "+rad);
    foreach(var r in all.Where(r=>new Vector2(r.bounds.center.x-x,r.bounds.center.z-z).magnitude<=rad&&r.bounds.size.x*r.bounds.size.z<4000f).OrderByDescending(r=>r.bounds.size.x*r.bounds.size.z).Take(14))
     sb.AppendLine("   "+PostLedger308.PathOf(r.transform)+" ["+r.gameObject.scene.name+"] c "+r.bounds.center.ToString("0.0")+" size "+r.bounds.size.ToString("0.0")+" top "+r.bounds.max.y.ToString("0.0"));
   }
   return sb.ToString().TrimEnd();
  }

  static string Hearth(bool dry)
  {
   var main=HearthScene(!dry);
   if(!File.Exists(HearthData))throw new PostLedger308.Refused("no "+HearthData);
   var data=JObject.Parse(File.ReadAllText(HearthData).TrimStart('﻿'))["hearth"]??throw new PostLedger308.Refused("field117.json: hearth missing (python Tools/Art/FieldFirst308/field117_build.py)");
   var c=data["column"];float F(string k)=>c[k].Value<float>();
   var ink=c["ink"] as JArray;var colour=new Color(ink[0].Value<float>(),ink[1].Value<float>(),ink[2].Value<float>(),ink[3].Value<float>());
   var roofs=new List<Renderer>();
   for(int i=0;i<SceneManager.sceneCount;i++)
   {
    // the settlement houses sit in W_Demo_Main itself (geumpyo_inn, Village245, Content308/Rests), the realms hold the rest
    var s=SceneManager.GetSceneAt(i);if(!s.isLoaded)continue;
    foreach(var g in s.GetRootGameObjects())if(g.name!=HearthRoot)foreach(var r in g.GetComponentsInChildren<Renderer>(false))
     if(r.enabled&&!(r is ParticleSystemRenderer)&&r.name.IndexOf("roof",StringComparison.OrdinalIgnoreCase)>=0
        &&PostLedger308.PathOf(r.transform).IndexOf("Palanquin",StringComparison.OrdinalIgnoreCase)<0)roofs.Add(r);   // the parked 가마 has Roof_LOD* too
   }
   if(roofs.Count==0)throw new PostLedger308.Refused("no roof renderers in the open scenes");
   var sb=new StringBuilder("wash308-hearth"+(dry?" (dry)":"")+" | roofs in open scenes "+roofs.Count+" | column rise "+F("rise")+" life "+F("life")+" rate "+F("rate")+" size "+F("size0")+"–"+F("size1")+" ink "+colour+"\n");
   var root=main.GetRootGameObjects().FirstOrDefault(g=>g.name==HearthRoot);
   var plan=new List<(string id,Vector3 at,string label)>();
   foreach(var row in (data["rows"] as JArray)??new JArray())
   {
    string id=row["id"].Value<string>();float x=row["x"].Value<float>(),z=row["z"].Value<float>(),rad=row["r"].Value<float>();
    var house=roofs.Where(r=>new Vector2(r.bounds.center.x-x,r.bounds.center.z-z).magnitude<=rad)
     .OrderByDescending(r=>r.bounds.size.x*r.bounds.size.z).FirstOrDefault();
    if(house==null){sb.AppendLine("  "+id+" ("+row["name"]+"): no roof within "+rad+" m of ("+x+", "+z+") - skipped");continue;}
    var b=house.bounds;var at=new Vector3(b.center.x,b.max.y+.2f,b.center.z);
    bool built=root!=null&&root.transform.Find(id)!=null;
    sb.AppendLine("  "+id+" ("+row["name"]+"): "+house.name+" ["+house.gameObject.scene.name+"] "+new Vector2(b.center.x-x,b.center.z-z).magnitude.ToString("F1")+" m from the row, roof "+b.size.ToString("0.0")+" -> smoke at "+at.ToString("0.0")+(built?" (already built)":""));
    if(!built)plan.Add((id,at,row["name"].Value<string>()));
   }
   if(dry||plan.Count==0)return sb.Append(dry?" dry: nothing written":" nothing to build").ToString();
   var ledger=ReadLedger();string utc=PostLedger308.Utc();string backup=PostLedger308.Backup(BackupRoot,utc,main.path);ledger.backups.Add(backup);
   var mat=SmokeMaterial();
   if(root==null){root=new GameObject(HearthRoot);SceneManager.MoveGameObjectToScene(root,main);ledger.changes.Add(new Change{scene=main.path,kind="hearth",path=HearthRoot,before="",after="created",utc=utc});}
   foreach(var p in plan)
   {
    var holder=new GameObject(p.id);holder.transform.SetParent(root.transform,false);holder.transform.position=p.at;
    var go=new GameObject(SmokeName);go.transform.SetParent(holder.transform,false);go.isStatic=false;
    BuildSmoke(go,mat,F("rise"),F("life"),F("rate"),F("wind"),F("size0"),F("size1"),colour,c["cap"].Value<int>(),F("radius"));
    ledger.changes.Add(new Change{scene=main.path,kind="hearth",path=PostLedger308.PathOf(holder.transform),before="",after=p.label,utc=utc});
   }
   PostLedger308.SaveScene(main);WriteLedger(ledger);
   return sb.Append(" built "+plan.Count+"; saved "+PostLedger308.Short(main.path)+"; backup "+backup+"; ledger "+LedgerPath).ToString();
  }

  static string HearthRevert()
  {
   var main=HearthScene(true);
   var ledger=ReadLedger();var mine=ledger.changes.Where(c=>c.scene==main.path&&c.kind=="hearth").ToList();
   if(mine.Count==0)return "wash308-hearth:revert: nothing recorded";
   string utc=PostLedger308.Utc();string backup=PostLedger308.Backup(BackupRoot,utc,main.path);ledger.backups.Add(backup);int done=0;
   // children first, the root last
   foreach(var c in mine.OrderByDescending(c=>c.path.Length))
   {
    var t=PostLedger308.Find(main,c.path);
    if(t!=null&&(t.name==HearthRoot&&t.parent==null&&t.childCount==0||t.parent!=null&&t.parent.name==HearthRoot)){Object.DestroyImmediate(t.gameObject);done++;}
    else if(t!=null)continue;
    ledger.changes.Remove(c);
   }
   PostLedger308.SaveScene(main);WriteLedger(ledger);
   return "wash308-hearth:revert: removed "+done+"; backup "+backup;
  }
 }
}
