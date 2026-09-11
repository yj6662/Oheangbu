using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Spellcraft;
namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120StakeFieldBuilder
    {
        const string Folder="Assets/_Project/Art/SpellVFX120/StakeField";
        [Serializable] sealed class Report
        {
            public string status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",glyph="곳",beforeJson,afterJson,snapshot,technicalCheck;
            public string art="AWAITING_USER_REVIEW",gameplay="UNCONNECTED_CATALOG_PRESENTATION";
            public int stakeVertices,stakeTris,totalTris,renderers=20;
        }
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/017_ACF3.asset");if(p==null)throw new InvalidOperationException("Missing profile");var r=new Report();string path=Path.Combine(Vfx120Editor.Output,"stake_field_017_build.json");r.beforeJson=JsonUtility.ToJson(p);if(Vfx120Effect.IsStakeField(p)&&File.Exists(path))r.beforeJson=JsonUtility.FromJson<Report>(File.ReadAllText(path)).beforeJson;
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals"));r.snapshot=Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals/017_ACF3.asset.txt");if(!File.Exists(r.snapshot))File.Copy(AssetDatabase.GetAssetPath(p),r.snapshot);File.WriteAllText(path,JsonUtility.ToJson(r,true));if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"StakeField");
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
            Vfx120BambooGuardBuilder.Tube(new[]{Vector3.zero,new Vector3(.018f,.28f,0),new Vector3(-.012f,.66f,.01f),new Vector3(.02f,1,0)},new[]{.055f,.05f,.033f,.0008f},v,uv,t);
            for(int i=0;i<2;i++){float side=i==0?1:-1;Vfx120BambooGuardBuilder.Tube(new[]{new Vector3(0,.36f+i*.19f,0),new Vector3(side*.10f,.52f+i*.14f,.025f),new Vector3(side*.14f,.62f+i*.12f,.01f)},new[]{.025f,.014f,.0008f},v,uv,t);}
            var mesh=Vfx120BambooGuardBuilder.MeshOf("VFX120_HardStake_017",v,uv,t);r.stakeVertices=mesh.vertexCount;r.stakeTris=mesh.triangles.Length/3;r.totalTris=19*r.stakeTris+2;p.BodyMesh=Store(mesh,Folder+"/VFX120_HardStake_017.asset");
            var material=new Material(AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/Botanical/Materials/M_Bamboo_0_Bark.mat")){name="M_HardStake_017"};material.SetColor("_Tint",new Color(.3f,.25f,.16f));material.SetFloat("_TintStrength",.42f);material.SetFloat("_Saturation",.4f);p.BodyMaterial=Store(material,Folder+"/M_HardStake_017.mat");
            var mark=new Material(AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/BambooBolt/M_BambooBoltContact_001.mat")){name="M_StakeRemnant_017"};p.PatternMaterial=Store(mark,Folder+"/M_StakeRemnant_017.mat");p.AccentMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Vfx120Editor.AssetRoot+"/CompanionSeeds/VFX120_CompanionLeafMark_012.asset");
            p.SourcePattern="Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_192.png";p.Count=19;p.RibbonCount=0;p.PartScale=Vector3.one;p.NativeScale=.28f;p.NativeImpactScale=.36f;p.UseMist=false;p.NativeReplaceBody=false;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();r.afterJson=JsonUtility.ToJson(p);r.technicalCheck=Check(p);File.WriteAllText(path,JsonUtility.ToJson(r,true));return r.status+"; "+r.technicalCheck+"; tris="+r.totalTris;
        }
        static T Store<T>(T value,string path)where T:UnityEngine.Object
        {var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old==null){AssetDatabase.CreateAsset(value,path);return value;}EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}
        static string Check(Vfx120Profile p)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("StakeField_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;var plan=new AreaImpactPlan{Shape=AreaShape.Circle,Point=new Vector3(0,0,4),Radius=1.8f,Delay=.35f};e.SetAreaPlan(plan);e.Begin(Vector3.up,null,plan.Point,Color.white);e.Sample(1);
                var roots=e.StakeFieldInstance.GetComponentsInChildren<MeshRenderer>();var poses=roots.Select(x=>x.transform.localToWorldMatrix).ToArray();e.Sample(1.2f);if(!poses.SequenceEqual(roots.Select(x=>x.transform.localToWorldMatrix)))throw new InvalidOperationException("Idle stakes moved");
                if(e.SignalStakeContact(Vector3.one*99,1.3f)||e.SignalStakeContact(plan.Point,.1f))throw new InvalidOperationException("Invalid contact accepted");
                if(!e.SignalStakeContact(Vfx120StakeFieldReviewFixture.Contact(0),1.4f))throw new InvalidOperationException("Contact refused");e.Sample(1.45f);if(e.StakeActiveFeedback!=2||e.StakeContactCount!=1)throw new InvalidOperationException("Nonlocal feedback");
                e.Sample(1.8f);if(e.StakeActiveFeedback!=0)throw new InvalidOperationException("Feedback did not settle");e.Sample(e.Life);if(roots.Any(x=>x.enabled))throw new InvalidOperationException("Lingering stake");return "PASS_FIXED_FIELD_LOCAL_TWO_STAKE_FEEDBACK_SETTLE_LIFETIME";
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
