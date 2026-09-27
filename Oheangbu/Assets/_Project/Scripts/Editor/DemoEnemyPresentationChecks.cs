using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    public static class DemoEnemyPresentationChecks
    {
        private const string Art = "Assets/_Project/Art/SpellVFX120/";
        [Serializable] private sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Warning/eruption timing in actual Game View", "Slope screenshots", "Final size and visual readability" };
        }
        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Edit-mode fixture only.");
            var result = new Report(); var scene = EditorSceneManager.NewPreviewScene();
            void Check(bool ok, string label) { (ok ? result.passed : result.failed).Add(label); }
            try
            {
                var warning = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "AreaRift/Cast_고.prefab");
                var spike = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "AreaFive/Body_고.prefab");
                var impactPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "AreaFive/AreaContact_0.prefab");
                if (warning == null || spike == null || impactPrefab == null) throw new InvalidOperationException("Required existing authored wood assets are missing.");
                string warningBefore = EditorJsonUtility.ToJson(warning), spikeBefore = EditorJsonUtility.ToJson(spike), impactBefore = EditorJsonUtility.ToJson(impactPrefab);
                var go = new GameObject("WoodPresentationFixture"); SceneManager.MoveGameObjectToScene(go, scene);
                var vitals = go.AddComponent<EnemyVitals>(); vitals.Restore();
                var controller = go.AddComponent<EnemyController>(); controller.Init(null);
                var presentation = go.AddComponent<EnemyAttackPresentation>(); presentation.Configure(controller, warning, spike, impactPrefab);
                Check(presentation.HasAllSources, "References use existing wood KTP, bamboo mesh and contact prefab");
                var point = new Vector3(1000, 5000, 1000);
                AttackProvenance Attack() => AttackProvenance.Create(vitals, DamageSource.Enemy, Element.Wood);
                void Cue(AttackProvenance attack) => Call(presentation, "OnTelegraphed", new EnemyAttackCue(attack, EnemyAttackDelivery.GroundEruption, point, 1.35f, 1.35f));
                var first = Attack(); Cue(first);
                Check(Get(presentation, "_block") != null, "Native property block is initialized lazily on first presentation use");
                Check(presentation.ActiveAttackCount == 1 && presentation.ActiveInstanceCount == 4, "One warning and three reused spike instances created for one attack");
                Cue(first); Check(presentation.ActiveAttackCount == 1 && presentation.ActiveInstanceCount == 4, "Duplicate attack cue produces no duplicate instances");
                var sourceMesh = spike.GetComponent<MeshFilter>().sharedMesh; int sameMeshes = 0;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) if (filter.sharedMesh == sourceMesh) sameMeshes++;
                Check(sameMeshes == 3, "Spike clones share original authored bamboo mesh rather than primitive replacements");
                var effects = (IDictionary)Get(presentation, "_effects"); object effect = effects[first.AttackId];
                var warningClone = (GameObject)Get(effect, "Warning"); Vector3 fixedPosition = warningClone.transform.position;
                go.transform.position += Vector3.forward * 20; Call(presentation, "Update");
                Check(warningClone.transform.position == fixedPosition, "Ground warning remains fixed when enemy moves");
                Call(presentation, "OnImpact", new EnemyAttackImpact(first, point, ParryOutcome.None, 0, false));
                Check(presentation.ActiveInstanceCount == 4, "Missed ground attack emits no contact effect");
                Call(presentation, "OnAttackEnded", first, true);
                Check(presentation.ActiveAttackCount == 0 && presentation.ActiveInstanceCount == 0, "Cancelled attack removes warning and spikes immediately");
                Call(presentation, "OnImpact", new EnemyAttackImpact(first, point, ParryOutcome.None, 10, true));
                Check(presentation.ActiveInstanceCount == 0, "Late impact from cancelled attack cannot recreate effects");
                var second = Attack(); Cue(second); Call(presentation, "OnImpact", new EnemyAttackImpact(second, point, ParryOutcome.None, 17, true));
                Call(presentation, "OnImpact", new EnemyAttackImpact(second, point, ParryOutcome.None, 17, true));
                Check(presentation.ActiveInstanceCount == 5, "Actual damage creates one existing small wood contact even with duplicate signal");
                Call(presentation, "OnAttackEnded", second, false);
                Check(presentation.ActiveInstanceCount == 4, "Normal resolve removes warning but preserves short spike/contact tail");
                effects = (IDictionary)Get(presentation, "_effects"); Set(effects[second.AttackId], "End", Time.time - 1f); Call(presentation, "Update");
                Check(presentation.ActiveInstanceCount == 0 && presentation.ActiveAttackCount == 0, "Expiry removes all tail objects and particles");
                var third = Attack(); Cue(third); Call(presentation, "OnImpact", new EnemyAttackImpact(third, point, ParryOutcome.Half, 4, true));
                Check(presentation.ActiveInstanceCount == 4, "Half-parry does not duplicate common KTP parry contact");
                vitals.TakeDamage(float.MaxValue);
                Check(presentation.ActiveInstanceCount == 0 && presentation.ActiveAttackCount == 0, "Enemy death clears pending presentation");
                vitals.Restore(); var fourth = Attack(); Cue(fourth); Call(presentation, "OnDisable");
                Check(presentation.ActiveInstanceCount == 0 && presentation.ActiveAttackCount == 0, "Component disable clears every owned visual instance");
                Check(EditorJsonUtility.ToJson(warning) == warningBefore && EditorJsonUtility.ToJson(spike) == spikeBefore && EditorJsonUtility.ToJson(impactPrefab) == impactBefore,
                    "Referenced prefab assets remain unchanged");
            }
            catch (Exception e) { result.failed.Add(e.ToString()); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            result.status = result.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(result, true);
        }
        private static object Get(object instance, string name) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(instance);
        private static void Set(object instance, string name, object value) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(instance, value);
        private static object Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, args);
    }
}
