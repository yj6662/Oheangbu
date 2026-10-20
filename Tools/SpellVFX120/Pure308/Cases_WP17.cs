using System;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Pure308
{
    // SPEC-SPELL-120-308 WP-17 (D308-121, debate decision — user not yet asked, TEST): the twenty 결계 (domain) rows and
    // the arithmetic their handlers share. The handlers themselves (zones, NavMeshAgent moves, shots, wiring) are
    // engine-side; here the rule sheet, the enemy's and the player's domain state and DomainRule308.
    public static partial class Program
    {
        static readonly string Domain17 = "군굼굿궁눅눔눗눙묵문뭇뭉숙순숨숭욱운움웃";
        static readonly string DomainTest17 = "묵숨운";      // no world target yet (dashing enemy, a force on the player, a status)

        static partial void RunWP17(Report r, Context c)
        {
            RunWP17Rows(r, c);
            RunWP17Enemy(r);
            RunWP17Rule(r);
            RunWP17Player(r);
        }

        static void RunWP17Rows(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("결계-표", false, "the table did not build: the domain rows are not checked"); return; }
            int handled = 0, tests = 0; string wrong = "";
            foreach (char letter in Domain17)
            {
                var row = c.Row(letter);
                bool ok = row != null && row.Category == SpellCategory.Domain && row.Handler.StartsWith("domain.", StringComparison.Ordinal) && row.Pending.Length == 0;
                if (ok) handled++; else wrong += letter;
                bool test = row != null && row.Gate == SpellGateMode.Test;
                if (test) tests++;
                if (test != (DomainTest17.IndexOf(letter) >= 0)) wrong += letter;
            }
            r.Fact("wp17.handled", handled);
            r.Fact("wp17.gate_test", tests);
            r.Check("결계-표", handled == 20 && tests == 3 && wrong.Length == 0,
                "all twenty domain glyphs have a domain.* handler (none reserved); Gate test only where the world has no target yet (묵 숨 운)", wrong);

            // the canonical CSV numbers carried by the sheet (a value the CSV does not give is the sheet's own)
            bool Has(char letter, string key, float value) { var row = c.Row(letter); return row != null && r.Near(row.F(key, float.NaN), value); }
            bool radii = true, lives = true;
            foreach (char letter in Domain17) radii &= Has(letter, "domain.radius", letter == '궁' ? 2f : 3f);
            string lived = "군굼궁눔문뭇숙움눅욱";
            float[] life = { .5f, 8f, 4f, 8f, .8f, 8f, 4f, 10f, 6f, 6f };
            for (int i = 0; i < lived.Length; i++) lives &= Has(lived[i], "domain.life", life[i]);
            r.Check("결계-표", radii, "every domain is 3 m across the radius, the walking ring of 궁 2 m (canonical CSV)");
            r.Check("결계-표", lives, "the domains last as the canonical CSV says (군 .5 · 문 .8 · 궁 숙 4 · 굼 눔 뭇 8 · 움 10 · the rest 6 s)");
            r.Check("욱-1", Has('욱', "domain.scale", 1.3f) && Has('욱', "domain.boss", 1.15f), "욱: telegraph x1.3, a boss x1.15");
            r.Check("웃-1", Has('웃', "domain.scale", .5f) && Has('웃', "domain.boss", .75f), "웃: turn x.5, a boss x.75");
            r.Check("묵-1", Has('묵', "domain.scale", 0f) && Has('묵', "domain.boss", .7f), "묵: no dash for an enemy, a boss dashes at x.7");
            r.Check("눙-1", Has('눙', "domain.scale", 0f) && Has('눙', "domain.boss", .5f), "눙: the aim locks at the telegraph's start, a boss halfway");
            r.Check("숙-1", Has('숙', "domain.boss", .7f), "숙: a boss is not caged but slowed x.7");
            r.Check("굿-1", Has('굿', "domain.hold", 1f) && Has('굿', "domain.boss.hold", .5f), "굿: 1 s without a new attack after crossing in, a boss .5 s");
            r.Check("궁-1", Has('궁', "domain.ward.scale", .7f) && Has('궁', "domain.elemental", 1f), "궁: half of 구's elemental cut (.6 / 2 = .3 off)");
            r.Check("문-1", Has('문', "domain.ward.scale", 0f) && Has('문', "domain.elemental", 0f), "문: every attack the wall stops is stopped whole for .8 s");
            r.Check("눗-1", Has('눗', "domain.cap", 4f), "눗: at most four shots burnt");
            r.Check("숭-1", Has('숭', "domain.cap", 3f) && Has('숭', "domain.reflect", .5f), "숭: at most three shots turned back at half power");
            r.Check("뭉-1", Has('뭉', "domain.pull.speed", 1f) && Has('뭉', "domain.pull.budget", 2f), "뭉: at most 1 m/s, a set amount per cast");
            r.Check("굼-1", Has('굼', "domain.boss.hold", 1f), "굼: a boss breaks through after 1 s");
            r.Check("움-1", Has('움', "domain.scale", 2f), "움: ink regeneration x2");
        }

        static void RunWP17Enemy(Report r)
        {
            var d = new EnemyDomainState308();
            r.Check("결계-적", !d.Seal(0, 5f) && !d.Dash(7, 5f, 1f) && !d.Telegraph(7, 5f, 1f) && !d.Turn(7, 5f, 1f) && !d.Seal(7, float.NaN) && d.IsEmpty,
                "an entry without an owner, one that would change nothing or one without an end is refused");

            r.Check("묵-2", d.Dash(7, 5f, 0f) && r.Near(d.DashScale(1f), 0f) && d.Dash(8, 5f, .7f) && r.Near(d.DashScale(1f), 0f),
                "묵: dash 0 means the enemy walks; the slowest of two domains counts");
            d.Clear();

            d.Turn(7, 5f, .5f, 360f);
            r.Check("웃-2", r.Near(d.TurnRate(90f, 1f), 45f) && r.Near(d.TurnRate(0f, 1f), 180f),
                "웃: a turning enemy turns at half its rate; one that turns at once counts as 360°/s, then halved");
            d.Turn(7, 5f, .5f, 0f);
            r.Check("웃-2", r.Near(d.TurnRate(0f, 1f), 0f) && r.Near(d.TurnRate(90f, 6f), 90f),
                "웃: with no instant rate an instant turner stays instant; after the domain's end the rate is its own");
            d.Clear();

            d.Telegraph(7, 5f, 1.3f); d.Telegraph(8, 5f, 1.15f);
            r.Check("욱-2", r.Near(d.TelegraphScale(1f), 1.3f) && r.Near(d.TelegraphScale(5f), 1f), "욱: the longest telegraph counts; at its end x1 again");
            d.Clear();

            r.Check("눙-2", r.Near(d.AimLockShare(1f), -1f), "눙: with no entry the aim tracks to the end as before (-1)");
            d.Aim(7, 5f, .5f, 1f); d.Aim(8, 5f, 0f, 2f);
            float share = d.AimLockShare(1f, out float miss);
            r.Check("눙-2", r.Near(share, 0f) && r.Near(miss, 2f) && !d.Aim(9, 5f, 1.5f), "눙: the earliest lock counts with its own miss radius; a share above 1 is refused");
            d.Clear();

            d.Leash(7, 5f, Vector3.zero, 3f);
            Vector3 kept = d.Contain(new Vector3(5f, 2f, 0f), 1f);
            r.Check("숙-2", r.Near(kept.x, 3f) && r.Near(kept.y, 2f) && r.Near(kept.z, 0f) && d.Contain(new Vector3(1f, 0f, 1f), 1f) == new Vector3(1f, 0f, 1f),
                "숙: a step out of the circle ends on its edge (height kept); a step inside is free");
            d.Exclude(8, 5f, new Vector3(4f, 0f, 0f), 2f);
            Vector3 both = d.Contain(new Vector3(3.5f, 0f, 0f), 1f);
            r.Check("굼-2", d.Excluded(1f) && r.Near(both.x, 2f) && r.Near(both.z, 0f),
                "굼 with 숙: the step is kept in the cage first (3, 0), then put out of the ring (2, 0)", both.x.ToString("0.###") + "," + both.z.ToString("0.###"));
            r.Check("굼-2", r.Near(d.Contain(new Vector3(5f, 0f, 0f), 6f).x, 5f) && !d.Excluded(6f), "both entries end at their time");
            d.Clear();

            var m = new EnemyModifierState308();
            m.Domain.Guard(7, 5f);
            r.Check("순-1", !m.CanGuard(1f) && m.Domain.GuardBlocked(1f),"순: an enemy inside with its guard down cannot raise it");
            m.Clear(); m.Stance.Raise(1f); m.Domain.Guard(7, 5f);
            r.Check("순-1", m.CanGuard(1f), "순: a guard already up stays (낭 · 삿 · a strike from behind are for that)");
            m.Clear();
            r.Check("순-1", m.CanGuard(1f) && m.IsEmpty, "a cleared enemy (death, restore) carries no domain entry");

            d.Hold(7, 2f); d.Seal(8, 5f); d.Call(9, 5f);
            r.Check("굿-2", d.AttackHeld(1f) && !d.AttackHeld(2f), "굿: no attack starts until the hold's own end");
            r.Check("눅-1", d.Sealed(1f) && d.CallBlocked(1f), "눅 · 뭇: sealed and hushed while the entries stand");
            d.Remove(8);
            r.Check("눅-1", !d.Sealed(1f) && d.CallBlocked(1f), "removing one owner ends only its entries (the enemy stepped out of that domain)");
        }

        static void RunWP17Rule(Report r)
        {
            Vector3 centre = new Vector3(10f, 0f, 10f);
            Vector3 put = DomainLeashRule308.Exclude(centre, 3f, new Vector3(11f, 4f, 10f));
            r.Check("굼-3", r.Near(put.x, 13f) && r.Near(put.y, 4f) && r.Near(put.z, 10f), "굼: a point inside the ring goes out to its edge, straight away from the centre");
            Vector3 middle = DomainLeashRule308.Exclude(centre, 3f, centre);
            Vector3 edge = new Vector3(13f, 0f, 10f), outside = new Vector3(20f, 0f, 10f);
            r.Check("굼-3", r.Near(middle.z, 13f) && DomainLeashRule308.Exclude(centre, 3f, edge) == edge && DomainLeashRule308.Exclude(centre, 3f, outside) == outside
                && DomainLeashRule308.Exclude(centre, 0f, centre) == centre, "the centre goes along +z; the edge, outside and a ring of no size are unchanged");

            Vector3 pushed = DomainRule308.PushOut(centre, new Vector3(11f, 2f, 10f), 3.4f, .5f, Vector3.forward);
            r.Check("군-1", r.Near(pushed.x, 13.9f) && r.Near(pushed.y, 2f) && r.Near(pushed.z, 10f), "군: an enemy inside ends on the edge (body counted) plus the margin");
            Vector3 alongFacing = DomainRule308.PushOut(centre, centre, 3f, .5f, new Vector3(0f, 0f, -2f));
            Vector3 noFacing = DomainRule308.PushOut(centre, centre, 3f, 0f, Vector3.zero);
            Vector3 far = new Vector3(15f, 0f, 10f);
            r.Check("군-1", r.Near(alongFacing.z, 6.5f) && r.Near(alongFacing.x, 10f) && r.Near(noFacing.z, 13f) && DomainRule308.PushOut(centre, far, 3f, .5f, Vector3.forward) == far,
                "one on the centre goes along the caster's facing (or +z); one already out is not moved");

            Vector3 step = DomainRule308.PullStep(centre, new Vector3(13f, 0f, 10f), 1f, .1f, 2f, .8f, out float used);
            r.Check("뭉-2", r.Near(used, .1f) && r.Near(step.x, -.1f) && r.Near(step.z, 0f) && r.Near(step.y, 0f), "뭉: one frame moves toward the centre at the set speed");
            DomainRule308.PullStep(centre, new Vector3(13f, 0f, 10f), 1f, .1f, .05f, .8f, out float capped);
            DomainRule308.PullStep(centre, new Vector3(10.8f, 0f, 10f), 1f, .1f, 2f, .8f, out float core);
            DomainRule308.PullStep(centre, new Vector3(13f, 0f, 10f), 1f, 0f, 2f, .8f, out float still);
            r.Check("뭉-2", r.Near(capped, .05f) && r.Near(core, 0f) && r.Near(still, 0f), "the step stops at the budget, at the core and on a paused frame");
            Vector3 at = new Vector3(13f, 0f, 10f); float total = 0f;
            for (int i = 0; i < 600; i++) { var s = DomainRule308.PullStep(centre, at, 1f, 1f / 60f, 2f - total, .8f, out float u); at += s; total += u; }
            r.Check("뭉-2", total <= 2f + 1e-3f && r.Near(total, 2f, 1e-2f) && DomainRule308.PullStep(centre, at, 1f, .1f, 2f - total, .8f, out _) == Vector3.zero,
                "ten seconds inside pull an enemy 2 m at most, and never refill", total.ToString("0.###"));

            r.Check("굿-3", DomainRule308.Entered(true, false, true) && !DomainRule308.Entered(false, false, true) && !DomainRule308.Entered(true, true, true) && !DomainRule308.Entered(true, false, false),
                "굿: only an enemy seen outside and now inside crossed in; one standing inside when the domain rose did not");
            r.Check("결계-적", r.Near(DomainRule308.Pick(true, 0f, .7f), .7f) && r.Near(DomainRule308.Pick(false, 0f, .7f), 0f), "a boss takes the boss value");
        }

        static void RunWP17Player(Report r)
        {
            var p = new PlayerDomainState308();
            r.Check("결계-나", !p.Regen(7, 5f, 1f) && !p.Regen(0, 5f, 2f) && !p.HoldDrain(7, float.PositiveInfinity) && p.IsEmpty,
                "a regen that changes nothing, an entry without owner or without an end is refused");
            p.Regen(7, 5f, 2f); p.Regen(8, 5f, 1.5f);
            r.Check("움-2", r.Near(p.RegenScale(1f), 2f) && r.Near(p.RegenScale(5f), 1f), "움: the strongest regen counts; at its end x1 again");
            p.HoldDrain(7, 5f); p.Anchor(9, 5f); p.Ward(9, 5f);
            r.Check("눔-1", p.DrainHeld(1f) && p.Anchored(1f) && p.Warded(1f), "눔 · 숨 · 운: the entries stand while the player is inside");
            p.End(7);
            r.Check("눔-1", !p.DrainHeld(1f) && p.Anchored(1f) && r.Near(p.RegenScale(1f), 1f), "the player stepped out of one domain: only its entries end");
            p.Clear();
            r.Check("결계-나", p.IsEmpty && !p.Anchored(1f), "a clear (death, restore) drops every entry");
        }
    }
}
