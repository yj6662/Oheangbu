using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Spellcraft;
namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120RootLiftBuilder
    {
        const string Folder="Assets/_Project/Art/SpellVFX120/RootLift";
        [Serializable] sealed class Report
        {
            public string status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",glyph="곤",beforeJson,afterJson,snapshot,technicalCheck;
            public string source="Assets/_Project/Art/SpellVFX120/Botanical/Meshes/Mesh_Root.asset",art="AWAITING_USER_REVIEW",gameplay="UNCONNECTED_CATALOG_PRESENTATION";
            public int rootVertices,rootTris,totalTris,renderers=6;
        }
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/015_ACE4.asset");var r=new Report();var source=AssetDatabase.LoadAssetAtPath<Mesh>(r.source);var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120BotanicalBuilder.RootPath);
            if(p==null||source==null||prefab==null)throw new InvalidOperationException("Missing source");string path=Path.Combine(Vfx120Editor.Output,"root_lift_015_build.json");r.beforeJson=JsonUtility.ToJson(p);if(Vfx120Effect.IsRootLift(p)&&File.Exists(path))r.beforeJson=JsonUtility.FromJson<Report>(File.ReadAllText(path)).beforeJson;
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals"));r.snapshot=Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals/015_ACE4.asset.txt");if(!File.Exists(r.snapshot))File.Copy(AssetDatabase.GetAssetPath(p),r.snapshot);File.WriteAllText(path,JsonUtility.ToJson(r,true));
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"RootLift");var mesh=UnityEngine.Object.Instantiate(source);mesh.name="VFX120_RootPalm_015";r.rootVertices=mesh.vertexCount;r.rootTris=mesh.triangles.Length/3;r.totalTris=3*r.rootTris+6;p.BodyMesh=Store(mesh,Folder+"/VFX120_RootPalm_015.asset");
            var material=new Material(prefab.GetComponentInChildren<MeshRenderer>().sharedMaterial){name="M_RootPalm_015"};material.SetColor("_Tint",new Color(.27f,.35f,.2f));material.SetFloat("_TintStrength",.3f);p.BodyMaterial=Store(material,Folder+"/M_RootPalm_015.mat");
            var mark=new Material(AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/BambooBolt/M_BambooBoltContact_001.mat")){name="M_RootWake_015"};p.PatternMaterial=Store(mark,Folder+"/M_RootWake_015.mat");p.AccentMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Vfx120Editor.AssetRoot+"/CompanionSeeds/VFX120_CompanionLeafMark_012.asset");
            p.SourcePattern="Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_192.png";p.Count=3;p.RibbonCount=0;p.PartScale=Vector3.one;p.NativeScale=.3f;p.NativeImpactScale=.45f;p.UseMist=false;p.NativeReplaceBody=false;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();r.afterJson=JsonUtility.ToJson(p);r.technicalCheck=Check(p);File.WriteAllText(path,JsonUtility.ToJson(r,true));return r.status+"; "+r.technicalCheck+"; tris="+r.totalTris;
        }
        static T Store<T>(T value,string path)where T:UnityEngine.Object
        {var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old==null){AssetDatabase.CreateAsset(value,path);return value;}EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}
        static string Check(Vfx120Profile p)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("RootLift_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.Begin(Vector3.up,null,Vector3.forward*4,Color.white);if(e.RootLiftConfigured||e.PartCount!=0)throw new InvalidOperationException("Invalid plan revived body");
                var plan=new AreaImpactPlan{Shape=AreaShape.Circle,Point=new Vector3(.5f,0,4),Radius=1.4f,Delay=.52f};e.SetAreaPlan(plan);e.Begin(Vector3.up,null,plan.Point,Color.white);e.Sample(.52f);
                if(e.RootLiftRise<.999f||e.NativeImpact==null||Mathf.Abs(e.NativeImpactStartedAt-.52f)>.001f)throw new InvalidOperationException("Root lift timing mismatch");
                var scales=e.RootLiftInstance.GetComponentsInChildren<MeshRenderer>().Select(x=>x.transform.localScale).ToArray();e.Sample(1.2f);if(e.RootLiftRise>.001f)throw new InvalidOperationException("Root did not withdraw");
                if(!scales.SequenceEqual(e.RootLiftInstance.GetComponentsInChildren<MeshRenderer>().Select(x=>x.transform.localScale)))throw new InvalidOperationException("Animated root scaling");e.Sample(e.Life);if(e.RootLiftInstance.GetComponentsInChildren<MeshRenderer>().Any(x=>x.enabled))throw new InvalidOperationException("Lingering root");
                return "PASS_CIRCLE_PLAN_FIXED_SCALE_LIFT_WITHDRAWAL_LIFETIME";
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
