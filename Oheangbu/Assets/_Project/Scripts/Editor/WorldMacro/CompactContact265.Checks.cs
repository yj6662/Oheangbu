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
  static string ContactChecks265()
  {
   var scene=FrontageScene249();var roots=scene.GetRootGameObjects();var hub=roots.SelectMany(g=>g.GetComponentsInChildren<CompactEnvironmentContact265>()).Single();var report=new List<string>();
   void C(bool ok,string name)=>report.Add((ok?"PASS ":"FAIL ")+name);
   C(hub.Art.Contacts==hub&&hub.Session==VillageSession(),"candidate hub attached to actual walker and renderer");
   C(hub.Session.Walker.Body.GetComponent<CompactContactRelay265>().Hub==hub,"walker physical contact relay");
   C(hub.Vehicle!=null&&hub.Vehicle.GetComponent<CompactContactRelay265>().Hub==hub,"vehicle physical contact relay");
   C(hub.Surfaces.Length>100&&hub.Surfaces.All(s=>s.Collider!=null&&s.Collider.enabled),"all registered blockers remain enabled for every actor");
   C(hub.Surfaces.Select(s=>s.Collider).Distinct().Count()==hub.Surfaces.Length,"unique surface registrations");
   C(hub.Surfaces.Count(s=>s.Collider is MeshCollider)<=6,"only deliberately low-poly breached walls use mesh collisions");
   C(roots.Single(g=>g.name=="Compact_Rebuild_Terrain").GetComponentsInChildren<MeshCollider>().All(c=>c.sharedMesh==c.GetComponent<MeshFilter>().sharedMesh),"terrain collision still matches terrain geometry");
   C(new[]{"contact_leaf265","contact_stone265","contact_wood265"}.All(id=>hub.Sound.Palette.Find(id)?.Clip!=null&&hub.Sound.Palette.Find(id).MaxConcurrent==2),"three soft contact cues use bounded existing audio mixer pool");
   var soft=hub.Art.Sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Grass||p.Category==Sheet.Kind.Shrub).SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).Select(p=>p.Material).ToArray();
   C(soft.Length>0&&soft.All(m=>m.HasProperty("_ContactSoft265")&&m.GetFloat("_ContactSoft265")>.5f),"all soft vegetation LOD materials opt in");
   C(soft.All(m=>!ShaderUtil.ShaderHasError(m.shader)),"soft contact shaders compile");
   var preview=EditorSceneManager.NewPreviewScene();var go=new GameObject("265 bounded fixture");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,preview);
   var sheet=ScriptableObject.CreateInstance<Sheet>();var art=go.AddComponent<CompactRebuildArtRenderer>();var test=go.AddComponent<CompactEnvironmentContact265>();
   try{
    sheet.Prototypes=new[]{new Sheet.Prototype{Id="soft",Category=Sheet.Kind.Grass}};sheet.FixedPlacements=new[]{new Sheet.FixedPlacement{Id="test",PrototypeId="soft",Position=new Vector3(31.8f,2,31.8f)}};art.Sheet=sheet;art.enabled=false;
    var box=go.AddComponent<BoxCollider>();test.Art=art;test.Surfaces=new[]{new CompactEnvironmentContact265.Surface{Collider=box,Wood=true}};test.Prepare();
    C(test.HasSoftVegetation(new Vector3(32.2f,2,32.2f),1),"spatial grid queries across cell boundaries");
    C(!test.HasSoftVegetation(new Vector3(32.2f,7,32.2f),1),"roof/cave vertical separation excludes false foliage contact");
    C(!test.HasSoftVegetation(Vector3.zero,1),"empty area does not play vegetation rustle");
    C(!test.TryContact(box,Vector3.zero,.2f,100,false),"resting contacts do not spam effects");
    C(test.TryContact(box,Vector3.zero,2,100,false)&&!test.TryContact(box,Vector3.zero,2,100.2f,false)&&test.TryContact(box,Vector3.zero,2,100.7f,false),"contact cooldown and later repeat");
    test.AddPulse(new Vector3(1,2,3),2.6f,100);test.TickPulses(100.5f);C(test.BendPoints[0].w>0&&test.BendPoints[0].w<2.6f,"bend recovers gradually");test.TickPulses(102);C(test.BendPoints.All(v=>v.w==0),"bending returns fully to rest");
    for(int i=0;i<40;i++)test.AddPulse(new Vector3(i,0,0),1.5f,103);C(test.BendPoints.Length==8,"fixed eight-entry deformation history");
    test.ChipMesh=hub.ChipMesh;test.ChipMaterial=hub.ChipMaterial;test.CreateChips();for(int i=0;i<8;i++)test.KickChip(new Vector3(0,1000,0),100);
    C(test.ActiveChips==8&&!test.KickChip(Vector3.zero,100),"dynamic chip cap of eight without growth");test.RetireChips(103);C(test.ActiveChips==0&&test.GetComponentsInChildren<Rigidbody>(true).All(b=>b.isKinematic&&!b.gameObject.activeSelf),"all simulated chips retire after two seconds");
    C(test.KickChip(new Vector3(0,1000,0),104)&&test.GetComponentsInChildren<Rigidbody>(true).Length==8,"retired chip reused without new objects");
   }finally{Object.DestroyImmediate(go);Object.DestroyImmediate(sheet);EditorSceneManager.ClosePreviewScene(preview);}
   foreach(string id in new[]{"AbandonedHome264","CollapsedShelter264"}){var site=roots.Single(g=>g.name==PropsRoot264).transform.Find(id);var f=site.Find("RuinFabric265");C(f!=null&&f.GetComponentsInChildren<MeshFilter>().Length==10,"broken wall and seven irregular beams "+id);C(f.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterial.HasProperty("_BumpMap")&&r.sharedMaterial.GetTexture("_BumpMap")!=null),"private albedo and normal detail "+id);}
   C(VillageSession().Content.SaveSlot=="world-demo-compact-cave-v4","progress save slot unchanged");
   File.WriteAllLines(ContactOut265+"/checks.txt",report);return string.Join("\n",report);
  }
 }
}
