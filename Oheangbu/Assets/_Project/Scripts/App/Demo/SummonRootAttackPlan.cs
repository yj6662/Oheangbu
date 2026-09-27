using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    // Fixed world-space cast plan. Presentation and damage read the same immutable route and clock.
    public sealed class SummonRootAttackPlan
    {
        readonly ReadOnlyCollection<Vector3> positions;
        readonly ReadOnlyCollection<float> arrivals, distances;
        public IReadOnlyList<Vector3> GroundPositions=>positions;
        public IReadOnlyList<float> ArrivalTimes=>arrivals;
        public IReadOnlyList<float> GroundDistances=>distances;
        public Vector3 Origin=>positions[0];
        public Vector3 Direction {get;}
        public float Width {get;}
        public float Length=>distances[distances.Count-1];
        public float CastStartedAt {get;}
        public float ReleaseAt {get;}
        public float TravelEndAt=>arrivals[arrivals.Count-1];
        public float EndAt {get;}
        public float CleanupAfter {get;}
        public float TravelSpeed {get;}
        public float SampleTime {get;private set;}
        public bool IsCancelled {get;private set;}
        public bool IsReleased=>!IsCancelled&&SampleTime>=ReleaseAt;
        public bool IsFinished=>IsCancelled||SampleTime>=EndAt;
        public float FrontDistance=>IsReleased?Mathf.Clamp((SampleTime-ReleaseAt)*TravelSpeed,0,Length):0;
        public float Progress01=>Length>0?FrontDistance/Length:0;

        public SummonRootAttackPlan(IReadOnlyList<Vector3> points,float width,float castStartedAt,float windup,float speed,float cleanupAfter)
        {
            if(points==null||points.Count<2||points.Count>257)throw new ArgumentException("A root route needs 2..257 sampled points.",nameof(points));
            if(!Finite(width)||width<=0||!Finite(castStartedAt)||castStartedAt<0||!Finite(windup)||windup<0||
                !Finite(speed)||speed<=0||!Finite(cleanupAfter)||cleanupAfter<0)throw new ArgumentException("Root timings and width must be finite and valid.");
            var copy=new Vector3[points.Count];var length=new float[points.Count];var times=new float[points.Count];
            CastStartedAt=SampleTime=castStartedAt;ReleaseAt=castStartedAt+windup;Width=width;TravelSpeed=speed;CleanupAfter=cleanupAfter;
            Vector3 direction=Vector3.ProjectOnPlane(points[points.Count-1]-points[0],Vector3.up);
            if(direction.sqrMagnitude<.0001f)throw new ArgumentException("Root route must advance on the ground.");
            Direction=direction.normalized;
            for(int i=0;i<points.Count;i++)
            {
                Vector3 point=points[i];
                if(!Finite(point.x)||!Finite(point.y)||!Finite(point.z))throw new ArgumentException("Root ground coordinates must be finite.");
                copy[i]=point;length[i]=Vector3.Dot(point-points[0],Direction);
                if(i>0&&(length[i]<=length[i-1]||Vector3.ProjectOnPlane(point-points[0]-Direction*length[i],Vector3.up).sqrMagnitude>.0001f))
                    throw new ArgumentException("Root route must retain its original straight direction and forward order.");
                times[i]=ReleaseAt+length[i]/speed;
            }
            positions=Array.AsReadOnly(copy);distances=Array.AsReadOnly(length);arrivals=Array.AsReadOnly(times);
            EndAt=TravelEndAt+cleanupAfter;
        }
        public void AdvanceTo(float actorClockTime)
        {
            if(!Finite(actorClockTime)||actorClockTime<SampleTime)throw new ArgumentOutOfRangeException(nameof(actorClockTime),"Root time cannot rewind.");
            if(!IsCancelled)SampleTime=actorClockTime;
        }
        public void Cancel(){IsCancelled=true;}
        public float Longitudinal(Vector3 point)=>Vector3.Dot(Vector3.ProjectOnPlane(point-Origin,Vector3.up),Direction);
        public bool ContainsWidth(Vector3 point)
        {
            Vector3 horizontal=Vector3.ProjectOnPlane(point-Origin,Vector3.up);
            float along=Vector3.Dot(horizontal,Direction);
            return along>=0&&along<=Length&&Mathf.Abs(Vector3.Dot(horizontal,Vector3.Cross(Vector3.up,Direction)))<=Width*.5f;
        }
        public Vector3 GroundAt(float distance)
        {
            distance=Mathf.Clamp(distance,0,Length);
            for(int i=1;i<distances.Count;i++)if(distance<=distances[i])
                return Vector3.Lerp(positions[i-1],positions[i],(distance-distances[i-1])/(distances[i]-distances[i-1]));
            return positions[positions.Count-1];
        }
        static bool Finite(float value)=>float.IsFinite(value);
    }
}
