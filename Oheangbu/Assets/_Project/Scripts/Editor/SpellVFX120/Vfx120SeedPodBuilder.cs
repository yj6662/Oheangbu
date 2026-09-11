using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120SeedPodBuilder
    {
        const string Folder="Assets/_Project/Art/SpellVFX120/SeedPod";
        [Serializable] sealed class Report
        {
            public string status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",glyph="감",beforeJson,afterJson,snapshot,technicalCheck;
            public string art="AWAITING_USER_REVIEW",gameplay="UNCONNECTED_CATALOG_PRESENTATION";
            public int podVertices,podTris,knotVertices,knotTris,leafTris,totalTris,renderers=10;
        }
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/004_AC10.asset");var bark=AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/Botanical/Materials/M_Bamboo_0_Bark.mat");
            var shader=Shader.Find("Oheangbu/VFX120/InkPigment");if(p==null||bark==null||shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Missing source/shader");
            var r=new Report();string path=Path.Combine(Vfx120Editor.Output,"seed_pod_004_build.json");r.beforeJson=JsonUtility.ToJson(p);
            if(Vfx120Effect.IsSeedPod(p)&&File.Exists(path))r.beforeJson=JsonUtility.FromJson<Report>(File.ReadAllText(path)).beforeJson;
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals"));r.snapshot=Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals/004_AC10.asset.txt");if(!File.Exists(r.snapshot))File.Copy(AssetDatabase.GetAssetPath(p),r.snapshot);File.WriteAllText(path,JsonUtility.ToJson(r,true));
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"SeedPod");
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
            for(int j=0;j<=10;j++)for(int i=0;i<=16;i++)
            {
                float a=i/16f*Mathf.PI*2,b=j/10f*Mathf.PI;v.Add(new Vector3(Mathf.Cos(a)*Mathf.Sin(b)*.085f,Mathf.Cos(b)*.115f,Mathf.Sin(a)*Mathf.Sin(b)*.035f));uv.Add(new Vector2(i/16f,j/10f));
                if(i>0&&j>0){int q=(j-1)*17+i-1;t.AddRange(new[]{q,q+17,q+1,q+1,q+17,q+18});}
            }
            var pod=Vfx120BambooGuardBuilder.MeshOf("VFX120_SeedPod_004",v,uv,t);r.podVertices=pod.vertexCount;r.podTris=pod.triangles.Length/3;p.BodyMesh=Store(pod,Folder+"/VFX120_SeedPod_004.asset");
            v.Clear();uv.Clear();t.Clear();var points=new Vector3[13];var radius=new float[13];
            for(int j=0;j<13;j++){float u=j/12f,a=Mathf.Lerp(-.9f,3.9f,u);points[j]=new Vector3(Mathf.Cos(a)*.065f,.06f+Mathf.Sin(a)*.065f,.025f+u*.006f);radius[j]=.003f+.005f*Mathf.Sin(u*Mathf.PI);}
            Vfx120BambooGuardBuilder.Tube(points,radius,v,uv,t);var knot=Vfx120BambooGuardBuilder.MeshOf("VFX120_SeedPodKnot_004",v,uv,t);r.knotVertices=knot.vertexCount;r.knotTris=knot.triangles.Length/3;p.AccentMesh=Store(knot,Folder+"/VFX120_SeedPodKnot_004.asset");
            p.GuardianMeshes=new[]{AssetDatabase.LoadAssetAtPath<Mesh>(Vfx120Editor.AssetRoot+"/BambooBolt/VFX120_BambooBoltLeaf_001.asset")};r.leafTris=p.GuardianMeshes[0].triangles.Length/3;r.totalTris=r.podTris+4*r.knotTris+4*r.leafTris+2;
            var material=new Material(bark){name="M_SeedPod_004"};material.SetColor("_Tint",new Color(.29f,.38f,.23f));material.SetFloat("_TintStrength",.4f);p.BodyMaterial=Store(material,Folder+"/M_SeedPod_004.mat");
            var leaf=new Material(AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/BambooBolt/M_BambooBoltLeaf_001.mat")){name="M_SeedPodLeaf_004",shader=bark.shader};leaf.SetColor("_Tint",new Color(.19f,.46f,.35f));leaf.SetFloat("_TintStrength",.7f);p.InkMaterial=Store(leaf,Folder+"/M_SeedPodLeaf_004.mat");
            var mark=new Material(p.PatternMaterial){name="M_SeedPodSeal_004",shader=shader};string pattern="Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_3.png";mark.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(pattern));mark.SetTextureScale("_BaseMap",Vector2.one);mark.SetTextureOffset("_BaseMap",Vector2.zero);p.PatternMaterial=Store(mark,Folder+"/M_SeedPodSeal_004.mat");
            p.SourcePattern=pattern;p.Count=1;p.RibbonCount=0;p.PartScale=Vector3.one;p.NativeScale=.22f;p.NativeImpactScale=.65f;p.UseMist=false;p.NativeReplaceBody=false;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();r.afterJson=JsonUtility.ToJson(p);r.technicalCheck=Check(p);File.WriteAllText(path,JsonUtility.ToJson(r,true));return r.status+"; "+r.technicalCheck+"; tris="+r.totalTris;
        }
        static T Store<T>(T value,string path)where T:UnityEngine.Object
        {var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old==null){AssetDatabase.CreateAsset(value,path);return value;}EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}
        static string Check(Vfx120Profile p)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("Pod_Check");var host=new GameObject("PodHost_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.Begin(Vector3.up,null,Vfx120SeedPodReviewFixture.Contact,Color.white);e.Sample(1);
                if(e.DetonateSeedPod())throw new InvalidOperationException("Detonated before attachment");
                if(!e.AttachSeedPod(host.transform,Vfx120SeedPodReviewFixture.Contact,Vector3.back,.55f))throw new InvalidOperationException("Attachment refused");e.Sample(1.1f);var old=e.SeedPodPosition;host.transform.position+=Vector3.right;e.Sample(1.2f);
                if(Vector3.Distance(e.SeedPodPosition-old,Vector3.right)>.0001f)throw new InvalidOperationException("Host tracking mismatch");
                if(e.SeedPodDetonatedAt>=0||e.NativeImpact!=null)throw new InvalidOperationException("Invented detonation");
                if(!e.DetonateSeedPod(1.65f)||e.DetonateSeedPod(1.7f))throw new InvalidOperationException("Detonation not idempotent");e.Sample(1.8f);
                if(e.NativeImpact==null||Mathf.Abs(e.NativeImpactStartedAt-1.65f)>.001f)throw new InvalidOperationException("KTP impact clock mismatch");
                e.Sample(e.Life);if(e.SeedPodInstance.GetComponentsInChildren<MeshRenderer>().Any(x=>x.enabled))throw new InvalidOperationException("Lingering pod");return "PASS_ATTACHMENT_TRACKING_CONFIRMED_DETONATION_AND_LIFETIME";
            }
            finally{UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
