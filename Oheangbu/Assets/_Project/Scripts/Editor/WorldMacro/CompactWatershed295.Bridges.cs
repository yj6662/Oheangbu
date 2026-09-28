using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static void Bridges295(CompactWorldSurface field,List<string> report)
  {
   var old=Root295("Watershed295_Crossings");if(old!=null)Object.DestroyImmediate(old);var root=new GameObject("Watershed295_Crossings");
   var stone=Asset295("Materials/BridgeStone.mat",()=>new Material(AssetDatabase.LoadAssetAtPath<Material>(S293+"/Materials/Hyeongang_Granite.mat")));
   var crossings=JsonUtility.FromJson<Crossings295>(File.ReadAllText(G295+"/crossings.json")).Crossings;
   foreach(var crossing in crossings)
   {
    if(crossing.Kind=="Mum"||crossing.Kind=="AbilityGate")continue;
    var points=crossing.Points!=null&&crossing.Points.Length>1?crossing.Points:new[]{crossing.Start,crossing.End};
    var build=new BridgeMesh295();float width=Mathf.Clamp(crossing.RouteWidth,3.2f,8);float distance=0,nextPost=2,nextPier=7;
    var right=new Vector3[points.Length];
    for(int i=0;i<points.Length;i++){var dir=points[Mathf.Min(points.Length-1,i+1)]-points[Mathf.Max(0,i-1)];dir.y=0;right[i]=Vector3.Cross(Vector3.up,dir.normalized);}
    for(int i=1;i<points.Length;i++)
    {
     var a=points[i-1]+Vector3.up*.025f;var b=points[i]+Vector3.up*.025f;var r0=right[i-1];var r1=right[i];float length=Vector3.Distance(a,b);if(length<.001f)continue;
     build.Ribbon(a,b,r0,r1,width,.56f);
     foreach(int sign in new[]{-1,1})build.Ribbon(a+r0*(width*.5f-.14f)*sign+Vector3.up*.78f,b+r1*(width*.5f-.14f)*sign+Vector3.up*.78f,r0,r1,.20f,.22f);
     while(nextPost<distance+length)
     {
      float t=(nextPost-distance)/length;var p=Vector3.Lerp(a,b,t);var side=Vector3.Lerp(r0,r1,t).normalized;
      foreach(int sign in new[]{-1,1})build.Box(p+side*(width*.5f-.14f)*sign+Vector3.up*.4f,new Vector3(.28f,.8f,.28f),Quaternion.identity);
      nextPost+=3.8f;
     }
     while(nextPier<distance+length)
     {
      var p=Vector3.Lerp(a,b,(nextPier-distance)/length);float bottom=field.Sample(p.x,p.z)-.12f,height=p.y-bottom-.3f;
      var forward=b-a;forward.y=0;if(height>1)build.Box(new Vector3(p.x,bottom+height*.5f,p.z),new Vector3(width*.55f,height,1.8f),Quaternion.LookRotation(forward));nextPier+=14;
     }
     distance+=length;
    }
    // The first flat cross-section can stand above a sloping bank away from
    // its centre. Visible masonry aprons provide the same continuous support
    // across the walking width that the centreline profile already provides.
    foreach(bool start in new[]{true,false})
    {
     int index=start?0:points.Length-1;var end=points[index]+Vector3.up*.025f;
     var inward=points[start?1:points.Length-2]-points[index];inward.y=0;inward.Normalize();var lateral=Vector3.Cross(Vector3.up,inward);
     const int columns=12,rows=15;var grid=new Vector3[rows+1,columns+1];
     for(int row=0;row<=rows;row++)
     {
      float along=Mathf.Lerp(-6,1.5f,row/(float)rows);float blend=Mathf.SmoothStep(0,1,Mathf.Clamp01((along+6)/6));
      float deckY=end.y,remaining=Mathf.Max(0,along);
      for(int n=1;n<points.Length&&remaining>0;n++)
      {
       var p0=points[start?n-1:points.Length-n];var p1=points[start?n:points.Length-1-n];float length=Vector2.Distance(new Vector2(p0.x,p0.z),new Vector2(p1.x,p1.z));
       if(length<.0001f)continue;deckY=Mathf.Lerp(p0.y,p1.y,Mathf.Clamp01(remaining/length))+.025f;remaining-=length;
      }
      for(int column=0;column<=columns;column++)
      {
       var p=end+inward*along+lateral*Mathf.Lerp(-width*.5f,width*.5f,column/(float)columns);
       float ground=field.Sample(p.x,p.z)+.025f;
       p.y=Mathf.Max(ground,Mathf.Lerp(ground,deckY+.012f,blend));grid[row,column]=p;
      }
     }
     for(int row=1;row<=rows;row++)for(int column=1;column<=columns;column++)
      build.Surface(grid[row-1,column-1],grid[row-1,column],grid[row,column-1],grid[row,column],.12f);
    }
    var mesh=Asset295("Meshes/Crossing_"+crossing.Id+".asset",()=>new Mesh());build.Apply(mesh);EditorUtility.SetDirty(mesh);
    var go=MeshObject278(crossing.Id,mesh,stone,root.transform,true);
   }
   report.Add("Permanent first-visit bridges with continuous approach collision="+root.transform.childCount+"; visible terrain-conforming6m masonry apron at each bank, shared render/collision mesh.");
  }
  sealed class BridgeMesh295
  {
   readonly List<Vector3> v=new List<Vector3>();readonly List<Vector2> uv=new List<Vector2>();readonly List<int> t=new List<int>();
   void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
   {int n=v.Count;v.AddRange(new[]{a,b,c,d});float w=Vector3.Distance(a,b),h=Vector3.Distance(b,c);uv.AddRange(new[]{Vector2.zero,new Vector2(w,0),new Vector2(w,h),new Vector2(0,h)});t.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
   public void Ribbon(Vector3 a,Vector3 b,Vector3 r0,Vector3 r1,float width,float depth)
   {
    var al=a-r0*width*.5f;var ar=a+r0*width*.5f;var bl=b-r1*width*.5f;var br=b+r1*width*.5f;var down=Vector3.down*depth;
    Quad(al,bl,br,ar);Quad(al+down,ar+down,br+down,bl+down);
    Quad(al+down,bl+down,bl,al);Quad(br+down,ar+down,ar,br);Quad(ar+down,al+down,al,ar);Quad(bl+down,br+down,br,bl);
   }
   public void Surface(Vector3 al,Vector3 ar,Vector3 bl,Vector3 br,float depth)
   {
    var down=Vector3.down*depth;Quad(al,bl,br,ar);Quad(al+down,ar+down,br+down,bl+down);
    Quad(al+down,bl+down,bl,al);Quad(br+down,ar+down,ar,br);Quad(ar+down,al+down,al,ar);Quad(bl+down,br+down,br,bl);
   }
   public void Box(Vector3 centre,Vector3 size,Quaternion rotation)
   {var a=centre+rotation*new Vector3(0,size.y*.5f,-size.z*.5f);var b=centre+rotation*new Vector3(0,size.y*.5f,size.z*.5f);Ribbon(a,b,rotation*Vector3.right,rotation*Vector3.right,size.x,size.y);}
   public void Apply(Mesh mesh){mesh.Clear();mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();}
  }
 }
}
