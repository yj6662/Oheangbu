using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactRebuildAuthoring
    {
        // Place wall fixtures in the gallery's local frame, never along a fixed world axis.
        static void AlignGalleryLantern(Transform lantern,CaveLayoutData data,int index,Transform mine)
        {
            Vector3 center=data.lights[index],tangent=Vector3.forward;float nearest=float.PositiveInfinity;
            foreach(var path in new[]{data.main,data.branch,data.recess})
            for(int k=1;k<path.Length;k++)
            {
                Vector3 delta=path[k]-path[k-1];delta.y=0;
                if(delta.sqrMagnitude<.001f)continue;
                Vector3 relative=center-path[k-1];relative.y=0;
                float t=Mathf.Clamp01(Vector3.Dot(relative,delta)/delta.sqrMagnitude);
                float distance=(relative-delta*t).sqrMagnitude;
                if(distance<nearest){nearest=distance;tangent=delta.normalized;}
            }
            var rotation=Quaternion.LookRotation(tangent,Vector3.up);
            var post=center+rotation*new Vector3(2.35f,0,0);
            var floor=mine.GetComponentsInChildren<MeshCollider>(true).Single(c=>c.name=="Natural_Cave_Floor");
            if(floor.Raycast(new Ray(post+Vector3.up*10,Vector3.down),out var hit,20))center.y=hit.point.y;
            lantern.SetPositionAndRotation(center+rotation*new Vector3(1.6f,3.3f,0),rotation);
        }

        static string FixGallerySupports()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
            var data=JsonUtility.FromJson<CaveLayoutData>(File.ReadAllText(Output+"/CaveV4/geometry.json"));
            var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
            var original=SceneManager.GetActiveScene();var scene=EditorSceneManager.OpenScene(receipt.scene,OpenSceneMode.Additive);
            try
            {
                var mine=scene.GetRootGameObjects().Single(g=>g.name=="mine").transform;
                var lines=new List<string>();
                for(int i=0;i<data.lights.Length;i++)
                {
                    var lantern=mine.Find("Cave_V4_Dressing/Gallery_Lantern_"+i);
                    if(lantern==null)throw new Exception("Missing authored lantern "+i);
                    AlignGalleryLantern(lantern,data,i,mine);
                    lines.Add(lantern.name+" wall-side post="+lantern.Find("Upright").position+" yaw="+lantern.eulerAngles.y);
                }
                Physics.SyncTransforms();EditorSceneManager.SaveScene(scene);
                string result=string.Join("\n",lines);File.WriteAllText(Output+"/CaveV4/supports236.txt",result);return result;
            }
            finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
        }

        static string ContinuationSurvey(bool configure)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
            var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
            var original=SceneManager.GetActiveScene();var originalRoots=original.GetRootGameObjects().Where(g=>g.activeSelf).ToArray();
            var scene=EditorSceneManager.OpenScene(receipt.scene,OpenSceneMode.Additive);
            GameObject probe=null;
            try
            {
                foreach(var g in originalRoots)g.SetActive(false);
                var roots=scene.GetRootGameObjects();var s=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
                var rig=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlayerGestureRig>(true)).Single();
                var lines=new List<string>{"Gesture profile="+AssetDatabase.GetAssetPath(rig.Profile)};
                if(configure)
                {
                    string folder=Path.GetDirectoryName(receipt.scene).Replace('\\','/');
                    var profile=Object.Instantiate(rig.Profile);profile.name="PlayerGesture_Compact236";
                    profile.ArticulatedStrokes=true;profile.WristAnchorWeight=45;profile.WristStrokeSpan=.055f;profile.ArmStrokeSpan=.24f;profile.DirectionalElbowWeight=.55f;
                    profile=SavePrivate(profile,folder+"/PlayerGesture236.asset");
                    var so=new SerializedObject(rig);so.FindProperty("_profile").objectReferenceValue=profile;so.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
                    lines.Add("PASS candidate-only articulated profile saved");
                }
                var moving=roots.Where(g=>g.activeSelf&&(g.GetComponentInChildren<WorldMacroCombatWalker>(true)!=null||g.name=="WorldMacro_Playtest"||g.name=="Macro_CombatPlayerRig")).ToArray();
                foreach(var g in moving)g.SetActive(false);Physics.SyncTransforms();
                var source=s.Walker.Body;
                probe=new GameObject("ContinuationCapsuleProbe");SceneManager.MoveGameObjectToScene(probe,scene);
                var body=probe.AddComponent<CharacterController>();body.height=source.height;body.radius=source.radius;body.center=source.center;body.skinWidth=source.skinWidth;
                void Inspect(Vector3 p,string label)
                {
                    probe.transform.position=p;Physics.SyncTransforms();
                    var center=p+body.center;float half=Mathf.Max(0,body.height*.5f-body.radius);
                    foreach(var c in Physics.OverlapCapsule(center+Vector3.up*half,center-Vector3.up*half,body.radius+.03f,~0,QueryTriggerInteraction.Ignore).Where(c=>c!=body&&c.gameObject.scene==scene))
                    {
                        bool penetrates=Physics.ComputePenetration(body,p,Quaternion.identity,c,c.transform.position,c.transform.rotation,out var normal,out float distance);
                        if(!penetrates){normal=Vector3.zero;distance=0;}
                        lines.Add(label+" "+p+" collider="+HierarchyPath(c.transform)+" bounds="+c.bounds+" penetration="+penetrates+" depth="+distance+" normal="+normal);
                    }
                }
                Inspect(new Vector3(3549.77f,135.79f,1778),"START_STALL");
                foreach(var p in s.Content.MainPath.Take(3))Inspect(p,"AUTHORED");
                foreach(var t in roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>t.name=="Continuous_Approach_Soil"))
                    lines.Add("APPROACH "+HierarchyPath(t)+" "+t.position+" scale="+t.lossyScale);
                foreach(var g in moving)g.SetActive(true);
                var report=string.Join("\n",lines);File.WriteAllText(Output+"/continuation236_survey.txt",report);return report;
            }
            finally
            {
                if(probe!=null)Object.DestroyImmediate(probe);EditorSceneManager.CloseScene(scene,true);
                foreach(var g in originalRoots)if(g!=null)g.SetActive(true);Physics.SyncTransforms();SceneManager.SetActiveScene(original);
            }
        }
    }
}
