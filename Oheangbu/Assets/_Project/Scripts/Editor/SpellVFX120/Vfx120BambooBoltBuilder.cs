using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120BambooBoltBuilder
    {
        const string Folder="Assets/_Project/Art/SpellVFX120/BambooBolt";
        [Serializable] sealed class Report
        {
            public string status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",glyph="가",beforeJson,afterJson,snapshot,technicalCheck;
            public string art="AWAITING_USER_REVIEW",gameplay="Existing attack clock/target reader; actual player cast regression pending";
            public int bodyVertices,bodyTris,leafVertices,leafTris,totalTris,renderers=5;
        }
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/001_AC00.asset");var shader=Shader.Find("Oheangbu/VFX120/BambooBolt");
            if(p==null||shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Missing bolt or shader error");
            var r=new Report();string path=Path.Combine(Vfx120Editor.Output,"bamboo_bolt_001_build.json");r.beforeJson=JsonUtility.ToJson(p);
            if(Vfx120Effect.IsBambooBolt(p)&&File.Exists(path))r.beforeJson=JsonUtility.FromJson<Report>(File.ReadAllText(path)).beforeJson;
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals"));r.snapshot=Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals/001_AC00.asset.txt");
            if(!File.Exists(r.snapshot))File.Copy(AssetDatabase.GetAssetPath(p),r.snapshot);File.WriteAllText(path,JsonUtility.ToJson(r,true));
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"BambooBolt");
            var leaf=Leaf();var body=Body(leaf);r.bodyVertices=body.vertexCount;r.bodyTris=body.triangles.Length/3;r.leafVertices=leaf.vertexCount;r.leafTris=leaf.triangles.Length/3;r.totalTris=r.bodyTris+3*r.leafTris+2;
            p.BodyMesh=Store(body,Folder+"/VFX120_BambooBolt_001.asset");p.AccentMesh=Store(leaf,Folder+"/VFX120_BambooBoltLeaf_001.asset");
            var bark=AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/Botanical/Materials/M_Bamboo_0_Bark.mat");
            var mat=new Material(bark){name="M_BambooBolt_001",shader=shader};mat.SetFloat("_Build",1);mat.SetFloat("_Dry",0);mat.SetColor("_Tint",new Color(.37f,.5f,.3f));mat.SetFloat("_TintStrength",.3f);
            p.BodyMaterial=Store(mat,Folder+"/M_BambooBolt_001.mat");
            var lm=new Material(shader){name="M_BambooBoltLeaf_001"};lm.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SeyeonjeongPavilion/Texture/Plant/T_BrassicaNapus_Leaf_BC.png"));lm.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SeyeonjeongPavilion/Texture/Plant/T_BrassicaNapus_Leaf_N.png"));lm.SetFloat("_AlphaClip",1);lm.SetFloat("_Cutoff",.33f);
            p.InkMaterial=Store(lm,Folder+"/M_BambooBoltLeaf_001.mat");
            var pattern=new Material(p.PatternMaterial){name="M_BambooBoltContact_001",shader=Shader.Find("Oheangbu/VFX120/SeedLeafMark")};pattern.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_192.png"));pattern.SetTextureScale("_BaseMap",new Vector2(.5f,.65f));pattern.SetTextureOffset("_BaseMap",new Vector2(.25f,.18f));
            p.PatternMaterial=Store(pattern,Folder+"/M_BambooBoltContact_001.mat");p.NativeScale=.24f;p.NativeImpactScale=.5f;p.Count=1;p.PartScale=Vector3.one;p.RibbonCount=0;p.UseMist=false;p.NativeReplaceBody=false;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();r.afterJson=JsonUtility.ToJson(p);r.technicalCheck=Check(p);File.WriteAllText(path,JsonUtility.ToJson(r,true));return r.status+"; "+r.technicalCheck+"; tris="+r.totalTris;
        }
        static T Store<T>(T value,string path)where T:UnityEngine.Object
        {var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old==null){AssetDatabase.CreateAsset(value,path);return value;}EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}
        static Mesh Leaf()
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var stage=new List<Vector2>();var t=new List<int>();
            for(int y=0;y<=4;y++)for(int x=0;x<=2;x++)
            {float u=y/4f,w=x/2f;v.Add(new Vector3((w-.5f)*.105f,.012f*Mathf.Sin(u*Mathf.PI),u*.19f));uv.Add(new Vector2(w,u));stage.Add(new Vector2(.5f,u));if(y>0&&x>0){int b=(y-1)*3+x-1;t.AddRange(new[]{b,b+1,b+3,b+1,b+4,b+3});}}
            var mesh=Vfx120BambooGuardBuilder.MeshOf("VFX120_BambooBoltLeaf_001",v,uv,t);mesh.SetUVs(1,stage);return mesh;
        }
        static Mesh Body(Mesh leaf)
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();var fins=new List<int>();var stage=new List<Vector2>();
            float[] zs={-.52f,-.45f,-.44f,-.425f,-.41f,-.3f,-.29f,-.275f,-.26f,-.15f,-.14f,-.125f,-.11f,0};
            float[] r={.011f,.014f,.019f,.019f,.014f,.012f,.017f,.017f,.012f,.009f,.014f,.014f,.009f,.0008f};
            var path=new Vector3[zs.Length];for(int i=0;i<zs.Length;i++)path[i]=Vector3.up*zs[i];Vfx120BambooGuardBuilder.Tube(path,r,v,uv,t);
            for(int i=0;i<v.Count;i++){var p=v[i];v[i]=new Vector3(p.x,-p.z,p.y);stage.Add(new Vector2(Mathf.Clamp01((p.y+.52f)/.52f),0));}
            var lv=leaf.vertices;var lu=leaf.uv;var lt=leaf.triangles;
            for(int k=0;k<3;k++)
            {int offset=v.Count;var rot=Quaternion.Euler(0,0,k*120+15);for(int i=0;i<lv.Length;i++){var q=lv[i];v.Add(rot*(q+Vector3.up*.011f)+Vector3.back*.46f);uv.Add(lu[i]);stage.Add(new Vector2(.12f+lu[i].y*.28f,lu[i].y));}foreach(int index in lt)fins.Add(offset+index);}
            var mesh=new Mesh{name="VFX120_BambooBolt_001"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetUVs(1,stage);mesh.subMeshCount=2;mesh.SetTriangles(t,0);mesh.SetTriangles(fins,1);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
        }
        static string Check(Vfx120Profile p)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("BambooBolt_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.SetImpactClock(.8f);var origin=new Vector3(.2f,1,0);var target=new Vector3(1.2f,1.4f,4);e.Begin(origin,null,target,Color.white);
                foreach(float age in new[]{.1f,.4f,.8f}){e.Sample(age);if(Vector3.Distance(e.BambooBoltTip,Vector3.Lerp(origin,target,age/.8f))>.0001f)throw new InvalidOperationException("Tip path mismatch");}
                if(!e.BambooBoltContactSeen||e.NativeImpact==null||Mathf.Abs(e.NativeImpactStartedAt-.8f)>.0001f)throw new InvalidOperationException("Impact clock mismatch");
                e.Sample(e.Life);if(e.BambooBoltInstance.GetComponentsInChildren<MeshRenderer>().Any(r=>r.enabled))throw new InvalidOperationException("Lingering bolt renderer");
                if(p.BodyMesh.bounds.size.x<.02f||p.BodyMesh.bounds.size.z<.5f)throw new InvalidOperationException("Collapsed geometry");
                return "PASS_TARGET_PATH_IMPACT_CLOCK_AND_RENDERER_LIFETIME";
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
