using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>D308-150 V9 step 5a (TEST): builds the vol.2-owned variants of the old lasting parts (Vol2LegacyLastingSet) - COPIES of the prefabs the bridge stage borrows,
    /// read from the same places it reads them (the main game's glyph table = the Architecture296 visual set, the lab's EA ward profile), so a variant is made from exactly the
    /// part it replaces. The sources are loaded as prefab contents (an isolated copy) and saved under a new path: no source prefab, profile, material or glyph table is written.
    /// Darker ink = new material copies (M_V2L_*) with a darker _Ink and _Glow 0; denser = the copied particle systems' rate / size / alpha; a few systems are duplicated
    /// inside a variant (branches, a seal's post, rising motes from the element's own rim). No emission, no additive, no light.
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2LastingBuild Run "report"    the donors the stage uses (and the ㅓ own auras), nothing written
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2LastingBuild Run "build"     build / rebuild the 25 variants and the set (editor idle, no Play)
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2LastingBuild Run "on" | "off"  the set's switch (TEST)</summary>
    public static class Vol2LastingBuild
    {
        public const string Out = "Assets/_Project/Art/SpellVFX308/Vol2Vfx/LegacyLasting";
        public const string SetPath = Out + "/Resources/" + Vol2LegacyLastingSet.ResourcePath + ".asset";
        const string MainVisualSetGuid = "5ca7cd6c9cb82c74883e785ac9cb734c";
        const string LabWardProfileGuid = "f44e606387fe7ec4fbc556d10459a38c";
        static readonly string[] Keys = { "ga", "na", "ma", "sa", "a" };
        static readonly Color InkBlack = new Color(.05f, .047f, .045f, 1f);

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode && c != "report") return "REFUSED Vol2LastingBuild " + c + ": Play is running (edit mode only)";
                switch (c)
                {
                    case "report": return Report();
                    case "build": case "all": return Build();
                    case "on": return Switch(true);
                    case var s when s.StartsWith("dump:", StringComparison.Ordinal): return Dump(s.Substring(5));
                    case "off": return Switch(false);
                }
                return "REFUSED Vol2LastingBuild: report | build | on | off";
            }
            catch (Exception e) { return "REFUSED Vol2LastingBuild " + c + ": " + e.GetType().Name + " " + e.Message; }
        }

        // ---------------------------------------------------------------- the donors (what the stage borrows)

        sealed class Donors
        {
            public readonly GameObject[] Rim = new GameObject[5], Seal = new GameObject[5], Buff = new GameObject[5], Ward = new GameObject[5], Guard = new GameObject[5];
            public readonly List<string> Missing = new List<string>();
            public SpellVisualSetSO Set;
        }

        static Vfx120Profile Prof(SpellVisualSetSO set, char c)
        {
            if (set == null || !set.TryGet(c, out var v) || v.FxPrefab == null) return null;
            var fx = v.FxPrefab.GetComponent<Vfx120Effect>();
            return fx != null ? fx.Profile : null;
        }

        static Donors Find()
        {
            var d = new Donors { Set = AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>(AssetDatabase.GUIDToAssetPath(MainVisualSetGuid)) };
            if (d.Set == null) { d.Missing.Add("main visual set " + MainVisualSetGuid); return d; }
            var ward = AssetDatabase.LoadAssetAtPath<EAWardProfileSO>(AssetDatabase.GUIDToAssetPath(LabWardProfileGuid));
            for (int i = 0; i < 5; i++)
            {
                d.Rim[i] = Prof(d.Set, Vol2LegacyDonors.Rim[i])?.Zone308Prefab;
                d.Seal[i] = Prof(d.Set, Vol2LegacyDonors.Seal[i])?.Mark308Prefab;
                d.Buff[i] = Prof(d.Set, Vol2LegacyDonors.Aura[i])?.Aura308Prefab;
                d.Guard[i] = Prof(d.Set, Vol2LegacyDonors.Guard[i])?.Guard308Prefab;
                if (d.Guard[i] == null) d.Missing.Add("guard " + Vol2LegacyDonors.Guard[i]);
                var wfx = ward != null && ward.PresentationPrefabs != null && i < ward.PresentationPrefabs.Length && ward.PresentationPrefabs[i] != null ? ward.PresentationPrefabs[i].GetComponent<Vfx120Effect>() : null;
                d.Ward[i] = wfx != null && wfx.Profile != null ? wfx.Profile.Aura308Prefab : null;
                if (d.Rim[i] == null) d.Missing.Add("rim " + Vol2LegacyDonors.Rim[i]);
                if (d.Seal[i] == null) d.Missing.Add("seal " + Vol2LegacyDonors.Seal[i]);
                if (d.Buff[i] == null) d.Missing.Add("buff " + Vol2LegacyDonors.Aura[i]);
                if (d.Ward[i] == null) d.Missing.Add("ward " + Vol2LegacyDonors.Ward[i]);
            }
            return d;
        }

        static string Report()
        {
            var d = Find(); var sb = new StringBuilder();
            string P(GameObject g) => g != null ? AssetDatabase.GetAssetPath(g) : "NONE";
            for (int i = 0; i < 5; i++)
                sb.AppendLine(Keys[i] + ": rim " + P(d.Rim[i]) + " | seal " + P(d.Seal[i]) + " | buff " + P(d.Buff[i]) + " | ward film " + P(d.Ward[i]));
            foreach (char c in Vol2LegacyDonors.OwnAura) { var p = Prof(d.Set, c); sb.AppendLine("  own aura " + c + ": " + (p != null && p.Aura308Prefab != null ? AssetDatabase.GetAssetPath(p.Aura308Prefab) : "none")); }
            foreach (char c in "걱넉먹석억") { var p = Prof(d.Set, c); sb.AppendLine("  A1 " + c + ": " + (p != null && p.Aura308Prefab != null ? AssetDatabase.GetAssetPath(p.Aura308Prefab) : "none")); }
            if (d.Missing.Count > 0) sb.AppendLine("MISSING: " + string.Join(", ", d.Missing));
            return sb.ToString();
        }

        /// <summary>dump:&lt;prefab path&gt; - every particle system of a prefab (read only): rate, cap, delay, life, size, alpha peak of its colour over life, render mode, material.</summary>
        static string Dump(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path.Trim());
            if (go == null) return "no prefab " + path;
            var sb = new StringBuilder(go.name + " scale " + go.transform.localScale.x.ToString("0.00") + "\n");
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var m = ps.main; var em = ps.emission; var col = ps.colorOverLifetime; var r = ps.GetComponent<ParticleSystemRenderer>();
                float peak = col.enabled && col.color.mode == ParticleSystemGradientMode.Gradient ? col.color.gradient.alphaKeys.Max(k => k.alpha) : -1f;
                string rel = AnimationUtility.CalculateTransformPath(ps.transform, go.transform);
                sb.AppendLine(rel + ": rate " + em.rateOverTime.constantMax.ToString("0.0") + " bursts " + em.burstCount + " max " + m.maxParticles + " delay " + m.startDelay.constantMax.ToString("0.00") + " life " + m.startLifetime.constantMax.ToString("0.00")
                    + " size " + (m.startSize3D ? m.startSizeY.constantMax : m.startSize.constantMax).ToString("0.00") + " alpha " + peak.ToString("0.00") + " start a " + m.startColor.color.a.ToString("0.00") + " " + r.renderMode + " sim " + m.simulationSpace + " scaling " + m.scalingMode
                    + " shape " + (ps.shape.enabled ? ps.shape.shapeType + " r " + ps.shape.radius.ToString("0.00") : "off") + " mat " + (r.sharedMaterial != null ? r.sharedMaterial.name : "none") + " emit " + em.enabled);
            }
            return sb.ToString();
        }

        static string Switch(bool on)
        {
            var set = AssetDatabase.LoadAssetAtPath<Vol2LegacyLastingSet>(SetPath);
            if (set == null) return "no set at " + SetPath + " (build first)";
            set.Enabled = on; EditorUtility.SetDirty(set); AssetDatabase.SaveAssetIfDirty(set);
            return "Vol2LegacyLastingSet.Enabled = " + on;
        }

        // ---------------------------------------------------------------- build

        static readonly List<string> Log = new List<string>();
        static readonly Dictionary<string, Material> Dark = new Dictionary<string, Material>();

        static string Build()
        {
            var d = Find();
            if (d.Missing.Count > 0) return "REFUSED build: donors missing: " + string.Join(", ", d.Missing);
            Log.Clear(); Dark.Clear();
            foreach (var dir in new[] { Out, Out + "/Materials", Out + "/Resources", Out + "/Patterns" }) if (!AssetDatabase.IsValidFolder(dir)) { var parent = Path.GetDirectoryName(dir).Replace('\\', '/'); AssetDatabase.CreateFolder(parent, Path.GetFileName(dir)); }
            var set = AssetDatabase.LoadAssetAtPath<Vol2LegacyLastingSet>(SetPath);
            bool fresh = set == null;
            if (fresh) { set = ScriptableObject.CreateInstance<Vol2LegacyLastingSet>(); AssetDatabase.CreateAsset(set, SetPath); }
            var pairs = new List<Vol2LegacyLastingSet.Pair>();
            for (int i = 0; i < 5; i++)
            {
                string k = Keys[i];
                // the KCISA patterns of this element (vol.2-owned SeedLeafMark materials on the pack's textures, read only; tinted with the body palette)
                var pal = Prof(d.Set, Vol2LegacyDonors.Single[i]);
                Color tint = pal != null ? Color.Lerp(pal.Pigment, pal.Ink, .5f) : new Color(.12f, .1f, .09f, 1f); tint.a = 1f;
                if (set.PatRing == null || set.PatRing.Length != 5) set.PatRing = new GameObject[5];
                if (set.PatFan == null || set.PatFan.Length != 5) set.PatFan = new GameObject[5];
                if (set.PatSeal == null || set.PatSeal.Length != 5) set.PatSeal = new GameObject[5];
                set.PatRing[i] = PatPrefab("ring", k, PatMat("ring", k, PatRingTex, tint), Vol2PatternDecal.Kind.Disc);
                set.PatFan[i] = PatPrefab("fan", k, PatMat("fan", k, PatFanTex, tint), Vol2PatternDecal.Kind.Fan);
                set.PatSeal[i] = PatPrefab("seal", k, PatMat("seal", k, PatSealTex, Color.Lerp(tint, InkBlack, .35f)), Vol2PatternDecal.Kind.Plate);
                set.Growth[i] = Make(d.Rim[i], "PF_V2L_grow_" + k, root => Grow(root, k));
                var carry = Make(d.Rim[i], "PF_V2L_carry_" + k, root => Carry(root, k));
                var film = Make(d.Ward[i], "PF_V2L_film_" + k, root => Film(root, d.Rim[i], k));
                var seal = Make(d.Seal[i], "PF_V2L_seal_" + k, root => Seal(root, k));
                var wrap = Make(d.Buff[i], "PF_V2L_wrap_" + k, root => Wrap(root, k));   // no pattern frieze on the sleeves (SleeveBands): tried, hidden in the robe's full sleeve from the shoulder camera (step 5a probe) - the ink drops read there, a pattern did not
                set.Wrap[i] = wrap;
                if (set.Stake == null || set.Stake.Length != 5) set.Stake = new GameObject[5];
                set.Stake[i] = Make(d.Guard[i], "PF_V2L_stake_" + k, root => Stake(root, k));
                if (set.Band == null || set.Band.Length != 5) set.Band = new GameObject[5];
                set.Band[i] = Make(d.Rim[i], "PF_V2L_band_" + k, root => Band(root, k));
                pairs.Add(new Vol2LegacyLastingSet.Pair { From = d.Rim[i], To = carry });
                pairs.Add(new Vol2LegacyLastingSet.Pair { From = d.Ward[i], To = film });
                pairs.Add(new Vol2LegacyLastingSet.Pair { From = d.Seal[i], To = seal });
                pairs.Add(new Vol2LegacyLastingSet.Pair { From = d.Buff[i], To = wrap });
            }
            set.Variants = pairs.ToArray();
            set.SealAbove = .1f; set.SealScale = .75f;            // TEST: the plate floats just over the body and stays under the shoulder eye (2.25 m)
            EditorUtility.SetDirty(set); AssetDatabase.SaveAssetIfDirty(set);
            foreach (var m in Dark.Values) AssetDatabase.SaveAssetIfDirty(m);
            // the contract: no light, no emission keyword, every renderer material InkBleed with no glow
            int lights = 0, emissive = 0, glow = 0, mats = 0;
            foreach (var go in set.Growth.Concat(set.Wrap).Concat(set.Stake).Concat(set.Band).Concat(set.PatRing).Concat(set.PatFan).Concat(set.PatSeal).Concat(set.Variants.Select(p => p.To)).Where(g => g != null).Distinct())
            {
                lights += go.GetComponentsInChildren<Light>(true).Length;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) continue; mats++;
                        if (m.IsKeywordEnabled("_EMISSION")) emissive++;
                        if (m.HasProperty("_Glow") && m.GetFloat("_Glow") > 0.001f) glow++;
                    }
            }
            return "built " + (fresh ? "(new set) " : "") + "35 variants + 15 pattern decals -> " + Out + " | set " + SetPath + " Enabled=" + set.Enabled + " | lights " + lights + " emissive " + emissive + " glow " + glow + " (of " + mats + " material uses)\n" + string.Join("\n", Log);
        }

        // ---------------------------------------------------------------- KCISA patterns (DECISIONS #135 / #138 / #189: layered as ritual language, textures reused; the pack is not written)

        const string Pack = "Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/";
        /// <summary>Pattern_79 (a bold toothed ring round a few signs: the ring state; its teeth at ~.85 - 1 of the radius), Pattern_57 (a bold flourished roundel: a fan clips a wedge of it, its rim = the fan's arc) -
        /// the first picks, Pattern_164 (scroll ring) and Pattern_139 (spoked wheel), were hair-thin and vanished on the ground at pitch 0 (step 5a probe);
        /// Pattern_25 (the 回 seal), Pattern_236 (a cloud frieze in a frame, wound round the sleeve).</summary>
        const string PatRingTex = Pack + "Pattern_79.png", PatFanTex = Pack + "Pattern_57.png", PatSealTex = Pack + "Pattern_25.png", PatBand = Pack + "Pattern_236.png";

        static Material PatMat(string role, string k, string texture, Color tint)
        {
            string path = Out + "/Patterns/M_V2L_pat_" + role + "_" + k + ".mat";
            var shader = Shader.Find("Oheangbu/VFX120/SeedLeafMark") ?? throw new Exception("SeedLeafMark shader not found");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texture) ?? throw new Exception("missing " + texture);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader; m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", tint);
            m.SetFloat("_Pattern", 1f); m.SetFloat("_Alpha", .9f); m.SetFloat("_Soft", 0f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_Body", 0f); m.SetFloat("_Metal", 0f); m.SetFloat("_Fluid", 0f);
            m.DisableKeyword("_EMISSION"); m.renderQueue = 3000;
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            Log.Add(Path.GetFileNameWithoutExtension(path) + " <- " + texture);
            return m;
        }

        static GameObject PatPrefab(string role, string k, Material m, Vol2PatternDecal.Kind kind)
        {
            string path = Out + "/Patterns/PF_V2L_pat_" + role + "_" + k + ".prefab";
            var go = new GameObject(Path.GetFileNameWithoutExtension(path));
            try
            {
                go.layer = 20;                                                       // VfxAfterFog, as the old spell parts
                go.AddComponent<MeshFilter>(); var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
                var dc = go.AddComponent<Vol2PatternDecal>(); dc.Shape = kind; dc.Size = 1f; dc.Alpha = kind == Vol2PatternDecal.Kind.Plate ? .95f : .85f;
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>ㅓ (걱 included): the frieze wound round both upper sleeves, on the bones every frame (Vol2PatternDecal Band).</summary>
        static void SleeveBands(GameObject root, Material m)
        {
            foreach (var (name, from, to) in new[] { ("PatternSleeveR", HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm), ("PatternSleeveL", HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm) })
            {
                var go = new GameObject(name); go.transform.SetParent(root.transform, false); go.layer = 20;
                go.AddComponent<MeshFilter>(); var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
                var dc = go.AddComponent<Vol2PatternDecal>(); dc.Shape = Vol2PatternDecal.Kind.Band; dc.Size = .21f; dc.BandHeight = .17f; dc.BandTiles = 2f; dc.From = from; dc.To = to; dc.At = .5f; dc.Alpha = .95f;
            }
        }

        /// <summary>The source prefab's contents (an isolated copy), edited, saved under the new path (an existing variant is overwritten in place, its GUID kept).</summary>
        static GameObject Make(GameObject source, string name, Action<GameObject> edit)
        {
            string src = AssetDatabase.GetAssetPath(source), dst = Out + "/" + name + ".prefab";
            if (string.IsNullOrEmpty(src) || src.StartsWith(Out, StringComparison.Ordinal)) throw new Exception("bad source for " + name + ": " + src);
            var root = PrefabUtility.LoadPrefabContents(src);
            try
            {
                root.name = name;
                edit(root);
                var saved = PrefabUtility.SaveAsPrefabAsset(root, dst, out bool ok);
                if (!ok || saved == null) throw new Exception("save failed " + dst);
                Log.Add(name + " <- " + src);
                return saved;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ---------------------------------------------------------------- particle helpers (on the copy only)

        static ParticleSystem Sys(GameObject root, string path)
        {
            var t = root.transform.Find(path);
            return t != null ? t.GetComponent<ParticleSystem>() : null;
        }

        static ParticleSystem.MinMaxCurve Scale(ParticleSystem.MinMaxCurve c, float k)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant: return new ParticleSystem.MinMaxCurve(c.constant * k);
                case ParticleSystemCurveMode.TwoConstants: return new ParticleSystem.MinMaxCurve(c.constantMin * k, c.constantMax * k);
                case ParticleSystemCurveMode.Curve: return new ParticleSystem.MinMaxCurve(c.curveMultiplier * k, c.curve);
                default: return new ParticleSystem.MinMaxCurve(c.curveMultiplier * k, c.curveMin, c.curveMax);
            }
        }

        static void Size(ParticleSystem ps, float k)
        {
            if (ps == null) return;
            var m = ps.main;
            if (m.startSize3D) { m.startSizeX = Scale(m.startSizeX, k); m.startSizeY = Scale(m.startSizeY, k); m.startSizeZ = Scale(m.startSizeZ, k); }
            else m.startSize = Scale(m.startSize, k);
        }

        /// <summary>A strand's width (x) and height (y) apart (3D start size; a plain size takes the height factor).</summary>
        static void SizeXY(ParticleSystem ps, float kx, float ky)
        {
            if (ps == null) return;
            var m = ps.main;
            if (!m.startSize3D) { Size(ps, ky); return; }
            m.startSizeX = Scale(m.startSizeX, kx); m.startSizeY = Scale(m.startSizeY, ky);
        }

        static void Rate(ParticleSystem ps, float k, float capK = -1f)
        {
            if (ps == null) return;
            var em = ps.emission; em.rateOverTime = Scale(em.rateOverTime, k);
            var m = ps.main; m.maxParticles = Mathf.Max(1, Mathf.CeilToInt(m.maxParticles * (capK > 0f ? capK : k)));
        }

        /// <summary>The colour over life: alpha times <paramref name="alpha"/> (at most <paramref name="cap"/>), the colour keys <paramref name="darken"/> of the way to the ink.</summary>
        static void Tone(ParticleSystem ps, float alpha, float darken = 0f, float cap = 1f)
        {
            if (ps == null) return;
            var col = ps.colorOverLifetime;
            if (!col.enabled || col.color.mode != ParticleSystemGradientMode.Gradient) return;
            var g = col.color.gradient;
            var ck = g.colorKeys; var ak = g.alphaKeys;
            for (int i = 0; i < ck.Length; i++) ck[i].color = Color.Lerp(ck[i].color, InkBlack, darken);
            for (int i = 0; i < ak.Length; i++) ak[i].alpha = Mathf.Min(cap, ak[i].alpha * alpha);
            var n = new Gradient(); n.SetKeys(ck, ak); n.mode = g.mode;
            col.color = new ParticleSystem.MinMaxGradient(n);
        }

        /// <summary>A darker copy of the renderer's ink material (M_V2L_&lt;source&gt;: _Ink toward the ink black, opacity up, no glow, no emission); the source is not written.</summary>
        static void Darker(ParticleSystem ps, float toInk, float opacityAdd = .1f)
        {
            if (ps == null) return;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            var src = r != null ? r.sharedMaterial : null;
            if (src == null) return;
            string key = AssetDatabase.GetAssetPath(src) + "|" + toInk.ToString("0.00") + "|" + opacityAdd.ToString("0.00");
            if (!Dark.TryGetValue(key, out var m))
            {
                string path = Out + "/Materials/M_V2L_" + src.name.Replace("M_V308", "") .TrimStart('_') + "_d" + Mathf.RoundToInt(toInk * 100f) + ".mat";
                m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); } else { m.shader = src.shader; m.CopyPropertiesFromMaterial(src); }
                if (m.HasProperty("_Ink")) { var ink = src.GetColor("_Ink"); var dark = Color.Lerp(ink, InkBlack, toInk); dark.a = Mathf.Min(1f, ink.a + opacityAdd); m.SetColor("_Ink", dark); }
                if (m.HasProperty("_Opacity")) m.SetFloat("_Opacity", Mathf.Min(1f, src.GetFloat("_Opacity") + opacityAdd));
                if (m.HasProperty("_Glow")) m.SetFloat("_Glow", 0f);
                if (m.HasProperty("_Radial")) m.SetFloat("_Radial", 0f);
                m.DisableKeyword("_EMISSION");
                EditorUtility.SetDirty(m);
                Dark[key] = m;
            }
            r.sharedMaterial = m;
        }

        static void Life(ParticleSystem ps, float k)
        {
            if (ps == null) return;
            var m = ps.main; m.startLifetime = Scale(m.startLifetime, k);
        }

        /// <summary>The rim's per-metre density (Vfx308RingDensity) without its small-circle cut: a residue disc of 0.9 - 1.8 m dropped the strands (OffBelow 1.2) and halved the
        /// rest (HalfBelow 2) - the step 5a probe saw 3 live particles on 각's residue.</summary>
        static void FullDensity(GameObject root)
        {
            foreach (var dens in root.GetComponentsInChildren<Vfx308RingDensity>(true)) { dens.OffBelow = 0f; dens.HalfBelow = 0f; }
        }

        static ParticleSystem Duplicate(ParticleSystem ps, string name, Transform parent = null)
        {
            var go = Object.Instantiate(ps.gameObject, parent != null ? parent : ps.transform.parent, false);
            go.name = name;
            var n = go.GetComponent<ParticleSystem>();
            n.useAutoRandomSeed = false; n.randomSeed = ps.randomSeed + 7919u;
            return n;
        }

        // ---------------------------------------------------------------- the five families

        /// <summary>남긴다: the element's 결계 rim grown into a standing mass - taller, denser, darker strands on a band of the rim (not only its line), tilted branches among
        /// them, a darker rim line and more motes. The strands keep the rim's own sway, unravel and Shape scaling (heights do not grow with the radius).</summary>
        static void Grow(GameObject root, string k)
        {
            var rim = Sys(root, "Rim"); var foot = Sys(root, "PostFoot"); var top = Sys(root, "PostTop"); var motes = Sys(root, "Motes"); var pour = Sys(root, "Pour");
            FullDensity(root);
            Tone(pour, 1.15f);
            Tone(rim, 1.4f, .2f); Size(rim, 1.4f); Rate(rim, 1.6f);
            foreach (var s in new[] { foot, top })
            {
                if (s == null) continue;
                SizeXY(s, 3.2f, 2.3f); Rate(s, 5f, 6f); Darker(s, .45f);     // wide brush strands, not weeds (step 5a probe 3)
                var sh = s.shape; sh.radiusThickness = .3f;                         // a band 30 % deep inside the rim: a thicket, not one line
                var n = s.noise; if (n.enabled) n.strengthMultiplier *= 1.5f;
            }
            Tone(foot, 1.35f, .3f); Tone(top, 2.0f, .3f);
            if (k != "ga")
            {
                // the ink_strand / dust / streak sheets are thinner than wood's strokes (step 5a capture: the wood growth read, the others hardly): wider and darker
                SizeXY(foot, 1.5f, 1.1f); SizeXY(top, 1.4f, 1f); Tone(foot, 1.5f, .3f); Tone(top, 1.4f, .3f);
            }
            SizeXY(top, .9f, .8f);                                                    // the tops end near 1.9 m: under the shoulder eye (above it the wash look pales them on the sky)
            if (foot != null)
            {
                // branches: shorter strands leaning out of the thicket
                var b = Duplicate(foot, "Branch");
                Size(b, .7f); Rate(b, .6f);
                var m = b.main; m.startRotation = new ParticleSystem.MinMaxCurve(-42f * Mathf.Deg2Rad, 42f * Mathf.Deg2Rad);
                var sh = b.shape; sh.radiusThickness = .5f;
            }
            if (motes != null)
            {
                Rate(motes, 5f); Size(motes, 3.2f); Tone(motes, 1.3f, .2f); Life(motes, .6f);
                var v = motes.velocityOverLifetime; if (v.enabled) v.y = Scale(v.y, 2f);
            }
        }

        /// <summary>ㅓ: the rim carried round the caster's feet - a darker, wider rim line, short dark strands (no tall top: the player stands in it) and rising motes.</summary>
        static void Carry(GameObject root, string k)
        {
            var rim = Sys(root, "Rim"); var foot = Sys(root, "PostFoot"); var top = Sys(root, "PostTop"); var motes = Sys(root, "Motes");
            FullDensity(root);
            Tone(rim, 1.45f, .25f); Size(rim, 1.6f); Rate(rim, 2f);
            SizeXY(foot, 2.6f, 1.5f); Rate(foot, 3f, 3.5f); Tone(foot, 1.35f, .3f); Darker(foot, .4f);
            // the shoulder camera at pitch 0 sees the ground only from ~1 m in front of the caster: the ring's front hides under the figure, its sides show - the strands
            // stand up to hip height there (the old faint top made a dark short one)
            SizeXY(top, 2.4f, 1.25f); Rate(top, 2.5f, 3f); Tone(top, 2.2f, .3f); Darker(top, .4f);
            if (motes != null)
            {
                Rate(motes, 8f); Size(motes, 3.4f); Tone(motes, 1.35f, .2f); Life(motes, .55f);
                var v = motes.velocityOverLifetime; if (v.enabled) v.y = Scale(v.y, 3f);
            }
        }

        /// <summary>ㅜ / ㅗ ring states: the element's ward membrane as a continuous ink band - the ground ink line darker, denser and wider, the hold film many times more opaque
        /// and undulating (noise), the veil of the drawn segments visible, and the element's rim motes rising off the band (copied in from its 결계 rim).</summary>
        static void Film(GameObject root, GameObject rimSource, string k)
        {
            var line = Sys(root, "Rim"); var hold = Sys(root, "Hold");
            Tone(line, 1.75f, .2f); Size(line, 1.45f); Rate(line, 2.2f);
            if (line != null) { var m = line.main; m.startDelay = .45f; }
            if (hold != null)
            {
                Tone(hold, 15f, .25f, .6f); Rate(hold, 2.6f, 3f); Size(hold, 1.35f);
                var m = hold.main; m.startDelay = .6f;
                var n = hold.noise; n.enabled = true; n.strength = .09f; n.frequency = .6f; n.scrollSpeed = .45f; n.quality = ParticleSystemNoiseQuality.Low;
            }
            foreach (Transform seg in root.transform)
            {
                if (!seg.name.StartsWith("Seg", StringComparison.Ordinal)) continue;
                Tone(seg.Find("Veil")?.GetComponent<ParticleSystem>(), 8f, .2f);
                Tone(seg.Find("Foot")?.GetComponent<ParticleSystem>(), 1.1f, .1f);
            }
            // rising motes: the element's own rim motes, per 1 m of ring here (the membrane is scaled by the ring's radius; the motes' shape follows, their size does not)
            var rimRoot = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(rimSource));
            try
            {
                var src = Sys(rimRoot, "Motes");
                if (src != null)
                {
                    var go = Object.Instantiate(src.gameObject, root.transform, false); go.name = "Motes";
                    foreach (var dens in go.GetComponents<Vfx308RingDensity>()) Object.DestroyImmediate(dens);
                    var ps = go.GetComponent<ParticleSystem>(); ps.useAutoRandomSeed = false; ps.randomSeed = 52811u + (uint)k.Length;
                    var em = ps.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(18f);
                    var m = ps.main; m.maxParticles = 64; m.startDelay = .5f;
                    Size(ps, 1.7f); Tone(ps, 1.4f, .25f); Life(ps, .6f);   // rising to ~1.5 m, under the eye (over the sky the wash look pales them)
                    var v = ps.velocityOverLifetime; if (v.enabled) v.y = Scale(v.y, 2.5f);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(rimRoot); }
        }

        /// <summary>심는다 on a body: the install seal stood up as a plate facing the eye (its ring of strokes in the local XY plane - the stage turns the root to the camera and
        /// floats it above the body), darker and larger, with an upright post of the same strokes under it pointing down at the body.</summary>
        static void Seal(GameObject root, string k)
        {
            var band = Sys(root, "Band"); var post = Sys(root, "Post"); var haze = Sys(root, "Haze");
            foreach (var s in new[] { band, post })
            {
                if (s == null) continue;
                var sh = s.shape; sh.rotation = Vector3.zero;                       // the ring stands upright (it lay flat round the body's middle)
                Size(s, 1.35f); Tone(s, 1.3f, .4f); Darker(s, .4f);
            }
            Tone(haze, 1.6f);
            if (post != null)
            {
                var stem = Duplicate(post, "Stem");
                var sh = stem.shape; sh.enabled = false;                            // a disabled shape emits at the system's own origin
                stem.transform.localPosition = new Vector3(0f, -.78f, 0f);
                var m = stem.main; m.startSize3D = true;
                var s0 = m.startSize; float lo = s0.mode == ParticleSystemCurveMode.TwoConstants ? s0.constantMin : s0.constant, hi = s0.mode == ParticleSystemCurveMode.TwoConstants ? s0.constantMax : s0.constant;
                m.startSizeX = new ParticleSystem.MinMaxCurve(lo * .9f, hi * .9f); m.startSizeY = new ParticleSystem.MinMaxCurve(lo * 1.9f, hi * 1.9f); m.startSizeZ = new ParticleSystem.MinMaxCurve(1f);
                m.startRotation = new ParticleSystem.MinMaxCurve(-4f * Mathf.Deg2Rad, 4f * Mathf.Deg2Rad);
                var em = stem.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(2.2f); m.maxParticles = 5;
            }
        }

        /// <summary>심는다 on a body: the element's guard membrane (three upright strokes over a wet blot, the old 거 너 머 서 어 body) planted as a stake at the body's side - 0.72 of
        /// its size (1.65 m, over a bear's silhouette, under the shoulder eye), the strokes held dark (the old one eroded to .45 after the parry window), the blot behind
        /// them lighter so the body beside stays readable.</summary>
        static void Stake(GameObject root, string k)
        {
            root.transform.localScale = Vector3.one * .72f;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                string n = ps.gameObject.name;
                if (n.StartsWith("HoldStroke", StringComparison.Ordinal)) Tone(ps, 1.9f, .1f, .95f);
                else if (n == "HoldBody") Tone(ps, .75f);
            }
        }

        /// <summary>ㅓ: the buff band's ink wound up the great brush made a clear dark wrap (more, larger, darker drops on the spiral), the feet ring and the camera-side strokes
        /// darker and more frequent.</summary>
        static void Wrap(GameObject root, string k)
        {
            var brush = Sys(root, "Brush"); var ring = Sys(root, "Ring"); var thread = Sys(root, "Thread");
            Rate(brush, 2.6f, 3.2f); Size(brush, 1.9f); Tone(brush, 1.25f, .4f); Darker(brush, .35f);
            Tone(ring, 4.2f, .3f); Size(ring, 1.2f);
            Tone(thread, 1.25f);
            foreach (var n in new[] { "Cam/StrokeL", "Cam/StrokeR" }) { var s = Sys(root, n); Rate(s, 2.2f, 2f); Tone(s, 1.4f, .2f); }
            Tone(Sys(root, "End/Rim"), 2f);
            // the sleeve: the same spiral of ink round the right forearm (Vol2LimbWrap) - the brush shaft is dark ink, the wrap on it did not read (step 5a probe);
            // the robe is light, a dark band on its sleeve does
            if (brush != null)
            {
                // both upper sleeves (shoulder -> elbow): from the shoulder camera the forearm holding the brush is mostly under the frame, the upper arms are in it
                foreach (var (name, from, to) in new[] { ("SleeveR", HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm), ("SleeveL", HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm) })
                {
                    var sleeve = Duplicate(brush, name, root.transform);
                    foreach (var w in sleeve.GetComponents<AuraBrushWrap308>()) Object.DestroyImmediate(w);
                    var lw = sleeve.gameObject.AddComponent<Vol2LimbWrap>(); lw.From = from; lw.To = to;
                    var sh = sleeve.shape; sh.radius = .13f;                        // round the robe's sleeve, not the shaft
                    Size(sleeve, 2f); Rate(sleeve, 3f, 3.5f);
                }
            }
        }

        /// <summary>ㅜ / ㅗ ring states: the element's 결계 rim as a continuous dark band - the rim line darker, wider and three times as dense, the strands standing to the knee and
        /// hip with twice the sway (undulating), the motes rising off it (to ~1.5 m: under the shoulder eye). The ward membrane's standing sheets did not show in the ink-wash
        /// look - only its ground dots (step 5a probe) - so the band is built from the rim, the 결계's own family.</summary>
        static void Band(GameObject root, string k)
        {
            var rim = Sys(root, "Rim"); var foot = Sys(root, "PostFoot"); var top = Sys(root, "PostTop"); var motes = Sys(root, "Motes");
            FullDensity(root);
            Tone(rim, 1.55f, .3f); Size(rim, 1.5f); Rate(rim, 3f);
            foreach (var s in new[] { foot, top })
            {
                if (s == null) continue;
                Rate(s, 3f, 3.5f); Darker(s, .4f);
                var n = s.noise; if (n.enabled) n.strengthMultiplier *= 2f;
            }
            SizeXY(foot, 3.0f, 2.6f); Tone(foot, 1.45f, .35f);
            SizeXY(top, 2.4f, 1.8f); Tone(top, 2.4f, .35f);
            if (motes != null)
            {
                Rate(motes, 10f); Size(motes, 3.5f); Tone(motes, 1.4f, .25f); Life(motes, .55f);
                var v = motes.velocityOverLifetime; if (v.enabled) v.y = Scale(v.y, 3f);
            }
        }
    }
}
