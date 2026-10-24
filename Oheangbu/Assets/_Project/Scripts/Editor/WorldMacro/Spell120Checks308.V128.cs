using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-128 21 / D308-132 step 6 checks: the look of the five-element reactions (prefabs built by `Vfx308Build reaction`), the
    // React cue of a consumed zone, the reaction look call through the guard, the dressing the presenter gives a look. Edit Mode only:
    // the prefabs are loaded into an isolated scene (PrefabUtility.LoadPrefabContents), dressed exactly as the presenter dresses them,
    // and simulated with ParticleSystem.Simulate; nothing touches an open scene. What stays for Play: the picture itself
    // (colour against the real ground, 7 m first person, the first 0.5 s still) and the growth around the old ring - the mp4.
    public static partial class Spell120Checks308
    {
        const string V128LookAsset = "Assets/_Project/Resources/Spell308/SpellReactionLook308.asset";
        const string V128InkShader = "Oheangbu/SpellVFX308/InkBleed";
        const int V128FogLayer = 20, V128FireBudget = 48, V128SteamBudget = 32, V128TransitionBudget = 30;
        const float V128MaxHeight = 1.3f;

        // A presenter the checks can read: every call it hears, in order.
        sealed class V128Probe : ISpellPresenter, ISpellReactionPresenter308
        {
            public readonly List<SpellFxCueArgs> Reacts = new List<SpellFxCueArgs>();
            public readonly List<KeyValuePair<int, SpellFxCue>> Cues = new List<KeyValuePair<int, SpellFxCue>>();
            public readonly List<SpellFxCueArgs> CueArgs = new List<SpellFxCueArgs>();
            public readonly List<int> Ended = new List<int>();
            public SpellFxHandle Begin(in SpellFxRequest request) => new SpellFxHandle(1);
            public void Cue(SpellFxHandle handle, SpellFxCue cue, in SpellFxCueArgs args) { Cues.Add(new KeyValuePair<int, SpellFxCue>(handle.Id, cue)); CueArgs.Add(args); }
            public void End(SpellFxHandle handle, bool immediate = false) { Ended.Add(handle.Id); }
            public void EndAll() { }
            public void React(in SpellFxCueArgs args) { Reacts.Add(args); }
        }

        // An owner that lays one zone and tells its presenter how it ended (the real Retire path of ZoneEffect308).
        sealed class V128Owner : ZoneEffect308
        {
            public V128Owner(SpellZoneRuntime308 zones) : base(zones) { }
            public override string Id => "zone.v128";
            public override IReadOnlyList<SpellParamSpec> Params => System.Array.Empty<SpellParamSpec>();
            public override bool Commit(in SpellCastContext ctx) => false;
            protected override void Act(in SpellTickContext ctx) { }
            public int Lay(SpellZone308 zone, ISpellPresenter fx) => Place(zone, 5, zone.From, fx);
        }

        static partial void FixturesV128(Report r)
        {
            if (!r.Wants("농노오곡")) return;
            const float room = .02f;
            var forward = new Vector3(1f, 0f, 1f).normalized;

            // ---- the end reason: Reacted is told as React (with the way the wave ran), never as Break; expiry stays Expire
            {
                var zones = new SpellZoneRuntime308(); var owner = new V128Owner(zones); var fx = new V128Probe();
                float now = 100f; var centre = new Vector3(5f, 0f, 2f);
                var zone = new SpellZone308 { Owner = owner.Id, Cast = zones.NextCast(), Letter = '곡', Element = Element.Wood, Centre = centre, Radius = 2f, From = now - 1f, Until = now + 5f, Life = 6f, Fx = 7 };
                int id = owner.Lay(zone, fx);
                bool first = zones.React(id, now, forward), again = zones.React(id, now, forward);
                owner.Tick(new SpellTickContext(now, .02f, null, fx));
                r.Check("V128-end", id > 0 && first && !again && zones.Zones.All(z => z.Id != id) && fx.Cues.Count == 1 && fx.Cues[0].Key == 7 && fx.Cues[0].Value == SpellFxCue.React &&
                    Vector3.Dot(fx.CueArgs[0].Direction, forward) > .999f && Near(fx.CueArgs[0].Value, 2f) && fx.CueArgs[0].Point == centre && fx.Ended.Count == 0,
                    "a consumed zone is swept as Reacted and its floor is told React once: handle, the wave's direction, the zone's centre and radius - no Break, no End");
                var late = new SpellZone308 { Owner = owner.Id, Cast = zones.NextCast(), Letter = '곡', Element = Element.Wood, Centre = centre, Radius = 2f, From = now, Until = now + 1f, Life = 1f, Fx = 8 };
                owner.Lay(late, fx);
                owner.Tick(new SpellTickContext(now + 2f, .02f, null, fx));
                r.Check("V128-end", fx.Cues.Count == 2 && fx.Cues[1].Key == 8 && fx.Cues[1].Value == SpellFxCue.Expire && fx.CueArgs[1].Direction == Vector3.zero,
                    "a zone that runs out is still told Expire (no direction: nothing reacted)");
            }

            // ---- the cue and the call as the guard carries them: held with the cast, dropped with a withdrawn cast
            {
                var inner = new V128Probe(); var guard = new SpellPresenterGuard308(); guard.Bind(inner);
                var args = SpellReaction308.Args(ReactionKind308.Wildfire, new Vector3(1f, 0f, 2f), forward, 2f, 3f, 5f);
                guard.Hold(); guard.React(args);
                bool waits = inner.Reacts.Count == 0 && guard.WaitingCalls == 1;
                guard.Release(true);
                bool shown = inner.Reacts.Count == 1 && inner.Reacts[0].Kind == ReactionKind308.Wildfire && inner.Reacts[0].Point == args.Point && Near(inner.Reacts[0].Value, 3f) && Near(inner.Reacts[0].FromRadius, 2f) && Near(inner.Reacts[0].Life, 5f);
                guard.Hold(); guard.React(args); guard.Release(false);
                bool dropped = inner.Reacts.Count == 1;
                guard.React(args);
                var plain = new SpellPresenterGuard308(); plain.Bind(new NullSpellPresenter308()); plain.React(args);
                r.Check("V128-seam", waits && shown && dropped && inner.Reacts.Count == 2 && plain.WaitingCalls == 0 && plain.Faults == 0,
                    "the guard holds a reaction look with its cast, shows it when the cast stands, drops it with a withdrawn one; a presenter without the seam hears nothing");
                r.Check("V128-seam", typeof(ISpellReactionPresenter308).IsAssignableFrom(typeof(SpellDeployPresenter308)) && typeof(ISpellReactionPresenter308).IsAssignableFrom(typeof(Vfx120SpellPresenter308)) &&
                    typeof(ISpellReactionPresenter308).IsAssignableFrom(typeof(SpellPresenterGuard308)), "the guard, the deploy presenter and the interim presenter all carry the reaction seam");
                var presenter = new Vfx120SpellPresenter308();
                var bare = ScriptableObject.CreateInstance<SpellReactionLook308SO>();
                bare.Entries = new[] { new SpellReactionLook308SO.Entry { Kind = ReactionKind308.Mud, Prefab = null } };
                presenter.BindReactionLook(bare);
                presenter.React(SpellReaction308.Args(ReactionKind308.Mud, Vector3.zero, Vector3.forward, 2f, 2f, 3f));
                presenter.React(SpellReaction308.Args(ReactionKind308.Mud, Vector3.zero, Vector3.forward, 2f, 2f, 3f));
                presenter.React(SpellReaction308.Args(ReactionKind308.Wildfire, Vector3.zero, Vector3.forward, 2f, 3f, 3f));   // not in this look at all
                presenter.React(default);                                                                                       // None
                r.Check("V128-mud", presenter.ReactionsShown == 0 && presenter.ReactionsRefused == 3 && presenter.ReactionWarnings == 2 && presenter.LiveReactions == 0,
                    "a recognised kind with no prefab (mud) is refused twice and warned once; a kind the look lacks is warned once; None is silent; nothing is made and nothing throws");
                Object.DestroyImmediate(bare);
            }

            // ---- the kind of a table row (the cue is keyed by kind) and what draws it
            var look = AssetDatabase.LoadAssetAtPath<SpellReactionLook308SO>(V128LookAsset);
            {
                var errors = new List<string>();
                var rows = ReactionTable308.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Data/Spells", ReactionTable308.FileName), System.Text.Encoding.UTF8), errors);
                ReactionKind308 K(string id) => SpellReaction308.KindOf(rows.FirstOrDefault(x => x.Id == id));
                var guess = new ReactionRow308 { Id = "renamed", Kind = ReactionProductKind308.Damage, Product = Element.Fire };
                r.Check("V128-kind", errors.Count == 0 && K("wildfire") == ReactionKind308.Wildfire && K("steam") == ReactionKind308.Steam && K("mud") == ReactionKind308.Mud && K("nothing") == ReactionKind308.None &&
                    SpellReaction308.KindOf(guess) == ReactionKind308.Wildfire && SpellReaction308.KindOf(new ReactionRow308 { Id = "r", Kind = ReactionProductKind308.None, Product = Element.Water }) == ReactionKind308.Steam &&
                    SpellReaction308.KindOf(new ReactionRow308 { Id = "r", Kind = ReactionProductKind308.Slow, Product = Element.Earth }) == ReactionKind308.Mud,
                    "the three table rows map to wildfire / steam / mud by id, an unnamed row by what it leaves behind");
                if (!r.Check("V128-look", look != null, "Resources/Spell308/SpellReactionLook308 exists (run: Vfx308Build reaction)")) return;
                r.Check("V128-look", Resources.Load<SpellReactionLook308SO>(SpellReactionLook308SO.ResourcesPath) == look, "the look is where the presenter reads it (Resources path)");
                bool fire = look.TryGet(ReactionKind308.Wildfire, out var fireEntry), steam = look.TryGet(ReactionKind308.Steam, out var steamEntry), mud = look.TryGet(ReactionKind308.Mud, out _);
                r.Check("V128-look", fire && steam && !mud && look.Knows(ReactionKind308.Mud) && look.Knows(ReactionKind308.Wildfire) && !look.Knows(ReactionKind308.None) &&
                    fireEntry.Prefab.name == "PF_V308z_react_wildfire" && steamEntry.Prefab.name == "PF_V308z_react_steam" && !fireEntry.KeepHeight && steamEntry.KeepHeight,
                    "cue kind -> prefab: wildfire -> PF_V308z_react_wildfire (scaled all round), steam -> PF_V308z_react_steam (heights in metres), mud recognised with no prefab");
                if (!fire || !steam) return;

                CheckReactPrefab(r, "wildfire", fireEntry, V128FireBudget, 3.5f, 5.25f, 8f, forward);   // the largest product of the table: a 3.5 m material x1.5
                CheckReactPrefab(r, "steam", steamEntry, V128SteamBudget, 3f, 3f, 2f, forward);
            }
        }

        // One look: structure, particle budget, materials, colours, then dressed as the presenter dresses it and simulated.
        static void CheckReactPrefab(Report r, string name, SpellReactionLook308SO.Entry entry, int budget, float fromRadius, float radius, float life, Vector3 way)
        {
            string path = AssetDatabase.GetAssetPath(entry.Prefab);
            string c = "V128-" + name;
            var go = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var all = go.GetComponentsInChildren<ParticleSystem>(true);
                var body = go.GetComponent<Vfx308ZoneBody>();
                r.Check(c, body != null && all.Length > 0 && go.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == V128FogLayer),
                    name + ": a Vfx308ZoneBody on the root, " + all.Length + " layers, all on the after-fog layer");
                int total = all.Sum(ps => ps.main.maxParticles);
                int oneShot = all.Where(ps => ps.emission.rateOverTimeMultiplier <= 0f).Sum(ps => ps.emission.burstCount > 0 ? Mathf.Min(ps.main.maxParticles, Mathf.Max(1, (int)ps.emission.GetBurst(0).maxCount)) : 0);
                r.Check(c, total <= budget && oneShot <= V128TransitionBudget, name + ": " + total + " particles at most (budget " + budget + "), one-shot layers " + oneShot + " (transition budget " + V128TransitionBudget + ")");

                // ink, not light: the InkBleed shader only, alpha-blended, no glow / radial / heat / emission, nothing above LDR
                var mats = all.Select(ps => ps.GetComponent<ParticleSystemRenderer>().sharedMaterial).Distinct().ToArray();
                var bad = new List<string>();
                foreach (var m in mats)
                {
                    if (m == null) { bad.Add("a layer without a material"); continue; }
                    if (m.shader == null || m.shader.name != V128InkShader) bad.Add(m.name + ": shader " + (m.shader != null ? m.shader.name : "null"));
                    if (m.HasProperty("_Glow") && m.GetFloat("_Glow") > 0f) bad.Add(m.name + ": _Glow");
                    if (m.HasProperty("_Radial") && m.GetFloat("_Radial") > 0f) bad.Add(m.name + ": _Radial");
                    if (m.HasProperty("_Heat") && m.GetFloat("_Heat") > 0f) bad.Add(m.name + ": _Heat");
                    if (m.IsKeywordEnabled("_EMISSION") || (m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0f)) bad.Add(m.name + ": emission");
                }
                string shaderPath = mats.Length > 0 && mats[0] != null && mats[0].shader != null ? AssetDatabase.GetAssetPath(mats[0].shader) : "";
                var blends = shaderPath.EndsWith(".shader", System.StringComparison.Ordinal) && File.Exists(shaderPath)
                    ? Regex.Matches(File.ReadAllText(shaderPath), @"^\s*Blend\s+([^\r\n]+)", RegexOptions.Multiline).Cast<Match>().Select(x => Regex.Replace(x.Groups[1].Value.Trim(), @"\s+", " ")).ToArray() : new string[0];
                bool alphaOnly = blends.Length > 0 && blends.All(b => b == "SrcAlpha OneMinusSrcAlpha");
                bool ldr = all.All(ps => Ldr(ps));
                r.Check(c, bad.Count == 0 && alphaOnly && ldr,
                    name + ": " + mats.Length + " materials, InkBleed only, no glow / radial / heat / emission, blend '" + string.Join(" | ", blends) + "' (alpha, never additive), every colour LDR" + (bad.Count > 0 ? " - " + string.Join("; ", bad) : ""));

                if (name == "steam") CheckSteamLook(r, go, all);
                else CheckFireLook(r, go, all);
            }
            finally { PrefabUtility.UnloadPrefabContents(go); }

            // dressed and simulated, a fresh copy each time
            go = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var args = SpellReaction308.Args(name == "wildfire" ? ReactionKind308.Wildfire : ReactionKind308.Steam, Vector3.zero, way, fromRadius, radius, life);
                var before = go.GetComponentsInChildren<ParticleSystem>(true).Where(ps => ps.transform.parent == go.transform).ToDictionary(ps => ps.name, ps => new Vector3(ps.main.startSizeXMultiplier, ps.main.startSizeYMultiplier, ps.main.startSizeMultiplier));
                var body = SpellReactionDress308.Dress(go, entry, go.transform.localScale, args);
                if (!r.Check(c, body != null, name + ": the presenter's dressing finds the body")) return;
                Vector3 s = go.transform.localScale;
                var grow = go.GetComponent<Vfx308ReactGrow>();
                bool grows = fromRadius < radius - 1e-3f;
                bool scaleOk = entry.KeepHeight ? Near(s.x, radius) && Near(s.y, 1f) && Near(s.z, radius) && grow == null
                    : grows ? grow != null && Near(s.x, fromRadius) && Near(grow.To.x, radius) && Near(grow.Seconds, SpellReactionDress308.GrowSeconds) : Near(s.x, radius) && grow == null;
                if (grows && grow != null) { grow.Tick(SpellReactionDress308.GrowSeconds); scaleOk &= Near(go.transform.localScale.x, radius, .001f) && grow.Done; }
                r.Check(c, scaleOk && Vector3.Dot(go.transform.forward, way) > .999f,
                    name + ": born at " + (entry.KeepHeight ? "(radius, 1, radius)" : "the material's radius " + fromRadius) + (grows ? ", grown to " + radius + " in " + SpellReactionDress308.GrowSeconds + " s" : "") + ", +Z along the wave's way");
                float total = body.Life + body.DryFade;
                r.Check(c, body.Life >= SpellReactionDress308.MinBody - 1e-4f && Near(total, life + .1f, .06f) && body.LateAfter == 0f && body.LateBefore == 0f,
                    name + ": the body lives " + body.Life.ToString("0.00") + " s and dries over " + body.DryFade.ToString("0.00") + " s: gone with the " + life + " s product");

                // the beat of the fire: .4 s run (rising) -> .3 s burst (peak) -> only dying down
                if (name == "wildfire")
                {
                    var curve = SpellReactionDress308.RunBurstDecay(body.Life);
                    float run = SpellReactionDress308.RunSeconds / body.Life, burst = (SpellReactionDress308.RunSeconds + SpellReactionDress308.BurstSeconds) / body.Life;
                    bool up = true, down = true; float peak = 0f, peakAt = 0f, last = curve.Evaluate(0f);
                    for (int i = 1; i <= 400; i++)
                    {
                        float x = i / 400f, v = curve.Evaluate(x);
                        if (x <= run && v < last - 1e-4f) up = false;
                        if (x >= burst && v > last + 1e-4f) down = false;
                        if (v > peak) { peak = v; peakAt = x; }
                        last = v;
                    }
                    var tongues = body.Body.Where(ps => ps.name.StartsWith("Tongue")).ToArray();
                    var tongue = tongues.Length > 0 ? tongues[0] : null;
                    bool applied = tongue != null && !tongue.main.loop && Near(tongue.main.duration, body.Life, .01f) && tongue.emission.rateOverTime.mode == ParticleSystemCurveMode.Curve &&
                        Near(tongue.emission.rateOverTime.curve.Evaluate(.5f), curve.Evaluate(.5f), .001f);
                    r.Check(c, up && down && peakAt >= run && peakAt <= burst && curve.Evaluate(0f) < curve.Evaluate(run) && curve.Evaluate(1f) < curve.Evaluate(burst) && applied,
                        "wildfire: the tongue rate runs up over .4 s, peaks inside the next .3 s (x" + peak.ToString("0.0") + "), then only falls; every tongue layer has it and runs once over the body's life");
                    var ring = body.Body.FirstOrDefault(ps => ps.name == "OldRing");
                    r.Check(c, ring != null && ring.main.simulationSpace == ParticleSystemSimulationSpace.World && ring.emission.burstCount == 1,
                        "wildfire: the old perimeter is laid once, in world space, so it stays where it stood while the fire grows past it");
                }
                else
                {
                    var wet = body.Body.FirstOrDefault(ps => ps.name == "Wet");
                    var crack = body.Body.FirstOrDefault(ps => ps.name == "Crack");
                    r.Check(c, wet != null && crack != null && Near(wet.main.startSizeXMultiplier, before["Wet"].x * radius, .01f) && Near(wet.main.startSizeYMultiplier, before["Wet"].y * radius, .01f) &&
                        Near(crack.main.startSizeMultiplier, before["Crack"].z * radius, .01f),
                        "steam: the flat wet track and the crack are sized by the radius (their heights are in metres, the root is not scaled up)");
                }

                // simulated: nothing taller than 1.3 m, nothing over budget, something really drawn
                float ground = go.transform.position.y; int alive = 0, peakAlive = 0; float top = 0f; string how = "";
                foreach (float t in new[] { .3f, .7f, 1.2f, 1.9f, 3f })
                {
                    int now = 0;
                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        if (ps.transform.parent != go.transform) continue;
                        ps.Simulate(t, false, true, true);
                        if (ps.particleCount == 0) continue;
                        now += ps.particleCount;
                        var rd = ps.GetComponent<ParticleSystemRenderer>();
                        var ps2 = new ParticleSystem.Particle[ps.particleCount]; int n = ps.GetParticles(ps2);
                        float pTop = float.NegativeInfinity;
                        for (int i = 0; i < n; i++) pTop = Mathf.Max(pTop, (ps.main.simulationSpace == ParticleSystemSimulationSpace.World ? ps2[i].position : ps.transform.TransformPoint(ps2[i].position)).y);
                        if (ps.name == "Ash") continue;   // ash flakes drift up as far as the ember patch's do (~1.8 m, world space); the 1.3 m rule is the smoke's and the steam's
                        float bTop = rd.bounds.size.sqrMagnitude > 0f ? rd.bounds.max.y : float.NegativeInfinity;
                        top = Mathf.Max(top, bTop > float.NegativeInfinity ? bTop : pTop); how = bTop > float.NegativeInfinity ? "renderer bounds" : "particle centres";
                    }
                    peakAlive = Mathf.Max(peakAlive, now); alive += now;
                }
                r.Check(c, alive > 0 && peakAlive <= budget && top - ground <= V128MaxHeight,
                    name + ": simulated at radius " + radius + " - " + peakAlive + " particles alive at most (budget " + budget + "), highest point " + (top - ground).ToString("0.00") + " m, ash flakes aside (<= " + V128MaxHeight + ", " + how + ")" + (alive == 0 ? " - the simulation made no particle: not conclusive" : ""));

                // a zone body that a reaction consumes goes quietly: React pales it, it is not Splash and not Dry
                var probe = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var b = probe.GetComponent<Vfx308ZoneBody>();
                    b.Tick(.05f);
                    bool started = b.Started;
                    b.React(Vector3.right);
                    b.Tick(.1f);
                    r.Check(c, started && b.Reacted && b.Drying && !b.Gone, name + ": Vfx308ZoneBody.React(dir) on a started body: it is reacted, fading (no End, no Break) and still there for its pale-out");
                }
                finally { PrefabUtility.UnloadPrefabContents(probe); }
            }
            finally { PrefabUtility.UnloadPrefabContents(go); }
        }

        static bool Ldr(ParticleSystem ps)
        {
            var main = ps.main;
            bool ok = true;
            void Check(ParticleSystem.MinMaxGradient g)
            {
                if (g.mode == ParticleSystemGradientMode.Gradient || g.mode == ParticleSystemGradientMode.RandomColor) { if (g.gradient != null) foreach (var k in g.gradient.colorKeys) ok &= k.color.maxColorComponent <= 1.0001f; }
                else if (g.mode == ParticleSystemGradientMode.TwoGradients) { foreach (var k in g.gradientMin.colorKeys) ok &= k.color.maxColorComponent <= 1.0001f; foreach (var k in g.gradientMax.colorKeys) ok &= k.color.maxColorComponent <= 1.0001f; }
                else if (g.mode == ParticleSystemGradientMode.TwoColors) ok &= g.colorMin.maxColorComponent <= 1.0001f && g.colorMax.maxColorComponent <= 1.0001f;
                else ok &= g.color.maxColorComponent <= 1.0001f;
            }
            Check(main.startColor);
            if (ps.colorOverLifetime.enabled) Check(ps.colorOverLifetime.color);
            return ok;
        }

        // wildfire: tongues only on the fuel lines, none on the bare ground; the ash is darker than the ground; no tongue past the new perimeter
        static void CheckFireLook(Report r, GameObject go, ParticleSystem[] all)
        {
            var tongues = all.Where(ps => ps.name.StartsWith("Tongue")).ToArray();
            var fuel = all.Where(ps => ps.name.StartsWith("Fuel")).ToArray();
            bool pairs = tongues.Length >= 4 && tongues.Length == fuel.Length;
            bool online = pairs;
            for (int i = 0; online && i < tongues.Length; i++)
            {
                var a = tongues[i].shape; var b = fuel[i].shape;
                var flat = new Vector3(a.position.x, 0f, a.position.z);
                online &= a.shapeType == ParticleSystemShapeType.SingleSidedEdge && b.shapeType == a.shapeType && a.position == b.position && a.rotation == b.rotation && Near(a.radius, b.radius) &&
                    Mathf.Abs(Vector3.Dot(Quaternion.Euler(a.rotation) * Vector3.right, flat.normalized)) > .999f &&
                    (a.position + Quaternion.Euler(a.rotation) * Vector3.right * a.radius).magnitude <= 1.0001f && (a.position - Quaternion.Euler(a.rotation) * Vector3.right * a.radius).magnitude <= 1.0001f;
            }
            r.Check("V128-wildfire", online && tongues.All(ps => ps.main.scalingMode == ParticleSystemScalingMode.Shape) && all.Where(ps => ps.name.StartsWith("Tongue") || ps.name.StartsWith("Fuel")).All(ps => ps.shape.shapeType == ParticleSystemShapeType.SingleSidedEdge),
                "wildfire: " + tongues.Length + " tongue layers sit on exactly the same edge lines as the " + fuel.Length + " fuel strokes, none beyond the product's radius (the perimeter), and keep their size at any radius (Shape scaling)");
            var ash = Hex128("#5C5449");
            var ashMat = all.Where(ps => ps.name == "Ash" || ps.name == "OldRing" || ps.name == "Wisps").Select(ps => ps.colorOverLifetime.color.gradient).Where(g => g != null).SelectMany(g => g.colorKeys).Select(k => k.color).ToArray();
            r.Check("V128-wildfire", ashMat.Length > 0 && ashMat.All(k => k.maxColorComponent <= Hex128("#867C6B").maxColorComponent - .05f) && ashMat.All(k => Mathf.Abs(k.r - k.g) < .2f),
                "wildfire: smoke, ash and the old ring are painted darker than the #867C6B ceiling (body " + ColorUtility.ToHtmlStringRGB(ash) + "), never red");
        }

        // steam: cool (B >= R), darker at the foot, nothing white, along the wave, middle left empty, <= 16 smoke quads
        static void CheckSteamLook(Report r, GameObject go, ParticleSystem[] all)
        {
            var smoke = all.Where(ps => ps.name.StartsWith("Foot") || ps.name.StartsWith("Top")).ToArray();
            int quads = smoke.Sum(ps => ps.main.maxParticles);
            bool cool = true, notWhite = true, drift = true, hollow = true, short1 = true;
            foreach (var ps in smoke)
            {
                var keys = ps.colorOverLifetime.color.gradient.colorKeys;
                cool &= keys.All(k => k.color.b >= k.color.r);
                notWhite &= keys.All(k => k.color.maxColorComponent <= .5f);
                var v = ps.velocityOverLifetime;
                drift &= v.enabled && v.space == ParticleSystemSimulationSpace.Local && v.z.constantMin >= .5f && v.z.constantMax <= .7f && Mathf.Abs(v.x.constantMin) <= .2f && Mathf.Abs(v.x.constantMax) <= .2f && v.y.constantMax <= .11f;
                var sh = ps.shape; drift &= ps.GetComponent<ParticleSystemRenderer>().renderMode == ParticleSystemRenderMode.Stretch && ps.GetComponent<ParticleSystemRenderer>().lengthScale >= 1.5f && ps.GetComponent<ParticleSystemRenderer>().lengthScale <= 2f;
                hollow &= sh.shapeType == ParticleSystemShapeType.Box && Mathf.Abs(sh.position.z) - sh.scale.z * .5f >= .25f;
                short1 &= ps.main.startLifetime.constantMax <= 1f;
            }
            var near = smoke.Where(ps => ps.shape.position.z < 0f).Sum(ps => ps.main.maxParticles); var far = quads - near;
            r.Check("V128-steam", smoke.Length == 4 && quads >= 12 && quads <= 16 && near > 0 && far > 0, "steam: " + quads + " smoke quads in two strips (" + near + " + " + far + "), 12 to 16 as decided");
            r.Check("V128-steam", cool && notWhite, "steam: every smoke colour is cooler than red (B >= R) and none is white (every channel <= .5)");
            r.Check("V128-steam", drift && hollow && short1, "steam: it drifts along +Z at .55 - .65 m/s (sideways <= .12, up <= .1), stretched 1.5 - 2x along it, the two strips leave the middle (>= .5 of the radius) empty, each quad lives <= 1 s");
            float footLum = all.Where(ps => ps.name.StartsWith("Foot")).SelectMany(ps => ps.colorOverLifetime.color.gradient.colorKeys).Max(k => k.color.maxColorComponent);
            float topLum = all.Where(ps => ps.name.StartsWith("Top")).SelectMany(ps => ps.colorOverLifetime.color.gradient.colorKeys).Max(k => k.color.maxColorComponent);
            float footA = all.Where(ps => ps.name.StartsWith("Foot")).SelectMany(ps => ps.colorOverLifetime.color.gradient.alphaKeys).Max(k => k.alpha);
            float topA = all.Where(ps => ps.name.StartsWith("Top")).SelectMany(ps => ps.colorOverLifetime.color.gradient.alphaKeys).Max(k => k.alpha);
            r.Check("V128-steam", footLum < topLum && footA >= .25f && footA <= .3f && topA >= .08f && topA <= .15f,
                "steam: the foot is darker than the tips (" + footLum.ToString("0.00") + " < " + topLum.ToString("0.00") + ") and thicker (alpha " + footA.ToString("0.00") + " vs " + topA.ToString("0.00") + " at the tips)");
        }

        static Color Hex128(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
    }
}
