using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroActAuthoring
    {
        [Serializable] sealed class NavProgress {public string status,generation;public int volumes,next;public string[] surfaces,originalData;}
        static T[] Components<T>()where T:Component=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
        static string NamePath(Transform t)=>t.parent==null?t.name:NamePath(t.parent)+"/"+t.name;
        static string NavigationPrepare()
        {
            if(File.Exists(Path.Combine(Output,"terrain_navigation.json")))
            {
                var prior=JsonUtility.FromJson<NavProgress>(File.ReadAllText(Path.Combine(Output,"terrain_navigation.json")));
                if(prior.next>0||prior.volumes>0)throw new InvalidOperationException("Navigation already prepared; continue nav-step");
                var empty=GameObject.Find("ActsTerrain_DeepWaterNavigation");if(empty!=null&&empty.transform.childCount==0)Object.DestroyImmediate(empty);
            }
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%; no new navigation allocation");
            var q=Session.Traversal;if(q==null||q.Rules==null)throw new InvalidOperationException("Traversal missing");
            var root=new GameObject("ActsTerrain_DeepWaterNavigation");
            const int size=8;var cells=new HashSet<Vector2Int>();
            foreach(var t in q.Water)
            {
                var min=Vector3.Min(t.A,Vector3.Min(t.B,t.C));var max=Vector3.Max(t.A,Vector3.Max(t.B,t.C));
                for(int x=Mathf.FloorToInt(min.x/size);x<=Mathf.FloorToInt(max.x/size);x++)
                for(int z=Mathf.FloorToInt(min.z/size);z<=Mathf.FloorToInt(max.z/size);z++)cells.Add(new Vector2Int(x,z));
            }
            int count=0;
            foreach(var cell in cells.OrderBy(c=>c.x).ThenBy(c=>c.y))
            {
                Vector3 p=new Vector3((cell.x+.5f)*size,0,(cell.y+.5f)*size);float top=float.NegativeInfinity;bool covered=true;
                foreach(var offset in new[]{Vector3.zero,new Vector3(-4,0,-4),new Vector3(4,0,-4),new Vector3(-4,0,4),new Vector3(4,0,4)})
                {if(!q.TryWaterHeight(p+offset,out float y)){covered=false;break;}top=Mathf.Max(top,y);}
                if(!covered)continue;
                // Modifier only affects the submerged bed, never a bridge or a dry raised platform.
                var child=new GameObject("Bed_"+cell.x+"_"+cell.y);child.transform.SetParent(root.transform,false);
                child.transform.position=new Vector3(p.x,top-q.Rules.MaximumWadingDepth-10,p.z);
                var volume=child.AddComponent<NavMeshModifierVolume>();volume.area=1;volume.size=new Vector3(size,20,size);count++;
            }
            var surfaces=Components<NavMeshSurface>().OrderBy(s=>NamePath(s.transform)).ToArray();
            var progress=new NavProgress{status="PREPARED",volumes=count,surfaces=surfaces.Select(s=>NamePath(s.transform)).ToArray(),originalData=surfaces.Select(s=>AssetDatabase.GetAssetPath(s.navMeshData)).ToArray()};
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            return Write("terrain_navigation.json",progress);
        }
        static string NavigationRelayObstacle()
        {
            var collider=Components<Collider>().Single(c=>NamePath(c.transform)=="Demo_Chapter2_RelayAndLogging/OwnedStructures/Demo_Relay_Hanok/Surface_0_1");
            var bounds=collider.bounds;var existing=GameObject.Find("ActsTerrain_RelayPorchNavigation");
            var go=existing!=null?existing:new GameObject("ActsTerrain_RelayPorchNavigation");
            go.transform.position=new Vector3(bounds.center.x,bounds.center.y+2,bounds.center.z);
            var volume=go.GetComponent<NavMeshModifierVolume>();if(volume==null)volume=go.AddComponent<NavMeshModifierVolume>();
            volume.area=1;volume.size=new Vector3(bounds.size.x+.8f,12,bounds.size.z+.8f);
            EditorUtility.SetDirty(volume);EditorSceneManager.MarkSceneDirty(Session.gameObject.scene);EditorSceneManager.SaveScene(Session.gameObject.scene);
            return Write("relay_navigation_repair.json",new Report{status="AUTHORED_REBAKE_REQUIRED",scope="Navigation-only exclusion follows existing raised foundation plus agent clearance. Actual building mesh/collider preserved.",checks=new List<string>{"World bounds "+bounds,"Not-walkable volume "+volume.size}});
        }
        static string NavigationRestart()
        {
            var progress=JsonUtility.FromJson<NavProgress>(File.ReadAllText(Path.Combine(Output,"terrain_navigation.json")));
            File.Copy(Path.Combine(Output,"terrain_navigation.json"),Path.Combine(Output,"terrain_navigation_before_"+DateTime.UtcNow.ToString("HHmmss")+".json"));
            progress.generation=DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");progress.next=0;progress.status="PREPARED_AFTER_GEOMETRY_REPAIR";
            return Write("terrain_navigation.json",progress);
        }
        static string NavigationAudit()
        {
            var report=new Report{status="PASS",scope="Actual saved NavMesh data: actors and submerged triangle centroids. Native AI movement and full traversal separate."};
            var triangulation=NavMesh.CalculateTriangulation();int deep=0;var query=Session.Traversal;
            for(int i=0;i<triangulation.indices.Length;i+=3)
            {
                var p=(triangulation.vertices[triangulation.indices[i]]+triangulation.vertices[triangulation.indices[i+1]]+triangulation.vertices[triangulation.indices[i+2]])/3;
                if(query.IsDeep(p)){deep++;if(deep<30)report.failures.Add("Submerged navigation: "+p);}
            }
            report.checks.Add("Navigation triangles "+triangulation.indices.Length/3+", submerged="+deep);
            foreach(var actor in Session.Actors)
            {
                var agent=actor.GetComponent<NavMeshAgent>();var p=actor.transform.position-Vector3.up*agent.baseOffset;
                bool valid=NavMesh.SamplePosition(p,out var hit,2,agent.areaMask)&&Mathf.Abs(hit.position.y-p.y)<.8f&&!query.IsDeep(hit.position);
                (valid?report.checks:report.failures).Add("Actor "+actor.Id+" dry navigation support");
            }
            foreach(var link in Components<NavMeshLink>())
            {
                if(!link.isActiveAndEnabled){report.checks.Add("Disabled redundant unsafe seam: "+NamePath(link.transform));continue;}
                var a=link.transform.TransformPoint(link.startPoint);var b=link.transform.TransformPoint(link.endPoint);
                var path=new NavMeshPath();
                bool valid=NavMesh.SamplePosition(a,out var ah,.8f,NavMesh.AllAreas)&&NavMesh.SamplePosition(b,out var bh,.8f,NavMesh.AllAreas)&&
                    !query.IsDeep(ah.position)&&!query.IsDeep(bh.position)&&NavMesh.CalculatePath(ah.position,bh.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
                (valid?report.checks:report.failures).Add("Link endpoint connection "+NamePath(link.transform)+" "+a+" -> "+b);
            }
            if(report.failures.Count>0)report.status="FAIL";
            return Write("terrain_navigation_audit.json",report);
        }
        static string NavigationStep()
        {
            var progress=JsonUtility.FromJson<NavProgress>(File.ReadAllText(Path.Combine(Output,"terrain_navigation.json")));
            if(progress.next>=progress.surfaces.Length)return JsonUtility.ToJson(progress);
            if(Prologue.PrologueAudit.CommitRatio()>=.85f){progress.status="PAUSED_MEMORY_85_PERCENT";return Write("terrain_navigation.json",progress);}
            var surface=Components<NavMeshSurface>().Single(s=>NamePath(s.transform)==progress.surfaces[progress.next]);
            string folder=Folder+"/Navigation"+(string.IsNullOrEmpty(progress.generation)?"":"_"+progress.generation);Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            string path=folder+"/Surface_"+progress.next.ToString("D2")+".asset";
            var old=surface.navMeshData;var result=AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            try
            {
                if(result==null)
                {
                    surface.RemoveData();surface.navMeshData=null;Physics.SyncTransforms();surface.BuildNavMesh();result=surface.navMeshData;
                    if(result==null)throw new InvalidOperationException("No generated navigation");
                    surface.RemoveData();surface.navMeshData=null;AssetDatabase.CreateAsset(result,path);AssetDatabase.SaveAssets();
                }
                surface.RemoveData();surface.navMeshData=result;surface.AddData();
                EditorUtility.SetDirty(surface);EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);EditorSceneManager.SaveScene(surface.gameObject.scene);
                progress.next++;progress.status=progress.next==progress.surfaces.Length?"BAKED_NEEDS_PATH_CHECKS":"BAKING";
            }
            catch{surface.RemoveData();surface.navMeshData=old;surface.AddData();throw;}
            return Write("terrain_navigation.json",progress);
        }
    }
}
