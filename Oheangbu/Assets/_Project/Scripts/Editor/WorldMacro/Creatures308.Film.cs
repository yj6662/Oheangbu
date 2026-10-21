using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Oheangbu.App.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // D308-119 (SPEC-ENEMY-BODIES-308 AC B2: motion is judged as video) - film:<key> poses one built body through its roles in a preview
 // scene and writes 30 fps frames <stillDir>/film_<key>/f_0000.png ...: idle 1.5 s, walk and run two cycles each, attack, hit, stun up to 2 s,
 // death and a 0.5 s hold. Side view at the stills' light; the 1.8 m capsule stands behind. Read only (no scene, no asset written).
 // The mp4 is encoded outside the editor (ffmpeg).
 public static partial class Creatures308
 {
  static string Film(Cfg cfg,string key)
  {
   var s=cfg.species.FirstOrDefault(k=>k.key==key);if(s==null)return "REFUSED unknown species "+key;
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(cfg,s.key));if(prefab==null)return "REFUSED no prefab (bodies:build:"+key+" first)";
   const int fps=30;var st=cfg.still;string dir=Path.Combine(Harness303.RepoRoot,cfg.stillDir,"film_"+key);
   if(Directory.Exists(dir))foreach(var f in Directory.GetFiles(dir,"f_*.png"))File.Delete(f);
   Directory.CreateDirectory(dir);int n=0;var sb=new StringBuilder("Creatures308 film:"+key+"\n");
   using(var w=new Work())
   {
    var agent=prefab.GetComponent<NavMeshAgent>();var go=w.Adopt(Object.Instantiate(prefab));go.transform.SetPositionAndRotation(new Vector3(0,agent!=null?agent.baseOffset:0,0),Quaternion.Euler(0,90,0));
    // every frame is rendered inside one editor frame: without this a skin keeps the bone matrices of its first render (boar film: bones turned 75 deg, image unchanged)
    foreach(var k in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))k.forceMatrixRecalculationPerRender=true;
    var rig=go.GetComponent<EnemyRigMotion298>();var m=ReadMeasured().rows.FirstOrDefault(r=>r.key==s.key);float L=m!=null&&m.length>0?m.length:2f,H=m!=null&&m.height>0?m.height:1f;
    var seq=new (string role,AnimationClip clip,float seconds,bool loop)[]
    {
     ("idle",rig.Idle,1.5f,true),("walk",rig.Walk,rig.Walk!=null?rig.Walk.length*2:0,true),("run",rig.Run,rig.Run!=null?rig.Run.length*2:0,true),("attack",rig.Attack,rig.Attack!=null?rig.Attack.length:0,false),
     ("hit",rig.Hit,rig.Hit!=null?rig.Hit.length:0,false),("stun",rig.Stun,rig.Stun!=null?Mathf.Min(2f,rig.Stun.length):0,true),("death",rig.Death,rig.Death!=null?rig.Death.length+.5f:0,false)
    };
    var shader=go.GetComponentsInChildren<SkinnedMeshRenderer>(true).First().sharedMaterial.shader;
    var ground=w.Adopt(GameObject.CreatePrimitive(PrimitiveType.Plane));ground.transform.localScale=Vector3.one*10;
    var gm=w.Temp(new Material(shader));gm.SetColor("_BaseColor",new Color(.78f,.75f,.69f));ground.GetComponent<Renderer>().sharedMaterial=gm;
    var person=w.Adopt(GameObject.CreatePrimitive(PrimitiveType.Capsule));person.transform.position=new Vector3(-L*.5f-.9f,st.reference_m*.5f,1.2f);person.transform.localScale=new Vector3(.45f,st.reference_m*.5f,.45f);
    var pm=w.Temp(new Material(shader));pm.SetColor("_BaseColor",new Color(.75f,.2f,.15f));person.GetComponent<Renderer>().sharedMaterial=pm;
    var lightGo=w.Empty("Light");var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.shadows=LightShadows.Soft;lightGo.transform.rotation=Quaternion.Euler(42,25,0);
    var camGo=w.Empty("Camera");var cam=camGo.AddComponent<Camera>();cam.scene=w.Scene;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.9f,.88f,.83f);cam.fieldOfView=st.fov;cam.nearClipPlane=.1f;cam.farClipPlane=400;cam.enabled=false;
    float span=Mathf.Max(L+1.6f,Mathf.Max(H,st.reference_m)*1.9f);var target=new Vector3(0,Mathf.Max(H,st.reference_m)*.45f,0);
    float hfov=2*Mathf.Atan(Mathf.Tan(st.fov*Mathf.Deg2Rad*.5f)*960f/540f);float dist=span*.5f/Mathf.Tan(hfov*.5f)*1.15f;
    camGo.transform.position=target+new Vector3(dist*.72f,dist*.18f,-dist*.7f);camGo.transform.LookAt(target);   // front three-quarter (the body faces +x)
    string warm=Path.Combine(dir,"warmup.png");Capture(cam,960,540,warm,w);File.Delete(warm);   // the first render of a preview scene can come out blank (stills)
    foreach(var (role,clip,seconds,loop) in seq)
    {
     if(clip==null||seconds<=0){sb.Append("  ").Append(role).Append(": -\n");continue;}
     int frames=Mathf.CeilToInt(seconds*fps);sb.Append("  ").Append(role).Append(": ").Append(clip.name).Append(" ").Append(frames).Append(" frames from ").Append(n);
     // motion check: the largest skin-bone turn between the clip's start and middle (0 = the clip does not move this body)
     var skin=go.GetComponentsInChildren<SkinnedMeshRenderer>(true).First();Pose(rig.Animator,clip,0);var start=skin.bones.Select(k=>k!=null?k.localRotation:Quaternion.identity).ToArray();
     Pose(rig.Animator,clip,clip.length*.5f);float turn=0;for(int i=0;i<start.Length;i++)if(skin.bones[i]!=null)turn=Mathf.Max(turn,Quaternion.Angle(start[i],skin.bones[i].localRotation));
     sb.Append(" | bones turn ").Append(F(turn,"0.#")).Append(" deg\n");
     for(int i=0;i<frames;i++){float t=i/(float)fps;Pose(rig.Animator,clip,loop?t%clip.length:Mathf.Min(t,clip.length));Capture(cam,960,540,Path.Combine(dir,"f_"+n.ToString("0000")+".png"),w);n++;}
    }
   }
   return sb.Append("  ").Append(n).Append(" frames at ").Append(fps).Append(" fps -> ").Append(dir).ToString();
  }

  // face:<key> (D308-120: grotesque faces are judged up close) - the head at the idle start from the front, the front left and the
  // left side, 960x540 each: <stillDir>/face_<key>_{front,frontleft,left}.png. Read only, like film.
  static string Face(Cfg cfg,string key)
  {
   var s=cfg.species.FirstOrDefault(k=>k.key==key);if(s==null)return "REFUSED unknown species "+key;
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(cfg,s.key));if(prefab==null)return "REFUSED no prefab (bodies:build:"+key+" first)";
   var st=cfg.still;string dir=Path.Combine(Harness303.RepoRoot,cfg.stillDir);var sb=new StringBuilder("Creatures308 face:"+key+"\n");
   using(var w=new Work())
   {
    var agent=prefab.GetComponent<NavMeshAgent>();var go=w.Adopt(Object.Instantiate(prefab));go.transform.SetPositionAndRotation(new Vector3(0,agent!=null?agent.baseOffset:0,0),Quaternion.Euler(0,90,0));
    foreach(var k in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))k.forceMatrixRecalculationPerRender=true;
    var rig=go.GetComponent<EnemyRigMotion298>();Pose(rig.Animator,rig.Idle,0);
    var an=rig.Animator;Transform head=an!=null&&an.avatar!=null&&an.avatar.isHuman?an.GetBoneTransform(HumanBodyBones.Head):null;
    if(head==null)head=go.GetComponentsInChildren<Transform>(true).Where(t=>t.name.IndexOf("head",System.StringComparison.OrdinalIgnoreCase)>=0&&t.name.IndexOf("end",System.StringComparison.OrdinalIgnoreCase)<0).OrderBy(t=>t.name.Length).FirstOrDefault();
    if(head==null)return "REFUSED no head bone on "+key;
    var m=ReadMeasured().rows.FirstOrDefault(r=>r.key==s.key);float H=m!=null&&m.height>0?m.height:1f,span=Mathf.Max(.6f,H*.5f);
    var target=head.position+go.transform.forward*span*.25f;
    var shader=go.GetComponentsInChildren<SkinnedMeshRenderer>(true).First().sharedMaterial.shader;
    var ground=w.Adopt(GameObject.CreatePrimitive(PrimitiveType.Plane));ground.transform.localScale=Vector3.one*10;
    var gm=w.Temp(new Material(shader));gm.SetColor("_BaseColor",new Color(.78f,.75f,.69f));ground.GetComponent<Renderer>().sharedMaterial=gm;
    var lightGo=w.Empty("Light");var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.shadows=LightShadows.Soft;lightGo.transform.rotation=Quaternion.Euler(42,25,0);
    var camGo=w.Empty("Camera");var cam=camGo.AddComponent<Camera>();cam.scene=w.Scene;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.9f,.88f,.83f);cam.fieldOfView=st.fov;cam.nearClipPlane=.05f;cam.farClipPlane=100;cam.enabled=false;
    float dist=span*.5f/Mathf.Tan(st.fov*Mathf.Deg2Rad*.5f)*1.1f;Vector3 fwd=go.transform.forward,left=-go.transform.right;
    string warm=Path.Combine(dir,"face_warmup.png");camGo.transform.position=target+fwd*dist;camGo.transform.LookAt(target);Capture(cam,960,540,warm,w);File.Delete(warm);
    foreach(var (name,d) in new[]{("front",fwd),("frontleft",(fwd+left).normalized),("left",left)})
    {
     camGo.transform.position=target+(d+Vector3.up*.15f).normalized*dist;camGo.transform.LookAt(target);
     sb.Append("  ").Append(name).Append(": ").Append(Capture(cam,960,540,Path.Combine(dir,"face_"+key+"_"+name+".png"),w)).Append('\n');
    }
    sb.Append("  head bone ").Append(head.name).Append(", frame ").Append(F(span)).Append(" m");
   }
   return sb.ToString();
  }
 }
}
