using System;
using System.Globalization;
using System.Linq;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.Spell;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 forms2 (Tools/Unity/Stage308_forms2/DESIGN.md 5-3): the new forms in the same in-memory preview. Same rules as the
    // rest of DeployLook308: nothing is saved, every object is DontSave under the preview root, preview:off removes it.
    // Queue: Oheangbu.EditorTools.WorldMacro.DeployLook308 Run "<command>"
    //   preview:on:거:guard:phase=window|block|success|half|blockhit|fail|end[:hold=0.1][:hitage=s][:window=s][:life=s]
    //   preview:on:구:fence:phase=rise|stand|drain[:crowd=6][:radius=3][:life=6]
    //   preview:on:곰:emerge:phase=rise|reveal|exit[:dist=4][:age=s]
    //   preview:on:걱:buffcast | preview:on:걱:buffwalk:steps=6 | preview:on:걱:buffend[:age=s] | preview:on:넉:aura:age=3[:radius=2]
    //   preview:on:국:fieldmark:phase=rise|held|dry
    //   preview:on:감:loose
    //   preview:on:가:comet:u=0.1|0.5|0.9[:view=10][:arc=m][:curve=m][:ease=1][:flight=s]
    // Common options as before: :tier=pc|mobile :t=<s> :seed= :at= :yaw= :grade= :allowdirty.
    //   :dummy=<m>[:dummyh=<m>][:dummyw=<m>]   a plain box standing <m> ahead as an enemy stand-in (default 1.7 m tall; dummyh=0.7 = a
    //                                          four-legged beast): does a form cover it? No collider, removed with the preview.
    // The preview shows the TARGET state: the letter's row is read as Retire where the form needs it, whatever the map asset
    // says today (forms308-map changes the asset). The eye stands where the player's would (1.6 m).
    public static partial class DeployLook308
    {
        static readonly string[] Forms2Beats = { "guard", "fence", "emerge", "buffcast", "buffwalk", "buffend", "aura", "fieldmark", "loose", "comet" };
        static bool Forms2Beat(string beat) => Array.IndexOf(Forms2Beats, beat) >= 0;

        static string OnForms2(string[] a)
        {
            string glyph = a[2], beat = a[3].ToLowerInvariant();
            if (glyph.Length != 1) throw new PostLedger308.Refused("give one letter, e.g. preview:on:거:guard:phase=window");
            Context c = null;
            try
            {
                c = Begin(a.Skip(4));
                if (!c.Map.TryGet(glyph[0], out var row)) throw new PostLedger308.Refused("'" + glyph + "' is not one of the 120 letters");
                var want = beat == "guard" ? DeployCategory308.Parry : beat == "fence" ? DeployCategory308.Ward : beat == "emerge" ? DeployCategory308.Summon
                    : beat == "fieldmark" ? DeployCategory308.Field : beat == "loose" ? DeployCategory308.ComboInstall : beat == "comet" ? DeployCategory308.AttackSingle : DeployCategory308.Buff;
                if (row.Category != want) throw new PostLedger308.Refused("beat " + beat + " is for " + want + " letters (" + glyph + " is " + row.Category + ")");
                if (beat == "guard" || beat == "fence" || beat == "loose" || beat == "comet") row.LegacyBody = DeployLegacyBody308.Retire;
                bool mobile = c.Opt.TryGetValue("tier", out var tierText) && tierText.Equals("mobile", StringComparison.OrdinalIgnoreCase);
                var tier = mobile ? DeployTier308.Mobile : DeployTier308.PC;
                if (mobile && c.Profile.AtlasMobile != null) { c.Burst.SetTexture("_Atlas", c.Profile.AtlasMobile); c.Residue.SetTexture("_Atlas", c.Profile.AtlasMobile); }
                int seed = (int)Num(c.Opt, "seed", 308f);
                float grade = Mathf.Clamp01(Num(c.Opt, "grade", .5f));
                string phase = c.Opt.TryGetValue("phase", out var phaseText) ? phaseText.ToLowerInvariant() : "";
                MakeRoot(c, Num(c.Opt, "fov", 60f));
                float celSeconds = c.Profile.CelSeconds(c.Profile.Tier(tier));
                var quad = InkResidueField308.BuildQuad(); quad.name = Prefix + "Quad";
                Shader.SetGlobalFloat("_OhResidueNow", Mathf.Max(0f, Num(c.Opt, "age", 0f)));
                string standIn = StandIn(c);
                InkDeployRuntime308 runtime = null;
                float time = 0f; string text;

                switch (beat)
                {
                    case "guard":
                    {
                        // the rule's own numbers (CombatConfigSO.ParryWindow / GuardDuration x the hold scale), unless given
                        var config = AssetDatabase.FindAssets("t:CombatConfigSO").Select(g => AssetDatabase.LoadAssetAtPath<CombatConfigSO>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(o => o != null);
                        float hold = Mathf.Clamp(Num(c.Opt, "hold", 1f), .1f, 1f);
                        float window = Num(c.Opt, "window", (config != null ? config.ParryWindow : .9f) * hold), life = Num(c.Opt, "life", (config != null ? config.GuardDuration : 4f) * hold);
                        if (string.IsNullOrEmpty(phase)) phase = "window";
                        Vector3 letter = c.EyePoint + c.Forward * 1f;
                        runtime = Runtime(c, glyph, letter, new DeployCast308
                        {
                            Profile = c.Profile, Row = row, Origin = letter, FallbackPoint = letter, Grade01 = grade, Tint = PreviewTint(row.Element), Seed = seed, Tier = tier,
                            CameraPosition = c.EyePoint, HasCameraPosition = true, HoldSeconds = life, GuardWindow = window, GuardLife = life,
                        });
                        var line = runtime.GuardTimeline;
                        float melt = c.Profile.Beats.Melt, hitAge = Mathf.Max(0f, Num(c.Opt, "hitage", .08f));
                        bool contactInWindow = phase == "success" || phase == "half" || phase == "fail";
                        time = phase == "window" ? Mathf.Min(window * .5f, (line.WetEndCel - .5f) * celSeconds) : phase == "block" || phase == "blockhit" ? (window + life) * .5f : phase == "end" ? life - melt * .5f
                            : contactInWindow ? Mathf.Min(window * .5f, (line.WetEndCel - .5f) * celSeconds) : throw new PostLedger308.Refused("phase must be window, block, success, half, blockhit, fail or end");
                        time = Num(c.Opt, "t", time);
                        runtime.Sample(time);
                        string contactText = "";
                        if (contactInWindow || phase == "blockhit")
                        {
                            var contact = phase == "success" ? GuardContact308.Success : phase == "half" ? GuardContact308.Half : phase == "fail" ? GuardContact308.Fail : GuardContact308.Block;
                            runtime.NoteGuardContact(contact);
                            time += Mathf.Max(celSeconds, hitAge);
                            runtime.Sample(time);
                            // the contact point of the rule's convention: 1.1 m high, 1 m toward the attacker
                            Vector3 point = c.Ground + Vector3.up * InkDeployRuntime308.ChestHeight + c.Forward * 1f;
                            var g = c.Profile.Guard;
                            float star = contact == GuardContact308.Success ? g.SuccessStar : contact == GuardContact308.Half ? g.HalfStar : contact == GuardContact308.Block ? g.BlockStar : g.FailBlot;
                            float starLife = contact == GuardContact308.Block ? g.BlockStarSeconds : contact == GuardContact308.Fail ? g.FailBlotSeconds : g.StarSeconds;
                            if (hitAge < starLife)
                                Mark(c, quad, "Hit_contact", Matrix4x4.TRS(point, Quaternion.identity, Vector3.one * star),
                                    new Vector4(0f, starLife, contact == GuardContact308.Fail ? InkBurstMeshBuilder308.CellIgnite : InkBurstMeshBuilder308.CellStarB, 1f), new Vector4(0f, 0f, 1f, 0f));
                            var drops = new DeployDrop308[24];
                            int n = InkForms2Rules308.GuardSplash(c.Profile, tier, contact, point, c.Forward, seed, drops);
                            for (int i = 0; i < n; i++)
                            {
                                Vector3 at = drops[i].Local + drops[i].Velocity * hitAge + Vector3.down * (.5f * c.Profile.Residue.Gravity * hitAge * hitAge);
                                // forms3 (D10): a tailed drop lies along the way it travels, as the residue field draws it
                                var pose = InkResidueField308.AirPose(c.Profile.AirTail, at, drops[i].Velocity + Vector3.down * (c.Profile.Residue.Gravity * hitAge), drops[i].Size, drops[i].Cell, out bool tailed);
                                Mark(c, quad, "Hit_" + i + "_air", pose, new Vector4(0f, drops[i].Life, drops[i].Cell, 1f), new Vector4(0f, 0f, 1f, tailed ? 1f : 0f));
                            }
                            Shader.SetGlobalFloat("_OhResidueNow", hitAge);
                            contactText = " | contact " + contact + ": mark " + Deploy308.F(star) + " m for " + Deploy308.F(starLife) + " s, " + n + " drops, " + Deploy308.F(hitAge) + " s after";
                        }
                        text = "guard " + phase + " | window " + Deploy308.F(window) + " s life " + Deploy308.F(life) + " s (hold x" + Deploy308.F(hold) + (config != null ? ", from " + AssetDatabase.GetAssetPath(config) : ", no CombatConfigSO found: .9 / 4")
                            + ") | wet until cel " + line.WetEndCel + ", ends in cel " + line.EndCel + ", glow ends at cel " + line.GlowEndCel + " | phase now " + runtime.GuardPhase + ", mesh rebuilt " + runtime.MeshRebuilds + "x" + contactText;
                        break;
                    }
                    case "fence":
                    {
                        if (string.IsNullOrEmpty(phase)) phase = "stand";
                        float life = Num(c.Opt, "life", 6f), radius = Num(c.Opt, "radius", 3f), fade = Num(c.Opt, "fade", .5f);
                        Vector3 centre = c.Ground + Vector3.up * .02f;
                        runtime = Runtime(c, glyph, centre, new DeployCast308
                        {
                            Profile = c.Profile, Row = row, Origin = centre, FallbackPoint = centre + c.Forward, Grade01 = grade, Tint = PreviewTint(row.Element), Seed = seed, Tier = tier,
                            CameraPosition = c.EyePoint, HasCameraPosition = true, HoldSeconds = life, WardRadius = radius, WardHeight = 2.2f, FadeSeconds = fade,
                        });
                        time = phase == "rise" ? 2.5f * celSeconds : phase == "stand" ? life * .5f : phase == "drain" ? runtime.HoldEnd + c.Profile.Beats.Melt * .5f
                            : throw new PostLedger308.Refused("phase must be rise, stand or drain");
                        time = Num(c.Opt, "t", time);
                        runtime.Sample(time);
                        int marks = GroundMarks(c, quad, runtime, time);
                        // :crowd=N - the pool rule, as the director applies it: N ordinary bursts ask for a slot while this fence stands
                        string crowd = "";
                        if (c.Opt.ContainsKey("crowd"))
                        {
                            var t = c.Profile.Tier(tier);
                            int general = Mathf.Clamp(t.Bursts, 1, 8), standing = Mathf.Clamp(t.StandingBursts, 0, 6), n = Mathf.Max(0, (int)Num(c.Opt, "crowd", 6f)), pushed = 0, serial = 1;
                            var used = new bool[general + standing]; var order = new int[general + standing];
                            InkForms2Rules308.SlotRange(true, general, standing, out int f0, out int fc);
                            int fenceSlot = InkForms2Rules308.PickSlot(used, order, f0, fc, out _); used[fenceSlot] = true; order[fenceSlot] = serial++;
                            for (int i = 0; i < n; i++)
                            {
                                InkForms2Rules308.SlotRange(false, general, standing, out int g0, out int gc);
                                int slot = InkForms2Rules308.PickSlot(used, order, g0, gc, out _);
                                if (slot == fenceSlot) pushed++;
                                if (slot >= 0) { used[slot] = true; order[slot] = serial++; }
                            }
                            crowd = " | crowd " + n + " bursts on " + general + " + " + standing + " slots: the fence was pushed out " + pushed + " time(s)";
                        }
                        text = "fence " + phase + " | radius " + Deploy308.F(radius) + " m, life " + Deploy308.F(life) + " s, starts to go at " + Deploy308.F(runtime.HoldEnd) + " s | standing=" + runtime.Standing
                            + " | posts " + runtime.Stats.Posts + " rails " + runtime.Stats.Rails + " | " + marks + " ring marks" + crowd;
                        break;
                    }
                    case "emerge":
                    {
                        if (string.IsNullOrEmpty(phase)) phase = "rise";
                        float dist = Mathf.Clamp(Num(c.Opt, "dist", 4f), 1f, 30f);
                        Vector3 place = c.Ground + c.Forward * dist + Vector3.up * .02f;
                        if (phase == "exit")
                        {
                            float age = Mathf.Max(0f, Num(c.Opt, "age", .25f));
                            var su = c.Profile.Summon;
                            Mark(c, quad, "Mark_exit", InkResidueField308.Place(place, Vector3.up, c.Forward, su.ExitPuddle, su.ExitPuddle, c.Profile.Residue.Lift), new Vector4(0f, c.Profile.SpellLife, InkBurstMeshBuilder308.CellPuddle, c.Profile.Residue.Opacity), Vector4.zero);
                            int drops = mobile ? su.ExitDropsMobile : su.ExitDrops;
                            var rng = new DeployRng308(seed);
                            for (int i = 0; i < drops; i++)
                            {
                                float ang = (i + rng.Next()) / Mathf.Max(1, drops) * Mathf.PI * 2f;
                                Vector3 from = place + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * rng.Range(su.ExitRadiusMin, Mathf.Max(su.ExitRadiusMin, su.ExitRadiusMax))
                                    + Vector3.up * rng.Range(su.ExitHeightMin, Mathf.Max(su.ExitHeightMin, su.ExitHeightMax));
                                Vector3 at = from + Vector3.down * (.5f * c.Profile.Residue.Gravity * age * age);
                                if (at.y > place.y)
                                {
                                    var pose = InkResidueField308.AirPose(c.Profile.AirTail, at, Vector3.down * (c.Profile.Residue.Gravity * age), c.Profile.Residue.DropMid * su.ExitDropSizeMul, InkBurstMeshBuilder308.CellDropTailed, out bool tailed);
                                    Mark(c, quad, "Hit_" + i + "_air", pose, new Vector4(0f, su.ExitDropSeconds, InkBurstMeshBuilder308.CellDropTailed, 1f), new Vector4(0f, 0f, 1f, tailed ? 1f : 0f));
                                }
                            }
                            Shader.SetGlobalFloat("_OhResidueNow", age);
                            text = "emerge exit | puddle " + Deploy308.F(su.ExitPuddle) + " m + " + drops + " falling drops, " + Deploy308.F(age) + " s after the release | no burst";
                            break;
                        }
                        runtime = Runtime(c, glyph, place, new DeployCast308
                        {
                            Profile = c.Profile, Row = row, Origin = place, FallbackPoint = place + c.Forward * 2f, Grade01 = grade, Tint = PreviewTint(row.Element), Seed = seed, Tier = tier,
                            CameraPosition = c.EyePoint, HasCameraPosition = true, Anchor = place, HasAnchor = true,
                        });
                        time = phase == "rise" ? 2.5f * celSeconds : phase == "reveal" ? runtime.HoldEnd + c.Profile.Beats.Melt * .5f : throw new PostLedger308.Refused("phase must be rise, reveal or exit");
                        time = Num(c.Opt, "t", time);
                        runtime.Sample(time);
                        GroundMarks(c, quad, runtime, time);
                        text = "emerge " + phase + " | the summon stands " + Deploy308.F(dist) + " m ahead; the curtain " + Deploy308.F(c.Profile.Summon.StandOff) + " m nearer | gone by " + Deploy308.F(runtime.MeltEnd) + " s";
                        break;
                    }
                    case "comet":
                    {
                        float view = Mathf.Clamp(Num(c.Opt, "view", 10f), .8f, 60f), u = Mathf.Clamp01(Num(c.Opt, "u", .5f));
                        float flight = Num(c.Opt, "flight", view / 18f);
                        Vector3 origin = c.EyePoint + c.Forward * BrushReach - Vector3.up * .25f, target = c.Ground + c.Forward * view + Vector3.up * InkDeployRuntime308.ChestHeight;
                        float arc = Num(c.Opt, "arc", 0f), curve = Num(c.Opt, "curve", 0f), ease = Num(c.Opt, "ease", 1f);
                        runtime = Runtime(c, glyph, origin, new DeployCast308
                        {
                            Profile = c.Profile, Row = row, Origin = origin, FallbackPoint = target, ImpactClock = flight, Grade01 = grade, Tint = PreviewTint(row.Element), Seed = seed, Tier = tier,
                            CameraPosition = c.EyePoint, HasCameraPosition = true, PathArc = arc, PathCurve = curve, PathEase = ease,
                        });
                        time = Num(c.Opt, "t", u * flight);
                        runtime.Sample(time);
                        GroundMarks(c, quad, runtime, time);
                        Vector3 head = InkForms2Rules308.CometPoint(origin, target, InkForms2Rules308.CometU(time, flight), arc, curve, ease, InkForms2Rules308.CometSide(origin, target, seed));
                        var ce = c.Profile.CometOf(row.Element);
                        text = "comet u=" + Deploy308.F(InkForms2Rules308.CometU(time, flight)) + " | flight " + Deploy308.F(flight) + " s over " + Deploy308.F(view) + " m, path arc " + Deploy308.F(arc) + " curve " + Deploy308.F(curve) + " ease " + Deploy308.F(ease)
                            + " (the letter's own Vfx120Profile numbers are used in Play; here they are options) | head at " + head.ToString("0.00") + ", " + Deploy308.F(Vector3.Distance(head, c.EyePoint)) + " m from the eye = "
                            + Deploy308.F(2f * Mathf.Atan2(runtime.Stats.CometHead * .5f, Mathf.Max(.01f, Vector3.Distance(head, c.EyePoint))) * Mathf.Rad2Deg) + " deg (head " + Deploy308.F(runtime.Stats.CometHead) + " m, authored "
                            + Deploy308.F(ce.HeadSize) + "; tail half width " + Deploy308.F(runtime.Stats.CometTailHalf) + " m) | advance " + Deploy308.F(runtime.FlightAdvance);
                        break;
                    }
                    case "loose":
                    {
                        Vector3 letter = c.EyePoint + c.Forward * 1f - Vector3.up * .1f;
                        runtime = Runtime(c, glyph, letter, new DeployCast308
                        {
                            Profile = c.Profile, Row = row, Origin = letter, FallbackPoint = letter + c.Forward, Grade01 = grade, Tint = PreviewTint(row.Element), Seed = seed, Tier = tier,
                            CameraPosition = c.EyePoint, HasCameraPosition = true,
                        });
                        time = Num(c.Opt, "t", (c.Profile.Loose.SlideCel + .5f) * celSeconds);
                        runtime.Sample(time);
                        int marks = GroundMarks(c, quad, runtime, time);
                        text = "loose mark | slides on cel " + c.Profile.Loose.SlideCel + ", falls on cel " + c.Profile.Loose.FallCel + ", gone by " + Deploy308.F(runtime.MeltEnd) + " s | ground marks shown " + marks
                            + " (held " + runtime.Stats.HeldDrops + " of " + runtime.Stats.GroundMarks + ")";
                        break;
                    }
                    case "buffcast":
                    {
                        Vector3 feet = c.Ground + Vector3.up * .05f;
                        runtime = Runtime(c, glyph, feet, new DeployCast308
                        {
                            Profile = c.Profile, Row = row, Origin = feet, FallbackPoint = feet + c.Forward, Grade01 = grade, Tint = PreviewTint(row.Element), Seed = seed, Tier = tier,
                            CameraPosition = c.EyePoint, HasCameraPosition = true,
                        });
                        time = Num(c.Opt, "t", (runtime.ImpactCel + 2.5f) * celSeconds);
                        runtime.Sample(time);
                        GroundMarks(c, quad, runtime, time);
                        text = "buff cast | the winding strokes and the ring at the feet; gone by " + Deploy308.F(runtime.MeltEnd) + " s (nothing stands afterwards)";
                        break;
                    }
                    case "buffwalk":
                    {
                        int steps = Mathf.Clamp((int)Num(c.Opt, "steps", 6f), 1, 24);
                        var foot = c.Profile.Foot;
                        Vector3 side = Vector3.Cross(Vector3.up, c.Forward);
                        int perStep = InkForms2Rules308.FootMarksForStep(c.Profile, tier, 1), marks = 0;
                        for (int i = 0; i < steps; i++)
                        {
                            bool left = i % 2 == 0;
                            Vector3 point = c.Ground + c.Forward * (1.2f + i * foot.StrideWalk * .5f) + side * (left ? -foot.SideOffset : foot.SideOffset);
                            Mark(c, quad, "Foot_" + i, InkResidueField308.Place(point, Vector3.up, c.Forward, foot.Width, foot.Length, c.Profile.Residue.Lift),
                                new Vector4(0f, c.Profile.FootLife, i % 2 == 0 ? InkBurstMeshBuilder308.CellFoot : InkBurstMeshBuilder308.CellFootB, foot.Opacity), new Vector4(left ? 0f : 1f, 0f, 0f, 0f));
                            for (int k = 0; k < perStep; k++)
                            {
                                Vector3 at = point + side * ((left ? -1f : 1f) * foot.Width * (1.3f + k));
                                Mark(c, quad, "Mark_buff_" + i + "_" + k, InkResidueField308.Place(at, Vector3.up, c.Forward, c.Profile.Buff.FootMarkSize, c.Profile.Buff.FootMarkSize, c.Profile.Residue.Lift),
                                    new Vector4(0f, c.Profile.BuffMarkLife, c.Profile.BuffMarkCell((int)row.Element), foot.Opacity), Vector4.zero);
                                marks++;
                            }
                        }
                        text = "buff walk | " + steps + " steps, " + marks + " buff marks (cell " + c.Profile.BuffMarkCell((int)row.Element) + ", " + Deploy308.F(c.Profile.Buff.FootMarkSize) + " m, " + perStep + " per step on this tier, at most "
                            + Deploy308.F(c.Profile.Tier(tier).FootMarksPerSecond) + " per second in Play) | nothing floats on screen";
                        break;
                    }
                    case "buffend":
                    {
                        float age = Mathf.Max(0f, Num(c.Opt, "age", .15f));
                        var b = c.Profile.Buff;
                        for (int i = 0; i < b.ShedDrops; i++)
                        {
                            float k = b.ShedDrops <= 1 ? 0f : i / (float)(b.ShedDrops - 1) - .5f;
                            InkForms2Rules308.BuffShed(c.Profile, c.EyePoint, c.Forward, k, out Vector3 from, out Vector3 velocity);
                            float vary = InkDeployForms308.BuffShedVary(c.Profile, (int)Num(c.Opt, "seed", 308f), i, ref from, ref velocity);   // forms4 (P8): as the director does
                            Vector3 at = from + velocity * age + Vector3.down * (.5f * c.Profile.Residue.Gravity * age * age);
                            if (at.y > c.Ground.y)
                            {
                                var pose = InkResidueField308.AirPose(c.Profile.AirTail, at, velocity + Vector3.down * (c.Profile.Residue.Gravity * age), c.Profile.Residue.DropMid * b.ShedSizeMul * vary, InkBurstMeshBuilder308.CellDropTailed, out bool tailed, b.ShedTailMax);
                                Mark(c, quad, "Hit_" + i + "_air", pose, new Vector4(0f, b.ShedSeconds, InkBurstMeshBuilder308.CellDropTailed, 1f), new Vector4(0f, 0f, 1f, tailed ? 1f : 0f));
                            }
                        }
                        Mark(c, quad, "Mark_end", InkResidueField308.Place(c.Ground, Vector3.up, c.Forward, b.EndRing, b.EndRing, c.Profile.Residue.Lift), new Vector4(0f, c.Profile.SpellLife, InkBurstMeshBuilder308.CellRing, c.Profile.Residue.Opacity), Vector4.zero);
                        Shader.SetGlobalFloat("_OhResidueNow", age);
                        text = "buff end | " + b.ShedDrops + " tailed drops tossed up " + Deploy308.F(b.ShedForward) + " m ahead and " + Deploy308.F(b.ShedDown) + " m under the eye, then falling out of the view, a " + Deploy308.F(b.EndRing) + " m ring at the feet (look down to see it), " + Deploy308.F(age) + " s after";
                        break;
                    }
                    case "aura":
                    {
                        float age = Mathf.Max(0f, Num(c.Opt, "age", 3f)), radius = Num(c.Opt, "radius", 2f);
                        int stamps = c.Profile.Tier(tier).AuraStamps, rounds = Mathf.FloorToInt(age / Mathf.Max(.1f, c.Profile.Buff.AuraInterval)) + 1, shown = 0;
                        float size = c.Profile.Residue.DropMid * c.Profile.Buff.AuraSizeMul;
                        for (int r = 0; r < rounds; r++)
                        {
                            float born = r * c.Profile.Buff.AuraInterval;
                            if (age - born > c.Profile.BuffAuraLife) continue;
                            for (int i = 0; i < stamps; i++)
                            {
                                float ang = (r * c.Profile.Buff.AuraTurnDeg + i * 360f / Mathf.Max(1, stamps)) * Mathf.Deg2Rad;
                                Mark(c, quad, "Mark_aura_" + r + "_" + i, InkResidueField308.Place(c.Ground + new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius), Vector3.up, c.Forward, size, size, c.Profile.Residue.Lift),
                                    new Vector4(born, c.Profile.BuffAuraLife, InkBurstMeshBuilder308.CellDrop, c.Profile.Residue.Opacity), Vector4.zero);
                                shown++;
                            }
                        }
                        Shader.SetGlobalFloat("_OhResidueNow", age);
                        text = "fire aura | " + shown + " drops alive on the " + Deploy308.F(radius) + " m ring after " + Deploy308.F(age) + " s (" + stamps + " every " + Deploy308.F(c.Profile.Buff.AuraInterval) + " s, each dries in " + Deploy308.F(c.Profile.BuffAuraLife) + " s)";
                        break;
                    }
                    default:   // fieldmark
                    {
                        if (string.IsNullOrEmpty(phase)) phase = "held";
                        if (phase != "rise" && phase != "held" && phase != "dry") throw new PostLedger308.Refused("phase must be rise, held or dry");
                        var category = c.Profile.CategoryOf(DeployCategory308.Field);
                        var element = c.Profile.ElementOf(row.Element);
                        var rng = new DeployRng308(glyph[0] * 7919 + seed);
                        int drops = Mathf.RoundToInt(category.ResidueDrops * Mathf.Max(.5f, element.DropMul));
                        float age = phase == "dry" ? c.Profile.FieldTail * .6f : 0f;
                        Vector3 centre = c.Ground + c.Forward * 2.5f;
                        for (int i = 0; i < drops; i++)
                        {
                            float ang = rng.Next() * Mathf.PI * 2f, r = 1.5f * Mathf.Sqrt(rng.Next()), pick = rng.Next();
                            float size = (pick < .5f ? c.Profile.Residue.DropSmall : pick < .85f ? c.Profile.Residue.DropMid : c.Profile.Residue.DropLarge) * element.DropSizeMul * 2f;
                            int cell = pick < .5f ? InkBurstMeshBuilder308.CellDrop : pick < .85f ? InkBurstMeshBuilder308.CellCluster : InkBurstMeshBuilder308.CellPuddle;
                            Mark(c, quad, "Mark_field_" + i, InkResidueField308.Place(centre + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r), Vector3.up, c.Forward, size, size, c.Profile.Residue.Lift),
                                new Vector4(0f, c.Profile.FieldTail, cell, c.Profile.Residue.Opacity), Vector4.zero);
                        }
                        Shader.SetGlobalFloat("_OhResidueNow", age);
                        text = "field marks " + phase + " | " + drops + " marks; in Play they stay wet while the wiring says the platform / bridge exists, then dry in " + Deploy308.F(c.Profile.FieldTail)
                            + " s (shown " + Deploy308.F(age) + " s into the drying) | the birth strokes of the design (three rising strokes / one lengthening stroke) are NOT built in this stage";
                        break;
                    }
                }

                Finish(c, "glyph=" + glyph + "|beat=" + beat + "|phase=" + phase + "|seed=" + seed + "|tier=" + tier + "|t=" + time.ToString("0.###", CultureInfo.InvariantCulture), !mobile);
                c.Log.AppendLine("preview on (forms2): " + glyph + " " + row.Category + " " + row.Element + " | " + text + standIn + " | tier " + tier + (c.GroundFound ? "" : " | no ground collider: flat plane"));
                if (runtime != null)
                {
                    var s = runtime.Stats;
                    c.Log.AppendLine("form " + runtime.Form + " at " + time.ToString("0.###", CultureInfo.InvariantCulture) + " s (cel " + runtime.CurrentCel + ") | glow now " + Deploy308.F(runtime.GlowNow) + ", glow ends at " + Deploy308.F(runtime.GlowEnd)
                        + " s | top " + Deploy308.F(s.TopHeight) + " m over the ground, " + Deploy308.F(s.TopElevationDeg) + " deg from the eye | est. screen cover " + (s.ScreenShare * 100f).ToString("0.#", CultureInfo.InvariantCulture) + " %");
                    c.Log.Append(Describe(runtime, null));
                }
                return c.Log.ToString();
            }
            catch { Fail(c); throw; }
        }

        static InkDeployRuntime308 Runtime(Context c, string glyph, Vector3 at, in DeployCast308 cast)
        {
            var holder = Make("Burst_" + glyph, c.Root.transform);
            holder.transform.SetPositionAndRotation(at, Quaternion.LookRotation(c.Forward, Vector3.up));
            var runtime = holder.AddComponent<InkDeployRuntime308>();
            var asked = cast; asked.NewForms = true;   // the previews of this file show the forms2 forms
            if (!runtime.Configure(asked, null)) throw new PostLedger308.Refused("the runtime refused the cast");
            return runtime;
        }

        // :dummy=<m> - a plain box as an enemy stand-in (no collider), to see what a form covers of a body at that distance
        static string StandIn(Context c)
        {
            float distance = Num(c.Opt, "dummy", 0f);
            if (distance <= 0f) return "";
            float height = Mathf.Clamp(Num(c.Opt, "dummyh", 1.7f), .2f, 4f), width = Mathf.Clamp(Num(c.Opt, "dummyw", height < 1.2f ? 1.1f : .5f), .1f, 4f);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = Prefix + "StandIn"; body.hideFlags = Flags;
            var collider = body.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            body.transform.SetParent(c.Root.transform, false);
            body.transform.SetPositionAndRotation(c.Ground + c.Forward * distance + Vector3.up * (height * .5f), Quaternion.LookRotation(c.Forward, Vector3.up));
            body.transform.localScale = new Vector3(width, height, .35f);
            return " | stand-in " + Deploy308.F(width) + " x " + Deploy308.F(height) + " m at " + Deploy308.F(distance) + " m";
        }

        // the ground marks the form has asked for up to `time` (edit mode has no frame updates: one renderer per mark, wet)
        static int GroundMarks(Context c, Mesh quad, InkDeployRuntime308 runtime, float time)
        {
            int cel = Mathf.FloorToInt(time / runtime.CelSeconds + .0001f), n = 0;
            for (int i = 0; i < runtime.DropCount; i++)
            {
                var drop = runtime.Drop(i);
                if (drop.Air || drop.Cel > cel) continue;
                Vector3 world = runtime.Root.TransformPoint(drop.Local);
                Vector3 point = new Vector3(world.x, c.Ground.y, world.z), normal = Vector3.up;
                if (Physics.Raycast(world + Vector3.up * 1.1f, Vector3.down, out var hit, 6f, c.Profile.Residue.GroundLayers, QueryTriggerInteraction.Ignore) && hit.normal.y >= c.Profile.Residue.MinNormalY) { point = hit.point; normal = hit.normal; }
                Mark(c, quad, "Mark_" + i, InkResidueField308.Place(point, normal, c.Forward, drop.Size, drop.Size, c.Profile.Residue.Lift),
                    new Vector4(0f, c.Profile.SpellLife, drop.Cell, c.Profile.Residue.Opacity), Vector4.zero);
                n++;
            }
            return n;
        }
    }
}
