using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.Core.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class DemoClubAuthoring
    {
        const string Folder = "Assets/_Project/Art/Demo/Summons/DokkaebiClub";
        const string Model = Folder + "/SM_DokkaebiClub_Combat.fbx";
        const string Original = "Assets/_Project/Art/SpellVFX120/DokkaebiClub";
        public const string ProfilePath = Folder + "/Combat_Mom.asset";
        [Serializable] public sealed class Measurements
        {
            public string source = "Measured authored rig required";
            public float clubRange, originHeight, halfAngle, verticalTolerance, walkSpeed;
            public float footprintWidth, footprintLength, bodyHeight;
            public Vector3 footprintOffset;
        }
        [Serializable] sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public int triangles, skins, bones, materialSlots, maximumInfluences, unweighted;
            public float weightError;
            public Measurements measurements;
            public string[] unverified = { "Actual combat input", "Moving-target combat", "Terrain foot contact", "Final club animation", "CPU/GPU performance" };
        }
        public static string Execute(string command)
        {
            if (command == "apply") Apply();
            else if (command != "audit") throw new ArgumentException("club: apply/audit");
            return Audit();
        }
        static Measurements ReadMeasurements()
        {
            var path = Path.Combine(DemoSummonAuthoring.Output, "DokkaebiClub/combat_measurements.json");
            if (!File.Exists(path)) throw new FileNotFoundException("Rig measurements must precede scene authoring", path);
            var m = JsonUtility.FromJson<Measurements>(File.ReadAllText(path));
            if (m == null || string.IsNullOrWhiteSpace(m.source)) throw new InvalidOperationException("Missing measurement provenance");
            foreach (float v in new[] { m.clubRange, m.originHeight, m.halfAngle, m.verticalTolerance, m.walkSpeed, m.footprintWidth, m.footprintLength, m.bodyHeight })
                if (!float.IsFinite(v) || v <= 0) throw new InvalidOperationException("Missing finite positive rig measurement");
            return m;
        }
        static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (session == null || session.gameObject.scene.path != DemoFoundationAuthoring.Scene || session.Content.Campaign == null)
                throw new InvalidOperationException("Dedicated demo scene required");
            var m = ReadMeasurements();
            if (!File.Exists(Model)) throw new FileNotFoundException("Rig derivative required", Model);
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceUpdate);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            importer.importAnimation = true; importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; importer.optimizeGameObjects = false;
            importer.isReadable = true; importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.SaveAndReimport();
            var clips = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            AnimationClip Clip(string name) => clips.Single(c => c.name.EndsWith(name, StringComparison.Ordinal));
            var idle = Clip("DC_Idle"); var walk = Clip("DC_Walk"); var swing = Clip("DC_Swing");
            if (Mathf.Abs(swing.length - 1.7f) > .01f) throw new InvalidOperationException("Swing must match committed 1.7-second schedule");
            var material = AssetDatabase.LoadAssetAtPath<Material>(Original + "/M_AgedBronze.mat");
            if (material == null) throw new InvalidOperationException("Approved bronze material missing");
            var temporary = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
            try
            {
                temporary.name = "PF_DokkaebiClub_Combat";
                foreach (var renderer in temporary.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                var animator = temporary.GetComponent<Animator>() ?? temporary.AddComponent<Animator>(); animator.applyRootMotion = false;
                var profile = AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(ProfilePath);
                if (profile == null) { profile = ScriptableObject.CreateInstance<SummonCombatProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
                profile.Letter = "몸"; profile.Element = Element.Earth;
                profile.RootAttackEnabled = profile.FlameAttackEnabled = profile.TigerAttackEnabled = false; profile.ClubAttackEnabled = true;
                profile.IdleClip = idle; profile.WalkClip = walk; profile.AttackClip = swing;
                profile.WalkClipMetresPerSecond = m.walkSpeed; profile.FollowSpeed = 1.8f;
                profile.AttackRange = profile.ClubRange = m.clubRange; profile.ClubHalfAngleDegrees = m.halfAngle;
                profile.ClubOriginHeight = m.originHeight; profile.ClubHeightTolerance = m.verticalTolerance;
                profile.ClubWindupSeconds = .8f; profile.ClubSweepSeconds = .3f; profile.ClubRecoverySeconds = .6f;
                profile.ClubDamageMultiplier = profile.DamageMultiplier = 1.2f;
                profile.WindupSeconds = .95f; profile.CooldownSeconds = 1.2f;
                profile.FootprintWidth = m.footprintWidth; profile.FootprintLength = m.footprintLength;
                profile.BodyHeight = m.bodyHeight; profile.FootprintOffset = m.footprintOffset;
                profile.FormationSeal = AssetDatabase.LoadAssetAtPath<GameObject>(Original + "/KTP_Mom_Seal.prefab");
                profile.DissolveDebris = AssetDatabase.LoadAssetAtPath<GameObject>(Original + "/PF_Mom_Dissolve.prefab");
                profile.PresentationPrefab = PrefabUtility.SaveAsPrefabAsset(temporary, Folder + "/PF_DokkaebiClub_Combat.prefab");
                if (!profile.TryValidate(out string error, true)) throw new InvalidOperationException(error);
                var manager = session.Walker.Wiring.GetComponent<DemoSummonCombatManager>();
                if (manager == null) throw new InvalidOperationException("Combat summon manager missing");
                manager.Profiles = (manager.Profiles ?? Array.Empty<SummonCombatProfile>()).Where(p => p != null && p.Letter != "몸").Append(profile).ToArray();
                EditorUtility.SetDirty(profile); EditorUtility.SetDirty(manager); AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(session.gameObject.scene); EditorSceneManager.SaveScene(session.gameObject.scene);
            }
            finally { Object.DestroyImmediate(temporary); }
        }
        static string Audit()
        {
            var r = new Report(); void Check(bool pass, string text) => (pass ? r.passed : r.failed).Add(text);
            var p = AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(ProfilePath);
            Check(p != null && p.TryValidate(out _, true), "Valid dedicated earth club profile");
            if (p != null && p.PresentationPrefab != null)
            {
                r.measurements = ReadMeasurements();
                var skins = p.PresentationPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true); r.skins = skins.Length;
                r.bones = skins.SelectMany(s => s.bones).Distinct().Count();
                foreach (var skin in skins)
                {
                    for (int i = 0; i < skin.sharedMesh.subMeshCount; i++) r.triangles += (int)skin.sharedMesh.GetIndexCount(i) / 3;
                    r.materialSlots += skin.sharedMaterials.Length;
                    var counts = skin.sharedMesh.GetBonesPerVertex(); var weights = skin.sharedMesh.GetAllBoneWeights();
                    try { int cursor = 0; foreach (byte n in counts) { r.maximumInfluences = Mathf.Max(r.maximumInfluences, n); if (n == 0) r.unweighted++;
                        float total = 0; for (int i = 0; i < n; i++) total += weights[cursor++].weight; r.weightError = Mathf.Max(r.weightError, Mathf.Abs(1 - total)); } }
                    finally { counts.Dispose(); weights.Dispose(); }
                }
                Check(r.skins > 0 && r.bones >= 15, "Imported original mesh now has a deform skeleton");
                Check(r.maximumInfluences <= 4 && r.unweighted == 0 && r.weightError <= .0001f, "Normalized maximum four weights");
                Check(p.PresentationPrefab.GetComponentsInChildren<Collider>(true).Length == 0, "Summon does not physically block enemies");
                Check(p.IdleClip != null && p.WalkClip != null && p.AttackClip != null && Mathf.Abs(p.AttackClip.length - 1.7f) < .01f,
                    "Idle, walk and sweep import with committed attack duration");
                Check(p.FormationSeal != null && p.DissolveDebris != null, "Original earth formation and dissolve references retained");
                var manager = Object.FindFirstObjectByType<DemoSummonCombatManager>();
                Check(manager != null && manager.Profiles.Contains(p) && new[] { "곰", "놈", "솜" }.All(letter => manager.Profiles.Any(x => x != null && x.Letter == letter)),
                    "Club joins the preceding three combat summons");
            }
            r.status = r.failed.Count == 0 ? "PASS" : "FAIL"; string json = JsonUtility.ToJson(r, true);
            File.WriteAllText(Path.Combine(DemoSummonAuthoring.Output, "club_scene_audit.json"), json); return json;
        }
    }
}
