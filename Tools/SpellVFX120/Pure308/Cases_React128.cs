// #308 D308-128 B / D308-132: five-element reactions between the player's lingering zones and a later spell. Rule arithmetic
// only (the table, material, footprints, products, the zone list's React). The wiring (dispatch hook, damage source, cleanup
// of a consumed zone's slow / veil) is covered by the edit-mode fixtures (Spell120Checks308.R128.cs).
using System.Collections.Generic;
using System.IO;
using System.Text;
using Oheangbu.App;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Pure308
{
    public static partial class Program
    {
        static partial void RunReact128(Report r, Context c)
        {
            string path = Path.Combine(c.SheetsDir, ReactionTable308.FileName);
            if (!r.Check("R128", File.Exists(path), "Reactions308.csv sits next to the row sheets", path)) return;
            var errors = new List<string>();
            var rows = ReactionTable308.Parse(File.ReadAllText(path, Encoding.UTF8), errors);
            r.Check("R128", errors.Count == 0 && rows.Count == 3, "the live table parses: three rows, no error", string.Join(" | ", errors));
            var wild = ReactionRule308.Find(rows, Element.Wood, Element.Fire);
            var mud = ReactionRule308.Find(rows, Element.Water, Element.Earth);
            var steam = ReactionRule308.Find(rows, Element.Fire, Element.Water);
            if (!r.Check("R128", wild != null && mud != null && steam != null, "목생화 · 토극수 · 수극화 are the three rows")) return;
            r.Check("R128", ReactionRule308.Find(rows, Element.Fire, Element.Wood) == null && ReactionRule308.Find(rows, Element.Metal, Element.Fire) == null,
                "a pair is one way only (fire on wood reacts, wood on fire does not); metal has no row");
            r.Check("R128", wild.Generates && !mud.Generates && !steam.Generates && wild.Product == Element.Fire && mud.Product == Element.Earth && steam.Product == Element.Water,
                "relations and product elements");
            r.Check("R128", wild.Kind == ReactionProductKind308.Damage && mud.Kind == ReactionProductKind308.Slow && steam.Kind == ReactionProductKind308.None,
                "product kinds: wildfire hurts, mud slows, steam has no rule");

            // ---- the parser refuses what it must ----
            {
                const string H = "Id,Material,Trigger,Relation,Product,Kind,RadiusMul,LifeMode,LifeShare,LifeCap,PerSecond,HitShare,Tick,Slow\n";
                var e = new List<string>();
                ReactionTable308.Parse(H + "x,Fire,Wood,Generates,Fire,Damage,1,Extend,0,0,0,0,0.5,1\n", e);
                r.Check("R128", e.Count == 1, "a Generates row that is not 상생 is refused", string.Join(" | ", e));
                e.Clear(); ReactionTable308.Parse(H + "x,Wood,Fire,Generates,Fire,Damage,1,Extend,0,0,0,0,0,1\n", e);
                r.Check("R128", e.Count == 1, "a Damage row without a Tick is refused");
                e.Clear(); ReactionTable308.Parse(H + "a,Wood,Fire,Generates,Fire,Damage,1,Extend,0,0,0,0,0.5,1\nb,Wood,Fire,Generates,Fire,None,1,Fixed,0,1,0,0,0,1\n", e);
                r.Check("R128", e.Count == 1, "a second row for the same pair is refused");
                e.Clear(); ReactionTable308.Parse(H + "x,Wood,Fire,Generates,Fire,Damage,-1,Extend,0,0,0,0,0.5,1\n", e);
                r.Check("R128", e.Count == 1, "a negative number is refused");
                e.Clear(); ReactionTable308.Parse("Id,Material\n", e);
                r.Check("R128", e.Count == 1, "a wrong header is refused");
            }

            // ---- material ----
            var centre = new Vector3(10f, 0f, 10f);
            SpellZone308 Z(string owner, Element element, float radius = 3.5f, float from = 0f, float life = 8f, int depth = 0, long token = 0)
                => new SpellZone308 { Owner = owner, Element = element, Centre = centre, Radius = radius, From = from, Until = from + life, Life = life, Depth = depth, Token = token };
            var far = Vector3.zero;
            r.Check("R128", ReactionRule308.Eligible(Z("zone.slow", Element.Wood), 1f, far, null) && ReactionRule308.Eligible(Z("cone.healzone", Element.Fire), 1f, far, null)
                && ReactionRule308.Eligible(Z("zone.react", Element.Water, depth: 1), 1f, far, null),
                "zone.* (products included) and 녹's heal zone are material");
            r.Check("R128", !ReactionRule308.Eligible(Z("domain.fire", Element.Fire), 1f, far, null) && !ReactionRule308.Eligible(Z("cast.other", Element.Fire), 1f, far, null),
                "a domain is never material (the player's own defence under the feet)");
            r.Check("R128", !ReactionRule308.Eligible(Z("zone.slow", Element.Wood), 1f, centre + Vector3.right, null),
                "the zone the caster stands in is not material (burn your vines from outside)");
            r.Check("R128", !ReactionRule308.Eligible(Z("zone.slow", Element.Wood, from: 5f), 1f, far, null) && !ReactionRule308.Eligible(Z("zone.slow", Element.Wood, life: 2f), 3f, far, null),
                "an inactive zone (not yet / ran out) is not material");
            r.Check("R128", ReactionRule308.Eligible(Z("zone.react", Element.Water, depth: 1), 1f, far, null) && !ReactionRule308.Eligible(Z("zone.react", Element.Earth, depth: 2), 1f, far, null),
                "depth: a first product may react once more, a second product never (MaxDepth 2)");
            var window = new SpellAttackWindow308(100);
            r.Check("R128", !ReactionRule308.Eligible(Z("zone.slow", Element.Wood, token: 101), 1f, far, window) && ReactionRule308.Eligible(Z("zone.slow", Element.Wood, token: 99), 1f, far, window),
                "a zone the same cast laid (its token in the cast's open window) is not material: no reaction inside one cast");
            var reacted = Z("zone.slow", Element.Wood); reacted.Reacted = true;
            r.Check("R128", !ReactionRule308.Eligible(reacted, 1f, far, null), "a consumed zone reacts no more");

            // ---- footprints (edge contact counts) ----
            {
                var zone = Z("zone.slow", Element.Wood, radius: 2f);
                r.Check("R128", ReactionRule308.Overlaps(ReactionFootprint308.Point(centre + Vector3.right * 2f), zone) && !ReactionRule308.Overlaps(ReactionFootprint308.Point(centre + Vector3.right * 2.01f), zone),
                    "point: inside up to the rim");
                r.Check("R128", ReactionRule308.Overlaps(ReactionFootprint308.Circle(centre + Vector3.right * 4.5f, 2.5f), zone) && !ReactionRule308.Overlaps(ReactionFootprint308.Circle(centre + Vector3.right * 4.6f, 2.5f), zone),
                    "circle: radii add");
                var start = centre + Vector3.back * 10f;
                r.Check("R128", ReactionRule308.Overlaps(ReactionFootprint308.Corridor(start + Vector3.right * 3.5f, Vector3.forward, 1.5f, 14f), zone)
                    && !ReactionRule308.Overlaps(ReactionFootprint308.Corridor(start + Vector3.right * 3.6f, Vector3.forward, 1.5f, 14f), zone),
                    "corridor: half width + radius to the side");
                r.Check("R128", ReactionRule308.Overlaps(ReactionFootprint308.Corridor(start, Vector3.forward, 1.5f, 6.5f), zone) && !ReactionRule308.Overlaps(ReactionFootprint308.Corridor(start, Vector3.forward, 1.5f, 6.4f), zone),
                    "corridor: a wave that stops short of the circle does not touch it (the end is round)");
                r.Check("R128", ReactionRule308.Overlaps(ReactionFootprint308.Cone(centre + Vector3.back * 7f, Vector3.forward, 30f, 5f), zone)
                    && !ReactionRule308.Overlaps(ReactionFootprint308.Cone(centre + Vector3.back * 7.1f, Vector3.forward, 30f, 5f), zone),
                    "cone: the arc reaches the near rim");
                r.Check("R128", ReactionRule308.Overlaps(ReactionFootprint308.Cone(centre + Vector3.left * 3.5f + Vector3.back * 3f, Vector3.forward, 30f, 8f), zone)
                    && !ReactionRule308.Overlaps(ReactionFootprint308.Cone(centre + Vector3.left * 5f + Vector3.back * 3f, Vector3.forward, 30f, 8f), zone),
                    "cone: a circle beside the cone touches its edge (outside the angle, within the radius of the edge)");
                r.Check("R128", !ReactionRule308.Overlaps(ReactionFootprint308.Cone(centre + Vector3.back * 4f, Vector3.back, 30f, 8f), zone) && ReactionRule308.Overlaps(ReactionFootprint308.Cone(centre + Vector3.back * 1.9f, Vector3.back, 30f, 8f), zone),
                    "cone: pointing away misses unless the apex is inside");
                r.Check("R128", !ReactionRule308.Overlaps(default, zone), "no footprint (a volley, an air single cast) touches nothing");
            }

            // ---- products ----
            {
                var vines = Z("zone.slow", Element.Wood, radius: 3.5f, from: 0f, life: 8f);
                var fire = ReactionRule308.Product(wild, vines, 6f, 20f, 7);
                r.Check("R128", r.Near(fire.Radius, 5.25f) && r.Near(fire.Life, 3f) && r.Near(fire.Until, 9f) && fire.From == 6f && fire.Owner == ReactionRule308.ProductOwner && fire.Element == Element.Fire && fire.Depth == 1 && fire.Cast == 7,
                    "wildfire: radius x1.5 (3.5 -> 5.25), remaining 2 s -> 3 s, from the moment of the reaction", fire.Radius + " / " + fire.Life);
                var early = ReactionRule308.Product(wild, vines, 0f, 20f, 7);
                r.Check("R128", r.Near(early.Life, 11f), "wildfire: remaining 8 s -> 11 s (the +3 s cap)", early.Life.ToString());
                r.Check("R128", r.Near(fire.TickEvery, .5f) && r.Near(fire.Value, 20f * .25f * .5f), "wildfire step = follow-up power x .25 per second x .5 s", fire.Value.ToString());
                r.Check("R128", ReactionRule308.Product(wild, vines, 6f, 0f, 7).Value == 0f, "a follow-up without power leaves a wildfire that hurts nobody");
                var pool = Z("zone.react", Element.Water, radius: 3f, from: 0f, life: 8f, depth: 1);
                var mire = ReactionRule308.Product(mud, pool, 2f, 30f, 8);
                r.Check("R128", r.Near(mire.Life, 3f) && r.Near(mire.Value, .65f) && mire.TickEvery == 0f && r.Near(mire.Radius, 3f) && mire.Depth == 2,
                    "mud: life capped at 3 s, movement x.65, no steps, depth 2");
                r.Check("R128", r.Near(ReactionRule308.Product(mud, pool, 7.5f, 30f, 8).Life, .5f), "mud: never outlives its material");
                var haze = Z("zone.haze", Element.Fire, radius: 3f, from: 0f, life: 8f);
                var veil = ReactionRule308.Product(steam, haze, 7.9f, 30f, 9);
                r.Check("R128", r.Near(veil.Life, 2f) && veil.Value == 0f && veil.TickEvery == 0f && veil.Element == Element.Water,
                    "steam: fixed 2 s, no rule, water (the material of mud)");
                r.Check("R128", r.Near(ReactionRule308.HitPower(steam, 30f), 9f) && ReactionRule308.HitPower(steam, 0f) == 0f && ReactionRule308.HitPower(wild, 30f) == 0f,
                    "steam's instant blow = follow-up power x .3; none without power; wildfire has none");
            }

            // ---- the zone list ----
            {
                var zones = new SpellZoneRuntime308();
                var removed = new List<SpellZone308>();
                var stepping = Z("zone.spikes", Element.Wood); stepping.TickEvery = .5f;
                int id = zones.Add(stepping, 1, 0f, removed);
                r.Check("R128", zones.React(id, 1f) && !zones.React(id, 1f) && !zones.React(999, 1f), "React marks a live zone once; a second reaction or an unknown id gets nothing");
                var after = zones.Zones[0];
                r.Check("R128", after.Reacted && !SpellZoneRule308.Active(after, 1f) && SpellZoneRule308.Ended(after, 1f) && zones.Ticks(id, 3f) == 0 && SpellZoneRule308.ActiveSeconds(after, 0f, 5f) == 0f,
                    "a consumed zone stops at once: not active, ended, no more steps or seconds");
                r.Check("R128", !zones.Covered(centre, 1f), "a consumed zone covers nothing");
                zones.Sweep("zone.spikes", 1f, removed);
                r.Check("R128", zones.Count == 0 && removed.Count == 1 && removed[0].End == SpellZoneEnd.Reacted, "its owner sweeps it as Reacted (and so takes back what it applied)");
                var expired = Z("zone.slow", Element.Wood, life: 1f);
                int old = zones.Add(expired, 1, 0f, removed);
                r.Check("R128", !zones.React(old, 2f), "a zone that already ran out cannot be consumed");
                var reAdd = Z("zone.react", Element.Fire); reAdd.Reacted = true;
                zones.Add(reAdd, 3, 0f, removed);
                r.Check("R128", !zones.Zones[zones.Count - 1].Reacted, "Add clears the mark");
                int a = zones.Add(Z(ReactionRule308.ProductOwner, Element.Fire), 3, 0f, removed);
                int b = zones.Add(Z(ReactionRule308.ProductOwner, Element.Water), 3, 0f, removed);
                r.Check("R128", a > 0 && b > 0 && zones.CountOf(ReactionRule308.ProductOwner) == 3, "the product owner holds three at once (two reactions of one frame do not push each other out)");
            }
        }
    }
}
