using System;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellFx;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>D308-153 variant 우/H (2026-10-29, the user: "반투명 홀로그램 육각형 막에 문양이 박혀있는 모습" -> "돔 + 다양한 문양"): the hexagon-cell dome.
    /// Reads the repo's Art/SpellVFX308/Restart/u_H/h_dome.json + h_patterns.png (Tools/Art/SpellVFX308/restart/u_h_build.py) and builds, under Restart/U/H:
    /// u_h_dome.asset (the Mesh: uv0 pattern uv, uv1 hash/edge, uv2 pattern/height), h_patterns.png (imported linear-free sRGB, mips, clamp), M_U_H.mat (Oheangbu/SpellFx/HexDome),
    /// Restart/U/U_H.prefab = SpellFxPlayer + SpellFxHexDomePart, and merges 우/H into the catalogue (only that entry is replaced).
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupHCommands Run "setup:h"
    /// No dialogs. Saves only what it writes.</summary>
    public static class SpellFxSetupHCommands
    {
        const string Root = "Assets/_Project/Art/SpellVFX308/Restart";
        const string HDir = Root + "/U/H";

        public static string Run(string command)
        {
            try
            {
                var c = (command ?? "").Trim();
                if (!string.Equals(c, "setup:h", StringComparison.OrdinalIgnoreCase)) return "REFUSED setup: setup:h";
                return SetupH();
            }
            catch (Exception e) { return "EXCEPTION " + e.GetType().Name + " " + e.Message; }
        }

        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        static string RepoRoot => Directory.GetParent(ProjectRoot).FullName;
        static string Full(string assetPath) => Path.Combine(ProjectRoot, assetPath);

        static string SetupH()
        {
            var log = new StringBuilder();
            string src = Path.Combine(RepoRoot, "Art", "SpellVFX308", "Restart", "u_H");
            string json = Path.Combine(src, "h_dome.json"), atlas = Path.Combine(src, "h_patterns.png");
            if (!File.Exists(json) || !File.Exists(atlas)) return "REFUSED setup:h: run Tools/Art/SpellVFX308/restart/u_h_build.py first (" + src + ")";
            Directory.CreateDirectory(Full(HDir));

            // ---- mesh ----
            var d = Parse(File.ReadAllText(json));
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(HDir + "/u_h_dome.asset");
            bool newMesh = mesh == null;
            if (newMesh) mesh = new Mesh { name = "u_h_dome" };
            mesh.Clear();
            mesh.SetVertices(d.Pos); mesh.SetNormals(d.Nrm);
            mesh.SetUVs(0, d.Uv0); mesh.SetUVs(1, d.Uv1); mesh.SetUVs(2, d.Uv2); mesh.SetUVs(3, d.Ctr);
            mesh.SetTriangles(d.Tris, 0);
            mesh.RecalculateBounds();
            if (newMesh) AssetDatabase.CreateAsset(mesh, HDir + "/u_h_dome.asset"); else EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            log.Append("mesh verts ").Append(d.Pos.Length).Append(" tris ").Append(d.Tris.Length / 3).Append("; ");

            // ---- atlas ----
            string atlasAsset = HDir + "/h_patterns.png";
            var bytes = File.ReadAllBytes(atlas);
            if (!File.Exists(Full(atlasAsset)) || !File.ReadAllBytes(Full(atlasAsset)).SequenceEqual(bytes)) { File.WriteAllBytes(Full(atlasAsset), bytes); AssetDatabase.ImportAsset(atlasAsset, ImportAssetOptions.ForceSynchronousImport); }
            var imp = AssetImporter.GetAtPath(atlasAsset) as TextureImporter;
            if (imp != null && (imp.wrapMode != TextureWrapMode.Clamp || !imp.mipmapEnabled || imp.maxTextureSize != 2048))
            { imp.textureType = TextureImporterType.Default; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Clamp; imp.maxTextureSize = 2048; imp.filterMode = FilterMode.Trilinear; imp.SaveAndReimport(); }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasAsset);

            // ---- material ----
            var shader = Shader.Find("Oheangbu/SpellFx/HexDome");
            if (shader == null) return "REFUSED setup:h: shader Oheangbu/SpellFx/HexDome missing (" + log + ")";
            string matPath = HDir + "/M_U_H.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
            var fresh = new Material(shader);                    // every run resets the tunables to the shader's defaults (the shader file is the one place they live)
            try { EditorUtility.CopySerialized(fresh, mat); } finally { UnityEngine.Object.DestroyImmediate(fresh); }
            mat.name = "M_U_H";
            mat.SetTexture("_Patterns", tex);
            EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);
            // round 9: the ground mark
            var gShader = Shader.Find("Oheangbu/SpellFx/HexGround");
            if (gShader == null) return "REFUSED setup:h: shader Oheangbu/SpellFx/HexGround missing (" + log + ")";
            string gPath = HDir + "/M_U_H_ground.mat";
            var gMat = AssetDatabase.LoadAssetAtPath<Material>(gPath);
            if (gMat == null) { gMat = new Material(gShader); AssetDatabase.CreateAsset(gMat, gPath); }
            var gFresh = new Material(gShader);
            try { EditorUtility.CopySerialized(gFresh, gMat); } finally { UnityEngine.Object.DestroyImmediate(gFresh); }
            gMat.name = "M_U_H_ground";
            EditorUtility.SetDirty(gMat); AssetDatabase.SaveAssetIfDirty(gMat);

            // ---- prefab ----
            var go = new GameObject("U_H");
            GameObject prefab;
            try
            {
                var player = go.AddComponent<SpellFxPlayer>();
                var pso = new SerializedObject(player); pso.FindProperty("playSeconds").floatValue = 2.6f; pso.FindProperty("tailSeconds").floatValue = 0.4f; pso.ApplyModifiedPropertiesWithoutUndo();
                var part = go.AddComponent<SpellFxHexDomePart>();
                var so = new SerializedObject(part);
                so.FindProperty("dome").objectReferenceValue = mesh;
                so.FindProperty("material").objectReferenceValue = mat;
                so.FindProperty("groundMaterial").objectReferenceValue = gMat;
                so.ApplyModifiedPropertiesWithoutUndo();
                prefab = PrefabUtility.SaveAsPrefabAsset(go, Root + "/U/U_H.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            log.Append("prefab ok; ");

            // ---- catalogue: MERGE - only 우/H is replaced ----
            var cat = AssetDatabase.LoadAssetAtPath<SpellFxCatalog>(SpellFxCatalog.AssetPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SpellFxCatalog>(); AssetDatabase.CreateAsset(cat, SpellFxCatalog.AssetPath); }
            var list = (cat.entries ?? new SpellFxCatalog.Entry[0]).Where(e => e != null && !(e.letter == "우" && e.variant == "H")).ToList();
            list.Add(new SpellFxCatalog.Entry { letter = "우", variant = "H", prefab = prefab });
            cat.entries = list.ToArray();
            EditorUtility.SetDirty(cat); AssetDatabase.SaveAssetIfDirty(cat);
            log.Append("catalogue: " + string.Join(", ", cat.entries.Select(e => e.letter + "/" + e.variant)));
            return log.ToString();
        }

        sealed class Dome { public Vector3[] Pos, Nrm, Ctr; public Vector2[] Uv0, Uv1, Uv2; public int[] Tris; }

        /// <summary>The json is flat arrays of numbers (written by u_h_build.py): a tiny reader for "key": [[a, b, c], ...] / [i, j, ...] is enough.</summary>
        static Dome Parse(string text)
        {
            return new Dome
            {
                Pos = Rows(text, "pos", 3).Select(r => new Vector3(r[0], r[1], r[2])).ToArray(),
                Nrm = Rows(text, "nrm", 3).Select(r => new Vector3(r[0], r[1], r[2])).ToArray(),
                Uv0 = Rows(text, "uv0", 2).Select(r => new Vector2(r[0], r[1])).ToArray(),
                Uv1 = Rows(text, "uv1", 2).Select(r => new Vector2(r[0], r[1])).ToArray(),
                Uv2 = Rows(text, "uv2", 2).Select(r => new Vector2(r[0], r[1])).ToArray(),
                Ctr = Rows(text, "ctr", 3).Select(r => new Vector3(r[0], r[1], r[2])).ToArray(),
                Tris = Numbers(text, "tris").Select(v => (int)v).ToArray(),
            };
        }

        static float[] Numbers(string text, string key)
        {
            int at = text.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
            if (at < 0) throw new Exception("h_dome.json: no " + key);
            int open = text.IndexOf('[', at), depth = 0, end = open;
            for (int i = open; i < text.Length; i++) { if (text[i] == '[') depth++; else if (text[i] == ']' && --depth == 0) { end = i; break; } }
            var body = text.Substring(open, end - open + 1).Replace("[", " ").Replace("]", " ");
            return body.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        }

        static float[][] Rows(string text, string key, int n)
        {
            var flat = Numbers(text, key);
            var rows = new float[flat.Length / n][];
            for (int i = 0; i < rows.Length; i++) { rows[i] = new float[n]; Array.Copy(flat, i * n, rows[i], 0, n); }
            return rows;
        }
    }
}
