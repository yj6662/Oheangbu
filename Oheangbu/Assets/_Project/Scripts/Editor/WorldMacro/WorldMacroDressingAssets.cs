using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Source-preserving palette preparation. Temporary single-tree thumbnails run serially.</summary>
    public static partial class WorldMacroDressingAssets
    {
        const string Source="Assets/SeyeonjeongPavilion/Prefabs/";
        static string scopedFolder;
        static string Folder=>scopedFolder??WorldMacroDressingAuthoring.Folder;
        static readonly string[][] Trees={
            new[]{"SM_Henonis_1","SM_UlmusDavidiana_Summer_2","SM_PinusDensiflora_Spring_2"},
            new[]{"SM_UlmusDavidiana_Summer_2","SM_PinusDensiflora_Spring_2"},
            new[]{"SM_MeliaAzedarach_Winter_1","SM_PinusDensiflora_Spring_2"},
            new[]{"SM_PinusDensiflora_Spring_2","SM_MeliaAzedarach_Winter_1"},
            new[]{"SM_Salixpierotii_Summer_1","SM_UlmusDavidiana_Summer_2"}};
        static readonly Color[] Tints={new Color(.66f,.76f,.62f),new Color(.76f,.73f,.61f),new Color(.77f,.61f,.53f),new Color(.62f,.69f,.71f),new Color(.59f,.7f,.71f)};
        public static bool IsComplete(Sheet sheet)=>sheet.PaletteVersion==3&&sheet.Prototypes.Length==48;
        static void ReleaseSourceLoads(Sheet sheet)
        {
            // Unity's native unload does not regard this method's managed stack as an asset root.
            // Pin the working sheet (and its serialized dependencies) only during unloading;
            // never save the temporary flag into the source asset.
            var flags=sheet.hideFlags;
            try{sheet.hideFlags=flags|HideFlags.DontUnloadUnusedAsset;GC.Collect();EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();}
            finally{if(sheet!=null)sheet.hideFlags=flags;}
        }
        public static void Prepare(Sheet sheet)
        {
            if(Shader.Find("Oheangbu/WorldMacroVegetation")==null)throw new InvalidOperationException("Import WorldMacroVegetation shader before palette preparation.");
            Directory.CreateDirectory(Folder+"/Materials");Directory.CreateDirectory(Folder+"/Billboards");Directory.CreateDirectory(Folder+"/Meshes");Directory.CreateDirectory(Folder+"/Textures");
            var prototypes=new List<Sheet.Prototype>(sheet.Prototypes);
            // A mesh subasset keeps its original FBX dependency graph reachable. Migrate saved
            // partial work as well as new parts to standalone, geometry-identical mesh assets.
            foreach(var prototype in prototypes)
            {
                if(UsesMeshCopies(prototype))continue;
                if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Palette paused before mesh migration: commit >=85%. Saved copies will be reused.");
                foreach(var level in prototype.Lods)foreach(var part in level.Parts)part.Mesh=MeshCopy(part.Mesh);
                EditorUtility.SetDirty(sheet);AssetDatabase.SaveAssets();ReleaseSourceLoads(sheet);
            }
            ReleaseSourceLoads(sheet);
            for(int realm=0;realm<5;realm++)
            {
                foreach(var name in Trees[realm])Add(Source+name+".prefab",realm,Sheet.Kind.Tree);
                Add(Source+"SM_Deparia_1.prefab",realm,Sheet.Kind.Shrub);
                if(realm==4)Add(Source+"SM_PhragmitesAustralis_2.prefab",realm,Sheet.Kind.Shrub);
                if(realm==0)Add(Source+"SM_Henonis_2.prefab",realm,Sheet.Kind.Shrub);
                Add(Source+"SM_Grass.prefab",realm,Sheet.Kind.Grass);
                foreach(var name in new[]{"SM_Rock_L","SM_Rock_K"})Add(Source+name+".prefab",realm,Sheet.Kind.Rock);
                foreach(var path in new[]{"Assets/HwaseongHaenggung/Prefabs/SM_M_WoodLog.prefab","Assets/HwaseongHaenggung/Prefabs/SM_M_WoodenBox.prefab","Assets/Korea_TreasureProps/Prefabs/SM_052_Pot.prefab"})Add(path,realm,Sheet.Kind.Prop);
            }
            sheet.PaletteVersion=3;EditorUtility.SetDirty(sheet);AssetDatabase.SaveAssets();
            void Add(string path,int realm,Sheet.Kind category)
            {
                string name=Path.GetFileNameWithoutExtension(path),id=((RealmId)realm)+"_"+name;
                var existing=prototypes.FirstOrDefault(p=>p.Id==id);if(existing!=null&&UsesTextureCopies(existing))return;
                if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Palette paused before new source: commit >=85%. Saved "+prototypes.Count+" prototypes will be reused.");
                var reusable=prototypes.FirstOrDefault(p=>p.SourcePath==path&&p.Category==category&&UsesTextureCopies(p));
                var created=reusable!=null?ClonePrototype(reusable,realm):category==Sheet.Kind.Prop?PrepareProp(sheet,path,realm):PrepareSource(sheet,name,realm,category);
                if(existing!=null)prototypes.Remove(existing);prototypes.Add(created);sheet.Prototypes=prototypes.ToArray();EditorUtility.SetDirty(sheet);AssetDatabase.SaveAssets();
                // No source FBX, prefab or material is retained: prototypes reference owned mesh and texture copies.
                if(reusable==null)ReleaseSourceLoads(sheet);
            }
        }
        static bool UsesMeshCopies(Sheet.Prototype p)=>p.Lods.Length>0&&p.Lods.All(l=>l.Parts.All(part=>part.Mesh!=null&&AssetDatabase.GetAssetPath(part.Mesh).StartsWith(Folder+"/Meshes/",StringComparison.Ordinal)));
        static Mesh MeshCopy(Mesh source)
        {
            if(source==null)throw new InvalidOperationException("Source mesh missing while preparing independent dressing geometry.");
            if(AssetDatabase.GetAssetPath(source).StartsWith(Folder+"/Meshes/",StringComparison.Ordinal))return source;
            if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long localId))throw new InvalidOperationException("Source mesh has no stable asset identity: "+source.name);
            string path=Folder+"/Meshes/"+guid+"_"+localId+".asset";
            var copy=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(copy!=null)return copy;
            // Native cloning preserves topology, submeshes, UVs, normals, tangents, vertex colours
            // and all other existing channels. No decimation or normalization is performed here.
            copy=Object.Instantiate(source);copy.name=source.name;copy.hideFlags=HideFlags.None;
            try{AssetDatabase.CreateAsset(copy,path);return copy;}
            catch{Object.DestroyImmediate(copy);throw;}
        }
        static bool UsesTextureCopies(Sheet.Prototype p)=>p.Lods.Length>0&&p.Lods.All(l=>l.Parts.All(part=>part.Material!=null&&(AssetDatabase.GetAssetPath(part.Material.GetTexture("_BaseMap")).StartsWith(Folder+"/Textures/",StringComparison.Ordinal)||AssetDatabase.GetAssetPath(part.Material.GetTexture("_BaseMap")).StartsWith(Folder+"/Billboards/",StringComparison.Ordinal))));
        static Sheet.Prototype ClonePrototype(Sheet.Prototype source,int realm,string idOverride=null)
        {
            var p=new Sheet.Prototype{Id=idOverride??((RealmId)realm)+"_"+Path.GetFileNameWithoutExtension(source.SourcePath),SourcePath=source.SourcePath,SourceHash=source.SourceHash,Realm=(RealmId)realm,Category=source.Category,Weight=source.Weight,Scale=source.Scale,Size=source.Size,Radius=source.Radius,WetBank=source.WetBank,GroundPatch=source.GroundPatch,GroundPoints=source.GroundPoints,BillboardViews=source.BillboardViews,MaximumAltitude=source.MaximumAltitude,MaximumSlope=source.MaximumSlope,PolishVersion=source.PolishVersion,Lods=new Sheet.Level[source.Lods.Length]};
            for(int lod=0;lod<p.Lods.Length;lod++)
            {
                var parts=new List<Sheet.Part>();for(int i=0;i<source.Lods[lod].Parts.Length;i++)
                {
                    var part=source.Lods[lod].Parts[i];string path=Folder+"/Materials/"+p.Id+"_Cached_"+lod+"_"+i+".mat";
                    var mat=AssetDatabase.LoadAssetAtPath<Material>(path);bool create=mat==null;if(create)mat=new Material(part.Material);else mat.CopyPropertiesFromMaterial(part.Material);
                    mat.name=p.Id+"_Cached_"+lod+"_"+i;mat.enableInstancing=true;
                    float strength=p.Category==Sheet.Kind.Tree&&lod>=2?.25f:.45f;var previous=Color.Lerp(Color.white,Tints[(int)source.Realm],strength);var desired=Color.Lerp(Color.white,Tints[realm],strength);var colour=part.Material.GetColor("_BaseColor");
                    mat.SetColor("_BaseColor",new Color(colour.r/previous.r*desired.r,colour.g/previous.g*desired.g,colour.b/previous.b*desired.b,colour.a));
                    if(realm==2&&source.SourcePath.Contains("WoodLog"))mat.SetColor("_BaseColor",new Color(.2f,.16f,.13f));
                    if(create)AssetDatabase.CreateAsset(mat,path);else EditorUtility.SetDirty(mat);parts.Add(new Sheet.Part{Mesh=part.Mesh,Submesh=part.Submesh,Material=mat,Local=part.Local});
                }
                p.Lods[lod]=new Sheet.Level{Parts=parts.ToArray()};
            }
            return p;
        }
        static Sheet.Prototype PrepareSource(Sheet settings,string name,int realm,Sheet.Kind kind,string explicitPath=null)
        {
            string path=explicitPath??Source+name+".prefab";var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(prefab==null)throw new FileNotFoundException("Missing reviewed environment source: "+path);
            var group=prefab.GetComponent<LODGroup>();
            var levels=group!=null?group.GetLODs():new[]{new LOD(0,prefab.GetComponentsInChildren<Renderer>(true))};
            if(levels.Length==0)throw new InvalidOperationException("Source has no usable render LOD: "+path);
            Bounds bounds=BoundsOf(levels[0].renderers,prefab.transform);
            var p=new Sheet.Prototype{Id=((RealmId)realm)+"_"+name,SourcePath=path,SourceHash=AssetDatabase.GetAssetDependencyHash(path).ToString(),Realm=(RealmId)realm,Category=kind,Size=bounds.size,
                Radius=kind==Sheet.Kind.Tree?Mathf.Clamp(bounds.size.y*.032f,.09f,.55f):Mathf.Max(bounds.size.x,bounds.size.z)*.5f,
                WetBank=name.Contains("Salix")||name.Contains("Phragmites"),Scale=kind==Sheet.Kind.Tree?new Vector2(.78f,1.3f):new Vector2(.7f,1.25f)};
            p.Lods=new Sheet.Level[kind==Sheet.Kind.Tree?4:2];
            Vector3 offset=new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z);
            for(int l=0;l<2;l++)
            {
                int sourceLod=kind==Sheet.Kind.Tree?l+2:kind==Sheet.Kind.Grass?l: l+1;
                var parts=new List<Sheet.Part>();
                foreach(var renderer in levels[Mathf.Min(sourceLod,levels.Length-1)].renderers)
                {
                    var f=renderer.GetComponent<MeshFilter>();if(f?.sharedMesh==null)continue;
                    var materials=renderer.sharedMaterials;
                    for(int sm=0;sm<f.sharedMesh.subMeshCount;sm++)
                    {
                        if(sm>=materials.Length||materials[sm]==null)throw new InvalidOperationException("Source material slot is missing: "+path);
                        var mat=CopyMaterial(materials[sm],realm,name,l,kind,bounds.size.y,settings);
                        parts.Add(new Sheet.Part{Mesh=MeshCopy(f.sharedMesh),Submesh=sm,Material=mat,Local=Matrix4x4.Translate(offset)*prefab.transform.worldToLocalMatrix*f.transform.localToWorldMatrix});
                    }
                }
                p.Lods[l]=new Sheet.Level{Parts=parts.ToArray()};
            }
            if(kind==Sheet.Kind.Tree)
            {
                float span=Mathf.Max(bounds.size.x,bounds.size.y)*1.15f;
                var image=Billboard(name,p.Lods[0],span);
                for(int l=2;l<4;l++)
                {
                    string target=Folder+"/Materials/"+p.Id+"_Billboard"+l+".mat";
                    var mat=AssetDatabase.LoadAssetAtPath<Material>(target);bool create=mat==null;
                    if(create)mat=new Material(Shader.Find("Oheangbu/WorldMacroVegetation"));
                    mat.name=p.Id+"_Billboard"+l;mat.enableInstancing=true;mat.SetTexture("_BaseMap",image);mat.SetColor("_BaseColor",Color.Lerp(Color.white,Tints[realm],.25f));
                    mat.SetFloat("_Billboard",1);mat.SetFloat("_AlphaClip",1);mat.SetFloat("_Cutoff",.32f);mat.SetFloat("_Cull",0);mat.SetFloat("_BumpScale",0);
                    mat.SetFloat("_WindAmplitude",.045f);mat.SetFloat("_WindSpeed",.75f);mat.SetFloat("_Height",span);mat.SetFloat("_Saturation",.52f);mat.SetFloat("_AmbientFloor",.8f);mat.SetFloat("_LightResponse",.1f);
                    SetRange(mat,l==2?settings.TreeMiddle-20:settings.TreeDistance-100,l==2?settings.TreeMiddle+20:settings.TreeDistance,l==2?settings.TreeDistance-100:settings.ForestDistance-350,l==2?settings.TreeDistance:settings.ForestDistance);
                    if(create)AssetDatabase.CreateAsset(mat,target);else EditorUtility.SetDirty(mat);
                    p.Lods[l]=new Sheet.Level{Parts=new[]{new Sheet.Part{Mesh=Quad(),Material=mat,Submesh=0,Local=Matrix4x4.Scale(new Vector3(span,span,1))}}};
                }
            }
            return p;
        }
        static Sheet.Prototype PrepareProp(Sheet settings,string path,int realm)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(prefab==null)throw new FileNotFoundException(path);
            var renderers=prefab.GetComponentsInChildren<MeshRenderer>(true);var bounds=BoundsOf(renderers,prefab.transform);string name=Path.GetFileNameWithoutExtension(path);
            float size=name.Contains("WoodLog")?2.3f:name.Contains("Box")?.85f:.65f;float normalization=size/Mathf.Max(.001f,Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z));
            var p=new Sheet.Prototype{Id=((RealmId)realm)+"_"+name,SourcePath=path,SourceHash=AssetDatabase.GetAssetDependencyHash(path).ToString(),Realm=(RealmId)realm,Category=Sheet.Kind.Prop,Size=bounds.size*normalization,Radius=Mathf.Max(bounds.size.x,bounds.size.z)*normalization*.5f,Scale=new Vector2(.9f,1.1f)};
            var parts=new List<Sheet.Part>();Vector3 offset=new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z);
            foreach(var renderer in renderers)
            {
                var f=renderer.GetComponent<MeshFilter>();if(f?.sharedMesh==null)continue;
                for(int sm=0;sm<f.sharedMesh.subMeshCount;sm++)
                {
                    if(sm>=renderer.sharedMaterials.Length||renderer.sharedMaterials[sm]==null)throw new InvalidOperationException("Prop material slot missing: "+path);
                    var mat=CopyMaterial(renderer.sharedMaterials[sm],realm,name,0,Sheet.Kind.Prop,p.Size.y,settings);
                    // Dry ash-site timbers, military packing boxes and domestic jars retain their original textures.
                    if(realm==2&&name.Contains("WoodLog"))mat.SetColor("_BaseColor",new Color(.2f,.16f,.13f));
                    parts.Add(new Sheet.Part{Mesh=MeshCopy(f.sharedMesh),Submesh=sm,Material=mat,Local=Matrix4x4.Scale(Vector3.one*normalization)*Matrix4x4.Translate(offset)*prefab.transform.worldToLocalMatrix*f.transform.localToWorldMatrix});
                }
            }
            p.Lods=new[]{new Sheet.Level{Parts=parts.ToArray()},new Sheet.Level{Parts=parts.ToArray()}};return p;
        }
        static Bounds BoundsOf(Renderer[] renderers,Transform root)
        {
            bool first=true;Bounds result=default;
            foreach(var renderer in renderers){var f=renderer.GetComponent<MeshFilter>();if(f?.sharedMesh==null)continue;var b=f.sharedMesh.bounds;
                for(int i=0;i<8;i++){var local=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));var point=root.InverseTransformPoint(f.transform.TransformPoint(local));if(first){result=new Bounds(point,Vector3.zero);first=false;}else result.Encapsulate(point);}}
            if(first)throw new InvalidOperationException("Empty source render bounds");return result;
        }
        static Material CopyMaterial(Material source,int realm,string name,int lod,Sheet.Kind kind,float height,Sheet settings)
        {
            string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));string path=Folder+"/Materials/"+realm+"_"+name+"_"+guid+"_"+lod+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);bool create=mat==null;if(create)mat=new Material(Shader.Find("Oheangbu/WorldMacroVegetation"));
            mat.shader=Shader.Find("Oheangbu/WorldMacroVegetation");mat.enableInstancing=true;
            string texture=source.HasProperty("_BaseMap")?"_BaseMap":"_MainTex";
            if(source.HasProperty(texture)){mat.SetTexture("_BaseMap",TextureCopy(source.GetTexture(texture),kind==Sheet.Kind.Grass?512:1024,false));mat.SetTextureScale("_BaseMap",source.GetTextureScale(texture));mat.SetTextureOffset("_BaseMap",source.GetTextureOffset(texture));}
            if(source.HasProperty("_BumpMap"))mat.SetTexture("_BumpMap",TextureCopy(source.GetTexture("_BumpMap"),kind==Sheet.Kind.Grass?512:1024,true));
            bool clipped=source.IsKeywordEnabled("_ALPHATEST_ON")||(source.HasProperty("_AlphaClip")&&source.GetFloat("_AlphaClip")>.5f);
            mat.SetFloat("_AlphaClip",clipped?1:0);mat.SetFloat("_Cutoff",source.HasProperty("_Cutoff")?source.GetFloat("_Cutoff"):.4f);mat.SetFloat("_Cull",clipped?0:2);
            var colour=source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):Color.white;
            mat.SetColor("_BaseColor",colour*Color.Lerp(Color.white,Tints[realm],.45f));
            mat.SetFloat("_Saturation",.4f);mat.SetFloat("_AmbientFloor",.36f);mat.SetFloat("_LightResponse",.65f);mat.SetFloat("_Height",height);
            mat.SetFloat("_WindAmplitude",clipped?(kind==Sheet.Kind.Grass?.06f:.095f):0);mat.SetFloat("_WindSpeed",.85f);mat.SetFloat("_Billboard",0);
            // Custom owned foliage has no _BaseMap/_MainTex. Read its verified numeric
            // source slots only for the opt-in derivative; provider/C2 materials are untouched.
            CopyYmcFoliageTextures(source,mat,kind==Sheet.Kind.Grass?512:1024);
            float limit=kind==Sheet.Kind.Tree?settings.TreeDistance:kind==Sheet.Kind.Shrub?settings.ShrubDistance:kind==Sheet.Kind.Grass?settings.GrassDistance:350;
            if(kind==Sheet.Kind.Tree)SetRange(mat,lod==0?-1:settings.TreeNear-10,lod==0?0:settings.TreeNear+10,lod==0?settings.TreeNear-10:settings.TreeMiddle-20,lod==0?settings.TreeNear+10:settings.TreeMiddle+20);
            else SetRange(mat,-1,0,limit-20,limit);
            if(create)AssetDatabase.CreateAsset(mat,path);else EditorUtility.SetDirty(mat);return mat;
        }
        static Texture TextureCopy(Texture source,int maximum,bool normal)
        {
            if(source==null)return null;string original=AssetDatabase.GetAssetPath(source);if(string.IsNullOrEmpty(original)||!File.Exists(original))return source;
            string path=Folder+"/Textures/"+AssetDatabase.AssetPathToGUID(original)+(normal?"_Normal_":"_Color_")+maximum+Path.GetExtension(original);
            if(!File.Exists(path))File.Copy(original,path,false);
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(texture==null)AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;if(importer==null)throw new InvalidOperationException("Texture copy importer missing: "+path);
            var kind=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            if(importer.maxTextureSize!=maximum||importer.textureType!=kind||importer.sRGBTexture==normal||importer.isReadable||!importer.mipmapEnabled)
            {
                var sourceImporter=AssetImporter.GetAtPath(original) as TextureImporter;if(sourceImporter!=null){var settings=new TextureImporterSettings();sourceImporter.ReadTextureSettings(settings);importer.SetTextureSettings(settings);}
                importer.textureType=kind;importer.sRGBTexture=!normal;importer.isReadable=false;importer.mipmapEnabled=true;importer.maxTextureSize=maximum;importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static void SetRange(Material mat,float a,float b,float c,float d){mat.SetFloat("_FadeInStart",a);mat.SetFloat("_FadeInEnd",b);mat.SetFloat("_FadeOutStart",c);mat.SetFloat("_FadeOutEnd",d);}
        static Mesh Quad()
        {
            string path=Folder+"/Meshes/UprightBillboard.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh!=null)return mesh;
            mesh=new Mesh{name="Dressing_UprightBillboard"};mesh.vertices=new[]{new Vector3(-.5f,0,0),new Vector3(.5f,0,0),new Vector3(-.5f,1,0),new Vector3(.5f,1,0)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.triangles=new[]{0,2,1,1,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        public static int PrepareNewMaterialPasses(IEnumerable<Material> materials)
        {
            bool asyncBefore=ShaderUtil.allowAsyncCompilation;int checkedPasses=0;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                foreach(var material in materials.Where(m=>m!=null&&m.shader!=null&&m.shader.name=="Oheangbu/WorldMacroVegetation").Distinct())
                foreach(string passName in new[]{"ForwardLit","ShadowCaster","DepthOnly","DepthNormals","DepthNormalsOnly"})
                {
                    int pass=material.FindPass(passName);if(pass<0||!material.GetShaderPassEnabled(passName))continue;
                    if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Dressing shader preparation paused: system commit >=85%.");
                    if(!ShaderUtil.IsPassCompiled(material,pass))ShaderUtil.CompilePass(material,pass,true);
                    if(!ShaderUtil.IsPassCompiled(material,pass))throw new InvalidOperationException("Dressing shader variant remains unready: "+material.name+" / "+passName);
                    checkedPasses++;
                }
                return checkedPasses;
            }
            finally{ShaderUtil.allowAsyncCompilation=asyncBefore;}
        }
        static Texture2D Billboard(string name,Sheet.Level level,float span)
        {
            string path=Folder+"/Billboards/"+name+".png";var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(existing!=null)return existing;
            if(File.Exists(path))
            {
                // A interrupted import must reuse the image already captured, never silently replace it.
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(existing==null)throw new InvalidOperationException("Existing billboard could not be imported; preserved: "+path);return existing;
            }
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("No billboard render allocated: system commit >=85%. Prepared assets retained; rerun after cleanup.");
            GameObject root=null,camGo=null;RenderTexture rt=null;Texture2D image=null;var mats=new List<Material>();RenderTexture prior=RenderTexture.active;
            bool asyncBefore=ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;root=new GameObject("Dressing_BillboardTemporary"){hideFlags=HideFlags.HideAndDontSave,layer=31};
                foreach(var part in level.Parts)
                {
                    var go=new GameObject("Part"){hideFlags=HideFlags.HideAndDontSave,layer=31};go.transform.SetParent(root.transform,false);
                    go.transform.localPosition=part.Local.GetColumn(3);go.transform.localRotation=part.Local.rotation;go.transform.localScale=part.Local.lossyScale;
                    go.AddComponent<MeshFilter>().sharedMesh=part.Mesh;var renderer=go.AddComponent<MeshRenderer>();var material=new Material(part.Material){hideFlags=HideFlags.HideAndDontSave};
                    material.SetColor("_BaseColor",Color.white);material.SetFloat("_WindAmplitude",0);material.SetFloat("_Saturation",.8f);material.SetFloat("_AmbientFloor",.65f);SetRange(material,-1,0,99990,99999);mats.Add(material);
                    // One renderer per submesh; transparent unused slots prevent duplicate material rendering.
                    var slots=new Material[part.Mesh.subMeshCount];for(int i=0;i<slots.Length;i++){if(i==part.Submesh)slots[i]=material;else{var hidden=new Material(material){hideFlags=HideFlags.HideAndDontSave};hidden.SetFloat("_AlphaClip",1);hidden.SetColor("_BaseColor",new Color(0,0,0,0));mats.Add(hidden);slots[i]=hidden;}}
                    renderer.sharedMaterials=slots;renderer.shadowCastingMode=ShadowCastingMode.Off;
                }
                camGo=new GameObject("Dressing_BillboardCamera"){hideFlags=HideFlags.HideAndDontSave};var camera=camGo.AddComponent<Camera>();camera.enabled=false;
                camera.orthographic=true;camera.orthographicSize=span*.5f;camera.aspect=1;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.cullingMask=1<<31;
                camera.nearClipPlane=.1f;camera.farClipPlane=span*5;camera.transform.position=new Vector3(0,span*.5f,-span*2);camera.transform.rotation=Quaternion.identity;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
                PrepareNewMaterialPasses(mats);
                rt=new RenderTexture(384,384,24,RenderTextureFormat.ARGB32);image=new Texture2D(384,384,TextureFormat.RGBA32,false);
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,384,384),0,0);image.Apply(false);
                byte[] png=image.EncodeToPNG();using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))file.Write(png,0,png.Length);camera.targetTexture=null;
            }
            finally{ShaderUtil.allowAsyncCompilation=asyncBefore;RenderTexture.active=prior;if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(image!=null)Object.DestroyImmediate(image);if(camGo!=null)Object.DestroyImmediate(camGo);if(root!=null)Object.DestroyImmediate(root);foreach(var mat in mats)Object.DestroyImmediate(mat);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.alphaIsTransparency=true;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=512;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
