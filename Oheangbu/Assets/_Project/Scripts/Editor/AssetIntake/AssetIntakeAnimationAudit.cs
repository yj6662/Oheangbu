using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    // SPEC-ASSET-INTAKE. Read imported clip data without reimporting or sampling a prefab.
    public static class AssetIntakeAnimationAudit
    {
        private const string SourceFolder = "Assets/KTinteractiveProp/Volum 02/Meshes/";

        [Serializable]
        public sealed class Report
        {
            public string created;
            public string scope = "Eight animation-only FBX sources and their paired geometry/animation sources.";
            public string validation = "Imported data inspection only. Animator playback, binding to a scene instance, "
                + "event execution, and visual motion were not tested.";
            public List<PairInfo> items = new List<PairInfo>();
        }

        [Serializable]
        public sealed class PairInfo
        {
            public int catalogIndex;
            public string category, status, note;
            public SourceInfo animationSource, pairedSource;
        }

        [Serializable]
        public sealed class SourceInfo
        {
            public string path, guid, status, error, animationType, avatarSource;
            public bool importAnimation, configuredTakeMismatch;
            public int clipCount, previewClipCount, clipsWithUsableData;
            public List<ClipImportInfo> configuredClips = new List<ClipImportInfo>();
            public List<ClipImportInfo> defaultClips = new List<ClipImportInfo>();
            public List<ClipInfo> clips = new List<ClipInfo>();
        }

        [Serializable]
        public sealed class ClipImportInfo
        {
            public string name, takeName;
            public float firstFrame, lastFrame;
        }

        [Serializable]
        public sealed class ClipInfo
        {
            public string name, guid, status, error;
            public long localId;
            public float length, frameRate;
            public bool previewOnly, importedDataPresent;
            public int curveBindings, objectReferenceCurveBindings, eventCount;
            public string[] animatedPaths;
            public List<EventInfo> events = new List<EventInfo>();
        }

        [Serializable]
        public sealed class EventInfo
        {
            public float time, floatParameter;
            public int intParameter;
            public string functionName, stringParameter, objectReferencePath, objectReferenceName;
        }

        public static string Run()
        {
            var report = new Report { created = DateTimeOffset.Now.ToString("o") };
            string[] names = { "BesideTable", "Box", "Closet", "HalfChest", "Mirror", "ShelfCabinet", "StorageCabinets", "Tabletop" };
            for (int i = 0; i < names.Length; i++)
            {
                bool mirror = names[i] == "Mirror";
                var item = new PairInfo
                {
                    catalogIndex = 1583 + i,
                    category = mirror ? "애니메이션/거울 접기" : "애니메이션/가구 개폐",
                    animationSource = ReadSource(SourceFolder + "SK_" + names[i] + (mirror ? "_Hide.FBX" : "_Open.FBX")),
                    pairedSource = ReadSource(SourceFolder + "SK_" + names[i] + (mirror ? "_Flip.FBX" : "_Close.FBX")),
                    note = "Source binary inspection found bones and animation curves but no Geometry in the Open/Hide file. "
                        + "Its paired Close/Flip FBX supplies the geometry and avatar used by the existing prefabs. "
                        + "Positive duration and curve bindings confirm imported animation data, not a playback pass."
                };
                item.status = item.animationSource.status == "IMPORTED_DATA_PRESENT"
                    && item.pairedSource.status == "IMPORTED_DATA_PRESENT"
                    ? "IMPORTED_DATA_PRESENT_PLAYBACK_NOT_TESTED" : "IMPORT_DATA_ISSUE";
                if (item.animationSource.configuredTakeMismatch || item.pairedSource.configuredTakeMismatch)
                    item.note += " A configured take name does not match the importer's current default takes; "
                        + "inspect the recorded native clip duration and bindings before deciding whether import repair is needed.";
                report.items.Add(item);
            }

            string unityRoot = Directory.GetParent(Application.dataPath).FullName;
            string repoRoot = Directory.GetParent(unityRoot).FullName;
            string output = Path.Combine(repoRoot, "Docs/Assets/AnimationSources.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            int usable = report.items.Count(p => p.status == "IMPORTED_DATA_PRESENT_PLAYBACK_NOT_TESTED");
            int takeWarnings = report.items.Count(p => p.animationSource.configuredTakeMismatch || p.pairedSource.configuredTakeMismatch);
            return $"Animation source audit: {usable}/{report.items.Count} pairs contain imported duration/curve data; "
                + $"{report.items.Count - usable} pairs need import investigation; {takeWarnings} pairs have configured take mismatches. "
                + "Playback was not tested. See Docs/Assets/AnimationSources.json.";
        }

        private static SourceInfo ReadSource(string path)
        {
            var info = new SourceInfo { path = path, guid = AssetDatabase.AssetPathToGUID(path) };
            try
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) throw new InvalidOperationException("ModelImporter is absent for the expected source.");
                info.importAnimation = importer.importAnimation;
                info.animationType = importer.animationType.ToString();
                info.avatarSource = importer.sourceAvatar == null ? "" : AssetDatabase.GetAssetPath(importer.sourceAvatar);
                info.configuredClips = ImportInfo(importer.clipAnimations);
                info.defaultClips = ImportInfo(importer.defaultClipAnimations);
                var availableTakes = new HashSet<string>(info.defaultClips.Select(c => c.takeName), StringComparer.Ordinal);
                info.configuredTakeMismatch = info.configuredClips.Any(c => !string.IsNullOrEmpty(c.takeName)
                    && !availableTakes.Contains(c.takeName));

                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().OrderBy(c => c.name, StringComparer.Ordinal))
                    info.clips.Add(ReadClip(clip));
                info.previewClipCount = info.clips.Count(c => c.previewOnly);
                info.clipCount = info.clips.Count - info.previewClipCount;
                info.clipsWithUsableData = info.clips.Count(c => !c.previewOnly && c.importedDataPresent);
                if (!info.importAnimation) info.status = "ANIMATION_IMPORT_DISABLED";
                else if (info.clipCount == 0) info.status = "NO_IMPORTED_ANIMATION_CLIP";
                else if (info.clipsWithUsableData != info.clipCount) info.status = "EMPTY_OR_UNREADABLE_CLIP_DATA";
                else info.status = "IMPORTED_DATA_PRESENT";
            }
            catch (Exception ex)
            {
                info.status = "IMPORT_READ_ERROR";
                info.error = ex.GetType().Name + ": " + ex.Message;
            }
            return info;
        }

        private static List<ClipImportInfo> ImportInfo(ModelImporterClipAnimation[] clips)
        {
            return (clips ?? Array.Empty<ModelImporterClipAnimation>()).Select(c => new ClipImportInfo
            {
                name = c.name, takeName = c.takeName, firstFrame = c.firstFrame, lastFrame = c.lastFrame
            }).ToList();
        }

        private static ClipInfo ReadClip(AnimationClip clip)
        {
            var info = new ClipInfo { name = clip.name, previewOnly = clip.name.StartsWith("__preview__", StringComparison.Ordinal) };
            try
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long localId);
                info.guid = guid;
                info.localId = localId;
                info.length = clip.length;
                info.frameRate = clip.frameRate;
                var curves = AnimationUtility.GetCurveBindings(clip);
                var objectCurves = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                info.curveBindings = curves.Length;
                info.objectReferenceCurveBindings = objectCurves.Length;
                info.animatedPaths = curves.Select(c => c.path).Concat(objectCurves.Select(c => c.path))
                    .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
                foreach (var evt in AnimationUtility.GetAnimationEvents(clip))
                    info.events.Add(new EventInfo
                    {
                        time = evt.time, functionName = evt.functionName, stringParameter = evt.stringParameter,
                        floatParameter = evt.floatParameter, intParameter = evt.intParameter,
                        objectReferencePath = evt.objectReferenceParameter == null ? "" : AssetDatabase.GetAssetPath(evt.objectReferenceParameter),
                        objectReferenceName = evt.objectReferenceParameter == null ? "" : evt.objectReferenceParameter.name
                    });
                info.eventCount = info.events.Count;
                info.importedDataPresent = info.length > 0f && info.frameRate > 0f && info.curveBindings + info.objectReferenceCurveBindings > 0;
                info.status = info.previewOnly ? "PREVIEW_CLIP_EXCLUDED" : info.importedDataPresent
                    ? "IMPORTED_DATA_PRESENT_PLAYBACK_NOT_TESTED" : "EMPTY_ANIMATION_DATA";
            }
            catch (Exception ex)
            {
                info.status = "CLIP_READ_ERROR";
                info.error = ex.GetType().Name + ": " + ex.Message;
            }
            return info;
        }
    }
}
