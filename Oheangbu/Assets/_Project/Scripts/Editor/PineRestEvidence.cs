using System;
using System.IO;
using System.Linq;
using Oheangbu.App.Prologue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static partial class PineRestGameBuilder
    {
        static string EvidenceProps()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != Scene || scene.isDirty)
                throw new Exception("Saved Journey edit required");
            var session = Object.FindFirstObjectByType<PrologueSession>();
            var stand = GameObject.Find("Logging evidence box");
            var desk = GameObject.Find("Relay desk");
            if (stand == null || desk == null) throw new Exception("Existing ledger stand and desk required");
            var previous = GameObject.Find("Journey evidence presentation");
            if (previous != null) Object.DestroyImmediate(previous);
            var root = new GameObject("Journey evidence presentation");
            Material Mat(string name, float floor, float ceiling)
            {
                string path = Folder + "/" + name + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/Materials/Plaster.mat"));
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.SetFloat("_ToneFloor", floor);
                mat.SetFloat("_ToneCeiling", ceiling);
                mat.SetFloat("_RimStrength", .02f);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0);
                EditorUtility.SetDirty(mat);
                return mat;
            }
            var paper = Mat("LedgerPaper", .60f, .82f);
            var cover = Mat("LedgerCover", .08f, .25f);
            var thread = Mat("LedgerThread", .38f, .55f);
            GameObject Book(string name, Vector3 position, float yaw)
            {
                var book = new GameObject(name);
                book.transform.SetParent(root.transform);
                book.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
                void Part(string label, Vector3 local, Vector3 size, Material material)
                {
                    var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    part.name = label;
                    part.transform.SetParent(book.transform, false);
                    part.transform.localPosition = local;
                    part.transform.localScale = size;
                    part.GetComponent<Renderer>().sharedMaterial = material;
                    Object.DestroyImmediate(part.GetComponent<Collider>());
                }
                Part("Lower cover", new Vector3(0,.004f,0), new Vector3(.32f,.008f,.44f),cover);
                for (int i=0;i<6;i++)
                    Part("Paper gathering " + i,new Vector3(i%2*.001f,.012f+i*.0045f,0),new Vector3(.302f,.0038f,.421f),paper);
                Part("Upper cover",new Vector3(0,.041f,0),new Vector3(.32f,.008f,.44f),cover);
                for (int i=0;i<4;i++)
                {
                    float z=-.15f+i*.1f;
                    Part("Binding top " + i,new Vector3(-.143f,.047f,z),new Vector3(.034f,.003f,.004f),thread);
                    Part("Binding spine " + i,new Vector3(-.162f,.024f,z),new Vector3(.003f,.046f,.004f),thread);
                }
                return book;
            }
            var view = root.AddComponent<JourneyEvidenceView>();
            view.Session=session;
            view.EvidenceId="LoggingEvidence";
            view.ReportedId="j1:reported";
            var bounds=stand.GetComponentsInChildren<Renderer>().Select(r=>r.bounds).Aggregate((a,b)=>{a.Encapsulate(b);return a;});
            view.AtSite=Book("Ledger at logging site",new Vector3(bounds.center.x,bounds.max.y,bounds.center.z),-13);
            view.AtRecipient=Book("Ledger delivered to Jeongdam",new Vector3(desk.transform.position.x,desk.GetComponent<Renderer>().bounds.max.y,desk.transform.position.z),8);
            view.AtRecipient.SetActive(false);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            return "Bound ledger: logging site -> carried (hidden) -> relay desk, driven by committed progress";
        }

        static string EvidenceState()
        {
            var session=RoadRestSession();
            var view=Object.FindFirstObjectByType<JourneyEvidenceView>();
            if(view==null)throw new Exception("Evidence view missing");
            view.RefreshFromProgress();
            string result="site="+view.AtSite.activeSelf+"; recipient="+view.AtRecipient.activeSelf+"; collected="+session.Progress.completed.Contains(view.EvidenceId)+"; reported="+session.Progress.completed.Contains(view.ReportedId);
            File.AppendAllText(Path.GetFullPath("../Art/World/PineRest/evidence_presentation.txt"),result+"\n");
            return result;
        }

        static string EvidenceCapture()
        {
            RoadRestSession();
            var view=Object.FindFirstObjectByType<JourneyEvidenceView>();
            view.RefreshFromProgress();
            var book=view.AtRecipient.activeSelf?view.AtRecipient:view.AtSite;
            if(!book.activeSelf)throw new Exception("Ledger is currently carried");
            var camera=Camera.main;
            var position=camera.transform.position;
            var rotation=camera.transform.rotation;
            float aspect=camera.aspect;
            var previous=RenderTexture.active;
            var target=new RenderTexture(1920,1080,24);
            var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            string path="../Art/World/PineRest/ledger_"+(view.AtRecipient.activeSelf?"delivered":"site")+".png";
            try
            {
                camera.transform.position=book.transform.position+new Vector3(.65f,.72f,-.85f);
                camera.transform.LookAt(book.transform.position);
                camera.aspect=1920f/1080;
                target.Create();
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=target});
                RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,1920,1080),0,0);
                texture.Apply();
                File.WriteAllBytes(path,texture.EncodeToPNG());
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position,rotation);
                camera.aspect=aspect;
                RenderTexture.active=previous;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(texture);
            }
            return "Close inspection render (temporary camera pose restored): "+Path.GetFullPath(path);
        }
    }
}
