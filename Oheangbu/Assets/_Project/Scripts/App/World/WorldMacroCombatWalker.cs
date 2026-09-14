using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEngine;

namespace Oheangbu.App.World
{
    // Optional walking adapter for the existing vehicle seat. The review controller stays disabled.
    public sealed class WorldMacroCombatWalker:MonoBehaviour
    {
        public PlayerMotor Motor;
        public CharacterController Body;
        public CameraRigController CameraRig;
        public Camera ViewCamera;
        public DrawingInputController Drawing;
        public CombatLoopWiring Wiring;
        public Renderer[] Visuals;
        public float EyeHeight=1.55f;
        public bool Seated {get;private set;}
        public bool CanBoard=>isActiveAndEnabled&&!Seated&&Motor.enabled&&Body.enabled&&!Drawing.InDrawMode&&Motor.CanBeginDrawing&&Motor.CanStandForBoarding;
        Transform cameraParent;Vector3 cameraPosition;Quaternion cameraRotation;
        System.Func<bool> drawingGate;
        void OnEnable()
        {
            drawingGate = () => Motor == null || Motor.CanBeginDrawing;
            if (Drawing != null) Drawing.EntryAllowed = drawingGate;
        }
        // Authoring may assign references immediately after AddComponent/OnEnable.
        void Start()
        {
            if (Drawing != null) Drawing.EntryAllowed = drawingGate;
        }
        void OnDisable()
        {
            if (Drawing != null && Drawing.EntryAllowed == drawingGate) Drawing.EntryAllowed = null;
        }
        public void Suspend()
        {
            cameraParent=ViewCamera.transform.parent;cameraPosition=ViewCamera.transform.localPosition;cameraRotation=ViewCamera.transform.localRotation;
            Seated=true;Drawing.enabled=false;Motor.enabled=false;CameraRig.enabled=false;Wiring.enabled=false;Body.enabled=false;
            var target=Motor.GetComponent<LockOn>();if(target!=null&&target.IsLocked)target.Toggle();
            foreach(var r in Visuals)if(r!=null)r.enabled=false;
            ViewCamera.transform.SetParent(null,true);
        }
        public void Resume(Vector3 feet,float yaw)
        {
            Body.enabled=false;Body.transform.SetPositionAndRotation(feet,Quaternion.Euler(0,yaw,0));
            ViewCamera.transform.SetParent(cameraParent,false);ViewCamera.transform.localPosition=cameraPosition;ViewCamera.transform.localRotation=cameraRotation;
            foreach(var r in Visuals)if(r!=null)r.enabled=true;
            Seated=false;Body.enabled=true;Motor.ResetMotion();CameraRig.enabled=true;Motor.enabled=true;Drawing.enabled=true;Wiring.enabled=true;
        }
    }
}
