using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120AttachedFlameBuilder
    {
        public static string Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
            string folder=Vfx120Editor.AssetRoot+"/FireBolt";
            var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/026_B099.asset");
            string backup=Path.Combine(Vfx120Editor.Output,"FireOriginals/026_B099.asset.txt");if(!File.Exists(backup))File.Copy(AssetDatabase.GetAssetPath(p),backup);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/PF_FireBolt_025.prefab");var go=UnityEngine.Object.Instantiate(source);
            try
            {
                go.name="PF_AttachedFlame_026";
                foreach(var ps in go.transform.Find("Contact").GetComponentsInChildren<ParticleSystem>())
                {
                    ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main=ps.main;main.loop=true;main.duration=10;
                    var emission=ps.emission;emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
                    emission.rateOverTime=ps.name=="ImpactFire"?62:ps.name=="ImpactCinders"?16:12;
                    var velocity=ps.velocityOverLifetime;
                    velocity.x=new ParticleSystem.MinMaxCurve(-.2f,.2f);velocity.y=new ParticleSystem.MinMaxCurve(.6f,1.35f);velocity.z=new ParticleSystem.MinMaxCurve(-.17f,.17f);
                    var shape=ps.shape;shape.radius=.18f;
                }
                p.NativeBodyPrefab=PrefabUtility.SaveAsPrefabAsset(go,folder+"/PF_AttachedFlame_026.prefab");
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
            p.NativeBodyMotion=Vfx120NativeBodyMotion.None;p.NativeReplaceBody=false;p.Duration=4.2f;p.Count=1;p.RibbonCount=0;p.UseMist=false;
            p.PatternMaterial=AssetDatabase.LoadAssetAtPath<Material>(folder+"/M_HitPattern_110.mat");p.NativeScale=.26f;p.NativeImpactScale=.7f;
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();
            string check=Check(p);File.WriteAllText(Path.Combine(Vfx120Editor.Output,"attached_flame_026_build.json"),JsonUtility.ToJson(new Report{technicalCheck=check},true));return check;
        }
        [Serializable]class Report{public string glyph="낙",status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",technicalCheck;public int particleSystems=7,maxParticleCapacity=432,patternTris=2;public string scope="Attached rising flame and persistent KTP motif; no damage ticks implemented.";}
        static string Check(Vfx120Profile p)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("AttachedFlame_Check");var target=new GameObject("Receiver");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(target,scene);target.transform.position=new Vector3(0,0,4);
            try
            {
                var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.SetImpactClock(.6f);e.Begin(Vector3.up,target.transform,target.transform.position,Color.white);
                e.Sample(.4f);if(e.FireHitPatternOpacity!=0)throw new Exception("Early mark");
                e.Sample(2);if(e.FireHitPatternOpacity<.6f||e.FireBoltParticles==0)throw new Exception("Sustained fire/motif missing");
                var motif=go.GetComponentsInChildren<MeshRenderer>();var block=new MaterialPropertyBlock();bool found=false;
                foreach(var renderer in motif)if(renderer.name=="KTP_FireContact_110")
                {renderer.GetPropertyBlock(block);if(block.GetFloat("_Erode")!=0||!renderer.enabled)throw new Exception("Sustained pattern erased in shader");found=true;}
                if(!found)throw new Exception("Pattern renderer absent");
                target.transform.position+=Vector3.right;
                e.Sample(2.2f);var contact=go.transform.Find("PF_AttachedFlame_026(Clone)/Contact");if(contact==null||Mathf.Abs(contact.position.x-1)>.002f)throw new Exception("Attachment did not follow");
                e.Sample(e.Life);if(e.FireBoltParticles!=0||e.FireHitPatternOpacity!=0)throw new Exception("Lingering effect");
                return "PASS_LATE_BURN_PATTERN_ATTACHMENT_AND_CLEANUP";
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(target);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
