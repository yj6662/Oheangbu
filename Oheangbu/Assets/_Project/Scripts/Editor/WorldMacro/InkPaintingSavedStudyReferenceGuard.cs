using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Read-only saved receipt/reference proof, captured before Play and checked across scene reload.</summary>
    public static class InkPaintingSavedStudyReferenceGuard
    {
        const string ScenePath = "Assets/_Project/Scenes/World/W_Demo_Compact.unity";
        const string Flow = "Art/World/WorldMacro/Compact/InkLandscape/InkPaintingStudy/FlowRevision/";
        [Serializable] public sealed class Reference
        { public string hierarchy, component, property, asset, guid; public long localId; }
        [Serializable] public sealed class Snapshot
        {
            public string navigationGeneration, geometryGeneration, navigationReceiptSha256, geometryReceiptSha256, lookReceiptSha256;
            public string sourceFingerprint, geography, dressing, delta, absolute;
            public int cells; public Reference[] references;
            public int runtimeCheckCalls, runtimeResolveCalls;
            public double runtimeCheckTotalMilliseconds, runtimeCheckMaximumMilliseconds, runtimeResolveTotalMilliseconds;
        }
        [Serializable] sealed class Mapping { public string source = null, derived = null; public long sourceLocalId = 0; }
        [Serializable] sealed class Stamp { public string path = null, sha256 = null; }
        [Serializable] sealed class Look
        {
            public string status = null, scene = null, sceneAfterSha256 = null;
            public bool sceneSaved = false, sourcesUnchanged = false, populationUnchanged = false, supplementaryPlacementUnchanged = false, structureUnchanged = false;
            public Mapping[] sheets = null, skies = null; public Stamp[] sources = null;
        }
        static string Workspace => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        sealed class RuntimeBinding { public Component target; public FieldInfo field; public Object expected; public string label; }
        sealed class RuntimeCache
        {
            public Snapshot snapshot; public RuntimeBinding[] bindings;
            public WorldMacroDressingRenderer renderer; public WorldMacroDressingSheetSO sheet;
            public WorldMacroSheetSO geography; public WorldMacroSurfaceDeformationSO delta; public WorldMacroFinalSurfaceSO absolute;
        }
        static RuntimeCache runtimeCache;
        static string runtimeCacheFailure;
        static InkPaintingSavedStudyReferenceGuard()
        {
            SceneManager.sceneUnloaded += _ => ClearRuntimeCache();
            AssemblyReloadEvents.beforeAssemblyReload += ClearRuntimeCache;
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.ExitingEditMode) ClearRuntimeCache();
            };
        }
        static void ClearRuntimeCache() { runtimeCache = null; runtimeCacheFailure = null; }
        static RuntimeCache ResolveRuntimeCache(Snapshot snapshot)
        {
            // One traversal per Play/reload. Hierarchies are calculated only for
            // requested component types, once per candidate, never per proof row.
            var types = new HashSet<string>(snapshot.references.Select(row => row.component), StringComparer.Ordinal);
            var components = Components(); var indexed = new Dictionary<string, Component>(StringComparer.Ordinal);
            foreach (var component in components)
            {
                string type = component.GetType().FullName; if (!types.Contains(type)) continue;
                string key = type + "\n" + Hierarchy(component.transform);
                Require(!indexed.ContainsKey(key), "Ambiguous runtime component " + key); indexed.Add(key, component);
            }
            var bindings = new List<RuntimeBinding>(snapshot.references.Length);
            foreach (var row in snapshot.references)
            {
                Require(indexed.TryGetValue(row.component + "\n" + row.hierarchy, out var component), "Missing runtime component " + row.hierarchy);
                FieldInfo field = null;
                for (Type type = component.GetType(); type != null && field == null; type = type.BaseType)
                    field = type.GetField(row.property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                Require(field != null && typeof(Object).IsAssignableFrom(field.FieldType), "Unsupported runtime reference field " + row.property);
                var expected = AssetDatabase.LoadAllAssetsAtPath(row.asset).SingleOrDefault(value => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id) && guid == row.guid && id == row.localId);
                Require(expected != null, "Missing exact receipted asset " + row.asset);
                bindings.Add(new RuntimeBinding { target = component, field = field, expected = expected, label = row.hierarchy + ":" + row.property });
            }
            return new RuntimeCache { snapshot = snapshot, bindings = bindings.ToArray(), renderer = components.OfType<WorldMacroDressingRenderer>().Single(),
                sheet = AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(snapshot.dressing), geography = AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(snapshot.geography),
                delta = AssetDatabase.LoadAssetAtPath<WorldMacroSurfaceDeformationSO>(snapshot.delta), absolute = AssetDatabase.LoadAssetAtPath<WorldMacroFinalSurfaceSO>(snapshot.absolute) };
        }
        static string Full(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(path.StartsWith("Assets/", StringComparison.Ordinal) ? Path.GetDirectoryName(Application.dataPath) : Workspace, path));
        static string FileHash(string path) { using var sha = SHA256.Create(); using var input = File.OpenRead(Full(path)); return Hex(sha.ComputeHash(input)); }
        static string TextHash(string value) { using var sha = SHA256.Create(); return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        static string Hex(byte[] value) => BitConverter.ToString(value).Replace("-", "").ToLowerInvariant();
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Saved study references: " + message); }
        static Component[] Components()
        {
            var scene = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Component>(true)).Where(v => v != null);
            // The authored UI explicitly moves to DontDestroyOnLoad in Awake.
            // Follow that exact singleton; do not search arbitrary additive scenes.
            if (Application.isPlaying && PlaytestUiRoot.Instance != null)
                scene = scene.Concat(PlaytestUiRoot.Instance.GetComponentsInChildren<Component>(true));
            return scene.Distinct().ToArray();
        }
        static string Hierarchy(Transform t) => t.parent == null ? t.name : Hierarchy(t.parent) + "/" + t.name;
        static Object Read(Component component, string property)
        {
            using var serialized = new SerializedObject(component); var field = serialized.FindProperty(property);
            Require(field != null && field.propertyType == SerializedPropertyType.ObjectReference, "Missing object field " + Hierarchy(component.transform) + ":" + property);
            return field.objectReferenceValue;
        }
        static string Key(Object value)
        {
            if (value == null) return "null";
            Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id), "Unidentified asset " + value.name);
            return guid + ":" + id;
        }
        static string JsonHash(Object value)
        {
            string text = Regex.Replace(JsonUtility.ToJson(value), "(?<!\\\\)\"instanceID\"\\s*:\\s*(-?[0-9]+)", match => {
                int id = int.Parse(match.Groups[1].Value); if (id == 0) return "\"asset\":\"null\"";
#pragma warning disable CS0618
                var item = EditorUtility.InstanceIDToObject(id);
#pragma warning restore CS0618
                Require(item != null, "Unresolved canonical instance ID " + id); return "\"asset\":\"" + Key(item) + "\"";
            });
            return TextHash(text);
        }
        static T Clean<T>(string path) where T : Object
        {
            var value = AssetDatabase.LoadAssetAtPath<T>(path);
            Require(value != null && EditorUtility.IsPersistent(value) && !EditorUtility.IsDirty(value), "Expected clean asset " + path); return value;
        }

        public static Snapshot Capture(int publishedCells)
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && SceneManager.GetActiveScene().path == ScenePath && !SceneManager.GetActiveScene().isDirty, "Clean compact Edit scene required");
            // Start has already called RequireCurrentBake. Repeat identity-only checks
            // here so this standalone read-only API never accepts a different ledger.
            string navPath = Flow + "NavigationUpdate/progress.json";
            var nav = JsonUtility.FromJson<InkPaintingNavigationUpdate.Progress>(File.ReadAllText(Full(navPath)));
            Require(nav != null && nav.installed && nav.sceneSaved && nav.next == 20 && nav.status == "INSTALLED_SAVED_EDIT_PATHS_VERIFIED" && nav.scene == ScenePath, "Installed navigation receipt required");
            Require(FileHash(ScenePath) == nav.sceneAfterSha256 && FileHash(nav.applicationReceiptPath) == nav.applicationReceiptSha256, "Navigation scene/application identity mismatch");
            var app = JsonUtility.FromJson<InkPaintingMountainApplication.ApplicationReceipt>(File.ReadAllText(Full(nav.applicationReceiptPath)));
            Require(app != null && app.status == "APPLIED_GEOMETRY_SAVED" && app.applied && app.sceneSaved && app.scene == ScenePath && app.generation == nav.geometryGeneration &&
                app.sceneAfterSha256 == nav.sceneBeforeSha256 && app.sourceFilesUnchanged && app.selectionDataUnchanged && app.protectedGeometryPreserved && app.exactSurfaceBindingsVerified && app.sourceSceneChainVerified,
                "Geometry application/preservation chain mismatch");
            Require(FileHash(app.lookApplicationReceiptPath) == app.lookReceiptSha256, "Look receipt changed");
            var look = JsonUtility.FromJson<Look>(File.ReadAllText(Full(app.lookApplicationReceiptPath)));
            Require(look != null && look.status == "PASS_PERSISTENT_APPLICATION" && look.scene == ScenePath && look.sceneSaved && look.sourcesUnchanged && look.populationUnchanged &&
                look.supplementaryPlacementUnchanged && look.structureUnchanged && look.sceneAfterSha256 == app.sceneBeforeSha256 && look.sheets != null && look.skies != null, "Saved look chain incomplete");
            var components = Components();
            var session = components.OfType<WorldMacroPlaytestSession>().Single(); var ui = components.OfType<PlaytestUiRoot>().Single();
            var summon = components.OfType<WorldMacroPalanquinSummon>().Single(); var renderer = components.OfType<WorldMacroDressingRenderer>().Single();
            Require(session.Content != null && ui.Content == session.Content && ui.MapData != null && ui.MapData.IsUsable && renderer.Sheet != null &&
                summon.WorldSheet != null && ui.WorldSheet == summon.WorldSheet && renderer.Sheet.Geography == summon.WorldSheet, "Content/map/world/dressing binding agreement failed");

            foreach (var row in app.assets)
            {
                var source = AssetDatabase.LoadAllAssetsAtPath(row.source).OfType<ScriptableObject>().Single(v => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(v, out string _, out long id) && id == row.sourceLocalId);
                var derived = Clean<ScriptableObject>(row.derived);
                Require(!EditorUtility.IsDirty(source) && JsonHash(source) == row.sourceJsonSha256 && JsonHash(derived) == row.derivedJsonSha256, "Source/derived serialized payload differs: " + row.derived);
            }
            foreach (var row in app.references)
            {
                var component = components.Single(v => Hierarchy(v.transform) == row.hierarchy && v.GetType().FullName == row.component);
                Require(Read(component, row.property) == Clean<ScriptableObject>(row.derived) && app.assets.Any(a => a.source == row.source && a.sourceLocalId == row.sourceLocalId && a.derived == row.derived),
                    "Geometry receipt binding differs: " + row.hierarchy + ":" + row.property);
            }
            var geographyRow = app.assets.Single(row => row.derived == AssetDatabase.GetAssetPath(summon.WorldSheet));
            var dressingRow = app.assets.Single(row => row.derived == AssetDatabase.GetAssetPath(renderer.Sheet));
            var lookSheet = look.sheets.Single(row => row.derived == dressingRow.source);
            var originalSheet = Clean<WorldMacroDressingSheetSO>(lookSheet.source);
            var lookSource = look.sources.Single(row => row.path == lookSheet.source);
            Require(FileHash(lookSource.path) == lookSource.sha256, "Pre-look dressing source changed");
            Require(renderer.Sheet.SourceFingerprint == originalSheet.SourceFingerprint && renderer.Sheet.Cells.Length == originalSheet.Cells.Length && renderer.Sheet.Cells.Length == publishedCells &&
                renderer.Sheet.SourceFingerprint.StartsWith("compact-physics-v2-fine2m-check1m:", StringComparison.Ordinal), "Current dressing does not preserve its receipted fine-v2 selection provenance");
            Require(AssetDatabase.GetAssetPath(renderer.Sheet.SurfaceDeformation) == app.deltaPath && AssetDatabase.GetAssetPath(summon.WorldSheet.FinalSurface) == app.absolutePatchPath,
                "Exact delta/absolute surface binding mismatch");

            var proofs = app.sourceFiles.ToDictionary(v => Full(v.path), v => v.sha256, StringComparer.OrdinalIgnoreCase);
            string sceneText = File.ReadAllText(Full(ScenePath)); var refs = new List<Reference>();
            void CaptureField(Component component, string property)
            {
                var value = Read(component, property); Require(value != null, "Required saved reference is null: " + property);
                string path = AssetDatabase.GetAssetPath(value); Require(EditorUtility.IsPersistent(value) && !EditorUtility.IsDirty(value), "Reference is transient/dirty: " + path);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long localId);
                // Confirm exact persisted field identity, not merely membership of a
                // broad compact directory or a list of unrelated captured assets.
                var id = GlobalObjectId.GetGlobalObjectIdSlow(component);
                string header = "--- !u!114 &" + id.targetObjectId;
                int start = sceneText.IndexOf(header + "\n", StringComparison.Ordinal);
                if (start < 0) start = sceneText.IndexOf(header + "\r\n", StringComparison.Ordinal);
                Require(start >= 0, "Saved component YAML identity missing: " + Hierarchy(component.transform));
                int end = sceneText.IndexOf("\n--- !u!", start + header.Length, StringComparison.Ordinal); if (end < 0) end = sceneText.Length;
                var match = Regex.Match(sceneText.Substring(start, end - start), "(?m)^  " + Regex.Escape(property) + @": \{fileID: (-?[0-9]+), guid: ([a-fA-F0-9]+), type: [0-9]+\}\r?$");
                Require(match.Success && match.Groups[1].Value == localId.ToString(System.Globalization.CultureInfo.InvariantCulture) && match.Groups[2].Value == guid,
                    "Live reference differs from exact saved scene field: " + Hierarchy(component.transform) + ":" + property);
                if (app.assets.Any(a => a.derived == path)) { /* canonical derivative payload verified above */ }
                else Require(proofs.TryGetValue(Full(path), out string expected) && FileHash(path) == expected, "Reference lacks unchanged source-file proof: " + path);
                refs.Add(new Reference { hierarchy = Hierarchy(component.transform), component = component.GetType().FullName, property = property, asset = path, guid = guid, localId = localId });
            }
            CaptureField(session, "Content"); CaptureField(ui, "Content"); CaptureField(ui, "MapData"); CaptureField(ui, "Theme");
            foreach (var row in app.references) CaptureField(components.Single(v => Hierarchy(v.transform) == row.hierarchy && v.GetType().FullName == row.component), row.property);
            foreach (var driver in components.OfType<WorldLookDriver>())
            {
                CaptureField(driver, "_palette"); CaptureField(driver, "_inkSkyProfile"); CaptureField(driver, "_regionalSkyProfile");
                var regional = Read(driver, "_regionalSkyProfile"); var mapping = look.skies.Single(row => row.derived == AssetDatabase.GetAssetPath(regional));
                var source = look.sources.Single(row => row.path == mapping.source);
                Require(FileHash(source.path) == source.sha256, "Regional sky source differs from look receipt");
            }
            return new Snapshot { navigationGeneration = nav.generation, geometryGeneration = app.generation, navigationReceiptSha256 = FileHash(navPath),
                geometryReceiptSha256 = nav.applicationReceiptSha256, lookReceiptSha256 = app.lookReceiptSha256, sourceFingerprint = renderer.Sheet.SourceFingerprint,
                cells = renderer.Sheet.Cells.Length, geography = geographyRow.derived, dressing = dressingRow.derived, delta = app.deltaPath, absolute = app.absolutePatchPath, references = refs.ToArray() };
        }

        public static bool Matches(Snapshot snapshot, out string error)
        {
            long checkStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                Require(snapshot != null && snapshot.references != null && snapshot.references.Length > 0, "Missing pre-Play reference proof");
                Require(runtimeCacheFailure == null, runtimeCacheFailure);
                if (runtimeCache == null)
                {
                    long resolveStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    try { runtimeCache = ResolveRuntimeCache(snapshot); }
                    finally { snapshot.runtimeResolveCalls++; snapshot.runtimeResolveTotalMilliseconds += ElapsedMilliseconds(resolveStarted); }
                }
                var cache = runtimeCache;
                Require(ReferenceEquals(cache.snapshot, snapshot), "Runtime snapshot changed without a Play/reload boundary");
                foreach (var binding in cache.bindings)
                {
                    Require(binding.target != null && binding.expected != null && binding.field.GetValue(binding.target) as Object == binding.expected,
                        "Runtime reference differs from pre-Play proof: " + binding.label);
                }
                Require(cache.renderer != null, "Runtime dressing component was destroyed");
                var sheet = cache.renderer.Sheet;
                Require(sheet != null && sheet == cache.sheet && cache.geography != null && cache.delta != null && cache.absolute != null &&
                    sheet.SourceFingerprint == snapshot.sourceFingerprint && sheet.Cells.Length == snapshot.cells && sheet.Geography == cache.geography && sheet.SurfaceDeformation == cache.delta && sheet.Geography.FinalSurface == cache.absolute,
                    "Runtime dressing/geography support identity changed");
                error = ""; return true;
            }
            catch (Exception failure) { runtimeCacheFailure = failure.Message; error = runtimeCacheFailure; return false; }
            finally
            {
                // Smoke evidence only. No file writes, profiler allocation, HUD or
                // normal gameplay loop is added by this measurement.
                if (snapshot != null)
                {
                    double milliseconds = ElapsedMilliseconds(checkStarted);
                    snapshot.runtimeCheckCalls++; snapshot.runtimeCheckTotalMilliseconds += milliseconds;
                    snapshot.runtimeCheckMaximumMilliseconds = Math.Max(snapshot.runtimeCheckMaximumMilliseconds, milliseconds);
                }
            }
        }
        static double ElapsedMilliseconds(long start) => (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency;
    }
}
