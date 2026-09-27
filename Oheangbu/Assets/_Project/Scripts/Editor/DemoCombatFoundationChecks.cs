using System;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    // Isolated Edit-mode integration checks. No Play transition, active-scene save, capture, or build.
    public static class DemoCombatFoundationChecks
    {
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>();
            public List<string> failed = new List<string>();
            public string[] unverified = { "Actual multi-enemy Game View input", "Parry VFX/audio timing", "Lock-on HUD pixels", "Boss/summon AI" };
        }

        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run these isolated checks in Edit mode.");
            var report = new Report();
            Scene scene = EditorSceneManager.NewPreviewScene();
            CombatConfigSO config = ScriptableObject.CreateInstance<CombatConfigSO>();
            try
            {
                Set(config, "_enemyMaxHp", 1000f);
                var player = Obj("Player", scene);
                var a = Enemy("A", scene, config, out var ca);
                var b = Enemy("B", scene, config, out var cb);
                var foreign = Enemy("OtherEncounter", scene, config, out _);
                var wiring = Obj("Wiring", scene).AddComponent<CombatLoopWiring>();
                Set(wiring, "_config", config); Set(wiring, "_enemies", new[] { a, b });
                Call(wiring, "CollectControllers");
                var judge = new ParryJudge(config);
                var global = new GroggyMeter(config);
                wiring.Construct(null, judge, global, null);
                // Invoke the production ownership handler via the real judge, excluding cosmetic spawns.
                judge.OwnedImpactResolved += result => Call(wiring, "ApplyParryProgress", result);
                ca.Init(judge); cb.Init(judge);
                int emitted = 0; judge.OwnedImpactResolved += _ => emitted++;

                AttackProvenance Hit(EnemyVitals source) => AttackProvenance.Create(source, DamageSource.Enemy, Element.Fire);
                void Parry(EnemyVitals source)
                { judge.RaiseGuard(Element.Water, 100f); judge.ResolveImpact(Element.Fire, 100.1f, Vector3.zero, Hit(source)); }
                void Check(bool ok, string name) { (ok ? report.passed : report.failed).Add(name); }
                void Reset() { wiring.ResetEncounterGroggy(); }
                AttackProvenance Direct(Element e, DamageSource source = DamageSource.PlayerDirect) =>
                    AttackProvenance.Create(player, source, e);

                Parry(a); Parry(b);
                Check(Near(a.Groggy.Value01, 1f / 3f) && Near(b.Groggy.Value01, 1f / 3f), "Parries accumulate separately per attacker");
                Parry(a); Parry(a);
                Check(ca.IsStunned && a.WeakPointActive && !cb.IsStunned && !b.WeakPointActive, "Only blossomed attacker enters stun");
                Check(Near(a.DamageMultiplier, 1.5f) && Near(b.DamageMultiplier, 1f), "Damage boost is target-local");
                int blossoms = 0; a.Groggy.Blossomed += () => blossoms++;
                Parry(a); Check(blossoms == 0, "Active window cannot reopen or extend from further parries");
                a.ExpireWeakPoint(Time.time + config.BlossomStunDuration + .01f);
                Check(!ca.IsStunned && !a.WeakPointActive && Near(a.Groggy.Value01, 0f) && Near(b.Groggy.Value01, 1f / 3f), "Window expiry resets only its owner and ends stun");

                Reset(); var duplicate = Hit(a);
                judge.RaiseGuard(Element.Water, 100); judge.ResolveImpact(Element.Fire, 100.1f, Vector3.zero, duplicate);
                int once = emitted;
                judge.RaiseGuard(Element.Water, 100); judge.ResolveImpact(Element.Fire, 100.1f, Vector3.zero, duplicate);
                Check(emitted == once && Near(a.Groggy.Value01, 1f / 3f), "Duplicate attack ID emits no duplicate reward signal");
                judge.ResolveImpact(Element.Fire, 100.1f, Vector3.zero, Hit(a));
                Check(Near(a.Groggy.Value01, 2f / 3f), "Duplicate signal does not consume the replacement guard");
                Reset(); Parry(foreign);
                Check(Near(a.Groggy.Value01, 0) && Near(b.Groggy.Value01, 0) && Near(foreign.Groggy.Value01, 0), "Foreign encounter attacker is not credited");
                judge.RaiseGuard(Element.Water, 100); judge.ResolveImpact(Element.Fire, 100.1f, Vector3.zero);
                Check(Near(a.Groggy.Value01, 0) && Near(b.Groggy.Value01, 0), "Ownerless legacy diagnostic never guesses an enemy");
                foreach (var guard in new[] { Element.Fire, Element.Wood })
                { judge.RaiseGuard(guard, 100); judge.ResolveImpact(Element.Fire, 100.1f, Vector3.zero, Hit(a)); }
                judge.RaiseGuard(Element.Water, 100); judge.ResolveImpact(Element.Fire, 101f, Vector3.zero, Hit(a));
                Check(Near(a.Groggy.Value01, 0), "Half, Fail and Block grant no groggy");

                Parry(a); Parry(b); b.TakeDamage(float.MaxValue);
                Check(!b.IsAlive && Near(b.Groggy.Value01, 0) && Near(a.Groggy.Value01, 1f / 3f), "Enemy death clears only dead owner");
                uint deadRevision = b.LifeRevision; b.Restore();
                Check(b.IsAlive && b.LifeRevision != deadRevision && !b.WeakPointActive, "Restore invalidates pending casts through life revision");
                Reset(); a.OpenWeakPoint(); b.OpenWeakPoint();
                // Same signal used by legacy session death/rest reset.
                Call(wiring, "TryHookServices"); global.Reset(); Call(wiring, "UnhookServices");
                Check(!a.WeakPointActive && !b.WeakPointActive && !ca.IsStunned && !cb.IsStunned, "Legacy DI encounter reset closes all local windows");

                a.Restore(); a.OpenWeakPoint();
                foreach (Element e in Enum.GetValues(typeof(Element))) a.TakeDamage(1, Direct(e, DamageSource.Summon));
                a.TakeDamage(1, Direct(Element.Wood, DamageSource.Harvest), true);
                a.TakeDamage(1); a.TakeDamage(0, Direct(Element.Water));
                Check(a.WeakPointElementMask == 0 && !a.CompletionAwarded, "Summon, harvest, unknown and zero damage excluded from completion");
                var wood = Direct(Element.Wood); a.TakeDamage(1, wood); a.TakeDamage(1, wood);
                Check(a.WeakPointElementMask == 1 && !a.CompletionAwarded, "Repeated element and attack ID cannot fake five-element completion");
                a.TakeDamage(1, Direct(Element.Metal)); a.TakeDamage(1, Direct(Element.Water)); a.TakeDamage(1, Direct(Element.Fire));
                var complete = a.TakeDamage(1, Direct(Element.Earth));
                Check(a.CompletionAwarded && Near(complete.CompletionBonus, 30f) && Near(complete.AppliedDamage, 31.5f), "Free-order fifth direct element awards one 30 damage TEST bonus");
                var repeated = a.TakeDamage(1, Direct(Element.Wood));
                Check(Near(repeated.CompletionBonus, 0) && Near(repeated.AppliedDamage, 1.5f), "Completion bonus cannot repeat within window");
                Check(b.WeakPointElementMask == 0, "Completion progress is target-local");
                a.ExpireWeakPoint(Time.time + 5f); var outside = a.TakeDamage(1, Direct(Element.Water));
                Check(a.WeakPointElementMask == 0 && !a.CompletionAwarded && Near(outside.AppliedDamage, 1f), "Expired window removes boost and completion progress");
                a.OpenWeakPoint(); a.TakeDamage(1, Direct(Element.Fire)); a.TakeDamage(float.MaxValue);
                Check(!ca.IsStunned && !a.WeakPointActive && a.WeakPointElementMask == 0, "Death during weak point removes combat rewards");

                a.Restore(); var lockOn = player.AddComponent<LockOn>(); Set(wiring, "_lockOn", lockOn);
                Set(lockOn, "_locked", true); Set(lockOn, "_target", b);
                Check(wiring.GroggyHudTarget == b, "HUD selects current lock-on enemy");
                Set(lockOn, "_locked", false); Check(wiring.GroggyHudTarget == null, "Unlocked HUD does not display a global meter");
                // Real Schedule -> PendingCast -> EnemyVitals route, without cosmetic contact prefabs.
                Set(wiring, "_contactVfx", null);
                var plan = new CastPlan { Cast = new SpellCast('가', SpellKind.AttackSingle, Element.Wood, 2f, default, 1f) };
                wiring.PlayerDamageScale = _ => 2f;
                int resolvedDamage = 0; EnemyDamageResult latest = default;
                wiring.EnemyDamageResolved += r => { resolvedDamage++; latest = r; };
                float beforeHit = a.Hp;
                Call(wiring, "Schedule", plan, a, Time.time - 1f, 2f);
                Call(wiring, "TickPendingCasts"); Call(wiring, "TickPendingCasts");
                Check(Near(plan.Hits[0].Power, 4f) && Near(beforeHit - a.Hp, 4f) && resolvedDamage == 1,
                    "Scheduled direct hit applies upgrade once and does not repeat on next tick");
                Check(latest.Attack.Source == DamageSource.PlayerDirect && latest.Attack.AttackId > 0 && latest.Target == a,
                    "Actual scheduled damage result preserves player source, target and attack ID");
                Call(wiring, "Schedule", plan, a, Time.time - 1f, 2f); a.Restore(); beforeHit = a.Hp;
                Call(wiring, "TickPendingCasts");
                Check(Near(a.Hp, beforeHit) && resolvedDamage == 1, "Pre-restore scheduled hit cannot damage restored encounter");
                Call(wiring, "Schedule", plan, a, Time.time - 1f, 2f); a.TakeDamage(float.MaxValue);
                Call(wiring, "TickPendingCasts");
                Check(resolvedDamage == 1, "Target killed in flight produces no scheduled hit result");
                Check(config.ParriesToBlossom == 3 && Near(config.BlossomStunDuration, 4) && Near(config.BlossomDamageMultiplier, 1.5f), "Existing default parry count, duration and multiplier preserved");
                CheckResourceUpgrades(scene, config, Check);
            }
            catch (Exception e) { report.failed.Add(e.ToString()); }
            finally { EditorSceneManager.ClosePreviewScene(scene); UnityEngine.Object.DestroyImmediate(config); }
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL";
            return JsonUtility.ToJson(report, true);
        }

        private static void CheckResourceUpgrades(Scene scene, CombatConfigSO config, Action<bool, string> check)
        {
            float originalStart = config.InkStart, originalMaxHp = config.PlayerMaxHp;
            var baseInk = new InkPool(config, null);
            int baseIncomeEvents = 0; float baseIncome = 0;
            baseInk.Gained += amount => { baseIncomeEvents++; baseIncome += amount; };
            check(Near(baseInk.CapacityMultiplier, 1) && Near(baseInk.Value, originalStart), "Unupgraded ink starts with original capacity and fill");
            baseInk.Restore(1);
            check(baseInk.TrySpend(config.SpellInkCost) && Near(baseInk.Value, 1 - config.SpellInkCost), "Unupgraded spell cost remains original normalized amount");
            baseInk.Gain(.1f);
            check(Near(baseInk.Value, 1 - config.SpellInkCost + .1f) && Near(baseIncome, .1f) && baseIncomeEvents == 1,
                "Unupgraded gain and positive-income event retain original amount");
            baseInk.SpendClamped(10);
            check(Near(baseInk.Value, 0) && !baseInk.TrySpend(.01f) && Near(baseInk.Value, 0) && baseIncomeEvents == 1,
                "Empty ink refuses costs without false income or negative balance");

            foreach (float scale in new[] { 1.1f, 1.2f })
            {
                string label = scale < 1.15f ? "Ink upgrade 10%: " : "Ink upgrade 20%: ";
                var ink = new InkPool(config, null);
                int gains = 0; float received = 0;
                ink.Gained += amount => { gains++; received += amount; };
                ink.Restore(.6f); ink.SetCapacityMultiplier(scale); ink.Broadcast();
                check(Near(ink.Value, .6f) && Near(ink.CapacityMultiplier, scale) && gains == 0,
                    label + "upgrade, restore and broadcast preserve fill without acquisition feedback");
                float spendFraction = config.SpellInkCost / scale;
                bool spent = ink.TrySpend(config.SpellInkCost);
                check(spent && Near(ink.Value, .6f - spendFraction) && gains == 0,
                    label + "same spell consumes base cost divided by new capacity");
                ink.Gain(.12f);
                check(gains == 1 && Near(received, .12f / scale) && Near(ink.Value, .6f - spendFraction + .12f / scale),
                    label + "same physical income scales its HUD fill and Gained amount together");
                float savedFraction = ink.Value;
                var reloaded = new InkPool(config, null); int reloadGains = 0;
                reloaded.Gained += _ => reloadGains++;
                reloaded.SetCapacityMultiplier(scale); reloaded.Restore(savedFraction);
                check(Near(reloaded.Value, savedFraction) && reloadGains == 0 &&
                    reloaded.TrySpend(config.SpellInkCost) && Near(reloaded.Value, savedFraction - spendFraction),
                    label + "normalized save reload preserves fill and upgraded next-cast cost");
                ink.Restore(.03f); int beforeClamp = gains;
                ink.SpendClamped(.12f);
                check(Near(ink.Value, 0) && gains == beforeClamp, label + "clamped misfire spends remaining ink without income");
                ink.Restore(.99f); received = 0; gains = 0;
                ink.Gain(.24f); ink.Gain(.24f);
                check(Near(ink.Value, 1) && gains == 1 && Near(received, .01f),
                    label + "overflow emits only actual added fraction and full pool emits nothing");
                ink.Restore(.4f); gains = 0; received = 0;
                ink.Gain(0); ink.Gain(-1); ink.SpendClamped(-1);
                check(Near(ink.Value, .4f) && gains == 0, label + "zero or negative income/cost cannot manufacture resources");
            }
            var progressiveInk = new InkPool(config, null); progressiveInk.Restore(.5f);
            int upgradeIncome = 0; progressiveInk.Gained += _ => upgradeIncome++;
            progressiveInk.SetCapacityMultiplier(1.1f); progressiveInk.SetCapacityMultiplier(1.2f); progressiveInk.SetCapacityMultiplier(1.2f);
            check(Near(progressiveInk.CapacityMultiplier, 1.2f) && Near(progressiveInk.Value, .5f) && upgradeIncome == 0,
                "Consecutive ink upgrades and repeated apply use absolute 1.2 multiplier, never compound or signal gain");

            var health = Obj("UpgradeHealth", scene).AddComponent<PlayerVitals>(); Set(health, "_config", config);
            int damageEvents = 0, deaths = 0, hpChanges = 0; float damageReported = 0;
            health.Damaged += amount => { damageEvents++; damageReported += amount; };
            health.Died += () => deaths++; health.HpChanged += () => hpChanges++;
            health.Restore();
            check(Near(health.MaxHp, originalMaxHp) && Near(health.Hp01, 1) && damageEvents == 0 && deaths == 0,
                "Unupgraded health restores original maximum without damage or death events");
            check(health.TakeDamage(20) && Near(health.Hp01, .8f) && Near(damageReported, 20) && damageEvents == 1,
                "Unupgraded direct damage remains 20 HP from original 100 HP");
            health.Restore(.5f); damageEvents = 0; damageReported = 0; int changesBeforeUpgrade = hpChanges;
            health.SetMaxHpMultiplier(1.1f);
            check(Near(health.MaxHp, 110f) && Near(health.Hp01, .5f) && Near(health.Hp01 * health.MaxHp, 55f) &&
                hpChanges == changesBeforeUpgrade + 1 && damageEvents == 0 && deaths == 0,
                "Health 10% upgrade preserves saved fill, sets 110 maximum and 55 current HP, broadcasts only HP change");
            health.SetMaxHpMultiplier(1.2f); health.SetMaxHpMultiplier(1.2f);
            check(Near(health.MaxHp, 120f) && Near(health.Hp01, .5f) && Near(health.Hp01 * health.MaxHp, 60f) && damageEvents == 0,
                "Health 20% and repeated apply use absolute 120 maximum and preserve 50% fill");
            check(health.TakeDamage(12f) && Near(health.Hp01, .4f) && Near(health.Hp01 * health.MaxHp, 48f) &&
                damageEvents == 1 && Near(damageReported, 12f), "Upgraded health takes physical damage once, normalized against upgraded maximum");
            float savedHealthFraction = health.Hp01;
            var reloadedHealth = Obj("ReloadedUpgradeHealth", scene).AddComponent<PlayerVitals>(); Set(reloadedHealth, "_config", config);
            int reloadDamage = 0, reloadDeaths = 0;
            reloadedHealth.Damaged += _ => reloadDamage++; reloadedHealth.Died += () => reloadDeaths++;
            reloadedHealth.SetMaxHpMultiplier(1.2f); reloadedHealth.Restore(savedHealthFraction);
            check(Near(reloadedHealth.MaxHp, 120f) && Near(reloadedHealth.Hp01, savedHealthFraction) &&
                Near(reloadedHealth.MaxHp * reloadedHealth.Hp01, 48f) && reloadDamage == 0 && reloadDeaths == 0,
                "Health save reload preserves normalized fill and upgraded absolute HP without damage feedback");
            health.Restore();
            check(Near(health.MaxHp, 120f) && Near(health.Hp01, 1) && damageEvents == 1 && deaths == 0,
                "Rest heals to upgraded maximum without damage or death signals");
            // 100f * 1.2f may be 120.000008f; use the actual maximum for an exact lethal fixture.
            health.TakeDamage(health.MaxHp); health.SetMaxHpMultiplier(1.1f); health.SetMaxHpMultiplier(1.2f);
            check(Near(health.Hp01, 0) && deaths == 1 && damageEvents == 2 && !health.TakeDamage(1),
                "Upgrade application cannot revive a dead player or duplicate death notification");
            health.Restore(.25f);
            check(Near(health.MaxHp, 120f) && Near(health.Hp01, .25f) && Near(health.MaxHp * health.Hp01, 30f) && deaths == 1,
                "Respawn restore uses upgraded maximum and keeps restored normalized fill");
            check(Near(config.PlayerMaxHp, originalMaxHp) && Near(config.InkStart, originalStart),
                "Runtime capacity and health upgrades never mutate shared CombatConfig values");
        }

        private static EnemyVitals Enemy(string name, Scene scene, CombatConfigSO config, out EnemyController controller)
        {
            var go = Obj(name, scene); var vitals = go.AddComponent<EnemyVitals>(); Set(vitals, "_config", config); vitals.Restore();
            controller = go.AddComponent<EnemyController>(); Set(controller, "_config", config); Set(controller, "_vitals", vitals);
            controller.Init(null); return vitals;
        }
        private static GameObject Obj(string name, Scene scene)
        { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go; }
        private static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;
        private static void Set(object instance, string name, object value) =>
            instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
        private static object Call(object instance, string name, params object[] args) =>
            instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, args);
    }
}
