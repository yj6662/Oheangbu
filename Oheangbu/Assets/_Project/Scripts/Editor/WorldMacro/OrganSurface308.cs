using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Oheangbu.App;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 공격 예고 = 모델 기관 표면 발광(SPEC-TELEGRAPH-ORGAN-308, D308-4) — 저작 원장. Queue-safe: refusals come back as strings, never a dialog.
 // One scene per call (Play / dirty scene / scene outside the list = refusal). Saves only the target prefab / scene / asset (no SaveAssets).
 //   assets               overlay material Art/Telegraph308/M_InkOrganSurface308 (Oheangbu/InkOrganSurface) + timing.SurfaceMaterial;
 //                        reports the shader's queue / passes / errors (AC-T3 static part)
 //   masks[:ember]        import settings of the organ masks: Single Channel R8, sRGB off, clamp, uncompressed, 2048 (crystal + ridge +
 //                        the D308-4d contamination masks Contam308/T_Organ308_Contam_<species>.png that the placement rows name;
 //                        the shared fire-haetae ember mask is only reported unless ':ember' — four ember materials sample it)
 //   dry[:prefabs|:<scene>]  what prefabs / apply would do (parts, organs, surfaces, mask coverage, front check). No writes
 //   prefabs              the six Folklore298 species prefabs: add the magic-stone crystal of the placement row (D308-4b/4c one big crystal;
 //                        D308-4e the SHAPE follows the actor's element: SM_Organ308_Shard = neutral (v3), SM_Organ308_Shard_<Wood|Fire|
 //                        Earth|Metal|Water> — row.mesh, cross-checked against row.element and the live attack profile; every shape shares
 //                        the one matte material M_Organ308_Shard.mat) as a MeshRenderer child "Organ308_maseok_shard" under the bone /
 //                        pose / size that shard308_placements.json (same folder, organ-art track) gives for that species; no collider,
 //                        shadows on, FolkloreReadability298 target; rebuild the organ set (TelegraphOrgans306 species table) and write the
 //                        D308-4d contamination of the row onto the shard organ (ContamRenderer = the body skin the bone drives, Texture =
 //                        body-UV0 R8 geodesic vein mask, Sphere = world-distance fallback for a body without usable UV0, None = no
 //                        contamination). No placement row = no part (never guessed)
 //   revert:prefabs       ledger only: removes the parts / readability targets it added and restores the recorded organs
 //   apply:<scene>        scene actors: south gate spear tip, dragon (mouth / eyes / ridge chain), growth-lesson shard (added under
 //                        Tree_00), mine_fire/0 ember mask, MineTutorialBoss306 (crystal mask, both shoulders Core); species readability
 //                        targets for the prefab parts. Ledger order: Architecture296 → Folklore298 → W_Demo_Main (Main is the promoted copy)
 //   revert:<scene>       ledger only
 //   status               ledger + the three scenes' organ signatures compared actor by actor (AC-T9; rerun 'apply:' after a re-promotion)
 //   check                AC-T6 parts (shard shape = the row's element shape, one shared matte material), AC-T8 masks (+ coverage > 0
 //                        per Mask organ), AC-C contamination (renderer / mode / mask import / UV0), AC-T5 front-facing organ per attack key,
 //                        submesh = 1, crystal shape = the live attack-profile element of every actor in the open scene, species FBX /
 //                        material byte hashes vs the first prefabs run — prefabs + the open scene when it is one of the three
 //   debug-view:on|off    Play only: _DebugView 1 on every live telegraph (AC-T8 capture: the region alone in white). Never saved
 //   debug-view:contam:on|off  Play only: _DebugView 2 (AC-C capture: the contamination veins alone in white). Never saved
 //   contam:status        Play only, read-only: contamination surfaces / attached now (= extra draws) / overlay materials per live telegraph (AC-C5)
 //   contam:off|on        Play only: suppress / restore the persistent contamination overlays (A/B capture, draw-call comparison). Never saved
 // Ledger: Art/Playtest306/Organ308/ledger.json · backups: Art/Playtest306/Backups/Organ308/<stamp>/<asset path> (before the first write).
 // Protected trees (Watershed295*, Reworld292*, MountainTrail285*), W_Demo_Compact.unity and 03_Content.asset are never touched.
 public static class OrganSurface308
 {
  const string Arch296="Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity";
  const string Folk298="Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity";
  const string Main="Assets/_Project/Scenes/World/W_Demo_Main.unity";
  static readonly string[] Scenes={Arch296,Folk298,Main};
  static readonly string[] Species={"dokkaebi","agwi","changgui","bulgasari","fox_spirit","imugi"};
  static readonly string[] SceneActors={"south_gate_general","cheongryong","demo_growth_lesson","mine_fire/0"};
  const string PrefabRoot="Assets/_Project/Art/Characters/Folklore298/Prefabs/PF_";
  internal const string PartRoot="Assets/_Project/Art/Characters/Organs308";
  // D308-4b/4c: one big crystal per enemy (organ-art track Tools/Blender/Organ308/shard308*.py → Art/Characters/Organ308/_ProjectAssets/.../Shard308).
  // D308-4e: six crystal shapes (neutral = the v3 SM_Organ308_Shard, + one per element) on the same frame (pivot = where the axis meets the
  // body, +Y = axis, same root, same scale k) — the placement row names the shape. ShardFbx = the neutral shape (GUID kept since v2)
  internal const string ShardMeshName="SM_Organ308_Shard";
  internal static readonly string[] ShardElements={"Neutral","Wood","Fire","Earth","Metal","Water"};   // order = Oheangbu.Core.Domain.Element after Neutral
  internal const string ShardDir=PartRoot+"/Shard308",ShardFbx=ShardDir+"/"+ShardMeshName+".fbx",ShardMat=ShardDir+"/M_Organ308_Shard.mat",ShardJson=ShardDir+"/shard308_placements.json";
  // D308-4d contamination masks (organ-art track Tools/Blender/Organ308/contam308.py → _ProjectAssets/.../Organs308/Contam308)
  internal const string ContamDir=PartRoot+"/Contam308";
  const string ArtRoot="Assets/_Project/Art/Telegraph308",SurfaceMat=ArtRoot+"/M_InkOrganSurface308.mat";
  const string TimingPath="Assets/_Project/Resources/Telegraph306/EnemyTelegraphTiming306.asset";
  internal const string CrystalMask="Assets/_Project/Art/Characters/MineBoss306/Textures/T_MineBoss306_CrystalMask.png";
  internal const string RidgeMask="Assets/_Project/Art/Characters/Folklore298/Textures/cheongryong/T_cheongryong_RidgeMask.png";
  internal const string EmberMask="Assets/_Project/Art/SpellVFX120/FireHaetae/Textures/T_FireHaetae_EmberMask.png";
  static readonly string[] Protected={"Assets/_Project/Art/World/Watershed295","Assets/_Project/Art/World/Reworld292","Assets/_Project/Art/World/MountainTrail285","W_Demo_Compact.unity","03_Content.asset"};
  const int MaxPartTriangles=300;    // AC-T6 [TEST] — D308-4c one-crystal budget (v2 D308-4b cluster: 600, v1 per-species parts: 1,500)
  static string Repo=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
  static string OutRoot=>Path.Combine(Repo,"Art/Playtest306/Organ308");
  static string LedgerFile=>Path.Combine(OutRoot,"ledger.json");
  static string BackupRoot=>Path.Combine(Repo,"Art/Playtest306/Backups/Organ308");

  // ---------------------------------------------------------------- ledger
  // contam* = D308-4d (absent in pre-4d ledger rows → None: what those organs had)
  [Serializable] class OrganDto{public string id,anchor,boneName,renderer,mask,contamRenderer,contamMask;public int role,human,mode,contamMode;public float radius,contamRadius;public Vector3 offset,surfacePoint,surfaceNormal,contamPoint;public string[] keys;}
  [Serializable] class Entry{public string target,path,actor,stamp,after;public bool organsWritten;public List<OrganDto> before=new List<OrganDto>();public List<string> addedParts=new List<string>();public List<string> addedReadability=new List<string>();}
  [Serializable] class HashRow{public string path,sha256;}
  [Serializable] class Ledger{public List<Entry> entries=new List<Entry>();public List<string> backups=new List<string>();public List<HashRow> hashes=new List<HashRow>();}
  // D308-4b 기관 부위 = 공통 마석 파편: every owner carries the same shard; per owner only the placement (bone, pose, size) comes from
  // shard308_placements.json (JsonUtility). localPosition = MESH BOUNDS CENTRE in bone space, localEulerAngles = Unity Euler,
  // worldSize = world max extent (m). role / keys there are informational and cross-checked against TelegraphOrgans306.ShardSpec. All TEST.
  // D308-4e: element = the owner's element (attack profile Elemental / Element; Neutral when not elemental), elementSource = that profile
  //   asset, mesh = the shape (localPosition / worldSize are that shape's bounds centre / max extent — same pivot and scale k as v3).
  // D308-4d: contamMode None | Texture | Sphere, contamMask = body-UV0 R8 mask (Texture), contamRadius = geodesic bake radius (m, Texture: a
  //   record; Sphere: the shader's world radius), contamLocalPoint = the crystal pivot (where the axis meets the body) in BONE space.
  [Serializable] internal class ShardPlacement{public string owner,organ,role,bone,element,mesh,elementSource,contamMode,contamMask,contamNote;public string[] keys;public Vector3 localPosition,localEulerAngles,contamLocalPoint;public float worldSize,radius,contamRadius;}
  [Serializable] internal class ShardShape{public string element,mesh;public int triangles,islands;public Vector3 meshBoundsCenter,meshBoundsSize;}
  [Serializable] internal class ShardDoc{public string schema,mesh,material,organ;public Color baseColor=new Color(.208f,.255f,.243f,1f);public float smoothness=.15f,metallic;public int maxTriangles,triangles;public Vector3 meshBoundsCenter,meshBoundsSize;public List<ShardShape> shapes=new List<ShardShape>();public List<ShardPlacement> placements=new List<ShardPlacement>();}
  internal static readonly string[] ShardOwners={"dokkaebi","agwi","changgui","bulgasari","fox_spirit","imugi","demo_growth_lesson"};

  public static string Run(string c)
  {
   c=(c??"").Trim();
   try
   {
    if(c.StartsWith("debug-view:contam:",StringComparison.Ordinal))return DebugView(c.Substring(18).Trim()=="on",true);
    if(c.StartsWith("debug-view:",StringComparison.Ordinal))return DebugView(c.Substring(11).Trim()=="on",false);
    if(c.StartsWith("contam:",StringComparison.Ordinal))return Contam(c.Substring(7).Trim());
    if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Play mode";
    // an open Prefab Mode with unsaved edits: opening a scene / saving a prefab under it could prompt or clobber — refuse (queue-safe)
    var stage=PrefabStageUtility.GetCurrentPrefabStage();
    if(stage!=null&&stage.scene.isDirty&&c!="status"&&c!="check")return "REFUSED Prefab Mode has unsaved changes ("+stage.assetPath+") — save or close it first";
    if(c=="assets")return Assets();
    if(c=="masks"||c=="masks:ember")return Masks(c=="masks:ember");
    if(c=="status")return Status();
    if(c=="check")return Check();
    if(c=="prefabs")return Prefabs("apply");
    if(c=="revert:prefabs")return Prefabs("revert");
    if(c=="dry"||c=="dry:prefabs")return Prefabs("dry");
    if(c.StartsWith("dry:",StringComparison.Ordinal))return SceneCommand("dry",c.Substring(4).Trim());
    if(c.StartsWith("apply:",StringComparison.Ordinal))return SceneCommand("apply",c.Substring(6).Trim());
    if(c.StartsWith("revert:",StringComparison.Ordinal))return SceneCommand("revert",c.Substring(7).Trim());
    return "REFUSED unknown command "+c+" (assets | masks[:ember] | dry[:prefabs|:<scene>] | prefabs | revert:prefabs | apply:<scene> | revert:<scene> | status | check | debug-view[:contam]:on|off | contam:status|on|off)";
   }
   catch(Exception e){return "REFUSED "+e.GetType().Name+": "+e.Message+"\n"+e.StackTrace;}
  }

  // ---------------------------------------------------------------- assets
  static string Assets()
  {
   var shader=Shader.Find(EnemyTelegraphTimingSO.SurfaceShaderName);
   if(shader==null)return "REFUSED shader "+EnemyTelegraphTimingSO.SurfaceShaderName+" not imported (deploy _ProjectAssets/Shaders/InkOrganSurface.shader to Assets/_Project/Shaders, then refresh)";
   Folder(ArtRoot);var lines=new List<string>();
   var mat=AssetDatabase.LoadAssetAtPath<Material>(SurfaceMat);
   if(mat==null){mat=new Material(shader){name="M_InkOrganSurface308"};AssetDatabase.CreateAsset(mat,SurfaceMat);lines.Add("created "+SurfaceMat);}
   else if(mat.shader!=shader){mat.shader=shader;EditorUtility.SetDirty(mat);lines.Add("shader rebound");}
   var t=AssetDatabase.LoadAssetAtPath<EnemyTelegraphTimingSO>(TimingPath);
   if(t==null)lines.Add("WARN timing "+TimingPath+" missing (TelegraphOrgans306 assets creates it)");
   else
   {
    if(t.SurfaceMaterial!=mat){t.SurfaceMaterial=mat;EditorUtility.SetDirty(t);lines.Add("timing.SurfaceMaterial bound");}
    float bright=Mathf.Min(1f,t.MaxBrightness);mat.SetFloat("_Brightness",bright);mat.SetFloat("_PaperBrightness",Mathf.Min(bright,t.PaperBrightness));
    mat.SetFloat("_RingScale",t.RingScale);mat.SetFloat("_RingWidth",t.RingWidth);mat.SetFloat("_Feather",t.Feather);mat.SetFloat("_MaskThreshold",t.MaskThreshold);mat.SetFloat("_ZBias",t.ZBias);
    // D308-4d template defaults (the overlay rewrites them every frame from the SO; the template keeps contamination off: _ContamMode 0)
    if(mat.HasProperty("_ContamDark")){mat.SetVector("_ContamDark",new Vector4(t.ContaminationDarkNear,t.ContaminationDarkFar,t.ContaminationInkMix,t.ContaminationTintAlpha));mat.SetFloat("_ContamFlowSoft",t.ContaminationFlowSoftness);mat.SetFloat("_ContamMaxLod",t.ContaminationMaxMip);mat.SetFloat("_ContamMode",0f);}
    EditorUtility.SetDirty(mat);
    AssetDatabase.SaveAssetIfDirty(t);
   }
   AssetDatabase.SaveAssetIfDirty(mat);
   // AC-T3 static: queue ≥ 3000, no ShadowCaster / DepthOnly / DepthNormals, no shader errors
   var passes=new List<string>();for(int i=0;i<mat.passCount;i++)passes.Add(mat.GetPassName(i));
   bool depth=mat.FindPass("ShadowCaster")>=0||mat.FindPass("DepthOnly")>=0||mat.FindPass("DepthNormals")>=0||mat.FindPass("DepthNormalsOnly")>=0;
   int messages=ShaderUtil.GetShaderMessageCount(shader);bool error=ShaderUtil.ShaderHasError(shader);
   return "assets: "+(lines.Count>0?string.Join("; ",lines):"present")+" | shader queue "+shader.renderQueue+(shader.renderQueue>=3000?" ok":" FAIL(<3000)")+
    ", passes ["+string.Join(",",passes)+"]"+(depth?" FAIL(depth/shadow pass)":" ok(no depth/shadow pass)")+", errors "+(error?"FAIL":"0")+" messages "+messages+
    (t!=null?" | MaxBrightness "+F(t.MaxBrightness)+" Paper "+F(Mathf.Min(t.MaxBrightness,t.PaperBrightness))+" Ring "+F(t.RingScale)+"/"+F(t.RingWidth)+" Feather "+F(t.Feather)+" VisibleDot "+F(t.VisibleDot)+
     " | contamination dark "+F(t.ContaminationDarkNear)+"/"+F(t.ContaminationDarkFar)+" ink "+F(t.ContaminationInkMix)+" tint "+F(t.ContaminationTintAlpha)+" attach "+F(t.ContaminationAttachDistance)+"+"+F(t.ContaminationDetachMargin)+" m, fade from "+F(t.ContaminationFadeStartDistance)+" m, max mip "+F(t.ContaminationMaxMip):"");
  }

  // ---------------------------------------------------------------- masks
  static string Masks(bool ember)
  {
   var sb=new StringBuilder("masks:\n");
   foreach(var path in MaskPaths())
   {
    var ti=AssetImporter.GetAtPath(path) as TextureImporter;
    if(ti==null){sb.AppendLine("  "+path+": missing"+(path==RidgeMask?" (ridge bake by the organ-art track; dragon chain stays Sphere — Temporary Exception)":path==CrystalMask?" (deploy Tools/Unity/Stage308_organ/_ProjectAssets copy first)":path.StartsWith(ContamDir,StringComparison.Ordinal)?" (deploy the organ-art Contam308 folder first)":""));continue;}
    bool shared=path==EmberMask&&!ember;
    if(!shared)
    {
     bool changed=false;
     if(ti.textureType!=TextureImporterType.SingleChannel){ti.textureType=TextureImporterType.SingleChannel;changed=true;}
     var settings=new TextureImporterSettings();ti.ReadTextureSettings(settings);
     if(settings.singleChannelComponent!=TextureImporterSingleChannelComponent.Red){settings.singleChannelComponent=TextureImporterSingleChannelComponent.Red;ti.SetTextureSettings(settings);changed=true;}
     if(ti.sRGBTexture){ti.sRGBTexture=false;changed=true;}
     if(!ti.mipmapEnabled){ti.mipmapEnabled=true;changed=true;}
     if(ti.wrapMode!=TextureWrapMode.Clamp){ti.wrapMode=TextureWrapMode.Clamp;changed=true;}
     if(ti.textureCompression!=TextureImporterCompression.Uncompressed){ti.textureCompression=TextureImporterCompression.Uncompressed;changed=true;}
     if(ti.maxTextureSize!=2048){ti.maxTextureSize=2048;changed=true;}
     if(ti.isReadable){ti.isReadable=false;changed=true;}
     if(changed)ti.SaveAndReimport();
     sb.Append("  "+path+(changed?": reimported":": already set"));
    }
    else sb.Append("  "+path+": shared by the ember materials — import untouched (masks:ember to force)");
    var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    sb.AppendLine(" | type "+ti.textureType+" sRGB "+ti.sRGBTexture+" format "+(tex!=null?tex.format.ToString():"-")+" size "+(tex!=null?tex.width+"x"+tex.height:"-")+(tex!=null&&tex.format==TextureFormat.R8&&!ti.sRGBTexture?" (R8 linear ok)":" (not R8 linear)"));
   }
   return sb.ToString().TrimEnd();
  }

  // the organ masks + every D308-4d contamination mask a placement row names (Texture mode)
  static List<string> MaskPaths()
  {
   var list=new List<string>{CrystalMask,RidgeMask,EmberMask};
   var doc=LoadShardDoc(out _);
   if(doc!=null)foreach(var p in doc.placements)if(p!=null&&ContamModeOf(p)==EnemyContaminationMode.Texture&&!string.IsNullOrEmpty(p.contamMask)&&!list.Contains(p.contamMask))list.Add(p.contamMask);
   return list;
  }

  // ---------------------------------------------------------------- prefabs
  static string Prefabs(string verb)
  {
   var led=Load();var sb=new StringBuilder("prefabs ("+verb+"):\n");string stamp=Stamp();bool ledgerChanged=false;
   var doc=LoadShardDoc(out string docNote);sb.AppendLine("  shard: "+ShardFbx+(ResolveShard()!=null?" ok":" MISSING")+" | "+docNote);
   using(var cache=new SurfaceCache())
   foreach(var id in Species)
   {
    string path=PrefabRoot+id+".prefab";
    if(AssetDatabase.LoadAssetAtPath<GameObject>(path)==null){sb.AppendLine("  "+id+": missing "+path);continue;}
    if(IsProtected(path)){sb.AppendLine("  "+id+": protected — skipped");continue;}
    var root=PrefabUtility.LoadPrefabContents(path);
    try
    {
     var set=root.GetComponent<EnemyOrganSet>()??root.GetComponentInChildren<EnemyOrganSet>(true);
     if(set==null){sb.AppendLine("  "+id+": no organ set (run TelegraphOrgans306 apply first) — skipped");continue;}
     string setPath=Rel(root.transform,set.transform);
     var entry=led.entries.FirstOrDefault(e=>e.target==path&&e.path==setPath);
     if(verb=="revert")
     {
      if(entry==null){sb.AppendLine("  "+id+": nothing recorded");continue;}
      sb.AppendLine("  "+id+": "+RevertEntry(root.transform,set,entry,out bool did));
      if(did){Backup(path,stamp,led);PrefabUtility.SaveAsPrefabAsset(root,path);}   // nothing undone = prefab not rewritten
      led.entries.Remove(entry);ledgerChanged=true;continue;
     }
     bool write=verb=="apply";bool changed=false;var notes=new List<string>();
     if(write&&entry==null){entry=new Entry{target=path,path=setPath,actor=id,stamp=stamp,before=(set.Organs??new EnemyOrganSet.Organ[0]).Where(o=>o!=null).Select(o=>Dto(set.transform,o)).ToList()};led.entries.Add(entry);ledgerChanged=true;RecordHashes(root,led);}
     notes.Add(ShardOrganId+": "+EnsurePart(root.transform,root.transform,id,doc,write,root.transform,entry,ref changed,out bool placed));
     // no shard placed (not delivered / no placement row / no bone): leave the #306 organs as they are (the species are neutral in
     // content and never light either way) — an organ set pointing at a part that is not there would only be unbound
     if(!placed){sb.AppendLine("  "+id+": shard not placed — organs untouched: "+string.Join(" | ",notes));continue;}
     var organs=TelegraphOrgans306.Build(root.transform,root.transform,TelegraphOrgans306.SpeciesSpecs(id),cache,out string how);
     notes.Add(how);
     notes.Add(ApplyContamination(organs,root.transform,RowOf(doc,id)));   // D308-4d
     string before=Signature(set.transform,set.Organs),after=Signature(set.transform,organs);
     if(write)
     {
      if(before!=after){set.Organs=organs;EditorUtility.SetDirty(set);changed=true;entry.organsWritten=true;}
      changed|=EnsureReadability(set,organs,root.transform,entry,notes);
      if(changed){Backup(path,stamp,led);PrefabUtility.SaveAsPrefabAsset(root,path);entry.after=after;ledgerChanged=true;}
     }
     sb.AppendLine("  "+id+(verb=="dry"?(before!=after?" [organs would change]":" [organs same]"):changed?" [saved]":" [no change]")+": "+string.Join(" | ",notes));
    }
    finally{PrefabUtility.UnloadPrefabContents(root);}
   }
   if(ledgerChanged)Save(led);
   return sb.ToString().TrimEnd();
  }

  // ---------------------------------------------------------------- scenes
  static string SceneCommand(string verb,string target)
  {
   string path=ResolveScene(target);if(path==null)return "REFUSED scene not in the #308 list: "+target+" ("+string.Join(", ",Scenes)+")";
   for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)return "REFUSED dirty scene "+SceneManager.GetSceneAt(i).path;
   if(!File.Exists(Abs(path)))return path+": missing";
   var led=Load();
   if(verb=="apply")
   {
    int at=Array.IndexOf(Scenes,path);var warn=new List<string>();
    for(int i=0;i<at;i++)if(!led.entries.Any(e=>e.target==Scenes[i]))warn.Add(Path.GetFileNameWithoutExtension(Scenes[i]));
    if(warn.Count>0)Debug.LogWarning("[OrganSurface308] ledger order: "+string.Join(", ",warn)+" not applied yet before "+Path.GetFileNameWithoutExtension(path));
   }
   var scene=SceneManager.GetActiveScene().path==path?SceneManager.GetActiveScene():EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
   var sb=new StringBuilder(path+" ("+verb+"):\n");string stamp=Stamp();bool changed=false,ledgerChanged=false;
   if(verb=="revert")
   {
    var entries=led.entries.Where(e=>e.target==path).ToList();
    if(entries.Count==0)return path+": nothing recorded";
    foreach(var e in entries)
    {
     var owner=FindPath(scene,e.path);
     if(owner==null){sb.AppendLine("  "+e.path+": not found (recorded entry kept)");continue;}
     var set=owner.GetComponent<EnemyOrganSet>();sb.AppendLine("  "+(e.actor??e.path)+": "+RevertEntry(owner,set,e,out bool did));
     led.entries.Remove(e);ledgerChanged=true;changed|=did;   // the scene is saved only when something was actually undone
    }
    if(changed){Backup(path,stamp,led);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}
    if(ledgerChanged)Save(led);
    return sb.ToString().TrimEnd();
   }
   bool write=verb=="apply";
   var actors=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PrologueEncounter>(true)).ToArray();
   var doc=LoadShardDoc(out string docNote);sb.AppendLine("  shard: "+(ResolveShard()!=null?"ok":"MISSING "+ShardFbx)+" | "+docNote);
   using(var cache=new SurfaceCache())
   {
    foreach(var id in SceneActors)
    {
     var actor=actors.FirstOrDefault(a=>a.Id==id);if(actor==null){sb.AppendLine("  "+id+": not in scene");continue;}
     if(IsProtected(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(actor.gameObject))){sb.AppendLine("  "+id+": protected prefab — skipped");continue;}
     if(!actor.GetComponentsInChildren<Renderer>(true).Any(r=>r.enabled)){sb.AppendLine("  "+id+": visuals hidden — skipped");continue;}
     var set=actor.GetComponent<EnemyOrganSet>();
     if(set==null){sb.AppendLine("  "+id+": no actor organ set (run TelegraphOrgans306 apply:"+path+" first) — skipped");continue;}
     var visual=actor.transform.Find("Folklore298_Visual");if(visual==null)visual=actor.transform;
     string hp=HierarchyPath(set.transform);var entry=led.entries.FirstOrDefault(e=>e.target==path&&e.path==hp);
     if(write&&entry==null){entry=new Entry{target=path,path=hp,actor=id,stamp=stamp,before=(set.Organs??new EnemyOrganSet.Organ[0]).Where(o=>o!=null).Select(o=>Dto(set.transform,o)).ToList()};led.entries.Add(entry);ledgerChanged=true;}
     var notes=new List<string>();bool actorChanged=false;
     if(ShardOwners.Contains(id))
     {
      notes.Add(ShardOrganId+": "+EnsurePart(actor.transform,visual,id,doc,write,set.transform,entry,ref actorChanged,out bool placed));
      if(!placed){changed|=actorChanged;ledgerChanged|=actorChanged;sb.AppendLine("  "+id+": shard not placed — organs untouched: "+string.Join(" | ",notes));continue;}
     }
     var organs=TelegraphOrgans306.Build(actor.transform,visual,TelegraphOrgans306.ActorSpecs(id),cache,out string how);notes.Add(how);
     if(ShardOwners.Contains(id))notes.Add(ApplyContamination(organs,visual,RowOf(doc,id)));   // D308-4d (growth lesson: row None — the tree has 3 submeshes)
     actorChanged|=WriteOrgans(set,organs,entry,write,notes);
     changed|=actorChanged;ledgerChanged|=actorChanged;   // entry paths / organsWritten may have changed in place
     sb.AppendLine("  "+id+(write?(actorChanged?" [changed]":" [no change]"):"")+": "+string.Join(" | ",notes));
    }
    // MineTutorialBoss306 (Main only until MineBoss306 apply: runs on the candidates — then the same organs come out of MineBoss306.BuildOrgans)
    var boss=actors.FirstOrDefault(a=>a.Id==MineTutorialProfileSO.BossId);
    if(boss==null)sb.AppendLine("  "+MineTutorialProfileSO.BossId+": not in scene — run MineBoss306 apply:"+path+" first (skipped; AC-T9 excludes the boss until then)");
    else
    {
     var body=boss.transform.Find("MineBoss306_Visual");var set=body!=null?body.GetComponent<EnemyOrganSet>():null;var animator=body!=null?body.GetComponent<Animator>():null;
     var smr=body!=null?body.GetComponentInChildren<SkinnedMeshRenderer>(true):null;
     if(set==null||animator==null||smr==null)sb.AppendLine("  "+MineTutorialProfileSO.BossId+": body / organ set / animator missing — skipped");
     else
     {
      string hp=HierarchyPath(set.transform);var entry=led.entries.FirstOrDefault(e=>e.target==path&&e.path==hp);
      if(write&&entry==null){entry=new Entry{target=path,path=hp,actor=MineTutorialProfileSO.BossId,stamp=stamp,before=(set.Organs??new EnemyOrganSet.Organ[0]).Where(o=>o!=null).Select(o=>Dto(set.transform,o)).ToList()};led.entries.Add(entry);ledgerChanged=true;}
      var bsb=new StringBuilder();var organs=MineBoss306.BuildOrgans(boss.transform,animator,smr,cache,bsb);var notes=new List<string>{bsb.ToString().Trim().Replace("\n"," / ")};
      bool bossChanged=WriteOrgans(set,organs,entry,write,notes);changed|=bossChanged;
      sb.AppendLine("  "+MineTutorialProfileSO.BossId+(write?(bossChanged?" [changed]":" [no change]"):"")+": "+string.Join(" | ",notes));
     }
    }
    // species actors: the prefab parts must also be readability targets on the scene instances (an instance may override Targets)
    foreach(var set in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<EnemyOrganSet>(true)))
    {
     if(set.Organs==null||!set.Organs.Any(o=>o!=null&&o.Mode==EnemyOrganSurfaceMode.Part&&o.TargetRenderer!=null&&o.TargetRenderer.name.StartsWith("Organ308_",StringComparison.Ordinal)))continue;
     var actor=set.GetComponentInParent<PrologueEncounter>(true);if(actor!=null&&Array.IndexOf(SceneActors,actor.Id)>=0)continue;
     string hp=HierarchyPath(set.transform);var entry=led.entries.FirstOrDefault(e=>e.target==path&&e.path==hp);
     var notes=new List<string>();
     if(!write){var rd=Readability(set);int missing=rd==null?-1:set.Organs.Count(o=>o!=null&&o.Mode==EnemyOrganSurfaceMode.Part&&o.TargetRenderer!=null&&!rd.Targets.Contains(o.TargetRenderer));
      sb.AppendLine("  "+(actor!=null?actor.Id:hp)+": species parts, readability targets missing "+missing);continue;}
     if(entry==null){entry=new Entry{target=path,path=hp,actor=actor!=null?actor.Id:hp,stamp=stamp,before=new List<OrganDto>()};}
     if(EnsureReadability(set,set.Organs,set.transform,entry,notes))
     {
      if(!led.entries.Contains(entry))led.entries.Add(entry);
      changed=ledgerChanged=true;sb.AppendLine("  "+entry.actor+": "+string.Join(" | ",notes));
     }
    }
   }
   if(write&&changed){Backup(path,stamp,led);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);sb.AppendLine("  saved "+path);}
   else if(write)sb.AppendLine("  no change (idempotent)");
   if(write)foreach(var row in led.entries.Where(x=>x.target==path))
   {
    // the signature every scene is compared by (AC-T9): written organs, or the unchanged organs of an actor this scene left as they were
    var o=FindPath(scene,row.path);var s=o!=null?o.GetComponent<EnemyOrganSet>():null;string sig=s!=null?Signature(s.transform,s.Organs):null;
    if(sig!=null&&row.after!=sig){row.after=sig;ledgerChanged=true;}
   }
   if(ledgerChanged)Save(led);
   return sb.ToString().TrimEnd();
  }

  static bool WriteOrgans(EnemyOrganSet set,EnemyOrganSet.Organ[] organs,Entry entry,bool write,List<string> notes)
  {
   string before=Signature(set.transform,set.Organs),after=Signature(set.transform,organs);
   if(!write){notes.Add(before!=after?"organs would change":"organs same");return false;}
   if(before==after)return false;
   set.Organs=organs;EditorUtility.SetDirty(set);entry.organsWritten=true;
   var tel=set.GetComponent<EnemyElementTelegraph>();if(tel!=null)EditorUtility.SetDirty(tel);
   return true;
  }

  // ---------------------------------------------------------------- parts (D308-4b: one common shard)
  internal const string ShardOrganId=TelegraphOrgans306.ShardOrgan;
  internal static string ShardPartName=>TelegraphOrgans306.PartName(ShardOrganId);

  // the shard FBX of a shape from the organ-art track (no fuzzy search: a per-species or renamed model is never picked up).
  // D308-4e: mesh name = SM_Organ308_Shard (Neutral) or SM_Organ308_Shard_<Element>; the file is <mesh name>.fbx in ShardDir
  internal static string MeshNameFor(string element)=>string.IsNullOrEmpty(element)||element==ShardElements[0]?ShardMeshName:ShardMeshName+"_"+element;
  internal static bool IsShardMeshName(string name)=>!string.IsNullOrEmpty(name)&&ShardElements.Any(e=>MeshNameFor(e)==name);
  static string ShardFbxOf(string meshName)=>ShardDir+"/"+(string.IsNullOrEmpty(meshName)?ShardMeshName:meshName)+".fbx";
  static string RowMesh(ShardPlacement row)=>row==null||string.IsNullOrEmpty(row.mesh)?ShardMeshName:row.mesh;
  static string RowElement(ShardPlacement row)=>row==null||string.IsNullOrEmpty(row.element)?ShardElements[0]:row.element;
  static string ResolveShard()=>AssetDatabase.LoadMainAssetAtPath(ShardFbx)!=null?ShardFbx:null;
  static Mesh ShardMesh(string meshName){string fbx=ShardFbxOf(meshName);return AssetDatabase.LoadMainAssetAtPath(fbx)!=null?AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>().OrderByDescending(m=>m.vertexCount).FirstOrDefault():null;}
  static ShardPlacement RowOf(ShardDoc doc,string owner)=>doc!=null&&doc.placements!=null?doc.placements.FirstOrDefault(p=>p!=null&&p.owner==owner):null;
  static EnemyContaminationMode ContamModeOf(ShardPlacement p)=>p!=null&&!string.IsNullOrEmpty(p.contamMode)&&Enum.TryParse(p.contamMode,out EnemyContaminationMode m)?m:EnemyContaminationMode.None;

  // D308-4e row consistency: the shape is one of the six and is the one its element names (null = consistent)
  static string RowShapeProblem(ShardPlacement row)
  {
   string mesh=RowMesh(row),el=RowElement(row);
   if(!IsShardMeshName(mesh))return "mesh "+mesh+" is not an Organ308 shard shape ("+string.Join(", ",ShardElements.Select(MeshNameFor))+")";
   if(!ShardElements.Contains(el))return "element "+el+" unknown";
   return MeshNameFor(el)!=mesh?"element "+el+" wants "+MeshNameFor(el)+" but the row names "+mesh:null;
  }

  // D308-4e: the crystal shape follows the actor's element = its attack profile (Elemental 0 → Neutral, COMBAT-DEFENSE 무속성은 빛나지 않는다)
  internal static string ProfileElement(EnemyAttackProfileSO p)=>p==null?null:p.Elemental?p.Element.ToString():ShardElements[0];
  // the row's elementSource ("Oheangbu/Assets/..." repo path or "Assets/...") loaded as the live profile; null when absent
  static EnemyAttackProfileSO RowProfile(ShardPlacement row)
  {
   if(row==null||string.IsNullOrEmpty(row.elementSource))return null;
   string p=row.elementSource.Replace('\\','/');int at=p.IndexOf("Assets/",StringComparison.Ordinal);
   return at>=0?AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(p.Substring(at)):null;
  }
  // a scene actor's authored profile (serialized field — the AttackProfile property is runtime-gated)
  static EnemyAttackProfileSO ActorProfile(EnemyController enemy)
  {
   if(enemy==null)return null;var p=new SerializedObject(enemy).FindProperty("_attackProfile");
   return p!=null?p.objectReferenceValue as EnemyAttackProfileSO:null;
  }
  // row element vs the live profile it cites: null = agree (or no source to compare), else the problem
  static string RowLiveElementProblem(ShardPlacement row)
  {
   if(row==null||string.IsNullOrEmpty(row.elementSource))return null;
   var prof=RowProfile(row);if(prof==null)return "element source "+row.elementSource+" not found";
   string live=ProfileElement(prof);
   return live!=RowElement(row)?"ELEMENT CHANGED: "+prof.name+" is now "+live+" but the row says "+RowElement(row)+" (rerun python Tools/Blender/Organ308/shard308e_run.py — elements → prep → deploy — then redeploy Shard308)":null;
  }

  internal static ShardDoc LoadShardDoc(out string note)
  {
   string file=Abs(ShardJson);
   if(!File.Exists(file)){note="placements MISSING "+ShardJson+" (deploy the organ-art Shard308 folder)";return null;}
   try
   {
    var doc=JsonUtility.FromJson<ShardDoc>(File.ReadAllText(file));
    if(doc==null||doc.placements==null){note="placements unreadable "+ShardJson;return null;}
    var missing=ShardOwners.Where(o=>!doc.placements.Any(p=>p!=null&&p.owner==o)).ToArray();
    note="placements "+(ShardOwners.Length-missing.Length)+"/"+ShardOwners.Length+(missing.Length>0?" (missing "+string.Join(",",missing)+")":"")+" ("+doc.schema+")";
    return doc;
   }
   catch(Exception e){note="placements unreadable "+ShardJson+": "+e.Message;return null;}
  }

  // the one shared material (delivered by the organ-art track); only when it is missing is it made here from the JSON colour — matte, no
  // emission (ART-INK: the shard never glows by itself — only the telegraph overlay tints it inside the window, LDR)
  static Material EnsureShardMaterial(ShardDoc doc,bool write,out string note)
  {
   note="";var mat=AssetDatabase.LoadAssetAtPath<Material>(ShardMat);
   if(mat!=null||!write){if(mat==null)note="material would be created";return mat;}
   var lit=Shader.Find("Universal Render Pipeline/Lit");if(lit==null){note="URP Lit missing";return null;}
   Folder(ShardDir);
   var c=doc!=null?doc.baseColor:new Color(.208f,.255f,.243f,1f);c.a=1f;
   mat=new Material(lit){name=Path.GetFileNameWithoutExtension(ShardMat)};
   mat.SetColor("_BaseColor",c);mat.SetFloat("_Smoothness",Mathf.Min(.2f,doc!=null?doc.smoothness:.15f));mat.SetFloat("_Metallic",0f);
   mat.DisableKeyword("_EMISSION");mat.SetColor("_EmissionColor",Color.black);mat.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;
   AssetDatabase.CreateAsset(mat,ShardMat);AssetDatabase.SaveAssetIfDirty(mat);note="material created "+ShardMat;
   return mat;
  }

  // adds / updates "Organ308_maseok_shard" under the bone the placement row names; returns a note. root = where ledger paths start.
  // placed = the part is there after this call (or would be added by apply when dry)
  static string EnsurePart(Transform owner,Transform visual,string ownerId,ShardDoc doc,bool write,Transform root,Entry entry,ref bool changed,out bool placed)
  {
   placed=false;string name=ShardPartName;
   var existing=owner.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);
   // stray Organ308_* children (e.g. a retired v1 per-species part restored from a backup) are reported, never touched here
   var stray=owner.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Organ308_",StringComparison.Ordinal)&&t.name!=name).Select(t=>t.name).Distinct().ToArray();
   string strayNote=stray.Length>0?" | STRAY "+string.Join(",",stray)+" (not the D308-4b shard; revert:prefabs / revert:<scene> removes ledger-recorded parts)":"";
   var row=RowOf(doc,ownerId);
   if(row==null){placed=existing!=null;return "NO PLACEMENT ROW for "+ownerId+" in "+ShardJson+(existing!=null?" (existing part left as is)":" — part not added")+strayNote;}
   // D308-4e: the row's shape — refused (never swapped for another shape) when it is not one of the six or not the one its element names
   string meshName=RowMesh(row),shapeProblem=RowShapeProblem(row);
   if(shapeProblem!=null){placed=existing!=null;return "ROW SHAPE REFUSED for "+ownerId+": "+shapeProblem+(existing!=null?" (existing part left as is)":" — part not added")+strayNote;}
   string fbx=ShardFbxOf(meshName);var mesh=ShardMesh(meshName);
   if(mesh==null){placed=existing!=null;return (existing!=null?"part present, shard FBX "+fbx+" not found (left as is)":"SHARD MISSING ("+fbx+" not delivered): organ stays unbound")+strayNote;}
   if(IsProtected(fbx))return "protected — skipped";
   string liveNote=RowLiveElementProblem(row);liveNote=liveNote!=null?" | "+liveNote:"";
   var bones=visual.GetComponentsInChildren<Transform>(true).Where(t=>t.name==row.bone).ToArray();
   if(bones.Length==0){placed=existing!=null;return "no bone "+row.bone+" under "+visual.name+(existing!=null?" (existing part left as is)":" — part not added")+strayNote;}
   var bone=bones.OrderByDescending(t=>t.gameObject.activeInHierarchy).First();
   int tris=0;for(int i=0;i<mesh.subMeshCount;i++)tris+=(int)mesh.GetIndexCount(i)/3;
   var mat=EnsureShardMaterial(doc,write,out string matNote);
   // pose from the placement row: the mesh bounds centre lands on bone.TransformPoint(localPosition). Computed and compared in BONE-LOCAL
   // space (no world positions): a scene actor far from the origin (growth lesson, world ~10^3 m) keeps float noise ~1e-4 m in world
   // coordinates, which would make every rerun "move" the part and resave the scene (not idempotent)
   float meshSize=Mathf.Max(mesh.bounds.size.x,Mathf.Max(mesh.bounds.size.y,mesh.bounds.size.z));
   Quaternion localRot=Quaternion.Euler(row.localEulerAngles);
   float worldScale=row.worldSize>0f&&meshSize>1e-5f?row.worldSize/meshSize:1f;
   Vector3 localPos=row.localPosition-bone.InverseTransformVector(bone.rotation*(localRot*(mesh.bounds.center*worldScale)));
   var spec=TelegraphOrgans306.ShardSpec(ownerId);
   string roleNote=spec!=null&&!string.IsNullOrEmpty(row.role)&&row.role!=spec.Role.ToString()?", ROLE MISMATCH json "+row.role+" vs table "+spec.Role:"";
   string desc=Path.GetFileName(fbx)+" ("+RowElement(row)+") -> "+bone.name+(bones.Length>1?" (first of "+bones.Length+" named so)":"")+", size "+F(meshSize*worldScale)+" m, tris "+tris+(tris>MaxPartTriangles?" OVER "+MaxPartTriangles:"")+roleNote+(matNote.Length>0?", "+matNote:"")+liveNote+strayNote;
   placed=true;
   if(!write)return (existing!=null?"update ":"add ")+desc;
   var part=existing;bool created=false;
   if(part==null){part=new GameObject(name).transform;created=true;}
   bool moved=part.parent!=bone;
   if(moved)
   {
    // a placement row that changed bone moves the part: keep the ledger paths (parts / readability targets) pointing at it, so
    // revert:prefabs / revert:<scene> still finds and removes exactly what apply added
    string oldRel=created?null:Rel(root,part);
    part.SetParent(bone,false);
    if(oldRel!=null&&entry!=null)
    {
     string newRel=Rel(root,part);
     for(int i=0;i<entry.addedParts.Count;i++)if(entry.addedParts[i]==oldRel)entry.addedParts[i]=newRel;
     for(int i=0;i<entry.addedReadability.Count;i++)if(entry.addedReadability[i]==oldRel)entry.addedReadability[i]=newRel;
    }
   }
   var ls=bone.lossyScale;Vector3 local=new Vector3(worldScale/Mathf.Max(1e-5f,Mathf.Abs(ls.x)),worldScale/Mathf.Max(1e-5f,Mathf.Abs(ls.y)),worldScale/Mathf.Max(1e-5f,Mathf.Abs(ls.z)));
   // tolerances in bone-local units: 1e-5 relative to the bone scale (~0.01 mm world for a 1-scale bone), rotation via the dot product
   float tol=1e-5f*Mathf.Max(1e-3f,1f/Mathf.Max(1e-5f,Mathf.Abs(ls.x)));
   if(created||moved||(part.localPosition-localPos).magnitude>tol||Mathf.Abs(Quaternion.Dot(part.localRotation,localRot))<.999999f||(part.localScale-local).magnitude>1e-5f*Mathf.Max(1f,local.magnitude))
   {part.localPosition=localPos;part.localRotation=localRot;part.localScale=local;changed=true;}
   if(part.gameObject.layer!=bone.gameObject.layer){part.gameObject.layer=bone.gameObject.layer;changed=true;}
   var mf=part.GetComponent<MeshFilter>();if(mf==null){mf=part.gameObject.AddComponent<MeshFilter>();changed=true;}
   if(mf.sharedMesh!=mesh){mf.sharedMesh=mesh;changed=true;}
   var mr=part.GetComponent<MeshRenderer>();if(mr==null){mr=part.gameObject.AddComponent<MeshRenderer>();changed=true;}
   if(mat!=null&&(mr.sharedMaterials.Length!=1||mr.sharedMaterial!=mat)){mr.sharedMaterials=new[]{mat};changed=true;}
   if(mr.shadowCastingMode!=ShadowCastingMode.On){mr.shadowCastingMode=ShadowCastingMode.On;changed=true;}
   if(!mr.receiveShadows){mr.receiveShadows=true;changed=true;}
   foreach(var col in part.GetComponentsInChildren<Collider>(true)){Object.DestroyImmediate(col,true);changed=true;}
   if(created&&entry!=null){entry.addedParts.Add(Rel(root,part));}
   return (created?"added ":"kept ")+desc;
  }

  // D308-4d: writes the row's contamination onto the shard organ (Part). ContamRenderer = the body skin the placement bone drives (single
  // submesh; UV0 for Texture), ContamLocalPoint = the crystal pivot (row, bone space) moved into the organ anchor (= the part) space.
  // A Texture row whose mask is not imported is left None and reported MISSING (a deploy error never turns silently into another look);
  // a body without UV0 falls back to Sphere (world distance — Temporary Exception, reported). Any other row = None. Returns a note.
  static string ApplyContamination(EnemyOrganSet.Organ[] organs,Transform visual,ShardPlacement row)
  {
   var organ=organs!=null?organs.FirstOrDefault(o=>o!=null&&o.Id==ShardOrganId):null;
   if(organ==null)return "contamination: no shard organ";
   organ.ContamRenderer=null;organ.ContamMode=EnemyContaminationMode.None;organ.ContamMask=null;organ.ContamRadius=0f;organ.ContamLocalPoint=Vector3.zero;
   var mode=ContamModeOf(row);
   if(mode==EnemyContaminationMode.None)return "contamination none"+(row!=null&&!string.IsNullOrEmpty(row.contamNote)?" ("+row.contamNote+")":"");
   if(organ.TargetRenderer==null||organ.Mode!=EnemyOrganSurfaceMode.Part)return "contamination NOT WRITTEN: shard organ unbound";
   var part=organ.TargetRenderer.transform;var bone=part.parent;
   if(bone==null||bone.name!=row.bone)return "contamination NOT WRITTEN: shard not under the row bone "+row.bone;
   var body=BodyRendererFor(visual,bone);
   if(body==null)return "contamination NOT WRITTEN: no body renderer driven by "+bone.name;
   int sub=OrganSurfaceOverlay.SubMeshCount(body);
   if(sub!=1)return "contamination REFUSED: body "+body.name+" has "+sub+" submeshes (the overlay redraws only the last)";
   if(!(row.contamRadius>0f))return "contamination NOT WRITTEN: radius "+F(row.contamRadius);
   string how;
   if(mode==EnemyContaminationMode.Texture)
   {
    Mesh mesh=null;if(body is SkinnedMeshRenderer smr)mesh=smr.sharedMesh;else{var mf=body.GetComponent<MeshFilter>();if(mf!=null)mesh=mf.sharedMesh;}
    var mask=string.IsNullOrEmpty(row.contamMask)?null:AssetDatabase.LoadAssetAtPath<Texture2D>(row.contamMask);
    if(mask==null)return "contamination MISSING mask "+row.contamMask+" (deploy Contam308, run masks) — not written";
    if(mesh==null||!mesh.HasVertexAttribute(VertexAttribute.TexCoord0)){mode=EnemyContaminationMode.Sphere;how="SPHERE (body "+body.name+" has no UV0 — world distance, Temporary Exception)";}
    else{organ.ContamMask=mask;how="Texture "+mask.name;}
   }
   else how="SPHERE (row: "+(row.contamNote??"")+" — world distance, Temporary Exception)";
   organ.ContamRenderer=body;organ.ContamMode=mode;organ.ContamRadius=row.contamRadius;
   organ.ContamLocalPoint=part.InverseTransformPoint(bone.TransformPoint(row.contamLocalPoint));
   return "contamination "+how+" on "+body.name+", radius "+F(row.contamRadius)+" m";
  }

  static FolkloreReadability298 Readability(EnemyOrganSet set)=>set.GetComponentInParent<FolkloreReadability298>(true)??set.GetComponentInChildren<FolkloreReadability298>(true);

  // part renderers join the body's readability SH fill (same diffuse probe as the body)
  static bool EnsureReadability(EnemyOrganSet set,EnemyOrganSet.Organ[] organs,Transform root,Entry entry,List<string> notes)
  {
   var rd=Readability(set);if(rd==null){notes.Add("no FolkloreReadability298 (parts keep scene lighting)");return false;}
   bool changed=false;var targets=(rd.Targets??new Renderer[0]).ToList();var usage=(rd.RestoreUsage??new LightProbeUsage[0]).ToList();
   bool usageAligned=usage.Count==targets.Count;
   foreach(var o in organs)
   {
    if(o==null||o.Mode!=EnemyOrganSurfaceMode.Part||o.TargetRenderer==null||!o.TargetRenderer.name.StartsWith("Organ308_",StringComparison.Ordinal)||targets.Contains(o.TargetRenderer))continue;
    targets.Add(o.TargetRenderer);if(usageAligned)usage.Add(o.TargetRenderer.lightProbeUsage);
    entry?.addedReadability.Add(Rel(root,o.TargetRenderer.transform));changed=true;
   }
   if(changed){rd.Targets=targets.ToArray();if(usageAligned)rd.RestoreUsage=usage.ToArray();EditorUtility.SetDirty(rd);notes.Add("readability targets "+targets.Count);}
   return changed;
  }

  static string RevertEntry(Transform root,EnemyOrganSet set,Entry e)=>RevertEntry(root,set,e,out _);
  // did = something was actually undone (the caller saves the prefab / scene only then)
  static string RevertEntry(Transform root,EnemyOrganSet set,Entry e,out bool did)
  {
   var parts=new List<string>();did=false;
   var rd=set!=null?Readability(set):null;
   if(rd!=null&&e.addedReadability.Count>0)
   {
    var targets=(rd.Targets??new Renderer[0]).ToList();var usage=(rd.RestoreUsage??new LightProbeUsage[0]).ToList();bool aligned=usage.Count==targets.Count;int removed=0;
    foreach(var rel in e.addedReadability){var t=FindRel(root,rel);var r=t!=null?t.GetComponent<Renderer>():null;int at=r!=null?targets.IndexOf(r):-1;if(at<0)continue;targets.RemoveAt(at);if(aligned)usage.RemoveAt(at);removed++;}
    if(removed>0){rd.Targets=targets.ToArray();if(aligned)rd.RestoreUsage=usage.ToArray();EditorUtility.SetDirty(rd);did=true;}
    parts.Add("readability -"+removed);
   }
   if(set!=null&&e.organsWritten){set.Organs=e.before.Select(d=>FromDto(set.transform,d)).ToArray();EditorUtility.SetDirty(set);var tel=set.GetComponent<EnemyElementTelegraph>();if(tel!=null)EditorUtility.SetDirty(tel);parts.Add("organs restored ("+e.before.Count+")");did=true;}
   int gone=0;foreach(var rel in e.addedParts){var t=FindRel(root,rel);if(t!=null){Object.DestroyImmediate(t.gameObject,true);gone++;did=true;}}
   if(e.addedParts.Count>0)parts.Add("parts removed "+gone+"/"+e.addedParts.Count);
   return parts.Count>0?string.Join(", ",parts):"nothing to undo";
  }

  // ---------------------------------------------------------------- status / check
  static string Status()
  {
   var led=Load();var sb=new StringBuilder("ledger "+LedgerFile+": "+led.entries.Count+" entries, "+led.backups.Count+" backups, "+led.hashes.Count+" hashes\n");
   sb.AppendLine("  shader "+(Shader.Find(EnemyTelegraphTimingSO.SurfaceShaderName)!=null)+", material "+(AssetDatabase.LoadAssetAtPath<Material>(SurfaceMat)!=null)+
    ", masks crystal "+(AssetDatabase.LoadAssetAtPath<Texture2D>(CrystalMask)!=null)+" ridge "+(AssetDatabase.LoadAssetAtPath<Texture2D>(RidgeMask)!=null)+" ember "+(AssetDatabase.LoadAssetAtPath<Texture2D>(EmberMask)!=null)+
    ", shard "+(ResolveShard()!=null)+" material "+(AssetDatabase.LoadAssetAtPath<Material>(ShardMat)!=null));
   var sdoc=LoadShardDoc(out string docNote);sb.AppendLine("  shard "+ShardDir+": "+docNote);
   // D308-4e shapes delivered / D308-4d contamination masks delivered (rows: owner = element shape, contamination mode)
   sb.AppendLine("  shapes "+ShardElements.Count(e=>ShardMesh(MeshNameFor(e))!=null)+"/"+ShardElements.Length+" ("+string.Join(", ",ShardElements.Select(e=>e+(ShardMesh(MeshNameFor(e))!=null?"":" MISSING")))+")");
   if(sdoc!=null)sb.AppendLine("  rows "+string.Join(", ",sdoc.placements.Where(p=>p!=null).Select(p=>p.owner+"="+RowElement(p)+"/"+ContamModeOf(p)+(ContamModeOf(p)==EnemyContaminationMode.Texture?(AssetDatabase.LoadAssetAtPath<Texture2D>(p.contamMask)!=null?"":" MASK MISSING"):""))));
   foreach(var g in led.entries.GroupBy(e=>e.target))sb.AppendLine("  "+g.Key+": "+g.Count()+" ("+string.Join(", ",g.Select(e=>e.actor+(e.organsWritten?" organs":"")+(e.addedParts.Count>0?" +"+e.addedParts.Count+" part":"")+(e.addedReadability.Count>0?" +"+e.addedReadability.Count+" rd":"")))+")");
   // AC-T9: the same actor carries the same organs in the three scenes (signature recorded at each apply)
   int diffs=0;
   foreach(var actor in led.entries.Where(e=>Scenes.Contains(e.target)&&e.after!=null).Select(e=>e.actor).Distinct())
   {
    var rows=Scenes.Select(s=>led.entries.FirstOrDefault(e=>e.target==s&&e.actor==actor)).ToArray();
    var present=rows.Where(r=>r!=null).ToArray();bool same=present.Select(r=>r.after).Distinct().Count()<=1;
    if(!same)diffs++;
    sb.AppendLine("  compare "+actor+": "+string.Join(" / ",Scenes.Select((s,i)=>Path.GetFileNameWithoutExtension(s)+"="+(rows[i]==null?"-":rows[i].after==null?"?":Hash8(rows[i].after))))+(same?(present.Length<Scenes.Length?" (same where applied; missing scenes need apply:)":" SAME"):" DIFFER"));
   }
   sb.Append("scene signature differences: "+diffs+" (after a re-promotion rerun apply: on the scene that lost its entries — idempotent)");
   return sb.ToString();
  }

  static string Check()
  {
   var sb=new StringBuilder("check:\n");int fails=0;var sphere=new List<string>();var led=Load();
   void Row(bool ok,string what){if(!ok)fails++;sb.AppendLine((ok?"  PASS ":"  FAIL ")+what);}
   var doc=LoadShardDoc(out string docNote);
   // AC-T8 imports (+ AC-C: every contamination mask a Texture row names is R8 linear)
   foreach(var path in MaskPaths())
   {
    var ti=AssetImporter.GetAtPath(path) as TextureImporter;var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    bool contam=path.StartsWith(ContamDir,StringComparison.Ordinal);
    if(ti==null){if(contam)Row(false,path+" (contamination mask named by a placement row) missing — deploy Contam308");else sb.AppendLine("  INFO "+path+" missing");continue;}
    bool ok=tex!=null&&tex.format==TextureFormat.R8&&!ti.sRGBTexture;
    if(path==EmberMask&&!ok)sb.AppendLine("  INFO "+path+" format "+(tex!=null?tex.format.ToString():"-")+" sRGB "+ti.sRGBTexture+" (shared import left as is; the overlay thresholds .r — run masks:ember to force R8 linear)");
    else Row(ok,path+" R8 linear: format "+(tex!=null?tex.format.ToString():"-")+" sRGB "+ti.sRGBTexture+(contam?" (D308-4d)":""));
   }
   // species FBX / material bytes (AC-T6)
   foreach(var h in led.hashes){string now=File.Exists(Abs(h.path))?Sha(Abs(h.path)):"missing";Row(now==h.sha256,"unchanged bytes "+h.path);}
   // D308-4e AC-T6: the six crystal shapes (neutral v3 + five elements) — each delivered, imported on the predicted bounds (axis map), one
   // submesh, ≤ MaxPartTriangles; one shared matte material; a placement row for every owner
   var shardMat=AssetDatabase.LoadAssetAtPath<Material>(ShardMat);
   var shapes=doc!=null&&doc.shapes!=null&&doc.shapes.Count>0?doc.shapes:new List<ShardShape>{new ShardShape{element=ShardElements[0],mesh=ShardMeshName,meshBoundsCenter=doc!=null?doc.meshBoundsCenter:Vector3.zero,meshBoundsSize=doc!=null?doc.meshBoundsSize:Vector3.zero}};
   foreach(var e in ShardElements)
   {
    var shape=shapes.FirstOrDefault(s=>s!=null&&s.element==e);string name=MeshNameFor(e);var mesh=ShardMesh(name);
    bool used=doc!=null&&doc.placements.Any(p=>p!=null&&RowMesh(p)==name);
    if(mesh==null){if(used)Row(false,"shard shape "+e+" "+ShardFbxOf(name)+" missing (a placement row uses it)");else sb.AppendLine("  INFO shard shape "+e+" "+ShardFbxOf(name)+" missing (no row uses it yet)");continue;}
    // the FBX axis map is INFERRED from the v1 smoke import: the imported bounds must equal what Blender predicted (else every pose is off)
    if(shape!=null&&shape.meshBoundsSize.sqrMagnitude>0f)
     Row((mesh.bounds.size-shape.meshBoundsSize).magnitude<=.001f&&(mesh.bounds.center-shape.meshBoundsCenter).magnitude<=.001f,
      "shard shape "+e+" import bounds "+V(mesh.bounds.center)+" / "+V(mesh.bounds.size)+" = predicted "+V(shape.meshBoundsCenter)+" / "+V(shape.meshBoundsSize)+" (<= 1 mm; axis map)");
    else sb.AppendLine("  INFO shard shape "+e+": no predicted bounds in "+ShardJson);
    int st=0;for(int i=0;i<mesh.subMeshCount;i++)st+=(int)mesh.GetIndexCount(i)/3;
    Row(mesh.subMeshCount==1&&st<=MaxPartTriangles,"shard shape "+e+" ("+mesh.name+") submeshes "+mesh.subMeshCount+" (1), triangles "+st+" <= "+MaxPartTriangles+(used?" [used]":""));
   }
   Row(shardMat!=null,"shard material "+ShardMat+(shardMat!=null?"":" missing")+" (one matte material for every shape)");
   Row(doc!=null&&ShardOwners.All(o=>doc.placements.Any(p=>p!=null&&p.owner==o)),"shard placements: "+docNote);
   if(doc!=null)foreach(var p in doc.placements.Where(x=>x!=null))
   {
    var spec=TelegraphOrgans306.ShardSpec(p.owner);
    Row(spec!=null&&p.organ==ShardOrganId&&p.role==spec.Role.ToString()&&p.worldSize>0f&&!string.IsNullOrEmpty(p.bone),
     "shard placement "+p.owner+": organ "+p.organ+", bone "+p.bone+", size "+F(p.worldSize)+" m, role json "+p.role+" / table "+(spec!=null?spec.Role.ToString():"-"));
    // D308-4e: the row's shape is the one its element names, and the element is still the live attack profile's
    string shapeProblem=RowShapeProblem(p),liveProblem=RowLiveElementProblem(p);var prof=RowProfile(p);
    Row(shapeProblem==null&&liveProblem==null,"shard placement "+p.owner+": shape "+RowMesh(p)+" = element "+RowElement(p)+(prof!=null?" = live "+prof.name+" ("+ProfileElement(prof)+")":" (no element source)")+
     (shapeProblem!=null?" | "+shapeProblem:"")+(liveProblem!=null?" | "+liveProblem:""));
    // D308-4d row: Texture names a mask in Contam308 and a radius; None needs nothing
    var cm=ContamModeOf(p);
    if(cm!=EnemyContaminationMode.None)Row(p.contamRadius>0f&&(cm!=EnemyContaminationMode.Texture||(!string.IsNullOrEmpty(p.contamMask)&&p.contamMask.StartsWith(ContamDir,StringComparison.Ordinal))),
     "contamination row "+p.owner+": "+cm+", radius "+F(p.contamRadius)+" m"+(cm==EnemyContaminationMode.Texture?", mask "+p.contamMask:""));
    else sb.AppendLine("  INFO contamination row "+p.owner+": none"+(!string.IsNullOrEmpty(p.contamNote)?" ("+p.contamNote+")":""));
   }
   using(var cache=new SurfaceCache())
   {
    foreach(var id in Species)
    {
     string path=PrefabRoot+id+".prefab";if(AssetDatabase.LoadAssetAtPath<GameObject>(path)==null)continue;
     var root=PrefabUtility.LoadPrefabContents(path);
     try{var set=root.GetComponentInChildren<EnemyOrganSet>(true);if(set!=null)CheckSet(id,root.transform,set,cache,Row,sphere,sb,RowOf(doc,id));}
     finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    var scene=SceneManager.GetActiveScene();
    if(Scenes.Contains(scene.path))
    {
     string sceneName=Path.GetFileNameWithoutExtension(scene.path);
     var actors=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PrologueEncounter>(true)).ToArray();
     foreach(var id in SceneActors.Concat(new[]{MineTutorialProfileSO.BossId}))
     {
      var actor=actors.FirstOrDefault(a=>a.Id==id);if(actor==null)continue;
      var set=id==MineTutorialProfileSO.BossId?actor.GetComponentInChildren<EnemyOrganSet>(true):actor.GetComponent<EnemyOrganSet>();
      if(set!=null)CheckSet(sceneName+"/"+id,actor.transform,set,cache,Row,sphere,sb,ShardOwners.Contains(id)?RowOf(doc,id):null);
     }
     // species instances (the six + every actor reusing a species prefab, e.g. mine_beast / village_road_raider / forest_beast308):
     // the instance keeps the prefab's shard + contamination (no stale instance override) and the crystal shape = THAT actor's element
     foreach(var set in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<EnemyOrganSet>(true)))
     {
      var shard=set.Organs!=null?set.Organs.FirstOrDefault(o=>o!=null&&o.Id==ShardOrganId):null;
      if(shard==null)continue;
      var actor=set.GetComponentInParent<PrologueEncounter>(true);if(actor!=null&&Array.IndexOf(SceneActors,actor.Id)>=0)continue;
      string label=sceneName+"/"+(actor!=null?actor.Id:HierarchyPath(set.transform));
      string prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(set.gameObject)??"";
      string species=Species.FirstOrDefault(s=>prefab.EndsWith("/PF_"+s+".prefab",StringComparison.Ordinal));
      var enemy=set.GetComponentInParent<EnemyController>(true);var prof=ActorProfile(enemy);
      CheckSet(label,set.transform,set,cache,Row,sphere,sb,RowForActor(species!=null?RowOf(doc,species):null,prof));
      var mf=shard.TargetRenderer!=null?shard.TargetRenderer.GetComponent<MeshFilter>():null;string shape=mf!=null?ShapeOf(mf.sharedMesh):null;
      if(prof==null)sb.AppendLine("  INFO "+label+": no authored attack profile (legacy controller) — crystal shape "+(shape??"-")+" not judged against an element");
      else Row(shape!=null&&shape==ProfileElement(prof),label+": crystal shape "+(shape??"none")+" = actor element "+ProfileElement(prof)+" ("+prof.name+", D308-4e)");
     }
    }
    else sb.AppendLine("  INFO open scene "+scene.path+" is not one of the three: scene actors not checked (open one and rerun)");
   }
   sb.AppendLine("  Sphere-mode organs (Temporary Exception, ask the user): "+(sphere.Count>0?string.Join(", ",sphere):"none"));
   sb.Append("fails="+fails);
   return sb.ToString();
  }

  // D308-18 답 5 (wood beasts on the agwi body): a scene instance of a species prefab that carries its OWN authored profile is judged
  // against THAT actor's element ("crystal shape = actor element", D308-4e); bone, contamination and every other field of the species
  // row stay. No authored profile, or the same element = the species row itself.
  static ShardPlacement RowForActor(ShardPlacement row,EnemyAttackProfileSO prof)
  {
   if(row==null||prof==null)return row;
   string element=ProfileElement(prof);if(element==RowElement(row))return row;
   var copy=JsonUtility.FromJson<ShardPlacement>(JsonUtility.ToJson(row));copy.element=element;copy.mesh=MeshNameFor(element);return copy;
  }

  // expect = the placement row of this owner (prefab species / growth lesson / a scene instance of a species prefab), null = none
  static void CheckSet(string label,Transform owner,EnemyOrganSet set,SurfaceCache cache,Action<bool,string> row,List<string> sphere,StringBuilder sb,ShardPlacement expect)
  {
   float threshold=MaskThreshold();
   foreach(var o in set.Organs??new EnemyOrganSet.Organ[0])
   {
    if(o==null)continue;string tag=label+"/"+o.Id;
    if(o.TargetRenderer==null){sb.AppendLine("  INFO "+tag+": unbound (no surface renderer — never lights)");continue;}
    int sub=OrganSurfaceOverlay.SubMeshCount(o.TargetRenderer);row(sub==1,tag+": target "+o.TargetRenderer.name+" submeshes "+sub);
    if(o.Mode==EnemyOrganSurfaceMode.Sphere)sphere.Add(tag);
    if(o.Mode==EnemyOrganSurfaceMode.Mask)
    {
     var probe=new EnemyOrganSet.Organ{Anchor=o.Anchor,LocalOffset=o.LocalOffset,Radius=o.Radius,TargetRenderer=o.TargetRenderer,Mode=o.Mode,Mask=o.Mask};
     ComputeSurface(probe,owner,owner.position+Vector3.up,threshold,cache,false,out float cover);
     row(cover>0f&&probe.Mode==EnemyOrganSurfaceMode.Mask,tag+": mask vertices inside the sphere "+F(Mathf.Max(0f,cover))+" (> 0)");
    }
    if(o.Mode==EnemyOrganSurfaceMode.Part&&o.TargetRenderer.name.StartsWith("Organ308_",StringComparison.Ordinal))
    {
     var r=o.TargetRenderer;var mf=r.GetComponent<MeshFilter>();int tris=0;if(mf!=null&&mf.sharedMesh!=null)for(int i=0;i<mf.sharedMesh.subMeshCount;i++)tris+=(int)mf.sharedMesh.GetIndexCount(i)/3;
     var m=r.sharedMaterial;bool emissionOff=m!=null&&!m.IsKeywordEnabled("_EMISSION")&&(!m.HasProperty("_EmissionColor")||m.GetColor("_EmissionColor").maxColorComponent<=.0001f);
     // D308-4e (AC-T6 relaxed from "one common mesh"): the part is one of the six crystal shapes — the one the placement row names when
     // there is a row — and every shape wears the one shared matte material
     var shardMat=AssetDatabase.LoadAssetAtPath<Material>(ShardMat);
     string shape=mf!=null?ShapeOf(mf.sharedMesh):null,want=expect!=null?RowElement(expect):null;
     bool shapeOk=shape!=null&&(want==null||shape==want);
     bool sharedMat=m!=null&&m==shardMat&&r.sharedMaterials.Length==1;
     var rd=Readability(set);
     row(r is MeshRenderer&&r.transform.parent!=null,tag+": MeshRenderer under "+(r.transform.parent!=null?r.transform.parent.name:"-"));
     row(r.name==ShardPartName&&shapeOk&&sharedMat&&emissionOff,tag+": crystal shape "+(shape??"NOT A SHARD SHAPE")+" ("+(mf!=null&&mf.sharedMesh!=null?mf.sharedMesh.name:"-")+")"+(want!=null?" = row "+want:"")+
      " + shared matte material "+(m!=null?m.name:"-")+", _EMISSION off + black");
     row(r.GetComponentsInChildren<Collider>(true).Length==0,tag+": no collider");
     row(tris<=MaxPartTriangles,tag+": triangles "+tris+" <= "+MaxPartTriangles);
     if(rd==null)sb.AppendLine("  INFO "+tag+": no FolkloreReadability298 on this actor (the shard keeps the scene lighting, like the body)");
     else row(rd.Targets!=null&&rd.Targets.Contains(r),tag+": FolkloreReadability298 target");
    }
    if(o.Id==ShardOrganId)CheckContamination(tag,o,expect,row,sphere,sb);
   }
   string front=FrontReport(owner,set.Organs??new EnemyOrganSet.Organ[0],out int frontFails);
   row(frontFails==0,label+": "+front);
  }

  // which of the six crystal shapes this mesh asset is (element name), or null
  static string ShapeOf(Mesh mesh)
  {
   if(mesh==null)return null;
   foreach(var e in ShardElements){var s=ShardMesh(MeshNameFor(e));if(s!=null&&s==mesh)return e;}
   return null;
  }

  // AC-C (D308-4d): the shard organ carries the row's contamination — on the body skin (single submesh, SkinnedMeshRenderer), Texture =
  // an R8 linear mask on a body with UV0; a Sphere organ (row Sphere or no UV0 downgrade) is listed with the Temporary Exceptions
  static void CheckContamination(string tag,EnemyOrganSet.Organ o,ShardPlacement expect,Action<bool,string> row,List<string> sphere,StringBuilder sb)
  {
   var want=ContamModeOf(expect);
   if(expect==null){sb.AppendLine("  INFO "+tag+": contamination "+o.ContamMode+" (no placement row to compare)");return;}
   if(want==EnemyContaminationMode.None){row(o.ContamMode==EnemyContaminationMode.None,tag+": contamination none (row"+(!string.IsNullOrEmpty(expect.contamNote)?": "+expect.contamNote:"")+") — has "+o.ContamMode);return;}
   row(o.HasContamination&&(o.ContamMode==want||(want==EnemyContaminationMode.Texture&&o.ContamMode==EnemyContaminationMode.Sphere)),
    tag+": contamination "+o.ContamMode+" (row "+want+") on "+(o.ContamRenderer!=null?o.ContamRenderer.name:"-")+", radius "+F(o.ContamRadius)+" m"+(o.HasContamination?"":" — MISSING (rerun prefabs; a scene instance may override Organs)"));
   if(o.ContamRenderer!=null)
   {
    int cs=OrganSurfaceOverlay.SubMeshCount(o.ContamRenderer);
    row(cs==1&&o.ContamRenderer is SkinnedMeshRenderer,tag+": contamination on the body skin "+o.ContamRenderer.name+" ("+o.ContamRenderer.GetType().Name+", submeshes "+cs+")");
   }
   if(o.ContamMode==EnemyContaminationMode.Texture)
   {
    var path=o.ContamMask!=null?AssetDatabase.GetAssetPath(o.ContamMask):"";var ti=AssetImporter.GetAtPath(path) as TextureImporter;
    var smr=o.ContamRenderer as SkinnedMeshRenderer;bool uv=smr!=null&&smr.sharedMesh!=null&&smr.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord0);
    row(o.ContamMask!=null&&o.ContamMask.format==TextureFormat.R8&&ti!=null&&!ti.sRGBTexture&&uv&&path==expect.contamMask,
     tag+": contamination mask "+(o.ContamMask!=null?o.ContamMask.name+" "+o.ContamMask.format:"-")+" sRGB "+(ti!=null&&ti.sRGBTexture)+", body UV0 "+uv+" (row "+expect.contamMask+")");
   }
   if(o.ContamMode==EnemyContaminationMode.Sphere)sphere.Add(tag+" (contamination: world distance)");
  }

  // ---------------------------------------------------------------- Play debug view (AC-T8 / AC-C capture)
  static string DebugView(bool on,bool contamination)
  {
   if(!EditorApplication.isPlaying)return "REFUSED debug-view is Play only (never saved)";
   int n=0,c=0;
   foreach(var tel in Object.FindObjectsByType<EnemyElementTelegraph>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
   {if(contamination)tel.ContaminationDebugView=on;else tel.DebugView=on;n++;c+=tel.ContaminationRendererCount;}
   return "debug-view"+(contamination?":contam ":" ")+(on?"on":"off")+" on "+n+" telegraphs"+(contamination?" (contamination surfaces attached last frame: "+c+")":"");
  }

  // D308-4d Play-only measurement (AC-C3 / AC-C5). contam:status = live counts, read-only (contamination surfaces, how many are attached
  // right now = extra draws while visible, overlay materials, restore mismatches, camera distance per actor). contam:off | contam:on =
  // suppress / restore the persistent contamination overlays on every live telegraph for an A/B capture or a draw-call comparison.
  // Nothing is saved; leaving Play resets it.
  static string Contam(string arg)
  {
   if(!EditorApplication.isPlaying)return "REFUSED contam:"+arg+" is Play only (never saved)";
   var tels=Object.FindObjectsByType<EnemyElementTelegraph>(FindObjectsInactive.Exclude,FindObjectsSortMode.None);
   if(arg=="on"||arg=="off"){foreach(var t in tels)t.ContaminationSuppressed=arg=="off";return "contam "+arg+" on "+tels.Length+" telegraphs (Play only; takes effect at the next LateUpdate)";}
   if(arg!="status")return "REFUSED contam:"+arg+" (contam:status | contam:on | contam:off)";
   var cam=Camera.main;
   var sb=new StringBuilder("contam status: "+tels.Length+" telegraphs, camera "+(cam!=null?V(cam.transform.position):"none")+"\n");
   int surfaces=0,attached=0,organAttached=0,materials=0,mismatches=0,deferred=0,suppressed=0;
   foreach(var t in tels)
   {
    if(t.Deferred){deferred++;continue;}
    var o=t.Overlay;if(o==null)continue;
    surfaces+=o.ContaminationSurfaceCount;attached+=o.ContaminationAttachedCount;organAttached+=o.OrganAttachedCount;materials+=o.MaterialCount;mismatches+=o.RestoreMismatches;
    if(t.ContaminationSuppressed)suppressed++;
    if(o.ContaminationSurfaceCount==0)continue;
    var set=t.Organs;float distance=-1f;
    if(set!=null&&cam!=null)for(int i=0;i<set.Count;i++){var organ=set.Get(i);if(organ!=null&&organ.HasContamination){distance=(cam.transform.position-set.ContamPoint(i)).magnitude;break;}}
    sb.AppendLine("  "+HierarchyPath(t.transform)+": surfaces "+o.ContaminationSurfaceCount+", attached "+o.ContaminationAttachedCount+", camera "+(distance>=0f?F(distance)+" m":"-")+
     (t.ContaminationSuppressed?" SUPPRESSED":"")+(o.ShaderMissing?" SHADER MISSING":"")+(o.Refusals.Length>0?" | "+o.Refusals:""));
   }
   sb.Append("total: contamination surfaces "+surfaces+", attached "+attached+" (= extra draws while those bodies are on screen), telegraph overlays "+organAttached+
    ", overlay materials "+materials+", restore mismatches "+mismatches+", deferred telegraphs "+deferred+", suppressed "+suppressed);
   return sb.ToString();
  }

  // ================================================================ shared authoring (TelegraphOrgans306, MineBoss306)
  internal static float MaskThreshold(){var t=AssetDatabase.LoadAssetAtPath<EnemyTelegraphTimingSO>(TimingPath);return t!=null?t.MaskThreshold:.1f;}
  static float VisibleDot(){var t=AssetDatabase.LoadAssetAtPath<EnemyTelegraphTimingSO>(TimingPath);return t!=null?t.VisibleDot:-.1f;}

  // the body skin an organ sits on: the skinned renderer that has the anchor among its bones (single submesh preferred, largest first)
  internal static Renderer BodyRendererFor(Transform visual,Transform anchor)
  {
   var skins=visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.sharedMesh!=null).ToArray();
   IEnumerable<SkinnedMeshRenderer> pick=skins;
   if(anchor!=null){var exact=skins.Where(r=>r.bones!=null&&Array.IndexOf(r.bones,anchor)>=0).ToArray();if(exact.Length>0)pick=exact;}
   var best=pick.OrderByDescending(r=>OrganSurfaceOverlay.SubMeshCount(r)==1).ThenByDescending(r=>r.bounds.size.sqrMagnitude).FirstOrDefault();
   if(best!=null)return best;
   return (anchor!=null?anchor:visual).GetComponentsInChildren<MeshRenderer>(true).Where(r=>!r.name.StartsWith("Organ308_",StringComparison.Ordinal))
    .OrderByDescending(r=>OrganSurfaceOverlay.SubMeshCount(r)==1).ThenByDescending(r=>r.bounds.size.sqrMagnitude).FirstOrDefault();
  }

  internal sealed class Sampled{public Vector3[] P,N;public Vector2[] UV;}
  // per-command caches: baked meshes (world space, current = bind / edit pose) and readable mask copies
  internal sealed class SurfaceCache:IDisposable
  {
   readonly Dictionary<Renderer,Sampled> meshes=new Dictionary<Renderer,Sampled>();
   readonly Dictionary<string,Texture2D> masks=new Dictionary<string,Texture2D>();
   public Sampled Mesh(Renderer r,out string error)
   {
    error=null;if(r==null){error="no renderer";return null;}
    if(meshes.TryGetValue(r,out var hit))return hit;
    Sampled s=null;
    try
    {
     if(r is SkinnedMeshRenderer smr)
     {
      if(smr.sharedMesh==null){error="no mesh";return null;}
      var baked=new UnityEngine.Mesh();
      try
      {
       smr.BakeMesh(baked);var v=baked.vertices;var n=baked.normals;var uv=baked.uv;var t=smr.transform;
       // baked vertices: pick the transform whose world bounds match the renderer's (BakeMesh scale semantics differ by version)
       var a=Matrix4x4.TRS(t.position,t.rotation,Vector3.one);var b=t.localToWorldMatrix;
       float ea=BoundsError(v,a,smr.bounds),eb=BoundsError(v,b,smr.bounds);var m=ea<=eb?a:b;
       s=new Sampled{P=new Vector3[v.Length],N=new Vector3[v.Length],UV=uv!=null&&uv.Length==v.Length?uv:new Vector2[v.Length]};
       for(int i=0;i<v.Length;i++){s.P[i]=m.MultiplyPoint3x4(v[i]);s.N[i]=n!=null&&n.Length==v.Length?(t.rotation*n[i]).normalized:Vector3.up;}
      }
      finally{Object.DestroyImmediate(baked);}
     }
     else
     {
      var mf=r.GetComponent<MeshFilter>();var mesh=mf!=null?mf.sharedMesh:null;if(mesh==null){error="no mesh";return null;}
      var v=mesh.vertices;var n=mesh.normals;var uv=mesh.uv;var m=r.transform.localToWorldMatrix;var nm=m.inverse.transpose;
      s=new Sampled{P=new Vector3[v.Length],N=new Vector3[v.Length],UV=uv!=null&&uv.Length==v.Length?uv:new Vector2[v.Length]};
      for(int i=0;i<v.Length;i++){s.P[i]=m.MultiplyPoint3x4(v[i]);s.N[i]=n!=null&&n.Length==v.Length?nm.MultiplyVector(n[i]).normalized:Vector3.up;}
     }
    }
    catch(Exception e){error=e.GetType().Name+": "+e.Message;s=null;}
    meshes[r]=s;return s;
   }
   static float BoundsError(Vector3[] v,Matrix4x4 m,Bounds target)
   {
    if(v.Length==0)return float.MaxValue;var b=new Bounds(m.MultiplyPoint3x4(v[0]),Vector3.zero);
    for(int i=1;i<v.Length;i+=Mathf.Max(1,v.Length/2000))b.Encapsulate(m.MultiplyPoint3x4(v[i]));
    return (b.center-target.center).magnitude+(b.size-target.size).magnitude;
   }
   public Texture2D Mask(Texture2D asset)
   {
    if(asset==null)return null;string path=AssetDatabase.GetAssetPath(asset);
    if(masks.TryGetValue(path,out var hit))return hit;
    Texture2D tex=null;
    try{var bytes=File.ReadAllBytes(Abs(path));tex=new Texture2D(2,2,TextureFormat.RGBA32,false,true);if(!tex.LoadImage(bytes)){Object.DestroyImmediate(tex);tex=null;}}
    catch(Exception){if(tex!=null)Object.DestroyImmediate(tex);tex=null;}
    masks[path]=tex;return tex;
   }
   public void Dispose(){foreach(var t in masks.Values)if(t!=null)Object.DestroyImmediate(t);masks.Clear();meshes.Clear();}
  }

  // precomputes the organ's surface (SPEC 방어 성공 먹 획): Part = part bounds centre projected outward onto the part (spear tip: the bounds
  // centre itself) and Radius grown to cover the whole part; Mask = mask-weighted centroid of the bind-pose vertices inside the sphere moved to
  // the nearest masked vertex (coverage 0 → Sphere, reported); Sphere = outermost in-sphere vertex along the mean normal. Returns a note.
  internal static string ComputeSurface(EnemyOrganSet.Organ organ,Transform owner,Vector3 bodyCentre,float threshold,SurfaceCache cache,bool tipCentre,out float coverage)
  {
   coverage=-1f;var anchor=organ.Anchor!=null?organ.Anchor:owner;
   Vector3 c=anchor.TransformPoint(organ.LocalOffset);float radius=organ.Radius;
   Vector3 outward=c-bodyCentre;if(outward.sqrMagnitude<1e-8f)outward=owner.forward;outward.Normalize();
   void Set(Vector3 point,Vector3 normal){organ.SurfaceLocalPoint=anchor.InverseTransformPoint(point);var ln=anchor.InverseTransformDirection(normal.sqrMagnitude>1e-8f?normal.normalized:outward);organ.SurfaceLocalNormal=ln.sqrMagnitude>1e-8f?ln.normalized:Vector3.forward;}
   if(organ.TargetRenderer==null){Set(c,outward);return "unbound";}
   // SPEC: the appended overlay redraws only the last submesh, so a renderer with != 1 submesh can never carry it — dry / apply refuse and report
   int subMeshes=OrganSurfaceOverlay.SubMeshCount(organ.TargetRenderer);
   if(subMeshes!=1){string refused=organ.TargetRenderer.name;organ.TargetRenderer=null;Set(c,outward);return "REFUSED target "+refused+" has "+subMeshes+" submeshes (overlay needs 1) -> UNBOUND";}
   var s=cache.Mesh(organ.TargetRenderer,out string error);
   if(s==null||s.P.Length==0){Set(c,outward);return "surface not sampled ("+error+")";}
   var notes=new List<string>();
   if(organ.Mode==EnemyOrganSurfaceMode.Part)
   {
    var bc=organ.TargetRenderer.bounds.center;Vector3 n=bc-bodyCentre;if(n.sqrMagnitude<1e-8f)n=owner.forward;n.Normalize();
    // a part this tool placed points its +Y outward (EnsurePart); trust it unless it faces into the body (placement.json conventions)
    var pt=organ.TargetRenderer.transform;if(pt.name.StartsWith("Organ308_",StringComparison.Ordinal)&&Vector3.Dot(pt.up,n)>-.2f)n=pt.up;
    float reach=0f;foreach(var p in s.P)reach=Mathf.Max(reach,(p-c).magnitude);
    if(radius<reach*1.05f){organ.Radius=radius=reach*1.05f;notes.Add("radius "+F(radius)+" covers the part");}
    if(tipCentre){Set(bc,n);coverage=1f;return string.Join(", ",notes);}
    int best=-1;float score=float.NegativeInfinity;
    for(int i=0;i<s.P.Length;i++){var v=s.P[i]-bc;float along=Vector3.Dot(v,n);if(along<=0f)continue;float perp=(v-n*along).magnitude;float sc=along-2f*perp;if(sc>score){score=sc;best=i;}}
    if(best<0)Set(bc,n);else Set(s.P[best],(n+s.N[best]).normalized);
    coverage=1f;return string.Join(", ",notes);
   }
   Texture2D tex=organ.Mode==EnemyOrganSurfaceMode.Mask?cache.Mask(organ.Mask):null;
   if(organ.Mode==EnemyOrganSurfaceMode.Mask&&tex==null){organ.Mode=EnemyOrganSurfaceMode.Sphere;notes.Add("mask unreadable -> SPHERE (Temporary Exception)");}
   int inside=0,hits=0;Vector3 wsum=Vector3.zero,nsum=Vector3.zero;float wtot=0f;
   for(int i=0;i<s.P.Length;i++)
   {
    if((s.P[i]-c).sqrMagnitude>=radius*radius)continue;inside++;
    float w=1f;if(tex!=null){float m=tex.GetPixelBilinear(s.UV[i].x,s.UV[i].y).r;w=m>=threshold?m:0f;}
    if(w<=0f)continue;hits++;wsum+=s.P[i]*w;nsum+=s.N[i]*w;wtot+=w;
   }
   coverage=inside>0?hits/(float)inside:0f;
   if(organ.Mode==EnemyOrganSurfaceMode.Mask&&hits==0)
   {
    organ.Mode=EnemyOrganSurfaceMode.Sphere;
    string again=ComputeSurface(organ,owner,bodyCentre,threshold,cache,false,out _);
    return "mask coverage 0 inside the sphere -> DOWNGRADED to SPHERE (Temporary Exception, ask the user)"+(again.Length>0?", "+again:"");
   }
   if(inside==0){Set(c,outward);notes.Add("NO SURFACE inside the sphere");return string.Join(", ",notes);}
   Vector3 normal=nsum.sqrMagnitude>1e-8f?nsum.normalized:outward;
   if(organ.Mode==EnemyOrganSurfaceMode.Mask)
   {
    Vector3 centroid=wsum/wtot;int best=-1;float bestD=float.PositiveInfinity;
    for(int i=0;i<s.P.Length;i++)
    {
     if((s.P[i]-c).sqrMagnitude>=radius*radius)continue;
     float m=tex.GetPixelBilinear(s.UV[i].x,s.UV[i].y).r;if(m<threshold)continue;
     float d=(s.P[i]-centroid).sqrMagnitude;if(d<bestD){bestD=d;best=i;}
    }
    Set(best>=0?s.P[best]:centroid,normal);
   }
   else
   {
    int best=-1;float far=float.NegativeInfinity;
    for(int i=0;i<s.P.Length;i++){if((s.P[i]-c).sqrMagnitude>=radius*radius)continue;float a=Vector3.Dot(s.P[i]-c,normal);if(a>far){far=a;best=i;}}
    Set(best>=0?s.P[best]:c,normal);
   }
   return string.Join(", ",notes);
  }

  // AC-T5 (authoring): per attack key at least one organ faces the actor's front (outward normal · forward ≥ VisibleDot); chains flow anyway
  internal static string FrontReport(Transform owner,IList<EnemyOrganSet.Organ> organs,out int fails)
  {
   fails=0;float dot=VisibleDot();var keys=organs.Where(o=>o!=null&&o.AttackKeys!=null).SelectMany(o=>o.AttackKeys).Distinct().ToList();
   var parts=new List<string>();
   foreach(var key in keys)
   {
    bool ok=organs.Any(o=>o!=null&&o.TargetRenderer!=null&&o.AttackKeys!=null&&(o.AttackKeys.Contains(key)||o.AttackKeys.Contains(EnemyOrganSet.KeyAny))&&(o.Role==EnemyOrganRole.Spine||Facing(owner,o)>=dot));
    if(!ok)fails++;parts.Add(key+":"+(ok?"ok":"FAIL"));
   }
   return "front("+(parts.Count>0?string.Join(" ",parts):"no keys")+")";
  }
  static float Facing(Transform owner,EnemyOrganSet.Organ o)
  {
   var a=o.Anchor!=null?o.Anchor:owner;var n=o.HasSurface?a.TransformDirection(o.SurfaceLocalNormal):a.TransformPoint(o.LocalOffset)-(owner.position+Vector3.up);
   return n.sqrMagnitude>1e-8f?Vector3.Dot(n.normalized,owner.forward):-1f;
  }

  // ---------------------------------------------------------------- helpers
  static OrganDto Dto(Transform owner,EnemyOrganSet.Organ o)=>new OrganDto{id=o.Id,anchor=Rel(owner,o.Anchor),boneName=o.BoneName,renderer=o.TargetRenderer!=null?Rel(owner,o.TargetRenderer.transform):null,
   mask=o.Mask!=null?AssetDatabase.GetAssetPath(o.Mask):"",role=(int)o.Role,human=(int)o.HumanBone,mode=(int)o.Mode,radius=o.Radius,offset=o.LocalOffset,surfacePoint=o.SurfaceLocalPoint,surfaceNormal=o.SurfaceLocalNormal,keys=o.AttackKeys,
   contamRenderer=o.ContamRenderer!=null?Rel(owner,o.ContamRenderer.transform):null,contamMask=o.ContamMask!=null?AssetDatabase.GetAssetPath(o.ContamMask):"",contamMode=(int)o.ContamMode,contamRadius=o.ContamRadius,contamPoint=o.ContamLocalPoint};
  static EnemyOrganSet.Organ FromDto(Transform owner,OrganDto d)
  {
   var anchor=FindRel(owner,d.anchor);var rt=FindRel(owner,d.renderer);var ct=string.IsNullOrEmpty(d.contamRenderer)?null:FindRel(owner,d.contamRenderer);
   return new EnemyOrganSet.Organ{Id=d.id,Anchor=anchor,BoneName=d.boneName,TargetRenderer=rt!=null?rt.GetComponent<Renderer>():null,Mask=string.IsNullOrEmpty(d.mask)?null:AssetDatabase.LoadAssetAtPath<Texture2D>(d.mask),
    Role=(EnemyOrganRole)d.role,HumanBone=(HumanBodyBones)d.human,Mode=(EnemyOrganSurfaceMode)d.mode,Radius=d.radius,LocalOffset=d.offset,SurfaceLocalPoint=d.surfacePoint,SurfaceLocalNormal=d.surfaceNormal,AttackKeys=d.keys??new[]{EnemyOrganSet.KeyAny},
    ContamRenderer=ct!=null?ct.GetComponent<Renderer>():null,ContamMode=(EnemyContaminationMode)d.contamMode,ContamMask=string.IsNullOrEmpty(d.contamMask)?null:AssetDatabase.LoadAssetAtPath<Texture2D>(d.contamMask),
    ContamRadius=d.contamRadius,ContamLocalPoint=d.contamPoint};
  }
  static string Signature(Transform owner,EnemyOrganSet.Organ[] organs)
  {
   if(organs==null)return "";var sb=new StringBuilder();
   foreach(var o in organs)
   {
    if(o==null){sb.Append("null;");continue;}
    sb.Append(o.Id).Append('|').Append(o.Role).Append('|').Append(o.Mode).Append('|').Append(Rel(owner,o.Anchor)).Append('|').Append(o.TargetRenderer!=null?o.TargetRenderer.name:"-").Append('|')
     .Append(o.Mask!=null?o.Mask.name:"-").Append('|').Append(F3(o.Radius)).Append('|').Append(V(o.LocalOffset)).Append('|').Append(V(o.SurfaceLocalPoint)).Append('|').Append(V(o.SurfaceLocalNormal)).Append('|')
     .Append(o.AttackKeys!=null?string.Join(",",o.AttackKeys):"");
    // D308-4d: only organs that carry contamination grow the signature (pre-4d / boss / dragon / gate signatures stay byte-identical)
    if(o.ContamMode!=EnemyContaminationMode.None)
     sb.Append("|C:").Append(o.ContamMode).Append('|').Append(o.ContamRenderer!=null?o.ContamRenderer.name:"-").Append('|').Append(o.ContamMask!=null?o.ContamMask.name:"-")
      .Append('|').Append(F3(o.ContamRadius)).Append('|').Append(V(o.ContamLocalPoint));
    sb.Append(';');
   }
   return sb.ToString();
  }
  static string Rel(Transform root,Transform t)
  {
   if(t==null)return null;if(t==root)return "";
   var parts=new List<string>();for(var p=t;p!=null&&p!=root;p=p.parent)parts.Insert(0,p.name);
   return t.IsChildOf(root)?string.Join("/",parts):"!"+t.name;   // outside the owner: by name
  }
  static Transform FindRel(Transform root,string rel)
  {
   if(rel==null)return null;if(rel=="")return root;
   if(rel.StartsWith("!",StringComparison.Ordinal)){string n=rel.Substring(1);var top=root;while(top.parent!=null)top=top.parent;return top.GetComponentsInChildren<Transform>(true).FirstOrDefault(x=>x.name==n);}
   return root.Find(rel);
  }
  static Transform FindPath(Scene scene,string hierarchyPath)
  {
   int slash=hierarchyPath.IndexOf('/');string top=slash<0?hierarchyPath:hierarchyPath.Substring(0,slash);
   foreach(var g in scene.GetRootGameObjects())if(g.name==top)return slash<0?g.transform:g.transform.Find(hierarchyPath.Substring(slash+1));
   return null;
  }
  static string ResolveScene(string target)
  {
   if(Scenes.Contains(target))return target;
   return Scenes.FirstOrDefault(s=>string.Equals(Path.GetFileNameWithoutExtension(s),target,StringComparison.OrdinalIgnoreCase));
  }
  static bool IsProtected(string assetPath)=>!string.IsNullOrEmpty(assetPath)&&Protected.Any(p=>assetPath.IndexOf(p,StringComparison.OrdinalIgnoreCase)>=0);
  static void RecordHashes(GameObject root,Ledger led)
  {
   foreach(var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
   {
    var paths=new List<string>{AssetDatabase.GetAssetPath(smr.sharedMesh)};paths.AddRange(smr.sharedMaterials.Where(m=>m!=null).Select(AssetDatabase.GetAssetPath));
    foreach(var a in root.GetComponentsInChildren<Animator>(true))if(a.avatar!=null)paths.Add(AssetDatabase.GetAssetPath(a.avatar));
    foreach(var p in paths.Where(p=>!string.IsNullOrEmpty(p)&&p.StartsWith("Assets/",StringComparison.Ordinal)).Distinct())
     if(!led.hashes.Any(h=>h.path==p)&&File.Exists(Abs(p)))led.hashes.Add(new HashRow{path=p,sha256=Sha(Abs(p))});
   }
  }
  static void Backup(string assetPath,string stamp,Ledger led)
  {
   string src=Abs(assetPath),dst=Path.Combine(BackupRoot,stamp,assetPath.Replace('/',Path.DirectorySeparatorChar));
   if(File.Exists(dst)||!File.Exists(src))return;
   if(led.backups.Any(b=>b.EndsWith(assetPath.Replace('/',Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)&&File.Exists(b)))return;   // the first (pre-308) copy is the one that matters
   Directory.CreateDirectory(Path.GetDirectoryName(dst));File.Copy(src,dst);
   if(File.Exists(src+".meta"))File.Copy(src+".meta",dst+".meta",true);
   led.backups.Add(dst);
  }
  static Ledger Load(){try{return File.Exists(LedgerFile)?JsonUtility.FromJson<Ledger>(File.ReadAllText(LedgerFile))??new Ledger():new Ledger();}catch(Exception){return new Ledger();}}
  static void Save(Ledger l){Directory.CreateDirectory(OutRoot);File.WriteAllText(LedgerFile,JsonUtility.ToJson(l,true),new UTF8Encoding(false));}
  static string Sha(string file){using(var sha=SHA256.Create())using(var fs=File.OpenRead(file))return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-","").ToLowerInvariant();}
  static string Hash8(string s){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-","").Substring(0,8).ToLowerInvariant();}
  static string Abs(string assetPath)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",assetPath));
  static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;var parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
  static string HierarchyPath(Transform t){var s=t.name;for(var p=t.parent;p!=null;p=p.parent)s=p.name+"/"+s;return s;}
  static string Stamp()=>DateTime.Now.ToString("yyyyMMddTHHmmss",CultureInfo.InvariantCulture);
  static string F(float v)=>v.ToString("0.###",CultureInfo.InvariantCulture);
  static string F3(float v)=>v.ToString("0.000",CultureInfo.InvariantCulture);
  static string V(Vector3 v)=>F3(v.x)+","+F3(v.y)+","+F3(v.z);
 }
}
