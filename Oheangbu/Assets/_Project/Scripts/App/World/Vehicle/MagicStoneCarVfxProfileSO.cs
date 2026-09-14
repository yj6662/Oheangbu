using UnityEngine;

namespace Oheangbu.App.World.Vehicle
{
    [CreateAssetMenu(menuName="Oheangbu/World/Magic Stone Car VFX TEST",fileName="MagicStoneCarVfxProfile")]
    public sealed class MagicStoneCarVfxProfileSO : ScriptableObject
    {
        [Min(.01f)] public float IgnitionSeconds=.2f;
        [Min(.01f)] public float PatternSeconds=.4f;
        [Min(.01f)] public float VentSeconds=.45f;
        [Min(.01f)] public float CooldownSeconds=1;
        [Min(.01f)] public float StoppedSpeed=.25f;
        [Min(.01f)] public float MovingSpeed=.8f;
        [Range(0,1)] public float PedalThreshold=.1f;
        [Min(0)] public float MinimumMotorTorque=1;
        [ColorUsage(false,true)] public Color CoreColour=new Color(.1f,.65f,.8f,1);
        [Min(0)] public float PeakEmission=2.2f;
        [Min(0)] public float RunningEmission=.22f;
        [Min(0)] public float IdleEmission;
        [Min(.01f)] public float EmissionResponse=.15f;
        [Min(1)] public int VentParticles=18;
        public GameObject PatternPrefab;
    }
}
