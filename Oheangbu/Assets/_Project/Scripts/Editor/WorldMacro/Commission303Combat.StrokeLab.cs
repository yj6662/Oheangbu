using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Drawing;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // stroke-lab (plan §5.3): registered JamoTemplateLibrarySO templates, resampled with corners kept, laid out on screen,
    // turned into the exact frame script the Play harness queues, then fed offline to a fresh RecognitionPipeline
    // initialized from the same library (Initialize → Recognize → HangulComposer.TryCompose) with the scene controller's
    // 2 px / minimum-point rules. The brush is the resolver book's own EvaluateBrushPower. Templates, matcher and book are
    // only read. Nothing in the scene or project changes; output = CombatTest/stroke-lab*.json and stroke-recipe.json.
    public static partial class Commission303Combat
    {
        internal static readonly Vector2Int[] StrokeLabScreens = { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) };
        static readonly float[] LabFps = { 30f, 45f, 60f };
        const int Perturbations = 20, PerturbTop = 24;

        [Serializable] sealed class LabRow
        {
            public string initialTemplate = "", medialTemplate = "", nominal = "", rejected = "";
            public int kInitial, kMedial, initialStrokes, points, penUps, frames, intervals;
            public bool nominalAll, perturbed;
            public float worst1080 = -1, worst1440 = -1, nominal30, nominal45, nominal60;
            public float rate1080 = -1, rate1440 = -1, p10_30 = -1, p50_30 = -1, p10_45 = -1, p10_60 = -1;
            public int shots30 = 999, shots45 = 999, shots60 = 999;
        }
        [Serializable] sealed class LabReport
        {
            public string letter = "", utc = "", scene = "", book = "", library = "", gate = "", plan = "", note = "";
            public float enemyHp, basePower, minPixels; public int minCommit, combos, recognitions, candidates; public double seconds;
            public List<LabRow> rows = new List<LabRow>();
            public StrokeRecipe303 best;
        }

        static string StrokeLab(string letterArg)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != Harness303.MainScene && scene.path != Harness303.FolkloreScene) return "refused: open " + Harness303.MainScene + " (or the #298 scene) — the lab reads its live template library and spell book";
            var s = Harness303.Session;
            if (s == null) return "refused: no single playtest session";
            var lib = Harness303.Field<JamoTemplateLibrarySO>(s.Walker.Drawing, "_templates");
            var spell = SpellBook(s);
            if (lib == null || spell == null) return "refused: template library or resolver spell book missing";
            var target = s.Actors.FirstOrDefault(a => a != null && a.Id == TargetId);
            float hp = target != null ? target.GetComponent<EnemyVitals>().MaxHp : 60f;
            float minPx = Harness303.FieldValue(s.Walker.Drawing, "_minSamplePixelDistance") is float m ? m : 2f;
            int minCommit = Harness303.FieldValue(s.Walker.Drawing, "_minCommitPointCount") is int c ? c : 8;
            Directory.CreateDirectory(Folder);
            var letters = string.IsNullOrEmpty(letterArg) ? new[] { "마" } : letterArg.Split(',').Select(x => x.Trim()).Where(x => x.Length == 1).ToArray();
            var reports = new List<LabReport>();
            foreach (var l in letters) reports.Add(LabLetter(l[0], lib, spell, hp, minPx, minCommit, scene.path));
            // G1: 마 at 30 fps p10 ≥ 0.70 (≤ 6 casts) = plan A; otherwise plan B with the better of 나 / 가
            var ma = reports.FirstOrDefault(r => r.letter == "마");
            if (ma != null && ma.gate != "PASS" && !letters.Contains("나")) { reports.Add(LabLetter('나', lib, spell, hp, minPx, minCommit, scene.path)); }
            if (ma != null && ma.gate != "PASS" && !letters.Contains("가")) { reports.Add(LabLetter('가', lib, spell, hp, minPx, minCommit, scene.path)); }
            StrokeRecipe303 chosen = null;
            if (ma != null && ma.gate == "PASS") { chosen = ma.best; chosen.plan = "A"; chosen.gate = "PASS"; }
            else
            {
                var b = reports.Where(r => r.best != null && r.letter != "마").OrderBy(r => r.best.shots30p10).ThenByDescending(r => r.best.brush30p10).FirstOrDefault();
                if (b != null) { chosen = b.best; chosen.plan = "B"; chosen.gate = "PLAN_B"; }
                else if (ma == null && reports.Count > 0 && reports[0].best != null) { chosen = reports[0].best; chosen.plan = "B"; chosen.gate = "PLAN_B"; }
            }
            foreach (var r in reports) Harness303.WriteJson(Folder, "stroke-lab-" + r.letter + ".json", r);
            Harness303.WriteJson(Folder, "stroke-lab.json", new LabBundle { reports = reports, chosen = chosen });
            if (chosen == null)
            {
                var fail = new StrokeRecipe303 { gate = "FAIL", labRunUtc = DateTime.UtcNow.ToString("O") };
                Harness303.WriteJson(Folder, "stroke-recipe.json", fail);
                return "stroke-lab: no recipe passed (" + string.Join("; ", reports.Select(r => r.letter + " " + r.gate + " " + r.note)) + ")";
            }
            Harness303.WriteJson(Folder, "stroke-recipe.json", chosen);
            return "stroke-lab: plan " + chosen.plan + " glyph " + chosen.letter + " " + chosen.initialTemplate + "(k" + chosen.kInitial + ") + " + chosen.medialTemplate + "(k" + chosen.kMedial + ")" +
                   " 30 fps p10 brush " + Harness303.F(chosen.brush30p10) + " → " + chosen.shots30p10 + " casts; rate " + Harness303.F(chosen.recognitionRate, "F2") +
                   " | " + string.Join("; ", reports.Select(r => r.letter + " " + r.gate + " (" + r.candidates + "/" + r.combos + " candidates, " + Harness303.F((float)r.seconds, "F1") + " s)"));
        }
        [Serializable] sealed class LabBundle { public List<LabReport> reports; public StrokeRecipe303 chosen; }

        static LabReport LabLetter(char letter, JamoTemplateLibrarySO lib, SpellBookSO spell, float hp, float minPx, int minCommit, string scenePath)
        {
            var started = DateTime.UtcNow;
            var report = new LabReport { letter = letter.ToString(), utc = started.ToString("O"), scene = scenePath, book = AssetDatabase.GetAssetPath(spell), library = AssetDatabase.GetAssetPath(lib), enemyHp = hp, minPixels = minPx, minCommit = minCommit };
            if (!spell.TryGet(letter, out var entry) || entry.Kind != SpellKind.AttackSingle && entry.Kind != SpellKind.AttackArea) { report.gate = "FAIL"; report.note = "not an attack glyph in the resolver book"; return report; }
            report.basePower = entry.BasePower;
            if (!Decompose(letter, out string initial, out string medial)) { report.gate = "FAIL"; report.note = "not a final-less initial+medial glyph"; return report; }
            var initials = Strokes303.Templates(lib, "_initials").Where(t => t != null && Strokes303.TemplateName(t) == initial).ToList();
            var medials = Strokes303.Templates(lib, "_medials").Where(t => t != null && Strokes303.TemplateName(t) == medial).ToList();
            if (initials.Count == 0 || medials.Count == 0) { report.gate = "FAIL"; report.note = "no registered template for " + initial + " / " + medial; return report; }
            bool horizontal = medial == "ㅗ" || medial == "ㅜ";
            Rect iniRect = horizontal ? new Rect(.39f, .55f, .22f, .23f) : new Rect(.29f, .34f, .21f, .34f);
            Rect medRect = horizontal ? new Rect(.34f, .29f, .33f, .19f) : new Rect(.55f, .31f, .18f, .40f);
            var pipeline = new RecognitionPipeline(); pipeline.Initialize(lib);
            var medKs = new[] { 5, 6, 8, 10, 12 };
            foreach (var ini in initials)
            {
                var iniStrokes = Strokes303.Read(ini);
                var iniKs = iniStrokes.Count == 1 ? new[] { 8, 12, 16, 20 } : new[] { 5, 6, 8, 10, 12 };
                foreach (var med in medials)
                {
                    var medStrokes = Strokes303.Read(med);
                    foreach (int ki in iniKs)
                        foreach (int km in medKs)
                        {
                            report.combos++;
                            var row = new LabRow { initialTemplate = ini.name, medialTemplate = med.name, kInitial = ki, kMedial = km, initialStrokes = iniStrokes.Count };
                            report.rows.Add(row);
                            bool all = true; var names = new List<string>();
                            foreach (var screen in StrokeLabScreens)
                            {
                                var glyph = Strokes303.Layout(iniStrokes, iniRect, screen.x, screen.y, ki).Concat(Strokes303.Layout(medStrokes, medRect, screen.x, screen.y, km)).ToList();
                                Strokes303.Frames(glyph, 1, out row.points, out row.penUps);
                                row.frames = row.points + row.penUps + 2; row.intervals = row.points + row.penUps - 1;
                                var sim = Strokes303.Simulate(glyph, 1, 30f, minPx, null, 0, 0, out float d30, out int recorded);
                                report.recognitions++;
                                bool ok = Recognize(pipeline, sim, recorded, minCommit, screen.y, letter, out float worst, out string got);
                                names.Add(got);
                                if (screen.y == 1080) row.worst1080 = worst; else row.worst1440 = worst;
                                if (!ok) { all = false; break; }
                                float b30 = spell.EvaluateBrushPower(worst, d30), b45 = spell.EvaluateBrushPower(worst, d30 * 30f / 45f), b60 = spell.EvaluateBrushPower(worst, d30 * .5f);
                                row.nominal30 = row.nominal30 == 0 ? b30 : Mathf.Min(row.nominal30, b30); row.nominal45 = row.nominal45 == 0 ? b45 : Mathf.Min(row.nominal45, b45); row.nominal60 = row.nominal60 == 0 ? b60 : Mathf.Min(row.nominal60, b60);
                            }
                            row.nominal = string.Join("/", names); row.nominalAll = all;
                            if (!all) row.rejected = "nominal not " + letter + " on every screen";
                        }
                }
            }
            // perturbation on the strongest nominal combinations
            foreach (var row in report.rows.Where(r => r.nominalAll).OrderByDescending(r => r.nominal30).ThenBy(r => r.frames).Take(PerturbTop))
            {
                var iniStrokes = Strokes303.Read(initials.First(t => t.name == row.initialTemplate));
                var medStrokes = Strokes303.Read(medials.First(t => t.name == row.medialTemplate));
                var p30 = new List<float>(); var p45 = new List<float>(); var p60 = new List<float>();
                var perScreen30 = new List<List<float>>(); var perScreen45 = new List<List<float>>(); var perScreen60 = new List<List<float>>();
                foreach (var screen in StrokeLabScreens)
                {
                    var glyph = Strokes303.Layout(iniStrokes, iniRect, screen.x, screen.y, row.kInitial).Concat(Strokes303.Layout(medStrokes, medRect, screen.x, screen.y, row.kMedial)).ToList();
                    int good = 0; var s30 = new List<float>(); var s45 = new List<float>(); var s60 = new List<float>();
                    for (int i = 0; i < Perturbations; i++)
                    {
                        var rnd = new System.Random(unchecked(letter * 7919 + i * 104729 + screen.y));
                        var sim = Strokes303.Simulate(glyph, 1, 30f, minPx, rnd, .004f * screen.y, .1f, out float d30, out int recorded);
                        report.recognitions++;
                        bool ok = Recognize(pipeline, sim, recorded, minCommit, screen.y, letter, out float worst, out _);
                        if (ok) good++;
                        s30.Add(ok ? spell.EvaluateBrushPower(worst, d30) : 0); s45.Add(ok ? spell.EvaluateBrushPower(worst, d30 * 30f / 45f) : 0); s60.Add(ok ? spell.EvaluateBrushPower(worst, d30 * .5f) : 0);
                    }
                    if (screen.y == 1080) row.rate1080 = good / (float)Perturbations; else row.rate1440 = good / (float)Perturbations;
                    perScreen30.Add(s30); perScreen45.Add(s45); perScreen60.Add(s60);
                }
                row.perturbed = true;
                row.p10_30 = perScreen30.Min(x => Percentile(x, .1f)); row.p50_30 = perScreen30.Min(x => Percentile(x, .5f));
                row.p10_45 = perScreen45.Min(x => Percentile(x, .1f)); row.p10_60 = perScreen60.Min(x => Percentile(x, .1f));
                row.shots30 = Strokes303.Shots(hp, entry.BasePower * row.p10_30); row.shots45 = Strokes303.Shots(hp, entry.BasePower * row.p10_45); row.shots60 = Strokes303.Shots(hp, entry.BasePower * row.p10_60);
                if (Mathf.Min(row.rate1080, row.rate1440) < 19f / 20f) row.rejected = "perturbed recognition below 19/20";
            }
            var candidates = report.rows.Where(r => r.perturbed && string.IsNullOrEmpty(r.rejected)).ToList();
            report.candidates = candidates.Count;
            var best = candidates.OrderBy(r => r.shots30).ThenByDescending(r => Mathf.Min(r.rate1080, r.rate1440)).ThenBy(r => r.frames).ThenByDescending(r => r.p10_30).FirstOrDefault();
            report.seconds = (DateTime.UtcNow - started).TotalSeconds;
            if (best == null) { report.gate = "FAIL"; report.note = "no combination passed nominal + 19/20 perturbed recognition"; return report; }
            var ini0 = initials.First(t => t.name == best.initialTemplate); var med0 = medials.First(t => t.name == best.medialTemplate);
            report.best = new StrokeRecipe303
            {
                letter = letter.ToString(), initialName = initial, medialName = medial, initialTemplate = best.initialTemplate, medialTemplate = best.medialTemplate,
                initialPath = AssetDatabase.GetAssetPath(ini0), medialPath = AssetDatabase.GetAssetPath(med0), kInitial = best.kInitial, kMedial = best.kMedial, penUpFrames = 1,
                points = best.points, penUps = best.penUps, frames = best.frames, initialRect = iniRect, medialRect = medRect, basePower = entry.BasePower, enemyHp = hp,
                brush30p10 = best.p10_30, brush30p50 = best.p50_30, brush45p10 = best.p10_45, brush60p10 = best.p10_60, nominalWorst1080 = best.worst1080, nominalWorst1440 = best.worst1440,
                shots30p10 = best.shots30, shots45p10 = best.shots45, shots60p10 = best.shots60, recognitionRate = Mathf.Min(best.rate1080, best.rate1440),
                minimumBrush = Mathf.Max(.3f, best.p10_30 - .05f), labRunUtc = report.utc, book = report.book, library = report.library,
            };
            bool planA = letter == '마' && best.p10_30 >= .70f && best.shots30 <= 6;
            report.gate = letter == '마' ? (planA ? "PASS" : "FAIL_PLAN_A") : "CANDIDATE_PLAN_B";
            report.plan = planA ? "A" : "B";
            report.note = "best 30 fps p10 " + Harness303.F(best.p10_30) + " (" + best.shots30 + " casts), p50 " + Harness303.F(best.p50_30) + ", rate " + Harness303.F(report.best.recognitionRate, "F2");
            return report;
        }

        static bool Recognize(RecognitionPipeline pipeline, List<StrokeData> strokes, int recorded, int minCommit, float height, char expected, out float worst, out string got)
        {
            worst = -1; got = "";
            if (recorded < minCommit) { got = "cancel"; return false; }
            var r = pipeline.Recognize(strokes, height);
            worst = r.WorstDistance;
            if (!r.Success || !HangulComposer.TryCompose(r.InitialName, r.MedialName, r.FinalName, out char letter, out _, out _, out _)) { got = "misfire"; return false; }
            got = letter.ToString();
            return letter == expected;
        }

        static float Percentile(List<float> values, float q)
        {
            if (values.Count == 0) return 0;
            var sorted = values.OrderBy(x => x).ToList();
            int i = Mathf.Clamp(Mathf.FloorToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1);
            return sorted[i];
        }

        static bool Decompose(char glyph, out string initial, out string medial)
        {
            const string initials = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
            initial = medial = null;
            int syllable = glyph - 0xAC00;
            if (syllable < 0 || syllable >= 11172 || syllable % 28 != 0) return false;
            initial = initials[syllable / (21 * 28)].ToString();
            int vowel = syllable / 28 % 21;
            medial = vowel == 0 ? "ㅏ" : vowel == 4 ? "ㅓ" : vowel == 8 ? "ㅗ" : vowel == 13 ? "ㅜ" : null;
            return medial != null && "ㄱㄴㅁㅅㅇ".Contains(initial);
        }
    }
}
