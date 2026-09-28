using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 lighting diagnostics: isolate one variable at a time (ambient probe, a renderer feature, the camera renderer).
 public static partial class CompactRebuildAuthoring
 {
  // ambient-probe[:update] — SH L0 (DC) of the probe shaders actually sample; with :update, DynamicGI.UpdateEnvironment first
  static string AmbientProbe297(bool update)
  {
   string Dc(SphericalHarmonicsL2 sh)=>"L0=("+sh[0,0].ToString("F4",CultureInfo.InvariantCulture)+","+sh[1,0].ToString("F4",CultureInfo.InvariantCulture)+","+sh[2,0].ToString("F4",CultureInfo.InvariantCulture)+") L1y(idx1)=("+sh[0,1].ToString("F4",CultureInfo.InvariantCulture)+","+sh[1,1].ToString("F4",CultureInfo.InvariantCulture)+","+sh[2,1].ToString("F4",CultureInfo.InvariantCulture)+") L1z="+sh[0,2].ToString("F4",CultureInfo.InvariantCulture)+" L1x="+sh[0,3].ToString("F4",CultureInfo.InvariantCulture)+" eval(up,side,down).r="+Eval297(sh);
   var sb=new StringBuilder();sb.AppendLine("mode="+RenderSettings.ambientMode+" sky="+RenderSettings.ambientSkyColor+" equator="+RenderSettings.ambientEquatorColor+" ground="+RenderSettings.ambientGroundColor);
   sb.AppendLine("probe before: "+Dc(RenderSettings.ambientProbe));
   if(update){DynamicGI.UpdateEnvironment();sb.AppendLine("probe after UpdateEnvironment: "+Dc(RenderSettings.ambientProbe));}
   return sb.ToString();
  }
  static string Eval297(SphericalHarmonicsL2 sh){var dirs=new[]{Vector3.up,Vector3.right,Vector3.down};var c=new Color[3];sh.Evaluate(dirs,c);return string.Join("/",c.Select(x=>"("+x.r.ToString("F3",CultureInfo.InvariantCulture)+","+x.g.ToString("F3",CultureInfo.InvariantCulture)+","+x.b.ToString("F3",CultureInfo.InvariantCulture)+")"));}
  // material-survey:<shader name> — scene materials using a shader, grouped by folder, with renderer counts
  static string MaterialSurvey297(string shader)
  {
   var scene=SceneManager.GetActiveScene();var count=new Dictionary<Material,int>();
   foreach(var r in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)))foreach(var m in r.sharedMaterials)if(m!=null&&m.shader!=null&&m.shader.name==shader)count[m]=count.TryGetValue(m,out int n)?n+1:1;
   var sb=new StringBuilder();sb.AppendLine(count.Count+" materials, "+count.Values.Sum()+" renderer slots");
   foreach(var g in count.GroupBy(k=>Path.GetDirectoryName(AssetDatabase.GetAssetPath(k.Key)).Replace("\\","/")))sb.AppendLine("  "+g.Key+": "+g.Count()+" materials, "+g.Sum(k=>k.Value)+" slots");
   return sb.ToString();
  }
  // pick:<eye>:<target>:<px>,<py> — renderers whose bounds the view ray through a 1920x1080 capture pixel crosses (nearest first)
  static string Pick297(string arg)
  {
   var a=arg.Split(':');var eye=Vec297(a[0]);var target=Vec297(a[1]);var px=a[2].Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray();
   var rot=Quaternion.LookRotation(target-eye);float tan=Mathf.Tan(30*Mathf.Deg2Rad);
   var dir=rot*new Vector3((px[0]/1920f*2-1)*tan*16f/9,(1-px[1]/1080f*2)*tan,1).normalized;var ray=new Ray(eye,dir);
   var hits=new List<(float d,Renderer r)>();
   foreach(var ren in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)){if(!ren.enabled||!ren.gameObject.activeInHierarchy)continue;if(ren.bounds.IntersectRay(ray,out float d))hits.Add((d,ren));}
   return "dir "+dir.ToString("F3")+"\n"+string.Join("\n",hits.OrderBy(h=>h.d).Take(14).Select(h=>h.d.ToString("F0",CultureInfo.InvariantCulture)+"m "+Path297(h.r.transform)+" | "+h.r.bounds.center.ToString("F0")+" size "+h.r.bounds.size.ToString("F0")+" | "+h.r.GetType().Name));
  }
  // rpick:<eye>:<target>:<px>,<py> — physics ray (back faces included) through a capture pixel: every collider hit, nearest first
  static string RayPick297(string arg)
  {
   var a=arg.Split(':');var eye=Vec297(a[0]);var target=Vec297(a[1]);var px=a[2].Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray();
   var rot=Quaternion.LookRotation(target-eye);float tan=Mathf.Tan(30*Mathf.Deg2Rad);
   var dir=rot*new Vector3((px[0]/1920f*2-1)*tan*16f/9,(1-px[1]/1080f*2)*tan,1).normalized;bool back=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=true;
   try{var hits=Physics.RaycastAll(eye,dir,2000,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance).Take(10);
    return string.Join("\n",hits.Select(h=>h.distance.ToString("F1",CultureInfo.InvariantCulture)+"m "+Path297(h.collider.transform)+" n="+h.normal.ToString("F2")+" facing="+(Vector3.Dot(h.normal,dir)<0?"front":"back")+" @"+h.point.ToString("F1")));}
   finally{Physics.queriesHitBackfaces=back;}
  }
  // renderer:<path> — materials per submesh (triangles, world bounds of each submesh) of one renderer
  static string Renderer297(string path)
  {
   var t=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(x=>Path297(x)==path);if(t==null)return "no "+path;
   var r=t.GetComponent<Renderer>();var mf=t.GetComponent<MeshFilter>();var sb=new StringBuilder();sb.AppendLine("mesh "+(mf!=null&&mf.sharedMesh!=null?AssetDatabase.GetAssetPath(mf.sharedMesh)+" sub="+mf.sharedMesh.subMeshCount:"-"));
   var mats=r.sharedMaterials;var v=mf!=null&&mf.sharedMesh!=null?mf.sharedMesh.vertices:null;
   for(int i=0;i<mats.Length;i++)
   {
    string info="";if(v!=null&&i<mf.sharedMesh.subMeshCount){var tri=mf.sharedMesh.GetTriangles(i);if(tri.Length>0){var b=new Bounds(t.TransformPoint(v[tri[0]]),Vector3.zero);foreach(var k in tri)b.Encapsulate(t.TransformPoint(v[k]));info=" tris="+tri.Length/3+" min "+b.min.ToString("F0")+" max "+b.max.ToString("F0");}}
    sb.AppendLine(i+": "+(mats[i]!=null?mats[i].name+" ("+mats[i].shader.name+") "+AssetDatabase.GetAssetPath(mats[i]):"null")+info);
   }
   return sb.ToString();
  }
  // export-mesh:<path>:<frame root path> — a renderer's mesh (all submeshes) as JSON {v,n,t,sub} in the frame root's local space
  // (e.g. `mine`, whose local space is the V4 geometry frame), for the Python dressing tools. Written to Finish297/Export/<name>.json.
  [Serializable] class ExportMesh297{public string name,frame;public float[] v,n;public int[] t,sub;}
  static string ExportMesh297Cmd(string arg)
  {
   var a=arg.Split(':');var all=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
   var t=all.FirstOrDefault(x=>Path297(x)==a[0]);var frame=all.FirstOrDefault(x=>Path297(x)==a[1]);if(t==null||frame==null)return "missing "+arg;
   var mesh=t.GetComponent<MeshFilter>().sharedMesh;var v=mesh.vertices;var n=mesh.normals;
   var lv=v.Select(p=>frame.InverseTransformPoint(t.TransformPoint(p))).ToArray();var ln=n.Select(q=>frame.InverseTransformDirection(t.TransformDirection(q))).ToArray();
   var tris=new List<int>();var sub=new List<int>();for(int s=0;s<mesh.subMeshCount;s++){var tr=mesh.GetTriangles(s);tris.AddRange(tr);sub.Add(tr.Length);}
   var e=new ExportMesh297{name=t.name,frame=a[1],v=lv.SelectMany(p=>new[]{p.x,p.y,p.z}).ToArray(),n=ln.SelectMany(p=>new[]{p.x,p.y,p.z}).ToArray(),t=tris.ToArray(),sub=sub.ToArray()};
   Directory.CreateDirectory(O297+"/Export");string file=O297+"/Export/"+t.name+".json";File.WriteAllText(file,JsonUtility.ToJson(e));
   return file+" v="+v.Length+" t="+tris.Count/3+" frame="+a[1]+" framePos="+frame.position.ToString("F3")+" frameYaw="+frame.eulerAngles.y.ToString("F1");
  }
  // groups:<root path> — each direct child: active, renderer/collider counts, bounds, rendered-terrain min/max under the
  // footprint (5x5 samples), gap = bounds.min.y - terrain max (floating if >> 0), content points within 25 m
  static string Groups297(string path)
  {
   var all=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
   var root=all.FirstOrDefault(t=>Path297(t)==path);if(root==null)return "no "+path;var content=Session292().Content;
   float Ground(float x,float z){foreach(var h in Physics.RaycastAll(new Vector3(x,3000,z),Vector3.down,4000,~0,QueryTriggerInteraction.Ignore).OrderByDescending(h=>h.point.y))if(h.collider.transform.root.name.Contains("Terrain"))return h.point.y;return float.NaN;}
   var sb=new StringBuilder();
   foreach(Transform c in root)
   {
    var rs=c.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled).ToArray();var cs=c.GetComponentsInChildren<Collider>(true).Where(x=>x.enabled).ToArray();
    if(rs.Length==0&&cs.Length==0){sb.AppendLine(c.name+" (empty)");continue;}
    var b=rs.Length>0?rs[0].bounds:cs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);foreach(var x in cs)b.Encapsulate(x.bounds);
    float lo=float.MaxValue,hi=float.MinValue;for(int i=0;i<5;i++)for(int j=0;j<5;j++){float y=Ground(Mathf.Lerp(b.min.x,b.max.x,i/4f),Mathf.Lerp(b.min.z,b.max.z,j/4f));if(float.IsNaN(y))continue;lo=Mathf.Min(lo,y);hi=Mathf.Max(hi,y);}
    var pts=content.Points.Where(p=>Vector2.Distance(new Vector2(p.Position.x,p.Position.z),new Vector2(b.center.x,b.center.z))<25+b.extents.magnitude*.5f).Select(p=>p.Id).ToArray();
    sb.AppendLine(c.name+(c.gameObject.activeInHierarchy?"":" [inactive]")+" r="+rs.Length+" c="+cs.Length+" min "+b.min.ToString("F0")+" max "+b.max.ToString("F0")+" terrain "+lo.ToString("F1")+".."+hi.ToString("F1")+" gap="+(b.min.y-hi).ToString("F1")+(pts.Length>0?" points="+string.Join(",",pts):""));
   }
   return sb.ToString();
  }
  // route-nav:<route id> — #296 exported route samples (1.5 m) without NavMesh within 1.2 m, with the colliders standing there
  [Serializable] class RoutePts297{public string id;public Vector3[] points;}[Serializable] class RoutesFile297{public RoutePts297[] routes;}
  static string RouteNav297(string id)
  {
   var r=JsonUtility.FromJson<RoutesFile297>(File.ReadAllText(O297+"/../Architecture296/Generated/routes.json")).routes.FirstOrDefault(x=>x.id==id);if(r==null)return "no route "+id;
   var sb=new StringBuilder();int n=0,miss=0;
   for(int i=1;i<r.points.Length;i++){int k=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(r.points[i-1],r.points[i])/1.5f));for(int j=0;j<k;j++){var p=Vector3.Lerp(r.points[i-1],r.points[i],j/(float)k);n++;
    if(UnityEngine.AI.NavMesh.SamplePosition(p,out _,1.2f,UnityEngine.AI.NavMesh.AllAreas))continue;miss++;
    var cols=Physics.OverlapSphere(p+Vector3.up*.9f,1.2f,~0,QueryTriggerInteraction.Ignore).Select(c=>Path297(c.transform)).Distinct().Take(4);
    var ground=Physics.Raycast(p+Vector3.up*30,Vector3.down,out var h,60,~0,QueryTriggerInteraction.Ignore)?h.point.y.ToString("F1")+" "+Path297(h.collider.transform):"none";
    sb.AppendLine("miss "+p.ToString("F1")+" ground="+ground+" near=["+string.Join(", ",cols)+"]");}}
   return id+": samples "+n+" missing "+miss+"\n"+sb;
  }
  // urp:<key>=<value>[,...] — A/B switches for performance isolation: shadow=<m>, cascades=<n>, forwardplus=<0|1> (Renderer297),
  // ssao=<0|1>. Inspection only; `lighting` re-applies the contract values.
  static string Urp297(string arg)
  {
   // shadow/cascade switches target the PC asset (Standalone default) only; the Mobile asset is never edited
   var pipe=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");var q=pipe;
   var data=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(A297+"/Renderer297.asset");var sb=new StringBuilder();
   foreach(var kv in arg.Split(',').Select(s=>s.Split('=')))
   {
    string k=kv[0];float v=float.Parse(kv[1],CultureInfo.InvariantCulture);
    foreach(var p in new[]{pipe,q}.Where(p=>p!=null).Distinct())
    {
     if(k=="shadow"){p.shadowDistance=v;EditorUtility.SetDirty(p);}
     if(k=="cascades"){p.shadowCascadeCount=(int)v;EditorUtility.SetDirty(p);}
    }
    if(k=="forwardplus"){var so=new SerializedObject(data);so.FindProperty("m_RenderingMode").intValue=v>0?2:0;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(data);}
    if(k=="ssao"){var f=data.rendererFeatures.FirstOrDefault(x=>x!=null&&x.name=="SSAO297");if(f!=null){f.SetActive(v>0);EditorUtility.SetDirty(f);}}
    sb.Append(k+"="+v+" ");
   }
   AssetDatabase.SaveAssets();return sb+"| PC shadow="+pipe.shadowDistance+" cascades="+pipe.shadowCascadeCount+"; editor quality level="+QualitySettings.names[QualitySettings.GetQualityLevel()];
  }
  // ink-tune:<prop>=<value>,... | ink-tune:reset — set float properties on the candidate 수묵담채 material (reset = shader defaults)
  static string InkTune297(string arg)
  {
   var m=AssetDatabase.LoadAssetAtPath<Material>(A297+"/Materials/M_InkWash297.mat");if(m==null)return "no M_InkWash297";
   if(arg=="reset"){var s=m.shader;for(int i=0;i<s.GetPropertyCount();i++)if(s.GetPropertyType(i)==UnityEngine.Rendering.ShaderPropertyType.Float||s.GetPropertyType(i)==UnityEngine.Rendering.ShaderPropertyType.Range)m.SetFloat(s.GetPropertyName(i),s.GetPropertyDefaultFloatValue(i));else if(s.GetPropertyType(i)==UnityEngine.Rendering.ShaderPropertyType.Vector)m.SetVector(s.GetPropertyName(i),s.GetPropertyDefaultVectorValue(i));}
   else foreach(var kv in arg.Split(',').Select(x=>x.Split('=')))m.SetFloat(kv[0],float.Parse(kv[1],CultureInfo.InvariantCulture));
   EditorUtility.SetDirty(m);AssetDatabase.SaveAssets();
   var sh=m.shader;return string.Join(" ",Enumerable.Range(0,sh.GetPropertyCount()).Where(i=>sh.GetPropertyType(i)!=UnityEngine.Rendering.ShaderPropertyType.Vector).Select(i=>sh.GetPropertyName(i)+"="+m.GetFloat(sh.GetPropertyName(i)).ToString("0.###",CultureInfo.InvariantCulture)))+(ShaderUtil.ShaderHasError(sh)?" SHADER ERROR":"");
  }
  // floaters[:gap] — renderers whose whole bounds sit above (or below) the rendered terrain over their footprint, grouped by 3 path levels
  static string Floaters297(float gap)
  {
   float Ground(float x,float z)
   {
    foreach(var h in Physics.RaycastAll(new Vector3(x,3000,z),Vector3.down,4000,~0,QueryTriggerInteraction.Ignore).OrderByDescending(h=>h.point.y))
     if(h.collider.transform.root.name.Contains("Terrain"))return h.point.y;
    return float.NaN;
   }
   var groups=new Dictionary<string,(int n,float max,Vector3 at,bool up)>();
   foreach(var ren in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
   {
    if(!ren.enabled||!ren.gameObject.activeInHierarchy||ren is ParticleSystemRenderer||ren is TrailRenderer||ren is LineRenderer)continue;
    string root=ren.transform.root.name;if(root.Contains("Terrain")||root.Contains("Sky")||root.Contains("Water")||root.Contains("Capture"))continue;
    var b=ren.bounds;if(b.size.x>200||b.size.z>200)continue;
    float top=float.MinValue,low=float.MaxValue;bool any=false;
    foreach(var p in new[]{new Vector2(b.center.x,b.center.z),new Vector2(b.min.x,b.min.z),new Vector2(b.max.x,b.min.z),new Vector2(b.min.x,b.max.z),new Vector2(b.max.x,b.max.z)})
    {float y=Ground(p.x,p.y);if(float.IsNaN(y))continue;any=true;top=Mathf.Max(top,y);low=Mathf.Min(low,y);}
    if(!any)continue;float above=b.min.y-top,below=low-b.max.y;
    if(above<=gap&&below<=gap)continue;bool up=above>gap;float g=up?above:below;
    var parts=Path297(ren.transform).Split('/');string key=string.Join("/",parts.Take(Math.Min(3,parts.Length-1>0?parts.Length-1:1)))+(up?" [FLOAT]":" [BURIED]");
    groups[key]=groups.TryGetValue(key,out var o)?(o.n+1,Mathf.Max(o.max,g),g>o.max?b.center:o.at,up):(1,g,b.center,up);
   }
   var sb=new StringBuilder();sb.AppendLine(groups.Count+" groups (gap>"+gap+"m)");
   foreach(var kv in groups.OrderByDescending(k=>k.Value.max))sb.AppendLine(kv.Value.max.ToString("F1",CultureInfo.InvariantCulture)+"m x"+kv.Value.n+" "+kv.Key+" @"+kv.Value.at.ToString("F0"));
   File.WriteAllText(O297+"/floaters.txt",sb.ToString());return sb.ToString();
  }
  // tree:<path>[:depth] — children of a scene object with active state, renderer/collider counts, triangles and world bounds
  static string Tree297(string arg)
  {
   var a=arg.Split(':');int depth=a.Length>1?int.Parse(a[1]):1;
   var all=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true));
   var root=all.FirstOrDefault(t=>Path297(t)==a[0]);if(root==null)return "no "+a[0];
   var sb=new StringBuilder();
   void Walk(Transform t,int level)
   {
    var rs=t.GetComponentsInChildren<Renderer>(true);var cs=t.GetComponentsInChildren<Collider>(true);long tris=0;Bounds b=default;bool any=false;
    foreach(var r in rs){var mf=r.GetComponent<MeshFilter>();if(mf!=null&&mf.sharedMesh!=null)for(int s=0;s<mf.sharedMesh.subMeshCount;s++)tris+=(long)mf.sharedMesh.GetIndexCount(s)/3;if(!any){b=r.bounds;any=true;}else b.Encapsulate(r.bounds);}
    sb.AppendLine(new string(' ',level*2)+t.name+(t.gameObject.activeInHierarchy?"":" [inactive]")+" r="+rs.Length+" c="+cs.Length+" tris="+tris+(any?" min "+b.min.ToString("F0")+" max "+b.max.ToString("F0"):""));
    if(level<depth)foreach(Transform c in t)Walk(c,level+1);
   }
   Walk(root,0);return sb.ToString();
  }
  // ceiling:<x0>,<z0>,<x1>,<z1>,<step>,<floorY> — for each grid cell with a floor near floorY, what is first hit straight up
  // (cave shell vs terrain vs nothing). Terrain seen from below is back-face culled, so a terrain "ceiling" shows sky.
  static string Ceiling297(string arg)
  {
   var v=arg.Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray();float step=v[4],fy=v[5];
   var rows=new StringBuilder();var tally=new SortedDictionary<string,int>();
   for(float z=v[3];z>=v[1];z-=step)
   {
    var line=new StringBuilder();
    for(float x=v[0];x<=v[2];x+=step)
    {
     char ch=' ';
     if(Physics.Raycast(new Vector3(x,fy+6,z),Vector3.down,out var floor,12,~0,QueryTriggerInteraction.Ignore))
     {
      var o=floor.point+Vector3.up*1.6f;
      if(Physics.Raycast(o,Vector3.up,out var up,60,~0,QueryTriggerInteraction.Ignore))
      {string root=up.collider.transform.root.name;string k=root=="mine"?Path297(up.collider.transform).Split('/').Skip(1).FirstOrDefault()??"mine":root;tally[k]=tally.TryGetValue(k,out int n)?n+1:1;
       ch=root=="mine"?(up.distance<14?'#':'h'):(root.Contains("Terrain")?'T':'o');}
      else{ch='.';tally["sky"]=tally.TryGetValue("sky",out int n)?n+1:1;}
      if(floor.point.y>fy+4||floor.point.y<fy-4)ch=char.ToLower(ch)=='#'?'+':ch;
     }
     line.Append(ch);
    }
    rows.AppendLine(z.ToString("F0",CultureInfo.InvariantCulture).PadLeft(5)+" "+line);
   }
   return "# shell<14m  h shell higher  T terrain  o other  . sky\n"+string.Join(", ",tally.Select(k=>k.Key+"="+k.Value))+"\n"+rows;
  }
  // heights:<x0>,<z0>,<x1>,<z1>,<step>,<refY> — candidate FinalSurface height minus refY on a grid (metres, rounded)
  static string Heights297(string arg)
  {
   var v=arg.Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray();var field=new Oheangbu.Data.World.CompactWorldSurface(Session292().MountainLayout);var sb=new StringBuilder();
   sb.Append("  z\\x ");for(float x=v[0];x<=v[2];x+=v[4])sb.Append(((int)x%1000).ToString().PadLeft(4));sb.AppendLine();
   for(float z=v[3];z>=v[1];z-=v[4]){sb.Append(z.ToString("F0",CultureInfo.InvariantCulture).PadLeft(5)+" ");for(float x=v[0];x<=v[2];x+=v[4])sb.Append(Mathf.RoundToInt(field.Sample(x,z)-v[5]).ToString().PadLeft(4));sb.AppendLine();}
   return sb.ToString();
  }
  // section:<x0>,<z0>,<x1>,<z1>,<step>,<floorY> — cross-sections along a line: floor, left/right wall distance at 1.2 m and 3 m,
  // and the first hit upward (queriesHitBackfaces on, so a ceiling seen from either side counts; terrain reported separately)
  static string Section297(string arg)
  {
   var v=arg.Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray();var a0=new Vector3(v[0],v[5],v[1]);var a1=new Vector3(v[2],v[5],v[3]);
   var dir=(a1-a0);dir.y=0;float len=dir.magnitude;dir/=len;var side=new Vector3(-dir.z,0,dir.x);bool back=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=true;
   string Hit(Vector3 o,Vector3 d,float max){return Physics.Raycast(o,d,out var h,max,~0,QueryTriggerInteraction.Ignore)?h.distance.ToString("F1",CultureInfo.InvariantCulture)+(h.collider.transform.root.name=="mine"?"":"("+h.collider.transform.root.name.Substring(0,Math.Min(8,h.collider.transform.root.name.Length))+")"):"-";}
   var sb=new StringBuilder();sb.AppendLine("t  floor  L1.2 R1.2  L3 R3  up");
   try
   {
    for(float t=0;t<=len;t+=v[4])
    {
     var p=a0+dir*t;float floor=Physics.Raycast(p+Vector3.up*5,Vector3.down,out var f,10,~0,QueryTriggerInteraction.Ignore)?f.point.y:float.NaN;var o=new Vector3(p.x,float.IsNaN(floor)?v[5]:floor,p.z);
     sb.AppendLine(t.ToString("F0").PadLeft(3)+" "+(float.IsNaN(floor)?"  -  ":floor.ToString("F1",CultureInfo.InvariantCulture))+"  "+Hit(o+Vector3.up*1.2f,-side,20)+" "+Hit(o+Vector3.up*1.2f,side,20)+"  "+Hit(o+Vector3.up*3,-side,20)+" "+Hit(o+Vector3.up*3,side,20)+"  "+Hit(o+Vector3.up*.5f,Vector3.up,60)+"  @"+p.x.ToString("F0")+","+p.z.ToString("F0"));
    }
   }
   finally{Physics.queriesHitBackfaces=back;}
   return sb.ToString();
  }
  // profile:<path>:<x>,<z>,<dx>,<dz>[,<half>] — slice a mesh renderer with the vertical plane through (x,z) normal to (dx,dz);
  // prints the cut segments as (lateral, y) pairs, lateral positive to the right of the direction. Written to Finish297/profile-*.txt.
  static string Profile297(string arg)
  {
   var a=arg.Split(':');var v=a[1].Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray();float half=v.Length>4?v[4]:16;
   var all=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true));
   var t0=all.FirstOrDefault(t=>Path297(t)==a[0]);if(t0==null)return "no "+a[0];var mf=t0.GetComponent<MeshFilter>();if(mf==null||mf.sharedMesh==null)return "no mesh";
   var n=new Vector3(v[2],0,v[3]).normalized;var right=new Vector3(n.z,0,-n.x);var o=new Vector3(v[0],0,v[1]);
   var mesh=mf.sharedMesh;var verts=mesh.vertices.Select(p=>t0.TransformPoint(p)).ToArray();var tris=mesh.triangles;var sb=new StringBuilder();int count=0;
   for(int i=0;i<tris.Length;i+=3)
   {
    var p=new[]{verts[tris[i]],verts[tris[i+1]],verts[tris[i+2]]};var d=p.Select(q=>Vector3.Dot(q-o,n)).ToArray();var cut=new List<Vector3>();
    for(int e=0;e<3;e++){int f=(e+1)%3;if((d[e]<0)!=(d[f]<0)){float s=d[e]/(d[e]-d[f]);cut.Add(Vector3.Lerp(p[e],p[f],s));}}
    if(cut.Count!=2)continue;var l0=Vector3.Dot(cut[0]-o,right);var l1=Vector3.Dot(cut[1]-o,right);if(Mathf.Abs(l0)>half&&Mathf.Abs(l1)>half)continue;
    sb.AppendLine(l0.ToString("F2",CultureInfo.InvariantCulture)+" "+cut[0].y.ToString("F2",CultureInfo.InvariantCulture)+" "+l1.ToString("F2",CultureInfo.InvariantCulture)+" "+cut[1].y.ToString("F2",CultureInfo.InvariantCulture));count++;
   }
   string file=O297+"/profile-"+((int)v[0])+"_"+((int)v[1])+".txt";File.WriteAllText(file,sb.ToString());return count+" segments -> "+file;
  }
  // feature297:<name>:<0|1> — toggle one feature of the candidate renderer
  static string Feature297(string arg)
  {
   var a=arg.Split(':');var data=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(A297+"/Renderer297.asset");
   var f=data.rendererFeatures.FirstOrDefault(x=>x!=null&&x.name==a[0]);if(f==null)return "no feature "+a[0]+"; have "+string.Join(",",data.rendererFeatures.Select(x=>x?.name));
   f.SetActive(a[1]=="1");EditorUtility.SetDirty(f);EditorUtility.SetDirty(data);AssetDatabase.SaveAssets();return a[0]+" active="+f.isActive;
  }
  // camera-renderer:<index> — renderer index of every game camera in the scene (reversible: run with the old index)
  static string CameraRenderer297(int index)
  {
   var scene=SceneManager.GetActiveScene();var sb=new StringBuilder();
   foreach(var c in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).Where(c=>c.cameraType==CameraType.Game))
   {var d=c.GetUniversalAdditionalCameraData();int before=new SerializedObject(d).FindProperty("m_RendererIndex").intValue;if(before>=3){d.SetRenderer(index);EditorUtility.SetDirty(d);}sb.AppendLine(Path297(c.transform)+": "+before+" -> "+new SerializedObject(d).FindProperty("m_RendererIndex").intValue);}
   Save292();return sb.ToString();
  }
  // shaders-near:<x>,<y>,<z>,<r> — which shaders/materials the renderers in a sphere use, with support/error state
  static string ShadersNear297(string arg)
  {
   var v=arg.Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray();var c=new Vector3(v[0],v[1],v[2]);float r=v[3];
   var rows=new SortedDictionary<string,int>();
   foreach(var ren in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
   {
    if(!ren.enabled||!ren.gameObject.activeInHierarchy||ren.bounds.SqrDistance(c)>r*r)continue;
    foreach(var m in ren.sharedMaterials)
    {
     string key=m==null?"<null material> on "+Path297(ren.transform):(m.shader==null?"<null shader> "+m.name:m.shader.name+(m.shader.isSupported?"":" UNSUPPORTED")+(ShaderUtil.ShaderHasError(m.shader)?" ERROR":"")+" passes="+m.passCount+" | "+m.name);
     rows[key]=rows.TryGetValue(key,out int n)?n+1:1;
    }
   }
   return string.Join("\n",rows.Select(k=>k.Value+"x "+k.Key));
  }
 }
}
