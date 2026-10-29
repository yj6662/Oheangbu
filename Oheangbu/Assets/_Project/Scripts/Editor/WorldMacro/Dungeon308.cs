using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 small dungeons (SPEC-WORLD-DUNGEON-308, D308-57 / D308-58). Every value [TEST].
    // The geometry and every position are made offline by Tools/Art/dungeon308.py (Art/World/Compact/Rebuild/Dungeon308/<id>/{Meshes/*.json,
    // layout.json}; numbers in Tools/Unity/Stage308_dungeon/dungeon308.json). This tool owns ONE root (Dungeon308) in the play scene, ONE asset
    // folder (Assets/_Project/Art/World/Dungeon308) and the content rows whose Id starts with "dg308_" in the play scene's content asset.
    //   Dungeon308/<id>/Drawn_*      the walls, floors, roofs and props (renderers only)
    //   Dungeon308/<id>/Colliders    BoxColliders, each inside a drawn wall / floor block (no invisible wall)
    //   Dungeon308/<id>/Gate_*       SpellFieldGate308: the closed body (drawn + one BoxCollider inside it) and what is left when it is open.
    //                                The session saves an opened gate (WorldMacroPlaytestSession.FieldGates308)
    //   Dungeon308/<id>/Door         WorldShortcutDoor, unbarred from the inside (content point = the door id). twoSided (D308-117 C3, the front
    //                                section of a dungeon opened before 청룡): the point sits in the door plane and the door carries a
    //                                WorldMacroContentPoint of the same id, so the sight test passes its own leaves from either room
    //   Dungeon308/<id>/Reward_*     a porcelain prefab of the heritage prop pack + WorldMacroContentPoint: celadon = a stone-scripture
    //                                fragment (Evidence), white porcelain on a high stand = coins (Currency)
    //   Dungeon308/<id>/Lamp_*       cave dungeons only (D308-61): a lantern set down on the floor, its paper glowing, one point light in it
    // No other light, no emissive material, no text object, no HUD. Queue-safe: Run(string) never opens a dialog; refusals come back as "REFUSED ...".
    // The play scene and the content asset are backed up before they are written; AssetDatabase.SaveAssets is never called.
    //   refit:<id>   rebuilds only that dungeon's Door and Reward_* objects and its door / reward content rows from its layout.json
    //                (D308-117 C3: a layout patch must not cost a rebuild of every mesh and renderer of all fifteen)
    //   status | build | clear | check | cavemats (the realm rock / charred timber / water materials of cave dungeons; build runs it too)
    //   hamlet:build | hamlet:clear   the realm outbuildings of Tools/Art/hamlet308.py (SPEC-WORLD-REALM-IDENTITY-308): root Hamlet308, one drawn
    //                                 mesh set per realm, one BoxCollider per building, no content row
    public static class Dungeon308
    {
        const string RootName = "Dungeon308", SceneName = "W_Demo_Main", AssetDir = "Assets/_Project/Art/World/Dungeon308", RowPrefix = "dg308_";
        const string ContentAsset = "Assets/_Project/Scenes/World/Main/WorldContent_Main.asset", MaterialDir = "Assets/_Project/Art/World/Finish297/Materials/";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string DataDir => Path.Combine(RepoRoot, "Art", "World", "Compact", "Rebuild", "Dungeon308");
        static string StageDir => Path.Combine(RepoRoot, "Tools", "Unity", "Stage308_dungeon");

        [Serializable] sealed class Sub { public string material = ""; public int[] triangles = Array.Empty<int>(); }
        [Serializable] sealed class MeshData { public string name = ""; public float[] vertices = Array.Empty<float>(), normals = Array.Empty<float>(), uvs = Array.Empty<float>(); public Sub[] submeshes = Array.Empty<Sub>(); }
        [Serializable] sealed class Col { public float[] center = Array.Empty<float>(), size = Array.Empty<float>(); public float yaw; }
        [Serializable] sealed class GateRow { public string id = "", kind = "", closed = "", open = ""; public float[] pos = Array.Empty<float>(), size = Array.Empty<float>(); public float yaw; }
        [Serializable] sealed class DoorRow { public string id = ""; public float[] pos = Array.Empty<float>(), inside = Array.Empty<float>(); public float yaw, width, height; public bool twoSided; }
        [Serializable] sealed class Reward { public string id = "", kind = "", prefab = "", prompt = "", text = "", locked = ""; public int currency; public float[] pos = Array.Empty<float>(), stand = Array.Empty<float>(); }
        [Serializable] sealed class Layout { public string id = "", realm = "", name = "", theme = ""; public string[] drawn = Array.Empty<string>(); public Col[] colliders = Array.Empty<Col>(); public GateRow[] gates = Array.Empty<GateRow>(); public DoorRow door = new DoorRow(); public Reward[] rewards = Array.Empty<Reward>(); public float[] entrance = Array.Empty<float>(); public int[] holesFlat = Array.Empty<int>(); public Lamp[] lamps = Array.Empty<Lamp>(); public RockRow[] rocks = Array.Empty<RockRow>(); public Pool[] pools = Array.Empty<Pool>(); }
        [Serializable] sealed class RockRow { public string prefab = ""; public float[] pos = Array.Empty<float>(); public float yaw, size; }
        [Serializable] sealed class Pool { public float[] centre = Array.Empty<float>(), size = Array.Empty<float>(); public float yaw; }
        [Serializable] sealed class CaveStyle { public string realm = "", rockMaterial = ""; public float[] rock = Array.Empty<float>(), oreColor = Array.Empty<float>(); public float ambient = .24f; }
        [Serializable] sealed class CaveAssets { public string rockSource = "", timberSource = "", rockPrefabDir = ""; public float[] charColor = Array.Empty<float>(), waterColor = Array.Empty<float>(), slagColor = Array.Empty<float>(); public float waterSmoothness = .9f, oreMatte = .5f, oreVein = .85f, powderMatte = .7f; }
        [Serializable] sealed class Lamp { public float[] pos = Array.Empty<float>(); public float yaw; }
        [Serializable] sealed class LampCfg { public string paperMesh = "", frameMesh = "", paperMaterial = "", frameMaterial = ""; public float[] color = Array.Empty<float>(); public float intensity = 2.8f, range = 15f, lightY = .34f; }
        [Serializable] sealed class Index { public string version = ""; public string[] dungeons = Array.Empty<string>(); }
        [Serializable] sealed class RewardCfg { public string prefabDir = "", fragmentPrompt = "", fragmentText = "", coinsPrompt = "", coinsText = "", doorPrompt = "", doorText = ""; }
        [Serializable] sealed class Cfg { public RewardCfg rewards = new RewardCfg(); public LampCfg lamp = new LampCfg(); public CaveStyle[] caveStyles = Array.Empty<CaveStyle>(); public CaveAssets caveAssets = new CaveAssets(); }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "status") return Status();
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only (Play is running)";
                if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "REFUSED scripts are compiling or failed to compile";
                if (c == "cavemats") return CaveMaterials(JsonUtility.FromJson<Cfg>(File.ReadAllText(Path.Combine(StageDir, "dungeon308.json"))));
                if (c == "build") return Build();
                if (c.StartsWith("refit:", StringComparison.Ordinal)) return Refit(c.Substring(6).Trim());
                if (c == "clear") return Clear();
                if (c == "check") return Check();
                if (c == "hamlet:build") return Hamlet(true);
                if (c == "holes:apply") return Holes(true);
                if (c == "holes:revert") return Holes(false);
                if (c == "veg:clear") return Veg(true);
                if (c == "veg:restore") return Veg(false);
                if (c == "hamlet:clear") return Hamlet(false);
                if (c == "maseok:build") return Maseok(true);
                if (c == "maseok:clear") return Maseok(false);
                return "REFUSED unknown command '" + c + "' (status | build | clear | check | hamlet:build | hamlet:clear | veg:clear | veg:restore | holes:apply | holes:revert)";
            }
            catch (Exception e) { return "REFUSED " + e.Message + (e is NullReferenceException ? " @ " + e.StackTrace : ""); }
        }

        static Scene PlayScene(bool clean)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != SceneName) throw new Exception("open " + SceneName + " first (active scene is " + scene.name + ")");
            if (clean && scene.isDirty) throw new Exception("the play scene has unsaved changes");
            return scene;
        }
        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);
        static Index ReadIndex() => JsonUtility.FromJson<Index>(File.ReadAllText(Path.Combine(DataDir, "index.json")));
        static Layout ReadLayout(string id) => JsonUtility.FromJson<Layout>(File.ReadAllText(Path.Combine(DataDir, id, "layout.json")));

        // One field gate of a layout as Build arms it. Shared with the spell fixture (Spell120Checks308.F125) so the check meets the
        // gate Build makes: the closed body's one BoxCollider inside the drawn mass, and the gate that switches closed and open.
        internal readonly struct GateSpec
        {
            public readonly string Dungeon, Id, Closed, Open; public readonly SpellFieldGateKind Kind; public readonly Vector3 Pos, Size; public readonly float Yaw;
            public GateSpec(string dungeon, string id, string closed, string open, SpellFieldGateKind kind, Vector3 pos, Vector3 size, float yaw)
            { Dungeon = dungeon; Id = id; Closed = closed; Open = open; Kind = kind; Pos = pos; Size = size; Yaw = yaw; }
            public float Radius => Size.x * .5f + .3f;
        }
        static GateSpec Spec(string dungeon, GateRow row) =>
            new GateSpec(dungeon, row.id, row.closed, row.open, (SpellFieldGateKind)Enum.Parse(typeof(SpellFieldGateKind), row.kind), V(row.pos), V(row.size), row.yaw);
        // Every gate of every dungeon in the index (files read, never written)
        internal static List<GateSpec> GateSpecs() => ReadIndex().dungeons.SelectMany(id => ReadLayout(id).gates.Select(row => Spec(id, row))).ToList();
        internal static bool MeshFile(string dungeon, string mesh) => File.Exists(Path.Combine(DataDir, dungeon, "Meshes", mesh + ".json"));
        // body = a fresh child of closed (Build makes it with new GameObject, the fixture in its own scene)
        internal static SpellFieldGate308 Arm(in GateSpec s, GameObject g, GameObject closed, GameObject body, GameObject open)
        {
            body.transform.SetPositionAndRotation(s.Pos + Vector3.up * s.Size.y * .5f, Quaternion.Euler(0f, s.Yaw, 0f)); body.AddComponent<BoxCollider>().size = new Vector3(s.Size.x, s.Size.y, s.Size.z * .55f);
            var gate = g.AddComponent<SpellFieldGate308>();
            gate.Configure(s.Id, s.Kind, s.Radius, new[] { closed }, open != null ? new[] { open } : Array.Empty<GameObject>());
            return gate;
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        static string Backup(Scene scene, string label)
        {
            string dir = Path.Combine(StageDir, "Backup", DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_" + label); Directory.CreateDirectory(dir);
            File.Copy(Path.GetFullPath(scene.path), Path.Combine(dir, Path.GetFileName(scene.path)), true);
            File.Copy(Path.GetFullPath(ContentAsset), Path.Combine(dir, Path.GetFileName(ContentAsset)), true);
            return dir;
        }

        static string Status()
        {
            var sb = new StringBuilder("Dungeon308 status\n"); var index = File.Exists(Path.Combine(DataDir, "index.json")) ? ReadIndex() : new Index();
            sb.Append("  data ").Append(index.version).Append(": ").Append(index.dungeons.Length).Append(" dungeons\n");
            var content = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(ContentAsset);
            sb.Append("  content rows ").Append(content == null ? "CONTENT ASSET MISSING" : content.Points.Count(p => p.Id.StartsWith(RowPrefix, StringComparison.Ordinal)).ToString(Inv)).Append('\n');
            var scene = SceneManager.GetActiveScene(); var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            sb.Append("  root in ").Append(scene.name).Append(": ").Append(root == null ? "absent" : root.transform.childCount + " dungeons, gates " + root.GetComponentsInChildren<SpellFieldGate308>(true).Length
                + ", doors " + root.GetComponentsInChildren<WorldShortcutDoor>(true).Length + ", colliders " + root.GetComponentsInChildren<Collider>(true).Length + ", renderers " + root.GetComponentsInChildren<Renderer>(true).Length);
            return sb.ToString();
        }

        static Mesh MakeMesh(MeshData d, out string[] slots)
        {
            int n = d.vertices.Length / 3; var v = new Vector3[n]; var nn = new Vector3[n]; var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(d.vertices[3 * i], d.vertices[3 * i + 1], d.vertices[3 * i + 2]);
                nn[i] = new Vector3(d.normals[3 * i], d.normals[3 * i + 1], d.normals[3 * i + 2]);
                uv[i] = new Vector2(d.uvs[2 * i], d.uvs[2 * i + 1]);
            }
            var mesh = new Mesh { name = d.name, indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(v); mesh.SetNormals(nn); mesh.SetUVs(0, uv); mesh.subMeshCount = d.submeshes.Length;
            for (int s = 0; s < d.submeshes.Length; s++) mesh.SetTriangles(d.submeshes[s].triangles, s, false);
            mesh.RecalculateBounds(); mesh.RecalculateTangents(); slots = d.submeshes.Select(s => s.material).ToArray(); return mesh;
        }

        static GameObject Drawn(string name, Mesh mesh, string[] slots, Transform parent, Dictionary<string, Material> cache)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.position = Vector3.zero; g.transform.rotation = Quaternion.identity;
            g.AddComponent<MeshFilter>().sharedMesh = mesh; var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterials = slots.Select(s =>
            {
                if (!cache.TryGetValue(s, out var m)) { m = AssetDatabase.LoadAssetAtPath<Material>((s.StartsWith("Assets/", StringComparison.Ordinal) ? s : MaterialDir + s) + ".mat"); cache[s] = m; }      // a full asset path = a material outside the kit folder (cave rock)
                if (m == null) throw new Exception("material missing: " + MaterialDir + s + ".mat");
                if (m.IsKeywordEnabled("_EMISSION")) throw new Exception("emissive material refused: " + s);
                return m;
            }).ToArray();
            r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; return g;
        }

        static GameObject Slab(string name, Transform parent, Vector3 localCentre, Vector3 size, Material material)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name; Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = localCentre; g.transform.localScale = size; g.GetComponent<MeshRenderer>().sharedMaterial = material; return g;
        }

        // D308-62: the realm's own cave rock = a copy of the mine rock with that realm's colour; charred timber; still water. Sources are only read.
        static string CaveMaterials(Cfg all)
        {
            Folder(AssetDir + "/Materials"); var a = all.caveAssets; int n = 0;
            Material Copy(string name, string source)
            {
                var src = AssetDatabase.LoadAssetAtPath<Material>(source); if (src == null) throw new Exception("material source missing: " + source);
                string path = AssetDir + "/Materials/" + name + ".mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(src) { name = name }; AssetDatabase.CreateAsset(mat, path); } else mat.CopyPropertiesFromMaterial(src);
                n++; return mat;
            }
            foreach (var st in all.caveStyles)
            {
                if (st.rock.Length != 3) throw new Exception("caveStyles." + st.realm + ".rock needs three numbers"); var mat = Copy("CaveRock_" + st.realm, a.rockSource);
                mat.SetColor("_BaseColor", new Color(st.rock[0], st.rock[1], st.rock[2], 1f)); mat.SetFloat("_Ambient", st.ambient); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);
            }
            Material Plain(string name, string shader, Color colour, float smooth)
            {
                string path = AssetDir + "/Materials/" + name + ".mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(Shader.Find(shader)) { name = name }; AssetDatabase.CreateAsset(mat, path); }
                mat.SetColor("_BaseColor", colour); if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); n++; return mat;
            }
            foreach (var st in all.caveStyles)      // D308-64: the realm's ore. Only the vein nobody finished cutting glows (unlit, dull); cut ore, powder and slag do not
            {
                if (st.oreColor.Length != 3) continue; var ore = new Color(st.oreColor[0], st.oreColor[1], st.oreColor[2], 1f);
                Plain("CaveOre_" + st.realm, "Universal Render Pipeline/Lit", ore * a.oreMatte, .35f); Plain("CaveOre_Vein_" + st.realm, "Universal Render Pipeline/Unlit", ore * a.oreVein, 0f); Plain("CavePowder_" + st.realm, "Universal Render Pipeline/Lit", ore * a.powderMatte, 0f);
            }
            if (a.slagColor.Length == 3) Plain("CaveSlag", "Universal Render Pipeline/Lit", new Color(a.slagColor[0], a.slagColor[1], a.slagColor[2], 1f), .1f);
            if (a.charColor.Length == 3) { var mat = Copy("CaveTimber_Char", a.timberSource); var col = new Color(a.charColor[0], a.charColor[1], a.charColor[2], 1f); mat.SetColor("_BaseColor", col); if (mat.HasProperty("_Color")) mat.SetColor("_Color", col); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); }
            if (a.waterColor.Length == 3)
            {
                string path = AssetDir + "/Materials/CaveWater.mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "CaveWater" }; AssetDatabase.CreateAsset(mat, path); }
                mat.SetColor("_BaseColor", new Color(a.waterColor[0], a.waterColor[1], a.waterColor[2], 1f)); mat.SetFloat("_Smoothness", a.waterSmoothness); mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_EnvironmentReflections", 0f); mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); n++;      // under the ground: no sky in the water
            }
            return "Dungeon308 cavemats: " + n + " materials in " + AssetDir + "/Materials";
        }

        static string Build()
        {
            var scene = PlayScene(true); var index = ReadIndex(); if (index.dungeons.Length == 0) throw new Exception("no dungeon in " + DataDir + " (run python Tools/Art/dungeon308.py build ...)");
            var all = JsonUtility.FromJson<Cfg>(File.ReadAllText(Path.Combine(StageDir, "dungeon308.json"))); var cfg = all.rewards; var lampCfg = all.lamp; int lamps = 0, realmRocks = 0, pools = 0; CaveMaterials(all);
            var water = AssetDatabase.LoadAssetAtPath<Material>(AssetDir + "/Materials/CaveWater.mat"); var rockPrefabs = new Dictionary<string, GameObject>();
            Mesh paperMesh = AssetDatabase.LoadAssetAtPath<Mesh>(lampCfg.paperMesh), frameMesh = AssetDatabase.LoadAssetAtPath<Mesh>(lampCfg.frameMesh); Material paperMat = AssetDatabase.LoadAssetAtPath<Material>(lampCfg.paperMaterial), frameMat = AssetDatabase.LoadAssetAtPath<Material>(lampCfg.frameMaterial);
            var content = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(ContentAsset); if (content == null) throw new Exception("content asset missing: " + ContentAsset);
            var session = Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(s => s.gameObject.scene == scene);
            if (session == null) throw new Exception("no WorldMacroPlaytestSession in the play scene");
            string backup = Backup(scene, "build");
            var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName); if (old != null) Object.DestroyImmediate(old);
            Folder(AssetDir + "/Meshes");
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { AssetDir + "/Meshes" })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            var root = new GameObject(RootName); SceneManager.MoveGameObjectToScene(root, scene);
            var cache = new Dictionary<string, Material>(); var rows = new List<PrologueContentSO.Point>(); int gates = 0, doors = 0, rewards = 0, colliders = 0; long vertices = 0;
            var wood = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "wood_dark.mat"); var iron = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "iron.mat");
            foreach (string id in index.dungeons)
            {
                var lay = ReadLayout(id); var home = new GameObject(id); home.transform.SetParent(root.transform, false);
                var meshes = new Dictionary<string, (Mesh mesh, string[] slots)>();
                foreach (string file in Directory.GetFiles(Path.Combine(DataDir, id, "Meshes"), "*.json").OrderBy(x => x))
                {
                    var d = JsonUtility.FromJson<MeshData>(File.ReadAllText(file)); var mesh = MakeMesh(d, out var slots);
                    AssetDatabase.CreateAsset(mesh, AssetDir + "/Meshes/" + d.name + ".asset"); AssetDatabase.SaveAssetIfDirty(mesh); meshes[d.name] = (mesh, slots); vertices += mesh.vertexCount;
                }
                foreach (string name in lay.drawn) Drawn("Drawn_" + name.Substring(id.Length + 1), meshes[name].mesh, meshes[name].slots, home.transform, cache);
                var cols = new GameObject("Colliders"); cols.transform.SetParent(home.transform, false);
                foreach (var c in lay.colliders)
                {
                    var g = new GameObject("c"); g.transform.SetParent(cols.transform, false); g.transform.SetPositionAndRotation(V(c.center), Quaternion.Euler(0f, c.yaw, 0f));
                    g.AddComponent<BoxCollider>().size = V(c.size); colliders++;
                }
                foreach (var row in lay.gates)
                {
                    var spec = Spec(id, row);
                    var g = new GameObject("Gate_" + row.id); g.transform.SetParent(home.transform, false); g.transform.position = spec.Pos;
                    var closed = Drawn("Closed", meshes[row.closed].mesh, meshes[row.closed].slots, g.transform, cache);
                    var body = new GameObject("Body"); body.transform.SetParent(closed.transform, false);
                    GameObject open = meshes.ContainsKey(row.open) ? Drawn("Open", meshes[row.open].mesh, meshes[row.open].slots, g.transform, cache) : null;
                    var gate = Arm(spec, g, closed, body, open);
                    EditorUtility.SetDirty(gate); gates++;
                }
                if (!string.IsNullOrEmpty(lay.door.id)) { BuildDoor(lay.door, home.transform, session, cfg, wood, iron, rows); doors++; }
                foreach (var lamp in lay.lamps)      // D308-61: a cave dungeon is lit by lanterns set down on the floor (attraction lights); the paper panes glow, the light sits in their middle
                {
                    if (paperMesh == null || frameMesh == null || paperMat == null || frameMat == null || lampCfg.color.Length != 3) throw new Exception("lantern mesh or material missing (dungeon308.json lamp)");
                    var g = new GameObject("Lamp_" + lamps); g.transform.SetParent(home.transform, false); g.transform.SetPositionAndRotation(V(lamp.pos), Quaternion.Euler(0f, lamp.yaw, 0f));
                    void Part(string name, Mesh mesh, Material mat) { var part = new GameObject(name); part.transform.SetParent(g.transform, false); part.AddComponent<MeshFilter>().sharedMesh = mesh; var pr = part.AddComponent<MeshRenderer>(); pr.sharedMaterial = mat; pr.shadowCastingMode = ShadowCastingMode.Off; }
                    Part("Frame", frameMesh, frameMat); Part("Paper", paperMesh, paperMat);
                    var glow = new GameObject("Glow"); glow.transform.SetParent(g.transform, false); glow.transform.localPosition = new Vector3(0f, lampCfg.lightY, 0f); var light = glow.AddComponent<Light>();
                    light.type = LightType.Point; light.color = new Color(lampCfg.color[0], lampCfg.color[1], lampCfg.color[2], 1f); light.intensity = lampCfg.intensity; light.range = lampCfg.range; light.shadows = LightShadows.None; lamps++;
                }
                foreach (var row in lay.rocks)      // the realm's own boulders in the falls of rock (drawn only: the heap has its collider)
                {
                    if (!rockPrefabs.TryGetValue(row.prefab, out var prefab)) rockPrefabs[row.prefab] = prefab = AssetDatabase.LoadAssetAtPath<GameObject>(all.caveAssets.rockPrefabDir + row.prefab + ".prefab");
                    if (prefab == null) throw new Exception("rock prefab missing: " + all.caveAssets.rockPrefabDir + row.prefab);
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, home.transform); inst.name = "Rock_" + realmRocks; foreach (var col in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
                    inst.transform.rotation = Quaternion.Euler(0f, row.yaw, 0f); var rs = inst.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) { Object.DestroyImmediate(inst); continue; }
                    var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds); inst.transform.localScale *= row.size / Mathf.Max(b.size.x, b.size.z, .01f);
                    b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds); inst.transform.position += V(row.pos) - new Vector3(b.center.x, b.min.y + b.size.y * .25f, b.center.z); realmRocks++;
                }
                foreach (var pool in lay.pools)      // still water a few centimetres over the floor level: it shows where the uneven floor lies lower
                {
                    if (water == null) throw new Exception("CaveWater material missing (dungeon308.json caveAssets.waterColor)");
                    var g = Slab("Water_" + pools, home.transform, Vector3.zero, new Vector3(pool.size[0], .01f, pool.size[1]), water); g.transform.SetPositionAndRotation(V(pool.centre), Quaternion.Euler(0f, pool.yaw, 0f));
                    var wr = g.GetComponent<MeshRenderer>(); wr.shadowCastingMode = ShadowCastingMode.Off; pools++;
                }
                foreach (var r in lay.rewards) { BuildReward(r, home.transform, cfg, rows); rewards++; }
            }
            content.Points = content.Points.Where(p => !p.Id.StartsWith(RowPrefix, StringComparison.Ordinal)).Concat(rows).ToArray();
            EditorUtility.SetDirty(content); AssetDatabase.SaveAssetIfDirty(content);
            EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved");
            return "Dungeon308 build: " + index.dungeons.Length + " dungeons, vertices " + vertices + ", colliders " + colliders + ", gates " + gates + ", doors " + doors + ", rewards " + rewards + ", lanterns " + lamps + ", realm rocks " + realmRocks + ", pools " + pools + ", content rows " + rows.Count + ", backup " + backup + "\n" + Check();
        }

        static void BuildDoor(DoorRow d, Transform home, WorldMacroPlaytestSession session, RewardCfg cfg, Material wood, Material iron, List<PrologueContentSO.Point> rows)
        {
            var g = new GameObject("Door"); g.transform.SetParent(home, false); g.transform.SetPositionAndRotation(V(d.pos), Quaternion.Euler(0f, d.yaw, 0f));
            Transform Leaf(string name, float sign)
            {
                var hinge = new GameObject(name); hinge.transform.SetParent(g.transform, false); hinge.transform.localPosition = new Vector3(sign * d.width * .5f, 0f, 0f);
                Slab("Board", hinge.transform, new Vector3(-sign * d.width * .25f, d.height * .5f, 0f), new Vector3(d.width * .5f - .02f, d.height, .09f), wood); return hinge.transform;
            }
            var door = g.AddComponent<WorldShortcutDoor>(); door.Session = session; door.Id = d.id; door.LeftLeaf = Leaf("LeftLeaf", -1f); door.RightLeaf = Leaf("RightLeaf", 1f);
            door.Bar = Slab("Bar", g.transform, new Vector3(0f, 1.15f, .14f), new Vector3(d.width + .5f, .13f, .13f), iron != null ? iron : wood).transform;
            var block = g.AddComponent<BoxCollider>(); block.center = new Vector3(0f, d.height * .5f, 0f); block.size = new Vector3(d.width, d.height, .3f); door.Blockers = new Collider[] { block };
            if (d.twoSided) { var own = g.AddComponent<WorldMacroContentPoint>(); own.Id = d.id; }      // the sight test skips the door's own leaves and blocker
            EditorUtility.SetDirty(door);
            rows.Add(new PrologueContentSO.Point { Id = d.id, Kind = PrologueInteractionKind.Evidence, Position = V(d.inside), Prompt = cfg.doorPrompt, Text = cfg.doorText, Radius = 1.9f, LockedText = "" });
        }
        // kind fragment = a celadon with a stone-scripture fragment (Evidence), coins = white porcelain with coins (Currency), evidence = a found
        // object with its own prompt / text (D308-117 C3: 업동's charcoal sack); a prefab path starting "Assets/" is used as it is
        static void BuildReward(Reward r, Transform home, RewardCfg cfg, List<PrologueContentSO.Point> rows)
        {
            string path = r.prefab.StartsWith("Assets/", StringComparison.Ordinal) ? r.prefab + (r.prefab.EndsWith(".prefab", StringComparison.Ordinal) ? "" : ".prefab") : cfg.prefabDir + r.prefab + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (prefab == null) throw new Exception("reward prefab missing: " + path);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, home); inst.name = "Reward_" + r.id;
            foreach (var col in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
            var rs = inst.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds);
            float scale = Mathf.Clamp(.55f / Mathf.Max(b.size.y, .01f), .4f, 3f); inst.transform.localScale *= scale;      // every piece about 0.55 m tall: read at a glance
            b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds);
            inst.transform.position += V(r.pos) - new Vector3(b.center.x, b.min.y, b.center.z);
            var point = inst.AddComponent<WorldMacroContentPoint>(); point.Id = r.id; point.Visual = inst.transform;
            bool fragment = r.kind == "fragment", evidence = r.kind == "evidence";
            string prompt = !string.IsNullOrEmpty(r.prompt) ? r.prompt : fragment ? cfg.fragmentPrompt : cfg.coinsPrompt, text = !string.IsNullOrEmpty(r.text) ? r.text : fragment ? cfg.fragmentText : cfg.coinsText;
            rows.Add(new PrologueContentSO.Point { Id = r.id, Kind = fragment || evidence ? PrologueInteractionKind.Evidence : PrologueInteractionKind.Currency, Position = V(r.stand), Prompt = prompt,
                Text = text, Currency = fragment || evidence ? 0 : r.currency, Radius = fragment || evidence ? 2.1f : 1.3f, LockedText = r.locked ?? "" });   // locked = shown before the commission that wants it is taken (C2)
        }
        static string Refit(string id)
        {
            var scene = PlayScene(true); if (!ReadIndex().dungeons.Contains(id)) throw new Exception("no dungeon '" + id + "' in the index");
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName); var home = root != null ? root.transform.Find(id) : null;
            if (home == null) throw new Exception("no " + RootName + "/" + id + " in the play scene (run build once)");
            var lay = ReadLayout(id); var all = JsonUtility.FromJson<Cfg>(File.ReadAllText(Path.Combine(StageDir, "dungeon308.json"))); var cfg = all.rewards;
            var content = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(ContentAsset); if (content == null) throw new Exception("content asset missing: " + ContentAsset);
            var session = Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(s => s.gameObject.scene == scene);
            if (session == null) throw new Exception("no WorldMacroPlaytestSession in the play scene");
            string backup = Backup(scene, "refit-" + id);
            int removed = 0;
            foreach (Transform t in home.Cast<Transform>().ToArray()) if (t.name == "Door" || t.name.StartsWith("Reward_", StringComparison.Ordinal)) { Object.DestroyImmediate(t.gameObject); removed++; }
            var wood = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "wood_dark.mat"); var iron = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "iron.mat");
            var rows = new List<PrologueContentSO.Point>();
            if (!string.IsNullOrEmpty(lay.door.id)) BuildDoor(lay.door, home, session, cfg, wood, iron, rows);
            foreach (var r in lay.rewards) BuildReward(r, home, cfg, rows);
            // this dungeon's rows only (the door and every reward of the layout share the prefix dg308_<id>_)
            string mine = RowPrefix + id + "_"; int before = content.Points.Count(p => p.Id.StartsWith(mine, StringComparison.Ordinal));
            if (rows.Any(r => !r.Id.StartsWith(mine, StringComparison.Ordinal))) throw new Exception("a door / reward id of " + id + " does not start with " + mine);
            content.Points = content.Points.Where(p => !p.Id.StartsWith(mine, StringComparison.Ordinal)).Concat(rows).ToArray();
            EditorUtility.SetDirty(content); AssetDatabase.SaveAssetIfDirty(content);
            EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved");
            return "Dungeon308 refit " + id + ": objects removed " + removed + ", rebuilt door " + (string.IsNullOrEmpty(lay.door.id) ? "-" : lay.door.id + (lay.door.twoSided ? " (two-sided)" : "")) + " + " + lay.rewards.Length
                + " rewards (" + string.Join(", ", lay.rewards.Select(r => r.id + " " + r.kind + (r.currency > 0 ? " " + r.currency : ""))) + "), content rows " + before + " -> " + rows.Count + ", backup " + backup + "\n" + Check();
        }

        static string Clear()
        {
            var scene = PlayScene(true); var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            var content = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(ContentAsset); string backup = Backup(scene, "clear");
            int rows = content.Points.Count(p => p.Id.StartsWith(RowPrefix, StringComparison.Ordinal));
            content.Points = content.Points.Where(p => !p.Id.StartsWith(RowPrefix, StringComparison.Ordinal)).ToArray(); EditorUtility.SetDirty(content); AssetDatabase.SaveAssetIfDirty(content);
            if (old != null) { Object.DestroyImmediate(old); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            return "Dungeon308 clear: root " + (old != null ? "removed" : "absent") + ", content rows removed " + rows + ", backup " + backup;
        }

        static string Check()
        {
            var scene = PlayScene(false); var sb = new StringBuilder("Dungeon308 check\n"); int pass = 0, fail = 0;
            void C(bool ok, string text) { sb.Append(ok ? "  [PASS] " : "  [FAIL] ").Append(text).Append('\n'); if (ok) pass++; else fail++; }
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName); var index = ReadIndex();
            C(root != null && root.transform.childCount == index.dungeons.Length, "one root with a child per dungeon (" + (root != null ? root.transform.childCount : 0) + " of " + index.dungeons.Length + ")");
            if (root == null) return sb.Append(pass).Append(" checks, ").Append(fail).Append(" failed").ToString();
            var content = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(ContentAsset); var ids = new HashSet<string>(content.Points.Select(p => p.Id));
            var lights = root.GetComponentsInChildren<Light>(true); C(lights.All(l => l.transform.parent != null && l.transform.parent.name.StartsWith("Lamp_", StringComparison.Ordinal) && l.type == LightType.Point), "AC-D4 the only lights are the lanterns of cave dungeons (" + lights.Length + ")");
            C(root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).All(m => m != null && !m.IsKeywordEnabled("_EMISSION")), "AC-D4 no missing and no emissive material");
            C(root.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m == null || !m.name.StartsWith("CaveOre_Vein_", StringComparison.Ordinal)) || r.name.Contains("Ore_Vein")), "D308-64 the glowing vein material is only on objects named Ore_Vein (the emission allow-list)");
            var styles = JsonUtility.FromJson<Cfg>(File.ReadAllText(Path.Combine(StageDir, "dungeon308.json"))).caveStyles; var caveRocks = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null && m.name.StartsWith("CaveRock_", StringComparison.Ordinal)).Select(m => m.name).Distinct().Count();
            C(caveRocks == styles.Length, "D308-62 every cave dungeon wears its realm's own rock (" + caveRocks + " rock materials for " + styles.Length + " realm styles)");
            var gates = root.GetComponentsInChildren<SpellFieldGate308>(true);
            C(gates.All(g => !string.IsNullOrEmpty(g.Id) && g.GetComponentsInChildren<Collider>(true).Length == 1) && gates.Select(g => g.Id).Distinct().Count() == gates.Length, "every gate has a unique id and one collider under its closed body (" + gates.Length + " gates)");
            var doors = root.GetComponentsInChildren<WorldShortcutDoor>(true);
            C(doors.All(d => d.Session != null && ids.Contains(d.Id) && d.LeftLeaf != null && d.RightLeaf != null && d.Blockers.Length == 1), "every door is wired to the session and has its content row (" + doors.Length + " doors)");
            var points = root.GetComponentsInChildren<WorldMacroContentPoint>(true);
            var rewardPoints = points.Where(p => p.name.StartsWith("Reward_", StringComparison.Ordinal)).ToArray(); int planned = index.dungeons.Sum(d => ReadLayout(d).rewards.Length);
            C(points.All(p => ids.Contains(p.Id)) && rewardPoints.Length == planned, "every reward of the layouts stands, each with its content row (" + rewardPoints.Length + " of " + planned + ")");
            var twoSided = index.dungeons.Select(ReadLayout).Where(l => l.door.twoSided).Select(l => l.door.id).ToArray();
            C(twoSided.All(t => doors.Any(d => d.Id == t && d.GetComponent<WorldMacroContentPoint>() != null && d.GetComponent<WorldMacroContentPoint>().Id == t)), "D308-117 C3 every two-sided door carries its own content point (" + string.Join(", ", twoSided) + ")");
            int fragments = content.Points.Count(p => p.Id.StartsWith(RowPrefix, StringComparison.Ordinal) && p.Id.EndsWith("_fragment", StringComparison.Ordinal));
            C(fragments == index.dungeons.Length, "one stone-scripture fragment per dungeon (" + fragments + ")");
            // a collider box must sit inside the ground or a drawn block: its centre is never above every renderer of its dungeon (cheap sanity)
            int loose = 0;
            foreach (Transform home in root.transform)
            {
                var rs = home.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) { loose++; continue; }
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); b.Expand(.2f);
                foreach (var col in home.GetComponentsInChildren<BoxCollider>(true)) if (!b.Contains(col.bounds.center)) loose++;
            }
            C(loose == 0, "every collider centre lies inside the drawn bounds of its dungeon (outside: " + loose + ")");
            return sb.Append(pass).Append(" checks, ").Append(fail).Append(" failed").ToString();
        }
        // ---- D308-64: ore-craft fittings on ordinary buildings (Tools/Art/maseok308.py). Root Maseok308 in the play scene (not streamed): one drawn
        // mesh per realm, culled beyond cullM, a box inside each jar / brazier, and the lit lamps (a point light on the ones the plan marks).
        [Serializable] sealed class MsLamp { public float[] pos = Array.Empty<float>(); public bool light; }
        [Serializable] sealed class MsLight { public float[] color = Array.Empty<float>(); public float intensity = 2f, range = 9f; }
        [Serializable] sealed class MsLayout { public string version = ""; public string[] drawn = Array.Empty<string>(); public Col[] colliders = Array.Empty<Col>(); public MsLamp[] lamps = Array.Empty<MsLamp>(); public float cullM = 160f; public MsLight light = new MsLight(); }
        static string Maseok(bool build)
        {
            const string rootName = "Maseok308", assets = "Assets/_Project/Art/World/Maseok308"; var scene = PlayScene(true);
            string data = Path.Combine(RepoRoot, "Art", "World", "Compact", "Rebuild", "Maseok308"); var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == rootName);
            if (!build && old == null) return "Dungeon308 maseok:clear: root absent";
            if (build && !File.Exists(Path.Combine(data, "layout.json"))) throw new Exception("no " + data + "/layout.json (run python Tools/Art/maseok308.py ...)");
            string dir = Path.Combine(RepoRoot, "Tools", "Unity", "Stage308_maseok", "Backup", DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_maseok"); Directory.CreateDirectory(dir);
            File.Copy(Path.GetFullPath(scene.path), Path.Combine(dir, Path.GetFileName(scene.path)), true);
            if (old != null) Object.DestroyImmediate(old);
            if (!build) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); return "Dungeon308 maseok:clear: removed, backup " + dir; }
            var lay = JsonUtility.FromJson<MsLayout>(File.ReadAllText(Path.Combine(data, "layout.json"))); Folder(assets + "/Meshes"); Folder(assets + "/Materials");
            var mats = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot, "Tools", "Unity", "Stage308_maseok", "maseok308.json")))["materials"] as Newtonsoft.Json.Linq.JObject;
            foreach (var kv in mats)      // onggi, brass, lacquer, unlit paper: plain lit materials, no emission
            {
                string path = assets + "/Materials/" + kv.Key + ".mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = kv.Key }; AssetDatabase.CreateAsset(mat, path); }
                var col = (Newtonsoft.Json.Linq.JArray)kv.Value["color"]; mat.SetColor("_BaseColor", new Color((float)col[0], (float)col[1], (float)col[2], 1f)); mat.SetFloat("_Smoothness", (float)kv.Value["smooth"]); mat.SetFloat("_Metallic", (float)kv.Value["metallic"]);
                mat.SetFloat("_EnvironmentReflections", 0f); mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { assets + "/Meshes" })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            var root = new GameObject(rootName); SceneManager.MoveGameObjectToScene(root, scene); var cache = new Dictionary<string, Material>(); long vertices = 0; int lights = 0;
            foreach (string name in lay.drawn)
            {
                var d = JsonUtility.FromJson<MeshData>(File.ReadAllText(Path.Combine(data, "Meshes", name + ".json"))); var mesh = MakeMesh(d, out var slots);
                AssetDatabase.CreateAsset(mesh, assets + "/Meshes/" + d.name + ".asset"); AssetDatabase.SaveAssetIfDirty(mesh); vertices += mesh.vertexCount; var g = Drawn(name, mesh, slots, root.transform, cache);
                g.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            var cols = new GameObject("Colliders"); cols.transform.SetParent(root.transform, false);
            foreach (var c in lay.colliders) { var g = new GameObject("c"); g.transform.SetParent(cols.transform, false); g.transform.SetPositionAndRotation(V(c.center), Quaternion.Euler(0f, c.yaw, 0f)); g.AddComponent<BoxCollider>().size = V(c.size); }
            var lamps = new GameObject("Lamps").transform; lamps.SetParent(root.transform, false);
            foreach (var l in lay.lamps)
            {
                if (!l.light || lay.light.color.Length != 3) continue; var g = new GameObject("Lamp_" + lights++); g.transform.SetParent(lamps, false); g.transform.position = V(l.pos); var light = g.AddComponent<Light>();
                light.type = LightType.Point; light.color = new Color(lay.light.color[0], lay.light.color[1], lay.light.color[2], 1f); light.intensity = lay.light.intensity; light.range = lay.light.range; light.shadows = LightShadows.None;
            }
            EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved");
            bool clean = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).All(m => m != null && !m.IsKeywordEnabled("_EMISSION") && !m.name.ToLowerInvariant().Contains("copper") && !m.name.ToLowerInvariant().Contains("brass_gear"));
            return "Dungeon308 maseok:build: " + lay.drawn.Length + " drawn meshes, vertices " + vertices + ", colliders " + lay.colliders.Length + ", lit lamps " + lay.lamps.Length + " (lights " + lights + "), no emission keyword and no temple (copper / gear) material: " + (clean ? "PASS" : "FAIL") + ", backup " + dir;
        }
        [Serializable] sealed class HamletHouse { public string realm = "", style = "", prefab = "", variant = ""; public float x, y, z, yaw; }
        [Serializable] sealed class HamletVariant { public string name = ""; public float[] tint = Array.Empty<float>(); }
        [Serializable] sealed class HamletLayout { public string version = "", prefabDir = ""; public string[] drawn = Array.Empty<string>(); public Col[] colliders = Array.Empty<Col>(); public HamletHouse[] houses = Array.Empty<HamletHouse>(); public HamletVariant[] variants = Array.Empty<HamletVariant>(); }
        static string Hamlet(bool build)
        {
            const string rootName = "Hamlet308", assets = "Assets/_Project/Art/World/Hamlet308"; var scene = PlayScene(true);
            string data = Path.Combine(RepoRoot, "Art", "World", "Compact", "Rebuild", "Hamlet308"); var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == rootName);
            if (!build && old == null) return "Dungeon308 hamlet:clear: root absent";
            if (build && !File.Exists(Path.Combine(data, "layout.json"))) throw new Exception("no " + data + "/layout.json (run python Tools/Art/hamlet308.py ...)");
            string dir = Path.Combine(StageDir, "Backup", DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_hamlet"); Directory.CreateDirectory(dir);
            File.Copy(Path.GetFullPath(scene.path), Path.Combine(dir, Path.GetFileName(scene.path)), true);
            if (old != null) Object.DestroyImmediate(old);
            if (!build) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); return "Dungeon308 hamlet:clear: removed, backup " + dir; }
            var lay = JsonUtility.FromJson<HamletLayout>(File.ReadAllText(Path.Combine(data, "layout.json")));
            Folder(assets + "/Meshes"); foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { assets + "/Meshes" })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            var root = new GameObject(rootName); SceneManager.MoveGameObjectToScene(root, scene); var cache = new Dictionary<string, Material>(); long vertices = 0;
            foreach (string name in lay.drawn)
            {
                var d = JsonUtility.FromJson<MeshData>(File.ReadAllText(Path.Combine(data, "Meshes", name + ".json"))); var mesh = MakeMesh(d, out var slots);
                AssetDatabase.CreateAsset(mesh, assets + "/Meshes/" + d.name + ".asset"); AssetDatabase.SaveAssetIfDirty(mesh); vertices += mesh.vertexCount; Drawn(name, mesh, slots, root.transform, cache);
            }
            // D308-65: whole pack buildings (HouseKit308 prefabs). Each realm variant = copies of the prefab's materials with the base colour multiplied
            int packed = 0; var variantMats = new Dictionary<string, Material>(); Folder("Assets/_Project/Art/World/HouseKit308/Variants");
            foreach (var h in lay.houses)
            {
                if (string.IsNullOrEmpty(h.prefab)) continue; var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(lay.prefabDir + h.prefab + ".prefab"); if (prefab == null) throw new Exception("house prefab missing: " + lay.prefabDir + h.prefab + " (run HouseKit308 bake)");
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform); inst.name = h.realm + "_" + h.prefab + "_" + packed; foreach (var col in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
                var mb = inst.GetComponent<MeshFilter>().sharedMesh.bounds; inst.transform.SetPositionAndRotation(new Vector3(h.x, h.y, h.z), Quaternion.Euler(0f, h.yaw, 0f)); inst.transform.position -= inst.transform.rotation * new Vector3(mb.center.x, 0f, mb.center.z);
                var variant = lay.variants.FirstOrDefault(v => v.name == h.variant); var r = inst.GetComponent<MeshRenderer>();
                if (variant != null && variant.tint.Length == 3 && (variant.tint[0] != 1f || variant.tint[1] != 1f || variant.tint[2] != 1f))
                    r.sharedMaterials = r.sharedMaterials.Select(m =>
                    {
                        string key = variant.name + "/" + m.name; if (variantMats.TryGetValue(key, out var made)) return made; Folder("Assets/_Project/Art/World/HouseKit308/Variants/" + variant.name);
                        string path = "Assets/_Project/Art/World/HouseKit308/Variants/" + variant.name + "/" + m.name + ".mat"; made = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (made == null) { made = new Material(m) { name = m.name }; AssetDatabase.CreateAsset(made, path); } else made.CopyPropertiesFromMaterial(m);
                        var c0 = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white; made.SetColor("_BaseColor", new Color(c0.r * variant.tint[0], c0.g * variant.tint[1], c0.b * variant.tint[2], c0.a)); EditorUtility.SetDirty(made); AssetDatabase.SaveAssetIfDirty(made); variantMats[key] = made; return made;
                    }).ToArray();
                vertices += inst.GetComponent<MeshFilter>().sharedMesh.vertexCount; packed++;
            }
            var cols = new GameObject("Colliders"); cols.transform.SetParent(root.transform, false);
            foreach (var c in lay.colliders)
            {
                var g = new GameObject("house"); g.transform.SetParent(cols.transform, false); g.transform.SetPositionAndRotation(V(c.center), Quaternion.Euler(0f, c.yaw, 0f)); g.AddComponent<BoxCollider>().size = V(c.size);
            }
            EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved");
            return "Dungeon308 hamlet:build: " + packed + " pack buildings, " + lay.drawn.Length + " drawn meshes, " + lay.colliders.Length + " buildings, vertices " + vertices + ", backup " + dir;
        }
        // ---- veg: plants of the placement sheets that stand inside a dungeon or a building footprint are taken out (file backups, veg:restore)
        [Serializable] sealed class Foot { public float x, z, yaw, w, d; }
        [Serializable] sealed class FootFile { public string[] sheets = Array.Empty<string>(); public Foot[] feet = Array.Empty<Foot>(); }
        static string VegBackup => Path.Combine(StageDir, "Ledger", "veg_before");
        static string Veg(bool clear)
        {
            string file = Path.Combine(StageDir, "footprints308.json"); if (!File.Exists(file)) throw new Exception("no " + file + " (run python Tools/Art/footprints308.py)");
            var data = JsonUtility.FromJson<FootFile>(File.ReadAllText(file)); string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!clear)
            {
                if (!Directory.Exists(VegBackup)) return "Dungeon308 veg:restore: nothing recorded";
                int n = 0; foreach (string sheet in data.sheets) { string b = Path.Combine(VegBackup, Path.GetFileName(sheet)); if (!File.Exists(b)) continue; File.Copy(b, Path.Combine(project, sheet), true); AssetDatabase.ImportAsset(sheet, ImportAssetOptions.ForceUpdate); n++; }
                Directory.Move(VegBackup, VegBackup + "_restored_" + DateTime.Now.ToString("yyyyMMddTHHmmss", Inv)); return "Dungeon308 veg:restore: " + n + " sheets restored";
            }
            if (Directory.Exists(VegBackup)) throw new Exception("already cleared (veg:restore first): " + VegBackup);
            Directory.CreateDirectory(VegBackup); var sb = new StringBuilder(); var feet = data.feet.Select(f => (f, cos: Mathf.Cos(f.yaw * Mathf.Deg2Rad), sin: Mathf.Sin(f.yaw * Mathf.Deg2Rad), r: Mathf.Sqrt(f.w * f.w + f.d * f.d) * .5f)).ToArray();
            foreach (string sheet in data.sheets)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(sheet); if (asset == null) { sb.Append("\n  MISSING ").Append(sheet); continue; }
                File.Copy(Path.Combine(project, sheet), Path.Combine(VegBackup, Path.GetFileName(sheet)), true);
                var so = new SerializedObject(asset); var list = so.FindProperty("FixedPlacements"); if (list == null || !list.isArray) { sb.Append("\n  NO LIST ").Append(sheet); continue; }
                int removed = 0;
                for (int i = list.arraySize - 1; i >= 0; i--)
                {
                    var p = list.GetArrayElementAtIndex(i).FindPropertyRelative("Position").vector3Value; bool inside = false;
                    foreach (var (f, cos, sin, r) in feet)
                    {
                        float dx = p.x - f.x, dz = p.z - f.z; if (Mathf.Abs(dx) > r || Mathf.Abs(dz) > r) continue;
                        float lx = dx * cos - dz * sin, lz = dx * sin + dz * cos;      // world -> local (local x = (cos, -sin), local z = (sin, cos))
                        if (Mathf.Abs(lx) <= f.w * .5f && Mathf.Abs(lz) <= f.d * .5f) { inside = true; break; }
                    }
                    if (inside) { list.DeleteArrayElementAtIndex(i); removed++; }
                }
                so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
                sb.Append("\n  ").Append(Path.GetFileName(sheet)).Append(": removed ").Append(removed).Append(" of ").Append(list.arraySize + removed);
            }
            return "Dungeon308 veg:clear: " + data.feet.Length + " footprints, backup " + VegBackup + sb;
        }
        // ---- holes (D308-59): the open stair pit of an underground dungeon needs the ground gone over it. The terrain is 500 m mesh tiles
        // (Reworld292_Terrain/Terrain_XX_YY, LOD0 = renderer + MeshCollider). For every tile that holds a hole cell, a COPY of the LOD0 mesh
        // the tile shows now, without the triangles of those 4 m lattice cells, is saved under this tool's folder and the tile's LOD0
        // MeshFilter and MeshCollider are pointed at it (the same kind of re-pointing as D308-9c Q1; no protected FILE is written).
        // The ledger keeps what each tile showed before. A later terrain stage (CliffBoundary308 tiles:) re-points the tiles: run holes:revert
        // before it and holes:apply after it.
        [Serializable] sealed class HoleRow { public string tile = "", filterMesh = "", colliderMesh = "", holeMesh = ""; public int cells; }
        [Serializable] sealed class HoleLedger { public List<HoleRow> rows = new List<HoleRow>(); }
        static string HoleLedgerFile => Path.Combine(StageDir, "Ledger", "holes308.json");
        static string Holes(bool apply)
        {
            var scene = PlayScene(true); var terrain = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Reworld292_Terrain"); if (terrain == null) throw new Exception("no Reworld292_Terrain root");
            var ledger = File.Exists(HoleLedgerFile) ? JsonUtility.FromJson<HoleLedger>(File.ReadAllText(HoleLedgerFile)) : new HoleLedger();
            string dir = Path.Combine(StageDir, "Backup", DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_holes"); Directory.CreateDirectory(dir);
            File.Copy(Path.GetFullPath(scene.path), Path.Combine(dir, Path.GetFileName(scene.path)), true); var sb = new StringBuilder();
            MeshFilter Lod0(Transform tile) => tile.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).OrderByDescending(f => f.sharedMesh.vertexCount).FirstOrDefault();
            foreach (var row in ledger.rows)
            {
                var tile = terrain.transform.Find(row.tile); if (tile == null) { sb.Append("\n  MISSING tile ").Append(row.tile); continue; }
                var filter = Lod0(tile); var col = tile.GetComponent<MeshCollider>(); var holeMesh = AssetDatabase.LoadAssetAtPath<Mesh>(row.holeMesh);
                if (filter != null && filter.sharedMesh == holeMesh) filter.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(row.filterMesh);      // only if the tile still shows the hole mesh (a terrain stage may have re-pointed it)
                if (col != null && col.sharedMesh == holeMesh) col.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(row.colliderMesh);
                sb.Append("\n  restored ").Append(row.tile);
            }
            ledger.rows.Clear();
            if (apply)
            {
                var byTile = new Dictionary<string, HashSet<long>>();
                foreach (string id in ReadIndex().dungeons)
                {
                    var lay = ReadLayout(id);
                    for (int k = 0; k + 1 < lay.holesFlat.Length; k += 2)
                    {
                        int i = lay.holesFlat[k], j = lay.holesFlat[k + 1]; string tile = string.Format(Inv, "Terrain_{0:D2}_{1:D2}", Mathf.Min(7, j * 4 / 500), Mathf.Min(11, i * 4 / 500));
                        if (!byTile.TryGetValue(tile, out var set)) byTile[tile] = set = new HashSet<long>(); set.Add((long)i * 4096 + j);
                    }
                }
                Folder(AssetDir + "/Terrain");
                foreach (var kv in byTile.OrderBy(k => k.Key))
                {
                    var tile = terrain.transform.Find(kv.Key); if (tile == null) throw new Exception("tile not found: " + kv.Key);
                    var filter = Lod0(tile); var col = tile.GetComponent<MeshCollider>(); if (filter == null || col == null) throw new Exception("tile without LOD0 filter / collider: " + kv.Key);
                    var source = filter.sharedMesh; var v = source.vertices; var tri = source.triangles; var keep = new List<int>(tri.Length); int removed = 0; var m = tile.localToWorldMatrix;
                    for (int t = 0; t < tri.Length; t += 3)
                    {
                        var cen = m.MultiplyPoint3x4((v[tri[t]] + v[tri[t + 1]] + v[tri[t + 2]]) / 3f); long key = (long)Mathf.FloorToInt(cen.z / 4f) * 4096 + Mathf.FloorToInt(cen.x / 4f);
                        if (kv.Value.Contains(key)) { removed++; continue; }
                        keep.Add(tri[t]); keep.Add(tri[t + 1]); keep.Add(tri[t + 2]);
                    }
                    if (removed != kv.Value.Count * 2) sb.Append("\n  WARNING ").Append(kv.Key).Append(": removed ").Append(removed).Append(" triangles for ").Append(kv.Value.Count).Append(" cells (expected 2 per cell)");
                    var mesh = Object.Instantiate(source); mesh.name = kv.Key + "_dg308holes"; mesh.triangles = keep.ToArray(); mesh.RecalculateBounds();
                    string path = AssetDir + "/Terrain/" + mesh.name + ".asset"; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path); AssetDatabase.SaveAssetIfDirty(mesh);
                    ledger.rows.Add(new HoleRow { tile = kv.Key, filterMesh = AssetDatabase.GetAssetPath(source), colliderMesh = AssetDatabase.GetAssetPath(col.sharedMesh), holeMesh = path, cells = kv.Value.Count });
                    filter.sharedMesh = mesh; col.sharedMesh = mesh; sb.Append("\n  ").Append(kv.Key).Append(": ").Append(kv.Value.Count).Append(" cells, ").Append(removed).Append(" triangles out, was ").Append(Path.GetFileName(AssetDatabase.GetAssetPath(source)));
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(HoleLedgerFile)); File.WriteAllText(HoleLedgerFile, JsonUtility.ToJson(ledger, true));
            EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved");
            return "Dungeon308 holes:" + (apply ? "apply" : "revert") + ": tiles " + ledger.rows.Count + ", backup " + dir + sb;
        }
    }
}
