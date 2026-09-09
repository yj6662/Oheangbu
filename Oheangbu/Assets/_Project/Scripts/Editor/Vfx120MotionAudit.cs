using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.SpellVFX120;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // Edit-mode numerical audit, deliberately separate from Play lifetime/particle/render tests.
    public static class Vfx120MotionAudit
    {
        private const string CatalogPath = "Assets/_Project/Art/SpellVFX120/Data/VFX120_Catalog.asset";
        private const float Tolerance = 0.00001f;
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int[] Rates = { 30, 60, 120 };
        [Serializable] private sealed class Row
        {
            public string glyph, profile, status, error;
            public float life, maximumPositionMagnitude, maximumScaleMagnitude, maximumSameTimeDelta, maximumEndAlpha;
            public int samples, equalTimeComparisons, parts, meshRenderers, ribbons, particleSystems, particleBudget, gameObjects;
        }
        [Serializable] private sealed class AssetCheck
        {
            public string path, kind, before, after, status, error;
        }
        [Serializable] private sealed class Report
        {
            public string status, unityVersion, output;
            public string mode = "EDIT_MODE_ANALYTIC_SAMPLE_ONLY";
            public string scope = "120 catalog profiles; actual Begin/Sample at 30/60/120 fps, all transform/ribbon coordinates, common-time comparisons, endpoint alpha, finite nonnegative scale, instance budget and source asset immutability.";
            public string runtimeDestruction = "UNVERIFIED: Update/Destroy, delayed destruction and concurrent-instance resource leaks require Play mode.";
            public string particleMotion = "UNVERIFIED: PreviewControlled=false; particles stopped after Begin; no ParticleSystem.Simulate or rendering is performed.";
            public string artGameplayPerformance = "UNVERIFIED: no visual quality, impact/damage timing, particle completion, GPU/CPU frame budget or in-game presentation PASS.";
            public string budget = "At most 32 body parts + max(body count, required accents <=12) accent parts, 1 motif, 3 ribbons, 1 atmosphere root, 3 particle systems; 73 GameObjects including effect root and total native maxParticles <=360. Runtime shared motif mesh cache is not a per-instance resource and is not a leak assertion.";
            public int entries, passed, failed, sampledFrames;
            public bool temporarySceneClosed;
            public float tolerance = Tolerance;
            public int[] fps = Rates;
            public List<Row> rows = new List<Row>();
            public List<AssetCheck> assets = new List<AssetCheck>();
            public List<string> errors = new List<string>();
        }
        private sealed class Source
        {
            public Object asset;
            public AssetCheck check;
        }
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        private static bool Finite(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);

        public static string Run()
        {
            var report = new Report { unityVersion = Application.unityVersion };
            string repo = Directory.GetParent(Application.dataPath).Parent.FullName;
            report.output = Path.Combine(repo, "Art/SpellVFX120/motion_audit.json");
            Scene preview = default;
            var sources = new List<Source>();
            try
            {
                Need(!EditorApplication.isPlayingOrWillChangePlaymode, "Run only in stopped Edit mode; no live gameplay objects are touched.");
                var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(CatalogPath);
                Need(catalog != null && catalog.Entries != null, "Catalog is unavailable.");
                report.entries = catalog.Entries.Length;
                Need(report.entries == 120, "Expected exactly 120 entries.");
                var glyphs = new HashSet<string>();
                var seen = new HashSet<Object>();
                foreach (var entry in catalog.Entries)
                {
                    Need(!string.IsNullOrEmpty(entry.Glyph) && glyphs.Add(entry.Glyph), "Missing or duplicate glyph: " + entry.Glyph);
                    Need(entry.Profile != null && entry.Profile.Glyph == entry.Glyph, "Profile/glyph mismatch: " + entry.Glyph);
                    var p = entry.Profile;
                    foreach (Object asset in new Object[] { p, p.BodyMesh, p.AccentMesh, p.BodyMaterial, p.InkMaterial, p.PatternMaterial, p.MistMaterial })
                    {
                        if (asset == null || !seen.Add(asset)) continue;
                        var source = new Source { asset = asset, check = new AssetCheck { path = AssetDatabase.GetAssetPath(asset), kind = asset.GetType().Name } };
                        try { source.check.before = Fingerprint(asset); }
                        catch (Exception e) { source.check.status = "UNVERIFIED_BUFFER"; source.check.error = e.Message; }
                        sources.Add(source); report.assets.Add(source.check);
                    }
                }
                preview = EditorSceneManager.NewPreviewScene();
                foreach (var entry in catalog.Entries)
                {
                    var row = new Row { glyph = entry.Glyph, profile = AssetDatabase.GetAssetPath(entry.Profile) };
                    report.rows.Add(row);
                    try { RunProfile(entry.Profile, preview, row); row.status = "PASS_ANALYTIC_SAMPLE_ONLY"; report.passed++; }
                    catch (Exception e) { row.status = "FAIL"; row.error = e.Message; report.failed++; }
                    report.sampledFrames += row.samples;
                }
            }
            catch (Exception e) { report.errors.Add(e.Message); }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                report.temporarySceneClosed = !preview.IsValid();
                foreach (var source in sources)
                {
                    try
                    {
                        source.check.after = Fingerprint(source.asset);
                        if (source.check.before != null)
                            source.check.status = source.check.before == source.check.after ? "PASS_UNCHANGED" : "FAIL_SOURCE_MUTATED";
                    }
                    catch (Exception e) { source.check.status = "UNVERIFIED_BUFFER"; source.check.error = e.Message; }
                }
            }
            bool assetsPass = report.assets.Count > 0 && report.assets.TrueForAll(x => x.status == "PASS_UNCHANGED");
            report.status = report.errors.Count == 0 && report.passed == 120 && report.failed == 0 && assetsPass && report.temporarySceneClosed
                ? "PASS_ANALYTIC_SAMPLE_ONLY" : "FAIL_OR_UNVERIFIED";
            Directory.CreateDirectory(Path.GetDirectoryName(report.output));
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(report.output, json);
            return json;
        }

        private static void RunProfile(Vfx120Profile profile, Scene preview, Row row)
        {
            Need(profile.BodyMesh != null && profile.AccentMesh != null && profile.BodyMaterial != null && profile.InkMaterial != null && profile.PatternMaterial != null, "Missing mesh/material source.");
            Need(profile.Count >= 1 && profile.Count <= 32 && profile.RibbonCount >= 0 && profile.RibbonCount <= 3, "Authored profile exceeds instance part budget.");
            var common = new List<float[]>();
            float[] endpoint = null;
            foreach (int fps in Rates)
            {
                var go = new GameObject("Vfx120MotionAudit_" + profile.Glyph + "_" + fps) { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(go, preview);
                try
                {
                    // Only the actual runtime component and actual catalog profile are used.
                    var fx = go.AddComponent<Vfx120Effect>(); fx.Profile = profile; fx.PreviewControlled = false;
                    Vector3 origin = new Vector3(1.3f, 1.4f, -.9f);
                    fx.Begin(origin, null, new Vector3(3.6f, .1f, 7.2f), Color.magenta);
                    Need(fx.Begun && Finite(fx.Life) && fx.Life > 0 && fx.Life <= 120, "Begin did not establish a finite, bounded positive lifetime.");
                    if (fps == 30) row.life = fx.Life;
                    else Need(fx.Life == row.life, "Lifetime changed between frame rates.");
                    Need((go.transform.position - origin).sqrMagnitude < Tolerance * Tolerance && go.transform.localScale == Vector3.one, "Begin failed to normalize origin/scale.");
                    var transforms = go.GetComponentsInChildren<Transform>(true);
                    var renderers = go.GetComponentsInChildren<Renderer>(true);
                    var meshes = go.GetComponentsInChildren<MeshRenderer>(true);
                    var lines = go.GetComponentsInChildren<LineRenderer>(true);
                    var particles = go.GetComponentsInChildren<ParticleSystem>(true);
                    foreach (var t in transforms) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                    foreach (var ps in particles) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    int particleBudget = 0;
                    foreach (var ps in particles) particleBudget += ps.main.maxParticles;
                    Need(fx.PartCount <= 32 && meshes.Length <= 65 && lines.Length <= 3 && particles.Length <= 3 && particleBudget <= 360 && transforms.Length <= 73, "Actual instance exceeds updated part/component/native-particle budget.");
                    Need(go.GetComponentsInChildren<Collider>(true).Length == 0 && go.GetComponentsInChildren<Rigidbody>(true).Length == 0, "Presentation created gameplay collision/physics components.");
                    row.parts = Math.Max(row.parts, fx.PartCount); row.meshRenderers = Math.Max(row.meshRenderers, meshes.Length);
                    row.ribbons = Math.Max(row.ribbons, lines.Length); row.particleSystems = Math.Max(row.particleSystems, particles.Length);
                    row.particleBudget = Math.Max(row.particleBudget, particleBudget);
                    row.gameObjects = Math.Max(row.gameObjects, transforms.Length);
                    int step = fps / 30;
                    int frames = Mathf.FloorToInt(fx.Life * fps);
                    var lineBuffer = new Vector3[48];
                    for (int frame = 0; frame <= frames; frame++)
                    {
                        float seconds = frame / (float)fps;
                        fx.Sample(seconds); row.samples++;
                        Need(Mathf.Abs(fx.Age - seconds) <= Tolerance, "Sample did not preserve requested age.");
                        InspectFinite(transforms, lines, lineBuffer, row);
                        if (frame % step != 0) continue;
                        float[] state = Snapshot(transforms, lines, lineBuffer);
                        if (fps == 30) common.Add(state);
                        else Compare(common[frame / step], state, row);
                    }
                    fx.Sample(fx.Life); row.samples++;
                    InspectFinite(transforms, lines, lineBuffer, row);
                    float[] end = Snapshot(transforms, lines, lineBuffer);
                    if (fps == 30) endpoint = end; else Compare(endpoint, end, row);
                    var block = new MaterialPropertyBlock();
                    foreach (var renderer in renderers)
                    {
                        if (renderer is ParticleSystemRenderer) continue;
                        renderer.GetPropertyBlock(block);
                        float alpha = block.GetFloat(AlphaId);
                        Need(Finite(alpha), "Nonfinite endpoint alpha.");
                        row.maximumEndAlpha = Mathf.Max(row.maximumEndAlpha, Mathf.Abs(alpha));
                        Need(Mathf.Abs(alpha) <= Tolerance, "Analytic renderer remains visible at Life: " + renderer.name);
                    }
                    foreach (var line in lines) Need(Mathf.Abs(line.widthMultiplier) <= Tolerance, "Ribbon width remains at Life.");
                    fx.Sample(fx.Life + 1f); row.samples++;
                    InspectFinite(transforms, lines, lineBuffer, row);
                    // Reverse order detects hidden state accumulation after the end of a run.
                    for (int i = common.Count - 1; i >= 0; i--)
                    {
                        fx.Sample(i / 30f); row.samples++;
                        Compare(common[i], Snapshot(transforms, lines, lineBuffer), row);
                    }
                    Need(go.GetComponentsInChildren<Transform>(true).Length == transforms.Length, "Sample created unbounded child objects.");
                }
                finally { if (go != null) Object.DestroyImmediate(go); }
            }
        }

        private static void InspectFinite(Transform[] transforms, LineRenderer[] lines, Vector3[] buffer, Row row)
        {
            foreach (var t in transforms)
            {
                Vector3 p = t.position, s = t.localScale; Quaternion q = t.rotation;
                Need(Finite(p) && Finite(s) && Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w), "Nonfinite transform: " + t.name);
                Need(s.x >= 0 && s.y >= 0 && s.z >= 0, "Negative scale: " + t.name);
                row.maximumPositionMagnitude = Mathf.Max(row.maximumPositionMagnitude, p.magnitude);
                row.maximumScaleMagnitude = Mathf.Max(row.maximumScaleMagnitude, s.magnitude);
            }
            foreach (var line in lines)
            {
                Need(line.positionCount <= buffer.Length && Finite(line.widthMultiplier), "Unbounded/nonfinite ribbon.");
                int count = line.GetPositions(buffer);
                for (int i = 0; i < count; i++) Need(Finite(buffer[i]), "Nonfinite ribbon coordinate.");
            }
        }

        private static float[] Snapshot(Transform[] transforms, LineRenderer[] lines, Vector3[] buffer)
        {
            int size = transforms.Length * 10;
            foreach (var line in lines) size += line.positionCount * 3 + 1;
            var result = new float[size]; int n = 0;
            foreach (var t in transforms)
            {
                Vector3 p = t.position, s = t.localScale; Quaternion q = t.rotation;
                result[n++] = p.x; result[n++] = p.y; result[n++] = p.z;
                result[n++] = q.x; result[n++] = q.y; result[n++] = q.z; result[n++] = q.w;
                result[n++] = s.x; result[n++] = s.y; result[n++] = s.z;
            }
            foreach (var line in lines)
            {
                int count = line.GetPositions(buffer);
                for (int i = 0; i < count; i++) { result[n++] = buffer[i].x; result[n++] = buffer[i].y; result[n++] = buffer[i].z; }
                result[n++] = line.widthMultiplier;
            }
            return result;
        }
        private static void Compare(float[] expected, float[] actual, Row row)
        {
            Need(expected != null && expected.Length == actual.Length, "Sample topology changed between rates.");
            for (int i = 0; i < actual.Length; i++)
            {
                Need(Finite(actual[i]), "Nonfinite common-time state.");
                float delta = Mathf.Abs(expected[i] - actual[i]);
                row.maximumSameTimeDelta = Mathf.Max(row.maximumSameTimeDelta, delta);
                Need(delta <= Tolerance, "Different transform/ribbon state at identical age, delta=" + delta);
            }
            row.equalTimeComparisons++;
        }

        [Serializable] private sealed class NativeRow
        {
            public string glyph, status, error;
            public float life, peakTime, emissionEndsAt, maximumParticleLifetime, maximumSameTimeDelta;
            public int systems, particleBudget, peakParticles, endParticles, samples, repeatComparisons;
            public int[] peakPerSystem;
        }
        [Serializable] private sealed class NativeReport
        {
            public string status, unityVersion, output;
            public string mode = "EDIT_MODE_SEEDED_NATIVE_PREVIEW_SAMPLES_ONLY";
            public string scope = "Ten representatives (two per element), actual effect Begin/Sample with PreviewControlled=true. At 30/60/120 probe grids: peak-minus-one-frame, exact common peak, end-minus-one-frame, exact Life, then peak replay. Native particle buffers are read for finite position/velocity/size/rotation/lifetime, <=360 budget, repeatable common-time state and zero end count. No per-frame whole-life simulation or image rendering.";
            public string runtime = "UNVERIFIED: these fps values define approach probes, not continuous Play frame rates. Native runtime Update/timeScale/culling/concurrent-instance leakage and gameplay timing remain untested.";
            public string visuals = "UNVERIFIED: valid particles/counts do not demonstrate rendered visibility, optical color, collision-free flight or art quality. Local-space preview does not prove a world-space historical emitter trail.";
            public int passed, failed;
            public bool temporarySceneClosed;
            public int[] fps = Rates;
            public List<NativeRow> rows = new List<NativeRow>();
            public List<AssetCheck> materials = new List<AssetCheck>();
            public List<string> errors = new List<string>();
        }
        private sealed class NativeState
        {
            public int[] counts;
            public uint[] seeds;
            public float[] values;
        }

        public static string RunNativeParticles()
        {
            var report = new NativeReport { unityVersion = Application.unityVersion };
            string repo = Directory.GetParent(Application.dataPath).Parent.FullName;
            report.output = Path.Combine(repo, "Art/SpellVFX120/native_particle_audit.json");
            Scene preview = default;
            var sources = new List<Source>();
            var seen = new HashSet<Object>();
            string[] glyphs = { "가", "곰", "나", "논", "마", "목", "사", "소", "앗", "옥" };
            try
            {
                Need(!EditorApplication.isPlayingOrWillChangePlaymode, "Native preview audit requires stopped Edit mode.");
                var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(CatalogPath);
                Need(catalog != null && catalog.Entries != null, "Catalog is unavailable.");
                preview = EditorSceneManager.NewPreviewScene();
                foreach (string glyph in glyphs)
                {
                    var row = new NativeRow { glyph = glyph }; report.rows.Add(row);
                    try
                    {
                        Vfx120Profile profile = null;
                        foreach (var entry in catalog.Entries) if (entry.Glyph == glyph) { Need(profile == null, "Duplicate representative glyph."); profile = entry.Profile; }
                        Need(profile != null, "Representative profile is missing.");
                        foreach (Object material in new Object[] { profile.BodyMaterial, profile.InkMaterial, profile.PatternMaterial, profile.MistMaterial })
                        {
                            if (material == null || !seen.Add(material)) continue;
                            var source = new Source { asset = material, check = new AssetCheck { path = AssetDatabase.GetAssetPath(material), kind = "Material", before = Fingerprint(material) } };
                            sources.Add(source); report.materials.Add(source.check);
                        }
                        RunNativeProfile(profile, preview, row);
                        row.status = "PASS_SEEDED_NATIVE_PREVIEW_ONLY"; report.passed++;
                    }
                    catch (Exception e) { row.status = "FAIL"; row.error = e.Message; report.failed++; }
                }
            }
            catch (Exception e) { report.errors.Add(e.Message); }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                report.temporarySceneClosed = !preview.IsValid();
                foreach (var source in sources)
                {
                    try { source.check.after = Fingerprint(source.asset); source.check.status = source.check.before == source.check.after ? "PASS_UNCHANGED" : "FAIL_SOURCE_MUTATED"; }
                    catch (Exception e) { source.check.status = "UNVERIFIED"; source.check.error = e.Message; }
                }
            }
            bool assetsPass = report.materials.Count > 0 && report.materials.TrueForAll(x => x.status == "PASS_UNCHANGED");
            report.status = report.passed == 10 && report.failed == 0 && report.errors.Count == 0 && assetsPass && report.temporarySceneClosed
                ? "PASS_SEEDED_NATIVE_PREVIEW_ONLY" : "FAIL_OR_UNVERIFIED";
            Directory.CreateDirectory(Path.GetDirectoryName(report.output));
            string json = JsonUtility.ToJson(report, true); File.WriteAllText(report.output, json); return json;
        }

        private static void RunNativeProfile(Vfx120Profile profile, Scene preview, NativeRow row)
        {
            NativeState reference = null;
            var buffer = new ParticleSystem.Particle[360];
            foreach (int fps in Rates)
            {
                var go = new GameObject("Vfx120NativeAudit_" + profile.Glyph + "_" + fps) { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(go, preview);
                try
                {
                    var effect = go.AddComponent<Vfx120Effect>(); effect.Profile = profile; effect.PreviewControlled = true;
                    effect.Begin(new Vector3(1.3f, 1.4f, -.9f), null, new Vector3(3.6f, .1f, 7.2f), Color.magenta);
                    Need(effect.Begun && Finite(effect.Life) && effect.Life > 0, "Effect did not begin.");
                    var atmosphere = go.GetComponentInChildren<Vfx120Atmosphere>(true);
                    Need(atmosphere != null, "Actual effect did not create the atmosphere.");
                    var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                    foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                    Need(systems.Length > 0 && systems.Length <= 3, "Native system budget exceeded or no systems created.");
                    int budget = 0;
                    foreach (var ps in systems)
                    {
                        budget += ps.main.maxParticles;
                        Need(!ps.main.loop && !ps.main.useUnscaledTime && !ps.useAutoRandomSeed, "Native loop/time/seed contract mismatch.");
                        Need(ps.main.startColor.color == Color.white, "Native palette is multiplied by nonwhite startColor.");
                    }
                    Need(budget <= 360 && atmosphere.ParticleBudget == budget, "Native particle cap is not bounded or differs from declared budget.");
                    Need(Finite(atmosphere.EmissionEndsAt) && Finite(atmosphere.MaximumParticleLifetime)
                        && atmosphere.EmissionEndsAt + atmosphere.MaximumParticleLifetime <= effect.Life + Tolerance, "Final emission can outlive root Life.");
                    row.systems = systems.Length; row.particleBudget = budget;
                    row.life = effect.Life; row.emissionEndsAt = atmosphere.EmissionEndsAt; row.maximumParticleLifetime = atmosphere.MaximumParticleLifetime;
                    float peak = atmosphere.EmissionEndsAt * .82f;
                    if (fps == 30) row.peakTime = peak; else Need(Mathf.Abs(row.peakTime - peak) <= Tolerance, "Preview peak clock changed between grids.");

                    effect.Sample(Mathf.Max(0, peak - 1f / fps)); row.samples++;
                    ReadNative(systems, buffer);
                    effect.Sample(peak); row.samples++;
                    NativeState state = ReadNative(systems, buffer);
                    int peakCount = Sum(state.counts);
                    Need(peakCount > 0 && peakCount <= budget, "No native particles at the emission peak, or count exceeds cap.");
                    if (reference == null) { reference = state; row.peakPerSystem = state.counts; row.peakParticles = peakCount; }
                    else CompareNative(reference, state, row);
                    effect.Sample(Mathf.Max(0, effect.Life - 1f / fps)); row.samples++;
                    ReadNative(systems, buffer);
                    effect.Sample(effect.Life); row.samples++;
                    int endCount = Sum(ReadNative(systems, buffer).counts);
                    row.endParticles = Math.Max(row.endParticles, endCount);
                    Need(endCount == 0 && atmosphere.LiveParticleCount == 0, "Residual native particles at root Life.");
                    effect.Sample(peak); row.samples++;
                    CompareNative(reference, ReadNative(systems, buffer), row);
                }
                finally { if (go != null) Object.DestroyImmediate(go); }
            }
        }

        private static NativeState ReadNative(ParticleSystem[] systems, ParticleSystem.Particle[] buffer)
        {
            var counts = new int[systems.Length];
            var values = new List<float>(360 * 18);
            var seeds = new List<uint>(360);
            for (int system = 0; system < systems.Length; system++)
            {
                var ps = systems[system];
                int count = ps.GetParticles(buffer); counts[system] = count;
                Need(count == ps.particleCount && count <= ps.main.maxParticles, "Native buffer truncated or exceeds system cap.");
                for (int i = 0; i < count; i++)
                {
                    var particle = buffer[i];
                    Vector3 position = particle.position, velocity = particle.velocity, rotation = particle.rotation3D, size = particle.GetCurrentSize3D(ps);
                    Need(Finite(position) && Finite(velocity) && Finite(rotation) && Finite(size)
                        && Finite(particle.remainingLifetime) && Finite(particle.startLifetime), "Nonfinite actual native particle state.");
                    Need(size.x >= 0 && size.y >= 0 && size.z >= 0 && particle.startLifetime > 0 && particle.remainingLifetime >= 0, "Invalid actual native particle size/lifetime.");
                    Color32 color = particle.GetCurrentColor(ps);
                    seeds.Add(particle.randomSeed);
                    values.Add(position.x); values.Add(position.y); values.Add(position.z);
                    values.Add(velocity.x); values.Add(velocity.y); values.Add(velocity.z);
                    values.Add(rotation.x); values.Add(rotation.y); values.Add(rotation.z);
                    values.Add(size.x); values.Add(size.y); values.Add(size.z);
                    values.Add(particle.remainingLifetime); values.Add(particle.startLifetime);
                    values.Add(color.r); values.Add(color.g); values.Add(color.b); values.Add(color.a);
                }
            }
            Need(Sum(counts) <= 360, "Combined native count exceeds budget.");
            return new NativeState { counts = counts, seeds = seeds.ToArray(), values = values.ToArray() };
        }
        private static void CompareNative(NativeState expected, NativeState actual, NativeRow row)
        {
            Need(expected.counts.Length == actual.counts.Length && expected.seeds.Length == actual.seeds.Length && expected.values.Length == actual.values.Length, "Native common-time count changed.");
            for (int i = 0; i < actual.counts.Length; i++) Need(expected.counts[i] == actual.counts[i], "Native common-time per-system count changed.");
            for (int i = 0; i < actual.seeds.Length; i++) Need(expected.seeds[i] == actual.seeds[i], "Native common-time particle seed/order changed.");
            for (int i = 0; i < actual.values.Length; i++)
            {
                float delta = Mathf.Abs(expected.values[i] - actual.values[i]);
                row.maximumSameTimeDelta = Mathf.Max(row.maximumSameTimeDelta, delta);
                Need(delta <= Tolerance, "Native common-time particle state changed, delta=" + delta);
            }
            row.repeatComparisons++;
        }
        private static int Sum(int[] values) { int total = 0; foreach (int value in values) total += value; return total; }

        private static string Fingerprint(Object asset)
        {
            // Material/profile serialized values include original colors; per-renderer MPBs must not change these.
            if (!(asset is Mesh mesh))
            {
                using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(EditorJsonUtility.ToJson(asset))));
            }
            Need(mesh.isReadable, "Mesh CPU buffers are unavailable; not claiming unchanged geometry: " + mesh.name);
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory))
            {
                writer.Write(mesh.vertexCount); writer.Write(mesh.subMeshCount); writer.Write(mesh.blendShapeCount);
                foreach (Vector3 p in mesh.vertices) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
                foreach (Vector3 p in mesh.normals) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
                foreach (Vector4 p in mesh.tangents) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); writer.Write(p.w); }
                var uv = new List<Vector4>();
                for (int channel = 0; channel < 8; channel++)
                {
                    mesh.GetUVs(channel, uv); writer.Write(uv.Count);
                    foreach (Vector4 p in uv) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); writer.Write(p.w); }
                }
                foreach (Color c in mesh.colors) { writer.Write(c.r); writer.Write(c.g); writer.Write(c.b); writer.Write(c.a); }
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    writer.Write((int)mesh.GetTopology(sub)); var indices = mesh.GetIndices(sub); writer.Write(indices.Length);
                    foreach (int index in indices) writer.Write(index);
                }
                Bounds bounds = mesh.bounds;
                writer.Write(bounds.center.x); writer.Write(bounds.center.y); writer.Write(bounds.center.z);
                writer.Write(bounds.size.x); writer.Write(bounds.size.y); writer.Write(bounds.size.z);
                writer.Flush();
                using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(memory.ToArray()));
            }
        }
        private static string Hex(byte[] data) => BitConverter.ToString(data).Replace("-", "").ToLowerInvariant();
    }
}
