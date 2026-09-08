using UnityEngine;

namespace Oheangbu.C02RigFaceLab
{
    public sealed class LabSkinningScope : MonoBehaviour
    {
        public bool allowMoreThanFourWeights;
        SkinWeights previous;
        bool changed;
        public string previousSetting => previous.ToString();
        void OnEnable()
        {
            if (!Application.isPlaying || !allowMoreThanFourWeights) return;
            previous = QualitySettings.skinWeights;
            QualitySettings.skinWeights = SkinWeights.Unlimited;
            changed = true;
        }
        void OnDisable() { Restore(); }
        void OnDestroy() { Restore(); }
        void Restore() { if (!changed) return; QualitySettings.skinWeights = previous; changed = false; }
    }
}
