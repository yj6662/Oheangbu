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

        public bool IsValid => TouchShader != null && SizeInStrokeWidths > 0f && SpreadSeconds > 0f;
    }
}
