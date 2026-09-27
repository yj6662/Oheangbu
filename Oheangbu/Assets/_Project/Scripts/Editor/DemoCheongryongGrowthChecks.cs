using System;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    // Explicit isolated checks. Never enters Play mode, opens campaign scenes, saves assets or builds.
    public static class DemoCheongryongGrowthChecks
    {
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Manual Metal timing with moving boss and summons", "Boss head/tail/root/projectile attacks",
                "Growth animation, cancellation presentation and audio", "Boss difficulty, campaign completion and performance" };
        }

        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run growth checks in Edit mode.");
            var report = new Report();
            void Check(bool pass, string name) => (pass ? report.passed : report.failed).Add(name);
            try { CheckPure(Check); }
            catch (Exception e) { report.failed.Add("Pure growth: " + e); }
            Scene scene = EditorSceneManager.NewPreviewScene();
            var config = ScriptableObject.CreateInstance<CombatConfigSO>();
            try
            {
                Set(config, "_enemyMaxHp", 100f);
                CheckHealing(scene, config, Check);
                CheckAdapter(scene, config, Check);
            }
            catch (Exception e) { report.failed.Add("EnemyVitals adapter: " + e); }
            finally { EditorSceneManager.ClosePreviewScene(scene); UnityEngine.Object.DestroyImmediate(config); }
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL";
            return JsonUtility.ToJson(report, true);
        }

        private static void CheckPure(Action<bool, string> check)
        {
            var c = new CheongryongGrowthCycle();
            check(!c.Sample(10, 50.01f, 100, 1) && c.State == CheongryongGrowthState.Ready, "Above half HP does not start growth");
            check(!c.Sample(10, 50, 100, 1, true) && c.State == CheongryongGrowthState.Windup && c.AttemptUsed && Near(c.WindupRemaining, 4),
                "Exactly half HP starts one four-second attempt; initiating Metal hit is not a retroactive cancellation");
            for (int i = 0; i < 20; i++) c.Sample(10, 50, 100, 1);
            check(Near(c.WindupRemaining, 4), "Repeated samples at paused scaled time preserve the entire window");
            check(!c.Sample(13.999, 40, 100, 1) && c.State == CheongryongGrowthState.Windup, "No early heal immediately before deadline");
            check(c.Sample(14, 40, 100, 1, true) && c.State == CheongryongGrowthState.PhaseTwo,
                "Exact four-second boundary heals once; Metal at deadline is too late");
            check(!c.Sample(20, 1, 100, 1) && c.State == CheongryongGrowthState.PhaseTwo, "Low HP after successful healing never starts a second attempt");

            c.Sample(20, 100, 100, 2); c.Sample(20, 49, 100, 2);
            check(!c.Sample(23.999, 48, 100, 2, true) && c.State == CheongryongGrowthState.Interrupted && c.AttemptUsed,
                "Confirmed Metal just before deadline cancels the active attempt");
            check(!c.Sample(100, 1, 100, 2) && c.State == CheongryongGrowthState.Interrupted, "Interrupted attempt cannot heal or repeat later");
            c.Sample(100, 100, 100, 3); c.Sample(101, 1, 100, 3);
            check(!c.Sample(105, 0, 100, 3) && c.State == CheongryongGrowthState.Ended, "Lethal damage at deadline takes precedence over healing");
            check(!c.Sample(200, 1, 100, 3) && c.State == CheongryongGrowthState.Ended, "Ended encounter cannot revive its pending heal");
            c.Sample(200, 100, 100, 4); c.Sample(201, 50, 100, 4); c.End();
            check(!c.Sample(220, 50, 100, 4) && c.State == CheongryongGrowthState.Ended, "Disable cleanup consumes pending growth until encounter revision changes");
            c.Sample(220, 100, 100, 5);
            check(c.State == CheongryongGrowthState.Ready && !c.AttemptUsed && Near(c.WindupRemaining, 0), "New life revision completely re-arms growth");
            c.Sample(220, 50, 100, 5);
            check(c.Sample(1000, 49, 100, 5) && !c.Sample(1001, 49, 100, 5), "Large time step consumes heal only once");
            check(Throws(() => c.Sample(double.NaN, 50, 100, 5)) && Throws(() => c.Sample(double.PositiveInfinity, 50, 100, 5)) &&
                Throws(() => c.Sample(999, 50, 100, 5)), "Invalid or rewinding same-life time is rejected");
        }

        private static void CheckHealing(Scene scene, CombatConfigSO config, Action<bool, string> check)
        {
            var v = Enemy("HealingOnly", scene, config);
            v.TakeDamage(70); v.AddParry(AttackProvenance.Create(v, DamageSource.Enemy, Element.Wood)); v.OpenWeakPoint();
            uint revision = v.LifeRevision;
            float groggy = v.Groggy.Value01, multiplier = v.DamageMultiplier;
            int hpEvents = 0, damageEvents = 0, deathEvents = 0;
            v.HpChanged += () => hpEvents++; v.DamageResolved += _ => damageEvents++; v.Died += () => deathEvents++;
            check(Near(v.Heal(1000), 70) && Near(v.Hp, v.MaxHp) && hpEvents == 1,
                "Real EnemyVitals heal clamps to maximum and broadcasts one HP change");
            check(v.LifeRevision == revision && Near(v.Groggy.Value01, groggy) && v.WeakPointActive && Near(v.DamageMultiplier, multiplier) &&
                damageEvents == 0 && deathEvents == 0, "Healing preserves life revision, groggy and weak point without damage/death events");
            check(Near(v.Heal(1), 0) && Near(v.Heal(-1), 0) && Near(v.Heal(float.NaN), 0) && Near(v.Heal(float.PositiveInfinity), 0) && hpEvents == 1,
                "Full-health or invalid healing cannot generate HP events");
            v.TakeDamage(float.MaxValue); revision = v.LifeRevision; int eventsAtDeath = hpEvents;
            check(Near(v.Heal(100), 0) && !v.IsAlive && v.LifeRevision == revision && hpEvents == eventsAtDeath && deathEvents == 1,
                "Healing never resurrects dead EnemyVitals or repeats death");
        }

        private static void CheckAdapter(Scene scene, CombatConfigSO config, Action<bool, string> check)
        {
            var v = Enemy("GrowthBoss", scene, config);
            var attacker = Obj("PlayerOrSummon", scene);
            var growth = v.gameObject.AddComponent<CheongryongGrowthController>();
            double now = Time.timeAsDouble + 1000d;
            Set(growth, "_clock", (Func<double>)(() => now));
            Call(growth, "OnEnable");
            int interrupted = 0;
            growth.GrowthInterrupted += () => interrupted++;
            AttackProvenance Hit(Element e, DamageSource source = DamageSource.PlayerDirect) => AttackProvenance.Create(attacker, source, e);
            void Tick() => Call(growth, "Update");

            v.TakeDamage(50, Hit(Element.Metal));
            check(growth.IsWindingUp && Near(growth.WindupRemaining, 4) && interrupted == 0,
                "Real DamageResolved starts threshold attempt after initiating hit");
            now += 3.5; Tick(); v.TakeDamage(1, Hit(Element.Wood));
            check(growth.IsWindingUp && Near(growth.WindupRemaining, .5), "Non-Metal damage preserves active deadline");
            v.TakeDamage(0, Hit(Element.Metal));
            v.TakeDamage(1, new AttackProvenance(0, attacker, DamageSource.PlayerDirect, Element.Metal));
            v.TakeDamage(1, Hit(Element.Metal, DamageSource.Unknown));
            check(growth.IsWindingUp && interrupted == 0, "Zero damage and unconfirmed Metal provenance do not cancel growth");
            v.TakeDamage(1, Hit(Element.Metal));
            check(growth.WasInterrupted && interrupted == 1 && growth.AttemptUsed, "Confirmed direct Metal emits one interruption event");
            now += 20; Tick(); v.TakeDamage(1, Hit(Element.Metal));
            check(growth.WasInterrupted && interrupted == 1 && v.Hp < 50, "Repeated Metal and later ticks cannot repeat cancellation or heal");

            v.Restore();
            check(growth.CurrentState == CheongryongGrowthState.Ready && !growth.AttemptUsed, "Restore immediately resets subscribed adapter");
            v.TakeDamage(50, Hit(Element.Wood)); now += 1; v.TakeDamage(1, Hit(Element.Metal, DamageSource.Summon));
            check(growth.WasInterrupted && interrupted == 2, "Summon Metal uses the same confirmed-hit interruption path");

            v.Restore(); v.TakeDamage(50, Hit(Element.Wood));
            uint revision = v.LifeRevision;
            for (int i = 0; i < 10; i++) Tick();
            check(growth.IsWindingUp && Near(growth.WindupRemaining, 4) && Near(v.Hp, 50), "Adapter does not advance at frozen scaled time");
            v.AddParry(AttackProvenance.Create(v, DamageSource.Enemy, Element.Wood));
            float groggy = v.Groggy.Value01;
            now += 4; v.TakeDamage(1, Hit(Element.Metal));
            check(growth.IsPhaseTwo && Near(v.Hp, 100) && v.LifeRevision == revision && Near(v.Groggy.Value01, groggy) && interrupted == 2,
                "Deadline hit samples shared clock before cancellation; full heal enters phase two without combat reset");
            v.TakeDamage(60, Hit(Element.Wood)); now += 100; Tick();
            check(growth.IsPhaseTwo && Near(v.Hp, 40), "Phase-two threshold crossing cannot heal a second time");

            v.Restore(); v.TakeDamage(50, Hit(Element.Wood)); now += 4; v.TakeDamage(100, Hit(Element.Metal)); Tick();
            check(!v.IsAlive && growth.CurrentState == CheongryongGrowthState.Ended && interrupted == 2,
                "Lethal confirmed Metal at deadline cannot heal, revive, or count as interruption");
            v.Restore(); v.TakeDamage(50, Hit(Element.Wood));
            growth.enabled = false; Call(growth, "OnDisable"); now += 20;
            v.TakeDamage(1, Hit(Element.Metal)); Tick();
            check(growth.CurrentState == CheongryongGrowthState.Ended && Near(v.Hp, 49) && interrupted == 2,
                "Disabled controller cleans pending heal and damage subscriptions");
            growth.enabled = true; Call(growth, "OnEnable"); Tick();
            check(growth.CurrentState == CheongryongGrowthState.Ended, "Re-enabling controller alone does not re-arm the same life");
            growth.enabled = false; Call(growth, "OnDisable"); v.Restore();
            growth.enabled = true; Call(growth, "OnEnable"); v.TakeDamage(50, Hit(Element.Wood)); now += 1;
            v.TakeDamage(1, Hit(Element.Metal, DamageSource.Summon));
            check(growth.WasInterrupted && interrupted == 3, "Restore while controller dormant re-arms exactly one subscribed interruption on enable");

            v.Restore(); v.TakeDamage(50, Hit(Element.Wood)); v.enabled = false; now += 20; Tick();
            check(growth.CurrentState == CheongryongGrowthState.Ended && Near(v.Hp, 50), "Disabled vitals cannot receive a pending full heal");
            v.Restore(); Tick();
            check(growth.CurrentState == CheongryongGrowthState.Ended, "Restoring dormant vitals does not activate growth prematurely");
            v.enabled = true; Tick();
            check(growth.CurrentState == CheongryongGrowthState.Ready && !growth.AttemptUsed,
                "Campaign activation after dormant Restore arms the unconsumed life revision");
            v.TakeDamage(50, Hit(Element.Fire));
            check(growth.IsWindingUp, "First real hit after campaign activation starts growth normally");
        }

        private static EnemyVitals Enemy(string name, Scene scene, CombatConfigSO config)
        { var v = Obj(name, scene).AddComponent<EnemyVitals>(); Set(v, "_config", config); v.Restore(); return v; }
        private static GameObject Obj(string name, Scene scene)
        { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go; }
        private static bool Near(double a, double b) => Math.Abs(a - b) < .0001d;
        private static bool Throws(Action action) { try { action(); return false; } catch (ArgumentOutOfRangeException) { return true; } }
        private static void Set(object instance, string name, object value) =>
            instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
        private static void Call(object instance, string name) =>
            instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
    }
}
