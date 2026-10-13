using UnityEngine;

namespace Oheangbu.Data.Spell
{
    // #308 (user request 2026-10-13): the ink that spreads where the brush touches down at the start of a stroke.
    // Presentation only - recognition and judgement never read it. No profile in Resources = no effect.
    [CreateAssetMenu(menuName = "Oheangbu/Spell/Ink Touch 308 Profile", fileName = "InkTouch308Profile")]
    public sealed class InkTouch308Profile : ScriptableObject
    {
        public const string ResourcesPath = "Touch308/InkTouch308Profile";

        public Shader TouchShader;
        [Tooltip("번진 먹의 크기: 획 기본 폭의 몇 배(쿼드 한 변).")]
        [Range(1f, 12f)] public float SizeInStrokeWidths = 4.2f;
        [Tooltip("크기 흩뜨림(±비율).")]
        [Range(0f, .5f)] public float SizeJitter = .18f;
        [Min(.02f)] public float SpreadSeconds = .22f;
        [Range(0f, 1f)] public float Fingers = .55f;
        [Range(0f, 1f)] public float Droplets = .6f;
        [Range(0f, 1f)] public float Opacity = .96f;
        [Range(0f, 1f)] public float WetSheen = .3f;
        [Min(.1f)] public float WetSeconds = 3.5f;

        [Header("획이 조금 늦게 나온다 · 예쁜 붓글씨 쪽으로 아주 조금 (표현 전용 — 인식은 원래 입력 그대로)")]
        [Tooltip("붓끝이 지나간 뒤 먹이 화면에 나타나기까지(초). 0 = 끔. 이 사이의 앞선 점들로 획을 다듬는다.")]
        [Range(0f, .2f)] public float StrokeDelaySeconds = .07f;
        [Tooltip("획 경로 다듬기(0 = 입력 그대로, 1 = 이웃 다섯 점 평균). 획의 첫 점 · 끝 점은 그대로 둔다.")]
        [Range(0f, 1f)] public float PathSmoothing = .5f;
        [Tooltip("세로로 그을 때 굵어지는 비율 · 가로로 그을 때 가늘어지는 비율(붓글씨의 가로 가늘고 세로 굵은 결).")]
        [Range(0f, .4f)] public float VerticalWidthGain = .14f;
        [Range(0f, .4f)] public float HorizontalWidthLoss = .08f;

        [Header("획 끝: 거친 갈필 · 그은 방향으로 흩뿌려지는 먹")]
        [Tooltip("획 끝(먹이 빠지는 꼬리)의 거칠기. 셰이더 _EndRough.")]
        [Range(0f, 2f)] public float EndRough = 1.1f;
        [Tooltip("흩뿌림 크기: 획 기본 폭의 몇 배(가장 빠르게 뗐을 때의 길이).")]
        [Range(0f, 20f)] public float FlingSizeInStrokeWidths = 9f;
        [Tooltip("이 속도 비율(빠른 획 속도 대비) 아래로 뗀 획은 흩뿌리지 않는다.")]
        [Range(0f, 1f)] public float FlingMinSpeedShare = .12f;
        [Range(0f, 1f)] public float FlingOpacity = .9f;

        public bool IsValid => TouchShader != null && SizeInStrokeWidths > 0f && SpreadSeconds > 0f;
    }
}
