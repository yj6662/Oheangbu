using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Core.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class DemoTigerAuthoring
    {
        public const string Folder="Assets/_Project/Art/Demo/Summons/MetalTiger";
        public const string Model=Folder+"/SM_MetalTiger_Combat.fbx";
        public const string ProfilePath=Folder+"/Combat_Som.asset";
        const string Original="Assets/_Project/Art/SpellVFX120/MetalTiger";
        [Serializable] sealed class Report
        {
            public string status;
            public List<string> passed=new List<string>(),failed=new List<string>();
            public int tris,skins,materials,bones,maximumInfluences,unweighted;
            public float weightError;
            public Vector3 boundsSize;
            public string[] clips;
            public string[] unverified={"Actual input", "Terrain paw contacts", "Final tiger skinning and gait", "CPU/GPU performance"};
        }
        public static string Execute(string command)
        {
            if(command=="apply")Apply();else if(command!="audit")throw new ArgumentException("tiger: apply/audit");
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
            importer.isReadable=true;importer.animationCompression=ModelImporterAnimationCompression.Off;importer.SaveAndReimport();
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var clips=AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
            AnimationClip Clip(string suffix)=>clips.Single(c=>c.name.EndsWith(suffix,StringComparison.Ordinal));
            var idle=Clip("MT_Idle");var walk=Clip("MT_Walk");var run=Clip("MT_Run");var leap=Clip("MT_Leap");
            var left=Clip("MT_ClawLeft");var right=Clip("MT_ClawRight");
            var material=AssetDatabase.LoadAssetAtPath<Material>(Original+"/M_BrushedSilver.mat");
            if(material==null)throw new InvalidOperationException("Approved silver material missing");
            var temporary=Object.Instantiate(model);temporary.name="PF_MetalTiger_Combat";
            try
            {
                foreach(var renderer in temporary.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
                var animator=temporary.GetComponent<Animator>()??temporary.AddComponent<Animator>();animator.applyRootMotion=false;
                var profile=AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(ProfilePath);
                if(profile==null){profile=ScriptableObject.CreateInstance<SummonCombatProfile>();AssetDatabase.CreateAsset(profile,ProfilePath);}
                profile.Letter="솜";profile.Element=Element.Metal;profile.RootAttackEnabled=false;profile.FlameAttackEnabled=false;profile.TigerAttackEnabled=true;
                profile.IdleClip=idle;profile.WalkClip=walk;profile.AttackClip=left;
                profile.TigerRunClip=run;profile.TigerLeapClip=leap;profile.TigerClawLeftClip=left;profile.TigerClawRightClip=right;
                profile.WalkClipMetresPerSecond=1.076923f;profile.TigerRunClipMetresPerSecond=2.75f;
                profile.FollowSpeed=2;profile.TigerRunSpeed=4;profile.DamageMultiplier=.5f;profile.TigerClawDamageMultiplier=.5f;
                profile.AttackRange=2.4f;profile.TigerLeapMinRange=3;profile.TigerLeapMaxRange=5;profile.TigerLandingStandOff=2;
                profile.TigerLeapPrepareSeconds=.2f;profile.TigerLeapLandSeconds=.8f;profile.TigerLeapSeconds=1.2f;
                profile.TigerClawSeconds=.8f;profile.TigerClawContactSeconds=.3f;profile.TigerLeapArcHeight=.38f;profile.TigerRecoverySeconds=.35f;
                profile.WindupSeconds=.3f;profile.CooldownSeconds=.35f;
                profile.FormationSeal=AssetDatabase.LoadAssetAtPath<GameObject>(Original+"/KTP_Som_Seal.prefab");
                profile.DissolveDebris=AssetDatabase.LoadAssetAtPath<GameObject>(Original+"/PF_Som_Dissolve.prefab");
                idle.SampleAnimation(temporary,0);Bounds bound=default;bool hasPoint=false;
                foreach(var renderer in temporary.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var baked=new Mesh();
                    try
                    {
                        renderer.BakeMesh(baked,false);
                        foreach(var vertex in baked.vertices)
                        {Vector3 point=renderer.transform.TransformPoint(vertex);if(!hasPoint){bound=new Bounds(point,Vector3.zero);hasPoint=true;}else bound.Encapsulate(point);}
                    }
                    finally{Object.DestroyImmediate(baked);}
                }
                if(!hasPoint)throw new InvalidOperationException("Idle pose has no skinned surface");
                profile.BodyHeight=bound.max.y-temporary.transform.position.y+.05f;
                profile.FootprintWidth=bound.size.x+.06f;profile.FootprintLength=bound.size.z+.06f;
                Vector3 centre=temporary.transform.InverseTransformPoint(bound.center);profile.FootprintOffset=new Vector3(centre.x,0,centre.z);
                Object.DestroyImmediate(temporary);temporary=Object.Instantiate(model);temporary.name="PF_MetalTiger_Combat";
                foreach(var renderer in temporary.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
                animator=temporary.GetComponent<Animator>()??temporary.AddComponent<Animator>();animator.applyRootMotion=false;
                profile.PresentationPrefab=PrefabUtility.SaveAsPrefabAsset(temporary,Folder+"/PF_MetalTiger_Combat.prefab");
                if(!profile.TryValidate(out string error,true))throw new InvalidOperationException(error);
                var manager=session.Walker.Wiring.GetComponent<DemoSummonCombatManager>();
                if(manager==null)throw new InvalidOperationException("Existing combat summon manager missing");
                manager.Profiles=(manager.Profiles??Array.Empty<SummonCombatProfile>()).Where(p=>p!=null&&p.Letter!="솜").Append(profile).ToArray();
                EditorUtility.SetDirty(profile);EditorUtility.SetDirty(manager);AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(session.gameObject.scene);EditorSceneManager.SaveScene(session.gameObject.scene);
            }
            finally{if(temporary!=null)Object.DestroyImmediate(temporary);}
        }
        static string Audit()
        {
            var r=new Report();void Check(bool ok,string message)=>(ok?r.passed:r.failed).Add(message);
            var profile=AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(ProfilePath);
            Check(profile!=null&&profile.TryValidate(out _,true),"Valid dedicated tiger combat profile");
            if(profile!=null&&profile.PresentationPrefab!=null)
            {
                var skins=profile.PresentationPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);r.skins=skins.Length;
                r.bones=skins.SelectMany(s=>s.bones).Distinct().Count();
                r.boundsSize=new Vector3(profile.FootprintWidth-.06f,profile.BodyHeight-.05f,profile.FootprintLength-.06f);
                foreach(var skin in skins)
                {
                    for(int i=0;i<skin.sharedMesh.subMeshCount;i++)r.tris+=(int)skin.sharedMesh.GetIndexCount(i)/3;
                    r.materials+=skin.sharedMaterials.Length;
                    var counts=skin.sharedMesh.GetBonesPerVertex();var weights=skin.sharedMesh.GetAllBoneWeights();
                    try{int cursor=0;foreach(byte count in counts){r.maximumInfluences=Mathf.Max(r.maximumInfluences,count);if(count==0)r.unweighted++;
                        float sum=0;for(int i=0;i<count;i++)sum+=weights[cursor++].weight;r.weightError=Mathf.Max(r.weightError,Mathf.Abs(1-sum));}}
                    finally{counts.Dispose();weights.Dispose();}
                }
                Check(r.skins==1&&r.bones>=20,"Existing tiger now has a real deform skeleton");
                Check(r.tris==12414&&r.materials==1,"Original tiger triangle budget and silver surface retained");
                Check(r.maximumInfluences<=4&&r.unweighted==0&&r.weightError<=.0001f,"Imported skin uses normalized maximum four weights");
                Check(profile.PresentationPrefab.GetComponentsInChildren<Collider>(true).Length==0,"Tiger presentation cannot physically block enemies");
                var clips=new[]{profile.IdleClip,profile.WalkClip,profile.TigerRunClip,profile.TigerLeapClip,profile.TigerClawLeftClip,profile.TigerClawRightClip};
                r.clips=clips.Select(c=>c==null?"MISSING":c.name).ToArray();Check(!r.clips.Contains("MISSING"),"All six locomotion, leap and alternating claw clips imported");
                Check(Mathf.Abs(profile.TigerLeapClip.length-1.2f)<.01f&&Mathf.Abs(profile.TigerClawLeftClip.length-.8f)<.01f&&Mathf.Abs(profile.TigerClawRightClip.length-.8f)<.01f,"Clip durations match production contact schedule");
                var manager=Object.FindFirstObjectByType<DemoSummonCombatManager>();
                Check(manager!=null&&manager.Profiles.Contains(profile)&&manager.Profiles.Any(p=>p.Letter=="곰")&&manager.Profiles.Any(p=>p.Letter=="놈"),"Tiger joins both implemented summons without replacing their profiles");
            }
            r.status=r.failed.Count==0?"PASS":"FAIL";string json=JsonUtility.ToJson(r,true);
            File.WriteAllText(Path.Combine(DemoSummonAuthoring.Output,"tiger_scene_audit.json"),json);return json;
        }
    }
}
