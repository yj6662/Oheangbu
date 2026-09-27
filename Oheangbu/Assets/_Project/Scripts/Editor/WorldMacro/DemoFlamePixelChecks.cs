using System;
using System.Collections.Generic;
using System.IO;
using Oheangbu.App.Demo;
using Oheangbu.EditorTools.Prologue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Offscreen GPU diagnostic: actual PS/texture/flipbook against their fixed combat cone.
    // The top view proves horizontal extent only. It does not prove campaign wall occlusion or art quality.
    public static class DemoFlamePixelChecks
    {
        const int Size=512;
        [Serializable] sealed class Row
        {
            public bool constrained;
            public float yaw,maximumVisibleRange,peak;
            public int frames,litPixels,outsidePixels;
        }
        [Serializable] sealed class Report
        {
            public string status;
            public List<string> passed=new List<string>(),failed=new List<string>();
            public List<Row> rows=new List<Row>();
            public float maximumCommit;
            public string scope="512-square GPU test of actual flame PS in isolated preview scene; 2 origins/yaws, 6 times, 0.04m raster tolerance. No campaign input/performance or final art approval.";
        }
        public static string Run()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Edit mode only");
            var report=new Report();var scene=EditorSceneManager.NewPreviewScene();
            RenderTexture target=null;Texture2D readback=null;SummonCombatProfile profile=null;
            var previous=RenderTexture.active;
            try
            {
                profile=Object.Instantiate(AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(DemoHaetaeAuthoring.ProfilePath));
                profile.FlameAdditiveShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/DemoFlameAdditive.shadergraph");
                profile.FlameAlphaShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/DemoFlameAlpha.shadergraph");
                if(profile.FlameAdditiveShader==null||profile.FlameAlphaShader==null)throw new InvalidOperationException("Derived shader graphs missing");
                if(ShaderUtil.ShaderHasError(profile.FlameAdditiveShader)||ShaderUtil.ShaderHasError(profile.FlameAlphaShader))throw new InvalidOperationException("Derived shader compile error");
                var cameraObject=new GameObject("Owned top-down GPU test");SceneManager.MoveGameObjectToScene(cameraObject,scene);
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.orthographic=true;
                camera.orthographicSize=6;camera.aspect=1;camera.nearClipPlane=.1f;camera.farClipPlane=25;camera.allowMSAA=false;
                var urp=camera.GetUniversalAdditionalCameraData();urp.renderPostProcessing=false;urp.antialiasing=AntialiasingMode.None;
                target=new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBFloat){antiAliasing=1};target.Create();camera.targetTexture=target;
                readback=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);
                foreach(float yaw in new[]{0f,37f})foreach(bool constrained in new[]{false,true})
                {
                    profile.FlameConstrainVisual=constrained;
                    var origin=yaw==0?Vector3.zero:new Vector3(312,40,-205);
                    var forward=Quaternion.Euler(0,yaw,0)*Vector3.forward;
                    var plan=new SummonFlameAttackPlan(origin,forward,profile.FlameRange,profile.FlameHalfAngleDegrees,
                        profile.FlameVerticalTolerance,0,profile.FlameWindupSeconds,profile.FlameSpraySeconds,profile.FlameRecoverySeconds);
                    var holder=new GameObject("Owned GPU test flame");SceneManager.MoveGameObjectToScene(holder,scene);
                    var visual=holder.AddComponent<DemoSummonFlamePresentation>();
                    var row=new Row{constrained=constrained,yaw=yaw};report.rows.Add(row);
                    try
                    {
                        camera.transform.SetPositionAndRotation(origin+forward*2+Vector3.up*10,Quaternion.LookRotation(Vector3.down,Vector3.forward));
                        foreach(float elapsed in new[]{.1f,.25f,.45f,.65f,.85f,1.05f})
                        {
                            report.maximumCommit=Mathf.Max(report.maximumCommit,PrologueAudit.CommitRatio());
                            if(report.maximumCommit>=.85f)throw new InvalidOperationException("GPU captures stopped at >=85% system commit");
                            float clock=plan.ReleaseAt+elapsed;plan.AdvanceTo(clock);visual.Sample(profile,plan,clock,true);
                            if(GraphicsSettings.currentRenderPipeline!=null)RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});
                            else camera.Render();
                            RenderTexture.active=target;readback.ReadPixels(new Rect(0,0,Size,Size),0,0,false);readback.Apply(false,false);
                            var pixels=readback.GetPixels();
                            Vector3 bottom=camera.ViewportToWorldPoint(new Vector3(.5f/Size,.5f/Size,10));
                            Vector3 dx=camera.transform.right*(12f/Size),dy=camera.transform.up*(12f/Size);
                            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
                            {
                                Color color=pixels[y*Size+x];float peak=Mathf.Max(color.r,color.g,color.b);row.peak=Mathf.Max(row.peak,peak);
                                if(peak<.025f)continue;
                                row.litPixels++;Vector3 delta=bottom+dx*x+dy*y-origin;delta.y=0;
                                float radius=delta.magnitude;row.maximumVisibleRange=Mathf.Max(row.maximumVisibleRange,radius);
                                float longitudinal=Vector3.Dot(delta,forward),lateral=Vector3.Dot(delta,Vector3.Cross(Vector3.up,forward));
                                float theta=plan.HalfAngleDegrees*Mathf.Deg2Rad;
                                float side=longitudinal*Mathf.Sin(theta)-Mathf.Abs(lateral)*Mathf.Cos(theta);
                                if(radius>plan.Range+.04f||side<-.04f)row.outsidePixels++;
                            }
                            row.frames++;
                        }
                    }
                    finally{Object.DestroyImmediate(holder);}
                }
                foreach(var row in report.rows)
                {
                    bool nonblank=row.frames==6&&row.litPixels>100;
                    (nonblank?report.passed:report.failed).Add("Nonblank six-frame GPU output: yaw="+row.yaw+", constrained="+row.constrained);
                    bool extent=row.constrained?row.outsidePixels==0:row.outsidePixels>0;
                    (extent?report.passed:report.failed).Add((row.constrained?"Derived shader confines visible pixels":"Original effect reproduces pixels beyond combat cone")+", yaw="+row.yaw);
                }
            }
            catch(Exception e){report.failed.Add(e.ToString());}
            finally
            {
                RenderTexture.active=previous;if(target!=null){target.Release();Object.DestroyImmediate(target);}
                if(readback!=null)Object.DestroyImmediate(readback);if(profile!=null)Object.DestroyImmediate(profile);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            report.status=report.failed.Count==0?"PASS_GPU_HORIZONTAL_EXTENT":"FAIL";
            string json=JsonUtility.ToJson(report,true);File.WriteAllText(Path.Combine(DemoSummonAuthoring.Output,"flame_pixels.json"),json);return json;
        }
    }
}
