using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Demo;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string GateInfillName296="FixedArchInfill296";
  struct InfillVertex296 { public Vector3 P,N;public Vector2 UV; }
  sealed class InfillPart296 { public Vector3[] P,N;public Vector2[] UV;public int[] T;public Material Material; }
  [Serializable] sealed class GateInfillItem296
  {
   public string Path,Source;public int Triangles,Renderers,LodLevels,ProfileSamples,CoverageSamples,CoverageMisses,RotationPoses;
   public float LeafMaximumY,FixedMinimumY,MinimumRotationClearance,ArchCrownY,MaximumPrePaddingBoundsError,BoundsExpansionPerFace;public Bounds LocalBounds;
  }
  [Serializable] sealed class GateInfillReceipt296
  {
   public string Utc,BeforeColliders,AfterColliders,BeforeDoorState,AfterDoorState;
   public bool CollidersUnchanged,DoorConfigurationUnchanged;public GateInfillItem296[] Gates;
  }
  static string GateStateStamp296()
  {
   var lines=new List<string>();
   foreach(var gate in Components295<SouthGateDoorPresentation>().Where(g=>g.gameObject.activeInHierarchy).OrderBy(g=>ScenePathVenue296(g.transform),StringComparer.Ordinal))
   {
    lines.Add(ScenePathVenue296(gate.transform)+"|"+EditorJsonUtility.ToJson(gate));
    foreach(var t in gate.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="LeftHinge"||t.name=="RightHinge"))lines.Add(ScenePathVenue296(t)+"|"+t.localPosition.ToString("R")+"|"+t.localRotation.ToString("R")+"|"+t.localScale.ToString("R"));
    foreach(var n in gate.GetComponents<UnityEngine.AI.NavMeshObstacle>())lines.Add(EditorJsonUtility.ToJson(n));
   }
   return RetainingTextHash296(string.Join("\n",lines));
  }
  static List<InfillPart296> InfillLeafParts296(SouthGateDoorPresentation gate,Transform hinge)
  {
   var result=new List<InfillPart296>();
   foreach(var filter in hinge.GetComponentsInChildren<MeshFilter>(true))
   {
    var mesh=filter.sharedMesh;var renderer=filter.GetComponent<MeshRenderer>();if(mesh==null||renderer==null)continue;
    var matrix=gate.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
    var p=mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();var n=mesh.normals.Select(v=>matrix.inverse.transpose.MultiplyVector(v).normalized).ToArray();
    for(int s=0;s<mesh.subMeshCount;s++)result.Add(new InfillPart296{P=p,N=n,UV=mesh.uv,T=mesh.GetTriangles(s),Material=renderer.sharedMaterials[s]});
   }
   return result;
  }
  static float[] InfillProfile296(Transform door,Transform architecture,float half,int count)
  {
   var ceiling=Enumerable.Repeat(float.PositiveInfinity,count+1).ToArray();
   foreach(var filter in architecture.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null&&f.transform.parent!=null&&ScenePathVenue296(f.transform).Contains("CAP_GATE_ARCH")))
   {
    var mesh=filter.sharedMesh;var m=door.worldToLocalMatrix*filter.transform.localToWorldMatrix;var v=mesh.vertices.Select(m.MultiplyPoint3x4).ToArray();var tris=mesh.triangles;
    for(int t=0;t<tris.Length;t+=3)
    {
     var a=v[tris[t]];var b=v[tris[t+1]];var c=v[tris[t+2]];float lo=Mathf.Min(a.x,Mathf.Min(b.x,c.x)),hi=Mathf.Max(a.x,Mathf.Max(b.x,c.x));
     int start=Mathf.Clamp(Mathf.CeilToInt((lo+half)/(2*half)*count),0,count),end=Mathf.Clamp(Mathf.FloorToInt((hi+half)/(2*half)*count),0,count);
     for(int i=start;i<=end;i++)
     {
      float x=Mathf.Lerp(-half,half,i/(float)count),y=float.PositiveInfinity;Edge(a,b);Edge(b,c);Edge(c,a);
      if(y>5.5f&&y<ceiling[i])ceiling[i]=y;
      void Edge(Vector3 p,Vector3 q)
      {if(Mathf.Abs(q.x-p.x)<.000001f)return;float u=(x-p.x)/(q.x-p.x);if(u>=-.00001f&&u<=1.00001f)y=Mathf.Min(y,Mathf.LerpUnclamped(p.y,q.y,u));}
     }
    }
   }
   if(ceiling.Any(float.IsInfinity)||ceiling.Max()<9)throw new InvalidOperationException("Cannot resolve actual Korean gate arch ceiling");
   return ceiling;
  }
  static List<InfillVertex296> ClipInfill296(List<InfillVertex296> input,Func<Vector3,float> distance)
  {
   var output=new List<InfillVertex296>();if(input.Count==0)return output;
   var prior=input[input.Count-1];float pd=distance(prior.P);
   foreach(var next in input)
   {
    float nd=distance(next.P);bool a=pd>=-.000001f,b=nd>=-.000001f;
    if(a!=b){float t=pd/(pd-nd);output.Add(new InfillVertex296{P=Vector3.LerpUnclamped(prior.P,next.P,t),N=Vector3.LerpUnclamped(prior.N,next.N,t).normalized,UV=Vector2.LerpUnclamped(prior.UV,next.UV,t)});}
    if(b)output.Add(next);prior=next;pd=nd;
   }
   return output;
  }
  static Mesh MakeInfill296(List<InfillPart296> parts,float[] ceiling,float half,float bottom,bool lintel,Material material)
  {
   var p=new List<Vector3>();var n=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();int count=ceiling.Length-1;
   foreach(var source in parts.Where(part=>part.Material==material))
   {
    var positions=new Vector3[source.P.Length];var normals=new Vector3[source.P.Length];
    for(int i=0;i<positions.Length;i++)
    {
     var q=source.P[i];var normal=source.N[i];
     if(lintel){positions[i]=new Vector3(q.y-4.10f,-(q.x+2.10f)+bottom+.12f,q.z*1.30f);normals[i]=new Vector3(normal.y,-normal.x,normal.z).normalized;}
     else {positions[i]=q+Vector3.up*2.10f;normals[i]=normal;}
    }
    for(int t=0;t<source.T.Length;t+=3)
    {
     var seed=new List<InfillVertex296>();for(int k=0;k<3;k++){int i=source.T[t+k];seed.Add(new InfillVertex296{P=positions[i],N=normals[i],UV=source.UV[i]});}
     float xmin=seed.Min(v=>v.P.x),xmax=seed.Max(v=>v.P.x);
     int first=Mathf.Clamp(Mathf.FloorToInt((xmin+half)/(2*half)*count),0,count-1),last=Mathf.Clamp(Mathf.FloorToInt((xmax+half)/(2*half)*count),0,count-1);
     for(int cell=first;cell<=last;cell++)
     {
      float left=Mathf.Lerp(-half,half,cell/(float)count),right=Mathf.Lerp(-half,half,(cell+1f)/count);
      float lower=bottom+(lintel?0:.20f),upper=lintel?bottom+.24f:100;
      if(Mathf.Max(ceiling[cell],ceiling[cell+1])+.04f<lower)continue;
      var polygon=ClipInfill296(seed,q=>q.x-left);polygon=ClipInfill296(polygon,q=>right-q.x);
      polygon=ClipInfill296(polygon,q=>q.y-lower);polygon=ClipInfill296(polygon,q=>upper-q.y);
      polygon=ClipInfill296(polygon,q=>Mathf.LerpUnclamped(ceiling[cell],ceiling[cell+1],(q.x-left)/(right-left))+.04f-q.y);
      for(int k=1;k+1<polygon.Count;k++)
      {
       var a=polygon[0];var b=polygon[k];var c=polygon[k+1];if(Vector3.Cross(b.P-a.P,c.P-a.P).sqrMagnitude<1e-12f)continue;
       foreach(var vertex in new[]{a,b,c}){triangles.Add(p.Count);p.Add(vertex.P);n.Add(vertex.N);uv.Add(vertex.UV);}
      }
     }
    }
   }
   if(lintel)
   {
    // Close the newly cut underside with the same source boundary UVs. The
    // narrow caps lie inside the original wood thickness and add no collider.
    int original=p.Count;
    for(int cell=0;cell<count;cell++)
    {
     float left=Mathf.Lerp(-half,half,cell/(float)count),right=Mathf.Lerp(-half,half,(cell+1f)/count);
     var candidates=Enumerable.Range(0,original).Where(i=>Mathf.Abs(p[i].y-bottom)<.00002f&&p[i].x>=left-.00001f&&p[i].x<=right+.00001f).OrderBy(i=>p[i].x).ThenBy(i=>p[i].z).ToArray();
     if(candidates.Length<3)continue;
     float Cross(int a,int b,int c)=>(p[b].x-p[a].x)*(p[c].z-p[a].z)-(p[b].z-p[a].z)*(p[c].x-p[a].x);
     var hull=new List<int>();foreach(int i in candidates){while(hull.Count>=2&&Cross(hull[hull.Count-2],hull[hull.Count-1],i)<=.00000001f)hull.RemoveAt(hull.Count-1);hull.Add(i);}int lower=hull.Count;
     foreach(int i in candidates.Reverse()){while(hull.Count>lower&&Cross(hull[hull.Count-2],hull[hull.Count-1],i)<=.00000001f)hull.RemoveAt(hull.Count-1);hull.Add(i);}if(hull.Count>0)hull.RemoveAt(hull.Count-1);
     for(int k=1;k+1<hull.Count;k++)foreach(int i in new[]{hull[0],hull[k],hull[k+1]}){triangles.Add(p.Count);p.Add(p[i]);n.Add(Vector3.down);uv.Add(uv[i]);}
    }
   }
   var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.SetVertices(p);mesh.SetNormals(n);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
  }
  static bool InfillProjectedTriangle296(Vector3 point,Vector3 a,Vector3 b,Vector3 c)
  {
   float Cross(Vector3 x,Vector3 y,Vector3 z)=>(y.x-x.x)*(z.y-x.y)-(y.y-x.y)*(z.x-x.x);
   float area=Cross(a,b,c);if(Mathf.Abs(area)<1e-7f)return false;float u=Cross(point,b,c)/area,v=Cross(a,point,c)/area,w=1-u-v;return u>=-.00001f&&v>=-.00001f&&w>=-.00001f;
  }
  static GateInfillItem296 BuildGateInfill296(SouthGateDoorPresentation gate,Transform architecture,int index)
  {
   var old=gate.transform.Find(GateInfillName296);if(old!=null)Object.DestroyImmediate(old.gameObject);
   var left=gate.transform.Find("LeftHinge");var right=gate.transform.Find("RightHinge");if(left==null||right==null)throw new Exception("Gate hinges missing");
   var serialized=new SerializedObject(gate);
   if(Quaternion.Angle(left.localRotation,serialized.FindProperty("_leftClosed").quaternionValue)>.01f||Quaternion.Angle(right.localRotation,serialized.FindProperty("_rightClosed").quaternionValue)>.01f)throw new Exception("Generate fixed infill with doors closed");
   var partsLeft=InfillLeafParts296(gate,left);var parts=partsLeft.Concat(InfillLeafParts296(gate,right)).ToList();
   float top=parts.Max(s=>s.P.Max(v=>v.y)),bottom=top+.11f,half=4.30f;const int count=86;
   var ceiling=InfillProfile296(gate.transform,architecture,half,count);var root=new GameObject(GateInfillName296).transform;root.SetParent(gate.transform,false);root.gameObject.isStatic=true;
   var renderers=new List<Renderer>();var meshes=new List<Mesh>();float boundsError=0,boundsPadding=0;
   foreach(bool lintel in new[]{false,true})foreach(var material in parts.Select(p=>p.Material).Distinct())
   {
    var built=MakeInfill296(lintel?partsLeft:parts,ceiling,half,bottom,lintel,material);if(built.vertexCount==0){Object.DestroyImmediate(built);continue;}
    string key="GateInfill/gate"+index+"_"+(lintel?"lintel":"panel")+"_"+renderers.Count;
    var mesh=Asset296("Meshes/"+key+".asset",()=>new Mesh());
    // A CPU-valid CopySerialized mesh failed every normal/forced-LOD/unlit
    // render; rebuilding the identical arrays with native Mesh setters rendered
    // correctly. Keep the saved asset GUID while refreshing its native buffers.
    mesh.Clear();mesh.indexFormat=built.indexFormat;mesh.vertices=built.vertices;
    mesh.normals=built.normals;mesh.uv=built.uv;mesh.tangents=built.tangents;
    mesh.triangles=built.triangles;mesh.bounds=built.bounds;mesh.UploadMeshData(false);
    mesh.name=Path.GetFileName(key);Object.DestroyImmediate(built);
    // Bounds stores centre/extents: reconstructing min/max can lose one float
    // ULP even for local metre coordinates. Measure the actual discrepancy,
    // permit only a sub-millimetre rounding error, pad that measured amount,
    // then retain the exact Contains assertion for every vertex.
    float outside=0;var tight=mesh.bounds;
    foreach(var vertex in mesh.vertices)outside=Mathf.Max(outside,Mathf.Max(Mathf.Max(tight.min.x-vertex.x,vertex.x-tight.max.x),Mathf.Max(Mathf.Max(tight.min.y-vertex.y,vertex.y-tight.max.y),Mathf.Max(tight.min.z-vertex.z,vertex.z-tight.max.z))));
    if(outside>.001f)throw new Exception("Gate bounds discrepancy exceeds rounding allowance: "+outside.ToString("R")+"m "+key);
    float padding=outside+.00002f;tight.Expand(padding*2);mesh.bounds=tight;
    boundsError=Mathf.Max(boundsError,outside);boundsPadding=Mathf.Max(boundsPadding,padding);EditorUtility.SetDirty(mesh);
    var go=new GameObject(lintel?"OriginalWood_HorizontalLintel":"OriginalWood_ArchTransom");go.transform.SetParent(root,false);go.isStatic=true;
    go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;renderers.Add(r);meshes.Add(mesh);
   }
   if(renderers.Count==0)throw new Exception("Empty arch infill");
   var bounds=meshes[0].bounds;foreach(var mesh in meshes){bounds.Encapsulate(mesh.bounds);foreach(var v in mesh.vertices)if(!mesh.bounds.Contains(v))throw new Exception("Gate infill mesh bounds miss vertex");}
   var lod=root.gameObject.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.002f,renderers.ToArray())});lod.RecalculateBounds();
   var receipt=new GateInfillItem296{Path=ScenePathVenue296(gate.transform),Source="Assets/HwaseongForteressGate/Prefabs/SM_G_MetalDoor_001.prefab",Triangles=meshes.Sum(m=>(int)m.GetIndexCount(0)/3),Renderers=renderers.Count,LodLevels=1,ProfileSamples=count+1,LeafMaximumY=top,FixedMinimumY=bounds.min.y,ArchCrownY=ceiling.Max(),LocalBounds=bounds,MinimumRotationClearance=float.PositiveInfinity,MaximumPrePaddingBoundsError=boundsError,BoundsExpansionPerFace=boundsPadding};
   // Rotation about each unchanged vertical hinge preserves Y. Sampling the
   // entire 0..100 degree animation also verifies the serialized hinge axes.
   for(int angle=0;angle<=100;angle+=5)foreach(var hinge in new[]{left,right})
   {
    var local=gate.transform.worldToLocalMatrix*hinge.localToWorldMatrix;var rotation=Matrix4x4.Rotate(Quaternion.Euler(0,hinge==left?-angle:angle,0));
    foreach(var source in InfillLeafParts296(gate,hinge))foreach(var v in source.P){var moved=(local*rotation*local.inverse).MultiplyPoint3x4(v);receipt.MinimumRotationClearance=Mathf.Min(receipt.MinimumRotationClearance,bounds.min.y-moved.y);}
    receipt.RotationPoses++;
   }
   if(receipt.MinimumRotationClearance<.099f)throw new Exception("Fixed transom reduces door swing clearance");
   var surfaces=meshes.Select(m=>(v:m.vertices,t:m.triangles)).ToArray();
   for(int i=1;i<count;i++)
   {
    float x=Mathf.Lerp(-half,half,i/(float)count),max=ceiling[i]-.04f;
    for(float y=bottom+.02f;y<max;y+=.075f)
    {receipt.CoverageSamples++;var point=new Vector3(x,y,0);bool covered=surfaces.Any(s=>Enumerable.Range(0,s.t.Length/3).Any(t=>InfillProjectedTriangle296(point,s.v[s.t[t*3]],s.v[s.t[t*3+1]],s.v[s.t[t*3+2]])));if(!covered)receipt.CoverageMisses++;}
   }
   if(receipt.CoverageMisses>0)throw new Exception("Upper arch infill projected gap samples="+receipt.CoverageMisses+"/"+receipt.CoverageSamples);
   return receipt;
  }
  static void GateInfill296(List<string> report)
  {
   var doors=Components295<SouthGateDoorPresentation>().Where(g=>g.gameObject.activeInHierarchy&&g.IsConfigured).OrderBy(g=>ScenePathVenue296(g.transform),StringComparer.Ordinal).ToArray();var records=new List<GateInfillItem296>();
   foreach(var gate in doors)
   {
    var architecture=gate.transform.parent.Find(gate.name=="VictoryGate253"?"OriginalCompactGateArchitecture":"KoreanGateArchitecture");
    if(architecture==null)throw new Exception("Fixed arch infill cannot resolve architecture: "+ScenePathVenue296(gate.transform));
    records.Add(BuildGateInfill296(gate,architecture,records.Count));
   }
   File.WriteAllText(O296+"/gate-infill.json",JsonUtility.ToJson(new GateInfillReceipt296{Utc=DateTime.UtcNow.ToString("O"),Gates=records.ToArray()},true));
   report.Add("Fixed source-wood arch transoms="+records.Count+"; combined renderers="+records.Sum(r=>r.Renderers)+"; triangles="+records.Sum(r=>r.Triangles)+"; projected coverage misses="+records.Sum(r=>r.CoverageMisses)+"; minimum moving-leaf clearance="+records.Min(r=>r.MinimumRotationClearance).ToString("F3")+"m. Original hinges, blockers, Nav and animation untouched.");
  }
  public static string RefreshGateInfill296()
  {
   RequireClean292();if(EditorApplication.isPlayingOrWillChangePlaymode||Session292().gameObject.scene.path!=Scene296)throw new Exception("Gate infill needs saved296 Edit scene");
   string physical=RetainingTextHash296(RetainingSceneCollision296()),state=GateStateStamp296();var report=new List<string>();GateInfill296(report);
   var receipt=JsonUtility.FromJson<GateInfillReceipt296>(File.ReadAllText(O296+"/gate-infill.json"));receipt.BeforeColliders=physical;receipt.AfterColliders=RetainingTextHash296(RetainingSceneCollision296());receipt.BeforeDoorState=state;receipt.AfterDoorState=GateStateStamp296();
   receipt.CollidersUnchanged=receipt.BeforeColliders==receipt.AfterColliders;receipt.DoorConfigurationUnchanged=state==receipt.AfterDoorState;
   if(!receipt.CollidersUnchanged||!receipt.DoorConfigurationUnchanged)throw new Exception("Fixed infill changed gate physics or controls");
   Save292();File.WriteAllText(O296+"/gate-infill-refresh.json",JsonUtility.ToJson(receipt,true));File.WriteAllLines(O296+"/gate-infill-refresh.txt",report);return string.Join("\n",report);
  }
  public static string CaptureGateInfill296(bool opened)
  {
   RequireClean292();if(EditorApplication.isPlayingOrWillChangePlaymode||Session292().gameObject.scene.path!=Scene296)throw new Exception("Gate capture needs saved296 Edit scene");
   var gates=Components295<SouthGateDoorPresentation>().Where(g=>g.gameObject.activeInHierarchy).ToArray();var prior=gates.Select(g=>g.IsOpenRequested).ToArray();string state=GateStateStamp296();
   var view=ArchitectureViews296().Views.Single(v=>v.Id=="south-gate-eye");string output=O296+"/Captures/GateInfill/"+(opened?"opened":"closed")+".png";Directory.CreateDirectory(Path.GetDirectoryName(output));
   try{foreach(var gate in gates)gate.SetOpened(opened,true);Physics.SyncTransforms();eye293=view.Eye;target293=view.Target;output293=output;raw293=false;Capture292(6);File.WriteAllText(output+".json",JsonUtility.ToJson(new CaptureReceipt295{Stage=opened?"gate-open":"gate-closed",Scene=Scene296,CapturedUtc=DateTime.UtcNow.ToString("O"),View=view},true));}
   finally{eye293=null;target293=null;output293=null;raw293=false;for(int i=0;i<gates.Length;i++)gates[i].SetOpened(prior[i],true);Physics.SyncTransforms();if(state!=GateStateStamp296())throw new Exception("Gate capture did not restore leaf/Nav state");UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Scene296);}
   return output;
  }
 }
}
