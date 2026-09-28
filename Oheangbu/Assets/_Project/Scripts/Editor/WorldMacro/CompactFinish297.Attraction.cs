using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEngine.SceneManagement;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 attraction pass (SPEC-WORLD-FINISH-297, TEST) — colour = guidance, only the LOCKED light sources glow.
 // 산정 마석 봉수 on every realm high-mountain summit (봉수대 kit mesh from Tools/Art/beacon297.py, InkBeacon297 오행색 core,
 // a BeaconHalo297 card drawn by Renderer297's "BeaconHalo297" RenderObjects feature after the fog, a small point light),
 // warmer 주막 등불 (InnLantern297 = InkBeacon297), a marked 성황당 flame, the light-exempt candidate fog (Fog297 = RealmFog297 on
 // Renderer297's CompactMist238 only, Normal requirement added), the 수묵담채 land-readability values on M_InkWash297 and the
 // post grade. Candidate assets only. Originals are recorded once to <K297>/Attraction/original.json; attraction-revert
 // restores them, removes the beacon root and the halo feature, then deletes the record (the next apply records afresh).
 public static partial class CompactRebuildAuthoring
 {
  // post grade shared with Lighting297 (TEST): lower world saturation; Bloom catches only the HDR light sources
  const float PostSaturation297=-12f,BloomThreshold297=1.15f,BloomIntensity297=.3f,BloomScatter297=.7f;
  const string AttractionRoot297="Finish297_Attraction",HaloFeature297="BeaconHalo297";
  // M_InkWash297 TEST values; every one is inert at its shader default, so restoring the recorded values restores the look
  static readonly (string name,float value)[] InkAttraction297={("_LandCeiling",.86f),("_LandDetail",.35f),("_SlopeInk",.25f),("_SlopeWashKeep",.5f),
   ("_PaperWashMax",.38f),("_HighlightPaper",.10f),("_Tint",.30f),("_LightExempt",1f),("_EmissiveMark",1f)};
  // 오행색 by realm: 목 청림 / 화 적로 / 금 철옹 / 수 현강 / 토 황경
  static readonly Dictionary<string,Color> BeaconColour297=new Dictionary<string,Color>(StringComparer.OrdinalIgnoreCase){
   {"cheongrim",new Color(.31f,.62f,.40f)},{"jeokro",new Color(.95f,.36f,.18f)},{"cheolong",new Color(.95f,.82f,.45f)},{"hyeongang",new Color(.25f,.45f,.95f)},{"hwanggyeong",new Color(.95f,.72f,.25f)}};
  [Serializable] class AttractionSwap297{public string renderer,path,material;public int slot;}
  [Serializable] class AttractionOriginal297
  {
   public AttractionSwap297[] lanterns;public string flameShader;public Color flameEmission;public bool flameEmissionOn;
   public string fogMaterial;public int fogRequirements;public string[] inkNames;public float[] inkValues;
   public string postProfile;public bool satOn,thresholdOn,intensityOn,scatterOn;public float saturation,threshold,intensity,scatter;
  }
  [Serializable] class BeaconAnchor297{public float[] core;public float top,platformTop,footprint,stairFront,lip;}

  static string Attraction297(bool revert)
  {
   RequireClean292();var scene=SceneManager.GetActiveScene();string folder=K297+"/Attraction",file=folder+"/original.json";Directory.CreateDirectory(folder);
   var data=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(A297+"/Renderer297.asset");if(data==null)throw new Exception("Renderer297 missing: run lighting first");
   var mist=data.rendererFeatures.OfType<FullScreenPassRendererFeature>().FirstOrDefault(f=>f.name=="CompactMist238")??throw new Exception("CompactMist238 missing on Renderer297");
   var ink=AssetDatabase.LoadAssetAtPath<Material>(A297+"/Materials/M_InkWash297.mat")??throw new Exception("M_InkWash297 missing: run lighting first");
   var flame=AssetDatabase.LoadAssetAtPath<Material>(A297+"/Materials/flame.mat");
   var profile=Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).Where(v=>v.gameObject.scene==scene&&v.isGlobal).OrderByDescending(v=>v.priority).First().sharedProfile;
   string lanternPath=A297+"/Materials/InnLantern297.mat",fogPath=A297+"/Materials/Fog297.mat";
   var beaconShader=Shader.Find("Oheangbu/Finish297/InkBeacon")??throw new Exception("InkBeacon297 shader missing");
   if(!File.Exists(file))
   {
    profile.TryGet<ColorAdjustments>(out var ca0);profile.TryGet<Bloom>(out var bl0);string fogNow=AssetDatabase.GetAssetPath(mist.passMaterial);
    File.WriteAllText(file,JsonUtility.ToJson(new AttractionOriginal297{lanterns=new AttractionSwap297[0],
     flameShader=flame==null?"":flame.shader==beaconShader?"Universal Render Pipeline/Lit":flame.shader.name,
     flameEmission=flame!=null&&flame.HasProperty("_EmissionColor")?flame.GetColor("_EmissionColor"):Color.black,flameEmissionOn=flame!=null&&flame.IsKeywordEnabled("_EMISSION"),
     fogMaterial=fogNow==fogPath?A292+"/Materials/Fog292.mat":fogNow,fogRequirements=(int)mist.requirements,
     inkNames=InkAttraction297.Select(p=>p.name).ToArray(),inkValues=InkAttraction297.Select(p=>ink.HasProperty(p.name)?ink.GetFloat(p.name):0f).ToArray(),
     postProfile=AssetDatabase.GetAssetPath(profile),satOn=ca0!=null&&ca0.saturation.overrideState,saturation=ca0!=null?ca0.saturation.value:0f,
     thresholdOn=bl0!=null&&bl0.threshold.overrideState,threshold=bl0!=null?bl0.threshold.value:.9f,intensityOn=bl0!=null&&bl0.intensity.overrideState,intensity=bl0!=null?bl0.intensity.value:0f,
     scatterOn=bl0!=null&&bl0.scatter.overrideState,scatter=bl0!=null?bl0.scatter.value:.7f},true));
   }
   var rec=JsonUtility.FromJson<AttractionOriginal297>(File.ReadAllText(file));var report=new List<string>();
   if(revert)
   {
    var roots=scene.GetRootGameObjects().Where(x=>x.name==AttractionRoot297).ToArray();foreach(var stale in roots)Object.DestroyImmediate(stale);report.Add("beacon roots removed="+roots.Length);
    var lantern=AssetDatabase.LoadAssetAtPath<Material>(lanternPath);int restored=0;
    var renderers=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).ToArray();
    var bySlot=renderers.GroupBy(r=>SlotPath297(r.transform)).ToDictionary(g=>g.Key,g=>g.ToArray());var byName=renderers.GroupBy(r=>HierarchyPath(r.transform)).ToDictionary(g=>g.Key,g=>g.ToArray());
    foreach(var s in rec.lanterns??new AttractionSwap297[0])
    {
     var original=AssetDatabase.LoadAssetAtPath<Material>(s.material);if(original==null||lantern==null)continue;
     if(!bySlot.TryGetValue(s.renderer,out var rs)&&!byName.TryGetValue(s.path,out rs))continue;
     foreach(var r in rs){var mats=r.sharedMaterials;if(s.slot<mats.Length&&mats[s.slot]==lantern){mats[s.slot]=original;r.sharedMaterials=mats;EditorUtility.SetDirty(r);restored++;}}
    }
    report.Add("lantern slots restored="+restored);
    if(flame!=null&&!string.IsNullOrEmpty(rec.flameShader))
    {
     var sh=Shader.Find(rec.flameShader);if(sh!=null)flame.shader=sh;flame.SetColor("_EmissionColor",rec.flameEmission);
     if(rec.flameEmissionOn)flame.EnableKeyword("_EMISSION");else flame.DisableKeyword("_EMISSION");flame.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;EditorUtility.SetDirty(flame);report.Add("flame -> "+rec.flameShader);
    }
    var fogOriginal=AssetDatabase.LoadAssetAtPath<Material>(rec.fogMaterial);if(fogOriginal!=null)mist.passMaterial=fogOriginal;mist.requirements=(ScriptableRenderPassInput)rec.fogRequirements;EditorUtility.SetDirty(mist);
    RemoveHaloFeature297(data);report.Add("CompactMist238 -> "+rec.fogMaterial+" requirements="+(ScriptableRenderPassInput)rec.fogRequirements+"; halo feature removed");
    for(int i=0;i<(rec.inkNames?.Length??0);i++)if(ink.HasProperty(rec.inkNames[i]))ink.SetFloat(rec.inkNames[i],rec.inkValues[i]);EditorUtility.SetDirty(ink);report.Add("M_InkWash297 values restored="+(rec.inkNames?.Length??0));
    var post=AssetDatabase.LoadAssetAtPath<VolumeProfile>(rec.postProfile)??profile;
    if(post.TryGet<ColorAdjustments>(out var ca)){ca.saturation.overrideState=rec.satOn;ca.saturation.value=rec.saturation;EditorUtility.SetDirty(ca);}
    if(post.TryGet<Bloom>(out var bl)){bl.threshold.overrideState=rec.thresholdOn;bl.threshold.value=rec.threshold;bl.intensity.overrideState=rec.intensityOn;bl.intensity.value=rec.intensity;bl.scatter.overrideState=rec.scatterOn;bl.scatter.value=rec.scatter;EditorUtility.SetDirty(bl);}
    EditorUtility.SetDirty(post);report.Add("post saturation="+rec.saturation+" bloom "+rec.intensity+"@"+rec.threshold+" scatter "+rec.scatter);
    AssetDatabase.SaveAssets();Save292();File.Delete(file);
    return "Reverted #297 attraction: "+string.Join("; ",report);
   }

   // 1. 산정 마석 봉수: kit meshes once, then one beacon per realm high mountain (MountainLayout summit, TEST placement)
   var anchor=JsonUtility.FromJson<BeaconAnchor297>(File.ReadAllText(folder+"/beacon.json"));var core=V297(anchor.core);
   string meshes=A297+"/Attraction/Meshes/";DevSceneKit.EnsureFolder(A297+"/Attraction/Meshes");DevSceneKit.EnsureFolder(A297+"/Attraction/Materials");
   var stone=new (Mesh mesh,string[] slots)[3];var cores=new (Mesh mesh,string[] slots)[3];
   for(int lod=0;lod<3;lod++){stone[lod]=LoadKitMesh297(folder+"/Meshes/Beacon_LOD"+lod+".json",meshes+"Beacon_LOD"+lod+".asset");cores[lod]=LoadKitMesh297(folder+"/Meshes/BeaconCore_LOD"+lod+".json",meshes+"BeaconCore_LOD"+lod+".asset");}
   var collision=LoadKitMesh297(folder+"/Meshes/Beacon_Collision.json",meshes+"Beacon_Collision.asset").mesh;var quad=HaloQuad297(meshes+"BeaconHaloQuad297.asset");
   var haloShader=Shader.Find("Oheangbu/Finish297/BeaconHalo")??throw new Exception("BeaconHalo297 shader missing");
   // beacon materials are created once; afterwards the tuning belongs to the material (mat-tune)
   Material Owned(string name,Shader shader,Action<Material> init)
   {string at=A297+"/Attraction/Materials/"+name+".mat";var made=AssetDatabase.LoadAssetAtPath<Material>(at);if(made==null){made=new Material(shader){name=name};init(made);AssetDatabase.CreateAsset(made,at);}else if(made.shader!=shader)made.shader=shader;return made;}
   var kit=new Dictionary<string,Material>();Material Kit(string slot)=>kit.TryGetValue(slot,out var known)?known:(kit[slot]=KitMaterial297(slot));
   foreach(var stale in scene.GetRootGameObjects().Where(x=>x.name==AttractionRoot297).ToArray())Object.DestroyImmediate(stale);
   var root=new GameObject(AttractionRoot297);Physics.SyncTransforms();
   var session=Session292();var points=session.Content.Points;
   foreach(var realmGroup in session.MountainLayout.Mountains.Where(x=>x!=null&&x.Kind==CompactMountainKind.High&&!string.IsNullOrEmpty(x.Realm)).GroupBy(x=>x.Realm.ToLowerInvariant()))
   {
    var mountain=realmGroup.OrderByDescending(x=>x.MainStory).ThenByDescending(x=>x.Summit.y).First();
    if(!BeaconColour297.TryGetValue(realmGroup.Key,out var colour)){report.Add("skip "+mountain.Id+": no element colour for realm "+mountain.Realm);continue;}
    string realm=char.ToUpperInvariant(realmGroup.Key[0])+realmGroup.Key.Substring(1);
    // the stair and the 아궁이 face the approach (toward the mountain's foot)
    var toFoot=new Vector3(mountain.Foot.x-mountain.Summit.x,0,mountain.Foot.z-mountain.Summit.z);float yaw=toFoot.sqrMagnitude>1?Mathf.Atan2(toFoot.x,toFoot.z)*Mathf.Rad2Deg:0;var rot=Quaternion.Euler(0,yaw,0);
    // keep the platform off every route: summit points are also route ends (#296 approaches, mountain paths), so
    // take the highest ground on rings 10–22 m around the summit that is ≥ 9 m from all route points
    var routePts=new List<Vector3>();
    {string rp=O296+"/Generated/routes.json";if(File.Exists(rp))foreach(var r in JsonUtility.FromJson<Routes292>(File.ReadAllText(rp)).routes??new Route292[0])routePts.AddRange(r.points??new Vector3[0]);}
    foreach(var mm in session.MountainLayout.Mountains)if(mm!=null){routePts.AddRange(mm.MainPath??new Vector3[0]);routePts.AddRange(mm.TemplePath??new Vector3[0]);routePts.AddRange(mm.ReturnPath??new Vector3[0]);}
    routePts.AddRange(session.Content.MainPath??new Vector3[0]);routePts.AddRange(session.Content.BranchPath??new Vector3[0]);
    float Clear(Vector3 q)=>routePts.Count==0?999f:routePts.Min(r=>Vector2.Distance(new Vector2(r.x,r.z),new Vector2(q.x,q.z)));
    var summit=mountain.Summit;
    if(Clear(summit)<9f)
    {
     Vector3 best=summit;float bestY=float.NegativeInfinity;
     for(float rr=10;rr<=22;rr+=3)for(int k=0;k<16;k++){var q=summit+new Vector3(Mathf.Cos(k*Mathf.PI/8),0,Mathf.Sin(k*Mathf.PI/8))*rr;var g=SummitGround297(q,out _);if(Clear(g)>=9f&&g.y>bestY){bestY=g.y;best=g;}}
     if(bestY>float.NegativeInfinity){report.Add("beacon "+realm+" moved "+Vector2.Distance(new Vector2(best.x,best.z),new Vector2(summit.x,summit.z)).ToString("F0")+" m off the summit route end (clearance "+Clear(best).ToString("F1")+" m)");summit=best;}
    }
    var centre=SummitGround297(summit,out string how);
    // seat the platform: its buried skirt (1.2 m) must cover the downhill corners, the uphill corners stay shallow
    var ys=new List<float>{centre.y};foreach(var corner in new[]{new Vector3(-1,0,-1),new Vector3(1,0,-1),new Vector3(1,0,1),new Vector3(-1,0,1)})ys.Add(SummitGround297(centre+rot*(corner*anchor.footprint*.5f),out _).y);
    float low=ys.Min(),high=ys.Max();float seat=high-.6f>low+1.1f?low+1.1f:Mathf.Clamp(centre.y,high-.6f,low+1.1f);
    var beacon=new GameObject("Beacon_"+realm).transform;beacon.SetParent(root.transform,false);beacon.SetPositionAndRotation(new Vector3(centre.x,seat,centre.z),rot);
    var coreMat=Owned("M_Beacon297_"+realm,beaconShader,m=>{m.SetColor("_Color",colour);m.SetFloat("_Intensity",4f);});
    var haloMat=Owned("M_BeaconHalo297_"+realm,haloShader,m=>{m.SetColor("_Color",colour);m.SetFloat("_Intensity",1.1f);m.SetFloat("_Cover",1f);});
    var lods=new List<LOD>();float[] cut={.10f,.025f,.0015f};
    for(int lod=0;lod<3;lod++)
    {
     var part=new GameObject("LOD"+lod);part.transform.SetParent(beacon,false);part.AddComponent<MeshFilter>().sharedMesh=stone[lod].mesh;var sr=part.AddComponent<MeshRenderer>();
     sr.sharedMaterials=stone[lod].slots.Select(s=>s=="beacon_core"?coreMat:Kit(s)).ToArray();sr.shadowCastingMode=lod<2?ShadowCastingMode.On:ShadowCastingMode.Off;
     GameObjectUtility.SetStaticEditorFlags(part,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
     // the fire body is its own renderer: a light source casts no shadow and is never batched (its flicker is object-relative)
     var fire=new GameObject("Core"+lod);fire.transform.SetParent(beacon,false);fire.AddComponent<MeshFilter>().sharedMesh=cores[lod].mesh;var cr=fire.AddComponent<MeshRenderer>();
     cr.sharedMaterials=cores[lod].slots.Select(_=>coreMat).ToArray();cr.shadowCastingMode=ShadowCastingMode.Off;cr.receiveShadows=false;
     lods.Add(new LOD(cut[lod],new Renderer[]{sr,cr}));
    }
    var lodGroup=beacon.gameObject.AddComponent<LODGroup>();lodGroup.SetLODs(lods.ToArray());lodGroup.RecalculateBounds();
    var body=new GameObject("Collision");body.transform.SetParent(beacon,false);body.AddComponent<MeshCollider>().sharedMesh=collision;
    // halo card on the core (outside the LOD group: it carries the beacon to 3 km) + a small point light
    var halo=new GameObject("Halo");halo.transform.SetParent(beacon,false);halo.transform.localPosition=core;halo.AddComponent<MeshFilter>().sharedMesh=quad;var hr=halo.AddComponent<MeshRenderer>();
    hr.sharedMaterial=haloMat;hr.shadowCastingMode=ShadowCastingMode.Off;hr.receiveShadows=false;hr.lightProbeUsage=LightProbeUsage.Off;hr.reflectionProbeUsage=ReflectionProbeUsage.Off;hr.allowOcclusionWhenDynamic=false;
    var light=new GameObject("Light").AddComponent<Light>();light.transform.SetParent(beacon,false);light.transform.localPosition=core+Vector3.up*.3f;
    light.type=LightType.Point;light.color=colour;light.intensity=2f;light.range=18f;light.shadows=LightShadows.None;light.lightmapBakeType=LightmapBakeType.Realtime;
    var near=points.Where(p=>p!=null&&Vector3.Distance(p.Position,beacon.position)<10f).Select(p=>p.Id).ToArray();
    report.Add("beacon "+realm+" ("+mountain.Id+") at "+beacon.position.ToString("F1")+" yaw "+yaw.ToString("F0")+" support="+how+" corners "+low.ToString("F1")+".."+high.ToString("F1")+(near.Length>0?" | content points within 10 m: "+string.Join(",",near):""));
   }

   // 2. 주막 등불: CodexInkLantern InnLantern materials -> InnLantern297 (InkBeacon297: CodexInkLantern has no colour and clamps
   //    to 1.2 LDR); original material per renderer slot recorded (sibling-indexed path + name path)
   var lanternSource=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/ThatchedInn/Materials/InnLantern.mat");
   var inn=AssetDatabase.LoadAssetAtPath<Material>(lanternPath);
   if(inn==null)
   {
    inn=new Material(beaconShader){name="InnLantern297"};inn.SetColor("_Color",new Color(1f,.58f,.26f));inn.SetFloat("_Intensity",1.8f);inn.SetFloat("_Chroma",1.1f);
    inn.SetFloat("_CoreLow",.75f);inn.SetFloat("_Flicker",.05f);inn.SetFloat("_RimInk",.3f);inn.SetFloat("_Cull",lanternSource!=null&&lanternSource.HasProperty("_Cull")?lanternSource.GetFloat("_Cull"):2f);
    AssetDatabase.CreateAsset(inn,lanternPath);
   }
   var swaps=(rec.lanterns??new AttractionSwap297[0]).ToList();var recorded=new HashSet<string>(swaps.Select(s=>s.renderer+"#"+s.slot));int swapped=0;
   foreach(var r in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)))
   {
    var mats=r.sharedMaterials;bool changed=false;
    for(int i=0;i<mats.Length;i++)
    {
     var was=mats[i];if(was==null||was==inn||was.shader==null||was.shader.name!="Oheangbu/CodexInkLantern"||!was.name.Contains("InnLantern"))continue;
     string key=SlotPath297(r.transform);if(recorded.Add(key+"#"+i))swaps.Add(new AttractionSwap297{renderer=key,path=HierarchyPath(r.transform),slot=i,material=AssetDatabase.GetAssetPath(was)});
     mats[i]=inn;changed=true;swapped++;
    }
    if(changed){r.sharedMaterials=mats;EditorUtility.SetDirty(r);}
   }
   rec.lanterns=swaps.ToArray();report.Add("주막 등불: "+swapped+" renderer slots -> InnLantern297 (recorded "+swaps.Count+")");

   // 3. 성황당 flame: marker-writing HDR source (compound rebuilds keep the shader; KitMaterial297 only sets Lit properties)
   if(flame!=null)
   {
    flame.shader=beaconShader;flame.SetColor("_Color",new Color(1f,.66f,.32f));flame.SetFloat("_Intensity",3f);flame.SetFloat("_Flicker",.18f);
    flame.SetFloat("_NoiseScale",14f);flame.SetFloat("_RimInk",.35f);flame.SetFloat("_Cull",2f);EditorUtility.SetDirty(flame);report.Add("flame.mat -> InkBeacon297 (1,.66,.32) x3");
   }

   // 4. fog: private copy with the light exemption, on Renderer297's mist feature only (Reworld292's renderer is never touched)
   var fogShader=Shader.Find("Oheangbu/Finish297/RealmFog297")??throw new Exception("RealmFog297 shader missing");var fog=AssetDatabase.LoadAssetAtPath<Material>(fogPath);
   if(fog==null)
   {
    var src=AssetDatabase.LoadAssetAtPath<Material>(rec.fogMaterial)??throw new Exception("fog source "+rec.fogMaterial+" missing");
    fog=new Material(src){name="Fog297"};fog.shader=fogShader;fog.SetFloat("_LightMarkerFog",.8f);fog.SetFloat("_EmissiveMark",1f);AssetDatabase.CreateAsset(fog,fogPath);
   }
   else if(fog.shader!=fogShader)fog.shader=fogShader;
   mist.passMaterial=fog;mist.requirements|=ScriptableRenderPassInput.Normal;EditorUtility.SetDirty(mist);

   // 5. halo pass after the fog: same event as CompactMist238 (550), later in the feature list (URP sorts passes stably by
   //    event); it draws only the "BeaconHalo297" LightMode, so the regular transparent pass never draws a halo
   var feature=data.rendererFeatures.OfType<RenderObjects>().FirstOrDefault(f=>f.name==HaloFeature297);
   if(feature==null){feature=ScriptableObject.CreateInstance<RenderObjects>();feature.name=HaloFeature297;AssetDatabase.AddObjectToAsset(feature,data);data.rendererFeatures.Add(feature);}
   var fs=feature.settings;fs.passTag=HaloFeature297;fs.Event=RenderPassEvent.BeforeRenderingPostProcessing;fs.overrideMode=RenderObjects.RenderObjectsSettings.OverrideMaterialMode.None;
   fs.filterSettings.RenderQueueType=RenderQueueType.Transparent;fs.filterSettings.LayerMask=~0;fs.filterSettings.PassNames=new[]{HaloFeature297};feature.SetActive(true);
   int mistAt=data.rendererFeatures.IndexOf(mist),haloAt=data.rendererFeatures.IndexOf(feature);
   if(haloAt<mistAt){data.rendererFeatures.RemoveAt(haloAt);data.rendererFeatures.Insert(data.rendererFeatures.IndexOf(mist)+1,feature);}
   EditorUtility.SetDirty(feature);RendererFeatureMap297(data);
   report.Add("fog: CompactMist238 -> Fog297 (RealmFog297, _LightMarkerFog "+fog.GetFloat("_LightMarkerFog").ToString("0.##",CultureInfo.InvariantCulture)+") requirements="+mist.requirements+"; halo feature after it: "+string.Join(",",data.rendererFeatures.Select(f=>f.name)));

   // 6. 수묵담채 land readability + light exemption (TEST values; the originals are in the record)
   var missing=new List<string>();foreach(var (name,value) in InkAttraction297){if(ink.HasProperty(name))ink.SetFloat(name,value);else missing.Add(name);}EditorUtility.SetDirty(ink);
   report.Add("M_InkWash297: "+string.Join(" ",InkAttraction297.Select(p=>p.name+"="+p.value.ToString("0.##",CultureInfo.InvariantCulture)))+(missing.Count>0?" | MISSING (shader not v2?): "+string.Join(",",missing):""));

   // 7. post grade (candidate profile)
   T Comp<T>() where T:VolumeComponent{if(!profile.TryGet<T>(out var vc)){vc=profile.Add<T>(false);vc.name=typeof(T).Name;AssetDatabase.AddObjectToAsset(vc,profile);}return vc;}
   var grade=Comp<ColorAdjustments>();grade.saturation.Override(PostSaturation297);var bloom=Comp<Bloom>();bloom.threshold.Override(BloomThreshold297);bloom.intensity.Override(BloomIntensity297);bloom.scatter.Override(BloomScatter297);
   EditorUtility.SetDirty(grade);EditorUtility.SetDirty(bloom);EditorUtility.SetDirty(profile);
   report.Add("post: saturation "+PostSaturation297+", bloom "+BloomIntensity297+"@"+BloomThreshold297+" scatter "+BloomScatter297+" ("+AssetDatabase.GetAssetPath(profile)+")");

   File.WriteAllText(file,JsonUtility.ToJson(rec,true));AssetDatabase.SaveAssets();Save292();
   string text=string.Join("\n",report);File.WriteAllText(folder+"/attraction.txt",text);return text;
  }

  // highest walkable support at a summit: the terrain, or a rock/structure at most 2.5 m above it (tree crowns and tall
  // structures ignored); the layout point itself when nothing is hit
  static Vector3 SummitGround297(Vector3 p,out string how)
  {
   var hits=Physics.RaycastAll(p+Vector3.up*400f,Vector3.down,1200f,~0,QueryTriggerInteraction.Ignore)
    .Where(h=>h.normal.y>.5f&&!(h.collider is CharacterController)&&h.collider.transform.root.name!=AttractionRoot297).OrderByDescending(h=>h.point.y).ToArray();
   if(hits.Length==0){how="layout (no collider)";return p;}
   var ground=hits.Where(h=>h.collider is TerrainCollider||h.collider.transform.root.name.Contains("Terrain")).Select(h=>(RaycastHit?)h).FirstOrDefault();
   if(ground.HasValue&&hits[0].point.y-ground.Value.point.y>2.5f){how="terrain "+ground.Value.collider.name;return ground.Value.point;}
   how=hits[0].collider.name;return hits[0].point;
  }

  // hierarchy path with sibling indices (unique even where sibling names repeat)
  static string SlotPath297(Transform t)=>(t.parent==null?"":SlotPath297(t.parent)+"/")+t.name+"["+t.GetSiblingIndex()+"]";

  static Mesh HaloQuad297(string asset)
  {
   var m=new Mesh{name=Path.GetFileNameWithoutExtension(asset)};
   m.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};
   m.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};m.normals=Enumerable.Repeat(Vector3.back,4).ToArray();m.triangles=new[]{0,2,1,0,3,2};m.RecalculateTangents();
   // the vertex shader billboards and sizes the card: generous bounds so it is not culled while its centre is just off screen
   m.bounds=new Bounds(Vector3.zero,Vector3.one*12f);return ArtMesh(m,asset);
  }

  static void RemoveHaloFeature297(UniversalRendererData data)
  {
   var f=data.rendererFeatures.FirstOrDefault(x=>x!=null&&x.name==HaloFeature297);if(f==null)return;
   data.rendererFeatures.Remove(f);AssetDatabase.RemoveObjectFromAsset(f);Object.DestroyImmediate(f,true);RendererFeatureMap297(data);
  }
  static void RendererFeatureMap297(UniversalRendererData data)
  {
   var rs=new SerializedObject(data);var map=rs.FindProperty("m_RendererFeatureMap");map.arraySize=data.rendererFeatures.Count;
   for(int i=0;i<map.arraySize;i++){AssetDatabase.TryGetGUIDAndLocalFileIdentifier(data.rendererFeatures[i],out string guid,out long id);map.GetArrayElementAtIndex(i).longValue=id;}
   rs.ApplyModifiedPropertiesWithoutUndo();data.SetDirty();EditorUtility.SetDirty(data);
  }

  // mat-tune:<assetPath>[:<prop>=<value>,...] — tuning tool, records nothing. Floats/ints, colours (#RRGGBB[AA] or r|g|b[|a],
  // sRGB like the inspector), vectors (x|y|z|w), textures (asset path or null), shader=<name>. No assignments = list only.
  static string MatTune297(string arg)
  {
   int at=arg.IndexOf(':');string path=at<0?arg:arg.Substring(0,at);var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null)return "no material at "+path;
   var sb=new StringBuilder();
   if(at>=0)
    foreach(var kv in arg.Substring(at+1).Split(',').Where(part=>part.Contains("=")).Select(part=>part.Split(new[]{'='},2)))
    {
     string k=kv[0].Trim(),v=kv[1].Trim();
     if(k=="shader"){var found=Shader.Find(v);if(found==null)sb.Append("no shader "+v+"; ");else m.shader=found;continue;}
     if(!m.HasProperty(k)){sb.Append("missing "+k+"; ");continue;}
     int pi=m.shader.FindPropertyIndex(k);var type=pi>=0?m.shader.GetPropertyType(pi):ShaderPropertyType.Float;
     float[] Nums()=>v.Split('|').Select(x=>float.Parse(x,CultureInfo.InvariantCulture)).ToArray();
     if(type==ShaderPropertyType.Color)
     {
      Color colour;if(v.StartsWith("#")){if(!ColorUtility.TryParseHtmlString(v,out colour)){sb.Append("bad colour "+v+"; ");continue;}}else{var f=Nums();colour=new Color(f[0],f[1],f[2],f.Length>3?f[3]:1f);}
      m.SetColor(k,colour);
     }
     else if(type==ShaderPropertyType.Vector){var f=Nums();m.SetVector(k,new Vector4(f[0],f.Length>1?f[1]:0,f.Length>2?f[2]:0,f.Length>3?f[3]:0));}
     else if(type==ShaderPropertyType.Texture)m.SetTexture(k,v=="null"||v.Length==0?null:AssetDatabase.LoadAssetAtPath<Texture>(v));
     else if(type==ShaderPropertyType.Int)m.SetInteger(k,int.Parse(v,CultureInfo.InvariantCulture));
     else m.SetFloat(k,float.Parse(v,CultureInfo.InvariantCulture));
    }
   EditorUtility.SetDirty(m);AssetDatabase.SaveAssets();
   string Fmt(Vector4 x)=>string.Join("|",new[]{x.x,x.y,x.z,x.w}.Select(a=>a.ToString("0.###",CultureInfo.InvariantCulture)));
   var shaderNow=m.shader;sb.Append(path+" ("+shaderNow.name+(ShaderUtil.ShaderHasError(shaderNow)?" SHADER ERROR":"")+"):");
   for(int i=0;i<shaderNow.GetPropertyCount();i++)
   {
    string n=shaderNow.GetPropertyName(i);var t=shaderNow.GetPropertyType(i);
    sb.Append(" "+n+"="+(t==ShaderPropertyType.Color?Fmt(m.GetColor(n)):t==ShaderPropertyType.Vector?Fmt(m.GetVector(n)):t==ShaderPropertyType.Texture?(m.GetTexture(n)!=null?m.GetTexture(n).name:"none"):t==ShaderPropertyType.Int?m.GetInteger(n).ToString(CultureInfo.InvariantCulture):m.GetFloat(n).ToString("0.###",CultureInfo.InvariantCulture)));
   }
   return sb.ToString();
  }
 }
}
