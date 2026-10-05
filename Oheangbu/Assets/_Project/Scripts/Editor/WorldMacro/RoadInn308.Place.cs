using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 // RoadInn308 - where the lantern may hang (relayout fix 2b, AC-I9). READ ONLY: nothing here writes a scene, an asset or a file.
 // The first lantern place was worked out against the house's base box and came out inside the room of the #303 house: drawn in
 // none of 15 stills, and the old checks (pose = the data's offset) all passed. These four lines measure the place instead:
 //   (ga)  no collider reaches into the lantern BODY box (the renderers under place.body_parts = the two caps; the paper drum lies
 //         between them, the hanging rod above is not part of it) grown by place.clear_m on every side - Physics.OverlapBox
 //   (na)  the rod's top hangs inside the roof over it - the roof MESHES (a thatch roof has no collider): at most hang.gap_max_m
 //         under the underside, at least hang.cover_min_m under the top surface
 //   (da)  the body keeps place.door.clear_m from the entry steps in plan
 //   (ra)  from every `need` row of place.sight.eyes (sight.eye_m over the physical ground) the line to the middle of the body
 //         box is free of colliders OUTSIDE sight.building_roots (terrain, cliffs, walls of the world) AND of every triangle
 //         DRAWN under sight.building_roots (there what is drawn decides: this hamlet's collider boxes are the bounding boxes
 //         of L-shaped parts - larger than what is drawn in places, absent in others: the lived-in house's wall box covers its
 //         main wing only, the south end of its west wing and every thatch roof carry none) AND of the vegetation rows of the scene's sheets
 //         (a tree = its trunk + a crown ellipsoid from veg.crown_from of its height, anything else one ellipsoid).
 //         Rows with need false are reported only.
 //   (ga) is followed by one INFO line: how far the body box can grow before a triangle DRAWN under sight.building_roots reaches
 //         into it (the wall at the south end of the west wing and the thatch carry no collider, so (ga) alone cannot see them). Reported, never failed:
 //         the wall plane is caught by the timber colliders standing in it and by (ra).
 // Every number is roadinn308.json lantern.place (place.report_grow_m = how far the two "can grow" reports look; the 12 halvings
 // of GrowRoom are numeric resolution = that reach / 4096, not a rule). The same law, on the real meshes offline:
 // roadinn_lantern308_dry.py.
 public static partial class RoadInn308
 {
  sealed class VegRow308{public string id="",proto="";public Vector3 pos;public float height,radius,trunk;public bool tree;}
  sealed class SightWorld308
  {
   public readonly List<(MeshRenderer r,BuildingAudit308.MeshTris308 m)> buildings=new List<(MeshRenderer,BuildingAudit308.MeshTris308)>();
   public readonly List<VegRow308> veg=new List<VegRow308>();public int unreadable,sheets;public HashSet<string> roots=new HashSet<string>();
  }

  static HashSet<string> NameSet(JToken o,string key)=>new HashSet<string>(Arr(o,key).Select(x=>x.Value<string>()));
  static bool UnderNamed(Transform t,Transform stop,HashSet<string> names){for(var p=t;p!=null&&p!=stop;p=p.parent)if(names.Contains(p.name))return true;return false;}
  static bool Drawn(MeshRenderer r){var mf=r.GetComponent<MeshFilter>();return r.enabled&&r.gameObject.activeInHierarchy&&mf!=null&&mf.sharedMesh!=null;}

  // the lantern as it is drawn: body = the renderers under the place.body_parts children; rod top = its highest drawn point
  // (the far-light sleeve / card of lantern.focus_exclude is another tool's and never counts)
  static bool LanternShape(Cfg cfg,Transform lantern,out Bounds body,out float rodTop,out string why)
  {
   body=default;rodTop=float.NegativeInfinity;why=null;
   var parts=NameSet(Req(cfg.Lantern,"place"),"body_parts");var foreign=NameSet(cfg.Lantern,"focus_exclude");bool any=false;
   foreach(var r in lantern.GetComponentsInChildren<MeshRenderer>(true))
   {
    if(!Drawn(r)||UnderNamed(r.transform,lantern,foreign))continue;
    rodTop=Mathf.Max(rodTop,r.bounds.max.y);
    if(!UnderNamed(r.transform,lantern,parts))continue;
    if(!any){body=r.bounds;any=true;}else body.Encapsulate(r.bounds);
   }
   if(!any)why="no drawn renderer under a child named "+string.Join(" / ",parts)+" (lantern.place.body_parts): the body box cannot be taken";
   return any;
  }

  // colliders inside the box grown by `grow` (the lantern's own root, previews, actors / NPC bodies and the player never count)
  static string[] BoxHits(Cfg cfg,Bounds body,float grow)
   =>Physics.OverlapBox(body.center,body.extents+Vector3.one*grow,Quaternion.identity,~0,QueryTriggerInteraction.Ignore)
    .Where(c=>c!=null&&!(c is CharacterController)&&!skip.Contains(c)&&!Preview(c.transform)&&!Own(cfg,c.transform)).Select(c=>BuildingAudit308.KeyOf(c.transform)).Distinct().ToArray();

  // REPORT helper: how far the box can grow on every side before inside(grow) names something. false = nothing within `reach`.
  static bool GrowRoom(float reach,Func<float,string[]> inside,out float room,out string[] who)
  {
   room=reach;who=inside(reach);if(who.Length==0)return false;
   float lo=0f,hi=reach;
   for(int i=0;i<12;i++){float mid=(lo+hi)*.5f;var at=inside(mid);if(at.Length>0){hi=mid;who=at;}else lo=mid;}
   room=lo;return true;
  }

  // does the triangle touch the axis-aligned box (centre, half)? Separating axes: the 3 box normals, the triangle normal, the 9
  // edge crosses (the test of roadinn_lantern308_dry.py tri_box_overlap)
  static bool TriInBox(Vector3 a,Vector3 b,Vector3 c,Vector3 centre,Vector3 half)
  {
   a-=centre;b-=centre;c-=centre;
   if(Mathf.Min(a.x,Mathf.Min(b.x,c.x))>half.x||Mathf.Max(a.x,Mathf.Max(b.x,c.x))<-half.x)return false;
   if(Mathf.Min(a.y,Mathf.Min(b.y,c.y))>half.y||Mathf.Max(a.y,Mathf.Max(b.y,c.y))<-half.y)return false;
   if(Mathf.Min(a.z,Mathf.Min(b.z,c.z))>half.z||Mathf.Max(a.z,Mathf.Max(b.z,c.z))<-half.z)return false;
   Vector3 e0=b-a,e1=c-b,e2=a-c;var n=Vector3.Cross(e0,e1);
   if(Mathf.Abs(Vector3.Dot(n,a))>Mathf.Abs(n.x)*half.x+Mathf.Abs(n.y)*half.y+Mathf.Abs(n.z)*half.z)return false;
   bool Apart(Vector3 ax)
   {
    float p0=Vector3.Dot(a,ax),p1=Vector3.Dot(b,ax),p2=Vector3.Dot(c,ax),r=Mathf.Abs(ax.x)*half.x+Mathf.Abs(ax.y)*half.y+Mathf.Abs(ax.z)*half.z;
    return Mathf.Min(p0,Mathf.Min(p1,p2))>r||Mathf.Max(p0,Mathf.Max(p1,p2))<-r;
   }
   bool EdgeApart(Vector3 e)=>Apart(Vector3.Cross(Vector3.right,e))||Apart(Vector3.Cross(Vector3.up,e))||Apart(Vector3.Cross(Vector3.forward,e));
   return !(EdgeApart(e0)||EdgeApart(e1)||EdgeApart(e2));
  }

  // the triangles DRAWN under sight.building_roots that come within `reach` of the box, in world space, with their renderer's key
  static List<(Vector3 a,Vector3 b,Vector3 c,string key)> DrawnNear(SightWorld308 w,Bounds box,float reach)
  {
   var near=new List<(Vector3,Vector3,Vector3,string)>();var big=new Bounds(box.center,box.size+Vector3.one*(2f*reach));
   foreach(var (r,m) in w.buildings)
   {
    if(!r.bounds.Intersects(big))continue;var l2w=r.transform.localToWorldMatrix;string key=BuildingAudit308.KeyOf(r.transform);
    for(int i=0;i+2<m.t.Length;i+=3)
    {
     Vector3 a=l2w.MultiplyPoint3x4(m.v[m.t[i]]),b=l2w.MultiplyPoint3x4(m.v[m.t[i+1]]),c=l2w.MultiplyPoint3x4(m.v[m.t[i+2]]);
     if(TriInBox(a,b,c,big.center,big.extents))near.Add((a,b,c,key));
    }
   }
   return near;
  }

  // the roof meshes over a vertical line: lowest and highest crossing above `above` (infinity / -infinity when there is none)
  static void RoofOver(Scene scene,IEnumerable<string> keys,float x,float z,float above,out float under,out float top,List<string> missing)
  {
   under=float.PositiveInfinity;top=float.NegativeInfinity;var cache=new BuildingAudit308.MeshCache308();
   foreach(var key in keys)
   {
    var root=BuildingAudit308.Resolve(scene,key);if(root==null){missing.Add(key);continue;}
    foreach(var r in root.GetComponentsInChildren<MeshRenderer>(false))
    {
     if(!Drawn(r))continue;var b=r.bounds;if(x<b.min.x||x>b.max.x||z<b.min.z||z>b.max.z||b.max.y<above)continue;
     var m=cache.Get(r.GetComponent<MeshFilter>().sharedMesh);if(m==null)continue;var w=r.transform.localToWorldMatrix;
     for(int i=0;i+2<m.t.Length;i+=3)
     {
      Vector3 a=w.MultiplyPoint3x4(m.v[m.t[i]]),p=w.MultiplyPoint3x4(m.v[m.t[i+1]]),c=w.MultiplyPoint3x4(m.v[m.t[i+2]]);
      if(x<Mathf.Min(a.x,Mathf.Min(p.x,c.x))||x>Mathf.Max(a.x,Mathf.Max(p.x,c.x))||z<Mathf.Min(a.z,Mathf.Min(p.z,c.z))||z>Mathf.Max(a.z,Mathf.Max(p.z,c.z)))continue;
      double d=((double)p.z-c.z)*((double)a.x-c.x)+((double)c.x-p.x)*((double)a.z-c.z);if(Math.Abs(d)<1e-12)continue;
      double w0=(((double)p.z-c.z)*((double)x-c.x)+((double)c.x-p.x)*((double)z-c.z))/d,w1=(((double)c.z-a.z)*((double)x-c.x)+((double)a.x-c.x)*((double)z-c.z))/d,w2=1-w0-w1;
      if(w0<0||w1<0||w2<0)continue;float y=(float)(w0*a.y+w1*p.y+w2*c.y);if(y<=above)continue;
      under=Mathf.Min(under,y);top=Mathf.Max(top,y);
     }
    }
   }
  }

  // ---------- sight: what can stand on a line ----------

  static SightWorld308 SightWorld(Scene scene,JToken sight)
  {
   var w=new SightWorld308();var roots=NameSet(sight,"building_roots");var ignore=NameSet(sight,"ignore_names");var cache=new BuildingAudit308.MeshCache308();w.roots=roots;
   foreach(var g in scene.GetRootGameObjects())
   {
    if(!roots.Contains(g.name)||Preview(g.transform))continue;
    foreach(var r in g.GetComponentsInChildren<MeshRenderer>(false))
    {
     if(!Drawn(r)||UnderNamed(r.transform,null,ignore))continue;
     var m=cache.Get(r.GetComponent<MeshFilter>().sharedMesh);if(m==null){w.unreadable++;continue;}
     w.buildings.Add((r,m));
    }
   }
   var veg=Req(sight,"veg");float trunkMin=Num(veg,"trunk_min_m");
   foreach(var sheet in BuildingAudit308.SceneSheets(scene,new BuildingAudit308.Config308()))
   {
    w.sheets++;var protos=new Dictionary<string,WorldMacroDressingSheetSO.Prototype>();
    foreach(var p in sheet.Prototypes??Array.Empty<WorldMacroDressingSheetSO.Prototype>())if(p!=null&&!string.IsNullOrEmpty(p.Id))protos[p.Id]=p;
    foreach(var f in sheet.FixedPlacements??Array.Empty<WorldMacroDressingSheetSO.FixedPlacement>())
    {
     if(f==null||f.PrototypeId==null||!protos.TryGetValue(f.PrototypeId,out var p)||p.Category==WorldMacroDressingSheetSO.Kind.Grass)continue;
     w.veg.Add(new VegRow308{id=f.Id??"",proto=f.PrototypeId,pos=f.Position,height=p.Size.y*f.Scale,radius=Mathf.Max(p.Size.x,p.Size.z)*.5f*f.Scale,trunk=Mathf.Max(p.Radius*f.Scale,trunkMin),tree=p.Category==WorldMacroDressingSheetSO.Kind.Tree});
    }
   }
   return w;
  }

  // first crossing (0..1) of the segment p-q with a mesh, in the mesh's own frame; -1 = none
  static float SegMesh(BuildingAudit308.MeshTris308 m,Vector3 p,Vector3 q)
  {
   var d=q-p;float best=-1f;
   for(int i=0;i+2<m.t.Length;i+=3)
   {
    var a=m.v[m.t[i]];var e1=m.v[m.t[i+1]]-a;var e2=m.v[m.t[i+2]]-a;var h=Vector3.Cross(d,e2);float det=Vector3.Dot(e1,h);
    if(det>-1e-12f&&det<1e-12f)continue;float inv=1f/det;var s=p-a;float u=Vector3.Dot(s,h)*inv;if(u<0f||u>1f)continue;
    var c=Vector3.Cross(s,e1);float v=Vector3.Dot(d,c)*inv;if(v<0f||u+v>1f)continue;
    float t=Vector3.Dot(e2,c)*inv;if(t>1e-5f&&t<1f-1e-5f&&(best<0f||t<best))best=t;
   }
   return best;
  }
  static float SegEllipsoid(Vector3 a,Vector3 b,Vector3 c,Vector3 r)
  {
   double px=((double)a.x-c.x)/r.x,py=((double)a.y-c.y)/r.y,pz=((double)a.z-c.z)/r.z,dx=((double)b.x-a.x)/r.x,dy=((double)b.y-a.y)/r.y,dz=((double)b.z-a.z)/r.z;
   double A=dx*dx+dy*dy+dz*dz,B=2*(px*dx+py*dy+pz*dz),C=px*px+py*py+pz*pz-1;
   if(A<1e-18)return C<=0?0f:-1f;
   double disc=B*B-4*A*C;if(disc<0)return -1f;
   double s=Math.Sqrt(disc),t0=(-B-s)/(2*A),t1=(-B+s)/(2*A);
   return t1<0||t0>1?-1f:(float)Math.Max(t0,0);
  }
  static float SegCylinder(Vector3 a,Vector3 b,float cx,float cz,float radius,float y0,float y1)
  {
   double px=(double)a.x-cx,pz=(double)a.z-cz,dx=(double)b.x-a.x,dz=(double)b.z-a.z,A=dx*dx+dz*dz;if(A<1e-18)return -1f;
   double B=2*(px*dx+pz*dz),C=px*px+pz*pz-(double)radius*radius,disc=B*B-4*A*C;if(disc<0)return -1f;
   double s=Math.Sqrt(disc);
   foreach(double t in new[]{(-B-s)/(2*A),(-B+s)/(2*A)}){double y=a.y+((double)b.y-a.y)*t;if(t>=0&&t<=1&&y>=y0&&y<=y1)return (float)t;}
   return -1f;
  }

  // null = the line is free; else what stands on it first
  static string Blocker(Cfg cfg,SightWorld308 w,JToken veg,Vector3 eye,Vector3 target)
  {
   float dist=Vector3.Distance(eye,target);if(dist<1e-3f)return null;var dir=(target-eye)/dist;float best=float.PositiveInfinity;string who=null;
   foreach(var h in Physics.RaycastAll(eye,dir,dist,~0,QueryTriggerInteraction.Ignore))
   {
    var c=h.collider;if(c==null||c is CharacterController||skip.Contains(c)||Preview(c.transform)||Own(cfg,c.transform))continue;
    if(w.roots.Contains(c.transform.root.name))continue;   // a building: its drawn triangles are tested below, never its (larger) collider box
    if(h.distance<best){best=h.distance;who="collider "+BuildingAudit308.KeyOf(c.transform);}
   }
   var ray=new Ray(eye,dir);
   foreach(var (r,m) in w.buildings)
   {
    if(!r.bounds.IntersectRay(ray,out float near)||near>dist)continue;
    var inv=r.transform.worldToLocalMatrix;float t=SegMesh(m,inv.MultiplyPoint3x4(eye),inv.MultiplyPoint3x4(target));
    if(t>=0f&&t*dist<best){best=t*dist;who="renderer "+BuildingAudit308.KeyOf(r.transform);}
   }
   float crownFrom=Num(veg,"crown_from");var e2=new Vector2(eye.x,eye.z);var t2=new Vector2(target.x,target.z);
   foreach(var v in w.veg)
   {
    if(SegDist(new Vector2(v.pos.x,v.pos.z),e2,t2)>Mathf.Max(v.radius,v.trunk))continue;
    float t;string part;
    if(v.tree)
    {
     float crown=v.pos.y+crownFrom*v.height,top=v.pos.y+v.height;
     float a=SegCylinder(eye,target,v.pos.x,v.pos.z,v.trunk,v.pos.y,crown),b=SegEllipsoid(eye,target,new Vector3(v.pos.x,(crown+top)*.5f,v.pos.z),new Vector3(v.radius,(top-crown)*.5f,v.radius));
     if(a>=0f&&(b<0f||a<=b)){t=a;part="trunk of ";}else{t=b;part="crown of ";}
    }
    else{t=SegEllipsoid(eye,target,new Vector3(v.pos.x,v.pos.y+v.height*.5f,v.pos.z),new Vector3(v.radius,v.height*.5f,v.radius));part="rock / shrub ";}
    if(t>=0f&&t*dist<best){best=t*dist;who="vegetation row: "+part+v.id+" ("+v.proto+")";}
   }
   return who==null?null:"blocked at "+F(best,"F1")+" m by "+who;
  }

  // ---------- AC-I9: the four lines (check: on the lantern as it stands; probe: shifted to the data's target, before a write) ----------

  static void PlaceLines(Cfg cfg,Scene scene,Transform lantern,Vector3 shift,Action<bool,string> check,Action<string> info)
  {
   var pl=cfg.Lantern["place"];
   if(pl==null||pl.Type!=JTokenType.Object){info("AC-I9 not checked: the data has no lantern.place block (older than 308.roadinn.2)");return;}
   if(!LanternShape(cfg,lantern,out var body,out float rodTop,out string why)){check(false,"AC-I9 "+why);return;}
   body.center+=shift;rodTop+=shift.y;
   var sight=Req(pl,"sight");var veg=Req(sight,"veg");var world=SightWorld(scene,sight);   // what is drawn under the building roots: (ga)'s info line and (ra)
   // (ga) clear of every collider
   float clear=Num(pl,"clear_m"),reach=Num(pl,"report_grow_m");var hits=BoxHits(cfg,body,clear);
   string nearest=GrowRoom(reach,g=>BoxHits(cfg,body,g),out float room,out var who)?F(room)+" m ("+string.Join(", ",who)+")":"none within "+F(reach,"F1")+" m";
   check(hits.Length==0,"AC-I9 (가) lantern body box "+F(body.size.x)+" x "+F(body.size.y)+" x "+F(body.size.z)+" m at "+V(body.center)+" grown by clear_m "+F(clear)+" on every side holds no collider"
    +(hits.Length>0?" - inside: "+string.Join(", ",hits):"")+"; the box can grow "+nearest+" [M]");
   // (ga) info: what is DRAWN next to the body (no collider needed) - reported, never failed
   var drawn=DrawnNear(world,body,reach);
   bool met=GrowRoom(reach,g=>{var half=body.extents+Vector3.one*g;return drawn.Where(t=>TriInBox(t.a,t.b,t.c,body.center,half)).Select(t=>t.key).Distinct().ToArray();},out float drawnRoom,out var drawnWho);
   info("AC-I9 (가) drawn surfaces [M mesh; reported, colliders are the rule]: "+(!met?"no triangle drawn under "+string.Join(" / ",world.roots)+" within "+F(reach,"F1")+" m of the body box"
    :drawnRoom<=0f?"a triangle drawn under "+string.Join(" / ",world.roots)+" reaches INTO the body box ("+string.Join(", ",drawnWho)+") - the lantern stands in something that is drawn"
    :"the body box can grow "+F(drawnRoom)+" m before a triangle drawn under "+string.Join(" / ",world.roots)+" reaches into it ("+string.Join(", ",drawnWho)+")"));
   // (na) the hang
   var hang=Req(pl,"hang");var missing=new List<string>();
   RoofOver(scene,Arr(hang,"roofs").Select(x=>x.Value<string>()),body.center.x,body.center.z,body.max.y,out float under,out float top,missing);
   if(missing.Count>0||float.IsInfinity(under))check(false,"AC-I9 (나) hang: "+(missing.Count>0?"roof key(s) not in the scene: "+string.Join(", ",missing):"no roof mesh over the lantern axis "+V(body.center)+" - nothing to hang from"));
   else
   {
    float gap=under-rodTop,cover=top-rodTop;
    check(gap<=Num(hang,"gap_max_m")+1e-4f&&cover>=Num(hang,"cover_min_m")-1e-4f,"AC-I9 (나) hang: roof over the axis "+F(under,"F3")+" (underside) .. "+F(top,"F3")+" (top) [M mesh]; rod top "+F(rodTop,"F3")+" = "
     +(gap<0f?F(-gap,"F3")+" m above":F(gap,"F3")+" m under")+" the underside (<= "+F(Num(hang,"gap_max_m"))+" under it), "+F(cover,"F3")+" m under the top surface (>= "+F(Num(hang,"cover_min_m"))+"); top cap "+F(under-body.max.y,"F3")+" m under the roof");
   }
   // (da) the door
   var door=Req(pl,"door");float doorGap=float.PositiveInfinity;missing.Clear();
   foreach(var k in Arr(door,"keys").Select(x=>x.Value<string>()))
   {
    var t=BuildingAudit308.Resolve(scene,k);var rs=t!=null?t.GetComponentsInChildren<Renderer>(false):Array.Empty<Renderer>();
    if(rs.Length==0){missing.Add(k);continue;}
    foreach(var r in rs)
    {
     var b=r.bounds;float dx=Mathf.Max(b.min.x-body.max.x,body.min.x-b.max.x,0f),dz=Mathf.Max(b.min.z-body.max.z,body.min.z-b.max.z,0f);
     doorGap=Mathf.Min(doorGap,Mathf.Sqrt(dx*dx+dz*dz));
    }
   }
   check(missing.Count==0&&doorGap>=Num(door,"clear_m")-1e-4f,"AC-I9 (다) door: lantern body to the entry steps "+(float.IsInfinity(doorGap)?"-":F(doorGap))+" m in plan (>= "+F(Num(door,"clear_m"),"F1")+")"+(missing.Count>0?"; not in the scene: "+string.Join(", ",missing):""));
   // (ra) the sight lines
   float eyeM=Num(sight,"eye_m");int need=0,bad=0;var lines=new List<string>();var badIds=new List<string>();
   foreach(var e in Arr(sight,"eyes"))
   {
    string id=Str(e,"id");bool must=Flag(e,"need");if(must)need++;
    if(!Ground(Num(e,"x"),Num(e,"z"),out var g,out _)){if(must){bad++;badIds.Add(id);}lines.Add("AC-I9 sight "+id+": no ground under ("+F(Num(e,"x"),"F1")+", "+F(Num(e,"z"),"F1")+")");continue;}
    var eye=g+Vector3.up*eyeM;string block=Blocker(cfg,world,veg,eye,body.center);
    if(block!=null&&must){bad++;badIds.Add(id);}
    lines.Add("AC-I9 sight "+id+(must?" [need]":" [info]")+" eye "+V(eye)+" -> "+V(body.center)+" "+F(Vector3.Distance(eye,body.center),"F1")+" m: "+(block??"free"));
   }
   check(need>0&&bad==0,"AC-I9 (라) sight (eye "+F(eyeM)+" m; colliders outside "+string.Join(" / ",world.roots)+" AND the "+world.buildings.Count+" meshes drawn under it AND "+world.veg.Count+" vegetation rows of "+world.sheets+" sheet(s)): free from "+(need-bad)+" of "+need+" needed points"
    +(bad>0?" - NOT from "+string.Join(", ",badIds):"")+(world.unreadable>0?"; "+world.unreadable+" mesh(es) could not be read":""));
   foreach(var l in lines)info(l);
  }
 }
}
