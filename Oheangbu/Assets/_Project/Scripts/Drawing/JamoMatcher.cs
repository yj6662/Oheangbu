using PDollarGestureRecognizer;

namespace Oheangbu.Drawing
{
    // $P 매칭을 원시 거리값으로 수행하는 매처.
    // PointCloudRecognizer.Classify의 Score는 거리 2 이상이 전부 0으로 포화돼
    // 「획 그룹 분할」끼리 비교할 변별력이 사라진다 — 그래서 거리 자체를 돌려주는 포트를 쓴다.
    // 알고리즘은 PDollar의 GreedyCloudMatch와 동일(원본 무수정 유지).
    public static class JamoMatcher
    {
        public struct Match
        {
            public string Name;
            public float Distance; // 작을수록 유사
        }

        public static Match Classify(Point[] rawPoints, Gesture[] templates)
        {
            var candidate = new Gesture(rawPoints);
            var best = new Match { Name = null, Distance = float.MaxValue };
            foreach (var template in templates)
            {
                float d = GreedyCloudMatch(candidate.Points, template.Points);
                if (d < best.Distance) { best.Distance = d; best.Name = template.Name; }
            }
            return best;
        }

        // ---- 이하 $P GreedyCloudMatch 포트 ----

        private static float GreedyCloudMatch(Point[] points1, Point[] points2)
        {
            int n = points1.Length;
            const float eps = 0.5f;
            int step = (int)System.Math.Floor(System.Math.Pow(n, 1.0f - eps));
            if (step < 1) step = 1;
            float minDistance = float.MaxValue;
            for (int i = 0; i < n; i += step)
            {
                float d1 = CloudDistance(points1, points2, i);
                float d2 = CloudDistance(points2, points1, i);
                minDistance = System.Math.Min(minDistance, System.Math.Min(d1, d2));
            }
            return minDistance;
        }

        private static float CloudDistance(Point[] points1, Point[] points2, int startIndex)
        {
            int n = points1.Length;
            var matched = new bool[n];
            float sum = 0f;
            int i = startIndex;
            do
            {
                int index = -1;
                float min = float.MaxValue;
                for (int j = 0; j < n; j++)
                {
                    if (matched[j]) continue;
                    float dist = Geometry.SqrEuclideanDistance(points1[i], points2[j]);
                    if (dist < min) { min = dist; index = j; }
                }
                matched[index] = true;
                float weight = 1.0f - ((i - startIndex + n) % n) / (1.0f * n);
                sum += weight * (float)System.Math.Sqrt(min);
                i = (i + 1) % n;
            } while (i != startIndex);
            return sum;
        }
    }
}
