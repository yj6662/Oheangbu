using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // Executes the production adapter and production VFX component with a null art profile.
    // Diagnostic receive properties are available before Begin returns for that profile.
    public static class Vfx120AdapterAudit
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] private sealed class CaseResult { public string name, status, detail; }
        [Serializable] private sealed class Report
        {
            public string status;
            public string scope = "Actual BrushStrokeFeedAdapter.SpawnPattern boundary calls on transient objects. Attack/area/guard routing, unchanged area plan, old lifetime compatibility and authored color/lifetime ownership. No drawing recognition, damage timing, visual art or performance PASS implied.";
            public List<CaseResult> cases = new List<CaseResult>();
        }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
        private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Hidden).GetValue(target);
        private static void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-10f;

        public static string Run()
        {
            var report = new Report();
            var owned = new List<GameObject>();
            var config = AssetDatabase.LoadAssetAtPath<CombatConfigSO>("Assets/_Project/Data/Configs/CombatConfig_Default.asset");
            var style = AssetDatabase.LoadAssetAtPath<BrushStyleSO>("Assets/_Project/Data/Configs/BrushStyle_Default.asset");
            if (config == null || style == null) return "{\"status\":\"FAIL_MISSING_TEST_SETTINGS\"}";
            GameObject Make(string name)
            {
                var go = new GameObject("Vfx120AdapterAudit_" + name) { hideFlags = HideFlags.HideAndDontSave };
                owned.Add(go); return go;
            }
            void Check(string name, Action body)
            {
                try { body(); report.cases.Add(new CaseResult { name = name, status = "PASS" }); }
                catch (Exception e) { report.cases.Add(new CaseResult { name = name, status = "FAIL", detail = (e.InnerException ?? e).Message }); }
            }

            try
            {
                var adapter = Make("Adapter").AddComponent<BrushStrokeFeedAdapter>();
                Set(adapter, "_style", style); Set(adapter, "_combatConfig", config);
                var camera = Make("Camera").AddComponent<Camera>(); camera.enabled = false;
                camera.transform.position = new Vector3(2f, 3f, -4f); camera.transform.rotation = Quaternion.identity;
                // The merged adapter uses Camera.main; the local drawing experiment can override it.
                var cameraField = typeof(BrushStrokeFeedAdapter).GetField("_projectionCamera", Hidden);
                if (cameraField != null) cameraField.SetValue(adapter, camera);
                var effectiveCamera = cameraField != null ? camera : Camera.main;
                var target = Make("Target").transform; target.position = new Vector3(3f, 1f, 9f);
                var stroke = Make("Stroke").AddComponent<BrushStrokeRenderer>();
                stroke.Data.Add(new BrushStrokePoint(new Vector3(-0.2f, 1f, 0f), 0.02f, 1f, 0f));
                stroke.Data.Add(new BrushStrokePoint(new Vector3(0.2f, 1f, 0f), 0.02f, 1f, 0f));
                Type groupType = typeof(BrushStrokeFeedAdapter).GetNestedType("FadingGroup", BindingFlags.NonPublic);
                object group = Activator.CreateInstance(groupType, true);
                ((IList)groupType.GetField("Strokes").GetValue(group)).Add(stroke);
                Color tint = new Color(0.15f, 0.7f, 0.35f, 0.8f);
                groupType.GetField("FlashColor").SetValue(group, tint);
                MethodInfo spawn = typeof(BrushStrokeFeedAdapter).GetMethod("SpawnPattern", Hidden);
                var template = Make("AuthoredTemplate"); template.SetActive(false); template.AddComponent<Vfx120Effect>();
                var ps = template.AddComponent<ParticleSystem>();
                var main = ps.main; main.playOnAwake = false; main.startColor = Color.magenta;
                var color = ps.colorOverLifetime; color.enabled = true;
                var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.green, 0), new GradientColorKey(Color.blue, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
                color.color = new ParticleSystem.MinMaxGradient(gradient);

                void Authored(string name, bool attack, bool parry, AreaImpactPlan plan, Transform hitTarget, float duration, Vector3 expected)
                {
                    Check(name, () =>
                    {
                        Set(adapter, "_pendingFxPrefab", template); Set(adapter, "_pendingAttack", attack); Set(adapter, "_pendingParry", parry);
                        Set(adapter, "_pendingAttackTarget", hitTarget); Set(adapter, "_pendingAttackDuration", duration); Set(adapter, "_pendingAreaPlan", plan);
                        string before = plan == null ? null : JsonUtility.ToJson(plan);
                        var existing = new HashSet<Vfx120Effect>(Resources.FindObjectsOfTypeAll<Vfx120Effect>());
                        spawn.Invoke(adapter, new[] { group });
                        Vfx120Effect probe = null;
                        foreach (var effect in Resources.FindObjectsOfTypeAll<Vfx120Effect>())
                            if (!existing.Contains(effect) && !EditorUtility.IsPersistent(effect)) { probe = effect; owned.Add(effect.gameObject); }
                        Need(probe != null, "Authored component was not instantiated");
                        Need(Near(probe.ReceivedOrigin, new Vector3(0f, 1f, 0f)), "Origin differs from actual stroke bounds");
                        Need(Near(probe.ReceivedFallback, expected), "Wrong fallback geometry");
                        Need(probe.ReceivedTarget == (attack ? hitTarget : null), "Wrong target");
                        Need(probe.ReceivedImpactClock == (attack && duration > 0 ? duration : 0f), "Impact clock changed or invented");
                        Need(ReferenceEquals(probe.ReceivedAreaPlan, attack ? plan : null), "Area plan not forwarded as supplied");
                        Need(plan == null || before == JsonUtility.ToJson(plan), "Adapter mutated combat area plan");
                        Need(probe.ReceivedGuardClock == (parry && !attack ? config.GuardDuration : 0f), "Guard clock mismatch");
                        Need(probe.ReceivedGuardBrightWindow == (parry && !attack ? config.ParryWindow : 0f), "Parry brightness window mismatch");
                        Need(probe.GetComponent<PatternEffectLifetime>() == null, "Old lifetime attached to authored root");
                        var clonedPs = probe.GetComponent<ParticleSystem>();
                        Need(clonedPs.main.startColor.color == Color.magenta, "Old single-color tint replaced authored start color");
                        Color start = clonedPs.colorOverLifetime.color.gradient.Evaluate(0);
                        Color end = clonedPs.colorOverLifetime.color.gradient.Evaluate(1);
                        Need(start.g > 0.99f && start.r < 0.01f && start.b < 0.01f && end.b > 0.99f && end.r < 0.01f && end.g < 0.01f, "Authored gradient whitened");
                        Need(Get<Transform>(adapter, "_pendingAttackTarget") == null && Get<float>(adapter, "_pendingAttackDuration") == 0 && Get<AreaImpactPlan>(adapter, "_pendingAreaPlan") == null, "Pending target/clock/plan leaked to next cast");
                    });
                }
                Vector3 miss = effectiveCamera != null
                    ? effectiveCamera.transform.position + effectiveCamera.transform.forward * style.PatternMissRange
                    : new Vector3(0, 1, 0) + adapter.transform.forward * style.PatternMissRange;
                Authored("authored_single_clock", true, false, null, target, 0.375f, miss);
                Authored("authored_miss_no_invented_clock", true, false, null, null, 0f, miss);
                Authored("authored_cone_point_is_origin", true, false, new AreaImpactPlan { Shape = AreaShape.Cone, Point = new Vector3(1, 2, 3), Direction = new Vector3(3, 7, 4), Length = 10f }, target, 0f, new Vector3(7, 2, 11));
                foreach (var shape in new[] { AreaShape.Circle, AreaShape.Path, AreaShape.Volley })
                {
                    var area = new AreaImpactPlan { Shape = shape, Point = new Vector3(4, 0, 9), Direction = Vector3.forward, Delay = 0.4f, Speed = 7f, Radius = 1.2f, Length = 12f };
                    area.Shots.Add(new PlannedHit { ImpactTime = 17.125f, Power = 3.5f });
                    Authored("authored_" + shape, true, false, area, target, 0f, area.Point);
                }
                Authored("authored_parry_clock", false, true, null, null, 0f, new Vector3(0, 1, 0));
                Authored("authored_other_stationary", false, false, null, null, 0f, new Vector3(0, 1, 0));

                var legacy = Make("LegacyTemplate"); legacy.SetActive(false);
                Check("legacy_guard_lifetime_preserved", () =>
                {
                    Set(adapter, "_pendingFxPrefab", legacy); Set(adapter, "_pendingAttack", false); Set(adapter, "_pendingParry", true);
                    var existing = new HashSet<PatternEffectLifetime>(Resources.FindObjectsOfTypeAll<PatternEffectLifetime>());
                    spawn.Invoke(adapter, new[] { group });
                    PatternEffectLifetime created = null;
                    foreach (var lifetime in Resources.FindObjectsOfTypeAll<PatternEffectLifetime>())
                        if (!existing.Contains(lifetime) && !EditorUtility.IsPersistent(lifetime)) { created = lifetime; owned.Add(lifetime.gameObject); }
                    Need(created != null, "Legacy guard no longer owns old bloom lifetime");
                    Need(Get<float>(created, "_lifetime") == config.GuardDuration, "Legacy guard duration changed");
                    Need(Get<float>(created, "_guardBrightWindow") == config.ParryWindow, "Legacy parry window changed");
                });
            }
            finally
            {
                // BrushStrokeRenderer's runtime OnDestroy uses delayed Destroy;
                // release only this test's owned mesh before edit-mode cleanup.
                foreach (var go in owned)
                {
                    if (go == null) continue;
                    foreach (var renderer in go.GetComponentsInChildren<BrushStrokeRenderer>(true))
                    {
                        Mesh mesh = Get<Mesh>(renderer, "_mesh");
                        Set(renderer, "_mesh", null);
                        if (mesh != null) Object.DestroyImmediate(mesh);
                    }
                }
                for (int i = owned.Count - 1; i >= 0; --i) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            }
            report.status = report.cases.TrueForAll(c => c.status == "PASS") ? "PASS_ADAPTER_BOUNDARY_ONLY" : "FAIL";
            return JsonUtility.ToJson(report, true);
        }
    }
}
