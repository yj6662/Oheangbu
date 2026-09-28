using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Oheangbu.App.World;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactFolklore298
    {
        const BindingFlags AudioPrivate298 = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] sealed class AudioActorAudit298
        {
            public string Actor, Profile, ActorId, MissingClips, EncounterState;
            public bool VitalsOnOwner, AssignedSoundscape, SharedMixer, Enabled;
        }
        [Serializable] sealed class AudioCheckReceipt298
        {
            public string StartedUtc, FinishedUtc, Scope, Scene, Status, SceneShaBefore, SceneShaAfter;
            public bool Finished, Passed, Restored, SyntheticDiagnosticClip = true, ActualElevenLabsAudio, DspOrListeningVerified;
            public int VoiceCount, ReservedVoices, NativeStarts, CombatTelegraphs, CombatEnds, DamageEvents, DeathEvents;
            public List<string> Checks = new List<string>(), Failures = new List<string>(), Limitations = new List<string>();
            public AudioActorAudit298[] SceneActors;
        }
        static string AudioChecksPath298 => Path.Combine(OutputRoot, "Analysis/audio-unity-checks.json");

        /// <summary>Actual Unity Edit/PreviewScene diagnostics. Never enters Play, saves a scene or changes game saves.</summary>
        public static string AudioChecks(string argument)
        {
            if (argument == "status") return File.Exists(AudioChecksPath298) ? File.ReadAllText(AudioChecksPath298) : "No #298 Unity audio diagnostics.";
            if (argument != "run" && argument != "audit") throw new ArgumentException("AudioChecks: run, audit or status");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Audio checks use an isolated Edit PreviewScene. Exit Play first.");
            var active = SceneManager.GetActiveScene();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var dirty = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).ToDictionary(s => s, s => s.isDirty);
            var random = UnityEngine.Random.state;
            var source = Object.FindObjectsByType<CompactSoundscape255>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.gameObject.scene == active && x.Palette != null);
            var report = new AudioCheckReceipt298 {
                StartedUtc = DateTime.UtcNow.ToString("O"), Scene = active.path, SceneShaBefore = AudioFileHash298(active.path),
                Scope = "Actual Unity engine objects in an isolated Edit PreviewScene. Real profile/emitter/shared16voice pool and existing SFX mixer routing; public EnemyVitals.TakeDamage/Restore and EnemyController.StopAttack/ResetEncounter. The private combat begin and lifecycle callback methods are invoked as fixture setup because Edit mode does not advance gameplay. No fabricated event invocation, production actor mutation, Play bootstrap, natural AI detection, DSP recording, ElevenLabs audio or listening certification."
            };
            report.SceneActors = Object.FindObjectsByType<EnemyAudioEmitter298>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(x => x.gameObject.scene == active).Select(x => new AudioActorAudit298 {
                    Actor = AudioHierarchy298(x.transform), Profile = AssetDatabase.GetAssetPath(x.Profile),
                    ActorId = x.Profile != null ? x.Profile.ActorId : "", MissingClips = x.Profile != null ? x.Profile.MissingClips : "MissingProfile",
                    VitalsOnOwner = x.GetComponent<EnemyVitals>() != null, AssignedSoundscape = x.Soundscape != null,
                    SharedMixer = x.Soundscape != null && x.Soundscape.Palette != null && x.Soundscape.Palette.Mix != null && x.Soundscape.Palette.Mix.Sfx != null,
                    Enabled = x.enabled, EncounterState = x.GetComponent<PrologueEncounter>() != null ? x.GetComponent<PrologueEncounter>().Current.ToString() : "NoPrologueEncounter"
                }).ToArray();
            report.Limitations.Add("Synthetic all-zero PCM diagnostic clip, Volume=0. Accepted source routing is not evidence of audible sound.");
            report.Limitations.Add("CompactSoundscape255.Start session/bootstrap and automatic Unity Play lifecycle remain separate tests. Private field injection only supplies the real shared pool and focus flag to an isolated service.");
            string encounterSource = Path.Combine(Application.dataPath, "_Project/Scripts/App/Prologue/PrologueEncounter.cs");
            string emitterSource = Path.Combine(Application.dataPath, "_Project/Scripts/App/World/EnemyAudioEmitter298.cs");
            bool encounterCallsAlert = File.Exists(encounterSource) && File.ReadAllText(encounterSource).Contains("NotifyAlert(");
            bool emitterTracksEncounter = File.Exists(emitterSource) && File.ReadAllText(emitterSource).Contains("PrologueEncounter");
            if (!encounterCallsAlert && !emitterTracksEncounter)
                report.Limitations.Add("INTEGRATION GAP: PrologueEncounter changes Patrol/Return/Chase after distance and line-of-sight checks, but neither that owner nor the emitter currently binds this real awareness transition to NotifyAlert. Rebind/restore must reset awareness history when this is integrated. Manual NotifyAlert is tested below; automatic alerts are not implemented.");
            else report.Limitations.Add("An awareness integration token is present in source. This is only an audit hint; natural Nav/line-of-sight Chase transitions are not certified by this fixture.");

            Scene preview = default;
            var assets = new List<Object>();
            WorldMacroAudioVoicePool pool = null;
            CompactSoundscape255 audio = null;
            try
            {
                if (argument == "run")
                {
                    if (source == null || source.Palette.Mix == null || source.Palette.Mix.Sfx == null)
                        throw new InvalidOperationException("The active saved world must contain a CompactSoundscape255 with its existing SFX mixer. No replacement mixer is invented.");
                    if (Time.timeScale <= .0001f) throw new InvalidOperationException("The fixture does not override global timeScale; restore it before this Edit diagnostic.");
                    preview = EditorSceneManager.NewPreviewScene();
                    var host = AudioObject298("Audio298_IsolatedFixture", preview);
                    audio = host.AddComponent<CompactSoundscape255>();
                    var clip = AudioClip.Create("SYNTHETIC_SILENT_AUDIO298_NOT_ELEVENLABS", 48000, 1, 48000, false); assets.Add(clip);
                    clip.SetData(new float[48000], 0);
                    var palette = ScriptableObject.CreateInstance<CompactSoundPalette255>(); assets.Add(palette);
                    palette.Mix = source.Palette.Mix;
                    palette.Entries = new[] {
                        new CompactSoundPalette255.Entry { Id="enemy_windup", Cue=AudioCue298(clip) },
                        new CompactSoundPalette255.Entry { Id="enemy_death", Cue=AudioCue298(clip) }
                    };
                    audio.Palette = palette;
                    pool = new WorldMacroAudioVoicePool(host.transform, 16, 4, false, "CompactSound255_");
                    AudioSet298(audio, "pool", pool); AudioSet298(audio, "focused", true);
                    report.VoiceCount = pool.Sources.Length;
                    report.ReservedVoices = (int)AudioGet298(pool, "_reserved");
                    AudioCheck298(report, report.VoiceCount == 16 && report.ReservedVoices == 4, "Actual bounded shared pool:16 sources/4 reserved.");
                    AudioCheck298(report, audio.EnemyAudioReady298, "Actual Soundscape role playback service ready in isolated fixture.");
                    var profile = ScriptableObject.CreateInstance<EnemyAudioProfile298>(); assets.Add(profile); profile.ActorId = "audio298_fixture";
                    profile.Alert = AudioCue298(clip); profile.Windup = AudioCue298(clip); profile.HitA = AudioCue298(clip); profile.HitB = AudioCue298(clip); profile.Death = AudioCue298(clip);
                    var config = ScriptableObject.CreateInstance<CombatConfigSO>(); assets.Add(config);
                    var attack = ScriptableObject.CreateInstance<EnemyAttackProfileSO>(); assets.Add(attack); attack.ApplyDefaults(EnemyArchetype.NeutralMelee); attack.Damage = 0;
                    var player = AudioObject298("Audio298_Target", preview); player.transform.position = new Vector3(0, 0, 1.2f);
                    var go = AudioObject298("Audio298_Actor_A", preview);
                    var vitals = go.AddComponent<EnemyVitals>(); AudioSet298(vitals, "_config", config); vitals.Restore();
                    var controller = go.AddComponent<EnemyController>(); AudioSet298(controller, "_config", config); AudioSet298(controller, "_player", player.transform); controller.Init(null); controller.Configure(attack);
                    var emitter = go.AddComponent<EnemyAudioEmitter298>(); emitter.Soundscape = audio; emitter.Profile = profile; emitter.Rebind();
                    controller.AttackTelegraphed += _ => report.CombatTelegraphs++;
                    controller.AttackPresentationEnded += (_, __) => report.CombatEnds++;
                    vitals.DamageResolved += _ => report.DamageEvents++;
                    vitals.Died += () => report.DeathEvents++;

                    AudioCheck298(report, emitter.Played == 0, "Rebind/activation emits no invented awareness event.");
                    AudioCheck298(report, emitter.NotifyAlert() && emitter.LastRole == "Alert", "Explicit awareness hook reaches the actual shared service.");
                    pool.StopAll(true);
                    int played = emitter.Played;
                    AudioInvoke298(controller, "BeginAuthoredTelegraph");
                    AudioCheck298(report, controller.IsTelegraphing && report.CombatTelegraphs == 1 && emitter.Played == played + 1, "Actual authored combat begin emits one windup.");
                    AudioCheck298(report, EnemyAudioEmitter298.OwnsCue(vitals, EnemyAudioRole298.Windup, audio), "Bound playable windup suppresses generic observer through the actual production predicate.");
                    AudioCheck298(report, pool.Sources.Any(s => s.clip == clip && s.outputAudioMixerGroup == source.Palette.Mix.Sfx), "Actual native source uses the existing SFX mixer group.");
                    controller.StopAttack();
                    AudioCheck298(report, report.CombatEnds == 1 && !controller.IsTelegraphing && AudioOwnerReleasing298(pool, emitter, profile.Windup), "Public StopAttack cancels this owner's actual windup request.");

                    pool.StopAll(true); played = emitter.Played;
                    vitals.TakeDamage(1); bool firstHit = AudioCurrentCues298(pool).Contains(profile.HitA);
                    vitals.TakeDamage(1); bool secondHit = AudioCurrentCues298(pool).Contains(profile.HitB);
                    AudioCheck298(report, report.DamageEvents == 2 && emitter.Played == played + 2 && firstHit && secondHit, "Two real nonlethal damage results alternate hit clips.");
                    pool.StopAll(true); played = emitter.Played; uint life = vitals.LifeRevision;
                    vitals.TakeDamage(vitals.MaxHp + 100);
                    AudioCheck298(report, !vitals.IsAlive && vitals.LifeRevision != life && report.DeathEvents == 1 && emitter.Played == played + 1 && emitter.LastRole == "Death", "Real lethal damage advances life revision and plays only one death.");
                    vitals.TakeDamage(1);
                    AudioCheck298(report, report.DeathEvents == 1 && emitter.Played == played + 1 && !emitter.NotifyAlert(), "Dead actor cannot produce repeated damage/death or awareness audio.");
                    controller.ResetEncounter(); AudioInvoke298(emitter, "Update"); pool.StopAll(true); played = emitter.Played;
                    AudioInvoke298(controller, "BeginAuthoredTelegraph");
                    AudioCheck298(report, vitals.IsAlive && emitter.BoundLifeRevision == vitals.LifeRevision && emitter.Played == played + 1, "Public encounter reset/Restore re-arms a new actual attack.");
                    controller.StopAttack();

                    // Edit does not schedule ordinary MonoBehaviour lifecycle callbacks. Invoke the real methods explicitly.
                    AudioInvoke298(emitter, "OnDisable"); AudioInvoke298(vitals, "OnDisable"); played = emitter.Played;
                    vitals.TakeDamage(1);
                    AudioCheck298(report, emitter.Played == played, "Actual emitter OnDisable unsubscribes committed damage.");
                    emitter.Rebind(); emitter.Rebind(); pool.StopAll(true); played = emitter.Played;
                    vitals.TakeDamage(1);
                    AudioCheck298(report, emitter.Played == played + 1, "Repeated actual Rebind leaves one damage subscription.");

                    pool.StopAll(true); controller.StopAttack(); emitter.Profile = null;
                    AudioCheck298(report, emitter.Status.StartsWith("MissingProfile", StringComparison.Ordinal) && !EnemyAudioEmitter298.OwnsCue(vitals, EnemyAudioRole298.Windup, audio), "Missing profile stays visible and cannot suppress generic cue.");
                    int fallback = emitter.FallbackPlayed;
                    AudioInvoke298(controller, "BeginAuthoredTelegraph");
                    AudioCheck298(report, emitter.FallbackPlayed == fallback + 1 && audio.LastCue == "enemy_windup", "Dynamically bound actor without profile uses actual generic palette fallback.");
                    controller.StopAttack(); emitter.Profile = profile; profile.Windup.Clip = null;
                    AudioCheck298(report, emitter.Status.Contains("windup") && !EnemyAudioEmitter298.OwnsCue(vitals, EnemyAudioRole298.Windup, audio), "A missing role remains visible without muting the generic observer.");
                    profile.Windup.Clip = clip;

                    pool.StopAll(true);
                    var other = AudioObject298("Audio298_Actor_B", preview); var otherVitals = other.AddComponent<EnemyVitals>(); otherVitals.Restore();
                    var otherEmitter = other.AddComponent<EnemyAudioEmitter298>(); otherEmitter.Profile = profile; otherEmitter.Soundscape = audio; otherEmitter.Rebind();
                    AudioCheck298(report, audio.EmitEnemy298(emitter, profile.Windup, "owner-a", go.transform.position) &&
                        audio.EmitEnemy298(otherEmitter, profile.Windup, "owner-b", other.transform.position), "Two actual actors share one bounded pool and profile.");
                    // Edit may report native isPlaying=false without a player loop. Explicit voice slots isolate
                    // cancellation mechanics; ordinary service requests above still use automatic pool selection.
                    pool.StopAll(true);
                    pool.Play(profile.Windup, go.transform.position, 1, palette.Mix.Sfx, slot:4, owner:emitter);
                    pool.Play(profile.Windup, other.transform.position, 1, palette.Mix.Sfx, slot:5, owner:otherEmitter);
                    audio.ReleaseEnemy298(emitter, profile.Windup);
                    AudioCheck298(report, AudioOwnerReleasing298(pool, emitter, profile.Windup) && !AudioOwnerReleasing298(pool, otherEmitter, profile.Windup), "Owner cancellation leaves the other actor's request intact.");

                    var awarenessGo = AudioObject298("Audio298_PerceptionBinding", preview); awarenessGo.SetActive(false);
                    awarenessGo.AddComponent<EnemyVitals>();
                    var awareness = awarenessGo.AddComponent<PrologueEncounter>();
                    awarenessGo.GetComponent<NavMeshAgent>().enabled = false;
                    var awarenessEmitter = awarenessGo.AddComponent<EnemyAudioEmitter298>(); awarenessEmitter.Profile = profile; awarenessEmitter.Soundscape = audio;
                    awarenessGo.SetActive(true); awarenessGo.GetComponent<EnemyVitals>().Restore(); awarenessEmitter.Rebind(); awarenessEmitter.Rebind();
                    AudioCheck298(report, AudioDetectionBindings298(awareness, awarenessEmitter) == 1 && awarenessEmitter.Played == 0,
                        "Actual PrologueEncounter.PlayerDetected has exactly one emitter subscriber after repeated Rebind; no invented detection.");
                    AudioInvoke298(awarenessEmitter, "OnDisable");
                    AudioCheck298(report, AudioDetectionBindings298(awareness, awarenessEmitter) == 0, "Actual perception event subscription removed on emitter disable.");
                    awarenessEmitter.Rebind();
                    AudioCheck298(report, AudioDetectionBindings298(awareness, awarenessEmitter) == 1, "Actual perception owner rebind restores one subscriber.");
                    report.Limitations.Add("Perception owner subscription uses a disabled diagnostic NavMeshAgent. Natural distance/line-of-sight/Nav Patrol-to-Chase publication must be observed in Play separately.");
                    AudioSet298(audio, "focused", false);
                    AudioCheck298(report, !EnemyAudioEmitter298.OwnsCue(vitals, EnemyAudioRole298.Death, audio), "Unavailable/focus-muted shared service cannot claim generic ownership.");
                    report.NativeStarts = pool.Starts;
                }
                else report.Limitations.Add("audit command inspects serialized actor/profile/mixer wiring only; engine fixture did not run.");
            }
            catch (Exception exception)
            {
                var cause = exception is TargetInvocationException invocation && invocation.InnerException != null ? invocation.InnerException : exception;
                report.Failures.Add(cause.GetType().Name + ": " + cause.Message);
            }
            finally
            {
                try
                {
                    if (audio != null) AudioSet298(audio, "pool", null);
                    pool?.Dispose();
                    if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                    foreach (var asset in assets) if (asset != null) Object.DestroyImmediate(asset);
                    UnityEngine.Random.state = random;
                    report.SceneShaAfter = AudioFileHash298(active.path);
                    var after = EditorSceneManager.GetSceneManagerSetup();
                    report.Restored = active == SceneManager.GetActiveScene() && report.SceneShaBefore == report.SceneShaAfter &&
                        setup.Length == after.Length && setup.Zip(after, (a, b) => a.path == b.path && a.isLoaded == b.isLoaded && a.isActive == b.isActive).All(x => x) &&
                        dirty.All(p => p.Key.IsValid() && p.Key.isDirty == p.Value);
                    AudioCheck298(report, report.Restored, "Original scene bytes, open-scene setup and dirty flags preserved; preview fixtures removed.");
                }
                catch (Exception cleanup) { report.Failures.Add("Cleanup: " + cleanup.Message); report.Restored = false; }
                report.Finished = true; report.FinishedUtc = DateTime.UtcNow.ToString("O");
                report.Passed = argument == "run" && report.Failures.Count == 0 && report.Restored;
                report.Status = report.Failures.Count > 0 ? "FAIL" : argument == "audit" ? "STRUCTURE_ONLY" : "ENGINE_EVENT_FIXTURE_PASS_WITH_LIMITATIONS";
                Directory.CreateDirectory(Path.GetDirectoryName(AudioChecksPath298));
                if (File.Exists(AudioChecksPath298))
                {
                    string history = Path.Combine(OutputRoot, "Analysis/AudioChecksHistory"); Directory.CreateDirectory(history);
                    File.Copy(AudioChecksPath298, Path.Combine(history, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + ".json"));
                }
                File.WriteAllText(AudioChecksPath298, JsonUtility.ToJson(report, true));
            }
            return JsonUtility.ToJson(report, true);
        }

        static GameObject AudioObject298(string name, Scene scene)
        { var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave }; SceneManager.MoveGameObjectToScene(go, scene); return go; }
        static WorldMacroPlaytestAudioProfileSO.Cue AudioCue298(AudioClip clip) => new WorldMacroPlaytestAudioProfileSO.Cue {
            Clip = clip, Volume = 0, SpatialBlend = 1, Cooldown = 0, MaxConcurrent = 12, AttackSeconds = .01f, ReleaseSeconds = .04f
        };
        static void AudioCheck298(AudioCheckReceipt298 report, bool condition, string label)
        { if (condition) report.Checks.Add(label); else report.Failures.Add(label); }
        static object AudioGet298(object value, string name) => value.GetType().GetField(name, AudioPrivate298)?.GetValue(value)
            ?? throw new MissingFieldException(value.GetType().Name, name);
        static void AudioSet298(object value, string name, object member)
        { var field = value.GetType().GetField(name, AudioPrivate298) ?? throw new MissingFieldException(value.GetType().Name, name); field.SetValue(value, member); }
        static void AudioInvoke298(object value, string method)
        { var info = value.GetType().GetMethod(method, AudioPrivate298) ?? throw new MissingMethodException(value.GetType().Name, method); info.Invoke(value, null); }
        static bool AudioOwnerReleasing298(WorldMacroAudioVoicePool pool, object owner, WorldMacroPlaytestAudioProfileSO.Cue cue)
        {
            var voices = (Array)AudioGet298(pool, "_voices");
            foreach (var voice in voices)
            {
                var type = voice.GetType(); var current = type.GetField("Current").GetValue(voice); var request = current.GetType();
                if (ReferenceEquals(request.GetField("Owner").GetValue(current), owner) && ReferenceEquals(request.GetField("Cue").GetValue(current), cue))
                    return (bool)type.GetField("Releasing").GetValue(voice);
            }
            return false;
        }
        static WorldMacroPlaytestAudioProfileSO.Cue[] AudioCurrentCues298(WorldMacroAudioVoicePool pool)
        {
            var list = new List<WorldMacroPlaytestAudioProfileSO.Cue>();
            foreach (var voice in (Array)AudioGet298(pool, "_voices"))
            {
                var current = voice.GetType().GetField("Current").GetValue(voice);
                var cue = current.GetType().GetField("Cue").GetValue(current) as WorldMacroPlaytestAudioProfileSO.Cue;
                if (cue != null) list.Add(cue);
            }
            return list.ToArray();
        }
        static string AudioHierarchy298(Transform transform) => transform.parent != null ? AudioHierarchy298(transform.parent) + "/" + transform.name : transform.name;
        static int AudioDetectionBindings298(PrologueEncounter encounter, EnemyAudioEmitter298 emitter)
        {
            var callbacks = typeof(PrologueEncounter).GetField("PlayerDetected", AudioPrivate298)?.GetValue(encounter) as Delegate;
            return callbacks == null ? 0 : callbacks.GetInvocationList().Count(x => ReferenceEquals(x.Target, emitter));
        }
        static string AudioFileHash298(string path)
        { if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "UNSAVED_OR_MISSING"; using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    }
}
