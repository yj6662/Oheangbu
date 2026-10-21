// #308 D308-123 (2026-10-21): the swift buff (buff.swift) speeds up every real player projectile, not only the single shot.
// The cast hook covers the casts whose speed is SpellCast.SpeedMul (single shots, the giyeok trace, and now the volley);
// shots whose speed is their own row number (volley bounces, droplets, companion shots) read SpellBuffState308.SpeedScale at
// launch. The mieum-final ballistic shell keeps its fall point and only lands sooner. Install marks, summons and the cast
// interval are not projectiles of it.
// What the runner compares: the shared scale (none, one, two buffs, expiry, recast, clear), the companion landing time with
// and without the buff, the bounce and droplet landing times at the scaled speed, the shell's flight, and (static) the call
// sites that must read the scale.
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Pure308
{
    public static partial class Program
    {
        static partial void RunSpeed123(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("S123", false, "D308-123 cases need the table"); return; }
            var swift = c.Build.Rows.SingleOrDefault(x => x.Handler == "buff.swift" && x.Feature == SpellLegacyFeature.None);
            if (!r.Check("S123", swift != null, "the swift row (buff.swift) is in the table")) return;
            float mul = swift.F("speed.mul", 0f);
            r.Check("S123", mul > 1f, "the swift row speeds projectiles up (speed.mul above 1)", mul.ToString(CultureInfo.InvariantCulture));
            char letter = swift.Letter[0];

            // ---- the shared scale ----
            {
                var buffs = new SpellBuffState308();
                r.Check("S123-1", buffs.SpeedScale(0f) == 1f, "no buff running: scale 1");
                buffs.Activate('가', 0f, 30f);
                r.Check("S123-1", buffs.SpeedScale(1f) == 1f, "a buff without a speed scale leaves it at 1");
                buffs.Activate(letter, 0f, 10f, 0, mul);
                r.Check("S123-1", r.Near(buffs.SpeedScale(1f), mul), "the swift buff running: scale = its speed.mul", buffs.SpeedScale(1f).ToString(CultureInfo.InvariantCulture));
                buffs.Activate('나', 0f, 5f, 0, 2f);
                r.Check("S123-1", r.Near(buffs.SpeedScale(1f), mul * 2f) && r.Near(buffs.SpeedScale(6f), mul),
                    "two speed buffs multiply; each stops counting when its own time is over");
                buffs.Activate(letter, 8f, 10f, 0, mul);
                r.Check("S123-1", r.Near(buffs.SpeedScale(9f), mul) && r.Near(buffs.SpeedScale(17f), mul) && buffs.SpeedScale(18f) == 1f,
                    "a recast replaces the swift time and does not compound the scale");
                var odd = new SpellBuffState308();
                odd.Activate('다', 0f, 10f, 0, 0f); odd.Activate('라', 0f, 10f, 0, -3f); odd.Activate('마', 0f, 10f, 0, float.NaN);
                r.Check("S123-1", odd.SpeedScale(1f) == 1f, "a scale of 0, below 0 or NaN counts as 1 (never stops or reverses a shot)");
                buffs.Clear();
                r.Check("S123-1", buffs.SpeedScale(9f) == 1f, "a clear (death, rest, scene leave) ends the scale with the buffs");
            }

            // ---- companion shots: distance / (row speed x scale) ----
            {
                var rows = c.Build.Rows.Where(x => x.Handler == "buff.companion").ToArray();
                if (r.Check("S123-2", rows.Length > 0, "the companion rows are in the table"))
                {
                    var actors = WP09Actors(); Vector3 from = Vector3.zero;
                    var companion = WP09Companion(rows[0]);
                    float duration = rows[0].F("duration", 0f), distance = Vector3.Distance(from, actors[0].Position);
                    var state = new CompanionShotRule308.State(); var timers = new SpellBuffState308();
                    var plain = new List<CompanionShotRule308.Shot>(); var fast = new List<CompanionShotRule308.Shot>();
                    state.Activate(timers, companion, 5f, duration);
                    state.Accompany(timers, actors, 0, from, 6f, plain);
                    timers.Activate(letter, 6f, 10f, 0, mul);
                    state.Accompany(timers, actors, 0, from, 6f, fast);
                    float interval = companion.Interval > 0f ? companion.Interval : 0f;
                    r.Check("S123-2", plain.Count > 0 && plain.Count == fast.Count &&
                        plain.Select((s, i) => r.Near(s.At, 6f + i * interval + distance / companion.Speed)).All(x => x) &&
                        fast.Select((s, i) => r.Near(s.At, 6f + i * interval + distance / (companion.Speed * mul))).All(x => x),
                        "a companion shot launched while swift runs flies at row speed x speed.mul (same shots, earlier landing)",
                        string.Join(",", plain.Select(s => s.At.ToString("0.###", CultureInfo.InvariantCulture))) + " / " +
                        string.Join(",", fast.Select(s => s.At.ToString("0.###", CultureInfo.InvariantCulture))));
                    r.Check("S123-2", plain.Zip(fast, (a, b) => a.Power == b.Power && a.Element == b.Element && a.TargetId == b.TargetId).All(x => x),
                        "the swift buff changes the landing time only: power, element and target stay");
                }
            }

            // ---- bounces and droplets: the rule lands at distance / (row speed x scale) ----
            {
                var droplets = c.Build.Rows.FirstOrDefault(x => x.Handler == "hit.droplets");
                var ricochet = c.Build.Rows.FirstOrDefault(x => x.Handler == "volley.ricochet");
                if (r.Check("S123-3", droplets != null && ricochet != null, "the droplet and ricochet rows are in the table"))
                {
                    var point = new Vector3(1f, 0f, 5f); float now = 4f;
                    float dropSpeed = droplets.F("drop.speed", 0f), radius = droplets.F("drop.radius", 0f);
                    var crowd = new[] { WP04Actor(0, point), WP04Actor(1, WP04Polar(point, 10f, radius * .5f)) };
                    var scratch = new List<int>(); var slow = new List<SpellHitOrder>(); var quick = new List<SpellHitOrder>();
                    DropletRule308.Plan(crowd, 0, point, 1, radius, dropSpeed, now, 1f, droplets.Element, scratch, slow);
                    DropletRule308.Plan(crowd, 0, point, 1, radius, dropSpeed * mul, now, 1f, droplets.Element, scratch, quick);
                    float d = WP04Distance(point, crowd[1].Position);
                    r.Check("S123-3", slow.Count == 1 && quick.Count == 1 && r.Near(slow[0].At, now + d / dropSpeed) && r.Near(quick[0].At, now + d / (dropSpeed * mul)) &&
                        quick[0].TargetId == slow[0].TargetId && r.Near(quick[0].Power, slow[0].Power),
                        "a droplet at the swift-scaled speed chases the same enemy with the same power and lands sooner");

                    float bounceSpeed = ricochet.F("bounce.speed", 0f), range = ricochet.F("bounce.range", 0f);
                    var hit = new Vector3(0f, 0f, 8f);
                    var pair = new[] { WP04Actor(0, hit), WP04Actor(1, WP04Polar(hit, 40f, range * .5f)) };
                    var visited = new List<int> { 0 };
                    bool a = RicochetRule308.Bounce(pair, hit, visited, 0, 1, 1, range, bounceSpeed, now, 2f, .5f, ricochet.Element, scratch, out SpellHitOrder plainBounce);
                    bool b = RicochetRule308.Bounce(pair, hit, visited, 0, 1, 1, range, bounceSpeed * mul, now, 2f, .5f, ricochet.Element, scratch, out SpellHitOrder fastBounce);
                    float e = WP04Distance(hit, pair[1].Position);
                    r.Check("S123-3", a && b && fastBounce.TargetId == plainBounce.TargetId && r.Near(plainBounce.At, now + e / bounceSpeed) &&
                        r.Near(fastBounce.At, now + e / (bounceSpeed * mul)) && r.Near(fastBounce.Power, plainBounce.Power),
                        "a bounce at the swift-scaled speed goes to the same enemy with the same power and lands sooner");
                }
            }

            // ---- the mieum-final shell: same fall point, shorter flight ----
            {
                var spot = new Vector3(0f, 0f, 18f); float speed = 18f, fall = 1f;
                float flight = BallisticRule308.FlightSeconds(Vector3.zero, spot, speed), quick = BallisticRule308.FlightSeconds(Vector3.zero, spot, speed * mul);
                // a target walking straight out of the fall circle: at this pace it escapes the plain shell but not the swift one
                float walk = fall / ((flight + quick) * .5f);
                bool plainLands = BallisticRule308.Lands(spot, spot + new Vector3(walk * flight, 0f, 0f), fall);
                bool swiftLands = BallisticRule308.Lands(spot, spot + new Vector3(walk * quick, 0f, 0f), fall);
                r.Check("S123-4", r.Near(quick, flight / mul) && quick < flight && !plainLands && swiftLands,
                    "the shell keeps the fall point fixed at the cast; at swift speed it only lands sooner (a target walking out is caught)",
                    flight.ToString("0.###", CultureInfo.InvariantCulture) + " -> " + quick.ToString("0.###", CultureInfo.InvariantCulture));
            }

            // ---- the volley reads the cast speed; every volley row is 1 of its own, so nothing changes without the buff ----
            {
                var volleys = c.Build.Rows.Where(x => x.VolleyShots > 1).ToArray();
                var odd = volleys.Where(x => !(x.ProjectileSpeedMul == 0f || r.Near(x.ProjectileSpeedMul, 1f))).ToArray();
                r.Check("S123-5", volleys.Length > 0 && odd.Length == 0,
                    "every volley row has no speed multiplier of its own (empty or 1): the volley flies at its area speed unless swift runs",
                    volleys.Length + " rows; off: " + string.Join(",", odd.Select(x => x.Letter + "=" + x.ProjectileSpeedMul.ToString(CultureInfo.InvariantCulture))));
            }

            // ---- static: the call sites that must read the scale ----
            {
                string scripts = Path.Combine(c.Root, "Oheangbu", "Assets", "_Project", "Scripts", "App");
                string Read(string name) { string p = Path.Combine(scripts, name); return File.Exists(p) ? File.ReadAllText(p) : ""; }
                string ricochetSrc = Read("RicochetEffect308.cs"), dropletSrc = Read("DropletEffect308.cs"), companionSrc = Read("CompanionShotRule308.cs"),
                    swiftSrc = Read("SwiftEffect308.cs"), buffSrc = Read("SelfBuffEffect308.cs"), wiringSrc = Read("CombatLoopWiring.cs");
                r.Check("S123-6", ricochetSrc.Contains("row.F(\"bounce.speed\", 0f) * (ctx.Host.Buffs != null ? ctx.Host.Buffs.SpeedScale(ctx.Now) : 1f)") &&
                    dropletSrc.Contains("watch.Row.F(\"drop.speed\", 0f) * (ctx.Host.Buffs != null ? ctx.Host.Buffs.SpeedScale(ctx.Now) : 1f)") &&
                    companionSrc.Contains("distance / (companion.Speed * scale)") && companionSrc.Contains("timers.SpeedScale(now)"),
                    "the bounce, droplet and companion launches multiply their row speed by the shared scale");
                r.Check("S123-6", swiftSrc.Contains("protected override float SpeedScale => _mul;") && buffSrc.Contains("Buffs.Activate(Letter, ctx.Now, duration, Charges, SpeedScale);"),
                    "the swift buff hands its speed.mul to the shared timers when it starts");
                r.Check("S123-6", wiringSrc.Contains("(cast.Area.Speed > 0f ? cast.Area.Speed : 32f) * cast.SpeedMul"),
                    "the volley plan flies at its area speed x the cast's speed multiplier (the swift cast hook)");
            }
        }
    }
}
