using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Drawing;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Play-mode evidence for the five accepted summon casts. The diagnostic supplies an already-recognized
    // DrawnLetter and the normal input events to the existing subscribers; it does not exercise handwriting recognition.
    public static class WorldMacroSummonCastRuntimeReview
    {
        [Serializable]
        private sealed class Row
        {
            public string glyph, prefab, file, presentation;
            public int acceptedEvents, maxConcurrent, visibleRenderers;
            public float inkBefore, inkAfter, expectedCost, life, captureAge, lastObservedAge;
            public float maxPlayerDriftM, maxEffectOriginDriftM, maxPresentationDriftM, pauseAgeDelta;
            public Vector3 acceptedOrigin, acceptedForward, presentationCentre, viewport;
            public bool acceptedOnce, singleInkSpend, noAttackPlan, correctPresentation, grounded;
            public bool staticComponents, inCamera, imageWritten, pauseHeld, disappeared, registryClean;
        }

        [Serializable]
        private sealed class Report
        {
            public string utc, status = "RUNNING", detail;
            public string scope = "Editor Play: injected already-recognized DrawnLetter plus normal subscribed stroke/Commit events -> live SpellResolver/CombatLoopWiring/BrushStrokeFeedAdapter -> authored VFX Update/destruction. This is not raw handwriting, pen input, user visual approval, movement, attack, damage, defence or progression evidence.";
            public int width = 1920, height = 1080, attackPlans, pendingDamageAfterCast, finalRegistryCount;
            public uint guardRevisionBefore, guardRevisionAfter;
            public float originalInk, restoredInk, originalTimeScale, restoredTimeScale, totalGameSeconds;
            public bool usedLiveSceneServices, stayedAtInnReviewPose, enemyHealthUnchanged, defenceStateUnchanged;
            public bool combatRestored, motorRestored, inkRestored, timeScaleRestored;
            public Row[] rows;
        }

        private enum Phase { Inject, FindSpawn, Observe, Pause, WaitForDestroy }

        private static readonly char[] Glyphs = { '곰', '놈', '몸', '솜', '옴' };
        private static readonly string[] Prefabs = { "016_ACF0", "040_B188", "064_BAB8", "088_C19C", "112_C634" };
        private static readonly string[] Files = { "summon_01_gom.png", "summon_02_nom.png", "summon_03_mom.png", "summon_04_som.png", "summon_05_om.png" };
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly string Output = Path.Combine(WorldMacroPlaytestAuthoring.Output, "SummonCast");
        private static readonly string ReportPath = Path.Combine(Output, "runtime_review.json");

        private static bool _running;
        private static int _index, _injectedFrame, _lastFrame;
        private static double _deadline, _pauseUntil, _spawnUntil;
        private static Phase _phase;
        private static Report _report;
        private static Row _row;
        private static WorldMacroPlaytestSession _session;
        private static BrushStrokeFeedAdapter _adapter;
        private static CombatLoopWiring _wiring;
        private static DrawnLetterEventChannelSO _letterChannel;
        private static InkPool _ink;
        private static ParryJudge _judge;
        private static Vfx120Effect _effect;
        private static HashSet<int> _beforeEffects;
        private static Vector3 _playerStart, _effectOrigin, _effectRootStart, _presentationStart;
        private static float _runStarted, _pauseAge, _resumeScale;
        private static bool _motorWasEnabled, _combatWasActive;
        private static float[] _enemyHealthBefore;

        public static string Begin()
        {
            if (_running) throw new InvalidOperationException("Summon runtime review is already running.");
            if (!EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Start the World Macro playtest first and select its flat inn review station.");

            _session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (_session == null || _session.Progress == null || _session.Walker == null || _session.Walker.Body == null)
                throw new InvalidOperationException("World Macro play session is not ready.");
            if (_session.Walker.Seated || _session.Walker.Drawing.InDrawMode || _session.Walker.Motor.IsDrawing)
                throw new InvalidOperationException("The player must be standing outside drawing mode for the summon review.");
            if (Vector3.Distance(_session.Walker.Body.transform.position, _session.Content.InnCheckpointFeet) > 6f)
                throw new InvalidOperationException("Use Runtime visit:geumpyo_inn before the summon review.");
            if (!_session.TrySafeFeet(_session.Walker.Body.transform.position, out var safe)
                || Vector3.Distance(safe, _session.Walker.Body.transform.position) > .35f || !FlatGround(_session.Walker.Body.transform, out _))
                throw new InvalidOperationException("The selected inn review pose is not on verified flat ground.");

            _wiring = _session.Walker.Wiring;
            _adapter = Object.FindObjectsByType<BrushStrokeFeedAdapter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => ReferenceEquals(Read<DrawingInputController>(candidate, "_input"), _session.Walker.Drawing));
            if (_wiring == null || _adapter == null) throw new InvalidOperationException("Live summon cast consumers are missing.");
            var book = Read<SpellBookSO>(_adapter, "_spellBook");
            if (book == null || AssetDatabase.GetAssetPath(book) != WorldMacroSummonCastAuthoring.TestBookPath)
                throw new InvalidOperationException("The live adapter is not using the World Macro summon TEST book.");
            _letterChannel = Read<DrawnLetterEventChannelSO>(_wiring, "_letterDrawn");
            if (_letterChannel == null || !ReferenceEquals(_letterChannel, Read<DrawnLetterEventChannelSO>(_adapter, "_letterDrawn")))
                throw new InvalidOperationException("Live combat and presentation do not share the DrawnLetter channel.");
            _ink = Read<InkPool>(_wiring, "_ink");
            _judge = Read<ParryJudge>(_wiring, "_judge");
            var config = Read<CombatConfigSO>(_wiring, "_config");
            if (_ink == null || config == null) throw new InvalidOperationException("Live combat ink service is not ready.");
            if (_adapter.ActiveSummonPresentationCount != 0)
                throw new InvalidOperationException("Clear any existing summon presentation before starting the bounded review.");

            Directory.CreateDirectory(Output);
            foreach (string file in Files)
            {
                string path = Path.Combine(Output, file);
                if (File.Exists(path)) File.Delete(path);
            }
            if (File.Exists(ReportPath)) File.Delete(ReportPath);

            _report = new Report { utc = DateTime.UtcNow.ToString("o"), rows = new Row[Glyphs.Length] };
            _report.originalInk = _ink.Value;
            _report.originalTimeScale = Time.timeScale;
            _report.guardRevisionBefore = _judge != null ? _judge.GuardRevision : 0;
            _enemyHealthBefore = _session.Actors.Select(actor => actor.GetComponent<EnemyVitals>().Hp01).ToArray();
            _report.usedLiveSceneServices = true;
            _playerStart = _session.Walker.Body.transform.position;
            _motorWasEnabled = _session.Walker.Motor.enabled;
            _combatWasActive = _session.CombatActive;
            _session.CombatActive = false;
            _session.Cull();
            _session.Walker.Motor.enabled = false;
            _resumeScale = 1f;
            Time.timeScale = _resumeScale;
            _runStarted = Time.time;
            _index = 0;
            _phase = Phase.Inject;
            _deadline = EditorApplication.timeSinceStartup + 50;
            _wiring.CastPlanned += OnAttackPlan;
            _wiring.SummonAccepted += OnSummonAccepted;
            _running = true;
            EditorApplication.update += Tick;
            return "RUNNING: five live accepted summon presentations; injected recognized events, not raw handwriting. Poll SummonCast runtime-poll.";
        }

        public static string Poll()
        {
            if (_running)
                return "RUNNING: glyph " + (_index + 1) + "/" + Glyphs.Length + " phase=" + _phase + "; no progress artifact is written.";
            if (!File.Exists(ReportPath)) return "NOT_RUN";
            var report = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
            return report.status + "; stills=" + (report.rows == null ? 0 : report.rows.Count(row => row != null && row.imageWritten))
                + "; finalRegistry=" + report.finalRegistryCount + "; rawHandwriting=UNVERIFIED; " + ReportPath;
        }

        private static void Tick()
        {
            if (!_running) return;
            if (_lastFrame == Time.frameCount) return;
            _lastFrame = Time.frameCount;
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play mode ended during summon review.");
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("Summon review exceeded its 50 second bound.");
                ObserveStability();
                switch (_phase)
                {
                    case Phase.Inject: InjectCast(); break;
                    case Phase.FindSpawn: FindSpawn(); break;
                    case Phase.Observe: Observe(); break;
                    case Phase.Pause: ObservePause(); break;
                    case Phase.WaitForDestroy: WaitForDestroy(); break;
                }
            }
            catch (Exception e)
            {
                Finish("ERROR", e.ToString());
            }
        }

        private static void InjectCast()
        {
            if (_index >= Glyphs.Length) { Finish("PASS", "Five accepted casts spawned, remained static, rendered once each and self-cleaned."); return; }
            char glyph = Glyphs[_index];
            _row = new Row { glyph = glyph.ToString(), prefab = Prefabs[_index], file = Files[_index] };
            _report.rows[_index] = _row;
            _beforeEffects = new HashSet<int>(Object.FindObjectsByType<Vfx120Effect>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(effect => effect.GetInstanceID()));
            _ink.Restore(1f);
            _row.inkBefore = _ink.Value;
            _row.expectedCost = Read<CombatConfigSO>(_wiring, "_config").SpellInkCost;

            Rect rect = _session.Walker.ViewCamera.pixelRect;
            if (rect.width <= 0 || rect.height <= 0) throw new InvalidOperationException("Live camera has no viewport.");
            var started = Read<Delegate>(_session.Walker.Drawing, "StrokeStarted");
            var point = Read<Delegate>(_session.Walker.Drawing, "StrokePointAdded");
            var ended = Read<Delegate>(_session.Walker.Drawing, "StrokeEnded");
            var committed = Read<Delegate>(_session.Walker.Drawing, "Committed");
            var exited = Read<Delegate>(_session.Walker.Drawing, "ModeExited");
            if (started == null || point == null || ended == null || committed == null || exited == null)
                throw new InvalidOperationException("Production brush adapter is not subscribed to the normal input events.");
            for (int stroke = 0; stroke < 2; stroke++)
            {
                started.DynamicInvoke();
                for (int p = 0; p < 8; p++)
                {
                    float u = p / 7f;
                    point.DynamicInvoke(new Vector2(rect.x + rect.width * (.43f + .14f * u),
                        rect.y + rect.height * (.43f + .13f * (stroke == 0 ? u : 1f - u))));
                }
                ended.DynamicInvoke();
            }

            Jamo initial = new[] { Jamo.Giyeok, Jamo.Nieun, Jamo.Mieum, Jamo.Siot, Jamo.Ieung }[_index];
            _letterChannel.Raise(new DrawnLetter(glyph, initial, Jamo.O, Jamo.Mieum, .8f, .75f, 1.2f, 1.4f, 2));
            committed.DynamicInvoke(true);
            exited.DynamicInvoke();
            _row.inkAfter = _ink.Value;
            _row.singleInkSpend = Mathf.Abs(_row.inkAfter - (_row.inkBefore - _row.expectedCost)) < .00001f;
            if (!_row.singleInkSpend) throw new InvalidOperationException(glyph + " did not spend exactly one standard spell cost.");
            _row.noAttackPlan = _report.attackPlans == 0 && ((IList)ReadObject(_wiring, "_pendingCasts")).Count == 0;
            _report.pendingDamageAfterCast = ((IList)ReadObject(_wiring, "_pendingCasts")).Count;
            if (!_row.noAttackPlan) throw new InvalidOperationException(glyph + " created an attack plan or pending damage.");
            _injectedFrame = Time.frameCount;
            _spawnUntil = EditorApplication.timeSinceStartup + 2;
            _phase = Phase.FindSpawn;
        }

        private static void FindSpawn()
        {
            if (Time.frameCount <= _injectedFrame) return;
            _effect = Object.FindObjectsByType<Vfx120Effect>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => !_beforeEffects.Contains(candidate.GetInstanceID()) && candidate.Profile != null
                    && candidate.Profile.Glyph == _row.glyph);
            if (_effect == null)
            {
                if (EditorApplication.timeSinceStartup < _spawnUntil) return;
                throw new InvalidOperationException(_row.glyph + " accepted cast produced no authored VFX instance within two seconds.");
            }
            _row.life = _effect.Life;
            _row.correctPresentation = CorrectPresentation(_effect, out _row.presentation);
            _row.grounded = Grounded(_effect);
            _row.staticComponents = PresentationOnly(_effect.gameObject);
            if (!_row.correctPresentation || !_row.grounded || !_row.staticComponents || Mathf.Abs(_row.life - 4.6f) > .001f)
                throw new InvalidOperationException(_row.glyph + " did not build its grounded static 4.6 second presentation.");
            _effectOrigin = _effect.ReceivedOrigin;
            _effectRootStart = _effect.transform.position;
            _presentationStart = PresentationCentre(_effect);
            _row.presentationCentre = _presentationStart;
            Vector3 expectedForward = Vector3.ProjectOnPlane(_session.Walker.Body.transform.forward, Vector3.up).normalized;
            Vector3 acceptedForward = Vector3.ProjectOnPlane(_row.acceptedForward, Vector3.up).normalized;
            _row.acceptedOnce = _row.acceptedEvents == 1 && Vector3.Distance(_row.acceptedOrigin, _playerStart) < .001f
                && expectedForward.sqrMagnitude > .5f && Vector3.Dot(expectedForward, acceptedForward) > .999f;
            if (!_row.acceptedOnce || Vector3.Distance(_effectOrigin, _row.acceptedOrigin) > .001f)
                throw new InvalidOperationException(_row.glyph + " spawn was not tied to exactly one accepted player pose.");
            _phase = Phase.Observe;
        }

        private static void Observe()
        {
            if (_effect == null) throw new InvalidOperationException(_row.glyph + " disappeared before its review still.");
            _row.lastObservedAge = _effect.Age;
            _row.visibleRenderers = VisibleRenderers(_effect);
            if (_effect.Age < 1.1f || _row.visibleRenderers <= 0) return;
            _row.viewport = _session.Walker.ViewCamera.WorldToViewportPoint(_presentationStart + Vector3.up * .8f);
            _row.inCamera = _row.viewport.z > 0 && _row.viewport.x > 0 && _row.viewport.x < 1 && _row.viewport.y > 0 && _row.viewport.y < 1;
            if (!_row.inCamera) throw new InvalidOperationException(_row.glyph + " presentation is outside the live player camera.");
            RenderStill(_session.Walker.ViewCamera, Path.Combine(Output, _row.file));
            _row.captureAge = _effect.Age;
            _row.imageWritten = File.Exists(Path.Combine(Output, _row.file));
            if (!_row.imageWritten) throw new IOException(_row.glyph + " still was not written.");
            if (_index == 0)
            {
                _pauseAge = _effect.Age;
                Time.timeScale = 0;
                _pauseUntil = EditorApplication.timeSinceStartup + .45;
                _phase = Phase.Pause;
            }
            else _phase = Phase.WaitForDestroy;
        }

        private static void ObservePause()
        {
            if (_effect == null) throw new InvalidOperationException("Paused summon was destroyed by unscaled time.");
            if (EditorApplication.timeSinceStartup < _pauseUntil) return;
            _row.pauseAgeDelta = Mathf.Abs(_effect.Age - _pauseAge);
            _row.pauseHeld = _row.pauseAgeDelta < .002f;
            Time.timeScale = _resumeScale;
            if (!_row.pauseHeld) throw new InvalidOperationException("Summon lifetime advanced while Time.timeScale was zero.");
            _phase = Phase.WaitForDestroy;
        }

        private static void WaitForDestroy()
        {
            if (_effect != null)
            {
                _row.lastObservedAge = _effect.Age;
                return;
            }
            _row.disappeared = true;
            _row.registryClean = _adapter.ActiveSummonPresentationCount == 0;
            if (!_row.registryClean) throw new InvalidOperationException(_row.glyph + " self-destruction left a tracked summon.");
            _index++;
            _effect = null;
            _row = null;
            _phase = Phase.Inject;
        }

        private static void ObserveStability()
        {
            if (_row == null || _session == null || _session.Walker == null) return;
            _row.maxPlayerDriftM = Mathf.Max(_row.maxPlayerDriftM, Vector3.Distance(_playerStart, _session.Walker.Body.transform.position));
            if (_effect == null) return;
            _row.maxEffectOriginDriftM = Mathf.Max(_row.maxEffectOriginDriftM,
                Mathf.Max(Vector3.Distance(_effectOrigin, _effect.ReceivedOrigin), Vector3.Distance(_effectRootStart, _effect.transform.position)));
            _row.maxPresentationDriftM = Mathf.Max(_row.maxPresentationDriftM, Vector3.Distance(_presentationStart, PresentationCentre(_effect)));
            _row.maxConcurrent = Mathf.Max(_row.maxConcurrent, _adapter.ActiveSummonPresentationCount);
            if (_row.maxPlayerDriftM > .01f || _row.maxEffectOriginDriftM > .001f || _row.maxPresentationDriftM > .001f)
                throw new InvalidOperationException(_row.glyph + " player, accepted origin or static presentation drifted during its lifetime.");
        }

        private static void OnAttackPlan(CastPlan _) { if (_report != null) _report.attackPlans++; }

        private static void OnSummonAccepted(SpellCast cast, Vector3 origin, Vector3 forward)
        {
            if (_row == null || cast.Letter.ToString() != _row.glyph) return;
            _row.acceptedEvents++;
            _row.acceptedOrigin = origin;
            _row.acceptedForward = forward;
        }

        private static bool CorrectPresentation(Vfx120Effect effect, out string presentation)
        {
            int count = (effect.WoodDeer != null ? 1 : 0) + (effect.FireHaetae != null ? 1 : 0)
                + (effect.DokkaebiClub != null ? 1 : 0) + (effect.MetalTiger != null ? 1 : 0)
                + (effect.WaterTurtle != null ? 1 : 0);
            presentation = effect.WoodDeer != null ? nameof(WoodDeerVfx)
                : effect.FireHaetae != null ? nameof(FireHaetaeVfx)
                : effect.DokkaebiClub != null ? nameof(DokkaebiClubVfx)
                : effect.MetalTiger != null ? nameof(MetalTigerVfx)
                : effect.WaterTurtle != null ? nameof(WaterTurtleVfx) : "MISSING";
            return count == 1 && ((_row.glyph == "곰" && effect.WoodDeer != null)
                || (_row.glyph == "놈" && effect.FireHaetae != null)
                || (_row.glyph == "몸" && effect.DokkaebiClub != null)
                || (_row.glyph == "솜" && effect.MetalTiger != null)
                || (_row.glyph == "옴" && effect.WaterTurtle != null));
        }

        private static bool Grounded(Vfx120Effect effect)
        {
            if (effect.WoodDeer != null) return effect.WoodDeer.Grounded;
            if (effect.FireHaetae != null) return effect.FireHaetae.Grounded;
            if (effect.DokkaebiClub != null) return effect.DokkaebiClub.Grounded;
            if (effect.MetalTiger != null) return effect.MetalTiger.Grounded;
            return effect.WaterTurtle != null && effect.WaterTurtle.Grounded;
        }

        private static Vector3 PresentationCentre(Vfx120Effect effect)
        {
            if (effect.WoodDeer != null) return effect.WoodDeer.Centre;
            if (effect.FireHaetae != null) return effect.FireHaetae.Centre;
            if (effect.DokkaebiClub != null) return effect.DokkaebiClub.Centre;
            if (effect.MetalTiger != null) return effect.MetalTiger.Centre;
            return effect.WaterTurtle != null ? effect.WaterTurtle.Centre : effect.ReceivedOrigin;
        }

        private static int VisibleRenderers(Vfx120Effect effect)
        {
            if (effect.WoodDeer != null) return effect.WoodDeer.VisibleRenderers;
            if (effect.FireHaetae != null) return effect.FireHaetae.VisibleRenderers;
            if (effect.DokkaebiClub != null) return effect.DokkaebiClub.VisibleRenderers;
            if (effect.MetalTiger != null) return effect.MetalTiger.VisibleRenderers;
            return effect.WaterTurtle != null ? effect.WaterTurtle.VisibleRenderers : 0;
        }

        private static bool PresentationOnly(GameObject root)
        {
            return root.GetComponentsInChildren<EnemyController>(true).Length == 0
                && root.GetComponentsInChildren<EnemyVitals>(true).Length == 0
                && root.GetComponentsInChildren<Collider>(true).Length == 0
                && root.GetComponentsInChildren<Rigidbody>(true).Length == 0
                && root.GetComponentsInChildren<Animator>(true).Length == 0
                && root.GetComponentsInChildren<Animation>(true).Length == 0;
        }

        private static bool FlatGround(Transform player, out Vector3 normal)
        {
            normal = Vector3.zero;
            float best = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(player.position + Vector3.up, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null || hit.collider is CharacterController || hit.transform.IsChildOf(player) || hit.distance >= best) continue;
                best = hit.distance;
                normal = hit.normal;
            }
            return best < float.PositiveInfinity && normal.y >= .95f;
        }

        private static void RenderStill(Camera source, string path)
        {
            GameObject root = null;
            Camera camera = null;
            RenderTexture texture = null;
            Texture2D image = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                root = new GameObject("Temporary_SummonCastStill") { hideFlags = HideFlags.HideAndDontSave };
                camera = root.AddComponent<Camera>();
                camera.CopyFrom(source);
                camera.enabled = false;
                camera.aspect = 1920f / 1080f;
                camera.useOcclusionCulling = false;
                camera.layerCullDistances = new float[32];
                camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                var sourceData = source.GetComponent("UniversalAdditionalCameraData");
                if (sourceData != null)
                {
                    var destinationData = root.AddComponent(sourceData.GetType());
                    foreach (string name in new[] { "renderPostProcessing", "renderShadows", "volumeLayerMask", "antialiasing", "antialiasingQuality", "stopNaN", "dithering", "requiresColorOption", "requiresDepthOption" })
                    {
                        var property = sourceData.GetType().GetProperty(name);
                        if (property != null && property.CanRead && property.CanWrite) property.SetValue(destinationData, property.GetValue(sourceData));
                    }
                }
                texture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
                image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                image.Apply(false);
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                if (camera != null) camera.targetTexture = null;
                RenderTexture.active = previous;
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                if (image != null) Object.DestroyImmediate(image);
                if (root != null) Object.DestroyImmediate(root);
            }
        }

        private static void Finish(string status, string detail)
        {
            if (!_running) return;
            _running = false;
            EditorApplication.update -= Tick;
            if (_wiring != null)
            {
                _wiring.CastPlanned -= OnAttackPlan;
                _wiring.SummonAccepted -= OnSummonAccepted;
            }
            try
            {
                if (_adapter != null) Invoke(_adapter, "DestroyAllImmediate");
                if (_report != null) _report.finalRegistryCount = _adapter != null ? _adapter.ActiveSummonPresentationCount : -1;
            }
            catch (Exception e) { status = "ERROR"; detail += "\nCleanup: " + e; }
            if (_session != null)
            {
                if (_session.Walker != null && _session.Walker.Motor != null) _session.Walker.Motor.enabled = _motorWasEnabled;
                _session.CombatActive = _combatWasActive;
                _session.Cull();
            }
            Time.timeScale = _report != null ? _report.originalTimeScale : 1f;
            if (_ink != null && _report != null) _ink.Restore(_report.originalInk);
            if (_report != null)
            {
                _report.totalGameSeconds = Time.time - _runStarted;
                _report.restoredInk = _ink != null ? _ink.Value : -1;
                _report.restoredTimeScale = Time.timeScale;
                _report.guardRevisionAfter = _judge != null ? _judge.GuardRevision : 0;
                _report.motorRestored = _session != null && _session.Walker.Motor.enabled == _motorWasEnabled;
                _report.combatRestored = _session != null && _session.CombatActive == _combatWasActive;
                _report.inkRestored = _ink != null && Mathf.Abs(_ink.Value - _report.originalInk) < .00001f;
                _report.timeScaleRestored = Mathf.Abs(Time.timeScale - _report.originalTimeScale) < .00001f;
                _report.stayedAtInnReviewPose = _session != null && Vector3.Distance(_playerStart, _session.Walker.Body.transform.position) <= .01f;
                _report.enemyHealthUnchanged = _session != null && _enemyHealthBefore != null
                    && _enemyHealthBefore.Length == _session.Actors.Length
                    && !_session.Actors.Where((actor, i) => Mathf.Abs(actor.GetComponent<EnemyVitals>().Hp01 - _enemyHealthBefore[i]) > .00001f).Any();
                _report.defenceStateUnchanged = _report.guardRevisionAfter == _report.guardRevisionBefore;
                if (_report.finalRegistryCount != 0 || !_report.motorRestored || !_report.combatRestored
                    || !_report.inkRestored || !_report.timeScaleRestored || !_report.stayedAtInnReviewPose
                    || !_report.enemyHealthUnchanged || !_report.defenceStateUnchanged) status = "ERROR";
                _report.status = status;
                _report.detail = detail;
                Directory.CreateDirectory(Output);
                File.WriteAllText(ReportPath, JsonUtility.ToJson(_report, true));
            }
            _session = null; _adapter = null; _wiring = null; _letterChannel = null; _ink = null; _judge = null;
            _effect = null; _row = null; _enemyHealthBefore = null;
        }

        private static object ReadObject(object target, string field)
        {
            var info = target.GetType().GetField(field, Hidden);
            if (info == null) throw new MissingFieldException(target.GetType().Name, field);
            return info.GetValue(target);
        }

        private static T Read<T>(object target, string field) { return (T)ReadObject(target, field); }

        private static void Invoke(object target, string method)
        {
            var info = target.GetType().GetMethod(method, Hidden);
            if (info == null) throw new MissingMethodException(target.GetType().Name, method);
            info.Invoke(target, null);
        }
    }
}
