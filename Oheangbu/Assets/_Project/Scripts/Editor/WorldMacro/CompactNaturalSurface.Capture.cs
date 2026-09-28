using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using Oheangbu.App.World.Dressing;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactNaturalSurface
    {
        static string Capture(string label)
        {
            if(!new[]{"before","after","close_before","close_after","wind_a","wind_b"}.Contains(label))throw new ArgumentException(label);
            var source=Find<Oheangbu.App.World.Vehicle.WorldMacroPalanquinSeat>().ViewCamera;
            var dressing=Find<WorldMacroDressingRenderer>();bool diagnostic=dressing.AllowDiagnosticCameras;
            var observer=dressing.Observer;
            bool async=ShaderUtil.allowAsyncCompilation;var sky=RenderSettings.skybox;var skyCopy=sky==null?null:new Material(sky);
            var previous=RenderTexture.active;RenderTexture rt=null;Texture2D pixels=null;GameObject go=null;
            float priorTime=Shader.GetGlobalFloat("_DressingTime");
            try
            {
                ShaderUtil.allowAsyncCompilation=false;dressing.AllowDiagnosticCameras=true;
                go=new GameObject("NaturalSurfaceReview"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
                bool close=label.StartsWith("close")||label.StartsWith("wind");
                var eye=close?new Vector3(879,135,235):new Vector3(882,137,240);
                var target=close?new Vector3(873,133,231):new Vector3(830,133,176);
                camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));camera.fieldOfView=60;camera.aspect=16f/9;camera.nearClipPlane=.1f;camera.farClipPlane=4500;camera.orthographic=false;camera.useOcclusionCulling=false;
                dressing.Observer=camera;
                int ui=LayerMask.NameToLayer("UI");if(ui>=0)camera.cullingMask&=~(1<<ui);
                var extra=camera.GetUniversalAdditionalCameraData();var old=source.GetComponent<UniversalAdditionalCameraData>();if(old!=null)EditorUtility.CopySerialized(old,extra);extra.renderType=CameraRenderType.Base;extra.cameraStack.Clear();
                Find<Oheangbu.App.WorldLookDriver>()?.PreviewRegionalSky(eye);
                // Warm only the still-image view. This is expressly not first-visit performance evidence.
                for(int i=0;i<80;i++){Guard();dressing.PrepareView(eye,64);}
                rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);rt.Create();camera.targetTexture=rt;
                for(int i=0;i<16;i++){Guard();RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});}
                RenderTexture.active=rt;pixels=new Texture2D(1920,1080,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();
                if(pixels.GetPixels32().Count(c=>c.r+c.g+c.b>12)<1920*1080/100)throw new InvalidOperationException("Black capture rejected");
                string path=Path.Combine(Output,label+".png");File.WriteAllBytes(path,pixels.EncodeToPNG());
                File.WriteAllText(Path.Combine(Output,label+"_submissions.json"),dressing.RenderCostJson());return path;
            }
            finally
            {
                dressing.Observer=observer;dressing.AllowDiagnosticCameras=diagnostic;ShaderUtil.allowAsyncCompilation=async;Shader.SetGlobalFloat("_DressingTime",priorTime);RenderTexture.active=previous;
                if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(pixels!=null)Object.DestroyImmediate(pixels);if(go!=null)Object.DestroyImmediate(go);
                if(skyCopy!=null){sky.CopyPropertiesFromMaterial(skyCopy);Object.DestroyImmediate(skyCopy);}RenderSettings.skybox=sky;
            }
        }
    }
}
