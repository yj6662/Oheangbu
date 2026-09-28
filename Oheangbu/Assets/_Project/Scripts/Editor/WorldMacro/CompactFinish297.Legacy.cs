using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 legacy ground pass (SPEC-WORLD-FINISH-297 §2 opening area): #245/#261/#263/#264 content was authored on the pre-#292
 // terrain and #296 kept it in place, so near the new first route houses, carts, fences and a road float 20-95 m in the air
 // and orphaned tree colliders hang 40-50 m up. Units (transforms with a footprint <= 30 m, split from bigger containers):
 //   * visible + floating  -> re-seated: base to the rendered terrain under the footprint (median, 0.12 m embed);
 //   * collider-only + floating -> retired (colliders disabled; the instanced vegetation they belonged to was re-seated in #292);
 //   * draped road meshes that float over > 10 % of their vertices -> retired (the #292 route network replaces them).
 // Content points inside a re-seated footprint move with it. legacy[:dry] reports; originals recorded once; legacy-revert.
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] class LegacyRecord297{public string path;public Vector3 position;public bool active;public string action;public string[] colliders;}
  [Serializable] class LegacyOriginal297{public LegacyRecord297[] units;public string content;}
  // RootCaveAndSinmok263 (the root-cave small dungeon) sits above the new ground as a whole: it needs its own relocation, not a
  // per-object drop, and stays out of this pass (reported as a follow-up).
  static readonly string[] LegacyRoots297={"Village245","CheongrimRoadStories264","CheongrimDetail261"};

  static string Legacy297(string mode)
  {
   RequireClean292();var scene=SceneManager.GetActiveScene();var roots=scene.GetRootGameObjects();var session=Session292();var content=session.Content;
   string file=K297+"/legacy-original.json";var all=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
   if(mode=="revert")
   {
    var o=JsonUtility.FromJson<LegacyOriginal297>(File.ReadAllText(file));
    var byKey=new Dictionary<string,Transform>();foreach(var x in all){byKey[Key297(x)]=x;}
    foreach(var u in o.units){var t=u.path.Contains("@")?(byKey.TryGetValue(u.path,out var hit)?hit:null):all.FirstOrDefault(x=>HierarchyPath(x)==u.path);if(t==null)continue;t.position=u.position;if(u.action=="retire")t.gameObject.SetActive(u.active);
     if(u.action=="retire-colliders")foreach(var c in t.GetComponentsInChildren<Collider>(true))c.enabled=true;}
    if(!string.IsNullOrEmpty(o.content)){File.Copy(K297+"/legacy-content-backup.asset",AssetDatabase.GetAssetPath(content),true);AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(content),ImportAssetOptions.ForceUpdate);}
    Physics.SyncTransforms();Save292();return "legacy ground restored: "+o.units.Length+" units";
   }
   bool dry=mode=="dry";
   float Ground(float x,float z){foreach(var h in Physics.RaycastAll(new Vector3(x,3000,z),Vector3.down,4000,~0,QueryTriggerInteraction.Ignore).OrderByDescending(h=>h.point.y))if(h.collider.transform.root.name.Contains("Terrain"))return h.point.y;return float.NaN;}
   // units
   var units=new List<Transform>();
   void Split(Transform t)
   {
    var rs=t.GetComponentsInChildren<Renderer>(false).Where(Solid297).ToArray();var cs=t.GetComponentsInChildren<Collider>(false).Where(c=>c.enabled).ToArray();
    if(rs.Length==0&&cs.Length==0)return;
    var b=Bounds297(rs,cs);bool road=Draped297(t.name);
    if((b.size.x>30||b.size.z>30)&&!road&&t.childCount>0){foreach(Transform c in t)if(c.gameObject.activeInHierarchy)Split(c);return;}
    units.Add(t);
   }
   foreach(var name in LegacyRoots297){var r=roots.FirstOrDefault(g=>g.name==name);if(r!=null)foreach(Transform c in r.transform)if(c.gameObject.activeInHierarchy)Split(c);}
   var records=new List<LegacyRecord297>();var tally=new SortedDictionary<string,int>();var sb=new StringBuilder();int movedPoints=0,shifted=0;
   var lanes=new List<Vector3>();
   void Lane(IList<Vector3> pts){for(int i=1;i<pts.Count;i++){int k=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(pts[i-1],pts[i])/1.0f));for(int j=0;j<k;j++)lanes.Add(Vector3.Lerp(pts[i-1],pts[i],j/(float)k));}}
   foreach(var r in JsonUtility.FromJson<RoutesFile297>(File.ReadAllText(O297+"/../Architecture296/Generated/routes.json")).routes)Lane(r.points);
   Lane(content.MainPath);Lane(content.BranchPath);
   bool Blocks(Rect box)=>lanes.Any(q=>box.Contains(new Vector2(q.x,q.z)));
   foreach(var u in units)
   {
    var rs=u.GetComponentsInChildren<Renderer>(false).Where(Solid297).ToArray();var cs=u.GetComponentsInChildren<Collider>(false).Where(c=>c.enabled).ToArray();var b=Bounds297(rs,cs);
    var ys=new List<float>();for(int i=0;i<5;i++)for(int j=0;j<5;j++){float y=Ground(Mathf.Lerp(b.min.x,b.max.x,i/4f),Mathf.Lerp(b.min.z,b.max.z,j/4f));if(!float.IsNaN(y))ys.Add(y);}
    if(ys.Count==0)continue;ys.Sort();float hi=ys.Last(),median=ys[ys.Count/2];float gap=b.min.y-hi;
    bool road=Draped297(u.name);
    string action=null;
    if(road)
    {
     // draped road: fraction of its vertices floating > .5 m over the rendered terrain
     int n=0,floating=0;foreach(var mf in u.GetComponentsInChildren<MeshFilter>(false)){var v=mf.sharedMesh!=null?mf.sharedMesh.vertices:Array.Empty<Vector3>();for(int i=0;i<v.Length;i+=Mathf.Max(1,v.Length/400)){var w=mf.transform.TransformPoint(v[i]);float g=Ground(w.x,w.z);if(float.IsNaN(g))continue;n++;if(w.y-g>.5f)floating++;}}
     if(n>0&&floating>n*.10f)action="retire";
     sb.AppendLine("draped "+HierarchyPath(u)+" vertices floating "+floating+"/"+n+(action!=null?" -> retire":" -> keep"));
    }
    else if(gap>1.0f){action=rs.Length>0?"reseat":"retire-colliders";}
    if(action==null)continue;tally[action]=tally.TryGetValue(action,out int k)?k+1:1;
    if(action!="retire-colliders")sb.AppendLine(action+" "+HierarchyPath(u)+" gap="+gap.ToString("F1",CultureInfo.InvariantCulture)+" footprint "+b.size.x.ToString("F0")+"x"+b.size.z.ToString("F0")+" terrain "+ys.First().ToString("F1")+".."+hi.ToString("F1"));
    if(dry)continue;
    var rec=new LegacyRecord297{path=Key297(u),position=u.position,active=u.gameObject.activeSelf,action=action,colliders=Array.Empty<string>()};
    if(action=="retire")u.gameObject.SetActive(false);
    else if(action=="retire-colliders")foreach(var c in cs)c.enabled=false;
    else
    {
     // a solid landing on a walk line (routes, main/branch path) steps aside perpendicular to it, nearest clear side first
     Vector3 side=Vector3.zero;var foot=new Rect(b.min.x-.9f,b.min.z-.9f,b.size.x+1.8f,b.size.z+1.8f);
     if(cs.Length>0&&Blocks(foot))
     {
      var near=lanes.Where(q=>foot.Contains(new Vector2(q.x,q.z))).ToArray();var dirs=new List<Vector3>();
      for(int i=0;i<near.Length-1;i++){var d=near[i+1]-near[i];d.y=0;if(d.sqrMagnitude>1e-4f)dirs.Add(d.normalized);}
      var along=dirs.Count>0?dirs.Aggregate(Vector3.zero,(a,d)=>a+(Vector3.Dot(a,d)<0?-d:d)).normalized:Vector3.right;var perp=Vector3.Cross(Vector3.up,along).normalized;
      foreach(float s in new[]{3f,-3f,5f,-5f,7f,-7f,9f,-9f,12f,-12f,15f,-15f}){var o=perp*s;var box2=new Rect(foot.x+o.x,foot.y+o.z,foot.width,foot.height);if(!Blocks(box2)){side=o;break;}}
      if(side!=Vector3.zero)
      {
       u.position+=side;ys.Clear();for(int i=0;i<5;i++)for(int j=0;j<5;j++){float y=Ground(Mathf.Lerp(b.min.x,b.max.x,i/4f)+side.x,Mathf.Lerp(b.min.z,b.max.z,j/4f)+side.z);if(!float.IsNaN(y))ys.Add(y);}
       ys.Sort();median=ys[ys.Count/2];shifted++;sb.AppendLine("  off the walk line: "+HierarchyPath(u)+" moved "+side.ToString("F1"));
      }
      else sb.AppendLine("  WARN no clear side for "+HierarchyPath(u));
     }
     float dy=median-.12f-b.min.y;u.position+=Vector3.up*dy;
     var box=new Rect(b.min.x-1,b.min.z-1,b.size.x+2,b.size.z+2);
     foreach(var p in content.Points)if(box.Contains(new Vector2(p.Position.x,p.Position.z))&&Mathf.Abs(p.Position.y-b.min.y)<b.size.y+2){p.Position+=side+Vector3.up*dy;movedPoints++;}
    }
    records.Add(rec);
   }
   sb.Insert(0,"legacy units "+units.Count+": "+string.Join(", ",tally.Select(kv=>kv.Key+"="+kv.Value))+(dry?" (dry run)":"")+"; content points moved="+movedPoints+"; stepped off walk lines="+shifted+"\n");
   if(!dry)
   {
    if(!File.Exists(file))
    {
     AssetDatabase.SaveAssets();File.Copy(AssetDatabase.GetAssetPath(content),K297+"/legacy-content-backup.asset",true);
     File.WriteAllText(file,JsonUtility.ToJson(new LegacyOriginal297{units=records.ToArray(),content="backup"},true));
    }
    else{var o=JsonUtility.FromJson<LegacyOriginal297>(File.ReadAllText(file));var known=new HashSet<string>(o.units.Select(x=>x.path));o.units=o.units.Concat(records.Where(r=>!known.Contains(r.path))).ToArray();File.WriteAllText(file,JsonUtility.ToJson(o,true));}
    EditorUtility.SetDirty(content);Physics.SyncTransforms();Save292();
   }
   string text=sb.ToString();File.WriteAllText(K297+"/legacy"+(dry?"-dry":"")+".txt",text);return text.Length>6000?text.Substring(0,6000)+"\n... (full list in legacy.txt)":text;
  }
  // meshes draped over the old terrain as one piece (village roads, #261 combined litter cells): judged per vertex
  static bool Draped297(string n)=>n.Contains("Track")||n.Contains("Loop")||n.Contains("Entrance")||n.Contains("Road")||n.StartsWith("GroundLitter");
  // particles (hearth smoke) report empty bounds at the world origin; they move with their group but never size it
  // unique record key: same-named siblings (fence posts, cart wheels) differ by their sibling-index chain
  static string Key297(Transform t){var idx=new List<int>();for(var x=t;x!=null;x=x.parent)idx.Add(x.GetSiblingIndex());idx.Reverse();return HierarchyPath(t)+"@"+string.Join(".",idx);}
  static bool Solid297(Renderer r)=>r.enabled&&!(r is ParticleSystemRenderer)&&!(r is TrailRenderer)&&!(r is LineRenderer)&&r.bounds.size.sqrMagnitude>1e-6f;
  static Bounds Bounds297(Renderer[] rs,Collider[] cs)
  {
   bool any=false;Bounds b=default;
   foreach(var r in rs){if(!any){b=r.bounds;any=true;}else b.Encapsulate(r.bounds);}
   foreach(var c in cs){if(!any){b=c.bounds;any=true;}else b.Encapsulate(c.bounds);}
   return b;
  }
 }
}
