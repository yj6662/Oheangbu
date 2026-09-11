using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    [InitializeOnLoad]
    public static class KtpContactAudit
    {
        const string Key = "KtpContactAudit.Running";
        const string ReportFile = "ktp_contact_play_audit.json";
        static readonly List<string> Checks = new List<string>();
        static readonly List<string> Errors = new List<string>();
        static Scene scene;
        static Camera camera;
        static GameObject rig;
        static CombatLoopWiring wiring;
        static PlayerVitals vitals;
        static DodgeAction dodge;
        static ParryJudge judge;
        static GroggyMeter groggy;
        static InkPool ink;
        static CombatConfigSO config;
        static KtpContactProfile profile;
        static float started;
        static int stage;
        static bool sawLive;
        static bool priorBackground;
        static List<KtpContactEffect> spawned;
        [Serializable] class Report { public string status; public string[] checks, errors; public float seconds; }

        static KtpContactAudit()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += Log;
        }
        static void Log(string message, string stack, LogType type)
        {
            if (SessionState.GetBool(Key, false) && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                Errors.Add(message);
        }
        public static string Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Key, false)) throw new InvalidOperationException("Audit already running or Play active.");
            if (AssetDatabase.LoadAssetAtPath<KtpContactProfile>(KtpContactBuild.AssetPath) == null) throw new InvalidOperationException("Build contacts first.");
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true; return "PLAY_REQUESTED";
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                if (stage == 0) { Setup(); stage = 1; return; }
                if (stage == 1)
                {
                    if (Time.time - started < .15f) return;
                    Exercise(); stage = 2; return;
                }
                if (stage == 2)
                {
                    sawLive |= spawned.Where(e => e != null).Any(e => e.LiveParticles > 0);
                    if (spawned.All(e => e == null))
                    {
                        Require(sawLive, "Native Update produced live particles");
                        Checks.Add("All original contacts naturally finished and destroyed");
                        judge.RaiseGuard(Element.Wood, Time.time);
                        judge.ResolveImpact(Element.Earth, Time.time, new Vector3(0, 0, 3));
                        Require(Effects().Length == 1, "A second contact can spawn after cleanup");
                        rig.SetActive(false); stage = 3; return;
                    }
                    if (Time.time - started > 18) throw new InvalidOperationException("Contacts failed to expire within 18 seconds");
                }
                else if (stage == 3)
                {
                    Require(Effects().Length == 0, "Disabling wiring removes all owned contacts");
                    Finish();
                }
            }
            catch (Exception e) { Errors.Add(e.ToString()); Finish(); }
        }
        static void Setup()
        {
            Checks.Clear(); Errors.Clear();
            priorBackground = Application.runInBackground; Application.runInBackground = true;
            // Existing scene state is changed only inside Play and is restored on exit.
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
            scene = SceneManager.CreateScene("KTP_Contact_Runtime_Audit");
            camera = new GameObject("ContactAuditCamera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
            camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f, .14f, .16f); camera.fieldOfView = 60;
            camera.nearClipPlane = .05f; camera.farClipPlane = 50; camera.allowHDR = true;
            profile = Resources.Load<KtpContactProfile>(KtpContactProfile.ResourcePath);
            Require(profile != null, "Resource profile loads with vendor subtree references");
            config = ScriptableObject.CreateInstance<CombatConfigSO>();
            rig = new GameObject("ContactAuditRig"); rig.SetActive(false); SceneManager.MoveGameObjectToScene(rig, scene);
            dodge = rig.AddComponent<DodgeAction>(); Set(dodge, "_config", config);
            vitals = rig.AddComponent<PlayerVitals>(); Set(vitals, "_config", config); Set(vitals, "_dodge", dodge);
            wiring = rig.AddComponent<CombatLoopWiring>(); Set(wiring, "_config", config); Set(wiring, "_playerVitals", vitals);
            judge = new ParryJudge(config); groggy = new GroggyMeter(config); ink = new InkPool(config, null);
            wiring.Construct(null, judge, groggy, ink); rig.SetActive(true); started = Time.time;
        }
        static void Exercise()
        {
            Require(Camera.main == camera, "First person contact uses the active camera");
            var elements = new[] { Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water };
            foreach (var element in elements)
            {
                Vfx120Effect guard = null;
                if (element == Element.Wood || element == Element.Fire)
                {
                    var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>("Assets/_Project/Art/SpellVFX120/Data/VFX120_Catalog.asset");
                    var entry = catalog.Entries.Single(x => x.Glyph == (element == Element.Wood ? "거" : "너"));
                    var go = Object.Instantiate(entry.Prefab); go.SetActive(true); SceneManager.MoveGameObjectToScene(go, scene);
                    guard = go.GetComponent<Vfx120Effect>(); guard.PreviewControlled = false; guard.DemonstrationCues = false;
                    guard.Begin(Vector3.zero, null, Vector3.forward * 3, Color.white);
                    if (element == Element.Wood) Vfx120Effect.SelectBambooGuard(guard); else Vfx120Effect.SelectFireGuard(guard);
                }
                var attack = elements.First(e => ElementRelations.Overcomes(element, e));
                int before = Effects().Length;
                judge.RaiseGuard(element, Time.time);
                var result = judge.ResolveImpact(attack, Time.time, new Vector3(0, 0, 3));
                Require(result == ParryOutcome.Success && Effects().Length == before + 1, element + " success emits exactly one original contact");
                var e = Effects().Single(x => x.Source == profile.ParrySource(element) && !spawnedContains(x));
                if (guard != null)
                {
                    Require(guard.ParryAt >= 0, element + " original guard receives confirmed parry");
                    guard.Sample(.25f);
                    Require(guard.NativeImpact == null && guard.NativeImpactStartedAt < 0, element + " guard suppresses duplicate contact motif");
                    Require(element == Element.Wood ? guard.BambooGuardBend > 0 : guard.FireGuardPulse > 0,
                        element + " guard keeps its bend or pulse response");
                    Object.DestroyImmediate(guard.gameObject);
                }
                CheckSource(e); Capture(e.Source, profile.ParryScale, "parry_" + element);
                tracked.Add(e);
            }
            float reward = ink.Value, meter = groggy.Value01;
            int count = Effects().Length;
            judge.RaiseGuard(Element.Wood, Time.time);
            Require(judge.ResolveImpact(Element.Wood, Time.time, new Vector3(0, 0, 3)) == ParryOutcome.Half, "Same-element impact resolves Half");
            Require(Effects().Length == count + 1 && ink.Value == reward && groggy.Value01 == meter, "Half creates one contact without rewards");
            Require(Effects().Any(e => Mathf.Approximately(e.transform.localScale.x, profile.ParryScale * profile.HalfScale)), "Half uses half carrier scale");
            count = Effects().Length;
            judge.RaiseGuard(Element.Wood, Time.time);
            Require(judge.ResolveImpact(Element.Fire, Time.time, new Vector3(0, 0, 3)) == ParryOutcome.Fail && Effects().Length == count, "Fail emits no parry contact");
            judge.RaiseGuard(Element.Wood, Time.time - config.ParryWindow - .01f);
            Require(judge.ResolveImpact(Element.Fire, Time.time, Vector3.forward * 3) == ParryOutcome.Block && Effects().Length == count, "Late Block emits no success contact");
            judge.RaiseGuard(Element.Wood, Time.time - config.GuardDuration - 1);
            Require(judge.ResolveImpact(Element.Fire, Time.time, Vector3.forward * 3) == ParryOutcome.None && Effects().Length == count, "Expired guard emits no contact");
            float hp = vitals.Hp01;
            Require(vitals.TakeDamage(5) && vitals.Hp01 < hp && Effects().Length == count + 1, "Actual damage emits one player-hit contact");
            Require(ink.Value == reward && groggy.Value01 == meter, "Taking damage does not grant ink or groggy");
            Capture(profile.PlayerHit, profile.PlayerHitScale, "player_hit", profile.PlayerHitDistance);
            count = Effects().Length;
            vitals.TakeDamage(0); Require(Effects().Length == count, "Zero damage creates no hit contact");
            Require(dodge.TryDodge(Vector3.right), "Actual dodge begins");
            Require(!vitals.TakeDamage(5) && Effects().Length == count, "Invulnerable dodge emits no hit contact");
            Set(dodge, "_invulnerableUntil", Time.time - 1);
            Require(vitals.TakeDamage(100000) && vitals.Hp01 == 0 && Effects().Length == count + 1, "Lethal hit retains contact feedback");
            count = Effects().Length;
            Require(!vitals.TakeDamage(5) && Effects().Length == count, "Already dead player emits no additional hit");
            Require(Object.FindObjectsByType<ParryBurstEffect>(FindObjectsSortMode.None).Length == 0, "No temporary shard burst accompanies original contacts");
            spawned = Effects().ToList();
        }
        static readonly List<KtpContactEffect> tracked = new List<KtpContactEffect>();
        static bool spawnedContains(KtpContactEffect e) => tracked.Contains(e);
        static KtpContactEffect[] Effects() => Object.FindObjectsByType<KtpContactEffect>(FindObjectsSortMode.None);
        static void CheckSource(KtpContactEffect effect)
        {
            var a = effect.Source.GetComponentsInChildren<ParticleSystem>(true);
            var b = effect.Content.GetComponentsInChildren<ParticleSystem>(true);
            Require(a.Length == b.Length, "All authored particle layers retained");
            for (int i = 0; i < a.Length; i++)
            {
                Require(a[i].name == b[i].name && a[i].main.maxParticles == b[i].main.maxParticles
                    && a[i].main.duration == b[i].main.duration && Same(a[i].main.startColor, b[i].main.startColor)
                    && Same(a[i].main.startSize, b[i].main.startSize)
                    && a[i].emission.burstCount == b[i].emission.burstCount
                    && a[i].GetComponent<Renderer>().sharedMaterials.SequenceEqual(b[i].GetComponent<Renderer>().sharedMaterials), "Source colors, capacity, size, timing, bursts and shared materials retained: " + a[i].name);
            }
        }
        static bool Same(ParticleSystem.MinMaxGradient a, ParticleSystem.MinMaxGradient b)
        {
            if (a.mode != b.mode) return false;
            for (int t = 0; t <= 16; t++) for (int k = 0; k <= 2; k++)
                if (a.Evaluate(t / 16f, k / 2f) != b.Evaluate(t / 16f, k / 2f)) return false;
            return true;
        }
        static bool Same(ParticleSystem.MinMaxCurve a, ParticleSystem.MinMaxCurve b)
        {
            if (a.mode != b.mode) return false;
            for (int t = 0; t <= 16; t++) for (int k = 0; k <= 2; k++)
                if (!Mathf.Approximately(a.Evaluate(t / 16f, k / 2f), b.Evaluate(t / 16f, k / 2f))) return false;
            return true;
        }
        static void Capture(GameObject source, float scale, string name, float distance = 3)
        {
            var hidden = Effects().Select(e => e.gameObject).ToArray(); foreach (var go in hidden) go.SetActive(false);
            var effect = KtpContactEffect.Spawn(source, Vector3.forward * distance, Quaternion.Euler(profile.SourceEuler), scale, scene, true);
            Require(effect != null, "Review original subtree spawned");
            string folder = Path.Combine(Vfx120Editor.Output, "ContactReview"); Directory.CreateDirectory(folder);
            var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32); rt.Create();
            var texture = new Texture2D(960, 540, TextureFormat.RGB24, false);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                foreach (float age in new[] { .12f, .32f, .65f, 1.2f })
                {
                    effect.Sample(age); camera.Render(); RenderTexture.active = rt;
                    texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); texture.Apply();
                    File.WriteAllBytes(Path.Combine(folder, name + "_" + age.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ".png"), texture.EncodeToPNG());
                }
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive; rt.Release();
                Object.DestroyImmediate(texture); Object.DestroyImmediate(rt); Object.DestroyImmediate(effect.gameObject);
                foreach (var go in hidden) go.SetActive(true);
            }
        }
        static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        static void Require(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); Checks.Add(label); }
        static void Finish()
        {
            string json = JsonUtility.ToJson(new Report { status = Errors.Count == 0 ? "PASS" : "FAIL", checks = Checks.ToArray(), errors = Errors.ToArray(), seconds = Time.time - started }, true);
            File.WriteAllText(Path.Combine(Vfx120Editor.Output, ReportFile), json);
            Application.runInBackground = priorBackground; SessionState.SetBool(Key, false); stage = 0; tracked.Clear();
            EditorApplication.isPlaying = false;
        }
    }
}
