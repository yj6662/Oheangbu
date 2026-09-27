using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using Oheangbu.Combat;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactKcisaReplacement
    {
        const string HumanSource="Assets/_Project/Art/Characters/PlaytestReRig/PF_Player_C02_ReRig.prefab";
        static string ActorSource(Transform old)
        {
            string p=PathOf(old).ToLowerInvariant();
            if(!old.name.Contains("Enemy")||new[]{"checkpoint","gate_boss","hyeonun","muhak","sovereign"}.Any(p.Contains))return HumanSource;
            // Do not substitute a tiger for a dragon or a fire bird. Unavailable unique models
            // are reported explicitly and remain queued for model production.
            if(new[]{"azure_dragon","fallen_dragon","suzaku","mechanical_buddha"}.Any(p.Contains))return null;
            string kind=p.Contains("hyeongang")?"WaterTurtle":p.Contains("fire")||p.Contains("jeokro")?"FireHaetae":p.Contains("sacred_tree")?"WoodDeer":"MetalTiger";
            return "Assets/_Project/Art/Demo/Summons/"+kind+"/PF_"+kind+"_Combat.prefab";
        }
        static AnimationClip[] ActorClips(string source)
        {
            if(source==HumanSource)return new[]{AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Art/Characters/PlaytestNaturalLocomotion/Animations/IdleStill.anim"),AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Art/Characters/PlaytestNaturalLocomotion/Animations/WalkForward.anim")};
            string fbx=source.Replace("PF_","SM_").Replace(".prefab",".fbx");
            return AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
        }
        static RuntimeAnimatorController ActorController(string source,AnimationClip[] clips)
        {
            string key=Path.GetFileNameWithoutExtension(source),path=Folder+"/Actors/"+key+".controller";
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);if(controller!=null)return controller;
            var idle=clips.FirstOrDefault(c=>c.name.IndexOf("Idle",StringComparison.OrdinalIgnoreCase)>=0);
            if(idle==null)throw new InvalidOperationException("Owned idle clip missing: "+source);
            controller=AnimatorController.CreateAnimatorControllerAtPath(path);var state=controller.layers[0].stateMachine.AddState("Idle");state.motion=idle;controller.layers[0].stateMachine.defaultState=state;
            return controller;
        }
        static string ActorMotion()
        {
            int count=0;
            foreach(var holder in All.Where(t=>t.name=="OwnedActorAppearance").ToArray())
            {
                var anim=holder.GetComponentInChildren<Animator>();if(anim==null)continue;
                var controller=anim.runtimeAnimatorController as AnimatorController;if(controller==null)continue;
                bool human=controller.name.Contains("Player");string source=human?HumanSource:"Assets/_Project/Art/Demo/Summons/"+controller.name.Replace("PF_","").Replace("_Combat","")+"/"+controller.name+".prefab";
                var clips=ActorClips(source);var sm=controller.layers[0].stateMachine;
                var walk=clips.FirstOrDefault(c=>c!=null&&c.name.IndexOf("Walk",StringComparison.OrdinalIgnoreCase)>=0);
                var attack=clips.FirstOrDefault(c=>c!=null&&(c.name.IndexOf("Attack",StringComparison.OrdinalIgnoreCase)>=0||c.name.IndexOf("Horn",StringComparison.OrdinalIgnoreCase)>=0||c.name.IndexOf("Bite",StringComparison.OrdinalIgnoreCase)>=0));
                foreach(var pair in new[]{("Walk",walk),("Attack",attack)})if(pair.Item2!=null&&!sm.states.Any(s=>s.state.name==pair.Item1)){var st=sm.AddState(pair.Item1);st.motion=pair.Item2;}
                var driver=holder.GetComponent<Oheangbu.App.World.OwnedActorMotion>();if(driver==null)driver=holder.gameObject.AddComponent<Oheangbu.App.World.OwnedActorMotion>();
                driver.Animator=anim;driver.Enemy=holder.GetComponentInParent<EnemyController>();driver.MotionRoot=driver.Enemy!=null?driver.Enemy.transform:holder.parent;
                driver.HasWalk=walk!=null;driver.HasAttack=attack!=null;EditorUtility.SetDirty(driver);EditorUtility.SetDirty(controller);count++;
            }
            AssetDatabase.SaveAssets();UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return count+" source animation adapters connected (no root motion or damage ownership)";
        }
        static string Actors()
        {
            BeginChanges();Directory.CreateDirectory(Folder+"/Actors");int count=0;var missing=new System.Collections.Generic.List<string>();
            var targets=Primitives().Where(t=>t.name.Contains("Enemy")||t.name.Contains("NPC")||t.name=="Clerk_TemporaryAppearance"||t.name=="Replaceable_Innkeeper_Visual").ToArray();
            foreach(var old in targets)
            {
                Guard();string source=ActorSource(old);if(source==null){missing.Add(PathOf(old));continue;}
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(source);if(prefab==null)throw new FileNotFoundException(source);
                var oldBounds=old.GetComponent<Renderer>().bounds;var holder=new GameObject("OwnedActorAppearance").transform;
                holder.position=new Vector3(oldBounds.center.x,oldBounds.min.y,oldBounds.center.z);holder.rotation=old.rotation;holder.SetParent(old,true);
                var model=Object.Instantiate(prefab,holder,false);model.name="Appearance";
                foreach(var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(behaviour);
                foreach(var c in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
                foreach(var c in model.GetComponentsInChildren<Camera>(true))Object.DestroyImmediate(c.gameObject);
                foreach(var body in model.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(body);
                var clips=ActorClips(source);var idle=clips.FirstOrDefault(c=>c.name.IndexOf("Idle",StringComparison.OrdinalIgnoreCase)>=0);
                var anim=model.GetComponent<Animator>();if(anim==null)anim=model.AddComponent<Animator>();anim.applyRootMotion=false;anim.runtimeAnimatorController=ActorController(source,clips);anim.cullingMode=AnimatorCullingMode.CullUpdateTransforms;
                idle.SampleAnimation(model,0);
                var rs=model.GetComponentsInChildren<Renderer>(true);Bounds b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
                float desired=source==HumanSource?1.70f:Mathf.Min(1.55f,oldBounds.size.y*.8f);
                if(oldBounds.size.y>3&&source!=HumanSource)desired=2.5f;
                float scale=desired/Mathf.Max(.1f,b.size.y);model.transform.localScale*=scale;
                b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
                model.transform.position+=new Vector3(holder.position.x-b.center.x,holder.position.y-b.min.y,holder.position.z-b.center.z);
                // Each role gets its own subdued fabric dye while face/skin and source maps remain intact.
                if(source==HumanSource)
                {
                    string role=PathOf(old);Color dye=role.Contains("Clerk")||role.Contains("checkpoint")?new Color(.39f,.47f,.52f):role.Contains("wangso")||role.Contains("merchant")?new Color(.5f,.42f,.32f):new Color(.53f,.51f,.43f);
                    foreach(var r in rs)r.sharedMaterials=r.sharedMaterials.Select(m=>RoleMaterial(m,dye)).ToArray();
                }
                var enemy=old.GetComponentInParent<EnemyController>();
                if(enemy!=null){var serialized=new SerializedObject(enemy);serialized.FindProperty("_renderer").objectReferenceValue=rs.OrderByDescending(r=>r.bounds.size.sqrMagnitude).First();serialized.ApplyModifiedPropertiesWithoutUndo();}
                Retire(old);foreach(var hat in old.parent.Cast<Transform>().Where(t=>t.name=="ClothHat"&&Primitive(t)))Retire(hat);
                Record(old,holder,source,"Appearance-only derivative; original interaction/AI/hit volumes/IDs retained; owned idle animation; final individual costume design remains reviewable");count++;
            }
            File.WriteAllLines(Output+"/unique_actor_models_pending.txt",missing);
            return FinishChanges("Owned rigged actor appearances (unique creatures separately listed)",count);
        }
        static Material RoleMaterial(Material source,Color dye)
        {
            if(source==null)return null;string name=source.name.ToLowerInvariant();
            if(!name.Contains("cloth")&&!name.Contains("robe")&&!name.Contains("costume")&&!name.Contains("body"))return source;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long id);
            string path=Folder+"/Actors/"+guid+"_"+id+"_"+ColorUtility.ToHtmlStringRGB(dye)+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m!=null)return m;
            m=new Material(source){name=source.name+"_RoleDye"};if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",dye);AssetDatabase.CreateAsset(m,path);return m;
        }
        static Bounds ActualActorBounds(Transform model)
        {
            Bounds result=new Bounds();bool first=true;
            foreach(var r in model.GetComponentsInChildren<Renderer>())
            {
                Mesh mesh=null;bool temporary=false;
                if(r is SkinnedMeshRenderer skin){mesh=new Mesh();skin.BakeMesh(mesh);temporary=true;}
                else {var f=r.GetComponent<MeshFilter>();if(f!=null)mesh=f.sharedMesh;}
                if(mesh==null)continue;
                foreach(var p in Corners(mesh.bounds)){var v=r.transform.TransformPoint(p);if(first){result=new Bounds(v,Vector3.zero);first=false;}else result.Encapsulate(v);}
                if(temporary)Object.DestroyImmediate(mesh);
            }
            return result;
        }
        static string RefineActors()
        {
            int count=0;
            foreach(var holder in All.Where(t=>t.name=="OwnedActorAppearance").ToArray())
            {
                var model=holder.Find("Appearance");var animator=model.GetComponent<Animator>();bool human=animator.runtimeAnimatorController.name.Contains("Player");
                if(human)foreach(var t in model.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="C02_NearArm"||t.name=="C02_NearBrush"||t.name=="C02_WorldBrush").ToArray())if(t!=null)Object.DestroyImmediate(t.gameObject);
                foreach(var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    r.sharedMaterials=r.sharedMaterials.Select(m=>
                    {
                        if(m==null||!m.HasProperty("_Age"))return m;
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m,out string guid,out long localId);string path=Folder+"/Actors/Permanent_"+guid+"_"+localId+".mat";
                        var copy=AssetDatabase.LoadAssetAtPath<Material>(path);if(copy!=null)return copy;
                        copy=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=m.name+"_Permanent",enableInstancing=true};
                        if(m.HasProperty("_BaseMap"))copy.SetTexture("_BaseMap",m.GetTexture("_BaseMap"));
                        copy.SetColor("_BaseColor",m.HasProperty("_BaseColor")?m.GetColor("_BaseColor"):Color.white);
                        if(m.HasProperty("_NormalMap")){copy.SetTexture("_BumpMap",m.GetTexture("_NormalMap"));copy.EnableKeyword("_NORMALMAP");}
                        copy.SetFloat("_Smoothness",.24f);copy.SetFloat("_Metallic",.08f);AssetDatabase.CreateAsset(copy,path);return copy;
                    }).ToArray();EditorUtility.SetDirty(r);
                }
                var b=ActualActorBounds(model);float desired=human?1.7f:1.4f;
                model.localScale*=desired/Mathf.Max(.1f,b.size.y);b=ActualActorBounds(model);
                model.position+=new Vector3(holder.position.x-b.center.x,holder.position.y-b.min.y,holder.position.z-b.center.z);count++;
            }
            AssetDatabase.SaveAssets();UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return count+" actors: permanent non-dissolving materials; player brush / first-person arms removed; deformed mesh bounds grounded";
        }
    }
}
