using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using static Oheangbu.EditorTools.Vol2.Vol2ShapeFixtureKit;
using Object = UnityEngine.Object;
using R = Oheangbu.Vol2.Rules.ReactionRulesV3;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-b slice 8 edit-mode fixture: the 25-direction reaction engine on real contacts, the world's materials, the field gates, the ring slot, the dash block and the seams. No scene,
    /// no Play, a hand-driven clock. Queue:
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2ReactionFixture Run "all"
    ///   C00 25 cells: two standing patches overlap (the newer acts); relation, who is consumed, thickened / halved, the child (body, amount, depth 1, owner, lineage, life), the product
    ///       (slow / damage / ticks / occlusion), damage requests all Vol2DamageOrigin.Reaction, no cast contact raised (no seed hears it), ledger audit clean, one visual
    ///   C90/C91 the old looks: exactly three of 25 map to ReactionKind308 (들불 Fire>Wood, 김 Water>Fire, 진창 Earth>Water)
    ///   S1  same cast refused (rules), a child and its parent's cast refused; S2 a returning shape cannot consume the same material again (ReturnSameMaterial / DuplicateContact)
    ///   G1  no grandchildren: a child's re-contact only dries it (ChildConsumed, 0.3 s, no new material); G2 repeated ticks add nothing (double reaction 0)
    ///   Q1  reservation race: two actors on one material in one tick - one commit, the other lost; a half-blocked material is read again (revision) and goes on
    ///   D1  direction: the newer acts; equal time -> the higher cast; a MOVING one acts first even if older
    ///   P1  a split half is read like any material; O1 ownership: the child takes the acting owner (stolen when the enemy acts), the enemy igniter's damage cap
    ///   E1  eligibility: a player's body-attached material reacts through a hostile shape touching the host, and an enemy caster cannot choose it
    ///   B1  boundary: a patch ending at the tick is not read; E0 engine off = nothing
    ///   M1  REAL cast: a fire path flies over a standing metal patch; M2 a fan lands on a wood patch; M3 a ring lands on a water patch
    ///   W1  world materials: placed with a world cast id and the world faction, burnt by fire, the vine door opens its gate
    ///   F1  field gates: 숫 cuts the boulder, 웅 cleanses the pollution band, 눈 burns the tagged vine, nothing opens in combat, a platform is noted
    ///   R1  ring slot (둘레 1): a second ring of mine replaces the first; the world's, the enemy's, the same cast's and the child's are left alone
    ///   K1  뭄 dash block: an enemy on the hardened ground carries dash x0 for the hold time, one beside it does not
    ///   H1  멈 seam: the stagger listener that runs BEFORE the gate gets the exemption (EnsureReported) and the hit is reported once
    /// Expected values are written here from NumbersV3, the 25-cell table and the wuxing cycle, never read back from a run.</summary>
    public static class Vol2ReactionFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-reaction-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-reaction-fixture: editor busy (play / compile / import)";
            var r = new Report();
            var made = new List<Object>();
            try { using (var errors = new ErrorCounter()) { Cases(r, made); Check(r, "E9", errors.Errors == 0, "no error log during the fixture" + (errors.Errors > 0 ? ": " + errors.First : "")); } }
            catch (Exception e) { Check(r, "EXCEPTION", false, e.ToString()); }
            finally { foreach (var o in made) if (o != null) Object.DestroyImmediate(o); }
            return JsonUtility.ToJson(r, true);
        }

        static void Check(Report r, string id, bool ok, string detail)
        {
            if (ok) r.pass++; else r.fail++;
            r.lines.Add((ok ? "PASS " : "FAIL ") + id + " " + detail);
        }

        static bool Eq(decimal a, decimal b) => Math.Abs(a - b) <= 0.000000001m;
        static readonly string[] BodyNames = { "Wood", "Fire", "Earth", "Metal", "Water" };

        // ---- the wuxing cycle, written down here and nowhere read from the rules ----
        static readonly int[] Over = { 2, 3, 4, 0, 1 };      // Wood > Earth, Fire > Metal, Earth > Water, Metal > Wood, Water > Fire
        static R.Kind Expect(int a, int b)
        {
            if (a == b) return R.Kind.Same;
            if ((a + 1) % 5 == b) return R.Kind.Feed;
            if ((b + 1) % 5 == a) return R.Kind.Transfer;
            return Over[a] == b ? R.Kind.Break : R.Kind.Block;
        }

        static Rig NewRig(Vol2Rulebook book, List<Vol2ReactionVisual> events = null, bool enabled = true)
        {
            var rig = Rig.Create(book);
            rig.Coord.Reactions.Enabled = enabled;
            if (events != null) rig.Coord.Reactions.ReactionResolved += events.Add;
            return rig;
        }

        static long OwnerOf(long faction) => faction == Vol2Faction.Enemy ? 2 : faction == Vol2Faction.World ? 3 : 1;

        static Vol2Material Mat(Rig rig, long cast, Body body, long faction, float ahead, float side, float radius = 0.6f, decimal amount = 0.9m, double lifeSeconds = 10.0, int depth = 0, string tag = "")
            => rig.Coord.Materials.Create(new CastId(cast), '가', body, Vol2MaterialKind.Residue, amount, Vol2Time.FromSeconds(lifeSeconds), Vol2Footprint.Disc(At(ahead, side), radius),
                OwnerOf(faction), faction, true, rig.Services.Clock.NowUs, depth, 0, tag);

        static Vol2ReactionShape Shape(Rig rig, long cast, Body body, long faction = Vol2Faction.Player, decimal balance = 1m)
            => new Vol2ReactionShape { Cast = new CastId(cast), Body = body, FactionId = faction, OwnerId = OwnerOf(faction), Kind = Vol2ShapeKind.Path, StartUs = rig.Services.Clock.NowUs,
                ExpiresUs = rig.Services.Clock.NowUs + 10000000L, Balance = balance, DirectBudget = balance };

        static void Cases(Report r, List<Object> made)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "F0", book.Ok, book.Ok ? "rulebook loaded" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var n = book.Numbers;
            decimal sameMul = n.Get("reaction.sameBody.maxMultiplier"), childMul = n.Get("reaction.child.parentMultiplier"), blocked = n.Get("reaction.blocked.remaining"), dmgCap = n.Get("reaction.damageCap"), enemyCap = n.Get("reaction.enemy.damageCap");
            long childLife = RuleTypesV3.Microseconds(n.Get("timing.child.life")), fade = RuleTypesV3.Microseconds(n.Get("reaction.child.fade"));
            Check(r, "F1", Eq(sameMul, 1.2m) && Eq(childMul, 0.5m) && Eq(blocked, 0.5m) && Eq(dmgCap, 0.8m) && Eq(enemyCap, 0.5m) && childLife == 10000000 && fade == 300000 && Eq(n.Get("reaction.depth"), 1m), "table numbers: same x1.2, child x0.5, blocked x0.5, damage cap 0.8 (enemy 0.5), child life 10 s, dry 0.3 s, depth 1");

            // ---------------------------------------------------------------- C00: the 25 cells
            int legacy = 0;
            for (int a = 0; a < 5; a++)
                for (int b = 0; b < 5; b++)
                {
                    string id = "C" + a + b + " " + BodyNames[a] + ">" + BodyNames[b];
                    var events = new List<Vol2ReactionVisual>();
                    var rig = NewRig(book, events);
                    int cast = 0; rig.Coord.ContactResolved += (l, c) => cast++;
                    var bm = Mat(rig, 9101, (Body)b, Vol2Faction.Enemy, 4, 0, 0.8f);          // the contacted one: an enemy patch, older
                    var tgt = rig.Add("T", 4, 0.3f);                                          // an enemy body standing in it
                    rig.Advance(0.05, 1);
                    var am = Mat(rig, 9102, (Body)a, Vol2Faction.Player, 4, 0.5f, 0.6f);       // the acting one: mine, newer
                    rig.Advance(0.05, 1);
                    var kind = Expect(a, b);
                    long now = rig.Services.Clock.NowUs;
                    bool oneEvent = events.Count == 1 && events[0].Relation == kind && events[0].ActingBody == (Body)a && events[0].ContactedBody == (Body)b && !events[0].ActingIsShape;
                    Check(r, id + " a", oneEvent && rig.Coord.Reactions.Committed == 1 && rig.Coord.Reactions.Ledger.Count == 1, "one committed reaction, relation " + kind + ", acting " + BodyNames[a] + ", contacted " + BodyNames[b] + " (the newer acts)");
                    if (events.Count != 1) continue;
                    var v = events[0];
                    long ownerAfter = v.OwnerId;
                    switch (kind)
                    {
                        case R.Kind.Same:
                            Check(r, id + " b", am.End == Vol2MaterialEnd.Consumed && bm.Alive && bm.Thickened && Eq(bm.Remaining, (0.9m + 0.9m) * sameMul) && bm.ExpiresUs == v.AtUs + 10000000 && v.ChildId == 0 && v.ContactEnd == Vol2MaterialEnd.None,
                                "비화: the acting one is consumed, the contacted one is the SAME material made thicker: (0.9 + 0.9) x 1.2 = 2.16, life renewed from now, no child");
                            break;
                        case R.Kind.Feed:
                        case R.Kind.Transfer:
                            {
                                var child = v.ChildId > 0 && rig.Coord.Materials.TryGet(new MaterialId(v.ChildId), out var c) ? c : null;
                                int generated = kind == R.Kind.Feed ? b : a;          // the body the other one generates
                                Check(r, id + " b", am.End == Vol2MaterialEnd.Consumed && bm.End == Vol2MaterialEnd.Consumed && child != null && child.Alive && (int)child.Body == generated && Eq(child.Remaining, (0.9m + 0.9m) * childMul) &&
                                    child.Depth == 1 && child.Cast.Value == 9102 && child.Ancestors != null && child.Ancestors.Contains(9101) && child.OwnerId == 1 && child.FactionId == Vol2Faction.Player && child.LifeUs == childLife && child.Origin == "reaction",
                                    "상생: both consumed, ONE child of the body that is generated (" + BodyNames[generated] + "), (0.9 + 0.9) x 0.5 = 0.9, depth 1, cast of the acting one with both lineages, the acting owner, 10 s");
                                break;
                            }
                        case R.Kind.Block:
                            Check(r, id + " b", am.End == Vol2MaterialEnd.Consumed && bm.Alive && Eq(bm.Remaining, 0.9m * blocked) && v.ChildId == 0 && v.ContactEnd == Vol2MaterialEnd.None,
                                "막힘: the acting one is spent, the contacted one keeps half (0.45) and stays");
                            break;
                        default:
                            Check(r, id + " b", am.End == Vol2MaterialEnd.Consumed && bm.End == Vol2MaterialEnd.Consumed && v.ChildId == 0, "꺾음: both consumed, no child");
                            break;
                    }
                    // the product
                    decimal budget = 0.9m * dmgCap;
                    rig.Advance(2.3, 23);
                    var dmg = rig.Damage.Requests; var slow = rig.Status.Requests;
                    if (kind == R.Kind.Break)
                    {
                        bool slowCell = (a == 0 && b == 2) || (a == 2 && b == 4);
                        if (slowCell)
                        {
                            long dur = RuleTypesV3.Microseconds(n.Get(a == 0 ? "reaction.woodEarth.slow" : "reaction.earthWater.slow"));
                            Check(r, id + " c", v.Product == R.Product.Slow && slow.Count == 1 && slow[0].Kind == Vol2BodyActionKind.Bind && slow[0].DurationUs == dur && slow[0].Magnitude == 0.5f && slow[0].Target == tgt && dmg.Count == 0 && v.DurationUs == dur,
                                "slow product: one bind of " + dur / 1000000 + " s x0.5 on the body in the patch, no damage");
                        }
                        else
                        {
                            bool lasting = a == 1;           // 화극금 달군 쇠: 2 s of ticks
                            int expectTicks = lasting ? 4 : 1;
                            decimal sum = 0m; foreach (var q in dmg) sum += q.Units;
                            Check(r, id + " c", v.Product == R.Product.Damage && dmg.Count == expectTicks && Eq(sum, budget) && dmg.All(q => q.Origin == Vol2DamageOrigin.Reaction && q.Lasting && q.IsReaction && q.Target == tgt) && slow.Count == 0 &&
                                (a != 4 || v.OcclusionUs == 300000) && (!lasting || v.DurationUs == 2000000),
                                "damage product: " + expectTicks + " request(s) in all 0.9 x 0.8 = 0.72 units (shared), every one a Reaction request" + (a == 4 ? ", the steam hides for 0.3 s" : "") + (lasting ? ", over 2 s" : ""));
                        }
                    }
                    else Check(r, id + " c", dmg.Count == 0 && slow.Count == 0 && v.Product != R.Product.Damage && v.Product != R.Product.Slow, "no damage and no slow from a " + kind);
                    var audit = rig.Coord.Reactions.Audit();
                    Check(r, id + " d", audit.Count == 0 && cast == 0 && rig.Coord.Reactions.Ledger[0].Groggy == 0m && rig.Coord.Reactions.Ledger[0].SeedTriggers == 0 && rig.Coord.Reactions.ActorMismatch == 0 && rig.Coord.Reactions.Exceptions == 0,
                        "ledger audit clean" + (audit.Count > 0 ? " (" + string.Join("; ", audit) + ")" : "") + "; the reaction was never announced as a contact of a cast (0 seed hears it); groggy 0; the engine predicted the acting side right");
                    if (v.Legacy != ReactionKind308.None) legacy++;
                    _ = now; _ = ownerAfter;
                }
            Check(r, "C90", legacy == 3, "exactly three of the 25 cells have an old look (들불, 김, 진창): " + legacy);
            Check(r, "C91", Vol2ReactionVisual.LegacyOf(R.Kind.Transfer, Body.Fire, Body.Wood) == ReactionKind308.Wildfire && Vol2ReactionVisual.LegacyOf(R.Kind.Break, Body.Water, Body.Fire) == ReactionKind308.Steam &&
                Vol2ReactionVisual.LegacyOf(R.Kind.Break, Body.Earth, Body.Water) == ReactionKind308.Mud && Vol2ReactionVisual.LegacyOf(R.Kind.Break, Body.Fire, Body.Metal) == ReactionKind308.None && Vol2ReactionVisual.LegacyOf(R.Kind.Feed, Body.Wood, Body.Fire) == ReactionKind308.None,
                "화>목 옮겨붙음 = Wildfire, 수>화 꺾음 = Steam, 토>수 꺾음 = Mud (prefab TODO: MudPrefabMissing), everything else None");

            // ---------------------------------------------------------------- S1 same cast
            {
                var rig = NewRig(book);
                var x = Mat(rig, 9201, Body.Wood, Vol2Faction.Player, 4, 0); var y = Mat(rig, 9201, Body.Fire, Vol2Faction.Player, 4, 0.3f);
                rig.Advance(0.1, 2);
                bool sweepSkips = rig.Coord.Reactions.Committed == 0 && rig.Coord.Reactions.Attempts.Count == 0;
                rig.Coord.Reactions.SubmitMaterials(x.Id, y.Id); rig.Advance(0.05, 1);
                var att = rig.Coord.Reactions.Attempts;
                Check(r, "S1a", sweepSkips && att.Count == 1 && !att[0].Accepted && att[0].Reason == "SameCast" && x.Alive && y.Alive && rig.Coord.Reactions.Committed == 0, "two materials of ONE cast: the sweep does not even try; submitted by hand the rules refuse (SameCast); both stay");
                var bm = Mat(rig, 9202, Body.Water, Vol2Faction.Enemy, 8, 0); rig.Advance(0.02, 1); var am = Mat(rig, 9203, Body.Wood, Vol2Faction.Player, 8, 0.4f); rig.Advance(0.05, 1);      // water feeds wood: a child (cast 9203, lineage 9202 + 9203)
                var ledger = rig.Coord.Reactions.Ledger; var child = ledger.Count == 1 && ledger[0].ChildId > 0 ? rig.Coord.Materials.TryGet(new MaterialId(ledger[0].ChildId), out var c) ? c : null : null;
                var again = Mat(rig, 9202, Body.Fire, Vol2Faction.Enemy, 8, 0.2f);       // a new material of the PARENT's cast touches the child
                rig.Coord.Reactions.SubmitMaterials(again.Id, child != null ? child.Id : again.Id); rig.Advance(0.05, 1);
                var last = rig.Coord.Reactions.Attempts.Last();
                Check(r, "S1b", child != null && !last.Accepted && last.Reason == "SameCast" && rig.Coord.Reactions.Ledger.Count == 1 && child.Alive, "a reaction child carries both parents' casts: a material of a PARENT's cast cannot react with it (SameCast)");
            }

            // ---------------------------------------------------------------- S2 return
            {
                var rig = NewRig(book);
                var metal = Mat(rig, 9301, Body.Metal, Vol2Faction.Enemy, 5, 0, 1.0f);
                var s1 = Shape(rig, 9302, Body.Wood);
                rig.Coord.Reactions.SubmitShape(s1, metal.Id); rig.Advance(0.05, 1);
                Check(r, "S2a", rig.Coord.Reactions.Committed == 1 && s1.Consumed && metal.Alive && Eq(metal.Remaining, 0.45m) && rig.Coord.Reactions.Ledger[0].ActingIsShape && rig.Coord.Reactions.Ledger[0].Relation == R.Kind.Block,
                    "a wood shape meets a metal patch: blocked (the shape's balance is spent, the patch keeps half)");
                var s2 = Shape(rig, 9302, Body.Wood);        // the same cast on its way BACK, fresh balance
                rig.Coord.Reactions.SubmitShape(s2, metal.Id, returning: true); rig.Advance(0.05, 1);
                var a1 = rig.Coord.Reactions.Attempts.Last();
                Check(r, "S2b", !a1.Accepted && a1.Reason == "ReturnSameMaterial" && rig.Coord.Reactions.Committed == 1 && Eq(metal.Remaining, 0.45m), "the returning shape cannot consume the same material again (ReturnSameMaterial); nothing changes");
                var s3 = Shape(rig, 9302, Body.Wood);
                rig.Coord.Reactions.SubmitShape(s3, metal.Id, returning: false); rig.Advance(0.05, 1);
                var a2 = rig.Coord.Reactions.Attempts.Last();
                Check(r, "S2c", !a2.Accepted && a2.Reason == "DuplicateContact" && rig.Coord.Reactions.Committed == 1, "the same cast meeting the same material twice outside a return is the duplicate contact (DuplicateContact)");
                var audit = rig.Coord.Reactions.Audit();
                Check(r, "S2d", audit.Count == 0, "the ledger holds one contact for that cast and that material");
            }

            // ---------------------------------------------------------------- G1 no grandchildren
            {
                var events = new List<Vol2ReactionVisual>();
                var rig = NewRig(book, events);
                var mB = Mat(rig, 9401, Body.Fire, Vol2Faction.Enemy, 4, 0); rig.Advance(0.02, 1);
                var mA = Mat(rig, 9402, Body.Wood, Vol2Faction.Player, 4, 0.4f); rig.Advance(0.05, 1);       // wood feeds fire: child (fire, depth 1)
                Check(r, "G1a", events.Count == 1 && events[0].Relation == R.Kind.Feed && events[0].ChildId > 0, "wood feeds fire: a child is born");
                var child = rig.Coord.Materials.TryGet(new MaterialId(events[0].ChildId), out var c) ? c : null;
                long created = rig.Coord.Materials.TotalCreated;
                var d = Mat(rig, 9403, Body.Wood, Vol2Faction.Player, 4, 0.2f); long createdWithD = rig.Coord.Materials.TotalCreated;
                rig.Advance(0.05, 1);
                Check(r, "G1b", events.Count == 2 && events[1].Relation == R.Kind.Feed && events[1].ChildId == 0 && rig.Coord.Materials.TotalCreated == createdWithD && child != null && child.End == Vol2MaterialEnd.ChildConsumed && d.End == Vol2MaterialEnd.Consumed &&
                    events[1].DryUs == fade && events[1].ContactEnd == Vol2MaterialEnd.ChildConsumed && events[1].ContactReason.StartsWith("ChildConsumed"),
                    "a wood patch touches the CHILD of a feeding: the child only dries (ChildConsumed, 0.3 s, no direction), the other is consumed, and NO new material is made (no grandchild)");
                rig.Advance(1.0, 20);
                Check(r, "G1c", rig.Coord.Reactions.Committed == 2 && rig.Coord.Reactions.Audit().Count == 0 && rig.Coord.Reactions.Ledger.All(x => x.ChildDepth <= 1), "nothing more happens: two reactions in the ledger, the audit is clean");
                // a child can still be reacted with by a different product (a break), and that never makes a child either
                var events2 = new List<Vol2ReactionVisual>();
                var rig2 = NewRig(book, events2);
                Mat(rig2, 9411, Body.Fire, Vol2Faction.Enemy, 4, 0); rig2.Advance(0.02, 1); Mat(rig2, 9412, Body.Wood, Vol2Faction.Player, 4, 0.4f); rig2.Advance(0.05, 1);
                var water = Mat(rig2, 9413, Body.Water, Vol2Faction.Player, 4, 0.2f); long before = rig2.Coord.Materials.TotalCreated; rig2.Advance(0.05, 1);
                Check(r, "G1d", events2.Count == 2 && events2[1].Relation == R.Kind.Break && events2[1].ChildId == 0 && rig2.Coord.Materials.TotalCreated == before && water.End == Vol2MaterialEnd.Consumed, "water breaks the fire child: damage product, no child");
            }

            // ---------------------------------------------------------------- G2 double reaction 0
            {
                var rig = NewRig(book);
                var bm = Mat(rig, 9501, Body.Metal, Vol2Faction.Enemy, 4, 0, 1.0f); rig.Advance(0.02, 1);
                var am = Mat(rig, 9502, Body.Wood, Vol2Faction.Player, 4, 0.5f);
                rig.Advance(2.0, 100);
                int accepted = rig.Coord.Reactions.Attempts.Count(x => x.Accepted);
                Check(r, "G2a", rig.Coord.Reactions.Committed == 1 && accepted == 1 && rig.Coord.Reactions.Attempts.Count == 1 && bm.Alive && Eq(bm.Remaining, 0.45m), "100 ticks with the blocked patch still lying there: ONE reaction, ONE attempt (no re-try of a settled pair), the patch keeps half");
                var c3 = Mat(rig, 9503, Body.Wood, Vol2Faction.Player, 4, -0.5f);       // a THIRD material (another cast) may block it again: that is a new contact, not a double
                rig.Advance(0.1, 2);
                Check(r, "G2b", rig.Coord.Reactions.Committed == 2 && Eq(bm.Remaining, 0.225m), "another cast's wood is a new contact: blocked again (0.45 x 0.5)");
                var same1 = Mat(rig, 9511, Body.Earth, Vol2Faction.Enemy, 12, 0, 1.0f); rig.Advance(0.01, 1); var same2 = Mat(rig, 9512, Body.Earth, Vol2Faction.Player, 12, 0.3f); rig.Advance(0.05, 1);
                var same3 = Mat(rig, 9513, Body.Earth, Vol2Faction.Player, 12, -0.3f); rig.Advance(0.05, 1);
                var rej = rig.Coord.Reactions.Attempts.Last();
                Check(r, "G2c", same1.Thickened && same3.Alive && !rej.Accepted && rej.Reason == "SameBodyOnce", "비화 happens once per material: a second same-body patch finds it thickened (SameBodyOnce) and is not consumed");
            }

            // ---------------------------------------------------------------- Q1 reservation race
            {
                // A: Fire acts first on metal (Break: both consumed) - the second actor, wood, finds the metal gone
                var events = new List<Vol2ReactionVisual>();
                var rig = NewRig(book, events);
                var bm = Mat(rig, 9601, Body.Metal, Vol2Faction.Enemy, 4, 0, 1.0f); rig.Advance(0.01, 1);
                var a1 = Mat(rig, 9602, Body.Fire, Vol2Faction.Player, 4, 0.9f, 0.3f);
                var a2 = Mat(rig, 9603, Body.Wood, Vol2Faction.Player, 4, -0.9f, 0.3f);
                rig.Advance(0.05, 1);
                Check(r, "Q1a", events.Count == 1 && events[0].ActingBody == Body.Fire && !bm.Alive && a2.Alive && rig.Coord.Reactions.Lost == 1 && rig.Coord.Reactions.Committed == 1,
                    "two actors on one metal patch in ONE tick: the first by order commits (fire breaks it), the second finds it gone: lost in the reservation, not consumed, not double-spent");
                // B: Wood acts first (Block: metal halved) - then fire acts on the half
                var events2 = new List<Vol2ReactionVisual>();
                var rig2 = NewRig(book, events2);
                var bm2 = Mat(rig2, 9611, Body.Metal, Vol2Faction.Enemy, 4, 0, 1.0f); rig2.Advance(0.01, 1);
                var w = Mat(rig2, 9612, Body.Wood, Vol2Faction.Player, 4, 0.9f, 0.3f);
                var f = Mat(rig2, 9613, Body.Fire, Vol2Faction.Player, 4, -0.9f, 0.3f);
                var tgt = rig2.Add("T", 4, 0.2f);
                rig2.Advance(0.05, 1);
                Check(r, "Q1b", events2.Count == 2 && events2[0].Relation == R.Kind.Block && events2[1].Relation == R.Kind.Break && rig2.Coord.Reactions.Revalidated == 1 && !bm2.Alive && Eq(events2[1].DamageBudget, 0.9m * dmgCap) && rig2.Coord.Reactions.Lost == 0,
                    "the first actor blocks the metal (it keeps half): the second's contact had read the OLD revision, reads it again and goes on - fire breaks the half-patch; the product is priced by the actor's 0.9, not the patch's remainder");
            }

            // ---------------------------------------------------------------- D1 direction
            {
                var ev1 = new List<Vol2ReactionVisual>();
                var rig = NewRig(book, ev1);
                var x = Mat(rig, 9701, Body.Fire, Vol2Faction.Enemy, 4, 0, 0.8f); rig.Advance(0.03, 1); var y = Mat(rig, 9702, Body.Metal, Vol2Faction.Player, 4, 0.4f, 0.6f); rig.Advance(0.05, 1);
                Check(r, "D1a", ev1.Count == 1 && ev1[0].ActingBody == Body.Metal && ev1[0].Relation == R.Kind.Block && !y.Alive && x.Alive && Eq(x.Remaining, 0.45m), "two standing patches: the NEWER (metal) acts - blocked by the older fire, which keeps half");
                var ev2 = new List<Vol2ReactionVisual>();
                var rigB = NewRig(book, ev2);
                var p = Mat(rigB, 9711, Body.Fire, Vol2Faction.Enemy, 4, 0, 0.8f); var q = Mat(rigB, 9712, Body.Metal, Vol2Faction.Player, 4, 0.4f, 0.6f);     // the same moment
                rigB.Advance(0.05, 1);
                Check(r, "D1b", ev2.Count == 1 && ev2[0].ActingBody == Body.Metal && ev2[0].ActingCast.Value == 9712, "made at the same moment: the higher cast id acts");
                var ev3 = new List<Vol2ReactionVisual>();
                var rigC = NewRig(book, ev3);
                var older = Mat(rigC, 9721, Body.Fire, Vol2Faction.Enemy, 4, 0, 0.8f); rigC.Advance(0.03, 1); var newer = Mat(rigC, 9722, Body.Metal, Vol2Faction.Player, 4, 3f, 0.6f);
                rigC.Advance(0.05, 1);
                Check(r, "D1c", ev3.Count == 0, "apart: nothing");
                // the older fire is MOVED into the metal: a moving body acts first even though it is older
                rigC.Coord.Materials.TryMove(older.Id, Vol2Footprint.Disc(At(4, 3f), 0.8f), rigC.Services.Clock.NowUs);
                rigC.Advance(0.05, 1);
                Check(r, "D1d", ev3.Count == 1 && ev3[0].ActingBody == Body.Fire && ev3[0].Relation == R.Kind.Break && !older.Alive && !newer.Alive, "the older fire is carried into the metal: a MOVING body acts first - fire breaks metal (it would have been blocked had it stood)");
            }

            // ---------------------------------------------------------------- P1 split halves
            {
                var events = new List<Vol2ReactionVisual>();
                var rig = NewRig(book, events);
                var parent = Mat(rig, 9801, Body.Wood, Vol2Faction.Player, 4, 0, 0.8f);
                rig.Coord.Materials.TrySplit(parent.Id, 2, rig.Services.Clock.NowUs, out var halves);
                var half = halves[0];
                rig.Advance(0.02, 1);
                var f1 = Mat(rig, 9802, Body.Fire, Vol2Faction.Enemy, 4, 0.9f, 0.3f); var f2 = Mat(rig, 9803, Body.Fire, Vol2Faction.Enemy, 4, -0.9f, 0.3f);
                rig.Advance(0.05, 1);
                var ledger = rig.Coord.Reactions.Ledger;
                Check(r, "P1a", half.Origin == "split" && ledger.Count == 2 && ledger.Any(x => x.ContactId == half.Id.Value) && ledger.Any(x => x.ContactId == parent.Id.Value) && !half.Alive && !parent.Alive,
                    "the split half is read by the engine like its parent: one fire burns the parent, the other burns the half (both consumed)");
            }

            // ---------------------------------------------------------------- O1 ownership
            {
                var events = new List<Vol2ReactionVisual>();
                var rig = NewRig(book, events);
                var mine = Mat(rig, 9901, Body.Wood, Vol2Faction.Player, 4, 0, 0.8f); rig.Advance(0.02, 1);
                var theirs = Mat(rig, 9902, Body.Fire, Vol2Faction.Enemy, 4, 0.4f, 0.6f); rig.Advance(0.05, 1);
                var ledger = rig.Coord.Reactions.Ledger;
                var child = ledger.Count == 1 && rig.Coord.Materials.TryGet(new MaterialId(ledger[0].ChildId), out var c) ? c : null;
                Check(r, "O1a", child != null && child.FactionId == Vol2Faction.Enemy && child.OwnerId == 2 && child.Body == Body.Fire && events[0].FactionId == Vol2Faction.Enemy,
                    "the ENEMY's newer fire lands on MY wood: the child is fire and it is the ENEMY's (the acting owner takes it) - the ownership race is decided by who acts");
                var ev2 = new List<Vol2ReactionVisual>();
                var rig2 = NewRig(book, ev2);
                var theirsMetal = Mat(rig2, 9911, Body.Metal, Vol2Faction.Player, 4, 0, 0.8f); rig2.Advance(0.02, 1);
                var eFire = Mat(rig2, 9912, Body.Fire, Vol2Faction.Enemy, 4, 0.4f, 0.6f); var t = rig2.Add("T", 4, 0.2f);
                rig2.Advance(0.05, 1);
                Check(r, "O1b", ev2.Count == 1 && ev2[0].Relation == R.Kind.Break && Eq(ev2[0].DamageBudget, 0.9m * dmgCap * enemyCap) && ev2[0].AffectedBodies == 0 && rig2.Damage.Requests.Count == 0,
                    "an enemy-owned fire breaks my metal: its damage cap is halved again (0.9 x 0.8 x 0.5 = 0.36); an enemy-owned reaction does not harm enemy bodies, and the player is no Vol2 target (TE-S8-3): 0 requests");
            }

            // ---------------------------------------------------------------- E1 eligibility
            {
                var rig = NewRig(book);
                long now = rig.Services.Clock.NowUs;
                var attached = rig.Coord.Materials.Create(new CastId(9921), '가', Body.Wood, Vol2MaterialKind.Residue, 0.9m, 6000000, Vol2Footprint.Attached(Vector3.zero), 1, Vol2Faction.Player, true, now);
                var standing = Mat(rig, 9922, Body.Wood, Vol2Faction.Player, 6, 0);
                var enemyOne = Mat(rig, 9923, Body.Wood, Vol2Faction.Enemy, 8, 0);
                var choices = Vol2ReactionEligibility.EnemyCasterChoices(rig.Coord.Materials, Vol2Faction.Enemy, now);
                Check(r, "E1a", Vol2ReactionEligibility.NormalContact(attached, now) && !Vol2ReactionEligibility.EnemyCasterSelects(attached, now) && choices.Contains(standing) && !choices.Contains(attached) && !choices.Contains(enemyOne),
                    "a player's body-attached material is eligible for normal contact but an enemy caster cannot CHOOSE it; my standing patch can be chosen, the enemy's own cannot");
                var mine = Shape(rig, 9924, Body.Fire, Vol2Faction.Player);
                Check(r, "E1b", !rig.Coord.Reactions.SubmitHostTouch(mine, attached.Id), "my own shape touching my own host is no contact");
                var hostile = Shape(rig, 9925, Body.Fire, Vol2Faction.Enemy);
                var events = new List<Vol2ReactionVisual>(); rig.Coord.Reactions.ReactionResolved += events.Add;
                Check(r, "E1c", rig.Coord.Reactions.SubmitHostTouch(hostile, attached.Id), "a hostile shape touching the host is a normal contact");
                rig.Advance(0.05, 1);
                Check(r, "E1d", events.Count == 1 && events[0].Relation == R.Kind.Transfer && events[0].ActingIsShape && !attached.Alive && events[0].FactionId == Vol2Faction.Enemy, "an enemy fire shape reaches the host: it burns the attached wood (옮겨붙음), the child is the enemy's");
                // the attached material is not in the standing sweep (no world extent)
                var rig2 = NewRig(book);
                rig2.Coord.Materials.Create(new CastId(9931), '가', Body.Wood, Vol2MaterialKind.Residue, 0.9m, 6000000, Vol2Footprint.Attached(Vector3.zero), 1, Vol2Faction.Player, true, rig2.Services.Clock.NowUs);
                Mat(rig2, 9932, Body.Fire, Vol2Faction.Enemy, 0, 0);
                rig2.Advance(0.1, 2);
                Check(r, "E1e", rig2.Coord.Reactions.Committed == 0, "a standing fire on the caster's feet does not read the attached material by position");
            }

            // ---------------------------------------------------------------- B1 boundary, E0 off
            {
                var rig = NewRig(book);
                var bm = Mat(rig, 9941, Body.Water, Vol2Faction.Enemy, 4, 0, 0.8f, 0.9m, 1.0); rig.Advance(0.01, 1);
                var am = Mat(rig, 9942, Body.Wood, Vol2Faction.Player, 4, 0.5f);
                rig.Advance(0.98, 1);        // clock = 0.99 s, the patch ends at 1.00 s
                bool before = rig.Coord.Reactions.Committed == 1;
                var rig2 = NewRig(book);
                var b2 = Mat(rig2, 9951, Body.Water, Vol2Faction.Enemy, 4, 0, 0.8f, 0.9m, 1.0);
                rig2.Advance(0.5, 1);
                var a2 = Mat(rig2, 9952, Body.Wood, Vol2Faction.Player, 4, 0.5f);
                rig2.Advance(0.5, 1);        // the tick lands EXACTLY at the end of the water patch: [start, end) - it is gone, no reaction
                Check(r, "B1", before && rig2.Coord.Reactions.Committed == 0 && b2.End == Vol2MaterialEnd.Expired && a2.Alive, "a patch that ends AT the tick is not read ([start, end)): it expires, the other lives on");
                var rig3 = NewRig(book, null, enabled: false);
                var b3 = Mat(rig3, 9961, Body.Metal, Vol2Faction.Enemy, 4, 0, 1.0f); rig3.Advance(0.01, 1); var a3 = Mat(rig3, 9962, Body.Fire, Vol2Faction.Player, 4, 0.5f);
                rig3.Advance(1.0, 20);
                Check(r, "E0", rig3.Coord.Reactions.Committed == 0 && rig3.Coord.Reactions.PendingTickets == 0 && a3.Alive && b3.Alive, "engine off (the default for every earlier slice's fixtures): overlapping patches stay as they are");
            }

            // ---------------------------------------------------------------- M1 - M3 real casts
            {
                var events = new List<Vol2ReactionVisual>();
                var rig = NewRig(book, events);
                var metal = Mat(rig, 9971, Body.Metal, Vol2Faction.Enemy, 6, 0, 0.8f);
                var tgt = rig.Add("T", 6, 0.1f);
                var log = rig.Cast('나', out var dec);                    // a fire path, 한 번
                rig.Advance(0.6, 60);
                Check(r, "M1a", dec.Accepted && events.Count == 1 && events[0].ActingIsShape && events[0].ActingBody == Body.Fire && events[0].Relation == R.Kind.Break && events[0].ActingCast == log.Cast && !metal.Alive && rig.Coord.Reactions.Ledger[0].FromShape,
                    "a fire path flies over a standing metal patch: the MOVING shape acts (fire breaks metal) before it reaches the body behind");
                rig.Advance(2.4, 24);
                var spell = rig.Damage.Requests.Where(q => q.Origin == Vol2DamageOrigin.Spell).ToList();
                var react = rig.Damage.Requests.Where(q => q.Origin == Vol2DamageOrigin.Reaction).ToList();
                Check(r, "M1b", spell.Count == 1 && react.Count == 4 && react.All(q => q.Cast == log.Cast) && log.Contacts.Count == 1 && log.Contacts.All(x => x.Kind == "Instant"), "the cast's own hit is one Spell request; the reaction's four burn ticks are Reaction requests of the same cast and never become contacts of the cast");
                var log2 = rig.Cast('나', out _); rig.Advance(0.6, 60);
                Check(r, "M1c", rig.Coord.Reactions.Committed == 1, "a second fire path finds the patch gone: no second consumption");
            }
            {
                var events = new List<Vol2ReactionVisual>();
                var rig = NewRig(book, events);
                var wood = Mat(rig, 9981, Body.Wood, Vol2Faction.Enemy, 3, 0, 0.8f);
                var log = rig.Cast('노', out var dec);                    // a fire fan, 한 번
                rig.Advance(0.1, 2);
                var child = events.Count == 1 && events[0].ChildId > 0 && rig.Coord.Materials.TryGet(new MaterialId(events[0].ChildId), out var c) ? c : null;
                Check(r, "M2", dec.Accepted && events.Count == 1 && events[0].ActingIsShape && events[0].Relation == R.Kind.Transfer && events[0].Legacy == ReactionKind308.Wildfire && !wood.Alive && child != null && child.Body == Body.Fire && child.Cast == log.Cast,
                    "a fire fan lands on a wood patch: 옮겨붙음 at once (the fan's first step), the old Wildfire look, a fire child of the fan's cast");
            }
            {
                var events = new List<Vol2ReactionVisual>();
                var rig = NewRig(book, events);
                var water = Mat(rig, 9991, Body.Water, Vol2Faction.Enemy, 2, 0, 0.5f);
                var log = rig.Cast('누', out var dec);                    // a fire ring, 한 번
                rig.Advance(0.1, 2);
                Check(r, "M3", dec.Accepted && events.Count == 1 && events[0].Relation == R.Kind.Block && events[0].ActingIsShape && water.Alive && Eq(water.Remaining, 0.45m), "a fire ring lands on a water patch on its band: blocked (the ring is spent, the water keeps half)");
            }

            // ---------------------------------------------------------------- W1 world materials, gate burn
            {
                var events = new List<Vol2ReactionVisual>();
                var rig = NewRig(book, events);
                var gate = new FakeGate("vine_door", SpellFieldGateKind.Vine, At(4, 0), 1f);
                var adapter = new Vol2FieldGateAdapter(rig.Caster, rig.Services.Clock) { Gates = () => new Vol2FieldGateAdapter.IGate[] { gate }, InCombat = () => false, MakePlatforms = false };
                rig.Coord.Field = adapter;
                var world = new Vol2WorldMaterials(rig.Coord.Materials, rig.Services.Clock, rig.Coord.Reactions.Tuning);
                var vine = world.Place(new Vol2WorldMaterialSpec { Id = "vine_patch", Body = Body.Wood, Position = At(4, 0), Radius = 1.0f, GateId = "vine_door" });
                Check(r, "W1a", vine.FactionId == Vol2Faction.World && vine.OwnerId == 3 && vine.Cast.Value > 8000000 && vine.Cast.Value < 9000000 && vine.Origin == "world" && vine.GateDoor && vine.Tag == "vine_door" && vine.Alive && world.TryGet("vine_patch", out _),
                    "a world material: world faction and owner, a cast id of the world's own range, origin world, a gate door tagged with its gate's id");
                rig.Advance(0.02, 1);
                var fire = Mat(rig, 9801, Body.Fire, Vol2Faction.Player, 4, 0.5f, 0.6f); rig.Advance(0.05, 1);
                Check(r, "W1b", events.Count == 1 && events[0].Relation == R.Kind.Transfer && !vine.Alive && gate.IsOpen,
                    "my fire reaches the world's vine patch: 옮겨붙음 consumes it and the field port opens the gate it was the door of (" + (events.Count > 0 ? events[0].ContactReason : "no event") + ")");
                var ring = world.Place(new Vol2WorldMaterialSpec { Id = "ember_ring", Body = Body.Fire, Shape = Vol2WorldShape.Ring, Position = At(10, 0), RingInner = 1f, RingOuter = 2f });
                var fan = world.Place(new Vol2WorldMaterialSpec { Id = "water_fan", Body = Body.Water, Shape = Vol2WorldShape.Fan, Position = At(20, 0), Forward = Vector3.forward, FanDepth = 4f, FanHalfAngleDeg = 30f });
                Check(r, "W1c", ring.Footprint.Kind == Vol2FootprintKind.Ring && Mathf.Approximately(ring.Footprint.RingInner, 1f) && Mathf.Approximately(ring.Footprint.RingOuter, 2f) && fan.Footprint.Kind == Vol2FootprintKind.Fan && Mathf.Approximately(fan.Footprint.FanDepth, 4f) && world.Placed.Count == 3,
                    "ring and fan world materials (the footprint factories: Vol2Footprint.RingAt / FanAt)");
            }

            // ---------------------------------------------------------------- F1 field gates
            {
                var rig = NewRig(book);
                var g1 = new FakeGate("vine", SpellFieldGateKind.Vine, At(10, 0), 1f); var g2 = new FakeGate("rock", SpellFieldGateKind.Boulder, At(3, 0), 1f);
                var g3 = new FakeGate("foul", SpellFieldGateKind.Pollution, At(2.5f, 0), 1f); var g4 = new FakeGate("far foul", SpellFieldGateKind.Pollution, At(20, 0), 1f);
                bool combat = false;
                var ad = new Vol2FieldGateAdapter(rig.Caster, rig.Services.Clock) { Gates = () => new Vol2FieldGateAdapter.IGate[] { g1, g2, g3, g4 }, InCombat = () => combat, MakePlatforms = false };
                string cut = ad.OnGateCut(new CastId(1), Vector3.zero, 2f);
                Check(r, "F1a", g2.IsOpen && !g1.IsOpen && !g3.IsOpen && cut.Contains("rock") && cut.Contains("opened") && ad.Opened == 1, "숫: the ring (r 2 m) cut the boulder gate whose edge it reaches - only the Boulder kind: " + cut);
                string clean = ad.OnCleanse(new CastId(2), Vector3.zero, 2f, 4f);
                Check(r, "F1b", g3.IsOpen && !g4.IsOpen && !g1.IsOpen && clean.Contains("1 of 2"), "웅: the cleansing band (2 - 4 m) opens the Pollution gate it passes over, not the far one: " + clean);
                var vine = rig.Coord.Materials.Create(new CastId(8000001), '\0', Body.Wood, Vol2MaterialKind.Residue, 0.9m, 100000000, Vol2Footprint.Disc(At(10, 0), 1f), 3, Vol2Faction.World, false, rig.Services.Clock.NowUs, 0, 0, "", true);
                string burn = ad.OnVineBurn(new CastId(3), vine);
                Check(r, "F1c", g1.IsOpen && burn.Contains("burnt open"), "눈: a burning vine door with no tag opens the nearest Vine gate under it: " + burn);
                combat = true;
                var gx = new FakeGate("rock2", SpellFieldGateKind.Boulder, At(3, 0), 1f);
                var ad2 = new Vol2FieldGateAdapter(rig.Caster, rig.Services.Clock) { Gates = () => new Vol2FieldGateAdapter.IGate[] { gx }, InCombat = () => combat, MakePlatforms = false };
                string refused = ad2.OnGateCut(new CastId(4), Vector3.zero, 2f);
                string plat = ad2.OnPlatform(new CastId(5), Vector3.zero, 2f, 6000000);
                combat = false; string plat2 = ad2.OnPlatform(new CastId(6), Vector3.zero, 2f, 6000000);
                Check(r, "F1d", !gx.IsOpen && refused.Contains("in combat") && plat.Contains("in combat") && plat2.Contains("noted") && ad2.Refused == 1, "in combat nothing opens and no platform stands (the glyph does its combat work only); out of combat the platform is noted (no collider in a fixture)");
                combat = false;
                // the contract: the coordinator asks the port through FieldRequest; a cast of 숫 would have asked OnGateCut
                rig.Coord.Field = ad2;
                Check(r, "F1e", rig.Coord.Field == ad2, "the coordinator's field port is the adapter");
            }

            // ---------------------------------------------------------------- R1 ring slot
            {
                var rig = NewRig(book);
                long now = rig.Services.Clock.NowUs;
                Vol2Material Ring(long cast, long faction, string origin = "cast", Vol2MaterialKind kind = Vol2MaterialKind.Residue)
                    => rig.Coord.Materials.Create(new CastId(cast), '구', Body.Wood, kind, 0.9m, 10000000, Vol2Footprint.RingAt(Vector3.zero, 1.7f, 2.3f), OwnerOf(faction), faction, true, rig.Services.Clock.NowUs, 0, 0, "", false, null, origin);
                var first = Ring(9901, Vol2Faction.Player);
                var world = Ring(9902, Vol2Faction.World, "world"); var enemy = Ring(9903, Vol2Faction.Enemy);
                var sameCast = Ring(9901, Vol2Faction.Player);
                Check(r, "R1a", first.Alive && sameCast.Alive && world.Alive && enemy.Alive && rig.Coord.Reactions.RingSlot.Replaced == 0, "a second ring of the SAME cast, the world's and the enemy's rings do not take the slot");
                var second = Ring(9904, Vol2Faction.Player);
                Check(r, "R1b", first.End == Vol2MaterialEnd.Replaced && sameCast.End == Vol2MaterialEnd.Replaced && second.Alive && world.Alive && enemy.Alive && rig.Coord.Reactions.RingSlot.Replaced == 2,
                    "a ring of ANOTHER cast of mine takes the slot: the old ring (and its twin) end Replaced; the world's and the enemy's stay");
                var child = Ring(9905, Vol2Faction.Player, "reaction");
                Check(r, "R1c", second.Alive && child.Alive, "a reaction child lying on a ring footprint is not a ring cast");
                // 뭄 after 국: the hardened ground and a ring seed are rings too (TE-S8-10), but a ground of the SAME cast as the seed it came from does not replace its own seed
                var seedRing = rig.Coord.Materials.Create(new CastId(9906), '굼', Body.Wood, Vol2MaterialKind.Mark, 0m, 8000000, Vol2Footprint.RingAt(Vector3.zero, 1.7f, 2.3f), 1, Vol2Faction.Player, false, rig.Services.Clock.NowUs);
                var ground = rig.Coord.Materials.Create(new CastId(9906), '뭄', Body.Earth, Vol2MaterialKind.Residue, 0m, 8000000, Vol2Footprint.RingAt(Vector3.zero, 1.7f, 2.3f), 1, Vol2Faction.Player, false, rig.Services.Clock.NowUs, 0, 0, Vol2SeedExecutor.TagGround);
                Check(r, "R1e", second.End == Vol2MaterialEnd.Replaced && seedRing.Alive && ground.Alive && child.Alive, "a ring SEED of another cast takes the slot too (the earlier residue ring ends Replaced); the hardened ground of the same cast as the seed does not replace its own seed");
                var rigOff = NewRig(book, null, enabled: false);
                var a = rigOff.Coord.Materials.Create(new CastId(1), '구', Body.Wood, Vol2MaterialKind.Residue, 0.9m, 10000000, Vol2Footprint.RingAt(Vector3.zero, 1.7f, 2.3f), 1, Vol2Faction.Player, true, 0);
                var b = rigOff.Coord.Materials.Create(new CastId(2), '구', Body.Wood, Vol2MaterialKind.Residue, 0.9m, 10000000, Vol2Footprint.RingAt(Vector3.zero, 1.7f, 2.3f), 1, Vol2Faction.Player, true, 0);
                Check(r, "R1d", a.Alive && b.Alive, "engine off: the slot is off (earlier slices' rows keep their behaviour)");
                _ = now;
            }

            // ---------------------------------------------------------------- X1 a 가른다 cast splits, it does not burn
            {
                var events = new List<Vol2ReactionVisual>();
                var rig = NewRig(book, events);
                var wood = Mat(rig, 9990, Body.Wood, Vol2Faction.Enemy, 4, 0, 0.8f);
                var tgt = rig.Add("T", 4, 0.1f);
                var log = rig.Cast('낫', out var dec);                    // a fire path 가른다
                rig.Advance(0.5, 50);
                Check(r, "X1", dec.Accepted && events.Count == 0 && wood.Splits == 1 && rig.Coord.Reactions.Committed == 0, "a fire 가른다 over a wood patch: the patch is SPLIT (not burnt) - the shape of a split cast does not react with what it splits");
            }

            // ---------------------------------------------------------------- R2 ring slot with REAL casts
            {
                var events = new List<Vol2ReactionVisual>();
                var rig = NewRig(book, events);
                var log1 = rig.Cast('눅', out var d1);                    // a fire ring residue (남긴다)
                rig.Advance(0.45, 45);
                var log2 = rig.Cast('묵', out var d2);                    // an earth ring residue
                rig.Advance(0.3, 30);
                var first = log1 != null ? log1.Materials : new List<Vol2Material>(); var second = log2 != null ? log2.Materials : new List<Vol2Material>();
                Check(r, "R2a", d1.Accepted && d2.Accepted && first.Count == 1 && second.Count == 1 && first[0].End == Vol2MaterialEnd.Replaced && second[0].Alive && rig.Coord.Reactions.RingSlot.Replaced == 1,
                    "two real ring casts of mine: the second ring residue takes the slot, the first ends Replaced");
                Check(r, "R2b", events.Count == 0 && rig.Coord.Reactions.Ledger.Count == 0, "the two rings never reacted with each other (the old one was gone before the sweep): " + string.Join("; ", rig.Coord.Reactions.Ledger.Select(x => x.ToString() + " " + x.ActingIsShape)));
            }

            // ---------------------------------------------------------------- K1 dash block
            {
                var rig = NewRig(book);
                var onGround = Vol2DefenceFixtureSupport.NewEnemy(made, "K1_on"); var beside = Vol2DefenceFixtureSupport.NewEnemy(made, "K1_beside");
                onGround.transform.position = At(1f, 0f); beside.transform.position = At(6f, 0f);
                var t1 = new Vol2EnemyTarget(onGround, 1); var t2 = new Vol2EnemyTarget(beside, 2); t1.Refresh(); t2.Refresh();
                var ground = rig.Coord.Materials.Create(new CastId(9910), '뭄', Body.Earth, Vol2MaterialKind.Residue, 0m, 8000000, Vol2Footprint.RingAt(Vector3.zero, 1.7f, 2.3f), 1, Vol2Faction.Player, false, rig.Services.Clock.NowUs, 0, 0, Vol2SeedExecutor.TagGround);
                var block = new Vol2MovementBlock(rig.Coord.Materials, rig.Coord.Reactions.Tuning);
                var list = new IVol2Target[] { t1, t2 };
                block.Tick(rig.Services.Clock.NowUs, list, 100f);
                Check(r, "K1a", onGround.Modifiers.Domain.DashScale(100.1f) == 0f && beside.Modifiers.Domain.DashScale(100.1f) == 1f && block.Applied == 1, "an enemy standing on the hardened ground (inside the ring's outer edge) cannot dash or leap (dash x0); the one beside it can");
                Check(r, "K1b", onGround.Modifiers.Domain.DashScale(100.5f) == 1f, "the hold ends 0.25 s after the last tick: off the ground, the enemy dashes again");
                rig.Advance(9.0, 1);
                block.Tick(rig.Services.Clock.NowUs, list, 200f);
                Check(r, "K1c", !ground.Alive && onGround.Modifiers.Domain.DashScale(200.1f) == 1f, "after the 8 s the ground is soft again and nothing is held");
            }

            // ---------------------------------------------------------------- H1 stagger exemption order
            {
                var def = new Vol2DefenceRig(made, book, 1f);
                var rig = NewRig(book);
                var enemy = Vol2DefenceFixtureSupport.NewEnemy(made, "H1_e"); enemy.transform.position = At(1f, 0f);
                var t1 = new Vol2EnemyTarget(enemy, 1); rig.Coord.Registry.Synthetic = () => new IVol2Target[] { t1 }; t1.Refresh();
                int staggerPlays = 0, exemptPlays = 0;
                Vol2SeedHitGate gateRef = null;
                // a stagger listener that subscribed to Damaged BEFORE the gate (the real order: WorldMacroPlayerReaction303 binds in OnEnable)
                def.Player.Damaged += amount => { if (gateRef == null) return; gateRef.EnsureReported(def.Player); if (gateRef.TakeStaggerExemption()) exemptPlays++; else staggerPlays++; };
                var hub = Vol2SeedWiring.Bind(rig.Coord, def.Kit, def.Player);
                gateRef = hub.Hits; def.Kit.HitGate = hub.Hits;
                var log = rig.Cast('멈', out _); rig.Advance(0.2, 4);
                bool took = def.Player.TakeAttackDamage(6f, IncomingDamageKind.Melee, enemy);
                Check(r, "H1a", took && exemptPlays == 1 && staggerPlays == 0 && hub.Hits.Reported == 1 && hub.Hits.ExemptionsGranted == 1 && hub.Hits.ExemptionsTaken == 1,
                    "the stagger listener ran BEFORE the gate's own subscription: EnsureReported made the gate confirm the hit first, the 멈 seed granted the exemption, the listener took it; the hit was reported ONCE");
                took = def.Player.TakeAttackDamage(6f, IncomingDamageKind.Melee, enemy);
                Check(r, "H1b", took && exemptPlays == 1 && staggerPlays == 1 && hub.Hits.Reported == 2, "the next hit (no seed left): the stagger plays; a hit is still reported once");
            }
        }

        sealed class FakeGate : Vol2FieldGateAdapter.IGate
        {
            public string Id { get; } public SpellFieldGateKind Kind { get; } public Vector3 Position { get; } public float Radius { get; }
            public bool IsOpen { get; private set; }
            public FakeGate(string id, SpellFieldGateKind kind, Vector3 position, float radius) { Id = id; Kind = kind; Position = position; Radius = radius; }
            public bool Open() { if (IsOpen) return false; IsOpen = true; return true; }
        }
    }
}
