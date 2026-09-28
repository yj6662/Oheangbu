using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroActAuthoring
    {
        static string ReuseShortcutSurface()
        {
            var site=Components<DemoGukRevisitSite>().Single();var marker=Components<WorldActShortcut>().Single();
            var slab=site.transform.Find("ExistingStone_Upper").GetComponent<MeshCollider>();var ramp=site.transform.Find("ExistingStone_Descent").GetComponent<MeshCollider>();
            var q=slab.transform.rotation;var center=site.LiftPad.position+q*new Vector3(0,0,2.55f);
            var path=new List<Vector3>();
            Vector3 Support(Vector3 p,Collider surface)
            {if(!surface.Raycast(new Ray(p+Vector3.up*6,Vector3.down),out var hit,12))throw new InvalidOperationException("Missing reused descent support");return hit.point+Vector3.up*.02f;}
            var start=site.UpperSurface.position;var join=center+q*new Vector3(1.85f,0,.75f);
            for(int i=0;i<=12;i++)path.Add(Support(Vector3.Lerp(start,join,i/12f),i<12?slab:ramp));
            for(int i=1;i<=34;i++)path.Add(Support(center+q*new Vector3(Mathf.Lerp(1.85f,10.12f,i/34f),0,.75f),ramp));
            var collider=marker.GetComponent<MeshCollider>();var renderer=marker.GetComponent<MeshRenderer>();
            var previous=marker.Path;bool oldCollider=collider.enabled,oldRenderer=renderer.enabled;
            marker.Path=path.ToArray();collider.enabled=false;renderer.enabled=false;Physics.SyncTransforms();
            string result=CheckShortcut();
            var report=JsonUtility.FromJson<Report>(result);
            if(report.failures.Count>0){marker.Path=previous;collider.enabled=oldCollider;renderer.enabled=oldRenderer;Physics.SyncTransforms();throw new InvalidOperationException("Existing surface corridor failed. Candidate not saved: "+string.Join(" | ",report.failures.Take(5)));}
            marker.DiscoveryPoint=start;
            EditorUtility.SetDirty(marker);EditorUtility.SetDirty(collider);EditorUtility.SetDirty(renderer);EditorSceneManager.MarkSceneDirty(Session.gameObject.scene);EditorSceneManager.SaveScene(Session.gameObject.scene);
            return Write("guk_descent.json",new Report{status="REUSED_EXISTING_SURFACE_PHYSICS_PASS",scope="Existing upper slab and 1.8m descent retained. Redundant intersecting generated ramp disabled; its mesh asset preserved. Discovery/path follows actual reused collider surface.",checks={"Upper-front lift height unchanged","47 centreline rows; 141 supported capsule samples","Native ascent/descent remains unverified"}});
        }
        static string Shortcut()
        {
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%; authoring held");
            var site=Components<DemoGukRevisitSite>().Single();var existing=Components<WorldActShortcut>().FirstOrDefault();
            if(existing!=null)return "Existing shortcut preserved; inspect before further editing";
            Vector3 start=site.UpperSurface.position+new Vector3(.15f,.02f,0),end=site.DescentExit.position;
            if(!Session.TrySafeFeet(end,out var safe))throw new InvalidOperationException("Existing descent exit has no safe dry support");
            end=safe-Vector3.up*.04f;
            var path=new Vector3[25];var vertices=new List<Vector3>();var triangles=new List<int>();var uvs=new List<Vector2>();
            for(int i=0;i<path.Length;i++)
            {
                float t=(float)i/(path.Length-1);var center=Vector3.Lerp(start,end,t);center.x+=Mathf.Sin(t*Mathf.PI)*.6f;path[i]=center;
                Vector3 forward=(end-start).normalized;forward.y=0;forward.Normalize();Vector3 right=Vector3.Cross(Vector3.up,forward);
                for(int side=0;side<4;side++)
                {
                    float x=side==0?-3.6f:side==1?-1.05f:side==2?1.05f:3.6f;var p=center+right*x;
                    if(side==0||side==3)
                    {
                        var hits=Physics.RaycastAll(p+Vector3.up*2,Vector3.down,10,1,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance).ToArray();
                        if(hits.Length==0)throw new InvalidOperationException("Missing slope shoulder support");
                        p.y=Mathf.Min(center.y-.01f,hits[0].point.y-.06f);
                    }
                    vertices.Add(p);uvs.Add(new Vector2(x,t*Vector3.Distance(start,end)));
                }
                if(i==0)continue;
                for(int side=0;side<3;side++)
                {
                    int a=(i-1)*4+side,b=a+1,c=i*4+side,d=c+1;
                    triangles.Add(a);triangles.Add(c);triangles.Add(b);triangles.Add(b);triangles.Add(c);triangles.Add(d);
                }
            }
            var mesh=new Mesh{name="Guk_Descent_Earth"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetUVs(0,uvs);mesh.RecalculateNormals();mesh.RecalculateBounds();
            // Match the terrain material; shoulders intersect the real ground instead of floating above it.
            var floor=Physics.RaycastAll(end+Vector3.up,Vector3.down,3,1,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance).First();
            var material=floor.collider.GetComponent<Renderer>()?.sharedMaterial;if(material==null)throw new InvalidOperationException("Terrain material missing");
            string meshPath=Folder+"/GukDescent.asset";AssetDatabase.CreateAsset(mesh,meshPath);
            var root=new GameObject("ActsTerrain_GukDescent");root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>().sharedMaterial=material;root.AddComponent<MeshCollider>().sharedMesh=mesh;
            var shortcut=root.AddComponent<WorldActShortcut>();shortcut.DiscoveryPoint=start;shortcut.Path=path;
            EditorSceneManager.MarkSceneDirty(Session.gameObject.scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(Session.gameObject.scene);
            return Write("guk_descent.json",new Report{status="AUTHORED_NEEDS_PLAY_CHECK",scope="Upper Guk site to existing descent exit and logging/inn/capital junction path",checks=new List<string>{"Length "+Vector3.Distance(start,end),"Same terrain material", "Existing front lift height and Guk reward proof preserved","Discovery saved independently of campaign progression"}});
        }
    }
}
