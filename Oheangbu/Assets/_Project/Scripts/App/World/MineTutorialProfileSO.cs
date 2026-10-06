using System;
using UnityEngine;

namespace Oheangbu.App.World
{
    public enum MineTutorialSignal
    {
        StandingStill,      // T0 — 시작 뒤 움직임 입력 없이 일정 시간
        FirstRespawn,       // T3 — 첫 쓰러짐 뒤 쉼터에서 일어남
        BossSighted,        // L1 — 보스가 플레이어를 알아봄
        NeutralTelegraph,   // L2 — 첫 무속성 예고(들이받기)
        AfterNeutral,       // L3 — 첫 무속성 기술이 끝남
        ElementalShard,     // L4 — 첫 청록 기관 발광(파편 던지기)
        GroggyFull,         // L5 — 그로기 가득
        InkLow,             // L6 — 먹 부족
        GroundRing,         // L7 — 결정 고리 분출
    }

    public enum MineTutorialWait { None, LockOn, PlayerHit }

    // #306 #11 폐광 튜토리얼 박자 [TEST, SPEC-PLAYTEST-306 #11, D306]. 카드는 각각 한 번만, 조작만 말한다(설정·길 안내 금지).
    // 원장 id = IdPrefix + beat Id (WorldMacroProgress.ledger.completed), 새 저장에만 적용(EnrolledId).
    [CreateAssetMenu(menuName = "Oheangbu/World/Mine Tutorial Profile")]
    public sealed class MineTutorialProfileSO : ScriptableObject
    {
        public const string IdPrefix = "tutorial.mine.";
        public const string EnrolledId = IdPrefix + "enrolled";
        public const string BossId = "mine_tutorial_boss";

        [Serializable]
        public sealed class Beat
        {
            public string Id;
            public MineTutorialSignal Signal;
            public string Title;
            [TextArea] public string Body;
            [Tooltip("건반 모양으로 그릴 키 이름(예: Tab, Shift, Q, 좌클릭)")] public string[] Keys = Array.Empty<string>();
            [Tooltip("획 재생할 글자(비우면 없음)")] public string Letter = "";
            public MineTutorialWait Wait;
            [Min(0f)] public float WaitSeconds = 6f;
        }

        public bool Enabled = true;
        public Beat[] Beats = Array.Empty<Beat>();
        [Header("신호 [TEST]")]
        [Min(1f)] public float StandingStillSeconds = 8f;
        [Min(.1f)] public float StandingStillMetres = 1f;
        [Range(0f, 1f)] public float InkLowFraction = .2f;
        [Tooltip("같은 받아치기 신호를 되풀이하는 최대 횟수(첫 번 제외)")]
        [Range(0, 4)] public int ParryRepeats = 2;
        [Tooltip("카드는 닫기 입력을 이 시간 뒤부터 받는다(실시간)")]
        [Min(0f)] public float CardMinSeconds = .6f;
        [Tooltip("Esc를 이만큼 누르면 남은 익힘을 모두 본 것으로 한다(실시간)")]
        [Min(.2f)] public float SkipHoldSeconds = 1f;
        [Header("받아치기 결과 한 줄(카드 아님)")]
        public string ParrySuccessLine = "받아칠수록 고리에 먹이 찬다.";
        public string ParryGeneratesLine = "어는 목을 살린다.";

        public Beat Find(MineTutorialSignal signal)
        {
            if (Beats == null) return null;
            foreach (var b in Beats) if (b != null && b.Signal == signal) return b;
            return null;
        }

        public bool TryValidate(out string error)
        {
            if (Beats == null || Beats.Length == 0 || Beats.Length > 9) { error = "박자는 1~9개."; return false; }
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var b in Beats)
            {
                if (b == null || string.IsNullOrWhiteSpace(b.Id) || !seen.Add(b.Id) || b.Id.IndexOf('.') >= 0) { error = "박자 id가 비었거나 겹친다."; return false; }
                if (string.IsNullOrWhiteSpace(b.Body)) { error = "박자 " + b.Id + " 본문이 없다."; return false; }
            }
            error = null; return true;
        }

        /// <summary>The TEST script (PLAN §2-11 table): seven fight cards at most, T0 / T3 / L6 only when their condition happens.</summary>
        public void ApplyDefaults()
        {
            Beats = new[]
            {
                B("t0_walk", MineTutorialSignal.StandingStill, "걷기", "[W A S D] 걷는다 · [마우스] 본다", new[] { "W", "A", "S", "D", "마우스" }),
                B("t3_rest", MineTutorialSignal.FirstRespawn, "쉼", "쉬면 몸과 먹이 돌아온다. 쓰러뜨린 것들도 돌아온다.", Array.Empty<string>()),
                B("l1_lock", MineTutorialSignal.BossSighted, "붙들기", "[Tab] 붙든다", new[] { "Tab" }, "", MineTutorialWait.LockOn, 6f),
                B("l2_dodge", MineTutorialSignal.NeutralTelegraph, "피하기", "빛 없는 것은 몸으로 피한다 [Shift]", new[] { "Shift" }),
                B("l3_draw", MineTutorialSignal.AfterNeutral, "긋기", "[Q]를 누른 채 긋고, 떼면 글자가 선다 — 사", new[] { "Q" }, "사", MineTutorialWait.PlayerHit, 8f),
                B("l4_parry", MineTutorialSignal.ElementalShard, "받아치기", "청록은 목이다. 金이 木을 이긴다 — 서.\n닿기 직전에 뗀다.", new[] { "Q" }, "서"),
                B("l5_riposte", MineTutorialSignal.GroggyFull, "앞잡", "고리가 닫혔다 — [Q] 한 번", new[] { "Q" }),
                B("l6_harvest", MineTutorialSignal.InkLow, "먹 뽑기", "먹이 마르면 [좌클릭]으로 덩어리를 뽑는다", new[] { "좌클릭" }),
                B("l7_ward", MineTutorialSignal.GroundRing, "두르기", "사방에서 오면 두른다 — 수", new[] { "Q" }, "수"),
            };
        }

        static Beat B(string id, MineTutorialSignal signal, string title, string body, string[] keys, string letter = "",
            MineTutorialWait wait = MineTutorialWait.None, float waitSeconds = 0f) =>
            new Beat { Id = id, Signal = signal, Title = title, Body = body, Keys = keys, Letter = letter, Wait = wait, WaitSeconds = waitSeconds };
    }

    /// <summary>One card request from the session to the UI (track S raises, track U shows).</summary>
    public sealed class MineTutorialCard306
    {
        public string BeatId, Title, Body, Letter;
        public string[] Keys;
        public float MinSeconds, SkipHoldSeconds;
    }
}
