using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120FireBoltBuilder
    {
        const string Folder="Assets/_Project/Art/SpellVFX120/FireBolt";
        static Material fire,smoke,ember;
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit required");
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/025_B098.asset");
            var shader=Shader.Find("Oheangbu/VFX120/CombustionParticle");
            if(p==null||shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Profile/shader unavailable");
            string backup=Path.Combine(Vfx120Editor.Output,"FireOriginals");Directory.CreateDirectory(backup);
            string original=Path.Combine(backup,"025_B098.asset.txt");if(!File.Exists(original))File.Copy(AssetDatabase.GetAssetPath(p),original);
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"FireBolt");
            fire=Material(shader,"M_Flame","par_Fire.png",false,1.7f);
            fire.SetFloat("_AtlasColumns",8);EditorUtility.SetDirty(fire);
            smoke=Material(shader,"M_Smoke","Noise_02.png",true,1);
            ember=Material(shader,"M_Ember","pointLight.png",false,2.1f);
            string patternPath=Folder+"/M_HitPattern_110.mat";
            var pattern=AssetDatabase.LoadAssetAtPath<Material>(patternPath);
            if(pattern==null){pattern=new Material(Shader.Find("Oheangbu/VFX120/SeedLeafMark"));AssetDatabase.CreateAsset(pattern,patternPath);}
            pattern.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_110.png"));
            pattern.SetFloat("_Pattern",1);pattern.SetFloat("_Alpha",1);pattern.SetFloat("_Erode",0);pattern.renderQueue=3100;EditorUtility.SetDirty(pattern);p.PatternMaterial=pattern;
            var root=new GameObject("PF_FireBolt_025");root.SetActive(false);
            try
            {
                var travel=Child(root.transform,"Travel");var contact=Child(root.transform,"Contact");
                var core=System(travel,"Combustion",fire,96,145,.16f,.28f,.16f,.31f,true);
                Velocity(core,new Vector3(0,.25f,-1.2f),new Vector3(.12f,.3f,.6f));Noise(core,.13f,.8f);
                var tongues=System(travel,"TornFlame",fire,96,100,.24f,.4f,.22f,.43f,true);
                Velocity(tongues,new Vector3(0,.45f,-1.7f),new Vector3(.3f,.45f,.8f));Noise(tongues,.23f,1.5f);
                var sparks=System(travel,"TrailingEmbers",ember,48,48,.28f,.52f,.012f,.029f,false);
                Velocity(sparks,new Vector3(0,.45f,-.7f),new Vector3(.45f,.3f,.45f));Gravity(sparks,.13f);
                var plume=System(travel,"CoolingSmoke",smoke,40,26,.35f,.65f,.2f,.42f,false);
                Velocity(plume,new Vector3(0,.6f,-.15f),new Vector3(.14f,.2f,.12f));Noise(plume,.14f,.65f);SmokeColor(plume);
                var flash=System(contact,"ImpactFire",fire,64,0,.28f,.58f,.35f,.7f,true);Burst(flash,38);Velocity(flash,new Vector3(0,1.1f,0),new Vector3(1.5f,.9f,1.5f));Noise(flash,.27f,1.2f);
                var cinders=System(contact,"ImpactCinders",ember,48,0,.35f,.75f,.013f,.032f,false);Burst(cinders,32);Velocity(cinders,new Vector3(0,1.3f,0),new Vector3(2,1.1f,2));Gravity(cinders,.5f);
                var residue=System(contact,"ImpactSmoke",smoke,40,0,.45f,.8f,.28f,.64f,false);Burst(residue,18);Velocity(residue,new Vector3(0,.8f,0),new Vector3(.4f,.3f,.4f));Noise(residue,.2f,.8f);SmokeColor(residue);
                root.SetActive(true);p.NativeBodyPrefab=PrefabUtility.SaveAsPrefabAsset(root,Folder+"/PF_FireBolt_025.prefab");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
            p.NativeBodyMotion=Vfx120NativeBodyMotion.None;p.NativeReplaceBody=false;p.Count=1;p.RibbonCount=0;p.UseMist=false;p.NativeScale=.23f;p.NativeImpactScale=.55f;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();
            string check=Check(p);
            File.WriteAllText(Path.Combine(Vfx120Editor.Output,"fire_bolt_025_build.json"),JsonUtility.ToJson(new Report{technicalCheck=check},true));
            return check;
        }
        [Serializable]class Report{public string glyph="나",status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",technicalCheck,art="AWAITING_USER_REVIEW";public int particleSystems=7,maxParticleCapacity=432,bodyMeshTriangles=0;public string sources="KTP/PublicTexture/par_Fire.png (8x1 atlas), Noise_02.png, pointLight.png; existing KTP cast/impact prefabs";public string scope="Visual combustion only; no damage, speed, lights, collisions or gameplay edits";}
        static Transform Child(Transform parent,string name){var go=new GameObject(name);go.transform.SetParent(parent,false);return go.transform;}
        static Material Material(Shader shader,string name,string texture,bool isSmoke,float intensity)
        {
            string path=Folder+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}m.shader=shader;m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/KoreanTraditionalPattern_Effect/Textures/PublicTexture/"+texture));m.SetFloat("_Smoke",isSmoke?1:0);m.SetFloat("_Intensity",intensity);m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)(isSmoke?BlendMode.OneMinusSrcAlpha:BlendMode.One));EditorUtility.SetDirty(m);return m;
        }
        static ParticleSystem System(Transform parent,string name,Material material,int cap,float rate,float minLife,float maxLife,float minSize,float maxSize,bool atlas)
        {
            var ps=Child(parent,name).gameObject.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.playOnAwake=false;main.loop=true;main.duration=10;main.maxParticles=cap;main.startLifetime=new ParticleSystem.MinMaxCurve(minLife,maxLife);main.startSpeed=0;main.startSize=new ParticleSystem.MinMaxCurve(minSize,maxSize);main.startRotation=new ParticleSystem.MinMaxCurve(-.45f,.45f);main.simulationSpace=ParticleSystemSimulationSpace.World;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;main.useUnscaledTime=false;
            ps.useAutoRandomSeed=false;ps.randomSeed=(uint)(100+cap+rate);
            var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=parent.name=="Travel"?.075f:.12f;
            var em=ps.emission;em.enabled=true;em.rateOverTime=rate;
            var col=ps.colorOverLifetime;col.enabled=true;var grad=new Gradient();grad.SetKeys(new[]{new GradientColorKey(new Color(1,.95f,.75f),0),new GradientColorKey(new Color(1,.52f,.18f),.45f),new GradientColorKey(new Color(.48f,.1f,.025f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.85f,.12f),new GradientAlphaKey(.55f,.6f),new GradientAlphaKey(0,1)});col.color=grad;
            var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.45f),new Keyframe(.3f,1),new Keyframe(1,.7f)));
            if(atlas){var sheet=ps.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=8;sheet.numTilesY=1;sheet.frameOverTime=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,0,1,1));sheet.cycleCount=1;}
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.sortMode=ParticleSystemSortMode.Distance;
            return ps;
        }
        static void Velocity(ParticleSystem ps,Vector3 v,Vector3 spread){var m=ps.velocityOverLifetime;m.enabled=true;m.space=ParticleSystemSimulationSpace.Local;m.x=new ParticleSystem.MinMaxCurve(v.x-spread.x,v.x+spread.x);m.y=new ParticleSystem.MinMaxCurve(v.y-spread.y,v.y+spread.y);m.z=new ParticleSystem.MinMaxCurve(v.z-spread.z,v.z+spread.z);}
        static void Noise(ParticleSystem ps,float strength,float frequency){var n=ps.noise;n.enabled=true;n.strength=strength;n.frequency=frequency;n.scrollSpeed=.7f;n.octaveCount=2;n.quality=ParticleSystemNoiseQuality.Medium;}
        static void Gravity(ParticleSystem ps,float value){var m=ps.main;m.gravityModifier=value;}
        static void Burst(ParticleSystem ps,short count){var e=ps.emission;e.SetBursts(new[]{new ParticleSystem.Burst(0,count)});var m=ps.main;m.loop=false;m.duration=1;}
        static void SmokeColor(ParticleSystem ps){var c=ps.colorOverLifetime;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(new Color(.13f,.12f,.11f),0),new GradientColorKey(new Color(.28f,.27f,.25f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.23f,.18f),new GradientAlphaKey(0,1)});c.color=g;var s=ps.sizeOverLifetime;s.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.35f,1,1.7f));}
        static string Check(Vfx120Profile p)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("FireBolt_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.SetImpactClock(.8f);e.Begin(Vector3.up,null,new Vector3(0,1,4),Color.white);
                e.Sample(.4f);if(!e.FireBoltConfigured||e.FireBoltParticles==0||Vector3.Distance(e.FireBoltPosition,new Vector3(0,1,2))>.002f)throw new Exception("Travel particles/path failed: configured="+e.FireBoltConfigured+" particles="+e.FireBoltParticles+" position="+e.FireBoltPosition);
                e.Sample(1);if(!e.FireBoltImpacted||e.FireBoltParticles==0||e.NativeImpact==null||e.FireHitPatternOpacity<.9f)throw new Exception("Impact or KTP motif missing");
                e.Sample(e.Life);if(e.FireBoltParticles!=0||e.FireHitPatternOpacity!=0)throw new Exception("Particles or motif remain at end");
                int previous=-1;
                foreach(int fps in new[]{30,60,120})
                {
                    e.SetImpactClock(.8f);e.Begin(Vector3.up,null,new Vector3(0,1,4),Color.white);
                    for(int f=1;f<=fps;f++)e.Sample(f/(float)fps);
                    if(previous>=0&&previous!=e.FireBoltParticles)throw new Exception("Particle count differs by sampling frame rate");
                    previous=e.FireBoltParticles;
                }
                e.Begin(Vector3.up,null,new Vector3(0,1,4),Color.white);e.SetImpactClock(0);e.Sample(1);if(e.FireBoltImpacted)throw new Exception("Unconfirmed impact fabricated");
                return "PASS_PARTICLES_KTP_HIT_MOTIF_PATH_LIFETIME_30_60_120_SAMPLING";
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
