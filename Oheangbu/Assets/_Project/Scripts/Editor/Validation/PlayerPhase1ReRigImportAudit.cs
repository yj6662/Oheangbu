#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace Oheangbu.EditorTools
{
    public static class PlayerPhase1ReRigImportAudit
    {
        const string Model = "Assets/_Project/Art/Models/PlayerPhase1Validation/ReRig/Dosa_Phase1_Rerig.fbx";
        [Serializable] public class Report { public bool avatarValid; public bool humanoid; public int triangles; public int skinnedMeshes; public string[] clips; public string status; }
        public static string Run()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var map = new Dictionary<string,string> { {"Hips","Hips"},{"Spine","Spine02"},{"Chest","Spine01"},{"UpperChest","Spine"},{"Neck","neck"},{"Head","Head"} };
            foreach(var side in new[]{"Left","Right"})
            {
                foreach(var pair in new[]{("UpperLeg","UpLeg"),("LowerLeg","Leg"),("Foot","Foot"),("Toes","ToeBase"),("Shoulder","Shoulder"),("UpperArm","Arm"),("LowerArm","ForeArm"),("Hand","Hand")}) map[side+pair.Item1]=side+pair.Item2;
                foreach(var finger in new[]{"Thumb","Index","Middle","Ring","Little"})
                {
                    var joints=new[]{"Proximal","Intermediate","Distal"};
                    for(int i=0;i<3;i++)map[side+" "+finger+" "+joints[i]]=side+"Hand"+finger+(i+1);
                }
            }
            var desc = importer.humanDescription;
            desc.human = map.Select(p=>new HumanBone{humanName=p.Key,boneName=p.Value,limit=new HumanLimit{useDefaultValues=true}}).ToArray();
            desc.skeleton=model.GetComponentsInChildren<Transform>(true).Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray();
            importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.humanDescription=desc;importer.isReadable=true;importer.SaveAndReimport();
            model=AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var assets=AssetDatabase.LoadAllAssetsAtPath(Model);var avatar=assets.OfType<Avatar>().FirstOrDefault();
            var meshes=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var result=new Report{avatarValid=avatar!=null&&avatar.isValid,humanoid=avatar!=null&&avatar.isHuman,triangles=meshes.Sum(r=>r.sharedMesh.triangles.Length/3),skinnedMeshes=meshes.Length,clips=assets.OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).Select(c=>c.name).ToArray()};
            result.status=result.avatarValid&&result.humanoid&&result.triangles<=60000?"PASS":"FAIL";
            var json=JsonUtility.ToJson(result,true);File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlayerPhase1/ReRig/unity_import_audit.json")),json);return json;
        }
    }
}
#endif

