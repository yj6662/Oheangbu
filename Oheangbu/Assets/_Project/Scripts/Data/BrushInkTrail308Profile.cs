using UnityEngine;

namespace Oheangbu.Data.Spell
{
    // #308 (user request 2026-10-13: "붓끝에 먹물로 붓으로 그리는 듯한 잔상이 계속 남게 해줘. 그냥 걸어다닐때도.") [every number TEST]
    // The ink the brush tip leaves in the air while the player moves: a short ribbon behind the world brush's tip that thins,
    // breaks into dry-brush streaks and is gone in about a second. Presentation only: recognition, power and judgement never
    // read it. Ink, not light (ART-INK): no emission field, no HDR colour.
    // Runtime lookup: Resources "Trail308/BrushInkTrail308Profile". Missing = no trail.
    [CreateAssetMenu(menuName = "Oheangbu/Spell/Brush Ink Trail 308", fileName = "BrushInkTrail308Profile")]
    public sealed class BrushInkTrail308Profile : ScriptableObject
    {
        public Shader TrailShader;

        [Header("길이 · 굵기")]
        [Tooltip("잔상이 남아 있는 시간(초).")]
        [Range(.1f, 4f)] public float Seconds = 1.1f;
        [Tooltip("붓끝 바로 뒤의 굵기(m).")]
        [Range(.005f, .4f)] public float HeadWidth = .055f;
        [Tooltip("꼬리 끝의 굵기(머리 굵기에 대한 비율).")]
        [Range(0f, 3f)] public float TailWidthShare = .15f;
        [Tooltip("붓끝이 이만큼(m) 움직일 때마다 점을 하나 놓는다.")]
        [Range(.005f, .3f)] public float MinVertexDistance = .025f;
        [Tooltip("붓끝 빠르기가 이 값(m/s)일 때 가장 가늘어진다(빠를수록 갈필).")]
        [Min(.1f)] public float ThinAtSpeed = 9f;
        [Tooltip("가장 빠를 때의 굵기 비율.")]
        [Range(.1f, 1f)] public float ThinShare = .55f;

        [Header("먹")]
        [ColorUsage(false, false)] public Color InkColor = new Color(.05f, .047f, .045f, 1f);
        [Tooltip("머리 쪽 농도.")]
        [Range(0f, 1f)] public float Opacity = .82f;
        [Tooltip("갈필 결의 촘촘함(폭 방향 줄 수).")]
        [Range(2f, 60f)] public float StreakDensity = 18f;
        [Tooltip("갈필의 세기 — 꼬리로 갈수록 결이 갈라진다.")]
        [Range(0f, 1f)] public float DryStrength = .7f;
        [Tooltip("가장자리 부드러움.")]
        [Range(.02f, .6f)] public float EdgeSoftness = .22f;

        [Header("끄는 조건")]
        [Tooltip("카메라에서 이 거리(m) 안에서는 그리지 않는다(가까운 화면에서 눈앞을 가리지 않게).")]
        [Min(0)] public float HideWithinCamera = .45f;
        [Tooltip("붓끝이 한 프레임에 이 거리(m)보다 멀리 옮겨지면(순간 이동) 잔상을 지운다.")]
        [Min(.5f)] public float ClearJump = 3f;

        public bool IsValid => TrailShader != null && Seconds > 0f && HeadWidth > 0f && !float.IsNaN(Seconds + HeadWidth + MinVertexDistance + ThinAtSpeed);
    }
}
