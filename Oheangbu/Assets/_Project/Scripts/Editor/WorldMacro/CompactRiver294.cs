using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
using Dress294=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string A294=A292+"/River294",O294="../Art/World/Compact/Rebuild/River294";
  sealed class RiverRow294 {public Vector2 P,Side;public float Y,S,Left,Right,Flow,Grade;}
  static Vector2 Along294(CompactWorldLayoutSO.Drainage river,float fraction)
  {
   var p=river.Centreline;float total=0;for(int i=1;i<p.Length;i++)total+=Vector2.Distance(p[i-1],p[i]);float remaining=total*fraction;
   for(int i=1;i<p.Length;i++){float d=Vector2.Distance(p[i-1],p[i]);if(remaining<=d)return Vector2.Lerp(p[i-1],p[i],remaining/d);remaining-=d;}return p.Last();
  }
  static List<RiverRow294> RiverRows294(CompactWorldLayoutSO.Drainage river,CompactWorldSurface field)
  {
   var points=new List<Vector2>();var control=river.Centreline;
   for(int i=0;i<control.Length-1;i++)
   {
    Vector2 a=control[Mathf.Max(0,i-1)],b=control[i],c=control[i+1],d=control[Mathf.Min(control.Length-1,i+2)];
    int count=Mathf.CeilToInt(Vector2.Distance(b,c)/3);
    for(int k=0;k<count;k++){float t=k/(float)count;var curve=.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);
     // Rounded bends remain inside the original channel corridor.
     points.Add(Vector2.Lerp(b,c,t)+Vector2.ClampMagnitude(curve-Vector2.Lerp(b,c,t),river.HalfWidth*.7f));}
   }
   points.Add(control.Last());
   var offsets=new float[points.Count];
   for(int i=0;i<points.Count;i++)
   {
    var tangent=(points[Mathf.Min(i+1,points.Count-1)]-points[Mathf.Max(0,i-1)]).normalized;var side=new Vector2(-tangent.y,tangent.x);
    float best=float.PositiveInfinity;
    for(float u=-river.HalfWidth;u<=river.HalfWidth;u+=1)
    {var p=points[i]+side*u;float cost=field.Sample(p.x,p.y)+u*u*.012f;if(cost<best){best=cost;offsets[i]=u;}}
   }
   var shifted=new Vector2[points.Count];
   for(int i=0;i<points.Count;i++)
   {
    var tangent=(points[Mathf.Min(i+1,points.Count-1)]-points[Mathf.Max(0,i-1)]).normalized;float offset=0,weight=0;
    for(int k=-6;k<=6;k++){float w=7-Mathf.Abs(k);offset+=offsets[Mathf.Clamp(i+k,0,points.Count-1)]*w;weight+=w;}
    shifted[i]=points[i]+new Vector2(-tangent.y,tangent.x)*(offset/weight);
   }
   var rows=new List<RiverRow294>();float length=0;
   for(int i=0;i<shifted.Length;i++)
   {
    var p=shifted[i];if(i>0)length+=Vector2.Distance(p,shifted[i-1]);
    var tangent=(shifted[Mathf.Min(i+1,shifted.Length-1)]-shifted[Mathf.Max(0,i-1)]).normalized;var side=new Vector2(-tangent.y,tangent.x);
    float a=field.Sample(p.x-tangent.x*9,p.y-tangent.y*9),b=field.Sample(p.x+tangent.x*9,p.y+tangent.y*9),grade=(b-a)/18;
    float depth=Mathf.Lerp(.88f,.24f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.015f,.22f,Mathf.Abs(grade))));
    float y=field.Sample(p.x,p.y)+depth;
    float Width(int sign)
    {
     float limit=river.HalfWidth*(.95f+.22f*Mathf.Sin(length*.027f+sign*1.7f)+.13f*Mathf.Sin(length*.083f));
     float previous=0;
     for(float u=.4f;u<limit;u+=.4f)
     {var at=p+side*u*sign;if(field.Sample(at.x,at.y)>=y-.015f){float low=previous,high=u;for(int j=0;j<7;j++){float mid=(low+high)*.5f;at=p+side*mid*sign;if(field.Sample(at.x,at.y)<y-.015f)low=mid;else high=mid;}return Mathf.Max(.12f,(low+high)*.5f);}previous=u;}
     return Mathf.Max(.12f,limit);
    }
    rows.Add(new RiverRow294{P=p,Side=side,Y=y,S=length,Left=Width(-1),Right=Width(1),Flow=-Mathf.Clamp(grade*40,-1,1),Grade=Mathf.Abs(grade)});
   }
   return rows;
  }
  static Material RiverMaterial294(string name,string shader)
  {var found=Shader.Find(shader);if(found==null)throw new Exception("Missing shader: "+shader);var m=Asset292("River294/Materials/"+name+".mat",()=>new Material(found));m.shader=found;m.enableInstancing=true;EditorUtility.SetDirty(m);return m;}
  static bool RiverClear294(CompactWorldLayoutSO layout,Vector2 p,float margin)
  {
   if(p.x<4||p.x>3996||p.y<4||p.y>5996)return false;
   if(layout.Places.Any(x=>Vector2.Distance(x.XZ,p)<Mathf.Max(14,x.GroundRadius)+margin))return false;
   foreach(var mountain in layout.Mountains)
    foreach(var q in mountain.MainPath)if(Vector2.Distance(new Vector2(q.x,q.z),p)<7+margin)return false;
   return true;
  }
  static string Rivers294()
  {
   RequireClean292();Directory.CreateDirectory(O294);var session=Session292();var layout=session.MountainLayout;var field=new CompactWorldSurface(layout);
   var old=session.gameObject.scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Reworld292_Water");if(old==null)throw new Exception("292 water root required");
   var previous=GameObject.Find("River294");if(previous!=null)Object.DestroyImmediate(previous);
   var root=new GameObject("River294");
   var waterMat=RiverMaterial294("River","Oheangbu/Reworld292/River294");
   waterMat.SetTexture("_Bed",Tex293("brown_mud_rocks_01","diff"));
   waterMat.SetColor("_Sky",new Color(.46f,.50f,.46f));waterMat.SetColor("_Deep",new Color(.055f,.08f,.069f));
   var rockMat=RiverMaterial294("WetBoulder","Oheangbu/Reworld293/HighlandGranite");
   rockMat.CopyPropertiesFromMaterial(AssetDatabase.LoadAssetAtPath<Material>(S293+"/Materials/Hyeongang_Granite.mat"));
   rockMat.SetVector("_Value293",new Vector4(.23f,1.15f,0,0));rockMat.SetFloat("_WorldScale",.7f);rockMat.SetFloat("_MossAmount",.45f);
   var source=Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None).First(x=>x.Sheet!=null&&x.Sheet.FixedPlacements.Length>50000);
   var sheet=Asset292("River294/Dressing.asset",()=>ScriptableObject.CreateInstance<Dress294>());
   var protos=new List<Dress294.Prototype>();
   foreach(string slug in new[]{"namaqualand_boulder_02","namaqualand_boulder_04","namaqualand_cliff_02"})
   {
    var meshes=Enumerable.Range(0,3).Select(l=>AssetDatabase.LoadAssetAtPath<Mesh>(S293+"/Outcrops/"+slug+"_LOD"+l+".asset")??Json293(V293+"/Outcrops/"+slug+"_LOD"+l+".json",A294+"/SourceMeshes/"+slug+"_LOD"+l+".asset")).ToArray();
    if(meshes.Any(m=>m==null))throw new Exception("Missing CC0 mesh "+slug);
    float unit=2.6f/Mathf.Max(meshes[0].bounds.size.x,meshes[0].bounds.size.y,meshes[0].bounds.size.z);
    protos.Add(new Dress294.Prototype{Id="River294_"+slug,Category=Dress294.Kind.Rock,Size=meshes[0].bounds.size*unit,SourcePath=S293+"/Outcrops/"+slug+"_LOD0.asset",Lods=meshes.Select(m=>new Dress294.Level{Parts=new[]{new Dress294.Part{Mesh=m,Material=rockMat,Local=Matrix4x4.Scale(Vector3.one*unit)}}}).ToArray()});
   }
   foreach(string id in new[]{"Hyeongang_SM_PhragmitesAustralis_1","Hyeongang_SM_PhragmitesAustralis_2","Hyeongang_SM_Deparia_1","Hyeongang_SM_Salixpierotii_Summer_1"})
    protos.Add(source.Sheet.Prototypes.First(x=>x.Id==id));
   sheet.Prototypes=protos.ToArray();sheet.FixedPlacements=Array.Empty<Dress294.FixedPlacement>();sheet.TreeNear=55;sheet.TreeMiddle=130;sheet.ForestDistance=1200;sheet.ShrubDistance=180;sheet.GrassDistance=100;
   var placements=new List<Dress294.FixedPlacement>();var triangles=new List<WorldTerrainQuery.WaterTriangle>();var report=new List<string>();int chunks=0;var rng=new System.Random(294);
   const int bankW=2001,bankH=3001;var bankPixels=new Color32[bankW*bankH];
   var mask=layout.SurfaceDistribution;float R(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
   bool Clear(Vector2 p)=>RiverClear294(layout,p,2)&&mask.GetPixelBilinear(p.x/4000,p.y/6000).a<.04f;
   void Add(string id,Vector2 p,float scale,float sink,string tag)
   {if(!Clear(p)||field.Normal(p.x,p.y).y<.65f)return;placements.Add(new Dress294.FixedPlacement{Id="river294_"+tag+"_"+placements.Count,PrototypeId=id,Position=new Vector3(p.x,field.Sample(p.x,p.y)-sink,p.y),Euler=new Vector3(0,R(0,360),0),Scale=scale});}
   foreach(var river in layout.Drainages)
   {
    var rows=RiverRows294(river,field);int before=triangles.Count;
    for(int start=0;start<rows.Count-1;start+=40)
    {
     int end=Mathf.Min(rows.Count-1,start+40);var vertices=new List<Vector3>();var uv=new List<Vector2>();var flow=new List<Vector2>();var colors=new List<Color>();var indices=new List<int>();
     for(int i=start;i<=end;i++)
     {
      var row=rows[i];for(int j=0;j<9;j++)
      {float t=j/8f,u=t<.5f?Mathf.Lerp(-row.Left,0,t*2):Mathf.Lerp(0,row.Right,(t-.5f)*2);var p=row.P+row.Side*u;float depth=Mathf.Max(0,row.Y-field.Sample(p.x,p.y));vertices.Add(new Vector3(p.x,row.Y,p.y));uv.Add(new Vector2(u,row.S));flow.Add(new Vector2(row.Flow,row.Grade));colors.Add(new Color(Mathf.Clamp01(depth/1.5f),Mathf.Clamp01(row.Grade*7),Mathf.Min(u+row.Left,row.Right-u),1));}
      if(i>start)for(int j=0;j<8;j++){int a=(i-start-1)*9+j,b=(i-start)*9+j;indices.AddRange(new[]{a,a+1,b,a+1,b+1,b});}
     }
     var mesh=Mesh294(river.Id+"_Water_"+start,vertices,uv,flow,colors,indices);var go=MeshObject278(mesh.name,mesh,waterMat,root.transform,false);go.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
     for(int k=0;k<indices.Count;k+=3)triangles.Add(new WorldTerrainQuery.WaterTriangle{A=vertices[indices[k]],B=vertices[indices[k+1]],C=vertices[indices[k+2]]});chunks++;
    }
    // Paint a continuous material mask onto the existing ground. Coplanar bank ribbons
    // cannot follow its 4m triangle topology and cause depth fighting at walking height.
    foreach(var row in rows)foreach(int sign in new[]{-1,1})
    {
     float width=sign<0?row.Left:row.Right;var shore=row.P+row.Side*sign*width;
     float spread=4.6f+1.5f*Mathf.Sin(row.S*.033f+sign);
     int x0=Mathf.Max(0,Mathf.FloorToInt((shore.x-spread)/2)),x1=Mathf.Min(bankW-1,Mathf.CeilToInt((shore.x+spread)/2));
     int z0=Mathf.Max(0,Mathf.FloorToInt((shore.y-spread)/2)),z1=Mathf.Min(bankH-1,Mathf.CeilToInt((shore.y+spread)/2));
     for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
     {
      var p=new Vector2(x*2,z*2);if(!Clear(p))continue;float weight=1-Mathf.SmoothStep(.1f,1,Vector2.Distance(p,shore)/spread);
      int index=z*bankW+x;byte strength=(byte)Mathf.RoundToInt(weight*255);if(strength<=bankPixels[index].r)continue;
      float wet=Mathf.Clamp01((row.Y+.7f-field.Sample(p.x,p.y))/1.3f);bankPixels[index]=new Color32(strength,(byte)Mathf.RoundToInt(wet*weight*255),0,255);
     }
    }
    for(int i=2;i<rows.Count-2;i+=3)
    {
     var row=rows[i];float clump=Mathf.PerlinNoise(row.P.x*.022f+9,row.P.y*.022f);
     if(clump<.34f)continue;
     foreach(int sign in new[]{-1,1})
     {
      float width=sign<0?row.Left:row.Right;var bank=row.P+row.Side*sign*width;
      if(rng.NextDouble()<.72)for(int k=0;k<rng.Next(2,5);k++)Add(protos[rng.Next(3)].Id,bank+row.Side*sign*R(-1.1f,2)+new Vector2(R(-2,2),R(-2,2)),R(.24f,.95f),R(.08f,.23f),"rock");
      if(row.Grade<.17f&&rng.NextDouble()<.75)
       for(int k=0;k<rng.Next(5,12);k++){var p=bank+row.Side*sign*R(.5f,4.8f)+new Vector2(R(-2,2),R(-2,2));Add(protos[3+rng.Next(3)].Id,p,R(.5f,.95f),.03f,"bankplant");}
      if(row.Grade<.075f&&rng.NextDouble()<.03)Add(protos[6].Id,bank+row.Side*sign*R(5,9),R(.6f,.95f),.07f,"willow");
     }
    }
    report.Add(river.Id+": rows="+rows.Count+" water triangles="+(triangles.Count-before)+" width="+rows.Min(r=>r.Left+r.Right).ToString("F2")+".."+rows.Max(r=>r.Left+r.Right).ToString("F2")+"m. Local downhill flow only; global drainage topology retained.");
   }
   var bankTexture=Asset292("River294/BankMask.asset",()=>new Texture2D(bankW,bankH,TextureFormat.RGBA32,true,true));
   bankTexture.SetPixels32(bankPixels);bankTexture.Apply(true,false);bankTexture.wrapMode=TextureWrapMode.Clamp;bankTexture.filterMode=FilterMode.Bilinear;EditorUtility.SetDirty(bankTexture);
   var ground=AssetDatabase.LoadAssetAtPath<Material>(A292+"/Materials/KoreanGround.mat");
   ground.SetTexture("_BankMask294",bankTexture);ground.SetTexture("_BankGravel294",Tex293("rocks_ground_06","diff"));ground.SetTexture("_BankMud294",Tex293("brown_mud_rocks_01","diff"));ground.SetFloat("_BankStrength294",1);EditorUtility.SetDirty(ground);
   sheet.FixedPlacements=placements.ToArray();EditorUtility.SetDirty(sheet);
   var art=root.AddComponent<CompactRebuildArtRenderer>();art.Sheet=sheet;art.Observer=source.Observer;art.Contacts=source.Contacts;art.Invalidate();
   foreach(var query in session.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldTerrainQuery>(true))){query.Water=triangles.ToArray();query.Reindex();EditorUtility.SetDirty(query);}
   // Preserve the authored population asset. The scene uses a private copy with submerged vegetation omitted.
   var original=AssetDatabase.LoadAssetAtPath<Dress294>(A292+"/Data/41ed54300d9ef754a8e74defc2ab189a_MountainVegetation.asset");
   var dry=Asset292("River294/DryLandscape.asset",()=>ScriptableObject.CreateInstance<Dress294>());EditorUtility.CopySerialized(original,dry);
   var terrainQuery=session.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldTerrainQuery>(true)).First();
   var vegetation=new HashSet<string>(original.Prototypes.Where(p=>p.Category==Dress294.Kind.Tree||p.Category==Dress294.Kind.Shrub||p.Category==Dress294.Kind.Grass).Select(p=>p.Id));
   dry.FixedPlacements=original.FixedPlacements.Where(p=>!vegetation.Contains(p.PrototypeId)||!terrainQuery.TryWaterHeight(p.Position,out float y)||y-p.Position.y<.12f).ToArray();
   source.Sheet=dry;source.Invalidate();EditorUtility.SetDirty(dry);report.Add("Submerged old vegetation omitted from scene-only copy="+(original.FixedPlacements.Length-dry.FixedPlacements.Length)+"; original sheet unchanged.");
   old.SetActive(false);Save292();report.Add("Water chunks="+chunks+", bank material mask=2001x3001 (no overlay meshes)"+", instanced CC0 rocks / existing plants="+placements.Count+". No new colliders; old water retained inactive.");File.WriteAllLines(O294+"/build.txt",report);return string.Join("\n",report);
  }
  static Mesh Mesh294(string name,List<Vector3> vertices,List<Vector2> uv,List<Vector2> flow,List<Color> colors,List<int> triangles)
  {var mesh=Asset292("River294/Meshes/"+name+".asset",()=>new Mesh());mesh.Clear();mesh.name=name;mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetUVs(1,flow);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);return mesh;}
  static string CaptureRiver294(string arg)
  {
   var args=arg.Split(':');int view=int.Parse(args[0]);string stage=args.Length>1?args[1]:"after";if(stage!="before"&&stage!="after")throw new ArgumentException("before/after required");
   var layout=Session292().MountainLayout;var field=new CompactWorldSurface(layout);int ri=view<6?view/2:view-6;var river=layout.Drainages[ri];float f=ri==0?.36f:ri==1?.30f:.65f;
   var centre=Along294(river,f);var ahead=Along294(river,f+.018f);var forward=(ahead-centre).normalized;var side=new Vector2(-forward.y,forward.x);var target=new Vector3(centre.x,field.Sample(centre.x,centre.y)+.5f,centre.y);
   Vector3 eye;
   if(view>=6){var rows=RiverRows294(river,field);var row=rows.OrderBy(r=>(r.P-centre).sqrMagnitude).First();var p=row.P+row.Side*(row.Right+1.8f)-forward*5;eye=new Vector3(p.x,Mathf.Max(field.Sample(p.x,p.y)+1.7f,row.Y+1.7f),p.y);target=new Vector3(row.P.x+forward.x*12,row.Y+.45f,row.P.y+forward.y*12);}
   else if(view%2==0){var p=centre+side*(river.HalfWidth+8)-forward*17;eye=new Vector3(p.x,Mathf.Max(field.Sample(p.x,p.y)+2.5f,target.y+4),p.y);}
   else eye=target+new Vector3(side.x*80-forward.x*60,75,side.y*80-forward.y*60);
   string folder=O294+"/"+stage;Directory.CreateDirectory(folder);eye293=eye;target293=target;output293=folder+"/river-"+view+".png";
   var roots=Session292().gameObject.scene.GetRootGameObjects();var current=roots.FirstOrDefault(g=>g.name=="River294");var old=roots.FirstOrDefault(g=>g.name=="Reworld292_Water");
   bool active=current!=null&&current.activeSelf,oldActive=old!=null&&old.activeSelf;var landscape=Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None).First(a=>a.Sheet!=null&&a.Sheet.FixedPlacements.Length>50000);var previousSheet=landscape.Sheet;
   var ground=AssetDatabase.LoadAssetAtPath<Material>(A292+"/Materials/KoreanGround.mat");float bankStrength=ground.GetFloat("_BankStrength294");
   try{if(stage=="before")ground.SetFloat("_BankStrength294",0);if(stage=="before"&&current!=null){current.SetActive(false);old.SetActive(true);landscape.Sheet=AssetDatabase.LoadAssetAtPath<Dress294>(A292+"/Data/41ed54300d9ef754a8e74defc2ab189a_MountainVegetation.asset");landscape.Invalidate();}Capture292(6);return output293;}
   finally{ground.SetFloat("_BankStrength294",bankStrength);if(current!=null)current.SetActive(active);if(old!=null)old.SetActive(oldActive);landscape.Sheet=previousSheet;landscape.Invalidate();eye293=null;target293=null;output293=null;}
  }
  static string CheckRiver294()
  {
   var session=Session292();var root=GameObject.Find("River294");if(root==null)throw new Exception("Build River294 first");var field=new CompactWorldSurface(session.MountainLayout);var lines=new List<string>();
   void C(bool ok,string text)=>lines.Add((ok?"PASS ":"FAIL ")+text);
   var meshes=root.GetComponentsInChildren<MeshFilter>();C(meshes.Length>0,"water/bank chunks exist");
   C(meshes.All(f=>f.sharedMesh.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z))),"finite vertices");
   C(meshes.All(f=>f.sharedMesh.triangles.All(i=>i>=0&&i<f.sharedMesh.vertexCount)),"valid triangle indices");
   C(root.GetComponentsInChildren<Collider>().Length==0,"decorative water/banks introduce no solid colliders");
   C(root.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader!=null&&m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader))),"candidate shaders compile");
   var rendered=new List<WorldTerrainQuery.WaterTriangle>();
   foreach(var filter in meshes.Where(f=>f.name.Contains("_Water_"))){var v=filter.sharedMesh.vertices;var t=filter.sharedMesh.triangles;for(int i=0;i<t.Length;i+=3)rendered.Add(new WorldTerrainQuery.WaterTriangle{A=v[t[i]],B=v[t[i+1]],C=v[t[i+2]]});}
   foreach(var query in session.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldTerrainQuery>(true)))
   {
    C(query.Water.Length==rendered.Count&&query.Water.Zip(rendered,(a,b)=>a.A==b.A&&a.B==b.B&&a.C==b.C).All(x=>x),"query triangles exactly match rendered water: "+rendered.Count);
    int misses=0;for(int i=0;i<rendered.Count;i+=13){var t=rendered[i];var p=(t.A+t.B+t.C)/3;if(!query.TryWaterHeight(p,out float y)||y<p.y-.002f)misses++;}C(misses==0,"query covers sampled water faces; misses="+misses);
   }
   var art=root.GetComponent<CompactRebuildArtRenderer>();var positions=art.Sheet.FixedPlacements;C(positions.All(p=>RiverClear294(session.MountainLayout,new Vector2(p.Position.x,p.Position.z),2)),"bank props clear settlements and mountain routes");
   C(positions.All(p=>Mathf.Abs(p.Position.y-field.Sample(p.Position.x,p.Position.z))<.25f),"bank props rooted / partly buried within 0.25m");
   C(positions.Select(p=>p.Id).Distinct().Count()==positions.Length,"unique placement IDs");
   var landscape=Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None).First(a=>a.Sheet!=null&&a.Sheet.FixedPlacements.Length>50000);
   var cam=Camera.main;int mismatch=0;var probe=new GameObject("River294_CheckCamera"){hideFlags=HideFlags.HideAndDontSave};var camera=probe.AddComponent<Camera>();camera.enabled=false;if(cam!=null)camera.CopyFrom(cam);
   try{foreach(var river in session.MountainLayout.Drainages)foreach(float f in new[]{.15f,.35f,.65f,.85f}){var p=field.Point(Along294(river,f));camera.transform.SetPositionAndRotation(p+new Vector3(25,14,-25),Quaternion.LookRotation(new Vector3(-25,-14,25)));foreach(var renderer in new[]{art,landscape}){var check=renderer.CompareWithFullScan(camera);mismatch+=check.Mismatches;}}}finally{Object.DestroyImmediate(probe);}
   C(mismatch==0,"river and dry-landscape dressing cell culling equals full scan across 12 poses each; mismatches="+mismatch);
   lines.Add("Global watershed / all confluences / ford traversal / manual art review / 120fps are not validated.");File.WriteAllLines(O294+"/checks.txt",lines);return string.Join("\n",lines);
  }
 }
}
