using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 capital town - the read-only half of Town308: check (AC-T1..T11 on the saved scene, the physical ground measured again),
 // eyes (the Presentation297 shot lines of the stills) and status. Nothing here writes a scene, an asset or a ledger.
 public static partial class Town308
 {
  // the smallest distance of two boxes: 0 when they touch or overlap, else alternating closest points (both convex: it converges)
  static float Gap(BoxCollider a,BoxCollider b)
  {
   if(Physics.ComputePenetration(a,a.transform.position,a.transform.rotation,b,b.transform.position,b.transform.rotation,out _,out _))return 0f;
   Vector3 p=b.bounds.center,q=a.bounds.center;
   for(int i=0;i<16;i++){q=a.ClosestPoint(p);p=b.ClosestPoint(q);}
   return Vector3.Distance(p,q);
  }
  static Vector3[] Corners(Item it)
  {
   float hx=it.K.W/2f,hz=it.K.D/2f;var flat=new Vector3(it.Pos.x,0f,it.Pos.z);
   return new[]{new Vector3(-hx,0,-hz),new Vector3(hx,0,-hz),new Vector3(hx,0,hz),new Vector3(-hx,0,hz)}.Select(c=>flat+it.Rot*c).ToArray();
  }

  static string Check(string arg)
  {
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,false);var scene=Open(t);Physics.SyncTransforms();int fail=0,pass=0;
   var sb=new StringBuilder("Town308 check "+t.Alias+" (data "+Short(cfg.Sha)+" "+(Opt(cfg.J,"version")??"?")+")\n");
   void C(bool ok,string what){if(ok)pass++;else fail++;sb.AppendLine("  ["+(ok?"PASS":"FAIL")+"] "+what);}
   var root=Root(cfg,scene);var want=EnabledSlices(cfg).SelectMany(p=>Arr(p,"rows").Select(r=>Str(p,"id")+"/"+Str(r,"id"))).ToList();
   var have=root==null?new List<string>():root.Cast<Transform>().SelectMany(h=>h.Cast<Transform>().Select(o=>h.name+"/"+o.name)).ToList();
   C(root!=null&&want.All(have.Contains)&&have.Count==want.Count,"AC-T1 "+cfg.Root+" holds exactly the rows of the enabled slices ("+have.Count+" of "+want.Count+")"+(root==null?" - the root is absent: run scene-apply":""));
   if(root==null)return sb.Append("  FAIL "+fail).ToString();
   C(scene.GetRootGameObjects().Count(g=>g.name==cfg.Root)==1&&!PostLedger308.UnderProtectedTree(root),"AC-T1 one root named "+cfg.Root+", outside the protected trees");
   var items=new List<Item>();var boxes=new List<(BoxCollider box,Item it)>();int buildings=0,flat=0,podium=0,stepStones=0,lod0=0;float worst=0f,sill=0f,riser=0f;bool ground=true,same=true,assets=true;
   foreach(var slice in EnabledSlices(cfg))foreach(var r in Arr(slice,"rows"))
   {
    var obj=root.Find(Str(slice,"id")+"/"+Str(r,"id"));if(obj==null){same=false;sb.AppendLine("     "+Str(r,"id")+" is not in the scene");continue;}
    Item it;try{it=Build(cfg,slice,r);}catch(Refuse e){ground=false;sb.AppendLine("     "+e.Message);continue;}
    items.Add(it);
    if(!Same(cfg,obj,it)){same=false;sb.AppendLine("     "+it.Id+" at "+V(obj.position)+" is not what the data and the physical ground give ("+V(it.Pos)+", "+it.Steps.Count+" step stone(s), "+it.K.Colliders.Length+" box(es))");}
    foreach(var mf in obj.GetComponentsInChildren<MeshFilter>(true))if(mf.sharedMesh==null||!EditorUtility.IsPersistent(mf.sharedMesh)||!AssetDatabase.GetAssetPath(mf.sharedMesh).StartsWith(cfg.AssetDir+"/",StringComparison.Ordinal)){assets=false;sb.AppendLine("     "+it.Id+"/"+mf.name+": its mesh is not an asset under "+cfg.AssetDir);}
    if(it.K.Building){buildings++;worst=Mathf.Max(worst,it.Relief);if(it.Seat=="flat")flat++;else podium++;sill=Mathf.Max(sill,it.SillMax);riser=Mathf.Max(riser,it.RiserMax);}
    stepStones+=it.Steps.Count;lod0+=Mathf.RoundToInt(Num(MeshRow(cfg,it.K.Lods[0]),"tris"));
    foreach(var bc in obj.GetComponentsInChildren<BoxCollider>(true))boxes.Add((bc,it));
   }
   if(stepStones>0)lod0+=stepStones*Mathf.RoundToInt(Num(MeshRow(cfg,"Step"),"tris"));
   C(same,"AC-T2 every row stands where the data and the PHYSICAL ground put it (plinth top = highest ground under the plinth + "+F(cfg.R("reveal_min_m"))+" m), with the data's meshes, materials, boxes and step stones ("+items.Count+" measured)");
   C(assets,"AC-T3 every mesh under the root is a Mesh asset of "+cfg.AssetDir);
   C(ground&&worst<=cfg.R("podium_relief_max_m"),"AC-T4 flat ground or a drawn plinth: "+buildings+" building(s), physical relief under the plinth max "+F(worst)+" m <= podium_relief_max_m "+F(cfg.R("podium_relief_max_m"))+" (flat <= "+F(cfg.R("flat_tol_m"))+": "+flat+", podium: "+podium+")");
   C(ground&&sill<=cfg.R("door_reveal_max_m")&&riser<=cfg.R("riser_max_m")+1e-3f&&cfg.R("riser_max_m")<=cfg.R("entry_step_limit_m"),"AC-T4 entry: highest door sill "+F(sill)+" m <= "+F(cfg.R("door_reveal_max_m"))+", highest riser "+F(riser)+" m <= riser_max_m "+F(cfg.R("riser_max_m"))+" <= the rule "+F(cfg.R("entry_step_limit_m"))+"; step stones "+stepStones);
   // colliders
   int all=root.GetComponentsInChildren<Collider>(true).Length,wantBoxes=items.Sum(i=>i.K.Colliders.Length);
   C(all==boxes.Count&&boxes.Count==wantBoxes,"AC-T5 every collider under the root is a data box: "+boxes.Count+" BoxCollider (data "+wantBoxes+"), "+(all-boxes.Count)+" other; step stones and the lantern carry none");
   int near=0,touching=0,snag=0,badTouch=0;float lane=float.PositiveInfinity;string laneWho="-",snagWho="";
   for(int i=0;i<boxes.Count;i++)for(int k=i+1;k<boxes.Count;k++)
   {
    if(boxes[i].it==boxes[k].it)continue;
    float d=Gap(boxes[i].box,boxes[k].box);if(d>=12f)continue;near++;
    if(d<=cfg.R("touch_max_m")){touching++;string a=Opt(boxes[i].it.Row,"attach"),b=Opt(boxes[k].it.Row,"attach");if(a!=boxes[k].it.Id&&b!=boxes[i].it.Id)badTouch++;continue;}
    if(d<lane){lane=d;laneWho=boxes[i].it.Id+" | "+boxes[k].it.Id;}
    if(d>cfg.R("snag_gap_lo_m")&&d<cfg.R("snag_gap_hi_m")-1e-3f){snag++;if(snagWho.Length<160)snagWho+=" ["+boxes[i].it.Id+"|"+boxes[k].it.Id+" "+F(d)+"]";}
   }
   C(snag==0&&badTouch==0,"AC-T6 no snagging gap: "+near+" box pair(s) nearer than 12 m - "+touching+" touching (a stall on its own shop: "+(touching-badTouch)+"), narrowest lane "+(float.IsInfinity(lane)?"-":F(lane))+" m ("+laneWho+") >= lane_min_m "+F(cfg.R("lane_min_m"))+"; pairs between "+F(cfg.R("snag_gap_lo_m"))+" and "+F(cfg.R("snag_gap_hi_m"))+" m: "+snag+snagWho);
   float lowTop=float.PositiveInfinity;string lowWho="-";
   foreach(var (box,it) in boxes){float top=box.bounds.max.y-it.GMax;if(top<lowTop){lowTop=top;lowWho=it.Id;}}
   C(boxes.Count>0&&lowTop>=cfg.R("collider_top_min_m"),"AC-T6 no lip: every box rises "+F(lowTop)+" m or more above the highest ground under it ("+lowWho+") >= collider_top_min_m "+F(cfg.R("collider_top_min_m"))+" - none ends between "+F(cfg.R("lip_lo_m"))+" and "+F(cfg.R("lip_hi_m"))+" m");
   // roads (straight on their x beside the slice: town_plan.py P4 proves that on the layout asset; here the built objects are measured)
   foreach(string side in new[]{"west","east"})
   {
    var rd=cfg.J["roads"][side];float rx=Num(rd,"x"),half=Num(rd,"half_m");float b=float.PositiveInfinity,o=float.PositiveInfinity;string bw="-",ow="-";
    foreach(var it in items)
    {
     float m=Corners(it).Min(c=>Mathf.Abs(c.x-rx))-half;
     if(it.K.Building){if(m<b){b=m;bw=it.Id;}}else if(m<o){o=m;ow=it.Id;}
     var step=cfg.Kinds["Step"];float reach=Mathf.Abs((it.Rot*new Vector3(step.W/2f,0f,0f)).x)+Mathf.Abs((it.Rot*new Vector3(0f,0f,step.D/2f)).x);   // the stone's half extent along world x
     foreach(var s in it.Steps){float ms=Mathf.Abs((it.Pos+it.Rot*s.Local).x-rx)-half-reach;if(ms<o){o=ms;ow=it.Id+"/"+s.Name;}}
    }
    C(b>=cfg.R("shoulder_min_m")-1e-3f&&o>=cfg.R("stall_shoulder_min_m")-1e-3f,"AC-T7 road "+Str(rd,"route")+" (x "+F(rx,"F0")+", "+F(half*2f,"F1")+" m wide) stays open: nearest building plinth "+F(b)+" m off its edge ("+bw+") >= "+F(cfg.R("shoulder_min_m"),"F1")+"; nearest stall / lantern / step stone "+F(o)+" m ("+ow+") >= "+F(cfg.R("stall_shoulder_min_m"),"F1"));
   }
   // lights and glow
   var ld=cfg.J["light"];var lights=root.GetComponentsInChildren<Light>(true);int wantLights=items.Count(i=>i.K.HasLight);
   C(lights.Length==wantLights&&lights.Length<=Mathf.RoundToInt(Num(ld,"max_count"))&&lights.All(l=>l.type==LightType.Point&&l.shadows==LightShadows.None&&l.range<=Num(ld,"range_m")+1e-3f&&l.intensity<=Num(ld,"intensity")+1e-3f),
    "AC-T8 lights under the root "+lights.Length+" (= "+wantLights+" lantern row(s), <= "+F(Num(ld,"max_count"),"F0")+"): "+(lights.Length==0?"none":string.Join(", ",lights.Select(l=>l.type+" range "+F(l.range,"F1")+" intensity "+F(l.intensity,"F1")+" shadows "+l.shadows))));
   var rends=root.GetComponentsInChildren<Renderer>(true);int glow=0,badGlow=0;
   foreach(var r in rends)foreach(var m in r.sharedMaterials){if(m==null){badGlow++;continue;}if(m==cfg.Glow)glow++;else if(m.IsKeywordEnabled("_EMISSION"))badGlow++;}
   C(badGlow==0&&glow==wantLights,"AC-T8 glow: the lantern material on "+glow+" renderer slot(s) (= lantern rows "+wantLights+"); _EMISSION or missing material on any other slot: "+badGlow+" ("+rends.Length+" renderer(s))");
   int scripts=root.GetComponentsInChildren<MonoBehaviour>(true).Length,texts=root.GetComponentsInChildren<TextMesh>(true).Length,bodies=root.GetComponentsInChildren<Rigidbody>(true).Length;
   int nav=root.GetComponentsInChildren<NavMeshAgent>(true).Length+root.GetComponentsInChildren<NavMeshObstacle>(true).Length,anim=root.GetComponentsInChildren<Animator>(true).Length+root.GetComponentsInChildren<AudioSource>(true).Length;
   C(scripts==0&&texts==0&&bodies==0&&nav==0&&anim==0,"AC-T9 dressing only: scripts "+scripts+", text meshes "+texts+", rigidbodies "+bodies+", NavMesh agents / obstacles "+nav+", animators / audio sources "+anim+" under the root (no person, no dialogue, no text, no content)");
   // cost against the offline count
   var off=cfg.J["offline"];int groups=root.GetComponentsInChildren<LODGroup>(true).Length;
   // a step stone more or less than offline is the physical ground speaking (a sill near a riser boundary): reported, not a FAIL
   int stepTris=Mathf.RoundToInt(Num(MeshRow(cfg,"Step"),"tris")),offSteps=off==null?0:Mathf.RoundToInt(Num(off,"steps"));
   bool cost=off!=null&&rends.Length-stepStones==Mathf.RoundToInt(Num(off,"renderers"))-offSteps&&boxes.Count==Mathf.RoundToInt(Num(off,"box_colliders"))&&lod0-stepStones*stepTris==Mathf.RoundToInt(Num(off,"tris_L0"))-offSteps*stepTris&&groups==Mathf.RoundToInt(Num(off,"lod_groups"));
   C(cost,"AC-T10 cost = the offline count (step stones aside): MeshRenderers "+rends.Length+", LODGroups "+groups+", BoxColliders "+boxes.Count+", near triangles "+lod0+", step stones "+stepStones+(off==null?" (the data has no offline block)":" (offline "+F(Num(off,"renderers"),"F0")+" / "+F(Num(off,"lod_groups"),"F0")+" / "+F(Num(off,"box_colliders"),"F0")+" / "+F(Num(off,"tris_L0"),"F0")+" / "+offSteps+")")+(off!=null&&stepStones!=offSteps?" - the physical ground gives another number of step stones than height_1b: look at the WARN rows of plan":""));
   // nothing of the scene stands inside a box (terrain aside)
   int hits=0;string hitWho="";float pad=cfg.R("object_clear_m");
   foreach(var (box,it) in boxes)
   {
    var half=Vector3.Scale(box.size,box.transform.lossyScale)*.5f+new Vector3(pad,0f,pad);
    foreach(var c in Physics.OverlapBox(box.transform.TransformPoint(box.center),half,box.transform.rotation,~0,QueryTriggerInteraction.Ignore))
    {
     if(c==null||c.transform.root==root||TerrainLike(c)||Preview(c.transform))continue;
     hits++;if(hitWho.Length<200)hitWho+=" ["+it.Id+" <- "+BuildingAudit308.KeyOf(c.transform)+"]";
    }
   }
   C(hits==0,"AC-T11 no collider of the scene (terrain aside) within "+F(pad,"F1")+" m of a box: "+hits+hitWho);
   if(scene.isDirty)sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
   return sb.Append("  "+(fail==0?"no FAIL":"FAIL "+fail)+" (PASS "+pass+")").ToString();
  }

  // ---------- eyes: the still lines on the physical ground ----------

  static string Eyes(string arg)
  {
   string label="";int ci=arg.IndexOf(':');if(ci>=0){label=arg.Substring(ci+1).Trim();arg=arg.Substring(0,ci);}
   if(label.Length>0&&!label.All(ch=>char.IsLetterOrDigit(ch)||ch=='_'))throw new Refuse("eyes label: letters, digits and _ only");
   var cfg=Load();var t=TargetOf(cfg,arg);PreviewGuard(cfg,t,false);Open(t);Physics.SyncTransforms();var read=Req(cfg.J,"read");
   var sb=new StringBuilder("Town308 eyes "+t.Alias+" (eye = physical ground + eye_h_m; a line that starts with 'shot:' is one Presentation297 Run argument, a line that starts with '#' is its note)\n");
   foreach(var slice in EnabledSlices(cfg))foreach(var s in Arr(slice,"stills"))
   {
    var e=Req(s,"eye") as JArray;var l=Req(s,"look") as JArray;
    if(!Ground(cfg,e[0].Value<float>(),e[1].Value<float>(),out var ge)||!Ground(cfg,l[0].Value<float>(),l[1].Value<float>(),out var gl)){sb.AppendLine("  "+Str(s,"id")+": no ground under the eye or the look-at point");continue;}
    Vector3 eye=ge+Vector3.up*Num(s,"eye_h_m"),look=gl+Vector3.up*Num(s,"look_h_m");
    // two lines per still: the note first (never part of the command), then the shot line with nothing after hideplayer
    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,"  # {0}: {1:F1} m - {2}",Str(s,"id"),Vector3.Distance(eye,look),Opt(s,"what")??""));
    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,"shot:town_{0}:{1:F2},{2:F2},{3:F2}:{4:F2},{5:F2},{6:F2}:fov={7:F0}:w={8:F0}:h={9:F0}:hideplayer",(label.Length>0?label+"_":"")+Str(slice,"id")+"_"+Str(s,"id"),eye.x,eye.y,eye.z,look.x,look.y,look.z,Num(read,"fov"),Num(read,"still_w"),Num(read,"still_h")));
   }
   return sb.ToString();
  }

  // ---------- status ----------

  static string Status()
  {
   var sb=new StringBuilder("Town308 status (play="+EditorApplication.isPlaying+")\n");
   Cfg cfg;try{cfg=Load();}catch(Refuse r){return sb.Append("  "+r.Message).ToString();}
   int rows=EnabledSlices(cfg).Sum(p=>Arr(p,"rows").Count()),solid=EnabledSlices(cfg).Sum(p=>Arr(p,"rows").Count(r=>cfg.Kinds[Str(r,"kind")].Solid));
   sb.AppendLine("  data "+DataFile+" sha "+Short(cfg.Sha)+" version "+(Opt(cfg.J,"version")??"?")+": "+EnabledSlices(cfg).Count()+" slice(s) on ("+string.Join(", ",EnabledSlices(cfg).Select(s=>Str(s,"id")))+"), "+rows+" row(s), "+solid+" with a BoxCollider");
   int ok=Arr(cfg.J,"meshes").Count(m=>MeshMatches(cfg,AssetDatabase.LoadAssetAtPath<Mesh>(MeshAsset(cfg,Str(m,"name"))),m));
   sb.AppendLine("  mesh assets in place "+ok+"/"+Arr(cfg.J,"meshes").Count()+" under "+cfg.AssetDir+"; mesh json "+Arr(cfg.J,"meshes").Count(m=>JsonState(cfg,m)==null)+"/"+Arr(cfg.J,"meshes").Count()+" as the data says; live asset ledger rows "+Live(ReadLedger(cfg,"assets")).Count());
   foreach(var t in cfg.Targets)sb.AppendLine("  "+t.Alias+": live scene ledger rows "+Live(ReadLedger(cfg,"scene_"+t.Alias)).Count());
   var active=SceneManager.GetActiveScene();var root=Root(cfg,active);
   return sb.Append("  active scene "+active.path+(active.isDirty?" (dirty)":"")+": "+cfg.Root+" "+(root==null?"absent":"present, "+root.Cast<Transform>().Sum(h=>h.childCount)+" object(s)")).ToString();
  }
 }
}
