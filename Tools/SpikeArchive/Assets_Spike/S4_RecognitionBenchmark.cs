// S4 인식 정량 벤치마크(SPEC-SPIKE-SPELL-RECOGNITION) — "PDollar/JamoMatcher를 계속 쓸 것인가"의 판정 데이터를 만든다.
// 제시형 세션: 하네스가 목표 글자를 제시 -> 사용자가 Q 홀드로 작도 -> 릴리즈 판정 -> 기대/실제 자동 대조·누적.
// 검증이 끝나면 폴더째 제거한다(Spike 규약).
//
// 조작:
//   Q 홀드 + 마우스 드래그 = 작도(DrawingInputController가 처리) / 릴리즈 = 판정·기록
//   N = 이번 글자 건너뛰기   S = CSV 저장   R = 세션 초기화   M = 화면 표시 전환(요약 <-> confusion)
//
// 기록: 판정마다 CSV 한 행(Assets/Spike/BenchmarkResults/). 플레이 종료 후 재분석 가능해야 하므로 파일로 남긴다.
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Oheangbu.Drawing;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class S4_RecognitionBenchmark : MonoBehaviour
{
    [SerializeField] private DrawingInputController _controller;

    [Header("표본 세트 (§4 — 초성 5 × 중성 4를 고루 덮고 받침 유/무를 섞는다)")]
    [Tooltip("제시할 글자들. 기본값 15자: 무받침 10(초성 5 × 중성 ㅏㅓㅗㅜ 순환) + 받침 5(3분할 표본)")]
    [SerializeField]
    private string[] _targetLetters =
    {
        "가", "너", "모", "수", "아",   // 무받침 — 초성 5 × 중성 4 순환
        "거", "노", "무", "사", "어",
        "각", "넌", "몸", "삿", "엉"    // 받침 — 3분할 표본(ㄱㄴㅁㅅㅇ 받침 각 1)
    };

    [Tooltip("글자당 목표 표본 수. 정확도 판정=20회 이상(§4) / 성능 회귀 확인만이면 5회로 충분 — " +
        "처리시간은 획 수가 같으면 분산이 좁아 소표본에서도 p95가 안정적이다")]
    [SerializeField, Min(1)] private int _samplesPerLetter = 5;

    [Tooltip("표본이 적은 글자를 우선 제시(무작위 대신 균등 수집)")]
    [SerializeField] private bool _balancedOrder = true;

    // 한 번의 판정 기록 — CSV 한 행이 된다
    private struct Sample
    {
        public string Expected;
        public string Actual;        // 실패면 "(불발)"
        public bool Correct;
        public int SplitGroups;      // 2 / 3 / 0
        public int ExpectedGroups;   // 기대 분할(받침 유무) — 2/3
        public int StrokeCount;
        public double ElapsedMs;
        public float WorstDistance;
        public float AverageDistance;
        public string ExpectedInitial, ExpectedMedial, ExpectedFinal;
        public string ActualInitial, ActualMedial, ActualFinal;
    }

    private readonly List<Sample> _samples = new List<Sample>();
    private readonly Dictionary<string, int> _attempts = new Dictionary<string, int>();
    private int _targetIndex;
    private bool _showConfusion;
    private string _lastLine = "아직 판정 없음";
    private string _savedPath;

    private string CurrentTarget => _targetLetters.Length > 0 ? _targetLetters[_targetIndex] : "";

    private void OnEnable()
    {
        if (_controller != null) _controller.CommitDiagnosed += OnCommitDiagnosed;
        foreach (var letter in _targetLetters)
        {
            if (!_attempts.ContainsKey(letter)) _attempts[letter] = 0;
        }
        PickNextTarget();
    }

    private void OnDisable()
    {
        if (_controller != null) _controller.CommitDiagnosed -= OnCommitDiagnosed;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.nKey.wasPressedThisFrame) PickNextTarget();
        if (keyboard.sKey.wasPressedThisFrame) SaveCsv();
        if (keyboard.rKey.wasPressedThisFrame) ResetSession();
        if (keyboard.mKey.wasPressedThisFrame) _showConfusion = !_showConfusion;
    }

    private void OnCommitDiagnosed(DrawingInputController.CommitDiagnostics d)
    {
        string expected = CurrentTarget;
        if (string.IsNullOrEmpty(expected)) return;

        DecomposeExpected(expected, out string expIni, out string expMed, out string expFin);
        string actual = d.Success ? d.Letter.ToString() : "(불발)";

        var sample = new Sample
        {
            Expected = expected,
            Actual = actual,
            Correct = d.Success && actual == expected,
            SplitGroups = d.SplitGroupCount,
            ExpectedGroups = string.IsNullOrEmpty(expFin) ? 2 : 3,
            StrokeCount = d.StrokeCount,
            ElapsedMs = d.ElapsedMilliseconds,
            WorstDistance = d.WorstDistance,
            AverageDistance = d.AverageDistance,
            ExpectedInitial = expIni,
            ExpectedMedial = expMed,
            ExpectedFinal = expFin,
            ActualInitial = d.Success ? d.InitialName : "",
            ActualMedial = d.Success ? d.MedialName : "",
            ActualFinal = d.Success ? d.FinalName : "",
        };
        _samples.Add(sample);
        _attempts[expected] = _attempts.TryGetValue(expected, out int n) ? n + 1 : 1;

        _lastLine = $"기대 「{expected}」 → {(sample.Correct ? "적중" : "오답")} 「{actual}」 · " +
            $"{d.StrokeCount}획 · {d.SplitGroupCount}분할 · {d.ElapsedMilliseconds:F2}ms · 거리(최악 {d.WorstDistance:F2})";

        PickNextTarget();
    }

    // 유니코드 한글 분해 — 기대 글자의 자리별 기대값(자리별 confusion용)
    private static void DecomposeExpected(string letter, out string initial, out string medial, out string final)
    {
        string[] choTable = { "ㄱ", "ㄲ", "ㄴ", "ㄷ", "ㄸ", "ㄹ", "ㅁ", "ㅂ", "ㅃ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅉ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ" };
        string[] jungTable = { "ㅏ", "ㅐ", "ㅑ", "ㅒ", "ㅓ", "ㅔ", "ㅕ", "ㅖ", "ㅗ", "ㅘ", "ㅙ", "ㅚ", "ㅛ", "ㅜ", "ㅝ", "ㅞ", "ㅟ", "ㅠ", "ㅡ", "ㅢ", "ㅣ" };
        string[] jongTable = { "", "ㄱ", "ㄲ", "ㄳ", "ㄴ", "ㄵ", "ㄶ", "ㄷ", "ㄹ", "ㄺ", "ㄻ", "ㄼ", "ㄽ", "ㄾ", "ㄿ", "ㅀ", "ㅁ", "ㅂ", "ㅄ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ" };

        initial = medial = final = "";
        if (string.IsNullOrEmpty(letter)) return;
        int code = letter[0] - 0xAC00;
        if (code < 0 || code >= 11172) return;
        initial = choTable[code / (21 * 28)];
        medial = jungTable[code % (21 * 28) / 28];
        final = jongTable[code % 28];
    }

    // 표본이 가장 적은 글자를 우선 제시 — 균등 수집(§4 표본 요건 충족을 돕는다)
    private void PickNextTarget()
    {
        if (_targetLetters.Length == 0) return;
        if (!_balancedOrder)
        {
            _targetIndex = (_targetIndex + 1) % _targetLetters.Length;
            return;
        }
        int best = 0, bestCount = int.MaxValue;
        for (int i = 0; i < _targetLetters.Length; i++)
        {
            int idx = (_targetIndex + 1 + i) % _targetLetters.Length; // 같은 글자 연속 제시 회피
            int count = _attempts.TryGetValue(_targetLetters[idx], out int n) ? n : 0;
            if (count < bestCount) { bestCount = count; best = idx; }
        }
        _targetIndex = best;
    }

    private void ResetSession()
    {
        _samples.Clear();
        foreach (var letter in _targetLetters) _attempts[letter] = 0;
        _lastLine = "세션 초기화됨";
        _savedPath = null;
    }

    private void SaveCsv()
    {
        if (_samples.Count == 0) { _lastLine = "저장할 표본 없음"; return; }
        string dir = Path.Combine(Application.dataPath, "Spike", "BenchmarkResults");
        Directory.CreateDirectory(dir);
        // 파일명에 시각을 넣어 세션마다 분리 보관
        string file = Path.Combine(dir, $"recognition_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");

        var sb = new StringBuilder();
        sb.AppendLine("expected,actual,correct,expected_groups,split_groups,strokes,elapsed_ms,worst_distance,avg_distance," +
            "exp_initial,exp_medial,exp_final,act_initial,act_medial,act_final");
        foreach (var s in _samples)
        {
            sb.AppendLine(string.Join(",",
                s.Expected, s.Actual, s.Correct ? "1" : "0",
                s.ExpectedGroups.ToString(), s.SplitGroups.ToString(), s.StrokeCount.ToString(),
                s.ElapsedMs.ToString("F3", CultureInfo.InvariantCulture),
                s.WorstDistance.ToString("F4", CultureInfo.InvariantCulture),
                s.AverageDistance.ToString("F4", CultureInfo.InvariantCulture),
                s.ExpectedInitial, s.ExpectedMedial, s.ExpectedFinal,
                s.ActualInitial, s.ActualMedial, s.ActualFinal));
        }
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true)); // BOM — 엑셀에서 한글 깨짐 방지
        _savedPath = file;
        _lastLine = $"CSV 저장: {Path.GetFileName(file)} ({_samples.Count}행)";
        Debug.Log($"[S4] {_lastLine}\n{file}");
    }

    // ---- 화면 집계 (세부 분석은 CSV로) ----

    private void OnGUI()
    {
        string progress = _attempts.TryGetValue(CurrentTarget, out int cur) ? $"({cur}/{_samplesPerLetter})" : "";
        GUI.Label(new Rect(10, 10, 1400, 26),
            $"[S4 인식 벤치마크] 이번에 그릴 글자: 「{CurrentTarget}」  {progress}" +
            "   |  Q 홀드=작도 · N=건너뛰기 · S=CSV 저장 · R=초기화 · M=표시 전환");
        GUI.Label(new Rect(10, 34, 1400, 24), _lastLine);

        if (_samples.Count == 0) return;

        if (_showConfusion) DrawConfusion();
        else DrawSummary();
    }

    private void DrawSummary()
    {
        int total = _samples.Count, correct = 0, misfire = 0;
        int g2Total = 0, g2Correct = 0, g3Total = 0, g3Correct = 0;
        var perLetter = new Dictionary<string, (int total, int correct)>();
        var timesByStroke = new Dictionary<int, List<double>>();

        foreach (var s in _samples)
        {
            if (s.Correct) correct++;
            if (s.SplitGroups == 0) misfire++;
            if (s.ExpectedGroups == 2) { g2Total++; if (s.Correct) g2Correct++; }
            else { g3Total++; if (s.Correct) g3Correct++; }

            var cur = perLetter.TryGetValue(s.Expected, out var v) ? v : (0, 0);
            perLetter[s.Expected] = (cur.Item1 + 1, cur.Item2 + (s.Correct ? 1 : 0));

            if (!timesByStroke.TryGetValue(s.StrokeCount, out var list))
            {
                list = new List<double>();
                timesByStroke[s.StrokeCount] = list;
            }
            list.Add(s.ElapsedMs);
        }

        int y = 64;
        GUI.Label(new Rect(10, y, 1400, 24),
            $"■ 완성 글자 정확도 {Pct(correct, total)} ({correct}/{total})  ·  불발 {misfire}  " +
            $"|  2분할(무받침) {Pct(g2Correct, g2Total)} ({g2Correct}/{g2Total})  ·  3분할(받침) {Pct(g3Correct, g3Total)} ({g3Correct}/{g3Total})");
        y += 24;

        // 판정 기준(§6) 대비 현황 — 측정 전 선언된 기준선을 화면에 함께 띄운다
        string verdict = total < 20 ? "표본 부족" : Ratio(correct, total) >= 0.90f ? "유지 기준 충족"
            : Ratio(correct, total) >= 0.80f ? "조건부 구간(80~90%)" : "대안 탐색 구간(<80%)";
        GUI.Label(new Rect(10, y, 1400, 24), $"■ 기준(§6): 정확도 ≥90% 유지 / 80~90% 조건부 / <80% 대안  →  현재: {verdict}");
        y += 28;

        var strokeKeys = new List<int>(timesByStroke.Keys);
        strokeKeys.Sort();
        var timeParts = new List<string>();
        foreach (int k in strokeKeys)
        {
            var list = timesByStroke[k];
            list.Sort();
            timeParts.Add($"{k}획 p50 {Percentile(list, 0.5):F2} / p95 {Percentile(list, 0.95):F2}ms (n={list.Count})");
        }
        GUI.Label(new Rect(10, y, 1400, 24), "■ 처리시간(획 수별): " + string.Join("  |  ", timeParts));
        y += 28;

        var letterParts = new List<string>();
        foreach (var letter in _targetLetters)
        {
            if (!perLetter.TryGetValue(letter, out var v)) continue;
            letterParts.Add($"{letter} {v.correct}/{v.total}");
        }
        GUI.Label(new Rect(10, y, 1400, 24), "■ 글자별: " + string.Join("  ", letterParts));
        y += 26;
        if (_savedPath != null) GUI.Label(new Rect(10, y, 1400, 24), $"저장 위치: {_savedPath}");
    }

    // 자리별 혼동 표 — 무엇을 무엇으로 틀렸는가(§3-2). 오답만 모아 보여준다.
    private void DrawConfusion()
    {
        var pairs = new Dictionary<string, int>();
        foreach (var s in _samples)
        {
            if (s.Correct) continue;
            Accumulate(pairs, "초성", s.ExpectedInitial, s.ActualInitial);
            Accumulate(pairs, "중성", s.ExpectedMedial, s.ActualMedial);
            Accumulate(pairs, "종성", s.ExpectedFinal, s.ActualFinal);
            Accumulate(pairs, "글자", s.Expected, s.Actual);
        }

        int y = 64;
        GUI.Label(new Rect(10, y, 1400, 24), "■ 혼동 쌍 (오답만 · 기대→실제 · 빈도순) — 전체 행렬은 CSV로 분석");
        y += 26;
        var keys = new List<string>(pairs.Keys);
        keys.Sort((a, b) => pairs[b].CompareTo(pairs[a]));
        for (int i = 0; i < keys.Count && i < 16; i++)
        {
            GUI.Label(new Rect(20, y, 1400, 22), $"{keys[i]} × {pairs[keys[i]]}");
            y += 22;
        }
        if (keys.Count == 0) GUI.Label(new Rect(20, y, 1400, 22), "오답 없음");
    }

    private static void Accumulate(Dictionary<string, int> map, string slot, string expected, string actual)
    {
        if (expected == actual) return; // 이 자리는 맞았다
        string key = $"{slot}: {(string.IsNullOrEmpty(expected) ? "(없음)" : expected)} → {(string.IsNullOrEmpty(actual) ? "(없음)" : actual)}";
        map[key] = map.TryGetValue(key, out int n) ? n + 1 : 1;
    }

    private static float Ratio(int part, int total) => total > 0 ? (float)part / total : 0f;

    private static string Pct(int part, int total) => total > 0 ? $"{100f * part / total:F1}%" : "-";

    // 정렬된 리스트에서의 백분위 — 표본이 적을 때도 안전하게 인덱스를 자른다
    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0.0;
        int index = Mathf.Clamp(Mathf.CeilToInt((float)(p * sorted.Count)) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }
}
