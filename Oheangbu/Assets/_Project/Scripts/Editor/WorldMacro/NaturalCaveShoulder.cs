using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Local entrance earthwork. Retains the carved air and existing tested walking strip.</summary>
    public static class NaturalCaveShoulder
    {
        const string GroupName="Mountain_Entrance_Transition";
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
            if(root==null || root.Find(GroupName)!=null)throw new InvalidOperationException("Missing cave or entrance transition already installed.");
            var temporary=new List<GameObject>();
            var originalColliders=new List<MeshCollider>();
            var originalHeight=new Dictionary<Vector2Int,float>();
            var lines=new List<string>();
            try
            {
                // Temporary query-only original terrain. Layer 31 excludes it from all gameplay mask-1 queries.
                foreach(string name in new[]{"Terrain_052","Terrain_060"})
                {
                    var source=AssetDatabase.LoadAssetAtPath<Mesh>(WorldMacroBuilder.Folder+"/Meshes/"+name+".asset");
                    if(source==null)throw new InvalidOperationException("Missing original terrain "+name);
                    var active=GameObject.Find(name).transform;
                    var query=new GameObject("~OriginalGroundQuery_"+name){hideFlags=HideFlags.HideAndDontSave,layer=31};
                    query.transform.SetPositionAndRotation(active.position,active.rotation);query.transform.localScale=active.lossyScale;
                    query.AddComponent<MeshCollider>().sharedMesh=source;temporary.Add(query);originalColliders.Add(query.GetComponent<MeshCollider>());
                }
                Physics.SyncTransforms();
                float OriginalY(Vector3 p)
                {
                    var key=new Vector2Int(Mathf.RoundToInt(p.x*1000),Mathf.RoundToInt(p.z*1000));
                    if(originalHeight.TryGetValue(key,out var cached))return cached;
                    var ray=new Ray(root.TransformPoint(new Vector3(p.x,1000,p.z)),Vector3.down);
                    float best=float.MinValue;
                    foreach(var collider in originalColliders)if(collider.Raycast(ray,out var hit,2000))best=Mathf.Max(best,root.InverseTransformPoint(hit.point).y);
                    if(best==float.MinValue)throw new InvalidOperationException("Original ground sample missed at "+p);
                    originalHeight.Add(key,best);return best;
                }

                // Remove the artificial discontinuity without filling the already carved portal or raising
                // anything into the tested path. The exact retained triangles/UVs of PortalTrim are preserved.
                foreach(string name in new[]{"Terrain_052","Terrain_060"})
                {
                    var filter=GameObject.Find(name).GetComponent<MeshFilter>();var current=filter.sharedMesh;
                    string path=AssetDatabase.GetAssetPath(current);
                    if(!path.Contains("_PortalTrim"))throw new InvalidOperationException("Expected carved PortalTrim terrain: "+path);
                    var mesh=Object.Instantiate(current);mesh.name=name+"_SoftShoulder";
                    var positions=mesh.vertices;int changed=0;float largest=0;
                    for(int i=0;i<positions.Length;i++)
                    {
                        var p=root.InverseTransformPoint(filter.transform.TransformPoint(positions[i]));
                        if(p.x< -78||p.x> -41||p.z< -48||p.z>48)continue;
                        float baseY=OriginalY(p);
                        if(p.y<=baseY+.001f)continue;
                        float blend=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-77,-44,p.x));
                        float y=Mathf.Lerp(p.y,baseY,blend);largest=Mathf.Max(largest,p.y-y);
                        p.y=y;positions[i]=filter.transform.InverseTransformPoint(root.TransformPoint(p));changed++;
                    }
                    mesh.vertices=positions;mesh.RecalculateNormals();mesh.RecalculateBounds();mesh=Save(mesh);
                    filter.sharedMesh=mesh;var collider=filter.GetComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=mesh;
                    lines.Add(name+" lowered artificial collar vertices="+changed+" max="+largest.ToString("F3")+"m; no height increased; source="+path);
                }
                Physics.SyncTransforms();
                var group=new GameObject(GroupName).transform;group.SetParent(root,false);
                var wall=root.Find("Natural_Cave_Interior").GetComponent<MeshCollider>();
                var wallMesh=wall.sharedMesh;
                // The section is taken from the actual clipped rim, not an ideal ellipse or octagon.
                var edge=wallMesh.vertices.Where(p=>Mathf.Abs(p.x+43)<.0015f).GroupBy(p=>Mathf.RoundToInt(p.z*100))
                    .Select(g=>g.OrderByDescending(p=>p.y).First()).OrderBy(p=>p.z).ToArray();
                if(edge.Length<12)throw new InvalidOperationException("Exact cave aperture boundary missing.");
                float middle=(edge.First().z+edge.Last().z)*.5f, high=edge.Max(p=>p.y);
                var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
                const int rings=14;
                bool oldBack=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=true;
                try
                {
                    for(int ring=0;ring<=rings;ring++)for(int i=0;i<edge.Length;i++)
                    {
                        var inner=edge[i];inner.x=-42.94f;inner.y+=.065f;
                        float crown=Mathf.Clamp01(inner.y/high);
                        var outer=new Vector3(Mathf.Lerp(-58,-80,Mathf.Sqrt(crown)),0,middle+(inner.z-middle)*1.48f);
                        outer.y=OriginalY(outer)+.01f;
                        float t=ring/(float)rings;var p=Vector3.Lerp(inner,outer,t);
                        p.y=Mathf.Lerp(inner.y,outer.y,Mathf.SmoothStep(0,1,t));
                        if(ring>0)
                        {
                            var ray=new Ray(root.TransformPoint(new Vector3(p.x,70,p.z)),Vector3.down);
                            if(wall.Raycast(ray,out var roof,70))p.y=Mathf.Max(p.y,root.InverseTransformPoint(roof.point).y+.07f);
                            p.y=Mathf.Max(p.y,OriginalY(p)+.01f);
                        }
                        vertices.Add(p);uv.Add(new Vector2(p.x,p.z)*.16f);
                    }
                }
                finally {Physics.queriesHitBackfaces=oldBack;}
                for(int r=0;r<rings;r++)for(int i=0;i<edge.Length-1;i++)
                {int a=r*edge.Length+i,b=a+edge.Length;triangles.AddRange(new[]{a,b,a+1,a+1,b,b+1});}
                var shoulder=new Mesh{name="Mountain_Rock_Transition",indexFormat=IndexFormat.UInt32};shoulder.SetVertices(vertices);shoulder.SetUVs(0,uv);shoulder.SetTriangles(triangles,0);shoulder.RecalculateNormals();shoulder.RecalculateBounds();shoulder.RecalculateTangents();
                var material=new Material(root.Find("Natural_Cave_Interior").GetComponent<Renderer>().sharedMaterial){name="Exterior_Entrance_Rock"};
                material.SetColor("_BaseColor",new Color(.46f,.47f,.435f));material.SetFloat("_CaveAmbient",.16f);material.SetFloat("_LightResponse",.5f);
                string materialPath=WorldMacroNaturalCave.Folder+"/Materials/Exterior_Entrance_Rock.mat";
                if(AssetDatabase.LoadAssetAtPath<Material>(materialPath)!=null)throw new InvalidOperationException("Existing entrance material; inspect previous work before rerun.");
                AssetDatabase.CreateAsset(material,materialPath);Surface(group,Save(shoulder),material);
                lines.Add("Irregular rock transition follows "+edge.Length+" actual aperture vertices, "+rings+" rings; joins original terrain 15–37m behind the entrance.");

                // A broad, ground-level apron covers the cut-away ground on either side of the existing
                // narrow strip. Its centre lies below the already tested walking surface, not above it.
                var approach=root.Find("Exterior_Mine_Approach").GetComponent<MeshCollider>();
                vertices.Clear();uv.Clear();triangles.Clear();
                const int AX=40,AZ=64;
                for(int x=0;x<=AX;x++)for(int z=0;z<=AZ;z++)
                {
                    var p=new Vector3(Mathf.Lerp(-53,-14,x/(float)AX),0,Mathf.Lerp(-27,37,z/(float)AZ));
                    float baseY=OriginalY(p), centerZ=Mathf.Lerp(5,-1,Mathf.InverseLerp(-43,-26,p.x));
                    float core=.010f+1.58f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(-43,-26,p.x));
                    float front=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-26,-18,p.x));core=Mathf.Lerp(core,baseY-.025f,front);
                    float side=Mathf.SmoothStep(0,1,Mathf.InverseLerp(15,28,Mathf.Abs(p.z-centerZ)));
                    p.y=Mathf.Lerp(Mathf.Min(core,baseY-.018f),baseY-.025f,side);
                    // Positive road lift near the entrance is valid: never drag the new underlay upward
                    // into it and do not read a mountain ceiling as ground.
                    var ray=new Ray(root.TransformPoint(new Vector3(p.x,15,p.z)),Vector3.down);
                    if(approach.Raycast(ray,out var walking,30))p.y=Mathf.Min(p.y,root.InverseTransformPoint(walking.point).y-.018f);
                    vertices.Add(p);uv.Add(new Vector2(p.x,p.z)*.3f);
                }
                for(int x=0;x<AX;x++)for(int z=0;z<AZ;z++)
                {int a=x*(AZ+1)+z,b=a+AZ+1;triangles.AddRange(new[]{a,a+1,b,b,a+1,b+1});}
                var apron=new Mesh{name="Natural_Mine_Broad_Apron",indexFormat=IndexFormat.UInt32};apron.SetVertices(vertices);apron.SetUVs(0,uv);apron.SetTriangles(triangles,0);apron.RecalculateNormals();apron.RecalculateBounds();apron.RecalculateTangents();
                Surface(group,Save(apron),root.Find("Natural_Cave_Floor").GetComponent<Renderer>().sharedMaterial);
                lines.Add("Broad irregular-height ground underlay: 39m x64m, "+triangles.Count/3+" tris, same floor material; bounds join original ground. Existing approach preserved.");
                Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                lines.Add("UNVERIFIED final visual acceptance and full moving performance; run existing route/capsule validation again after application.");
                File.WriteAllLines(WorldMacroNaturalCave.Output+"/entrance_transition.txt",lines);
                return string.Join("\n",lines);
            }
            finally {foreach(var go in temporary)Object.DestroyImmediate(go);Physics.SyncTransforms();}
        }
        static Mesh Save(Mesh mesh)
        {
            string path=WorldMacroNaturalCave.Folder+"/Meshes/"+mesh.name+".asset";
            if(AssetDatabase.LoadAssetAtPath<Mesh>(path)!=null)throw new InvalidOperationException("Refuse to replace existing transition asset "+path);
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static void Surface(Transform parent,Mesh mesh,Material material)
        {
            var go=new GameObject(mesh.name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;go.AddComponent<MeshCollider>().sharedMesh=mesh;go.isStatic=true;
        }
    }
}
