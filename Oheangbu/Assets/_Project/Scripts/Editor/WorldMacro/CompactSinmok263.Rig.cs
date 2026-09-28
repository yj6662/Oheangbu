using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.AI;
using Oheangbu.App.World;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Oheangbu.Data.Demo;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static Mesh Tube263(string name,Vector3[] points,float[] radii,int seed,int sides=18,int rings=48)
  {
   var vertices=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
   Vector3 Curve(float u){u=Mathf.Clamp(u,0,points.Length-1);int k=Math.Min(points.Length-2,(int)u);float t=u-k;
    var a=points[Math.Max(0,k-1)];var b=points[k];var c=points[k+1];var d=points[Math.Min(points.Length-1,k+2)];
    return .5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);}
   for(int i=0;i<=rings;i++){
    float u=i/(float)rings*(points.Length-1);int k=Math.Min(points.Length-2,(int)u);float t=u-k;
    Vector3 center=Curve(u),axis=(Curve(u+.01f)-Curve(u-.01f)).normalized;
    Vector3 across=Vector3.Cross(axis,Mathf.Abs(axis.y)>.9f?Vector3.forward:Vector3.up).normalized,other=Vector3.Cross(axis,across);
    for(int j=0;j<=sides;j++){float a=j/(float)sides*Mathf.PI*2;
     float r=Mathf.Lerp(radii[k],radii[k+1],t)*(1+.12f*Mathf.Sin(a*7+seed)+.065f*Mathf.Sin(a*13+u*4+seed));
     r*=1+.055f*Mathf.Sin(u*16+a*3);vertices.Add(center+(across*Mathf.Cos(a)+other*Mathf.Sin(a))*r);uv.Add(new Vector2(j/(float)sides*3,i/(float)rings*8));
     if(i>0&&j>0){int n=i*(sides+1)+j;tris.AddRange(new[]{n-sides-2,n-1,n,n-sides-2,n,n-sides-1});}
    }
   }
   // Close cut ends, keeping a real solid silhouette at detached branch tips.
   for(int j=1;j<sides;j++){tris.AddRange(new[]{0,j+1,j});int b=rings*(sides+1);tris.AddRange(new[]{b,b+j,b+j+1});}
   var mesh=Asset263(name,()=>new Mesh());mesh.Clear();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);return mesh;
  }
  static PrologueEncounter Tree263(Transform parent,Vector3 foot,WorldMacroPlaytestSession s,Material bark,Func<float,float,RaycastHit> ground)
  {
   var original=s.Actors.Single(a=>a.Id==WorldMacroPlaytestSession.CheongryongId);
   var actor=Object.Instantiate(original,parent);actor.name=WorldMacroPlaytestSession.SinmokId;actor.Id=actor.name;actor.transform.SetPositionAndRotation(foot,Quaternion.identity);actor.transform.localScale=Vector3.one;
   foreach(var behaviour in actor.GetComponents<MonoBehaviour>())
    if(!(behaviour is PrologueEncounter||behaviour is EnemyVitals||behaviour is EnemyController||behaviour is CheongryongCombatController||behaviour is CheongryongAttackPresentation))Object.DestroyImmediate(behaviour);
   foreach(Transform child in actor.transform.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
   foreach(var collider in actor.GetComponents<Collider>())Object.DestroyImmediate(collider);
   var oldAnimator=actor.GetComponent<Animator>();if(oldAnimator!=null)Object.DestroyImmediate(oldAnimator);
   var vitals=actor.GetComponent<EnemyVitals>();var serialized=new SerializedObject(vitals);var config=Asset263("SinmokCombatTEST",()=>Object.Instantiate((CombatConfigSO)serialized.FindProperty("_config").objectReferenceValue));
   var cs=new SerializedObject(config);cs.FindProperty("_enemyMaxHp").floatValue=520;cs.ApplyModifiedPropertiesWithoutUndo();serialized.FindProperty("_config").objectReferenceValue=config;serialized.ApplyModifiedPropertiesWithoutUndo();
   var collision=actor.gameObject.AddComponent<CapsuleCollider>();collision.radius=1.85f;collision.height=7;collision.center=Vector3.up*3.5f;
   var agent=actor.GetComponent<NavMeshAgent>();agent.enabled=false;agent.radius=2;agent.height=7;agent.baseOffset=0;agent.speed=0;agent.updateRotation=false;
   actor.Player=s.Walker.Body.transform;actor.Session=null;actor.Speed=0;actor.DetectionRange=23;actor.Leash=35;actor.PreferredDistance=0;actor.PatrolPoints=new[]{foot};
   var profile=Asset263("SinmokAttacksTEST",()=>ScriptableObject.CreateInstance<CheongryongCombatProfile>());
   profile.EngageRange=25;profile.LeashRange=35;profile.BiteRange=7;profile.BiteWindup=1.3f;profile.BiteDamage=18;profile.BiteHalfAngle=40;profile.BiteRecovery=1.5f;
   profile.TailRange=10;profile.TailWindup=1.7f;profile.TailDamage=22;profile.TailRecovery=1.6f;profile.TailHalfAngle=155;
   profile.RootRadius=2.4f;profile.RootWindup=1.8f;profile.RootDamage=18;profile.RootRecovery=1.2f;
   profile.ProjectileRange=25;profile.ProjectileWindup=1.5f;profile.ProjectileSpeed=9;profile.ProjectileDamage=16;profile.CooldownSeconds=1.2f;profile.VerticalTolerance=6;
   var combat=actor.GetComponent<CheongryongCombatController>();combat.ConfigureProfile(profile,config);
   var bones=new List<Transform>();
   Transform Bone(string name,Transform p,Vector3 point){var t=new GameObject(name).transform;t.SetParent(p,false);t.position=foot+point;bones.Add(t);return t;}
   var rig=Bone("Root",actor.transform,Vector3.zero);var lower=Bone("TrunkLower",rig,new Vector3(0,2.5f,0));var upper=Bone("TrunkUpper",lower,new Vector3(.6f,6.5f,0));var crown=Bone("Crown",upper,new Vector3(-.3f,10,0));
   var parts=new List<(string name,Vector3[] path,float[] radii,Transform[] bones)>();
   parts.Add(("Trunk",new[]{Vector3.zero,new Vector3(0,2.5f,0),new Vector3(.6f,6.5f,0),new Vector3(-.3f,10,0),new Vector3(.8f,13,.6f)},new[]{2.35f,1.75f,1.45f,.95f,.15f},new[]{rig,lower,upper,crown,crown}));
   for(int k=0;k<7;k++){
    float a=k*2.39996f;var d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));float len=6.2f+k*.4f;
    var p=new[]{new Vector3(0,.6f,0),d*2.5f+Vector3.up*.55f,d*len+Vector3.up*.12f,d*(len+1.4f)-Vector3.up*.10f};
    for(int n=1;n<p.Length;n++)p[n].y+=ground(foot.x+p[n].x,foot.z+p[n].z).point.y-foot.y;
    var b1=Bone("RootSpread"+k,rig,p[1]);var b2=Bone("RootTip"+k,b1,p[2]);parts.Add(("RootPart"+k,p,new[]{.85f,.64f,.24f,.02f},new[]{rig,b1,b2,b2}));
   }
   for(int k=0;k<9;k++){
    float a=k*2.39996f+.4f;Vector3 start=new Vector3(.1f,6.7f+k*.48f,0),d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
    var p=new[]{start,start+d*2+Vector3.up*1.3f,start+d*(5+k%3)+Vector3.up*(1.5f+k%2),start+d*(7+k%3)+Vector3.up*(2.6f+k%3)};
    if(k==0)p=new[]{start,new Vector3(1,5,2),new Vector3(2,3,5),new Vector3(2.5f,2.2f,7)};
    if(k==1)p=new[]{start,new Vector3(-2,4.8f,1),new Vector3(-6,2.8f,2),new Vector3(-9,2.2f,1)};
    var b1=Bone("BranchBase"+k,upper,p[0]);var b2=Bone("BranchElbow"+k,b1,p[1]);var b3=Bone("BranchTip"+k,b2,p[2]);
    parts.Add(("BranchPart"+k,p,new[]{.65f,.48f,.24f,.015f},new[]{b1,b2,b3,b3}));
    for(int q=0;q<3;q++){var fork=p[2]+d*q*.4f;var end=p[3]+new Vector3(Mathf.Cos(a+q)*2,1+q*.5f,Mathf.Sin(a+q)*2);
     parts.Add(("Twig"+k+"_"+q,new[]{fork,Vector3.Lerp(fork,end,.55f)+Vector3.up*.5f,end},new[]{.19f,.1f,.006f},new[]{b3,b3,b3}));}
   }
   int partIndex=0;
   foreach(var part in parts){int ringCount=part.name.StartsWith("Twig")?18:48;var mesh=Tube263("Tree_"+part.name,part.path,part.radii,partIndex++,18,ringCount);
    var weights=new BoneWeight[mesh.vertexCount];for(int i=0;i<=ringCount;i++){
     float u=i/(float)ringCount*(part.bones.Length-1);int k=Math.Min(part.bones.Length-2,(int)u);float t=u-k;
     for(int j=0;j<=18;j++)weights[i*19+j]=new BoneWeight{boneIndex0=bones.IndexOf(part.bones[k]),weight0=1-t,boneIndex1=bones.IndexOf(part.bones[k+1]),weight1=t};}
    mesh.boneWeights=weights;mesh.bindposes=bones.Select(b=>b.worldToLocalMatrix*actor.transform.localToWorldMatrix).ToArray();EditorUtility.SetDirty(mesh);
    var obj=new GameObject(part.name);obj.transform.SetParent(actor.transform,false);var skin=obj.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.sharedMaterial=bark;skin.bones=bones.ToArray();skin.rootBone=rig;skin.localBounds=new Bounds(Vector3.up*7,new Vector3(30,22,30));skin.updateWhenOffscreen=true;skin.forceMatrixRecalculationPerRender=true;
   }
   var mouth=new GameObject("MouthOrigin").transform;mouth.SetParent(rig,false);mouth.localPosition=new Vector3(0,1.1f,1.7f);
   var sweep=new GameObject("Body_12").transform;sweep.SetParent(rig,false);sweep.localPosition=new Vector3(0,.7f,0);combat.ConfigureSockets(mouth,sweep);
   var animator=actor.gameObject.AddComponent<Animator>();animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
   var avatar=Asset263("SinmokGenericAvatar",()=>AvatarBuilder.BuildGenericAvatar(actor.gameObject,"Root"));animator.avatar=avatar;
   AnimationClip Clip(string name,int attack){var c=Asset263("Tree_"+name,()=>new AnimationClip());c.ClearCurves();c.frameRate=30;
    foreach(var bone in bones){Vector3 anticipation=Vector3.zero,strike=Vector3.zero;
     if(attack<0){if(bone==upper)anticipation=new Vector3(0,0,1.2f);if(bone==crown)anticipation=new Vector3(.8f,0,-1.5f);strike=-anticipation;}
     else{if(bone==upper){anticipation=new Vector3(-2,attack==1?-12:0,0);strike=new Vector3(3,attack==1?18:0,0);}
      if(bone.name=="BranchBase0"&&attack==0){anticipation=new Vector3(-34,0,-8);strike=new Vector3(12,0,4);}
      if(bone.name=="BranchBase1"&&attack==1){anticipation=new Vector3(-7,-55,-8);strike=new Vector3(10,65,0);}
      if(bone.name.StartsWith("RootSpread")&&attack==2){anticipation=new Vector3(0,0,-8);strike=new Vector3(0,0,14);}
      if(bone==crown&&attack==3){anticipation=new Vector3(-13,0,0);strike=new Vector3(12,0,0);}}
     if(anticipation==Vector3.zero&&strike==Vector3.zero)continue;var qs=new[]{Quaternion.identity,Quaternion.Euler(anticipation),Quaternion.Euler(strike),Quaternion.identity};float[] ts=attack<0?new[]{0f,1.5f,4.5f,6f}:new[]{0f,.5f,.9f,1f};
     string path=AnimationUtility.CalculateTransformPath(bone,actor.transform);for(int channel=0;channel<4;channel++){var curve=new AnimationCurve();for(int n=0;n<4;n++)curve.AddKey(ts[n],qs[n][channel]);AnimationUtility.SetEditorCurve(c,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+"xyzw"[channel]),curve);}}
    c.EnsureQuaternionContinuity();EditorUtility.SetDirty(c);return c;}
   var animation=actor.gameObject.AddComponent<SinmokRigAnimation>();animation.Animator=animator;animation.Combat=combat;animation.TurningTrunk=upper;animation.Idle=Clip("Idle",-1);animation.Attacks=Enumerable.Range(0,4).Select(k=>Clip(new[]{"BranchSlam","BranchSweep","RootRise","SeedThrow"}[k],k)).ToArray();
   var corpse=new GameObject("SinmokRemnant263");corpse.transform.SetParent(parent,false);corpse.transform.position=foot;
   var stump=Tube263("FallenStump",new[]{Vector3.zero,new Vector3(0,2,0),new Vector3(.4f,4.7f,1)},new[]{2.3f,1.7f,.95f},17);
   corpse.AddComponent<MeshFilter>().sharedMesh=stump;corpse.AddComponent<MeshRenderer>().sharedMaterial=bark;corpse.AddComponent<MeshCollider>().sharedMesh=stump;corpse.SetActive(false);
   var landmark=parent.gameObject.AddComponent<SinmokLandmark>();landmark.Vitals=vitals;landmark.Remnant=corpse;
   foreach(var o in new Object[]{profile,config,s.Walker.Wiring})EditorUtility.SetDirty(o);
   return actor;
  }
 }
}
