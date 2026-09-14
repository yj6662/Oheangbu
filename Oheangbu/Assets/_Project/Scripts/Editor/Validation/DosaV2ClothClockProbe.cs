using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // Native clock measurement, independent of the character, acceptance or settings.
    // Uniform unpinned translation removes spring/collision behavior from the test.
    public static class DosaV2ClothClockProbe
    {
        private struct ProbeStage { }
        [Serializable] private sealed class Sample { public int frame; public double fixedTime; public float delta,unscaled,fixedDelta,scale,meanY,minY,maxY; }
        [Serializable] private sealed class Condition
        { public string id; public float capture,scale,fixedStep; public List<Sample> samples=new List<Sample>(); public float inferredStepFromAcceleration, maximumSpreadY, actualMaxDistance, actualBackstop; public double accelerationFromFixedClock; public int repeatedFixedClockFrames; }
        [Serializable] private sealed class Report
        { public string status,error,path,unity; public bool restored; public List<Condition> conditions=new List<Condition>();
          public string scope="Unpinned identity-skinned 5x5 flat patch, constant external acceleration -9.81m/s2, zero damping/gravity/wind/collisions. Capture centroid after actual ClothFinish. Use distinct fixedTime samples and divided differences to measure acceleration; repeated render frames do not advance the solver. Retain render-frame second differences only as diagnostic evidence. No character, production motions or acceptance change; time/global state restored."; }
        private static Session _active; private static Report _last;
        public static string Begin()
        {
            if(_active!=null)return Status();
            if(!Application.isPlaying||EditorApplication.isPaused||SceneManager.GetActiveScene().name!="C2_PlayerV2Validation"||DosaV2ClothDiagnostics.IsRunning||DosaV2DrawingStaticValidation.IsRunning)
                return "WAIT: idle unpaused isolated review Play required.";
            _active=new Session();try{_active.Start();}catch(Exception e){_active.Finish("FAIL",e.ToString());}return Status();
        }
        public static string Status()
        {var r=_active?.Result??_last;return r==null?"NOT_STARTED":JsonUtility.ToJson(new StatusView{status=r.status,error=r.error,path=r.path,conditions=r.conditions.Count,restored=r.restored});}
        [Serializable] private sealed class StatusView {public string status,error,path;public int conditions;public bool restored;}
        public static string Stop(){_active?.Finish("STOPPED",null);return Status();}
        private sealed class Session
        {
            public readonly Report Result=new Report();
            private readonly Dictionary<Cloth,bool> _review=new Dictionary<Cloth,bool>();
            private Scene _scene;private GameObject _root;private Mesh _mesh;private Cloth _cloth;
            private float _capture,_scale,_fixed;private int _condition,_spawnFrame,_lastFrame=-1;private bool _saved,_finished;
            private Condition Current=>Result.conditions[_condition];
            public void Start()
            {
                _capture=Time.captureDeltaTime;_scale=Time.timeScale;_fixed=Time.fixedDeltaTime;_saved=true;
                Result.path=Path.GetFullPath(Path.Combine(Application.dataPath,"../Screenshots/PlayerDosaV2/cloth-clock-probe.json"));Result.unity=Application.unityVersion;
                foreach(var c in Object.FindObjectsByType<Cloth>(FindObjectsInactive.Include,FindObjectsSortMode.None)){_review[c]=c.enabled;c.enabled=false;}
                foreach(int hz in new[]{30,60,120,240})Result.conditions.Add(new Condition{id="capture"+hz,capture=1f/hz,scale=1f,fixedStep=_fixed});
                Result.conditions.Add(new Condition{id="capture60_scaleQuarter",capture=1f/60,scale=.25f,fixedStep=_fixed});
                Result.conditions.Add(new Condition{id="capture120_fixed120",capture=1f/120,scale=1f,fixedStep=1f/120});
                Result.conditions.Add(new Condition{id="capture240_fixed240",capture=1f/240,scale=1f,fixedStep=1f/240});
                _scene=SceneManager.CreateScene("DisposableNativeClockProbe");
                var loop=PlayerLoop.GetCurrentPlayerLoop();Remove(ref loop);if(!Insert(ref loop))throw new InvalidOperationException("ClothFinish stage unavailable.");PlayerLoop.SetPlayerLoop(loop);
                EditorApplication.playModeStateChanged+=PlayChanged;AssemblyReloadEvents.beforeAssemblyReload+=Reload;Result.status="RUNNING";Spawn();Save();
            }
            private void Spawn()
            {
                Time.captureDeltaTime=Current.capture;Time.timeScale=Current.scale;Time.fixedDeltaTime=Current.fixedStep;
                _root=new GameObject("FreeFallingCloth_"+Current.id);SceneManager.MoveGameObjectToScene(_root,_scene);
                var vertices=new List<Vector3>();var triangles=new List<int>();
                for(int z=0;z<5;z++)for(int x=0;x<5;x++)vertices.Add(new Vector3((x-2)*.025f,0f,(z-2)*.025f));
                for(int z=0;z<4;z++)for(int x=0;x<4;x++){int a=z*5+x;triangles.AddRange(new[]{a,a+5,a+6,a,a+6,a+1});}
                _mesh=new Mesh{name="ClockProbePatch"};_mesh.SetVertices(vertices);_mesh.SetTriangles(triangles,0);_mesh.bindposes=new[]{Matrix4x4.identity};
                _mesh.boneWeights=Enumerable.Repeat(new BoneWeight{boneIndex0=0,weight0=1f},25).ToArray();_mesh.RecalculateNormals();_mesh.RecalculateBounds();
                var renderer=_root.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=_mesh;renderer.bones=new[]{_root.transform};renderer.rootBone=_root.transform;renderer.updateWhenOffscreen=true;
                _cloth=_root.AddComponent<Cloth>();_cloth.enabled=false;_cloth.useGravity=false;_cloth.externalAcceleration=new Vector3(0,-9.81f,0);_cloth.randomAcceleration=Vector3.zero;
                _cloth.damping=0;_cloth.sleepThreshold=0;_cloth.useTethers=false;_cloth.worldAccelerationScale=0;_cloth.worldVelocityScale=0;
                _cloth.stretchingStiffness=1;_cloth.bendingStiffness=0;_cloth.clothSolverFrequency=480;
                // Surface penetration 0 is an active per-particle backstop at the
                // original skinned surface; it blocks this downward free-fall.
                // Match the 100m travel radius to keep this clock probe unconstrained.
                _cloth.coefficients=Enumerable.Repeat(new ClothSkinningCoefficient{maxDistance=100f,collisionSphereDistance=100f},_cloth.vertices.Length).ToArray();
                _spawnFrame=Time.frameCount;_lastFrame=-1;
            }
            private void Measure()
            {
                if(_finished||Time.frameCount==_lastFrame)return;_lastFrame=Time.frameCount;
                try
                {
                    if(Time.frameCount-_spawnFrame<3)return;
                    if(!_cloth.enabled){_cloth.ClearTransformMotion();_cloth.enabled=true;return;}
                    Vector3[] p=_cloth.vertices;if(p.Length!=25||p.Any(v=>!float.IsFinite(v.y)))throw new InvalidOperationException("Invalid native particle array.");
                    var coefficients=_cloth.coefficients;
                    if(coefficients.Length!=25||coefficients.Any(c=>c.maxDistance!=100f||c.collisionSphereDistance!=100f))throw new InvalidOperationException("Free fall travel/backstop settings did not persist.");
                    Current.actualMaxDistance=coefficients[0].maxDistance;Current.actualBackstop=coefficients[0].collisionSphereDistance;
                    var sample=new Sample{frame=Time.frameCount,fixedTime=Time.fixedTimeAsDouble,delta=Time.deltaTime,unscaled=Time.unscaledDeltaTime,fixedDelta=Time.fixedDeltaTime,scale=Time.timeScale,meanY=p.Average(v=>v.y),minY=p.Min(v=>v.y),maxY=p.Max(v=>v.y)};
                    Current.samples.Add(sample);Current.maximumSpreadY=Mathf.Max(Current.maximumSpreadY,sample.maxY-sample.minY);
                    if(Current.samples.Count<24)return;
                    if(Mathf.Abs(Current.samples[23].meanY-Current.samples[5].meanY)<.001f)throw new InvalidOperationException("No free translation observed; cannot infer a native clock from a constrained patch.");
                    var second=new List<float>();for(int i=5;i<Current.samples.Count-1;i++)second.Add(-(Current.samples[i+1].meanY-2*Current.samples[i].meanY+Current.samples[i-1].meanY)/9.81f);
                    second.Sort();Current.inferredStepFromAcceleration=Mathf.Sqrt(Mathf.Max(0,second[second.Count/2]));Save();
                    var fixedSamples=Current.samples.Skip(5).GroupBy(s=>s.fixedTime).Select(g=>g.Last()).ToArray();
                    Current.repeatedFixedClockFrames=Current.samples.Count-Current.samples.Select(s=>s.fixedTime).Distinct().Count();
                    var accelerations=new List<double>();
                    for(int i=2;i<fixedSamples.Length;i++)
                    {var a=fixedSamples[i-2];var b=fixedSamples[i-1];var c=fixedSamples[i];double d1=b.fixedTime-a.fixedTime,d2=c.fixedTime-b.fixedTime;accelerations.Add(2d*((c.meanY-b.meanY)/d2-(b.meanY-a.meanY)/d1)/(d1+d2));}
                    accelerations.Sort();if(accelerations.Count<2)throw new InvalidOperationException("Insufficient actual fixed steps for clock validation.");
                    Current.accelerationFromFixedClock=accelerations[accelerations.Count/2];Save();
                    Object.DestroyImmediate(_root);Object.DestroyImmediate(_mesh);_root=null;_mesh=null;_condition++;
                    if(_condition==Result.conditions.Count)Finish("COMPLETE_MEASURED",null);else Spawn();
                }
                catch(Exception e){Finish("FAIL",e.ToString());}
            }
            private bool Insert(ref PlayerLoopSystem loop)
            {
                if(loop.subSystemList==null)return false;var list=loop.subSystemList.ToList();
                for(int i=0;i<list.Count;i++){if(list[i].type==typeof(UnityEngine.PlayerLoop.PostLateUpdate.PhysicsSkinnedClothFinishUpdate)){list.Insert(i+1,new PlayerLoopSystem{type=typeof(ProbeStage),updateDelegate=Measure});loop.subSystemList=list.ToArray();return true;}var child=list[i];if(Insert(ref child)){list[i]=child;loop.subSystemList=list.ToArray();return true;}}
                return false;
            }
            private static void Remove(ref PlayerLoopSystem loop)
            {if(loop.subSystemList==null)return;var list=loop.subSystemList.Where(l=>l.type!=typeof(ProbeStage)).ToArray();for(int i=0;i<list.Length;i++)Remove(ref list[i]);loop.subSystemList=list;}
            private void PlayChanged(PlayModeStateChange s){if(s==PlayModeStateChange.ExitingPlayMode)Finish("STOPPED","Play exit");}
            private void Reload()=>Finish("STOPPED","Assembly reload");
            public void Finish(string status,string error)
            {
                if(_finished)return;_finished=true;Result.error=error;
                var loop=PlayerLoop.GetCurrentPlayerLoop();Remove(ref loop);PlayerLoop.SetPlayerLoop(loop);EditorApplication.playModeStateChanged-=PlayChanged;AssemblyReloadEvents.beforeAssemblyReload-=Reload;
                if(_root!=null)Object.DestroyImmediate(_root);if(_mesh!=null)Object.DestroyImmediate(_mesh);
                foreach(var pair in _review)if(pair.Key!=null)pair.Key.enabled=pair.Value;
                if(_saved){Time.captureDeltaTime=_capture;Time.timeScale=_scale;RestoreFixedStep(_fixed);}
                if(_scene.IsValid()&&_scene.isLoaded){var op=SceneManager.UnloadSceneAsync(_scene);if(op!=null){Result.status="RESTORING";Save();op.completed+=_=>Complete(status);return;}}
                Complete(status);
            }
            private void Complete(string status){Result.status=status;Result.restored=_saved&&Time.captureDeltaTime==_capture&&Time.timeScale==_scale&&Time.fixedDeltaTime==_fixed&&_review.All(p=>p.Key==null||p.Key.enabled==p.Value);_last=Result;_active=null;Save();}
            private static void RestoreFixedStep(float target)
            {
                // Unity converts setter input to a rational native clock. Feeding
                // a cached getter back directly can round down again. Find the
                // adjacent representable setter input whose getter equals it.
                float candidate=target;
                for(int i=0;i<32;i++)
                {
                    Time.fixedDeltaTime=candidate;
                    float actual=Time.fixedDeltaTime;
                    if(actual==target)return;
                    int bits=BitConverter.SingleToInt32Bits(candidate);
                    candidate=BitConverter.Int32BitsToSingle(bits+(actual<target?1:-1));
                }
            }
            private void Save(){if(string.IsNullOrEmpty(Result.path))return;Directory.CreateDirectory(Path.GetDirectoryName(Result.path));File.WriteAllText(Result.path,JsonUtility.ToJson(Result,true));}
        }
    }
}
