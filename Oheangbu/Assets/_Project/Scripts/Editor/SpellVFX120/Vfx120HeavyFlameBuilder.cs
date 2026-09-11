using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120HeavyFlameBuilder
    {
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
            string folder=Vfx120Editor.AssetRoot+"/FireBolt";
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/027_B09C.asset");
            string backup=Path.Combine(Vfx120Editor.Output,"FireOriginals/027_B09C.asset.txt");if(!File.Exists(backup))File.Copy(AssetDatabase.GetAssetPath(p),backup);
            var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/PF_FireBolt_025.prefab"));
            try
            {
                go.name="PF_HeavyFlame_027";
                foreach(var ps in go.transform.Find("Travel").GetComponentsInChildren<ParticleSystem>())
                {
                    ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;
                    if(ps.name=="Combustion"||ps.name=="TornFlame")
                    {main.startSize=new ParticleSystem.MinMaxCurve(.3f,.5f);main.startLifetime=new ParticleSystem.MinMaxCurve(.12f,.22f);var velocity=ps.velocityOverLifetime;velocity.z=new ParticleSystem.MinMaxCurve(-.65f,-.2f);var shape=ps.shape;shape.radius=.16f;}
                }
                var impact=go.transform.Find("Contact/ImpactFire").GetComponent<ParticleSystem>();var m=impact.main;m.startSize=new ParticleSystem.MinMaxCurve(.5f,.95f);var emission=impact.emission;emission.SetBursts(new[]{new ParticleSystem.Burst(0,54)});
                for(int i=0;i<3;i++)
                {
                    var jet=UnityEngine.Object.Instantiate(impact.gameObject,impact.transform.parent).GetComponent<ParticleSystem>();jet.name="FlameTongue_"+i;
                    jet.transform.localRotation=Quaternion.Euler(-40,i*120,0);var main=jet.main;main.maxParticles=32;main.startLifetime=new ParticleSystem.MinMaxCurve(.22f,.45f);main.startSpeed=new ParticleSystem.MinMaxCurve(1.8f,2.8f);main.startSize=new ParticleSystem.MinMaxCurve(.2f,.48f);
                    var v=jet.velocityOverLifetime;v.enabled=false;var shape=jet.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=12;shape.radius=.04f;
                    var em=jet.emission;em.SetBursts(new[]{new ParticleSystem.Burst(0,24)});
                }
                p.NativeBodyPrefab=PrefabUtility.SaveAsPrefabAsset(go,folder+"/PF_HeavyFlame_027.prefab");
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
            string path=folder+"/M_HeavyHit_156.mat";var pattern=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(pattern==null){pattern=new Material(AssetDatabase.LoadAssetAtPath<Material>(folder+"/M_HitPattern_110.mat"));AssetDatabase.CreateAsset(pattern,path);}
            pattern.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_156.png"));EditorUtility.SetDirty(pattern);
            p.PatternMaterial=pattern;p.NativeBodyMotion=Vfx120NativeBodyMotion.None;p.NativeReplaceBody=false;p.Count=1;p.RibbonCount=0;p.UseMist=false;p.NativeScale=.35f;p.NativeImpactScale=.85f;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();string check=Check(p);File.WriteAllText(Path.Combine(Vfx120Editor.Output,"heavy_flame_027_build.json"),JsonUtility.ToJson(new Report{technicalCheck=check},true));return check;
        }
        [Serializable]class Report{public string glyph="난",status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",technicalCheck;public int particleSystems=10,maxParticleCapacity=528,patternTris=2;public string scope="Dense travelling fire, simultaneous three flame tongues and Pattern156 impact. No secondary damage or added hit.";}
        static string Check(Vfx120Profile p)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("HeavyFlame_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.SetImpactClock(.6f);e.Begin(Vector3.up,null,new Vector3(0,1,4),Color.white);
                e.Sample(.3f);if(e.FireHitPatternOpacity!=0||e.FireBoltParticles==0)throw new Exception("Travel failed");
                e.Sample(.75f);if(e.FireHitPatternOpacity<.9f||e.FireBoltParticles<50||e.FireBoltParticles>528)throw new Exception("Heavy impact missing");
                e.Sample(e.Life);if(e.FireHitPatternOpacity!=0||e.FireBoltParticles!=0)throw new Exception("Lingering effect");
                return "PASS_COMPACT_TRAVEL_SINGLE_HEAVY_IMPACT_PATTERN_CLEANUP";
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
