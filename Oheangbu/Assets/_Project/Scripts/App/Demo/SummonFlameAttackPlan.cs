using System;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    // The mouth position and cone are fixed at windup start. Moving the player, actor or enemy
    // does not steer an accepted flame; current target positions are tested against this cone at release.
    public sealed class SummonFlameAttackPlan
    {
        public Vector3 Origin {get;}
        public Vector3 Direction {get;}
        public float Range {get;}
        public float HalfAngleDegrees {get;}
        public float VerticalTolerance {get;}
        public float StartedAt {get;}
        public float ReleaseAt {get;}
        public float SprayEndAt {get;}
        public float EndAt {get;}
        public float SampleTime {get;private set;}
        public bool IsCancelled {get;private set;}
        public bool IsReleased=>!IsCancelled&&SampleTime>=ReleaseAt;
        public bool IsSpraying=>IsReleased&&SampleTime<SprayEndAt;
        public bool IsFinished=>IsCancelled||SampleTime>=EndAt;
        readonly float minimumDot;

        public SummonFlameAttackPlan(Vector3 origin,Vector3 direction,float range,float halfAngleDegrees,float verticalTolerance,
            float startedAt,float windup,float spray,float recovery)
        {
            if(!Finite(origin)||!Finite(direction)||!Valid(range)||range<=0||!Valid(halfAngleDegrees)||halfAngleDegrees<=0||halfAngleDegrees>=90||
                !Valid(verticalTolerance)||verticalTolerance<=0||!Valid(startedAt)||startedAt<0||!Valid(windup)||windup<0||
                !Valid(spray)||spray<=0||!Valid(recovery)||recovery<0)throw new ArgumentException("Flame geometry and timing must be finite and valid.");
            direction=Vector3.ProjectOnPlane(direction,Vector3.up);
            if(direction.sqrMagnitude<.0001f)throw new ArgumentException("Flame direction must have a horizontal component.");
            Origin=origin;Direction=direction.normalized;Range=range;HalfAngleDegrees=halfAngleDegrees;VerticalTolerance=verticalTolerance;
            StartedAt=SampleTime=startedAt;ReleaseAt=startedAt+windup;SprayEndAt=ReleaseAt+spray;EndAt=SprayEndAt+recovery;
            if(!Valid(EndAt))throw new ArgumentException("Flame timeline overflow.");
            minimumDot=Mathf.Cos(halfAngleDegrees*Mathf.Deg2Rad);
        }
        public bool Contains(Vector3 worldPoint)
        {
            if(!Finite(worldPoint))return false;
            Vector3 delta=worldPoint-Origin;
            if(Mathf.Abs(delta.y)>VerticalTolerance)return false;
            delta.y=0;float distance=delta.magnitude;
            return distance<=Range&&distance>.0001f&&Vector3.Dot(delta/distance,Direction)>=minimumDot;
        }
        public void AdvanceTo(float actorClockTime)
        {
            if(!Valid(actorClockTime)||actorClockTime<SampleTime)throw new ArgumentOutOfRangeException(nameof(actorClockTime),"Flame time cannot rewind.");
            if(!IsCancelled)SampleTime=actorClockTime;
        }
        public void Cancel(){IsCancelled=true;}
        static bool Valid(float value)=>float.IsFinite(value);
        static bool Finite(Vector3 value)=>Valid(value.x)&&Valid(value.y)&&Valid(value.z);
    }
}
