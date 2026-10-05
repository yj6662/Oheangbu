using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 WP-06 fixtures (SPEC-SPELL-120-308): the lingering zones on the real wiring. Every cast goes through the resolver
    // (f.Cast); zones are advanced by ticking the handler with an explicit clock (Edit Mode has no running frame clock).
    // Time comparisons leave a few hundredths of a second of room: the editor clock can be a large float.
    // What stays for Play: the look of each zone, enemy AI walking in and out, and a real enemy swing at the tree.
    public static partial class Spell120Checks308
    {
        static readonly string[] WP06Sheets = { "Rules308_Base", "Rules308_WP00", "Rules308_WP06" };

        // A presenter that draws nothing but hands out real handles and remembers what it was told (the null presenter's
        // handle is always 0, and a handler has nothing to cue on a presentation that was never shown).
        sealed class WP06Presenter : ISpellPresenter
        {
            public readonly List<SpellFxRequest> Begun = new List<SpellFxRequest>();
            public readonly List<KeyValuePair<int, SpellFxCue>> Cues = new List<KeyValuePair<int, SpellFxCue>>();
            public readonly List<int> Ended = new List<int>();
            int _next;
            public SpellFxHandle Begin(in SpellFxRequest request) { Begun.Add(request); return new SpellFxHandle(++_next); }
            public void Cue(SpellFxHandle handle, SpellFxCue cue, in SpellFxCueArgs args) { Cues.Add(new KeyValuePair<int, SpellFxCue>(handle.Id, cue)); }
            public void End(SpellFxHandle handle, bool immediate = false) { Ended.Add(handle.Id); }
            public void EndAll() { }
            public int Count(SpellFxCue cue) => Cues.Count(c => c.Value == cue);
        }

        static partial void FixturesWP06(Report r)
        {
            // grid cells: 14 = wood zone.slow, 17 = wood zone.spikes, 38 = fire cone.healzone, 10 = wood zone.tree
            char vine = SpellGrammar308.LetterAt(14), spikes = SpellGrammar308.LetterAt(17), healing = SpellGrammar308.LetterAt(38), tree = SpellGrammar308.LetterAt(10);
            if (!r.Wants(new string(new[] { vine, spikes, healing, tree }))) return;
            const float room = .05f;

            // registration: the container builds the four handlers around one zone list; a handler made with new owns its own.
            // 2026-10-04: the count was "exactly 4". WP-13 added a fifth zone handler (zone.haze) that takes the same list by
            // design (SpellEffectInstaller308.WP06.cs), so the clause now asks for the four WP-06 handlers by type, once each,
            // and for ONE list shared by every zone handler the installer builds, whatever their number.
            var built = SpellEffectInstaller308.CreateAll().OfType<ZoneEffect308>().ToArray();
            bool four = built.OfType<VineZoneEffect308>().Count() == 1 && built.OfType<SpikeZoneEffect308>().Count() == 1 &&
                built.OfType<HealZoneEffect308>().Count() == 1 && built.OfType<HealTreeEffect308>().Count() == 1;
            r.Check("W06", four && built.Length >= 4 && built.All(e => e.Zones != null) && built.Select(e => e.Zones).Distinct().Count() == 1,
                "the installer registers the four WP-06 zone handlers, and every zone handler it builds shares one SpellZoneRuntime308 (" + built.Length + " handlers: " +
                string.Join(" ", built.Select(e => e.Id).OrderBy(id => id, System.StringComparer.Ordinal)) + ")");
            r.Check("W06", new VineZoneEffect308().Zones != new VineZoneEffect308().Zones && SpellEffectInstaller308.CreateAll().OfType<ZoneEffect308>().First().Zones != built[0].Zones,
                "the list is instance state: two handlers made with new, and two containers, never share it");

            var zones = new SpellZoneRuntime308();
            var vineFx = new VineZoneEffect308(zones); var spikeFx = new SpikeZoneEffect308(zones);
            var healFx = new HealZoneEffect308(zones); var treeFx = new HealTreeEffect308(zones);
            using (var f = NewFixture(r, new ISpellEffect[] { vineFx, spikeFx, healFx, treeFx }, WP06Sheets))
            {
                var fx = new WP06Presenter();
                f.Wiring.ConstructSpells(f.Registry, f.Finals, fx);      // the same registry and unlocks, a presenter that can be read
                ISpellCastHost host = f.Wiring;
                f.Finals.Open.Add(SpellFinal.Giyeok); f.Finals.Open.Add(SpellFinal.Siot); f.Finals.Open.Add(SpellFinal.Mieum);
                float cost = f.Config.SpellInkCost, brush = f.Brush(), now = Time.time;
                var all = f.Inside.Concat(f.Outside).Concat(new[] { f.Boss }).ToArray();
                Vector3 home = f.Player.transform.position;
                float PlayerHp() => f.Player.Hp01 * f.Player.MaxHp;
                void Tick(ISpellEffect effect, float at, float delta) => effect.Tick(new SpellTickContext(at, delta, host, fx));
                void Reset() { f.Wiring.ClearSpells308(SpellClearReason.Disabled); f.RestoreAll(); f.Player.transform.SetPositionAndRotation(home, Quaternion.identity); }
                SpellZone308[] Of(string owner) => zones.Zones.Where(z => z.Owner == owner).ToArray();

                // ================= zone.slow =================
                if (r.Wants(vine.ToString()))
                {
                    f.Resolver.TryRow(vine, out var row);
                    float radius = row.F("zone.radius", 0f), life = row.F("zone.life", 0f), speed = row.F("slow.speed", 1f);
                    Reset(); f.Aim(f.Inside[1]);
                    float ink = f.Ink.Value; int begun = fx.Begun.Count; float[] hp = all.Select(e => e.Hp).ToArray();
                    f.Cast(vine);
                    var placed = Of(VineZoneEffect308.HandlerId);
                    r.Check("곡-1", f.Misfires.Count == 0 && f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && placed.Length == 1 &&
                        placed[0].Centre == f.Inside[1].transform.position && Near(placed[0].Radius, radius) && fx.Begun.Count == begun + 1 && fx.Begun.Last().Role == SpellFxRole.Zone,
                        "a drawn cast costs one spell, places one circle on the aimed enemy's spot and begins one zone presentation");
                    r.Check("곡-1", f.Plans.Count == 0 && f.PendingCount == 0, "no hit is scheduled: the vines do not strike (section 15 default)");
                    if (placed.Length == 1)
                    {
                        var zone = placed[0];
                        var inside = all.Where(e => AreaGeometry.InCircle(zone.Centre, e.transform.position, radius, out _)).ToArray();
                        var outside = all.Except(inside).ToArray();
                        Tick(vineFx, now, 0f);
                        r.Check("곡-2", zone.From > now && all.All(e => e.Control.MovementScale(now) == 1f), "before the vines have formed (the row's formation delay) nobody is slowed");
                        float mid = zone.From + 1f;
                        Tick(vineFx, mid, 1f);
                        r.Check("곡-3", inside.Length >= 3 && outside.Length >= 3 && inside.All(e => Near(e.Control.MovementScale(mid), speed)) && outside.All(e => e.Control.MovementScale(mid) == 1f),
                            "enemies inside move at slow.speed, enemies outside at full speed (" + inside.Length + " in, " + outside.Length + " out)");
                        r.Check("곡-3", inside.Contains(f.Boss) && all.All(e => !e.Control.BlocksActions(mid)) && all.Select(e => e.Hp).SequenceEqual(hp) && f.Hits.Count == 0 &&
                            all.All(e => e.Groggy.Value01 == 0f), "a slow only: no action block (a boss is slowed like anyone), no damage, no groggy");
                        Vector3 spot = f.Inside[0].transform.position;
                        f.Inside[0].transform.position = zone.Centre + Vector3.right * (radius + 5f);
                        Tick(vineFx, mid + .25f, .25f);
                        bool left = f.Inside[0].Control.MovementScale(mid + .25f) == 1f;
                        f.Inside[0].transform.position = spot;
                        Tick(vineFx, mid + .5f, .25f);
                        r.Check("곡-3", left && Near(f.Inside[0].Control.MovementScale(mid + .5f), speed), "an enemy that walks out is at full speed at once; walking back in slows it again");
                        int expired = fx.Count(SpellFxCue.Expire);
                        Tick(vineFx, zone.Until - .5f, .5f);
                        bool still = Of(VineZoneEffect308.HandlerId).Length == 1 && Near(f.Inside[1].Control.MovementScale(zone.Until - .5f), speed);
                        Tick(vineFx, zone.Until + .5f, 1f);
                        r.Check("곡-2", still && Near(zone.Until - zone.From, life, room) && Of(VineZoneEffect308.HandlerId).Length == 0 &&
                            all.All(e => e.Control.MovementScale(zone.Until + .5f) == 1f) && fx.Count(SpellFxCue.Expire) == expired + 1,
                            "the zone lasts zone.life, then it is removed, everybody is released and the presenter hears one expiry");
                    }
                    Reset(); f.Aim(f.Inside[1]); f.Cast(vine); f.Ink.Restore(); f.Aim(f.Outside[1]); int ended = fx.Ended.Count; f.Cast(vine);
                    placed = Of(VineZoneEffect308.HandlerId);
                    bool replaced = placed.Length == 1 && placed[0].Centre == f.Outside[1].transform.position && fx.Ended.Count == ended + 1;
                    r.Check("곡-1", replaced, "a second cast replaces the first zone (zone.max 1) and ends its presentation");
                    if (placed.Length == 1)
                    {
                        float at = placed[0].From + .5f;
                        Tick(vineFx, at, .5f);
                        bool slowed = Near(f.Outside[1].Control.MovementScale(at), speed);
                        f.Wiring.ClearSpells308(SpellClearReason.Rest);
                        r.Check("곡-2", slowed && zones.Count == 0 && f.Outside[1].Control.MovementScale(at) == 1f, "rest, death or scene leave drops the zone and its slow");
                    }
                }

                // ================= zone.spikes =================
                if (r.Wants(spikes.ToString()))
                {
                    f.Resolver.TryRow(spikes, out var row); f.Resolver.TryRow(SpellGrammar308.BaseOf(spikes), out var circleRow);
                    float life = row.F("zone.life", 0f), dps = row.F("zone.dps", 0f), step = row.F("zone.tick", 0f);
                    Reset(); f.Aim(f.Inside[1]);
                    float ink = f.Ink.Value; f.Cast(spikes);
                    var placed = Of(SpikeZoneEffect308.HandlerId);
                    bool planned = f.Plans.Count == 1 && f.Plans[0].Area != null && placed.Length == 1;
                    r.Check("곳-1", planned && f.Misfires.Count == 0 && f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost), "a drawn cast costs one spell and makes one circle plan and one zone");
                    if (planned)
                    {
                        var plan = f.Plans[0]; var zone = placed[0];
                        var expected = all.Where(e => AreaGeometry.InCircle(f.Inside[1].transform.position, e.transform.position, circleRow.AreaRadius, out _)).ToArray();
                        r.Check("곳-1", plan.Area.Shape == AreaShape.Circle && plan.Area.Point == f.Inside[1].transform.position && Near(plan.Area.Radius, circleRow.AreaRadius) &&
                            Near(plan.Area.Delay, circleRow.AreaImpactDelay) && expected.Length >= 2 && plan.Hits.Select(h => h.Target).OrderBy(e => e.GetInstanceID()).SequenceEqual(expected.OrderBy(e => e.GetInstanceID())) &&
                            plan.Hits.All(h => Near(h.Power, circleRow.BasePower * brush)),
                            "the first hit is the wood circle's judgement: same centre, radius, delay, the enemies inside, base power x brush (" + plan.Hits.Count + " hits)");
                        r.Check("곳-1", plan.Area.Spikes.Count == 0 && plan.Hits.All(h => Near(h.ImpactTime - plan.Area.CreatedAt, plan.Area.Delay, room)),
                            "every enemy in the circle is hit at the formation delay (the staggered rise stays the wood circle's own trait)");
                        r.Check("곳-2", zone.Centre == plan.Area.Point && Near(zone.Radius, plan.Area.Radius) && Near(zone.From - now, plan.Area.Delay, room) && Near(zone.Until - zone.From, life, room),
                            "the zone is that circle, from the first rise for zone.life");
                        f.Land();
                        float stepPower = SpellZoneRule308.StepPower(circleRow.BasePower * brush, dps, step);
                        var victim = expected[0]; var stayer = expected[1]; var bystander = all.Except(expected).First();
                        float hp = victim.Hp, away = bystander.Hp; int hits = f.Hits.Count;
                        Tick(spikeFx, zone.From + step * .5f, step * .5f);
                        bool early = victim.Hp == hp && f.Hits.Count == hits;
                        Tick(spikeFx, zone.From + step + room, step * .5f);
                        var stepHits = f.Hits.Skip(hits).ToArray();
                        r.Check("곳-3", early && stepPower > 0f && Near(hp - victim.Hp, stepPower) && bystander.Hp == away && stepHits.Length == expected.Length &&
                            stepHits.All(h => h.Attack.Source == DamageSource.PersistentSpell && h.Attack.AttackId == zone.Token && Near(h.AppliedDamage, stepPower)),
                            "after one zone.tick every enemy standing in the spikes takes one step (cast power x zone.dps x zone.tick), nobody outside does");
                        victim.OpenWeakPoint();
                        float groggy = victim.Groggy.Value01; hits = f.Hits.Count;
                        Tick(spikeFx, zone.From + step * 2f + room, step);
                        r.Check("곳-3", f.Hits.Count > hits && victim.WeakPointActive && victim.WeakPointElementMask == 0 && !victim.CompletionAwarded && victim.Groggy.Value01 == groggy &&
                            f.Hits.Skip(hits).All(h => h.CompletionBonus == 0f), "a step is damage over time: it never feeds the five-element completion or groggy, even inside an open weak point");
                        victim.ResetCombatState();
                        Vector3 spot = victim.transform.position; victim.transform.position = zone.Centre + Vector3.right * (zone.Radius + 5f);
                        hp = victim.Hp; Tick(spikeFx, zone.From + step * 3f + room, step);
                        bool spared = victim.Hp == hp;
                        victim.transform.position = spot;
                        float stayed = stayer.Hp; int expiredBefore = fx.Count(SpellFxCue.Expire);
                        Tick(spikeFx, zone.Until + 5f, 5f);
                        int total = Mathf.RoundToInt(life / step);
                        r.Check("곳-3", spared && total > 3 && Near(stayed - stayer.Hp, stepPower * (total - 3), .01f),
                            "an enemy that stepped out takes nothing; one that stays for the whole life takes life / tick steps in total (" + total + ")");
                        r.Check("곳-2", Of(SpikeZoneEffect308.HandlerId).Length == 0 && fx.Count(SpellFxCue.Expire) == expiredBefore + 1, "after zone.life the zone is removed and the presenter hears one expiry");
                    }
                }

                // ================= cone.healzone =================
                if (r.Wants(healing.ToString()))
                {
                    f.Resolver.TryRow(healing, out var row); f.Resolver.TryRow(SpellGrammar308.BaseOf(healing), out var coneRow);
                    float radius = row.F("heal.radius", 0f), life = row.F("heal.life", 0f), rate = row.F("heal.hp01ps", 0f); int max = Mathf.RoundToInt(row.F("heal.max", 0f));
                    Reset(); f.Aim(null);
                    var expected = all.Where(e => AreaGeometry.InCone(home, Vector3.forward, e.transform.position, f.Config.AreaConeAngle, f.Config.AreaConeRange, out _, out _)).ToArray();
                    float ink = f.Ink.Value; f.Cast(healing);
                    bool planned = f.Plans.Count == 1 && f.Plans[0].Area != null;
                    r.Check("녹-1", planned && f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && f.Plans[0].Area.Shape == AreaShape.Cone && expected.Length >= 1 &&
                        f.Plans[0].Hits.Select(h => h.Target).OrderBy(e => e.GetInstanceID()).SequenceEqual(expected.OrderBy(e => e.GetInstanceID())) &&
                        f.Plans[0].Hits.All(h => Near(h.Power, coneRow.BasePower * brush)),
                        "the first hit is the fire cone's judgement: the enemies in the cone ahead, base power x brush (" + (planned ? f.Plans[0].Hits.Count : 0) + " hits)");
                    r.Check("녹-2", Of(HealZoneEffect308.HandlerId).Length == 0, "a scheduled hit is not a hit yet: no zone before anything has landed");
                    f.Player.TakeDamage(40f); float hp = PlayerHp(); int secondary = fx.Count(SpellFxCue.Secondary);
                    f.Land();
                    var placed = Of(HealZoneEffect308.HandlerId);
                    r.Check("녹-2", f.Hits.Count == expected.Length && placed.Length == Mathf.Min(expected.Length, max) && placed.Length > 0 &&
                        placed.All(z => expected.Any(e => e.transform.position == z.Centre) && Near(z.Radius, radius) && Near(z.Until - z.From, life, room)) &&
                        placed.Select(z => z.Centre).Distinct().Count() == placed.Length && fx.Count(SpellFxCue.Secondary) == secondary + placed.Length,
                        "a healing zone opens on the spot of each enemy the cone really hit, at most heal.max (" + placed.Length + " zones for " + expected.Length + " hits)");
                    if (placed.Length > 0)
                    {
                        Tick(healFx, now + 1f, 1f);
                        bool outside = !zones.Covered(home, now + 1f, HealZoneEffect308.HandlerId) && Near(PlayerHp(), hp);
                        f.Player.transform.position = placed[0].Centre;
                        float inkBefore = f.Ink.Value;
                        Tick(healFx, now + 2f, 1f);
                        r.Check("녹-3", outside && rate > 0f && Near(PlayerHp() - hp, rate * f.Player.MaxHp, room) && f.Ink.Value == inkBefore,
                            "standing outside heals nothing; one second inside heals heal.hp01ps of max HP (HP only, no ink)");
                        hp = PlayerHp(); int expired = fx.Count(SpellFxCue.Expire);
                        Tick(healFx, now + life + 5f, 1f);
                        r.Check("녹-3", Of(HealZoneEffect308.HandlerId).Length == 0 && Near(PlayerHp(), hp) && fx.Count(SpellFxCue.Expire) == expired + 1,
                            "after heal.life the zones are gone and heal no more; the cast's one presentation hears one expiry");
                    }
                    // a cast that hits nobody: turn the caster until the cone is empty
                    Reset(); f.Aim(null);
                    float yaw = new[] { 270f, 90f, 180f, 225f, 135f }.FirstOrDefault(a => !all.Any(e => AreaGeometry.InCone(home, Quaternion.Euler(0f, a, 0f) * Vector3.forward, e.transform.position, f.Config.AreaConeAngle, f.Config.AreaConeRange, out _, out _)));
                    if (yaw == 0f) r.Check("녹-4", false, "fixture: no direction with an empty cone was found, the thin-air cast was not exercised");
                    else
                    {
                        f.Player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                        f.Player.TakeDamage(40f); ink = f.Ink.Value; hp = PlayerHp();
                        f.Cast(healing); f.Land(); Tick(healFx, now + 1f, 1f);
                        r.Check("녹-4", f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 0 && f.Hits.Count == 0 &&
                            zones.Count == 0 && Near(PlayerHp(), hp), "a cast into thin air spends its ink, hits nobody and leaves no healing zone");
                    }
                }

                // ================= zone.tree =================
                if (r.Wants(tree.ToString()))
                {
                    f.Resolver.TryRow(tree, out var row);
                    float radius = row.F("tree.radius", 0f), life = row.F("tree.life", 0f), rate = row.F("heal.hp01ps", 0f), body = row.F("tree.body", 0f), reach = row.F("tree.reach", 0f);
                    int hitsNeeded = Mathf.RoundToInt(row.F("tree.hits", 1f));
                    Reset(); f.Player.TakeDamage(50f);
                    float ink = f.Ink.Value, hp = PlayerHp(); f.Cast(tree);
                    var placed = Of(HealTreeEffect308.HandlerId);
                    r.Check("검-1", f.Misfires.Count == 0 && f.Accepted.Count == 1 && f.Accepted[0].Kind == SpellKind.Buff && Near(ink - f.Ink.Value, cost) && placed.Length == 1 &&
                        placed[0].Centre == home && Near(placed[0].Radius, radius) && f.Plans.Count == 0 && f.PendingCount == 0,
                        "a drawn cast costs one spell and plants one tree zone at the caster's feet; nothing is scheduled against an enemy");
                    float healed = 0f;
                    if (placed.Length == 1)
                    {
                        Tick(treeFx, now + 1f, 1f);
                        healed = PlayerHp() - hp;
                        f.Player.transform.position = home + Vector3.left * (radius + 4f);
                        Tick(treeFx, now + 2f, 1f);
                        r.Check("검-2", rate > 0f && Near(healed, rate * f.Player.MaxHp, room) && Near(PlayerHp() - hp, healed) && Of(HealTreeEffect308.HandlerId)[0].Centre == home,
                            "inside the circle one second heals heal.hp01ps of max HP; outside nothing; the tree stays where it was planted");
                        f.Player.transform.position = home; int expired = fx.Count(SpellFxCue.Expire);
                        Tick(treeFx, now + life - .5f, .1f); bool alive = Of(HealTreeEffect308.HandlerId).Length == 1;
                        Tick(treeFx, now + life + .5f, 1f);
                        r.Check("검-3", alive && Near(placed[0].Until - placed[0].From, life, room) && Of(HealTreeEffect308.HandlerId).Length == 0 && fx.Count(SpellFxCue.Expire) == expired + 1 &&
                            fx.Count(SpellFxCue.Break) == 0, "the tree lives for tree.life, then it is removed and the presenter hears one expiry (not a break)");
                    }
                    Reset(); f.Cast(tree); f.Ink.Restore(); int endedBefore = fx.Ended.Count; f.Player.transform.position = home + Vector3.left * 30f; f.Cast(tree);
                    placed = Of(HealTreeEffect308.HandlerId);
                    r.Check("검-1", placed.Length == 1 && placed[0].Centre == home + Vector3.left * 30f && fx.Ended.Count == endedBefore + 1, "planting again replaces the old tree (tree.max 1)");

                    // ---- struck and gone ----
                    Reset(); f.Cast(tree); f.Player.TakeDamage(30f);
                    var striker = f.Inside[0]; Vector3 strikerHome = striker.transform.position;
                    void Strike(EnemyVitals attacker, IncomingDamageKind kind)
                    {
                        // the wiring listens to PlayerVitals.Damaged in OnEnable, which Edit Mode never runs: deliver the same call by hand
                        if (EnemyStrike308.Deliver(f.Player, attacker, 5f, kind)) SpellFixture308.Call(f.Wiring, "OnPlayerDamaged", 5f);
                    }
                    bool Standing() { var trees = Of(HealTreeEffect308.HandlerId); return trees.Length == 1 && !trees[0].Broken; }
                    striker.transform.position = home + Vector3.forward * (body + reach + 1f);
                    Strike(striker, IncomingDamageKind.Melee);
                    bool farKept = Standing();
                    striker.transform.position = home + Vector3.forward * (body + reach - .2f);
                    Strike(striker, IncomingDamageKind.Ranged);
                    bool rangedKept = Standing();
                    r.Check("검-4", reach > 0f && farKept && rangedKept, "a melee attacker out of tree.reach of the trunk, or a ranged hit, leaves the tree standing");
                    ink = f.Ink.Value; hp = PlayerHp(); int broken = fx.Count(SpellFxCue.Break);
                    for (int i = 0; i < hitsNeeded; i++) Strike(striker, IncomingDamageKind.Melee);
                    bool struck = Of(HealTreeEffect308.HandlerId).Length == 1 && Of(HealTreeEffect308.HandlerId)[0].Broken;
                    Tick(treeFx, now + 1f, 1f);
                    r.Check("검-4", struck && Of(HealTreeEffect308.HandlerId).Length == 0 && fx.Count(SpellFxCue.Break) == broken + 1,
                        "tree.hits melee strikes on the player from within reach of the trunk break the tree; it is removed and the presenter hears a break");
                    r.Check("검-4", Near(hp - PlayerHp(), 5f * hitsNeeded) && f.Ink.Value == ink,
                        "being hit gave nothing back: HP fell by exactly the strikes, no healing from the broken tree, no ink (no reward for being hit)");
                    striker.transform.position = strikerHome;
                    Reset(); f.Cast(tree);
                    bool missed = treeFx.Break(home + Vector3.right * 50f, 1f) == 0 && Standing();
                    int direct = 0; for (int i = 0; i < hitsNeeded; i++) direct += treeFx.Break(home + Vector3.right * (body * .5f), 0f);
                    r.Check("검-4", missed && direct == 1 && !Standing(), "Break(point, reach) is the entry for an attack aimed at the tree itself: a strike on the trunk breaks it, a strike elsewhere does not");

                    // A9: the same cast with the null presenter gives the same rule result
                    f.Wiring.ConstructSpells(f.Registry, f.Finals, f.Fx);
                    Reset(); f.Player.TakeDamage(50f); hp = PlayerHp(); f.Cast(tree);
                    treeFx.Tick(new SpellTickContext(now + 1f, 1f, host, f.Fx));
                    r.Check("A9", Of(HealTreeEffect308.HandlerId).Length == 1 && Near(PlayerHp() - hp, healed) && healed > 0f, "with the null presenter the tree heals the same amount (rules never read a presenter)");
                    f.Wiring.ConstructSpells(f.Registry, f.Finals, fx);
                }
                Reset();
            }
        }
    }
}
