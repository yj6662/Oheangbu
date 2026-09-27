using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  // #293 candidate look pass (candidate scene + candidate vegetation copies only).
  // The candidate lit its world with zero ambient (flat black), so every shaded canopy fell to the
  // material's diffuse floor and read as a black blot. Originals are recorded once for "look293-revert".
  [Serializable] sealed class LookMaterial293 {public string path,shader,baseMap;public float ambient,saturation,light,wash,washStart,washEnd,cutoff=-1;public Color baseColor;}
  [Serializable] sealed class LookRecord293 {public int ambientMode;public Color sky,equator,ground;public float ambientIntensity,sunIntensity;public Color sunColor;public LookMaterial293[] materials;}
  // colour-bled leaf atlas copies from Tools/Art/dilate_leaves293.py (alpha and opaque texels identical to the source)
  [Serializable] sealed class Leaf293 {public string source,dilated;public string[] materials;}
  [Serializable] sealed class Leaves293 {public Leaf293[] items;}
  const float LeafCutoff293=.2f;

  const string InkVegetation293="Oheangbu/Study/InkPaintingVegetation";
  static string Look293(bool revert)
  {
   string file=O293+"/look-original.json";var scene=SceneManager.GetActiveScene();
   var sun=Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(l=>l.type==LightType.Directional&&l.gameObject.scene==scene);
   var mats=AssetDatabase.FindAssets("t:Material",new[]{A292+"/Materials"}).Select(AssetDatabase.GUIDToAssetPath)
    .Where(p=>Path.GetFileName(p).StartsWith("Vegetation_")).Select(p=>(Path:p,Mat:AssetDatabase.LoadAssetAtPath<Material>(p)))
    .Where(x=>x.Mat!=null&&x.Mat.shader!=null&&(x.Mat.shader.name=="Oheangbu/WorldMacroVegetation"||x.Mat.shader.name==InkVegetation293)).ToArray();
   var lit=Shader.Find("Oheangbu/WorldMacroVegetation");
   if(!File.Exists(file))
   {
    var record=new LookRecord293{ambientMode=(int)RenderSettings.ambientMode,sky=RenderSettings.ambientSkyColor,equator=RenderSettings.ambientEquatorColor,ground=RenderSettings.ambientGroundColor,
     ambientIntensity=RenderSettings.ambientIntensity,sunIntensity=sun!=null?sun.intensity:0,sunColor=sun!=null?sun.color:Color.white,
     materials=mats.Select(x=>new LookMaterial293{path=x.Path,shader=x.Mat.shader.name,ambient=x.Mat.GetFloat("_AmbientFloor"),saturation=x.Mat.GetFloat("_Saturation"),light=x.Mat.GetFloat("_LightResponse"),
      wash=x.Mat.GetFloat("_WashStrength"),washStart=x.Mat.GetFloat("_WashStart"),washEnd=x.Mat.GetFloat("_WashEnd"),cutoff=x.Mat.GetFloat("_Cutoff"),baseColor=x.Mat.GetColor("_BaseColor")}).ToArray()};
    File.WriteAllText(file,JsonUtility.ToJson(record,true));
   }
   var rec=JsonUtility.FromJson<LookRecord293>(File.ReadAllText(file));
   // records written before these fields existed: the pass had not changed _Cutoff or _BaseMap yet, so current = original
   if(rec.materials.Any(o=>o.cutoff<0||string.IsNullOrEmpty(o.baseMap)))
   {
    foreach(var o in rec.materials){var m=AssetDatabase.LoadAssetAtPath<Material>(o.path);if(m==null)continue;if(o.cutoff<0)o.cutoff=m.GetFloat("_Cutoff");if(string.IsNullOrEmpty(o.baseMap))o.baseMap=AssetDatabase.GetAssetPath(m.GetTexture("_BaseMap"));}
    File.WriteAllText(file,JsonUtility.ToJson(rec,true));
   }
   if(revert)
   {
    RenderSettings.ambientMode=(AmbientMode)rec.ambientMode;RenderSettings.ambientSkyColor=rec.sky;RenderSettings.ambientEquatorColor=rec.equator;RenderSettings.ambientGroundColor=rec.ground;RenderSettings.ambientIntensity=rec.ambientIntensity;
    if(sun!=null){sun.intensity=rec.sunIntensity;sun.color=rec.sunColor;EditorUtility.SetDirty(sun);}
    foreach(var o in rec.materials){var m=AssetDatabase.LoadAssetAtPath<Material>(o.path);if(m==null)continue;if(!string.IsNullOrEmpty(o.shader)&&Shader.Find(o.shader)!=null)m.shader=Shader.Find(o.shader);m.SetFloat("_AmbientFloor",o.ambient);m.SetFloat("_Saturation",o.saturation);m.SetFloat("_LightResponse",o.light);m.SetFloat("_WashStrength",o.wash);m.SetFloat("_WashStart",o.washStart);m.SetFloat("_WashEnd",o.washEnd);if(o.cutoff>=0)m.SetFloat("_Cutoff",o.cutoff);m.SetColor("_BaseColor",o.baseColor);
     if(!string.IsNullOrEmpty(o.baseMap)){var map=AssetDatabase.LoadAssetAtPath<Texture>(o.baseMap);if(map!=null)m.SetTexture("_BaseMap",map);}EditorUtility.SetDirty(m);}
    Save292();return "Reverted candidate lighting and "+rec.materials.Length+" vegetation materials to the recorded originals";
   }
   // soft overcast mountain light: sky/equator/ground ambient, a warm but restrained sun
   RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.60f,.64f,.68f);RenderSettings.ambientEquatorColor=new Color(.47f,.49f,.47f);
   RenderSettings.ambientGroundColor=new Color(.24f,.23f,.21f);RenderSettings.ambientIntensity=1;
   if(sun!=null){sun.intensity=1.15f;sun.color=new Color(1f,.96f,.90f);EditorUtility.SetDirty(sun);}
   int brightened=0,washed=0,converted=0,clipped=0;
   foreach(var x in mats)
   {
    var o=rec.materials.FirstOrDefault(r=>r.path==x.Path);if(o==null)continue;var m=x.Mat;
    // Cheongrim alone still drew its plants with the retired ink-painting study shader (near-black canopy tones)
    bool ink=o.shader==InkVegetation293;if(ink&&lit!=null){m.shader=lit;converted++;}
    // the ink study clipped at .04-.11 to keep near-transparent fringes for thin strokes; lit, those fringes pull the
    // atlases' black transparent RGB into the leaves. A mild floor plus the colour-bled atlases below fix it without
    // thinning the far LODs (.42 alone stripped pines and elms to bare twigs at distance).
    if(ink&&m.GetFloat("_AlphaClip")>.5f&&o.cutoff<LeafCutoff293){m.SetFloat("_Cutoff",LeafCutoff293);clipped++;}else if(o.cutoff>=0)m.SetFloat("_Cutoff",o.cutoff);
    if(!string.IsNullOrEmpty(o.baseMap)){var map=AssetDatabase.LoadAssetAtPath<Texture>(o.baseMap);if(map!=null)m.SetTexture("_BaseMap",map);}
    m.SetFloat("_AmbientFloor",Mathf.Max(o.ambient,.40f));m.SetFloat("_Saturation",Mathf.Max(o.saturation,.55f));m.SetFloat("_LightResponse",Mathf.Max(o.light,.78f));
    // distant canopies settle into the atmosphere (layered washes) instead of staying as black dots
    // (light: the realm air of RealmFog293 carries most of the distance; a strong wash here double-fades canopies)
    if(o.wash<=0){m.SetFloat("_WashStrength",.22f);m.SetFloat("_WashStart",380);m.SetFloat("_WashEnd",2400);washed++;}
    float lum=o.baseColor.r*.2126f+o.baseColor.g*.7152f+o.baseColor.b*.0722f;
    float want=ink?.74f:.45f;
    // only a texture multiplier is lifted; a flat-colour canopy (Meshy pine tufts, no base map) keeps its authored deep
    // green, which lifted to .74 rendered as pale white tufts in sun
    if(lum<want&&!string.IsNullOrEmpty(o.baseMap)){var c=Color.Lerp(o.baseColor*(want/Mathf.Max(.05f,lum)),new Color(want,want,want),ink?.35f:0);c.a=1;m.SetColor("_BaseColor",c);brightened++;}else m.SetColor("_BaseColor",o.baseColor);
    EditorUtility.SetDirty(m);
   }
   // Realm canopies (ART-SKY atmosphere seeds, TEST): tree leaf cards take their realm's air — 청림 어둑한 녹음 /
   // 적로 탄 주홍 / 철옹 쇠빛 회청 / 현강 물안개 청묵 / 황경 황토 기운. Bark keeps its colour, and a material shared by
   // two realms keeps its own. The multiplier applies to the value set above, so re-running stays idempotent.
   var canopyTint=new System.Collections.Generic.Dictionary<Oheangbu.Data.World.RealmId,Color>
   {
    {Oheangbu.Data.World.RealmId.Cheongrim,new Color(.70f,.80f,.70f)},{Oheangbu.Data.World.RealmId.Jeokro,new Color(.84f,.74f,.64f)},
    {Oheangbu.Data.World.RealmId.Cheolong,new Color(.72f,.76f,.78f)},{Oheangbu.Data.World.RealmId.Hyeongang,new Color(.60f,.70f,.72f)},
    {Oheangbu.Data.World.RealmId.Hwanggyeong,new Color(.82f,.80f,.64f)},
   };
   var leafRealms=new System.Collections.Generic.Dictionary<Material,System.Collections.Generic.HashSet<Oheangbu.Data.World.RealmId>>();
   foreach(var sheet in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Oheangbu.App.World.CompactRebuildArtRenderer>(true)).Select(a=>a.Sheet).Where(s=>s!=null).Distinct())
    foreach(var prototype in sheet.Prototypes)
     foreach(var level in prototype.Lods)foreach(var part in level.Parts)
     {
      if(part.Material==null)continue;
      // any non-tree use (shrub, grass, prop) also counts as "shared" so those keep their colour
      var key=prototype.Category==Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Tree?prototype.Realm:(Oheangbu.Data.World.RealmId)(-1);
      if(!leafRealms.TryGetValue(part.Material,out var set))leafRealms[part.Material]=set=new System.Collections.Generic.HashSet<Oheangbu.Data.World.RealmId>();set.Add(key);
     }
   int canopies=0;
   foreach(var kv in leafRealms)
   {
    var m=kv.Key;if(kv.Value.Count!=1||!canopyTint.TryGetValue(kv.Value.First(),out var tint))continue;
    if(!rec.materials.Any(r=>r.path==AssetDatabase.GetAssetPath(m))||m.GetFloat("_AlphaClip")<.5f)continue;
    var c=m.GetColor("_BaseColor");m.SetColor("_BaseColor",new Color(c.r*tint.r,c.g*tint.g,c.b*tint.b,c.a));EditorUtility.SetDirty(m);canopies++;
   }
   // colour-bled atlases: same import as the source, mip alpha coverage preserved at the cards' own cutoff
   int bled=0,atlases=0;string leavesFile=A292+"/Highlands293/Textures/Leaves/leaves.json";
   if(File.Exists(leavesFile))
    foreach(var leaf in JsonUtility.FromJson<Leaves293>(File.ReadAllText(leavesFile)).items)
    {
     var targets=leaf.materials.Where(p=>rec.materials.Any(r=>r.path==p)).Select(AssetDatabase.LoadAssetAtPath<Material>).Where(m=>m!=null&&m.GetFloat("_AlphaClip")>.5f).ToArray();
     var source=AssetImporter.GetAtPath(leaf.source) as TextureImporter;if(targets.Length==0||source==null||!File.Exists(leaf.dilated))continue;
     AssetDatabase.ImportAsset(leaf.dilated);var importer=AssetImporter.GetAtPath(leaf.dilated) as TextureImporter;if(importer==null)continue;
     var settings=new TextureImporterSettings();source.ReadTextureSettings(settings);importer.SetTextureSettings(settings);
     importer.maxTextureSize=source.maxTextureSize;importer.textureCompression=source.textureCompression;importer.crunchedCompression=source.crunchedCompression;importer.compressionQuality=source.compressionQuality;
     importer.alphaIsTransparency=false;importer.mipmapEnabled=true;importer.mipMapsPreserveCoverage=true;importer.alphaTestReferenceValue=targets.Min(m=>m.GetFloat("_Cutoff"));importer.SaveAndReimport();
     var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(leaf.dilated);if(texture==null)continue;atlases++;
     foreach(var m in targets){m.SetTexture("_BaseMap",texture);EditorUtility.SetDirty(m);bled++;}
    }
   Save292();
   return "Candidate look: trilight ambient, sun 1.15; vegetation "+mats.Length+" materials (floor≥.40, saturation≥.55, light≥.78), distance wash enabled on "+washed+", dark base colours lifted on "+brightened+", ink-study plants converted to the lit vegetation shader "+converted+", leaf-card cutoff floor "+LeafCutoff293+" on "+clipped+", colour-bled coverage-preserving atlases "+atlases+" on "+bled+" materials, realm canopy tones on "+canopies+". Revert: look293-revert";
  }
 }
}
