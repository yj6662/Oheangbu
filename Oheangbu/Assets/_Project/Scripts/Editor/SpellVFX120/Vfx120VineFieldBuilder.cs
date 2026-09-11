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
    public static class Vfx120VineFieldBuilder
    {
        const string Folder="Assets/_Project/Art/SpellVFX120/VineField";
        [Serializable] sealed class Report
        {
            public string status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",glyph="곡",beforeJson,afterJson,snapshot,technicalCheck;
            public string stemSource="Assets/SeyeonjeongPavilion/Texture/Plant/T_BrassicaNapus_Stem_BC.png";
            public string leafSource="Assets/SeyeonjeongPavilion/Texture/Plant/T_BrassicaNapus_Leaf_BC.png";
            public string patternSource="Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_33.png";
            public string art="AWAITING_USER_REVIEW",gameplay="UNCONNECTED_CATALOG_PRESENTATION";
            public int orphanPreviewMeshesRemoved;
            public int vertices,stemTris,leafTris,patternTris,totalTris,meshRenderers=1,submeshes=3,contactLines=3;
        }
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/014_ACE1.asset");var shader=Shader.Find("Oheangbu/VFX120/VineSurface");
            if(p==null||shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Missing vine profile or shader error");
            var r=new Report();
            var referenced=Resources.FindObjectsOfTypeAll<MeshFilter>().Select(f=>f.sharedMesh).ToArray();
            foreach(var oldMesh in Resources.FindObjectsOfTypeAll<Mesh>())
                if(oldMesh.name=="VineField_GroundInstance"&&!EditorUtility.IsPersistent(oldMesh)&&!referenced.Contains(oldMesh))
                {UnityEngine.Object.DestroyImmediate(oldMesh);r.orphanPreviewMeshesRemoved++;}
            string path=Path.Combine(Vfx120Editor.Output,"vine_field_014_build.json");r.beforeJson=JsonUtility.ToJson(p);
            if(Vfx120Effect.IsVineField(p)&&File.Exists(path))r.beforeJson=JsonUtility.FromJson<Report>(File.ReadAllText(path)).beforeJson;
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals"));r.snapshot=Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals/014_ACE1.asset.txt");
            if(!File.Exists(r.snapshot))File.Copy(AssetDatabase.GetAssetPath(p),r.snapshot);File.WriteAllText(path,JsonUtility.ToJson(r,true));
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"VineField");
            var mesh=FieldMesh();r.vertices=mesh.vertexCount;r.stemTris=mesh.GetTriangles(0).Length/3;r.leafTris=mesh.GetTriangles(1).Length/3;r.patternTris=mesh.GetTriangles(2).Length/3;r.totalTris=mesh.triangles.Length/3;
            p.BodyMesh=Store(mesh,Folder+"/VFX120_VineField_014.asset");
            var stem=new Material(shader){name="M_VineStem_014"};Plant(stem,r.stemSource,false);stem.SetColor("_Tint",new Color(.4f,.42f,.23f));
            p.BodyMaterial=Store(stem,Folder+"/M_VineStem_014.mat");
            var leaf=new Material(shader){name="M_VineLeaf_014"};Plant(leaf,r.leafSource,true);leaf.SetColor("_Tint",new Color(.45f,.48f,.27f));
            p.InkMaterial=Store(leaf,Folder+"/M_VineLeaf_014.mat");
            var pattern=new Material(shader){name="M_VineKTP_014"};pattern.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(r.patternSource));pattern.SetFloat("_PatternInk",1);pattern.SetColor("_Tint",new Color(.27f,.34f,.19f));pattern.SetFloat("_BumpScale",0);
            p.PatternMaterial=Store(pattern,Folder+"/M_VineKTP_014.mat");
            var ink=new Material(Shader.Find("Oheangbu/VFX120/InkPigment")){name="M_VineContactInk_014"};ink.SetColor("_BaseColor",Color.white);ink.SetFloat("_Pattern",0);ink.SetFloat("_Soft",0);
            p.MistMaterial=Store(ink,Folder+"/M_VineContactInk_014.mat");
            p.Count=12;p.PartScale=Vector3.one;p.RibbonCount=0;p.UseMist=false;p.NativeScale=.25f;p.NativeReplaceBody=false;p.NativeImpactPrefab=null;p.NativeFieldPrefab=null;p.SourcePattern=r.patternSource;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();r.afterJson=JsonUtility.ToJson(p);r.technicalCheck=Check(p);File.WriteAllText(path,JsonUtility.ToJson(r,true));return r.status+"; "+r.technicalCheck+"; tris="+r.totalTris;
        }
        static void Plant(Material m,string path,bool alpha)
        {
            var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(path);var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(path.Replace("_BC.png","_N.png"));
            if(tex==null||normal==null)throw new InvalidOperationException("Missing plant source");m.SetTexture("_BaseMap",tex);m.SetTexture("_BumpMap",normal);m.SetFloat("_AlphaClip",alpha?1:0);m.SetFloat("_Cutoff",.33f);m.SetFloat("_TintStrength",.25f);m.SetFloat("_Saturation",.65f);
        }
        static T Store<T>(T value,string path)where T:UnityEngine.Object
        {var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old==null){AssetDatabase.CreateAsset(value,path);return value;}EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}
        static Vector3 Main(int q,float u)
        {float a=q*Mathf.PI*.5f+.18f;var d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));var s=new Vector3(-d.z,0,d.x);return d*(.035f+.86f*u)+s*(.09f*Mathf.Sin(u*6.4f+q*.7f)*u)+Vector3.up*(.038f+.021f*Mathf.Pow(Mathf.Sin(u*8+q),2));}
        static Mesh FieldMesh()
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var stage=new List<Vector2>();var stem=new List<int>();var leaf=new List<int>();var mark=new List<int>();
            for(int q=0;q<4;q++)
            {
                var points=new Vector3[33];var widths=new float[33];for(int j=0;j<33;j++){float u=j/32f;points[j]=Main(q,u);widths[j]=Mathf.Lerp(.014f,.004f,u);}
                int first=v.Count;Vfx120BambooGuardBuilder.Tube(points,widths,v,uv,stem);for(int j=first;j<v.Count;j++)stage.Add(new Vector2(.015f+uv[j].y*.91f,uv[j].y));
                for(int arm=0;arm<2;arm++)
                {
                    float start=.28f+arm*.31f;var root=Main(q,start);var direction=(Main(q,start+.02f)-root).normalized;var side=Vector3.Cross(Vector3.up,direction)*(arm==0?1:-1);
                    points=new Vector3[21];widths=new float[21];for(int j=0;j<21;j++){float u=j/20f;points[j]=root+direction*(.22f*u)+side*(.21f*Mathf.Sin(u*2.3f))+Vector3.up*.025f*Mathf.Sin(u*3.14f);widths[j]=Mathf.Lerp(.009f,.0025f,u);}
                    first=v.Count;Vfx120BambooGuardBuilder.Tube(points,widths,v,uv,stem);for(int j=first;j<v.Count;j++)stage.Add(new Vector2(start*.9f+.05f+uv[j].y*.22f,uv[j].y));
                }
                for(int k=0;k<6;k++)
                {
                    float u=.22f+k*.12f;var root=Main(q,u);var direction=(Main(q,u+.01f)-root).normalized;var side=Vector3.Cross(Vector3.up,direction)*(k%2==0?1:-1);int b=v.Count;
                    for(int y=0;y<=4;y++)for(int x=0;x<=2;x++)
                    {
                        float f=y/4f,w=x/2f-.5f;v.Add(root+side*f*.16f+direction*w*.14f+Vector3.up*(.02f+Mathf.Sin(f*Mathf.PI)*.042f));uv.Add(new Vector2(x/2f,f));stage.Add(new Vector2(u*.9f+.05f+f*.06f,1));
                        if(y>0&&x>0){int n=b+(y-1)*3+x-1;leaf.AddRange(new[]{n,n+1,n+3,n+1,n+4,n+3});}
                    }
                }
                // Open ornament fragments nest into the vines; no continuous boundary ring.
                var center=Main(q,.54f);int offset=v.Count;
                for(int j=0;j<=20;j++)for(int edge=0;edge<2;edge++)
                {
                    float a=Mathf.Lerp(-35,65,j/20f)*Mathf.Deg2Rad+q*Mathf.PI*.5f;float r=edge==0?.13f:.20f;
                    v.Add(new Vector3(center.x+Mathf.Cos(a)*r,.018f,center.z+Mathf.Sin(a)*r));uv.Add(Vector2.one*.5f+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*(edge==0?.36f:.49f));stage.Add(new Vector2(.51f+j/20f*.11f,0));
                    if(j>0&&edge==1){int b=offset+(j-1)*2;mark.AddRange(new[]{b,b+2,b+1,b+1,b+2,b+3});}
                }
            }
            var mesh=new Mesh{name="VFX120_VineField_014"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetUVs(1,stage);mesh.subMeshCount=3;mesh.SetTriangles(stem,0);mesh.SetTriangles(leaf,1);mesh.SetTriangles(mark,2);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
        }
        static string Check(Vfx120Profile p)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("VineField_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.Begin(Vector3.up,null,Vector3.forward*4,Color.white);if(e.VineFieldConfigured||e.PartCount!=0)throw new InvalidOperationException("Unplanned field");
                var plan=new AreaImpactPlan{Shape=AreaShape.Circle,Point=new Vector3(0,0,4),Radius=1.75f,Delay=.38f};string before=JsonUtility.ToJson(plan);e.SetAreaPlan(plan);e.Begin(Vector3.up,null,Vector3.forward*4,Color.white);
                if(!e.VineFieldConfigured||e.VineFieldOwnedMesh==p.BodyMesh)throw new InvalidOperationException("Missing owned mesh");
                e.Sample(.2f);if(e.VineFieldSpread!=0||e.SignalVineFootContact(new Vector3(0,0,4)))throw new InvalidOperationException("Premature field/contact");
                e.Sample(1.6f);if(e.VineFieldSpread<.99f||e.VineFieldActiveContacts!=0)throw new InvalidOperationException("Spread or invented contact");
                if(e.SignalVineFootContact(new Vector3(10,0,4)))throw new InvalidOperationException("Out-of-field contact accepted");
                if(!e.SignalVineFootContact(new Vector3(.2f,0,4)))throw new InvalidOperationException("Confirmed foot rejected");e.Sample(1.7f);if(e.VineFieldActiveContacts!=1)throw new InvalidOperationException("Contact missing");
                e.Sample(2.3f);if(e.VineFieldActiveContacts!=0)throw new InvalidOperationException("Contact lingered");
                if(!e.ReleaseVineField()||e.ReleaseVineField())throw new InvalidOperationException("Release idempotency");e.Sample(3.1f);if(e.VineFieldSpread!=0||e.VineFieldInstance.GetComponentInChildren<MeshRenderer>().enabled)throw new InvalidOperationException("Released field remains");
                foreach(var v in e.VineFieldOwnedMesh.vertices)if(!Vfx120InterceptionMotion.Finite(v)||v.y>.22f||v.y<0)throw new InvalidOperationException("Invalid flat preview field height");
                if(before!=JsonUtility.ToJson(plan))throw new InvalidOperationException("Plan mutated");
                var owned=e.VineFieldOwnedMesh;UnityEngine.Object.DestroyImmediate(go);if(owned!=null)throw new InvalidOperationException("Owned mesh leak");
                return "PASS_SPREAD_CONTACT_ISOLATION_RELEASE_AND_OWNED_MESH_CLEANUP";
            }
            finally{if(go!=null)UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
