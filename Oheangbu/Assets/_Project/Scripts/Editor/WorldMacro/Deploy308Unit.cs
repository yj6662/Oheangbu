using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Data.Spell;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // SPEC-SPELL-DEPLOY-308 "deploy308-unit": the checks that need no Play mode
    // (AC-D2 D3 D4 D5 D9 D10 D11 D12 D19 D22 D16 D29 D30 D18 D31 D32 D33 D34 D35 D36 D37 D38 D39 D40 D26 D27 D23).
    // D308-10c: D5 = momentary glow only, D9 = impact frames only for a groggy target, D18 = the stronger hit reaction,
    // D29 = the footprints boot alone with the layer switch off (D308-107), D30 = the KTP contact sits on the body surface facing the hit (D308-107). D26 = the hit splash only on a confirmed hit, D27 = which rows replace the KTP enemy-hit contact (D308-13b).
    // D31 = the contacts boot alone with the layer switch off (D308-107 d): flicker + impact director, no slots, the KTP contact stays.
    // D32 = the impact frame's figure is the hit target's renderers, and the contact path asks it on a groggy spell hit only (D308-106 c).
    // D33 = a range-ㄱ letter's hit has no pool and its copy carries one foot stain, laid once per struck enemy on the ground below (D308-104 e).
    // D34 = the live summon profiles retire the old seal / phase particles / debris, a profile without the flag keeps them (D308-107 f).
    // D35 = the builder size caps: ring band <= 1.0 m, mark radius <= r + 1.8 m (D308-104 g).
    // D36 = a Scatter impact beside the body marks the edge the needle passed (D308-108). D37 = the summon core lights for the first two cels of activity only (D308-108).
    // Everything is built in memory with HideFlags.DontSave and destroyed before the command returns; no asset, scene or setting
    // is written. A line is "ok" or "FAIL" with the measured numbers; the words PASS / VALIDATED are left to the report.
    internal static class Deploy308Unit
    {
        const HideFlags Flags = HideFlags.DontSave;
        const string HudInclude = "Assets/_Project/Art/UI/UI304/Shaders/ImpactHud308.hlsl",
            MeterShader = "Assets/_Project/Art/UI/UI304/Shaders/InkMeter.shader", RevealShader = "Assets/_Project/Art/UI/UI304/Shaders/InkReveal.shader";

        sealed class FakeHost : ISpellDeployHost308
        {
            public int Impacts, ImpactFrames, Stamps, Air, Still, Began, GroggyAsked, GroggyWithPlan, GroggyWithTarget;
            public bool Groggy;
            public float Pause, MaxStamp, AirSize;
            public DeployTier308 TierValue;
            public DeployTier308 Tier => TierValue;
            public float CutPause => Pause;
            public Camera ViewCamera => null;
            public bool RentBurst(InkDeployRuntime308 owner, out InkBurstBuffer308 buffer, out Mesh mesh) { buffer = null; mesh = null; return false; }
            public void ReturnBurst(InkDeployRuntime308 owner, InkBurstBuffer308 buffer, Mesh mesh) { }
            public void StampResidue(in ResidueStamp308 stamp) { Stamps++; MaxStamp = Mathf.Max(MaxStamp, Mathf.Max(stamp.Width, stamp.Length)); }
            public void ReleaseHeld(int owner) { }
            public void SpawnAir(Vector3 worldPoint, Vector3 velocity, float size, int cell, float life, float gravityScale, float landY)
            { if (gravityScale <= 0f) { Still++; return; } Air++; AirSize += size; }
            public void RequestImpact(in ImpactRequest308 request) { Impacts++; ImpactFrames = request.Frames; }
            public void NotifyDeployBegan(char letter) { Began++; }
            public bool TargetGroggy(Transform target, AreaImpactPlan plan)
            { GroggyAsked++; if (plan != null) GroggyWithPlan++; else if (target != null) GroggyWithTarget++; return Groggy; }
        }

        sealed class FakePass : IImpactPass308
        {
            public int Added, Flat, Stain, Knock;
            public void Setup(Material material, int fullPass, int quadPass) { }
            public void ClearFlicker() { Added = Flat = Stain = Knock = 0; }
            public void AddFlicker(Renderer renderer, Material flat, int subMeshes)
            {
                Added++;
                if (flat.name.Contains("stain")) Stain++; else if (flat.name.Contains("knock")) Knock++; else Flat++;
            }
            public bool HasWork => Added > 0;
            public int RecordedFull => 0;
            public int RecordedDraw => 0;
            public bool Enqueue(Camera camera) => true;
        }

        internal static string Run()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
            var sb = new StringBuilder("deploy308-unit\n");
            int checks = 0, failed = 0;
            void Check(string id, bool ok, string detail) { checks++; if (!ok) failed++; sb.AppendLine(id + " " + (ok ? "ok" : "FAIL") + ": " + detail); }
            var trash = new List<Object>();
            GameObject root = null;
            try
            {
                // the profile under test: the asset when it exists (its tuned values), otherwise the code defaults
                var asset = AssetDatabase.LoadAssetAtPath<SpellDeploy308ProfileSO>(Deploy308.ProfilePath);
                var profile = asset != null ? Object.Instantiate(asset) : ScriptableObject.CreateInstance<SpellDeploy308ProfileSO>();
                profile.hideFlags = Flags; trash.Add(profile);
                var map = ScriptableObject.CreateInstance<SpellDeploy308MapSO>(); map.hideFlags = Flags; trash.Add(map);
                map.Rows = Deploy308.ReadMap(out string mapReport, out _);
                profile.Map = map; profile.LayerEnabled = true;
                if (profile.ImpactShader == null) profile.ImpactShader = Shader.Find(Deploy308.ImpactShader);
                root = new GameObject("Deploy308Unit") { hideFlags = Flags };
                var pc = profile.Tier(DeployTier308.PC); var mobile = profile.Tier(DeployTier308.Mobile);
                float cel = profile.CelSeconds(pc);
                // the judged target of the synthetic single casts, and an enemy that is not it
                var judged = new GameObject("UnitTarget") { hideFlags = Flags }; judged.transform.SetParent(root.transform, false); judged.transform.position = new Vector3(0f, 0f, 6f);
                var bystander = new GameObject("UnitBystander") { hideFlags = Flags }; bystander.transform.SetParent(root.transform, false); bystander.transform.position = new Vector3(3f, 0f, 6f);

                // ---- D2: 120 rows, counts, grid (re-asserted from the syllables), impact-eligible rows = the 50 attacks
                Check("D2", map.Count == 120, mapReport);

                // ---- D3: beats
                {
                    var notes = new StringBuilder(); bool ok = true;
                    foreach (char letter in "가노서무옴걱")
                    {
                        foreach (float clock in new[] { .05f, .18f, .31f, .6f, 1.0f })
                        {
                            var runtime = Runtime(root, profile, map, letter, .5f, clock, null, out var cast);
                            float judgedClock = InkDeployRuntime308.JudgementClock(profile, cast);
                            bool whole = true, silent = true;
                            float lead = profile.Beats.Ignite + profile.Beats.Silence;
                            for (int i = 0; i < runtime.BirthCount; i++)
                            {
                                float birth = runtime.BirthCel(i);
                                if (Mathf.Abs(birth - Mathf.Round(birth)) * cel > .001f) whole = false;
                                float at = birth * cel;
                                if (judgedClock >= lead + cel && at > profile.Beats.Ignite + .001f && at < lead - .001f) silent = false;
                            }
                            bool first = Mathf.Abs(runtime.ImpactCel * cel - judgedClock) <= cel + .001f;
                            if (!whole || !silent || !first) { ok = false; notes.Append(" [" + letter + " T=" + clock + " whole=" + whole + " silent=" + silent + " firstCel=" + runtime.ImpactCel + "]"); }
                            Kill(runtime);
                        }
                    }
                    Check("D3", ok, "6 letters x 5 clocks: births on whole cels (cel " + Deploy308.F(cel * 1000f) + " ms), nothing born in the silence, first burst cel within 1 cel of the judgement clock" + notes);
                }

                // ---- D4: grade -> bold stroke count (the cast plan replay half of D4 needs Play)
                {
                    int[] bold = new int[3]; float[] grades = { 0f, .5f, 1f };
                    for (int g = 0; g < 3; g++) { var r = Runtime(root, profile, map, '가', grades[g], .6f, null, out _); bold[g] = r.Stats.Bold; Kill(r); }
                    Check("D4", bold[0] == profile.Grades[0].Bold && bold[1] == profile.Grades[1].Bold && bold[2] == profile.Grades[2].Bold && bold[0] < bold[1] && bold[1] < bold[2],
                        "bold strokes at grade 0 / .5 / 1 = " + bold[0] + " / " + bold[1] + " / " + bold[2] + " (data " + profile.Grades[0].Bold + " / " + profile.Grades[1].Bold + " / " + profile.Grades[2].Bold + ")");
                }

                // ---- D5 (D308-10c): the glow is momentary - on the cels after a stroke's birth, never after, never anywhere else
                {
                    int glowCels = profile.GlowCels; float prev = 2f;
                    bool falloff = glowCels <= 0 || Mathf.Approximately(profile.GlowFalloff(0f), 1f);
                    for (int k = 0; k <= SpellDeploy308ProfileSO.MaxGlowCels + 2; k++)
                    {
                        float g = profile.GlowFalloff(k);
                        if (g > prev + 1e-5f || (k >= glowCels && g != 0f)) falloff = false;
                        prev = g;
                    }
                    float keepAmount = profile.Stroke.GlowAmount; int keepCels = profile.Stroke.GlowCels;
                    profile.Stroke.GlowAmount = 5f; profile.Stroke.GlowCels = 99;
                    bool clamped = profile.GlowAmount <= SpellDeploy308ProfileSO.MaxGlowAmount + 1e-6f && profile.GlowCels <= SpellDeploy308ProfileSO.MaxGlowCels
                        && profile.GlowFalloff(SpellDeploy308ProfileSO.MaxGlowCels) == 0f;
                    profile.Stroke.GlowAmount = keepAmount; profile.Stroke.GlowCels = keepCels;

                    // the amount the runtime hands to the material: > 0 only inside the burst beat, 0 on every cel of the hold and the
                    // melt, 0 while a ward stands after its rise
                    var notes = new StringBuilder(); bool beat = true; float wardStanding = -1f, wardGlowEnd = 0f, wardRiseEnd = 0f;
                    foreach (char letter in "가노무옴걱")
                    {
                        var runtime = Runtime(root, profile, map, letter, 1f, .5f, null, out _);
                        float within = 0f, after = 0f, end = runtime.GlowEnd, meltEnd = runtime.MeltEnd;
                        if (letter == '무') { wardGlowEnd = end; wardRiseEnd = runtime.ImpactTime + Mathf.Max(1, Mathf.RoundToInt(profile.Beats.Burst / cel)) * cel; }
                        for (int k = 0; k * cel < meltEnd; k++)
                        {
                            float t = k * cel + .001f;
                            runtime.Sample(t);
                            if (!runtime.Configured) break;
                            if (t < end) within = Mathf.Max(within, runtime.GlowNow); else after = Mathf.Max(after, runtime.GlowNow);
                            if (letter == '무' && t > end + 1f && wardStanding < 0f) wardStanding = runtime.GlowNow;
                        }
                        bool lit = profile.GlowAmount <= 0f || within > 0f;
                        if (!lit || after != 0f || end > runtime.HoldEnd + 1e-4f) { beat = false; notes.Append(" [" + letter + " within=" + Deploy308.F(within) + " after=" + Deploy308.F(after) + " glowEnd=" + Deploy308.F(end) + " holdEnd=" + Deploy308.F(runtime.HoldEnd) + "]"); }
                        Kill(runtime);
                    }
                    bool ward = wardStanding == 0f && wardGlowEnd <= wardRiseEnd + 1e-4f;

                    // the shaders: the glow term lives in InkBurst308 only; nothing blends additively or declares an HDR colour
                    string burst = Source(Deploy308.BurstShader), common = File.Exists(PostLedger308.RepoPath("Oheangbu/" + CommonInclude())) ? File.ReadAllText(PostLedger308.RepoPath("Oheangbu/" + CommonInclude())) : null;
                    bool burstHas = burst != null && burst.Contains("_GlowCels") && burst.Contains("OH_GLOW_CEILING_OUT") && burst.Contains("Blend Off") && common != null && common.Contains("OH_GLOW_CEILING_OUT");
                    var dirty = new StringBuilder();
                    foreach (var pair in new[]
                    {
                        new KeyValuePair<string, string>("InkResidue308", Source(Deploy308.ResidueShader)), new KeyValuePair<string, string>("InkFlat308", Source(Deploy308.FlatShader)),
                        new KeyValuePair<string, string>("ImpactFrame308", Source(Deploy308.ImpactShader)), new KeyValuePair<string, string>("ImpactHud308.hlsl", FileOrNull(HudInclude)),
                        new KeyValuePair<string, string>("InkMeter", FileOrNull(MeterShader)), new KeyValuePair<string, string>("InkReveal", FileOrNull(RevealShader)),
                        new KeyValuePair<string, string>("InkBurst308 (blend / HDR only)", burst == null ? null : burst.Replace("_Glow", "")),
                        // SPEC-SPELL-VFX-OVERHAUL-308 2nd-stage gate (D308-84): the #308 spell shaders. Solid / strand / arc carry no glow at
                        // all; InkBleed / InkFlipbook carry only the capped birth glow (checked below for its ceiling), so here blend / HDR only.
                        new KeyValuePair<string, string>("InkSolid308", FileOrNull(Vfx308Dir + "InkSolid308.shader")), new KeyValuePair<string, string>("InkBody308", FileOrNull(Vfx308Dir + "InkBody308.shader")),
                        new KeyValuePair<string, string>("InkStrand308", FileOrNull(Vfx308Dir + "InkStrand308.shader")), new KeyValuePair<string, string>("InkArc308", FileOrNull("Assets/_Project/Shaders/SwingInk308/InkArc308.shader")),
                        new KeyValuePair<string, string>("InkBleed308 (blend / HDR only)", Vfx308Glow("InkBleed308.shader")), new KeyValuePair<string, string>("InkFlipbook308 (blend / HDR only)", Vfx308Glow("InkFlipbook308.shader")),
                    })
                    {
                        if (pair.Value == null) { dirty.Append(" [" + pair.Key + " not found]"); continue; }
                        if (pair.Value.Contains("_Glow") || pair.Value.Contains("_Emission") || pair.Value.Contains("[HDR]") || pair.Value.Contains("Blend One One") || pair.Value.Contains("Blend SrcAlpha One\n"))
                            dirty.Append(" [" + pair.Key + " has a glow / emission / additive term]");
                    }

                    // bloom: the glow's ceiling stays under the soft knee (half the linear threshold) of every project profile whose bloom is on
                    float lowest = float.PositiveInfinity; string lowestAt = "no volume profile with bloom on under Assets/_Project";
                    foreach (string guid in AssetDatabase.FindAssets("t:VolumeProfile", new[] { "Assets/_Project" }))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        var volume = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
                        if (volume == null || !volume.TryGet<Bloom>(out var bloom) || !bloom.active || !bloom.intensity.overrideState || bloom.intensity.value <= 0f) continue;
                        float threshold = bloom.threshold.overrideState ? bloom.threshold.value : .9f;
                        if (threshold < lowest) { lowest = threshold; lowestAt = path; }
                    }
                    float knee = float.IsPositiveInfinity(lowest) ? float.PositiveInfinity : Mathf.GammaToLinearSpace(lowest) * .5f;
                    float body = Mathf.GammaToLinearSpace(profile.Stroke.BodyValue), birth = Mathf.Min(body + profile.GlowAmount, SpellDeploy308ProfileSO.GlowOutputCeiling);
                    bool underKnee = SpellDeploy308ProfileSO.GlowOutputCeiling < knee;
                    Check("D5", falloff && clamped && beat && ward && burstHas && dirty.Length == 0 && underKnee,
                        "glow " + Deploy308.F(profile.GlowAmount) + " (linear) over " + glowCels + " cels: falloff 1 -> 0 and 0 from cel " + glowCels + " on " + falloff + ", raw 5 / 99 clamp to " + Deploy308.F(SpellDeploy308ProfileSO.MaxGlowAmount) + " / "
                        + SpellDeploy308ProfileSO.MaxGlowCels + " " + clamped + " | runtime amount > 0 only inside the burst beat, 0 in hold and melt (5 letters) " + beat + notes + " | ward: 0 while it stands " + (wardStanding == 0f)
                        + ", glow ends " + Deploy308.F(wardGlowEnd) + " s <= rise end " + Deploy308.F(wardRiseEnd) + " s | InkBurst308 carries the term " + burstHas + ", no glow / emission / additive / HDR elsewhere " + (dirty.Length == 0) + dirty
                        + " | body at birth " + Deploy308.F(Mathf.LinearToGammaSpace(birth)) + " on screen (linear " + Deploy308.F(birth) + "), ceiling linear " + Deploy308.F(SpellDeploy308ProfileSO.GlowOutputCeiling)
                        + " < bloom knee " + (float.IsPositiveInfinity(knee) ? "none" : Deploy308.F(knee)) + " " + underKnee + " [lowest threshold " + (float.IsPositiveInfinity(lowest) ? "-" : Deploy308.F(lowest)) + ": " + lowestAt + "]");
                }

                // ---- D9 (D308-10c): impact frames only for a spell that lands on a groggy enemy
                {
                    int calm = 0, groggyAttack = 0, groggyOther = 0, askedTarget = 0, askedPlan = 0, rows = 0; var wrong = new StringBuilder();
                    char install = default;
                    // the rule under test is the default one; an asset left on Always (comparison captures) is reported and fails the line
                    var assetTrigger = profile.Impact.Trigger; profile.Impact.Trigger = DeployImpactTrigger308.GroggyOnly;
                    foreach (var row in map.Rows)
                    {
                        bool isAttack = row.Category == DeployCategory308.AttackSingle || row.Category == DeployCategory308.AttackArea;
                        if (row.Category == DeployCategory308.ComboInstall && install == default) install = row.Char;
                        // the target is not groggy: nothing is asked for - no full-screen frame, no local form, no cut pause
                        var host = new FakeHost { Pause = .05f };
                        // the fake host lends no buffer, so Configure would refuse: run hostless for the mesh, then replay the clock with the host's counters
                        var runtime = Runtime(root, profile, map, row.Char, 1f, .5f, host, out _, judged.transform);
                        for (float t = 0f; t < 3f; t += cel * .5f) runtime.Sample(t);
                        calm += host.Impacts;
                        if (host.Impacts != 0 || runtime.PauseSeconds != 0f) wrong.Append(row.Letter);
                        Kill(runtime);
                        // the target is groggy: exactly one request from an attack row, none (and no question) from any other row
                        host = new FakeHost { Groggy = true, Pause = .05f };
                        runtime = Runtime(root, profile, map, row.Char, 1f, .5f, host, out _, judged.transform);
                        for (float t = 0f; t < 3f; t += cel * .5f) runtime.Sample(t);
                        if (isAttack)
                        {
                            groggyAttack += host.Impacts;
                            if (row.Category == DeployCategory308.AttackArea) askedPlan += host.GroggyWithPlan; else askedTarget += host.GroggyWithTarget;
                            if (host.Impacts != 1 || host.GroggyAsked != 1 || !(runtime.PauseSeconds > 0f)) wrong.Append(row.Letter);
                        }
                        else { groggyOther += host.Impacts; if (host.Impacts != 0 || host.GroggyAsked != 0) wrong.Append(row.Letter); }
                        rows++;
                        Kill(runtime);
                    }
                    // the combo trigger: only when the triggered target is groggy, always 3 frames; never with the switch off
                    int Trigger(bool groggy, out int frames)
                    {
                        var host = new FakeHost { Groggy = groggy };
                        var runtime = Runtime(root, profile, map, install, 0f, .5f, host, out _, judged.transform, true);
                        for (float t = 0f; t < 3f; t += cel * .5f) runtime.Sample(t);
                        frames = host.ImpactFrames; Kill(runtime);
                        return host.Impacts;
                    }
                    bool keepCombo = profile.Impact.ComboTrigger; profile.Impact.ComboTrigger = true;
                    int trigYes = Trigger(true, out int trigFrames), trigNo = Trigger(false, out _);
                    profile.Impact.ComboTrigger = false; int trigOff = Trigger(true, out _);
                    profile.Impact.ComboTrigger = keepCombo;
                    // Trigger = Always: the D308-10 behaviour (comparison captures) - every attack row asks, groggy or not
                    profile.Impact.Trigger = DeployImpactTrigger308.Always;
                    int always = 0, alwaysOther = 0;
                    foreach (var row in map.Rows)
                    {
                        var host = new FakeHost();
                        var runtime = Runtime(root, profile, map, row.Char, 1f, .5f, host, out _, judged.transform);
                        for (float t = 0f; t < 3f; t += cel * .5f) runtime.Sample(t);
                        if (row.Category == DeployCategory308.AttackSingle || row.Category == DeployCategory308.AttackArea) always += host.Impacts; else alwaysOther += host.Impacts;
                        Kill(runtime);
                    }
                    profile.Impact.Trigger = assetTrigger;
                    Check("D9", assetTrigger == DeployImpactTrigger308.GroggyOnly && calm == 0 && askedTarget == 25 && askedPlan == 25 && trigNo == 0 && groggyAttack == 50 && groggyOther == 0 && wrong.Length == 0
                        && trigYes == 1 && trigFrames == 3 && trigOff == 0 && always == 50 && alwaysOther == 0,
                        rows + " rows, the profile's Impact.Trigger = " + assetTrigger + " (want GroggyOnly): target not groggy -> requests " + calm + " (want 0, no cut pause) | groggy -> attack rows " + groggyAttack + " (want 50), other rows " + groggyOther
                        + " (want 0); asked about the judged target " + askedTarget + " (25 single), about the plan " + askedPlan + " (25 area)" + (wrong.Length > 0 ? " wrong: " + wrong : "")
                        + " | combo trigger '" + install + "': groggy " + trigYes + " request, " + trigFrames + " frames; not groggy " + trigNo + "; switch off " + trigOff
                        + " | Trigger=Always: attack rows " + always + " (want 50), other rows " + alwaysOther);
                }

                // ---- D10: the flip limiter under a fake clock
                {
                    int WorstWindow(float perSecond, out int granted)
                    {
                        var limiter = new ImpactLimiter308(); limiter.Configure(profile.Impact.TokenCapacity, perSecond, profile.Impact.MinInterval); limiter.Reset();
                        var grants = new List<double>();
                        for (int i = 0; i < 40; i++) { double now = 10.0 + i * .1; if (limiter.TryTake(now)) grants.Add(now); }   // 10 requests per second for 4 s
                        granted = grants.Count;
                        int worst = 0;
                        foreach (double g in grants) worst = Math.Max(worst, grants.Count(x => x >= g && x < g + 1.0));
                        return worst;
                    }
                    int data = WorstWindow(profile.FlipsPerSecond, out int grantedData), nine = WorstWindow(9f, out int grantedNine);
                    Check("D10", data <= Mathf.FloorToInt(profile.FlipsPerSecond + .0001f) && data <= 3 && nine <= 3 && grantedData > 0,
                        "10 requests/s for 4 s: worst 1 s window " + data + " at the data rate " + Deploy308.F(profile.FlipsPerSecond) + "/s (" + grantedData + " granted), " + nine + " with 9/s asked (" + grantedNine + " granted, code ceiling 3)");
                }

                // ---- D11 / D12 / D19 / D22: the impact director with fake probes
                {
                    bool near = false;
                    var director = root.AddComponent<ImpactFrameDirector308>();
                    director.Configure(profile, DeployTier308.PC, (s, r) => near, null, null);
                    if (!director.PassAvailable)
                        sb.AppendLine("note: the impact pass is not available (ImpactFramePass308 / the App asmdef reference not deployed, or shader " + Deploy308.ImpactShader + " missing) - D11 D12 D19 D22 will FAIL until it is");
                    var request = new ImpactRequest308 { WorldPoint = Vector3.forward * 5f, Frames = 3, Strength = 1f };
                    director.SetUserSettings(DeployFlash308.Full, true, false);
                    bool idleHud = director.HudGlobal == Vector4.zero && director.ImpactGlobal == Vector4.zero;   // no request, no HUD reaction
                    var full = director.Request(request, 100.0);
                    director.Tick(100.0); int kind0 = director.FrameKind; bool swap0 = director.HudGlobal.w > .5f;
                    director.Tick(100.0 + profile.ImpactFrameSeconds * 1.5); int kind1 = director.FrameKind;
                    director.Tick(100.0 + profile.ImpactFrameSeconds * 2.5); int kind2 = director.FrameKind;
                    director.Tick(100.0 + profile.ImpactFrameSeconds * 3.5); bool ended = !director.Active && director.ImpactGlobal == Vector4.zero && director.HudGlobal == Vector4.zero;
                    director.SetUserSettings(DeployFlash308.Reduced, true, false);
                    var reduced = director.Request(request, 110.0); director.Tick(110.0); bool reducedNoSwap = director.HudGlobal.w < .5f && director.FrameKind == 2;
                    director.Tick(111.0);
                    director.SetUserSettings(DeployFlash308.Off, true, false);
                    var off = director.Request(request, 120.0);
                    var old = JsonUtility.FromJson<UserSettingsData>("{\"Version\":1,\"MasterVolume\":1,\"ReducedMotion\":false}");
                    Check("D11", full == ImpactForm308.Full && reduced == ImpactForm308.Local && off == ImpactForm308.None && kind0 == 1 && kind1 == 2 && kind2 == 3 && swap0 && ended && reducedNoSwap
                        && idleHud && old.ImpactFlash == 0 && old.HudImpactReact,
                        "no request -> HUD globals 0 " + idleHud + " | full -> " + full + " frames " + kind0 + "," + kind1 + "," + kind2 + " (invert, silhouette, return; value swap on frame 1 " + swap0 + ", globals 0 after " + ended + ") | reduced -> " + reduced
                        + " (no swap " + reducedNoSwap + ") | off -> " + off + " | an old settings file reads ImpactFlash " + old.ImpactFlash + " HudImpactReact " + old.HudImpactReact);

                    director.SetUserSettings(DeployFlash308.Full, true, false);
                    near = true; int gateBefore = director.SuppressedByGate;
                    var gated = director.Request(request, 130.0); director.Tick(131.0);
                    near = false;
                    float keepFrame = profile.Impact.FrameSeconds; profile.Impact.FrameSeconds = .1f;
                    bool frameClamped = profile.ImpactTotalSeconds(3) <= .1265f;   // a raw .1 s frame still totals <= 126 ms
                    profile.Impact.FrameSeconds = keepFrame;
                    float total = profile.ImpactTotalSeconds(3);
                    float window = 0f; string windowSource = "no CombatConfigSO asset found";
                    foreach (string guid in AssetDatabase.FindAssets("t:CombatConfigSO"))
                    {
                        var config = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(AssetDatabase.GUIDToAssetPath(guid));
                        if (config == null) continue;
                        if (window <= 0f || config.ParryWindow < window) { window = config.ParryWindow; windowSource = AssetDatabase.GUIDToAssetPath(guid); }
                    }
                    Check("D12", gated == ImpactForm308.Local && director.SuppressedByGate == gateBefore + 1 && frameClamped && total <= .1265f && window > 0f && total <= window * profile.Impact.MaxWindowShare + .0005f,
                        "judgement near -> " + gated + ", suppressed count +" + (director.SuppressedByGate - gateBefore) + " | 3 frames = " + Deploy308.F(total * 1000f) + " ms (limit 126, raw .1 s frame clamps " + frameClamped + ") = "
                        + (window > 0f ? Deploy308.F(total / window * 100f) + " % of the shortest parry window " + Deploy308.F(window) + " s" : "?") + " (limit " + Deploy308.F(profile.Impact.MaxWindowShare * 100f) + " %) [" + windowSource + "]");

                    bool refusedUnlisted = !director.RequestFlood("boss-finish");
                    var allowed = profile.Flood.AllowedEvents;
                    profile.Flood.AllowedEvents = new[] { "unit" };
                    near = true; bool refusedNear = !director.RequestFlood("unit"); near = false;
                    profile.Flood.AllowedEvents = allowed;
                    int listed = asset != null && asset.Flood.AllowedEvents != null ? asset.Flood.AllowedEvents.Length : 0;
                    Check("D19", refusedUnlisted && refusedNear && listed == 0, "flood: an event off the list refused " + refusedUnlisted + ", a listed event near a judgement window dropped " + refusedNear
                        + " | events on the asset's list " + listed + " (phase 1 = 0)" + (asset == null ? " (no asset yet: code default)" : ""));

                    var mobileDirector = root.AddComponent<ImpactFrameDirector308>();
                    mobileDirector.Configure(profile, DeployTier308.Mobile, (s, r) => false, null, null);
                    mobileDirector.SetUserSettings(DeployFlash308.Full, true, false);
                    var mobileForm = mobileDirector.Request(request, 200.0); mobileDirector.Tick(200.0);
                    Check("D22", mobileForm == ImpactForm308.Local && !mobile.FullScreen && mobile.BoldMax <= 4 && !mobile.Fine && mobile.NeedleMax == 4 && Mathf.Approximately(mobile.CelHz, 10f)
                        && mobile.ColumnStrips == 12 && mobile.FlickerMax == 2 && !mobile.HudReactDefault && !mobile.Flood && !mobile.FovBreath && mobileDirector.HudGlobal == Vector4.zero,
                        "Mobile tier: impact -> " + mobileForm + " (no colour-copy frame), bold <= " + mobile.BoldMax + ", fine " + mobile.Fine + ", needles " + mobile.NeedleMax + ", cel " + Deploy308.F(mobile.CelHz)
                        + " Hz, column strips " + mobile.ColumnStrips + ", flicker " + mobile.FlickerMax + ", HUD reaction " + mobile.HudReactDefault + ", flood " + mobile.Flood + ", fov breath " + mobile.FovBreath);
                    mobileDirector.ForceFrame(0, default, 0f, false); director.ForceFrame(0, default, 0f, false);
                }

                // ---- D16: lifetimes and caps of the residue field, stepped with its own clock
                {
                    float keepSpell = profile.Residue.SpellLife, keepFoot = profile.Residue.FootLife, keepTail = profile.Residue.FieldTail;
                    profile.Residue.SpellLife = 1f; profile.Residue.FootLife = 9f; profile.Residue.FieldTail = 0f;
                    bool clamped = profile.SpellLife == 3f && profile.FootLife == 5f && profile.FieldTail == 3f;
                    profile.Residue.SpellLife = keepSpell; profile.Residue.FootLife = keepFoot; profile.Residue.FieldTail = keepTail;
                    bool inRange = profile.SpellLife >= 3f && profile.SpellLife <= 5f && profile.FootLife >= 3f && profile.FootLife <= 5f && profile.FieldTail >= 3f && profile.FieldTail <= 5f;
                    var field = root.AddComponent<InkResidueField308>();
                    field.UseManualClock(50f);
                    field.Configure(profile, DeployTier308.PC);
                    field.UseManualClock(50f);
                    float now = 50f; int worstSpell = 0, worstFoot = 0;
                    for (int burst = 0; burst < 20; burst++)   // 20 casts in a row, 24 marks each, plus sprinting
                    {
                        for (int i = 0; i < 24; i++)
                            field.Stamp(new ResidueStamp308 { Point = new Vector3(i * .3f, 0f, burst * .3f), Normal = Vector3.up, HasGround = true, Forward = Vector3.forward, Width = .2f, Length = .2f, Opacity = 1f, Life = profile.SpellLife });
                        for (int i = 0; i < 3; i++)
                            field.Stamp(new ResidueStamp308 { Point = new Vector3(i, 0f, -burst), Normal = Vector3.up, HasGround = true, Forward = Vector3.forward, Width = .1f, Length = .26f, Opacity = .5f, Life = profile.FootLife, Foot = true });
                        for (int frame = 0; frame < 12; frame++) { now += 1f / 60f; field.Step(now, 1f / 60f, false); }
                        field.CountAlive(out int s, out int f, out _); worstSpell = Math.Max(worstSpell, s); worstFoot = Math.Max(worstFoot, f);
                    }
                    for (int frame = 0; frame < 120 && field.Queued > 0; frame++) { now += 1f / 60f; field.Step(now, 1f / 60f, false); }
                    float last = now;
                    field.Step(last + Mathf.Max(profile.SpellLife, profile.FootLife) + .1f, .1f, false);
                    field.CountAlive(out int afterSpell, out int afterFoot, out int afterAir);
                    // D308-107: the Mobile footprint ring (raised 12 -> 20) holds at its own cap
                    var mobileHost = new GameObject("UnitMobileField") { hideFlags = Flags }; mobileHost.transform.SetParent(root.transform, false);
                    var mobileField = mobileHost.AddComponent<InkResidueField308>();
                    mobileField.UseManualClock(50f); mobileField.Configure(profile, DeployTier308.Mobile); mobileField.UseManualClock(50f);
                    float mobileNow = 50f; int worstMobileFoot = 0;
                    for (int step = 0; step < 40; step++)
                    {
                        mobileField.Stamp(new ResidueStamp308 { Point = new Vector3(step * .1f, 0f, -step), Normal = Vector3.up, HasGround = true, Forward = Vector3.forward, Width = .1f, Length = .26f, Opacity = .5f, Life = profile.FootLife, Foot = true });
                        for (int frame = 0; frame < 6; frame++) { mobileNow += 1f / 60f; mobileField.Step(mobileNow, 1f / 60f, false); }
                        mobileField.CountAlive(out _, out int mf, out _); worstMobileFoot = Math.Max(worstMobileFoot, mf);
                    }
                    mobileField.ReleaseResources(); Object.DestroyImmediate(mobileHost);
                    Check("D16", clamped && inRange && worstSpell <= pc.SpellResidue && worstFoot <= pc.Footprints && worstSpell > 0 && afterSpell + afterFoot + afterAir == 0
                        && worstMobileFoot <= mobile.Footprints && worstMobileFoot > 0,
                        "lifetimes spell " + Deploy308.F(profile.SpellLife) + " foot " + Deploy308.F(profile.FootLife) + " field tail " + Deploy308.F(profile.FieldTail) + " s (raw 1 / 9 / 0 clamp to 3 / 5 / 3: " + clamped
                        + ") | 20 casts: most alive spell " + worstSpell + "/" + pc.SpellResidue + ", foot " + worstFoot + "/" + pc.Footprints + " | alive at life + .1 s: " + (afterSpell + afterFoot + afterAir)
                        + " | Mobile, 40 steps at 10/s: most alive foot " + worstMobileFoot + "/" + mobile.Footprints);
                }

                // ---- D29 (D308-107, 11.37 3): the layer switch off, the footprint switch on = the footprints alone boot
                {
                    var footProfile = Object.Instantiate(profile); footProfile.hideFlags = Flags; trash.Add(footProfile);
                    footProfile.LayerEnabled = false; footProfile.Foot.Enabled = true;
                    footProfile.Contacts = new SpellDeploy308ProfileSO.ContactsSet();   // the contact switches off: footprints alone (D31 covers the contacts)
                    var host = new GameObject("UnitFootOnly") { hideFlags = Flags }; host.transform.SetParent(root.transform, false);
                    var player = new GameObject("UnitPlayer") { hideFlags = Flags }; player.transform.SetParent(host.transform, false);
                    var footDirector = host.AddComponent<SpellDeployDirector308>();
                    footDirector.Boot(footProfile, null, null, null, player.transform, null, null);
                    bool footOk = footDirector.FootOnly && footDirector.Field != null && footDirector.Foot != null && footDirector.PoolSize == 0 && footDirector.StandingSize == 0
                        && footDirector.Impact == null && footDirector.Flicker == null && footDirector.HudBinder == null && footDirector.Present == null
                        && host.GetComponents<FootprintEmitter308>().Length == 1 && host.GetComponents<InkResidueField308>().Length == 1
                        && footDirector.Foot.Blocked() != "off" && !footDirector.OwnsHitContact('가');
                    string footLine = "layer off + foot on: foot-only " + footDirector.FootOnly + ", field " + (footDirector.Field != null) + ", emitter " + host.GetComponents<FootprintEmitter308>().Length
                        + ", slots " + footDirector.PoolSize + "+" + footDirector.StandingSize + ", impact " + (footDirector.Impact != null) + ", flicker " + (footDirector.Flicker != null)
                        + ", present " + (footDirector.Present != null) + ", emitter says " + (footDirector.Foot != null ? footDirector.Foot.Blocked() ?? "laying" : "-");
                    foreach (var f in host.GetComponents<InkResidueField308>()) f.ReleaseResources();
                    Object.DestroyImmediate(host);
                    // both switches off: nothing at all
                    var offProfile = Object.Instantiate(footProfile); offProfile.hideFlags = Flags; trash.Add(offProfile); offProfile.Foot.Enabled = false;
                    var offHost = new GameObject("UnitAllOff") { hideFlags = Flags }; offHost.transform.SetParent(root.transform, false);
                    var offDirector = offHost.AddComponent<SpellDeployDirector308>();
                    offDirector.Boot(offProfile, null, null, null, offHost.transform, null, null);
                    bool offOk = offDirector.Field == null && offDirector.Foot == null && offHost.GetComponents<Component>().Length == 2;
                    Object.DestroyImmediate(offHost);
                    Check("D29", footOk && offOk, footLine + " | both off: field " + !offOk + " -> nothing added " + offOk);
                }

                // ---- D30 (D308-107, 11.37 2): the KTP contact point is where the hit meets the body (CombatLoopWiring.HitSurfacePoint)
                {
                    var method = typeof(CombatLoopWiring).GetMethod("HitSurfacePoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (method == null) Check("D30", false, "CombatLoopWiring.HitSurfacePoint not found");
                    else
                    {
                        Vector3 Surface(EnemyVitals t, Vector3? from, Vector3? aim) => (Vector3)method.Invoke(null, new object[] { t, from, aim });
                        GameObject Body(string name, Vector3 pos, Vector3 center, bool withCollider)
                        {
                            var go = new GameObject(name) { hideFlags = Flags }; go.transform.SetParent(root.transform, false); go.transform.position = pos;
                            if (withCollider) { var c = go.AddComponent<CapsuleCollider>(); c.center = center; c.height = 2f; c.radius = .4f; }
                            go.AddComponent<EnemyVitals>();
                            return go;
                        }
                        Vector3 p = new Vector3(300f, -500f, 300f);
                        var feet = Body("UnitHitFeet", p, Vector3.up, true);                       // pivot at the feet (mine boss, sinmok)
                        var mid = Body("UnitHitMid", p + Vector3.right * 20f, Vector3.zero, true);   // pivot mid-body (most world enemies)
                        var bare = Body("UnitHitBare", p + Vector3.right * 40f, Vector3.zero, false);
                        Physics.SyncTransforms();
                        float Axis(Vector3 q, Vector3 pivot) => new Vector2(q.x - pivot.x, q.z - pivot.z).magnitude;
                        var fv = feet.GetComponent<EnemyVitals>(); var mv = mid.GetComponent<EnemyVitals>(); var bv = bare.GetComponent<EnemyVitals>();
                        Vector3 pm = mid.transform.position, pb = bare.transform.position;
                        // front shot at chest height on the feet-pivot body: the ray to pivot + 1.1 meets the near side of the capsule
                        Vector3 front = Surface(fv, p + new Vector3(0f, 1f, 10f), null);
                        bool frontOk = Mathf.Abs(Axis(front, p) - .4f) < .02f && front.z - p.z > .35f && Mathf.Abs(front.y - p.y - 1.1f) < .05f;
                        // mid-body pivot: the ray to pivot + 1.1 passes over the head, the closest point on the body facing the shooter is used
                        Vector3 over = Surface(mv, pm + new Vector3(0f, .4f, 10f), null);
                        bool overOk = Mathf.Abs(Axis(over, pm) - .4f) < .02f && over.z - pm.z > .35f && Mathf.Abs(over.y - pm.y - .4f) < .05f;
                        // scatter aim: the ray goes to the given aim point instead of pivot + 1.1
                        Vector3 aimed = Surface(fv, p + new Vector3(0f, 1f, 10f), p + new Vector3(.25f, .6f, 0f));
                        bool aimOk = Mathf.Abs(Axis(aimed, p) - .4f) < .02f && aimed.x - p.x > .05f && (aimed - front).magnitude > .1f;
                        // a shot that starts inside the body: the body centre
                        Vector3 inside = Surface(fv, p + Vector3.up, null);
                        bool insideOk = (inside - feet.GetComponent<CapsuleCollider>().bounds.center).sqrMagnitude < 1e-6f;
                        // no source, or no collider: the old pivot + .8
                        Vector3 noFrom = Surface(fv, null, null), noCol = Surface(bv, pb + Vector3.forward * 10f, null);
                        bool legacyOk = (noFrom - (p + Vector3.up * .8f)).sqrMagnitude < 1e-6f && (noCol - (pb + Vector3.up * .8f)).sqrMagnitude < 1e-6f;
                        string L(Vector3 q, Vector3 pivot) => "(" + Deploy308.F(q.x - pivot.x) + ", " + Deploy308.F(q.y - pivot.y) + ", " + Deploy308.F(q.z - pivot.z) + ")";
                        Check("D30", frontOk && overOk && aimOk && insideOk && legacyOk,
                            "feet pivot, front: " + L(front, p) + " axis " + Deploy308.F(Axis(front, p)) + " ok " + frontOk + " | mid pivot, over the head: " + L(over, pm) + " ok " + overOk
                            + " | scatter aim: " + L(aimed, p) + " ok " + aimOk + " | from inside: " + L(inside, p) + " ok " + insideOk + " | no from / no collider = pivot + .8: " + legacyOk);
                        // ---- D36 (D308-108, 11.38 2): a Scatter impact .6 m beside the capsule misses the ray; the mark sits on the edge the needle passed
                        Vector3 shot = p + new Vector3(0f, 1f, 10f);
                        Vector3 right = Surface(fv, shot, p + new Vector3(.6f, 1f, 0f)), left = Surface(fv, shot, p + new Vector3(-.6f, 1f, 0f));
                        Vector3 facing = feet.GetComponent<CapsuleCollider>().ClosestPoint(shot);
                        bool rightOk = Mathf.Abs(Axis(right, p) - .4f) < .02f && right.x - p.x > .3f && (right - facing).magnitude > .3f;
                        bool leftOk = Mathf.Abs(Axis(left, p) - .4f) < .02f && left.x - p.x < -.3f && (left - facing).magnitude > .3f;
                        Check("D36", rightOk && leftOk, "needle passes right: " + L(right, p) + " axis " + Deploy308.F(Axis(right, p)) + " ok " + rightOk
                            + " | left: " + L(left, p) + " ok " + leftOk + " | facing side (old fallback): " + L(facing, p));
                        Object.DestroyImmediate(feet); Object.DestroyImmediate(mid); Object.DestroyImmediate(bare);
                    }
                }

                // ---- D18 (strengthened by D308-10c): the hit reaction redraws, the target's materials and property blocks stay as they were
                {
                    var shader = Shader.Find(Deploy308.FlatShader);
                    if (shader == null) Check("D18", false, "shader " + Deploy308.FlatShader + " is not in the project yet");
                    else
                    {
                        profile.FlatShader = shader;
                        var bodyMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? shader) { hideFlags = Flags }; trash.Add(bodyMaterial);
                        var target = new GameObject("FlickerTarget") { hideFlags = Flags }; target.transform.SetParent(root.transform, false);
                        target.transform.position = new Vector3(0f, 0f, 20f);
                        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.hideFlags = Flags; cube.transform.SetParent(target.transform, false);
                        var renderer = cube.GetComponent<MeshRenderer>(); renderer.sharedMaterial = bodyMaterial;
                        var block = new MaterialPropertyBlock(); block.SetFloat("_UnitProbe", 3f); renderer.SetPropertyBlock(block);
                        var cameraGo = new GameObject("FlickerCamera") { hideFlags = Flags }; cameraGo.transform.SetParent(root.transform, false);
                        var camera = cameraGo.AddComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 60f; camera.aspect = 16f / 9f;
                        var flicker = root.AddComponent<HitFlicker308>(); flicker.Configure(profile, DeployTier308.PC);
                        var pass = new FakePass();
                        var f0 = profile.Flicker;
                        // one hit, sampled every 2 ms: flat-value flicks (rising edges, and how long the first one stays), when the stain
                        // and the knock are drawn, and whether the stain's cover ever rises
                        int Play(double start, DeployFlash308 flash, out int flicks, out float firstOn, out float stainFrom, out float stainTo, out float knockTo, out bool stainShrinks)
                        {
                            flicks = flicker.Notify(target.transform, DamageSource.PlayerDirect, camera, flash, start, true, target.transform.position);
                            int on = 0; bool was = false; firstOn = 0f; stainFrom = -1f; stainTo = -1f; knockTo = -1f; stainShrinks = true; float cover = 2f;
                            for (double t = start; t < start + 2.0; t += .002)
                            {
                                pass.ClearFlicker(); flicker.Fill(pass, t, null);
                                bool now = pass.Flat > 0;
                                if (now && !was) on++;
                                if (now && on == 1) firstOn = (float)(t - start) + .002f;
                                was = now;
                                if (pass.Stain > 0) { if (stainFrom < 0f) stainFrom = (float)(t - start); stainTo = (float)(t - start); if (flicker.LastStainCover > cover + 1e-4f) stainShrinks = false; cover = flicker.LastStainCover; }
                                if (pass.Knock > 0) knockTo = (float)(t - start);
                            }
                            return on;
                        }
                        int small = Play(300.0, DeployFlash308.Full, out int smallFlicks, out float smallPop, out float stainFrom0, out float stainTo0, out float knockTo0, out bool shrinks); float smallHz = flicker.LastHz;
                        cube.transform.localScale = Vector3.one * 30f;   // fills the view: a big target
                        int big = Play(310.0, DeployFlash308.Full, out int bigFlicks, out _, out _, out _, out _, out _); float bigPeriod = flicker.LastPeriod, share = flicker.LastShare;
                        cube.transform.localScale = Vector3.one;
                        int reducedOn = Play(320.0, DeployFlash308.Reduced, out int reducedFlicks, out float reducedPop, out float reducedStainFrom, out _, out float reducedKnock, out _);
                        int summon = flicker.Notify(target.transform, DamageSource.Summon, camera, DeployFlash308.Full, 330.0);
                        int offCount = flicker.Notify(target.transform, DamageSource.PlayerDirect, camera, DeployFlash308.Off, 340.0);
                        pass.ClearFlicker(); flicker.Fill(pass, 340.05, null); int offDrawn = pass.Added;
                        // photosensitivity (review 10c): a hit on a small target whose flicks still run does not restart them (a volley
                        // lands every .1 s), and small targets hit together count as one area - the one that takes their total to
                        // BigShare or more is treated as a big target
                        int rehitFirst = flicker.Notify(target.transform, DamageSource.PlayerDirect, camera, DeployFlash308.Full, 350.0, true, target.transform.position);
                        float rehitRun = rehitFirst * flicker.LastPeriod;
                        int rehitSoon = flicker.Notify(target.transform, DamageSource.PlayerDirect, camera, DeployFlash308.Full, 350.0 + rehitRun * .5, true, target.transform.position);
                        int rehitAfter = flicker.Notify(target.transform, DamageSource.PlayerDirect, camera, DeployFlash308.Full, 350.0 + rehitRun + .05, true, target.transform.position);
                        bool rehitOk = rehitFirst == f0.Count && rehitSoon == 0 && rehitAfter == rehitFirst;
                        var crowd = new int[3]; float crowdShare = 0f; int crowdSmall = 0; bool crowdOk = true;
                        for (int i = 0; i < crowd.Length; i++)
                        {
                            var other = new GameObject("FlickerCrowd" + i) { hideFlags = Flags }; other.transform.SetParent(root.transform, false);
                            other.transform.position = new Vector3(0f, 0f, 20f);
                            var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.hideFlags = Flags; body.transform.SetParent(other.transform, false);
                            body.transform.localScale = Vector3.one * 8f;   // about a tenth of the view each
                            body.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
                            crowd[i] = flicker.Notify(other.transform, DamageSource.PlayerDirect, camera, DeployFlash308.Full, 360.0, true, other.transform.position);
                            crowdShare = flicker.LastShare;
                            bool wantBig = (crowdSmall + 1) * crowdShare >= f0.BigShare;
                            if (!wantBig) crowdSmall++;
                            crowdOk &= crowd[i] == flicker.CountFor(DeployFlash308.Full, wantBig);
                        }
                        crowdOk &= crowdShare < f0.BigShare;   // each of them alone is a small target
                        var after = new MaterialPropertyBlock(); renderer.GetPropertyBlock(after);
                        bool untouched = renderer.sharedMaterial == bodyMaterial && Mathf.Approximately(after.GetFloat("_UnitProbe"), 3f) && renderer.sharedMaterials.Length == 1;
                        bool popOk = Mathf.Abs(smallPop - Mathf.Max(f0.PopSeconds, f0.OnSeconds)) <= .006f;
                        bool stainOk = !f0.Stain || (stainFrom0 >= smallPop - .006f && stainTo0 <= smallPop + f0.StainSeconds + .004f && shrinks);
                        bool knockOk = !f0.Knock || (knockTo0 >= 0f && knockTo0 <= f0.KnockSeconds + .004f);
                        bool bigOk = big == f0.BigCount && bigPeriod >= profile.Impact.MinInterval - 1e-4f && bigPeriod >= 1f / f0.BigHz - 1e-4f;
                        bool reducedOk = reducedOn == f0.ReducedCount && reducedFlicks == f0.ReducedCount && (f0.ReducedCount == 0 || (reducedPop <= f0.OnSeconds + .006f && reducedKnock < 0f && (!f0.Stain || reducedStainFrom >= 0f)));
                        Check("D18", untouched && small == f0.Count && smallFlicks == f0.Count && Mathf.Approximately(smallHz, f0.Hz) && popOk && stainOk && knockOk && bigOk && reducedOk && summon == 0 && offCount == 0 && offDrawn == 0
                                && rehitOk && crowdOk,
                            "target material and property block unchanged " + untouched + " | small target " + small + " flat flicks at " + Deploy308.F(smallHz) + " Hz (data " + f0.Count + " at " + Deploy308.F(f0.Hz) + "), the first is the pop "
                            + Deploy308.F(smallPop * 1000f) + " ms (data " + Deploy308.F(f0.PopSeconds * 1000f) + ") " + popOk + " | stain drawn " + Deploy308.F(stainFrom0 * 1000f) + " - " + Deploy308.F(stainTo0 * 1000f) + " ms (after the pop, "
                            + Deploy308.F(f0.StainSeconds * 1000f) + " ms), cover never rises " + shrinks + ", radius " + Deploy308.F(flicker.LastStainRadius) + " m | knock drawn until " + Deploy308.F(knockTo0 * 1000f) + " ms (data " + Deploy308.F(f0.KnockSeconds * 1000f)
                            + ") | screen share " + Deploy308.F(share) + " -> " + big + " flicks (data " + f0.BigCount + "), period " + Deploy308.F(bigPeriod) + " s >= limiter interval " + Deploy308.F(profile.Impact.MinInterval) + " " + bigOk
                            + " | reduced: " + reducedOn + " flick of " + Deploy308.F(reducedPop * 1000f) + " ms, no knock " + (reducedKnock < 0f) + " | summon hit " + summon + ", flash off " + offCount + " (drawn " + offDrawn + ")"
                            + " | re-hit while the flicks run " + rehitSoon + " (want 0, no restart), after them " + rehitAfter + " " + rehitOk + " | three small targets hit together (share " + Deploy308.F(crowdShare) + " each, big from "
                            + Deploy308.F(f0.BigShare) + "): " + crowd[0] + " / " + crowd[1] + " / " + crowd[2] + " flicks " + crowdOk);
                    }
                }

                // ---- D31 (D308-107 d): the layer switch off, a Contacts switch on = the contacts alone boot (no slots, the KTP contact stays)
                {
                    var cProfile = Object.Instantiate(profile); cProfile.hideFlags = Flags; trash.Add(cProfile);
                    cProfile.LayerEnabled = false; cProfile.Foot.Enabled = false;
                    cProfile.Contacts = new SpellDeploy308ProfileSO.ContactsSet { Flicker = true };
                    var host = new GameObject("UnitContactOnly") { hideFlags = Flags }; host.transform.SetParent(root.transform, false);
                    var cDirector = host.AddComponent<SpellDeployDirector308>();
                    cDirector.Boot(cProfile, null, null, null, host.transform, null, null);
                    bool flickOk = cDirector.ContactOnly && !cDirector.LayerLive && !cDirector.FootOnly && cDirector.PoolSize == 0 && cDirector.StandingSize == 0
                        && cDirector.Flicker != null && cDirector.Impact != null && cDirector.HudBinder == null && cDirector.Present == null && cDirector.Field == null
                        && cDirector.Foot == null && !cDirector.OwnsHitContact('가');
                    string cLine = "flicker only: contact-only " + cDirector.ContactOnly + ", live " + cDirector.LayerLive + ", slots " + cDirector.PoolSize + "+" + cDirector.StandingSize
                        + ", flicker " + (cDirector.Flicker != null) + ", impact " + (cDirector.Impact != null) + ", field " + (cDirector.Field != null) + ", present " + (cDirector.Present != null)
                        + ", owns 가 " + cDirector.OwnsHitContact('가');
                    foreach (var c in host.GetComponents<HitFlicker308>()) c.ReleaseResources();
                    foreach (var c in host.GetComponents<ImpactFrameDirector308>()) c.ReleaseResources();
                    Object.DestroyImmediate(host);
                    // the splash switch: the present stage and the residue field come up too, still no slots
                    var sProfile = Object.Instantiate(cProfile); sProfile.hideFlags = Flags; trash.Add(sProfile);
                    sProfile.Contacts = new SpellDeploy308ProfileSO.ContactsSet { HitSplash = true };
                    var sHost = new GameObject("UnitContactSplash") { hideFlags = Flags }; sHost.transform.SetParent(root.transform, false);
                    var sDirector = sHost.AddComponent<SpellDeployDirector308>();
                    sDirector.Boot(sProfile, null, null, null, sHost.transform, null, null);
                    bool splashOk = sDirector.ContactOnly && sDirector.Present != null && sDirector.Field != null && sDirector.PoolSize == 0 && !sDirector.OwnsHitContact('가');
                    string sLine = "splash on: present " + (sDirector.Present != null) + ", field " + (sDirector.Field != null) + ", slots " + sDirector.PoolSize;
                    foreach (var c in sHost.GetComponents<SpellPresentStage308>()) c.ReleaseResources();
                    foreach (var c in sHost.GetComponents<HitFlicker308>()) c.ReleaseResources();
                    foreach (var c in sHost.GetComponents<ImpactFrameDirector308>()) c.ReleaseResources();
                    foreach (var c in sHost.GetComponents<InkResidueField308>()) c.ReleaseResources();
                    Object.DestroyImmediate(sHost);
                    Check("D31", flickOk && splashOk, cLine + " | " + sLine);
                }

                // ---- D32 (D308-106, 11.37 c): the impact frame's figure is the hit target (its renderers), not a brightness cut; the
                // contact path asks it on a groggy spell hit only
                {
                    var flat = Shader.Find(Deploy308.FlatShader);
                    var mProfile = Object.Instantiate(profile); mProfile.hideFlags = Flags; trash.Add(mProfile);
                    mProfile.FlatShader = flat;
                    var mHost = new GameObject("UnitImpactMask") { hideFlags = Flags }; mHost.transform.SetParent(root.transform, false);
                    var mDirector = mHost.AddComponent<ImpactFrameDirector308>();
                    mDirector.Configure(mProfile, DeployTier308.PC, (s, r) => false, null, null);
                    mDirector.SetUserSettings(DeployFlash308.Full, true, false);
                    var body = new GameObject("MaskTarget") { hideFlags = Flags }; body.transform.SetParent(root.transform, false); body.transform.position = new Vector3(0f, 0f, 12f);
                    for (int i = 0; i < 3; i++)
                    {
                        var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.hideFlags = Flags; part.transform.SetParent(body.transform, false);
                        part.transform.localPosition = Vector3.up * i;
                        if (i == 2) part.GetComponent<MeshRenderer>().enabled = false;   // a hidden part stays out of the figure
                    }
                    var vitals = body.AddComponent<EnemyVitals>();
                    bool ready = mDirector.MaskReady;
                    var targeted = new ImpactRequest308 { WorldPoint = body.transform.position, Frames = 2, Strength = 1f, Target = vitals };
                    var tForm = mDirector.Request(targeted, 500.0); int tCount = mDirector.MaskCount;
                    mDirector.Tick(500.0 + mProfile.ImpactFrameSeconds * 3.5); int endCount = mDirector.MaskCount; bool tEnded = !mDirector.Active;
                    var plain = mDirector.Request(new ImpactRequest308 { WorldPoint = body.transform.position, Frames = 2, Strength = 1f }, 510.0); int plainCount = mDirector.MaskCount;
                    mDirector.Tick(511.0);
                    int want = mDirector.PassAvailable ? 2 : 0;   // without the pass nothing is granted, so nothing is collected
                    bool maskOk = flat != null && ready && tCount == want && endCount == 0 && tEnded && plainCount == 0;
                    string mLine = "mask materials " + ready + " (" + Deploy308.FlatShader + (flat != null ? "" : " MISSING") + ") | target request -> " + tForm + ", figure renderers " + tCount
                        + " (want 2: the hidden part left out), after the frames " + endCount + " | no target -> " + plain + ", figure " + plainCount + " (the old cut)";
                    mDirector.ReleaseResources();
                    Object.DestroyImmediate(mHost);

                    // the contact path: layer off, ImpactFrame switch on - a groggy spell hit asks, a calm one / a summon hit / no element does not
                    var gProfile = Object.Instantiate(mProfile); gProfile.hideFlags = Flags; trash.Add(gProfile);
                    gProfile.LayerEnabled = false; gProfile.Foot.Enabled = false;
                    gProfile.Contacts = new SpellDeploy308ProfileSO.ContactsSet { ImpactFrame = true };
                    bool groggy = false;
                    var gHost = new GameObject("UnitContactImpact") { hideFlags = Flags }; gHost.transform.SetParent(root.transform, false);
                    var gDirector = gHost.AddComponent<SpellDeployDirector308>();
                    gDirector.Boot(gProfile, null, null, null, gHost.transform, null, null, (t, p, grace) => groggy);
                    var hit = typeof(SpellDeployDirector308).GetMethod("OnEnemyDamage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    int Asked(DamageSource source, Oheangbu.Core.Domain.Element? element, bool isGroggy)
                    {
                        groggy = isGroggy;
                        int before = gDirector.Impact != null ? gDirector.Impact.Requested : -1;
                        hit?.Invoke(gDirector, new object[] { new EnemyDamageResult(vitals, AttackProvenance.Create(null, source, element), 10f, 0f, false) });
                        int asked = gDirector.Impact != null ? gDirector.Impact.Requested - before : -1;
                        if (gDirector.Impact != null) gDirector.Impact.Tick(Time.unscaledTimeAsDouble + 5.0);
                        return asked;
                    }
                    var wood = (Oheangbu.Core.Domain.Element?)Oheangbu.Core.Domain.Element.Wood;
                    int askGroggy = Asked(DamageSource.PlayerDirect, wood, true), askCalm = Asked(DamageSource.PlayerDirect, wood, false);
                    int askSummon = Asked(DamageSource.Summon, wood, true), askPlain = Asked(DamageSource.PlayerDirect, null, true);
                    bool contactOk = hit != null && gDirector.ContactOnly && gDirector.Impact != null && askGroggy == 1 && askCalm == 0 && askSummon == 0 && askPlain == 0;
                    string gLine = "contact path: groggy spell hit asks " + askGroggy + ", calm " + askCalm + ", summon " + askSummon + ", no element " + askPlain
                        + " (frames " + gProfile.Impact.ContactFrames + ")";
                    foreach (var c in gHost.GetComponents<HitFlicker308>()) c.ReleaseResources();
                    foreach (var c in gHost.GetComponents<ImpactFrameDirector308>()) c.ReleaseResources();
                    Object.DestroyImmediate(gHost);
                    Check("D32", maskOk && contactOk, mLine + " | " + gLine);
                }

                // ---- D33 (D308-104 e, 11.37 6): a range-ㄱ letter leaves one foot stain per struck enemy instead of a pool per hit
                {
                    const string V2 = "Assets/_Project/Art/SpellVFX308/V2";
                    bool IsK(string glyph) => !string.IsNullOrEmpty(glyph) && glyph[0] >= 0xAC00 && glyph[0] <= 0xD7A3 && (glyph[0] - 0xAC00) % 28 == 1;
                    bool Contract(GameObject g)
                    {
                        var ps = g != null ? g.GetComponent<ParticleSystem>() : null;
                        return ps != null && !ps.main.loop && g.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 && g.GetComponentsInChildren<Collider>(true).Length == 0
                            && g.GetComponentsInChildren<Animator>(true).Length == 0 && g.GetComponentsInChildren<Rigidbody>(true).Length == 0;
                    }
                    // the asset side: the five range-ㄱ copies carry a stain that passes the contact pre-check, the other range copies none
                    int kSet = 0, kBad = 0, otherSet = 0; GameObject sample = null; var bad = new List<string>();
                    foreach (var guid in AssetDatabase.FindAssets("t:Vfx120Profile", new[] { V2 + "/Profiles" }))
                    {
                        var p = AssetDatabase.LoadAssetAtPath<Vfx120Profile>(AssetDatabase.GUIDToAssetPath(guid));
                        if (p == null || !p.name.EndsWith("_308b") || p.AreaRemake == Vfx120AreaRemake.None) continue;
                        if (IsK(p.Glyph))
                        {
                            if (Contract(p.AreaStainPrefab)) { kSet++; sample = sample ?? p.AreaStainPrefab; }
                            else { kBad++; bad.Add(p.Glyph); }
                        }
                        else if (p.AreaStainPrefab != null) { otherSet++; bad.Add(p.Glyph); }
                    }
                    // the range-ㄱ hit keeps its stakes and drops the pool (no Linger anywhere in it)
                    int hitK = 0, hitLinger = 0;
                    foreach (var guid in AssetDatabase.FindAssets("PF_V308b_hit_ t:GameObject", new[] { V2 + "/Prefabs" }))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (!path.EndsWith("_k.prefab")) continue;
                        hitK++;
                        var g = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (g != null && g.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Linger")) hitLinger++;
                    }
                    bool assetOk = kSet == 5 && kBad == 0 && otherSet == 0 && hitK == 5 && hitLinger == 0;
                    string aLine = "range-ㄱ copies with a stain " + kSet + " (want 5), broken " + kBad + ", other range copies with one " + otherSet
                        + (bad.Count > 0 ? " [" + string.Join(" ", bad) + "]" : "") + " | hit_*_k prefabs " + hitK + ", with a Linger " + hitLinger;

                    // the runtime side, on a wiring in a preview scene (the stain spawns there, not in the open scene)
                    bool runOk = false; string rLine = "no stain prefab to try";
                    var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                    try
                    {
                        var throttle = typeof(CombatLoopWiring).GetMethod("TrySpawnAreaStain308", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var ground = typeof(CombatLoopWiring).GetMethod("StainGround308", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var clear = typeof(CombatLoopWiring).GetMethod("ClearStains308", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var vfxField = typeof(CombatLoopWiring).GetField("_contactVfx", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var listField = typeof(CombatLoopWiring).GetField("_contacts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (throttle == null || ground == null || clear == null || vfxField == null || listField == null) rLine = "CombatLoopWiring stain members not found";
                        else if (sample != null)
                        {
                            var host = new GameObject("UnitStainWiring");
                            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, preview);
                            var wiring = host.AddComponent<CombatLoopWiring>();
                            var sProfile = ScriptableObject.CreateInstance<Vfx120Profile>(); sProfile.hideFlags = Flags; trash.Add(sProfile);
                            sProfile.Glyph = "곡"; sProfile.KtpEmphasis = true; sProfile.AreaStainPrefab = sample;
                            sProfile.AreaStainDiameter = 1.7f; sProfile.AreaStainAlpha = .2f; sProfile.AreaStainSeconds = 9f;
                            var contact = ScriptableObject.CreateInstance<KtpContactProfile>(); contact.hideFlags = Flags; trash.Add(contact);
                            contact.SpellProfiles = new[] { sProfile };
                            vfxField.SetValue(wiring, contact);
                            Vector3 q = new Vector3(-300f, -500f, 300f);
                            EnemyVitals Enemy(string name, Vector3 at)
                            {
                                var go = new GameObject(name) { hideFlags = Flags }; go.transform.SetParent(root.transform, false); go.transform.position = at;
                                return go.AddComponent<EnemyVitals>();
                            }
                            var one = Enemy("UnitStainA", q); var two = Enemy("UnitStainB", q + Vector3.right * 20f);
                            bool Try(EnemyVitals t, char letter) => (bool)throttle.Invoke(wiring, new object[] { t, letter });
                            bool first = Try(one, '곡'), again = Try(one, '곡'), other = Try(two, '곡'), plain = Try(one, '고');
                            clear.Invoke(wiring, null);
                            bool afterClear = Try(one, '곡');
                            var effects = (System.Collections.IList)listField.GetValue(wiring);
                            var made = effects.Count > 0 ? effects[0] as KtpContactEffect : null;
                            var ps = made != null ? made.GetComponentInChildren<ParticleSystem>() : null;
                            bool sized = ps != null && Mathf.Abs(ps.main.startSize.constant - 1.7f) < 1e-4f && Mathf.Abs(ps.main.startLifetime.constant - 9f) < 1e-4f
                                && Mathf.Abs(ps.main.startColor.color.a - .2f) < 1e-4f && made.transform.localScale == Vector3.one;
                            bool inPreview = made != null && made.gameObject.scene == preview;
                            float pivotY = made != null ? made.transform.position.y - q.y : float.NaN;   // no collider, no ground: the pivot + .02
                            // the ground: a slab under a capsule body, a steep slab above it is skipped, the body's own collider is skipped
                            var slab = new GameObject("UnitStainGround") { hideFlags = Flags }; slab.transform.SetParent(root.transform, false);
                            slab.transform.position = q + new Vector3(40f, -.5f, 0f); var box = slab.AddComponent<BoxCollider>(); box.size = new Vector3(6f, 1f, 6f);
                            var steep = new GameObject("UnitStainSteep") { hideFlags = Flags }; steep.transform.SetParent(root.transform, false);
                            steep.transform.SetPositionAndRotation(q + new Vector3(40f, 2.5f, 0f), Quaternion.Euler(0f, 0f, 70f)); var sBox = steep.AddComponent<BoxCollider>(); sBox.size = new Vector3(6f, .2f, 6f);
                            var walker = Enemy("UnitStainC", q + new Vector3(40f, .3f, 0f));
                            var cap = walker.gameObject.AddComponent<CapsuleCollider>(); cap.center = Vector3.up; cap.height = 2f; cap.radius = .4f;
                            Physics.SyncTransforms();
                            Vector3 g = (Vector3)ground.Invoke(wiring, new object[] { walker, .45f });
                            bool groundOk = Mathf.Abs(g.y - q.y - .02f) < .01f && Mathf.Abs(g.x - walker.transform.position.x) < .01f;
                            runOk = first && !again && other && !plain && afterClear && effects.Count == 3 && sized && inPreview && Mathf.Abs(pivotY - .02f) < .01f && groundOk;
                            rLine = "first " + first + ", same enemy again " + again + ", another enemy " + other + ", a letter without a stain " + plain + ", after clear " + afterClear
                                + " | spawned " + effects.Count + " (want 3), size/life/alpha from the profile at scale 1 " + sized + ", in the preview scene " + inPreview
                                + " | no ground: pivot + " + Deploy308.F(pivotY) + " | slab under a capsule, steep slab above: y " + Deploy308.F(g.y - q.y) + " (want .02)";
                            Object.DestroyImmediate(slab); Object.DestroyImmediate(steep);
                            Object.DestroyImmediate(one.gameObject); Object.DestroyImmediate(two.gameObject); Object.DestroyImmediate(walker.gameObject);
                        }
                    }
                    finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview); }
                    Check("D33", assetOk && runOk, aLine + " | " + rLine);
                }

                // ---- D34 (D308-107 f, 11.37 7): the live summon profiles retire the old seal / phase particles / debris; a profile
                // without the flag keeps them. FormationSeal / DissolveDebris stay assigned (the authoring checks need them).
                {
                    var live = AssetDatabase.FindAssets("t:SummonCombatProfile", new[] { "Assets/_Project/Art/World/Architecture296/Data" })
                        .Select(g => AssetDatabase.LoadAssetAtPath<Oheangbu.App.Demo.SummonCombatProfile>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x != null).ToList();
                    int flagged = live.Count(p => p.RetireLegacySummonFx308), sealKept = live.Count(p => p.FormationSeal != null), debrisKept = live.Count(p => p.DissolveDebris != null);
                    bool dataOk = live.Count == 5 && flagged == 5;
                    string dLine = "live profiles " + live.Count + " (want 5), retired " + flagged + ", seal assigned " + sealKept + ", debris assigned " + debrisKept;

                    // the runtime side: a copy of 곰 (debris path) and of 놈 (phase-particle path), flag on and off, in a preview scene
                    var runs = new List<string>(); bool runOk = true;
                    var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                    try
                    {
                        foreach (string letter in new[] { "곰", "놈" })
                        {
                            var src = live.FirstOrDefault(p => p.Letter == letter && p.PresentationPrefab != null && p.FormationSeal != null);
                            if (src == null) { runOk = false; runs.Add(letter + ": no live profile with a body and a seal"); continue; }
                            foreach (bool retire in new[] { true, false })
                            {
                                var copy = Object.Instantiate(src); copy.hideFlags = Flags; trash.Add(copy);
                                copy.RetireLegacySummonFx308 = retire;
                                var host = new GameObject("UnitSummonFx_" + letter + (retire ? "_retired" : "_legacy"));
                                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, preview);
                                try
                                {
                                    var pres = host.AddComponent<Oheangbu.App.Demo.DemoSummonPresentation>();
                                    pres.Configure(copy);
                                    int before = host.transform.childCount;
                                    var clock = new Oheangbu.App.Demo.SummonCombatClock(Mathf.Max(.1f, copy.FormationSeconds), 5f, 1f);
                                    pres.Sample(clock, 0f, 0f);
                                    bool seal = host.transform.Find("Summon_KTP_Bottom") != null;
                                    if (seal) Object.DestroyImmediate(host.transform.Find("Summon_KTP_Bottom").gameObject);   // edit mode: Sample's Destroy would refuse
                                    clock.Advance(Mathf.Max(.1f, copy.FormationSeconds) + .05f); clock.BeginDissolve(); clock.Advance(.05f);
                                    pres.Sample(clock, 0f, 0f);
                                    bool phase = host.GetComponent<Oheangbu.App.Demo.DemoSummonPhaseParticles>() != null;
                                    bool debris = host.transform.childCount > before;   // the model was the only child before the clock ran (놈's phase particles parent their debris here too)
                                    bool want = !retire;
                                    bool ok = seal == want && phase == (want && letter == "놈")
                                        && (retire ? !debris : letter == "놈" || debris == (copy.DissolveDebris != null));
                                    runOk &= ok;
                                    runs.Add(letter + (retire ? " retired" : " legacy") + ": seal " + seal + ", phase particles " + phase + ", debris " + debris + (ok ? "" : " FAIL"));
                                }
                                finally { Object.DestroyImmediate(host); }
                            }
                        }
                    }
                    finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview); }
                    Check("D34", dataOk && runOk, dLine + " | " + string.Join("; ", runs));
                }

                // ---- D35 (D308-104 g, 11.37 8): the builder size caps - band = min(.32 r, 1.0 m), mark radius <= r + 1.8 m.
                // The mock's mark / r ratios (size_basis: 고 1.556, 곡 1.583, 몽 1.615) give the wanted mark; 노 ring stays as frozen.
                {
                    var rows = new[] { ("go", 2.5f, 1.556f, .80f, 3.89f), ("gok", 3.5f, 1.583f, 1f, 5.3f), ("mong", 6f, 1.615f, 1f, 7.8f), ("no", 3.09f, 1.576f, .989f, 4.87f) };
                    bool capOk = true; var parts = new List<string>();
                    foreach (var (key, r, ratio, wantBand, wantMark) in rows)
                    {
                        float band = Oheangbu.EditorTools.SpellVFX120.Vfx308Build.AreaBand308(r);
                        float mark = Oheangbu.EditorTools.SpellVFX120.Vfx308Build.AreaMark308(r, r * ratio);
                        bool ok = Mathf.Abs(band - wantBand) < .005f && Mathf.Abs(mark - wantMark) < .01f && band <= 1f + 1e-4f && mark <= r + 1.8f + 1e-4f;
                        capOk &= ok;
                        parts.Add(key + " r" + r.ToString("0.##") + " band " + band.ToString("0.###") + " mark " + mark.ToString("0.##") + (ok ? "" : " (want " + wantBand + " / " + wantMark + ")"));
                    }
                    // 11.42 4: the built bodies - path Head band depth <= 1.0 m, circle Pool rim (end size x end grow x .85 / 2) <= 1.8 m past its point
                    int seen = 0;
                    foreach (var guid in AssetDatabase.FindAssets("PF_V308b_ t:GameObject", new[] { "Assets/_Project/Art/SpellVFX308/V2/Prefabs" }))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid), name = System.IO.Path.GetFileNameWithoutExtension(path);
                        bool isPath = name.StartsWith("PF_V308b_path_"), isCircle = name.StartsWith("PF_V308b_circle_");
                        if (!isPath && !isCircle) continue;
                        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (go == null) continue;
                        var layer = go.transform.Find(isPath ? "Head" : "Pool")?.GetComponent<ParticleSystem>(); if (layer == null) continue;
                        seen++;
                        if (isPath)
                        {
                            float depth = layer.shape.scale.z;
                            if (depth > Oheangbu.EditorTools.SpellVFX120.Vfx308Build.AreaBandCap308 + 1e-3f) { capOk = false; parts.Add(name + " Head depth " + depth.ToString("0.##") + " > 1.0"); }
                        }
                        else
                        {
                            var sol = layer.sizeOverLifetime; float grow = sol.enabled ? sol.size.Evaluate(1f) * sol.size.curveMultiplier : 1f;
                            float rim = layer.main.startSize.constantMax * grow * .85f * .5f;
                            if (rim > Oheangbu.EditorTools.SpellVFX120.Vfx308Build.AreaMarkMargin308 + 1e-3f) { capOk = false; parts.Add(name + " Pool rim " + rim.ToString("0.##") + " > 1.8"); }
                        }
                    }
                    parts.Add(seen > 0 ? "built bodies " + seen : "no built bodies");
                    Check("D35", capOk, string.Join("; ", parts));
                }

                // ---- D37 (D308-108, 11.38 4): the summon core lights for the first two cels of activity only (B), the shader's _Age window is gone
                {
                    var sp = ScriptableObject.CreateInstance<Oheangbu.App.Demo.SummonCombatProfile>(); sp.hideFlags = Flags; trash.Add(sp);
                    float secs = sp.CoreBirthGlowSeconds308;
                    var clock = new Oheangbu.App.Demo.SummonCombatClock(1.2f, 20f, .8f);
                    clock.Advance(.5f); float forming = Oheangbu.App.Demo.DemoSummonPresentation.CoreBirthGlow(clock, secs);
                    clock.Advance(.75f); float born = Oheangbu.App.Demo.DemoSummonPresentation.CoreBirthGlow(clock, secs);
                    float off0 = Oheangbu.App.Demo.DemoSummonPresentation.CoreBirthGlow(clock, 0f);
                    clock.Advance(.15f); float after = Oheangbu.App.Demo.DemoSummonPresentation.CoreBirthGlow(clock, secs);
                    const string bodyShader = "Assets/_Project/Shaders/SpellVFX308/InkBody308.shader";
                    string src = File.Exists(bodyShader) ? File.ReadAllText(bodyShader) : null;
                    bool shaderOk = src != null && !src.Contains("step(1.1, _Age)") && src.Contains("half lift = saturate(_CoreGlow);");
                    bool ok = Mathf.Abs(secs - 1f / 6f) < 1e-4f && forming == 0f && born == 1f && off0 == 0f && after == 0f && shaderOk;
                    Check("D37", ok, "default " + Deploy308.F(secs) + " s | forming " + forming + ", activity .05 s " + born + ", seconds 0 " + off0
                        + ", activity .2 s " + after + " | shader: no _Age window, lift = _CoreGlow " + shaderOk);
                }

                // ---- D38 (11.43): the ward ripple sits on the membrane toward an attacker outside the ring; inside = nothing crossed
                {
                    var c = new Vector3(10f, 2f, -4f); const float r = 3f, h = 2.2f;
                    bool outOk = EAWardRuntime.RippleContact(c, r, h, c + new Vector3(8f, 0f, 6f), out var p, out var n);
                    float flat = new Vector2(p.x - c.x, p.z - c.z).magnitude, up = p.y - c.y;
                    outOk &= Mathf.Abs(flat - r) < 1e-3f && up >= .4f - 1e-4f && up <= h - .2f + 1e-4f && Vector3.Dot(n, new Vector3(.8f, 0f, .6f)) > .999f;
                    bool highOk = EAWardRuntime.RippleContact(c, r, h, c + new Vector3(-9f, 6f, 0f), out var ph, out _) && Mathf.Abs(ph.y - c.y - (h - .2f)) < 1e-4f;
                    bool inOk = !EAWardRuntime.RippleContact(c, r, h, c + new Vector3(1.5f, 0f, 1f), out _, out _);
                    Check("D38", outOk && highOk && inOk, "outside: on ring " + Deploy308.F(flat) + " m, up " + Deploy308.F(up) + " m ok " + outOk
                        + " | high attacker clamped " + highOk + " | inside refused " + inOk);
                }

                // ---- D39 (11.44): the haetae ink breath body takes the plan's cone angle and range scale on the manual clock, sprays,
                // is gone after EndAt; the built body (if any) draws no additive material
                {
                    var sp = ScriptableObject.CreateInstance<Oheangbu.App.Demo.SummonCombatProfile>(); sp.hideFlags = Flags; trash.Add(sp);
                    var body = new GameObject("D39 ink breath body") { hideFlags = Flags }; trash.Add(body);
                    var bps = body.AddComponent<ParticleSystem>(); var bm = bps.main; bm.playOnAwake = false; bm.startLifetime = .3f; bm.startSpeed = 5f;
                    var be = bps.emission; be.rateOverTime = 40f; var bs = bps.shape; bs.shapeType = ParticleSystemShapeType.Cone; bs.angle = 10f; bs.radius = .1f;
                    sp.FlameInk308Prefab = body; sp.FlameInk308AuthoredRange = 4.5f;
                    var plan = new Oheangbu.App.Demo.SummonFlameAttackPlan(Vector3.zero, Vector3.forward, 6f, 25f, 1f, 0f, .2f, .65f, .3f);
                    var holder = new GameObject("D39 holder") { hideFlags = Flags }; trash.Add(holder);
                    var vis = holder.AddComponent<Oheangbu.App.Demo.DemoSummonFlamePresentation>();
                    string err = null; float angle = -1f, scale = -1f; int peak = 0, after = -1, children = -1;
                    try
                    {
                        vis.Sample(sp, plan, 0f, true);
                        var inst = holder.transform.childCount > 0 ? holder.transform.GetChild(0) : null;
                        if (inst != null) { scale = inst.localScale.x; var ips = inst.GetComponentInChildren<ParticleSystem>(true); if (ips != null) angle = ips.shape.angle; }
                        for (float t = plan.ReleaseAt; t < plan.SprayEndAt; t += 1f / 60f) { plan.AdvanceTo(t); vis.Sample(sp, plan, t, true); peak = Mathf.Max(peak, vis.LiveParticles); }
                        plan.AdvanceTo(plan.EndAt); vis.Sample(sp, plan, plan.EndAt, true); after = vis.LiveParticles; children = holder.transform.childCount;
                    }
                    catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
                    bool runOk = err == null && Mathf.Abs(angle - 25f) < 1e-3f && Mathf.Abs(scale - 6f / 4.5f) < 1e-3f && peak > 0 && after == 0 && children == 0;
                    const string breathPath = "Assets/_Project/Art/SpellVFX308/V2/Prefabs/PF_V308i_haetae_breath.prefab";
                    var built = AssetDatabase.LoadAssetAtPath<GameObject>(breathPath); int additive = 0, mats = 0;
                    if (built != null)
                        foreach (var r in built.GetComponentsInChildren<ParticleSystemRenderer>(true))
                            foreach (var m in r.sharedMaterials)
                            {
                                if (m == null) continue; mats++;
                                bool add = m.shader.name.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0
                                    || (m.HasProperty("_DstBlend") && Mathf.Approximately(m.GetFloat("_DstBlend"), (float)BlendMode.One));
                                if (add) additive++;
                            }
                    bool builtOk = built == null || (mats > 0 && additive == 0);
                    Check("D39", runOk && builtOk, (err ?? "no exception") + " | cone " + Deploy308.F(angle) + " deg (plan 25), scale " + Deploy308.F(scale)
                        + " (6/4.5), spray peak " + peak + ", after EndAt " + after + " live / " + children + " bodies | built body "
                        + (built == null ? "not built" : mats + " materials, additive " + additive));
                }

                // ---- D40 (11.45): the ㄱ-final copies carry one variation each (각 낙 follow, 삭 pierce, 악 return) and their own ink
                // body; a body draws no ground-flat layer and no additive material, the pierce body has an empty Contact, and the
                // runtime hands the return leg to the effect
                {
                    const string v2 = "Assets/_Project/Art/SpellVFX308/V2";
                    var rows = new[] { ("002_AC01", "ga"), ("026_B099", "na"), ("074_C0AD", "sa"), ("098_C545", "a") };
                    var parts = new List<string>(); bool ok = true; int seen = 0;
                    foreach (var (id, key) in rows)
                    {
                        var p = AssetDatabase.LoadAssetAtPath<Oheangbu.App.SpellVFX120.Vfx120Profile>(v2 + "/Profiles/" + id + "_308e.asset");
                        if (p == null) continue; seen++;
                        bool flags = p.Bolt300Pierce == (key == "sa") && p.Bolt300Return == (key == "a") && p.Bolt300FollowTarget == (key is "ga" or "na")
                            && Mathf.Abs(p.Bolt300FollowHeight - (key == "na" ? 1f : 0f)) < 1e-4f;
                        var body = p.Bolt300Prefab; bool own = body != null && body.name == "PF_V308j_giyeok_" + key;
                        int flat = 0, additive = 0, contactPs = 0;
                        if (body != null)
                        {
                            foreach (var r in body.GetComponentsInChildren<ParticleSystemRenderer>(true))
                            {
                                if (r.renderMode == ParticleSystemRenderMode.HorizontalBillboard) flat++;
                                foreach (var m in r.sharedMaterials)
                                    if (m != null && (m.shader.name.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0
                                        || (m.HasProperty("_DstBlend") && Mathf.Approximately(m.GetFloat("_DstBlend"), (float)BlendMode.One)))) additive++;
                            }
                            var contact = body.transform.Find("Contact"); if (contact != null) contactPs = contact.GetComponentsInChildren<ParticleSystem>(true).Length;
                        }
                        bool contactOk = key == "sa" ? contactPs == 0 : contactPs > 0;
                        bool rowOk = flags && own && flat == 0 && additive == 0 && contactOk; ok &= rowOk;
                        parts.Add(key + (rowOk ? " ok" : " FAIL") + " (flags " + flags + ", own body " + own + ", flat " + flat + ", additive " + additive + ", contact systems " + contactPs + ")");
                    }
                    const string traces = "Assets/_Project/Scripts/App/EAGiyeokRuntime.Traces.cs";
                    bool wired = File.Exists(traces) && File.ReadAllText(traces).Contains("SetReturn308(t.Bounce");
                    ok &= wired;
                    Check("D40", ok, (seen > 0 ? string.Join("; ", parts) : "not built") + " | runtime hands the return leg " + wired);
                }

                // ---- D26 (D308-10c): the hit splash comes only with a CONFIRMED hit
                {
                    float[] grades = { 0f, .5f, 1f };
                    int[] missAir = new int[3], hitAir = new int[3], want = new int[3];
                    int missStill = 0, hitStill = 0, missSplash = 0, earlyStill = -1; float missStamp = 0f, hitStamp = 0f, missSize = 0f, hitSize = 0f, slowest = float.PositiveInfinity;
                    bool refusedBystander = false, tookTarget = false, outward = true;
                    var wood = profile.ElementOf(Oheangbu.Core.Domain.Element.Wood);
                    for (int g = 0; g < 3; g++)
                    {
                        // a miss: no hit is confirmed, the burst is the plain one
                        var miss = new FakeHost();
                        var runtime = Runtime(root, profile, map, '가', grades[g], .5f, miss, out _, judged.transform);
                        for (float t = 0f; t < 3f; t += cel * .5f) runtime.Sample(t);
                        missAir[g] = miss.Air; missSplash += runtime.HitSplashes;
                        if (g == 1) { missStill = miss.Still; missStamp = miss.MaxStamp; missSize = miss.Air > 0 ? miss.AirSize / miss.Air : 0f; }
                        Kill(runtime);
                        // the same cast with its hit confirmed before the burst's first cel: the splash waits for that cel
                        var hit = new FakeHost();
                        runtime = Runtime(root, profile, map, '가', grades[g], .5f, hit, out _, judged.transform);
                        runtime.Sample(0f);
                        bool refused = !runtime.ConfirmHit(bystander.transform), took = runtime.ConfirmHit(judged.transform);
                        if (g == 1) { refusedBystander = refused; tookTarget = took; earlyStill = hit.Still; }
                        for (float t = 0f; t < 3f; t += cel * .5f) runtime.Sample(t);
                        hitAir[g] = hit.Air;
                        want[g] = Mathf.RoundToInt(profile.Hit.AirDrops * profile.Hit.Scale(g) * Mathf.Clamp(wood.DropMul, .6f, 1.4f));
                        if (g == 1)
                        {
                            hitStill = hit.Still; hitStamp = hit.MaxStamp;
                            Vector3 point = judged.transform.position + Vector3.up * InkDeployRuntime308.ChestHeight; float sum = 0f; int n = 0;
                            for (int i = 0; i < runtime.HitDropCount; i++)
                            {
                                var drop = runtime.HitDrop(i);
                                if (!drop.Air || drop.Still) continue;
                                sum += drop.Size; n++;
                                slowest = Mathf.Min(slowest, drop.Velocity.magnitude);
                                if (Vector3.Dot(drop.Velocity, drop.Local - point) <= 0f) outward = false;
                            }
                            hitSize = n > 0 ? sum / n : 0f;
                        }
                        Kill(runtime);
                    }
                    // an area cast takes several hits, never more than Hit.MaxPerCast; a hostless cast takes none
                    var areaHost = new FakeHost();
                    var area = Runtime(root, profile, map, '노', .5f, .5f, areaHost, out _, judged.transform);
                    int confirmations = 0;
                    for (float t = 0f; t < 1f; t += cel * .5f) { area.Sample(t); area.ConfirmHit(t < .5f ? judged.transform : bystander.transform); confirmations++; }
                    int areaSplashes = area.HitSplashes; Kill(area);
                    var bare = Runtime(root, profile, map, '가', .5f, .5f, null, out _, judged.transform);
                    bool hostless = !bare.ConfirmHit(judged.transform) && bare.HitSplashes == 0; Kill(bare);
                    bool counts = hitAir[0] - missAir[0] == want[0] && hitAir[1] - missAir[1] == want[1] && hitAir[2] - missAir[2] == want[2] && want[0] < want[1] && want[1] < want[2];
                    Check("D26", !profile.Hit.Enabled || (missSplash == 0 && refusedBystander && tookTarget && earlyStill == 0 && counts && hitStill - missStill == 2 && hitStamp > missStamp && hitSize > missSize
                            && slowest >= profile.Hit.SpeedMin * .9f && outward && areaSplashes > 1 && areaSplashes <= profile.Hit.MaxPerCast && hostless),
                        "miss: splashes " + missSplash + " (want 0), air drops low / mid / high " + missAir[0] + " / " + missAir[1] + " / " + missAir[2] + " | confirmed hit: + " + (hitAir[0] - missAir[0]) + " / " + (hitAir[1] - missAir[1]) + " / "
                        + (hitAir[2] - missAir[2]) + " air drops (data " + want[0] + " / " + want[1] + " / " + want[2] + "), + " + (hitStill - missStill) + " standing sprites (star + blot), ground puddle " + Deploy308.F(hitStamp) + " m against "
                        + Deploy308.F(missStamp) + " m, mean drop " + Deploy308.F(hitSize) + " m against " + Deploy308.F(missSize) + " m, slowest " + Deploy308.F(slowest) + " m/s (data min " + Deploy308.F(profile.Hit.SpeedMin) + "), all outward " + outward
                        + " | a bystander's hit refused " + refusedBystander + ", the judged target's taken " + tookTarget + ", shown on the burst's first cel (sprites before it " + earlyStill + ") | area cast: " + areaSplashes
                        + " splashes for " + confirmations + " confirmations (cap " + profile.Hit.MaxPerCast + ") | hostless cast: none " + hostless);
                }

                // ---- D27 (D308-13b): which rows replace the KTP enemy-hit contact, and the basic five's policy
                {
                    string keepLetters = profile.EnabledLetters; bool keepReplace = profile.Hit.ReplaceLegacyContact;
                    profile.EnabledLetters = "가서옴"; profile.Hit.ReplaceLegacyContact = true;
                    bool attackOn = InkDeployForms308.OwnsHitContact(profile, '가'), attackOff = InkDeployForms308.OwnsHitContact(profile, '나');
                    bool parry = InkDeployForms308.OwnsHitContact(profile, '서'), summon = InkDeployForms308.OwnsHitContact(profile, '옴');
                    profile.Hit.ReplaceLegacyContact = false; bool switchOff = InkDeployForms308.OwnsHitContact(profile, '가'); profile.Hit.ReplaceLegacyContact = true;
                    profile.LayerEnabled = false; bool layerOff = InkDeployForms308.OwnsHitContact(profile, '가'); profile.LayerEnabled = true;
                    profile.EnabledLetters = keepLetters; profile.Hit.ReplaceLegacyContact = keepReplace;
                    var five = new StringBuilder(); bool basic = true;
                    foreach (char letter in "가나마사아")
                    {
                        bool found = map.TryGet(letter, out var row);
                        bool right = found && row.Category == DeployCategory308.AttackSingle && row.LegacyBody == DeployLegacyBody308.Retire && row.ImpactFrame;
                        basic &= right; five.Append(letter).Append(found ? "=" + row.LegacyBody : "=?").Append(' ');
                    }
                    Check("D27", attackOn && !attackOff && !parry && !summon && !switchOff && !layerOff && basic,
                        "KTP enemy-hit contact replaced: enabled attack row " + attackOn + " | attack row not switched on " + attackOff + ", parry row " + parry + ", summon row " + summon + ", Hit.ReplaceLegacyContact off " + switchOff
                        + ", layer off " + layerOff + " (all want False) | basic five single attacks, old body retired: " + five + basic);
                }

                // ---- D23: nothing of the layer is left behind
                {
                    var runtime = Runtime(root, profile, map, '가', 1f, .6f, null, out _);
                    var holder = runtime.gameObject;
                    int before = holder.transform.childCount;
                    runtime.Sample(runtime.Duration + 1f);
                    bool hidden = runtime.BurstRenderer != null && !runtime.BurstRenderer.enabled && runtime.BurstMesh == null && runtime.GlowNow == 0f;
                    runtime.Dispose();
                    int afterChildren = holder.transform.childCount;
                    Object.DestroyImmediate(holder);
                    Vector4 impact = Shader.GetGlobalVector("_OhImpact308"), hud = Shader.GetGlobalVector("_OhImpactHud308");
                    Check("D23", before == 1 && hidden && afterChildren <= 1 && impact == Vector4.zero && hud == Vector4.zero,
                        "burst root " + before + " -> hidden and mesh released after its timeline " + hidden + " -> disposed | globals _OhImpact308 " + impact + " _OhImpactHud308 " + hud);
                }

                sb.AppendLine("not covered here (Play): D4 cast-plan replay, D13, D14, D20 GC, D21, D23 play enter/exit, D25 object list, D28 the wiring's groggy probe, real hits for D9 / D26 / D27 | (DeployLook308): D5 captures D7 D8 D15 D24");
                sb.Append("deploy308-unit: " + checks + " checks, " + failed + " failed");
                return sb.ToString();
            }
            finally
            {
                Shader.SetGlobalVector("_OhImpact308", Vector4.zero); Shader.SetGlobalVector("_OhImpactHud308", Vector4.zero); Shader.SetGlobalFloat("_OhResidueNow", 0f);
                Shader.SetGlobalVector("_OhImpactHudPoint308", Vector4.zero);
                if (root != null)
                {
                    // edit mode never calls OnDestroy on these: release what they made by hand
                    foreach (var d in root.GetComponents<ImpactFrameDirector308>()) d.ReleaseResources();
                    foreach (var f in root.GetComponents<HitFlicker308>()) f.ReleaseResources();
                    foreach (var r in root.GetComponentsInChildren<InkResidueField308>(true)) r.ReleaseResources();
                    foreach (var runtime in root.GetComponentsInChildren<InkDeployRuntime308>(true)) runtime.Dispose();
                    Object.DestroyImmediate(root);
                }
                foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            }
        }

        // edit mode never calls OnDestroy: a runtime's own mesh and root are released by hand before its holder goes
        static void Kill(InkDeployRuntime308 runtime)
        {
            if (runtime == null) return;
            var holder = runtime.gameObject;
            runtime.Dispose();
            Object.DestroyImmediate(holder);
        }

        static string Source(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            return shader == null ? null : FileOrNull(AssetDatabase.GetAssetPath(shader));
        }

        const string Vfx308Dir = "Assets/_Project/Shaders/SpellVFX308/";
        // a #308 shader that may glow: its glow must go through the ceiling (OH_GLOW_CEILING_OUT) and be capped at .35 in its
        // properties; returns the source with _Glow taken out for the blend / HDR rule, or a marker that fails that rule
        static string Vfx308Glow(string file)
        {
            string src = FileOrNull(Vfx308Dir + file);
            if (src == null) return null;
            bool capped = src.Contains("OH_GLOW_CEILING_OUT") && src.Contains("Range(0,.35)");
            return capped ? src.Replace("_Glow", "") : "_Glow (uncapped)";
        }

        static string FileOrNull(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            string path = PostLedger308.RepoPath("Oheangbu/" + assetPath);
            return File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : null;
        }

        static string CommonInclude()
        {
            var shader = Shader.Find(Deploy308.BurstShader);
            string path = shader != null ? AssetDatabase.GetAssetPath(shader) : null;
            return string.IsNullOrEmpty(path) ? "Assets/_Project/Shaders/InkCommon308.hlsl" : Path.GetDirectoryName(path).Replace('\\', '/') + "/InkCommon308.hlsl";
        }

        // a deploy runtime for one letter with a synthetic cast: origin at the brush tip, target 6 m ahead. `target` is the judged
        // target of a single cast (and of a triggered combo burst); area rows carry a circle plan instead.
        static InkDeployRuntime308 Runtime(GameObject root, SpellDeploy308ProfileSO profile, SpellDeploy308MapSO map, char letter, float grade, float clock, FakeHost host,
            out DeployCast308 cast, Transform target = null, bool triggered = false)
        {
            if (!map.TryGet(letter, out var row)) throw new PostLedger308.Refused("'" + letter + "' is not in the map");
            var go = new GameObject("Unit_" + letter) { hideFlags = Flags };
            go.transform.SetParent(root.transform, false);
            AreaImpactPlan plan = null;
            if (row.Category == DeployCategory308.AttackArea) plan = new AreaImpactPlan { Shape = AreaShape.Circle, Point = new Vector3(0f, 0f, 6f), Direction = Vector3.forward, Radius = 3f, Delay = clock };
            bool ward = row.Category == DeployCategory308.Ward;
            cast = new DeployCast308
            {
                Profile = profile, Row = row, Origin = new Vector3(0f, 1.35f, 1.6f), FallbackPoint = new Vector3(0f, 1.1f, 6f), Plan = plan,
                Target = row.Category == DeployCategory308.AttackSingle || triggered ? target : null, Triggered = triggered,
                ImpactClock = row.Category == DeployCategory308.AttackSingle ? clock : 0f, Grade01 = grade, Tint = Color.gray, Seed = 308, Tier = DeployTier308.PC,
                HoldSeconds = ward ? 4f : 0f, WardRadius = ward ? 3f : 0f, WardHeight = ward ? 2.2f : 0f,
                CameraPosition = new Vector3(0f, 1.6f, 0f), HasCameraPosition = true,
            };
            var runtime = go.AddComponent<InkDeployRuntime308>();
            // with a counting host the runtime still builds its own mesh: the fake host lends no pooled buffer
            if (!runtime.Configure(cast, null)) throw new PostLedger308.Refused("the runtime refused letter '" + letter + "'");
            if (host != null) runtime.AttachHostForChecks(host);
            return runtime;
        }
    }
}
