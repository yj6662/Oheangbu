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
        bool[] visualStates;
        PlayerVisualDriver visualDriver;bool visualDriverWasEnabled;
        Riposte301 riposte;
        void OnEnable()
        {
            // D301 앞잡: a Q press claimed by the volley never opens the draw mode
            if (riposte == null) riposte = GetComponent<Riposte301>();
            if (riposte == null)
            {
                var profile = Resources.Load<Riposte301Profile>("Riposte301Profile");
                if (profile != null) { riposte = gameObject.AddComponent<Riposte301>(); riposte.Profile = profile; }
            }
            if (riposte != null) riposte.Walker = this;
            drawingGate = () => (Motor == null || Motor.CanBeginDrawing) && (riposte == null || !riposte.SuppressDraw);
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
            visualStates=new bool[Visuals.Length];for(int i=0;i<Visuals.Length;i++)if(Visuals[i]!=null)visualStates[i]=Visuals[i].enabled;
            visualDriver=Motor.GetComponent<PlayerVisualDriver>();visualDriverWasEnabled=visualDriver!=null&&visualDriver.enabled;if(visualDriver!=null)visualDriver.enabled=false;
            foreach(var r in Visuals)if(r!=null)r.enabled=false;
            ViewCamera.transform.SetParent(null,true);
        }
        // #308 D308-8 (SPEC-VEHICLE-UX-308 §6): the rider keeps the body visible while seated. Only renderers that were enabled before
        // Suspend come back (Suspend's own record); Resume restores the same record as before. Presentation only, no gameplay state.
        public bool SeatedBodyVisible308 {get;private set;}
        public void SetSeatedBodyVisible308(bool visible)
        {
            if(!Seated||Visuals==null){SeatedBodyVisible308=false;return;}
            for(int i=0;i<Visuals.Length;i++)if(Visuals[i]!=null)Visuals[i].enabled=visible&&visualStates!=null&&i<visualStates.Length&&visualStates[i];
            SeatedBodyVisible308=visible;
        }
        public void Resume(Vector3 feet,float yaw)
        {
            Body.enabled=false;Body.transform.SetPositionAndRotation(feet,Quaternion.Euler(0,yaw,0));
            ViewCamera.transform.SetParent(cameraParent,false);ViewCamera.transform.localPosition=cameraPosition;ViewCamera.transform.localRotation=cameraRotation;
            for(int i=0;i<Visuals.Length;i++)if(Visuals[i]!=null)Visuals[i].enabled=visualStates!=null&&i<visualStates.Length&&visualStates[i];
            Seated=false;SeatedBodyVisible308=false;Body.enabled=true;Motor.ResetMotion();CameraRig.enabled=true;Motor.enabled=true;Drawing.enabled=true;Wiring.enabled=true;if(visualDriver!=null)visualDriver.enabled=visualDriverWasEnabled;
        }
    }
}
