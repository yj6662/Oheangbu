using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class DemoHaetaeAuthoring
    {
        public const string Folder="Assets/_Project/Art/Demo/Summons/FireHaetae";
        public const string Model=Folder+"/SM_FireHaetae_Combat.fbx";
        public const string ProfilePath=Folder+"/Combat_Nom.asset";
        const string Original="Assets/_Project/Art/SpellVFX120/FireHaetae";
        [Serializable] class Report
        {
            public string status;
            public List<string> passed=new List<string>(),failed=new List<string>();
            public int tris,skins,materials,bones,maximumInfluences,unweighted;
            public float weightError;
            public Vector3 boundsSize,mouthOffset;
            public string[] clips;
            public string[] unverified={"Actual input", "Full terrain movement", "Visual approval", "CPU/GPU performance"};
        }
        public static string Execute(string command)
        {
            if(command=="apply") Apply();
            else if(command!="audit")throw new ArgumentException("haetae: apply/audit");
            return Audit();
        }
        static void Apply()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit mode only");
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(session==null||session.gameObject.scene.path!=DemoFoundationAuthoring.Scene||session.Content.Campaign==null)
                throw new InvalidOperationException("Dedicated demo scene required");
            if(!File.Exists(Model))throw new FileNotFoundException("Rig derivative required",Model);
            AssetDatabase.ImportAsset(Model,ImportAssetOptions.ForceUpdate);
            var importer=(ModelImporter)AssetImporter.GetAtPath(Model);
            importer.importAnimation=true;importer.animationType=ModelImporterAnimationType.Generic;
            importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.optimizeGameObjects=false;
            importer.isReadable=true;importer.animationCompression=ModelImporterAnimationCompression.Off;
            importer.SaveAndReimport();
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var clips=AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
            AnimationClip Clip(string suffix)=>clips.Single(c=>c.name.EndsWith(suffix,StringComparison.Ordinal));
            var idle=Clip("FH_Idle");var forward=Clip("FH_WalkForward");var backward=Clip("FH_WalkBackward");var attack=Clip("FH_FlameAttack");
            var shader=Shader.Find("Oheangbu/DemoFireHaetae");
            if(shader==null)throw new InvalidOperationException("Combat-only haetae shader missing");
            var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/M_CharcoalEmber_Combat.mat");
            if(material==null)
            { material=new Material(AssetDatabase.LoadAssetAtPath<Material>(Original+"/M_CharcoalEmber.mat"));AssetDatabase.CreateAsset(material,Folder+"/M_CharcoalEmber_Combat.mat"); }
            material.shader=shader;material.SetFloat("_Age",2);material.SetFloat("_MotionTime",0);EditorUtility.SetDirty(material);
            var temporary=Object.Instantiate(model);temporary.name="PF_FireHaetae_Combat";
            try
            {
                foreach(var r in temporary.GetComponentsInChildren<Renderer>(true))r.sharedMaterials=Enumerable.Repeat(material,r.sharedMaterials.Length).ToArray();
                var animator=temporary.GetComponent<Animator>()??temporary.AddComponent<Animator>();animator.applyRootMotion=false;
                var profile=AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(ProfilePath);
                if(profile==null){profile=ScriptableObject.CreateInstance<SummonCombatProfile>();AssetDatabase.CreateAsset(profile,ProfilePath);}
                profile.Letter="놈";profile.Element=Element.Fire;profile.RootAttackEnabled=false;profile.FlameAttackEnabled=true;
                profile.IdleClip=idle;profile.WalkClip=forward;profile.BackwardWalkClip=backward;profile.AttackClip=attack;profile.FlameAttackClip=attack;
                profile.WalkClipMetresPerSecond=.9230769f;profile.FollowSpeed=1.8f;profile.DamageMultiplier=1;
                profile.WindupSeconds=.45f;profile.CooldownSeconds=1.2f;
                profile.FlamePreferredDistance=3.5f;profile.FlameMinimumDistance=2.5f;profile.FlameRange=4.5f;profile.FlameHalfAngleDegrees=30;
                profile.FlameWindupSeconds=.45f;profile.FlameSpraySeconds=.65f;profile.FlameRecoverySeconds=.5f;profile.FlameDamageMultiplier=1;
                profile.FlamePrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/SpellVFX120/Traditional/Bodies/PF_KTP_FlameCone.prefab");
                profile.FlameConstrainVisual=true;
                profile.FlameAdditiveShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/DemoFlameAdditive.shadergraph");
                profile.FlameAlphaShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/DemoFlameAlpha.shadergraph");
                profile.FlameBoundaryFeather=.15f;profile.FlameVisualIntensity=.7f;
                profile.FormationSeal=AssetDatabase.LoadAssetAtPath<GameObject>(Original+"/KTP_Nom_Seal.prefab");
                profile.DissolveDebris=AssetDatabase.LoadAssetAtPath<GameObject>(Original+"/PF_Nom_Dissolve.prefab");
                // Dimensions and mouth are measured in the actual idle pose, not the source asymmetric bind pose.
                idle.SampleAnimation(temporary,0);
                var renderers=temporary.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if(renderers.Length==0)throw new InvalidOperationException("Skinned haetae required");
                Bounds bound=default;bool hasPoint=false;
                foreach(var renderer in renderers)
                {
                    var baked=new Mesh();
                    try
                    {
                        renderer.BakeMesh(baked,false);
                        foreach(var vertex in baked.vertices)
                        {
                            Vector3 point=renderer.transform.TransformPoint(vertex);
                            if(!hasPoint){bound=new Bounds(point,Vector3.zero);hasPoint=true;}else bound.Encapsulate(point);
                        }
                    }
                    finally{Object.DestroyImmediate(baked);}
                }
                if(!hasPoint)throw new InvalidOperationException("Idle pose produced no surface for footprint measurement");
                profile.BodyHeight=bound.max.y-temporary.transform.position.y+.05f;
                profile.FootprintWidth=bound.size.x+.06f;profile.FootprintLength=bound.size.z+.06f;
                Vector3 centre=temporary.transform.InverseTransformPoint(bound.center);profile.FootprintOffset=new Vector3(centre.x,0,centre.z);
                var mouth=temporary.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="MouthOrigin");
                profile.FlameOriginOffset=temporary.transform.InverseTransformPoint(mouth.position);
                // Keep the source bind skeleton in the saved prefab; the manual graph evaluates idle on preparation.
                Object.DestroyImmediate(temporary);temporary=Object.Instantiate(model);temporary.name="PF_FireHaetae_Combat";
                foreach(var r in temporary.GetComponentsInChildren<Renderer>(true))r.sharedMaterials=Enumerable.Repeat(material,r.sharedMaterials.Length).ToArray();
                animator=temporary.GetComponent<Animator>()??temporary.AddComponent<Animator>();animator.applyRootMotion=false;
                profile.PresentationPrefab=PrefabUtility.SaveAsPrefabAsset(temporary,Folder+"/PF_FireHaetae_Combat.prefab");
                if(!profile.TryValidate(out string error,true))throw new InvalidOperationException(error);
                var manager=session.Walker.Wiring.GetComponent<DemoSummonCombatManager>();
                if(manager==null)throw new InvalidOperationException("Existing dedicated combat summon manager required");
                manager.Profiles=(manager.Profiles??Array.Empty<SummonCombatProfile>()).Where(p=>p!=null&&p.Letter!="놈").Append(profile).ToArray();
                manager.PlayerVitals=session.Walker.Motor.GetComponent<PlayerVitals>();manager.VehicleSeat=Object.FindFirstObjectByType<WorldMacroPalanquinSeat>();
                EditorUtility.SetDirty(profile);EditorUtility.SetDirty(manager);AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(session.gameObject.scene);EditorSceneManager.SaveScene(session.gameObject.scene);
            }
            finally{if(temporary!=null)Object.DestroyImmediate(temporary);}
        }
        static string Audit()
        {
            var r=new Report();void Check(bool ok,string message)=>(ok?r.passed:r.failed).Add(message);
            var profile=AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(ProfilePath);
            Check(profile!=null&&profile.TryValidate(out _,true),"Valid dedicated fire combat profile");
            if(profile!=null&&profile.PresentationPrefab!=null)
            {
                var skins=profile.PresentationPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);r.skins=skins.Length;
                r.bones=skins.SelectMany(s=>s.bones).Distinct().Count();r.mouthOffset=profile.FlameOriginOffset;
                foreach(var skin in skins)
                {
                    for(int i=0;i<skin.sharedMesh.subMeshCount;i++)r.tris+=(int)skin.sharedMesh.GetIndexCount(i)/3;
                    r.materials+=skin.sharedMaterials.Length;r.boundsSize=new Vector3(profile.FootprintWidth-.06f,profile.BodyHeight-.05f,profile.FootprintLength-.06f);
                    var counts=skin.sharedMesh.GetBonesPerVertex();var weights=skin.sharedMesh.GetAllBoneWeights();
                    try{int cursor=0;foreach(byte count in counts){r.maximumInfluences=Mathf.Max(r.maximumInfluences,count);if(count==0)r.unweighted++;
                        float sum=0;for(int i=0;i<count;i++)sum+=weights[cursor++].weight;r.weightError=Mathf.Max(r.weightError,Mathf.Abs(1-sum));}}
                    finally{counts.Dispose();weights.Dispose();}
                }
                Check(r.skins==1&&r.bones>=18,"Existing haetae now has a real deform skeleton");
                Check(r.tris<=14000&&r.materials==1,"Original haetae geometry budget and one surface retained");
                Check(r.maximumInfluences<=4&&r.unweighted==0&&r.weightError<=.0001f,"Real imported weights are normalized with at most four influences");
                Check(profile.PresentationPrefab.GetComponentsInChildren<Collider>(true).Length==0,"Haetae does not physically block enemies or navigation");
                r.clips=new[]{profile.IdleClip,profile.WalkClip,profile.BackwardWalkClip,profile.FlameAttackClip}.Select(c=>c==null?"MISSING":c.name).ToArray();
                Check(!r.clips.Contains("MISSING"),"Idle, forward, backward and flame clips imported");
                Check(profile.FlamePrefab!=null&&profile.FlamePrefab.GetComponentsInChildren<ParticleSystem>(true).Length==10,"Existing KTP flame systems reused");
                Check(profile.FlameConstrainVisual&&profile.FlameAdditiveShader!=null&&profile.FlameAlphaShader!=null&&
                    !ShaderUtil.ShaderHasError(profile.FlameAdditiveShader)&&!ShaderUtil.ShaderHasError(profile.FlameAlphaShader),"Derived cone shaders are retained by the profile and compile without errors");
                var manager=Object.FindFirstObjectByType<DemoSummonCombatManager>();
                Check(manager!=null&&manager.Profiles.Contains(profile)&&manager.Profiles.Any(p=>p.Letter=="곰"),"Fire combat joins wood combat without replacing its profile");
            }
            r.status=r.failed.Count==0?"PASS":"FAIL";
            string json=JsonUtility.ToJson(r,true);Directory.CreateDirectory(DemoSummonAuthoring.Output);
            File.WriteAllText(Path.Combine(DemoSummonAuthoring.Output,"haetae_scene_audit.json"),json);return json;
        }
    }
}
