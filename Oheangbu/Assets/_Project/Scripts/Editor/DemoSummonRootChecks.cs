using System;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    // Disposable API fixture. Calls the production cast, manager and common damage path.
    // No native input, campaign scene edits, captures, asset authoring or runtime visual claims.
    public static class DemoSummonRootChecks
    {
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public List<string> diagnostics = new List<string>();
            public string[] unverified = { "Native drawing input", "Root attack mesh and animation readability",
                "Campaign slopes and moving-player combat", "Frame performance" };
        }

        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Isolated Edit-mode checks only.");
            var r = new Report(); Fixture f = null;
            void Check(bool pass, string label) => (pass ? r.passed : r.failed).Add(label);
            void Snapshot(string label)
            {
                var clock = f.Manager.ActiveClock; var currentPlan = f.Manager.ActiveRootPlan;
                r.diagnostics.Add(label + ": actor=" + (f.Manager.Active != null) + ", phase=" + clock?.Phase +
                    ", age=" + clock?.Elapsed.ToString("R") + ", pending=" + clock?.PendingAttackId +
                    ", root=" + f.Manager.ActiveAttackIsRoot + ", length=" + currentPlan?.Length +
                    ", released=" + currentPlan?.IsReleased + ", front=" + currentPlan?.FrontDistance +
                    ", cancelled=" + currentPlan?.IsCancelled + ", rootStarts=" + f.Manager.RootAttackStarts +
                    ", cancellations=" + f.Manager.RootAttackCancellations + ", consumed=" + f.Manager.ConsumedStrikes +
                    ", hitA=" + f.Count(f.A) + ", hitB=" + f.Count(f.B) + ", hitSide=" + f.Count(f.Side));
            }
            try
            {
                // Float-facing presentation time can round to the release boundary one substep before
                // the precise combat clock is due. The manager must wait, not cancel that pending cast.
                var boundary = new SummonCombatClock(.2f, 20, .2f);
                for (int i = 0; i < 8; i++) boundary.Advance(.025f);
                float visibleRelease = boundary.Elapsed + .55f; boundary.TryBeginAttack(1, 10, 1, .55f, 10);
                for (int i = 0; i < 22; i++) boundary.Advance(.025f);
                bool early = boundary.TryConsumeHit(1, 10, 1, true, true, true);
                Check(boundary.Elapsed >= visibleRelease && !early && boundary.PendingAttackId == 1,
                    "Rounded visual release boundary leaves a not-yet-due combat strike pending rather than invalid");
                boundary.Advance(.025f);
                Check(boundary.TryConsumeHit(1, 10, 1, true, true, true), "Pending boundary strike becomes consumable on the next scaled step");
                f = new Fixture();
                Check(!f.Profile.RootAttackEnabled && Near(f.Profile.RootWindupSeconds, .55f) && Near(f.Profile.RootAttackRange, 6) &&
                    Near(f.Profile.RootAttackWidth, .7f) && Near(f.Profile.RootTravelSpeed, 8), "Root attack is optional with authored .55s/6m/.7m/8mps defaults");
                f.Reset(); f.A.transform.position = f.Manager.Active.transform.position + Vector3.forward * 1.7f + Vector3.up * .875f;
                f.B.gameObject.SetActive(false); f.Side.gameObject.SetActive(false); f.Sync(); f.UntilAttack();
                Check(!f.Manager.ActiveAttackIsRoot && f.Manager.ActiveRootPlan == null, "Root-off profile preserves existing horn attack selection");
                f.Advance(f.Profile.WindupSeconds + .1f);
                Check(f.Count(f.A) == 1, "Root-off horn attack still reaches production damage path");
                f.Profile.RootAttackEnabled = true; f.Profile.RootCooldownSeconds = 10;
                f.Profile.RootMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_Project/Art/SpellVFX120/Botanical/Meshes/Mesh_Root.asset");
                f.Profile.RootMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/SpellVFX120/Botanical/Materials/M_Root_Bark.mat");
                Check(f.Profile.RootMesh != null && f.Profile.RootMaterial != null, "Existing botanical mesh and bark material are available for root presentation diagnostic");

                f.Reset(); f.UntilAttack(); var plan = f.Manager.ActiveRootPlan;
                Check(f.Manager.ActiveAttackIsRoot && plan != null, "Target beyond horn reach selects actual optional root attack");
                if (plan == null) throw new InvalidOperationException("Expected root plan, received another attack.");
                var rootPieces = f.Manager.Active.GetComponentsInChildren<MeshFilter>(true);
                var scales = new Vector3[rootPieces.Length];
                for (int i = 0; i < rootPieces.Length; i++) scales[i] = rootPieces[i].transform.localScale;
                bool reused = rootPieces.Length > 0 && rootPieces.Length <= 8;
                foreach (var piece in rootPieces) reused &= piece.sharedMesh == f.Profile.RootMesh && piece.GetComponent<Renderer>().sharedMaterial == f.Profile.RootMaterial;
                Check(reused, "Six-metre line reuses botanical source in at most eight rigid mesh pieces");
                Check(Near(plan.ReleaseAt - plan.CastStartedAt, .55f) && Near(plan.Width, .7f) && plan.Length <= 6.01f && plan.Length > 4.8f,
                    "Committed root plan records authored windup, width and supported six-metre corridor");
                var positions = new List<Vector3>(plan.GroundPositions); var times = new List<float>(plan.ArrivalTimes);
                bool monotonic = times.Count > 2 && positions.Count == times.Count;
                for (int i = 1; i < times.Count; i++) monotonic &= times[i] >= times[i - 1] &&
                    Math.Abs((times[i] - times[i - 1]) - Vector3.Distance(positions[i], positions[i - 1]) / f.Profile.RootTravelSpeed) < .015f;
                Check(monotonic, "Ground samples and arrival times form a monotonic authored-speed front");
                float age = f.Manager.ActiveClock.Elapsed; var front = plan.FrontDistance;
                f.Manager.Tick(0);
                Check(Near(f.Manager.ActiveClock.Elapsed, age) && Near(plan.FrontDistance, front) && f.Hits.Count == 0, "Pause freezes root telegraph, propagation and damage");
                f.Advance(.4f); Check(f.Hits.Count == 0, "Root telegraph has no early damage before .55-second release");
                int guard = 0; while (f.Count(f.A) == 0 && guard++ < 100) f.Manager.Tick(.01f);
                Check(f.Count(f.A) == 1 && f.Count(f.B) == 0 && f.Count(f.Side) == 0, "Near captured enemy is hit before farther enemy while side target is excluded");
                age = f.Manager.ActiveClock.Elapsed; front = plan.FrontDistance; int countAtPause = f.Hits.Count;
                var pausedPositions = new Vector3[rootPieces.Length]; bool rigid = true;
                for (int i = 0; i < rootPieces.Length; i++)
                { rigid &= rootPieces[i] != null && rootPieces[i].transform.localScale == scales[i]; if (rootPieces[i] != null) pausedPositions[i] = rootPieces[i].transform.position; }
                Check(rigid, "Travel raises existing rigid root pieces without repeatedly changing their scale");
                for (int i = 0; i < 60; i++) f.Manager.Tick(0);
                Check(Near(f.Manager.ActiveClock.Elapsed, age) && Near(plan.FrontDistance, front) && f.Hits.Count == countAtPause, "Pause during live root travel preserves front and does not duplicate earlier contact");
                bool frozen = true; for (int i = 0; i < rootPieces.Length; i++) frozen &= rootPieces[i] != null && rootPieces[i].transform.position == pausedPositions[i] && rootPieces[i].transform.localScale == scales[i];
                Check(frozen, "Paused root presentation preserves the actual piece positions and scales");
                f.Advance(1.5f);
                Check(f.Count(f.A) == 1 && f.Count(f.B) == 1 && f.Count(f.Side) == 0 && f.Contacts == 2,
                    "Two targets receive one common damage/contact each; lateral target receives none");
                bool owned = f.Hits.Count == 2;
                foreach (var hit in f.Hits) owned &= hit.Attack.Source == DamageSource.Summon && hit.Attack.Element == Element.Wood &&
                    hit.Attack.Instigator == f.Manager.Active && Near(hit.AppliedDamage, 10 * f.Wiring.SummonDamageScale(Element.Wood) * f.Profile.RootDamageMultiplier);
                Check(owned, "Actual root contacts retain actor provenance and apply snapshot multiplier once");
                Check(f.Hits.Count == 2 && f.Hits[0].Attack.AttackId == f.Hits[1].Attack.AttackId, "Single propagated root attack shares one provenance ID across different targets");
                bool unchanged = plan.GroundPositions.Count == positions.Count && plan.ArrivalTimes.Count == times.Count;
                for (int i = 0; i < positions.Count && unchanged; i++) unchanged &= plan.GroundPositions[i] == positions[i] && plan.ArrivalTimes[i] == times[i];
                Check(unchanged, "Playback does not regenerate committed terrain samples or target timing");

                foreach (int fps in new[] { 30, 60, 120 })
                foreach (float speed in new[] { 1f, .25f })
                {
                    f.Reset(); f.UntilAttack();
                    float elapsed = 0, step = speed / fps;
                    while (elapsed < 1.6f) { f.Manager.Tick(step); elapsed += step; }
                    Check(f.Count(f.A)==1 && f.Count(f.B)==1 && f.Count(f.Side)==0,
                        fps+"fps at time scale "+speed+" preserves one propagated hit per captured in-strip target");
                }

                f.Reset(); f.UntilAttack(); Snapshot("death windup start"); plan = f.Manager.ActiveRootPlan; f.A.TakeDamage(f.A.Hp); f.Advance(2); Snapshot("death after front");
                Check(f.Count(f.A) == 0 && f.Count(f.B) == 1, "Primary target death during windup invalidates only that captured life while farther target remains eligible");
                f.Reset(); f.UntilAttack(); Snapshot("respawn windup start"); uint oldLife = f.A.LifeRevision; f.A.TakeDamage(f.A.Hp); f.A.Restore(); f.Advance(2); Snapshot("respawn after front");
                Check(f.A.LifeRevision != oldLife && f.Count(f.A) == 0 && f.Count(f.B) == 1, "Respawned target cannot inherit scheduled root hit from its previous life revision");
                f.Reset(); f.UntilAttack(); Snapshot("dodge windup start"); f.Advance(.65f); Snapshot("dodge after release"); f.A.transform.position += Vector3.right * 2; f.Sync(); f.Advance(1.5f); Snapshot("dodge after front");
                Check(f.Count(f.A) == 0 && f.Count(f.B) == 1, "Enemy moving sideways after release evades front while farther in-strip enemy is still hit");
                f.Reset(); f.UntilAttack(); Snapshot("late entry windup start"); f.Side.transform.position = f.Manager.Active.transform.position + Vector3.forward * 4 + Vector3.up * .875f;
                f.Sync(); f.Advance(2); Snapshot("late entry after front");
                Check(f.Count(f.Side) == 0 && f.Count(f.A) == 1 && f.Count(f.B) == 1, "Enemy entering corridor after snapshot is not silently added to the attack plan");

                f.Reset(); f.Wall(2); f.Advance(1.5f);
                Check(f.Manager.ActiveRootPlan == null && f.Hits.Count == 0, "Existing wall prevents target acquisition and behind-wall root contact");
                f.Reset(); f.GroundGap(2, 1); f.UntilAttack(); plan = f.Manager.ActiveRootPlan;
                Check(plan != null && plan.Length < 2.2f, "Missing ground clips committed root path before unsupported span");
                f.Advance(2); Check(f.Hits.Count == 0, "Truncated ground path cannot hit enemies on the opposite bank");

                f.Reset(); f.UntilAttack(); f.Wall(2); f.Advance(2);
                Check(f.Hits.Count == 0, "Physical wall appearing during windup stops real root contacts behind it");
                f.Reset(); f.UntilAttack(); f.GroundGap(2, 1); f.Advance(2);
                Check(f.Hits.Count == 0, "Missing ground introduced ahead of the front stops root propagation instead of bridging air");

                f.Reset(); f.UntilAttack(); var cleared = f.Manager.ActiveRootPlan; int hitsBeforeClear = f.Hits.Count;
                f.Manager.Clear(); f.Manager.Tick(2);
                Check(f.Manager.Active == null && f.Manager.ActiveRootPlan == null && cleared.IsCancelled && f.Hits.Count == hitsBeforeClear,
                    "Clear cancels pending root plan and prevents late common damage");
                f.Reset(); f.UntilAttack(); var dissolving = f.Manager.ActiveRootPlan; f.Advance(.65f); Snapshot("before forced dissolution");
                Check(dissolving != null && !dissolving.IsCancelled && f.Manager.ActiveRootPlan == dissolving,
                    "Root remains active after release until explicit dissolution");
                f.Manager.ActiveClock?.BeginDissolve(); f.Advance(.4f);
                Check(f.Manager.Active == null && dissolving != null && dissolving.IsCancelled && f.Hits.Count == 0,
                    "Dissolution during advancing root front cancels future arrivals and cleans ownership");
                f.Profile.ActivitySeconds = 2; f.Reset();
                foreach (var enemy in new[] { f.A, f.B, f.Side }) enemy.transform.position = f.Origin + Vector3.right * 25;
                f.Sync(); f.Advance(1.2f);
                f.A.transform.position = f.Manager.Active.transform.position + Vector3.forward * 3.2f + Vector3.up * .875f;
                f.B.transform.position = f.Manager.Active.transform.position + Vector3.forward * 4.8f + Vector3.up * .875f;
                f.Sync(); int rootStarts = f.Manager.RootAttackStarts; f.Advance(3);
                Check(f.Manager.Active == null && f.Manager.ActiveRootPlan == null && f.Manager.RootAttackStarts == rootStarts && f.Hits.Count == 0,
                    "Insufficient remaining activity prevents incomplete root cast and naturally removes summon");
                int afterExpiry = f.Hits.Count; f.Manager.Tick(4);
                Check(f.Hits.Count == afterExpiry, "Expired summon never revives a root strike on later ticks");
                bool oldMeshesGone = true; foreach (var piece in rootPieces) oldMeshesGone &= piece == null;
                Check(oldMeshesGone, "Cleared/replaced summon destroys previously allocated botanical root pieces");
            }
            catch (Exception e) { r.failed.Add(e.ToString()); }
            finally { f?.Dispose(); }
            r.status = r.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(r, true);
        }

        sealed class Fixture : IDisposable
        {
            public Scene Scene;
            public CombatConfigSO Config;
            public SummonCombatProfile Profile;
            public CombatLoopWiring Wiring;
            public DemoSummonCombatManager Manager;
            public InkPool Ink;
            public EnemyVitals A, B, Side;
            public BoxCollider Floor;
            public Vector3 Origin = new Vector3(6200, 6200, 6200);
            public readonly List<EnemyDamageResult> Hits = new List<EnemyDamageResult>();
            public int Contacts;
            NavMeshData data; NavMeshDataInstance installed;
            readonly List<GameObject> temporary = new List<GameObject>();

            public Fixture()
            {
                try
                {
                Scene = EditorSceneManager.NewPreviewScene();
                Config = ScriptableObject.CreateInstance<CombatConfigSO>(); Set(Config, "_enemyMaxHp", 1000f);
                Profile = ScriptableObject.CreateInstance<SummonCombatProfile>();
                var ground = Obj("Root fixture ground", Origin + Vector3.down * .1f);
                Floor = ground.AddComponent<BoxCollider>(); Floor.size = new Vector3(60, .2f, 60);
                var settings = NavMesh.GetSettingsByIndex(0); settings.agentRadius = .4f; settings.agentHeight = 2;
                settings.agentClimb = .3f; settings.agentSlope = 35; settings.overrideVoxelSize = true; settings.voxelSize = .15f;
                var sources = new List<NavMeshBuildSource> { new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(ground.transform.position, Quaternion.identity, Vector3.one), size = Floor.size, area = 0 } };
                data = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(Origin, new Vector3(64, 8, 64)), Vector3.zero, Quaternion.identity);
                if (data == null) throw new InvalidOperationException("Root fixture NavMesh build failed.");
                installed = NavMesh.AddNavMeshData(data);
                if (!installed.valid) throw new InvalidOperationException("Root fixture NavMesh installation failed.");
                var template = Obj("Diagnostic empty appearance", Vector3.zero); template.SetActive(false);
                Profile.PresentationPrefab = template; Profile.FormationSeconds = .2f; Profile.DissolveSeconds = .2f;
                Profile.CooldownSeconds = 10; Profile.FollowSpeed = 1.8f;
                var player = Obj("Root fixture player feet", Origin);
                Wiring = Obj("Root fixture wiring", Origin).AddComponent<CombatLoopWiring>();
                Set(Wiring, "_config", Config); Set(Wiring, "_playerTransform", player.transform);
                Ink = new InkPool(Config, null); Wiring.Construct(null, null, null, Ink); Set(Wiring, "_contactVfx", null);
                Manager = Wiring.gameObject.AddComponent<DemoSummonCombatManager>(); Manager.Wiring = Wiring; Manager.Profiles = new[] { Profile };
                Call(Manager, "Bind");
                A = Enemy("Root target A"); B = Enemy("Root target B"); Side = Enemy("Root excluded side target");
                Set(Wiring, "_enemies", new[] { A, B, Side }); Call(Wiring, "CollectControllers");
                Wiring.EnemyDamageResolved += hit => Hits.Add(hit); Wiring.EnemyHitResolved += (_, __) => Contacts++;
                Sync();
                }
                catch { Dispose(); throw; }
            }
            EnemyVitals Enemy(string name)
            {
                var enemy = Obj(name, Origin + Vector3.right * 25).AddComponent<EnemyVitals>();
                Set(enemy, "_config", Config); enemy.Restore(); return enemy;
            }
            public GameObject Obj(string name, Vector3 point)
            { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, Scene); go.transform.position = point; return go; }
            public void Reset()
            {
                Manager.Clear(); foreach (var go in temporary) if (go != null) UnityEngine.Object.DestroyImmediate(go); temporary.Clear();
                Floor.enabled = true; A.Restore(); B.Restore(); Side.Restore(); A.gameObject.SetActive(true); B.gameObject.SetActive(true); Side.gameObject.SetActive(true);
                foreach (var enemy in new[] { A, B, Side }) enemy.transform.position = Origin + Vector3.right * 25;
                Hits.Clear(); Contacts = 0; Ink.Restore(); Sync();
                Call(Wiring, "ResolveSummon", new SpellCast('곰', SpellKind.Summon, Element.Wood, 10, default, 1));
                if (Manager.Active == null) throw new InvalidOperationException("Root fixture summon preparation failed: " + Manager.LastFailure);
                Vector3 start = Manager.Active.transform.position;
                A.transform.position = start + Vector3.forward * 3.2f + Vector3.up * .875f;
                B.transform.position = start + Vector3.forward * 4.8f + Vector3.up * .875f;
                Side.transform.position = start + Vector3.forward * 4f + Vector3.right * 1.5f + Vector3.up * .875f;
                Sync();
            }
            public void UntilAttack()
            {
                int guard = 0;
                while (Manager.Active != null && Manager.ActiveClock.PendingAttackId == 0 && guard++ < 140) Manager.Tick(.025f);
                if (Manager.Active == null || Manager.ActiveClock.PendingAttackId == 0) throw new InvalidOperationException("Root fixture attack did not start.");
            }
            public void Advance(float time) { while (time > 0) { float step = Mathf.Min(.025f, time); Manager.Tick(step); time -= step; } }
            public int Count(EnemyVitals target) { int count = 0; foreach (var hit in Hits) if (hit.Target == target && hit.AppliedDamage > 0) count++; return count; }
            public void Wall(float forwardDistance)
            {
                var wall = Obj("Root path solid wall", Manager.Active.transform.position + Vector3.forward * forwardDistance + Vector3.up);
                wall.AddComponent<BoxCollider>().size = new Vector3(4, 4, .25f); temporary.Add(wall); Sync();
            }
            public void GroundGap(float forwardDistance, float length)
            {
                Floor.enabled = false; float gapStart = Manager.Active.transform.position.z + forwardDistance;
                foreach (var span in new[] { new Vector2(Origin.z - 30, gapStart), new Vector2(gapStart + length, Origin.z + 30) })
                {
                    var ground = Obj("Root ground gap bank", new Vector3(Origin.x, Origin.y - .1f, (span.x + span.y) * .5f));
                    ground.AddComponent<BoxCollider>().size = new Vector3(60, .2f, span.y - span.x); temporary.Add(ground);
                }
                Sync();
            }
            public void Sync() { Physics.SyncTransforms(); var physics = Scene.GetPhysicsScene(); if (physics.IsValid() && !physics.Equals(Physics.defaultPhysicsScene)) physics.Simulate(.001f); }
            public void Dispose()
            {
                if (Manager != null) Manager.Clear(); if (installed.valid) installed.Remove(); if (data != null) UnityEngine.Object.DestroyImmediate(data);
                if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene);
                if (Profile != null) UnityEngine.Object.DestroyImmediate(Profile); if (Config != null) UnityEngine.Object.DestroyImmediate(Config);
            }
        }

        static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        static bool Near(float a, float b) => Math.Abs(a - b) < .002f;
        static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }
}
