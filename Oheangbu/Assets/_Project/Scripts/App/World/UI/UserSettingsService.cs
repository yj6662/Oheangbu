using System;
using System.IO;
using Oheangbu.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.World.UI
{
    /// <summary>Versioned user preferences, separate from world progress and safe to preview.</summary>
    [DefaultExecutionOrder(-9000), DisallowMultipleComponent]
    public sealed class UserSettingsService : MonoBehaviour
    {
        public const string SettingsFileName = "user-settings-v1.json";
        public const float DisplayPreviewSeconds = 15f;

        public GameplayRuntimeStateSO RuntimeState;
        public WorldMacroAudioMixProfileSO AudioMix;

        private AtomicJsonStore<UserSettingsData> _store;
        private UserSettingsData _factoryDefaults;
        private UserSettingsData _confirmed;
        private UserSettingsData _current;
        private bool _previewing;
        private double _previewDeadline;
        private int _reportedPreviewSecond = -1;
        private bool _initialized;

        public UserSettingsData Current => (_current ?? _factoryDefaults ?? CaptureDefaults()).Clone();
        public UserSettingsData Confirmed => (_confirmed ?? _factoryDefaults ?? CaptureDefaults()).Clone();
        public bool IsPreviewing => _previewing;
        public float PreviewSecondsRemaining => !_previewing ? 0f
            : Mathf.Max(0f, (float)(_previewDeadline - Time.realtimeSinceStartupAsDouble));
        public float EffectiveGameplayVolume => Current.MasterVolume * Current.GameplayVolume;
        public float EffectiveUiVolume => Current.MasterVolume * Current.UiVolume;
        public string LoadStatus { get; private set; } = "uninitialized";
        public string SaveError { get; private set; }

        public event Action Changed;
        public event Action<bool> PreviewStateChanged;
        public event Action<float> PreviewSecondsChanged;

        private void Awake() { Initialize(); }
        private void OnEnable() { Initialize(); }

        public void Initialize()
        {
            if (_initialized)
            {
                ApplyRuntime(_current ?? _confirmed ?? _factoryDefaults, false);
                return;
            }

            _factoryDefaults = CaptureDefaults();
            _store = new AtomicJsonStore<UserSettingsData>(
                Path.Combine(Application.persistentDataPath, SettingsFileName), UserSettingsData.Valid);
            UserSettingsData loaded = _store.Load();
            LoadStatus = _store.LoadStatus;
            _confirmed = UserSettingsData.Sanitize(loaded ?? _factoryDefaults, _factoryDefaults);
            _current = _confirmed.Clone();
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            _initialized = true;
            ApplyRuntime(_current, true);
        }

        /// <summary>Applies and persists immediately, clearing any preview.</summary>
        public void Apply(UserSettingsData value)
        {
            EnsureInitialized();
            bool wasPreviewing = _previewing;
            _previewing = false;
            _confirmed = UserSettingsData.Sanitize(value, _factoryDefaults);
            _current = _confirmed.Clone();
            ApplyRuntime(_current, true);
            SaveConfirmed();
            if (wasPreviewing) PreviewStateChanged?.Invoke(false);
        }

        /// <summary>Applies an unsaved display/settings preview that rolls back after 15 real seconds.</summary>
        public void Preview(UserSettingsData value)
        {
            EnsureInitialized();
            bool wasPreviewing = _previewing;
            _current = UserSettingsData.Sanitize(value, _confirmed);
            _previewing = true;
            _previewDeadline = Time.realtimeSinceStartupAsDouble + DisplayPreviewSeconds;
            _reportedPreviewSecond = -1;
            ApplyRuntime(_current, true);
            if (!wasPreviewing) PreviewStateChanged?.Invoke(true);
            ReportPreviewSeconds();
        }

        public bool Confirm()
        {
            EnsureInitialized();
            if (!_previewing) return false;
            _confirmed = _current.Clone();
            _previewing = false;
            _reportedPreviewSecond = -1;
            SaveConfirmed();
            PreviewStateChanged?.Invoke(false);
            PreviewSecondsChanged?.Invoke(0f);
            Changed?.Invoke();
            return true;
        }

        public bool Revert()
        {
            EnsureInitialized();
            if (!_previewing) return false;
            _previewing = false;
            _reportedPreviewSecond = -1;
            _current = _confirmed.Clone();
            ApplyRuntime(_current, true);
            PreviewStateChanged?.Invoke(false);
            PreviewSecondsChanged?.Invoke(0f);
            return true;
        }

        /// <summary>Previews factory defaults; Confirm is required to persist them.</summary>
        public void Reset() { Preview(_factoryDefaults); }

        private void Update()
        {
            if (!_previewing) return;
            if (PreviewSecondsRemaining <= 0f) { Revert(); return; }
            ReportPreviewSeconds();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && _previewing) Revert();
        }

        private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            if (_previewing) { Revert(); return; }
            ApplyRuntime(_current, true);
        }

        private void ReportPreviewSeconds()
        {
            int second = Mathf.CeilToInt(PreviewSecondsRemaining);
            if (second == _reportedPreviewSecond) return;
            _reportedPreviewSecond = second;
            PreviewSecondsChanged?.Invoke(PreviewSecondsRemaining);
        }

        private void ApplyRuntime(UserSettingsData value, bool notify)
        {
            if (value == null) return;
            value = UserSettingsData.Sanitize(value, _factoryDefaults);
            _current = value.Clone();

            int quality = Mathf.Clamp(value.QualityLevel, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
            if (QualitySettings.GetQualityLevel() != quality) QualitySettings.SetQualityLevel(quality, true);
            QualitySettings.vSyncCount = value.VSyncCount;
            Application.targetFrameRate = value.TargetFrameRate;
            if (Screen.width != value.ScreenWidth || Screen.height != value.ScreenHeight
                || Screen.fullScreenMode != value.WindowMode)
                Screen.SetResolution(value.ScreenWidth, value.ScreenHeight, value.WindowMode);

            RuntimeState?.SetPreferences(value.LookSensitivity, value.InvertLookY, value.ReducedMotion);
            AudioMix?.ApplyUserVolumes(value.MasterVolume, value.GameplayVolume, value.UiVolume);
            foreach (WorldMacroPlaytestAudio audio in FindObjectsByType<WorldMacroPlaytestAudio>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
                audio.ApplyVolumeSettings(value.MasterVolume, value.GameplayVolume);
            if (notify) Changed?.Invoke();
        }

        private void SaveConfirmed()
        {
            try
            {
                _store.Save(_confirmed);
                SaveError = null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is ArgumentException)
            {
                SaveError = "설정 저장 실패: " + exception.Message;
                Debug.LogError(SaveError, this);
            }
        }

        private void EnsureInitialized()
        {
            if (!_initialized) Initialize();
        }

        private static UserSettingsData CaptureDefaults()
        {
            int width = Mathf.Max(640, Screen.width);
            int height = Mathf.Max(360, Screen.height);
            return new UserSettingsData
            {
                Version = UserSettingsData.CurrentVersion,
                ScreenWidth = width,
                ScreenHeight = height,
                WindowMode = Screen.fullScreenMode,
                QualityLevel = Mathf.Max(0, QualitySettings.GetQualityLevel()),
                VSyncCount = Mathf.Clamp(QualitySettings.vSyncCount, 0, 4),
                TargetFrameRate = Application.targetFrameRate,
            };
        }

        private void OnDisable()
        {
            if (_previewing)
            {
                _previewing = false;
                _current = _confirmed?.Clone();
                ApplyRuntime(_current, false);
            }
            if (_initialized) SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            _initialized = false;
        }
    }
}
