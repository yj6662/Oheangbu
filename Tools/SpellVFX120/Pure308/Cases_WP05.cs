// SPEC-SPELL-120-308 L2 cases of WP-05 (enemy control): circle.interrupt, single.freeze, path.undertow.
// Rule code only (InterruptRule308, FreezeRule308, UndertowRule308, ControlHitWatch308). The Unity half of the handlers
// (EnemyControlState, EnemyController.StopAttack, the airborne TEST target) is the edit-mode fixture's job (Spell120Checks308.WP05).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Pure308
{
    public static partial class Program
    {
        // Eight slots: three ordinary enemies ahead, three out of every shape (behind, far, far to the side), a boss ahead, a dead one ahead.
        static SpellActorSnap[] WP05Actors()
        {
            return new[]
            {
                new SpellActorSnap(0, new Vector3(0f, 0f, 3f), true, 1, 1f, false),
                new SpellActorSnap(1, new Vector3(.8f, 0f, 4.5f), true, 1, 1f, false),
                new SpellActorSnap(2, new Vector3(-.8f, 0f, 6f), true, 4, .5f, false),
                new SpellActorSnap(3, new Vector3(0f, 0f, -5f), true, 1, 1f, false),
                new SpellActorSnap(4, new Vector3(0f, 0f, 60f), true, 1, 1f, false),
                new SpellActorSnap(5, new Vector3(40f, 0f, 2f), true, 1, 1f, false),
                new SpellActorSnap(6, new Vector3(1.5f, 0f, 5f), true, 2, 1f, true),
                new SpellActorSnap(7, new Vector3(.2f, 0f, 4f), false, 3, 0f, false),
            };
        }

        // The giyeok precedent the control glyphs are measured against: root duration and boss root speed of EAGiyeokProfileSO
        // (class defaults in the source plus every EAGiyeok*.asset of the project). Read as text; nothing is written.
        static string WP05GiyeokPrecedent(Context c, List<float> rootDurations, List<float> bossSpeeds)
        {
            float Number(string text, string key, char separator)
            {
                int at = text.IndexOf(key, StringComparison.Ordinal);
                if (at < 0) return float.NaN;
                at += key.Length;
                while (at < text.Length && (text[at] == ' ' || text[at] == separator)) at++;
                int end = at;
                while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.')) end++;
                return float.TryParse(text.Substring(at, end - at), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : float.NaN;
            }
            void Take(string text, char separator)
            {
                float root = Number(text, "RootDuration", separator), boss = Number(text, "BossRootSpeed", separator);
                if (float.IsNaN(root) || float.IsNaN(boss)) return;
                rootDurations.Add(root); bossSpeeds.Add(boss);
            }
            string project = Path.Combine(c.Root, "Oheangbu", "Assets", "_Project");
            string source = Path.Combine(project, "Scripts", "Combat", "EAGiyeokProfileSO.cs");
            if (File.Exists(source)) Take(File.ReadAllText(source), '=');
            try
            {
                foreach (string asset in Directory.EnumerateFiles(project, "EAGiyeok*.asset", SearchOption.AllDirectories)) Take(File.ReadAllText(asset), ':');
            }
            catch (Exception exception) { return "asset scan failed: " + exception.Message; }
            return rootDurations.Count + " profile sources";
        }

        static partial void RunWP05(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W05", false, "WP-05 cases need the table"); return; }
            var interrupt = c.Build.Rows.SingleOrDefault(x => x.Handler == "circle.interrupt");
            var freeze = c.Build.Rows.SingleOrDefault(x => x.Handler == "single.freeze");
            var undertow = c.Build.Rows.SingleOrDefault(x => x.Handler == "path.undertow");
            if (!r.Check("W05", interrupt != null && freeze != null && undertow != null, "the three WP-05 handlers each own exactly one row")) return;

            // ---- rows: grid cell, inherited numbers, gate ----
            var circleBase = c.Row(SpellGrammar308.BaseOf(interrupt.Letter[0]));
            var singleBase = c.Row(SpellGrammar308.BaseOf(freeze.Letter[0]));
            var pathBase = c.Row(SpellGrammar308.BaseOf(undertow.Letter[0]));
            r.Check("W05", interrupt.Index == 15 && interrupt.Element == Element.Wood && interrupt.Final == SpellFinal.Nieun && interrupt.Kind == SpellKind.AttackArea &&
                interrupt.AreaShape == AreaShape.Circle && interrupt.BasePower == circleBase.BasePower && interrupt.AreaRadius == circleBase.AreaRadius &&
                interrupt.AreaImpactDelay == circleBase.AreaImpactDelay && interrupt.AreaRadius > 0f && !interrupt.Has(SpellGrammar308.SpikesKey),
                "interrupt row: wood circle with the nieun final, base circle numbers inherited, one impact time (no staggered spikes)",
                interrupt.Index + " " + interrupt.AreaShape + " r" + interrupt.AreaRadius + " p" + interrupt.BasePower);
            r.Check("W05", freeze.Index == 101 && freeze.Element == Element.Water && freeze.Final == SpellFinal.Siot && freeze.Kind == SpellKind.AttackSingle &&
                freeze.AreaShape == AreaShape.None && freeze.BasePower == singleBase.BasePower && freeze.ProjectileSpeedMul == singleBase.ProjectileSpeedMul && freeze.BasePower > 0f,
                "freeze row: water single with the siot final, base power and projectile speed inherited", freeze.Index + " p" + freeze.BasePower + " s" + freeze.ProjectileSpeedMul);
            r.Check("W05", undertow.Index == 114 && undertow.Element == Element.Water && undertow.Final == SpellFinal.Ieung && undertow.Kind == SpellKind.AttackArea &&
                undertow.AreaShape == AreaShape.Path && undertow.BasePower == pathBase.BasePower && undertow.AreaRadius == pathBase.AreaRadius &&
                undertow.AreaLength == pathBase.AreaLength && undertow.AreaSpeed == pathBase.AreaSpeed && undertow.AreaImpactDelay == pathBase.AreaImpactDelay && undertow.AreaSpeed > 0f,
                "undertow row: water path with the ieung final, base corridor numbers inherited",
                undertow.Index + " w" + undertow.AreaRadius + " l" + undertow.AreaLength + " v" + undertow.AreaSpeed);
            foreach (var row in new[] { interrupt, freeze, undertow })
            {
                var gate = new ModelGate();
                var unregistered = SpellResolveCore308.Resolve(row.Letter[0], .37f, .2f, row, gate, out _);
                gate.Registered.Add(row.Handler);
                var locked = SpellResolveCore308.Resolve(row.Letter[0], .37f, .2f, row, gate, out _);
                gate.Unlocked.Add(row.Final);
                var open = SpellResolveCore308.Resolve(row.Letter[0], .37f, .2f, row, gate, out SpellCast cast);
                r.Check("W05", unregistered == SpellResolveStatus.Unavailable && locked == SpellResolveStatus.Locked && open == SpellResolveStatus.Ok &&
                    r.Near(cast.Power, row.BasePower * .37f) && cast.Kind == row.Kind && cast.Element == row.Element && cast.Brush01 == .2f,
                    row.Handler + ": Unavailable without its handler, Locked behind its final, Ok when unlocked with base power x brush",
                    unregistered + " " + locked + " " + open + " power " + cast.Power);
                r.Check("W05", row.Gate == SpellGateMode.Final && row.Feature == SpellLegacyFeature.None && string.IsNullOrEmpty(row.Pending),
                    row.Handler + ": a new row behind its final (no legacy feature, not reserved, not an open gate)");
            }

            // ---- the hit watch the three handlers share ----
            var watch = new ControlHitWatch308<int>();
            watch.Add(11, 5f, 100); watch.Add(12, 5f, 200); watch.Add(0, 5f, 1); watch.Add(13, float.NaN, 1);
            r.Check("W05", watch.Count == 2, "hit watch: an entry needs a real attack id and a time");
            r.Check("W05", watch.Sweep(4.9f) == 0 && watch.Count == 2, "hit watch: nothing is dropped before the impact time");
            r.Check("W05", watch.Sweep(5f) == 0 && watch.Count == 2, "hit watch: at the impact time the entries stay for that frame (the hit lands after the handlers tick)");
            r.Check("W05", watch.TryTake(11, out int taken) && taken == 100 && watch.Count == 1 && !watch.TryTake(11, out _), "hit watch: a confirmed hit takes its entry exactly once");
            r.Check("W05", !watch.TryTake(99, out _), "hit watch: another attack's hit takes nothing");
            r.Check("W05", watch.Sweep(5.02f) == 1 && watch.Count == 0, "hit watch: a hit that never landed is dropped on the next frame (no control without a confirmed hit)");

            var actors = WP05Actors();
            var rootDurations = new List<float>(); var bossRootSpeeds = new List<float>();
            string precedent = WP05GiyeokPrecedent(c, rootDurations, bossRootSpeeds);
            float rootDuration = rootDurations.Count > 0 ? rootDurations.Min() : float.PositiveInfinity;
            r.Fact("wp05.giyeokPrecedent", precedent + ", root " + rootDuration.ToString(CultureInfo.InvariantCulture) + ", boss speed " +
                string.Join("/", bossRootSpeeds.Select(v => v.ToString(CultureInfo.InvariantCulture))));

            // ================= interrupt =================
            var orders = new List<InterruptRule308.Order>();
            Vector3 centre = actors[1].Position; float impactAt = 10f + interrupt.AreaImpactDelay;
            InterruptRule308.Plan(actors, centre, interrupt.AreaRadius, impactAt, orders);
            int[] inside = actors.Where(a => a.Alive && AreaGeometry.InCircle(centre, a.Position, interrupt.AreaRadius, out _)).Select(a => a.Id).ToArray();
            r.Check("곤-1", orders.Select(o => o.TargetId).SequenceEqual(inside) && inside.SequenceEqual(new[] { 0, 1, 2, 6 }),
                "every living enemy inside the circle is interrupted: the three ordinary ones and the boss; the dead one and the three outside are not",
                string.Join(",", orders.Select(o => o.TargetId)));
            r.Check("곤-1", orders.All(o => o.At == impactAt && o.Life == actors[o.TargetId].Life), "one impact time for the whole circle; each order is bound to the life it was planned for");
            var none = new List<InterruptRule308.Order>();
            InterruptRule308.Plan(actors, new Vector3(-30f, 0f, -30f), interrupt.AreaRadius, impactAt, none);
            InterruptRule308.Plan(actors, centre, 0f, impactAt, none);
            InterruptRule308.Plan(null, centre, interrupt.AreaRadius, impactAt, none);
            r.Check("곤-1", none.Count == 0, "an empty circle, a zero radius and no targets interrupt nobody");

            var ispec = new InterruptRule308.Spec(interrupt.F("interrupt.duration", 0f), interrupt.F("boss.speed", 1f));
            bool applied = InterruptRule308.Control(false, false, 20f, ispec, out float until, out float speed, out bool blocks);
            r.Check("곤-2", applied && blocks && speed == 0f && ispec.Duration > 0f && r.Near(until, 20f + ispec.Duration),
                "ordinary enemy: no action and no movement for the row's duration, then free again", "until " + until + " duration " + ispec.Duration);
            applied = InterruptRule308.Control(true, false, 20f, ispec, out until, out speed, out blocks);
            r.Check("곤-2", applied && !blocks && speed == ispec.BossSpeed && speed > 0f && speed < 1f && r.Near(until, 20f + ispec.Duration),
                "boss: slowed to boss.speed for the same time, actions not blocked (giyeok precedent)", "speed " + speed);
            r.Check("곤-2", !InterruptRule308.Control(false, true, 20f, ispec, out _, out _, out _), "a hit that killed applies no control");
            r.Check("곤-2", !InterruptRule308.Control(false, false, 20f, new InterruptRule308.Spec(0f, .7f), out _, out _, out _), "a row without a duration applies no control");

            float scale = interrupt.F("power.scale", 1f);
            var root = c.Row(SpellGrammar308.LetterAt(2));       // the wood giyeok attack: the full root this glyph is the mitigated form of
            r.Check("곤-4", r.Near(InterruptRule308.Power(interrupt.BasePower * .37f, scale), interrupt.BasePower * .37f * scale) &&
                InterruptRule308.Power(0f, scale) == 0f && InterruptRule308.Power(5f, 0f) == 0f, "strike power = cast power x power.scale");
            r.Check("곤-4", scale > 0f && scale < 1f && interrupt.BasePower * scale < circleBase.BasePower && root != null && interrupt.BasePower * scale < root.BasePower,
                "mitigated power: the factor is below 1, so the strike is weaker than its base circle and than the giyeok root strike",
                (interrupt.BasePower * scale) + " < " + circleBase.BasePower + " and < " + (root != null ? root.BasePower : float.NaN));
            r.Check("곤-4", !float.IsInfinity(rootDuration) && ispec.Duration < rootDuration, "mitigated control: shorter than the giyeok root of every profile",
                ispec.Duration + " < " + rootDuration + " (" + precedent + ")");

            // ================= freeze =================
            float castPower = freeze.BasePower * .37f, shatter = freeze.F("shatter.power", 0f);
            var fspec = new FreezeRule308.Spec(freeze.F("freeze.duration", 0f), freeze.F("boss.speed", 1f), FreezeRule308.ShatterPower(castPower, shatter), freeze.Element, freeze.Letter[0]);
            var state = new FreezeRule308.State();
            bool frozen = state.Freeze(0, 1, 501, false, false, 30f, fspec, out until, out speed, out blocks);
            r.Check("앗-1", frozen && blocks && speed == 0f && fspec.Duration > 0f && r.Near(until, 30f + fspec.Duration) && state.Count == 1 &&
                state.IsFrozen(0, 1, 30f) && state.IsFrozen(0, 1, until - .01f) && !state.IsFrozen(0, 1, until) && !state.IsFrozen(1, 1, 30f),
                "the confirmed hit freezes its one target: no action and no movement for the row's duration", "until " + until);
            r.Check("앗-1", !state.Freeze(1, 1, 510, false, true, 30f, fspec, out _, out _, out _) && state.Count == 1, "a hit that killed freezes nothing");
            r.Check("앗-1", !new FreezeRule308.State().Freeze(1, 1, 511, false, false, 30f, new FreezeRule308.Spec(0f, .5f, 1f, Element.Water, freeze.Letter[0]), out _, out _, out _),
                "a row without a duration freezes nothing");

            r.Check("앗-2", !state.TryShatter(0, 1, 501, false, 30f, out _) && state.Count == 1, "the freezing hit itself does not break its own ice");
            r.Check("앗-2", !state.TryShatter(1, 1, 777, false, 31f, out _) && state.Count == 1, "a hit on another enemy leaves the ice alone");
            bool broke = state.TryShatter(0, 1, 777, false, 31f, out var broken);
            r.Check("앗-2", broke && state.Count == 0 && !state.IsFrozen(0, 1, 31f), "the next confirmed hit on the frozen enemy breaks the ice: the freeze is over before its time");
            r.Check("앗-3", broke && broken.Owner == 501 && broken.Life == 1 && broken.Bonus > 0f && r.Near(broken.Bonus, castPower * shatter) && broken.Element == Element.Water,
                "the break carries the extra damage (cast power x shatter.power) under the freezing hit's attack id, water", "bonus " + broken.Bonus);
            r.Check("앗-3", !state.TryShatter(0, 1, 501, false, 31f, out _) && !state.TryShatter(0, 1, 778, false, 31.1f, out _),
                "once: the extra damage coming back through the hook, and any later hit, find no ice (no second bonus, control already released)");
            r.Check("앗-3", FreezeRule308.ShatterPower(0f, shatter) == 0f && FreezeRule308.ShatterPower(castPower, 0f) == 0f, "no cast power or no factor = no extra damage");

            state.Freeze(0, 1, 502, false, false, 40f, fspec, out until, out _, out _);
            r.Check("앗-2", !state.TryShatter(0, 1, 779, false, until, out _) && state.Count == 0, "a hit after the freeze ran out breaks nothing (no bonus)");
            state.Freeze(0, 1, 503, false, false, 50f, fspec, out until, out _, out _);
            r.Check("앗-2", state.Expire(until - .01f) == 0 && state.Count == 1 && state.Expire(until) == 1 && state.Count == 0, "an unbroken freeze ends by itself at its time");
            state.Freeze(0, 1, 504, false, false, 60f, fspec, out _, out _, out _);
            r.Check("앗-2", !state.TryShatter(0, 2, 780, true, 61f, out _) && state.Count == 0, "a hit that kills the frozen enemy drops the freeze without the bonus");
            state.Freeze(0, 1, 505, false, false, 62f, fspec, out _, out _, out _);
            r.Check("앗-2", !state.TryShatter(0, 2, 781, false, 63f, out _) && state.Count == 0, "the slot holds another life: the old freeze is dropped, not broken");

            // a second freezing hit on a frozen enemy: it breaks the first ice (bonus), then freezes again
            state.Freeze(0, 1, 520, false, false, 70f, fspec, out _, out _, out _);
            bool rebroke = state.TryShatter(0, 1, 521, false, 71f, out broken);
            bool refrozen = state.Freeze(0, 1, 521, false, false, 71f, fspec, out until, out _, out _);
            r.Check("앗-3", rebroke && broken.Owner == 520 && refrozen && state.Count == 1 && state[0].Owner == 521 && r.Near(until, 71f + fspec.Duration),
                "casting it again on a frozen enemy breaks the first ice (bonus once) and freezes anew: never two freezes on one enemy");
            state.Clear();

            bool bossFrozen = state.Freeze(6, 2, 530, true, false, 80f, fspec, out until, out speed, out blocks);
            r.Check("앗-4", bossFrozen && !blocks && speed == fspec.BossSpeed && speed > 0f && r.Near(until, 80f + fspec.Duration),
                "boss: slowed to boss.speed for the freeze duration, actions not blocked", "speed " + speed);
            r.Check("앗-4", bossRootSpeeds.Count > 0 && bossRootSpeeds.All(v => r.Near(v, fspec.BossSpeed)),
                "boss.speed equals the giyeok boss root speed of every profile (the precedent the glyph text names)",
                fspec.BossSpeed + " vs " + string.Join("/", bossRootSpeeds.Select(v => v.ToString(CultureInfo.InvariantCulture))) + " (" + precedent + ")");
            r.Check("앗-4", state.TryShatter(6, 2, 782, false, 81f, out broken) && broken.Bonus > 0f && state.Count == 0, "the boss's ice breaks the same way (bonus once, slow released)");
            state.Freeze(0, 1, 540, false, false, 90f, fspec, out _, out _, out _); state.Clear();
            r.Check("W05", state.Count == 0, "Clear drops every freeze (player death, rest, scene leave)");

            // ================= undertow =================
            var sweeps = new List<UndertowRule308.Order>();
            Vector3 start = Vector3.zero, direction = new Vector3(0f, 0f, 1f); float castAt = 10f;
            UndertowRule308.Plan(actors, start, direction, undertow.AreaRadius, undertow.AreaLength, undertow.AreaSpeed, castAt, undertow.AreaImpactDelay, sweeps);
            int[] corridor = actors.Where(a => a.Alive && AreaGeometry.InCorridor(start, direction, a.Position, undertow.AreaRadius, undertow.AreaLength, out _, out _)).Select(a => a.Id).ToArray();
            r.Check("옹-1", sweeps.Select(o => o.TargetId).SequenceEqual(corridor) && corridor.SequenceEqual(new[] { 0, 1, 2, 6 }),
                "the front sweeps every living enemy inside the corridor (boss included); behind, beyond the length, outside the width and dead are not swept",
                string.Join(",", sweeps.Select(o => o.TargetId)));
            r.Check("옹-1", sweeps.All(o => r.Near(o.At, castAt + undertow.AreaImpactDelay + actors[o.TargetId].Position.z / undertow.AreaSpeed) && o.Life == actors[o.TargetId].Life) &&
                sweeps[0].At < sweeps[1].At && sweeps[1].At < sweeps[2].At, "each enemy is swept when the front reaches it: cast + rise delay + distance along the corridor / front speed");
            var noSweep = new List<UndertowRule308.Order>();
            UndertowRule308.Plan(actors, start, direction, undertow.AreaRadius, undertow.AreaLength, 0f, castAt, undertow.AreaImpactDelay, noSweep);
            UndertowRule308.Plan(actors, start, new Vector3(0f, 0f, -1f), undertow.AreaRadius, 2f, undertow.AreaSpeed, castAt, undertow.AreaImpactDelay, noSweep);
            r.Check("옹-1", noSweep.Count == 0, "a front without speed, or a corridor with nobody inside, sweeps nobody");

            var uspec = new UndertowRule308.Spec(undertow.F("slow.duration", 0f), undertow.F("slow.speed", 1f));
            bool swept = UndertowRule308.Sweep(false, false, 12f, uspec, out until, out speed, out bool pull);
            r.Check("옹-1", swept && speed == uspec.SlowSpeed && speed < 1f && speed >= 0f && uspec.Duration > 0f && r.Near(until, 12f + uspec.Duration) && !pull,
                "a swept enemy moves at slow.speed (< 1) until now + slow.duration; the control ends there, so its own speed returns", "speed " + speed + " until " + until);
            r.Check("옹-1", !UndertowRule308.Sweep(false, true, 12f, uspec, out _, out _, out _) && !UndertowRule308.Sweep(false, false, 12f, new UndertowRule308.Spec(0f, .5f), out _, out _, out _),
                "a hit that killed, or a row without a duration, slows nothing");
            UndertowRule308.Sweep(false, false, 12f, new UndertowRule308.Spec(3f, 1.7f), out _, out float clamped, out _);
            r.Check("옹-1", clamped == 1f, "the slow is a movement scale inside 0..1 (it can never speed an enemy up)");

            // 옹-2 is held in the clause sheet (no airborne enemy existed). USER_ANSWERS Q5: the rule is built against a TEST target.
            swept = UndertowRule308.Sweep(true, false, 12f, uspec, out float pulledUntil, out float pulledSpeed, out pull);
            r.Check("옹-2", swept && pull && r.Near(pulledUntil, 12f + uspec.Duration) && pulledSpeed == uspec.SlowSpeed,
                "an enemy that is in the air when the front sweeps it is pulled to the ground for the same time, and slowed like everyone else");
            UndertowRule308.Sweep(false, false, 12f, uspec, out _, out _, out pull);
            r.Check("옹-2", !pull, "an enemy on the ground is only slowed (nothing to pull down)");
            r.Check("옹-2", !UndertowRule308.Sweep(true, true, 12f, uspec, out _, out _, out _), "an airborne enemy the hit killed is not pulled");
        }
    }
}
