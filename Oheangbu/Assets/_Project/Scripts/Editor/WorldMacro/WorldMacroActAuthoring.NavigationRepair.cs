using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroActAuthoring
    {
        sealed class LinkPoint{public Vector3 p;public NavMeshSurface owner;}
        sealed class LinkWork{public NavMeshLink link;public Vector3 a,b;public List<LinkPoint> starts=new List<LinkPoint>(),ends=new List<LinkPoint>();}
        static string RepairNavigationLinks(bool widerSearch=false)
        {
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%");
            var report=new Report{status="REPAIRED_RECHECK_REQUIRED",scope="Derived seam endpoints only. Owning surface isolated for endpoint sampling; actual dry support and capsule clearance; native escort movement unverified."};
            var surfaces=Components<NavMeshSurface>();var links=Components<NavMeshLink>();var work=new List<LinkWork>();var query=Session.Traversal;
            foreach(var link in links)
            {
                var a=link.transform.TransformPoint(link.startPoint);var b=link.transform.TransformPoint(link.endPoint);
                var currentPath=new NavMeshPath();
                if(NavMesh.SamplePosition(a,out var ah,.8f,NavMesh.AllAreas)&&NavMesh.SamplePosition(b,out var bh,.8f,NavMesh.AllAreas)&&NavMesh.CalculatePath(ah.position,bh.position,NavMesh.AllAreas,currentPath)&&currentPath.status==NavMeshPathStatus.PathComplete)continue;
                work.Add(new LinkWork{link=link,a=a,b=b});
            }
            bool Floor(Vector3 at,out Vector3 point)
            {
                point=default;
                foreach(var h in Physics.RaycastAll(at+Vector3.up*4,Vector3.down,8,1,QueryTriggerInteraction.Ignore).OrderBy(h=>Mathf.Abs(h.point.y-at.y)))
                    if(h.collider.name.StartsWith("Terrain_")&&h.normal.y>=.71f&&query.IsPermanentDrySupport(h.point,h.collider)){point=h.point;return true;}
                return false;
            }
            try
            {
                foreach(var s in surfaces)s.RemoveData();
                foreach(var owner in surfaces)
                {
                    owner.AddData();var center=owner.transform.TransformPoint(owner.center);var half=Vector3.Scale(owner.size,owner.transform.lossyScale)*.5f;
                    foreach(var item in work)foreach(bool start in new[]{true,false})
                    {
                        var original=start?item.a:item.b;var list=start?item.starts:item.ends;
                        var offsets=widerSearch?Enumerable.Range(-12,25).Select(i=>i*.5f).ToArray():new[]{0f,-.25f,.25f,-.5f,.5f,-.75f,.75f,-1f,1f,-1.25f,1.25f,-1.5f,1.5f};
                        foreach(float dx in offsets)foreach(float dz in offsets)
                        {
                            var at=original+new Vector3(dx,0,dz);
                            if(Mathf.Abs(at.x-center.x)>half.x||Mathf.Abs(at.z-center.z)>half.z||!Floor(at,out var ground))continue;
                            if(!NavMesh.SamplePosition(ground,out var nav,.3f,NavMesh.AllAreas)||Mathf.Abs(nav.position.x-center.x)>half.x||Mathf.Abs(nav.position.z-center.z)>half.z||!Floor(nav.position,out var actual)||Mathf.Abs(actual.y-nav.position.y)>.2f)continue;
                            if(!list.Any(p=>p.owner==owner&&Vector3.Distance(p.p,nav.position)<.05f))list.Add(new LinkPoint{p=nav.position,owner=owner});
                        }
                    }
                    owner.RemoveData();
                }
            }
            finally{foreach(var s in surfaces){s.RemoveData();s.AddData();}foreach(var link in links)link.UpdateLink();}
            var reasons=new Dictionary<string,int>();
            bool Reject(string reason){reasons[reason]=reasons.TryGetValue(reason,out int n)?n+1:1;return false;}
            bool Clear(Vector3 a,Vector3 b)
            {
                float distance=Vector3.Distance(a,b);if(distance<.15f||distance>3.5f)return Reject("distance");
                var flat=b-a;flat.y=0;if(flat.magnitude<.1f||Mathf.Abs(b.y-a.y)>flat.magnitude*.7f)return Reject("grade");
                int count=Mathf.CeilToInt(distance/.15f);
                for(int i=0;i<=count;i++)
                {
                    var p=Vector3.Lerp(a,b,(float)i/count);if(!Floor(p,out var floor))return Reject("no_floor");if(Mathf.Abs(floor.y-p.y)>.22f)return Reject("floor_line");
                    var obstacle=Physics.OverlapCapsule(floor+Vector3.up*.34f,floor+Vector3.up*1.47f,.27f,1,QueryTriggerInteraction.Ignore).FirstOrDefault(c=>c.attachedRigidbody==null&&!c.transform.IsChildOf(Session.Walker.Body.transform));if(obstacle!=null)return Reject("collision:"+obstacle.name);
                }
                return true;
            }
            foreach(var item in work)
            {
                bool repaired=false;var oldStart=item.link.startPoint;var oldEnd=item.link.endPoint;
                var pairs=(from a in item.starts from b in item.ends where a.owner!=b.owner&&Vector3.Distance(a.p,b.p)<=3.5f orderby Vector3.Distance(a.p,item.a)+Vector3.Distance(b.p,item.b) select new {a,b}).Take(widerSearch?40000:7000);
                foreach(var pair in pairs)
                {
                    if(!Clear(pair.a.p,pair.b.p))continue;
                    item.link.startPoint=item.link.transform.InverseTransformPoint(pair.a.p);item.link.endPoint=item.link.transform.InverseTransformPoint(pair.b.p);item.link.UpdateLink();
                    var path=new NavMeshPath();if(NavMesh.CalculatePath(pair.a.p,pair.b.p,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete)
                    {report.checks.Add(NamePath(item.link.transform)+": "+item.a+" / "+item.b+" -> "+pair.a.p+" / "+pair.b.p);EditorUtility.SetDirty(item.link);repaired=true;break;}
                    item.link.startPoint=oldStart;item.link.endPoint=oldEnd;item.link.UpdateLink();
                }
                if(!repaired)
                {
                    item.link.enabled=false;
                    foreach(var pair in pairs)
                    {
                        var alternate=new NavMeshPath();
                        if(!NavMesh.CalculatePath(pair.a.p,pair.b.p,NavMesh.AllAreas,alternate)||alternate.status!=NavMeshPathStatus.PathComplete)continue;
                        float length=0;bool clear=true;
                        for(int i=1;i<alternate.corners.Length;i++)
                        {
                            var a=alternate.corners[i-1];var b=alternate.corners[i];float d=Vector3.Distance(a,b);length+=d;
                            int pieces=Mathf.Max(1,Mathf.CeilToInt(d/2));
                            for(int k=0;k<pieces;k++)if(!Clear(Vector3.Lerp(a,b,(float)k/pieces),Vector3.Lerp(a,b,(float)(k+1)/pieces)))clear=false;
                        }
                        if(!clear||length>12)continue;
                        report.checks.Add(NamePath(item.link.transform)+": unsafe redundant shortcut disabled; independently owned endpoints remain connected by actual supported "+length.ToString("F2")+"m alternate path");
                        EditorUtility.SetDirty(item.link);repaired=true;break;
                    }
                    if(!repaired){item.link.enabled=true;item.link.UpdateLink();report.failures.Add(NamePath(item.link.transform)+": candidate endpoints "+item.starts.Count+" / "+item.ends.Count+"; actual clearance/owner connection not satisfied");}
                }
            }
            report.checks.Add("Diagnostics: "+string.Join("; ",reasons.Select(r=>r.Key+"="+r.Value)));
            if(report.checks.Count>1){EditorSceneManager.MarkSceneDirty(Session.gameObject.scene);EditorSceneManager.SaveScene(Session.gameObject.scene);}
            if(report.failures.Count>0)report.status="PARTIAL_REPAIR";
            return Write("navigation_link_repairs.json",report);
        }
    }
}
