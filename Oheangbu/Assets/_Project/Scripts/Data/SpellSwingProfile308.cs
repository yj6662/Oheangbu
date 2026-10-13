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
