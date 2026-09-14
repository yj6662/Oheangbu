using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static class CodexMountainLODAudit
    {
        [Serializable] private sealed class Sample
        {
            public string mountain;
            public float projectedHeight;
            public int expectedLOD;
            public float silhouetteIoU, meanRgbError, automaticVsForcedError;
            public bool silhouetteWithinOnePixel;
        }
        [Serializable] private sealed class Report
        {
            public bool passed;
            public float sceneQualityLodBias;
            public string valleySelectionEstimate;
            public string conditions="960x540 perspective FOV60; isolated real mountain; lodBias=1; post off; camera moves; fades sampled in size mode for deterministic coverage, not timing; original settings restored";
            public List<Sample> samples=new List<Sample>();
        }
        public static string Probe()
        {
            if(EditorApplication.isPlaying || SceneManager.GetActiveScene().path!=CodexWorldSceneBuilder.ScenePath) return "FAIL: C2 Edit mode required";
            var originals=CodexMountainLOD.Originals();
            if(originals.Length!=9) return "FAIL: missing mountains";
            var fadeAnimation=originals.ToDictionary(r=>r.GetComponent<LODGroup>(),r=>r.GetComponent<LODGroup>().animateCrossFading);
            var driver=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<WorldLookDriver>(true)).First();
            driver.Apply();
            var report=new Report();
            report.sceneQualityLodBias=QualitySettings.lodBias;
            var valley=new Vector3(0,CodexWorldGeometry.Height(0,12)+1.8f,12);
            report.valleySelectionEstimate=string.Join("; ",originals.Select(r=>
            {
                var g=r.GetComponent<LODGroup>();
                float h=g.size*QualitySettings.lodBias/(2*Mathf.Tan(30*Mathf.Deg2Rad)*Vector3.Distance(valley,g.transform.TransformPoint(g.localReferencePoint)));
                var ls=g.GetLODs();int level=h>=ls[0].screenRelativeTransitionHeight?0:h>=ls[1].screenRelativeTransitionHeight?1:2;
                return $"{r.name}: effectiveHeight={h:F3}, LOD{level} (excludes fade/frustum)";
            }));
            var oldLayers=new Dictionary<GameObject,int>();
            float oldBias=QualitySettings.lodBias;
            int oldMax=QualitySettings.maximumLODLevel;
            bool oldAsyncCompilation=ShaderUtil.allowAsyncCompilation;
            var previous=RenderTexture.active;
            GameObject eye=null;RenderTexture rt=null;Texture2D pixels=null;
            string folder=CodexWorldAudit.CaptureFolder+"/MountainLOD";
            Directory.CreateDirectory(folder);
            try
            {
                QualitySettings.lodBias=1;QualitySettings.maximumLODLevel=0;
                // A first-use cross-fade variant must finish compiling before pixel validation.
                ShaderUtil.allowAsyncCompilation=false;
                eye=new GameObject("~MountainLODAudit") {hideFlags=HideFlags.HideAndDontSave};
                var camera=eye.AddComponent<Camera>();camera.enabled=false;
                camera.fieldOfView=60;camera.nearClipPlane=.1f;camera.farClipPlane=100000;
                camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                camera.allowHDR=true;camera.allowMSAA=false;
                var extra=camera.GetUniversalAdditionalCameraData();extra.renderPostProcessing=false;extra.renderShadows=false;
                rt=new RenderTexture(960,540,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
                pixels=new Texture2D(960,540,TextureFormat.RGB24,false,false);camera.targetTexture=rt;
                Color32[] Render(LODGroup group,int force,string save=null)
                {
                    group.ForceLOD(force);camera.Render();camera.Render();
                    RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,960,540),0,0);pixels.Apply();
                    if(save!=null) File.WriteAllBytes(folder+"/"+save+".png",pixels.EncodeToPNG());
                    return pixels.GetPixels32();
                }
                foreach(var original in originals)
                {
                    var group=original.GetComponent<LODGroup>();
                    group.animateCrossFading=false;
                    foreach(var renderer in group.GetLODs().SelectMany(l=>l.renderers))
                    {oldLayers[renderer.gameObject]=renderer.gameObject.layer;renderer.gameObject.layer=31;}
                    Vector3 centre=group.transform.TransformPoint(group.localReferencePoint);
                    var lods=group.GetLODs();
                    float first=lods[0].screenRelativeTransitionHeight,second=lods[1].screenRelativeTransitionHeight;
                    float[] heights={(1+first)*.5f,(first+second)*.5f,second*.4f,.015f};
                    for(int sample=0;sample<heights.Length;sample++)
                    {
                        int level=Math.Min(sample,2);
                        float distance=group.size/(2*Mathf.Tan(30*Mathf.Deg2Rad)*heights[sample]);
                        eye.transform.SetPositionAndRotation(centre-Vector3.forward*distance,Quaternion.identity);
                        bool save=original==originals[0] && sample<3;
                        var full=Render(group,0,save?"height_"+level+"_full":null);
                        var reduced=Render(group,level,save?"height_"+level+"_lod":null);
                        var automatic=Render(group,-1,save?"height_"+level+"_auto":null);
                        report.samples.Add(new Sample {mountain=original.name,projectedHeight=heights[sample],expectedLOD=level,
                            silhouetteIoU=IoU(full,reduced),silhouetteWithinOnePixel=WithinOnePixel(full,reduced),meanRgbError=Error(full,reduced),automaticVsForcedError=Error(automatic,reduced)});
                    }
                    // Samples inside each size-based fade band; compare mask coverage to the detailed mesh.
                    foreach(float h in new[]{first+(1-first)*lods[0].fadeTransitionWidth*.5f,second+(first-second)*lods[1].fadeTransitionWidth*.5f})
                    {
                        eye.transform.position=centre-Vector3.forward*(group.size/(2*Mathf.Tan(30*Mathf.Deg2Rad)*h));
                        var full=Render(group,0);
                        var automatic=Render(group,-1,original==originals[0]?"crossfade_"+h.ToString("F3",System.Globalization.CultureInfo.InvariantCulture):null);
                        report.samples.Add(new Sample {mountain=original.name,projectedHeight=h,expectedLOD=-1,
                            silhouetteIoU=IoU(full,automatic),silhouetteWithinOnePixel=WithinOnePixel(full,automatic),meanRgbError=Error(full,automatic),automaticVsForcedError=-1});
                    }
                    group.ForceLOD(-1);
                    foreach(var renderer in group.GetLODs().SelectMany(l=>l.renderers)) renderer.gameObject.layer=oldLayers[renderer.gameObject];
                }
                // At eight pixels high, a single edge pixel has a large IoU cost: enforce a one-pixel silhouette bound there.
                report.passed=report.samples.All(s=>(s.projectedHeight<.02f?s.silhouetteWithinOnePixel:s.silhouetteIoU>.98f) && (s.expectedLOD<0 || s.automaticVsForcedError<.002f));
                string json=JsonUtility.ToJson(report,true);File.WriteAllText(folder+"/gpu-validation.json",json);
                return $"{(report.passed?"PASS":"FAIL")}: samples={report.samples.Count}; minSilhouetteIoU={report.samples.Min(s=>s.silhouetteIoU):F6}; "
                    +$"maxAutoForcedError={report.samples.Max(s=>s.automaticVsForcedError):F6}; maxMeanRgbError={report.samples.Max(s=>s.meanRgbError):F6}; report={folder}/gpu-validation.json";
            }
            finally
            {
                foreach(var pair in oldLayers) if(pair.Key!=null) pair.Key.layer=pair.Value;
                foreach(var r in originals) r.GetComponent<LODGroup>().ForceLOD(-1);
                foreach(var pair in fadeAnimation) pair.Key.animateCrossFading=pair.Value;
                QualitySettings.lodBias=oldBias;QualitySettings.maximumLODLevel=oldMax;
                ShaderUtil.allowAsyncCompilation=oldAsyncCompilation;
                RenderTexture.active=previous;
                if(eye!=null) Object.DestroyImmediate(eye);
                if(rt!=null) {rt.Release();Object.DestroyImmediate(rt);}
                if(pixels!=null) Object.DestroyImmediate(pixels);
            }
        }
        public static string CaptureSettledViews()
        {
            var groups=CodexMountainLOD.Originals().Select(r=>r.GetComponent<LODGroup>()).ToArray();
            var modes=groups.Select(g=>g.fadeMode).ToArray();
            try
            {
                // A freshly teleported capture camera should show the settled LOD, not the transient animation.
                foreach(var g in groups) {g.fadeMode=LODFadeMode.None;g.ForceLOD(-1);}
                return CodexWorldAudit.CaptureAll();
            }
            finally {for(int i=0;i<groups.Length;i++) groups[i].fadeMode=modes[i];}
        }
        private static bool Filled(Color32 c)=>Math.Max(c.r,Math.Max(c.g,c.b))>8;
        private static bool WithinOnePixel(Color32[] a,Color32[] b)
        {
            for(int i=0;i<a.Length;i++)
            {
                if(Filled(a[i])==Filled(b[i])) continue;
                var other=Filled(a[i])?b:a;
                int x=i%960,y=i/960;bool found=false;
                for(int dy=-1;dy<=1;dy++) for(int dx=-1;dx<=1;dx++)
                {int px=x+dx,py=y+dy;if(px>=0&&px<960&&py>=0&&py<540&&Filled(other[py*960+px])) found=true;}
                if(!found) return false;
            }
            return a.Any(Filled)&&b.Any(Filled);
        }
        private static float IoU(Color32[] a,Color32[] b)
        {
            int union=0,intersection=0;
            for(int i=0;i<a.Length;i++){bool x=Filled(a[i]),y=Filled(b[i]);if(x||y)union++;if(x&&y)intersection++;}
            return union>0?intersection/(float)union:0;
        }
        private static float Error(Color32[] a,Color32[] b)
        {
            double total=0;
            for(int i=0;i<a.Length;i++) total+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);
            return (float)(total/(a.Length*3.0*255));
        }
    }
}
