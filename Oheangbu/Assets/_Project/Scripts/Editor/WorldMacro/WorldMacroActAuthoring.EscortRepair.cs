using System;
using System.Linq;
using Oheangbu.App.Demo;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroActAuthoring
    {
        static string RepairEscortStaging()
        {
            var session=Session;var stop=Components<DemoEscortSceneRoute>().Single().Stops.Single(s=>s.Id=="escort_start");
            var cargo=session.DemoEscortCargo;var report=new Report{status="AUTHORING_REPAIR_REQUIRES_PLAY",scope="Relocate cargo and staging to the open road side of existing raised relay porch. Existing building colliders and NPC contract position preserved."};
            bool Ground(Vector3 p,out Vector3 ground)
            {
                ground=default;
                foreach(var h in Physics.RaycastAll(p+Vector3.up*2,Vector3.down,5,1,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
                {
                    if(!h.collider.name.StartsWith("Terrain_")||!session.Traversal.IsPermanentDrySupport(h.point,h.collider)||h.normal.y<.85f)continue;
                    var at=h.point+Vector3.up*.03f;
                    if(Physics.OverlapCapsule(at+Vector3.up*.32f,at+Vector3.up*1.47f,.28f,~0,QueryTriggerInteraction.Ignore).Any(c=>c.attachedRigidbody==null&&!c.transform.IsChildOf(cargo)&&!c.transform.IsChildOf(session.DemoEscortCompanion)&&!c.transform.IsChildOf(session.Walker.Body.transform)))continue;
                    if(!NavMesh.SamplePosition(at,out var nav,.3f,NavMesh.AllAreas)||Mathf.Abs(nav.position.y-at.y)>.2f)continue;
                    ground=at;return true;
                }
                return false;
            }
            if(!Ground(new Vector3(823.75f,133.5f,147.7f),out var pickup)||!Ground(new Vector3(799.2334f,133.7f,153.6374f),out var wait)||!Ground(new Vector3(799.5f,133.7f,151.8f),out var cargoWait))throw new InvalidOperationException("Road-side cargo/staging lacks support or clearance");
            if(!Ground(new Vector3(799.2334f,133.7f,147.7f),out var approach))throw new InvalidOperationException("Approach lacks support");
            int samples=0;
            foreach(var segment in new[]{new[]{pickup,approach},new[]{approach,wait}})
            {
                int count=Mathf.CeilToInt(Vector3.Distance(segment[0],segment[1])/.3f);samples+=count;
                for(int i=0;i<=count;i++)if(!Ground(Vector3.Lerp(segment[0],segment[1],(float)i/count),out _))throw new InvalidOperationException("Actual staging path obstructed at "+i+"/"+count);
            }
            report.checks.Add("Cargo "+cargo.position+" -> "+pickup);report.checks.Add("Wangso staging "+stop.CompanionWait.position+" -> "+wait);report.checks.Add("Ground/clearance samples "+(samples+1));
            var marker=stop.StagingApproach;
            if(marker==null){marker=new GameObject("StagingApproach_AlongRoadside").transform;marker.SetParent(stop.CompanionWait.parent,true);stop.StagingApproach=marker;}
            marker.position=approach;EditorUtility.SetDirty(marker);EditorUtility.SetDirty(Components<DemoEscortSceneRoute>().Single());
            cargo.position=pickup;stop.CompanionWait.position=wait;stop.CargoWait.position=cargoWait;
            EditorUtility.SetDirty(cargo);EditorUtility.SetDirty(stop.CompanionWait);EditorUtility.SetDirty(stop.CargoWait);
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);EditorSceneManager.SaveScene(session.gameObject.scene);
            return Write("escort_staging_repair.json",report);
        }
    }
}
