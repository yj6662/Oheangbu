using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Oheangbu.App.Demo;

namespace Oheangbu.EditorTools.WorldMacro
{
 // verify:<scene> - read only. Every limit is a number of amneung308.json checks{} (TEST). The offline counterpart of V1-V9 is
 // Tools/Art/amneung308_dry.py (height field); here the same things are measured on the opened scene: its physics ground, the built
 // transforms and the real rock mesh. What only Play can show (the real CharacterController on the joint ledges, 국 cast on the pads,
 // the look) is NOT judged here - see the human checklist of the stage notes.
 public static partial class Amneung308
 {
  sealed class Tally
  {
   public readonly StringBuilder Text=new StringBuilder();public int Pass,Fail,Info;
   public void C(bool ok,string what){Text.AppendLine((ok?"PASS ":"FAIL ")+what);if(ok)Pass++;else Fail++;}
   public void I(string what){Text.AppendLine("INFO "+what);Info++;}
   // an identity line: the objects under the root are the ones the data builds (adopt:<scene> refuses on a FAIL of these; the lines
   // that measure the ground, the NavMesh, the trees or the place count of the realm are not identity)
   public int IdFail;public void Id(bool ok,string what){if(!ok)IdFail++;C(ok,what);}
  }

  static string Verify(string scenePath)
  {
   PostLedger308.RequireEditable();var d=Load();var scene=PostLedger308.Open(scenePath);string alias=PostLedger308.Short(scenePath);
   return Done(Judge(scenePath,alias,d,scene,LookActive(alias)),alias,scenePath);
  }
  // the lines of verify. lookOn = V2 / V3 / V10 are judged on the look rocks: verify asks the rocks ledger (LookActive), adopt:<scene>
  // decides by what stands in the holder (it has no ledger yet). Nothing is written here.
  static Tally Judge(string scenePath,string alias,DataD d,Scene scene,bool lookOn)
  {
   var k=new Tally();var ck=d.checks;var l=ReadLedger(alias);
   k.I("data sha "+Short(d.Sha)+" version "+d.version+"; ledger "+(l==null?"none":l.state+" "+l.utc+" data "+Short(l.dataSha)+(l.dataSha==d.Sha?"":" (NOT this data)")));
   int rootCount;var root=RootOf(scene,d.root,out rootCount);
   k.Id(rootCount==1,"V1 one root "+d.root+" in the scene (found "+rootCount+")");
   if(root==null)return k;
   Physics.SyncTransforms();
   var rocksT=root.transform.Find("Rocks");var coresT=root.transform.Find("Core");var stoneT=root.transform.Find("Stone");
   // ---- V2 rocks: transforms, LODs, no collider; V3 bottoms buried
   var lod0=AssetDatabase.LoadAssetAtPath<Mesh>(d.assets.rock_lod0);var lod1=AssetDatabase.LoadAssetAtPath<Mesh>(d.assets.rock_lod1);var mat=AssetDatabase.LoadAssetAtPath<Material>(d.assets.rock_material);
   int rockBad=0;float worstBury=float.MaxValue;string worstRock="";var built=new List<(RockD data,Transform t)>();
   // #308 fix 2 L1: while the rocks ledger is applied the holder carries the look rocks (Amneung308.Look.cs) - V2 / V3 / V10 are judged on them
   var lookBuilt=lookOn?LookVerifyRocks(k,d,alias,root.transform,rocksT):null;
   if(!lookOn)foreach(var r in d.rocks)
   {
    var t=rocksT!=null?rocksT.Find(r.id):null;
    if(t==null){rockBad++;k.Text.AppendLine("     rock "+r.id+" missing");continue;}
    built.Add((r,t));
    bool trs=Vector3.Distance(t.position,new Vector3(r.x,r.y,r.z))<=ck.trs_tol_m&&Mathf.Abs(Mathf.DeltaAngle(t.rotation.eulerAngles.y,r.yaw))<=ck.yaw_tol_deg&&Vector3.Distance(t.lossyScale,new Vector3(r.scale[0],r.scale[1],r.scale[2]))<=ck.trs_tol_m;
    var group=t.GetComponent<LODGroup>();var lods=group!=null?group.GetLODs():new LOD[0];
    var m0=t.Find("LOD0")?.GetComponent<MeshFilter>();var m1=t.Find("LOD1")?.GetComponent<MeshFilter>();
    bool look=lods.Length==2&&m0!=null&&m1!=null&&m0.sharedMesh==lod0&&m1.sharedMesh==lod1&&t.GetComponentsInChildren<MeshRenderer>(true).All(x=>x.sharedMaterial==mat);
    if(!trs||!look){rockBad++;k.Text.AppendLine("     rock "+r.id+": transform "+(trs?"ok":"OFF "+Trs(t))+", look "+(look?"ok":"OFF (LODs "+lods.Length+")"));}
    float lo=float.MaxValue;
    for(int i=-1;i<=1;i++)for(int j=-1;j<=1;j++){var w=ToWorld(i*r.scale[0]*.5f,j*r.scale[2]*.5f,r.x,r.z,r.yaw);if(Ground(w.x,w.y,root.transform,out float gy))lo=Mathf.Min(lo,gy);}
    if(lo-t.position.y<worstBury){worstBury=lo-t.position.y;worstRock=r.id;}
   }
   if(!lookOn){
   k.Id(rockBad==0&&built.Count==d.rocks.Length,"V2 rocks "+built.Count+" of "+d.rocks.Length+" at their data transform (tol "+F(ck.trs_tol_m)+" m / "+F(ck.yaw_tol_deg)+" deg), LODGroup with the two #275 meshes and the granite material ("+rockBad+" off)");
   int rockColliders=rocksT!=null?rocksT.GetComponentsInChildren<Collider>(true).Length:0;
   k.Id(rockColliders==0,"V2 visible rocks carry no collider (found "+rockColliders+"): the jointed ledges would be climbed");
   k.C(worstBury>=ck.rock_bury_min_m,"V3 rock bottoms under the lowest physical ground of their footprint (9 points each): min "+F(worstBury)+" m at "+worstRock+" (need "+F(ck.rock_bury_min_m)+")");
   }
   // ---- V4 cores: data transform, no renderer, top over / bottom under the ground
   int coreBad=0;float worstOver=float.MaxValue,worstUnder=float.MaxValue;string overAt="",underAt="";var boxes=new List<(string id,float x,float z,float yaw,float len,float thick)>();
   foreach(var c in d.cores)
   {
    var t=coresT!=null?coresT.Find(c.id):null;var box=t!=null?t.GetComponent<BoxCollider>():null;
    if(box==null){coreBad++;k.Text.AppendLine("     core "+c.id+" missing");continue;}
    var size=Vector3.Scale(box.size,t.lossyScale);
    bool ok=Vector3.Distance(t.TransformPoint(box.center),new Vector3(c.x,c.y,c.z))<=ck.trs_tol_m&&Mathf.Abs(Mathf.DeltaAngle(t.rotation.eulerAngles.y,c.yaw))<=ck.yaw_tol_deg&&Vector3.Distance(size,new Vector3(c.size[0],c.size[1],c.size[2]))<=ck.trs_tol_m
            &&!box.isTrigger&&box.enabled&&t.GetComponentsInChildren<Renderer>(true).Length==0&&t.GetComponent<Rigidbody>()==null;
    if(!ok){coreBad++;k.Text.AppendLine("     core "+c.id+": OFF "+Trs(t)+" size "+Harness303.V(size));}
    var wc=t.TransformPoint(box.center);boxes.Add((c.id,wc.x,wc.z,t.rotation.eulerAngles.y,size.x,size.z));
    float top=wc.y+size.y*.5f,bottom=wc.y-size.y*.5f,ring=ck.core_ground_ring_m,step=ck.support_probe_step_m;
    for(float u=-size.x*.5f-ring;u<=size.x*.5f+ring+1e-3f;u+=step)
     for(float v=-size.z*.5f-ring;v<=size.z*.5f+ring+1e-3f;v+=step)
     {
      float du=Mathf.Max(Mathf.Abs(u)-size.x*.5f,0f),dv=Mathf.Max(Mathf.Abs(v)-size.z*.5f,0f);if(du*du+dv*dv>ring*ring)continue;
      var w=ToWorld(u,v,wc.x,wc.z,t.rotation.eulerAngles.y);if(InStone(d,w.x,w.y))continue;       // the stone and its pads are judged by V6
      if(!Ground(w.x,w.y,root.transform,out float gy))continue;
      if(top-gy<worstOver){worstOver=top-gy;overAt=c.id;}
      if(du==0f&&dv==0f&&gy-bottom<worstUnder){worstUnder=gy-bottom;underAt=c.id;}
     }
   }
   k.Id(coreBad==0,"V4 cores "+(d.cores.Length-coreBad)+" of "+d.cores.Length+": BoxCollider at the data centre / yaw / size, solid, no renderer, no Rigidbody");
   k.C(worstOver>=ck.core_over_ground_min_m,"V4 core tops over the highest physical ground within "+F(ck.core_ground_ring_m,"F1")+" m of their footprint: min "+F(worstOver)+" m at "+overAt+" (need "+F(ck.core_over_ground_min_m)+" = 국 2.4 + jump 0.75 + step 0.30 + slack)");
   k.C(worstUnder>=ck.core_bury_min_m,"V4 core bottoms under the lowest physical ground of their footprint: min "+F(worstUnder)+" m at "+underAt+" (need "+F(ck.core_bury_min_m)+")");
   // ---- V5 no gap: every joint of the chain core .. core, stone, core .. core overlaps
   if(boxes.Count==d.cores.Length&&boxes.Count>1)
   {
    var chain=boxes.ToList();int at=0;float best=float.MaxValue;
    for(int i=0;i+1<chain.Count;i++){float s=Dist(chain[i].x,chain[i].z,d.stone.x,d.stone.z)+Dist(chain[i+1].x,chain[i+1].z,d.stone.x,d.stone.z);if(s<best){best=s;at=i;}}
    chain.Insert(at+1,("stone",d.stone.x,d.stone.z,d.stone.yaw_box,d.stone.half_m*2f,d.stone.half_m*2f));
    float thin=float.MaxValue;string thinAt="";
    for(int i=0;i+1<chain.Count;i++){var j=Joint(chain[i],chain[i+1]);float m=Mathf.Min(j.x,j.y);if(m<thin){thin=m;thinAt=chain[i].id+" | "+chain[i+1].id+" ("+F(j.x)+" along x "+F(j.y)+" across)";}}
    k.C(thin>=ck.joint_min_m,"V5 no gap in the core chain ("+boxes.Count+" boxes + the stone): the thinnest joint "+thinAt+" (need "+F(ck.joint_min_m)+" m both ways)");
   }
   else k.C(false,"V5 core chain not judged (cores missing)");
   // ---- V6 stone and pads: a collider of this root answers at the data top; rise
   float stoneTop=float.NaN,padTopY=float.NaN;int stoneMiss=0,padMiss=0,n=0;
   foreach(var off in new[]{Vector2.zero,new Vector2(1,1),new Vector2(-1,1),new Vector2(1,-1),new Vector2(-1,-1)})
   {
    var w=ToWorld(off.x*d.stone.half_m*.6f,off.y*d.stone.half_m*.6f,d.stone.x,d.stone.z,d.stone.yaw_box);n++;
    if(OwnTop(root.transform,stoneT,w.x,w.y,out float y)&&Mathf.Abs(y-d.stone.top_y)<=ck.stone_top_tol_m)stoneTop=float.IsNaN(stoneTop)?y:Mathf.Max(stoneTop,y);else stoneMiss++;
   }
   k.Id(stoneMiss==0,"V6 stone top: a collider of the Stone group answers within "+F(ck.stone_top_tol_m)+" m of y "+F(d.stone.top_y)+" at "+(n-stoneMiss)+" of "+n+" probe points"+(float.IsNaN(stoneTop)?"":" (measured "+F(stoneTop)+")"));
   foreach(var pad in d.stone.pads)
   {
    // the part of the pad the stone does not cover: its outer third, toward the open side
    var dir=new Vector2(pad.x-d.stone.x,pad.z-d.stone.z).normalized;float out_=pad.size[2]*.5f*.6f;
    if(OwnTop(root.transform,stoneT,pad.x+dir.x*out_,pad.z+dir.y*out_,out float y)&&Mathf.Abs(y-pad.top_y)<=ck.pad_top_tol_m)padTopY=y;else{padMiss++;k.Text.AppendLine("     pad "+pad.id+": no top at y "+F(pad.top_y)+(float.IsNaN(y)?"":" (measured "+F(y)+")"));}
    float lo=float.MaxValue,hi=float.MinValue;
    for(int i=-1;i<=1;i++)for(int j=-1;j<=1;j++){var w=ToWorld(i*pad.size[0]*.5f,j*pad.size[2]*.5f,pad.x,pad.z,pad.yaw);if(Ground(w.x,w.y,root.transform,out float gy)){lo=Mathf.Min(lo,gy);hi=Mathf.Max(hi,gy);}}
    k.C(hi-pad.top_y<=ck.pad_ground_over_top_max_m&&pad.top_y-pad.size[1]<lo,"V6 pad "+pad.id+": top "+F(pad.top_y)+", physical ground under it "+F(lo)+".."+F(hi)+" (ground over the top at most "+F(ck.pad_ground_over_top_max_m)+" m, bottom "+F(pad.top_y-pad.size[1])+" under the lowest ground)");
   }
   k.Id(padMiss==0&&d.stone.pads.Length>0,"V6 pad tops: "+(d.stone.pads.Length-padMiss)+" of "+d.stone.pads.Length+" answer within "+F(ck.pad_top_tol_m)+" m of their data top");
   if(!float.IsNaN(stoneTop)&&!float.IsNaN(padTopY))k.C(stoneTop-padTopY>=ck.rise_m[0]&&stoneTop-padTopY<=ck.rise_m[1],"V6 lift rise (measured stone top - pad top) "+F(stoneTop-padTopY)+" m in ["+F(ck.rise_m[0],"F1")+", "+F(ck.rise_m[1],"F1")+"] (맨땅 국 = 2.4 m)");
   else k.C(false,"V6 lift rise not measured (stone or pad top missing)");
   var parts=stoneT!=null?d.stone.parts.Select(x=>stoneT.Find(x.name)).Where(t=>t!=null).ToArray():new Transform[0];
   foreach(var t in parts)
   {
    var rs=t.GetComponentsInChildren<Renderer>(true);if(rs.Length==0){k.C(false,"V6 "+t.name+" has no renderer");continue;}
    var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);float side=Mathf.Min(b.size.x,b.size.z);
    k.C(side>=ck.stone_size_m[0]&&side<=ck.stone_size_m[1],"V6 "+t.name+": look bounds "+F(b.size.x)+" x "+F(b.size.y)+" x "+F(b.size.z)+" m, top "+F(b.max.y)+" (plan side in ["+F(ck.stone_size_m[0],"F1")+", "+F(ck.stone_size_m[1],"F1")+"] m: the reach proof assumed a 4 x 4 m top)");
   }
   k.Id(parts.Length==d.stone.parts.Length,"V6 stone parts "+parts.Length+" of "+d.stone.parts.Length+" (cloned from "+d.stone.source_root+")");
   // ---- V7 site nodes; V8 국 places
   var sd=d.stone.site;
   if(sd.enabled)
   {
    var sites=root.GetComponentsInChildren<DemoGukRevisitSite>(true).Where(s=>s.RewardId==sd.reward_id).ToArray();
    if(sites.Length!=1)k.Id(false,"V7 one DemoGukRevisitSite "+sd.reward_id+" under the root (found "+sites.Length+")");
    else
    {
     var s=sites[0];bool nodes=s.LiftPad!=null&&s.UpperSurface!=null&&s.DescentExit!=null;float up=nodes?s.UpperSurface.position.y-s.LiftPad.position.y:float.NaN;
     k.Id(nodes&&s.isActiveAndEnabled&&up>=ck.rise_m[0]&&up<=ck.rise_m[1],"V7 site "+sd.reward_id+": active, three nodes, UpperSurface - LiftPad "+(nodes?F(up):"-")+" m in ["+F(ck.rise_m[0],"F1")+", "+F(ck.rise_m[1],"F1")+"]");
     if(nodes)k.C(Harness303.Flat(s.UpperSurface.position,new Vector3(d.stone.x,0,d.stone.z))<=d.stone.half_m&&Harness303.Flat(s.LiftPad.position,s.DescentExit.position)>d.stone.half_m*2f,"V7 UpperSurface on the stone, LiftPad and DescentExit on opposite sides ("+F(Harness303.Flat(s.LiftPad.position,s.DescentExit.position))+" m apart): both directions");
    }
   }
   else k.I("V7 site off in data (no DemoGukRevisitSite; the place is then not counted by Content308 scene-check)");
   try
   {
    var names=new List<string>();int gates=Content308.GukGates308(scene,ck.gates_realm,names);
    // #308 D308-18 (3): the cliff-top lift (CliffGuk308 lift-on) adds one place. The count with it is the number Content308 scene-check
    // already accepts (content308_scene.json gates.expected_with_cliff_top) - read from there, so this data file (and the ledger sha
    // that pins it) never has to change when the lift goes on.
    int withTop=Content308.GukGatesWithCliffTop308(ck.gates_realm);
    k.C((gates==ck.gates_expected||(withTop>0&&gates==withTop))&&gates<=ck.gates_cap-ck.gates_reserved,"V8 LDB:55 국 places in "+ck.gates_realm+": "+gates+" ["+string.Join(", ",names)+"] (expected "+ck.gates_expected+(withTop>0?", or "+withTop+" with the cliff-top site":"")+"; cap "+ck.gates_cap+" with "+ck.gates_reserved+" kept for the 신목 upper place -> at most "+(ck.gates_cap-ck.gates_reserved)+")");
   }
   catch(Exception e){k.C(false,"V8 국 place count failed: "+e.Message);}
   // ---- V9 canon: no light, no glow, no text
   int lights=root.GetComponentsInChildren<Light>(true).Length;
   var glow=root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&Emissive(m)).Select(m=>m.name).Distinct().ToArray();
   var texts=root.GetComponentsInChildren<Component>(true).Where(c=>c!=null&&TextType(c.GetType())).Select(c=>c.GetType().Name).Distinct().ToArray();
   var scripts=root.GetComponentsInChildren<MonoBehaviour>(true).Where(m=>m!=null&&!(m is DemoGukRevisitSite)).Select(m=>m.GetType().Name).Distinct().ToArray();
   k.Id(lights==0,"V9 Light components under the root: "+lights+" (must be 0; the only new sustained light of the extension is the inn lantern)");
   k.Id(glow.Length==0,"V9 emissive materials under the root: "+glow.Length+(glow.Length>0?" ["+string.Join(", ",glow)+"]":"")+" (must be 0)");
   k.Id(texts.Length==0,"V9 text components under the root: "+texts.Length+(texts.Length>0?" ["+string.Join(", ",texts)+"]":"")+" (must be 0: no instruction text)");
   k.Id(scripts.Length==0,"V9 scripts under the root besides the site marker: "+scripts.Length+(scripts.Length>0?" ["+string.Join(", ",scripts)+"]":""));
   // ---- V10 core-fit on the real LOD1 mesh
   if(lookOn)LookVerifyFit(k,d,root.transform,boxes,lookBuilt);
   else if(lod1!=null&&built.Count>0&&boxes.Count==d.cores.Length)
   {
    var tris=new List<(Vector3 a,Vector3 b,Vector3 c,float x0,float x1,float z0,float z1)>();
    var verts=lod1.vertices;var idx=lod1.triangles;
    foreach(var (_,t) in built)
    {
     var m=t.localToWorldMatrix;
     for(int i=0;i+2<idx.Length;i+=3)
     {
      Vector3 a=m.MultiplyPoint3x4(verts[idx[i]]),b=m.MultiplyPoint3x4(verts[idx[i+1]]),c=m.MultiplyPoint3x4(verts[idx[i+2]]);
      tris.Add((a,b,c,Mathf.Min(a.x,Mathf.Min(b.x,c.x)),Mathf.Max(a.x,Mathf.Max(b.x,c.x)),Mathf.Min(a.z,Mathf.Min(b.z,c.z)),Mathf.Max(a.z,Mathf.Max(b.z,c.z))));
     }
    }
    float RockTop(float x,float z)
    {
     float top=float.NegativeInfinity;
     foreach(var q in tris)
     {
      if(x<q.x0||x>q.x1||z<q.z0||z>q.z1)continue;
      float den=(q.b.z-q.c.z)*(q.a.x-q.c.x)+(q.c.x-q.b.x)*(q.a.z-q.c.z);if(Mathf.Abs(den)<1e-9f)continue;
      float l1=((q.b.z-q.c.z)*(x-q.c.x)+(q.c.x-q.b.x)*(z-q.c.z))/den,l2=((q.c.z-q.a.z)*(x-q.c.x)+(q.a.x-q.c.x)*(z-q.c.z))/den,l3=1f-l1-l2;
      if(l1<-1e-5f||l2<-1e-5f||l3<-1e-5f)continue;
      float y=l1*q.a.y+l2*q.b.y+l3*q.c.y;if(y>top)top=y;
     }
     return top;
    }
    int low=0,bare=0,facePts=0,cells=0;float cell=ck.raster_cell_m;
    for(int ci=0;ci<boxes.Count;ci++)
    {
     var bx=boxes[ci];float coreTop=d.cores[ci].top_y;
     for(float u=-bx.len*.5f+cell*.5f;u<bx.len*.5f;u+=cell)
      for(float v=-bx.thick*.5f+cell*.5f;v<bx.thick*.5f;v+=cell)
      {
       var w=ToWorld(u,v,bx.x,bx.z,bx.yaw);if(!Ground(w.x,w.y,root.transform,out float gy))continue;cells++;
       float rt=RockTop(w.x,w.y);if(rt<coreTop-1e-3f&&rt<gy+ck.fit_low_rock_m)low++;
      }
     for(int side=-1;side<=1;side+=2)
      for(float u=-bx.len*.5f;u<=bx.len*.5f+1e-3f;u+=cell)
      {
       var w=ToWorld(u,side*bx.thick*.5f,bx.x,bx.z,bx.yaw);if(!Ground(w.x,w.y,root.transform,out float gy))continue;facePts++;
       if(!(RockTop(w.x,w.y)>=gy+ck.fit_face_h_m))bare++;
      }
    }
    k.C(low<=ck.fit_low_cells_max,"V10 core-fit: core cells where the rock is lower than the core AND lower than ground + "+F(ck.fit_low_rock_m)+" m: "+low+" of "+cells+" (limit "+ck.fit_low_cells_max+"; an invisible wall over a low rock)");
    k.C(bare<=ck.fit_bare_points_max,"V10 core-fit: core face points with no rock "+F(ck.fit_face_h_m,"F1")+" m above the ground: "+bare+" of "+facePts+" (limit "+ck.fit_bare_points_max+"; the wall would stand outside the rock)");
   }
   else k.C(false,"V10 core-fit not judged (rock mesh or objects missing)");
   // ---- V11 trees; V12 NavMesh note
   var asks=new List<BuildingFix308.VegRowAsk308>();
   foreach(var s in d.vegetation.sheets.Where(s=>s.role=="rendered"))foreach(var r in d.vegetation.rows)asks.Add(new BuildingFix308.VegRowAsk308{sheet=s.path,id=r.id,proto=r.proto,at=new Vector3(r.x,r.y,r.z)});
   int left=BuildingFix308.VegRowsLeft(asks,d.vegetation.tolerance_m);
   k.C(left==0,"V11 trees under the band still in the rendered sheet: "+(left<0?"sheet missing":left.ToString())+" of "+d.vegetation.rows.Length+" (0 after Amneung308 veg-apply; the sheet is shared by the three scenes)");
   int navUnder=0,navTop=0;
   foreach(var bx in boxes)
   {
    if(Ground(bx.x,bx.z,root.transform,out float gy)&&NavMesh.SamplePosition(new Vector3(bx.x,gy,bx.z),out _,ck.nav_probe_m,NavMesh.AllAreas))navUnder++;
   }
   foreach(var c in d.cores)if(NavMesh.SamplePosition(new Vector3(c.x,c.top_y,c.z),out _,ck.nav_probe_m,NavMesh.AllAreas))navTop++;
   bool navStone=NavMesh.SamplePosition(new Vector3(d.stone.x,d.stone.top_y,d.stone.z),out _,ck.nav_probe_m,NavMesh.AllAreas);
   k.I("V12 NavMesh: under "+navUnder+" of "+boxes.Count+" core centres there is still NavMesh on the ground ("+(navUnder>0?"the bake is OLDER than the band: enemies would path through it - run the shared bake after the three applies":"the bake already excludes the band")+"); on core tops "+navTop+", on the stone top "+(navStone?"yes (an island: no link is authored, enemies do not cross)":"no"));
   return k;
  }

  static string Done(Tally k,string alias,string scenePath)
  {
   string head="Amneung308 verify "+scenePath+"\nPASS "+k.Pass+" / FAIL "+k.Fail+" / INFO "+k.Info+"\n";
   Directory.CreateDirectory(OutDir);string file=Path.Combine(OutDir,"amneung308-"+alias+"-verify.txt");File.WriteAllText(file,head+k.Text,new UTF8Encoding(false));
   return head+k.Text+"  report "+file;
  }

  static float Dist(float ax,float az,float bx,float bz)=>Mathf.Sqrt((ax-bx)*(ax-bx)+(az-bz)*(az-bz));
  static bool InStone(DataD d,float x,float z)
  {
   var s=ToLocal(x,z,d.stone.x,d.stone.z,d.stone.yaw_box);if(Mathf.Abs(s.x)<=d.stone.half_m&&Mathf.Abs(s.y)<=d.stone.half_m)return true;
   foreach(var p in d.stone.pads){var q=ToLocal(x,z,p.x,p.z,p.yaw);if(Mathf.Abs(q.x)<=p.size[0]*.5f&&Mathf.Abs(q.y)<=p.size[2]*.5f)return true;}
   return false;
  }
  // overlap of two footprint boxes, as its extent along and across the first one (0, 0 = they do not touch); 0.05 m samples
  static Vector2 Joint((string id,float x,float z,float yaw,float len,float thick) a,(string id,float x,float z,float yaw,float len,float thick) b)
  {
   const float step=.05f;float u0=float.MaxValue,u1=float.MinValue,v0=float.MaxValue,v1=float.MinValue;bool any=false;
   for(float u=-a.len*.5f;u<=a.len*.5f+1e-4f;u+=step)
    for(float v=-a.thick*.5f;v<=a.thick*.5f+1e-4f;v+=step)
    {
     var w=ToWorld(u,v,a.x,a.z,a.yaw);var q=ToLocal(w.x,w.y,b.x,b.z,b.yaw);
     if(Mathf.Abs(q.x)>b.len*.5f||Mathf.Abs(q.y)>b.thick*.5f)continue;
     any=true;u0=Mathf.Min(u0,u);u1=Mathf.Max(u1,u);v0=Mathf.Min(v0,v);v1=Mathf.Max(v1,v);
    }
   return any?new Vector2(u1-u0+step,v1-v0+step):Vector2.zero;
  }
  // the highest upward-facing hit of a collider under `group` (itself under `root`) at (x, z)
  static bool OwnTop(Transform root,Transform group,float x,float z,out float y)
  {
   y=float.NaN;if(group==null)return false;float best=float.NegativeInfinity;
   foreach(var h in Physics.RaycastAll(new Vector3(x,2000f,z),Vector3.down,4000f,~0,QueryTriggerInteraction.Ignore))
    if(h.collider!=null&&h.normal.y>.5f&&h.collider.transform.IsChildOf(group)&&h.point.y>best)best=h.point.y;
   if(float.IsNegativeInfinity(best))return false;
   y=best;return true;
  }
 }
}
