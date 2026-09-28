using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.Core.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static class DemoTurtleAuthoring
 {
  const string Folder="Assets/_Project/Art/Demo/Summons/WaterTurtle";
  const string Original="Assets/_Project/Art/SpellVFX120/WaterTurtle";
  const string Model=Folder+"/SM_WaterTurtle_Combat.fbx";
  public const string ProfilePath=Folder+"/Combat_Om.asset";
  [Serializable] class BoundsData{public float[] size,center;}
  [Serializable] class ReleaseData{public float[] actorSpaceOriginUnity;public float maxVariationFromStartM;}
  [Serializable] class Measurements{public BoundsData idleBodyBoundsUnity;public ReleaseData castRelease;public string sourceSha256;}
  [Serializable] class Report
  {public string status;public List<string> passed=new List<string>(),failed=new List<string>();
   public int triangles,skins,bones,materials,maximumInfluences,unweighted;public float weightError;public Vector3 mouthOffset;
   public string[] unverified={"Native combat input","Moving-target combat","Actual terrain contact","Final mouth/cast animation","CPU/GPU performance"};}
  public static string Execute(string command){if(command=="apply")Apply();else if(command!="audit")throw new ArgumentException("apply/audit");return Audit();}
  static Measurements Read()
  {
   var m=JsonUtility.FromJson<Measurements>(File.ReadAllText(Path.Combine(DemoSummonAuthoring.Output,"WaterTurtle/combat_measurements.json")));
   if(m==null||m.idleBodyBoundsUnity?.size?.Length!=3||m.castRelease?.actorSpaceOriginUnity?.Length!=3||m.castRelease.maxVariationFromStartM>.002f)
    throw new InvalidOperationException("Verified rig and stable cast socket required");return m;
  }
  static Vector3 V(float[] v)=>new Vector3(v[0],v[1],v[2]);
  static Material MaterialAt(string name,Shader shader)
  {var m=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/"+name+".mat");if(m==null){m=new Material(shader){name=name};AssetDatabase.CreateAsset(m,Folder+"/"+name+".mat");}m.shader=shader;return m;}
  static void Apply()
  {
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
   if(EditorApplication.isPlaying||s==null||s.gameObject.scene.path!=DemoFoundationAuthoring.Scene)throw new InvalidOperationException("Dedicated demo in Edit mode required");
   var m=Read();AssetDatabase.ImportAsset(Model,ImportAssetOptions.ForceUpdate);
   var importer=(ModelImporter)AssetImporter.GetAtPath(Model);importer.importAnimation=true;importer.animationType=ModelImporterAnimationType.Generic;
   importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.optimizeGameObjects=false;importer.isReadable=true;
   importer.animationCompression=ModelImporterAnimationCompression.Off;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.SaveAndReimport();
   var clips=AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
   AnimationClip Clip(string n)=>clips.Single(c=>c.name.EndsWith(n,StringComparison.Ordinal));
   var idle=Clip("WT_Idle");var walk=Clip("WT_Walk");var cast=Clip("WT_WaterCast");
   if(Mathf.Abs(cast.length-1.8f)>.01f)throw new InvalidOperationException("Cast clip duration mismatch");
   var original=AssetDatabase.LoadAssetAtPath<Material>(Original+"/M_TurtleJade.mat");
   var shader=Shader.Find("Oheangbu/Demo/WaterJet");if(original==null||shader==null)throw new InvalidOperationException("Required material missing");
   var jet=MaterialAt("M_Turtle_WaterJet",shader);jet.SetColor("_BaseColor",new Color(.12f,.32f,.39f,.75f));
   jet.SetTexture("_Noise",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/KoreanTraditionalPattern_Effect/Textures/PublicTexture/Noise_02.png"));
   var foam=MaterialAt("M_Turtle_Foam",Shader.Find("Universal Render Pipeline/Particles/Unlit"));
   foam.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/KoreanTraditionalPattern_Effect/Textures/PublicTexture/Ball.png"));
   foam.SetColor("_BaseColor",Color.white);foam.SetFloat("_Surface",1);foam.SetFloat("_Blend",0);foam.SetFloat("_ZWrite",0);
   foam.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);foam.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
   foam.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");foam.renderQueue=3001;
   var temp=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
   try
   {
    temp.name="PF_WaterTurtle_Combat";foreach(var r in temp.GetComponentsInChildren<Renderer>(true))r.sharedMaterials=Enumerable.Repeat(original,r.sharedMaterials.Length).ToArray();
    (temp.GetComponent<Animator>()??temp.AddComponent<Animator>()).applyRootMotion=false;
    var p=AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(ProfilePath);
    if(p==null){p=ScriptableObject.CreateInstance<SummonCombatProfile>();AssetDatabase.CreateAsset(p,ProfilePath);}
    p.Letter="옴";p.Element=Element.Water;p.RootAttackEnabled=p.FlameAttackEnabled=p.TigerAttackEnabled=p.ClubAttackEnabled=false;p.WaterAttackEnabled=true;
    p.IdleClip=idle;p.WalkClip=walk;p.BackwardWalkClip=Clip("WT_BackWalk");p.AttackClip=p.WaterAttackClip=cast;p.WalkClipMetresPerSecond=.4f;p.FollowSpeed=.6f;
    p.AttackRange=p.WaterRange=6;p.WaterRadius=.22f;p.WaterTravelSpeed=12;p.WaterPreferredDistance=4;p.WaterMinimumDistance=2.5f;
    p.WaterWindupSeconds=.6f;p.WaterStreamSeconds=.7f;p.WaterRecoverySeconds=.5f;p.WindupSeconds=.6f;p.CooldownSeconds=2;
    p.WaterDamageMultiplier=p.DamageMultiplier=1;p.WaterOriginOffset=V(m.castRelease.actorSpaceOriginUnity);
    p.WaterJetMaterial=jet;p.WaterFoamMaterial=foam;
    var size=V(m.idleBodyBoundsUnity.size);p.FootprintWidth=size.x+.06f;p.FootprintLength=size.z+.06f;p.BodyHeight=size.y+.05f;
    p.FootprintOffset=Vector3.zero;p.FormationSeal=AssetDatabase.LoadAssetAtPath<GameObject>(Original+"/KTP_Om_Seal.prefab");
    p.DissolveDebris=AssetDatabase.LoadAssetAtPath<GameObject>(Original+"/PF_Om_Dissolve.prefab");
    p.PresentationPrefab=PrefabUtility.SaveAsPrefabAsset(temp,Folder+"/PF_WaterTurtle_Combat.prefab");
    if(!p.TryValidate(out string error,true))throw new InvalidOperationException(error);
    var manager=s.Walker.Wiring.GetComponent<DemoSummonCombatManager>();manager.Profiles=manager.Profiles.Where(x=>x!=null&&x.Letter!="옴").Append(p).ToArray();
    EditorUtility.SetDirty(p);EditorUtility.SetDirty(jet);EditorUtility.SetDirty(foam);EditorUtility.SetDirty(manager);AssetDatabase.SaveAssets();
    EditorSceneManager.MarkSceneDirty(s.gameObject.scene);EditorSceneManager.SaveScene(s.gameObject.scene);
   }
   finally{Object.DestroyImmediate(temp);}
  }
  static string Audit()
  {
   var r=new Report();void Check(bool ok,string text)=>(ok?r.passed:r.failed).Add(text);
   var p=AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(ProfilePath);Check(p!=null&&p.TryValidate(out _,true),"Valid water combat profile");
   if(p!=null&&p.PresentationPrefab!=null)
   {
    r.mouthOffset=p.WaterOriginOffset;var skins=p.PresentationPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);r.skins=skins.Length;
    r.bones=skins.SelectMany(s=>s.bones).Distinct().Count();
    foreach(var skin in skins)
    {for(int i=0;i<skin.sharedMesh.subMeshCount;i++)r.triangles+=(int)skin.sharedMesh.GetIndexCount(i)/3;r.materials+=skin.sharedMaterials.Length;
     var counts=skin.sharedMesh.GetBonesPerVertex();var weights=skin.sharedMesh.GetAllBoneWeights();
     try{int cursor=0;foreach(byte n in counts){r.maximumInfluences=Mathf.Max(r.maximumInfluences,n);if(n==0)r.unweighted++;float sum=0;for(int i=0;i<n;i++)sum+=weights[cursor++].weight;r.weightError=Mathf.Max(r.weightError,Mathf.Abs(sum-1));}}
     finally{counts.Dispose();weights.Dispose();}}
    Check(r.triangles==16648&&r.materials==1&&r.skins==1,"Approved turtle topology and one material retained");
    Check(r.maximumInfluences<=4&&r.unweighted==0&&r.weightError<.0001f,"Normalized maximum four skin weights");
    Check(p.PresentationPrefab.GetComponentsInChildren<Collider>(true).Length==0,"No physical blocker or enemy target collider");
    Check(p.IdleClip&&p.WalkClip&&p.BackwardWalkClip&&p.AttackClip&&Mathf.Abs(p.AttackClip.length-1.8f)<.01f,"Idle, forward/backward walk and cast match shared duration");
    Check(p.WaterJetMaterial&&p.WaterFoamMaterial&&p.FormationSeal&&p.DissolveDebris,"Water surface and original formation/dissolve references assigned");
    Check(Vector3.Distance(p.WaterOriginOffset,V(Read().castRelease.actorSpaceOriginUnity))<.001f,"Origin matches verified planted mouth socket");
    var manager=Object.FindFirstObjectByType<DemoSummonCombatManager>();
    Check(manager!=null&&new[]{"곰","놈","솜","몸","옴"}.All(n=>manager.Profiles.Count(x=>x!=null&&x.Letter==n)==1),"All five combat profiles present exactly once");
   }
   r.status=r.failed.Count==0?"PASS":"FAIL";string json=JsonUtility.ToJson(r,true);File.WriteAllText(Path.Combine(DemoSummonAuthoring.Output,"water_scene_audit.json"),json);return json;
  }
 }
}
