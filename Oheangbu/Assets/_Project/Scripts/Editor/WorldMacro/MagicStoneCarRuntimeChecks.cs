using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
using MagicStoneCarWallObserver=Oheangbu.App.World.Vehicle.MagicStoneCarWallObserver;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Explicit bounded API probes. Temporary colliders are removed; no keys, walking, route tests or production obstacles are created.</summary>
    [InitializeOnLoad]
    public static class MagicStoneCarRuntimeChecks
    {
        [Serializable] public sealed class Check {public string name,status,detail;}
        [Serializable] public sealed class Sample
        {public string phase,vfxPhase;public float gameTime,speed,torque,glow,age,releasedStoppedSeconds;public int activations,patternStarts,ventStarts,particles,frontContacts,rearContacts;public bool armed,movedForRearm,observerAlive;}
        [Serializable] public sealed class Report
        {
            public string utc,status,scope;public Check[] checks;public Sample[] samples;
            public bool cleanupComplete;public float maximumDisplacementM,maximumFrameDelta,pausedGameSeconds,pausedRealSeconds;
            public int frontContacts,rearContacts;public float[] activationTimes;
            public int sampleCount;public string sampling="At most 60Hz during active VFX, 15Hz otherwise, plus phase changes. Contact callbacks remain continuous.";
        }
        static string Output=>MagicStoneCarAuthoring.Output;
        static WorldMacroPalanquinSeat seat;
        static WorldMacroPalanquinController car;
        static MagicStoneCarDriveVfx vfx;
        static MagicStoneCarWallObserver observer;
        static GameObject fixtures;
        static bool running,seatEnabled,paused,pausedDone,forwardChecked,tapsChecked,reverseChecked;
        public static bool IsRunning=>running;
        static Vector3 position,velocity,angularVelocity;
        static Quaternion rotation;
        static float timeScale,startGame,lastGame,pauseStartGame,resumeGame,nextSample;
        static string lastSamplePhase,lastSampleVfxPhase;
        static double deadline,pauseStartReal;
        static int startActivations,startPattern,startVent,forwardEnd,tapEnd,reverseStart,pauseActivation;
        static readonly List<Check> checks=new List<Check>();
        static readonly List<Sample> samples=new List<Sample>();
        static readonly List<float> activations=new List<float>();
        static Report report;
        static bool negativeTorque;
        static float firstPatternAge=-1,firstVentAge=-1;
        static MagicStoneCarRuntimeChecks()
        {
            AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(running)Finish("ABORTED","Assembly reload interrupted the live diagnostic.");};
            EditorApplication.playModeStateChanged+=s=>{if(running&&s==PlayModeStateChange.ExitingPlayMode)Finish("ABORTED","Play mode exited.");};
        }
        static WorldMacroPalanquinSeat FindSeat()
        {
            if(!EditorApplication.isPlaying||EditorApplication.isPaused)throw new InvalidOperationException("Running unpaused macro Play scene required; this tool does not start it.");
            var root=GameObject.Find(WorldMacroPalanquinAuthoring.RootName);var s=root==null?null:root.GetComponent<WorldMacroPalanquinSeat>();
            if(s==null||s.GetComponent<MagicStoneCarDriveVfx>()==null)throw new InvalidOperationException("New MagicStoneCar must be installed.");return s;
        }
        static void Add(string name,bool pass,string detail)=>checks.Add(new Check{name=name,status=pass?"PASS":"FAIL",detail=detail});
        public static string CameraChecks()
        {
            if(running)throw new InvalidOperationException("Finish the drive-state probe before camera ownership checks.");
            var s=FindSeat();var r=s.ReviewController;if(s.Occupied||!s.CanBoard)throw new InvalidOperationException("Unoccupied parked vehicle and player already within boarding range required. The helper does not place or walk the player.");
            var localChecks=new List<Check>();GameObject obstacle=null;bool began=false;bool oldEnable=r.enabled;Vector3 startingFeet=r.WalkBody.transform.position;
            try
            {
                r.enabled=false;r.enabled=oldEnable;Record("player OnEnable preserves feet",Vector3.Distance(startingFeet,r.WalkBody.transform.position)<.001f,"Same-frame disable/enable; no CharacterController.Move call.");
                Vector3 focus=r.WalkBody.transform.position+Vector3.up*r.ShoulderOffset.y;r.RefreshWalkCamera(true);
                Record("player shoulder offset active",r.PlayerShoulderView&&Vector3.Distance(r.transform.position,r.WalkBody.transform.position+Vector3.up*r.EyeHeight)>.3f,"Actual shoulder camera; cap remains visible.");
                bool playerWall=CameraWall(focus,r.transform.position,r.ShoulderCollisionRadius,()=>r.RefreshWalkCamera(true),r.transform,out obstacle,out string playerDetail);Record("player SphereCast response",playerWall,playerDetail);DestroyFixture(ref obstacle);r.RefreshWalkCamera(true);
                began=s.TryBoard();Record("public boarding",began,"Actual TryBoard; no key synthesis or player relocation.");
                if(began)
                {
                    Record("vehicle starts in shoulder view",!s.SeatedView,"StartInSeatedView=false.");bool first=s.ToggleView();Record("V path switches to first person",first&&s.SeatedView&&Vector3.Distance(s.ViewCamera.transform.position,s.SeatSocket.position)<.001f,"Actual ToggleView method shared with V input.");
                    // Compare the actual camera after the public V path with the authored socket angle
                    // after removing only the permitted body sway. Previously 12 degrees became 3.6.
                    Quaternion bodyRotation=s.Vehicle.BodyVisualRoot.rotation;
                    Quaternion authoredSeat=Quaternion.Inverse(bodyRotation)*s.SeatSocket.rotation;
                    Quaternion dampedBody=Quaternion.Slerp(Quaternion.Euler(0,s.Vehicle.transform.eulerAngles.y,0),bodyRotation,Mathf.Clamp01(s.Vehicle.Profile.SeatTiltFollow));
                    Quaternion actualSeat=Quaternion.Inverse(dampedBody)*s.ViewCamera.transform.rotation;
                    float authoredPitch=Mathf.DeltaAngle(0,authoredSeat.eulerAngles.x),actualPitch=Mathf.DeltaAngle(0,actualSeat.eulerAngles.x);
                    Record("first-person preserves authored seat pitch",first&&s.SeatedView&&Quaternion.Angle(actualSeat,authoredSeat)<.1f,"Actual neutral-input camera: authored "+authoredPitch.ToString("F3")+"deg / observed "+actualPitch.ToString("F3")+"deg after permitted body-sway removal. SeatTiltFollow="+s.Vehicle.Profile.SeatTiltFollow.ToString("F2")+" applies to the body, not the socket.");
                    bool second=s.ToggleView();Record("V path returns to shoulder",second&&!s.SeatedView,"Same camera owner remains active.");
                    var vehicleFocus=s.ExternalLookSocket.position+Vector3.up*s.Vehicle.Profile.ExternalPivotHeight;
                    bool vehicleWall=CameraWall(vehicleFocus,s.ViewCamera.transform.position,s.Vehicle.Profile.CameraCollisionRadius,()=>s.RefreshCamera(true),s.ViewCamera.transform,out obstacle,out string vehicleDetail);Record("vehicle SphereCast response",vehicleWall,vehicleDetail);DestroyFixture(ref obstacle);s.RefreshCamera(true);
                    bool exit=s.TryExit();Record("safe public exit",exit,s.LastInteraction);
                    if(exit)
                    {
                        var exited=r.WalkBody.transform.position;r.enabled=false;r.enabled=true;Record("post-exit OnEnable preserves exit feet",Vector3.Distance(exited,r.WalkBody.transform.position)<.001f,"Restores explicit exit feet rather than shoulder camera position.");
                        Record("player shoulder restored after exit",r.enabled&&r.Mode==1&&r.WalkBody.enabled&&r.PlayerShoulderView,"Player owns input/camera again.");
                        bool again=s.TryBoard();Record("reboarding",again,s.LastInteraction);if(again)Record("second safe exit",s.TryExit(),s.LastInteraction);
                    }
                }
            }
            finally
            {
                DestroyFixture(ref obstacle);if(began&&s.Occupied)s.TryExit();
                var result=new Report{utc=DateTime.UtcNow.ToString("o"),status=localChecks.Count>0&&localChecks.All(c=>c.status=="PASS")?"PASS_BOUNDED_CAMERA_API":"FINDINGS",scope="Actual camera ownership and SphereCast functions in Play. A temporary collider is inserted into each camera ray then removed. No walking, simulated keyboard input, general-world camera traversal or comfort approval.",checks=localChecks.ToArray(),cleanupComplete=obstacle==null&&!s.Occupied};
                Directory.CreateDirectory(Output);File.WriteAllText(Output+"/camera_checks.json",JsonUtility.ToJson(result,true));
            }
            return string.Join("\n",localChecks.Select(c=>c.status+" "+c.name+" "+c.detail));
            void Record(string name,bool pass,string detail)=>localChecks.Add(new Check{name=name,status=pass?"PASS":"FAIL",detail=detail});
        }
        static bool CameraWall(Vector3 focus,Vector3 original,float radius,Action update,Transform camera,out GameObject obstacle,out string detail)
        {
            var delta=original-focus;float distance=delta.magnitude;if(distance<.9f){obstacle=null;detail="Current camera already too close to an existing obstacle for this bounded fixture.";return false;}
            obstacle=new GameObject("MagicStoneDiagnosticCameraWall"){hideFlags=HideFlags.HideAndDontSave};var box=obstacle.AddComponent<BoxCollider>();box.size=new Vector3(1.2f,1.2f,.08f);
            obstacle.transform.SetPositionAndRotation(focus+delta.normalized*(distance*.6f),Quaternion.LookRotation(delta.normalized,Vector3.up));Physics.SyncTransforms();update();
            float resulting=Vector3.Distance(focus,camera.position),clearance=Vector3.Distance(camera.position,box.ClosestPoint(camera.position));
            detail="Before "+distance.ToString("F3")+"m / blocked "+resulting.ToString("F3")+"m; fixture clearance "+clearance.ToString("F3")+"m. Temporary collider, no rendered wall.";
            return resulting<distance*.6f&&clearance>=radius-.02f&&Finite(camera.position);
        }
        static void DestroyFixture(ref GameObject go){if(go!=null)Object.DestroyImmediate(go);go=null;Physics.SyncTransforms();}
        public static string BeginDriveStateChecks()
        {
            if(running)throw new InvalidOperationException("Already running.");seat=FindSeat();car=seat.Vehicle;vfx=car.GetComponent<MagicStoneCarDriveVfx>();
            if(seat.Occupied||car.DriverPresent||!car.IsConfigured||car.Speed>.3f||Time.timeScale<=0)throw new InvalidOperationException("Configured unoccupied parked dynamic vehicle required.");
            if(car.Profile==null||car.Body.isKinematic)throw new InvalidOperationException("Actual dynamic WheelCollider body required.");
            position=car.Body.position;rotation=car.Body.rotation;velocity=car.Body.linearVelocity;angularVelocity=car.Body.angularVelocity;timeScale=Time.timeScale;seatEnabled=seat.enabled;
            checks.Clear();samples.Clear();activations.Clear();forwardChecked=tapsChecked=reverseChecked=paused=pausedDone=negativeTorque=false;firstPatternAge=firstVentAge=-1;forwardEnd=tapEnd=reverseStart=pauseActivation=0;nextSample=0;lastSamplePhase=lastSampleVfxPhase=null;
            report=new Report{utc=DateTime.UtcNow.ToString("o"),status="RUNNING",scope="Actual motor torque and contact with two temporary collider walls on the installed site; API pedal holds/taps/reverse and runtime timescale pause. Roughly 14 game seconds, max 0.65m displacement. No keyboard synthesis, player walking, full route or comfort claim."};
            fixtures=new GameObject("MagicStoneDiagnosticWalls"){hideFlags=HideFlags.HideAndDontSave};float front=float.NegativeInfinity,rear=float.PositiveInfinity;
            foreach(var box in car.GetComponentsInChildren<BoxCollider>())
            for(int i=0;i<8;i++){var local=box.center+Vector3.Scale(box.size*.5f,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));float z=car.transform.InverseTransformPoint(box.transform.TransformPoint(local)).z;front=Mathf.Max(front,z);rear=Mathf.Min(rear,z);}
            if(!float.IsFinite(front)||!float.IsFinite(rear)){Object.DestroyImmediate(fixtures);throw new InvalidOperationException("Actual hull bounds missing.");}
            for(int i=0;i<2;i++){var wall=new GameObject(i==0?"MagicStoneDiagnosticWall_Front":"MagicStoneDiagnosticWall_Rear");wall.transform.SetParent(fixtures.transform);wall.transform.SetPositionAndRotation(car.transform.TransformPoint(new Vector3(0,1.8f,i==0?front+.075f:rear-.075f)),car.transform.rotation);wall.AddComponent<BoxCollider>().size=new Vector3(3.1f,4.2f,.1f);}
            observer=car.gameObject.AddComponent<MagicStoneCarWallObserver>();
            if(observer==null||!observer.isActiveAndEnabled)
            {if(observer!=null)Object.DestroyImmediate(observer);observer=null;Object.DestroyImmediate(fixtures);fixtures=null;throw new InvalidOperationException("Runtime contact observer could not be attached; diagnostic not started.");}
            seat.enabled=false;car.SetDriverPresent(true);car.SetDriverInput(0,0,true);vfx.ClearPresentation();startActivations=vfx.ActivationCount;startPattern=vfx.PatternStartCount;startVent=vfx.VentStartCount;
            startGame=lastGame=Time.time;deadline=EditorApplication.timeSinceStartup+90;running=true;Physics.SyncTransforms();EditorApplication.update+=Tick;
            return "RUNNING: actual front/rear wall contacts, held/tapped API throttle, reverse, one runtime pause; fixtures and vehicle pose restored on completion. No player movement.";
        }
        public static string Poll()
        {
            if(running)return "RUNNING "+(Time.time-startGame).ToString("F2")+" game seconds; "+samples.Count+" samples; phase="+vfx.Phase;
            string path=Output+"/drive_state_checks.json";if(!File.Exists(path))return "NOT_RUN";var data=JsonUtility.FromJson<Report>(File.ReadAllText(path));return data.status+"; checks="+data.checks.Count(c=>c.status=="PASS")+"/"+data.checks.Length+"; cleanup="+data.cleanupComplete+"; raw="+path;
        }
        public static string End(){if(running)Finish("ABORTED","Stopped explicitly.");return Poll();}
        static void Tick()
        {
            if(!running)return;
            try
            {
                if(car==null||vfx==null||!EditorApplication.isPlaying){Finish("ABORTED","Live vehicle unavailable.");return;}
                if(observer==null||!observer.isActiveAndEnabled||fixtures==null){Finish("ABORTED","Runtime contact observer or stop-wall fixture was removed; no contact pass inferred.");return;}
                if(EditorApplication.timeSinceStartup>deadline){Finish("ABORTED","Wall-clock deadline reached.");return;}
                if(EditorApplication.isPaused)return;
                if(paused)
                {
                    if(EditorApplication.timeSinceStartup-pauseStartReal<.4)return;
                    report.pausedGameSeconds=Time.time-pauseStartGame;report.pausedRealSeconds=(float)(EditorApplication.timeSinceStartup-pauseStartReal);
                    Add("runtime pause clears visual effects",vfx.Phase=="PAUSED_CLEAR"&&vfx.LiveParticles==0&&vfx.Glow==0&&report.pausedGameSeconds<.001f,"Time.timeScale=0; game delta "+report.pausedGameSeconds.ToString("F4")+"s, real "+report.pausedRealSeconds.ToString("F2")+"s; phase "+vfx.Phase);
                    Time.timeScale=timeScale;paused=false;pausedDone=true;resumeGame=Time.time;lastGame=Time.time;return;
                }
                float now=Time.time,dt=now-lastGame;if(dt<=0)return;lastGame=now;float elapsed=now-startGame;
                report.maximumFrameDelta=Mathf.Max(report.maximumFrameDelta,dt);report.maximumDisplacementM=Mathf.Max(report.maximumDisplacementM,Vector3.ProjectOnPlane(car.Body.position-position,Vector3.up).magnitude);
                if(!Finite(car.Body.position)||!Finite(car.Body.linearVelocity)||report.maximumDisplacementM>.65f||Mathf.Abs(car.Body.position.y-position.y)>1.25f||Vector3.Dot(car.transform.up,Vector3.up)<.75f){Finish("FINDINGS","Vehicle left bounded finite/displacement/height/tilt envelope.");return;}
                int count=vfx.ActivationCount-startActivations;if(count>activations.Count)activations.Add(elapsed);
                if(firstPatternAge<0&&vfx.PatternStartCount>startPattern)firstPatternAge=vfx.LastPatternStartAge;
                if(firstVentAge<0&&vfx.VentStartCount>startVent)firstVentAge=vfx.LastVentStartAge;
                string phase=elapsed<1.8f?"settle":elapsed<5?"front_wall_hold":elapsed<6.4f?"tap_api":elapsed<8.4f?"released":elapsed<11.2f?"rear_wall_reverse":elapsed<12.6f?"released_before_pause":"pause_departure";
                if(elapsed>=nextSample||phase!=lastSamplePhase||vfx.Phase!=lastSampleVfxPhase)
                {
                    samples.Add(new Sample{phase=phase,vfxPhase=vfx.Phase,gameTime=elapsed,speed=car.Speed,torque=car.AppliedMotorTorque,glow=vfx.Glow,age=vfx.ActivationAge,activations=count,patternStarts=vfx.PatternStartCount-startPattern,ventStarts=vfx.VentStartCount-startVent,particles=vfx.LiveParticles,armed=vfx.Armed,releasedStoppedSeconds=vfx.ReleasedStoppedSeconds,movedForRearm=vfx.MovedForRearm,frontContacts=observer.FrontContacts,rearContacts=observer.RearContacts,observerAlive=true});
                    nextSample=elapsed+(vfx.ActivationAge>=0?1f/60f:1f/15f);lastSamplePhase=phase;lastSampleVfxPhase=vfx.Phase;
                }
                if(elapsed>=5&&!forwardChecked){forwardChecked=true;forwardEnd=count;Add("held throttle at front wall activates once",count==1&&observer.FrontContacts>0,"Activations "+count+"; actual front collision callbacks "+observer.FrontContacts);}
                if(elapsed>=6.4f&&!tapsChecked){tapsChecked=true;tapEnd=count;Add("wall pedal taps do not rearm",tapEnd==forwardEnd&&!vfx.MovedForRearm,"0.1s pressed/released API pulses; additional activations="+(tapEnd-forwardEnd)+"; actual displacement rearm="+vfx.MovedForRearm+". A stationary car requires continuous pedal release for the full cooldown. These are not synthesized keyboard events.");}
                if(elapsed>=8.4f&&reverseStart==0)reverseStart=count;
                if(phase=="rear_wall_reverse"&&car.AppliedMotorTorque<-.1f)negativeTorque=true;
                if(elapsed>=11.2f&&!reverseChecked){reverseChecked=true;Add("released pedal rearms reverse departure",negativeTorque&&count-reverseStart==1&&observer.RearContacts>0,"Negative actual torque="+negativeTorque+"; reverse activations="+(count-reverseStart)+"; rear collision callbacks="+observer.RearContacts);}
                if(pausedDone)
                {
                    car.SetDriverInput(1,0,false);
                    if(now-resumeGame>=.8f)
                    {
                        Add("held pedal after pause does not retrigger",vfx.ActivationCount==pauseActivation,"Continuous stopped pedal release for one cooldown, or actual departure followed by stopped cooldown, required before another activation.");
                        float tolerance=report.maximumFrameDelta+.03f;
                        Add("sequential ignition pattern and vent timing",firstPatternAge>=vfx.Profile.IgnitionSeconds&&firstPatternAge<=vfx.Profile.IgnitionSeconds+tolerance&&firstVentAge>=vfx.Profile.IgnitionSeconds+vfx.Profile.PatternSeconds&&firstVentAge<=vfx.Profile.IgnitionSeconds+vfx.Profile.PatternSeconds+tolerance,"First pattern age "+firstPatternAge.ToString("F3")+"s; first vent age "+firstVentAge.ToString("F3")+"s; frame tolerance "+tolerance.ToString("F3")+"s.");
                        Add("single stage start per activation",vfx.PatternStartCount-startPattern<=count&&vfx.VentStartCount-startVent<=count,"Pattern starts="+(vfx.PatternStartCount-startPattern)+", vent starts="+(vfx.VentStartCount-startVent)+", activation count="+count+". Paused activation may end before its vent stage.");
                        Finish(checks.All(c=>c.status=="PASS")?"PASS_BOUNDED_DRIVE_STATE":"FINDINGS","Actual drive-state sequence completed.");return;
                    }
                }
                else if(elapsed>=12.6f&&vfx.ActivationAge>=vfx.Profile.IgnitionSeconds+.02f)
                {pauseActivation=vfx.ActivationCount;pauseStartGame=now;pauseStartReal=EditorApplication.timeSinceStartup;paused=true;Time.timeScale=0;return;}
                if(phase=="settle"||phase=="released"||phase=="released_before_pause")car.SetDriverInput(0,0,true);
                else if(phase=="front_wall_hold"||phase=="pause_departure")car.SetDriverInput(1,0,false);
                else if(phase=="tap_api"){bool pressed=(int)((elapsed-5)/.1f)%2==1;car.SetDriverInput(pressed?1:0,0,!pressed);}
                else car.SetDriverInput(-1,0,false);
            }
            catch(Exception e){Finish("ERROR",e.ToString());}
        }
        static bool Finite(Vector3 value)=>float.IsFinite(value.x)&&float.IsFinite(value.y)&&float.IsFinite(value.z);
        static void Finish(string status,string detail)
        {
            if(!running)return;running=false;EditorApplication.update-=Tick;Time.timeScale=timeScale;report.status=status;Add("completion detail",status.StartsWith("PASS"),detail);
            report.frontContacts=observer==null?0:observer.FrontContacts;report.rearContacts=observer==null?0:observer.RearContacts;
            try
            {
                if(car!=null){car.SetDriverPresent(false);if(vfx!=null)vfx.ClearPresentation();car.Body.position=position;car.Body.rotation=rotation;car.Body.linearVelocity=velocity;car.Body.angularVelocity=angularVelocity;car.transform.SetPositionAndRotation(position,rotation);}
                if(observer!=null)Object.DestroyImmediate(observer);if(fixtures!=null)Object.DestroyImmediate(fixtures);fixtures=null;observer=null;Physics.SyncTransforms();
                if(seat!=null)seat.enabled=seatEnabled;report.cleanupComplete=fixtures==null&&observer==null&&Mathf.Approximately(Time.timeScale,timeScale);
                Add("temporary fixtures and particles cleaned",report.cleanupComplete&&(vfx==null||vfx.LiveParticles==0&&vfx.Glow==0),"Temporary wall colliders removed, original vehicle pose/velocity/time scale and seat component restored.");
            }
            finally
            {
                report.checks=checks.ToArray();report.samples=samples.ToArray();report.sampleCount=samples.Count;report.activationTimes=activations.ToArray();Directory.CreateDirectory(Output);File.WriteAllText(Output+"/drive_state_checks.json",JsonUtility.ToJson(report,false));car=null;seat=null;vfx=null;
            }
        }
    }
}
