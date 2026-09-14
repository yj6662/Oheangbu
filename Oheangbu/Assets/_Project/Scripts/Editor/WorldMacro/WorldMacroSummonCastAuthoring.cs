using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>
    /// Installs the five presentation-only summon casts into the World Macro playtest copy.
    /// It never changes the prototype spell book or creates summon movement/combat behavior.
    /// </summary>
    public static class WorldMacroSummonCastAuthoring
    {
        public const string SourceBookPath = "Assets/_Project/Data/Configs/SpellBook_Proto.asset";
        public const string TestBookPath = "Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset";
        public const string VisualSetPath = "Assets/_Project/Art/SpellVFX120/Data/SpellVisualSet_120.asset";

        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly struct SummonRow
        {
            public readonly char Letter;
            public readonly Element Element;
            public readonly string PrefabName;
            public readonly string PresentationField;
            public readonly string ModelField;
            public readonly string SealField;
            public readonly string DebrisField;

            public SummonRow(char letter, Element element, string prefabName, string presentationField,
                string modelField, string sealField, string debrisField)
            {
                Letter = letter;
                Element = element;
                PrefabName = prefabName;
                PresentationField = presentationField;
                ModelField = modelField;
                SealField = sealField;
                DebrisField = debrisField;
            }
        }

        private static readonly SummonRow[] Rows =
        {
            new SummonRow('곰', Element.Wood,  "016_ACF0", nameof(Vfx120Profile.WoodDeerPresentation),
                nameof(Vfx120Profile.WoodDeerPrefab), nameof(Vfx120Profile.WoodDeerSeal), nameof(Vfx120Profile.WoodDeerDebris)),
            new SummonRow('놈', Element.Fire,  "040_B188", nameof(Vfx120Profile.FireHaetaePresentation),
                nameof(Vfx120Profile.FireHaetaePrefab), nameof(Vfx120Profile.FireHaetaeSeal), nameof(Vfx120Profile.FireHaetaeDebris)),
            new SummonRow('몸', Element.Earth, "064_BAB8", nameof(Vfx120Profile.DokkaebiClubPresentation),
                nameof(Vfx120Profile.DokkaebiClubPrefab), nameof(Vfx120Profile.DokkaebiClubSeal), nameof(Vfx120Profile.DokkaebiClubDebris)),
            new SummonRow('솜', Element.Metal, "088_C19C", nameof(Vfx120Profile.MetalTigerPresentation),
                nameof(Vfx120Profile.MetalTigerPrefab), nameof(Vfx120Profile.MetalTigerSeal), nameof(Vfx120Profile.MetalTigerDebris)),
            new SummonRow('옴', Element.Water, "112_C634", nameof(Vfx120Profile.WaterTurtlePresentation),
                nameof(Vfx120Profile.WaterTurtlePrefab), nameof(Vfx120Profile.WaterTurtleSeal), nameof(Vfx120Profile.WaterTurtleDebris)),
        };

        public static string Execute(string argument)
        {
            string action = (argument ?? string.Empty).Trim().ToLowerInvariant();
            if (action == "runtime-begin") return WorldMacroSummonCastRuntimeReview.Begin();
            if (action == "runtime-poll") return WorldMacroSummonCastRuntimeReview.Poll();
            RequireScene();
            switch (action)
            {
                case "inspect": return Inspect();
                case "install": return Install();
                case "validate": return Validate();
                case "test": return RunFocusedTests();
                default: throw new ArgumentException("Expected inspect, install, validate, test, runtime-begin, or runtime-poll.");
            }
        }

        public static string Inspect()
        {
            var source = Need<SpellBookSO>(SourceBookPath);
            var visuals = Need<SpellVisualSetSO>(VisualSetPath);
            var lines = new List<string>
            {
                "scene=" + WorldMacroPlaytestAuthoring.ScenePath,
                "sourceBook=" + SourceBookPath + " sha256=" + Hash(SourceBookPath),
                "testBook=" + (AssetDatabase.LoadAssetAtPath<SpellBookSO>(TestBookPath) != null ? TestBookPath : "NOT_INSTALLED"),
            };
            foreach (var row in Rows)
            {
                bool sourceMapped = source.TryGet(row.Letter, out _);
                bool visualMapped = visuals.TryGet(row.Letter, out var visual);
                lines.Add(row.Letter + " kind=Summon sourceMapped=" + sourceMapped + " visual="
                    + (visualMapped && visual.FxPrefab != null ? visual.FxPrefab.name : "MISSING"));
            }
            return string.Join("\n", lines);
        }

        public static string Install()
        {
            string before = Hash(SourceBookPath);
            var source = Need<SpellBookSO>(SourceBookPath);
            var book = AssetDatabase.LoadAssetAtPath<SpellBookSO>(TestBookPath);
            if (book == null)
            {
                book = Object.Instantiate(source);
                book.name = "SpellBook_WorldMacroSummon_TEST";
                AssetDatabase.CreateAsset(book, TestBookPath);
            }
            else
            {
                EditorUtility.CopySerialized(source, book);
                book.name = "SpellBook_WorldMacroSummon_TEST";
            }

            WriteSummonEntries(book);
            foreach (var scope in Object.FindObjectsByType<CombatLifetimeScope>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Assign(scope, "_spellBook", book);
            foreach (var adapter in Object.FindObjectsByType<BrushStrokeFeedAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Assign(adapter, "_spellBook", book);

            EditorUtility.SetDirty(book);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            string after = Hash(SourceBookPath);
            if (!string.Equals(before, after, StringComparison.Ordinal))
                throw new InvalidOperationException("Source SpellBook_Proto changed while installing the TEST copy.");
            return "Installed independent World Macro TEST spell book with five Summon entries. " + Validate();
        }

        public static string Validate()
        {
            var source = Need<SpellBookSO>(SourceBookPath);
            var book = Need<SpellBookSO>(TestBookPath);
            var visuals = Need<SpellVisualSetSO>(VisualSetPath);
            NeedCondition(CountKind(source, SpellKind.Summon) == 0, "SpellBook_Proto must not contain Summon entries.");
            NeedCondition(CountKind(book, SpellKind.Summon) == Rows.Length, "TEST book must contain exactly five Summon entries.");

            foreach (var row in Rows)
            {
                NeedCondition(book.TryGet(row.Letter, out var entry), "TEST book is missing " + row.Letter + ".");
                NeedCondition(entry.Kind == SpellKind.Summon && entry.Element == row.Element,
                    row.Letter + " must be classified as its elemental Summon.");
                NeedCondition(entry.BasePower == 0 && entry.AreaShape == AreaShape.None,
                    row.Letter + " must not carry attack power or area geometry.");
                NeedCondition(visuals.TryGet(row.Letter, out var visual) && visual.FxPrefab != null,
                    "SpellVisualSet_120 is missing " + row.Letter + ".");
                NeedCondition(visual.FxPrefab.name == row.PrefabName,
                    row.Letter + " points to " + visual.FxPrefab.name + " instead of " + row.PrefabName + ".");
                var effect = visual.FxPrefab.GetComponentInChildren<Vfx120Effect>(true);
                NeedCondition(effect != null && effect.Profile != null, row.PrefabName + " has no Vfx120Effect profile.");
                NeedCondition(effect.Profile.Glyph == row.Letter.ToString() && effect.Profile.Behavior == Vfx120Behavior.Summon,
                    row.PrefabName + " is not a summon presentation profile.");
                var presentation = typeof(Vfx120Profile).GetField(row.PresentationField);
                NeedCondition(presentation != null && (bool)presentation.GetValue(effect.Profile),
                    row.PrefabName + " does not select the approved static presentation.");
                NeedPresentationOnly(visual.FxPrefab, row.PrefabName);
                NeedPresentationOnly(ReadProfilePrefab(effect.Profile, row.ModelField), row.Letter + " static model");
                NeedPresentationOnly(ReadProfilePrefab(effect.Profile, row.SealField), row.Letter + " seal");
                NeedPresentationOnly(ReadProfilePrefab(effect.Profile, row.DebrisField), row.Letter + " debris");
            }

            var scopes = Object.FindObjectsByType<CombatLifetimeScope>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var adapters = Object.FindObjectsByType<BrushStrokeFeedAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            NeedCondition(scopes.Length > 0 && adapters.Length > 0, "Macro scene combat spell-book consumers are missing.");
            foreach (var scope in scopes) NeedCondition(ReferenceEquals(Read<SpellBookSO>(scope, "_spellBook"), book), "CombatLifetimeScope is not wired to the TEST book.");
            foreach (var adapter in adapters) NeedCondition(ReferenceEquals(Read<SpellBookSO>(adapter, "_spellBook"), book), "Brush adapter is not wired to the TEST book.");
            return "VALID: five mapped static summon presentations; TEST book isolated; scene consumers agree; no summon combat, physics or animation components.";
        }

        public static string RunFocusedTests()
        {
            Validate();
            var book = Need<SpellBookSO>(TestBookPath);
            var sceneAdapter = Object.FindFirstObjectByType<BrushStrokeFeedAdapter>(FindObjectsInactive.Include);
            NeedCondition(sceneAdapter != null, "Scene brush adapter missing.");
            var style = Read<BrushStyleSO>(sceneAdapter, "_style");
            NeedCondition(style != null, "Scene brush style missing.");

            GameObject host = null;
            CombatConfigSO config = null;
            try
            {
                host = new GameObject("SummonCastFocusedTests");
                host.SetActive(false);
                var player = new GameObject("PlayerPose");
                player.transform.SetParent(host.transform, false);
                player.transform.position = new Vector3(12, 3, -8);
                player.transform.rotation = Quaternion.Euler(0, 37, 0);
                var adapter = host.AddComponent<BrushStrokeFeedAdapter>();
                var wiring = host.AddComponent<CombatLoopWiring>();
                Assign(adapter, "_style", style);
                Assign(adapter, "_spellBook", book);
                Assign(wiring, "_brushAdapter", adapter);
                Assign(wiring, "_playerTransform", player.transform);
                config = ScriptableObject.CreateInstance<CombatConfigSO>();
                Assign(wiring, "_config", config);
                var ink = new InkPool(config, null);
                wiring.Construct(new SpellResolver(book), null, null, ink);
                host.SetActive(true);

                int accepted = 0, plans = 0;
                Vector3 acceptedOrigin = default, acceptedForward = default;
                wiring.SummonAccepted += (_, origin, forward) => { accepted++; acceptedOrigin = origin; acceptedForward = forward; };
                wiring.CastPlanned += _ => plans++;

                var drawn = new DrawnLetter('곰', default, default, default, .6f, .6f, 1f, 1f, 3);
                Call(adapter, "ApplyLetterDrawn", drawn);
                float inkBefore = ink.Value;
                Call(wiring, "OnLetterDrawn", drawn);
                Call(adapter, "LateUpdate");
                NeedCondition(accepted == 1, "Mapped summon was not accepted exactly once.");
                NeedCondition(Mathf.Approximately(ink.Value, inkBefore - config.SpellInkCost), "Accepted summon did not spend exactly one standard spell cost.");
                NeedCondition(plans == 0 && ((IList)ReadObject(wiring, "_pendingCasts")).Count == 0, "Summon created an attack plan or pending damage.");
                NeedCondition(Read<bool>(adapter, "_pendingSummon") && Read<bool>(adapter, "_hasPendingSummonPose"), "Accepted summon pose was not available in the same frame.");
                NeedCondition(acceptedOrigin == player.transform.position && Vector3.Dot(acceptedForward, player.transform.forward) > .999f,
                    "Accepted summon did not resolve from the player pose.");

                Call(adapter, "ApplyCommitted", false, Time.time);
                ink.Restore(0);
                Call(adapter, "ApplyLetterDrawn", drawn);
                Call(wiring, "OnLetterDrawn", drawn);
                Call(adapter, "LateUpdate");
                NeedCondition(accepted == 1 && ink.Value == 0, "Rejected summon changed acceptance count or ink.");
                NeedCondition(Read<bool>(adapter, "_pendingCastFailed"), "Rejected summon did not reach the visual failure gate in the same frame.");

                var firstByLetter = new Dictionary<char, GameObject>();
                foreach (var row in Rows)
                {
                    var instance = new GameObject("Tracked_" + row.Letter);
                    instance.transform.SetParent(host.transform, false);
                    firstByLetter[row.Letter] = instance;
                    Call(adapter, "TrackSummon", row.Letter, instance);
                }
                NeedCondition(adapter.ActiveSummonPresentationCount == Rows.Length, "Five different summon presentations were not bounded at five.");
                var replacement = new GameObject("Tracked_곰_Retrigger");
                replacement.transform.SetParent(host.transform, false);
                Call(adapter, "TrackSummon", '곰', replacement);
                NeedCondition(adapter.ActiveSummonPresentationCount == Rows.Length
                    && (firstByLetter['곰'] == null || !firstByLetter['곰'].activeSelf),
                    "Retrigger did not replace the same glyph while preserving the five-presentation bound.");
                Call(adapter, "ClearActiveSummons");
                NeedCondition(adapter.ActiveSummonPresentationCount == 0, "Summon cleanup left tracked presentations alive.");

                return "PASS: accepted/rejected real cast gate, single ink spend, five summon classifications, same-frame pose, bounded retrigger, cleanup.";
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
                if (config != null) Object.DestroyImmediate(config);
            }
        }

        private static void WriteSummonEntries(SpellBookSO book)
        {
            var serialized = new SerializedObject(book);
            var entries = serialized.FindProperty("_entries");
            for (int i = entries.arraySize - 1; i >= 0; i--)
            {
                string letter = entries.GetArrayElementAtIndex(i).FindPropertyRelative("Letter").stringValue;
                if (!string.IsNullOrEmpty(letter) && Array.Exists(Rows, row => row.Letter == letter[0]))
                    entries.DeleteArrayElementAtIndex(i);
            }
            foreach (var row in Rows)
            {
                int index = entries.arraySize;
                entries.InsertArrayElementAtIndex(index);
                var entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("Letter").stringValue = row.Letter.ToString();
                entry.FindPropertyRelative("Kind").enumValueIndex = (int)SpellKind.Summon;
                entry.FindPropertyRelative("Element").enumValueIndex = (int)row.Element;
                entry.FindPropertyRelative("BasePower").floatValue = 0;
                entry.FindPropertyRelative("AreaShape").enumValueIndex = (int)AreaShape.None;
                entry.FindPropertyRelative("ProjectileSpeedMul").floatValue = 0;
                entry.FindPropertyRelative("AreaAngle").floatValue = 0;
                entry.FindPropertyRelative("AreaRadius").floatValue = 0;
                entry.FindPropertyRelative("AreaLength").floatValue = 0;
                entry.FindPropertyRelative("AreaSpeed").floatValue = 0;
                entry.FindPropertyRelative("AreaImpactDelay").floatValue = 0;
                entry.FindPropertyRelative("VolleyShots").intValue = 0;
                entry.FindPropertyRelative("VolleyInterval").floatValue = 0;
                entry.FindPropertyRelative("ScatterVolley").boolValue = false;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static int CountKind(SpellBookSO book, SpellKind kind)
        {
            var serialized = new SerializedObject(book);
            var entries = serialized.FindProperty("_entries");
            int count = 0;
            for (int i = 0; i < entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("Kind").enumValueIndex == (int)kind) count++;
            return count;
        }

        private static GameObject ReadProfilePrefab(Vfx120Profile profile, string fieldName)
        {
            var field = typeof(Vfx120Profile).GetField(fieldName);
            var prefab = field != null ? field.GetValue(profile) as GameObject : null;
            NeedCondition(prefab != null, profile.Glyph + " is missing presentation dependency " + fieldName + ".");
            return prefab;
        }

        private static void NeedPresentationOnly(GameObject prefab, string label)
        {
            NeedCondition(prefab.GetComponentsInChildren<EnemyController>(true).Length == 0
                && prefab.GetComponentsInChildren<EnemyVitals>(true).Length == 0,
                label + " must not contain combat AI or vitals.");
            NeedCondition(prefab.GetComponentsInChildren<Collider>(true).Length == 0
                && prefab.GetComponentsInChildren<Rigidbody>(true).Length == 0,
                label + " must not contain gameplay collision or physics bodies.");
            NeedCondition(prefab.GetComponentsInChildren<Animator>(true).Length == 0
                && prefab.GetComponentsInChildren<Animation>(true).Length == 0,
                label + " must remain static and contain no animation component.");
        }

        private static void RequireScene()
        {
            if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Open W_WorldMacro_Playtest in Edit mode. SummonCast only edits its TEST spell book and that scene's two consumers.");
        }

        private static T Need<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException("Missing required asset: " + path);
            return asset;
        }

        private static string Hash(string assetPath)
        {
            string full = Path.GetFullPath(assetPath);
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(full))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void NeedCondition(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Assign(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null) throw new MissingFieldException(target.GetType().Name, field);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void Assign(object target, string field, object value)
        {
            var info = target.GetType().GetField(field, Hidden);
            if (info == null) throw new MissingFieldException(target.GetType().Name, field);
            info.SetValue(target, value);
        }

        private static T Read<T>(object target, string field)
        {
            return (T)ReadObject(target, field);
        }

        private static object ReadObject(object target, string field)
        {
            var info = target.GetType().GetField(field, Hidden);
            if (info == null) throw new MissingFieldException(target.GetType().Name, field);
            return info.GetValue(target);
        }

        private static object Call(object target, string method, params object[] arguments)
        {
            var info = target.GetType().GetMethod(method, Hidden);
            if (info == null) throw new MissingMethodException(target.GetType().Name, method);
            return info.Invoke(target, arguments);
        }
    }
}
