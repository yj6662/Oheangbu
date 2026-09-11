using System.Globalization;
using System.Text.Json;
using UnityEngine;
using Oheangbu.EditorTools.SpellVFX120;

// Execute the actual factory against Unity's math structs, with a data-only Mesh
// collector. This is not Unity Editor/runtime validation; the parent owns that.
namespace UnityEngine
{
    public class Mesh
    {
        public string name;
        public Vector3[] vertices, normals;
        public Vector2[] uv;
        public Color[] colors;
        public int[] triangles;
        public void SetVertices(List<Vector3> v)=>vertices=v.ToArray();
        public void SetUVs(int channel,List<Vector2> v)=>uv=v.ToArray();
        public void SetColors(List<Color> v)=>colors=v.ToArray();
        public void SetTriangles(List<int> v,int submesh)=>triangles=v.ToArray();
        public void RecalculateNormals()
        {
            normals=new Vector3[vertices.Length];
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                var normal=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
                normals[a]+=normal;normals[b]+=normal;normals[c]+=normal;
            }
            for(int i=0;i<normals.Length;i++)normals[i].Normalize();
        }
        public void RecalculateTangents(){}
        public void RecalculateBounds(){}
    }
}

class Program
{
    static string Key(Vector3 v)=>$"{Math.Round(v.x,6)},{Math.Round(v.y,6)},{Math.Round(v.z,6)}";
    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        string output=args.Length>0?args[0]:".";Directory.CreateDirectory(output);
        var summaries=new List<object>();
        foreach(string family in Vfx120MeshFactory.Families)
        {
            Mesh m=Vfx120MeshFactory.Build(family);
            if(m.vertices.Any(v=>!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z)))
                throw new InvalidOperationException(family+" has non-finite vertices");
            var groupIds=new Dictionary<string,int>();var map=new int[m.vertices.Length];
            var groups=new Dictionary<int,List<int>>();
            for(int i=0;i<map.Length;i++)
            {
                string key=Key(m.vertices[i]);
                if(!groupIds.TryGetValue(key,out int group))groupIds[key]=group=groupIds.Count;
                map[i]=group;if(!groups.ContainsKey(group))groups[group]=new List<int>();groups[group].Add(i);
            }
            var edges=new Dictionary<(int,int),int>();int degenerate=0,reversed=0;
            for(int i=0;i<m.triangles.Length;i+=3)
            {
                int a=m.triangles[i],b=m.triangles[i+1],c=m.triangles[i+2];
                var normal=Vector3.Cross(m.vertices[b]-m.vertices[a],m.vertices[c]-m.vertices[a]);
                if(normal.sqrMagnitude<1e-14f)degenerate++;
                var midpoint=(m.vertices[a]+m.vertices[b]+m.vertices[c])/3;
                if(family=="Ring")
                {
                    // Normalization changes the original torus major radius.
                    float radius=.46f/(2*(.46f+.031f));
                    var axis=new Vector3(midpoint.x,midpoint.y,0).normalized*radius;
                    if(Vector3.Dot(normal,midpoint-axis)<=0)reversed++;
                }
                else if(family=="Rock"&&Vector3.Dot(normal,midpoint)<=0)reversed++;
                foreach(var e in new[]{(map[a],map[b]),(map[b],map[c]),(map[c],map[a])})
                {
                    var key=e.Item1<e.Item2?e:(e.Item2,e.Item1);
                    edges.TryGetValue(key,out int count);edges[key]=count+1;
                }
            }
            float seamNormalError=0;
            foreach(var group in groups.Values)
                foreach(int i in group)seamNormalError=Math.Max(seamNormalError,(m.normals[group[0]]-m.normals[i]).magnitude);
            int boundary=edges.Count(e=>e.Value==1),nonmanifold=edges.Count(e=>e.Value!=2);
            summaries.Add(new{family,vertices=m.vertices.Length,triangles=m.triangles.Length/3,
                finite=true,degenerateTriangles=degenerate,positionWeldBoundaryEdges=boundary,
                positionWeldNonmanifoldEdges=nonmanifold,reversedFaceNormals=reversed,maxDuplicateNormalError=seamNormalError});
            if(family=="Ring"||family=="Rock")
            {
                if(boundary!=0||nonmanifold!=0||degenerate!=0||reversed!=0||seamNormalError>1e-6f)
                    throw new InvalidOperationException(family+" failed geometric closure, winding or seam check");
                using var writer=new StreamWriter(Path.Combine(output,family+"_Repaired.obj"));
                writer.WriteLine("# Actual Vfx120MeshFactory with data-only Mesh collector; Unity math structs");
                foreach(var v in m.vertices)writer.WriteLine($"v {v.x:R} {v.y:R} {v.z:R}");
                foreach(var uv in m.uv)writer.WriteLine($"vt {uv.x:R} {uv.y:R}");
                foreach(var n in m.normals)writer.WriteLine($"vn {n.x:R} {n.y:R} {n.z:R}");
                for(int i=0;i<m.triangles.Length;i+=3)
                {
                    var t=m.triangles.Skip(i).Take(3).Select(v=>$"{v+1}/{v+1}/{v+1}");
                    writer.WriteLine("f "+string.Join(" ",t));
                }
            }
        }
        string json=JsonSerializer.Serialize(new{validation="Factory C# + actual Unity math, data-only Mesh substitute; no Unity Editor calls",families=summaries},new JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(Path.Combine(output,"factory_repair_checks.json"),json);Console.WriteLine(json);
    }
}
