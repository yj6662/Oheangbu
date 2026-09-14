using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.BrushRender;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class WorldMacroSkyAuthoring
    {
        public const string ProfilePath=WorldMacroBuilder.Folder+"/RegionalSkyProfile.asset";
        static string Output=>WorldMacroBuilder.Output+"/RegionalSky";
        static WorldLookDriver Driver=>Object.FindFirstObjectByType<WorldLookDriver>();
        static RegionalInkSkyProfile Profile=>AssetDatabase.LoadAssetAtPath<RegionalInkSkyProfile>(ProfilePath);
        [Serializable] public class LiveReport
        {
            public string scope="Actual Play mode camera relocation, normal LateUpdate and shader material observation; not real player-input travel.";
            public float started,elapsed,error;public Color initial,actual,expected,target;public bool pass;public Vector3 origin;
        }
        static LiveReport live;
        public static string LiveBegin()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            var cam=Camera.main;var destination=WorldMacroBuilder.Sheet.FindSite("Hyeongang").Position;
            var target=Profile.Evaluate(WorldMacroBuilder.Sheet,destination);
            live=new LiveReport{started=Time.time,origin=cam.transform.position,initial=RenderSettings.skybox.GetColor("_Zenith"),target=target.Zenith};
            cam.transform.position=destination+Vector3.up*1.8f;
            return "Camera relocated; normal LateUpdate drives the sky. Observe after at least 3 seconds.";
        }
        public static string LiveEnd()
        {
            if(!Application.isPlaying||live==null)throw new Exception("Run SkyLiveBegin in play mode first");
            live.elapsed=Time.time-live.started;if(live.elapsed<3)throw new Exception("Need at least 3 game seconds before observation");
            live.actual=RenderSettings.skybox.GetColor("_Zenith");
            live.expected=Color.Lerp(live.initial,live.target,1-Mathf.Exp(-live.elapsed/Mathf.Max(.01f,Profile.ResponseSeconds)));
            live.error=Vector4.Distance(live.actual,live.expected);live.pass=live.error<.004f&&Vector4.Distance(live.initial,live.actual)>.01f;
            Directory.CreateDirectory(Output);File.WriteAllText(Output+"/live.json",JsonUtility.ToJson(live,true));
            Camera.main.transform.position=live.origin;
            return $"Play-mode transition {(live.pass?"PASS":"FAIL")}; elapsed={live.elapsed}; material error={live.error}";
        }
        public static void Configure(WorldLookDriver driver,WorldMacroSheetSO sheet,InkSkyProfile baseline)
        {
            var profile=Profile;
            if(profile==null)
            {
                profile=ScriptableObject.CreateInstance<RegionalInkSkyProfile>();
                var palette=AssetDatabase.LoadAssetAtPath<ElementPaletteSO>(DevSceneKit.PalettePath);
                profile.Regions=sheet.Regions.Select(r=>
                {
                    char initial=r.Realm switch{RealmId.Cheongrim=>'ㄱ',RealmId.Jeokro=>'ㄴ',RealmId.Cheolong=>'ㅅ',RealmId.Hyeongang=>'ㅇ',_=>'ㅁ'};
                    var colour=palette.GetBaseColor(initial);
                    // Keep the exact existing horizon where the distant mountain wash meets it.
                    return new RegionalInkSkyProfile.Entry{Realm=r.Realm,Horizon=baseline.Horizon,
                        Zenith=Color.Lerp(baseline.Zenith,colour,.16f),Cloud=Color.Lerp(baseline.Cloud,colour,.18f),
                        CloudDensity=r.Realm switch{RealmId.Hyeongang=>.20f,RealmId.Cheongrim=>.17f,RealmId.Cheolong=>.16f,RealmId.Jeokro=>.12f,_=>.14f}};
                }).ToArray();
                AssetDatabase.CreateAsset(profile,ProfilePath);
            }
            WorldMacroBuilder.Set(driver,"_regionalSkyProfile",profile);
            WorldMacroBuilder.Set(driver,"_skyGeography",sheet);
            driver.Apply();
        }
        public static string Install()
        {
            RequireScene();Directory.CreateDirectory(Output);
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene,Output+"/BeforeRegionalSky.unity",true);
            var baseline=AssetDatabase.LoadAssetAtPath<InkSkyProfile>(WorldMacroBuilder.Folder+"/SkyProfile.asset");
            Configure(Driver,WorldMacroBuilder.Sheet,baseline);
            // Never serialize the HideAndDontSave runtime sky material into a scene asset.
            RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>(WorldMacroBuilder.Folder+"/Sky.mat");
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Driver.Apply();
            return "Regional sky installed without terrain rebuild. Shared cloud phase and exact horizon retained.";
        }
        static void RequireScene()
        {
            if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=WorldMacroBuilder.ScenePath)
                throw new InvalidOperationException("Macro scene in edit mode required");
        }
        [Serializable] public class Check{public string name;public bool pass;public float measured,limit;}
        [Serializable] public class Report{public string scope="Spatial/temporal numerical tests and actual driver material calls in edit mode; not a player-input travel test.";public string utc;public Check[] checks;public string[] limitations;}
        static float Difference(RegionalInkSkyProfile.SkyState a,RegionalInkSkyProfile.SkyState b)
        {
            float Diff(Color x,Color y)=>Mathf.Max(Mathf.Abs(x.r-y.r),Mathf.Abs(x.g-y.g),Mathf.Abs(x.b-y.b));
            return Mathf.Max(Diff(a.Horizon,b.Horizon),Diff(a.Zenith,b.Zenith),Diff(a.Cloud,b.Cloud),Mathf.Abs(a.CloudDensity-b.CloudDensity));
        }
        public static string Audit()
        {
            RequireScene();Directory.CreateDirectory(Output);
            var s=WorldMacroBuilder.Sheet;var p=Profile;var weights=new float[s.Regions.Length];
            var checks=new List<Check>();
            void Test(string n,float v,float limit)=>checks.Add(new Check{name=n,measured=v,limit=limit,pass=!(float.IsNaN(v)||float.IsInfinity(v))&&v<=limit});
            float sumError=0,maxStep=0;
            for(float z=s.BoundsMin.y;z<=s.BoundsMax.y;z+=100)for(float x=s.BoundsMin.x;x<=s.BoundsMax.x;x+=100)
            {
                var pos=new Vector3(x,0,z);p.GetWeights(s,pos,weights);sumError=Mathf.Max(sumError,Mathf.Abs(weights.Sum()-1));
                var a=p.Evaluate(s,pos);maxStep=Mathf.Max(maxStep,Difference(a,p.Evaluate(s,pos+Vector3.right)),Difference(a,p.Evaluate(s,pos+Vector3.forward)));
            }
            Test("normalized region weights including unassigned gaps",sumError,.00001f);
            Test("maximum parameter delta over 1m at 100m-grid origins",maxStep,.002f);
            float routeStep=0,altitude=0;
            foreach(var r in s.Routes)for(int i=1;i<r.Points.Length;i++)
            {
                var a=r.Points[i-1];var b=r.Points[i];int n=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/4));
                for(int k=0;k<n;k++){var pos=Vector3.Lerp(a,b,k/(float)n);routeStep=Mathf.Max(routeStep,Difference(p.Evaluate(s,pos),p.Evaluate(s,Vector3.Lerp(a,b,(k+1f)/n))));}
            }
            Test("actual authored routes maximum <=4m parameter delta",routeStep,.005f);
            var start=p.Evaluate(s,s.FindSite("Cheongrim").Position);var target=p.Evaluate(s,s.FindSite("Hyeongang").Position);
            var expected=RegionalInkSkyProfile.Blend(start,target,1-Mathf.Exp(-2/p.ResponseSeconds));
            foreach(int fps in new[]{30,60,120})
            {
                var state=start;float t=1-Mathf.Exp(-(1f/fps)/p.ResponseSeconds);
                for(int i=0;i<fps*2;i++)state=RegionalInkSkyProfile.Blend(state,target,t);
                Test("2s transition frame rate "+fps,Difference(state,expected),.00001f);
            }
            foreach(var site in s.Sites)altitude=Mathf.Max(altitude,Difference(p.Evaluate(s,site.Position),p.Evaluate(s,site.Position+Vector3.up*2000)));
            Test("height does not change regional sky",altitude,.000001f);
            var baseline=AssetDatabase.LoadAssetAtPath<InkSkyProfile>(WorldMacroBuilder.Folder+"/SkyProfile.asset");
            var source=AssetDatabase.LoadAssetAtPath<Material>(WorldMacroBuilder.Folder+"/Sky.mat");
            string before=EditorJsonUtility.ToJson(source);Driver.Apply();var instance=RenderSettings.skybox;
            foreach(var site in s.Sites)Driver.PreviewRegionalSky(site.Position);
            Test("one transient material reused",RenderSettings.skybox==instance&&instance!=source?0:1,0);
            Test("authored sky material unchanged",before==EditorJsonUtility.ToJson(source)?0:1,0);
            Test("shared cloud scale",Mathf.Abs(instance.GetFloat("_CloudScale")-baseline.CloudScale),0);
            Test("shared cloud speed",Mathf.Abs(instance.GetFloat("_CloudSpeed")-baseline.CloudSpeed),0);
            Test("horizon matches original",p.Regions.Max(e=>Vector4.Distance(e.Horizon,baseline.Horizon)),.000001f);
            // Run the exact driver interpolation path; do not infer execution from profile math only.
            var method=typeof(WorldLookDriver).GetMethod("ApplyRegionalSky",BindingFlags.NonPublic|BindingFlags.Instance);
            method.Invoke(Driver,new object[]{s.FindSite("Cheongrim").Position,0f,true});
            for(int i=0;i<120;i++)method.Invoke(Driver,new object[]{s.FindSite("Hyeongang").Position,1f/60,false});
            var actual=RenderSettings.skybox.GetColor("_Zenith");
            Test("actual driver 2s material interpolation",Vector4.Distance(actual,expected.Zenith),.00002f);
            Driver.Apply();
            var report=new Report{utc=DateTime.UtcNow.ToString("o"),checks=checks.ToArray(),limitations=new[]{"Sky visual approval belongs to the user.","No real-input full route traversal or 30/60/120fps rendered movie.","Frame-rate tests use constant target with explicit dt; spatial route sweeps are separate.","Ground, fog, sun, ambient and reflection settings remain unchanged."}};
            File.WriteAllText(Output+"/audit.json",JsonUtility.ToJson(report,true));
            return $"Regional sky checks {checks.Count(c=>c.pass)}/{checks.Count}; max 1m delta={maxStep}; max route step={routeStep}";
        }
        [Serializable] public class CaptureInfo{public string id;public Vector3 position;public float[] weights;}
        public static string Capture(string id)
        {
            RequireScene();if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new Exception("New sky screenshot stopped: commit >=85%");
            Directory.CreateDirectory(Output);var s=WorldMacroBuilder.Sheet;var p=Profile;Vector3 pos;
            if(id.StartsWith("transition_"))
            {
                float desired=id.EndsWith("wood")?.8f:id.EndsWith("earth")?.2f:.5f;
                int wood=Array.FindIndex(s.Regions,r=>r.Realm==RealmId.Cheongrim);var w=new float[s.Regions.Length];
                var points=s.Routes.Where(r=>r.Id=="Return_Inn_EastFoothillPass"||r.Id=="Return_EastFoothillPass_Capital").SelectMany(r=>r.Points).ToArray();
                pos=points.OrderBy(v=>{p.GetWeights(s,v,w);return Mathf.Abs(w[wood]-desired);}).First();
            }
            else pos=s.FindSite(id)?.Position??throw new Exception("Unknown region site "+id);
            pos.y=WorldMacroTerrain.SurfaceHeight(s,pos.x,pos.z)+1.8f;
            var forward=s.FindSite("Hwanggyeong").Position-s.FindSite("Inn").Position;forward.y=0;forward.Normalize();
            var go=new GameObject("Temporary_RegionalSkyCapture"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();cam.CopyFrom(Camera.main);cam.enabled=false;
            cam.farClipPlane=40000;cam.nearClipPlane=.3f;cam.useOcclusionCulling=false;cam.layerCullDistances=new float[32];
            cam.transform.SetPositionAndRotation(pos,Quaternion.LookRotation(forward+Vector3.up*.48f));
            cam.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            RenderTexture rt=null;Texture2D image=null;var old=RenderTexture.active;
            try
            {
                Driver.PreviewRegionalSky(pos);rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);image=new Texture2D(1920,1080,TextureFormat.RGB24,false);
                cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(Output+"/"+id+".png",image.EncodeToPNG());
            }
            finally{cam.targetTexture=null;RenderTexture.active=old;if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(image!=null)Object.DestroyImmediate(image);Object.DestroyImmediate(go);Driver.Apply();}
            var weights=new float[s.Regions.Length];p.GetWeights(s,pos,weights);
            File.WriteAllText(Output+"/"+id+".json",JsonUtility.ToJson(new CaptureInfo{id=id,position=pos,weights=weights},true));
            return id+" 1920x1080; commit="+Prologue.PrologueAudit.CommitRatio();
        }
    }
}
