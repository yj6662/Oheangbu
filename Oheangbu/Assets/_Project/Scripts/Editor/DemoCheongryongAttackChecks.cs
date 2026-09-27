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
    // Isolated real combat adapters + local physics. No Play mode, scene save, asset refresh or build.
    public static class DemoCheongryongAttackChecks
    {
        [Serializable] private sealed class ProbeReport { public List<string> samples = new List<string>(); }
        public static string RunProbe()
        {
            var report = new ProbeReport();
            foreach (CheongryongAttackKind kind in Enum.GetValues(typeof(CheongryongAttackKind)))
                using (var f = new Fixture())
                {
                    var plan = f.Begin(kind);
                    string Snapshot(string stage) => kind + " " + stage + " now=" + f.Now.ToString("R") + " start=" + plan.StartedAt.ToString("R") +
                        " release=" + plan.ReleaseAt.ToString("R") + " activeEnd=" + plan.ActiveEndAt.ToString("R") + " end=" + plan.EndAt.ToString("R") +
                        " origin=" + plan.Origin + " target=" + plan.TargetPoint + " player=" + f.Player.transform.position + " dir=" + plan.Direction +
                        " contains=" + plan.Contains(f.Player.transform.position) + " active=" + plan.IsActive + " consumed=" + plan.ContactConsumed +
                        " cancelled=" + plan.IsCancelled + " state=" + f.Combat.State + " hp=" + f.Hp + " hits=" + f.Hits.Count + " ends=" + f.Ends;
                    f.Combat.AttackImpactResolved += hit => report.samples.Add(kind + " IMPACT time=" + f.Now.ToString("R") + " contact=" + hit.Contact + " outcome=" + hit.Outcome + " damage=" + hit.AppliedDamage + " id=" + hit.Plan.Attack.AttackId);
                    report.samples.Add(Snapshot("began"));
                    f.Tick(plan.ReleaseAt - .01f); report.samples.Add(Snapshot("before release"));
                    Vector3 end = kind == CheongryongAttackKind.RootEruption ? plan.TargetPoint + Vector3.up * .8f : f.Player.transform.position + Vector3.up * .4f;
                    RaycastHit[] hits = null;
                    int count = ScenePhysicsQuery.RaycastAll(f.Scene, plan.Origin, end - plan.Origin, Vector3.Distance(plan.Origin, end), ~0, ref hits);
                    for (int i = 0; i < count; i++) report.samples.Add(kind + " ray hits " + hits[i].transform.name + " at " + hits[i].point + " distance=" + hits[i].distance);
                    f.Tick(plan.ReleaseAt + .01f); report.samples.Add(Snapshot("after release"));
                    f.Until(plan.ActiveEndAt + .01f); report.samples.Add(Snapshot("after active"));
                }
            return JsonUtility.ToJson(report, true);
        }

        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Manual boss attack readability and moving body/socket alignment",
                "Manual dodge and Metal parry timing with actual input", "Boss difficulty and phase-two cadence balance",
                "Integrated campaign encounter lifecycle, rewards, audio and 1080p performance" };
        }

        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run attack checks in Edit mode.");
            var report = new Report();
            void Check(bool pass, string name) => (pass ? report.passed : report.failed).Add(name);
            void Group(string label, Action action) { try { action(); } catch (Exception e) { report.failed.Add(label + ": " + e); } }
            Group("Profile and physics", () => CheckProfile(Check));
            foreach (CheongryongAttackKind kind in Enum.GetValues(typeof(CheongryongAttackKind)))
            {
                var attackKind = kind;
                foreach (int fps in new[] { 30, 60, 120 })
                {
                    int rate = fps;
                    Group(kind + " " + fps + " fps", () => CheckSchedule(attackKind, rate, 1f, Check));
                }
                Group(kind + " slowed", () => CheckSchedule(attackKind, 60, .15f, Check));
                Group(kind + " dodge", () => CheckDodge(attackKind, Check));
                Group(kind + " wall", () => CheckWall(attackKind, Check));
            }
            Group("Locked geometry and missed windows", () => CheckGeometry(Check));
            Group("Parry mapping and ownership", () => CheckParry(Check));
            Group("Cancellation and restoration", () => CheckCancellation(Check));
            Group("Growth coordination", () => CheckGrowth(Check));
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL";
            return JsonUtility.ToJson(report, true);
        }

        private static void CheckProfile(Action<bool, string> check)
        {
            using (var f = new Fixture())
            {
                check(f.Profile.TryValidate(out _), "Default four-attack TEST profile validates");
                f.Profile.ProjectileSpeed = float.NaN;
                check(!f.Profile.TryValidate(out _), "Non-finite projectile speed rejected");
                f.Profile.ProjectileSpeed = 10; f.Profile.PhaseTwoCooldownFactor = 0;
                check(!f.Profile.TryValidate(out _), "Zero phase-two interval factor rejected");
                check(f.Scene.GetPhysicsScene().IsValid() && !f.Scene.GetPhysicsScene().Equals(Physics.defaultPhysicsScene),
                    "Checks query a separate local physics world");
            }
        }

        private static void CheckSchedule(CheongryongAttackKind kind, int fps, float scale, Action<bool, string> check)
        {
            using (var f = new Fixture())
            {
                var plan = f.Begin(kind);
                float start = f.Now;
                for (int i = 0; i < 20; i++) f.Tick(start);
                check(f.Hits.Count == 0 && Near(plan.SampleTime, start), kind + ": frozen scaled time preserves telegraph (" + fps + "/" + scale + ")");
                f.Until(plan.ActiveEndAt + .01f, scale / fps);
                int contacts = 0; foreach (var hit in f.Hits) if (hit.Contact) contacts++;
                check(contacts == 1 && Near(f.Player.MaxHp - f.Hp, plan.Damage) && plan.ContactConsumed && !plan.IsCancelled && f.Hits[0].Plan == plan,
                    kind + ": exactly one real PlayerVitals hit at " + fps + " fps, scale " + scale);
                check(plan.Attack.AttackId > 0 && plan.Attack.Instigator == f.Enemy && plan.Attack.Source == DamageSource.Enemy &&
                    plan.Attack.Element == (kind >= CheongryongAttackKind.RootEruption ? Element.Wood : (Element?)null),
                    kind + ": real unique EnemyVitals-owned provenance and expected neutral/Wood element");
                float hp = f.Hp;
                for (int i = 0; i < 8; i++) f.Tick(f.Now);
                check(Near(hp, f.Hp) && f.Ends == 1, kind + ": repeated same-frame samples cannot repeat contact or cleanup");
            }
        }

        private static void CheckDodge(CheongryongAttackKind kind, Action<bool, string> check)
        {
            using (var f = new Fixture())
            {
                var plan = f.Begin(kind);
                f.Judge.RaiseGuard(Element.Metal, plan.ReleaseAt);
                var dodge = f.Player.gameObject.AddComponent<DodgeAction>(); Set(dodge, "_config", f.Config); Set(f.Player, "_dodge", dodge);
                check(dodge.TryDodge(Vector3.right) && dodge.IsInvulnerable, kind + ": real DodgeAction starts immunity");
                if (kind >= CheongryongAttackKind.RootEruption) f.Judge.ClearGuard();
                f.Until(plan.ActiveEndAt + .01f, .02f);
                check(Near(f.Hp, f.Player.MaxHp) && f.Hits.Count == 1 && f.Hits[0].Contact && Near(f.Hits[0].AppliedDamage, 0),
                    kind + ": actual PlayerVitals dodge suppresses damage");
                if (kind < CheongryongAttackKind.RootEruption)
                    check(f.Parries.Count == 0 && Near(f.Enemy.Groggy.Value01, 0), kind + ": neutral attack never queries guard or grants parry reward");
            }
        }

        private static void CheckWall(CheongryongAttackKind kind, Action<bool, string> check)
        {
            using (var f = new Fixture())
            {
                var plan = f.Begin(kind);
                Vector3 end = f.Player.transform.position + Vector3.up * .8f;
                f.Wall(Vector3.Lerp(plan.Origin, end, .6f));
                f.Judge.RaiseGuard(Element.Metal, plan.ReleaseAt);
                f.Until(plan.ActiveEndAt + .01f, .025f);
                check(Near(f.Hp, f.Player.MaxHp) && f.Parries.Count == 0, kind + ": actual scene wall inserted after windup blocks damage and parry rewards");
            }
        }

        private static void CheckGeometry(Action<bool, string> check)
        {
            using (var f = new Fixture())
            {
                var head = f.Object("Supplied mouth socket", new Vector3(0, 1, 2)).transform;
                f.Combat.ConfigureSockets(head, null);
                var plan = f.Begin(CheongryongAttackKind.HeadBite);
                Vector3 origin = plan.Origin, direction = plan.Direction;
                head.position += Vector3.right * 8; f.Player.transform.position += Vector3.right * 8; f.Sync();
                f.Until(plan.ActiveEndAt + .01f);
                check(plan.Origin == origin && plan.Direction == direction && Near(f.Hp, f.Player.MaxHp), "Head socket and player motion cannot retarget a locked bite");
            }
            foreach (var kind in new[] { CheongryongAttackKind.RootEruption, CheongryongAttackKind.WoodProjectile })
                using (var f = new Fixture())
                {
                    var plan = f.Begin(kind); Vector3 target = plan.TargetPoint;
                    f.Player.transform.position += Vector3.right * 5; f.Sync(); f.Until(plan.ActiveEndAt + .01f);
                    check(plan.TargetPoint == target && Near(f.Hp, f.Player.MaxHp), kind + ": current position recheck allows leaving the locked ground/ray");
                }
            using (var f = new Fixture())
            {
                var plan = f.Begin(CheongryongAttackKind.TailSweep);
                f.Player.transform.position = plan.Origin + Vector3.back * 3; f.Sync(); f.Until(plan.ActiveEndAt + .01f);
                check(Near(f.Hp, f.Player.MaxHp), "Tail remains a bounded broad sector; player behind the locked sector misses");
            }
            foreach (var kind in new[] { CheongryongAttackKind.HeadBite, CheongryongAttackKind.RootEruption, CheongryongAttackKind.WoodProjectile })
                using (var f = new Fixture())
                {
                    var plan = f.Begin(kind); f.Tick(plan.EndAt + .5f);
                    check(Near(f.Hp, f.Player.MaxHp) && f.Combat.CurrentPlan == null, kind + ": late Update beyond the full lifetime discards overdue damage");
                }
            using (var f = new Fixture())
            {
                var plan = f.Begin(CheongryongAttackKind.WoodProjectile);
                f.Tick(plan.ReleaseAt + 1f);
                check(Near(f.Player.MaxHp - f.Hp, plan.Damage), "One delayed projectile Update sweeps the travelled path instead of tunnelling through current player");
            }
            using (var f = new Fixture())
            {
                UnityEngine.Object.DestroyImmediate(f.Ground); f.Sync(); f.Prepare(CheongryongAttackKind.RootEruption);
                check(!f.Combat.TryBeginAttack(CheongryongAttackKind.RootEruption), "Root eruption requires actual grounded geometry in the actor scene");
            }
            using (var f = new Fixture())
            {
                f.Prepare(CheongryongAttackKind.WoodProjectile); f.Wall(new Vector3(0, 1, 4));
                check(!f.Combat.TryBeginAttack(CheongryongAttackKind.WoodProjectile), "Existing local wall prevents starting an attack through cover");
            }
        }

        private static void CheckParry(Action<bool, string> check)
        {
            foreach (var kind in new[] { CheongryongAttackKind.RootEruption, CheongryongAttackKind.WoodProjectile })
                foreach (var expected in new[] { ParryOutcome.Success, ParryOutcome.Half, ParryOutcome.Fail, ParryOutcome.Block, ParryOutcome.None })
                    using (var f = new Fixture())
                    {
                        Set(f.Config, "_halfParryDamageFactor", .3f); Set(f.Config, "_guardBlockFactor", .65f);
                        var plan = f.Begin(kind);
                        float contactAt = kind == CheongryongAttackKind.RootEruption ? plan.ReleaseAt : plan.ReleaseAt +
                            (Vector3.Dot(f.Player.transform.position + Vector3.up * .8f - plan.Origin, plan.Direction) - plan.Radius) / plan.Speed;
                        if (expected != ParryOutcome.None)
                            f.Judge.RaiseGuard(expected == ParryOutcome.Success ? Element.Metal : expected == ParryOutcome.Fail ? Element.Water : Element.Wood,
                                contactAt - (expected == ParryOutcome.Block ? 1.1f : .1f));
                        var other = f.Object("Unrelated enemy", Vector3.right * 10).AddComponent<EnemyVitals>(); other.Restore();
                        int rewards = 0;
                        f.Judge.OwnedImpactResolved += hit => { if (hit.Outcome == ParryOutcome.Success) { rewards++; f.Enemy.AddParry(hit.Attack); } };
                        f.Until(plan.ActiveEndAt + .01f);
                        float factor = expected == ParryOutcome.Success ? 0f : expected == ParryOutcome.Half ? .3f : expected == ParryOutcome.Block ? .65f : 1f;
                        check(f.Hits.Count == 1 && f.Hits[0].Outcome == expected && Near(f.Player.MaxHp - f.Hp, plan.Damage * factor),
                            kind + ": real ParryJudge " + expected + " uses existing CombatConfig damage factor");
                        int beforeReplay = f.Parries.Count;
                        f.Judge.ResolveImpact(Element.Wood, f.Now, Vector3.zero, plan.Attack);
                        check(beforeReplay == (expected == ParryOutcome.None ? 0 : 1) && f.Parries.Count == beforeReplay && rewards == (expected == ParryOutcome.Success ? 1 : 0) &&
                            Near(other.Groggy.Value01, 0f) && Near(f.Enemy.Groggy.Value01, expected == ParryOutcome.Success ? 1f / f.Config.ParriesToBlossom : 0f),
                            kind + ": " + expected + " reward remains once-only and belongs solely to the attacking EnemyVitals");
                    }
            using (var f = new Fixture())
            {
                Set(f.Config, "_parriesToBlossom", 1);
                var plan = f.Begin(CheongryongAttackKind.RootEruption);
                f.Judge.RaiseGuard(Element.Metal, plan.ReleaseAt - .1f);
                f.Judge.OwnedImpactResolved += hit => { if (hit.Outcome == ParryOutcome.Success) f.Enemy.AddParry(hit.Attack); };
                f.Tick(plan.ReleaseAt + .01f);
                check(f.Combat.State == CheongryongCombatState.Stunned && f.Combat.CurrentPlan == null && plan.IsCancelled && Near(f.Hp, f.Player.MaxHp),
                    "Synchronous owned parry -> weak point cancels the attack without being overwritten by recovery");
            }
        }

        private static void CheckCancellation(Action<bool, string> check)
        {
            foreach (string reason in new[] { "boss death", "player death", "disable", "leash", "AttackEnabled", "weak point", "life revision" })
                foreach (bool inFlight in new[] { false, true })
                    using (var f = new Fixture())
                    {
                        var plan = f.Begin(CheongryongAttackKind.WoodProjectile);
                        if (inFlight) f.Tick(plan.ReleaseAt + .1f);
                        switch (reason)
                        {
                            case "boss death": f.Enemy.TakeDamage(float.MaxValue); break;
                            case "player death": f.Player.ApplyFatalFall(); break;
                            case "disable": f.Combat.enabled = false; Call(f.Combat, "OnDisable"); break;
                            case "leash": f.Player.transform.position += Vector3.right * 50; f.Sync(); f.Tick(f.Now); break;
                            case "AttackEnabled": f.Combat.AttackEnabled = false; break;
                            case "weak point": f.Enemy.OpenWeakPoint(); break;
                            case "life revision": f.Enemy.Restore(); f.Tick(f.Now); break;
                        }
                        int hitCount = f.Hits.Count;
                        f.Tick(plan.ActiveEndAt + .01f);
                        check(plan.IsCancelled && f.Hits.Count == hitCount && f.CancelledEnds == 1,
                            reason + " during " + (inFlight ? "flight" : "windup") + " cancels pending damage and ends presentation once");
                        if (reason == "boss death")
                        {
                            f.Enemy.Restore(); f.Tick(f.Now);
                            check(f.Combat.State == CheongryongCombatState.Idle && f.Combat.CurrentPlan == null, "Restore life revision re-arms dead controller without resurrecting its old attack");
                        }
                    }
        }

        private static void CheckGrowth(Action<bool, string> check)
        {
            using (var f = new Fixture())
            {
                var growth = f.Enemy.gameObject.AddComponent<CheongryongGrowthController>();
                Set(growth, "_clock", (Func<double>)(() => f.Now)); Call(growth, "OnEnable");
                f.Combat.Configure(f.Player.transform, f.Player, f.Judge);
                var plan = f.Begin(CheongryongAttackKind.WoodProjectile);
                f.Enemy.TakeDamage(50, AttackProvenance.Create(f.Player, DamageSource.PlayerDirect, Element.Wood));
                check(growth.IsWindingUp && plan.IsCancelled && f.Combat.State == CheongryongCombatState.Growth, "Half-HP growth immediately cancels an already telegraphed attack");
                f.Now += 1f; Call(growth, "Update"); f.Tick(f.Now);
                check(!f.Combat.TryBeginAttack(CheongryongAttackKind.RootEruption) && f.Combat.CurrentPlan == null, "Four-second growth window suspends every attack");
                f.Enemy.TakeDamage(1, AttackProvenance.Create(f.Player, DamageSource.PlayerDirect, Element.Metal));
                check(growth.WasInterrupted && !f.Combat.PhaseTwoCadence && f.Combat.State == CheongryongCombatState.Recovery,
                    "Confirmed Metal cancellation prevents phase-two acceleration and gives a short recovery");
                float cancelledAt = f.Now;
                f.Now = cancelledAt + f.Profile.CancelRecoverySeconds - .01f;
                check(!f.Combat.TryBeginAttack(CheongryongAttackKind.RootEruption), "Growth cancellation recovery cannot be skipped by explicit attack requests");
                f.Now = cancelledAt + f.Profile.CancelRecoverySeconds + .01f;
                check(f.Combat.TryBeginAttack(CheongryongAttackKind.RootEruption), "Attack resumes after bounded growth cancellation recovery");
                var phaseTwoPlan = f.Combat.CurrentPlan; f.Until(phaseTwoPlan.ActiveEndAt + .01f);
                float readyAt = (float)Get(f.Combat, "_readyAt");
                check(Near(readyAt - f.LastEndedAt, phaseTwoPlan.EndAt - phaseTwoPlan.ActiveEndAt + f.Profile.CooldownSeconds),
                    "Interrupted growth retains phase-one cooldown and full telegraph");
                f.Enemy.Restore(); f.Tick(f.Now);
                f.Enemy.TakeDamage(50, AttackProvenance.Create(f.Player, DamageSource.PlayerDirect, Element.Wood));
                f.Now += 4f; Call(growth, "Update"); f.Tick(f.Now);
                check(growth.IsPhaseTwo && Near(f.Enemy.Hp, f.Enemy.MaxHp) && f.Combat.PhaseTwoCadence && f.Combat.CurrentPlan == null,
                    "Uninterrupted growth heals once and keeps combat in post-growth recovery");
                f.Now += f.Profile.CancelRecoverySeconds + .01f;
                check(f.Combat.TryBeginAttack(CheongryongAttackKind.RootEruption), "Phase-two attacks resume after growth recovery");
                phaseTwoPlan = f.Combat.CurrentPlan; f.Until(phaseTwoPlan.ActiveEndAt + .01f);
                readyAt = (float)Get(f.Combat, "_readyAt");
                check(Near(readyAt - f.LastEndedAt, phaseTwoPlan.EndAt - phaseTwoPlan.ActiveEndAt + f.Profile.CooldownSeconds * f.Profile.PhaseTwoCooldownFactor),
                    "Successful healing alone applies phase-two cooldown factor without shortening telegraph");
            }
        }

        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly CombatConfigSO Config;
            public readonly CheongryongCombatProfile Profile;
            public readonly EnemyVitals Enemy;
            public readonly PlayerVitals Player;
            public readonly ParryJudge Judge;
            public readonly CheongryongCombatController Combat;
            public readonly GameObject Ground;
            public readonly List<CheongryongAttackImpact> Hits = new List<CheongryongAttackImpact>();
            public readonly List<ParryImpactResult> Parries = new List<ParryImpactResult>();
            public float Now = Time.time + 100f;
            public float LastEndedAt;
            public int Ends, CancelledEnds;
            public float Hp => Player.Hp01 * Player.MaxHp;
            public Fixture()
            {
                Scene = EditorSceneManager.NewPreviewScene(); Config = ScriptableObject.CreateInstance<CombatConfigSO>();
                Profile = ScriptableObject.CreateInstance<CheongryongCombatProfile>();
                Set(Config, "_enemyMaxHp", 100f);
                Ground = Object("Local ground", new Vector3(0, -.5f, 0)); Ground.AddComponent<BoxCollider>().size = new Vector3(100, 1, 100);
                Enemy = Object("Cheongryong combat diagnostic", Vector3.zero).AddComponent<EnemyVitals>(); Set(Enemy, "_config", Config); Enemy.Restore();
                Player = Object("Real player vitals", new Vector3(0, 0, 8)).AddComponent<PlayerVitals>(); Set(Player, "_config", Config); Player.Restore();
                Judge = new ParryJudge(Config); Judge.OwnedImpactResolved += hit => Parries.Add(hit);
                Combat = Enemy.gameObject.AddComponent<CheongryongCombatController>(); Set(Combat, "_clock", (Func<float>)(() => Now));
                Combat.ConfigureProfile(Profile, Config); Combat.Configure(Player.transform, Player, Judge);
                Combat.AttackImpactResolved += hit => Hits.Add(hit);
                Combat.AttackEnded += (_, cancelled) => { Ends++; LastEndedAt = Now; if (cancelled) CancelledEnds++; };
                Sync();
            }
            public GameObject Object(string name, Vector3 position)
            { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, Scene); go.transform.position = position; return go; }
            public void Prepare(CheongryongAttackKind kind)
            {
                Player.transform.position = new Vector3(0, 0, kind == CheongryongAttackKind.HeadBite ? 4f : kind == CheongryongAttackKind.TailSweep ? 2f : 8f);
                Now += Profile.InitialDelay + Profile.CancelRecoverySeconds + .1f; Sync();
            }
            public CheongryongAttackPlan Begin(CheongryongAttackKind kind)
            { Prepare(kind); if (!Combat.TryBeginAttack(kind)) throw new InvalidOperationException("Could not begin " + kind); return Combat.CurrentPlan; }
            public void Tick(float now) { Now = now; Call(Combat, "Update"); }
            public void Until(float end, float step = .02f)
            { int count = 0; while (Now < end && count++ < 10000) Tick(Mathf.Min(end, Now + step)); if (count >= 10000) throw new InvalidOperationException("Unbounded diagnostic loop"); }
            public void Wall(Vector3 point)
            { var wall = Object("Local wall", point); wall.AddComponent<BoxCollider>().size = new Vector3(4, 4, .2f); Sync(); }
            public void Sync()
            { Physics.SyncTransforms(); var physics = Scene.GetPhysicsScene(); if (physics.IsValid() && !physics.Equals(Physics.defaultPhysicsScene)) physics.Simulate(.001f); }
            public void Dispose()
            { if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene); UnityEngine.Object.DestroyImmediate(Profile); UnityEngine.Object.DestroyImmediate(Config); }
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) < .004f;
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Call(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
