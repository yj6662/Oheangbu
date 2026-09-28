using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using System.Reflection;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // A material study only: capture swaps are scoped and restore even on failure.
    // No global scene application until the mountain expression has been reviewed.
    public static class CompactInkPaintingStudy
    {
        const string Folder="Assets/_Project/Art/World/WorldCompact/InkLandscape/InkPaintingStudy/FlowRevision";
        static string Output=>WorldMacroCompactAuthoring.Output+"/InkLandscape/InkPaintingStudy/FlowRevision";
        static IEnumerable<Renderer> Renderers=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true));
        static Renderer[] previewRenderers;
        static Material[][] previewOriginals;
        static double previewExpires;
        static Texture2D formGeography;
        static Dictionary<Material,Texture> formOriginalFields;
        static void RestoreForm()
        {
            InkPaintingMountainForm.EndPreview();
            if(formOriginalFields!=null)foreach(var pair in formOriginalFields)if(pair.Key!=null)pair.Key.SetTexture("_PaintedGeoField",pair.Value);
            formOriginalFields=null;
            if(formGeography!=null)Object.DestroyImmediate(formGeography);formGeography=null;
        }
        static string FormPreview()
        {
            Preview();
            try
            {
                InkPaintingMountainForm.BeginPreview();
                var sources=previewRenderers.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.HasProperty("_PaintedInkEnabled")).Distinct().ToArray();
                formGeography=InkPaintingGeographyField.Bake(sources,null,out var rect);
                formOriginalFields=new Dictionary<Material,Texture>();
                foreach(string path in AssetDatabase.FindAssets("t:Material",new[]{Folder}).Select(AssetDatabase.GUIDToAssetPath))
                {
                    var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(!m.HasProperty("_PaintedGeoField"))continue;
                    formOriginalFields.Add(m,m.GetTexture("_PaintedGeoField"));m.SetTexture("_PaintedGeoField",formGeography);
                }
                previewExpires=EditorApplication.timeSinceStartup+180;
                File.WriteAllText(Output+"/mountain_form_preview.json",InkPaintingMountainForm.ReceiptJson);
                return "Mountain form and painting preview active; capture or restore next. No scene saved.";
            }
            catch{RestorePreview();throw;}
        }
        static Oheangbu.App.WorldLookDriver skyDriver;
        static RegionalInkSkyProfile originalSky,temporarySky;
        static readonly FieldInfo RegionalSkyField=typeof(Oheangbu.App.WorldLookDriver).GetField("_regionalSkyProfile",BindingFlags.Instance|BindingFlags.NonPublic);
        static void BeginPaperSky()
        {
            skyDriver=CompactRecovery.Find<Oheangbu.App.WorldLookDriver>();
            if(skyDriver==null)return;
            originalSky=RegionalSkyField.GetValue(skyDriver) as RegionalInkSkyProfile;
            if(originalSky==null)return;
            temporarySky=Object.Instantiate(originalSky);temporarySky.hideFlags=HideFlags.HideAndDontSave;
            foreach(var entry in temporarySky.Regions)
            {
                // Lift only sky pigment to off-white paper; exposure and environmental light stay fixed.
                entry.Horizon=new Color(.98f,.93f,.83f,1);
                entry.Zenith=new Color(.86f,.82f,.74f,1)+(entry.Zenith-new Color(.69f,.71f,.68f,1))*.2f;
                entry.Cloud=new Color(.75f,.72f,.66f,1);
                entry.CloudDensity*=.6f;
            }
            RegionalSkyField.SetValue(skyDriver,temporarySky);
        }
        static void EndPaperSky()
        {
            if(skyDriver!=null&&originalSky!=null)
            {
                RegionalSkyField.SetValue(skyDriver,originalSky);
                var seat=CompactRecovery.Find<Oheangbu.App.World.Vehicle.WorldMacroPalanquinSeat>();
                if(seat!=null&&seat.ViewCamera!=null)skyDriver.PreviewRegionalSky(seat.ViewCamera.transform.position);
            }
            if(temporarySky!=null)Object.DestroyImmediate(temporarySky);
            skyDriver=null;originalSky=null;temporarySky=null;
        }
        static void ExpirePreview(){if(EditorApplication.timeSinceStartup>previewExpires)RestorePreview();}
        static void BeforePreviewSceneSave(Scene scene,string path){RestorePreview();}
        static void BeforePreviewSceneClose(Scene scene,bool removing){RestorePreview();}
        static void RestorePreview()
        {
            RestoreForm();
            InkPaintingFoliagePreview.End();
            EndPaperSky();
            if(previewRenderers==null)return;
            for(int i=0;i<previewRenderers.Length;i++)if(previewRenderers[i]!=null)previewRenderers[i].sharedMaterials=previewOriginals[i];
            previewRenderers=null;previewOriginals=null;
            AssemblyReloadEvents.beforeAssemblyReload-=RestorePreview;
            EditorApplication.quitting-=RestorePreview;
            EditorApplication.update-=ExpirePreview;
            EditorSceneManager.sceneSaving-=BeforePreviewSceneSave;
            EditorSceneManager.sceneClosing-=BeforePreviewSceneClose;
        }
        static string Preview()
        {
            if(previewRenderers!=null)throw new InvalidOperationException("Preview already active");
            previewRenderers=Renderers.Where(r=>r.sharedMaterials.Any(Mountain)).ToArray();previewOriginals=previewRenderers.Select(r=>r.sharedMaterials).ToArray();
            AssemblyReloadEvents.beforeAssemblyReload+=RestorePreview;EditorApplication.quitting+=RestorePreview;
            // Register the coordinator before nested foliage/form handlers so restoration
            // always unwinds form -> foliage -> material, including a user-initiated save.
            EditorSceneManager.sceneSaving+=BeforePreviewSceneSave;
            EditorSceneManager.sceneClosing+=BeforePreviewSceneClose;
            previewExpires=EditorApplication.timeSinceStartup+60;EditorApplication.update+=ExpirePreview;
            try
            {
                for(int i=0;i<previewRenderers.Length;i++)previewRenderers[i].sharedMaterials=previewOriginals[i].Select(m=>Mountain(m)?AssetDatabase.LoadAssetAtPath<Material>(DerivedPath(m)):m).ToArray();
                if(previewRenderers.Any(r=>r.sharedMaterials.Any(m=>m==null)))throw new InvalidOperationException("Missing study material");
                InkPaintingFoliagePreview.Begin(Folder);
                BeginPaperSky();
                EditorApplication.QueuePlayerLoopUpdate();SceneView.RepaintAll();
                return "Temporary painting preview bound for renderer update; capture or restore must follow, do not save scene.";
            }
            catch{RestorePreview();throw;}
        }
        static bool Mountain(Material m)=>m!=null&&m.HasProperty("_CIEnabled")&&m.GetFloat("_CIEnabled")>.5f&&(m.shader.name=="Oheangbu/CompactNaturalGround"||m.shader.name=="Oheangbu/WorldMacroTerrain");
        static string Digest(string path){using(var hash=System.Security.Cryptography.SHA256.Create())using(var file=File.OpenRead(path))return Convert.ToBase64String(hash.ComputeHash(file));}
        [Serializable] sealed class Baseline {public string utc,scene,sceneHash;public string[] sources;public int renderers;public bool wasDirty;}
        [Serializable] sealed class Receipt
        {
            public string status,view,sourceSceneHash,qualityName,pipelineAsset;
            public bool bindingsRestored,sceneFileUnchanged,sourceMaterialsUnchanged,sceneDirtyBefore,sceneDirtyAfter;
            public string[] shaderChecks;
            public int materials,rendererBindings,qualityIndex,outputWidth,outputHeight;
            public float pipelineRenderScale;
        }
        public static string Execute(string command)
        {
            if(command=="spur-shoulder-begin")return InkPaintingMountainApplication.BeginSouthernSpurTrial("Tools/Unity/Staging/SouthernSpurShoulderTrial/trial_manifest_shoulder_v2.json");
            if(command=="spur-toe-begin")return InkPaintingMountainApplication.BeginSouthernSpurTrial("Tools/Unity/Staging/SouthernToeTrial/trial_manifest_toe_v3.json");
            if(command=="spur-trial-begin")return InkPaintingMountainApplication.BeginSouthernSpurTrial("Tools/Unity/Staging/SouthernSpurTrial/trial_manifest.json");
            if(command.StartsWith("spur-trial-capture:"))return InkPaintingMountainApplication.CaptureSouthernSpurTrial(command.Substring("spur-trial-capture:".Length));
            if(command=="spur-trial-audit")return InkPaintingMountainApplication.AuditSouthernSpurTrial();
            if(command=="spur-trial-export")return InkPaintingMountainApplication.ExportSouthernSpurTrial();
            if(command=="spur-trial-restore")return InkPaintingMountainApplication.RestoreSouthernSpurTrial();
            if(InkPaintingMountainApplication.IsSouthernSpurTrialActive)
            {
                if(command=="restore")return InkPaintingMountainApplication.RestoreSouthernSpurTrial();
                throw new InvalidOperationException("Restore the source-based southern-spur trial before any other study command.");
            }
            if(command=="restore"){string saved=InkPaintingSavedStudyPlay.Restore();RestorePreview();return saved+" Preview materials restored";}
            if(command=="performance-before"||command=="performance-after")
            {
                var session=CompactRecovery.Find<Oheangbu.App.World.WorldMacroPlaytestSession>();
                if(!Application.isPlaying||session==null||string.IsNullOrEmpty(session.TestSaveSuffix))throw new InvalidOperationException("Isolated Play required");
                RestorePreview();
                // Saved-geometry comparison: change materials only, never reswap sky or deformation.
                if(command=="performance-before")return InkPaintingSavedStudyPlay.BeginOriginal();
                return InkPaintingSavedStudyPlay.AfterSaved();
            }
            if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=WorldMacroCompactAuthoring.TargetScene){RestorePreview();throw new InvalidOperationException("Compact Edit scene required");}
            if(Prologue.PrologueAudit.CommitRatio()>=.85f){RestorePreview();throw new InvalidOperationException("System commit >=85%; stopped");}
            Directory.CreateDirectory(Output);
            if(command=="form-map-bake"){RestorePreview();return InkPaintingFormMapBaker.BakeAndBind("Tools/Unity/Staging/FormMapAnchors/form_map_anchors_proposal.json");}
            if(command=="form-map-v2"){RestorePreview();return InkPaintingFormMapBaker.BakeAndBind("Tools/Unity/Staging/FormMapAnchors/form_map_anchors_v2_loaded_faces.json");}
            if(command=="form-map-v3"){RestorePreview();return InkPaintingFormMapBaker.BakeAndBind("Tools/Unity/Staging/CrestInkAuthoring/form_map_anchors_v3_one_crest.json");}
            if(command=="form-map-restore"){RestorePreview();return InkPaintingFormMapBaker.RestoreLast();}
            if(command=="reload-derived")
            {
                RestorePreview();
                var paths=AssetDatabase.FindAssets("t:Material",new[]{Folder+"/Materials",Folder+"/FoliageMaterials"}).Select(AssetDatabase.GUIDToAssetPath).ToArray();
                foreach(var path in paths)AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                return "Reloaded "+paths.Length+" owned material assets from their saved files; no scene saved.";
            }
            if(command=="bank-bake"){RestorePreview();return InkPaintingRoadBankField.BakeAndBind();}
            if(command=="look-install")
            {
                RestorePreview();
                foreach(var path in AssetDatabase.FindAssets("t:Material",new[]{Folder+"/Materials",Folder+"/FoliageMaterials"}).Select(AssetDatabase.GUIDToAssetPath))
                {
                    var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                    foreach(var property in material.GetTexturePropertyNames())
                    {var texture=material.GetTexture(property);if(texture!=null&&!EditorUtility.IsPersistent(texture))throw new InvalidOperationException("Transient texture remains on "+path+" : "+property);}
                    AssetDatabase.SaveAssetIfDirty(material);
                }
                return InkPaintingPersistentApplication.Apply();
            }
            if(command=="geometry-prepare")
            {
                RestorePreview();
                var settings=JsonUtility.FromJson<InkPaintingMountainApplication.Settings>(File.ReadAllText(Output+"/geometry_application_settings.json"));
                return InkPaintingMountainApplication.Prepare(settings);
            }
            if(command=="geometry-audit-proof")
            {
                var settings=JsonUtility.FromJson<InkPaintingMountainApplication.Settings>(File.ReadAllText(Output+"/geometry_application_settings.json"));
                return InkPaintingMountainForm.DiagnoseAuditProvenance(settings.exportReceiptPath,settings.physicsReceiptPath,settings.normalReceiptPath);
            }
            if(command=="geometry-install"){RestorePreview();return InkPaintingMountainApplication.ApplyAndSave();}
            if(command=="geometry-verify")return InkPaintingMountainApplication.VerifyApplied(File.ReadAllText(Output+"/current_geometry_receipt.txt").Trim());
            if(command=="navigation-prepare")return InkPaintingNavigationUpdate.Prepare(File.ReadAllText(Output+"/current_geometry_receipt.txt").Trim());
            if(command=="navigation-step")return InkPaintingNavigationUpdate.Step();
            if(command=="navigation-audit")return InkPaintingNavigationUpdate.Audit();
            if(command=="navigation-install")return InkPaintingNavigationUpdate.Install();
            if(command=="geography-install")return InkPaintingPersistentGeographyField.Apply(new InkPaintingPersistentGeographyField.Settings{geometryApplicationReceiptPath=File.ReadAllText(Output+"/current_geometry_receipt.txt").Trim()});
            if(command=="leaf-inspect"){RestorePreview();return InkPaintingLeafPadding.Inspect();}
            if(command=="leaf-prepare"){RestorePreview();return InkPaintingLeafPadding.Prepare();}
            if(command=="leaf-enable"){RestorePreview();return InkPaintingLeafPadding.SetEnabled(true);}
            if(command=="leaf-restore"){RestorePreview();return InkPaintingLeafPadding.Restore();}
            if(command.StartsWith("probe:"))return InkPaintingViewProbe.Probe(Output,command.Substring(6));
            if(command=="preview")return Preview();
            if(command=="shape-prepare-file")
            {
                RestorePreview();
                var settings=JsonUtility.FromJson<InkPaintingMountainForm.Settings>(File.ReadAllText(Output+"/mountain_form_settings.json"));
                if(settings==null)throw new InvalidOperationException("Missing mountain form settings");
                var result=InkPaintingMountainForm.Prepare(settings);
                File.WriteAllText(Output+"/mountain_form_prepared.json",result);return result;
            }
            if(command.StartsWith("shape-prepare:"))
            {
                RestorePreview();float limit=float.Parse(command.Substring(14),System.Globalization.CultureInfo.InvariantCulture);
                var settings=InkPaintingMountainForm.CheongrimReviewSettings();
                settings.DeltaLimit=limit;settings.PeakAmplitude=limit*.8f;settings.SaddleAmplitude=limit;
                var result=InkPaintingMountainForm.Prepare(settings);
                File.WriteAllText(Output+"/mountain_form_prepared.json",result);return result;
            }
            if(command=="shape-preview")return FormPreview();
            if(command=="shape-physics")
            {
                try
                {
                    var result=InkPaintingMountainForm.AuditPhysicalSupport();
                    File.WriteAllText(Output+"/mountain_form_physics.json",result);return result;
                }
                finally{RestorePreview();}
            }
            if(command=="shape-normals")
            {
                try
                {
                    var physics=InkPaintingMountainForm.AuditPhysicalSupport();
                    File.WriteAllText(Output+"/mountain_form_physics.json",physics);
                    var result=InkPaintingMountainForm.AuditPhysicalNormals(physics);
                    File.WriteAllText(Output+"/mountain_form_normals.json",result);return result;
                }
                finally{RestorePreview();}
            }
            if(command=="shape-receipt")return InkPaintingMountainForm.ReceiptJson;
            if(command=="shape-export-audit")
            {
                RestorePreview();
                var settings=JsonUtility.FromJson<InkPaintingMountainApplication.Settings>(File.ReadAllText(Output+"/geometry_application_settings.json"));
                InkPaintingMountainForm.AuditAgainstExport(settings.exportReceiptPath);
                var audited=InkPaintingMountainForm.LastExportBoundAudit;
                File.WriteAllText(Output+"/mountain_export_physics.json",audited.physicalJson);
                File.WriteAllText(Output+"/mountain_export_normals.json",audited.normalJson);
                return "Export-bound physical/normal receipts written; original audit receipts preserved.";
            }
            if(command=="shape-export")
            {
                RestorePreview();
                return InkPaintingMountainForm.ExportPrepared(Folder+"/MountainForms",Output+"/MountainExport");
            }
            if(command=="form-status")return InkPaintingFormStudy.LastResult;
            if(command=="form")
            {
                if(previewRenderers!=null)throw new InvalidOperationException("Restore game preview first");
                var material=AssetDatabase.FindAssets("t:Material",new[]{Folder+"/Materials"}).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>).First(m=>m.shader.name=="Oheangbu/Study/InkPaintingGround");
                return InkPaintingFormStudy.Capture(Output,material);
            }
            if(command=="prepare")return Prepare();
            if(command=="wash")return BindWash();
            if(command.StartsWith("capture-before:"))return Capture(command.Substring(15),false);
            if(command.StartsWith("capture-installed:"))return Capture(command.Substring(18),false);
            if(command.StartsWith("capture:"))return Capture(command.Substring(8),true);
            throw new ArgumentException(command);
        }
        static string Prepare()
        {
            if(previewRenderers!=null)throw new InvalidOperationException("Restore temporary preview before preparing derivatives");
            var scene=SceneManager.GetActiveScene();
            var renderers=Renderers.Where(r=>r.sharedMaterials.Any(Mountain)).ToArray();
            var sources=renderers.SelectMany(r=>r.sharedMaterials).Where(Mountain).Distinct().ToArray();
            if(!File.Exists(Output+"/baseline.json"))
            {
                File.Copy(scene.path,Output+"/before_disk.unity",false);
                if(!EditorSceneManager.SaveScene(scene,Output+"/before_open.unity",true))throw new IOException("Live scene backup failed");
                var baseline=new Baseline{utc=DateTime.UtcNow.ToString("o"),scene=scene.path,sceneHash=Digest(scene.path),wasDirty=scene.isDirty,renderers=renderers.Length,
                    sources=sources.Select(m=>AssetDatabase.GetAssetPath(m)).Select(p=>p+"|"+Digest(p)).ToArray()};
                File.WriteAllText(Output+"/baseline.json",JsonUtility.ToJson(baseline,true));
                File.Copy(WorldMacroCompactAuthoring.Output+"/InkLandscape/BroadBrush/views.json",Output+"/views.json",false);
            }
            string atlasPath="Assets/_Project/Art/World/WorldCompact/InkLandscape/InkPaintingStudy/PaintedInkAtlas.png";
            AssetDatabase.ImportAsset(atlasPath,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(atlasPath);
            importer.sRGBTexture=false;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;
            importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;importer.maxTextureSize=2048;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.npotScale=TextureImporterNPOTScale.None;
            importer.alphaSource=TextureImporterAlphaSource.None;importer.SaveAndReimport();
            Directory.CreateDirectory(Folder+"/Materials");AssetDatabase.Refresh();
            var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            var geography=InkPaintingGeographyField.Bake(sources,Folder+"/Geography.asset",out var geographyRect);
            File.WriteAllText(Output+"/geography_field.json",JsonUtility.ToJson(InkPaintingGeographyField.LastReceipt,true));
            foreach(var src in sources)
            {
                var shader=Shader.Find(src.shader.name=="Oheangbu/CompactNaturalGround"?"Oheangbu/Study/InkPaintingGround":"Oheangbu/Study/InkPaintingTerrain");
                if(shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Missing/invalid painting shader: "+(shader==null?"null":shader.name));
                var material=new Material(src);material.name=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(src));material.shader=shader;material.SetTexture("_PaintedInkAtlas",atlas);
                material.SetTexture("_PaintedGeoField",geography);material.SetVector("_PaintedGeoRect",geographyRect);material.SetFloat("_PaintedGeoEnabled",1);
                string path=DerivedPath(src);var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(existing==null)AssetDatabase.CreateAsset(material,path);else{EditorUtility.CopySerialized(material,existing);Object.DestroyImmediate(material);EditorUtility.SetDirty(existing);}
            }
            InkPaintingFoliagePreview.Prepare(Folder);
            foreach(var path in AssetDatabase.FindAssets("t:Material",new[]{Folder+"/FoliageMaterials"}).Select(AssetDatabase.GUIDToAssetPath))
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                material.SetTexture("_PaintedGeoField",geography);material.SetVector("_PaintedGeoRect",geographyRect);material.SetFloat("_PaintedGeoEnabled",1);
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
            return "Prepared "+sources.Length+" isolated mountain material derivatives, "+atlas.width+"x"+atlas.height+" atlas. Scene has NOT been switched.";
        }
        static string BindWash()
        {
            if(previewRenderers!=null)throw new InvalidOperationException("Restore before binding the wash");
            string path="Assets/_Project/Art/World/WorldCompact/InkLandscape/InkPaintingStudy/ConnectedInkWash.png";
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture=false;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Mirror;
            importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;importer.maxTextureSize=2048;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.npotScale=TextureImporterNPOTScale.None;
            importer.alphaSource=TextureImporterAlphaSource.None;importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);int count=0;
            if(texture==null)throw new InvalidOperationException("Connected ink wash import is not ready. Retry wash on the next Editor update; no material was changed.");
            foreach(string asset in AssetDatabase.FindAssets("t:Material",new[]{Folder+"/Materials"}).Select(AssetDatabase.GUIDToAssetPath))
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>(asset);
                material.SetTexture("_PaintedRockWash",texture);material.SetFloat("_PaintedWashEnabled",1);
                material.SetVector("_PaintedWashTiling",new Vector4(520,600,.65f,0));
                EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);count++;
            }
            return "Bound connected ink wash "+texture.width+"x"+texture.height+" GUID="+AssetDatabase.AssetPathToGUID(path)+" to "+count+" derived mountain materials";
        }
        static string DerivedPath(Material src)=>Folder+"/Materials/"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(src))+".mat";
        static string Capture(string request,bool study)
        {
            string[] parts=request.Split(':');string label=parts.Length==2?parts[0]:(study?"after":"before"),view=parts[parts.Length-1];
            var scene=SceneManager.GetActiveScene();var baseline=JsonUtility.FromJson<Baseline>(File.ReadAllText(Output+"/baseline.json"));
            if(study&&previewRenderers==null)throw new InvalidOperationException("Run preview in the preceding Editor update before study capture");
            var rr=study?previewRenderers:Renderers.Where(r=>r.sharedMaterials.Any(Mountain)).ToArray();var old=study?previewOriginals:rr.Select(r=>r.sharedMaterials).ToArray();
            var receipt=new Receipt{view=view,sceneDirtyBefore=scene.isDirty,sourceSceneHash=Digest(scene.path),rendererBindings=rr.Length};
            receipt.qualityIndex=QualitySettings.GetQualityLevel();
            receipt.qualityName=QualitySettings.names[receipt.qualityIndex];
            var pipeline=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            receipt.pipelineAsset=pipeline==null?"":AssetDatabase.GetAssetPath(pipeline);
            receipt.pipelineRenderScale=pipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp?urp.renderScale:1;
            receipt.outputWidth=1920;receipt.outputHeight=1080;
            string capture;
            bool previousAsync=ShaderUtil.allowAsyncCompilation;
            try
            {
                if(rr.Any(r=>r.sharedMaterials.Any(m=>m==null)))throw new InvalidOperationException("Missing study material");
                // A previously queued async variant can still be unavailable when
                // async compilation is disabled. Explicitly finish the actual scene
                // material passes before any render request, so a sky-only terrain
                // image cannot be accepted simply because there was no shader error.
                ShaderUtil.allowAsyncCompilation=false;
                var studyMaterials=Renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.shader!=null&&
                    m.shader.name.StartsWith("Oheangbu/Study/InkPainting",StringComparison.Ordinal)).Distinct();
                int checkedPasses=0,compiledPasses=0;
                foreach(var material in studyMaterials)
                for(int pass=0;pass<material.passCount;pass++)
                {
                    if(!material.GetShaderPassEnabled(material.GetPassName(pass)))continue;
                    if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Capture shader preparation stopped: system commit >=85%.");
                    if(!ShaderUtil.IsPassCompiled(material,pass)){ShaderUtil.CompilePass(material,pass,true);compiledPasses++;}
                    if(!ShaderUtil.IsPassCompiled(material,pass))throw new InvalidOperationException("Capture shader pass unready: "+material.name+" / "+pass);
                    checkedPasses++;
                }
                File.WriteAllText(Output+"/"+label+"_"+view+"_shader_readiness.txt", "Actual scene study material passes checked: "+checkedPasses+"; compiled synchronously: "+compiledPasses+". This checks material passes, not art approval or all possible runtime variants.");
                CompactRecovery.Find<WorldMacroDressingRenderer>().ResetCache();
                capture=CompactInkLandscapeAuthoring.CaptureReviewAt(label+":"+view,Output);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation=previousAsync;
                for(int i=0;i<rr.Length;i++)rr[i].sharedMaterials=old[i];
                RestorePreview();
                CompactRecovery.Find<WorldMacroDressingRenderer>().ResetCache();
                receipt.bindingsRestored=rr.Select((r,i)=>r.sharedMaterials.SequenceEqual(old[i])).All(x=>x);
                receipt.sceneFileUnchanged=receipt.sourceSceneHash==Digest(scene.path);
                receipt.sceneDirtyAfter=scene.isDirty;
                receipt.sourceMaterialsUnchanged=baseline.sources.All(row=>{int split=row.LastIndexOf('|');return Digest(row.Substring(0,split))==row.Substring(split+1);});
                receipt.shaderChecks=new[]{"Oheangbu/Study/InkPaintingGround","Oheangbu/Study/InkPaintingTerrain","Oheangbu/Study/InkPaintingVegetation","Oheangbu/Study/InkPaintingFoliageCard"}.Select(name=>{
                    var shader=Shader.Find(name);return (shader!=null&&!ShaderUtil.ShaderHasError(shader)?"PASS ":"FAIL ")+name+(shader==null?"":": "+string.Join("; ",ShaderUtil.GetShaderMessages(shader).Select(m=>m.severity+" "+m.message)));}).ToArray();
                receipt.materials=baseline.sources.Length;
                receipt.status=receipt.bindingsRestored&&receipt.sceneFileUnchanged&&receipt.sourceMaterialsUnchanged&&receipt.shaderChecks.All(x=>x.StartsWith("PASS"))?"PASS":"FAIL";
                File.WriteAllText(Output+"/"+label+"_"+view+"_checks.json",JsonUtility.ToJson(receipt,true));
            }
            bool installed=Renderers.Any(r=>r.sharedMaterials.Any(m=>m!=null&&m.shader!=null&&m.shader.name.StartsWith("Oheangbu/Study/InkPainting",StringComparison.Ordinal)));
            return capture+"; "+receipt.status+(installed?" saved painting scene captured; scene and source assets unchanged.":" source scene/materials restored. NOT a global application.");
        }
    }
}
