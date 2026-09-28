using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class DemoSummonAuthoring
    {
        public const string Folder = "Assets/_Project/Art/Demo/Summons/WoodDeer";
        public const string ModelPath = Folder + "/SM_WoodDeer_Combat.fbx";
        public const string ProfilePath = Folder + "/Combat_Gom.asset";
        public static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Demo/Summons"));
        [Serializable] class Report
        {
            public string status, scope = "Wood deer combat derivative only; legacy static prefab and other four summons preserved.";
            public List<string> passed = new List<string>(), failed = new List<string>();
            public int tris, skinnedRenderers, colliders, bones, materialSlots;
            public int maxInfluences,unweightedVertices;
            public float maxWeightSumError;
            public List<string> partStats=new List<string>();
            public string[] clips, unverified = { "Manual combat input", "Final model/animation approval", "Moving CPU/GPU performance", "Other four summon combat implementations" };
        }
        public static string Execute(string command)
        {
            Directory.CreateDirectory(Output);
            if (command == "support-import") return DemoWoodDeerSupportDiagnostics.InspectImportedPoses();
            if (command == "support-active") return DemoWoodDeerSupportDiagnostics.CaptureActive();
            if (command == "apply") return Apply();
            if (command == "audit") return Audit();
            if (command == "tests") return Save("clock_tests.json", DemoSummonFoundationChecks.Run());
            if (command == "damage-tests") return Save("damage_tests.json", DemoSummonDamageChecks.Run());
            if (command == "root-tests") return Save("root_tests.json", DemoSummonRootChecks.Run());
            if (command == "flame-tests") return Save("flame_tests.json", DemoSummonFlameChecks.Run());
            if (command == "flame-geometry") return DemoFlameGeometryChecks.Run();
            if (command == "flame-pixels") return DemoFlamePixelChecks.Run();
            if (command == "tiger-tests") return Save("tiger_tests.json", DemoSummonTigerChecks.Run());
            if (command == "club-tests") return Save("club_tests.json", DemoSummonClubChecks.Run());
            if (command == "water-tests") return Save("water_tests.json", DemoSummonWaterChecks.Run());
            if (command == "water-visual-tests") return Save("water_visual_tests.json", DemoSummonWaterVisualChecks.Run());
            if (command.StartsWith("turtle:")) return DemoTurtleAuthoring.Execute(command.Substring(7));
            if (command.StartsWith("haetae:")) return DemoHaetaeAuthoring.Execute(command.Substring(7));
            if (command.StartsWith("tiger:")) return DemoTigerAuthoring.Execute(command.Substring(6));
            if (command.StartsWith("club:")) return DemoClubAuthoring.Execute(command.Substring(5));
            if (command == "manager-tests") return Save("manager_tests.json", DemoSummonManagerChecks.Run());
            if (command.StartsWith("play:")) return DemoSummonPlayChecks.Execute(command.Substring(5));
            throw new ArgumentException("summons: apply/audit/tests/damage-tests");
        }
        static string Save(string name, string json) { File.WriteAllText(Path.Combine(Output,name),json); return json; }
        static void FolderAt(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\','/'); FolderAt(parent); AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode required");
            var session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (session == null || session.gameObject.scene.path != DemoFoundationAuthoring.Scene || session.Content.Campaign == null)
                throw new InvalidOperationException("Only the dedicated demo scene may be authored");
            if (!File.Exists(ModelPath)) throw new FileNotFoundException("Rigged derivative must exist before scene wiring", ModelPath);
            FolderAt(Folder);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) { AssetDatabase.ImportAsset(ModelPath); importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath); }
            importer.importAnimation = true; importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false; importer.isReadable = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            var imported = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
            AnimationClip Clip(string keyword) => clips.SingleOrDefault(c => c.name.IndexOf(keyword,StringComparison.OrdinalIgnoreCase)>=0);
            var idle = Clip("Idle"); var walk = Clip("Walk"); var attack = Clip("Horn"); var rootCast = Clip("RootCast");
            if (idle == null || walk == null || attack == null) throw new InvalidOperationException("Expected unique Idle, Walk and Horn clips: "+string.Join(",",clips.Select(c=>c.name)));
            Shader shader = Shader.Find("Oheangbu/DemoWoodDeer");
            if (shader == null) throw new InvalidOperationException("Combat-only deer shader missing");
            var materials = new Dictionary<string,Material>();
            foreach (string name in new[]{"Bark","Root","Leaf"})
            {
                string path = Folder + "/M_" + name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/SpellVFX120/WoodDeer/M_"+name+".mat"));
                    AssetDatabase.CreateAsset(material,path);
                }
                material.shader = shader; material.SetFloat("_Age",2); material.SetFloat("_MotionTime",0);
                EditorUtility.SetDirty(material); materials[name] = material;
            }
            var temporary = Object.Instantiate(imported); temporary.name = "PF_WoodDeer_Combat";
            try
            {
                foreach (var renderer in temporary.GetComponentsInChildren<Renderer>(true))
                {
                    string name = renderer.name.IndexOf("Leaves",StringComparison.OrdinalIgnoreCase)>=0 ? "Leaf"
                        : renderer.name.IndexOf("Root",StringComparison.OrdinalIgnoreCase)>=0 ? "Root" : "Bark";
                    renderer.sharedMaterials = Enumerable.Repeat(materials[name], renderer.sharedMaterials.Length).ToArray();
                }
                var animator = temporary.GetComponent<Animator>();
                if (animator == null) animator = temporary.AddComponent<Animator>();
                animator.applyRootMotion = false;
                var prefab = PrefabUtility.SaveAsPrefabAsset(temporary, Folder + "/PF_WoodDeer_Combat.prefab");
                var profile = AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(ProfilePath);
                if (profile == null) { profile = ScriptableObject.CreateInstance<SummonCombatProfile>(); AssetDatabase.CreateAsset(profile,ProfilePath); }
                profile.PresentationPrefab = prefab; profile.IdleClip = idle; profile.WalkClip = walk; profile.AttackClip = attack;
                profile.RootAttackEnabled = true; profile.RootAttackClip = rootCast;
                profile.RootMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_Project/Art/SpellVFX120/Botanical/Meshes/Mesh_Root.asset");
                var rootSource = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/SpellVFX120/Botanical/Materials/M_Root_Bark.mat");
                profile.RootMaterial = AssetDatabase.LoadAssetAtPath<Material>(Folder+"/M_RootAttack.mat");
                if(profile.RootMaterial==null){profile.RootMaterial=new Material(rootSource);AssetDatabase.CreateAsset(profile.RootMaterial,Folder+"/M_RootAttack.mat");}
                profile.RootMaterial.SetColor("_BaseColor",new Color(1.25f,1.13f,.90f));
                profile.RootMaterial.SetFloat("_TintStrength",.28f);profile.RootMaterial.SetColor("_Tint",new Color(.60f,.65f,.39f));
                EditorUtility.SetDirty(profile.RootMaterial);
                if (profile.RootAttackClip == null || profile.RootMesh == null || profile.RootMaterial == null)
                    throw new InvalidOperationException("Existing root clip/mesh/material required before enabling root combat");
                profile.WalkClipMetresPerSecond = 1.2f; profile.FollowSpeed = 1.8f; profile.WindupSeconds = .7f;
                var modelRenderers=temporary.GetComponentsInChildren<Renderer>(true);
                Bounds modelBounds=modelRenderers[0].bounds;foreach(var r in modelRenderers)modelBounds.Encapsulate(r.bounds);
                profile.BodyHeight=Mathf.Max(1.7f,modelBounds.max.y-temporary.transform.position.y+.05f);
                profile.FootprintWidth=modelBounds.size.x+.06f;profile.FootprintLength=modelBounds.size.z+.06f;
                var centre=temporary.transform.InverseTransformPoint(modelBounds.center);
                profile.FootprintOffset=new Vector3(centre.x,0,centre.z);
                profile.FormationSeal = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/SpellVFX120/WoodDeer/KTP_Gom_Seal.prefab");
                profile.DissolveDebris = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/SpellVFX120/WoodDeer/PF_Gom_Dissolve.prefab");
                EditorUtility.SetDirty(profile);
                var manager = session.Walker.Wiring.GetComponent<DemoSummonCombatManager>();
                if (manager == null) manager = session.Walker.Wiring.gameObject.AddComponent<DemoSummonCombatManager>();
                manager.Wiring = session.Walker.Wiring;
                manager.Profiles = (manager.Profiles ?? Array.Empty<SummonCombatProfile>()).Where(p=>p!=null && p.Letter!=profile.Letter).Append(profile).ToArray();
                manager.PlayerVitals = session.Walker.Motor.GetComponent<PlayerVitals>();
                manager.VehicleSeat = Object.FindFirstObjectByType<WorldMacroPalanquinSeat>();
                EditorUtility.SetDirty(manager);
                AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(session.gameObject.scene); EditorSceneManager.SaveScene(session.gameObject.scene);
            }
            finally { Object.DestroyImmediate(temporary); }
            return Audit();
        }
        static string Audit()
        {
            var report = new Report();
            void Check(bool ok, string text) { (ok?report.passed:report.failed).Add(text); }
            var profile = AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(ProfilePath);
            Check(profile != null && profile.TryValidate(out _,true),"Valid isolated wood-deer combat profile");
            if (profile != null && profile.PresentationPrefab != null)
            {
                var prefab=profile.PresentationPrefab;
                var skins=prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                report.skinnedRenderers=skins.Length; report.colliders=prefab.GetComponentsInChildren<Collider>(true).Length;
                report.bones=skins.SelectMany(s=>s.bones).Where(b=>b!=null).Distinct().Count();
                foreach(var skin in skins)
                {
                    int tris=0;for(int i=0;i<skin.sharedMesh.subMeshCount;i++)tris+=(int)skin.sharedMesh.GetIndexCount(i)/3;
                    report.tris+=tris;report.materialSlots+=skin.sharedMaterials.Length;
                    report.partStats.Add(skin.name+": "+tris+" imported triangles, "+skin.sharedMesh.vertexCount+" split vertices");
                    var perVertex=skin.sharedMesh.GetBonesPerVertex();var weights=skin.sharedMesh.GetAllBoneWeights();
                    try
                    {
                        int index=0;foreach(byte count in perVertex)
                        {
                            report.maxInfluences=Mathf.Max(report.maxInfluences,count);if(count==0)report.unweightedVertices++;
                            float sum=0;for(int i=0;i<count;i++)sum+=weights[index++].weight;
                            report.maxWeightSumError=Mathf.Max(report.maxWeightSumError,Mathf.Abs(1-sum));
                        }
                    }
                    finally {perVertex.Dispose();weights.Dispose();}
                }
                Check(report.skinnedRenderers==3&&report.bones>=16,"All three approved surfaces share a real deform rig");
                Check(report.tris<=18000&&report.materialSlots<=3,"Approved triangle and material budgets preserved");
                Check(report.maxInfluences<=4&&report.unweightedVertices==0&&report.maxWeightSumError<=.0001f,"Unity-imported skin weights <=4, normalized and no unweighted vertices");
                Check(report.colliders==0,"Summon presentation cannot physically block attacks or navigation");
                report.clips=new[]{profile.IdleClip,profile.WalkClip,profile.AttackClip}.Select(c=>c!=null?c.name:"MISSING").ToArray();
                Check(profile.IdleClip!=null&&profile.WalkClip!=null&&profile.AttackClip!=null,"Real imported idle/walk/horn animation clips");
                Check(profile.ActivitySeconds==20,"Activity lifetime is separate from formation and dissolve");
                var manager=Object.FindFirstObjectByType<DemoSummonCombatManager>();
                Check(manager!=null&&manager.Wiring!=null&&manager.PlayerVitals!=null&&manager.Profiles.Contains(profile),"Dedicated demo manager wired to player combat and death");
            }
            report.status=report.failed.Count==0?"PASS":"FAIL"; return Save("scene_audit.json",JsonUtility.ToJson(report,true));
        }
    }
}
