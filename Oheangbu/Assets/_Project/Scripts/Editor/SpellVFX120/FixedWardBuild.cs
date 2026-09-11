using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.SpellVFX120;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class FixedWardBuild
 {
  public const string Folder="Assets/_Project/Art/SpellVFX120/FixedWards";
  public static string Output=>Path.Combine(Vfx120Editor.Output,"FixedWards");
  public static Vfx120Profile Profile(string g)=>AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset").Entries.Single(x=>x.Glyph==g).Profile;
  public static string Build(string glyph)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||!new[]{"구","무","수","우","누"}.Contains(glyph))throw new Exception("Stopped editor and one ward required");
   if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"FixedWards");
   var p=Profile(glyph);if(!File.Exists(Folder+"/Baseline_"+glyph+".asset"))AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(p),Folder+"/Baseline_"+glyph+".asset");
   int family=glyph=="구"?0:glyph=="누"?1:glyph=="무"?2:glyph=="수"?3:4;
   p.WardKind=glyph=="구"?Vfx120WardKind.Wood:glyph=="무"?Vfx120WardKind.Earth:glyph=="수"?Vfx120WardKind.Metal:glyph=="우"?Vfx120WardKind.Water:Vfx120WardKind.Fire;
   p.WardRadius=3;p.WardHeight=2.2f;p.WardFormation=.45f;p.WardFade=.5f;p.Duration=4;
   var shader=Shader.Find("Oheangbu/FixedWard");if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Ward shader error");
   string path=Folder+"/Surface_"+glyph+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}
   var colors=new[]{new Color(.28f,.45f,.21f),new Color(.8f,.23f,.11f),new Color(.47f,.35f,.21f),new Color(.65f,.69f,.66f),new Color(.18f,.4f,.52f)};
   mat.SetColor("_BaseColor",colors[family]);mat.SetFloat("_Mode",(int)p.WardKind);EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);p.WardMaterial=mat;
   p.WardPatternPrefab=Pattern(family);p.WardContactPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120Editor.AssetRoot+"/KtpEmphasis/SmallPatternContact_"+family+".prefab");p.WardDebrisPrefab=Debris(glyph,family,colors[family]);
   if(glyph=="누")
   {
    string debrisPath=AssetDatabase.GetAssetPath(p.WardDebrisPrefab);var edit=PrefabUtility.LoadPrefabContents(debrisPath);
    try{foreach(var ps in edit.GetComponentsInChildren<ParticleSystem>())if(ps.name=="Fragments"){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.startColor=new Color(.12f,.10f,.085f,.8f);}PrefabUtility.SaveAsPrefabAsset(edit,debrisPath);}finally{PrefabUtility.UnloadPrefabContents(edit);}
   }
   if(glyph=="우")
   {
    var ws=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"/WardWater.shadergraph");if(ws==null||ShaderUtil.ShaderHasError(ws))throw new Exception("Ward PolyOne copy failed");
    string wp=Folder+"/PolyOneWard.mat";var water=AssetDatabase.LoadAssetAtPath<Material>(wp);if(water==null){water=new Material(AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/AreaFive/PolyOneWave.mat"));water.shader=ws;water.SetFloat("_WardOpacity",.38f);AssetDatabase.CreateAsset(water,wp);}p.WardWaterMaterial=water;
   }
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);Directory.CreateDirectory(Path.Combine(Output,glyph));File.WriteAllText(Path.Combine(Output,glyph,"settings.json"),EditorJsonUtility.ToJson(p,true));
   return "WARD_BUILT "+glyph+"; VFX only; radius3 height2.2 duration4";
  }
  static GameObject Pattern(int family)
  {
   string path=Folder+"/Pattern_"+(family==3?"MetalGrid":family.ToString())+".prefab";var old=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(old!=null)return old;
   GameObject go;
   if(family==3)
   {
    var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KoreanTraditionalPattern_Effect/Prefabs/Bottom/Bottom09-01.prefab");var pattern=source.GetComponentsInChildren<ParticleSystem>(true).First(x=>x.name=="Pattern");go=new GameObject("KTP_MetalGrid_Ward");var upright=new GameObject("Upright");upright.transform.SetParent(go.transform,false);upright.transform.localRotation=Quaternion.Euler(90,0,0);var copy=Object.Instantiate(pattern.gameObject,upright.transform,false);copy.transform.localPosition=Vector3.zero;copy.SetActive(true);
   }
   else go=Object.Instantiate(KtpQuickCastBuild.Pattern(Vfx120Editor.AssetRoot+"/KtpEmphasis",family,true));
   try
   {
    foreach(var ps in go.GetComponentsInChildren<ParticleSystem>())
    {ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.startLifetime=4;main.duration=4;main.loop=false;main.startDelay=0;main.startSpeed=0;main.maxParticles=1;main.scalingMode=ParticleSystemScalingMode.Hierarchy;var em=ps.emission;em.rateOverTime=0;em.rateOverDistance=0;em.SetBursts(new[]{new ParticleSystem.Burst(0,1)});var c=ps.colorOverLifetime;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.55f,.1f),new GradientAlphaKey(.4f,.86f),new GradientAlphaKey(0,1)});c.color=gradient;}
    return PrefabUtility.SaveAsPrefabAsset(go,path);
   }finally{Object.DestroyImmediate(go);}
  }
  static GameObject Debris(string glyph,int family,Color color)
  {
   string path=Folder+"/Debris_"+glyph+".prefab";var old=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(old!=null)return old;var go=new GameObject("WardDebris_"+glyph);
   try
   {
    KtpOffsetGuardBuild.Debris(go,Folder,"Fragments",family==3?9:16,color,1,.5f,family==0?new Vector3(.07f,.02f,.03f):family==3?new Vector3(.035f,.008f,.05f):new Vector3(.04f,.04f,.04f),.2f);
    if(family==0)KtpOffsetGuardBuild.Debris(go,Folder,"Leaves",8,new Color(.28f,.45f,.15f),.4f,.55f,new Vector3(.055f,.008f,.09f),.25f);
    if(family==1||family==2||family==4)
    {
     var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;m.playOnAwake=false;m.loop=false;m.duration=.5f;m.startLifetime=.45f;m.startSpeed=new ParticleSystem.MinMaxCurve(.25f,1.2f);m.startSize=family==1?.24f:family==4?.12f:.2f;m.startColor=family==1?new Color(1,.43f,.1f,.9f):family==4?new Color(.65f,.84f,.9f,.6f):new Color(.5f,.39f,.24f,.4f);m.maxParticles=24;
     var e=ps.emission;e.rateOverTime=0;e.SetBursts(new[]{new ParticleSystem.Burst(0,16)});var sh=ps.shape;sh.shapeType=ParticleSystemShapeType.Hemisphere;sh.radius=.07f;
     var c=ps.colorOverLifetime;c.enabled=true;var grad=new Gradient();grad.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});c.color=grad;
     var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/FireBolt/"+(family==1?"M_Flame.mat":"M_Smoke.mat"));
    }
    foreach(var ps in go.GetComponentsInChildren<ParticleSystem>()){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;m.playOnAwake=false;m.loop=false;m.simulationSpace=ParticleSystemSimulationSpace.World;m.startLifetime=new ParticleSystem.MinMaxCurve(.25f,.55f);m.duration=.55f;}
    return PrefabUtility.SaveAsPrefabAsset(go,path);
   }finally{Object.DestroyImmediate(go);}
  }
 }
}
