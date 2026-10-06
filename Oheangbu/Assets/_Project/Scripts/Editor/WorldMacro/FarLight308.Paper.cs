using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 먼 불빛 — the lantern paper, in three variants the data switches (farlight308.json paper.variant; relayout fix 2, L5):
    //   P0  no paper: the KCISA lantern as the pack ships it (unlit body; the lamp's Point light is all there is)
    //   P1  the #308 sleeve: a built-in cube, widthScale x the WHOLE body box (caps and hanging rod included), InnLantern297
    //       (InkBeacon, HDR 1.8 > Bloom threshold) — reads as a flat saturated orange box standing out of the frame
    //   P2  한지: a built-in cylinder laid on the paper faces only, inside the four posts (the frame data = the lantern mesh as
    //       measured, mesh-local metres), with its own InkBeacon material: the rest-lantern colour mixed toward paper white, peak
    //       under the Bloom threshold. The body is ONE submesh with ONE material (paper and wood share a texture), so the paper
    //       cannot be lit through a material slot — a sleeve is the only way without a new mesh or shader.
    // The drum mesh is MEASURED, never assumed (relayout fix 2b): "Cylinder.fbx" is the legacy built-in pCylinder1 (fileID 10203,
    // radius 1), the primitive is "New-Cylinder.fbx" (fileID 10206, radius 0.5) — the first P2 build took the name on trust and
    // drew every tube twice too wide while the transform-only check passed. Now: MeasureDrum reads the named mesh's vertices
    // (a closed round drum of `sides` sides, or the row is refused), the sleeve is scaled from the measured radius / half height,
    // and CheckDrawn (AC-L14) measures what is DRAWN — the sleeve's vertices in the lantern body's own metres — for P2, P1 and P0.
    // The lantern mesh has no vertical falloff property to drive (InkBeacon: view-angle core + rim ink only): on a round drum that
    // gives a bright middle and inked edges; on P1's flat faces it gives nothing — that is why P1 reads flat.
    // Every number is data. This file holds property NAMES of the InkBeacon shader, no values.
    public static partial class FarLight308
    {
        internal const string P0 = "P0", P1 = "P1", P2 = "P2";
        const string ReShape = "re-shape", TakeAway = "take away";
        static readonly string[] Variants = { P0, P1, P2 };

        [Serializable] internal sealed class FrameCfg
        {
            public string source = "";
            public float[] meshSize_m = Array.Empty<float>(), paperBand_m = Array.Empty<float>();
            public float sizeTolerance_m, paperCornerRadius_m, paperApothem_m, ribOuterRadius_m;
            public int ribs;
        }
        [Serializable] internal sealed class HanjiCfg
        {
            public string material = "", mesh = "";
            public int sides;
            public float meshRadius, meshHalfHeight, meshTolerance, radius_m, bandInset_m, minClear_m, minRibShow_m, poseTolerance_m, drawnTolerance_m;
            public float[] baseColour = Array.Empty<float>(), paperWhite = Array.Empty<float>();
            public float mix, minSaturation, maxSaturation, intensity, chroma, coreLow, flicker, flickerSpeed, noiseScale, rimInk, rimPower;
            public bool normalizePeak;
        }
        [Serializable] internal sealed class PostCfg { public float exposureEV; }

        sealed class PaperCtx { public string variant; public Material box, hanji; public bool dry; public Drum drum; public string drumWhy; }
        /// <summary>The sleeve mesh as measured from its own vertices (mesh units).</summary>
        internal sealed class Drum { public Mesh mesh; public Vector3 centre; public float radius, halfHeight; public int rim, triangles; public string text = ""; }
        /// <summary>A sleeve as drawn, in the lantern body mesh's own metres (the frame the data's `frame` block is written in).</summary>
        sealed class Drawn { public float radius, halfX, halfZ, yMin, yMax, axisOff; public Vector3 lossy; public string mesh = ""; }
        sealed class PaperWant
        {
            public string variant, text; public bool box; public Mesh mesh; public Material material; public Matrix4x4 matrix;
            public Vector3 pos, scale; public float yaw;                       // P1
            public Transform frame; public Vector3 localPos, localScale;      // P2 (in the body mesh's own frame)
        }

        // ------------------------------------------------------------------ data

        /// <summary>The variant a call works in: the override of this call, else the data's, else P1 (a data file older than the variants).</summary>
        internal static string Variant(Cfg c, string over)
        {
            string v = !string.IsNullOrEmpty(over) ? over : string.IsNullOrEmpty(c.paper.variant) ? P1 : c.paper.variant;
            if (!Variants.Contains(v)) throw new PostLedger308.Refused("paper variant '" + v + "' is not one of " + string.Join(" | ", Variants));
            return v;
        }

        static string VariantArg(string[] parts)
        {
            var o = PostLedger308.Options(parts.Skip(2));
            if (!o.TryGetValue("variant", out var v) || v.Length == 0) return "";
            if (!Variants.Contains(v)) throw new PostLedger308.Refused("variant=" + v + " is not one of " + string.Join(" | ", Variants));
            return v;
        }

        internal static string HanjiPath(Cfg c) => string.IsNullOrEmpty(c.paper.hanji.material) ? "" : c.materialFolder.TrimEnd('/') + "/" + c.paper.hanji.material + ".mat";

        /// <summary>The material colour (as authored, sRGB): the rest-lantern colour mixed toward paper white.</summary>
        internal static Color HanjiColour(HanjiCfg h) =>
            Color.Lerp(new Color(h.baseColour[0], h.baseColour[1], h.baseColour[2], 1f), new Color(h.paperWhite[0], h.paperWhite[1], h.paperWhite[2], 1f), h.mix);

        /// <summary>What InkBeacon's LightColour() makes of it (linear): chroma push about the luminance, then the peak normalised to 1.</summary>
        internal static Vector3 HanjiHue(HanjiCfg h)
        {
            var s = HanjiColour(h);
            var c = new Vector3(Mathf.GammaToLinearSpace(s.r), Mathf.GammaToLinearSpace(s.g), Mathf.GammaToLinearSpace(s.b));
            float l = .2126f * c.x + .7152f * c.y + .0722f * c.z;
            c = Vector3.Max(Vector3.zero, new Vector3(l, l, l) + h.chroma * (c - new Vector3(l, l, l)));
            float max = Mathf.Max(c.x, Mathf.Max(c.y, c.z));
            return h.normalizePeak ? c / Mathf.Max(max, 1e-3f) : c;
        }

        /// <summary>1 - min / max of the emitted hue, display-referred (0 = white paper, 1 = a pure colour).</summary>
        internal static float HanjiSaturation(HanjiCfg h)
        {
            var c = HanjiHue(h);
            float r = Mathf.LinearToGammaSpace(c.x), g = Mathf.LinearToGammaSpace(c.y), b = Mathf.LinearToGammaSpace(c.z);
            float max = Mathf.Max(r, Mathf.Max(g, b)), min = Mathf.Min(r, Mathf.Min(g, b));
            return max <= 0f ? 0f : 1f - min / max;
        }

        /// <summary>The brightest channel the sleeve can reach, with the scene's post exposure laid over it (InkBeacon: hue x intensity
        /// x breath, breath <= 1 + flicker; the body term lerp(coreLow, 1, heat) is at most 1).</summary>
        internal static float HanjiPeak(Cfg c)
        {
            var h = c.paper.hanji; var hue = HanjiHue(h);
            return Mathf.Max(hue.x, Mathf.Max(hue.y, hue.z)) * h.intensity * (1f + h.flicker) * Mathf.Pow(2f, c.post.exposureEV);
        }

        static void ValidatePaper(Cfg c)
        {
            string variant = Variant(c, "");
            var h = c.paper.hanji; var f = c.paper.frame;
            if (string.IsNullOrEmpty(h.material))
            {
                if (variant == P2) throw new PostLedger308.Refused("paper.variant P2 needs the paper.hanji and paper.frame blocks");
                return;   // a data file without P2: P0 / P1 only
            }
            void Need(bool ok, string what) { if (!ok) throw new PostLedger308.Refused("paper (한지): " + what); }
            Need(!PostLedger308.IsProtectedPath(HanjiPath(c)), "protected material path " + HanjiPath(c));
            // review F8: SyncPaper writes this material's values - it must be the sleeve's OWN asset, never one another tool or lamp kind shares
            Need(!string.Equals(HanjiPath(c), c.paperMaterial, StringComparison.OrdinalIgnoreCase) && !c.kinds.Any(k => string.Equals(MaterialPath(c, k), HanjiPath(c), StringComparison.OrdinalIgnoreCase)), "hanji.material " + HanjiPath(c) + " is the P1 paper material or a far-glow kind material (the sleeve needs its own asset)");
            // relayout fix 2b: no name guard — a built-in mesh NAME says nothing about its size ("Cylinder.fbx" is the legacy radius-1
            // mesh). The mesh the name gives is measured where it is used (MeasureDrum); here only the numbers that measuring needs.
            Need(h.meshTolerance > 0f && h.drawnTolerance_m > 0f, "hanji needs meshTolerance > 0 (how far the measured mesh may be from sides / meshRadius / meshHalfHeight) and drawnTolerance_m > 0 (the drawn tube against 2 x radius_m) — a data file older than 308.farlight.4");
            Need(f.meshSize_m.Length == 3 && f.paperBand_m.Length == 2 && f.sizeTolerance_m > 0f, "frame needs meshSize_m[3], paperBand_m[2], sizeTolerance_m > 0");
            Need(f.paperBand_m[0] >= 0f && f.paperBand_m[1] > f.paperBand_m[0] && f.paperBand_m[1] <= f.meshSize_m[1], "frame.paperBand_m must lie inside the mesh height, bottom < top");
            Need(f.paperCornerRadius_m > 0f && f.paperApothem_m > 0f && f.paperApothem_m <= f.paperCornerRadius_m && f.ribOuterRadius_m > f.paperCornerRadius_m, "frame radii: 0 < paperApothem_m <= paperCornerRadius_m < ribOuterRadius_m");
            Need(!string.IsNullOrEmpty(h.mesh) && h.sides >= 8 && h.meshRadius > 0f && h.meshHalfHeight > 0f, "hanji needs mesh, sides >= 8, meshRadius, meshHalfHeight (the built-in mesh's own size)");
            Need(h.poseTolerance_m > 0f && h.minClear_m > 0f && h.minRibShow_m > 0f, "hanji needs poseTolerance_m, minClear_m, minRibShow_m > 0");
            Need(h.bandInset_m >= 0f && f.paperBand_m[1] - f.paperBand_m[0] - 2f * h.bandInset_m > 0f, "bandInset_m " + F(h.bandInset_m, "0.####") + " must be >= 0 and leave a band (the sleeve never leaves the paper band)");
            // a polygon of `sides` at an unknown turn: its flats come as near as radius x cos(pi / sides)
            float inner = h.radius_m * Mathf.Cos(Mathf.PI / h.sides);
            Need(inner - f.paperCornerRadius_m >= h.minClear_m - 1e-6f, "radius_m " + F(h.radius_m, "0.####") + " (flats at " + F(inner, "0.####") + ") clears the paper corners (" + F(f.paperCornerRadius_m, "0.####") + ") by less than minClear_m " + F(h.minClear_m, "0.####") + " — the sleeve would sink into the paper faces (hidden, or z-fighting)");
            Need(f.ribOuterRadius_m - h.radius_m >= h.minRibShow_m - 1e-6f, "radius_m " + F(h.radius_m, "0.####") + " leaves the posts (" + F(f.ribOuterRadius_m, "0.####") + ") less than minRibShow_m " + F(h.minRibShow_m, "0.####") + " proud — the frame would not show (a box again)");
            Need(h.baseColour.Length >= 3 && h.paperWhite.Length >= 3 && h.mix >= 0f && h.mix <= 1f, "hanji needs baseColour[3], paperWhite[3], 0 <= mix <= 1");
            Need(h.intensity > 0f && h.intensity <= 8f && h.chroma >= 1f && h.chroma <= 2f && h.coreLow >= 0f && h.coreLow <= 1f && h.flicker >= 0f && h.flicker <= .5f
                && h.flickerSpeed >= 0f && h.flickerSpeed <= 4f && h.rimInk >= 0f && h.rimInk <= 1f && h.rimPower >= .5f && h.rimPower <= 8f && h.noiseScale > 0f, "a hanji value is outside the InkBeacon property range");
            float sat = HanjiSaturation(h);
            Need(h.maxSaturation > h.minSaturation && sat >= h.minSaturation - 1e-4f && sat <= h.maxSaturation + 1e-4f, "saturation " + F(sat, "0.###") + " of the mixed colour is outside " + F(h.minSaturation) + " .. " + F(h.maxSaturation) + " (below = white paper like the walls, above = the orange box again): change mix");
            float peak = HanjiPeak(c);
            // ART-INK 발광 상한: same cap as the far glow card — the brightest the paper can get stays under the Bloom threshold
            Need(peak <= c.maxCentre + 1e-4f && c.maxCentre < c.bloomThreshold, "peak " + F(peak, "0.###") + " (intensity x (1 + flicker) x 2^exposureEV) exceeds maxCentre " + F(c.maxCentre) + " (Bloom threshold " + F(c.bloomThreshold) + ") — the paper would bloom and print white at a distance");
        }

        static string PaperStatus(Cfg c)
        {
            var h = c.paper.hanji; string v = Variant(c, "");
            if (string.IsNullOrEmpty(h.material)) return "  paper variant " + v + " (no hanji block: P0 / P1 only)";
            var m = AssetDatabase.LoadAssetAtPath<Material>(HanjiPath(c)); var col = HanjiColour(h);
            return "  paper variant " + v + "; 한지 material " + HanjiPath(c) + ": " + (m == null ? "absent (sync creates it)" : HanjiBaked(h, m) ? "present = data" : "present, DIFFERS from the data (sync)")
                + "; colour (" + F(col.r, "0.###") + ", " + F(col.g, "0.###") + ", " + F(col.b, "0.###") + ") saturation " + F(HanjiSaturation(h), "0.###") + ", peak " + F(HanjiPeak(c), "0.###") + " <= " + F(c.maxCentre) + " < Bloom " + F(c.bloomThreshold)
                + "\n  sleeve mesh [M]: " + DrumStatus(h);
        }

        static string DrumStatus(HanjiCfg h) { var drum = MeasureDrum(h, out string why); return drum != null ? drum.text : "REFUSED — " + why; }

        // ------------------------------------------------------------------ the drum mesh, measured (relayout fix 2b)

        /// <summary>The built-in mesh hanji.mesh names, measured from its vertices: null with a reason unless it is a closed round
        /// drum — every vertex on one of two cap levels, each either on the rim circle or on the axis, the rim on exactly `sides`
        /// angles, every welded edge shared by two triangles — whose radius and half height are the data's meshRadius /
        /// meshHalfHeight within meshTolerance. The sleeve is scaled from the MEASURED numbers (WantPaper, MakePole).</summary>
        internal static Drum MeasureDrum(HanjiCfg h, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(h.mesh)) { why = "the data names no hanji.mesh"; return null; }
            Mesh mesh = null;
            try { mesh = Resources.GetBuiltinResource<Mesh>(h.mesh); } catch (Exception) { mesh = null; }
            if (mesh == null) { why = "no built-in mesh is called '" + h.mesh + "' (the primitive cylinder is New-Cylinder.fbx)"; return null; }
            var data = new BuildingAudit308.MeshCache308().Get(mesh);
            if (data == null || data.v == null || data.v.Length == 0 || data.t == null || data.t.Length < 3) { why = "the vertices of '" + h.mesh + "' (" + mesh.name + ") cannot be read: it cannot be measured, so it is not used"; return null; }
            float tol = h.meshTolerance; var b = mesh.bounds; var c = b.center; float half = b.extents.y, radius = 0f;
            string head = "'" + h.mesh + "' (mesh " + mesh.name + ", " + data.v.Length + " vertices, " + data.t.Length / 3 + " triangles)";
            foreach (var v in data.v) radius = Mathf.Max(radius, new Vector2(v.x - c.x, v.z - c.z).magnitude);
            if (radius <= tol || half <= tol) { why = head + " is flat: radius " + F(radius, "0.####") + ", half height " + F(half, "0.####"); return null; }
            if (Mathf.Abs(c.x) > tol || Mathf.Abs(c.y) > tol || Mathf.Abs(c.z) > tol) { why = head + " is not centred on its origin (bounds centre " + P(c) + "): a sleeve scaled about the origin would sit off the lantern axis"; return null; }
            var angles = new List<float>();
            foreach (var v in data.v)
            {
                if (Mathf.Abs(Mathf.Abs(v.y - c.y) - half) > tol) { why = head + " is not a drum: a vertex stands at height " + F(v.y, "0.####") + ", between the two caps (+-" + F(half, "0.####") + ") — a capsule / sphere / bevelled shape"; return null; }
                float r = new Vector2(v.x - c.x, v.z - c.z).magnitude;
                if (r <= tol) continue;   // a cap centre
                if (r < radius - tol) { why = head + " is not a drum: a vertex at radius " + F(r, "0.####") + " is neither on the rim (" + F(radius, "0.####") + ") nor on the axis"; return null; }
                angles.Add(Mathf.Atan2(v.z - c.z, v.x - c.x));
            }
            angles.Sort(); int rim = 0; float step = tol / radius;   // rim vertices closer than the tolerance are one corner
            for (int i = 0; i < angles.Count; i++) if (i == 0 || angles[i] - angles[i - 1] > step) rim++;
            if (rim > 1 && angles[0] + 2f * Mathf.PI - angles[angles.Count - 1] <= step) rim--;
            if (rim != h.sides) { why = head + " has " + rim + " corners around its rim, the data says sides " + h.sides + " (a cube has 4): not the round drum the frame numbers were worked out for"; return null; }
            // closed: weld the vertices by place; every edge must then belong to exactly two triangles
            float q = 1f / Mathf.Max(tol, 1e-5f); var ids = new Dictionary<Vector3Int, int>(); var id = new int[data.v.Length];
            for (int i = 0; i < data.v.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(data.v[i].x * q), Mathf.RoundToInt(data.v[i].y * q), Mathf.RoundToInt(data.v[i].z * q));
                if (!ids.TryGetValue(k, out int n)) { n = ids.Count; ids[k] = n; }
                id[i] = n;
            }
            var edges = new Dictionary<long, int>(); int solid = 0;
            for (int i = 0; i + 2 < data.t.Length; i += 3)
            {
                int a0 = id[data.t[i]], a1 = id[data.t[i + 1]], a2 = id[data.t[i + 2]];
                if (a0 == a1 || a1 == a2 || a2 == a0) continue;
                solid++;
                AddEdge(edges, a0, a1); AddEdge(edges, a1, a2); AddEdge(edges, a2, a0);
            }
            int open = edges.Values.Count(x => x != 2);
            if (solid == 0 || open > 0) { why = head + " is not closed: " + open + " edge(s) do not join exactly two triangles (an open tube would show its inside)"; return null; }
            string measured = "radius " + F(radius, "0.####") + ", half height " + F(half, "0.####") + ", " + rim + " sides, closed";
            if (Mathf.Abs(radius - h.meshRadius) > tol || Mathf.Abs(half - h.meshHalfHeight) > tol)
            {
                why = head + " measures " + measured + "; the data describes meshRadius " + F(h.meshRadius, "0.####") + ", meshHalfHeight " + F(h.meshHalfHeight, "0.####") + " (tolerance " + F(tol, "0.####")
                    + ") — the name gives another mesh than the data means (Cylinder.fbx = the legacy pCylinder1 of radius 1; the primitive is New-Cylinder.fbx)";
                return null;
            }
            return new Drum { mesh = mesh, centre = c, radius = radius, halfHeight = half, rim = rim, triangles = solid, text = head + ": " + measured + " = the data (tolerance " + F(tol, "0.####") + ")" };
        }

        /// <summary>mesh[:<name>] — READ ONLY, no scene, no asset: what MeasureDrum makes of a built-in mesh name against the data's
        /// drum numbers (sides, meshRadius, meshHalfHeight, meshTolerance). No name = the data's own hanji.mesh. This is how the
        /// editor itself says [M] that "Cylinder.fbx" is the radius-1 mesh and "New-Cylinder.fbx" the radius-0.5 one (TE-L5d).</summary>
        static string MeshProbe(string name)
        {
            var c = Load(); var h = c.paper.hanji;
            if (string.IsNullOrEmpty(h.material)) return "refused: the data has no paper.hanji block (no drum numbers to measure a mesh against)";
            var probe = JsonUtility.FromJson<HanjiCfg>(JsonUtility.ToJson(h));
            if (!string.IsNullOrEmpty(name)) probe.mesh = name;
            var drum = MeasureDrum(probe, out string why);
            return "FarLight308 mesh '" + probe.mesh + "' (read only; data " + c.version + ": sides " + h.sides + ", meshRadius " + F(h.meshRadius, "0.####") + ", meshHalfHeight " + F(h.meshHalfHeight, "0.####") + ", meshTolerance " + F(h.meshTolerance, "0.####")
                + (probe.mesh == h.mesh ? "; this is the data's hanji.mesh" : "; the data's hanji.mesh is '" + h.mesh + "'") + ")\n  sleeve mesh [M vertices]: " + (drum != null ? drum.text : "REFUSED — " + why);
        }

        static void AddEdge(Dictionary<long, int> edges, int u, int w)
        {
            long key = u < w ? ((long)u << 32) | (uint)w : ((long)w << 32) | (uint)u;
            edges[key] = (edges.TryGetValue(key, out int have) ? have : 0) + 1;
        }

        static PaperCtx NewPaperCtx(Cfg c, string variant, Material box, bool dry)
        {
            var ctx = new PaperCtx { variant = variant, box = box, hanji = AssetDatabase.LoadAssetAtPath<Material>(HanjiPath(c)), dry = dry };
            if (!string.IsNullOrEmpty(c.paper.hanji.material)) ctx.drum = MeasureDrum(c.paper.hanji, out ctx.drumWhy);
            return ctx;
        }

        // ------------------------------------------------------------------ the 한지 material (sync)

        static readonly string[] HanjiProps = { "_Color", "_Intensity", "_NormalizePeak", "_Chroma", "_CoreLow", "_Flicker", "_FlickerSpeed", "_NoiseScale", "_RimInk", "_RimPower" };

        static void WriteHanji(HanjiCfg h, Material m)
        {
            m.SetColor("_Color", HanjiColour(h)); m.SetFloat("_Intensity", h.intensity); m.SetFloat("_NormalizePeak", h.normalizePeak ? 1f : 0f); m.SetFloat("_Chroma", h.chroma);
            m.SetFloat("_CoreLow", h.coreLow); m.SetFloat("_Flicker", h.flicker); m.SetFloat("_FlickerSpeed", h.flickerSpeed); m.SetFloat("_NoiseScale", h.noiseScale);
            m.SetFloat("_RimInk", h.rimInk); m.SetFloat("_RimPower", h.rimPower);
        }

        internal static bool HanjiBaked(HanjiCfg h, Material m)
        {
            if (m == null || HanjiProps.Any(p => !m.HasProperty(p))) return false;
            var a = m.GetColor("_Color"); var b = HanjiColour(h);
            return Mathf.Abs(a.r - b.r) < 1e-4f && Mathf.Abs(a.g - b.g) < 1e-4f && Mathf.Abs(a.b - b.b) < 1e-4f
                && Mathf.Approximately(m.GetFloat("_Intensity"), h.intensity) && Mathf.Approximately(m.GetFloat("_NormalizePeak"), h.normalizePeak ? 1f : 0f) && Mathf.Approximately(m.GetFloat("_Chroma"), h.chroma)
                && Mathf.Approximately(m.GetFloat("_CoreLow"), h.coreLow) && Mathf.Approximately(m.GetFloat("_Flicker"), h.flicker) && Mathf.Approximately(m.GetFloat("_FlickerSpeed"), h.flickerSpeed)
                && Mathf.Approximately(m.GetFloat("_NoiseScale"), h.noiseScale) && Mathf.Approximately(m.GetFloat("_RimInk"), h.rimInk) && Mathf.Approximately(m.GetFloat("_RimPower"), h.rimPower);
        }

        /// <summary>The 한지 material: a copy of the P1 paper material (same shader, same culling), its values from the data. Created
        /// here, listed in the asset ledger (assets:revert deletes it), saved alone. Made whatever the variant is, so that a
        /// comparison still of P2 never needs a data edit.</summary>
        static void SyncPaper(Cfg c, bool dry, AssetLedger assets, StringBuilder sb)
        {
            var h = c.paper.hanji; if (string.IsNullOrEmpty(h.material)) return;
            string path = HanjiPath(c); var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (dry) { sb.AppendLine("  material " + path + (m == null ? ": would be created" : HanjiBaked(h, m) ? ": present = data" : ": present (values re-baked from the data)")); return; }
            var src = AssetDatabase.LoadAssetAtPath<Material>(c.paperMaterial) ?? throw new PostLedger308.Refused("paper material missing: " + c.paperMaterial);
            if (HanjiProps.Any(p => !src.HasProperty(p))) throw new PostLedger308.Refused(c.paperMaterial + " (" + src.shader.name + ") lacks an InkBeacon property the 한지 material needs: " + string.Join(", ", HanjiProps.Where(p => !src.HasProperty(p))));
            if (m == null)
            {
                EnsureFolder(c.materialFolder.TrimEnd('/'));
                m = new Material(src) { name = h.material };
                WriteHanji(h, m);
                AssetDatabase.CreateAsset(m, path);
                if (!assets.created.Contains(path)) assets.created.Add(path);
                sb.AppendLine("  created " + path + " (from " + src.name + ", " + src.shader.name + ")");
                return;
            }
            if (EditorUtility.IsDirty(m)) throw new PostLedger308.Refused(path + " has unsaved in-memory changes (another session?)");
            string before = EditorJsonUtility.ToJson(m);
            if (m.shader != src.shader) m.shader = src.shader;
            WriteHanji(h, m);
            if (EditorJsonUtility.ToJson(m) != before) { EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); sb.AppendLine("  baked " + path + " (intensity " + F(h.intensity) + ", mix " + F(h.mix) + ", coreLow " + F(h.coreLow) + ", rim " + F(h.rimInk) + ")"); }
        }

        // ------------------------------------------------------------------ pose

        /// <summary>The body's biggest mesh (the same pick as Body()).</summary>
        static MeshFilter BodyMesh(Transform lamp, string bodyName)
        {
            var body = Child(lamp, bodyName); if (body == null) return null;
            MeshFilter best = null; float vol = 0f;
            foreach (var f in body.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                var s = Vector3.Scale(f.sharedMesh.bounds.size, Abs(f.transform.lossyScale)); float v = s.x * s.y * s.z;
                if (v > vol) { vol = v; best = f; }
            }
            return best;
        }

        static float Deviation(Matrix4x4 a, Matrix4x4 b)
        {
            float d = 0f;
            for (int r = 0; r < 3; r++) for (int col = 0; col < 4; col++) d = Mathf.Max(d, Mathf.Abs(a[r, col] - b[r, col]));
            return d;
        }

        /// <summary>The sleeve the variant asks for on this lamp; null with a reason when there is none to make (P0, or a refusal).</summary>
        static PaperWant WantPaper(Cfg c, PaperCtx ctx, Transform lamp, out string why)
        {
            why = null;
            if (ctx.variant == P0) { why = "variant P0 (no paper)"; return null; }
            if (ctx.variant == P1)
            {
                if (!Body(lamp, c.paper.body, out var centre, out float yaw, out var size)) { why = "no '" + c.paper.body + "' body under the lamp"; return null; }
                var pos = centre + Vector3.up * ((c.paper.centreFraction - .5f) * size.y);
                var scale = new Vector3(size.x * c.paper.widthScale, size.y * c.paper.heightFraction, size.z * c.paper.widthScale);
                return new PaperWant { variant = P1, box = true, mesh = Builtin("Cube.fbx"), material = ctx.box, pos = pos, yaw = yaw, scale = scale, matrix = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), scale),
                    text = "P1 box " + F(scale.x) + " x " + F(scale.y) + " x " + F(scale.z) + " m at " + P(pos) + " (" + (ctx.box != null ? ctx.box.name : "?") + ")" };
            }
            var h = c.paper.hanji; var f = c.paper.frame;
            if (string.IsNullOrEmpty(h.material)) { why = "the data has no paper.hanji block (P2 unavailable)"; return null; }
            var mf = BodyMesh(lamp, c.paper.body);
            if (mf == null) { why = "no '" + c.paper.body + "' body mesh under the lamp"; return null; }
            var b = mf.sharedMesh.bounds; var tr = mf.transform;
            // the frame numbers were measured on ONE mesh, in its own axes: any other mesh, or the same one lying down, is refused
            if (Mathf.Abs(b.size.x - f.meshSize_m[0]) > f.sizeTolerance_m || Mathf.Abs(b.size.y - f.meshSize_m[1]) > f.sizeTolerance_m || Mathf.Abs(b.size.z - f.meshSize_m[2]) > f.sizeTolerance_m)
            { why = "body mesh " + mf.sharedMesh.name + " is " + F(b.size.x, "0.###") + " x " + F(b.size.y, "0.###") + " x " + F(b.size.z, "0.###") + " m; the frame data describes " + F(f.meshSize_m[0], "0.###") + " x " + F(f.meshSize_m[1], "0.###") + " x " + F(f.meshSize_m[2], "0.###") + " (another lantern mesh: measure it, do not guess)"; return null; }
            if (Vector3.Dot(tr.up, Vector3.up) < .999f) { why = "the body mesh does not stand upright (its Y axis is the paper drum's axis)"; return null; }
            // a child of the lamp can only follow the mesh without shear when the lamp is evenly scaled across, or not turned against it
            var ls = Abs(lamp.lossyScale); float turn = Mathf.Abs(Mathf.DeltaAngle(lamp.eulerAngles.y, tr.eulerAngles.y)) % 90f; turn = Mathf.Min(turn, 90f - turn);
            if (Mathf.Abs(ls.x - ls.z) > 1e-3f * Mathf.Max(ls.x, ls.z) && turn > .1f) { why = "the lamp is scaled " + F(ls.x, "0.###") + " x " + F(ls.z, "0.###") + " across and the body is turned " + F(turn, "0.#") + " deg inside it: a child sleeve would shear"; return null; }
            // the sleeve mesh as MEASURED (relayout fix 2b): a name that gives no closed round drum of the data's shape is refused here
            var drum = ctx.drum;
            if (drum == null) { why = "sleeve mesh: " + (ctx.drumWhy ?? "not measured"); return null; }
            float lo = f.paperBand_m[0] + h.bandInset_m, hi = f.paperBand_m[1] - h.bandInset_m;
            var localScale = new Vector3(h.radius_m / drum.radius, (hi - lo) * .5f / drum.halfHeight, h.radius_m / drum.radius);
            var localPos = new Vector3(b.center.x, b.min.y + (lo + hi) * .5f, b.center.z) - Vector3.Scale(drum.centre, localScale);
            if (ctx.hanji == null && !ctx.dry) { why = "한지 material missing: " + HanjiPath(c) + " (sync creates it)"; return null; }
            var world = Vector3.Scale(new Vector3(2f * h.radius_m, hi - lo, 2f * h.radius_m), Abs(tr.lossyScale));
            return new PaperWant { variant = P2, mesh = drum.mesh, material = ctx.hanji, frame = tr, localPos = localPos, localScale = localScale, matrix = tr.localToWorldMatrix * Matrix4x4.TRS(localPos, Quaternion.identity, localScale),
                text = "P2 한지 drum d " + F(world.x, "0.###") + " x h " + F(world.y, "0.###") + " m at " + P(tr.TransformPoint(localPos)) + " (" + (ctx.hanji != null ? ctx.hanji.name : h.material + ", sync creates it") + "; mesh " + drum.mesh.name + " measured r " + F(drum.radius, "0.###") + "), posts " + F((f.ribOuterRadius_m - h.radius_m) * Abs(tr.lossyScale).x * 1000f, "0.#") + " mm proud" };
        }

        /// <summary>Is this sleeve the one the variant asks for? P1 = the #308 build (cube, material, place; its pose was never
        /// recorded apart from the object). P2 = mesh, material and the whole pose against the body mesh.</summary>
        static bool PaperMatches(Cfg c, Transform have, PaperWant want)
        {
            var mf = have.GetComponent<MeshFilter>(); var r = have.GetComponent<MeshRenderer>();
            if (mf == null || r == null || mf.sharedMesh != want.mesh || want.material == null || r.sharedMaterial != want.material) return false;
            float tol = Mathf.Max(c.paper.hanji.poseTolerance_m, 1e-4f);
            return want.box ? Vector3.Distance(have.position, want.pos) <= tol : Deviation(have.localToWorldMatrix, want.matrix) <= tol;
        }

        static string Describe(Transform sleeve)
        {
            var mf = sleeve.GetComponent<MeshFilter>(); var r = sleeve.GetComponent<MeshRenderer>();
            return (mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "no mesh") + ", " + (r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "no material");
        }

        static Transform BuildPaper(Cfg c, Transform lamp, PaperWant want)
        {
            if (want.material == null) throw new Exception("paper " + want.variant + ": no material (run sync)");
            if (want.box) return MakePaper(c, lamp, want.pos, want.yaw, want.scale, want.material);
            var go = new GameObject(c.paper.name) { layer = lamp.gameObject.layer }; var t = go.transform;
            // posed in the body mesh's own frame (the frame data is mesh-local), then handed to the lamp with the world pose kept
            t.SetParent(want.frame, false); t.localPosition = want.localPos; t.localRotation = Quaternion.identity; t.localScale = want.localScale;
            t.SetParent(lamp, true);
            float dev = Deviation(t.localToWorldMatrix, want.matrix);
            if (dev > Mathf.Max(c.paper.hanji.poseTolerance_m, 1e-4f)) { Object.DestroyImmediate(go); throw new Exception("paper P2 under " + PostLedger308.PathOf(lamp) + ": the sleeve came out " + F(dev, "0.#####") + " off its pose (sheared parent chain)"); }
            go.AddComponent<MeshFilter>().sharedMesh = want.mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = want.material; Quiet(r);
            return t;
        }

        // ------------------------------------------------------------------ plan (one lights[] row)

        /// <summary>Adds the paper op of one row, if any. Returns a refusal text, or null. `present` counts a sleeve already right.</summary>
        static string PlanPaper(Cfg c, PaperCtx ctx, string id, Transform lamp, List<Op> ops, ref int present)
        {
            var have = Child(lamp, c.paper.name);
            if (ctx.variant == P0)
            {
                if (have == null) return null;
                ops.Add(new Op { what = "paper", row = id, verb = TakeAway, text = "paper " + id + ": " + PostLedger308.PathOf(have) + " (" + Describe(have) + ") — variant P0",
                    run = (s, l, utc) => { Object.DestroyImmediate(have.gameObject); l.made.RemoveAll(m => m.what == "paper" && m.row == id); } });
                return null;
            }
            var want = WantPaper(c, ctx, lamp, out string why);
            if (want == null) return why;
            if (have == null)
            {
                ops.Add(new Op { what = "paper", row = id, text = "paper " + id + ": " + c.paper.name + " " + want.text, run = (s, l, utc) => l.made.Add(Record("paper", id, BuildPaper(c, lamp, want), utc)) });
                return null;
            }
            if (PaperMatches(c, have, want)) { present++; return null; }
            ops.Add(new Op { what = "paper", row = id, verb = ReShape, text = "paper " + id + ": (" + Describe(have) + ") -> " + want.text,
                run = (s, l, utc) =>
                {
                    Object.DestroyImmediate(have.gameObject); l.made.RemoveAll(m => m.what == "paper" && m.row == id);
                    l.made.Add(Record("paper", id, BuildPaper(c, lamp, want), utc));
                } });
            return null;
        }

        // ------------------------------------------------------------------ check (read only; called by Check)

        /// <summary>AC-L13: every paper row wears the data's variant (mesh, material, pose), the 한지 material equals the data, and
        /// its peak stays under the Bloom threshold the scene really has.</summary>
        static void CheckPapers(Cfg c, UnityEngine.SceneManagement.Scene scene, float sceneBloom, Action<bool, string, string> line, StringBuilder sb)
        {
            string variant = Variant(c, ""); var h = c.paper.hanji;
            var ctx = NewPaperCtx(c, variant, AssetDatabase.LoadAssetAtPath<Material>(c.paperMaterial), false);
            int rows = 0, bad = 0;
            foreach (var row in c.lights.Where(r => r.paper))
            {
                var lamp = Resolve(scene, row.path, V3(row.at), c.tolerance_m); if (lamp == null) continue;
                rows++; var have = Child(lamp, c.paper.name);
                if (variant == P0) { if (have != null) { bad++; sb.AppendLine("  FAIL AC-L13 " + row.id + ": a sleeve is there (" + Describe(have) + "), the data says P0"); } continue; }
                var want = WantPaper(c, ctx, lamp, out string why);
                if (want == null) { bad++; sb.AppendLine("  FAIL AC-L13 " + row.id + ": " + why); continue; }
                if (have == null) { bad++; sb.AppendLine("  FAIL AC-L13 " + row.id + ": no sleeve, the data says " + variant); continue; }
                if (!PaperMatches(c, have, want)) { bad++; sb.AppendLine("  FAIL AC-L13 " + row.id + ": the sleeve (" + Describe(have) + ") is not " + want.text + " — apply:<scene> re-shapes it"); }
            }
            line(bad == 0, "AC-L13", "paper rows wearing the data's variant " + variant + " (mesh, material, pose): " + (rows - bad) + "/" + rows);
            if (variant != P2 || string.IsNullOrEmpty(h.material)) return;
            line(HanjiBaked(h, ctx.hanji), "AC-L13", "한지 material " + HanjiPath(c) + (ctx.hanji == null ? " MISSING" : HanjiBaked(h, ctx.hanji) ? " = data" : " differs from the data (run sync)"));
            float peak = HanjiPeak(c), thr = float.IsInfinity(sceneBloom) ? c.bloomThreshold : sceneBloom;
            line(peak <= c.maxCentre + 1e-4f && peak < thr, "AC-L13", "한지 peak " + F(peak, "0.###") + " (intensity " + F(h.intensity) + " x (1 + flicker " + F(h.flicker) + ") x 2^" + F(c.post.exposureEV) + " EV) <= " + F(c.maxCentre) + " < Bloom " + F(thr, "0.###") + (float.IsInfinity(sceneBloom) ? " (data)" : " [M]")
                + "; saturation " + F(HanjiSaturation(h), "0.###") + " in " + F(h.minSaturation) + " .. " + F(h.maxSaturation));
        }

        // ------------------------------------------------------------------ what is DRAWN, measured (AC-L14; relayout fix 2b)

        /// <summary>The sleeve as the renderer draws it: its mesh vertices (the bounds corners when they cannot be read) carried into
        /// the lantern body mesh's own frame. Null with a reason when nothing is drawn.</summary>
        static Drawn MeasureDrawn(BuildingAudit308.MeshCache308 cache, Transform sleeve, MeshFilter body, out string why)
        {
            why = null; var mf = sleeve.GetComponent<MeshFilter>(); var r = sleeve.GetComponent<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null || r == null) { why = "the sleeve has no mesh / renderer"; return null; }
            if (!r.enabled || !sleeve.gameObject.activeInHierarchy) { why = "the sleeve is not drawn (renderer off or object inactive)"; return null; }
            var data = cache.Get(mf.sharedMesh); Vector3[] pts;
            if (data != null && data.v != null && data.v.Length > 0) pts = data.v;
            else
            {
                var mb = mf.sharedMesh.bounds; pts = new Vector3[8];
                for (int i = 0; i < 8; i++) pts[i] = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
            }
            // sleeve mesh -> body mesh frame through their common parent (the lamp): local numbers only. World matrices at x ~ 3000 m
            // would lose about half a millimetre in single precision; they are the fall-back when the two do not share the lamp.
            var bb = body.sharedMesh.bounds; var lamp = sleeve.parent;
            var m = lamp != null && body.transform.IsChildOf(lamp) ? UpTo(body.transform, lamp).inverse * UpTo(sleeve, lamp) : body.transform.worldToLocalMatrix * sleeve.localToWorldMatrix;
            var d = new Drawn { yMin = float.PositiveInfinity, yMax = float.NegativeInfinity, mesh = mf.sharedMesh.name, lossy = Abs(body.transform.lossyScale) };
            float x0 = float.PositiveInfinity, x1 = float.NegativeInfinity, z0 = float.PositiveInfinity, z1 = float.NegativeInfinity;
            foreach (var v in pts)
            {
                var p = m.MultiplyPoint3x4(v);
                d.radius = Mathf.Max(d.radius, new Vector2(p.x - bb.center.x, p.z - bb.center.z).magnitude);
                x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z);
                d.yMin = Mathf.Min(d.yMin, p.y - bb.min.y); d.yMax = Mathf.Max(d.yMax, p.y - bb.min.y);
            }
            d.halfX = (x1 - x0) * .5f; d.halfZ = (z1 - z0) * .5f;
            d.axisOff = new Vector2((x0 + x1) * .5f - bb.center.x, (z0 + z1) * .5f - bb.center.z).magnitude;
            return d;
        }

        /// <summary>The matrix that carries t's local points into `ancestor`'s frame (the product of the local TRS on the way up).</summary>
        static Matrix4x4 UpTo(Transform t, Transform ancestor)
        {
            var m = Matrix4x4.identity;
            for (var p = t; p != null && p != ancestor; p = p.parent) m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
            return m;
        }

        /// <summary>AC-L14: the three variants judged alike, by what is drawn (lantern-local metres = the body mesh's frame).
        /// P2: the tube's diameter is 2 x radius_m, it stays inside the four posts and outside the paper faces by the data's
        /// clearances, its top and bottom lie inside the paper band, its axis is the lantern's. P1: the box is the data's law
        /// (widthScale / heightFraction / centreFraction of the body box); how far it stands outside the frame is reported, not
        /// failed — that is the look P1 is known for. P0: nothing is drawn.</summary>
        static void CheckDrawn(Cfg c, UnityEngine.SceneManagement.Scene scene, Action<bool, string, string> line, StringBuilder sb)
        {
            string variant = Variant(c, ""); var h = c.paper.hanji; var f = c.paper.frame; var cache = new BuildingAudit308.MeshCache308();
            bool framed = f.meshSize_m.Length == 3 && f.paperBand_m.Length == 2;
            float tol = h.drawnTolerance_m > 0f ? h.drawnTolerance_m : Mathf.Max(h.poseTolerance_m, 1e-4f);
            if (variant == P2) { var drum = MeasureDrum(h, out string no); line(drum != null, "AC-L14", "sleeve mesh [M vertices] " + (drum != null ? drum.text : "REFUSED — " + no)); }
            int rows = 0, bad = 0; string sample = null;
            foreach (var row in c.lights.Where(r => r.paper))
            {
                var lamp = Resolve(scene, row.path, V3(row.at), c.tolerance_m); if (lamp == null) continue;
                rows++; var sleeve = Child(lamp, c.paper.name);
                if (variant == P0) { if (sleeve != null) { bad++; sb.AppendLine("  FAIL AC-L14 " + row.id + ": a sleeve is drawn (" + Describe(sleeve) + "), the data says P0"); } continue; }
                var body = BodyMesh(lamp, c.paper.body);
                if (sleeve == null || body == null) { bad++; sb.AppendLine("  FAIL AC-L14 " + row.id + ": " + (sleeve == null ? "no sleeve to measure" : "no '" + c.paper.body + "' body mesh to measure against")); continue; }
                var d = MeasureDrawn(cache, sleeve, body, out string notDrawn);
                if (d == null) { bad++; sb.AppendLine("  FAIL AC-L14 " + row.id + ": " + notDrawn); continue; }
                var bb = body.sharedMesh.bounds; var why = new List<string>(); string text;
                if (variant == P2)
                {
                    if (!framed) { bad++; sb.AppendLine("  FAIL AC-L14 " + row.id + ": the data has no paper.frame block to measure the tube against"); continue; }
                    float lo = f.paperBand_m[0] + h.bandInset_m, hi = f.paperBand_m[1] - h.bandInset_m;
                    float proud = f.ribOuterRadius_m - d.radius, clear = d.radius * Mathf.Cos(Mathf.PI / Mathf.Max(h.sides, 3)) - f.paperCornerRadius_m;
                    if (Mathf.Abs(2f * d.radius - 2f * h.radius_m) > tol) why.Add("diameter " + F(2f * d.radius, "0.####") + " is not 2 x radius_m = " + F(2f * h.radius_m, "0.####") + " (+-" + F(tol, "0.####") + "): x " + F(d.radius / h.radius_m, "0.##") + " — a mesh of another radius scaled as if it were the data's?");
                    if (proud < h.minRibShow_m - 1e-5f) why.Add("the tube is " + (proud < 0f ? F(-proud * 1000f, "0.#") + " mm WIDER than the four posts" : "only " + F(proud * 1000f, "0.#") + " mm inside the four posts") + " (posts at " + F(f.ribOuterRadius_m, "0.####") + ", need " + F(h.minRibShow_m * 1000f, "0.#") + " mm): the frame does not show");
                    if (clear < h.minClear_m - 1e-5f) why.Add("the tube's flats clear the paper corners by " + F(clear * 1000f, "0.#") + " mm (need " + F(h.minClear_m * 1000f, "0.#") + "): sunk into the paper");
                    if (d.yMin < f.paperBand_m[0] - tol || d.yMax > f.paperBand_m[1] + tol) why.Add("the tube runs " + F(d.yMin, "0.####") + " .. " + F(d.yMax, "0.####") + ", outside the paper band " + F(f.paperBand_m[0], "0.####") + " .. " + F(f.paperBand_m[1], "0.####") + " (over the plates)");
                    else if (Mathf.Abs(d.yMin - lo) > tol || Mathf.Abs(d.yMax - hi) > tol) why.Add("the tube runs " + F(d.yMin, "0.####") + " .. " + F(d.yMax, "0.####") + ", the data says " + F(lo, "0.####") + " .. " + F(hi, "0.####"));
                    if (d.axisOff > tol) why.Add("the tube's axis is " + F(d.axisOff * 1000f, "0.#") + " mm off the lantern's");
                    text = "d " + F(2f * d.radius, "0.####") + " (world " + F(2f * d.radius * d.lossy.x, "0.###") + " m), posts " + F(proud * 1000f, "0.#") + " mm proud, paper corners cleared " + F(clear * 1000f, "0.#") + " mm, band " + F(d.yMin, "0.####") + " .. " + F(d.yMax, "0.####") + ", mesh " + d.mesh;
                }
                else
                {
                    // P1: the #308 box law, in the same frame (the box may be turned a quarter: x / z are compared as a pair)
                    float wx = c.paper.widthScale * bb.size.x * .5f, wz = c.paper.widthScale * bb.size.z * .5f, wh = c.paper.heightFraction * bb.size.y, wc = c.paper.centreFraction * bb.size.y;
                    bool across = Mathf.Abs(d.halfX - wx) <= tol && Mathf.Abs(d.halfZ - wz) <= tol || Mathf.Abs(d.halfX - wz) <= tol && Mathf.Abs(d.halfZ - wx) <= tol;
                    if (!across) why.Add("the box is " + F(2f * d.halfX, "0.####") + " x " + F(2f * d.halfZ, "0.####") + " across, the data's law gives " + F(2f * wx, "0.####") + " x " + F(2f * wz, "0.####") + " (widthScale " + F(c.paper.widthScale) + " x the body box)");
                    if (Mathf.Abs(d.yMax - d.yMin - wh) > tol || Mathf.Abs((d.yMin + d.yMax) * .5f - wc) > tol) why.Add("the box runs " + F(d.yMin, "0.####") + " .. " + F(d.yMax, "0.####") + ", the data's law gives " + F(wc - wh * .5f, "0.####") + " .. " + F(wc + wh * .5f, "0.####"));
                    text = "box " + F(2f * d.halfX, "0.####") + " x " + F(d.yMax - d.yMin, "0.####") + " x " + F(2f * d.halfZ, "0.####") + ", band " + F(d.yMin, "0.####") + " .. " + F(d.yMax, "0.####")
                        + (framed ? "; against the frame [info — the look P1 is known for]: corners " + F((d.radius - f.ribOuterRadius_m) * 1000f, "0.#") + " mm outside the posts, " + F((d.yMax - f.paperBand_m[1]) * 1000f, "0.#") + " mm above the paper band" : "");
                }
                if (why.Count > 0) { bad++; sb.AppendLine("  FAIL AC-L14 " + row.id + ": " + string.Join("; ", why)); }
                else if (sample == null) sample = row.id + " " + text;
            }
            line(bad == 0, "AC-L14", "drawn sleeves measured, lantern-local m [M mesh vertices in the body mesh's frame], variant " + variant + ": " + (rows - bad) + "/" + rows
                + (variant == P0 ? " lanterns with nothing drawn" : sample != null ? " — e.g. " + sample : ""));
            // review F7 / TE-L5e: only lights[] rows are measured above. A pole (its drum, its lantern's sleeve) is built from the same
            // measured mesh but nothing measures it as drawn yet — not measured is not passed, so an enabled pole fails here.
            int polesOn = c.poles.Count(p => p.enabled);
            if (polesOn > 0) line(false, "AC-L14", polesOn + " pole(s) enabled: the pole drum and the pole lantern's sleeve are NOT measured as drawn yet (TE-L5e) — extend CheckDrawn before a pole is switched on");
        }
    }
}
