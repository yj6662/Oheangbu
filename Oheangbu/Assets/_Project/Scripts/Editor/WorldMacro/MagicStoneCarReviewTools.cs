using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Explicit public-API diagnostics and sequential stills, never synthesized keys or automatic player walking.</summary>
    public static class MagicStoneCarReviewTools
    {
        [Serializable] public sealed class State
        {
            public string utc,action,scope="Explicit public API call, not keyboard input or full route traversal.",phase;
            public bool result,occupied,seatedView,playerShoulder,reviewEnabled,walkEnabled,finite;
            public int mode,activations,particles;public float speed,torque,glow;
            public Vector3 camera,feetBefore,feetAfter,seat;public Quaternion cameraRotation;
        }
        [Serializable] sealed class Trace {public State[] events;}
        [Serializable] public sealed class VariantPass
        {public string material,pass;public bool compiledBefore,compiledAfter;}
        [Serializable] public sealed class VariantReadiness
        {public bool anyCompilingBefore,anyCompilingAfter;public int checkedPasses,compiledNow;public VariantPass[] passes;}
        [Serializable] sealed class CaptureInfo
        {
            public string utc,view,file,scope;public int width=1920,height=1080;public float commitBefore,commitAfter;
            public Vector3 position,target;public bool playing,seatedView,occupied;public string enginePhase;
            public VariantReadiness variants;
        }
        static string Output=>MagicStoneCarAuthoring.Output;
        static WorldMacroPalanquinSeat Seat()
        {var root=GameObject.Find(WorldMacroPalanquinAuthoring.RootName);var seat=root==null?null:root.GetComponent<WorldMacroPalanquinSeat>();if(seat==null||seat.GetComponent<MagicStoneCarDriveVfx>()==null)throw new InvalidOperationException("Install the new MagicStoneCar version first.");return seat;}
        public static string CameraAction(string action)
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play mode required. This helper does not enter Play or place the walking capsule.");
            var s=Seat();var before=s.ReviewController.WalkBody.transform.position;bool result;
            if(action=="board")result=s.TryBoard();else if(action=="exit")result=s.TryExit();else if(action=="toggle")result=s.ToggleView();else if(action=="snapshot")result=true;else throw new ArgumentException("board, exit, toggle or snapshot expected.");
            var state=ReadState(s,action,result,before);string path=Output+"/camera_api.json";var trace=File.Exists(path)?JsonUtility.FromJson<Trace>(File.ReadAllText(path)):new Trace();
            var list=new List<State>(trace?.events??Array.Empty<State>());list.Add(state);Directory.CreateDirectory(Output);File.WriteAllText(path,JsonUtility.ToJson(new Trace{events=list.ToArray()},true));
            return action+": "+result+"; occupied="+state.occupied+"; seated="+state.seatedView+"; playerShoulder="+state.playerShoulder+"; finite="+state.finite+"; mode="+state.mode+"; "+s.LastInteraction+"; record="+path;
        }
        public static string Snapshot()
        {
            var s=Seat();var state=ReadState(s,"snapshot",true,s.ReviewController.WalkBody.transform.position);
            Directory.CreateDirectory(Output);File.WriteAllText(Output+"/live_snapshot.json",JsonUtility.ToJson(state,true));
            return "phase="+state.phase+"; speed="+state.speed.ToString("F3")+"; torque="+state.torque.ToString("F1")+"; activations="+state.activations+"; particles="+state.particles+"; glow="+state.glow.ToString("F3")+"; finite="+state.finite;
        }
        static State ReadState(WorldMacroPalanquinSeat s,string action,bool result,Vector3 before)
        {
            var v=s.GetComponent<MagicStoneCarDriveVfx>();var r=s.ReviewController;var c=s.ViewCamera;
            var state=new State{utc=DateTime.UtcNow.ToString("o"),action=action,result=result,occupied=s.Occupied,seatedView=s.SeatedView,playerShoulder=r.PlayerShoulderView,reviewEnabled=r.enabled,walkEnabled=r.WalkBody.enabled,mode=r.Mode,camera=c.transform.position,cameraRotation=c.transform.rotation,feetBefore=before,feetAfter=r.WalkBody.transform.position,seat=s.SeatSocket.position,speed=s.Vehicle.Speed,torque=s.Vehicle.AppliedMotorTorque,phase=v.Phase,activations=v.ActivationCount,particles=v.LiveParticles,glow=v.Glow};
            state.finite=Finite(state.camera)&&Finite(state.feetAfter)&&float.IsFinite(state.speed)&&float.IsFinite(state.glow);return state;
        }
        static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
        public static VariantReadiness PrepareCaptureVariants(GameObject root)
        {
            if(root==null)throw new ArgumentNullException(nameof(root));
            var result=new VariantReadiness{anyCompilingBefore=ShaderUtil.anythingCompiling};
            var rows=new List<VariantPass>();bool asyncBefore=ShaderUtil.allowAsyncCompilation;
            var names=new[]{"ForwardLit","GBuffer","ShadowCaster","DepthOnly","DepthNormals","DepthNormalsOnly"};
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                foreach(var material in root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct())
                foreach(string passName in names)
                {
                    int pass=material.FindPass(passName);if(pass<0||!material.GetShaderPassEnabled(passName))continue;
                    if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Stopped shader preparation: system commit >=85%.");
                    var row=new VariantPass{material=material.name,pass=passName,compiledBefore=ShaderUtil.IsPassCompiled(material,pass)};
                    // Disallowing new async work alone does not complete a variant already requested by another camera.
                    if(!row.compiledBefore){ShaderUtil.CompilePass(material,pass,true);result.compiledNow++;}
                    row.compiledAfter=ShaderUtil.IsPassCompiled(material,pass);rows.Add(row);
                    if(!row.compiledAfter)throw new InvalidOperationException("Shader variant not ready after synchronous compile: "+material.name+" / "+passName);
                }
                result.checkedPasses=rows.Count;result.passes=rows.ToArray();result.anyCompilingAfter=ShaderUtil.anythingCompiling;
                if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Stopped after shader preparation: system commit >=85%.");
                return result;
            }
            finally{ShaderUtil.allowAsyncCompilation=asyncBefore;}
        }
        public static string Capture(string view)
        {
            if(SceneManager.GetActiveScene().path!=WorldMacroBuilder.ScenePath)throw new InvalidOperationException("Existing macro scene required.");
            float commit=Prologue.PrologueAudit.CommitRatio();if(commit>=.85f)throw new InvalidOperationException("Stopped before new capture: system commit >=85%.");
            var s=Seat();var source=s.ViewCamera;if(source==null)throw new InvalidOperationException("Scene camera missing.");var car=s.Vehicle;var profile=car.Profile;
            Vector3 position,target;
            if(view=="seated"){position=s.SeatSocket.position;target=position+s.SeatSocket.forward*15;}
            else if(view=="vehicle_shoulder")
            {target=s.ExternalLookSocket.position+Vector3.up*profile.ExternalPivotHeight;position=target+Quaternion.Euler(0,car.transform.eulerAngles.y,0)*new Vector3(profile.ExternalShoulderOffset,0,-profile.ExternalDistance);target+=car.transform.forward*2;}
            else if(view=="exterior"){target=car.transform.position+Vector3.up*1.75f;position=target+car.transform.TransformDirection(new Vector3(5.8f,2.4f,7.5f));}
            else if(view=="engine"){target=car.BodyVisualRoot.Find("FrontCoreSocket").position;position=target+car.transform.TransformDirection(new Vector3(1.65f,.7f,2.2f));}
            else if(view=="player")
            {var r=s.ReviewController;target=r.WalkBody.transform.position+Vector3.up*r.ShoulderOffset.y;var rotation=Quaternion.Euler(0,r.WalkBody.transform.eulerAngles.y,0);position=target+rotation*new Vector3(r.ShoulderOffset.x,0,r.ShoulderOffset.z);target+=rotation*Vector3.forward*10;}
            else if(view=="live"){position=source.transform.position;target=position+source.transform.forward*15;}
            else throw new ArgumentException("seated, vehicle_shoulder, exterior, engine, player or live expected.");
            if(view=="player"&&s.Occupied)throw new InvalidOperationException("Exit before capturing player shoulder composition.");
            var vfx=car.GetComponent<MagicStoneCarDriveVfx>();var info=new CaptureInfo{utc=DateTime.UtcNow.ToString("o"),view=view,file=view+".png",position=position,target=target,commitBefore=commit,playing=EditorApplication.isPlaying,occupied=s.Occupied,seatedView=s.SeatedView,enginePhase=vfx.Phase,scope=view=="live"?"Actual current camera pose/state still; no input or movement simulated.":"Composed static camera at actual model sockets; not a live boarding or camera collision test."};
            GameObject go=null;Camera camera=null;RenderTexture rt=null;Texture2D image=null;Material skyCopy=null;var active=RenderTexture.active;var sky=RenderSettings.skybox;bool asyncBefore=ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;info.variants=PrepareCaptureVariants(car.gameObject);if(sky!=null)skyCopy=new Material(sky){hideFlags=HideFlags.HideAndDontSave};
                go=new GameObject("Temporary_MagicStoneCarStill"){hideFlags=HideFlags.HideAndDontSave};camera=go.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;camera.aspect=1920f/1080;camera.useOcclusionCulling=false;camera.layerCullDistances=new float[32];camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position,Vector3.up));
                var data=source.GetComponent<UniversalAdditionalCameraData>();if(data!=null)EditorUtility.CopySerialized(data,camera.GetUniversalAdditionalCameraData());
                Object.FindFirstObjectByType<WorldLookDriver>()?.PreviewRegionalSky(position);
                if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Stopped before render: system commit >=85%.");
                rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);image=new Texture2D(1920,1080,TextureFormat.RGB24,false);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply(false);Directory.CreateDirectory(Output);File.WriteAllBytes(Output+"/"+info.file,image.EncodeToPNG());
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation=asyncBefore;if(camera!=null)camera.targetTexture=null;RenderTexture.active=active;if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(image!=null)Object.DestroyImmediate(image);if(go!=null)Object.DestroyImmediate(go);
                if(sky!=null&&skyCopy!=null)sky.CopyPropertiesFromMaterial(skyCopy);RenderSettings.skybox=sky;if(skyCopy!=null)Object.DestroyImmediate(skyCopy);
            }
            info.commitAfter=Prologue.PrologueAudit.CommitRatio();File.WriteAllText(Output+"/"+view+".json",JsonUtility.ToJson(info,true));return view+" 1920x1080 still: "+Output+"/"+info.file;
        }
    }
}
