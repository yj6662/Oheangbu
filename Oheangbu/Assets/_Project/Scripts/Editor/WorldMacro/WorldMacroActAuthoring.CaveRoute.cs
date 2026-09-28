using System;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroActAuthoring
    {
        sealed class RouteNode {public Vector2Int key;public Vector3 p;public float g=float.PositiveInfinity,h;public RouteNode parent;public bool closed;}
        static string RepairCaveRoute()
        {
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%");
            var s=Session;var cave=GameObject.Find("Playtest_NaturalCave").transform;
            Vector3 start=cave.TransformPoint(new Vector3(8,0,-38));
            int prefix=Enumerable.Range(0,s.Content.MainPath.Length).OrderBy(i=>Vector3.Distance(s.Content.MainPath[i],cave.TransformPoint(new Vector3(0,0,-26)))).First();
            int join=Enumerable.Range(0,s.Content.MainPath.Length).Where(i=>s.Content.MainPath[i].z>=625&&s.Content.MainPath[i].z<630&&i>prefix).First();
            Vector3 goal=s.Content.MainPath[join];
            bool Ground(Vector3 p,out Vector3 floor)
            {
                floor=default;
                var hits=Physics.RaycastAll(new Vector3(p.x,1600,p.z),Vector3.down,2400,1,QueryTriggerInteraction.Ignore)
                    .Where(h=>h.normal.y>.71f&&(h.collider.name.StartsWith("Terrain_")||h.collider.name=="Continuous_Approach_Soil")&&h.collider.gameObject.scene==s.gameObject.scene).OrderByDescending(h=>h.point.y).ToArray();
                foreach(var hit in hits)
                {
                    var at=hit.point+Vector3.up*.05f;
                    if(s.Traversal!=null&&!s.Traversal.IsPermanentDrySupport(hit.point,hit.collider))continue;
                    if(Physics.OverlapCapsule(at+Vector3.up*.52f,at+Vector3.up*1.47f,.27f,1,QueryTriggerInteraction.Ignore).Any(c=>!c.transform.IsChildOf(s.Walker.Body.transform)&&c.attachedRigidbody==null))continue;
                    floor=at;return true;
                }
                return false;
            }
            if(!Ground(start,out start)||!Ground(goal,out goal))throw new InvalidOperationException("Actual soil exit/outer join lacks standing clearance");
            var nodes=new Dictionary<Vector2Int,RouteNode>();var rejected=new HashSet<Vector2Int>();var open=new List<RouteNode>();
            var first=new RouteNode{key=Vector2Int.zero,p=start,g=0,h=Vector3.Distance(start,goal)};nodes[first.key]=first;open.Add(first);RouteNode last=null;
            int expanded=0;
            while(open.Count>0&&expanded<12000)
            {
                int best=0;for(int i=1;i<open.Count;i++)if(open[i].g+open[i].h<open[best].g+open[best].h)best=i;
                var node=open[best];open.RemoveAt(best);if(node.closed)continue;node.closed=true;expanded++;
                if(new Vector2(node.p.x-goal.x,node.p.z-goal.z).magnitude<2.5f&&Mathf.Abs(node.p.y-goal.y)<1.5f){last=node;break;}
                for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
                {
                    if(dx==0&&dz==0)continue;var key=node.key+new Vector2Int(dx,dz);
                    if(key.x< -25||key.x>45||key.y< -15||key.y>100||rejected.Contains(key))continue;
                    if(!nodes.TryGetValue(key,out var next))
                    {
                        var at=start+new Vector3(key.x*2,0,key.y*2);
                        if(!Ground(at,out var floor)){rejected.Add(key);continue;}
                        next=new RouteNode{key=key,p=floor,h=Vector3.Distance(floor,goal)};nodes[key]=next;
                    }
                    if(next.closed)continue;
                    var delta=next.p-node.p;float horizontal=new Vector2(delta.x,delta.z).magnitude;
                    if(Mathf.Abs(delta.y)>Mathf.Tan(30*Mathf.Deg2Rad)*horizontal)continue;
                    if(!ClearLine(node.p,next.p))continue;
                    float g=node.g+delta.magnitude;if(g>=next.g)continue;next.g=g;next.parent=node;if(!open.Contains(next))open.Add(next);
                }
            }
            if(last==null)return Write("cave_route_repair.json",new Report{status="NO_SAFE_ROUTE_FOUND",scope="Physical support/clearance search around existing mountain, no geometry changed",checks=new List<string>{"Expanded "+expanded}});
            var raw=new List<Vector3>{goal};for(var n=last;n!=null;n=n.parent)raw.Add(n.p);raw.Reverse();
            bool ClearLine(Vector3 a,Vector3 b)
            {
                int steps=Mathf.CeilToInt(Vector3.Distance(a,b)/.5f);Vector3 previous=a;
                var right=Vector3.Cross(Vector3.up,b-a).normalized;
                for(int i=1;i<=steps;i++)
                {
                    if(!Ground(Vector3.Lerp(a,b,(float)i/steps),out var p))return false;
                    float horizontal=new Vector2(p.x-previous.x,p.z-previous.z).magnitude;
                    if(Mathf.Abs(p.y-previous.y)>Mathf.Tan(30*Mathf.Deg2Rad)*horizontal+.02f)return false;
                    foreach(float side in new[]{-.5f,.5f})if(!Ground(p+right*side,out var edge)||Mathf.Abs(edge.y-p.y)>.55f)return false;
                    previous=p;
                }
                return true;
            }
            var corners=new List<Vector3>{raw[0]};int cursor=0;
            while(cursor<raw.Count-1)
            {
                int next=cursor+1;for(int i=raw.Count-1;i>cursor+1;i--)if(ClearLine(raw[cursor],raw[i])){next=i;break;}
                if(!ClearLine(raw[cursor],raw[next]))return Write("cave_route_repair.json",new Report{status="WIDTH_OR_GRADE_FAILURE",scope="No scene changes",failures=new List<string>{raw[cursor]+" → "+raw[next]}});
                corners.Add(raw[next]);cursor=next;
            }
            var points=new List<Vector3>();
            var from=s.Content.MainPath[prefix];
            foreach(var to in corners)
            {
                int steps=Math.Max(1,Mathf.CeilToInt(Vector3.Distance(from,to)/.5f));
                for(int i=1;i<=steps;i++){if(!Ground(Vector3.Lerp(from,to,(float)i/steps),out var p))throw new InvalidOperationException("Resampled route lost support");points.Add(p);}
                from=to;
            }
            s.Content.MainPath=s.Content.MainPath.Take(prefix+1).Concat(points).Concat(s.Content.MainPath.Skip(join+1)).ToArray();
            EditorUtility.SetDirty(s.Content);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(s.gameObject.scene);EditorSceneManager.SaveScene(s.gameObject.scene);
            return Write("cave_route_repair.json",new Report{status="PHYSICS_ROUTE_REPAIRED",scope="Replaced stale exterior centerline through the mountain with supported ground around the actual portal. No roof accepted as floor; no terrain or cave wall generated.",checks=new List<string>{"Expanded "+expanded,"Corners "+corners.Count,"Samples "+points.Count,"Standing capsule corridor 1.54m wide and slope <=30 degrees sampled every 0.5m"}});
        }
    }
}
