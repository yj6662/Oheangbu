using System;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;
using static Oheangbu.EditorTools.Vol2.Vol2DefenceFixtureSupport;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-b slice 2 (D308-149, design sections 5 and 9) edit-mode fixture of the vol.2 defence side: payment, parry, posture and window,
    /// 갈무리, 걱, and the wiring hooks. In memory only (HideAndDontSave objects, a hand-driven clock, no scene, no asset, no Play). Queue:
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2DefenceFixture Run "all"
    ///   K  payment: every one of the 120 letters pays its table cost (parry 0, 걱 26) BEFORE CastAccepted; short ink refuses; gate exception refuses
    ///   P  parry: success / wrong body / neutral / projectile / unknown attack / ink 0 / debt once / window boundary 499,999 vs 500,000 us /
    ///      cooldown 0.8 s / short telegraph / duplicate impact / legacy judge untouched
    ///   G  posture: 40 (parry) 60 (active seed) 10 (blade), window 2 s x 0.6^(n-1) and a bundle of at most 5 s, reset after 6 s, new life,
    ///      the old step meter never fed, the riposte's TryConsumeWeakPointWindow still works
    ///   M  갈무리: damage 0.3 and +4 ink, 12 / 24 per target for the session (scene reload, LifeRevision, loaded save), HP floor 1, window B, carries
    ///   R  걱: 30 HP over 3 s, ink 26, cooldown 20 s, out-of-combat budget 30 per rest, combat gate (window, once per encounter / phase), hit interrupts
    ///   W  wiring: bind / lazy bind / no ink = no kit / Legacy = no kit, parry rewards skipped, telegraph cue recorded, hit hook, swing entries
    /// Not covered (Play): real enemy controllers raising AttackTelegraphed, the contact collector of slice 3, the lab with the kit bound.</summary>
    public static class Vol2DefenceFixture
    {
        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-defence-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-defence-fixture: editor busy (play / compile / import)";
            var r = new Vol2FixtureReport();
            var made = new List<Object>();
            try { RunAll(r, made); } catch (Exception e) { Check(r, "EXCEPTION", false, e.ToString()); }
            finally { foreach (var o in made) if (o != null) Object.DestroyImmediate(o); }
            return JsonUtility.ToJson(r, true);
        }

        static char ParryLetter(Body b) => GlyphRulesV3.Encode(b, Frame.Attachment, Verb.Once);
        static AttackProvenance EnemyAttack(EnemyVitals attacker, Element? el) => AttackProvenance.Create(attacker, DamageSource.Enemy, el);
        static void Tell(Vol2DefenceRig rig, AttackProvenance a, EnemyAttackDelivery d, float seconds) => rig.Kit.Telegraphs.Record(new EnemyAttackCue(a, d, Vector3.zero, seconds, 1f));
        static ParryOutcome Impact(Vol2DefenceRig rig, AttackProvenance a, Element? judgeElement = null)
            => rig.Judge.ResolveImpact(judgeElement ?? a.Element ?? Element.Wood, 0f, Vector3.zero, a);

        static void RunAll(Vol2FixtureReport r, List<Object> made)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "S0", book.Ok, book.Ok ? "rulebook loaded (" + book.Spells.Count + " spells)" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            Payment(r, made, book);
            Integration(r, made, book);
            Parry(r, made, book);
            Posture(r, made, book);
            Gather(r, made, book);
            Recovery(r, made, book);
            Wiring(r, made, book);
        }

        // ---------------------------------------------------------------- K: payment
        static void Payment(Vol2FixtureReport r, List<Object> made, Vol2Rulebook book)
        {
            var rig = new Vol2DefenceRig(made, book);
            // K1: a peak letter pays its cost, and the ink is already spent when CastAccepted is raised
            decimal seenAtAccept = -1m; int acceptedEvents = 0;
            rig.Adapter.CastAccepted += d => { acceptedEvents++; seenAtAccept = rig.InkUnits; };
            var d1 = rig.Cast('각');
            Check(r, "K1", d1.Accepted && d1.Plan != null && Near(d1.Plan.Cost, 24m) && Near(rig.InkUnits, 76m, 0.05m) && acceptedEvents == 1 && Near(seenAtAccept, 76m, 0.05m),
                "각 (peak, cost " + (d1.Plan != null ? d1.Plan.Cost : -1) + "): ink 100 -> " + rig.InkUnits + "; at the moment CastAccepted was raised the ink was already " + seenAtAccept);

            // K2: all 120 rows pay exactly their table cost
            int paidRight = 0, checkedRows = 0; var wrong = new List<string>();
            foreach (var row in book.Spells)
            {
                rig.Kit.ResetForRow();
                var d = rig.Cast(row.Letter); checkedRows++;
                decimal spent = rig.Kit.LastPayment.InkSpent;
                if (d.Accepted && Near(spent, row.Cost, 0.05m)) paidRight++; else if (wrong.Count < 6) wrong.Add(row.Letter + ":" + (d.Accepted ? "spent " + spent + " cost " + row.Cost : "refused " + d.Detail));
            }
            Check(r, "K2", checkedRows == 120 && paidRight == 120, "all 120 letters accepted and charged exactly the table cost (parry 0, 걱 26): " + paidRight + "/" + checkedRows + (wrong.Count > 0 ? " first problems " + string.Join("; ", wrong) : ""));

            // K3: short ink refuses, takes nothing, issues no CastId, raises no CastAccepted
            var rig3 = new Vol2DefenceRig(made, book);
            rig3.Ink.Restore(0.10f); int accepts3 = 0; rig3.Adapter.CastAccepted += d => accepts3++;
            var d3 = rig3.Cast('가');   // bare ㅏ, cost 11
            Check(r, "K3", !d3.Accepted && d3.Reason == Vol2CastRejectReason.PaymentRefused && d3.Detail.StartsWith("InkShort", StringComparison.Ordinal) && !d3.CastId.IsValid && d3.Plan == null && accepts3 == 0 &&
                Near(rig3.InkUnits, 10m, 0.05m) && rig3.Kit.PaymentsRefused == 1, "10 ink for a cost-11 letter: refused (" + d3.Detail + "), nothing taken (" + rig3.InkUnits + "), no CastId, no CastAccepted");

            // K4: a letter outside the vocabulary pays nothing and never reaches the gate
            var rig4 = new Vol2DefenceRig(made, book);
            var d4 = rig4.Cast('A'); var d4b = rig4.Cast('힣');
            Check(r, "K4", !d4.Accepted && d4.Reason == Vol2CastRejectReason.NotInVocabulary && !d4b.Accepted && rig4.Kit.PaymentsMade == 0 && rig4.Kit.PaymentsRefused == 0 && Near(rig4.InkUnits, 100m), "outside the 120: refused before the gate, no ink touched");

            // K5: a gate that throws is a refusal, never a free accepted cast
            var thrower = new Vol2GlyphAdapter(book.Numbers, null, new ManualVol2Clock(1), new Vol2IdIssuer(9)) { Payment = _ => throw new InvalidOperationException("fixture gate failure") };
            Vol2CastDecision d5;
            using (new LogSilencer()) d5 = thrower.Receive('각');
            Check(r, "K5", !d5.Accepted && d5.Reason == Vol2CastRejectReason.PaymentRefused && d5.Detail.Contains("payment gate threw") && thrower.AcceptedCount == 0, "gate exception -> refusal: " + d5.Detail);

            // K6: no gate = slice 1 behaviour
            var free = new Vol2DefenceRig(made, book, withPayment: false);
            var d6 = free.Cast('각');
            Check(r, "K6", d6.Accepted && Near(free.InkUnits, 100m), "no Payment set: accepted, nothing spent (slice 1)");

            // K7: capacity x2 still costs 24 points
            var rig7 = new Vol2DefenceRig(made, book); rig7.Ink.SetCapacityMultiplier(2f);
            var d7 = rig7.Cast('각');
            Check(r, "K7", d7.Accepted && Near(rig7.InkUnits, 176m, 0.1m), "capacity x2: 200 points, the peak costs 24 -> " + rig7.InkUnits);
        }

        sealed class LogSilencer : IDisposable
        {
            readonly bool _before; public LogSilencer() { _before = Debug.unityLogger.logEnabled; Debug.unityLogger.logEnabled = false; }
            public void Dispose() { Debug.unityLogger.logEnabled = _before; }
        }

        // ---------------------------------------------------------------- I: slice 3's coordinator with the real ports
        sealed class IntCaster : IVol2Caster { public Vector3 Position => Vector3.zero; public Vector3 AimDirection => Vector3.forward; public Transform Anchor => null; }
        sealed class IntNoWall : IVol2Obstruction { public bool TryBlock(Vector3 from, Vector3 to, out float distance) { distance = 0f; return false; } }

        static void Integration(Vol2FixtureReport r, List<Object> made, Vol2Rulebook book)
        {
            // one cast of the bare ㅏ (wood, cost 11, 0.6 units) through the REAL adapter, gate, coordinator, shape, budget and the kit's two ports
            for (int variant = 0; variant < 3; variant++)
            {
                double now = 0;
                var services = Vol2Installer.Build(SpellRuntimeMode.Vol2, book, _ => true, () => now);
                var enemy = NewEnemy(made, "I_e" + variant);
                enemy.transform.position = new Vector3(0f, 0f, 5f);
                if (variant == 1) { enemy.OpenWeakPointFor(2f); }
                if (variant == 2) { enemy.OpenWeakPointFor(2f); enemy.SetAttackElementSource308(new Vol2FakeAttackElement { Element = Element.Earth }); }
                var registry = new Vol2TargetRegistry { ExtrasOnly = true }; registry.AddExtra(enemy);
                var runtime = Vol2ShapeRuntime.Create(services, registry, new IntCaster(), new IntNoWall(), null, null, null);
                var ink = new InkPool(null, null);
                var pgo = new GameObject("I_player" + variant) { hideFlags = HideFlags.HideAndDontSave }; made.Add(pgo);
                var player = pgo.AddComponent<PlayerVitals>(); player.Restore(0.5f);
                var kit = new Vol2DefenceKit(book.Numbers, services.Clock, ink, player, null, new Vol2FakeContext(), 10f);
                services.Route.Adapter.Payment = kit.TryPay;
                runtime.Coordinator.Ink = kit.PaidInk; runtime.Coordinator.Damage = kit.DamageSink;
                var d = services.Route.Adapter.Receive('가');
                for (int i = 0; i < 60; i++) { now += 0.05; runtime.Coordinator.Tick(0.05f); }
                Vol2CastLog log = null; if (d.Accepted) runtime.Coordinator.TryGetLog(d.CastId, out log);
                // wood instant 0.6 units x HpPerUnit 10; window B 1.5; counter K 1.25 (wood overcomes the earth attack body, inside the window)
                double expectedLoss = variant == 0 ? 6.0 : variant == 1 ? 9.0 : 11.25;
                double loss = 60.0 - enemy.Hp;
                Check(r, "I" + (variant + 1), d.Accepted && log != null && log.ContactCount == 1 && Near(loss, expectedLoss, 0.01) && Near(ink.Value * 100.0, 89.0, 0.05) && Near(log.InkPaid, 11m) &&
                    kit.PaidInk.AlreadyPaid == 1 && kit.PaidInk.PaidHere == 0 && kit.DamageSink.Hits == 1 && kit.DamageSink.LastTerms.Window == (variant > 0) && kit.DamageSink.LastTerms.Counter == (variant == 2),
                    (variant == 0 ? "no window" : variant == 1 ? "window open" : "window open + counter body") + ": enemy lost " + loss + " HP (expected " + expectedLoss + "), ink charged once (pool " + (ink.Value * 100f) + "), terms " + (kit.DamageSink.Hits > 0 ? kit.DamageSink.LastTerms.ToString() : "none"));
                if (variant == 0)
                {
                    // refused for ink: no cast log, no shape, no damage
                    ink.Restore(0.05f); var enemy2 = NewEnemy(made, "I_e_short"); enemy2.transform.position = new Vector3(0f, 0f, 5f); registry.AddExtra(enemy2);
                    var d2 = services.Route.Adapter.Receive('가');
                    for (int i = 0; i < 40; i++) { now += 0.05; runtime.Coordinator.Tick(0.05f); }
                    Check(r, "I4", !d2.Accepted && d2.Reason == Vol2CastRejectReason.PaymentRefused && runtime.Coordinator.ActiveCount == 0 && Near(enemy2.Hp, 60.0), "5 ink for an 11-cost cast: refused at the gate, no shape, no damage");
                    // the spend of the gate is never repeated by the coordinator, and a cast the gate did not pay is paid by the port, never free
                    var spender = kit.PaidInk; ink.Restore(1f);
                    bool ok = spender.TrySpend(new CastId(9999), 11m, out decimal paid);
                    Check(r, "I5", ok && paid == 11m && spender.PaidHere == 1 && Near(ink.Value * 100.0, 89.0, 0.05), "a cast the gate never paid is charged by the port once (11), not given free");
                }
                runtime.Dispose();
            }
        }

        // ---------------------------------------------------------------- P: parry
        static void Parry(Vol2FixtureReport r, List<Object> made, Vol2Rulebook book)
        {
            var fire = ParryLetter(Body.Fire);
            // P1: correct body (fire parry vs a metal attack), long telegraph
            {
                var rig = new Vol2DefenceRig(made, book); var att = NewEnemy(made, "P1_att");
                int owned = 0, observed = 0, gained = 0; rig.Judge.OwnedImpactResolved += _ => owned++; rig.Judge.ImpactResolved += (o, e, p) => observed++; rig.Ink.Gained += _ => gained++;
                var d = rig.Cast(fire); rig.Advance(300000);
                var a = EnemyAttack(att, Element.Metal); Tell(rig, a, EnemyAttackDelivery.MeleeArc, 1.0f);
                var o2 = Impact(rig, a);
                Check(r, "P1", d.Accepted && d.IsParry && d.Plan.Cost == 0m && o2 == ParryOutcome.Success && rig.Kit.Parry.Last.Defended && rig.Kit.Parry.Last.Posture == 40m && Near(rig.Kit.Posture.PostureOf(att), 40m) &&
                    Near(att.Groggy.Value01, 0.4) && !rig.Kit.Parry.Pending && Near(rig.InkUnits, 100m) && owned == 1 && observed == 1,
                    "fire parry vs metal, telegraph 1.0 s: outcome " + o2 + ", posture " + rig.Kit.Posture.PostureOf(att) + ", HUD meter " + att.Groggy.Value01 + ", ink " + rig.InkUnits + " (no cost, no refund), contact observations " + owned + "/" + observed);
                Check(r, "P1b", gained == 0 && !att.Groggy.IsBlossomed, "no ink income from a parry (gained events " + gained + "); the old step meter not advanced");
                var again = Impact(rig, a);
                Check(r, "P9", again == ParryOutcome.Success && Near(rig.Kit.Posture.PostureOf(att), 40m) && owned == 1, "the same attack asked again: the judge answers its first verdict, posture stays 40, one observation");
            }
            // P2: wrong body defends, no posture
            {
                var rig = new Vol2DefenceRig(made, book); var att = NewEnemy(made, "P2_att");
                rig.Cast(fire); rig.Advance(100000);
                var a = EnemyAttack(att, Element.Wood); Tell(rig, a, EnemyAttackDelivery.MeleeArc, 1.0f);
                var o2 = Impact(rig, a);
                Check(r, "P2", o2 == ParryOutcome.Success && Near(rig.Kit.Posture.PostureOf(att), 0m) && !att.Groggy.HasExternal && rig.Kit.Parry.Last.Defended && rig.Kit.Parry.Last.Posture == 0m,
                    "fire parry vs wood: still defended (all damage voided), posture " + rig.Kit.Posture.PostureOf(att));
            }
            // P3: neutral attack defends, no posture
            {
                var rig = new Vol2DefenceRig(made, book); var att = NewEnemy(made, "P3_att");
                rig.Cast(fire); rig.Advance(100000);
                var a = EnemyAttack(att, null); Tell(rig, a, EnemyAttackDelivery.MeleeArc, 1.0f);
                var o2 = Impact(rig, a, Element.Metal);
                Check(r, "P3", o2 == ParryOutcome.Success && Near(rig.Kit.Posture.PostureOf(att), 0m) && !rig.Kit.Parry.Last.AttackBody.HasValue, "neutral attack: defended, no body, no posture");
            }
            // P4: a projectile is not defended and does not use the attempt up
            {
                var rig = new Vol2DefenceRig(made, book); var att = NewEnemy(made, "P4_att");
                rig.Cast(fire); rig.Advance(100000);
                var shot = EnemyAttack(att, Element.Metal); Tell(rig, shot, EnemyAttackDelivery.AimedProjectile, 1.0f);
                var o1 = Impact(rig, shot);
                bool stillPending = rig.Kit.Parry.Pending;
                var melee = EnemyAttack(att, Element.Metal); Tell(rig, melee, EnemyAttackDelivery.MeleeArc, 1.0f);
                var o2 = Impact(rig, melee);
                Check(r, "P4", o1 == ParryOutcome.None && stillPending && o2 == ParryOutcome.Success && Near(rig.Kit.Posture.PostureOf(att), 40m), "projectile: lands (" + o1 + "), attempt still pending; the melee hit after it is defended (" + o2 + ")");
            }
            // P5: an attack without a cue is unknown -> treated as a projectile
            {
                var rig = new Vol2DefenceRig(made, book); var att = NewEnemy(made, "P5_att");
                rig.Cast(fire); rig.Advance(100000);
                var o1 = Impact(rig, EnemyAttack(att, Element.Metal));
                Check(r, "P5", o1 == ParryOutcome.None && rig.Kit.Parry.UnknownAttacks == 1 && rig.Kit.Parry.Last.UnknownAttack && rig.Kit.Parry.Last.Projectile, "no AttackTelegraphed cue: counted as unknown, treated as a projectile (never defended)");
            }
            // P6: ink 0 allowed; the debt is min(ink, 14), once, no refund
            {
                var rig = new Vol2DefenceRig(made, book);
                rig.Ink.Restore(0f);
                var d = rig.Cast(fire);
                rig.Advance(600000); rig.Kit.Tick();
                Check(r, "P6a", d.Accepted && Near(rig.InkUnits, 0m) && !rig.Kit.Parry.Pending && rig.Kit.Parry.Last.Kind == "Timeout" && rig.Kit.Parry.Last.InkDebt == 0m, "ink 0: the parry is still accepted; its failed window costs min(0, 14) = 0");
                var rig2 = new Vol2DefenceRig(made, book); rig2.Ink.Restore(0.40f);
                rig2.Cast(fire); rig2.Advance(499999); rig2.Kit.Tick(); bool insideStillPending = rig2.Kit.Parry.Pending && Near(rig2.InkUnits, 40m, 0.05m);
                rig2.Advance(1); rig2.Kit.Tick(); decimal afterWindow = rig2.InkUnits;
                rig2.Advance(1000000); rig2.Kit.Tick();
                Check(r, "P6b", insideStillPending && Near(afterWindow, 26m, 0.05m) && Near(rig2.InkUnits, 26m, 0.05m) && rig2.Kit.Parry.Last.Settled, "ink 40: nothing owed at 499,999 us, at 500,000 us the debt 14 is taken once -> " + afterWindow + ", unchanged after (" + rig2.InkUnits + ")");
            }
            // P7: the window boundary on the impact
            {
                var rig = new Vol2DefenceRig(made, book); var att = NewEnemy(made, "P7_att");
                rig.Cast(fire); rig.Advance(499999);
                var a1 = EnemyAttack(att, Element.Metal); Tell(rig, a1, EnemyAttackDelivery.MeleeArc, 1.0f);
                var in1 = Impact(rig, a1);
                rig.Kit.Parry.Reset();
                rig.Cast(fire); rig.Advance(500000);
                var a2 = EnemyAttack(att, Element.Metal); Tell(rig, a2, EnemyAttackDelivery.MeleeArc, 1.0f);
                var in2 = Impact(rig, a2);
                Check(r, "P7", in1 == ParryOutcome.Success && in2 == ParryOutcome.None && Near(rig.InkUnits, 86m, 0.05m) && rig.Kit.Parry.Last.Reason == "Failed" && rig.Kit.Parry.Last.Settled,
                    "impact at 499,999 us after the cast: " + in1 + "; at 500,000 us: " + in2 + " with the debt 14 (ink " + rig.InkUnits + ")");
            }
            // P8: cooldown 0.8 s from the START of the previous attempt, and the unsettled one is paid at the next begin
            {
                var rig = new Vol2DefenceRig(made, book);
                var first = rig.Cast(fire); rig.Advance(799999);
                int accepts = 0; rig.Adapter.CastAccepted += d => accepts++;
                var tooSoon = rig.Cast(ParryLetter(Body.Wood));
                rig.Advance(1);
                var onTime = rig.Cast(ParryLetter(Body.Wood));
                Check(r, "P8", first.Accepted && !tooSoon.Accepted && tooSoon.Reason == Vol2CastRejectReason.PaymentRefused && tooSoon.Detail == "Parry:Cooldown" && !tooSoon.CastId.IsValid && onTime.Accepted && accepts == 1 &&
                    Near(rig.InkUnits, 86m, 0.05m), "second parry at +799,999 us: " + tooSoon.Detail + "; at +800,000 us: accepted, and the first attempt (never hit) was paid then: ink " + rig.InkUnits);
            }
            // P10: a short telegraph defends with the right body but gives no posture
            {
                var rig = new Vol2DefenceRig(made, book); var att = NewEnemy(made, "P10_att");
                rig.Cast(fire); rig.Advance(100000);
                var a = EnemyAttack(att, Element.Metal); Tell(rig, a, EnemyAttackDelivery.MeleeArc, 0.5f);
                var o1 = Impact(rig, a);
                Check(r, "P10", o1 == ParryOutcome.Success && Near(rig.Kit.Posture.PostureOf(att), 0m) && rig.Kit.Parry.Last.TelegraphUs == 500000, "telegraph 0.5 s (< 0.8 s): defended, no posture");
            }
            // P11: the legacy judge, untouched; and a vol.2 judge ignores the old guard
            {
                var legacy = new ParryJudge(null); legacy.RaiseGuard(Element.Fire, 0f);
                var o1 = legacy.ResolveImpact(Element.Metal, 0.1f, Vector3.zero, default);
                var vol2 = new Vol2DefenceRig(made, book); vol2.Judge.RaiseGuard(Element.Fire, 0f);
                var o2 = vol2.Judge.ResolveImpact(Element.Metal, 0.1f, Vector3.zero, default);
                Check(r, "P11", legacy.Vol2Resolver == null && o1 == ParryOutcome.Success && o2 == ParryOutcome.None, "legacy judge: old guard defends (" + o1 + "); a vol.2 judge never reads the old guard (" + o2 + ")");
            }
            // P12: an impact after the window with no Tick in between settles the failure once
            {
                var rig = new Vol2DefenceRig(made, book); var att = NewEnemy(made, "P12_att");
                rig.Cast(fire); rig.Advance(900000);
                var a = EnemyAttack(att, Element.Metal); Tell(rig, a, EnemyAttackDelivery.MeleeArc, 1.0f);
                var o1 = Impact(rig, a); rig.Kit.Tick();
                Check(r, "P12", o1 == ParryOutcome.None && Near(rig.InkUnits, 86m, 0.05m) && !rig.Kit.Parry.Pending, "late impact: lands, debt 14 once (ink " + rig.InkUnits + "), a later Tick owes nothing more");
            }
        }

        // ---------------------------------------------------------------- G: posture
        static void Posture(Vol2FixtureReport r, List<Object> made, Vol2Rulebook book)
        {
            var fire = ParryLetter(Body.Fire);
            var seed = GlyphRulesV3.Encode(Body.Fire, Frame.Path, Verb.Seed);
            var blade = GlyphRulesV3.Encode(Body.Fire, Frame.Attachment, Verb.Split);
            var rig = new Vol2DefenceRig(made, book); var att = NewEnemy(made, "G1_att");
            int opened = 0; att.WeakPointOpened += () => opened++;
            // G1: three correct parries open the window
            Vol2PostureRecord last = default;
            for (int i = 0; i < 3; i++)
            {
                rig.Cast(fire); rig.Advance(300000);
                var a = EnemyAttack(att, Element.Metal); Tell(rig, a, EnemyAttackDelivery.MeleeArc, 1.0f);
                Impact(rig, a); last = rig.Kit.Posture.Last;
                rig.Advance(500000);
            }
            Check(r, "G1", last.Opened && last.WindowUs == 2000000 && last.OpenedOnVitals && att.WeakPointActive && att.WeakPointGeneration == 1 && opened == 1 && Near(att.Groggy.Value01, 1.0) && !att.Groggy.IsBlossomed,
                "40 + 40 + 40: window of " + last.WindowUs + " us opened on the enemy (generation " + att.WeakPointGeneration + "), HUD meter " + att.Groggy.Value01 + ", old step meter not blossomed");
            // G7: the riposte's narrow close still works on this window
            bool consumed = att.TryConsumeWeakPointWindow(att.LifeRevision, att.WeakPointGeneration);
            Check(r, "G7", consumed && !att.WeakPointActive && !att.Groggy.HasExternal && Near(att.Groggy.Value01, 0.0), "TryConsumeWeakPointWindow(life, generation) closes the vol.2 window and clears the meter");

            // G2: the window shrinks 2 s x 0.6^(n-1), a bundle never passes 5 s
            var t = NewEnemy(made, "G2_t"); long end = rig.Clock.NowUs; var lengths = new List<long>(); decimal total = 0m;
            for (int n = 0; n < 12; n++)
            {
                rig.Advance(1);   // strictly after the previous window's end
                rig.Kit.Posture.ApplyActiveSeed(t, seed, true, true);
                var rec = rig.Kit.Posture.ApplyActiveSeed(t, seed, true, true);
                if (!rec.Opened) { lengths.Add(0); break; }
                lengths.Add(rec.WindowUs); total = rec.TotalWindowSeconds;
                rig.Advance(rec.WindowUs); t.ExpireWeakPoint(Time.time + 100f);
            }
            Check(r, "G2a", lengths.Count >= 3 && lengths[0] == 2000000 && lengths[1] == 1200000 && lengths[2] == 720000 && lengths[3] == 432000, "window lengths " + string.Join(", ", lengths) + " us (2 s x 0.6^(n-1))");
            Check(r, "G2b", total <= 5m && total > 4.9m, "twelve windows add up to " + total + " s, never past 5 s");

            // G3: after 6 s without an opening the count starts again
            var t3 = NewEnemy(made, "G3_t");
            rig.Kit.Posture.ApplyActiveSeed(t3, seed, true, true); var w1 = rig.Kit.Posture.ApplyActiveSeed(t3, seed, true, true);
            rig.Advance(w1.WindowUs); t3.ExpireWeakPoint(Time.time + 100f);
            rig.Advance(5000000); rig.Kit.Posture.ApplyActiveSeed(t3, seed, true, true); var w2 = rig.Kit.Posture.ApplyActiveSeed(t3, seed, true, true);
            rig.Advance(w2.WindowUs); t3.ExpireWeakPoint(Time.time + 100f);
            rig.Advance(6000000); rig.Kit.Posture.ApplyActiveSeed(t3, seed, true, true); var w3 = rig.Kit.Posture.ApplyActiveSeed(t3, seed, true, true);
            Check(r, "G3", w1.WindowUs == 2000000 && w2.WindowUs == 1200000 && w3.WindowUs == 2000000 && w3.WindowNumber == 1, "5 s after a window the next one is " + w2.WindowUs + " us (n=2); after 6 s it is " + w3.WindowUs + " us again (n=" + w3.WindowNumber + ")");

            // G4 / G5: the other two sources
            var s = NewEnemy(made, "G4_s");
            var b1 = rig.Kit.Posture.ApplyBlade(s, blade, true, true, true);
            var b2 = rig.Kit.Posture.ApplyBlade(s, blade, true, true, false);
            var a1 = rig.Kit.Posture.ApplyActiveSeed(s, seed, true, true);
            var a2 = rig.Kit.Posture.ApplyActiveSeed(s, seed, false, true);
            Check(r, "G4", b1.Added == 10m && b2.Added == 0m && a1.Added == 60m && a2.Added == 0m && Near(rig.Kit.Posture.PostureOf(s), 70m), "blade +10 (only with the counter), active seed +60 (only direct and own): posture " + rig.Kit.Posture.PostureOf(s));
            Check(r, "G4b", Near(s.Groggy.Value01, 0.7) && !s.Groggy.IsBlossomed, "HUD meter shows posture/100 = " + s.Groggy.Value01);

            // G8 / G9: a new life starts clean; the dead take no posture
            var l = NewEnemy(made, "G8_l");
            rig.Kit.Posture.ApplyParry(l); l.Restore();
            var afterRestore = rig.Kit.Posture.ApplyParry(l);
            Check(r, "G8", Near(afterRestore.PostureAfter, 40m) && afterRestore.LifeRevision == l.LifeRevision, "after Restore (new LifeRevision " + l.LifeRevision + ") the posture is " + afterRestore.PostureAfter + ", not 80");
            l.TakeResolvedVol2Damage(1000f, new AttackProvenance(99001, null, DamageSource.PlayerDirect, null));
            var dead = rig.Kit.Posture.ApplyParry(l);
            Check(r, "G9", !dead.Accepted && dead.Reason == "NoTarget", "a dead enemy takes no posture");
            // G10: resetting the encounter
            var e10 = NewEnemy(made, "G10_e"); rig.Kit.Posture.ApplyParry(e10); rig.Kit.Posture.Reset();
            Check(r, "G10", Near(rig.Kit.Posture.PostureOf(e10), 0m), "Posture.Reset clears every target");
        }

        // ---------------------------------------------------------------- M: 갈무리
        static void Gather(Vol2FixtureReport r, List<Object> made, Vol2Rulebook book)
        {
            long sw = 0;
            SwingId Next() => new SwingId(++sw);
            // M1: one swing
            {
                var rig = new Vol2DefenceRig(made, book); rig.Ink.Restore(0.5f); var e = NewEnemy(made, "M1_e");
                EnemyDamageResult seen = default; e.DamageResolved += x => seen = x;
                var rec = rig.Kit.Gather.OnSwingFirstHit(e, Next(), e);
                Check(r, "M1", rec.Accepted && rec.Reason == "Gathered" && Near(rec.DamageUnits, 0.3m) && Near(rec.AppliedHp, 3.0) && Near(e.Hp, 57.0) && Near(rec.InkGained, 4m, 0.05m) && Near(rig.InkUnits, 54m, 0.1m),
                    "first swing: " + rec + " | enemy HP " + e.Hp + ", ink " + rig.InkUnits);
                Check(r, "M8", seen.Attack.Source == DamageSource.Harvest && seen.Attack.NoCredit && seen.AppliedDamage > 0f, "the hit is a Harvest-source attack with NoCredit set");
                var again = rig.Kit.Gather.OnSwingFirstHit(e, new SwingId(sw), e);
                Check(r, "M9b", !again.Accepted && again.Reason == "FirstEnemyOnly", "the same swing meeting a second enemy: " + again.Reason);
            }
            // M2: per-target caps, 12 normal / 24 boss, and the swing cooldown 1.1 s
            {
                var rig = new Vol2DefenceRig(made, book); rig.Ink.Restore(0.2f); var e = NewEnemy(made, "M2_e");
                var gains = new List<decimal>(); var cooled = rig.Kit.Gather.OnSwingFirstHit(e, Next(), e); gains.Add(cooled.InkGained);
                rig.Advance(500000); var early = rig.Kit.Gather.OnSwingFirstHit(e, Next(), e);
                for (int i = 0; i < 4; i++) { rig.Advance(1200000); gains.Add(rig.Kit.Gather.OnSwingFirstHit(e, Next(), e).InkGained); }
                Check(r, "M2a", !early.Accepted && early.Reason == "CooldownOrOldSwing" && Near(gains[0], 4m, 0.05m) && Near(gains[1], 4m, 0.05m) && Near(gains[2], 4m, 0.05m) && Near(gains[3], 0m) && Near(gains[4], 0m),
                    "normal enemy: swing at +0.5 s refused (" + early.Reason + "); gains " + string.Join(", ", gains) + " (4 + 4 + 4 = 12, then none)");
                var boss = NewEnemy(made, "M2_boss", 400f, true); var bg = new List<decimal>(); rig.Ink.Restore(0.1f);
                for (int i = 0; i < 8; i++) { rig.Advance(1200000); bg.Add(rig.Kit.Gather.OnSwingFirstHit(boss, Next(), boss).InkGained); }
                decimal sum = 0m; foreach (var g in bg) sum += g;
                Check(r, "M2b", Near(sum, 24m, 0.1m) && Near(bg[5], 4m, 0.05m) && Near(bg[6], 0m), "boss: " + string.Join(", ", bg) + " (24 in total, six hits)");
            }
            // M3: the HP floor
            {
                var rig = new Vol2DefenceRig(made, book); var e = NewEnemy(made, "M3_e", 1.5f);
                var r1 = rig.Kit.Gather.OnSwingFirstHit(e, Next(), e); rig.Advance(1200000);
                var r2 = rig.Kit.Gather.OnSwingFirstHit(e, Next(), e);
                Check(r, "M3", Near(e.Hp, 1.0) && e.IsAlive && Near(r1.AppliedHp, 0.5) && Near(r2.AppliedHp, 0.0) && r2.Accepted, "HP 1.5: first swing takes " + r1.AppliedHp + " (to the floor), second takes " + r2.AppliedHp + ", the enemy lives at " + e.Hp);
                var low = NewEnemy(made, "M3_low", 60f); low.TakeResolvedVol2Damage(59.5f, new AttackProvenance(98001, null, DamageSource.PlayerDirect, null));
                rig.Advance(1200000); var r3 = rig.Kit.Gather.OnSwingFirstHit(low, Next(), low);
                Check(r, "M3b", Near(low.Hp, 0.5) && low.IsAlive && r3.AppliedHp == 0f && r3.Accepted, "HP 0.5 (below the floor): the swing takes nothing and never kills (HP " + low.Hp + ")");
            }
            // M4 / M5: the ledger belongs to the session
            {
                var rig = new Vol2DefenceRig(made, book); rig.Ink.Restore(0.1f);
                var area = new GameObject("M4_Area") { hideFlags = HideFlags.HideAndDontSave }; made.Add(area);
                var wolf = NewEnemy(made, "Wolf", 60f, false, 0f, area.transform);
                decimal got = 0m;
                for (int i = 0; i < 3; i++) { rig.Advance(1200000); got += rig.Kit.Gather.OnSwingFirstHit(wolf, Next(), wolf).InkGained; }
                long stable = rig.Kit.Targets.StableId(wolf);
                Object.DestroyImmediate(wolf.gameObject);
                var reloaded = NewEnemy(made, "Wolf", 60f, false, 0f, area.transform);
                rig.Advance(1200000); var after = rig.Kit.Gather.OnSwingFirstHit(reloaded, Next(), reloaded);
                Check(r, "M4", Near(got, 12m, 0.1m) && rig.Kit.Targets.StableId(reloaded) == stable && Near(after.InkGained, 0m), "the same enemy after a scene reload (new object, same scene path): stable id " + stable + " kept, budget still spent -> +" + after.InkGained);
                reloaded.Restore(); rig.Advance(1200000); var revived = rig.Kit.Gather.OnSwingFirstHit(reloaded, Next(), reloaded);
                Check(r, "M4b", Near(revived.InkGained, 0m) && reloaded.LifeRevision >= 2, "a new LifeRevision (" + reloaded.LifeRevision + ") does not refill it");
                var other = NewEnemy(made, "Wolf", 60f, false, 0f, area.transform);   // a second sibling under the same parent has its own index
                rig.Advance(1200000); var fresh = rig.Kit.Gather.OnSwingFirstHit(other, Next(), other);
                Check(r, "M4c", rig.Kit.Targets.StableId(other) != stable && Near(fresh.InkGained, 4m, 0.05m), "a different sibling of the same name has its own ledger entry (+" + fresh.InkGained + ")");
                var loaded = new Vol2SessionLedger(new DefenceNumbersV3Dto(book.Numbers));
                var rig5 = new Vol2DefenceRig(made, book, session: loaded); rig5.Ink.Restore(0.1f);
                var e5 = NewEnemy(made, "M5_e"); var first5 = rig5.Kit.Gather.OnSwingFirstHit(e5, Next(), e5);
                loaded.OnLoaded(new[] { new NoInkSwingV3.TargetKey(1, rig5.Kit.Targets.StableId(e5)) });
                rig5.Advance(1200000); var second5 = rig5.Kit.Gather.OnSwingFirstHit(e5, Next(), e5);
                Check(r, "M5", Near(first5.InkGained, 4m, 0.05m) && Near(second5.InkGained, 0m), "a loaded save marks the restored enemy empty: +" + first5.InkGained + " before, +" + second5.InkGained + " after OnLoaded");
            }
            // M6: B at the hit frame
            {
                var rig = new Vol2DefenceRig(made, book); var e = NewEnemy(made, "M6_e"); e.OpenWeakPointFor(2f);
                var rec = rig.Kit.Gather.OnSwingFirstHit(e, Next(), e);
                Check(r, "M6", rec.Window && Near(rec.DamageUnits, 0.45m) && Near(rec.AppliedHp, 4.5), "inside the window the swing is 0.3 x 1.5 = " + rec.DamageUnits + " units -> " + rec.AppliedHp + " HP");
            }
            // M7: the carry becomes contacts for the executors
            {
                var rig = new Vol2DefenceRig(made, book); var e = NewEnemy(made, "M7_e");
                var attach = GlyphRulesV3.Encode(Body.Fire, Frame.Attachment, Verb.Leave); var seed = GlyphRulesV3.Encode(Body.Water, Frame.Path, Verb.Seed);
                rig.Kit.Gather.OwnerId = 1;
                rig.Kit.Gather.CarryProvider = () => new[]
                {
                    new NoInkSwingV3.Carry(5, 9, 1, attach), new NoInkSwingV3.Carry(6, 10, 1, seed, true),
                    new NoInkSwingV3.Carry(7, 11, 2, attach), new NoInkSwingV3.Carry(8, 12, 1, '걱')
                };
                NoInkSwingV3.Step step = null; rig.Kit.Gather.Gathered += (rec, st) => step = st;
                var rec7 = rig.Kit.Gather.OnSwingFirstHit(e, Next(), e);
                bool kinds = step != null && step.Contacts.Count == 2 && step.Contacts[0].Kind == ContactKind.Touch && step.Contacts[1].Kind == ContactKind.Activate && step.Contacts[0].CastId == 5 && step.Contacts[1].CastId == 6;
                Check(r, "M7", rec7.Accepted && rec7.Contacts == 2 && kinds, "carried attachment -> Touch, carried seed -> Activate (original CastIds 5 and 6); wrong owner and 걱 carry nothing");
            }
            // M9 / M10: air and a dead target
            {
                var rig = new Vol2DefenceRig(made, book); rig.Ink.Restore(0.5f); var e = NewEnemy(made, "M9_e");
                var miss = rig.Kit.Gather.OnSwingMissed(Next());
                rig.Advance(1200000);
                var hit = rig.Kit.Gather.OnSwingFirstHit(e, Next(), e);
                e.TakeResolvedVol2Damage(1000f, new AttackProvenance(98002, null, DamageSource.PlayerDirect, null));
                rig.Advance(1200000); var dead = rig.Kit.Gather.OnSwingFirstHit(e, Next(), e);
                Check(r, "M9", miss.Accepted && miss.Reason == "Swing" && miss.InkGained == 0m && hit.Reason == "Gathered" && dead.Reason == "Swing" && dead.AppliedHp == 0f, "air: " + miss.Reason + ", no ink; a hit afterwards: " + hit.Reason + "; a dead enemy counts as air: " + dead.Reason);
            }
        }

        // ---------------------------------------------------------------- R: 걱
        static void Recovery(Vol2FixtureReport r, List<Object> made, Vol2Rulebook book)
        {
            // R1: 30 HP over 3 s, ink 26
            {
                var rig = new Vol2DefenceRig(made, book, 0.5f);
                var d = rig.Cast('걱'); long t0 = rig.Clock.NowUs;
                bool active = rig.Kit.Recovery.Active; float hp0 = rig.PlayerHp;
                rig.Advance(1500000); rig.Kit.Tick(); float mid = rig.PlayerHp;
                rig.Advance(1500000); rig.Kit.Tick(); float end = rig.PlayerHp;
                rig.Advance(1000000); rig.Kit.Tick();
                Check(r, "R1", d.Accepted && d.IsRecovery && active && Near(rig.InkUnits, 74m, 0.05m) && Near(hp0, 50.0, 0.01) && Near(mid, 65.0, 0.05) && Near(end, 80.0, 0.05) && !rig.Kit.Recovery.Active && Near(rig.PlayerHp, 80.0, 0.05),
                    "걱: ink 100 -> " + rig.InkUnits + "; HP " + hp0 + " -> " + mid + " (1.5 s) -> " + end + " (3 s), then stops");
                // R2: cooldown 20 s from the start, then the out-of-combat budget (30 per rest)
                rig.Clock.Set(t0 + 10000000); var cd = rig.Cast('걱');
                rig.Clock.Set(t0 + 20000000); var budget = rig.Cast('걱');
                rig.Kit.Recovery.NotifyRest(); var afterRest = rig.Cast('걱');
                Check(r, "R2", !cd.Accepted && cd.Detail == "Recovery:CooldownOrCast" && !budget.Accepted && budget.Detail == "Recovery:OutsideBudget" && afterRest.Accepted,
                    "at +10 s: " + cd.Detail + "; at +20 s the rest budget is spent: " + budget.Detail + "; after NotifyRest: " + (afterRest.Accepted ? "accepted" : afterRest.Detail));
            }
            // R3: being hit stops it at once; the HP already given stays
            {
                var rig = new Vol2DefenceRig(made, book, 0.5f);
                rig.Cast('걱'); rig.Advance(1000000); rig.Kit.Tick(); float before = rig.PlayerHp;
                var rec = rig.Kit.Recovery.OnPlayerDamaged(5f);
                rig.Advance(2000000); rig.Kit.Tick();
                Check(r, "R3", rec.Accepted && !rig.Kit.Recovery.Active && Near(before, 60.0, 0.1) && Near(rig.PlayerHp, before, 0.01), "a hit at +1 s: recovery ended, HP " + before + " stays (" + rig.PlayerHp + ")");
                rig.Clock.Set(rig.Clock.NowUs + 1000000);
                var retry = rig.Cast('걱');
                Check(r, "R3b", !retry.Accepted && retry.Detail == "Recovery:CooldownOrCast", "the cooldown still runs from the start of the interrupted cast: " + retry.Detail);
            }
            // R4: in combat only inside the weak-point window, once per encounter / phase
            {
                var rig = new Vol2DefenceRig(made, book, 0.5f);
                rig.Context.Engaged = true; rig.Context.WeakWindowOpen = false; rig.Context.EncounterId = 7;
                var noWindow = rig.Cast('걱');
                rig.Context.WeakWindowOpen = true; rig.Context.WindowId = 1;
                var ok = rig.Cast('걱'); bool consumed = rig.Kit.Recovery.Last.ConsumedWindow; decimal inkAfter = rig.InkUnits;
                rig.Advance(21000000); rig.Kit.Tick(); rig.Ink.Restore(1f);
                var sameWindow = rig.Cast('걱');
                rig.Context.WindowId = 2; var sameEncounter = rig.Cast('걱');
                rig.Context.EncounterId = 8; rig.Context.WindowId = 3; var otherEncounter = rig.Cast('걱');
                rig.Advance(21000000); rig.Kit.Tick(); rig.Ink.Restore(1f);
                rig.Context.EncounterId = 8; rig.Context.BossPhaseId = 1; rig.Context.WindowId = 4; var otherPhase = rig.Cast('걱');
                rig.Advance(21000000); rig.Kit.Tick(); rig.Ink.Restore(1f);
                rig.Context.Engaged = false; var outside = rig.Cast('걱');
                Check(r, "R4", !noWindow.Accepted && noWindow.Detail == "Recovery:EncounterOrWindow" && ok.Accepted && consumed && Near(inkAfter, 74m, 0.05m) && !sameWindow.Accepted && !sameEncounter.Accepted && sameEncounter.Detail == "Recovery:EncounterOrWindow" &&
                    otherEncounter.Accepted && otherPhase.Accepted && outside.Accepted,
                    "in combat: no window -> " + noWindow.Detail + "; window 1 of encounter 7 -> accepted (window consumed); same window again -> " + (sameWindow.Accepted ? "accepted" : sameWindow.Detail) + "; another window of encounter 7 -> " + (sameEncounter.Accepted ? "accepted" : sameEncounter.Detail) +
                    "; encounter 8 -> " + (otherEncounter.Accepted ? "accepted" : otherEncounter.Detail) + "; boss phase 1 -> " + (otherPhase.Accepted ? "accepted" : otherPhase.Detail) + "; out of combat (budget untouched by the fights) -> " + (outside.Accepted ? "accepted" : outside.Detail));
            }
            // R5: ink short / dead / clamp at max HP
            {
                var rig = new Vol2DefenceRig(made, book, 0.5f); rig.Ink.Restore(0.20f);
                var d = rig.Cast('걱');
                Check(r, "R5", !d.Accepted && d.Detail == "Recovery:InkOrDead" && Near(rig.InkUnits, 20m, 0.05m) && !rig.Kit.Recovery.Active, "ink 20 < 26: refused, nothing taken");
                var dead = new Vol2DefenceRig(made, book, 0f); var d2 = dead.Cast('걱');
                Check(r, "R5b", !d2.Accepted && d2.Detail == "Recovery:InkOrDead" && Near(dead.InkUnits, 100m), "a dead player cannot start it");
                var near = new Vol2DefenceRig(made, book, 0.95f); near.Cast('걱'); near.Advance(1500000); near.Kit.Tick(); near.Advance(1500000); near.Kit.Tick();
                Check(r, "R6", Near(near.PlayerHp, 100.0, 0.05) && Near(near.Kit.Recovery.TotalHealed, 5m, 0.05m), "HP 95: the recovery gives only what fits: total healed " + near.Kit.Recovery.TotalHealed);
            }
            // R7: no attack side effects
            {
                var rig = new Vol2DefenceRig(made, book, 0.5f); rig.Cast('걱');
                Check(r, "R7", rig.Kit.LastPayment.Kind == "Recovery" && rig.Kit.LastPayment.HpReserved == 30m && Near(rig.Kit.LastPayment.InkSpent, 26m, 0.05m), "the 걱 payment is its own kind (" + rig.Kit.LastPayment + ")");
                // a later executor of the same cast (slice 4's Leave executor) asks the coordinator's ink port: it must find 26 paid, not charge 26 again
                var castId = rig.Last.CastId; decimal inkBefore = rig.InkUnits;
                bool known = rig.Kit.TryGetPaid(castId, out decimal recorded);
                bool asked = rig.Kit.PaidInk.TrySpend(castId, 26m, out decimal answered);
                Check(r, "R8", known && recorded == 26m && asked && answered == 26m && Near(rig.InkUnits, inkBefore) && rig.Kit.PaidInk.AlreadyPaid == 1 && rig.Kit.PaidInk.PaidHere == 0, "the 걱 payment is on record (" + recorded + "): the ink port answers paid without a second charge (ink " + rig.InkUnits + ")");
                var parry = new Vol2DefenceRig(made, book); parry.Cast(ParryLetter(Body.Fire));
                Check(r, "R9", parry.Kit.TryGetPaid(parry.Last.CastId, out decimal zero) && zero == 0m, "a parry cast is on record as paid 0");
            }
        }

        // ---------------------------------------------------------------- W: wiring
        static readonly MethodInfo OnLetter = typeof(CombatLoopWiring).GetMethod("OnLetterDrawn", BindingFlags.Instance | BindingFlags.NonPublic);

        sealed class WiringProbe
        {
            public CombatLoopWiring W; public InkPool Ink; public ParryJudge Judge; public PlayerVitals Player; public Vol2GlyphAdapter Adapter; public int Misfires;
            public void Cast(char c) => OnLetter.Invoke(W, new object[] { new DrawnLetter(c, default, default, null, .6f, .6f, 1f, 1f, 3) });
            public bool Active => (bool)typeof(CombatLoopWiring).GetProperty("Vol2DefenceActive", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(W);
        }

        static WiringProbe NewWiring(List<Object> made, Vol2Rulebook book, string name, bool vol2, bool withInk, ManualVol2Clock clock = null)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave }; made.Add(go);
            var w = go.AddComponent<CombatLoopWiring>();
            var config = ScriptableObject.CreateInstance<CombatConfigSO>(); config.hideFlags = HideFlags.HideAndDontSave; made.Add(config);
            var pgo = new GameObject(name + "_player") { hideFlags = HideFlags.HideAndDontSave }; made.Add(pgo);
            var player = pgo.AddComponent<PlayerVitals>(); player.Restore(0.5f);
            var probe = new WiringProbe { W = w, Player = player, Judge = new ParryJudge(config), Ink = new InkPool(config, null) };
            SetField(w, "_config", config); SetField(w, "_playerVitals", player); SetField(w, "_judge", probe.Judge);
            if (withInk) SetField(w, "_ink", probe.Ink);
            SetField(w, "_resolver", new SpellResolver(null));
            w.CastMisfired += (c, s) => probe.Misfires++;
            clock = clock ?? new ManualVol2Clock(1000000);
            if (vol2) { probe.Adapter = new Vol2GlyphAdapter(book.Numbers, null, clock, new Vol2IdIssuer(5)); w.ConstructVol2(SpellRoute.CreateVol2(probe.Adapter)); }
            else w.ConstructVol2(SpellRoute.CreateLegacy());
            return probe;
        }

        static void Wiring(Vol2FixtureReport r, List<Object> made, Vol2Rulebook book)
        {
            // W1: bind
            var clock = new ManualVol2Clock(1000000);
            var p = NewWiring(made, book, "W1", true, true, clock);
            var kit = p.W.BindVol2Defence();
            Check(r, "W1d", kit != null && !kit.PaysOrdinaryCasts, "bound without the installer: the gate leaves ordinary casts to the coordinator (no double charge)");
            kit.PaysOrdinaryCasts = true;
            Check(r, "W1", kit != null && p.W.Vol2Defence == kit && p.Adapter.Payment != null && ReferenceEquals(p.Judge.Vol2Resolver, kit.Parry) && p.Active, "bound: payment gate on the adapter, the vol.2 resolver on the judge, rewards guard active");
            p.Cast('각');
            Check(r, "W1b", p.Misfires == 0 && Near(kit.Ink.Units, 76m, 0.05m) && p.Adapter.AcceptedCount == 1, "a letter through OnLetterDrawn: accepted, 24 ink paid, the old path not called (misfires " + p.Misfires + ")");
            p.Ink.Restore(0.1f); p.Cast('가');
            Check(r, "W1c", p.Adapter.RejectedCount == 1 && p.Misfires == 0 && Near(kit.Ink.Units, 10m, 0.05m), "short ink through the wiring: refused, still no old-path misfire");

            // W5b: the discriminating pair for W5 - the same call on a wiring WITHOUT the kit does run the old AddParry
            var legacyW = NewWiring(made, book, "W5b", false, true);
            var legacyAttacker = NewEnemy(made, "W5b_att");
            ((List<EnemyVitals>)typeof(CombatLoopWiring).GetField("_targets", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(legacyW.W)).Add(legacyAttacker);
            Call(legacyW.W, "ApplyParryProgress", new ParryImpactResult(ParryOutcome.Success, Element.Fire, Vector3.zero, new AttackProvenance(55003, legacyAttacker, DamageSource.Enemy, Element.Metal)));
            Check(r, "W5b", Near(legacyAttacker.Groggy.Value01, 1.0 / 3.0, 1e-3), "legacy wiring, same call: the old AddParry ran (old meter " + legacyAttacker.Groggy.Value01 + " = 1 of 3 steps)");

            // N: the installer's registration, through a real VContainer build
            {
                double now = 0;
                var services = Vol2Installer.Build(SpellRuntimeMode.Vol2, book, _ => true, () => now);
                var inst = NewWiring(made, book, "N1", true, true);
                inst.W.ConstructVol2(services.Route);
                var registry = new Vol2TargetRegistry { ExtrasOnly = true };
                var runtime = Vol2ShapeRuntime.Create(services, registry, new IntCaster(), new IntNoWall(), null, null, new Vol2RecordingInkSpender());
                var builder = new VContainer.ContainerBuilder();
                builder.RegisterInstance(inst.W);
                Vol2DefenceInstaller.Register(builder, services);
                using (var container = builder.Build())
                {
                    var bound = inst.W.Vol2Defence;
                    bool ledger = container.TryResolve(out Vol2SessionLedger session);
                    Check(r, "N1", bound != null && bound.PaysOrdinaryCasts && ReferenceEquals(runtime.Coordinator.Damage, bound.DamageSink) && ReferenceEquals(runtime.Coordinator.Ink, bound.PaidInk) && ledger && ReferenceEquals(session, bound.Session) &&
                        services.Route.Adapter.Payment != null, "Register: the kit is bound to the wiring at build, the coordinator got the real damage sink and the paid-ink port, the gate pays ordinary casts, the session ledger is in the container");
                }
                double now2 = 0;
                var noShapes = Vol2Installer.Build(SpellRuntimeMode.Vol2, book, _ => true, () => now2);
                var p2 = NewWiring(made, book, "N2", true, true); p2.W.ConstructVol2(noShapes.Route);
                var b2 = new VContainer.ContainerBuilder(); b2.RegisterInstance(p2.W); Vol2DefenceInstaller.Register(b2, noShapes);
                using (new LogSilencer()) { using (b2.Build()) { } }
                Check(r, "N2", p2.W.Vol2Defence != null && !p2.W.Vol2Defence.PaysOrdinaryCasts, "Register before the shape runtime exists: the kit is bound and leaves ordinary casts to the coordinator (a warning says where to place the line)");
                var legacyServices = Vol2Installer.Build(SpellRuntimeMode.Legacy308, null, _ => true);
                var p3 = NewWiring(made, book, "N3", false, true);
                var b3 = new VContainer.ContainerBuilder(); b3.RegisterInstance(p3.W); Vol2DefenceInstaller.Register(b3, legacyServices);
                using (var c3 = b3.Build()) Check(r, "N3", p3.W.Vol2Defence == null && !c3.TryResolve(out Vol2SessionLedger _), "Register in Legacy308: nothing registered, nothing bound");
            }

            // N4 / N5: the ORDER of the two build callbacks decides the rebinding (the claim handed to the main agent)
            {
                double nowA = 0;
                var servicesA = Vol2Installer.Build(SpellRuntimeMode.Vol2, book, _ => true, () => nowA);
                var pa = NewWiring(made, book, "N4", true, true); pa.W.ConstructVol2(servicesA.Route);
                Vol2ShapeRuntime runtimeA = null;
                var ba = new ContainerBuilder(); ba.RegisterInstance(pa.W);
                ba.RegisterBuildCallback(_ => runtimeA = Vol2ShapeRuntime.Create(servicesA, new Vol2TargetRegistry { ExtrasOnly = true }, new IntCaster(), new IntNoWall(), null, null, new Vol2RecordingInkSpender()));
                Vol2DefenceInstaller.Register(ba, servicesA);   // registered AFTER the shape runtime's callback, as the main agent must place it
                using (ba.Build()) { }
                var kitA = pa.W.Vol2Defence;
                Check(r, "N4", runtimeA != null && kitA != null && kitA.PaysOrdinaryCasts && ReferenceEquals(runtimeA.Coordinator.Damage, kitA.DamageSink) && ReferenceEquals(runtimeA.Coordinator.Ink, kitA.PaidInk),
                    "shape callback first, then Register: the ports are rebound and the gate pays ordinary casts");

                double nowB = 0;
                var servicesB = Vol2Installer.Build(SpellRuntimeMode.Vol2, book, _ => true, () => nowB);
                var pb = NewWiring(made, book, "N5", true, true); pb.W.ConstructVol2(servicesB.Route);
                Vol2ShapeRuntime runtimeB = null;
                var bb = new ContainerBuilder(); bb.RegisterInstance(pb.W);
                Vol2DefenceInstaller.Register(bb, servicesB);   // the wrong place: BEFORE the shape runtime's callback
                bb.RegisterBuildCallback(_ => runtimeB = Vol2ShapeRuntime.Create(servicesB, new Vol2TargetRegistry { ExtrasOnly = true }, new IntCaster(), new IntNoWall(), null, null, new Vol2RecordingInkSpender()));
                using (new LogSilencer()) { using (bb.Build()) { } }
                var kitB = pb.W.Vol2Defence;
                Check(r, "N5", runtimeB != null && kitB != null && !kitB.PaysOrdinaryCasts && !ReferenceEquals(runtimeB.Coordinator.Damage, kitB.DamageSink),
                    "Register before the shape callback (misplaced): the gate leaves ordinary casts to the coordinator, nothing is rebound, nothing is charged twice");
            }

            // W2 / W3: no kit without ink pool or without a Vol2 route
            var noInk = NewWiring(made, book, "W2", true, false);
            var legacy = NewWiring(made, book, "W3", false, true);
            Check(r, "W2", noInk.W.BindVol2Defence() == null && noInk.Adapter.Payment == null && legacy.W.BindVol2Defence() == null && legacy.W.Vol2Defence == null && !legacy.Active, "no ink pool, or a Legacy308 route: no kit, no payment gate, no resolver (the slice 1 shape)");

            // W9: lazy bind from the per-frame tick
            var lazy = NewWiring(made, book, "W9", true, true);
            Call(lazy.W, "TickVol2Defence");
            Check(r, "W9", lazy.W.Vol2Defence != null && lazy.Adapter.Payment != null, "the first tick binds the kit when the installer line is absent");

            // W4 / W5: the old parry rewards do not run in vol.2
            // (the ink refund line of OnParryImpactResolved is guarded by the same Vol2DefenceActive; that handler also spawns an ink stamp, so it is not called here)
            var rw = NewWiring(made, book, "W4", true, true); rw.W.BindVol2Defence(); rw.Ink.Restore(0.5f);
            var attacker = NewEnemy(made, "W5_att");
            ((List<EnemyVitals>)typeof(CombatLoopWiring).GetField("_targets", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rw.W)).Add(attacker);
            var res = new ParryImpactResult(ParryOutcome.Success, Element.Fire, Vector3.zero, new AttackProvenance(55001, attacker, DamageSource.Enemy, Element.Metal));
            Call(rw.W, "ApplyParryProgress", res);
            Check(r, "W5", Near(attacker.Groggy.Value01, 0.0) && !attacker.Groggy.IsBlossomed, "vol.2: ApplyParryProgress does not run AddParry (old meter " + attacker.Groggy.Value01 + ")");

            // W6: telegraph cue
            Call(rw.W, "OnVol2Telegraph", new EnemyAttackCue(new AttackProvenance(55002, attacker, DamageSource.Enemy, Element.Metal), EnemyAttackDelivery.MeleeArc, Vector3.zero, 1.2f, 1f));
            bool known = rw.W.Vol2Defence.Telegraphs.TryGet(55002, out var entry);
            Check(r, "W6", known && !entry.Projectile && entry.TelegraphUs == 1200000, "an AttackTelegraphed cue is recorded by AttackId (melee, 1,200,000 us)");

            // W7: a hit on the player ends the recovery
            var hp = NewWiring(made, book, "W7", true, true, clock); hp.W.BindVol2Defence();
            hp.Cast('걱');
            bool wasActive = hp.W.Vol2Defence.Recovery.Active;
            Call(hp.W, "OnPlayerDamaged", 5f);
            Check(r, "W7", wasActive && !hp.W.Vol2Defence.Recovery.Active, "OnPlayerDamaged stops the running recovery");

            // W8: the swing entries
            var sw = NewWiring(made, book, "W8", true, true); sw.W.BindVol2Defence(); sw.Ink.Restore(0.5f);
            var enemy = NewEnemy(made, "W8_e");
            var rec = sw.W.ReportVol2SwingFirstHit(enemy, new SwingId(1));
            Check(r, "W8", rec.Accepted && rec.Reason == "Gathered" && Near(enemy.Hp, 57.0) && sw.W.ReportVol2SwingMissed(new SwingId(2)).Reason == "CooldownOrOldSwing", "the swing's first-hit entry reaches 갈무리 through the wiring (HP " + enemy.Hp + "); the cooldown applies to the next swing");

            // W10: the encounter reset clears posture
            var kit10 = rw.W.Vol2Defence; kit10.Posture.ApplyParry(attacker);
            rw.W.ResetEncounterGroggy();
            Check(r, "W10", Near(kit10.Posture.PostureOf(attacker), 0m), "ResetEncounterGroggy also resets the posture state");

            // W11: dying
            var dp = NewWiring(made, book, "W11", true, true, clock); dp.W.BindVol2Defence(); dp.Cast('걱');
            Call(dp.W, "OnPlayerDied");
            Check(r, "W11", !dp.W.Vol2Defence.Recovery.Active, "death ends the recovery and drops the parry attempt");
        }
    }
}
