using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        static Rect? actRefinementRegion;
        static WorldMacroRoadGradeSO refinementProfile;
        static RoadEdgeIndex refinementRoadIndex;
        sealed class RoadEdgeIndex
        {
            struct Edge {public Vector3 a,b;public float halfWidth;}
            readonly Dictionary<long,List<Edge>> cells=new Dictionary<long,List<Edge>>();
            static long Key(int x,int z)=>((long)x<<32)^(uint)z;
            public RoadEdgeIndex(WorldMacroRoadGradeSO profile)
            {
                foreach(var line in profile.Lines)for(int i=1;i<line.Points.Length;i++)
                {
                    var a=line.Points[i-1];var b=line.Points[i];float radius=line.Width*.5f+64;
                    var edge=new Edge{a=a,b=b,halfWidth=line.Width*.5f};
                    for(int z=Mathf.FloorToInt((Mathf.Min(a.z,b.z)-radius)/64);z<=Mathf.FloorToInt((Mathf.Max(a.z,b.z)+radius)/64);z++)
                    for(int x=Mathf.FloorToInt((Mathf.Min(a.x,b.x)-radius)/64);x<=Mathf.FloorToInt((Mathf.Max(a.x,b.x)+radius)/64);x++)
                    {long key=Key(x,z);if(!cells.TryGetValue(key,out var list)){list=new List<Edge>();cells.Add(key,list);}list.Add(edge);}
                }
            }
            public float BedDistance(Vector3 p)
            {
                if(!cells.TryGetValue(Key(Mathf.FloorToInt(p.x/64),Mathf.FloorToInt(p.z/64)),out var edges))return float.PositiveInfinity;
                float min=float.PositiveInfinity;
                foreach(var edge in edges)min=Mathf.Min(min,WorldMacroTerrain.SegmentDistance(p.x,p.z,edge.a,edge.b,out _)-edge.halfWidth);
                return min;
            }
        }

        // Edge decisions use world XZ only. Both triangles sharing an edge insert the
        // same midpoint, including both sides of a terrain chunk boundary. Original
        // triangle holes and source height interpolation remain intact.
        static Mesh BuildRefinedTerrain(Mesh original,MeshJob job,Transform target,WorldMacroRoadGradeSO profile,out float maxChange,bool preserveInputHeights=false)
        {
            var source=original.vertices;var world=source.Select(v=>preserveInputHeights?target.TransformPoint(v):Compression.Map(job.originalMatrix.MultiplyPoint3x4(v))).ToList();
            var uv=new List<Vector4>[8];
            for(int channel=0;channel<8;channel++){var list=new List<Vector4>();original.GetUVs(channel,list);if(list.Count==source.Length)uv[channel]=list;}
            var sourceColors=original.colors;List<Color> colors=sourceColors.Length==source.Length?sourceColors.ToList():null;
            var triangles=new List<int>[original.subMeshCount];for(int s=0;s<triangles.Length;s++)triangles[s]=original.GetTriangles(s).ToList();
            if(refinementProfile!=profile||refinementRoadIndex==null){refinementProfile=profile;refinementRoadIndex=new RoadEdgeIndex(profile);}
            var roads=refinementRoadIndex;
            for(int iteration=0;iteration<8;iteration++)
            {
                var midpoints=new Dictionary<ulong,int>();
                ulong Key(int a,int b)=>(ulong)(uint)Mathf.Min(a,b)<<32|(uint)Mathf.Max(a,b);
                void Consider(int a,int b)
                {
                    ulong key=Key(a,b);if(midpoints.ContainsKey(key))return;
                    var p=world[a];var q=world[b];float length=new Vector2(p.x-q.x,p.z-q.z).magnitude;
                    // Protected content already has a 2 m guard. Keep crossing edges
                    // below 1 m so a changed outside corner cannot reach its footprint.
                    var middle=(p+q)*.5f;
                    bool protectedP=profile.IsProtected(p.x,p.z),protectedQ=profile.IsProtected(q.x,q.z);
                    bool crossesProtection=protectedP!=protectedQ || protectedP!=profile.IsProtected(middle.x,middle.z);
                    float limit=crossesProtection?1f:2.5f;
                    if(actRefinementRegion.HasValue&&actRefinementRegion.Value.Contains(new Vector2(middle.x,middle.z)))limit=.5f;
                    if(length<=limit)return;
                    if(!crossesProtection && roads.BedDistance(middle)>10+length*.5f)return;
                    int index=world.Count;midpoints.Add(key,index);world.Add(middle);
                    for(int channel=0;channel<8;channel++)if(uv[channel]!=null)uv[channel].Add((uv[channel][a]+uv[channel][b])*.5f);
                    if(colors!=null)colors.Add((colors[a]+colors[b])*.5f);
                }
                foreach(var ts in triangles)for(int i=0;i<ts.Count;i+=3){Consider(ts[i],ts[i+1]);Consider(ts[i+1],ts[i+2]);Consider(ts[i+2],ts[i]);}
                if(midpoints.Count==0)break;
                for(int s=0;s<triangles.Length;s++)
                {
                    var previous=triangles[s];var next=new List<int>(previous.Count*2);
                    void Tri(int a,int b,int c){next.Add(a);next.Add(b);next.Add(c);}
                    for(int i=0;i<previous.Count;i+=3)
                    {
                        int a=previous[i],b=previous[i+1],c=previous[i+2];
                        bool ab=midpoints.TryGetValue(Key(a,b),out int p),bc=midpoints.TryGetValue(Key(b,c),out int q),ca=midpoints.TryGetValue(Key(c,a),out int r);
                        switch((ab?1:0)|(bc?2:0)|(ca?4:0))
                        {
                            case 0:Tri(a,b,c);break;
                            case 1:Tri(a,p,c);Tri(p,b,c);break;
                            case 2:Tri(b,q,a);Tri(q,c,a);break;
                            case 4:Tri(c,r,b);Tri(r,a,b);break;
                            case 3:Tri(b,q,p);Tri(a,p,c);Tri(p,q,c);break;
                            case 6:Tri(c,r,q);Tri(b,q,a);Tri(q,r,a);break;
                            case 5:Tri(a,p,r);Tri(c,r,b);Tri(r,p,b);break;
                            case 7:Tri(a,p,r);Tri(p,b,q);Tri(r,q,c);Tri(p,q,r);break;
                        }
                    }
                    triangles[s]=next;
                }
                if(world.Count>2000000)throw new InvalidOperationException("Compact road refinement per-chunk safety cap exceeded.");
            }
            maxChange=0;var vertices=new Vector3[world.Count];
            for(int i=0;i<vertices.Length;i++){var v=world[i];float y=preserveInputHeights?v.y:profile.Height(v.x,v.z,v.y);maxChange=Mathf.Max(maxChange,Mathf.Abs(y-v.y));v.y=y;vertices[i]=target.InverseTransformPoint(v);}
            var mesh=new Mesh{name=original.name+"_Compact",indexFormat=vertices.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.vertices=vertices;
            for(int channel=0;channel<8;channel++)if(uv[channel]!=null)mesh.SetUVs(channel,uv[channel]);
            if(colors!=null)mesh.SetColors(colors);
            mesh.subMeshCount=triangles.Length;for(int s=0;s<triangles.Length;s++)mesh.SetTriangles(triangles[s],s,false);
            mesh.RecalculateNormals();if(uv[0]!=null)mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
        }
    }
}
