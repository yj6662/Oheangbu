using UnityEngine;

namespace Oheangbu.App.World.Vehicle
{
    /// <summary>Presentation only. Reads actual motor torque; never sends driving or combat events.</summary>
    [DefaultExecutionOrder(180),DisallowMultipleComponent]
    public sealed class MagicStoneCarDriveVfx : MonoBehaviour
    {
        public WorldMacroPalanquinController Vehicle;
        public MagicStoneCarVfxProfileSO Profile;
        public Renderer[] CoreRenderers;
        public Transform PatternSocket;
        public ParticleSystem[] Vents;
        public int ActivationCount {get;private set;}
        public string Phase {get;private set;}="OFF";
        public float Glow {get;private set;}
        public float ActivationAge=>active?age:-1;
        public bool Armed=>armed;
        public float ReleasedStoppedSeconds=>releasedStoppedSeconds;
        public bool MovedForRearm=>movedForRearm;
        public int PatternStartCount {get;private set;}
        public int VentStartCount {get;private set;}
        public float LastPatternStartAge {get;private set;}=-1;
        public float LastVentStartAge {get;private set;}=-1;
        public int LiveParticles { get {int n=0;if(patternSystems!=null)foreach(var p in patternSystems)if(p!=null)n+=p.particleCount;if(Vents!=null)foreach(var p in Vents)if(p!=null)n+=p.particleCount;return n;} }
        GameObject pattern;
        ParticleSystem[] patternSystems;
        MaterialPropertyBlock block;
        bool armed=true,movedForRearm,active,wasPaused,patternStarted,ventStarted;
        float age,cooldown,stoppedSeconds,releasedStoppedSeconds;
        Vector3 activationPosition;
        static readonly int Emission=Shader.PropertyToID("_EmissionColor");
        void Start(){Prepare();ClearPresentation();}
        public bool Prepare()
        {
            if(Vehicle==null||Profile==null||PatternSocket==null)return false;
            if(block==null)block=new MaterialPropertyBlock();
            if(pattern==null&&Profile.PatternPrefab!=null)
            {
                pattern=Instantiate(Profile.PatternPrefab,PatternSocket,false);pattern.name="DrivePattern_Runtime";
                pattern.transform.localPosition=Vector3.zero;pattern.transform.localRotation=Quaternion.identity;
                patternSystems=pattern.GetComponentsInChildren<ParticleSystem>(true);
                foreach(var ps in patternSystems){var main=ps.main;main.useUnscaledTime=false;main.playOnAwake=false;}
            }
            return true;
        }
        void Update()
        {
            if(Vehicle==null||Profile==null||!Vehicle.isActiveAndEnabled||!Vehicle.DriverPresent)
            {ClearPresentation();armed=true;movedForRearm=false;cooldown=stoppedSeconds=releasedStoppedSeconds=0;Phase="OFF";return;}
            if(Time.deltaTime<=0)
            {if(!wasPaused){ClearPresentation();armed=false;movedForRearm=false;stoppedSeconds=releasedStoppedSeconds=0;activationPosition=transform.position;}wasPaused=true;Phase="PAUSED_CLEAR";return;}
            wasPaused=false;if(block==null&&!Prepare())return;
            float dt=Time.deltaTime;cooldown=Mathf.Max(0,cooldown-dt);
            bool stopped=Vehicle.Speed<=Profile.StoppedSpeed;
            stoppedSeconds=stopped?stoppedSeconds+dt:0;
            bool released=Mathf.Abs(Vehicle.RequestedThrottle)<Profile.PedalThreshold;
            releasedStoppedSeconds=stopped&&released?releasedStoppedSeconds+dt:0;
            // A short torque spike against a wall is not a departure. Require actual planar displacement.
            if(Vector3.ProjectOnPlane(transform.position-activationPosition,Vector3.up).sqrMagnitude>.25f)movedForRearm=true;
            bool ready=movedForRearm?stoppedSeconds>=Profile.CooldownSeconds:releasedStoppedSeconds>=Profile.CooldownSeconds;
            if(!armed&&!active&&cooldown<=0&&ready)armed=true;
            // Actual nonzero torque already includes the controller's brake/direction-change gating.
            bool driven=Mathf.Abs(Vehicle.AppliedMotorTorque)>=Profile.MinimumMotorTorque;
            if(armed&&Vehicle.Speed<=Profile.StoppedSpeed&&!released&&driven)Activate();
            float target=driven||Vehicle.Speed>Profile.StoppedSpeed?Profile.RunningEmission:Profile.IdleEmission;
            if(active)
            {
                age+=dt;float patternEnd=Profile.IgnitionSeconds+Profile.PatternSeconds,end=patternEnd+Profile.VentSeconds;
                target=Mathf.Lerp(Profile.RunningEmission,Profile.PeakEmission,1-Mathf.Clamp01(Mathf.Abs(age-Profile.IgnitionSeconds)/Mathf.Max(.01f,Profile.IgnitionSeconds)));
                if(age>=Profile.IgnitionSeconds&&!patternStarted)
                {patternStarted=true;LastPatternStartAge=age;PatternStartCount++;if(age<patternEnd&&patternSystems!=null)foreach(var p in patternSystems)if(p!=null)p.Play(false);}
                if(age>=patternEnd){Stop(patternSystems);if(!ventStarted){ventStarted=true;LastVentStartAge=age;VentStartCount++;if(age<end&&Vents!=null)foreach(var p in Vents)if(p!=null){p.Play(false);p.Emit(Profile.VentParticles);}}}
                Phase=age<Profile.IgnitionSeconds?"IGNITING":age<patternEnd?"PATTERN":"VENTING";
                if(age>=end)Stop(Vents);
                if(age>=end){active=false;Phase="RUNNING";}
            }
            else Phase=driven?"RUNNING":"IDLE";
            Glow=Mathf.Lerp(Glow,target,1-Mathf.Exp(-dt/Mathf.Max(.01f,Profile.EmissionResponse)));ApplyGlow();
        }
        void Activate()
        {
            if(!Prepare())return;
            active=true;armed=false;movedForRearm=false;age=stoppedSeconds=releasedStoppedSeconds=0;cooldown=Profile.CooldownSeconds;activationPosition=transform.position;ActivationCount++;
            patternStarted=ventStarted=false;LastPatternStartAge=LastVentStartAge=-1;Stop(patternSystems);Stop(Vents);
        }
        void ApplyGlow()
        {
            if(CoreRenderers==null||block==null||Profile==null)return;
            foreach(var renderer in CoreRenderers)if(renderer!=null){renderer.GetPropertyBlock(block);block.SetColor(Emission,Profile.CoreColour*Glow);renderer.SetPropertyBlock(block);}
        }
        static void Stop(ParticleSystem[] systems)
        {if(systems!=null)foreach(var p in systems)if(p!=null)p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);}
        public void ClearPresentation()
        {active=false;patternStarted=ventStarted=false;age=0;Glow=0;Stop(patternSystems);Stop(Vents);ApplyGlow();}
        void OnDisable(){ClearPresentation();armed=true;movedForRearm=false;releasedStoppedSeconds=stoppedSeconds=0;Phase="OFF";}
        void OnDestroy(){if(pattern!=null)Destroy(pattern);}
    }
}
