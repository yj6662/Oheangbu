using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120WoodLiftBuilder
    {
        const string Folder="Assets/_Project/Art/SpellVFX120/WoodLift";
        [Serializable] sealed class Report
        {
            public string status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",glyph="국",beforeJson,afterJson,snapshot,technicalCheck;
            public string art="AWAITING_USER_REVIEW",gameplay="UNCONNECTED_NONCOMBAT_PRESENTATION";
            public int trunkVertices,trunkTris,deckVertices,deckTris,leafTris,totalTris,renderers=6;
        }
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/020_AD6D.asset");if(p==null)throw new InvalidOperationException("Missing profile");var r=new Report();string path=Path.Combine(Vfx120Editor.Output,"wood_lift_020_build.json");r.beforeJson=JsonUtility.ToJson(p);if(Vfx120Effect.IsWoodLift(p)&&File.Exists(path))r.beforeJson=JsonUtility.FromJson<Report>(File.ReadAllText(path)).beforeJson;
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals"));r.snapshot=Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals/020_AD6D.asset.txt");if(!File.Exists(r.snapshot))File.Copy(AssetDatabase.GetAssetPath(p),r.snapshot);File.WriteAllText(path,JsonUtility.ToJson(r,true));if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"WoodLift");
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
            for(int k=0;k<3;k++)
            {
                int offset=v.Count;for(int j=0;j<=20;j++)for(int i=0;i<12;i++)
                {
                    float y=j/20f*2.4f,twist=k*Mathf.PI*2/3+y*.95f,a=i/12f*Mathf.PI*2,radius=.10f+.016f*Mathf.Sin(y*4+k);
                    v.Add(new Vector3(Mathf.Cos(twist)*.085f+Mathf.Cos(a)*radius,y,Mathf.Sin(twist)*.085f+Mathf.Sin(a)*radius));uv.Add(new Vector2(i/12f,y*.7f));
                    if(j>0){int b=offset+(j-1)*12+i,c=offset+(j-1)*12+(i+1)%12;t.AddRange(new[]{b,c,b+12,c,c+12,b+12});}
                }
                float angle=k*Mathf.PI*2/3;Vector3 radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                Vfx120BambooGuardBuilder.Tube(new[]{Vector3.up*1.98f+radial*.05f,Vector3.up*2.12f+radial*.15f,Vector3.up*2.27f+radial*.31f,Vector3.up*2.36f+radial*.43f},new[]{.065f,.06f,.044f,.022f},v,uv,t);
            }
            var trunk=Vfx120BambooGuardBuilder.MeshOf("VFX120_WoodLiftTrunk_020",v,uv,t);r.trunkVertices=trunk.vertexCount;r.trunkTris=trunk.triangles.Length/3;p.BodyMesh=Store(trunk,Folder+"/VFX120_WoodLiftTrunk_020.asset");
            v.Clear();uv.Clear();t.Clear();v.Add(new Vector3(0,.015f,0));uv.Add(Vector2.one*.5f);v.Add(new Vector3(0,-.06f,0));uv.Add(Vector2.one*.5f);
            for(int i=0;i<20;i++)
            {
                float a=i/20f*Mathf.PI*2,rad=.45f+.028f*Mathf.Sin(a*5)+.012f*Mathf.Cos(a*3);var q=new Vector3(Mathf.Cos(a)*rad,0,Mathf.Sin(a)*rad*.86f);v.Add(q+Vector3.up*.015f);uv.Add(new Vector2(q.x+.5f,q.z+.5f));v.Add(q-Vector3.up*.06f);uv.Add(new Vector2(i/20f,.2f));
            }
            for(int i=0;i<20;i++){int a=2+i*2,b=2+((i+1)%20)*2;t.AddRange(new[]{0,b,a,1,a+1,b+1,a,b,a+1,b,b+1,a+1});}
            var deck=Vfx120BambooGuardBuilder.MeshOf("VFX120_WoodLiftDeck_020",v,uv,t);r.deckVertices=deck.vertexCount;r.deckTris=deck.triangles.Length/3;p.AccentMesh=Store(deck,Folder+"/VFX120_WoodLiftDeck_020.asset");
            var material=new Material(AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/Botanical/Materials/M_Bamboo_0_Bark.mat")){name="M_WoodLift_020"};material.SetColor("_Tint",new Color(.35f,.30f,.2f));material.SetFloat("_TintStrength",.28f);p.BodyMaterial=Store(material,Folder+"/M_WoodLift_020.mat");
            var leaf=new Material(AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/SeedPod/M_SeedPodLeaf_004.mat")){name="M_WoodLiftLeaf_020"};p.InkMaterial=Store(leaf,Folder+"/M_WoodLiftLeaf_020.mat");p.GuardianMeshes=new[]{AssetDatabase.LoadAssetAtPath<Mesh>(Vfx120Editor.AssetRoot+"/BambooBolt/VFX120_BambooBoltLeaf_001.asset")};r.leafTris=p.GuardianMeshes[0].triangles.Length/3;
            var mark=new Material(AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/BambooBolt/M_BambooBoltContact_001.mat")){name="M_WoodLiftWake_020"};p.PatternMaterial=Store(mark,Folder+"/M_WoodLiftWake_020.mat");r.totalTris=r.trunkTris+r.deckTris+3*r.leafTris+2;
            p.SourcePattern="Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_192.png";p.Count=1;p.RibbonCount=0;p.PartScale=Vector3.one;p.NativeScale=.28f;p.NativeImpactPrefab=null;p.NativeFieldPrefab=null;p.UseMist=false;p.NativeReplaceBody=false;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();r.afterJson=JsonUtility.ToJson(p);r.technicalCheck=Check(p);File.WriteAllText(path,JsonUtility.ToJson(r,true));return r.status+"; "+r.technicalCheck+"; tris="+r.totalTris;
        }
        static T Store<T>(T value,string path)where T:UnityEngine.Object
        {var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old==null){AssetDatabase.CreateAsset(value,path);return value;}EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}
        static string Check(Vfx120Profile p)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("WoodLift_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.Begin(Vector3.up,null,Vector3.forward*4,Color.white);e.Sample(.3f);var renderers=e.WoodLiftInstance.GetComponentsInChildren<MeshRenderer>();if(renderers.Any(x=>x.enabled))throw new InvalidOperationException("Invented lift plan");
                if(!e.ConfigureWoodLift(Vector3.zero,2.1f)||e.SetWoodLiftHeight(3))throw new InvalidOperationException("Height limit failure");foreach(float height in new[]{.1f,1,2.1f}){if(!e.SetWoodLiftHeight(height))throw new InvalidOperationException("Height refused");e.Sample(1);if(Mathf.Abs(e.WoodLiftHeight-height)>.001f)throw new InvalidOperationException("Height drift");}
                if(e.NativeImpact!=null||e.WoodLiftInstance.GetComponentsInChildren<Collider>().Length!=0)throw new InvalidOperationException("Combat/collision content in utility lift");if(!e.ReleaseWoodLift(3.5f)||e.ReleaseWoodLift(3.6f))throw new InvalidOperationException("Release not idempotent");e.Sample(e.Life);if(renderers.Any(x=>x.enabled))throw new InvalidOperationException("Lingering lift");return "PASS_SUPPLIED_HEIGHT_NO_AUTOMATIC_MOVEMENT_RELEASE_LIFETIME";
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
