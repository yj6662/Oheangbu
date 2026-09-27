using UnityEngine;

namespace Oheangbu.Data.World
{
    [CreateAssetMenu(menuName="Oheangbu/World/Mum Bridge TEST")]
    public sealed class MumBridgeProfileSO : ScriptableObject
    {
        [Min(.1f)] public float MinimumSpan=4f;
        [Min(1f)] public float MaximumSpan=40f;
        [Min(.5f)] public float Width=2.6f;
        [Min(.1f)] public float Thickness=.45f;
        [Min(0)] public float MaximumEndHeightDifference=2f;
        [Range(0,30)] public float MaximumSlope=8f;
        [Min(.1f)] public float FormationSeconds=1.2f;
        [Min(1)] public int MaximumActive=16;
        [Min(.1f)] public float LandingOverlap=.6f;
        [Range(0,6f)] public float MaximumBankEmbedApproach=6f;
        [Range(0,45)] public float MaximumBankSlope=20f;
        [Min(.01f)] public float BankHeightTolerance=.18f;
        public Material DeckMaterial;
        public Material PreviewMaterial;
        [Tooltip("Optional source-derived paving. Empty preserves the original generated-earth presentation.")]
        public Mesh SurfaceTile;
        public Material[] SurfaceMaterials=System.Array.Empty<Material>();
        [Min(.1f)] public float SurfaceTileLength=2.6f;
        public bool IsValid=>Finite(MinimumSpan)&&Finite(MaximumSpan)&&MinimumSpan>=4&&MaximumSpan>=MinimumSpan&&MaximumSpan<=40&&
            Finite(Width)&&Width>=2.6f&&Finite(Thickness)&&Thickness>.05f&&Finite(MaximumEndHeightDifference)&&MaximumEndHeightDifference>=0&&MaximumEndHeightDifference<=2&&
            Finite(MaximumSlope)&&MaximumSlope>=0&&MaximumSlope<=8&&Finite(FormationSeconds)&&FormationSeconds>0&&MaximumActive>0&&MaximumActive<=16&&
            Finite(LandingOverlap)&&LandingOverlap>.1f&&Finite(MaximumBankSlope)&&MaximumBankSlope>=0&&MaximumBankSlope<=45&&
            Finite(MaximumBankEmbedApproach)&&MaximumBankEmbedApproach>=0&&MaximumBankEmbedApproach<=6f&&
            Finite(BankHeightTolerance)&&BankHeightTolerance>0&&DeckMaterial!=null;
        static bool Finite(float value)=>float.IsFinite(value);
    }
}
