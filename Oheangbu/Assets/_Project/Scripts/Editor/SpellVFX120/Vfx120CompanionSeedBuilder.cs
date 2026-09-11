using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120CompanionSeedBuilder
    {
        const string Folder="Assets/_Project/Art/SpellVFX120/CompanionSeeds";
        [Serializable] sealed class Report
        {
            public string status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",glyph="겅",beforeJson,afterJson,snapshot,technicalCheck;
            public string bodySource="Assets/_Project/Art/SpellVFX120/Botanical/Materials/M_Bamboo_0_Bark.mat";
            public string patternSource="Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_192.png";
            public string patternTreatment="UV central patch scale(0.5,0.65),offset(0.25,0.18); alpha smoothstep(0.5,0.85) suppresses faint lattice. Original pixels unchanged.";
            public string art="AWAITING_USER_REVIEW",gameplay="UNCONNECTED_CATALOG_PRESENTATION";
            public int shellVertices,shellTris,totalShellTris,markTris=4,renderers=6;
        }
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/012_AC85.asset");var report=new Report();var shader=Shader.Find("Oheangbu/VFX120/SeedLeafMark");
            if(p==null||p.Glyph!="겅"||shader==null)throw new InvalidOperationException("Missing profile/shader");
            if(ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException(string.Join("; ",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message)));
            string path=Path.Combine(Vfx120Editor.Output,"companion_seed_012_build.json");report.beforeJson=JsonUtility.ToJson(p);
            if(Vfx120Effect.IsCompanionSeeds(p)&&File.Exists(path))report.beforeJson=JsonUtility.FromJson<Report>(File.ReadAllText(path)).beforeJson;
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals"));report.snapshot=Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals/012_AC85.asset.txt");
            if(!File.Exists(report.snapshot))File.Copy(AssetDatabase.GetAssetPath(p),report.snapshot);File.WriteAllText(path,JsonUtility.ToJson(report,true));
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"CompanionSeeds");
            var shell=Shell();report.shellVertices=shell.vertexCount;report.shellTris=shell.triangles.Length/3;report.totalShellTris=4*report.shellTris;
            p.BodyMesh=Store(shell,Folder+"/VFX120_CompanionSeedShell_012.asset");
            var mark=Vfx120BambooGuardBuilder.MeshOf("VFX120_CompanionLeafMark_012",new List<Vector3>{new Vector3(-.3f,-.5f,0),new Vector3(.3f,-.5f,0),new Vector3(-.3f,.5f,0),new Vector3(.3f,.5f,0)},new List<Vector2>{Vector2.zero,Vector2.right,Vector2.up,Vector2.one},new List<int>{0,1,2,1,3,2});
            p.AccentMesh=Store(mark,Folder+"/VFX120_CompanionLeafMark_012.asset");
            var source=AssetDatabase.LoadAssetAtPath<Material>(report.bodySource);if(source==null)throw new InvalidOperationException("Missing bark source");
            var material=new Material(source){name="M_CompanionSeed_012"};material.SetColor("_Tint",new Color(.62f,.51f,.29f));material.SetFloat("_TintStrength",.38f);material.SetFloat("_Saturation",.6f);material.SetFloat("_GroundY",-10000);
            p.BodyMaterial=Store(material,Folder+"/M_CompanionSeed_012.mat");
            var pattern=new Material(p.PatternMaterial){name="M_CompanionLeafMark_012",shader=shader};pattern.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(report.patternSource));pattern.SetTextureScale("_BaseMap",new Vector2(.5f,.65f));pattern.SetTextureOffset("_BaseMap",new Vector2(.25f,.18f));
            p.PatternMaterial=Store(pattern,Folder+"/M_CompanionLeafMark_012.mat");
            p.Count=2;p.RibbonCount=0;p.PartScale=Vector3.one;p.SourcePattern=report.patternSource;p.NativeReplaceBody=false;p.NativeScale=.18f;p.NativeImpactPrefab=null;p.NativeFieldPrefab=null;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();report.afterJson=JsonUtility.ToJson(p);report.technicalCheck=Check(p);File.WriteAllText(path,JsonUtility.ToJson(report,true));
            return report.status+"; "+report.technicalCheck+"; shellTris="+report.totalShellTris;
        }
        static T Store<T>(T value,string path)where T:UnityEngine.Object
        {var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old==null){AssetDatabase.CreateAsset(value,path);return value;}EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}
        static Mesh Shell()
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();const int n=8,layerSize=81;
            for(int layer=0;layer<2;layer++)for(int z=0;z<=n;z++)for(int a=0;a<=n;a++)
            {
                float t=z/(float)n,theta=Mathf.Lerp(-Mathf.PI*.5f,Mathf.PI*.5f,a/(float)n),radius=(.0015f+.021f*Mathf.Sin(t*Mathf.PI))*(layer==0?1:.72f);
                v.Add(new Vector3(Mathf.Cos(theta)*radius,Mathf.Sin(theta)*radius*.65f,Mathf.Lerp(-.08f,.085f,t)));uv.Add(new Vector2(a/(float)n,t));
                if(z>0&&a>0){int b=v.Count-11;if(layer==0)tris.AddRange(new[]{b,b+1,b+9,b+1,b+10,b+9});else tris.AddRange(new[]{b,b+9,b+1,b+1,b+9,b+10});}
            }
            for(int j=0;j<n;j++)
            {
                Bridge(j,j+1,layerSize,tris);Bridge(n*9+j+1,n*9+j,layerSize,tris);
                Bridge((j+1)*9,j*9,layerSize,tris);Bridge(j*9+8,(j+1)*9+8,layerSize,tris);
            }
            return Vfx120BambooGuardBuilder.MeshOf("VFX120_CompanionSeedShell_012",v,uv,tris);
        }
        static void Bridge(int a,int b,int offset,List<int> t){t.AddRange(new[]{a,b,a+offset,b,b+offset,a+offset});}
        static string Check(Vfx120Profile p)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("Seed_Check");var primary=new GameObject("Primary_Check");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(primary,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.DemonstrationCues=false;e.Begin(Vector3.up,null,Vector3.forward*4,Color.white);
                if(e.ConfirmCompanionHit(0,Vector3.one,Vector3.back))throw new InvalidOperationException("Hit without launch plan");
                if(!e.SetCompanionProjectile(primary.transform,.4f,1.45f))throw new InvalidOperationException("Missing attachment");primary.transform.position=new Vector3(.3f,1.1f,2);e.Sample(1);
                Vector3 seed0=e.CompanionPosition(0),seed1=e.CompanionPosition(1);primary.transform.position+=Vector3.right;e.Sample(1.05f);
                if(Vector3.Distance(e.CompanionPosition(0)-seed0,Vector3.right)>.0001f||Vector3.Distance(e.CompanionPosition(1)-seed1,Vector3.right)>.0001f)throw new InvalidOperationException("Primary tracking mismatch");
                var contact=Vfx120CompanionSeedReviewFixture.ContactPosition(0);if(!e.ConfirmCompanionHit(0,contact,Vector3.back,1.45f))throw new InvalidOperationException("First hit refused");e.Sample(1.67f);
                if(e.CompanionHitMask!=1||e.CompanionOpenMask!=1)throw new InvalidOperationException("First hit changed another seed");
                if(e.ConfirmCompanionHit(0,contact,Vector3.back,1.68f))throw new InvalidOperationException("Duplicate hit accepted");
                if(!e.ConfirmCompanionHit(1,Vfx120CompanionSeedReviewFixture.ContactPosition(1),Vector3.back,1.7f))throw new InvalidOperationException("Second hit refused");e.Sample(1.92f);
                if(e.CompanionHitMask!=3||e.CompanionOpenMask!=3)throw new InvalidOperationException("Missing independent opening");
                return "PASS_PRIMARY_TRACKING_AND_INDEPENDENT_HITS";
            }
            finally{UnityEngine.Object.DestroyImmediate(primary);UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
