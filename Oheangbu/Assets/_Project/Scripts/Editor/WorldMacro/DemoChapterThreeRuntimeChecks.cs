using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Continues the completed Chapter2 UUID Play session; never enters Play or modifies authored assets.</summary>
    [InitializeOnLoad]
    public static class DemoChapterThreeRuntimeChecks
    {
        const string StateKey = "DemoChapterThreeRuntimeChecks.State";
        const string SuffixKey = "PlaytestUiReviewSuffix";
        static readonly string[] PreviousStages = { "commission", "mine_evidence", "office_report", "inn_rest", "relay", "cargo_contract", "logging" };
        [Serializable] sealed class Check { public string name, status, detail; }
        [Serializable] sealed class ProtectedFile { public string path, hash; }
        [Serializable] sealed class Run
        {
            public string status, phase, suffix, savePath, sourceContentPath, sourceCampaignPath, contentMemoryHash, campaignMemoryHash;
            public string scope = "Actual Play API integration continuing a completed Chapter2 UUID diagnostic. Real EnemyVitals damage/provenance, growth time, PrologueEncounter death, central resolved-letter Guk cast with real ink charge, live FieldSpellService lift, short collision-resolved ledge transfer, Session transactions and independent file reload. Safe teleport only reaches the ground lift pad; it never supplies field-use proof. No native keyboard/mouse combat/drawing, automatic route walkthrough, app restart, visual approval, or full demo completion.";
            public bool active, holdingPlay, restored, originalCombat;
            public int stage, startCurrency, lessonCurrency, bossCurrency, bossDeaths, originalContentId, contentCloneId, campaignCloneId;
            public double deadline, scaledReadyAt; public float bossPostGrowthHp;
            public int gukAccepted, gukCurrency; public float gukInkBefore, gukInkCost, gukStartY, gukMaximumHeight;
            public double gukStartedAt, gukMoveLastAt, gukMoveDeadline;
            public Vector3 gukPad, gukSurface, gukFinalFeet;
            public List<Check> checks = new List<Check>(); public List<string> failures = new List<string>();
            public List<ProtectedFile> protectedFiles = new List<ProtectedFile>();
            public string[] unverified = { "Native keyboard/mouse combat", "Full traversal and encounter balance", "Performance", "Visual quality and rig deformation", "Application restart and re-entry", "仁 second lethal hit and checkpoint recharge in this run" };
        }
        static Run run;
        static WorldMacroPlaytestSession session;
        static WorldMacroPlaytestSO originalContent, contentClone;
        static DemoCampaignProfile campaignClone;
        static PlaytestUiRoot ui;
        static EnemyVitals observedBoss;
        static DemoGukRevisitSite gukSite;
        static Oheangbu.App.CombatLoopWiring observedWiring;
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Demo/Chapter3/runtime_tests.json"));
        static DemoChapterThreeRuntimeChecks()
        {
            string pending = SessionState.GetString(StateKey, "");
            if (!string.IsNullOrEmpty(pending)) run = JsonUtility.FromJson<Run>(pending);
            EditorApplication.playModeStateChanged += PlayChanged;
            EditorApplication.quitting += EditorQuitting;
            // Domain reload interrupts a diagnostic; recover owned references, restore data, never resume half an assertion sequence.
            if (run != null && !run.restored && (run.active || run.holdingPlay)) EditorApplication.delayCall += InterruptedReload;
        }
        public static string Execute(string command)
        {
            if(command=="input-status")
            {
                var currentUi=PlaytestUiRoot.Instance;var gate=currentUi?.Gate;
                return $"focused={Application.isFocused}; editorPaused={EditorApplication.isPaused}; timeScale={Time.timeScale}; frame={Time.frameCount}; page={currentUi?.Page}; busy={currentUi?.Busy}; pauseDepth={currentUi?.Pause?.Depth}; blocked={gate?.InputBlocked}; pending={gate?.ReleasePending}; focusOwner={gate?.FocusOwnsBlock}; neutralFrames={gate?.NeutralFrames}";
            }
            if (command == "inspect") return InspectApproach();
            if (command == "poll") return run != null ? JsonUtility.ToJson(run, true) : File.Exists(Output) ? File.ReadAllText(Output) : "NOT_RUN";
            if (command == "stop")
            {
                if (run == null || run.restored) return "NOT_RUNNING";
                if (run.active) { run.status = "ABORTED"; run.failures.Add("Stopped before all API checks finished."); }
                Restore(); return "Chapter3 runtime clones and combat flag restored; Chapter2 still owns held Play and UUID suffix. Use chapter2:runtime:stop to exit Play.";
            }
            if (command != "begin") throw new ArgumentException("Use begin, poll, inspect or stop. Inspect is a read-only approach probe; begin continues an already completed Chapter2 held-Play diagnostic.");
            if (run != null && !run.restored && (run.active || run.holdingPlay)) throw new InvalidOperationException("Stop the previous Chapter3 diagnostic first.");
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPaused)
                throw new InvalidOperationException("Begin requires active, unpaused Play after Chapter2 API checks have completed.");
            session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>(); ui = PlaytestUiRoot.Instance;
            if (session == null || session.gameObject.scene.path != DemoFoundationAuthoring.Scene || session.Progress == null || ui == null ||
                !session.TestSaveSuffix.StartsWith("_chapter2_", StringComparison.Ordinal) ||
                !Guid.TryParseExact(session.TestSaveSuffix.Substring("_chapter2_".Length), "N", out _))
                throw new InvalidOperationException("A real Chapter2 UUID session is required; normal saves are forbidden.");
            string chapter2 = DemoChapterTwoRuntimeChecks.Execute("poll");
            if (!chapter2.Contains("PASS_API_INTEGRATION") || !chapter2.Contains(session.TestSaveSuffix) ||
                session.Progress.campaign.Completed.Count != PreviousStages.Length || !PreviousStages.All(session.Progress.campaign.Completed.Contains))
                throw new InvalidOperationException("Chapter2 must have actually completed its seven stages. No progress injection is allowed.");
            originalContent = session.Content;
            string contentPath = AssetDatabase.GetAssetPath(originalContent), campaignPath = AssetDatabase.GetAssetPath(originalContent.Campaign);
            if (contentPath != DemoFoundationAuthoring.Folder + "/Content.asset" || !campaignPath.StartsWith(DemoFoundationAuthoring.Folder + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Expected original isolated demo assets; another runtime content override must be restored first.");
            foreach (string id in new[] { DemoChapterThreeSceneAuthoring.LessonActorId, WorldMacroPlaytestSession.CheongryongId })
                if (!session.Actors.Any(a => a != null && a.Id == id)) throw new InvalidOperationException("Author Chapter3 actor before testing: " + id);
            gukSite = Object.FindObjectsByType<DemoGukRevisitSite>(FindObjectsSortMode.None).SingleOrDefault(s => s.gameObject.scene == session.gameObject.scene);
            if (gukSite == null || gukSite.LiftPad == null || gukSite.UpperSurface == null || session.DemoField == null ||
                originalContent.Points.Count(p => p.Id == DemoGukRevisitSite.Id) != 1)
                throw new InvalidOperationException("Author the real Guk revisit site, surface, content point and bound FieldSpellService before running extended Chapter3 checks.");
            string path = Path.Combine(Application.persistentDataPath, originalContent.SaveSlot + session.TestSaveSuffix + ".json");
            if (!File.Exists(path)) throw new InvalidOperationException("Chapter2 UUID primary save must already exist.");
            run = new Run { active = true, status = "RUNNING", phase = "close Chapter2 shop and configure runtime-only stages", suffix = session.TestSaveSuffix,
                savePath = path, sourceContentPath = contentPath, sourceCampaignPath = campaignPath, originalContentId = originalContent.GetInstanceID(),
                originalCombat = session.CombatActive, startCurrency = session.Progress.ledger.currency, deadline = EditorApplication.timeSinceStartup + 60,
                contentMemoryHash = HashText(JsonUtility.ToJson(originalContent)), campaignMemoryHash = HashText(JsonUtility.ToJson(originalContent.Campaign)) };
            foreach (string file in Directory.GetFiles(Application.persistentDataPath, "*.json*", SearchOption.TopDirectoryOnly)
                .Where(p => !p.StartsWith(path, StringComparison.OrdinalIgnoreCase)).Concat(new[] { contentPath, campaignPath }))
                run.protectedFiles.Add(new ProtectedFile { path = file, hash = Hash(file) });
            try
            {
                CheckIsolation();
                campaignClone = Object.Instantiate(originalContent.Campaign); campaignClone.name = "Chapter3_RuntimeCampaign_" + run.suffix; campaignClone.hideFlags = HideFlags.DontSave;
                contentClone = Object.Instantiate(originalContent); contentClone.name = "Chapter3_RuntimeContent_" + run.suffix; contentClone.hideFlags = HideFlags.DontSave;
                contentClone.Campaign = campaignClone; run.contentCloneId = contentClone.GetInstanceID(); run.campaignCloneId = campaignClone.GetInstanceID();
                Stage("deep_forest", DemoEventKind.GrowthInterrupted, DemoGrowthLessonLink.LessonId, 40);
                Stage("cheongryong", DemoEventKind.BossDefeated, WorldMacroPlaytestSession.CheongryongId, 160);
                CheckThat(campaignClone.IsValid, "runtime campaign schema", "Only two cloned stages enabled; original assets are untouched.");
                session.Content = contentClone; ui.CloseMenu(); session.CombatActive = false; session.Cull();
                EditorApplication.update -= Tick; EditorApplication.update += Tick; Persist();
                return "RUNNING: <=60-second actual Play API diagnostic, original Chapter2 UUID only; runtime Content/Campaign clones. poll for results; stop restores clones without ending Play.";
            }
            catch (Exception exception) { Fail(exception); Restore(); throw; }
        }
        [Serializable] sealed class ColliderProbe
        {
            public string path, type, layerName; public int layer; public bool enabled, active, trigger, playerOwned;
            public Vector3 position, lossyScale, boundsCenter, boundsSize; public float localRadius, localHeight;
        }
        [Serializable] sealed class RendererProbe
        { public string path; public bool enabled, active; public Vector3 boundsCenter, boundsSize; }
        [Serializable] sealed class SupportProbe
        { public Vector3 origin, hitPoint, normal; public bool hit; public string colliderPath; }
        [Serializable] sealed class SafeHitProbe
        {
            public string colliderPath, rejection; public Vector3 point, normal, capsuleBottom, capsuleTop;
            public float distance; public bool playerOwned, slopeAccepted, supportAccepted;
            public List<ColliderProbe> overlaps = new List<ColliderProbe>();
            public List<SupportProbe> supports = new List<SupportProbe>();
        }
        [Serializable] sealed class ApproachProbe
        {
            public int index; public float radius, candidateY; public string groundException, groundCollider, outcome;
            public Vector3 original, candidate, groundPoint, groundNormal, resolvedFeet;
            public bool grounded, withinBounds, actualTrySafeFeet;
            public List<SafeHitProbe> safetyHits = new List<SafeHitProbe>();
        }
        [Serializable] sealed class ApproachReport
        {
            public string status = "READ_ONLY_PROBE", utc, scene, suffix, growthState, outputPath;
            public string scope = "Read-only physics queries reproducing all 48 approach candidates in the currently loaded Edit or Play scene. Edit-state HP/growth/graph fields are not initialized gameplay evidence. No teleport, Cull, activation, transform synchronization, save, campaign or runtime-test state change. Support rays are also inspected after overlap failure to expose independent faults.";
            public bool playing, combatActive, bossActive, actorEnabled, vitalsEnabled, bossAlive, controllerEnabled, legacyEnabled, bodyConfigured, graphConfigured, hasGraph;
            public bool navPresent, navEnabled, navOnMesh, navSampleFound;
            public float bossHp, playerSlopeLimit, navBaseOffset; public int safeCount, groundFailures, nearbyTerrainTotal;
            public Vector2 worldMin, worldMax; public Vector3 playerFeet, bossPosition, navPosition, navNextPosition, navSamplePosition;
            public List<ColliderProbe> bossColliders = new List<ColliderProbe>();
            public List<RendererProbe> bossRenderers = new List<RendererProbe>();
            public List<ColliderProbe> nearbyTerrain = new List<ColliderProbe>();
            public List<ApproachProbe> candidates = new List<ApproachProbe>();
        }
        static string TransformPath(Transform value)
        {
            if (value == null) return "";
            string path = value.name; while (value.parent != null) { value = value.parent; path = value.name + "/" + path; }
            return path;
        }
        static ColliderProbe DescribeCollider(Collider value, Transform player)
        {
            var result = new ColliderProbe { path = TransformPath(value.transform), type = value.GetType().Name,
                layer = value.gameObject.layer, layerName = LayerMask.LayerToName(value.gameObject.layer), enabled = value.enabled,
                active = value.gameObject.activeInHierarchy, trigger = value.isTrigger,
                playerOwned = player != null && value.transform.IsChildOf(player), position = value.transform.position,
                lossyScale = value.transform.lossyScale, boundsCenter = value.bounds.center, boundsSize = value.bounds.size };
            if (value is CapsuleCollider capsule) { result.localRadius = capsule.radius; result.localHeight = capsule.height; }
            else if (value is CharacterController character) { result.localRadius = character.radius; result.localHeight = character.height; }
            else if (value is SphereCollider sphere) result.localRadius = sphere.radius;
            return result;
        }
        static string InspectApproach()
        {
            // Use local references: inspecting must not change the harness's held session or progress.
            var targetSession = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (targetSession == null || targetSession.Walker == null || targetSession.Walker.Body == null)
                throw new InvalidOperationException("Inspect requires a loaded authored session and its serialized real Walker/Body; no Play entry or object creation is performed.");
            var actor = targetSession.Actors?.SingleOrDefault(a => a != null && a.Id == WorldMacroPlaytestSession.CheongryongId);
            if (actor == null) throw new InvalidOperationException("No actual cheongryong actor in the current session.");
            var player = targetSession.Walker.Body.transform;
            var vitals = actor.GetComponent<EnemyVitals>(); var growth = actor.GetComponent<CheongryongGrowthController>();
            var controller = actor.GetComponent<CheongryongCombatController>(); var legacy = actor.GetComponent<EnemyController>();
            var follow = actor.GetComponent<CheongryongBodyFollow>(); var graph = actor.GetComponent<CheongryongRigAnimation>();
            var nav = actor.GetComponent<UnityEngine.AI.NavMeshAgent>();
            var report = new ApproachReport { utc = DateTime.UtcNow.ToString("O"), playing = EditorApplication.isPlaying,
                scene = targetSession.gameObject.scene.path, suffix = targetSession.TestSaveSuffix, combatActive = targetSession.CombatActive,
                bossPosition = actor.transform.position, playerFeet = player.position, playerSlopeLimit = targetSession.Walker.Body.slopeLimit,
                worldMin = targetSession.PreviewSheetBoundsMin, worldMax = targetSession.PreviewSheetBoundsMax,
                bossActive = actor.gameObject.activeInHierarchy, actorEnabled = actor.enabled, vitalsEnabled = vitals != null && vitals.enabled,
                bossAlive = vitals != null && vitals.IsAlive, bossHp = vitals != null ? vitals.Hp : -1,
                growthState = growth != null ? growth.State.ToString() : "missing", controllerEnabled = controller != null && controller.enabled,
                legacyEnabled = legacy != null && legacy.enabled, bodyConfigured = follow != null && follow.IsConfigured,
                graphConfigured = graph != null && graph.IsConfigured, hasGraph = graph != null && graph.HasGraph,
                navPresent = nav != null, navEnabled = nav != null && nav.enabled };
            if (nav != null)
            {
                report.navPosition = nav.transform.position; report.navBaseOffset = nav.baseOffset;
                report.navOnMesh = EditorApplication.isPlaying && nav.isActiveAndEnabled && nav.isOnNavMesh;
                if (report.navOnMesh) report.navNextPosition = nav.nextPosition;
            }
            report.navSampleFound = UnityEngine.AI.NavMesh.SamplePosition(actor.transform.position, out var navHit, 6, UnityEngine.AI.NavMesh.AllAreas);
            if (report.navSampleFound) report.navSamplePosition = navHit.position;
            foreach (var collider in actor.GetComponentsInChildren<Collider>(true)) report.bossColliders.Add(DescribeCollider(collider, player));
            foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true)) report.bossRenderers.Add(new RendererProbe
                { path = TransformPath(renderer.transform), enabled = renderer.enabled, active = renderer.gameObject.activeInHierarchy,
                    boundsCenter = renderer.bounds.center, boundsSize = renderer.bounds.size });
            foreach (var collider in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!collider.name.StartsWith("Terrain_", StringComparison.Ordinal)) continue;
                Bounds bounds = collider.bounds;
                // Disabled collider bounds are empty; the shared mesh still reveals the authored coverage.
                if (collider is MeshCollider mesh && mesh.sharedMesh != null && bounds.size == Vector3.zero)
                {
                    Bounds local = mesh.sharedMesh.bounds;
                    bounds = new Bounds(mesh.transform.TransformPoint(local.center), Vector3.zero);
                    for (int corner = 0; corner < 8; corner++) bounds.Encapsulate(mesh.transform.TransformPoint(local.center +
                        Vector3.Scale(local.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
                }
                if (bounds.max.x < report.bossPosition.x - 80 || bounds.min.x > report.bossPosition.x + 80 ||
                    bounds.max.z < report.bossPosition.z - 80 || bounds.min.z > report.bossPosition.z + 80) continue;
                report.nearbyTerrainTotal++;
                var row = DescribeCollider(collider, player); row.boundsCenter = bounds.center; row.boundsSize = bounds.size;
                report.nearbyTerrain.Add(row);
            }
            foreach (float radius in new[] { 12f, 9f, 16f }) for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI / 8;
                var row = new ApproachProbe { index = report.candidates.Count, radius = radius,
                    original = actor.transform.position + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius };
                report.candidates.Add(row);
                try
                {
                    row.groundPoint = WorldMacroPlaytestAuthoring.Ground(row.original, true);
                    row.grounded = true; row.candidate = row.groundPoint + Vector3.up * .06f; row.candidateY = row.candidate.y;
                    foreach (var h in Physics.RaycastAll(new Vector3(row.original.x, 2200, row.original.z), Vector3.down, 4400, 1, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                        if (h.normal.y > 0 && h.collider.name.StartsWith("Terrain_", StringComparison.Ordinal))
                        { row.groundNormal = h.normal; row.groundCollider = TransformPath(h.transform); break; }
                }
                catch (Exception exception) { row.groundException = exception.ToString(); row.outcome = "Ground exception"; report.groundFailures++; continue; }
                row.withinBounds = row.candidate.x >= report.worldMin.x && row.candidate.x <= report.worldMax.x && row.candidate.z >= report.worldMin.y && row.candidate.z <= report.worldMax.y;
                row.actualTrySafeFeet = targetSession.TrySafeFeet(row.candidate, out row.resolvedFeet);
                if (row.actualTrySafeFeet) report.safeCount++;
                foreach (var hit in Physics.RaycastAll(row.candidate + Vector3.up * 1.5f, Vector3.down, 4, 1, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                {
                    var detail = new SafeHitProbe { colliderPath = TransformPath(hit.transform), point = hit.point, normal = hit.normal,
                        distance = hit.distance, playerOwned = hit.transform.IsChildOf(player), slopeAccepted = hit.normal.y >= Mathf.Cos(report.playerSlopeLimit * Mathf.Deg2Rad) };
                    row.safetyHits.Add(detail);
                    if (detail.playerOwned || !detail.slopeAccepted) { detail.rejection = detail.playerOwned ? "player hit ignored" : "slope rejected"; continue; }
                    var feet = hit.point + Vector3.up * .05f;
                    detail.capsuleBottom = feet + Vector3.up * .31f; detail.capsuleTop = feet + Vector3.up * 1.47f;
                    foreach (var collider in Physics.OverlapCapsule(detail.capsuleBottom, detail.capsuleTop, .27f, ~0, QueryTriggerInteraction.Ignore))
                        detail.overlaps.Add(DescribeCollider(collider, player));
                    foreach (var offset in new[] { Vector3.right * .25f, Vector3.left * .25f, Vector3.forward * .25f, Vector3.back * .25f })
                    {
                        var support = new SupportProbe { origin = feet + offset + Vector3.up * .3f };
                        support.hit = Physics.Raycast(support.origin, Vector3.down, out var supportHit, .65f, 1, QueryTriggerInteraction.Ignore);
                        if (support.hit) { support.hitPoint = supportHit.point; support.normal = supportHit.normal; support.colliderPath = TransformPath(supportHit.transform); }
                        detail.supports.Add(support);
                    }
                    detail.supportAccepted = detail.supports.All(s => s.hit);
                    detail.rejection = detail.overlaps.Any(c => !c.playerOwned) ? "capsule overlap" : !detail.supportAccepted ? "support ray miss" : "accepted";
                }
                row.outcome = !row.withinBounds ? "outside preview bounds" : row.actualTrySafeFeet ? "safe" : row.safetyHits.Count == 0 ? "no layer-0 downward support hit" : string.Join("; ", row.safetyHits.Select(h => h.rejection).Distinct());
            }
            report.outputPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Demo/Chapter3/runtime_approach_probe.json"));
            string json = JsonUtility.ToJson(report, true); Directory.CreateDirectory(Path.GetDirectoryName(report.outputPath));
            File.WriteAllText(report.outputPath, json);
            return $"READ_ONLY_PROBE safe={report.safeCount}/{report.candidates.Count}; groundFailures={report.groundFailures}; file={report.outputPath}; outcomes="+
                string.Join(", ",report.candidates.GroupBy(c=>c.outcome).Select(g=>g.Key+":"+g.Count()));
        }
        static void Stage(string id, DemoEventKind kind, string trigger, int reward)
        {
            var stage = campaignClone.Stages.Single(s => s.Id == id); stage.Implemented = true; stage.Event = kind; stage.TriggerId = trigger; stage.TongboReward = reward;
        }
        static string Hash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
        static string HashText(string text) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", ""); }
        static void Persist()
        {
            if (run == null) return; string json = JsonUtility.ToJson(run, true); SessionState.SetString(StateKey, json);
            Directory.CreateDirectory(Path.GetDirectoryName(Output)); File.WriteAllText(Output, json);
        }
        static void CheckThat(bool pass, string name, string detail)
        {
            run.checks.Add(new Check { name = name, status = pass ? "PASS" : "FAIL", detail = detail });
            if (!pass) throw new InvalidOperationException(name + ": " + detail);
        }
        static void Next(int stage, string phase, double delay = .12)
        { run.stage = stage; run.phase = phase; run.scaledReadyAt = Time.timeAsDouble + delay; Persist(); }
        static bool InputReady => ui != null && ui.Pause != null && !ui.Pause.IsPaused && !ui.Pause.Gate.InputBlocked && Time.timeScale > 0;
        static int Currency => session.Progress.ledger.currency;
        static bool HasStage(string id) => session.Progress.campaign.Completed.Contains(id);
        static PrologueEncounter Actor(string id) => session.Actors.Single(a => a != null && a.Id == id);
        static AttackProvenance Hit(Element element) => AttackProvenance.Create(session.Walker.Body.gameObject, DamageSource.PlayerDirect, element);
        static void Approach(PrologueEncounter actor)
        {
            foreach (float radius in new[] { 12f, 9f, 16f }) for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI / 8; Vector3 candidate = actor.transform.position + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius;
                try { candidate = WorldMacroPlaytestAuthoring.Ground(candidate, true) + Vector3.up * .06f; } catch (Exception) { continue; }
                if (!session.TrySafeFeet(candidate, out var feet)) continue;
                Vector3 facing = Vector3.ProjectOnPlane(actor.transform.position - feet, Vector3.up);
                session.Teleport(feet, Quaternion.LookRotation(facing).eulerAngles.y); Physics.SyncTransforms();
                CheckThat(true, "safe API approach " + actor.Id, "Actual terrain + Session.TrySafeFeet/Teleport; no walk or input synthesized."); return;
            }
            throw new InvalidOperationException("No safe capsule position around authored " + actor.Id);
        }
        static void EnableDeathSubscription(PrologueEncounter actor)
        {
            // CombatActive=false prevents attacks globally. Prologue alone is enabled synchronously for the real death event.
            session.CombatActive = false; session.Cull(); actor.enabled = true;
            actor.GetComponent<EnemyController>().AttackEnabled = false; actor.GetComponent<EnemyController>().enabled = false;
            var boss = actor.GetComponent<CheongryongCombatController>();
            if (boss != null) { boss.enabled = true; boss.AttackEnabled = false; }
            CheckThat(actor.enabled && actor.GetComponent<EnemyVitals>().enabled, "actual registered life/death path " + actor.Id,
                "Encounter and boss life observers are enabled synchronously, with AttackEnabled=false; synthetic damage uses real EnemyVitals, not Session progress injection.");
        }
        static WorldMacroProgress Disk()
        {
            CheckIsolation(); var store = new AtomicJsonStore<WorldMacroProgress>(run.savePath, WorldMacroProgress.Valid); var loaded = store.Load();
            CheckThat(loaded != null && store.LoadStatus == "primary", "independent UUID primary reload", "New AtomicJsonStore reader; not an app restart."); return loaded;
        }
        static void OnBossDeath() { if (run != null) run.bossDeaths++; }
        static void OnGukAccepted(SpellCast cast, Vector3 origin, Vector3 direction)
        { if (run != null && cast.Letter == '국' && cast.Kind == SpellKind.Field) run.gukAccepted++; }
        static Vector3 ActualFeet()
        {
            var body = session.Walker.Body;
            return body.transform.TransformPoint(body.center) - Vector3.up * body.height * .5f;
        }
        static T WiringField<T>(string name)
        {
            var field = typeof(Oheangbu.App.CombatLoopWiring).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(name);
            return (T)field.GetValue(session.Walker.Wiring);
        }
        static bool HasActualGukProof()
        {
            var field = typeof(WorldMacroPlaytestSession).GetField("provenGukSite", BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null && ReferenceEquals(field.GetValue(session), gukSite);
        }
        static void CastGukThroughWiring()
        {
            var method = typeof(Oheangbu.App.CombatLoopWiring).GetMethod("OnLetterDrawn", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException("OnLetterDrawn");
            method.Invoke(session.Walker.Wiring, new object[] { new DrawnLetter('국', default, default, null, .8f, .7f, 2f, 2f, 4) });
        }
        static void Tick()
        {
            if (run == null || !run.active) return;
            try
            {
                if (EditorApplication.timeSinceStartup > run.deadline) throw new TimeoutException("Chapter3 Play API diagnostic exceeded 60 seconds; no time-scale override is used.");
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play exited during Chapter3 diagnostic.");
                CheckIsolation(); if (EditorApplication.isPaused || !InputReady || Time.timeAsDouble < run.scaledReadyAt) return;
                switch (run.stage)
                {
                    case 0:
                    {
                        CheckThat(PreviousStages.All(HasStage) && session.Progress.campaign.Completed.Count == 7, "seven real previous stages retained", "Chapter2 progression remains unchanged.");
                        var lesson = Actor(DemoChapterThreeSceneAuthoring.LessonActorId); Approach(lesson); session.Cull();
                        Next(10, "allow normal Unity lifecycle to activate lesson", .25); break;
                    }
                    case 10:
                    {
                        var lesson = Actor(DemoChapterThreeSceneAuthoring.LessonActorId);
                        var growth = lesson.GetComponent<CheongryongGrowthController>(); var life = lesson.GetComponent<EnemyVitals>();
                        CheckThat(growth != null && growth.State == CheongryongGrowthState.Ready && life.IsAlive && Mathf.Abs(life.Hp - life.MaxHp) < .001f,
                            "lesson begins alive and armed", $"No Restore or hidden growth-state reset; state={growth?.State}, hp={life.Hp}/{life.MaxHp}, enabled={life.enabled}, revision={life.LifeRevision}.");
                        EnableDeathSubscription(lesson); life.TakeDamage(life.MaxHp * .5f, Hit(Element.Fire));
                        CheckThat(growth.IsWindingUp && !HasStage("deep_forest"), "actual threshold damage starts growth", "Fire PlayerDirect provenance reaches actual HpChanged/DamageResolved.");
                        Next(1, "interrupt live lesson growth with Metal"); break;
                    }
                    case 1:
                    {
                        var lesson = Actor(DemoChapterThreeSceneAuthoring.LessonActorId); var growth = lesson.GetComponent<CheongryongGrowthController>();
                        CheckThat(growth.IsWindingUp, "growth remains within four-second window", "Waiting used actual Unity scaled time.");
                        lesson.GetComponent<EnemyVitals>().TakeDamage(1f, Hit(Element.Metal));
                        CheckThat(growth.WasInterrupted && lesson.GetComponent<DemoGrowthLessonLink>().HasProof && HasStage("deep_forest") && Currency == run.startCurrency + 40,
                            "Metal interruption commits lesson and 40 reward", "Real DamageResolved -> GrowthInterrupted -> registered proof link -> durable Session transaction.");
                        run.lessonCurrency = Currency; var disk = Disk();
                        CheckThat(disk.campaign.Completed.Contains("deep_forest") && disk.ledger.completed.Contains(DemoGrowthLessonLink.LessonId) && disk.ledger.currency == Currency,
                            "lesson proof and reward on disk", "Primary JSON reflects the same transaction before any explicit SaveNow.");
                        CheckThat(!lesson.GetComponent<DemoGrowthLessonLink>().TryCommit() && Currency == run.lessonCurrency, "lesson cannot reward twice", "Retry of the actual proof is idempotent.");
                        Next(2, "approach actual Cheongryong"); break;
                    }
                    case 2:
                    {
                        var boss = Actor(WorldMacroPlaytestSession.CheongryongId); Approach(boss); session.Cull();
                        Next(20, "allow normal Unity lifecycle to activate boss", .25); break;
                    }
                    case 20:
                    {
                        var boss = Actor(WorldMacroPlaytestSession.CheongryongId);
                        var life = boss.GetComponent<EnemyVitals>(); var controller = boss.GetComponent<CheongryongCombatController>();
                        var body = boss.GetComponent<CheongryongBodyFollow>(); var rig = boss.GetComponent<CheongryongRigAnimation>();
                        CheckThat(controller != null && controller.Profile != null && !boss.GetComponent<EnemyController>().enabled, "boss replaces legacy attack update", "Actual scene component and configured profile.");
                        CheckThat(body != null && body.IsConfigured && rig != null && rig.IsConfigured && rig.HasGraph && rig.GraphEvaluationCount > 0,
                            "actual imported body follow and animation graph", "Configured graph has evaluated in Play; visual deformation is not claimed.");
                        CheckThat(life.IsAlive && Mathf.Abs(life.Hp - life.MaxHp) < .001f && boss.GetComponent<CheongryongGrowthController>().State == CheongryongGrowthState.Ready,
                            "boss life and growth ready", "No diagnostic health/phase reset.");
                        observedBoss = life; observedBoss.Died += OnBossDeath; EnableDeathSubscription(boss);
                        life.TakeDamage(life.MaxHp * .5f, Hit(Element.Fire));
                        CheckThat(boss.GetComponent<CheongryongGrowthController>().IsWindingUp, "boss starts four-second growth", "Actual PlayerDirect Fire damage.");
                        Next(3, "wait 4.2 scaled seconds for uncancelled growth", 4.2); break;
                    }
                    case 3:
                    {
                        var boss = Actor(WorldMacroPlaytestSession.CheongryongId); var life = boss.GetComponent<EnemyVitals>(); var growth = boss.GetComponent<CheongryongGrowthController>();
                        CheckThat(growth.IsPhaseTwo && growth.AttemptUsed && Mathf.Abs(life.Hp - life.MaxHp) < .001f && boss.GetComponent<CheongryongCombatController>().PhaseTwoCadence,
                            "uncancelled growth heals once and enters phase 2", "4.2 real scaled seconds elapsed; no direct Tick, phase setter or Time.timeScale change.");
                        life.TakeDamage(life.MaxHp * .55f, Hit(Element.Fire)); life.TakeDamage(1f, Hit(Element.Metal)); run.bossPostGrowthHp = life.Hp;
                        Next(4, "verify no repeated growth", .2); break;
                    }
                    case 4:
                    {
                        var boss = Actor(WorldMacroPlaytestSession.CheongryongId); var life = boss.GetComponent<EnemyVitals>(); var growth = boss.GetComponent<CheongryongGrowthController>();
                        CheckThat(growth.IsPhaseTwo && !growth.IsWindingUp && Mathf.Abs(life.Hp - run.bossPostGrowthHp) < .001f,
                            "later threshold and Metal do not rearm growth", "Second low-health transition preserves phase 2 and current HP.");
                        EnableDeathSubscription(boss); life.TakeDamage(float.MaxValue, Hit(Element.Fire));
                        CheckThat(!life.IsAlive && run.bossDeaths == 1 && boss.Current == PrologueEncounter.Behaviour.Dead && HasStage("cheongryong") &&
                            session.Progress.defeated.Contains(WorldMacroPlaytestSession.CheongryongId) && Currency == run.lessonCurrency + 160,
                            "real boss death commits campaign and 160 reward", "EnemyVitals.Died -> PrologueEncounter.Defeated -> Session reward transaction. No direct primary Session.EnemyDefeated call.");
                        run.bossCurrency = Currency;
                        CheckThat(!boss.GetComponent<CheongryongRigAnimation>().HasGraph, "death releases owned animation graph", "Actual boss life notification releases the graph before the next diagnostic phase.");
                        CheckThat(session.HasDemoGuk && session.Progress.ledger.completed.Contains(WorldMacroPlaytestSession.GiyeokUnlockId) && session.Progress.ui.knownSpellLetters.Contains("국") &&
                            session.Progress.ui.knownVirtues.Contains("仁") && session.RenAvailable, "국 종성ㄱ and仁 unlock together", "Explicit gameplay unlock plus UI discovery and unused lethal-survival guard.");
                        ui.CloseMenu(); Next(5, "reload boss reward and reject duplicate defeat"); break;
                    }
                    case 5:
                    {
                        var disk = Disk();
                        CheckThat(disk.campaign.Completed.Contains("cheongryong") && disk.defeated.Contains(WorldMacroPlaytestSession.CheongryongId) && disk.ledger.currency == run.bossCurrency &&
                            disk.ledger.completed.Contains(WorldMacroPlaytestSession.GiyeokUnlockId) && disk.ui.knownSpellLetters.Contains("국") && disk.ui.knownVirtues.Contains("仁"),
                            "boss reward atomic on primary file", "Fresh store reload of stage, defeated ID, currency and both unlocks; no explicit post-reward SaveNow.");
                        Actor(WorldMacroPlaytestSession.CheongryongId).GetComponent<EnemyVitals>().TakeDamage(float.MaxValue, Hit(Element.Fire));
                        session.EnemyDefeated(WorldMacroPlaytestSession.CheongryongId);
                        CheckThat(run.bossDeaths == 1 && Currency == run.bossCurrency && session.Progress.campaign.Completed.Count == 9 && session.RenAvailable,
                            "duplicate damage and defeat notification cannot repay or recharge", "Direct Session call is a negative duplicate test only, after real death succeeded.");
                        var player = session.Walker.Body.GetComponent<PlayerVitals>();
                        CheckThat(player.TakeDamage(float.MaxValue) && Mathf.Abs(player.Hp01 * player.MaxHp - 1f) < .001f && session.Progress.renUsed && !session.RenAvailable,
                            "first actual lethal hit uses newly unlocked 仁", "Actual PlayerVitals.TakeDamage and durable guard preserve exactly 1 HP; no synthetic virtue assignment.");
                        disk = Disk(); CheckThat(disk.renUsed && Mathf.Abs(disk.ledger.hp * player.MaxHp - 1f) < .001f && disk.ledger.currency == run.bossCurrency,
                            "仁 use durably saved before damage observation", "The UUID primary retains used state and 1 HP.");
                        Stage("guk_return", DemoEventKind.FieldUsed, DemoGukRevisitSite.Id, 60);
                        CheckThat(session.Progress.version == WorldMacroProgress.CurrentVersion && session.Progress.campaign.Completed.Count == 9 &&
                            campaignClone.Stages.Single(s => s.Id == "guk_return").Implemented &&
                            !campaignClone.Stages.Single(s => s.Id == "escort").Implemented,
                            "runtime-only tenth stage enabled after actual boss and Ren checks", "Only the runtime campaign clone enables FieldUsed and 60 reward; original campaign and future escort remain unchanged.");
                        run.gukPad = gukSite.LiftPad.position; run.gukSurface = gukSite.UpperSurface.position;
                        CheckThat(session.TrySafeFeet(run.gukPad + Vector3.up * .06f, out var feet) &&
                            Vector3.Distance(feet, run.gukPad) < .2f, "actual authored Guk pad has safe ground", "No search substitutes another site and no teleport reaches the elevated surface.");
                        Vector3 facing = Vector3.ProjectOnPlane(run.gukSurface - feet, Vector3.up);
                        session.Teleport(feet, Quaternion.LookRotation(facing).eulerAngles.y);
                        Next(30, "settle on actual ground lift pad before central Guk cast", .3); break;
                    }
                    case 30:
                    {
                        var field = session.DemoField;
                        CheckThat(field != null && field.IsUnlocked && !field.HasPlatform && !HasActualGukProof(),
                            "ground placement alone provides no Guk-use proof", "No proof setter or service Tick is invoked by this diagnostic.");
                        CheckThat(Vector3.Distance(ActualFeet(), run.gukPad) < .2f && !HasStage("guk_return"),
                            "player stands on the authored lower pad", "Actual CharacterController feet; no elevated teleport.");
                        var ink = WiringField<InkPool>("_ink"); var config = WiringField<CombatConfigSO>("_config");
                        run.gukInkBefore = ink.Value; run.gukInkCost = config.SpellInkCost / ink.CapacityMultiplier;
                        CheckThat(run.gukInkBefore >= run.gukInkCost, "real ink balance can pay Guk", "No diagnostic refill.");
                        observedWiring = session.Walker.Wiring; observedWiring.CastAccepted += OnGukAccepted;
                        run.gukStartY = ActualFeet().y; run.gukStartedAt = Time.timeAsDouble;
                        CastGukThroughWiring();
                        CheckThat(field.State == FieldLiftState.Rising && run.gukAccepted == 1 &&
                            Mathf.Abs(ink.Value - (run.gukInkBefore - run.gukInkCost)) < .0001f,
                            "central resolver casts Guk and spends ink exactly once", "Resolved-letter API -> CombatLoopWiring.OnLetterDrawn -> resolver -> FieldSpellService.TryPrepare/CommitPrepared and InkPool. Not keyboard drawing or direct service commit.");
                        Next(31, "wait for actual Unity lift updates", 0); break;
                    }
                    case 31:
                    {
                        var field = session.DemoField; run.gukMaximumHeight = Mathf.Max(run.gukMaximumHeight, field.CurrentHeight);
                        if (field.State == FieldLiftState.Rising)
                        {
                            if (Time.timeAsDouble - run.gukStartedAt > 6) throw new TimeoutException("Actual Guk rise did not finish within six scaled seconds: " + field.LastFailure);
                            break;
                        }
                        CheckThat(field.State == FieldLiftState.Holding && field.PassengerSupported &&
                            Mathf.Abs(field.CurrentHeight - FieldSpellService.MaximumHeight) < .02f &&
                            ActualFeet().y - run.gukStartY >= 2.25f && Time.timeAsDouble - run.gukStartedAt >= 1.8,
                            "real controller rides Guk to 2.4m Holding", $"Actual time={Time.timeAsDouble-run.gukStartedAt:F3}s, lift={field.CurrentHeight:F3}m, feet delta={ActualFeet().y-run.gukStartY:F3}m; no direct Tick or height assignment. Failure={field.LastFailure}");
                        CheckThat(HasActualGukProof() && !HasStage("guk_return") && Currency == run.bossCurrency,
                            "actual supported Holding sets site proof without prematurely granting reward", "Read-only observation of Session proof created by real StateChanged, pad distance and supported height.");
                        run.gukMoveLastAt = Time.timeAsDouble; run.gukMoveDeadline = run.gukMoveLastAt + 8;
                        Next(32, "short CharacterController transfer from lift to upper surface", 0); break;
                    }
                    case 32:
                    {
                        double now = Time.timeAsDouble;
                        if (now > run.gukMoveDeadline) throw new TimeoutException("Cannot reach actual upper reward surface using collision-resolved movement. feet=" + ActualFeet() + " target=" + run.gukSurface);
                        float elapsed = Mathf.Clamp((float)(now - run.gukMoveLastAt), 0f, .1f); run.gukMoveLastAt = now;
                        if (elapsed <= 0) break;
                        // Bounded diagnostic transfer, not a route walkthrough. Motor/physics remain active.
                        // Never add upward travel: the real field service has supplied all ascent.
                        while (elapsed > .00001f)
                        {
                            float dt = Mathf.Min(elapsed, 1f / 60f); elapsed -= dt;
                            Vector3 horizontal = Vector3.ProjectOnPlane(run.gukSurface - ActualFeet(), Vector3.up);
                            session.Walker.Body.Move(Vector3.ClampMagnitude(horizontal, 2.2f * dt) + Vector3.down * (1.2f * dt));
                        }
                        run.gukFinalFeet = ActualFeet();
                        if (run.gukFinalFeet.y < run.gukSurface.y - .3f)
                            throw new InvalidOperationException("Controller fell below Guk upper surface during transfer; no teleport or safety bypass applied. feet=" + run.gukFinalFeet);
                        if (Vector3.ProjectOnPlane(run.gukSurface - run.gukFinalFeet, Vector3.up).magnitude > .12f ||
                            Mathf.Abs(run.gukSurface.y - run.gukFinalFeet.y) > .2f) break;
                        CheckThat(HasActualGukProof() && session.CanInteract(DemoGukRevisitSite.Id),
                            "real upper feet and line of sight permit the authored interaction", "Short horizontal/downward CharacterController.Move only; safe ascent proof came from the live lift.");
                        CheckThat(session.Interact(DemoGukRevisitSite.Id) && HasStage("guk_return") &&
                            session.Progress.campaign.Completed.Count == 10 && Currency == run.bossCurrency + 60 &&
                            session.Progress.ledger.completed.Contains(DemoGukRevisitSite.Id),
                            "actual authored Guk interaction commits 60 and stage ten", "Session.Interact enforces live proof, same site, upper elevation, distance and LOS before its durable transaction.");
                        run.gukCurrency = Currency; ui.CloseMenu(); Next(33, "close real detail menu and reload field reward", .2); break;
                    }
                    case 33:
                    {
                        var disk = Disk();
                        CheckThat(disk.campaign.Completed.Count == 10 && disk.campaign.Completed.Contains("guk_return") &&
                            disk.ledger.completed.Contains(DemoGukRevisitSite.Id) && disk.ledger.currency == run.gukCurrency && disk.renUsed,
                            "field reward and prior Ren use persisted together", "Independent UUID primary reader; no reward injection or app-restart claim.");
                        CheckThat(session.Interact(DemoGukRevisitSite.Id) && Currency == run.gukCurrency && session.Progress.campaign.Completed.Count == 10,
                            "actual repeated Guk interaction cannot repay", "Same live content point after the real detail page closes.");
                        CheckThat(run.gukAccepted == 1 && Mathf.Abs(WiringField<InkPool>("_ink").Value - (run.gukInkBefore - run.gukInkCost)) < .0001f,
                            "one accepted field cast and one ink debit through reward", "The diagnostic never refills ink or recasts to manufacture proof.");
                        ui.CloseMenu(); session.DemoField.RequestRelease(); Next(34, "verify departed lift cleanup", .3); break;
                    }
                    case 34:
                    {
                        CheckThat(!session.DemoField.HasPlatform && session.Walker.Body.enabled && session.Walker.Motor.enabled,
                            "departed lift cleans up without disabling movement", "Actual release after player reaches the real stone ledge.");
                        VerifyProtected(); run.status = "PASS_API_INTEGRATION"; run.active = false; run.holdingPlay = true;
                        run.phase = "finished at actual Guk upper reward surface for optional still capture; clones held until stop; Chapter2 owns Play lifecycle";
                        Persist(); EditorApplication.update -= Tick; break;
                    }
                }
            }
            catch (Exception exception) { Fail(exception); }
        }
        static void CheckIsolation()
        {
            if (session == null || session.TestSaveSuffix != run.suffix || SessionState.GetString(SuffixKey, "") != run.suffix ||
                !run.suffix.StartsWith("_chapter2_", StringComparison.Ordinal) || !string.Equals(Path.Combine(Application.persistentDataPath, session.Content.SaveSlot + session.TestSaveSuffix + ".json"), run.savePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("UUID save isolation changed; no further diagnostic mutation is allowed.");
        }
        static void VerifyProtected()
        {
            foreach (var file in run.protectedFiles) CheckThat(File.Exists(file.path) && Hash(file.path) == file.hash, "protected file unchanged", Path.GetFileName(file.path));
            var known = new HashSet<string>(run.protectedFiles.Select(f => f.path), StringComparer.OrdinalIgnoreCase);
            CheckThat(Directory.GetFiles(Application.persistentDataPath, "*.json*", SearchOption.TopDirectoryOnly).Where(p => !p.StartsWith(run.savePath, StringComparison.OrdinalIgnoreCase)).All(known.Contains),
                "no non-test save created", "Only the existing Chapter2 UUID primary/backup/temp may change.");
            if (originalContent != null) CheckThat(HashText(JsonUtility.ToJson(originalContent)) == run.contentMemoryHash && HashText(JsonUtility.ToJson(originalContent.Campaign)) == run.campaignMemoryHash,
                "original assets unchanged in memory", "Only runtime clones received implemented-stage overrides.");
        }
        static void Fail(Exception exception)
        {
            run.failures.Add(exception.ToString()); run.status = "FINDINGS"; run.active = false; run.holdingPlay = EditorApplication.isPlaying;
            run.phase = "failed; inspect then stop to restore runtime-only data"; EditorApplication.update -= Tick; Persist();
        }
        static void Restore()
        {
            if (run == null || run.restored) return; EditorApplication.update -= Tick;
            if (observedBoss != null) observedBoss.Died -= OnBossDeath; observedBoss = null;
            if (observedWiring != null) observedWiring.CastAccepted -= OnGukAccepted; observedWiring = null;
            if (session == null) session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (originalContent == null) originalContent = EditorUtility.InstanceIDToObject(run.originalContentId) as WorldMacroPlaytestSO ?? AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(run.sourceContentPath);
            if (contentClone == null) contentClone = EditorUtility.InstanceIDToObject(run.contentCloneId) as WorldMacroPlaytestSO;
            if (campaignClone == null) campaignClone = EditorUtility.InstanceIDToObject(run.campaignCloneId) as DemoCampaignProfile;
            if (session != null && session.TestSaveSuffix == run.suffix)
            {
                if (EditorApplication.isPlaying && session.DemoField != null) session.DemoField.RequestRelease();
                if (originalContent != null) session.Content = originalContent;
                session.CombatActive = run.originalCombat;
                if (EditorApplication.isPlaying) session.Cull();
            }
            if (contentClone != null && !AssetDatabase.Contains(contentClone)) Object.DestroyImmediate(contentClone);
            if (campaignClone != null && !AssetDatabase.Contains(campaignClone)) Object.DestroyImmediate(campaignClone);
            contentClone = null; campaignClone = null;
            try { VerifyProtected(); } catch (Exception exception) { run.failures.Add(exception.ToString()); run.status = "FINDINGS"; }
            run.active = false; run.holdingPlay = false; run.restored = true; run.phase = "runtime clones restored; Chapter2 save suffix untouched"; Persist();
        }
        static void PlayChanged(PlayModeStateChange state)
        {
            if (run == null || run.restored) return;
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            { if (run.active) { run.status = "ABORTED"; run.failures.Add("Play exited before diagnostic finished."); } Restore(); }
        }
        static void EditorQuitting() { if (run != null && !run.restored) Restore(); }
        static void InterruptedReload()
        {
            if (run == null || run.restored) return;
            run.status = "ABORTED"; run.failures.Add("Domain reload interrupted the held diagnostic; runtime data restored instead of resuming partial checks."); Restore();
        }
    }
}
