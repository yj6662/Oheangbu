using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // D308-119 (SPEC-ENEMY-BODIES-308 §2-3) - rigid parts on a body: a weapon in the hand, later armour and organ pieces on the boss.
 // Data: a species row's parts[] {prefab, bone, length, grip01, pos[3], euler[3], note}. bone = a HumanBodyBones name on a Humanoid body
 // (RightHand ...) or a transform name on a Generic one. The prefab copy is scaled so its longest side is `length` m, held at grip01
 // along that side (0 = one end, .5 = the middle), then moved by pos (bone space, metres) and turned by euler. Colliders are stripped:
 // the part is looks only (the attack profile and capsule decide combat). prefab may be a model file (a Meshy part FBX); albedo (optional)
 // then gives it the body's ink look - the Folklore298 shader on an ink-toned copy at the part's saturation / value (0 = the species'), no emission.
 public static partial class Creatures308
 {
  [Serializable] sealed class PartRow{public string prefab="",bone="",note="",albedo="";public float length,grip01=.5f,saturation,value;public float[] pos=Array.Empty<float>(),euler=Array.Empty<float>();}

  static Transform PartBone(Body b,string name)
  {
   if(b.animator!=null&&b.animator.avatar!=null&&b.animator.avatar.isHuman&&Enum.TryParse(name,out HumanBodyBones hb))return b.animator.GetBoneTransform(hb);
   return b.model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);
  }

  // parts:probe:<key> (read only): each part bone's local axes in body space (x right, y up, z forward) at the idle pose and at the
  // attack contact, with the direction from the parent bone - to choose a part's euler without guessing
  static string ProbeParts(Cfg cfg,string key)
  {
   var s=cfg.species.FirstOrDefault(k=>k.key==key);if(s==null)return "REFUSED unknown species "+key;
   var sb=new System.Text.StringBuilder("Creatures308 parts:probe:"+key+"\n");
   using(var w=new Work())
   {
    var b=BuildVisual(cfg,s,w,false,out _);var idle=Clip(s.idle);var attack=Clip(s.attack);var W=b.wrapper.transform;
    MeasureAttack(b,attack,cfg.fit,out float peak,out _,out _);
    string D(Vector3 v){v=W.InverseTransformDirection(v).normalized;return "("+v.x.ToString("0.00")+","+v.y.ToString("0.00")+","+v.z.ToString("0.00")+")";}
    foreach(var p in s.parts??Array.Empty<PartRow>())
    {
     var bone=PartBone(b,p.bone);if(bone==null){sb.Append("  ").Append(p.bone).Append(": not on the body\n");continue;}
     foreach(var (label,clip,t) in new[]{("idle",idle,0f),("contact",attack,attack.length*peak)})
     {
      Pose(b.animator,clip,t);
      sb.Append("  ").Append(bone.name).Append(" ").Append(label).Append(": x").Append(D(bone.right)).Append(" y").Append(D(bone.up)).Append(" z").Append(D(bone.forward))
       .Append(" from parent").Append(bone.parent!=null?D(bone.position-bone.parent.position):"-").Append('\n');
     }
    }
   }
   return sb.ToString();
  }

  static string AttachParts(Cfg cfg,Sp s,Body b,Work w)
  {
   if(s.parts==null||s.parts.Length==0)return "";
   var notes=new System.Text.StringBuilder();Shader shader=null;
   foreach(var p in s.parts)
   {
    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(p.prefab);if(prefab==null)throw new Refuse(s.key+": part prefab missing "+p.prefab);
    var bone=PartBone(b,p.bone);if(bone==null)throw new Refuse(s.key+": part bone '"+p.bone+"' not on the body");
    var copy=(GameObject)Object.Instantiate(prefab);copy.name=prefab.name;
    foreach(var c in copy.GetComponentsInChildren<Component>(true).Where(k=>k is Collider||k is Rigidbody||k is LODGroup).ToArray())Object.DestroyImmediate(c);
    copy.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);copy.transform.localScale=Vector3.one;
    var rs=copy.GetComponentsInChildren<Renderer>(true);if(rs.Length==0){Object.DestroyImmediate(copy);throw new Refuse(s.key+": part "+p.prefab+" has no renderer");}
    Bounds bd=rs[0].bounds;foreach(var r in rs)bd.Encapsulate(r.bounds);
    int axis=bd.size.x>=bd.size.y&&bd.size.x>=bd.size.z?0:bd.size.y>=bd.size.z?1:2;float longest=bd.size[axis];
    float k=p.length>0?p.length/Mathf.Max(.001f,longest):1f,lossy=Mathf.Max(.001f,bone.lossyScale.x);
    var hold=bd.center;hold[axis]=bd.min[axis]+longest*Mathf.Clamp01(p.grip01);
    var root=new GameObject("Part308_"+prefab.name).transform;root.SetParent(bone,false);
    root.localPosition=(p.pos!=null&&p.pos.Length==3?new Vector3(p.pos[0],p.pos[1],p.pos[2]):Vector3.zero)/lossy;
    root.localRotation=p.euler!=null&&p.euler.Length==3?Quaternion.Euler(p.euler[0],p.euler[1],p.euler[2]):Quaternion.identity;root.localScale=Vector3.one;
    copy.transform.SetParent(root,false);copy.transform.localScale=Vector3.one*(k/lossy);copy.transform.localPosition=-hold*(k/lossy);copy.transform.localRotation=Quaternion.identity;
    foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=0;
    if(!string.IsNullOrEmpty(p.albedo))
    {
     if(shader==null){var reference=AssetDatabase.LoadAssetAtPath<Material>(cfg.look.reference);if(reference==null||reference.shader==null)throw new Refuse("look.reference material missing "+cfg.look.reference);shader=reference.shader;}
     var tone=new Sp{key=s.key+"_"+prefab.name,albedo=p.albedo,saturation=p.saturation>0?p.saturation:s.saturation,value=p.value>0?p.value:s.value,tint=s.tint};
     var m=Mat(cfg,cfg.prefabDir+"/Materials/M_"+tone.key+".mat",shader);var tex=InkTexture(cfg,tone,w);
     m.SetTexture("_BaseMap",tex);if(m.HasProperty("_MainTex"))m.SetTexture("_MainTex",tex);m.SetColor("_BaseColor",Color.white);
     if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",cfg.look.smoothness);if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",0);
     if(m.HasProperty("_BumpMap"))m.SetTexture("_BumpMap",null);m.DisableKeyword("_NORMALMAP");
     m.DisableKeyword("_EMISSION");m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;if(m.HasProperty("_EmissionColor"))m.SetColor("_EmissionColor",Color.black);
     EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);
     foreach(var r in root.GetComponentsInChildren<Renderer>(true))r.sharedMaterials=Enumerable.Repeat(m,r.sharedMaterials.Length).ToArray();
    }
    notes.Append("; part ").Append(prefab.name).Append(" on ").Append(bone.name).Append(" ").Append(F(longest*k)).Append(" m");
   }
   return notes.ToString();
  }
 }
}
