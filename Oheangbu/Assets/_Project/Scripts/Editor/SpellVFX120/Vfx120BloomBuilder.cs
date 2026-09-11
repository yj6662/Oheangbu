using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120BloomBuilder
    {
        const string Folder="Assets/_Project/Art/SpellVFX120/Bloom";
        [Serializable] sealed class Report
        {
            public string status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",glyph="건",beforeJson,afterJson,snapshot,technicalCheck;
            public string leafSource="Assets/SeyeonjeongPavilion/Texture/Plant/T_NymphaeaTetragona_Leaf_BC.png";
            public string patternSource="Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_210.png";
            public string leafUV="Upper-left leaf atlas patch: scale (0.44,0.44), offset (0.025,0.53). Texture pixels unchanged.";
            public string art="AWAITING_USER_REVIEW",gameplay="UNCONNECTED_CATALOG_PRESENTATION";
            public int leafVertices,leafTris,totalBodyTris,renderers=2,ownedRuntimeMeshes=2;
        }
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/009_AC74.asset");
            var report=new Report();var shader=Shader.Find("Oheangbu/VFX120/BloomLeaf");
            if(p==null||p.Glyph!="건"||shader==null)throw new InvalidOperationException("Missing profile or shader");
            if(ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException(string.Join("; ",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message)));
            report.beforeJson=JsonUtility.ToJson(p);string path=Path.Combine(Vfx120Editor.Output,"bloom_009_build.json");
            if(Vfx120Effect.IsBloom(p)&&File.Exists(path))report.beforeJson=JsonUtility.FromJson<Report>(File.ReadAllText(path)).beforeJson;
            else if(Vfx120Effect.IsBloom(p))report.beforeJson=""; // interrupted prior build: original YAML remains in snapshot
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals"));
            report.snapshot=Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals/009_AC74.asset.txt");
            if(!File.Exists(report.snapshot))File.Copy(AssetDatabase.GetAssetPath(p),report.snapshot);
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"Bloom");
            var mesh=Leaf();report.leafVertices=mesh.vertexCount;report.leafTris=mesh.triangles.Length/3;report.totalBodyTris=2*report.leafTris;
            p.BodyMesh=Store(mesh,Folder+"/VFX120_BloomLeaf_009.asset");
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(report.leafSource);
            var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(report.leafSource.Replace("_BC.png","_N.png"));
            var pattern=AssetDatabase.LoadAssetAtPath<Texture2D>(report.patternSource);
            if(texture==null||normal==null||pattern==null)throw new InvalidOperationException("Missing source textures");
            var material=new Material(shader){name="M_BloomLeaf_009"};
            material.SetTexture("_BaseMap",texture);material.SetTexture("_BumpMap",normal);material.SetTexture("_PatternMap",pattern);
            material.SetTextureScale("_BaseMap",new Vector2(.44f,.44f));material.SetTextureOffset("_BaseMap",new Vector2(.025f,.53f));
            material.SetFloat("_Saturation",.55f);material.SetFloat("_TintStrength",.16f);material.SetFloat("_AlphaClip",0);material.SetFloat("_GroundY",-10000);
            p.BodyMaterial=Store(material,Folder+"/M_BloomLeaf_009.mat");
            p.Count=2;p.RibbonCount=0;p.PartScale=Vector3.one;p.SourcePattern=report.patternSource;p.NativeReplaceBody=false;p.NativeFieldPrefab=null;
            p.NativeScale=.26f;p.NativeImpactScale=.65f;
            p.NativeCastPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120Editor.AssetRoot+"/Traditional/KTP_Cast_LeafBloom.prefab");
            p.NativeImpactPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120Editor.AssetRoot+"/Traditional/KTP_Impact_LeafBurst.prefab");
            if(p.NativeCastPrefab==null||p.NativeImpactPrefab==null)throw new InvalidOperationException("KTP cast/impact missing");
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();report.afterJson=JsonUtility.ToJson(p);report.technicalCheck=Check(p);
            File.WriteAllText(path,JsonUtility.ToJson(report,true));return report.status+"; "+report.technicalCheck+"; tris="+report.totalBodyTris;
        }
        static T Store<T>(T value,string path) where T:UnityEngine.Object
        {
            var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old==null){AssetDatabase.CreateAsset(value,path);return value;}
            EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;
        }
        static Mesh Leaf()
        {
            var v=new List<Vector3>{Vector3.zero};var uv=new List<Vector2>{new Vector2(.5f,.5f)};var tris=new List<int>();
            const int rings=9,segments=32;
            for(int ring=1;ring<=rings;ring++)for(int x=0;x<=segments;x++)
            {
                float theta=Mathf.Lerp(235,575,x/(float)segments)*Mathf.Deg2Rad,r=ring/(float)rings;
                var d=new Vector2(Mathf.Cos(theta),Mathf.Sin(theta))*r;
                v.Add(new Vector3(d.x*.34f,d.y*.43f,0));uv.Add(new Vector2(.5f+d.x*.5f,.5f+d.y*.5f));
                if(x==0)continue;int end=v.Count-1;
                if(ring==1){tris.Add(0);tris.Add(end-1);tris.Add(end);}
                else{int lower=end-segments-1;tris.Add(lower-1);tris.Add(end-1);tris.Add(end);tris.Add(lower-1);tris.Add(end);tris.Add(lower);}
            }
            return Vfx120BambooGuardBuilder.MeshOf("VFX120_BloomLeaf_009",v,uv,tris);
        }
        static string Check(Vfx120Profile p)
        {
            foreach(float t in new[]{0f,.28f,.5f,.8f,1.4f})foreach(var v in p.BodyMesh.vertices)
            {
                Vector3 result=Vfx120Effect.BloomVertex(v,Vfx120Effect.BloomOpening(t,.28f));
                if(!Vfx120InterceptionMotion.Finite(result)||result.magnitude>1)throw new InvalidOperationException("Invalid leaf deformation");
            }
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("Bloom_Check");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.DemonstrationCues=false;e.Begin(Vector3.up,null,Vector3.forward*4,Color.white);
                e.Sample(.3f);if(e.BloomOpened!=0||e.NativeImpact!=null)throw new InvalidOperationException("Unconfirmed bloom");
                if(!e.SignalBloom()||e.SignalBloom())throw new InvalidOperationException("One-shot bloom latch failed");
                e.Sample(.55f);if(e.BloomOpened<.99f||e.NativeImpact==null)throw new InvalidOperationException("Confirmed bloom missing");
                return "PASS_FINITE_FOLDS_AND_ONE_SHOT_CONFIRMATION";
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
