using System;
using System.IO;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class DemoChapterThreePresentationAuthoring
    {
        const string Model="Assets/_Project/Art/Demo/Chapter3/Cheongryong/SM_Cheongryong_Prototype.fbx";
        public static string Apply()
        {
            var session=UnityEngine.Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(EditorApplication.isPlaying||session==null||session.gameObject.scene.path!=DemoFoundationAuthoring.Scene)
                throw new InvalidOperationException("Dedicated demo Edit scene required");
            var boss=session.Actors.First(a=>a!=null&&a.Id==WorldMacroPlaytestSession.CheongryongId);
            var controller=boss.GetComponent<CheongryongCombatController>();
            var animator=boss.GetComponentInChildren<Animator>(true);
            if(animator==null)
            {
                var visual=boss.transform.Find("Chapter3_Visual");
                if(visual==null)throw new InvalidOperationException("Imported rig visual missing");
                animator=visual.gameObject.AddComponent<Animator>();
            }
            var clips=AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
            AnimationClip Clip(string name)=>clips.Single(c=>c.name.EndsWith(name,StringComparison.Ordinal));
            var rig=boss.GetComponent<CheongryongRigAnimation>();if(rig==null)rig=boss.gameObject.AddComponent<CheongryongRigAnimation>();
            // Serialize references without creating an Edit-mode playable graph.
            var so=new SerializedObject(rig);
            so.FindProperty("_animator").objectReferenceValue=animator;
            so.FindProperty("_idleClip").objectReferenceValue=Clip("CR_Idle");
            so.FindProperty("_headAttackClip").objectReferenceValue=Clip("CR_HeadAttack_Anticipation");
            so.FindProperty("_tailAttackClip").objectReferenceValue=Clip("CR_TailSweep_Anticipation");
            so.FindProperty("_controller").objectReferenceValue=controller;so.ApplyModifiedPropertiesWithoutUndo();
            animator.enabled=true;animator.applyRootMotion=false;animator.runtimeAnimatorController=null;
            var bones=boss.GetComponentsInChildren<Transform>(true);
            Transform Bone(string name)=>bones.Single(t=>t.name==name);
            // Cover the real muzzle, rather than a sphere at the neck pivot.
            var head=Bone("Head");var mouth=Bone("MouthOrigin");
            foreach(var collider in boss.GetComponentsInChildren<CapsuleCollider>(true).Where(c=>c.name.StartsWith("Damage_",StringComparison.Ordinal)))
            {
                var scale=collider.transform.parent.lossyScale;
                collider.transform.localScale=new Vector3(1/Mathf.Abs(scale.x),1/Mathf.Abs(scale.y),1/Mathf.Abs(scale.z));
                EditorUtility.SetDirty(collider.transform);
            }
            var headHit=head.Find("Damage_Head");
            if(headHit==null)throw new InvalidOperationException("Authored head damage collider missing");
            Vector3 localMouth=head.InverseTransformPoint(mouth.position);
            headHit.localPosition=localMouth*.5f;
            headHit.localRotation=Quaternion.FromToRotation(Vector3.up,localMouth.normalized);
            var headCapsule=headHit.GetComponent<CapsuleCollider>();
            headCapsule.center=Vector3.zero;headCapsule.direction=1;headCapsule.radius=.55f;
            headCapsule.height=Vector3.Distance(head.position,mouth.position)+headCapsule.radius*2;
            EditorUtility.SetDirty(headHit);EditorUtility.SetDirty(headCapsule);
            controller.ConfigureSockets(Bone("MouthOrigin"),Bone("Body_12"));
            float length=0;for(int i=12;i<24;i++)length+=Vector3.Distance(Bone("Body_"+i.ToString("00")).position,Bone("Body_"+(i+1).ToString("00")).position);
            length+=Vector3.Distance(Bone("Body_24").position,Bone("TailTip").position);
            controller.Profile.TailRange=length;EditorUtility.SetDirty(controller.Profile);
            var tail=boss.GetComponent<CheongryongTailSweepPresentation>();if(tail==null)tail=boss.gameObject.AddComponent<CheongryongTailSweepPresentation>();
            if(!tail.Configure(controller,Enumerable.Range(12,13).Select(i=>Bone("Body_"+i.ToString("00"))).ToArray(),Bone("TailTip")))
                throw new InvalidOperationException("Tail sweep chain configuration failed");
            EditorUtility.SetDirty(tail);
            var sync=boss.GetComponent<CheongryongColliderPoseSync>();
            if(sync==null)sync=boss.gameObject.AddComponent<CheongryongColliderPoseSync>();
            sync.Configure(boss.GetComponentsInChildren<Collider>(true).Where(c=>c.name.StartsWith("Damage_",StringComparison.Ordinal)).ToArray());
            EditorUtility.SetDirty(sync);
            session.DemoWoodLiftProfile=AssetDatabase.LoadAssetAtPath<Oheangbu.App.SpellVFX120.Vfx120Profile>("Assets/_Project/Art/SpellVFX120/Profiles/020_AD6D.asset");
            EditorUtility.SetDirty(session);
            Physics.SyncTransforms();
            EditorUtility.SetDirty(rig);EditorUtility.SetDirty(animator);EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(session.gameObject.scene);EditorSceneManager.SaveScene(session.gameObject.scene);
            string result=JsonUtility.ToJson(new Report{status="AUTHORED_UNVERIFIED",clips=clips.Select(c=>c.name).ToArray(),tailRange=length},true);
            File.WriteAllText(Path.Combine(DemoChapterThreeAuthoring.Output,"presentation_authoring.json"),result);return result;
        }
        [Serializable]sealed class Report{public string status;public string[] clips;public float tailRange;}
    }
}
