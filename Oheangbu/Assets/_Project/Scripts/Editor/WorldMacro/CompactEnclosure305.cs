using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #305 field enclosure, phase 1 (SPEC-WORLD-ENCLOSURE-305 / D305): kit N6 only = dense forest band + hidden collision shell on
 // the EA frontier of W_Demo_Main. Inputs are offline (Tools/Art/enclosure305.py): segments305.json + Out/forest305_candidates.json.
 // Queue calls never open dialogs: refusals come back as "REFUSED ..." strings. check/audit never save.
 //   status                  inputs + whether the active scene carries the Enclosure305 root
 //   build-scene:<path>      backup, rebuild root (Shell/*_col + joint strips E305_J*_col, Obstacles/*_ob*, Forest305_Renderer) + Forest305.asset + meshes
 //   remove-scene:<path>     destroy the root, save (assets stay)
 //   check-scene:<path>      CC push probes (walk / +1.05 / +3.45) on segments, joints and open ends + reserved-site distances -> Out/check-<scene>.txt
 //   audit-scene:<path>      invisible-wall audit + cover density + renderer-less colliders elsewhere -> Out/audit-<scene>.txt
 public static class Enclosure305 { public static string Run(string command)=>CompactRebuildAuthoring.Enclosure305(command); }
 public static partial class CompactRebuildAuthoring
 {
  const string E305="../Art/World/Compact/Rebuild/Enclosure305",O305=E305+"/Out",A305="Assets/_Project/Art/World/Enclosure305",Root305="Enclosure305";
  const string Height305="Assets/_Project/Art/World/Finish297/Surface/height.bytes";
  static readonly string[] Scenes305={"Assets/_Project/Scenes/World/W_Demo_Main.unity","Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity","Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity"};   // main + the two candidates it is promoted from (NavMesh is baked in the #296 candidate)
  // TEST numbers (Spec 설계): resample, offset toward the blocked side, across probe, below/above ground, thickness, chunk length
  const float Step305=1.5f,Offset305=1f,Across305=3f,Below305=1f,Above305=5.5f,Thick305=.6f,Chunk305=48f,Plug305=2.4f,PlugCross305=3.5f;   // Above 4.5 -> 5.5 (check 305: 2 m over Guk reach 3.45)
  // carve boxes (Temporary Exception until the NavMesh rebake)
  const float BoxLen305=12f,BoxThick305=1.2f,BoxBelow305=1.5f,BoxAbove305=5.5f;
  // NavMesh re-baked with the shells and plugs in the #296 candidate (2026-09-30, D305 bake hold lifted): carve boxes are off. Set true
  // only if a scene must run on a NavMesh baked without Enclosure305 (Spec 305 Temporary Exception, now closed).
  const bool Carve305=false;
  // Spec: blocking height above player-side ground >= 4.0 m; forest top-up keeps 3.5 m off existing trees (every kind); 6 m off content
  const float MinBlock305=4f,TreeClear305=3.5f,ContentClear305=6f;
  const int PlacementCap305=50000;

  public static string Enclosure305(string command)
  {
   Directory.CreateDirectory(O305);
   try
   {
    if(command=="status")return Status305();
    if(command.StartsWith("build-scene:"))return Build305(command.Substring(12).Trim());
    if(command.StartsWith("remove-scene:"))return Remove305(command.Substring(13).Trim());
    if(command.StartsWith("check-scene:"))return Check305(command.Substring(12).Trim());
    if(command.StartsWith("audit-scene:"))return Audit305(command.Substring(12).Trim());
   }
   catch(Refuse305 r){return "REFUSED "+r.Message;}
   return "ERROR unknown Enclosure305 command '"+command+"' (status | build-scene:<path> | remove-scene:<path> | check-scene:<path> | audit-scene:<path>)";
  }
  sealed class Refuse305:Exception{public Refuse305(string m):base(m){}}

  // build/remove: Edit mode, whitelisted target, nothing unsaved; the target is then opened Single
  static Scene Open305(string path)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Refuse305("Edit mode only (Play is running)");
   if(!Scenes305.Contains(path))throw new Refuse305("not a #305 target: "+path+" (allowed: "+string.Join(", ",Scenes305)+")");
   for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);if(s.isDirty)throw new Refuse305("scene "+s.path+" has unsaved changes - save or discard them first (the build saves the scene)");}
   return EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
  }
  // check/audit: Edit mode, the target already active and carrying the root (nothing is opened or saved)
  static (Scene scene,Transform root) Active305(string path)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Refuse305("Edit mode only (Play is running)");
   var s=SceneManager.GetActiveScene();if(s.path!=path)throw new Refuse305("active scene is "+s.path+", not "+path);
   var r=s.GetRootGameObjects().FirstOrDefault(g=>g.name==Root305);if(r==null)throw new Refuse305("no "+Root305+" root in "+path+" (run build-scene first)");
   return (s,r.transform);
  }

  // ---------- inputs ----------

  sealed class Seg305{public string id,kit,realm,zone;public bool ea,closed;public int block;public Vector2[] pts;public float length;public string[] ends,notes;}
  [Serializable] class Cand305{public string Id,ClusterId,PrototypeId,kind;public float[] Position,Euler;public float Scale=1,u;}
  [Serializable] class Forest305File{public string version;public float band_m;public string[] prototypes;public Cand305[] candidates;}
  // one resampled station: line point p, travel tangent t, blocked-side normal nb (block * right normal (dz,-dx)), arc s, shell centre q
  struct Station305{public Vector2 p,t,nb,q;public float s;}

  static List<Seg305> Segments305(out string version)
  {
   var d=(Dictionary<string,object>)Json305.Parse(File.ReadAllText(E305+"/segments305.json"));version=JStr305(d,"version");
   var list=new List<Seg305>();
   foreach(var o in (List<object>)d["segments"])
   {
    var s=(Dictionary<string,object>)o;
    var pts=((List<object>)s["points"]).Select(q=>{var a=(List<object>)q;return new Vector2(Convert.ToSingle(a[0],CultureInfo.InvariantCulture),Convert.ToSingle(a[1],CultureInfo.InvariantCulture));}).ToArray();
    list.Add(new Seg305{id=JStr305(s,"id"),kit=JStr305(s,"kit"),realm=JStr305(s,"realm"),zone=JStr305(s,"zone"),ea=JBool305(s,"ea"),closed=JBool305(s,"closed"),
     block=s.TryGetValue("block",out var b)&&b!=null&&Convert.ToDouble(b,CultureInfo.InvariantCulture)<0?-1:1,pts=pts,
     length=s.TryGetValue("length_m",out var l)&&l!=null?Convert.ToSingle(l,CultureInfo.InvariantCulture):0,ends=JStrs305(s,"ends"),notes=JStrs305(s,"notes")});
   }
   return list;
  }
  static List<Seg305> N6Segments305(out string version,out int others){var all=Segments305(out version);var n6=all.Where(s=>s.kit=="N6"&&s.pts.Length>=2).ToList();others=all.Count-n6.Count;return n6;}
  static Forest305File Forest305Input()=>JsonUtility.FromJson<Forest305File>(File.ReadAllText(O305+"/forest305_candidates.json"));
  static string JStr305(Dictionary<string,object> d,string k)=>d.TryGetValue(k,out var v)&&v!=null?Convert.ToString(v,CultureInfo.InvariantCulture):"";
  static bool JBool305(Dictionary<string,object> d,string k)=>d.TryGetValue(k,out var v)&&v is bool b&&b;
  static string[] JStrs305(Dictionary<string,object> d,string k)=>d.TryGetValue(k,out var v)&&v is List<object> l?l.Select(x=>Convert.ToString(x,CultureInfo.InvariantCulture)).ToArray():Array.Empty<string>();

  // minimal JSON reader (nested arrays are outside JsonUtility): object -> Dictionary, array -> List, number -> double
  sealed class Json305
  {
   readonly string s;int i;
   Json305(string s){this.s=s;}
   public static object Parse(string text){var j=new Json305(text.TrimStart('﻿'));var v=j.Value();return v;}
   void W(){while(i<s.Length&&char.IsWhiteSpace(s[i]))i++;}
   bool Word(string w){if(string.CompareOrdinal(s,i,w,0,w.Length)!=0)return false;i+=w.Length;return true;}
   object Value()
   {
    W();char c=s[i];
    if(c=='{'){i++;var d=new Dictionary<string,object>();W();if(s[i]=='}'){i++;return d;}
     while(true){W();var k=Str();W();if(s[i++]!=':')throw new FormatException("':' expected at "+i);d[k]=Value();W();char e=s[i++];if(e=='}')return d;if(e!=',')throw new FormatException("',' expected at "+i);}}
    if(c=='['){i++;var l=new List<object>();W();if(s[i]==']'){i++;return l;}
     while(true){l.Add(Value());W();char e=s[i++];if(e==']')return l;if(e!=',')throw new FormatException("',' expected at "+i);}}
    if(c=='"')return Str();
    if(Word("true"))return true;if(Word("false"))return false;if(Word("null"))return null;
    if(Word("NaN"))return double.NaN;if(Word("Infinity"))return double.PositiveInfinity;if(Word("-Infinity"))return double.NegativeInfinity;
    int st=i;while(i<s.Length&&"+-0123456789.eE".IndexOf(s[i])>=0)i++;
    if(st==i)throw new FormatException("bad JSON value at "+i);
    return double.Parse(s.Substring(st,i-st),NumberStyles.Float,CultureInfo.InvariantCulture);
   }
   string Str()
   {
    i++;var sb=new StringBuilder();
    while(s[i]!='"')
    {
     char c=s[i++];if(c!='\\'){sb.Append(c);continue;}
     char e=s[i++];
     switch(e){case 'n':sb.Append('\n');break;case 't':sb.Append('\t');break;case 'r':sb.Append('\r');break;case 'b':sb.Append('\b');break;case 'f':sb.Append('\f');break;
      case 'u':sb.Append((char)Convert.ToInt32(s.Substring(i,4),16));i+=4;break;default:sb.Append(e);break;}
    }
    i++;return sb.ToString();
   }
  }

  // ---------- line geometry ----------

  static Vector2 At305(Vector2[] P,float[] cum,float s)
  {
   for(int i=1;i<P.Length;i++)if(cum[i]>=s||i==P.Length-1){float d=Mathf.Max(1e-5f,cum[i]-cum[i-1]);return Vector2.Lerp(P[i-1],P[i],Mathf.Clamp01((s-cum[i-1])/d));}
   return P[0];
  }
  // stations every <= 1.5 m (both ends included); tangent from +-0.75 m so corners get a mitred normal
  static Station305[] Stations305(Seg305 g,float step=Step305)
  {
   var P=g.pts;if(g.closed&&P.Length>2&&P[0]!=P[P.Length-1])P=P.Concat(new[]{P[0]}).ToArray();
   var cum=new float[P.Length];for(int i=1;i<P.Length;i++)cum[i]=cum[i-1]+Vector2.Distance(P[i-1],P[i]);float L=cum[cum.Length-1];
   int n=Mathf.Max(1,Mathf.CeilToInt(L/step));var st=new Station305[n+1];var prev=Vector2.up;
   for(int k=0;k<=n;k++)
   {
    float s=L*k/n;var p=At305(P,cum,s);var t=At305(P,cum,Mathf.Min(L,s+.75f))-At305(P,cum,Mathf.Max(0,s-.75f));
    t=t.sqrMagnitude>1e-8f?t.normalized:prev;prev=t;var nb=new Vector2(t.y,-t.x)*g.block;
    st[k]=new Station305{p=p,t=t,nb=nb,q=p+nb*Offset305,s=s};
   }
   return st;
  }
  static float SegDist305(Vector2 p,Vector2 a,Vector2 b){var d=b-a;float t=d.sqrMagnitude<1e-8f?0:Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude);return Vector2.Distance(p,a+d*t);}
  static float LineDist305(Vector2 p,Vector2[] pts){float best=float.PositiveInfinity;for(int i=1;i<pts.Length;i++)best=Mathf.Min(best,SegDist305(p,pts[i-1],pts[i]));return best;}
  static Vector2 Rotate305(Vector2 v,float deg){float r=deg*Mathf.Deg2Rad,c=Mathf.Cos(r),s=Mathf.Sin(r);return new Vector2(v.x*c-v.y*s,v.x*s+v.y*c);}
  static bool Cross305(Vector2 a,Vector2 b,Vector2 c,Vector2 d,out Vector2 x)
  {
   x=default;var r=b-a;var s=d-c;float den=r.x*s.y-r.y*s.x;if(Mathf.Abs(den)<1e-8f)return false;
   var w=c-a;float t=(w.x*s.y-w.y*s.x)/den,u=(w.x*r.y-w.y*r.x)/den;if(t<0||t>1||u<0||u>1)return false;x=a+r*t;return true;
  }

  // ---------- joints ----------

  // two segment ends on one line point (<= 5 cm). Each shell stops 1 m inside its own blocked side, so a convex joint leaves a
  // 0.8-1.4 m opening (review 305): "arc" = a joint strip E305_J<a>_<b>_col on radius Offset305 around the joint point from A's end
  // normal to B's (<= 20 deg a station); "cross" = the stubs already intersect (concave), no mesh; "flush" = ends coincide (< 2 cm).
  // chain = A's last 12 stations (toward the joint) + arc | crossing + B's first 12: one simple blocked-side curve for the probes.
  sealed class Joint305{public string name,kind,note;public Seg305 a,b;public Vector2 p,bis;public float angle,gap;public Station305[] arc,chain;public int k,ka,kb;}
  static Station305[] Toward305(Station305[] st,bool atEnd,int n){var o=atEnd?st.Skip(Mathf.Max(0,st.Length-n)).ToArray():st.Take(n).Reverse().ToArray();return o;}
  static Vector2 EndPoint305(Seg305 g,bool atEnd)=>atEnd?g.pts[g.pts.Length-1]:g.pts[0];
  static string Short305(string id)=>id.StartsWith("E305_")?id.Substring(5):id;
  static List<Joint305> Joints305(List<Seg305> segs,Dictionary<Seg305,Station305[]> stations)
  {
   var ends=new List<(Seg305 g,bool atEnd)>();foreach(var g in segs)if(!g.closed){ends.Add((g,false));ends.Add((g,true));}
   var list=new List<Joint305>();var names=new HashSet<string>();
   for(int i=0;i<ends.Count;i++)for(int j=i+1;j<ends.Count;j++)
   {
    var (ga,ea)=ends[i];var (gb,eb)=ends[j];var pa=EndPoint305(ga,ea);var pb=EndPoint305(gb,eb);if(Vector2.Distance(pa,pb)>.05f)continue;
    var A=Toward305(stations[ga],ea,12);var B=Toward305(stations[gb],eb,12).Reverse().ToArray();
    var J=new Joint305{a=ga,b=gb,p=(pa+pb)*.5f,gap=Vector2.Distance(A[A.Length-1].q,B[0].q)};
    string name="E305_J"+Short305(ga.id)+"_"+Short305(gb.id);for(int n=1;!names.Add(name);n++)name="E305_J"+Short305(ga.id)+"_"+Short305(gb.id)+"_"+n;J.name=name+"_col";
    var nA=A[A.Length-1].nb;var nB=B[0].nb;J.angle=Vector2.SignedAngle(nA,nB);
    var outA=(A[A.Length-1].q-A[Mathf.Max(0,A.Length-2)].q);outA=outA.sqrMagnitude>1e-8f?outA.normalized:A[A.Length-1].t;
    var departB=B.Length>1?(B[1].q-B[0].q):B[0].t;departB=departB.sqrMagnitude>1e-8f?departB.normalized:outA;
    // consistent blocked sides turn with the line (nB = nA rotated by the line's turn); > 90 deg off = the block signs disagree
    float turn=Vector2.SignedAngle(outA,departB);J.note=Mathf.Abs(Mathf.DeltaAngle(J.angle,turn))>90f?"SIDE FLIP: block signs of "+ga.id+"/"+gb.id+" disagree (fix segments305.json)":"";
    // +-180 (hairpin / flip): the sign is numerical noise -> go round the far side of the joint (arc midpoint along A's outward tangent)
    if(Mathf.Abs(J.angle)>175f){if(Vector2.Dot(Rotate305(nA,J.angle*.5f),outA)<0)J.angle=-J.angle;if(J.note=="")J.note="hairpin";}
    J.bis=Rotate305(nA,J.angle*.5f);
    // concave: A's stub runs into B's (search back from the joint) -> trim both at the crossing
    int ci=-1,cj=-1;Vector2 X=default;
    if(J.gap>=.02f)for(int u=A.Length-2;u>=0&&ci<0;u--)for(int v=0;v+1<B.Length;v++)if(Cross305(A[u].q,A[u+1].q,B[v].q,B[v+1].q,out X)){ci=u;cj=v;break;}
    if(J.gap<.02f)
    {
     J.kind="flush";J.arc=Array.Empty<Station305>();J.chain=A.Concat(B.Skip(1)).ToArray();J.k=A.Length-1;J.ka=Mathf.Max(0,J.k-2);J.kb=Mathf.Min(J.chain.Length-1,J.k+2);
    }
    else if(ci>=0)
    {
     J.kind="cross";var nx=(A[ci].nb+B[cj].nb);nx=nx.sqrMagnitude>1e-6f?nx.normalized:J.bis;
     J.arc=Array.Empty<Station305>();J.chain=A.Take(ci+1).Concat(new[]{new Station305{p=J.p,q=X,nb=nx,t=A[ci].t}}).Concat(B.Skip(cj+1)).ToArray();
     J.k=ci+1;J.ka=Mathf.Max(0,ci-1);J.kb=Mathf.Min(J.chain.Length-1,ci+3);
    }
    else
    {
     J.kind="arc";int m=Mathf.Max(1,Mathf.CeilToInt(Mathf.Abs(J.angle)/20f));J.arc=new Station305[m+1];
     for(int w=0;w<=m;w++)
     {
      var dir=Rotate305(nA,J.angle*w/m);var t=Mathf.Abs(J.angle)<1f?outA:Rotate305(dir,90f*Mathf.Sign(J.angle));
      J.arc[w]=new Station305{p=J.p,q=J.p+dir*Offset305,nb=dir,t=t,s=w*Offset305*Mathf.Abs(J.angle)*Mathf.Deg2Rad/m};
     }
     J.chain=A.Concat(J.arc).Concat(B).ToArray();J.k=A.Length+m/2;J.ka=Mathf.Max(0,A.Length-3);J.kb=Mathf.Min(J.chain.Length-1,A.Length+m+3);
    }
    list.Add(J);
   }
   return list;
  }

  // ---------- ground ----------

  // physical ground under xz: y = highest upward-facing (normal.y > .5) hit on terrain colliders (any support if none is terrain);
  // top = highest walkable support of any kind (Support297 rule: rocks, wall tops, decks) and the collider that gave it.
  // Colliders under `ignore` (the #305 root), character controllers and `skip` (actors, player body) never count.
  static bool Ground305(float x,float z,Transform ignore,HashSet<Collider> skip,out float y,out bool terrain)=>Ground305(x,z,ignore,skip,out y,out terrain,out _,out _);
  static bool Ground305(float x,float z,Transform ignore,HashSet<Collider> skip,out float y,out bool terrain,out float top,out Collider topBy)
  {
   float best=float.NegativeInfinity;top=float.NegativeInfinity;topBy=null;
   foreach(var h in Physics.RaycastAll(new Vector3(x,2000f,z),Vector3.down,4000f,~0,QueryTriggerInteraction.Ignore))
   {
    var c=h.collider;if(h.normal.y<=.5f||c is CharacterController)continue;
    if(ignore!=null&&c.transform.IsChildOf(ignore))continue;if(skip!=null&&skip.Contains(c))continue;
    if(h.point.y>top){top=h.point.y;topBy=c;}if(h.point.y>best&&Terrain305(c))best=h.point.y;
   }
   terrain=!float.IsNegativeInfinity(best);y=terrain?best:top;return !float.IsNegativeInfinity(y);
  }
  // terrain = TerrainCollider or a Terrain*/ *Surface* object or parent ("Ground" is not: house_Re_Ground, GroundLitter, LowGroundFill)
  static bool Terrain305(Collider c)
  {
   if(c is TerrainCollider)return true;
   for(var t=c.transform;t!=null;t=t.parent){var n=t.name;if(n.Contains("Terrain")||n.Contains("Surface"))return true;}
   return false;
  }
  // the highest walkable support within [from.y-down, from.y+up] (probe hover / cover checks)
  static bool Support305(Vector3 from,float up,float down,Transform ignore,HashSet<Collider> skip,out float y)
  {
   y=float.NegativeInfinity;
   foreach(var h in Physics.RaycastAll(from+Vector3.up*up,Vector3.down,up+down,~0,QueryTriggerInteraction.Ignore))
   {var c=h.collider;if(h.normal.y<=.5f||c is CharacterController||(ignore!=null&&c.transform.IsChildOf(ignore))||(skip!=null&&skip.Contains(c)))continue;if(h.point.y>y)y=h.point.y;}
   return !float.IsNegativeInfinity(y);
  }
  // encounter actors and the resting player body are gameplay, not ground
  static HashSet<Collider> Skip305(WorldMacroPlaytestSession session)
  {
   var set=new HashSet<Collider>();if(session==null)return set;
   foreach(var a in session.Actors??Array.Empty<Oheangbu.App.Prologue.PrologueEncounter>())if(a!=null)foreach(var c in a.GetComponentsInChildren<Collider>(true))set.Add(c);
   if(session.Walker!=null&&session.Walker.Body!=null)foreach(var c in session.Walker.Body.GetComponentsInChildren<Collider>(true))set.Add(c);
   return set;
  }
  // offline height field of the main scene's FinalSurface (<f4, 1501 x 1001, 4 m, h[z/4, x/4], bilinear) - comparison only
  static Func<float,float,float> Offline305()
  {
   const int H=1501,W=1001;const float C=4f;
   if(!File.Exists(Height305))return null;var bytes=File.ReadAllBytes(Height305);if(bytes.Length!=H*W*4)return null;
   var h=new float[H*W];Buffer.BlockCopy(bytes,0,h,0,bytes.Length);
   return (x,z)=>{float j=Mathf.Clamp(x/C,0,W-1.001f),i=Mathf.Clamp(z/C,0,H-1.001f);int j0=(int)j,i0=(int)i;float fj=j-j0,fi=i-i0;
    return h[i0*W+j0]*(1-fj)*(1-fi)+h[i0*W+j0+1]*fj*(1-fi)+h[(i0+1)*W+j0]*(1-fj)*fi+h[(i0+1)*W+j0+1]*fj*fi;};
  }
  static string Stats305(List<float> v)
  {
   if(v.Count==0)return "n=0";var abs=v.Select(Mathf.Abs).OrderBy(x=>x).ToList();
   return "n="+v.Count+" mean="+v.Average().ToString("F3",CultureInfo.InvariantCulture)+" mean|d|="+abs.Average().ToString("F3",CultureInfo.InvariantCulture)+
          " p95|d|="+abs[Mathf.Clamp(Mathf.CeilToInt(abs.Count*.95f)-1,0,abs.Count-1)].ToString("F3",CultureInfo.InvariantCulture)+" max|d|="+abs[abs.Count-1].ToString("F3",CultureInfo.InvariantCulture);
  }
  static string F305(float v,string f="F2")=>v.ToString(f,CultureInfo.InvariantCulture);

  // 2D bucket grid of world points (XZ)
  sealed class Grid305
  {
   readonly float cell;readonly Dictionary<long,List<Vector3>> map=new Dictionary<long,List<Vector3>>();public int Count;
   public Grid305(float cell){this.cell=cell;}
   static long Key(int x,int z)=>((long)x<<32)^(uint)z;
   public void Add(Vector3 p){long k=Key(Mathf.FloorToInt(p.x/cell),Mathf.FloorToInt(p.z/cell));if(!map.TryGetValue(k,out var l))map[k]=l=new List<Vector3>();l.Add(p);Count++;}
   public IEnumerable<Vector3> Near(Vector2 c,float r)
   {
    int x0=Mathf.FloorToInt((c.x-r)/cell),x1=Mathf.FloorToInt((c.x+r)/cell),z0=Mathf.FloorToInt((c.y-r)/cell),z1=Mathf.FloorToInt((c.y+r)/cell);
    for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++)if(map.TryGetValue(Key(x,z),out var l))foreach(var p in l)if((new Vector2(p.x,p.z)-c).sqrMagnitude<=r*r)yield return p;
   }
   public bool Any(Vector2 c,float r)=>Near(c,r).Any();
  }

  // ---------- status / remove ----------

  static string Status305()
  {
   var sb=new StringBuilder();
   try
   {
    var n6=N6Segments305(out string version,out int others);
    sb.AppendLine("segments305.json "+version+": N6 "+n6.Count+" segments, "+F305(n6.Sum(s=>s.length)/1000f,"F2")+" km (other kits skipped: "+others+"), open ends "+n6.Sum(s=>s.ends.Count(e=>e=="open"))+
                  ", realms "+string.Join(",",n6.GroupBy(s=>s.realm).Select(g=>g.Key+"="+g.Count())));
   }
   catch(Exception e){sb.AppendLine("segments305.json: "+e.Message);}
   try
   {
    var f=Forest305Input();
    sb.AppendLine("forest305_candidates.json "+f.version+": "+(f.candidates?.Length??0)+" candidates (tree "+(f.candidates?.Count(c=>c.kind=="tree")??0)+", shrub "+(f.candidates?.Count(c=>c.kind=="shrub")??0)+"), prototypes "+string.Join(",",f.prototypes??Array.Empty<string>()));
   }
   catch(Exception e){sb.AppendLine("forest305_candidates.json: "+e.Message);}
   sb.AppendLine("offline height field "+(File.Exists(Height305)?"present":"missing")+" ("+Height305+")");
   var sheet=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(A305+"/Forest305.asset");
   sb.AppendLine("Forest305.asset: "+(sheet==null?"absent":sheet.FixedPlacements.Length+" placements, "+sheet.Prototypes.Length+" prototypes"));
   var scene=SceneManager.GetActiveScene();var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==Root305);
   sb.Append("active scene "+scene.path+(scene.isDirty?" (dirty)":"")+(Scenes305.Contains(scene.path)?" [target]":" [not a target]")+": ");
   if(root==null)sb.AppendLine("no "+Root305+" root");
   else
   {
    var shell=root.transform.Find("Shell");var obs=root.transform.Find("Obstacles");var fr=root.transform.Find("Forest305_Renderer");
    sb.AppendLine(Root305+" root: shells "+(shell!=null?shell.childCount:0)+", carve boxes "+(obs!=null?obs.childCount:0)+", forest renderer "+(fr!=null&&fr.GetComponent<CompactRebuildArtRenderer>()!=null?"yes":"no")+", active "+root.activeSelf);
   }
   sb.Append("play="+EditorApplication.isPlaying);
   return sb.ToString();
  }

  static string Remove305(string path)
  {
   var scene=Open305(path);var roots=scene.GetRootGameObjects().Where(g=>g.name==Root305).ToArray();
   if(roots.Length==0)return "nothing to remove: no "+Root305+" root in "+path+" (scene not saved)";
   string backup=Backup305(path);foreach(var g in roots)Object.DestroyImmediate(g);
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return "removed "+roots.Length+" "+Root305+" root(s) from "+path+"; Forest305.asset and shell meshes kept; backup "+backup;
  }
  static string Backup305(string path)
  {
   string dir=O305+"/Before";Directory.CreateDirectory(dir);
   string file=dir+"/"+Path.GetFileNameWithoutExtension(path)+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity";File.Copy(path,file,true);return Path.GetFullPath(file);
  }
 }
}
