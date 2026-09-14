using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // [SPEC-WORLD-MAP §6 · §7 감사 메서드] 월드 맵 검증 진입점 — 전부 public static string, 1호출 = 1프레임(reflection-method-call).
    // P1(§8)에서 구현: A8 AssetGate · A9 StatusWords/ScaleMirror · A11 Determinism(합성 해시만) · A12 NarrativeSilence · CaptureFar(far 오버로드) · AuthoringLog(F 기록).
    // 나머지는 서명만 세우고 "NOT_IMPLEMENTED(P<n>)"를 돌려준다 — 게이트 단계(§8 P2~P5)가 채운다. 반환 첫 토큰 = PASS / FAIL / NOT_IMPLEMENTED.
    // 규율: 이 파일은 계측 도구다 — StatusWords의 float 리터럴 스캔은 빌더·carver·Driver 소스 대상이며 본 파일은 제외한다(§6 A9).
    // A9 StatusWords 4단: LOCKED 0 · float 리터럴(화이트리스트 밖 0 — [SealingConstants] 클래스는 명시 출력 후 건너뜀, §13-13) ·
    // 런타임 소스(App/World·Data/World) SetHeights/SetHoles/UnityEngine.Splines/static Instance 0 · Driver 합산 정확히 1(드라이버가 든 씬 로드 시).
    // 픽셀 통계는 여기서 계산하지 않는다(PNG → Tools/LookAudit/*.py) — WorldLookAudit와 같은 분업.
    public static class WorldMapAudit
    {
        public const string CaptureFolder = "Screenshots/World";
        public const string AreaSheetPath = "Assets/_Project/Data/World/AreaSheet_GeumpyoRoad.asset";
        public const string WorldScalePath = "Assets/_Project/Data/World/WorldScale_Cheongrim.asset";
        public const string TerrainSynthPath = "Assets/_Project/Data/World/TerrainSynth_GeumpyoRoad.asset";
        public const string DataWorldFolder = "Assets/_Project/Data/World";
        public const string ShadersFolder = "Assets/_Project/Shaders";
        public const string LandmarkModelsFolder = "Assets/_Project/Art/Models/Landmarks";
        public const string AuthoringLogFolder = "Tools/WorldRuns";
        public const string AgentTypeName = "Humanoid_Oheangbu";

        private static readonly string[] RequiredLayers = { "WorldGround", "WorldRidge", "WorldAnchor", "WorldLight", "WorldVeg", "WorldRibbon" };
        private static readonly string[] RequiredPackages = { "com.unity.terrain-tools", "com.unity.splines", "com.unity.probuilder", "com.unity.ai.navigation" };
        private static readonly string[] WorldScriptFolders =
        {
            "Assets/_Project/Scripts/Data/World",
            "Assets/_Project/Scripts/App/World",
            "Assets/_Project/Scripts/Editor/World",
        };
        private static readonly string[] BuilderDriverFolders =
        {
            "Assets/_Project/Scripts/App/World",
            "Assets/_Project/Scripts/Editor/World",
        };
        // §6 A9 런타임 asmdef 금지 참조 — Terrain 편집 API · Splines · 정적 Instance 싱글턴(주석·Tooltip 제외).
        private static readonly string[] RuntimeWorldFolders =
        {
            "Assets/_Project/Scripts/App/World",
            "Assets/_Project/Scripts/Data/World",
        };
        private static readonly Regex RuntimeForbidden = new Regex(@"SetHeights|SetHoles|UnityEngine\.Splines|static\s+[A-Za-z<>]+\s+Instance\b", RegexOptions.Compiled);
        // §13-13 봉인 상수 클래스 표식 — 리터럴 스캔은 이 어트리뷰트가 붙은 class 본문을 명시 출력 후 건너뛴다.
        private const string SealingAttributeToken = "[SealingConstants]";
        private static readonly Regex ClassDecl = new Regex(@"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
        // §6 A9 화이트리스트 {0, 0.5, 1, 2, -1, 1e-4} + PI — 부호는 리터럴 앞 '-'로 판정한다.
        private static readonly float[] LiteralWhitelist = { 0f, 0.5f, 1f, 2f, -1f, 1e-4f };
        private static readonly Regex FloatLiteral = new Regex(@"(-?)\b(\d+\.?\d*(?:[eE][-+]?\d+)?)f\b", RegexOptions.Compiled);
        private static readonly Regex Hangul = new Regex("[\uAC00-\uD7A3\u1100-\u11FF\u3130-\u318F]", RegexOptions.Compiled);

        // ---------------------------------------------------------------- A. 캐논 게이트

        public static string CanonGates() => NotImplemented(3, "A1 — NavMesh 경로 × GateSlot 볼륨 교차 · mode==MainPath 0 · 예고 슬롯 3 존재");
        public static string InvisibleWalls() => NotImplemented(3, "A2 — 렌더러 없는 비트리거 콜라이더 census · 경계 띠 경사 ≥boundarySlopeDeg 비율");
        public static string NavIslands() => NotImplemented(3, "A3 — 본길·지선 단일 섬 · 이동 컴포넌트 census");
        public static string Fords() => NotImplemented(3, "A3 — 여울 하상 ≤fordMaxDepth · 심연 도달 불가");
        public static string MaterialGate() => NotImplemented(4, "A4 — 지속 발광 = InkLightSource 1종 · 인스턴스 4종 · 환경 재질 Emission 0");
        public static string PaletteProbe() => NotImplemented(4, "A5 — 씬·재질 색 저장 0 · _Oh* = 팔레트 .linear · 등불색 = GetLanternColor()");
        public static string TextureGate() => NotImplemented(4, "A5 — 월드 씬 참조 재질의 텍스처 참조 0(엔진 내부 3종·_OhMarkMask 제외)");

        // A7 — Terrain 봉인. P1은 Terrain 무접촉(D1)이라 로드된 씬에 Terrain이 없으면 P2 안내를 돌려준다.
        public static string Sealing()
        {
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (terrains.Length == 0) return NotImplemented(2, "A7 — 로드된 씬에 Terrain 0(P1 = Terrain 무접촉) · TerrainSealer.Verify는 P2 InkTerrain 이식 씬에서");
            var sb = new StringBuilder();
            bool ok = true;
            foreach (var t in terrains)
            {
                string r = TerrainSealer.Verify(t);
                if (!r.StartsWith("PASS", StringComparison.Ordinal)) ok = false;
                sb.Append(t.name).Append(": ").Append(r).Append('\n');
            }
            return (ok ? "PASS A7 Sealing\n" : "FAIL A7 Sealing\n") + sb.ToString().TrimEnd();
        }

        // A8 — 에셋·의존 규약: manifest 직접 의존 4종 · TagManager 레이어 6종 · NavMesh 에이전트 타입 · 셰이더 한글 Tooltip 0 ·
        // Blender FBX 루트 scale 1·rot 0 · *.report.json 전 PASS. Meshy/유료 팩 GUID 참조 0은 씬이 생기는 P3에서 추가한다.
        public static string AssetGate()
        {
            var lines = new List<string>();
            bool ok = true;

            string manifestPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "manifest.json"));
            if (!File.Exists(manifestPath))
            {
                ok = false;
                lines.Add("FAIL manifest: " + manifestPath + " 없음");
            }
            else
            {
                string manifest = File.ReadAllText(manifestPath);
                foreach (var pkg in RequiredPackages)
                {
                    bool present = Regex.IsMatch(manifest, "\"" + Regex.Escape(pkg) + "\"\\s*:");
                    if (!present) ok = false;
                    lines.Add((present ? "ok  " : "FAIL") + " manifest 직접 의존 " + pkg);
                }
            }

            foreach (var layer in RequiredLayers)
            {
                bool present = LayerMask.NameToLayer(layer) >= 0;
                if (!present) ok = false;
                lines.Add((present ? "ok  " : "FAIL") + " TagManager 레이어 " + layer);
            }

            int agentId;
            bool agentFound = TryFindAgentType(AgentTypeName, out agentId);
            if (!agentFound) ok = false;
            lines.Add((agentFound ? "ok  " : "FAIL") + " NavMesh 에이전트 타입 " + AgentTypeName + (agentFound ? " id=" + agentId : " 미등록(Navigation 창 P0 chore)"));

            int tooltipHits = 0;
            if (Directory.Exists(ShadersFolder))
            {
                foreach (var shader in Directory.GetFiles(ShadersFolder, "*.shader", SearchOption.AllDirectories))
                {
                    var text = File.ReadAllLines(shader);
                    for (int i = 0; i < text.Length; i++)
                    {
                        // 어트리뷰트 형태([Tooltip( … 한글 …)만 위반 — 주석 줄의 「Tooltip」 언급은 규약 문구다(2026-09-07 오탐 정정)
                        string t = text[i].TrimStart();
                        if (t.StartsWith("//", StringComparison.Ordinal)) continue;
                        if (t.Contains("[Tooltip(") && Hangul.IsMatch(t))
                        {
                            tooltipHits++;
                            lines.Add("FAIL 셰이더 한글 Tooltip " + Norm(shader) + ":" + (i + 1));
                        }
                    }
                }
            }
            if (tooltipHits > 0) ok = false; else lines.Add("ok   셰이더 한글 Tooltip 0");

            if (Directory.Exists(LandmarkModelsFolder))
            {
                var reports = Directory.GetFiles(LandmarkModelsFolder, "*.report.json", SearchOption.TopDirectoryOnly);
                foreach (var report in reports)
                {
                    string result = ReadJsonString(File.ReadAllText(report), "result");
                    bool pass = result == "PASS";
                    if (!pass) ok = false;
                    lines.Add((pass ? "ok  " : "FAIL") + " report " + Path.GetFileName(report) + " result=" + (result ?? "?"));
                }
                if (reports.Length == 0) lines.Add("ok   report.json 0건(랜드마크 미반입)");

                foreach (var fbx in Directory.GetFiles(LandmarkModelsFolder, "*.fbx", SearchOption.TopDirectoryOnly))
                {
                    string assetPath = Norm(fbx);
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    if (root == null) { lines.Add("FAIL FBX 로드 실패 " + assetPath); ok = false; continue; }
                    bool scaleOk = Approximately(root.transform.localScale, Vector3.one);
                    bool rotOk = Quaternion.Angle(root.transform.localRotation, Quaternion.identity) <= 1e-4f;
                    if (!scaleOk || !rotOk) ok = false;
                    lines.Add((scaleOk && rotOk ? "ok  " : "FAIL") + " FBX 루트 " + Path.GetFileName(fbx) + " scale=" + root.transform.localScale + " rot=" + root.transform.localRotation.eulerAngles);
                }
            }
            else lines.Add("ok   " + LandmarkModelsFolder + " 미생성(랜드마크 미반입)");

            lines.Add("note Meshy/유료 팩 GUID 참조 0 = P3(월드 씬 생성 후)");
            return (ok ? "PASS A8 AssetGate\n" : "FAIL A8 AssetGate\n") + string.Join("\n", lines);
        }

        // A9 — 상태어·리터럴: SO·World 스크립트의 'LOCKED' 0 · 빌더/Driver 소스 float 리터럴 화이트리스트 밖 0(SO 필드값과 일치하면 표기).
        public static string StatusWords()
        {
            var lines = new List<string>();
            bool ok = true;
            string token = "LOCK" + "ED";   // 자기 적중 회피

            if (Directory.Exists(DataWorldFolder))
            {
                foreach (var asset in Directory.GetFiles(DataWorldFolder, "*.asset", SearchOption.AllDirectories))
                {
                    if (File.ReadAllText(asset).Contains(token)) { ok = false; lines.Add("FAIL " + token + " in " + Norm(asset)); }
                }
            }
            foreach (var folder in WorldScriptFolders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var cs in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
                {
                    if (IsAuditSource(cs)) continue;
                    var text = File.ReadAllLines(cs);
                    for (int i = 0; i < text.Length; i++)
                    {
                        if (text[i].Contains(token)) { ok = false; lines.Add("FAIL " + token + " " + Norm(cs) + ":" + (i + 1)); }
                    }
                }
            }
            lines.Add("scan " + token + " = " + (ok ? "0건" : "적중"));

            var soValues = CollectSoFloatValues();
            int offenders = 0;
            int scanned = 0;
            foreach (var folder in BuilderDriverFolders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var cs in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
                {
                    if (IsAuditSource(cs)) continue;
                    scanned++;
                    var text = File.ReadAllLines(cs);
                    var sealedClasses = new List<string>();
                    bool[] sealedLines = SealedLineMask(text, sealedClasses);
                    foreach (var cls in sealedClasses)
                    {
                        lines.Add("skipped: " + cls + " (SealingConstants) " + Norm(cs));
                    }
                    for (int i = 0; i < text.Length; i++)
                    {
                        if (sealedLines[i]) continue;
                        string line = StripLineComment(text[i]);
                        if (line.Contains("[Tooltip(")) continue;
                        foreach (Match m in FloatLiteral.Matches(line))
                        {
                            float v;
                            if (!float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                            if (m.Groups[1].Value == "-") v = -v;
                            if (IsWhitelisted(v)) continue;
                            offenders++;
                            string tag = soValues.Contains(v) ? " (=SO 필드값 중복)" : "";
                            lines.Add("FAIL literal " + m.Value + tag + " " + Norm(cs) + ":" + (i + 1));
                        }
                    }
                }
            }
            if (offenders > 0) ok = false;
            lines.Add("scan float literal: files=" + scanned + " offenders=" + offenders + " (화이트리스트 {0, 0.5, 1, 2, -1, 1e-4, PI} · 감사 소스 제외 · [SealingConstants] 명시 제외)");

            // 런타임 소스(App/World · Data/World) — Terrain 편집 API·Splines·정적 Instance 0(주석·Tooltip 제외)
            int runtimeHits = 0;
            int runtimeScanned = 0;
            foreach (var folder in RuntimeWorldFolders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var cs in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
                {
                    runtimeScanned++;
                    var text = File.ReadAllLines(cs);
                    for (int i = 0; i < text.Length; i++)
                    {
                        string line = StripLineComment(text[i]);
                        if (line.Contains("[Tooltip(")) continue;
                        var m = RuntimeForbidden.Match(line);
                        if (!m.Success) continue;
                        runtimeHits++;
                        lines.Add("FAIL runtime ref '" + m.Value + "' " + Norm(cs) + ":" + (i + 1));
                    }
                }
            }
            if (runtimeHits > 0) ok = false;
            lines.Add("scan runtime source: files=" + runtimeScanned + " hits=" + runtimeHits + " (SetHeights|SetHoles|UnityEngine.Splines|static T Instance)");

            // Driver 합산 정확히 1(층 11) — 드라이버가 든 씬(FarSet)이 로드돼 있을 때만 판정, 아니면 P2 안내
            var lookDrivers = Object.FindObjectsByType<WorldLookDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var scaleDrivers = Object.FindObjectsByType<WorldScaleDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (scaleDrivers.Length == 0)
            {
                // 스파이크 씬(C1_WorldLookdev)은 WorldLookDriver만 든다 — 월드 씬(FarSet) 판정이 아니다
                lines.Add("note " + NotImplemented(2, "Driver 합산 정확히 1 — WorldScaleDriver 0 = 월드 씬(FarSet) 미로드(현재 WorldLookDriver=" + lookDrivers.Length + ")"));
            }
            else
            {
                bool single = lookDrivers.Length == 1 && scaleDrivers.Length == 1;
                if (!single) ok = false;
                lines.Add((single ? "ok  " : "FAIL") + " Driver census WorldLookDriver=" + lookDrivers.Length + " WorldScaleDriver=" + scaleDrivers.Length + " (합산 정확히 1)");
            }
            return (ok ? "PASS A9 StatusWords\n" : "FAIL A9 StatusWords\n") + string.Join("\n", lines);
        }

        // 한 줄 주석 제거(문자열 안 "//"는 구분하지 않는다 — 계측용 단순 스캔).
        private static string StripLineComment(string line)
        {
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            return comment >= 0 ? line.Substring(0, comment) : line;
        }

        // [SealingConstants]가 붙은 class 본문 줄 마스크 — 어트리뷰트 줄부터 중괄호 깊이가 0으로 돌아오는 줄까지(단순 brace-depth 스캔).
        private static bool[] SealedLineMask(string[] text, List<string> classNames)
        {
            var mask = new bool[text.Length];
            bool pending = false;
            bool inside = false;
            bool entered = false;
            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                string line = StripLineComment(text[i]);
                if (!inside)
                {
                    if (line.Contains(SealingAttributeToken)) pending = true;
                    if (pending)
                    {
                        var m = ClassDecl.Match(line);
                        if (m.Success)
                        {
                            classNames.Add(m.Groups[1].Value);
                            inside = true;
                            entered = false;
                            depth = 0;
                            pending = false;
                        }
                        else if (line.Trim().Length > 0 && !line.TrimStart().StartsWith("[", StringComparison.Ordinal))
                        {
                            pending = false;   // 어트리뷰트 뒤에 class 선언이 오지 않음 — 표식 무효
                        }
                    }
                    if (!inside)
                    {
                        mask[i] = pending;
                        continue;
                    }
                }
                mask[i] = true;
                foreach (char c in line)
                {
                    if (c == '{') { depth++; entered = true; }
                    else if (c == '}') depth--;
                }
                if (entered && depth <= 0) inside = false;
            }
            return mask;
        }

        // A9 — 값 미러: WorldScaleSO.walkSpeedMps == CombatConfigSO.MoveSpeed (Data는 Combat을 참조할 수 없어 값으로 동치를 감사한다).
        public static string ScaleMirror()
        {
            var scale = AssetDatabase.LoadAssetAtPath<WorldScaleSO>(WorldScalePath);
            if (scale == null) return "FAIL A9 ScaleMirror: " + WorldScalePath + " 없음";
            var config = AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DevSceneKit.DefaultConfigPath);
            if (config == null) return "FAIL A9 ScaleMirror: " + DevSceneKit.DefaultConfigPath + " 없음";
            bool same = scale.WalkSpeedMps == config.MoveSpeed;
            return (same ? "PASS" : "FAIL") + " A9 ScaleMirror walkSpeedMps=" + F(scale.WalkSpeedMps) + " MoveSpeed=" + F(config.MoveSpeed)
                   + " MetersPerMinute=" + F(scale.MetersPerMinute);
        }

        // A11 — 결정론(P1 = 합성 해시만): 같은 시트·시드로 TerrainSynth.Generate 2회 → HeightsHash 동일 ∧ max|Δh| = 0.
        // Terrain 인스턴스 재해시(브러시/외부 편집 감지)는 P3 BuildArea 이후 WorldBuildStamp와 대조한다.
        public static string Determinism()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<AreaSheetSO>(AreaSheetPath);
            if (sheet == null) return "FAIL A11 Determinism: " + AreaSheetPath + " 없음";
            if (sheet.Synth == null) return "FAIL A11 Determinism: AreaSheet.synth 미지정";
            var scale = AssetDatabase.LoadAssetAtPath<WorldScaleSO>(WorldScalePath);
            if (scale == null) return "FAIL A11 Determinism: " + WorldScalePath + " 없음";

            var watch = System.Diagnostics.Stopwatch.StartNew();
            float[,] a = TerrainSynth.Generate(sheet, sheet.Synth, scale);
            long msA = watch.ElapsedMilliseconds;
            float[,] b = TerrainSynth.Generate(sheet, sheet.Synth, scale);
            long msB = watch.ElapsedMilliseconds - msA;
            if (a == null || b == null) return "FAIL A11 Determinism: Generate가 null 반환";

            string hashA = TerrainSynth.HeightsHash(a);
            string hashB = TerrainSynth.HeightsHash(b);
            bool sameShape = a.GetLength(0) == b.GetLength(0) && a.GetLength(1) == b.GetLength(1);
            float maxDelta = 0f;
            if (sameShape)
            {
                for (int y = 0; y < a.GetLength(0); y++)
                    for (int x = 0; x < a.GetLength(1); x++)
                    {
                        float d = Mathf.Abs(a[y, x] - b[y, x]);
                        if (d > maxDelta) maxDelta = d;
                    }
            }
            bool ok = sameShape && hashA == hashB && maxDelta == 0f;
            return (ok ? "PASS" : "FAIL") + " A11 Determinism res=" + a.GetLength(0) + "x" + a.GetLength(1)
                   + " hashA=" + hashA + " hashB=" + hashB + " maxAbsDelta=" + F(maxDelta)
                   + " seed=" + sheet.Synth.Seed + " ms=" + msA + "/" + msB
                   + "\nnote Terrain 인스턴스 재해시(브러시 감지) = P3 WorldBuildStamp 이후";
        }

        // A12 — 서사 침묵 census: 시트 pois[].narrativeSlot 전부 공란(주막 방(榜) 슬롯 1 제외는 P4 씬 텍스트 census에서).
        public static string NarrativeSilence()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<AreaSheetSO>(AreaSheetPath);
            if (sheet == null) return "FAIL A12 NarrativeSilence: " + AreaSheetPath + " 없음";
            var pois = sheet.Pois ?? new AreaSheetSO.PoiSpec[0];
            var filled = new List<string>();
            foreach (var poi in pois)
                if (poi != null && !string.IsNullOrEmpty(poi.narrativeSlot)) filled.Add(poi.id + "=\"" + poi.narrativeSlot + "\"");
            bool ok = filled.Count == 0;
            return (ok ? "PASS" : "FAIL") + " A12 NarrativeSilence pois=" + pois.Length + " filledNarrativeSlots=" + filled.Count
                   + (ok ? "" : "\n" + string.Join("\n", filled));
        }

        public static string SpikeRegression() => NotImplemented(2, "A10 — C1_WorldLookdev 컷1·컷2 신선 로드 → FarSet 로드·플레이·언로드 → 재캡처 diff 0");

        // ---------------------------------------------------------------- C. 통행·스케일

        public static string WalkMinutes(string from, string to) => NotImplemented(3, "C1·C2 — NavMesh 경로 길이 ÷ MetersPerMinute (" + from + " → " + to + ")");
        public static string SlopeMap() => NotImplemented(3, "C3 — 본선·지선 종단/횡단 경사 · NavMesh 삼각형 ≤45° · ≥cliffSlopeDeg 셀 = 키트 매입/경계");
        public static string DropAudit() => NotImplemented(3, "C4 — 코리도 ±3 m NavMesh 경계 에지 자유 낙하 ≤maxWalkableDropM · 치명 에지 ≥3 m 밖");
        public static string OneWayLedge() => NotImplemented(3, "C6 — 상단 별도 섬 ∧ 낙차턱 ∈ [oneWayDropMinM, maxWalkableDropM] ∧ NavMeshLink/OffMeshLink 0");
        public static string Connectivity() => NotImplemented(3, "C7 — 차수 1 노드 전부 PoiSlot/rewardSlot 반경 안");
        public static string LandmarkSeamScan() => NotImplemented(3, "C8·C9 — 발자국 이음 |지면−발자국면| ≤0.05 · 밑면 노출 0 · FBX 바운드 = report.json ±1%");

        // ---------------------------------------------------------------- B·D. 룩·인력

        public static string HorizonClosure() => NotImplemented(4, "B13 — 본선 40 표본 × yaw 5° 림 앙각 ≤rimElevationMaxDeg ∧ L1~L4 상단 앙각 > 림");
        public static string RidgeStacking() => NotImplemented(4, "층 8 — 인접 겹 L(k+1) 상단 앙각 > L(k) 방위 비율 ≥60%(해석적)");
        public static string LumaAlongPath() => NotImplemented(4, "B12 — 본선 30 m 간격 전방 30° 원뿔 지형 luma 중앙값(WorldGround 마스크)");
        public static string SightlineCheck(string poi, string target) => NotImplemented(4, "D1 — CaptureLayerMask 픽셀 ≥15 ∧ 가림 ≤10% ∧ Mid 직선 ≤midAttractionMaxDistance (" + poi + " → " + target + ")");
        public static string AttractionOrder(string poi) => NotImplemented(4, "D6 — 크리티컬 광원 영역 채도·luma > 이면 (" + poi + ")");

        // ---------------------------------------------------------------- E·F. 성능·저작 단가

        public static string RenderStatsAt(string poi) => NotImplemented(5, "E — tris/drawCalls/SetPass @ " + poi);

        // F — 저작 단가 기록: Tools/WorldRuns/run-<date>.md 에 한 줄 append(층 · 시간 · UTC). PASS 조건이 아니라 기록 항목.
        public static string AuthoringLog(string layer, float hours)
        {
            if (string.IsNullOrEmpty(layer)) return "FAIL F AuthoringLog: layer 공란";
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", AuthoringLogFolder));
            Directory.CreateDirectory(dir);
            var utc = DateTime.UtcNow;
            string file = Path.Combine(dir, "run-" + utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".md");
            bool fresh = !File.Exists(file);
            var sb = new StringBuilder();
            if (fresh)
            {
                sb.Append("# WorldRuns run-").Append(utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append(" — SPEC-WORLD-MAP §6 F 저작 단가 기록\n\n");
                sb.Append("| UTC | 층 | 시간(h) |\n|---|---|---|\n");
            }
            sb.Append("| ").Append(utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(" | ").Append(layer).Append(" | ").Append(F(hours)).Append(" |\n");
            File.AppendAllText(file, sb.ToString(), new UTF8Encoding(false));
            return "PASS F AuthoringLog " + file;
        }

        // ---------------------------------------------------------------- 캡처 3종

        // WorldLookAudit.Capture의 far 오버로드(§6 도구 — 기존 서명은 far 1000 유지 = 스파이크 A10 무손상). 월드 컷은 far = WorldScaleSO.cameraFar.
        // 1920x1080 · FOV 60 · SolidColor 소지(드라이버 팔레트) · sRGB RT → PNG. 파일 = 프로젝트 루트 Screenshots/World(.gitignore).
        public static string CaptureFar(float x, float y, float z, float yaw, float pitch, string name, bool post, float far)
        {
            const int width = 1920;
            const int height = 1080;
            if (string.IsNullOrEmpty(name)) return "FAIL CaptureFar: name 공란";
            if (far <= 0f) return "FAIL CaptureFar: far ≤ 0";
            var driver = Object.FindFirstObjectByType<WorldLookDriver>();
            if (driver != null) driver.Apply();

            var go = new GameObject("~WorldCaptureCamera");
            go.hideFlags = HideFlags.HideAndDontSave;
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = far;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = driver != null && driver.Palette != null ? driver.Palette.PaperColor : Color.black;
            cam.allowHDR = true;
            go.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(pitch, yaw, 0f));
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = post;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = true;

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            string fullPath;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                Directory.CreateDirectory(CaptureFolder);
                fullPath = Path.GetFullPath(Path.Combine(CaptureFolder, name + ".png"));
                File.WriteAllBytes(fullPath, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(go);
            }
            return fullPath;
        }

        public static string CaptureDepthR32(float x, float y, float z, float yaw, float pitch, string name, float far)
            => NotImplemented(4, "선형 눈 깊이 m float32 raw .r32 (RFloat RT) — " + name);

        public static string CaptureLayerMask(float x, float y, float z, float yaw, float pitch, string name, string layerName, float far)
            => NotImplemented(4, "포스트 off · cullingMask=" + layerName + " · 배경 센티널 (255,0,255) — " + name);

        // ---------------------------------------------------------------- 내부

        private static string NotImplemented(int phase, string what) => "NOT_IMPLEMENTED(P" + phase + ") " + what;

        private static bool TryFindAgentType(string name, out int agentTypeId)
        {
            int count = NavMesh.GetSettingsCount();
            for (int i = 0; i < count; i++)
            {
                var settings = NavMesh.GetSettingsByIndex(i);
                if (NavMesh.GetSettingsNameFromID(settings.agentTypeID) == name)
                {
                    agentTypeId = settings.agentTypeID;
                    return true;
                }
            }
            agentTypeId = 0;
            return false;
        }

        private static bool IsWhitelisted(float v)
        {
            foreach (var w in LiteralWhitelist) if (v == w) return true;
            return Mathf.Abs(v - Mathf.PI) <= 1e-4f;
        }

        private static bool IsAuditSource(string path)
        {
            string file = Path.GetFileName(path);
            return file == nameof(WorldMapAudit) + ".cs";
        }

        // WorldScaleSO·TerrainSynthSO 직렬화 float 값 집합 — 코드 리터럴이 데이터를 복제했는지 표기용.
        private static HashSet<float> CollectSoFloatValues()
        {
            var set = new HashSet<float>();
            CollectFloats(AssetDatabase.LoadAssetAtPath<Object>(WorldScalePath), set);
            CollectFloats(AssetDatabase.LoadAssetAtPath<Object>(TerrainSynthPath), set);
            return set;
        }

        private static void CollectFloats(Object asset, HashSet<float> set)
        {
            if (asset == null) return;
            var so = new SerializedObject(asset);
            var it = so.GetIterator();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = true;
                if (it.propertyType == SerializedPropertyType.Float) set.Add(it.floatValue);
                else if (it.propertyType == SerializedPropertyType.Integer) set.Add(it.intValue);
            }
        }

        private static string ReadJsonString(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static bool Approximately(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) <= 1e-4f && Mathf.Abs(a.y - b.y) <= 1e-4f && Mathf.Abs(a.z - b.z) <= 1e-4f;
        }

        private static string Norm(string path) => path.Replace('\\', '/');

        private static string F(float v) => v.ToString(CultureInfo.InvariantCulture);

        public static string Sha256File(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
