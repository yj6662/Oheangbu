using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 compound interactions: 빗장 shortcut doors (WorldShortcutDoor + an inside-only content point) and the 성황당 rest
 // (content Rest point + CheckpointSpec). Both reuse the existing session interaction/checkpoint rules; the ids added to
 // the candidate content are recorded in <compound>/content.json and removed again by compound-remove.
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] class KitBlocker297{public float[] centre,size;}
  [Serializable] class KitDoor297{public string id,gate,prompt,text;public float[] position,bar,barOpen,interaction;public float yaw,hinge,thick;public KitBlocker297 blocker;}
  [Serializable] class KitRest297{public string id,label,altar;public float[] position,feet,flames;public float feetYaw,radius;}
  [Serializable] class KitContent297{public string[] points,checkpoints;}

  // highest walkable physical support under p (from `up` above), ignoring `ignore`'s own colliders
  static Vector3 Support297(Vector3 p,float up=2f,Transform ignore=null)
  {
   var hits=Physics.RaycastAll(p+Vector3.up*up,Vector3.down,up+6f,~0,QueryTriggerInteraction.Ignore)
    .Where(h=>h.normal.y>.5f&&!(h.collider is CharacterController)&&(ignore==null||!h.transform.IsChildOf(ignore))).OrderByDescending(h=>h.point.y).ToArray();
   return hits.Length>0?hits[0].point:p;
  }

  static Transform DoorPart297(Transform door,string src,string meshes,string key,Vector3 local,bool moving)
  {
   var go=new GameObject(key);go.transform.SetParent(door,false);go.transform.localPosition=local;
   var lods=new List<LOD>();float[] cut={.12f,.03f,.006f};
   for(int lod=0;lod<3;lod++)
   {
    string json=src+door.name+"_"+key+"_LOD"+lod+".json";if(!File.Exists(json))continue;
    var (mesh,slots)=LoadKitMesh297(json,meshes+door.name+"_"+key+"_LOD"+lod+".asset");
    var part=new GameObject("LOD"+lod);part.transform.SetParent(go.transform,false);
    part.AddComponent<MeshFilter>().sharedMesh=mesh;var r=part.AddComponent<MeshRenderer>();
    r.sharedMaterials=slots.Select(KitMaterial297).ToArray();r.shadowCastingMode=lod<2?ShadowCastingMode.On:ShadowCastingMode.Off;
    lods.Add(new LOD(cut[lod],new Renderer[]{r}));
   }
   if(lods.Count>0){var g=go.AddComponent<LODGroup>();g.SetLODs(lods.ToArray());g.RecalculateBounds();}
   if(!moving){GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);foreach(Transform t in go.transform)GameObjectUtility.SetStaticEditorFlags(t.gameObject,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);}
   return go.transform;
  }

  static string CompoundInteractions297(string name,KitCompound297 data,GameObject root)
  {
   var session=Session292();var content=session.Content;string src=K297+"/"+name+"/Meshes/";string meshes=A297+"/"+name+"/Meshes/";
   var points=content.Points.ToList();var visuals=(session.InteractionVisuals??Array.Empty<WorldMacroPlaytestSession.InteractionVisual>()).ToList();
   var checkpoints=content.Checkpoints.ToList();var addedPoints=new List<string>();var addedCheckpoints=new List<string>();var report=new List<string>();
   void Replace(PrologueContentSO.Point p,Renderer[] rs)
   {points.RemoveAll(x=>x!=null&&x.Id==p.Id);visuals.RemoveAll(v=>v!=null&&v.Id==p.Id);points.Add(p);visuals.Add(new WorldMacroPlaytestSession.InteractionVisual{Id=p.Id,Renderers=rs});addedPoints.Add(p.Id);}
   Physics.SyncTransforms();
   foreach(var d in data.doors??Array.Empty<KitDoor297>())
   {
    var door=new GameObject("Door_"+d.gate).transform;door.SetParent(root.transform,false);
    var at=V297(d.position);door.SetPositionAndRotation(Support297(at,1.5f,door),Quaternion.Euler(0,d.yaw,0));
    var left=DoorPart297(door,src,meshes,"left",new Vector3(-d.hinge,0,0),true);var right=DoorPart297(door,src,meshes,"right",new Vector3(d.hinge,0,0),true);
    // the 빗장 rides on the left leaf: it slides clear of the right leaf's keeper, then swings open with its leaf
    var bar=DoorPart297(door,src,meshes,"bar",Vector3.zero,true);bar.SetParent(left,false);bar.localPosition=new Vector3(d.hinge,0,0);
    // blocker + carving obstacle are authored disabled: the edit-time NavMesh bake sees the passage open and the door
    // closes it at runtime (WorldShortcutDoor enables both until the committed fact opens it)
    var block=new GameObject("Blocker");block.transform.SetParent(door,false);var bc=block.AddComponent<BoxCollider>();bc.center=V297(d.blocker.centre);bc.size=V297(d.blocker.size);bc.enabled=false;
    var ob=block.AddComponent<NavMeshObstacle>();ob.shape=NavMeshObstacleShape.Box;ob.center=bc.center;ob.size=bc.size;ob.carving=true;ob.carveOnlyStationary=false;ob.enabled=false;
    var sd=door.gameObject.AddComponent<WorldShortcutDoor>();sd.Session=session;sd.Id=d.id;sd.LeftLeaf=left;sd.RightLeaf=right;sd.Bar=bar;
    sd.BarOpenOffset=V297(d.barOpen);sd.OpenDegrees=88;sd.Blockers=new Collider[]{bc};sd.Obstacle=ob;
    var ip=Support297(door.TransformPoint(V297(d.interaction)),1.5f,door);
    Replace(new PrologueContentSO.Point{Id=d.id,Kind=PrologueInteractionKind.Currency,Position=ip,Radius=2f,Prompt=d.prompt,Text=d.text,Currency=0},bar.GetComponentsInChildren<Renderer>(true));
    report.Add("door "+d.id+" at "+door.position.ToString("F2")+" interaction "+ip.ToString("F2"));
   }
   var rest=data.rest;
   if(rest!=null&&!string.IsNullOrEmpty(rest.id))
   {
    var altar=root.transform.Find(rest.altar)??throw new Exception("rest altar "+rest.altar+" missing");
    altar.position=Support297(altar.position,1.5f,altar);
    var cp=altar.gameObject.AddComponent<WorldMacroContentPoint>();cp.Id=rest.id;cp.Visual=altar;
    var flame=KitMaterial297("flame");flame.EnableKeyword("_EMISSION");flame.SetColor("_EmissionColor",new Color(1f,.62f,.30f)*2.2f);flame.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;EditorUtility.SetDirty(flame);
    for(int i=0;i+2<(rest.flames?.Length??0);i+=3)
    {
     var l=new GameObject("CandleLight_"+i/3).AddComponent<Light>();l.transform.SetParent(altar,false);l.transform.localPosition=new Vector3(rest.flames[i],rest.flames[i+1]+.06f,rest.flames[i+2]);
     l.type=LightType.Point;l.color=new Color(1f,.74f,.48f);l.intensity=1.1f;l.range=6f;l.shadows=LightShadows.None;
    }
    var feet=Support297(V297(rest.feet),1.5f);
    Replace(new PrologueContentSO.Point{Id=rest.id,Kind=PrologueInteractionKind.Rest,Position=altar.position,Radius=rest.radius,Prompt="",Text=""},altar.GetComponentsInChildren<Renderer>(true).Where(r=>r.GetComponent<MeshFilter>()!=null).ToArray());
    checkpoints.RemoveAll(c=>c!=null&&c.Id==rest.id);checkpoints.Add(new WorldMacroPlaytestSO.CheckpointSpec{Id=rest.id,Label=rest.label,Feet=feet,Yaw=rest.feetYaw});addedCheckpoints.Add(rest.id);
    report.Add("rest "+rest.id+" altar "+altar.position.ToString("F2")+" feet "+feet.ToString("F2"));
   }
   if(addedPoints.Count==0)return "interactions=none";
   content.Points=points.ToArray();content.Checkpoints=checkpoints.ToArray();session.InteractionVisuals=visuals.ToArray();
   EditorUtility.SetDirty(content);EditorUtility.SetDirty(session);
   File.WriteAllText(K297+"/"+name+"/content.json",JsonUtility.ToJson(new KitContent297{points=addedPoints.ToArray(),checkpoints=addedCheckpoints.ToArray()},true));
   return string.Join("\n",report);
  }

  // closed-door evidence (blockers enabled only for this check): the outside walker stops at the leaves, cannot focus the
  // inside-only point (range or line of sight), and the inside player can. Edit-mode physics only, not manual play.
  static List<string> DoorClosedChecks297(KitCompound297 data,CharacterController cc,GameObject go)
  {
   var lines=new List<string>();var root=GameObject.Find(data.root);if(root==null)return lines;var session=Session292();float eye=session.Walker.EyeHeight;
   foreach(var door in root.GetComponentsInChildren<WorldShortcutDoor>(true))
   {
    var point=session.Content.Points.FirstOrDefault(p=>p!=null&&p.Id==door.Id);if(point==null){lines.Add("FAIL door "+door.Id+": no content point");continue;}
    var prior=door.Blockers.Select(b=>b.enabled).ToArray();
    try
    {
     foreach(var b in door.Blockers)b.enabled=true;Physics.SyncTransforms();
     var from=Support297(door.transform.TransformPoint(new Vector3(0,0,-3.5f)),1.5f);var to=door.transform.TransformPoint(new Vector3(0,0,2f));
     cc.enabled=false;go.transform.position=from+Vector3.up*(cc.height*.5f-cc.center.y+.3f);go.SetActive(true);cc.enabled=true;Physics.SyncTransforms();
     for(int i=0;i<600;i++){var f=cc.transform.TransformPoint(cc.center)-Vector3.up*(cc.height*.5f);var d=to-f;d.y=0;if(d.magnitude<.2f)break;var m=d.normalized*(4.5f/60);m.y=-.12f;cc.Move(m);}
     var feet=cc.transform.TransformPoint(cc.center)-Vector3.up*(cc.height*.5f);float depth=door.transform.InverseTransformPoint(feet).z;
     bool Sight(Vector3 body){Vector3 e=body+Vector3.up*eye,t=point.Position+Vector3.up*1.25f-e;return !Physics.RaycastAll(e,t.normalized,t.magnitude,~0,QueryTriggerInteraction.Ignore).Any(h=>h.collider!=cc);}
     cc.enabled=false;go.SetActive(false);Physics.SyncTransforms();
     // outside: line of sight must be blocked even from the leaves themselves (range alone is not relied on)
     var hug=Support297(door.transform.TransformPoint(new Vector3(0,0,-.2f-cc.radius)),1.5f);
     bool outsideSight=Sight(feet)||Sight(hug);var inside=Support297(door.transform.TransformPoint(new Vector3(0,0,1f)),1.5f);
     bool inner=Vector3.Distance(inside,point.Position)<=point.Radius&&Sight(inside);
     lines.Add((depth<.05f&&!outsideSight&&inner?"PASS":"FAIL")+" door "+door.Id+" closed: outside walker stopped at local z="+depth.ToString("F2")+
      " (door plane 0), outside line of sight="+outsideSight+" (walker "+Vector3.Distance(feet,point.Position).ToString("F2")+" m, against the leaves "+Vector3.Distance(hug,point.Position).ToString("F2")+" m, radius "+point.Radius+"), inside focus="+inner);
    }
    finally{for(int i=0;i<door.Blockers.Length;i++)door.Blockers[i].enabled=prior[i];Physics.SyncTransforms();}
   }
   return lines;
  }

  // doors-pose:<0..1> — review captures only: poses every shortcut door as WorldShortcutDoor would at that open amount
  // (authored closed pose = identity leaves, bar at zero). doors-pose:0 restores the authored pose and saves the scene.
  static string DoorsPose297(float amount)
  {
   int n=0;
   foreach(var d in Object.FindObjectsByType<WorldShortcutDoor>(FindObjectsInactive.Include,FindObjectsSortMode.None))
   {
    float bar=Mathf.SmoothStep(0,1,Mathf.Clamp01(amount/.3f)),swing=Mathf.SmoothStep(0,1,Mathf.Clamp01((amount-.3f)/.7f));
    if(d.Bar!=null)d.Bar.localPosition=(d.LeftLeaf!=null&&d.Bar.parent==d.LeftLeaf?-d.LeftLeaf.localPosition:Vector3.zero)+d.BarOpenOffset*bar;
    if(d.LeftLeaf!=null)d.LeftLeaf.localRotation=Quaternion.Euler(0,-d.OpenDegrees*swing,0);
    if(d.RightLeaf!=null)d.RightLeaf.localRotation=Quaternion.Euler(0,d.OpenDegrees*swing,0);n++;
   }
   if(amount<=0)Save292();
   return "posed "+n+" doors at "+amount.ToString("F2")+(amount<=0?" (authored closed pose, saved)":" (unsaved review pose — run doors-pose:0 after capturing)");
  }

  static string CompoundContentRemove297(string name)
  {
   string file=K297+"/"+name+"/content.json";if(!File.Exists(file))return "content: nothing recorded";
   var rec=JsonUtility.FromJson<KitContent297>(File.ReadAllText(file));var session=Session292();var content=session.Content;
   var ids=new HashSet<string>(rec.points??Array.Empty<string>());var cps=new HashSet<string>(rec.checkpoints??Array.Empty<string>());
   int before=content.Points.Length;
   content.Points=content.Points.Where(p=>p==null||!ids.Contains(p.Id)).ToArray();content.Checkpoints=content.Checkpoints.Where(c=>c==null||!cps.Contains(c.Id)).ToArray();
   session.InteractionVisuals=(session.InteractionVisuals??Array.Empty<WorldMacroPlaytestSession.InteractionVisual>()).Where(v=>v==null||!ids.Contains(v.Id)).ToArray();
   EditorUtility.SetDirty(content);EditorUtility.SetDirty(session);File.Delete(file);
   return "content: removed "+(before-content.Points.Length)+" points, "+cps.Count+" checkpoints";
  }
 }
}
