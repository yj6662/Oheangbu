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
    // (AC-D2 D3 D4 D5 D9 D10 D11 D12 D19 D22 D16 D18 D26 D27 D23).
    // D308-10c: D5 = momentary glow only, D9 = impact frames only for a groggy target, D18 = the stronger hit reaction,
    // D26 = the hit splash only on a confirmed hit, D27 = which rows replace the KTP enemy-hit contact (D308-13b).
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
                    Check("D16", clamped && inRange && worstSpell <= pc.SpellResidue && worstFoot <= pc.Footprints && worstSpell > 0 && afterSpell + afterFoot + afterAir == 0,
                        "lifetimes spell " + Deploy308.F(profile.SpellLife) + " foot " + Deploy308.F(profile.FootLife) + " field tail " + Deploy308.F(profile.FieldTail) + " s (raw 1 / 9 / 0 clamp to 3 / 5 / 3: " + clamped
                        + ") | 20 casts: most alive spell " + worstSpell + "/" + pc.SpellResidue + ", foot " + worstFoot + "/" + pc.Footprints + " | alive at life + .1 s: " + (afterSpell + afterFoot + afterAir));
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
                    foreach (var r in root.GetComponents<InkResidueField308>()) r.ReleaseResources();
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
