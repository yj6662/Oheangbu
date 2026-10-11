using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 트레일러 4차(SPEC-TRAILER-308 §4차): 필름 사양의 glyph 입력 전개 · HUD 부분 표시 · 강토 로더 정착 대기 · 적중 로그.
    // 인식 · 필세 · DriveInput은 건드리지 않는다 — 여기서 만드는 것은 기존 stroke / q 이벤트와 같은 가상 입력뿐이다(인식 불가침).
    public static partial class Presentation297
    {
        // ---------------------------------------------------------------- glyph → stroke + q
        // {"type":"glyph","key":"뭄","time":t,"duration":d,"points":[cx,cy,size]} → 초성 · 중성 · (받침) 등록 템플릿을 뷰포트 0–1(y 위쪽)에
        // 놓은 stroke 이벤트들 + 첫 점 한 프레임 전부터 마지막 점까지 눌리는 q 이벤트(다음 프레임에 Q · LMB가 함께 풀림 = 발사).
        const string GlyphInitials308 = "ㄱㄴㅁㅅㅇ", GlyphMedials308 = "ㅏㅓㅗㅜ", GlyphFinals308 = "ㄱㄴㅁㅅㅇ";   // 받침 = JamoTemplateLibrarySO.FinalCandidates 거울
        const string HangulInitials308 = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
        const string HangulMedials308 = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
        const string HangulFinals308 = " ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";
        const float GlyphBaseX308 = .5f, GlyphBaseY308 = .5f, GlyphBaseSize308 = .5f;   // points 기본값 — 아래 rect 상수는 전부 이 기준으로 적혀 있다
        const int GlyphPenUpFrames308 = 2, GlyphMinStrokeFrames308 = 4, GlyphPointsPerStroke308 = 16;
        // 받침 없음(StrokeLab 전례): 세로형 중성(ㅏㅓ)은 초성 왼쪽 · 중성 오른쪽, 가로형 중성(ㅗㅜ)은 초성 위 · 중성 아래
        static readonly Rect GlyphIniV308 = new Rect(.29f, .34f, .21f, .34f), GlyphMedV308 = new Rect(.55f, .31f, .18f, .40f);
        static readonly Rect GlyphIniH308 = new Rect(.39f, .55f, .22f, .23f), GlyphMedH308 = new Rect(.34f, .29f, .33f, .19f);
        // 받침 있음 · 가로형 중성(뭄 등, 계획서 §1의 (cx−.11, cy+.12 …)를 cx=cy=.5로 적은 것): 초성 위 / 중성 가운데 / 받침 아래
        static readonly Rect GlyphIniHF308 = new Rect(.39f, .62f, .22f, .20f), GlyphMedHF308 = new Rect(.33f, .43f, .34f, .16f), GlyphFinHF308 = new Rect(.39f, .20f, .22f, .20f);
        // 받침 있음 · 세로형 중성(산 등 — 계획서에 없어 세로형 전례를 위 60 %로 눌러 넣고 받침을 그 아래 가운데에 둔다)
        static readonly Rect GlyphIniVF308 = new Rect(.29f, .48f, .21f, .28f), GlyphMedVF308 = new Rect(.55f, .46f, .18f, .34f), GlyphFinVF308 = new Rect(.40f, .20f, .22f, .20f);
        static readonly List<string> glyphNotes308 = new List<string>();

        static InputEvent[] ExpandGlyphs308(InputEvent[] inputs, int fps, float width, float height)
        {
            glyphNotes308.Clear();
            if (inputs == null || !inputs.Any(e => e != null && e.type == "glyph")) return inputs;
            if (fps <= 0) throw new ArgumentException("glyph: fps must be positive");
            var library = GlyphLibrary308();
            var result = new List<InputEvent>();
            foreach (var e in inputs)
            {
                if (e == null) continue;
                if (e.type == "glyph") result.AddRange(ExpandGlyph308(e, library, fps, width, height)); else result.Add(e);
            }
            return result.ToArray();
        }

        static List<InputEvent> ExpandGlyph308(InputEvent e, JamoTemplateLibrarySO library, int fps, float width, float height)
        {
            string key = (e.key ?? "").Trim();
            if (key.Length != 1) throw new ArgumentException("glyph: key must be one syllable, got '" + e.key + "'");
            DecomposeGlyph308(key[0], out string ini, out string med, out string fin);
            float cx = e.points != null && e.points.Length > 0 ? e.points[0] : GlyphBaseX308;
            float cy = e.points != null && e.points.Length > 1 ? e.points[1] : GlyphBaseY308;
            float size = e.points != null && e.points.Length > 2 ? e.points[2] : GlyphBaseSize308;
            if (size <= 0f) throw new ArgumentException("glyph " + key + ": size must be positive");
            bool horizontal = med == "ㅗ" || med == "ㅜ";
            Rect iniRect = fin == null ? (horizontal ? GlyphIniH308 : GlyphIniV308) : (horizontal ? GlyphIniHF308 : GlyphIniVF308);
            Rect medRect = fin == null ? (horizontal ? GlyphMedH308 : GlyphMedV308) : (horizontal ? GlyphMedHF308 : GlyphMedVF308);
            Rect finRect = horizontal ? GlyphFinHF308 : GlyphFinVF308;
            var names = new List<string>(); var lengths = new List<float>(); var strokes = new List<List<Vector2>>();
            names.Add(PickTemplate308(library, "_initials", ini, out var iniStrokes).name);
            strokes.AddRange(LayoutJamo308(iniStrokes, PlaceGlyphRect308(iniRect, cx, cy, size), width, height, lengths));
            names.Add(PickTemplate308(library, "_medials", med, out var medStrokes).name);
            strokes.AddRange(LayoutJamo308(medStrokes, PlaceGlyphRect308(medRect, cx, cy, size), width, height, lengths));
            if (fin != null)
            {
                // 받침은 초성 템플릿 재사용(JamoTemplateLibrarySO.BuildFinalGestures와 같은 규칙) — 인식기는 획 순서 초→중→종과 받침 평균 y가 아래인 것만 본다
                names.Add(PickTemplate308(library, "_initials", fin, out var finStrokes).name);
                strokes.AddRange(LayoutJamo308(finStrokes, PlaceGlyphRect308(finRect, cx, cy, size), width, height, lengths));
            }
            // 시간 배분: 획 사이 2프레임 정지, 나머지를 획 길이 비례(획마다 최소 4프레임), 반올림 오차는 가장 긴 획이 받는다
            int n = strokes.Count, total = Mathf.RoundToInt(e.duration * fps), gaps = (n - 1) * GlyphPenUpFrames308, draw = total - gaps;
            if (draw < n * GlyphMinStrokeFrames308)
                throw new ArgumentException("glyph " + key + ": duration " + F308(e.duration) + " s is too short for " + n + " strokes at " + fps + " fps (need >= " + F308((n * GlyphMinStrokeFrames308 + gaps) / (float)fps) + " s)");
            float sum = lengths.Sum(); int longest = 0;
            var frames = new int[n];
            for (int i = 0; i < n; i++)
            {
                frames[i] = Mathf.Max(GlyphMinStrokeFrames308, Mathf.RoundToInt(draw * (sum > 0f ? lengths[i] / sum : 1f / n)));
                if (lengths[i] > lengths[longest]) longest = i;
            }
            frames[longest] = Mathf.Max(GlyphMinStrokeFrames308, frames[longest] + (draw - frames.Sum()));
            // DriveInput의 프레임 k 입력 시각은 k/fps: 창을 1/4프레임 일찍 열고 반 프레임 짧게 닫아 경계의 부동소수 오차를 피한다
            int first = Mathf.RoundToInt(e.time * fps), k = first;
            var events = new List<InputEvent>();
            for (int i = 0; i < n; i++)
            {
                events.Add(new InputEvent { type = "stroke", time = (k - .25f) / fps, duration = (frames[i] - .5f) / fps, points = strokes[i].SelectMany(p => new[] { p.x, p.y }).ToArray() });
                k += frames[i] + GlyphPenUpFrames308;
            }
            int last = k - GlyphPenUpFrames308 - 1, qFrom = Mathf.Max(0, first - 1);   // 마지막 점의 프레임 · Q는 첫 점 한 프레임 전부터(StrokeLab §5.3 전례)
            events.Add(new InputEvent { type = "q", time = (qFrom - .25f) / fps, duration = (last - qFrom + .5f) / fps });
            glyphNotes308.Add("glyph " + key + " strokes=" + n + " at " + F308(e.time) + " (" + ini + "+" + med + (fin != null ? "+" + fin : "") + " " + string.Join("+", names)
                + ", frames " + first + "-" + last + " [" + string.Join(",", frames) + "], q " + qFrom + "-" + last + ", release " + (last + 1)
                + ", centre " + F308(cx) + "," + F308(cy) + " size " + F308(size) + ", screen " + width + "x" + height + ")");
            return events;
        }

        // 전개 결과("glyph 뭄 strokes=N at t …")는 state가 만들어진 뒤 commits로 옮긴다
        static void FlushGlyphNotes308(FilmState target) { if (target != null) target.commits.AddRange(glyphNotes308); glyphNotes308.Clear(); }

        static string F308(float v) => v.ToString("F2", CultureInfo.InvariantCulture);

        // 완성형 한 글자 → 자모. 허용 밖(초성 ㄱㄴㅁㅅㅇ · 중성 ㅏㅓㅗㅜ · 받침 없음/ㄱㄴㅁㅅㅇ)이면 예외
        static void DecomposeGlyph308(char ch, out string ini, out string med, out string fin)
        {
            int code = ch - 0xAC00;
            if (code < 0 || code >= 11172) throw new ArgumentException("glyph '" + ch + "' is not a Hangul syllable");
            ini = HangulInitials308[code / 588].ToString(); med = HangulMedials308[code % 588 / 28].ToString();
            int f = code % 28; fin = f == 0 ? null : HangulFinals308[f].ToString();
            if (!GlyphInitials308.Contains(ini)) throw new ArgumentException("glyph '" + ch + "': initial " + ini + " is not in " + GlyphInitials308);
            if (!GlyphMedials308.Contains(med)) throw new ArgumentException("glyph '" + ch + "': medial " + med + " is not in " + GlyphMedials308);
            if (fin != null && !GlyphFinals308.Contains(fin)) throw new ArgumentException("glyph '" + ch + "': final " + fin + " is not in " + GlyphFinals308 + " (or none)");
        }

        // (cx,cy,size) 기준 평행이동 · 배율: rect' = ((x−.5)·s+cx, (y−.5)·s+cy, w·s, h·s), s = size/.5 — 기본값이면 전례 rect 그대로
        static Rect PlaceGlyphRect308(Rect r, float cx, float cy, float size)
        {
            float s = size / GlyphBaseSize308;
            return new Rect((r.x - GlyphBaseX308) * s + cx, (r.y - GlyphBaseY308) * s + cy, r.width * s, r.height * s);
        }

        // 템플릿 라이브러리 = 세션 플레이어의 DrawingInputController._templates(LockDraw308.Measure · StrokeLab과 같은 통로)
        static JamoTemplateLibrarySO GlyphLibrary308()
        {
            var session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            DrawingInputController drawing = session != null && session.Walker != null ? session.Walker.Drawing : null;
            if (drawing == null) drawing = Object.FindFirstObjectByType<DrawingInputController>();
            var library = Harness303.Field<JamoTemplateLibrarySO>(drawing, "_templates");
            if (library == null) throw new Exception("glyph: no drawing controller with a template library (_templates) in the scene");
            return library;
        }

        // 같은 자모의 등록 템플릿 중 획 수가 가장 적은 것(같으면 먼저 등록된 것 — ㅁ은 mieum_02)
        static TextAsset PickTemplate308(JamoTemplateLibrarySO library, string field, string jamo, out List<List<Vector2>> strokes)
        {
            TextAsset best = null; strokes = null;
            foreach (var asset in Strokes303.Templates(library, field))
            {
                if (asset == null || Strokes303.TemplateName(asset) != jamo) continue;
                var read = Strokes303.Read(asset);
                if (read.Count == 0) continue;
                if (best == null || read.Count < strokes.Count) { best = asset; strokes = read; }
            }
            if (best == null) throw new Exception("glyph: no registered template for " + jamo + " (" + field + ")");
            return best;
        }

        // 템플릿 → Strokes303.Layout(픽셀, y 위쪽) → 뷰포트 0–1(DriveInput의 view.x·Screen.width 규약). 획 길이는 픽셀로 잰다(가로세로 비율)
        static List<List<Vector2>> LayoutJamo308(List<List<Vector2>> template, Rect rect, float width, float height, List<float> lengths)
        {
            var result = new List<List<Vector2>>();
            foreach (var stroke in Strokes303.Layout(template, rect, width, height, GlyphPointsPerStroke308))
            {
                float length = 0f;
                for (int i = 1; i < stroke.Count; i++) length += Vector2.Distance(stroke[i - 1], stroke[i]);
                lengths.Add(length);
                result.Add(stroke.Select(p => new Vector2(p.x / width, p.y / height)).ToList());
            }
            return result;
        }

        // ---------------------------------------------------------------- HUD 부분 표시
        // 녹화는 별도 캡처 카메라 → RenderTexture라 ScreenSpaceOverlay HUD_Canvas는 원래 안 찍힌다. HudController는 레티클을 `_reticle.position = screen`
        // (HudController.cs:536 — 오버레이 캔버스의 "월드 좌표 = 화면 픽셀" 규약)으로 놓으므로 캔버스를 ScreenSpaceCamera로 바꾸면 레티클이 월드 (1157,592,0)으로
        // 날아간다 → 필름 동안 캔버스를 WorldSpace로 바꾸되 오버레이가 몰던 배치(픽셀 = 월드)를 그대로 고정하고, 전용 층에 올린 뒤 직교 오버레이 카메라
        // (URP 카메라 스택 · 깊이 지움)로 [0,W]×[0,H]를 캡처 카메라 위에 얹는다. 깊이를 지우므로 1인칭 팔 · 붓(.70–1.65 m)에 가려지지 않고, 좌표가 픽셀
        // 단위(≤ 수천)라 세계 좌표 수천 m에서 생기는 float 오차(near .01 + .02 평면이면 정점마다 ±3–6 px)도 없다. 오버레이 패스에서는 렌더러 기능
        // (InkWash297 · CompactMist238 · SSAO297 …)을 꺼 둔다(beginCameraRendering) — 전체화면 먹 패스가 지워진 깊이 위에서 다시 돌면 프레임을 망친다.
        // keep 밖은 SetActive(false)로 숨기고, HUD가 테이크 중 다시 켜는 요소(InteractionPrompt · TargetHpStroke)는 캡처 직전마다 HudFilterTick308이 다시
        // 숨긴 뒤 Canvas.ForceUpdateCanvases()로 배치를 다시 만든다(EditorApplication.update 안의 수동 Render는 플레이어 루프의 캔버스 갱신을 거치지 않는다).
        // hud=""이면 아무것도 안 한다.
        sealed class HudHidden308 { public GameObject Go; public bool WasActive; }
        sealed class HudLayer308 { public GameObject Go; public int Prior; }
        sealed class HudMask308 { public Camera Camera; public int Prior; }
        const float HudOverlayDepth308 = 100f, HudOverlayNear308 = .1f, HudOverlayFar308 = 2000f;   // 직교 카메라는 캔버스 평면(z=0) 앞 100에서 +z를 본다 — 레티클 z = 적까지 거리(m)라 far를 넉넉히
        const int HudLayerSearchTop308 = 30, HudLayerSearchBottom308 = 8;                           // 이름 없는 사용자 층을 위에서부터 찾는다(TagManager: 13–19 · 21–30이 비어 있다)
        static readonly List<HudHidden308> hudHidden308 = new List<HudHidden308>();
        static readonly List<HudLayer308> hudLayers308 = new List<HudLayer308>();
        static readonly List<HudMask308> hudMasks308 = new List<HudMask308>();
        static Canvas hudCanvas308; static RenderMode hudPriorMode308; static Camera hudPriorCamera308, hudFilmCamera308, hudOverlay308; static int hudLayer308 = -1;
        static Transform hudPresenter308; static Canvas worldPrompt308; static bool worldPromptPriorEnabled308;
        static ScriptableRendererFeature[] hudFeatures308 = new ScriptableRendererFeature[0]; static bool[] hudFeaturesPrior308 = new bool[0];
        // keep 별칭(reticle · ink · hp · marks · bearing · minimap · targethp · boss · prompt · lowhp · notice · arrival · all, 또는 물체 이름 그대로)
        // → HUD 물체 이름(HudController · HudVessels308 · HudLockOn304 · Hud*304가 짓는 이름)
        static readonly Dictionary<string, string> HudAlias308 = new Dictionary<string, string>
        {
            { "Reticle", "reticle" }, { "Ink_BrushBar", "ink" }, { "Ink", "ink" }, { "InkBottle", "ink" }, { "Ink_ReceivedSegment", "ink" }, { "Ink_CostPreview", "ink" }, { "HP_BrushStroke", "hp" }, { "HP", "hp" },
            { "Mark_Dodge", "marks" }, { "Mark_Jump", "marks" }, { "Mark_Vehicle", "marks" }, { "BearingLine304", "bearing" }, { "PersistentMinimap", "minimap" },
            { "TargetHpStroke", "targethp" }, { "BossBar304", "boss" }, { "InteractionPrompt", "prompt" }, { "Notice", "notice" }, { "LocationNameCanvas", "arrival" }
        };

        static string HudAliasOf308(string name) =>
            HudAlias308.TryGetValue(name, out var alias) ? alias : name.StartsWith("LowHp_InkEdge_", StringComparison.Ordinal) ? "lowhp" : name.ToLowerInvariant();

        static void ApplyHudFilter308(string keep, Camera filmCamera)
        {
            RestoreHud308();   // 앞 필름이 복원을 못 했어도 기록이 겹치지 않게
            var keepSet = new HashSet<string>(keep.Split(',').Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length > 0));
            bool Keep(string name) => keepSet.Contains("all") || keepSet.Contains(HudAliasOf308(name)) || keepSet.Contains(name.ToLowerInvariant());   // "all" = 숨기지 않고 캔버스만 얹는다
            var hud = Object.FindFirstObjectByType<HudController>();
            var canvas = hud != null ? hud.GetComponentsInChildren<Canvas>(true).FirstOrDefault(c => c.name == "HUD_Canvas") : null;
            if (canvas == null) state?.commits.Add("hud: HUD_Canvas not found (nothing hidden on the HUD)");
            else
            {
                foreach (Transform child in canvas.transform)
                {
                    // 용기 묶음(Vessels308 / 구 Meters304)은 한 단계 내려가 HP · 먹 · 표시를 따로 가린다
                    if (child.name == "Vessels308" || child.name == "Meters304") { foreach (Transform part in child) if (!Keep(part.name)) Hide308(part.gameObject); continue; }
                    if (!Keep(child.name)) Hide308(child.gameObject);
                }
                int layer = FreeLayer308();
                if (filmCamera == null) state?.commits.Add("hud: no film camera (HUD hidden only, not captured)");
                else if (layer < 0) state?.commits.Add("hud: no unnamed layer free for the HUD overlay (HUD hidden only, not captured)");
                else CaptureHud308(canvas, filmCamera, layer);
            }
            // 메뉴 캔버스의 Notice(의뢰 알림 · 토스트)와 도착 카드 캔버스(LocationNameCanvas/ArrivalCard304) — 오버레이라 원래 안 찍히지만 사양대로 끈다
            var root = Object.FindFirstObjectByType<PlaytestUiRoot>();
            var notice = root != null ? root.transform.Find("Playtest_MenuCanvas/Notice") : null;
            if (notice != null && !Keep("Notice")) Hide308(notice.gameObject);
            foreach (var arrival in Object.FindObjectsByType<WorldLocationArrival>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var nameCanvas = arrival.transform.Find("LocationNameCanvas");
                if (nameCanvas != null && !Keep("LocationNameCanvas")) Hide308(nameCanvas.gameObject);
            }
            // 월드 공간 상호작용 글자(WorldInteractionPrompt)는 지연 생성이고 프레젠터가 SetActive를 되돌리므로 Canvas 컴포넌트를 끈다(틱마다)
            var presenter = Object.FindFirstObjectByType<WorldMacroPlaytestHudPresenter>();
            hudPresenter308 = presenter != null ? presenter.transform : null;
            HudFilterTick308();
            state?.commits.Add("hud keep=" + keep + " hidden=" + hudHidden308.Count + (hudOverlay308 != null ? " overlay " + hudOverlay308.name + " layer " + hudLayer308 : " (not captured)"));
        }

        // 캔버스를 WorldSpace로 바꾸고 오버레이 배치를 고정 → 전용 층 → 직교 오버레이 카메라를 캡처 카메라 스택에 → 오버레이 패스의 렌더러 기능 끄기 훅
        static void CaptureHud308(Canvas canvas, Camera filmCamera, int layer)
        {
            int width = Screen.width, height = Screen.height; float scale = Mathf.Max(1e-4f, canvas.scaleFactor);   // 오버레이 모드의 CanvasScaler 배율(1920×1080 게임 뷰면 1)
            hudCanvas308 = canvas; hudPriorMode308 = canvas.renderMode; hudPriorCamera308 = canvas.worldCamera; hudFilmCamera308 = filmCamera; hudLayer308 = layer;
            canvas.renderMode = RenderMode.WorldSpace;
            // 오버레이가 매 프레임 몰던 루트 rect — 피벗 = 화면 픽셀 (pivot·W, pivot·H), 월드 배율 = CanvasScaler 배율, 크기 = 화면/배율 — 를 그대로 고정한다
            var rect = (RectTransform)canvas.transform;
            float parentScale = rect.parent != null ? Mathf.Max(1e-4f, rect.parent.lossyScale.x) : 1f;
            rect.position = new Vector3(rect.pivot.x * width, rect.pivot.y * height, 0f); rect.rotation = Quaternion.identity;
            rect.localScale = Vector3.one * (scale / parentScale); rect.sizeDelta = new Vector2(width / scale, height / scale);
            // 전용 층: 캔버스 트리는 오버레이 카메라만 그리고, 다른 카메라(캡처 · 메인 · 미니맵 …)는 그 층을 보지 않는다
            int bit = 1 << layer;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) StripLayer308(cam, bit);
            StripLayer308(filmCamera, bit);   // 캡처 카메라는 비활성 컴포넌트라 따로
            ApplyHudLayer308(canvas.transform, layer);
            var stack = filmCamera.GetUniversalAdditionalCameraData().cameraStack;
            if (stack == null) { state?.commits.Add("hud: film camera has no camera stack (renderer does not stack) — HUD hidden only, not captured"); return; }
            hudOverlay308 = OpenHudOverlay308(filmCamera, layer, width, height);
            stack.RemoveAll(c => c == null); if (!stack.Contains(hudOverlay308)) stack.Add(hudOverlay308);
            canvas.worldCamera = hudOverlay308;
            hudFeatures308 = RendererFeatures308(filmCamera); hudFeaturesPrior308 = new bool[hudFeatures308.Length];
            RenderPipelineManager.beginCameraRendering -= HudOverlayBegin308; RenderPipelineManager.beginCameraRendering += HudOverlayBegin308;
            RenderPipelineManager.endCameraRendering -= HudOverlayEnd308; RenderPipelineManager.endCameraRendering += HudOverlayEnd308;
            float rtAspect = filmCamera.aspect, viewAspect = width / (float)height;
            state?.commits.Add("hud canvas " + width + "x" + height + " scale " + F308(scale) + " layer " + layer + " features off during overlay: " + string.Join(",", hudFeatures308.Select(f => f.name))
                + (Mathf.Abs(viewAspect - rtAspect) > .01f ? " (WARNING game view aspect " + F308(viewAspect) + " != film " + F308(rtAspect) + ": HUD letterboxed — set the Game view to " + spec.width + "x" + spec.height + ")" : ""));
        }

        // 이름 없는 층 = 아무것도 쓰지 않는 층(인스펙터 목록에 안 나온다). 없으면 -1
        static int FreeLayer308()
        {
            for (int i = HudLayerSearchTop308; i >= HudLayerSearchBottom308; i--) if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return -1;
        }

        static void StripLayer308(Camera cam, int bit)
        {
            if (cam == null || (cam.cullingMask & bit) == 0 || hudMasks308.Any(m => m.Camera == cam)) return;
            hudMasks308.Add(new HudMask308 { Camera = cam, Prior = cam.cullingMask }); cam.cullingMask &= ~bit;
        }

        // 캔버스 트리를 전용 층으로(기록) — 테이크 중 새로 생긴 자식도 틱마다 같이. 바꾼 물체 수를 돌려준다
        static int ApplyHudLayer308(Transform root, int layer)
        {
            int changed = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject; if (go.layer == layer) continue;
                if (!hudLayers308.Any(l => l.Go == go)) hudLayers308.Add(new HudLayer308 { Go = go, Prior = go.layer });
                go.layer = layer; changed++;
            }
            return changed;
        }

        // 직교 오버레이 카메라: 캔버스 평면 [0,W]×[0,H](z=0)를 필름 틀(캡처 카메라 비율)로 본다 — 게임 뷰 비율이 다르면 전체가 들어가게 레터박스
        static Camera OpenHudOverlay308(Camera filmCamera, int layer, int width, int height)
        {
            var go = new GameObject("Presentation297_HudOverlay") { hideFlags = HideFlags.DontSave, layer = layer };
            go.transform.SetPositionAndRotation(new Vector3(width * .5f, height * .5f, -HudOverlayDepth308), Quaternion.identity);
            var overlay = go.AddComponent<Camera>();
            overlay.orthographic = true; overlay.nearClipPlane = HudOverlayNear308; overlay.farClipPlane = HudOverlayFar308;
            float rtAspect = filmCamera.aspect, viewAspect = width / (float)height;
            overlay.aspect = rtAspect;
            overlay.orthographicSize = viewAspect >= rtAspect ? width * .5f / rtAspect : height * .5f;
            overlay.cullingMask = 1 << layer; overlay.clearFlags = CameraClearFlags.Depth; overlay.depth = filmCamera.depth + 1f;
            overlay.allowHDR = filmCamera.allowHDR; overlay.allowMSAA = filmCamera.allowMSAA; overlay.useOcclusionCulling = false;
            overlay.enabled = true;   // URP 스택은 활성 오버레이만 그린다(Overlay 종류는 혼자서는 그려지지 않는다)
            var data = overlay.GetUniversalAdditionalCameraData();
            EditorUtility.CopySerialized(filmCamera.GetUniversalAdditionalCameraData(), data);   // 같은 렌더러 색인(스택 조건) · 볼륨 층
            data.renderType = CameraRenderType.Overlay; data.renderPostProcessing = false; data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off; data.requiresDepthOption = CameraOverrideOption.Off;
            var so = new SerializedObject(data); so.FindProperty("m_ClearDepth").boolValue = true; so.ApplyModifiedPropertiesWithoutUndo();   // clearDepth는 읽기 전용 속성(DosaPlayerBuilder 전례)
            return overlay;
        }

        // 캡처 카메라가 쓰는 렌더러의 기능 목록(UniversalRenderPipelineAsset.m_RendererDataList[m_RendererIndex] — 둘 다 internal이라 리플렉션)
        static ScriptableRendererFeature[] RendererFeatures308(Camera filmCamera)
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var list = asset != null ? Harness303.FieldValue(asset, "m_RendererDataList") as ScriptableRendererData[] : null;
            if (list == null || list.Length == 0) return new ScriptableRendererFeature[0];
            int index = Harness303.FieldValue(filmCamera.GetUniversalAdditionalCameraData(), "m_RendererIndex") is int i ? i : -1;
            if (index < 0) index = Harness303.FieldValue(asset, "m_DefaultRendererIndex") is int d ? d : 0;
            var data = index >= 0 && index < list.Length ? list[index] : null;
            return data != null && data.rendererFeatures != null ? data.rendererFeatures.Where(f => f != null).ToArray() : new ScriptableRendererFeature[0];
        }

        // 오버레이 패스 동안만 렌더러 기능을 끈다 — 전체화면 패스(InkWash297 · CompactMist238)는 Overlay 카메라를 걸러내지 않는다
        static void HudOverlayBegin308(ScriptableRenderContext context, Camera camera)
        {
            if (hudOverlay308 == null || camera != hudOverlay308) return;
            for (int i = 0; i < hudFeatures308.Length; i++) { var f = hudFeatures308[i]; if (f == null) continue; hudFeaturesPrior308[i] = f.isActive; f.SetActive(false); }
        }

        static void HudOverlayEnd308(ScriptableRenderContext context, Camera camera)
        {
            if (hudOverlay308 == null || camera != hudOverlay308) return;
            for (int i = 0; i < hudFeatures308.Length; i++) { var f = hudFeatures308[i]; if (f != null) f.SetActive(hudFeaturesPrior308[i]); }
        }

        static void Hide308(GameObject go)
        {
            if (go == null || hudHidden308.Any(h => h.Go == go)) return;
            hudHidden308.Add(new HudHidden308 { Go = go, WasActive = go.activeSelf });
            if (go.activeSelf) go.SetActive(false);
        }

        // 캡처 직전마다: HUD가 되살린 요소를 다시 끄고, 캔버스 모드 · 층을 확인한 뒤 배치를 다시 만든다(수동 Render는 PlayerUpdateCanvases를 거치지 않는다 — 검토 지적 3).
        // 씬이 사라졌으면 null 검사로 무해
        static void HudFilterTick308()
        {
            if (hudHidden308.Count == 0 && hudCanvas308 == null && hudPresenter308 == null) return;
            bool changed = false;
            foreach (var h in hudHidden308) if (h.Go != null && h.Go.activeSelf) { h.Go.SetActive(false); changed = true; }
            if (hudCanvas308 != null)
            {
                if (hudCanvas308.renderMode != RenderMode.WorldSpace) { hudCanvas308.renderMode = RenderMode.WorldSpace; changed = true; }
                if (hudLayer308 >= 0 && ApplyHudLayer308(hudCanvas308.transform, hudLayer308) > 0) changed = true;
            }
            if (hudPresenter308 != null)
            {
                if (worldPrompt308 == null)
                {
                    var prompt = hudPresenter308.Find("WorldInteractionPrompt");
                    var promptCanvas = prompt != null ? prompt.GetComponent<Canvas>() : null;
                    if (promptCanvas != null) { worldPrompt308 = promptCanvas; worldPromptPriorEnabled308 = promptCanvas.enabled; }
                }
                if (worldPrompt308 != null && worldPrompt308.enabled) { worldPrompt308.enabled = false; changed = true; }
            }
            if (changed || hudCanvas308 != null) Canvas.ForceUpdateCanvases();   // 싸다: 더러운 요소만 다시 만든다 — 이 프레임의 레티클 · 먹 수위도 캡처에 들어간다
        }

        // 역순 복원: 숨김 → 월드 프롬프트 → 층 → 오버레이 카메라(스택에서 빼고 파괴) → 카메라 cullingMask → 캔버스 모드(오버레이로 돌아가면 Unity가 루트 rect를 다시 몬다)
        static void RestoreHud308()
        {
            RenderPipelineManager.beginCameraRendering -= HudOverlayBegin308; RenderPipelineManager.endCameraRendering -= HudOverlayEnd308;
            hudFeatures308 = new ScriptableRendererFeature[0]; hudFeaturesPrior308 = new bool[0];
            for (int i = hudHidden308.Count - 1; i >= 0; i--) { var h = hudHidden308[i]; if (h.Go != null && h.WasActive && !h.Go.activeSelf) h.Go.SetActive(true); }
            hudHidden308.Clear();
            if (worldPrompt308 != null) worldPrompt308.enabled = worldPromptPriorEnabled308;
            worldPrompt308 = null; hudPresenter308 = null;
            for (int i = hudLayers308.Count - 1; i >= 0; i--) { var l = hudLayers308[i]; if (l.Go != null) l.Go.layer = l.Prior; }
            hudLayers308.Clear();
            if (hudOverlay308 != null)
            {
                if (hudFilmCamera308 != null) hudFilmCamera308.GetUniversalAdditionalCameraData().cameraStack?.Remove(hudOverlay308);
                Object.DestroyImmediate(hudOverlay308.gameObject);
            }
            for (int i = hudMasks308.Count - 1; i >= 0; i--) { var m = hudMasks308[i]; if (m.Camera != null) m.Camera.cullingMask = m.Prior; }
            hudMasks308.Clear();
            if (hudCanvas308 != null) { hudCanvas308.worldCamera = hudPriorCamera308; hudCanvas308.renderMode = hudPriorMode308; }
            hudCanvas308 = null; hudPriorCamera308 = null; hudFilmCamera308 = null; hudOverlay308 = null; hudLayer308 = -1;
        }

        // ---------------------------------------------------------------- 강토 로더 정착
        // settle>0: captureFramerate를 미루고 RealmStreamLoader308이 1.5 s 조용하거나(Loading/Leaving 없음 · 적재 횟수 불변) 상한에 닿으면 캡처 시작
        const float SettleQuietSeconds308 = 1.5f;
        static bool settling308; static float settleStart308, settleQuiet308, settleCap308; static int settleActivity308;
        static RealmStreamLoader308[] settleLoaders308 = new RealmStreamLoader308[0];

        static void BeginSettle308(float cap)
        {
            settling308 = true; settleCap308 = Mathf.Max(0f, cap);
            settleStart308 = settleQuiet308 = Time.realtimeSinceStartup; settleActivity308 = -1;
            settleLoaders308 = Object.FindObjectsByType<RealmStreamLoader308>(FindObjectsSortMode.None);
        }

        // true = 정착 끝(캡처 시작). 상한으로 끝났으면 commits에 경고를 남긴다
        static bool SettleTick308()
        {
            float now = Time.realtimeSinceStartup;
            bool busy = RealmLoaderBusy308(out int activity);
            if (busy || activity != settleActivity308) { settleQuiet308 = now; settleActivity308 = activity; }
            bool quiet = now - settleQuiet308 >= SettleQuietSeconds308, capped = now - settleStart308 >= settleCap308;
            if (!quiet && !capped) return false;
            settling308 = false;
            state?.commits.Add("settle " + (quiet ? "quiet" : "cap") + " after " + (now - settleStart308).ToString("F1", CultureInfo.InvariantCulture) + " s, loaders " + settleLoaders308.Length
                + (capped && !quiet ? " (WARNING loader still busy: scenes may pop in during the take)" : ""));
            return true;
        }

        // RealmStreamLoader308의 private states[](Out/Loading/In/Leaving)를 리플렉션으로 읽기만 한다 — Describe()는 문자열이라 틱마다 쓰기엔 비싸다
        static bool RealmLoaderBusy308(out int activity)
        {
            activity = 0; bool busy = false;
            foreach (var loader in settleLoaders308)
            {
                if (loader == null) continue;
                activity += loader.Loads + loader.Unloads;
                if (Harness303.FieldValue(loader, "states") is RealmStreamLoader308.State[] states)
                    foreach (var s in states) if (s == RealmStreamLoader308.State.Loading || s == RealmStreamLoader308.State.Leaving) busy = true;
            }
            return busy;
        }

        // ---------------------------------------------------------------- 적중 로그
        // CombatLoopWiring.EnemyDamageResolved(실제 생존 대상에게 적용된 피해만) → commits "HIT <id> dmg=<n> hp=<n> [KILLED] frame=<index>".
        // 저장은 FilmTick의 15프레임 주기에 맡긴다. hitLog=false면 구독하지 않는다.
        static CombatLoopWiring hitWiring308;

        static void OpenHitLog308(WorldMacroPlaytestSession session)
        {
            CloseHitLog308();
            var wiring = session != null && session.Walker != null ? session.Walker.Wiring : null;
            if (wiring == null) wiring = Object.FindFirstObjectByType<CombatLoopWiring>();
            if (wiring == null) { state?.commits.Add("hitlog: no CombatLoopWiring in the scene"); return; }
            hitWiring308 = wiring; wiring.EnemyDamageResolved += OnEnemyDamage308;
        }

        static void CloseHitLog308()
        {
            if (ReferenceEquals(hitWiring308, null)) return;   // 파괴된 물체여도 관리 이벤트의 구독은 풀어 둔다
            hitWiring308.EnemyDamageResolved -= OnEnemyDamage308; hitWiring308 = null;
        }

        static void OnEnemyDamage308(EnemyDamageResult r)
        {
            if (state == null) return;
            var target = r.Target;
            string id = target != null ? (target.GetComponentInParent<PrologueEncounter>()?.Id ?? target.name) : "?";
            state.commits.Add("HIT " + id + " dmg=" + r.AppliedDamage.ToString("F1", CultureInfo.InvariantCulture) + " hp=" + (target != null ? target.Hp.ToString("F1", CultureInfo.InvariantCulture) : "?")
                + (r.Killed ? " KILLED" : "") + " frame=" + index);
        }
    }
}
