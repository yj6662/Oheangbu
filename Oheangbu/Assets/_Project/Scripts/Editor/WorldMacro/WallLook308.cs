using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 P0 look gate (SPEC-WORLD-CLIFF-BOUNDARY-308 section 6, D308-3c): an in-memory preview of the closed pass gate and
    // the long wall running to the cliffs, built from the owned pack Assets/HwaseongForteressGate on the plan.2 line inside
    // the open W_Demo_Main world. Nothing is saved: every object, mesh and material made here carries HideFlags.DontSave, no
    // asset is created, the scene is never saved, no existing object is changed (optional forest hiding = a Behaviour.enabled
    // flag that preview:off restores). The layout comes from look/tools/wall1_layout.py (look/wall/wall_layout.json); the
    // Q3 = C study lines (variant "mid") and the gate-foot numbers come from Tools/Art/jangseong308.py layout
    // (look/wall/wall_layout_mid.json, optional: without it the tool behaves as before). The preview keeps no state in
    // statics, only in its own scene objects.
    // Queue: Oheangbu.EditorTools.WorldMacro.WallLook308 Run "<command>"
    //   preview:on[:line=foot|mid|crest][:mat=pack|ink][:roof=1|2|12][:gate=centre|door][:cap=0.45][:nosink][:nochi][:hide=<Behaviour path>][:allowdirty]
    //                   gate=door = gate-foot proposal: the block floor is set from the lowest terrain along the door plane
    //   preview:off     remove everything, restore what was disabled and the quality level (while a cliff preview is up the level
    //                   stays for it; if that cliff preview went up after this one, this preview's older quality record is handed to it)
    //   status          modules up, counts, per-module seat + A1 table (re-measured on the real terrain colliders), scene dirty flag
    //   ground:<x>,<z>[;<x>,<z>...]     physics ground (terrain tile colliders) under points
    //   near:<x>,<z>:<radius>           what stands there now (renderer groups, instanced dressing renderers)
    // Phase 1b (stage Stage308_cliff1b/wall): the wall ledger's own preview (Jangseong308 preview:on) is built through OnExternal
    // below, so both previews share ONE root, ONE quality record and the order rule cliff on -> wall on -> wall off -> cliff off.
    // preview:off and status of this class serve both.
    public static class WallLook308
    {
        const string RootName = "WallLook308_Preview";
        const string CliffRootName = "CliffLook308_Preview";
        const string LayoutFile = "Art/World/Compact/Rebuild/CliffBoundary308/look/wall/wall_layout.json";
        const string ExtraFile = "Art/World/Compact/Rebuild/CliffBoundary308/look/wall/wall_layout_mid.json";
        const string TerrainRoot = "Reworld292_Terrain";
        const string InkShader = "Oheangbu/Finish297/KoreanArchitecture";
        const HideFlags Flags = HideFlags.DontSave;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [Serializable] sealed class Piece { public string prefab, tag; public float x, y, z, yaw, sx, sy, sz; }
        [Serializable] sealed class Box { public string tag, materialFrom; public float cx, cy, cz, sx, sy, sz; }
        [Serializable] sealed class Line { public string id, variant, label, prefix; public bool lateLeft; public float[] pts, bendsDeg; public float length, planLength, cliffEntry, cliffFillAtEnd; public int planModules, planFrom; }
        [Serializable] sealed class Layout
        {
            public string version, packPrefabDir, wallPrefab, parapetPrefab;
            public float moduleLength, moduleHeight, moduleHalfDepth, parapetY, parapetZ, parapetTop, gateX, gateZ, lateX, lateZ, gateHalfWidth, gateTop, gateScale;
            public float gradeCap, bendBastionDeg, gradeBreakBastion, a1Radius, a1SideMin, a1Limit, deckRadius, topOverEaMin, eaProbe;
            public Piece[] gatePieces, chiPieces; public Box[] gateBoxes, chiBoxes; public Line[] lines;
            [NonSerialized] public float doorPlaneZ, doorHalfWidth, doorBottom, doorBury;   // from the extra file; doorHalfWidth 0 = no gate-foot numbers
        }
        [Serializable] sealed class Extra { public string version; public Line[] lines; public float doorPlaneZ, doorHalfWidth, doorBottom, doorBury; }

        sealed class Opt { public string line = "foot", mat = "pack", roof = "2", hide = "", gate = "centre"; public float cap = -1f; public bool sink = true, chi = true; }

        sealed class Module
        {
            public string id, line; public int leg; public bool gateSpan, inCliff;
            public Vector2 a, b, late; public float length, scale, arc;
            public float naturalA, naturalB, baseA, baseB, groundCentre, floatMax, buryMax, topOverEa, lateHigh, late16, lateBelowTop;
            public float[] sampleU, sampleGround;
            public Vector2 Centre => (a + b) * .5f;
            public float BaseCentre => (baseA + baseB) * .5f;
            public float GradeNatural => (naturalB - naturalA) / length;
            public float GradeBuilt => (baseB - baseA) / length;
            public float Cut => Mathf.Max(naturalA - baseA, naturalB - baseB);
        }

        sealed class Chi { public string id, reason; public Vector2 at, late; public float baseY, groundFront; }

        sealed class Plan
        {
            public readonly List<Module> Modules = new List<Module>(); public readonly List<Chi> Chis = new List<Chi>();
            public float GateFloor; public string GateGround = ""; public readonly List<string> Notes = new List<string>();
        }

        public static string Run(string command)
        {
            string[] a = (command ?? "").Split(':');
            try
            {
                switch (a[0])
                {
                    case "preview":
                        if (a.Length > 1 && a[1] == "on") return On(a);
                        if (a.Length > 1 && a[1] == "off") return Off();
                        throw new PostLedger308.Refused("use preview:on[:line=foot|mid|crest][:mat=pack|ink][:gate=centre|door] or preview:off");
                    case "status": return Status();
                    case "ground": return Ground(a.Length > 1 ? a[1] : "");
                    case "near": return Near(a);
                    default: throw new PostLedger308.Refused("unknown command '" + command + "'");
                }
            }
            catch (PostLedger308.Refused r) { return "refused: " + r.Message; }
        }

        // ---------------------------------------------------------------- preview on / off

        static string On(string[] a)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
            var raw = PostLedger308.Options(a.Skip(2));
            var opt = new Opt();
            if (raw.TryGetValue("line", out var ln) && ln.Length > 0) opt.line = ln;
            if (raw.TryGetValue("mat", out var mt) && mt.Length > 0) opt.mat = mt;
            if (raw.TryGetValue("roof", out var rf) && rf.Length > 0) opt.roof = rf;
            if (raw.TryGetValue("hide", out var hd) && hd.Length > 0) opt.hide = hd;
            if (raw.TryGetValue("gate", out var gt) && gt.Length > 0) opt.gate = gt;
            if (raw.TryGetValue("cap", out var cp) && cp.Length > 0) opt.cap = float.Parse(cp, Inv);
            opt.sink = !raw.ContainsKey("nosink"); opt.chi = !raw.ContainsKey("nochi");
            if (opt.mat != "pack" && opt.mat != "ink") throw new PostLedger308.Refused("mat must be pack or ink");
            if (opt.gate != "centre" && opt.gate != "door") throw new PostLedger308.Refused("gate must be centre or door");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != PostLedger308.Main) throw new PostLedger308.Refused("active scene is '" + scene.path + "'; the preview is built in " + PostLedger308.Main);
            if (scene.isDirty && !raw.ContainsKey("allowdirty")) throw new PostLedger308.Refused("the scene has unsaved changes that are not from this tool (add :allowdirty to preview anyway)");
            var layout = Load();   // every file read happens before the first change
            var variants = layout.lines.SelectMany(l => l.variant.Split(',')).Distinct().OrderBy(v => v).ToArray();
            if (!variants.Contains(opt.line)) throw new PostLedger308.Refused("line must be one of " + string.Join("|", variants) + " (mid needs " + ExtraFile + " from Tools/Art/jangseong308.py layout)");
            if (opt.gate == "door" && layout.doorHalfWidth <= 0f) throw new PostLedger308.Refused("gate=door needs the gate-foot numbers of " + ExtraFile);
            if (opt.cap < 0f) opt.cap = layout.gradeCap;
            Behaviour hide = null;
            if (opt.hide.Length > 0)
            {
                hide = ResolveBehaviour(opt.hide);
                if (hide == null) throw new PostLedger308.Refused("no Behaviour at '" + opt.hide + "' (path/Type)");
                if (PostLedger308.UnderProtectedTree(hide.transform)) throw new PostLedger308.Refused("'" + opt.hide + "' is under a protected tree");
            }

            int prior = QualitySettings.GetQualityLevel();
            int cliffPrior = CliffPrior();
            if (cliffPrior >= 0) prior = cliffPrior;   // the cliff preview already switched to PC: keep the level that was there before it
            bool dirtyAtOn = scene.isDirty;
            var log = new StringBuilder();
            Plan plan = null; GameObject root = null; bool disabled = false;
            try
            {
                // The plan is pure measurement on the terrain colliders and runs before anything is torn down or switched: when it
                // fails, an earlier preview stays up with its own state record and the quality level is exactly what it was.
                Physics.SyncTransforms();
                plan = Compute(layout, opt);
                // From here on a failure restores the quality level recorded by the preview this one replaces (catch below).
                if (FindRoots().Count > 0) { log.AppendLine("replaced an earlier preview: " + Teardown(false, out int carried)); if (carried >= 0) prior = carried; }
                root = Make(RootName, null);
                var stats = Build(layout, opt, plan, root);
                if (hide != null && hide.enabled) { Make("Disabled|" + opt.hide, root.transform); hide.enabled = false; disabled = true; }
                int pc = Array.IndexOf(QualitySettings.names, "PC");
                if (pc < 0) throw new PostLedger308.Refused("no quality level named PC");
                if (pc != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(pc, true);
                Make("State|q=" + prior + "|line=" + opt.line + "|mat=" + opt.mat + "|roof=" + opt.roof + "|gate=" + opt.gate + "|cap=" + opt.cap.ToString("0.###", Inv) + "|sink=" + (opt.sink ? 1 : 0) + "|chi=" + (opt.chi ? 1 : 0)
                    + "|dirtyAtOn=" + dirtyAtOn + "|utc=" + PostLedger308.Utc(), root.transform);
                log.AppendLine(stats);
            }
            catch
            {
                if (disabled && hide != null) hide.enabled = true;
                if (QualitySettings.GetQualityLevel() != prior && cliffPrior < 0) QualitySettings.SetQualityLevel(prior, true);
                Destroy(root);
                throw;
            }
            log.AppendLine(Summary(layout, opt, plan));
            log.AppendLine("quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " (recorded prior level " + prior + " " + QualitySettings.names[prior] + (cliffPrior >= 0 ? ", taken from the cliff preview's record" : "") + ")");
            log.Append("preview up: line=" + opt.line + " mat=" + opt.mat + " roof=" + opt.roof + " gate=" + opt.gate + (disabled ? " hidden=" + opt.hide : "") + " | scene dirty=" + scene.isDirty);
            return log.ToString();
        }

        /// <summary>The preview shell for a build made by another tool of this track (Jangseong308 preview:on): the same guards, the one
        /// root named WallLook308_Preview, PC quality with the recorded prior level and the state record that preview:off reads.
        /// `build` fills the root (everything under it is flagged DontSave here); a failure removes the root and restores the level.</summary>
        internal static string OnExternal(string stateTag, bool allowDirty, Action<GameObject> build)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
            var scene = SceneManager.GetActiveScene();
            if (!PostLedger308.Scenes.Contains(scene.path)) throw new PostLedger308.Refused("active scene '" + scene.path + "' is not a #308 target scene");
            if (scene.isDirty && !allowDirty) throw new PostLedger308.Refused("the scene has unsaved changes that are not from this tool (add :allowdirty to preview anyway)");
            int prior = QualitySettings.GetQualityLevel();
            int cliffPrior = CliffPrior();
            if (cliffPrior >= 0) prior = cliffPrior;   // the cliff preview already switched to PC: keep the level that was there before it
            bool dirtyAtOn = scene.isDirty;
            var log = new StringBuilder(); GameObject root = null;
            try
            {
                Physics.SyncTransforms();
                if (FindRoots().Count > 0) { log.AppendLine("replaced an earlier preview: " + Teardown(false, out int carried)); if (carried >= 0) prior = carried; }
                root = Make(RootName, null);
                build(root);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = Flags;
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)) r.allowOcclusionWhenDynamic = false;
                int pc = Array.IndexOf(QualitySettings.names, "PC");
                if (pc < 0) throw new PostLedger308.Refused("no quality level named PC");
                if (pc != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(pc, true);
                Make("State|q=" + prior + "|" + stateTag + "|dirtyAtOn=" + dirtyAtOn + "|utc=" + PostLedger308.Utc(), root.transform);
            }
            catch
            {
                if (QualitySettings.GetQualityLevel() != prior && cliffPrior < 0) QualitySettings.SetQualityLevel(prior, true);
                Destroy(root);
                throw;
            }
            log.AppendLine("quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " (recorded prior level " + prior + " " + QualitySettings.names[prior] + (cliffPrior >= 0 ? ", taken from the cliff preview's record" : "") + ")");
            log.Append("preview up: " + stateTag + " | scene dirty=" + scene.isDirty);
            return log.ToString();
        }

        static string Off()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (FindRoots().Count == 0) return "preview: none to remove | " + QualityText() + " | scene dirty=" + SceneManager.GetActiveScene().isDirty;
            string text = Teardown(true, out _);
            return "preview off: " + text + " | " + QualityText() + " | scene dirty=" + SceneManager.GetActiveScene().isDirty;
        }

        static string QualityText() => "quality " + QualitySettings.GetQualityLevel() + " (" + QualitySettings.names[QualitySettings.GetQualityLevel()] + ")";

        /// <summary>Removes every preview object with its in-memory meshes and materials, re-enables what it disabled and (optionally) restores the recorded quality level.</summary>
        static string Teardown(bool restoreQuality, out int priorQuality)
        {
            priorQuality = -1;
            int restored = 0, unresolved = 0, objects = 0, meshes = 0, materials = 0;
            foreach (var root in FindRoots())
            {
                foreach (Transform c in root.transform)
                {
                    if (c.name.StartsWith("State|", StringComparison.Ordinal)) { int q = StateInt(c.name, "q"); if (q >= 0) priorQuality = q; }
                    else if (c.name.StartsWith("Disabled|", StringComparison.Ordinal))
                    {
                        var b = ResolveBehaviour(c.name.Substring(9));
                        if (b != null) { b.enabled = true; restored++; } else unresolved++;
                    }
                }
                objects += root.GetComponentsInChildren<Transform>(true).Length;
                Release(root, out int m, out int mt); meshes += m; materials += mt;
                Object.DestroyImmediate(root);
            }
            int cliffPrior = CliffPrior(); bool cliffUp = cliffPrior >= 0; bool valid = priorQuality >= 0 && priorQuality < QualitySettings.names.Length;
            if (restoreQuality && !cliffUp && valid && QualitySettings.GetQualityLevel() != priorQuality) QualitySettings.SetQualityLevel(priorQuality, true);
            // Order guard: a cliff preview that went up AFTER this one found the editor already on PC and recorded PC as "the level
            // before", so its preview:off would leave the editor on PC. This preview's record is the older one: hand it over
            // (the record is the name of a DontSave object of the cliff preview; nothing is saved).
            string handed = "";
            if (restoreQuality && cliffUp && valid && cliffPrior != priorQuality)
                handed = HandOverQuality(priorQuality) > 0
                    ? "; the cliff preview had recorded quality " + cliffPrior + " because it went up after this one - its record now says " + priorQuality
                    : "; THE CLIFF PREVIEW RECORDS QUALITY " + cliffPrior + " BUT THE LEVEL BEFORE THE PREVIEWS WAS " + priorQuality;
            return objects + " objects, " + meshes + " in-memory meshes and " + materials + " in-memory materials destroyed, " + restored + " behaviours re-enabled" + (unresolved > 0 ? ", " + unresolved + " DISABLED BEHAVIOURS NOT FOUND" : "")
                + (priorQuality >= 0 ? ", recorded prior quality " + priorQuality : ", no recorded quality") + (cliffUp && restoreQuality ? " (quality left as it is: the cliff preview is still up and restores it" + handed + ")" : "");
        }

        /// <summary>Writes the quality level into the q= field of the cliff preview's state record. Returns the number of records rewritten.</summary>
        static int HandOverQuality(int quality)
        {
            int rewritten = 0;
            foreach (var root in FindRoots(CliffRootName))
                foreach (Transform c in root.transform)
                {
                    if (!c.name.StartsWith("State|", StringComparison.Ordinal)) continue;
                    string[] parts = c.name.Split('|');
                    for (int i = 0; i < parts.Length; i++)
                        if (parts[i].StartsWith("q=", StringComparison.Ordinal)) { parts[i] = "q=" + quality.ToString(Inv); rewritten++; }
                    c.name = string.Join("|", parts);
                }
            return rewritten;
        }

        static void Release(GameObject root, out int meshes, out int materials)
        {
            meshes = 0; materials = 0;
            var seenMesh = new HashSet<Mesh>(); var seenMat = new HashSet<Material>();
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null && !EditorUtility.IsPersistent(f.sharedMesh)) seenMesh.Add(f.sharedMesh);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && !EditorUtility.IsPersistent(m)) seenMat.Add(m);
            foreach (var m in seenMesh) { Object.DestroyImmediate(m); meshes++; }
            foreach (var m in seenMat) { Object.DestroyImmediate(m); materials++; }
        }

        static void Destroy(GameObject root)
        {
            if (root == null) return;
            Release(root, out _, out _);
            Object.DestroyImmediate(root);
        }

        // ---------------------------------------------------------------- plan (pure measurement, no scene change)

        static Layout Load()
        {
            string file = PostLedger308.RepoPath(LayoutFile);
            if (!File.Exists(file)) throw new PostLedger308.Refused("no layout " + file + " (run look/tools/wall1_layout.py)");
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(file, Encoding.UTF8));
            if (layout == null || layout.lines == null || layout.lines.Length == 0 || layout.gatePieces == null || layout.moduleLength <= 0f) throw new PostLedger308.Refused("layout " + file + " is empty");
            string extraFile = PostLedger308.RepoPath(ExtraFile);
            if (File.Exists(extraFile))
            {
                var extra = JsonUtility.FromJson<Extra>(File.ReadAllText(extraFile, Encoding.UTF8));
                if (extra == null) throw new PostLedger308.Refused("extra layout " + extraFile + " is empty");
                if (extra.lines != null) layout.lines = layout.lines.Concat(extra.lines.Where(x => x != null && x.pts != null && layout.lines.All(l => l.id != x.id))).ToArray();
                layout.doorPlaneZ = extra.doorPlaneZ; layout.doorHalfWidth = extra.doorHalfWidth; layout.doorBottom = extra.doorBottom; layout.doorBury = extra.doorBury;
            }
            return layout;
        }

        static Plan Compute(Layout layout, Opt opt)
        {
            var plan = new Plan();
            if (!TerrainY(layout.gateX, layout.gateZ, out float centreY)) throw new PostLedger308.Refused("no terrain collider under the gate centre");
            plan.GateFloor = centreY;
            var late = new Vector2(layout.lateX, layout.lateZ); var along = new Vector2(-late.y, late.x);
            float zEdge = (layout.gatePieces.Max(p => Mathf.Abs(p.z)) + 1f);
            // terrain along the door plane (the leaves stand at local z = doorPlaneZ, across the passage) and along the passage centreline
            string door = ""; var gate2 = new Vector2(layout.gateX, layout.gateZ);
            if (layout.doorHalfWidth > 0f)
            {
                float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                for (float x = -layout.doorHalfWidth; x <= layout.doorHalfWidth + .001f; x += .5f)
                {
                    var q = gate2 + along * x + late * layout.doorPlaneZ;
                    if (!TerrainY(q.x, q.y, out float gy)) throw new PostLedger308.Refused("no terrain collider under the door plane");
                    lo = Mathf.Min(lo, gy); hi = Mathf.Max(hi, gy);
                }
                if (opt.gate == "door") plan.GateFloor = lo - layout.doorBury - layout.doorBottom;   // the leaf bottom ends doorBury below the lowest ground it stands over
                float leaf = plan.GateFloor + layout.doorBottom;
                var profile = new List<string>();
                for (int k = -2; k <= 2; k++)
                {
                    var q = gate2 + late * (k * zEdge * .5f);
                    profile.Add((k * zEdge * .5f).ToString("+0.0;-0.0", Inv) + ":" + (TerrainY(q.x, q.y, out float gy) ? (gy - plan.GateFloor).ToString("+0.00;-0.00", Inv) : "none"));
                }
                door = " | door leaves (local z " + layout.doorPlaneZ.ToString("0.00", Inv) + ", bottom = floor " + layout.doorBottom.ToString("+0.00;-0.00", Inv) + "): terrain along the door plane " + lo.ToString("F2", Inv) + ".." + hi.ToString("F2", Inv)
                    + ", gap under the leaves " + (leaf - hi).ToString("+0.00;-0.00", Inv) + ".." + (leaf - lo).ToString("+0.00;-0.00", Inv) + " m (negative = sunk)"
                    + " | passage centreline terrain relative to the floor, EA end -> late end (local z:dy): " + string.Join(" ", profile);
            }
            var corners = new List<string>();
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                {
                    var q = new Vector2(layout.gateX, layout.gateZ) + along * (sx * layout.gateHalfWidth) + late * (sz * zEdge);
                    corners.Add((sz > 0 ? "late" : "EA") + (sx > 0 ? "+" : "-") + " " + (TerrainY(q.x, q.y, out float gy) ? (gy - plan.GateFloor).ToString("+0.00;-0.00", Inv) : "none"));
                }
            plan.GateGround = "floor y " + plan.GateFloor.ToString("F2", Inv) + (opt.gate == "door"
                    ? " (gate=door: lowest door-plane terrain - " + layout.doorBury.ToString("0.00", Inv) + " bury - door bottom; terrain under the passage centre is " + centreY.ToString("F2", Inv) + ", floor lowered by " + (centreY - plan.GateFloor).ToString("F2", Inv) + " m)"
                    : " (terrain under the passage centre)")
                + "; ground at the four block corners relative to the floor: " + string.Join(", ", corners) + " m" + door;

            var lattice = new Dictionary<long, float>();
            foreach (var line in layout.lines)
            {
                if (!line.variant.Split(',').Contains(opt.line)) continue;
                PlanLine(layout, line, opt, plan, lattice);
            }
            if (plan.Modules.Count == 0) throw new PostLedger308.Refused("no line of variant '" + opt.line + "' in the layout");
            return plan;
        }

        static void PlanLine(Layout layout, Line line, Opt opt, Plan plan, Dictionary<long, float> lattice)
        {
            int legs = line.pts.Length / 2 - 1;
            if (legs < 1) throw new PostLedger308.Refused("line " + line.id + " has fewer than two points");
            var p = new Vector2[legs + 1];
            for (int i = 0; i <= legs; i++) p[i] = new Vector2(line.pts[i * 2], line.pts[i * 2 + 1]);
            var dir = new Vector2[legs]; var lateOf = new Vector2[legs];
            for (int k = 0; k < legs; k++) { dir[k] = (p[k + 1] - p[k]).normalized; lateOf[k] = line.lateLeft ? new Vector2(-dir[k].y, dir[k].x) : new Vector2(dir[k].y, -dir[k].x); }
            var bend = new float[legs + 1];
            for (int k = 1; k < legs; k++) bend[k] = Vector2.SignedAngle(dir[k - 1], dir[k]);
            float hd = layout.moduleHalfDepth;
            float Ext(float degrees) => Mathf.Min(1.5f, hd * Mathf.Tan(Mathf.Abs(degrees) * Mathf.Deg2Rad * .5f));   // legs overlap at a bend so the outer corner closes

            var mods = new List<Module>(); var natural = new List<float>(); var vertexJoint = new int[legs + 1];
            float arc0 = 0f; string prefix = !string.IsNullOrEmpty(line.prefix) ? line.prefix : line.id == "west" ? "W" : line.id == "east" ? "E" : "C";
            for (int k = 0; k < legs; k++)
            {
                float e0 = k > 0 ? Ext(bend[k]) : 0f, e1 = k < legs - 1 ? Ext(bend[k + 1]) : 0f;
                Vector2 s = p[k] - dir[k] * e0, e = p[k + 1] + dir[k] * e1; float len = (e - s).magnitude;
                int n = Mathf.Max(1, Mathf.RoundToInt(len / layout.moduleLength));
                if (len / (n * layout.moduleLength) > 1.12f) n++;
                for (int j = 0; j <= n; j++)
                {
                    Vector2 q = Vector2.Lerp(s, e, j / (float)n); float h = FootMin(q, lateOf[k], hd);
                    if (j == 0 && k > 0) natural[natural.Count - 1] = Mathf.Min(natural[natural.Count - 1], h);   // the joint shared with the previous leg
                    else natural.Add(h);
                    if (j == n) break;
                    var m = new Module { line = line.id, leg = k, gateSpan = k < line.planFrom, a = q, b = Vector2.Lerp(s, e, (j + 1) / (float)n), late = lateOf[k], length = len / n, scale = len / (n * layout.moduleLength), arc = arc0 - e0 + len * (j + .5f) / n };
                    m.id = prefix + (mods.Count + 1).ToString("00"); m.inCliff = line.cliffEntry > 0f && m.arc >= line.cliffEntry;
                    mods.Add(m);
                }
                vertexJoint[k + 1] = natural.Count - 1;
                arc0 += (p[k + 1] - p[k]).magnitude;
            }
            if (natural.Any(float.IsNaN)) throw new PostLedger308.Refused("line " + line.id + ": a joint has no terrain collider under it");

            // footprint samples (5 along x 3 across) for the float / bury measure
            foreach (var m in mods)
            {
                m.sampleU = new float[15]; m.sampleGround = new float[15];
                for (int i = 0; i < 5; i++)
                    for (int c = 0; c < 3; c++)
                    {
                        float u = (i + .5f) / 5f; var q = Vector2.Lerp(m.a, m.b, u) + m.late * ((c - 1) * hd);
                        m.sampleU[i * 3 + c] = u; m.sampleGround[i * 3 + c] = TerrainY(q.x, q.y, out float gy) ? gy : float.NaN;
                    }
            }

            // joint heights: natural footprint minimum -> grade cap by lowering (what the bench op would cut) -> lowered until nothing floats
            var built = natural.ToArray();
            void Cap()
            {
                if (opt.cap <= 0f) return;
                for (int i = 1; i < built.Length; i++) built[i] = Mathf.Min(built[i], built[i - 1] + opt.cap * mods[i - 1].length);
                for (int i = built.Length - 2; i >= 0; i--) built[i] = Mathf.Min(built[i], built[i + 1] + opt.cap * mods[i].length);
            }
            float Float(int i)
            {
                float worst = 0f; var m = mods[i];
                for (int s = 0; s < 15; s++) if (!float.IsNaN(m.sampleGround[s])) worst = Mathf.Max(worst, Mathf.Lerp(built[i], built[i + 1], m.sampleU[s]) - m.sampleGround[s]);
                return worst;
            }
            Cap();
            for (int pass = 0; opt.sink && pass < 6; pass++)
            {
                bool moved = false;
                for (int i = 0; i < mods.Count; i++) { float f = Float(i); if (f > .02f) { built[i] -= f; built[i + 1] -= f; moved = true; } }
                Cap();
                if (!moved) break;
            }

            float radius = layout.a1Radius;
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i]; m.naturalA = natural[i]; m.naturalB = natural[i + 1]; m.baseA = built[i]; m.baseB = built[i + 1];
                m.floatMax = 0f; m.buryMax = 0f;
                for (int s = 0; s < 15; s++)
                {
                    if (float.IsNaN(m.sampleGround[s])) continue;
                    float gap = Mathf.Lerp(m.baseA, m.baseB, m.sampleU[s]) - m.sampleGround[s];
                    m.floatMax = Mathf.Max(m.floatMax, gap); m.buryMax = Mathf.Max(m.buryMax, -gap);
                }
                var c = m.Centre;
                m.groundCentre = TerrainY(c.x, c.y, out float gc) ? gc : float.NaN;
                var ea = c - m.late * (hd + layout.eaProbe);
                m.topOverEa = TerrainY(ea.x, ea.y, out float ge) ? m.BaseCentre + layout.moduleHeight - ge : float.NaN;
                // A1 (plan/tools/step4_review.py wall_check): highest late-side ground on the 4 m lattice within the radius and more than a1SideMin behind the line
                m.lateHigh = float.NegativeInfinity; m.late16 = float.NegativeInfinity; int lateCells = 0, lateBelow = 0; float wallTop = m.BaseCentre + layout.parapetTop;
                int x0 = Mathf.FloorToInt((c.x - radius) / 4f), x1 = Mathf.CeilToInt((c.x + radius) / 4f), z0 = Mathf.FloorToInt((c.y - radius) / 4f), z1 = Mathf.CeilToInt((c.y + radius) / 4f);
                for (int iz = z0; iz <= z1; iz++)
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        var d = new Vector2(ix * 4f, iz * 4f) - c; float dist = d.magnitude;
                        if (dist > radius || Vector2.Dot(d, m.late) <= layout.a1SideMin) continue;
                        long key = ((long)ix << 32) ^ (uint)iz;
                        if (!lattice.TryGetValue(key, out float y)) { y = TerrainY(ix * 4f + .02f, iz * 4f + .02f, out float ly) ? ly : float.NaN; lattice[key] = y; }
                        if (float.IsNaN(y)) continue;
                        lateCells++; if (y <= wallTop) lateBelow++;
                        if (y > m.lateHigh) m.lateHigh = y;
                        if (dist <= layout.deckRadius && y > m.late16) m.late16 = y;
                    }
                m.lateBelowTop = lateCells > 0 ? lateBelow / (float)lateCells : float.NaN;
            }
            plan.Modules.AddRange(mods);

            if (!opt.chi) return;
            // chi (bastions): bends over the limit, the cliff joint, grade breaks over the limit - never closer than 1.5 modules to another one
            var chis = new List<Chi>();
            bool Free(Vector2 at) => chis.All(c => (c.at - at).magnitude > layout.moduleLength * 1.5f);
            for (int k = 1; k < legs; k++)
                if (Mathf.Abs(bend[k]) > layout.bendBastionDeg)
                    chis.Add(new Chi { at = p[k], late = (lateOf[k - 1] + lateOf[k]).normalized, baseY = built[vertexJoint[k]], reason = "bend " + bend[k].ToString("+0.0;-0.0", Inv) + " deg" });
            if (line.cliffEntry > 0f)
            {
                var m = mods.OrderBy(x => Mathf.Abs(x.arc - line.cliffEntry)).First();
                if (Free(m.Centre)) chis.Add(new Chi { at = m.Centre, late = m.late, baseY = m.BaseCentre, reason = "cliff joint (prototype fill > 2 m from arc " + line.cliffEntry.ToString("F0", Inv) + " m)" });
                else plan.Notes.Add(line.id + ": the cliff joint (arc " + line.cliffEntry.ToString("F0", Inv) + " m) falls within 1.5 modules of a bend chi - one chi serves both");
            }
            for (int i = 1; i < mods.Count; i++)
            {
                float jump = Mathf.Abs(mods[i].GradeBuilt - mods[i - 1].GradeBuilt);
                if (jump <= layout.gradeBreakBastion || mods[i].leg != mods[i - 1].leg || mods[i].inCliff) continue;
                if (Free(mods[i].a)) chis.Add(new Chi { at = mods[i].a, late = mods[i].late, baseY = mods[i].baseA, reason = "grade break " + jump.ToString("F2", Inv) + " at " + mods[i - 1].id + "|" + mods[i].id });
                else plan.Notes.Add(line.id + ": grade break " + jump.ToString("F2", Inv) + " at " + mods[i - 1].id + "|" + mods[i].id + " is within 1.5 modules of another chi - none added");
            }
            for (int i = 0; i < chis.Count; i++)
            {
                chis[i].id = prefix + "chi" + (i + 1);
                var front = chis[i].at + chis[i].late * (hd + layout.moduleLength);
                chis[i].groundFront = TerrainY(front.x, front.y, out float gf) ? gf - chis[i].baseY : float.NaN;
            }
            plan.Chis.AddRange(chis);
        }

        /// <summary>Lowest terrain under the wall thickness at one station (5 samples across).</summary>
        static float FootMin(Vector2 q, Vector2 late, float halfDepth)
        {
            float low = float.PositiveInfinity;
            for (int c = -2; c <= 2; c++)
            {
                var s = q + late * (c * halfDepth * .5f);
                if (TerrainY(s.x, s.y, out float y)) low = Mathf.Min(low, y);
            }
            return float.IsInfinity(low) ? float.NaN : low;
        }

        // ---------------------------------------------------------------- build (in memory)

        static string Build(Layout layout, Opt opt, Plan plan, GameObject root)
        {
            var prefabs = new Dictionary<string, GameObject>();
            GameObject Asset(string name)
            {
                if (prefabs.TryGetValue(name, out var asset)) return asset;
                asset = AssetDatabase.LoadAssetAtPath<GameObject>(layout.packPrefabDir + name + ".prefab");
                if (asset == null) throw new PostLedger308.Refused("pack prefab missing: " + layout.packPrefabDir + name + ".prefab");
                prefabs[name] = asset; return asset;
            }
            GameObject Clone(string prefab, Transform parent, string name)
            {
                var go = Object.Instantiate(Asset(prefab), parent);
                go.name = name;
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = Flags;
                return go;
            }
            Material First(string prefab)
            {
                var r = Asset(prefab).GetComponentInChildren<MeshRenderer>(true);
                var m = r != null ? r.sharedMaterials.FirstOrDefault(Usable) : null;
                if (m == null) throw new PostLedger308.Refused("no usable material on pack prefab " + prefab);
                return m;
            }
            void Pieces(Piece[] pieces, Box[] boxes, Transform pivot)
            {
                foreach (var pc in pieces)
                {
                    if (pc.tag == "munru_roof1" && !opt.roof.Contains("1")) continue;
                    if (pc.tag == "munru_roof2" && !opt.roof.Contains("2")) continue;
                    var go = Clone(pc.prefab, pivot, pc.tag + "|" + pc.prefab);
                    go.transform.localPosition = new Vector3(pc.x, pc.y, pc.z); go.transform.localRotation = Quaternion.Euler(0f, pc.yaw, 0f); go.transform.localScale = new Vector3(pc.sx, pc.sy, pc.sz);
                }
                foreach (var b in boxes)
                {
                    var go = Make(b.tag + "|procedural", pivot);
                    go.transform.localPosition = new Vector3(b.cx, b.cy, b.cz);
                    go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(new Vector3(b.sx, b.sy, b.sz), RootName + "_" + b.tag, layout.moduleLength, layout.moduleHeight);
                    go.AddComponent<MeshRenderer>().sharedMaterial = First(b.materialFrom);
                }
            }

            // gate
            var gate = Make("Gate", root.transform);
            gate.transform.SetPositionAndRotation(new Vector3(layout.gateX, plan.GateFloor, layout.gateZ), Quaternion.LookRotation(new Vector3(layout.lateX, 0f, layout.lateZ), Vector3.up));
            Pieces(layout.gatePieces, layout.gateBoxes, gate.transform);

            // wall modules: sheared copies of the pack mesh (never stepped), one in-memory mesh per module
            var wallMesh = Asset(layout.wallPrefab).GetComponentInChildren<MeshFilter>(true).sharedMesh; var parapetMesh = Asset(layout.parapetPrefab).GetComponentInChildren<MeshFilter>(true).sharedMesh;
            if (wallMesh == null || parapetMesh == null || !wallMesh.isReadable || !parapetMesh.isReadable) throw new PostLedger308.Refused("wall / parapet pack mesh missing or unreadable");
            var groups = new Dictionary<string, Transform>();
            foreach (var m in plan.Modules)
            {
                if (!groups.TryGetValue(m.line, out var group)) { group = Make("Wall_" + m.line, root.transform).transform; groups[m.line] = group; }
                var late3 = new Vector3(m.late.x, 0f, m.late.y); var rotation = Quaternion.LookRotation(late3, Vector3.up);
                var right = rotation * Vector3.right; var centre = m.Centre;
                bool plusIsB = Vector3.Dot(right, new Vector3(m.b.x - m.a.x, 0f, m.b.y - m.a.y)) > 0f;
                float grade = (plusIsB ? m.baseB - m.baseA : m.baseA - m.baseB) / m.length;   // rise per metre toward local +x
                var body = Clone(layout.wallPrefab, group, m.id + "|" + layout.wallPrefab);
                body.transform.SetPositionAndRotation(new Vector3(centre.x, m.BaseCentre, centre.y), rotation); body.transform.localScale = new Vector3(m.scale, 1f, 1f);
                body.GetComponentInChildren<MeshFilter>(true).sharedMesh = Sheared(wallMesh, grade * m.scale, m.id + "_body");
                var top = Clone(layout.parapetPrefab, group, m.id + "|" + layout.parapetPrefab);
                top.transform.SetPositionAndRotation(new Vector3(centre.x, m.BaseCentre + layout.parapetY, centre.y) + late3 * layout.parapetZ, rotation); top.transform.localScale = new Vector3(m.scale, 1f, 1f);
                top.GetComponentInChildren<MeshFilter>(true).sharedMesh = Sheared(parapetMesh, grade * m.scale, m.id + "_parapet");
            }

            // chi kits (rigid, level)
            if (plan.Chis.Count > 0)
            {
                var chiRoot = Make("Chi", root.transform);
                foreach (var c in plan.Chis)
                {
                    var pivot = Make(c.id, chiRoot.transform);
                    pivot.transform.SetPositionAndRotation(new Vector3(c.at.x, c.baseY, c.at.y), Quaternion.LookRotation(new Vector3(c.late.x, 0f, c.late.y), Vector3.up));
                    Pieces(layout.chiPieces, layout.chiBoxes, pivot.transform);
                }
            }

            // materials: pack as it is; a missing slot takes the renderer's first usable material; mat=ink = in-memory copies on the existing ink shader
            int repaired = 0; var repairedOn = new HashSet<string>(); var ink = new Dictionary<Material, Material>();
            Shader inkShader = opt.mat == "ink" ? Shader.Find(InkShader) : null;
            if (opt.mat == "ink" && inkShader == null) throw new PostLedger308.Refused("shader " + InkShader + " not found");
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials; var fallback = mats.FirstOrDefault(Usable) ?? First(layout.wallPrefab); bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!Usable(mats[i])) { mats[i] = fallback; repaired++; repairedOn.Add(r.name.Substring(r.name.IndexOf('|') + 1)); changed = true; }
                    if (inkShader == null) continue;
                    if (!ink.TryGetValue(mats[i], out var copy)) { copy = Ink(mats[i], inkShader); ink[mats[i]] = copy; }
                    mats[i] = copy; changed = true;
                }
                if (changed) r.sharedMaterials = mats;
                r.allowOcclusionWhenDynamic = false;
            }
            return "built: gate " + gate.GetComponentsInChildren<MeshRenderer>(true).Length + " renderers, wall modules " + plan.Modules.Count + " (body + parapet each), chi " + plan.Chis.Count
                + " | unusable material slots replaced in memory: " + repaired + (repaired > 0 ? " (" + string.Join(", ", repairedOn) + ")" : "")
                + (inkShader != null ? " | mat=ink: " + ink.Count + " in-memory copies of the pack materials on " + InkShader + " (#296 crossing recipe + #297 swap values), nothing written" : " | mat=pack: pack materials as they are");
        }

        static bool Usable(Material m) => m != null && m.shader != null && m.shader.isSupported && m.shader.name != "Hidden/InternalErrorShader";

        /// <summary>In-memory copy of a pack material on the existing ink architecture shader: CompactArchitecture296 CrossingMaterial296 values, then the #297 shading swap values.</summary>
        static Material Ink(Material source, Shader shader)
        {
            var m = new Material(source) { name = source.name + "_WallLook308Ink", hideFlags = Flags };
            float bump = source.HasProperty("_BumpScale") ? source.GetFloat("_BumpScale") : .65f;
            m.shader = shader;
            void Set(string name, float value) { if (m.HasProperty(name)) m.SetFloat(name, value); }
            Set("_Saturation", .35f); Set("_LightResponse", .7f); Set("_BumpScale", bump); Set("_WashStrength", 0f); Set("_FadeInStart", -1f); Set("_FadeInEnd", 0f);
            Set("_FadeOutStart", 8000f); Set("_FadeOutEnd", 10000f); Set("_WindAmplitude", 0f); Set("_Leaf279", 0f); Set("_SimpleLighting", 0f);
            Set("_AmbientFloor", .07f); Set("_AmbientScale297", 1.35f); Set("_InkMixNear297", .30f); Set("_InkMixFar297", .55f); Set("_AOStrength297", 1f);
            return m;
        }

        /// <summary>Copy of a pack mesh sheared along its length (y += grade * x): the top line follows the ground, joints stay vertical.</summary>
        static Mesh Sheared(Mesh source, float grade, string name)
        {
            var mesh = Object.Instantiate(source);
            mesh.name = RootName + "_" + name; mesh.hideFlags = Flags;
            var v = source.vertices;
            for (int i = 0; i < v.Length; i++) v[i].y += grade * v[i].x;
            mesh.vertices = v;
            var n = source.normals;
            if (n != null && n.Length == v.Length) { for (int i = 0; i < n.Length; i++) n[i] = new Vector3(n[i].x - grade * n[i].y, n[i].y, n[i].z).normalized; mesh.normals = n; }
            var t = source.tangents;
            if (t != null && t.Length == v.Length)
            {
                for (int i = 0; i < t.Length; i++) { var d = new Vector3(t[i].x, t[i].y + grade * t[i].x, t[i].z).normalized; t[i] = new Vector4(d.x, d.y, d.z, t[i].w); }
                mesh.tangents = t;
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Box with metre-scaled UVs (one texture repeat = one wall module face) so procedural fill reads at the pack's stone size.</summary>
        static Mesh BoxMesh(Vector3 size, string name, float uvWidth, float uvHeight)
        {
            var h = size * .5f; var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            void Face(Vector3 normal, Vector3 r, Vector3 u)
            {
                var c = Vector3.Scale(normal, h); var rr = Vector3.Scale(r, h); var uu = Vector3.Scale(u, h); int o = v.Count;
                foreach (var q in new[] { c - rr - uu, c - rr + uu, c + rr + uu, c + rr - uu }) { v.Add(q); n.Add(normal); uv.Add(new Vector2(Vector3.Dot(q, r) / uvWidth, Vector3.Dot(q, u) / uvHeight)); }
                t.Add(o); t.Add(o + 1); t.Add(o + 2); t.Add(o); t.Add(o + 2); t.Add(o + 3);   // front = cross(u, r)
            }
            Face(Vector3.right, Vector3.forward, Vector3.up); Face(Vector3.left, Vector3.back, Vector3.up);
            Face(Vector3.forward, Vector3.left, Vector3.up); Face(Vector3.back, Vector3.right, Vector3.up);
            Face(Vector3.up, Vector3.right, Vector3.forward); Face(Vector3.down, Vector3.left, Vector3.forward);
            var mesh = new Mesh { name = name, hideFlags = Flags };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            return mesh;
        }

        // ---------------------------------------------------------------- report

        static string Summary(Layout layout, Opt opt, Plan plan)
        {
            var sb = new StringBuilder();
            sb.AppendLine("gate: " + plan.GateGround);
            foreach (var group in plan.Modules.GroupBy(m => m.line))
            {
                var all = group.ToList(); var line = layout.lines.First(l => l.id == group.Key); var open = all.Where(m => !m.inCliff).ToList(); var wing = open.Where(m => !m.gateSpan).ToList();
                sb.AppendLine("line " + group.Key + ": " + all.Count + " modules built (" + all.Count(m => m.gateSpan) + " on the gate span, " + all.Count(m => !m.gateSpan) + " on the plan wing; plan " + line.planModules + " for " + line.planLength.ToString("F1", Inv)
                    + " m), " + all.Count(m => m.inCliff) + " inside the raised cliff of the stage-1a prototype (arc >= " + line.cliffEntry.ToString("F0", Inv) + " m), x scale " + all.Min(m => m.scale).ToString("F3", Inv) + ".." + all.Max(m => m.scale).ToString("F3", Inv));
                sb.AppendLine("  grade along the wall, natural ground |g|: " + Five(open.Select(m => Mathf.Abs(m.GradeNatural))) + ", modules over " + opt.cap.ToString("0.##", Inv) + ": " + open.Count(m => Mathf.Abs(m.GradeNatural) > opt.cap + .0005f)
                    + " | built |g| max " + open.Max(m => Mathf.Abs(m.GradeBuilt)).ToString("F3", Inv));
                sb.AppendLine("  joint lowered below the natural footprint minimum (grade cap + no-float): " + Five(open.Select(m => m.Cut)) + ", modules > 1 m: " + open.Count(m => m.Cut > 1f) + ", > 2 m: " + open.Count(m => m.Cut > 2f));
                sb.AppendLine("  gap under the base (base above ground, 15 samples per module): max " + open.Max(m => m.floatMax).ToString("F2", Inv) + " m, modules > 0.05 m: " + open.Count(m => m.floatMax > .05f)
                    + " | base below ground (sunk): " + Five(open.Select(m => m.buryMax)));
                var tops = open.Where(m => !float.IsNaN(m.topOverEa)).Select(m => m.topOverEa).ToList();
                sb.AppendLine("  deck (" + layout.moduleHeight.ToString("F1", Inv) + " m) over the EA-side ground " + layout.eaProbe.ToString("0.#", Inv) + " m in front: " + Five(tops) + ", modules under " + layout.topOverEaMin.ToString("0.#", Inv) + " m: " + tops.Count(x => x < layout.topOverEaMin));
                var a1 = wing.Select(m => m.lateHigh - m.groundCentre).ToList(); var a1Top = wing.Select(m => m.lateHigh - (m.BaseCentre + layout.parapetTop)).ToList();
                sb.AppendLine("  A1, highest late-side ground within " + layout.a1Radius.ToString("F0", Inv) + " m minus the ground under the module (plan wing, outside the cliff, n=" + wing.Count + "): " + Five(a1) + ", modules over " + layout.a1Limit.ToString("F0", Inv) + " m: " + a1.Count(x => x > layout.a1Limit));
                sb.AppendLine("  A1 against the wall top (parapet top = base + " + layout.parapetTop.ToString("F2", Inv) + " m): " + Five(a1Top) + ", modules with late ground above the wall top: " + a1Top.Count(x => x > 0f));
                sb.AppendLine("  share of the late-side lattice ground within " + layout.a1Radius.ToString("F0", Inv) + " m that is not higher than the wall top: " + Five(wing.Select(m => m.lateBelowTop * 100f)) + " %");
                var d16 = wing.Select(m => m.late16 - m.BaseCentre).ToList();
                sb.AppendLine("  late-side ground within " + layout.deckRadius.ToString("F0", Inv) + " m minus the base: " + Five(d16) + ", modules where it is above the deck: " + d16.Count(x => x > layout.moduleHeight));
            }
            sb.AppendLine("chi: " + plan.Chis.Count + (plan.Chis.Count == 0 ? "" : " - " + string.Join("; ", plan.Chis.Select(c => c.id + " (" + c.at.x.ToString("F0", Inv) + "," + c.at.y.ToString("F0", Inv) + ") " + c.reason + ", ground at its front face "
                + (float.IsNaN(c.groundFront) ? "none" : c.groundFront.ToString("+0.0;-0.0", Inv) + " m vs base")))));
            foreach (string note in plan.Notes) sb.AppendLine("note: " + note);
            return sb.ToString().TrimEnd();
        }

        static string Table(Layout layout, Plan plan)
        {
            var sb = new StringBuilder();
            sb.AppendLine("id   | centre x,z      | base y | grade nat>built | lowered | gap  | sunk | deck-EA | A1 base | A1 top | late16-base | below top % | flags");
            foreach (var m in plan.Modules)
            {
                var c = m.Centre;
                sb.AppendLine(m.id.PadRight(4) + " | " + (c.x.ToString("F1", Inv) + "," + c.y.ToString("F1", Inv)).PadRight(15) + " | " + m.BaseCentre.ToString("F1", Inv).PadLeft(6) + " | "
                    + (m.GradeNatural.ToString("+0.00;-0.00", Inv) + ">" + m.GradeBuilt.ToString("+0.00;-0.00", Inv)).PadRight(15) + " | " + m.Cut.ToString("F2", Inv).PadLeft(7) + " | " + m.floatMax.ToString("F2", Inv) + " | " + m.buryMax.ToString("F2", Inv) + " | "
                    + m.topOverEa.ToString("F1", Inv).PadLeft(7) + " | " + (m.lateHigh - m.groundCentre).ToString("F1", Inv).PadLeft(7) + " | " + (m.lateHigh - (m.BaseCentre + layout.parapetTop)).ToString("F1", Inv).PadLeft(6) + " | "
                    + (m.late16 - m.BaseCentre).ToString("F1", Inv).PadLeft(11) + " | " + (m.lateBelowTop * 100f).ToString("F0", Inv).PadLeft(11) + " | " + (m.gateSpan ? "gate-span " : "") + (m.inCliff ? "in-cliff " : "") + (Mathf.Abs(m.GradeNatural) > layout.gradeCap + .0005f ? "steep " : ""));
            }
            return sb.ToString().TrimEnd();
        }

        static string Five(IEnumerable<float> values)
        {
            var v = values.Where(x => !float.IsNaN(x) && !float.IsInfinity(x)).OrderBy(x => x).ToArray();
            if (v.Length == 0) return "no samples";
            float At(float f) => v[Mathf.Clamp(Mathf.RoundToInt(f * (v.Length - 1)), 0, v.Length - 1)];
            return "min " + v[0].ToString("F2", Inv) + " / median " + At(.5f).ToString("F2", Inv) + " / p90 " + At(.9f).ToString("F2", Inv) + " / max " + v[v.Length - 1].ToString("F2", Inv);
        }

        static string Status()
        {
            var scene = SceneManager.GetActiveScene(); var sb = new StringBuilder();
            sb.AppendLine("scene " + scene.path + " dirty=" + scene.isDirty + " playing=" + EditorApplication.isPlaying);
            sb.AppendLine(QualityText() + " of [" + string.Join(",", QualitySettings.names) + "]" + (CliffPrior() >= 0 ? " | cliff preview (CliffLook308) is up" : " | no cliff preview"));
            var roots = FindRoots();
            if (roots.Count == 0) { sb.Append("preview: none - 0 objects up, 0 disabled behaviours (restored)"); return sb.ToString(); }
            Physics.SyncTransforms();
            foreach (var root in roots)
            {
                var all = root.GetComponentsInChildren<Transform>(true);
                var state = all.FirstOrDefault(t => t.name.StartsWith("State|", StringComparison.Ordinal));
                var disabled = all.Where(t => t.name.StartsWith("Disabled|", StringComparison.Ordinal)).ToArray();
                var renderers = root.GetComponentsInChildren<MeshRenderer>(true); long tris = 0; int memoryMeshes = 0;
                foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (f.sharedMesh == null) continue;
                    for (int s = 0; s < f.sharedMesh.subMeshCount; s++) tris += f.sharedMesh.GetIndexCount(s) / 3;
                    if (!EditorUtility.IsPersistent(f.sharedMesh)) memoryMeshes++;
                }
                sb.AppendLine("preview up: " + all.Length + " objects (all " + (all.All(t => (t.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0) ? "DontSave" : "NOT ALL DontSave") + "), " + renderers.Length + " renderers, " + tris + " tris, "
                    + memoryMeshes + " in-memory meshes, colliders " + root.GetComponentsInChildren<Collider>(true).Length + ", lights " + root.GetComponentsInChildren<Light>(true).Length + ", " + (state != null ? state.name : "no state record"));
                foreach (Transform group in root.transform)
                    if (group.GetComponentsInChildren<MeshRenderer>(true).Length > 0) sb.AppendLine("  " + group.name + ": " + group.GetComponentsInChildren<MeshRenderer>(true).Length + " renderers");
                sb.AppendLine("disabled behaviours: " + disabled.Length);
                foreach (var d in disabled) { var b = ResolveBehaviour(d.name.Substring(9)); sb.AppendLine("  " + d.name.Substring(9) + " -> " + (b == null ? "NOT FOUND" : "enabled=" + b.enabled)); }
                sb.AppendLine("materials on the preview:");
                foreach (var m in renderers.SelectMany(r => r.sharedMaterials).Distinct().OrderBy(m => m == null ? "" : m.name))
                {
                    if (m == null) { sb.AppendLine("  NULL SLOT"); continue; }
                    bool glow = m.IsKeywordEnabled("_EMISSION") || (m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0f);
                    sb.AppendLine("  " + m.name + " | " + m.shader.name + (m.shader.isSupported ? "" : " NOT SUPPORTED") + " | " + (EditorUtility.IsPersistent(m) ? AssetDatabase.GetAssetPath(m) : "in memory")
                        + " | smoothness " + (m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness").ToString("0.###", Inv) : "-") + " metallic " + (m.HasProperty("_Metallic") ? m.GetFloat("_Metallic").ToString("0.###", Inv) : "-")
                        + (m.IsKeywordEnabled("_METALLICSPECGLOSSMAP") ? " +metallic map" : "") + " | emission " + (glow ? "ON" : "none"));
                }
                if (state == null) continue;
                // a build of the wall ledger (OnExternal): its seat is measured by that tool, not by the P0 look-gate layout
                if (StateText(state.name, "by", "") != "") { sb.AppendLine("built by " + StateText(state.name, "by", "") + " (layout " + StateText(state.name, "layout", "?") + "): Jangseong308 status / plan report its seat"); continue; }
                var opt = new Opt { line = StateText(state.name, "line", "foot"), mat = StateText(state.name, "mat", "pack"), roof = StateText(state.name, "roof", "2"), gate = StateText(state.name, "gate", "centre"),
                    cap = float.Parse(StateText(state.name, "cap", "0.45"), Inv), sink = StateInt(state.name, "sink") != 0, chi = StateInt(state.name, "chi") != 0 };
                try
                {
                    var layout = Load(); var plan = Compute(layout, opt);
                    int bodies = root.GetComponentsInChildren<Transform>(true).Count(t => t.name.EndsWith("|" + layout.wallPrefab, StringComparison.Ordinal) && t.parent != null && t.parent.name.StartsWith("Wall_", StringComparison.Ordinal));
                    sb.AppendLine("re-measured now on the terrain colliders (" + plan.Modules.Count + " modules in the plan, " + bodies + " wall bodies up" + (bodies == plan.Modules.Count ? "" : " - MISMATCH") + "):");
                    sb.AppendLine(Summary(layout, opt, plan));
                    sb.AppendLine(Table(layout, plan));
                }
                catch (PostLedger308.Refused r) { sb.AppendLine("re-measure refused (the preview objects above are still up): " + r.Message); }
            }
            return sb.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- probes

        static bool TerrainY(float x, float z, out float y)
        {
            y = float.NegativeInfinity; bool any = false;
            foreach (var hit in Physics.RaycastAll(new Vector3(x, 3000f, z), Vector3.down, 6000f, ~0, QueryTriggerInteraction.Ignore))
            {
                var t = hit.collider.transform;
                if (t.parent == null || t.parent.name != TerrainRoot || hit.point.y <= y) continue;
                y = hit.point.y; any = true;
            }
            return any;
        }

        static string Ground(string argument)
        {
            Physics.SyncTransforms();
            var sb = new StringBuilder();
            foreach (string pair in argument.Split(';').Where(s => s.Trim().Length > 0))
            {
                var v = pair.Split(',').Select(t => float.Parse(t, Inv)).ToArray();
                bool ok = TerrainY(v[0], v[1], out float y);
                string top = "-"; float topY = float.NegativeInfinity;
                foreach (var hit in Physics.RaycastAll(new Vector3(v[0], 3000f, v[1]), Vector3.down, 6000f, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.point.y > topY) { topY = hit.point.y; top = PostLedger308.PathOf(hit.collider.transform); }
                sb.AppendLine(v[0].ToString("F1", Inv) + "," + v[1].ToString("F1", Inv) + " terrain=" + (ok ? y.ToString("F2", Inv) : "none") + " top=" + (float.IsInfinity(topY) ? "none" : topY.ToString("F2", Inv)) + " " + top);
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Read-only: renderer groups whose bounds centre lies within a radius, and every instanced dressing renderer of the scene.</summary>
        static string Near(string[] a)
        {
            if (a.Length < 3) throw new PostLedger308.Refused("use near:<x>,<z>:<radius>");
            var v = a[1].Split(',').Select(t => float.Parse(t, Inv)).ToArray(); float radius = float.Parse(a[2], Inv);
            var scene = SceneManager.GetActiveScene(); var groups = new SortedDictionary<string, int>(); var sb = new StringBuilder();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                {
                    var c = r.bounds.center;
                    if (!r.enabled || new Vector2(c.x - v[0], c.z - v[1]).magnitude > radius) continue;
                    var t = r.transform; while (t.parent != null && t.parent.parent != null) t = t.parent;
                    string key = t.parent == null ? t.name : t.parent.name + "/" + t.name;
                    groups[key] = groups.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            sb.AppendLine("enabled renderers with bounds centre within " + radius.ToString("F0", Inv) + " m of (" + v[0].ToString("F0", Inv) + "," + v[1].ToString("F0", Inv) + "):");
            foreach (var g in groups) sb.AppendLine("  " + g.Key + ": " + g.Value);
            sb.AppendLine("instanced dressing renderers in the scene (no per-object renderer; drawn from sheets):");
            foreach (var root in scene.GetRootGameObjects())
                foreach (var b in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (b != null && b.GetType().Name.IndexOf("DressingRenderer", StringComparison.Ordinal) >= 0)
                        sb.AppendLine("  " + PostLedger308.PathOf(b.transform) + "/" + b.GetType().Name + " enabled=" + b.enabled + " active=" + b.gameObject.activeInHierarchy);
            return sb.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- scene helpers

        static GameObject Make(string name, Transform parent)
        {
            var go = new GameObject(name) { hideFlags = Flags };
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        static List<GameObject> FindRoots() => FindRoots(RootName);

        static List<GameObject> FindRoots(string name) =>
            Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && g.name == name && !EditorUtility.IsPersistent(g) && g.transform.parent == null).ToList();

        /// <summary>Quality level the cliff preview recorded before it switched to PC, or -1 when no cliff preview is up.</summary>
        static int CliffPrior()
        {
            foreach (var root in FindRoots(CliffRootName))
                foreach (Transform c in root.transform)
                    if (c.name.StartsWith("State|", StringComparison.Ordinal)) { int q = StateInt(c.name, "q"); if (q >= 0) return q; }
            return -1;
        }

        static int StateInt(string state, string key) => int.TryParse(StateText(state, key, ""), NumberStyles.Integer, Inv, out int v) ? v : -1;

        static string StateText(string state, string key, string fallback)
        {
            foreach (string part in state.Split('|'))
                if (part.StartsWith(key + "=", StringComparison.Ordinal)) return part.Substring(key.Length + 1);
            return fallback;
        }

        /// <summary>"Root/Child/.../Object/TypeName" -> the Behaviour of that type on that scene object.</summary>
        static Behaviour ResolveBehaviour(string path)
        {
            int cut = path.LastIndexOf('/');
            if (cut <= 0) return null;
            var t = PostLedger308.Find(SceneManager.GetActiveScene(), path.Substring(0, cut));
            if (t == null) return null;
            string type = path.Substring(cut + 1);
            return t.GetComponents<Behaviour>().FirstOrDefault(b => b != null && b.GetType().Name == type);
        }
    }
}
