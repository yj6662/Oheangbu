using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-128 B / D308-132 fixtures: five-element reactions on the real wiring. The material zones are put straight into
    // the shared zone list, already formed (Edit Mode has no running clock to wait out a formation delay); the follow-up is a
    // drawn letter through the resolver, so the dispatch hook, the legacy route (노 오 모 plan after their accept) and the
    // handler route (농) are the real ones. Ticks use an explicit clock.
    // What stays for Play: the look (the product has no presentation yet: the table is not baked into Resources until it has).
    public static partial class Spell120Checks308
    {
        static readonly string[] R128Sheets = { "Rules308_Base", "Rules308_WP00", "Rules308_WP06", "Rules308_WP13" };

        static partial void FixturesR128(Report r)
        {
            const char fireCone = '노', waterPath = '오', earthPath = '모', fireHaze = '농';
            if (!r.Wants(new string(new[] { fireCone, waterPath, earthPath, fireHaze }))) return;
            const float room = .05f;
            var errors = new List<string>();
            string csv = File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Data/Spells", ReactionTable308.FileName), Encoding.UTF8);
            var rows = ReactionTable308.Parse(csv, errors);
            if (!r.Check("R128", errors.Count == 0 && rows.Count == 3, "the reaction table parses " + string.Join(" | ", errors))) return;
            var wild = ReactionRule308.Find(rows, Element.Wood, Element.Fire);
            var steam = ReactionRule308.Find(rows, Element.Fire, Element.Water);
            var mud = ReactionRule308.Find(rows, Element.Water, Element.Earth);

            var built = SpellEffectInstaller308.CreateAll();
            var react = built.OfType<ReactionZoneEffect308>().ToArray();
            r.Check("R128", react.Length == 1 && react[0].Zones == built.OfType<VineZoneEffect308>().First().Zones,
                "the installer builds the reaction handler on the one shared zone list");

            var zones = new SpellZoneRuntime308();
            var vineFx = new VineZoneEffect308(zones); var hazeFx = new HazeZoneEffect308(zones); var healFx = new HealZoneEffect308(zones);
            var reactFx = new ReactionZoneEffect308(zones);
            reactFx.UseTable(rows);
            using (var f = NewFixture(r, new ISpellEffect[] { vineFx, hazeFx, healFx, reactFx }, R128Sheets))
            {
                var fx = new WP06Presenter();
                f.Wiring.ConstructSpells(f.Registry, f.Finals, fx);
                ISpellCastHost host = f.Wiring;
                f.Finals.Open.Add(SpellFinal.Ieung); f.Finals.Open.Add(SpellFinal.Giyeok);
                float now = Time.time;
                var all = f.Inside.Concat(f.Outside).Concat(new[] { f.Boss }).ToArray();
                Vector3 home = f.Player.transform.position;
                void Tick(ISpellEffect effect, float at, float delta) => effect.Tick(new SpellTickContext(at, delta, host, fx));
                void Reset() { f.Wiring.ClearSpells308(SpellClearReason.Disabled); zones.Clear(); f.RestoreAll(); f.Player.transform.SetPositionAndRotation(home, Quaternion.identity); }
                SpellZone308[] Of(string owner) => zones.Zones.Where(z => z.Owner == owner).ToArray();
                int Material(string owner, Element element, Vector3 centre, float radius, float value)
                {
                    var zone = new SpellZone308
                    {
                        Owner = owner, Cast = zones.NextCast(), Letter = '곡', Element = element, Centre = centre, Radius = radius,
                        From = now - 1f, Until = now + 7f, Life = 8f, Value = value,
                        Token = AttackProvenance.Create(f.Player, DamageSource.PersistentSpell, element).AttackId,
                    };
                    zone.Fx = 1000 + zone.Cast;   // a handle of its own, so its owner has a look to tell
                    return zones.Add(zone, 1, now, null);
                }
                float LastPower() => f.Accepted.Count > 0 ? f.Accepted.Last().Power : 0f;

                // ================= 목생화: vines + the fire cone (a legacy row: it plans after its accept) =================
                {
                    Reset(); f.Aim(f.Inside[1]);
                    Vector3 spot = f.Inside[1].transform.position;
                    int vines = Material(VineZoneEffect308.HandlerId, Element.Wood, spot, 3.5f, .5f);
                    Tick(vineFx, now, 0f);
                    bool slowed = Near(f.Inside[1].Control.MovementScale(now), .5f);
                    float[] hp = all.Select(e => e.Hp).ToArray(); int breaks = fx.Count(SpellFxCue.Break), reacts = fx.Count(SpellFxCue.React), looks = fx.Reacts.Count;
                    f.Cast(fireCone);
                    var product = Of(ReactionZoneEffect308.HandlerId);
                    float power = LastPower();
                    r.Check("R128-wild", f.Misfires.Count == 0 && f.Accepted.Count == 1 && f.Plans.Count == 1 && f.Plans[0].Area != null && f.Plans[0].Area.Shape == AreaShape.Cone,
                        "the fire cone is accepted and plans its cone (legacy route)");
                    r.Check("R128-wild", slowed && zones.Zones.First(z => z.Id == vines).Reacted && product.Length == 1,
                        "the cone reaching the vines consumes them and lays one wildfire");
                    if (product.Length == 1)
                    {
                        var fire = product[0];
                        float life = ReactionRule308.ProductLife(wild, 7f);
                        r.Check("R128-wild", fire.Element == Element.Fire && Near(fire.Radius, 3.5f * 1.5f) && fire.Centre == spot && Near(fire.Until - fire.From, life, room) && fire.Depth == 1 &&
                            Near(fire.Value, SpellZoneRule308.StepPower(power, wild.PerSecond, wild.Tick) * host.DamageScale(Element.Fire)),
                            "the wildfire: same centre, radius x1.5, remaining 7 s -> " + life + " s, step = cone power x .25 x .5 s (" + fire.Value + ")");
                        Tick(vineFx, now, 0f);
                        r.Check("R128-wild", Of(VineZoneEffect308.HandlerId).Length == 0 && all.All(e => e.Control.MovementScale(now) == 1f) && fx.Count(SpellFxCue.Break) == breaks && fx.Count(SpellFxCue.React) == reacts + 1,
                            "the vines' owner sweeps them at its next tick: the slow is gone, its floor is told React (no Break splash)");
                        // D308-132 step 6: the consumed floor hears the way the wave ran; the product's look is told once, by kind
                        Vector3 way = (spot - home); way.y = 0f; way.Normalize();
                        var told = fx.ReactCues.LastOrDefault();
                        r.Check("R128-wild", fx.ReactCues.Count == 1 && Vector3.Dot(told.Direction, way) > .99f && Near(told.Value, 3.5f) && told.Point == spot,
                            "the floor's React cue carries the wave's flat direction, the material's radius and its centre");
                        var look = fx.Reacts.Skip(looks).ToArray();
                        r.Check("R128-wild", look.Length == 1 && look[0].Kind == ReactionKind308.Wildfire && look[0].Point == spot && Near(look[0].Value, 3.5f * 1.5f) && Near(look[0].FromRadius, 3.5f) &&
                            Near(look[0].Life, life, room) && Vector3.Dot(look[0].Direction, way) > .99f,
                            "the wildfire look is told once: kind, centre, radius x1.5 (grown from the material's 3.5), the product's life, the wave's direction");
                        r.Check("R128-wild", all.Select(e => e.Hp).SequenceEqual(hp), "a burning reaction has no instant blow (the cone's own hits are still on their way)");
                        f.Land(); f.ClearLog();
                        var inFire = all.Where(e => e.IsAlive && SpellZoneRule308.Inside(fire, e.transform.position)).ToArray();
                        var outFire = all.Except(inFire).ToArray();
                        float[] before = all.Select(e => e.Hp).ToArray(); float groggy = inFire.Length > 0 ? inFire[0].Groggy.Value01 : 0f;
                        Tick(reactFx, fire.From + wild.Tick + room, wild.Tick);
                        r.Check("R128-wild", inFire.Length >= 2 && f.Hits.Count == inFire.Length && f.Hits.All(h => h.Attack.Source == DamageSource.PersistentSpell && h.Attack.AttackId == fire.Token && Near(h.AppliedDamage, fire.Value, .01f)) &&
                            outFire.All(e => e.Hp == before[System.Array.IndexOf(all, e)]),
                            "one step later every enemy in the wildfire takes one step of damage over time, nobody outside (" + inFire.Length + " in)");
                        r.Check("R128-wild", inFire.All(e => !e.CompletionAwarded) && inFire[0].Groggy.Value01 == groggy && f.Hits.All(h => h.CompletionBonus == 0f),
                            "the steps never feed groggy or the five-element completion");
                        Tick(reactFx, fire.Until + 1f, 1f);
                        r.Check("R128-wild", Of(ReactionZoneEffect308.HandlerId).Length == 0, "the wildfire runs out at its life");
                    }
                    // a second cone finds nothing: the vines were consumed once
                    f.Ink.Restore(); f.ClearLog(); f.Cast(fireCone);
                    r.Check("R128-wild", Of(ReactionZoneEffect308.HandlerId).Length == 0, "a material reacts once: a second cone lays nothing");
                }

                // ================= the handler route: vines + 농 (its own haze is laid by the same cast and never reacts) =================
                {
                    Reset(); f.Aim(f.Inside[1]);
                    int vines = Material(VineZoneEffect308.HandlerId, Element.Wood, f.Inside[1].transform.position, 3.5f, .5f);
                    f.Cast(fireHaze);
                    r.Check("R128-wild", f.Misfires.Count == 0 && f.Accepted.Count == 1 && Of(HazeZoneEffect308.HandlerId).Length == 1 &&
                        zones.Zones.First(z => z.Id == vines).Reacted && Of(ReactionZoneEffect308.HandlerId).Length == 1,
                        "a handler-route fire cone (농) also burns the vines; its own haze stays");
                }

                // ================= 수극화: a haze + the water wave =================
                {
                    Reset(); f.Aim(f.Inside[2]);
                    Vector3 spot = f.Inside[2].transform.position;
                    int haze = Material(HazeZoneEffect308.HandlerId, Element.Fire, spot, 3f, .5f);
                    Tick(hazeFx, now, 0f);
                    bool veiled = hazeFx.VeiledEnemies > 0;
                    var material = zones.Zones.First(z => z.Id == haze);
                    var inHaze = all.Where(e => e.IsAlive && SpellZoneRule308.Inside(material, e.transform.position, e.BodyRadius)).ToArray();
                    float[] hp = all.Select(e => e.Hp).ToArray();
                    f.Cast(waterPath);
                    float power = LastPower();
                    var product = Of(ReactionZoneEffect308.HandlerId);
                    float blow = ReactionRule308.HitPower(steam, power) * host.DamageScale(Element.Water);
                    r.Check("R128-steam", veiled && f.Plans.Count == 1 && f.Plans[0].Area != null && f.Plans[0].Area.Shape == AreaShape.Path && zones.Zones.First(z => z.Id == haze).Reacted && product.Length == 1,
                        "the water wave (legacy path) reaching the haze consumes it and leaves a steam");
                    if (product.Length == 1)
                    {
                        var veil = product[0];
                        r.Check("R128-steam", veil.Element == Element.Water && veil.Value == 0f && veil.TickEvery == 0f && Near(veil.Until - veil.From, steam.LifeCap, room) && veil.Depth == 1,
                            "the steam: water, no rule, " + steam.LifeCap + " s");
                        var seen = fx.Reacts.LastOrDefault();
                        r.Check("R128-steam", seen.Kind == ReactionKind308.Steam && seen.Point == spot && Near(seen.Value, 3f) && Near(seen.FromRadius, 3f) && Near(seen.Life, steam.LifeCap, room) &&
                            Vector3.Dot(seen.Direction, new Vector3(spot.x - home.x, 0f, spot.z - home.z).normalized) > .99f,
                            "the steam look is told: kind, the haze's centre and radius (no growth), 2 s, along the wave's way (caster -> beyond)");
                        var instant = f.Hits.Where(h => h.Attack.AttackId == veil.Token).ToArray();
                        r.Check("R128-steam", inHaze.Length >= 2 && instant.Length == inHaze.Length && instant.All(h => h.Attack.Source == DamageSource.PersistentSpell && Near(h.AppliedDamage, blow, .01f)) &&
                            all.Except(inHaze).All(e => e.Hp == hp[System.Array.IndexOf(all, e)]),
                            "the instant blow (wave power x .3 = " + blow + ") lands once on every enemy in the haze and on nobody else (" + inHaze.Length + " in)");
                        Tick(hazeFx, now, 0f);
                        r.Check("R128-steam", Of(HazeZoneEffect308.HandlerId).Length == 0 && hazeFx.VeiledEnemies == 0, "the haze's owner sweeps it: nobody keeps its blur");

                        // depth 2: the steam (water) + an earth wave -> mud
                        f.Ink.Restore(); f.ClearLog(); f.Land(); f.ClearLog();
                        f.Cast(earthPath);
                        var mire = Of(ReactionZoneEffect308.HandlerId).Where(z => z.Element == Element.Earth).ToArray();
                        r.Check("R128-mud", f.Accepted.Count == 1 && mire.Length == 1 && mire[0].Depth == 2 && Near(mire[0].Value, mud.Slow) && mire[0].TickEvery == 0f &&
                            Near(mire[0].Until - mire[0].From, Mathf.Min(veil.Until - now, mud.LifeCap), room),
                            "the earth wave turns the steam into mud (depth 2, movement x" + mud.Slow + ", never longer than the steam it came from)");
                        r.Check("R128-mud", fx.Reacts.Count > 0 && fx.Reacts.Last().Kind == ReactionKind308.Mud, "the mud look is told by its own kind (the presenter has no prefab for it yet and says so once)");
                        if (mire.Length == 1)
                        {
                            float at = now + .1f;
                            Tick(reactFx, at, .1f);
                            var inMud = all.Where(e => e.IsAlive && SpellZoneRule308.Inside(mire[0], e.transform.position)).ToArray();
                            r.Check("R128-mud", inMud.Length >= 1 && inMud.All(e => Near(e.Control.MovementScale(at), mud.Slow)) && all.Except(inMud).All(e => e.Control.MovementScale(at) == 1f),
                                "enemies in the mud move at x" + mud.Slow + ", the others at full speed");
                            f.Ink.Restore(); int before = Of(ReactionZoneEffect308.HandlerId).Length; f.Cast(earthPath);
                            r.Check("R128-mud", Of(ReactionZoneEffect308.HandlerId).Length == before, "depth 2 is the end: a mud reacts no more");
                            f.Wiring.ClearSpells308(SpellClearReason.PlayerDied);
                            r.Check("R128-mud", Of(ReactionZoneEffect308.HandlerId).Length == 0 && all.All(e => e.Control.MovementScale(at) == 1f), "death drops every product and its slow");
                        }
                    }
                }

                // ================= 녹's heal zone is material too =================
                {
                    Reset(); f.Aim(f.Inside[2]);
                    int heal = Material("cone.healzone", Element.Fire, f.Inside[2].transform.position, 2f, .02f);
                    f.Cast(waterPath);
                    r.Check("R128-steam", zones.Zones.First(z => z.Id == heal).Reacted && Of(ReactionZoneEffect308.HandlerId).Length == 1,
                        "the water wave turns 녹's heal zone into steam as well (a lasting gift traded for a blow)");
                }

                // ================= what never reacts =================
                {
                    Reset(); f.Aim(f.Inside[1]);
                    int under = Material(VineZoneEffect308.HandlerId, Element.Wood, home + Vector3.forward * .5f, 3.5f, .5f);
                    f.Cast(fireCone);
                    r.Check("R128-none", !zones.Zones.First(z => z.Id == under).Reacted && Of(ReactionZoneEffect308.HandlerId).Length == 0,
                        "vines the caster stands in do not burn (burn them from outside)");

                    Reset(); f.Aim(f.Inside[2]);
                    var domain = new SpellZone308
                    {
                        Owner = "domain.test", Cast = zones.NextCast(), Element = Element.Fire, Centre = f.Inside[2].transform.position, Radius = 3f,
                        From = now - 1f, Until = now + 7f, Life = 8f,
                    };
                    int ward = zones.Add(domain, 1, now, null);
                    f.Cast(waterPath);
                    r.Check("R128-none", !zones.Zones.First(z => z.Id == ward).Reacted && Of(ReactionZoneEffect308.HandlerId).Length == 0,
                        "a domain is never material, even where the wave passes");

                    Reset(); f.Aim(f.Inside[1]);
                    int vines = Material(VineZoneEffect308.HandlerId, Element.Wood, f.Inside[1].transform.position, 3.5f, .5f);
                    f.Cast(waterPath);
                    r.Check("R128-none", !zones.Zones.First(z => z.Id == vines).Reacted && Of(ReactionZoneEffect308.HandlerId).Length == 0,
                        "a pair outside the table (water on wood) does nothing");

                    Reset(); f.Aim(f.Inside[1]);
                    vines = Material(VineZoneEffect308.HandlerId, Element.Wood, f.Inside[1].transform.position, 3.5f, .5f);
                    reactFx.UseTable(System.Array.Empty<ReactionRow308>());
                    f.Cast(fireCone);
                    bool nothing = !zones.Zones.First(z => z.Id == vines).Reacted && Of(ReactionZoneEffect308.HandlerId).Length == 0;
                    reactFx.UseTable(rows);
                    r.Check("R128-none", nothing, "without a table nothing reacts (the game reads it from Resources: no copy, no reaction)");

                    Reset(); f.Aim(f.Inside[1]); f.Ink.Restore();
                    vines = Material(VineZoneEffect308.HandlerId, Element.Wood, f.Inside[1].transform.position, 3.5f, .5f);
                    // no ink: the cast is refused before its accept, so it never reached the ground
                    float ink = f.Ink.Value; f.Ink.TrySpend(ink);
                    f.Cast(fireCone);
                    r.Check("R128-none", f.Accepted.Count == 0 && !zones.Zones.First(z => z.Id == vines).Reacted && Of(ReactionZoneEffect308.HandlerId).Length == 0,
                        "a cast that was not accepted (no ink) reacts with nothing");
                }
                Reset();
            }

            // ================= the same cast never reacts with what it laid (the dispatch window at hook time) =================
            // No real handler lays a zone that is already formed when its own dispatch ends, so a probe does: it lays a fire zone
            // that is active at once on the aimed enemy and plans a single shot there. A fire + fire row (outside the real table)
            // would make it react with itself if the cast's window did not hold its token.
            {
                char probeCell = SpellGrammar308.LetterAt(27);
                var probeZones = new SpellZoneRuntime308();
                var probe = new R128SameCastProbe(probeZones);
                var same = new ReactionZoneEffect308(probeZones);
                same.UseTable(new[] { new ReactionRow308 { Id = "same", Material = Element.Fire, Trigger = Element.Fire, Product = Element.Fire, Kind = ReactionProductKind308.None, RadiusMul = 1f, LifeMode = ReactionLifeMode308.Fixed, LifeCap = 1f } });
                var sheet = new SpellSheet308("Rules308_ZZFixture", RuleHeader + probeCell + ",,zone.probe,,AttackSingle,,,,10,,,,,,,,,,,\n");
                using (var f = NewFixture(r, new ISpellEffect[] { probe, same }, R128Sheets, sheet))
                {
                    f.Finals.Open.Add(SpellFinal.Nieun);
                    f.Aim(f.Inside[1]);
                    f.Cast(probeCell);
                    var first = probeZones.Zones.Where(z => z.Owner == R128SameCastProbe.HandlerId).ToArray();
                    r.Check("R128-same", f.Misfires.Count == 0 && f.Accepted.Count == 1 && first.Length == 1 && !first[0].Reacted && probeZones.CountOf(ReactionZoneEffect308.HandlerId) == 0,
                        "a cast whose own zone is formed and under its own ground does not react with it (its token is in the cast's window)");
                    f.Ink.Restore(); f.Cast(probeCell);
                    var both = probeZones.Zones.Where(z => z.Owner == R128SameCastProbe.HandlerId).OrderBy(z => z.Id).ToArray();
                    r.Check("R128-same", both.Length == 2 && both[0].Reacted && !both[1].Reacted && probeZones.CountOf(ReactionZoneEffect308.HandlerId) == 1,
                        "the next cast does react with the zone the first one laid, and not with its own");
                }
            }
        }

        // A handler for the check above only: lays a fire zone that is active at once, plans a single shot on the aimed enemy.
        sealed class R128SameCastProbe : ZoneEffect308
        {
            public const string HandlerId = "zone.probe";
            public R128SameCastProbe(SpellZoneRuntime308 zones) : base(zones) { }
            public override string Id => HandlerId;
            public override IReadOnlyList<SpellParamSpec> Params => System.Array.Empty<SpellParamSpec>();
            public override bool Commit(in SpellCastContext ctx)
            {
                var target = ctx.Host.AimedTarget();
                if (target == null || ctx.Host.PlanSingle(ctx.Cast, false) == null) return false;
                var zone = new SpellZone308
                {
                    Owner = HandlerId, Cast = Zones.NextCast(), Element = ctx.Cast.Element, Centre = target.transform.position, Radius = 2f,
                    From = ctx.Now, Until = ctx.Now + 5f, Life = 5f,
                    Token = AttackProvenance.Create(Instigator(ctx.Host), DamageSource.PersistentSpell, ctx.Cast.Element).AttackId,
                };
                return Place(zone, 2, ctx.Now, ctx.Fx) != 0;
            }
            protected override void Act(in SpellTickContext ctx) { }
        }
    }
}
