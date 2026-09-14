using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Oheangbu.App.World
{
    /// <summary>Macro inspection: a real capsule for user walking, plus free/route cameras. No combat or progression.</summary>
    public sealed class WorldMacroReviewController : MonoBehaviour
    {
        public WorldMacroSheetSO Sheet;
        public float EyeHeight = 1.8f;
        public float FlySpeed = 80f;
        public CharacterController WalkBody;
        public Renderer WalkVisual;
        public float Gravity = 22f;
        public int RouteIndex;
        public int Mode;
        [Tooltip("Macro TEST shortcuts 4-8: palace, fortress, cave, temple, palanquin.")]
        public Transform[] InspectionStops;
        [Header("Macro player camera only; shared PlayerRig is unchanged")]
        public bool PlayerShoulderView;
        public Vector3 ShoulderOffset = new Vector3(.55f,1.5f,-2.7f);
        [Min(.01f)] public float ShoulderResponse = .12f;
        [Min(.01f)] public float ShoulderCollisionRadius = .2f;
        public LayerMask CameraEnvironmentMask = Physics.DefaultRaycastLayers;
        float yaw, pitch, distance, verticalSpeed;
        int appliedMode=-1;
        Vector3 cameraVelocity, resumeFeet;
        float resumeYaw;
        bool resumePending;
        readonly RaycastHit[] cameraHits = new RaycastHit[32];
        void OnEnable()
        {
            if(resumePending){ResumeWalkAt(resumeFeet,resumeYaw);return;}
            if(Mode==1&&PlayerShoulderView&&WalkBody!=null){ResumeWalkAt(WalkBody.transform.position,WalkBody.transform.eulerAngles.y);return;}
            yaw=transform.eulerAngles.y;pitch=transform.eulerAngles.x;if(pitch>180)pitch-=360;appliedMode=-1;cameraVelocity=Vector3.zero;
        }
        void OnDisable() { if(WalkBody!=null)WalkBody.enabled=false;if(WalkVisual!=null)WalkVisual.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On; }
        void Update()
        {
            if(Sheet==null || Keyboard.current==null)return;
            var k=Keyboard.current;
            if(k.digit4Key.wasPressedThisFrame)VisitStop(0);
            if(k.digit5Key.wasPressedThisFrame)VisitStop(1);
            if(k.digit6Key.wasPressedThisFrame)VisitStop(2);
            if(k.digit7Key.wasPressedThisFrame)VisitStop(3);
            if(k.digit8Key.wasPressedThisFrame)VisitStop(4);
            if(k.digit1Key.wasPressedThisFrame)Mode=0;
            if(k.digit2Key.wasPressedThisFrame)Mode=1;
            if(k.digit3Key.wasPressedThisFrame){Mode=2;distance=0;}
            if(appliedMode!=Mode)
            {
                if(WalkBody!=null)
                {
                    WalkBody.enabled=false;
                    if(Mode==1){var feet=transform.position;feet.y=Ground(feet)+.04f;WalkBody.transform.position=feet;WalkBody.enabled=true;verticalSpeed=0;}
                }
                ApplyWalkVisibility();cameraVelocity=Vector3.zero;
                appliedMode=Mode;
            }
            if(k.rightBracketKey.wasPressedThisFrame){RouteIndex=(RouteIndex+1)%Mathf.Max(1,Sheet.Routes.Length);distance=0;}
            if(k.leftBracketKey.wasPressedThisFrame){RouteIndex=(RouteIndex+Sheet.Routes.Length-1)%Mathf.Max(1,Sheet.Routes.Length);distance=0;}
            if(Mouse.current!=null && Mouse.current.rightButton.isPressed){var d=Mouse.current.delta.ReadValue();yaw+=d.x*.12f;pitch=Mathf.Clamp(pitch-d.y*.12f,-85,85);transform.rotation=Quaternion.Euler(pitch,yaw,0);}
            if(Mode==2 && Sheet.Routes.Length>0){
                var route=Sheet.Routes[Mathf.Clamp(RouteIndex,0,Sheet.Routes.Length-1)];
                distance+=Time.deltaTime*(route.Carriage?Sheet.CarriageSpeed:Sheet.WalkSpeed);
                var p=Along(route.Points,distance,out var next);p.y=Ground(p)+EyeHeight;
                transform.position=p;
                if(Mouse.current==null||!Mouse.current.rightButton.isPressed){var d=next-p;d.y=0;if(d.sqrMagnitude>.01f)transform.rotation=Quaternion.LookRotation(d);}
                return;
            }
            Vector3 input=new Vector3((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.eKey.isPressed?1:0)-(k.qKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));
            Vector3 move=transform.TransformDirection(Vector3.ClampMagnitude(input,1));
            if(Mode==1 && WalkBody!=null)
            {
                var horizontal=Quaternion.Euler(0,yaw,0)*Vector3.ClampMagnitude(new Vector3(input.x,0,input.z),1)*Sheet.WalkSpeed;
                if(WalkBody.isGrounded && verticalSpeed<0)verticalSpeed=-2;
                verticalSpeed=Mathf.Max(-50,verticalSpeed-Gravity*Time.deltaTime);
                WalkBody.transform.rotation=Quaternion.Euler(0,yaw,0);
                WalkBody.Move((horizontal+Vector3.up*verticalSpeed)*Time.deltaTime);
                ApplyWalkCamera(false);
                return;
            }
            if(Mode==1){move.y=0;move=move.normalized*Sheet.WalkSpeed;}else move*=FlySpeed*(k.leftShiftKey.isPressed?5:1);
            var target=transform.position+move*Time.deltaTime;
            if(Mode==1){if(!WorldMacroTerrain.Contains(Sheet,target.x,target.z))return;target.y=Ground(target)+EyeHeight;}
            transform.position=target;
        }
        public void VisitStop(int index)
        {
            if(InspectionStops==null||index<0||index>=InspectionStops.Length||InspectionStops[index]==null||WalkBody==null)return;
            var stop=InspectionStops[index];ResumeWalkAt(stop.position,stop.eulerAngles.y);
        }
        // Boarding/exit restores the capsule's feet, never infers them from an offset camera.
        public void ResumeWalkAt(Vector3 feet,float facingYaw)
        {
            if(WalkBody==null)return;
            resumeFeet=feet;resumeYaw=facingYaw;resumePending=!isActiveAndEnabled;
            WalkBody.enabled=false;WalkBody.transform.SetPositionAndRotation(feet,Quaternion.Euler(0,facingYaw,0));
            WalkBody.enabled=isActiveAndEnabled;yaw=facingYaw;pitch=0;verticalSpeed=0;Mode=1;appliedMode=1;
            cameraVelocity=Vector3.zero;ApplyWalkVisibility();ApplyWalkCamera(true);
        }
        void ApplyWalkVisibility()
        {
            if(WalkVisual!=null)WalkVisual.shadowCastingMode=Mode==1&&!PlayerShoulderView?UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly:UnityEngine.Rendering.ShadowCastingMode.On;
        }
        public void RefreshWalkCamera(bool snap=true)
        { ApplyWalkVisibility();if(Mode==1&&WalkBody!=null)ApplyWalkCamera(snap); }
        void ApplyWalkCamera(bool snap)
        {
            if(!snap&&Time.deltaTime<=0)return;
            Vector3 feet=WalkBody.transform.position;var orbit=Quaternion.Euler(pitch,yaw,0);
            if(!PlayerShoulderView){transform.SetPositionAndRotation(feet+Vector3.up*EyeHeight,orbit);return;}
            var focus=feet+Vector3.up*ShoulderOffset.y;
            var desired=ClampWalkCamera(focus,focus+orbit*new Vector3(ShoulderOffset.x,0,ShoulderOffset.z));
            Vector3 position=snap?desired:Vector3.SmoothDamp(transform.position,desired,ref cameraVelocity,Mathf.Max(.01f,ShoulderResponse),Mathf.Infinity,Time.deltaTime);
            position=ClampWalkCamera(focus,position);
            var direction=focus+orbit*Vector3.forward*10-position;
            transform.SetPositionAndRotation(position,direction.sqrMagnitude>.0001f?Quaternion.LookRotation(direction,Vector3.up):orbit);
        }
        Vector3 ClampWalkCamera(Vector3 focus,Vector3 candidate)
        {
            var delta=candidate-focus;float length=delta.magnitude;if(length<.001f)return focus;
            int count=Physics.SphereCastNonAlloc(focus,ShoulderCollisionRadius,delta/length,cameraHits,length,CameraEnvironmentMask,QueryTriggerInteraction.Ignore);
            if(count==cameraHits.Length)length=Mathf.Min(length,.1f);
            else for(int i=0;i<count;i++)if(cameraHits[i].collider!=WalkBody)length=Mathf.Min(length,Mathf.Max(.05f,cameraHits[i].distance-.03f));
            return focus+delta.normalized*length;
        }
        float Ground(Vector3 p)
        {
            // Collider is the same coarse surface as the view; bridge decks can override it.
            float ground=float.NegativeInfinity;
            foreach(var hit in Physics.RaycastAll(new Vector3(p.x,2000,p.z),Vector3.down,4000,1,QueryTriggerInteraction.Ignore))
                if(hit.collider.name.StartsWith("Terrain_")||hit.collider.name=="Bridge_Deck_TEST"||hit.collider.name.StartsWith("Bridge_Apron_TEST"))ground=Mathf.Max(ground,hit.point.y);
            return float.IsNegativeInfinity(ground)?WorldMacroTerrain.SurfaceHeight(Sheet,p.x,p.z):ground;
        }
        public static Vector3 Along(Vector3[] points,float distance,out Vector3 next)
        {
            if(points.Length==0){next=Vector3.forward;return Vector3.zero;}
            for(int i=1;i<points.Length;i++){float l=Vector3.Distance(points[i-1],points[i]);if(distance<=l){next=points[i];return Vector3.Lerp(points[i-1],points[i],l>.001f?distance/l:0);}distance-=l;}
            next=points[points.Length-1]+(points.Length>1?points[points.Length-1]-points[points.Length-2]:Vector3.forward);return points[points.Length-1];
        }
    }
}
