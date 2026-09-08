using System;
using UnityEngine;

namespace Oheangbu.C02RigFaceLab
{
    [CreateAssetMenu(menuName = "Oheangbu/C02 Rig Face Lab/Profile")]
    public sealed class LabProfileSO : ScriptableObject
    {
        public string[] faceChannels = Array.Empty<string>();
        public ExpressionPreset[] presets = Array.Empty<ExpressionPreset>();
        public float transitionSeconds = 0.18f;
        public int runCycles = 6;
        public int captureFps = 30;
        public int captureWidth = 1280;
        public int captureHeight = 720;
        public float gameReferenceFov = 60f;
        public Vector3 gameReferenceOffset = new Vector3(0f, 1.25f, -2.8f);
        public string gameReferenceSource = "PlayerRig.prefab CameraPivot.y=0.7, CombatConfig_Default shoulder=(0,0.55,-2.8), camera FOV=60. Distance reference; no game controller or collision.";
    }

    [Serializable]
    public sealed class ExpressionPreset
    {
        public string name;
        public float[] values = Array.Empty<float>();
        public string verification = "UNVERIFIED";
    }
}
