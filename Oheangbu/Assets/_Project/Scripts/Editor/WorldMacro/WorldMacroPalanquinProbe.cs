using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Oheangbu.App.World.Vehicle;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Bounded real-physics diagnostic in Play. Does not synthesize keys or move the walking capsule.</summary>
    [InitializeOnLoad]
    public static class WorldMacroPalanquinProbe
    {
        [Serializable] public sealed class Sample
        {
            public string phase;
            public float elapsedGameSeconds, forwardSpeedMps, speedMps, observedAccelerationMps2, steerLeftDeg, steerRightDeg, displacementM;
            public int groundedWheels;
            public float visualBodyPitchDeg, visualBodyRollDeg;
        }
        [Serializable] public sealed class Check { public string name, status, detail; }
        [Serializable] public sealed class Report
        {
            public string status, detail, utc, scenePath;
            public int schemaVersion;
            public string scope = "Installed WheelCollider vehicle on its actual site in Play; API-owned inputs, not keyboard events. Short settle/accelerate/steer/brake sequence, not full traversal, handling approval or player walking.";
            public float durationGameSeconds, maxDisplacementM, peakSpeedMps, accelerationPhasePeakMps, maxAccelerationSampleMps2, maximumObservedSampleIntervalSeconds, finalBrakeSpeedMps, fourWheelContactSeconds;
            public Vector3 startPosition, endPosition;
            public float[] wheelVisualTravelDegrees = new float[4], wheelPeakRpm = new float[4];
            public bool allFinite = true, restored;
            public float peakVisualBodyTiltDeg, peakVisualBodyPitchDeg, peakVisualBodyRollDeg;
            public bool visualBodyWithinClamps = true;
            public bool magicStoneVfxPresent;
            public int engineActivations;
            public float peakEngineGlow;
            public Check[] checks = Array.Empty<Check>();
            public Sample[] samples = Array.Empty<Sample>();
            public string[] unverified = { "Actual E/WASD/Space/V keyboard interaction", "Seated camera comfort and visibility in motion", "Bridges, full routes, slopes outside the bounded test window", "FPS/performance and save/travel gameplay" };
        }
        const float SettleEnd=2.5f, AccelerateEnd=3.5f, SteerEnd=4.2f, BrakeEnd=6.4f, MaximumDisplacement=6f;
        static string outputOverride;
        static string Output => outputOverride??(WorldMacroBuilder.Output + (GameObject.Find(WorldMacroPalanquinAuthoring.RootName)?.GetComponent<MagicStoneCarDriveVfx>()!=null?"/MagicStoneCar/physics_probe.json":"/Palanquin/physics_probe.json"));
        static WorldMacroPalanquinController car;
        static WorldMacroPalanquinSeat seat;
        static Report report;
        static readonly List<Sample> samples = new List<Sample>();
        static Vector3 originalPosition, originalVelocity, originalAngularVelocity;
        static Quaternion originalRotation;
        static Quaternion[] priorVisualRotations;
        static bool running, seatWasEnabled;
        public static bool IsRunning=>running;
        static double startGame, priorFixedTime, deadline;
        static float priorSpeed, contactRun, maximumContactRun, maxSteer;
        static MagicStoneCarDriveVfx driveVfx;
        static int priorActivations;

        static WorldMacroPalanquinProbe()
        {
            EditorApplication.playModeStateChanged += state => { if(running && state==PlayModeStateChange.ExitingPlayMode)Finish("ABORTED","Play mode exited before completion."); };
            AssemblyReloadEvents.beforeAssemblyReload += () => { if(running)Finish("ABORTED","Assembly reload interrupted the probe."); };
        }
        public static string Begin()
        {
            if(running)throw new InvalidOperationException("A vehicle probe is already running. Poll or End it first.");
            if(!EditorApplication.isPlaying || EditorApplication.isPaused || SceneManager.GetActiveScene().path!=WorldMacroBuilder.ScenePath)
                throw new InvalidOperationException("Installed macro scene must be running and unpaused. This helper does not start Play mode.");
            var root=GameObject.Find(WorldMacroPalanquinAuthoring.RootName);car=root==null?null:root.GetComponent<WorldMacroPalanquinController>();seat=root==null?null:root.GetComponent<WorldMacroPalanquinSeat>();
            if(car==null||!car.IsConfigured||seat==null||seat.Occupied||car.DriverPresent||car.Body.isKinematic)
                throw new InvalidOperationException("A configured unoccupied dynamic palanquin is required; existing input ownership is never stolen.");
            if(car.Speed>.3f)throw new InvalidOperationException("Park the palanquin before this bounded test.");
            if(!Preflight(out string reason))throw new InvalidOperationException("Vehicle probe not started: "+reason);
            originalPosition=car.Body.position;originalRotation=car.Body.rotation;originalVelocity=car.Body.linearVelocity;originalAngularVelocity=car.Body.angularVelocity;
            seatWasEnabled=seat.enabled;seat.enabled=false;car.SetDriverPresent(true);car.SetDriverInput(0,0,true);
            report=new Report{schemaVersion=2,status="RUNNING",utc=DateTime.UtcNow.ToString("o"),scenePath=SceneManager.GetActiveScene().path,startPosition=originalPosition};
            driveVfx=car.GetComponent<MagicStoneCarDriveVfx>();priorActivations=driveVfx==null?0:driveVfx.ActivationCount;report.magicStoneVfxPresent=driveVfx!=null;
            outputOverride=WorldMacroBuilder.Output+(driveVfx!=null?"/MagicStoneCar/physics_probe.json":"/Palanquin/physics_probe.json");
            samples.Clear();priorVisualRotations=car.Wheels.Select(w=>w.Visual.rotation).ToArray();priorSpeed=car.ForwardSpeed;
            startGame=Time.timeAsDouble;priorFixedTime=Time.fixedTimeAsDouble;deadline=EditorApplication.timeSinceStartup+65;
            contactRun=maximumContactRun=maxSteer=0;running=true;EditorApplication.update+=Tick;
            return "RUNNING: 2.5s settle, 1.0s throttle, 0.7s gentle steer, 2.2s brake; maximum 6m displacement. Actual vehicle pose is restored afterward. No capsule movement or keyboard simulation.";
        }
        public static string Poll()
        {
            if(running)return "RUNNING "+(Time.timeAsDouble-startGame).ToString("F2")+"s / "+BrakeEnd+"s; "+samples.Count+" observed physics samples";
            if(!File.Exists(Output))return "NOT_RUN";
            var saved=JsonUtility.FromJson<Report>(File.ReadAllText(Output));var checks=saved.checks??Array.Empty<Check>();
            return saved.status+"; checks "+checks.Count(c=>c.status=="PASS")+"/"+checks.Length+"; travel="+saved.maxDisplacementM.ToString("F2")+"m; peak="+saved.peakSpeedMps.ToString("F2")+"m/s; stopped="+saved.finalBrakeSpeedMps.ToString("F5")+"m/s; body tilt="+(saved.schemaVersion>=2?saved.peakVisualBodyTiltDeg.ToString("F2")+"deg":"not previously measured")+"; restored="+saved.restored+"; raw samples: "+Output;
        }
        public static string End()
        { if(running)Finish("ABORTED","Stopped explicitly; original vehicle pose and seat component ownership restored.");return Poll(); }
        public static string TryBoard()
        {
            if(running)throw new InvalidOperationException("Finish the physics probe before testing the public boarding API.");
            var s=FindSeat();bool ok=s.TryBoard();return (ok?"BOARDED: ":"NOT_BOARDED: ")+s.LastInteraction+" Public API call, not an E-key event; capsule was not repositioned by this helper.";
        }
        public static string TryExit()
        {
            if(running)throw new InvalidOperationException("Finish the physics probe before testing the public exit API.");
            var s=FindSeat();bool ok=s.TryExit();return (ok?"EXITED: ":"NOT_EXITED: ")+s.LastInteraction+" Public API call, not an E-key event.";
        }
        static WorldMacroPalanquinSeat FindSeat()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Public boarding/exit calls require Play mode.");
            var root=GameObject.Find(WorldMacroPalanquinAuthoring.RootName);var s=root==null?null:root.GetComponent<WorldMacroPalanquinSeat>();
            if(s==null)throw new InvalidOperationException("Missing installed vehicle seat.");return s;
        }
        static bool Ground(Vector3 p,out RaycastHit ground)
        {
            ground=default;bool found=false;
            foreach(var hit in Physics.RaycastAll(p+Vector3.up*10,Vector3.down,30,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                if((hit.collider.name.StartsWith("Terrain_")||hit.collider.name.StartsWith("Bridge_"))&&(!found||hit.point.y>ground.point.y)){ground=hit;found=true;}
            return found;
        }
        static bool Preflight(out string reason)
        {
            reason=null;Physics.SyncTransforms();var start=car.transform.position;var facing=Quaternion.Euler(0,car.transform.eulerAngles.y,0);
            // Clear enough space for the short sequence and its braking margin. No artificial test floor.
            for(int z=-1;z<=8;z++)
            {
                float highest=float.NegativeInfinity;
                for(int x=-1;x<=1;x++)
                {
                    var p=start+facing*new Vector3(x*1.75f,0,z);
                    if(!Ground(p,out var hit)||Vector3.Angle(hit.normal,Vector3.up)>12){reason="Missing or >12-degree ground in the 8m staging corridor.";return false;}
                    highest=Mathf.Max(highest,hit.point.y);
                    foreach(var river in WorldMacroBuilder.Sheet.Rivers)for(int i=1;i<river.Points.Length;i++)
                    {
                        float d=WorldMacroTerrain.SegmentDistance(p.x,p.z,river.Points[i-1],river.Points[i],out float t);
                        if(d<Mathf.Max(180,river.Width*3)&&hit.point.y<Mathf.Lerp(river.Points[i-1].y,river.Points[i].y,t)+.5f){reason="River-level buffer intersects the staging corridor.";return false;}
                    }
                }
                var centre=start+facing*new Vector3(0,0,z);centre.y=highest+car.Profile.HullCentre.y;
                foreach(var c in Physics.OverlapBox(centre,new Vector3(1.8f,car.Profile.HullSize.y*.5f,.55f),facing,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                    if(!c.transform.IsChildOf(car.transform)&&!c.name.StartsWith("Terrain_")&&!c.name.StartsWith("Bridge_")){reason="Obstacle in staging corridor: "+c.name;return false;}
            }
            return true;
        }
        static void Tick()
        {
            if(!running)return;
            try
            {
                if(car==null||seat==null||!EditorApplication.isPlaying){Finish("ABORTED","Vehicle or Play scene is no longer available.");return;}
                if(EditorApplication.timeSinceStartup>deadline){Finish("ABORTED","Wall-clock limit reached, possibly paused or severely slowed. No passing result inferred.");return;}
                if(EditorApplication.isPaused || Time.fixedTimeAsDouble<=priorFixedTime)return;
                float elapsed=(float)(Time.timeAsDouble-startGame),dt=(float)(Time.fixedTimeAsDouble-priorFixedTime);priorFixedTime=Time.fixedTimeAsDouble;
                string phase=elapsed<SettleEnd?"settle":elapsed<AccelerateEnd?"accelerate":elapsed<SteerEnd?"steer":"brake";
                float forward=car.ForwardSpeed,acceleration=(forward-priorSpeed)/Mathf.Max(.0001f,dt);priorSpeed=forward;
                var displacement=Vector3.ProjectOnPlane(car.Body.position-originalPosition,Vector3.up).magnitude;
                report.durationGameSeconds=elapsed;report.maximumObservedSampleIntervalSeconds=Mathf.Max(report.maximumObservedSampleIntervalSeconds,dt);
                report.maxDisplacementM=Mathf.Max(report.maxDisplacementM,displacement);report.peakSpeedMps=Mathf.Max(report.peakSpeedMps,car.Speed);
                if(phase=="accelerate"){report.accelerationPhasePeakMps=Mathf.Max(report.accelerationPhasePeakMps,forward);report.maxAccelerationSampleMps2=Mathf.Max(report.maxAccelerationSampleMps2,Mathf.Abs(acceleration));}
                int contacts=0;
                for(int i=0;i<4;i++)
                {
                    var w=car.Wheels[i];if(w.Collider.GetGroundHit(out _))contacts++;
                    report.wheelPeakRpm[i]=Mathf.Max(report.wheelPeakRpm[i],Mathf.Abs(w.Collider.rpm));
                    report.wheelVisualTravelDegrees[i]+=Quaternion.Angle(priorVisualRotations[i],w.Visual.rotation);priorVisualRotations[i]=w.Visual.rotation;
                    report.allFinite &= Finite(w.Visual.position)&&Finite(w.Visual.rotation)&&Finite(w.Collider.rpm)&&Finite(w.Collider.steerAngle);
                }
                if(phase=="settle"){contactRun=contacts==4?contactRun+dt:0;maximumContactRun=Mathf.Max(maximumContactRun,contactRun);report.fourWheelContactSeconds=maximumContactRun;}
                maxSteer=Mathf.Max(maxSteer,Mathf.Abs(car.Wheels[0].Collider.steerAngle),Mathf.Abs(car.Wheels[1].Collider.steerAngle));
                report.allFinite &= Finite(car.Body.position)&&Finite(car.Body.rotation)&&Finite(car.Body.linearVelocity)&&Finite(car.Body.angularVelocity);
                var visualEuler=car.BodyVisualRoot.localEulerAngles;float bodyPitch=Mathf.DeltaAngle(0,visualEuler.x),bodyRoll=Mathf.DeltaAngle(0,visualEuler.z);
                report.allFinite &= Finite(visualEuler);report.visualBodyWithinClamps &= Finite(visualEuler)&&Mathf.Abs(bodyPitch)<=car.Profile.MaximumPitch+.05f&&Mathf.Abs(bodyRoll)<=car.Profile.MaximumRoll+.05f;
                report.peakVisualBodyPitchDeg=Mathf.Max(report.peakVisualBodyPitchDeg,Mathf.Abs(bodyPitch));report.peakVisualBodyRollDeg=Mathf.Max(report.peakVisualBodyRollDeg,Mathf.Abs(bodyRoll));
                report.peakVisualBodyTiltDeg=Mathf.Max(report.peakVisualBodyTiltDeg,Mathf.Sqrt(bodyPitch*bodyPitch+bodyRoll*bodyRoll));
                if(driveVfx!=null){report.engineActivations=driveVfx.ActivationCount-priorActivations;report.peakEngineGlow=Mathf.Max(report.peakEngineGlow,driveVfx.Glow);}
                samples.Add(new Sample{phase=phase,elapsedGameSeconds=elapsed,forwardSpeedMps=forward,speedMps=car.Speed,observedAccelerationMps2=acceleration,steerLeftDeg=car.Wheels[0].Collider.steerAngle,steerRightDeg=car.Wheels[1].Collider.steerAngle,displacementM=displacement,groundedWheels=contacts,visualBodyPitchDeg=bodyPitch,visualBodyRollDeg=bodyRoll});
                if(!report.allFinite){Finish("FINDINGS","Non-finite physics or visual data; test stopped and pose restored.");return;}
                if(displacement>MaximumDisplacement||Mathf.Abs(car.Body.position.y-originalPosition.y)>1.25f||Vector3.Dot(car.transform.up,Vector3.up)<.75f){Finish("FINDINGS","Vehicle left the bounded displacement/height/tilt envelope.");return;}
                if(elapsed>=SettleEnd&&maximumContactRun<.2f){Finish("FINDINGS","Four wheels did not maintain ground contact for 0.2s during settling; no acceleration started.");return;}
                if(elapsed>=BrakeEnd)
                {
                    report.finalBrakeSpeedMps=car.Speed;
                    var checks=new List<Check>();
                    Add("four-wheel settling",maximumContactRun>=.2f,maximumContactRun.ToString("F3")+" consecutive seconds");
                    Add("throttle increased speed",report.accelerationPhasePeakMps>.5f,report.accelerationPhasePeakMps.ToString("F3")+"m/s peak in acceleration window");
                    Add("bounded observed acceleration",report.maxAccelerationSampleMps2<15,report.maxAccelerationSampleMps2.ToString("F3")+"m/s²; coarse observation bound, not comfort approval");
                    Add("front-wheel steering",maxSteer>5,maxSteer.ToString("F2")+" degrees");
                    Add("four visual wheels rotate",Enumerable.Range(0,4).All(i=>report.wheelPeakRpm[i]>1&&report.wheelVisualTravelDegrees[i]>8),"Each wheel: measured rpm and visible transform angle travel");
                    Add("brake brought vehicle to rest",report.finalBrakeSpeedMps<.35f,report.finalBrakeSpeedMps.ToString("F3")+"m/s");
                    Add("finite data",report.allFinite,"Body velocities, position/rotation, wheel transforms/rpm/steer");
                    Add("visual body tilt within profile clamps",report.visualBodyWithinClamps&&Finite(report.peakVisualBodyTiltDeg),"Peak pitch "+report.peakVisualBodyPitchDeg.ToString("F3")+"deg / roll "+report.peakVisualBodyRollDeg.ToString("F3")+"deg; combined "+report.peakVisualBodyTiltDeg.ToString("F3")+"deg. Local Euler values converted to signed angles; not comfort approval.");
                    if(driveVfx!=null)Add("one engine activation during one departure",report.engineActivations==1&&report.peakEngineGlow>.01f,"Actual motor-driven activation count "+report.engineActivations+"; peak glow "+report.peakEngineGlow.ToString("F3")+". Wall rearm/pause are separate checks.");
                    report.checks=checks.ToArray();Finish(checks.All(c=>c.status=="PASS")?"PASS_BOUNDED_VEHICLE_DIAGNOSTIC":"FINDINGS","Short API-driven physics sequence completed. No full-route or keyboard-input claim.");return;
                    void Add(string name,bool pass,string detail)=>checks.Add(new Check{name=name,status=pass?"PASS":"FAIL",detail=detail});
                }
                if(phase=="settle"||phase=="brake")car.SetDriverInput(0,0,true);
                else if(phase=="accelerate")car.SetDriverInput(1,0,false);
                else car.SetDriverInput(.3f,.45f,false);
            }
            catch(Exception e){Finish("ERROR",e.ToString());}
        }
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
        static bool Finite(Quaternion q)=>Finite(q.x)&&Finite(q.y)&&Finite(q.z)&&Finite(q.w);
        static void Finish(string status,string detail)
        {
            if(!running)return;running=false;EditorApplication.update-=Tick;
            report.status=status;report.detail=detail;report.samples=samples.ToArray();report.endPosition=car==null?originalPosition:car.transform.position;
            try
            {
                if(car!=null)
                {
                    car.SetDriverPresent(false);
                    if(driveVfx!=null)driveVfx.ClearPresentation();
                    if(car.Body!=null){car.Body.position=originalPosition;car.Body.rotation=originalRotation;car.Body.linearVelocity=originalVelocity;car.Body.angularVelocity=originalAngularVelocity;car.transform.SetPositionAndRotation(originalPosition,originalRotation);Physics.SyncTransforms();report.restored=true;}
                }
            }
            finally
            {
                if(seat!=null)seat.enabled=seatWasEnabled;
                Directory.CreateDirectory(Path.GetDirectoryName(Output));File.WriteAllText(Output,JsonUtility.ToJson(report,true));
                car=null;seat=null;
            }
        }
    }
}
