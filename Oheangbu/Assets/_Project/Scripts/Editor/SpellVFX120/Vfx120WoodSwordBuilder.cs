using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120WoodSwordBuilder
    {
        const string Folder="Assets/_Project/Art/SpellVFX120/WoodSword";
        [Serializable] sealed class Report
        {
            public string status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",glyph="것",beforeJson,afterJson,snapshot,technicalCheck;
            public string bodySource="Assets/_Project/Art/SpellVFX120/Botanical/Materials/M_Bamboo_0_Bark.mat";
            public string patternSource="Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_33.png";
            public string art="AWAITING_USER_REVIEW",gameplay="UNCONNECTED_CATALOG_PRESENTATION";
            public int bodyVertices,bodyTris,collarTris=2,totalStaticTris,meshRenderers=2,traceRenderers=2,traceCapacity=32;
        }
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/011_AC83.asset");
            var report=new Report();var shader=Shader.Find("Oheangbu/VFX120/WoodSword");
            if(p==null||p.Glyph!="것"||shader==null)throw new InvalidOperationException("Missing profile/shader");
            if(ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException(string.Join("; ",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message)));
            string path=Path.Combine(Vfx120Editor.Output,"wood_sword_011_build.json");report.beforeJson=JsonUtility.ToJson(p);
            if(Vfx120Effect.IsWoodSword(p)&&File.Exists(path))report.beforeJson=JsonUtility.FromJson<Report>(File.ReadAllText(path)).beforeJson;
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals"));report.snapshot=Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals/011_AC83.asset.txt");
            if(!File.Exists(report.snapshot))File.Copy(AssetDatabase.GetAssetPath(p),report.snapshot);
            File.WriteAllText(path,JsonUtility.ToJson(report,true));
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"WoodSword");
            var mesh=Blade();report.bodyVertices=mesh.vertexCount;report.bodyTris=mesh.triangles.Length/3;report.totalStaticTris=report.bodyTris+2;
            p.BodyMesh=Store(mesh,Folder+"/VFX120_WoodSword_011.asset");
            var collar=Vfx120BambooGuardBuilder.MeshOf("VFX120_WoodSwordCollar_011",new List<Vector3>{new Vector3(-.085f,.07f,-.085f),new Vector3(.085f,.07f,-.085f),new Vector3(-.085f,.07f,.085f),new Vector3(.085f,.07f,.085f)},new List<Vector2>{Vector2.zero,Vector2.right,Vector2.up,Vector2.one},new List<int>{0,2,1,1,2,3});
            p.AccentMesh=Store(collar,Folder+"/VFX120_WoodSwordCollar_011.asset");
            var source=AssetDatabase.LoadAssetAtPath<Material>(report.bodySource);if(source==null)throw new InvalidOperationException("Bamboo material missing");
            var material=new Material(source){name="M_WoodSword_011",shader=shader};material.SetFloat("_Assemble",1);material.SetFloat("_GroundY",-10000);material.SetFloat("_Saturation",.55f);
            p.BodyMaterial=Store(material,Folder+"/M_WoodSword_011.mat");
            var pattern=new Material(p.PatternMaterial){name="M_WoodSwordCollar_011"};pattern.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(report.patternSource));
            p.PatternMaterial=Store(pattern,Folder+"/M_WoodSwordCollar_011.mat");
            p.InkMaterial=AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/Materials/M_Ink.mat");
            p.Count=1;p.RibbonCount=0;p.PartScale=Vector3.one;p.SourcePattern=report.patternSource;p.NativeReplaceBody=false;p.NativeScale=.24f;p.NativeImpactScale=.55f;
            p.NativeImpactPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120Editor.AssetRoot+"/Traditional/KTP_Impact_LeafBurst.prefab");p.NativeFieldPrefab=null;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();report.afterJson=JsonUtility.ToJson(p);report.technicalCheck=Check(p);
            File.WriteAllText(path,JsonUtility.ToJson(report,true));return report.status+"; "+report.technicalCheck+"; staticTris="+report.totalStaticTris;
        }
        static T Store<T>(T value,string path)where T:UnityEngine.Object
        {var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old==null){AssetDatabase.CreateAsset(value,path);return value;}EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}
        static Mesh Blade()
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();var ids=new List<Vector2>();
            var handle=new Vector3[13];var widths=new float[13];
            for(int i=0;i<13;i++){handle[i]=new Vector3(0,Mathf.Lerp(-.22f,.075f,i/12f),0);widths[i]=.017f+(i%3==0?.002f:0);}
            Vfx120BambooGuardBuilder.Tube(handle,widths,v,uv,tris);while(ids.Count<v.Count)ids.Add(Vector2.zero);
            for(int part=0;part<4;part++)
            {
                float lo=.075f+part*.25f,hi=lo+.25f;int start=v.Count;
                for(int y=0;y<2;y++)
                {
                    float height=y==0?lo:hi;float width=part==3&&y==1?.002f:.052f+.02f*Mathf.Sin(height*Mathf.PI);
                    v.Add(new Vector3(-width*.5f,height,0));v.Add(new Vector3(0,height,.013f));v.Add(new Vector3(width,height,0));v.Add(new Vector3(0,height,-.013f));
                    for(int j=0;j<4;j++){uv.Add(new Vector2(j/3f,height));ids.Add(new Vector2(part+1,0));}
                }
                for(int j=0;j<4;j++){int next=(j+1)%4;tris.Add(start+j);tris.Add(start+4+j);tris.Add(start+next);tris.Add(start+next);tris.Add(start+4+j);tris.Add(start+4+next);}
                tris.AddRange(new[]{start,start+1,start+2,start,start+2,start+3,start+4,start+6,start+5,start+4,start+7,start+6});
                var node=new[]{new Vector3(0,lo-.006f,0),new Vector3(0,lo,0),new Vector3(0,lo+.006f,0)};
                int nodeStart=v.Count;Vfx120BambooGuardBuilder.Tube(node,new[]{.025f,.029f,.025f},v,uv,tris);
                for(int j=nodeStart;j<v.Count;j++){var p=v[j];p.x*=1.6f;p.z*=.65f;v[j]=p;ids.Add(new Vector2(part+1,0));}
            }
            var result=Vfx120BambooGuardBuilder.MeshOf("VFX120_WoodSword_011",v,uv,tris);result.SetUVs(1,ids);
            result.bounds=new Bounds(new Vector3(.02f,.43f,0),new Vector3(.3f,1.5f,.15f));return result;
        }
        static string Check(Vfx120Profile p)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("Sword_Check");var grip=new GameObject("Grip_Check");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(grip,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.DemonstrationCues=false;e.Begin(Vector3.up,null,Vector3.forward*4,Color.white);e.SetWoodSwordGrip(grip.transform);
                grip.transform.SetPositionAndRotation(new Vector3(.3f,1.1f,.6f),Quaternion.Euler(25,37,-35));e.Sample(.7f);
                if(Vector3.Distance(e.WoodSwordInstance.transform.position,grip.transform.position)>.00001f||Quaternion.Angle(e.WoodSwordInstance.transform.rotation,grip.transform.rotation)>.001f)throw new InvalidOperationException("Grip mismatch");
                if(e.SignalWoodSwordContact(Vector3.one))throw new InvalidOperationException("Contact accepted outside swing");
                e.SetWoodSwordSwing(true);if(!e.SignalWoodSwordContact(Vector3.one,.8f))throw new InvalidOperationException("Missing confirmed contact");e.Sample(.85f);
                if(e.NativeImpact==null)throw new InvalidOperationException("KTP contact missing");
                var last=grip.transform.position;UnityEngine.Object.DestroyImmediate(grip);e.Sample(.9f);
                if(Vector3.Distance(e.WoodSwordInstance.transform.position,last)>.00001f)throw new InvalidOperationException("Lost grip snap");
                return "PASS_GRIP_POSE_CONTACT_GATE_AND_LOST_ANCHOR";
            }
            finally{if(grip!=null)UnityEngine.Object.DestroyImmediate(grip);UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
