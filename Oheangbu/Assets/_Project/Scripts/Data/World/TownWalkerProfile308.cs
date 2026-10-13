using UnityEngine;

namespace Oheangbu.Data.World
{
    // #308 D308-77 (TEST): the silent townspeople of the capital as data. TownWalkers308 reads every number from this asset; the code
    // holds no default numbers and nothing switches on without a usable asset (Usable). Appearance only: the people have no dialogue,
    // no content row, no collider, no EnemyVitals (so lock-on, aim assist and hits never see them) and no HUD element.
    [CreateAssetMenu(menuName = "Oheangbu/World/Town Walker Profile 308")]
    public sealed class TownWalkerProfile308 : ScriptableObject
    {
        [Tooltip("끄는 스위치. 꺼져 있으면 몸을 하나도 꺼내지 않는다.")]
        public bool Enabled;

        [Header("인원 · 거리 [TEST]")]
        [Tooltip("동시에 몸을 가진 사람의 상한(몸 종류와 상관없이).")]
        [Min(0)] public int HardCap;
        [Tooltip("플레이어에서 이 거리(m) 안에 든 사람이 몸을 얻는다.")]
        [Min(0)] public float ShowDistance;
        [Tooltip("이 거리(m)를 넘으면 몸을 돌려준다(ShowDistance 보다 커야 깜빡이지 않는다).")]
        [Min(0)] public float HideDistance;
        [Tooltip("이 거리(m)보다 가까운 사람은 화면 밖에 있을 때만 몸을 얻는다(눈앞에서 생겨나지 않게). 가림(모퉁이 뒤) 검사는 하지 않는다.")]
        [Min(0)] public float PopDistance;
        [Tooltip("화면 밖 판정에 더하는 여유(뷰포트 비율).")]
        [Range(0, .5f)] public float ViewMargin;
        [Tooltip("도성 반경 안으로 들어온 뒤 이 시간(초) 동안은 PopDistance 를 보지 않는다(불러오기 · 순간이동 직후의 빈 거리 방지).")]
        [Min(0)] public float FirstFillSeconds;
        [Tooltip("몸 배정 주기(초).")]
        [Min(.02f)] public float AssignSeconds;
        [Tooltip("배정 한 번에 새로 꺼내는 몸의 수(새 몸 만들기가 한 프레임에 몰리지 않게).")]
        [Min(1)] public int SpawnsPerAssign;
        [Tooltip("몸이 없는 사람을 한 프레임에 몇 명씩 굴리는가(자리만 계산).")]
        [Min(1)] public int LogicPerFrame;

        [Header("걷기 [TEST]")]
        [Min(0)] public float WalkSpeedMin;
        [Min(0)] public float WalkSpeedMax;
        [Min(0)] public float TurnDegreesPerSecond;
        [Tooltip("옆으로 비켜서는 빠르기(m/s).")]
        [Min(0)] public float SideStepSpeed;

        [Header("머무름 [TEST]")]
        [Tooltip("좌판 · 가게 앞 몇 m 앞에서 들를지 정하는가.")]
        [Min(0)] public float StopLookAhead;
        [Tooltip("들를지 묻는 주기(초)와 그때마다의 확률.")]
        [Min(.1f)] public float BrowseCheckSeconds;
        [Range(0, 1)] public float BrowseChancePerCheck;
        [Min(0)] public float BrowseSecondsMin;
        [Min(0)] public float BrowseSecondsMax;

        [Header("플레이어 앞에서 [TEST]")]
        [Tooltip("이 거리(m) 안에서 플레이어가 앞에 있으면 옆으로 비킨다.")]
        [Min(0)] public float YieldRadius;
        [Tooltip("플레이어가 탈것에 앉아 있을 때의 비킴 거리(m).")]
        [Min(0)] public float SeatedYieldRadius;
        [Tooltip("비켜서는 폭(m). 길의 쓸 수 있는 폭 안으로 잘린다.")]
        [Min(0)] public float SideStepMetres;
        [Tooltip("이 거리(m) 안이고 비킬 폭이 없으면 멈춰 서서 본다.")]
        [Min(0)] public float StopRadius;
        [Tooltip("몸이 플레이어와 겹치지 않게 밀려나는 반경(m)과 빠르기(m/s). 충돌체가 아니다 - 플레이어는 막히지 않는다.")]
        [Min(0)] public float SoftPushRadius;
        [Min(0)] public float SoftPushSpeed;
        [Tooltip("이 거리(m) 안에서 흘끗 본다(인사 · 말 없음).")]
        [Min(0)] public float GlanceRadius;
        [Min(0)] public float GlanceHoldMin;
        [Min(0)] public float GlanceHoldMax;
        [Min(0)] public float GlanceCooldownSeconds;
        [Tooltip("머리가 몸 앞 방향에서 돌 수 있는 각(도).")]
        [Range(0, 90)] public float HeadYawClamp;
        [Range(0, 60)] public float HeadPitchClamp;
        [Range(0, 1)] public float HeadWeight;
        [Min(.01f)] public float HeadBlendSeconds;

        [Header("싸움이 났을 때 [TEST]")]
        [Tooltip("추격 중인 적이 플레이어에서 이 거리(m) 안이면 싸움으로 본다.")]
        [Min(0)] public float ThreatRadius;
        [Tooltip("WideThreatIds 의 적(보스)은 이 거리(m).")]
        [Min(0)] public float WideThreatRadius;
        public string[] WideThreatIds = System.Array.Empty<string>();
        [Tooltip("싸움 때 플레이어에서 이 거리(m) 안의 사람이 등을 돌리고 물러난다.")]
        [Min(0)] public float ThreatReactRadius;
        [Tooltip("물러날 때의 걸음 배율.")]
        [Min(0)] public float AvertSpeedScale;
        [Tooltip("싸움이 끝난 뒤 이 시간(초) 동안은 계속 물러나 있는다.")]
        [Min(0)] public float ThreatHoldSeconds;
        [Tooltip("싸움 판정 주기(초).")]
        [Min(.05f)] public float ThreatScanSeconds;

        [Header("몸 [TEST]")]
        [Min(0)] public float CrossFadeSeconds;
        [Tooltip("이 거리(m) 밖의 몸은 FarPoseHz 로만 자세를 갱신한다.")]
        [Min(0)] public float FullRateDistance;
        [Min(1)] public float FarPoseHz;
        [Tooltip("이 거리(m) 안의 몸만 그림자를 드리운다.")]
        [Min(0)] public float ShadowDistance;
        [Tooltip("키 변주(배율).")]
        [Min(.1f)] public float ScaleMin;
        [Min(.1f)] public float ScaleMax;
        [Tooltip("옷 색(몸 프리팹의 TintSlots 에 곱한다). 사람 자료의 tint 번호가 이 칸을 가리킨다.")]
        public Color[] Tints = System.Array.Empty<Color>();

        public bool Usable => Enabled && HardCap > 0 && ShowDistance > 0 && HideDistance > ShowDistance && WalkSpeedMin > 0 && WalkSpeedMax >= WalkSpeedMin
            && TurnDegreesPerSecond > 0 && AssignSeconds > 0 && SpawnsPerAssign > 0 && LogicPerFrame > 0 && BrowseCheckSeconds > 0 && ThreatScanSeconds > 0
            && HeadBlendSeconds > 0 && FarPoseHz > 0 && ScaleMin > 0 && ScaleMax >= ScaleMin && BrowseSecondsMax >= BrowseSecondsMin;
    }
}
