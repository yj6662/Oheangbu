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
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactFolklore298
    {
        [Serializable] sealed class PlacementReceipt { public string id, model, realm, sourceTemplate; public Vector3 feet; public bool respawnOnRest; }
        [Serializable] sealed class BuildReceipt { public string timestamp, scene, saveSlot; public int originalActors, resultingActors; public PlacementReceipt[] placements; public string[] preserved; }
        static readonly string[] NewSpecies = { "dokkaebi", "agwi", "changgui", "bulgasari", "fox_spirit", "imugi" };
        static WorldMacroPlaytestSession Session() => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
        static void SetRef(Object target, string name, Object value)
        {
            var serialized = new SerializedObject(target); var property = serialized.FindProperty(name);
            if (property == null) throw new Exception(target.GetType().Name + " missing serialized field " + name);
            property.objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(target);
        }
        static T GetRef<T>(Object target, string name) where T : Object => new SerializedObject(target).FindProperty(name)?.objectReferenceValue as T;
        static T DataAsset<T>(string name, Func<T> create) where T : Object
        {
            Folder(AssetRoot + "/Data"); string path = AssetRoot + "/Data/" + name + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path); if (asset != null) return asset;
            asset = create(); AssetDatabase.CreateAsset(asset, path); return asset;
        }
        static void OpenCandidate()
        {
            RequireEdit();
            if (SceneManager.GetActiveScene().isDirty) throw new Exception("Save or explicitly discard current scene before candidate authoring");
            Folder(AssetRoot);
            if (!File.Exists(ScenePath) && !AssetDatabase.CopyAsset(SourceScene, ScenePath)) throw new Exception("Cannot clone saved296 scene");
            if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            var session = Session();
            var source = session.Content;
            var content = DataAsset("WorldContent298", () => Object.Instantiate(source));
            // Keep unchanged geometry revision so valid saved feet retain their meaning.
            content.SaveSlot = "world-folklore-298"; session.Content = content; session.TestSaveSuffix = "";
            var previousLayout = session.MountainLayout;
            var layout = DataAsset("WorldLayout298", () => Object.Instantiate(previousLayout));
            if (previousLayout != layout)
            {
                // Only scene bindings change. Shared295/296 geometry and source SOs remain untouched.
                foreach (var component in SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true)).Where(c => c != null))
                {
                    var serialized = new SerializedObject(component); var property = serialized.GetIterator(); bool changed = false;
                    while (property.Next(true)) if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == previousLayout) { property.objectReferenceValue = layout; changed = true; }
                    if (changed) { serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(component); }
                }
            }
            session.MountainLayout = layout; EditorUtility.SetDirty(layout);
            EditorUtility.SetDirty(content); EditorUtility.SetDirty(session);
        }
        static Vector3 FindPlacement(ModelRow row, WorldMacroPlaytestSession session, IEnumerable<Vector3> reserved)
        {
            Vector3 centre = row.preferredFeet;
            if (!string.IsNullOrWhiteSpace(row.placeId))
            {
                var place = session.MountainLayout.Places.SingleOrDefault(p => p.Id == row.placeId) ?? throw new Exception(row.id + " unknown place " + row.placeId);
                var surface = new CompactWorldSurface(session.MountainLayout); centre = new Vector3(place.XZ.x, surface.Sample(place.XZ.x, place.XZ.y), place.XZ.y);
            }
            else if (centre == Vector3.zero) throw new Exception(row.id + " needs an explicit placeId or preferredFeet");
            var template = session.Actors.Single(a => a.Id == row.templateId); var nav = template.GetComponent<NavMeshAgent>();
            float radius = Mathf.Max(.3f, row.capsuleRadius); float height = Mathf.Max(radius * 2 + .1f, row.capsuleHeight);
            var original = session.Actors.Where(a => !a.Id.StartsWith("folklore298/", StringComparison.Ordinal)).Select(a => a.transform.position).ToArray();
            for (int ring = 0; ring <= Mathf.FloorToInt(Mathf.Min(100, row.searchRadius) / 3); ring++)
            for (int spoke = 0; spoke < (ring == 0 ? 1 : 24); spoke++)
            {
                float angle = spoke * Mathf.PI * 2 / 24; var probe = centre + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (ring * 3);
                if (PlacementFailure298(row, session, original, reserved, nav.areaMask, radius, height, probe, out var feet, out _) == null) return feet;
            }
            throw new Exception(row.id + " no supported dry connected spawn within bounded area; author a different preferredFeet, do not relax physical checks");
        }
        static GameObject AddVisual(PrologueEncounter actor, ModelRow row)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(row.id)) ?? throw new Exception("Import first " + row.id);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, actor.transform); visual.name = "Folklore298_Visual";
            visual.transform.localPosition = Vector3.down * actor.GetComponent<NavMeshAgent>().baseOffset;
            visual.transform.localRotation = Quaternion.identity; visual.transform.localScale = Vector3.one;
            if (row.id == "imugi")
            {
                var head = visual.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Head");
                var offset = actor.transform.InverseTransformPoint(head.position); visual.transform.localPosition -= new Vector3(offset.x, 0, offset.z);
            }
            return visual;
        }
        static void BindMotion(PrologueEncounter actor, ModelRow row, GameObject visual)
        {
            var oldMotion = actor.GetComponent<EnemyRigMotion273>(); if (oldMotion != null) Object.DestroyImmediate(oldMotion);
            var rig = actor.GetComponent<EnemyRigMotion298>(); if (rig == null) rig = actor.gameObject.AddComponent<EnemyRigMotion298>();
            rig.Animator = visual.GetComponentInChildren<Animator>(true); rig.Enemy = actor.GetComponent<EnemyController>(); rig.Vitals = actor.GetComponent<EnemyVitals>();
            rig.General = actor.GetComponent<SouthGateGeneralController>();
            rig.Idle = Clip(row, "idle"); rig.Walk = Clip(row, "walk"); rig.Attack = Clip(row, "attack");
            rig.Hit = Clip(row, "hit"); rig.Stun = Clip(row, "stun"); rig.Death = Clip(row, "death");
            rig.AttackPeak01 = row.clips.Single(c => c.role == "attack").peak01;
            if (rig.General != null)
            {
                rig.Sweep = Clip(row, "sweep"); rig.Wave = Clip(row, "wave");
                rig.SweepPeak01 = row.clips.Single(c => c.role == "sweep").peak01; rig.WavePeak01 = row.clips.Single(c => c.role == "wave").peak01;
            }
            rig.WalkMetresPerSecond = row.walkMetresPerSecond; rig.LoopStun = row.clips.Single(c => c.role == "stun").loop;
            if (row.id == "imugi")
            {
                var bones = visual.GetComponentsInChildren<Transform>(true); Transform Bone(string name) => bones.Single(t => t.name == name);
                var follow = actor.GetComponent<CheongryongBodyFollow>(); if (follow == null) follow = actor.gameObject.AddComponent<CheongryongBodyFollow>();
                if (!follow.Configure(Bone("Head"), Enumerable.Range(1, 24).Select(i => Bone("Body_" + i.ToString("00"))).ToArray(), Bone("TailTip"))) throw new Exception("Imugi body-follow chain invalid");
                rig.SerpentFollow = follow;
                // No competing Generic body walk: manifest must bind both roles to head-only CR_Idle.
                if (!rig.Idle.name.Contains("CR_Idle") || !rig.Walk.name.Contains("CR_Idle") || !rig.Attack.name.Contains("CR_HeadAttack")) throw new Exception("Imugi Follow needs head-only idle/walk/attack clips");
            }
            if (row.id == "bulgasari" || row.id == "fox_spirit")
            {
                SamplePoseGraph298(rig.Animator, rig.Idle, 0);
                var bones = visual.GetComponentsInChildren<Transform>(true); Transform Bone(string name) => bones.Single(t => t.name == name);
                var feet = actor.GetComponent<EnemyFootPlacement298>(); if (feet == null) feet = actor.gameObject.AddComponent<EnemyFootPlacement298>();
                var chains = new[] { "Fore_L", "Fore_R", "Hind_L", "Hind_R" }.Select(name => new EnemyFootPlacement298.LegBinding { Name = name, Upper = Bone(name + "_Upper"), Lower = Bone(name + "_Lower"), Foot = Bone(name + "_Foot") }).ToArray();
                feet.Configure(actor.transform, visual.transform, Bone("Pelvis"), visual.GetComponentsInChildren<SkinnedMeshRenderer>(true), chains, ~0);
                if (!feet.Configured) throw new Exception(row.id + " four-leg foot calibration failed"); rig.FootPlacement = feet; EditorUtility.SetDirty(feet);
            }
            actor.DeathVisualSeconds = Mathf.Max(actor.DeathVisualSeconds, rig.Death.length + .1f);
            SetRef(rig.Enemy, "_renderer", visual.GetComponentsInChildren<Renderer>(true).First());
            EditorUtility.SetDirty(rig); EditorUtility.SetDirty(actor);
        }
        static void BindAudio(PrologueEncounter actor, string modelId, WorldMacroPlaytestSession session)
        {
            string audioId = modelId == "south_gate_general" ? "jangsu" : modelId;
            var profile = DataAsset("Audio_" + audioId, () => ScriptableObject.CreateInstance<EnemyAudioProfile298>()); profile.ActorId = audioId;
            var audio = actor.GetComponent<EnemyAudioEmitter298>(); if (audio == null) audio = actor.gameObject.AddComponent<EnemyAudioEmitter298>();
            audio.Profile = profile; audio.Soundscape = session.GetComponent<CompactSoundscape255>() ?? Object.FindFirstObjectByType<CompactSoundscape255>();
            audio.Rebind();
            EditorUtility.SetDirty(profile); EditorUtility.SetDirty(audio);
        }
        static void ReplaceGeneral(PrologueEncounter actor, ModelRow row, WorldMacroPlaytestSession session)
        {
            var old = actor.GetComponent<SouthGateGeneralPresentation>();
            var warning = old != null ? GetRef<GameObject>(old, "_earthWarning") : actor.GetComponent<EnemyRigMotion298>()?.EarthWarning;
            var impact = old != null ? GetRef<GameObject>(old, "_earthImpact") : actor.GetComponent<EnemyRigMotion298>()?.EarthImpact;
            var weapon = actor.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == "Temporary_ReusedMesh_Polearm")
                ?? throw new Exception("Existing owned bamboo/metal polearm is required before replacing general");
            // Detach first: on repeated candidate builds the preserved weapon is
            // inside the previous generated visual, which is about to be removed.
            weapon.SetParent(actor.transform, true);
            foreach (var r in actor.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var animator in actor.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            var previous = actor.transform.Find("Folklore298_Visual"); if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var visual = AddVisual(actor, row); BindMotion(actor, row, visual);
            var motion = actor.GetComponent<EnemyRigMotion298>(); motion.EarthWarning = warning; motion.EarthImpact = impact;
            var hand = motion.Animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand == null) throw new Exception("New Humanoid general lacks RightHand");
            // Source polearm was authored along local +Y with a 2.5m shaft. Its
            // meshes/materials and child dimensions stay unchanged. Determine the
            // grip mount from the real thrust contact pose, then restore idle.
            SamplePoseGraph298(motion.Animator, motion.Attack, motion.Attack.length * motion.AttackPeak01);
            weapon.SetParent(hand, false); weapon.localPosition = Vector3.zero;
            weapon.rotation = Quaternion.FromToRotation(Vector3.up, actor.transform.forward);
            var handScale = hand.lossyScale; weapon.localScale = new Vector3(1 / handScale.x, 1 / handScale.y, 1 / handScale.z);
            foreach (var r in weapon.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            actor.GetComponent<SouthGateGeneralController>().ConfigureWeapon(weapon);
            SamplePoseGraph298(motion.Animator, motion.Idle, 0);
            EditorUtility.SetDirty(actor.GetComponent<SouthGateGeneralController>());
            if (old != null) Object.DestroyImmediate(old); BindAudio(actor, row.id, session);
        }
        static void ReplaceDragon(PrologueEncounter actor, ModelRow row, WorldMacroPlaytestSession session)
        {
            var previousGenerated = actor.transform.Find("Folklore298_Visual");
            var oldBones = actor.GetComponentsInChildren<Transform>(true);
            var colliders = actor.GetComponentsInChildren<Collider>(true).Where(c => c.name.StartsWith("Damage_", StringComparison.Ordinal)).ToArray();
            var visuals = actor.GetComponentsInChildren<Renderer>(true);
            var visual = AddVisual(actor, row);
            var bones = visual.GetComponentsInChildren<Transform>(true); Transform Bone(string n) => bones.Single(b => b.name == n);
            var chain = Enumerable.Range(1, 24).Select(i => Bone("Body_" + i.ToString("00"))).ToArray();
            var combat = actor.GetComponent<CheongryongCombatController>();
            if (!actor.GetComponent<CheongryongBodyFollow>().Configure(Bone("Head"), chain, Bone("TailTip"))) throw new Exception("New dragon chain failed body-follow contract");
            if (!actor.GetComponent<CheongryongRigAnimation>().Configure(visual.GetComponentInChildren<Animator>(true), Clip(row, "idle"), Clip(row, "attack"), Clip(row, "tail"), combat)) throw new Exception("New dragon clips failed animation contract");
            var tail = actor.GetComponent<CheongryongTailSweepPresentation>();
            var priorTail = new SerializedObject(tail).FindProperty("_tailBones");
            var tailNames = Enumerable.Range(0, priorTail.arraySize).Select(i => priorTail.GetArrayElementAtIndex(i).objectReferenceValue.name).ToArray();
            if (!tail.Configure(combat, tailNames.Select(Bone).ToArray(), Bone("TailTip"))) throw new Exception("New dragon tail failed contract");
            var stateRig = actor.GetComponent<CheongryongRigAnimation>();
            if (!stateRig.ConfigureStateClips(Clip(row, "hit"), Clip(row, "stun"), Clip(row, "death"), actor.GetComponent<CheongryongBodyFollow>(), tail)) throw new Exception("New dragon whole-body state clips failed contract");
            actor.DeathVisualSeconds = Mathf.Max(actor.DeathVisualSeconds, Clip(row, "death").length + .1f);
            EditorUtility.SetDirty(stateRig);
            foreach (var collider in colliders)
            {
                var parent = Bone(collider.transform.parent.name); var localPosition = collider.transform.localPosition; var localRotation = collider.transform.localRotation; var worldScale = collider.transform.lossyScale;
                collider.transform.SetParent(parent, false); collider.transform.localPosition = localPosition; collider.transform.localRotation = localRotation;
                var scale = parent.lossyScale; collider.transform.localScale = new Vector3(worldScale.x / scale.x, worldScale.y / scale.y, worldScale.z / scale.z);
            }
            if (!actor.GetComponent<CheongryongColliderPoseSync>().Configure(colliders)) throw new Exception("New dragon collider sync failed");
            combat.ConfigureSockets(Bone("MouthOrigin"), Bone("Body_12"));
            // Preserve old visual source in296. Rename inactive old bone names so
            // runtime FirstOrDefault socket binding cannot choose stale transforms.
            foreach (var r in visuals) r.enabled = false;
            foreach (var old in oldBones) if (old != actor.transform && (old.name == "Head" || old.name == "MouthOrigin" || old.name == "TailTip" || old.name.StartsWith("Body_", StringComparison.Ordinal))) old.name = "Legacy298_" + old.name;
            foreach (var animator in actor.GetComponentsInChildren<Animator>(true)) if (!animator.transform.IsChildOf(visual.transform)) animator.enabled = false;
            if (previousGenerated != null) Object.DestroyImmediate(previousGenerated.gameObject);
            BindAudio(actor, row.id, session);
        }
        static void SpeciesDefaults(ModelRow row)
        {
            // TEST data defaults are explicit and may be overridden in the manifest.
            // Every species remains ordinary neutral melee: no new combo/boss rules.
            float range = 2.4f, windup = .8f, recovery = .7f, damage = 15, arc = 90, speed = 2.2f, radius = .45f, height = 1.9f;
            switch (row.id)
            {
                case "dokkaebi": range = 1.5f; windup = 1.1f; recovery = 1.05f; damage = 20; arc = 45; speed = 1.9f; radius = .55f; height = 2.25f; break;
                case "agwi": range = 1.35f; windup = 1.0f; recovery = .85f; damage = 13; arc = 65; speed = 1.45f; radius = .48f; height = 2.1f; break;
                case "changgui": range = 1.2f; windup = .8f; recovery = .8f; damage = 13; arc = 70; speed = 2.25f; radius = .4f; height = 1.85f; break;
                case "bulgasari": range = 2.8f; windup = 1.3f; recovery = 1.2f; damage = 23; arc = 95; speed = 1.4f; radius = .7f; height = 2.0f; break;
                case "fox_spirit": range = 1.9f; windup = .6f; recovery = .55f; damage = 11; arc = 70; speed = 3.1f; radius = .36f; height = 1.3f; break;
                case "imugi": range = 2.9f; windup = 1.05f; recovery = 1.0f; damage = 18; arc = 45; speed = 1.65f; radius = .6f; height = 1.4f; break;
            }
            if (row.range <= 0) row.range = range; if (row.telegraph <= 0) row.telegraph = windup; if (row.recovery <= 0) row.recovery = recovery;
            if (row.damage <= 0) row.damage = damage; if (row.arcDegrees <= 0) row.arcDegrees = arc;
            if (Mathf.Approximately(row.speed, 2.2f) || row.speed <= 0) row.speed = speed;
            if (row.capsuleRadius <= 0) row.capsuleRadius = radius; if (row.capsuleHeight <= 0) row.capsuleHeight = height;
            // TEST298: Player CC radius .28 / skin .03; enemy radii .55/.48/.40.
            // Horizontal stops .88/.80/.72 keep .05/.04/.04m shape clearance.
            // Shared EnemyController uses 3D root distance: with player feet y=0,
            // sqrt(stop^2 + baseOffset^2) = 1.4283/1.3200/1.1722m, below
            // the respective 1.50/1.35/1.20m ranges. Do not change shared semantics.
            if (row.preferredDistance <= 0) row.preferredDistance = row.id == "dokkaebi" ? .88f : row.id == "agwi" ? .80f : row.id == "changgui" ? .72f : Mathf.Max(1.1f, row.range - .6f);
            if (row.cooldownMin <= 0) row.cooldownMin = 1.2f; if (row.cooldownMax <= 0) row.cooldownMax = 2.2f;
        }
        public static string BuildCandidate()
        {
            RequireEdit(); var manifest = ReadManifest();
            foreach (var id in NewSpecies.Concat(new[] { "cheongryong", "south_gate_general" }))
            {
                var row = manifest.rows.SingleOrDefault(r => r.id == id) ?? throw new Exception("Missing required model " + id);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(id)) ?? throw new Exception("Import first " + id);
                var receipt = Inspect(row, prefab, row.model, true);
                if (receipt.status != "PASS") throw new Exception(id + " cannot replace/enter candidate: " + string.Join(";", receipt.errors));
            }
            OpenCandidate(); var session = Session(); Physics.SyncTransforms();
            var placements = new Dictionary<string, Vector3>();
            var priorBodies = session.Actors.Where(a => a.Id.StartsWith("folklore298/", StringComparison.Ordinal)).SelectMany(a => a.GetComponentsInChildren<Collider>(true)).ToArray();
            var priorEnabled = priorBodies.Select(c => c.enabled).ToArray();
            try
            {
                // A rebuild must not push new placement away from its own old actors.
                foreach (var collider in priorBodies) collider.enabled = false; Physics.SyncTransforms();
                foreach (var id in NewSpecies) { var row = manifest.rows.Single(r => r.id == id); SpeciesDefaults(row); placements[id] = FindPlacement(row, session, placements.Values); }
            }
            finally { for (int i = 0; i < priorBodies.Length; i++) priorBodies[i].enabled = priorEnabled[i]; Physics.SyncTransforms(); }
            int originalCount = session.Actors.Count(a => !a.Id.StartsWith("folklore298/", StringComparison.Ordinal));
            // All quality and placement checks above complete before deleting any
            // prior generated actor. Original296 actor roots/IDs stay in place.
            foreach (var actor in session.Actors.Where(a => a.Id.StartsWith("folklore298/", StringComparison.Ordinal)).ToArray()) Object.DestroyImmediate(actor.gameObject);
            var actors = session.Actors.Where(a => a != null).ToList();
            var specs = session.Content.Encounters.Where(e => !e.Id.StartsWith("folklore298/", StringComparison.Ordinal)).ToList();
            var receipts = new List<PlacementReceipt>();
            var root = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "Folklore298_Encounters") ?? new GameObject("Folklore298_Encounters");
            foreach (var id in NewSpecies)
            {
                var row = manifest.rows.Single(r => r.id == id); var seed = actors.Single(a => a.Id == row.templateId);
                var actor = Object.Instantiate(seed, root.transform); actor.name = "Folklore298_" + id; actor.Id = "folklore298/" + id;
                var nav = actor.GetComponent<NavMeshAgent>(); nav.enabled = false;
                foreach (Transform child in actor.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
                nav.radius = row.capsuleRadius; nav.height = row.capsuleHeight; nav.baseOffset = row.capsuleHeight * .5f;
                var capsule = actor.GetComponent<CapsuleCollider>(); if (capsule == null) throw new Exception("Ordinary source has no body capsule");
                capsule.radius = row.capsuleRadius; capsule.height = row.capsuleHeight; capsule.center = Vector3.zero; capsule.direction = 1;
                actor.transform.position = placements[id] + Vector3.up * nav.baseOffset; actor.transform.rotation = Quaternion.Euler(0, row.yaw, 0);
                actor.PatrolPoints = new[] { actor.transform.position }; actor.DetectionRange = row.detection; actor.Leash = row.leash; actor.Speed = row.speed;
                var profile = DataAsset("Attack_" + id, () => ScriptableObject.CreateInstance<EnemyAttackProfileSO>());
                profile.ApplyDefaults(EnemyArchetype.NeutralMelee);
                profile.Range = row.range; profile.Telegraph = row.telegraph; profile.Recovery = row.recovery; profile.Damage = row.damage; profile.ArcDegrees = row.arcDegrees;
                profile.CooldownRange = new Vector2(row.cooldownMin, row.cooldownMax); profile.MovementSpeedHint = row.speed; profile.PreferredDistanceHint = row.preferredDistance;
                if (!profile.TryValidate(out string error)) throw new Exception(row.id + " invalid attack data " + error); EditorUtility.SetDirty(profile);
                actor.Ranged = false; actor.PreferredDistance = profile.PreferredDistanceHint;
                actor.GetComponent<EnemyController>().Configure(profile);
                var visual = AddVisual(actor, row); BindMotion(actor, row, visual); BindAudio(actor, id, session);
                actors.Add(actor); specs.Add(new WorldMacroPlaytestSO.Encounter { Id = actor.Id, ContentId = actor.Id, Feet = placements[id], Patrol = new[] { placements[id] }, Ranged = false, RespawnOnRest = true, Detection = row.detection, Leash = row.leash, Speed = row.speed, Activation = 180 });
                receipts.Add(new PlacementReceipt { id = actor.Id, model = id, realm = session.MountainLayout.RealmAt(new Vector2(placements[id].x, placements[id].z))?.Id, sourceTemplate = row.templateId, feet = placements[id], respawnOnRest = true });
            }
            ReplaceGeneral(actors.Single(a => a.Id == "south_gate_general"), manifest.rows.Single(r => r.id == "south_gate_general"), session);
            ReplaceDragon(actors.Single(a => a.Id == "cheongryong"), manifest.rows.Single(r => r.id == "cheongryong"), session);
            session.Actors = actors.ToArray(); session.Content.Encounters = specs.ToArray();
            var wiring = new SerializedObject(session.Walker.Wiring); var targets = wiring.FindProperty("_enemies");
            if (targets == null) throw new Exception("CombatLoopWiring target array missing");
            var retained = Enumerable.Range(0, targets.arraySize).Select(i => targets.GetArrayElementAtIndex(i).objectReferenceValue as EnemyVitals).Where(v => v != null).ToList();
            foreach (var actor in actors) if (!retained.Contains(actor.GetComponent<EnemyVitals>())) retained.Add(actor.GetComponent<EnemyVitals>());
            targets.arraySize = retained.Count; for (int i = 0; i < retained.Count; i++) targets.GetArrayElementAtIndex(i).objectReferenceValue = retained[i];
            wiring.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(session.Walker.Wiring);
            var layout = session.MountainLayout;
            var places = layout.Places.Where(p => !p.Id.StartsWith("folklore298/", StringComparison.Ordinal)).ToList();
            foreach (var place in places) place.EncounterIds = (place.EncounterIds ?? Array.Empty<string>()).Where(id => !id.StartsWith("folklore298/", StringComparison.Ordinal)).ToArray();
            foreach (var receipt in receipts)
            {
                var row = manifest.rows.Single(r => r.id == receipt.model);
                places.Add(new CompactWorldLayoutSO.Place { Id = receipt.id, Realm = receipt.realm, Label = row.displayName, Purpose = "Folklore298 ordinary encounter; no campaign unlock", XZ = new Vector2(receipt.feet.x, receipt.feet.z), GroundRadius = 5, EncounterId = receipt.id, EncounterIds = new[] { receipt.id }, SceneRoots = new[] { "Folklore298_Encounters/Folklore298_" + row.id } });
                var owner = places.FirstOrDefault(p => p.Id == row.placeId); if (owner != null) owner.EncounterIds = owner.EncounterIds.Concat(new[] { receipt.id }).Distinct().ToArray();
            }
            layout.Places = places.ToArray(); EditorUtility.SetDirty(layout);
            EditorUtility.SetDirty(session); EditorUtility.SetDirty(session.Content); Physics.SyncTransforms();
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            File.WriteAllText(Path.Combine(OutputRoot, "candidate.json"), JsonUtility.ToJson(new BuildReceipt { timestamp = DateTime.UtcNow.ToString("O"), scene = ScenePath, saveSlot = session.Content.SaveSlot, originalActors = originalCount, resultingActors = actors.Count, placements = receipts.ToArray(), preserved = new[] { "296 and295 source scenes", "existing9actorIDs/positions", "original5ordinary Meshy visuals", "Sinmok", "growth lesson", "campaign/quest/boss reward/unlock schema", "terrain/water/architecture/NavMesh" } }, true));
            return "Saved Folklore298 candidate: " + actors.Count + " actors, six new ordinary encounters; original save slot untouched. Play combat/save QA remains required.";
        }
    }
}
