using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactKcisaReplacement
    {
        static string Capture(string view)
        {
            Guard();var source=CompactRecovery.Find<Oheangbu.App.World.Vehicle.WorldMacroPalanquinSeat>().ViewCamera;
            Vector3 eye=new Vector3(882,137,240),target=new Vector3(830,133,176);float fov=60;
            string node=view=="clerk"?"Clerk_TemporaryAppearance":view=="beast"?"Enemy_TEMP_Visual":view=="house"?"KCISA_House_000":view=="pagoda"?"Authored_GranitePagoda":view=="shrine"?"KCISA_SeonghwangCairn":view=="tree"?"KCISA_SacredElm":null;
            if(node!=null)
            {
                var t=All.First(x=>x.name==node);var rr=t.GetComponentsInChildren<Renderer>().Where(r=>r is SkinnedMeshRenderer||r.GetComponent<MeshFilter>()!=null&&r.GetComponent<MeshFilter>().sharedMesh!=null).ToArray();
                Bounds b=rr[0].bounds;foreach(var r in rr)b.Encapsulate(r.bounds);
                target=b.center;float d=Mathf.Max(2.5f,b.size.magnitude*(view=="pagoda"?1.22f:.85f));eye=target+t.forward*d+t.right*d*.35f+Vector3.up*d*.12f;fov=48;
            }
            if(view=="stonecap")
            {
                var t=All.First(t=>t.name=="Stone_Terrace");target=t.position+Vector3.up*t.lossyScale.y*.5f;
                eye=target+new Vector3(8,30,22);fov=60;
                var f=t.GetComponentInChildren<MeshFilter>();
                foreach(var mf in t.GetComponentsInChildren<MeshFilter>())if(mf.sharedMesh!=null){var mesh=mf.sharedMesh;mesh.UploadMeshData(false);mf.sharedMesh=null;mf.sharedMesh=mesh;}
            }
            var dressing=CompactRecovery.Find<Oheangbu.App.World.Dressing.WorldMacroDressingRenderer>();var observer=dressing.Observer;bool diagnostic=dressing.AllowDiagnosticCameras;
            var prev=RenderTexture.active;RenderTexture rt=null;Texture2D pixels=null;GameObject go=null;bool async=ShaderUtil.allowAsyncCompilation;
            var sky=RenderSettings.skybox;var skyCopy=sky==null?null:new Material(sky);
            try
            {
                ShaderUtil.allowAsyncCompilation=false;go=new GameObject("KcisaReview"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();cam.CopyFrom(source);cam.enabled=false;
                cam.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));cam.fieldOfView=fov;cam.aspect=16f/9;cam.nearClipPlane=.06f;cam.farClipPlane=4500;cam.orthographic=false;cam.useOcclusionCulling=false;
                var data=cam.GetUniversalAdditionalCameraData();var old=source.GetComponent<UniversalAdditionalCameraData>();if(old!=null)EditorUtility.CopySerialized(old,data);data.renderType=CameraRenderType.Base;data.cameraStack.Clear();
                int ui=LayerMask.NameToLayer("UI");if(ui>=0)cam.cullingMask&=~(1<<ui);
                CompactRecovery.Find<Oheangbu.App.WorldLookDriver>()?.PreviewRegionalSky(eye);dressing.Observer=cam;dressing.AllowDiagnosticCameras=true;
                for(int i=0;i<48;i++){Guard();dressing.PrepareView(eye,32);}
                rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);rt.Create();cam.targetTexture=rt;
                for(int i=0;i<6;i++)RenderPipeline.SubmitRenderRequest(cam,new RenderPipeline.StandardRequest{destination=rt});
                RenderTexture.active=rt;pixels=new Texture2D(1920,1080,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();
                if(pixels.GetPixels32().Count(c=>c.r+c.g+c.b>12)<1920*1080/100)throw new InvalidOperationException("Black capture rejected");
                string path=Output+"/"+view+".png";File.WriteAllBytes(path,pixels.EncodeToPNG());return path;
            }
            finally
            {
                dressing.Observer=observer;dressing.AllowDiagnosticCameras=diagnostic;ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=prev;
                if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(pixels!=null)Object.DestroyImmediate(pixels);if(go!=null)Object.DestroyImmediate(go);
                if(skyCopy!=null){sky.CopyPropertiesFromMaterial(skyCopy);Object.DestroyImmediate(skyCopy);}RenderSettings.skybox=sky;
            }
        }
        static string Verify()
        {
            var left=Primitives().Select(t=>PathOf(t)).ToArray();File.WriteAllLines(Output+"/remaining_primitive_displays.txt",left);
            var renderers=All.Select(t=>t.GetComponent<Renderer>()).Where(r=>r!=null&&r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            var bad=renderers.Where(r=>r.sharedMaterials.Any(m=>m==null||m.shader==null||m.shader.name=="Hidden/InternalErrorShader")).Select(r=>PathOf(r.transform)).ToArray();
            var actors=All.Where(t=>t.name=="OwnedActorAppearance").ToArray();var failures=actors.Where(t=>t.GetComponentInChildren<Animator>()==null||t.GetComponentsInChildren<SkinnedMeshRenderer>().Any(s=>s.sharedMesh==null||s.bones.Any(b=>b==null))).Select(PathOf).ToList();
            foreach(var enemy in All.Select(t=>t.GetComponent<Oheangbu.Combat.EnemyController>()).Where(e=>e!=null))
            {var s=new SerializedObject(enemy);var r=s.FindProperty("_renderer").objectReferenceValue as Renderer;if(r!=null&&r.GetComponent<MeshFilter>()!=null&&r.GetComponent<MeshFilter>().sharedMesh==null)failures.Add("Enemy still bound to retired renderer: "+PathOf(enemy.transform));}
            var report=new Verification{primitiveDisplaysRemaining=left.Length,actorModels=actors.Length,missingMaterials=bad,actorFailures=failures.ToArray(),commit=Prologue.PrologueAudit.CommitRatio(),runtimePlayChecked=false};
            File.WriteAllText(Output+"/technical_checks.json",JsonUtility.ToJson(report,true));return JsonUtility.ToJson(report);
        }
        [Serializable] sealed class Verification{public int primitiveDisplaysRemaining,actorModels;public string[] missingMaterials,actorFailures;public float commit;public bool runtimePlayChecked;}
    }
}
