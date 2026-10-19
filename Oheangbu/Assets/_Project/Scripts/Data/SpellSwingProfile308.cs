using System;
using UnityEngine;

namespace Oheangbu.Data.Spell
{
    // SPEC-SPELL-SWING-308 (D308-80) [every number TEST]: the swing after a drawn glyph. Release still confirms the cast; a
    // waiting category (attack, summon, field, install) is planned and launched at the swing's apex, and until the lock ends the
    // drawing mode cannot be entered again (the rate cap on casting). Parry, ward and buff take effect at release and only lock.
    // Nothing here is read by recognition or by power: the swing's time is not part of the brush force (SPELL-BRUSH).
    // Runtime lookup: scene field -> Resources "Swing308/SpellSwingProfile308". Missing = the game before this stage.
    [CreateAssetMenu(menuName = "Oheangbu/Spell/Swing Profile 308", fileName = "SpellSwingProfile308")]
    public sealed class SpellSwingProfile308 : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("떼고 나서 발사(계획)까지(초). 0 = 떼는 순간(기다리지 않는다).")]
            [Min(0)] public float ApexSeconds;
            [Tooltip("떼고 나서 다시 쓸 수 있을 때까지(초). 정점보다 짧으면 정점으로 늘린다.")]
            [Min(0)] public float LockSeconds;
            [Tooltip("Cast308 층의 애니메이터 상태 이름(비우면 동작 없음).")]
            public string State;
        }

        [Header("기다리는 분류 — 정점에 발사 [TEST]")]
        // 2026-10-13 (D308-80c, user): slower and heavier - the cast leaves later too (single .25 -> .40, area .50 -> .65, summon .55 -> .70)
        public Entry AttackSingle = new Entry { ApexSeconds = 0.40f, LockSeconds = 0.65f, State = "SwingSweep" };
        public Entry AttackArea = new Entry { ApexSeconds = 0.65f, LockSeconds = 1.10f, State = "SwingArc" };
        public Entry Summon = new Entry { ApexSeconds = 0.70f, LockSeconds = 1.10f, State = "SwingSlam" };
        public Entry Field = new Entry { ApexSeconds = 0.60f, LockSeconds = 0.95f, State = "SwingSlam" };
        public Entry Install = new Entry { ApexSeconds = 0.60f, LockSeconds = 0.95f, State = "SwingSlam" };

        [Header("기다리지 않는 분류 — 떼는 순간 효과, 잠금만 [TEST]")]
        public Entry Parry = new Entry { ApexSeconds = 0f, LockSeconds = .20f, State = "SwingPlant" };
        public Entry Ward = new Entry { ApexSeconds = 0f, LockSeconds = .40f, State = "SwingPlant" };
        public Entry Buff = new Entry { ApexSeconds = 0f, LockSeconds = .40f, State = "SwingDraw" };

        // D308-84 (SPEC-SPELL-SWING-308 section 5c): the motion is picked at release from the category and the place the cast is
        // expected to land (the aimed target, or the free-aim point). Presentation only: apex, lock and power above are untouched,
        // and an empty state name falls back to the entry's own state. Enabled = false is the table of D308-80 (one motion per category).
        [Serializable]
        public struct PositionRule
        {
            public bool Enabled;
            [Tooltip("공격 · 단발: 예상 자리가 이만큼(m, 수평) 이상 멀면 SingleFarState.")]
            [Min(0)] public float SingleFarMeters;
            public string SingleFarState;
            [Tooltip("공격 · 원: 이만큼(m) 이상 멀면 CircleFarState, 아니면 CircleNearState.")]
            [Min(0)] public float CircleFarMeters;
            public string CircleNearState, CircleFarState;
            [Tooltip("공격 · 경로: 발밑에서 바닥을 타고 나가므로 땅 찍기.")]
            public string PathState;
            [Tooltip("방벽: 짧은 내리찍기. 효과는 지금처럼 떼는 순간 — 클립을 정점 WardLeadSeconds 앞부터 재생한다.")]
            public string WardState;
            [Min(0)] public float WardLeadSeconds;
            [Tooltip("상체 조준 보정(도): 대상이 높으면 젖힘(위), 낮으면 숙임(아래), 좌우 요. 락온 · 자유 조준 대상이 있을 때만.")]
            [Range(0, 60)] public float AimPitchUp, AimPitchDown, AimYaw;
            [Tooltip("이 각(도)보다 작은 높이 차이는 보정하지 않는다.")]
            [Range(0, 20)] public float AimDeadbandDegrees;
            [Tooltip("정점 이만큼(초) 전부터 보정이 오른다. 잠금 끝까지 내려간다.")]
            [Min(.01f)] public float AimInSeconds;

            public static PositionRule Default => new PositionRule
            {
                Enabled = true, SingleFarMeters = 8f, SingleFarState = "SwingThrow", CircleFarMeters = 6f, CircleNearState = "SwingSlam", CircleFarState = "SwingStrikeFar",
                PathState = "SwingSlam", WardState = "SwingSlam", WardLeadSeconds = .15f, AimPitchUp = 35f, AimPitchDown = 25f, AimYaw = 20f, AimDeadbandDegrees = 5f, AimInSeconds = .25f,
            };
        }

        [Header("위치 · 분류별 동작 — D308-84 [TEST]")]
        public PositionRule Position = PositionRule.Default;

        [Header("취소 [TEST]")]
        [Tooltip("정점 전에 한 번에 최대 체력의 이 비율 이상을 맞으면 불발(큰 경직 — 피격 반응 프로필의 LargeHitShare와 맞춘다). 0 = 피격은 불발시키지 않는다.")]
        [Range(0, 1)] public float LargeHitFizzleShare = .22f;

        public bool IsValid =>
            Ok(AttackSingle) && Ok(AttackArea) && Ok(Summon) && Ok(Field) && Ok(Install) && Ok(Parry) && Ok(Ward) && Ok(Buff);

        static bool Ok(in Entry e) =>
            !float.IsNaN(e.ApexSeconds) && !float.IsInfinity(e.ApexSeconds) && e.ApexSeconds >= 0f && e.ApexSeconds <= 3f &&
            !float.IsNaN(e.LockSeconds) && !float.IsInfinity(e.LockSeconds) && e.LockSeconds >= 0f && e.LockSeconds <= 5f;
    }
}
