using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static void BuryRuin264(Transform site,Sheet sheet,Func<float,float,RaycastHit> ground)
  {
   // One owned marker makes this safe to repeat; a full prop rebuild starts from clean geometry.
   if(site.Find("BuriedRubble264")!=null)return;
   foreach(var child in site.Cast<Transform>().ToArray()){
    if(child.name.StartsWith("MasonryRubble")){Object.DestroyImmediate(child.gameObject);continue;}
    float depth=child.name.StartsWith("CollapsedRafter")?.09f:child.name.StartsWith("AbandonedPick")?.035f:
     child.name.Contains("Chest")?.16f:child.name=="FallenLattice"?.22f:child.name=="SurvivingRoofFragment"?.30f:.38f;
    child.position-=Vector3.up*depth;
   }
   var root=new GameObject("BuriedRubble264").transform;root.SetParent(site,false);
   var rock=sheet.Prototypes.First(p=>p.Id=="Cheongrim_SM_Rock_K");
   var random=new System.Random(site.name=="AbandonedHome264"?26431:26451);float R()=>(float)random.NextDouble();
   for(int i=0;i<70;i++){
    // Debris fans out from the back wall and one collapsed side, with scattered chips farther out.
    bool large=i<10;float x,z;
    if(i<32){x=-3.5f+R()*7;z=-2.4f+(R()-.5f)*2.0f;}
    else if(i<54){x=-3.2f+(R()-.5f)*2.2f;z=-2.3f+R()*5.5f;}
    else{x=-4.4f+R()*8.8f;z=-3.8f+R()*7.8f;}
    var spot=site.TransformPoint(new Vector3(x,0,z));var hit=ground(spot.x,spot.z);
    var t=new GameObject((large?"FoundationRubble_":"StoneChip_")+i).transform;t.SetParent(root,false);
    foreach(var part in rock.Lods[0].Parts){var g=new GameObject("Stone",typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(t,false);g.transform.localPosition=part.Local.GetColumn(3);g.transform.localRotation=part.Local.rotation;g.transform.localScale=part.Local.lossyScale;g.GetComponent<MeshFilter>().sharedMesh=part.Mesh;g.GetComponent<Renderer>().sharedMaterial=part.Material;}
    Bounds B(){var rs=t.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;}
    var bounds=B();float width=large?.55f+R()*.6f:.10f+R()*.31f;float height=width*(.45f+R()*.45f);
    t.localScale=new Vector3(width/Mathf.Max(.01f,bounds.size.x),height/Mathf.Max(.01f,bounds.size.y),width*(.7f+R()*.6f)/Mathf.Max(.01f,bounds.size.z));
    t.rotation=Quaternion.Euler(R()*16, R()*360,R()*18);bounds=B();
    float burial=bounds.size.y*(.28f+R()*.22f);t.position+=hit.point-new Vector3(bounds.center.x,bounds.min.y+burial,bounds.center.z);
    if(large){
     // A simple inscribed obstacle is enough for the few substantial foundation stones.
     bounds=B();var proxy=new GameObject("RubbleCollision",typeof(BoxCollider));proxy.transform.SetParent(root,false);proxy.transform.position=bounds.center;
     proxy.GetComponent<BoxCollider>().size=new Vector3(bounds.size.x*.76f,bounds.size.y*.7f,bounds.size.z*.76f);
    }
   }
  }
  public static string RuinBurial264(string command)
  {
   var scene=FrontageScene249();var art=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();
   var root=scene.GetRootGameObjects().Single(g=>g.name==PropsRoot264);var ground=FinalSurface(scene);
   var sites=new[]{root.transform.Find("AbandonedHome264"),root.transform.Find("CollapsedShelter264")};
   string dir=PropsOut264+"/Burial";Directory.CreateDirectory(dir);
   if(command=="apply"){
    if(scene.isDirty)throw new Exception("Clean candidate required");
    foreach(var t in sites)BuryRuin264(t,art.Sheet,ground);
    var ledger=JsonUtility.FromJson<PropLedger264>(File.ReadAllText(PropsOut264+"/placements.json"));
    foreach(var t in sites){var rs=t.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);ledger.sites.Single(e=>e.id==t.name).size=b.size;}
    File.WriteAllText(PropsOut264+"/placements.json",JsonUtility.ToJson(ledger,true));
    EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);return "Two ruins partially buried; 140 rock fragments, 20 simple fixed collision proxies, no dynamic rigidbodies.";
   }
   if(command!="checks")throw new ArgumentException(command);
   var checks=new List<string>();void C(bool ok,string name)=>checks.Add((ok?"PASS ":"FAIL ")+name);
   foreach(var t in sites){var rubble=t.Find("BuriedRubble264");C(rubble!=null,"burial root "+t.name);if(rubble==null)continue;
    var stones=rubble.Cast<Transform>().Where(c=>c.name.StartsWith("FoundationRubble_")||c.name.StartsWith("StoneChip_")).ToArray();
    C(stones.Length==70,"70 stones "+t.name);C(rubble.GetComponentsInChildren<Collider>().Length==10&&rubble.GetComponentsInChildren<MeshCollider>().Length==0,"only 10 primitive colliders "+t.name);
    C(stones.All(s=>{var rs=s.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);float y=ground(b.center.x,b.center.z).point.y;return b.min.y<y&&b.max.y>y;}),"every rubble mesh straddles ground "+t.name);
    C(t.GetComponentsInChildren<Rigidbody>().Length==0,"no simulated debris bodies "+t.name);
    var post=t.Cast<Transform>().First(p=>p.name=="WeatheredPost");C(ground(post.position.x,post.position.z).point.y-post.position.y>.35f,"structural base buried at least 35cm "+t.name);
   }
   File.WriteAllLines(dir+"/checks.txt",checks);return string.Join("\n",checks);
  }
 }
}
