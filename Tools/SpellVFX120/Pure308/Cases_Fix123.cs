// #308 D308-123 (2026-10-21, debate decision Codex · Fable · Claude, TEST): the fixes of the six accompanying rules that
// can run without Unity.
//  - jantan (companion shots): the shots carry the row power times the brush of the accompanied cast, once. The brush of
//    the buff's own cast is never kept, so the two are never multiplied together. A brush that is not usable (NaN, 0,
//    below 0, infinite) counts as 1.
// What stays for the editor fixtures: the needle's weaker fallback shot, the standing barrage, the icicle's extra damage
// outside the five elements (Spell120Checks308.WP13 / WP05).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App;
using UnityEngine;

namespace Pure308
{
    public static partial class Program
    {
        static partial void RunFix123(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("F123", false, "D308-123 cases need the table"); return; }
            var rows = c.Build.Rows.Where(x => x.Handler == "buff.companion").ToArray();
            if (!r.Check("F123", rows.Length > 0, "the companion rows are in the table")) return;

            // ---- jantan: the brush of the accompanied cast, once ----
            {
                var actors = WP09Actors(); Vector3 from = Vector3.zero;
                var companion = WP09Companion(rows[0]);
                float duration = rows[0].F("duration", 0f);
                List<CompanionShotRule308.Shot> Shots(float? brush)
                {
                    var state = new CompanionShotRule308.State(); var timers = new SpellBuffState308(); var shots = new List<CompanionShotRule308.Shot>();
                    state.Activate(timers, companion, 5f, duration);
                    if (brush.HasValue) state.Accompany(timers, actors, 0, from, 6f, shots, brush.Value);
                    else state.Accompany(timers, actors, 0, from, 6f, shots);
                    return shots;
                }
                var plain = Shots(null); var strong = Shots(1.5f); var weak = Shots(.6f);
                r.Check("잔탄-1", plain.Count > 0 && plain.All(s => r.Near(s.Power, companion.Power)),
                    "without a brush the shots carry the row power", plain.Count > 0 ? plain[0].Power.ToString() : "-");
                r.Check("잔탄-1", strong.Count == plain.Count && strong.All(s => r.Near(s.Power, companion.Power * 1.5f)) && weak.All(s => r.Near(s.Power, companion.Power * .6f)),
                    "each shot carries the row power x the brush of the accompanied cast (x1.5 and x0.6)");
                r.Check("잔탄-1", strong.Select(s => s.At).SequenceEqual(plain.Select(s => s.At)) && strong.Select(s => s.TargetId).SequenceEqual(plain.Select(s => s.TargetId)),
                    "the brush changes the power only: the same shots at the same times on the same target");
                bool unusable = new[] { float.NaN, 0f, -1f, float.PositiveInfinity }.All(b => Shots(b).All(s => r.Near(s.Power, companion.Power)));
                r.Check("잔탄-1", unusable, "a brush of NaN, 0, below 0 or infinite counts as 1");
            }

            // ---- static: the effect passes the accompanied cast's brush, and only that one ----
            {
                string scripts = Path.Combine(c.Root, "Oheangbu", "Assets", "_Project", "Scripts", "App");
                string Read(string name) { string p = Path.Combine(scripts, name); return File.Exists(p) ? File.ReadAllText(p) : ""; }
                string effect = Read("CompanionShotEffect308.cs"), rule = Read("CompanionShotRule308.cs");
                r.Check("잔탄-2", effect.Contains("ctx.Now, _shots, ctx.Cast.Brush);") && rule.Contains("companion.Power * brush"),
                    "the accompanying call passes ctx.Cast.Brush and the rule multiplies it into the shot power once");
            }
        }
    }
}