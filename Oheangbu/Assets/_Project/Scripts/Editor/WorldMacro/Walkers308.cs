using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-77 townspeople of the capital (Tools/Unity/Stage308_walkers/PLAN_walkers308.md). Edit mode only, queue-safe (no dialog;
    // refusals are text). Scenes are saved with SaveScene, assets with SaveAssetIfDirty - AssetDatabase.SaveAssets is never called.
    //   status          what is deployed / imported / built / placed
    //   bodies:import   Assets/_Project/Art/World/PackLite308/people (put there by bodies_extract.py: the PeopleLite308 light bodies + the pack's 4 clips)
    //                   -> Generic import, one URP Lit material per colour variant, LODGroup over _LOD0/1/2, PF_Townsfolk308_<Body>.prefab
    //   routes:build    walkers308.json -> TownWalkerRoutes308_Hanyang.asset (points without a baked height are rayed onto the terrain once)
    //   place           the root Hanyang308_WalkersHost (TownWalkers308) into the play scene + the unit Hanyang308_Walkers (ground props only) into Realm_Hwanggyeong with its stream manifest row
    //   clear           removes that unit and its manifest row
    //   check           routes on the ground and clear of colliders, stops / posts clear, caps, bodies (clips, materials, not targetable), wiring
    //   cost            edit-mode count of the whole pool (renderers, triangles per LOD) at the busiest point
    //   on | off        the Enabled flag of the profile (off = no body is ever taken out)
    //   shots:lineup | shots:walk | shots:people | shots:off   temporary bodies (never saved) + the Presentation297 still commands; off removes them
    public static class Walkers308
    {
        public static string Run(string command) => Hanyang308.WalkersRun(command);
    }

    public static partial class Hanyang308
    {
        const string WUnit = "Hanyang308_Walkers", WHost = "Hanyang308_WalkersHost", WPreview = "Walkers308_Preview", WDir = "Assets/_Project/Art/Characters/Townsfolk308", WLite = "Assets/_Project/Art/World/PackLite308/people";
        const string WData = "Assets/_Project/Data/World/Walkers308", WProfile = WData + "/TownWalkerProfile308_Hanyang.asset", WRoutes = WData + "/TownWalkerRoutes308_Hanyang.asset";
        const float WNoY = -9000f, WAlong = 1f, WCrossStep = .25f, WFeet = .05f, WStep = .2f, WCliff = .2f, WMargin = .6f, WTaper = .4f;      // routes:build: point spacing, cross-table spacing, feet tolerance of check, floor step, collider margin, width taper (m per m)      // route point spacing and cross-table spacing (m) of routes:build
        static string WStage => Path.Combine(Repo, "Tools", "Unity", "Stage308_walkers");
        static string WFilePath => Path.Combine(WStage, "walkers308.json");

        [Serializable] sealed class WPt { public float x, y, z; }
        [Serializable] sealed class WRoute { public string id = ""; public bool loop; public float half; public List<WPt> points = new List<WPt>(); }
        [Serializable] sealed class WStop { public int route; public float s, lat, yaw; }
        [Serializable] sealed class WWalker { public string role = "", body = "", tint = ""; public int route, dir, seed; public float start, lane, speed; }
        [Serializable] sealed class WPost { public string role = "", body = "", tint = "", prop = ""; public float x, y, z, yaw, propX, propZ; public int job, seed; }
        [Serializable] sealed class WBody
        {
            public string key = "", lite = "", fbx = "", prefab = "", idle = "", walk = "", head = "", foot = ""; public int triangles, maxActive; public bool alphaClip;
            public float stride, height, cutoff, lod1, lod2, cull;      // stride = metres per walk cycle measured offline; lod1 / lod2 / cull = metres
            public List<string> variants = new List<string>();      // one atlas per tintOrder entry
        }
        [Serializable] sealed class WFile
        {
            public string version = ""; public int triangleBudget, hardCap; public List<string> tintOrder = new List<string>(); public List<WBody> bodies = new List<WBody>();
            public List<WRoute> routes = new List<WRoute>(); public List<WStop> stops = new List<WStop>(); public List<WWalker> walkers = new List<WWalker>(); public List<WPost> posts = new List<WPost>();
        }
        static WFile WLoad()
        {
            if (!File.Exists(WFilePath)) throw new Refuse("no " + WFilePath);
            var f = JsonUtility.FromJson<WFile>(File.ReadAllText(WFilePath)); if (f == null || f.routes.Count == 0 || f.bodies.Count == 0) throw new Refuse("walkers308.json has no routes / bodies");
            return f;
        }

        internal static string WalkersRun(string command)
        {
            string c = (command ?? "").Trim(); string[] a = c.Split(':');
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Refuse("Play mode");
                if (EditorApplication.isCompiling) throw new Refuse("compiling");
                switch (a[0])
                {
                    case "status": return WStatus();
                    case "bodies": if (a.Length > 1 && a[1] == "import") return WBodiesImport(); break;
                    case "routes": if (a.Length > 1 && a[1] == "build") return WRoutesBuild(); break;
                    case "place": return WPlace(true);
                    case "clear": return WPlace(false);
                    case "check": return WCheck();
                    case "shots": return a.Length > 1 && a[1] == "off" ? WPreviewOff() : WShots(a.Length > 1 ? a[1] : "people", a.Length > 2 ? a[2] : "");
                    case "cost": return WCost();
                    case "on": return WSwitch(true);
                    case "off": return WSwitch(false);
                }
                return "REFUSED unknown command '" + c + "' (status | bodies:import | routes:build | place | clear | check | cost | on | off | shots:lineup | shots:walk | shots:people | shots:off)";
            }
            catch (Refuse r) { return "REFUSED " + r.Message; }
            catch (Exception e) { return "error " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace; }
        }

        static string WStatus()
        {
            var sb = new StringBuilder("Walkers308 status\n"); var f = File.Exists(WFilePath) ? JsonUtility.FromJson<WFile>(File.ReadAllText(WFilePath)) : null;
            sb.AppendLine("  plan " + (f == null ? "MISSING " + WFilePath : f.version + " routes " + f.routes.Count + " walkers " + f.walkers.Count + " posts " + f.posts.Count + " stops " + f.stops.Count));
            sb.AppendLine("  lite people folder " + (AssetDatabase.IsValidFolder(WLite) ? "present" : "absent (bodies_extract.py apply)"));
            var profile = AssetDatabase.LoadAssetAtPath<TownWalkerProfile308>(WProfile); sb.AppendLine("  profile " + (profile == null ? "absent (deploy.py apply)" : "usable " + profile.Usable + " cap " + profile.HardCap));
            var routes = AssetDatabase.LoadAssetAtPath<TownWalkerRoutes308>(WRoutes); sb.AppendLine("  routes asset " + (routes == null ? "absent (routes:build)" : routes.Source + " walkers " + routes.Walkers.Length + " posts " + routes.Posts.Length));
            if (f != null) foreach (var b in f.bodies) { var p = AssetDatabase.LoadAssetAtPath<TownWalkerBody308>(b.prefab); sb.AppendLine("  body " + b.key + " " + (p == null ? "absent (bodies:import)" : "usable " + p.Usable + " tris " + p.Triangles + " stride " + F(p.StrideMetres, "F2") + " human " + (p.Animator != null && p.Animator.isHuman) + " jobs " + p.Jobs.Count(x => x != null))); }
            var realm = SceneManager.GetSceneByPath(RealmScene);
            var mainS = SceneManager.GetSceneByPath(PlayScene); sb.AppendLine("  host in the play scene " + (mainS.IsValid() && mainS.isLoaded && mainS.GetRootGameObjects().Any(g => g.name == WHost) ? "placed" : "not placed"));
            sb.Append("  props unit " + (!realm.IsValid() || !realm.isLoaded ? "realm scene not open (place / check open it themselves)" : (WFindUnit(realm) != null ? "placed" : "not placed")));
            return sb.ToString();
        }
        static Transform WFindUnit(Scene realm) { var root = RealmRootOf(realm); if (root == null) return null; for (int i = 0; i < root.childCount; i++) if (root.GetChild(i).name == WUnit) return root.GetChild(i); return null; }

        // ---------- bodies ----------
        // PeopleLite308 (Tools/Blender/PeopleLite308): one FBX per body = the pack's own skeleton (same bone names, hierarchy and bind pose, finger /
        // spare skirt bones removed) + <Name>_LOD0 / _LOD1 / _LOD2 meshes, one alpha-clipped material, one 1024 atlas per colour variant.
        // The pack's in-place Generic .anim clips bind by transform path, so they drive the lite skeleton as they are (curves of removed bones are skipped).
        static string WBodiesImport()
        {
            var f = WLoad(); if (!AssetDatabase.IsValidFolder(WLite)) throw new Refuse("no " + WLite + " - run `python Tools/Unity/Stage308_walkers/bodies_extract.py apply`, then Refresh");
            var lit = Shader.Find("Universal Render Pipeline/Lit"); if (lit == null) throw new Refuse("URP Lit shader not found");
            if (f.tintOrder.Count == 0) throw new Refuse("walkers308.json has no tintOrder");
            var sb = new StringBuilder("Walkers308 bodies:import (PeopleLite308)\n"); int total = 0;
            foreach (var b in f.bodies)
            {
                var mi = AssetImporter.GetAtPath(b.fbx) as ModelImporter; if (mi == null) throw new Refuse("no model " + b.fbx);
                if (b.variants.Count != f.tintOrder.Count) throw new Refuse(b.key + ": variants " + b.variants.Count + " != tintOrder " + f.tintOrder.Count);
                bool change = mi.animationType != ModelImporterAnimationType.Generic || mi.importAnimation || mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.importBlendShapes
                    || mi.importCameras || mi.importLights || mi.isReadable || mi.importTangents != ModelImporterTangents.None || mi.optimizeGameObjects || mi.addCollider;
                if (change)
                {
                    mi.animationType = ModelImporterAnimationType.Generic; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; mi.importAnimation = false;
                    mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false; mi.isReadable = false;
                    mi.importTangents = ModelImporterTangents.None; mi.optimizeGameObjects = false; mi.addCollider = false; mi.SaveAndReimport();
                }
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(b.fbx); if (model == null) throw new Refuse("model did not import: " + b.fbx);
                var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(b.idle); var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(b.walk);
                if (idle == null || walk == null) throw new Refuse(b.key + ": idle / walk clip missing (" + b.idle + " , " + b.walk + ")");
                // one material per colour variant: the same URP Lit settings, only the atlas differs
                string matDir = WLite + "/Materials"; Directory.CreateDirectory(Abs(matDir)); AssetDatabase.ImportAsset(matDir, ImportAssetOptions.ForceSynchronousImport);
                var variants = new Material[f.tintOrder.Count]; int texFixed = 0;
                for (int i = 0; i < variants.Length; i++)
                {
                    var ti = AssetImporter.GetAtPath(b.variants[i]) as TextureImporter; if (ti == null) throw new Refuse("no texture " + b.variants[i]);
                    if (ti.maxTextureSize != 1024 || !ti.mipmapEnabled || !ti.sRGBTexture || ti.alphaIsTransparency != b.alphaClip || ti.mipMapsPreserveCoverage != b.alphaClip)
                    {
                        ti.maxTextureSize = 1024; ti.mipmapEnabled = true; ti.sRGBTexture = true; ti.alphaIsTransparency = b.alphaClip;
                        ti.mipMapsPreserveCoverage = b.alphaClip; if (b.alphaClip) ti.alphaTestReferenceValue = b.cutoff; ti.SaveAndReimport(); texFixed++;
                    }
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(b.variants[i]); if (tex == null) throw new Refuse("texture did not import: " + b.variants[i]);
                    string mp = matDir + "/M_Townsfolk308_" + b.lite + "_" + f.tintOrder[i] + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                    if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, mp); }
                    m.shader = lit; m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", .05f); m.SetFloat("_Metallic", 0);
                    m.SetFloat("_SpecularHighlights", 0); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); m.SetFloat("_EnvironmentReflections", 0); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    m.SetFloat("_Cull", 0);      // two-sided: skirt lining, hat brim underside and hair cards are single sheets
                    if (b.alphaClip) { m.SetFloat("_AlphaClip", 1); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cutoff", b.cutoff); m.renderQueue = 2450; }
                    else { m.SetFloat("_AlphaClip", 0); m.DisableKeyword("_ALPHATEST_ON"); m.renderQueue = -1; }
                    m.enableInstancing = true; EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); variants[i] = m;
                }
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(model, scene); go.name = Path.GetFileNameWithoutExtension(b.prefab);
                    foreach (var col in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);
                    var animator = go.GetComponent<Animator>(); if (animator == null) animator = go.AddComponent<Animator>();
                    animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderBy(r => r.name, StringComparer.Ordinal).ToArray();
                    if (skins.Length != 3 || !skins[0].name.EndsWith("_LOD0", StringComparison.Ordinal) || !skins[2].name.EndsWith("_LOD2", StringComparison.Ordinal))
                        throw new Refuse(b.key + ": expected <Name>_LOD0 / _LOD1 / _LOD2 skinned meshes, found " + string.Join(", ", skins.Select(r => r.name)));
                    var tris = new int[3]; int bones = 0;
                    for (int i = 0; i < 3; i++)
                    {
                        var sk = skins[i]; var mesh = sk.sharedMesh; for (int s = 0; s < mesh.subMeshCount; s++) tris[i] += (int)(mesh.GetIndexCount(s) / 3);
                        sk.sharedMaterials = new[] { variants[0] }; sk.updateWhenOffscreen = false; sk.skinnedMotionVectors = false; sk.quality = i == 2 ? SkinQuality.Bone2 : SkinQuality.Bone4;
                        sk.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes; sk.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off; bones = Mathf.Max(bones, sk.bones.Length);
                    }
                    // LOD switch distances (metres) -> screen-relative heights at a 60 degree vertical field of view (QualitySettings.lodBias scales them at run time)
                    float tall = b.height > 0 ? b.height : 1.7f; float H(float metres) => Mathf.Clamp(tall / (2f * metres * Mathf.Tan(30f * Mathf.Deg2Rad)), .002f, .95f);
                    var group = go.GetComponent<LODGroup>(); if (group == null) group = go.AddComponent<LODGroup>();
                    group.fadeMode = LODFadeMode.None; group.animateCrossFading = false;
                    group.SetLODs(new[] { new LOD(H(b.lod1), new Renderer[] { skins[0] }), new LOD(H(b.lod2), new Renderer[] { skins[1] }), new LOD(H(b.cull), new Renderer[] { skins[2] }) });
                    group.RecalculateBounds();
                    Transform head = WFindDeep(go.transform, b.head), foot = WFindDeep(go.transform, b.foot);
                    string strideNote = "(measured offline: stance-foot speed x clip length)"; float stride = b.stride;
                    if (stride <= 0) stride = WStride(go, walk, foot, out strideNote);
                    var body = go.AddComponent<TownWalkerBody308>();
                    body.Animator = animator; body.Renderers = skins.Cast<Renderer>().ToArray(); body.Head = head; body.Idle = idle; body.Walk = walk; body.Jobs = Array.Empty<AnimationClip>(); body.StrideMetres = stride;
                    body.MaxActive = b.maxActive; body.Variants = variants; body.TintSlots = Array.Empty<TownWalkerBody308.TintSlot>(); body.Triangles = tris[0];
                    Directory.CreateDirectory(Path.GetDirectoryName(Abs(b.prefab))); PrefabUtility.SaveAsPrefabAsset(go, b.prefab, out bool ok); total += tris[0];
                    sb.AppendLine("  " + b.key + " (" + b.lite + "): " + (ok ? "saved " : "FAILED to save ") + b.prefab + " | rig Generic | tris LOD0/1/2 " + tris[0] + " / " + tris[1] + " / " + tris[2]
                        + (tris[0] > f.triangleBudget ? " (OVER the " + f.triangleBudget + " budget)" : "") + " | skin bones " + bones + " | materials 1 x variants " + variants.Length + " (textures re-imported " + texFixed + ")"
                        + " | LOD at " + F(b.lod1, "F0") + " / " + F(b.lod2, "F0") + " m, culled " + F(b.cull, "F0") + " m | head " + (head != null ? head.name : "NONE") + " | stride " + F(stride, "F2") + " m " + strideNote
                        + " | idle " + F(idle.length, "F2") + " s walk " + F(walk.length, "F2") + " s | max active " + b.maxActive);
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            sb.Append("  crowd worst case: " + f.hardCap + " bodies all at LOD0 = " + (f.bodies.Count > 0 ? f.hardCap * (total / f.bodies.Count) : 0) + " triangles per pass, " + f.hardCap + " draw calls per pass (1 material each)");
            return sb.ToString();
        }
        static AnimationClip WClip(string path)
        {
            if (path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)) return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
        }
        static Transform WFindDeep(Transform t, string name) { if (string.IsNullOrEmpty(name)) return null; foreach (var x in t.GetComponentsInChildren<Transform>(true)) if (x.name == name) return x; return null; }
        static Texture WTex(Material m, string prop) => m.HasProperty(prop) ? m.GetTexture(prop) : null;
        // built-in Standard (pink under URP) -> URP Lit, matte; a cutout / fade material becomes alpha-clipped and two-sided (hair cards)
        static bool WToLit(Material m, Shader lit)
        {
            if (m.shader == lit) return false;
            Texture main = WTex(m, "_MainTex") ?? WTex(m, "_BaseMap"), bump = WTex(m, "_BumpMap") ?? WTex(m, "_DetailNormalMap");
            Color c = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white; bool cut = m.IsKeywordEnabled("_ALPHATEST_ON") || m.HasProperty("_Mode") && m.GetFloat("_Mode") >= 1f;
            m.shader = lit; m.SetTexture("_BaseMap", main); m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 1)); m.SetFloat("_Smoothness", .08f); m.SetFloat("_Metallic", 0);
            if (bump != null) { m.SetTexture("_BumpMap", bump); m.EnableKeyword("_NORMALMAP"); }
            if (cut) { m.SetFloat("_AlphaClip", 1); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cutoff", .4f); m.SetFloat("_Cull", 0); m.renderQueue = 2450; }
            m.enableInstancing = true; EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); return true;
        }
        // metres per walk cycle of an in-place clip = twice the fore-aft range of one foot (each foot is on the ground for half the cycle)
        static float WStride(GameObject go, AnimationClip walk, Transform foot, out string note)
        {
            note = ""; if (foot == null) { note = "(no foot bone: 1.30 assumed)"; return 1.3f; }
            float lo = float.MaxValue, hi = float.MinValue; Vector3 start = go.transform.position;
            for (int i = 0; i <= 32; i++) { walk.SampleAnimation(go, walk.length * i / 32f); float z = go.transform.InverseTransformPoint(foot.position).z; lo = Mathf.Min(lo, z); hi = Mathf.Max(hi, z); }
            go.transform.position = start; float stride = (hi - lo) * 2f;
            if (stride < .5f || stride > 2.4f) { note = "(measured " + F(stride, "F2") + " is implausible: 1.30 assumed)"; return 1.3f; }
            return stride;
        }

        // ---------- routes ----------
        static float WTerrainY(Transform terrain, float x, float z, float fallback, ref int rayed, ref int missed)
        {
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(new Vector3(x, 600f, z), Vector3.down, 1200f, ~0, QueryTriggerInteraction.Ignore)) if (h.collider.transform.IsChildOf(terrain) && h.point.y > best) best = h.point.y;
            if (float.IsNegativeInfinity(best)) { missed++; return fallback; }
            rayed++; return best;
        }
        static string WRoutesBuild()
        {
            var f = WLoad(); var main = Main(); var openedR = new List<Scene>(); OpenAdditive(RealmScene, main, openedR); Physics.SyncTransforms();      // the city's colliders (bridge decks, stalls, walls) live in the realm scene
            try {
            var terr = main.GetRootGameObjects().FirstOrDefault(g => g.name == "Reworld292_Terrain"); if (terr == null) throw new Refuse("no Reworld292_Terrain root");
            int rayed = 0, missed = 0; var bodyIndex = f.bodies.Select((b, i) => (b.key, i)).ToDictionary(x => x.key, x => x.i); var tintIndex = f.tintOrder.Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i);
            var lo = new Vector2(float.MaxValue, float.MaxValue); var hi = -lo;
            void Grow(float x, float z) { lo = Vector2.Min(lo, new Vector2(x, z)); hi = Vector2.Max(hi, new Vector2(x, z)); }
            var routes = new List<TownWalkerRoutes308.Route>();
            var unitT = SceneManager.GetSceneByPath(RealmScene) is Scene rs0 && rs0.IsValid() && rs0.isLoaded ? WFindUnit(rs0) : null; var over = new Collider[16]; var said = new List<string>();
            // the walking surface: the terrain, or a floor-like collider (bridge deck, ramp) up to 2 m above it - never a roof, never the walkers' own props
            float Surface(float x, float z, float fallback)
            {
                float ground = float.NegativeInfinity, best = float.NegativeInfinity; var all = Physics.RaycastAll(new Vector3(x, 600f, z), Vector3.down, 1200f, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in all) if (h.collider.transform.IsChildOf(terr.transform) && h.point.y > ground) ground = h.point.y;
                if (float.IsNegativeInfinity(ground)) { missed++; return fallback; }
                best = ground;
                foreach (var h in all) { if (h.collider.transform.IsChildOf(terr.transform) || unitT != null && h.collider.transform.IsChildOf(unitT)) continue; if (h.normal.y > .6f && h.point.y > best && h.point.y < ground + 2f) best = h.point.y; }
                rayed++; return best;
            }
            bool Solid(Vector3 p, float radius)
            {
                int n = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * (radius + .3f), p + Vector3.up * 1.5f, radius, over, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++) { var t = over[i].transform; if (t.IsChildOf(terr.transform) || unitT != null && t.IsChildOf(unitT)) continue; return true; }
                return false;
            }
            foreach (var r in f.routes)
            {
                var pts = new List<Vector3>();
                foreach (var p in r.points) { pts.Add(new Vector3(p.x, 0, p.z)); Grow(p.x, p.z); }
                // a point every WAlong m, every height rayed onto the walking surface
                var dense = new List<Vector3> { pts[0] };
                for (int i = 1; i < pts.Count; i++)
                {
                    float d = Vector2.Distance(new Vector2(pts[i].x, pts[i].z), new Vector2(pts[i - 1].x, pts[i - 1].z)); int n = Mathf.Max(1, Mathf.CeilToInt(d / WAlong));
                    for (int k = 1; k <= n; k++) dense.Add(Vector3.Lerp(pts[i - 1], pts[i], k / (float)n));
                }
                float lastY = 0; for (int i = 0; i < dense.Count; i++) { var q = dense[i]; q.y = lastY = Surface(q.x, q.z, lastY); dense[i] = q; }
                // the cross table: surface height relative to the centreline every WCrossStep m, out to the planned half width and to the farthest stop of this route;
                // the usable half width of each point = how far both sides stay a continuous floor (no step over WStep) and WMargin clear of every collider
                int ri = routes.Count; float reach = r.half; foreach (var s in f.stops) if (s.route == ri) reach = Mathf.Max(reach, Mathf.Abs(s.lat));
                int side = Mathf.CeilToInt((reach + .2f) / WCrossStep), count = side * 2 + 1; var cross = new float[dense.Count * count]; var widths = new float[dense.Count]; var rights = new Vector2[dense.Count]; int centreBlocked = 0;
                for (int i = 0; i < dense.Count; i++)
                {
                    Vector3 a = dense[Mathf.Max(0, i - 1)], b = dense[Mathf.Min(dense.Count - 1, i + 1)]; if (r.loop && (i == 0 || i == dense.Count - 1)) { a = dense[dense.Count - 2]; b = dense[1]; }
                    var right = new Vector3(b.z - a.z, 0, -(b.x - a.x)); right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right; rights[i] = new Vector2(right.x, right.z);
                    for (int k = 0; k < count; k++) { if (k == side) continue; var q = dense[i] + right * ((k - side) * WCrossStep); cross[i * count + k] = Surface(q.x, q.z, dense[i].y) - dense[i].y; }
                    if (Solid(dense[i], .28f)) { centreBlocked++; if (said.Count < 12) said.Add("centre in a collider: " + r.id + " point " + i + " " + dense[i]); }
                    float free = r.half;
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        float ok = 0, prev = 0;
                        for (float l = .25f; l <= r.half + .001f; l += .25f)
                        {
                            float u = sgn * l / WCrossStep + side; int k0 = Mathf.Clamp((int)u, 0, count - 2); float y = Mathf.Lerp(cross[i * count + k0], cross[i * count + k0 + 1], u - k0);
                            var q = dense[i] + right * (sgn * l); q.y = dense[i].y + y;
                            if (Mathf.Abs(y - prev) > WStep || Solid(q, WMargin)) break;
                            ok = l; prev = y;
                        }
                        if (ok > r.half - .25f) ok = r.half;      // the planned half width itself was already kept clear by the plan (build_walkers.py)
                        free = Mathf.Min(free, ok);
                    }
                    widths[i] = free;
                    // a table top, a rail or a bank beside the floor must not pull the feet of someone standing next to it: beyond a step the table keeps the floor's height
                    for (int sgn = -1; sgn <= 1; sgn += 2) for (int k = side + sgn; k >= 0 && k < count; k += sgn) if (Mathf.Abs(cross[i * count + k] - cross[i * count + k - sgn]) > WCliff) cross[i * count + k] = cross[i * count + k - sgn];
                }
                // the width never changes faster than WTaper m per metre along the route (a walker drifts in, it does not jump)
                for (int i = 1; i < dense.Count; i++) widths[i] = Mathf.Min(widths[i], widths[i - 1] + WTaper * Vector3.Distance(dense[i], dense[i - 1]));
                for (int i = dense.Count - 2; i >= 0; i--) widths[i] = Mathf.Min(widths[i], widths[i + 1] + WTaper * Vector3.Distance(dense[i], dense[i + 1]));
                if (r.loop) { float w = Mathf.Min(widths[0], widths[dense.Count - 1]); widths[0] = widths[dense.Count - 1] = w; }
                float jump = 0; for (int i = 1; i < dense.Count; i++) jump = Mathf.Max(jump, Mathf.Abs(dense[i].y - dense[i - 1].y));
                said.Add(r.id + ": points " + dense.Count + " | usable half planned " + F(r.half) + " -> min " + F(widths.Min(), "F2") + " mean " + F(widths.Average(), "F2") + " (narrower than planned at " + widths.Count(w => w < r.half - .01f) + " points) | centre blocked " + centreBlocked + " | largest height step between points " + F(jump, "F2") + " m");
                routes.Add(new TownWalkerRoutes308.Route { Id = r.id, Loop = r.loop, HalfWidth = r.half, Points = dense.ToArray(), CrossCount = count, CrossStep = WCrossStep, Cross = cross, Widths = widths, Rights = rights });
            }
            // stops keep their distance along the route: densifying adds points on the same legs, the length does not change
            var stops = f.stops.OrderBy(s => s.route).ThenBy(s => s.s).Select(s => new TownWalkerRoutes308.Stop { Route = s.route, Distance = s.s, Lateral = s.lat, Yaw = s.yaw }).ToArray();
            int Idx(Dictionary<string, int> d, string k, string what) { if (!d.TryGetValue(k ?? "", out int i)) throw new Refuse("unknown " + what + " '" + k + "'"); return i; }
            var walkers = f.walkers.Select(w => new TownWalkerRoutes308.Walker { Role = w.role, Route = w.route, Start = w.start, Direction = w.dir < 0 ? -1 : 1, Lane = w.lane, Body = Idx(bodyIndex, w.body, "body"), Tint = Idx(tintIndex, w.tint, "tint"), Speed = w.speed, Seed = w.seed }).ToArray();
            var posts = f.posts.Select(p => { float y = Surface(p.x, p.z, p.y > WNoY ? p.y : 0); Grow(p.x, p.z);
                return new TownWalkerRoutes308.Post { Role = p.role, Position = new Vector3(p.x, y, p.z), Yaw = p.yaw, Body = Idx(bodyIndex, p.body, "body"), Tint = Idx(tintIndex, p.tint, "tint"), Job = p.job, Seed = p.seed }; }).ToArray();
            if (missed > 0) throw new Refuse(missed + " points have no terrain under them (is the terrain of the capital loaded in the play scene?) - nothing written");
            Directory.CreateDirectory(Abs(WData)); AssetDatabase.ImportAsset("Assets/_Project/Data/World", ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ImportRecursive);
            var asset = AssetDatabase.LoadAssetAtPath<TownWalkerRoutes308>(WRoutes); bool fresh = asset == null;
            if (fresh) asset = ScriptableObject.CreateInstance<TownWalkerRoutes308>();
            asset.Source = f.version + " " + Stamp(); asset.Bodies = f.bodies.Select(b => b.key).ToArray(); asset.Routes = routes.ToArray(); asset.Stops = stops; asset.Walkers = walkers; asset.Posts = posts; asset.BoundsMin = lo; asset.BoundsMax = hi;
            if (fresh) AssetDatabase.CreateAsset(asset, WRoutes); else { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
            return "Walkers308 routes:build -> " + WRoutes + " | routes " + routes.Count + " points " + routes.Sum(r => r.Points.Length) + " stops " + stops.Length + " walkers " + walkers.Length + " posts " + posts.Length
                + " | heights rayed " + rayed + " | box " + lo + " .. " + hi + "\n  " + string.Join("\n  ", said);
            } finally { foreach (var sc in openedR) EditorSceneManager.CloseScene(sc, true); }
        }

        // ---------- place / clear ----------
        static string WPlace(bool build)
        {
            var f = WLoad(); var main = Main(); if (main.isDirty) throw new Refuse("the play scene is dirty");
            TownWalkerProfile308 profile = null; TownWalkerRoutes308 routes = null; var prefabs = new List<TownWalkerBody308>();
            if (build)
            {
                profile = AssetDatabase.LoadAssetAtPath<TownWalkerProfile308>(WProfile); if (profile == null) throw new Refuse("no " + WProfile + " (deploy.py apply, then Refresh)");
                routes = AssetDatabase.LoadAssetAtPath<TownWalkerRoutes308>(WRoutes); if (routes == null) throw new Refuse("no " + WRoutes + " (routes:build)");
                foreach (var b in f.bodies) { var p = AssetDatabase.LoadAssetAtPath<TownWalkerBody308>(b.prefab); if (p == null || !p.Usable) throw new Refuse("body " + b.key + " missing or not usable (bodies:import)"); prefabs.Add(p); }
                if (!routes.Bodies.SequenceEqual(f.bodies.Select(b => b.key))) throw new Refuse("routes asset body order differs from walkers308.json (routes:build again)");
            }
            var opened = new List<Scene>(); var realm = OpenAdditive(RealmScene, main, opened); var sb = new StringBuilder("Walkers308 " + (build ? "place" : "clear") + "\n");
            try
            {
                var root = RealmRootOf(realm); if (root == null) throw new Refuse("no Realm_Hwanggyeong root"); if (realm.isDirty) throw new Refuse("the realm scene is dirty");
                var manifest = JsonUtility.FromJson<MFile>(File.ReadAllText(ManifestFile)); var mine = manifest.units.Where(u => u.realm == "Hwanggyeong").ToList();
                if (mine.Count != root.childCount) throw new Refuse("manifest Hwanggyeong units " + mine.Count + " != realm children " + root.childCount);
                foreach (var old in main.GetRootGameObjects().Where(g => g.name == WHost).ToArray()) { UnityEngine.Object.DestroyImmediate(old); sb.AppendLine("  removed " + WHost + " (play scene)"); }
                var parent = main.GetRootGameObjects().FirstOrDefault(g => g.name == MainParent); if (parent == null) throw new Refuse("no '" + MainParent + "' parent in the play scene (the city is not built)");
                string backup = Path.Combine(WStage, "Backup", Stamp() + "_" + (build ? "place" : "clear")); Directory.CreateDirectory(backup);
                foreach (string file in new[] { Abs(RealmScene), ManifestFile, Abs("Assets/_Project/Scenes/World/Stream/RealmStreamSheet308.asset"), Abs(PlayScene) }) if (File.Exists(file)) File.Copy(file, Path.Combine(backup, Path.GetFileName(file)), true);
                for (int c = root.childCount - 1; c >= 0; c--)
                {
                    var ch = root.GetChild(c); if (ch.name != WUnit) continue; var row = mine[c];
                    if (row.name != WUnit || row.parentPath != MainParent) throw new Refuse("realm child " + c + " is not the walkers unit in the manifest");
                    manifest.units.Remove(row); mine.RemoveAt(c); UnityEngine.Object.DestroyImmediate(ch.gameObject); sb.AppendLine("  removed " + WUnit);
                }
                if (build)
                {
                    var unit = new GameObject(WUnit); SceneManager.MoveGameObjectToScene(unit, realm); unit.transform.SetParent(root, true);
                    var host = new GameObject(WHost); SceneManager.MoveGameObjectToScene(host, main);      // a root of the play scene, like the stream loader: realm scenes hold static parts only (RealmStream308 AC-S2)
                    var comp = host.AddComponent<TownWalkers308>(); comp.Profile = profile; comp.Routes = routes; comp.BodyPrefabs = prefabs.ToArray();
                    comp.Session = main.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).FirstOrDefault();
                    var props = new GameObject("Props"); props.transform.SetParent(unit.transform, false); int placed = 0, missing = 0;
                    foreach (var p in f.posts)
                    {
                        if (string.IsNullOrEmpty(p.prop)) continue; var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p.prop); if (prefab == null) { missing++; sb.AppendLine("  prop missing " + p.prop); continue; }
                        var post = routes.Posts.FirstOrDefault(x => x.Seed == p.seed); if (post == null) continue;
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, realm); go.transform.SetParent(props.transform, true);
                        var rot = Quaternion.Euler(0, post.Yaw, 0); go.transform.SetPositionAndRotation(post.Position + rot * new Vector3(p.propX, 0, p.propZ), Quaternion.Euler(0, post.Yaw + 35f, 0));
                        foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;   // set dressing beside a figure: never a solid on the street
                        go.name = "Prop_" + p.role + "_" + placed++;
                    }
                    Count(unit.transform, out int rn, out long vn);
                    manifest.units.Add(new MUnit { realm = "Hwanggyeong", name = WUnit, parentGid = GlobalObjectId.GetGlobalObjectIdSlow(parent).ToString(), parentPath = MainParent, sibling = manifest.units.Count(u => u.realm == "Hwanggyeong" && u.parentPath == MainParent), renderers = rn, vertices = vn });
                    sb.AppendLine("  built " + WUnit + ": people " + (routes.Walkers.Length + routes.Posts.Length) + " (walkers " + routes.Walkers.Length + ", posts " + routes.Posts.Length + ") cap " + profile.HardCap + " | ground props " + placed + (missing > 0 ? " missing " + missing : ""));
                }
                manifest.stamp = Stamp(); File.WriteAllText(ManifestFile, JsonUtility.ToJson(manifest, true));
                EditorSceneManager.MarkSceneDirty(realm); EditorSceneManager.MarkSceneDirty(main); bool ok = EditorSceneManager.SaveScene(realm) && EditorSceneManager.SaveScene(main);
                sb.Append(ok ? "saved (realm scene + play scene)" : "FAILED a scene was not saved").Append(" | backup " + backup + " | " + SyncSheet());
            }
            finally { foreach (var s in opened) EditorSceneManager.CloseScene(s, true); }
            return sb.ToString();
        }

        // ---------- check ----------
        static string WCheck()
        {
            var f = WLoad(); var main = Main(); var sb = new StringBuilder("Walkers308 check\n"); int fails = 0;
            void C(bool ok, string what) { sb.AppendLine((ok ? "  PASS " : "  FAIL ") + what); if (!ok) fails++; }
            var profile = AssetDatabase.LoadAssetAtPath<TownWalkerProfile308>(WProfile); var routes = AssetDatabase.LoadAssetAtPath<TownWalkerRoutes308>(WRoutes);
            C(profile != null && profile.Usable, "profile present and usable"); C(routes != null, "routes asset present"); if (profile == null || routes == null) return sb.Append("FAIL " + fails).ToString();
            C(profile.HardCap <= 24 && profile.HideDistance > profile.ShowDistance && profile.PopDistance <= profile.ShowDistance, "caps: hard cap " + profile.HardCap + " (<= 24), show " + F(profile.ShowDistance) + " < hide " + F(profile.HideDistance) + ", pop " + F(profile.PopDistance));
            C(profile.Tints != null && profile.Tints.Length >= f.tintOrder.Count, "tints " + (profile.Tints == null ? 0 : profile.Tints.Length) + " >= " + f.tintOrder.Count);
            C(routes.Walkers.Length == f.walkers.Count && routes.Posts.Length == f.posts.Count && routes.Stops.Length == f.stops.Count, "routes asset matches walkers308.json (walkers " + routes.Walkers.Length + ", posts " + routes.Posts.Length + ", stops " + routes.Stops.Length + ")");
            // bodies
            var errorShader = Shader.Find("Hidden/InternalErrorShader"); var bodies = new List<TownWalkerBody308>();
            foreach (var b in f.bodies)
            {
                var p = AssetDatabase.LoadAssetAtPath<TownWalkerBody308>(b.prefab); bodies.Add(p); if (p == null) { C(false, "body " + b.key + " prefab present"); continue; }
                var bad = p.Renderers.Where(r => r != null).SelectMany(r => r.sharedMaterials).Where(m => m == null || m.shader == null || m.shader == errorShader || !m.shader.isSupported || m.shader.name == "Standard" || m.shader.name.StartsWith("Legacy Shaders", StringComparison.Ordinal)).ToList();
                C(p.Usable && p.Idle != null && p.Walk != null && !p.Idle.legacy && !p.Walk.legacy, "body " + b.key + " usable, clips valid (stride " + F(p.StrideMetres, "F2") + " m, jobs " + p.Jobs.Count(x => x != null) + ")");
                C(bad.Count == 0, "body " + b.key + " materials render under URP (bad " + bad.Count + ")");
                bool inert = p.GetComponentsInChildren<Collider>(true).Length == 0 && p.GetComponentsInChildren<EnemyVitals>(true).Length == 0 && p.GetComponentsInChildren<WorldMacroContentPoint>(true).Length == 0 && p.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true).Length == 0;
                C(inert, "body " + b.key + " not targetable (no collider, EnemyVitals, content point, agent)");
                sb.AppendLine("  " + (p.Triangles <= f.triangleBudget ? "PASS " : "WARN ") + "body " + b.key + " triangles " + p.Triangles + " (budget " + f.triangleBudget + ", max active " + p.MaxActive + ")");
            }
            // ground and clearance (the city's colliders live in the realm scene: open it for the measure)
            var opened = new List<Scene>(); var realm = OpenAdditive(RealmScene, main, opened);
            try
            {
                Physics.SyncTransforms(); var terr = main.GetRootGameObjects().FirstOrDefault(g => g.name == "Reworld292_Terrain"); var unit = WFindUnit(realm);
                var hits = new Collider[16]; int samples = 0, blocked = 0, tight = 0, offGround = 0, noGround = 0; float worst = 0, narrow = float.MaxValue; var notes = new List<string>();
                bool Blocked(Vector3 p, float radius, out string who)
                {
                    who = ""; int n = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * (radius + .3f), p + Vector3.up * 1.5f, radius, hits, ~0, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++) { var t = hits[i].transform; if (terr != null && t.IsChildOf(terr.transform)) continue; if (unit != null && t.IsChildOf(unit)) continue; who = KeyOf(t); return true; }
                    return false;
                }
                // feet: the walking surface (any collider under a ray from chest height) against the baked height
                void Feet(Vector3 p, string label)
                {
                    float best = float.NaN; Collider on = null;
                    foreach (var h in Physics.RaycastAll(new Vector3(p.x, p.y + 1.2f, p.z), Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore)) { if (unit != null && h.collider.transform.IsChildOf(unit)) continue; if (float.IsNaN(best) || h.point.y > best) { best = h.point.y; on = h.collider; } }
                    if (float.IsNaN(best)) { noGround++; notes.Add("no ground under " + label + " " + p); return; }
                    float d = Mathf.Abs(best - p.y); if (d > worst) worst = d; if (d > WFeet) { offGround++; notes.Add("feet " + F(best - p.y, "F2") + " m (ground - baked) " + label + " " + p + " on " + (terr != null && on.transform.IsChildOf(terr.transform) ? "terrain" : KeyOf(on.transform))); }
                }
                void Probe(Vector3 p, string label, float margin)
                {
                    samples++; Feet(p, label);
                    if (Blocked(p, .28f, out string who)) { blocked++; notes.Add("in a collider " + label + " " + p + " <- " + who); }
                    else if (margin > .28f && Blocked(p, margin, out who)) { tight++; notes.Add("closer than " + F(margin, "F2") + " m to a collider " + label + " " + p + " <- " + who); }
                }
                var cums = routes.Routes.Select(r => WCum(r.Points)).ToArray();
                for (int r = 0; r < routes.Routes.Length; r++)
                {
                    var rt = routes.Routes[r]; float length = cums[r][cums[r].Length - 1];
                    for (float s = .37f; s <= length; s += 1f)      // between the baked points, not on them
                    {
                        float w = WHalf(rt, cums[r], s); narrow = Mathf.Min(narrow, w);
                        Probe(WOn(rt, cums[r], s, 0), rt.Id + " centre s " + F(s, "F0"), .28f); Probe(WOn(rt, cums[r], s, w), rt.Id + " right edge s " + F(s, "F0"), .55f); Probe(WOn(rt, cums[r], s, -w), rt.Id + " left edge s " + F(s, "F0"), .55f);
                        Feet(WOn(rt, cums[r], s, w * .5f), rt.Id + " right lane s " + F(s, "F0")); Feet(WOn(rt, cums[r], s, -w * .5f), rt.Id + " left lane s " + F(s, "F0"));
                    }
                }
                int feetSamples = samples / 3 * 5;      // centre + two edges (Probe) + two lanes (Feet) at every metre
                C(noGround == 0 && offGround <= feetSamples / 200 && worst <= .25f, "routes on the ground across the usable width (centre, both lanes, both edges every 1 m): feet within " + F(WFeet, "F2") + " m at " + (feetSamples - offGround) + " of " + feetSamples
                    + " samples; off " + offGround + " (allowed 0.5 %: the terrain has sub-metre bumps the 1 m x 0.25 m table does not hold), worst " + F(worst, "F3") + " m (allowed 0.25), no ground " + noGround);
                C(blocked == 0, "route samples inside a collider: " + blocked + " of " + samples + " (centre and both edges of the usable width)");
                C(tight == 0, "route edges (the usable half width of each point, narrowest " + F(narrow, "F2") + " m) keep 0.55 m from every collider (closer " + tight + ")");
                samples = 0; blocked = 0; tight = 0; offGround = 0; noGround = 0; worst = 0; int crossing = 0;
                foreach (var st in routes.Stops)
                {
                    var rt = routes.Routes[st.Route]; Probe(WOn(rt, cums[st.Route], st.Distance, st.Lateral), "stop " + rt.Id + " s " + F(st.Distance, "F0"), .28f);
                    for (float l = WHalf(rt, cums[st.Route], st.Distance); l < Mathf.Abs(st.Lateral) - .2f; l += .25f) if (Blocked(WOn(rt, cums[st.Route], st.Distance, Mathf.Sign(st.Lateral) * l), .28f, out _)) { crossing++; break; }
                }
                C(blocked == 0 && crossing == 0 && offGround == 0 && noGround == 0, "stops free and on the ground: " + routes.Stops.Length + " stops (in a collider " + blocked + ", sideways step blocked " + crossing + ", feet off " + offGround + ", no ground " + noGround + ", worst feet " + F(worst, "F3") + " m)");
                samples = 0; blocked = 0; offGround = 0; noGround = 0; worst = 0;
                foreach (var p in routes.Posts) Probe(p.Position, "post " + p.Role, .28f);
                C(blocked == 0 && offGround == 0 && noGround == 0, "posts free and on the ground: " + routes.Posts.Length + " posts (in a collider " + blocked + ", feet off " + offGround + ", worst feet " + F(worst, "F3") + " m)");
                C(routes.Walkers.All(w => w.Lane <= routes.Routes[w.Route].HalfWidth), "every walker lane lies inside the usable half width of its route");
                foreach (string n in notes.Take(14)) sb.AppendLine("    " + n); if (notes.Count > 14) sb.AppendLine("    ... " + (notes.Count - 14) + " more in Out/check_detail.txt");
                Directory.CreateDirectory(Path.Combine(WStage, "Out")); File.WriteAllLines(Path.Combine(WStage, "Out", "check_detail.txt"), notes);
                C(unit != null, "unit " + WUnit + " placed in the realm scene");
                if (unit != null)
                {
                    var hosts = main.GetRootGameObjects().Where(g => g.name == WHost).ToArray(); var comp = hosts.Length == 1 ? hosts[0].GetComponent<TownWalkers308>() : null;
                    C(hosts.Length == 1 && comp != null && comp.Session != null && UnityEngine.Object.FindObjectsByType<TownWalkers308>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1, "one " + WHost + " root in the play scene with the component and the session (the realm scene holds only the ground props)");
                    C(unit.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, "the realm unit holds no script (RealmStream308 AC-S2)");
                    C(comp != null && comp.Profile == profile && comp.Routes == routes && comp.BodyPrefabs.SequenceEqual(bodies) && comp.BodyPrefabs.Length == routes.Bodies.Length, "unit wiring (profile, routes, body prefabs in the routes' order)");
                    C(unit.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), "unit has no enabled collider (ground props are set dressing)");
                    C(unit.GetComponentsInChildren<EnemyVitals>(true).Length == 0, "unit holds no EnemyVitals (lock-on candidates come from EnemyVitals only)");
                }
            }
            finally { foreach (var s in opened) EditorSceneManager.CloseScene(s, true); }
            sb.AppendLine("  NOT CHECKED here: walking, yielding, the head turn, pop-in, the fight reaction - Play only");
            string verdict = fails == 0 ? "PASS (edit-mode checks)" : "FAIL " + fails; Directory.CreateDirectory(Path.Combine(WStage, "Out")); File.WriteAllText(Path.Combine(WStage, "Out", "check.txt"), sb + verdict);
            return sb.Append(verdict).ToString();
        }

        static float[] WCum(Vector3[] pts) { var c = new float[pts.Length]; for (int i = 1; i < pts.Length; i++) c[i] = c[i - 1] + Vector2.Distance(new Vector2(pts[i].x, pts[i].z), new Vector2(pts[i - 1].x, pts[i - 1].z)); return c; }
        // the same place the run-time puts a person: route point at s + right x lateral, height = centreline + cross table
        static Vector3 WOn(TownWalkerRoutes308.Route rt, float[] cum, float s, float lat) => WOn(rt, cum, s, lat, out _);
        static float WHalf(TownWalkerRoutes308.Route rt, float[] cum, float s) { int i = 0; while (i < rt.Points.Length - 2 && s > cum[i + 1]) i++; float len = cum[i + 1] - cum[i]; return rt.HalfAt(i, len > 0 ? Mathf.Clamp01((s - cum[i]) / len) : 0); }
        static Vector3 WOn(TownWalkerRoutes308.Route rt, float[] cum, float s, float lat, out float yaw)
        {
            var pts = rt.Points; int i = 0; while (i < pts.Length - 2 && s > cum[i + 1]) i++;
            float len = cum[i + 1] - cum[i], t = len > 0 ? Mathf.Clamp01((s - cum[i]) / len) : 0; var p = Vector3.LerpUnclamped(pts[i], pts[i + 1], t);
            var tan = new Vector2(pts[i + 1].x - pts[i].x, pts[i + 1].z - pts[i].z).normalized; yaw = Mathf.Atan2(tan.x, tan.y) * Mathf.Rad2Deg;
            var right = rt.RightAt(i, t, tan); p.x += right.x * lat; p.z += right.y * lat; p.y += rt.Rise(i, t, lat); return p;
        }

        // ---------- switch ----------
        static string WSwitch(bool on)
        {
            var profile = AssetDatabase.LoadAssetAtPath<TownWalkerProfile308>(WProfile); if (profile == null) throw new Refuse("no " + WProfile);
            profile.Enabled = on; EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            return "Walkers308 " + (on ? "on" : "off") + ": profile Enabled " + profile.Enabled + ", usable " + profile.Usable + (on ? "" : " (no body is ever taken out; the unit costs one flag test a frame)");
        }

        // ---------- cost (edit-mode count: the whole pool instantiated once, never saved) ----------
        static string WCost()
        {
            var f = WLoad(); var profile = AssetDatabase.LoadAssetAtPath<TownWalkerProfile308>(WProfile); var routes = AssetDatabase.LoadAssetAtPath<TownWalkerRoutes308>(WRoutes);
            if (profile == null || routes == null) throw new Refuse("routes / profile asset missing");
            var prefabs = f.bodies.Select(b => AssetDatabase.LoadAssetAtPath<TownWalkerBody308>(b.prefab)).ToArray(); if (prefabs.Any(p => p == null)) throw new Refuse("a body prefab is missing (bodies:import)");
            // the people the pool would hold at the busiest spot: the HardCap nearest to the place with most people within ShowDistance
            var cums = routes.Routes.Select(r => WCum(r.Points)).ToArray(); var where = new List<(Vector3 p, int body)>();
            foreach (var w in routes.Walkers) { var rt = routes.Routes[w.Route]; where.Add((WOn(rt, cums[w.Route], w.Start * cums[w.Route][cums[w.Route].Length - 1], (w.Direction > 0 ? -1 : 1) * Mathf.Min(w.Lane, WHalf(rt, cums[w.Route], w.Start * cums[w.Route][cums[w.Route].Length - 1]))), w.Body)); }
            foreach (var p in routes.Posts) where.Add((p.Position, p.Body));
            Vector3 busiest = Vector3.zero; int most = -1;
            foreach (var c in where) { int n = where.Count(o => Vector2.Distance(new Vector2(o.p.x, o.p.z), new Vector2(c.p.x, c.p.z)) < profile.ShowDistance); if (n > most) { most = n; busiest = c.p; } }
            var pool = where.OrderBy(o => (o.p - busiest).sqrMagnitude).Take(profile.HardCap).ToList();
            var root = new GameObject("Walkers308_Cost") { hideFlags = HideFlags.HideAndDontSave }; int renderers = 0, bones = 0, objects = 0; long lod0 = 0, lod1 = 0, lod2 = 0, typical = 0; var mats = new HashSet<Material>();
            int near = 0, mid = 0;
            try
            {
                foreach (var o in pool)
                {
                    var b = UnityEngine.Object.Instantiate(prefabs[o.body], o.p, Quaternion.identity, root.transform); objects += b.GetComponentsInChildren<Transform>(true).Length;
                    var sk = b.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderBy(r => r.name, StringComparer.Ordinal).ToArray(); renderers += b.GetComponentsInChildren<Renderer>(true).Length;
                    float d = (o.p - busiest).magnitude; var wb = f.bodies[o.body]; int use = d < wb.lod1 ? 0 : d < wb.lod2 ? 1 : 2; if (use == 0) near++; else if (use == 1) mid++;
                    for (int i = 0; i < sk.Length; i++)
                    {
                        long t = 0; for (int m = 0; m < sk[i].sharedMesh.subMeshCount; m++) t += sk[i].sharedMesh.GetIndexCount(m) / 3;
                        if (i == 0) { lod0 += t; bones += sk[i].bones.Length; } else if (i == 1) lod1 += t; else lod2 += t;
                        if (i == use) typical += t; foreach (var m in sk[i].sharedMaterials) mats.Add(m);
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            return "Walkers308 cost (edit-mode count, pool of " + pool.Count + " = HardCap " + profile.HardCap + ", instantiated and destroyed, nothing saved)\n"
                + "  busiest point " + busiest.ToString("F0") + ": " + most + " of " + where.Count + " people within ShowDistance " + F(profile.ShowDistance, "F0") + " m\n"
                + "  renderers added " + renderers + " (3 LOD renderers a body, 1 drawn at a time -> " + pool.Count + " draws a pass) | transforms " + objects + " | skinned bones (LOD0) " + bones + " | materials shared " + mats.Count + "\n"
                + "  triangles: all LOD0 " + lod0 + " (worst case a pass) | all LOD1 " + lod1 + " | all LOD2 " + lod2 + " | standing at that point with the LOD distances (" + near + " at LOD0, " + mid + " at LOD1) " + typical + "\n"
                + "  NOT MEASURED: frame time, skinning cost, shadow passes - Play only";
        }

        // ---------- stills ----------
        static string WPreviewOff()
        {
            int n = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++) foreach (var g in SceneManager.GetSceneAt(i).GetRootGameObjects()) if (g.name == WPreview) { UnityEngine.Object.DestroyImmediate(g); n++; }
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>()) if (g != null && g.name == WPreview && !EditorUtility.IsPersistent(g)) { UnityEngine.Object.DestroyImmediate(g); n++; }
            return "Walkers308 shots:off removed " + n + " preview root(s)";
        }
        // temporary bodies (HideAndDontSave: drawn, never saved, the scene is not dirtied). Sets:
        //   shots:lineup[:<s>]  every body x colour standing in a row on Jongno at distance s (default: the middle), in front of the shops
        //   shots:walk[:<s>]    every body in a sampled walk pose on four lanes of Jongno at s, + each body at four phases of its walk clip
        //   shots:people        all people at their places (posts standing, walkers mid-stride, a customer at every third stop)
        static string WShots(string set, string arg)
        {
            var f = WLoad(); Main(); WPreviewOff(); var routes = AssetDatabase.LoadAssetAtPath<TownWalkerRoutes308>(WRoutes); var profile = AssetDatabase.LoadAssetAtPath<TownWalkerProfile308>(WProfile);
            if (routes == null || profile == null) throw new Refuse("routes / profile asset missing");
            var prefabs = f.bodies.Select(b => AssetDatabase.LoadAssetAtPath<TownWalkerBody308>(b.prefab)).ToArray(); if (prefabs.Any(p => p == null)) throw new Refuse("a body prefab is missing (bodies:import)");
            var root = new GameObject(WPreview) { hideFlags = HideFlags.HideAndDontSave }; var sb = new StringBuilder("Walkers308 shots:" + set + " (preview bodies are temporary: run `Walkers308 shots:off` after the stills)\n"); int made = 0, shot = 0;
            var cums = routes.Routes.Select(r => WCum(r.Points)).ToArray();
            TownWalkerBody308 Stand(Vector3 at, float yaw, int bodyIndex, int tint, AnimationClip clip, float time)
            {
                var b = UnityEngine.Object.Instantiate(prefabs[bodyIndex], at, Quaternion.Euler(0, yaw, 0), root.transform);
                foreach (var t in b.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                if (clip != null) { clip.SampleAnimation(b.gameObject, Mathf.Repeat(time, clip.length)); b.transform.SetPositionAndRotation(at, Quaternion.Euler(0, yaw, 0)); }
                if (b.Variants != null && b.Variants.Length > 0) { var vm = b.Variants[Mathf.Abs(tint) % b.Variants.Length]; foreach (var vr in b.Renderers) if (vr != null && vm != null) vr.sharedMaterial = vm; }
                made++; return b;
            }
            void Shot(string label, Vector3 eye, Vector3 target, float fov = 40f) => sb.AppendLine("  shot:walkers308_" + set + "_" + (shot++).ToString("D2") + "_" + label + ":" + WV(eye) + ":" + WV(target) + ":fov=" + F(fov, "F0") + ":w=1280:h=720:hideplayer");
            int jr = Array.FindIndex(routes.Routes, r => r.Id == "jongno_s"); if (jr < 0) jr = 0; var jong = routes.Routes[jr]; float jl = cums[jr][cums[jr].Length - 1];
            float sAt = float.TryParse(arg, System.Globalization.NumberStyles.Float, Inv, out float parsed) ? parsed : -1f;
            if (set == "lineup")
            {
                float s0 = sAt >= 0 ? sAt : jl * .5f; WOn(jong, cums[jr], s0, 0, out float yaw); var along = Quaternion.Euler(0, yaw, 0) * Vector3.forward; var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                // three groups (one body each) of four colours, 0.85 m apart along the street on the right edge of the usable width, facing the street centre
                for (int b = 0; b < prefabs.Length; b++)
                {
                    float gs = s0 + (b - (prefabs.Length - 1) * .5f) * 5f;
                    for (int t = 0; t < f.tintOrder.Count; t++) Stand(WOn(jong, cums[jr], gs + (t - 1.5f) * .85f, jong.HalfWidth), yaw - 90f, b, t, prefabs[b].Idle, prefabs[b].Idle.length * .3f);
                    Vector3 g = WOn(jong, cums[jr], gs, jong.HalfWidth), up = Vector3.up;
                    Shot("front4_" + f.bodies[b].key, g - right * 4.2f + up * 1.45f, g + up * .95f); Shot("q4_" + f.bodies[b].key, g - right * 3.4f + along * 2.6f + up * 1.5f, g + up * .95f);
                    Shot("back4_" + f.bodies[b].key, g + right * 3.6f + along * 1.2f + up * 1.5f, g + up * .95f);
                }
                Vector3 m = WOn(jong, cums[jr], s0, jong.HalfWidth); Shot("front12_all", m - right * 12f + Vector3.up * 1.6f, m + Vector3.up * .9f, 50f); Shot("q12_all", m - right * 9f + along * 9f + Vector3.up * 1.6f, m + Vector3.up * .9f, 45f);
                Shot("far30_all", m - right * 6f + along * 30f + Vector3.up * 1.7f, m + Vector3.up * .9f, 40f);
            }
            else if (set == "walk")
            {
                float s0 = sAt >= 0 ? sAt : jl * .5f; var c = WOn(jong, cums[jr], s0, 0, out float yaw); var along = Quaternion.Euler(0, yaw, 0) * Vector3.forward; var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                float[] lanes = { -jong.HalfWidth, -jong.HalfWidth * .45f, jong.HalfWidth * .45f, jong.HalfWidth };
                // row A (at s0): one body per lane, mid-stride, the lanes alternate direction as at run time (left of travel)
                for (int k = 0; k < lanes.Length; k++) { int b = k % prefabs.Length; Stand(WOn(jong, cums[jr], s0 + (k % 2) * .6f, lanes[k]), lanes[k] < 0 ? yaw : yaw + 180f, b, k % f.tintOrder.Count, prefabs[b].Walk, prefabs[b].Walk.length * (.25f + .5f * (k % 2))); }
                Shot("across_feet", c + along * 5.5f + Vector3.up * .45f, c + Vector3.up * .35f, 50f); Shot("across", c + along * 6f + Vector3.up * 1.5f, c + Vector3.up * .9f, 45f);
                Shot("side_right", c + right * (jong.HalfWidth + 3.5f) + Vector3.up * 1.2f, c + Vector3.up * .8f, 45f); Shot("side_left", c - right * (jong.HalfWidth + 3.5f) + Vector3.up * 1.2f, c + Vector3.up * .8f, 45f);
                // row B (12 m further): the woman alone at four phases of her walk clip, 1.3 m apart on the centreline - looked at from both sides and from front / back
                int fem = f.bodies.FindIndex(b => b.key == "female"); if (fem < 0) fem = prefabs.Length - 1; float sB = s0 + 12f; var cb = WOn(jong, cums[jr], sB, 0);
                for (int k = 0; k < 4; k++) Stand(WOn(jong, cums[jr], sB + (k - 1.5f) * 1.3f, 0), yaw, fem, k, prefabs[fem].Walk, prefabs[fem].Walk.length * k / 4f);
                Shot("female_right", cb + right * 4.4f + Vector3.up * 1.1f, cb + Vector3.up * .85f, 45f); Shot("female_left", cb - right * 4.4f + Vector3.up * 1.1f, cb + Vector3.up * .85f, 45f);
                Shot("female_front", cb + along * 5.2f + right * 1.2f + Vector3.up * 1.3f, cb + Vector3.up * .85f, 40f); Shot("female_back", cb - along * 5.2f - right * 1.2f + Vector3.up * 1.3f, cb + Vector3.up * .85f, 40f);
                // row C (12 m back): the two men at four phases
                float sC = s0 - 12f; var cc = WOn(jong, cums[jr], sC, 0);
                for (int k = 0; k < 4; k++) { int b = k % 2 == 0 ? 0 : Mathf.Min(1, prefabs.Length - 1); Stand(WOn(jong, cums[jr], sC + (k - 1.5f) * 1.3f, 0), yaw, b, k, prefabs[b].Walk, prefabs[b].Walk.length * k / 4f); }
                Shot("male_right", cc + right * 4.4f + Vector3.up * 1.1f, cc + Vector3.up * .85f, 45f); Shot("male_left", cc - right * 4.4f + Vector3.up * 1.1f, cc + Vector3.up * .85f, 45f);
            }
            else if (set == "people")
            {
                foreach (var p in routes.Posts) Stand(p.Position, p.Yaw, p.Body, p.Tint, prefabs[p.Body].Idle, (p.Seed & 255) * .01f);
                foreach (var w in routes.Walkers) { var rt = routes.Routes[w.Route]; var at = WOn(rt, cums[w.Route], w.Start * cums[w.Route][cums[w.Route].Length - 1], (w.Direction > 0 ? -1 : 1) * Mathf.Min(w.Lane, WHalf(rt, cums[w.Route], w.Start * cums[w.Route][cums[w.Route].Length - 1])), out float yaw); Stand(at, w.Direction > 0 ? yaw : yaw + 180f, w.Body, w.Tint, prefabs[w.Body].Walk, (w.Seed & 255) / 255f * prefabs[w.Body].Walk.length); }
                for (int i = 0; i < routes.Stops.Length; i += 3) { var st = routes.Stops[i]; Stand(WOn(routes.Routes[st.Route], cums[st.Route], st.Distance, st.Lateral), st.Yaw, i % prefabs.Length, i % f.tintOrder.Count, prefabs[i % prefabs.Length].Idle, i * .13f); }
                var seen = new HashSet<string>(); int vend = 0;
                foreach (var p in routes.Posts)
                {
                    if (p.Role == "vendor") { if (vend++ % 4 != 0) continue; } else if (!seen.Add(p.Role)) continue;
                    var fwd = Quaternion.Euler(0, p.Yaw, 0) * Vector3.forward; var side = Quaternion.Euler(0, p.Yaw + 50f, 0) * Vector3.forward;
                    Shot("post_" + p.Role, p.Position + side * 5.5f + Vector3.up * 1.7f, p.Position + Vector3.up * .9f + fwd * .6f, 45f);
                }
                for (int r = 0; r < routes.Routes.Length; r++) { var rt = routes.Routes[r]; float len = cums[r][cums[r].Length - 1]; var a = WOn(rt, cums[r], len * .35f, 0, out float yaw); var along = Quaternion.Euler(0, yaw, 0) * Vector3.forward; Shot("street_" + rt.Id, a - along * 3f + Vector3.up * 1.7f, a + along * 25f + Vector3.up * 1.0f, 55f); }
            }
            else { UnityEngine.Object.DestroyImmediate(root); throw new Refuse("shots: lineup[:<s>] | walk[:<s>] | people | off"); }
            return sb.Append("preview bodies " + made + " (poses are sampled frames: motion itself is Play only). Run each line with Presentation297.").ToString();
        }
        static string WV(Vector3 v) => F(v.x, "F2") + "," + F(v.y, "F2") + "," + F(v.z, "F2");
    }
}
