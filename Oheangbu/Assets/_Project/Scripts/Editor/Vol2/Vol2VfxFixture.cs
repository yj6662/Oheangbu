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
    /// <summary>S5-b slice 9a edit-mode fixture of the vol.2 spell look (D308-150 V5): the data, the model on the SCOPE clock, the six path recipes x five bodies, the end reasons, the pool. No Play. Queue:
    /// python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2VfxFixture Run "all"
    ///   V0 24 templates (P0 - P5 filled, 18 later), 5 body looks (edge cue and end behaviour all different, L* 18 / 60 layers, plan colours), 6 recipes registered
    ///   V1 plan B beats (0.08 / 0.18 / 0.30), plan G branch height, the shoulder camera ground point
    ///   V2 no emission / additive / glow / light in any vol.2 VFX material, the shader or a prefab; the shader is opaque cutout with depth passes
    ///   V3 the presenter files own no timer (no Time.time / deltaTime / coroutine / Invoke): lifetimes come from the executors' clock only
    ///   V4 the 30 casts (P0 - P5 x wood fire earth metal water): instance, template, body, strokes (the verb's shape), hollow ownership, dry-out
    ///   V5 the six end reasons (만 소 자 교 피 정): code, look-tail, retire time; the expiry warning window
    ///   V6 a clock jump of 10 s in one step ends a residue (no Time.time dependence); AbortAll clears at once with no tail
    ///   V7 contacts: only a budget-accepted contact is accented; A / F / R casts draw nothing in slice 9a
    ///   V8 seed follows its host and is pressed when worked; strip cut faces end at the strip's UntilUs; the return head goes out and back with the executor's report
    ///   V9 pool: views return, reused, no leaked object or mesh after the presenter is destroyed; the lasting cap is counted (not enforced)
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

        static readonly string[] Rows = { "가나마사아", "각낙막삭악", "간난만산안", "감남맘삼암", "갓낫맛삿앗", "강낭망상앙" };
        static readonly Body[] Bodies = { Body.Wood, Body.Fire, Body.Earth, Body.Metal, Body.Water };
        const long S = 1000000;

        static float LabL(Color c)
        {
            Func<float, float> lin = u => u <= 0.04045f ? u / 12.92f : Mathf.Pow((u + 0.055f) / 1.055f, 2.4f);
            float y = 0.2126f * lin(c.r) + 0.7152f * lin(c.g) + 0.0722f * lin(c.b);
            float f = y > 0.008856f ? Mathf.Pow(y, 1f / 3f) : 7.787f * y + 16f / 116f;
            return 116f * f - 16f;
        }

        static string HexOf(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        sealed class Setup : IDisposable
        {
            public Rig Rig; public Vol2VfxModel Model; public Vol2VfxCatalogue Cat;
            public readonly List<Vol2StrokeAttr> Strokes = new List<Vol2StrokeAttr>();
            public Setup(Vol2Rulebook book, Vol2VfxCatalogue cat, Vol2VfxRecipeRegistry recipes = null)
            {
                Cat = cat; Rig = Rig.Create(book); Model = new Vol2VfxModel(Rig.Coord, cat, recipes);
            }
            public long NowUs => Rig.Services.Clock.NowUs;
            /// <summary>Advance the hand clock (steps of dt) and tick the model on it, like the view does each frame.</summary>
            public void Drive(double seconds, double dt = 0.05)
            {
                int n = Math.Max(1, (int)Math.Round(seconds / dt));
                for (int i = 0; i < n; i++) { Rig.Advance(seconds / n); Model.Tick(NowUs); }
            }
            public Vol2VfxInstance Inst(Vol2CastLog log) { Model.TryGet(log.Cast, out var i); return i; }
            /// <summary>Writes the instance's strokes into a recording writer and returns them.</summary>
            public List<Vol2StrokeAttr> Write(Vol2VfxInstance inst)
            {
                var w = new Vol2InkWriter { Record = new List<Vol2StrokeAttr>() };
                inst.Recipe.Write(inst, w);
                return w.Record;
            }
            public void Dispose() { Model.Dispose(); }
        }

        /// <summary>The default registry with the kept NB20 recipes of A0 / F0 / R0 swapped in (their checks V4n / V4p stay valid while stroke20 is the registered look).</summary>
        static Vol2VfxRecipeRegistry Nb20Registry() => Vol2VfxRecipeRegistry.CreateDefault().Set(new Vol2Nb20AttachRecipe()).Set(new Vol2Nb20FanRecipe()).Set(new Vol2Nb20RingRecipe());

        /// <summary>stroke20 rule checks (D308-150 V7: the user judges the look by eye; the machine checks only the rules): the 20 no-batchim casts on the hand clock.</summary>
        static void Stroke20(Report r, Vol2Rulebook book, Vol2VfxCatalogue cat, bool fromAsset)
        {
            string[] rows = { "가나마사아", "거너머서어", "고노모소오", "구누무수우" };
            string[] ids = { "P0", "A0", "F0", "R0" };
            Type[] kinds = { typeof(Vol2StrokePathRecipe), typeof(Vol2StrokeAttachRecipe), typeof(Vol2StrokeFanRecipe), typeof(Vol2StrokeRingRecipe) };
            const double Frame60 = 1.0 / 60.0;
            const float Eps = 0.0015f;
            for (int f = 0; f < 4; f++)
                for (int bi = 0; bi < 5; bi++)
                {
                    char letter = rows[f][bi]; Body body = Bodies[bi]; string tag = ids[f] + body;
                    using (var s = new Setup(book, cat))
                    {
                        if (f == 0) s.Rig.Add("A", 4.6f, 0f); else if (f == 1) s.Rig.Add("A", 1.6f, 0.25f); else if (f == 2) s.Rig.Add("A", 3.4f, -0.3f); else s.Rig.Add("A", 1.9f, 0.5f);
                        var log = s.Rig.Cast(letter, out _);
                        var inst = log != null ? s.Inst(log) : null;
                        bool spawned = inst != null && inst.Template.id == ids[f] && inst.Look.body == body && inst.Recipe.GetType() == kinds[f];
                        if (!spawned) { Check(r, "V10." + tag, false, letter + " " + body + ": NOT SPAWNED as stroke20 " + ids[f]); continue; }
                        // ---- first cue: what is written in the first game frame, and the earliest appear of any stroke (plan B: a cue <= 0.08 s) ----
                        s.Drive(Frame60, Frame60);
                        var first = s.Write(inst);
                        float firstAppear = first.Count > 0 ? first.Min(x => x.Appear) : 99f;
                        bool brushOnly = first.Count > 0 && first.All(x => x.IsBrush && x.Hollow > 0f);
                        // ---- run to the contact (A0: a successful parry 0.22 s into the window, as the capture simulates it) ----
                        float tContact = -1f; long parryUs = -1;
                        List<Vol2StrokeAttr> atContact = null;
                        for (int i = 0; i < 40 && tContact < 0f; i++)
                        {
                            s.Drive(Frame60, Frame60);
                            if (f == 1)
                            {
                                if (inst.Rel(s.NowUs) >= 0.22f) { parryUs = s.NowUs; s.Model.Parried(new Vector3(0f, 1.15f, 1.05f), s.NowUs); tContact = inst.Rel(s.NowUs); }
                            }
                            else
                            {
                                var acc = log.Contacts.Where(x => x.BudgetAccepted).ToList();
                                if (acc.Count > 0) tContact = inst.Rel(acc.Min(x => x.NowUs));
                            }
                        }
                        s.Drive(0.07, Frame60);          // past the contact stroke's 0.06 s writing
                        atContact = s.Write(inst);
                        bool shape; string what;
                        int follow = atContact.Count(x => x.Role == Vol2StrokeRole.FollowHead), offs = atContact.Count(x => x.Role == Vol2StrokeRole.RigidOffset);
                        int flat = atContact.Count(x => x.Layout == Vol2StrokeLayout.BrushFlat), drift = atContact.Count(x => x.Role == Vol2StrokeRole.Drift);
                        bool slash = atContact.Any(x => x.Role == Vol2StrokeRole.Normal && x.Layout == Vol2StrokeLayout.Brush && x.Width >= 0.2f && x.Normal.sqrMagnitude > 0.5f && x.Appear >= tContact - Eps);
                        switch (f)
                        {
                            case 0: shape = follow == 1 && slash && drift >= 3; what = "flying stroke + small 앞잡 at the body + flecks"; break;
                            case 1: shape = offs == 3 && atContact.Count(x => x.Role == Vol2StrokeRole.Normal && x.Normal.sqrMagnitude > 0.5f && x.Appear >= tContact - Eps) >= 5 && atContact.Where(x => x.Role == Vol2StrokeRole.RigidOffset).All(x => Mathf.Abs(x.End - 0.5f) < 0.02f && x.EndCode == Vol2EndCode.Child); what = "shaft pulse + tip bead + sleeve pulse ending with the 0.5 s window + the parry burst (5 strokes)"; break;
                            case 2: shape = flat >= 5 && slash; what = "2 radial edges + far arc + 2 inner strokes (" + flat + " flat) + small 앞잡 on the body"; break;
                            default: shape = flat >= 6 && atContact.Count(x => x.Layout == Vol2StrokeLayout.Brush && x.Width >= 0.15f && x.Width < 0.2f) >= 2 && slash; what = "6 band strokes + standing pair + small 앞잡 on the body"; break;
                        }
                        // ---- dry-out window: every stroke but the riding pulses starts to crack <= 0.18 s after the contact (the contact look not before 0.12 s) and is gone 0.15 s later ----
                        float endMax = -1f, tailMax = 0f; bool contactLate = true;
                        foreach (var x in atContact)
                        {
                            if (x.Role == Vol2StrokeRole.RigidOffset) continue;
                            endMax = Mathf.Max(endMax, x.End); tailMax = Mathf.Max(tailMax, x.EndSeconds);
                            if (x.Role == Vol2StrokeRole.Normal && x.Appear >= tContact - Eps && x.End < tContact + 0.12f - Eps && x.Width >= 0.2f) contactLate = false;
                        }
                        bool dryWindow = tContact >= 0f && endMax <= Mathf.Max(tContact, 0f) + 0.18f + Eps && tailMax <= 0.15f + Eps && contactLate;
                        // ---- no residue: no part holds a material; alive through the accent, over by the end of its look-tail ----
                        bool noMat = inst.Parts.All(p => p.Mat == null) && log.Materials.All(m => m.Kind != Vol2MaterialKind.Residue);
                        float over = f == 1 ? 0.5f + 0.12f + 0.06f : Mathf.Max(tContact, 0f) + 0.12f + 0.15f + 0.06f;
                        float aliveAt = f == 1 ? 0.55f : Mathf.Max(tContact, 0f) + 0.20f;
                        s.Drive(Math.Max(0.0, aliveAt - inst.Rel(s.NowUs)), Frame60);
                        bool alive = s.Model.Instances.Count == 1;
                        s.Drive(Math.Max(0.0, over - inst.Rel(s.NowUs)), Frame60);
                        // gone = nothing of it is drawn any more (a burn cast stays open for its lasting damage with no look: the presenter has returned its view)
                        bool gone = s.Model.Instances.Count == 0 || s.Write(inst).Count == 0;       // nothing is written any more (an empty swing-ink part of a fixture without a brush draws nothing)
                        Check(r, "V10." + tag, brushOnly && shape, letter + " " + body + ": " + what + (shape ? "" : " NOT MET (" + atContact.Count + " strokes, follow " + follow + " offset " + offs + " flat " + flat + " drift " + drift + " slash " + slash + ")") + (brushOnly ? "" : ", not all brush ribbons"));
                        Check(r, "V10t." + tag, firstAppear <= 0.08f && first.Count > 0 && dryWindow && noMat && alive && gone,
                            letter + " " + body + ": first cue at " + firstAppear.ToString("0.000") + " s (<= 0.08), contact " + tContact.ToString("0.000") + " s, everything cracks by +" + (endMax - Mathf.Max(tContact, 0f)).ToString("0.000") + " s (<= 0.18) over <= " + tailMax.ToString("0.00") + " s, no residue " + noMat + ", alive at " + aliveAt.ToString("0.00") + " s " + alive + ", gone by " + over.ToString("0.00") + " s " + gone);
                    }
                }
            // ---- sizes (analytic, the settled shoulder camera pitch 0, FOV 60, 1080p: 935.3 px x size / camera depth): P0 at 3 / 6 / 12 m ----
            {
                string detail = ""; bool ok = true;
                foreach (float d in new[] { 3f, 6f, 12f })
                    using (var s = new Setup(book, cat))
                    {
                        s.Rig.Add("A", d, 0f);
                        var log = s.Rig.Cast('가', out _); var inst = log != null ? s.Inst(log) : null;
                        s.Drive(d / 36f + 0.12, Frame60);
                        var st = inst != null ? s.Write(inst) : new List<Vol2StrokeAttr>();
                        var trail = st.FirstOrDefault(x => x.Role == Vol2StrokeRole.FollowHead);
                        var head = st.Where(x => x.Role == Vol2StrokeRole.Normal && x.Normal.sqrMagnitude > 0.5f && x.Layout == Vol2StrokeLayout.Brush).Select(x => x.Width).DefaultIfEmpty(0f).Max();
                        float zc = d - 0.35f + 2.8f;
                        float pxTrail = 935.3f * trail.Width / zc, pxHead = 935.3f * head / zc;
                        bool okD = pxTrail >= 16f && pxHead >= 20f;
                        ok &= okD;
                        detail += d.ToString("0") + " m: path " + pxTrail.ToString("0") + " px, contact head " + pxHead.ToString("0") + " px" + (okD ? "" : " LOW") + "; ";
                    }
                Check(r, "V10s", ok, "minimum sizes on screen (P0, shoulder camera pitch 0): the flying stroke >= 16 px and the contact stroke head >= 20 px at 3 / 6 / 12 m [" + detail + "]");
            }
            // ---- glow 0: the brush branch is the same opaque cutout material (no second material, no blend, no light); the 앞잡 ink is near black ----
            {
                var mat = cat.inkMaterial;
                string src = File.Exists(Path.GetFullPath("Assets/_Project/Shaders/SpellVFX308/Vol2Ink.shader")) ? File.ReadAllText(Path.GetFullPath("Assets/_Project/Shaders/SpellVFX308/Vol2Ink.shader")) : "";
                var crack = mat != null && mat.HasProperty("_Crack") ? mat.GetTexture("_Crack") : null;
                float inkL = mat != null && mat.HasProperty("_BrushInk") ? LabL(mat.GetColor("_BrushInk")) : 99f, dryL = mat != null && mat.HasProperty("_BrushDry") ? LabL(mat.GetColor("_BrushDry")) : 99f;
                bool ok = mat != null && (!fromAsset || (crack != null && crack.name == "RipStroke308_Crack")) && inkL <= 12f && dryL <= 55f && !mat.IsKeywordEnabled("_EMISSION")
                    && Regex.Matches(Regex.Replace(src, "//[^\n]*", ""), @"BrushCut\(i\)").Count >= 3;
                Check(r, "V10g", ok, "glow 0: the brush strokes are the shared opaque-cutout ink material (V2a / V2c), the 앞잡 crack field on it" + (crack != null ? "" : " [no _Crack]") + ", wet ink L* " + inkL.ToString("0.0") + " (<= 12), dry L* " + dryL.ToString("0.0") + ", the depth passes cut the same brush texels");
            }
            // ---- pool / leak: the four templates through one presenter ----
            {
                int meshesBefore = Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m.name == "Vol2Vfx");
                Vol2VfxPresenter pres = null; string detail = ""; bool ok = true;
                using (var s = new Setup(book, cat))
                {
                    pres = Vol2VfxPresenter.Attach(s.Rig.Coord, null, cat, null, HideFlags.HideAndDontSave);
                    try
                    {
                        s.Rig.Add("A", 3.4f, 0.2f);
                        pres.Step();
                        foreach (char letter in "가거고구")
                        {
                            var log = s.Rig.Cast(letter, out _);
                            for (int i = 0; i < 60; i++)
                            {
                                s.Rig.Advance(0.02); pres.Step();
                                if (letter == '거' && i == 11) s.Model.Parried(new Vector3(0f, 1.15f, 1.0f), s.NowUs);
                            }
                            bool back = pres.ActiveViews == 0 && pres.PoolFree == pres.PoolCreated;
                            ok &= back && log != null; detail += letter + (back ? " returned; " : " NOT RETURNED; ");
                        }
                        bool empty = Resources.FindObjectsOfTypeAll<Mesh>().Where(m => m.name == "Vol2Vfx").All(m => m.vertexCount == 0);
                        ok &= pres.PoolCreated == 1 && empty; detail += "created " + pres.PoolCreated + ", meshes emptied " + empty;
                    }
                    finally { if (pres != null) { pres.Release(); UnityEngine.Object.DestroyImmediate(pres.gameObject); } }
                }
                int meshesAfter = Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m.name == "Vol2Vfx");
                Check(r, "V10p", ok && meshesAfter == meshesBefore, "pool: 가 거 고 구 one after another take ONE pooled view and give it back emptied; nothing left after the presenter is destroyed (meshes " + meshesBefore + " -> " + meshesAfter + ") [" + detail + "]");
            }
        }

        static void Cases(Report r)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "F0", book.Ok, book.Ok ? "rulebook loaded" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var cat = Vol2VfxCatalogue.Load();
            Check(r, "F1", cat != null, cat != null ? "catalogue loaded (" + (AssetDatabase.Contains(cat) ? "asset" : "in-memory default") + ")" : "NO CATALOGUE (shader not imported?)");
            if (cat == null) return;
            bool fromAsset = AssetDatabase.Contains(cat);

            // ---- V0 data ----
            {
                bool complete = cat.Complete(out string problem);
                var filled = cat.templates.Where(t => t.filled).Select(t => t.id).OrderBy(x => x).ToList();
                Check(r, "V0a", complete && cat.templates.Length == 24 && filled.SequenceEqual(new[] { "A0", "F0", "P0", "P1", "P2", "P3", "P4", "P5", "R0" }) && cat.templates.Select(t => t.id).Distinct().Count() == 24,
                    "24 distinct template ids, exactly P0 - P5 + the NB20 A0 F0 R0 filled (the other 15 A / F / R are later slices) [" + string.Join(",", filled) + "]" + (complete ? "" : " [" + problem + "]"));
                var recipes = Vol2VfxRecipeRegistry.CreateDefault();
                bool allMapped = true;
                foreach (var t in cat.templates.Where(t => t.filled))
                    allMapped &= recipes.TryGet(t.recipe, out _) && t.recipe == Vol2VfxDefaults.RecipeOf(new Vol2TemplateId(t.frame, t.verb))
                        && (t.frame == Frame.Path ? (int)t.recipe == (int)t.verb + 1 : t.verb == Verb.Once);
                Check(r, "V0b", recipes.Count == 9 && allMapped && recipes.TryGet(Vol2Recipe.PathOneHit, out var p0r) && p0r is Vol2StrokePathRecipe
                    && recipes.TryGet(Vol2Recipe.AttachOnce, out var a0r) && a0r is Vol2StrokeAttachRecipe && recipes.TryGet(Vol2Recipe.FanOnce, out var f0r) && f0r is Vol2StrokeFanRecipe
                    && recipes.TryGet(Vol2Recipe.RingOnce, out var r0r) && r0r is Vol2StrokeRingRecipe,
                    "nine recipes registered, one per filled template: the Path frame x its verb and the 한 번 of Attachment / Fan / Ring; the 20 no-batchim P0 / A0 / F0 / R0 = stroke20 (the small 앞잡); the slice 9a P0 and the NB20 ones are kept unregistered");
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
                Check(r, "V0g", Mathf.Approximately(t0.revealSeconds, 0.08f) && Mathf.Approximately(t0.outlineSeconds, 0.18f) && Mathf.Approximately(t0.surfaceSeconds, 0.30f) && Mathf.Approximately(t0.accentSeconds, 0.09f)
                    && Mathf.Approximately(t0.accentStableSeconds, 0.12f) && Mathf.Approximately(t0.expiryWarnSeconds, 0.4f) && Mathf.Approximately(t0.endExpireSeconds, 0.30f) && Mathf.Approximately(t0.endConsumeSeconds, 0.15f)
                    && Mathf.Approximately(t0.endChildSeconds, 0.30f) && Mathf.Approximately(t0.endReplaceSeconds, 0.20f) && Mathf.Approximately(t0.endHitCancelSeconds, 0.08f) && Mathf.Approximately(t0.endCleanseSeconds, 0.25f),
                    "plan B numbers in the data: beats 0.08 / 0.18 / 0.30, accent 0.09 + 0.12, expiry warning 0.4, end tails 만 0.30 / 소 0.15 / 자 0.30 / 교 0.20 / 피 0.08 / 정 0.25");
            }

            // ---- V1 beats ----
            {
                var t = cat.Template(new Vol2TemplateId(Frame.Path, Verb.Leave));
                Check(r, "V1a", Vol2VfxBeats.PhaseAt(t, 0.05f) == Vol2VfxPhase.Reveal && Vol2VfxBeats.PhaseAt(t, 0.10f) == Vol2VfxPhase.Outline && Vol2VfxBeats.PhaseAt(t, 0.20f) == Vol2VfxPhase.Surface && Vol2VfxBeats.PhaseAt(t, 0.35f) == Vol2VfxPhase.Settled
                    && Vol2VfxBeats.PhaseAt(t, 0.08f) == Vol2VfxPhase.Outline && Vol2VfxBeats.PhaseAt(t, 0.18f) == Vol2VfxPhase.Surface && Vol2VfxBeats.PhaseAt(t, 0.30f) == Vol2VfxPhase.Settled,
                    "phase at 0.05 / 0.10 / 0.20 / 0.35 s = Reveal / Outline / Surface / Settled; the boundaries 0.08 / 0.18 / 0.30 belong to the later beat");
                Check(r, "V1b", Mathf.Approximately(Vol2VfxBeats.Settle(t, 0.18f), 0f) && Mathf.Approximately(Vol2VfxBeats.Settle(t, 0.30f), 1f) && Mathf.Abs(Vol2VfxBeats.Settle(t, 0.24f) - 0.5f) < 0.001f,
                    "only the tone settles between 0.18 and 0.30 s (0 -> 1)");
                Check(r, "V1c", Mathf.Approximately(Vol2VfxBeats.BranchMaxHeight(2f), 0.4f) && Mathf.Approximately(Vol2VfxBeats.BranchMaxHeight(3f), 0.4f) && Mathf.Approximately(Vol2VfxBeats.BranchMaxHeight(4f), 0.65f) && Mathf.Approximately(Vol2VfxBeats.BranchMaxHeight(5f), 0.9f) && Mathf.Approximately(Vol2VfxBeats.BranchMaxHeight(9f), 0.9f),
                    "plan G h_max(d) = 0.4 + 0.25 x clamp(d - 3, 0, 2): 0.4 (<= 3 m), 0.65 (4 m), 0.9 (>= 5 m)");
                Vector3 g = Vol2VfxBeats.ShoulderGround(Vector3.zero, Vector3.forward);
                Check(r, "V1d", Vector3.Distance(g, new Vector3(0.7f, 0f, -2.8f)) < 0.001f, "the settled shoulder camera stands at (0.7, -2.8) from the cast point on the ground");
            }

            // ---- V2 hygiene ----
            {
                string shaderPath = "Assets/_Project/Shaders/SpellVFX308/Vol2Ink.shader";
                string src = File.Exists(Path.GetFullPath(shaderPath)) ? File.ReadAllText(Path.GetFullPath(shaderPath)) : "";
                string code = Regex.Replace(src, "//[^\n]*", "");
                var forbidden = new[] { @"Blend\s+(One|SrcAlpha|OneMinusSrcAlpha|SrcColor|DstColor|Zero)\b", @"_Glow", @"_Radial", @"[Ee]mission", @"_Heat", @"ZWrite\s+Off", @"_EMISSION" };
                var hits = forbidden.Where(p => Regex.IsMatch(code, p)).ToList();
                Check(r, "V2a", src.Length > 0 && hits.Count == 0 && code.Contains("clip(") && Regex.Matches(code, "LightMode\"=\"(UniversalForward|DepthOnly|DepthNormals)\"").Count == 3 && code.Contains("Cull Off"),
                    "the ink shader: no blend, no emission / glow / radial / heat, no ZWrite Off; opaque cutout (clip) with UniversalForward + DepthOnly + DepthNormals" + (hits.Count > 0 ? " [found " + string.Join(",", hits) + "]" : ""));
                var shader = Shader.Find(Vol2VfxCatalogue.ShaderName);
                Check(r, "V2b", shader != null && shader.isSupported && !shader.name.ToLowerInvariant().Contains("error"), "the shader is imported and supported" + (shader != null ? "" : " [not found]"));
                bool matsOk = true; int matCount = 0; string matBad = "";
                var guids = AssetDatabase.FindAssets("t:Material", new[] { Vol2VfxBuild.Root });
                foreach (string guid in guids)
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)); if (m == null) continue;
                    matCount++;
                    bool ok = m.shader != null && m.shader.name == Vol2VfxCatalogue.ShaderName && !m.IsKeywordEnabled("_EMISSION")
                        && (!m.HasProperty("_EmissionColor") || m.GetColor("_EmissionColor").maxColorComponent <= 0f)
                        && (!m.HasProperty("_SrcBlend") || (int)m.GetFloat("_SrcBlend") != 1 || (int)m.GetFloat("_DstBlend") != 1)
                        && (!m.HasProperty("_Glow") || m.GetFloat("_Glow") == 0f) && (!m.HasProperty("_Radial") || m.GetFloat("_Radial") == 0f);
                    if (!ok) { matsOk = false; matBad += m.name + "; "; }
                }
                var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { Vol2VfxBuild.Root });
                bool noLight = true;
                foreach (string guid in prefabs) { var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)); if (go != null && go.GetComponentInChildren<Light>(true) != null) noLight = false; }
                Check(r, "V2c", matsOk && noLight && (matCount >= 1 || !fromAsset), matCount + " vol.2 VFX material(s), " + prefabs.Length + " prefab(s): ink shader only, no _EMISSION keyword, no emission colour, no additive blend, no glow / radial, no Light" + (matBad.Length > 0 ? " [" + matBad + "]" : ""));
                var mat = cat.inkMaterial;
                Check(r, "V2e", mat == null || !fromAsset || (mat.HasProperty("_Bristle") && mat.GetTexture("_Bristle") != null && mat.GetTexture("_Bristle").name == "RipStroke308_Bristle"), "the ink material carries the riposte one-stroke bristle mask (RipStroke308_Bristle) as the stroke texture");
                {
                    // NB20: the authored atlas pair on the shared material, imported as data (linear), mips keeping the cutout coverage; the shader's sheet path has no emission either (V2a)
                    var sheet = mat != null && mat.HasProperty("_Sheet") ? mat.GetTexture("_Sheet") as Texture2D : null;
                    var time = mat != null && mat.HasProperty("_SheetTime") ? mat.GetTexture("_SheetTime") as Texture2D : null;
                    var ti = AssetImporter.GetAtPath(Vol2VfxBuild.SheetTexture) as TextureImporter; var tt = AssetImporter.GetAtPath(Vol2VfxBuild.SheetTimeTexture) as TextureImporter;
                    bool ok = !fromAsset || (sheet != null && time != null && sheet.width == 4096 && sheet.height == 4096 && time.width == 4096 && ti != null && tt != null && !ti.sRGBTexture && !tt.sRGBTexture
                        && ti.mipmapEnabled && ti.mipMapsPreserveCoverage && sheet.name == "NB20_Sheet" && time.name == "NB20_SheetTime");
                    Check(r, "V2f", ok, "NB20: the ink material carries the authored atlas pair (NB20_Sheet 4096 linear, coverage-preserving mips + NB20_SheetTime 4096 linear)" + (sheet == null ? " [no _Sheet]" : "") + (time == null ? " [no _SheetTime]" : ""));
                    bool paper = mat == null || !mat.HasProperty("_PaperColor") || LabL(mat.GetColor("_PaperColor")) <= 86f;
                    Check(r, "V2g", paper && src.Contains("SheetCut(i)") && Regex.Matches(code, @"SheetCut\(i\)").Count >= 3, "NB20 sheet path: the depth passes cut the same texels as the colour pass; the paper rim is a flat colour below paper white (L* <= 86), no light");
                }
                Check(r, "V2d", mat != null && mat.shader != null && mat.shader.name == Vol2VfxCatalogue.ShaderName && !mat.IsKeywordEnabled("_EMISSION") && mat.renderQueue == 2450 && mat.renderQueue < 3000,
                    "the catalogue's ink material: the ink shader, opaque cutout queue " + (mat != null ? mat.renderQueue.ToString() : "-") + ", no emission keyword");
            }

            // ---- V3 no presenter-owned lifetime ----
            {
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "_Project/Scripts/App/Vol2/Presentation"));
                var files = new[] { "Vol2VfxData.cs", "Vol2VfxModel.cs", "Vol2VfxRecipes.cs", "Vol2Nb20Recipes.cs", "Vol2StrokeRecipes.cs", "Vol2VfxPresenter.cs", "Vol2InkMesh.cs", "Vol2BodyLook.cs", "Vol2VfxTemplate.cs", "Vol2VfxCatalogue.cs" };
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
                Check(r, "V3", scanned == files.Length && found.Length == 0, "the presenter / model / recipe / data sources use no Time.*, coroutine, Invoke or delay: " + scanned + " files scanned" + (found.Length > 0 ? " [" + found + "]" : ""));
            }

            // ---- V4 the 30 casts ----
            for (int verb = 0; verb < 6; verb++)
                for (int bi = 0; bi < 5; bi++)
                {
                    char letter = Rows[verb][bi]; Body body = Bodies[bi]; string tag = "P" + verb + body;
                    using (var s = new Setup(book, cat))
                    {
                        var a = s.Rig.Add("A", 4f, 0f);
                        if (verb == 2) { s.Rig.Add("N1", 5f, 1.6f); s.Rig.Add("N2", 4.8f, -1.7f); }
                        var log = s.Rig.Cast(letter, out var dec);
                        var inst = log != null ? s.Inst(log) : null;
                        bool spawned = inst != null && inst.Template.id == "P" + verb && inst.Look.body == body && inst.Recipe.Kind == (Vol2Recipe)(verb + 1);
                        s.Drive(verb == 1 ? 2.0 : 0.3);        // 각 (the wood 남긴다 peak) cages its body for 1.5 s before the residue stands
                        var strokes = inst != null ? s.Write(inst) : new List<Vol2StrokeAttr>();
                        int follow = strokes.Count(x => x.Role == Vol2StrokeRole.FollowHead), drift = strokes.Count(x => x.Role == Vol2StrokeRole.Drift), offset = strokes.Count(x => x.Role == Vol2StrokeRole.RigidOffset), flat = strokes.Count(x => x.Layout == Vol2StrokeLayout.Flat);
                        int normalBillboard = strokes.Count(x => x.Role == Vol2StrokeRole.Normal && x.Layout == Vol2StrokeLayout.Billboard);
                        bool shape;
                        string what;
                        switch (verb)
                        {
                            case 0:
                            {
                                // stroke20 P0: one flying brush stroke following the head + the small 앞잡 at the body (+ needles + 3 - 5 flecks + the body accent), every stroke a brush ribbon
                                int fl = strokes.Count(x => x.Role == Vol2StrokeRole.Drift && !x.Ash);
                                shape = follow == 1 && strokes.All(x => x.IsBrush) && strokes.Any(x => x.Role == Vol2StrokeRole.Normal && x.Width >= 0.28f) && (body == Body.Fire ? drift >= 4 : fl >= 3 && fl <= 5);
                                what = "flying brush stroke + small 앞잡 + needles + " + drift + " flecks (stroke20 P0)"; break;
                            }
                            case 1: shape = follow <= 1 && normalBillboard >= 6 && inst.Parts.Any(p => p.Mat != null); what = "stroke + trunk, 3 branches, 2 twigs (" + normalBillboard + " standing strokes)"; break;
                            case 2: shape = follow == 1 && flat >= 8; what = "stroke + flat bundles (" + flat + " flat strokes)"; break;
                            case 3: shape = follow == 1 && inst.Parts.Any(p => p.Mat != null && p.Mat.Tag == Vol2SeedExecutor.TagSeed) && normalBillboard >= 2; what = "stroke + a stem and a ring standing on the host"; break;
                            case 4: shape = follow == 4 && flat == 3 && normalBillboard >= 2; what = "chest blade + engraved cut (centre + 2 lips) + a pair of cut faces"; break;
                            default: shape = follow == 1 && offset == 1; what = "stroke + one closed loop riding the head"; break;
                        }
                        bool mine = strokes.Where(x => x.Role != Vol2StrokeRole.Drift).All(x => x.Hollow > 0f || (verb == 4 && x.Width <= 0.15f));
                        Check(r, "V4." + tag, spawned && shape && mine, letter + " " + body + ": instance P" + verb + " x " + body + (spawned ? "" : " NOT SPAWNED") + ", " + what + (shape ? "" : " NOT MET (strokes " + strokes.Count + ")") + (mine ? "" : ", a stroke is solid (not mine)"));
                        // everything ends by itself on the executors' clock
                        s.Drive(14.0, 0.25);
                        Check(r, "V4e." + tag, s.Model.Instances.Count == 0 && s.Model.RetiredCount == 1 && log != null && log.Finished, letter + " " + body + ": the cast and its look are over after the executors' own ends (retired " + s.Model.RetiredCount + ", live " + s.Model.Instances.Count + ")");
                    }
                }

            // ---- V4b NB20 P0 (kept, unregistered: swapped in with Set) x five bodies ----
            for (int bi = 0; bi < 5; bi++)
            {
                char letter = Rows[0][bi]; Body body = Bodies[bi];
                using (var s = new Setup(book, cat, Vol2VfxRecipeRegistry.CreateDefault().Set(new Vol2Nb20PathRecipe())))
                {
                    s.Rig.Add("A", 4f, 0f);
                    var log = s.Rig.Cast(letter, out _);
                    var inst = log != null ? s.Inst(log) : null;
                    s.Drive(0.3);
                    var strokes = inst != null ? s.Write(inst) : new List<Vol2StrokeAttr>();
                    int drift = strokes.Count(x => x.Role == Vol2StrokeRole.Drift);
                    // the flying head has folded into the contact by 0.3 s; the tail (rigid with the head, revealed by it) still dries; 3 upright contact sheets + 3 thrown flat on the ground + 3 - 5 flecks
                    int sheets = strokes.Count(x => x.IsSheet && x.Cell >= 0 && x.Cell < Vol2Nb20Atlas.Cells), contactSheets = strokes.Count(x => x.IsSheet && x.Role == Vol2StrokeRole.Normal && x.Cell >= Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.Contact, Body.Wood) && x.Cell <= Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.SupportB, Body.Water));
                    bool tail = strokes.Any(x => x.Role == Vol2StrokeRole.RigidOffset && (x.Sheet & Vol2SheetFlags.RevealByHead) != 0 && x.Cell == Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.Tail, body));
                    bool bodyCells = strokes.Where(x => x.IsSheet && x.Cell < 50).All(x => x.Cell % 5 == Vol2Nb20Atlas.BodyIndex(body));
                    bool shape = inst != null && inst.Recipe is Vol2Nb20PathRecipe && sheets == strokes.Count && tail && contactSheets == 6 && strokes.Count(x => x.Layout == Vol2StrokeLayout.SheetFlat) == 3 && drift >= 3 && drift <= 5 && bodyCells;
                    Check(r, "V4b.P0" + body, shape, letter + " " + body + " (NB20 P0 swapped in): tail + " + contactSheets + " contact sheets + " + drift + " flecks, every element the " + body + " variant");
                    s.Drive(3.0, 0.1);
                    Check(r, "V4be.P0" + body, s.Model.Instances.Count == 0 && s.Model.RetiredCount == 1, letter + " " + body + " (NB20 P0): over after the executor's own end");
                }
            }

            // ---- V4n NB20 (kept, unregistered since stroke20: swapped in with Set): the 15 casts of A0 / F0 / R0 x five bodies (P0 is in V4b) ----
            {
                string[] rows = { "거너머서어", "고노모소오", "구누무수우" };
                string[] ids = { "A0", "F0", "R0" };
                for (int f = 0; f < 3; f++)
                    for (int bi = 0; bi < 5; bi++)
                    {
                        char letter = rows[f][bi]; Body body = Bodies[bi]; string tag = ids[f] + body;
                        using (var s = new Setup(book, cat, Nb20Registry()))
                        {
                            if (f == 0) s.Rig.Add("A", 1.6f, 0f); else if (f == 1) s.Rig.Add("A", 3.4f, 0f); else s.Rig.Add("A", 1.9f, 0.5f);
                            var log = s.Rig.Cast(letter, out _);
                            var inst = log != null ? s.Inst(log) : null;
                            bool spawned = inst != null && inst.Template.id == ids[f] && inst.Look.body == body && inst.Recipe.Kind == (Vol2Recipe)(7 + f);
                            s.Drive(0.05, 0.025);
                            var strokes = inst != null ? s.Write(inst) : new List<Vol2StrokeAttr>();
                            bool allSheets = strokes.Count > 0 && strokes.All(x => x.IsSheet && x.Cell >= 0 && x.Cell < Vol2Nb20Atlas.Cells && x.Hollow > 0f);
                            bool bodyCells = strokes.Where(x => x.Cell < 50).All(x => x.Cell % 5 == Vol2Nb20Atlas.BodyIndex(body));
                            bool shape; string what;
                            if (f == 0)
                            {
                                int pulses = strokes.Count(x => x.Role == Vol2StrokeRole.RigidOffset && x.Cell == Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.Pulse, body));
                                bool endsAtWindow = strokes.Where(x => x.Role == Vol2StrokeRole.RigidOffset).All(x => Mathf.Abs(x.End - 0.5f) < 0.02f && x.EndCode == Vol2EndCode.Child);
                                shape = pulses == 2 && endsAtWindow && !strokes.Any(x => x.Cell >= Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.Contact, Body.Wood) && x.Cell <= Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.SupportB, Body.Water));
                                what = "a shaft pulse + a sleeve pulse riding the brush, ending with the 0.5 s window (child-dry), no splash without a parry";
                            }
                            else if (f == 1)
                            {
                                var flat = strokes.Where(x => x.Layout == Vol2StrokeLayout.SheetFlat).ToList();
                                int edges = flat.Count(x => x.Cell == Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.FanRadial, body)), arc = flat.Count(x => x.Cell == Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.FanArc, body));
                                bool order = flat.Where(x => x.Cell == Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.FanArc, body)).All(x => x.Appear > 0.02f);
                                bool contact = strokes.Count(x => x.Layout == Vol2StrokeLayout.Sheet && x.Cell >= 10 && x.Cell < 25) == 3;
                                shape = edges == 4 && arc == 1 && order && contact;
                                what = "two radial edges + the far arc (+33 ms) + two inner strokes flat on the ground, a contact look on the body in the fan (" + flat.Count + " flat)";
                            }
                            else
                            {
                                int band = strokes.Count(x => x.Layout == Vol2StrokeLayout.SheetFlat && x.Cell == Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.RingArc, body));
                                int standing = strokes.Count(x => x.Layout == Vol2StrokeLayout.Sheet && x.Cell == Vol2Nb20Atlas.Cell(Vol2Nb20Atlas.E.Standing, body));
                                bool contact = strokes.Count(x => x.Layout == Vol2StrokeLayout.Sheet && x.Cell >= 10 && x.Cell < 25) == 3;
                                shape = band == 6 && standing == 2 && contact;
                                what = "six band strokes (r 2 m) + a standing pair + a contact look on the body in the band";
                            }
                            bool dry = strokes.Where(x => x.Role != Vol2StrokeRole.Drift && x.Role != Vol2StrokeRole.RigidOffset).All(x => x.End < 0.5f && x.EndSeconds <= 0.15f + 1e-4f);
                            Check(r, "V4n." + tag, spawned && allSheets && bodyCells && shape && (f == 0 || dry), letter + " " + body + ": " + what + (spawned ? "" : " NOT SPAWNED") + (shape ? "" : " NOT MET (" + strokes.Count + " elements)") + (allSheets ? "" : ", not all NB20 sheets") + (bodyCells ? "" : ", a cell of another body") + (f == 0 || dry ? "" : ", something does not dry by 0.27 s"));
                            s.Drive(3.0, 0.1);
                            Check(r, "V4ne." + tag, s.Model.Instances.Count == 0 && s.Model.RetiredCount == 1, letter + " " + body + ": over after the executor's own end (retired " + s.Model.RetiredCount + ", live " + s.Model.Instances.Count + ")");
                        }
                    }
                using (var s = new Setup(book, cat, Nb20Registry()))
                {
                    s.Rig.Add("A", 1.6f, 0f);
                    var log = s.Rig.Cast('거', out _); s.Drive(0.1, 0.05);
                    var inst = s.Inst(log);
                    int before = inst != null ? inst.Parts.Count : -1;
                    s.Model.Parried(new Vector3(0f, 1.1f, 1.0f), s.NowUs);
                    s.Model.Parried(new Vector3(0f, 1.1f, 1.0f), s.NowUs);       // the same impact relayed twice: one splash
                    var strokes = inst != null ? s.Write(inst) : new List<Vol2StrokeAttr>();
                    bool splash = inst != null && inst.Parts.Count == before + 2 && strokes.Any(x => x.Cell == Vol2Nb20Atlas.Ignite) && strokes.Count(x => x.Cell >= 10 && x.Cell < 25) == 3 && strokes.Count(x => x.Role == Vol2StrokeRole.Drift) == 3;
                    s.Drive(0.6, 0.05);
                    s.Model.Parried(new Vector3(0f, 1.1f, 1.0f), s.NowUs);       // after the window: nothing
                    Check(r, "V4p", splash && s.Model.ParriesDrawn == 1, "거: a successful parry inside the window splashes once (needle star + contact stroke + 3 flecks); a repeat of the same impact and a parry after the window draw nothing (drawn " + s.Model.ParriesDrawn + ")");
                }
            }

            // ---- V10 stroke20 (D308-150 V7): the 20 no-batchim casts, the rule checks ----
            Stroke20(r, book, cat, fromAsset);

            // ---- V5 end reasons on a residue (낙: fire, 남긴다, 10 s) ----
            {
                var reasons = new[] { (Vol2MaterialEnd.Consumed, Vol2EndCode.Consume, 0.15f), (Vol2MaterialEnd.ChildConsumed, Vol2EndCode.Child, 0.30f), (Vol2MaterialEnd.Replaced, Vol2EndCode.Replace, 0.20f), (Vol2MaterialEnd.HitCancelled, Vol2EndCode.HitCancel, 0.08f), (Vol2MaterialEnd.Cleansed, Vol2EndCode.Cleanse, 0.25f) };
                foreach (var (why, code, tail) in reasons)
                {
                    using (var s = new Setup(book, cat))
                    {
                        s.Rig.Add("A", 4f, 0f);
                        var log = s.Rig.Cast('낙', out _); s.Drive(1.0);
                        var inst = s.Inst(log); var m = log.Materials.First(x => x.Kind == Vol2MaterialKind.Residue);
                        var part = inst.Parts.First(p => p.Mat == m);
                        long at = s.NowUs;
                        s.Rig.Coord.Materials.TryEnd(m.Id, why, at); s.Model.Tick(s.NowUs);
                        var strokes = s.Write(inst);
                        var tree = strokes.Where(x => x.Role == Vol2StrokeRole.Normal && x.Layout == Vol2StrokeLayout.Billboard && x.EndCode == code).ToList();
                        bool timing = tree.Count >= 6 && tree.All(x => Mathf.Abs(x.End - inst.Rel(at)) < 0.001f && Mathf.Abs(x.EndSeconds - tail) < 0.001f && x.Warn == 0f);
                        s.Drive(tail - 0.06, 0.02);
                        bool stillAlive = inst.Parts.Where(p => p == part).All(p => !inst.PartRetired(p, s.NowUs));
                        s.Drive(0.12, 0.02);
                        bool retired = inst.PartRetired(part, s.NowUs);
                        Check(r, "V5." + why, part.Ended && part.Code == code && part.EndUs == at && timing && stillAlive && retired,
                            Vol2EndCodes.Name(code) + " (" + why + "): the part ends on the event frame with code " + code + ", every tree stroke carries it (end " + inst.Rel(at).ToString("0.00") + " s, tail " + tail.ToString("0.00") + " s), alive until the tail is over, then retired");
                    }
                }
                using (var s = new Setup(book, cat))
                {
                    s.Rig.Add("A", 4f, 0f);
                    var log = s.Rig.Cast('낙', out _); s.Drive(1.0);
                    var inst = s.Inst(log); var m = log.Materials.First(x => x.Kind == Vol2MaterialKind.Residue); var part = inst.Parts.First(p => p.Mat == m);
                    long expiry = m.ExpiresUs;
                    var live = s.Write(inst).Where(x => x.Role == Vol2StrokeRole.Normal && x.Layout == Vol2StrokeLayout.Billboard && x.End < 50f).ToList();
                    bool warn = live.Count >= 6 && live.All(x => Mathf.Abs(x.Warn - 0.4f) < 0.001f && Mathf.Abs(x.End - inst.Rel(expiry)) < 0.001f && x.EndCode == Vol2EndCode.Expire);
                    s.Drive((expiry - s.NowUs) / 1e6 + 0.02, 0.1);
                    Check(r, "V5.Expired", warn && part.Ended && part.Code == Vol2EndCode.Expire && part.EndUs >= expiry && part.EndUs < expiry + 150000, "만: the residue's scheduled end (created + 10 s) is read live: the strokes carry the 0.4 s warning window and end " + inst.Rel(expiry).ToString("0.00") + " s; the Expired event ends the part with the body behaviour");
                    s.Drive(0.4, 0.05);
                    Check(r, "V5.ExpiredTail", s.Model.Instances.Count == 0, "만: gone 0.3 s after the event (the natural dissolve), the instance retires");
                }
            }

            // ---- V6 clock jump / abort ----
            {
                using (var s = new Setup(book, cat))
                {
                    s.Rig.Add("A", 4f, 0f);
                    var log = s.Rig.Cast('악', out _); s.Drive(0.4);
                    var inst = s.Inst(log);
                    s.Rig.Advance(10.7); s.Model.Tick(s.NowUs);           // the lab's fast forward: one long frame
                    var part = inst.Parts.FirstOrDefault(p => p.Mat != null);
                    bool ended = part != null && part.Ended && part.Code == Vol2EndCode.Expire;
                    s.Drive(0.4, 0.1);
                    Check(r, "V6a", ended && s.Model.Instances.Count == 0, "a 10.7 s clock jump in ONE step ends the residue (만) and the instance retires 0.3 s later: the look follows the scope clock, not frames or Time.time");
                }
                using (var s = new Setup(book, cat))
                {
                    s.Rig.Add("A", 4f, 0f);
                    var log = s.Rig.Cast('낙', out _); s.Drive(0.6);
                    int before = s.Model.Instances.Count;
                    s.Rig.Coord.AbortAll(); s.Model.Tick(s.NowUs);
                    Check(r, "V6b", before == 1 && s.Model.Instances.Count == 0 && s.Model.AbortedCount == 1, "AbortAll (the lab reset between rows) clears the instance at once, no tail, counted as aborted");
                }
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
                    Check(r, "V7b", s.Model.SpawnedCount == 3 && s.Model.Covers(parry) && s.Model.Covers(fan) && s.Model.Covers(ring) && (leave == null || !s.Model.Covers(leave)),
                        "NB20: the 한 번 of A / F / R (거 고 구) spawn their look (" + s.Model.SpawnedCount + "); a later-slice A / F / R template (곡) is still left to the debug lines");
                }
            }

            // ---- V8 seed / strip / return ----
            {
                using (var s = new Setup(book, cat))
                {
                    var host = s.Rig.Add("A", 5f, 0f);
                    var seedCast = s.Rig.Cast('감', out _); s.Drive(0.4);
                    var inst = s.Inst(seedCast); var part = inst.Parts.First(p => p.Mat != null);
                    Vector3 p0 = part.Pos;
                    host.MoveTo(Vol2ShapeFixtureKit.At(5f, 1.2f)); s.Drive(0.2);
                    bool follows = (part.Pos - p0).magnitude > 0.5f && Mathf.Abs(part.Pos.x - 1.2f) < 0.3f && part.Pos.z < 5f;
                    s.Rig.Caster.AimDirection = new Vector3(1.2f, 0f, 5f).normalized;        // the direct hit goes at the host where it stands now
                    var hit = s.Rig.Cast('가', out _); s.Drive(0.3);
                    bool pressed = part.Ended && part.Code == Vol2EndCode.Consume && inst.Parts.Any(p => p.Kind == 11);
                    Check(r, "V8a", follows && pressed, "감: the seed dot stands on its host's near side and follows it (" + p0.x.ToString("0.0") + " -> " + part.Pos.x.ToString("0.0") + "); a later direct hit works it: ended 소 with the spatter accent [ended=" + part.Ended + " code=" + part.Code + " accents=" + inst.Parts.Count(p => p.Kind == 11) + " hitContacts=" + (hit != null ? hit.Contacts.Count : -1) + "]");
                }
                using (var s = new Setup(book, cat))
                {
                    s.Rig.Add("A", 4f, 0f);
                    var log = s.Rig.Cast('갓', out _); s.Drive(0.5);
                    var inst = s.Inst(log); var cut = inst.Parts.FirstOrDefault(p => p.Kind == 50);
                    s.Rig.Coord.Verbs.TryGet(Verb.Split, out var exAny); var ex = exAny as Vol2SplitExecutor;
                    long until = ex != null ? ex.Strips.Entries.Where(e => e.Cast == log.Cast).Select(e => e.UntilUs).DefaultIfEmpty(0).Max() : 0;
                    bool sched = cut != null && until > 0 && cut.EndSchedule == until && !cut.Ended;
                    s.Drive((until - s.NowUs) / 1e6 + 0.05, 0.1);
                    Check(r, "V8b", sched && cut.Ended && cut.EndUs == until && cut.Code == Vol2EndCode.Expire, "갓: the pair of cut faces lasts exactly the strip (the executor's UntilUs, 3 s) and ends there");
                }
                using (var s = new Setup(book, cat))
                {
                    s.Rig.Add("A", 4f, 0f);
                    var log = s.Rig.Cast('강', out _);
                    var inst = s.Inst(log);
                    var heads = new List<float>(); var times = new List<long>();
                    for (int i = 0; i < 60; i++) { s.Drive(0.02, 0.02); heads.Add(inst.Recipe.Head(inst, s.NowUs)); times.Add(s.NowUs); if (inst.Retired) break; }
                    s.Rig.Coord.Verbs.TryGet(Verb.Return, out var rexAny); var rex = rexAny as Vol2ReturnExecutor; var rep = rex != null ? rex.ReportOf(log.Cast) : null;
                    int peak = heads.IndexOf(heads.Max());
                    bool outAndBack = rep != null && rep.BackStartUs > 0 && rep.BackEndUs > rep.BackStartUs && heads.Max() > 10f && peak > 3 && peak < heads.Count - 3 && heads[heads.Count - 1] < 1.5f;
                    bool monoOut = true, monoBack = true;
                    for (int i = 1; i <= peak; i++) monoOut &= heads[i] >= heads[i - 1] - 0.001f;
                    for (int i = peak + 1; i < heads.Count; i++) monoBack &= heads[i] <= heads[i - 1] + 0.001f;
                    Check(r, "V8c", outAndBack && monoOut && monoBack, "강: the head goes out to " + heads.Max().ToString("0.0") + " m (36 m/s) and comes back to the caster, steadily, on the executor's report (back leg " + (rep != null ? ((rep.BackEndUs - rep.BackStartUs) / 1000) + " ms" : "none") + ")");
                    s.Drive(1.0, 0.05);
                    Check(r, "V8d", s.Model.Instances.Count == 0, "강: the loop and the stroke dry 0.15 s after the return and the instance retires");
                }
            }

            // ---- V9 pool, leaks, cap ----
            {
                var sceneDirtyBefore = UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;
                int meshesBefore = Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m.name == "Vol2Vfx");
                int objectsBefore = Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.name == "Vol2Vfx" || g.name == "Vol2VfxPresenter");
                Vol2VfxPresenter pres = null;
                using (var s = new Setup(book, cat))
                {
                    pres = Vol2VfxPresenter.Attach(s.Rig.Coord, null, cat, null, HideFlags.HideAndDontSave);
                    try
                    {
                        s.Rig.Add("A", 4f, 0f);
                        pres.Step();
                        var log1 = s.Rig.Cast('가', out _);
                        for (int i = 0; i < 6; i++) { s.Rig.Advance(0.05); pres.Step(); }
                        bool active = pres.ActiveViews == 1 && pres.PoolCreated == 1 && pres.Rebuilds >= 1 && pres.LastVertices > 0;
                        for (int i = 0; i < 20; i++) { s.Rig.Advance(0.05); pres.Step(); }
                        bool returned = pres.ActiveViews == 0 && pres.PoolFree == pres.PoolCreated;
                        var log2 = s.Rig.Cast('나', out _);
                        for (int i = 0; i < 40; i++) { s.Rig.Advance(0.05); pres.Step(); }
                        bool reused = pres.PoolCreated == 1 && pres.ActiveViews == 0 && pres.PoolFree == 1 && pres.Drawn == 2;
                        Check(r, "V9a", active && returned && reused, "one cast takes one pooled view (mesh built), gives it back when the look is over, the next cast reuses it (created " + pres.PoolCreated + ", free " + pres.PoolFree + ", drawn " + pres.Drawn + ")");
                        // a model-level cap: four sustained casts at once are counted (plan H slot 3), not removed
                        s.Rig.Targets.Clear();
                        var aims = new[] { new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, -1f) };
                        for (int k = 0; k < 4; k++) s.Rig.Add("S" + k, 0f, 0f).MoveTo(aims[k] * 5f);
                        int capBefore = s.Model.CapExceeded;
                        for (int k = 0; k < 4; k++) { s.Rig.Caster.AimDirection = aims[k]; s.Rig.Cast('감', out _); s.Rig.Advance(0.02); pres.Step(); }
                        for (int k = 0; k < 8; k++) { s.Rig.Advance(0.05); pres.Step(); }        // the four paths reach their bodies and plant
                        Check(r, "V9b", s.Model.LastingCap == 3 && s.Model.CapExceeded == capBefore + 1 && pres.ActiveViews == 4, "four sustained casts at once: the lasting cap 3 is counted (" + (s.Model.CapExceeded - capBefore) + " overflow) but NOT enforced in slice 9a, all 4 are drawn [views " + pres.ActiveViews + " instances " + s.Model.Instances.Count + " materials " + s.Rig.Coord.Materials.AliveCount + "]");
                        s.Rig.Coord.AbortAll(); pres.Step();
                        Check(r, "V9c", pres.ActiveViews == 0 && pres.PoolFree == pres.PoolCreated, "AbortAll returns every view to the pool (" + pres.PoolFree + " of " + pres.PoolCreated + ")");
                        bool emptyMeshes = true;
                        foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>()) if (m.name == "Vol2Vfx" && m.vertexCount != 0) emptyMeshes = false;
                        Check(r, "V9d", emptyMeshes, "the pooled meshes are emptied on return");
                    }
                    finally { if (pres != null) { pres.Release(); UnityEngine.Object.DestroyImmediate(pres.gameObject); } }
                }
                int meshesAfter = Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m.name == "Vol2Vfx");
                int objectsAfter = Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.name == "Vol2Vfx" || g.name == "Vol2VfxPresenter");
                Check(r, "V9e", meshesAfter == meshesBefore && objectsAfter == objectsBefore && UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty == sceneDirtyBefore,
                    "after the presenter is destroyed no pooled view, mesh or object is left (meshes " + meshesBefore + " -> " + meshesAfter + ", objects " + objectsBefore + " -> " + objectsAfter + "), the open scene is not dirtied");
            }
        }
    }
}
