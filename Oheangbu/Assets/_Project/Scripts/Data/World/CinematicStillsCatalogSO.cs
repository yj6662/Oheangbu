using System;
using Oheangbu.Core.Events;
using UnityEngine;

namespace Oheangbu.Data.World
{
    public enum CinematicTriggerKind { None, PlaneCrossing, LoadingNewJourney, EncounterDetected, EncounterDefeated }
    public enum CinematicFreeze { Pause, AlreadyFrozen }
    public enum CinematicEase { Linear, InOutSine }
    public enum CinematicSkipKey { F, Space, Enter, Escape, MouseLeft, PadSouth, PadEast, PadStart }

    // #308 그림 시네마틱 [TEST, SPEC-CINEMATIC-STILLS-308, D308-26 / D308-28]: 글 없는 정지 그림 2 – 4장, 5 – 10초, 건너뛸 수 있음.
    // 이 자료에는 화면 글이 되는 칸이 하나도 없다(제목 · 자막 칸 없음). 그림은 직접 들지 않고 Resources 경로로만 든다
    // (SO 가 그림을 들면 전부 한꺼번에 올라온다). SO .asset 은 git 이 추적하지 않으므로 표의 원천은 ApplyDefaults308() 이다
    // (선례 MineTutorialProfileSO.ApplyDefaults) — 저작 도구가 이것을 불러 자산을 만든다. 런타임은 자산만 읽는다: 자산이 없으면 아무 일도 없다.
    [CreateAssetMenu(menuName = "Oheangbu/World/Cinematic Stills Catalog 308")]
    public sealed class CinematicStillsCatalogSO : ScriptableObject
    {
        public const string ResourcePath = "Cinematic308/CinematicStills308";
        public const string EnrolledId = "cinematic.enrolled";
        public const string HarnessArgument = "--cinematic-test";
        public static string SeenId(string sequenceId) => "cinematic." + sequenceId + ".seen";

        [Serializable]
        public sealed class Tokens
        {
            [Tooltip("암막(150)과 로딩(32000) 사이")] public int SortingOrder = 200;
            [Tooltip("이 시간 안의 건너뛰기 입력은 버린다(초)")] public float GuardSeconds = .40f;
            public float FadeInSeconds = .35f;
            public float FadeOutSeconds = .50f;
            public float SkipFadeSeconds = .20f;
            [Tooltip("움직임 줄이기: 모든 들고 나감 · 겹침이 이 길이")] public float ReducedMotionFadeSeconds = .12f;
            [Tooltip("요청 뒤 그림이 이 시간 안에 안 올라오면 버린다(본 것으로 적지 않는다)")] public float LoadTimeoutSeconds = .25f;
            [Tooltip("요청 뒤 Started 가 이 시간 안에 안 오면 판정기가 버린다")] public float RequestTimeoutSeconds = 1.0f;
            [Tooltip("한 프레임에 시계가 나아가는 상한(끊김 뒤 장을 건너뛰지 않게)")] public float MaxFrameSeconds = .10f;
            public CinematicSkipKey[] SkipKeys = Array.Empty<CinematicSkipKey>();
            [Tooltip("도구가 준 원본 크기")] public int SourceWidth = 1672, SourceHeight = 941;
            [Tooltip("바탕 = UI 토큰 한지 #E6E2D7")] public Color Paper = new Color(.902f, .886f, .843f, 1f);
        }

        [Serializable]
        public sealed class StretchLimit { public int ScreenWidth; public float MaxStretch; }

        // 검사 기준(AC-CS.1 · .3 · .4 · .5 · .7 · .20, 8절 예산). 코드에 수치를 두지 않으려고 자료에 둔다.
        [Serializable]
        public sealed class Limits
        {
            public int MinStills = 2, MaxStills = 4;
            public float MinTotalSeconds = 5f, MaxTotalSeconds = 10f, MinStillSeconds = 2f;
            public float MinCrossFadeSeconds = .30f, MaxCrossFadeSeconds = .50f;
            public float MinGuardSeconds = .30f, MaxGuardSeconds = .60f, MaxSkipFadeSeconds = .30f;
            [Tooltip("훑기: 창 가운데가 옮겨 가는 거리 / 그림 폭")] public float MaxPanFractionOfSource = .10f;
            public float MaxPush = 1.06f;
            [Tooltip("초당 화면 폭의 몇 배까지 훑는가")] public float MaxPanScreenFractionPerSecond = .05f;
            [Tooltip("초점은 끝 창의 가운데 이 폭 × 높이 안")] public float FocusCentreWidth = .60f, FocusCentreHeight = .70f;
            [Tooltip("다른 화면 비로 잘라도 초점이 가장자리에서 이만큼 안쪽")] public float CropEdgeMargin = .10f;
            public float[] CropAspects = Array.Empty<float>();
            public StretchLimit[] Stretch = Array.Empty<StretchLimit>();
            public float MaxCueGain = .60f;
            public int MaxVoices = 2;
            public float NormalLengthTolerance = .01f, RectTolerance = .0005f;
            public int MaxTextureSize = 2048;
            public long MaxStillBytes = 2359296, MaxSequenceBytes = 9437184, MaxTotalBytes = 41943040;
        }

        [Serializable]
        public sealed class Trigger
        {
            public CinematicTriggerKind Kind;
            [Tooltip("EncounterDetected / EncounterDefeated (조각 2 – 3)")] public string EncounterId = "";
            [Tooltip("PlaneCrossing: 면 위의 한 점(세계 좌표)")] public Vector3 Point;
            [Tooltip("PlaneCrossing: 바깥 방향(XZ, 길이 1). 안 → 밖으로 넘을 때만 켠다")] public Vector2 OutwardNormalXZ;
            public float HalfWidth;
            public float FeetYMin, FeetYMax;
            [Tooltip("넘은 뒤 시작 조건을 기다려 주는 반경. 벗어나면 버린다(본 것으로 적지 않는다)")] public float PendingRadius;
            [Tooltip("이 반경 안 · 안쪽에 있으면 그림을 미리 올리고 도착 이름을 붙든다")] public float PreloadRadius;
            [Tooltip("한 걸음이 이보다 길면 넘은 것으로 치지 않는다(되살아남 · 순간 이동)")] public float MaxCrossingStep;
            public float StartDelaySeconds;
        }

        [Serializable]
        public sealed class Cue
        {
            [Tooltip("소리 팔레트(CompactSoundPalette255)의 id")] public string Id = "";
            public float AtSeconds, Gain, FadeInSeconds, FadeOutSeconds;
            public bool Loop;
        }

        [Serializable]
        public sealed class Still
        {
            public string Id = "";
            [Tooltip("Resources 밑 경로(확장자 없음)")] public string ResourcePath = "";
            public float Seconds;
            [Tooltip("앞 장 위로 이 장이 번져 드는 시간(첫 장은 쓰지 않는다)")] public float CrossFadeSeconds;
            [Tooltip("그림 좌표 0..1, 왼쪽 위가 원점. 원본과 같은 비(폭 = 높이)")] public Rect Rect0 = new Rect(0, 0, 1, 1), Rect1 = new Rect(0, 0, 1, 1);
            public CinematicEase Ease = CinematicEase.InOutSine;
            public Vector2 Focus = new Vector2(.5f, .5f);
            public Cue[] Cues = Array.Empty<Cue>();
            [Tooltip("자리 그림(TE-6). 릴리스 검사에서 0")] public bool IsPlaceholder;
        }

        [Serializable]
        public sealed class Sequence
        {
            public string Id = "";
            public bool Disabled;
            public Trigger Trigger = new Trigger();
            public CinematicFreeze Freeze = CinematicFreeze.Pause;
            [Tooltip("새 여정에만: 저장에 cinematic.enrolled 가 있어야 뜬다")] public bool RequiresEnrollment;
            [Tooltip("걸려 있는 동안 이 도착 이름을 붙들어 둔다(#262). 비우면 없음")] public string HoldsArrivalId = "";
            public bool DefersDetail;
            [Tooltip("D308-28: 방향을 말하지 않는다 — 가로 훑기 0, 창과 초점이 세로 가운데 축 위")] public bool NoLateralMotion;
            public Still[] Stills = Array.Empty<Still>();
            public float TotalSeconds { get { float t = 0; if (Stills != null) foreach (var s in Stills) if (s != null) t += s.Seconds; return t; } }
        }

        public Tokens Style = new Tokens();
        public Limits Rules = new Limits();
        public StringEventChannelSO Requested, Started, Finished;
        public Sequence[] Sequences = Array.Empty<Sequence>();

        public Sequence Find(string id)
        {
            if (string.IsNullOrEmpty(id) || Sequences == null) return null;
            foreach (var s in Sequences) if (s != null && s.Id == id) return s;
            return null;
        }

        /// <summary>The table (TEST). Slice 1 = `mine.exit` only, re-briefed by D308-28: three stills that give memory and mood and say
        /// no direction - no pan, every window centred, no outdoor landmark. Channels are assets and are not touched here.</summary>
        public void ApplyDefaults308()
        {
            Style = new Tokens
            {
                SkipKeys = new[] { CinematicSkipKey.F, CinematicSkipKey.Space, CinematicSkipKey.Enter, CinematicSkipKey.Escape, CinematicSkipKey.MouseLeft,
                    CinematicSkipKey.PadSouth, CinematicSkipKey.PadEast, CinematicSkipKey.PadStart },
            };
            Rules = new Limits
            {
                CropAspects = new[] { 3440f / 1440f, 1920f / 1200f },
                Stretch = new[] { new StretchLimit { ScreenWidth = 1920, MaxStretch = 1.35f }, new StretchLimit { ScreenWidth = 2560, MaxStretch = 1.80f } },
            };
            Sequences = new[]
            {
                new Sequence
                {
                    Id = "mine.exit", RequiresEnrollment = true, HoldsArrivalId = "realm_cheongrim", NoLateralMotion = true, Freeze = CinematicFreeze.Pause,
                    Trigger = new Trigger
                    {
                        // 문턱 (3357.31, 2012.82) 에서 굴 안으로 4.0 m, 바깥 방향 42.9도 [O Art/Cinematic308/_work_design/trigger_mine_exit.json]
                        Kind = CinematicTriggerKind.PlaneCrossing, Point = new Vector3(3354.59f, 168.1f, 2009.89f), OutwardNormalXZ = new Vector2(.6807f, .7325f),
                        HalfWidth = 5f, FeetYMin = 166.8f, FeetYMax = 171.6f, PendingRadius = 12f, PreloadRadius = 40f, MaxCrossingStep = 3f,
                    },
                    Stills = new[]
                    {
                        // 01 뒤에 남은 굴: 물러남 1.05 -> 1.00. 굴 공기.
                        S("gallery", "Cinematic308/Stills/mine_exit_01_gallery", 2.8f, 1.05f, 1.00f, new Cue { Id = "cave_air_loop", Gain = .45f, FadeInSeconds = .2f, Loop = true }),
                        // 02 손과 붓: 다가감 1.00 -> 1.03. 굴 공기가 이 장 끝에서 잦아든다.
                        S("brush", "Cinematic308/Stills/mine_exit_02_brush", 2.6f, 1.00f, 1.03f, new Cue { Id = "cave_air_loop", Gain = .45f, FadeOutSeconds = 1.2f, Loop = true }),
                        // 03 틀 너머의 종이: 다가감 1.00 -> 1.06, 종이에서 게임으로. 바람이 든다.
                        S("paper", "Cinematic308/Stills/mine_exit_03_paper", 3.0f, 1.00f, 1.06f, new Cue { Id = "wind_loop", Gain = .40f, FadeInSeconds = 1.0f, FadeOutSeconds = .5f, Loop = true }),
                    },
                },
            };
        }

        /// <summary>A window of the source picture's own aspect, centred, showing 1 / zoom of it.</summary>
        public static Rect Centred(float zoom)
        {
            float size = 1f / Mathf.Max(1f, zoom);
            return new Rect((1f - size) * .5f, (1f - size) * .5f, size, size);
        }

        static Still S(string id, string path, float seconds, float zoom0, float zoom1, params Cue[] cues) => new Still
        {
            Id = id, ResourcePath = path, Seconds = seconds, CrossFadeSeconds = .45f, Rect0 = Centred(zoom0), Rect1 = Centred(zoom1),
            Ease = CinematicEase.InOutSine, Focus = new Vector2(.5f, .5f), Cues = cues, IsPlaceholder = true,
        };
    }
}
