using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>
    /// Import contract for the backdrop models. [SPEC-COMPACT-BACKDROP-RING · BACKDROP-SOURCE]
    /// Shipping state keeps meshes non-readable; readability is only raised around a measurement
    /// and restored afterwards, so the shipped asset matches the Spec.
    /// </summary>
    public static partial class CompactBackdropRing
    {
        [Serializable] public sealed class ImportRow
        {
            public string path,normals;
            public bool importerFound,readable,addColliders,importCameras,importLights;
            public float globalScale;
            public bool useFileUnits,bakeAxisConversion,generateSecondaryUV;
            public string materialImportMode;
            public bool changed;
        }
        [Serializable] public sealed class ImportReceipt
        {
            public string utc,phase;
            public ImportRow[] models;
            public StageTiming[] timings;
            public string[] findings;
        }

        static readonly string[] ModelPaths={PeakModelPath,RidgeModelPath};

        /// <summary>Creates the ring profile asset with its inline defaults if it is missing. Never overwrites an authored one.</summary>
        public static string EnsureProfile()
        {
            var existing=AssetDatabase.LoadAssetAtPath<Oheangbu.Data.World.CompactBackdropRingProfile>(ProfilePath);
            if(existing!=null)return "{\"status\":\"exists\",\"path\":\""+ProfilePath+"\"}";
            var directory=Path.GetDirectoryName(ProfilePath);
            if(!AssetDatabase.IsValidFolder(directory))
            {
                var parent=Path.GetDirectoryName(directory).Replace('\\','/');
                AssetDatabase.CreateFolder(parent,Path.GetFileName(directory));
            }
            var profile=ScriptableObject.CreateInstance<Oheangbu.Data.World.CompactBackdropRingProfile>();
            AssetDatabase.CreateAsset(profile,ProfilePath);
            AssetDatabase.SaveAssets();
            return "{\"status\":\"created\",\"path\":\""+ProfilePath+"\"}";
        }

        /// <summary>Applies the Spec import contract. Idempotent: re-running reports changed=false.</summary>
        public static string Import()
        {
            var receipt=new ImportReceipt{utc=DateTime.UtcNow.ToString("O"),phase="importing"};
            var timings=new List<StageTiming>();
            var findings=new List<string>();
            var rows=new List<ImportRow>();
            Directory.CreateDirectory(Output);

            Timed(timings,"apply_import_settings",()=>
            {
                foreach(var path in ModelPaths)rows.Add(ApplyImport(path,findings));
            });

            receipt.models=rows.ToArray();
            receipt.timings=timings.ToArray();
            receipt.findings=findings.ToArray();
            receipt.phase="imported";
            var json=JsonUtility.ToJson(receipt,true);
            File.WriteAllText(Path.Combine(Output,"import.json"),json);
            return json;
        }

        static ImportRow ApplyImport(string path,List<string> findings)
        {
            var row=new ImportRow{path=path};
            var importer=AssetImporter.GetAtPath(path) as ModelImporter;
            if(importer==null){findings.Add("No ModelImporter at "+path);return row;}
            row.importerFound=true;
            bool changed=false;

            // The baked import scale and axis correction are what bounds normalisation cancels;
            // changing either breaks the measured axis mapping.
            if(!importer.useFileUnits){importer.useFileUnits=true;changed=true;}
            if(!Mathf.Approximately(importer.globalScale,1f)){importer.globalScale=1f;changed=true;}
            if(importer.bakeAxisConversion){importer.bakeAxisConversion=false;changed=true;}

            if(importer.addCollider){importer.addCollider=false;changed=true;}
            if(importer.importCameras){importer.importCameras=false;changed=true;}
            if(importer.importLights){importer.importLights=false;changed=true;}
            if(importer.generateSecondaryUV){importer.generateSecondaryUV=false;changed=true;}
            if(importer.materialImportMode!=ModelImporterMaterialImportMode.None)
            {importer.materialImportMode=ModelImporterMaterialImportMode.None;changed=true;}
            if(importer.meshCompression!=ModelImporterMeshCompression.Off)
            {importer.meshCompression=ModelImporterMeshCompression.Off;changed=true;}
            // Shipping state. Measurement raises this temporarily and restores it.
            if(importer.isReadable){importer.isReadable=false;changed=true;}

            if(changed){importer.SaveAndReimport();}
            row.changed=changed;
            row.normals=importer.importNormals.ToString();
            row.readable=importer.isReadable;
            row.addColliders=importer.addCollider;
            row.importCameras=importer.importCameras;
            row.importLights=importer.importLights;
            row.globalScale=importer.globalScale;
            row.useFileUnits=importer.useFileUnits;
            row.bakeAxisConversion=importer.bakeAxisConversion;
            row.generateSecondaryUV=importer.generateSecondaryUV;
            row.materialImportMode=importer.materialImportMode.ToString();
            return row;
        }

        /// <summary>Raises readability for a measurement pass; returns the paths it changed.</summary>
        static List<string> RaiseReadable()
        {
            var raised=new List<string>();
            foreach(var path in ModelPaths)
            {
                var importer=AssetImporter.GetAtPath(path) as ModelImporter;
                if(importer==null||importer.isReadable)continue;
                importer.isReadable=true;importer.SaveAndReimport();raised.Add(path);
            }
            return raised;
        }

        /// <summary>Restores the shipping non-readable state. Always called from a finally block.</summary>
        static void RestoreReadable(List<string> raised)
        {
            if(raised==null)return;
            foreach(var path in raised)
            {
                var importer=AssetImporter.GetAtPath(path) as ModelImporter;
                if(importer==null||!importer.isReadable)continue;
                importer.isReadable=false;importer.SaveAndReimport();
            }
        }
    }
}
