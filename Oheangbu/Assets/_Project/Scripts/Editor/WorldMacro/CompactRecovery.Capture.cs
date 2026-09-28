using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactRecovery
    {
        static string Capture(string mode)
        {
            RequireEdit();
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%; capture stopped");
            if(!new[]{"baseline","roads","path","shadows","ao","terrainShadows","after"}.Contains(mode))throw new ArgumentException(mode);
            var scene=SceneManager.GetActiveScene();
            var renderers=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).ToArray();
            var disabled=new List<Renderer>();
            var casters=new Dictionary<Renderer,ShadowCastingMode>();
            var mats=renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();
            var materialCopies=new Dictionary<Material,Material>();
            var lights=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Light>(true)).Where(l=>l.type==LightType.Directional).ToDictionary(l=>l,l=>l.shadows);
            var source=Find<Oheangbu.App.World.Vehicle.WorldMacroPalanquinSeat>()?.ViewCamera;
            if(source==null)source=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First(c=>c.GetComponentInParent<Canvas>()==null);
            GameObject go=null;RenderTexture rt=null;Texture2D pixels=null;var previous=RenderTexture.active;
            var sky=RenderSettings.skybox;var skyCopy=sky==null?null:new Material(sky){hideFlags=HideFlags.HideAndDontSave};
            bool async=ShaderUtil.allowAsyncCompilation;
            try
            {
                if(mode=="roads")foreach(var r in renderers.Where(r=>PathOf(r.transform).Contains("01_TerrainConformedRoutes")&&r.enabled)){r.enabled=false;disabled.Add(r);}
                string property=mode=="path"?"_GroundPath":mode=="ao"?"_AoStrength":null;
                if(property!=null)foreach(var m in mats.Where(m=>m.HasProperty(property))){materialCopies[m]=new Material(m){hideFlags=HideFlags.HideAndDontSave};m.SetFloat(property,0);}
                if(mode=="shadows")foreach(var l in lights.Keys)l.shadows=LightShadows.None;
                if(mode=="terrainShadows")foreach(var r in renderers.Where(r=>r.name.StartsWith("Terrain_"))){casters[r]=r.shadowCastingMode;r.shadowCastingMode=ShadowCastingMode.Off;}
                ShaderUtil.allowAsyncCompilation=false;
                go=new GameObject("CompactRecoveryCamera"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
                var eye=new Vector3(882,137,240);var target=new Vector3(830,133,176);
                camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));camera.fieldOfView=60;camera.aspect=16f/9;camera.nearClipPlane=.1f;camera.farClipPlane=4500;camera.orthographic=false;camera.useOcclusionCulling=false;
                if(mode=="baseline")
                {
                    var ray=camera.ViewportPointToRay(new Vector3(.435f,1-.53f));
                    var hits=Physics.RaycastAll(ray,500).OrderBy(h=>h.distance).Select(h=>PathOf(h.collider.transform)+" position="+h.point.ToString("R")+" normal="+h.normal.ToString("R")+" triangle="+h.triangleIndex);
                    File.WriteAllLines(Path.Combine(Output,"shadow_surface.txt"),hits);
                }
                int ui=LayerMask.NameToLayer("UI");if(ui>=0)camera.cullingMask&=~(1<<ui);
                var additional=camera.GetUniversalAdditionalCameraData();var sa=source.GetComponent<UniversalAdditionalCameraData>();if(sa!=null)EditorUtility.CopySerialized(sa,additional);additional.renderType=CameraRenderType.Base;additional.cameraStack.Clear();
                Find<Oheangbu.App.WorldLookDriver>()?.PreviewRegionalSky(eye);
                rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);rt.Create();camera.targetTexture=rt;
                if(GraphicsSettings.currentRenderPipeline!=null)RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});else camera.Render();
                RenderTexture.active=rt;pixels=new Texture2D(1920,1080,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();
                if(pixels.GetPixels32().Count(c=>c.r+c.g+c.b>12)<1920*1080/100)throw new InvalidOperationException("Black capture rejected");
                string path=Path.Combine(Output,mode+".png");File.WriteAllBytes(path,pixels.EncodeToPNG());return path;
            }
            finally
            {
                foreach(var r in disabled)r.enabled=true;foreach(var l in lights)l.Key.shadows=l.Value;
                foreach(var r in casters)r.Key.shadowCastingMode=r.Value;
                foreach(var m in materialCopies){m.Key.CopyPropertiesFromMaterial(m.Value);Object.DestroyImmediate(m.Value);}
                RenderTexture.active=previous;if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(pixels!=null)Object.DestroyImmediate(pixels);if(go!=null)Object.DestroyImmediate(go);
                if(skyCopy!=null){sky.CopyPropertiesFromMaterial(skyCopy);Object.DestroyImmediate(skyCopy);}RenderSettings.skybox=sky;ShaderUtil.allowAsyncCompilation=async;
            }
        }
    }
}
