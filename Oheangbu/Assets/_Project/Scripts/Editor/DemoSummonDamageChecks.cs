using System;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    // Calls production ApplySummonHit; it does not impersonate the summon AI/cast manager.
    public static class DemoSummonDamageChecks
    {
        [Serializable] private sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public int ownedWorldWallHits;
            public string[] unverified = { "Actual player cast and two-cost ink transaction", "Summon target selection and manager attack cadence",
                "Moving scene navigation and range", "Summon animations, visual contacts and cleanup in Play" };
        }

        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Isolated Edit-mode checks only.");
            var r = new Report(); var scene = EditorSceneManager.NewPreviewScene();
            var config = ScriptableObject.CreateInstance<CombatConfigSO>();
            void Check(bool pass, string text) => (pass ? r.passed : r.failed).Add(text);
            try
            {
                Set(config, "_enemyMaxHp", 1000f); string configBefore = JsonUtility.ToJson(config);
                var origin = new Vector3(3000, 3000, 3000);
                var player = Obj("SummonOwner", scene, origin);
                var attacker = Obj("SummonedDeer", scene, origin + Vector3.right);
                var a = Enemy("TargetA", scene, origin + Vector3.forward * 6, config);
                var b = Enemy("TargetB", scene, origin + Vector3.forward * 6 + Vector3.right * 4, config);
                var foreign = Enemy("UnwatchedTarget", scene, origin + Vector3.forward * 5 + Vector3.left * 3, config);
                var wiring = Obj("SummonDamageWiring", scene, origin).AddComponent<CombatLoopWiring>();
                Set(wiring, "_config", config); Set(wiring, "_enemies", new[] { a, b }); Set(wiring, "_playerTransform", player.transform);
                Call(wiring, "CollectControllers");
                int damageEvents = 0, contactEvents = 0; EnemyDamageResult last = default; Element lastElement = default;
                wiring.EnemyDamageResolved += hit => { damageEvents++; last = hit; };
                wiring.EnemyHitResolved += (_, element) => { contactEvents++; lastElement = element; };
                Vector3 strikeOrigin = origin + Vector3.up * .4f;
                AttackProvenance New(Element element = Element.Wood) => AttackProvenance.Create(attacker, DamageSource.Summon, element);
                EnemyDamageResult Hit(EnemyVitals target, AttackProvenance attack, float power = 10) => wiring.ApplySummonHit(target, target.LifeRevision, power, strikeOrigin, attack, '곰');

                var first = New(); var result = Hit(a, first);
                Check(Near(result.AppliedDamage, 10) && result.Target == a && result.Attack.Instigator == attacker && result.Attack.Source == DamageSource.Summon,
                    "Production summon damage retains owner, source and exact target");
                Check(damageEvents == 1 && contactEvents == 1 && last.Target == a && lastElement == Element.Wood, "Positive summon hit uses common damage and element contact events once");
                float aHp = a.Hp; result = Hit(a, first);
                Check(result.AppliedDamage == 0 && Near(a.Hp, aHp) && damageEvents == 1 && contactEvents == 1, "Duplicate attack ID on same enemy pays no damage and spawns no repeated contact");
                result = Hit(b, New());
                Check(Near(result.AppliedDamage, 10) && Near(a.Hp, aHp) && damageEvents == 2 && last.Target == b, "Independent attack against second enemy affects only that enemy");
                Check(Near(a.Groggy.Value01, 0) && Near(b.Groggy.Value01, 0), "Summon damage never accrues parry groggy");

                a.OpenWeakPoint(); a.TakeDamage(1, AttackProvenance.Create(player, DamageSource.PlayerDirect, Element.Fire));
                int directMask = a.WeakPointElementMask; int before = damageEvents;
                foreach (Element element in Enum.GetValues(typeof(Element)))
                {
                    if ((int)element < 0 || (int)element >= 5) continue;
                    result = Hit(a, New(element));
                    Check(Near(result.AppliedDamage, 15) && result.CompletionBonus == 0 && a.WeakPointElementMask == directMask && !a.CompletionAwarded,
                        element + " summon benefits from existing weak point but cannot credit direct five-element completion");
                }
                Check(damageEvents == before + 5 && Near(a.Groggy.Value01, 0), "Five elemental summon hits create five contacts without groggy or completion reward");
                a.ResetCombatState(); before = damageEvents; int contactsBefore = contactEvents;
                Check(Hit(foreign, New()).AppliedDamage == 0 && Near(foreign.Hp, 1000), "Target outside wired encounter is rejected");
                Check(Hit(a, AttackProvenance.Create(player, DamageSource.PlayerDirect, Element.Wood)).AppliedDamage == 0,
                    "Direct player provenance cannot enter summon-only entry point");
                Check(Hit(a, new AttackProvenance(0, attacker, DamageSource.Summon, Element.Wood)).AppliedDamage == 0, "Unidentified summon attack is rejected");
                Check(Hit(a, AttackProvenance.Create(null, DamageSource.Summon, Element.Wood)).AppliedDamage == 0, "Ownerless summon provenance is rejected");
                Check(Hit(a, AttackProvenance.Create(attacker, DamageSource.Summon, null)).AppliedDamage == 0, "Missing elemental provenance is rejected instead of silently losing contact event");
                Check(Hit(a, New(), 0).AppliedDamage == 0 && Hit(a, New(), -1).AppliedDamage == 0 && Hit(a, New(), float.NaN).AppliedDamage == 0 && Hit(a, New(), float.PositiveInfinity).AppliedDamage == 0,
                    "Zero, negative and non-finite summon power are rejected");
                Check(damageEvents == before && contactEvents == contactsBefore, "Rejected provenance, target and power emit no common damage/contact signals");

                uint oldLife = a.LifeRevision; a.TakeDamage(a.Hp); before = damageEvents;
                Check(wiring.ApplySummonHit(a, oldLife, 10, strikeOrigin, New(), '곰').AppliedDamage == 0, "Dead enemy receives no summon hit");
                a.Restore(); Check(a.LifeRevision != oldLife && wiring.ApplySummonHit(a, oldLife, 10, strikeOrigin, New(), '곰').AppliedDamage == 0 && Near(a.Hp, 1000),
                    "Old life revision cannot damage respawned enemy");
                Check(Near(Hit(a, New()).AppliedDamage, 10) && damageEvents == before + 1, "New attack can damage restored enemy with current life revision");
                a.enabled = false; before = damageEvents;
                Check(Hit(a, New()).AppliedDamage == 0 && damageEvents == before, "Disabled target rejects damage"); a.enabled = true;
                wiring.enabled = false; Check(Hit(a, New()).AppliedDamage == 0, "Disabled wiring rejects delayed summon callback"); wiring.enabled = true;

                var wall = Obj("SummonOccluder", scene, origin + Vector3.forward * 3 + Vector3.up);
                var collider = wall.AddComponent<BoxCollider>(); collider.size = new Vector3(3, 4, .5f);
                Physics.SyncTransforms(); var physics = scene.GetPhysicsScene();
                if (physics.IsValid() && !physics.Equals(Physics.defaultPhysicsScene)) physics.Simulate(.001f);
                RaycastHit[] hits = null;
                r.ownedWorldWallHits = ScenePhysicsQuery.RaycastAll(scene, strikeOrigin, Vector3.forward, 6, ~0, ref hits);
                Check(r.ownedWorldWallHits > 0, "Fixture occluder exists in the wiring-owned physics scene");
                aHp = a.Hp; before = damageEvents; var blocked = New(); result = Hit(a, blocked);
                Check(result.AppliedDamage == 0 && Near(a.Hp, aHp) && damageEvents == before, "Physical wall blocks summon hit through production scene-owned query");
                collider.enabled = false; Physics.SyncTransforms(); if (physics.IsValid() && !physics.Equals(Physics.defaultPhysicsScene)) physics.Simulate(.001f);
                Check(Near(Hit(a, New()).AppliedDamage, 10), "New attack succeeds after actual occluder removal");

                // Captured power is passed explicitly. This checks the entry point does not reapply a
                // changed upgrade; actual cast-quality and upgrade capture belong to the manager suite.
                a.ResetCombatState(); float multiplier = 1.1f; wiring.PlayerDamageScale = _ => multiplier;
                float capturedPower = 10 * multiplier; multiplier = 1.2f;
                Check(Near(Hit(a, New(), capturedPower).AppliedDamage, 11), "Previously captured 10-percent summon power is not scaled again by current 20-percent upgrade");
                Check(Near(Hit(a, New(), 10 * multiplier).AppliedDamage, 12), "New captured 20-percent power passes through once");
                Check(JsonUtility.ToJson(config) == configBefore, "Isolated summon path leaves shared CombatConfig unchanged");
            }
            catch (Exception e) { r.failed.Add(e.ToString()); }
            finally { EditorSceneManager.ClosePreviewScene(scene); UnityEngine.Object.DestroyImmediate(config); }
            r.status = r.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(r, true);
        }
        private static EnemyVitals Enemy(string name, Scene scene, Vector3 position, CombatConfigSO config)
        { var v = Obj(name, scene, position).AddComponent<EnemyVitals>(); Set(v, "_config", config); v.Restore(); return v; }
        private static GameObject Obj(string name, Scene scene, Vector3 position)
        { var go = new GameObject(name); go.transform.position = position; SceneManager.MoveGameObjectToScene(go, scene); return go; }
        private static bool Near(float a, float b) => Math.Abs(a - b) < .001f;
        private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, value);
        private static object Call(object obj, string method) => obj.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, null);
    }
}
