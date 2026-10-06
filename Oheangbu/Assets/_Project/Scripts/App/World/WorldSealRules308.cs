using System;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>
    /// #308 1b′ save relocation (SPEC-WORLD-ENCLOSURE-305 §1b′ "세이브 이전", D308-3) [TEST]. Pure static rules, no mutable static
    /// state: a save without the south-gate fact whose position / checkpoint / drop / vehicle lies beyond the seal is moved to the
    /// nearest resolvable EA rest. Nothing is added to ledger.completed (no reward or story step is skipped). Physics queries are
    /// passed in by the caller (the session) so the rule itself stays testable from the editor.
    /// </summary>
    public static class WorldSealRules308
    {
        public delegate bool SafeFeetQuery(Vector3 candidate,out Vector3 feet);

        public struct Plan
        {
            public bool Position,Checkpoint,Drop,Vehicle;
            public string RestId;
            public Vector3 RestFeet,VehicleFeet,Origin;
            public float RestYaw;
            public bool Any=>Position||Checkpoint||Drop||Vehicle;
            public string Describe()=>"rest "+RestId+" "+V(RestFeet)+" from "+V(Origin)+" ["+(Position?"position ":"")+(Checkpoint?"checkpoint ":"")+(Drop?"drop ":"")+(Vehicle?"vehicle":"")+"]";
            static string V(Vector3 v)=>"("+v.x.ToString("F1")+", "+v.y.ToString("F1")+", "+v.z.ToString("F1")+")";
        }

        /// <summary>The seal applies only with a configured profile and while the required fact is not on record.</summary>
        public static bool Armed(WorldSealProfileSO profile,WorldMacroProgress progress)=>
            profile!=null&&profile.IsConfigured&&progress?.ledger?.completed!=null&&!progress.ledger.completed.Contains(profile.RequiredFact);

        /// <summary>Even-odd test of the XZ point against every ring (a ring inside a ring is a hole).</summary>
        public static bool InsideRings(WorldSealProfileSO profile,Vector3 p)
        {
            if(profile?.BeyondRings==null||!Finite(p))return false;
            bool inside=false;
            foreach(var ring in profile.BeyondRings)
            {
                var pts=ring?.Points;if(pts==null||pts.Length<3)continue;
                for(int i=0,j=pts.Length-1;i<pts.Length;j=i++)
                {
                    Vector2 a=pts[i],b=pts[j];
                    if((a.y>p.z)!=(b.y>p.z)&&p.x<(b.x-a.x)*(p.z-a.y)/(b.y-a.y)+a.x)inside=!inside;
                }
            }
            return inside;
        }

        public static bool InVolume(WorldSealProfileSO.Volume v,Vector3 p)
        {
            if(v==null)return false;
            var local=Quaternion.Euler(0,-v.Yaw,0)*(p-v.Center);var half=v.Size*.5f;
            return Mathf.Abs(local.x)<=Mathf.Abs(half.x)&&Mathf.Abs(local.y)<=Mathf.Abs(half.y)&&Mathf.Abs(local.z)<=Mathf.Abs(half.z);
        }

        /// <summary>Inside an EA mine box and more than UndergroundDepth under the surface (surfaceY NaN = unknown = not underground).</summary>
        public static bool InUnderground(WorldSealProfileSO profile,Vector3 p,float surfaceY)
        {
            if(profile?.EaUndergroundVolumes==null||!float.IsFinite(surfaceY)||surfaceY-p.y<=profile.UndergroundDepth)return false;
            foreach(var v in profile.EaUndergroundVolumes)if(InVolume(v,p))return true;
            return false;
        }

        /// <summary>Beyond the seal: inside the rings and not an EA mine under them. Interiors of buildings beyond are beyond.</summary>
        public static bool IsBeyond(WorldSealProfileSO profile,Vector3 p,Func<Vector3,float> surfaceY)=>
            InsideRings(profile,p)&&!InUnderground(profile,p,surfaceY!=null?surfaceY(p):float.NaN);

        /// <summary>
        /// Detached plan (no mutation). False with a reason when nothing applies: no profile / fact recorded / escort under way /
        /// nothing beyond / no resolvable rest. The rest is the EaRestIds entry nearest (XZ) to the saved position whose
        /// checkpoint resolves and has safe feet; FallbackRestIds in order otherwise.
        /// </summary>
        public static bool TryPlan(WorldSealProfileSO profile,WorldMacroPlaytestSO content,WorldMacroProgress progress,
            Func<Vector3,float> surfaceY,SafeFeetQuery safeFeet,out Plan plan,out string reason)
        {
            plan=default;reason=null;
            if(!Armed(profile,progress)){reason=profile==null?"no seal profile":!profile.IsConfigured?"seal profile not configured":"seal fact recorded";return false;}
            if(content==null||safeFeet==null){reason="no content";return false;}
            // an escort resume belongs to the escort's own reload rules (its route is EA; never moved here)
            var escort=progress.escort;
            if(escort!=null&&escort.Stage>=DemoEscortStage.Escorting&&escort.Stage<DemoEscortStage.Delivered){reason="escort under way";return false;}
            var l=progress.ledger;
            plan.Origin=l.hasPosition&&Finite(l.position)?l.position:l.checkpointPosition;
            plan.Position=l.hasPosition&&IsBeyond(profile,l.position,surfaceY);
            bool checkpointKnown=WorldMacroCheckpointRules.TryResolve(content,progress,l.checkpoint,out var current);
            plan.Checkpoint=checkpointKnown&&IsBeyond(profile,current.Feet,surfaceY)||Finite(l.checkpointPosition)&&IsBeyond(profile,l.checkpointPosition,surfaceY);
            plan.Drop=profile.MoveDrops&&l.dropCurrency>0&&IsBeyond(profile,l.dropPosition,surfaceY);
            plan.Vehicle=profile.MoveVehicle&&l.hasVehicle&&IsBeyond(profile,l.vehiclePosition,surfaceY);
            if(!plan.Any){reason="nothing beyond the seal";return false;}
            if(!TrySelectRest(profile,content,progress,plan.Origin,surfaceY,safeFeet,out plan.RestId,out plan.RestFeet,out plan.RestYaw))
            {reason="no resolvable EA rest";plan=default;return false;}
            if(plan.Vehicle)
            {
                var side=Quaternion.Euler(0,plan.RestYaw,0)*Vector3.right;
                if(!safeFeet(plan.RestFeet+side*4f,out plan.VehicleFeet)&&!safeFeet(plan.RestFeet-side*4f,out plan.VehicleFeet))plan.VehicleFeet=plan.RestFeet;
            }
            return true;
        }

        public static bool TrySelectRest(WorldSealProfileSO profile,WorldMacroPlaytestSO content,WorldMacroProgress progress,Vector3 origin,
            Func<Vector3,float> surfaceY,SafeFeetQuery safeFeet,out string id,out Vector3 feet,out float yaw)
        {
            id=null;feet=default;yaw=0;float best=float.MaxValue;
            foreach(var candidate in profile.EaRestIds??Array.Empty<string>())
            {
                if(string.IsNullOrWhiteSpace(candidate)||!WorldMacroCheckpointRules.TryResolve(content,progress,candidate,out var spec))continue;
                if(IsBeyond(profile,spec.Feet,surfaceY)||!safeFeet(spec.Feet,out var safe))continue;
                float d=Vector2.Distance(new Vector2(origin.x,origin.z),new Vector2(spec.Feet.x,spec.Feet.z));
                if(d<best){best=d;id=spec.Id;feet=safe;yaw=spec.Yaw;}
            }
            if(id!=null)return true;
            foreach(var candidate in profile.FallbackRestIds??Array.Empty<string>())
            {
                if(string.IsNullOrWhiteSpace(candidate)||!WorldMacroCheckpointRules.TryResolve(content,progress,candidate,out var spec)||!safeFeet(spec.Feet,out var safe))continue;
                id=spec.Id;feet=safe;yaw=spec.Yaw;return true;
            }
            return false;
        }

        /// <summary>Writes the plan into the ledger. ledger.completed is never touched.</summary>
        public static void Apply(in Plan plan,PrologueProgress ledger)
        {
            if(ledger==null||!plan.Any||string.IsNullOrEmpty(plan.RestId))return;
            if(plan.Position){ledger.position=plan.RestFeet;ledger.yaw=plan.RestYaw;ledger.hasPosition=true;}
            if(plan.Checkpoint){ledger.checkpoint=plan.RestId;ledger.checkpointPosition=plan.RestFeet;}
            if(plan.Drop)ledger.dropPosition=plan.RestFeet;
            if(plan.Vehicle)ledger.vehiclePosition=plan.VehicleFeet;
        }

        static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
    }
}
