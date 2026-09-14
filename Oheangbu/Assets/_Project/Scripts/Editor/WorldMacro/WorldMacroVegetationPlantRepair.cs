using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroDressingAssets
    {
        // These are the verified property semantics of the owned Yongmeori foliage shaders.
        // Other Unreal materials are not guessed by numeric property suffix.
        static bool IsYmcFoliageMaterial(Material source)
        {
            string path=AssetDatabase.GetAssetPath(source);
            return path.StartsWith("Assets/YongmeoriCoast/Material/Foliage/",StringComparison.Ordinal)
                && source.shader!=null&&source.shader.name.StartsWith("Unreal/MI_YMC_",StringComparison.Ordinal)
                && source.HasProperty("Material_Texture2D_0")&&source.HasProperty("Material_Texture2D_1")&&source.HasProperty("Material_Texture2D_3");
        }
        static bool CopyYmcFoliageTextures(Material source,Material target,int maximum)
        {
            if(Folder!=PolishFolder||!IsYmcFoliageMaterial(source))return false;
            var color=source.GetTexture("Material_Texture2D_1");var normal=source.GetTexture("Material_Texture2D_0");var opacity=source.GetTexture("Material_Texture2D_3");
            if(color==null||normal==null||opacity==null)throw new InvalidDataException("Missing supplier foliage map: "+AssetDatabase.GetAssetPath(source));
            target.SetTexture("_BaseMap",PackFoliageColorOpacity(source,maximum));
            target.SetTextureScale("_BaseMap",source.GetTextureScale("Material_Texture2D_1"));target.SetTextureOffset("_BaseMap",source.GetTextureOffset("Material_Texture2D_1"));
            target.SetTexture("_BumpMap",TextureCopy(normal,maximum,true));
            target.SetFloat("_BumpScale",1);target.SetFloat("_AlphaClip",1);target.SetFloat("_Cutoff",.333f);target.SetFloat("_Cull",0);
            return true;
        }
        static Texture2D PackFoliageColorOpacity(Material source,int maximum)
        {
            var colorSource=source.GetTexture("Material_Texture2D_1");var opacitySource=source.GetTexture("Material_Texture2D_3");
            string sourceKey=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            string path=Folder+"/Textures/"+sourceKey+"_FoliageRGBA_"+maximum+"_v1.png";
            var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(existing!=null)return existing;
            GuardPolish();Texture2D color=null,opacity=null,packed=null;
            try
            {
                color=new Texture2D(2,2,TextureFormat.RGBA32,false);opacity=new Texture2D(2,2,TextureFormat.RGBA32,false);
                if(!color.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(colorSource)))||!opacity.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(opacitySource))))throw new InvalidDataException("Cannot decode supplier foliage PNGs.");
                if(color.width!=opacity.width||color.height!=opacity.height)throw new InvalidDataException("Foliage color/opacity dimensions differ: "+source.name);
                var rgb=color.GetPixels32();var mask=opacity.GetPixels32();int foreground=0;
                for(int i=0;i<rgb.Length;i++){rgb[i].a=mask[i].r;if(mask[i].r>=85)foreground++;}
                // A branch map may legitimately be fully opaque; never require holes or invent them.
                if(foreground==0)throw new InvalidDataException("Supplier foliage opacity rejects the entire surface: "+source.name);
                packed=new Texture2D(color.width,color.height,TextureFormat.RGBA32,false);packed.SetPixels32(rgb);packed.Apply(false);File.WriteAllBytes(path,packed.EncodeToPNG());
            }
            finally{if(color!=null)Object.DestroyImmediate(color);if(opacity!=null)Object.DestroyImmediate(opacity);if(packed!=null)Object.DestroyImmediate(packed);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.alphaIsTransparency=true;importer.mipmapEnabled=true;importer.isReadable=false;importer.maxTextureSize=maximum;importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        [Serializable] public sealed class PlantRepairRow
        {public string prototype,sourceMaterial,targetMaterial,colorSource,normalSource,opacitySource,packedColor;public int lod,part;public bool sourcePreserved,passed;}
        public static PlantRepairRow[] RepairMissingPlantMaterials(Sheet sheet,Sheet.Prototype p)
        {
            string prior=scopedFolder;scopedFolder=PolishFolder;
            try
            {
                var rows=new List<PlantRepairRow>();
                if(!p.Lods.Any(l=>l.Parts.Any(part=>part.Material!=null&&part.Material.GetTexture("_BaseMap")==null)))return rows.ToArray();
                GuardPolish();var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(p.SourcePath);if(prefab==null)throw new FileNotFoundException(p.SourcePath);
                var group=prefab.GetComponent<LODGroup>();var levels=group!=null?group.GetLODs():new[]{new LOD(0,prefab.GetComponentsInChildren<Renderer>(true))};
                for(int lod=0;lod<p.Lods.Length;lod++)
                {
                    if(!p.Lods[lod].Parts.Any(part=>part.Material!=null&&part.Material.GetTexture("_BaseMap")==null))continue;
                    int sourceLod=p.Category==Sheet.Kind.Tree?lod+2:p.Category==Sheet.Kind.Grass?lod:lod+1;
                    var sources=new List<Material>();
                    foreach(var renderer in levels[Mathf.Min(sourceLod,levels.Length-1)].renderers)
                    {var mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;if(mesh==null)continue;for(int sub=0;sub<mesh.subMeshCount;sub++)sources.Add(renderer.sharedMaterials[sub]);}
                    if(sources.Count!=p.Lods[lod].Parts.Length)throw new InvalidDataException("Ambiguous source part correspondence: "+p.Id+" LOD"+lod);
                    for(int i=0;i<sources.Count;i++)
                    {
                        var target=p.Lods[lod].Parts[i].Material;if(target==null||target.GetTexture("_BaseMap")!=null)continue;
                        RequireOwnedGrassMaterial(target);var source=sources[i];string sourcePath=AssetDatabase.GetAssetPath(source);string before=AssetDatabase.GetAssetDependencyHash(sourcePath).ToString();
                        BackupGrassMaterial(target);bool repaired=CopyYmcFoliageTextures(source,target,p.Category==Sheet.Kind.Grass?512:1024);
                        if(repaired)EditorUtility.SetDirty(target);
                        rows.Add(new PlantRepairRow{prototype=p.Id,lod=lod,part=i,sourceMaterial=sourcePath,targetMaterial=AssetDatabase.GetAssetPath(target),colorSource=repaired?AssetDatabase.GetAssetPath(source.GetTexture("Material_Texture2D_1")):null,normalSource=repaired?AssetDatabase.GetAssetPath(source.GetTexture("Material_Texture2D_0")):null,opacitySource=repaired?AssetDatabase.GetAssetPath(source.GetTexture("Material_Texture2D_3")):null,packedColor=AssetDatabase.GetAssetPath(target.GetTexture("_BaseMap")),sourcePreserved=before==AssetDatabase.GetAssetDependencyHash(sourcePath).ToString(),passed=repaired&&target.GetTexture("_BaseMap")!=null&&target.GetTexture("_BumpMap")!=null&&target.GetFloat("_AlphaClip")>.5f});
                    }
                }
                AssetDatabase.SaveAssets();return rows.ToArray();
            }
            finally{scopedFolder=prior;}
        }
    }
    public static partial class WorldMacroVegetationPolish
    {
        [Serializable] sealed class PlantRepairReport
        {public string utc,scope="Active tree/shrub/grass material audit; only missing supplier texture bindings repaired. No placement, mesh, gameplay, lighting, captures or build changes. Runtime visuals unverified.";public bool passed;public int activePlantSlots;public string[] missingBefore,missingAfter;public WorldMacroDressingAssets.PlantRepairRow[] repairs;}
        static string[] MissingPlantTextures(Sheet sheet)=>sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Tree||p.Category==Sheet.Kind.Shrub||p.Category==Sheet.Kind.Grass).SelectMany(p=>p.Lods.SelectMany((l,lod)=>l.Parts.Select((part,i)=>new{p.Id,lod,part,i}))).Where(x=>x.part.Material==null||x.part.Material.GetTexture("_BaseMap")==null).Select(x=>x.Id+" LOD"+x.lod+" part"+x.i).ToArray();
        static IEnumerator<string> RefreshPlants(Sheet sheet)
        {
            var before=MissingPlantTextures(sheet);var rows=new List<WorldMacroDressingAssets.PlantRepairRow>();
            foreach(var p in sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Tree||p.Category==Sheet.Kind.Shrub||p.Category==Sheet.Kind.Grass))
            {var changes=WorldMacroDressingAssets.RepairMissingPlantMaterials(sheet,p);if(changes.Length==0)continue;rows.AddRange(changes);yield return "Repaired missing maps: "+p.Id+" ("+changes.Length+" material slots)";}
            var after=MissingPlantTextures(sheet);var report=new PlantRepairReport{utc=DateTime.UtcNow.ToString("O"),missingBefore=before,missingAfter=after,repairs=rows.ToArray(),activePlantSlots=sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Tree||p.Category==Sheet.Kind.Shrub||p.Category==Sheet.Kind.Grass).Sum(p=>p.Lods.Sum(l=>l.Parts.Length)),passed=after.Length==0&&rows.All(r=>r.passed&&r.sourcePreserved)};
            File.WriteAllText(Output+"/plant_material_repair.json",JsonUtility.ToJson(report,true));
            yield return report.passed?"All active plant materials have color textures; repaired supplier maps preserved. Visual/performance verification remains separate.":"Unresolved plant texture bindings remain; inspect plant_material_repair.json.";
        }
    }
}
