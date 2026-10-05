using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 먼 불빛 — read-only checks (SPEC-ATTRACTION-LIGHT-308 AC-L1..L8, L11; L13 = the paper variant, L14 = the DRAWN sleeve measured — FarLight308.Paper.cs) and the capture entries. Nothing here writes a scene or
    // an asset; the report file and the stills go under Art/Playtest308/Relayout/farlight/.
    public static partial class FarLight308
    {
        static string Check(string scenePath)
        {
            PostLedger308.RequireEditable();
            var c = Load(); var scene = PostLedger308.Open(scenePath);
            int layer = LayerMask.NameToLayer(c.layer); int fail = 0, warn = 0;
            var sb = new StringBuilder("FarLight308 check " + scenePath + " " + PostLedger308.Utc() + " (" + c.version + ")\n");
            void Line(bool ok, string ac, string text) { if (!ok) fail++; sb.AppendLine((ok ? "  ok   " : "  FAIL ") + ac + " " + text); }
            void Warn(string ac, string text) { warn++; sb.AppendLine("  WARN " + ac + " " + text); }

            var profile = AssetDatabase.LoadAssetAtPath<AttractionLightProfileSO>(c.profileAsset);
            var cards = PostLedger308.All<AttractionLight308>(scene).ToList();
            var glowRenderers = PostLedger308.All<Renderer>(scene).Where(r => r.sharedMaterials.Any(m => m != null && m.shader != null && m.shader.name == c.shader)).ToList();
            var papers = PostLedger308.All<Transform>(scene).Where(t => t.name == c.paper.name).ToList();
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == c.root);

            // ---- AC-L1: counts of new sustained emissive objects, by type
            string paperVariant = Variant(c, ""); bool paperOn = paperVariant != P0;   // P0 = no sleeve anywhere
            string paperMatWant = paperVariant == P2 ? HanjiPath(c) : c.paperMaterial;
            var want = new Dictionary<AttractionLightProfileSO.Kind, int>(); int wantPaper = 0, pending = 0, missing = 0;
            foreach (var row in c.lights)
            {
                var lamp = Resolve(scene, row.path, V3(row.at), c.tolerance_m);
                if (lamp == null) { if (row.optional) pending++; else { missing++; sb.AppendLine("  MISSING lamp " + row.id + " (" + row.path + ")"); } continue; }
                var kind = ParseKind(row.kind);
                if (row.glow) want[kind] = (want.TryGetValue(kind, out int n) ? n : 0) + 1;
                if (row.paper && paperOn) wantPaper++;
            }
            int poles = c.poles.Count(p => p.enabled);
            foreach (var p in c.poles.Where(p => p.enabled)) { var kind = ParseKind(p.kind); want[kind] = (want.TryGetValue(kind, out int n) ? n : 0) + 1; if (paperOn) wantPaper++; }
            foreach (AttractionLightProfileSO.Kind kind in Enum.GetValues(typeof(AttractionLightProfileSO.Kind)))
            {
                int have = cards.Count(x => x.Kind == kind), need = want.TryGetValue(kind, out int n) ? n : 0;
                Line(have == need, "AC-L1", "far glow cards " + kind + ": " + have + " (data " + need + ")");
            }
            Line(papers.Count == wantPaper, "AC-L1", "paper sleeves (" + c.paper.name + ", variant " + paperVariant + "): " + papers.Count + " (data " + wantPaper + ")");
            int poleLights = root != null ? root.GetComponentsInChildren<Light>(true).Length : 0;
            Line(poleLights == poles, "AC-L1", "new Point lights (poles under " + c.root + "): " + poleLights + " (data " + poles + ")");
            Line(missing == 0, "AC-L1", "lamps of the data present: missing " + missing + ", optional pending " + pending);
            foreach (var p in papers)
            {
                var r = p.GetComponent<MeshRenderer>(); string mat = r != null && r.sharedMaterial != null ? AssetDatabase.GetAssetPath(r.sharedMaterial) : "";
                if (mat != paperMatWant) Line(false, "AC-L1", "paper " + PostLedger308.PathOf(p) + " uses " + mat + " (expected " + paperMatWant + ", variant " + paperVariant + ")");
            }

            // ---- AC-L2: caps (never a bulb): profile, baked materials, the scene's Bloom threshold [M]
            float bloom = float.PositiveInfinity; string bloomFrom = "no Bloom override in the scene volumes";
            foreach (var v in PostLedger308.All<Volume>(scene))
            {
                var vp = v.sharedProfile; if (vp == null || !v.isActiveAndEnabled) continue;
                if (vp.TryGet(out Bloom b) && b.active && b.threshold.overrideState && b.threshold.value < bloom) { bloom = b.threshold.value; bloomFrom = v.name + " threshold " + F(bloom, "0.###") + ", intensity " + F(b.intensity.value, "0.###"); }
            }
            sb.AppendLine("  info AC-L2 Bloom [M]: " + bloomFrom + " (data " + F(c.bloomThreshold, "0.###") + ")");
            // ---- AC-L13: the paper variant (mesh, material, pose per row; the 한지 material and its peak)
            CheckPapers(c, scene, bloom, Line, sb);
            // ---- AC-L14: what is drawn, measured from the sleeve's vertices in the lantern body's own metres (P2 / P1 / P0 alike)
            CheckDrawn(c, scene, Line, sb);
            if (profile == null) Line(false, "AC-L2", "profile missing: " + c.profileAsset);
            else
            {
                foreach (var e in profile.Entries ?? Array.Empty<AttractionLightProfileSO.Entry>())
                {
                    float centre = AttractionLightProfileSO.CentreOverWhite(e);
                    float thr = float.IsInfinity(bloom) ? profile.BloomThreshold : bloom;
                    Line(profile.WithinCaps(e) && centre < bloom, "AC-L2", e.Kind + ": brightest centre over white " + F(centre, "0.###") + " <= " + F(profile.MaxCentre, "0.###") + " < Bloom " + (float.IsInfinity(bloom) ? F(profile.BloomThreshold, "0.###") + " (data)" : F(bloom, "0.###"))
                        + "; share the Bloom soft knee still takes: " + F(AttractionLightProfileSO.BloomShare(centre, thr) * 100f, "0.#") + " % (not zero: the URP threshold is gamma, the knee is half of it)");
                    // AC-L11: a thing in the world, not a marker — world size capped, pixel floor and strength fall with distance
                    Line(e.MaxWorldRadius > 0f && e.MaxWorldRadius >= e.WorldRadius && e.MinPxFar > 0f && e.MinPxFar <= e.MinPx && e.FarPeak > 0f && e.FarPeak <= 1f && c.cullBounds_m * .35f >= e.MaxWorldRadius, "AC-L11",
                        e.Kind + ": world diameter <= " + F(2f * e.MaxWorldRadius) + " m at any distance; pixel floor " + F(e.MinPx) + " -> " + F(e.MinPxFar) + " px and strength 1 -> " + F(e.FarPeak) + " between " + F(e.NearFade.y) + " and " + F(e.FarFade.x) + " m");
                    var m = e.Material;
                    bool baked = m != null && m.shader != null && m.shader.name == c.shader && Mathf.Approximately(m.GetFloat("_Peak"), e.Peak) && Mathf.Approximately(m.GetFloat("_Cover"), e.Cover)
                        && Mathf.Approximately(m.GetFloat("_MinPx"), e.MinPx) && Mathf.Approximately(m.GetFloat("_MaxPx"), e.MaxPx) && Mathf.Approximately(m.GetFloat("_Radius"), e.WorldRadius)
                        && Mathf.Approximately(m.GetFloat("_MinPxFar"), e.MinPxFar) && Mathf.Approximately(m.GetFloat("_MaxWorld"), e.MaxWorldRadius) && Mathf.Approximately(m.GetFloat("_FarPeak"), e.FarPeak)
                        && Mathf.Approximately(m.GetVector("_NearFade").x, e.NearFade.x) && Mathf.Approximately(m.GetVector("_NearFade").y, e.NearFade.y)
                        && Mathf.Approximately(m.GetVector("_FarFade").x, e.FarFade.x) && Mathf.Approximately(m.GetVector("_FarFade").y, e.FarFade.y) && Mathf.Approximately(m.GetFloat("_RefHeight"), profile.ReferenceHeight);
                    Line(baked, "AC-L2", e.Kind + ": material " + (m != null ? AssetDatabase.GetAssetPath(m) : "MISSING") + (baked ? " = profile" : " differs from the profile (run sync)"));
                    var k = c.kinds.FirstOrDefault(x => x.kind == e.Kind.ToString());
                    if (k != null && (!Mathf.Approximately(k.peak, e.Peak) || !Mathf.Approximately(k.cover, e.Cover) || !Mathf.Approximately(k.minPx, e.MinPx) || !Mathf.Approximately(k.minPxFar, e.MinPxFar) || !Mathf.Approximately(k.maxWorldRadius, e.MaxWorldRadius) || !Mathf.Approximately(k.farPeak, e.FarPeak))) Warn("AC-L2", e.Kind + ": the profile differs from farlight308.json (run sync, or copy the tuned values back into the data)");
                }
            }

            // ---- AC-L3: the card is depth tested, on the after-fog layer, outside every LODGroup (never distance-dropped)
            var lodRenderers = new HashSet<Renderer>();
            foreach (var g in PostLedger308.All<LODGroup>(scene)) foreach (var lod in g.GetLODs()) foreach (var r in lod.renderers) if (r != null) lodRenderers.Add(r);
            int badCard = 0;
            foreach (var card in cards)
            {
                var r = card.Card; string where = PostLedger308.PathOf(card.transform);
                var entry = profile != null ? profile.Find(card.Kind) : null;
                bool ok = r != null && r.gameObject == card.gameObject && card.gameObject.layer == layer && !lodRenderers.Contains(r) && entry != null && r.sharedMaterial == entry.Material
                    && r.shadowCastingMode == ShadowCastingMode.Off && !card.gameObject.isStatic && card.Profile == profile;
                if (!ok) { badCard++; sb.AppendLine("  FAIL AC-L3 card " + where + ": layer " + LayerMask.LayerToName(card.gameObject.layer) + ", in LODGroup " + (r != null && lodRenderers.Contains(r)) + ", material " + (r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "none")); }
            }
            Line(badCard == 0, "AC-L3", "cards on layer " + c.layer + ", outside every LODGroup, profile material, no shadows: " + (cards.Count - badCard) + "/" + cards.Count);
            Line(glowRenderers.Count == cards.Count && glowRenderers.All(r => r.GetComponent<AttractionLight308>() != null), "AC-L3", "renderers drawing " + c.shader + ": " + glowRenderers.Count + " (all carry AttractionLight308: " + glowRenderers.All(r => r.GetComponent<AttractionLight308>() != null) + ")");
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(EventWash308.RendererPath);
            if (data == null) Line(false, "AC-L3", "Renderer297 missing at " + EventWash308.RendererPath);
            else
            {
                var so = new SerializedObject(data); int bit = layer >= 0 ? 1 << layer : 0;
                bool outOfNormal = (so.FindProperty("m_TransparentLayerMask").FindPropertyRelative("m_Bits").intValue & bit) == 0;
                var names = data.rendererFeatures.Where(f => f != null).Select(f => f.name + (f.isActive ? "" : "(off)")).ToList();
                int after = names.IndexOf("VfxAfterFog300_Transparent"), mist = names.IndexOf("CompactMist238");
                Line(after >= 0 && outOfNormal, "AC-L3", "Renderer297 draws layer " + c.layer + " in VfxAfterFog300_Transparent only (feature " + (after >= 0 ? "present" : "ABSENT") + ", normal transparent mask excludes it: " + outOfNormal + ")");
                // review N7: the layer must reach the screen through one camera only (an overlay / second base camera with the layer
                // in its mask would draw every card twice). The player camera may be spawned at runtime: 0 here is not an error.
                var cams = PostLedger308.All<Camera>(scene).Where(x => x.cameraType == CameraType.Game && bit != 0 && (x.cullingMask & bit) != 0).ToList();
                string camText = "Game cameras in the scene whose culling mask draws layer " + c.layer + ": " + cams.Count + (cams.Count > 0 ? " (" + string.Join(", ", cams.Select(x => PostLedger308.PathOf(x.transform) + (x.isActiveAndEnabled ? "" : " [off]") + (x.targetTexture != null ? " [texture]" : ""))) + ")" : "");
                if (cams.Count(x => x.isActiveAndEnabled) > 1) Warn("AC-L3", camText + " — more than one active camera draws the cards (double draw, or a map texture would show them)"); else sb.AppendLine("  info AC-L3 " + camText);
                if (after >= 0 && mist >= 0 && after < mist) Warn("AC-L3", "feature order [" + string.Join(", ", names) + "]: the after-fog transparent pass is enqueued BEFORE CompactMist238 at the same event — the realm air of a far background lies over the card (dry run: fogWorst). Human check on the far stills; reordering Renderer297 is a shared-asset change (not done here)");
            }

            // ---- AC-L4: every card follows a lit lamp
            int badBind = 0;
            foreach (var card in cards)
            {
                var src = card.Source; bool ok = src != null && src.enabled && src.gameObject.activeInHierarchy && src.intensity > 0f && Vector3.Distance(src.transform.position, card.transform.position) <= 1.5f;
                if (!ok) { badBind++; sb.AppendLine("  FAIL AC-L4 card " + PostLedger308.PathOf(card.transform) + ": source " + (src == null ? "none" : PostLedger308.PathOf(src.transform) + " enabled " + src.enabled + " at " + F(Vector3.Distance(src.transform.position, card.transform.position)) + " m")); }
            }
            Line(badBind == 0, "AC-L4", "cards bound to a lit Light within 1.5 m: " + (cards.Count - badBind) + "/" + cards.Count);

            // ---- AC-L7: never on enemies / contamination / protected trees
            int forbidden = 0;
            foreach (var t in cards.Select(x => x.transform).Concat(papers).Concat(glowRenderers.Select(r => r.transform)))
                if (Forbidden(c, t, out string why)) { forbidden++; sb.AppendLine("  FAIL AC-L7 " + PostLedger308.PathOf(t) + ": " + why); }
            Line(forbidden == 0, "AC-L7", "objects outside the allowed paths (" + string.Join(", ", c.allowedPaths) + ", " + c.root + "/), on encounter actors (enemies / contamination) or under protected trees: " + forbidden);
            int kinds = cards.Count(x => x.Kind != AttractionLightProfileSO.Kind.InnLantern && x.Kind != AttractionLightProfileSO.Kind.ShrineCandle);
            Line(kinds == 0, "AC-L7", "cards of a kind outside 주막 등롱 / 성황당 촛불: " + kinds);

            // ---- AC-L8: the ledger describes the scene
            var ledger = File.Exists(LedgerFile(scenePath)) ? ReadLedger(scenePath) : null; int lost = 0;
            if (ledger != null) foreach (var m in ledger.made) if (FindMade(scene, c, m) == null) { lost++; sb.AppendLine("  FAIL AC-L8 ledger row not in the scene: " + m.what + " " + m.row + " " + m.path); }
            Line(lost == 0, "AC-L8", "ledger " + (ledger == null ? "none" : ledger.made.Count + " row(s)") + ", rows not found in the scene " + lost);

            // ---- AC-L5: physics ray per sightline [M] (instanced sheet trees / rocks have no collider in edit mode: the after-stills judge those)
            Physics.SyncTransforms();
            float lower = c.stillEyeHeight_m - c.eyeHeight_m;   // the stills' camera stands at stillEyeHeight_m; the player's eye is eyeHeight_m (review F2)
            sb.AppendLine("  sightlines [M physics ray, player eye (" + F(c.eyeHeight_m) + " m above the ground = still eye - " + F(lower) + " m) -> card anchor]:");
            foreach (var s in c.sightlines)
            {
                Vector3 anchor; string who = s.light;
                if (!TryAnchor(scene, c, s.light, out anchor) && !(s.alt.Length > 0 && TryAnchor(scene, c, who = s.alt, out anchor))) { sb.AppendLine("   " + s.id + " " + s.light + ": lamp not in the scene (" + s.priority + ")"); continue; }
                foreach (var (tag, eyeArr) in new[] { ("eye", s.eye), ("eyeAfter", s.eyeAfter) })
                {
                    if (eyeArr == null || eyeArr.Length < 3) continue;
                    var eye = V3(eyeArr) + Vector3.down * lower; float dist = Vector3.Distance(eye, anchor);
                    string hit = "clear";
                    if (Physics.Linecast(eye, anchor, out var h, ~0, QueryTriggerInteraction.Ignore) && h.distance < dist - .75f) hit = "blocked at " + F(h.distance, "F1") + " m by " + PostLedger308.PathOf(h.collider.transform);
                    sb.AppendLine("   " + s.id + " [" + s.priority + "] " + tag + " " + P(eye) + " -> " + who + " " + P(anchor) + " " + F(dist, "F1") + " m: " + hit);
                }
            }

            string file = Path.Combine(OutDir, "check_farlight308_" + PostLedger308.Short(scenePath) + ".txt");
            Directory.CreateDirectory(OutDir);
            sb.AppendLine("  result: FAIL " + fail + ", WARN " + warn + " -> " + file);
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            return sb.ToString();
        }

        static bool TryAnchor(Scene scene, Cfg c, string id, out Vector3 anchor)
        {
            anchor = default;
            var row = c.lights.FirstOrDefault(l => l.id == id);
            if (row != null)
            {
                var lamp = Resolve(scene, row.path, V3(row.at), c.tolerance_m); if (lamp == null) return false;
                var card = Child(lamp, c.glow.name); anchor = card != null ? card.position : Anchor(c, lamp, row.pair); return true;
            }
            var pole = c.poles.FirstOrDefault(p => p.id == id); if (pole == null) return false;
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == c.root); var holder = root != null ? Child(root.transform, pole.id) : null;
            if (holder == null) return false;
            var glow = holder.GetComponentInChildren<AttractionLight308>(true); if (glow == null) return false;
            anchor = glow.transform.position; return true;
        }

        // ------------------------------------------------------------------ captures

        static readonly string[] Fovs = { "n60", "z09" };
        static readonly string[] Tiers = { "PC", "Mobile" };

        static string ShotList()
        {
            var c = Load(); var sb = new StringBuilder();
            foreach (var pri in new[] { "primary", "secondary" })
                foreach (var s in c.sightlines.Where(x => x.priority == pri))
                    foreach (var tier in Tiers) foreach (var fov in Fovs)
                    {
                        sb.AppendLine("shot:" + s.id + ":" + fov + ":" + tier);
                        if (s.eyeAfter.Length >= 3) sb.AppendLine("shot:" + s.id + ":" + fov + ":" + tier + ":eye=after");
                    }
            return sb.ToString();
        }

        static string Shot(string[] parts)
        {
            if (parts.Length < 4) throw new PostLedger308.Refused("shot:<sightline>:<n60|z09>:<PC|Mobile>[:eye=after][:label=<text>]");
            // review N8a: a still switches the quality level; never during Play
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running): a still switches the quality level");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
            var c = Load(); var o = PostLedger308.Options(parts.Skip(4));
            var s = c.sightlines.FirstOrDefault(x => x.id == parts[1].Trim()) ?? throw new PostLedger308.Refused("no sightline " + parts[1]);
            string fovName = parts[2].Trim(), tier = parts[3].Trim();
            if (!Fovs.Contains(fovName) || !Tiers.Contains(tier)) throw new PostLedger308.Refused("shot:<sightline>:<n60|z09>:<PC|Mobile>");
            bool after = o.TryGetValue("eye", out var e) && e == "after";
            if (after && s.eyeAfter.Length < 3) throw new PostLedger308.Refused(s.id + " has no eyeAfter");
            var eye = V3(after ? s.eyeAfter : s.eye);
            Vector3 aim;
            if (s.aim.Length >= 3) aim = V3(s.aim);
            else if (!TryAnchor(SceneManager.GetActiveScene(), c, s.light, out aim) && !(s.alt.Length > 0 && TryAnchor(SceneManager.GetActiveScene(), c, s.alt, out aim)))
                throw new PostLedger308.Refused(s.id + ": no aim in the data and the lamp is not in the open scene");
            string label = o.TryGetValue("label", out var l) && l.Length > 0 ? l : "after";
            foreach (char ch in Path.GetInvalidFileNameChars()) if (label.IndexOf(ch) >= 0) throw new PostLedger308.Refused("label has a character a file name cannot hold");
            string name = "farlight308_" + s.id + (after ? "e" : "") + "_" + fovName + "_" + tier;
            string made = Presentation297.ShotTier308(name, eye, aim, fovName == "z09" ? 9f : 60f, 1600, 900, tier, true);
            string dir = Path.Combine(OutDir, "stills_" + label); Directory.CreateDirectory(dir);
            string to = Path.Combine(dir, name + ".png"); File.Copy(made, to, true);
            return to;
        }
    }
}
