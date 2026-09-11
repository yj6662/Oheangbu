using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120SeedTransferBuilder
    {
        const string Folder="Assets/_Project/Art/SpellVFX120/SeedTransfer";
        [Serializable] sealed class Report
        {
            public string status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",glyph="간",beforeJson,afterJson,snapshot,technicalCheck;
            public string art="AWAITING_USER_REVIEW",gameplay="UNCONNECTED_CATALOG_PRESENTATION";
            public int thornVertices,thornTris,shellVertices,shellTris,totalTris,renderers=4;
        }
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/003_AC04.asset");
            var source=AssetDatabase.LoadAssetAtPath<Mesh>(Vfx120Editor.AssetRoot+"/CompanionSeeds/VFX120_CompanionSeedShell_012.asset");
            var bark=AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/Botanical/Materials/M_Bamboo_0_Bark.mat");
            var shader=Shader.Find("Oheangbu/VFX120/SeedLeafMark");
            if(p==null||source==null||bark==null||shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Missing source or shader error");
            var r=new Report();string path=Path.Combine(Vfx120Editor.Output,"seed_transfer_003_build.json");r.beforeJson=JsonUtility.ToJson(p);
            if(Vfx120Effect.IsSeedTransfer(p)&&File.Exists(path))r.beforeJson=JsonUtility.FromJson<Report>(File.ReadAllText(path)).beforeJson;
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals"));r.snapshot=Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals/003_AC04.asset.txt");
            if(!File.Exists(r.snapshot))File.Copy(AssetDatabase.GetAssetPath(p),r.snapshot);File.WriteAllText(path,JsonUtility.ToJson(r,true));
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"SeedTransfer");
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
            Vfx120BambooGuardBuilder.Tube(new[]{new Vector3(0,0,-.31f),new Vector3(.012f,.008f,-.22f),new Vector3(.005f,.025f,-.10f),Vector3.zero},new[]{.018f,.025f,.013f,.0007f},v,uv,t);
            for(int i=0;i<3;i++)
            {
                float z=-.25f+i*.055f;float sign=i%2==0?1:-1;
                Vfx120BambooGuardBuilder.Tube(new[]{new Vector3(0,.005f,z),new Vector3(sign*.03f,.025f,z-.02f),new Vector3(sign*.055f,.018f,z-.055f)},new[]{.007f,.005f,.0006f},v,uv,t);
            }
            var thorn=Vfx120BambooGuardBuilder.MeshOf("VFX120_SeedThorn_003",v,uv,t);r.thornVertices=thorn.vertexCount;r.thornTris=thorn.triangles.Length/3;
            p.BodyMesh=Store(thorn,Folder+"/VFX120_SeedThorn_003.asset");
            var shell=UnityEngine.Object.Instantiate(source);shell.name="VFX120_TransferSeedShell_003";
            var sv=shell.vertices;for(int i=0;i<sv.Length;i++)sv[i]=Vector3.Scale(sv[i],new Vector3(1.6f,1.6f,.8f));shell.vertices=sv;shell.RecalculateBounds();shell.RecalculateNormals();shell.RecalculateTangents();
            r.shellVertices=shell.vertexCount;r.shellTris=shell.triangles.Length/3;r.totalTris=r.thornTris+2*r.shellTris+2;p.AccentMesh=Store(shell,Folder+"/VFX120_TransferSeedShell_003.asset");
            var mat=new Material(bark){name="M_TransferSeed_003"};mat.SetColor("_Tint",new Color(.57f,.47f,.24f));mat.SetFloat("_TintStrength",.45f);mat.SetFloat("_Saturation",.5f);p.BodyMaterial=Store(mat,Folder+"/M_TransferSeed_003.mat");
            var mark=new Material(p.PatternMaterial){name="M_TransferBud_003",shader=shader};string pattern="Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_192.png";
            mark.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(pattern));mark.SetTextureScale("_BaseMap",new Vector2(.5f,.65f));mark.SetTextureOffset("_BaseMap",new Vector2(.25f,.18f));p.PatternMaterial=Store(mark,Folder+"/M_TransferBud_003.mat");
            p.SourcePattern=pattern;p.Count=1;p.RibbonCount=0;p.PartScale=Vector3.one;p.NativeScale=.22f;p.NativeImpactScale=.38f;p.UseMist=false;p.NativeReplaceBody=false;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();r.afterJson=JsonUtility.ToJson(p);r.technicalCheck=Check(p);File.WriteAllText(path,JsonUtility.ToJson(r,true));return r.status+"; "+r.technicalCheck+"; tris="+r.totalTris;
        }
        static T Store<T>(T value,string path)where T:UnityEngine.Object
        {var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old==null){AssetDatabase.CreateAsset(value,path);return value;}EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}
        static string Check(Vfx120Profile p)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("SeedTransfer_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.SetImpactClock(.6f);e.Begin(Vector3.up,null,new Vector3(0,1.1f,4),Color.white);
                e.Sample(1.4f);if(e.SeedTransferStartedAt>=0)throw new InvalidOperationException("Invented transfer");
                if(e.ConfirmSeedTransferHit(Vector3.one))throw new InvalidOperationException("Hit without transfer");
                if(!e.RequestSeedTransfer(null,Vfx120SeedTransferReviewFixture.Recipient,1.3f))throw new InvalidOperationException("Transfer refused");
                if(e.RequestSeedTransfer(null,Vector3.one,1.4f))throw new InvalidOperationException("Duplicate transfer");
                e.Sample(1.575f);if(e.SeedTransferPosition.y<1.5f)throw new InvalidOperationException("Arc missing");
                e.Sample(1.85f);if(Vector3.Distance(e.SeedTransferPosition,Vfx120SeedTransferReviewFixture.Recipient)>.0001f)throw new InvalidOperationException("Endpoint mismatch");
                if(!e.ConfirmSeedTransferHit(Vfx120SeedTransferReviewFixture.Recipient,1.85f)||e.ConfirmSeedTransferHit(Vector3.zero,1.9f))throw new InvalidOperationException("Hit confirmation not idempotent");
                e.Sample(e.Life);if(e.SeedTransferInstance.GetComponentsInChildren<MeshRenderer>().Any(x=>x.enabled))throw new InvalidOperationException("Lingering renderers");
                return "PASS_NO_AUTOMATIC_TRANSFER_ARC_ENDPOINT_CONFIRMED_HIT_LIFETIME";
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
