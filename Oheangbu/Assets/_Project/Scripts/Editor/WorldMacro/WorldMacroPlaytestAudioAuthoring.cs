using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class WorldMacroPlaytestAudioAuthoring
    {
        public const string AudioFolder = "Assets/_Project/Audio/PlaytestFeedback";
        public const string ProfilePath = AudioFolder + "/WorldMacroPlaytestAudioProfile.asset";

        private static readonly string[] ClipNames =
        {
            "brush_stroke", "cast_wood", "cast_fire", "cast_earth", "cast_metal", "cast_water",
            "impact", "player_hit", "parry", "harvest", "interact", "rest", "summon_appear", "summon_release"
        };

        [Serializable]
        private sealed class ClipDiagnostic
        {
            public string name;
            public string path;
            public float seconds;
            public int channels;
            public int frequency;
        }

        [Serializable]
        private sealed class DiagnosticReport
        {
            public string scope = "W_WorldMacro_Playtest event-driven SFX only";
            public string validationMode = "Edit-mode clip metadata and serialized wiring inspection; no direct cue playback and no gameplay event is claimed.";
            public string runtimeEvidence = "Read WorldMacroPlaytestAudio.RuntimeCounters before/after live events. Rejected casts must not increment acceptedCast; direct clip audition is not event-path evidence.";
            public int voiceLimit;
            public int componentCount;
            public float totalSeconds;
            public ClipDiagnostic[] clips = Array.Empty<ClipDiagnostic>();
            public string validation;
            public string[] eventMapping =
            {
                "DrawingInputController.StrokeStarted -> brush_stroke",
                "CombatLoopWiring.CastAccepted -> cast_<element>; mapped Summon uses 0.35 gain",
                "CombatLoopWiring.EnemyHitResolved -> impact",
                "PlayerVitals.Damaged(amount>0) -> player_hit",
                "CombatLoopWiring.ParryResolved(Success/Half/Block) -> parry",
                "HarvestAction.Extracted -> harvest with 1.2s cooldown",
                "WorldMacroPlaytestSession.InteractionResolved -> interact or rest",
                "BrushStrokeFeedAdapter.SummonPresentationStarted/Released -> summon_appear/summon_release"
            };
            public string[] limitations =
            {
                "Generated clip character and final mix loudness require user listening review.",
                "This diagnostic does not simulate handwriting, combat, interaction, pause, or summon lifetime events."
            };
        }

        [Serializable]
        private sealed class ListenerRecord
        {
            public string scene;
            public string owner;
            public bool enabled;
            public bool activeInHierarchy;
            public bool activeAndEnabled;
            public bool ownsEnabledCamera;
            public bool matchesMainCamera;
            public bool matchesWalkerViewCamera;
        }

        [Serializable]
        private sealed class ListenerReport
        {
            public string mode;
            public string activeScene;
            public string mainCamera;
            public string walkerViewCamera;
            public int listenerCount;
            public int activeEnabledCount;
            public ListenerRecord[] listeners = Array.Empty<ListenerRecord>();
        }

        public static string Execute(string argument)
        {
            switch ((argument ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "install": return Install();
                case "validate": return Validate();
                case "diagnostics": return Diagnostics();
                case "runtime": return RuntimeDiagnostics();
                case "listeners": return ListenerInventory();
                default: throw new ArgumentException("Audio command must be install, validate, diagnostics, runtime, or listeners.");
            }
        }

        public static string Install()
        {
            RequireScene();
            if (!AssetDatabase.IsValidFolder(AudioFolder))
                throw new DirectoryNotFoundException("Generated audio folder is missing: " + AudioFolder);
            WorldMacroPlaytestAudioProfileSO profile = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestAudioProfileSO>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<WorldMacroPlaytestAudioProfileSO>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            profile.BrushStroke.Clip = Clip("brush_stroke");
            profile.CastWood.Clip = Clip("cast_wood");
            profile.CastFire.Clip = Clip("cast_fire");
            profile.CastEarth.Clip = Clip("cast_earth");
            profile.CastMetal.Clip = Clip("cast_metal");
            profile.CastWater.Clip = Clip("cast_water");
            profile.Impact.Clip = Clip("impact");
            profile.PlayerHit.Clip = Clip("player_hit");
            profile.Parry.Clip = Clip("parry");
            profile.Harvest.Clip = Clip("harvest");
            profile.Interact.Clip = Clip("interact");
            profile.Rest.Clip = Clip("rest");
            profile.SummonAppear.Clip = Clip("summon_appear");
            profile.SummonRelease.Clip = Clip("summon_release");
            EditorUtility.SetDirty(profile);

            WorldMacroPlaytestSession session = NeedSingle<WorldMacroPlaytestSession>();
            WorldMacroCombatWalker walker = session.Walker;
            if (walker == null) throw new InvalidOperationException("World macro walker is missing.");
            WorldMacroPlaytestAudio[] existing = Object.FindObjectsByType<WorldMacroPlaytestAudio>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (existing.Length > 1) throw new InvalidOperationException("More than one playtest audio component exists.");
            WorldMacroPlaytestAudio audio = existing.Length == 1 ? existing[0]
                : session.gameObject.AddComponent<WorldMacroPlaytestAudio>();

            BrushStrokeFeedAdapter brushAdapter = NeedSingle<BrushStrokeFeedAdapter>();
            PlayerVitals playerVitals = walker.Body != null ? walker.Body.GetComponent<PlayerVitals>() : null;
            HarvestAction harvest = walker.Motor != null ? walker.Motor.GetComponent<HarvestAction>() : null;
            if (walker.Wiring == null || walker.Drawing == null || playerVitals == null || harvest == null)
                throw new InvalidOperationException("Playtest audio event sources are incomplete.");

            Assign(audio, "_profile", profile);
            Assign(audio, "_session", session);
            Assign(audio, "_wiring", walker.Wiring);
            Assign(audio, "_drawing", walker.Drawing);
            Assign(audio, "_brushAdapter", brushAdapter);
            Assign(audio, "_playerVitals", playerVitals);
            Assign(audio, "_harvest", harvest);
            EditorUtility.SetDirty(audio);
            string listenerChanges = EnsurePlaytestListener(walker);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return "Installed one 12-voice event-driven playtest audio subscriber. " + listenerChanges + " " + Validate();
        }

        public static string Validate()
        {
            RequireScene();
            WorldMacroPlaytestAudioProfileSO profile = Need<WorldMacroPlaytestAudioProfileSO>(ProfilePath);
            AudioClip[] clips = ClipNames.Select(Clip).ToArray();
            if (clips.Distinct().Count() != ClipNames.Length)
                throw new InvalidOperationException("The audio profile must use fourteen distinct source clips.");
            if (clips.Sum(clip => clip.length) > 30.01f)
                throw new InvalidOperationException("Generated SFX exceed the authorized thirty-second total.");
            if (clips.Any(clip => clip.channels != 1 || clip.frequency != 44100))
                throw new InvalidOperationException("Generated SFX must remain mono 44.1 kHz after import.");

            NeedCue(profile.BrushStroke, Clip("brush_stroke"), false, "brush stroke");
            NeedCue(profile.CastWood, Clip("cast_wood"), false, "wood cast");
            NeedCue(profile.CastFire, Clip("cast_fire"), false, "fire cast");
            NeedCue(profile.CastEarth, Clip("cast_earth"), false, "earth cast");
            NeedCue(profile.CastMetal, Clip("cast_metal"), false, "metal cast");
            NeedCue(profile.CastWater, Clip("cast_water"), false, "water cast");
            NeedCue(profile.Impact, Clip("impact"), true, "enemy impact");
            NeedCue(profile.PlayerHit, Clip("player_hit"), false, "player hit");
            NeedCue(profile.Parry, Clip("parry"), true, "parry impact");
            NeedCue(profile.Harvest, Clip("harvest"), true, "harvest");
            NeedCue(profile.Interact, Clip("interact"), true, "interaction");
            NeedCue(profile.Rest, Clip("rest"), true, "rest");
            NeedCue(profile.SummonAppear, Clip("summon_appear"), true, "summon appearance");
            NeedCue(profile.SummonRelease, Clip("summon_release"), true, "summon release");
            if (profile.Harvest.Cooldown < 1f)
                throw new InvalidOperationException("Harvest cue cooldown must bound continuous extraction events.");

            WorldMacroPlaytestSession session = NeedSingle<WorldMacroPlaytestSession>();
            WorldMacroPlaytestAudio audio = NeedSingle<WorldMacroPlaytestAudio>();
            NeedReference(audio, "_profile", profile);
            NeedReference(audio, "_session", session);
            NeedReference(audio, "_wiring", session.Walker.Wiring);
            NeedReference(audio, "_drawing", session.Walker.Drawing);
            NeedReference(audio, "_playerVitals", session.Walker.Body.GetComponent<PlayerVitals>());
            NeedReference(audio, "_harvest", session.Walker.Motor.GetComponent<HarvestAction>());
            NeedReference(audio, "_brushAdapter", NeedSingle<BrushStrokeFeedAdapter>());
            ValidatePlaytestListener(session.Walker);
            return "VALID: 14 mono 44.1 kHz clips <=30s, one profile/component, 12 pooled voices, spatial split, bounded harvest, one active listener on Walker.ViewCamera.";
        }

        public static string Diagnostics()
        {
            string validation = Validate();
            AudioClip[] clips = ClipNames.Select(Clip).ToArray();
            var report = new DiagnosticReport
            {
                voiceLimit = WorldMacroPlaytestAudio.VoiceLimit,
                componentCount = Object.FindObjectsByType<WorldMacroPlaytestAudio>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                totalSeconds = clips.Sum(clip => clip.length),
                clips = clips.Select((clip, index) => new ClipDiagnostic
                {
                    name = ClipNames[index],
                    path = AssetDatabase.GetAssetPath(clip),
                    seconds = clip.length,
                    channels = clip.channels,
                    frequency = clip.frequency
                }).ToArray(),
                validation = validation
            };
            return JsonUtility.ToJson(report, true);
        }

        public static string RuntimeDiagnostics()
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Runtime audio counters require Play mode.");
            WorldMacroPlaytestAudio audio = NeedSingle<WorldMacroPlaytestAudio>();
            return "LIVE EVENT COUNTERS (not direct clip audition): " + audio.RuntimeCounters;
        }

        public static string ListenerInventory()
        {
            RequirePlaytestSceneLoaded();
            WorldMacroPlaytestSession[] sessions = Object.FindObjectsByType<WorldMacroPlaytestSession>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Camera viewCamera = sessions.Length == 1 && sessions[0].Walker != null
                ? sessions[0].Walker.ViewCamera : null;
            Camera mainCamera = Camera.main;
            AudioListener[] listeners = SceneListeners();
            var report = new ListenerReport
            {
                mode = EditorApplication.isPlaying ? "Play" : "Edit",
                activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
                mainCamera = CameraPath(mainCamera),
                walkerViewCamera = CameraPath(viewCamera),
                listenerCount = listeners.Length,
                activeEnabledCount = listeners.Count(IsActiveListener),
                listeners = listeners.Select(listener =>
                {
                    Camera ownerCamera = listener.GetComponent<Camera>();
                    return new ListenerRecord
                    {
                        scene = SceneLabel(listener.gameObject),
                        owner = HierarchyPath(listener.transform),
                        enabled = listener.enabled,
                        activeInHierarchy = listener.gameObject.activeInHierarchy,
                        activeAndEnabled = IsActiveListener(listener),
                        ownsEnabledCamera = ownerCamera != null && ownerCamera.enabled && ownerCamera.gameObject.activeInHierarchy,
                        matchesMainCamera = ownerCamera != null && ownerCamera == mainCamera,
                        matchesWalkerViewCamera = ownerCamera != null && ownerCamera == viewCamera
                    };
                }).ToArray()
            };
            return JsonUtility.ToJson(report, true);
        }

        private static string EnsurePlaytestListener(WorldMacroCombatWalker walker)
        {
            if (walker == null || walker.ViewCamera == null)
                throw new InvalidOperationException("Walker.ViewCamera is required for the playtest AudioListener.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            GameObject owner = walker.ViewCamera.gameObject;
            if (!owner.scene.IsValid() || owner.scene != scene)
                throw new InvalidOperationException("Walker.ViewCamera must be a scene instance in W_WorldMacro_Playtest.");

            AudioListener target = owner.GetComponent<AudioListener>();
            bool added = target == null;
            if (target == null) target = Undo.AddComponent<AudioListener>(owner);
            if (!target.enabled)
            {
                Undo.RecordObject(target, "Enable playtest view AudioListener");
                target.enabled = true;
                EditorUtility.SetDirty(target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }

            var disabled = new List<string>();
            foreach (AudioListener listener in SceneListeners())
            {
                if (listener == target || listener.gameObject.scene != scene || !listener.enabled) continue;
                disabled.Add(HierarchyPath(listener.transform));
                Undo.RecordObject(listener, "Disable competing playtest AudioListener");
                listener.enabled = false;
                EditorUtility.SetDirty(listener);
                PrefabUtility.RecordPrefabInstancePropertyModifications(listener);
            }
            EditorUtility.SetDirty(owner);
            return "Listener target=" + HierarchyPath(target.transform) + "; added=" + added
                + "; disabledSceneCompetitors=" + disabled.Count
                + (disabled.Count > 0 ? " [" + string.Join(", ", disabled) + "]" : string.Empty) + ".";
        }

        private static void ValidatePlaytestListener(WorldMacroCombatWalker walker)
        {
            if (walker == null || walker.ViewCamera == null)
                throw new InvalidOperationException("Walker.ViewCamera is required for listener validation.");
            AudioListener expected = walker.ViewCamera.GetComponent<AudioListener>();
            AudioListener[] active = SceneListeners().Where(IsActiveListener).ToArray();
            if (active.Length != 1)
                throw new InvalidOperationException("Expected exactly one active enabled scene AudioListener; found "
                    + active.Length + ". Inventory: " + ListenerInventory());
            if (expected == null || active[0] != expected)
                throw new InvalidOperationException("The sole active AudioListener is not owned by Walker.ViewCamera. Inventory: "
                    + ListenerInventory());
            if (Camera.main != walker.ViewCamera)
                throw new InvalidOperationException("Walker.ViewCamera is not the active MainCamera. Inventory: "
                    + ListenerInventory());
        }

        private static AudioListener[] SceneListeners()
        {
            return Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(listener => listener != null && listener.gameObject.scene.IsValid()
                    && listener.gameObject.scene.isLoaded)
                .OrderBy(listener => SceneLabel(listener.gameObject), StringComparer.Ordinal)
                .ThenBy(listener => HierarchyPath(listener.transform), StringComparer.Ordinal)
                .ToArray();
        }

        private static bool IsActiveListener(AudioListener listener)
        {
            return listener != null && listener.enabled && listener.gameObject.activeInHierarchy;
        }

        private static string CameraPath(Camera camera)
        {
            return camera != null ? SceneLabel(camera.gameObject) + ":" + HierarchyPath(camera.transform) : "none";
        }

        private static string SceneLabel(GameObject owner)
        {
            return !string.IsNullOrEmpty(owner.scene.path) ? owner.scene.path : owner.scene.name;
        }

        private static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        private static AudioClip Clip(string name)
        {
            return Need<AudioClip>("Assets/_Project/Audio/JourneyRenewal/" + name + ".wav");
        }

        private static void NeedCue(WorldMacroPlaytestAudioProfileSO.Cue cue, AudioClip clip,
            bool spatial, string label)
        {
            if (cue == null || cue.Clip != clip)
                throw new InvalidOperationException(label + " cue is not wired to its generated clip.");
            if (cue.Volume <= 0f || cue.Volume > 1f)
                throw new InvalidOperationException(label + " cue volume is outside 0..1.");
            if (spatial ? cue.SpatialBlend <= 0f : cue.SpatialBlend > 0f)
                throw new InvalidOperationException(label + (spatial ? " must be spatial." : " must be 2D."));
        }

        private static void RequireScene()
        {
            RequirePlaytestSceneLoaded();
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Open W_WorldMacro_Playtest in Edit mode for audio authoring.");
        }

        private static void RequirePlaytestSceneLoaded()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Open W_WorldMacro_Playtest for audio listener inspection.");
        }

        private static T Need<T>(string path) where T : Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null) throw new FileNotFoundException("Missing required asset: " + path);
            return value;
        }

        private static T NeedSingle<T>() where T : Object
        {
            T[] values = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (values.Length != 1) throw new InvalidOperationException("Expected exactly one " + typeof(T).Name
                + "; found " + values.Length + ".");
            return values[0];
        }

        private static void Assign(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) throw new MissingFieldException(target.GetType().Name, field);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void NeedReference(Object target, string field, Object expected)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null || property.objectReferenceValue != expected)
                throw new InvalidOperationException(target.name + "." + field + " is not wired to "
                    + (expected != null ? expected.name : "null") + ".");
        }
    }
}
