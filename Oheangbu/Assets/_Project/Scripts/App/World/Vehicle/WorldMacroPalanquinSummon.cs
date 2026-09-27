using System;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World.Vehicle
{
    /// <summary>Relocates the one existing parked car. Does not instantiate, drive or change profiles.</summary>
    [DefaultExecutionOrder(-200), DisallowMultipleComponent]
    public sealed partial class WorldMacroPalanquinSummon : MonoBehaviour
    {
        public WorldMacroPalanquinController Vehicle;
        public WorldMacroPalanquinSeat Seat;
        public WorldMacroCombatWalker Walker;
        public WorldMacroSheetSO WorldSheet;
        [Range(2,20)] public float MaximumSlope=14;
        [Min(0)] public float Clearance=.2f;
        [Min(1)] public float MinimumPlayerDistance=6;
        public string LastResult { get; private set; }
        public string LastRoute { get; private set; }
        // Diagnostics only; natural call feedback remains unchanged.
        public string LastPlacementDiagnostic { get; private set; }
        public int SuccessfulCalls { get; private set; }
        double nextCall;
        readonly RaycastHit[] hits=new RaycastHit[48];
        readonly Collider[] overlaps=new Collider[64];
        struct Candidate { public Vector3 Point,Forward;public string Route; }
        public struct Placement { public Vector3 Position;public Quaternion Rotation;public string Route;public float Slope; }

        public bool TrySummonFromMenu(PauseCoordinator menuOwner,out string message)
        {
            if(!Application.isPlaying || !isActiveAndEnabled) return Fail("플레이 중 오행부 메뉴에서 부를 수 있다.",out message);
            if(menuOwner==null || !menuOwner.IsPaused || menuOwner.Gate==null || !menuOwner.Gate.InputBlocked ||
                menuOwner.Gate.FocusOwnsBlock || menuOwner.Gate.ReleasePending || Time.timeScale>.0001f)
                return Fail("오행부 메뉴가 완전히 열린 뒤 다시 부른다.",out message);
            if(!Ready(out message)) {LastResult=message;return false;}
            if(menuOwner.PalanquinController!=Vehicle && menuOwner.PalanquinSeat!=Seat)
                return Fail("메뉴와 가마 연결을 확인해야 한다.",out message);
            if(Seat.Occupied || Vehicle.DriverPresent || Walker.Seated)
                return Fail("가마에서 내린 뒤 부를 수 있다.",out message);
            if(Vehicle.Speed>.15f || Vehicle.Body.angularVelocity.magnitude>.1f || Mathf.Abs(Vehicle.AppliedMotorTorque)>.1f)
                return Fail("가마가 완전히 멈춘 뒤 부를 수 있다.",out message);
            var vitals=Walker.Motor.GetComponent<PlayerVitals>();
            if(vitals!=null && vitals.Hp01<=0) return Fail("지금은 가마를 부를 수 없다.",out message);
            if(!Walker.Body.enabled || !Walker.Motor.enabled || Walker.Drawing.InDrawMode)
                return Fail("보행 상태에서 오행부를 연다.",out message);
            if(!Walker.Motor.IsLocomotionGrounded || Walker.Motor.IsDodging || Walker.Motor.IsHarvesting)
                return Fail("땅에 발을 붙이고 행동을 마친 뒤 부른다.",out message);
            if(Time.unscaledTimeAsDouble<nextCall) return Fail("가마가 자리를 잡는 중이다.",out message);
            Physics.SyncTransforms();
            if(!TryFindPlacement(Walker.Body.transform.position,Walker.Body.transform.forward,out var placement,out message))
            {LastResult=message;return false;}
            // Everything above is read-only. No failed probe can move or reconfigure the vehicle.
            if(!CommitPlacement(placement))return Fail("산길에서는 가마가 오지 않는다.",out message);
            message=LastResult="앞에 자동차를 불렀다. E로 탑승.";return true;
        }
        bool CommitPlacement(Placement placement)
        {
            if(!CompactMountainAccess.VehicleAllowed(gameObject.scene,Walker.Body.transform.position)||!CompactMountainAccess.VehicleAllowed(gameObject.scene,placement.Position,Vehicle.Hull.bounds.extents.magnitude))return false;
            var body=Vehicle.Body;
            Vehicle.StopDriverInputForUi();
            body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;
            body.position=placement.Position;body.rotation=placement.Rotation;
            Vehicle.transform.SetPositionAndRotation(placement.Position,placement.Rotation);
            if(IsRecalled) { IsRecalled=false; Vehicle.gameObject.SetActive(true); }
            RecallArmed=false; awaySeconds=0;
            Vehicle.ResetMountainTraversalPose();Physics.SyncTransforms();body.WakeUp();
            nextCall=Time.unscaledTimeAsDouble+1.5;SuccessfulCalls++;LastRoute=placement.Route;return true;
        }
        bool Fail(string text,out string message) {message=LastResult=text;return false;}
        bool Ready(out string reason)
        {
            reason="가마 호출 연결을 확인해야 한다.";
            if(Vehicle==null||Seat==null||Walker==null||WorldSheet==null||Vehicle.Body==null||Vehicle.Hull==null||Vehicle.Profile==null||
                Walker.Body==null||Walker.Motor==null||Walker.Drawing==null||Seat.Vehicle!=Vehicle||(!Vehicle.gameObject.activeInHierarchy&&!IsRecalled))return false;
            if(Application.isPlaying&&!Vehicle.IsConfigured)return false;
            if(!Vehicle.ValidateConfiguration(out var issue)){reason="가마 구성: "+issue;return false;}
            if(UnityEngine.Object.FindObjectsByType<WorldMacroPalanquinController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length!=1)
            {reason="호출할 가마가 한 대로 지정되어 있지 않다.";return false;}
            return true;
        }
        // Read-only query is also available to Editor diagnostics. No actor or save mutation.
        public bool TryFindPlacement(Vector3 playerFeet,Vector3 preferredForward,out Placement placement,out string reason)
        {
            LastPlacementDiagnostic=null;
            placement=default;if(!Ready(out reason))return false;
            if(!CompactMountainAccess.VehicleAllowed(gameObject.scene,playerFeet))return Fail("산길에서는 가마가 오지 않는다.",out reason);
            // Both G and the menu use the same nearby, player-relative position.
            // Never fall back to a distant road or the other side of an obstacle.
            var forward=Vector3.ProjectOnPlane(preferredForward,Vector3.up);
            if(forward.sqrMagnitude<.0001f)forward=Vector3.ProjectOnPlane(Walker.Body.transform.forward,Vector3.up);
            if(forward.sqrMagnitude<.0001f)forward=Vector3.forward;
            forward.Normalize();
            float rearExtent=Mathf.Max(0,Vehicle.Hull.size.z*.5f-Vehicle.Hull.center.z);
            float distance=Mathf.Max(MinimumPlayerDistance,rearExtent+Walker.Body.radius+Clearance+1f);
            var candidate=new Candidate{Point=playerFeet+forward*distance,Forward=forward,Route="player_front"};
            if(TryPose(candidate,playerFeet,out placement)){reason=null;return true;}
            reason="앞에 자동차를 놓을 공간이 부족하다.";return false;
        }
        bool TryPose(Candidate candidate,Vector3 playerFeet,out Placement result)
        {
            result=default;Vector3 point=candidate.Point;Quaternion rotation=Quaternion.LookRotation(candidate.Forward,Vector3.up);
            Vector3 normal=Vector3.zero;float height=0,min=1e9f,max=-1e9f;
            for(int i=0;i<4;i++)
            {
                var wheel=Vehicle.Wheels[i].Collider;Vector3 anchor=Vehicle.transform.InverseTransformPoint(wheel.transform.position);
                if(!Ground(point+rotation*new Vector3(anchor.x,0,anchor.z),out var hit))return RejectPlacement("wheel ground "+i);
                normal+=hit.normal;
            }
            normal.Normalize();float slope=Vector3.Angle(normal,Vector3.up);if(slope>MaximumSlope)return RejectPlacement("slope "+slope);
            rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(candidate.Forward,normal).normalized,normal);
            for(int i=0;i<4;i++)
            {
                var wheel=Vehicle.Wheels[i].Collider;Vector3 anchor=Vehicle.transform.InverseTransformPoint(wheel.transform.position);
                Vector3 rotated=rotation*anchor;
                if(!Ground(new Vector3(point.x+rotated.x,point.y,point.z+rotated.z),out var hit))return RejectPlacement("tilted wheel ground "+i);
                float suspension=wheel.suspensionDistance*(1-wheel.suspensionSpring.targetPosition);
                float rootY=hit.point.y-rotated.y+(wheel.radius+suspension)*normal.y+.025f;
                min=Mathf.Min(min,rootY);max=Mathf.Max(max,rootY);height+=rootY;
            }
            if(max-min>Mathf.Min(.16f,Vehicle.Profile.SuspensionDistance*.65f))return RejectPlacement("wheel support spread "+(max-min));
            point.y=height*.25f;
            // Nine support points cover the hull between wheels, not just four isolated ledges.
            Vector3 size=Vehicle.Hull.size;
            for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++)
                if(!Ground(point+rotation*new Vector3(x*size.x*.48f,0,z*size.z*.48f),out _))return RejectPlacement("hull support "+x+","+z);
            var half=size*.5f+Vector3.one*Clearance;
            if(!EmptyBox(point+rotation*Vehicle.Hull.center,half,rotation))return RejectPlacement("hull overlap");
            // The restored car has a separate protruding engine collider. Probe every
            // rigid box in the proposed pose before moving the vehicle, including when
            // it is recalled (inactive). Visual meshes and wheel colliders are excluded.
            foreach(var part in Vehicle.GetComponentsInChildren<BoxCollider>(true))
            {
                if(part==Vehicle.Hull||!part.enabled||part.isTrigger||part.attachedRigidbody!=Vehicle.Body)continue;
                var localCenter=Vehicle.transform.InverseTransformPoint(part.transform.TransformPoint(part.center));
                var localRotation=Quaternion.Inverse(Vehicle.transform.rotation)*part.transform.rotation;
                var partHalf=Vector3.Scale(part.size,part.transform.lossyScale)*.5f+Vector3.one*Clearance;
                if(!EmptyBox(point+rotation*localCenter,partHalf,rotation*localRotation))return RejectPlacement("part overlap "+part.name);
            }
            foreach(var binding in Vehicle.Wheels)
            {
                var wheel=binding.Collider;var anchor=Vehicle.transform.InverseTransformPoint(wheel.transform.position);
                Vector3 center=point+rotation*anchor-normal*wheel.suspensionDistance*(1-wheel.suspensionSpring.targetPosition);
                int count=Physics.OverlapSphereNonAlloc(center,wheel.radius*.92f,overlaps,Vehicle.Profile.EnvironmentMask,QueryTriggerInteraction.Ignore);
                if(count==overlaps.Length)return RejectPlacement("wheel query overflow");
                for(int i=0;i<count;i++)if(!Ignore(overlaps[i]))return RejectPlacement("wheel overlap "+overlaps[i].name);
            }
            // A 6 m diagonal at kilometre-scale coordinates can round to 5.999987.
            // Five millimetres absorb float error, not physical clearance failures.
            if(Vector3.Distance(point,playerFeet)+.005f<MinimumPlayerDistance)return RejectPlacement("player distance "+Vector3.Distance(point,playerFeet).ToString("F6"));
            if(!Door(point,rotation,1,playerFeet)&&!Door(point,rotation,-1,playerFeet))return RejectPlacement("both door approaches blocked");
            if(!CompactMountainAccess.VehicleAllowed(gameObject.scene,point,half.magnitude))return RejectPlacement("mountain foot-only area");
            LastPlacementDiagnostic=null;
            result=new Placement{Position=point,Rotation=rotation,Route=candidate.Route,Slope=slope};return true;
        }
        bool RejectPlacement(string detail){LastPlacementDiagnostic=detail+(string.IsNullOrEmpty(LastPlacementDiagnostic)?"":" / "+LastPlacementDiagnostic);return false;}
        bool Ground(Vector3 expected,out RaycastHit chosen)
        {
            chosen=default;
            int count=Physics.RaycastNonAlloc(expected+Vector3.up*5,Vector3.down,hits,13,Vehicle.Profile.EnvironmentMask,QueryTriggerInteraction.Ignore);
            if(count==hits.Length)return false;float nearest=float.PositiveInfinity;
            for(int i=0;i<count;i++)
            {
                var h=hits[i];if(Ignore(h.collider)||h.collider is CharacterController||h.rigidbody!=null||
                    Vector3.Angle(h.normal,Vector3.up)>MaximumSlope||Mathf.Abs(h.point.y-expected.y)>6)continue;
                if(h.distance<nearest){chosen=h;nearest=h.distance;}
            }
            return !float.IsPositiveInfinity(nearest)&&(Session?.Traversal!=null?Session.Traversal.IsPermanentDrySupport(chosen.point,chosen.collider):Dry(chosen.point));
        }
        bool Dry(Vector3 point)
        {
            if(Session?.Traversal!=null)return Session.Traversal.IsDry(point);
            foreach(var river in WorldSheet.Rivers)
            {
                if(river==null||river.Points==null)continue;
                for(int i=1;i<river.Points.Length;i++)
                {
                    Vector3 a=river.Points[i-1],b=river.Points[i],v=b-a;v.y=0;if(v.sqrMagnitude<.01f)continue;
                    float t=Mathf.Clamp01(Vector3.Dot(new Vector3(point.x-a.x,0,point.z-a.z),v)/v.sqrMagnitude);
                    Vector3 p=Vector3.Lerp(a,b,t);
                    if(Vector2.Distance(new Vector2(point.x,point.z),new Vector2(p.x,p.z))<river.Width*.5f && point.y<p.y+.2f)return false;
                }
            }
            return true;
        }
        bool EmptyBox(Vector3 center,Vector3 half,Quaternion rotation)
        {
            int count=Physics.OverlapBoxNonAlloc(center,half,overlaps,rotation,Vehicle.Profile.EnvironmentMask,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length)return false;
            for(int i=0;i<count;i++)if(!Ignore(overlaps[i]))return RejectPlacement(overlaps[i].name);return true;
        }
        bool Door(Vector3 point,Quaternion rotation,int side,Vector3 playerFeet)
        {
            if(!Ground(point+rotation*Vector3.right*Vehicle.Profile.ExitSideDistance*side,out var ground))return false;
            float height=Mathf.Max(1.75f,Walker.Body.height),radius=Mathf.Max(.32f,Walker.Body.radius);
            Vector3 bottom=ground.point+Vector3.up*(radius+.12f),top=bottom+Vector3.up*(height-2*radius);
            int count=Physics.OverlapCapsuleNonAlloc(bottom,top,radius,overlaps,Vehicle.Profile.EnvironmentMask,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length)return false;
            for(int i=0;i<count;i++)if(!Ignore(overlaps[i]))return false;
            // Do not offer a destination behind a cave wall, gate or mountain flank.
            Vector3 eye=playerFeet+Vector3.up*1.35f,delta=ground.point+Vector3.up*1.35f-eye;
            count=Physics.RaycastNonAlloc(eye,delta.normalized,hits,delta.magnitude,Vehicle.Profile.EnvironmentMask,QueryTriggerInteraction.Ignore);
            if(count==hits.Length)return false;
            for(int i=0;i<count;i++)if(!Ignore(hits[i].collider)&&hits[i].collider!=Walker.Body)return false;
            return true;
        }
        bool Ignore(Collider collider)=>collider==null||!collider.enabled||collider.transform.IsChildOf(Vehicle.transform);
    }
}
