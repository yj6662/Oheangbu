using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 steam kit (D308-43 "불교 지역은 스팀펑크의 문법으로", SPEC-ARCH-TEMPLE-308 §6). Every value [TEST].
    // Parts and their four textures are made offline by Tools/Art/steamkit308.py (Art/World/Compact/Rebuild/SteamKit308). This tool
    // owns Assets/_Project/Art/World/SteamKit308 (meshes, textures, four materials, part prefabs) and the dressed temple prefabs
    // Assets/_Project/Art/World/Temple308/Prefabs/Temple308S_* (a Temple308_* building + a "Steam" child [+ "Col"]). The pack
    // prefabs and the plain Temple308_* prefabs are not changed. No emission (ART-LIGHT cap: pipes and gauges do not glow), no light,
    // no script. Placement reads the building itself: ground-storey walls, the roof's own vertices (ridge line, slope height),
    // and for stone works the narrow waists of the profile.
    //   build    textures, materials, mesh assets, part prefabs
    //   dress    Temple308S_* from every Temple308_* (+ Temple308S_Yunjangdae: water wheel driving a sutra wheel)
    //   finish   LODGroup (near: everything / far: large pieces only) + colliders on every Temple308S_*
    //   meshlod[:apply]  the pack models' importer "mesh LOD" switch (probe, or set and reimport)
    //   sheet    pictures of the parts and of the dressed buildings (front and back)
    public static class SteamKit308
    {
        const string Dir = "Assets/_Project/Art/World/SteamKit308", TempleDir = "Assets/_Project/Art/World/Temple308/Prefabs", ColDir = "Assets/_Project/Art/World/Temple308/Collision";
        static string DataRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Art", "World", "Compact", "Rebuild", "SteamKit308"));
        [Serializable] sealed class Sub { public string material = ""; public int[] triangles = Array.Empty<int>(); }
        [Serializable] sealed class MeshData { public string name = ""; public float[] vertices = Array.Empty<float>(), normals = Array.Empty<float>(), uvs = Array.Empty<float>(); public Sub[] submeshes = Array.Empty<Sub>(); }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only";
                if (c == "build") return Build();
                if (c == "dress") return Dress();
                if (c == "finish") return Finish("Temple308S_", "Steam");
                if (c.StartsWith("lite", StringComparison.Ordinal)) return Lite(c.Contains(":") ? float.Parse(c.Substring(5), System.Globalization.CultureInfo.InvariantCulture) : .3f);
                if (c == "meshlod") return MeshLod(false);
                if (c == "meshlod:apply") return MeshLod(true);
                if (c == "sheet") return TempleKit308.Sheet("Steam_") + "\n" + TempleKit308.Sheet("Temple308S_");
                return "REFUSED build | dress | finish | meshlod[:apply] | sheet";
            }
            catch (Exception e) { return "REFUSED " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace; }
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Texture2D Tex(string name, bool linear)
        {
            string source = Path.Combine(DataRoot, "Textures", name + ".png"); if (!File.Exists(source)) return null;
            Folder(Dir + "/Textures"); string path = Dir + "/Textures/" + name + ".png";
            File.Copy(source, Path.GetFullPath(path), true); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.sRGBTexture == linear || importer.maxTextureSize != 512) { importer.sRGBTexture = !linear; importer.maxTextureSize = 512; importer.wrapMode = TextureWrapMode.Repeat; importer.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Slot(string slot)
        {
            string path = Dir + "/Materials/" + slot + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { Folder(Dir + "/Materials"); m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = slot }; AssetDatabase.CreateAsset(m, path); }
            var bc = Tex(slot + "_BC", false); var ms = Tex(slot + "_MS", true);
            m.SetColor("_BaseColor", Color.white); m.SetTexture("_BaseMap", bc);
            if (ms != null) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", 1f); m.SetFloat("_GlossMapScale", 1f); m.SetFloat("_SmoothnessTextureChannel", 0f); }
            m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); return m;
        }

        static string Build()
        {
            string meshDir = Path.Combine(DataRoot, "Meshes");
            if (!Directory.Exists(meshDir)) return "REFUSED no data under " + meshDir + " (run Tools/Art/steamkit308.py)";
            Folder(Dir + "/Meshes"); Folder(Dir + "/Prefabs"); int made = 0; var slots = new Dictionary<string, Material>();
            foreach (string file in Directory.GetFiles(meshDir, "*.json").OrderBy(x => x))
            {
                var d = JsonUtility.FromJson<MeshData>(File.ReadAllText(file)); int n = d.vertices.Length / 3;
                var v = new Vector3[n]; var nn = new Vector3[n]; var uv = new Vector2[n];
                for (int i = 0; i < n; i++) { v[i] = new Vector3(d.vertices[3 * i], d.vertices[3 * i + 1], d.vertices[3 * i + 2]); nn[i] = new Vector3(d.normals[3 * i], d.normals[3 * i + 1], d.normals[3 * i + 2]); uv[i] = new Vector2(d.uvs[2 * i], d.uvs[2 * i + 1]); }
                string meshPath = Dir + "/Meshes/" + d.name + ".asset"; var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath); bool fresh = mesh == null;
                if (fresh) mesh = new Mesh { name = d.name }; else mesh.Clear();
                mesh.SetVertices(v); mesh.SetNormals(nn); mesh.SetUVs(0, uv); mesh.subMeshCount = d.submeshes.Length;
                for (int s = 0; s < d.submeshes.Length; s++) mesh.SetTriangles(d.submeshes[s].triangles, s, false);
                mesh.RecalculateBounds(); mesh.RecalculateTangents();
                if (fresh) AssetDatabase.CreateAsset(mesh, meshPath); else { EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); }
                var go = new GameObject(d.name); go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = d.submeshes.Select(s => { if (!slots.TryGetValue(s.material, out var mat)) slots[s.material] = mat = Slot(s.material); return mat; }).ToArray();
                r.shadowCastingMode = ShadowCastingMode.On;
                PrefabUtility.SaveAsPrefabAsset(go, Dir + "/Prefabs/" + d.name + ".prefab"); Object.DestroyImmediate(go); made++;
            }
            return "SteamKit308 build: parts " + made + ", materials " + slots.Count;
        }

        static GameObject Part(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Prefabs/" + name + ".prefab");

        static Bounds? BoundsOf(IEnumerable<Renderer> rs)
        {
            Bounds? b = null;
            foreach (var r in rs) { if (b == null) b = r.bounds; else { var x = b.Value; x.Encapsulate(r.bounds); b = x; } }
            return b;
        }

        static List<Vector3> WorldVertices(IEnumerable<Renderer> rs)
        {
            var list = new List<Vector3>();
            foreach (var r in rs)
            {
                var f = r.GetComponent<MeshFilter>(); if (f == null || f.sharedMesh == null) continue;
                var m = r.transform.localToWorldMatrix; foreach (var v in f.sharedMesh.vertices) list.Add(m.MultiplyPoint3x4(v));
            }
            return list;
        }

        static float SurfaceY(List<Vector3> verts, float x, float z, float radius, float fallback)
        {
            float best = float.NegativeInfinity, r2 = radius * radius;
            foreach (var v in verts) { float dx = v.x - x, dz = v.z - z; if (dx * dx + dz * dz <= r2 && v.y > best) best = v.y; }
            return float.IsNegativeInfinity(best) ? fallback : best;
        }

        static bool Has(string name, string part) => name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        static string Dress()
        {
            if (Part("Steam_RoofStack") == null) return "REFUSED run build first";
            var sb = new StringBuilder("SteamKit308 dress\n"); int made = 0;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("Temple308_ t:Prefab", new[] { TempleDir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid); string name = Path.GetFileNameWithoutExtension(path);
                    if (!name.StartsWith("Temple308_", StringComparison.Ordinal)) continue;
                    var holder = new GameObject("Temple308S_" + name.Substring(10)); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, scene);
                    var body = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), holder.transform);
                    var all = body.GetComponentsInChildren<Renderer>(true); var B = BoundsOf(all).Value;
                    // front = +Z for every dressed prefab: the entrance steps tell which side the hall faces (the pack's Demo scene turns them every way)
                    var steps = BoundsOf(all.Where(r => Has(r.name, "Step")));
                    if (steps != null)
                    {
                        Vector3 d = steps.Value.center - B.center; d.y = 0f;
                        if (d.magnitude > 1f)
                        {
                            float yaw = Mathf.Abs(d.x) > Mathf.Abs(d.z) ? (d.x > 0 ? -90f : 90f) : (d.z > 0 ? 0f : 180f);
                            body.transform.RotateAround(new Vector3(B.center.x, 0f, B.center.z), Vector3.up, yaw); B = BoundsOf(all).Value;
                            body.transform.position -= new Vector3(B.center.x, 0f, B.center.z); B = BoundsOf(all).Value;
                        }
                    }
                    var steam = new GameObject("Steam"); steam.transform.SetParent(holder.transform, false);
                    GameObject Put(string part, Vector3 position, float yaw, Vector3 scale)
                    {
                        var g = (GameObject)PrefabUtility.InstantiatePrefab(Part(part), steam.transform);
                        g.transform.localPosition = position; g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f); g.transform.localScale = scale; return g;
                    }
                    if (name.Contains("_SM_")) DressStone(name, all, B, Put); else DressHall(name, all, B, Put);
                    PrefabUtility.SaveAsPrefabAsset(holder, TempleDir + "/" + holder.name + ".prefab"); made++;
                    sb.Append("  ").Append(holder.name).Append(": ").Append(steam.transform.childCount).Append(" parts\n"); Object.DestroyImmediate(holder);
                }
                // the geared sutra wheel driven by a water wheel (stand-alone piece for a court or a stream bank)
                {
                    var holder = new GameObject("Temple308S_Yunjangdae"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, scene);
                    var steam = new GameObject("Steam"); steam.transform.SetParent(holder.transform, false);
                    foreach (var (part, p, yaw) in new[] { ("Steam_PrayerWheel", new Vector3(1.6f, 0f, 0f), 0f), ("Steam_WaterWheel", new Vector3(-2.3f, -1.9f, 0f), 0f), ("Steam_Tank", new Vector3(1.6f, 0f, -2.6f), 0f), ("Steam_Pipe4", new Vector3(1.6f, 2.0f, -2.0f), -90f) })
                    { var g = (GameObject)PrefabUtility.InstantiatePrefab(Part(part), steam.transform); g.transform.localPosition = p; g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f); if (part == "Steam_Pipe4") g.transform.localScale = new Vector3(.3f, .7f, .7f); }
                    PrefabUtility.SaveAsPrefabAsset(holder, TempleDir + "/" + holder.name + ".prefab"); made++; Object.DestroyImmediate(holder);
                }
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
            return sb.Append("  dressed ").Append(made).ToString();
        }

        static void DressHall(string name, Renderer[] all, Bounds B, Func<string, Vector3, float, Vector3, GameObject> Put)
        {
            // ground storey only: upper storeys of a tiered hall stand back from the wall the pipes hang on
            var W = BoundsOf(all.Where(r => (Has(r.name, "Wall") || Has(r.name, "Pillar")) && r.bounds.min.y < B.min.y + 2.5f && r.bounds.size.y < 7f))
                    ?? new Bounds(B.center, new Vector3(B.size.x * .7f, B.size.y * .5f, B.size.z * .7f));
            var roofs = all.Where(r => Has(r.name, "Roof")).ToArray();
            float ground = B.min.y, zb = W.min.z - .14f, y1 = Mathf.Max(ground + 2.2f, W.max.y - .5f), span = W.size.x - .6f, back = B.min.z - 1.2f;
            bool large = W.size.x > 8f;

            // ---- on the roof: ridge line and slope height come from the roof's own vertices
            if (roofs.Length > 0)
            {
                var verts = WorldVertices(roofs); float top = verts.Max(v => v.y);
                var crest = verts.Where(v => v.y > top - .45f).ToList();
                float cx0 = crest.Min(v => v.x), cx1 = crest.Max(v => v.x), cz0 = crest.Min(v => v.z), cz1 = crest.Max(v => v.z);
                bool alongX = cx1 - cx0 >= cz1 - cz0; float length = alongX ? cx1 - cx0 : cz1 - cz0;
                var mid = new Vector3((cx0 + cx1) / 2f, top, (cz0 + cz1) / 2f);
                if (length > 2.5f)
                {
                    Put("Steam_RidgePipe8", new Vector3(mid.x, top - .04f, mid.z), alongX ? 0f : 90f, new Vector3(length * .86f / 8f, 1f, 1f));
                    // stack through the rear slope, a third of the way down from the ridge
                    float eave = W.max.y + .6f, depth = alongX ? B.size.z : B.size.x;
                    Vector3 at = alongX ? new Vector3(mid.x - length * .28f, 0f, mid.z - depth * .16f) : new Vector3(mid.x - depth * .16f, 0f, mid.z - length * .28f);
                    float ys = SurfaceY(verts, at.x, at.z, .7f, Mathf.Lerp(top, eave, .4f));
                    float sy = Mathf.Clamp((top + (large ? 2.4f : 1.5f) - ys) / 6.3f, .45f, 1.5f), sr = large ? 1f : .7f;
                    Smoke(Put("Steam_RoofStack", new Vector3(at.x, ys - .12f, at.z), 0f, new Vector3(sr, sy, sr)), 1f);
                    if (large && length > 6f)
                    {
                        Vector3 at2 = alongX ? new Vector3(mid.x + length * .30f, 0f, mid.z - depth * .12f) : new Vector3(mid.x - depth * .12f, 0f, mid.z + length * .30f);
                        float y2 = SurfaceY(verts, at2.x, at2.z, .6f, Mathf.Lerp(top, eave, .3f));
                        Smoke(Put("Steam_RoofStack", new Vector3(at2.x, y2 - .12f, at2.z), 0f, new Vector3(.62f, Mathf.Clamp((top + 1.2f - y2) / 6.3f, .35f, 1f), .62f)), .6f);
                    }
                    // gear train in the gable under the ridge end
                    float rise = top - eave, gs = Mathf.Clamp(rise * .26f, .42f, 1.05f);
                    if (rise > 1.6f)
                    {
                        if (alongX) Put("Steam_GableGear", new Vector3(cx1 + .10f, top - .55f - 1.25f * gs, mid.z), 90f, Vector3.one * gs);
                        else Put("Steam_GableGear", new Vector3(mid.x, top - .55f - 1.25f * gs, cz1 + .10f), 0f, Vector3.one * gs);
                    }
                }
                else Put("Steam_Vent", new Vector3(mid.x, top - .05f, mid.z), 0f, Vector3.one * 1.3f);
            }

            // ---- back wall: pipe bundle under the eave, a return near the sill, riser, valve, gauge
            Put("Steam_PipeBundle8", new Vector3(W.max.x - .3f, y1 - .1f, zb), 180f, new Vector3(span / 8f, 1f, 1f));
            Put("Steam_Pipe8", new Vector3(W.max.x - .3f, W.min.y + .9f, zb + .03f), 180f, new Vector3(span / 8f, .7f, .7f));
            Put("Steam_Riser4", new Vector3(W.min.x, ground, zb), 0f, new Vector3(1f, (y1 + .12f - ground) / 4f, 1f));
            Put("Steam_Valve", new Vector3(W.center.x - span * .2f, y1 + .12f, zb - .04f), 180f, Vector3.one);
            Put("Steam_Gauge", new Vector3(W.center.x + span * .22f, y1 + .62f, zb - .05f), 180f, Vector3.one);

            // ---- front: slim risers at the two corners that turn in under the eave, a gauge on the left one
            float yf = Mathf.Max(ground + 2.0f, W.max.y - .35f), zf = W.max.z + .12f;
            Put("Steam_Riser4", new Vector3(W.min.x - .06f, ground, zf), 0f, new Vector3(.62f, (yf - ground) / 4f, .62f));
            Put("Steam_Riser4", new Vector3(W.max.x + .06f, ground, zf), 180f, new Vector3(.62f, (yf - ground) / 4f, .62f));
            Put("Steam_Gauge", new Vector3(W.min.x - .06f, W.min.y + 1.55f, zf + .07f), 0f, Vector3.one * .85f);

            if (large)
            {
                // boiler behind the platform fed into the riser, a tank on the other corner, a gear plate low on the far side wall
                Put("Steam_Boiler", new Vector3(W.min.x + 1.9f, ground, back), 0f, Vector3.one);
                Put("Steam_Pipe4", new Vector3(W.min.x, ground + 2.35f, back), -90f, new Vector3(Mathf.Max(.2f, (zb - back) / 4f), .8f, .8f));
                Put("Steam_Tank", new Vector3(W.max.x - .9f, ground, back + .2f), 0f, Vector3.one);
                float gs = Mathf.Clamp(W.size.y / 3.6f, .5f, 1.1f);
                Put("Steam_Gears", new Vector3(W.min.x - .14f, W.min.y + .2f, W.center.z), -90f, Vector3.one * gs);
                Put("Steam_Pipe8", new Vector3(W.max.x + .13f, W.min.y + 1.0f, W.max.z - .3f), 90f, new Vector3((W.size.z - .6f) / 8f, .7f, .7f));
            }
            else
            {
                Put("Steam_Tank", new Vector3(W.min.x - 1.0f, ground, W.center.z - W.size.z * .2f), 0f, Vector3.one * (W.size.x < 5f ? .6f : Mathf.Clamp(B.size.y / 6f, .6f, 1f)));
                Put("Steam_Valve", new Vector3(W.max.x + .1f, W.min.y + 1.3f, W.center.z), 90f, Vector3.one);
            }
            // the bell pavilion strikes its bell by machine
            if (Has(name, "Song_Jong")) Put("Steam_BellStriker", new Vector3(W.center.x - .4f, W.min.y, W.center.z), 0f, Vector3.one * .9f);
        }

        // Thin grey smoke from a stack's mouth: a plain particle system (no script, no light, unlit and not emissive - smoke is ink, not a lamp).
        static void Smoke(GameObject stack, float amount)
        {
            var go = new GameObject("Smoke"); go.transform.SetParent(stack.transform, false); go.transform.localPosition = new Vector3(0f, 6.25f, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var parent = stack.transform.lossyScale; go.transform.localScale = new Vector3(1f / Mathf.Max(.01f, parent.x), 1f / Mathf.Max(.01f, parent.z), 1f / Mathf.Max(.01f, parent.y));
            var ps = go.AddComponent<ParticleSystem>(); var main = ps.main;
            main.loop = true; main.prewarm = true; main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f); main.startSpeed = new ParticleSystem.MinMaxCurve(.7f, 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(.7f * amount + .3f, 1.3f * amount + .3f); main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(.16f, .15f, .14f, .42f), new Color(.34f, .33f, .31f, .30f));
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 40; main.gravityModifier = -.012f; main.playOnAwake = true;
            var emission = ps.emission; emission.rateOverTime = 2.4f * amount + .6f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 7f; shape.radius = .16f;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, .5f), new Keyframe(.4f, 1.6f), new Keyframe(1f, 3.4f)));
            var color = ps.colorOverLifetime; color.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .12f), new GradientAlphaKey(.55f, .6f), new GradientAlphaKey(0f, 1f) });
            color.color = g;
            var drift = ps.velocityOverLifetime; drift.enabled = true; drift.space = ParticleSystemSimulationSpace.World;
            drift.x = new ParticleSystem.MinMaxCurve(.25f, .6f); drift.y = new ParticleSystem.MinMaxCurve(0f, 0f); drift.z = new ParticleSystem.MinMaxCurve(-.1f, .15f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-.25f, .25f);
            var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = SmokeMaterial(); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; renderer.sortingFudge = 2f;
        }

        static Material SmokeMaterial()
        {
            string path = Dir + "/Materials/smoke.mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path); if (m != null) return m;
            Folder(Dir + "/Textures"); string texPath = Dir + "/Textures/smoke_puff.png";
            if (!File.Exists(Path.GetFullPath(texPath)))
            {
                const int n = 64; var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float dx = (x + .5f) / n * 2f - 1f, dy = (y + .5f) / n * 2f - 1f, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a) * (.8f + .2f * Mathf.PerlinNoise(x * .17f, y * .17f));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                t.Apply(); File.WriteAllBytes(Path.GetFullPath(texPath), t.EncodeToPNG()); Object.DestroyImmediate(t); AssetDatabase.ImportAsset(texPath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(texPath); importer.alphaIsTransparency = true; importer.maxTextureSize = 64; importer.SaveAndReimport();
            }
            m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "smoke" };
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath)); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent; m.SetOverrideTag("RenderType", "Transparent");
            AssetDatabase.CreateAsset(m, path); return m;
        }

        static void DressStone(string name, Renderer[] all, Bounds B, Func<string, Vector3, float, Vector3, GameObject> Put)
        {
            bool small = B.size.x < 6f && B.size.z < 6f;
            if (!small)
            {
                // rock buddha, bridge, pond: a tank and a tall feed beside it, a line along the foot, a water wheel at the bridge
                Put("Steam_Tank", new Vector3(B.min.x - .9f, B.min.y, B.center.z), 0f, Vector3.one);
                Put("Steam_PipeBundle8", new Vector3(B.min.x, B.min.y + .6f, B.min.z - .16f), 0f, new Vector3(B.size.x / 8f, 1f, 1f));
                Put("Steam_Valve", new Vector3(B.center.x, B.min.y + .82f, B.min.z - .22f), 180f, Vector3.one);
                if (B.size.y > 6f) Put("Steam_Riser4", new Vector3(B.min.x - .2f, B.min.y, B.center.z + .9f), 0f, new Vector3(1f, B.size.y * .7f / 4f, 1f));
                if (Has(name, "Seungseongyo")) Put("Steam_WaterWheel", new Vector3(B.center.x, B.min.y - .6f, B.max.z + 1.1f), 90f, Vector3.one);
                return;
            }
            // pagodas, stupas, steles: copper bands at the narrow waists of the profile, a collar at the foot, valve, gauge, vent, a thin riser
            var verts = WorldVertices(all); const int N = 26; var extent = new float[N]; float cx = B.center.x, cz = B.center.z;
            foreach (var v in verts)
            {
                int k = Mathf.Clamp((int)((v.y - B.min.y) / B.size.y * N), 0, N - 1);
                float e = Mathf.Max(Mathf.Abs(v.x - cx), Mathf.Abs(v.z - cz)); if (e > extent[k]) extent[k] = e;
            }
            float widest = extent.Max(); bool square = Has(name, "Pagoda") || Has(name, "bi");
            var waists = new List<int>();
            // a waist = a slice narrower than the widest slice within two slices on either side (roof stones stick out between the bodies)
            for (int k = 3; k < N - 2; k++)
            {
                if (extent[k] <= 0f) continue;
                float around = Mathf.Max(Mathf.Max(extent[Mathf.Max(0, k - 2)], extent[k - 1]), Mathf.Max(extent[k + 1], extent[Mathf.Min(N - 1, k + 2)]));
                bool lowest = extent[k] <= extent[k - 1] && extent[k] <= extent[k + 1];
                if (lowest && extent[k] < around * .93f && extent[k] < widest * .9f && (waists.Count == 0 || k - waists[waists.Count - 1] >= 3)) waists.Add(k);
            }
            foreach (int k in waists.Take(3))
            {
                float y = B.min.y + (k + .5f) / N * B.size.y, d = extent[k] * 2f + .05f;
                Put(square ? "Steam_BandSquare" : "Steam_BandRound", new Vector3(cx, y, cz), 0f, new Vector3(d, 1f, d));
            }
            // the scans stand on a wide ground slab: the collar hugs the lowest tier of the work itself, and the fittings follow its size
            float foot = Enumerable.Range(1, 5).Select(k => extent[k]).Where(e => e > 0f).DefaultIfEmpty(widest).Min() * 2f + .08f, fs = Mathf.Clamp(B.size.y / 5f, .4f, 1f);
            Put("Steam_Collar", new Vector3(cx, B.min.y + B.size.y / N, cz), 0f, new Vector3(foot, .55f * fs, foot));
            Put("Steam_Valve", new Vector3(cx - foot * .2f, B.min.y + B.size.y / N + .1f * fs, cz + foot / 2f + .05f), 0f, Vector3.one * .6f * fs);
            Put("Steam_Gauge", new Vector3(cx + foot * .2f, B.min.y + B.size.y / N + .42f * fs, cz + foot / 2f + .05f), 0f, Vector3.one * .8f * fs);
            Put("Steam_Vent", new Vector3(cx + foot / 2f - .06f, B.min.y + B.size.y / N + .19f * fs, cz - foot / 2f + .06f), 0f, Vector3.one * fs);
            float up = waists.Count > 0 ? (waists[0] + .5f) / N * B.size.y : B.size.y * .4f;
            Put("Steam_Riser4", new Vector3(cx - (waists.Count > 0 ? extent[waists[0]] : foot / 2f) - .32f, B.min.y, cz), 0f, new Vector3(.55f * fs, Mathf.Max(.2f, (up + .05f) / 4f), .55f * fs));
        }

        // finish - LODGroup and colliders on every dressed prefab.
        // LOD0 everything; LOD1 only pieces 1.6 m or larger (roofs, walls, pillars, platform, stacks, boiler ...); culled under 1.2 % of the screen.
        // Colliders: platform / steps / stone keep their own triangles (walking surfaces are not clustered - SPEC-EDITOR-MEMORY-308 B);
        // everything else is one mesh clustered at 0.22 m (walking surfaces at 0.08 m). All of it is a subset of what is drawn.
        // prefix / child: also used for the part-kit prefabs (Temple308K_*, child "Kit" - KitDress308). Switched-off pieces (an opened door bay) are left out.
        internal static string Finish(string prefix, string child)
        {
            Folder(ColDir); var sb = new StringBuilder("SteamKit308 finish " + prefix + "\n"); int done = 0;
            foreach (string guid in AssetDatabase.FindAssets(prefix + " t:Prefab", new[] { TempleDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); if (!System.IO.Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal)) continue; var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var old = root.transform.Find("Col"); if (old != null) Object.DestroyImmediate(old.gameObject);
                    var renderers = root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.gameObject.activeInHierarchy).ToArray();
                    var group = root.GetComponent<LODGroup>(); if (group == null) group = root.AddComponent<LODGroup>();
                    var far = renderers.Where(r => r.name == "LampGlow" || r.name.StartsWith("KitLarge_", StringComparison.Ordinal) || (!r.name.StartsWith("KitSmall_", StringComparison.Ordinal) && Mathf.Max(r.bounds.size.x, Mathf.Max(r.bounds.size.y, r.bounds.size.z)) >= 1.6f)).Cast<Renderer>().ToArray();   // KitSmall_ = merged small fittings (KitDress308): near only
                    group.SetLODs(new[] { new LOD(.14f, renderers.Cast<Renderer>().ToArray()), new LOD(.012f, far) }); group.RecalculateBounds();

                    var fine = new List<CombineInstance>(); var coarse = new List<CombineInstance>(); var inverse = root.transform.worldToLocalMatrix;
                    foreach (var r in renderers)
                    {
                        var f = r.GetComponent<MeshFilter>(); if (f == null || f.sharedMesh == null) continue;
                        float size = Mathf.Max(r.bounds.size.x, Mathf.Max(r.bounds.size.y, r.bounds.size.z)); if (size < .6f || r.name.StartsWith("KitSmall_", StringComparison.Ordinal) || r.name == "LampGlow") continue;
                        bool walk = Has(r.name, "Kidan") || Has(r.name, "Step") || Has(r.name, "Floor") || Has(r.name, "Seungseongyo") || Has(r.name, "Samindang");
                        var kitRoot = root.transform.Find(child); bool steamPart = kitRoot != null && r.transform.IsChildOf(kitRoot); if (steamPart && size < 1.2f) continue;
                        for (int s = 0; s < f.sharedMesh.subMeshCount; s++)
                            (walk ? fine : coarse).Add(new CombineInstance { mesh = f.sharedMesh, subMeshIndex = s, transform = inverse * r.transform.localToWorldMatrix });
                    }
                    var col = new GameObject("Col"); col.transform.SetParent(root.transform, false); long tris = 0;
                    foreach (var (list, cell, tag) in new[] { (fine, .08f, "Walk"), (coarse, .22f, "Mass") })
                    {
                        if (list.Count == 0) continue;
                        var combined = new Mesh { indexFormat = IndexFormat.UInt32 }; combined.CombineMeshes(list.ToArray(), true, true);
                        var mesh = MemoryFix308.Cluster(combined, cell); Object.DestroyImmediate(combined);
                        mesh.name = root.name + "_" + tag; string meshPath = ColDir + "/" + mesh.name + ".asset";
                        AssetDatabase.DeleteAsset(meshPath); AssetDatabase.CreateAsset(mesh, meshPath);
                        var g = new GameObject(tag); g.transform.SetParent(col.transform, false); var mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; tris += mesh.triangles.Length / 3;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path); done++;
                    sb.Append("  ").Append(root.name).Append(": renderers ").Append(renderers.Length).Append(" far ").Append(far.Length).Append(", collider triangles ").Append(tris).Append('\n');
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return sb.Append("  finished ").Append(done).ToString();
        }

        // lite[:share] - the pack models are far denser than this game needs (every roof tile is modelled): replace the meshes of the
        // plain Temple308_* prefabs by a lighter copy taken from the importer's own mesh LOD chain (the first level whose triangle
        // count is at or under `share` of the full mesh), compacted to the vertices that level uses. Same local space, same
        // attributes, same submeshes: nothing else in the prefab changes. Copies live in Temple308/Lite. Run dress + finish after.
        static string Lite(float share)
        {
            const string liteDir = "Assets/_Project/Art/World/Temple308/Lite"; Folder(liteDir);
            var cache = new Dictionary<Mesh, Mesh>(); long before = 0, after = 0; int swapped = 0, kept = 0; var notes = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("Temple308_ t:Prefab", new[] { TempleDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); if (!Path.GetFileNameWithoutExtension(path).StartsWith("Temple308_", StringComparison.Ordinal)) continue;
                var root = PrefabUtility.LoadPrefabContents(path); bool dirty = false;
                try
                {
                    foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var m = f.sharedMesh; if (m == null) continue;
                        if (AssetDatabase.GetAssetPath(m).StartsWith(liteDir, StringComparison.Ordinal)) { kept++; continue; }
                        before += m.vertexCount;
                        if (m.vertexCount < 6000 || m.lodCount < 2) { after += m.vertexCount; kept++; continue; }
                        if (!cache.TryGetValue(m, out var lite))
                        {
                            lite = LiteCopy(m, share, out string note); cache[m] = lite;
                            if (lite != null) { string asset = liteDir + "/" + m.name + "_Lite.asset"; AssetDatabase.DeleteAsset(asset); AssetDatabase.CreateAsset(lite, asset); }
                            else if (notes.Count < 6) notes.Add(m.name + ": " + note);
                        }
                        if (lite == null) { after += m.vertexCount; kept++; continue; }
                        f.sharedMesh = lite; after += lite.vertexCount; swapped++; dirty = true;
                    }
                    if (dirty) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return "lite " + share.ToString("F2") + ": swapped " + swapped + " filters (" + cache.Values.Count(x => x != null) + " meshes), kept " + kept + " | vertices " + before.ToString("N0") + " -> " + after.ToString("N0")
                   + (notes.Count > 0 ? "\n  skipped: " + string.Join(" | ", notes) : "");
        }

        static Mesh LiteCopy(Mesh m, float share, out string note)
        {
            note = ""; int subs = m.subMeshCount; int level = -1; long full = 0;
            for (int s = 0; s < subs; s++) full += m.GetLod(s, 0).indexCount;
            for (int l = 1; l < m.lodCount; l++)
            {
                long count = 0; for (int s = 0; s < subs; s++) count += m.GetLod(s, l).indexCount;
                level = l; if (count <= full * share) break;
            }
            if (level < 1) { note = "no lower level"; return null; }
            var pos = m.vertices; var nor = m.normals; var tan = m.tangents; var uv = m.uv; var uv2 = m.uv2;
            var map = new int[pos.Length]; for (int i = 0; i < map.Length; i++) map[i] = -1;
            var used = new List<int>(); var tris = new int[subs][];
            // the LOD ranges address the mesh's whole index buffer (GetIndices returns level 0 only)
            int[] buffer;
            using (var data = Mesh.AcquireReadOnlyMeshData(m))
            {
                var d0 = data[0];
                if (d0.indexFormat == IndexFormat.UInt16) { var raw = d0.GetIndexData<ushort>(); buffer = new int[raw.Length]; for (int i = 0; i < raw.Length; i++) buffer[i] = raw[i]; }
                else { var raw = d0.GetIndexData<uint>(); buffer = new int[raw.Length]; for (int i = 0; i < raw.Length; i++) buffer[i] = (int)raw[i]; }
            }
            for (int s = 0; s < subs; s++)
            {
                var range = m.GetLod(s, level); var sub = m.GetSubMesh(s);
                if ((long)range.indexStart + range.indexCount > buffer.Length) { note = "level " + level + " range outside the index buffer (" + range.indexStart + "+" + range.indexCount + " of " + buffer.Length + ")"; return null; }
                var t = new int[range.indexCount];
                for (int i = 0; i < t.Length; i++)
                {
                    int v = buffer[range.indexStart + i] + sub.baseVertex; if (map[v] < 0) { map[v] = used.Count; used.Add(v); }
                    t[i] = map[v];
                }
                tris[s] = t;
            }
            if (used.Count >= pos.Length * .9f) { note = "level " + level + " keeps " + used.Count + " of " + pos.Length + " vertices"; return null; }
            var lite = new Mesh { name = m.name + "_Lite", indexFormat = used.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            lite.SetVertices(used.Select(i => pos[i]).ToArray());
            if (nor.Length == pos.Length) lite.SetNormals(used.Select(i => nor[i]).ToArray());
            if (tan.Length == pos.Length) lite.SetTangents(used.Select(i => tan[i]).ToArray());
            if (uv.Length == pos.Length) lite.SetUVs(0, used.Select(i => uv[i]).ToArray());
            if (uv2.Length == pos.Length) lite.SetUVs(1, used.Select(i => uv2[i]).ToArray());
            lite.subMeshCount = subs; for (int s = 0; s < subs; s++) lite.SetTriangles(tris[s], s, false);
            lite.RecalculateBounds(); return lite;
        }

        // meshlod - Unity 6.2+ can build mesh LODs inside the model importer (one mesh, several index ranges). Probe lists the importer
        // properties with "lod" in the name on one pack model; apply switches them on for every pack model and reimports.
        static string MeshLod(bool apply)
        {
            var models = AssetDatabase.FindAssets("t:Model", new[] { "Assets/TemplesKor" }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
            if (models.Length == 0) return "REFUSED no models under Assets/TemplesKor";
            if (!apply)
            {
                var so = new SerializedObject(AssetImporter.GetAtPath(models[0])); var it = so.GetIterator(); var names = new List<string>();
                while (it.Next(true)) if (it.propertyPath.IndexOf("lod", StringComparison.OrdinalIgnoreCase) >= 0 && names.Count < 40) names.Add(it.propertyPath + "=" + (it.propertyType == SerializedPropertyType.Boolean ? it.boolValue.ToString() : it.propertyType == SerializedPropertyType.Integer ? it.intValue.ToString() : it.propertyType == SerializedPropertyType.Float ? it.floatValue.ToString() : it.propertyType.ToString()));
                return "meshlod probe (" + models.Length + " models): " + string.Join(" | ", names);
            }
            int changed = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in models)
                {
                    var importer = AssetImporter.GetAtPath(path); var so = new SerializedObject(importer); var p = so.FindProperty("generateMeshLods");
                    if (p == null || p.propertyType != SerializedPropertyType.Boolean) return "REFUSED the importer has no generateMeshLods switch (run meshlod to see the names)";
                    if (!p.boolValue) { p.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport(); changed++; }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            return "meshlod apply: " + changed + " of " + models.Length + " models switched on";
        }
    }
}
