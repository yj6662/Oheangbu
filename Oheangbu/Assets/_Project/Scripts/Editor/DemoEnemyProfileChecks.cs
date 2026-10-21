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

                // #308 D308-125 적용 3: a charge runs the line it locked, hits the swept player once, and a wall stops it short
                var charge = ScriptableObject.CreateInstance<EnemyAttackProfileSO>(); profiles.Add(charge); charge.ApplyDefaults(EnemyArchetype.NeutralMelee);
                charge.Delivery = EnemyAttackDelivery.Charge; charge.Mode = EnemyController.AttackMode.RangedOnly; charge.Damage = 18; charge.Range = 8;
                charge.ProjectileSpeed = 7; charge.ImpactRadius = .7f; charge.MovementSpeedHint = 2.2f; charge.PreferredDistanceHint = 6;
                Check(charge.TryValidate(out _), "Charge profile with ranged mode validates");
                charge.Mode = EnemyController.AttackMode.MeleeOnly; Check(!charge.TryValidate(out _), "Charge with melee mode rejected");
                charge.Mode = EnemyController.AttackMode.RangedOnly; charge.ChargeMinTravel = 9; Check(!charge.TryValidate(out _), "Charge minimum travel beyond range rejected");
                charge.ChargeMinTravel = 4;
                float scale = Get<float>(controller, "_recoveryScale308");
                void ChargeAt(float playerAt)
                {
                    enemy.transform.SetPositionAndRotation(origin, Quaternion.identity); wallCollider.enabled = false;
                    controller.AttackEnabled = true; player.transform.position = origin + Vector3.forward * playerAt;
                    controller.Configure(charge); hp.Restore(); judge.ClearGuard(); Physics.SyncTransforms();
                    Call(controller, "BeginAuthoredTelegraph"); Resolve();
                }
                bool RunCharge() { for (int i = 0; i < 200 && controller.IsCharging; i++) { Call(controller, "TickAuthoredAttack"); Physics.SyncTransforms(); } return !controller.IsCharging; }
                float Ran() => enemy.transform.position.z - origin.z;
                ChargeAt(6); bool charging = controller.IsCharging && cue.Delivery == EnemyAttackDelivery.Charge; int before = impacts;
                Check(charging && RunCharge() && impacts == before + 1 && impact.InShape && Near(impact.AppliedDamage, 18) && !impact.Attack.Element.HasValue &&
                    Mathf.Abs(Ran() - 8) < .01f && Near(controller.RecoveryDuration, charge.Recovery * scale),
                    "Charge hits the swept player once and runs on to distance + overshoot, cut to range");
                ChargeAt(6); player.transform.position += Vector3.right * 2; Physics.SyncTransforms(); before = impacts;
                Check(controller.IsCharging && RunCharge() && impacts == before + 1 && !impact.InShape && Near(hp.Hp01, 1),
                    "Charge keeps its locked line and misses a sidestep after the lock");
                ChargeAt(7); wall.transform.position = origin + Vector3.forward * 5.5f + Vector3.up; wallCollider.enabled = true; Physics.SyncTransforms();
                Check(controller.IsCharging && RunCharge() && Mathf.Abs(Ran() - 4.75f) < .02f && !impact.InShape && Near(hp.Hp01, 1) &&
                    Near(controller.RecoveryDuration, charge.ChargeWallRecovery * scale),
                    "Wall stops a charge a body radius short and gives the long wall recovery");
                enemy.transform.SetPositionAndRotation(origin, Quaternion.identity); player.transform.position = origin + Vector3.forward * 6;
                wall.transform.position = origin + Vector3.forward * 4 + Vector3.up; Physics.SyncTransforms();
                Check(!(bool)Call(controller, "ChargeCanStart308"), "No charge starts with less than its minimum travel of open path");
                wallCollider.enabled = false; Physics.SyncTransforms();
                Check((bool)Call(controller, "ChargeCanStart308"), "Charge starts on an open path");

                // #308 D308-125 적용 4: a charger that cannot start its run closes in and bites once with its fallback, then is the charge again
                var bite = ScriptableObject.CreateInstance<EnemyAttackProfileSO>(); profiles.Add(bite); bite.ApplyDefaults(EnemyArchetype.NeutralMelee);
                bite.Damage = 12; bite.Range = 2.2f; bite.Telegraph = .75f; bite.Recovery = .6f; bite.ArcDegrees = 80; bite.PreferredDistanceHint = 1.35f;
                charge.Fallback = bite; Check(charge.TryValidate(out _), "Charge with a melee fallback validates");
                bite.Fallback = charge; Check(!bite.TryValidate(out _) && !charge.TryValidate(out _), "A fallback off a charge, or one with its own fallback, rejected");
                bite.Fallback = null; bite.Range = 9; Check(!charge.TryValidate(out _), "A fallback beyond the charge's range rejected");
                bite.Range = 2.2f; charge.Fallback = profiles[1]; Check(!charge.TryValidate(out _), "A fallback that is not a melee arc rejected");
                charge.Fallback = bite;
                void IdleAt(float playerAt, float wallAt)
                {
                    enemy.transform.SetPositionAndRotation(origin, Quaternion.identity); controller.AttackEnabled = true;
                    player.transform.position = origin + Vector3.forward * playerAt; wall.transform.position = origin + Vector3.forward * wallAt + Vector3.up;
                    wallCollider.enabled = wallAt > 0f; controller.Configure(charge); hp.Restore(); judge.ClearGuard(); Physics.SyncTransforms();
                    Set(controller, "_cooldown", 0f); Call(controller, "TickAuthoredAttack");
                }
                IdleAt(3, 3.75f);
                Check(!controller.AttackInProgress && controller.CloseIn308 && Near(controller.CloseInRange308, 2.2f) && Near(controller.CloseInDistance308, 1.35f),
                    "A blocked charger out of bite reach starts nothing and asks to close in to the bite's distance");
                Set(controller, "_cooldown", 5f); wallCollider.enabled = false; Physics.SyncTransforms(); Set(controller, "_closeInCheck308", 0f);
                Call(controller, "TickAuthoredAttack"); bool held = controller.CloseIn308 && !controller.AttackInProgress;
                Set(controller, "_cooldown", 0f); Call(controller, "TickAuthoredAttack");
                Check(held && controller.IsTelegraphing && cue.Delivery == EnemyAttackDelivery.Charge && !controller.CloseIn308,
                    "A close-in holds while the cooldown runs, and the next choice on an open path is the charge");
                controller.StopAttack();
                IdleAt(2, 2.75f); before = impacts;
                bool bit = controller.IsTelegraphing && cue.Delivery == EnemyAttackDelivery.MeleeArc && controller.AttackProfile == bite &&
                    controller.EffectiveAttackMode == EnemyController.AttackMode.MeleeOnly &&
                    Near(controller.PredictedImpactTime, Get<float>(controller, "_stateUntil")) && Near(controller.TelegraphDuration, bite.Telegraph); Resolve();
                Check(bit && impacts == before + 1 && impact.InShape && Near(impact.AppliedDamage, 12) && controller.IsRecovering &&
                    Near(controller.RecoveryDuration, bite.Recovery * scale),
                    "A blocked charger within bite reach lands its fallback bite with the bite's own damage and recovery");
                Resolve(); float cooldown = Get<float>(controller, "_cooldown");
                Check(!controller.AttackInProgress && controller.AttackProfile == charge && cooldown >= charge.CooldownRange.x && cooldown <= charge.CooldownRange.y,
                    "Back in idle the charger is the charge again and the charge's cooldown runs");
                IdleAt(2, 2.75f); beforeCancelled = cancelled; controller.StopAttack();
                Check(cancelled == beforeCancelled + 1 && controller.AttackProfile == charge && !controller.AttackInProgress,
                    "A bind or stop during the bite gives the charge back");
                IdleAt(6, 0f);
                Check(controller.IsTelegraphing && cue.Delivery == EnemyAttackDelivery.Charge && !controller.CloseIn308 && controller.AttackProfile == charge,
                    "An open path still charges and the charger does not close in");
                controller.StopAttack(); charge.Fallback = null; IdleAt(2, 2.75f);
                Check(!controller.AttackInProgress && !controller.CloseIn308, "Without a fallback a blocked charger only waits, as before");
                wallCollider.enabled = false; Physics.SyncTransforms();
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
