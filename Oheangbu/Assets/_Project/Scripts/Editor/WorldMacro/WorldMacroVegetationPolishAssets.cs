using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroDressingAssets
    {
        public const string PolishFolder="Assets/_Project/Art/World/WorldMacro/Playtest/VegetationPolish";
        public static void PreparePolish(Sheet sheet,int sourceBudget=1)
        {
            string before=scopedFolder;scopedFolder=PolishFolder;
            try
            {
                foreach(string child in new[]{"Materials","Meshes","Textures","Billboards"})Directory.CreateDirectory(Folder+"/"+child);
                int processed=0;bool pending=false;
                var originals=sheet.Prototypes.ToArray();var result=new List<Sheet.Prototype>();
                foreach(var original in originals)
                {
                    if(original.PolishVersion==1){result.Add(original);continue;}
                    if(processed>=sourceBudget)return;GuardPolish();processed++;
                    var p=CopyPolishPrototype(original);
                    if(p.Category==Sheet.Kind.Tree)MakeDirectionalCards(sheet,p);
                    if(p.Category==Sheet.Kind.Grass&&!p.GroundPatch)MakeGrassPatch(sheet,p);
                    ConfigurePolishRanges(sheet,p);p.PolishVersion=1;result.Add(p);
                    sheet.Prototypes=result.Concat(originals.Skip(result.Count)).ToArray();EditorUtility.SetDirty(sheet);AssetDatabase.SaveAssets();
                }
                sheet.Prototypes=result.ToArray();
                for(int realm=0;realm<5;realm++)
                {
                    // Existing owned sources: neither new generation nor substitutes for these plants.
                    Add("Assets/HwaseongHaenggung/Prefabs/SM_Grass.prefab",realm,Sheet.Kind.Grass);
                    Add("Assets/YongmeoriCoast/Prefabs/SM_YMC_Grass_01_H.prefab",realm,Sheet.Kind.Grass);
                    Add("Assets/SeyeonjeongPavilion/Prefabs/SM_Deparia_3.prefab",realm,Sheet.Kind.Shrub);
                    if(realm==0||realm==1)Add("Assets/HwaseongHaenggung/Prefabs/SM_Bush.prefab",realm,Sheet.Kind.Shrub);
                    if(realm==2||realm==3)Add("Assets/YongmeoriCoast/Prefabs/SM_YMC_Bush_01_Small.prefab",realm,Sheet.Kind.Shrub);
                    if(realm==4)Add("Assets/SeyeonjeongPavilion/Prefabs/SM_PhragmitesAustralis_1.prefab",realm,Sheet.Kind.Shrub);
                }
                sheet.PaletteVersion=pending?3:4;EditorUtility.SetDirty(sheet);AssetDatabase.SaveAssets();
                void Add(string path,int realm,Sheet.Kind kind)
                {
                    string name=Path.GetFileNameWithoutExtension(path);
                    if(path.Contains("HwaseongHaenggung/")&&name=="SM_Grass")name="SM_Grass_Haenggung";
                    string id=((Oheangbu.Data.World.RealmId)realm)+"_"+name;
                    if(result.Any(p=>p.Id==id))return;if(processed>=sourceBudget){pending=true;return;}GuardPolish();processed++;
                    var prior=result.FirstOrDefault(p=>p.SourcePath==path);
                    var p=prior!=null?ClonePrototype(prior,realm,id):PrepareSource(sheet,name,realm,kind,path);
                    // Real prefab scale is recorded before intentional size variation; reject broken units.
                    if(p.Size.y<=.01f||p.Size.y>12||p.Size.x>25||p.Size.z>25)throw new InvalidOperationException("Review source physical scale before using "+path+": "+p.Size);
                    if(kind==Sheet.Kind.Grass&&!p.GroundPatch)MakeGrassPatch(sheet,p);
                    p.MaximumAltitude=kind==Sheet.Kind.Grass?560:realm==0?430:realm==4?330:580;
                    p.MaximumSlope=kind==Sheet.Kind.Grass?40:36;
                    p.Weight=kind==Sheet.Kind.Grass?.7f:.65f;
                    if(name.Contains("Phragmites"))p.WetBank=true;
                    ConfigurePolishRanges(sheet,p);p.PolishVersion=1;result.Add(p);sheet.Prototypes=result.ToArray();EditorUtility.SetDirty(sheet);AssetDatabase.SaveAssets();
                    ReleaseSourceLoads(sheet);
                }
            }
            finally{scopedFolder=before;}
        }
        static void GuardPolish()
        {if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Vegetation preparation paused: system commit >=85%; completed assets are reusable.");}
        static Sheet.Prototype CopyPolishPrototype(Sheet.Prototype source)
        {
            var p=new Sheet.Prototype{Id=source.Id,SourcePath=source.SourcePath,SourceHash=source.SourceHash,Realm=source.Realm,Category=source.Category,Weight=source.Weight,Scale=source.Scale,Size=source.Size,Radius=source.Radius,WetBank=source.WetBank,GroundPoints=source.GroundPoints,GroundPatch=source.GroundPatch,BillboardViews=source.BillboardViews,PolishVersion=source.PolishVersion,MaximumAltitude=source.MaximumAltitude,MaximumSlope=source.MaximumSlope,Lods=new Sheet.Level[source.Lods.Length]};
            for(int lod=0;lod<p.Lods.Length;lod++)p.Lods[lod]=new Sheet.Level{Parts=source.Lods[lod].Parts.Select((part,i)=>new Sheet.Part{Mesh=part.Mesh,Submesh=part.Submesh,Local=part.Local,Material=PolishMaterial(part.Material,p.Id+"_L"+lod+"_P"+i)}).ToArray()};
            return p;
        }
        static Material PolishMaterial(Material source,string name)
        {
            string path=Folder+"/Materials/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null){mat=new Material(source){name=name,enableInstancing=true};AssetDatabase.CreateAsset(mat,path);}return mat;
        }
        static Material CardMaterial(Sheet.Prototype p,string suffix,Texture texture,bool upright,int views)
        {
            string path=Folder+"/Materials/"+p.Id+suffix+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null){mat=new Material(Shader.Find("Oheangbu/WorldMacroVegetation")){name=p.Id+suffix};AssetDatabase.CreateAsset(mat,path);}
            mat.enableInstancing=true;mat.SetTexture("_BaseMap",texture);mat.SetColor("_BaseColor",Color.Lerp(Color.white,Tints[(int)p.Realm],.35f));
            mat.SetFloat("_Billboard",upright?1:0);mat.SetFloat("_BillboardViews",views);mat.SetFloat("_SimpleLighting",1);mat.SetFloat("_AmbientFloor",.85f);
            mat.SetFloat("_AlphaClip",1);mat.SetFloat("_Cutoff",upright?.34f:.42f);mat.SetFloat("_Cull",0);mat.SetFloat("_BumpScale",0);mat.SetFloat("_Saturation",.48f);
            mat.SetFloat("_WindAmplitude",p.Category==Sheet.Kind.Tree?.04f:.03f);mat.SetFloat("_WindSpeed",.8f);mat.SetFloat("_Height",p.Size.y);EditorUtility.SetDirty(mat);return mat;
        }
        static void MakeDirectionalCards(Sheet settings,Sheet.Prototype p)
        {
            float span=Mathf.Max(p.Size.y,Mathf.Max(p.Size.x,p.Size.z))*1.08f;
            // Texture shared by regional material variants; the prototype's original texture/mesh is retained.
            string sourceKey=AssetDatabase.AssetPathToGUID(p.SourcePath);
            var atlas=DirectionalAtlas(sourceKey+"_Views4",p.Lods[0],span,4,false);
            var levels=new List<Sheet.Level>{p.Lods[0],p.Lods[1]};
            for(int lod=2;lod<4;lod++)
            {
                var mat=CardMaterial(p,lod==2?"_Directional":"_Canopy",atlas,true,4);
                levels.Add(new Sheet.Level{Parts=new[]{new Sheet.Part{Mesh=Quad(),Material=mat,Local=Matrix4x4.Scale(new Vector3(span,span,1))}}});
            }
            p.BillboardViews=4;p.Lods=levels.ToArray();
        }
        static void MakeGrassPatch(Sheet settings,Sheet.Prototype p)
        {
            var parts=new List<Sheet.Part>();Bounds bounds=default;bool first=true;
            float normalization=Mathf.Min(1,.65f/Mathf.Max(.01f,p.Size.y));
            for(int partIndex=0;partIndex<p.Lods[0].Parts.Length;partIndex++)
            {
                var part=p.Lods[0].Parts[partIndex];string path=Folder+"/Meshes/"+p.Id+"_GroundPatch_"+partIndex+".asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(mesh==null)
                {
                    var combines=new CombineInstance[4];
                    for(int n=0;n<4;n++)
                    {
                        float angle=n*137.51f,r=n==0?0:.36f;var offset=new Vector3(Mathf.Cos(angle*Mathf.Deg2Rad)*r,0,Mathf.Sin(angle*Mathf.Deg2Rad)*r);
                        combines[n]=new CombineInstance{mesh=part.Mesh,subMeshIndex=part.Submesh,transform=Matrix4x4.TRS(offset,Quaternion.Euler(0,angle,0),Vector3.one*(normalization*(.82f+n*.06f)))*part.Local};
                    }
                    mesh=new Mesh{name=p.Id+"_GroundPatch",indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(combines,true,true);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);
                }
                if(first){bounds=mesh.bounds;first=false;}else bounds.Encapsulate(mesh.bounds);
                parts.Add(new Sheet.Part{Mesh=mesh,Material=part.Material,Submesh=0});
            }
            var meshLevel=new Sheet.Level{Parts=parts.ToArray()};p.Size=bounds.size;p.Radius=Mathf.Max(bounds.extents.x,bounds.extents.z);p.GroundPatch=true;p.Scale=new Vector2(.85f,1.2f);
            float span=Mathf.Max(p.Size.y,Mathf.Max(p.Size.x,p.Size.z))*1.12f;
            var atlas=DirectionalAtlas(p.Id+"_Patch4",meshLevel,span,4,false);var top=DirectionalAtlas(p.Id+"_Cover",meshLevel,span,1,true);
            var card=CardMaterial(p,"_GroundCard",atlas,true,4);var cover=CardMaterial(p,"_GroundCover",top,false,1);cover.SetFloat("_WindAmplitude",0);
            p.Lods=new[]{meshLevel,new Sheet.Level{Parts=new[]{new Sheet.Part{Mesh=Quad(),Material=card,Local=Matrix4x4.Scale(new Vector3(span,span,1))}}},new Sheet.Level{Parts=new[]{new Sheet.Part{Mesh=GroundQuad(),Material=cover}}}};
            p.MaximumAltitude=560;p.MaximumSlope=40;
        }
        static Mesh GroundQuad()
        {
            string path=Folder+"/Meshes/GroundCoverQuad.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh!=null)return mesh;
            mesh=new Mesh{name="OwnedGrass_TopViewPatch"};mesh.vertices=new[]{new Vector3(-.5f,0,-.5f),new Vector3(.5f,0,-.5f),new Vector3(-.5f,0,.5f),new Vector3(.5f,0,.5f)};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.triangles=new[]{0,2,1,2,3,1};mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static void ConfigurePolishRanges(Sheet sheet,Sheet.Prototype p)
        {
            for(int l=0;l<p.Lods.Length;l++)foreach(var part in p.Lods[l].Parts)
            {
                var m=part.Material;
                if(p.Category==Sheet.Kind.Tree)
                {
                    if(l==0)SetRange(m,-1,0,sheet.TreeNear-10,sheet.TreeNear+10);
                    if(l==1)SetRange(m,sheet.TreeNear-10,sheet.TreeNear+10,sheet.TreeMiddle-20,sheet.TreeMiddle+20);
                    if(l==2)SetRange(m,sheet.TreeMiddle-20,sheet.TreeMiddle+20,sheet.TreeDistance-100,sheet.TreeDistance);
                    if(l==3)SetRange(m,sheet.TreeDistance-100,sheet.TreeDistance,sheet.ForestDistance-350,sheet.ForestDistance);
                }
                else if(p.Category==Sheet.Kind.Grass&&p.GroundPatch)
                {
                    if(l==0)SetRange(m,-1,0,sheet.GrassMeshDistance-10,sheet.GrassMeshDistance+10);
                    if(l==1)SetRange(m,sheet.GrassMeshDistance-10,sheet.GrassMeshDistance+10,sheet.GrassDistance-25,sheet.GrassDistance);
                    if(l==2)SetRange(m,sheet.GrassDistance-25,sheet.GrassDistance,sheet.GroundCoverDistance-60,sheet.GroundCoverDistance);
                }
                else SetRange(m,-1,0,(p.Category==Sheet.Kind.Shrub?sheet.ShrubDistance:350)-20,p.Category==Sheet.Kind.Shrub?sheet.ShrubDistance:350);
                EditorUtility.SetDirty(m);
            }
        }
        static Texture2D DirectionalAtlas(string name,Sheet.Level level,float span,int views,bool top)
        {
            string path=Folder+"/Billboards/"+name+".png";var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(existing!=null)return existing;
            GuardPolish();GameObject root=null,camGo=null;RenderTexture rt=null;Texture2D atlas=null;var mats=new List<Material>();var prior=RenderTexture.active;bool asyncBefore=ShaderUtil.allowAsyncCompilation;
            const int size=256;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;root=new GameObject("VegetationPolish_AtlasSource"){hideFlags=HideFlags.HideAndDontSave,layer=31};
                foreach(var part in level.Parts)
                {
                    var node=new GameObject("SourcePart"){hideFlags=HideFlags.HideAndDontSave,layer=31};node.transform.SetParent(root.transform,false);node.transform.localPosition=part.Local.GetColumn(3);node.transform.localRotation=part.Local.rotation;node.transform.localScale=part.Local.lossyScale;
                    node.AddComponent<MeshFilter>().sharedMesh=part.Mesh;var renderer=node.AddComponent<MeshRenderer>();var slots=new Material[part.Mesh.subMeshCount];
                    for(int sm=0;sm<slots.Length;sm++)
                    {
                        var mat=new Material(part.Material){hideFlags=HideFlags.HideAndDontSave};mat.SetFloat("_Billboard",0);mat.SetFloat("_WindAmplitude",0);mat.SetFloat("_SimpleLighting",1);mat.SetFloat("_AmbientFloor",.95f);mat.SetColor("_BaseColor",Color.white);SetRange(mat,-1,0,99990,99999);
                        if(sm!=part.Submesh){mat.SetFloat("_AlphaClip",1);mat.SetColor("_BaseColor",Color.clear);}slots[sm]=mat;mats.Add(mat);
                    }
                    renderer.sharedMaterials=slots;renderer.shadowCastingMode=ShadowCastingMode.Off;
                }
                camGo=new GameObject("VegetationPolish_AtlasCamera"){hideFlags=HideFlags.HideAndDontSave};var camera=camGo.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=span*.5f;camera.aspect=1;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.cullingMask=1<<31;camera.nearClipPlane=.01f;camera.farClipPlane=span*6;camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
                PrepareNewMaterialPasses(mats);rt=new RenderTexture(size,size,24,RenderTextureFormat.ARGB32);atlas=new Texture2D(size*views,size,TextureFormat.RGBA32,false);camera.targetTexture=rt;
                for(int v=0;v<views;v++)
                {
                    GuardPolish();var centre=Vector3.up*(top?0:span*.5f);var direction=top?Vector3.up:Quaternion.Euler(0,-v*360f/views,0)*Vector3.back;
                    camera.transform.position=centre+direction*span*2;camera.transform.LookAt(centre,top?Vector3.forward:Vector3.up);camera.Render();RenderTexture.active=rt;atlas.ReadPixels(new Rect(0,0,size,size),v*size,0,false);
                }
                atlas.Apply(false);File.WriteAllBytes(path,atlas.EncodeToPNG());camera.targetTexture=null;
            }
            finally{ShaderUtil.allowAsyncCompilation=asyncBefore;RenderTexture.active=prior;if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(atlas!=null)Object.DestroyImmediate(atlas);if(camGo!=null)Object.DestroyImmediate(camGo);if(root!=null)Object.DestroyImmediate(root);foreach(var m in mats)Object.DestroyImmediate(m);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.alphaIsTransparency=true;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=1024;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
