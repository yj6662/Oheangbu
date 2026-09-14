using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World.Dressing;
using Object = UnityEngine.Object;
using Sheet = Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroDressingAssets
    {
        [Serializable] public sealed class ShrubLodRow
        {
            public string prototype, source, sourceHashBefore, sourceHashAfter, atlas;
            public int[] trianglesBefore, trianglesAfter;
            public int sourceLodGroups, sourceLodLevels, atlasViews;
            public bool nearLevelPreserved, middleLevelPreserved, sourcePreserved, passed;
            public string scope = "Original close and middle levels retained. Owned four-view card appended; rendering transitions and GPU cost require separate runtime measurement.";
        }

        public static bool IsHeavyShrub(Sheet.Prototype p) => p.Category == Sheet.Kind.Shrub
            && (p.SourcePath == "Assets/HwaseongHaenggung/Prefabs/SM_Bush.prefab"
                || p.SourcePath == "Assets/YongmeoriCoast/Prefabs/SM_YMC_Bush_01_Small.prefab");

        static int[] ShrubTriangles(Sheet.Prototype p) => p.Lods.Select(l => l.Parts.Sum(part =>
            part.Mesh == null ? 0 : (int)part.Mesh.GetIndexCount(part.Submesh) / 3)).ToArray();

        public static ShrubLodRow AppendHeavyShrubCard(Sheet sheet, Sheet.Prototype p)
        {
            if (!IsHeavyShrub(p)) throw new ArgumentException("Only the two verified heavy shrub sources are supported.");
            if (p.Lods.Length < 2 || p.Lods[0].Parts.Length == 0) throw new InvalidDataException("Missing original shrub levels: " + p.Id);
            string prior = scopedFolder; scopedFolder = PolishFolder;
            try
            {
                GuardPolish();
                var row = new ShrubLodRow { prototype = p.Id, source = p.SourcePath, trianglesBefore = ShrubTriangles(p),
                    sourceHashBefore = AssetDatabase.GetAssetDependencyHash(p.SourcePath).ToString(), atlasViews = 4 };
                if (row.sourceHashBefore != p.SourceHash) throw new InvalidDataException("Supplier source changed: " + p.SourcePath);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p.SourcePath);
                if (prefab == null) throw new FileNotFoundException(p.SourcePath);
                var groups = prefab.GetComponentsInChildren<LODGroup>(true);
                row.sourceLodGroups = groups.Length;
                row.sourceLodLevels = groups.Length == 0 ? 1 : groups.Sum(g => g.GetLODs().Length);
                // Both inspected prefabs have one mesh and no genuine lower LOD. A future supplier
                // revision must be reviewed explicitly rather than silently ignoring real LODs.
                if (groups.Length != 0) throw new InvalidDataException("Source gained genuine LODs; review them before card authoring: " + p.SourcePath);
                var near = p.Lods[0]; var middle = p.Lods[1];
                if (near.Parts.Any(part => part.Mesh == null || part.Material == null || part.Material.GetTexture("_BaseMap") == null))
                    throw new InvalidDataException("Run refreshplants first; cannot bake a shrub with missing source color: " + p.Id);
                foreach (var part in near.Parts) RequireOwnedGrassMaterial(part.Material);
                // Texture-sensitive identity prevents reuse of an atlas baked before material repair.
                string maps = string.Join("|", near.Parts.Select(part =>
                    AssetDatabase.GetAssetPath(part.Material.GetTexture("_BaseMap")) + ":" +
                    AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(part.Material.GetTexture("_BaseMap"))) + ":" +
                    part.Material.GetFloat("_AlphaClip") + ":" + part.Material.GetFloat("_Cutoff")));
                string atlasName = AssetDatabase.AssetPathToGUID(p.SourcePath) + "_Shrub4_" + Hash128.Compute(maps).ToString();
                float span = Mathf.Max(p.Size.x, Mathf.Max(p.Size.y, p.Size.z)) * 1.08f;
                var texture = DirectionalAtlas(atlasName, near, span, 4, false);
                var material = CardMaterial(p, "_ShrubDirectional", texture, true, 4);
                // The atlas stores untinted source RGB. Preserve the near material's regional tint;
                // avoid the bright white-card replacement that prompted the earlier texture repair.
                var dominant = near.Parts.OrderByDescending(part => part.Mesh.GetIndexCount(part.Submesh)).First().Material;
                material.SetColor("_BaseColor", dominant.GetColor("_BaseColor"));
                material.SetFloat("_Saturation", dominant.GetFloat("_Saturation"));
                material.SetFloat("_AmbientFloor", .72f); material.SetFloat("_LightResponse", .1f);
                material.SetFloat("_Cutoff", .333f);
                SetRange(material, 19f, 29f, sheet.ShrubDistance - 20f, sheet.ShrubDistance);
                EditorUtility.SetDirty(material);
                p.Lods = new[] { near, middle, new Sheet.Level { Parts = new[] {
                    new Sheet.Part { Mesh = Quad(), Material = material, Submesh = 0,
                        Local = Matrix4x4.Scale(new Vector3(span, span, 1f)) } } } };
                p.BillboardViews = 4;
                row.atlas = AssetDatabase.GetAssetPath(texture); row.trianglesAfter = ShrubTriangles(p);
                row.nearLevelPreserved = ReferenceEquals(near, p.Lods[0]);
                row.middleLevelPreserved = ReferenceEquals(middle, p.Lods[1]);
                row.sourceHashAfter = AssetDatabase.GetAssetDependencyHash(p.SourcePath).ToString();
                row.sourcePreserved = row.sourceHashAfter == row.sourceHashBefore;
                row.passed = row.nearLevelPreserved && row.middleLevelPreserved && row.sourcePreserved
                    && row.trianglesAfter[2] == 2 && texture != null && p.Lods[2].Parts[0].Material.enableInstancing;
                if (!row.passed) throw new InvalidDataException("Shrub card authoring invariant failed: " + p.Id);
                EditorUtility.SetDirty(sheet); AssetDatabase.SaveAssets();
                return row;
            }
            finally { scopedFolder = prior; }
        }
    }

    public static partial class WorldMacroVegetationPolish
    {
        [Serializable] sealed class ShrubLodReport
        {
            public string utc, baseline;
            public bool passed;
            public WorldMacroDressingAssets.ShrubLodRow[] rows;
            public string scope = "Two original heavy shrub species, four existing regional prototypes. Existing close/middle meshes, IDs, placement, density, source textures retained. Owned four-view cards appended. No terrain regeneration, gameplay, screenshot or build. Runtime transition/color/GPU tests remain unverified.";
        }
        static IEnumerator<string> OptimizeShrubLods(Sheet sheet)
        {
            string dir = Output + "/Baseline/ShrubLod"; Directory.CreateDirectory(dir);
            string baseline = dir + "/Dressing_BeforeShrubCards.asset";
            if (!File.Exists(baseline)) File.Copy(SheetPath, baseline);
            var rows = new List<WorldMacroDressingAssets.ShrubLodRow>();
            foreach (var p in sheet.Prototypes.Where(WorldMacroDressingAssets.IsHeavyShrub))
            {
                rows.Add(WorldMacroDressingAssets.AppendHeavyShrubCard(sheet, p));
                File.WriteAllText(Output + "/shrub_lod.json", JsonUtility.ToJson(new ShrubLodReport {
                    utc = DateTime.UtcNow.ToString("O"), baseline = baseline, passed = false, rows = rows.ToArray() }, true));
                yield return "Original shrub + four-view card saved: " + p.Id;
            }
            var renderer = Object.FindFirstObjectByType<WorldMacroDressingRenderer>();
            if (renderer != null && renderer.Sheet == sheet) renderer.ResetCache();
            bool passed = rows.Count == 4 && rows.All(r => r.passed);
            File.WriteAllText(Output + "/shrub_lod.json", JsonUtility.ToJson(new ShrubLodReport {
                utc = DateTime.UtcNow.ToString("O"), baseline = baseline, passed = passed, rows = rows.ToArray() }, true));
            if (!passed) throw new InvalidDataException("Expected the four existing regional shrub prototypes.");
            yield return "Shrub card assets ready; runtime selects mesh/card transitions. Density and positions unchanged.";
        }
    }
}
