using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App.World;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroActAuthoring
    {
        [Serializable] sealed class WaterProbe {public Vector3 surface,bed;public float depth,slope;public string support,status;}
        [Serializable] sealed class TerrainReport {public string status="PHYSICS_SAMPLES_ONLY";public List<WaterProbe> water=new List<WaterProbe>();public List<string> safe=new List<string>();public int deep,shallow,noBed,centerCovered,offsetOneCovered,offsetFourCovered;}
        static string RepairCheckpoints()
        {
            var s=Session;var report=new Report{status="PASS",scope="Local safe dry checkpoint support correction, no terrain regeneration"};
            foreach(var cp in s.Content.Checkpoints)
            {
                if(s.TrySafeFeet(cp.Feet,out _))continue;
                bool repaired=false;Vector3 old=cp.Feet;
                for(float radius=.5f;radius<=5&&!repaired;radius+=.5f)
                for(int i=0;i<16&&!repaired;i++)
                {
                    float a=i*Mathf.PI/8;var candidate=old+new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius);
                    if(!s.TrySafeFeet(candidate,out var safe))continue;
                    cp.Feet=safe;repaired=true;report.checks.Add(cp.Id+": "+old+" → "+safe);
                }
                if(!repaired)report.failures.Add(cp.Id);
            }
            if(report.failures.Count>0)report.status="FAIL";
            UnityEditor.EditorUtility.SetDirty(s.Content);UnityEditor.AssetDatabase.SaveAssets();return Write("checkpoint_repairs.json",report);
        }
        static string TerrainAudit()
        {
            var s=Session;var query=s.Traversal;if(query==null)throw new InvalidOperationException("Prepare first");
            var report=new TerrainReport();Physics.SyncTransforms();
            foreach(var cp in s.Content.Checkpoints)report.safe.Add(cp.Id+": "+s.TrySafeFeet(cp.Feet,out _));
            report.safe.Add("start: "+s.TrySafeFeet(s.Content.StartFeet,out _));
            int stride=Math.Max(1,query.Water.Length/900);
            for(int i=0;i<query.Water.Length;i+=stride)
            {
                var t=query.Water[i];var p=(t.A+t.B+t.C)/3;
                if(query.TryWaterHeight(p,out _))report.centerCovered++;
                if(query.TryWaterHeight(p+Vector3.right,out _))report.offsetOneCovered++;
                if(query.TryWaterHeight(p+Vector3.right*4,out _))report.offsetFourCovered++;
                var hits=Physics.RaycastAll(p+Vector3.up*.02f,Vector3.down,3000,1,QueryTriggerInteraction.Ignore)
                    .Where(h=>h.collider.attachedRigidbody==null&&h.collider.gameObject.scene==SceneManager.GetActiveScene()&&!h.collider.transform.IsChildOf(s.Walker.Body.transform))
                    .OrderBy(h=>h.distance).ToArray();
                if(hits.Length==0){report.noBed++;report.water.Add(new WaterProbe{surface=p,status="NO_BED"});continue;}
                var hit=hits[0];float depth=p.y-hit.point.y;bool deep=depth>query.Rules.MaximumWadingDepth;
                if(deep)report.deep++;else report.shallow++;
                report.water.Add(new WaterProbe{surface=p,bed=hit.point,depth=depth,slope=Vector3.Angle(hit.normal,Vector3.up),support=hit.collider.name,status=deep?"DEEP":"SHALLOW"});
            }
            return Write("terrain_samples.json",report);
        }
    }
}
