using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    /// <summary>
    /// Connects authored KTP subtrees to existing spell profiles. This editor-only
    /// authoring pass never changes gameplay, life, input, cue clocks or source assets.
    /// Run the representative TraditionalBuilder first; its four outputs are reused.
    /// </summary>
    public static class Vfx120TraditionalCatalogBuilder
    {
        const string Pack = "Assets/KoreanTraditionalPattern_Effect/Prefabs/";
        const string Folder = Vfx120TraditionalBuilder.Output + "/Catalog";

        sealed class Family
        {
            public string Element, Key, PatternSource, ChargeSource, ImpactSource, FloorSource, ShieldSource;
            public string Basis;
            public GameObject Cast, ReserveCast, Impact, Floor, Shield;
        }

        [Serializable] sealed class SourceRecord
        {
            public string prefab, subtree, role;
            public int copies = 1;
            public string adaptation;
            public Vector3 carrierEuler, carrierScale;
            public float opacityMultiplier;
        }

        [Serializable] sealed class PrefabRecord
        {
            public string path, element, role, reusePolicy;
            public string visualStatus = "AUTHORED_MAPPING_ONLY_NOT_RENDER_VERIFIED";
            public int particleSystems, animators;
            public SourceRecord[] sources;
        }

        [Serializable] sealed class MappingRecord
        {
            public string glyph, title, intent, element, behavior;
            public bool assigned, gameplayConnected, replaceBody;
            public string cast, impact, field, fieldRole, impactPolicy, overridePolicy;
            public string preservedSummonPrefab;
            public float nativeScale, impactScale;
        }

        [Serializable] sealed class BuildReport
        {
            public string status = "BUILT_UNITY_RENDER_AND_GAMEPLAY_NOT_VERIFIED";
            public string generatedUtc;
            public int total, assigned, reserved, casts, impacts, fields, replacedShieldBodies;
            public string sourceSelection = "Docs/Art/SpellVFX120/KTP_REWORK_SELECTION.json";
            public string definitionManifest = "Art/SpellVFX120/build_manifest.json";
            public string limitation = "Shared native cast/contact/field motifs do not replace each spell's distinct body/motion. No art PASS from mapping; inspect actual renders. Original vendor assets unchanged.";
            public PrefabRecord[] prefabs;
            public MappingRecord[] profiles;
        }

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build the native catalog outside Play Mode.");
            var definitions = JsonUtility.FromJson<Vfx120Editor.Manifest>(
                File.ReadAllText(Path.Combine(Vfx120Editor.Output, "build_manifest.json")));
            var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(
                Vfx120Editor.AssetRoot + "/Data/VFX120_Catalog.asset");
            if (definitions == null || definitions.spells == null || definitions.spells.Length != 120
                || definitions.spells.Select(d => d.glyph).Distinct().Count() != 120
                || catalog == null || catalog.Entries == null || catalog.Entries.Length != 120)
                throw new InvalidOperationException("Expected existing 120 unique definitions and catalog profiles.");
            var entries = catalog.Entries.ToDictionary(e => e.Glyph);
            foreach (var d in definitions.spells)
                if (!entries.TryGetValue(d.glyph, out var e) || e.Profile == null
                    || e.Profile.Glyph != d.glyph || e.Profile.Assigned != d.assigned)
                    throw new InvalidOperationException("Catalog/definition mismatch: " + d.glyph);

            // Preserve the root-owned, visually tuned prototype assets and their GUIDs.
            var woodCast = Require(Vfx120TraditionalBuilder.Output + "/KTP_Cast_LeafBloom.prefab");
            var woodImpact = Require(Vfx120TraditionalBuilder.Output + "/KTP_Impact_LeafBurst.prefab");
            var woodFloor = Require(Vfx120TraditionalBuilder.Output + "/KTP_Summon_LotusCourt.prefab");
            var woodShield = Require(Vfx120TraditionalBuilder.Output + "/KTP_Shield_JadeCanopy.prefab");
            EnsureFolder(Folder);
            var families = Families();
            var prefabRows = new List<PrefabRecord>();
            foreach (var f in families)
            {
                Preflight(f);
                if (f.Element == "목")
                {
                    f.Cast = woodCast; f.Impact = woodImpact; f.Floor = woodFloor; f.Shield = woodShield;
                    RecordReused(prefabRows, f, f.Cast, "Cast", new[] {
                        Source(f.PatternSource, "Pattern", "primary", new Vector3(90, 0, 0), .28f, 1),
                        Source(f.ChargeSource, "Charge", "support", new Vector3(0, 90, 0), 1, .30f) });
                    RecordReused(prefabRows, f, f.Impact, "Impact", new[] { Source(f.ImpactSource, "Explosion", "contact", new Vector3(0, 90, 0), 1, 1) });
                    RecordReused(prefabRows, f, f.Floor, "Summon", new[] { Source(f.FloorSource, "", "field", Vector3.zero, 1, 1) });
                    RecordReused(prefabRows, f, f.Shield, "Shield", new[] { Source(f.ShieldSource, "", "field", Vector3.zero, 1, 1) });
                }
                else
                {
                    f.Cast = BuildCast(f, false, prefabRows);
                    f.Impact = BuildImpact(f, prefabRows);
                    f.Floor = BuildField(f, false, prefabRows);
                    f.Shield = BuildField(f, true, prefabRows);
                }
                // Reserved glyphs have only a quiet review emblem: no charge, hit or field.
                f.ReserveCast = BuildCast(f, true, prefabRows);
            }

            var rows = new List<MappingRecord>();
            foreach (var d in definitions.spells)
            {
                var entry = entries[d.glyph];
                var p = entry.Profile;
                var f = families.First(x => x.Element == Element(d.glyph));
                bool reserved = !d.assigned || p.Behavior == Vfx120Behavior.Reserve;
                bool representative = p.Glyph == "가" || p.Glyph == "곰" || p.Glyph == "구";
                bool shield = !reserved && p.Behavior == Vfx120Behavior.Shield && p.Glyph != "막";
                bool summon = !reserved && p.Behavior == Vfx120Behavior.Summon;
                bool impact = !reserved && NeedsImpact(d, p);

                // Existing representative Native slots win. No reference to SummonPrefab,
                // SummonScale/Yaw, BodyMesh/Material or the creature's pose is assigned here.
                if (!representative || p.NativeCastPrefab == null)
                    p.NativeCastPrefab = reserved ? f.ReserveCast : f.Cast;
                if (impact)
                {
                    if (!representative || p.NativeImpactPrefab == null) p.NativeImpactPrefab = f.Impact;
                }
                else p.NativeImpactPrefab = null;
                if (shield || summon)
                {
                    if (!representative || p.NativeFieldPrefab == null)
                        p.NativeFieldPrefab = shield ? f.Shield : f.Floor;
                    p.NativeFieldRole = shield ? Vfx120TraditionalMotif.Role.Shield : Vfx120TraditionalMotif.Role.Summon;
                    p.NativeReplaceBody = shield;
                }
                else
                {
                    p.NativeFieldPrefab = null;
                    p.NativeReplaceBody = false;
                }
                float scale = reserved ? .40f : summon ? .65f : shield ? .52f : .48f;
                if (p.Glyph == "노" || p.Glyph == "모") scale = .30f;
                p.NativeScale = Mathf.Clamp(scale, .28f, .65f);
                p.NativeImpactScale = p.Glyph == "노" || p.Glyph == "모" ? .42f : 1f;
                if (p.BotanicalPrefab != null)
                { p.NativeScale = .4f; p.NativeImpactScale = .65f; }
                if (p.Glyph == "오") { p.NativeScale = .30f; p.NativeImpactScale = .36f; }
                if (p.Glyph == "막" || p.Glyph == "뭄") { p.NativeScale = .32f; p.NativeImpactScale = .45f; }
                EditorUtility.SetDirty(p);
                rows.Add(new MappingRecord {
                    glyph = d.glyph, title = d.title, intent = d.intent, element = f.Element,
                    behavior = p.Behavior.ToString(), assigned = p.Assigned, gameplayConnected = entry.GameplayConnected,
                    cast = PathOf(p.NativeCastPrefab), impact = PathOf(p.NativeImpactPrefab), field = PathOf(p.NativeFieldPrefab),
                    fieldRole = p.NativeFieldPrefab == null ? "None" : p.NativeFieldRole.ToString(),
                    replaceBody = p.NativeReplaceBody, nativeScale = p.NativeScale, impactScale = p.NativeImpactScale,
                    impactPolicy = impact ? "EXISTING_HIT_OR_RECEIVED_IMPACT_CLOCK_ONLY; demonstration requires both preview flags"
                        : reserved ? "RESERVED_NO_GAMEPLAY_IMPACT" : "NO_CONTACT_BURST_FOR_THIS_INTENT",
                    overridePolicy = representative ? "PRESERVE_EXISTING_REPRESENTATIVE_NATIVE_REFERENCES; fixed .48/.65/.52 scale"
                        : "ASSIGN_NATIVE_PHASES_ONLY; preserve body, motion, lifecycle, gameplay and Meshy slots",
                    preservedSummonPrefab = PathOf(p.SummonPrefab)
                });
            }
            AssetDatabase.SaveAssets();
            var report = new BuildReport {
                generatedUtc = DateTime.UtcNow.ToString("o"), total = rows.Count,
                assigned = rows.Count(r => r.assigned), reserved = rows.Count(r => !r.assigned),
                casts = rows.Count(r => !string.IsNullOrEmpty(r.cast)), impacts = rows.Count(r => !string.IsNullOrEmpty(r.impact)),
                fields = rows.Count(r => !string.IsNullOrEmpty(r.field)), replacedShieldBodies = rows.Count(r => r.replaceBody),
                prefabs = prefabRows.ToArray(), profiles = rows.ToArray()
            };
            File.WriteAllText(Path.Combine(Vfx120Editor.Output, "ktp_catalog_mapping.json"), JsonUtility.ToJson(report, true));
            return "BUILT_NATIVE_CATALOG_" + report.total + "_CASTS_" + report.casts + "_IMPACTS_" + report.impacts
                + "_FIELDS_" + report.fields + "_RESERVED_" + report.reserved + "_VISUAL_UNVERIFIED";
        }

        static Family[] Families() => new[] {
            new Family { Element = "목", Key = "Wood", PatternSource = "Bottom/Bottom03-01.prefab", ChargeSource = "Fly/Fly06-01.prefab",
                ImpactSource = "Fly/Fly01-01.prefab", FloorSource = "Bottom/Bottom12-01.prefab", ShieldSource = "Bottom/Bottom04-01.prefab",
                Basis = "꽃판 발동, 굵은 회전 잎 타격; 기존 목 시제품 4개 재사용" },
            new Family { Element = "화", Key = "Fire", PatternSource = "Bottom/Bottom05-01.prefab", ChargeSource = "Fly/Fly05-01.prefab",
                ImpactSource = "Fly/Fly03-01.prefab", FloorSource = "Bottom/Bottom05-01.prefab", ShieldSource = "Bottom/Bottom09-01.prefab",
                Basis = "곡선 화문과 native Fire_01, 팽창 충격면, 겹친 화막" },
            new Family { Element = "토", Key = "Earth", PatternSource = "Bottom/Bottom12-01.prefab", ChargeSource = "",
                ImpactSource = "Fly/Fly10-01.prefab", FloorSource = "Bottom/Bottom12-01.prefab", ShieldSource = "Bottom/Bottom14-01.prefab",
                Basis = "방사 꽃판 기반 지반, 엮임 문양과 이중 충격파, 다중 패널 토벽" },
            new Family { Element = "금", Key = "Metal", PatternSource = "Bottom/Bottom09-01.prefab", ChargeSource = "Fly/Fly08-01.prefab",
                ImpactSource = "Fly/Fly07-01.prefab", FloorSource = "Bottom/Bottom18-01.prefab", ShieldSource = "Bottom/Bottom14-01.prefab",
                Basis = "직각 격자 Pattern125와 파편 Pattern162; 삼태극 Pattern225를 범용 기호로 쓰지 않음" },
            new Family { Element = "수", Key = "Water", PatternSource = "Bottom/Bottom15-01.prefab", ChargeSource = "",
                ImpactSource = "Fly/Fly10-01.prefab", FloorSource = "Bottom/Bottom15-01.prefab", ShieldSource = "Bottom/Bottom15-01.prefab",
                Basis = "두 물고기 Pattern188, 두겹의 원래 ShockWave와 유연한 외곽 광면" }
        };

        static bool NeedsImpact(Vfx120Editor.Definition d, Vfx120Profile p)
        {
            // Environmental completion, passive auras, heal/buff/weapon entry and
            // protective fields are not automatically enemy-hit explosions.
            if (p.Glyph == "녹" || p.Glyph == "막" || p.Glyph == "곳") return true;
            if (!string.IsNullOrEmpty(d.intent) && d.intent.Contains("비전투")) return false;
            switch (p.Behavior)
            {
                case Vfx120Behavior.Projectile: case Vfx120Behavior.Bind:
                case Vfx120Behavior.Burst: case Vfx120Behavior.Wave: return true;
                default: return false;
            }
        }

        static string Element(string glyph)
        {
            if (string.IsNullOrEmpty(glyph) || glyph.Length != 1) throw new ArgumentException("Expected one Hangul syllable.");
            int initial = (glyph[0] - 0xAC00) / 588;
            switch (initial) { case 0: return "목"; case 2: return "화"; case 6: return "토"; case 9: return "금"; case 11: return "수"; }
            throw new ArgumentException("Unsupported element: " + glyph);
        }

        static GameObject BuildCast(Family f, bool reserve, List<PrefabRecord> records)
        {
            var root = new GameObject("KTP_" + (reserve ? "ReserveCast_" : "Cast_") + f.Key);
            var sources = new List<SourceRecord>();
            try
            {
                var emblem = Add(root, f.PatternSource, "Pattern", new Vector3(90, 0, 0), .28f);
                emblem.name = "Primary_AuthoredPattern";
                sources.Add(Source(f.PatternSource, "Pattern", "primary", new Vector3(90, 0, 0), .28f, 1));
                if (!reserve && !string.IsNullOrEmpty(f.ChargeSource))
                {
                    var charge = Add(root, f.ChargeSource, "Charge", new Vector3(0, 90, 0), 1);
                    charge.name = "Supporting_Charge"; Opacity(charge, .30f);
                    sources.Add(Source(f.ChargeSource, "Charge", "support", new Vector3(0, 90, 0), 1, .30f));
                }
                if (!reserve && f.Element == "화")
                {
                    var flame = Add(root, f.FloorSource, "Fire_01", new Vector3(90, 0, 0), .28f);
                    flame.name = "Supporting_AuthoredFire"; Opacity(flame, .30f);
                    sources.Add(Source(f.FloorSource, "Fire_01", "fire support", new Vector3(90, 0, 0), .28f, .30f));
                }
                return Save(root, f, reserve ? "ReserveCast" : "Cast", sources, records);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static GameObject BuildImpact(Family f, List<PrefabRecord> records)
        {
            var root = new GameObject("KTP_Impact_" + f.Key);
            try
            {
                var contact = Add(root, f.ImpactSource, "Explosion", new Vector3(0, 90, 0), 1);
                ReduceFill(contact, .28f);
                if (f.Element == "금") DisableDigitalFill(contact);
                // Fly10 Light_00 uses an unmasked built-in white texture: its two
                // ExplosionLight billboards leave rectangular fills on the ground.
                if (f.Element == "토" || f.Element == "수")
                    foreach (var t in contact.GetComponentsInChildren<Transform>(true))
                        if (t.name == "ExplosionLight_00" || t.name == "ExplosionLight_01") t.gameObject.SetActive(false);
                return Save(root, f, "Impact", new List<SourceRecord> {
                    Source(f.ImpactSource, "Explosion", "contact; named fill alpha x.28", new Vector3(0, 90, 0), 1, 1)
                }, records);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static GameObject BuildField(Family f, bool shield, List<PrefabRecord> records)
        {
            var root = new GameObject("KTP_" + (shield ? "Shield_" : "Summon_") + f.Key);
            string sourcePath = shield ? f.ShieldSource : f.FloorSource;
            var sources = new List<SourceRecord>();
            try
            {
                var field = Add(root, sourcePath, "", Vector3.zero, 1);
                ReduceFill(field, shield ? .42f : .22f);
                // This known source has two broad square fills that hid its flower plate.
                if (sourcePath == "Bottom/Bottom12-01.prefab")
                    foreach (var t in field.GetComponentsInChildren<Transform>(true))
                        if (t.name == "Light_00" || t.name == "Light_02") t.gameObject.SetActive(false);
                sources.Add(Source(sourcePath, "", "field; named fill attenuated in copy", Vector3.zero, 1, 1));
                if (f.Element == "금")
                {
                    DisableDigitalFill(field);
                    // Retain Bottom18 Par_Tech or Bottom14 native panels, but avoid
                    // manufacturing a generic metal meaning for the complete trigram emblem.
                    foreach (var t in field.GetComponentsInChildren<Transform>(true))
                        if (t.name == "Pattern") t.gameObject.SetActive(false);
                    Add(root, f.PatternSource, "Pattern", Vector3.zero, 1).name = "Primary_AngularLattice";
                    sources.Add(Source(f.PatternSource, "Pattern", "replacement authored lattice system", Vector3.zero, 1, 1));
                }
                if (shield && f.Element == "화")
                {
                    var fire = Add(root, f.FloorSource, "Fire_01", Vector3.zero, .65f);
                    Opacity(fire, .30f);
                    sources.Add(Source(f.FloorSource, "Fire_01", "fire membrane support", Vector3.zero, .65f, .30f));
                }
                if (shield)
                {
                    string panelSource = f.Element == "금" ? f.PatternSource : sourcePath;
                    Vfx120TraditionalBuilder.AddShieldPanels(root, Pack + panelSource);
                    var panels = Source(panelSource, "Pattern", "upright shield boundaries", Vector3.zero, 1, 1);
                    panels.copies = 4;
                    panels.adaptation = "Root-owned AddShieldPanels: four separated upright curved arcs, 256 tris each; original Pattern PS lifetime/alpha/custom data/material; carrier start size=1, speed=0, rotation=0, shape/rotation-over-lifetime disabled.";
                    sources.Add(panels);
                }
                return Save(root, f, shield ? "Shield" : "Summon", sources, records);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static void Preflight(Family f)
        {
            FindSource(f.PatternSource, "Pattern");
            if (!string.IsNullOrEmpty(f.ChargeSource)) FindSource(f.ChargeSource, "Charge");
            FindSource(f.ImpactSource, "Explosion"); FindSource(f.FloorSource, ""); FindSource(f.ShieldSource, "");
            if (f.Element == "화") FindSource(f.FloorSource, "Fire_01");
        }

        static GameObject FindSource(string path, string child)
        {
            var source = Require(Pack + path);
            if (path.StartsWith("Fly/", StringComparison.Ordinal) && child != "Charge" && child != "Explosion")
                throw new InvalidOperationException("Whole Fly demo roots cannot be used by the native catalog.");
            if (string.IsNullOrEmpty(child)) return source;
            var selected = source.GetComponentsInChildren<Transform>(true)
                .Where(t => string.Equals(t.name, child, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (selected.Length != 1) throw new InvalidOperationException("Expected one authored subtree " + path + "/" + child);
            return selected[0].gameObject;
        }

        static GameObject Add(GameObject root, string path, string child, Vector3 euler, float scale)
        {
            var source = FindSource(path, child);
            if (source.GetComponentsInChildren<Animator>(true).Length != 0
                || source.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                throw new InvalidOperationException("Selected native subtree must have no demo controller/scripts: " + path);
            var copy = UnityEngine.Object.Instantiate(source, root.transform, false);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.Euler(euler) * copy.transform.localRotation;
            copy.transform.localScale *= scale;
            copy.SetActive(true);
            foreach (var ps in copy.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.playOnAwake = false; main.stopAction = ParticleSystemStopAction.None;
            }
            return copy;
        }

        static void ReduceFill(GameObject root, float factor)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                string name = ps.name;
                if (name.IndexOf("Pattern", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                // Apply once per actual PS, never recursively multiply child alpha twice.
                if (name == "Light" || name.StartsWith("Light_", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Aura", StringComparison.OrdinalIgnoreCase)
                    || name.IndexOf("Flare", StringComparison.OrdinalIgnoreCase) >= 0)
                    Opacity(ps, factor);
            }
        }

        static void DisableDigitalFill(GameObject root)
        {
            // Actual C2 capture03 showed solid white digital tiles masking the paws.
            // These source demo layers are omitted from the curated metal variant;
            // the lattice Pattern and remaining native timing/curves are retained.
            foreach (var part in root.GetComponentsInChildren<Transform>(true))
                if (part.name.IndexOf("Tech", StringComparison.OrdinalIgnoreCase) >= 0
                    || part.name == "Light" || part.name.StartsWith("Light_", StringComparison.OrdinalIgnoreCase))
                    part.gameObject.SetActive(false);
        }

        static void Opacity(GameObject root, float factor)
        { foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) Opacity(ps, factor); }

        static void Opacity(ParticleSystem ps, float factor)
        {
            var main = ps.main; var value = main.startColor;
            var mode = value.mode;
            if (mode == ParticleSystemGradientMode.Color) value.color = Alpha(value.color, factor);
            else if (mode == ParticleSystemGradientMode.TwoColors)
            { value.colorMin = Alpha(value.colorMin, factor); value.colorMax = Alpha(value.colorMax, factor); }
            else if (mode == ParticleSystemGradientMode.TwoGradients)
            { value.gradientMin = Alpha(value.gradientMin, factor); value.gradientMax = Alpha(value.gradientMax, factor); }
            else value.gradient = Alpha(value.gradient, factor);
            value.mode = mode; main.startColor = value;
        }

        static Color Alpha(Color c, float factor) { c.a *= factor; return c; }
        static Gradient Alpha(Gradient original, float factor)
        {
            if (original == null) return null;
            var gradient = new Gradient { mode = original.mode };
            var keys = original.alphaKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].alpha *= factor;
            gradient.SetKeys(original.colorKeys, keys);
            return gradient;
        }

        static GameObject Save(GameObject root, Family f, string role, List<SourceRecord> sources, List<PrefabRecord> records)
        {
            int systems = root.GetComponentsInChildren<ParticleSystem>(true).Length;
            // Existing runtime budget stays unchanged. Reject an oversized authored group
            // instead of silently deleting layers or raising its performance limits.
            if (systems < 1 || systems > Vfx120TraditionalMotif.Settings.DefaultFor(Vfx120TraditionalMotif.Role.Cast).MaxSystems)
                throw new InvalidOperationException("Native system budget exceeded: " + root.name + " = " + systems);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/" + root.name + ".prefab");
            if (prefab == null) throw new InvalidOperationException("Could not save " + root.name);
            records.Add(new PrefabRecord { path = PathOf(prefab), element = f.Element, role = role,
                reusePolicy = "Independent authored PS subtrees; explicit fill/carrier/shield-panel adaptations listed in sources. " + f.Basis,
                particleSystems = systems, animators = 0, sources = sources.ToArray() });
            return prefab;
        }

        static void RecordReused(List<PrefabRecord> records, Family f, GameObject prefab, string role, SourceRecord[] sources)
        { records.Add(new PrefabRecord { path = PathOf(prefab), element = f.Element, role = role,
            reusePolicy = "Existing root-owned prototype reused unchanged; see ktp_rework_build.txt for current tuning.",
            particleSystems = prefab.GetComponentsInChildren<ParticleSystem>(true).Length,
            animators = prefab.GetComponentsInChildren<Animator>(true).Length, sources = sources }); }

        static SourceRecord Source(string path, string child, string role, Vector3 euler, float scale, float opacity)
            => new SourceRecord { prefab = Pack + path, subtree = child, role = role,
                carrierEuler = euler, carrierScale = Vector3.one * scale, opacityMultiplier = opacity };
        static string PathOf(UnityEngine.Object obj) => obj == null ? "" : AssetDatabase.GetAssetPath(obj);
        static GameObject Require(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path)
            ?? throw new FileNotFoundException("Required KTP asset missing. Build the representative prototypes first: " + path);
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
