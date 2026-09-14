using UnityEngine;

namespace Oheangbu.Data
{
    // Canonical-world authoring data; no gameplay configuration is changed.
    [CreateAssetMenu(menuName = "Oheangbu/World/Codex World Settings")]
    public sealed class CodexWorldSettingsSO : ScriptableObject
    {
        public int Seed = 90726;
        public Vector2 GroundSize = new Vector2(160f, 220f);
        public Vector2Int GroundResolution = new Vector2Int(128, 176);
        [Tooltip("Latitude rings and circumferential samples. Shared normals preserve broad weathered shoulders.")]
        public Vector2Int RockResolution = new Vector2Int(24, 40);
        [Header("Derived rock kit placement")]
        public bool UseDerivedRockKit = true;
        public int RockClusterCount = 16;
        public int RockScatterClusters = 30;
        public Vector2 RockClusterDiameter = new Vector2(8f, 14f);
        public float RockPathClearance = 5f;
        public float RockInnClearance = 12f;
        [Range(0f, 1f)] public float RockSurfaceTone = .35f;
        [Range(0f, 1f)] public float RockAmbient = .28f;
        [Range(0f, 2f)] public float RockDiffuse = .72f;
        [Range(0f, 1f)] public float RockLodNear = .18f;
        [Range(0f, 1f)] public float RockLodMiddle = .06f;
        [Tooltip("Samples along the range and across its depth. This is authoring data, not a production polygon budget.")]
        public Vector2Int MountainResolution = new Vector2Int(256, 128);
        [Header("Mountain LOD by projected height")]
        public Vector2Int MountainLod1Stride = new Vector2Int(1, 4);
        public Vector2Int MountainLod2Stride = new Vector2Int(2, 8);
        public Vector2 MountainLodThresholds = new Vector2(.65f, .30f);
        public bool MountainLodAnimateCrossFading = true;
        [Range(0f, 1f)] public float MountainLodFadeWidth = .15f;
        [Header("Shared mountain surface and distance atmosphere")]
        [Range(0f, 1f)] public float MountainRockTone = .16f;
        [Range(0f, 1f)] public float MountainAmbient = .30f;
        [Range(0f, 2f)] public float MountainDiffuse = .95f;
        [Range(0f, .5f)] public float MountainSurfaceVariation = .18f;
        [Min(0f)] public float MountainAtmosphereStart = 65f;
        [Range(0f, .01f)] public float MountainAtmosphereDensity = .00085f;
        [Range(0f, 1f)] public float MountainAtmosphereTone = .68f;
        public float PathWidth = 4.8f;
        [Header("Path pigment on the ground surface")]
        public Vector2Int PathMaskResolution = new Vector2Int(1024, 2048);
        [Min(.05f)] public float PathEdgeBlend = .8f;
        [Min(0f)] public float PathEdgeVariation = .22f;
        public Vector2 PathSurfaceTones = new Vector2(.26f, .64f);
        [Range(0f, .3f)] public float PathPigmentVariation = .09f;
        public float TunnelLength = 34f;
        public Vector3 InnAnchor = new Vector3(-15f, 0f, 38f);
        public float InnYaw = 12f;
        [Header("Thatched inn asset and warm entrance lighting")]
        public string InnModelPath = "Assets/House_1/house.fbx";
        [Min(.1f)] public float InnModelScale = .8f;
        public float InnModelYawOffset = 180f;
        public float InnFoundationEmbed = .12f;
        [Range(0f,1f)] public float InnSaturation = .06f;
        [Range(0f,2f)] public float InnAlbedoValue = .85f;
        [Range(0f,1f)] public float InnDiffuseSkyFill = .32f;
        [Range(0f,1f)] public float InnSmoothness = .12f;
        public Vector3[] InnLanternPositions = {new Vector3(.4f,2.6f,-.5f),new Vector3(-4f,2.6f,-.5f)};
        public float InnModelVerticalScale = 1.2f;
        [Header("Entry stone: donor UVs from h1_house_ground's original stair")]
        [Min(.001f)] public float InnStepBevel = .025f;
        [Min(.01f)] public float InnStepRiserTextureHeight = .166667f;
        public Vector2[] InnStepTreadUV = {new Vector2(.4451f,.6103f),new Vector2(.4454f,.5188f),new Vector2(.4636f,.6092f),new Vector2(.4665f,.5227f)};
        public Vector2[] InnStepRiserUV = {new Vector2(.4636f,.6092f),new Vector2(.4665f,.5227f),new Vector2(.4691f,.6129f),new Vector2(.4719f,.5193f)};
        public Vector2[] InnStepAlternateTreadUV = {new Vector2(.4153f,.6111f),new Vector2(.417f,.5187f),new Vector2(.4337f,.6055f),new Vector2(.4359f,.5236f)};
        public Vector2[] InnStepAlternateRiserUV = {new Vector2(.4337f,.6055f),new Vector2(.4359f,.5236f),new Vector2(.44f,.6082f),new Vector2(.4412f,.5209f)};
        public Vector3 InnApproachLocal = new Vector3(-1.5f,0,5f);
        public Vector3 InnPorchLocal = new Vector3(-1.5f,0,-2f);
        public Vector3[] InnEntryStepPositions = {new Vector3(-1.5f,.1666665f,3f),new Vector3(-1.5f,.25f,2.3f),new Vector3(-1.5f,.3333335f,1.6f),new Vector3(-1.5f,.4166665f,.9f),new Vector3(-1.5f,.5f,.2f)};
        public Vector3[] InnEntryStepSizes = {new Vector3(2f,.333333f,.8f),new Vector3(2f,.5f,.8f),new Vector3(2f,.666667f,.8f),new Vector3(2f,.833333f,.8f),new Vector3(2f,1f,.8f)};
        [Min(0f)] public float InnLanternIntensity = 6f;
        [Min(.1f)] public float InnLanternRange = 7f;
        [Range(0f,1.2f)] public float InnLanternSourceIntensity = 1.2f;
        [Range(0f,1f)] public float InnLanternPaperMix = .5f;
        public Vector3 SunEuler = new Vector3(32f, 145f, 0f);
        public float SunIntensity = 0.9f;
        public int ScatteredRockCount = 130;
        public int PineCount = 24;
        public Vector3 Spawn = new Vector3(0f, 1.1f, -3f);
        public float WashStart = 25f;
        public float WashEnd = 480f;
        [HideInInspector, Tooltip("LEGACY: preserved for old material comparison only; canonical mountains use one shared surface.")]
        public CodexMountainInkLayer[] MountainInkLayers =
        {
            new CodexMountainInkLayer(1.42f, .16f, .008f, .19f, .045f, .025f, .060f, .30f),
            new CodexMountainInkLayer(1.35f, .18f, .025f, .25f, .09f, .018f, .045f, .25f),
            new CodexMountainInkLayer(1.25f, .20f, .06f, .33f, .15f, .012f, .030f, .18f),
            new CodexMountainInkLayer(1.15f, .23f, .11f, .42f, .22f, .009f, .022f, .12f)
        };
    }

    [System.Serializable]
    public sealed class CodexMountainInkLayer
    {
        [Range(.25f, 3f)] public float InkDensity;
        [Range(0f, 1f)] public float AmbientLevel;
        [Range(0f, 1f)] public float ToneFloor;
        [Range(0f, 1f)] public float ToneCeiling;
        [Range(0f, 1f)] public float WashStrength;
        [Min(.001f)] public float NoiseScale;
        [Min(.001f)] public float StrokeScale;
        [Range(0f, 1f)] public float StrokeStrength;

        public CodexMountainInkLayer(float inkDensity, float ambientLevel, float toneFloor, float toneCeiling,
            float washStrength, float noiseScale, float strokeScale, float strokeStrength)
        {
            InkDensity = inkDensity; AmbientLevel = ambientLevel; ToneFloor = toneFloor;
            ToneCeiling = toneCeiling; WashStrength = washStrength; NoiseScale = noiseScale;
            StrokeScale = strokeScale; StrokeStrength = strokeStrength;
        }
    }
}
