using System;
using System.IO;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.World.Vehicle;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
using MagicStoneCarWallObserver=Oheangbu.App.World.Vehicle.MagicStoneCarWallObserver;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>One still from actual torque-triggered ignition. Never starts or simulates the VFX itself.</summary>
    [InitializeOnLoad]
    public static class MagicStoneCarIgnitionCapture
    {
        [Serializable] public sealed class Report
        {
            public string utc,status,detail,file,phase;
            public string scope="Diagnostic departure against temporary invisible physical stop walls. One composed engine-camera still during actual torque-triggered PATTERN; not keyboard input, ordinary free driving, video or visual approval.";
            public int width=1920,height=1080,activationDelta,patternStartDelta,particles,frontContacts,rearContacts;
            public bool imageWritten,restored,fixturesRemoved,particlesCleared,driverRestored,seatOwnerRestored;
            public float gameSeconds,activationAge,appliedTorque,speed,maximumDisplacementM,commitBefore,commitAtRender,commitAfter,positionRestoreErrorM,rotationRestoreErrorDeg,velocityRestoreErrorMps,angularRestoreError;
            public Vector3 originalPosition,captureVehiclePosition,cameraPosition,cameraTarget;
            public MagicStoneCarReviewTools.VariantReadiness variants;
        }
        static string Output=>MagicStoneCarAuthoring.Output;
        public static bool IsRunning=>running;
        static bool running,seatWasEnabled,driverWasPresent,wasSleeping;
        static WorldMacroPalanquinController car;
        static WorldMacroPalanquinSeat seat;
        static MagicStoneCarDriveVfx vfx;
        static MagicStoneCarWallObserver observer;
        static GameObject fixtures;
        static Vector3 position,velocity,angularVelocity;
        static Quaternion rotation;
        static float startGame,lastGame;
        static double deadline;
        static int initialActivations,initialPatterns;
        static Report report;
        static Func<bool> dressingIsRunning;
        static bool overlapChecksInitialized;

        static MagicStoneCarIgnitionCapture()
        {
            AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(running)Finish("ABORTED","Assembly reload interrupted capture.");};
            EditorApplication.playModeStateChanged+=s=>{if(running&&s==PlayModeStateChange.ExitingPlayMode)Finish("ABORTED","Play mode exited before the still.");};
        }
        static void PrepareOverlapChecks()
        {
            if(overlapChecksInitialized)return;
            // Resolve the optional module once. The timed capture never reads reports or reflects per frame.
            var type=typeof(WorldMacroBuilder).Assembly.GetType("Oheangbu.EditorTools.WorldMacro.WorldMacroDressingProbe");
            if(type!=null)
            {
                var getter=type.GetProperty("IsRunning",BindingFlags.Public|BindingFlags.Static)?.GetGetMethod();
                if(getter==null||getter.ReturnType!=typeof(bool))throw new InvalidOperationException("Dressing probe must expose its in-memory IsRunning flag before capture.");
                dressingIsRunning=(Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>),getter);
            }
            overlapChecksInitialized=true;
        }
        static string OtherDiagnostic()
        {
            if(WorldMacroPalanquinProbe.IsRunning)return "palanquin physics probe";
            if(MagicStoneCarRuntimeChecks.IsRunning)return "drive-state probe";
            if(dressingIsRunning!=null&&dressingIsRunning())return "dressing frame measurement";
            return null;
        }
        public static string BeginIgnitionCapture()
        {
            if(running)throw new InvalidOperationException("Ignition capture already running; Poll or End it first.");
            if(!EditorApplication.isPlaying||EditorApplication.isPaused||Time.timeScale<=0||SceneManager.GetActiveScene().path!=WorldMacroBuilder.ScenePath)
                throw new InvalidOperationException("Existing macro scene must be running and unpaused. This tool does not start Play.");
            PrepareOverlapChecks();string other=OtherDiagnostic();if(other!=null)throw new InvalidOperationException("Wait for "+other+" to finish.");
            float commit=Prologue.PrologueAudit.CommitRatio();if(commit>=.85f)throw new InvalidOperationException("Stopped before diagnostic/capture: system commit >=85%.");
            var root=GameObject.Find(WorldMacroPalanquinAuthoring.RootName);car=root==null?null:root.GetComponent<WorldMacroPalanquinController>();
            seat=root==null?null:root.GetComponent<WorldMacroPalanquinSeat>();vfx=root==null?null:root.GetComponent<MagicStoneCarDriveVfx>();
            if(car==null||seat==null||vfx==null||!vfx.isActiveAndEnabled||vfx.Profile==null||!car.IsConfigured||!car.isActiveAndEnabled||seat.Occupied||car.DriverPresent||car.Body.isKinematic||car.Speed>.15f)
                throw new InvalidOperationException("New configured unoccupied parked dynamic car with enabled DriveVfx required; no existing input owner is displaced.");
            if(seat.ViewCamera==null||car.BodyVisualRoot.Find("FrontCoreSocket")==null)throw new InvalidOperationException("Actual camera and FrontCoreSocket required.");
            if(root.GetComponent<MagicStoneCarWallObserver>()!=null||GameObject.Find("MagicStoneDiagnosticWalls")!=null||GameObject.Find("MagicStoneIgnitionWalls")!=null)
                throw new InvalidOperationException("Another vehicle diagnostic or leftover fixture exists; finish its cleanup first.");
            // Complete shader preparation before starting the real timed ignition, not midway through its short pattern stage.
            var variants=MagicStoneCarReviewTools.PrepareCaptureVariants(root);
            position=car.Body.position;rotation=car.Body.rotation;velocity=car.Body.linearVelocity;angularVelocity=car.Body.angularVelocity;
            wasSleeping=car.Body.IsSleeping();seatWasEnabled=seat.enabled;driverWasPresent=car.DriverPresent;
            initialActivations=vfx.ActivationCount;initialPatterns=vfx.PatternStartCount;
            report=new Report{utc=DateTime.UtcNow.ToString("o"),status="RUNNING",originalPosition=position,commitBefore=commit,variants=variants};
            running=true;
            try
            {
                float front=float.NegativeInfinity,rear=float.PositiveInfinity;
                foreach(var box in car.GetComponentsInChildren<BoxCollider>())
                {
                    if(!box.enabled||box.isTrigger)continue;
                    for(int i=0;i<8;i++)
                    {
                        var p=box.center+Vector3.Scale(box.size*.5f,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                        float z=car.transform.InverseTransformPoint(box.transform.TransformPoint(p)).z;front=Mathf.Max(front,z);rear=Mathf.Min(rear,z);
                    }
                }
                if(!float.IsFinite(front)||!float.IsFinite(rear))throw new InvalidOperationException("Actual solid hull bounds are missing.");
                fixtures=new GameObject("MagicStoneIgnitionWalls"){hideFlags=HideFlags.HideAndDontSave};
                for(int i=0;i<2;i++)
                {
                    var wall=new GameObject(i==0?"MagicStoneDiagnosticWall_Front":"MagicStoneDiagnosticWall_Rear");wall.transform.SetParent(fixtures.transform);
                    wall.transform.SetPositionAndRotation(car.transform.TransformPoint(new Vector3(0,1.8f,i==0?front+.075f:rear-.075f)),car.transform.rotation);
                    wall.AddComponent<BoxCollider>().size=new Vector3(3.1f,4.2f,.1f);
                }
                observer=car.gameObject.AddComponent<MagicStoneCarWallObserver>();
                if(observer==null||!observer.isActiveAndEnabled)throw new InvalidOperationException("Runtime contact observer could not be attached; no departure started.");
                seat.enabled=false;
                car.SetDriverPresent(true);car.SetDriverInput(0,0,true);
                startGame=lastGame=Time.time;deadline=EditorApplication.timeSinceStartup+35;
                Physics.SyncTransforms();EditorApplication.update+=Tick;
                return "RUNNING: actual parked-car torque departure; one engine still during PATTERN, then restore. Max 0.65m planar displacement; no manual VFX playback.";
            }
            catch(Exception e){Finish("ERROR",e.ToString());return Poll();}
        }
        public static string Begin()=>BeginIgnitionCapture();
        public static string Poll()
        {
            if(running)return "RUNNING "+(Time.time-startGame).ToString("F2")+"s; actual phase="+(vfx==null?"missing":vfx.Phase);
            string path=Output+"/ignition.json";if(!File.Exists(path))return "NOT_RUN";
            var data=JsonUtility.FromJson<Report>(File.ReadAllText(path));return data.status+"; image="+data.imageWritten+"; phase="+data.phase+"; age="+data.activationAge.ToString("F3")+"s; restored="+data.restored+"; "+path;
        }
        public static string End(){if(running)Finish("ABORTED","Stopped explicitly before capture.");return Poll();}
        static void Tick()
        {
            if(!running)return;
            try
            {
                if(car==null||seat==null||vfx==null||!EditorApplication.isPlaying){Finish("ABORTED","Live diagnostic targets unavailable.");return;}
                if(observer==null||!observer.isActiveAndEnabled||fixtures==null){Finish("ABORTED","Runtime contact observer or stop wall disappeared before capture.");return;}
                if(EditorApplication.isPaused||Time.timeScale<=0){Finish("ABORTED","Pause interrupted the diagnostic; no staged image was produced.");return;}
                if(EditorApplication.timeSinceStartup>deadline){Finish("ABORTED","Wall-clock deadline reached.");return;}
                string other=OtherDiagnostic();if(other!=null){Finish("ABORTED","Overlapping "+other+" detected.");return;}
                if(seat.Occupied||seat.enabled!=false||!car.DriverPresent){Finish("ABORTED","Vehicle input ownership changed.");return;}
                float elapsed=Time.time-startGame;if(Time.time<=lastGame)return;lastGame=Time.time;
                report.gameSeconds=elapsed;report.maximumDisplacementM=Mathf.Max(report.maximumDisplacementM,Vector3.ProjectOnPlane(car.Body.position-position,Vector3.up).magnitude);
                if(!Finite(car.Body.position)||!Finite(car.Body.linearVelocity)||report.maximumDisplacementM>.65f||Mathf.Abs(car.Body.position.y-position.y)>1.25f||Vector3.Dot(car.transform.up,Vector3.up)<.75f)
                {Finish("FINDINGS","Vehicle exceeded the finite/0.65m displacement/height/tilt envelope before capture.");return;}
                if(Prologue.PrologueAudit.CommitRatio()>=.85f){Finish("STOPPED_MEMORY","System commit >=85%; no new render started.");return;}
                report.activationDelta=vfx.ActivationCount-initialActivations;report.patternStartDelta=vfx.PatternStartCount-initialPatterns;
                if(report.activationDelta>1){Finish("FINDINGS","More than one real ignition occurred; no repeated captures allowed.");return;}
                float patternEnd=vfx.Profile.IgnitionSeconds+vfx.Profile.PatternSeconds;
                float earliest=vfx.Profile.IgnitionSeconds+Mathf.Min(.1f,vfx.Profile.PatternSeconds*.25f);
                if(report.activationDelta==1&&report.patternStartDelta==1&&vfx.Phase=="PATTERN"&&vfx.ActivationAge>=earliest&&vfx.ActivationAge<patternEnd&&vfx.LiveParticles>0&&Mathf.Abs(car.AppliedMotorTorque)>=vfx.Profile.MinimumMotorTorque)
                {
                    report.phase=vfx.Phase;report.activationAge=vfx.ActivationAge;report.appliedTorque=car.AppliedMotorTorque;report.speed=car.Speed;report.particles=vfx.LiveParticles;report.captureVehiclePosition=car.Body.position;
                    CaptureActualPattern();Finish("CAPTURED_DIAGNOSTIC","One still from actual motor-torque activation during PATTERN; vehicle and ownership restored.");return;
                }
                if(report.activationDelta==1&&vfx.ActivationAge>=patternEnd){Finish("MISSED_PATTERN","No renderable PATTERN frame was observed. VFX was not replayed or paused to fabricate one.");return;}
                if(elapsed>5){Finish("FINDINGS","No torque-triggered visible pattern within five game seconds.");return;}
                if(elapsed<1.2f)car.SetDriverInput(0,0,true);else car.SetDriverInput(1,0,false);
            }
            catch(Exception e){Finish("ERROR",e.ToString());}
        }
        static void CaptureActualPattern()
        {
            report.commitAtRender=Prologue.PrologueAudit.CommitRatio();if(report.commitAtRender>=.85f)throw new InvalidOperationException("System commit >=85% immediately before rendering.");
            var source=seat.ViewCamera;Vector3 target=car.BodyVisualRoot.Find("FrontCoreSocket").position;
            Vector3 cameraPosition=target+car.transform.TransformDirection(new Vector3(1.65f,.7f,2.2f));
            report.cameraPosition=cameraPosition;report.cameraTarget=target;
            GameObject go=null;Camera camera=null;RenderTexture rt=null;Texture2D image=null;Material skyCopy=null;
            var active=RenderTexture.active;var sky=RenderSettings.skybox;bool asyncBefore=ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;if(sky!=null)skyCopy=new Material(sky){hideFlags=HideFlags.HideAndDontSave};
                go=new GameObject("Temporary_MagicStoneIgnitionStill"){hideFlags=HideFlags.HideAndDontSave};camera=go.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
                camera.aspect=1920f/1080;camera.useOcclusionCulling=false;camera.layerCullDistances=new float[32];camera.transform.SetPositionAndRotation(cameraPosition,Quaternion.LookRotation(target-cameraPosition,Vector3.up));
                var data=source.GetComponent<UniversalAdditionalCameraData>();if(data!=null)EditorUtility.CopySerialized(data,camera.GetUniversalAdditionalCameraData());
                Object.FindFirstObjectByType<WorldLookDriver>()?.PreviewRegionalSky(cameraPosition);
                if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85% before render target allocation.");
                rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);image=new Texture2D(1920,1080,TextureFormat.RGB24,false);camera.targetTexture=rt;
                camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply(false);
                Directory.CreateDirectory(Output);File.WriteAllBytes(Output+"/ignition.png",image.EncodeToPNG());report.imageWritten=true;report.file="ignition.png";
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation=asyncBefore;if(camera!=null)camera.targetTexture=null;RenderTexture.active=active;
                if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(image!=null)Object.DestroyImmediate(image);if(go!=null)Object.DestroyImmediate(go);
                if(sky!=null&&skyCopy!=null)sky.CopyPropertiesFromMaterial(skyCopy);RenderSettings.skybox=sky;if(skyCopy!=null)Object.DestroyImmediate(skyCopy);
            }
        }
        static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
        static void Finish(string status,string detail)
        {
            if(!running)return;running=false;EditorApplication.update-=Tick;report.status=status;report.detail=detail;
            report.frontContacts=observer==null?0:observer.FrontContacts;report.rearContacts=observer==null?0:observer.RearContacts;
            bool cleanupError=false;
            void Clean(Action action){try{action();}catch(Exception e){cleanupError=true;report.detail+="\nCleanup: "+e;}}
            try
            {
                Clean(()=>{if(car!=null)
                {
                    car.SetDriverPresent(driverWasPresent);car.SetDriverInput(0,0,true);if(vfx!=null)vfx.ClearPresentation();
                    car.Body.position=position;car.Body.rotation=rotation;car.Body.linearVelocity=velocity;car.Body.angularVelocity=angularVelocity;car.transform.SetPositionAndRotation(position,rotation);
                    foreach(var wheel in car.Wheels){wheel.Collider.motorTorque=0;wheel.Collider.brakeTorque=car.Profile.ParkingBrakeTorquePerWheel;}
                    if(wasSleeping)car.Body.Sleep();
                    report.positionRestoreErrorM=Vector3.Distance(car.Body.position,position);report.rotationRestoreErrorDeg=Quaternion.Angle(car.Body.rotation,rotation);report.velocityRestoreErrorMps=Vector3.Distance(car.Body.linearVelocity,velocity);report.angularRestoreError=Vector3.Distance(car.Body.angularVelocity,angularVelocity);
                    report.driverRestored=car.DriverPresent==driverWasPresent;
                }});
                Clean(()=>{if(observer!=null)Object.DestroyImmediate(observer);observer=null;});
                Clean(()=>{if(fixtures!=null)Object.DestroyImmediate(fixtures);fixtures=null;});
                Clean(()=>Physics.SyncTransforms());
                Clean(()=>{if(seat!=null){seat.enabled=seatWasEnabled;report.seatOwnerRestored=seat.enabled==seatWasEnabled&&!seat.Occupied;}});
                report.fixturesRemoved=fixtures==null&&observer==null;report.particlesCleared=vfx==null||vfx.LiveParticles==0&&vfx.Glow==0;
                report.restored=!cleanupError&&car!=null&&report.driverRestored&&report.seatOwnerRestored&&report.fixturesRemoved&&report.particlesCleared&&report.positionRestoreErrorM<.001f&&report.rotationRestoreErrorDeg<.01f&&report.velocityRestoreErrorMps<.001f&&report.angularRestoreError<.001f;
                if(report.imageWritten&&!report.restored)report.status="CAPTURED_RESTORE_FINDINGS";
                if(cleanupError)report.status="CLEANUP_ERROR";
            }
            catch(Exception e){report.status="CLEANUP_ERROR";report.detail+="\n"+e;}
            finally
            {
                report.commitAfter=Prologue.PrologueAudit.CommitRatio();Directory.CreateDirectory(Output);File.WriteAllText(Output+"/ignition.json",JsonUtility.ToJson(report,true));car=null;seat=null;vfx=null;
            }
        }
    }
}
