using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120WetRootBuilder
    {
        const string Folder="Assets/_Project/Art/SpellVFX120/WetRoot";
        [Serializable] sealed class Report
        {
            public string status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",glyph="강",beforeJson,afterJson,snapshot,technicalCheck;
            public string art="AWAITING_USER_REVIEW",gameplay="UNCONNECTED_CATALOG_PRESENTATION";
            public int vertices,bodyTris,totalTris,renderers=2,lineRenderers=3,maxLinePoints=48;
        }
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/006_AC15.asset");var shader=Shader.Find("Oheangbu/VFX120/WetRoot");if(p==null||shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Missing source/shader");
            var r=new Report();string path=Path.Combine(Vfx120Editor.Output,"wet_root_006_build.json");r.beforeJson=JsonUtility.ToJson(p);if(Vfx120Effect.IsWetRoot(p)&&File.Exists(path))r.beforeJson=JsonUtility.FromJson<Report>(File.ReadAllText(path)).beforeJson;
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals"));r.snapshot=Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals/006_AC15.asset.txt");if(!File.Exists(r.snapshot))File.Copy(AssetDatabase.GetAssetPath(p),r.snapshot);File.WriteAllText(path,JsonUtility.ToJson(r,true));
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"WetRoot");
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
            Vfx120BambooGuardBuilder.Tube(new[]{new Vector3(-.009f,-.006f,-.38f),new Vector3(0,.008f,-.26f),new Vector3(.005f,.012f,-.12f),Vector3.zero},new[]{.009f,.016f,.008f,.0006f},v,uv,t);
            var body=Vfx120BambooGuardBuilder.MeshOf("VFX120_WetThorn_006",v,uv,t);r.vertices=body.vertexCount;r.bodyTris=body.triangles.Length/3;r.totalTris=r.bodyTris+2;p.BodyMesh=Store(body,Folder+"/VFX120_WetThorn_006.asset");
            var mat=new Material(AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/Botanical/Materials/M_Bamboo_0_Bark.mat")){name="M_WetRoot_006",shader=shader};mat.SetColor("_Tint",new Color(.15f,.23f,.17f));mat.SetFloat("_TintStrength",.7f);mat.SetFloat("_BumpScale",.35f);p.BodyMaterial=Store(mat,Folder+"/M_WetRoot_006.mat");
            var line=new Material(p.PatternMaterial){name="M_WetRootLine_006",shader=Shader.Find("Oheangbu/VFX120/InkPigment")};string pattern="Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_33.png";line.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(pattern));line.SetTextureScale("_BaseMap",Vector2.one);line.SetTextureOffset("_BaseMap",Vector2.zero);line.SetFloat("_Pattern",.4f);p.InkMaterial=Store(line,Folder+"/M_WetRootLine_006.mat");
            var mark=new Material(AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/BambooBolt/M_BambooBoltContact_001.mat")){name="M_WetRootDrop_006"};p.PatternMaterial=Store(mark,Folder+"/M_WetRootDrop_006.mat");
            p.SourcePattern=pattern;p.Count=1;p.RibbonCount=0;p.PartScale=Vector3.one;p.NativeScale=.17f;p.NativeImpactScale=.22f;p.UseMist=false;p.NativeReplaceBody=false;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();r.afterJson=JsonUtility.ToJson(p);r.technicalCheck=Check(p);File.WriteAllText(path,JsonUtility.ToJson(r,true));return r.status+"; "+r.technicalCheck+"; bodyTris="+r.totalTris;
        }
        static T Store<T>(T value,string path)where T:UnityEngine.Object
        {var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old==null){AssetDatabase.CreateAsset(value,path);return value;}EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}
        static string Check(Vfx120Profile p)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("WetRoot_Check");var host=new GameObject("WetSurface_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.Begin(Vector3.up,null,new Vector3(0,1.1f,4),Color.white);e.Sample(.8f);
                if(e.WetRootVisiblePaths!=0||e.NativeImpact!=null)throw new InvalidOperationException("Invented surface contact");
                var paths=Vfx120WetRootReviewFixture.Paths();if(!e.SignalWetRootContact(host.transform,paths,Vector3.back,.6f)||e.SignalWetRootContact(null,paths,Vector3.back,.7f))throw new InvalidOperationException("Contact not idempotent");
                paths[0][11]=Vector3.one*999;e.Sample(.9f);if(e.WetRootVisiblePaths!=3)throw new InvalidOperationException("Missing root paths");
                var line=e.WetRootInstance.GetComponentsInChildren<LineRenderer>()[0];var old=line.GetPosition(15);if(old.magnitude>10)throw new InvalidOperationException("Borrowed caller array");host.transform.position+=Vector3.right;e.Sample(.9f);if(Vector3.Distance(line.GetPosition(15)-old,Vector3.right)>.0001f)throw new InvalidOperationException("Surface tracking mismatch");
                e.Sample(e.Life);if(e.WetRootInstance.GetComponentsInChildren<Renderer>().Any(x=>x.enabled))throw new InvalidOperationException("Lingering wet root");return "PASS_CONFIRMED_SURFACE_COPY_TRACKING_ABSORPTION_LIFETIME";
            }
            finally{UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
