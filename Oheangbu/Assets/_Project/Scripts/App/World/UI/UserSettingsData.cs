using System;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    [Serializable]
    public sealed class UserSettingsData
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        [Range(0f, 1f)] public float MasterVolume = 1f;
        [Range(0f, 1f)] public float GameplayVolume = 1f;
        [Range(0f, 1f)] public float UiVolume = 1f;
        public int ScreenWidth = 1920;
        public int ScreenHeight = 1080;
        public FullScreenMode WindowMode = FullScreenMode.FullScreenWindow;
        public int QualityLevel;
        [Range(0, 4)] public int VSyncCount = 1;
        public int TargetFrameRate = -1;
        [Range(.1f, 4f)] public float LookSensitivity = 1f;
        public bool InvertLookY;
        [Range(.75f, 2f)] public float UiScale = 1f;
        [Range(.75f, 2f)] public float TextScale = 1f;
        public bool ShowMinimap = true;      // #306: the ink-circle minimap (HudMinimap304); no longer drives the bearing line
        public bool MinimapFollowView;       // #306: false = north up (the arrow turns), true = the map turns with the view
        public bool ShowBearingLine = true;  // #306: the bearing ink line; missing in older files -> JsonUtility keeps these defaults
        public bool ReducedMotion;

        public UserSettingsData Clone()
        {
            return (UserSettingsData)MemberwiseClone();
        }

        public static bool Valid(UserSettingsData value)
        {
            return value != null && value.Version == CurrentVersion
                && Finite01(value.MasterVolume) && Finite01(value.GameplayVolume) && Finite01(value.UiVolume)
                && value.ScreenWidth >= 640 && value.ScreenWidth <= 16384
                && value.ScreenHeight >= 360 && value.ScreenHeight <= 16384
                && Enum.IsDefined(typeof(FullScreenMode), value.WindowMode)
                && value.QualityLevel >= 0 && value.QualityLevel <= 64
                && value.VSyncCount >= 0 && value.VSyncCount <= 4
                && (value.TargetFrameRate == -1 || value.TargetFrameRate >= 15 && value.TargetFrameRate <= 1000)
                && float.IsFinite(value.LookSensitivity) && value.LookSensitivity >= .1f && value.LookSensitivity <= 4f
                && float.IsFinite(value.UiScale) && value.UiScale >= .75f && value.UiScale <= 2f
                && float.IsFinite(value.TextScale) && value.TextScale >= .75f && value.TextScale <= 2f;
        }

        public static UserSettingsData Sanitize(UserSettingsData value, UserSettingsData fallback)
        {
            UserSettingsData source = value ?? fallback ?? new UserSettingsData();
            UserSettingsData result = source.Clone();
            result.Version = CurrentVersion;
            result.MasterVolume = Mathf.Clamp01(FiniteOr(result.MasterVolume, 1f));
            result.GameplayVolume = Mathf.Clamp01(FiniteOr(result.GameplayVolume, 1f));
            result.UiVolume = Mathf.Clamp01(FiniteOr(result.UiVolume, 1f));
            result.ScreenWidth = Mathf.Clamp(result.ScreenWidth, 640, 16384);
            result.ScreenHeight = Mathf.Clamp(result.ScreenHeight, 360, 16384);
            if (!Enum.IsDefined(typeof(FullScreenMode), result.WindowMode))
                result.WindowMode = fallback != null ? fallback.WindowMode : FullScreenMode.FullScreenWindow;
            int qualityCount = Mathf.Max(1, QualitySettings.names.Length);
            result.QualityLevel = Mathf.Clamp(result.QualityLevel, 0, qualityCount - 1);
            result.VSyncCount = Mathf.Clamp(result.VSyncCount, 0, 4);
            if (result.TargetFrameRate != -1) result.TargetFrameRate = Mathf.Clamp(result.TargetFrameRate, 15, 1000);
            result.LookSensitivity = Mathf.Clamp(FiniteOr(result.LookSensitivity, 1f), .1f, 4f);
            result.UiScale = Mathf.Clamp(FiniteOr(result.UiScale, 1f), .75f, 2f);
            result.TextScale = Mathf.Clamp(FiniteOr(result.TextScale, 1f), .75f, 2f);
            return result;
        }

        private static bool Finite01(float value) => float.IsFinite(value) && value >= 0f && value <= 1f;
        private static float FiniteOr(float value, float fallback) => float.IsFinite(value) ? value : fallback;
    }
}
