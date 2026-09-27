using System;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    public static class DemoEnemyProfileChecks
    {
        [Serializable] private sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public bool physicsSceneValid, usesDefaultPhysics;
            public int wallDefaultWorldHits, wallOwnedWorldHits, floorDefaultWorldHits, floorOwnedWorldHits;
            public string[] unverified = { "Actual player input against six archetypes", "Ground warning VFX readability", "Navigation tactics and final animation", "Projectile visual occlusion in Game View" };
        }
        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Edit-mode isolated checks only.");
            var result = new Report(); var profiles = new List<EnemyAttackProfileSO>();
            var scene = EditorSceneManager.NewPreviewScene(); var config = ScriptableObject.CreateInstance<CombatConfigSO>();
            var random = UnityEngine.Random.state;
            void Check(bool ok, string label) { (ok ? result.passed : result.failed).Add(label); }
            try
            {
                string originalConfig = JsonUtility.ToJson(config);
                result.physicsSceneValid = scene.GetPhysicsScene().IsValid();
                result.usesDefaultPhysics = scene.GetPhysicsScene().Equals(Physics.defaultPhysicsScene);
                foreach (EnemyArchetype archetype in Enum.GetValues(typeof(EnemyArchetype)))
                {
                    var profile = ScriptableObject.CreateInstance<EnemyAttackProfileSO>(); profiles.Add(profile); profile.ApplyDefaults(archetype);
                    Check(profile.TryValidate(out _), archetype + " default profile validates finite and coherent values");
                }
                var origin = new Vector3(1000, 5000, 1000);
                var enemy = Obj("ProfileEnemy", scene, origin); var vitals = enemy.AddComponent<EnemyVitals>(); Set(vitals, "_config", config); vitals.Restore();
                var controller = enemy.AddComponent<EnemyController>(); Set(controller, "_config", config); Set(controller, "_vitals", vitals);
                var player = Obj("ProfileTarget", scene, origin + Vector3.forward * 2); var hp = player.AddComponent<PlayerVitals>(); Set(hp, "_config", config); hp.Restore();
                Set(controller, "_player", player.transform); Set(controller, "_playerVitals", hp);
                var judge = new ParryJudge(config); controller.Init(judge);
                judge.OwnedImpactResolved += hit => { if (hit.Outcome == ParryOutcome.Success) vitals.AddParry(hit.Attack); };
                int cues = 0, cancelled = 0, impacts = 0; EnemyAttackCue cue = default; EnemyAttackImpact impact = default;
                controller.AttackTelegraphed += e => { cues++; cue = e; };
                controller.AttackPresentationEnded += (_, wasCancelled) => { if (wasCancelled) cancelled++; };
                controller.AttackImpactResolved += e => { impacts++; impact = e; };
                Check(controller.AttackProfile == null && controller.EffectiveAttackMode == EnemyController.AttackMode.LegacyDistance &&
                    Near(controller.AttackDamage, config.MeleeDamage) && Near(controller.AttackRange, config.EnemyEngageRange) && Near(controller.RecoveryDuration, .4f),
                    "Absent profile preserves legacy serialized mode and config values");
                var invalid = profiles[0]; invalid.Telegraph = float.NaN;
                bool rejected = false; try { controller.Configure(invalid); } catch (ArgumentException) { rejected = true; }
                Check(rejected && controller.AttackProfile == null, "Invalid non-finite profile is rejected without changing controller");
                invalid.ApplyDefaults(EnemyArchetype.NeutralMelee); invalid.ProjectileSpeed = float.PositiveInfinity;
                Check(!invalid.TryValidate(out _), "Infinite projectile speed rejected");
                invalid.ApplyDefaults(EnemyArchetype.NeutralMelee); invalid.Damage = -1;
                Check(!invalid.TryValidate(out _), "Negative damage rejected");
                invalid.ApplyDefaults(EnemyArchetype.NeutralMelee); invalid.Mode = EnemyController.AttackMode.RangedOnly;
                Check(!invalid.TryValidate(out _), "Delivery/mode mismatch rejected"); invalid.ApplyDefaults(EnemyArchetype.NeutralMelee);

                void Begin(EnemyArchetype archetype, float distance = 2)
                {
                    controller.AttackEnabled = true; player.transform.position = origin + Vector3.forward * distance;
                    controller.Configure(profiles[(int)archetype]); hp.Restore(); judge.ClearGuard(); Physics.SyncTransforms();
                    Call(controller, "BeginAuthoredTelegraph");
                }
                void Resolve() { Set(controller, "_stateUntil", Time.time - 1f); Call(controller, "TickAuthoredAttack"); }
                Begin(EnemyArchetype.NeutralMelee); Resolve();
                Check(Near(impact.AppliedDamage, 15) && impact.Attack.Source == DamageSource.Enemy && !impact.Attack.Element.HasValue && impact.Attack.Instigator == vitals,
                    "Neutral melee uses no parry element and preserves owned damage provenance");
                Begin(EnemyArchetype.FireRanged, 8); Resolve(); float duration = Get<float>(controller, "_authoredFlightDuration");
                float expected = Vector3.Distance(origin + Vector3.up * 1.2f, player.transform.position + Vector3.up * .5f) / profiles[1].ProjectileSpeed;
                Check(Near(duration, expected) && cue.Attack.Element == Element.Fire && cue.Attack.AttackId > 0,
                    "Fire profile sets element and computes flight time from range and speed");
                judge.RaiseGuard(Element.Water, Time.time); Set(controller, "_impactTime", Time.time - 1f); Call(controller, "TickAuthoredAttack");
                Check(impact.Outcome == ParryOutcome.Success && Near(impact.AppliedDamage, 0) && Near(vitals.Groggy.Value01, 1f / 3f),
                    "Authored fire still uses same owned parry and attacker-local groggy route");

                Begin(EnemyArchetype.WaterRanged, 8); Resolve(); Vector3 aimed = controller.AuthoredTargetPoint;
                player.transform.position += Vector3.right * 4; Physics.SyncTransforms(); Set(controller, "_impactTime", Time.time - 1f); Call(controller, "TickAuthoredAttack");
                Check(!impact.InShape && Near(impact.AppliedDamage, 0) && controller.AuthoredTargetPoint == aimed,
                    "Water projectile keeps launch aim and misses lateral movement");
                Begin(EnemyArchetype.FireRanged, 8); Resolve(); player.transform.position += Vector3.right * 4; Physics.SyncTransforms();
                Set(controller, "_impactTime", Time.time - 1f); Call(controller, "TickAuthoredAttack");
                Check(impact.InShape && Near(impact.AppliedDamage, 12), "Fire homing differs from aimed water and follows moving target");

                Begin(EnemyArchetype.MetalSoldier); player.transform.position = origin + new Vector3(2, 0, 0); Resolve();
                Check(!impact.InShape && Near(impact.AppliedDamage, 0), "Metal thrust commits to narrow windup direction and can be sidestepped");
                Begin(EnemyArchetype.EarthHeavy); player.transform.position = origin + Quaternion.Euler(0, 60, 0) * Vector3.forward * 2; Resolve();
                Check(impact.InShape && Near(impact.AppliedDamage, 24) && impact.Attack.Element == Element.Earth &&
                    Near(controller.TelegraphDuration, 1.5f) && Near(controller.RecoveryDuration, 1.2f),
                    "Earth heavy uses wider committed arc, elemental contact and long recovery");

                Begin(EnemyArchetype.FireRanged, 8); int beforeCancelled = cancelled, beforeImpacts = impacts;
                controller.AttackEnabled = false; Call(controller, "TickAuthoredAttack"); Resolve();
                Check(cancelled == beforeCancelled + 1 && impacts == beforeImpacts && Near(hp.Hp01, 1),
                    "Disabling attacks during telegraph cancels once without late damage");
                Begin(EnemyArchetype.FireRanged, 8); beforeCancelled = cancelled; controller.StopAttack(); Resolve();
                Check(cancelled == beforeCancelled + 1 && Near(hp.Hp01, 1), "StopAttack cancels authored telegraph and presentation");

                var wall = Obj("ProfileOccluder", scene, origin + Vector3.forward * 4 + Vector3.up);
                var wallCollider = wall.AddComponent<BoxCollider>(); wallCollider.size = new Vector3(5, 5, .5f);
                Physics.SyncTransforms(); controller.Configure(profiles[1]); controller.AttackEnabled = true;
                player.transform.position = origin + Vector3.forward * 8; Physics.SyncTransforms();
                RaycastHit[] probeHits = null;
                result.wallDefaultWorldHits = Physics.RaycastAll(origin + Vector3.up * .4f, Vector3.forward, 8, ~0, QueryTriggerInteraction.Ignore).Length;
                result.wallOwnedWorldHits = ScenePhysicsQuery.RaycastAll(scene, origin + Vector3.up * .4f, Vector3.forward, 8, ~0, ref probeHits);
                int previousCues = cues; Call(controller, "BeginAuthoredTelegraph");
                Check(!controller.HasLineOfSight() && cues == previousCues, "Physical occluder prevents authored telegraph through wall");
                wallCollider.enabled = false; Physics.SyncTransforms(); Begin(EnemyArchetype.FireRanged, 8);
                beforeCancelled = cancelled; wallCollider.enabled = true; Physics.SyncTransforms(); Call(controller, "TickAuthoredAttack");
                Check(cancelled == beforeCancelled + 1 && Near(hp.Hp01, 1), "New wall during telegraph cancels authored damage");
                wallCollider.enabled = false; Physics.SyncTransforms();

                var floor = Obj("VineTargetFloor", scene, origin + Vector3.forward * 6 - Vector3.up * .5f);
                var floorCollider = floor.AddComponent<BoxCollider>(); floorCollider.size = new Vector3(10, 1, 10); Physics.SyncTransforms();
                result.floorDefaultWorldHits = Physics.RaycastAll(origin + Vector3.forward * 6 + Vector3.up * 2, Vector3.down, 6, ~0, QueryTriggerInteraction.Ignore).Length;
                result.floorOwnedWorldHits = ScenePhysicsQuery.RaycastAll(scene, origin + Vector3.forward * 6 + Vector3.up * 2, Vector3.down, 6, ~0, ref probeHits);
                Begin(EnemyArchetype.WoodVine, 6); Vector3 fixedGround = controller.AuthoredTargetPoint;
                Check(cue.Delivery == EnemyAttackDelivery.GroundEruption && Mathf.Abs(fixedGround.y - origin.y) < .01f,
                    "Wood vine selects actual ground for its warning at windup start");
                player.transform.position += Vector3.right * 3; Physics.SyncTransforms(); Resolve();
                Check(!impact.InShape && Near(impact.AppliedDamage, 0) && controller.AuthoredTargetPoint == fixedGround,
                    "Wood ground eruption remains at warned point and misses player who leaves radius");
                Begin(EnemyArchetype.WoodVine, 6); Resolve();
                Check(impact.InShape && Near(impact.AppliedDamage, 17) && impact.Attack.Element == Element.Wood,
                    "Wood ground impact damages standing target with wood provenance");
                floorCollider.enabled = false; Physics.SyncTransforms(); previousCues = cues; Begin(EnemyArchetype.WoodVine, 6);
                Check(cues == previousCues, "Missing ground cancels vine cast instead of erupting in midair");
                controller.Configure(null);
                Check(controller.AttackProfile == null && controller.EffectiveAttackMode == EnemyController.AttackMode.LegacyDistance,
                    "Removing optional profile restores original mode");
                Check(JsonUtility.ToJson(config) == originalConfig, "Six archetypes never mutate shared CombatConfig");
            }
            catch (Exception e) { result.failed.Add(e.ToString()); }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene); UnityEngine.Object.DestroyImmediate(config);
                foreach (var profile in profiles) UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Random.state = random;
            }
            result.status = result.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(result, true);
        }
        private static GameObject Obj(string name, Scene scene, Vector3 position)
        { var go = new GameObject(name); go.transform.position = position; SceneManager.MoveGameObjectToScene(go, scene); return go; }
        private static bool Near(float a, float b) => Mathf.Abs(a - b) < .001f;
        private static T Get<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        private static void Set(object instance, string name, object value) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
        private static object Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, args);
    }
}
