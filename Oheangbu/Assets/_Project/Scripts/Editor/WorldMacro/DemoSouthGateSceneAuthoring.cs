using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Additive local authoring. Existing capital arch, suppliers, terrain, map routes and campaign stages remain owned elsewhere.
    public static class DemoSouthGateSceneAuthoring
    {
        public const string RootName = "Demo_SouthGate_General", BossId = "south_gate_general", RestId = "south_gate_approach_rest";
        public const string Folder = "Assets/_Project/Art/Demo/SouthGate";
        const string ModelSource = "Assets/_Project/Art/Characters/PlaytestRecoveryGripA/Player_C02_GripA.fbx";
        const string IdleSource = "Assets/_Project/Art/Characters/PlaytestNaturalLocomotion/Animations/IdleStill.anim";
        const string WalkSource = "Assets/_Project/Art/Characters/PlaytestNaturalLocomotion/Animations/WalkForward.anim";
        const string ActionSource = "Assets/_Project/Art/C02_RigFaceLab/Animations/Integrated_B2_C3_Attack.anim";
        const string ShaftSource = "Assets/_Project/Art/SpellVFX120/BambooBolt/VFX120_BambooBolt_001.asset";
        const string ShaftMaterial = "Assets/_Project/Art/SpellVFX120/BambooBolt/M_BambooBolt_001.mat";
        const string TipSource = "Assets/_Project/Art/SpellVFX120/AreaFive/Needle.asset";
        const string TipMaterial = "Assets/_Project/Art/SpellVFX120/AreaFive/Needle.mat";
        const string DoorSource = "Assets/HwaseongForteressGate/Prefabs/SM_G_MetalDoor_001.prefab";
        const string HelmetSource = "Assets/Korea_TreasureProps/Prefabs/SM_004_Helmet.prefab";
        const string WarningSource = "Assets/_Project/Art/SpellVFX120/Traditional/Catalog/KTP_Cast_Earth.prefab";
        const string ImpactSource = "Assets/_Project/Art/SpellVFX120/Traditional/Catalog/KTP_Impact_Earth.prefab";
        static readonly string[] Sources = { ModelSource, IdleSource, WalkSource, ActionSource, ShaftSource, ShaftMaterial, TipSource, TipMaterial, DoorSource, HelmetSource, WarningSource, ImpactSource };
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Demo/SouthGate"));
        static WorldMacroPlaytestSession Session => Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        [Serializable] sealed class Check { public string name, status, detail; }
        [Serializable] sealed class Candidate
        { public Vector3 position; public float spread, maxSlope, score, routeDistance, gateDistance; public bool suitable; public string reason; }
        [Serializable] sealed class RouteProbe { public Vector3 authored, physical; public float terrainY, slope, authoredMinusPhysical; public string support; }
        [Serializable] sealed class CeilingProbe { public Vector3 point; public float height; public string collider; }
        [Serializable] sealed class Report
        {
            public string status, scope, archPath; public string[] sources = Sources;
            public string temporaryArt = "Reused C02 human/avatar, existing humanoid locomotion and body-probe action clip. No dedicated polearm attack source was found. Spear is assembled from existing bamboo-bolt and metal-needle meshes; helmet is a supplier source. Dedicated general costume/two-hand polearm animation remains unfinished.";
            public Vector3 gateCentre, forward, approachPoint, boss, rest; public float openingWidth, openingHeight, restPathMetres, restWalkSeconds;
            public int examinedCandidates; public RouteProbe[] nearbyRoute; public CeilingProbe[] ceilingProbes; public Candidate[] oppositeSideDiagnostics;
            public int actors, ownedActors, implementedStages, triangles; public Candidate[] candidates; public Check[] checks;
        }
        static void RequireEdit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Session == null || Session.gameObject.scene.path != DemoFoundationAuthoring.Scene ||
                AssetDatabase.GetAssetPath(Session.Content) != DemoFoundationAuthoring.Folder + "/Content.asset")
                throw new InvalidOperationException("Dedicated W_Demo_Campaign Edit scene and isolated demo Content required.");
        }
        public static string Execute(string command)
        {
            RequireEdit(); Directory.CreateDirectory(Output);
            if (command == "survey") return Save("scene_survey.json", Survey());
            if (command == "apply") return Apply();
            if (command == "audit") return Save("scene_audit.json", Audit());
            throw new ArgumentException("Use survey, apply or audit. No stage is enabled here.");
        }
        static string Save(string name, Report report) { string json = JsonUtility.ToJson(report, true); File.WriteAllText(Path.Combine(Output, name), json); return json; }
        static T Asset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing required existing source: " + path);
        static void FolderExists(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); FolderExists(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        static Transform Node(Transform parent, string name)
        { var node = new GameObject(name).transform; node.SetParent(parent, false); return node; }
        static Bounds BoundsOf(GameObject value)
        {
            var renderers = value.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("No real visual source on " + value.name);
            Bounds bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds); return bounds;
        }
        static Transform Arch()
        {
            var arches = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => t.name == "CAP_GATE_ARCH" && t.gameObject.scene == Session.gameObject.scene).ToArray();
            if (arches.Length != 1) throw new InvalidOperationException("Exactly one real capital CAP_GATE_ARCH required.");
            return arches[0];
        }
        static string PathOf(Transform t) { string result = t.name; while (t.parent != null) { t = t.parent; result = t.name + "/" + result; } return result; }
        static bool Ground(Vector3 p, out RaycastHit hit)
        {
            hit = default;
            var hits = Physics.RaycastAll(new Vector3(p.x, 2200, p.z), Vector3.down, 4400, 1, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToArray();
            var terrain = hits.FirstOrDefault(h => h.collider.name.StartsWith("Terrain_", StringComparison.Ordinal) && h.normal.y > 0);
            if (terrain.collider == null) return false;
            hit = terrain;
            // Existing authored ground/decks count as support; never choose a gate roof, actor, table or scenery rock.
            foreach (var value in hits)
            {
                string path = PathOf(value.transform);
                bool floor = path.Contains("Palace_Forecourt_AssetSurface") || path.Contains("Stone_Terrace") ||
                    path.Contains("Gate_Approach_Surface") || path.Contains("Gate_Passage_Surface");
                if (floor && value.normal.y > 0 && value.point.y >= terrain.point.y - .1f && value.point.y <= terrain.point.y + 3f)
                { hit = value; break; }
            }
            return true;
        }
        static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        static float RouteDistance(Vector3 p, Vector3[] route)
        {
            float distance = float.MaxValue;
            for (int i = 1; i < route.Length; i++) distance = Mathf.Min(distance, WorldMacroTerrain.SegmentDistance(p.x, p.z, route[i-1], route[i], out _));
            return distance;
        }
        static Candidate Examine(Vector3 p, float radius)
        {
            var c = new Candidate { position = p }; var heights = new List<float>();
            for (int i = 0; i < 17; i++)
            {
                float r = i == 0 ? 0 : i <= 8 ? radius * .5f : radius, a = (i - 1) * Mathf.PI / 4;
                Vector3 point = p + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r;
                if (!Ground(point, out var h)) { c.reason = "terrain support absent"; return c; }
                if (i == 0) c.position = h.point + Vector3.up * .06f;
                heights.Add(h.point.y); c.maxSlope = Mathf.Max(c.maxSlope, Vector3.Angle(h.normal, Vector3.up));
            }
            c.spread = heights.Max() - heights.Min(); c.suitable = c.maxSlope <= 22 && c.spread <= radius * .45f;
            c.reason = c.suitable ? "supported terrain; physical clearance checked separately" : "steep or uneven"; return c;
        }
        static bool ClearCharacter(Vector3 feet)
        {
            foreach (var collider in Physics.OverlapCapsule(feet + Vector3.up * .34f, feet + Vector3.up * 1.5f, .3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (collider.transform.IsChildOf(Session.Walker.Body.transform)) continue;
                if (collider.GetComponentInParent<PrologueEncounter>() != null) continue;
                return false;
            }
            return true;
        }
        static Report Survey()
        {
            Physics.SyncTransforms();
            var arch = Arch(); var bounds = BoundsOf(arch.gameObject);
            var report = new Report { archPath = PathOf(arch), scope = "Read-only actual arch aperture/terrain/support and source survey. No world regeneration, source change, collision removal or campaign activation." };
            foreach (string source in Sources) if (AssetDatabase.LoadMainAssetAtPath(source) == null) throw new InvalidOperationException("Missing source " + source);
            var route = WorldMacroBuilder.Sheet.Routes.SingleOrDefault(r => r.Id == "Road_SouthPost_Gate");
            if (route == null || route.Points.Length < 2) throw new InvalidOperationException("Existing south-post to gate approach route missing.");
            report.gateCentre = new Vector3(bounds.center.x, 0, bounds.center.z);
            if (!Ground(report.gateCentre, out var floor)) throw new InvalidOperationException("No terrain floor below real arch centre.");
            report.gateCentre.y = floor.point.y + .03f;
            // Do not infer the approach side from the final short turn: derive it from actual route positions around the moved arch.
            var approachNodes = route.Points.Where(p => FlatDistance(p, report.gateCentre) >= 22 && FlatDistance(p, report.gateCentre) <= 90).ToArray();
            if (approachNodes.Length == 0) throw new InvalidOperationException("No real approach route within 22–90m of the existing gate.");
            report.approachPoint = approachNodes.Aggregate(Vector3.zero, (sum,p) => sum + p) / approachNodes.Length;
            var outward = Vector3.ProjectOnPlane(report.approachPoint - report.gateCentre, Vector3.up).normalized;
            report.forward = Vector3.ProjectOnPlane(arch.forward, Vector3.up).normalized;
            if (Vector3.Dot(-report.forward, outward) < 0) report.forward = -report.forward;
            if (Mathf.Abs(Vector3.Dot(report.forward, outward)) < .25f) throw new InvalidOperationException("Route approaches the side of the arch; requires geographic review.");
            var right = Vector3.Cross(Vector3.up, report.forward);
            var routeProbes = new List<RouteProbe>();
            foreach (var p in route.Points.Where(p => FlatDistance(p, report.gateCentre) <= 160))
            {
                if (!Ground(p, out var ground)) continue;
                float terrainY = ground.point.y;
                foreach(var h in Physics.RaycastAll(new Vector3(p.x,2200,p.z), Vector3.down,4400,1,QueryTriggerInteraction.Ignore))
                    if(h.collider.name.StartsWith("Terrain_",StringComparison.Ordinal)) { terrainY=h.point.y;break; }
                routeProbes.Add(new RouteProbe { authored=p, physical=ground.point, terrainY=terrainY, slope=Vector3.Angle(ground.normal,Vector3.up),
                    authoredMinusPhysical=p.y-ground.point.y, support=PathOf(ground.transform) });
            }
            report.nearbyRoute = routeProbes.ToArray();
            bool HitsArch(Vector3 p, Vector3 direction, float distance) => Physics.RaycastAll(p, direction, distance, ~0, QueryTriggerInteraction.Ignore).Any(h => h.transform.IsChildOf(arch));
            float left = 0, rightEdge = 0;
            for (float x = 0; x <= Mathf.Min(12, bounds.size.x * .5f); x += .2f)
            { if (HitsArch(report.gateCentre - right * x - report.forward * 15 + Vector3.up * 1.5f, report.forward, 30)) break; left = x; }
            for (float x = 0; x <= Mathf.Min(12, bounds.size.x * .5f); x += .2f)
            { if (HitsArch(report.gateCentre + right * x - report.forward * 15 + Vector3.up * 1.5f, report.forward, 30)) break; rightEdge = x; }
            report.gateCentre += right * ((rightEdge - left) * .5f); report.openingWidth = left + rightEdge;
            float ceiling = 20; var ceilingProbes = new List<CeilingProbe>();
            foreach (float fraction in new[] { -.4f, 0, .4f })
            {
                var h = Physics.RaycastAll(report.gateCentre + right * report.openingWidth * fraction + Vector3.up * .1f, Vector3.up, 20, ~0, QueryTriggerInteraction.Ignore)
                    .Where(rayHit => rayHit.transform.IsChildOf(arch)).OrderBy(rayHit => rayHit.distance).FirstOrDefault();
                if (h.collider == null) { ceilingProbes.Add(new CeilingProbe { height=-1, collider="No inner arch support" }); continue; }
                ceiling = Mathf.Min(ceiling, h.point.y-report.gateCentre.y);
                ceilingProbes.Add(new CeilingProbe { point=h.point, height=h.point.y-report.gateCentre.y, collider=PathOf(h.transform) });
            }
            report.ceilingProbes = ceilingProbes.ToArray();
            report.openingHeight = ceiling;
            var choices = new List<Candidate>(); var seen = new HashSet<Vector2Int>();
            void AddCandidate(Vector3 p)
            {
                if (!seen.Add(new Vector2Int(Mathf.RoundToInt(p.x/3),Mathf.RoundToInt(p.z/3)))) return;
                float gateDistance=FlatDistance(p,report.gateCentre), roadDistance=RouteDistance(p,route.Points);
                if(gateDistance<18 || gateDistance>160 || roadDistance>36 || Vector3.Dot(p-report.gateCentre,-report.forward)<12) return;
                var c = Examine(p,9); c.gateDistance=gateDistance;c.routeDistance=roadDistance;
                if(c.suitable && !ClearCharacter(c.position)) { c.suitable=false;c.reason="body capsule obstructed"; }
                c.score=c.maxSlope+c.spread+gateDistance*.16f+roadDistance*.3f;choices.Add(c);
            }
            foreach (var p in route.Points.Where(p => FlatDistance(p,report.gateCentre)<=160))
                foreach(float x in new[]{0f,-8f,8f,-16f,16f,-24f,24f}) AddCandidate(p+right*x);
            for(float distance=20;distance<=140;distance+=12) for(float lateral=-36;lateral<=36;lateral+=12)
                AddCandidate(report.gateCentre-report.forward*distance+right*lateral);
            report.oppositeSideDiagnostics = new[]{18f,30f,48f}.Select(distance=>Examine(report.gateCentre+report.forward*distance,9)).ToArray();
            Candidate selected = null;
            foreach (var selection in choices.Where(c => c.suitable).OrderBy(c => c.score).Take(12))
            {
                report.boss = selection.position;
                var restOptions = new List<Candidate>();
                foreach (float distance in new[] { 37f, 29f, 43f }) foreach (float lateral in new[] { -6f, 6f, -10f, 10f, -18f, 18f })
                {
                    var rest = Examine(report.boss - report.forward * distance + right * lateral, 2);
                    rest.routeDistance=RouteDistance(rest.position,route.Points);
                    if (rest.suitable && rest.routeDistance<=32 && ClearCharacter(rest.position))
                    {rest.score=rest.maxSlope+rest.spread+rest.routeDistance*.3f; restOptions.Add(rest);}
                }
                var chosenRest=restOptions.OrderBy(c=>c.score).FirstOrDefault();
                if(chosenRest!=null) { selected=selection;report.rest=chosenRest.position;break; }
            }
            report.examinedCandidates=choices.Count;
            report.candidates = choices.OrderByDescending(c=>c.suitable).ThenBy(c=>c.score).Take(36).ToArray();
            report.status = selected != null && report.rest != Vector3.zero && report.openingWidth >= 2.4f && report.openingWidth <= 12 && report.openingHeight >= 3 && report.openingHeight < 15 && ceilingProbes.All(p=>p.height>0) ? "PASS" : "FAIL";
            return report;
        }
        static void Backup()
        {
            string folder = Path.Combine(Output, "Backups"); Directory.CreateDirectory(folder); var manifest = new List<string>();
            foreach (string source in new[] { DemoFoundationAuthoring.Scene, AssetDatabase.GetAssetPath(Session.Content), AssetDatabase.GetAssetPath(Session.Content.Campaign) })
                foreach (string file in new[] { source, source + ".meta" })
                {
                    if (!File.Exists(file)) continue; string target = Path.Combine(folder, Path.GetFileName(file));
                    if (!File.Exists(target)) File.Copy(file, target);
                    using (var sha = SHA256.Create()) manifest.Add(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(target))).Replace("-", "") + " " + Path.GetFileName(target));
                }
            string receipt = Path.Combine(folder, "SHA256.txt"); if (!File.Exists(receipt)) File.WriteAllLines(receipt, manifest);
        }
        static void Set(Object target, string property, Object value)
        { var so = new SerializedObject(target); var p = so.FindProperty(property); if (p == null) throw new InvalidOperationException(property); p.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        static void Fit(GameObject model, Vector3 dimensions, Vector3 bottom)
        {
            // Fit in world-aligned source space, then restore the authored socket orientation.
            var parent = model.transform.parent; var rotation = model.transform.rotation;
            model.transform.SetParent(null, true); model.transform.rotation = Quaternion.identity;
            var bounds = BoundsOf(model); Vector3 scale = model.transform.localScale;
            scale = Vector3.Scale(scale, new Vector3(dimensions.x > 0 ? dimensions.x / Mathf.Max(.0001f, bounds.size.x) : 1,
                dimensions.y > 0 ? dimensions.y / Mathf.Max(.0001f, bounds.size.y) : 1, dimensions.z > 0 ? dimensions.z / Mathf.Max(.0001f, bounds.size.z) : 1));
            model.transform.localScale = scale; bounds = BoundsOf(model);
            var offset = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) - model.transform.position;
            model.transform.rotation = rotation; model.transform.position = bottom - rotation * offset;
            model.transform.SetParent(parent, true);
        }
        static void MakeNavigation(Transform root, Report survey)
        {
            var volume = new Bounds(survey.rest,Vector3.one); volume.Encapsulate(survey.gateCentre);volume.Encapsulate(survey.boss);volume.Expand(new Vector3(70,40,70));
            var nav = Node(root, "LocalNavigation"); nav.position = volume.center;
            var surface = nav.gameObject.AddComponent<NavMeshSurface>(); surface.collectObjects = CollectObjects.Volume;
            surface.center = Vector3.zero; surface.size = volume.size; surface.layerMask = 1;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders; surface.overrideVoxelSize = true; surface.voxelSize = .2f;
            surface.overrideTileSize = true; surface.tileSize = 128; Physics.SyncTransforms(); surface.BuildNavMesh();
            var generated = surface.navMeshData; if (generated == null) throw new InvalidOperationException("Local navigation generation failed.");
            surface.RemoveData(); var data = Object.Instantiate(generated); AssetDatabase.CreateAsset(data, Folder + "/Navigation.asset");
            if (!AssetDatabase.Contains(generated)) Object.DestroyImmediate(generated); surface.navMeshData = data; surface.AddData();
        }
        static void MeshPart(Transform parent, string name, Mesh mesh, Material material, float length, float diameter, float y)
        {
            var part = Node(parent, name); var b = mesh.bounds;
            int axis = b.size.y >= b.size.x && b.size.y >= b.size.z ? 1 : b.size.z >= b.size.x ? 2 : 0;
            Vector3 direction = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            part.localRotation = Quaternion.FromToRotation(direction, Vector3.up);
            Vector3 scale = Vector3.one * (diameter / Mathf.Max(.0001f, axis == 0 ? Mathf.Max(b.size.y, b.size.z) : axis == 1 ? Mathf.Max(b.size.x, b.size.z) : Mathf.Max(b.size.x, b.size.y)));
            scale[axis] = length / Mathf.Max(.0001f, b.size[axis]); part.localScale = scale;
            part.localPosition = Vector3.up * y - part.localRotation * Vector3.Scale(b.center, scale);
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh; part.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
        static PrologueEncounter CreateActor(Transform root, Report survey)
        {
            var seed = Session.Actors.First(a => a != null && a.GetComponent<EnemyController>() != null && a.GetComponent<CheongryongCombatController>() == null);
            if (!NavMesh.SamplePosition(survey.boss, out var spawn, 3, NavMesh.AllAreas)) throw new InvalidOperationException("No local boss navigation support.");
            var actor = Object.Instantiate(seed, root); actor.gameObject.SetActive(false); actor.name = BossId; actor.Id = BossId; actor.Session = null;
            var prefab = PrefabUtility.GetOutermostPrefabInstanceRoot(actor.gameObject); if (prefab == actor.gameObject) PrefabUtility.UnpackPrefabInstance(prefab, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (Transform child in actor.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            foreach (var collider in actor.GetComponents<Collider>()) Object.DestroyImmediate(collider);
            foreach (var component in actor.GetComponents<EnemyAttackPresentation>()) Object.DestroyImmediate(component);
            foreach (var renderer in actor.GetComponents<Renderer>()) Object.DestroyImmediate(renderer);
            foreach (var filter in actor.GetComponents<MeshFilter>()) Object.DestroyImmediate(filter);
            foreach (var oldAnimator in actor.GetComponents<Animator>()) Object.DestroyImmediate(oldAnimator);
            var sourceConfig = new SerializedObject(seed.GetComponent<EnemyVitals>()).FindProperty("_config").objectReferenceValue as CombatConfigSO;
            var config = Object.Instantiate(sourceConfig); config.name = "SouthGateGeneral_Config_TEST";
            var tuning = new SerializedObject(config); tuning.FindProperty("_enemyMaxHp").floatValue = 480; tuning.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(config, Folder + "/CombatConfig.asset");
            var agent = actor.GetComponent<NavMeshAgent>(); agent.enabled = false; agent.baseOffset = .875f; agent.radius = .42f; agent.height = 2.05f;
            actor.transform.SetPositionAndRotation(spawn.position + Vector3.up * .875f, Quaternion.LookRotation(-survey.forward)); actor.transform.localScale = Vector3.one;
            actor.Player = Session.Walker.Body.transform; actor.Speed = 1.7f; actor.Ranged = false; actor.PreferredDistance = 2.4f; actor.Leash = 24; actor.DetectionRange = 17;
            actor.PatrolPoints = new[] { actor.transform.position };
            var capsule = actor.gameObject.AddComponent<CapsuleCollider>(); capsule.radius = .42f; capsule.height = 2.05f; capsule.center = Vector3.up * .15f;
            Set(actor.GetComponent<EnemyVitals>(), "_config", config); var legacy = actor.GetComponent<EnemyController>();
            Set(legacy, "_config", config); Set(legacy, "_renderer", null); legacy.AttackEnabled = false; legacy.enabled = false;
            var profile = ScriptableObject.CreateInstance<SouthGateGeneralProfile>(); AssetDatabase.CreateAsset(profile, Folder + "/GeneralProfile.asset");
            var controller = actor.gameObject.AddComponent<SouthGateGeneralController>(); controller.ConfigureProfile(profile, config); controller.AttackEnabled = false;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(Asset<GameObject>(ModelSource), actor.transform); model.name = "Temporary_General_C02";
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (var script in model.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
            var bounds = BoundsOf(model); float heightScale = 2.05f / Mathf.Max(.01f, bounds.size.y); model.transform.localScale *= heightScale;
            bounds = BoundsOf(model); model.transform.position += spawn.position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(ModelSource).OfType<Avatar>().FirstOrDefault(a => a.isHuman && a.isValid);
            animator.applyRootMotion = false; animator.runtimeAnimatorController = null;
            if (animator.avatar == null) throw new InvalidOperationException("Existing humanoid source Avatar missing.");
            var hand = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "RightHand");
            var head = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => string.Equals(t.name, "Head", StringComparison.OrdinalIgnoreCase));
            if (hand == null) throw new InvalidOperationException("C02 right-hand socket missing.");
            var weapon = Node(hand, "Temporary_ReusedMesh_Polearm"); var hs = hand.lossyScale;
            weapon.localScale = new Vector3(1 / Mathf.Max(.0001f, Mathf.Abs(hs.x)), 1 / Mathf.Max(.0001f, Mathf.Abs(hs.y)), 1 / Mathf.Max(.0001f, Mathf.Abs(hs.z)));
            weapon.localRotation = Quaternion.Euler(80, 0, 15);
            MeshPart(weapon, "ReusedBambooShaft", Asset<Mesh>(ShaftSource), Asset<Material>(ShaftMaterial), 2.5f, .065f, .2f);
            MeshPart(weapon, "ReusedMetalTip", Asset<Mesh>(TipSource), Asset<Material>(TipMaterial), .52f, .12f, 1.64f);
            if (head != null)
            {
                var helmet = (GameObject)PrefabUtility.InstantiatePrefab(Asset<GameObject>(HelmetSource), head);
                PrefabUtility.UnpackPrefabInstance(helmet, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                Fit(helmet, new Vector3(.48f, .42f, .5f), head.position + Vector3.up * .02f);
                foreach (var collider in helmet.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            }
            controller.ConfigureWeapon(weapon);
            var presentation = actor.gameObject.AddComponent<SouthGateGeneralPresentation>();
            presentation.Configure(controller, animator, weapon, Asset<AnimationClip>(IdleSource), Asset<AnimationClip>(WalkSource), Asset<AnimationClip>(ActionSource), Asset<GameObject>(WarningSource), Asset<GameObject>(ImpactSource));
            actor.gameObject.SetActive(true); controller.enabled = false; return actor;
        }
        static void CreateDoors(Transform root, Report survey)
        {
            var gate = Node(root, "VictoryGateLeaves"); gate.position = survey.gateCentre; gate.rotation = Quaternion.LookRotation(survey.forward);
            float half = survey.openingWidth * .5f, height = survey.openingHeight - .16f;
            var left = Node(gate, "LeftHinge"); left.localPosition = Vector3.left * half;
            var right = Node(gate, "RightHinge"); right.localPosition = Vector3.right * half;
            foreach (var hinge in new[] { left, right })
            {
                var leaf = (GameObject)PrefabUtility.InstantiatePrefab(Asset<GameObject>(DoorSource), hinge);
                PrefabUtility.UnpackPrefabInstance(leaf, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                Fit(leaf, new Vector3(half + .06f, height, .26f), gate.position + gate.right * (hinge == left ? -half * .5f : half * .5f));
                foreach (var collider in leaf.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            }
            var blocker = gate.gameObject.AddComponent<BoxCollider>(); blocker.center = Vector3.up * height * .5f; blocker.size = new Vector3(survey.openingWidth + .2f, height, .5f);
            var door = gate.gameObject.AddComponent<SouthGateDoorPresentation>(); door.Configure(left, right, new Collider[] { blocker }); door.ConfigureSession(Session);
        }
        static string Apply()
        {
            if (GameObject.Find(RootName) != null) return Save("scene_audit.json", Audit());
            var survey = Survey(); Save("scene_survey.json", survey); if (survey.status != "PASS") throw new InvalidOperationException("South gate survey must pass before additive authoring.");
            var table = GameObject.Find("Playtest_Village_Office")?.transform.Find("Clerk_WorkTable") ?? GameObject.Find("Playtest_OwnedAssets")?.transform.Find("Geumpyo_ThatchedInn/Rest_Low_Table");
            if (table == null) throw new InvalidOperationException("Existing approved rest table missing.");
            if (!AssetDatabase.LoadAllAssetsAtPath(ModelSource).OfType<Avatar>().Any(a => a.isHuman && a.isValid))
                throw new InvalidOperationException("Existing humanoid source Avatar missing before apply.");
            var seed = Session.Actors.FirstOrDefault(a => a != null && a.GetComponent<EnemyController>() != null && a.GetComponent<CheongryongCombatController>() == null);
            if (seed == null || seed.GetComponent<NavMeshAgent>() == null || seed.GetComponent<EnemyVitals>() == null ||
                new SerializedObject(seed.GetComponent<EnemyVitals>()).FindProperty("_config").objectReferenceValue == null)
                throw new InvalidOperationException("Existing actor/config/navigation seed missing before apply.");
            Backup(); FolderExists(Folder);
            if (AssetDatabase.LoadMainAssetAtPath(Folder + "/CombatConfig.asset") != null || AssetDatabase.LoadMainAssetAtPath(Folder + "/Navigation.asset") != null)
                throw new InvalidOperationException("Owned partial assets already exist; review recovery before retry, do not overwrite.");
            var root = new GameObject(RootName).transform; MakeNavigation(root, survey); var actor = CreateActor(root, survey); CreateDoors(root, survey);
            var rest = Node(root, RestId); rest.position = survey.rest;
            var copy = Object.Instantiate(table, rest); copy.name = "OwnedRestTable"; copy.localPosition = Vector3.zero; copy.localRotation = Quaternion.identity;
            var point = rest.gameObject.AddComponent<WorldMacroContentPoint>(); point.Id = RestId; point.Visual = copy; point.CombatConnected = true;
            var content = Session.Content;
            Session.Actors = Session.Actors.Where(a => a != null && a.Id != BossId).Concat(new[] { actor }).ToArray();
            content.Encounters = content.Encounters.Where(e => e.Id != BossId).Concat(new[] { new WorldMacroPlaytestSO.Encounter { Id = BossId, ContentId = BossId,
                Feet = actor.transform.position - Vector3.up * .875f, Patrol = new[] { actor.transform.position - Vector3.up * .875f }, Ranged = false, Speed = 1.7f, Detection = 17, Leash = 24, Activation = 160, RespawnOnRest = false } }).ToArray();
            content.Points = content.Points.Where(p => p.Id != RestId).Concat(new[] { new PrologueContentSO.Point { Id = RestId, Kind = PrologueInteractionKind.Rest,
                Position = survey.rest, Radius = 3, Prompt = "남문 앞 쉼터에서 휴식", Text = "성문을 마주하기 전 숨을 고른다." } }).ToArray();
            var restFeet = survey.rest - survey.forward * 2;
            if (!Ground(restFeet, out var floor)) throw new InvalidOperationException("Checkpoint ground vanished."); restFeet = floor.point + Vector3.up * .06f;
            content.Checkpoints = (content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>()).Where(p => p.Id != RestId).Concat(new[] { new WorldMacroPlaytestSO.CheckpointSpec { Id = RestId, Label = "남문 앞 쉼터", Feet = restFeet, Yaw = Quaternion.LookRotation(survey.forward).eulerAngles.y, Shop = true } }).ToArray();
            Session.PreviewPoints = Session.PreviewPoints.Where(p => p != null).Concat(new[] { point }).ToArray();
            var wiring = new SerializedObject(Session.Walker.Wiring); var enemies = wiring.FindProperty("_enemies"); enemies.arraySize = Session.Actors.Length;
            for (int i = 0; i < Session.Actors.Length; i++) { enemies.GetArrayElementAtIndex(i).objectReferenceValue = Session.Actors[i].GetComponent<EnemyVitals>(); Session.Actors[i].GetComponent<NavMeshAgent>().enabled = false; }
            wiring.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(content); EditorUtility.SetDirty(Session); AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(Session.gameObject.scene); EditorSceneManager.SaveScene(Session.gameObject.scene);
            return Save("scene_audit.json", Audit());
        }
        static Report Audit()
        {
            Physics.SyncTransforms();
            var report = new Report { scope = "Authored scene/configuration audit. Combat API checks, manual traversal, dedicated polearm animation, victory transaction and runtime gate opening remain separate." };
            var checks = new List<Check>(); void Check(bool pass, string name, string detail = "") => checks.Add(new Check { name = name, status = pass ? "PASS" : "FAIL", detail = detail });
            var root = GameObject.Find(RootName); var actors = Session.Actors.Where(a => a != null && a.Id == BossId).ToArray();
            report.actors = Session.Actors.Length; report.ownedActors = actors.Length; report.implementedStages = Session.Content.Campaign.Stages.Count(s => s.Implemented);
            Check(root != null && actors.Length == 1, "one owned root and one general actor");
            Check(Session.Content.Encounters.Count(e => e.Id == BossId && !e.RespawnOnRest) == 1, "single persistent boss spec");
            Check(Session.Content.Checkpoints.Count(c => c.Id == RestId) == 1, "one authored approach checkpoint");
            if (root != null)
            {
                var door = root.GetComponentInChildren<SouthGateDoorPresentation>(); Check(door != null && door.IsConfigured && !door.OpenAnimationComplete, "configured closed victory door; no false terminal state");
                Check(root.GetComponentsInChildren<NavMeshSurface>().Length == 1, "one local navigation volume");
            }
            if (actors.Length == 1)
            {
                var actor = actors[0]; var controller = actor.GetComponent<SouthGateGeneralController>(); var presentation = actor.GetComponent<SouthGateGeneralPresentation>();
                Check(controller != null && controller.Profile != null && controller.Profile.TryValidate(out _), "general profile configured");
                Check(!actor.GetComponent<EnemyController>().enabled && !actor.GetComponent<NavMeshAgent>().enabled, "legacy attack and nav Update disabled in saved edit state");
                Check(presentation != null && presentation.IsConfigured, "existing humanoid clips/avatar and weapon configured", presentation != null ? presentation.TemporaryArtNotice : "missing");
                Check(actor.GetComponent<EnemyVitals>().MaxHp == 480, "isolated 480 HP TEST configuration");
                report.boss = actor.transform.position - Vector3.up * .875f;
                foreach (var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (skin.sharedMesh != null)
                    for (int sub = 0; sub < skin.sharedMesh.subMeshCount; sub++) report.triangles += (int)skin.sharedMesh.GetIndexCount(sub) / 3;
                foreach (var filter in actor.GetComponentsInChildren<MeshFilter>(true)) if (filter.sharedMesh != null)
                    for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++) report.triangles += (int)filter.sharedMesh.GetIndexCount(sub) / 3;
                var rest = Session.Content.Checkpoints.SingleOrDefault(c => c.Id == RestId);
                if (rest != null && NavMesh.SamplePosition(rest.Feet, out var a, 3, NavMesh.AllAreas) && NavMesh.SamplePosition(report.boss, out var b, 3, NavMesh.AllAreas))
                {
                    var path = new NavMeshPath(); bool found = NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                    for (int i = 1; i < path.corners.Length; i++) report.restPathMetres += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                    report.restWalkSeconds = report.restPathMetres / 2.2f; report.rest = rest.Feet;
                    Check(found && report.restWalkSeconds <= 30, "checkpoint returns to boss within 30 TEST walking seconds", report.restWalkSeconds + "s actual NavMesh path / 2.2m/s");
                }
                else Check(false, "checkpoint/boss navigation support");
            }
            report.checks = checks.ToArray(); report.status = checks.All(c => c.status == "PASS") ? "PASS" : "FAIL"; return report;
        }
    }
}
