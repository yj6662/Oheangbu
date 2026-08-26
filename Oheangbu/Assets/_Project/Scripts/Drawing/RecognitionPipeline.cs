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

            public Result(bool success, string initialName, string medialName, string finalName,
                float worstDistance, float averageDistance)
            {
                Success = success;
                InitialName = initialName;
                MedialName = medialName;
                FinalName = finalName;
                WorstDistance = worstDistance;
                AverageDistance = averageDistance;
            }

            public static Result Fail() => new Result(false, null, null, "", float.MaxValue, float.MaxValue);
        }

        private const int MinPointsPerGroup = 4;          // $P가 형태를 말할 수 있는 최소 점 수(레거시 검증값)
        private const float MedialMinorAxisRatio = 0.22f; // 중성 구조 검증 — 보조축/주축 비 임계(퇴화 직선 배제)
        private const float FinalBelowRatio = 0.1f;       // 종성 공간 게이트 — 글자 높이 대비 「아래」 판정 여유
        private const float MedialAxisDominance = 1.15f;  // 중성 기하 판별 — 세로형/가로형 판정의 종횡 우세 비

        private Gesture[] _initials = new Gesture[0];
        private Gesture[] _medials = new Gesture[0];
        private Gesture[] _finals = new Gesture[0];

        public bool IsReady => _initials.Length > 0 && _medials.Length > 0;

        public void Initialize(JamoTemplateLibrarySO library)
        {
            _initials = library.BuildInitialGestures();
            _medials = library.BuildMedialGestures();
            _finals = library.BuildFinalGestures();
        }

        // strokes: 커밋된 글자의 획 목록(Raw — 화면 좌표·시각). screenHeight: 필기 좌표계 Y 뒤집기용.
        public Result Recognize(IReadOnlyList<StrokeData> strokes, float screenHeight)
        {
            int strokeCount = strokes.Count;
            // 합성 글자는 최소 초성+중성 — 한 획으로는 글자가 성립하지 않는다(불발)
            if (!IsReady || strokeCount < 2) return Result.Fail();

            var all = ToPDollarPoints(strokes, screenHeight);

            // --- 2분할 (초성|중성) ---
            float bestAvg2 = float.MaxValue, worst2 = 0f;
            string ini2 = null, med2 = null;
            Point[] med2Pts = null;
            for (int split = 1; split < strokeCount; split++)
            {
                var iniPts = CollectPoints(all, 0, split);
                var medPts = CollectPoints(all, split, strokeCount);
                if (iniPts.Length < MinPointsPerGroup || medPts.Length < MinPointsPerGroup) continue;
                // 순수 직선 하나뿐인 중성 후보는 잘못된 분할 — $P는 퇴화 직선에 부당하게 좋은 거리를 주므로 구조로 거른다
                if (!IsPlausibleMedialShape(medPts)) continue;

                var mi = JamoMatcher.Classify(iniPts, _initials);
                var mm = JamoMatcher.Classify(medPts, _medials);
                float avg = (mi.Distance + mm.Distance) * 0.5f;
                if (avg < bestAvg2)
                {
                    bestAvg2 = avg;
                    ini2 = mi.Name;
                    med2 = mm.Name;
                    worst2 = Mathf.Max(mi.Distance, mm.Distance);
                    med2Pts = medPts;
                }
            }

            // --- 3분할 (초성|중성|종성) — 그룹 수가 달라 거리 「합」은 불공평하므로 「그룹당 평균」으로 겨룬다 ---
            float bestAvg3 = float.MaxValue, worst3 = 0f;
            string ini3 = null, med3 = null, fin3 = null;
            Point[] med3Pts = null;
            if (strokeCount >= 3 && _finals.Length > 0)
            {
                for (int s1 = 1; s1 < strokeCount - 1; s1++)
                for (int s2 = s1 + 1; s2 < strokeCount; s2++)
                {
                    var iniPts = CollectPoints(all, 0, s1);
                    var medPts = CollectPoints(all, s1, s2);
                    var finPts = CollectPoints(all, s2, strokeCount);
                    if (iniPts.Length < MinPointsPerGroup || medPts.Length < MinPointsPerGroup
                        || finPts.Length < MinPointsPerGroup) continue;
                    if (!IsPlausibleMedialShape(medPts)) continue;
                    // 공간 게이트: 받침은 반드시 초성·중성보다 아래 — 없으면 무받침 글자의 획 일부를 받침으로 오인한다
                    if (!IsFinalBelow(iniPts, medPts, finPts)) continue;

                    var mi = JamoMatcher.Classify(iniPts, _initials);
                    var mm = JamoMatcher.Classify(medPts, _medials);
                    var mf = JamoMatcher.Classify(finPts, _finals);
                    float avg = (mi.Distance + mm.Distance + mf.Distance) / 3f;
                    if (avg < bestAvg3)
                    {
                        bestAvg3 = avg;
                        ini3 = mi.Name;
                        med3 = mm.Name;
                        fin3 = mf.Name;
                        worst3 = Mathf.Max(mi.Distance, Mathf.Max(mm.Distance, mf.Distance));
                        med3Pts = medPts;
                    }
                }
            }

            // --- 선택: 평균 거리가 더 좋은 쪽. 유효한 분할이 없으면 실패(불발 — 폴백 없음) ---
            if (med3Pts != null && (med2Pts == null || bestAvg3 < bestAvg2))
            {
                string medial = ClassifyMedialByGeometry(med3Pts, med3);
                return new Result(true, ini3, medial, fin3, worst3, bestAvg3);
            }
            if (med2Pts != null)
            {
                string medial = ClassifyMedialByGeometry(med2Pts, med2);
                return new Result(true, ini2, medial, "", worst2, bestAvg2);
            }
            return Result.Fail();
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
        private static bool IsPlausibleMedialShape(Point[] rawPoints)
        {
            var pts = new Gesture(rawPoints).Points; // $P 정규화(리샘플·무게중심 원점)를 거친 형태로 판정
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
        private static string ClassifyMedialByGeometry(Point[] rawPoints, string fallback)
        {
            var pts = new Gesture(rawPoints).Points;
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
