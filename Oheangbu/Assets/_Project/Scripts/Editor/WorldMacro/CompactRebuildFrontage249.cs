using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string FrontageOutput = Output + "/Frontage249";
  const string FrontageRoot = "Village249Frontage";
  [Serializable] public sealed class FrontageRecord249
  {
   public string scene;
   public FrontageEntry249[] groups;
   public int renderers, colliders;
   public string[] objects;
  }
  [Serializable] public sealed class FrontageEntry249 { public string pointId; public Vector3 anchor; public float yaw; }

  static Scene FrontageScene249()
  {
   var scene = SceneManager.GetActiveScene();
   if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.path.Contains("slice-5e82ecd76d2a/"))
    throw new Exception("Latest candidate in Edit mode required");
   return scene;
  }

  public static string FrontageBuild249()
  {
   var scene = FrontageScene249();
   Directory.CreateDirectory(FrontageOutput);
   var s = VillageSession();
   var manifest = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();
   string layoutPath = AssetDatabase.GetAssetPath(manifest.Layout);
   string backup = FrontageOutput + "/Backup/" + Path.GetFileName(layoutPath);
   if (!File.Exists(backup)) File.Copy(layoutPath, backup);
   var old = scene.GetRootGameObjects().SingleOrDefault(g => g.name == FrontageRoot);
   if (old != null) Object.DestroyImmediate(old);
   var ground = FinalSurface(scene);
   var root = new GameObject(FrontageRoot);
   string folder = Path.GetDirectoryName(scene.path).Replace('\\','/') + "/Frontage249";
   Directory.CreateDirectory(folder); AssetDatabase.Refresh();
   var wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/ThatchedInn/Materials/house_Re_Wood_01_Muted.mat");
   var lampPaper = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/ThatchedInn/Materials/InnLantern.mat");
   Material Mat(string name, Color color)
   {
    string path = folder + "/" + name + ".mat";
    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
    if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
    m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", .05f); m.SetFloat("_Cull", 0); EditorUtility.SetDirty(m); return m;
   }
   var cloth = Mat("FadedHemp", new Color(.40f,.43f,.40f));
   var iron = Mat("WornIron", new Color(.13f,.14f,.13f));
   var fiber = Mat("StrawCord", new Color(.42f,.36f,.25f));
   var entries = new List<FrontageEntry249>();
   Transform Group(string pointId, Vector3 forward)
   {
    var p = s.Content.Points.Single(x => x.Id == pointId);
    var g = new GameObject(pointId); g.transform.SetParent(root.transform);
    g.transform.SetPositionAndRotation(p.Position, Quaternion.LookRotation(forward));
    entries.Add(new FrontageEntry249 { pointId = pointId, anchor = p.Position, yaw = g.transform.eulerAngles.y }); return g.transform;
   }
   GameObject Solid(Transform parent, string name, Vector3 pos, Vector3 size, Material material, bool collide = false, PrimitiveType shape = PrimitiveType.Cube)
   {
    var go = GameObject.CreatePrimitive(shape); go.name = name; go.transform.SetParent(parent, false);
    go.transform.localPosition = pos; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = material;
    if (!collide) Object.DestroyImmediate(go.GetComponent<Collider>());
    return go;
   }
   void Beam(Transform parent, string name, Vector3 a, Vector3 b, float width, Material material)
   {
    var go = Solid(parent, name, (a+b)*.5f, new Vector3(width, (b-a).magnitude, width), material);
    go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b-a);
   }
   Transform Grounded(Transform parent, string name, Vector3 local)
   {
    var g = new GameObject(name); g.transform.SetParent(parent, false);
    var p = parent.TransformPoint(local); p.y = ground(p.x,p.z).point.y;
    g.transform.position = p; return g.transform;
   }
   GameObject Prop(Transform parent, string name, string prefabName, Vector3 local, float height, float yaw = 0)
   {
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HwaseongHaenggung/Prefabs/" + prefabName + ".prefab");
    if (prefab == null) throw new Exception("Missing frontage prop " + prefabName);
    var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab); g.name = name; g.transform.SetParent(parent,false);
    g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.Euler(0,yaw,0);
    foreach (var c in g.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
    Bounds B() { var rr = g.GetComponentsInChildren<Renderer>(); var b = rr[0].bounds; foreach (var r in rr) b.Encapsulate(r.bounds); return b; }
    var bounds = B(); g.transform.localScale *= height / Mathf.Max(.001f,bounds.size.y); bounds = B();
    if(prefabName=="SM_M_WoodLog" && Mathf.Max(bounds.size.x,bounds.size.z)>2.8f)
    {g.transform.localScale *= 2.8f/Mathf.Max(bounds.size.x,bounds.size.z);bounds=B();}
    g.transform.position += parent.TransformPoint(local) - new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
    return g;
   }
   void Table(Transform group, string name, Vector3 local, float width, float depth)
   {
    var t = Grounded(group,name,local);
    Solid(t,"PlankTop",new Vector3(0,.92f,0),new Vector3(width,.12f,depth),wood,true);
    foreach(float x in new[]{-width*.4f,width*.4f}) foreach(float z in new[]{-depth*.34f,depth*.34f})
     Solid(t,"TrestleLeg",new Vector3(x,.44f,z),new Vector3(.12f,.88f,.12f),wood,true);
    Beam(t,"CrossBrace",new Vector3(-width*.4f,.2f,0),new Vector3(width*.4f,.70f,0),.085f,wood);
   }
   void Awning(Transform group)
   {
    // Sag and uneven hem are geometry. The shop silhouette reads from the yard without a sign.
    var t = Grounded(group,"HempAwning",new Vector3(1.3f,0,-1.5f));
    foreach(float x in new[]{-2.4f,2.4f}) foreach(float z in new[]{-1.4f,1.3f})
    {
     float h = z<0 ? 3.15f : 2.65f;
     Solid(t,"AwningPost",new Vector3(x,h*.5f,z),new Vector3(.12f,h,.12f),wood,true);
     Beam(t,"Lashing",new Vector3(x-.13f,h-.15f,z),new Vector3(x+.13f,h-.15f,z),.055f,fiber);
    }
    Beam(t,"FrontRail",new Vector3(-2.5f,2.65f,1.3f),new Vector3(2.5f,2.65f,1.3f),.11f,wood);
    Beam(t,"BackRail",new Vector3(-2.5f,3.15f,-1.4f),new Vector3(2.5f,3.15f,-1.4f),.11f,wood);
    var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
    for(int z=0;z<=8;z++) for(int x=0;x<=20;x++)
    {
     float u=x/20f,v=z/8f;
     vertices.Add(new Vector3(Mathf.Lerp(-2.5f,2.5f,u),Mathf.Lerp(3.16f,2.59f,v)-.15f*Mathf.Sin(u*Mathf.PI)+.035f*Mathf.Sin(u*31)*v,Mathf.Lerp(-1.5f,1.5f,v)));
     uv.Add(new Vector2(u,v)); if(x>0&&z>0){int n=z*21+x;triangles.AddRange(new[]{n-22,n-1,n,n-22,n,n-21});}
    }
    string path=folder+"/Awning.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
    if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}mesh.Clear();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
    var canopy=new GameObject("SaggingHemp",typeof(MeshFilter),typeof(MeshRenderer));canopy.transform.SetParent(t,false);canopy.GetComponent<MeshFilter>().sharedMesh=mesh;canopy.GetComponent<Renderer>().sharedMaterial=cloth;
   }
   var shop = Group("village_shop",Vector3.left);
   scene.GetRootGameObjects().Single(x=>x.name=="Village245").transform.Find("village_shop").rotation=shop.rotation;
   Awning(shop); Table(shop,"SalesTrestle",new Vector3(2.0f,0,-1.3f),2.8f,1.05f);
   var display=shop.Find("SalesTrestle");
   for(int i=0;i<3;i++)
   {
    Solid(display,"FoldedHemp",new Vector3(.45f,1.025f+i*.10f,0),new Vector3(.72f,.085f,.53f),cloth);
    var shaft=Solid(display,"BrushShaft",new Vector3(-.85f+i*.22f,1.02f,0),new Vector3(.035f,.35f,.035f),wood,false,PrimitiveType.Cylinder);shaft.transform.localRotation=Quaternion.Euler(90,0,-9+i*8);
    var tip=Solid(display,"BrushHair",new Vector3(-.85f+i*.22f,1.02f,.39f),new Vector3(.065f,.08f,.065f),iron,false,PrimitiveType.Capsule);tip.transform.localRotation=Quaternion.Euler(90,0,0);
   }
   var stock=Grounded(shop,"PackedStock",new Vector3(4.5f,0,-2.0f));
   Prop(stock,"StockChest","SM_M_WoodenBox",Vector3.zero,.85f,8);
   Solid(stock,"StockCollision",new Vector3(0,.4f,0),new Vector3(1.1f,.8f,.9f),wood,true).GetComponent<Renderer>().enabled=false;
   Prop(stock,"SmallChest","SM_M_WoodenBox",new Vector3(-.8f,0,.65f),.55f,-14);

   var artisan=Group("village_artisan",Vector3.right);
   scene.GetRootGameObjects().Single(x=>x.name=="Village245").transform.Find("village_artisan").rotation=artisan.rotation;
   Table(artisan,"JoinerBench",new Vector3(-2.4f,0,-1.0f),3.4f,1.25f);var bench=artisan.Find("JoinerBench");
   Solid(bench,"WorkedBoard",new Vector3(0,1.04f,0),new Vector3(2.7f,.10f,.42f),fiber);
   Solid(bench,"PlaneBody",new Vector3(.6f,1.14f,.08f),new Vector3(.33f,.13f,.14f),wood);
   Solid(bench,"PlaneIron",new Vector3(.62f,1.22f,.08f),new Vector3(.025f,.13f,.10f),iron).transform.localRotation=Quaternion.Euler(0,0,-25);
   Beam(bench,"MalletHandle",new Vector3(-.55f,1.12f,-.15f),new Vector3(-.85f,1.12f,.17f),.05f,wood);
   Solid(bench,"MalletHead",new Vector3(-.85f,1.14f,.17f),new Vector3(.24f,.12f,.12f),wood);
   var rack=Grounded(artisan,"SeasoningRack",new Vector3(3.8f,0,-1.5f));
   foreach(float x in new[]{-1.45f,1.45f}) Solid(rack,"RackUpright",new Vector3(x,1.25f,0),new Vector3(.18f,2.5f,.20f),wood,true);
   for(int level=0;level<4;level++)
   {
    Solid(rack,"Spacer",new Vector3(0,.24f+level*.48f,0),new Vector3(3.2f,.12f,.7f),wood);
    for(int j=0;j<3;j++) Solid(rack,"SeasoningBoard",new Vector3((j-1)*.035f,.35f+level*.48f,(j-1)*.27f),new Vector3(3.45f-j*.16f,.085f,.22f),wood);
   }
   Solid(rack,"RackCollision",new Vector3(0,1.15f,0),new Vector3(3.5f,2.3f,.85f),wood,true).GetComponent<Renderer>().enabled=false;
   var cut=Grounded(artisan,"CutEnds",new Vector3(-4.2f,0,1.1f));
   Prop(cut,"CutLogs","SM_M_WoodLog",Vector3.zero,.65f,35);
   for(int i=0;i<10;i++) Solid(cut,"Offcut",new Vector3(Mathf.Sin(i*2.1f)*.6f,.06f+i%2*.06f,Mathf.Cos(i*1.7f)*.45f),new Vector3(.1f,.05f,.28f+i%3*.12f),fiber).transform.localRotation=Quaternion.Euler(0,i*37,0);

   var cp=s.Content.Checkpoints.Single(x=>x.Id=="village_rest");var doorPoint=s.Content.Points.Single(x=>x.Id=="village_rest");var outward=cp.Feet-doorPoint.Position;outward.y=0;
   var inn=Group("village_rest",outward.normalized);
   foreach(float x in new[]{-2.8f,2.8f})
   {
    var lamp=Grounded(inn,x<0?"DoorLanternLeft":"DoorLanternRight",new Vector3(x,0,.2f));
    Solid(lamp,"LampPost",new Vector3(0,1.5f,0),new Vector3(.13f,3,.13f),wood,true);
    Beam(lamp,"LampArm",new Vector3(0,2.95f,0),new Vector3(0,2.95f,.65f),.10f,wood);
    Solid(lamp,"PaperLantern",new Vector3(0,2.4f,.45f),new Vector3(.38f,.25f,.38f),lampPaper,false,PrimitiveType.Cylinder);
    foreach(float y in new[]{2.13f,2.67f}) Solid(lamp,"LanternRim",new Vector3(0,y,.45f),new Vector3(.43f,.025f,.43f),wood,false,PrimitiveType.Cylinder);
    for(int i=0;i<8;i++){float a=i*Mathf.PI/4;var p=new Vector3(Mathf.Cos(a)*.19f,2.4f,.45f+Mathf.Sin(a)*.19f);Solid(lamp,"LanternRib",p,new Vector3(.018f,.52f,.018f),wood);}
    Beam(lamp,"LanternCord",new Vector3(0,2.68f,.45f),new Vector3(0,2.95f,.45f),.018f,fiber);
   }
   var cargo=Grounded(inn,"TravelCargo",new Vector3(-4.8f,0,1));
   Prop(cargo,"GuestCargo","SM_M_WoodenBox",Vector3.zero,.70f,12);
   Prop(cargo,"GuestCargoSmall","SM_M_WoodenBox",new Vector3(.1f,.70f,0),.42f,-8);
   Table(inn,"LuggageBench",new Vector3(4.7f,0,1),2.5f,.75f);

   // The single placement ledger refers to the actual interaction anchors, not copied NPC coordinates.
   var village=manifest.Layout.Places.Single(x=>x.Id=="village");
   village.SceneRoots=village.SceneRoots.Where(x=>x!=FrontageRoot).Concat(new[]{FrontageRoot}).ToArray();
   foreach(var e in entries)
   {
    var place=e.pointId=="village_rest"?village:manifest.Layout.Places.Single(x=>x.Id==e.pointId);
    string binding=FrontageRoot+"/"+e.pointId;
    place.SceneRoots=place.SceneRoots.Where(x=>x!=binding).Concat(new[]{binding}).ToArray();
   }
   Physics.SyncTransforms();
   var record=new FrontageRecord249 { scene=scene.path,groups=entries.ToArray(),renderers=root.GetComponentsInChildren<Renderer>().Count(x=>x.enabled),colliders=root.GetComponentsInChildren<Collider>().Length,
    objects=root.GetComponentsInChildren<Transform>().Select(t=>AnimationUtility.CalculateTransformPath(t,root.transform)+" | "+t.localPosition.ToString("F4")+" | "+t.localScale.ToString("F4")).ToArray() };
   File.WriteAllText(FrontageOutput+"/placements.json",JsonUtility.ToJson(record,true));
   File.WriteAllText(Output+"/layout.json",JsonUtility.ToJson(manifest.Layout,true));
   EditorUtility.SetDirty(manifest.Layout);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return "Frontage249 built: "+record.renderers+" renderers, "+record.colliders+" colliders; NavMesh needs rebake";
  }

  public static string FrontageAudit249()
  {
   var scene=FrontageScene249();var s=VillageSession();var root=scene.GetRootGameObjects().Single(x=>x.name==FrontageRoot);var record=JsonUtility.FromJson<FrontageRecord249>(File.ReadAllText(FrontageOutput+"/placements.json"));
   var checks=new List<string>();void Check(bool ok,string name){checks.Add((ok?"PASS ":"FAIL ")+name);}
   Check(root.transform.childCount==3,"exactly three service frontage groups");
   Check(root.GetComponentsInChildren<MonoBehaviour>().Length==0,"no additional runtime scripts or interaction state");
   Check(root.GetComponentsInChildren<Renderer>().Where(x=>x.enabled).All(x=>x.sharedMaterials.All(m=>m!=null&&m.shader!=null&&m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader))),"all frontage materials supported without shader errors");
   var ledger=JsonUtility.FromJson<VillageLedger245>(File.ReadAllText(VillageOutput+"/ledger.json"));
   var cols=root.GetComponentsInChildren<Collider>();var ground=FinalSurface(scene);Physics.SyncTransforms();
   foreach(var e in record.groups)
   {
    var p=s.Content.Points.Single(x=>x.Id==e.pointId);Check(Vector3.Distance(p.Position,e.anchor)<.001f,"anchor agrees with content: "+e.pointId);
    var g=root.transform.Find(e.pointId);var end=p.Position;
    if(e.pointId=="village_rest")end+=g.forward*2.7f;
    bool found=NavMesh.SamplePosition(end,out var hit,1.5f,NavMesh.AllAreas);var nav=new NavMeshPath();
    Check(found&&NavMesh.CalculatePath(ledger.main.Last(),hit.position,NavMesh.AllAreas,nav)&&nav.status==NavMeshPathStatus.PathComplete,"yard navigation reaches "+e.pointId);
    bool clear=true;
    for(int i=0;i<=10;i++)
    {
     var feet=p.Position+g.forward*(.8f+i*.3f);feet.y=ground(feet.x,feet.z).point.y;
     clear &= !Physics.OverlapCapsule(feet+Vector3.up*.5f,feet+Vector3.up*1.5f,.45f,~0,QueryTriggerInteraction.Ignore).Any(c=>cols.Contains(c));
    }
    Check(clear,"frontage leaves 0.9m capsule approach clear: "+e.pointId);
   }
   File.WriteAllText(FrontageOutput+"/audit.txt",string.Join("\n",checks));return string.Join("\n",checks);
  }

  public static string FrontageCapture249(string label)
  {
   var scene=FrontageScene249();var s=VillageSession();var ground=FinalSurface(scene);Directory.CreateDirectory(FrontageOutput);
   var go=new GameObject("FrontageReviewCamera");var camera=go.AddComponent<Camera>();camera.CopyFrom(s.Walker.ViewCamera);camera.enabled=false;EditorUtility.CopySerialized(s.Walker.ViewCamera.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   var art=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var previous=art.Observer;art.Observer=camera;
   Vector3 G(float x,float z,float y)=>ground(x,z).point+Vector3.up*y;
   var views=new List<(string,Vector3,Vector3)>{("arrival",G(2750,2216,1.9f),G(2700,2180,2)),("court",G(2710,2199,1.9f),G(2693,2160,2)),("shop",G(2701,2187,1.8f),G(2713,2183,1.4f)),("artisan",G(2697,2197,1.8f),G(2684,2190,1.2f))};
   var p=s.Content.Points.Single(x=>x.Id=="village_rest").Position;var d=s.Content.Checkpoints.Single(x=>x.Id=="village_rest").Feet-p;d.y=0;d.Normalize();views.Add(("inn",p+d*13+Vector3.up*1.9f,p+Vector3.up*1.7f));
   var rt=new RenderTexture(1920,1080,24);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);var active=RenderTexture.active;
   try{camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=62;foreach(var v in views){camera.transform.SetPositionAndRotation(v.Item2,Quaternion.LookRotation(v.Item3-v.Item2));camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(FrontageOutput+"/"+label+"_"+v.Item1+".png",tex.EncodeToPNG());}}
   finally{art.Observer=previous;camera.targetTexture=null;RenderTexture.active=active;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}return "Five frontage captures: "+label;
  }

  public static string FrontageRuntime249(string command)
  {
   var s=VillageSession();if(!EditorApplication.isPlaying||!s.TestSaveSuffix.StartsWith("_compact_slice_"))throw new Exception("Private Play diagnostic required");
   var ui=PlaytestUiRoot.Instance;var results=new List<string>();
   void Check(bool ok,string text){results.Add((ok?"PASS ":"FAIL ")+text);File.AppendAllText(FrontageOutput+"/runtime.txt",results.Last()+"\n");if(!ok)throw new Exception(text);}
   if(command=="services")
   {
    File.WriteAllText(FrontageOutput+"/runtime.txt","");
    foreach(string id in new[]{"village_shop","village_artisan"})
    {
     Check(ApproachVillage(id),"NavMesh approach with new frontage: "+id);
     Check(s.Interact(id),"actual interaction accepted: "+id);
     Check(ui.GetComponentsInChildren<Transform>().Any(x=>x.name=="EquipmentTradeWindow"),"dedicated trade window opens: "+id);
     ui.CloseMenu();ui.Gate.ReleaseImmediately();
    }
    Check(ApproachVillage("village_rest"),"NavMesh approach to guesthouse door");
    Check(s.Interact("village_rest"),"door rest interaction accepted");
   }
   else if(command=="rest-return")
   {
    Check(Application.isFocused,"GameView/application focus during return check");
    Check(!s.RestPresentationActive&&s.VillageRestPresentation.CompletedCount>0,"guesthouse door rest completes");
    Check(s.Progress.ledger.checkpoint=="village_rest","guesthouse checkpoint committed");
    var actual=s.Walker.Body.transform.position;var expected=s.Content.Checkpoints.Single(x=>x.Id=="village_rest").Feet;
    Check(Vector3.Distance(actual,expected)<.5f,"controller returns to guesthouse checkpoint: actual="+actual+" expected="+expected);
   }
   else throw new Exception("Unknown frontage diagnostic");
   return string.Join("\n",results);
  }
 }
}
