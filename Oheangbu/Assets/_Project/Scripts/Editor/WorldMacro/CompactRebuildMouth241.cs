using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
  [Serializable] class MouthPlan241 {public Vector3 origin,side,forward;public Vector2[] aperture;public Vector3[] boundary;public float cut,front;}
  static readonly Vector3 MouthOrigin241=new Vector3(3435,135.76f,1857);
  static readonly Vector3 MouthForward241=new Vector3(.728f,0,-.686f).normalized;
  static readonly Vector3 MouthSide241=new Vector3(.686f,0,.728f).normalized;
  static float Ramp241(float a,float b,float v)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,v));
  static void SmoothWeld241(Mesh mesh) {
   var v=mesh.vertices;var t=mesh.triangles;var accum=new Dictionary<Vector3Int,Vector3>();var keys=v.Select(p=>Vector3Int.RoundToInt(p*1000)).ToArray();
   for(int i=0;i<t.Length;i+=3){var n=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]);for(int j=0;j<3;j++){var k=keys[t[i+j]];accum.TryGetValue(k,out var sum);accum[k]=sum+n;}}
   mesh.normals=keys.Select(k=>accum[k].normalized).ToArray();mesh.RecalculateBounds();
  }
  static string Mouth241(string command) {
   var scene=SceneManager.GetActiveScene();var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
   if(scene.path!=receipt.scene)throw new Exception("Latest candidate required");
   var roots=scene.GetRootGameObjects();var mine=roots.Single(g=>g.name=="mine");string baseFolder=Path.GetDirectoryName(receipt.scene).Replace('\\','/'),folder=baseFolder+"/Mouth241",dir=Output+"/Mouth241";Directory.CreateDirectory(dir);
   var wall=mine.GetComponentsInChildren<MeshCollider>().Single(c=>c.name=="Natural_Cave_Interior");var cover=mine.GetComponentsInChildren<MeshCollider>().Single(c=>c.name=="Cave_ExteriorCover237");
   if(command.StartsWith("capture:"))return MouthCapture241(command.Substring(8));
   if(command=="support"){
    var log=new List<string>();foreach(var p in new[]{new Vector3(3439.077f,135.489f,1853.894f),new Vector3(3438.265f,135.454f,1854.441f)}){
     log.Add("SAMPLE "+p);foreach(bool back in new[]{false,true}){bool prev=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=back;
      foreach(var h in Physics.RaycastAll(p+Vector3.up*1.5f,Vector3.down,4).Where(h=>h.collider.gameObject.scene==scene).OrderBy(h=>h.distance))log.Add("ray back="+back+" "+h.collider.name+" y="+h.point.y+" normal="+h.normal);Physics.queriesHitBackfaces=prev;}
     var c=mine.GetComponentsInChildren<MeshCollider>().Single(c=>c.name=="Continuous_Approach_Soil");var v=c.sharedMesh.vertices;var t=c.sharedMesh.triangles;
     for(int i=0;i<t.Length;i+=3){var aa=c.transform.TransformPoint(v[t[i]]);var bb=c.transform.TransformPoint(v[t[i+1]]);var cc=c.transform.TransformPoint(v[t[i+2]]);var bounds=new Bounds(aa,Vector3.zero);bounds.Encapsulate(bb);bounds.Encapsulate(cc);bounds.Expand(.7f);if(bounds.Contains(p))log.Add("near triangle "+aa+" / "+bb+" / "+cc);}}
    string report=string.Join("\n",log);File.WriteAllText(dir+"/support.txt",report);return report;}
   if(command=="map"){SurveyTexture("cave.png",baseFolder+"/CavePlan.png");return "Updated cave illustration";}
   if(command=="pose"){
    if(!EditorApplication.isPlaying)throw new Exception("Play required");var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
    var p=MouthOrigin241-MouthForward241*19;var approach=mine.GetComponentsInChildren<MeshCollider>().Single(c=>c.name=="Continuous_Approach_Soil");
    if(approach.Raycast(new Ray(p+Vector3.up*30,Vector3.down),out var h,100))p.y=h.point.y+.12f;
    Oheangbu.App.World.UI.PlaytestUiRoot.Instance.CloseMenu();session.Teleport(p,Mathf.Atan2(MouthForward241.x,MouthForward241.z)*Mathf.Rad2Deg);return "Actual player mouth pose "+p;}
   if(command=="finish"){
    if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");Physics.SyncTransforms();var sample=FinalSurface(scene);
    var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var sheet=Object.Instantiate(AssetDatabase.LoadAssetAtPath<Oheangbu.Data.World.WorldMacroDressingSheetSO>(baseFolder+"/Boundary240/Placements.asset"));int moved=0;
    foreach(var placed in sheet.FixedPlacements){var p=placed.Position;if(p.x<3370||p.x>3616||p.z<1700||p.z>1930)continue;var proto=sheet.Prototypes.Single(t=>t.Id==placed.PrototypeId);if(proto.Category==Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Prop)continue;
     float y=sample(p.x,p.z).point.y-(proto.Category==Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Rock?proto.Size.y*placed.Scale*.17f:0);if(Mathf.Abs(y-p.y)>.025f)moved++;placed.Position=new Vector3(p.x,y,p.z);var collision=art.transform.Find(placed.Id);if(collision!=null)collision.position=placed.Position;}
    art.Sheet=SavePrivate(sheet,folder+"/Placements.asset");art.Invalidate();var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>()).Single();manifest.Art=art.Sheet;EditorUtility.SetDirty(manifest);File.WriteAllText(Output+"/art_placements.json",JsonUtility.ToJson(art.Sheet,true));
    AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);string report="Regrounded "+moved+" local placements and matching colliders; "+ExportFinalHeights(scene);File.WriteAllText(dir+"/finish.txt",report);return report;}
   if(command=="audit") {
    var lines=new List<string>();void Check(bool p,string s)=>lines.Add((p?"PASS ":"FAIL ")+s);
    bool previous=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=true;
    try{int clear=0,total=0;float low=999;
     for(float d=-4;d<=22;d+=1){var p=MouthOrigin241+MouthForward241*d;var origin=p+Vector3.up*.2f;total++;
      if(wall.Raycast(new Ray(origin,Vector3.up),out var hit,20)){low=Mathf.Min(low,hit.distance+.2f);if(hit.distance>2.1f)clear++;else lines.Add("DETAIL low d="+d+" height="+(hit.distance+.2f));}else{lines.Add("DETAIL no ceiling d="+d);}}
     Check(clear==total,"mouth centreline headroom "+clear+"/"+total+" minimum="+low.ToString("F2")+"m");
     Check(wall.sharedMesh==wall.GetComponent<MeshFilter>().sharedMesh&&cover.sharedMesh==cover.GetComponent<MeshFilter>().sharedMesh,"visible opening and cover match colliders");
    }finally{Physics.queriesHitBackfaces=previous;}
    Check(cover.GetComponent<Renderer>().sharedMaterials.Length==2,"continuous slope material across host; separate interior rock");
    var materials=new[]{wall.GetComponent<Renderer>(),cover.GetComponent<Renderer>()}.SelectMany(r=>r.sharedMaterials).Distinct();Check(materials.All(m=>m!=null&&m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader)),"mouth shaders compile");
    var s=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();Check(s.Content.SaveSlot=="world-demo-compact-cave-v4","content slot unchanged");
    string report=string.Join("\n",lines);File.WriteAllText(dir+"/audit.txt",report);return report;
   }
   if(command!="build"||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit build required");
   DevSceneKit.EnsureFolder(folder);
   Vector3 Local(Vector3 p){var q=p-MouthOrigin241;return new Vector3(Vector3.Dot(q,MouthSide241),q.y,Vector3.Dot(q,MouthForward241));}
   Vector3 World(Vector3 p)=>MouthOrigin241+MouthSide241*p.x+Vector3.up*p.y+MouthForward241*p.z;
   var original=AssetDatabase.LoadAssetAtPath<Mesh>(baseFolder+"/Boundary240/EmbeddedLining.asset");
   var source=original.vertices.Select(wall.transform.TransformPoint).Select(Local).ToArray();var indices=original.triangles;
   var vertices=new List<Vector3>();var kept=new List<int>();var ring=new Dictionary<Vector2Int,Vector3>();const float cut=12;
   // Cut only the old mouth. The remaining gallery mesh and its boundary are exact.
   for(int i=0;i<indices.Length;i+=3){var polygon=new List<Vector3>();
    for(int j=0;j<3;j++){var a=source[indices[i+j]];var b=source[indices[i+(j+1)%3]];float va=a.z-cut,vb=b.z-cut;if(va>=0)polygon.Add(a);
     if((va>=0)!=(vb>=0)){var p=Vector3.Lerp(a,b,va/(va-vb));polygon.Add(p);ring[Vector2Int.RoundToInt(new Vector2(p.x,p.y)*1000)]=p;}}
    for(int j=2;j<polygon.Count;j++){int n=vertices.Count;vertices.Add(polygon[0]);vertices.Add(polygon[j-1]);vertices.Add(polygon[j]);kept.AddRange(new[]{n,n+1,n+2});}}
   var boundary=ring.Values.OrderBy(p=>Mathf.Atan2(p.y-1.4f,p.x)).ToArray();if(boundary.Length<12)throw new Exception("No continuous mouth boundary");
   var aperture=new[]{new Vector2(-2.8f,-3),new Vector2(3,-3),new Vector2(3.1f,1.3f),new Vector2(2.4f,2.7f),new Vector2(.5f,3.45f),new Vector2(-1.4f,4.1f),new Vector2(-2.9f,3.25f),new Vector2(-3.25f,1.4f)};
   
   Vector2 AtAngle(Vector2[] polygon,float angle){var origin=new Vector2(0,1.4f);var direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));float best=1000;
    float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
    for(int j=0;j<polygon.Length;j++){var a=polygon[j];var e=polygon[(j+1)%polygon.Length]-a;float denom=Cross(direction,e);if(Mathf.Abs(denom)<.00001f)continue;
     float t=Cross(a-origin,e)/denom,u=Cross(a-origin,direction)/denom;if(t>0&&u>=0&&u<=1)best=Mathf.Min(best,t);}return origin+direction*best;}
   var angles=boundary.Select(p=>Mathf.Atan2(p.y-1.4f,p.x)).ToArray();int count=boundary.Length;
   var mouth=new Vector3[count];for(int j=0;j<count;j++){var q=AtAngle(aperture,angles[j]);mouth[j]=new Vector3(q.x,q.y,-18);}
   var tunnel=new List<int>();int first=vertices.Count;const int rings=13;
   for(int k=0;k<rings;k++){float t=k/(float)(rings-1),blend=Ramp241(.18f,1,t);for(int j=0;j<count;j++){
    var v=Vector3.Lerp(mouth[j],boundary[j],blend);v.z=Mathf.Lerp(-18,cut,t);v.x+=.55f*Mathf.Sin(t*Mathf.PI*2)*Mathf.Sin(t*Mathf.PI);vertices.Add(v);}}
   for(int k=0;k<rings-1;k++)for(int j=0;j<count;j++){int a=first+k*count+j,b=first+k*count+(j+1)%count,c=a+count,d=b+count;tunnel.AddRange(new[]{a,c,b,b,c,d});}
   var lining=new Mesh{name="MouthInterior241",indexFormat=IndexFormat.UInt32};lining.vertices=vertices.Select(p=>wall.transform.InverseTransformPoint(World(p))).ToArray();lining.subMeshCount=2;lining.SetTriangles(kept,0);lining.SetTriangles(tunnel,1);lining.uv=vertices.Select(p=>new Vector2(p.x,p.z)*.2f).ToArray();SmoothWeld241(lining);lining.RecalculateTangents();lining=ArtMesh(lining,folder+"/LowMouthInterior.asset");wall.sharedMesh=null;wall.sharedMesh=lining;wall.GetComponent<MeshFilter>().sharedMesh=lining;
   // Reconstruct the host from terrain and the new gallery roof. Do not retain
   // the old, much larger opening in its surface.
   var terrain=roots.SelectMany(g=>g.GetComponentsInChildren<MeshCollider>()).Where(c=>c.name.StartsWith("Terrain_")).ToArray();
   const int nx=126,nz=116;const float x0=3370,z0=1700,step=2;
   var points=new Vector3[nx*nz];var raised=new bool[points.Length];
   bool oldBack=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=true;Physics.SyncTransforms();
   try{for(int z=0;z<nz;z++)for(int x=0;x<nx;x++){
    float wx=x0+x*step,wz=z0+z*step,ground=float.NegativeInfinity;var ray=new Ray(new Vector3(wx,600,wz),Vector3.down);
    foreach(var c in terrain)if(c.bounds.min.x<=wx&&c.bounds.max.x>=wx&&c.bounds.min.z<=wz&&c.bounds.max.z>=wz&&c.Raycast(ray,out var h,800))ground=Mathf.Max(ground,h.point.y);
    if(float.IsNegativeInfinity(ground))throw new Exception("Missing terrain");
    float dx=(wx-3521)/83,dz=(wz-1797)/75,inward=Local(new Vector3(wx,0,wz)).z;
    float hill=135.2f+43*Mathf.Exp(-(dx*dx+dz*dz)*1.25f)*Ramp241(-12,35,inward);
    float edge=Mathf.Min(Mathf.Min(x,nx-1-x),Mathf.Min(z,nz-1-z))*step;
    float height=Mathf.Lerp(ground,Mathf.Max(ground,hill),Ramp241(0,18,edge));
    if(inward>=-5&&wall.Raycast(ray,out var roof,800))height=Mathf.Max(height,roof.point.y+Mathf.Lerp(1.25f,3.5f,Ramp241(-5,25,inward)));
    int at=x+z*nx;raised[at]=height>ground+.025f;points[at]=new Vector3(wx,height+.015f,wz);
   }}finally{Physics.queriesHitBackfaces=oldBack;}
   var raw=(Vector3[])points.Clone();
   for(int z=8;z<nz-8;z++)for(int x=8;x<nx-8;x++){int at=x+z*nx;float h=raw[at].y;
    for(int dz=-7;dz<=7;dz++)for(int dx=-7;dx<=7;dx++)if(raised[at+dx+dz*nx])h=Mathf.Max(h,raw[at+dx+dz*nx].y-Mathf.Sqrt(dx*dx+dz*dz)*step*.85f);
    if(h>points[at].y+.025f)raised[at]=true;points[at].y=h;}
   float HostHeight(Vector3 p){float fx=Mathf.Clamp((p.x-x0)/step,0,nx-1.001f),fz=Mathf.Clamp((p.z-z0)/step,0,nz-1.001f);int x=(int)fx,z=(int)fz,i=x+z*nx;return Mathf.Lerp(Mathf.Lerp(points[i].y,points[i+1].y,fx-x),Mathf.Lerp(points[i+nx].y,points[i+nx+1].y,fx-x),fz-z);}
   // Trim the new lining exactly where it meets the solid hillside.
   var clipped=new List<Vector3>();var inner=new List<int>();var entry=new List<int>();var vv=lining.vertices;
   for(int sub=0;sub<2;sub++){var tt=lining.GetTriangles(sub);var dest=sub==0?inner:entry;
    for(int i=0;i<tt.Length;i+=3){var poly=new List<Vector3>();for(int j=0;j<3;j++){var a=wall.transform.TransformPoint(vv[tt[i+j]]);var b=wall.transform.TransformPoint(vv[tt[i+(j+1)%3]]);float va=HostHeight(a)-a.y+.015f,vb=HostHeight(b)-b.y+.015f;if(va>=0)poly.Add(a);if((va>=0)!=(vb>=0))poly.Add(Vector3.Lerp(a,b,va/(va-vb)));}
     for(int j=2;j<poly.Count;j++){int n=clipped.Count;clipped.Add(wall.transform.InverseTransformPoint(poly[0]));clipped.Add(wall.transform.InverseTransformPoint(poly[j-1]));clipped.Add(wall.transform.InverseTransformPoint(poly[j]));dest.AddRange(new[]{n,n+1,n+2});}}}
   lining.Clear();lining.SetVertices(clipped);lining.subMeshCount=2;lining.SetTriangles(inner,0);lining.SetTriangles(entry,1);lining.uv=clipped.Select(p=>new Vector2(p.x,p.z)*.2f).ToArray();SmoothWeld241(lining);lining.RecalculateTangents();EditorUtility.SetDirty(lining);wall.sharedMesh=null;wall.sharedMesh=lining;
   float Air(Vector3 world){var p=Local(world);if(p.z<-18)return 1;float t=Mathf.Clamp01((p.z+18)/(cut+18)),blend=Ramp241(.18f,1,t);p.x-=.55f*Mathf.Sin(t*Mathf.PI*2)*Mathf.Sin(t*Mathf.PI);
    float angle=Mathf.Atan2(p.y-1.4f,p.x);int j=0;while(j<count-1&&angles[j+1]<angle)j++;
    int next=(j+1)%count;float a0=angles[j],a1=next==0?angles[0]+Mathf.PI*2:angles[next];float aa=angle;if(aa<a0)aa+=Mathf.PI*2;
    var end=Vector3.Lerp(boundary[j],boundary[next],Mathf.InverseLerp(a0,a1,aa));var start=AtAngle(aperture,angle);float radius=Mathf.Lerp((start-new Vector2(0,1.4f)).magnitude,new Vector2(end.x,end.y-1.4f).magnitude,blend);
    return new Vector2(p.x,p.y-1.4f).magnitude-radius;}
   var outerVertices=new List<Vector3>();var soilFaces=new List<int>();var rockFaces=new List<int>();
   void Emit(Vector3 a,Vector3 b,Vector3 c,int depth=0){var center=(a+b+c)/3;var lc=Local(center);
    if(depth<4&&lc.z<16&&lc.z>-22&&Mathf.Abs(lc.x)<16&&Mathf.Max((a-b).sqrMagnitude,Mathf.Max((b-c).sqrMagnitude,(c-a).sqrMagnitude))>.16f){var ab=(a+b)*.5f;var bc=(b+c)*.5f;var ca=(c+a)*.5f;Emit(a,ab,ca,depth+1);Emit(ab,b,bc,depth+1);Emit(ca,bc,c,depth+1);Emit(ab,bc,ca,depth+1);return;}
    var input=new[]{a,b,c};var poly=new List<Vector3>();for(int j=0;j<3;j++){var v=input[j];var w=input[(j+1)%3];float va=Local(v).z<12?Air(v):1,wa=Local(w).z<12?Air(w):1;if(va>=0)poly.Add(v);if((va>=0)!=(wa>=0))poly.Add(Vector3.Lerp(v,w,va/(va-wa)));}
    var dest=lc.z<16&&Mathf.Abs(lc.x)<14?rockFaces:soilFaces;
    for(int j=2;j<poly.Count;j++){int n=outerVertices.Count;outerVertices.Add(cover.transform.InverseTransformPoint(poly[0]));outerVertices.Add(cover.transform.InverseTransformPoint(poly[j-1]));outerVertices.Add(cover.transform.InverseTransformPoint(poly[j]));dest.AddRange(new[]{n,n+1,n+2});}}
   for(int z=0;z<nz-1;z++)for(int x=0;x<nx-1;x++){int a=x+z*nx,b=a+1,c=a+nx,d=c+1;if(!raised[a]&&!raised[b]&&!raised[c]&&!raised[d])continue;Emit(points[a],points[c],points[b]);Emit(points[b],points[c],points[d]);}
   var host=new Mesh{name="SolidMouthHost241",indexFormat=IndexFormat.UInt32};host.SetVertices(outerVertices);host.subMeshCount=2;host.SetTriangles(soilFaces,0);host.SetTriangles(rockFaces,1);host.uv=outerVertices.Select(p=>new Vector2(p.x,p.z)*.2f).ToArray();SmoothWeld241(host);host.RecalculateTangents();host=ArtMesh(host,folder+"/LowMouthCover.asset");cover.sharedMesh=host;cover.GetComponent<MeshFilter>().sharedMesh=host;
   var stone=new Material(AssetDatabase.LoadAssetAtPath<Material>(baseFolder+"/Surface239/Rock.mat"));stone.SetColor("_BaseColor",new Color(.34f,.33f,.30f));stone.SetFloat("_Ambient",.40f);stone.SetFloat("_Scale",.32f);stone=SurveyMaterial(stone,folder+"/EntranceRock.mat");
   var soil=AssetDatabase.LoadAssetAtPath<Material>(baseFolder+"/Atmosphere238/M_4bb84661d27d73b4f819ff4bd0dd81d3_2100000.mat");cover.GetComponent<Renderer>().sharedMaterials=new[]{soil,soil};wall.GetComponent<Renderer>().sharedMaterials=new[]{AssetDatabase.LoadAssetAtPath<Material>(baseFolder+"/Surface239/Rock.mat"),stone};
   var outer=Object.Instantiate(lining);outer.name="LowMouthOuter";var triangles=outer.triangles;for(int i=0;i<triangles.Length;i+=3){int a=triangles[i];triangles[i]=triangles[i+2];triangles[i+2]=a;}outer.subMeshCount=1;outer.triangles=triangles;outer.RecalculateNormals();outer=ArtMesh(outer,folder+"/LowMouthOuter.asset");wall.transform.Find("Cave_OuterCollision237").GetComponent<MeshCollider>().sharedMesh=outer;
   var bank=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).SingleOrDefault(t=>t.name=="Approach_Bank240");if(bank!=null)bank.gameObject.SetActive(true);
   // Replace overlapping old floor edges in the rebuilt entrance with one
   // continuous walkable surface. Keep the distant approach and inner galleries.
   var floorFilter=mine.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Natural_Cave_Floor");
   var approachFilter=mine.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Continuous_Approach_Soil");
   foreach(var f in new[]{floorFilter,approachFilter}){string baseline=folder+"/"+f.name+"_Source.asset";if(AssetDatabase.LoadAssetAtPath<Mesh>(baseline)==null)AssetDatabase.CreateAsset(Object.Instantiate(f.sharedMesh),baseline);}
   var approachSource=AssetDatabase.LoadAssetAtPath<Mesh>(folder+"/Continuous_Approach_Soil_Source.asset");
   approachFilter.GetComponent<MeshCollider>().sharedMesh=approachSource;Physics.SyncTransforms();
   Mesh CutFloor(MeshFilter filter,float plane,bool positive){
    var originalFloor=AssetDatabase.LoadAssetAtPath<Mesh>(folder+"/"+filter.name+"_Source.asset");var v=originalFloor.vertices;var t=originalFloor.triangles;var output=new List<Vector3>();var tris=new List<int>();var outsideTris=new List<int>();
    for(int i=0;i<t.Length;i+=3){var poly=new List<Vector3>();for(int j=0;j<3;j++){var aa=filter.transform.TransformPoint(v[t[i+j]]);var bb=filter.transform.TransformPoint(v[t[i+(j+1)%3]]);float va=(Local(aa).z-plane)*(positive?1:-1),vb=(Local(bb).z-plane)*(positive?1:-1);if(va>=0)poly.Add(aa);if((va>=0)!=(vb>=0))poly.Add(Vector3.Lerp(aa,bb,va/(va-vb)));}
     for(int j=2;j<poly.Count;j++){int n=output.Count;output.Add(filter.transform.InverseTransformPoint(poly[0]));output.Add(filter.transform.InverseTransformPoint(poly[j-1]));output.Add(filter.transform.InverseTransformPoint(poly[j]));tris.AddRange(new[]{n,n+1,n+2});}}
    if(positive){int start=output.Count;const int columns=41,rows=61;
     for(int k=0;k<rows;k++)for(int j=0;j<columns;j++){float depth=Mathf.Lerp(-18,12,k/(float)(rows-1)),across=Mathf.Lerp(-1,1,j/(float)(columns-1)),u=across*Mathf.Lerp(3.8f,10,Ramp241(-18,12,depth));var world=World(new Vector3(u,0,depth));var edge=World(new Vector3(across*3.8f,0,-18));float edgeHeight=MouthOrigin241.y;
      if(approachFilter.GetComponent<MeshCollider>().Raycast(new Ray(edge+Vector3.up*30,Vector3.down),out var hit,100))edgeHeight=hit.point.y;else foreach(var ground in terrain)if(ground.Raycast(new Ray(edge+Vector3.up*300,Vector3.down),out var support,600))edgeHeight=support.point.y;
      world.y=Mathf.Lerp(edgeHeight,MouthOrigin241.y,Ramp241(-18,-8,depth));float feather=Ramp241(.7f,1,Mathf.Abs(across))*(1-Ramp241(-14,-8,depth));
      if(feather>0)foreach(var ground in terrain)if(ground.Raycast(new Ray(world+Vector3.up*300,Vector3.down),out var support,600))world.y=Mathf.Lerp(world.y,support.point.y-.025f,feather);output.Add(filter.transform.InverseTransformPoint(world));}
     for(int k=0;k<rows-1;k++)for(int j=0;j<columns-1;j++){int aa=start+k*columns+j,bb=aa+1,cc=aa+columns,dd=cc+1;var destination=Mathf.Lerp(-18,12,(k+.5f)/(rows-1))<0?outsideTris:tris;destination.AddRange(new[]{aa,bb,cc,bb,dd,cc});}}
    var mesh=new Mesh{name=filter.name+"Mouth241",indexFormat=IndexFormat.UInt32};mesh.SetVertices(output);mesh.subMeshCount=positive?2:1;mesh.SetTriangles(tris,0);if(positive)mesh.SetTriangles(outsideTris,1);mesh.uv=output.Select(p=>new Vector2(p.x,p.z)*.2f).ToArray();SmoothWeld241(mesh);mesh.RecalculateTangents();return ArtMesh(mesh,folder+"/"+filter.name+".asset");}
   var floorMesh=CutFloor(floorFilter,12,true);var approachMesh=CutFloor(approachFilter,-18,false);
   floorFilter.sharedMesh=floorMesh;floorFilter.GetComponent<MeshCollider>().sharedMesh=floorMesh;floorFilter.GetComponent<Renderer>().sharedMaterials=new[]{AssetDatabase.LoadAssetAtPath<Material>(baseFolder+"/Surface239/Floor.mat"),soil};approachFilter.sharedMesh=approachMesh;approachFilter.GetComponent<MeshCollider>().sharedMesh=approachMesh;
   if(bank!=null){var sourceBank=AssetDatabase.LoadAssetAtPath<Mesh>(baseFolder+"/Boundary240/ApproachBank.asset");var bv=sourceBank.vertices;var bt=sourceBank.triangles;var trimmed=new List<int>();
    for(int i=0;i<bt.Length;i+=3){var center=bank.TransformPoint((bv[bt[i]]+bv[bt[i+1]]+bv[bt[i+2]])/3);if(Local(center).z>=-32)continue;trimmed.AddRange(new[]{bt[i],bt[i+1],bt[i+2]});}
    var retained=Object.Instantiate(sourceBank);retained.triangles=trimmed.ToArray();retained.RecalculateBounds();retained=ArtMesh(retained,folder+"/ApproachBank.asset");bank.GetComponent<MeshFilter>().sharedMesh=retained;bank.GetComponent<MeshCollider>().sharedMesh=retained;}
   File.WriteAllText(dir+"/aperture.json",JsonUtility.ToJson(new MouthPlan241{origin=MouthOrigin241,side=MouthSide241,forward=MouthForward241,aperture=aperture,boundary=boundary,cut=cut,front=-18},true));
   Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
   return "New connected mouth lining and solid outer rock face saved; inner galleries retained";
  }
  static string MouthCapture241(string label) {
   var roots=SceneManager.GetActiveScene().GetRootGameObjects();var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
   var go=new GameObject("Temporary_Mouth241");var camera=go.AddComponent<Camera>();camera.CopyFrom(session.Walker.ViewCamera);camera.enabled=false;camera.useOcclusionCulling=false;EditorUtility.CopySerialized(session.Walker.ViewCamera.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).ToArray();var observers=art.Select(a=>a.Observer).ToArray();foreach(var a in art)a.Observer=camera;
   Vector3 P(float u,float h,float d)=>MouthOrigin241+MouthSide241*u+MouthForward241*d+Vector3.up*h;
   var eyes=new[]{new Vector3(3398,140,1886),P(-13,3,-19),P(10,2.4f,-15),P(0,1.7f,8)};
   var targets=new[]{P(0,2,0),P(0,2,0),P(0,1.8f,2),P(0,1.4f,-20)};
   var rt=new RenderTexture(1920,1080,24);var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);var prior=RenderTexture.active;
   try{camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=65;for(int i=0;i<eyes.Length;i++){camera.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(targets[i]-eyes[i]));camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(Output+"/Mouth241/"+label+"_"+i+".png",image.EncodeToPNG());}}
   finally{for(int i=0;i<art.Length;i++)art[i].Observer=observers[i];camera.targetTexture=null;RenderTexture.active=prior;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(go);}return "Four mouth viewpoints captured: "+label;
  }
 }
}
