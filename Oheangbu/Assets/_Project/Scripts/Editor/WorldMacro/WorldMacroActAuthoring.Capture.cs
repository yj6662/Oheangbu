using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroActAuthoring
    {
        static string CaptureTerrainView(string id)
        {
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%; capture stopped");
            Vector3 eye,target;
            switch(id)
            {
                case "vehicle_before": case "vehicle_after":
                    var car=Object.FindFirstObjectByType<Oheangbu.App.World.Vehicle.WorldMacroPalanquinController>();
                    target=car.transform.position+Vector3.up*1.65f;eye=target+car.transform.TransformDirection(new Vector3(5,1.8f,6.5f));
                    MagicStoneCarReviewTools.PrepareCaptureVariants(car.gameObject);break;
                case "seam": eye=new Vector3(784,143,258);target=new Vector3(776.4f,135,250.1f);break;
                case "relay": eye=new Vector3(792,142,133);target=new Vector3(812,135,152);break;
                case "guk": eye=new Vector3(879,152,678);target=new Vector3(870,149,693);break;
                case "cave": eye=new Vector3(1882,198,452);target=new Vector3(1889,197,483);break;
                default:throw new ArgumentException("seam/relay/guk/cave");
            }
            var scene=SceneManager.GetActiveScene();bool dirty=scene.isDirty;
            var source=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First(c=>c.GetComponentInParent<Canvas>()==null&&c.GetComponent<UniversalAdditionalCameraData>()?.renderType!=CameraRenderType.Overlay);
            GameObject go=null;RenderTexture rt=null;Texture2D pixels=null;var previous=RenderTexture.active;
            var sky=RenderSettings.skybox;var snapshot=sky==null?null:new Material(sky){hideFlags=HideFlags.HideAndDontSave};
            bool async=ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                go=new GameObject("ActsTerrain_DiagnosticCamera"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
                camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));camera.fieldOfView=60;camera.aspect=16f/9;camera.nearClipPlane=.1f;camera.farClipPlane=4500;camera.orthographic=false;camera.useOcclusionCulling=false;
                int ui=LayerMask.NameToLayer("UI");if(ui>=0)camera.cullingMask&=~(1<<ui);
                var additional=camera.GetUniversalAdditionalCameraData();var sourceAdditional=source.GetComponent<UniversalAdditionalCameraData>();if(sourceAdditional!=null)EditorUtility.CopySerialized(sourceAdditional,additional);additional.renderType=CameraRenderType.Base;additional.cameraStack.Clear();
                Object.FindFirstObjectByType<Oheangbu.App.WorldLookDriver>()?.PreviewRegionalSky(eye);
                rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);rt.Create();camera.targetTexture=rt;
                if(GraphicsSettings.currentRenderPipeline!=null)RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});else camera.Render();
                RenderTexture.active=rt;pixels=new Texture2D(1920,1080,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();
                var colors=pixels.GetPixels32();int nonBlack=colors.Count(c=>c.r+c.g+c.b>12);if(nonBlack<colors.Length/100)throw new InvalidOperationException("Black capture rejected");
                string folder=Path.Combine(Output,"Captures");Directory.CreateDirectory(folder);string file=Path.Combine(folder,id+".png");File.WriteAllBytes(file,pixels.EncodeToPNG());
                return Write("capture_"+id+".json",new Report{status="CAPTURED_EDIT_SCENE",scope="1920x1080 terrain inspection; existing scene materials, no foliage prewarm. Not native gameplay or final art approval.",checks={"image="+file,"eye="+eye,"target="+target}});
            }
            finally
            {
                RenderTexture.active=previous;if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(pixels!=null)Object.DestroyImmediate(pixels);if(go!=null)Object.DestroyImmediate(go);
                if(snapshot!=null){sky.CopyPropertiesFromMaterial(snapshot);Object.DestroyImmediate(snapshot);}RenderSettings.skybox=sky;ShaderUtil.allowAsyncCompilation=async;
                if(scene.isDirty!=dirty)Debug.LogWarning("Diagnostic capture changed scene dirty state; scene was not saved.");
            }
        }
    }
}
