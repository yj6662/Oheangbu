using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 cliff boundary 1b, package "pieces" (SPEC-WORLD-CLIFF-BOUNDARY-308 design 8, D308-9c Q4, D308-9e): what CliffGuk308 and
    // SmallOps308 share. Data lives in Art/World/Compact/Rebuild/CliffBoundary308/small_1b/ (written by Tools/Art/small308.py); ledgers and
    // reports in .../CliffBoundary308/Out/. Nothing here opens a dialog, calls AssetDatabase.SaveAssets or writes a protected file.
    internal static class Pieces308
    {
        internal const string DataDir = "Art/World/Compact/Rebuild/CliffBoundary308/small_1b";
        internal const string OutDir = "Art/World/Compact/Rebuild/CliffBoundary308/Out";
        internal const string BackupDir = "Art/World/Compact/Rebuild/CliffBoundary308/Out/Backup/small308";
        internal const string OpsConfigFile = DataDir + "/smallops308.cfg.json";   // the small ledger ops; the UP-1 pieces read guk308.json
        internal static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal static string F(float v, string format = "F2") => v.ToString(format, Inv);
        internal static string V(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";
        internal static string Short(string sha) => string.IsNullOrEmpty(sha) ? "-" : sha.Substring(0, Math.Min(12, sha.Length));

        // ---------------------------------------------------------------- data (JSON; arrays of arrays, so Newtonsoft)

        internal static JObject Json(string repoRelative, out string sha)
        {
            string abs = PostLedger308.RepoPath(repoRelative);
            if (!File.Exists(abs)) throw new PostLedger308.Refused("data missing: " + repoRelative + " (run `python Tools/Art/small308.py all`)");
            var bytes = File.ReadAllBytes(abs); sha = CliffCore308.Sha(bytes);
            try { return JObject.Parse(new UTF8Encoding(false).GetString(bytes).TrimStart('﻿')); }
            catch (Exception e) { throw new PostLedger308.Refused(repoRelative + " does not parse: " + e.Message); }
        }
        internal static JToken Req(JToken o, string key)
        {
            var t = o?[key];
            if (t == null || t.Type == JTokenType.Null) throw new PostLedger308.Refused("data: missing '" + key + "' in " + (o == null ? "(null)" : string.IsNullOrEmpty(o.Path) ? "(root)" : o.Path));
            return t;
        }
        internal static float Num(JToken o, string key) => Req(o, key).Value<float>();
        internal static string Str(JToken o, string key) => Req(o, key).Value<string>();
        internal static string Opt(JToken o, string key) { var t = o?[key]; return t == null || t.Type == JTokenType.Null ? "" : t.Value<string>(); }
        internal static bool Flag(JToken o, string key) { var t = o?[key]; return t != null && t.Type == JTokenType.Boolean && t.Value<bool>(); }
        internal static IEnumerable<JToken> Arr(JToken o, string key) => o?[key] as JArray ?? (IEnumerable<JToken>)Array.Empty<JToken>();
        internal static float At(JToken array, int i)
        {
            if (!(array is JArray a) || a.Count <= i) throw new PostLedger308.Refused("data: " + (array == null ? "(null)" : array.Path) + " needs " + (i + 1) + " numbers");
            return a[i].Value<float>();
        }
        internal static Vector3 V3(JToken array) => new Vector3(At(array, 0), At(array, 1), At(array, 2));

        // ---------------------------------------------------------------- guards

        /// <summary>Edit mode, not compiling, no dirty scene, a #308 target scene outside the protected paths, and no look preview up
        /// (a preview switches real renderers off until its own preview:off: a scene save in that state would write them).</summary>
        internal static Scene OpenTarget(string scenePath, JToken previewRoots, bool write)
        {
            PostLedger308.RequireEditable();
            if (!PostLedger308.Scenes.Contains(scenePath)) throw new PostLedger308.Refused("not a #308 target scene: " + scenePath);
            if (PostLedger308.IsProtectedPath(scenePath)) throw new PostLedger308.Refused("protected path " + scenePath);
            var names = new HashSet<string>((previewRoots as JArray ?? new JArray()).Select(t => t.Value<string>()));
            if (names.Count == 0) throw new PostLedger308.Refused("data: 'preview_roots' must name the look preview roots (CliffLook308_Preview, WallLook308_Preview)");
            var up = Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && !EditorUtility.IsPersistent(g) && g.transform.parent == null && names.Contains(g.name)).Select(g => g.name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var active = SceneManager.GetActiveScene(); bool switches = !(active.path == scenePath && SceneManager.sceneCount == 1);
            if (up.Length > 0 && (write || switches))
                throw new PostLedger308.Refused("a look preview is up (" + string.Join(", ", up) + "): run that tool's preview:off first" + (write ? " (a save now would write the renderers it switched off)" : " (opening another scene would orphan its restore list)"));
            return PostLedger308.Open(scenePath);
        }

        internal static bool Saved(Transform t) { for (var p = t; p != null; p = p.parent) if ((p.gameObject.hideFlags & HideFlags.DontSave) != 0) return false; return true; }

        /// <summary>Every saved transform whose hierarchy path equals `path` (names are split on '/').</summary>
        internal static List<Transform> FindAll(Scene scene, string path)
        {
            var list = new List<Transform>(); var parts = (path ?? "").Split('/'); if (parts.Length == 0 || parts[0].Length == 0) return list;
            void Walk(Transform t, int next)
            {
                if (next >= parts.Length) { list.Add(t); return; }
                foreach (Transform c in t) if (c.name == parts[next]) Walk(c, next + 1);
            }
            foreach (var root in scene.GetRootGameObjects()) if (root.name == parts[0] && Saved(root.transform)) Walk(root.transform, 1);
            return list;
        }
        internal static Transform FindOne(Scene scene, string path, string what)
        {
            var hit = FindAll(scene, path);
            if (hit.Count != 1) throw new PostLedger308.Refused(what + ": " + hit.Count + " objects at " + path + " (expected 1)");
            return hit[0];
        }

        // ---------------------------------------------------------------- files, ledgers, reports

        internal static string OutFile(string name) { string dir = PostLedger308.RepoPath(OutDir); Directory.CreateDirectory(dir); return Path.Combine(dir, name).Replace('\\', '/'); }
        internal static string ShaAsset(string assetPath) => CliffCore308.ShaFile(PostLedger308.Abs(assetPath));
        internal static T ReadLedger<T>(string name) where T : class
        {
            string f = OutFile(name);
            return File.Exists(f) ? JsonUtility.FromJson<T>(File.ReadAllText(f, Encoding.UTF8)) : null;
        }
        internal static void WriteLedger(string name, object ledger) => CliffCore308.WriteText(OutFile(name), JsonUtility.ToJson(ledger, true));
        internal static string Archive(string name)
        {
            string f = OutFile(name), to = f + ".reverted-" + PostLedger308.Utc();
            if (File.Exists(f)) File.Move(f, to);
            return to;
        }
        internal static string Report(string name, StringBuilder sb)
        {
            string f = OutFile(name); CliffCore308.WriteText(f, sb.ToString().TrimEnd());
            return sb.ToString().TrimEnd() + "\nreport " + f;
        }
        /// <summary>Byte copy of a scene / asset (and its .meta) before a write; returns the repo-relative folder.</summary>
        internal static string Backup(string utc, params string[] assetPaths)
        {
            string dir = PostLedger308.RepoPath(BackupDir);
            foreach (string p in assetPaths)
            {
                if (PostLedger308.IsProtectedPath(p)) throw new PostLedger308.Refused("protected path " + p);
                PostLedger308.Backup(dir, utc, p);
            }
            return BackupDir + "/" + utc;
        }
        /// <summary>Saves the scene or reloads it from disk (nothing half-applied stays in memory).</summary>
        internal static void SaveOrReload(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (EditorSceneManager.SaveScene(scene)) return;
            EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
            throw new PostLedger308.Refused("SaveScene returned false for " + scene.path + " (scene reloaded from disk, nothing saved)");
        }
        internal static void Reload(Scene scene) => EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);

        // ---------------------------------------------------------------- physics ground (what a node or a capsule stands on)

        /// <summary>Colliders that are never ground: the player body, actors, look previews, the enclosure / seal shells and `own`.</summary>
        internal sealed class GroundRule
        {
            public readonly HashSet<Collider> Skip = new HashSet<Collider>(); public readonly List<Transform> Ignore = new List<Transform>();
            public GroundRule(Scene scene, params Transform[] own)
            {
                var session = CliffCore308.Session(scene);
                if (session != null)
                {
                    foreach (var a in session.Actors ?? Array.Empty<Oheangbu.App.Prologue.PrologueEncounter>()) if (a != null) foreach (var c in a.GetComponentsInChildren<Collider>(true)) Skip.Add(c);
                    if (session.Walker != null && session.Walker.Body != null) foreach (var c in session.Walker.Body.GetComponentsInChildren<Collider>(true)) Skip.Add(c);
                }
                foreach (var g in scene.GetRootGameObjects()) if (g.name == "Enclosure305" || g.name == "Seal308") Ignore.Add(g.transform);
                foreach (var t in own) if (t != null) Ignore.Add(t);
                Physics.SyncTransforms();
            }
            public bool Usable(RaycastHit h, float minNormalY = .5f)
            {
                var c = h.collider; if (c == null || h.normal.y < minNormalY || c is CharacterController || Skip.Contains(c) || !Saved(c.transform)) return false;
                foreach (var r in Ignore) if (r != null && c.transform.IsChildOf(r)) return false;
                return true;
            }
        }
        static bool IsTerrain(Collider c)
        {
            if (c is TerrainCollider) return true;
            for (var t = c.transform; t != null; t = t.parent) if (t.name.Contains("Terrain") || t.name.Contains("Surface")) return true;
            return false;
        }
        /// <summary>The walkable support at (x, z): the terrain, or the highest walkable hit within `window` over it (decks, slabs,
        /// the village ground mesh). Same rule as Content308.Ground308.</summary>
        internal static bool Ground(GroundRule rule, float x, float z, float window, out RaycastHit best)
        {
            best = default; float baseY = float.NegativeInfinity;
            var hits = Physics.RaycastAll(new Vector3(x, 2000f, z), Vector3.down, 4000f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits) if (rule.Usable(h) && IsTerrain(h.collider) && h.point.y > baseY) baseY = h.point.y;
            if (float.IsNegativeInfinity(baseY)) foreach (var h in hits) if (rule.Usable(h) && h.point.y > baseY) baseY = h.point.y;
            if (float.IsNegativeInfinity(baseY)) return false;
            bool any = false;
            foreach (var h in hits) { if (!rule.Usable(h) || h.point.y < baseY - .05f || h.point.y > baseY + window) continue; if (!any || h.point.y > best.point.y) { best = h; any = true; } }
            return any;
        }

        // ---------------------------------------------------------------- generated mesh assets (git-ignored folder, shared by the three scenes)

        /// <summary>Creates the mesh asset, or keeps the one already there when its content is the same. Only this asset is saved.</summary>
        internal static Mesh SaveMesh(Mesh built, string assetPath, out bool created)
        {
            created = false;
            if (!assetPath.StartsWith("Assets/_Project/Art/World/CliffBoundary308/", StringComparison.Ordinal) || PostLedger308.IsProtectedPath(assetPath))
            { Object.DestroyImmediate(built); throw new PostLedger308.Refused("refusing to write a mesh at " + assetPath); }
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null)
            {
                bool same = CliffCore308.MeshContentSha(existing) == CliffCore308.MeshContentSha(built);
                Object.DestroyImmediate(built);
                if (!same) throw new PostLedger308.Refused("another mesh already sits at " + assetPath + " with different content (the name carries the data hash: this should not happen; delete it by hand after checking no scene uses it)");
                return existing;
            }
            DevSceneKit.EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            built.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(built, assetPath); AssetDatabase.SaveAssetIfDirty(built); created = true;
            return built;
        }
    }
}
