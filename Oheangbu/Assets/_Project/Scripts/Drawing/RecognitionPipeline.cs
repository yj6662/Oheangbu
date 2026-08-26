using System.Collections.Generic;
using Oheangbu.Core.Domain;
using PDollarGestureRecognizer;
using UnityEngine;

namespace Oheangbu.Drawing
{
    // 합성 글자(초성+중성[+종성]) 인식 — 획 그룹 분할 파이프라인.
    // 레거시 DrawingInputController의 실측 검증 알고리즘을 충실 포팅했다(SPEC-DRAWING-INPUT §8):
    //   2분할/3분할을 그룹당 평균 거리로 경쟁시키고, 퇴화 직선 중성 배제·종성 공간 게이트·
    //   거울상 중성 기하 판별로 $P의 약점을 구조 검증이 메운다.
    // 스펙 차이: 약발동·ㅏ 폴백 없음 — 유효한 분할이 없으면 실패(불발, §3-3).
    // 기하 임계 상수들은 레거시 실측을 통과한 값 그대로다 — 변경은 인식률 회귀 측정과 함께만.
    public sealed class RecognitionPipeline
    {
        public readonly struct Result
        {
            public readonly bool Success;
            public readonly string InitialName;
            public readonly string MedialName;
            public readonly string FinalName;        // 무받침이면 빈 문자열
            public readonly float WorstDistance;     // 가장 서툰 자모의 거리(형 원자료)
            public readonly float AverageDistance;   // 채택 분할의 평균 거리(구조 원자료)

            // 계측 노출(SPEC-SPIKE-SPELL-RECOGNITION §5) — 판정에는 쓰이지 않는 관측값.
            // 2분할/3분할은 탐색 공간이 달라 인식률을 분리 집계해야 하고, 3분할은 O(n²)이라
            // 처리시간을 획 수와 함께 봐야 의미가 있다.
            public readonly int SplitGroupCount;     // 2=초성+중성 / 3=초성+중성+종성 / 0=실패

            public Result(bool success, string initialName, string medialName, string finalName,
                float worstDistance, float averageDistance, int splitGroupCount)
            {
                Success = success;
                InitialName = initialName;
                MedialName = medialName;
                FinalName = finalName;
                WorstDistance = worstDistance;
                AverageDistance = averageDistance;
                SplitGroupCount = splitGroupCount;
            }

            public static Result Fail() => new Result(false, null, null, "", float.MaxValue, float.MaxValue, 0);
        }

        // 마지막 Recognize 호출의 소요시간(ms) — 계측 전용. 인식 결과에 영향 없음.
        public double LastElapsedMilliseconds { get; private set; }

        private const int MinPointsPerGroup = 4;          // $P가 형태를 말할 수 있는 최소 점 수(레거시 검증값)
        private const float MedialMinorAxisRatio = 0.22f; // 중성 구조 검증 — 보조축/주축 비 임계(퇴화 직선 배제)
        private const float FinalBelowRatio = 0.1f;       // 종성 공간 게이트 — 글자 높이 대비 「아래」 판정 여유
        private const float MedialAxisDominance = 1.15f;  // 중성 기하 판별 — 세로형/가로형 판정의 종횡 우세 비

        private Gesture[] _initials = new Gesture[0];
        private Gesture[] _medials = new Gesture[0];
        private Gesture[] _finals = new Gesture[0];

        // ---- 획 수 구조 게이트(§17-4) ----
        // 템플릿에서 「이 자리가 몇 획으로 그려질 수 있는가」를 자동 추출한다.
        // 하드코딩하지 않는 이유: 새 필기 템플릿을 추가하면 허용 획 수가 저절로 넓어져야
        // 필기 다양성이 죽지 않는다(템플릿이 곧 정본).
        private bool[] _initialStrokeOk = new bool[0];
        private bool[] _medialStrokeOk = new bool[0];
        private bool[] _finalStrokeOk = new bool[0];
        private const int MaxTrackedStrokes = 16; // 이 이상은 게이트를 적용하지 않는다(안전판)

        // ---- 계산 재사용 캐시(SPEC-SPIKE-SPELL-RECOGNITION §12 후속과제 1) ----
        // 같은 획 그룹이 여러 분할 후보에서 반복 등장한다(8획 기준 중복 47%).
        // 인식 「결과」는 바꾸지 않고 계산만 재사용한다 — 키는 (시작 획, 끝 획).
        private readonly Dictionary<int, Point[]> _groupPoints = new Dictionary<int, Point[]>();
        private readonly Dictionary<int, Gesture> _groupGesture = new Dictionary<int, Gesture>();
        private readonly Dictionary<int, bool> _medialShapeOk = new Dictionary<int, bool>();
        private readonly Dictionary<int, float> _groupMeanY = new Dictionary<int, float>();
        private readonly Dictionary<long, JamoMatcher.Match> _matchCache = new Dictionary<long, JamoMatcher.Match>();

        private static int GroupKey(int from, int to) => from * 64 + to;

        private enum TemplateSet { Initial = 0, Medial = 1, Final = 2 }

        private Point[] _allPoints = new Point[0];

        public bool IsReady => _initials.Length > 0 && _medials.Length > 0;

        public void Initialize(JamoTemplateLibrarySO library)
        {
            _initials = library.BuildInitialGestures();
            _medials = library.BuildMedialGestures();
            _finals = library.BuildFinalGestures();
            _initialStrokeOk = BuildStrokeCountTable(_initials);
            _medialStrokeOk = BuildStrokeCountTable(_medials);
            _finalStrokeOk = BuildStrokeCountTable(_finals);
        }

        // strokes: 커밋된 글자의 획 목록(Raw — 화면 좌표·시각). screenHeight: 필기 좌표계 Y 뒤집기용.
        public Result Recognize(IReadOnlyList<StrokeData> strokes, float screenHeight)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = RecognizeInternal(strokes, screenHeight);
            stopwatch.Stop();
            LastElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            return result;
        }

        private Result RecognizeInternal(IReadOnlyList<StrokeData> strokes, float screenHeight)
        {
            int strokeCount = strokes.Count;
            // 합성 글자는 최소 초성+중성 — 한 획으로는 글자가 성립하지 않는다(불발)
            if (!IsReady || strokeCount < 2) return Result.Fail();

            var all = ToPDollarPoints(strokes, screenHeight);
            ResetCaches();
            _allPoints = all;

            // --- 2분할 (초성|중성) ---
            float bestAvg2 = float.MaxValue, worst2 = 0f;
            string ini2 = null, med2 = null;
            Point[] med2Pts = null;
            int med2From = -1, med2To = -1;
            for (int split = 1; split < strokeCount; split++)
            {
                // 획 수 게이트(§17-4) — $P를 돌리기 전에 구조적으로 불가능한 분할을 배제한다.
                // 예: 중성 템플릿이 전부 2획이면 「중성 3획」 후보는 볼 필요가 없다.
                if (!StrokeCountAllowed(_initialStrokeOk, split)
                    || !StrokeCountAllowed(_medialStrokeOk, strokeCount - split)) continue;

                var iniPts = GetGroupPoints(0, split);
                var medPts = GetGroupPoints(split, strokeCount);
                if (iniPts.Length < MinPointsPerGroup || medPts.Length < MinPointsPerGroup) continue;
                // 순수 직선 하나뿐인 중성 후보는 잘못된 분할 — $P는 퇴화 직선에 부당하게 좋은 거리를 주므로 구조로 거른다
                // (게이트를 매칭 「전에」 두는 순서는 원본과 동일 — 걸러진 후보는 애초에 매칭하지 않는다)
                if (!IsPlausibleMedialShapeCached(split, strokeCount)) continue;

                var mi = MatchCached(0, split, TemplateSet.Initial);
                // Branch & Bound(§17-5): 초성 거리만으로 이미 현재 최선을 넘길 수 없으면
                // 중성 매칭을 생략한다. 남은 자리 거리가 0이어도 못 이기는 경우이므로 결과 불변.
                if (mi.Distance >= bestAvg2 * 2f) continue;

                var mm = MatchCached(split, strokeCount, TemplateSet.Medial);
                float avg = (mi.Distance + mm.Distance) * 0.5f;
                if (avg < bestAvg2)
                {
                    bestAvg2 = avg;
                    ini2 = mi.Name;
                    med2 = mm.Name;
                    worst2 = Mathf.Max(mi.Distance, mm.Distance);
                    med2Pts = medPts;
                    med2From = split; med2To = strokeCount;
                }
            }

            // --- 3분할 (초성|중성|종성) — 그룹 수가 달라 거리 「합」은 불공평하므로 「그룹당 평균」으로 겨룬다 ---
            float bestAvg3 = float.MaxValue, worst3 = 0f;
            string ini3 = null, med3 = null, fin3 = null;
            Point[] med3Pts = null;
            int med3From = -1, med3To = -1;
            if (strokeCount >= 3 && _finals.Length > 0)
            {
                for (int s1 = 1; s1 < strokeCount - 1; s1++)
                for (int s2 = s1 + 1; s2 < strokeCount; s2++)
                {
                    // 획 수 게이트 — 세 자리 전부 템플릿이 아는 획 수여야 한다
                    if (!StrokeCountAllowed(_initialStrokeOk, s1)
                        || !StrokeCountAllowed(_medialStrokeOk, s2 - s1)
                        || !StrokeCountAllowed(_finalStrokeOk, strokeCount - s2)) continue;

                    var iniPts = GetGroupPoints(0, s1);
                    var medPts = GetGroupPoints(s1, s2);
                    var finPts = GetGroupPoints(s2, strokeCount);
                    if (iniPts.Length < MinPointsPerGroup || medPts.Length < MinPointsPerGroup
                        || finPts.Length < MinPointsPerGroup) continue;
                    if (!IsPlausibleMedialShapeCached(s1, s2)) continue;
                    // 공간 게이트: 받침은 반드시 초성·중성보다 아래 — 없으면 무받침 글자의 획 일부를 받침으로 오인한다
                    if (!IsFinalBelowCached(0, s1, s1, s2, s2, strokeCount)) continue;

                    var mi = MatchCached(0, s1, TemplateSet.Initial);
                    // B&B 1차: 초성만으로 한계 초과면 나머지 두 자리 매칭 생략
                    if (mi.Distance >= bestAvg3 * 3f) continue;

                    var mm = MatchCached(s1, s2, TemplateSet.Medial);
                    // B&B 2차: 초성+중성 합이 이미 한계 초과면 종성 매칭 생략
                    if (mi.Distance + mm.Distance >= bestAvg3 * 3f) continue;

                    var mf = MatchCached(s2, strokeCount, TemplateSet.Final);
                    float avg = (mi.Distance + mm.Distance + mf.Distance) / 3f;
                    if (avg < bestAvg3)
                    {
                        bestAvg3 = avg;
                        ini3 = mi.Name;
                        med3 = mm.Name;
                        fin3 = mf.Name;
                        worst3 = Mathf.Max(mi.Distance, Mathf.Max(mm.Distance, mf.Distance));
                        med3Pts = medPts;
                        med3From = s1; med3To = s2;
                    }
                }
            }

            // --- 선택: 평균 거리가 더 좋은 쪽. 유효한 분할이 없으면 실패(불발 — 폴백 없음) ---
            if (med3Pts != null && (med2Pts == null || bestAvg3 < bestAvg2))
            {
                string medial = ClassifyMedialByGeometry(GetGroupGesture(med3From, med3To), med3);
                return new Result(true, ini3, medial, fin3, worst3, bestAvg3, 3);
            }
            if (med2Pts != null)
            {
                string medial = ClassifyMedialByGeometry(GetGroupGesture(med2From, med2To), med2);
                return new Result(true, ini2, medial, "", worst2, bestAvg2, 2);
            }
            return Result.Fail();
        }

        // 템플릿 점군의 StrokeID 개수로 「가능한 획 수」 집합을 만든다.
        // 정규화(Resample)는 StrokeID를 보존하므로 원본 획 구조가 그대로 남는다.
        private static bool[] BuildStrokeCountTable(Gesture[] templates)
        {
            var table = new bool[MaxTrackedStrokes + 1];
            foreach (var t in templates)
            {
                int count = CountStrokes(t.Points);
                if (count >= 1 && count <= MaxTrackedStrokes) table[count] = true;
            }
            return table;
        }

        private static int CountStrokes(Point[] points)
        {
            if (points == null || points.Length == 0) return 0;
            int count = 1;
            for (int i = 1; i < points.Length; i++)
            {
                if (points[i].StrokeID != points[i - 1].StrokeID) count++;
            }
            return count;
        }

        // 이 자리를 이 획 수로 쓸 수 있는가. 표에 없는 큰 획 수는 「모름」으로 보고 통과시킨다
        // — 게이트는 확실히 불가능한 것만 걷어내야 인식 결과가 바뀌지 않는다.
        private static bool StrokeCountAllowed(bool[] table, int strokeCount)
        {
            if (table.Length == 0) return true;                       // 표 미구축 — 게이트 비활성
            if (strokeCount < 1 || strokeCount > MaxTrackedStrokes) return true;
            return table[strokeCount];
        }

        // ---- 캐시 헬퍼 ----
        // 전부 「같은 입력에 같은 답」을 돌려주는 순수 재사용이다. 판정 로직·비교 순서는 원본과 동일하며,
        // 캐시를 비활성화해도 결과가 같아야 한다(§5 비침습 원칙의 연장).

        private void ResetCaches()
        {
            _groupPoints.Clear();
            _groupGesture.Clear();
            _medialShapeOk.Clear();
            _groupMeanY.Clear();
            _matchCache.Clear();
        }

        // 획 그룹의 점 배열 — 같은 (from,to)가 여러 분할 후보에서 반복 요청된다
        private Point[] GetGroupPoints(int from, int to)
        {
            int key = GroupKey(from, to);
            if (_groupPoints.TryGetValue(key, out var cached)) return cached;
            var pts = CollectPoints(_allPoints, from, to);
            _groupPoints[key] = pts;
            return pts;
        }

        // $P 정규화(Scale→Translate→Resample 32점) 결과 — 가장 비싼 전처리라 그룹당 1회만 수행한다
        private Gesture GetGroupGesture(int from, int to)
        {
            int key = GroupKey(from, to);
            if (_groupGesture.TryGetValue(key, out var cached)) return cached;
            var gesture = new Gesture(GetGroupPoints(from, to));
            _groupGesture[key] = gesture;
            return gesture;
        }

        // (그룹 × 템플릿셋) 매칭 결과 — 중복 조합에서 같은 계산이 반복되는 것을 막는다
        private JamoMatcher.Match MatchCached(int from, int to, TemplateSet set)
        {
            long key = ((long)GroupKey(from, to) << 8) | (long)set;
            if (_matchCache.TryGetValue(key, out var cached)) return cached;
            var templates = set == TemplateSet.Initial ? _initials
                : set == TemplateSet.Medial ? _medials : _finals;
            var match = JamoMatcher.Classify(GetGroupGesture(from, to), templates); // 정규화 재사용
            _matchCache[key] = match;
            return match;
        }

        private bool IsPlausibleMedialShapeCached(int from, int to)
        {
            int key = GroupKey(from, to);
            if (_medialShapeOk.TryGetValue(key, out bool cached)) return cached;
            bool ok = IsPlausibleMedialShape(GetGroupGesture(from, to));
            _medialShapeOk[key] = ok;
            return ok;
        }

        private float GetGroupMeanY(int from, int to)
        {
            int key = GroupKey(from, to);
            if (_groupMeanY.TryGetValue(key, out float cached)) return cached;
            float mean = MeanY(GetGroupPoints(from, to));
            _groupMeanY[key] = mean;
            return mean;
        }

        // 종성 공간 게이트(캐시판) — 원본 IsFinalBelow와 같은 식이되, 그룹 평균 Y를 재사용한다.
        // 전체 높이는 세 그룹의 합집합 = 글자 전체이므로 전 점에서 한 번만 구하면 된다.
        private bool IsFinalBelowCached(int iniFrom, int iniTo, int medFrom, int medTo, int finFrom, int finTo)
        {
            float iniY = GetGroupMeanY(iniFrom, iniTo);
            float medY = GetGroupMeanY(medFrom, medTo);
            float finY = GetGroupMeanY(finFrom, finTo);
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in _allPoints)
            {
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
            float height = Mathf.Max(maxY - minY, 1f);
            return finY > Mathf.Max(iniY, medY) + height * FinalBelowRatio;
        }

        // Raw(화면 좌표: 아래가 y=0) → $P 필기 좌표계(아래로 증가) 변환 — S2·레거시와 같은 규약
        private static Point[] ToPDollarPoints(IReadOnlyList<StrokeData> strokes, float screenHeight)
        {
            var list = new List<Point>();
            for (int strokeId = 0; strokeId < strokes.Count; strokeId++)
            {
                foreach (var p in strokes[strokeId].Points)
                {
                    list.Add(new Point(p.Position.x, screenHeight - p.Position.y, strokeId));
                }
            }
            return list.ToArray();
        }

        private static Point[] CollectPoints(Point[] all, int strokeFrom, int strokeTo)
        {
            var list = new List<Point>();
            foreach (var p in all)
            {
                if (p.StrokeID >= strokeFrom && p.StrokeID < strokeTo) list.Add(p);
            }
            return list.ToArray();
        }

        // 종성 공간 게이트 — 받침 후보 그룹의 무게중심이 초성·중성 무게중심보다
        // 글자 전체 높이의 일정 비율 이상 아래에 있어야 한다(필기 좌표계 y는 아래로 증가).
        private static bool IsFinalBelow(Point[] ini, Point[] med, Point[] fin)
        {
            float iniY = MeanY(ini), medY = MeanY(med), finY = MeanY(fin);
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var arr in new[] { ini, med, fin })
            foreach (var p in arr)
            {
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
            float height = Mathf.Max(maxY - minY, 1f);
            return finY > Mathf.Max(iniY, medY) + height * FinalBelowRatio;
        }

        private static float MeanY(Point[] pts)
        {
            float sum = 0f;
            foreach (var p in pts) sum += p.Y;
            return sum / pts.Length;
        }

        // 중성 구조 검증 — 기본 중성(ㅏㅓㅗㅜ)은 「긴 획 + 직교 짧은 획」이라
        // 보조축 폭이 주축의 일정 비율 이상이어야 한다.
        private static bool IsPlausibleMedialShape(Gesture normalized)
        {
            var pts = normalized.Points; // $P 정규화(리샘플·무게중심 원점)를 거친 형태로 판정
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in pts)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
            float w = maxX - minX, h = maxY - minY;
            float major = Mathf.Max(w, h), minor = Mathf.Min(w, h);
            return minor > major * MedialMinorAxisRatio;
        }

        // 중성 기하 판별 — ㅏ/ㅓ, ㅗ/ㅜ는 거울상이라 $P 거리로는 변별이 약하다.
        // 정규화 점구름(무게중심=원점)에서 바운딩박스 중심의 부호로 점획의 방향을 가른다.
        private static string ClassifyMedialByGeometry(Gesture normalized, string fallback)
        {
            var pts = normalized.Points;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in pts)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
            float w = maxX - minX, h = maxY - minY;
            float centerX = (minX + maxX) * 0.5f, centerY = (minY + maxY) * 0.5f;

            if (h > w * MedialAxisDominance) return centerX > 0f ? "ㅏ" : "ㅓ"; // 세로형: 점이 오른쪽=ㅏ, 왼쪽=ㅓ
            if (w > h * MedialAxisDominance) return centerY < 0f ? "ㅗ" : "ㅜ"; // 가로형(y 아래+): 점이 위=ㅗ, 아래=ㅜ
            return fallback; // 종횡이 애매하면 $P 결과 유지
        }
    }
}
