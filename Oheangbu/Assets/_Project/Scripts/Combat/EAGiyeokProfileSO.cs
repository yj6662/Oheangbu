using UnityEngine;
namespace Oheangbu.Combat
{
    [CreateAssetMenu(menuName="Oheangbu/Combat/EA Giyeok TEST")]
    public sealed class EAGiyeokProfileSO:ScriptableObject
    {
        public GameObject RootPrefab, BurnPrefab, PiercePrefab, ReturnPrefab;
        [Min(.1f)] public float PierceRange=18f, PierceRadius=.45f, ReturnDelay=.65f, ReturnReach=2f;
        [Min(.1f)] public float RootDuration=3f, BurnDuration=5f;
        [Range(.1f,1)] public float BossRootSpeed=.55f;
        [Min(0)] public float BurnPowerPerSecond=.25f;
        public bool Valid=>float.IsFinite(PierceRange)&&PierceRange>0&&float.IsFinite(PierceRadius)&&PierceRadius>0
            &&float.IsFinite(ReturnDelay)&&ReturnDelay>0&&float.IsFinite(ReturnReach)&&ReturnReach>0
            &&float.IsFinite(RootDuration)&&RootDuration>0&&float.IsFinite(BurnDuration)&&BurnDuration>0
            &&float.IsFinite(BossRootSpeed)&&BossRootSpeed>=.1f&&BossRootSpeed<=1
            &&float.IsFinite(BurnPowerPerSecond)&&BurnPowerPerSecond>=0;
    }
}
