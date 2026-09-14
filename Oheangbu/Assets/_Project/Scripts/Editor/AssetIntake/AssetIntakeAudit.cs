using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // SPEC-ASSET-INTAKE. Source assets retain paths/GUIDs. Previews copy geometry only;
    // no vendor MonoBehaviour, animator, particle system, or audio source is instantiated.
    public static class AssetIntakeAudit
    {
        public static readonly string[] Packs={"BillemotdonggulLavaTubePack","HwaseongForteressGate","HwaseongHaenggung","JejumokGwana","Korea_TreasureProps","KoreanTraditionalFestival","KTinteractiveProp","SeyeonjeongPavilion","YongmeoriCoast"};
        static string UnityRoot=>Directory.GetParent(Application.dataPath).FullName;
        static string RepoRoot=>Directory.GetParent(UnityRoot).FullName;
        static string ReportFolder=>Path.Combine(RepoRoot,"Docs/Assets");
        static string PreviewFolder=>Path.Combine(UnityRoot,"Screenshots/AssetCatalog");
        static string[] Roots=>Packs.Select(p=>"Assets/"+p).ToArray();
        static Catalog catalog;
        static Vector3 previewDirection=new Vector3(1f,.55f,-1f);

        [Serializable] public class Entry
        {
            public int index; public string kind,path,guid,pack,name,category,label,placement,confidence,preview,previewStatus;
            public int renderers,meshes,colliders,missingScripts,missingMesh,missingColliderMesh,missingMaterial,brokenMaterials,lodGroups;
            public long triangles,highestLodTriangles; public float[] size; public string[] materials,modelSources,behaviours; public string[] lodDetails;
            public float previewCoverage;public string previewView;public string[] alternatePreviews,animationClips;
        }
        [Serializable] public class MatEntry
        {
            public string path,guid,name,shader,status,pipeline;public long localId;public bool embedded,supported,hasErrors;public string[] textures;
        }
        [Serializable] public class Catalog
        {
            public string created,scope;public int prefabs,modelFiles,modelsWithoutPrefab;public List<Entry> entries=new List<Entry>();public List<MatEntry> materials=new List<MatEntry>();
        }
        [Serializable] public class Repair
        {
            public string path,beforeShader,afterShader,backup,status;public List<string> mapping=new List<string>();
        }
        [Serializable] public class Repairs { public string timestamp;public List<Repair> items=new List<Repair>(); }

        [MenuItem("Oheangbu/Assets/1 Audit received model packs")]
        public static void AuditMenu()=>Debug.Log(Scan());

        public static string Scan()
        {
            Directory.CreateDirectory(ReportFolder);Directory.CreateDirectory(PreviewFolder);
            catalog=new Catalog{created=DateTimeOffset.Now.ToString("o"),scope=string.Join(", ",Packs)};
            var prefabPaths=Find("t:Prefab");var modelPaths=Find("t:Model");
            catalog.prefabs=prefabPaths.Length;catalog.modelFiles=modelPaths.Length;
            var usedModels=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var statuses=new Dictionary<Shader,string>();
            ScanMaterials(statuses);
            foreach(var file in prefabPaths)
            {
                var entry=ReadEntry(file,"prefab",statuses);catalog.entries.Add(entry);
                foreach(var m in entry.modelSources)usedModels.Add(m);
                if(catalog.entries.Count%4==0){File.WriteAllText(Path.Combine(ReportFolder,"scan-progress.txt"),"Prefabs "+catalog.entries.Count+"/"+prefabPaths.Length);EditorUtility.UnloadUnusedAssetsImmediate();}
            }
            foreach(var file in modelPaths)
                if(!usedModels.Contains(file)){catalog.entries.Add(ReadEntry(file,"model_without_prefab",statuses));catalog.modelsWithoutPrefab++;}
            for(int i=0;i<catalog.entries.Count;i++)catalog.entries[i].index=i;
            Save();
            return Summary();
        }

        public static string RefreshMaterialCatalog()
        {
            if(catalog==null)Load();catalog.materials.Clear();
            ScanMaterials(new Dictionary<Shader,string>());catalog.created=DateTimeOffset.Now.ToString("o");Save();return Summary();
        }
        static void ScanMaterials(Dictionary<Shader,string> statuses)
        {
            int materialFilesScanned=0;
            foreach(var file in Find("t:Material"))
            {
            foreach(var material in AssetDatabase.LoadAllAssetsAtPath(file).OfType<Material>())
            {
                if(material==null)continue;
                var shader=material.shader;string status=ShaderStatus(material,statuses);
                var textures=new List<string>();
                if(shader!=null)for(int p=0;p<shader.GetPropertyCount();p++)
                    if(shader.GetPropertyType(p)==ShaderPropertyType.Texture)
                    {string property=shader.GetPropertyName(p);var t=material.GetTexture(property);if(t!=null)textures.Add(property+"="+AssetDatabase.GetAssetPath(t));}
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material,out string guid,out long localId);
                catalog.materials.Add(new MatEntry{path=file,guid=guid,localId=localId,name=material.name,embedded=!file.EndsWith(".mat",StringComparison.OrdinalIgnoreCase),shader=shader?shader.name:"MISSING",status=status,pipeline=material.GetTag("RenderPipeline",false,""),supported=shader&&shader.isSupported,hasErrors=shader&&ShaderUtil.ShaderHasError(shader),textures=textures.ToArray()});
            }
            if(++materialFilesScanned%4==0){File.WriteAllText(Path.Combine(ReportFolder,"scan-progress.txt"),"Material files "+materialFilesScanned);EditorUtility.UnloadUnusedAssetsImmediate();}
            }
        }

        static string[] Find(string filter)=>AssetDatabase.FindAssets(filter,Roots).Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        static Entry ReadEntry(string file,string kind,Dictionary<Shader,string> statuses)
        {
            var e=new Entry{path=file,guid=AssetDatabase.AssetPathToGUID(file),kind=kind,pack=file.Split('/')[1],name=Path.GetFileNameWithoutExtension(file),category=AssetIntakeTaxonomy.CategoryFor(file),label=AssetIntakeTaxonomy.LabelFor(file),confidence=AssetIntakeTaxonomy.ConfidenceFor(file)};
            e.placement=AssetIntakeTaxonomy.PlacementFor(e.category);e.preview="Screenshots/AssetCatalog/Thumbnails/"+e.guid+".png";
            e.previewStatus=File.Exists(Path.Combine(UnityRoot,e.preview))?"EXISTING_REVIEW":"pending";
            var go=AssetDatabase.LoadAssetAtPath<GameObject>(file);
            var mats=new HashSet<string>();var models=new HashSet<string>();var behaviours=new HashSet<string>();var uniqueMeshes=new HashSet<Mesh>();var badMats=new HashSet<Material>();
            if(go==null){e.missingMesh=1;e.materials=Array.Empty<string>();e.modelSources=Array.Empty<string>();e.behaviours=Array.Empty<string>();e.lodDetails=Array.Empty<string>();return e;}
            Bounds bounds=default;bool first=true;
            foreach(var transform in go.GetComponentsInChildren<Transform>(true))
            {
                e.missingScripts+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                foreach(var mb in transform.GetComponents<MonoBehaviour>())if(mb!=null)behaviours.Add(mb.GetType().FullName);
            }
            var all=go.GetComponentsInChildren<Renderer>(true);e.renderers=all.Length;e.colliders=go.GetComponentsInChildren<Collider>(true).Length;
            if(all.Length==0&&kind!="prefab")
            {
                e.animationClips=AssetDatabase.LoadAllAssetsAtPath(file).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview")).Select(c=>c.name).ToArray();
                if(e.animationClips.Length>0){e.kind="animation_clip_source";e.category="애니메이션/가구 개폐";e.label=Path.GetFileNameWithoutExtension(file);e.preview="";e.previewStatus="NOT_APPLICABLE_ANIMATION";e.confidence="AnimationClip 하위 자산 확인";e.placement="동일 이름 가구 프리팹의 개폐 애니메이션. 별도 배치 메시 없음.";}
            }
            e.missingColliderMesh=go.GetComponentsInChildren<MeshCollider>(true).Count(c=>!c.sharedMesh);
            foreach(var renderer in all)
            {
                foreach(var material in renderer.sharedMaterials)
                {
                    if(material==null){e.missingMaterial++;continue;}
                    mats.Add(AssetDatabase.GetAssetPath(material));if(ShaderStatus(material,statuses)!="OK")badMats.Add(material);
                }
                var mesh=MeshOf(renderer);if(mesh==null){if(renderer is MeshRenderer||renderer is SkinnedMeshRenderer)e.missingMesh++;continue;}
                uniqueMeshes.Add(mesh);e.triangles+=Triangles(mesh);
                var model=AssetDatabase.GetAssetPath(mesh);if(!string.IsNullOrEmpty(model))models.Add(model);
                var b=TransformedBounds(mesh.bounds,go.transform.worldToLocalMatrix*renderer.localToWorldMatrix);
                if(first){bounds=b;first=false;}else bounds.Encapsulate(b);
            }
            var lodDetails=new List<string>();var nonHighest=new HashSet<Renderer>();
            foreach(var group in go.GetComponentsInChildren<LODGroup>(true))
            {
                e.lodGroups++;var levels=group.GetLODs();
                for(int i=0;i<levels.Length;i++)
                {long tris=0;foreach(var r in levels[i].renderers){if(!r)continue;tris+=Triangles(MeshOf(r));if(i>0)nonHighest.Add(r);}lodDetails.Add(group.name+"/LOD"+i+":"+tris);}
            }
            foreach(var renderer in all)if(!nonHighest.Contains(renderer))e.highestLodTriangles+=Triangles(MeshOf(renderer));
            e.meshes=uniqueMeshes.Count;e.brokenMaterials=badMats.Count;e.size=new[]{bounds.size.x,bounds.size.y,bounds.size.z};
            e.materials=mats.OrderBy(p=>p).ToArray();e.modelSources=models.OrderBy(p=>p).ToArray();e.behaviours=behaviours.OrderBy(p=>p).ToArray();e.lodDetails=lodDetails.ToArray();return e;
        }

        static string ShaderStatus(Material material,Dictionary<Shader,string> cache)
        {
            var shader=material.shader;if(!shader||shader.name=="Hidden/InternalErrorShader")return "MISSING_SHADER";
            if(cache.TryGetValue(shader,out var cached))return cached;
            string result=ShaderUtil.ShaderHasError(shader)?"SHADER_ERROR":!shader.isSupported?"UNSUPPORTED":
                shader.name=="Standard"||shader.name.StartsWith("Standard (")||shader.name.StartsWith("Legacy Shaders/")?"BUILTIN_IN_URP":
                material.GetTag("RenderPipeline",false,"")=="UniversalPipeline"||shader.name.StartsWith("Universal Render Pipeline/")||shader.name.StartsWith("Shader Graphs/")?"OK":"REVIEW_PIPELINE";
            cache[shader]=result;return result;
        }

        public static string RepairKnownMaterials()
        {
            if(catalog==null)Load();
            var log=new Repairs{timestamp=DateTimeOffset.Now.ToString("o")};
            string backupFolder=Path.Combine(RepoRoot,"Art/AssetIntakeBackups/20260907");
            var urp=Shader.Find("Universal Render Pipeline/Lit");if(!urp)return "FAIL: URP Lit unavailable";
            foreach(var record in catalog.materials.Where(m=>m.status!="OK"))
            {
                if(!record.path.EndsWith(".mat",StringComparison.OrdinalIgnoreCase)){log.items.Add(new Repair{path=record.path,beforeShader=record.shader,status="REVIEW: embedded material requires an external remap; unchanged"});continue;}
                var material=AssetDatabase.LoadAssetAtPath<Material>(record.path);if(!material)continue;
                var oldShader=material.shader;string oldName=oldShader?oldShader.name:"MISSING";
                var repair=new Repair{path=record.path,beforeShader=oldName};log.items.Add(repair);
                bool standard=oldName=="Standard"||oldName=="Standard (Specular setup)";
                bool isUnreal=oldName.StartsWith("Unreal/",StringComparison.Ordinal);
                // A generated shader's name does not identify its pipeline. Yongmeori
                // has working URP shaders named Unreal/*; never replace those by name.
                if(material.GetTag("RenderPipeline",false,"")=="UniversalPipeline")
                {repair.status=oldShader&&oldShader.isSupported&&!ShaderUtil.ShaderHasError(oldShader)?"SKIPPED: current URP shader is healthy":"REVIEW: URP shader needs source-level repair";continue;}
                if(!standard&&!isUnreal)
                {repair.status="REVIEW: custom/missing shader needs semantic mapping";continue;}
                string sourcePath=oldShader?AssetDatabase.GetAssetPath(oldShader):"";
                string source=File.Exists(Path.Combine(UnityRoot,sourcePath))?File.ReadAllText(Path.Combine(UnityRoot,sourcePath)):"";
                bool billemotFormula=isUnreal&&IsKnownBillemotFormula(source);
                if(isUnreal&&!billemotFormula)
                {repair.status="REVIEW: custom texture operations/roughness/opacity require a verified mapping; material unchanged";continue;}
                string backup=Path.Combine(backupFolder,record.path.Replace('/',Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(backup));if(!File.Exists(backup))File.Copy(Path.Combine(UnityRoot,record.path),backup);
                repair.backup=backup;
                var textures=ReadTextures(material);
                string FindTexture(params string[] candidates)
                {
                    foreach(var candidate in candidates)
                    {var exact=textures.FirstOrDefault(t=>t.texture&&(t.name.Equals(candidate,StringComparison.OrdinalIgnoreCase)||t.description.Equals(candidate,StringComparison.OrdinalIgnoreCase)));if(exact!=null)return exact.name;}
                    return null;
                }
                TextureSlot Slot(string name)=>name==null?null:textures.First(t=>t.name==name);
                string baseName=isUnreal?FindTexture("MainTexture","BaseColor","Base Color","BC","Diffuse","Albedo"):FindTexture("_MainTex");
                string normalName=FindTexture("_BumpMap","NormalMap","Normal","NM");
                string aoName=FindTexture("_OcclusionMap","Occlusion","AmbientOcclusion","AO");
                string metallicName=FindTexture("_MetallicGlossMap","Metallic","MT");
                string emissionName=FindTexture("_EmissionMap","Emissive","Emission");
                var tint=material.HasProperty("_Color")?material.GetColor("_Color"):Color.white;
                var emission=material.HasProperty("_EmissionColor")?material.GetColor("_EmissionColor"):Color.black;
                float Float(string p,float fallback)=>material.HasProperty(p)?material.GetFloat(p):fallback;
                float mode=Float("_Mode",0),cutoff=Float("_Cutoff",.5f),metallic=Float("_Metallic",0),bump=Float("_BumpScale",1),occlusion=Float("_OcclusionStrength",1);
                // Standard's rasterizer culls backfaces; doubleSidedGI is a separate
                // lightmapping flag and must not silently enable two-sided rendering.
                float cull=Float("_Cull",2),smoothChannel=Float("_SmoothnessTextureChannel",0);
                string specName=FindTexture("_SpecGlossMap");var specColor=material.HasProperty("_SpecColor")?material.GetColor("_SpecColor"):new Color(.2f,.2f,.2f,1);
                bool specular=oldName.Contains("Specular");
                bool smoothFromMap=(specular?specName:metallicName)!=null||smoothChannel>.5f;
                float smooth=smoothFromMap?Float("_GlossMapScale",1):Float("_Glossiness",.5f);
                float detailNormalScale=Float("_DetailNormalMapScale",1),parallax=Float("_Parallax",.02f),specHighlights=Float("_SpecularHighlights",1),reflections=Float("_GlossyReflections",1);
                bool detail=FindTexture("_DetailAlbedoMap","_DetailNormalMap")!=null;
                if(standard&&detail&&Float("_UVSec",0)>.5f)
                {repair.status="REVIEW: Standard detail UV1 is not supported by URP Lit; material unchanged";continue;}
                if(standard&&mode>=2&&smoothChannel>.5f)
                {repair.status="REVIEW: transparent albedo-alpha smoothness needs a separate packed smoothness texture; material unchanged";continue;}
                if(isUnreal&&(baseName==null||aoName==null||normalName==null))
                {repair.status="REVIEW: verified cave formula requires albedo, AO, and normal textures; material unchanged";continue;}
                var original=new Material(material);
                try
                {
                    Texture2D packed=null;
                    if(billemotFormula)
                    {
                        packed=PackBillemotMask(record.path,Slot(baseName),Slot(aoName),repair);
                        // The original shader samples UV0 without texture ST and sets
                        // opacity=1, emission=0 and Cull Off explicitly in its source.
                        tint=Color.white;emission=Color.black;cull=0;mode=0;smoothChannel=0;smooth=1;metallic=1;occlusion=1;bump=1;
                    }
                    int originalQueue=material.renderQueue;
                    bool originalEmission=material.IsKeywordEnabled("_EMISSION");
                    material.shader=urp;material.shaderKeywords=Array.Empty<string>();material.SetColor("_BaseColor",tint);
                    material.SetFloat("_WorkflowMode",specular?0:1);material.SetColor("_SpecColor",specColor);if(specular)material.EnableKeyword("_SPECULAR_SETUP");
                    material.SetFloat("_Metallic",metallic);material.SetFloat("_Smoothness",smooth);material.SetFloat("_BumpScale",bump);material.SetFloat("_OcclusionStrength",occlusion);material.SetFloat("_Cull",cull);
                    material.SetFloat("_SpecularHighlights",specHighlights);material.SetFloat("_EnvironmentReflections",reflections);
                    if(specHighlights==0)material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");if(reflections==0)material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    void Map(string input,string target,string keyword=null)
                    {if(input==null){material.SetTexture(target,null);return;}var t=Slot(input);material.SetTexture(target,t.texture);material.SetTextureScale(target,billemotFormula?Vector2.one:t.scale);material.SetTextureOffset(target,billemotFormula?Vector2.zero:t.offset);repair.mapping.Add(input+" -> "+target+" : "+AssetDatabase.GetAssetPath(t.texture)+"; scale="+t.scale+"; offset="+t.offset);if(keyword!=null)material.EnableKeyword(keyword);}
                    Map(baseName,"_BaseMap");Map(normalName,"_BumpMap","_NORMALMAP");
                    Map(aoName,"_OcclusionMap","_OCCLUSIONMAP");Map(metallicName,"_MetallicGlossMap");Map(specName,"_SpecGlossMap");
                    if((specular?specName:metallicName)!=null)material.EnableKeyword("_METALLICSPECGLOSSMAP");
                    if(packed){material.SetTexture("_MetallicGlossMap",packed);material.SetTexture("_OcclusionMap",packed);material.EnableKeyword("_METALLICSPECGLOSSMAP");material.EnableKeyword("_OCCLUSIONMAP");}
                    Map(emissionName,"_EmissionMap");material.SetColor("_EmissionColor",emission);if(originalEmission&&emission.maxColorComponent>0)material.EnableKeyword("_EMISSION");
                    Map(FindTexture("_DetailAlbedoMap"),"_DetailAlbedoMap");Map(FindTexture("_DetailNormalMap"),"_DetailNormalMap");Map(FindTexture("_DetailMask"),"_DetailMask");Map(FindTexture("_ParallaxMap"),"_ParallaxMap","_PARALLAXMAP");
                    material.SetFloat("_DetailAlbedoMapScale",1);material.SetFloat("_DetailNormalMapScale",detailNormalScale);material.SetFloat("_Parallax",parallax);
                    if(detail)material.EnableKeyword("_DETAIL_MULX2");
                    material.SetFloat("_SmoothnessTextureChannel",smoothChannel);if(smoothChannel>.5f&&mode<2)material.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                    ConfigureRepairBlend(material,(int)mode,cutoff,originalQueue);
                    repair.mapping.Add("Smoothness="+smooth+" from "+(smoothFromMap?"_GlossMapScale":"_Glossiness")+"; channel="+smoothChannel+"; detailNormalScale="+detailNormalScale+"; parallax="+parallax+"; mode="+mode+"; cull="+cull);
                    repair.afterShader=material.shader.name;
                    if(!material.shader.isSupported||ShaderUtil.ShaderHasError(material.shader))throw new InvalidOperationException("URP Lit validation failed");
                    repair.status="REPAIRED: properties mapped; visual validation pending";
                    EditorUtility.SetDirty(material);
                }
                catch(Exception ex)
                {EditorUtility.CopySerialized(original,material);EditorUtility.SetDirty(material);repair.afterShader=oldName;repair.status="REVIEW: rolled back: "+ex.Message;}
                finally{Object.DestroyImmediate(original);}
            }
            AssetDatabase.SaveAssets();File.WriteAllText(Path.Combine(ReportFolder,"MaterialRepairs.json"),JsonUtility.ToJson(log,true),new UTF8Encoding(false));
            return $"Repair pass: {log.items.Count(i=>i.status.StartsWith("REPAIRED"))} repaired, {log.items.Count(i=>i.status.StartsWith("REVIEW"))} review. Originals backed up to {backupFolder}. Run Scan again.";
        }

        static void ConfigureRepairBlend(Material material,int mode,float cutoff,int sourceQueue)
        {
            bool transparent=mode>=2,cutout=mode==1,premultiplied=mode==3;
            material.SetFloat("_AlphaClip",cutout?1:0);material.SetFloat("_Cutoff",cutoff);material.SetFloat("_Surface",transparent?1:0);
            // URP 17 Alpha+PreserveSpecular performs diffuse premultiplication in
            // shader, matching Standard mode 3. URP Blend=Premultiply instead assumes
            // already premultiplied input and must not be used for Standard textures.
            material.SetFloat("_Blend",0);material.SetFloat("_BlendModePreserveSpecular",premultiplied?1:0);
            material.SetFloat("_SrcBlend",transparent&&!premultiplied?(float)BlendMode.SrcAlpha:(float)BlendMode.One);
            material.SetFloat("_DstBlend",transparent?(float)BlendMode.OneMinusSrcAlpha:(float)BlendMode.Zero);
            material.SetFloat("_SrcBlendAlpha",(float)BlendMode.One);material.SetFloat("_DstBlendAlpha",transparent?(float)BlendMode.OneMinusSrcAlpha:(float)BlendMode.Zero);
            material.SetFloat("_ZWrite",transparent?0:1);material.SetFloat("_AlphaToMask",cutout?1:0);
            if(cutout)material.EnableKeyword("_ALPHATEST_ON");if(transparent)material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");if(premultiplied)material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType",cutout?"TransparentCutout":transparent?"Transparent":"Opaque");
            int automatic=cutout?2450:transparent?3000:2000;
            material.renderQueue=sourceQueue>=0?sourceQueue:automatic;material.SetFloat("_QueueOffset",material.renderQueue-automatic);
            material.SetShaderPassEnabled("ShadowCaster",!transparent);material.SetShaderPassEnabled("DepthOnly",!transparent);
        }

        static bool IsKnownBillemotFormula(string source)
        {
            // Match the operations, not only texture descriptions: other Unreal
            // exports can use layered textures or packed RN/MT maps differently.
            string[] required={"#pragma surface surf Standard vertex:vert addshadow","Cull Off",
                "MaterialFloat2 Local0 = Parameters.TexCoords[0].xy;","MaterialFloat Local8 = (Local6.g - Local6.b);",
                "MaterialFloat Local9 = saturate(Local8);","MaterialFloat3 Local13 = (((MaterialFloat3)1.00000000) - Local11.rgb);",
                "MaterialFloat3 Local14 = (((MaterialFloat3)Local9) * Local13);","MaterialFloat3 Local15 = (((MaterialFloat3)1.00000000) - Local14);",
                "MaterialFloat3 Local16 = PositiveClampedPow(Local15,((MaterialFloat3)20.00000000));","MaterialFloat3 Local17 = saturate(Local16);",
                "PixelMaterialInputs.BaseColor = Local6.rgb;","PixelMaterialInputs.Metallic = Local16;","PixelMaterialInputs.Roughness = Local17;",
                "PixelMaterialInputs.AmbientOcclusion = Local11.rgb;","PixelMaterialInputs.Opacity = 1.00000000;",
                "Material.PreshaderBuffer[0] = float4(0.000000,0.000000,0.000000,0.000000);"};
            return required.All(source.Contains);
        }

        static Texture2D PackBillemotMask(string materialPath,TextureSlot albedo,TextureSlot ao,Repair repair)
        {
            int width=Math.Max(albedo.texture.width,ao.texture.width),height=Math.Max(albedo.texture.height,ao.texture.height);
            if(width>SystemInfo.maxTextureSize||height>SystemInfo.maxTextureSize)throw new InvalidOperationException("Mask exceeds GPU texture limit");
            Color[] basePixels=ReadTextureOnGpu(albedo.texture,width,height),aoPixels=ReadTextureOnGpu(ao.texture,width,height);
            var output=new Color32[basePixels.Length];
            for(int i=0;i<output.Length;i++)
            {
                float metallic=Mathf.Pow(1-Mathf.Clamp01(basePixels[i].g-basePixels[i].b)*(1-aoPixels[i].r),20);
                output[i]=new Color(Mathf.Clamp01(metallic),Mathf.Clamp01(aoPixels[i].r),0,1-Mathf.Clamp01(metallic));
            }
            string folder="Assets/_Project/Art/AssetIntake/RecoveredMasks";DevSceneKit.EnsureFolder(folder);
            string path=folder+"/"+AssetDatabase.AssetPathToGUID(materialPath)+"_MetallicAO_Smoothness.png";
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,false,true);
            try{texture.SetPixels32(output);texture.Apply(false,false);File.WriteAllBytes(Path.Combine(UnityRoot,path),texture.EncodeToPNG());}
            finally{Object.DestroyImmediate(texture);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=false;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=true;importer.maxTextureSize=Mathf.NextPowerOfTwo(Math.Max(width,height));
            importer.wrapMode=albedo.texture.wrapMode;importer.filterMode=albedo.texture.filterMode;importer.anisoLevel=albedo.texture.anisoLevel;importer.SaveAndReimport();
            var result=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(!result)throw new InvalidOperationException("Packed mask import failed");
            repair.mapping.Add("BAKED "+width+"x"+height+" linear RGBA8: R=pow(1-saturate(base.g-base.b)*(1-AO.r),20), G=AO.r, A=1-R -> "+path+"; source albedo="+AssetDatabase.GetAssetPath(albedo.texture)+"; source AO="+AssetDatabase.GetAssetPath(ao.texture));
            return result;
        }

        static Color[] ReadTextureOnGpu(Texture texture,int width,int height)
        {
            var previous=RenderTexture.active;bool oldSrgb=GL.sRGBWrite;
            var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var copy=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
            try
            {
                GL.sRGBWrite=false;Graphics.Blit(texture,target);RenderTexture.active=target;
                copy.ReadPixels(new Rect(0,0,width,height),0,0,false);copy.Apply(false,false);return copy.GetPixels();
            }
            finally{RenderTexture.active=previous;GL.sRGBWrite=oldSrgb;Object.DestroyImmediate(copy);RenderTexture.ReleaseTemporary(target);}
        }

        class TextureSlot { public string name,description;public Texture texture;public Vector2 scale,offset; }
        static List<TextureSlot> ReadTextures(Material m)
        {
            var list=new List<TextureSlot>();if(!m.shader)return list;
            for(int i=0;i<m.shader.GetPropertyCount();i++)if(m.shader.GetPropertyType(i)==ShaderPropertyType.Texture)
            {string n=m.shader.GetPropertyName(i);list.Add(new TextureSlot{name=n,description=m.shader.GetPropertyDescription(i),texture=m.GetTexture(n),scale=m.GetTextureScale(n),offset=m.GetTextureOffset(n)});}
            return list;
        }

        public static string PreviewBatch(int start,int count)
        {
            if(catalog==null)Load();Directory.CreateDirectory(Path.Combine(PreviewFolder,"Thumbnails"));
            var preview=new PreviewRenderUtility();var statuses=new Dictionary<Shader,string>();int ok=0,failed=0;bool previousAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            try
            {
                preview.camera.cameraType=CameraType.Preview;preview.camera.fieldOfView=32;preview.camera.allowHDR=false;preview.camera.allowMSAA=false;
                preview.camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
                preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.28f,.31f,.34f,1);
                preview.lights[0].intensity=1.15f;preview.lights[0].transform.rotation=Quaternion.Euler(40,140,0);
                preview.lights[1].intensity=.55f;preview.lights[1].transform.rotation=Quaternion.Euler(340,320,0);
                preview.lights[0].color=Color.white;preview.lights[1].color=Color.white;
                preview.ambientColor=new Color(.30f,.30f,.30f,1);
                for(int i=start;i<Mathf.Min(start+count,catalog.entries.Count);i++)
                {
                    var previousEntry=catalog.entries[i];var e=ReadEntry(previousEntry.path,previousEntry.kind,statuses);e.index=i;catalog.entries[i]=e;GameObject geometry=null;Texture2D pixels=null;
                    try
                    {
                        if(e.kind=="animation_clip_source")continue;
                        var source=AssetDatabase.LoadAssetAtPath<GameObject>(e.path);if(!source)throw new InvalidOperationException("Asset GameObject missing");
                        geometry=CopyGeometry(source,out var bounds);preview.AddSingleGO(geometry);
                        float radius=Mathf.Max(bounds.extents.magnitude,.02f);var direction=previewDirection.normalized;e.previewView=previewDirection.ToString();
                        preview.camera.transform.position=bounds.center+direction*radius/Mathf.Sin(16*Mathf.Deg2Rad)*1.12f;
                        preview.camera.transform.LookAt(bounds.center);preview.camera.nearClipPlane=Mathf.Max(.001f,radius*.005f);preview.camera.farClipPlane=Mathf.Max(20,radius*20);
                        Texture texture;bool previewOpen=false;
                        try
                        {
                            preview.BeginPreview(new Rect(0,0,384,384),GUIStyle.none);previewOpen=true;
                            foreach(var renderer in geometry.GetComponentsInChildren<MeshRenderer>())
                            {
                                renderer.enabled=false;var mesh=MeshOf(renderer);var materials=renderer.sharedMaterials;
                                for(int sub=0;sub<mesh.subMeshCount;sub++)
                                    if(sub<materials.Length&&materials[sub])preview.DrawMesh(mesh,renderer.localToWorldMatrix,materials[sub],sub);
                            }
                            preview.Render(true,false);texture=preview.EndPreview();previewOpen=false;
                        }
                        finally{if(previewOpen)preview.EndPreview();}
                        var rt=RenderTexture.GetTemporary(384,384,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var previous=RenderTexture.active;
                        try{Graphics.Blit(texture,rt);RenderTexture.active=rt;pixels=new Texture2D(384,384,TextureFormat.RGB24,false,false);pixels.ReadPixels(new Rect(0,0,384,384),0,0);pixels.Apply();}
                        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);}
                        var colors=pixels.GetPixels32();var bg=colors[0];int changed=0;
                        foreach(var color in colors)if(Math.Abs(color.r-bg.r)+Math.Abs(color.g-bg.g)+Math.Abs(color.b-bg.b)>25)changed++;
                        e.previewCoverage=changed/(float)colors.Length;
                        e.previewStatus=e.previewCoverage<.001f?"EMPTY_REVIEW":"rendered";
                        File.WriteAllBytes(Path.Combine(UnityRoot,e.preview),pixels.EncodeToPNG());ok++;
                    }
                    catch(Exception ex){e.previewStatus="FAIL: "+ex.GetType().Name+": "+ex.Message;failed++;}
                    finally{if(pixels)Object.DestroyImmediate(pixels);if(geometry)Object.DestroyImmediate(geometry);}
                }
            }
            finally{preview.Cleanup();ShaderUtil.allowAsyncCompilation=previousAsync;Save();EditorUtility.UnloadUnusedAssetsImmediate();}
            return $"Preview batch {start}..{Mathf.Min(start+count,catalog.entries.Count)-1}: rendered={ok}, failed={failed}; total rendered={catalog.entries.Count(e=>e.previewStatus=="rendered")}/{catalog.entries.Count}";
        }
        public static string PreviewAlternates(int index)
        {
            if(catalog==null)Load();var best=catalog.entries[index];byte[] bestBytes=File.Exists(Path.Combine(UnityRoot,best.preview))?File.ReadAllBytes(Path.Combine(UnityRoot,best.preview)):null;
            float bestCoverage=best.previewCoverage;var images=new List<string>();
            var directions=new[]{new Vector3(0,.15f,-1),new Vector3(0,.15f,1),new Vector3(1,.15f,0),new Vector3(-1,.15f,0),new Vector3(.55f,1,.55f),new Vector3(.55f,-1,.55f)};
            try
            {
                for(int angle=0;angle<directions.Length;angle++)
                {
                    previewDirection=directions[angle];PreviewBatch(index,1);var entry=catalog.entries[index];
                    if(entry.kind=="animation_clip_source"||!File.Exists(Path.Combine(UnityRoot,entry.preview)))continue;
                    string variant="Screenshots/AssetCatalog/Thumbnails/"+entry.guid+"_view"+angle+".png";
                    File.Copy(Path.Combine(UnityRoot,entry.preview),Path.Combine(UnityRoot,variant),true);images.Add(variant);
                    if(entry.previewCoverage>bestCoverage){best=entry;bestCoverage=entry.previewCoverage;bestBytes=File.ReadAllBytes(Path.Combine(UnityRoot,entry.preview));}
                }
            }
            finally
            {
                previewDirection=new Vector3(1,.55f,-1);best.alternatePreviews=images.ToArray();catalog.entries[index]=best;
                if(bestBytes!=null)File.WriteAllBytes(Path.Combine(UnityRoot,best.preview),bestBytes);Save();
            }
            return "Alternate views index="+index+" best coverage="+bestCoverage.ToString("F4")+" status="+best.previewStatus;
        }
        static GameObject CopyGeometry(GameObject source,out Bounds bounds)
        {
            var root=new GameObject("AssetCatalog_GeometryOnly");bounds=default;bool first=true;
            var excluded=new HashSet<Renderer>();
            foreach(var group in source.GetComponentsInChildren<LODGroup>(true))
                foreach(var lod in group.GetLODs().Skip(1))foreach(var r in lod.renderers)if(r)excluded.Add(r);
            foreach(var r in source.GetComponentsInChildren<Renderer>(true))
            {
                if(excluded.Contains(r))continue;var mesh=MeshOf(r);if(!mesh)continue;
                var matrix=source.transform.worldToLocalMatrix*r.localToWorldMatrix;
                var go=new GameObject(r.name);go.transform.SetParent(root.transform,false);
                go.transform.localPosition=matrix.GetColumn(3);go.transform.localRotation=matrix.rotation;go.transform.localScale=matrix.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=r.sharedMaterials;
                renderer.shadowCastingMode=ShadowCastingMode.TwoSided;renderer.receiveShadows=true;
                var b=TransformedBounds(mesh.bounds,matrix);if(first){bounds=b;first=false;}else bounds.Encapsulate(b);
            }
            if(first){Object.DestroyImmediate(root);throw new InvalidOperationException("No renderable geometry");}return root;
        }
        static Mesh MeshOf(Renderer r)=>r is SkinnedMeshRenderer skinned?skinned.sharedMesh:r.GetComponent<MeshFilter>()?.sharedMesh;
        static long Triangles(Mesh mesh){if(!mesh)return 0;long count=0;for(int s=0;s<mesh.subMeshCount;s++)if(mesh.GetTopology(s)==MeshTopology.Triangles)count+=(long)mesh.GetIndexCount(s)/3;return count;}
        static Bounds TransformedBounds(Bounds b,Matrix4x4 m)
        {var x=m.MultiplyVector(new Vector3(b.extents.x,0,0));var y=m.MultiplyVector(new Vector3(0,b.extents.y,0));var z=m.MultiplyVector(new Vector3(0,0,b.extents.z));var e=new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z));return new Bounds(m.MultiplyPoint3x4(b.center),e*2);}
        static void Load()=>catalog=JsonUtility.FromJson<Catalog>(File.ReadAllText(Path.Combine(ReportFolder,"ModelCatalog.json")));
        static string Csv(string value)=>"\""+(value??"").Replace("\"","\"\"")+"\"";
        static void Save()
        {
            Directory.CreateDirectory(ReportFolder);File.WriteAllText(Path.Combine(ReportFolder,"ModelCatalog.json"),JsonUtility.ToJson(catalog,true),new UTF8Encoding(false));
            var csv=new StringBuilder("index,kind,pack,name,label,category,placement,confidence,path,guid,renderers,uniqueMeshes,allLodTriangles,highestLodTriangles,colliders,missingScripts,missingMesh,missingMaterial,brokenMaterials,sizeX,sizeY,sizeZ,preview,previewStatus\n");
            foreach(var e in catalog.entries)csv.AppendLine(string.Join(",",new[]{e.index.ToString(),Csv(e.kind),Csv(e.pack),Csv(e.name),Csv(e.label),Csv(e.category),Csv(e.placement),Csv(e.confidence),Csv(e.path),Csv(e.guid),e.renderers.ToString(),e.meshes.ToString(),e.triangles.ToString(),e.highestLodTriangles.ToString(),e.colliders.ToString(),e.missingScripts.ToString(),e.missingMesh.ToString(),e.missingMaterial.ToString(),e.brokenMaterials.ToString(),(e.size?[0]??0).ToString("G",CultureInfo.InvariantCulture),(e.size?[1]??0).ToString("G",CultureInfo.InvariantCulture),(e.size?[2]??0).ToString("G",CultureInfo.InvariantCulture),Csv(e.preview),Csv(e.previewStatus)}));
            File.WriteAllText(Path.Combine(ReportFolder,"ModelCatalog.csv"),csv.ToString(),new UTF8Encoding(true));
        }
        static string Summary()
        {
            var lines=new List<string>{$"Catalog: {catalog.prefabs} prefabs; {catalog.modelFiles} model files; {catalog.modelsWithoutPrefab} models without prefab; {catalog.entries.Count} entries; {catalog.materials.Count} materials"};
            lines.AddRange(catalog.materials.GroupBy(m=>m.status).Select(g=>g.Key+"="+g.Count()));
            lines.Add($"Missing scripts={catalog.entries.Sum(e=>e.missingScripts)}, mesh={catalog.entries.Sum(e=>e.missingMesh)}, material slots={catalog.entries.Sum(e=>e.missingMaterial)}");
            return string.Join("\n",lines);
        }
    }
}
