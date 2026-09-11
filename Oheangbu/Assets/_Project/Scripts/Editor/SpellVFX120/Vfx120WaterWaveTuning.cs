using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120WaterWaveTuning
    {
        [Serializable] sealed class Report
        {
            public string status = "AUTHORED_REQUIRES_RENDER_REVIEW", generatedUtc, profile;
            public string before, after, patternSource, material;
            public string policy = "Existing WaveCrest and KTP water cast/contact sources retained. Only visual crest height and native hierarchy scale change; clocks and AreaPlan are unchanged.";
        }
        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");
            var p=catalog.Entries.Single(e=>e.Glyph=="오").Profile;
            var r=new Report { generatedUtc=DateTime.UtcNow.ToString("o"), profile=AssetDatabase.GetAssetPath(p), before=JsonUtility.ToJson(p) };
            if(p.BodyMesh==null || p.BodyMaterial==null || p.NativeCastPrefab==null || p.NativeImpactPrefab==null)
                throw new InvalidOperationException("Existing water crest/source motif is missing.");
            const string sourceMaterial="Assets/_Project/Art/SpellVFX120/Materials/M_Body_WaveCrest.mat";
            const string outputMaterial="Assets/_Project/Art/SpellVFX120/Materials/M_Body_KTP_WaterWave.mat";
            const string patternPath="Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_204.png";
            var basis=AssetDatabase.LoadAssetAtPath<Material>(sourceMaterial);
            var pattern=AssetDatabase.LoadAssetAtPath<Texture2D>(patternPath);
            if(basis==null || pattern==null || !basis.HasProperty("_WaterMotif"))
                throw new InvalidOperationException("Water source material/pattern is unavailable.");
            var material=AssetDatabase.LoadAssetAtPath<Material>(outputMaterial);
            if(material==null) { material=new Material(basis); AssetDatabase.CreateAsset(material,outputMaterial); }
            else EditorUtility.CopySerialized(basis,material);
            material.name="M_Body_KTP_WaterWave";
            material.SetTexture("_BaseMap",pattern); material.SetFloat("_WaterMotif",1);
            material.SetFloat("_Pattern",0);
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            p.BodyMaterial=material; r.patternSource=patternPath; r.material=outputMaterial;
            p.PartScale=new Vector3(p.PartScale.x,3.1f,p.PartScale.z);
            p.NativeScale=.30f; p.NativeImpactScale=.36f;
            EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p);
            r.after=JsonUtility.ToJson(p);
            File.WriteAllText(Path.Combine(Vfx120Editor.Output,"water_wave_tuning16.json"),JsonUtility.ToJson(r,true));
            return "TUNED_WATER_WAVE_KTP_CONTACT_SCALE_REQUIRES_RENDER_REVIEW";
        }
    }
}
