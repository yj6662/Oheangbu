using System;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static class CodexInnAssets
    {
        public static readonly string[] Sources={"Assets/House_1/house.fbx","Assets/House_2/house2 .fbx"};
        public const string Folder=CodexWorldSceneBuilder.AssetFolder+"/ThatchedInn";
        public static Bounds BoundsOf(GameObject go)
        {
            var rs=go.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;
            foreach(var r in rs.Skip(1)) b.Encapsulate(r.bounds);return b;
        }
        public static string Inspect()
        {
            if(EditorApplication.isPlaying) return "FAIL: Edit mode required";
            var report=new StringBuilder();
            string folder=CodexWorldAudit.CaptureFolder+"/HouseCandidates";Directory.CreateDirectory(folder);
            var driver=Object.FindFirstObjectByType<WorldLookDriver>();driver.Apply();
            bool oldAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            var previous=RenderTexture.active;
            try
            {
                for(int i=0;i<Sources.Length;i++)
                {
                    var source=AssetDatabase.LoadAssetAtPath<GameObject>(Sources[i]);
                    if(source==null) return "FAIL: not imported "+Sources[i];
                    var go=(GameObject)PrefabUtility.InstantiatePrefab(source);go.hideFlags=HideFlags.HideAndDontSave;
                    GameObject eye=null;RenderTexture rt=null;Texture2D image=null;
                    try
                    {
                        foreach(var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer=30;
                        var b=BoundsOf(go);
                        report.AppendLine(Sources[i]+" bounds="+b+" rootScale="+go.transform.localScale+" colliders="+go.GetComponentsInChildren<Collider>().Length);
                        foreach(var r in go.GetComponentsInChildren<MeshRenderer>())
                            report.AppendLine(r.name+" localPos="+r.transform.localPosition+" size="+r.bounds.size+" tris="+r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3+" materials="+string.Join(",",r.sharedMaterials.Select(m=>m==null?"NULL":m.name+":"+m.shader.name)));
                        eye=new GameObject("~InnAssetCamera"){hideFlags=HideFlags.HideAndDontSave};
                        var c=eye.AddComponent<Camera>();c.enabled=false;c.fieldOfView=35;c.nearClipPlane=.1f;c.farClipPlane=10000;c.cullingMask=1<<30;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.25f,.25f,.25f);
                        c.GetUniversalAdditionalCameraData().renderPostProcessing=false;
                        rt=new RenderTexture(1024,768,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);c.targetTexture=rt;
                        image=new Texture2D(1024,768,TextureFormat.RGB24,false,false);
                        for(int view=0;view<4;view++)
                        {
                            var d=Quaternion.Euler(0,view*90,0)*new Vector3(.6f,.4f,-1).normalized;
                            eye.transform.position=b.center+d*b.size.magnitude*1.7f;eye.transform.LookAt(b.center);
                            c.Render();c.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1024,768),0,0);image.Apply();
                            File.WriteAllBytes(folder+"/house"+(i+1)+"_view"+view+".png",image.EncodeToPNG());
                        }
                    }
                    finally {Object.DestroyImmediate(go);if(eye!=null)Object.DestroyImmediate(eye);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(image!=null)Object.DestroyImmediate(image);}
                }
                File.WriteAllText(folder+"/inspection.txt",report.ToString());return report.ToString();
            }
            finally {RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=oldAsync;}
        }
        public static string CaptureLighting()
        {
            var root=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>()).Single(t=>t.name==CodexThatchedInn.RootName);
            var lights=root.GetComponentsInChildren<Light>();var states=lights.Select(l=>l.enabled).ToArray();
            var groups=CodexMountainLOD.Originals().Select(r=>r.GetComponent<LODGroup>()).ToArray();var modes=groups.Select(g=>g.fadeMode).ToArray();
            bool oldAsync=ShaderUtil.allowAsyncCompilation;string folder=CodexWorldAudit.CaptureFolder+"/InnLighting";Directory.CreateDirectory(folder);
            Texture2D on=null,off=null;string cut=null;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                foreach(var g in groups) g.fadeMode=LODFadeMode.None;
                foreach(var l in lights)l.enabled=true;
                string captures=CodexWorldAudit.CaptureAll();
                cut=Path.GetFullPath(CodexWorldAudit.CaptureFolder+"/cut4_inn.png");File.Copy(cut,folder+"/on.png",true);
                foreach(var l in lights)l.enabled=false;
                CodexWorldAudit.CaptureCut(4);File.Copy(cut,folder+"/off.png",true);
                on=new Texture2D(2,2);off=new Texture2D(2,2);on.LoadImage(File.ReadAllBytes(folder+"/on.png"));off.LoadImage(File.ReadAllBytes(folder+"/off.png"));
                var a=on.GetPixels32();var b=off.GetPixels32();int changed=0;double total=0;int max=0;
                for(int i=0;i<a.Length;i++) {int delta=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);if(delta>6)changed++;total+=delta;max=Math.Max(max,delta);}
                string result=$"{(changed>100 && lights.Length==2?"PASS":"FAIL")}: actualLightOnOffChangedPixels={changed}; meanRGBDifference={total/(a.Length*3.0*255):F6}; maxRGBSumDifference={max}; intensity={string.Join(",",lights.Select(l=>l.intensity))}; range={string.Join(",",lights.Select(l=>l.range))}; sourceDiffusersUnchanged=true";
                File.WriteAllText(folder+"/validation.txt",result);return result;
            }
            finally
            {
                for(int i=0;i<lights.Length;i++)lights[i].enabled=states[i];
                for(int i=0;i<groups.Length;i++)groups[i].fadeMode=modes[i];
                ShaderUtil.allowAsyncCompilation=oldAsync;
                if(cut!=null && File.Exists(folder+"/on.png"))File.Copy(folder+"/on.png",cut,true);
                if(on!=null)Object.DestroyImmediate(on);if(off!=null)Object.DestroyImmediate(off);
            }
        }
    }
}
