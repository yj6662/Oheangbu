using System.IO;
using System.Linq;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #302 trailer giant 금강역사: importer setup (Humanoid, Mixamo clips copy the model avatar), URP PBR material and the
    // prefab with Guardian302Actor. Model = Meshy multi-image to 3D (one request), skin = Mixamo via a proxy, weights moved
    // to the full mesh in Blender (Art/Characters/Trailer302/GeumgangGuardian/mixamo/transfer.py).
    //   build — Assets/_Project/Art/Characters/Trailer302/PF_GeumgangGuardian302.prefab
    public static class Guardian302Build
    {
        const string Root = "Assets/_Project/Art/Characters/Trailer302/";
        static readonly (string file, string name, bool loop, float[] impacts)[] Clips =
        {
            ("G302_MutantBreathingIdle", "Idle", true, new float[0]), ("G302_MutantWalking", "Walk", true, new float[0]),
            // blow times measured on the clips (Guardian302Build analyze): end of the downswing / fastest swing / landing /
            // the body meeting the ground
            ("G302_MutantRoaring", "Roar", false, new float[0]), ("G302_MutantSwiping", "Swipe", false, new[] { 1.32f }),
            ("G302_MutantJumpAttack", "JumpSmash", false, new[] { 1.62f }), ("G302_MutantPunch", "Punch", false, new[] { .33f }),
            ("G302_MutantFlexingMuscles", "Flex", false, new float[0]), ("G302_MutantDying", "Die", false, new[] { 2.0f }),
            ("G302_GreatSwordDownwardSlash", "Smash", false, new[] { .70f }), ("G302_BigHitToHead", "Hit", false, new float[0]),
        };

        public static string Run(string command)
        {
            if (command.StartsWith("analyze:")) return Analyze(command.Substring(8));
            if (command != "build") throw new System.ArgumentException("Guardian302Build: build | analyze:<json>");
            string model = Root + "GeumgangGuardian302_Rigged.fbx";
            var mi = (ModelImporter)AssetImporter.GetAtPath(model);
            mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importAnimation = false; mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Off; mi.importNormals = ModelImporterNormals.Import;
            mi.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(model).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman) throw new System.Exception("Guardian302Build: model avatar is not humanoid");

            var moves = new Guardian302Actor.Move[Clips.Length];
            for (int i = 0; i < Clips.Length; i++)
            {
                var (file, name, loop, impacts) = Clips[i];
                string path = Root + "Animations/" + file + ".fbx";
                var ai = (ModelImporter)AssetImporter.GetAtPath(path);
                ai.animationType = ModelImporterAnimationType.Human; ai.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;   // Mixamo rig per clip; humanoid retargets onto the model
                ai.SaveAndReimport();
                ai.materialImportMode = ModelImporterMaterialImportMode.None; ai.importAnimation = true;
                var defaults = ai.defaultClipAnimations;
                if (defaults.Length > 0)
                {
                    // facing and height stay in the pose (height from the feet at the clip start, so leaps and crouches are the
                    // clip's own); the ground travel (centre of mass XZ) is root motion that Guardian302Actor applies to the object
                    var c = defaults[0]; c.name = "G302_" + name; c.loopTime = loop;
                    c.lockRootRotation = true; c.keepOriginalOrientation = true;
                    c.lockRootHeightY = true; c.keepOriginalPositionY = false; c.heightFromFeet = true;
                    c.lockRootPositionXZ = false; c.keepOriginalPositionXZ = false;
                    ai.clipAnimations = new[] { c };
                }
                ai.SaveAndReimport();
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(x => !x.name.StartsWith("__preview__"));
                AnimationCurve RootCurve(string axis)
                {
                    var b = AnimationUtility.GetCurveBindings(clip).FirstOrDefault(x => x.propertyName == "RootT." + axis);
                    return b.propertyName == null ? null : AnimationUtility.GetEditorCurve(clip, b);
                }
                moves[i] = new Guardian302Actor.Move { Name = name, Clip = clip, Loop = loop, Impacts = impacts, RootX = RootCurve("x"), RootZ = RootCurve("z") };
            }

            foreach (var (tex, normal, linear) in new[] { ("G302_BaseColor", false, false), ("G302_Normal", true, true), ("G302_MetallicSmoothness", false, true) })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(Root + "Textures/" + tex + ".png");
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default; ti.sRGBTexture = !linear;
                ti.maxTextureSize = 4096; ti.mipmapEnabled = true; ti.SaveAndReimport();
            }
            string matPath = Root + "M_GeumgangGuardian302.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Textures/G302_BaseColor.png"));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Textures/G302_Normal.png"));
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Textures/G302_MetallicSmoothness.png"));
            mat.SetFloat("_Smoothness", 1f); mat.SetFloat("_Metallic", 1f); mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP"); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(mat);

            var src = AssetDatabase.LoadAssetAtPath<GameObject>(model);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
            var anim = go.GetComponentInChildren<Animator>() ?? go.AddComponent<Animator>();
            anim.avatar = avatar; anim.runtimeAnimatorController = null;
            var actor = go.AddComponent<Guardian302Actor>(); actor.Moves = moves;
            actor.ImpactMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/SpellVFX120/Riposte301/M_RiposteSplash302.mat");   // the 앞잡 ink splash
            string prefab = Root + "PF_GeumgangGuardian302.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, prefab); Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            return "prefab " + prefab + " moves " + string.Join(",", moves.Select(m => m.Name + ":" + m.Clip.length.ToString("F2")));
        }

        // analyze:<json> — sample every move on an isolated copy of the prefab (no open scene is touched), 30 per second:
        // body centre of mass and feet/hands/head in the actor's own frame, to see in-pose travel, planted-foot slide and
        // where the blows land. The clip's root curve names are listed too.
        static string Analyze(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(Root + "PF_GeumgangGuardian302.prefab");
            try
            {
                var actor = root.GetComponent<Guardian302Actor>(); var anim = root.GetComponentInChildren<Animator>();
                anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; anim.Rebind();
                var bones = new[] { HumanBodyBones.Hips, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
                    HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.Head };
                var inv = root.transform.worldToLocalMatrix;
                string V(Vector3 w) { var p = inv.MultiplyPoint3x4(w); return $"[{p.x:F3},{p.y:F3},{p.z:F3}]"; }
                var sb = new System.Text.StringBuilder();
                sb.Append("{\"humanScale\":" + anim.humanScale.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + ",\"bones\":[" + string.Join(",", bones.Select(b => "\"" + b + "\"")) + "],\"moves\":{");
                for (int i = 0; i < actor.Moves.Length; i++)
                {
                    var m = actor.Moves[i];
                    var curves = AnimationUtility.GetCurveBindings(m.Clip).Select(b => b.propertyName).Where(n => n.StartsWith("Root") || n.StartsWith("Motion")).ToArray();
                    var graph = UnityEngine.Playables.PlayableGraph.Create("G302Analyze"); graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
                    var node = UnityEngine.Animations.AnimationClipPlayable.Create(graph, m.Clip); node.SetApplyFootIK(true);
                    UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "o", anim).SetSourcePlayable(node);
                    graph.Play();
                    if (i > 0) sb.Append(',');
                    sb.Append("\"" + m.Name + "\":{\"length\":" + m.Clip.length.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ",\"curves\":[" + string.Join(",", curves.Select(c => "\"" + c + "\"")) + "],\"frames\":[");
                    int n = Mathf.CeilToInt(m.Clip.length * 30f);
                    for (int f = 0; f <= n; f++)
                    {
                        node.SetTime(Mathf.Min(f / 30f, m.Clip.length)); graph.Evaluate(0f);
                        if (f > 0) sb.Append(',');
                        float tt = Mathf.Min(f / 30f, m.Clip.length);
                        sb.Append("[" + V(anim.bodyPosition) + ",[" + (m.RootX != null ? m.RootX.Evaluate(tt) : 0f).ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "," + (m.RootZ != null ? m.RootZ.Evaluate(tt) : 0f).ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "]," + string.Join(",", bones.Select(b => { var t = anim.GetBoneTransform(b); return t != null ? V(t.position) : "null"; })) + "]");
                    }
                    sb.Append("]}");
                    graph.Destroy();
                }
                sb.Append("}}");
                System.IO.File.WriteAllText(path, sb.ToString());
                return "analyzed " + actor.Moves.Length + " moves -> " + path;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
