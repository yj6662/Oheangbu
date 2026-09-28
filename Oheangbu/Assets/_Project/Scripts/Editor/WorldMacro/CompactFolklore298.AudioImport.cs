using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactFolklore298
    {
        [Serializable] sealed class AudioIntake298
        {
            public int schema;
            public string status, provider, model_id, manifest_file, manifest_file_sha256, manifest_canonical_sha256;
            public AudioIntakeClip298[] clips;
        }
        [Serializable] sealed class AudioIntakeClip298
        {
            public string id, actor, role, file, sha256, request_file, request_sha256, request_id, clip_sha256;
            public string original_file, original_sha256, processing_file, processing_sha256, request_body_json, request_body_sha256;
            public float duration_seconds;
            public bool loop, provider_request_id_absent;
            public long bytes, original_bytes;
            public float gain;
        }
        [Serializable] sealed class AudioPcm298
        {
            public string Id, Asset, OriginalSha256, ImportedSha256, ProcessingSha256, RequestSha256, Profile, Role;
            public int Samples, Channels, Frequency, NonzeroSamples;
            public float Seconds, Peak, Rms, Dc;
        }
        [Serializable] sealed class AudioImportReceipt298
        {
            public string Utc, Status, Scope, IntakeSha256;
            public bool Passed, NativePcmVerified, ProfilesLinked, ListeningVerified, DspOutputVerified;
            public List<AudioPcm298> Clips = new List<AudioPcm298>();
            public List<string> MissingRoles = new List<string>(), Failures = new List<string>();
        }
        static readonly string[] AudioActors298 = { "dokkaebi", "agwi", "changgui", "bulgasari", "fox_spirit", "imugi", "cheongryong", "jangsu" };
        static readonly Dictionary<string, string> AudioFields298 = new Dictionary<string, string>
        { {"alert","Alert"}, {"windup","Windup"}, {"hit_a","HitA"}, {"hit_b","HitB"}, {"death","Death"} };

        // Explicit local entrypoint only. No API, scene mutation, playback or synthetic clip fallback.
        // All selected native PCM must pass before any profile slot changes. Content-addressed
        // staging assets preserve every previously linked clip if a new intake fails validation.
        public static string AudioImport(string argument)
        {
            string output = Path.Combine(OutputRoot, "Analysis/audio-import.json");
            if (argument == "status") return File.Exists(output) ? File.ReadAllText(output) : "No #298 audio import receipt.";
            RequireEdit();
            if (argument != "import" && argument != "audit") throw new ArgumentException("AudioImport: import, audit or status");
            bool importing = argument == "import";
            var report = new AudioImportReceipt298 { Utc = DateTime.UtcNow.ToString("O"),
                Scope = "Recorded ElevenLabs intake provenance plus actual Unity-decoded PCM and candidate profile references. No provider authentication, commercial-rights attestation, listening, true-peak, DSP playback or scene mutation." };
            try
            {
                string audioRoot = Path.Combine(OutputRoot, "Audio");
                if (File.Exists(Path.Combine(audioRoot, ".generation.lock"))) throw new Exception("Generation lock exists; reconcile it first.");
                string intakePath = Path.Combine(audioRoot, "validated-intake.json");
                if (!File.Exists(intakePath)) throw new FileNotFoundException("Run folklore_sfx298.py validate-import with explicit generated IDs first.");
                report.IntakeSha256 = AudioFileHash298(intakePath);
                var intake = JsonUtility.FromJson<AudioIntake298>(File.ReadAllText(intakePath));
                PreflightAudioIntake298(intake, audioRoot);
                var clips = new Dictionary<string, AudioClip>();
                foreach (var item in intake.clips)
                {
                    string asset = AssetRoot + "/Audio/Generated/" + item.id + "_" + item.sha256.Substring(0, 16) + Path.GetExtension(item.file);
                    if (importing)
                    {
                        Folder(AssetRoot + "/Audio/Generated");
                        if (File.Exists(asset) && AudioFileHash298(asset) != item.sha256) throw new Exception("Candidate content-addressed asset collision: " + item.id);
                        if (!File.Exists(asset)) File.Copy(Path.Combine(audioRoot, item.file), asset, false);
                        AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
                        var importer = AssetImporter.GetAtPath(asset) as AudioImporter;
                        if (importer == null) throw new Exception("Native audio importer missing: " + item.id);
                        var settings = importer.defaultSampleSettings;
                        settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
                        settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate; settings.sampleRateOverride = 48000; settings.preloadAudioData = true;
                        importer.defaultSampleSettings = settings; importer.forceToMono = true; importer.loadInBackground = false;
                        // Normalize is inspector-serialized rather than a public AudioImporter API.
                        var serialized = new SerializedObject(importer); var normalize = serialized.FindProperty("m_Normalize");
                        if (normalize == null || normalize.propertyType != SerializedPropertyType.Boolean) throw new Exception("Audio normalization property unavailable.");
                        normalize.boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo();
                        importer.SaveAndReimport();
                    }
                    if (!File.Exists(asset) || AudioFileHash298(asset) != item.sha256) throw new Exception("Imported original is missing or changed: " + item.id);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(asset);
                    report.Clips.Add(CheckNativeAudio298(item, asset, clip)); clips.Add(item.id, clip);
                }
                // Recheck source files after decoding, before profile writes.
                PreflightAudioIntake298(intake, audioRoot);
                if (AudioFileHash298(intakePath) != report.IntakeSha256) throw new Exception("Intake changed during validation.");
                report.NativePcmVerified = true;
                if (importing) LinkAudioProfiles298(intake.clips, clips);
                foreach (var item in intake.clips)
                {
                    string profilePath = AssetRoot + "/Data/Audio_" + item.actor + ".asset";
                    var profile = AssetDatabase.LoadAssetAtPath<EnemyAudioProfile298>(profilePath);
                    var cue = profile != null ? AudioFields298[item.role] : null;
                    if (profile == null || profile.ActorId != item.actor || ((WorldMacroPlaytestAudioProfileSO.Cue)typeof(EnemyAudioProfile298).GetField(cue).GetValue(profile))?.Clip != clips[item.id])
                        throw new Exception("Candidate profile linkage mismatch: " + item.id);
                }
                foreach (var actor in AudioActors298)
                {
                    var profile = AssetDatabase.LoadAssetAtPath<EnemyAudioProfile298>(AssetRoot + "/Data/Audio_" + actor + ".asset");
                    string missing = profile != null ? profile.MissingClips : string.Join(",", AudioFields298.Keys);
                    if (!string.IsNullOrEmpty(missing)) report.MissingRoles.Add(actor + ":" + missing);
                }
                report.ProfilesLinked = true; report.Passed = true;
                report.Status = "PCM_AND_SELECTED_PROFILE_LINKS_PASS_LISTENING_PENDING";
            }
            catch (Exception error) { report.Status = "FAIL"; report.Failures.Add(error.GetType().Name + ": " + error.Message); }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            if (File.Exists(output))
            {
                string history = Path.Combine(OutputRoot, "History/AudioImport"); Directory.CreateDirectory(history);
                File.Copy(output, Path.Combine(history, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + ".json"));
            }
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            return JsonUtility.ToJson(report, true);
        }

        static void PreflightAudioIntake298(AudioIntake298 intake, string audioRoot)
        {
            if (intake == null || intake.schema != 2 || intake.status != "VERIFIED_PREPARED_INTAKE_NATIVE_PCM_PENDING" || intake.provider != "ElevenLabs" ||
                intake.model_id != "eleven_text_to_sound_v2" || intake.manifest_file != "manifest.json" || !AudioSha298(intake.manifest_file_sha256) ||
                !AudioSha298(intake.manifest_canonical_sha256) || intake.clips == null || intake.clips.Length == 0)
                throw new Exception("Unsupported verified audio intake.");
            string manifestPath = Path.Combine(audioRoot, "manifest.json");
            if (AudioFileHash298(manifestPath) != intake.manifest_file_sha256) throw new Exception("Audio manifest changed after intake verification.");
            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            if ((string)manifest["provider"] != intake.provider || (string)manifest["model_id"] != intake.model_id || (string)manifest["output_format"] != "mp3_44100_128")
                throw new Exception("Audio manifest provider/model mismatch.");
            var ids = new HashSet<string>(); var slots = new HashSet<string>();
            foreach (var item in intake.clips)
            {
                if (item == null || !AudioActors298.Contains(item.actor) || !AudioFields298.ContainsKey(item.role) ||
                    item.id != item.actor + "_" + item.role || !ids.Add(item.id) || !slots.Add(item.actor + "/" + item.role) || item.loop ||
                    !float.IsFinite(item.duration_seconds) || item.duration_seconds < .5f || item.duration_seconds > 30 ||
                    !AudioSha298(item.sha256) || !AudioSha298(item.request_sha256) || !AudioSha298(item.clip_sha256) || !AudioSha298(item.original_sha256) ||
                    !AudioSha298(item.processing_sha256) || !AudioSha298(item.request_body_sha256) ||
                    item.file != "Prepared/" + item.id + ".wav" || item.original_file != "Originals/" + item.id + ".mp3" ||
                    item.processing_file != "Processing/" + item.id + ".json" || item.request_file != "Requests/" + item.id + ".json" ||
                    item.provider_request_id_absent != string.IsNullOrEmpty(item.request_id) || !float.IsFinite(item.gain) || item.gain <= 0 || item.gain > 1)
                    throw new Exception("Invalid clip identity, hash, or unsupported loop. Initial profiles contain one-shot roles only.");
                var authored = ((JArray)manifest["clips"]).SingleOrDefault(t => (string)t["id"] == item.id);
                if (authored == null || (string)authored["actor"] != item.actor || (string)authored["role"] != item.role || (bool)authored["loop"] != item.loop ||
                    Math.Abs((double)authored["duration_seconds"] - item.duration_seconds) > .00001)
                    throw new Exception("Selected role differs from the authored manifest: " + item.id);
                string requestPath = Path.Combine(audioRoot, item.request_file), original = Path.Combine(audioRoot, item.original_file);
                string prepared = Path.Combine(audioRoot, item.file), processingPath = Path.Combine(audioRoot, item.processing_file);
                if (AudioFileHash298(requestPath) != item.request_sha256 || !File.Exists(original) || new FileInfo(original).Length != item.original_bytes ||
                    item.original_bytes < 128 || item.original_bytes > 16 * 1024 * 1024 || AudioFileHash298(original) != item.original_sha256 ||
                    !File.Exists(prepared) || new FileInfo(prepared).Length != item.bytes || item.bytes < 128 || item.bytes > 16 * 1024 * 1024 ||
                    AudioFileHash298(prepared) != item.sha256 || AudioFileHash298(processingPath) != item.processing_sha256)
                    throw new Exception("Generation receipt/original hash mismatch: " + item.id);
                var receipt = JObject.Parse(File.ReadAllText(requestPath));
                var expected = new JObject { ["text"] = authored["text"].DeepClone(), ["duration_seconds"] = authored["duration_seconds"].DeepClone(),
                    ["prompt_influence"] = authored["prompt_influence"].DeepClone(), ["loop"] = authored["loop"].DeepClone(), ["model_id"] = intake.model_id };
                if ((string)receipt["id"] != item.id || (string)receipt["status"] != "SUCCEEDED" || (string)receipt["sha256"] != item.original_sha256 ||
                    (string)receipt["file"] != item.original_file || (long)receipt["bytes"] != item.original_bytes ||
                    ((string)receipt["request_id"] ?? "") != item.request_id ||
                    (receipt["request_id"] == null) != item.provider_request_id_absent ||
                    (string)receipt["manifest_sha256"] != intake.manifest_canonical_sha256 || (string)receipt["clip_sha256"] != item.clip_sha256 || !JToken.DeepEquals(receipt["request"], expected))
                    throw new Exception("Successful provider provenance does not match selected audio: " + item.id);
                using (var hash = SHA256.Create())
                {
                    string actual = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(item.request_body_json ?? ""))).Replace("-", "").ToLowerInvariant();
                    if (actual != item.request_body_sha256 || !JToken.DeepEquals(JObject.Parse(item.request_body_json), expected))
                        throw new Exception("Recorded POST body SHA mismatch: " + item.id);
                }
                var processing = JObject.Parse(File.ReadAllText(processingPath));
                if ((int)processing["schema"] != 1 || (string)processing["id"] != item.id ||
                    (string)processing["algorithm"] != "stereo-mean-48k-s16-attenuate-only-v1" ||
                    (string)processing["original_file"] != item.original_file || (string)processing["original_sha256"] != item.original_sha256 ||
                    (string)processing["request_sha256"] != item.request_sha256 || (string)processing["file"] != item.file ||
                    (string)processing["sha256"] != item.sha256 || (long)processing["bytes"] != item.bytes ||
                    Math.Abs((double)processing["gain"] - item.gain) > .000001 || (int)processing["sample_rate"] != 48000 ||
                    (int)processing["channels"] != 1 || (int)processing["bits"] != 16 || (int)processing["paid_requests"] != 0)
                    throw new Exception("Original-to-prepared PCM provenance mismatch: " + item.id);
            }
        }
        static bool AudioSha298(string value) => value != null && value.Length == 64 && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        static AudioPcm298 CheckNativeAudio298(AudioIntakeClip298 item, string asset, AudioClip clip)
        {
            var importer = AssetImporter.GetAtPath(asset) as AudioImporter;
            if (clip == null || importer == null || importer.defaultSampleSettings.compressionFormat != AudioCompressionFormat.PCM ||
                importer.defaultSampleSettings.loadType != AudioClipLoadType.DecompressOnLoad || !importer.defaultSampleSettings.preloadAudioData)
                throw new Exception("PCM preload import contract missing: " + item.id);
            var normalization = new SerializedObject(importer).FindProperty("m_Normalize");
            if (!importer.forceToMono || importer.loadInBackground || normalization == null || normalization.boolValue)
                throw new Exception("Mono import must preserve original gain and load synchronously: " + item.id);
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            if (clip.loadState != AudioDataLoadState.Loaded || clip.channels != 1 || clip.frequency != 48000 || clip.samples <= 0 || clip.samples > 48000 * 31)
                throw new Exception("Native PCM is not fully loaded as 48k mono: " + item.id);
            float seconds = clip.samples / (float)clip.frequency;
            if (Mathf.Abs(seconds - item.duration_seconds) > Mathf.Max(.12f, item.duration_seconds * .1f)) throw new Exception("Decoded duration differs from the explicit request: " + item.id);
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) throw new Exception("Unity PCM GetData failed: " + item.id);
            double square = 0, sum = 0; float peak = 0; int nonzero = 0;
            foreach (float sample in data)
            {
                if (!float.IsFinite(sample)) throw new Exception("Nonfinite decoded PCM: " + item.id);
                peak = Mathf.Max(peak, Mathf.Abs(sample)); square += sample * (double)sample; sum += sample;
                if (Mathf.Abs(sample) > .00001f) nonzero++;
            }
            float rms = (float)Math.Sqrt(square / data.Length), dc = (float)(sum / data.Length);
            if (peak > .995f || peak < .0001f || rms < .00001f || nonzero < 48 || Mathf.Abs(dc) > .05f)
                throw new Exception("PCM is silent, clipped, too short in activity, or has excessive DC: " + item.id);
            return new AudioPcm298 { Id=item.id, Asset=asset, OriginalSha256=item.original_sha256, ImportedSha256=item.sha256,
                ProcessingSha256=item.processing_sha256, RequestSha256=item.request_sha256,
                Profile=AssetRoot+"/Data/Audio_"+item.actor+".asset", Role=item.role, Samples=clip.samples, Channels=clip.channels, Frequency=clip.frequency,
                Seconds=seconds, Peak=peak, Rms=rms, Dc=dc, NonzeroSamples=nonzero };
        }
        static void LinkAudioProfiles298(AudioIntakeClip298[] items, Dictionary<string, AudioClip> clips)
        {
            var originals = new Dictionary<EnemyAudioProfile298, string>(); var created = new List<string>();
            try
            {
                foreach (string actor in items.Select(i => i.actor).Distinct())
                {
                    string path = AssetRoot + "/Data/Audio_" + actor + ".asset";
                    var profile = AssetDatabase.LoadAssetAtPath<EnemyAudioProfile298>(path);
                    if (profile != null && !string.IsNullOrEmpty(profile.ActorId) && profile.ActorId != actor) throw new Exception("Existing candidate profile has a different actor identity.");
                    if (profile == null) { profile = DataAsset("Audio_" + actor, () => ScriptableObject.CreateInstance<EnemyAudioProfile298>()); created.Add(path); }
                    else originals.Add(profile, EditorJsonUtility.ToJson(profile));
                    profile.ActorId = actor;
                    foreach (var item in items.Where(i => i.actor == actor))
                    {
                        var field = typeof(EnemyAudioProfile298).GetField(AudioFields298[item.role]);
                        var cue = field.GetValue(profile) as WorldMacroPlaytestAudioProfileSO.Cue;
                        if (cue == null) throw new Exception("Existing candidate cue settings missing; repair explicitly before import.");
                        cue.Clip = clips[item.id];
                    }
                    EditorUtility.SetDirty(profile);
                }
                foreach (var profile in originals.Keys) AssetDatabase.SaveAssetIfDirty(profile);
                foreach (string path in created) AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<EnemyAudioProfile298>(path));
            }
            catch
            {
                foreach (var pair in originals) { EditorJsonUtility.FromJsonOverwrite(pair.Value, pair.Key); EditorUtility.SetDirty(pair.Key); AssetDatabase.SaveAssetIfDirty(pair.Key); }
                foreach (string path in created) AssetDatabase.DeleteAsset(path);
                throw;
            }
        }
    }
}
