using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    // #308 D308-77: where the townspeople of one city root walk and stand. Written by the editor command `Walkers308 routes:build`
    // from Tools/Unity/Stage308_walkers/walkers308.json (heights baked there - no ground ray at run time). Read-only at run time.
    // Lateral values are metres from the centreline, + = right of increasing distance along the route.
    public sealed class TownWalkerRoutes308 : ScriptableObject
    {
        [Serializable] public sealed class Route
        {
            public string Id = ""; public bool Loop; [Min(0)] public float HalfWidth;
            public Vector3[] Points = Array.Empty<Vector3>();
            [Tooltip("가로 높이 표: 점마다 CrossCount 개(중심선에서 CrossStep m 간격, 가운데 칸 = 중심선)의 '중심선 높이와의 차'(m). 가로 경사가 있는 길에서 차선 · 들를 자리의 발이 땅에 닿게 한다.")]
            public int CrossCount; public float CrossStep; public float[] Cross = Array.Empty<float>();
            [Tooltip("점마다 쓸 수 있는 반폭(m): 다리 · 좌판 · 담 앞에서 HalfWidth 보다 좁아진다. 비어 있으면 HalfWidth.")]
            public float[] Widths = Array.Empty<float>();
            [Tooltip("점마다 '오른쪽'(XZ 단위 벡터, 앞뒤 점으로 잰 것). 가로 높이 표가 이 방향으로 구워졌다 - 굽은 곳에서 차선이 꺾이지 않고 돈다.")]
            public Vector2[] Rights = Array.Empty<Vector2>();

            // the sideways direction at (segment, t): the baked per-point one where there is a table, else the segment's own
            public Vector2 RightAt(int segment, float t, Vector2 tangent) => segment >= 0 && segment + 1 < Rights.Length ? Vector2.LerpUnclamped(Rights[segment], Rights[segment + 1], t) : new Vector2(tangent.y, -tangent.x);

            // usable half width at (segment, t along it)
            public float HalfAt(int segment, float t) => segment >= 0 && segment + 1 < Widths.Length ? Mathf.Lerp(Widths[segment], Widths[segment + 1], t) : HalfWidth;

            // ground height at (segment, t along it, lateral) minus the centreline height there; 0 when no table was baked
            public float Rise(int segment, float t, float lateral)
            {
                if (CrossCount < 2 || CrossStep <= 0 || segment < 0 || (segment + 2) * CrossCount > Cross.Length) return 0;
                float u = Mathf.Clamp(lateral / CrossStep + (CrossCount - 1) * .5f, 0, CrossCount - 1); int k = Mathf.Min((int)u, CrossCount - 2); float f = u - k;
                int a = segment * CrossCount + k, b = a + CrossCount;
                return Mathf.Lerp(Mathf.Lerp(Cross[a], Cross[a + 1], f), Mathf.Lerp(Cross[b], Cross[b + 1], f), t);
            }
        }
        [Serializable] public sealed class Stop
        {
            public int Route; public float Distance, Lateral, Yaw;
        }
        [Serializable] public sealed class Walker
        {
            public string Role = ""; public int Route; [Range(0, 1)] public float Start; public int Direction = 1; [Min(0)] public float Lane;
            public int Body, Tint; [Range(0, 1)] public float Speed; public int Seed;
        }
        [Serializable] public sealed class Post
        {
            public string Role = ""; public Vector3 Position; public float Yaw; public int Body, Tint, Job, Seed;
        }
        public string Source = "";
        [Tooltip("몸 번호 -> 이름(walkers308.json body). TownWalkers308.BodyPrefabs 와 같은 순서.")]
        public string[] Bodies = Array.Empty<string>();
        public Route[] Routes = Array.Empty<Route>();
        [Tooltip("길 번호, 그 안에서는 거리 순으로 정렬돼 있다.")]
        public Stop[] Stops = Array.Empty<Stop>();
        public Walker[] Walkers = Array.Empty<Walker>();
        public Post[] Posts = Array.Empty<Post>();
        [Tooltip("모든 길 · 자리를 싼 상자(XZ). 플레이어가 이 상자 + HideDistance 밖이면 아무것도 굴리지 않는다.")]
        public Vector2 BoundsMin, BoundsMax;
    }
}
