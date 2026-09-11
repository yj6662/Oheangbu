using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class KtpEmphasisBuild
    {
        public static string Build(string glyph)
        {
            if(glyph=="Inspect")
            {
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120Editor.AssetRoot+"/KtpOriginal/Original_0_3.prefab");
                return string.Join("\n",source.GetComponentsInChildren<Renderer>(true).Where(x=>x.name.ToLowerInvariant().Contains("pattern")).SelectMany(x=>x.sharedMaterials.Select(m=>x.name+" "+AssetDatabase.GetAssetPath(m)+" shader="+m.shader.name+" path="+AssetDatabase.GetAssetPath(m.shader)+" props="+string.Join(",",Enumerable.Range(0,m.shader.GetPropertyCount()).Select(i=>m.shader.GetPropertyName(i)+":"+m.shader.GetPropertyType(i))))));
            }
            if(EditorApplication.isPlayingOrWillChangePlaymode || !new[]{"가","나","거","너"}.Contains(glyph)) throw new Exception("Stopped editor and selected glyph required");
            var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");
            var p=catalog.Entries.Single(x=>x.Glyph==glyph).Profile;
            p.KtpEmphasis=true;p.KtpCastMultiplier=2.5f;p.KtpImpactMultiplier=2;p.KtpFieldMultiplier=1.5f;
            p.KtpContactMultiplier=2;p.KtpBrightness=2.2f;
            p.KtpCastOffset=new Vector3(0,0,1.15f);p.KtpImpactOffset=Vector3.zero;p.KtpFieldOffset=Vector3.zero;
            string castFolder=Vfx120Editor.AssetRoot+"/KtpEmphasis";if(!AssetDatabase.IsValidFolder(castFolder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"KtpEmphasis");
            int family=glyph=="나"||glyph=="너"?1:0;
            string castPath=castFolder+"/CastPattern_"+family+".prefab";
            var cast=AssetDatabase.LoadAssetAtPath<GameObject>(castPath);
            if(cast==null)
            {
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120Editor.AssetRoot+"/KtpOriginal/Original_"+family+"_3.prefab");
                var host=new GameObject("EmphasisCastPattern");try{var copy=Object.Instantiate(source,host.transform,false);copy.transform.localRotation=Quaternion.Euler(90,0,0);cast=PrefabUtility.SaveAsPrefabAsset(host,castPath);}finally{Object.DestroyImmediate(host);}
            }
            p.NativeCastPrefab=cast;
            var cp=AssetDatabase.LoadAssetAtPath<KtpContactProfile>(KtpContactBuild.AssetPath);
            cp.SpellProfiles=catalog.Entries.Where(x=>new[]{"가","나","거","너"}.Contains(x.Glyph)).Select(x=>x.Profile).ToArray();
            EditorUtility.SetDirty(p);EditorUtility.SetDirty(cp);AssetDatabase.SaveAssetIfDirty(p);AssetDatabase.SaveAssetIfDirty(cp);
            var folder=Path.Combine(Vfx120Editor.Output,"Emphasis4");Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,glyph+"_settings.json"),EditorJsonUtility.ToJson(p,true));
            return "BUILT "+glyph+"; camera="+(Camera.main==null?"none":Camera.main.transform.position+" "+Camera.main.transform.eulerAngles);
        }
    }
}
