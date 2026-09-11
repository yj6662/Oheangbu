using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120WoodWardBuilder
    {
        const string Folder = "Assets/_Project/Art/SpellVFX120/WoodWard";
        [Serializable] sealed class Report
        {
            public string status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",glyph="구",beforeJson,afterJson,snapshot;
            public string patternSource,footprintSource,bodyMaterialSource,technicalCheck;
            public string gameplay="UNCONNECTED_CATALOG_PRESENTATION",art="AWAITING_USER_REVIEW";
            public int supports=6,bodyVertices,panelVertices,bodyTris,panelTris,totalBodyTris,renderers=12,footprintSystems;
        }
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required");
            string root=Vfx120Editor.AssetRoot;
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(root+"/Profiles/019_AD6C.asset");
            var r=new Report {patternSource="Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_126.png",
                footprintSource="Assets/KoreanTraditionalPattern_Effect/Prefabs/Bottom/Bottom04-01.prefab",
                bodyMaterialSource=root+"/Botanical/Materials/M_Bamboo_0_Bark.mat"};
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(r.patternSource);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(r.footprintSource);
            var bark=AssetDatabase.LoadAssetAtPath<Material>(r.bodyMaterialSource);
            if(p==null || p.Glyph!="구" || texture==null || source==null || bark==null) throw new InvalidOperationException("Missing 구/KTP/bamboo source");
            r.beforeJson=JsonUtility.ToJson(p);
            string reportPath=Path.Combine(Vfx120Editor.Output,"wood_ward_019_build.json");
            if(Vfx120Effect.IsWoodWard(p) && File.Exists(reportPath)) r.beforeJson=JsonUtility.FromJson<Report>(File.ReadAllText(reportPath)).beforeJson;
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals"));
            r.snapshot=Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals/019_AD6C.asset.txt");
            if(!File.Exists(r.snapshot)) File.Copy(AssetDatabase.GetAssetPath(p),r.snapshot);
            if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(root,"WoodWard");
            var frame=Frame(); var panel=Panel(false); var floor=Panel(true);
            r.bodyVertices=frame.vertexCount;r.panelVertices=panel.vertexCount;
            r.bodyTris=frame.triangles.Length/3;r.panelTris=panel.triangles.Length/3;r.totalBodyTris=(r.bodyTris+r.panelTris)*6;
            p.BodyMesh=Store(frame,Folder+"/VFX120_WoodWardSupport_019.asset");
            p.AccentMesh=Store(panel,Folder+"/VFX120_WoodWardPanel_019.asset");
            floor=Store(floor,Folder+"/VFX120_WoodWardFootprint_019.asset");
            var material=new Material(bark){name="M_WoodWard_019"};
            material.SetFloat("_Saturation",.55f);material.SetFloat("_TintStrength",.25f);
            p.BodyMaterial=Store(material,Folder+"/M_WoodWard_019.mat");
            var pattern=new Material(p.PatternMaterial){name="M_WoodWardPanel_019"};
            pattern.SetTexture("_BaseMap",texture);pattern.SetFloat("_Pattern",1);pattern.SetFloat("_Body",0);
            p.PatternMaterial=Store(pattern,Folder+"/M_WoodWardPanel_019.mat");
            p.SourcePattern=r.patternSource;p.Count=6;p.RibbonCount=0;p.PartScale=Vector3.one;p.NativeScale=.32f;
            p.NativeReplaceBody=false;p.NativeImpactPrefab=null;
            p.NativeFieldPrefab=Footprints(source,floor,p.Size,p.NativeScale);p.NativeFieldRole=Vfx120TraditionalMotif.Role.Shield;
            r.footprintSystems=p.NativeFieldPrefab.GetComponentsInChildren<ParticleSystem>(true).Length;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();r.afterJson=JsonUtility.ToJson(p);r.technicalCheck=Check(p);
            File.WriteAllText(reportPath,JsonUtility.ToJson(r,true));return r.status+"; "+r.technicalCheck+"; totalBodyTris="+r.totalBodyTris;
        }
        static T Store<T>(T value,string path) where T:UnityEngine.Object
        {
            var old=AssetDatabase.LoadAssetAtPath<T>(path);
            if(old==null){AssetDatabase.CreateAsset(value,path);return value;}
            EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;
        }
        static Mesh Frame()
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
            foreach(float side in new[]{-1f,1f})
            {
                var path=new Vector3[19];var widths=new float[19];
                for(int j=0;j<path.Length;j++)
                {float t=j/18f;path[j]=new Vector3(side*(.43f-.08f*Mathf.Sin(t*Mathf.PI)),t*1.55f,.04f*Mathf.Sin(t*3));widths[j]=(.024f-.008f*t)*(j%6==0?1.3f:1);}
                Vfx120BambooGuardBuilder.Tube(path,widths,v,uv,tris);
            }
            foreach(float height in new[]{.35f,1.42f})
            {
                var path=new Vector3[17];var widths=new float[17];
                for(int j=0;j<path.Length;j++)
                {float t=j/16f;path[j]=new Vector3(Mathf.Lerp(-.43f,.43f,t),height+Mathf.Sin(t*Mathf.PI)*.08f,-.015f);widths[j]=.012f;}
                Vfx120BambooGuardBuilder.Tube(path,widths,v,uv,tris);
            }
            return Vfx120BambooGuardBuilder.MeshOf("VFX120_WoodWardSupport_019",v,uv,tris);
        }
        static Mesh Panel(bool floor)
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
            for(int y=0;y<=4;y++)for(int x=0;x<=4;x++)
            {
                float u=x/4f,t=y/4f;
                v.Add(floor?new Vector3(u-.5f,0,t-.5f):new Vector3((u-.5f)*.75f,.46f+t*.9f,-.025f-.045f*Mathf.Sin(u*Mathf.PI)));
                uv.Add(new Vector2(u,t));
                if(x>0 && y>0){int b=v.Count-7;tris.Add(b);tris.Add(b+1);tris.Add(b+5);tris.Add(b+1);tris.Add(b+6);tris.Add(b+5);}
            }
            if(floor) for(int i=0;i<tris.Count;i+=3){int swap=tris[i+1];tris[i+1]=tris[i+2];tris[i+2]=swap;}
            return Vfx120BambooGuardBuilder.MeshOf(floor?"VFX120_WoodWardFootprint_019":"VFX120_WoodWardPanel_019",v,uv,tris);
        }
        static GameObject Footprints(GameObject source,Mesh floor,float radius,float nativeScale)
        {
            var sourcePattern=source.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Pattern").gameObject;
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var root=new GameObject("KTP_WoodWardFootprints_019");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);root.SetActive(false);
            try
            {
                for(int i=0;i<6;i++)
                {
                    var stamp=UnityEngine.Object.Instantiate(sourcePattern,root.transform,false);stamp.name="Footprint_"+i;
                    stamp.transform.localPosition=Vfx120Effect.WardAnchor(i,radius)/nativeScale;
                    stamp.transform.localRotation=Quaternion.identity;stamp.transform.localScale=Vector3.one*(.48f/nativeScale);stamp.SetActive(true);
                    var ps=stamp.GetComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main=ps.main;main.startSize3D=false;main.startSize=1;main.startSpeed=0;main.startRotation3D=false;main.startRotation=0;
                    main.playOnAwake=false;main.stopAction=ParticleSystemStopAction.None;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
                    var shape=ps.shape;shape.enabled=false;
                    var rotation=ps.rotationOverLifetime;rotation.enabled=false;
                    var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=floor;renderer.alignment=ParticleSystemRenderSpace.Local;
                }
                root.SetActive(true);return PrefabUtility.SaveAsPrefabAsset(root,Folder+"/KTP_WoodWardFootprints_019.prefab");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
        static string Check(Vfx120Profile p)
        {
            if(!Vfx120Effect.IsWoodWard(p)) throw new InvalidOperationException("Ward unprepared");
            for(int i=0;i<6;i++)
            {
                Vector3 a=Vfx120Effect.WardAnchor(i,p.Size);
                if(Mathf.Abs(a.magnitude-p.Size)>.0001f) throw new InvalidOperationException("Perimeter placement changed radius");
                if(Vfx120Effect.WardTilt(1.05f,-1)!=0 || Vfx120Effect.WardTilt(.8f,.95f)!=0 || Vfx120Effect.WardTilt(1.6f,.95f)!=0)
                    throw new InvalidOperationException("Tilt outside hit window");
            }
            if(Vfx120Effect.WardTilt(1.1f,.95f)<=0)throw new InvalidOperationException("Missing hit tilt");
            return "PASS_SIX_DISTINCT_PERIMETER_POSITIONS_AND_HIT_WINDOW";
        }
    }
}
