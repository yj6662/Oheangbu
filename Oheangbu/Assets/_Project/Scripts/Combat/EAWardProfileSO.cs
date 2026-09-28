using UnityEngine;

namespace Oheangbu.Combat
{
    [CreateAssetMenu(menuName = "Oheangbu/Combat/EA Wards TEST")]
    public sealed class EAWardProfileSO : ScriptableObject
    {
        public GameObject[] PresentationPrefabs = new GameObject[5]; // 구 누 무 수 우
        [Min(1)] public float Duration = 6f;
        [Min(.1f)] public float Radius = 3f;
        [Min(.1f)] public float Height = 2.2f;
        [Min(0)] public float Formation = .45f;
        [Min(0)] public float Fade = .5f;
        [Range(0,.9f)] public float ElementalReduction = .6f;
        public bool Valid => float.IsFinite(Duration) && Duration > 0 && float.IsFinite(Radius) && Radius > 0
            && float.IsFinite(Height) && Height > 0 && float.IsFinite(Formation) && Formation >= 0
            && float.IsFinite(Fade) && Fade >= 0 && Formation + Fade < Duration
            && float.IsFinite(ElementalReduction) && ElementalReduction >= 0 && ElementalReduction <= .9f;
    }
}
