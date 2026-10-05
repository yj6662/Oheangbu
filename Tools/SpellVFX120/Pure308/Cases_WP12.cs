// SPEC-SPELL-120-308 L2 cases of WP-12 (path-opening field glyphs): field.burn, field.cut, field.purify.
// Rule arithmetic only: which gate a cast opens and when a cast counts as a combat cast. Finding gates in a scene, opening
// them and the ink transaction are covered by the edit-mode fixtures (Spell120Checks308.WP12.cs).
using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Pure308
{
    public static partial class Program
    {
        static partial void RunWP12(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W12", false, "WP-12 cases need the table"); return; }
            // handler id -> the gate kind it opens (SpellFieldGateKind: 0 vine, 1 boulder, 2 pollution) and its grid cell
            var handlers = new[] { "field.burn", "field.cut", "field.purify" };
            int[] cells = { 45, 95, 120 };
            var finals = new[] { SpellFinal.Nieun, SpellFinal.Siot, SpellFinal.Ieung };
            var elements = new[] { Element.Fire, Element.Metal, Element.Water };
            var rows = handlers.Select(h => c.Build.Rows.SingleOrDefault(x => x.Handler == h)).ToArray();
            if (!r.Check("W12", rows.All(x => x != null), "the three WP-12 rows are in the table (Rules308_WP12 replaces the reserved rows)")) return;
            r.Check("W12", c.Build.Rows.Count(x => x.Pending == "WP-12") == 0, "no row is still reserved for WP-12");

            Vector3 player = new Vector3(10f, 2f, -4f), forward = Vector3.forward;
            for (int k = 0; k < 3; k++)
            {
                var row = rows[k]; string open = row.Letter + "-1", peace = row.Letter + "-2";
                float reach = row.F("gate.reach", 0f), angle = row.F("gate.angle", 0f), delay = row.F("gate.delay", -1f), combat = row.F("combat.radius", -1f);
                r.Check(open, row.Index == cells[k] && row.Category == SpellCategory.Field && row.Kind == SpellKind.Field && row.Final == finals[k] && row.Element == elements[k] &&
                    row.Feature == SpellLegacyFeature.None && row.Gate == SpellGateMode.Final && row.BasePower == 0f && string.IsNullOrEmpty(row.Pending),
                    "a field row on the handler route, behind its own final consonant, without power", row.Index + " " + row.Kind + " " + row.Final);
                r.Check(open, reach > 0f && angle > 0f && delay >= 0f && combat >= 0f, "its numbers are row data", "reach " + reach + " angle " + angle + " delay " + delay + " combat " + combat);
                var gate = new ModelGate { Registered = { row.Handler } };
                bool locked = SpellResolveCore308.Resolve(row.Letter[0], 1f, 1f, row, gate, out _) == SpellResolveStatus.Locked;
                gate.Unlocked.Add(row.Final);
                bool casts = SpellResolveCore308.Resolve(row.Letter[0], 1f, 1f, row, gate, out SpellCast cast) == SpellResolveStatus.Ok && cast.Kind == SpellKind.Field && cast.Power == 0f;
                r.Check(open, locked && casts, "locked until its final consonant is owned, then it resolves as a field cast");

                // gates around the caster: 0 = this handler's kind ahead, 1 = the same kind farther ahead, 2 / 3 = the two other kinds
                // nearer than both, 4 = the same kind behind, 5 = the same kind out of reach
                int kind = k, otherA = (k + 1) % 3, otherB = (k + 2) % 3;
                var gates = new List<FieldGateSnap>
                {
                    new FieldGateSnap(0, kind, player + forward * (reach * .6f), 1f, false, false),
                    new FieldGateSnap(1, kind, player + forward * (reach * .9f), 1f, false, false),
                    new FieldGateSnap(2, otherA, player + forward * 1.5f, 1f, false, false),
                    new FieldGateSnap(3, otherB, player + forward * 2f, 1f, false, false),
                    new FieldGateSnap(4, kind, player - forward * 2f, .5f, false, false),
                    new FieldGateSnap(5, kind, player + forward * (reach + 1f + .5f), 1f, false, false),
                };
                int pick = FieldGateRule308.Pick(gates, kind, player, forward, reach, angle);
                r.Check(open, pick == 0, "the cast opens the nearest closed gate of its own kind that the caster faces and reaches; gates of the other two kinds are not its to open", "picked " + pick);
                gates[0] = new FieldGateSnap(0, kind, gates[0].Position, 1f, true, false);
                r.Check(open, FieldGateRule308.Pick(gates, kind, player, forward, reach, angle) == 1, "a gate that is already open is passed over: the next one opens");
                gates[1] = new FieldGateSnap(1, kind, gates[1].Position, 1f, false, true);
                r.Check(open, FieldGateRule308.Pick(gates, kind, player, forward, reach, angle) == FieldGateRule308.None,
                    "a gate another cast is already opening is not picked again; the one behind the caster and the one out of reach never are");
                var only = new List<FieldGateSnap> { new FieldGateSnap(0, kind, player + forward * (reach + 1f - .05f), 1f, false, false) };
                var beyond = new List<FieldGateSnap> { new FieldGateSnap(0, kind, player + forward * (reach + 1f + .05f), 1f, false, false) };
                r.Check(open, FieldGateRule308.Pick(only, kind, player, forward, reach, angle) == 0 && FieldGateRule308.Pick(beyond, kind, player, forward, reach, angle) == FieldGateRule308.None,
                    "reach is measured to the gate's edge (pivot distance minus its radius)");
                float inAngle = (angle - 2f) * Mathf.Deg2Rad, outAngle = (angle + 2f) * Mathf.Deg2Rad;
                var atEdge = new List<FieldGateSnap> { new FieldGateSnap(0, kind, player + new Vector3(Mathf.Sin(inAngle), 0f, Mathf.Cos(inAngle)) * 3f, .2f, false, false) };
                var pastEdge = new List<FieldGateSnap> { new FieldGateSnap(0, kind, player + new Vector3(Mathf.Sin(outAngle), 0f, Mathf.Cos(outAngle)) * 3f, .2f, false, false) };
                r.Check(open, FieldGateRule308.Pick(atEdge, kind, player, forward, reach, angle) == 0 && (angle + 2f > 180f || FieldGateRule308.Pick(pastEdge, kind, player, forward, reach, angle) == FieldGateRule308.None),
                    "the caster must face the gate within gate.angle of the flat forward");
                var around = new List<FieldGateSnap> { new FieldGateSnap(0, kind, player - forward * .5f, 2f, false, false) };
                r.Check(open, FieldGateRule308.Pick(around, kind, player, forward, reach, angle) == 0, "standing inside the thing itself (a polluted patch) needs no facing");
                var twins = new List<FieldGateSnap> { new FieldGateSnap(7, kind, player + forward * 3f, 1f, false, false), new FieldGateSnap(4, kind, player + forward * 3f, 1f, false, false) };
                r.Check(open, FieldGateRule308.Pick(twins, kind, player, forward, reach, angle) == 1, "a tie goes to the lower id (the same answer every time)");
                var above = new List<FieldGateSnap> { new FieldGateSnap(0, kind, player + forward * 2f + Vector3.up * (reach + 5f), 1f, false, false) };
                r.Check(open, FieldGateRule308.Pick(above, kind, player, forward, reach, angle) == FieldGateRule308.None && FieldGateRule308.Pick(null, kind, player, forward, reach, angle) == FieldGateRule308.None &&
                    FieldGateRule308.Pick(new List<FieldGateSnap>(), kind, player, forward, reach, angle) == FieldGateRule308.None,
                    "a gate far above or below is out of reach; nothing to open = no pick (the cast is refused before any ink is spent)");

                // ---- non-combat only ----
                var calm = new[]
                {
                    new SpellActorSnap(0, player + forward * (combat + 5f), true, 1, 1f, false),      // alive, far, not hunting
                    new SpellActorSnap(1, player + forward * 1f, false, 1, 0f, false),               // dead, right here
                };
                r.Check(peace, !FieldGateRule308.InCombat(calm, new[] { false, false }, player, combat) && !FieldGateRule308.InCombat(null, null, player, combat) &&
                    !FieldGateRule308.InCombat(new SpellActorSnap[0], new bool[0], player, combat),
                    "no living enemy near and nobody hunting: the cast is allowed");
                r.Check(peace, FieldGateRule308.InCombat(calm, new[] { true, false }, player, combat),
                    "an enemy that hunts the player makes it a combat cast however far away it is (the rule the wood lift and the earth bridge use): refused");
                r.Check(peace, !FieldGateRule308.InCombat(calm, new[] { false, true }, player, combat), "a dead enemy never counts");
                var near = new[] { new SpellActorSnap(0, player + forward * (combat - .1f), true, 1, 1f, false) };
                var justOut = new[] { new SpellActorSnap(0, player + forward * (combat + .1f), true, 1, 1f, false) };
                r.Check(peace, combat > 0f && FieldGateRule308.InCombat(near, new[] { false }, player, combat) && !FieldGateRule308.InCombat(justOut, new[] { false }, player, combat),
                    "a living enemy within combat.radius of the caster makes it a combat cast too; just outside does not");
                r.Check(peace, !FieldGateRule308.InCombat(near, new[] { false }, player, 0f) && FieldGateRule308.InCombat(near, new[] { true }, player, 0f) &&
                    !FieldGateRule308.InCombat(near, null, player, 0f), "combat.radius 0 leaves the hunt as the only test");
            }
        }
    }
}
