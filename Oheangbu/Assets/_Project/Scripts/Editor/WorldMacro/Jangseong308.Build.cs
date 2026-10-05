using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // plan / apply / revert / preview of the wall ledger. The pack (Assets/HwaseongForteressGate) is referenced by prefab NAME only;
    // what is derived from it (sheared, merged meshes; ink material copies) is written under the git-ignored asset root of the cliff
    // ledger (cb308.json assetRoot), in a folder named after the build key, so a scene's assets are never rewritten under it (AC-B22).
    public static partial class Jangseong308
    {
        const string BuildVersion = "j308.1";

        // ---------------------------------------------------------------- merged meshes (36-byte vertices, one sub-mesh per material)

        [StructLayout(LayoutKind.Sequential)] struct V36 { public Vector3 p; public short nx, ny, nz, nw, tx, ty, tz, tw; public Vector2 uv; }

        sealed class MeshAcc
        {
            readonly List<V36> verts = new List<V36>(); readonly List<Material> mats = new List<Material>(); readonly List<List<int>> tris = new List<List<int>>();
            public int Vertices => verts.Count;
            public int Triangles => tris.Sum(t => t.Count) / 3;
            public Material[] Materials => mats.ToArray();

            List<int> Bucket(Material m)
            {
                int i = mats.IndexOf(m);
                if (i < 0) { mats.Add(m); tris.Add(new List<int>()); i = mats.Count - 1; }
                return tris[i];
            }

            static short S(float v) => (short)Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 32767f);

            public void Add(Mesh src, Material[] materials, Matrix4x4 m)
            {
                var v = src.vertices; var n = src.normals; var t = src.tangents; var uv = src.uv; int o = verts.Count;
                var nm = m.inverse.transpose; bool flip = m.determinant < 0f;
                for (int i = 0; i < v.Length; i++)
                {
                    var nn = n != null && n.Length == v.Length ? nm.MultiplyVector(n[i]).normalized : Vector3.up;
                    Vector3 td; float tw = 1f;
                    if (t != null && t.Length == v.Length) { td = m.MultiplyVector(new Vector3(t[i].x, t[i].y, t[i].z)).normalized; tw = flip ? -t[i].w : t[i].w; }
                    else td = m.MultiplyVector(Vector3.right).normalized;
                    verts.Add(new V36 { p = m.MultiplyPoint3x4(v[i]), nx = S(nn.x), ny = S(nn.y), nz = S(nn.z), tx = S(td.x), ty = S(td.y), tz = S(td.z), tw = S(tw), uv = uv != null && uv.Length == v.Length ? uv[i] : Vector2.zero });
                }
                for (int s = 0; s < src.subMeshCount; s++)
                {
                    var list = Bucket(materials[Mathf.Min(s, materials.Length - 1)]); var idx = src.GetTriangles(s);
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        list.Add(o + idx[i]);
                        if (flip) { list.Add(o + idx[i + 2]); list.Add(o + idx[i + 1]); } else { list.Add(o + idx[i + 1]); list.Add(o + idx[i + 2]); }
                    }
                }
            }

            /// <summary>Box with metre-scaled UVs (one texture repeat = one wall module face), as the look gate drew its procedural fill.</summary>
            public void AddBox(Vector3 size, Matrix4x4 m, Material material, float uvWidth, float uvHeight)
            {
                var h = size * .5f; var list = Bucket(material); var nm = m.inverse.transpose;
                void Face(Vector3 normal, Vector3 r, Vector3 u)
                {
                    var c = Vector3.Scale(normal, h); var rr = Vector3.Scale(r, h); var uu = Vector3.Scale(u, h); int o = verts.Count;
                    var nn = nm.MultiplyVector(normal).normalized; var td = m.MultiplyVector(r).normalized;
                    foreach (var q in new[] { c - rr - uu, c - rr + uu, c + rr + uu, c + rr - uu })
                        verts.Add(new V36 { p = m.MultiplyPoint3x4(q), nx = S(nn.x), ny = S(nn.y), nz = S(nn.z), tx = S(td.x), ty = S(td.y), tz = S(td.z), tw = S(-1f), uv = new Vector2(Vector3.Dot(q, r) / uvWidth, Vector3.Dot(q, u) / uvHeight) });
                    list.Add(o); list.Add(o + 1); list.Add(o + 2); list.Add(o); list.Add(o + 2); list.Add(o + 3);   // front = cross(u, r)
                }
                Face(Vector3.right, Vector3.forward, Vector3.up); Face(Vector3.left, Vector3.back, Vector3.up);
                Face(Vector3.forward, Vector3.left, Vector3.up); Face(Vector3.back, Vector3.right, Vector3.up);
                Face(Vector3.up, Vector3.right, Vector3.forward); Face(Vector3.down, Vector3.left, Vector3.forward);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertexBufferParams(verts.Count,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm16, 4),
                    new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.SNorm16, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
                mesh.SetVertexBufferData(verts, 0, 0, verts.Count);
                int total = tris.Sum(t => t.Count); bool wide = verts.Count > 65535;
                mesh.SetIndexBufferParams(total, wide ? IndexFormat.UInt32 : IndexFormat.UInt16);
                if (wide) mesh.SetIndexBufferData(tris.SelectMany(t => t).ToArray(), 0, 0, total);
                else mesh.SetIndexBufferData(tris.SelectMany(t => t).Select(i => (ushort)i).ToArray(), 0, 0, total);
                mesh.subMeshCount = tris.Count; int start = 0;
                for (int i = 0; i < tris.Count; i++) { mesh.SetSubMesh(i, new SubMeshDescriptor(start, tris[i].Count, MeshTopology.Triangles)); start += tris[i].Count; }
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // ---------------------------------------------------------------- where derived things go (assets, or memory for the preview)

        sealed class Sink
        {
            public bool Memory; public string MeshDir, MaterialDir; public readonly SortedSet<string> Assets = new SortedSet<string>(StringComparer.Ordinal); public int MeshesMade, MeshesReused, MaterialsMade, MaterialsUpdated;
            readonly Dictionary<Material, Material> ink = new Dictionary<Material, Material>(); readonly Layout lay; readonly Shader shader; Dictionary<string, Mesh> colliders; bool colliderFileNew;
            public HideFlags Flags => Memory ? HideFlags.DontSave : HideFlags.None;

            public Sink(Layout lay, CliffCore308.Config cfg, string key, bool memory)
            {
                this.lay = lay; Memory = memory; MeshDir = cfg.assetRoot + MeshSub + "/" + key; MaterialDir = cfg.assetRoot + MaterialSub;
                shader = Shader.Find(lay.materials.shader);
                if (shader == null) throw new PostLedger308.Refused("shader " + lay.materials.shader + " not found");
                if (!Memory && (PostLedger308.IsProtectedPath(MeshDir + "/") || PostLedger308.IsProtectedPath(MaterialDir + "/") || !MeshDir.StartsWith(cfg.assetRoot + "/", StringComparison.Ordinal)))
                    throw new PostLedger308.Refused("refusing the output folder " + MeshDir);
            }

            void Recipe(Material m, Material source)
            {
                float bump = source.HasProperty("_BumpScale") ? source.GetFloat("_BumpScale") : lay.materials.bumpFallback;
                m.shader = shader;
                for (int i = 0; i < lay.materials.floatNames.Length && i < lay.materials.floatValues.Length; i++)
                    if (m.HasProperty(lay.materials.floatNames[i])) m.SetFloat(lay.materials.floatNames[i], lay.materials.floatValues[i]);
                if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", bump);
                m.DisableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack; m.enableInstancing = false;
            }

            /// <summary>Ink copy of a pack material on the existing ink architecture shader (never the pack material itself).</summary>
            public Material Ink(Material source)
            {
                if (ink.TryGetValue(source, out var done)) return done;
                Material result;
                if (Memory) { result = new Material(source) { name = source.name + "_J308", hideFlags = HideFlags.DontSave }; Recipe(result, source); }
                else
                {
                    string path = MaterialDir + "/" + source.name + "_J308.mat"; var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                    var fresh = new Material(source) { name = source.name + "_J308" }; Recipe(fresh, source);
                    if (existing == null) { DevSceneKit.EnsureFolder(MaterialDir); AssetDatabase.CreateAsset(fresh, path); AssetDatabase.SaveAssetIfDirty(fresh); result = fresh; MaterialsMade++; }
                    else
                    {
                        if (EditorJsonUtility.ToJson(fresh) != EditorJsonUtility.ToJson(existing))
                        {
                            existing.shader = fresh.shader; existing.CopyPropertiesFromMaterial(fresh); existing.enableInstancing = false;
                            EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing); MaterialsUpdated++;
                        }
                        Object.DestroyImmediate(fresh); result = existing;
                    }
                    Assets.Add(path);
                }
                if (Glows(result)) throw new PostLedger308.Refused("material " + result.name + " has emission (ART-INK: the wall never glows)");
                ink[source] = result; return result;
            }

            /// <summary>The mesh asset of this build (made once; an existing file of the same build key is reused, never rewritten).</summary>
            public Mesh Keep(string name, Func<Mesh> make)
            {
                if (Memory) { var m = make(); m.hideFlags = HideFlags.DontSave; return m; }
                string path = MeshDir + "/" + name + ".asset"; var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                Assets.Add(path);
                if (existing != null) { MeshesReused++; return existing; }
                DevSceneKit.EnsureFolder(MeshDir);
                var mesh = make(); mesh.name = name; AssetDatabase.CreateAsset(mesh, path); AssetDatabase.SaveAssetIfDirty(mesh); MeshesMade++;
                return mesh;
            }

            /// <summary>Collider meshes live together in one container asset of the build (sub-assets by name).</summary>
            public Mesh Collider(string name, Func<Mesh> make)
            {
                if (Memory) return null;
                string path = MeshDir + "/J308_colliders.asset";
                if (colliders == null)
                {
                    colliders = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToDictionary(m => m.name, m => m);
                    colliderFileNew = colliders.Count == 0; Assets.Add(path);
                }
                if (colliders.TryGetValue(name, out var hit)) { MeshesReused++; return hit; }
                if (!colliderFileNew)
                {
                    // Unity names the main object of an asset file after the file: the first collider the build made carries the container's name, not its own.
                    // It is recognised by its shape (same vertices and triangles as the mesh this module would get), never by order.
                    string holder = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (colliders.TryGetValue(holder, out var first) && first != null)
                    {
                        var want = make(); var wv = want.vertices; var fv = first.vertices; bool same = wv.Length == fv.Length && want.triangles.SequenceEqual(first.triangles);
                        for (int i = 0; same && i < wv.Length; i++) same = (wv[i] - fv[i]).sqrMagnitude < 1e-6f;
                        Object.DestroyImmediate(want);
                        if (same) { colliders.Remove(holder); colliders[name] = first; MeshesReused++; return first; }
                    }
                    throw new PostLedger308.Refused("the collider container " + path + " exists but lacks " + name + " (an interrupted build): delete that folder and run apply again");
                }
                DevSceneKit.EnsureFolder(MeshDir);
                var mesh = make(); mesh.name = name;
                if (colliders.Count == 0) AssetDatabase.CreateAsset(mesh, path); else AssetDatabase.AddObjectToAsset(mesh, path);
                colliders[name] = mesh; MeshesMade++;
                return mesh;
            }

            public void Flush()
            {
                if (Memory || colliders == null || !colliderFileNew || colliders.Count == 0) return;
                var main = AssetDatabase.LoadMainAssetAtPath(MeshDir + "/J308_colliders.asset");
                if (main != null) { EditorUtility.SetDirty(main); AssetDatabase.SaveAssetIfDirty(main); }
            }
        }

        static bool Glows(Material m) => m != null && (m.IsKeywordEnabled("_EMISSION") || (m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0f));
        static bool Usable(Material m) => m != null && m.shader != null && m.shader.isSupported && m.shader.name != "Hidden/InternalErrorShader";

        sealed class Pack
        {
            readonly Layout lay; readonly Dictionary<string, (Mesh mesh, Material[] mats)> cache = new Dictionary<string, (Mesh, Material[])>(); public int Repaired;
            public Pack(Layout lay) { this.lay = lay; }
            public (Mesh mesh, Material[] mats) Get(string name)
            {
                if (cache.TryGetValue(name, out var hit)) return hit;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(lay.packPrefabDir + name + ".prefab");
                if (prefab == null) throw new PostLedger308.Refused("pack prefab missing: " + lay.packPrefabDir + name + ".prefab");
                var filter = prefab.GetComponentInChildren<MeshFilter>(true); var renderer = prefab.GetComponentInChildren<MeshRenderer>(true);
                if (filter == null || renderer == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) throw new PostLedger308.Refused("pack prefab " + name + " has no readable mesh");
                var mats = renderer.sharedMaterials; var fallback = mats.FirstOrDefault(Usable);
                if (fallback == null && name != lay.wallPrefab) fallback = Get(lay.wallPrefab).mats.FirstOrDefault(Usable);
                if (fallback == null) throw new PostLedger308.Refused("no usable material on pack prefab " + name);
                mats = mats.Length == 0 ? new[] { fallback } : mats.ToArray();
                for (int i = 0; i < mats.Length; i++) if (!Usable(mats[i])) { mats[i] = fallback; Repaired++; }
                hit = (filter.sharedMesh, mats); cache[name] = hit; return hit;
            }
        }

        /// <summary>Everything the build loads, checked before anything is written: the ink shader, every pack prefab the layout names
        /// (readable mesh, a usable material), the rock prefabs when the layout builds them.</summary>
        static List<string> Preflight(Layout lay)
        {
            var problems = new List<string>();
            if (Shader.Find(lay.materials.shader) == null) problems.Add("shader " + lay.materials.shader + " not found");
            var names = new List<string> { lay.wallPrefab, lay.parapetPrefab, lay.gate.leaves.leftPrefab, lay.gate.leaves.rightPrefab };
            names.AddRange(lay.bastionKit.pieces.Select(p => p.prefab)); names.AddRange((lay.bastionKit.boxes ?? new BoxFill[0]).Select(b => b.materialFrom));
            names.AddRange(lay.gate.pieces.Where(p => lay.gate.skipTags == null || !lay.gate.skipTags.Contains(p.tag)).Select(p => p.prefab)); names.AddRange((lay.gate.boxes ?? new BoxFill[0]).Select(b => b.materialFrom));
            var pack = new Pack(lay);
            foreach (string name in names.Distinct())
                try { pack.Get(name); } catch (PostLedger308.Refused r) { problems.Add(r.Message); }
            foreach (string door in new[] { lay.gate.leaves.leftPrefab, lay.gate.leaves.rightPrefab })
                if (!lay.gate.pieces.Any(p => p.tag == lay.gate.doorTag && p.prefab == door)) problems.Add("the layout has no gate piece '" + door + "' with tag " + lay.gate.doorTag);
            if (lay.rocks.build)
                foreach (var slot in lay.rocks.slots)
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(slot.path) == null) problems.Add("rock prefab missing: " + slot.path);
            return problems;
        }

        // ---------------------------------------------------------------- collider meshes (data-driven shapes, inside the visible mass)

        /// <summary>Convex prism of the wall profile ([z, y] pairs, +z = hill side) along the module, sheared to its grade.</summary>
        static Mesh ModulePrism(Layout lay, float length, float grade)
        {
            float[] zy = lay.wallKit.colliderProfileZY; int n = zy.Length / 2; float h = length * .5f;
            var v = new Vector3[n * 2]; var t = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float z = zy[i * 2], y = zy[i * 2 + 1]; if (y <= 0f) y -= lay.wallKit.colliderSink;
                v[i] = new Vector3(-h, y - grade * h, z); v[n + i] = new Vector3(h, y + grade * h, z);
            }
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; t.Add(i); t.Add(j); t.Add(n + j); t.Add(i); t.Add(n + j); t.Add(n + i); }
            for (int i = 1; i + 1 < n; i++) { t.Add(0); t.Add(i + 1); t.Add(i); t.Add(n); t.Add(n + i); t.Add(n + i + 1); }
            var mesh = new Mesh(); mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateBounds(); mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>The bastion's collider parts (frustums: bottom rect b, top rect t) as one triangle mesh.</summary>
        static Mesh PartsMesh(Part[] parts)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            foreach (var p in parts)
            {
                int o = v.Count;
                v.Add(new Vector3(p.b[0], p.y0, p.b[1])); v.Add(new Vector3(p.b[2], p.y0, p.b[1])); v.Add(new Vector3(p.b[2], p.y0, p.b[3])); v.Add(new Vector3(p.b[0], p.y0, p.b[3]));
                v.Add(new Vector3(p.t[0], p.y1, p.t[1])); v.Add(new Vector3(p.t[2], p.y1, p.t[1])); v.Add(new Vector3(p.t[2], p.y1, p.t[3])); v.Add(new Vector3(p.t[0], p.y1, p.t[3]));
                int[] q = { 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0 };
                foreach (int i in q) t.Add(o + i);
                // both windings: a static triangle mesh must stop the capsule from either side of a face
                for (int i = 0; i + 2 < q.Length; i += 3) { t.Add(o + q[i]); t.Add(o + q[i + 2]); t.Add(o + q[i + 1]); }
            }
            var mesh = new Mesh(); mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateBounds(); mesh.RecalculateNormals();
            return mesh;
        }

        // ---------------------------------------------------------------- the build (scene objects; memory = preview without colliders)

        static GameObject Make(string name, Transform parent, HideFlags flags)
        {
            var go = new GameObject(name) { hideFlags = flags, layer = 0 };
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        static MeshRenderer Show(GameObject go, Mesh mesh, Material[] mats, bool shadows)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = mats;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off; r.lightProbeUsage = LightProbeUsage.BlendProbes; r.allowOcclusionWhenDynamic = false;
            return r;
        }

        static Matrix4x4 PieceMatrix(Piece p) => Matrix4x4.TRS(new Vector3(p.x, p.y, p.z), Quaternion.Euler(0f, p.yaw, 0f), new Vector3(p.sx, p.sy, p.sz));

        static void BuildInto(GameObject root, Layout lay, Plan plan, Sink sink, WorldMacroPlaytestSession session, List<string> notes)
        {
            var flags = sink.Flags; var pack = new Pack(lay); bool solid = !sink.Memory;
            Make("Build|key=" + plan.Key + "|layout=" + lay.Sha256.Substring(0, 12) + "|code=" + BuildVersion + "|utc=" + PostLedger308.Utc(), root.transform, flags);
            if (solid)
            {
                // tops of the wall, the bastions and the gate are not NavMesh ground (#296 bake convention, area 1 = Not Walkable)
                var nm = root.AddComponent<Unity.AI.Navigation.NavMeshModifier>(); nm.overrideArea = true; nm.area = 1;
            }
            var wall = Make("Wall", root.transform, flags);
            var body = pack.Get(lay.wallPrefab); var cap = pack.Get(lay.parapetPrefab);
            var bodyMats = body.mats.Select(sink.Ink).ToArray(); var capMats = cap.mats.Select(sink.Ink).ToArray();
            foreach (string wing in lay.modules.Select(m => m.wing).Distinct())
            {
                var group = Make(char.ToUpperInvariant(wing[0]) + wing.Substring(1), wall.transform, flags);
                var colliders = solid ? Make("Colliders", group.transform, flags) : null;
                // chunks = the culling unit: the modules that stand on THIS ground (not hidden in the rock), split evenly, never more than chunkMaxModules each
                var standing = plan.Modules.Where(s => s.M.wing == wing && !s.Hidden).ToList();
                int chunkCount = Mathf.Max(1, Mathf.CeilToInt(standing.Count / (float)lay.seat.chunkMaxModules)), per = Mathf.CeilToInt(standing.Count / (float)chunkCount);
                for (int k = 0; k < chunkCount; k++)
                {
                    var members = standing.Skip(k * per).Take(per).ToList(); string chunkName = wing + "_" + (k + 1);
                    if (members.Count == 0) continue;
                    var origin = new Vector3(members.Average(s => s.Centre.x), members.Min(s => Mathf.Min(s.BaseA, s.BaseB)), members.Average(s => s.Centre.y));
                    var acc = new MeshAcc(); var shift = Matrix4x4.Translate(-origin);
                    foreach (var s in members)
                    {
                        Frame(s, out var pos, out var rot, out float grade);
                        var late3 = new Vector3(s.Late.x, 0f, s.Late.y); var shear = Matrix4x4.identity; shear.m10 = grade; var scale = Matrix4x4.Scale(new Vector3(s.M.scale, 1f, 1f));
                        acc.Add(body.mesh, bodyMats, shift * Matrix4x4.TRS(pos, rot, Vector3.one) * shear * scale);
                        acc.Add(cap.mesh, capMats, shift * Matrix4x4.TRS(pos + Vector3.up * lay.parapetY + late3 * lay.parapetZ, rot, Vector3.one) * shear * scale);
                    }
                    var go = Make("Mesh_" + chunkName, group.transform, flags); go.transform.position = origin;
                    Show(go, sink.Keep("J308_wall_" + chunkName, () => acc.Build("J308_wall_" + chunkName)), acc.Materials, true);
                }
                if (solid)
                    foreach (var s in standing)
                    {
                        Frame(s, out var pos, out var rot, out float grade);
                        var go = Make(s.M.id + "_col", colliders.transform, flags); go.transform.SetPositionAndRotation(pos, rot);
                        var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = sink.Collider(s.M.id + "_col", () => ModulePrism(lay, s.M.length, grade)); mc.convex = true;
                    }
                // bastions of the wing: one merged mesh, one collider each
                var chis = plan.Bastions.Where(b => b.B.wing == wing && !b.Hidden).ToList();
                if (chis.Count > 0)
                {
                    var origin = new Vector3(chis[0].B.at[0], chis.Min(b => b.BaseY), chis[0].B.at[1]); var acc = new MeshAcc(); var shift = Matrix4x4.Translate(-origin);
                    foreach (var b in chis)
                    {
                        var pivot = Matrix4x4.TRS(new Vector3(b.B.at[0], b.BaseY, b.B.at[1]), Quaternion.LookRotation(new Vector3(b.B.late[0], 0f, b.B.late[1]), Vector3.up), Vector3.one);
                        foreach (var p in lay.bastionKit.pieces) { var src = pack.Get(p.prefab); acc.Add(src.mesh, src.mats.Select(sink.Ink).ToArray(), shift * pivot * PieceMatrix(p)); }
                        foreach (var bx in lay.bastionKit.boxes)
                            acc.AddBox(new Vector3(bx.sx, bx.sy, bx.sz), shift * pivot * Matrix4x4.Translate(new Vector3(bx.cx, bx.cy, bx.cz)), sink.Ink(pack.Get(bx.materialFrom).mats[0]), lay.moduleLength, lay.moduleHeight);
                        if (!solid) continue;
                        var col = Make(b.B.id + "_col", colliders.transform, flags);
                        col.transform.SetPositionAndRotation(new Vector3(b.B.at[0], b.BaseY, b.B.at[1]), Quaternion.LookRotation(new Vector3(b.B.late[0], 0f, b.B.late[1]), Vector3.up));
                        var mc = col.AddComponent<MeshCollider>(); mc.sharedMesh = sink.Collider("J308_bastion_col", () => PartsMesh(lay.bastionKit.colliderParts)); mc.convex = false;
                    }
                    var go = Make("Bastions_" + wing, group.transform, flags); go.transform.position = origin;
                    Show(go, sink.Keep("J308_bastions_" + wing, () => acc.Build("J308_bastions_" + wing)), acc.Materials, true);
                }
            }

            // ---- gate: masonry + roof (shadows), gate-house timber (no shadows), three box colliders, the two leaves with WorldSealGate308
            var g = lay.gate; var lateG = new Vector3(g.late[0], 0f, g.late[1]).normalized;
            var gate = Make("Gate", root.transform, flags); gate.transform.SetPositionAndRotation(new Vector3(g.centre[0], plan.GateFloor, g.centre[1]), Quaternion.LookRotation(lateG, Vector3.up));
            var masonry = new MeshAcc(); var timber = new MeshAcc();
            foreach (var p in g.pieces)
            {
                if (p.tag == g.doorTag || (g.skipTags != null && g.skipTags.Contains(p.tag))) continue;
                var src = pack.Get(p.prefab); bool quiet = g.noShadowTags != null && g.noShadowTags.Contains(p.tag);
                (quiet ? timber : masonry).Add(src.mesh, src.mats.Select(sink.Ink).ToArray(), PieceMatrix(p));
            }
            foreach (var bx in g.boxes ?? new BoxFill[0])
                masonry.AddBox(new Vector3(bx.sx, bx.sy, bx.sz), Matrix4x4.Translate(new Vector3(bx.cx, bx.cy, bx.cz)), sink.Ink(pack.Get(bx.materialFrom).mats[0]), lay.moduleLength, lay.moduleHeight);
            Show(Make("Masonry", gate.transform, flags), sink.Keep("J308_gate_masonry", () => masonry.Build("J308_gate_masonry")), masonry.Materials, true);
            if (timber.Vertices > 0) Show(Make("Timber", gate.transform, flags), sink.Keep("J308_gate_timber", () => timber.Build("J308_gate_timber")), timber.Materials, false);
            if (solid)
                foreach (var c in g.colliders)
                {
                    var go = Make(c.name, gate.transform, flags); var box = go.AddComponent<BoxCollider>(); box.center = V3(c.center); box.size = V3(c.size);
                }
            var lv = g.leaves; var leaves = Make("Leaves", gate.transform, flags);
            Transform Leaf(string name, string prefab, float sign, out BoxCollider open)
            {
                // sign -1 = the leaf on the gate's -x side (hinge at -hingeX, opens with -OpenDegrees: WorldSealGate308's left leaf)
                var hinge = Make(name, leaves.transform, flags); hinge.transform.localPosition = new Vector3(sign * lv.hingeX, 0f, lv.hingeZ);
                var piece = g.pieces.FirstOrDefault(p => p.tag == g.doorTag && p.prefab == prefab);
                if (piece == null) throw new PostLedger308.Refused("the layout has no gate piece '" + prefab + "' with tag " + g.doorTag);
                var src = pack.Get(prefab); var leaf = Make(prefab, hinge.transform, flags);
                leaf.transform.localPosition = new Vector3(piece.x - sign * lv.hingeX, piece.y, piece.z - lv.hingeZ); leaf.transform.localRotation = Quaternion.Euler(0f, piece.yaw, 0f); leaf.transform.localScale = new Vector3(piece.sx, piece.sy, piece.sz);
                Show(leaf, src.mesh, src.mats.Select(sink.Ink).ToArray(), true);     // the pack mesh as it is: referenced, never copied
                open = null;
                if (solid)
                {
                    var c = Make(name + "_col", hinge.transform, flags); open = c.AddComponent<BoxCollider>();
                    var centre = V3(lv.collider.center); open.center = new Vector3(-sign * centre.x, centre.y, centre.z); open.size = V3(lv.collider.size); open.enabled = false;
                }
                return hinge.transform;
            }
            var left = Leaf("J308_leaf_L", lv.leftPrefab, -1f, out var openL); var right = Leaf("J308_leaf_R", lv.rightPrefab, 1f, out var openR);
            if (solid)
            {
                var blocker = Make(g.blocker.name, leaves.transform, flags).AddComponent<BoxCollider>(); blocker.center = V3(g.blocker.center); blocker.size = V3(g.blocker.size); blocker.enabled = false;
                var ob = Make(g.obstacle.name, leaves.transform, flags).AddComponent<NavMeshObstacle>();
                ob.shape = NavMeshObstacleShape.Box; ob.center = V3(g.obstacle.center); ob.size = V3(g.obstacle.size); ob.carving = true; ob.carveOnlyStationary = true; ob.enabled = false;
                var seal = leaves.AddComponent<WorldSealGate308>();
                seal.Session = session; seal.RequiredCompleted = g.requiredFact; seal.LeftLeaf = left; seal.RightLeaf = right; seal.OpenDegrees = lv.openDegrees; seal.Duration = Mathf.Max(.1f, lv.duration);
                seal.ClosedBlockers = new Collider[] { blocker }; seal.ClosedNavigation = new[] { ob }; seal.OpenColliders = new Collider[] { openL, openR };
            }

            // ---- hill-side rocks (only when the layout says build: the deck-access probe decides, see check)
            if (lay.rocks.build && lay.rocks.slots.Length > 0)
            {
                var rocks = Make("Rocks", root.transform, flags); var ground = (CliffCore308.Ground)CliffCore308.TerrainRoot(root.scene, CliffCore308.LoadConfig());
                foreach (var slot in lay.rocks.slots)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(slot.path);
                    if (prefab == null) throw new PostLedger308.Refused("rock prefab missing: " + slot.path);
                    if (!CliffCore308.TerrainY(ground, slot.x, slot.z, out float gy)) throw new PostLedger308.Refused("rock slot of " + slot.module + " has no terrain under it");
                    var go = sink.Memory ? Object.Instantiate(prefab) : (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.scene);
                    go.name = "J308_rock_" + slot.module; go.transform.SetParent(rocks.transform, false);
                    go.transform.SetPositionAndRotation(new Vector3(slot.x, gy - slot.bury, slot.z), Quaternion.Euler(0f, slot.yaw, 0f)); go.transform.localScale = Vector3.one * (slot.scale > 0f ? slot.scale : 1f);
                    foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = flags;
                    foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterials = r.sharedMaterials.Select(m => Usable(m) ? sink.Ink(m) : m).ToArray();
                    if (sink.Memory) foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                }
            }
            sink.Flush();
            if (pack.Repaired > 0) notes.Add("unusable pack material slots replaced by the renderer's first usable material: " + pack.Repaired);
        }

        /// <summary>Module frame: origin on the centreline at the mean base height, +z = hill side, grade = rise per metre toward local +x.</summary>
        static void Frame(Seated s, out Vector3 pos, out Quaternion rot, out float grade)
        {
            var late3 = new Vector3(s.Late.x, 0f, s.Late.y); rot = Quaternion.LookRotation(late3, Vector3.up);
            var right = rot * Vector3.right; var c = s.Centre; pos = new Vector3(c.x, s.BaseCentre, c.y);
            bool plusIsB = Vector3.Dot(right, new Vector3(s.B.x - s.A.x, 0f, s.B.y - s.A.y)) > 0f;
            grade = (plusIsB ? s.BaseB - s.BaseA : s.BaseA - s.BaseB) / s.M.length;
        }

        /// <summary>What stands under a built root (LOD0 only under a LODGroup).</summary>
        internal static Counts Measure(GameObject root, string derivedDir)
        {
            var c = new Counts(); var lower = new HashSet<Renderer>();
            foreach (var lg in root.GetComponentsInChildren<LODGroup>(true)) foreach (var lod in lg.GetLODs().Skip(1)) foreach (var r in lod.renderers) if (r != null) lower.Add(r);
            var meshes = new HashSet<Mesh>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (lower.Contains(r)) continue;
                c.renderers++; c.batches += r.sharedMaterials.Length; if (r.shadowCastingMode != ShadowCastingMode.Off) c.shadowCasters++;
                var f = r.GetComponent<MeshFilter>(); var m = f != null ? f.sharedMesh : null; if (m == null) continue;
                for (int s = 0; s < m.subMeshCount; s++) c.triangles += (int)(m.GetIndexCount(s) / 3);
                string path = AssetDatabase.GetAssetPath(m);
                if (string.IsNullOrEmpty(path) || path.StartsWith(derivedDir, StringComparison.Ordinal)) meshes.Add(m);
            }
            long bytes = 0;
            foreach (var m in meshes)
            {
                long idx = 0; for (int s = 0; s < m.subMeshCount; s++) idx += m.GetIndexCount(s);
                bytes += (long)m.vertexCount * m.GetVertexBufferStride(0) + idx * (m.indexFormat == IndexFormat.UInt16 ? 2 : 4);
            }
            c.meshMemoryMB = bytes / 1048576f; c.colliders = root.GetComponentsInChildren<Collider>(true).Length;
            var rocks = root.transform.Find("Rocks"); c.rocks = rocks != null ? rocks.childCount : 0;
            var cols = root.transform.Find("Wall"); c.modulesBuilt = cols != null ? cols.GetComponentsInChildren<MeshCollider>(true).Count(m => m.convex) : 0;
            return c;
        }

        // ---------------------------------------------------------------- save relocation profile (AC-B20)

        /// <summary>WorldSealProfile308 from the terrain package's closure (plan/beyond308_1b.json, schema of Seal308's beyond308.json).</summary>
        static WorldSealProfileSO BuildProfile(CliffCore308.Config cfg, WorldMacroPlaytestSession session, bool write, out string note, out bool changed)
        {
            string file = PostLedger308.RepoPath(BeyondFile), path = cfg.assetRoot + ProfileSub; changed = false;
            if (!File.Exists(file)) throw new PostLedger308.Refused(BeyondFile + " is missing (terrain package output): the wall is never placed without its save-relocation profile - a save that already stands beyond the pass would be stranded");
            var d = JObject.Parse(File.ReadAllText(file, Encoding.UTF8));
            string fact = (string)d["required_fact"] ?? "";
            if (fact != WorldMacroPlaytestSession.SouthGateOpenedId) throw new PostLedger308.Refused(BeyondFile + " required_fact '" + fact + "' is not " + WorldMacroPlaytestSession.SouthGateOpenedId);
            var rings = new List<WorldSealProfileSO.Ring>();
            foreach (var ring in (d["beyond_rings"] as JArray) ?? new JArray())
            {
                var pts = (ring as JArray ?? ring["points"] as JArray ?? new JArray()).OfType<JArray>().Where(q => q.Count >= 2).Select(q => new Vector2((float)q[0], (float)q[q.Count >= 3 ? 2 : 1])).ToArray();
                if (pts.Length >= 3) rings.Add(new WorldSealProfileSO.Ring { Points = pts });
            }
            if (rings.Count == 0) throw new PostLedger308.Refused(BeyondFile + " has no beyond_rings");
            var volumes = new List<WorldSealProfileSO.Volume>();
            foreach (var v in (d["ea_underground_volumes"] as JArray) ?? new JArray())
            {
                Vector3 Vec(JToken t) => t is JArray a && a.Count >= 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : Vector3.zero;
                var vol = new WorldSealProfileSO.Volume { Id = (string)v["id"] ?? "", Yaw = v["yaw"] != null ? (float)v["yaw"] : 0f };
                if (v["min"] != null && v["max"] != null) { var a = Vec(v["min"]); var b = Vec(v["max"]); vol.Center = (a + b) * .5f; vol.Size = b - a; }
                else { vol.Center = Vec(v["centre"] ?? v["center"]); vol.Size = Vec(v["size"]); }
                volumes.Add(vol);
            }
            string[] Ids(string key) => (d[key] as JArray)?.Select(t => (string)t).Where(s => !string.IsNullOrEmpty(s)).ToArray() ?? new string[0];
            var fresh = ScriptableObject.CreateInstance<WorldSealProfileSO>(); fresh.name = Path.GetFileNameWithoutExtension(path);
            fresh.RequiredFact = fact; fresh.BeyondRings = rings.ToArray(); fresh.EaUndergroundVolumes = volumes.ToArray();
            if (d["underground_depth_m"] != null) fresh.UndergroundDepth = (float)d["underground_depth_m"];
            if (d["move_drops"] != null) fresh.MoveDrops = (bool)d["move_drops"];
            if (d["move_vehicle"] != null) fresh.MoveVehicle = (bool)d["move_vehicle"];
            var fallback = Ids("fallback_rest_ids"); if (fallback.Length > 0) fresh.FallbackRestIds = fallback;
            var rests = Ids("ea_rest_ids");
            if (rests.Length == 0)
            {
                // default = every rest checkpoint of this scene's content whose feet are not beyond (the escort station is not a rest)
                var content = session.Content; var ids = new List<string>();
                if (content != null)
                    foreach (var cp in content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>())
                        if (cp != null && cp.IsConfigured && !ids.Contains(cp.Id) && !WorldSealRules308.InsideRings(fresh, cp.Feet)
                            && Array.Exists(content.Points ?? Array.Empty<PrologueContentSO.Point>(), p => p != null && p.Id == cp.Id && p.Kind == PrologueInteractionKind.Rest)) ids.Add(cp.Id);
                rests = ids.ToArray();
            }
            if (rests.Length == 0) throw new PostLedger308.Refused(BeyondFile + " names no EA rest (ea_rest_ids) and the scene content has none outside the rings");
            fresh.EaRestIds = rests; fresh.SourceHash = CliffCore308.ShaFile(file);
            var existing = AssetDatabase.LoadAssetAtPath<WorldSealProfileSO>(path);
            note = "profile " + path + " from " + BeyondFile + " sha " + Short(fresh.SourceHash) + ": rings " + fresh.BeyondRings.Length + " (" + fresh.BeyondRings.Sum(r => r.Points.Length) + " points), EA mine boxes " + fresh.EaUndergroundVolumes.Length
                + ", EA rests " + fresh.EaRestIds.Length + " [" + string.Join(",", fresh.EaRestIds) + "], fallback [" + string.Join(",", fresh.FallbackRestIds) + "]";
            if (existing != null && EditorJsonUtility.ToJson(existing) == EditorJsonUtility.ToJson(fresh)) { Object.DestroyImmediate(fresh); note += " (unchanged)"; return existing; }
            changed = true;
            if (!write) { Object.DestroyImmediate(fresh); note += existing == null ? " (would be created)" : " (would be updated)"; return existing; }
            if (existing == null) { DevSceneKit.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/')); AssetDatabase.CreateAsset(fresh, path); AssetDatabase.SaveAssetIfDirty(fresh); note += " (created)"; return fresh; }
            EditorUtility.CopySerialized(fresh, existing); existing.name = Path.GetFileNameWithoutExtension(path); EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing); Object.DestroyImmediate(fresh); note += " (updated)";
            return existing;
        }

        // ---------------------------------------------------------------- plan / apply

        static string Apply(string token, bool dry)
        {
            var cfg = CliffCore308.LoadConfig(); string path = CliffCore308.ScenePath(cfg, token); string key = CliffCore308.SceneKey(cfg, path);
            var lay = LoadLayout(); var stale = StaleInputs(lay);
            Scene scene;
            if (dry) scene = OpenForRead(path);
            else { PostLedger308.RequireEditable(); RequireNoPreview(); scene = PostLedger308.Open(path); }
            if (PostLedger308.IsProtectedPath(scene.path)) throw new PostLedger308.Refused("protected path " + scene.path);
            var session = CliffCore308.Session(scene);
            if (session == null) throw new PostLedger308.Refused(scene.path + " has no WorldMacroPlaytestSession");
            var ground = (CliffCore308.Ground)CliffCore308.TerrainRoot(scene, cfg);

            // the terrain under the wall must be the stage the layout was measured on (benches, cliff ends)
            var terrain = CliffBoundary308.ReadLedger(cfg, path); string height = CliffHeight308.For(path, out string source), heightSha = CliffHeight308.Sha(height);
            var blockers = new List<string>();
            if (stale.Count > 0) blockers.Add("the layout is stale (inputs changed: " + string.Join(", ", stale) + ") - run python Tools/Art/jangseong308.py build, then build-check");
            if (terrain.state != "applied" || terrain.stage != lay.stage) blockers.Add("the scene's terrain ledger is '" + terrain.state + (terrain.stage != "" ? " " + terrain.stage : "") + "', the wall needs stage " + lay.stage + " (CliffBoundary308 tiles:" + key + ":stage=" + lay.stage + ")");
            else if (heightSha != StageHeightSha(lay)) blockers.Add("the scene's height " + height + " (sha " + Short(heightSha) + ", " + source + ") is not the layout's stage height (sha " + Short(StageHeightSha(lay)) + ")");
            if (!File.Exists(PostLedger308.RepoPath(BeyondFile))) blockers.Add(BeyondFile + " is missing (terrain package output; no wall without the save-relocation profile)");
            blockers.AddRange(Preflight(lay));

            var plan = Compute(lay, ground);
            float floorOff = plan.GateFloor - lay.gate.floorY;
            if (Mathf.Abs(floorOff) > lay.limits.floorTolerance) blockers.Add("the gate floor on this scene's ground is " + F(plan.GateFloor) + " m, the user-fixed floor is " + F(lay.gate.floorY) + " (tolerance " + F(lay.limits.floorTolerance) + "): the road under the gate must not have changed");
            var ledger = ReadLedger(cfg, path); var roots = Roots(scene, lay);
            var profileAsset = AssetDatabase.LoadAssetAtPath<WorldSealProfileSO>(cfg.assetRoot + ProfileSub);
            string builtKey = roots.Length == 1 ? Marker(roots[0], "key") : "";

            var sb = new StringBuilder((dry ? "plan " : "apply ") + key + " (" + scene.path + ") | layout " + lay.Sha256.Substring(0, 12) + " " + lay.version + " | terrain " + terrain.state + " " + terrain.stage + ", height sha " + Short(heightSha) + "\n");
            sb.AppendLine(PlanText(lay, plan));
            var b = lay.counts.budget;
            int builtModules = plan.Modules.Count(s => !s.Hidden), builtBastions = plan.Bastions.Count(x => !x.Hidden);
            sb.AppendLine("would stand: " + builtModules + " module colliders + " + builtBastions + " bastion colliders + " + lay.gate.colliders.Length + " gate boxes + 1 blocker + 2 leaf colliders = "
                + (builtModules + builtBastions + lay.gate.colliders.Length + 3) + " colliders (budget " + b.colliders + "); rocks " + (lay.rocks.build ? lay.rocks.slots.Length.ToString(Inv) : "0 (layout build=false, " + lay.rocks.slots.Length + " slots)"));
            sb.AppendLine("scene now: " + (roots.Length == 0 ? "no " + lay.root + " root" : roots.Length + " " + lay.root + " root(s), build " + builtKey) + " | wall ledger " + ledger.state + (ledger.buildKey != "" ? " build " + ledger.buildKey : "")
                + " | session SealProfile " + (session.SealProfile == null ? "none" : AssetDatabase.GetAssetPath(session.SealProfile)));

            if (dry)
            {
                string profileNote = "save relocation: " + BeyondFile + " missing"; bool profileChanged = false;
                if (File.Exists(PostLedger308.RepoPath(BeyondFile))) BuildProfile(cfg, session, false, out profileNote, out profileChanged);
                sb.AppendLine(profileNote);
                bool same = roots.Length == 1 && builtKey == plan.Key && session.SealProfile != null && session.SealProfile == profileAsset && !profileChanged;
                sb.AppendLine(same ? "apply would change nothing (build " + plan.Key + " stands, profile linked)" : "apply would " + (roots.Length > 0 ? "replace the root (build " + builtKey + " -> " + plan.Key + ")" : "build the root (build " + plan.Key + ")")
                    + ", write the derived meshes under " + cfg.assetRoot + MeshSub + "/" + plan.Key + ", ink material copies under " + cfg.assetRoot + MaterialSub + ", link the profile and save the scene");
                sb.Append(blockers.Count == 0 ? "plan: nothing blocks apply | nothing written" : "plan: apply WOULD REFUSE - " + string.Join(" | ", blockers) + " | nothing written");
                return sb.ToString();
            }
            if (blockers.Count > 0) throw new PostLedger308.Refused(string.Join(" | ", blockers));

            // idempotence: the same build stands, the profile is the same and linked
            BuildProfile(cfg, session, false, out string dryProfile, out bool profileWould);
            if (roots.Length == 1 && builtKey == plan.Key && session.SealProfile != null && session.SealProfile == profileAsset && !profileWould)
            {
                if (ledger.state != "applied" || ledger.buildKey != plan.Key)
                {
                    // the ledger was lost or the last run stopped after the scene save: settle it from what stands
                    ledger.state = "applied"; ledger.stage = lay.stage; ledger.buildKey = plan.Key; ledger.layoutSha256 = lay.Sha256; ledger.heightSha256 = heightSha; ledger.sceneShaAfter = CliffCore308.ShaFile(PostLedger308.Abs(path));
                    ledger.counts = Measure(roots[0], cfg.assetRoot + MeshSub); ledger.counts.modules = lay.modules.Length; ledger.counts.bastions = builtBastions; ledger.gateFloorY = plan.GateFloor;
                    WriteLedger(cfg, ledger, "apply: build " + plan.Key + " already stands; ledger settled from the scene");
                    return sb.Append("apply: build " + plan.Key + " already stands and the profile is linked - no scene change; the ledger was settled from the scene").ToString();
                }
                return sb.Append("apply: already applied (build " + plan.Key + ") - no change, nothing written").ToString();
            }

            // ---- write: ledger first (before-values), backup, assets, scene
            string utc = PostLedger308.Utc(), sceneAbs = PostLedger308.Abs(path);
            string backupDir = CliffCore308.OutFile(cfg, "Backup/" + utc + "-wall-" + key); Directory.CreateDirectory(backupDir);
            File.Copy(sceneAbs, Path.Combine(backupDir, Path.GetFileName(path)), true);
            if (File.Exists(sceneAbs + ".meta")) File.Copy(sceneAbs + ".meta", Path.Combine(backupDir, Path.GetFileName(path) + ".meta"), true);
            string profileAbs = PostLedger308.Abs(cfg.assetRoot + ProfileSub);
            if (File.Exists(profileAbs)) File.Copy(profileAbs, Path.Combine(backupDir, Path.GetFileName(profileAbs)), true);
            bool firstTouch = ledger.state == "none" || ledger.state == "reverted";
            if (firstTouch)
            {
                // the session link as it was before the wall (restored by revert); a re-apply keeps the first record
                string before = session.SealProfile != null ? AssetDatabase.GetAssetPath(session.SealProfile) : "";
                if (before != cfg.assetRoot + ProfileSub) { ledger.sealProfileBefore = before; ledger.sealProfileBeforeGuid = before != "" ? AssetDatabase.AssetPathToGUID(before) : ""; }
            }
            ledger.state = "applying"; ledger.stage = lay.stage; ledger.sceneShaBefore = CliffCore308.ShaFile(sceneAbs); ledger.backup = backupDir.Replace('\\', '/'); ledger.layoutSha256 = lay.Sha256; ledger.heightSha256 = heightSha;
            ledger.beyondSha256 = CliffCore308.ShaFile(PostLedger308.RepoPath(BeyondFile)); ledger.buildKey = plan.Key; ledger.gateFloorY = plan.GateFloor; ledger.seatWorstDelta = plan.WorstDelta;
            WriteLedger(cfg, ledger, "apply started: build " + plan.Key + ", backup " + ledger.backup);

            var notes = new List<string>(); Sink sink; GameObject root; string profileNote2;
            try
            {
                sink = new Sink(lay, cfg, plan.Key, false);
                var profile = BuildProfile(cfg, session, true, out profileNote2, out _);
                foreach (var old in roots) Object.DestroyImmediate(old);
                root = new GameObject(lay.root) { layer = 0 }; SceneManager.MoveGameObjectToScene(root, scene);
                BuildInto(root, lay, plan, sink, session, notes);
                var so = new SerializedObject(session); var prop = so.FindProperty("SealProfile");
                if (prop == null) throw new Exception("the session has no SealProfile field");
                prop.objectReferenceValue = profile; so.ApplyModifiedPropertiesWithoutUndo();
                PostLedger308.SaveScene(scene);
            }
            catch (Exception e)
            {
                try { EditorSceneManager.OpenScene(path, OpenSceneMode.Single); } catch (Exception inner) { Debug.LogException(inner); }
                throw new Exception("apply stopped before the scene was saved (" + e.GetType().Name + ": " + e.Message + "); the scene was reloaded from disk, the ledger stays 'applying' - run apply:" + key + " again or revert:" + key
                    + " (derived assets made so far stay under their build folder)", e);
            }
            ledger.state = "applied"; ledger.sceneShaAfter = CliffCore308.ShaFile(sceneAbs); ledger.profile = cfg.assetRoot + ProfileSub;
            sink.Assets.Add(cfg.assetRoot + ProfileSub);
            ledger.assets = sink.Assets.Select(p => new AssetRec { path = p, sha256 = CliffCore308.ShaFile(PostLedger308.Abs(p)) }).ToArray();
            ledger.counts = Measure(root, cfg.assetRoot + MeshSub); ledger.counts.modules = lay.modules.Length; ledger.counts.bastions = builtBastions;
            WriteLedger(cfg, ledger, "apply done: build " + plan.Key + ", meshes made " + sink.MeshesMade + " / reused " + sink.MeshesReused + ", materials made " + sink.MaterialsMade + " / updated " + sink.MaterialsUpdated);
            var c = ledger.counts;
            sb.AppendLine(profileNote2);
            foreach (string n in notes) sb.AppendLine("note: " + n);
            sb.AppendLine("built: renderers " + c.renderers + ", triangles " + c.triangles + " (budget " + b.triangles + "), batches " + c.batches + " (budget " + b.batches + "), shadow casters " + c.shadowCasters + ", colliders " + c.colliders + " (budget " + b.colliders
                + "), mesh memory " + F(c.meshMemoryMB) + " MB (budget " + F(b.meshMemoryMB, "F1") + ") | assets: meshes made " + sink.MeshesMade + ", reused " + sink.MeshesReused + "; materials made " + sink.MaterialsMade + ", updated " + sink.MaterialsUpdated);
            sb.Append("apply: scene saved (sha " + Short(ledger.sceneShaBefore) + " -> " + Short(ledger.sceneShaAfter) + "), backup " + ledger.backup + ", ledger " + LedgerFile(cfg, key) + " | next: check:" + key);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- revert

        static string Revert(string token)
        {
            var cfg = CliffCore308.LoadConfig(); string path = CliffCore308.ScenePath(cfg, token); string key = CliffCore308.SceneKey(cfg, path);
            PostLedger308.RequireEditable(); RequireNoPreview();
            var lay = LoadLayout(); var scene = PostLedger308.Open(path);
            var session = CliffCore308.Session(scene); var ledger = ReadLedger(cfg, path); var roots = Roots(scene, lay);
            string ours = cfg.assetRoot + ProfileSub; bool linked = session != null && session.SealProfile != null && AssetDatabase.GetAssetPath(session.SealProfile) == ours;
            if (roots.Length == 0 && !linked)
            {
                if (ledger.state == "applied" || ledger.state == "applying") { ledger.state = "reverted"; WriteLedger(cfg, ledger, "revert: nothing of the wall stands in the scene; ledger settled"); }
                return "revert " + key + ": nothing to revert (no " + lay.root + " root, the session does not link the wall's profile) - scene not saved";
            }
            string utc = PostLedger308.Utc(), sceneAbs = PostLedger308.Abs(path), before = CliffCore308.ShaFile(sceneAbs);
            string backupDir = CliffCore308.OutFile(cfg, "Backup/" + utc + "-wall-" + key + "-revert"); Directory.CreateDirectory(backupDir);
            File.Copy(sceneAbs, Path.Combine(backupDir, Path.GetFileName(path)), true);
            string restored = "left as it is";
            try
            {
                foreach (var r in roots) Object.DestroyImmediate(r);
                if (linked)
                {
                    var prior = ledger.sealProfileBefore != "" ? AssetDatabase.LoadAssetAtPath<WorldSealProfileSO>(ledger.sealProfileBefore) : null;
                    var so = new SerializedObject(session); var prop = so.FindProperty("SealProfile"); prop.objectReferenceValue = prior; so.ApplyModifiedPropertiesWithoutUndo();
                    restored = prior != null ? "restored to " + ledger.sealProfileBefore : ledger.sealProfileBefore != "" ? "CLEARED (the recorded " + ledger.sealProfileBefore + " no longer loads)" : "cleared (it was empty before the wall)";
                }
                PostLedger308.SaveScene(scene);
            }
            catch (Exception e)
            {
                try { EditorSceneManager.OpenScene(path, OpenSceneMode.Single); } catch (Exception inner) { Debug.LogException(inner); }
                throw new Exception("revert stopped before the scene was saved (" + e.GetType().Name + ": " + e.Message + "); the scene was reloaded from disk, the ledger is unchanged - run revert:" + key + " again", e);
            }
            string after = CliffCore308.ShaFile(sceneAbs); string pristine = ledger.sceneShaBefore, applyBackup = ledger.backup;
            ledger.state = "reverted"; ledger.sceneShaAfter = after; ledger.backup = backupDir.Replace('\\', '/');
            WriteLedger(cfg, ledger, "revert: removed " + roots.Length + " root(s), SealProfile " + restored);
            return "revert " + key + ": removed " + roots.Length + " " + lay.root + " root(s), session SealProfile " + restored + "; scene saved (sha " + Short(before) + " -> " + Short(after) + ")"
                + (pristine != "" ? ", " + (after == pristine ? "byte-identical to the scene before the last apply" : "not byte-identical to the scene before the last apply (" + Short(pristine) + "): the pre-apply file is in " + applyBackup + ")") : "")
                + "; derived meshes, ink materials and WorldSealProfile308 are kept (git-ignored); backup " + ledger.backup;
        }

        // ---------------------------------------------------------------- preview (memory only)

        static string PreviewOn(Dictionary<string, string> opt)
        {
            var cfg = CliffCore308.LoadConfig(); var lay = LoadLayout();
            var scene = SceneManager.GetActiveScene();
            if (CliffCore308.SceneKey(cfg, scene.path) == null) throw new PostLedger308.Refused("the active scene '" + scene.path + "' is not a #308 target scene");
            if (Roots(scene, lay).Length > 0) throw new PostLedger308.Refused("the wall is already built in this scene (" + lay.root + " root): a preview would stand inside it");
            string summary = "";
            string head = WallLook308.OnExternal("by=jangseong308|layout=" + lay.Sha256.Substring(0, 12), opt != null && opt.ContainsKey("allowdirty"), root =>
            {
                var plan = Compute(lay, (CliffCore308.Ground)CliffCore308.TerrainRoot(scene, cfg));
                var notes = new List<string>(); var sink = new Sink(lay, cfg, plan.Key, true);
                BuildInto(root, lay, plan, sink, null, notes);
                var c = Measure(root, cfg.assetRoot + MeshSub);
                summary = PlanText(lay, plan) + "\npreview: renderers " + c.renderers + ", triangles " + c.triangles + ", batches " + c.batches + ", shadow casters " + c.shadowCasters + ", colliders " + c.colliders
                    + " (a preview has none), mesh memory " + F(c.meshMemoryMB) + " MB, in memory only" + (notes.Count > 0 ? "\nnote: " + string.Join("\nnote: ", notes) : "");
            });
            var stale = StaleInputs(lay);
            return summary + "\n" + head + (stale.Count > 0 ? "\nWARNING the layout is stale (" + string.Join(", ", stale) + "): the preview shows the old layout" : "");
        }
    }
}
