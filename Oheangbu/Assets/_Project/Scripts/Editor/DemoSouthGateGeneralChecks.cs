using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    public static class DemoSouthGateGeneralChecks
    {
        [Serializable] sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string scope = "Isolated locked plans, real EnemyVitals/PlayerVitals/ParryJudge and separate local physics. Test clock only; no live scene or save.";
            public string[] unverified = { "Scene authoring/navigation and death-reward connection", "Manual dodge/parry readability", "Weapon animation and VFX/SFX", "Actual stage difficulty and performance" };
        }
        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run isolated South Gate checks in Edit mode.");
            var r = new Report();
            void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); r.passed.Add(name); }
            void Group(string name, Action action) { try { action(); } catch (Exception e) { r.failed.Add(name + ": " + e); } }
            foreach (SouthGateAttackKind kind in Enum.GetValues(typeof(SouthGateAttackKind)))
            {
                foreach (int fps in new[] { 30, 60, 120 }) foreach (float scale in new[] { 1f, .15f })
                    Group(kind + " " + fps + "/" + scale, () =>
                    {
                        using (var f = new Fixture())
                        {
                            var p = f.Begin(kind); float hp = f.Hp;
                            for (int i = 0; i < 5; i++) f.Tick(f.Now);
                            Check(f.Hits.Count == 0, kind + ": frozen scaled clock does not release (" + fps + "/" + scale + ")");
                            f.Until(p.EndAt + .001f, scale / fps);
                            int count = kind == SouthGateAttackKind.NeutralEarthCombo ? 2 : 1;
                            Check(f.Hits.Count(h => h.Contact) == count && Near(hp - f.Hp, p.Pulses.Sum(h => h.Damage)),
                                kind + ": exactly " + count + " actual damage contact(s), " + fps + "/" + scale);
                            Check(p.Pulses.All(h => h.Consumed && h.Attack.AttackId > 0 && h.Attack.Instigator == f.Enemy && h.Attack.Source == DamageSource.Enemy) &&
                                p.Pulses.Select(h => h.Attack.AttackId).Distinct().Count() == count,
                                kind + ": unique real enemy-owned attack IDs per pulse");
                            float after = f.Hp; for (int i = 0; i < 5; i++) f.Tick(f.Now);
                            Check(Near(f.Hp, after) && f.Ends == 1, kind + ": repeated sample does not repay damage/end");
                        }
                    });
                Group(kind + " wall", () =>
                {
                    using (var f = new Fixture())
                    {
                        var p = f.Begin(kind); f.Wall(); f.Until(p.EndAt + .001f);
                        Check(Near(f.Hp, f.Player.MaxHp) && f.Hits.All(h => !h.Contact), kind + ": actual post-windup wall blocks every contact");
                    }
                });
                Group(kind + " skipped active window", () =>
                {
                    using (var f = new Fixture())
                    {
                        var p = f.Begin(kind); f.Tick(p.EndAt + .1f);
                        Check(Near(f.Hp, f.Player.MaxHp), kind + ": skipped whole attack cannot reconstruct late damage");
                    }
                });
            }
            foreach (DamageSource source in new[] { DamageSource.PlayerDirect, DamageSource.Summon })
                Group("Wood interruption " + source, () =>
                {
                    using (var f = new Fixture())
                    {
                        var p = f.Begin(SouthGateAttackKind.NeutralEarthCombo); f.Until(p.EmpowerStartAt + .1f);
                        int hits = f.Hits.Count(h => h.Contact); float hp = f.Hp, enemyHp = f.Enemy.Hp;
                        var attack = AttackProvenance.Create(f.Player, source, Element.Wood);
                        f.Enemy.TakeDamage(1, attack);
                        Check(f.Enemy.Hp < enemyHp && p.EmpowerInterrupted && p.Pulses[1].Cancelled && p.Pulses[0].Consumed && !p.IsCancelled,
                            source + ": actual applied Wood damage cancels only the Earth follow-up");
                        Check(f.Controller.CurrentPlan == null && f.Controller.CounterWindowActive && !f.Controller.CanNavigate && f.Interrupts == 1,
                            source + ": bounded counter opportunity stops attacks/navigation without opening a global weak point");
                        Check(!f.Enemy.WeakPointActive && Near(f.Enemy.DamageMultiplier, 1), source + ": existing groggy and damage multiplier rules unchanged");
                        f.Enemy.TakeDamage(1, attack); f.Tick(f.Now + f.Profile.CounterWindow * .5f);
                        Check(f.Interrupts == 1 && Near(hp, f.Hp) && f.Hits.Count(h => h.Contact) == hits &&
                            !f.Controller.TryBeginAttack(SouthGateAttackKind.Thrust), source + ": duplicate signal cannot restart counter window or release pending hit");
                    }
                });
            Group("Invalid damage and timing", () =>
            {
                using (var f = new Fixture())
                {
                    var p = f.Begin(SouthGateAttackKind.NeutralEarthCombo);
                    f.Enemy.TakeDamage(1, AttackProvenance.Create(f.Player, DamageSource.PlayerDirect, Element.Wood));
                    Check(!p.EmpowerInterrupted, "Wood before the actual strengthening window does not cancel combo");
                    f.Until(p.EmpowerStartAt + .1f);
                    f.Enemy.TakeDamage(0, AttackProvenance.Create(f.Player, DamageSource.PlayerDirect, Element.Wood));
                    f.Enemy.TakeDamage(1, AttackProvenance.Create(f.Player, DamageSource.PlayerDirect, Element.Fire));
                    f.Enemy.TakeDamage(1, AttackProvenance.Create(f.Player, DamageSource.Harvest, Element.Wood));
                    f.Enemy.TakeDamage(1, new AttackProvenance(0, f.Player, DamageSource.PlayerDirect, Element.Wood));
                    f.Enemy.TakeDamage(1, new AttackProvenance(12345, null, DamageSource.Summon, Element.Wood));
                    Check(!p.EmpowerInterrupted && f.Interrupts == 0, "Zero damage, wrong element/source, zero ID and missing owner cannot interrupt");
                    f.Until(p.EmpowerEndAt + .001f);
                    f.Enemy.TakeDamage(1, AttackProvenance.Create(f.Player, DamageSource.PlayerDirect, Element.Wood));
                    Check(!p.EmpowerInterrupted, "Wood after release cannot retroactively cancel an Earth attack");
                }
            });
            Group("Parry ownership", () =>
            {
                using (var f = new Fixture())
                {
                    var p = f.Begin(SouthGateAttackKind.EarthShockwave);
                    float arrival = p.Pulses[0].ReleaseAt + (f.Player.transform.position.z - p.Origin.z - p.Pulses[0].Width) / p.Pulses[0].Speed;
                    f.Judge.RaiseGuard(Element.Wood, arrival - .01f); f.Until(arrival + .05f, .001f);
                    Check(f.Hits.Any(h => h.Contact && h.Outcome == ParryOutcome.Success && h.AppliedDamage == 0) && f.Parries == 1,
                        "Earth wave uses actual Wood parry and emits one existing owned-impact event");
                }
                using (var f = new Fixture())
                {
                    var p = f.Begin(SouthGateAttackKind.Thrust); f.Judge.RaiseGuard(Element.Wood, p.Pulses[0].ReleaseAt); f.Until(p.EndAt + .001f);
                    Check(f.Parries == 0 && f.Hits.Any(h => h.Contact && h.Outcome == ParryOutcome.None), "Neutral weapon attack never enters elemental parry reward path");
                }
            });
            Group("Lifecycle cancellation", () =>
            {
                using (var f = new Fixture())
                {
                    var p = f.Begin(SouthGateAttackKind.NeutralEarthCombo); f.Enemy.TakeDamage(float.MaxValue, AttackProvenance.Create(f.Player, DamageSource.PlayerDirect, Element.Wood));
                    Check(p.IsCancelled && f.Controller.State == SouthGateCombatState.Dead && f.Interrupts == 0, "Lethal Wood cancels by real death, not a false counter opportunity");
                    f.Enemy.Restore(); f.Tick(f.Now);
                    Check(f.Controller.State != SouthGateCombatState.Dead && f.Controller.CurrentPlan == null, "New life resets pending attacks without controller restoring HP");
                    p = f.Begin(SouthGateAttackKind.Thrust); f.Controller.enabled = false;
                    // Non-ExecuteAlways MonoBehaviours do not receive Play lifecycle callbacks in this Edit preview.
                    Call(f.Controller, "OnDisable"); f.Tick(p.EndAt);
                    Check(p.IsCancelled && Near(f.Hp, f.Player.MaxHp), "Actual OnDisable handler cancels without pending damage (explicit Edit-preview lifecycle invocation)");
                }
                using (var f = new Fixture())
                {
                    var p = f.Begin(SouthGateAttackKind.NeutralEarthCombo); f.Enemy.OpenWeakPoint();
                    Check(p.IsCancelled && f.Controller.State == SouthGateCombatState.Stunned, "Existing actual weak-point signal cancels general attacks");
                }
            });
            Group("Existing dodge and encounter eligibility", () =>
            {
                using (var f = new Fixture())
                {
                    var p = f.Begin(SouthGateAttackKind.Thrust);
                    var dodge = f.Player.gameObject.AddComponent<DodgeAction>(); Set(dodge, "_config", f.Config); Set(f.Player, "_dodge", dodge);
                    Check(dodge.TryDodge(Vector3.right) && dodge.IsInvulnerable, "Existing DodgeAction supplies actual neutral-attack immunity");
                    f.Tick(p.Pulses[0].ReleaseAt + .001f);
                    Check(Near(f.Hp, f.Player.MaxHp) && f.Hits.Any(h => h.Contact && h.AppliedDamage == 0), "Controller preserves PlayerVitals dodge check; contact cannot bypass immunity");
                }
                using (var f = new Fixture())
                {
                    var p = f.Begin(SouthGateAttackKind.NeutralEarthCombo); f.Player.transform.position = Vector3.forward * 100; f.Tick(f.Now + .01f);
                    Check(p.IsCancelled && f.Controller.CurrentPlan == null && Near(f.Hp, f.Player.MaxHp), "Leaving authored encounter range cancels pending combo");
                }
                using (var f = new Fixture())
                {
                    var p = f.Begin(SouthGateAttackKind.EarthShockwave); f.Player.TakeDamage(float.MaxValue);
                    Check(p.IsCancelled && f.Controller.CurrentPlan == null, "Actual player death immediately cancels pending general attack");
                }
            });
            Group("Pure locked geometry/profile", () =>
            {
                using (var f = new Fixture())
                {
                    var p = f.Begin(SouthGateAttackKind.NeutralEarthCombo); float damage = p.Pulses[1].Damage;
                    f.Profile.ComboEarthDamage = 100; f.Profile.WaveRange = 1; f.Player.transform.position = new Vector3(10, 0, 1);
                    Check(Near(p.Pulses[1].Damage, damage) && Near(p.Pulses[1].Range, 14) && p.Direction == Vector3.forward,
                        "Plan copies tuning and aim once; mutable player/profile cannot retarget it");
                    Check(!p.Contains(p.Pulses[0], p.Origin - p.Direction) && !p.Contains(p.Pulses[1], p.Origin + Vector3.up * 10),
                        "Behind-weapon and elevated points are outside locked volume");
                    f.Profile.WaveSpeed = float.NaN; Check(!f.Profile.TryValidate(out _), "Nonfinite profile values rejected");
                }
            });
            r.status = r.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(r, true);
        }
        sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly CombatConfigSO Config;
            public readonly SouthGateGeneralProfile Profile;
            public readonly EnemyVitals Enemy;
            public readonly PlayerVitals Player;
            public readonly ParryJudge Judge;
            public readonly SouthGateGeneralController Controller;
            public readonly List<SouthGateGeneralImpact> Hits = new List<SouthGateGeneralImpact>();
            public int Ends, Interrupts, Parries;
            public float Now = Time.time + 100;
            public float Hp => Player.Hp01 * Player.MaxHp;
            public Fixture()
            {
                Scene = EditorSceneManager.NewPreviewScene(); Config = ScriptableObject.CreateInstance<CombatConfigSO>();
                Profile = ScriptableObject.CreateInstance<SouthGateGeneralProfile>(); Set(Config, "_enemyMaxHp", 200f);
                var floor = Create("isolated ground", new Vector3(0, -.5f, 0)); floor.AddComponent<BoxCollider>().size = new Vector3(50, 1, 50);
                Enemy = Create("isolated general", Vector3.zero).AddComponent<EnemyVitals>(); Set(Enemy, "_config", Config); Enemy.Restore();
                Player = Create("actual player life", new Vector3(0, 0, 2)).AddComponent<PlayerVitals>(); Set(Player, "_config", Config); Player.Restore();
                Judge = new ParryJudge(Config); Judge.OwnedImpactResolved += _ => Parries++;
                Controller = Enemy.gameObject.AddComponent<SouthGateGeneralController>(); Set(Controller, "_clock", (Func<float>)(() => Now));
                Controller.ConfigureProfile(Profile, Config); Controller.Configure(Player.transform, Player, Judge);
                Controller.AttackImpactResolved += hit => Hits.Add(hit); Controller.AttackEnded += (_, __) => Ends++; Controller.EmpowerInterrupted += _ => Interrupts++;
                Sync();
            }
            GameObject Create(string name, Vector3 position)
            { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, Scene); go.transform.position = position; return go; }
            public SouthGateGeneralAttackPlan Begin(SouthGateAttackKind kind)
            {
                Player.transform.position = new Vector3(0, 0, kind == SouthGateAttackKind.EarthShockwave ? 7 : 2);
                Now += Profile.InitialDelay + Profile.CancelRecovery + .1f; Sync();
                if (!Controller.TryBeginAttack(kind)) throw new InvalidOperationException("Cannot begin " + kind);
                return Controller.CurrentPlan;
            }
            public void Tick(float time) { Now = time; Call(Controller, "Update"); }
            public void Until(float end, float step = .01f)
            { int count = 0; while (Now < end && count++ < 20000) Tick(Mathf.Min(end, Now + step)); if (count >= 20000) throw new InvalidOperationException("Unbounded test clock"); }
            public void Wall() { var wall = Create("actual local wall", new Vector3(0, 1.5f, 1.1f)); wall.AddComponent<BoxCollider>().size = new Vector3(5, 3, .2f); Sync(); }
            void Sync() { Physics.SyncTransforms(); var physics = Scene.GetPhysicsScene(); if (!physics.IsValid() || physics.Equals(Physics.defaultPhysicsScene)) throw new InvalidOperationException("Local physics required"); physics.Simulate(.001f); }
            public void Dispose() { if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene); UnityEngine.Object.DestroyImmediate(Profile); UnityEngine.Object.DestroyImmediate(Config); }
        }
        static bool Near(float a, float b) => Mathf.Abs(a - b) < .01f;
        static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        static void Call(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
