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
        static readonly Color[] GroundTints={new Color(.54f,.67f,.47f),new Color(.63f,.61f,.48f),new Color(.59f,.47f,.38f),new Color(.50f,.56f,.49f),new Color(.45f,.60f,.51f)};
        [Serializable] sealed class FineGrassReplacement
        {public string id,previousSource,actualSource;public Vector3 preservedFootprintAndHeight;public int previousTriangles,actualTriangles;}
        [Serializable] sealed class FineGrassReport
        {public string utc,scope="Opaque broad-leaf source replaced by existing alpha-tested fine grass. Placement seed, indices, weights, sizes, scales and fixed records preserved. Final visual result unverified.";public FineGrassReplacement[] changed;}
        public static void ReplaceOpaquePolishGrass(Sheet sheet)
        {
            string prior=scopedFolder;scopedFolder=PolishFolder;
            try
            {
                GuardPolish();var rows=new List<FineGrassReplacement>();
                string backup=WorldMacroVegetationPolish.Output+"/Baseline/GrassRefresh/Dressing_BeforeFineGrass.asset";Directory.CreateDirectory(Path.GetDirectoryName(backup));
                string sheetPath=AssetDatabase.GetAssetPath(sheet);if(sheetPath!=WorldMacroVegetationPolish.SheetPath)throw new InvalidOperationException("Fine grass replacement requires the derivative sheet.");
                if(!File.Exists(backup))File.Copy(sheetPath,backup);
                foreach(var p in sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Grass&&p.SourcePath.StartsWith("Assets/HwaseongHaenggung/",StringComparison.Ordinal)))
                {
                    var source=sheet.Prototypes.FirstOrDefault(s=>s.Realm==p.Realm&&s.Id==p.Realm+"_SM_Grass"&&s.SourcePath.StartsWith("Assets/SeyeonjeongPavilion/",StringComparison.Ordinal));
                    if(source==null||source.Lods.Length!=3||source.Lods[0].Parts.Any(s=>s.Material.GetTexture("_BaseMap")==null||s.Material.GetFloat("_AlphaClip")<.5f))throw new InvalidDataException("Verified fine-grass texture and cutout unavailable.");
                    var row=new FineGrassReplacement{id=p.Id,previousSource=p.SourcePath,actualSource=source.SourcePath,preservedFootprintAndHeight=p.Size,previousTriangles=p.Lods[0].Parts.Sum(s=>(int)s.Mesh.GetIndexCount(s.Submesh)/3)};
                    // Preserve the old candidate's bounds, so route clearance and existing placement tests
                    // produce exactly the same decisions. Only the renderer payload changes.
                    var shape=new Vector3(p.Size.x/source.Size.x,p.Size.y/source.Size.y,p.Size.z/source.Size.z);
                    var levels=new Sheet.Level[source.Lods.Length];
                    for(int lod=0;lod<levels.Length;lod++)
                    {
                        int level=lod;levels[lod]=new Sheet.Level{Parts=source.Lods[lod].Parts.Select((part,i)=>
                        {
                            var mat=PolishMaterial(part.Material,p.Id+"_FineGrass_L"+level+"_P"+i);mat.CopyPropertiesFromMaterial(part.Material);mat.enableInstancing=true;mat.SetFloat("_Height",p.Size.y);EditorUtility.SetDirty(mat);
                            var scale=level==2?new Vector3(shape.x,1,shape.z):shape;
                            return new Sheet.Part{Mesh=part.Mesh,Submesh=part.Submesh,Material=mat,Local=Matrix4x4.Scale(scale)*part.Local};
                        }).ToArray()};
                    }
                    p.SourcePath=source.SourcePath;p.SourceHash=source.SourceHash;p.Lods=levels;
                    row.actualTriangles=p.Lods[0].Parts.Sum(s=>(int)s.Mesh.GetIndexCount(s.Submesh)/3);rows.Add(row);
                }
                if(rows.Count>0)File.WriteAllText(WorldMacroVegetationPolish.Output+"/grass_fine_source_replacement.json",JsonUtility.ToJson(new FineGrassReport{utc=DateTime.UtcNow.ToString("O"),changed=rows.ToArray()},true));
                EditorUtility.SetDirty(sheet);AssetDatabase.SaveAssets();
            }
            finally{scopedFolder=prior;}
        }
        public static void RefreshPolishGrass(Sheet sheet,Sheet.Prototype p)
        {
            string prior=scopedFolder;scopedFolder=PolishFolder;
            try
            {
                GuardPolish();bool ymc=p.SourcePath.Contains("YongmeoriCoast/");
                foreach(var part in p.Lods[0].Parts)
                {
                    RequireOwnedGrassMaterial(part.Material);BackupGrassMaterial(part.Material);
                    if(ymc)
                    {
                        // Verified supplier shader: _1 BaseColor, _0 Normal, _3 red-channel OpacityMask.
                        // Its custom names were previously missed by the generic URP material converter.
                        var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/YongmeoriCoast/Material/Foliage/MI_YMC_Grass_01.mat");
                        if(source==null||source.GetTexture("Material_Texture2D_1")==null||source.GetTexture("Material_Texture2D_3")==null)throw new InvalidDataException("YMC grass color/opacity source unavailable.");
                        part.Material.SetTexture("_BaseMap",PackYmcGrass(source));
                        part.Material.SetTexture("_BumpMap",TextureCopy(source.GetTexture("Material_Texture2D_0"),512,true));
                        part.Material.SetFloat("_AlphaClip",1);part.Material.SetFloat("_Cutoff",.333f);part.Material.SetFloat("_Cull",0);part.Material.SetFloat("_WindAmplitude",.035f);
                    }
                    StyleGrass(part.Material,p.Realm,false);
                }
                if(ymc)
                {
                    float span=Mathf.Max(p.Size.y,Mathf.Max(p.Size.x,p.Size.z))*1.12f;
                    // One repaired source atlas, shared by all five regional variants. Other 68-source bakes stay intact.
                    var atlas=DirectionalAtlas("YMC_Grass_Repaired_v2_Patch4",p.Lods[0],span,4,false);
                    var top=DirectionalAtlas("YMC_Grass_Repaired_v2_Cover",p.Lods[0],span,1,true);
                    foreach(var part in p.Lods[1].Parts){RequireOwnedGrassMaterial(part.Material);BackupGrassMaterial(part.Material);part.Material.SetTexture("_BaseMap",atlas);}
                    foreach(var part in p.Lods[2].Parts){RequireOwnedGrassMaterial(part.Material);BackupGrassMaterial(part.Material);part.Material.SetTexture("_BaseMap",top);}
                }
                for(int lod=1;lod<p.Lods.Length;lod++)foreach(var part in p.Lods[lod].Parts){RequireOwnedGrassMaterial(part.Material);BackupGrassMaterial(part.Material);StyleGrass(part.Material,p.Realm,true);}
                ConfigurePolishRanges(sheet,p);EditorUtility.SetDirty(sheet);
            }
            finally{scopedFolder=prior;}
        }
        static void StyleGrass(Material m,Oheangbu.Data.World.RealmId realm,bool card)
        {
            m.SetColor("_BaseColor",GroundTints[(int)realm]);m.SetFloat("_Saturation",.65f);
            m.SetFloat("_AmbientFloor",card?.68f:.30f);m.SetFloat("_LightResponse",.45f);EditorUtility.SetDirty(m);
        }
        static void RequireOwnedGrassMaterial(Material m)
        {if(m==null||!AssetDatabase.GetAssetPath(m).StartsWith(PolishFolder+"/Materials/",StringComparison.Ordinal))throw new InvalidOperationException("Grass refresh may only modify the VegetationPolish derivative.");}
        static void BackupGrassMaterial(Material m)
        {
            string path=AssetDatabase.GetAssetPath(m),dir=WorldMacroVegetationPolish.Output+"/Baseline/GrassRefresh";Directory.CreateDirectory(dir);
            string target=dir+"/"+Path.GetFileName(path);if(!File.Exists(target))File.Copy(path,target);
        }
        static Texture2D PackYmcGrass(Material source)
        {
            string path=Folder+"/Textures/YMC_Grass_BaseColor_Opacity_v2.png";var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(existing!=null)return existing;
            Texture2D color=null,opacity=null,packed=null;
            try
            {
                color=new Texture2D(2,2,TextureFormat.RGBA32,false);opacity=new Texture2D(2,2,TextureFormat.RGBA32,false);
                if(!color.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(source.GetTexture("Material_Texture2D_1")))))throw new InvalidDataException("Invalid grass base-color PNG.");
                if(!opacity.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(source.GetTexture("Material_Texture2D_3")))))throw new InvalidDataException("Invalid grass opacity PNG.");
                if(color.width!=opacity.width||color.height!=opacity.height)throw new InvalidDataException("Grass color/opacity UV dimensions differ.");
                var rgb=color.GetPixels32();var alpha=opacity.GetPixels32();int opaque=0,clear=0;
                for(int i=0;i<rgb.Length;i++){rgb[i].a=alpha[i].r;if(alpha[i].r>170)opaque++;if(alpha[i].r<16)clear++;}
                if(opaque==0||clear==0)throw new InvalidDataException("Grass opacity has no valid foreground/background.");
                packed=new Texture2D(color.width,color.height,TextureFormat.RGBA32,false);packed.SetPixels32(rgb);packed.Apply(false);File.WriteAllBytes(path,packed.EncodeToPNG());
                string report="{\"scope\":\"RGBA packing: original RGB unchanged; source opacity red copied into alpha\",\"pixels\":"+rgb.Length+",\"opaque\":"+opaque+",\"clear\":"+clear+",\"passed\":true}";
                File.WriteAllText(WorldMacroVegetationPolish.Output+"/grass_texture_repair.json",report);
            }
            finally{if(color!=null)Object.DestroyImmediate(color);if(opacity!=null)Object.DestroyImmediate(opacity);if(packed!=null)Object.DestroyImmediate(packed);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.alphaIsTransparency=true;importer.mipmapEnabled=true;importer.isReadable=false;importer.maxTextureSize=512;importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        public static void AddLowGrassPalette(Sheet sheet)
        {
            string prior=scopedFolder;scopedFolder=PolishFolder;
            try
            {
                var result=sheet.Prototypes.ToList();
                for(int realm=0;realm<5;realm++)
                {
                    string id=((Oheangbu.Data.World.RealmId)realm)+"_LowGroundFill";
                    var source=result.FirstOrDefault(p=>(int)p.Realm==realm&&p.Id.EndsWith("_SM_Grass_Haenggung",StringComparison.Ordinal));
                    if(source==null)throw new InvalidDataException("Prepared owned low grass missing for realm "+realm);
                    var p=result.FirstOrDefault(x=>x.Id==id);
                    if(p==null)
                    {
                        p=new Sheet.Prototype{Id=id,SourcePath=source.SourcePath,SourceHash=source.SourceHash,Realm=source.Realm,Category=Sheet.Kind.Grass,Weight=0,Scale=new Vector2(.48f,.75f),Size=source.Size,Radius=source.Radius,GroundPatch=true,LowInfill=true,PolishVersion=1,MaximumSlope=32,MaximumAltitude=480,Lods=new Sheet.Level[source.Lods.Length]};
                        for(int lod=0;lod<p.Lods.Length;lod++)p.Lods[lod]=new Sheet.Level{Parts=source.Lods[lod].Parts.Select((part,i)=>new Sheet.Part{Mesh=part.Mesh,Submesh=part.Submesh,Local=part.Local,Material=PolishMaterial(part.Material,id+"_L"+lod+"_P"+i)}).ToArray()};
                        result.Add(p);
                    }
                    foreach(var part in p.Lods[0].Parts){StyleGrass(part.Material,p.Realm,false);SetRange(part.Material,-1,0,sheet.LowGrassMeshDistance-5,sheet.LowGrassMeshDistance+5);}
                    foreach(var part in p.Lods[1].Parts){StyleGrass(part.Material,p.Realm,true);SetRange(part.Material,sheet.LowGrassMeshDistance-5,sheet.LowGrassMeshDistance+5,sheet.LowGrassDistance-20,sheet.LowGrassDistance);}
                    // This layer intentionally has no extra far-ground pass; the existing cover owns that distance.
                    foreach(var part in p.Lods[2].Parts)SetRange(part.Material,99990,99999,99990,99999);
                }
                sheet.Prototypes=result.ToArray();sheet.LowGrassInfill=true;EditorUtility.SetDirty(sheet);AssetDatabase.SaveAssets();
            }
            finally{scopedFolder=prior;}
        }
    }
    public static partial class WorldMacroVegetationPolish
    {
        static IEnumerator<string> RefreshGrass(Sheet sheet)
        {
            if(sheet.PaletteVersion!=4)throw new InvalidOperationException("Complete the original prepare queue first.");
            var fixedBefore=string.Join("\n",sheet.FixedPlacements.Select(p=>JsonUtility.ToJson(p)));
            WorldMacroDressingAssets.ReplaceOpaquePolishGrass(sheet);
            var original=sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Grass&&!p.LowInfill).ToArray();
            foreach(var p in original){WorldMacroDressingAssets.RefreshPolishGrass(sheet,p);AssetDatabase.SaveAssets();yield return "Repaired grass derivative "+p.Id;}
            WorldMacroDressingAssets.AddLowGrassPalette(sheet);
            if(string.Join("\n",sheet.FixedPlacements.Select(p=>JsonUtility.ToJson(p)))!=fixedBefore)throw new InvalidOperationException("Fixed placements changed during grass refresh.");
            var renderer=Object.FindFirstObjectByType<Oheangbu.App.World.Dressing.WorldMacroDressingRenderer>();if(renderer!=null&&renderer.Sheet==sheet)renderer.ResetCache();
            File.WriteAllText(Output+"/GRASS_REFRESH.md","# 낮은 풀과 재질 보완\n\n용머리 풀의 커스텀 색상·노멀·별도 불투명 마스크가 누락되던 파생 재질을 복구했습니다. 원본 RGB를 유지하고 마스크 R을 alpha에 넣습니다. 해당 풀의 4방향/상부 카드 두 장만 다시 굽고 나머지 식생 카드는 재사용합니다. 풀 재질은 강토별 탁한 녹색·황갈색·회갈색으로 조정하며 전역 조명은 그대로입니다.\n\n화성행궁의 불투명 큰 잎 원형이 반복되는 블록으로 보이는 문제는 세연정의 실제 alpha-cutout 잔풀로 교체했습니다. 기존 슬롯 ID·가중치·배치 크기·시드는 보존하며 SourcePath/SourceHash는 실제 새 공급자 경로로 기록합니다. 일반 풀과 낮은 풀의 해당 10개 슬롯은 1,568 tris에서 16 tris로 줄고 기존 메시·텍스처는 보존됩니다. 새 카드를 굽지 않고 기존 세연정 카드를 재사용합니다.\n\n낮은 풀은 별도 시드/1.25m 격자입니다. 기존 군락·나무·수동 배치의 위치는 변경하지 않습니다. 높이 약 13–21cm, 메시 20m·카드 110m, 그림자와 충돌체 없음. 실제 통행 폭과 패치 반경을 비우며 기존 지형·수변·고도·민가 마스크를 따릅니다.\n\n정적 검사는 validate 명령, 현재 카메라에서의 밀도·색·LOD와 추가 CPU/GPU 비용은 별도 검수이며 미검증입니다.\n");
            yield return "Grass materials repaired; additive low layer installed. Run validate and current-camera capture/performance separately.";
        }
    }
}
