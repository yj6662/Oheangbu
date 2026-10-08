using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Core.Events;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 그림 시네마틱 — 편집기 검사 [SPEC-CINEMATIC-STILLS-308 10.1]. 읽기만(render 는 PNG 를 리포 뿌리 Art/ 밑에 쓴다): 자산 쓰기 0 ·
    // 씬 0 · 대화상자 0 · tests-run 아님. 검사는 카탈로그의 사본과 임시 채널로 돈다 — 실제 채널 SO 를 울리지 않는다(도메인 리로드가 꺼져 있다).
    //   큐: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.CinematicStills308Checks Run "check"
    //        "check"            자리 그림 허용 · "check:release" 자리 그림 0 · "check:mutant=guard|pan|seen" 검사가 실패할 줄 아는지
    //        "render"           장마다 시작 · 끝 창을 네 해상도로 오려 Art/Cinematic308/checks/ 에 PNG (uGUI 렌더가 아니라 창 오림 [I])
    //        "preload:<id>" · "play:<id>"   Play 중: 시험 슬롯에서 그 시퀀스를 직접 건다(AllowInHarness 를 켠다)
    //   -> Art/Cinematic308/checks/checks.txt, 머리 줄 `cinematic ok N/N` | `cinematic FAILED n/N`.
    public static class CinematicStills308Checks
    {
        static readonly int[][] Screens = { new[] { 1280, 720 }, new[] { 1920, 1080 }, new[] { 2560, 1080 }, new[] { 3440, 1440 } };
        static readonly Type[] RuntimeTypes =
        {
            typeof(StringEventChannelSO), typeof(CinematicStillsCatalogSO), typeof(CinematicStillsTimeline), typeof(CinematicStillsRules),
            typeof(CinematicTriggerEvaluator), typeof(CinematicEligibility), typeof(CinematicStillsPresenter),
        };

        sealed class Sheet
        {
            public readonly List<string> Lines = new List<string>(); public int Failed, Total;
            public void Add(bool ok, string label) { Total++; if (!ok) Failed++; Lines.Add((ok ? "ok      " : "FAILED  ") + label); }
            public void Info(string label) => Lines.Add("INFO    " + label);
        }

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string ChecksFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Cinematic308/checks"));

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            if (c.StartsWith("preload:", StringComparison.Ordinal)) return Harness(c.Substring(8), false);
            if (c.StartsWith("play:", StringComparison.Ordinal)) return Harness(c.Substring(5), true);
            if (c == "render") return Render();
            string mutant = c.StartsWith("check:mutant=", StringComparison.Ordinal) ? c.Substring(13) : "";
            if (c != "check" && c != "check:release" && mutant.Length == 0) return "REFUSED CinematicStills308Checks: check | check:release | check:mutant=guard|pan|seen | render | preload:<id> | play:<id>";
            var sheet = new Sheet(); var live = AssetDatabase.LoadAssetAtPath<CinematicStillsCatalogSO>(CinematicStills308Authoring.CatalogPath);
            sheet.Add(live != null, "asset " + CinematicStills308Authoring.CatalogPath + (live == null ? " does not load (run CinematicStills308Authoring apply)" : ""));
            NoCatalogue(sheet);
            if (live == null) return Finish(sheet, c);
            sheet.Add(Resources.Load<CinematicStillsCatalogSO>(CinematicStillsCatalogSO.ResourcePath) == live, "the runtime's Resources path finds the same asset");
            var temp = new List<Object>();
            try
            {
                var catalog = Object.Instantiate(live); catalog.hideFlags = HideFlags.HideAndDontSave; temp.Add(catalog);
                var problems = new List<string>(); CinematicStillsRules.Validate(live, problems, true);
                sheet.Add(problems.Count == 0, "AC-CS.1 / .3 / .4 / .5 / .7 / .20 data rules (live asset, channels set): " + (problems.Count == 0 ? "0 problems" : string.Join(" | ", problems)));
                catalog.Requested = Temp<StringEventChannelSO>(temp); catalog.Started = Temp<StringEventChannelSO>(temp); catalog.Finished = Temp<StringEventChannelSO>(temp);
                if (mutant == "guard") catalog.Style.GuardSeconds = 0f;
                if (mutant == "pan") foreach (var s in catalog.Sequences) if (s.Stills.Length > 0) s.Stills[0].Rect0 = new Rect(0f, .025f, .95f, .95f);
                problems.Clear(); CinematicStillsRules.Validate(catalog, problems, true);
                sheet.Add(problems.Count == 0, "data rules on the working copy" + (mutant.Length > 0 ? " [mutant " + mutant + "]" : "") + ": " + (problems.Count == 0 ? "0 problems" : string.Join(" | ", problems)));
                Pictures(sheet, live, c == "check:release");
                Sounds(sheet, live);
                Layer(sheet, catalog, temp);
                foreach (var sequence in catalog.Sequences) if (!sequence.Disabled) { Clock(sheet, catalog, sequence); Windows(sheet, catalog, sequence); Walk(sheet, catalog, sequence); }
                Once(sheet, catalog, mutant == "seen");
                Statics(sheet);
            }
            catch (Exception e) { sheet.Add(false, "exception " + e.GetType().Name + ": " + e.Message); }
            finally { foreach (var o in temp) if (o != null) Object.DestroyImmediate(o); }
            return Finish(sheet, c);
        }

        static T Temp<T>(List<Object> temp) where T : ScriptableObject { var o = ScriptableObject.CreateInstance<T>(); o.hideFlags = HideFlags.HideAndDontSave; temp.Add(o); return o; }

        static string Finish(Sheet sheet, string command)
        {
            string head = sheet.Failed == 0 ? "cinematic ok " + sheet.Total + "/" + sheet.Total : "cinematic FAILED " + sheet.Failed + "/" + sheet.Total;
            try { Directory.CreateDirectory(ChecksFolder); File.WriteAllText(Path.Combine(ChecksFolder, "checks.txt"), head + "   (" + command + ")\n" + string.Join("\n", sheet.Lines) + "\n"); }
            catch (Exception e) { sheet.Lines.Add("INFO    could not write checks.txt: " + e.Message); }
            return head + "\n" + string.Join("\n", sheet.Lines);
        }

        // AC-CS.23 (S5): no catalogue / no channel / no picture = nothing happens and nothing throws.
        static void NoCatalogue(Sheet sheet)
        {
            sheet.Add(CinematicEligibility.Judge(null, "mine.exit", new List<string>(), "", false) == CinematicVerdict.NoCatalogue, "AC-CS.23 no catalogue -> verdict NoCatalogue");
            var go = new GameObject("CinematicCheck308_none") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var presenter = go.AddComponent<CinematicStillsPresenter>(); presenter.Initialize(null, null, null, null, null, null, null);
                sheet.Add(presenter.LayerRoot == null && !presenter.IsPlaying, "AC-CS.23 a presenter without a catalogue builds no layer and plays nothing");
            }
            catch (Exception e) { sheet.Add(false, "AC-CS.23 presenter without a catalogue threw " + e.GetType().Name); }
            finally { Object.DestroyImmediate(go); }
        }

        static void Pictures(Sheet sheet, CinematicStillsCatalogSO catalog, bool release)
        {
            long all = 0; int placeholders = 0;
            foreach (var sequence in catalog.Sequences)
            {
                long sum = 0;
                foreach (var still in sequence.Stills)
                {
                    string path = CinematicStills308Authoring.StillAssetPath(still); var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (still.IsPlaceholder) placeholders++;
                    if (sequence.Disabled && texture == null) continue;
                    long bytes = texture != null ? CinematicStillsRules.EstimatedBytes(texture.width, texture.height, 0) : 0; sum += bytes;
                    sheet.Add(texture != null && Resources.Load<Texture2D>(still.ResourcePath) == texture && CinematicStills308Authoring.ImporterMatches(path, catalog.Rules.MaxTextureSize) && bytes <= catalog.Rules.MaxStillBytes,
                        "AC-CS.2 " + sequence.Id + "/" + still.Id + ": " + (texture != null ? texture.width + " x " + texture.height + ", " + bytes + " B (BC7 estimate), importer " + (CinematicStills308Authoring.ImporterMatches(path, catalog.Rules.MaxTextureSize) ? "as section 8" : "DIFFERS") + (still.IsPlaceholder ? ", placeholder" : "") : "no texture at " + path));
                }
                all += sum;
                if (!sequence.Disabled) sheet.Add(sum <= catalog.Rules.MaxSequenceBytes, "AC-CS.2 " + sequence.Id + ": sequence " + sum + " B <= " + catalog.Rules.MaxSequenceBytes);
            }
            sheet.Add(all <= catalog.Rules.MaxTotalBytes, "AC-CS.2 all stills " + all + " B <= " + catalog.Rules.MaxTotalBytes);
            if (release) sheet.Add(placeholders == 0, "AC-CS.2 release: IsPlaceholder 0 (found " + placeholders + ")");
            else sheet.Info("placeholders " + placeholders + " (allowed in this mode; check:release wants 0)");
        }

        static void Sounds(Sheet sheet, CinematicStillsCatalogSO catalog)
        {
            var themes = AssetDatabase.FindAssets("t:PlaytestUiThemeSO").Select(g => AssetDatabase.LoadAssetAtPath<PlaytestUiThemeSO>(AssetDatabase.GUIDToAssetPath(g))).Where(t => t != null && t.SoundPalette != null).ToArray();
            sheet.Add(themes.Length > 0, "AC-CS.20 a UI theme with a sound palette exists (" + themes.Length + ")");
            foreach (string id in catalog.Sequences.Where(s => !s.Disabled).SelectMany(s => s.Stills).SelectMany(s => s.Cues).Select(q => q.Id).Distinct())
                foreach (var theme in themes)
                {
                    var cue = theme.SoundPalette.Find(id);
                    sheet.Add(cue != null && cue.Clip != null, "AC-CS.20 sound '" + id + "' in " + theme.name + (cue != null && cue.Clip != null ? " (" + F(cue.Clip.length) + " s)" : " NOT FOUND"));
                }
        }

        // AC-CS.6: no text component anywhere under the layer; one paper Image and two RawImages; the layer does not take clicks.
        static void Layer(Sheet sheet, CinematicStillsCatalogSO catalog, List<Object> temp)
        {
            var go = new GameObject("CinematicCheck308_layer") { hideFlags = HideFlags.HideAndDontSave }; temp.Add(go);
            var presenter = go.AddComponent<CinematicStillsPresenter>(); presenter.Initialize(catalog, null, null, null, null, null, null);
            var root = presenter.LayerRoot; sheet.Add(root != null, "the presenter builds its layer"); if (root == null) return;
            int texts = go.GetComponentsInChildren<TMPro.TMP_Text>(true).Length + go.GetComponentsInChildren<Text>(true).Length + go.GetComponentsInChildren<TextMesh>(true).Length;
            sheet.Add(texts == 0, "AC-CS.6 text components under the layer: " + texts);
            sheet.Add(root.GetComponentsInChildren<Image>(true).Length == 1 && root.GetComponentsInChildren<RawImage>(true).Length == 2, "the layer is one paper Image and two RawImages");
            var canvas = root.GetComponent<Canvas>();
            sheet.Add(canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.sortingOrder == catalog.Style.SortingOrder && !canvas.enabled && canvas.sortingOrder > 150 && canvas.sortingOrder < 32000,
                "canvas: overlay, order " + (canvas != null ? canvas.sortingOrder.ToString() : "?") + " between the veil (150) and the loading screen (32000), off until a sequence plays");
            sheet.Add(root.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget) && root.GetComponent<GraphicRaycaster>() == null, "the layer takes no pointer events");
            var screenText = typeof(CinematicStillsCatalogSO).GetNestedTypes().SelectMany(t => t.GetFields()).Where(f => f.FieldType == typeof(string)).Select(f => f.DeclaringType.Name + "." + f.Name).ToArray();
            sheet.Add(screenText.All(n => n.EndsWith(".Id", StringComparison.Ordinal) || n.EndsWith("Id", StringComparison.Ordinal) || n.EndsWith(".ResourcePath", StringComparison.Ordinal)),
                "AC-CS.6 the data has no screen-text field (strings: " + string.Join(", ", screenText) + ")");
        }

        // AC-CS.3 / .7 / .19 on a fake clock.
        static void Clock(Sheet sheet, CinematicStillsCatalogSO catalog, CinematicStillsCatalogSO.Sequence sequence)
        {
            var k = catalog.Style; float step = 1f / 60f; string p = sequence.Id + ": ";
            var full = new CinematicStillsTimeline(sequence, k, false); int frames = 0;
            while (full.State != CinematicStillsTimeline.Phase.Done && frames < 100000) { full.Advance(step); frames++; }
            sheet.Add(Mathf.Abs(frames * step - sequence.TotalSeconds) <= step * 2f, "AC-CS.3 " + p + "plays " + F(frames * step) + " s on the wall clock (data " + F(sequence.TotalSeconds) + " s)");
            var early = new CinematicStillsTimeline(sequence, k, false); early.Advance(k.GuardSeconds * .5f > k.MaxFrameSeconds ? k.MaxFrameSeconds : k.GuardSeconds * .5f);
            sheet.Add(!early.TrySkip() && early.State == CinematicStillsTimeline.Phase.Playing, "AC-CS.7 " + p + "a skip inside the guard (" + F(k.GuardSeconds) + " s) is dropped");
            var skip = new CinematicStillsTimeline(sequence, k, false); while (skip.Clock < k.GuardSeconds + step) skip.Advance(step);
            bool took = skip.TrySkip(); float at = skip.Clock; while (skip.State != CinematicStillsTimeline.Phase.Done && skip.Clock < at + 5f) skip.Advance(step);
            sheet.Add(took && skip.Clock - at <= catalog.Rules.MaxSkipFadeSeconds + step && skip.LayerAlpha == 0f, "AC-CS.7 " + p + "a skip after the guard ends in " + F(skip.Clock - at) + " s (<= " + F(catalog.Rules.MaxSkipFadeSeconds) + ")");
            var still = new CinematicStillsTimeline(sequence, k, true); still.Advance(step);
            bool fixedWindow = true; for (int i = 0; i < sequence.Stills.Length; i++) fixedWindow &= still.Window(i, 0f) == sequence.Stills[i].Rect1;
            float t = 0f; while (still.LayerAlpha < 1f && t < 1f) { still.Advance(step); t += step; }
            sheet.Add(fixedWindow && t <= k.ReducedMotionFadeSeconds + step * 2f, "AC-CS.19 " + p + "reduced motion: the end window from the first frame, fade " + F(t) + " s");
        }

        // AC-CS.21 (numbers): at every listed resolution the window stays inside the picture at the start and the end of each still.
        static void Windows(Sheet sheet, CinematicStillsCatalogSO catalog, CinematicStillsCatalogSO.Sequence sequence)
        {
            float source = catalog.Style.SourceWidth / (float)catalog.Style.SourceHeight; var timeline = new CinematicStillsTimeline(sequence, catalog.Style, false); int bad = 0, n = 0; float t0 = 0f;
            foreach (var still in sequence.Stills)
            {
                foreach (var screen in Screens) foreach (float t in new[] { t0, t0 + still.Seconds })
                {
                    Rect w = CinematicStillsTimeline.Fit(timeline.Window(timeline.StillAt(Mathf.Min(t, t0 + still.Seconds - .0001f)), t), still.Focus, source, screen[0] / (float)screen[1]); n++;
                    if (w.x < -.0005f || w.y < -.0005f || w.xMax > 1.0005f || w.yMax > 1.0005f || w.width <= 0f || Mathf.Abs(source * w.width / w.height - screen[0] / (float)screen[1]) > .01f) bad++;
                }
                t0 += still.Seconds;
            }
            sheet.Add(bad == 0, "AC-CS.21 " + sequence.Id + ": " + n + " windows (4 resolutions, start and end) inside the picture and of the screen's aspect; outside " + bad);
        }

        // AC-CS.16: a straight walk through the plane along its normal (synthetic [I]: the cave walk samples were lost on 2026-10-06).
        static void Walk(Sheet sheet, CinematicStillsCatalogSO catalog, CinematicStillsCatalogSO.Sequence sequence)
        {
            var t = sequence.Trigger; if (t.Kind != CinematicTriggerKind.PlaneCrossing) return;
            Vector3 dir = new Vector3(t.OutwardNormalXZ.x, 0f, t.OutwardNormalXZ.y); float y = (t.FeetYMin + t.FeetYMax) * .5f; Vector3 fired = default;
            int Fires(bool outward)
            {
                var e = new CinematicTriggerEvaluator(sequence, catalog.Style.RequestTimeoutSeconds); int fires = 0;
                for (float s = -30f; s <= 20f; s += .25f)
                {
                    Vector3 feet = t.Point + dir * (outward ? s : -s); feet.y = y;
                    if (e.Step(new CinematicTriggerEvaluator.Input { Feet = feet, Now = (s + 30f) * .05f, Eligible = true, StartAllowed = true })) { fires++; fired = feet; e.NotifyStarted(); e.NotifyFinished(); }
                }
                return fires;
            }
            int forward = Fires(true), backward = Fires(false);
            sheet.Add(forward == 1 && backward == 0, "AC-CS.16 " + sequence.Id + ": walking out fires " + forward + " (want 1), walking in fires " + backward + " (want 0)");
            var actors = Object.FindObjectsByType<PrologueEncounter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (actors.Length == 0) { sheet.Info("AC-CS.16 no encounter actors in the open scenes: the distance to mine_beast/1 was not read here (offline: 18.0 m [O])"); return; }
            var nearest = actors.OrderBy(a => (a.transform.position - fired).sqrMagnitude).First();
            sheet.Info("AC-CS.16 nearest encounter to the firing place: " + nearest.Id + " at " + F((nearest.transform.position - fired).magnitude) + " m (must stay beyond its detection range)");
        }

        // AC-CS.8 / .9 / .10: the one rule.
        static void Once(Sheet sheet, CinematicStillsCatalogSO catalog, bool mutantSeen)
        {
            foreach (var sequence in catalog.Sequences.Where(s => !s.Disabled))
            {
                string id = sequence.Id; var enrolled = new List<string> { CinematicStillsCatalogSO.EnrolledId }; var seen = new List<string>(enrolled) { mutantSeen ? "cinematic." + id + ".saw" : CinematicStillsCatalogSO.SeenId(id) };
                sheet.Add(CinematicEligibility.Judge(catalog, id, enrolled, "", false) == CinematicVerdict.Ok, "AC-CS.8 " + id + ": a new journey that has not seen it -> Ok");
                sheet.Add(CinematicEligibility.Judge(catalog, id, seen, "", false) == CinematicVerdict.AlreadySeen, "AC-CS.8 " + id + ": seen -> refused" + (mutantSeen ? " [mutant seen]" : ""));
                if (sequence.RequiresEnrollment) sheet.Add(CinematicEligibility.Judge(catalog, id, new List<string>(), "", false) == CinematicVerdict.NotEnrolled, "AC-CS.9 " + id + ": an old save (no cinematic.enrolled) -> refused");
                sheet.Add(CinematicEligibility.Judge(catalog, id, enrolled, "_c303_x", false) == CinematicVerdict.HarnessSlot && CinematicEligibility.Judge(catalog, id, enrolled, "_c303_x", true) == CinematicVerdict.Ok,
                    "AC-CS.10 " + id + ": a harness slot is refused unless the check allows it");
            }
            sheet.Add(CinematicEligibility.Judge(catalog, "no.such", new List<string>(), "", false) == CinematicVerdict.UnknownSequence, "an unknown id -> refused");
        }

        // AC-CS.17 / .18: no mutable static field and no coroutine on the new runtime types.
        static void Statics(Sheet sheet)
        {
            const BindingFlags all = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var types = RuntimeTypes.SelectMany(t => new[] { t }.Concat(t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))).ToArray();
            var statics = types.SelectMany(t => t.GetFields(all)).Where(f => f.IsStatic && !f.IsLiteral && !f.IsInitOnly && !f.Name.Contains("<>")).Select(f => f.DeclaringType.Name + "." + f.Name).ToArray();
            sheet.Add(statics.Length == 0, "AC-CS.17 mutable static fields on the new runtime types: " + (statics.Length == 0 ? "0" : string.Join(", ", statics)));
            var routines = types.SelectMany(t => t.GetMethods(all)).Where(m => typeof(IEnumerator).IsAssignableFrom(m.ReturnType)).Select(m => m.DeclaringType.Name + "." + m.Name).ToArray();
            sheet.Add(routines.Length == 0, "AC-CS.18 coroutine methods on the new runtime types: " + (routines.Length == 0 ? "0" : string.Join(", ", routines)));
        }

        static string Render()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CinematicStillsCatalogSO>(CinematicStills308Authoring.CatalogPath);
            if (catalog == null) return "refused: no catalogue (run CinematicStills308Authoring apply)";
            Directory.CreateDirectory(ChecksFolder); int written = 0; var lines = new List<string>(); var previous = RenderTexture.active;
            try
            {
                foreach (var sequence in catalog.Sequences.Where(s => !s.Disabled))
                {
                    var timeline = new CinematicStillsTimeline(sequence, catalog.Style, false); float t0 = 0f;
                    foreach (var still in sequence.Stills)
                    {
                        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(CinematicStills308Authoring.StillAssetPath(still));
                        if (texture == null) { lines.Add("MISSING " + still.ResourcePath); t0 += still.Seconds; continue; }
                        float source = texture.width / (float)texture.height; int index = Array.IndexOf(sequence.Stills, still);
                        foreach (var screen in Screens)
                            for (int end = 0; end < 2; end++)
                            {
                                Rect uv = CinematicStillsTimeline.ToUv(CinematicStillsTimeline.Fit(timeline.Window(index, end == 0 ? t0 : t0 + still.Seconds), still.Focus, source, screen[0] / (float)screen[1]));
                                var target = RenderTexture.GetTemporary(screen[0], screen[1], 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                                var shot = new Texture2D(screen[0], screen[1], TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                                try
                                {
                                    Graphics.Blit(texture, target, new Vector2(uv.width, uv.height), new Vector2(uv.x, uv.y));
                                    RenderTexture.active = target; shot.ReadPixels(new Rect(0, 0, screen[0], screen[1]), 0, 0); shot.Apply(false, false);
                                    string name = sequence.Id.Replace('.', '_') + "_" + (index + 1).ToString("00") + "_" + still.Id + "_" + screen[0] + "x" + screen[1] + (end == 0 ? "_start" : "_end") + ".png";
                                    File.WriteAllBytes(Path.Combine(ChecksFolder, name), shot.EncodeToPNG()); written++;
                                }
                                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(shot); }
                            }
                        t0 += still.Seconds;
                    }
                }
            }
            catch (Exception e) { return "cinematic render FAILED after " + written + ": " + e.GetType().Name + " " + e.Message; }
            return "cinematic render ok " + written + " PNG -> " + ChecksFolder + (lines.Count > 0 ? "\n" + string.Join("\n", lines) : "");
        }

        // Play Mode, harness slot: the check asks for one sequence by id. The presenter's flag is an instance field (false again at the next bind).
        static string Harness(string id, bool play)
        {
            if (!EditorApplication.isPlaying) return "refused: Play mode only";
            var ui = PlaytestUiRoot.Instance; var presenter = ui != null ? ui.Cinematic308 : null; var session = ui != null ? ui.Session : null;
            if (presenter == null || session == null) return "refused: no stills presenter in this scene (title scene, or the catalogue asset is absent)";
            var catalog = Resources.Load<CinematicStillsCatalogSO>(CinematicStillsCatalogSO.ResourcePath);
            if (catalog == null || catalog.Find(id) == null || catalog.Requested == null) return "refused: unknown sequence '" + id + "' or no Requested channel";
            presenter.AllowInHarness = true;
            if (!play) { presenter.PreloadForHarness(id); return "cinematic preload asked: " + id + " (ask play:" + id + " a moment later)"; }
            if (!presenter.PicturesReady(id)) { presenter.PreloadForHarness(id); return "refused: pictures not loaded yet (preload asked now; missing = " + presenter.PictureMissing(id) + "), verdict " + session.CinematicVerdict308(id); }
            int before = presenter.StartedCount; var verdict = session.CinematicVerdict308(id);
            catalog.Requested.Raise(id);
            return "cinematic play " + id + ": verdict " + verdict + ", started " + (presenter.StartedCount - before) + ", playing " + presenter.IsPlaying + ", time scale " + F(Time.timeScale) + ", pause depth " + ui.Pause.Depth;
        }
    }
}
