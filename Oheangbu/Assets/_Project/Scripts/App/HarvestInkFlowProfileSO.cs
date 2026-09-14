using UnityEngine;
namespace Oheangbu.App
{
    [CreateAssetMenu(menuName="Oheangbu/Playtest/Harvest Ink Flow")]
    public sealed class HarvestInkFlowProfileSO : ScriptableObject
    {
        public Material Material;
        public Color InkColor = new Color(.105f,.095f,.08f,.92f);
        [Range(0,2)] public int FineThreads = 2;
        [Range(12,48)] public int Points = 32;
        [Min(.001f)] public float RootWidth = .036f, TipWidth = .006f;
        [Min(0f)] public float Spread = .055f, Sag = .12f;
        [Min(.03f)] public float RevealSeconds = .22f, ReleaseSeconds = .18f;
        [Min(.1f)] public float FlowMetersPerSecond = 2.6f;
        [Range(0,20)] public int Droplets = 12;
        [Min(.001f)] public float DropletSize = .018f;
    }
}
