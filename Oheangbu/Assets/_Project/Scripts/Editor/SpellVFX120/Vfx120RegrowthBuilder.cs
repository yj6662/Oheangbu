using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120RegrowthBuilder
    {
        const string Folder="Assets/_Project/Art/SpellVFX120/Regrowth";
        const string Plant="Assets/SeyeonjeongPavilion/Texture/Plant/";
        [Serializable] sealed class Report
        {
            public string status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",glyph="걱",beforeJson,afterJson,snapshot;
            public string leafSource=Plant+"T_BrassicaNapus_Leaf_BC.png",stemSource=Plant+"T_BrassicaNapus_Stem_BC.png";
            public string patternSource="Assets/KoreanTraditionalPattern_Effect/Prefabs/Bottom/Bottom03-01.prefab";
            public string technicalCheck,gameplay="UNCONNECTED_CATALOG_PRESENTATION",art="AWAITING_USER_REVIEW";
            public int sprouts=3,renderers=9,stemVertices,leafVertices,stemTris,leafTris,totalBodyTris;
        }
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/008_AC71.asset");
            var report=new Report();
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(report.patternSource);
            var shader=Shader.Find("Oheangbu/VFX120/RegrowthLeaf");
            if(p==null || p.Glyph!="걱" || source==null || shader==null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Missing source or leaf shader error");
            report.beforeJson=JsonUtility.ToJson(p);
            string reportPath=Path.Combine(Vfx120Editor.Output,"regrowth_008_build.json");
            if(Vfx120Effect.IsRegrowth(p) && File.Exists(reportPath)) report.beforeJson=JsonUtility.FromJson<Report>(File.ReadAllText(reportPath)).beforeJson;
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals"));
            report.snapshot=Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals/008_AC71.asset.txt");
            if(!File.Exists(report.snapshot))File.Copy(AssetDatabase.GetAssetPath(p),report.snapshot);
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"Regrowth");
            var stem=Stem();var leaf=Leaf();
            report.stemVertices=stem.vertexCount;report.leafVertices=leaf.vertexCount;
            report.stemTris=stem.triangles.Length/3;report.leafTris=leaf.triangles.Length/3;
            report.totalBodyTris=3*(report.stemTris+2*report.leafTris);
            p.BodyMesh=Store(stem,Folder+"/VFX120_RegrowthStem_008.asset");
            p.AccentMesh=Store(leaf,Folder+"/VFX120_RegrowthLeaf_008.asset");
            var bark=AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/Botanical/Materials/M_Bamboo_0_Bark.mat");
            if(bark==null)throw new InvalidOperationException("Missing botanical material");
            var sm=new Material(bark){name="M_RegrowthStem_008"};
            SetPlant(sm,"Stem");p.BodyMaterial=Store(sm,Folder+"/M_RegrowthStem_008.mat");
            var lm=new Material(shader){name="M_RegrowthLeaf_008"};SetPlant(lm,"Leaf");
            lm.SetFloat("_AlphaClip",1);lm.SetFloat("_Cutoff",.33f);lm.SetFloat("_VeinOnly",0);
            p.InkMaterial=Store(lm,Folder+"/M_RegrowthLeaf_008.mat");
            p.Count=3;p.PartScale=Vector3.one;p.RibbonCount=0;p.NativeScale=.32f;
            p.NativeReplaceBody=false;p.NativeImpactPrefab=null;p.NativeFieldPrefab=null;
            p.NativeCastPrefab=Stamp(source);
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();report.afterJson=JsonUtility.ToJson(p);
            report.technicalCheck=Check(p);File.WriteAllText(reportPath,JsonUtility.ToJson(report,true));
            return report.status+"; "+report.technicalCheck+"; tris="+report.totalBodyTris;
        }
        static void SetPlant(Material m,string part)
        {
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Plant+"T_BrassicaNapus_"+part+"_BC.png");
            var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(Plant+"T_BrassicaNapus_"+part+"_N.png");
            if(texture==null || normal==null)throw new InvalidOperationException("Plant texture missing: "+part);
            m.SetTexture("_BaseMap",texture);m.SetTexture("_BumpMap",normal);
            m.SetColor("_BaseColor",Color.white);m.SetFloat("_Saturation",.65f);m.SetFloat("_TintStrength",.12f);
            m.SetFloat("_Visibility",1);m.SetFloat("_GroundY",-10000);
        }
        static T Store<T>(T value,string path) where T:UnityEngine.Object
        {
            var old=AssetDatabase.LoadAssetAtPath<T>(path);
            if(old==null){AssetDatabase.CreateAsset(value,path);return value;}
            EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;
        }
        static Mesh Stem()
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
            var points=new Vector3[13];var radii=new float[13];
            for(int i=0;i<13;i++){float t=i/12f;points[i]=new Vector3(.008f*Mathf.Sin(t*2.4f),t*.105f,0);radii[i]=Mathf.Lerp(.003f,.0018f,t);}
            Vfx120BambooGuardBuilder.Tube(points,radii,v,uv,tris);
            return Vfx120BambooGuardBuilder.MeshOf("VFX120_RegrowthStem_008",v,uv,tris);
        }
        static Mesh Leaf()
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
            for(int y=0;y<=8;y++)for(int x=0;x<=6;x++)
            {
                float u=x/6f,t=y/8f;
                v.Add(new Vector3((u-.5f)*.19f,t*.17f,.014f*Mathf.Sin(t*Mathf.PI)+.012f*Mathf.Pow((u-.5f)*2,2)));
                uv.Add(new Vector2(u,t));
                if(x>0 && y>0){int b=v.Count-9;tris.Add(b);tris.Add(b+1);tris.Add(b+7);tris.Add(b+1);tris.Add(b+8);tris.Add(b+7);}
            }
            return Vfx120BambooGuardBuilder.MeshOf("VFX120_RegrowthLeaf_008",v,uv,tris);
        }
        static GameObject Stamp(GameObject source)
        {
            var pattern=source.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Pattern");
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var root=new GameObject("KTP_RegrowthSeed_008");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);root.SetActive(false);
            try
            {
                var v=new List<Vector3>{new Vector3(-.5f,0,-.5f),new Vector3(.5f,0,-.5f),new Vector3(-.5f,0,.5f),new Vector3(.5f,0,.5f)};
                var mesh=Store(Vfx120BambooGuardBuilder.MeshOf("VFX120_RegrowthSeed_008",v,new List<Vector2>{Vector2.zero,Vector2.right,Vector2.up,Vector2.one},new List<int>{0,2,1,1,2,3}),Folder+"/VFX120_RegrowthSeed_008.asset");
                var stamp=UnityEngine.Object.Instantiate(pattern.gameObject,root.transform,false);stamp.name="SeedPattern";
                stamp.transform.localPosition=Vector3.zero;stamp.transform.localRotation=Quaternion.identity;stamp.transform.localScale=Vector3.one*(.22f/.32f);stamp.SetActive(true);
                var ps=stamp.GetComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=ps.main;main.startSize3D=false;main.startSize=1;main.startSpeed=0;main.startRotation3D=false;main.startRotation=0;main.playOnAwake=false;
                main.stopAction=ParticleSystemStopAction.None;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
                var shape=ps.shape;shape.enabled=false;var rotation=ps.rotationOverLifetime;rotation.enabled=false;
                var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=mesh;renderer.alignment=ParticleSystemRenderSpace.Local;
                root.SetActive(true);return PrefabUtility.SaveAsPrefabAsset(root,Folder+"/KTP_RegrowthSeed_008.prefab");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
        static string Check(Vfx120Profile p)
        {
            if(!Vfx120Effect.IsRegrowth(p))throw new InvalidOperationException("Unprepared regrowth");
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var go=new GameObject("Regrowth_Check");var foot=new GameObject("Foot_Check");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(foot,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.DemonstrationCues=false;
                e.Begin(Vector3.up,null,Vector3.forward*4,Color.white);e.SetRegrowthFootAnchor(foot.transform);
                e.Sample(.7f);if(e.RegrowthOpenedMask!=0)throw new InvalidOperationException("Opened without a tick");
                e.SignalRegrowthTick();e.Sample(1.05f);if(e.RegrowthOpenedMask!=1)throw new InvalidOperationException("Tick opened unrelated leaf");
                foot.transform.position=new Vector3(1,.2f,.3f);foot.transform.rotation=Quaternion.Euler(0,37,0);e.Sample(1.1f);
                if(Vector3.Distance(e.RegrowthInstance.transform.position,foot.transform.position)>.0001f || Quaternion.Angle(e.RegrowthInstance.transform.rotation,foot.transform.rotation)>.001f)
                    throw new InvalidOperationException("Foot attachment mismatch");
                Vector3 last=foot.transform.position;UnityEngine.Object.DestroyImmediate(foot);e.Sample(1.2f);
                if(Vector3.Distance(e.RegrowthInstance.transform.position,last)>.0001f)throw new InvalidOperationException("Lost-anchor snap");
                return "PASS_TICK_ISOLATION_FOOT_ATTACHMENT_AND_LOST_ANCHOR";
            }
            finally{if(foot!=null)UnityEngine.Object.DestroyImmediate(foot);UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
