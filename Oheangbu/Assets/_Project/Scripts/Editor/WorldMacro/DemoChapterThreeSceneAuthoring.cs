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
    // Additive, opt-in authoring. No terrain, route, vegetation or campaign-stage regeneration.
    public static class DemoChapterThreeSceneAuthoring
    {
        public const string RootName = "Demo_Chapter3_GrowthAndCheongryong";
        public const string Folder = "Assets/_Project/Art/Demo/Chapter3";
        public const string BossId = "cheongryong";
        public const string LessonActorId = "demo_growth_lesson";
        public const string CheckpointId = "cheongryong_approach_rest";
        const string BossSource = Folder + "/Cheongryong/SM_Cheongryong_Prototype.fbx";
        const string TreeSource = "Assets/_Project/Art/SpellVFX120/Botanical/PF_PlantedTree.prefab";
        const string WarningSource = "Assets/_Project/Art/SpellVFX120/AreaRift/Cast_고.prefab";
        const string RootSource = "Assets/_Project/Art/SpellVFX120/AreaFive/Body_고.prefab";
        const string ContactSource = "Assets/_Project/Art/SpellVFX120/AreaFive/AreaContact_0.prefab";
        const string BoltMeshSource = "Assets/_Project/Art/SpellVFX120/BambooBolt/VFX120_BambooBolt_001.asset";
        const string BoltMaterialSource = "Assets/_Project/Art/SpellVFX120/BambooBolt/M_BambooBolt_001.mat";
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Demo/Chapter3"));
        static WorldMacroPlaytestSession Session => Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        static bool Owned(string id) => id == BossId || id == LessonActorId;
        [Serializable] sealed class Check { public string name, status, detail; }
        [Serializable] sealed class Probe { public Vector3 point; public float slope; public bool supported; }
        [Serializable] sealed class Candidate
        {
            public string id; public Vector3 centre; public Probe[] samples;
            public float maxSlope, heightSpread, routeDistance, relocationMetres, score; public bool suitable, terrainConnection;
        }
        [Serializable] sealed class SurveyReport
        {
            public string status, scene, terrainRevision, scope; public Candidate[] candidates;
            public Vector3 boss, lesson, checkpoint, bossSearchOrigin, lessonSearchOrigin;
            public int centreProbes, detailedCandidates, passingCandidates; public float searchRadius = 240, gridStep = 32;
        }
        [Serializable] sealed class Report
        {
            public string status, scope; public Check[] checks; public int actors, ownedActors, implementedStages;
            public float checkpointPathMetres, checkpointWalkSeconds; public string[] sources;
        }
        public static string Execute(string command)
        {
            Directory.CreateDirectory(Output);
            switch (command)
            {
                case "survey": return Survey();
                case "apply": return Apply();
                case "audit": return Audit();
                default: throw new ArgumentException("Chapter-three scene authoring: survey, apply, audit. Stage enablement is separate after runtime validation.");
            }
        }
        static void RequireEdit()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || Session == null || Session.gameObject.scene.path != DemoFoundationAuthoring.Scene)
                throw new InvalidOperationException("Dedicated W_Demo_Campaign scene in Edit mode required.");
            if (AssetDatabase.GetAssetPath(Session.Content) != DemoFoundationAuthoring.Folder + "/Content.asset" || Session.Content.Campaign == null ||
                !AssetDatabase.GetAssetPath(Session.Content.Campaign).StartsWith(DemoFoundationAuthoring.Folder + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Demo content/campaign isolation lost.");
        }
        static string Save(string name, object value)
        { string json = JsonUtility.ToJson(value, true); File.WriteAllText(Path.Combine(Output, name), json); return json; }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        static Vector3 Ground(Vector3 point) => WorldMacroPlaytestAuthoring.Ground(point, true) + Vector3.up * .06f;
        static Probe Sample(Vector3 point)
        {
            var hits = Physics.RaycastAll(new Vector3(point.x, 2200, point.z), Vector3.down, 4400, 1, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits.OrderBy(h => h.distance))
                if (hit.normal.y > 0 && hit.collider.name.StartsWith("Terrain_", StringComparison.Ordinal))
                    return new Probe { point = hit.point, slope = Vector3.Angle(Vector3.up, hit.normal), supported = true };
            return new Probe { point = point, supported = false, slope = 90 };
        }
        static Candidate Examine(string id, Vector3 centre, float radius)
        {
            var samples = new List<Probe> { Sample(centre) };
            // Inner and outer rings detect cliffs and sharp changes hidden by a centre-only raycast.
            foreach (float r in new[] { radius * .5f, radius })
                for (int i = 0; i < 8; i++)
                { float a = i * Mathf.PI / 4; samples.Add(Sample(centre + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r)); }
            bool supported = samples.All(p => p.supported);
            float spread = supported ? samples.Max(p => p.point.y) - samples.Min(p => p.point.y) : float.PositiveInfinity;
            float slope = samples.Max(p => p.slope);
            return new Candidate { id = id, centre = samples[0].point + Vector3.up * .06f, samples = samples.ToArray(), maxSlope = slope,
                heightSpread = supported ? spread : -1, suitable = supported && slope <= 28f && spread <= radius * .65f };
        }
        static Vector3 ClosestRoutePoint(Vector3 point, Vector3[] route)
        {
            float best = float.PositiveInfinity; Vector3 result = point;
            for (int i = 1; i < route.Length; i++)
            {
                Vector2 a = new Vector2(route[i - 1].x, route[i - 1].z), b = new Vector2(route[i].x, route[i].z);
                Vector2 q = new Vector2(point.x, point.z), edge = b - a;
                float t = edge.sqrMagnitude > .001f ? Mathf.Clamp01(Vector2.Dot(q - a, edge) / edge.sqrMagnitude) : 0;
                Vector2 closest = a + edge * t; float distance = (closest - q).sqrMagnitude;
                if (distance < best) { best = distance; result = new Vector3(closest.x, 0, closest.y); }
            }
            return result;
        }
        static float HorizontalDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        static bool TerrainConnection(Vector3 from, Vector3 to)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(HorizontalDistance(from, to) / 4f), 1, 32);
            Probe previous = null;
            for (int i = 0; i <= steps; i++)
            {
                var probe = Sample(Vector3.Lerp(from, to, (float)i / steps));
                if (!probe.supported || probe.slope > 28f) return false;
                if (previous != null)
                {
                    float horizontal = HorizontalDistance(previous.point, probe.point);
                    if (horizontal > .01f && Mathf.Abs(previous.point.y - probe.point.y) / horizontal > Mathf.Tan(28f * Mathf.Deg2Rad)) return false;
                }
                previous = probe;
            }
            return true;
        }
        static SurveyReport SurveyData()
        {
            Physics.SyncTransforms(); var candidates = new List<Candidate>(); int centres = 0, checkpointDetails = 0, checkpointPasses = 0;
            var sheet = WorldMacroBuilder.Sheet;
            Vector3[] Route(string id)
            {
                var route = sheet.Routes.FirstOrDefault(r => r.Id == id);
                if (route == null || route.Points == null || route.Points.Length < 2)
                    throw new InvalidOperationException("Authored approach route missing: " + id);
                return route.Points;
            }
            var dragonRoute = Route("Trail_Deep_Dragon");
            var lessonRoute = Route("Trail_Logging_Deep");
            // The current authored route termini are authoritative; old planning coordinates can be stale.
            Vector3 bossOrigin = dragonRoute[dragonRoute.Length - 1], lessonOrigin = lessonRoute[lessonRoute.Length - 1];
            Vector3 arrivalForward = bossOrigin - dragonRoute[dragonRoute.Length - 2]; arrivalForward.y = 0; arrivalForward.Normalize();
            foreach (var area in new[] {
                ("boss", bossOrigin, 12f, dragonRoute),
                ("lesson", lessonOrigin, 5f, lessonRoute) })
            {
                // Bounded local search follows the authored geography, never moves an encounter across regions.
                for (int x = -7; x <= 7; x++) for (int z = -7; z <= 7; z++)
                {
                    Vector3 offset = new Vector3(x * 32, 0, z * 32); if (offset.magnitude > 240) continue;
                    Vector3 point = area.Item2 + offset; Vector3 routePoint = ClosestRoutePoint(point, area.Item4);
                    float routeDistance = HorizontalDistance(point, routePoint); if (routeDistance > 96) continue;
                    centres++; var centre = Sample(point);
                    // A cheap centre check rejects clearly impossible sites before seventeen support probes.
                    if (!centre.supported || centre.slope > 28f) continue;
                    var candidate = Examine(area.Item1 + "_" + x + "_" + z, point, area.Item3);
                    candidate.routeDistance = routeDistance; candidate.relocationMetres = offset.magnitude;
                    candidate.terrainConnection = candidate.suitable && TerrainConnection(routePoint, candidate.centre);
                    candidate.suitable &= candidate.terrainConnection;
                    candidate.score = candidate.maxSlope + Mathf.Max(0, candidate.heightSpread) * 2 + routeDistance * .2f + offset.magnitude * .045f;
                    candidates.Add(candidate);
                }
            }
            var lessons = candidates.Where(c => c.id.StartsWith("lesson_", StringComparison.Ordinal) && c.suitable).OrderBy(c => c.score).ToArray();
            var bosses = candidates.Where(c => c.id.StartsWith("boss_", StringComparison.Ordinal) && c.suitable).OrderBy(c => c.score).ToArray();
            var lesson = lessons.FirstOrDefault(); Candidate boss = null; Candidate checkpoint = null;
            // Find a supported approach/rest site near each best natural clearing. Do not force south onto a steep slope.
            foreach (var option in bosses.Take(20))
            {
                var checkpoints = new List<Candidate>();
                foreach (float distance in new[] { 28f, 34f, 40f }) for (int direction = 0; direction < 16; direction++)
                {
                    float angle = direction * Mathf.PI / 8;
                    Vector3 point = option.centre + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * distance;
                    centres++; var centre = Sample(point); if (!centre.supported || centre.slope > 28f) continue;
                    var candidate = Examine("checkpoint_" + option.id + "_" + distance + "_" + direction, point, 2.5f);
                    candidate.routeDistance = HorizontalDistance(point, ClosestRoutePoint(point, dragonRoute));
                    candidate.relocationMetres = distance;
                    candidate.terrainConnection = candidate.suitable && TerrainConnection(point, option.centre);
                    candidate.suitable &= candidate.terrainConnection;
                    // Prefer the arrival side of the clearing and existing route; the actual NavMesh route is checked after authoring.
                    candidate.score = candidate.maxSlope + candidate.heightSpread * 2 + candidate.routeDistance * .3f +
                        Mathf.Max(0, Vector3.Dot(point - option.centre, arrivalForward)) * .15f;
                    checkpoints.Add(candidate); checkpointDetails++; if (candidate.suitable) checkpointPasses++;
                }
                checkpoint = checkpoints.Where(c => c.suitable).OrderBy(c => c.score).FirstOrDefault();
                if (checkpoint != null) { boss = option; break; }
            }
            // Keep a compact ranked sample, with all search totals. Full 17-point evidence remains on selected candidates.
            var retained = new List<Candidate>();
            foreach (string prefix in new[] { "boss_", "lesson_" })
            {
                retained.AddRange(candidates.Where(c => c.id.StartsWith(prefix, StringComparison.Ordinal) && c.suitable).OrderBy(c => c.score).Take(12));
                retained.AddRange(candidates.Where(c => c.id.StartsWith(prefix, StringComparison.Ordinal) && !c.suitable).OrderBy(c => c.score).Take(4));
            }
            if (boss != null && !retained.Contains(boss)) retained.Add(boss);
            if (checkpoint != null) retained.Add(checkpoint);
            return new SurveyReport { status = boss != null && lesson != null && checkpoint != null ? "PASS" : "FAIL", scene = Session.gameObject.scene.path,
                terrainRevision = Session.Content.TerrainRevision, candidates = retained.ToArray(), boss = boss?.centre ?? Vector3.zero,
                lesson = lesson?.centre ?? Vector3.zero, checkpoint = checkpoint?.centre ?? Vector3.zero, bossSearchOrigin = bossOrigin, lessonSearchOrigin = lessonOrigin,
                centreProbes = centres, detailedCandidates = candidates.Count + checkpointDetails, passingCandidates = candidates.Count(c => c.suitable) + checkpointPasses,
                scope = "240m bounded / 32m grid search from current Trail_Deep_Dragon and Trail_Logging_Deep endpoints. Original 17-point support, 28-degree maximum and radius*0.65 height-spread limits unchanged; route connection sampled every 4m. No terrain mutation. Checkpoint navigation length, obstacle clearance and manual traversal still require later validation." };
        }
        static string Survey() { RequireEdit(); return Save("scene_survey.json", SurveyData()); }
        static void Backup()
        {
            string backup = Output + "/Backups/before-scene-authoring";
            if (!Directory.Exists(backup) && Session.gameObject.scene.isDirty)
                throw new InvalidOperationException("Save the current demo scene before the first immutable backup; unsaved scene changes must not be omitted.");
            Directory.CreateDirectory(backup); var manifest = new List<string>();
            foreach (string source in new[] { DemoFoundationAuthoring.Scene, AssetDatabase.GetAssetPath(Session.Content), AssetDatabase.GetAssetPath(Session.Content.Campaign) })
            {
                foreach (string asset in new[] { source, source + ".meta" })
                {
                    if (!File.Exists(asset)) continue;
                    string target = Path.Combine(backup, Path.GetFileName(asset));
                    if (!File.Exists(target)) File.Copy(asset, target);
                    using (var sha = SHA256.Create()) manifest.Add(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(target))).Replace("-", "").ToLowerInvariant() + "  " + Path.GetFileName(target));
                }
            }
            string record = Path.Combine(backup, "SHA256.txt"); if (!File.Exists(record)) File.WriteAllLines(record, manifest);
        }
        static Transform Child(Transform parent, string name)
        {
            var child = parent.Find(name); if (child != null) return child;
            child = new GameObject(name).transform; child.SetParent(parent, false); return child;
        }
        static void Set(Object target, string name, Object value)
        {
            var so = new SerializedObject(target); var field = so.FindProperty(name);
            if (field == null) throw new InvalidOperationException("Missing serialized binding: " + name);
            field.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
        }
        static T RequireAsset<T>(string path) where T : Object
        { var asset = AssetDatabase.LoadAssetAtPath<T>(path); if (asset == null) throw new InvalidOperationException("Required owned source missing: " + path); return asset; }
        static T GetOrAdd<T>(GameObject go) where T : Component => go.TryGetComponent<T>(out var component) ? component : go.AddComponent<T>();
        static CombatConfigSO Config(string name, CombatConfigSO source, float hp)
        {
            string path = Folder + "/" + name + ".asset"; var config = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(path);
            if (config == null) { config = Object.Instantiate(source); config.name = name; var tuning = new SerializedObject(config); tuning.FindProperty("_enemyMaxHp").floatValue = hp; tuning.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.CreateAsset(config, path); }
            return config;
        }
        static EnemyAttackProfileSO WoodProfile()
        {
            string path = Folder + "/GrowthLesson_WoodVine.asset"; var p = AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(path);
            if (p == null) { p = ScriptableObject.CreateInstance<EnemyAttackProfileSO>(); p.ApplyDefaults(EnemyArchetype.WoodVine); AssetDatabase.CreateAsset(p, path); }
            return p;
        }
        static void BuildNavigation(Transform parent, string id, Vector3 centre, Vector3 size)
        {
            var node = Child(parent, "Navigation_" + id); node.position = centre;
            var surface = GetOrAdd<NavMeshSurface>(node.gameObject);
            surface.collectObjects = CollectObjects.Volume; surface.center = Vector3.zero; surface.size = size;
            surface.layerMask = 1; surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.overrideVoxelSize = true; surface.voxelSize = .2f; surface.overrideTileSize = true; surface.tileSize = 128;
            string path = Folder + "/Navigation_" + id + ".asset"; var data = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            surface.RemoveData(); surface.navMeshData = null; Physics.SyncTransforms(); surface.BuildNavMesh();
            var generated = surface.navMeshData; if (generated == null) throw new InvalidOperationException("Local NavMesh failed: " + id);
            surface.RemoveData();
            if (data == null) { data = Object.Instantiate(generated); AssetDatabase.CreateAsset(data, path); }
            else { EditorUtility.CopySerialized(generated, data); EditorUtility.SetDirty(data); }
            if (!AssetDatabase.Contains(generated)) Object.DestroyImmediate(generated);
            surface.navMeshData = data; surface.AddData(); EditorUtility.SetDirty(surface);
        }
        static PrologueEncounter Actor(Transform parent, string id, PrologueEncounter seed, Vector3 guess, CombatConfigSO config)
        {
            if (!NavMesh.SamplePosition(guess, out var hit, 3f, NavMesh.AllAreas)) throw new InvalidOperationException("No local NavMesh spawn: " + id);
            var existing = parent.Find(id); var actor = existing != null ? existing.GetComponent<PrologueEncounter>() : Object.Instantiate(seed, parent);
            actor.name = id; actor.Id = id; actor.Session = null; actor.Player = Session.Walker.Body.transform;
            var instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(actor.gameObject);
            if (instanceRoot == actor.gameObject) PrefabUtility.UnpackPrefabInstance(actor.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            // Delete our previous whole imported model before component stripping; imported model components are immutable.
            var ownedVisual = actor.transform.Find("Chapter3_Visual"); if (ownedVisual != null) Object.DestroyImmediate(ownedVisual.gameObject);
            foreach (var node in actor.GetComponentsInChildren<Transform>(true))
            {
                if (node == null || node == actor.transform) continue;
                var nested = PrefabUtility.GetOutermostPrefabInstanceRoot(node.gameObject);
                if (nested == node.gameObject && nested.transform.IsChildOf(actor.transform))
                    PrefabUtility.UnpackPrefabInstance(nested, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
            var agent = actor.GetComponent<NavMeshAgent>(); agent.enabled = false; agent.baseOffset = .875f;
            actor.transform.SetPositionAndRotation(hit.position + Vector3.up * agent.baseOffset, Quaternion.identity); actor.transform.localScale = Vector3.one;
            actor.PatrolPoints = new[] { actor.transform.position }; actor.Leash = 26; actor.DetectionRange = 18;
            Set(actor.GetComponent<EnemyVitals>(), "_config", config); Set(actor.GetComponent<EnemyController>(), "_config", config);
            Set(actor.GetComponent<EnemyController>(), "_renderer", null);
            // Strip only presentation/colliders on this isolated clone. Gameplay components stay on its root.
            foreach (var fx in actor.GetComponentsInChildren<EnemyAttackPresentation>(true)) Object.DestroyImmediate(fx);
            foreach (var animator in actor.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
            foreach (var animation in actor.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(animation);
            foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(renderer);
            foreach (var filter in actor.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(filter);
            foreach (var collider in actor.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);

            var growth = GetOrAdd<CheongryongGrowthController>(actor.gameObject); Set(growth, "_vitals", actor.GetComponent<EnemyVitals>());
            return actor;
        }
        static Transform Visual(PrologueEncounter actor, GameObject prefab, float targetHeight = 0)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, actor.transform); model.name = "Chapter3_Visual";
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity; model.transform.localScale = Vector3.one;
            foreach (var animator in model.GetComponentsInChildren<Animator>(true)) { animator.applyRootMotion = false; animator.enabled = false; }
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Source has no renderable mesh.");
            var bounds = renderers[0].bounds; foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            if (targetHeight > 0)
            {
                model.transform.localScale *= targetHeight / Mathf.Max(.01f, bounds.size.y);
                bounds = renderers[0].bounds; foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            }
            model.transform.position += Vector3.up * (actor.transform.position.y - .875f - bounds.min.y);
            return model.transform;
        }
        static Transform Bone(Transform model, string name) => model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)
            ?? throw new InvalidOperationException("Rig bone missing: " + name);
        static void HitCapsule(Transform bone, string name, float height, float radius)
        {
            var node = Child(bone, name); node.localPosition = Vector3.zero; node.localRotation = Quaternion.identity;
            var scale=bone.lossyScale;
            node.localScale=new Vector3(1/Mathf.Abs(scale.x),1/Mathf.Abs(scale.y),1/Mathf.Abs(scale.z));
            var capsule = GetOrAdd<CapsuleCollider>(node.gameObject); capsule.height = height; capsule.radius = radius; capsule.direction = 1;
        }
        static string Apply()
        {
            RequireEdit(); var source = RequireAsset<GameObject>(BossSource); var tree = RequireAsset<GameObject>(TreeSource);
            var sourceHead = Bone(source.transform, "Head"); Bone(source.transform, "MouthOrigin"); var sourceTail = Bone(source.transform, "TailTip");
            Transform previous = sourceHead;
            for (int i = 1; i <= 24; i++)
            {
                var segment = Bone(source.transform, "Body_" + i.ToString("00"));
                if (segment.parent != previous) throw new InvalidOperationException("Imported body chain parent mismatch before scene authoring.");
                previous = segment;
            }
            if (!sourceTail.IsChildOf(previous)) throw new InvalidOperationException("Imported tail hierarchy mismatch before scene authoring.");
            RequireAsset<GameObject>(WarningSource); RequireAsset<GameObject>(RootSource); RequireAsset<GameObject>(ContactSource); RequireAsset<Mesh>(BoltMeshSource); RequireAsset<Material>(BoltMaterialSource);
            string surveyPath = Path.Combine(Output, "scene_survey.json");
            if (!File.Exists(surveyPath)) throw new InvalidOperationException("Run and review scene survey before apply.");
            var prior = JsonUtility.FromJson<SurveyReport>(File.ReadAllText(surveyPath)); var survey = SurveyData();
            if (prior.status != "PASS" || survey.status != "PASS" || prior.scene != survey.scene || prior.terrainRevision != survey.terrainRevision ||
                Vector3.Distance(prior.boss, survey.boss) > .1f || Vector3.Distance(prior.lesson, survey.lesson) > .1f || Vector3.Distance(prior.checkpoint, survey.checkpoint) > .1f)
                throw new InvalidOperationException("Survey failed or terrain candidates changed; inspect a fresh survey first. No terrain flattening performed.");
            var seed = Session.Actors.FirstOrDefault(a => a != null && !Owned(a.Id)) ?? throw new InvalidOperationException("Existing enemy seed required.");
            var seedSO = new SerializedObject(seed.GetComponent<EnemyVitals>());
            var baseConfig = seedSO.FindProperty("_config").objectReferenceValue as CombatConfigSO ?? throw new InvalidOperationException("Enemy config source required.");
            var tableSource = GameObject.Find("Playtest_Village_Office")?.transform.Find("Clerk_WorkTable") ??
                GameObject.Find("Playtest_OwnedAssets")?.transform.Find("Geumpyo_ThatchedInn/Rest_Low_Table") ?? throw new InvalidOperationException("Owned rest-table source required.");
            Backup(); EnsureFolder(Folder);
            var root = GameObject.Find(RootName)?.transform ?? new GameObject(RootName).transform;
            var actorsRoot = Child(root, "Encounters");
            // Exclude existing owned actors from re-bakes without deactivating terrain or other encounters.
            bool oldActive = actorsRoot.gameObject.activeSelf; actorsRoot.gameObject.SetActive(false);
            try { BuildNavigation(root, "boss", survey.boss, new Vector3(128, 100, 128)); BuildNavigation(root, "growth_lesson", survey.lesson, new Vector3(56, 64, 56)); }
            finally { actorsRoot.gameObject.SetActive(oldActive); }
            var boss = Actor(actorsRoot, BossId, seed, survey.boss, Config("Cheongryong_CombatConfig_TEST", baseConfig, 400));
            boss.Speed = 1.7f; boss.PreferredDistance = 7; boss.Ranged = true; boss.GetComponent<NavMeshAgent>().radius = 1.05f;
            var model = Visual(boss, source); var head = Bone(model, "Head"); var mouth = Bone(model, "MouthOrigin"); var tail = Bone(model, "TailTip");
            var ordered = Enumerable.Range(1, 24).Select(i => Bone(model, "Body_" + i.ToString("00"))).ToArray();
            var body = GetOrAdd<CheongryongBodyFollow>(boss.gameObject);
            if (!body.Configure(head, ordered, tail)) throw new InvalidOperationException("Body-follow bind pose rejected.");
            HitCapsule(head, "Damage_Head", 1.1f, .65f);
            for (int i = 0; i < ordered.Length; i += 3) HitCapsule(ordered[i], "Damage_Body", .9f, .55f);
            var profile = AssetDatabase.LoadAssetAtPath<CheongryongCombatProfile>(Folder + "/Cheongryong_Attacks_TEST.asset");
            if (profile == null) { profile = ScriptableObject.CreateInstance<CheongryongCombatProfile>(); profile.EnvironmentMask = 1; AssetDatabase.CreateAsset(profile, Folder + "/Cheongryong_Attacks_TEST.asset"); }
            var controller = GetOrAdd<CheongryongCombatController>(boss.gameObject); controller.ConfigureProfile(profile,
                AssetDatabase.LoadAssetAtPath<CombatConfigSO>(Folder + "/Cheongryong_CombatConfig_TEST.asset")); controller.ConfigureSockets(mouth, tail); controller.AttackEnabled = false;
            GetOrAdd<CheongryongAttackPresentation>(boss.gameObject).Configure(controller, RequireAsset<GameObject>(WarningSource),
                RequireAsset<GameObject>(RootSource), RequireAsset<Mesh>(BoltMeshSource), RequireAsset<Material>(BoltMaterialSource), RequireAsset<GameObject>(ContactSource));
            boss.GetComponent<EnemyController>().Configure(WoodProfile()); boss.GetComponent<EnemyController>().AttackEnabled = false; boss.GetComponent<EnemyController>().enabled = false;
            var lesson = Actor(actorsRoot, LessonActorId, seed, survey.lesson, Config("GrowthLesson_CombatConfig_TEST", baseConfig, 180));
            lesson.Speed = .05f; lesson.PreferredDistance = 17; lesson.Ranged = true; Visual(lesson, tree, 3f);
            var lessonCollider = GetOrAdd<CapsuleCollider>(lesson.gameObject); lessonCollider.height = 2.8f; lessonCollider.radius = .65f; lessonCollider.center = Vector3.up * .525f;
            var lessonController = lesson.GetComponent<EnemyController>(); lessonController.enabled = true; lessonController.Configure(WoodProfile()); lessonController.AttackEnabled = false;
            var fx = GetOrAdd<EnemyAttackPresentation>(lesson.gameObject); fx.Configure(lessonController, RequireAsset<GameObject>(WarningSource), RequireAsset<GameObject>(RootSource), RequireAsset<GameObject>(ContactSource));
            GetOrAdd<DemoGrowthLessonLink>(lesson.gameObject).Session = Session;
            var content = Session.Content; var allActors = Session.Actors.Where(a => a != null && !Owned(a.Id)).Concat(new[] { lesson, boss }).ToArray(); Session.Actors = allActors;
            content.Encounters = content.Encounters.Where(e => !Owned(e.Id)).Concat(new[] { Spec(lesson, DemoGrowthLessonLink.LessonId, true), Spec(boss, BossId, false) }).ToArray();
            var table = Child(root, "RestPoint"); table.SetPositionAndRotation(survey.checkpoint, Quaternion.identity);
            if (table.childCount == 0) { var copy = Object.Instantiate(tableSource, table); copy.name = "Owned_RestTable"; copy.localPosition = Vector3.zero; copy.localRotation = Quaternion.identity; }
            content.Points = content.Points.Where(p => p.Id != CheckpointId && p.Id != DemoGrowthLessonLink.LessonId).Concat(new[] {
                new PrologueContentSO.Point { Id = CheckpointId, Kind = PrologueInteractionKind.Rest, Position = survey.checkpoint, Radius = 3, Prompt = "청룡 숲길 쉼터에서 휴식", Text = "짙은 숲을 지나기 전 잠시 숨을 고른다." },
                new PrologueContentSO.Point { Id = DemoGrowthLessonLink.LessonId, Kind = PrologueInteractionKind.Evidence, Position = survey.lesson, Radius = 4, Prompt = "재생하는 덩굴 살피기", Text = "덩굴이 다시 자라려는 순간 금 속성 술식으로 끊어 본다." }
            }).ToArray();
            content.Checkpoints = (content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>()).Where(c => c.Id != CheckpointId)
                .Concat(new[] { new WorldMacroPlaytestSO.CheckpointSpec { Id = CheckpointId, Label = "청룡 숲길 쉼터", Feet = Ground(survey.checkpoint + Vector3.back * 2), Yaw = 0, Shop = false } }).ToArray();
            var wiring = new SerializedObject(Session.Walker.Wiring); var enemies = wiring.FindProperty("_enemies"); enemies.arraySize = allActors.Length;
            for (int i = 0; i < allActors.Length; i++) { enemies.GetArrayElementAtIndex(i).objectReferenceValue = allActors[i].GetComponent<EnemyVitals>(); allActors[i].GetComponent<NavMeshAgent>().enabled = false; }
            wiring.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(content); EditorUtility.SetDirty(Session); AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(Session.gameObject.scene); EditorSceneManager.SaveScene(Session.gameObject.scene);
            // Deliberately do not mark deep_forest/cheongryong stages implemented here.
            return Audit();
        }
        static WorldMacroPlaytestSO.Encounter Spec(PrologueEncounter actor, string contentId, bool respawn) => new WorldMacroPlaytestSO.Encounter
        { Id = actor.Id, ContentId = contentId, Feet = actor.transform.position - Vector3.up * .875f, Patrol = new[] { actor.transform.position - Vector3.up * .875f },
            Ranged = actor.Ranged, RespawnOnRest = respawn, Speed = actor.Speed, Detection = actor.DetectionRange, Leash = actor.Leash, Activation = 140 };
        static string Audit()
        {
            RequireEdit(); Physics.SyncTransforms(); var checks = new List<Check>();
            void C(bool pass, string name, string detail = "") => checks.Add(new Check { name = name, status = pass ? "PASS" : "FAIL", detail = detail });
            var s = Session; var owned = s.Actors.Where(a => a != null && Owned(a.Id)).ToArray();
            C(owned.Length == 2, "two authored actors"); C(s.Actors.Where(a => a != null).Select(a => a.Id).Distinct().Count() == s.Actors.Length, "unique actor IDs");
            C(s.Content.Encounters.Count(e => Owned(e.Id)) == 2, "two matching encounter specs");
            foreach (var actor in owned)
            {
                C(!actor.GetComponent<NavMeshAgent>().enabled, "agent disabled in saved edit state " + actor.Id);
                C(NavMesh.SamplePosition(actor.transform.position - Vector3.up * .875f, out _, 2, NavMesh.AllAreas), "navigation support " + actor.Id);
                C(actor.GetComponent<CheongryongGrowthController>() != null, "growth component " + actor.Id);
                C(actor.GetComponentsInChildren<Renderer>(true).Any(), "owned mesh visual " + actor.Id);
            }
            var boss = owned.FirstOrDefault(a => a.Id == BossId); var lesson = owned.FirstOrDefault(a => a.Id == LessonActorId);
            C(boss != null && boss.GetComponent<CheongryongCombatController>() != null && !boss.GetComponent<EnemyController>().enabled, "single boss attack controller");
            C(boss != null && boss.GetComponent<CheongryongBodyFollow>() != null, "body follow authored");
            C(lesson != null && lesson.GetComponent<DemoGrowthLessonLink>()?.Session == s, "lesson proof session binding");
            C(s.Content.Encounters.Any(e => e.Id == BossId && !e.RespawnOnRest), "boss defeat persists");
            var checkpoint = s.Content.Checkpoints.FirstOrDefault(c => c.Id == CheckpointId);
            float length = -1;
            if (boss != null && checkpoint != null && NavMesh.SamplePosition(checkpoint.Feet, out var start, 3, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(boss.transform.position - Vector3.up * .875f, out var end, 3, NavMesh.AllAreas))
            {
                var path = new NavMeshPath(); if (NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                { length = 0; for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]); }
            }
            C(length >= 0 && length / 2.2f <= 30f, "checkpoint to boss path <=30 seconds at walk 2.2m/s", length.ToString("F2") + "m; calculated, not timed player traversal");
            checks.Add(new Check { name = "actual input, attacks, rig animation, terrain collision, checkpoint safety, performance and visual approval", status = "UNVERIFIED", detail = "Dedicated runtime validation required before enabling campaign stages." });
            return Save("scene_audit.json", new Report { status = checks.Any(c => c.status == "FAIL") ? "FAIL" : "PASS_AUTHORING_ONLY", checks = checks.ToArray(),
                scope = "Two temporary authored actors and local navigation. Campaign implementation flags preserved; no world rebuild or runtime completion claim.", actors = s.Actors.Length,
                ownedActors = owned.Length, implementedStages = s.Content.Campaign.Stages.Count(stage => stage.Implemented), checkpointPathMetres = length,
                checkpointWalkSeconds = length < 0 ? -1 : length / 2.2f, sources = new[] { BossSource, TreeSource, WarningSource, RootSource, ContactSource, BoltMeshSource, BoltMaterialSource } });
        }
    }
}





