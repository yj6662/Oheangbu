using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Subtracts sampled cave air from the copied entrance terrain, rather than removing whole centroid-selected faces.</summary>
    public static class NaturalCaveTerrainTrim
    {
        struct Vertex
        {
            public Vector3 P, N;
            public Vector2 U;
            public Color C;
            public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex
            { P=Vector3.Lerp(a.P,b.P,t), N=Vector3.Lerp(a.N,b.N,t).normalized, U=Vector2.Lerp(a.U,b.U,t), C=Color.Lerp(a.C,b.C,t) };
        }
        struct AirCell
        {
            public Plane[] Planes;
            public float Roof;
        }
        const float MinX=-57, MaxX=-34, MinZ=-18, MaxZ=32;
        const float Floor=.025f;
        const int NX=23, NZ=100;
        static float DX=>(MaxX-MinX)/NX;
        static float DZ=>(MaxZ-MinZ)/NZ;
        public static string Execute(string command)
        {
            if(command=="apply")return Apply();
            throw new ArgumentException(command);
        }
        public static string Apply()
        {
            if(EditorApplication.isPlaying || SceneManager.GetActiveScene().path!=WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Playtest Edit scene required.");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit guard >=85%.");
            var root=GameObject.Find(WorldMacroNaturalCave.RootName)?.transform;
            if(root==null)throw new InvalidOperationException("Natural cave missing.");
            var wall=root.Find("Natural_Cave_Interior").GetComponent<MeshCollider>();
            var heights=new float[NX+1,NZ+1];
            int samples=0;
            bool back=Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces=true;
            try
            {
                for(int x=0;x<=NX;x++)for(int z=0;z<=NZ;z++)
                {
                    // In front of the exact opening plane the same aperture continues through the hill shoulder.
                    float px=Mathf.Min(-43.06f,MinX+x*DX), pz=MinZ+z*DZ;
                    var ray=new Ray(root.TransformPoint(new Vector3(px,60,pz)),Vector3.down);
                    if(wall.Raycast(ray,out var hit,60-Floor))
                    {
                        float h=root.InverseTransformPoint(hit.point).y;
                        if(h>Floor+.12f){heights[x,z]=h; samples++;}
                    }
                }
            }
            finally {Physics.queriesHitBackfaces=back;}
            if(samples<100)throw new InvalidOperationException("Too few actual roof samples; no terrain edited: "+samples);
            var cells=new AirCell[NX,NZ];int validCells=0;
            for(int x=0;x<NX;x++)for(int z=0;z<NZ;z++)
            {
                float a=heights[x,z],b=heights[x,z+1],c=heights[x+1,z],d=heights[x+1,z+1];
                if(Mathf.Min(a,b,c,d)<=Floor+.12f)continue;
                // A conservative ruled roof remains inside both measured end sections. The small overlap
                // sits behind the real cave wall and prevents microscopic slivers at the shared rim.
                float h0=Mathf.Min(a,c)+.025f,h1=Mathf.Min(b,d)+.025f;
                float x0=MinX+x*DX,x1=x0+DX,z0=MinZ+z*DZ,z1=z0+DZ;
                var topNormal=new Vector3(0,1,-(h1-h0)/DZ).normalized;
                cells[x,z]=new AirCell {Roof=Mathf.Max(h0,h1),Planes=new[] {
                    new Plane(Vector3.left,new Vector3(x0,0,0)),new Plane(Vector3.right,new Vector3(x1,0,0)),
                    new Plane(Vector3.back,new Vector3(0,0,z0)),new Plane(Vector3.forward,new Vector3(0,0,z1)),
                    new Plane(Vector3.down,new Vector3(0,Floor,0)),new Plane(topNormal,new Vector3(0,h0,z0)) }};
                validCells++;
            }
            var lines=new List<string>{"Actual cave roof ray samples="+samples+"; air cells="+validCells+"; lower retained ground y="+Floor};
            foreach(string name in new[]{"Terrain_052","Terrain_060"})
            {
                var go=GameObject.Find(name);if(go==null)continue;
                var filter=go.GetComponent<MeshFilter>();var source=filter.sharedMesh;
                string sourcePath=AssetDatabase.GetAssetPath(source);
                if(!sourcePath.Contains("/NaturalCave/"))throw new InvalidOperationException("Refuse to replace original terrain directly: "+sourcePath);
                if(sourcePath.Contains("_PortalTrim")){lines.Add("SKIP already trimmed "+name);continue;}
                var positions=source.vertices;var normals=source.normals;var uvs=source.uv;var colors=source.colors;var indices=source.triangles;
                Matrix4x4 toCave=root.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                Matrix4x4 fromCave=filter.transform.worldToLocalMatrix*root.localToWorldMatrix;
                var sourceVertices=new Vertex[positions.Length];
                for(int i=0;i<positions.Length;i++)sourceVertices[i]=new Vertex{P=toCave.MultiplyPoint3x4(positions[i]),N=normals.Length==positions.Length?normals[i]:Vector3.up,U=uvs.Length==positions.Length?uvs[i]:Vector2.zero,C=colors.Length==positions.Length?colors[i]:Color.white};
                var result=new List<Vertex>();int touched=0,removed=0,fragments=0;
                for(int i=0;i<indices.Length;i+=3)
                {
                    var a=sourceVertices[indices[i]];var b=sourceVertices[indices[i+1]];var c=sourceVertices[indices[i+2]];
                    float x0=Mathf.Min(a.P.x,b.P.x,c.P.x),x1=Mathf.Max(a.P.x,b.P.x,c.P.x),z0=Mathf.Min(a.P.z,b.P.z,c.P.z),z1=Mathf.Max(a.P.z,b.P.z,c.P.z);
                    if(x1<MinX||x0>MaxX||z1<MinZ||z0>MaxZ||Mathf.Max(a.P.y,b.P.y,c.P.y)<=Floor)
                    {result.AddRange(new[]{a,b,c});continue;}
                    int startX=Mathf.Clamp(Mathf.FloorToInt((x0-MinX)/DX),0,NX-1),endX=Mathf.Clamp(Mathf.FloorToInt((x1-MinX)/DX),0,NX-1);
                    int startZ=Mathf.Clamp(Mathf.FloorToInt((z0-MinZ)/DZ),0,NZ-1),endZ=Mathf.Clamp(Mathf.FloorToInt((z1-MinZ)/DZ),0,NZ-1);
                    var pieces=new List<List<Vertex>>{new List<Vertex>{a,b,c}};bool changed=false;
                    for(int x=startX;x<=endX&&pieces.Count>0;x++)for(int z=startZ;z<=endZ&&pieces.Count>0;z++)
                    {
                        var cell=cells[x,z];if(cell.Planes==null||Mathf.Min(a.P.y,b.P.y,c.P.y)>cell.Roof+.01f)continue;
                        var next=new List<List<Vertex>>();
                        foreach(var polygon in pieces)Subtract(polygon,cell.Planes,next,ref changed);
                        pieces=next;
                    }
                    if(changed)touched++;
                    if(pieces.Count==0)removed++;
                    foreach(var polygon in pieces)for(int j=1;j<polygon.Count-1;j++)
                    {
                        if(Vector3.Cross(polygon[j].P-polygon[0].P,polygon[j+1].P-polygon[0].P).sqrMagnitude<1e-10f)continue;
                        result.AddRange(new[]{polygon[0],polygon[j],polygon[j+1]});fragments++;
                    }
                }
                var mesh=new Mesh{name=name+"_PortalTrim",indexFormat=IndexFormat.UInt32};
                mesh.SetVertices(result.Select(v=>fromCave.MultiplyPoint3x4(v.P)).ToList());
                mesh.SetNormals(result.Select(v=>v.N).ToList());mesh.SetUVs(0,result.Select(v=>v.U).ToList());
                if(colors.Length>0)mesh.SetColors(result.Select(v=>v.C).ToList());
                mesh.SetTriangles(Enumerable.Range(0,result.Count).ToArray(),0);mesh.RecalculateBounds();
                string path=WorldMacroNaturalCave.Folder+"/Meshes/"+mesh.name+".asset";
                if(AssetDatabase.LoadAssetAtPath<Mesh>(path)!=null)throw new InvalidOperationException("A previous trim asset exists; inspect it before another application.");
                AssetDatabase.CreateAsset(mesh,path);filter.sharedMesh=mesh;
                var collider=filter.GetComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=mesh;
                lines.Add(name+" source="+sourcePath+" tris="+indices.Length/3+" -> "+result.Count/3+" touched="+touched+" fullyRemoved="+removed+" localFragments="+fragments);
            }
            Physics.SyncTransforms();
            var checkpoint=root.TransformPoint(new Vector3(-44,.13f,5));
            lines.Add("Portal centre supporting hits="+string.Join(", ",Physics.RaycastAll(checkpoint+Vector3.up*1.7f,Vector3.down,3f,1).OrderBy(h=>h.distance).Select(h=>h.collider.name+":"+root.InverseTransformPoint(h.point).y.ToString("F3"))));
            File.WriteAllLines(WorldMacroNaturalCave.Output+"/portal_terrain_trim.txt",lines);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return string.Join("\n",lines);
        }

        static void Subtract(List<Vertex> polygon, Plane[] planes, List<List<Vertex>> outside, ref bool changed)
        {
            // If one half-space excludes the complete polygon there is no volume overlap and no split.
            foreach(var plane in planes)if(polygon.All(v=>plane.GetDistanceToPoint(v.P)>.00001f)){outside.Add(polygon);return;}
            var remainder=polygon;
            foreach(var plane in planes)
            {
                if(remainder.Count<3)return;
                var inner=new List<Vertex>();var outer=new List<Vertex>();
                for(int i=0;i<remainder.Count;i++)
                {
                    var a=remainder[i];var b=remainder[(i+1)%remainder.Count];
                    float da=plane.GetDistanceToPoint(a.P),db=plane.GetDistanceToPoint(b.P);bool ia=da<=.00001f,ib=db<=.00001f;
                    if(ia)inner.Add(a);else outer.Add(a);
                    if(ia!=ib){var v=Vertex.Lerp(a,b,Mathf.Clamp01(da/(da-db)));inner.Add(v);outer.Add(v);}
                }
                if(outer.Count>=3)outside.Add(outer);
                remainder=inner;
            }
            if(remainder.Count>=3)changed=true; // The final inside piece is intentionally discarded.
        }
    }
}
