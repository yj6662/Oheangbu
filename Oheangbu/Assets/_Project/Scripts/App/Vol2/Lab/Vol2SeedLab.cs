using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The 20 심는다 (ㅁ) glyphs in the spell lab (S5-b slice 5): 감 검 곰 굼 / 남 넘 놈 눔 / 맘 멈 몸 뭄 / 삼 섬 솜 숨 / 암 엄 옴 움. Every row runs the REAL chain - the letter
    /// through the route, the shapes, the budget, the damage entry, the defence kit - and the triggers are real where the game has them: a second cast
    /// through the letter channel (the owner's later direct hit), the 갈무리 swing through the wiring, a hit on the real PlayerVitals (ㅓ), bodies that walk
    /// into the fan / onto the band. LAB-SIMULATED (marked in the step names): the drawing state of 信 (a flag on the hub), the caster stepping onto
    /// the 뭄 band (a position override on the hub). Expectations are written here by hand from the frame geometry and the table sentences.
    /// Timeline of every row: t 0 cast, 0.8 to 1.8 s a letter is being drawn (信: the seeds wait 1 s less), the triggers from 2.1 s on, then the clock
    /// jumps past the end of the seed's life (the un-triggered one expires).</summary>
    public sealed class Vol2SeedLab : IVol2LabProvider
    {
        public string Name => "Seed";
        public bool Handles(Vol2SpellRowDto row) => row.Verb == Verb.Seed;

        sealed class State
        {
            public Vol2SeedHub Hub;
            public CombatLoopWiring Wiring;
            public PlayerVitals Player;
            public string Problem = "";
            public float HpBefore;
            public bool Hit1, Hit2, ExemptAfter1, Took1, ExemptAfter2;
            public long ExemptGrantedBefore, HitsBefore;
            public string GatherReason = "";
            public long SecondCast;
            public Vector3 CasterPos;
            public bool BlockedInside, BlockedOutside;
            public Vol2Material OldGiant;
        }

        readonly Dictionary<Vol2LabScenario, State> _state = new Dictionary<Vol2LabScenario, State>();

        static string Strip(string name) => name.Replace("Vol2LabDummy_", "");
        static string Fmt(decimal d) => d.ToString("0.#####", CultureInfo.InvariantCulture);
        static bool Near(decimal a, decimal b) => Math.Abs(a - b) <= 0.000001m;
        static string Word(Body b) => b == Body.Wood ? "bind" : b == Body.Fire ? "burn" : b == Body.Earth ? "push" : b == Body.Metal ? "cut" : "wet";

        // ------------------------------------------------------------------ scenarios ------------------------------------------------------------------

        public Vol2LabScenario Build(Vol2SpellRowDto row, Vol2LabContext ctx)
        {
            var caster = ctx.Shapes.Caster;
            var sc = new Vol2LabScenario { Origin = caster.Position, Forward = Vol2ShapeGeometry.FlatUnit(caster.AimDirection) };
            ctx.Scenario = sc;
            _state.Clear();
            var st = new State(); _state[sc] = st;
            var g = row.Glyph; char letter = g.Letter;
            st.Hub = Vol2SeedExecutor.HubOf(ctx.Coordinator);
            Wire(ctx, st);
            if (st.Hub != null) { st.Hub.DrawingSimulated = false; st.Hub.CasterPositionOverride = null; }
            st.ExemptGrantedBefore = st.Hub != null && st.Hub.Hits != null ? st.Hub.Hits.ExemptionsGranted : 0;
            void Step(double at, string name, Action<Vol2LabContext> act) { sc.Steps.Add(new Vol2LabStep { AtSeconds = at, Name = name, Do = act }); }
            Step(0.8, "LAB-SIMULATED: a letter starts to be drawn (信)", c => { if (st.Hub != null) st.Hub.DrawingSimulated = true; });
            Step(1.8, "LAB-SIMULATED: the letter is done (信)", c => { if (st.Hub != null) st.Hub.DrawingSimulated = false; });

            switch (g.Frame)
            {
                case Frame.Path:
                    {
                        ctx.Dummy("A", 4f, 0f); var b = ctx.Dummy("B", 6f, 0f); ctx.Dummy("C", 8f, 0f); ctx.Dummy("D", 10f, 0f);
                        Step(2.1, "the owner's later direct hit on A (a real 가 through the letter channel)", c => st.SecondCast = SecondCast(c, st));
                        Step(2.9, "갈무리 on B (the kit's swing through the wiring)", c =>
                        {
                            if (st.Wiring == null) { st.GatherReason = "no wiring"; return; }
                            var rec = st.Wiring.ReportVol2SwingFirstHit(b.Vitals, new SwingId(c.Services.Ids.NextSwing().Value));
                            st.GatherReason = rec.Reason ?? "";
                        });
                        sc.Describe = "the path passes through A 4 m, B 6 m, C 8 m, D 10 m: seeds on the first three, D (the fourth) gets none; 1 s of 信 mid-wait; at 2.1 s a real 가 hits A (the owner's later direct hit: A's seed works, posture 60); at 2.9 s 갈무리 on B (B's seed works, posture 60); C's never works and expires" + (letter == '맘' ? "; 맘: the struck body is pressed 1.5 s (no movement)" : "");
                        break;
                    }
                case Frame.Attachment:
                    {
                        var n1 = ctx.Dummy("N1", 1.0f, 0.5f); ctx.Dummy("N2", 1.4f, -0.6f); ctx.Dummy("N3", 1.8f, 0.2f); ctx.Dummy("N4", 1.9f, -0.2f); ctx.Dummy("Far", 5f, 0f);
                        Step(2.2, "the player really loses 6 HP (PlayerVitals.TakeAttackDamage)", c =>
                        {
                            if (st.Player == null) return;
                            st.HpBefore = st.Player.Hp01 * st.Player.MaxHp;
                            st.Hit1 = st.Player.TakeAttackDamage(6f, IncomingDamageKind.Melee, n1.Vitals);
                            var gate = st.Hub != null ? st.Hub.Hits : null;
                            st.ExemptAfter1 = gate != null && gate.StaggerExemptPending;
                            st.Took1 = gate != null && gate.TakeStaggerExemption();         // what the body-stagger executor asks BEFORE it plays
                        });
                        Step(3.0, "a second real hit (the seed is gone: nothing works)", c =>
                        {
                            if (st.Player == null) return;
                            st.Hit2 = st.Player.TakeAttackDamage(6f, IncomingDamageKind.Melee, n1.Vitals);
                            st.ExemptAfter2 = st.Hub != null && st.Hub.Hits != null && st.Hub.Hits.StaggerExemptPending;
                        });
                        sc.Describe = "the seed rides the body (no place in the world); 1 s of 信; at 2.2 s the player really loses HP: the enemies within 2 m, nearest first, N1 N2 N3 take the work (1.2 each), N4 (a fourth) is refused TargetCap, Far is out of reach; a second hit works nothing" + (letter == '멈' ? "; 멈: that first hit's body stagger is exempt (once), the second hit's is not" : "");
                        break;
                    }
                case Frame.Fan:
                    {
                        ctx.Dummy("In3", 3f, 0f); ctx.Dummy("Out7.5", 7.5f, 0f); ctx.Dummy("Side", 3.5f, 3.5f); ctx.Dummy("E", 7f, 0.3f); ctx.Dummy("Late", 7f, -1f);
                        Step(2.2, "E walks into the fan (an entry)", c => c.Move("E", 4f, 0.3f));
                        Step(3.2, "Late walks into the fan (the seed is gone)", c => c.Move("Late", 3.5f, -0.5f));
                        if (letter == '몸') st.OldGiant = ctx.Coordinator.Materials.Create(new CastId(9100001), '몸', Body.Earth, Vol2MaterialKind.Mark, 0m, RuleTypesV3.Microseconds(ctx.Services.Rulebook.Numbers.Get("timing.seed.life")), Vol2Footprint.Disc(ctx.At(3f, 0f), 0.6f), Vol2Faction.Player, Vol2Faction.Player, false, ctx.Services.Clock.NowUs, 0, 0, Vol2SeedExecutor.TagGiant);
                        sc.Describe = "the seed is the fan; In3 stands in it when it is laid (an initial occupant: NOT an entry); 1 s of 信; at 2.2 s E walks in: the seed works on every body in the fan then (In3 and E, 1.2 each" + (letter == '몸' ? "; 몸: the giant strikes 1.8, then collapses into an earth fan of amount 0 for 10 s; a giant that already stood is replaced (동시 1)" : "") + "); Late walks in afterwards: nothing; Out7.5 and Side are never in the fan";
                        break;
                    }
                default:
                    {
                        ctx.Dummy("Hole", 0.5f, 0.3f); ctx.Dummy("Band", 2f, 0f); ctx.Dummy("Out", 4.5f, 0f); ctx.Dummy("W", 4.2f, 1f); ctx.Dummy("Late", 4.5f, -1f);
                        if (letter == '뭄')
                        {
                            st.CasterPos = sc.Origin;
                            if (st.Hub != null) st.Hub.CasterPositionOverride = () => st.CasterPos;
                            Step(1.0, "W crosses the band (an enemy: nothing happens)", c => c.Move("W", 2f, 1f));
                            Step(2.2, "LAB-SIMULATED: the caster steps onto the band", c => st.CasterPos = c.At(2f, 0f));
                            Step(3.0, "is the ground hard? (inside the ring / outside it)", c =>
                            {
                                long now = c.Services.Clock.NowUs;
                                st.BlockedInside = Vol2SeedWorld.BlocksDashOrJump(c.Coordinator.Materials, c.At(1f, 0f), now, 0.4f);
                                st.BlockedOutside = Vol2SeedWorld.BlocksDashOrJump(c.Coordinator.Materials, c.At(4.5f, 0f), now, 0.4f);
                            });
                            sc.Describe = "뭄: the seed is the ring; an enemy crossing the band does NOT work it (W at 1.0 s); 1 s of 信; at 2.2 s the CASTER steps onto the band (lab-simulated position): the ring hardens 8 s - no damage (a non-damage axis), enemies inside cannot dash or jump, outside they can; the field is asked for the ground";
                        }
                        else
                        {
                            Step(2.2, "W crosses onto the band (an entry)", c => c.Move("W", 2f, 1f));
                            Step(3.2, "Late crosses onto the band (the seed is gone)", c => c.Move("Late", 2f, -1f));
                            sc.Describe = "the seed is the ring band; Band stands on it when it is laid (an initial occupant: NOT an entry); 1 s of 信; at 2.2 s W crosses onto the band: the seed works on every body on the band then (Band and W, 1.2 each); Late crosses afterwards: nothing; Hole (the hollow) and Out are never touched";
                        }
                        break;
                    }
            }
            sc.SettleSeconds = 0.5;
            return sc;
        }

        static long SecondCast(Vol2LabContext c, State st)
        {
            if (st.Wiring != null && st.Wiring.LetterChannel != null)
            {
                long before = c.Services.Ids.CastsIssued;
                st.Wiring.LetterChannel.Raise(new DrawnLetter('가', default, default, null, 0.6f, 0.6f, 1f, 1f, 3));
                return c.Services.Ids.CastsIssued > before ? c.Services.Ids.CastsIssued : -1;
            }
            var d = c.Services.Route.Adapter.Receive('가');
            return d.Accepted ? d.CastId.Value : -1;
        }

        static void Wire(Vol2LabContext ctx, State st)
        {
            st.Wiring = UnityEngine.Object.FindFirstObjectByType<CombatLoopWiring>();
            st.Player = UnityEngine.Object.FindFirstObjectByType<PlayerVitals>();
            if (st.Hub == null) { st.Problem = "no 심는다 hub (the executor is not registered)"; return; }
            var kit = st.Wiring != null ? st.Wiring.Vol2Defence : null;
            if (kit == null) { st.Problem = "no defence kit on the wiring"; return; }
            Vol2SeedWiring.Bind(ctx.Coordinator, kit, st.Player);
        }

        // ------------------------------------------------------------------ verdict ------------------------------------------------------------------

        public Vol2OnceVerdict Evaluate(Vol2SpellRowDto row, Vol2LabScenario sc, Vol2CastLog log, Vol2CastDecision decision, Vol2LabContext ctx)
        {
            var v = new Vol2OnceVerdict();
            var problems = new List<string>(); var checks = new List<string>();
            void Need(bool ok, string what) { if (!ok) problems.Add(what); else checks.Add(what); }
            _state.TryGetValue(sc, out var st); st = st ?? new State();
            var g = row.Glyph; char letter = g.Letter; var n = ctx.Services.Rulebook.Numbers; var co = ctx.Coordinator; var hub = st.Hub;
            try
            {
                if (st.Problem.Length > 0) problems.Add(st.Problem);
                Need(log.Status == "Done", "cast status " + log.Status);
                var plan = decision.Plan;
                decimal act = n.Get("action.seed.activation") * (letter == '몸' ? n.Get("peak.몸.activationMultiplier") : 1m);
                decimal cap = n.Get("combat.peak.structureForceCap"), force = n.Get("combat.force.min");
                decimal h = Vol2BodyProfile.For(g.Body, n).Instant;
                decimal unitsPerAct = h * Math.Min(act * force, cap);
                long life = RuleTypesV3.Microseconds(n.Get("timing.seed.life"));
                var seeds = log.Materials.Where(m => m.Tag == Vol2SeedExecutor.TagSeed || m.Tag == Vol2SeedExecutor.TagGiant).ToList();
                var acts = log.Contacts.Where(c => c.Kind == "Activation" && c.BudgetAccepted).ToList();
                var refused = log.Contacts.Where(c => c.Kind == "Activation" && !c.BudgetAccepted).ToList();
                IReadOnlyList<Vol2SeedRecord> recs = hub != null ? hub.RecordsOf(log.Cast) : (IReadOnlyList<Vol2SeedRecord>)new List<Vol2SeedRecord>();
                long paused = hub != null ? hub.PausedUsOf(log.Cast) : 0;
                Need(paused >= 900000 && paused <= 1300000, "信: the 1 s of drawing held the waiting seeds " + paused + " us");
                Need(co.Materials.Audit().Count == 0, "material audit: " + string.Join("; ", co.Materials.Audit()));
                Need(!log.Contacts.Any(c => c.Kind == "Instant" || c.Kind == "Second"), "a seed makes no first / second contact");
                Need(Near(log.Budget.PaidShared, 0m) && Near(log.Budget.Transferred, 0m), "no shared pay, no transfer (the seed holds nothing)");
                if (letter != '뭄')
                {
                    foreach (var c in acts)
                    {
                        Need(Near(c.Amount, act), Strip(c.TargetName) + " works with " + Fmt(c.Amount) + ", expected " + Fmt(act));
                        Need(c.DamageApplied && Near(c.Units, unitsPerAct), Strip(c.TargetName) + " units " + Fmt(c.Units) + ", expected H " + Fmt(h) + " x min(" + Fmt(act) + " x " + Fmt(force) + ", " + Fmt(cap) + ") = " + Fmt(unitsPerAct) + (letter == '몸' ? " (the entry caps C at 1.5: R 1.8 is clipped)" : ""));
                        string word = letter == '맘' ? "press" : Word(g.Body);
                        Need(c.BodyEffect.StartsWith(word, StringComparison.Ordinal), Strip(c.TargetName) + " body action '" + c.BodyEffect + "', expected " + word);
                    }
                    Need(acts.All(c => acts.Count(x => x.TargetId == c.TargetId && x.BodyEffect.Length > 0) <= 1), "one body action per body");
                    if (letter == '맘') Need(acts.All(c => c.BodyEffect.Contains("x0 ") && c.BodyEffect.Contains("1.5s")), "맘: pressed, no movement for 1.5 s");
                }
                v.Hit = string.Join("|", log.Contacts.Select(c => Strip(c.TargetName)).Where(x => x.Length > 0).Distinct().OrderBy(x => x, StringComparer.Ordinal));
                v.Units = acts.Sum(c => c.Units); v.Hp = log.TotalHp;
                v.BodyObserved = string.Join("; ", acts.Where(c => c.BodyEffect.Length > 0).Select(c => Strip(c.TargetName) + " " + c.BodyEffect));
                v.Ledger = string.Join("|", log.Contacts.Where(c => c.BudgetAccepted && c.Kind == "Activation").Select(c => c.Kind + ":" + Strip(c.TargetName) + ":" + Fmt(c.Amount)));

                switch (g.Frame)
                {
                    case Frame.Path:
                        {
                            var plants = log.Contacts.Where(c => c.Kind == "Plant").ToList();
                            Need(plants.Where(c => c.BudgetAccepted).Select(c => Strip(c.TargetName)).SequenceEqual(new[] { "A", "B", "C" }), "planted [" + string.Join("|", plants.Where(c => c.BudgetAccepted).Select(c => Strip(c.TargetName))) + "], expected [A|B|C]");
                            Need(plants.Any(c => Strip(c.TargetName) == "D" && !c.BudgetAccepted && c.BudgetReason == "TargetCap"), "the fourth body D is refused TargetCap (no seed)");
                            Need(!log.Contacts.Any(c => Strip(c.TargetName) == "D" && c.BudgetAccepted), "D took nothing");
                            Need(seeds.Count == 3 && seeds.All(m => m.Kind == Vol2MaterialKind.Mark && !m.PaysPool && m.Initial == 0m && m.FactionId == Vol2Faction.Player), "three seed materials (Mark, hold 0, mine)");
                            Need(acts.Select(c => Strip(c.TargetName)).SequenceEqual(new[] { "A", "B" }), "the seeds that worked: [" + string.Join("|", acts.Select(c => Strip(c.TargetName))) + "], expected [A|B]");
                            Need(Near(log.Budget.PaidPerTarget, 2m * act) && log.Budget.State.Targets.Count == 3, "paid " + Fmt(log.Budget.PaidPerTarget) + " (two seeds), three planted targets");
                            var a = recs.FirstOrDefault(r => r.Kind == "Activate" && r.TargetName.EndsWith("_A", StringComparison.Ordinal)); var b = recs.FirstOrDefault(r => r.Kind == "Activate" && r.TargetName.EndsWith("_B", StringComparison.Ordinal));
                            Need(a != null && a.Trigger == "direct", "A's seed worked on the owner's later direct hit (" + (a != null ? a.Trigger : "none") + ")");
                            Need(b != null && b.Trigger == "gather" && st.GatherReason == "Gathered", "B's seed worked on 갈무리 (" + (b != null ? b.Trigger : "none") + ", swing " + st.GatherReason + ")");
                            var posts = recs.Where(r => r.Kind == "Posture").ToList();
                            Need(posts.Count == 2 && posts.All(p => p.Posture.Called && p.Posture.Accepted && p.Posture.After == 60m && p.Posture.Added == 60m), "ㅏ+ㅁ active seed: posture 60 on each of the two bodies (" + string.Join(" / ", posts.Select(p => p.Posture.ToString())) + ")");
                            Need(st.SecondCast > 0, "the second cast was accepted (cast " + st.SecondCast + ")");
                            var cSeed = seeds.FirstOrDefault(m => m.Footprint.Kind == Vol2FootprintKind.Disc && recs.Any(r => r.Kind == "Plant" && r.MaterialId == m.Id.Value && r.TargetName.EndsWith("_C", StringComparison.Ordinal)));
                            Need(cSeed != null && cSeed.End == Vol2MaterialEnd.Expired && cSeed.ExpiresUs - log.StartUs == life + paused, "C's seed never worked and expired at cast + 8 s + the held time (" + (cSeed != null ? (cSeed.ExpiresUs - log.StartUs).ToString(CultureInfo.InvariantCulture) : "none") + " us)");
                            Need(seeds.Count(m => m.End == Vol2MaterialEnd.Consumed) == 2, "the two seeds that worked are Consumed");
                            Need(!log.Contacts.Any(c => Strip(c.TargetName) == "C" && c.Kind == "Activation"), "C took nothing");
                            break;
                        }
                    case Frame.Attachment:
                        {
                            var gate = hub != null ? hub.Hits : null;
                            Need(gate != null, "the hit gate is attached to the player");
                            Need(st.Hit1 && st.Hit2, "both hits really landed on the player (" + st.Hit1 + ", " + st.Hit2 + ")");
                            float hpAfter = st.Player != null ? st.Player.Hp01 * st.Player.MaxHp : 0f;
                            Need(st.Player != null && hpAfter < st.HpBefore - 11f, "the damage is taken as it is (HP " + st.HpBefore.ToString("0.#", CultureInfo.InvariantCulture) + " -> " + hpAfter.ToString("0.#", CultureInfo.InvariantCulture) + ")");
                            Need(seeds.Count == 1 && seeds[0].Attached && seeds[0].FactionId == Vol2Faction.Player && seeds[0].Initial == 0m && seeds[0].End == Vol2MaterialEnd.Consumed, "one seed on the body (no place in the world), Consumed once");
                            Need(acts.Select(c => Strip(c.TargetName)).SequenceEqual(new[] { "N1", "N2", "N3" }), "within 2 m, nearest first: [" + string.Join("|", acts.Select(c => Strip(c.TargetName))) + "], expected [N1|N2|N3]");
                            Need(refused.Any(c => Strip(c.TargetName) == "N4" && c.BudgetReason == "TargetCap") && !log.Contacts.Any(c => Strip(c.TargetName) == "Far"), "N4 (a fourth) is refused TargetCap; Far was out of reach");
                            Need(Near(log.Budget.PaidPerTarget, 3m * act), "paid " + Fmt(log.Budget.PaidPerTarget) + " = 3 x " + Fmt(act));
                            Need(recs.Count(r => r.Kind == "Activate") == 3, "the second hit worked nothing (3 activations in all)");
                            if (gate != null)
                            {
                                if (letter == '멈') Need(st.ExemptAfter1 && st.Took1 && !st.ExemptAfter2 && gate.ExemptionsGranted - st.ExemptGrantedBefore == 1, "멈: the first hit's stagger is exempt (granted inside the confirmation, taken once), the second hit's is not");
                                else Need(!st.ExemptAfter1 && !st.Took1 && gate.ExemptionsGranted - st.ExemptGrantedBefore == 0, "no stagger exemption (only 멈 grants it)");
                            }
                            Need(seeds.Count == 1 && seeds[0].ExpiresUs - log.StartUs == life + paused, "the seed's life is cast + 8 s + the held time");
                            break;
                        }
                    case Frame.Fan:
                        {
                            Need(seeds.Count == 1 && seeds[0].Footprint.Kind == Vol2FootprintKind.Fan && seeds[0].End == Vol2MaterialEnd.Consumed && seeds[0].ExpiresUs - log.StartUs == life + paused, "one seed shaped like the fan, Consumed, life cast + 8 s + the held time");
                            Need(acts.Select(c => Strip(c.TargetName)).SequenceEqual(new[] { "In3", "E" }), "the seed worked on [" + string.Join("|", acts.Select(c => Strip(c.TargetName))) + "], expected [In3|E] (In3 was there already, E entered)");
                            Need(acts.Count > 0 && acts.All(c => c.NowUs - log.StartUs >= 2000000) && acts.Select(c => c.SwingId).Distinct().Count() == 1, "all at the entry (>= 2 s), one trigger id");
                            Need(!log.Contacts.Any(c => new[] { "Late", "Out7.5", "Side" }.Contains(Strip(c.TargetName))), "Late (after), Out7.5 and Side were never touched");
                            Need(Near(log.Budget.PaidPerTarget, 2m * act), "paid " + Fmt(log.Budget.PaidPerTarget) + " = 2 x " + Fmt(act));
                            if (letter == '몸')
                            {
                                var rubble = log.Materials.FirstOrDefault(m => m.Tag == Vol2SeedExecutor.TagRubble);
                                Need(rubble != null && rubble.Kind == Vol2MaterialKind.Residue && rubble.Body == Body.Earth && rubble.Initial == 0m && rubble.LifeUs == RuleTypesV3.Microseconds(n.Get("peak.몸.residueLife")) && rubble.Footprint.Kind == Vol2FootprintKind.Fan, "몸: collapses into an earth material fan, amount 0, 10 s");
                                Need(st.OldGiant != null && st.OldGiant.End == Vol2MaterialEnd.Replaced, "몸: the giant that stood is replaced (동시 1)");
                                Need(seeds.Count == 1 && seeds[0].Tag == Vol2SeedExecutor.TagGiant, "몸: the waiting seed is the giant");
                            }
                            break;
                        }
                    default:
                        if (letter == '뭄')
                        {
                            Need(seeds.Count == 1 && seeds[0].Footprint.Kind == Vol2FootprintKind.Ring && seeds[0].End == Vol2MaterialEnd.Consumed, "one ring seed, Consumed when the caster stepped on");
                            Need(acts.Count == 1 && acts[0].TargetId == Vol2VerbRunBase.WorldTargetId && !acts[0].DamageRequested && Near(acts[0].Amount, act), "one activation, no body, no damage request (non-damage axis), booked " + Fmt(act));
                            Need(acts.Count == 1 && acts[0].NowUs - log.StartUs >= 2000000, "it waited for the caster (>= 2 s), W's crossing at 1 s did nothing");
                            var ground = log.Materials.FirstOrDefault(m => m.Tag == Vol2SeedExecutor.TagGround);
                            Need(ground != null && ground.Kind == Vol2MaterialKind.Residue && ground.Initial == 0m && ground.LifeUs == RuleTypesV3.Microseconds(n.Get("peak.뭄.groundLife")) && ground.Footprint.Kind == Vol2FootprintKind.Ring, "the ground: an earth ring material, amount 0, 8 s");
                            Need(st.BlockedInside && !st.BlockedOutside, "inside the hard ring no dash / jump (" + st.BlockedInside + "), outside they are free (" + !st.BlockedOutside + ")");
                            Need(!Vol2SeedWorld.BlocksDashOrJump(co.Materials, ctx.At(1f, 0f), ctx.Services.Clock.NowUs), "after the ground's 8 s the block is gone");
                            Need(log.FieldNotes.Count == 1 && log.FieldNotes[0].Contains("ground"), "the field was asked for the ground (" + string.Join(" / ", log.FieldNotes) + ")");
                            Need(!log.Contacts.Any(c => c.DamageRequested), "nothing was hurt");
                        }
                        else
                        {
                            Need(seeds.Count == 1 && seeds[0].Footprint.Kind == Vol2FootprintKind.Ring && seeds[0].End == Vol2MaterialEnd.Consumed && seeds[0].ExpiresUs - log.StartUs == life + paused, "one ring seed, Consumed, life cast + 8 s + the held time");
                            Need(acts.Select(c => Strip(c.TargetName)).SequenceEqual(new[] { "Band", "W" }), "the seed worked on [" + string.Join("|", acts.Select(c => Strip(c.TargetName))) + "], expected [Band|W] (Band was on it already, W crossed)");
                            Need(acts.Count > 0 && acts.All(c => c.NowUs - log.StartUs >= 2000000) && acts.Select(c => c.SwingId).Distinct().Count() == 1, "all at the crossing (>= 2 s), one trigger id");
                            Need(!log.Contacts.Any(c => new[] { "Late", "Hole", "Out" }.Contains(Strip(c.TargetName))), "Late (after), the hollow and the outside were never touched");
                            Need(Near(log.Budget.PaidPerTarget, 2m * act), "paid " + Fmt(log.Budget.PaidPerTarget) + " = 2 x " + Fmt(act));
                        }
                        break;
                }
                v.MaterialIds = string.Join("|", log.Materials.Select(m => m.Id.Value.ToString(CultureInfo.InvariantCulture)));
                v.Created = string.Join("|", log.Materials.Select(m => m.Id + ":" + m.Kind + ":" + m.Tag + ":" + Fmt(m.Initial)));
                v.Consumed = string.Join("|", log.Materials.Select(m => m.Id + ":" + m.End));
                v.Depth = "0"; v.Owner = log.Materials.Count > 0 ? "caster" : ""; v.Balance = Fmt(log.Budget.SharedRemaining);
                v.Expected = string.Join("|", acts.Select(c => Strip(c.TargetName)));
                v.Unexpected = "";
            }
            catch (Exception e) { problems.Add("verdict exception " + e.GetType().Name + ": " + e.Message); }
            finally
            {
                if (hub != null) { hub.DrawingSimulated = false; hub.CasterPositionOverride = null; }
                if (st.Player != null) st.Player.Restore(1f);
                _state.Remove(sc);
            }
            v.Checks = string.Join(" | ", checks);
            v.Pass = problems.Count == 0; v.Note = string.Join("; ", problems);
            return v;
        }
    }
}
