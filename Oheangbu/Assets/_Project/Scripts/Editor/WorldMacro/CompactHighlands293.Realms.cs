using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  // #293 Step 2 realm character for the highland sections (LDB realm table, #290 realm contract, ART-SKY seeds).
  // All values are TEST. Cheongrim reproduces the Step 1 materials exactly (285 granite, rocky_trail soil, 285 worn steps).
  // CC0 textures: Highlands293/sources.json (Poly Haven, user-approved 2026-09-27).
  sealed class RealmLook293
  {
   public string RockSlug,SoilSlug="rocky_trail",StoneSlug,Meshy;
   public float RockScale,SoilScale=1/2.4f,StoneScale,RockChroma=.36f,SoilChroma=.55f,StoneChroma=.34f,RockMoss=.5f,StoneMoss=.7f,MeshyHeight=6;
   public Color Rock=new Color(.81f,.81f,.77f),Soil=new Color(.92f,.90f,.86f),Stone=new Color(.90f,.90f,.86f),Timber=new Color(.90f,.86f,.80f);
   // scan value target (linear luminance of the texture's average, 0 = off) and local contrast — Cheongrim off
   public Vector2 RockValue=new Vector2(0,1),SoilValue=new Vector2(0,1),StoneValue=new Vector2(0,1);
   public string[] Outcrops={"mountainside","rock_face_01","rock_face_02"};
   public bool Iron,Charred,Tors,Wall;
  }
  static RealmLook293 LookFor293(string realm)
  {
   switch(realm)
   {
    // 불탄 전장: weathered rust-red blocks, red laterite gravel, broken set stones, charred timber and stone cairns
    case "Jeokro":return new RealmLook293{RockSlug="rock_boulder_cracked",RockScale=1/3.5f,RockChroma=.26f,RockMoss=.08f,Rock=new Color(.98f,.93f,.89f),RockValue=new Vector2(.25f,1.3f),
     SoilSlug="red_laterite_soil_stones",SoilScale=1/2.2f,SoilChroma=.26f,Soil=new Color(.98f,.94f,.92f),SoilValue=new Vector2(.15f,1.4f),
     StoneSlug="rock_boulder_cracked",StoneScale=1/1.3f,StoneChroma=.24f,StoneMoss=.06f,Stone=new Color(.98f,.94f,.90f),StoneValue=new Vector2(.26f,1.2f),
     Outcrops=new[]{"namaqualand_cliff_02","namaqualand_boulder_02","namaqualand_boulder_04"},Meshy="JeokroScorchedOutcrop",MeshyHeight=5.5f,
     Charred=true,Timber=new Color(.30f,.26f,.23f)};
    // 국경 관문 도시: grey-blue bedded rock, layered scree trail, flat slabs, fortress wall remnants, iron-strapped walkway
    case "Cheolong":return new RealmLook293{RockSlug="dark_rock_02",RockScale=1/4f,RockChroma=.24f,RockMoss=.22f,Rock=new Color(.92f,.95f,1f),RockValue=new Vector2(.17f,1.9f),
     SoilSlug="rocks_ground_06",SoilScale=1/2.5f,SoilChroma=.40f,Soil=new Color(.88f,.90f,.92f),
     StoneSlug="dark_rock_02",StoneScale=1/1.5f,StoneChroma=.24f,StoneMoss=.25f,Stone=new Color(.94f,.96f,1f),StoneValue=new Vector2(.19f,1.6f),
     Outcrops=new[]{"rock_face_01","rock_face_02","mountainside"},Meshy="CheolongBeddedSlab",MeshyHeight=6f,
     Iron=true,Wall=true,Timber=new Color(.62f,.58f,.52f)};
    // 가라앉는 옛 서울: dark wet mossy rock, riverbank mud trail, mossy stones, weathered wet timber
    case "Hyeongang":return new RealmLook293{RockSlug="rock_3",RockScale=1/3.5f,RockChroma=.32f,RockMoss=.95f,Rock=new Color(.66f,.70f,.70f),RockValue=new Vector2(.21f,1.2f),
     SoilSlug="brown_mud_03",SoilScale=1/2.4f,SoilChroma=.45f,Soil=new Color(.78f,.80f,.78f),
     StoneSlug="rock_3",StoneScale=1/1.4f,StoneChroma=.30f,StoneMoss=1f,Stone=new Color(.72f,.76f,.74f),
     Meshy="HyeongangWetOverhang",MeshyHeight=6f,Timber=new Color(.58f,.60f,.56f)};
    // 인왕산: pale granite domes and tors, decomposed-granite path, an old dressed-stone road with lighter repair stones
    case "Hwanggyeong":return new RealmLook293{RockSlug="tiger_rock",RockScale=1/5f,RockChroma=.16f,RockMoss=.18f,Rock=new Color(1f,.99f,.97f),RockValue=new Vector2(.23f,1.2f),
     SoilSlug="rocky_trail",SoilScale=1/2.4f,SoilChroma=.50f,Soil=new Color(.95f,.93f,.88f),
     StoneSlug="stone_pathway",StoneScale=1f,StoneChroma=.30f,StoneMoss=.15f,Stone=new Color(.90f,.88f,.84f),StoneValue=new Vector2(.19f,1.1f),
     Outcrops=new[]{"namaqualand_boulder_04","namaqualand_boulder_02"},Meshy="HwanggyeongGraniteTor",MeshyHeight=8.5f,Tors=true};
    default:return new RealmLook293();
   }
  }

  // the realm's section materials (candidate copies; asset keys per realm, so re-applying updates them in place)
  static (Material rock,Material soil,Material stone,Material repair,Material wall,Material timber) RealmMaterials293(string realm,Color tint,RealmLook293 look)
  {
   var rock=SectionMaterial293("Granite.mat",realm+"_Granite",tint,look.Rock,look.RockSlug,look.RockScale,look.RockChroma,look.RockMoss,look.RockValue);
   var soil=SectionMaterial293("Rooted_soil.mat",realm+"_Soil",tint,look.Soil,look.SoilSlug,look.SoilScale,look.SoilChroma,0,look.SoilValue);
   var stone=SectionMaterial293("Worn_steps.mat",realm+"_Stone",tint,look.Stone,look.StoneSlug,look.StoneScale,look.StoneChroma,look.StoneMoss,look.StoneValue);
   // Hwanggyeong old stone road: lighter, cleaner stones where the road was repaired; Cheolong wall blocks: dressed granite
   var repair=look.StoneSlug=="stone_pathway"?SectionMaterial293("Worn_steps.mat",realm+"_Repair",tint,new Color(1f,.98f,.95f),look.StoneSlug,look.StoneScale*1.3f,.22f,0):stone;
   var wall=look.Wall?SectionMaterial293("Worn_steps.mat",realm+"_Wall",tint,new Color(.86f,.86f,.84f),"tiger_rock",1/1.2f,.26f,.3f):stone;
   var timber=look.Charred?TimberMaterial293("poles",realm+"_Charred",tint,new Color(.20f,.17f,.15f)):null;
   return (rock,soil,stone,repair,wall,timber);
  }
  // quick look iteration: re-apply the realm materials without rebuilding any geometry
  static string Materials293()
  {
   var layout=Session292().MountainLayout;HighlandTextures293();var done=new List<string>();
   foreach(string realm in SectionIds293().Select(id=>SectionMeta293(id).realm).Distinct())
   {var area=layout.Realms.First(r=>r.Id.Equals(realm,StringComparison.OrdinalIgnoreCase));RealmMaterials293(realm,area.Tint,LookFor293(realm));done.Add(realm);}
   var ground=AssetDatabase.LoadAssetAtPath<Material>(A292+"/Materials/KoreanGround.mat");if(ground!=null)RealmFloors293(ground);
   AssetDatabase.SaveAssets();return "Realm section materials and ground realm floors re-applied in place: "+string.Join(", ",done);
  }

  // stone sets from Tools/Blender/build_highland293.py (counts = highland_geo293.STONE_SETS)
  static readonly Dictionary<string,Mesh[,]> stoneSets293=new Dictionary<string,Mesh[,]>();
  static Mesh[,] StoneSet293(string set)
  {
   if(stoneSets293.TryGetValue(set,out var lods)&&lods[0,0]!=null)return lods;
   int count=set=="natural"?16:set=="cut"?10:12;string prefix=set=="natural"?"Stone":set;lods=new Mesh[count,2];
   for(int k=0;k<count;k++)for(int l=0;l<2;l++)lods[k,l]=Json293(V293+"/Stones/"+prefix+"_"+k+"_LOD"+l+".json",S293+"/Stones/"+prefix+"_"+k+"_LOD"+l+".asset");
   return stoneSets293[set]=lods;
  }
  static readonly Dictionary<string,Mesh[]> outcrops293=new Dictionary<string,Mesh[]>();
  static Mesh[] Outcrop293(string slug)
  {
   if(outcrops293.TryGetValue(slug,out var lods)&&lods[0]!=null)return lods;
   return outcrops293[slug]=Enumerable.Range(0,3).Select(l=>Json293(V293+"/Outcrops/"+slug+"_LOD"+l+".json",S293+"/Outcrops/"+slug+"_LOD"+l+".asset")).ToArray();
  }
  static Material IronMaterial293()
  {
   var iron=Asset292("Highlands293/Sections/Materials/Iron_straps.mat",()=>new Material(Shader.Find("Universal Render Pipeline/Lit")));
   iron.SetColor("_BaseColor",new Color(.16f,.16f,.155f));iron.SetFloat("_Smoothness",.14f);iron.SetFloat("_Metallic",.55f);iron.enableInstancing=true;EditorUtility.SetDirty(iron);return iron;
  }

  // Walkway iron straps and brackets (Cheolong): flat bars across every third plank and a bracket at each rail post foot
  static void IronStraps293(Transform bridge,Material iron)
  {
   var planks=bridge.Cast<Transform>().Where(t=>t.name=="Timber_tread").ToArray();
   for(int i=0;i<planks.Length;i+=3)
   {
    var p=planks[i];var right=p.right;float half=p.localScale.x*.5f;
    foreach(float side in new[]{-.78f,.78f})
    {
     var go=Box290("Iron_strap",bridge,p.position+right*half*side+p.up*(p.localScale.y*.5f+.006f),new Vector3(.05f,.012f,p.localScale.z+.02f),iron,false);
     go.transform.rotation=p.rotation;
    }
   }
   foreach(var post in bridge.Cast<Transform>().Where(t=>t.name=="Rail_post").ToArray())
   {var go=Box290("Iron_bracket",bridge,post.position-post.up*post.localScale.y*.28f,new Vector3(.19f,.16f,.19f),iron,false);go.transform.rotation=post.rotation;}
  }

  // Realm traces around a section (visual set dressing; walking is carried by the section colliders)
  static string RealmProps293(Transform sec,MountainTrailProfile profile,Meta293 meta,RealmLook293 look,Material stone,Material wallStone,Material timber,System.Random rng)
  {
   float E(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());string A(string n)=>S293+"/"+meta.id+"/"+n+".asset";
   bool Ground(Vector3 from,float depth,out RaycastHit hit){var all=Physics.RaycastAll(from,Vector3.down,depth).Where(h=>h.collider.transform.IsChildOf(sec)||h.collider.transform.root.name=="Reworld292_Terrain").OrderBy(h=>h.distance).ToArray();hit=all.FirstOrDefault();return all.Length>0;}
   var notes=new List<string>();
   bool walkway=profile.bridgeEnd>profile.bridgeStart+.5f;bool OnWalkway(float s)=>walkway&&s>profile.bridgeStart-1.5f&&s<profile.bridgeEnd+1.5f;
   if(look.Charred)
   {
    // old palisade remnants on the valley edge and fallen logs on the apron (the 3m CC0 log, charred)
    var log=Enumerable.Range(0,2).Select(l=>Json293(V293+"/Props/dead_tree_trunk_LOD"+l+".json",S293+"/Props/dead_tree_trunk_LOD"+l+".asset")).ToArray();
    float half=log[0].bounds.extents.x;int stakes=0,fallen=0;
    for(float s=5;s<profile.length-5;s+=E(2.0f,4.2f))
    {
     if(OnWalkway(s)||rng.NextDouble()<.35)continue;var valley=-profile.Right(s)*meta.upSign;var p=profile.At(s)+valley*(profile.Width(s)*.5f+E(.9f,1.7f));
     if(!Ground(p+Vector3.up*3,8,out var hit))continue;float length=E(.9f,2.1f),sx=length/(2*half);
     var go=Lod293("Charred_stake",sec,log,timber,new[]{.05f,.006f},false);go.transform.rotation=Quaternion.Euler(E(-12,12),E(0,360),90+E(-10,10));
     go.transform.localScale=new Vector3(sx,E(.55f,.95f),E(.55f,.95f));go.transform.position=hit.point+Vector3.up*(half*sx-E(.25f,.45f));
     var cap=go.AddComponent<CapsuleCollider>();cap.direction=0;cap.radius=.16f;cap.height=2*half;stakes++;
    }
    for(int k=0;k<3;k++)
    {
     float s=E(6,profile.length-6);if(OnWalkway(s))continue;var valley=-profile.Right(s)*meta.upSign;var p=profile.At(s)+valley*(profile.Width(s)*.5f+E(2.5f,6f));
     if(!Ground(p+Vector3.up*5,12,out var hit))continue;var go=Lod293("Fallen_timber",sec,log,timber,new[]{.04f,.005f},false);
     go.transform.rotation=Quaternion.Euler(E(-5,5),E(0,360),E(-6,6));go.transform.localScale=Vector3.one*E(.5f,.8f);go.transform.position=hit.point-Vector3.up*.08f;fallen++;
    }
    // 전몰자 돌무덤: a stacked cairn of broken stones where the walker pauses (viewpoint shelf / the top of a flight)
    var set=StoneSet293("broken");int cairns=0;
    foreach(float s in meta.kind=="C"?new[]{profile.length-3.2f}:meta.kind=="A"?new[]{profile.length*.3f}:new float[0])
    {
     var valley=-profile.Right(s)*meta.upSign;var at=profile.At(s)+valley*(profile.Width(s)*.5f+1.25f);if(!Ground(at+Vector3.up*3,8,out var hit))continue;
     var pieces=new List<(Matrix4x4 m,int v)>();int[] ring={7,5,3,1};float[] radius={.72f,.46f,.22f,0},lift={.30f,.60f,.86f,1.06f};
     for(int r=0;r<ring.Length;r++)for(int k=0;k<ring[r];k++)
     {
      float a=(k+E(-.2f,.2f))/ring[r]*Mathf.PI*2;var c=hit.point+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius[r]*E(.85f,1.1f)+Vector3.up*lift[r];float size=E(.32f,.48f)*(1-.16f*r);
      pieces.Add((Matrix4x4.TRS(c,Quaternion.Euler(E(-18,18),E(0,360),E(-18,18)),new Vector3(size,size*E(.7f,1f),size*E(.8f,1.1f))),rng.Next(set.GetLength(0))));
     }
     var lods=new Mesh[2];
     for(int l=0;l<2;l++){var m=new Mesh{indexFormat=IndexFormat.UInt32};m.CombineMeshes(pieces.Select(x=>new CombineInstance{mesh=set[x.v,l],transform=x.m}).ToArray(),true,true);m.RecalculateBounds();lods[l]=ArtMesh(m,A("Cairn_"+cairns+"_LOD"+l));}
     var go=Lod293("Stone_cairn",sec,lods,stone,new[]{.05f,.006f},false);var cc=go.AddComponent<CapsuleCollider>();cc.center=hit.point+Vector3.up*.6f;cc.radius=.85f;cc.height=1.4f;cairns++;
    }
    notes.Add("charred stakes="+stakes+" fallen="+fallen+" cairns="+cairns);
   }
   if(look.Wall)
   {
    // 성곽 잔해: dry-stacked dressed granite following the crest behind the band, courses in running bond, gaps and fallen blocks
    var set=StoneSet293("cut");var crest=new List<(float s,Vector3 p)>();
    for(float s=3;s<profile.length-3;s+=1)
    {
     var up=profile.Right(s)*meta.upSign;var at=profile.At(s);Vector3? best=null;
     for(float u=3;u<18;u+=.75f){var from=at+up*u;var hit=Physics.RaycastAll(new Vector3(from.x,at.y+60,from.z),Vector3.down,90).Where(h=>h.collider.name=="Band").OrderBy(h=>h.distance).FirstOrDefault();if(hit.collider!=null&&(!best.HasValue||hit.point.y>best.Value.y))best=hit.point;}
     if(best.HasValue&&best.Value.y>at.y+2.5f)crest.Add((s,best.Value+up*E(1.3f,1.7f)));
    }
    // the longest contiguous crest run, up to 24m
    var runs=new List<List<(float s,Vector3 p)>>();foreach(var c in crest){if(runs.Count==0||c.s-runs[runs.Count-1].Last().s>1.5f)runs.Add(new List<(float,Vector3)>());runs[runs.Count-1].Add(c);}
    var run=runs.OrderByDescending(r=>r.Count).FirstOrDefault();int blocks=0;
    if(run!=null&&run.Count>=10)
    {
     run=run.Take(24).ToList();var combine=new List<(CombineInstance ci,int v)>();float gapA=E(.3f,.6f)*run.Count,gapB=gapA+E(1.5f,3f);
     for(int i=0;i<run.Count-1;i++)
     {
      var a=run[i].p;var b=run[i+1].p;var dir=(b-a);dir.y=0;if(dir.sqrMagnitude<1e-4f)continue;dir.Normalize();var yaw=Quaternion.LookRotation(dir,Vector3.up);  // block z = along the wall
      if(!Ground(a+Vector3.up*4,10,out var ga))continue;float wallH=i>=gapA&&i<gapB?E(.2f,.5f):E(1.1f,1.9f)*(.75f+.25f*Mathf.Sin(i*.7f));
      float y=ga.point.y-.18f;int course=0;
      while(y<ga.point.y+wallH)
      {
       float h=E(.28f,.40f),x=course%2==0?0:-.35f;
       while(x<1f){float len=E(.48f,.82f);if(!(y+h>ga.point.y+wallH-.2f&&rng.NextDouble()<.45))
        {var c=a+dir*(x+len*.5f);c.y=y+h;int v=rng.Next(set.GetLength(0));  // stone meshes: top at 0, bottom at -1
         combine.Add((new CombineInstance{mesh=set[v,0],transform=Matrix4x4.TRS(c,yaw*Quaternion.Euler(E(-1.5f,1.5f),E(-2,2),E(-1.5f,1.5f)),new Vector3(E(.62f,.78f),h-.02f,len-.03f))},v));blocks++;}
        x+=len;}
       y+=h;course++;
      }
     }
     for(int k=0;k<6;k++){var c=run[rng.Next(run.Count)];var up=-profile.Right(c.s)*meta.upSign;if(!Ground(c.p+up*E(.8f,2f)+Vector3.up*4,10,out var g))continue;int v=rng.Next(set.GetLength(0));
      combine.Add((new CombineInstance{mesh=set[v,0],transform=Matrix4x4.TRS(g.point+Vector3.up*.16f,Quaternion.Euler(E(-30,30),E(0,360),E(-30,30)),new Vector3(E(.5f,.7f),E(.28f,.38f),E(.5f,.8f)))},v));blocks++;}
     var lods=new Mesh[2];
     for(int l=0;l<2;l++){var m=new Mesh{indexFormat=IndexFormat.UInt32};m.CombineMeshes(combine.Select(x=>{var ci=x.ci;ci.mesh=set[x.v,l];return ci;}).ToArray(),true,true);m.RecalculateBounds();lods[l]=ArtMesh(m,A("Fortress_wall_LOD"+l));}
     var go=Lod293("Fortress_wall_remnant",sec,lods,wallStone,new[]{.10f,.012f},false);go.AddComponent<MeshCollider>().sharedMesh=lods[1];
    }
    notes.Add("fortress wall blocks="+blocks);
   }
   return notes.Count>0?"; "+string.Join(", ",notes):"";
  }
 }
}
