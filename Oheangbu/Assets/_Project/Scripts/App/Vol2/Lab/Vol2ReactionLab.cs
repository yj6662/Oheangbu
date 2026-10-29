using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEngine;
using R = Oheangbu.Vol2.Rules.ReactionRulesV3;

namespace Oheangbu.App
{
    /// <summary>One row of the reaction pass: the letter the lab injects (a real cast through the recognition channel), a variant name, and how long the row needs.</summary>
    public sealed class Vol2ReactionCase
    {
        public string Variant = "", Describe = "";
        public char Letter;
        /// <summary>direction rows: acting body, contacted body (the cast's body acts as the MOVING shape).</summary>
        public int Acting = -1, Contacted = -1;
        /// <summary>"dir" | "child" | "samecast" | "steal" | "gate" | "ring" | "seams"</summary>
        public string Kind = "dir";
        public double Settle = 0.6;
        public bool FastForward;
    }

    /// <summary>S5-b slice 8, the PLAY half (design section 12, scenario "반응"): the reaction pass of the spell lab. After the 125 rows of the verbs (run with the reaction engine paused, as before) the
    /// engine is switched on and these rows run with REAL contacts: a real cast comes through the recognition channel, flies, and meets a standing material.
    ///   - 25 DIRECTION rows: the bare path glyph of each body (가 나 마 사 아) over a planted enemy patch of each body (the moving shape acts): relation, who is consumed, thickened / halved, the child
    ///     (body, amount (plan + 0.9) x 0.5, depth 1, owner), the product (slow / damage ticks), reaction damage that is a Reaction request (no groggy, no seed), the audit.
    ///   - X1 a second cast meets the CHILD: it only dries, no grandchild. X2 a 남긴다 path over a wood patch: its shape reacts once, its own residue never reacts with it (same cast).
    ///   - X3 ownership race: the enemy's newer fire lands on my wood patch: the child is the enemy's. X4 a world vine door burns and opens its SpellFieldGate308.
    ///   - X5 둘레 1: a second ring residue of mine replaces the first. X6 the seams: the kit's hit gate, the 날붓 port, the borrowed HP-per-unit, the field port, the seed hub are bound.
    /// Expectations are written here from NumbersV3 and the wuxing cycle; nothing is read back from a run.</summary>
    public sealed class Vol2ReactionLab
    {
        public const string Scenario = "S8-reaction";
        static readonly string[] Names = { "Wood", "Fire", "Earth", "Metal", "Water" };
        static readonly int[] Over = { 2, 3, 4, 0, 1 };

        public IReadOnlyList<Vol2ReactionCase> Cases { get; }
        readonly Dictionary<Vol2LabScenario, Extra> _extra = new Dictionary<Vol2LabScenario, Extra>();

        sealed class Extra { public long CreatedAtBuild; public SpellFieldGate308 Gate; public Vol2Material World; public Vol2Material Planted, Child; public string[] Notes = new string[0]; }

        public Vol2ReactionLab()
        {
            var list = new List<Vol2ReactionCase>();
            for (int a = 0; a < 5; a++)
                for (int b = 0; b < 5; b++)
                {
                    var kind = Expect(a, b);
                    list.Add(new Vol2ReactionCase
                    {
                        Kind = "dir", Acting = a, Contacted = b, Variant = Names[a] + ">" + Names[b], Letter = GlyphRulesV3.Encode((Body)a, Frame.Path, Verb.Once),
                        Settle = a == 1 && b == 3 ? 2.5 : 0.6,
                        Describe = "the " + Names[a] + " path (a moving shape) flies over a standing enemy " + Names[b] + " patch at 6 m with an enemy body in it: " + kind
                    });
                }
            list.Add(new Vol2ReactionCase { Kind = "child", Variant = "child-recontact", Letter = GlyphRulesV3.Encode(Body.Wood, Frame.Path, Verb.Once), Settle = 0.9, Describe = "a wood path feeds a fire patch (a child is born); a SECOND wood path meets the child: it only dries, no grandchild" });
            list.Add(new Vol2ReactionCase { Kind = "samecast", Variant = "same-cast", Letter = GlyphRulesV3.Encode(Body.Fire, Frame.Path, Verb.Leave), FastForward = true, Settle = 0.8, Describe = "a fire 남긴다 path over a wood patch: the shape burns it once; the residue it then lays never reacts with its own cast" });
            list.Add(new Vol2ReactionCase { Kind = "steal", Variant = "ownership", Letter = GlyphRulesV3.Encode(Body.Wood, Frame.Path, Verb.Once), Settle = 0.6, Describe = "my wood patch stands at the side; the ENEMY's fire patch is laid on it later: the newer acts, the child is the enemy's" });
            list.Add(new Vol2ReactionCase { Kind = "gate", Variant = "vine-gate", Letter = GlyphRulesV3.Encode(Body.Fire, Frame.Path, Verb.Once), Settle = 0.6, Describe = "a fire path burns a world vine door: the SpellFieldGate308 it is the door of opens" });
            list.Add(new Vol2ReactionCase { Kind = "ring", Variant = "ring-slot", Letter = GlyphRulesV3.Encode(Body.Fire, Frame.Ring, Verb.Leave), FastForward = true, Settle = 0.9, Describe = "a fire ring residue, then an earth ring residue: the player holds ONE ring (둘레 1), the first ends Replaced" });
            list.Add(new Vol2ReactionCase { Kind = "seams", Variant = "seams-bound", Letter = GlyphRulesV3.Encode(Body.Wood, Frame.Path, Verb.Once), Settle = 0.3, Describe = "the open seams of slices 2, 5, 6 and 7 are bound by the installer" });
            Cases = list;
        }

        static R.Kind Expect(int a, int b)
        {
            if (a == b) return R.Kind.Same;
            if ((a + 1) % 5 == b) return R.Kind.Feed;
            if ((b + 1) % 5 == a) return R.Kind.Transfer;
            return Over[a] == b ? R.Kind.Break : R.Kind.Block;
        }

        public IVol2LabProvider ProviderFor(Vol2ReactionCase c) => new CaseProvider(this, c);

        sealed class CaseProvider : IVol2LabProvider
        {
            readonly Vol2ReactionLab _lab; readonly Vol2ReactionCase _case;
            public CaseProvider(Vol2ReactionLab lab, Vol2ReactionCase c) { _lab = lab; _case = c; }
            public string Name => "Reaction";
            public bool Handles(Vol2SpellRowDto row) => row.Letter == _case.Letter;
            public Vol2LabScenario Build(Vol2SpellRowDto row, Vol2LabContext ctx) => _lab.Build(_case, ctx);
            public Vol2OnceVerdict Evaluate(Vol2SpellRowDto row, Vol2LabScenario sc, Vol2CastLog log, Vol2CastDecision decision, Vol2LabContext ctx) => _lab.Evaluate(_case, sc, log, decision, ctx);
        }

        // ------------------------------------------------------------------ scenarios ------------------------------------------------------------------

        void CleanStale(Vol2LabContext ctx)
        {
            foreach (var kv in new List<KeyValuePair<Vol2LabScenario, Extra>>(_extra)) Release(kv.Value, ctx);
            _extra.Clear();
        }

        void Release(Extra x, Vol2LabContext ctx)
        {
            if (x.Gate != null) UnityEngine.Object.Destroy(x.Gate.gameObject);
            x.Gate = null;
            if (x.World != null && ctx.Shapes.World != null) ctx.Shapes.World.EndAll();
            x.World = null;
        }

        Vol2LabScenario Build(Vol2ReactionCase c, Vol2LabContext ctx)
        {
            CleanStale(ctx);
            var caster = ctx.Shapes.Caster;
            var sc = new Vol2LabScenario { Origin = caster.Position, Forward = Vol2ShapeGeometry.FlatUnit(caster.AimDirection), Variant = c.Variant, Describe = c.Describe, SettleSeconds = c.Settle, SkipFastForward = !c.FastForward };
            ctx.Scenario = sc;
            var x = new Extra { CreatedAtBuild = ctx.Coordinator.Materials.TotalCreated }; _extra[sc] = x;
            void Step(double at, string name, Action<Vol2LabContext> act) { sc.Steps.Add(new Vol2LabStep { AtSeconds = at, Name = name, Do = act }); }
            switch (c.Kind)
            {
                case "dir":
                    ctx.Plant("B", (Body)c.Contacted, 6f, 0f, Vol2Faction.Enemy, 0.8f);
                    ctx.Dummy("T", 6f, 0.1f);
                    break;
                case "child":
                    ctx.Plant("B", Body.Fire, 6f, 0f, Vol2Faction.Enemy, 0.8f);
                    ctx.Dummy("T", 6f, 0.1f);
                    sc.MinSeconds = 1.8;
                    Step(0.9, "a second wood path", cx => cx.Cast?.Invoke(GlyphRulesV3.Encode(Body.Wood, Frame.Path, Verb.Once)));
                    break;
                case "samecast":
                    ctx.Plant("B", Body.Wood, 5f, 0f, Vol2Faction.Enemy, 0.8f);
                    ctx.Dummy("T", 5.5f, 0.1f);
                    break;
                case "steal":
                    ctx.Plant("P", Body.Wood, 0f, 8f, Vol2Faction.Player, 0.8f);
                    Step(0.3, "the enemy lays fire on it", cx => x.Planted = cx.Plant("E", Body.Fire, 0f, 8.5f, Vol2Faction.Enemy, 0.6f));
                    break;
                case "gate":
                    x.Gate = SpellFieldGate308.CreateTest(SpellFieldGateKind.Vine, ctx.At(5f, 0f), 1.5f);
                    if (ctx.Shapes.World != null)
                        x.World = ctx.Shapes.World.Place(new Vol2WorldMaterialSpec { Id = "lab_vine", Body = Body.Wood, Position = ctx.At(5f, 0f), Radius = 0.9f, GateId = x.Gate.Id, LifeSeconds = 10f });
                    break;
                case "ring":
                    Step(0.45, "an earth ring residue", cx => cx.Cast?.Invoke(GlyphRulesV3.Encode(Body.Earth, Frame.Ring, Verb.Leave)));
                    break;
            }
            return sc;
        }

        // ------------------------------------------------------------------ verdict ------------------------------------------------------------------

        static string Fmt(decimal d) => d.ToString("0.#####", CultureInfo.InvariantCulture);
        static bool Near(decimal a, decimal b) => Math.Abs(a - b) <= 0.000001m;

        Vol2OnceVerdict Evaluate(Vol2ReactionCase c, Vol2LabScenario sc, Vol2CastLog log, Vol2CastDecision decision, Vol2LabContext ctx)
        {
            var v = new Vol2OnceVerdict(); var problems = new List<string>(); var checks = new List<string>();
            void Need(bool ok, string what) { if (!ok) problems.Add(what); else checks.Add(what); }
            _extra.TryGetValue(sc, out var x); x = x ?? new Extra();
            var co = ctx.Coordinator; var rx = co.Reactions; var n = ctx.Services.Rulebook.Numbers; var led = rx.Ledger;
            decimal sameMul = n.Get("reaction.sameBody.maxMultiplier"), childMul = n.Get("reaction.child.parentMultiplier"), blocked = n.Get("reaction.blocked.remaining"), dmgCap = n.Get("reaction.damageCap");
            float hpPerUnit = ctx.Kit != null ? ctx.Kit.HpPerUnit : Vol2Units.DefaultHpPerUnit;
            Need(rx.Enabled, "the reaction engine is on for this row");
            Need(rx.Exceptions == 0 && rx.ActorMismatch == 0, "no exception, no wrong prediction of the acting side");
            Vol2Material Planted(string key) { sc.Planted.TryGetValue(key, out var m); return m; }
            Vol2Material ById(long id) => id > 0 && co.Materials.TryGet(new MaterialId(id), out var m) ? m : null;
            try
            {
                switch (c.Kind)
                {
                    case "dir": EvalDirection(c, sc, log, decision, ctx, v, Need, led, n, sameMul, childMul, blocked, dmgCap, hpPerUnit); break;
                    case "child":
                        {
                            Need(led.Count == 2, "two reactions in the ledger (" + led.Count + ")");
                            if (led.Count == 2)
                            {
                                var first = led[0]; var second = led[1];
                                var child = ById(first.ChildId);
                                Need(first.Relation == R.Kind.Feed && first.ChildId > 0 && child != null && child.Body == Body.Fire && child.Depth == 1, "the first wood path feeds the fire patch: a fire child, depth 1");
                                Need(second.Relation == R.Kind.Feed && second.ChildId == 0 && second.ChildConsumed.Contains(first.ChildId) && second.ContactId == first.ChildId, "the second wood path meets the CHILD: the child only dries (ChildConsumed), no new child");
                                Need(child != null && child.End == Vol2MaterialEnd.ChildConsumed, "the child ended ChildConsumed (no direction)");
                                Need(co.Materials.TotalCreated - x.CreatedAtBuild == 2, "two materials were made in this row (the planted patch and the child): no grandchild (" + (co.Materials.TotalCreated - x.CreatedAtBuild) + ")");
                                Need(second.ActingCast != first.ActingCast, "two different casts");
                                v.MaterialIds = first.ContactId + "|" + first.ChildId; v.Created = first.ChildId.ToString(CultureInfo.InvariantCulture); v.Consumed = first.ContactId + ":Consumed|" + first.ChildId + ":ChildConsumed"; v.Depth = "1";
                            }
                            break;
                        }
                    case "samecast":
                        {
                            Need(led.Count == 1 && led[0].ActingIsShape && led[0].Relation == R.Kind.Transfer && led[0].ActingCast == log.Cast, "ONE reaction: the fire shape burned the wood patch (옮겨붙음); the cast's own residue never reacted (" + led.Count + " in the ledger)");
                            var res = log.Materials.FirstOrDefault();
                            Need(res != null && res.Kind == Vol2MaterialKind.Residue && res.Cast == log.Cast, "the cast laid its residue");
                            Need(res != null && !led.Any(r => r.ActingId == res.Id.Value || r.ContactId == res.Id.Value), "the residue is in no reaction (same cast as the shape and the child)");
                            Need(led.Count == 0 || rx.Audit().Count == 0, "audit clean");
                            if (led.Count == 1) { v.Created = led[0].ChildId.ToString(CultureInfo.InvariantCulture); v.Depth = led[0].ChildDepth.ToString(CultureInfo.InvariantCulture); }
                            break;
                        }
                    case "steal":
                        {
                            Need(led.Count == 1 && !led[0].ActingIsShape && led[0].Relation == R.Kind.Transfer && led[0].ActingFaction == Vol2Faction.Enemy && led[0].ContactFaction == Vol2Faction.Player, "the enemy's NEWER fire acts on my wood (옮겨붙음) (" + led.Count + " reactions)");
                            var child = led.Count == 1 ? ById(led[0].ChildId) : null;
                            Need(child != null && child.Body == Body.Fire && child.FactionId == Vol2Faction.Enemy && child.OwnerId == 2 && child.Depth == 1, "the child is fire and it is the ENEMY's (the acting owner takes it)");
                            Need(rx.Audit().Count == 0, "audit clean");
                            if (child != null) { v.MaterialIds = led[0].ContactId + "|" + child.Id; v.Created = child.Id.Value.ToString(CultureInfo.InvariantCulture); v.Owner = "enemy (stolen from caster)"; v.Depth = "1"; }
                            break;
                        }
                    case "gate":
                        {
                            Need(led.Count == 1 && led[0].ActingIsShape && led[0].Relation == R.Kind.Transfer && x.World != null && x.World.End == Vol2MaterialEnd.Consumed, "the fire shape burned the world's vine door (the world patch ended Consumed)");
                            Need(x.Gate != null && x.Gate.IsOpen, "the SpellFieldGate308 the vine is the door of is OPEN");
                            Need(ctx.Shapes.Field != null && ctx.Shapes.Field.Log.Any(s => s.Contains("burnt open")), "the field adapter logged it (" + (ctx.Shapes.Field != null ? string.Join(" / ", ctx.Shapes.Field.Log.Skip(Math.Max(0, ctx.Shapes.Field.Log.Count - 2))) : "no field adapter") + ")");
                            if (x.World != null) v.MaterialIds = x.World.Id.Value.ToString(CultureInfo.InvariantCulture);
                            v.Owner = "world";
                            break;
                        }
                    case "ring":
                        {
                            var rings = co.Materials.ByCast(log.Cast);
                            long second = ctx.Services.Ids.CastsIssued;
                            var other = co.Materials.ByCast(new CastId(second));
                            Need(rings.Count >= 1 && rings[0].End == Vol2MaterialEnd.Replaced, "the first ring residue ended Replaced (" + (rings.Count > 0 ? rings[0].End.ToString() : "none") + ")");
                            Need(other.Count >= 1 && other[0].End != Vol2MaterialEnd.Replaced && other[0].Footprint.Kind == Vol2FootprintKind.Ring && other[0].Body == Body.Earth, "the second ring (earth) took the slot and was not replaced (" + (other.Count > 0 ? other[0].End.ToString() : "none") + ")");
                            Need(rx.RingSlot.Replaced == 1, "the ring slot replaced exactly one ring (" + rx.RingSlot.Replaced + ")");
                            Need(led.Count == 0, "the two rings never reacted with each other (the old one was gone before the sweep): " + led.Count + (led.Count > 0 ? " [" + string.Join("; ", led.Select(r => r + (r.ActingIsShape ? " shape" : " standing") + " cast " + r.ActingCast + "/" + r.ContactCast)) + "]" : ""));
                            if (rings.Count > 0) v.MaterialIds = rings[0].Id + "|" + (other.Count > 0 ? other[0].Id.Value.ToString() : "");
                            v.Consumed = rings.Count > 0 ? rings[0].Id + ":" + rings[0].End : "";
                            break;
                        }
                    case "seams":
                        {
                            var kit = ctx.Kit;
                            Need(kit != null, "the defence kit is bound");
                            if (kit != null)
                            {
                                var hub = Vol2SeedExecutor.HubOf(co);
                                Need(kit.HitGate != null && hub != null && ReferenceEquals(kit.HitGate, hub.Hits) && hub.Hits.Attached, "멈: the kit's HitGate is the seed hub's gate and it is attached to the player (the stagger listener asks it)");
                                Need(hub != null && hub.Gather == kit.Gather && hub.ActiveSeedPosture != null && hub.Drawing != null, "심는다: Vol2SeedWiring.Bind ran in the installer (갈무리, posture, the drawing state)");
                                Need(co.Verbs.TryGet(Verb.Split, out var s) && s is Vol2SplitExecutor se && se.Blade is Vol2PostureBladePort, "가른다: the 날붓 port is bound explicitly (Vol2PostureBladePort)");
                                Need(co.Verbs.TryGet(Verb.Return, out var r) && r is Vol2ReturnExecutor re && Math.Abs(re.Tuning.HpPerUnit - kit.HpPerUnit) < 1e-6f, "돌아온다: the borrowed power's HP-per-unit is the kit's (" + kit.HpPerUnit.ToString(CultureInfo.InvariantCulture) + ")");
                                Need(kit.PaysOrdinaryCasts, "the kit pays ordinary casts (the installer ran after the shape runtime)");
                            }
                            Need(ctx.Shapes.Field != null && ReferenceEquals(co.Field, ctx.Shapes.Field), "the field gates (Vol2FieldGateAdapter) are the coordinator's field port");
                            Need(ctx.Shapes.World != null && ctx.Shapes.Block != null, "the world's materials and the 뭄 dash block exist");
                            break;
                        }
                }
            }
            finally { if (_extra.TryGetValue(sc, out var done)) { Release(done, ctx); } _extra.Remove(sc); }
            v.Checks = string.Join(" | ", checks);
            v.Pass = problems.Count == 0; v.Note = string.Join("; ", problems);
            return v;
        }

        void EvalDirection(Vol2ReactionCase c, Vol2LabScenario sc, Vol2CastLog log, Vol2CastDecision decision, Vol2LabContext ctx, Vol2OnceVerdict v, Action<bool, string> Need, IReadOnlyList<Vol2ReactionRecord> led,
            NumbersV3Dto n, decimal sameMul, decimal childMul, decimal blocked, decimal dmgCap, float hpPerUnit)
        {
            var co = ctx.Coordinator; var rx = co.Reactions;
            int a = c.Acting, b = c.Contacted; var kind = Expect(a, b);
            decimal ghost = decision != null && decision.Plan != null ? decision.Plan.Total : 0m;
            Need(ghost > 0m, "the bare path's plan total is the shape's reaction balance (" + Fmt(ghost) + ")");
            Need(led.Count == 1, "exactly one reaction (" + led.Count + ")");
            if (led.Count != 1) return;
            var rec = led[0];
            sc.Planted.TryGetValue("B", out var bm);
            Need(rec.ActingIsShape && rec.ActingCast == log.Cast && (int)rec.ActingBody == a && (int)rec.ContactedBody == b && rec.Relation == kind && bm != null && rec.ContactId == bm.Id.Value,
                "the " + Names[a] + " shape acted on the planted " + Names[b] + " patch: " + kind);
            Need(rec.FromShape && rec.Groggy == 0m && rec.SeedTriggers == 0 && rx.Audit().Count == 0, "ledger audit clean; groggy 0; no seed trigger");
            Need(log.Contacts.All(k => k.Kind == "Instant" || k.Kind == "Touch"), "the cast's own contacts are the direct hit only; the reaction is no contact of the cast");
            string product = kind.ToString();
            switch (kind)
            {
                case R.Kind.Same:
                    Need(bm != null && bm.Thickened && Near(bm.Remaining, (ghost + 0.9m) * sameMul), "비화: the patch is thickened to (" + Fmt(ghost) + " + 0.9) x 1.2 = " + Fmt((ghost + 0.9m) * sameMul));
                    break;
                case R.Kind.Block:
                    Need(bm != null && Near(bm.Remaining, 0.9m * blocked), "막힘: the patch keeps half (0.45)");
                    break;
                case R.Kind.Feed:
                case R.Kind.Transfer:
                    {
                        var child = ById(co, rec.ChildId);
                        int generated = kind == R.Kind.Feed ? b : a;
                        Need(bm != null && bm.End == Vol2MaterialEnd.Consumed && child != null && (int)child.Body == generated && Near(child.Remaining, (ghost + 0.9m) * childMul) && child.Depth == 1 && child.OwnerId == 1 && child.FactionId == Vol2Faction.Player,
                            "상생: the patch is consumed, ONE child of " + Names[generated] + ", (" + Fmt(ghost) + " + 0.9) x 0.5 = " + Fmt((ghost + 0.9m) * childMul) + ", depth 1, mine");
                        if (child != null) { v.Created = child.Id.Value.ToString(CultureInfo.InvariantCulture); v.Depth = "1"; v.Owner = "caster (acting)"; }
                        break;
                    }
                default:
                    {
                        Need(bm != null && bm.End == Vol2MaterialEnd.Consumed && rec.ChildId == 0, "꺾음: the patch is consumed, no child");
                        bool slow = (a == 0 && b == 2) || (a == 2 && b == 4);
                        decimal budget = ghost * dmgCap;
                        if (slow)
                        {
                            Need(rec.SlowsApplied == 1 && rec.Hits == 0 && rec.Affected == 1, "slow product: one bind on the body in the patch, no damage");
                            product = "slow";
                        }
                        else
                        {
                            int ticks = a == 1 ? 4 : 1;
                            Need(rec.Affected == 1 && rec.Hits == ticks && Near(rec.UnitsGiven, budget), "damage product: " + ticks + " Reaction request(s) sharing " + Fmt(ghost) + " x 0.8 = " + Fmt(budget) + " units (" + rec.Hits + " sent, " + Fmt(rec.UnitsGiven) + " given)");
                            Need(Math.Abs(rec.HpApplied - (float)budget * hpPerUnit) < 0.05f, "the sink took " + rec.HpApplied.ToString("0.###", CultureInfo.InvariantCulture) + " HP = " + Fmt(budget) + " units x " + hpPerUnit.ToString(CultureInfo.InvariantCulture) + " HP per unit");
                            product = "damage x" + ticks;
                            v.Units = rec.UnitsGiven; v.Hp = rec.HpApplied;
                        }
                        break;
                    }
            }
            v.MaterialIds = rec.ContactId + (rec.ChildId > 0 ? "|" + rec.ChildId : "");
            v.Consumed = string.Join("|", rec.Consumed.Select(id => id + ":Consumed")) + (kind == R.Kind.Block ? "|" + rec.ContactId + ":half" : "") + (kind == R.Kind.Same ? "|" + rec.ContactId + ":thickened" : "");
            v.Balance = Fmt(ghost);
            v.Ledger = kind + ":" + Names[a] + ">" + Names[b] + ":" + product;
            v.Hit = "T"; v.Expected = "T";
            v.BodyObserved = product;
        }

        static Vol2Material ById(Vol2CastCoordinator co, long id) => id > 0 && co.Materials.TryGet(new MaterialId(id), out var m) ? m : null;
    }
}
