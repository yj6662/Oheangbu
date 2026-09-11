using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120LeafCutBuilder
    {
        const string Folder="Assets/_Project/Art/SpellVFX120/LeafCut";
        [Serializable] sealed class Report
        {
            public string status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",glyph="갓",beforeJson,afterJson,snapshot,technicalCheck;
            public string art="AWAITING_USER_REVIEW",gameplay="UNCONNECTED_CATALOG_PRESENTATION";
            public int halfVertices,halfTris,arcVertices,arcTris,totalTris,renderers=3;
        }
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/005_AC13.asset");var shader=Shader.Find("Oheangbu/VFX120/BotanicalSurface");if(p==null||shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Missing source/shader");
            var r=new Report();string path=Path.Combine(Vfx120Editor.Output,"leaf_cut_005_build.json");r.beforeJson=JsonUtility.ToJson(p);if(Vfx120Effect.IsLeafCut(p)&&File.Exists(path))r.beforeJson=JsonUtility.FromJson<Report>(File.ReadAllText(path)).beforeJson;
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals"));r.snapshot=Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals/005_AC13.asset.txt");if(!File.Exists(r.snapshot))File.Copy(AssetDatabase.GetAssetPath(p),r.snapshot);File.WriteAllText(path,JsonUtility.ToJson(r,true));
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"LeafCut");
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
            for(int j=0;j<=16;j++)for(int i=0;i<=3;i++)
            {
                float u=j/16f,w=i/3f,width=.095f*Mathf.Sin(Mathf.PI*Mathf.Pow(u,.75f));
                v.Add(new Vector3(Mathf.Lerp(-.38f,.43f,u),w*width,.017f*(1-w)*Mathf.Sin(u*Mathf.PI)));uv.Add(new Vector2(.5f+.5f*w,u));
                if(j>0&&i>0){int b=(j-1)*4+i-1;t.AddRange(new[]{b,b+1,b+4,b+1,b+5,b+4});}
            }
            var half=Vfx120BambooGuardBuilder.MeshOf("VFX120_LeafCutHalf_005",v,uv,t);r.halfVertices=half.vertexCount;r.halfTris=half.triangles.Length/3;p.BodyMesh=Store(half,Folder+"/VFX120_LeafCutHalf_005.asset");
            v.Clear();uv.Clear();t.Clear();
            for(int j=0;j<=24;j++)for(int i=0;i<2;i++)
            {
                float u=j/24f,a=Mathf.Lerp(-2.65f,-.2f,u),width=.043f*Mathf.Sin(u*Mathf.PI),radius=.48f+(i==0?-1:1)*width;
                var q=new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius*.35f+.08f,0);v.Add(q);uv.Add(new Vector2(.5f+Mathf.Cos(a)*(.42f+i*.07f),.5f+Mathf.Sin(a)*(.42f+i*.07f)));
                if(j>0&&i==1){int b=(j-1)*2;t.AddRange(new[]{b,b+1,b+2,b+1,b+3,b+2});}
            }
            var arc=Vfx120BambooGuardBuilder.MeshOf("VFX120_LeafCutArc_005",v,uv,t);arc.colors=Enumerable.Repeat(Color.white,arc.vertexCount).ToArray();r.arcVertices=arc.vertexCount;r.arcTris=arc.triangles.Length/3;r.totalTris=2*r.halfTris+r.arcTris;p.AccentMesh=Store(arc,Folder+"/VFX120_LeafCutArc_005.asset");
            var leaf=new Material(AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/BambooBolt/M_BambooBoltLeaf_001.mat")){name="M_LeafCut_005",shader=shader};leaf.SetFloat("_AlphaClip",0);leaf.SetColor("_Tint",new Color(.24f,.32f,.18f));leaf.SetFloat("_TintStrength",.5f);p.BodyMaterial=Store(leaf,Folder+"/M_LeafCut_005.mat");
            var mark=new Material(p.PatternMaterial){name="M_LeafCutInk_005",shader=Shader.Find("Oheangbu/VFX120/InkPigment")};string pattern="Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_33.png";mark.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(pattern));mark.SetTextureScale("_BaseMap",Vector2.one);mark.SetTextureOffset("_BaseMap",Vector2.zero);mark.SetFloat("_Pattern",.72f);p.PatternMaterial=Store(mark,Folder+"/M_LeafCutInk_005.mat");
            p.SourcePattern=pattern;p.Count=1;p.RibbonCount=0;p.PartScale=Vector3.one;p.NativeScale=.18f;p.NativeImpactScale=.45f;p.UseMist=false;p.NativeReplaceBody=false;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();r.afterJson=JsonUtility.ToJson(p);r.technicalCheck=Check(p);File.WriteAllText(path,JsonUtility.ToJson(r,true));return r.status+"; "+r.technicalCheck+"; tris="+r.totalTris;
        }
        static T Store<T>(T value,string path)where T:UnityEngine.Object
        {var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old==null){AssetDatabase.CreateAsset(value,path);return value;}EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}
        static string Check(Vfx120Profile p)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("LeafCut_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;
                foreach(bool execution in new[]{false,true})
                {
                    e.Begin(Vector3.up,null,Vfx120LeafCutReviewFixture.Contact,Color.white);e.Sample(.7f);if(e.LeafCutAt>=0||e.NativeImpact!=null)throw new InvalidOperationException("Invented cut");
                    if(!e.SignalLeafCut(Vfx120LeafCutReviewFixture.Contact,Vector3.back,execution,.6f)||e.SignalLeafCut(Vector3.one,Vector3.up,true,.8f))throw new InvalidOperationException("Duplicate cut accepted");e.Sample(.7f);
                    if(e.LeafCutExecution!=execution||Mathf.Abs(e.LeafCutEmphasis-(execution?1.85f:1))>.001f)throw new InvalidOperationException("Execution emphasis mismatch");
                    if(e.NativeImpact==null||Mathf.Abs(e.NativeImpactStartedAt-.6f)>.001f)throw new InvalidOperationException("Impact timing mismatch");
                    e.Sample(e.Life);if(e.LeafCutInstance.GetComponentsInChildren<MeshRenderer>().Any(x=>x.enabled))throw new InvalidOperationException("Lingering cut");
                }
                return "PASS_NORMAL_EXECUTION_SEPARATION_CONFIRMED_CUT_AND_LIFETIME";
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
