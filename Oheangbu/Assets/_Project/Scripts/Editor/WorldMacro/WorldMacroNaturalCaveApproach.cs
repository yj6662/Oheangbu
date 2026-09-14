using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroNaturalCave
    {
        [Serializable] class RouteRecord{public int prefixCount;public Vector3[] exteriorTail;}
        static string Approach()
        {
            var root=Root;var old=root.Find("Exterior_Mine_Approach");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var mf=root.Find("Natural_Cave_Floor").GetComponent<MeshFilter>();var source=JsonUtility.FromJson<Geometry>(File.ReadAllText(Output+"/geometry.json")).meshes.First(m=>m.name=="Natural_Cave_Floor");
            var vs=new List<Vector3>();var ts=new List<int>();
            for(int i=0;i<source.triangles.Length;i+=3)
            {
                var p=new[]{source.vertices[source.triangles[i]],source.vertices[source.triangles[i+1]],source.vertices[source.triangles[i+2]]};var clip=new List<Vector3>();
                for(int j=0;j<3;j++){var a=p[j];var b=p[(j+1)%3];bool ia=a.x<=-43,ib=b.x<=-43;if(ia)clip.Add(a);if(ia!=ib)clip.Add(Vector3.Lerp(a,b,(-43-a.x)/(b.x-a.x)));}
                for(int j=1;j<clip.Count-1;j++){int n=vs.Count;vs.AddRange(new[]{clip[0],clip[j],clip[j+1]});ts.AddRange(new[]{n,n+1,n+2});}
            }
            var mesh=new Mesh{name="Natural_Cave_InnerFloor"};mesh.SetVertices(vs);mesh.SetTriangles(ts,0);mesh.uv=vs.Select(p=>new Vector2(p.x,p.z)*.3f).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();mesh=Save(mesh,mesh.name);mf.sharedMesh=mesh;mf.GetComponent<MeshCollider>().sharedMesh=mesh;
            float Ground(Vector3 p)
            {
                if(p.x<= -26)return .03f+1.58f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(-43,-26,p.x));var w=root.TransformPoint(p);var hits=Physics.RaycastAll(w+Vector3.up*35,Vector3.down,60,1).Where(h=>h.collider.name.StartsWith("Terrain_")).OrderBy(h=>h.distance).ToArray();
                return hits.Length==0?.03f:Mathf.Max(.03f,root.InverseTransformPoint(hits[0].point).y+.025f);
            }
            var nodes=new[]{new Vector3(-44,.03f,5),new Vector3(-26,.03f,-1),new Vector3(-9,.03f,-15),new Vector3(0,.03f,-26)};
            var centers=new List<Vector3>();for(int i=1;i<nodes.Length;i++){int count=Mathf.CeilToInt(Vector3.Distance(nodes[i-1],nodes[i]));for(int n=0;n<count;n++)centers.Add(Vector3.Lerp(nodes[i-1],nodes[i],n/(float)count));}centers.Add(nodes.Last());
            vs.Clear();ts.Clear();for(int i=0;i<centers.Count;i++)
            {
                var c=centers[i];var tangent=centers[Mathf.Min(i+1,centers.Count-1)]-centers[Mathf.Max(0,i-1)];var side=Vector3.Cross(Vector3.up,tangent).normalized;float width=c.x< -35?11f:Mathf.Lerp(11,2.2f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(-35,-25,c.x)));
                foreach(float offset in new[]{-width,0,width}){var p=c+side*offset;p.y=Ground(p);vs.Add(p);}
                if(i>0){int n=i*3;ts.AddRange(new[]{n-3,n,n-2,n-2,n,n+1,n-2,n+1,n-1,n-1,n+1,n+2});}
            }
            mesh=new Mesh{name="Exterior_Mine_Approach"};mesh.SetVertices(vs);mesh.SetTriangles(ts,0);mesh.uv=vs.Select(p=>new Vector2(p.x,p.z)*.3f).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();mesh=Save(mesh,mesh.name);var go=new GameObject(mesh.name);go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=Rock(true);go.AddComponent<MeshCollider>().sharedMesh=mesh;
            // The exterior tail after the old portal stays byte-for-byte unchanged.
            var record=JsonUtility.FromJson<RouteRecord>(File.ReadAllText(Output+"/gameplay_integration.json"));
            for(int i=0;i<record.exteriorTail.Length;i++)Session.Content.MainPath[record.prefixCount+i]=record.exteriorTail[i];
            for(int i=0;i<record.prefixCount;i++){var p=root.InverseTransformPoint(Session.Content.MainPath[i]);if(p.x> -43&&p.x<1&&p.z>=-26&&p.z<8){p.y=Ground(p)+.10f;Session.Content.MainPath[i]=root.TransformPoint(p);}}
            foreach(var terrain in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(f=>f.name.StartsWith("Terrain_")&&AssetDatabase.GetAssetPath(f.sharedMesh).Contains("/NaturalCave/")))
            {var terrainMesh=terrain.sharedMesh;var points=terrainMesh.vertices;bool changed=false;for(int i=0;i<points.Length;i++){var p=root.InverseTransformPoint(terrain.transform.TransformPoint(points[i]));if(p.x< -44||p.x> -25)continue;float t=Mathf.InverseLerp(-43,-26,p.x);float centerZ=Mathf.Lerp(5,-1,t);float distance=Mathf.Abs(p.z-centerZ);if(distance>14)continue;float desired=Ground(p)-.09f;float weight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(10,14,distance));p.y=Mathf.Lerp(p.y,Mathf.Min(p.y,desired),weight);points[i]=terrain.transform.InverseTransformPoint(root.TransformPoint(p));changed=true;}if(changed){terrainMesh.vertices=points;terrainMesh.RecalculateNormals();terrainMesh.RecalculateBounds();EditorUtility.SetDirty(terrainMesh);terrain.GetComponent<MeshCollider>().sharedMesh=null;terrain.GetComponent<MeshCollider>().sharedMesh=terrainMesh;}}
            var stack=GameObject.Find("MineStack");if(stack!=null)stack.SetActive(false);
            EditorUtility.SetDirty(Session.Content);EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());Physics.SyncTransforms();AssetDatabase.SaveAssets();return "Natural cave floor ends at hillside aperture; narrow terrain-following exterior approach installed; oversized old MineStack archived inactive.";
        }
    }
}
