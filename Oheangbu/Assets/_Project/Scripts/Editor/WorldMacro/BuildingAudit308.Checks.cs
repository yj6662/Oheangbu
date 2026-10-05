using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 checks C0–C10 (Spec §A table). Every number comes from config.json thresholds (TEST). Read only.
    public static partial class BuildingAudit308
    {
        internal sealed class Unit308
        {
            public string root = "", path = "", key = "", name = "";
            public Root308 cfg; public Transform t; public bool building, readOnly;
            public Renderer[] renderers = Array.Empty<Renderer>(); public HashSet<Renderer> lod0 = new HashSet<Renderer>();
            public Collider[] colliders = Array.Empty<Collider>(); public Bounds bounds; public bool hasBounds;
            public bool Wants(string check) => cfg.checks == null || cfg.checks.Length == 0 || cfg.checks.Contains(check);
        }

        sealed class Ctx308
        {
            public Scene scene; public Config308 cfg; public Thresholds308 th; public Tokens308 tok; public Report308 r; public MeshCache308 cache = new MeshCache308();
            public List<Unit308> units = new List<Unit308>(); public List<Corridor308> corridors; public string[] onlyChecks;
            public Dictionary<string, Root308> rootCfg = new Dictionary<string, Root308>(); public List<Transform> rootTransforms = new List<Transform>();
            public Dictionary<string, int> evaluated = new Dictionary<string, int>();
            List<(Bounds b, Renderer r)> allRenderers; List<Renderer> skirts;
            public bool Want(string c) => onlyChecks.Length == 0 || onlyChecks.Contains(c);
            public void Eval(string c, int n = 1) { evaluated.TryGetValue(c, out int k); evaluated[c] = k + n; }
            public void Add(string check, string level, Unit308 u, string path, Vector3 pos, float value, string detail, string extra = "")
                => r.findings.Add(new Finding308 { check = check, level = level, root = u != null ? u.root : "", unit = u != null ? u.key : "", path = path ?? "", position = pos, value = value, detail = detail ?? "", extra = extra ?? "" });
            public List<(Bounds b, Renderer r)> AllRenderers()
            {
                if (allRenderers != null) return allRenderers;
                allRenderers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(false)).Where(x => x.enabled && !(x is ParticleSystemRenderer)).Select(x => (x.bounds, x)).ToList();
                return allRenderers;
            }
            public List<Renderer> Skirts()
            {
                if (skirts != null) return skirts;
                skirts = AllRenderers().Select(x => x.r).Where(x => HasAny(x.name, tok.skirt) || (x.transform.parent != null && HasAny(x.transform.parent.name, tok.skirt))).ToList();
                return skirts;
            }
        }

        static Report308 RunChecks(Scene scene, Config308 cfg, string[] onlyRoots, string[] onlyChecks)
        {
            var total = Stopwatch.StartNew();
            var ctx = new Ctx308 { scene = scene, cfg = cfg, th = cfg.thresholds, tok = cfg.tokens ?? new Tokens308(), onlyChecks = onlyChecks };
            var r = ctx.r = new Report308 { scene = scene.path, utc = Utc(), quality = QualityName(), configVersion = cfg.version };
            r.qualityLevels = string.Join(", ", QualityLevels().Select(q => q.name + " lodBias " + q.lodBias.ToString(Inv)));
            Physics.SyncTransforms();
            ctx.corridors = Corridors(scene, cfg);
            var rootObjects = scene.GetRootGameObjects();
            foreach (var rc in cfg.roots)
            {
                if (onlyRoots.Length > 0 && !onlyRoots.Contains(rc.name)) continue;
                if (cfg.excludedRoots.Contains(rc.name)) { r.notes.Add("root " + rc.name + " is both listed and excluded; skipped"); continue; }
                var info = new RootInfo308 { name = rc.name, checks = rc.checks != null && rc.checks.Length > 0 ? string.Join(",", rc.checks) : "all", protectedRoot = cfg.ProtectedRoot(rc.name) };
                info.readOnly = rc.readOnly || info.protectedRoot;
                var gos = rootObjects.Where(g => g.name == rc.name).ToArray();
                info.present = gos.Length > 0;
                r.roots.Add(info);
                if (!info.present) continue;
                ctx.rootCfg[rc.name] = rc;
                foreach (var go in gos)
                {
                    ctx.rootTransforms.Add(go.transform);
                    var list = new List<Unit308>();
                    Collect(go.transform, 0, rc, info.readOnly, cfg, list);
                    foreach (var u in list) u.building = IsBuilding(u, cfg);
                    ctx.units.AddRange(list);
                    info.units += list.Count; info.buildings += list.Count(u => u.building);
                    info.renderers += go.GetComponentsInChildren<Renderer>(false).Length;
                    var cols = go.GetComponentsInChildren<Collider>(false).Where(c => c.enabled).ToArray();
                    info.colliders += cols.Length; info.staticColliders += cols.Count(c => c.attachedRigidbody == null && !c.isTrigger);
                    info.inactiveObjects += go.GetComponentsInChildren<Transform>(true).Count(t => !t.gameObject.activeInHierarchy);
                }
            }
            foreach (var u in ctx.units) r.unitIndex.Add(u.root + "|" + u.key + "|" + (u.building ? "B" : "P") + "|" + (u.hasBounds ? V(u.bounds.center) : ""));
            void Timed(string id, Action a)
            {
                if (!ctx.Want(id)) return;
                var sw = Stopwatch.StartNew();
                try { a(); }
                catch (Exception e) { ctx.Add(id, "SKIPPED", null, "", Vector3.zero, 0, "check threw: " + e.GetType().Name + " " + e.Message); }
                var c = new Count308 { check = id, seconds = (float)sw.Elapsed.TotalSeconds };
                foreach (var f in r.findings.Where(x => x.check == id))
                    switch (f.level) { case "FAIL": c.fail++; break; case "WARN": c.warn++; break; case "SKIPPED": c.skipped++; break; default: c.info++; break; }
                ctx.evaluated.TryGetValue(id, out int n); c.pass = Mathf.Max(0, n - r.findings.Where(x => x.check == id && x.level == "FAIL").Select(x => x.unit).Distinct().Count());
                r.counts.Add(c);
            }
            Timed("C0", () => C0(ctx));
            Timed("C1", () => C1(ctx));
            Timed("C2", () => C2(ctx));
            Timed("C3", () => C3(ctx));
            Timed("C4", () => C4(ctx));
            Timed("C5", () => C5(ctx));
            Timed("C6", () => C6(ctx));
            Timed("C7", () => C7(ctx));
            Timed("C8", () => C8(ctx));
            Timed("C9", () => C9(ctx));
            Timed("C10", () => C10(ctx));
            if (ctx.cache.Failed > 0) r.notes.Add(ctx.cache.Failed + " mesh(es) could not be read (checks on them are SKIPPED, not PASS)");
            r.seconds = (float)total.Elapsed.TotalSeconds;
            return r;
        }

        // units: transforms at the root's configured depth (a shallower leaf becomes a unit itself)
        static void Collect(Transform t, int level, Root308 rc, bool readOnly, Config308 cfg, List<Unit308> list)
        {
            if (!t.gameObject.activeInHierarchy) return;
            if (level >= Mathf.Max(1, rc.depth) || (level > 0 && t.childCount == 0))
            {
                var u = MakeUnit(t, rc, readOnly);
                if (u.renderers.Length == 0 && u.colliders.Length == 0) return;
                list.Add(u);
                return;
            }
            for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), level + 1, rc, readOnly, cfg, list);
        }

        internal static Unit308 MakeUnit(Transform t, Root308 rc, bool readOnly)
        {
            var u = new Unit308 { root = rc.name, cfg = rc, t = t, readOnly = readOnly, path = PathOf(t), key = KeyOf(t), name = t.name };
            u.renderers = t.GetComponentsInChildren<Renderer>(false).Where(x => x.enabled && !(x is ParticleSystemRenderer)).ToArray();
            u.colliders = t.GetComponentsInChildren<Collider>(false).Where(x => x.enabled).ToArray();
            var higher = new HashSet<Renderer>();
            foreach (var g in t.GetComponentsInChildren<LODGroup>(false)) { var lods = g.GetLODs(); for (int i = 1; i < lods.Length; i++) foreach (var x in lods[i].renderers) if (x != null) higher.Add(x); }
            foreach (var x in u.renderers) if (!higher.Contains(x)) u.lod0.Add(x);
            foreach (var x in u.renderers) { if (!u.hasBounds) { u.bounds = x.bounds; u.hasBounds = true; } else u.bounds.Encapsulate(x.bounds); }
            if (!u.hasBounds) foreach (var c in u.colliders) { if (!u.hasBounds) { u.bounds = c.bounds; u.hasBounds = true; } else u.bounds.Encapsulate(c.bounds); }
            return u;
        }

        // the units of the named roots (config depth), with the building classification — used by BuildingFix308
        internal static List<Unit308> UnitsOf(Scene scene, Config308 cfg, IEnumerable<string> rootNames)
        {
            var list = new List<Unit308>(); var names = new HashSet<string>(rootNames);
            foreach (var go in scene.GetRootGameObjects().Where(g => names.Contains(g.name)))
            {
                var rc = cfg.roots.FirstOrDefault(r => r.name == go.name) ?? new Root308 { name = go.name, depth = 1 };
                var part = new List<Unit308>();
                Collect(go.transform, 0, rc, cfg.ProtectedRoot(go.name) || rc.readOnly, cfg, part);
                foreach (var u in part) u.building = IsBuilding(u, cfg);
                list.AddRange(part);
            }
            return list;
        }

        // ported from CompactArchitecture296.Audit (IsArchitectureAsset296 / ExplicitArchitectureScene296) + config roots/tokens
        static bool ArchitectureAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.Contains("Hwaseong") || path.Contains("Jejumok") || path.Contains("Seyeonjeong") || path.Contains("House_1/") || path.Contains("House_2/") || path.Contains("ThatchedInn/") || path.Contains("/Architecture/") || path.Contains("/Architecture296/") || path.Contains("Kaesong_Hall");
        }
        static readonly string[] ExplicitArchitecture = { "Reworld292_Sansa/", "Watershed295_SunkenCapital/", "Watershed295_Crossings/", "CapitalSouthGate253/", "Playtest_OriginalC2Inn/", "Architecture296_Venues/", "Architecture296_Gates/", "Architecture296_Crossings/" };
        static bool IsBuilding(Unit308 u, Config308 cfg)
        {
            if (!u.hasBounds) return false;
            var th = cfg.thresholds;
            if (Mathf.Max(u.bounds.size.x, u.bounds.size.z) < th.minBuildingSize || u.bounds.size.y < th.minBuildingHeight) return false;
            if (cfg.buildingRoots.Any(p => u.root.StartsWith(p, StringComparison.Ordinal))) return true;
            if (ExplicitArchitecture.Any(p => (u.path + "/").StartsWith(p, StringComparison.Ordinal) || u.path.Contains("/" + p))) return true;
            if (HasAny(u.name, cfg.tokens.building)) return true;
            foreach (var x in u.renderers)
            {
                var m = MeshOf(x);
                if (m != null && ArchitectureAsset(AssetDatabase.GetAssetPath(m))) return true;
                if (ArchitectureAsset(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(x.gameObject))) return true;
            }
            return false;
        }

        static Soup308 SoupOf(Ctx308 ctx, IEnumerable<Renderer> renderers, Func<Renderer, int, bool> flag = null)
        {
            var s = new Soup308();
            foreach (var x in renderers) if (x != null && !(x is ParticleSystemRenderer)) s.Add(x, ctx.cache, flag);
            return s;
        }

        // ------------------------------------------------------------------ C0 fingerprint

        static string ColliderLine(Collider c)
        {
            var b = c.bounds;
            string mesh = c is MeshCollider mc && mc.sharedMesh != null ? mc.sharedMesh.name : "";
            return c.GetType().Name + "|" + Mathf.RoundToInt(b.center.x * 100) + "|" + Mathf.RoundToInt(b.center.y * 100) + "|" + Mathf.RoundToInt(b.center.z * 100) + "|"
                 + Mathf.RoundToInt(b.size.x * 100) + "|" + Mathf.RoundToInt(b.size.y * 100) + "|" + Mathf.RoundToInt(b.size.z * 100) + "|" + mesh;
        }
        internal static string ColliderHash(IEnumerable<Collider> cols, out int count)
        {
            var lines = cols.Where(c => c != null && c.enabled && !c.isTrigger && c.attachedRigidbody == null && !(c is CharacterController) && c.gameObject.activeInHierarchy).Select(ColliderLine).OrderBy(x => x, StringComparer.Ordinal).ToList();
            count = lines.Count;
            return ShaText(string.Join("\n", lines)).Substring(0, 16);
        }
        internal static SiteHash308 SiteHash(string name, Vector3 centre, float radius)
        {
            string h = ColliderHash(Physics.OverlapSphere(centre, radius, ~0, QueryTriggerInteraction.Ignore), out int n);
            return new SiteHash308 { name = name, centre = centre, hash = h, colliders = n };
        }

        static void C0(Ctx308 ctx)
        {
            foreach (var info in ctx.r.roots.Where(x => x.present))
            {
                var cols = ctx.rootTransforms.Where(t => t.name == info.name).SelectMany(t => t.GetComponentsInChildren<Collider>(false));
                info.colliderHash = ColliderHash(cols, out int n);
                ctx.Eval("C0");
            }
            var sites = new List<(string name, Vector3 c)>();
            foreach (var s in ctx.cfg.repairSites) if (s.centre.Length >= 3) sites.Add((s.name, Vec(s.centre)));
            foreach (var s in BuildingFix308.LedgerSites(ctx.cfg.AliasOf(ctx.scene.path) ?? "")) sites.Add(s);
            foreach (var s in sites) ctx.r.sites.Add(SiteHash(s.name, s.c, ctx.th.c0SiteRadius));
            ctx.Add("C0", "INFO", null, ctx.scene.path, Vector3.zero, sites.Count, "fingerprint: " + ctx.r.roots.Count(x => x.present) + " roots hashed, " + sites.Count + " repair sites (" + ctx.th.c0SiteRadius + " m)");
            // cross-scene comparison against the newest scans of the other two scenes (site hashes = FAIL after repair, roots = WARN)
            string alias = ctx.cfg.AliasOf(ctx.scene.path);
            if (alias == null) return;
            bool repaired = BuildingFix308.AllScenesRepaired(ctx.cfg);
            foreach (var other in ctx.cfg.scenes.Where(s => s.alias != alias))
            {
                string dir = NewestScan(other.alias);
                if (dir == null) { ctx.r.notes.Add("C0: no scan of " + other.alias + " to compare"); continue; }
                var o = LoadReport(dir);
                foreach (var s in ctx.r.sites)
                {
                    var os = o.sites.FirstOrDefault(x => x.name == s.name);
                    if (os == null || os.hash == s.hash) continue;
                    ctx.Add("C0", repaired ? "FAIL" : "WARN", null, s.name, s.centre, s.colliders - os.colliders, "repair-site static colliders differ from " + other.alias + " (" + s.hash + " vs " + os.hash + ", " + s.colliders + " vs " + os.colliders + ")" + (repaired ? "" : " — before repair: recorded"), Path.GetFileName(dir));
                }
                foreach (var info in ctx.r.roots.Where(x => x.present))
                {
                    var oi = o.roots.FirstOrDefault(x => x.name == info.name);
                    if (oi == null || !oi.present || oi.colliderHash == info.colliderHash) continue;
                    ctx.Add("C0", "WARN", null, info.name, Vector3.zero, info.staticColliders - oi.staticColliders, "root static colliders differ from " + other.alias + " (" + info.colliderHash + " vs " + oi.colliderHash + ") — shared NavMesh drift risk", Path.GetFileName(dir));
                }
            }
        }

        // ------------------------------------------------------------------ C1 references

        static void C1(Ctx308 ctx)
        {
            var texCache = new Dictionary<Material, List<string>>();
            List<string> MissingTextures(Material m)
            {
                if (texCache.TryGetValue(m, out var hit)) return hit;
                var list = new List<string>();
                var so = new SerializedObject(m);
                var envs = so.FindProperty("m_SavedProperties.m_TexEnvs");
                if (envs != null && envs.isArray)
                    for (int i = 0; i < envs.arraySize; i++)
                    {
                        var e = envs.GetArrayElementAtIndex(i);
                        var name = e.FindPropertyRelative("first"); var tex = e.FindPropertyRelative("second.m_Texture");
                        if (tex == null || name == null) continue;
                        if (tex.objectReferenceValue == null && tex.objectReferenceInstanceIDValue != 0 && m.HasProperty(name.stringValue)) list.Add(name.stringValue);
                    }
                texCache[m] = list; return list;
            }
            foreach (var u in ctx.units.Where(u => u.Wants("C1")))
            {
                ctx.Eval("C1");
                foreach (var x in u.renderers)
                {
                    string p = PathOf(x.transform);
                    var mesh = MeshOf(x);
                    if ((x is MeshRenderer || x is SkinnedMeshRenderer) && mesh == null) ctx.Add("C1", "FAIL", u, p, x.transform.position, 0, "enabled renderer with a null mesh");
                    var mats = x.sharedMaterials;
                    int slots = mesh != null ? mesh.subMeshCount : mats.Length;
                    if (mats.Length == 0) ctx.Add("C1", "FAIL", u, p, x.bounds.center, 0, "renderer has no material slots");
                    for (int i = 0; i < Mathf.Max(slots, mats.Length); i++)
                    {
                        var m = i < mats.Length ? mats[i] : null;
                        if (m == null) { if (i < slots) ctx.Add("C1", "FAIL", u, p, x.bounds.center, i, "null material slot " + i); continue; }
                        var sh = m.shader;
                        if (sh == null || sh.name == "Hidden/InternalErrorShader" || !sh.isSupported || ShaderUtil.ShaderHasError(sh))
                            ctx.Add("C1", "FAIL", u, p, x.bounds.center, i, "material " + m.name + " shader " + (sh == null ? "null" : sh.name + (sh.isSupported ? "" : " (unsupported)") + (ShaderUtil.ShaderHasError(sh) ? " (errors)" : "")));
                        foreach (var t in MissingTextures(m)) ctx.Add("C1", "WARN", u, p, x.bounds.center, i, "material " + m.name + " texture " + t + " missing");
                    }
                }
                foreach (var t in u.t.GetComponentsInChildren<Transform>(true))
                {
                    int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (missing > 0) ctx.Add("C1", "WARN", u, PathOf(t), t.position, missing, missing + " missing script(s)");
                    if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && PrefabUtility.GetPrefabInstanceStatus(t.gameObject) == PrefabInstanceStatus.MissingAsset)
                        ctx.Add("C1", "WARN", u, PathOf(t), t.position, 0, "prefab instance whose prefab asset is missing");
                }
            }
        }

        // ------------------------------------------------------------------ C2 landing

        internal static Collider[] Plates(Thresholds308 th, Tokens308 tok, Unit308 u, out string how)
        {
            var cols = u.colliders.Where(c => !c.isTrigger && !(c is CharacterController) && c.attachedRigidbody == null && Mathf.Min(c.bounds.size.x, c.bounds.size.z) >= th.c2PlateMinSize).ToArray();
            var named = cols.Where(c => HasAny(c.name, tok.plate) && !HasAny(c.name, tok.plateExclude)).ToArray();
            if (named.Length > 0) { how = "named"; return named; }
            float bottom = u.bounds.min.y;
            var low = cols.Where(c => c.bounds.min.y <= bottom + th.c2PlateBottomBand).OrderByDescending(c => c.bounds.size.x * c.bounds.size.z).Take(1).ToArray();
            how = low.Length > 0 ? "lowest-wide" : "none";
            return low;
        }

        // floor top at xz: per plate the lowest upward face (a Collision mesh with a roof answers its floor), the highest across plates
        internal static float PlateTop(Collider[] plates, float x, float z, float yHi, float yLo)
        {
            float best = float.NaN;
            foreach (var c in plates)
            {
                float y = yHi, low = float.NaN;
                for (int k = 0; k < 8 && y > yLo; k++)
                {
                    if (!c.Raycast(new Ray(new Vector3(x, y, z), Vector3.down), out var h, y - yLo)) break;
                    if (h.normal.y > .7f) low = h.point.y;
                    y = h.point.y - .01f;
                }
                if (!float.IsNaN(low) && (float.IsNaN(best) || low > best)) best = low;
            }
            return best;
        }
        static float PlateBottom(Collider[] plates, float x, float z, float yLo, float fallback)
        {
            float best = float.NaN;
            foreach (var c in plates)
                if (c.Raycast(new Ray(new Vector3(x, yLo, z), Vector3.up), out var h, 40f) && (float.IsNaN(best) || h.point.y < best)) best = h.point.y;
            return float.IsNaN(best) ? fallback : best;
        }

        // the plate's top outline (four world corners at the top) from its collider/mesh local bounds
        internal static Vector3[] PlateCorners(Collider c)
        {
            Bounds lb; bool local = true;
            if (c is BoxCollider bc) lb = new Bounds(bc.center, bc.size);
            else if (c is MeshCollider mc && mc.sharedMesh != null) lb = mc.sharedMesh.bounds;
            else { var f = c.GetComponent<MeshFilter>(); if (f != null && f.sharedMesh != null) lb = f.sharedMesh.bounds; else { lb = c.bounds; local = false; } }
            Vector3 W(float x, float z) => local ? c.transform.TransformPoint(new Vector3(x, lb.max.y, z)) : new Vector3(x, lb.max.y, z);
            return new[] { W(lb.min.x, lb.min.z), W(lb.max.x, lb.min.z), W(lb.max.x, lb.max.z), W(lb.min.x, lb.max.z) };
        }

        internal static UnitMetric308 Landing(Unit308 u, Collider[] plates, Thresholds308 th, Tokens308 tok, Func<Vector3, bool> skirted, List<(string level, Vector3 at, float value, string detail)> rows)
        {
            var m = new UnitMetric308 { root = u.root, unit = u.path, key = u.key, building = u.building, centre = u.bounds.center, size = u.bounds.size };
            var primary = plates.OrderByDescending(c => c.bounds.size.x * c.bounds.size.z).First();
            m.plate = PathOf(primary.transform);
            var corners = PlateCorners(primary);
            var centroid = (corners[0] + corners[1] + corners[2] + corners[3]) * .25f;
            float yHi = plates.Max(c => c.bounds.max.y) + .5f, yLo = plates.Min(c => c.bounds.min.y) - .5f;
            float perimeter = 0; for (int i = 0; i < 4; i++) perimeter += Vector2.Distance(XZ(corners[i]), XZ(corners[(i + 1) % 4]));
            int n = Mathf.Max(4, th.c2PerimeterPoints);
            int measured = 0; Vector3 worst = centroid;
            for (int k = 0; k < n; k++)
            {
                float s = perimeter * (k + .5f) / n; int e = 0; float acc = 0;
                while (e < 3) { float len = Vector2.Distance(XZ(corners[e]), XZ(corners[e + 1])); if (acc + len >= s) break; acc += len; e++; }
                var a = corners[e]; var b = corners[(e + 1) % 4]; float len2 = Mathf.Max(1e-4f, Vector2.Distance(XZ(a), XZ(b)));
                var p = Vector3.Lerp(a, b, Mathf.Clamp01((s - acc) / len2));
                var inward = centroid - p; inward.y = 0; inward = inward.sqrMagnitude > 1e-6f ? inward.normalized : Vector3.zero;
                float top = float.NaN; Vector3 q = p;
                foreach (float inset in new[] { th.c2PlateInset, th.c2PlateInset + .3f, th.c2PlateInset + .6f, th.c2PlateInset + 1f })
                {
                    q = p + inward * inset; top = PlateTop(plates, q.x, q.z, yHi, yLo);
                    if (!float.IsNaN(top)) break;
                }
                if (float.IsNaN(top)) continue;
                float bottom = PlateBottom(plates, q.x, q.z, yLo - 1f, top);
                float terrain = TerrainNear(new Vector3(q.x, top, q.z), th.c2TerrainWindow);
                if (float.IsNaN(terrain)) continue;
                measured++;
                float gap = bottom - terrain;
                if (gap > m.maxHover) { m.maxHover = gap; worst = new Vector3(q.x, bottom, q.z); }
                if (gap > th.c2HoverFail) { if (!skirted(new Vector3(q.x, (terrain + bottom) * .5f, q.z))) m.hovering++; }
                else if (gap > th.c2HoverWarn) m.warnHover++;
            }
            m.perimeter = measured;
            m.hoverShare = measured > 0 ? m.hovering / (float)measured : 0f;
            // thresholds: in front of every door-like part, the terrain must not stand above the floor top
            foreach (var d in u.renderers.Where(x => HasAny(x.name, tok.door)))
            {
                var dc = d.bounds.center; var dir = dc - u.bounds.center; dir.y = 0; dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
                var inner = dc - dir * .3f; float top = PlateTop(plates, inner.x, inner.z, yHi, yLo);
                if (float.IsNaN(top)) top = PlateTop(plates, dc.x, dc.z, yHi, yLo);
                if (float.IsNaN(top)) continue;
                var outer = dc + dir * th.c2DoorOutset;
                float terrain = TerrainNear(new Vector3(outer.x, top, outer.z), th.c2TerrainWindow);
                if (float.IsNaN(terrain)) continue;
                m.thresholds++;
                float rise = terrain - top;
                if (rise > m.maxThresholdRise) m.maxThresholdRise = rise;
                if (rise > th.c2ThresholdRise) rows.Add(("FAIL", new Vector3(outer.x, terrain, outer.z), rise, "terrain " + F(rise, "F2") + " m above the floor top at the threshold of " + d.name));
            }
            // area: share of the floor where the terrain stands more than c2BuriedDepth above the floor top
            float step = Mathf.Max(.25f, th.c2AreaStep);
            float l01 = Vector3.Distance(corners[0], corners[1]), l03 = Vector3.Distance(corners[0], corners[3]);
            int nu = Mathf.Max(1, Mathf.FloorToInt(l01 / step)), nv = Mathf.Max(1, Mathf.FloorToInt(l03 / step));
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    float uu = (i + .5f) / nu, vv = (j + .5f) / nv;
                    var p = Vector3.Lerp(Vector3.Lerp(corners[0], corners[1], uu), Vector3.Lerp(corners[3], corners[2], uu), vv);
                    float top = PlateTop(plates, p.x, p.z, yHi, yLo); if (float.IsNaN(top)) continue;
                    float terrain = TerrainNear(new Vector3(p.x, top, p.z), th.c2TerrainWindow); if (float.IsNaN(terrain)) continue;
                    m.areaSamples++;
                    if (terrain - top > th.c2BuriedDepth) m.buried++;
                }
            m.buriedShare = m.areaSamples > 0 ? m.buried / (float)m.areaSamples : 0f;
            bool fail = false;
            if (m.hoverShare > th.c2HoverShareFail) { fail = true; rows.Add(("FAIL", worst, m.hoverShare, "plate floats: " + m.hovering + "/" + measured + " perimeter points > " + F(th.c2HoverFail, "F2") + " m without dry stone (max " + F(m.maxHover, "F2") + " m)")); }
            if (m.buriedShare > th.c2BuriedShareFail) { fail = true; rows.Add(("FAIL", centroid, m.buriedShare, "floor buried: " + m.buried + "/" + m.areaSamples + " samples with terrain > " + F(th.c2BuriedDepth, "F1") + " m above the floor top")); }
            if (rows.Any(x => x.level == "FAIL")) fail = true;
            if (!fail && m.warnHover > 0) rows.Add(("WARN", worst, m.maxHover, m.warnHover + " perimeter point(s) float " + F(th.c2HoverWarn, "F2") + "–" + F(th.c2HoverFail, "F2") + " m"));
            m.c2 = fail ? "FAIL" : m.warnHover > 0 ? "WARN" : measured == 0 ? "SKIPPED" : "PASS";
            if (measured == 0) rows.Add(("SKIPPED", centroid, 0, "no perimeter point measured (plate or terrain not found)"));
            return m;
        }
        static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        static void C2(Ctx308 ctx)
        {
            var skirts = ctx.Skirts();
            bool Skirted(Vector3 p) { float r2 = ctx.th.c2SkirtRadius * ctx.th.c2SkirtRadius; foreach (var s in skirts) if (s != null && s.bounds.SqrDistance(p) <= r2) return true; return false; }
            foreach (var u in ctx.units.Where(u => u.building && u.Wants("C2")))
            {
                ctx.Eval("C2");
                var plates = Plates(ctx.th, ctx.tok, u, out string how);
                if (plates.Length == 0)
                {
                    ctx.r.units.Add(new UnitMetric308 { root = u.root, unit = u.path, key = u.key, building = true, centre = u.bounds.center, size = u.bounds.size, c2 = "SKIPPED" });
                    ctx.Add("C2", "SKIPPED", u, u.path, u.bounds.center, 0, "no plate collider");
                    continue;
                }
                var rows = new List<(string level, Vector3 at, float value, string detail)>();
                var m = Landing(u, plates, ctx.th, ctx.tok, Skirted, rows);
                m.plate += " (" + how + ")";
                ctx.r.units.Add(m);
                foreach (var row in rows) ctx.Add("C2", row.level, u, u.path, row.at, row.value, row.detail, m.plate);
            }
        }

        // ------------------------------------------------------------------ C3 vegetation inside buildings

        internal sealed class Place308 { public WorldMacroDressingSheetSO sheet; public int index; public string id = "", proto = "", kind = ""; public Vector3 pos; public float height, radius; }

        internal static List<Place308> Placements(Scene scene, Config308 cfg)
        {
            var list = new List<Place308>();
            foreach (var sheet in SceneSheets(scene, cfg))
            {
                var protos = new Dictionary<string, WorldMacroDressingSheetSO.Prototype>();
                foreach (var p in sheet.Prototypes) if (p != null && !string.IsNullOrEmpty(p.Id)) protos[p.Id] = p;
                var fp = sheet.FixedPlacements ?? Array.Empty<WorldMacroDressingSheetSO.FixedPlacement>();
                for (int i = 0; i < fp.Length; i++)
                {
                    var f = fp[i]; if (f == null || f.PrototypeId == null || !protos.TryGetValue(f.PrototypeId, out var proto)) continue;
                    if (proto.Category != WorldMacroDressingSheetSO.Kind.Tree && proto.Category != WorldMacroDressingSheetSO.Kind.Rock) continue;
                    list.Add(new Place308 { sheet = sheet, index = i, id = f.Id ?? "", proto = f.PrototypeId, kind = proto.Category.ToString(), pos = f.Position, height = proto.Size.y * f.Scale, radius = Mathf.Max(proto.Size.x, proto.Size.z) * f.Scale * .5f });
                }
            }
            return list;
        }

        internal sealed class PlaceGrid308
        {
            readonly float cell; readonly Dictionary<long, List<Place308>> map = new Dictionary<long, List<Place308>>();
            public float MaxRadius;
            public PlaceGrid308(IEnumerable<Place308> all, float cell)
            {
                this.cell = Mathf.Max(1f, cell);
                foreach (var p in all) { long k = Key(Mathf.FloorToInt(p.pos.x / this.cell), Mathf.FloorToInt(p.pos.z / this.cell)); if (!map.TryGetValue(k, out var l)) map[k] = l = new List<Place308>(); l.Add(p); MaxRadius = Mathf.Max(MaxRadius, p.radius); }
            }
            static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            public IEnumerable<Place308> In(float x0, float z0, float x1, float z1)
            {
                for (int x = Mathf.FloorToInt(x0 / cell); x <= Mathf.FloorToInt(x1 / cell); x++)
                    for (int z = Mathf.FloorToInt(z0 / cell); z <= Mathf.FloorToInt(z1 / cell); z++)
                        if (map.TryGetValue(Key(x, z), out var l)) foreach (var p in l) if (p.pos.x >= x0 && p.pos.x <= x1 && p.pos.z >= z0 && p.pos.z <= z1) yield return p;
            }
        }

        // upward ray from the base +lift through the plant height: the first LOD0 face it meets (NaN = none)
        internal static float Intrusion(Soup308 soup, Place308 p, float lift, List<(float y, int tri)> hits)
        {
            soup.Vertical(p.pos.x, p.pos.z, p.pos.y + lift, p.pos.y + lift + Mathf.Max(.5f, p.height), hits);
            return hits.Count == 0 ? float.NaN : hits.Min(h => h.y);
        }

        static void C3(Ctx308 ctx)
        {
            var places = Placements(ctx.scene, ctx.cfg);
            var grid = new PlaceGrid308(places, ctx.th.c3GridCell);
            ctx.r.notes.Add("C3 sheets: " + string.Join(", ", SceneSheets(ctx.scene, ctx.cfg).Select(s => s.name + " sha " + Sha(Abs(AssetDatabase.GetAssetPath(s))).Substring(0, 12))) + " — tree/rock placements " + places.Count);
            var hits = new List<(float y, int tri)>();
            foreach (var u in ctx.units.Where(u => u.building && u.Wants("C3")))
            {
                ctx.Eval("C3");
                var b = u.bounds; float pad = grid.MaxRadius + ctx.th.c3EaveOverlap;
                var near = grid.In(b.min.x - pad, b.min.z - pad, b.max.x + pad, b.max.z + pad).ToList();
                if (near.Count == 0) continue;
                var soup = SoupOf(ctx, u.lod0);
                if (soup.Skipped > 0) ctx.Add("C3", "SKIPPED", u, u.path, b.center, soup.Skipped, soup.Skipped + " LOD0 mesh(es) unreadable — those faces are not tested");
                foreach (var p in near)
                {
                    string extra = AssetDatabase.GetAssetPath(p.sheet) + "|" + p.index + "|" + p.id + "|" + p.proto + "|" + p.kind;
                    bool inside = p.pos.x >= b.min.x && p.pos.x <= b.max.x && p.pos.z >= b.min.z && p.pos.z <= b.max.z;
                    float y = inside && soup.Count > 0 ? Intrusion(soup, p, ctx.th.c3BaseLift, hits) : float.NaN;
                    if (!float.IsNaN(y)) { ctx.Add("C3", "FAIL", u, u.path, p.pos, y - p.pos.y, p.kind + " " + p.id + " (" + p.proto + ", sheet " + p.sheet.name + " #" + p.index + ") under a building face " + F(y - p.pos.y, "F2") + " m above its base", extra); continue; }
                    float dx = Mathf.Max(0, Mathf.Max(b.min.x - p.pos.x, p.pos.x - b.max.x)), dz = Mathf.Max(0, Mathf.Max(b.min.z - p.pos.z, p.pos.z - b.max.z));
                    float overlap = p.radius - Mathf.Sqrt(dx * dx + dz * dz);
                    if (overlap > ctx.th.c3EaveOverlap) ctx.Add("C3", "WARN", u, u.path, p.pos, overlap, p.kind + " " + p.id + " crown overlaps the building bounds by " + F(overlap, "F2") + " m (eave suspicion)", extra);
                }
            }
        }

        // ------------------------------------------------------------------ C4 duplicates

        internal static List<(Transform a, Transform b)> SiblingDuplicates(Transform root, float posTol, float rotTol)
        {
            var pairs = new List<(Transform, Transform)>();
            // different levels of one LODGroup are not duplicates (HouseholdTimber LOD0/1/2 rule)
            var level = new Dictionary<Transform, (LODGroup g, int l)>();
            foreach (var g in root.GetComponentsInChildren<LODGroup>(false)) { var lods = g.GetLODs(); for (int i = 0; i < lods.Length; i++) foreach (var x in lods[i].renderers) if (x != null) level[x.transform] = (g, i); }
            bool LodPair(Transform a, Transform b) => level.TryGetValue(a, out var la) && level.TryGetValue(b, out var lb) && la.g == lb.g && la.l != lb.l;
            foreach (var t in root.GetComponentsInChildren<Transform>(false))
            {
                if (t.childCount < 2) continue;
                var groups = new Dictionary<string, List<Transform>>();
                for (int i = 0; i < t.childCount; i++) { var c = t.GetChild(i); if (!c.gameObject.activeInHierarchy) continue; if (!groups.TryGetValue(c.name, out var l)) groups[c.name] = l = new List<Transform>(); l.Add(c); }
                foreach (var g in groups.Values)
                {
                    if (g.Count < 2) continue;
                    for (int i = 0; i < g.Count; i++)
                        for (int j = i + 1; j < g.Count; j++)
                            if (Vector3.Distance(g[i].localPosition, g[j].localPosition) <= posTol && Quaternion.Angle(g[i].localRotation, g[j].localRotation) <= rotTol && Vector3.Distance(g[i].localScale, g[j].localScale) <= posTol && !LodPair(g[i], g[j]))
                                pairs.Add((g[i], g[j]));
                }
            }
            return pairs;
        }

        static string MatrixKey(Matrix4x4 m)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) sb.Append(Mathf.RoundToInt(m[i, j] * 1000)).Append(',');
            sb.Append(Mathf.RoundToInt(m.m03 * 100)).Append(',').Append(Mathf.RoundToInt(m.m13 * 100)).Append(',').Append(Mathf.RoundToInt(m.m23 * 100));
            return sb.ToString();
        }

        static void C4(Ctx308 ctx)
        {
            foreach (var root in ctx.rootTransforms)
            {
                if (!ctx.rootCfg.TryGetValue(root.name, out var rc) || !(rc.checks == null || rc.checks.Length == 0 || rc.checks.Contains("C4"))) continue;
                ctx.Eval("C4");
                Unit308 UnitOf(Transform t) => ctx.units.FirstOrDefault(u => t == u.t || t.IsChildOf(u.t));
                foreach (var (a, b) in SiblingDuplicates(root, ctx.th.c4PosTol, ctx.th.c4RotTol))
                    ctx.Add("C4", "FAIL", UnitOf(b), KeyOf(b), b.position, Vector3.Distance(a.localPosition, b.localPosition), "same parent, name and local TRS as " + KeyOf(a), KeyOf(a));
                // same mesh + same world matrix (different LOD levels of one LODGroup excepted)
                var levelOf = new Dictionary<Renderer, (LODGroup g, int level)>();
                foreach (var g in root.GetComponentsInChildren<LODGroup>(false)) { var lods = g.GetLODs(); for (int i = 0; i < lods.Length; i++) foreach (var x in lods[i].renderers) if (x != null) levelOf[x] = (g, i); }
                var byKey = new Dictionary<string, List<Renderer>>();
                foreach (var x in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!x.enabled || x is ParticleSystemRenderer) continue;
                    var mesh = MeshOf(x); if (mesh == null) continue;
                    string k = mesh.GetInstanceID() + "|" + MatrixKey(x.localToWorldMatrix);
                    if (!byKey.TryGetValue(k, out var l)) byKey[k] = l = new List<Renderer>(); l.Add(x);
                }
                foreach (var l in byKey.Values.Where(l => l.Count > 1))
                    for (int i = 1; i < l.Count; i++)
                    {
                        bool lodPair = levelOf.TryGetValue(l[0], out var la) && levelOf.TryGetValue(l[i], out var lb) && la.g == lb.g && la.level != lb.level;
                        if (lodPair) continue;
                        // the sibling rule already reports exact sibling copies; this adds copies under different parents
                        if (l[i].transform.parent == l[0].transform.parent && l[i].name == l[0].name) continue;
                        ctx.Add("C4", "FAIL", UnitOf(l[i].transform), KeyOf(l[i].transform), l[i].bounds.center, 0, "same mesh " + MeshOf(l[i]).name + " and world matrix as " + KeyOf(l[0].transform), KeyOf(l[0].transform));
                    }
            }
            // coplanar faces within c4CoplanarDist on building units (z-fighting risk; WARN)
            foreach (var u in ctx.units.Where(u => u.building && u.Wants("C4")))
            {
                var soup = SoupOf(ctx, u.lod0);
                if (soup.Count == 0) continue;
                if (soup.Count > ctx.th.c4CoplanarMaxTriangles) { ctx.Add("C4", "SKIPPED", u, u.path, u.bounds.center, soup.Count, "coplanar check skipped: " + soup.Count + " triangles > " + ctx.th.c4CoplanarMaxTriangles); continue; }
                Coplanar(ctx, u, soup);
            }
        }

        static void Coplanar(Ctx308 ctx, Unit308 u, Soup308 s)
        {
            float nt = Mathf.Max(1e-3f, ctx.th.c4CoplanarNormalTol), dt = Mathf.Max(1e-4f, ctx.th.c4CoplanarDist);
            var buckets = new Dictionary<(int, int, int, int), List<int>>();
            var normal = new Vector3[s.Count]; var area = new float[s.Count];
            for (int i = 0; i < s.Count; i++)
            {
                var n = Vector3.Cross(s.B[i] - s.A[i], s.C[i] - s.A[i]); float a2 = n.magnitude; area[i] = a2 * .5f;
                if (a2 < 1e-5f) continue;
                n /= a2;
                if (n.x < -1e-4f || (Mathf.Abs(n.x) <= 1e-4f && (n.y < -1e-4f || (Mathf.Abs(n.y) <= 1e-4f && n.z < 0)))) n = -n;
                normal[i] = n; float d = Vector3.Dot(n, s.A[i]);
                var key = (Mathf.RoundToInt(n.x / nt), Mathf.RoundToInt(n.y / nt), Mathf.RoundToInt(n.z / nt), Mathf.RoundToInt(d / dt));
                if (!buckets.TryGetValue(key, out var l)) buckets[key] = l = new List<int>(); l.Add(i);
            }
            var pairArea = new Dictionary<(int, int), float>(); var pairAt = new Dictionary<(int, int), Vector3>();
            int budget = 400000;
            foreach (var l in buckets.Values)
            {
                if (l.Count < 2) continue;
                // only faces of different renderers can z-fight here; compare across owner groups
                var owners = l.GroupBy(i => s.Tag[i]).Select(g => g.ToList()).ToList();
                if (owners.Count < 2) continue;
                for (int ga = 0; ga < owners.Count && budget > 0; ga++)
                    for (int gb = ga + 1; gb < owners.Count && budget > 0; gb++)
                        foreach (int a in owners[ga])
                        {
                            if (budget <= 0) break;
                            foreach (int b in owners[gb])
                            {
                                if (--budget <= 0) break;
                                int oa = s.Tag[a], ob = s.Tag[b];
                                int small = area[a] <= area[b] ? a : b, large = small == a ? b : a;
                                var c = (s.A[small] + s.B[small] + s.C[small]) / 3f;
                                if (!InTri2(c, s.A[large], s.B[large], s.C[large], normal[large])) continue;
                                var key = (Mathf.Min(oa, ob), Mathf.Max(oa, ob));
                                pairArea.TryGetValue(key, out float acc); pairArea[key] = acc + area[small]; pairAt[key] = c;
                            }
                        }
            }
            if (budget <= 0) ctx.Add("C4", "SKIPPED", u, u.path, u.bounds.center, 0, "coplanar pair budget exhausted; partial result");
            foreach (var kv in pairArea.Where(kv => kv.Value > ctx.th.c4CoplanarArea))
                ctx.Add("C4", "WARN", u, PathOf(s.Owners[kv.Key.Item1].transform), pairAt[kv.Key], kv.Value, F(kv.Value, "F2") + " m² of faces within " + F(ctx.th.c4CoplanarDist, "F3") + " m of " + PathOf(s.Owners[kv.Key.Item2].transform) + " (z-fighting risk)");
        }
        static bool InTri2(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
        {
            int ax = Mathf.Abs(n.x) > Mathf.Abs(n.y) ? (Mathf.Abs(n.x) > Mathf.Abs(n.z) ? 0 : 2) : (Mathf.Abs(n.y) > Mathf.Abs(n.z) ? 1 : 2);
            Vector2 P(Vector3 v) => ax == 0 ? new Vector2(v.y, v.z) : ax == 1 ? new Vector2(v.x, v.z) : new Vector2(v.x, v.y);
            Vector2 p2 = P(p), a2 = P(a), b2 = P(b), c2 = P(c);
            float d1 = (p2.x - b2.x) * (a2.y - b2.y) - (a2.x - b2.x) * (p2.y - b2.y);
            float d2 = (p2.x - c2.x) * (b2.y - c2.y) - (b2.x - c2.x) * (p2.y - c2.y);
            float d3 = (p2.x - a2.x) * (c2.y - a2.y) - (c2.x - a2.x) * (p2.y - a2.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        // ------------------------------------------------------------------ C5 holes

        static void C5(Ctx308 ctx)
        {
            var hits = new List<(float y, int tri)>();
            // (a) roof grid
            foreach (var u in ctx.units.Where(u => u.building && u.Wants("C5")))
            {
                ctx.Eval("C5");
                var soup = SoupOf(ctx, u.lod0, (x, sub) =>
                {
                    if (HasAny(x.name, ctx.tok.roof)) return true;
                    var mats = x.sharedMaterials; return sub < mats.Length && mats[sub] != null && HasAny(mats[sub].name, ctx.tok.roof);
                });
                if (soup.Skipped > 0) ctx.Add("C5", "SKIPPED", u, u.path, u.bounds.center, soup.Skipped, soup.Skipped + " LOD0 mesh(es) unreadable");
                var roof = Enumerable.Range(0, soup.Count).Where(i => soup.Flag[i]).ToList();
                if (roof.Count == 0) continue;
                float x0 = roof.Min(i => Mathf.Min(soup.A[i].x, Mathf.Min(soup.B[i].x, soup.C[i].x))), x1 = roof.Max(i => Mathf.Max(soup.A[i].x, Mathf.Max(soup.B[i].x, soup.C[i].x)));
                float z0 = roof.Min(i => Mathf.Min(soup.A[i].z, Mathf.Min(soup.B[i].z, soup.C[i].z))), z1 = roof.Max(i => Mathf.Max(soup.A[i].z, Mathf.Max(soup.B[i].z, soup.C[i].z)));
                float y0 = u.bounds.min.y + ctx.th.c5RoofMinHeight, y1 = u.bounds.max.y + 1f;
                float step = Mathf.Max(.1f, ctx.th.c5RoofStep);
                int nx = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / step)), nz = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / step));
                if ((long)nx * nz > 200000) { ctx.Add("C5", "SKIPPED", u, u.path, u.bounds.center, nx * nz, "roof grid too large"); continue; }
                var covered = new bool[nx, nz];
                for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) { soup.Vertical(x0 + (i + .5f) * step, z0 + (j + .5f) * step, y0, y1, hits, true); covered[i, j] = hits.Count > 0; }
                int holes = 0; Vector3 first = Vector3.zero; int k = Mathf.Max(1, ctx.th.c5RoofNeighbours);
                for (int i = k; i < nx - k; i++)
                    for (int j = k; j < nz - k; j++)
                    {
                        if (covered[i, j]) continue;
                        if (!(covered[i - k, j] && covered[i + k, j] && covered[i, j - k] && covered[i, j + k])) continue;
                        if (holes++ == 0) first = new Vector3(x0 + (i + .5f) * step, u.bounds.max.y, z0 + (j + .5f) * step);
                    }
                if (holes > 0) ctx.Add("C5", "FAIL", u, u.path, first, holes, holes + " roof grid cell(s) (" + F(step, "F2") + " m) open with covered neighbours");
            }
            // (b) enterable halls: upper-hemisphere rays from 1.6 m must not reach the sky
            foreach (var (id, floor) in Interiors(ctx.cfg))
            {
                var fp = floor;
                if (Physics.Raycast(floor + Vector3.up * 2f, Vector3.down, out var fh, 6f, ~0, QueryTriggerInteraction.Ignore)) fp = fh.point;
                var eye = fp + Vector3.up * ctx.th.c5InteriorEye;
                var owners = ctx.units.Where(u => u.building && u.bounds.Contains(new Vector3(eye.x, u.bounds.center.y, eye.z))).ToList();
                if (owners.Count == 0) { ctx.Add("C5", "INFO", null, "interior:" + id, eye, 0, "enterable hall not inside a scanned building unit (roots filter?)"); continue; }
                ctx.Eval("C5");
                var soup = SoupOf(ctx, owners.SelectMany(u => u.lod0));
                int n = Mathf.Max(8, ctx.th.c5InteriorRays), escaped = 0; Vector3 firstDir = Vector3.up;
                float minSin = Mathf.Sin(ctx.th.c5InteriorMinElevation * Mathf.Deg2Rad);
                for (int i = 0; i < n; i++)
                {
                    float yv = minSin + (1f - minSin) * (i + .5f) / n, rr = Mathf.Sqrt(Mathf.Max(0, 1 - yv * yv)), phi = i * 2.399963f;
                    var d = new Vector3(Mathf.Cos(phi) * rr, yv, Mathf.Sin(phi) * rr);
                    if (Physics.Raycast(eye, d, ctx.th.c5InteriorReach, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (soup.Ray(eye, d, ctx.th.c5InteriorReach, out _, out _)) continue;
                    if (escaped++ == 0) firstDir = d;
                }
                var unit = owners[0];
                if (escaped > 0) ctx.Add("C5", "FAIL", unit, "interior:" + id, eye, escaped, escaped + "/" + n + " upper rays from " + F(ctx.th.c5InteriorEye, "F1") + " m reach the sky (first dir " + V(firstDir) + ")");
                else ctx.Add("C5", "INFO", unit, "interior:" + id, eye, 0, "sealed: 0/" + n + " upper rays escape");
            }
            // (c) LOD seams between touching LODGroups of one root
            foreach (var root in ctx.rootTransforms)
            {
                if (!ctx.rootCfg.TryGetValue(root.name, out var rc) || !(rc.checks == null || rc.checks.Length == 0 || rc.checks.Contains("C5"))) continue;
                var groups = root.GetComponentsInChildren<LODGroup>(false).Where(g => g.enabled && g.lodCount >= 2).Select(g => (g, lods: g.GetLODs())).Where(x => x.lods[0].renderers.Any(r => r != null)).ToList();
                var b0 = groups.Select(x => { var rs = x.lods[0].renderers.Where(r => r != null).ToArray(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }).ToList();
                var order = Enumerable.Range(0, groups.Count).OrderBy(i => b0[i].min.x).ToList();
                int pairs = 0;
                for (int oi = 0; oi < order.Count && pairs < ctx.th.c5SeamMaxPairs; oi++)
                    for (int oj = oi + 1; oj < order.Count && pairs < ctx.th.c5SeamMaxPairs; oj++)
                    {
                        int i = order[oi], j = order[oj];
                        if (b0[j].min.x > b0[i].max.x + ctx.th.c5SeamTouch) break;
                        var ei = b0[i]; ei.Expand(ctx.th.c5SeamTouch * 2f);
                        if (!ei.Intersects(b0[j])) continue;
                        pairs++;
                        Seam(ctx, groups[i].g, groups[i].lods, groups[j].g, groups[j].lods, b0[i], b0[j]);
                    }
                if (pairs >= ctx.th.c5SeamMaxPairs) ctx.Add("C5", "SKIPPED", null, root.name, Vector3.zero, pairs, "LOD seam pair budget reached");
            }
        }

        static IEnumerable<(string id, Vector3 floor)> Interiors(Config308 cfg)
        {
            if (cfg.interiors.fromArchitectureSheet && !string.IsNullOrEmpty(cfg.interiors.architectureSheet))
            {
                var sheet = AssetDatabase.LoadAssetAtPath<CompactArchitectureSheetSO>(cfg.interiors.architectureSheet);
                if (sheet != null) foreach (var a in sheet.Arenas) if (a != null && a.Interior) yield return (a.Id, a.Centre);
            }
            foreach (var x in cfg.interiors.extra) if (x.floor.Length >= 3) yield return (x.id, Vec(x.floor));
        }

        static void Seam(Ctx308 ctx, LODGroup ga, LOD[] la, LODGroup gb, LOD[] lb, Bounds ba, Bounds bb)
        {
            var region = new Bounds(); bool any = false;
            foreach (var c in Corners(ba)) if (bb.Contains(c)) { if (!any) { region = new Bounds(c, Vector3.zero); any = true; } else region.Encapsulate(c); }
            foreach (var c in Corners(bb)) if (ba.Contains(c)) { if (!any) { region = new Bounds(c, Vector3.zero); any = true; } else region.Encapsulate(c); }
            if (!any) { var mid = (ba.ClosestPoint(bb.center) + bb.ClosestPoint(ba.center)) * .5f; region = new Bounds(mid, Vector3.zero); }
            region.Expand(ctx.th.c5SeamRegion * 2f);
            float gap0 = Gap(ctx, la[0].renderers, lb[0].renderers, region);
            if (float.IsInfinity(gap0) || gap0 > ctx.th.c5Lod1Seam) return;   // not a joint at LOD0
            ctx.Eval("C5");
            Unit308 unit = ctx.units.FirstOrDefault(u => ga.transform == u.t || ga.transform.IsChildOf(u.t));
            string pair = KeyOf(ga.transform) + " | " + KeyOf(gb.transform);
            for (int level = 1; level <= 2; level++)
            {
                if (level >= la.Length || level >= lb.Length) break;
                float gap = Gap(ctx, la[level].renderers, lb[level].renderers, region);
                float limit = level == 1 ? ctx.th.c5Lod1Seam : ctx.th.c5Lod2Seam;
                if (gap > limit) ctx.Add("C5", level == 1 ? "FAIL" : "WARN", unit, pair, region.center, float.IsInfinity(gap) ? 99f : gap, "LOD" + level + " seam " + (float.IsInfinity(gap) ? "no geometry at the joint" : F(gap, "F2") + " m") + " (LOD0 " + F(gap0, "F3") + " m)");
            }
        }
        static IEnumerable<Vector3> Corners(Bounds b) { for (int i = 0; i < 8; i++) yield return b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)); }

        // smallest distance between two renderer sets inside a region (vertices of one to triangles of the other, both ways)
        static float Gap(Ctx308 ctx, Renderer[] ra, Renderer[] rb, Bounds region)
        {
            var sa = SoupOf(ctx, ra.Where(r => r != null)); var sb = SoupOf(ctx, rb.Where(r => r != null));
            List<int> In(Soup308 s) { var l = new List<int>(); for (int i = 0; i < s.Count; i++) { var tb = new Bounds(s.A[i], Vector3.zero); tb.Encapsulate(s.B[i]); tb.Encapsulate(s.C[i]); if (tb.Intersects(region)) l.Add(i); } return l; }
            var ia = In(sa); var ib = In(sb);
            if (ia.Count == 0 || ib.Count == 0) return float.PositiveInfinity;
            float best = float.PositiveInfinity;
            void Dir(Soup308 from, List<int> fi, Soup308 to, List<int> ti)
            {
                var pts = fi.SelectMany(i => new[] { from.A[i], from.B[i], from.C[i] }).Where(region.Contains).Distinct().ToList();
                int stride = Mathf.Max(1, (int)((long)pts.Count * ti.Count / 400000));
                for (int p = 0; p < pts.Count; p += stride)
                    foreach (int t in ti) { float d = (ClosestOnTri(pts[p], to.A[t], to.B[t], to.C[t]) - pts[p]).sqrMagnitude; if (d < best) best = d; }
            }
            Dir(sa, ia, sb, ib); Dir(sb, ib, sa, ia);
            return float.IsInfinity(best) ? best : Mathf.Sqrt(best);
        }

        // ------------------------------------------------------------------ C6 LOD

        static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        static void C6(Ctx308 ctx)
        {
            var levels = QualityLevels();
            float pcBias = levels.Where(q => q.name == "PC").Select(q => q.lodBias).DefaultIfEmpty(float.NaN).First();
            float mobileBias = levels.Where(q => q.name == "Mobile").Select(q => q.lodBias).DefaultIfEmpty(float.NaN).First();
            float tanHalf = Mathf.Tan(ctx.th.c6Fov * .5f * Mathf.Deg2Rad);
            var meshOk = new Dictionary<Mesh, bool>();
            foreach (var root in ctx.rootTransforms)
            {
                if (!ctx.rootCfg.TryGetValue(root.name, out var rc) || !(rc.checks == null || rc.checks.Length == 0 || rc.checks.Contains("C6"))) continue;
                foreach (var group in root.GetComponentsInChildren<LODGroup>(false))
                {
                    ctx.Eval("C6");
                    var lods = group.GetLODs(); var notes = new List<string>();
                    bool valid = lods.Length > 0; float previous = float.PositiveInfinity; bool found = false; var lb = new Bounds();
                    for (int li = 0; li < lods.Length; li++)
                    {
                        var level = lods[li];
                        if (!(level.screenRelativeTransitionHeight >= 0 && level.screenRelativeTransitionHeight < previous)) { valid = false; notes.Add("LOD" + li + " transition not decreasing"); }
                        if (level.renderers.Length == 0) { valid = false; notes.Add("LOD" + li + " empty"); }
                        previous = level.screenRelativeTransitionHeight;
                        foreach (var x in level.renderers)
                        {
                            if (x == null) { valid = false; notes.Add("LOD" + li + " null renderer"); continue; }
                            var mesh = MeshOf(x);
                            if (mesh == null) { valid = false; notes.Add("LOD" + li + " " + x.name + " null mesh"); continue; }
                            var b = mesh.bounds; var mm = group.transform.worldToLocalMatrix * x.localToWorldMatrix;
                            foreach (var c in Corners(b)) { var p = mm.MultiplyPoint3x4(c); if (!found) { lb = new Bounds(p, Vector3.zero); found = true; } else lb.Encapsulate(p); }
                            if (!meshOk.TryGetValue(mesh, out bool ok))
                            {
                                var data = ctx.cache.Get(mesh);
                                if (data == null) ok = true;   // unreadable: not judged here (cache counts it)
                                else { var eb = mesh.bounds; eb.Expand(.04f); ok = data.v.All(v => Finite(v) && eb.Contains(v)); }
                                meshOk[mesh] = ok;
                            }
                            if (!ok) { valid = false; notes.Add("LOD" + li + " " + mesh.name + " has non-finite or out-of-bounds vertices"); }
                        }
                    }
                    float needed = found ? Mathf.Max(lb.size.x, Mathf.Max(lb.size.y, lb.size.z)) : 0;
                    float centreError = found ? Vector3.Distance(group.localReferencePoint, lb.center) : 0;
                    if (!(found && group.size + ctx.th.c6SizeSlack >= needed)) { valid = false; notes.Add("size " + F(group.size, "F2") + " < needed " + F(needed, "F2")); }
                    if (found && centreError >= ctx.th.c6CentreTol) { valid = false; notes.Add("reference point off by " + F(centreError, "F2") + " m"); }
                    Unit308 unit = ctx.units.FirstOrDefault(u => group.transform == u.t || group.transform.IsChildOf(u.t));
                    if (!valid) ctx.Add("C6", "FAIL", unit, KeyOf(group.transform), group.transform.position, needed, string.Join("; ", notes));
                    float world = group.size * Mathf.Max(Mathf.Abs(group.transform.lossyScale.x), Mathf.Max(Mathf.Abs(group.transform.lossyScale.y), Mathf.Abs(group.transform.lossyScale.z)));
                    float Dist(float h, float bias) => h > 0 && !float.IsNaN(bias) ? world * bias / (2f * tanHalf * h) : float.PositiveInfinity;
                    ctx.r.lods.Add(new Lod308 { path = KeyOf(group.transform), size = world, transitions = lods.Select(l => l.screenRelativeTransitionHeight).ToArray(), pcDistance = lods.Select(l => Dist(l.screenRelativeTransitionHeight, pcBias)).ToArray(), mobileDistance = lods.Select(l => Dist(l.screenRelativeTransitionHeight, mobileBias)).ToArray() });
                }
            }
            ctx.Add("C6", "INFO", null, ctx.scene.path, Vector3.zero, ctx.r.lods.Count, "LOD transition distances (fov " + ctx.th.c6Fov + ", PC lodBias " + pcBias + ", Mobile lodBias " + mobileBias + ") are in audit.json lods");
        }

        // ------------------------------------------------------------------ C7 collision

        static void C7(Ctx308 ctx)
        {
            var all = ctx.AllRenderers();
            foreach (var u in ctx.units.Where(u => u.building && u.Wants("C7")))
            {
                ctx.Eval("C7");
                var half = new Vector2(u.bounds.extents.x, u.bounds.extents.z);
                float clearance = CorridorClearance(new Vector2(u.bounds.center.x, u.bounds.center.z), half, 0f, ctx.corridors, out string corridor);
                bool inCorridor = clearance < ctx.th.c7CorridorDistance;
                foreach (var x in u.lod0)
                {
                    var b = x.bounds;
                    if (Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) < ctx.th.c7MinRendererSize || MeshOf(x) == null) continue;
                    var data = ctx.cache.Get(MeshOf(x));
                    if (data == null) { ctx.Add("C7", "SKIPPED", u, PathOf(x.transform), b.center, 0, "mesh unreadable"); continue; }
                    var m = x.localToWorldMatrix; var cands = new List<Vector3>();
                    for (int i = 0; i + 2 < data.t.Length; i += 3)
                    {
                        Vector3 a = m.MultiplyPoint3x4(data.v[data.t[i]]), bb = m.MultiplyPoint3x4(data.v[data.t[i + 1]]), c = m.MultiplyPoint3x4(data.v[data.t[i + 2]]);
                        var n = Vector3.Cross(bb - a, c - a); float area = n.magnitude * .5f;
                        if (area < ctx.th.c7MinTriangleArea || Mathf.Abs(n.y / (2f * area)) > .3f) continue;
                        cands.Add((a + bb + c) / 3f);
                    }
                    if (cands.Count == 0) continue;
                    int stride = Mathf.Max(1, cands.Count / Mathf.Max(1, ctx.th.c7SamplesPerRenderer)), kept = 0, open = 0;
                    Vector3 firstOpen = b.center;
                    for (int i = 0; i < cands.Count; i += stride)
                    {
                        var p = cands[i];
                        float ground = float.NaN;
                        foreach (var h in Physics.RaycastAll(p + Vector3.up * .05f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore)) if (h.normal.y > .6f && (float.IsNaN(ground) || h.point.y > ground)) ground = h.point.y;
                        if (float.IsNaN(ground)) ground = TerrainTop(p);
                        float hgt = p.y - ground;
                        if (float.IsNaN(hgt) || hgt < ctx.th.c7WallMin || hgt > ctx.th.c7WallMax) continue;
                        kept++;
                        if (!Physics.CheckSphere(p, ctx.th.c7ProbeRadius, ~0, QueryTriggerInteraction.Ignore)) { if (open++ == 0) firstOpen = p; }
                    }
                    if (kept >= ctx.th.c7MinSamples && open / (float)kept >= ctx.th.c7NoColliderShare)
                        ctx.Add("C7", inCorridor ? "FAIL" : "WARN", u, PathOf(x.transform), firstOpen, open / (float)kept, "visible wall without collider: " + open + "/" + kept + " samples (" + F(ctx.th.c7WallMin, "F1") + "–" + F(ctx.th.c7WallMax, "F1") + " m) have no collider within " + F(ctx.th.c7ProbeRadius, "F2") + " m" + (inCorridor ? "; route " + corridor + " " + F(clearance, "F1") + " m" : ""));
                }
                foreach (var c in u.colliders)
                {
                    if (c.isTrigger || TerrainLike(c) || c is CharacterController) continue;
                    var cb = c.bounds; if (cb.size.x * cb.size.y * cb.size.z <= ctx.th.c7InvisibleVolume) continue;
                    var e = cb; e.Expand(ctx.th.c7InvisibleRadius * 2f);
                    bool seen = false;
                    foreach (var (rb, rr) in all) if (rb.Intersects(e)) { seen = true; break; }
                    if (!seen) ctx.Add("C7", "WARN", u, PathOf(c.transform), cb.center, cb.size.x * cb.size.y * cb.size.z, "collider " + F(cb.size.x * cb.size.y * cb.size.z, "F1") + " m³ with no renderer within " + F(ctx.th.c7InvisibleRadius, "F1") + " m (invisible wall)");
                }
            }
        }

        // ------------------------------------------------------------------ C8 state (inactive objects are seen here)

        static void C8(Ctx308 ctx)
        {
            foreach (var root in ctx.rootTransforms)
            {
                if (!ctx.rootCfg.TryGetValue(root.name, out var rc) || !(rc.checks == null || rc.checks.Length == 0 || rc.checks.Contains("C8"))) continue;
                ctx.Eval("C8");
                int depthCap = Mathf.Max(1, rc.depth) + 1;
                if (!root.gameObject.activeSelf) ctx.Add("C8", "WARN", null, root.name, root.position, 0, "root inactive");
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    int depth = 0; for (var p = t; p != root && p != null; p = p.parent) depth++;
                    if (!t.gameObject.activeSelf && t.parent != null && t.parent.gameObject.activeInHierarchy && depth <= depthCap) ctx.Add("C8", "WARN", null, KeyOf(t), t.position, depth, "inactive object");
                    if (t.gameObject.activeInHierarchy && depth <= depthCap && HasAny(t.name, ctx.tok.test)) ctx.Add("C8", "WARN", null, KeyOf(t), t.position, depth, "active object with a TEST name");
                }
                foreach (var x in root.GetComponentsInChildren<Renderer>(true))
                    if (!x.enabled && x.gameObject.activeInHierarchy && !(x is ParticleSystemRenderer)) ctx.Add("C8", "WARN", null, KeyOf(x.transform), x.transform.position, 0, "renderer disabled" + (MeshOf(x) == null ? " (null mesh: retired)" : ""));
                foreach (var c in root.GetComponentsInChildren<Collider>(false))
                {
                    if (!c.enabled || c.isTrigger || TerrainLike(c)) continue;
                    if (c.GetComponentsInChildren<Renderer>(false).Any(x => x.enabled) || (c.transform.parent != null && c.transform.parent.GetComponentsInChildren<Renderer>(false).Any(x => x.enabled && x.bounds.Intersects(c.bounds)))) continue;
                    float g = TerrainTop(c.bounds.center); if (float.IsNaN(g)) continue;
                    float above = c.bounds.min.y - g;
                    if (above > ctx.th.c8FloatHeight) ctx.Add("C8", "WARN", null, KeyOf(c.transform), c.bounds.center, above, "renderer-less collider " + F(above, "F1") + " m above the terrain");
                }
                foreach (var a in root.GetComponentsInChildren<Component>(false).Where(x => x is PrologueEncounter || x is PrologueInteraction || x is NpcJobActor))
                {
                    var t = a.transform; if (t.GetComponentsInChildren<Renderer>(false).Any(x => x.enabled)) continue;
                    float g = TerrainTop(t.position); if (float.IsNaN(g)) continue;
                    float above = t.position.y - g;
                    if (above > ctx.th.c8FloatHeight) ctx.Add("C8", "WARN", null, KeyOf(t), t.position, above, a.GetType().Name + " without renderer " + F(above, "F1") + " m above the terrain");
                }
            }
        }

        // ------------------------------------------------------------------ C9 emission (report only, ART-INK)

        internal static bool Emissive(Material m)
        {
            if (m == null || m.shader == null) return false;
            if (m.HasProperty("_EmissionColor"))
            {
                var c = m.GetColor("_EmissionColor");
                if (c.maxColorComponent > 0f && (m.IsKeywordEnabled("_EMISSION") || !m.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal))) return true;
            }
            foreach (var name in new[] { "_Emission", "_EmissionStrength", "_EmissionIntensity", "_Glow", "_GlowStrength" })
            {
                int i = m.shader.FindPropertyIndex(name);
                if (i < 0) continue;
                var type = m.shader.GetPropertyType(i);
                if ((type == ShaderPropertyType.Float || type == ShaderPropertyType.Range) && m.GetFloat(name) > 0f) return true;
            }
            return false;
        }

        static void C9(Ctx308 ctx)
        {
            int count = 0;
            foreach (var root in ctx.rootTransforms)
            {
                if (!ctx.rootCfg.TryGetValue(root.name, out var rc) || !(rc.checks == null || rc.checks.Length == 0 || rc.checks.Contains("C9"))) continue;
                if (ctx.cfg.emissionExcludeRoots.Contains(root.name)) continue;
                ctx.Eval("C9");
                foreach (var x in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!x.enabled) continue;
                    foreach (var m in x.sharedMaterials)
                    {
                        if (!Emissive(m)) continue;
                        count++;
                        string p = PathOf(x.transform);
                        if (!HasAny(p, ctx.cfg.emissionAllow) && !HasAny(m.name, ctx.cfg.emissionAllow))
                            ctx.Add("C9", "WARN", ctx.units.FirstOrDefault(u => x.transform == u.t || x.transform.IsChildOf(u.t)), p, x.bounds.center, 0, "emissive material " + m.name + " (" + m.shader.name + ") outside the light-source allow list");
                    }
                }
            }
            ctx.r.emissionCount = count;
            ctx.Add("C9", "INFO", null, ctx.scene.path, Vector3.zero, count, "emissive material slots in scanned roots (excluding " + string.Join(",", ctx.cfg.emissionExcludeRoots) + "): " + count);
        }

        // ------------------------------------------------------------------ C10 shaders (report; shot targets)

        static void C10(Ctx308 ctx)
        {
            var families = new Dictionary<string, int>();
            foreach (var u in ctx.units.Where(u => u.building && u.Wants("C10")))
            {
                ctx.Eval("C10");
                foreach (var x in u.lod0)
                {
                    var mats = x.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i]; if (m == null || m.shader == null) continue;
                        families.TryGetValue(m.shader.name, out int n); families[m.shader.name] = n + 1;
                        bool part = HasAny(x.name, ctx.tok.oneSided) || HasAny(m.name, ctx.tok.oneSided);
                        if (!part) continue;
                        float cull = m.HasProperty("_Cull") ? m.GetFloat("_Cull") : m.HasProperty("_CullMode") ? m.GetFloat("_CullMode") : 2f;
                        if (Mathf.RoundToInt(cull) == 2) ctx.Add("C10", "WARN", u, PathOf(x.transform), x.bounds.center, cull, "one-sided (_Cull 2) " + m.name + " (" + m.shader.name + ") on a roof/eave part: check from under the eave");
                    }
                }
            }
            ctx.r.shaderFamilies = families.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + ": " + kv.Value).ToList();
            ctx.Add("C10", "INFO", null, ctx.scene.path, Vector3.zero, families.Count, families.Count + " shader families on building units");
        }
    }
}
