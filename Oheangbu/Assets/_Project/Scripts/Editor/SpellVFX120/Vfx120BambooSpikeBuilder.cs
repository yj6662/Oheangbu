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
    public static class Vfx120BambooSpikeBuilder
    {
        const string Folder="Assets/_Project/Art/SpellVFX120/BambooSpikes";
        [Serializable] sealed class Report
        {
            public string status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",glyph="고",beforeJson,afterJson,snapshot,technicalCheck;
            public string barkSource="Assets/_Project/Art/SpellVFX120/Botanical/Materials/M_Bamboo_0_Bark.mat";
            public string patternSource="Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_192.png";
            public string art="AWAITING_USER_REVIEW",gameplay="Existing Circle plan/Delay reader. Actual player cast and damage replay not exercised by this builder.";
            public int spikeVertices,spikeTris,totalSpikeTris,markTris=10,renderers=22;
        }
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/013_ACE0.asset");
            var shader=Shader.Find("Oheangbu/VFX120/BambooSpike");var markShader=Shader.Find("Oheangbu/VFX120/SeedLeafMark");
            if(p==null||shader==null||markShader==null)throw new InvalidOperationException("Missing go profile/shaders");
            foreach(var s in new[]{shader,markShader})if(ShaderUtil.ShaderHasError(s))throw new InvalidOperationException(string.Join(";",ShaderUtil.GetShaderMessages(s).Select(m=>m.message)));
            var r=new Report();string path=Path.Combine(Vfx120Editor.Output,"bamboo_spike_013_build.json");r.beforeJson=JsonUtility.ToJson(p);
            if(Vfx120Effect.IsBambooSpikes(p)&&File.Exists(path))r.beforeJson=JsonUtility.FromJson<Report>(File.ReadAllText(path)).beforeJson;
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals"));r.snapshot=Path.Combine(Vfx120Editor.Output,"WoodDetailOriginals/013_ACE0.asset.txt");
            if(!File.Exists(r.snapshot))File.Copy(AssetDatabase.GetAssetPath(p),r.snapshot);File.WriteAllText(path,JsonUtility.ToJson(r,true));
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"BambooSpikes");
            var mesh=Spike();r.spikeVertices=mesh.vertexCount;r.spikeTris=mesh.triangles.Length/3;r.totalSpikeTris=17*r.spikeTris;
            p.BodyMesh=Store(mesh,Folder+"/VFX120_BambooSpike_013.asset");
            var quad=Vfx120BambooGuardBuilder.MeshOf("VFX120_BambooRootInk_013",new List<Vector3>{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(-.5f,.5f,0),new Vector3(.5f,.5f,0)},new List<Vector2>{Vector2.zero,Vector2.right,Vector2.up,Vector2.one},new List<int>{0,1,2,1,3,2});
            p.AccentMesh=Store(quad,Folder+"/VFX120_BambooRootInk_013.asset");
            var bark=AssetDatabase.LoadAssetAtPath<Material>(r.barkSource);if(bark==null)throw new InvalidOperationException("Missing bamboo material");
            var body=new Material(bark){name="M_BambooSpike_013",shader=shader};body.SetColor("_Tint",new Color(.34f,.43f,.27f));body.SetFloat("_TintStrength",.3f);body.SetFloat("_DissolveHeight",1.45f);
            p.BodyMaterial=Store(body,Folder+"/M_BambooSpike_013.mat");
            var mark=new Material(p.PatternMaterial){name="M_BambooRootInk_013",shader=markShader};mark.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(r.patternSource));mark.SetTextureScale("_BaseMap",new Vector2(.5f,.65f));mark.SetTextureOffset("_BaseMap",new Vector2(.25f,.18f));
            p.PatternMaterial=Store(mark,Folder+"/M_BambooRootInk_013.mat");
            p.Count=17;p.PartScale=Vector3.one;p.RibbonCount=0;p.UseMist=false;p.NativeReplaceBody=false;p.NativeScale=.32f;p.NativeImpactScale=.6f;
            p.NativeFieldPrefab=null;p.SourcePattern=r.patternSource;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();r.afterJson=JsonUtility.ToJson(p);r.technicalCheck=Check(p);
            File.WriteAllText(path,JsonUtility.ToJson(r,true));return r.status+"; "+r.technicalCheck+"; tris="+(r.totalSpikeTris+r.markTris);
        }
        static T Store<T>(T value,string path)where T:UnityEngine.Object
        {var old=AssetDatabase.LoadAssetAtPath<T>(path);if(old==null){AssetDatabase.CreateAsset(value,path);return value;}EditorUtility.CopySerialized(value,old);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}
        static Mesh Spike()
        {
            const int sides=10,row=11;
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();var colours=new List<Color>();
            float[] ys={0,.27f,.28f,.30f,.31f,.55f,.56f,.58f,.59f,.80f,.82f,.84f,1f};
            float[] widths={.065f,.058f,.067f,.067f,.057f,.048f,.056f,.056f,.047f,.040f,.047f,.040f,.028f};
            for(int j=0;j<ys.Length;j++)for(int k=0;k<=sides;k++)
            {
                float a=k*Mathf.PI*2/sides;float y=j==ys.Length-1?1f+.23f*Mathf.Cos(a):ys[j];
                v.Add(new Vector3(Mathf.Cos(a)*widths[j],y,Mathf.Sin(a)*widths[j]));uv.Add(new Vector2(k/(float)sides,y));colours.Add(Color.black);
                if(j>0&&k<sides){int b=(j-1)*row+k,n=j*row+k;t.AddRange(new[]{b,n,b+1,b+1,n,n+1});}
            }
            int rim=v.Count;
            // Separate pale cut rim and recessed inner wall, so the spear is hollow bamboo.
            for(int j=0;j<3;j++)for(int k=0;k<=sides;k++)
            {
                float a=k*Mathf.PI*2/sides;float y=j==2?.69f:1f+.23f*Mathf.Cos(a);float r=j==0?.028f:.017f;
                v.Add(new Vector3(Mathf.Cos(a)*r,y,Mathf.Sin(a)*r));uv.Add(new Vector2(k/(float)sides,y));colours.Add(Color.red);
                if(j>0&&k<sides){int b=rim+(j-1)*row+k,n=rim+j*row+k;t.AddRange(new[]{b,n,b+1,b+1,n,n+1});}
            }
            int floor=v.Count;v.Add(new Vector3(0,.69f,0));uv.Add(Vector2.one*.5f);colours.Add(Color.red);
            for(int k=0;k<sides;k++)t.AddRange(new[]{floor,rim+2*row+k+1,rim+2*row+k});
            int bottom=v.Count;v.Add(Vector3.zero);uv.Add(Vector2.zero);colours.Add(Color.black);
            for(int k=0;k<sides;k++)t.AddRange(new[]{bottom,k,k+1});
            var mesh=Vfx120BambooGuardBuilder.MeshOf("VFX120_BambooSpike_013",v,uv,t);mesh.SetColors(colours);return mesh;
        }
        static string Check(Vfx120Profile p)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("BambooSpike_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;
                e.Begin(Vector3.up,null,Vector3.forward*4,Color.white);
                if(e.BambooSpikesConfigured||e.PartCount!=0)throw new InvalidOperationException("Missing plan generated a field");
                var plan=new AreaImpactPlan{Shape=AreaShape.Circle,Point=new Vector3(1,0,4),Radius=2.3f,Delay=.62f};string before=JsonUtility.ToJson(plan);
                e.SetAreaPlan(plan);e.SetImpactClock(.2f);e.Begin(Vector3.up,null,Vector3.forward*4,Color.white);
                if(!e.BambooSpikesConfigured||e.BambooSpikesDelay!=.62f||e.BambooSpikesRadius!=2.3f||e.BambooSpikesCenter!=plan.Point)throw new InvalidOperationException("Circle plan not consumed");
                e.Sample(.48f);if(e.BambooSpikesRise!=0)throw new InvalidOperationException("Early rise");
                var scales=new Vector3[17];for(int i=0;i<17;i++)scales[i]=e.BambooSpikeScale(i);
                e.Sample(.62f);if(e.BambooSpikesRaised!=17||e.BambooSpikesRise!=1)throw new InvalidOperationException("Not complete on impact clock");
                for(int i=0;i<17;i++)
                {var point=e.BambooSpikePosition(i);var foot=Vfx120Effect.BambooSpikeFoot(i,2.3f);if(Vector2.Distance(new Vector2(point.x-1,point.z-4),foot)>.0001f||e.BambooSpikeScale(i)!=scales[i])throw new InvalidOperationException("Placement or fixed scale mismatch");}
                if(e.NativeImpact==null||Mathf.Abs(e.NativeImpactStartedAt-.62f)>.0001f)throw new InvalidOperationException("Impact motif ignores Circle delay");
                e.Sample(e.Life);if(e.BambooSpikesRaised!=0||e.BambooSpikesInstance.GetComponentsInChildren<MeshRenderer>().Any(r=>r.enabled))throw new InvalidOperationException("Lingering renderer");
                if(before!=JsonUtility.ToJson(plan))throw new InvalidOperationException("Plan mutated");
                plan.Radius=-1;e.SetAreaPlan(plan);e.Begin(Vector3.up,null,Vector3.forward*4,Color.white);
                if(e.BambooSpikesConfigured||e.PartCount!=0)throw new InvalidOperationException("Invalid radius generated field");
                return "PASS_CIRCLE_DELAY_SIMULTANEOUS_RISE_FIXED_SCALE_AND_QUIET_INVALID_PLAN";
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
