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

            // ---- the ground mark (shared by the five) ----
            var gShader = Shader.Find("Oheangbu/SpellFx/HexGround");
            if (gShader == null) return "REFUSED setup:h: shader Oheangbu/SpellFx/HexGround missing (" + log + ")";
            string gPath = HDir + "/M_U_H_ground.mat";
            var gMat = AssetDatabase.LoadAssetAtPath<Material>(gPath);
            if (gMat == null) { gMat = new Material(gShader); AssetDatabase.CreateAsset(gMat, gPath); }
            var gFresh = new Material(gShader);
            try { EditorUtility.CopySerialized(gFresh, gMat); } finally { UnityEngine.Object.DestroyImmediate(gFresh); }
            gMat.name = "M_U_H_ground";
            EditorUtility.SetDirty(gMat); AssetDatabase.SaveAssetIfDirty(gMat);

            var shader = Shader.Find("Oheangbu/SpellFx/HexDome");
            if (shader == null) return "REFUSED setup:h: shader Oheangbu/SpellFx/HexDome missing (" + log + ")";
            var built = new System.Collections.Generic.Dictionary<string, GameObject>();
            foreach (var b in Bodies)
            {
                // ---- atlas ----
                string atlasSrc = Path.Combine(src, b.Atlas);
                if (!File.Exists(atlasSrc)) { log.Append(b.Letter).Append(": no atlas ").Append(b.Atlas).Append(" (run u_h_build.py); "); continue; }
                string atlasAsset = HDir + "/" + b.Atlas;
                var bytes = File.ReadAllBytes(atlasSrc);
                if (!File.Exists(Full(atlasAsset)) || !File.ReadAllBytes(Full(atlasAsset)).SequenceEqual(bytes)) { File.WriteAllBytes(Full(atlasAsset), bytes); AssetDatabase.ImportAsset(atlasAsset, ImportAssetOptions.ForceSynchronousImport); }
                var imp = AssetImporter.GetAtPath(atlasAsset) as TextureImporter;
                if (imp != null && (imp.wrapMode != TextureWrapMode.Clamp || !imp.mipmapEnabled || imp.maxTextureSize != 2048))
                { imp.textureType = TextureImporterType.Default; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Clamp; imp.maxTextureSize = 2048; imp.filterMode = FilterMode.Trilinear; imp.SaveAndReimport(); }
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasAsset);

                // ---- material: the shader's defaults (the water look), then the body's colours ----
                string matPath = HDir + "/" + b.Mat + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
                var fresh = new Material(shader);
                try { EditorUtility.CopySerialized(fresh, mat); } finally { UnityEngine.Object.DestroyImmediate(fresh); }
                mat.name = b.Mat;
                mat.SetTexture("_Patterns", tex);
                foreach (var kv in b.Colours) mat.SetColor(kv.Key, kv.Value);
                EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);

                // ---- prefab ----
                var go = new GameObject(b.Prefab);
                try
                {
                    var player = go.AddComponent<SpellFxPlayer>();
                    var pso = new SerializedObject(player); pso.FindProperty("playSeconds").floatValue = 2.6f; pso.FindProperty("tailSeconds").floatValue = 0.4f; pso.ApplyModifiedPropertiesWithoutUndo();
                    var part = go.AddComponent<SpellFxHexDomePart>();
                    var so = new SerializedObject(part);
                    so.FindProperty("dome").objectReferenceValue = mesh;
                    so.FindProperty("material").objectReferenceValue = mat;
                    so.FindProperty("groundMaterial").objectReferenceValue = gMat;
                    so.FindProperty("burstBody").enumValueIndex = (int)b.Burst;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    built[b.Letter] = PrefabUtility.SaveAsPrefabAsset(go, Root + "/U/" + b.Prefab + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
                log.Append(b.Letter).Append(" prefab ok; ");
            }

            // ---- catalogue: MERGE - only <letter>/H of the five is replaced ----
            var cat = AssetDatabase.LoadAssetAtPath<SpellFxCatalog>(SpellFxCatalog.AssetPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SpellFxCatalog>(); AssetDatabase.CreateAsset(cat, SpellFxCatalog.AssetPath); }
            var list = (cat.entries ?? new SpellFxCatalog.Entry[0]).Where(e => e != null && !(e.variant == "H" && built.ContainsKey(e.letter))).ToList();
            foreach (var kv in built) list.Add(new SpellFxCatalog.Entry { letter = kv.Key, variant = "H", prefab = kv.Value });
            cat.entries = list.ToArray();
            EditorUtility.SetDirty(cat); AssetDatabase.SaveAssetIfDirty(cat);
            log.Append("catalogue: " + string.Join(", ", cat.entries.Select(e => e.letter + "/" + e.variant)));
            return log.ToString();
        }

        /// <summary>2026-10-30 (the user: "'ㅜ' 술식 5개 전부 우와 같은 종류의 술식으로 구현해줘"): the five 무받침 ㅜ wards (D308-156) share the hexagon dome; each has its
        /// body's patterns (u_h_build.py BODY_PATTERNS), colours and what a taken shot turns into. 우 keeps the water look the rounds tuned (the shader's defaults).</summary>
        sealed class BodySpec
        {
            public string Letter, Atlas, Mat, Prefab; public SpellFxBurstBody Burst;
            public System.Collections.Generic.Dictionary<string, Color> Colours = new System.Collections.Generic.Dictionary<string, Color>();
        }

        static BodySpec Body(string letter, string atlas, string mat, string prefab, SpellFxBurstBody burst, Color glass, Color rim, Color line, Color pattern, Color accent, Color lineInk, Color imp)
        {
            var b = new BodySpec { Letter = letter, Atlas = atlas, Mat = mat, Prefab = prefab, Burst = burst };
            b.Colours["_Glass"] = glass; b.Colours["_Rim"] = rim; b.Colours["_Line"] = line; b.Colours["_Pattern"] = pattern;
            b.Colours["_Accent"] = accent; b.Colours["_LineInk"] = lineInk; b.Colours["_ImpCol"] = imp;
            return b;
        }

        static readonly BodySpec[] Bodies =
        {
            new BodySpec { Letter = "우", Atlas = "h_patterns.png", Mat = "M_U_H", Prefab = "U_H", Burst = SpellFxBurstBody.Water },
            Body("구", "h_patterns_wood.png", "M_U_H_wood", "U_H_wood", SpellFxBurstBody.Wood,
                new Color(0.16f, 0.36f, 0.24f, 0.125f), new Color(0.55f, 0.85f, 0.6f, 0.08f), new Color(1.15f, 1.32f, 1.05f, 1f), new Color(0.9f, 1f, 0.85f, 0.85f),
                new Color(0.6f, 1.5f, 0.55f, 1f), new Color(0.3f, 0.38f, 0.3f, 1f), new Color(0.14f, 0.24f, 0.08f, 1f)),
            Body("누", "h_patterns_fire.png", "M_U_H_fire", "U_H_fire", SpellFxBurstBody.Fire,
                new Color(0.5f, 0.2f, 0.1f, 0.13f), new Color(1f, 0.6f, 0.35f, 0.08f), new Color(1.45f, 1.12f, 0.85f, 1f), new Color(1f, 0.88f, 0.75f, 0.85f),
                new Color(1.8f, 0.75f, 0.25f, 1f), new Color(0.42f, 0.3f, 0.25f, 1f), new Color(0.08f, 0.06f, 0.05f, 1f)),
            Body("무", "h_patterns_earth.png", "M_U_H_earth", "U_H_earth", SpellFxBurstBody.Earth,
                new Color(0.42f, 0.32f, 0.18f, 0.14f), new Color(0.85f, 0.7f, 0.45f, 0.08f), new Color(1.35f, 1.2f, 0.95f, 1f), new Color(0.98f, 0.9f, 0.75f, 0.85f),
                new Color(1.5f, 1.15f, 0.5f, 1f), new Color(0.4f, 0.34f, 0.26f, 1f), new Color(0.3f, 0.2f, 0.12f, 1f)),
            Body("수", "h_patterns_metal.png", "M_U_H_metal", "U_H_metal", SpellFxBurstBody.Metal,
                new Color(0.5f, 0.53f, 0.58f, 0.12f), new Color(0.85f, 0.88f, 0.95f, 0.08f), new Color(1.4f, 1.42f, 1.5f, 1f), new Color(0.95f, 0.96f, 1f, 0.85f),
                new Color(1.5f, 1.55f, 1.7f, 1f), new Color(0.38f, 0.39f, 0.42f, 1f), new Color(0.35f, 0.36f, 0.4f, 1f)),
        };
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
