using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Dress308 = Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-1 TRACK veg — vegetation exclusion pass over the CompactRebuildArtRenderer placement sheets
    // (DEFECTS_308 식생 침범 39장: 292_tree / 292_forest / 292_talus / 293s_* / v245_tree in the shared DryLandscape sheet,
    // plus Forest305 / Banks). Own entry point (BuildingFix308.cs Run() is not touched):
    //   queue … Oheangbu.EditorTools.WorldMacro.BuildingFix308 Veg "<command>"
    //   status                              config, sheet SHAs, plan and ledger state (read only)
    //   plan[:scene=<alias>]                dry run. Opens the scene (default veg308.json planScene = main), builds the footprints from the
    //                                       LOD0 renderers + colliders of the configured roots, tests every Tree/Rock placement near them
    //                                       (trunk inside footprint + class buffer / crown hull crossing a face / enclosure / floating base),
    //                                       finds the nearest valid spot outside (ground, slope, water, spacing, corridors, preserved
    //                                       areas) or plans a removal -> Fix/veg-plan.json + veg-plan.csv. Nothing is changed.
    //   guard[:scene=<alias>][:pending]     read only: the placements that still intrude in that scene (pending = with the plan laid over
    //                                       the sheets, to check the plan in the other two scenes before apply) -> Fix/veg-guard-<alias>.json
    //   apply                               applies Fix/veg-plan.json to the sheets (SHA must equal the plan's): backup of each sheet,
    //                                       moves / re-seats / removals, the "b308veg/" PreservedAreas mask on the area sheet,
    //                                       SaveAssetIfDirty on those sheets only, Fix/veg-ledger.json + Fix/veg-mask.json. A second apply = no change.
    //   replay[:reverted]                   after a re-bake brought placements back: re-applies the decisions stored in Fix/veg-mask.json
    //                                       by Id + original position (no scene needed); then run guard for anything new.
    //                                       :reverted = the decisions of the newest reverted op (Fix/veg-mask-reverted.json): revert -> re-bake -> replay:reverted
    //   revert                              newest applied op back, row by row (moved rows to their old position, removed rows re-inserted at
    //                                       their old index, mask areas back to the previous set); composes with F2 sheet-apply in any order.
    // Every number comes from BuildingAudit308/veg308.json (TEST). Protected sheets and trees are never written; no scene is saved;
    // no dialog; AssetDatabase.SaveAssets is never called. Runtime (App) code is untouched: the renderer reads FixedPlacements only,
    // the mask areas carry the Tree|Rock kind bits only (grass bakes skip them).
    public static partial class BuildingFix308
    {
        static string VegConfigFile => Path.Combine(BuildingAudit308.Folder, "veg308.json");
        static string VegPlanFile => Path.Combine(FixDir, "veg-plan.json");
        static string VegPlanCsv => Path.Combine(FixDir, "veg-plan.csv");
        static string VegLedgerFile => Path.Combine(FixDir, "veg-ledger.json");
        static string VegMaskFile => Path.Combine(FixDir, "veg-mask.json");
        static string VegMaskRevertedFile => Path.Combine(FixDir, "veg-mask-reverted.json");

        // ------------------------------------------------------------------ config (BuildingAudit308/veg308.json)

        [Serializable] internal sealed class VegRoot308 { public string name = "", units = "buildings"; }   // units: all | buildings | none (extraUnits only)
        [Serializable] internal sealed class VegClass308 { public string name = ""; public string[] tokens = Array.Empty<string>(); public float buffer, rockBuffer; }
        [Serializable] internal sealed class VegMove308
        {
            public float minMove, maxMove, step, margin, maxSlope, treeSpacing, rockSpacing, corridorClearance, passageClearance, groundWindow, obstacleHeight, waterMargin, skyClear;
            public int directions; public string[] groundTokens = Array.Empty<string>(); public string sectionPrefix = "", sectionRoot = "";
        }
        [Serializable] internal sealed class VegMaskCfg308
        {
            public string areaPrefix = "", areaSheetToken = ""; public bool writePreservedAreas; public int affectedKinds, maxAreasPerUnit, maxAreasHard;
            public float areaCell, yBelow, yAbove;
        }
        [Serializable] internal sealed class VegNamed308 { public string id = "", building = "", action = "auto"; public string[] defect = Array.Empty<string>(); public float minMove; }
        [Serializable] internal sealed class VegUnnamed308 { public string[] defect = Array.Empty<string>(); public float[] near = Array.Empty<float>(); public float radius; public string building = "", note = ""; }
        // limits: the faces of `unit` flag an un-named placement only within `radius` (XZ) of `near` (a footprint that must not sweep a kept grove)
        [Serializable] internal sealed class VegLimit308 { public string unit = ""; public float[] near = Array.Empty<float>(); public float radius; }
        [Serializable] internal sealed class VegCfg308
        {
            // avoidRoots: read as footprints for move targets only (nothing standing near them is flagged, no mask area)
            public string[] avoidRoots = Array.Empty<string>(); public VegLimit308[] limits = Array.Empty<VegLimit308>();
            // removeUnnamedWhenNoSpot: a placement DEFECTS_308 does not list is never removed unless this is on (it stays as "review");
            // floatNamedOnly: the floating-base test runs for the named placements only (the #307 floating pass owns the rest)
            public bool removeUnnamedWhenNoSpot, floatNamedOnly = true;
            public string version = "", status = "", planScene = "";
            public string[] sheetTokens = Array.Empty<string>(), kinds = Array.Empty<string>(), keepTokens = Array.Empty<string>(), keepIds = Array.Empty<string>(), removeIds = Array.Empty<string>();
            public VegRoot308[] roots = Array.Empty<VegRoot308>(); public string[] extraUnits = Array.Empty<string>(), excludeUnits = Array.Empty<string>(), enclosures = Array.Empty<string>(), skipRendererTokens = Array.Empty<string>();
            public VegClass308[] classes = Array.Empty<VegClass308>();
            public float minUnitSize, enclosureMaxArea; public bool includeColliders, removeWhenNoSpot;
            public float minTrunkRadius, belowBand, ringStep, ringArc, crownFrom, crownDepth, crownSyntheticShare, floatGap, reseatSink, rockSink, positionTolerance;
            public int crownSectors, crownBands, crownMinSamples, maxRows;
            public VegMove308 move = new VegMove308(); public VegMaskCfg308 mask = new VegMaskCfg308();
            public VegNamed308[] named = Array.Empty<VegNamed308>(); public VegUnnamed308[] unnamed = Array.Empty<VegUnnamed308>();
        }

        static VegCfg308 VegLoadConfig(out string error)
        {
            error = null;
            if (!File.Exists(VegConfigFile)) { error = "config missing: " + VegConfigFile; return null; }
            try
            {
                var c = JsonUtility.FromJson<VegCfg308>(File.ReadAllText(VegConfigFile).TrimStart('﻿'));
                if (c == null || c.roots.Length == 0 || c.classes.Length == 0 || c.sheetTokens.Length == 0) { error = "config incomplete (roots/classes/sheetTokens): " + VegConfigFile; return null; }
                if (c.ringStep <= 0 || c.ringArc <= 0 || c.move.step <= 0 || c.move.directions < 4 || c.move.maxMove <= 0 || c.crownSectors < 4 || c.crownBands < 1 || c.positionTolerance <= 0)
                { error = "config has a non-positive step (ringStep/ringArc/move.step/move.directions/move.maxMove/crownSectors/crownBands/positionTolerance): " + VegConfigFile; return null; }
                return c;
            }
            catch (Exception e) { error = "config unreadable: " + e.Message; return null; }
        }

        // ------------------------------------------------------------------ plan / ledger / mask shapes

        [Serializable] internal sealed class VegRow308
        {
            public string sheet = "", id = "", proto = "", kind = "", action = "", reason = "", unit = "", cls = "", defect = "", note = "", state = "", placementJson = "";
            public int index, crownHits; public Vector3 from, to; public float moved, footDistance = -1, faceAbove, crownDepth, groundGap, slope;
        }
        [Serializable] internal sealed class VegArea308 { public string id = "", unit = "", cls = ""; public Vector3 centre; public Vector2 half; public float yaw, minY, maxY, treePad, rockPad; }
        [Serializable] internal sealed class VegSheetInfo308 { public string path = "", sha = ""; public int placements, candidates, rows; }
        [Serializable] internal sealed class VegCover308 { public string id = "", defect = "", building = "", status = "", action = "", note = ""; }
        [Serializable] internal sealed class VegPlan308
        {
            public string utc = "", alias = "", scene = "", sceneSha = "", quality = "", configVersion = "", areaSheet = "";
            public int units, triangles, near, move, remove, reseat, review; public float seconds; public bool blocked;
            public List<VegSheetInfo308> sheets = new List<VegSheetInfo308>(); public List<VegRow308> rows = new List<VegRow308>();
            public List<VegArea308> areas = new List<VegArea308>(); public List<VegCover308> named = new List<VegCover308>(); public List<string> notes = new List<string>();
        }
        [Serializable] internal sealed class VegSheetOp308
        {
            public string path = "", shaBefore = "", shaAfter = "", backup = ""; public int moved, removed, reseated, already, stale, areasBefore, areasAfter;
            public List<VegRow308> rows = new List<VegRow308>(); public List<string> areasBeforeJson = new List<string>();
        }
        [Serializable] internal sealed class VegOp308
        {
            public string utc = "", command = "", status = "", detail = "", planUtc = "", revertedUtc = ""; public bool reverted;
            public List<VegSheetOp308> sheets = new List<VegSheetOp308>();
        }
        [Serializable] internal sealed class VegLedger308 { public List<VegOp308> ops = new List<VegOp308>(); }
        [Serializable] internal sealed class VegMask308
        {
            public string version = "", utc = "", scene = "", areaSheet = "", note = "";
            public List<VegArea308> areas = new List<VegArea308>(); public List<VegRow308> decisions = new List<VegRow308>();
        }
        [Serializable] internal sealed class VegGuard308
        {
            public string utc = "", alias = "", scene = "", sceneSha = "", quality = "", verdict = ""; public bool pending; public int near, violations;
            public List<VegRow308> rows = new List<VegRow308>(); public List<string> notes = new List<string>();
        }

        static VegLedger308 VegLoadLedger() => File.Exists(VegLedgerFile) ? JsonUtility.FromJson<VegLedger308>(File.ReadAllText(VegLedgerFile)) ?? new VegLedger308() : new VegLedger308();
        static void VegWrite(string file, object doc) { Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllText(file, JsonUtility.ToJson(doc, true), new UTF8Encoding(false)); }

        // ------------------------------------------------------------------ entry

        public static string Veg(string command)
        {
            command = (command ?? "").Trim();
            try
            {
                var a = command.Split(':'); var opt = PostLedger308.Options(a.Skip(1));
                switch (a[0])
                {
                    case "status": return VegStatus();
                    case "plan": return VegPlan(opt);
                    case "guard": return VegGuard(opt);
                    case "apply": return VegApply(false, false);
                    case "replay": return VegApply(true, opt.ContainsKey("reverted"));
                    case "revert": return VegRevert();
                    default: return "refused: BuildingFix308.Veg status | plan[:scene=<alias>] | guard[:scene=<alias>][:pending] | apply | replay[:reverted] | revert";
                }
            }
            catch (Exception e) { return "FAILED: " + e; }
        }

        // ------------------------------------------------------------------ geometry: triangle soup of the footprint sources

        sealed class VegSoup308
        {
            readonly List<Vector3> a = new List<Vector3>(), b = new List<Vector3>(), c = new List<Vector3>(); readonly List<int> unit = new List<int>();
            Vector3[] A, B, C; int[] U; float[] lowY, highY; int[] stamp; int tick; Dictionary<long, List<int>> grid; const float Cell = 2f;
            // skip[unit] = a target-only (avoid) unit: its faces are ignored while honourSkip is on (the test of a standing placement)
            public bool[] skip; public bool honourSkip;
            bool Skipped(int i) => honourSkip && skip != null && U[i] < skip.Length && skip[U[i]];
            public int Count => a.Count;
            static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            public void Add(Vector3 p, Vector3 q, Vector3 r, int u) { a.Add(p); b.Add(q); c.Add(r); unit.Add(u); grid = null; }
            public Vector3 VA(int i) => a[i]; public Vector3 VB(int i) => b[i]; public Vector3 VC(int i) => c[i]; public int UnitOf(int i) => unit[i];
            void Build()
            {
                A = a.ToArray(); B = b.ToArray(); C = c.ToArray(); U = unit.ToArray(); int n = A.Length;
                lowY = new float[n]; highY = new float[n]; stamp = new int[n]; tick = 0; grid = new Dictionary<long, List<int>>();
                for (int i = 0; i < n; i++)
                {
                    lowY[i] = Mathf.Min(A[i].y, Mathf.Min(B[i].y, C[i].y)); highY[i] = Mathf.Max(A[i].y, Mathf.Max(B[i].y, C[i].y));
                    float x0 = Mathf.Min(A[i].x, Mathf.Min(B[i].x, C[i].x)), x1 = Mathf.Max(A[i].x, Mathf.Max(B[i].x, C[i].x));
                    float z0 = Mathf.Min(A[i].z, Mathf.Min(B[i].z, C[i].z)), z1 = Mathf.Max(A[i].z, Mathf.Max(B[i].z, C[i].z));
                    if (float.IsNaN(x0) || float.IsNaN(z0) || x1 - x0 > 4000f || z1 - z0 > 4000f) continue;   // broken triangle: never indexed
                    for (int gx = Mathf.FloorToInt(x0 / Cell); gx <= Mathf.FloorToInt(x1 / Cell); gx++)
                        for (int gz = Mathf.FloorToInt(z0 / Cell); gz <= Mathf.FloorToInt(z1 / Cell); gz++)
                        {
                            long k = Key(gx, gz);
                            if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                            l.Add(i);
                        }
                }
            }
            // lowest face the vertical line at (x, z) meets with y in [y0, y1]
            public bool Vertical(float x, float z, float y0, float y1, out int u, out float y)
            {
                u = -1; y = float.PositiveInfinity;
                if (grid == null) Build();
                if (!grid.TryGetValue(Key(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell)), out var list)) return false;
                foreach (int i in list)
                {
                    if (highY[i] < y0 || lowY[i] > y1 || Skipped(i)) continue;
                    if (BuildingAudit308.VerticalHit(A[i], B[i], C[i], x, z, out float h) && h >= y0 && h <= y1 && h < y) { y = h; u = U[i]; }
                }
                return u >= 0;
            }
            // nearest crossing of the segment p -> q (t in 0..1)
            public bool Segment(Vector3 p, Vector3 q, out int u, out float t)
            {
                u = -1; t = 1f;
                if (grid == null) Build();
                var d = q - p; if (d.sqrMagnitude < 1e-10f) return false;
                float ylo = Mathf.Min(p.y, q.y), yhi = Mathf.Max(p.y, q.y); tick++;
                int gx0 = Mathf.FloorToInt(Mathf.Min(p.x, q.x) / Cell), gx1 = Mathf.FloorToInt(Mathf.Max(p.x, q.x) / Cell);
                int gz0 = Mathf.FloorToInt(Mathf.Min(p.z, q.z) / Cell), gz1 = Mathf.FloorToInt(Mathf.Max(p.z, q.z) / Cell);
                for (int gx = gx0; gx <= gx1; gx++)
                    for (int gz = gz0; gz <= gz1; gz++)
                    {
                        if (!grid.TryGetValue(Key(gx, gz), out var list)) continue;
                        foreach (int i in list)
                        {
                            if (stamp[i] == tick) continue; stamp[i] = tick;
                            if (highY[i] < ylo || lowY[i] > yhi || Skipped(i)) continue;
                            if (BuildingAudit308.RayTri(p, d, A[i], B[i], C[i], out float h) && h > 1e-4f && h < t) { t = h; u = U[i]; }
                        }
                    }
                return u >= 0;
            }
        }

        // XZ box (centre, unit axis u, half extents along u and its left normal) with a height range
        struct VegBox308
        {
            public Vector2 c, u, half; public float minY, maxY;
            public bool Contains(float x, float z, float pad)
            {
                float dx = x - c.x, dz = z - c.y;
                return Mathf.Abs(dx * u.x + dz * u.y) <= half.x + pad && Mathf.Abs(-dx * u.y + dz * u.x) <= half.y + pad;
            }
        }

        sealed class VegUnit308
        {
            public string key = "", root = ""; public VegClass308 cls; public Transform t; public Bounds bounds; public bool enclosure, avoid;
            public int firstTri, triCount; public float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            public List<VegBox308> boxes = new List<VegBox308>();
        }

        // crown hull of a prototype in placement space: horizontal segments from the trunk axis to the farthest vertex of each
        // sector x height band (LOD0 mesh parts, billboard parts skipped); synthetic rings when no mesh can be read
        sealed class VegHull308 { public Vector3[] axis = Array.Empty<Vector3>(), tip = Array.Empty<Vector3>(); public bool synthetic; }

        sealed class VegPlace308
        {
            public Dress308 sheet; public string sheetPath = ""; public int index; public Dress308.FixedPlacement fp; public Dress308.Prototype proto; public bool rock;
            public Vector3 pos; public Quaternion rot; public float scale, height, crown, trunk; public VegHull308 hull;
            public bool gone, hasTo; public Vector3 to;
            public Vector3 Now => hasTo ? to : pos;
        }

        sealed class VegHit308 { public string reason = ""; public int unit = -1, crownHits; public float footDistance = -1, faceAbove, crownDepth, groundGap, groundY; public Vector2 toward; }

        sealed class VegCtx308
        {
            public BuildingAudit308.Config308 cfg; public VegCfg308 v; public Scene scene;
            public List<VegUnit308> units = new List<VegUnit308>(); public List<int> enclosureUnits = new List<int>(); public VegSoup308 soup = new VegSoup308(); public BuildingAudit308.MeshCache308 cache = new BuildingAudit308.MeshCache308();
            public List<Dress308> sheets = new List<Dress308>(); public List<VegPlace308> places = new List<VegPlace308>(); public List<string> notes = new List<string>();
            public List<BuildingAudit308.Corridor308> corridors = new List<BuildingAudit308.Corridor308>(); public List<WorldTerrainQuery> water = new List<WorldTerrainQuery>();
            public List<Dress308.PreserveArea> areas = new List<Dress308.PreserveArea>(); public List<Dress308.Passage> passages = new List<Dress308.Passage>();
            readonly Dictionary<string, VegHull308> hulls = new Dictionary<string, VegHull308>(); readonly Dictionary<long, List<VegPlace308>> grid = new Dictionary<long, List<VegPlace308>>();
            const float GridCell = 8f; public float maxTreeBuffer, maxRockBuffer, maxCrown; public int unreadableHulls;
            static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

            public VegClass308 ClassOf(string path)
            {
                int cut = path.IndexOf('/'); string rest = cut >= 0 ? path.Substring(cut + 1) : path;
                foreach (var c in v.classes) if (c.tokens.Any(t => t.Length > 0 && rest.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)) return c;
                return v.classes[v.classes.Length - 1];
            }

            public void Index()
            {
                grid.Clear();
                foreach (var p in places)
                {
                    long k = Key(Mathf.FloorToInt(p.pos.x / GridCell), Mathf.FloorToInt(p.pos.z / GridCell));
                    if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<VegPlace308>();
                    l.Add(p);
                }
            }
            public IEnumerable<VegPlace308> In(float x0, float z0, float x1, float z1)
            {
                for (int x = Mathf.FloorToInt(x0 / GridCell); x <= Mathf.FloorToInt(x1 / GridCell); x++)
                    for (int z = Mathf.FloorToInt(z0 / GridCell); z <= Mathf.FloorToInt(z1 / GridCell); z++)
                        if (grid.TryGetValue(Key(x, z), out var l)) foreach (var p in l) yield return p;
            }
            // another tree/rock (at its present place) or an already chosen target within `spacing` of xz
            public bool Crowded(VegPlace308 me, Vector3 at, float spacing, List<Vector3> taken)
            {
                foreach (var q in In(at.x - spacing, at.z - spacing, at.x + spacing, at.z + spacing))
                {
                    if (q == me || q.gone || q.hasTo) continue;
                    float dx = q.pos.x - at.x, dz = q.pos.z - at.z; if (dx * dx + dz * dz < spacing * spacing) return true;
                }
                foreach (var t in taken) { float dx = t.x - at.x, dz = t.z - at.z; if (dx * dx + dz * dz < spacing * spacing) return true; }
                return false;
            }

            bool GroundLike(Collider c, bool section)
            {
                if (c == null) return false;
                if (c is TerrainCollider) return true;
                for (var t = c.transform; t != null; t = t.parent) foreach (var tok in v.move.groundTokens) if (tok.Length > 0 && t.name.IndexOf(tok, StringComparison.Ordinal) >= 0) return true;
                return section && c.transform.root.name == v.move.sectionRoot;
            }
            // highest ground surface within the window of refY under xz; obstacle = a non-ground collider standing in the plant's height there
            public bool Ground(VegPlace308 p, float x, float z, float refY, out RaycastHit ground, out string obstacle)
            {
                ground = default; obstacle = null; float w = Mathf.Max(2f, v.move.groundWindow);
                bool section = v.move.sectionPrefix.Length > 0 && v.move.sectionRoot.Length > 0 && (p.fp.Id ?? "").StartsWith(v.move.sectionPrefix, StringComparison.Ordinal);
                var hits = Physics.RaycastAll(new Vector3(x, refY + w, z), Vector3.down, w * 2f, ~0, QueryTriggerInteraction.Ignore);
                bool found = false;
                foreach (var h in hits) if (GroundLike(h.collider, section) && (!found || h.point.y > ground.point.y)) { ground = h; found = true; }
                if (!found) return false;
                foreach (var h in hits)
                    if (!GroundLike(h.collider, section) && h.point.y > ground.point.y - v.move.obstacleHeight && h.point.y < ground.point.y + Mathf.Max(Mathf.Max(1f, p.height), v.move.skyClear)) { obstacle = BuildingAudit308.PathOf(h.collider.transform); break; }
                return true;
            }
            public float Sink(VegPlace308 p) => p.rock ? v.rockSink : v.reseatSink;
            // base hanging in the air: the highest ground surface of the window lies more than floatGap under the base (a base at or
            // under the surface is never "floating", whatever lies deeper), and nothing else (a rock, a deck) carries the plant
            public bool Floating(VegPlace308 p, Vector3 at, out float gap, out float groundY)
            {
                gap = 0; groundY = at.y;
                if (v.floatGap <= 0 || !Ground(p, at.x, at.z, at.y, out var g, out _)) return false;
                gap = at.y - (g.point.y - Sink(p)); groundY = g.point.y;
                if (gap <= v.floatGap) return false;
                bool section = v.move.sectionPrefix.Length > 0 && v.move.sectionRoot.Length > 0 && (p.fp.Id ?? "").StartsWith(v.move.sectionPrefix, StringComparison.Ordinal);
                if (Physics.Raycast(new Vector3(at.x, at.y + .3f, at.z), Vector3.down, out var hit, gap + .35f, ~0, QueryTriggerInteraction.Ignore) && !GroundLike(hit.collider, section)) return false;
                return true;
            }

            public VegHull308 HullOf(Dress308 sheet, Dress308.Prototype proto, bool rock)
            {
                string key = sheet.GetInstanceID() + "|" + proto.Id;
                if (hulls.TryGetValue(key, out var done)) return done;
                var pts = new List<Vector3>();
                if (proto.Lods != null && proto.Lods.Length > 0 && proto.Lods[0] != null && proto.Lods[0].Parts != null)
                    foreach (var part in proto.Lods[0].Parts)
                    {
                        if (part == null || part.Mesh == null) continue;
                        var mat = part.Material;
                        if (mat != null && mat.HasProperty("_Billboard") && mat.GetFloat("_Billboard") > .5f) continue;
                        var data = cache.Get(part.Mesh); if (data == null) continue;
                        var used = new HashSet<int>();
                        for (int k = 0; k < data.sub.Length; k++) if (data.sub[k] == part.Submesh) { used.Add(data.t[k * 3]); used.Add(data.t[k * 3 + 1]); used.Add(data.t[k * 3 + 2]); }
                        foreach (int i in used) pts.Add(part.Local.MultiplyPoint3x4(data.v[i]));
                    }
                var hull = new VegHull308(); int sectors = v.crownSectors, bands = rock ? Mathf.Min(3, v.crownBands) : v.crownBands; float from = rock ? 0f : Mathf.Clamp01(v.crownFrom);
                var ax = new List<Vector3>(); var tp = new List<Vector3>();
                if (pts.Count >= 16)
                {
                    float lo = pts.Min(q => q.y), hi = pts.Max(q => q.y), hgt = Mathf.Max(.01f, hi - lo);
                    Vector2 centre = Vector2.zero; int nb = 0;
                    if (rock) { var bb = new Bounds(pts[0], Vector3.zero); foreach (var q in pts) bb.Encapsulate(q); centre = new Vector2(bb.center.x, bb.center.z); }
                    else { foreach (var q in pts) if (q.y <= lo + hgt * .05f) { centre += new Vector2(q.x, q.z); nb++; } if (nb > 0) centre /= nb; }
                    var best = new float[sectors * bands]; var at = new Vector3[sectors * bands];
                    foreach (var q in pts)
                    {
                        float f = (q.y - lo) / hgt; if (f < from) continue;
                        int band = Mathf.Min(bands - 1, Mathf.FloorToInt((f - from) / Mathf.Max(1e-4f, 1f - from) * bands));
                        float dx = q.x - centre.x, dz = q.z - centre.y, r = Mathf.Sqrt(dx * dx + dz * dz); if (r < 1e-4f) continue;
                        int sector = Mathf.Min(sectors - 1, Mathf.FloorToInt((Mathf.Atan2(dz, dx) + Mathf.PI) / (2f * Mathf.PI) * sectors));
                        int k = band * sectors + sector; if (r > best[k]) { best[k] = r; at[k] = q; }
                    }
                    for (int k = 0; k < best.Length; k++) if (best[k] > 0) { ax.Add(new Vector3(centre.x, at[k].y, centre.y)); tp.Add(at[k]); }
                }
                if (ax.Count == 0)
                {
                    hull.synthetic = true; unreadableHulls++;
                    float rc = Mathf.Max(proto.Size.x, proto.Size.z) * .5f * v.crownSyntheticShare, hgt = Mathf.Max(.5f, proto.Size.y);
                    for (int bnd = 0; bnd < bands; bnd++)
                    {
                        float f = from + (1f - from) * (bnd + .5f) / bands;
                        for (int s = 0; s < sectors; s++) { float an = 2f * Mathf.PI * s / sectors; ax.Add(new Vector3(0, f * hgt, 0)); tp.Add(new Vector3(rc * Mathf.Cos(an), f * hgt, rc * Mathf.Sin(an))); }
                    }
                }
                hull.axis = ax.ToArray(); hull.tip = tp.ToArray(); hulls[key] = hull;
                return hull;
            }
        }

        static bool VegKeep(VegCfg308 v, Dress308.FixedPlacement f)
        {
            string key = ((f.Id ?? "") + " " + (f.ClusterId ?? "") + " " + (f.PrototypeId ?? "")).ToLowerInvariant();
            return v.keepTokens.Any(t => t.Length > 0 && key.Contains(t.ToLowerInvariant())) || v.keepIds.Contains(f.Id);
        }

        // the sheets drawn by an active CompactRebuildArtRenderer of the scene whose name or path carries a configured token
        static List<Dress308> VegSheets(Scene scene, BuildingAudit308.Config308 cfg, VegCfg308 v, List<string> notes)
        {
            var list = new List<Dress308>();
            foreach (var r in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).Where(r => r.isActiveAndEnabled && r.Sheet != null))
            {
                var s = r.Sheet; if (list.Contains(s)) continue;
                string path = AssetDatabase.GetAssetPath(s);
                if (!v.sheetTokens.Any(t => t.Length > 0 && (s.name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))) continue;
                if (string.IsNullOrEmpty(path)) { notes.Add("sheet " + s.name + " is not an asset (skipped)"); continue; }
                if (cfg.ProtectedAsset(path)) { notes.Add("protected sheet " + path + " is read by nothing here and never written (skipped)"); continue; }
                list.Add(s);
            }
            return list.OrderBy(s => AssetDatabase.GetAssetPath(s), StringComparer.Ordinal).ToList();
        }

        static void VegAddBox(VegCtx308 x, Matrix4x4 m, Vector3 centre, Vector3 size, int unit)
        {
            var e = size * .5f; var p = new Vector3[8];
            for (int i = 0; i < 8; i++) p[i] = m.MultiplyPoint3x4(centre + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
            int[] q = { 0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3 };
            for (int i = 0; i < q.Length; i += 3) x.soup.Add(p[q[i]], p[q[i + 1]], p[q[i + 2]], unit);
        }

        static VegCtx308 VegBuild(BuildingAudit308.Config308 cfg, VegCfg308 v, Scene scene)
        {
            Physics.SyncTransforms();
            var x = new VegCtx308 { cfg = cfg, v = v, scene = scene };
            x.maxTreeBuffer = v.classes.Max(c => c.buffer); x.maxRockBuffer = v.classes.Max(c => c.rockBuffer);
            // ---- footprint units: buildings of the configured roots (every unit when allUnits), the extra paths, the enclosures
            var seen = new HashSet<Transform>();
            void AddUnit(BuildingAudit308.Unit308 u, bool enclosure, bool avoid = false)
            {
                if (!u.hasBounds || !seen.Add(u.t)) return;
                if (Mathf.Max(u.bounds.size.x, u.bounds.size.z) < v.minUnitSize) return;
                var unit = new VegUnit308 { key = u.path, root = u.root, t = u.t, bounds = u.bounds, enclosure = enclosure, avoid = avoid, cls = x.ClassOf(u.path), firstTri = x.soup.Count };
                int index = x.units.Count; int skipped = 0;
                foreach (var r in u.lod0)
                {
                    if (r == null || BuildingAudit308.HasAny(r.name, v.skipRendererTokens)) continue;
                    var mesh = BuildingAudit308.MeshOf(r); if (mesh == null) continue;
                    var data = x.cache.Get(mesh); if (data == null) { skipped++; continue; }
                    var m = r.localToWorldMatrix; var w = new Vector3[data.v.Length];
                    for (int i = 0; i < w.Length; i++) w[i] = m.MultiplyPoint3x4(data.v[i]);
                    for (int i = 0; i + 2 < data.t.Length; i += 3) x.soup.Add(w[data.t[i]], w[data.t[i + 1]], w[data.t[i + 2]], index);
                    if (enclosure)
                    {
                        var lb = mesh.bounds; var c0 = m.MultiplyPoint3x4(lb.center); var ux = m.MultiplyVector(Vector3.right); var uz = m.MultiplyVector(Vector3.forward);
                        var u2 = new Vector2(ux.x, ux.z); float sx = u2.magnitude; var n2 = new Vector2(uz.x, uz.z); float sz = n2.magnitude;
                        var box = new VegBox308 { c = new Vector2(c0.x, c0.z), u = sx > 1e-4f ? u2 / sx : Vector2.right, half = new Vector2(lb.extents.x * sx, lb.extents.z * sz), minY = r.bounds.min.y, maxY = r.bounds.max.y };
                        float area = box.half.x * box.half.y * 4f;
                        if (sx <= 1e-4f || sz <= 1e-4f) { }
                        else if (v.enclosureMaxArea > 0 && area > v.enclosureMaxArea) x.notes.Add("enclosure " + u.path + "/" + r.name + ": box " + area.ToString("F0", Inv) + " m2 > enclosureMaxArea " + v.enclosureMaxArea.ToString("F0", Inv) + " (its faces count, its interior does not)");
                        else unit.boxes.Add(box);
                    }
                }
                if (v.includeColliders)
                    foreach (var c in u.colliders)
                    {
                        if (c == null || c.isTrigger || BuildingAudit308.HasAny(c.name, v.skipRendererTokens)) continue;
                        if (c is MeshCollider mc && mc.sharedMesh != null)
                        {
                            var data = x.cache.Get(mc.sharedMesh); if (data == null) { skipped++; continue; }
                            var m = c.transform.localToWorldMatrix; var w = new Vector3[data.v.Length];
                            for (int i = 0; i < w.Length; i++) w[i] = m.MultiplyPoint3x4(data.v[i]);
                            for (int i = 0; i + 2 < data.t.Length; i += 3) x.soup.Add(w[data.t[i]], w[data.t[i + 1]], w[data.t[i + 2]], index);
                        }
                        else if (c is BoxCollider bc) VegAddBox(x, c.transform.localToWorldMatrix, bc.center, bc.size, index);
                    }
                unit.triCount = x.soup.Count - unit.firstTri;
                if (skipped > 0) x.notes.Add(unit.key + ": " + skipped + " mesh(es) unreadable — those faces are not in the footprint");
                if (unit.triCount == 0) { seen.Remove(u.t); return; }
                for (int i = unit.firstTri; i < x.soup.Count; i++)
                {
                    unit.minY = Mathf.Min(unit.minY, Mathf.Min(x.soup.VA(i).y, Mathf.Min(x.soup.VB(i).y, x.soup.VC(i).y)));
                    unit.maxY = Mathf.Max(unit.maxY, Mathf.Max(x.soup.VA(i).y, Mathf.Max(x.soup.VB(i).y, x.soup.VC(i).y)));
                }
                if (unit.boxes.Count > 0) x.enclosureUnits.Add(x.units.Count);
                x.units.Add(unit);
            }
            bool Under(string[] list, string path) => list.Any(e => e.Length > 0 && (path == e || path.StartsWith(e + "/", StringComparison.Ordinal)));
            foreach (var root in v.roots)
            {
                var list = BuildingAudit308.UnitsOf(scene, cfg, new[] { root.name });
                if (list.Count == 0) { x.notes.Add("root " + root.name + " absent or empty in this scene"); continue; }
                foreach (var u in list)
                {
                    if (Under(v.excludeUnits, u.path)) continue;
                    if (root.units == "all" || (root.units == "buildings" && u.building) || Under(v.extraUnits, u.path)) AddUnit(u, v.enclosures.Contains(u.path));
                }
            }
            foreach (var path in v.extraUnits.Concat(v.enclosures))
            {
                var t = BuildingAudit308.Resolve(scene, path); if (t == null) { x.notes.Add("configured unit " + path + " absent in this scene"); continue; }
                bool enclosure = v.enclosures.Contains(path);
                if (!enclosure && x.units.Any(k => t == k.t || t.IsChildOf(k.t))) continue;
                var rc = cfg.roots.FirstOrDefault(r => r.name == t.root.name) ?? new BuildingAudit308.Root308 { name = t.root.name, depth = 1 };
                AddUnit(BuildingAudit308.MakeUnit(t, rc, false), enclosure);
            }
            // ---- target-only footprints: roots that are no subject of this pass, but nothing may be moved into or under them
            foreach (string name in v.avoidRoots)
            {
                if (name.Length == 0 || v.roots.Any(r => r.name == name)) continue;
                foreach (var u in BuildingAudit308.UnitsOf(scene, cfg, new[] { name })) if (!Under(v.excludeUnits, u.path)) AddUnit(u, false, true);
            }
            x.soup.skip = x.units.Select(k => k.avoid).ToArray();
            // ---- placements (Tree / Rock kinds of the configured list) of the scene's sheets
            x.sheets = VegSheets(scene, cfg, v, x.notes);
            foreach (var sheet in x.sheets)
            {
                var protos = new Dictionary<string, Dress308.Prototype>();
                foreach (var p in sheet.Prototypes) if (p != null && !string.IsNullOrEmpty(p.Id)) protos[p.Id] = p;
                var fps = sheet.FixedPlacements ?? Array.Empty<Dress308.FixedPlacement>(); string path = AssetDatabase.GetAssetPath(sheet);
                for (int i = 0; i < fps.Length; i++)
                {
                    var f = fps[i]; if (f == null || f.PrototypeId == null || !protos.TryGetValue(f.PrototypeId, out var proto)) continue;
                    if (!v.kinds.Contains(proto.Category.ToString())) continue;
                    bool rock = proto.Category == Dress308.Kind.Rock; float s = f.Scale, wide = Mathf.Max(proto.Size.x, proto.Size.z) * s;
                    var pl = new VegPlace308 { sheet = sheet, sheetPath = path, index = i, fp = f, proto = proto, rock = rock, pos = f.Position, rot = Quaternion.Euler(f.Euler), scale = s, height = proto.Size.y * s, crown = wide * .5f,
                        trunk = rock ? wide * .25f : Mathf.Max(v.minTrunkRadius, proto.Radius * s) };
                    x.places.Add(pl); x.maxCrown = Mathf.Max(x.maxCrown, pl.crown);
                }
                if (sheet.PreservedAreas != null) foreach (var a in sheet.PreservedAreas) if (a != null && (v.mask.areaPrefix.Length == 0 || a.Id == null || !a.Id.StartsWith(v.mask.areaPrefix, StringComparison.Ordinal))) x.areas.Add(a);
                if (sheet.Passages != null) x.passages.AddRange(sheet.Passages.Where(p => p != null));
            }
            x.Index();
            x.corridors = BuildingAudit308.Corridors(scene, cfg);
            x.water = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldTerrainQuery>(true)).ToList();
            return x;
        }

        // the placements within reach of a footprint unit (bounds padded by the crown radius + the widest buffer)
        static List<VegPlace308> VegNear(VegCtx308 x)
        {
            var set = new HashSet<VegPlace308>(); float buf = Mathf.Max(x.maxTreeBuffer, x.maxRockBuffer) + x.v.move.margin + 1f;
            foreach (var u in x.units)
            {
                if (u.avoid) continue;
                float pad = x.maxCrown * 1.5f + buf; var b = u.bounds;
                foreach (var p in x.In(b.min.x - pad, b.min.z - pad, b.max.x + pad, b.max.z + pad))
                {
                    float r = p.crown * 1.5f + buf; var at = p.Now;
                    if (at.x < b.min.x - r || at.x > b.max.x + r || at.z < b.min.z - r || at.z > b.max.z + r) continue;
                    set.Add(p);
                }
            }
            return set.OrderBy(p => p.sheetPath, StringComparer.Ordinal).ThenBy(p => p.index).ToList();
        }

        // trunk inside a footprint + its class buffer / inside an enclosure box / crown hull crossing a face / base floating over the ground.
        // strict (move target): the buffer grows by move.margin, faces count up to move.skyClear above the base, any crown crossing counts;
        // the floating test is skipped.
        static VegHit308 VegTest(VegCtx308 x, VegPlace308 p, Vector3 at, bool strict, bool floating)
        {
            var v = x.v; float extra = strict ? v.move.margin : 0f;
            x.soup.honourSkip = !strict;   // avoid-only units count for move targets, never for a standing placement
            // a move target needs open sky as far as the footprints go (never under a roof, a deck or an arch that is higher than the plant)
            float y0 = at.y - v.belowBand, y1 = at.y + Mathf.Max(.5f, strict ? Mathf.Max(p.height, v.move.skyClear) : p.height);
            if (x.soup.Vertical(at.x, at.z, y0, y1, out int u, out float fy)) return new VegHit308 { reason = "trunk", unit = u, footDistance = 0, faceAbove = fy - at.y };
            float maxBuf = (p.rock ? x.maxRockBuffer : x.maxTreeBuffer) + p.trunk + extra;
            for (float r = v.ringStep; r <= maxBuf + 1e-4f; r += v.ringStep)
            {
                int n = Mathf.Max(6, Mathf.CeilToInt(2f * Mathf.PI * r / v.ringArc));
                for (int k = 0; k < n; k++)
                {
                    float an = 2f * Mathf.PI * k / n, ox = r * Mathf.Cos(an), oz = r * Mathf.Sin(an);
                    if (!x.soup.Vertical(at.x + ox, at.z + oz, y0, y1, out u, out fy)) continue;
                    var c = x.units[u].cls; float buf = (p.rock ? c.rockBuffer : c.buffer) + p.trunk + extra;
                    if (r <= buf + 1e-4f) return new VegHit308 { reason = "trunk", unit = u, footDistance = r, faceAbove = fy - at.y, toward = new Vector2(ox, oz).normalized };
                }
            }
            foreach (int i in x.enclosureUnits)
            {
                var eu = x.units[i];
                float pad = (p.rock ? eu.cls.rockBuffer : eu.cls.buffer) + p.trunk + extra;
                foreach (var b in eu.boxes)
                    if (at.y <= b.maxY + v.belowBand && at.y >= b.minY - Mathf.Max(1f, p.height) && b.Contains(at.x, at.z, pad))
                        return new VegHit308 { reason = "enclosure", unit = i, footDistance = 0, faceAbove = b.maxY - at.y, toward = (b.c - new Vector2(at.x, at.z)).normalized };
            }
            var hull = p.hull ?? (p.hull = x.HullOf(p.sheet, p.proto, p.rock));
            var m = Matrix4x4.TRS(at, p.rot, Vector3.one * p.scale);
            int hits = 0, cu = -1; float deep = 0; Vector2 dir = Vector2.zero;
            for (int i = 0; i < hull.axis.Length; i++)
            {
                Vector3 a = m.MultiplyPoint3x4(hull.axis[i]), b = m.MultiplyPoint3x4(hull.tip[i]);
                if (!x.soup.Segment(a, b, out int su, out float t)) continue;
                float depth = (1f - t) * Vector3.Distance(a, b);
                if (strict) return new VegHit308 { reason = "crown", unit = su, crownHits = 1, crownDepth = depth };
                if (depth < v.crownDepth) continue;
                hits++; dir += new Vector2(b.x - a.x, b.z - a.z).normalized;
                if (depth > deep) { deep = depth; cu = su; }
            }
            if (hits >= Mathf.Max(1, v.crownMinSamples)) return new VegHit308 { reason = "crown", unit = cu, crownHits = hits, crownDepth = deep, toward = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.zero };
            if (floating && x.Floating(p, at, out float gap, out float gy)) return new VegHit308 { reason = "floating", groundGap = gap, groundY = gy };
            return null;
        }

        // a move target: ground, slope, altitude, dry, free of obstacles and other plants, outside preserved areas / passages / route
        // corridors, and clear of every footprint (strict test)
        static bool VegSpot(VegCtx308 x, VegPlace308 p, float px, float pz, List<Vector3> taken, out Vector3 spot, out float slope, out string why)
        {
            spot = default; slope = 0; why = ""; var mv = x.v.move;
            if (!x.Ground(p, px, pz, p.pos.y, out var g, out string obstacle)) { why = "no ground"; return false; }
            if (obstacle != null) { why = "obstacle " + obstacle; return false; }
            slope = Vector3.Angle(g.normal, Vector3.up);
            if (slope > Mathf.Min(mv.maxSlope, p.proto.MaximumSlope)) { why = "slope"; return false; }
            if (g.point.y > p.proto.MaximumAltitude) { why = "altitude"; return false; }
            foreach (var q in x.water) if (q != null && q.TryWaterHeight(g.point, out float wy) && g.point.y < wy + mv.waterMargin) { why = "water"; return false; }
            if (x.Crowded(p, g.point, p.rock ? mv.rockSpacing : mv.treeSpacing, taken)) { why = "crowded"; return false; }
            var kind = p.rock ? Dress308.Kind.Rock : Dress308.Kind.Tree;
            foreach (var a in x.areas) if (Dress308.Excludes(a, g.point, kind)) { why = "preserved area " + a.Id; return false; }
            foreach (var r in x.passages) if (Dress308.PassageEdgeDistance(r, g.point) < mv.passageClearance) { why = "passage " + r.Id; return false; }
            if (x.corridors.Count > 0 && BuildingAudit308.CorridorClearance(new Vector2(g.point.x, g.point.z), x.corridors, out string cid) < p.trunk + mv.corridorClearance) { why = "route " + cid; return false; }
            spot = new Vector3(g.point.x, g.point.y - x.Sink(p), g.point.z);
            var hit = VegTest(x, p, spot, true, false);
            if (hit != null) { why = "intrudes (" + hit.reason + ")"; return false; }
            return true;
        }

        // a hit on a limited unit (veg308.json limits) outside every radius configured for it
        static bool VegOutOfLimit(VegCtx308 x, Vector3 at, VegHit308 hit)
        {
            if (hit == null || hit.unit < 0 || x.v.limits.Length == 0) return false;
            string key = x.units[hit.unit].key; bool any = false;
            foreach (var l in x.v.limits)
            {
                if (l.unit.Length == 0 || l.near.Length < 3 || !(key == l.unit || key.StartsWith(l.unit + "/", StringComparison.Ordinal))) continue;
                any = true;
                if (new Vector2(at.x - l.near[0], at.z - l.near[2]).magnitude <= l.radius) return false;
            }
            return any;
        }
        // inside an `unnamed` radius of veg308.json (a DEFECTS_308 row without a placement id)
        static bool VegListed(VegCfg308 v, Vector3 at)
        {
            foreach (var un in v.unnamed) if (un.near.Length >= 3 && new Vector2(at.x - un.near[0], at.z - un.near[2]).magnitude <= un.radius) return true;
            return false;
        }

        static VegRow308 VegRowOf(VegCtx308 x, VegPlace308 p, VegHit308 hit, VegNamed308 n)
        {
            var row = new VegRow308 { sheet = p.sheetPath, index = p.index, id = p.fp.Id ?? "", proto = p.fp.PrototypeId ?? "", kind = p.proto.Category.ToString(), from = p.pos,
                reason = hit != null ? hit.reason : "named", defect = n != null ? string.Join(",", n.defect) : "" };
            if (hit != null)
            {
                row.footDistance = hit.footDistance; row.faceAbove = hit.faceAbove; row.crownHits = hit.crownHits; row.crownDepth = hit.crownDepth; row.groundGap = hit.groundGap;
                if (hit.unit >= 0) { row.unit = x.units[hit.unit].key; row.cls = x.units[hit.unit].cls.name; }
            }
            if (row.unit.Length == 0 && n != null) row.unit = n.building;
            return row;
        }

        // ------------------------------------------------------------------ mask areas (footprint rectangles, also written as PreservedAreas)

        static List<Vector2> VegConvexHull(List<Vector2> pts)
        {
            pts = pts.OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            if (pts.Count < 3) return pts;
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var h = new List<Vector2>();
            foreach (var p in pts) { while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], p) <= 0) h.RemoveAt(h.Count - 1); h.Add(p); }
            int lower = h.Count + 1;
            for (int i = pts.Count - 2; i >= 0; i--) { var p = pts[i]; while (h.Count >= lower && Cross(h[h.Count - 2], h[h.Count - 1], p) <= 0) h.RemoveAt(h.Count - 1); h.Add(p); }
            h.RemoveAt(h.Count - 1);
            return h;
        }

        // smallest-area rectangle around a convex hull: centre, unit axis u, half extents (along u, along its left normal)
        static void VegMinRect(List<Vector2> hull, out Vector2 centre, out Vector2 u, out Vector2 half)
        {
            centre = Vector2.zero; u = Vector2.right; half = Vector2.zero; float best = float.PositiveInfinity;
            if (hull.Count == 0) return;
            int edges = hull.Count < 3 ? 1 : hull.Count;
            for (int i = 0; i < edges; i++)
            {
                var e = hull.Count < 2 ? Vector2.right : hull[(i + 1) % hull.Count] - hull[i];
                if (e.sqrMagnitude < 1e-10f) e = Vector2.right;
                e.Normalize(); var n = new Vector2(-e.y, e.x);
                float e0 = float.PositiveInfinity, e1 = float.NegativeInfinity, n0 = float.PositiveInfinity, n1 = float.NegativeInfinity;
                foreach (var p in hull) { float a = Vector2.Dot(p, e), b = Vector2.Dot(p, n); e0 = Mathf.Min(e0, a); e1 = Mathf.Max(e1, a); n0 = Mathf.Min(n0, b); n1 = Mathf.Max(n1, b); }
                float area = (e1 - e0) * (n1 - n0);
                if (area < best) { best = area; u = e; centre = e * ((e0 + e1) * .5f) + n * ((n0 + n1) * .5f); half = new Vector2((e1 - e0) * .5f, (n1 - n0) * .5f); }
            }
        }

        // 2D triangle vs axis-aligned box (separating axes: the box axes and the three edge normals)
        static bool VegTriBox(Vector2 a, Vector2 b, Vector2 c, Vector2 lo, Vector2 hi)
        {
            if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < lo.x || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > hi.x || Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < lo.y || Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > hi.y) return false;
            var bc = (lo + hi) * .5f; var be = (hi - lo) * .5f;
            for (int i = 0; i < 3; i++)
            {
                var p = i == 0 ? a : i == 1 ? b : c; var q = i == 0 ? b : i == 1 ? c : a; var n = new Vector2(-(q.y - p.y), q.x - p.x);
                if (n.sqrMagnitude < 1e-12f) continue;
                float t0 = Vector2.Dot(a, n), t1 = Vector2.Dot(b, n), t2 = Vector2.Dot(c, n), tmin = Mathf.Min(t0, Mathf.Min(t1, t2)), tmax = Mathf.Max(t0, Mathf.Max(t1, t2));
                float r = be.x * Mathf.Abs(n.x) + be.y * Mathf.Abs(n.y), cc = Vector2.Dot(bc, n);
                if (cc + r < tmin || cc - r > tmax) return false;
            }
            return true;
        }

        // rectangles (cell ranges i0..i1 x j0..j1) over the cells of a w x h grid that a triangle touches; the triangles are given
        // in grid space (0..w*cell, 0..h*cell). The scan stops once more than `limit` rectangles are needed.
        static List<(int i0, int j0, int i1, int j1)> VegCoverRects(Vector2[] a, Vector2[] b, Vector2[] c, int w, int h, float cell, int limit)
        {
            var mark = new bool[w * h];
            for (int t = 0; t < a.Length; t++)
            {
                int i0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a[t].x, Mathf.Min(b[t].x, c[t].x)) / cell), 0, w - 1), i1 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(a[t].x, Mathf.Max(b[t].x, c[t].x)) / cell), 0, w - 1);
                int j0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a[t].y, Mathf.Min(b[t].y, c[t].y)) / cell), 0, h - 1), j1 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(a[t].y, Mathf.Max(b[t].y, c[t].y)) / cell), 0, h - 1);
                for (int ii = i0; ii <= i1; ii++)
                    for (int jj = j0; jj <= j1; jj++)
                        if (!mark[jj * w + ii] && VegTriBox(a[t], b[t], c[t], new Vector2(ii * cell, jj * cell), new Vector2((ii + 1) * cell, (jj + 1) * cell))) mark[jj * w + ii] = true;
            }
            // greedy: the widest run of free marked cells in a row, grown downward while every cell under it is free and marked
            var used = new bool[w * h]; var rects = new List<(int i0, int j0, int i1, int j1)>();
            for (int j = 0; j < h && rects.Count <= limit; j++)
                for (int i = 0; i < w && rects.Count <= limit; i++)
                {
                    if (!mark[j * w + i] || used[j * w + i]) continue;
                    int i1 = i; while (i1 + 1 < w && mark[j * w + i1 + 1] && !used[j * w + i1 + 1]) i1++;
                    int j1 = j; bool grow = true;
                    while (grow && j1 + 1 < h) { for (int k = i; k <= i1; k++) if (!mark[(j1 + 1) * w + k] || used[(j1 + 1) * w + k]) { grow = false; break; } if (grow) j1++; }
                    for (int jj = j; jj <= j1; jj++) for (int k = i; k <= i1; k++) used[jj * w + k] = true;
                    rects.Add((i, j, i1, j1));
                }
            return rects;
        }

        static List<VegArea308> VegAreas(VegCtx308 x)
        {
            var v = x.v; var list = new List<VegArea308>(); var m = v.mask; float cell0 = Mathf.Max(.5f, m.areaCell); int cap = Mathf.Max(1, m.maxAreasPerUnit), hard = Mathf.Max(cap, m.maxAreasHard);
            foreach (var unit in x.units.OrderBy(k => k.key, StringComparer.Ordinal))
            {
                if (unit.avoid) continue;
                float tree = unit.cls.buffer + v.minTrunkRadius, rockPad = unit.cls.rockBuffer; int n = 0;
                void Emit(Vector2 c, Vector2 u, Vector2 half, float lo, float hi)
                {
                    float yaw = Mathf.Atan2(-u.y, u.x) * Mathf.Rad2Deg;
                    list.Add(new VegArea308 { id = m.areaPrefix + unit.key + "#" + n++, unit = unit.key, cls = unit.cls.name, centre = new Vector3(c.x, (lo + hi) * .5f, c.y), half = half, yaw = yaw,
                        minY = lo - m.yBelow, maxY = hi + m.yAbove, treePad = tree, rockPad = rockPad });
                }
                foreach (var b in unit.boxes) Emit(b.c, b.u, b.half, b.minY, b.maxY);   // enclosure interiors
                if (unit.triCount == 0) continue;
                var seenPts = new HashSet<long>(); var pts = new List<Vector2>();
                void Pt(Vector3 p) { long k = ((long)Mathf.RoundToInt(p.x * 10f) << 32) ^ (uint)Mathf.RoundToInt(p.z * 10f); if (seenPts.Add(k)) pts.Add(new Vector2(p.x, p.z)); }
                for (int i = unit.firstTri; i < unit.firstTri + unit.triCount; i++) { Pt(x.soup.VA(i)); Pt(x.soup.VB(i)); Pt(x.soup.VC(i)); }
                VegMinRect(VegConvexHull(pts), out var centre, out var ax, out var half);
                var nx = new Vector2(-ax.y, ax.x); bool done = false;
                // cell sizes from fine to coarse: the first one that needs <= maxAreasPerUnit rectangles; else the one with the fewest
                // (<= maxAreasHard); else no area for this unit (one box around a ring wall would shut the whole yard inside it)
                List<(int i0, int j0, int i1, int j1)> spare = null; float spareCell = 0;
                // the unit's triangles in the rectangle's own frame (0..2*half)
                int tc = unit.triCount; var la = new Vector2[tc]; var lb = new Vector2[tc]; var lc = new Vector2[tc];
                Vector2 Local(Vector3 p) { var d = new Vector2(p.x - centre.x, p.z - centre.y); return new Vector2(Vector2.Dot(d, ax) + half.x, Vector2.Dot(d, nx) + half.y); }
                for (int i = 0; i < tc; i++) { la[i] = Local(x.soup.VA(unit.firstTri + i)); lb[i] = Local(x.soup.VB(unit.firstTri + i)); lc[i] = Local(x.soup.VC(unit.firstTri + i)); }
                for (float cell = cell0; !done && cell <= cell0 * 8.01f; cell *= 2f)
                {
                    int w = Mathf.Max(1, Mathf.CeilToInt(half.x * 2f / cell)), h = Mathf.Max(1, Mathf.CeilToInt(half.y * 2f / cell));
                    if ((long)w * h > 65536) continue;
                    var rects = VegCoverRects(la, lb, lc, w, h, cell, hard);
                    if (rects.Count > cap)
                    {
                        if (rects.Count <= hard && (spare == null || rects.Count < spare.Count)) { spare = rects; spareCell = cell; }
                        continue;
                    }
                    spare = rects; spareCell = cell; done = true;
                }
                if (spare == null) { x.notes.Add("mask: " + unit.key + " needs more than " + hard + " rectangles — no area written for it (decisions + guard still cover it)"); continue; }
                foreach (var r in spare)
                {
                    float s0 = Mathf.Max(0, r.i0 * spareCell), s1 = Mathf.Min(half.x * 2f, (r.i1 + 1) * spareCell), t0 = Mathf.Max(0, r.j0 * spareCell), t1 = Mathf.Min(half.y * 2f, (r.j1 + 1) * spareCell);
                    var c = centre + ax * ((s0 + s1) * .5f - half.x) + nx * ((t0 + t1) * .5f - half.y);
                    Emit(c, ax, new Vector2((s1 - s0) * .5f, (t1 - t0) * .5f), unit.minY, unit.maxY);
                }
            }
            return list;
        }

        static Dress308.PreserveArea VegPreserve(VegCfg308 v, VegArea308 a) => new Dress308.PreserveArea
        {
            Id = a.id, Centre = a.centre, HalfSize = a.half, Yaw = a.yaw, ExcludeProcedural = true, LimitHeight = true, MinimumY = a.minY, MaximumY = a.maxY,
            AffectedKinds = v.mask.affectedKinds, TypedClearance = true, Padding = new Vector4(a.treePad, 0, 0, a.rockPad)
        };

        // ------------------------------------------------------------------ plan

        static string VegAlias(BuildingAudit308.Config308 cfg, VegCfg308 v, Dictionary<string, string> opt)
            => opt.TryGetValue("scene", out string s) && s.Length > 0 ? s : (v.planScene.Length > 0 ? v.planScene : "main");

        static string VegPlan(Dictionary<string, string> opt)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var v = VegLoadConfig(out err); if (v == null) return "refused: " + err;
            string alias = VegAlias(cfg, v, opt);
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            VegPlan308 plan;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var x = VegBuild(cfg, v, scene);
                plan = VegMakePlan(x, alias);
                plan.seconds = (float)sw.Elapsed.TotalSeconds;
                VegWrite(VegPlanFile, plan);
                VegWriteCsv(VegPlanCsv, plan.rows);
            }
            finally { GoBack(previous, scene, opened, true); }
            return VegPlanText(plan);
        }

        static VegPlan308 VegMakePlan(VegCtx308 x, string alias)
        {
            var v = x.v; var mv = v.move;
            var plan = new VegPlan308 { utc = BuildingAudit308.Utc(), alias = alias, scene = x.scene.path, sceneSha = BuildingAudit308.Sha(BuildingAudit308.Abs(x.scene.path)), quality = BuildingAudit308.QualityName(), configVersion = v.version,
                units = x.units.Count, triangles = x.soup.Count };
            var named = new Dictionary<string, VegNamed308>(); foreach (var n in v.named) if (!string.IsNullOrEmpty(n.id)) named[n.id] = n;
            var cover = new Dictionary<string, VegCover308>();
            foreach (var n in v.named) cover[n.id] = new VegCover308 { id = n.id, defect = string.Join(",", n.defect), building = n.building, status = "absent", action = "", note = "not in an active sheet of this scene" };
            var near = VegNear(x); plan.near = near.Count;
            var todo = new List<VegPlace308>(near); var inNear = new HashSet<VegPlace308>(near);
            foreach (var p in x.places) if (!inNear.Contains(p) && (named.ContainsKey(p.fp.Id ?? "") || v.removeIds.Contains(p.fp.Id))) todo.Add(p);
            var taken = new List<Vector3>(); var perSheet = new Dictionary<string, int>(); int outOfLimit = 0;
            // decisions already in force (Fix/veg-mask.json): a named placement that was moved once is never "forced" a second time by a new plan
            var decided = new List<VegRow308>();
            if (File.Exists(VegMaskFile)) { var done = JsonUtility.FromJson<VegMask308>(File.ReadAllText(VegMaskFile)); if (done != null) decided = done.decisions; }
            var dirs = new Vector2[mv.directions]; for (int k = 0; k < dirs.Length; k++) { float an = 2f * Mathf.PI * k / dirs.Length; dirs[k] = new Vector2(Mathf.Cos(an), Mathf.Sin(an)); }
            foreach (var p in todo)
            {
                string id = p.fp.Id ?? ""; named.TryGetValue(id, out var n);
                if (VegKeep(v, p.fp)) { if (n != null) { cover[id].status = "kept"; cover[id].note = "keepTokens / keepIds"; } continue; }
                var hit = VegTest(x, p, p.pos, false, inNear.Contains(p) && (!v.floatNamedOnly || n != null));
                bool remove = v.removeIds.Contains(id) || (n != null && n.action == "remove");
                if (!remove && n == null && VegOutOfLimit(x, p.pos, hit)) { outOfLimit++; continue; }
                string action;
                if (remove) action = "remove";
                else if (hit == null)
                {
                    if (n == null) continue;
                    if (n.action == "check") { cover[id].status = "clear"; cover[id].note = "check only: no intrusion measured"; continue; }
                    if (decided.Any(r => r.sheet == p.sheetPath && r.id == id && (r.action == "move" || r.action == "reseat") && Vector3.Distance(r.to, p.pos) <= v.positionTolerance))
                    { cover[id].status = "clear"; cover[id].note = "already moved by an applied veg op (Fix/veg-mask.json); no intrusion measured"; continue; }
                    if (n.action == "reseat")
                    {
                        if (!x.Ground(p, p.pos.x, p.pos.z, p.pos.y, out var g0, out _)) { cover[id].status = "review"; cover[id].note = "no ground under the base"; continue; }
                        float gap = p.pos.y - (g0.point.y - x.Sink(p));
                        if (Mathf.Abs(gap) <= v.positionTolerance) { cover[id].status = "clear"; cover[id].note = "already on the ground"; continue; }
                        hit = new VegHit308 { reason = "named", groundGap = gap, groundY = g0.point.y }; action = "reseat";
                    }
                    else if (n.minMove > 0) action = "move";
                    else { cover[id].status = "review"; cover[id].note = "named, but no intrusion measured and no minMove configured"; continue; }
                }
                else if (hit.reason == "floating")
                {
                    // lowered onto the ground it must not start to intrude: then it is moved like any other intruder
                    var seated = VegTest(x, p, new Vector3(p.pos.x, hit.groundY - x.Sink(p), p.pos.z), false, false);
                    if (seated != null) { seated.groundGap = hit.groundGap; hit = seated; action = "move"; } else action = "reseat";
                }
                else action = "move";
                var row = VegRowOf(x, p, hit, n);
                if (action == "remove") { row.action = "remove"; row.note = "configured removal"; row.placementJson = JsonUtility.ToJson(p.fp); p.gone = true; }
                else if (action == "reseat")
                {
                    row.action = "reseat"; row.to = new Vector3(p.pos.x, hit.groundY - x.Sink(p), p.pos.z); row.moved = Mathf.Abs(row.to.y - p.pos.y);
                    p.hasTo = true; p.to = row.to; taken.Add(row.to);
                }
                else
                {
                    Vector2 away = hit != null && hit.toward.sqrMagnitude > 1e-6f ? -hit.toward : Vector2.zero;
                    if (away == Vector2.zero && hit != null && hit.unit >= 0) { var c = x.units[hit.unit].bounds.center; var d = new Vector2(p.pos.x - c.x, p.pos.z - c.z); if (d.sqrMagnitude > 1e-4f) away = d.normalized; }
                    // a named placement without a measured direction leaves its own building (nearest point of the unit's bounds)
                    if (away == Vector2.zero && n != null)
                    {
                        var bu = x.units.Where(k => k.key == n.building || k.key.StartsWith(n.building + "/", StringComparison.Ordinal)).OrderBy(k => k.bounds.SqrDistance(p.pos)).FirstOrDefault();
                        if (bu != null)
                        {
                            var cp = bu.bounds.ClosestPoint(p.pos); var d = new Vector2(p.pos.x - cp.x, p.pos.z - cp.z);
                            if (d.sqrMagnitude < 1e-4f) d = new Vector2(p.pos.x - bu.bounds.center.x, p.pos.z - bu.bounds.center.z);
                            if (d.sqrMagnitude > 1e-4f) away = d.normalized;
                        }
                    }
                    var order = away == Vector2.zero ? dirs : dirs.OrderByDescending(d => Vector2.Dot(d, away)).ToArray();
                    float lo = Mathf.Max(mv.minMove, n != null ? n.minMove : 0f); bool found = false; var why = new Dictionary<string, int>();
                    for (float r = lo; r <= mv.maxMove + 1e-4f && !found; r += mv.step)
                        foreach (var d in order)
                        {
                            if (VegSpot(x, p, p.pos.x + d.x * r, p.pos.z + d.y * r, taken, out var spot, out float slope, out string no))
                            {
                                row.action = "move"; row.to = spot; row.slope = slope; row.moved = new Vector2(spot.x - p.pos.x, spot.z - p.pos.z).magnitude;
                                p.hasTo = true; p.to = spot; taken.Add(spot); found = true; break;
                            }
                            string k = no.Split(' ')[0]; why[k] = why.TryGetValue(k, out int cnt) ? cnt + 1 : 1;
                        }
                    if (!found)
                    {
                        // removeWhenNoSpot off: the row stays in the plan as "review" (listed, never applied, the placement is kept)
                        // a removal needs: removeWhenNoSpot, a DEFECTS_308 listing (named id or an `unnamed` radius) unless removeUnnamedWhenNoSpot,
                        // and at least one probed spot with ground under it — a failed ground lookup never deletes anything
                        bool sawGround = why.Keys.Any(k => k != "no"), listed = n != null || VegListed(v, p.pos);
                        if (v.removeWhenNoSpot && sawGround && (listed || v.removeUnnamedWhenNoSpot)) { row.action = "remove"; row.placementJson = JsonUtility.ToJson(p.fp); p.gone = true; } else row.action = "review";
                        row.note = (row.action != "review" ? "" : !sawGround ? "REVIEW (no ground found at any probed spot): " : !listed ? "REVIEW (not listed in DEFECTS_308, kept): " : "REVIEW: ") + "no valid spot within " + mv.maxMove.ToString("0.#", Inv) + " m (" + string.Join(", ", why.OrderByDescending(k => k.Value).Take(4).Select(k => k.Key + " " + k.Value)) + ")";
                    }
                }
                plan.rows.Add(row);
                perSheet[p.sheetPath] = perSheet.TryGetValue(p.sheetPath, out int c2) ? c2 + 1 : 1;
                if (n != null) { cover[id].status = hit != null && hit.reason != "named" ? "flagged" : "forced"; cover[id].action = row.action + (row.action == "move" ? " " + row.moved.ToString("F1", Inv) + " m" : ""); cover[id].note = row.reason + (row.unit.Length > 0 ? " @ " + row.unit : ""); }
            }
            // estimated positions (DEFECTS rows without a placement id): the defect number goes onto the rows found there
            foreach (var un in v.unnamed)
            {
                var c = BuildingAudit308.Vec(un.near); var hitRows = plan.rows.Where(r => new Vector2(r.from.x - c.x, r.from.z - c.z).magnitude <= un.radius).ToList();
                foreach (var r in hitRows) foreach (var d in un.defect) if (!("," + r.defect + ",").Contains("," + d + ",")) r.defect = r.defect.Length == 0 ? d : r.defect + "," + d;
                plan.named.Add(new VegCover308 { id = "(near " + BuildingAudit308.V(c) + " r" + un.radius.ToString("0.#", Inv) + ")", defect = string.Join(",", un.defect), building = un.building,
                    status = hitRows.Count > 0 ? "flagged" : "review", action = hitRows.Count + " row(s)", note = hitRows.Count > 0 ? string.Join(" ", hitRows.Take(6).Select(r => r.id)) : "no intruding placement measured in the radius — " + un.note });
            }
            plan.named.InsertRange(0, v.named.Select(n => cover[n.id]));
            // F2 (sheet-apply) may already have removed a named placement
            if (File.Exists(SheetLedgerFile))
            {
                var f2 = JsonUtility.FromJson<SheetLedger308>(File.ReadAllText(SheetLedgerFile));
                foreach (var c in plan.named.Where(c => c.status == "absent")) if (f2 != null && f2.rows.Any(r => r.id == c.id && !r.reverted)) { c.status = "removed-by-F2"; c.note = "already removed by sheet-apply (F2)"; }
            }
            plan.areas = VegAreas(x);
            var areaSheet = x.sheets.FirstOrDefault(s => v.mask.areaSheetToken.Length > 0 && AssetDatabase.GetAssetPath(s).IndexOf(v.mask.areaSheetToken, StringComparison.OrdinalIgnoreCase) >= 0) ?? x.sheets.FirstOrDefault();
            plan.areaSheet = v.mask.writePreservedAreas && areaSheet != null ? AssetDatabase.GetAssetPath(areaSheet) : "";
            foreach (var s in x.sheets)
            {
                string path = AssetDatabase.GetAssetPath(s);
                plan.sheets.Add(new VegSheetInfo308 { path = path, sha = BuildingAudit308.Sha(BuildingAudit308.Abs(path)), placements = (s.FixedPlacements ?? Array.Empty<Dress308.FixedPlacement>()).Length,
                    candidates = near.Count(p => p.sheet == s), rows = perSheet.TryGetValue(path, out int c) ? c : 0 });
            }
            plan.move = plan.rows.Count(r => r.action == "move"); plan.remove = plan.rows.Count(r => r.action == "remove"); plan.reseat = plan.rows.Count(r => r.action == "reseat"); plan.review = plan.rows.Count(r => r.action == "review");
            plan.notes.Add("footprint units per root: " + string.Join(", ", x.units.GroupBy(u => u.root).Select(g => g.Key + " " + g.Count())) + "; per class: " + string.Join(", ", x.units.GroupBy(u => u.cls.name).Select(g => g.Key + " " + g.Count())));
            if (x.units.Any(u => u.avoid)) plan.notes.Add("target-only (avoid) units: " + x.units.Count(u => u.avoid) + " of " + string.Join(", ", x.units.Where(u => u.avoid).Select(u => u.root).Distinct()));
            if (outOfLimit > 0) plan.notes.Add(outOfLimit + " un-named placement(s) touch a limited unit outside its radius (veg308.json limits) — left as they are");
            if (plan.rows.Any(r => r.action == "review")) plan.notes.Add(plan.rows.Count(r => r.action == "review") + " review row(s): never applied, the placement stays — guard reports each of them until it is decided (removeIds / keepIds / a larger maxMove)");
            plan.notes.AddRange(x.notes);
            if (x.unreadableHulls > 0) plan.notes.Add(x.unreadableHulls + " prototype(s) without a readable LOD0 mesh: synthetic crown rings used");
            if (x.cache.Failed > 0) plan.notes.Add(x.cache.Failed + " mesh(es) could not be read");
            if (x.water.Count == 0) plan.notes.Add("no WorldTerrainQuery in the scene: the water test of move targets was skipped");
            if (v.maxRows > 0 && plan.rows.Count > v.maxRows) { plan.blocked = true; plan.notes.Add("BLOCKED: " + plan.rows.Count + " rows > maxRows " + v.maxRows + " (review veg308.json before apply)"); }
            return plan;
        }

        static void VegWriteCsv(string file, List<VegRow308> rows)
        {
            var sb = new StringBuilder("sheet,index,id,prototype,kind,action,reason,unit,class,from_x,from_y,from_z,to_x,to_y,to_z,moved,foot_distance,face_above,crown_hits,crown_depth,ground_gap,slope,defect,note\n");
            foreach (var r in rows)
            {
                bool to = r.action == "move" || r.action == "reseat";
                sb.Append(Path.GetFileName(r.sheet)).Append(',').Append(r.index).Append(',').Append(r.id).Append(',').Append(r.proto).Append(',').Append(r.kind).Append(',').Append(r.action).Append(',').Append(r.reason).Append(',')
                    .Append(r.unit).Append(',').Append(r.cls).Append(',').Append(r.from.x.ToString("F3", Inv)).Append(',').Append(r.from.y.ToString("F3", Inv)).Append(',').Append(r.from.z.ToString("F3", Inv)).Append(',')
                    .Append(to ? r.to.x.ToString("F3", Inv) : "").Append(',').Append(to ? r.to.y.ToString("F3", Inv) : "").Append(',').Append(to ? r.to.z.ToString("F3", Inv) : "").Append(',').Append(r.moved.ToString("F2", Inv)).Append(',')
                    .Append(r.footDistance.ToString("F2", Inv)).Append(',').Append(r.faceAbove.ToString("F2", Inv)).Append(',').Append(r.crownHits).Append(',').Append(r.crownDepth.ToString("F2", Inv)).Append(',')
                    .Append(r.groundGap.ToString("F2", Inv)).Append(',').Append(r.slope.ToString("F1", Inv)).Append(',').Append(r.defect.Replace(',', ' ')).Append(',').Append('"').Append(r.note.Replace('"', '\'')).Append('"').Append('\n');
            }
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
        }

        static string VegPlanText(VegPlan308 p)
        {
            var sb = new StringBuilder("veg plan " + p.alias + " (" + p.scene + ") sha " + p.sceneSha + " quality " + p.quality + " config " + p.configVersion + (p.blocked ? " — BLOCKED" : "") + "\n");
            sb.AppendLine("  footprint units " + p.units + ", triangles " + p.triangles + ", placements near " + p.near + ", " + p.seconds.ToString("F1", Inv) + " s");
            sb.AppendLine("  rows " + p.rows.Count + ": move " + p.move + ", remove " + p.remove + ", reseat " + p.reseat + ", review " + p.review + "; mask areas " + p.areas.Count + (p.areaSheet.Length > 0 ? " -> PreservedAreas of " + Path.GetFileName(p.areaSheet) : " (file only)"));
            foreach (var g in p.rows.GroupBy(r => r.reason).OrderByDescending(g => g.Count())) sb.AppendLine("    reason " + g.Key + ": " + g.Count());
            foreach (var g in p.rows.GroupBy(r => r.unit.Split('/')[0]).OrderByDescending(g => g.Count())) sb.AppendLine("    root " + (g.Key.Length > 0 ? g.Key : "(none)") + ": " + g.Count());
            foreach (var s in p.sheets) sb.AppendLine("  sheet " + s.path + " sha " + s.sha + ": placements " + s.placements + ", near " + s.candidates + ", rows " + s.rows);
            var moved = p.rows.Where(r => r.action == "move").Select(r => r.moved).OrderBy(m => m).ToList();
            if (moved.Count > 0) sb.AppendLine("  move distance: min " + moved[0].ToString("F1", Inv) + " median " + moved[moved.Count / 2].ToString("F1", Inv) + " max " + moved[moved.Count - 1].ToString("F1", Inv) + " m");
            sb.AppendLine("  DEFECTS_308 named coverage: " + p.named.Count(c => c.status == "flagged") + " flagged, " + p.named.Count(c => c.status == "forced") + " forced, " + p.named.Count(c => c.status == "clear") + " clear, "
                + p.named.Count(c => c.status == "review") + " review, " + p.named.Count(c => c.status == "absent" || c.status == "removed-by-F2") + " absent, " + p.named.Count(c => c.status == "kept") + " kept (of " + p.named.Count + ")");
            foreach (var c in p.named.Where(c => c.status != "flagged")) sb.AppendLine("    " + c.status + " " + c.id + " [" + c.defect + "] " + c.action + " — " + c.note);
            foreach (var r in p.rows.Where(r => r.action == "remove" || r.action == "review").Take(40)) sb.AppendLine("    " + r.action + " " + r.kind + " " + r.id + " (" + r.reason + " @ " + r.unit + ") " + r.note);
            foreach (var n in p.notes.Take(20)) sb.AppendLine("  note " + n);
            sb.AppendLine("-> " + VegPlanFile);
            sb.AppendLine("-> " + VegPlanCsv);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ guard (read only)

        static bool VegOverlay(VegCtx308 x, List<VegRow308> rows, out int laid)
        {
            laid = 0; float tol = x.v.positionTolerance;
            var byKey = new Dictionary<string, List<VegPlace308>>();
            foreach (var p in x.places) { string k = p.sheetPath + "|" + (p.fp.Id ?? ""); if (!byKey.TryGetValue(k, out var l)) byKey[k] = l = new List<VegPlace308>(); l.Add(p); }
            foreach (var r in rows)
            {
                if (!byKey.TryGetValue(r.sheet + "|" + r.id, out var l)) continue;
                var p = l.FirstOrDefault(q => Vector3.Distance(q.pos, r.from) <= tol); if (p == null) continue;
                if (r.action == "remove") p.gone = true; else if (r.action == "move" || r.action == "reseat") { p.hasTo = true; p.to = r.to; } else continue;
                laid++;
            }
            return laid > 0;
        }

        static string VegGuard(Dictionary<string, string> opt)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var v = VegLoadConfig(out err); if (v == null) return "refused: " + err;
            string alias = VegAlias(cfg, v, opt); bool pending = opt.ContainsKey("pending");
            VegPlan308 plan = null;
            if (pending)
            {
                if (!File.Exists(VegPlanFile)) return "refused: guard:pending needs " + VegPlanFile + " (run plan first)";
                plan = JsonUtility.FromJson<VegPlan308>(File.ReadAllText(VegPlanFile));
            }
            string refuse = OpenTarget(cfg, alias, out var scene, out string previous, out bool opened);
            if (refuse != null) return refuse;
            var report = new VegGuard308 { utc = BuildingAudit308.Utc(), alias = alias, scene = scene.path, sceneSha = BuildingAudit308.Sha(BuildingAudit308.Abs(scene.path)), quality = BuildingAudit308.QualityName(), pending = pending };
            try
            {
                var x = VegBuild(cfg, v, scene);
                if (pending) { VegOverlay(x, plan.rows, out int laid); report.notes.Add("plan " + plan.utc + " (" + plan.alias + ") laid over the sheets: " + laid + " of " + plan.rows.Count + " row(s)"); }
                var near = VegNear(x); report.near = near.Count; var namedIds = new HashSet<string>(v.named.Select(n => n.id)); int outOfLimit = 0;
                foreach (var p in near)
                {
                    if (p.gone || VegKeep(v, p.fp)) continue;
                    bool isNamed = namedIds.Contains(p.fp.Id ?? "");
                    var hit = VegTest(x, p, p.Now, false, !v.floatNamedOnly || isNamed); if (hit == null) continue;
                    if (!isNamed && VegOutOfLimit(x, p.Now, hit)) { outOfLimit++; continue; }
                    var row = VegRowOf(x, p, hit, null); row.action = p.hasTo ? "planned-target" : "unplanned"; if (p.hasTo) row.to = p.to;
                    report.rows.Add(row);
                }
                if (outOfLimit > 0) report.notes.Add(outOfLimit + " un-named placement(s) touch a limited unit outside its radius (veg308.json limits) — not counted");
                report.notes.AddRange(x.notes);
            }
            finally { GoBack(previous, scene, opened, true); }
            report.violations = report.rows.Count; report.verdict = report.violations == 0 ? "PASS" : "FAIL";
            string file = Path.Combine(FixDir, "veg-guard-" + alias + (pending ? "-pending" : "") + ".json");
            VegWrite(file, report);
            var sb = new StringBuilder("veg guard " + alias + (pending ? " (pending plan)" : "") + ": " + report.verdict + " — " + report.violations + " intruding placement(s) of " + report.near + " near the footprints, scene sha " + report.sceneSha + "\n");
            foreach (var r in report.rows.Take(40))
                sb.AppendLine("  " + r.action + " " + r.kind + " " + r.id + " #" + r.index + " " + r.reason + " @ " + r.unit + " " + BuildingAudit308.V(r.action == "planned-target" ? r.to : r.from)
                    + (r.reason == "trunk" ? " foot " + r.footDistance.ToString("F2", Inv) + " m" : r.reason == "crown" ? " crown " + r.crownHits + "x " + r.crownDepth.ToString("F2", Inv) + " m" : r.reason == "floating" ? " gap " + r.groundGap.ToString("F2", Inv) + " m" : ""));
            foreach (var n in report.notes.Take(12)) sb.AppendLine("  note " + n);
            sb.AppendLine("-> " + file);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ apply / replay

        static bool VegAt(Dress308.FixedPlacement p, string id, Vector3 at, float tol) => p != null && (p.Id ?? "") == id && Vector3.Distance(p.Position, at) <= tol;

        static string VegApply(bool replay, bool fromReverted)
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var v = VegLoadConfig(out err); if (v == null) return "refused: " + err;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only (Play is running)";
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "refused: scripts are compiling or failed to compile";
            List<VegRow308> rows; List<VegArea308> areas; string areaSheet, planUtc, sceneAlias; var planSha = new Dictionary<string, string>();
            if (replay)
            {
                // replay:reverted = the mask as it stood before the newest revert (revert -> re-bake -> replay:reverted)
                string maskFile = fromReverted ? VegMaskRevertedFile : VegMaskFile;
                if (!File.Exists(maskFile)) return "refused: no " + maskFile + (fromReverted ? " (no revert was made)" : " (nothing was applied yet)");
                var mask = JsonUtility.FromJson<VegMask308>(File.ReadAllText(maskFile));
                if (mask.decisions.Count == 0 && mask.areas.Count == 0) return "refused: " + maskFile + " holds no decision" + (fromReverted ? "" : " (after a revert use replay:reverted)") + "; nothing changed";
                rows = mask.decisions; areas = mask.areas; areaSheet = mask.areaSheet; planUtc = mask.utc; sceneAlias = mask.scene;
            }
            else
            {
                if (!File.Exists(VegPlanFile)) return "refused: run plan first (" + VegPlanFile + ")";
                var plan = JsonUtility.FromJson<VegPlan308>(File.ReadAllText(VegPlanFile));
                if (plan.blocked) return "refused: the plan is BLOCKED — " + string.Join(" | ", plan.notes.Where(n => n.StartsWith("BLOCKED", StringComparison.Ordinal)));
                if (plan.configVersion != v.version) return "refused: the plan was made with config " + plan.configVersion + ", now " + v.version + "; run plan again";
                // the footprints come from the plan scene: a building fix saved there after the plan makes the plan stale
                string sceneNow = BuildingAudit308.Sha(BuildingAudit308.Abs(plan.scene));
                if (sceneNow != plan.sceneSha) return "refused: " + plan.scene + " changed since the plan (" + plan.sceneSha + " -> " + sceneNow + "); run plan again; nothing changed";
                rows = plan.rows; areas = plan.areas; areaSheet = plan.areaSheet; planUtc = plan.utc; sceneAlias = plan.alias;
                foreach (var s in plan.sheets) planSha[s.path] = s.sha;
            }
            float tol = v.positionTolerance; string prefix = v.mask.areaPrefix;
            var paths = rows.Select(r => r.sheet).Concat(string.IsNullOrEmpty(areaSheet) ? Array.Empty<string>() : new[] { areaSheet }).Where(p => !string.IsNullOrEmpty(p)).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
            // ---- verify everything first: nothing is changed unless every sheet passes
            var work = new List<(string path, Dress308 sheet, List<Dress308.FixedPlacement> list, List<Dress308.PreserveArea> areasNew, VegSheetOp308 op, bool change)>();
            foreach (string path in paths)
            {
                if (cfg.ProtectedAsset(path)) return "refused: protected sheet " + path + "; nothing changed";
                var sheet = AssetDatabase.LoadAssetAtPath<Dress308>(path); if (sheet == null) return "refused: no sheet " + path + "; nothing changed";
                if (EditorUtility.IsDirty(sheet)) return "refused: " + path + " has unsaved changes in memory (another session?); nothing changed";
                string sha = BuildingAudit308.Sha(BuildingAudit308.Abs(path));
                var op = new VegSheetOp308 { path = path, shaBefore = sha };
                var fps = (sheet.FixedPlacements ?? Array.Empty<Dress308.FixedPlacement>()).ToList(); var drop = new HashSet<int>(); var touched = new HashSet<int>();
                var mine = rows.Where(r => r.sheet == path).ToList(); var todoRows = new List<VegRow308>();
                foreach (var r in mine)
                {
                    // the row's own index first, else the one placement with this Id at the planned "from" position
                    int at = r.index >= 0 && r.index < fps.Count && VegAt(fps[r.index], r.id, r.from, tol) ? r.index : fps.FindIndex(p => VegAt(p, r.id, r.from, tol));
                    var copy = JsonUtility.FromJson<VegRow308>(JsonUtility.ToJson(r));
                    if (r.action == "remove")
                    {
                        if (at >= 0 && touched.Add(at)) { drop.Add(at); copy.index = at; copy.state = "applied"; copy.placementJson = JsonUtility.ToJson(fps[at]); op.removed++; }
                        else { copy.state = "already"; op.already++; }
                    }
                    else if (r.action == "move" || r.action == "reseat")
                    {
                        if (at >= 0 && Vector3.Distance(r.from, r.to) > tol && touched.Add(at)) { copy.index = at; copy.state = "applied"; if (r.action == "move") op.moved++; else op.reseated++; }
                        else if (fps.Any(p => VegAt(p, r.id, r.to, tol))) { copy.state = "already"; op.already++; }
                        else { copy.state = "stale"; op.stale++; }
                    }
                    else { copy.state = "skipped"; }   // "review" rows are listed only
                    op.rows.Add(copy); if (copy.state == "applied") todoRows.Add(copy);
                }
                if (op.stale > 0 && !replay)
                    return "refused: " + op.stale + " planned row(s) of " + path + " match no placement by Id + position (" + string.Join(", ", op.rows.Where(r => r.state == "stale").Take(8).Select(r => r.id)) + "); run plan again; nothing changed";
                if (!replay && todoRows.Count > 0 && planSha.TryGetValue(path, out string want) && want != sha) return "refused: " + path + " changed since the plan (" + want + " -> " + sha + "); run plan again; nothing changed";
                // the mask areas of this sheet
                var before = (sheet.PreservedAreas ?? Array.Empty<Dress308.PreserveArea>()).ToList(); var after = before;
                var oldMine = before.Where(a => a != null && a.Id != null && prefix.Length > 0 && a.Id.StartsWith(prefix, StringComparison.Ordinal)).ToList();
                op.areasBefore = oldMine.Count; op.areasBeforeJson = oldMine.Select(a => JsonUtility.ToJson(a)).ToList(); op.areasAfter = oldMine.Count; bool areaChange = false;
                if (v.mask.writePreservedAreas && prefix.Length > 0 && path == areaSheet)
                {
                    var fresh = areas.Select(a => VegPreserve(v, a)).ToList(); var freshJson = fresh.Select(a => JsonUtility.ToJson(a)).ToList();
                    if (!freshJson.SequenceEqual(op.areasBeforeJson)) { after = before.Where(a => !oldMine.Contains(a)).Concat(fresh).ToList(); areaChange = true; op.areasAfter = fresh.Count; }
                }
                // the new placement list (removals dropped; the moved objects stay in it and get their position in the write phase)
                var keep = new List<Dress308.FixedPlacement>(fps.Count);
                for (int i = 0; i < fps.Count; i++) if (!drop.Contains(i)) keep.Add(fps[i]);
                work.Add((path, sheet, keep, after, op, todoRows.Count > 0 || areaChange));
            }
            string utc = BuildingAudit308.Utc();
            var vop = new VegOp308 { utc = utc, command = replay ? "replay" : "apply", planUtc = planUtc };
            if (!work.Any(w => w.change))
            {
                foreach (var w in work) { w.op.shaAfter = w.op.shaBefore; w.op.backup = ""; vop.sheets.Add(w.op); }
                vop.status = "already"; vop.detail = "no change (idempotent); no sheet saved";
                var l0 = VegLoadLedger(); l0.ops.Add(VegSlim(vop)); VegWrite(VegLedgerFile, l0);
                return "already: veg " + vop.command + " — no change (" + work.Sum(w => w.op.already) + " row(s) already applied, " + work.Sum(w => w.op.stale) + " stale); sheets " + string.Join(", ", work.Select(w => Path.GetFileName(w.path) + " sha " + w.op.shaBefore.Substring(0, 12)));
            }
            string backupDir = Path.Combine(BuildingAudit308.Folder, "Backup", "Sheets", utc + "-veg"); Directory.CreateDirectory(backupDir);
            var shaLines = new StringBuilder();
            foreach (var w in work.Where(w => w.change))
            {
                string file = BuildingAudit308.Abs(w.path), backup = Path.Combine(backupDir, Path.GetFileName(file));
                File.Copy(file, backup, false); if (File.Exists(file + ".meta")) File.Copy(file + ".meta", backup + ".meta", false);
                shaLines.Append(w.op.shaBefore).Append("  ").Append(Path.GetFileName(file)).Append('\n');
                // moves: an applied row carries its index into the old list; the object itself is edited (it stays in the kept list)
                var old = w.sheet.FixedPlacements ?? Array.Empty<Dress308.FixedPlacement>();
                foreach (var r in w.op.rows.Where(r => r.state == "applied" && (r.action == "move" || r.action == "reseat"))) old[r.index].Position = r.to;
                w.sheet.FixedPlacements = w.list.ToArray();
                w.sheet.PreservedAreas = w.areasNew.ToArray();
                EditorUtility.SetDirty(w.sheet);
                AssetDatabase.SaveAssetIfDirty(w.sheet);
                w.op.backup = backup; w.op.shaAfter = BuildingAudit308.Sha(file);
            }
            File.WriteAllText(Path.Combine(backupDir, "sha256.txt"), shaLines.ToString(), new UTF8Encoding(false));
            foreach (var w in work) { if (!w.change) { w.op.shaAfter = w.op.shaBefore; w.op.backup = ""; } vop.sheets.Add(w.op); }
            foreach (var a in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (a.Sheet != null && work.Any(w => w.change && w.sheet == a.Sheet)) a.Invalidate();
            vop.status = "applied"; vop.detail = "moved " + work.Sum(w => w.op.moved) + ", reseated " + work.Sum(w => w.op.reseated) + ", removed " + work.Sum(w => w.op.removed) + ", already " + work.Sum(w => w.op.already) + ", stale " + work.Sum(w => w.op.stale);
            var ledger = VegLoadLedger(); ledger.ops.Add(vop); VegWrite(VegLedgerFile, ledger);
            VegWriteMask(v, ledger, areas, areaSheet, sceneAlias);
            VegWriteCsv(Path.Combine(FixDir, "veg-applied-" + utc + ".csv"), vop.sheets.SelectMany(s => s.rows).Where(r => r.state == "applied").ToList());
            var sb = new StringBuilder("applied: veg " + vop.command + " — " + vop.detail + "\n");
            foreach (var s in vop.sheets) sb.AppendLine("  " + s.path + ": moved " + s.moved + " reseated " + s.reseated + " removed " + s.removed + " already " + s.already + " stale " + s.stale + ", mask areas " + s.areasBefore + " -> " + s.areasAfter + ", sha " + s.shaBefore.Substring(0, 12) + " -> " + s.shaAfter.Substring(0, 12) + (s.backup.Length > 0 ? ", backup " + s.backup : " (unchanged)"));
            sb.AppendLine("-> " + VegLedgerFile); sb.AppendLine("-> " + VegMaskFile);
            return sb.ToString();
        }

        // an "already" op keeps no rows in the ledger (the file would grow by the whole plan on every rerun)
        static VegOp308 VegSlim(VegOp308 op)
        {
            foreach (var s in op.sheets) { s.rows = new List<VegRow308>(); s.areasBeforeJson = new List<string>(); }
            return op;
        }

        // the persistent mask: the footprint areas of the newest plan + every decision of the ops that are still in force
        static void VegWriteMask(VegCfg308 v, VegLedger308 ledger, List<VegArea308> areas, string areaSheet, string alias)
        {
            var mask = new VegMask308 { version = v.version, utc = BuildingAudit308.Utc(), scene = alias, areaSheet = areaSheet ?? "", areas = areas ?? new List<VegArea308>(),
                note = "decisions = rows applied by BuildingFix308.Veg and not reverted (Id + from = the placement a re-bake brings back); areas = footprint + class buffer rectangles (Tree|Rock)" };
            var byKey = new Dictionary<string, VegRow308>();
            foreach (var op in ledger.ops.Where(o => o.status == "applied" && !o.reverted))
                foreach (var s in op.sheets) foreach (var r in s.rows.Where(r => r.state == "applied")) byKey[r.sheet + "|" + r.id] = r;
            mask.decisions = byKey.Values.OrderBy(r => r.sheet, StringComparer.Ordinal).ThenBy(r => r.index).ToList();
            VegWrite(VegMaskFile, mask);
        }

        // ------------------------------------------------------------------ revert (row by row)

        static string VegRevert()
        {
            var cfg = BuildingAudit308.LoadConfig(out string err); if (cfg == null) return "refused: " + err;
            var v = VegLoadConfig(out err); if (v == null) return "refused: " + err;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only (Play is running)";
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "refused: scripts are compiling or failed to compile";
            var ledger = VegLoadLedger();
            var op = ledger.ops.LastOrDefault(o => o.status == "applied" && !o.reverted);
            if (op == null) return "refused: nothing to revert in " + VegLedgerFile;
            float tol = v.positionTolerance; string prefix = v.mask.areaPrefix;
            var changed = op.sheets.Where(s => s.shaAfter != s.shaBefore).ToList();
            foreach (var s in changed)
            {
                if (cfg.ProtectedAsset(s.path)) return "refused: protected sheet " + s.path;
                var sheet = AssetDatabase.LoadAssetAtPath<Dress308>(s.path); if (sheet == null) return "refused: no sheet " + s.path + "; nothing changed";
                if (EditorUtility.IsDirty(sheet)) return "refused: " + s.path + " has unsaved changes in memory; nothing changed";
            }
            string utc = BuildingAudit308.Utc(); string backupDir = Path.Combine(BuildingAudit308.Folder, "Backup", "Sheets", utc + "-veg-revert"); Directory.CreateDirectory(backupDir);
            var sb = new StringBuilder();
            foreach (var s in changed)
            {
                var sheet = AssetDatabase.LoadAssetAtPath<Dress308>(s.path); string file = BuildingAudit308.Abs(s.path); string before = BuildingAudit308.Sha(file);
                File.Copy(file, Path.Combine(backupDir, Path.GetFileName(file)), false);
                var fps = (sheet.FixedPlacements ?? Array.Empty<Dress308.FixedPlacement>()).ToList(); int back = 0, inserted = 0, missing = 0;
                foreach (var r in s.rows.Where(r => r.state == "applied" && (r.action == "move" || r.action == "reseat")))
                {
                    var p = fps.FirstOrDefault(q => VegAt(q, r.id, r.to, tol));
                    if (p != null) { p.Position = r.from; back++; } else if (!fps.Any(q => VegAt(q, r.id, r.from, tol))) missing++;
                }
                foreach (var r in s.rows.Where(r => r.state == "applied" && r.action == "remove").OrderBy(r => r.index))
                {
                    if (fps.Any(q => VegAt(q, r.id, r.from, tol))) continue;
                    var p = string.IsNullOrEmpty(r.placementJson) ? null : JsonUtility.FromJson<Dress308.FixedPlacement>(r.placementJson);
                    if (p == null) { missing++; continue; }
                    fps.Insert(Mathf.Clamp(r.index, 0, fps.Count), p); inserted++;
                }
                var areasNow = (sheet.PreservedAreas ?? Array.Empty<Dress308.PreserveArea>()).Where(a => a == null || a.Id == null || prefix.Length == 0 || !a.Id.StartsWith(prefix, StringComparison.Ordinal)).ToList();
                foreach (string j in s.areasBeforeJson) { var a = JsonUtility.FromJson<Dress308.PreserveArea>(j); if (a != null) areasNow.Add(a); }
                sheet.FixedPlacements = fps.ToArray(); sheet.PreservedAreas = areasNow.ToArray();
                EditorUtility.SetDirty(sheet); AssetDatabase.SaveAssetIfDirty(sheet);
                string after = BuildingAudit308.Sha(file);
                sb.AppendLine("  " + s.path + ": moved back " + back + ", re-inserted " + inserted + (missing > 0 ? ", NOT FOUND " + missing : "") + ", sha " + before.Substring(0, 12) + " -> " + after.Substring(0, 12)
                    + (after == s.shaBefore ? " (= the SHA before the apply)" : " (differs from the SHA before the apply " + s.shaBefore.Substring(0, 12) + ": other edits are present or the row order moved)"));
                foreach (var a in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (a.Sheet == sheet) a.Invalidate();
            }
            op.reverted = true; op.revertedUtc = utc;
            ledger.ops.Add(new VegOp308 { utc = utc, command = "revert", status = "reverted", detail = "op " + op.utc + " (" + op.command + ")", planUtc = op.planUtc });
            VegWrite(VegLedgerFile, ledger);
            if (File.Exists(VegMaskFile)) File.Copy(VegMaskFile, VegMaskRevertedFile, true);   // kept for replay:reverted (revert -> re-bake -> replay)
            VegMask308 old = File.Exists(VegMaskFile) ? JsonUtility.FromJson<VegMask308>(File.ReadAllText(VegMaskFile)) : null;
            bool anyLeft = ledger.ops.Any(o => o.status == "applied" && !o.reverted);
            VegWriteMask(v, ledger, anyLeft && old != null ? old.areas : new List<VegArea308>(), anyLeft && old != null ? old.areaSheet : "", old != null ? old.scene : "");
            return "reverted: veg op " + op.utc + " (" + op.command + ")\n" + sb + "  backup of the state before this revert: " + backupDir + "\n  the reverted decisions stay in " + VegMaskRevertedFile + " (replay:reverted)\n-> " + VegLedgerFile;
        }

        // ------------------------------------------------------------------ status

        static string VegStatus()
        {
            var cfg = BuildingAudit308.LoadConfig(out string err);
            var v = VegLoadConfig(out string verr);
            var sb = new StringBuilder("BuildingFix308.Veg status " + DateTime.UtcNow.ToString("O", Inv) + "\n");
            sb.AppendLine("config: " + (v != null ? v.version + " (" + v.status + ") roots " + v.roots.Length + ", classes " + string.Join("/", v.classes.Select(c => c.name + " " + c.buffer.ToString("0.##", Inv))) + ", named " + v.named.Length : verr));
            if (cfg == null) sb.AppendLine("audit config: " + err);
            if (File.Exists(VegPlanFile))
            {
                var p = JsonUtility.FromJson<VegPlan308>(File.ReadAllText(VegPlanFile));
                sb.AppendLine("plan " + p.utc + " (" + p.alias + "): rows " + p.rows.Count + " (move " + p.move + ", remove " + p.remove + ", reseat " + p.reseat + "), areas " + p.areas.Count + (p.blocked ? " BLOCKED" : ""));
                foreach (var s in p.sheets)
                {
                    string now = BuildingAudit308.Sha(BuildingAudit308.Abs(s.path));
                    sb.AppendLine("  sheet " + s.path + " plan sha " + (s.sha.Length >= 12 ? s.sha.Substring(0, 12) : s.sha) + " now " + (now.Length >= 12 ? now.Substring(0, 12) : now) + (now == s.sha ? " (same)" : " (CHANGED since the plan)"));
                }
            }
            else sb.AppendLine("plan: none (" + VegPlanFile + ")");
            var ledger = VegLoadLedger();
            sb.AppendLine("ledger ops: " + ledger.ops.Count);
            foreach (var o in ledger.ops.Skip(Math.Max(0, ledger.ops.Count - 8))) sb.AppendLine("  " + o.utc + " " + o.command + " " + o.status + (o.reverted ? " (reverted " + o.revertedUtc + ")" : "") + ": " + o.detail);
            if (File.Exists(VegMaskFile)) { var m = JsonUtility.FromJson<VegMask308>(File.ReadAllText(VegMaskFile)); sb.AppendLine("mask: " + m.decisions.Count + " decision(s), " + m.areas.Count + " area(s), area sheet " + m.areaSheet); }
            else sb.AppendLine("mask: none");
            return sb.ToString();
        }
    }
}
