using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactRebuildAuthoring
    {
        static string Check271()
        {
            var scene=FrontageScene249();var s=VillageSession();var actor=s.Actors.Single(a=>a.Id==WorldMacroPlaytestSession.SinmokId);
            var group=actor.transform.Find("Creature271");var report=new List<string>();
            void C(bool ok,string name)=>report.Add((ok?"PASS ":"FAIL ")+name);
            C(group!=null&&actor.transform.Cast<Transform>().Count(t=>t.name=="Creature271")==1,"one idempotent creature assembly");
            var skins=group.GetComponentsInChildren<SkinnedMeshRenderer>();var rig=actor.GetComponent<SinmokRigAnimation>();
            report.Add("DETAIL actor "+actor.transform.position+" rotation "+actor.transform.eulerAngles+" scale "+actor.transform.lossyScale);
            foreach(var skin in skins)report.Add("DETAIL "+skin.name+" mesh "+skin.sharedMesh.bounds+" local "+skin.localBounds);
            C(skins.Length==3,"new anatomy combined into three material batches");
            C(skins.All(r=>r.bones.Length==45&&r.bones.All(b=>b!=null)),"all original 45 bones bound");
            C(skins.All(r=>r.sharedMesh.boneWeights.Length==r.sharedMesh.vertexCount&&r.sharedMesh.bindposes.Length==45),"complete skin weights and bind poses");
            C(skins.All(r=>r.sharedMesh.boneWeights.All(w=>w.boneIndex0<45&&w.boneIndex1<45&&Mathf.Abs(w.weight0+w.weight1-1)<.0001f)),"valid normalized weights");
            C(skins.All(r=>r.sharedMesh.vertices.All(p=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z))),"finite geometry");
            C(skins.All(r=>r.sharedMesh.triangles.All(i=>i>=0&&i<r.sharedMesh.vertexCount)),"triangle indices valid");
            C(skins.All(r=>r.sharedMesh.normals.Length==r.sharedMesh.vertexCount&&r.sharedMesh.tangents.Length==r.sharedMesh.vertexCount),"normals and tangents present");
            C(skins.All(r=>{var b=r.localBounds;b.Expand(.02f);return b.Contains(r.sharedMesh.bounds.min)&&b.Contains(r.sharedMesh.bounds.max);}),"rest geometry within skin bounds with 1cm float tolerance");
            C(skins.All(r=>r.sharedMaterial!=null&&!ShaderUtil.ShaderHasError(r.sharedMaterial.shader)),"material shaders compile");
            C(skins.All(r=>AssetDatabase.GetAssetPath(r.sharedMaterial).Contains("Sinmok271/")),"dedicated materials leave environment bark untouched");
            var enabled=actor.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled).ToArray();
            C(enabled.Length==10,"ten visible skinned renderers including seven original terrain roots");
            C(actor.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.name=="Trunk"||r.name=="LivingCrown"||r.name.StartsWith("Twig")||r.name.StartsWith("BranchPart")).All(r=>!r.enabled),"old trunk crown and branches hidden, recoverable");
            C(group.GetComponentsInChildren<Collider>().Length==0,"no decorative collision or physics bodies");
            var capsule=actor.GetComponent<CapsuleCollider>();C(capsule!=null&&Mathf.Approximately(capsule.radius,1.85f)&&Mathf.Approximately(capsule.height,7),"existing combat body collision preserved");
            C(rig.Animator.avatar!=null&&rig.Animator.avatar.isValid&&rig.Attacks.Length==4&&rig.Idle!=null,"original avatar idle and four combat clips remain");
            C(s.Content.SaveSlot=="world-demo-compact-cave-v4"&&s.Content.Campaign.IsValid,"save slot and campaign graph valid");
            var preview=EditorSceneManager.NewPreviewScene();var clone=Object.Instantiate(actor.gameObject);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone,preview);
            try
            {
                var cr=clone.GetComponent<SinmokRigAnimation>();cr.Animator.Rebind();cr.Animator.enabled=false;
                var skin=clone.transform.Find("Creature271/Anatomy").GetComponent<SkinnedMeshRenderer>();var baked=new Mesh();
                try
                {
                    for(int i=0;i<4;i++)
                    {
                        cr.Animator.Rebind();cr.Animator.enabled=false;
                        foreach(var bone in skin.bones)bone.localRotation=Quaternion.identity;
                        skin.BakeMesh(baked);var before=baked.vertices;
                        cr.Attacks[i].SampleAnimation(clone,.9f);skin.BakeMesh(baked);var after=baked.vertices;
                        float max=before.Zip(after,(a,b)=>Vector3.Distance(a,b)).Max();
                        // Root rise moves the retained ground roots; the new torso may only breathe.
                        if(i==2)
                        {
                            var root=clone.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r=>r.name=="RootPart0");
                            foreach(var tr in clone.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("RootSpread")))tr.localRotation=Quaternion.identity;
                            root.BakeMesh(baked);before=baked.vertices;
                            cr.Attacks[i].SampleAnimation(clone,.9f);root.BakeMesh(baked);after=baked.vertices;
                            max=before.Zip(after,(a,b)=>Vector3.Distance(a,b)).Max();
                        }
                        C(max>.2f,"attack "+i+" deforms visible weighted geometry: "+max.ToString("F2")+"m");
                        C(after.All(p=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z)),"finite deformed vertices "+i);
                    }
                }finally{Object.DestroyImmediate(baked);}
            }finally{Object.DestroyImmediate(clone);EditorSceneManager.ClosePreviewScene(preview);}
            report.Add("METRIC visible vertices "+enabled.Sum(r=>r.sharedMesh.vertexCount)+" triangles "+enabled.Sum(r=>r.sharedMesh.triangles.Length/3)+" renderers "+enabled.Length+"; not GPU measurement");
            File.WriteAllLines(Output271+"/checks.txt",report);return string.Join("\n",report);
        }
        static string Capture271()
        {
            var scene=FrontageScene249();var s=VillageSession();var actor=s.Actors.Single(a=>a.Id==WorldMacroPlaytestSession.SinmokId);
            var art=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var observer=art.Observer;
            var go=new GameObject("Sinmok271 offscreen camera"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();cam.CopyFrom(s.Walker.ViewCamera);cam.enabled=false;
            EditorUtility.CopySerialized(s.Walker.ViewCamera.GetUniversalAdditionalCameraData(),cam.GetUniversalAdditionalCameraData());
            var sky=go.AddComponent<Skybox>();sky.material=AssetDatabase.LoadAssetAtPath<Material>(Folder263+"/Sky.asset");
            var rt=new RenderTexture(1920,1080,24);var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            var transforms=actor.GetComponentsInChildren<Transform>();var rotations=transforms.Select(t=>t.localRotation).ToArray();
            var renderers=actor.GetComponentsInChildren<SkinnedMeshRenderer>();var enabled=renderers.Select(r=>r.enabled).ToArray();
            var meshes=renderers.Select(r=>r.sharedMesh).ToArray();var materials=renderers.Select(r=>r.sharedMaterial).ToArray();
            var rig=actor.GetComponent<SinmokRigAnimation>();var group=actor.transform.Find("Creature271");var foot=actor.transform.position;
            string[] names={"before","front","detail","side","slam","sweep","back"};
            try
            {
                art.Observer=cam;cam.targetTexture=rt;cam.aspect=16f/9;cam.fieldOfView=49;cam.clearFlags=CameraClearFlags.Skybox;
                for(int i=0;i<names.Length;i++)
                {
                    for(int k=0;k<transforms.Length;k++)transforms[k].localRotation=rotations[k];
                    for(int k=0;k<renderers.Length;k++)renderers[k].enabled=i==0?!renderers[k].transform.IsChildOf(group):enabled[k];
                    for(int k=0;k<renderers.Length;k++)
                    {
                        bool originalRoot=i==0&&renderers[k].name.StartsWith("RootPart");
                        renderers[k].sharedMesh=originalRoot?AssetDatabase.LoadAssetAtPath<Mesh>(Folder263+"/Tree_"+renderers[k].name+".asset"):meshes[k];
                        renderers[k].sharedMaterial=originalRoot?AssetDatabase.LoadAssetAtPath<Material>(Folder263+"/TreeBark.asset"):materials[k];
                    }
                    Vector3 eye=foot+new Vector3(15,7,25),target=foot+Vector3.up*6.4f;
                    if(i==2){eye=foot+new Vector3(5,10.5f,14);target=foot+new Vector3(0,8.1f,1.1f);cam.fieldOfView=43;}else cam.fieldOfView=49;
                    if(i==3){eye=foot+new Vector3(25,8,8);target=foot+Vector3.up*6.5f;}
                    if(i==4)rig.Attacks[0].SampleAnimation(actor.gameObject,.5f);
                    if(i==5)rig.Attacks[1].SampleAnimation(actor.gameObject,.9f);
                    if(i==6)eye=foot+new Vector3(-16,8,-24);
                    cam.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));cam.Render();RenderTexture.active=rt;
                    image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(Output271+"/"+names[i]+".png",image.EncodeToPNG());
                }
            }
            finally
            {
                for(int k=0;k<transforms.Length;k++)transforms[k].localRotation=rotations[k];
                foreach(var tr in transforms)
                {
                    var serialized=new SerializedObject(tr);var hint=serialized.FindProperty("m_LocalEulerAnglesHint");
                    if(hint!=null){hint.vector3Value=tr.localEulerAngles;serialized.ApplyModifiedPropertiesWithoutUndo();}
                }
                for(int k=0;k<renderers.Length;k++)renderers[k].enabled=enabled[k];
                for(int k=0;k<renderers.Length;k++){renderers[k].sharedMesh=meshes[k];renderers[k].sharedMaterial=materials[k];}
                art.Observer=observer;cam.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(go);
            }
            // Sampling and restoring transforms may dirty the candidate; persist the restored default pose only.
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            return "Seven actual candidate offscreen 1080p captures; fixed sampled attack poses, no Play or user input.";
        }
    }
}
