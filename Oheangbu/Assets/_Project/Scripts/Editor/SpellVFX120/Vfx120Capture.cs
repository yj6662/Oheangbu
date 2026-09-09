using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120Capture
    {
        [Serializable] public class Request
        {
            public int start = 0, count = 120, width = 1280, height = 720, frames = 5;
            public bool clip;
            public bool demonstrationCues = true;
            // Zero captures the complete authored lifetime. Explicit durations remain supported.
            public float fps = 24, duration;
            public string folder = "Stills";
        }
        [Serializable] class Progress
        {
            public string status, folder, error;
            public int index, frame, completed, total;
            public bool demonstrationCues;
        }
        [Serializable] class CaptureEvidence
        {
            public string glyph, title, status = "PRESENTATION_REVIEW_ONLY";
            public string capturedUtc, loadedRuntimeAssemblyMvid, loadedRuntimeAssemblyLastWriteUtc;
            public bool demonstrationCues, gameplayConnectionVerified;
            public float impactTime, life;
            public float[] sampledSeconds;
            public string primaryTarget, secondaryTarget;
            public string eventSource;
            public string areaPlanSource;
            public string areaShape;
            public Vector3 areaPoint, areaDirection;
            public float areaRadius, areaAngle, areaLength, areaDelay, areaSpeed;
        }
        static Request _r;
        static Progress _p;
        static Vfx120Catalog _catalog;
        static Vfx120Effect _effect;
        static GameObject _fixtureRoot;
        static Camera _camera;
        static RenderTexture _target;
        static Texture2D _readback;
        static int _index, _frame;
        static string _directory;
        static readonly float[] SampleFractions = { .07f, .22f, .42f, .64f, .9f };

        public static string Start(string request)
        {
            if (_r != null) throw new InvalidOperationException("A VFX capture is already running");
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Capture requires edit review scene");
            string runtimeAssembly = typeof(Vfx120Effect).Assembly.Location;
            DateTime compiled = File.GetLastWriteTimeUtc(runtimeAssembly);
            foreach (string source in Directory.GetFiles("Assets/_Project/Scripts/App/SpellVFX120", "*.cs", SearchOption.AllDirectories))
                if (File.GetLastWriteTimeUtc(source) > compiled)
                    throw new InvalidOperationException("Refresh/compile VFX scripts before capture: " + source);
            _r = string.IsNullOrEmpty(request) ? new Request() : JsonUtility.FromJson<Request>(request);
            _r.start = Mathf.Max(0, _r.start); _r.count = Mathf.Max(1, _r.count);
            _r.frames = Mathf.Clamp(_r.frames, 1, 5); _r.fps = Mathf.Clamp(_r.fps, 1, 120);
            if (_r.width < 1 || _r.height < 1) { _r = null; throw new ArgumentException("Positive capture dimensions required"); }
            _catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot + "/Data/VFX120_Catalog.asset");
            _camera = Camera.main;
            if (_catalog == null || _camera == null) { _r = null; throw new InvalidOperationException("Open VFX review first"); }
            _index = _r.start; _frame = 0;
            _directory = Path.Combine(Vfx120Editor.Output, _r.folder); Directory.CreateDirectory(_directory);
            _target = new RenderTexture(_r.width, _r.height, 24, RenderTextureFormat.ARGB32); _target.Create();
            _readback = new Texture2D(_r.width, _r.height, TextureFormat.RGB24, false);
            _p = new Progress { status = "RUNNING", total = Mathf.Min(_r.count, Mathf.Max(0, _catalog.Entries.Length - _r.start)),
                folder = _r.folder, demonstrationCues = _r.demonstrationCues };
            EditorApplication.update += Tick;
            SaveProgress();
            return "CAPTURE_STARTED " + _directory;
        }

        static void Tick()
        {
            if (_r == null || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if (_index >= Mathf.Min(_catalog.Entries.Length, _r.start + _r.count)) { Finish(null); return; }
                var e = _catalog.Entries[_index];
                if (_effect == null)
                {
                    var root = UnityEngine.Object.Instantiate(e.Prefab);
                    root.hideFlags = HideFlags.DontSave;
                    _effect = root.GetComponent<Vfx120Effect>(); _effect.PreviewControlled = true;
                    _effect.DemonstrationCues = _r.demonstrationCues;
                    Transform primary = GameObject.Find("VFX Target")?.transform;
                    Transform secondary = null;
                    if (_r.demonstrationCues)
                    {
                        Vfx120Review.PrepareDemonstrationTargets(e.Glyph == "안", out primary, out secondary,
                            out var splitTargets, out _fixtureRoot);
                        _effect.SetSecondaryTargets(secondary, splitTargets);
                        _effect.SetAreaPlan(Vfx120Review.CreateDemonstrationAreaPlan(e.Profile));
                    }
                    _effect.Begin(new Vector3(0, 1, 0), primary, new Vector3(0, 1, 4), Color.white);
                    SaveEvidence(e, primary, secondary);
                }
                float t = _r.clip ? _frame / Mathf.Max(1, _r.fps) : SampleFractions[Mathf.Min(_frame, 4)] * _effect.Life;
                _effect.Sample(t);
                var previous = _camera.targetTexture; var active = RenderTexture.active;
                try
                {
                    _camera.targetTexture = _target; _camera.Render(); RenderTexture.active = _target;
                    _readback.ReadPixels(new Rect(0, 0, _r.width, _r.height), 0, 0); _readback.Apply();
                }
                finally { _camera.targetTexture = previous; RenderTexture.active = active; }
                string id = (_index + 1).ToString("000") + "_" + ((int)e.Glyph[0]).ToString("X4");
                string folder = _r.clip ? Path.Combine(_directory, id) : _directory; Directory.CreateDirectory(folder);
                string filename = _r.clip ? _frame.ToString("0000") + ".png" : id + "_" + _frame + ".png";
                File.WriteAllBytes(Path.Combine(folder, filename), _readback.EncodeToPNG());
                _frame++;
                int frameCount = FrameCount();
                if (_frame >= frameCount)
                {
                    UnityEngine.Object.DestroyImmediate(_effect.gameObject); _effect = null;
                    Vfx120Review.DestroyFixture(_fixtureRoot); _fixtureRoot = null;
                    _index++; _frame = 0; _p.completed++; SaveProgress();
                }
            }
            catch (Exception e) { Finish(e.ToString()); }
        }
        static int FrameCount() => _r.clip
            ? Mathf.Max(1, Mathf.CeilToInt(_r.fps * (_r.duration > 0 ? _r.duration : _effect.Life + .1f)))
            : _r.frames;

        static void SaveEvidence(Vfx120Catalog.Entry entry, Transform primary, Transform secondary)
        {
            var seconds = new float[FrameCount()];
            for (int i = 0; i < seconds.Length; i++)
                seconds[i] = _r.clip ? i / _r.fps : SampleFractions[i] * _effect.Life;
            var evidence = new CaptureEvidence
            {
                capturedUtc = DateTime.UtcNow.ToString("o"),
                loadedRuntimeAssemblyMvid = typeof(Vfx120Effect).Assembly.ManifestModule.ModuleVersionId.ToString(),
                loadedRuntimeAssemblyLastWriteUtc = File.GetLastWriteTimeUtc(typeof(Vfx120Effect).Assembly.Location).ToString("o"),
                glyph = entry.Glyph, title = entry.Profile.Title, demonstrationCues = _r.demonstrationCues,
                gameplayConnectionVerified = false, sampledSeconds = seconds,
                impactTime = _effect.ReceivedImpactClock > 0 ? _effect.ReceivedImpactClock : entry.Profile.Flight,
                life = _effect.Life, primaryTarget = primary != null ? primary.name : "fallback point",
                secondaryTarget = secondary != null ? secondary.name : "none",
                eventSource = _r.demonstrationCues
                    ? "Review-only: hit at flight for 녹/안; later C4 hit or 간 defeat at max(flight+0.3,life*0.48); 검 release at life*0.77. No gameplay events are dispatched."
                    : "No demonstration events. Only explicitly supplied presentation cues are sampled."
            };
            var area = _effect.ReceivedAreaPlan;
            evidence.areaPlanSource = area != null ? "REVIEW_PROFILE_ILLUSTRATION_NOT_COMBAT_PLAN" : "none";
            if (area != null)
            {
                evidence.areaShape = area.Shape.ToString(); evidence.areaPoint = area.Point;
                evidence.areaDirection = area.Direction; evidence.areaRadius = area.Radius;
                evidence.areaAngle = area.Angle; evidence.areaLength = area.Length;
                evidence.areaDelay = area.Delay; evidence.areaSpeed = area.Speed;
            }
            string id = (_index + 1).ToString("000") + "_" + ((int)entry.Glyph[0]).ToString("X4");
            File.WriteAllText(Path.Combine(_directory, id + "_capture.json"), JsonUtility.ToJson(evidence, true));
        }
        static void SaveProgress()
        {
            _p.index = _index; _p.frame = _frame;
            File.WriteAllText(Path.Combine(Vfx120Editor.Output, "capture_progress.json"), JsonUtility.ToJson(_p, true));
        }
        static void Finish(string error)
        {
            _p.status = error == null ? "COMPLETE" : "FAILED"; _p.error = error; SaveProgress();
            EditorApplication.update -= Tick;
            if (_effect != null) UnityEngine.Object.DestroyImmediate(_effect.gameObject);
            Vfx120Review.DestroyFixture(_fixtureRoot); _fixtureRoot = null;
            if (_target != null) { _target.Release(); UnityEngine.Object.DestroyImmediate(_target); }
            if (_readback != null) UnityEngine.Object.DestroyImmediate(_readback);
            _r = null;
        }
    }
}
