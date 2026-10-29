using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Oheangbu.App;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using static Oheangbu.EditorTools.Vol2.Vol2ShapeFixtureKit;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>Edit-mode fixture of the vol.2 spell-look FRAMEWORK (the data, the model on the SCOPE clock, the presenter's lifetime). No Play. Queue:
    /// python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2VfxFixture Run "all"
    ///   V0 24 templates (all filled), 24 recipes registered = the legacy bridge, 5 body looks (edge cue and end behaviour all different, L* 18 / 60 layers, plan colours), plan B numbers
    ///   V2 no stray vol.2 VFX material (only the bridge's lasting variants under Vol2Vfx), no Light in a prefab; the removed ink-mesh code and assets are really gone
    ///   V3 the model / presenter / data / bridge recipe files own no timer (no Time.time / deltaTime / coroutine / Invoke): lifetimes come from the executors' clock only
    ///   V6 AbortAll clears at once with no tail
    ///   V7 contacts: only a budget-accepted contact is accented; A / F / R and batchim casts are covered (no cast left to the debug lines)
    ///   V9 the presenter ticks the model, leaves no object behind and does not dirty the open scene
    /// The legacy bridge's own behaviour (bodies, parts, ends) is Vol2LegacyBridgeFixture. 2026-10-26: the checks of the removed slice 9a / NB20 / stroke20 recipes
    /// (V1 beats, V4 / V4b / V4n / V4p / V10 strokes, V5 / V8 ink parts, V9 mesh pool, V2 ink shader / material / atlas) went with them (Docs/Research/vfx120/cleanup_code_20261026.md).
    /// Expected values are written here from the plan and the data, never read back from a run.</summary>
    public static class Vol2VfxFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-vfx-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-vfx-fixture: editor busy (play / compile / import)";
            var r = new Report();
            try { using (var errors = new ErrorCounter()) { Cases(r); Check(r, "E0", errors.Errors == 0, "no error log during the fixture" + (errors.Errors > 0 ? ": " + errors.First : "")); } }
            catch (Exception e) { Check(r, "EXCEPTION", false, e.ToString()); }
            return JsonUtility.ToJson(r, true);
        }

        static void Check(Report r, string id, bool ok, string detail)
        {
            if (ok) r.pass++; else r.fail++;
            r.lines.Add((ok ? "PASS " : "FAIL ") + id + " " + detail);
        }

        static float LabL(Color c)
        {
            Func<float, float> lin = u => u <= 0.04045f ? u / 12.92f : Mathf.Pow((u + 0.055f) / 1.055f, 2.4f);
            float y = 0.2126f * lin(c.r) + 0.7152f * lin(c.g) + 0.0722f * lin(c.b);
            float f = y > 0.008856f ? Mathf.Pow(y, 1f / 3f) : 7.787f * y + 16f / 116f;
            return 116f * f - 16f;
        }

        static string HexOf(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        /// <summary>The model on the hand clock with the default registry (the legacy bridge; no stage bound in edit mode: the bridge recipes keep their bookkeeping only).</summary>
        sealed class Setup : IDisposable
        {
            public Rig Rig; public Vol2VfxModel Model; public Vol2VfxCatalogue Cat;
            public Setup(Vol2Rulebook book, Vol2VfxCatalogue cat)
            {
                Cat = cat; Rig = Rig.Create(book); Model = new Vol2VfxModel(Rig.Coord, cat);
            }
            public long NowUs => Rig.Services.Clock.NowUs;
            /// <summary>Advance the hand clock (steps of dt) and tick the model on it, like the view does each frame.</summary>
            public void Drive(double seconds, double dt = 0.05)
            {
                int n = Math.Max(1, (int)Math.Round(seconds / dt));
                for (int i = 0; i < n; i++) { Rig.Advance(seconds / n); Model.Tick(NowUs); }
            }
            public void Dispose() { Model.Dispose(); }
        }

        static void Cases(Report r)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "F0", book.Ok, book.Ok ? "rulebook loaded" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var cat = Vol2VfxCatalogue.Load();
            Check(r, "F1", cat != null, cat != null ? "catalogue loaded (" + (AssetDatabase.Contains(cat) ? "asset" : "in-memory default") + ")" : "NO CATALOGUE");
            if (cat == null) return;

            // ---- V0 data ----
            {
                bool complete = cat.Complete(out string problem);
                var filled = cat.templates.Where(t => t.filled).Select(t => t.id).OrderBy(x => x).ToList();
                Check(r, "V0a", complete && cat.templates.Length == 24 && filled.Count == 24 && cat.templates.Select(t => t.id).Distinct().Count() == 24,
                    "24 distinct template ids, all 24 filled (D308-150 V9 + step 4: every template is the legacy bridge) [" + string.Join(",", filled) + "]" + (complete ? "" : " [" + problem + "]"));
                var recipes = Vol2VfxRecipeRegistry.CreateDefault();
                bool allMapped = true;
                foreach (var t in cat.templates.Where(t => t.filled))
                    allMapped &= recipes.TryGet(t.recipe, out _) && t.recipe == Vol2VfxDefaults.RecipeOf(new Vol2TemplateId(t.frame, t.verb))
                        && (t.frame == Frame.Path ? (int)t.recipe == (int)t.verb + 1 : t.recipe != Vol2Recipe.None);
                bool batchim = true;
                foreach (Frame fr in new[] { Frame.Path, Frame.Attachment, Frame.Fan, Frame.Ring })
                    for (int v = 1; v <= 5; v++) batchim &= recipes.TryGet(Vol2VfxDefaults.RecipeOf(new Vol2TemplateId(fr, (Verb)v)), out var br) && br is Vol2LegacyVerbRecipe;
                Check(r, "V0b", recipes.Count == 24 && allMapped && batchim && recipes.TryGet(Vol2Recipe.PathOneHit, out var p0r) && p0r is Vol2LegacyPathRecipe
                    && recipes.TryGet(Vol2Recipe.AttachOnce, out var a0r) && a0r is Vol2LegacyAttachRecipe && recipes.TryGet(Vol2Recipe.FanOnce, out var f0r) && f0r is Vol2LegacyFanRecipe
                    && recipes.TryGet(Vol2Recipe.RingOnce, out var r0r) && r0r is Vol2LegacyRingRecipe,
                    "24 recipes registered, one per template: the 20 no-batchim P0 / A0 / F0 / R0 and the 100 batchim P1 - R5 = the legacy bridge (D308-150 V9 + step 4: the old main game's visuals; the free showcase wraps them per glyph in Play)" + (batchim ? "" : " [a batchim template is not the bridge]"));
                bool distinct = cat.bodies.Select(b => b.edge).Distinct().Count() == 5 && cat.bodies.Select(b => b.end).Distinct().Count() == 5;
                Check(r, "V0c", cat.bodies.Length == 5 && distinct, "5 body looks: 5 different edge cues (fibre tear / saw-tooth / plates / straight cut / hook) and 5 different end behaviours");
                var wood = cat.Look(Body.Wood); var fire = cat.Look(Body.Fire); var earth = cat.Look(Body.Earth); var metal = cat.Look(Body.Metal); var water = cat.Look(Body.Water);
                Check(r, "V0d", wood.endDirection.z > 0.5f && fire.endDirection.y > 0.5f && earth.endDirection.y < -0.5f && Mathf.Abs(metal.endDirection.x) > 0.5f && water.endDirection.y < -0.5f,
                    "end direction: wood forward, fire up (ash), earth down (sinks), metal sideways (snaps), water down (runs)");
                bool layers = true; string bad = "";
                foreach (var b in cat.bodies)
                {
                    float li = LabL(b.inkLayer), lp = LabL(b.dryPigment);
                    if (Mathf.Abs(li - 18f) > 1.5f || Mathf.Abs(lp - 60f) > 3.5f) { layers = false; bad += b.body + " ink L*" + li.ToString("0.0") + " pigment L*" + lp.ToString("0.0") + "; "; }
                }
                Check(r, "V0e", layers, "two lightness layers (plan C-2): reading ink L* 18 +/- 1.5, dry pigment L* 60 +/- 3.5 in every body" + (bad.Length > 0 ? " [" + bad + "]" : ""));
                Check(r, "V0f", HexOf(wood.wetColor) == "#368B75" && HexOf(fire.wetColor) == "#BE543B" && HexOf(earth.wetColor) == "#B68A3D" && HexOf(metal.wetColor) == "#A6B0AD" && HexOf(water.wetColor) == "#4F6A84",
                    "the wet colours are the plan C base colours (368B75 / BE543B / B68A3D / A6B0AD / 4F6A84)");
                var t0 = cat.Template(new Vol2TemplateId(Frame.Path, Verb.Once));
                Check(r, "V0g", Mathf.Approximately(t0.endExpireSeconds, 0.30f) && Mathf.Approximately(t0.endConsumeSeconds, 0.15f) && Mathf.Approximately(t0.endChildSeconds, 0.30f)
                    && Mathf.Approximately(t0.endReplaceSeconds, 0.20f) && Mathf.Approximately(t0.endHitCancelSeconds, 0.08f) && Mathf.Approximately(t0.endCleanseSeconds, 0.25f),
                    "plan B end tails in the data (the model reads them): 만 0.30 / 소 0.15 / 자 0.30 / 교 0.20 / 피 0.08 / 정 0.25");
            }

            // ---- V2 hygiene ----
            {
                int lastingCount = 0; string stray = "", info = "";
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Vol2VfxBuild.Root }))
                {
                    string mp = AssetDatabase.GUIDToAssetPath(guid);
                    var m = AssetDatabase.LoadAssetAtPath<Material>(mp); if (m == null) continue;
                    // D308-150 V9 step 5a: the legacy bridge's lasting variants (Vol2Vfx/LegacyLasting) are copies of OLD parts and keep the old family's ink shader (InkBleed / SeedLeafMark)
                    if (!mp.StartsWith(Vol2LastingBuild.Out + "/", StringComparison.Ordinal)) { stray += mp + "; "; continue; }
                    lastingCount++;
                    bool ok = m.shader != null && (m.shader.name == "Oheangbu/SpellVFX308/InkBleed" || m.shader.name == "Oheangbu/VFX120/SeedLeafMark") && !m.IsKeywordEnabled("_EMISSION")
                        && (!m.HasProperty("_EmissionColor") || m.GetColor("_EmissionColor").maxColorComponent <= 0f)
                        && (!m.HasProperty("_SrcBlend") || (int)m.GetFloat("_SrcBlend") != 1 || (int)m.GetFloat("_DstBlend") != 1)
                        && (!m.HasProperty("_Glow") || m.GetFloat("_Glow") == 0f) && (!m.HasProperty("_Radial") || m.GetFloat("_Radial") == 0f);
                    if (!ok) info += m.name + "; ";        // the bridge's lasting variants: no look rule (D308-150 V11), reported only
                }
                var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { Vol2VfxBuild.Root });
                bool noLight = true;
                foreach (string guid in prefabs) { var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)); if (go != null && go.GetComponentInChildren<Light>(true) != null) noLight = false; }
                Check(r, "V2c", stray.Length == 0 && noLight, "materials under " + Vol2VfxBuild.Root + ": only the " + lastingCount + " legacy-bridge lasting variants (old InkBleed / pattern SeedLeafMark), " + prefabs.Length + " prefab(s) with no Light"
                    + (stray.Length > 0 ? " [STRAY " + stray + "]" : "") + (info.Length > 0 ? " [INFO not on the old no-glow rules: " + info + "]" : ""));
                // the removed ink-mesh path (2026-10-26) stays removed: no type left in the runtime assembly, no shader / material / atlas left in the project
                var app = typeof(Vol2VfxModel).Assembly;
                var gone = new[] { "Vol2PathRecipeBase", "Vol2PathOneHitRecipe", "Vol2Nb20PathRecipe", "Vol2Nb20Atlas", "Vol2StrokeRecipeBase", "Vol2StrokePathRecipe", "Vol2InkWriter", "Vol2StrokeAttr", "Vol2Curves" };
                var left = gone.Where(n => app.GetType("Oheangbu.App." + n) != null).ToList();
                var paths = new[] { "Assets/_Project/Shaders/SpellVFX308/Vol2Ink.shader", Vol2VfxBuild.Root + "/M_Vol2Ink.mat", Vol2VfxBuild.Root + "/NB20/NB20_Sheet.png", Vol2VfxBuild.Root + "/NB20/NB20_SheetTime.png" };
                var still = paths.Where(p => File.Exists(Path.GetFullPath(p)) || AssetDatabase.LoadMainAssetAtPath(p) != null).ToList();
                Check(r, "V2x", left.Count == 0 && still.Count == 0 && Shader.Find("Oheangbu/SpellVFX308/Vol2Ink") == null,
                    "the slice 9a / NB20 / stroke20 ink-mesh code and assets are gone (" + gone.Length + " types, the Vol2Ink shader, M_Vol2Ink, the NB20 atlas pair)"
                    + (left.Count > 0 ? " [types left: " + string.Join(",", left) + "]" : "") + (still.Count > 0 ? " [assets left: " + string.Join(",", still) + "]" : ""));
            }

            // ---- V3 no presenter-owned lifetime ----
            {
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "_Project/Scripts/App/Vol2/Presentation"));
                var files = new[] { "Vol2VfxData.cs", "Vol2VfxModel.cs", "Vol2VfxPresenter.cs", "Vol2BodyLook.cs", "Vol2VfxTemplate.cs", "Vol2VfxCatalogue.cs", "Vol2LegacyRecipes.cs", "Vol2LegacyVerbRecipes.cs" };
                var timers = new Regex(@"Time\.(time|deltaTime|unscaled\w*|fixed\w*|realtimeSinceStartup|timeAsDouble)|StartCoroutine|IEnumerator|WaitFor\w+|(?<![.\w])Invoke\s*\(|InvokeRepeating|UniTask\.(Delay|Yield)|Task\.Delay|System\.Timers|new\s+Timer|DOTween|Destroy\s*\([^)]*,\s*[0-9]");
                string found = ""; int scanned = 0;
                foreach (string f in files)
                {
                    string p = Path.Combine(dir, f);
                    if (!File.Exists(p)) { found += f + " missing; "; continue; }
                    scanned++;
                    string code = Regex.Replace(File.ReadAllText(p), @"//[^\n]*", "");
                    foreach (Match m in timers.Matches(code)) found += f + ": " + m.Value + "; ";
                }
                Check(r, "V3", scanned == files.Length && found.Length == 0, "the presenter / model / data / bridge recipe sources use no Time.*, coroutine, Invoke or delay: " + scanned + " files scanned" + (found.Length > 0 ? " [" + found + "]" : ""));
            }

            // ---- V6 abort ----
            using (var s = new Setup(book, cat))
            {
                s.Rig.Add("A", 4f, 0f);
                s.Rig.Cast('낙', out _); s.Drive(0.6);
                int before = s.Model.Instances.Count;
                s.Rig.Coord.AbortAll(); s.Model.Tick(s.NowUs);
                Check(r, "V6b", before == 1 && s.Model.Instances.Count == 0 && s.Model.AbortedCount == 1, "AbortAll (the lab reset between rows) clears the instance at once, no tail, counted as aborted");
            }

            // ---- V7 contacts / coverage ----
            {
                using (var s = new Setup(book, cat))
                {
                    s.Rig.Add("A", 4f, 0f); s.Rig.Add("B", 6f, 0f); s.Rig.Add("C", 8f, 0f);
                    var log = s.Rig.Cast('삿', out _); s.Drive(0.6);
                    int accepted = log.Contacts.Count(c => c.BudgetAccepted), refused = log.Contacts.Count(c => !c.BudgetAccepted);
                    Check(r, "V7a", log.Contacts.Count >= 2 && accepted >= 1 && s.Model.ContactsAccented == accepted && s.Model.ContactsSkipped == refused,
                        "삿 through three bodies: " + accepted + " accepted contact(s) are accented, the " + refused + " the rules refused (FirstDamageOnly) draw no hit");
                }
                using (var s = new Setup(book, cat))
                {
                    s.Rig.Add("A", 2f, 0f);
                    var parry = s.Rig.Cast('거', out _); s.Drive(0.3);
                    var fan = s.Rig.Cast('고', out _); s.Drive(0.3);
                    var ring = s.Rig.Cast('구', out _); s.Drive(0.3);
                    var leave = s.Rig.Cast('곡', out _); s.Drive(0.3);
                    Check(r, "V7b", s.Model.SpawnedCount == (leave != null ? 4 : 3) && s.Model.Covers(parry) && s.Model.Covers(fan) && s.Model.Covers(ring) && (leave == null || s.Model.Covers(leave)),
                        "the 한 번 of A / F / R (거 고 구) and a batchim A / F / R template (곡) spawn their look (" + s.Model.SpawnedCount + "): no cast is left to the debug lines");
                }
            }

            // ---- V9 presenter lifetime ----
            {
                var sceneDirtyBefore = UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;
                int objectsBefore = Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.name == "Vol2VfxPresenter");
                Vol2VfxPresenter pres = null; bool ticked = false;
                using (var s = new Setup(book, cat))
                {
                    pres = Vol2VfxPresenter.Attach(s.Rig.Coord, null, cat, null, HideFlags.HideAndDontSave);
                    try
                    {
                        s.Rig.Add("A", 4f, 0f);
                        pres.Step();
                        s.Rig.Cast('가', out _);
                        for (int i = 0; i < 6; i++) { s.Rig.Advance(0.05); pres.Step(); }
                        ticked = pres.Drawn == 1 && pres.LastStepUs == s.NowUs && pres.Legacy == null;
                        s.Rig.Coord.AbortAll(); pres.Step(); pres.ClearAll();
                        ticked &= pres.Model.Instances.Count == 0;
                    }
                    finally { if (pres != null) { pres.Release(); UnityEngine.Object.DestroyImmediate(pres.gameObject); } }
                }
                int objectsAfter = Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.name == "Vol2VfxPresenter");
                Check(r, "V9", ticked && objectsAfter == objectsBefore && UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty == sceneDirtyBefore,
                    "the presenter ticks the model on the scope clock (가 spawned, no bridge stage in edit mode), AbortAll clears it, nothing is left after it is destroyed (objects " + objectsBefore + " -> " + objectsAfter + "), the open scene is not dirtied");
            }
        }
    }
}
